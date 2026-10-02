using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.25 문제마다 여러 갈래 해법 — 장면을 만들어 규칙이 맞물려 결과가 나오는지 본다.
public static partial class Program
{
    private static World WyShip(int seed, string ship, float hours = 1.5f)
    {
        bool off = MeetingSystem.MaidenOff;
        MeetingSystem.MaidenOff = true;
        var w = World.CreateDefault(seed, 0, ship);
        Run(w, SimTime.Hours(hours));
        MeetingSystem.MaidenOff = off;
        return w;
    }

    private static void WyPut(World w, CrewMember c, Cell at)
    {
        c.EndJob(w, ToilStatus.Interrupted);
        c.Position = at.Center;
        c.PreviousPosition = c.Position;
    }

    private static void WyStrip(World w, ItemKind k)
    {
        foreach (var f in w.Ship.Furniture) if (f.Storage is Inventory inv) { int n = inv.Count(k); if (n > 0) inv.Take(k, n); }
        foreach (var c in w.Crew) if (c.Carrying?.Kind == k) c.Carrying = null;
    }

    private static Cell? WyFloorNear(World w, Furniture f)
    {
        foreach (var s in f.UseSpots) if (w.Ship.IsOpenFloor(s)) return s;
        foreach (var d in Cell.Dirs8) if (w.Ship.IsOpenFloor(f.Cells[0] + d) && w.Ship.RoomAt(f.Cells[0] + d) == f.Room) return f.Cells[0] + d;
        return null;
    }

    private static string WyName(string id) => WaysTable.Get(id)?.Name ?? id;

    private static int RunWaysTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"여러 갈래 해법 점검 (v16.25) · 시드 {seed}\n");
        WaysSystem.Debug = Environment.GetEnvironmentVariable("WY_DEBUG") != null;
        static bool On(string n) => Environment.GetEnvironmentVariable("WY_ONLY") is not string o || o.Contains(n);
        var bad = WaysTable.Audit();
        int ways = WaysTable.All.Length;
        Check("표: 문제 20종마다 5갈래 이상 · 이름 · 그림", bad.Count == 0 && Enum.GetValues<Snag>().Length == 20, $"갈래 {ways}개 · " + string.Join(", ", bad.Take(6)));

        // 1) 같은 화재 (휴게실) — 시드 · 배 · 사람만 바꿔 돌리면 고르는 길이 갈린다
        if (On("same"))
        {
            string[] ships = { "Mirinae", "Hanbit", "Eunha", "Kestrel", "Cheonma" };
            var chosen = new List<string>();
            var lines = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                var w = WyShip(seed + i * 13, ships[i % ships.Length]);
                var hall = w.Ship.LiveRooms.FirstOrDefault(r => r.Kind == RoomType.Lounge) ?? w.Ship.LiveRooms.FirstOrDefault(r => r.Kind == RoomType.Mess);
                var at = hall?.Cells.Where(w.Ship.IsOpenFloor).Skip(hall.Cells.Count / 3).FirstOrDefault();
                if (hall == null || at is not Cell fireAt) { lines.Add($"{ships[i % 5]}: 휴게실 없음"); continue; }
                w.Fire.Ignite(fireAt, 0.45f);
                WayTry? first = null;
                for (int m = 0; m < 60 && first == null; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    first = w.Ways.Tries.FirstOrDefault(t => t.CrewId >= 0 && !t.Helper && w.Ways.Case(t.CaseId)?.Snag == Snag.Fire);
                }
                if (first == null) { lines.Add($"{ships[i % 5]}: 고른 사람 없음"); continue; }
                chosen.Add(first.WayId);
                var who = w.Crew.First(c => c.Id == first.CrewId);
                lines.Add($"{ships[i % 5]}/{seed + i * 13}: {who.Name} → {WyName(first.WayId)} ({first.Why}) [{first.Alts}]");
            }
            foreach (var l in lines) Console.WriteLine("     " + l);
            Check("같은 화재를 시드 · 배 · 사람만 바꾸면 고르는 해법이 3가지 이상 갈린다", chosen.Distinct().Count() >= 3, string.Join(" / ", chosen.Distinct().Select(WyName)));
        }
        var sealWorld = On("seal") || On("custom") ? WaysSeal(seed) : null;
        if (On("mattress")) WaysMattress(seed);
        if (On("trap")) WaysTrapped(seed);
        if (On("part")) WaysPart(seed);
        if (On("shock")) WaysShock(seed);
        if (sealWorld != null && On("custom")) WaysCustom(sealWorld);
        if (On("defy")) WaysDefy(seed);
        if (Environment.GetEnvironmentVariable("WY_FAST") != null) return _fails; // 고치는 중엔 결정론 · 성능을 건너뛴다
        // 결정론 · 저장 지문
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 (같은 시드 → 같은 지문)", a == b, $"{a:x8} / {b:x8}");
        }
        WaysPerf(seed);
        return _fails;
    }

    private static string WyTries(World w, WayCase? ck) => string.Join(" / ", w.Ways.Tries.Where(t => t.CaseId == ck?.Id).Select(t =>
        $"{w.Crew.FirstOrDefault(c => c.Id == t.CrewId)?.Name ?? (t.RobotId >= 0 ? "로봇" : "컴퓨터")} {WyName(t.WayId)} ({t.Why}){(t.Alts.Length > 0 ? $" [{t.Alts}]" : "")} {(t.State == 3 ? (t.Ok ? "됐다" : t.Result) : "중")}"));

    /// <summary>2) 소화기가 없는 방의 불 — 문을 닫아 산소를 끊어 끈다.</summary>
    private static World? WaysSeal(int seed)
    {
        var w = WyShip(seed, "Mirinae");
        WyNoFireGear(w);
        var room = w.Ship.LiveRooms.Where(r => r.Kind is RoomType.Storage or RoomType.Workshop && r.Doors.Count > 0 && !r.Doors.Any(d => d.IsExternal)).OrderBy(r => r.Volume).FirstOrDefault();
        if (room == null) { Check("소화기 없는 방의 불 — 장면", false, "창고 · 작업실 없음"); return null; }
        WyClearNear(w, room);
        {
            // 주컴퓨터 계획이 한 걸음으로 쓰는 갈래 목록 — 조건 · 시간 범위 · 부작용 · 나중 일
            var opts = w.Ways.Options(Snag.Fire, room);
            var ext = opts.FirstOrDefault(o => o.Id == "fire.ext");
            var seal = opts.FirstOrDefault(o => o.Id == "fire.seal");
            Check("계획용 갈래 목록: 소화기 없음 → 그 갈래는 못 쓰고 문 닫기는 된다 · 시간 범위 · 부작용 · 나중 일", opts.Count >= 5 && ext is { Ready: false } && seal is { Ready: true }
                && opts.All(o => o.MinLo <= o.MinHi) && opts.Any(o => o.Side.Length > 0) && opts.Any(o => o.Later.Length > 0) && opts.Any(o => !o.Ready && o.Need.Length > 0),
                string.Join(" · ", opts.Select(o => $"{o.Way.Name} {(o.Ready ? $"{o.MinLo:0}~{o.MinHi:0}분" : o.Need)}")));
        }
        foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).Take(2).ToList()) w.Fire.Ignite(c, 0.6f);
        float minO2 = 21f;
        WayTry? sealTry = null;
        long outAt = -1;
        for (int m = 0; m < 300 && outAt < 0; m++)
        {
            Run(w, SimTime.Minutes(1));
            minO2 = MathF.Min(minO2, room.Air.O2);
            if (WaysSystem.Debug && m % 20 == 0) Console.WriteLine($"   [봉쇄] {m}분 산소 {room.Air.O2:0.0} 불 {w.Fire.CountIn(room)}칸 최대 {w.Fire.Fires.Where(kv => w.Ship.RoomAt(kv.Key) == room).Select(kv => kv.Value).DefaultIfEmpty(0).Max():0.00} 문 {string.Join(",", room.Doors.Select(d => $"{d.Openness:0.0}{(d.Locked ? "잠" : "")}"))} 통풍 {room.VentOpen} 잠금 {room.Lockdown} 안 {w.Crew.Count(c => c.Room == room)}");
            sealTry ??= w.Ways.Tries.FirstOrDefault(t => t.WayId == "fire.seal" && w.Ways.Case(t.CaseId)?.RoomId == room.Id);
            if (w.Fire.CountIn(room) == 0 && m > 5) outAt = w.Tick;
        }
        Run(w, SimTime.Minutes(10));
        var ck = w.Ways.CaseFor(Snag.Fire, room);
        Console.WriteLine($"     {room.Name}: {WyTries(w, ck)} · 컴퓨터 안 {WyName(ck?.ComputerPick ?? "")}");
        Check("소화기가 없는 방의 불을 문을 닫아 산소를 끊어 끈다", sealTry != null && outAt >= 0 && minO2 < 12f && (ck?.SolvedBy == "fire.seal" || sealTry.Ok),
            $"닫은 사람 {w.Crew.FirstOrDefault(c => c.Id == sealTry?.CrewId)?.Name ?? "없음"} · 산소 최저 {minO2:0.0}kPa · 꺼짐 {(outAt >= 0 ? $"{(outAt - (sealTry?.Chosen ?? 0)) / (float)SimTime.Minutes(1):0}분" : "아직")} · 해결 {WyName(ck?.SolvedBy ?? "")}");
        var st = w.Ways.StepState(Snag.Fire, room, "fire.seal");
        Check("계획이 걸음을 확인할 수 있다 (해 봤다 · 잘됐다 · 풀렸다)", st.tried && st.ok && st.solved, $"{st}");
        Check("불이 꺼지면 문을 다시 열고 연대기에 한 줄이 남는다", !room.Lockdown && w.History.Events.Any(e => e.Text.Contains("문을 닫아 숨을 끊는다")), $"잠금 {room.Lockdown} · 통풍 {room.VentOpen}");
        return w;
    }

    /// <summary>3) 실링폼 · 금속판이 없을 때 — 매트리스로 구멍을 막고, 나중에 제대로 고친다.</summary>
    private static void WaysMattress(int seed)
    {
        var w = WyShip(seed, "Mirinae");
        WyStrip(w, ItemKind.Sealant); WyStrip(w, ItemKind.Plate);
        var room = w.Ship.LiveRooms.FirstOrDefault(r => r.Kind == RoomType.Quarters && r.Furniture.Any(f => f.Type == FurnitureType.Bed) && !r.Furniture.Any(f => f.Type is FurnitureType.Table)
            && w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r));
        if (room == null) { Check("매트리스 장면", false, "선체에 닿은 선실 없음"); return; }
        foreach (var a in w.Matter.Things.Where(a => a.Kind is ArticleKind.RubberMat or ArticleKind.PlasticCrate).ToList()) w.Matter.Remove(a);
        foreach (var r in w.Robots.Robots) r.Disabled = true; // 로봇은 다른 데서 바쁘다
        var wallCell = w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == room).Select(kv => kv.Key).First();
        Hull.Damage(w.Ship, wallCell, 0.85f);
        var wall = w.Ship.WallAt(wallCell)!;
        WayTry? plug = null;
        for (int m = 0; m < 120 && (plug == null || plug.State != 3); m++)
        {
            Run(w, SimTime.Minutes(1));
            plug ??= w.Ways.Tries.FirstOrDefault(t => t.CrewId >= 0 && w.Ways.Case(t.CaseId)?.Snag == Snag.Breach && t.Way.Fx == WayFx.Plug);
        }
        float q0 = wall.PatchQuality;
        var ck = w.Ways.CaseFor(Snag.Breach, room);
        Console.WriteLine($"     {room.Name} 파공 {wall.Breach:0.00}: {WyTries(w, ck)} · 컴퓨터 안 {WyName(ck?.ComputerPick ?? "")}");
        Check("실링폼 · 금속판이 없으면 매트리스로 구멍을 막는다", plug != null && plug.WayId == "breach.mattress" && plug.Ok && wall.Patched, $"{WyName(plug?.WayId ?? "")} · {plug?.Result} · 봉합 {wall.PatchQuality:0.00}");
        Run(w, SimTime.Hours(1));
        Check("매트리스 마개는 오래 못 간다 (보통 봉합보다 빨리 닳는다)", wall.PatchQuality < q0 - 0.05f && w.Ways.Plugs.Any(p => p.Wall == wallCell), $"{q0:0.00} → {wall.PatchQuality:0.00} (한 시간)");
        bool bare = w.Ways.Marks.Any(m => m.Look == WayLook.MattressPlug) && w.Ship.Furniture.Any(f => w.Ways.IsBare(f.Id));
        // 실링폼이 들어왔다 (교역 · 제작) → 누가 매트리스를 떼고 제대로 막는다
        var shelf = w.Ship.Containers.FirstOrDefault(f => f.Storage!.Accepts(ItemKind.Sealant) && f.Storage.Free >= 4);
        shelf?.Storage!.Add(ItemKind.Sealant, 4);
        long fixedAt = -1;
        for (int m = 0; m < 600 && fixedAt < 0; m++)
        {
            Run(w, SimTime.Minutes(1));
            if (w.Ways.Follows.Any(f => f.Kind == 0 && f.Done >= 0 && f.At == wallCell)) fixedAt = w.Tick;
        }
        var fol = w.Ways.Follows.FirstOrDefault(f => f.Kind == 0 && f.At == wallCell);
        Check("나중에 매트리스를 떼고 실링폼으로 제대로 막는다", fixedAt >= 0 && wall.Patched && wall.PatchQuality >= 0.5f && !w.Ways.Plugs.Any(p => p.Wall == wallCell && !p.Gone) && bare,
            $"{(fixedAt >= 0 ? $"{w.Crew.FirstOrDefault(c => c.Id == fol?.By)?.Name} · 품질 {wall.PatchQuality:0.00}" : "아직")} · 맨 침대 그림 {bare}");
    }

    /// <summary>4) 갇힘 — 정비 통로로 기어 나오거나 동료가 쇠지레로 문을 벌린다.</summary>
    private static void WaysTrapped(int seed)
    {
        var res = new List<string>();
        bool crawled = false, pried = false;
        for (int pass = 0; pass < 2; pass++)
        {
            var w = WyShip(seed + pass, pass == 0 ? "Hanbit" : "Mirinae");
            Room? room = null;
            if (pass == 0)
            {
                var hatch = w.Body.WallList.FirstOrDefault(wb => wb.Crawl);
                if (hatch != null) room = w.Ship.Rooms[hatch.CrawlA];
            }
            else room = w.Ship.LiveRooms.Where(r => r.Doors.Count(d => !d.IsExternal) == 1 && !r.Doors.Any(d => d.IsExternal) && r.Cells.Count >= 6
                && !w.Body.WallList.Any(wb => wb.Crawl && (wb.CrawlA == r.Id || wb.CrawlB == r.Id))).OrderBy(r => r.Id).FirstOrDefault();
            if (room == null) { res.Add(pass == 0 ? "통로 있는 방 없음" : "문 하나짜리 방 없음"); continue; }
            var v = w.Crew.Where(c => c.CanAct && c.Vitals.Injury < 0.3f).OrderBy(c => c.Id).First();
            WyPut(w, v, room.Cells.Where(w.Ship.IsOpenFloor).First());
            foreach (var d in room.Doors) if (!d.IsExternal) { d.Openness = 0f; d.Locked = true; d.Welded = true; d.Bent = 0.5f; }
            Run(w, 2);
            w.Ways.ScanNow();
            for (int m = 0; m < 180 && (v.Room == room || v.Room == null); m++) Run(w, SimTime.Minutes(1));
            var ck = w.Ways.CaseFor(Snag.Trapped);
            res.Add($"{room.Name}: {v.Name} {(v.Room == room ? "아직 갇힘" : $"나옴 → {v.Room?.Name}")} · {WyTries(w, ck)}");
            if (v.Room != room && w.Ways.Tries.Any(t => t.CaseId == ck?.Id && t.WayId == "trap.crawl" && t.Ok)) crawled = true;
            if (v.Room != room && w.Ways.Tries.Any(t => t.CaseId == ck?.Id && t.WayId == "trap.pry" && t.Ok))
            {
                pried = true;
                var ids = room.Doors.Select(d => d.Id).ToList();
                var fo = w.Ways.Follows.FirstOrDefault(f => f.Kind == 1 && ids.Contains(f.DoorId));
                bool stillBent = room.Doors.Any(d => d.Bent >= 0.5f) && fo != null && fo.Done < 0 && w.Ways.Marks.Any(m => m.Look == WayLook.PriedDoor);
                bool fixedElse = fo != null && fo.Done >= 0 && room.Doors.All(d => d.Bent < 0.3f) && !w.Ways.Marks.Any(m => m.Look == WayLook.PriedDoor && ids.Contains(m.DoorId));
                Check("쇠지레로 벌린 문은 문틀이 휘어 남고 나중에 펴는 일이 생긴다 (먼저 편 손이 있으면 그 일은 닫힌다)", stillBent || fixedElse,
                    $"휨 {string.Join(",", room.Doors.Select(d => d.Bent.ToString("0.00")))} · 뒷일 {string.Join(",", w.Ways.Follows.Where(f => f.Kind == 1).Select(f => f.Text + (f.Done >= 0 ? "(끝)" : "")))} · 그림 {w.Ways.Marks.Count(m => m.Look == WayLook.PriedDoor)}");
            }
        }
        foreach (var l in res) Console.WriteLine("     " + l);
        Check("갇힌 사람이 정비 통로로 기어 나오거나 동료가 쇠지레로 문을 벌린다", crawled || pried, $"통로 {crawled} · 쇠지레 {pried}");
    }

    /// <summary>5) 부품이 없다 — 다른 설비에서 떼어 오고, 그 설비가 멈춘 것을 누군가 알아챈다.</summary>
    private static void WaysPart(int seed)
    {
        var w = WyShip(seed, "Hanbit");
        w.Policies.Set("cannibalize", 2, "시험");
        foreach (var k in Enum.GetValues<ItemKind>()) if (ItemKinds.Tier(k) is ItemTier.Raw or ItemTier.Basic or ItemTier.General or ItemTier.Advanced) WyStrip(w, k); // 만들 재료도 없다
        foreach (var c in w.Crew) c.Kit.Clear();
        var fridge = w.Ship.Machines.FirstOrDefault(m => m.Body.Type == FurnitureType.Fridge && m.Faults.Count == 0);
        if (fridge == null) { Check("부품 장면", false, "냉장고 없음"); return; }
        fridge.Faults.Add(new Fault { Kind = FaultKind.CompressorFail, Since = w.Tick - SimTime.Hours(2) });
        Machine? donor = null;
        long noticedAt = -1;
        for (int m = 0; m < 720 && (noticedAt < 0 || fridge.Has(FaultKind.CompressorFail)); m++) // 알아챔 · 냉장고 살림 둘 다 기다린다
        {
            Run(w, SimTime.Minutes(1));
            donor ??= w.Ship.Machines.FirstOrDefault(x => x.Has(FaultKind.Stripped));
            if (donor != null && noticedAt < 0 && w.Ways.Noticed(donor.Body.Id)) noticedAt = w.Tick;
        }
        var ck = w.Ways.CaseFor(Snag.NoPart);
        Console.WriteLine($"     {fridge.Name}: 만들 수 있나 {w.Board.Obtainable(ItemKind.Motor)} · 재고 {w.Board.Have(ItemKind.Motor)} · {string.Join(",", Recipes.AllFor(ItemKind.Motor).Select(r => string.Join("+", r.Inputs.Select(x => $"{ItemKinds.Name(x.kind)}{w.Board.Have(x.kind)}"))))} · {WyTries(w, ck)}");
        var line = w.Log.Entries.LastOrDefault(e => e.Text.Contains("꺼져 있네"));
        Check("부품이 없어 다른 설비에서 떼어 오고 냉장고를 살린다", donor != null && !donor.Active && !fridge.Has(FaultKind.CompressorFail), $"떼어 낸 곳 {donor?.Name ?? "없음"} · 냉장고 {(fridge.Has(FaultKind.CompressorFail) ? "아직 고장" : "돌아감")}");
        Check("떼어 간 설비가 멈춘 것을 누군가 알아챈다", noticedAt >= 0 && line.Text != null, line.Text ?? "");
    }

    /// <summary>6) 전기 불에 물을 부으면 감전 (부작용) · 전원을 내린 사람은 멀쩡하다.</summary>
    private static void WaysShock(int seed)
    {
        int shocks = 0, safe = 0, runs = 0;
        var notes = new List<string>();
        for (int i = 0; i < 8 && (shocks == 0 || safe == 0); i++)
        {
            var w = WyShip(seed + 31 * i, i % 2 == 0 ? "Hanbit" : "Eunha");
            WyNoFireGear(w);
            var bed = w.Ship.Machines.Where(m => m.Body.Type == FurnitureType.GrowBed && m.Powered && !m.Body.Room.Detached).OrderBy(m => m.Body.Id).FirstOrDefault();
            if (bed == null || WyFloorNear(w, bed.Body) is not Cell at) continue;
            runs++;
            bool expert = shocks > 0;
            foreach (var c in w.Crew) c.SkillLevels[(int)Skill.Electrical] = expert ? 0.6f : 0.1f; // 처음엔 전기를 모르는 배, 감전을 본 뒤엔 아는 배
            var v = w.Crew.Where(c => c.CanAct).OrderBy(c => c.Id).Skip(i % 3).First();
            WyPut(w, v, at + new Cell(0, 1));
            foreach (var a in w.Matter.Things.Where(a => a.Kind is ArticleKind.Rug or ArticleKind.Towel or ArticleKind.PowderSack).ToList()) w.Matter.Remove(a);
            w.Fire.Ignite(at, 0.4f);
            Run(w, 3);
            w.Ways.ScanNow();
            WayTry? t = null;
            for (int m = 0; m < 90 && (t == null || t.State != 3); m++) { Run(w, SimTime.Minutes(1)); t ??= w.Ways.Tries.FirstOrDefault(x => x.CrewId >= 0 && x.Way.Fx is WayFx.Douse or WayFx.CutDouse); }
            if (t == null) { notes.Add($"물 안 씀 ({WyTries(w, w.Ways.CaseFor(Snag.Fire))})"); continue; }
            v = w.Crew.First(c => c.Id == t.CrewId);
            bool shocked = v.Vitals.InjuryCause?.Contains("감전") == true || t.Result.Contains("감전");
            if (t.Way.Fx == WayFx.Douse && shocked) shocks++;
            if (t.Way.Fx == WayFx.CutDouse && !shocked && t.Ok) safe++;
            notes.Add($"{v.Name}(전기 {v.SkillLevel(Skill.Electrical):0.0}): {WyName(t.WayId)} → {(shocked ? "감전" : "멀쩡")} ({t.Result}){(v.Lessons.Contains("ways:" + t.WayId) ? " · 교훈" : "")}");
        }
        foreach (var l in notes) Console.WriteLine("     " + l);
        Check("전기 불에 물을 부은 사람이 감전된다 (부작용 · 교훈)", shocks > 0, $"감전 {shocks}/{runs}");
        Check("전기를 아는 사람은 전원부터 내리고 물을 붓는다 — 감전 없음", safe > 0, $"멀쩡 {safe}");
    }

    /// <summary>7) 잘 통한 해법은 배의 관행이 되어 다음엔 더 빠르다 (2의 배에서 불을 두 번 더).</summary>
    private static void WaysCustom(World w)
    {
        var way = WaysTable.Get("fire.seal")!;
        float before = w.Ways.SpeedMul(null, way);
        var rooms = w.Ship.LiveRooms.Where(r => r.Kind is RoomType.Storage or RoomType.Workshop or RoomType.Lounge or RoomType.Medbay && r.Doors.Count > 0 && !r.Doors.Any(d => d.IsExternal) && w.Fire.CountIn(r) == 0).OrderByDescending(r => r.Id).Take(4).ToList();
        var whys = new List<string>();
        foreach (var room in rooms)
        {
            if (w.Ways.Tries.Any(t => t.Why.Contains("이 배에선"))) break;
            WyClearNear(w, room);
            foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).Take(2)) w.Fire.Ignite(c, 0.6f);
            for (int m = 0; m < 300 && w.Fire.CountIn(room) > 0; m++) Run(w, SimTime.Minutes(1));
            Run(w, SimTime.Minutes(30));
            var ck = w.Ways.CaseFor(Snag.Fire, room);
            whys.Add($"{room.Name}: {WyTries(w, ck)}");
        }
        var p = w.Ways.PracticeOrNull("fire.seal");
        float after = w.Ways.SpeedMul(null, way);
        foreach (var l in whys) Console.WriteLine("     " + l);
        Check("잘 통한 해법이 배의 관행이 되어 다음엔 더 빠르다", p != null && p.Custom && after < before - 0.05f, $"쓴 횟수 {p?.Uses} · 잘됨 {p?.Ok} · 관행 {p?.Custom} ({p?.Origin}) · 손빠르기 {before:0.00} → {after:0.00}");
        Check("다음 사람이 관행을 이유로 고른다", w.Ways.Tries.Any(t => t.Why.Contains("이 배에선")), string.Join(" | ", w.Ways.Tries.Where(t => t.WayId == "fire.seal").Select(t => t.Why).TakeLast(2)));
    }

    /// <summary>8) 컴퓨터 안을 두고 사람이 다른 길을 고르고 이유가 남는다.</summary>
    private static void WaysDefy(int seed)
    {
        var seen = new List<string>();
        bool any = false, timeline = false;
        string timelineNote = "";
        for (int i = 0; i < 6 && !any; i++)
        {
            // 로봇이 다 멈춘 배의 휴게실 불: 컴퓨터는 규정대로 소화기를 권하지만, 불 바로 옆에 러그를 든 사람이 있다
            var w = WyShip(seed + 7 * i, i % 2 == 0 ? "Mirinae" : "Hanbit");
            foreach (var r in w.Robots.Robots) r.Disabled = true;
            var hall = w.Ship.LiveRooms.FirstOrDefault(r => r.Kind == RoomType.Lounge) ?? w.Ship.LiveRooms.FirstOrDefault(r => r.Kind == RoomType.Mess);
            if (hall == null) continue;
            var cells = hall.Cells.Where(w.Ship.IsOpenFloor).ToList();
            var fireAt = cells[cells.Count / 2];
            var v = w.Crew.Where(c => c.CanAct).OrderByDescending(c => c.Traits.Bravery).Skip(i % 2).First();
            var stand = cells.OrderBy(c => Math.Abs(c.X - fireAt.X) + Math.Abs(c.Y - fireAt.Y)).First(c => c != fireAt);
            WyPut(w, v, stand);
            w.Matter.Add(ArticleKind.Rug, stand, "시험");
            w.Fire.Ignite(fireAt, 0.35f);
            for (int m = 0; m < 40 && !any; m++)
            {
                Run(w, SimTime.Minutes(1));
                var d = w.Ways.Tries.FirstOrDefault(t => t.Defied && t.Why.Length > 0);
                if (d != null)
                {
                    any = true;
                    seen.Add($"{w.Crew.First(c => c.Id == d.CrewId).Name}: {WyName(d.WayId)} — {d.Why} · 컴퓨터 일지에도 남음 {w.Log.Entries.Any(e => e.Text.Contains("다른 길을 골랐습니다"))}");
                    var kc = w.Ways.Case(d.CaseId);
                    var dec = w.Automation.Foresee.Timeline.FirstOrDefault(x => x.Id == kc?.Decision);
                    timeline = dec != null && dec.Options.Count >= 2;
                    timelineNote = dec == null ? "없음" : $"{dec.Title}: {string.Join(" / ", dec.Options.Select(o => o.Name))} → {dec.Pick.Name} ({dec.Reason})";
                }
            }
            var ck = w.Ways.CaseFor(Snag.Fire, hall);
            if (!any) seen.Add($"{hall.Name}: 컴퓨터 안 {WyName(ck?.ComputerPick ?? "")} · {WyTries(w, ck)}");
        }
        foreach (var l in seen) Console.WriteLine("     " + l);
        Check("컴퓨터 안 대신 사람이 다른 길을 고르고 이유가 남는다", any, seen.LastOrDefault() ?? "");
        Check("주컴퓨터가 갈래를 견줘 고른 안 · 이유를 타임라인에 남긴다 (나중에 채점)", timeline, timelineNote);
    }

    /// <summary>10) 성능: 30인 배 하루 (갈래 시스템 끔 / 켬).</summary>
    private static void WaysPerf(int seed)
    {
        double Day(bool off)
        {
            WaysSystem.Off = off;
            var w = World.CreateDefault(seed, 0, "Cheonma");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Run(w, SimTime.TicksPerDay);
            WaysSystem.Off = false;
            return sw.Elapsed.TotalSeconds;
        }
        double on1 = Day(false), off1 = Day(true), on2 = Day(false), off2 = Day(true);
        double on = Math.Min(on1, on2), off = Math.Min(off1, off2);
        Check("성능: 30인 배 하루 시간이 10% 안에서 같다", on <= off * 1.1, $"끔 {off:0.0}초 · 켬 {on:0.0}초 ({(on / off - 1) * 100:+0;-0}%)");
    }

    /// <summary>소화기 · 자동 소화 · 소방 로봇 · 진공 · 질식 · 호스를 모두 뺀 배.</summary>
    private static void WyNoFireGear(World w)
    {
        WyStrip(w, ItemKind.Extinguisher);
        w.Policies.Set("vacuumfire", 0, "시험");
        w.Policies.Set("inertfire", 0, "시험");
        w.Policies.Set("firemethod", 0, "시험");
        foreach (var r in w.Ship.Rooms) r.Suppression = false;
        foreach (var r in w.Robots.Robots) r.Foam = 0f;
    }

    /// <summary>그 방 근처의 천 · 물통 · 가루 포대를 치운다 (닫는 길만 남게).</summary>
    private static void WyClearNear(World w, Room room)
    {
        var c0 = room.Cells[room.Cells.Count / 2];
        foreach (var a in w.Matter.Things.Where(a => a.Kind is ArticleKind.Rug or ArticleKind.Towel or ArticleKind.WaterJug or ArticleKind.PowderSack && Math.Abs(a.At.X - c0.X) + Math.Abs(a.At.Y - c0.Y) < 50).ToList()) w.Matter.Remove(a);
    }
}
