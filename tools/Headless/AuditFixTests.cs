using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.24 점검 항해 고치기 1차 시험 (--auditfixtest)
//   AUDITFIX_PROBE=배,일수 를 주면 진단만: 사고마다 사람 체력이 어디까지 내려갔나.
public static partial class Program
{
    private static int RunAuditFixTest(int seed)
    {
        if (Environment.GetEnvironmentVariable("AUDITFIX_PROBE") is string probe) return AuditFixProbe(seed, probe);
        if (Environment.GetEnvironmentVariable("AUDITFIX_STALL") is string stall) return AuditFixStall(seed, stall);
        if (Environment.GetEnvironmentVariable("AUDITFIX_AUX") is string aux) return AuditFixAux(seed, aux);
        if (Environment.GetEnvironmentVariable("AUDITFIX_EXPOSE") is string expo) return AuditFixExpose(seed, expo);
        _fails = 0;
        Console.WriteLine($"점검 항해 고치기 시험 (v16.24) · 시드 {seed}\n");
        AfxHarm(seed);
        AfxAux(seed);
        AfxStall(seed);
        {
            Console.WriteLine("── 정전 속 캄캄한 방 · 결정론 ──");
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            Scenarios.Apply(w, "blackout", out _);
            int dark = 0;
            for (int m = 0; m < 8 * 60; m++) { Run(w, SimTime.Minutes(1)); dark += w.Ship.LiveRooms.Count(r => r.Dark); }
            Check("정전으로 캄캄한 방에서 발을 헛디딘다 (본 사람은 조심한다)", w.Body.Stats.DarkFalls > 0, $"넘어짐 {w.Body.Stats.DarkFalls} · 캄캄한 방-분 {dark}");
            uint H() { var x = World.CreateDefault(seed, 0, "Hanbit"); Run(x, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(x); }
            uint a = H(), b = H();
            Check("결정론 (같은 시드 두 번 같은 지문)", a == b, $"{a:x8} / {b:x8}");
        }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }

    private static Room AfxQuietRoom(World w) =>
        w.Ship.LiveRooms.Where(r => r.Type is RoomType.Storage or RoomType.Workshop or RoomType.Hydroponics && r.Cells.Count(w.Ship.IsOpenFloor) >= 4).OrderBy(r => r.Id).FirstOrDefault()
        ?? w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).OrderByDescending(r => r.Cells.Count).First();

    private static void AfxNextTo(World w, CrewMember h, CrewMember v)
    {
        var at = Cell.Dirs4.Select(d => v.Cell + d).FirstOrDefault(c => w.Ship.IsOpenFloor(c) && w.Ship.RoomAt(c) == v.Room);
        h.Position = (at == default ? v.Cell : at).Center; h.PreviousPosition = h.Position;
    }

    // ③ 둘이 해야 하는 일 (문 구동기): 잡아 줄 사람이 안 오면 손을 떼고 (이유를 남기고) · 두 번 허탕이면 혼자 지그로
    private static void AfxStall(int seed)
    {
        Console.WriteLine("── 둘이 하는 일 · 잡아 줄 사람이 없다 ──");
        var w = DayOne(seed, "Kestrel"); w.CrewCanDie = true;
        var d = w.Ship.Doors.Where(x => !x.IsExternal && x.RoomA != null && x.RoomB != null && !x.MotorBroken).OrderBy(x => x.Id).First();
        var solo = w.Crew.Where(c => c.CanAct).OrderByDescending(c => c.SkillLevel(Skill.Engineering)).First();
        foreach (var o in w.Crew.Where(o => o != solo)) o.Away = true; // 다른 사람은 모두 원정 · 배 밖
        w.Fixtures.BreakDoor(d, "구동기 모터 탐");
        w.Board.RequestScan();
        long held = 0, maxHeld = 0, firstHold = -1;
        int releases = 0;
        CrewMember? last = null;
        WorkOrder? order = null;
        for (int m = 0; m < 14 * 60 && d.MotorBroken; m++)
        {
            Run(w, SimTime.Minutes(1));
            order ??= w.Board.All.FirstOrDefault(o => o.Kind == WorkKind.RepairDoor && !o.Closed);
            if (order == null) continue;
            if (order.BlockedReason?.Contains("잡아 줄 사람") == true && firstHold < 0) firstHold = m;
            if (order.Assignee == null && last != null) releases++;
            last = order.Assignee;
            held = order.Assignee != null && order.Progress < 0.01f ? held + 1 : 0;
            maxHeld = Math.Max(maxHeld, held);
        }
        Check("잡아 줄 사람이 없으면 손을 떼고 이유를 남긴다 (보류 · 맡은 사람 없음)", firstHold >= 0 && releases >= 1, $"첫 보류 {firstHold}분 · 손 뗌 {releases}번 · 사유 {order?.BlockedReason}");
        Check("진척 없이 맡고만 있는 시간이 3시간을 넘지 않는다", maxHeld < 180, $"가장 길게 {maxHeld}분");
        Check("두 번 허탕 친 뒤에는 혼자 지그로 물려 끝낸다", !d.MotorBroken, $"문 {(d.MotorBroken ? "아직 고장" : "고침")} · 혼자 {w.Coop.Stats.Solos}번 · 보류 {w.Coop.Stats.Holds}번");
    }

    // ② 배터리가 바닥난 채 전기가 모자라다 → 보조 발전기 (컴퓨터가 원격으로 · 못 하면 사람이 손으로)
    private static void AfxAux(int seed)
    {
        Console.WriteLine("── 보조 발전기: 배터리가 바닥나 필수 방이 꺼졌다 ──");
        foreach (int mode in new[] { 0, 1, 2 })
        {
            bool remote = mode != 1;
            var w = DayOne(seed, "gen:research:linear:8:5"); w.CrewCanDie = true;
            var p = w.Power;
            var aux = w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault();
            if (aux == null) { Check("보조 발전기가 있다", false); return; }
            if (!remote) aux.Room.DataLinked = false; // 데이터선이 끊긴 방 — 컴퓨터가 원격으로 못 켠다
            if (mode == 2) // 원자로는 돌지만 (정비 중 절반 출력 · 냉각 펌프 하나 섬) 출력이 모자라다
            {
                PowerGrid.ReactorMaxKw = 18f; // 낡은 노심 — 돌기는 돌지만 모자라다 (시험 끝에 되돌린다)
            }
            else p.Heat(2000f);
            for (int i = 0; i < 6; i++) { p.BatteryCharge = mode == 2 && i > 0 ? p.BatteryCharge : 0f; Run(w, SimTime.Minutes(1)); } // 배터리가 바닥났다 (흐름도 0)
            var ess = w.Ship.LiveRooms.Where(r => r.Type is RoomType.LifeSupport or RoomType.Bridge or RoomType.Medbay).ToList();
            int darkMin = 0, at = -1;
            string seen = "";
            for (int m = 0; m < (mode == 2 ? 150 : 60) && at < 0; m++)
            {
                Run(w, SimTime.Minutes(1));
                if (ess.Any(r => !r.Powered)) darkMin++;
                if (p.AuxRunning) at = m;
                if (m == 3 || p.DeficitSince >= 0 && !seen.StartsWith("모자람")) seen = (p.DeficitSince >= 0 ? "모자람 " : "") + $"계획 {w.Automation.TriageOrNull?.Plan} · 원자로 {p.ReactorOnline} {p.ReactorLimit:0.#}kW · 배터리 {p.BatteryPercent * 100:0}% · 공급 {p.Delivered:0.#}/{p.Demand:0.#}kW · 주문 {string.Join(",", w.Board.Open.Where(o => o.Kind == WorkKind.StartAux).Select(o => o.Assignee?.Name ?? "-"))}";
            }
            PowerGrid.ReactorMaxKw = 48f;
            Check(mode == 0 ? "원자로 정지 · 배터리 바닥 — 컴퓨터가 원격으로 보조 발전기를 켠다" : mode == 1 ? "원격이 안 되는 발전기 — 사람이 손으로 켠다" : "원자로는 돌지만 출력이 모자라고 배터리가 바닥 — 보조 발전기를 켠다",
                at >= 0 && at <= (mode == 2 ? 150 : remote ? 10 : 45), $"{(at >= 0 ? $"{at + 6}분 만에" : "안 켬")} · 필수 방 정전 {darkMin}분 · {seen}");
        }
    }

    // ① 큰 상처 뒤 — 출혈 · 심정지 · 불붙는 순간 · 컴퓨터가 부른다
    private static void AfxHarm(int seed)
    {
        Console.WriteLine("── 큰 상처 뒤: 출혈 · 심정지 · 불붙는 순간 ──");
        // 곁의 사람이 눌러 준다
        {
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            var room = AfxQuietRoom(w);
            var v = w.Crew.First(c => c.CanAct); var h = w.Crew.First(c => c.CanAct && c != v);
            Put(w, v, room); Run(w, 1); AfxNextTo(w, h, v); h.EndJob(w, ToilStatus.Interrupted);
            v.Vitals.Health = 0.55f; NeedsSystem.AddInjury(v.Vitals, 0.45f, "운석 파편");
            Run(w, World.SystemInterval * 2);
            var t = w.Casualty.Of(v);
            Check("파편에 크게 다치면 피가 난다 (출혈)", t is { Kind: TraumaKind.Bleed } && t.Rate > 0.1f, t == null ? "없음" : $"{TraumaKind.Bleed} {t.Rate:0.00}/시간 · {t.Cause}");
            var spot = v.Cell;
            for (int i = 0; i < SimTime.Minutes(5); i++) { if (i % 20 == 0) { v.Position = spot.Center; AfxNextTo(w, h, v); h.EndJob(w, ToilStatus.Interrupted); } w.Step(); }
            var done = w.Casualty.Done.LastOrDefault(x => x.CrewId == v.Id);
            Check("곁에 있던 사람이 상처를 눌러 피가 멎는다 · 고마움이 남는다", w.Casualty.Of(v) == null && done != null && done.Outcome.Contains(h.Name) && v.Memory.Marks.Any(m => m.Text.Contains("살렸다")) && !v.Dead,
                $"{done?.Outcome ?? "아직"} · 체력 {v.Vitals.Health:0.00}" + (w.Casualty.Of(v) is Trauma tt ? $" · 열림 {tt.Kind} {tt.Rate:0.000} 도움 {tt.Helper} 부름 {tt.Paged} 스스로 {tt.SelfPressed} 방 {v.Room?.Name} 정신 {v.CanAct}" : $" · 닫힘 {w.Casualty.Done.LastOrDefault(x => x.CrewId == v.Id)?.Outcome}"));
        }
        // 혼자 · 컴퓨터가 못 보는 방 · 다른 사람은 모두 배 밖 → 출혈로 숨진다 (까닭이 남는다)
        {
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            var room = AfxQuietRoom(w);
            var v = w.Crew.Where(c => c.CanAct).OrderBy(c => c.Role == CrewRole.Medic ? 1 : 0).ThenBy(c => c.SkillLevel(Skill.Medicine)).First(); // 의료를 모르는 사람
            foreach (var o in w.Crew.Where(o => o != v)) o.Away = true;
            room.DataLinked = false;
            Put(w, v, room); Run(w, 1);
            v.Vitals.Health = 0.1f; /* 맞고 정신을 잃었다 */ NeedsSystem.AddInjury(v.Vitals, 0.5f, "운석 파편");
            string trace = "";
            for (int i = 0; i < 12 * 60 && !v.Dead; i++) { Run(w, SimTime.Minutes(1)); if (i % 20 == 0 && i < 300) trace += $" {i}:{v.Vitals.Health:0.00}/{(w.Casualty.Of(v)?.Rate ?? -1):0.00}"; }

            Run(w, World.SystemInterval * 2);
            var hist = w.History.Events.LastOrDefault(e => e.Kind == HistoryKind.Death && e.Text.Contains("숨졌다 —"));
            Check("맞고 쓰러진 채 혼자 · 올 사람이 없으면 출혈로 숨진다 (사인 · 왜 늦었는지가 기록에)", v.Dead && (v.Vitals.InjuryCause ?? "").Contains("출혈") && hist != null && hist.Text.Contains("출혈"),
                $"죽음 {v.Dead} · 사인 {v.Vitals.InjuryCause} · 역사 {hist?.Text} · 체력 {v.Vitals.Health:0.00}" + (w.Casualty.Of(v) is Trauma tt ? $" · 열림 {tt.Kind} {tt.Rate:0.000} 도움 {tt.Helper} 부름 {tt.Paged} 스스로 {tt.SelfPressed} 방 {v.Room?.Name} 정신 {v.CanAct}" : $" · 닫힘 {w.Casualty.Done.LastOrDefault(x => x.CrewId == v.Id)?.Outcome}"));
        }
        // 컴퓨터: 데이터선이 닿는 방이면 생체 신호로 알아채 가장 가까운 사람을 부른다 → 와서 멎게 한다
        {
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            var room = AfxQuietRoom(w);
            var v = w.Crew.First(c => c.CanAct);
            Put(w, v, room); Run(w, 1);
            foreach (var o in w.Crew.Where(o => o != v && o.Room == room)) { o.EndJob(w, ToilStatus.Interrupted); o.Position = w.Ship.LiveRooms.First(r => r != room && r.Type == RoomType.Corridor).Cells.First(w.Ship.IsOpenFloor).Center; o.PreviousPosition = o.Position; }
            int paged = w.Casualty.Paged;
            v.Vitals.Health = 0.5f; NeedsSystem.AddInjury(v.Vitals, 0.5f, "작업 사고");
            Run(w, World.SystemInterval * 2);
            for (int i = 0; i < 90 && w.Casualty.Of(v) != null; i++) Run(w, SimTime.Minutes(1));
            var done = w.Casualty.Done.LastOrDefault(x => x.CrewId == v.Id);
            Check("주 컴퓨터가 생체 신호로 알아채 가장 가까운 사람을 부른다", w.Casualty.Paged > paged && w.Automation.Book.Acts.Any(a => a.Key == "page"), $"부름 {w.Casualty.Paged - paged}");
            Check("불려 온 사람이 피를 멎게 한다 (살았다)", done != null && !v.Dead && !done.Outcome.Contains("숨"), (done?.Outcome ?? "아직 피가 난다") + (w.Casualty.Of(v) is Trauma tt ? $" · 열림 {tt.Kind} {tt.Rate:0.000} 도움 {tt.Helper} 부름 {tt.Paged} 스스로 {tt.SelfPressed} 방 {v.Room?.Name} 정신 {v.CanAct}" : $" · 닫힘 {w.Casualty.Done.LastOrDefault(x => x.CrewId == v.Id)?.Outcome}"));
        }
        // 심정지: 곁에서 가슴을 누르면 가끔 살아난다 · 혼자면 몇 분 안에 숨진다
        {
            int revived = 0, alone = 0, tries = 0;
            for (int k = 0; k < 4; k++)
            {
                var w = DayOne(seed + k, "Mirinae"); w.CrewCanDie = true;
                var room = AfxQuietRoom(w);
                var v = w.Crew.First(c => c.CanAct); var h = w.Crew.First(c => c.CanAct && c != v);
                Put(w, v, room); Run(w, 1); AfxNextTo(w, h, v); h.EndJob(w, ToilStatus.Interrupted);
                w.Casualty.Inflict(v, TraumaKind.Arrest, 0.6f, "배전반 감전");
                for (int i = 0; i < SimTime.Minutes(15) && w.Casualty.Of(v) != null; i++) { if (i % 20 == 0) { AfxNextTo(w, h, v); h.EndJob(w, ToilStatus.Interrupted); } w.Step(); }
                tries++;
                if (!v.Dead && w.Casualty.Of(v) == null) revived++;
                var w2 = DayOne(seed + k, "Mirinae"); w2.CrewCanDie = true;
                var v2 = w2.Crew.First(c => c.CanAct);
                foreach (var o in w2.Crew.Where(o => o != v2)) o.Away = true;
                Put(w2, v2, AfxQuietRoom(w2)); Run(w2, 1);
                w2.Casualty.Inflict(v2, TraumaKind.Arrest, 0.6f, "배전반 감전");
                Run(w2, SimTime.Minutes(25));
                if (v2.Dead && (v2.Vitals.InjuryCause ?? "").Contains("심정지")) alone++;
            }
            Check("심정지 — 곁에서 가슴을 누르면 살아나기도 한다", revived >= 1, $"{revived}/{tries}");
            Check("심정지 — 혼자면 몇 분 안에 숨진다 (사인: 심정지)", alone == tries, $"{alone}/{tries}");
        }
        // 길이 끝까지 가나: 갇힌 사람 — 짙은 연기 · 진공 (아무도 못 온다)
        foreach (string kind in new[] { "연기", "진공" })
        {
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            var room = AfxQuietRoom(w);
            var v = w.Crew.First(c => c.CanAct);
            foreach (var o in w.Crew.Where(o => o != v)) o.Away = true;
            Put(w, v, room); Run(w, 1);
            var spot = v.Cell;
            long downAt = -1, deadAt = -1;
            for (int m = 0; m < 240 && deadAt < 0; m++)
            {
                for (int k = 0; k < SimTime.Minutes(1); k++)
                {
                    if (kind == "연기") { room.Air.Smoke = 0.9f; room.Air.CO = MathF.Max(room.Air.CO, 0.2f); } else { room.Air.N2 = 1.5f; room.Air.O2 = 0.4f; room.Air.CO2 = 0.01f; }
                    if (!v.Down) { v.Position = spot.Center; v.PreviousPosition = v.Position; }
                    w.Step();
                }
                if (v.Down && downAt < 0) downAt = m;
                if (v.Dead) deadAt = m;
            }
            Check(kind == "연기" ? "짙은 연기에 갇힌 사람은 한두 시간 안에 쓰러지고 끝내 숨진다" : "진공에 갇힌 사람은 몇 분 만에 쓰러지고 곧 숨진다",
                downAt >= 0 && deadAt >= 0 && (kind == "연기" ? downAt <= 120 : downAt <= 6 && deadAt <= 25), $"쓰러짐 {downAt}분 · 숨짐 {deadAt}분 · 사인 {v.Vitals.InjuryCause}");
        }
        // 불붙는 순간 곁에 있던 사람 (잠든 사람은 더 덴다)
        {
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            var room = AfxQuietRoom(w);
            var v = w.Crew.First(c => c.CanAct);
            Put(w, v, room); Run(w, 1);
            var at = Cell.Dirs4.Select(d => v.Cell + d).First(c => w.Ship.IsOpenFloor(c) && w.Ship.RoomAt(c) == room);
            float inj = v.Vitals.Injury;
            int fl = w.Casualty.Flashes;
            w.Fire.Ignite(at, 0.5f);
            Run(w, World.SystemInterval * 2);
            Check("불이 일어난 자리 곁의 사람은 덴다 (옮겨붙은 화상)", w.Casualty.Flashes > fl && v.Vitals.Injury > inj && v.Vitals.Wounds.Any(x => x.Kind == WoundKind.Burn), $"부상 {inj:0.00} → {v.Vitals.Injury:0.00} · {v.Vitals.InjuryCause}");
        }
    }

    private static int AuditFixProbe(int seed, string spec)
    {
        var parts = spec.Split(',');
        Storyteller.PersonaValue = 1f; Storyteller.LevelValue = 3f;
        var w = World.CreateDefault(seed, 0, parts[0]);
        var seen = new Dictionary<int, ScaleCase>();
        w.CrewCanDie = true; w.Log.Capacity = 4000;
        float days = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 3f;
        long total = (long)(days * SimTime.TicksPerDay);
        var minHp = new Dictionary<int, (float hp, float ox, string who, string room, long t)>();
        var caseMin = new Dictionary<int, (float hp, float ox, string who)>();
        for (long t = 1; t <= total; t++)
        {
            w.Step();
            if (t % SimTime.Minutes(1) != 0) continue;
            foreach (var k in w.Scale.Cases) seen[k.Id] = k;
            var open = w.Scale.Cases.Where(k => k.End < 0 || w.Tick - k.End < SimTime.Minutes(30)).ToList();
            foreach (var c in w.Crew)
            {
                if (c.Dead) continue;
                float hp = c.Vitals.Health, ox = c.Vitals.Oxygen;
                if (!minHp.TryGetValue(c.Id, out var m) || hp < m.hp) minHp[c.Id] = (hp, ox, c.Name, c.Room?.Name ?? "밖", w.Tick);
                foreach (var k in open)
                {
                    bool hit = k.CrewId == c.Id || (c.Room != null && (k.RoomId == c.Room.Id || k.Rooms.Contains(c.Room.Id))) || k.Peak >= IncidentScale.Ship;
                    if (!hit) continue;
                    if (!caseMin.TryGetValue(k.Id, out var cm) || hp < cm.hp) caseMin[k.Id] = (hp, MathF.Min(ox, cm.hp == 0 ? 1f : cm.ox), c.Name);
                }
            }
        }
        string H(long t) => $"{(t - SimTime.Hours(7)) / (float)SimTime.TicksPerHour:0.0}h";
        foreach (var k in seen.Values.OrderBy(k => k.Id).Where(k => k.Peak >= IncidentScale.Room))
        {
            var cm = caseMin.GetValueOrDefault(k.Id, (1f, 1f, "-"));
            float hrs = ((k.End < 0 ? w.Tick : k.End) - k.Start) / (float)SimTime.TicksPerHour;
            Console.WriteLine($"[{k.Peak}] {H(k.Start)} {k.Key} {k.Name} · {hrs:0.0}h · 방 {k.RoomId}+{k.Rooms.Count} · 최저 체력 {cm.Item1:0.00} 산소 {cm.Item2:0.00} ({cm.Item3})");
        }
        foreach (var kv in minHp.OrderBy(x => x.Value.hp)) Console.WriteLine($"  {kv.Value.who}: 최저 {kv.Value.hp:0.00} 산소 {kv.Value.ox:0.00} @ {kv.Value.room} {H(kv.Value.t)}");
        foreach (var c in w.Crew)
            foreach (var wd in c.Vitals.Wounds) Console.WriteLine($"  상처 {c.Name}: {wd.Cause} {wd.Kind} {wd.Weight:0.00}");
        Console.WriteLine($"사망 {w.Crew.Count(c => c.Dead)} · 쓰러짐 {w.Crew.Sum(c => c.Stats.TimesDown)}");
        return 0;
    }

    private static int AuditFixStall(int seed, string spec)
    {
        var parts = spec.Split(',');
        Storyteller.PersonaValue = 1f; Storyteller.LevelValue = 3f;
        var w = World.CreateDefault(seed, 0, parts[0]);
        w.CrewCanDie = true; w.Log.Capacity = 4000;
        float days = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 3f;
        long total = (long)(days * SimTime.TicksPerDay);
        var claims = new Dictionary<int, (int who, long since, float prog)>();
        var shown = new HashSet<int>();
        string H(long t) => $"{(t - SimTime.Hours(7)) / (float)SimTime.TicksPerHour:0.0}h";
        for (long t = 1; t <= total; t++)
        {
            w.Step();
            if (t % SimTime.Minutes(10) != 0) continue;
            foreach (var o in w.Board.All)
            {
                if (o.Closed || o.Assignee == null) { claims.Remove(o.Id); continue; }
                if (!claims.TryGetValue(o.Id, out var c) || c.who != o.Assignee.Id || MathF.Abs(c.prog - o.Progress) > 1e-4f) { claims[o.Id] = (o.Assignee.Id, w.Tick, o.Progress); continue; }
                float hours = (w.Tick - c.since) / (float)SimTime.TicksPerHour;
                if (hours < 1f || (hours > 1.2f && hours < 3f) || (hours > 3.2f)) continue;
                var a = o.Assignee;
                Console.WriteLine($"{H(w.Tick)} [{hours:0.0}h] #{o.Id} {o.Title} 진척 {o.Progress:0.00} 막힘 {(o.BlockedUntil > w.Tick ? o.BlockedReason : "-")} · {a.Name}: 자세 {a.Pose} 일 {a.Job?.Label ?? "-"} (주문 {(a.Job?.Order?.Id.ToString() ?? "-")}) 방 {a.Room?.Name} 할수 {a.CanAct} 깨어 {a.IsAwake}");
            }
        }
        return 0;
    }

    // 노출 진단: 사람이 위험한 공기 · 불 곁에 얼마나 있었나 (분)
    private static int AuditFixExpose(int seed, string spec)
    {
        var parts = spec.Split(',');
        Storyteller.PersonaValue = 1f; Storyteller.LevelValue = 3f;
        var w = World.CreateDefault(seed, 0, parts[0]);
        w.CrewCanDie = true; w.Log.Capacity = 4000;
        float days = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        long total = (long)(days * SimTime.TicksPerDay);
        var ex = new Dictionary<string, int>();
        void A(string k) => ex[k] = ex.GetValueOrDefault(k) + 1;
        float minOx = 1f, minHp = 1f; var injPrev = new Dictionary<int, float>();
        for (long t = 1; t <= total; t++)
        {
            w.Step();
            if (t % SimTime.Minutes(1) != 0) continue;
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Room == null) continue;
                var a = c.Room.Air;
                string z = c.Pose == Pose.Sleeping ? "잠" : "깸";
                if (a.Pressure < 60f) A($"저압<60 {z}{(c.Suit != null ? " 우주복" : "")}");
                if (a.Pressure < 25f) A($"진공<25 {z}{(c.Suit != null ? " 우주복" : "")}");
                if (a.Smoke > 0.4f) A($"연기>0.4 {z}");
                if (a.CO > 0.1f) A($"CO>0.1 {z}");
                if (a.O2 < 15f) A($"산소<15 {z}");
                if (a.Temperature > 45f) A($"열>45 {z}");
                foreach (var (cell, inten) in w.Fire.Fires)
                    if ((c.Position - cell.Center).LengthSquared() < 1.6f * 1.6f) { A($"불 곁 {z}"); break; }
                minOx = MathF.Min(minOx, c.Vitals.Oxygen); minHp = MathF.Min(minHp, c.Vitals.Health);
                float inj = c.Vitals.Injury;
                if (inj > injPrev.GetValueOrDefault(c.Id) + 0.04f)
                    Console.WriteLine($"  {(w.Tick - SimTime.Hours(7)) / (float)SimTime.TicksPerHour:0.0}h 다침 {c.Name} +{inj - injPrev.GetValueOrDefault(c.Id):0.00} → 부상 {inj:0.00} 체력 {c.Vitals.Health:0.00} ({c.Vitals.InjuryCause}) @ {c.Room.Name} {z}");
                injPrev[c.Id] = inj;
            }
            foreach (var r in w.Ship.Rooms)
            {
                if (r.Detached) continue;
                int n = w.Crew.Count(c => !c.Dead && c.Room == r);
                if (r.Air.Pressure < 60f) A($"[방] 저압<60 (사람 {Math.Min(n, 2)})");
                if (r.Air.Smoke > 0.4f) A($"[방] 연기>0.4 (사람 {Math.Min(n, 2)})");
                if (r.Air.CO > 0.1f) A($"[방] CO>0.1 (사람 {Math.Min(n, 2)})");
                if (r.Air.O2 < 15f) A($"[방] 산소<15 (사람 {Math.Min(n, 2)})");
            }
            if (w.Fire.Fires.Count > 0) A($"[불] 칸 {Math.Min(w.Fire.Fires.Count, 5)}");
        }
        foreach (var kv in ex.OrderBy(k => k.Key, StringComparer.Ordinal)) Console.WriteLine($"  {kv.Key}: {kv.Value}분");
        Console.WriteLine($"최저 혈중 산소 {minOx:0.00} · 최저 체력 {minHp:0.00} · 쓰러짐 {w.Crew.Sum(c => c.Stats.TimesDown)} · 사망 {w.Crew.Count(c => c.Dead)}");
        var cs = w.Casualty; Console.WriteLine($"출혈 {cs.Bleeds} · 감전 {cs.Shocks} · 심정지 {cs.Arrests} · 불붙음 {cs.Flashes} · 멎음 {cs.Stopped} · 살림 {cs.Revived} · 부름 {cs.Paged} · 숨짐 {cs.Died}"); foreach (var t in cs.Done) Console.WriteLine($"  {t.Kind} {t.Cause} → {t.Outcome}");
        return 0;
    }

    private static int AuditFixAux(int seed, string spec)
    {
        var parts = spec.Split(',');
        Storyteller.PersonaValue = 1f; Storyteller.LevelValue = 3f;
        var w = World.CreateDefault(seed, 0, parts[0]);
        w.CrewCanDie = true; w.Log.Capacity = 4000;
        float from = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), to = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        var ess = w.Ship.Rooms.Where(r => !r.Detached && (r.Type is RoomType.LifeSupport or RoomType.Bridge or RoomType.Medbay || r.Furniture.Any(f => f.Type == FurnitureType.MainComputer))).ToList();
        long t0 = w.Tick, end = t0 + (long)(to * SimTime.TicksPerHour), start = t0 + (long)(from * SimTime.TicksPerHour); Console.WriteLine($"t0 {t0}");
        string H(long t) => $"{(t - SimTime.Hours(7)) / (float)SimTime.TicksPerHour:0.00}h";
        while (w.Tick < end)
        {
            w.Step();
            if (w.Tick < start || w.Tick % SimTime.Minutes(2) != 0) continue;
            var p = w.Power;
            var dark = ess.Where(r => !r.Powered && !r.Detached).ToList();
            if (dark.Count == 0) continue;
            var auxF = w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault(f => !f.Room.Detached);
            var tr = w.Automation.TriageOrNull;
            Console.WriteLine($"{H(w.Tick)} 어두운: {string.Join(",", dark.Select(r => $"{r.Name}(회로{r.Circuit} 급전{p.CircuitFed[Math.Clamp(r.Circuit,0,3)]} 끊{r.PowerCut} 차{r.BreakerOff} 연{r.PowerLinked})"))} · 원자로 {p.ReactorOnline} 배터리 {p.BatteryPercent:0.00} 흐름 {p.BatteryFlow:0.0} 수요 {p.Demand:0.0}/{p.Delivered:0.0} 부족 {p.DeficitSince >= 0} · 보조 {(auxF == null ? "없음" : $"{auxF.Room.Name} 돌{p.AuxRunning} 연료{p.AuxFuel:0} 멈춤{auxF.Machine?.Stopped} 데이터{auxF.Room.DataLinked} 고장{auxF.Machine?.Faults.Count}")} 손필요 {tr?.AuxNeedsHands} · 주문 {string.Join(",", w.Board.Open.Where(o => o.Kind is WorkKind.StartAux or WorkKind.ResetBreaker or WorkKind.BreakerOn or WorkKind.RestoreCircuit).Select(o => o.Title + "/" + (o.Assignee?.Name ?? "-")))}");
        }
        return 0;
    }
}
