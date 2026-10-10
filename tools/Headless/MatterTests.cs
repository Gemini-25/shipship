using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ShipSim.Core;

// v16.4 재질 · 원소 · 물리 한 벌 — 표 전체 자동 시험 · 규칙이 보이는 대로 동작하는지 · 검증 장면을 헤드리스에서 실제로 일으킨다
public static partial class Program
{
    private static List<Cell> MOpen(World w, Room r) => r.Cells
        .Where(c => w.Ship.IsOpenFloor(c) && w.Ship.DoorAt(c) == null && !w.Matter.Any(c) && !w.Portable.Occupied(c) && !r.Doors.Any(d => (d.Cell.Center - c.Center).LengthSquared() < 1.1f))
        .OrderBy(c => (c.Center - r.Center).LengthSquared()).ThenBy(c => c.X * 1000 + c.Y).ToList();

    /// <summary>네 이웃이 모두 빈 바닥인 칸 (히터를 가운데 두고 둘레에 물건을 놓는다).</summary>
    private static Cell? MHub(World w, Room r)
    {
        var open = new HashSet<Cell>(MOpen(w, r));
        foreach (var c in MOpen(w, r)) if (Cell.Dirs8.All(d => open.Contains(c + d))) return c;
        return null;
    }

    /// <summary>둘레 두 칸까지 빈 바닥인 칸 (폭발 · 밀기 시험).</summary>
    private static (Room room, Cell hub)? MWide(World w, Func<Room, bool> f)
    {
        foreach (var r in w.Ship.LiveRooms.Where(r => !r.Detached && r.Kind != RoomType.Corridor && f(r)).OrderByDescending(r => r.Cells.Count).ThenBy(r => r.Id))
        {
            var open = new HashSet<Cell>(MOpen(w, r));
            foreach (var c in MOpen(w, r))
            {
                bool ok = true;
                for (int dy = -2; dy <= 2 && ok; dy++) for (int dx = -2; dx <= 2 && ok; dx++) ok = open.Contains(new Cell(c.X + dx, c.Y + dy));
                if (ok) return (r, c);
            }
        }
        return null;
    }

    private static Room MRoom(World w, Func<Room, bool> f) => w.Ship.LiveRooms.Where(r => !r.Detached && r.Powered && r.Kind != RoomType.Corridor && f(r) && MHub(w, r) != null)
        .OrderByDescending(r => MOpen(w, r).Count).ThenBy(r => r.Id).First();

    private static PortableDevice MHeater(World w, Room room, Cell at)
    {
        var d = w.Portable.Devices.First(x => x.Kind == PortableKind.Heater && x.Stored && !x.Broken);
        w.Portable.PlaceNow(d, at, null, "hold", outlet: room);
        return d;
    }

    private static World MFresh(int seed)
    {
        var w = World.CreateDefault(seed, 0, "Hanbit");
        Run(w, SimTime.Minutes(3));
        w.Matter.QuietUntil = long.MaxValue; // 이 시험은 사람이 미리 치우지 않는다
        return w;
    }

    private static int RunMatterTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"재질 · 원소 · 물리 점검 (v16.4) · 시드 {seed}\n");

        // ── 0) 표 한 장: 재질 × 원소 모든 조합 · 원소끼리 · 전자기기 — 빈칸 · 모순 없음 ──
        {
            var bad = Matter.Audit();
            int cells = 0;
            foreach (var m in Matter.AllMaterials) foreach (var e in Matter.Elements) if (Matter.Rule(m, e).Defined) cells++;
            Check("재질 × 원소 모든 조합 — 빈칸 · 모순 없음", bad.Count == 0 && cells == Matter.AllMaterials.Length * Matter.ElementCount,
                $"재질 {Matter.AllMaterials.Length} × 원소 {Matter.ElementCount} = {cells}칸 · 원소끼리 {Matter.ElementCount * Matter.ElementCount} · 문제 {bad.Count}" + (bad.Count > 0 ? " — " + string.Join(" / ", bad.Take(6)) : ""));
            bool named = Matter.React(Material.Metal, Element.Electric) == Reaction.Conduct && Matter.React(Material.Rubber, Element.Electric) == Reaction.Insulate
                && Matter.Ignitability(Material.Fabric, 1f) < 0.15f && Matter.Ignitability(Material.Fabric, 0f) > 0.5f
                && Matter.React(Material.Glass, Element.Heat) == Reaction.Crack && Matter.Pair(Element.Heat, Element.Cold).R == Reaction.Crack
                && Matter.React(Material.Plastic, Element.Heat) == Reaction.Melt && Matter.ToxicSmoke(Material.Plastic) > 0f
                && Matter.React(Material.Paper, Element.Water) == Reaction.Ruin
                && Matter.React(Material.Ice, Element.Heat) == Reaction.Thaw && Matter.Rule(Material.Ice, Element.Heat).Into == Material.Liquid
                && Matter.Pair(Element.Fire, Element.Oxygen).R == Reaction.Explode;
            Check("표의 이름난 규칙 — 금속+전기=전도 · 고무=절연 · 젖은 천은 안 탄다 · 유리+급변=금 · 플라스틱+열=녹고 유독 · 종이+물=망가짐 · 얼음+열=물 · 산소+불=폭발적", named,
                string.Join(" · ", new[] { Matter.Say(Material.Metal, Element.Electric), Matter.Say(Material.Rubber, Element.Electric), Matter.Say(Material.Glass, Element.Heat), Matter.Say(Material.Ice, Element.Heat) }));
            // 바닥재 · 벽 층은 바탕 재질의 줄 · 옛 규칙은 같은 값 (동작 보존)
            bool same = true;
            foreach (var f in new[] { Material.Grate, Material.Rubber, Material.Tile, Material.Carpet, Material.MetalPlate })
                foreach (var wet in new[] { 0f, 0.3f, 1f })
                    same &= MathF.Abs(Matter.FireSpreadMul(f, wet) - (0.9f + 0.5f * Materials.Of(f).Burn) * (1f - 0.6f * wet)) < 1e-6f;
            same &= Matter.ShortRate(0.2f) == 3f * 0.2f && Matter.ShockMul(Material.Tile) == 1f && Matter.ShockMul(Material.MetalPlate) == 1f && Matter.ShockMul(Material.Carpet) == 1f;
            bool floors = Matter.React(Material.Grate, Element.Electric) == Reaction.Conduct && Matter.React(Material.Carpet, Element.Fire) == Reaction.Burn
                && Matter.React(Material.Tile, Element.Fire) == Reaction.Resist && Matter.ShockMul(Material.Rubber) < 0.1f;
            Check("흩어진 규칙이 표를 부른다 — 불 확산 · 누전 · 감전 값은 그대로 · 바닥재는 바탕 재질의 줄 (고무 바닥은 감전을 막는다)", same && floors,
                $"고무 바닥 감전 배율 {Matter.ShockMul(Material.Rubber):0.00} · 타일 {Matter.ShockMul(Material.Tile):0.00} · 격자+전기 {Matter.Name(Matter.React(Material.Grate, Element.Electric))}");
        }

        // ── 1) 보이는 대로 동작: 히터 곁의 젖은 천 · 마른 천 · 유리 · 얼음 · 종이 ──
        {
            var w = MFresh(seed);
            var m = w.Matter;
            var room = MRoom(w, r => r.Kind is not (RoomType.Reactor or RoomType.Engine));
            var hub = MHub(w, room)!.Value;
            MHeater(w, room, hub);
            var wet = m.Add(ArticleKind.Towel, hub + new Cell(1, 0), "시험"); wet.Water = wet.Spec.Capacity;
            var dry = m.Add(ArticleKind.Towel, hub + new Cell(-1, 0), "시험");
            var jar = m.Add(ArticleKind.GlassJar, hub + new Cell(0, 1), "시험");
            var ice = m.Add(ArticleKind.IceBlock, hub + new Cell(0, -1), "시험");
            var paper = m.Add(ArticleKind.PaperStack, MOpen(w, room).Last(), "시험");
            float iceMass = ice.Mass;
            Run(w, SimTime.Minutes(8));
            float wetChar = wet.Char, dryChar = dry.Char;
            bool wetStillWet = wet.WetFrac > 0.2f;
            Check("젖은 천은 안 탄다 · 마른 천은 히터 곁에서 그을린다 (같은 자리 · 같은 열)", wetChar == 0f && dryChar > 0.02f && wetStillWet && wet.Temp <= 72.5f,
                $"젖은 수건 {wet.Temp:0}℃ 물 {wet.WetFrac * 100:0}% 그을음 {wetChar:0.00} · 마른 수건 {dry.Temp:0}℃ 그을음 {dryChar:0.00} · 히터 곁 칸 {m.CellTemp(hub + new Cell(1, 0)):0}℃");
            Check("얼음 + 열 = 물 — 히터 곁 얼음이 녹아 바닥에 물", (ice.Mass < iceMass || !m.Things.Contains(ice)) && m.LitersAt(hub + new Cell(0, -1)) + m.Spills.Values.Sum(s => s.Liters) > 0.05f,
                $"얼음 {iceMass:0.0}→{(m.Things.Contains(ice) ? ice.Mass : 0f):0.0}kg · 쏟은 물 {m.Spills.Values.Sum(s => s.Liters):0.00}L · 녹아 없어짐 {m.Stats.Thawed}");
            // 달궈진 유리병에 물을 끼얹는다 → 급한 온도 변화
            float jarHot = jar.Temp;
            m.Pour(jar.At, Material.Liquid, 1.5f, "시험 — 끼얹은 물");
            Run(w, SimTime.Minutes(2));
            Check("유리 + 급한 온도 변화 = 금 — 히터에 달궈진 유리병에 물을 끼얹으면 금이 간다", jarHot > 70f && jar.Stage >= BreakStage.Cracked && m.Stats.ThermalCracks >= 1,
                $"유리병 {jarHot:0}℃ → {jar.Temp:0}℃ · 단계 {jar.Stage} · 열충격 {m.Stats.ThermalCracks}");
            m.Pour(paper.At, Material.Liquid, 2f, "시험 — 쏟은 물");
            Run(w, SimTime.Minutes(3));
            Check("종이 + 물 = 망가짐 — 쏟은 물을 머금은 서류 뭉치", paper.Ruined && paper.Water > 0f, $"서류 물 {paper.Water:0.00}L · 망가짐 {paper.Ruined} · 머금음 {m.Stats.Absorbed:0.0}L");
        }

        // ── 2) 플라스틱 + 불 = 녹고 유독 연기 · 금속 + 전기 = 전도 · 고무 매트 = 절연 · 산소 + 불꽃 = 폭발적 ──
        {
            var w = MFresh(seed);
            var m = w.Matter;
            var room = MRoom(w, r => Materials.FloorFor(r.Kind) == Material.MetalPlate && r.Kind != RoomType.Airlock);
            var cells = MOpen(w, room);
            var crate = m.Add(ArticleKind.PlasticCrate, cells[^1], "시험");
            float tox0 = room.Air.Toxin;
            w.Fire.Ignite(crate.At, 0.7f);
            Run(w, SimTime.Minutes(6));
            Check("플라스틱 + 불 = 녹고 유독 연기", crate.Melt > 0.1f && room.Air.Toxin > tox0 && m.Stats.Melted >= 1, $"녹음 {crate.Melt:0.00} · 독 {tox0:0.000}→{room.Air.Toxin:0.000}");
            foreach (var c in w.Fire.Fires.Keys.ToList()) w.Fire.Suppress(c, 3f, 5f);

            var w2 = MFresh(seed);
            var m2 = w2.Matter;
            var r2 = MRoom(w2, r => Materials.FloorFor(r.Kind) == Material.MetalPlate && r.Kind != RoomType.Airlock);
            var hub = MHub(w2, r2)!.Value;
            var j = m2.AddJunction(hub);
            foreach (var d in Cell.Dirs8) w2.Body.SetMark(hub + d, CellMark.Wet, 0.8f, "시험");
            w2.Body.SetMark(hub, CellMark.Wet, 0.8f, "시험");
            var matCell = hub + new Cell(1, 0);
            var mat = m2.Add(ArticleKind.RubberMat, matCell, "시험");
            for (int k = 0; k < 6; k++) { j.Wet = 0.9f; Run(w2, SimTime.Minutes(1)); }
            float onMetal = m2.LiveAt(hub + new Cell(-1, 0)), onMat = m2.LiveAt(matCell);
            Check("금속 + 전기 = 전도 — 젖은 금속판 바닥으로 번지고 · 고무 매트 칸은 0", onMetal > 0.25f && onMat == 0f && m2.Stats.Sparks >= 1,
                $"젖은 금속판 {onMetal:0.00} · 고무 매트 {onMat:0.00} · 불꽃 {m2.Stats.Sparks} · 전기 칸 {m2.LiveField.Count}");
            // 짙은 산소 + 불꽃 → 컴퓨터 경보 · 불이 순식간에
            r2.Air.O2 = 32f;
            int fires0 = w2.Fire.Count;
            for (int k = 0; k < 20 && m2.Stats.OxygenFlashes == 0; k++) { j.Wet = 0.9f; r2.Air.O2 = MathF.Max(r2.Air.O2, 32f); Run(w2, SimTime.Minutes(1)); }
            Check("산소 + 불꽃 = 폭발적 — 짙은 산소 방의 젖은 접속부 불꽃이 곧 불 · 주 컴퓨터가 먼저 경보", m2.Stats.OxygenFlashes >= 1 && m2.Stats.O2Warns >= 1 && w2.Fire.Count + w2.Fire.Scorch.Count > fires0,
                $"산소 불꽃 {m2.Stats.OxygenFlashes} · 컴퓨터 산소 경보 {m2.Stats.O2Warns} · 불 {w2.Fire.Count}칸");
        }

        // ── 3) 새는 관 아래 러그 → 다 머금고 넘쳐 바닥이 젖음 → 접속부가 젖어 누설 전류 → 걷어 말리다 발견 → 고침 ──
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            RunUntilHour(w, 8f);
            var m = w.Matter;
            var rug = m.Things.Where(t => t.Kind == ArticleKind.Rug && m.JunctionAt(t.At) != null && w.Ship.RoomAt(t.At) is Room rr && rr.Powered && rr.DataLinked).OrderBy(t => t.Id).First();
            var room = w.Ship.RoomAt(rug.At)!;
            var j = m.JunctionAt(rug.At)!;
            m.Drip(rug.At, 6f, "천장 관 이음에서 새는 물", hours: 3);
            long sat = -1, full = -1, floorWet = -1, junctionWet = -1, warned = -1, lifted = -1, found = -1, fixedAt = -1;
            for (int t = 0; t < SimTime.Hours(10) && fixedAt < 0; t++)
            {
                w.Step();
                if (sat < 0 && rug.WetFrac >= 0.6f) sat = w.Tick;
                if (full < 0 && rug.Water >= rug.Spec.Capacity * 0.99f) full = w.Tick;
                if (floorWet < 0 && (m.UnderWet(rug.Home) > 0.05f || w.Body.Mark(rug.Home, CellMark.Wet) > 0.2f || Cell.Dirs4.Any(d => w.Body.Mark(rug.Home + d, CellMark.Wet) > 0.2f))) floorWet = w.Tick;
                if (junctionWet < 0 && j.Wet >= 0.25f) junctionWet = w.Tick;
                if (warned < 0 && m.Stats.LeakWarns > 0) warned = w.Tick;
                if (lifted < 0 && m.Stats.Lifted > 0) lifted = w.Tick;
                if (found < 0 && j.Known) found = w.Tick;
                if (fixedAt < 0 && m.Stats.JunctionsFixed > 0) fixedAt = w.Tick;
            }
            string T(long x) => x < 0 ? "—" : SimTime.Clock(x);
            Check("러그가 새는 물을 머금다 한계를 넘으면 바닥이 젖는다", sat > 0 && floorWet >= sat && m.Stats.Seeps + m.Stats.Overflows >= 1,
                $"{room.Name} 러그 {rug.Spec.Capacity:0}L — 60% {T(sat)} · 다 머금음 {T(full)} → 바닥 젖음 {T(floorWet)} · 넘침 {m.Stats.Overflows} · 스밈 {m.Stats.Seeps}");
            Check("러그 아래로 스민 물에 숨은 배선 접속부가 젖고 · 주 컴퓨터는 누설 전류만 본다", junctionWet > 0 && warned > 0,
                $"접속부 젖음 {T(junctionWet)} · 컴퓨터 누설 전류 경고 {T(warned)} · 불꽃 {m.Stats.Sparks}");
            Check("러그를 걷어 말리다 그 아래 젖은 배선 접속부를 발견 → 고침", lifted > 0 && found > 0 && found >= lifted && fixedAt > 0 && m.Stats.Hung >= 1,
                $"걷음 {T(lifted)} · 발견 {T(found)} · 널어 말림 {m.Stats.Hung} · 고침 {T(fixedAt)} · 접속부 {(j.Taped ? "테이프" : "말림")}");
        }

        // ── 4) 젖은 바닥의 분전함 앞에 고무 매트를 깔고 작업 ──
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            RunUntilHour(w, 9f);
            var m = w.Matter;
            var room = w.Ship.LiveRooms.Where(r => !r.Detached && r.Powered && r.Kind is RoomType.Galley or RoomType.Mess or RoomType.Storage && Materials.FloorFor(r.Kind) != Material.Rubber).OrderBy(r => r.Id).First();
            room.BreakerOff = true;
            Player.Policy(w, "controlseat", 1); // 관제석에서 원격으로 올리지 않게 — 사람이 분전함 앞에 서야 한다
            bool laid = false, workedOnMat = false, restored = false;
            int shocks0 = m.Stats.LiveShocks + w.Moisture.Stats.Shocks;
            CrewMember? worker = null;
            for (int t = 0; t < SimTime.Hours(6) && !(workedOnMat && restored); t++)
            {
                if (!laid && t % SimTime.Minutes(5) == 0)
                {
                    foreach (var c in room.Cells) if (w.Ship.Grid.Kind(c) == TileKind.Floor) w.Body.RaiseMark(c, CellMark.Wet, 0.7f, "퍼내고 남은 물기");
                    w.Board.RequestScan();
                }
                if (w.Automation.Operator is CrewMember op) op.EndJob(w, ToilStatus.Interrupted);
                w.Step();
                laid |= m.Stats.MatsLaid > 0;
                foreach (var c in w.Crew)
                    if (!c.Dead && c.Job?.Order?.Kind == WorkKind.BreakerOn && c.Pose == Pose.Working && m.MatAt(c.Cell)) { workedOnMat = true; worker = c; }
                restored = !room.BreakerOff;
            }
            Check("젖은 바닥의 분전함 앞 — 고무 매트를 가져와 깔고 그 위에서 작업", laid && workedOnMat && restored,
                $"{room.Name} 매트 {m.Stats.MatsLaid} · 그 위 작업 {workedOnMat}({worker?.Name}) · 분전함 다시 올림 {restored} · 감전 배율(매트 위) {(worker != null ? m.ShockMul(worker) : 1f):0.00}");
            Check("고무 매트 위는 감전 걱정이 없다 (표: 고무 = 절연)", worker == null || m.ShockMul(worker) < 0.1f, $"감전 {m.Stats.LiveShocks + w.Moisture.Stats.Shocks - shocks0}");
        }

        // ── 5) 히터 옆 젖은 수건이 마르다 그을기 시작 → 연기 → 감지기 → 컴퓨터가 문을 닫음 → 냄새 → 사람 반응 ──
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            RunUntilHour(w, 10f);
            var m = w.Matter;
            var room = MRoom(w, r => r.DataLinked && r.Doors.Count(d => !d.IsExternal) >= 1 && r.Kind is RoomType.Quarters or RoomType.Laundry or RoomType.Lounge or RoomType.Mess or RoomType.PrivateCabins or RoomType.Gym);
            var hub = MHub(w, room)!.Value;
            var heater = MHeater(w, room, hub);
            var towel = m.Add(ArticleKind.Towel, hub + new Cell(1, 0), "시험 — 젖은 수건을 히터 곁에 널었다");
            towel.Water = towel.Spec.Capacity * 0.5f;
            m.QuietUntil = w.Tick + SimTime.Hours(8); // 미리 치우지 않는다 (사슬이 끝까지 가는지 본다)
            // 한 사람은 그 방에서 다른 일을 한다 (히터가 "빈 방"이 되지 않게 · 그 사람은 손이 묶여 있다)
            var sitter = w.Crew.Where(c => c.CanAct && !c.IsChild).OrderBy(c => (c.Position - room.Center).LengthSquared()).First();
            Teleport(w, sitter, MOpen(w, room).Last());
            Force(w, sitter, new Job(null, "책 읽기", new List<Toil> { new WaitToil(SimTime.Hours(6), Pose.Sitting) }), SimTime.Hours(6));
            var neighbors = room.Doors.Where(d => !d.IsExternal).Select(d => d.RoomA == room ? d.RoomB : d.RoomA).Where(r => r != null).Cast<Room>().Distinct().ToList();
            long warn = -1, dryT = -1, charT = -1, alarm = -1, seal = -1, smell = -1, doused = -1, unseal = -1;
            int sniff0 = w.Smells.Stats.BurntSniffs + w.Smells.Stats.Checks;
            CrewMember? responder = null;
            // v19 문이 다시 열린 뒤 탄내가 옆방으로 번지는 것까지 20분 더 본다 (닫힌 문 너머로는 0.004쯤만 샌다 — 들어간 사람이 냄새 갱신 때 마침 방에 있어야 통과했다)
            for (int t = 0; t < SimTime.Hours(4) && (unseal < 0 || doused < 0 || smell < 0 && w.Tick - unseal < SimTime.Minutes(20)); t++)
            {
                w.Step();
                if (warn < 0 && towel.Flagged) warn = w.Tick;
                if (dryT < 0 && towel.WetFrac < 0.03f) dryT = w.Tick;
                if (charT < 0 && towel.Char >= 0.04f) charT = w.Tick;
                if (alarm < 0 && m.Stats.SmokeAlarms > 0) alarm = w.Tick;
                if (seal < 0 && m.SmokeSealed(room)) seal = w.Tick;
                if (smell < 0 && charT > 0 && w.Crew.Any(c => c.Id != sitter.Id && w.Smells.Smelled(c, SmellKind.Burnt, SimTime.Minutes(10)) is { } sn && sn.Tick >= charT)) smell = w.Tick;
                if (doused < 0 && m.Stats.Doused > 0) { doused = w.Tick; responder = w.Crew.FirstOrDefault(c => c.Job?.Activity is MatterActivity); }
                if (unseal < 0 && m.Stats.Unseals > 0) unseal = w.Tick;
            }
            string T(long x) => x < 0 ? "—" : SimTime.Clock(x);
            Check("주 컴퓨터가 위험한 조합을 읽고 경고 — 히터 옆 젖은 천", warn > 0 && (dryT < 0 || warn <= dryT), $"경고 {T(warn)} · 마름 {T(dryT)}");
            Check("마르다 그을기 시작 → 연기 → 감지기 → 컴퓨터가 문을 닫음", dryT > 0 && charT >= dryT && alarm >= charT && seal >= alarm,
                $"{room.Name}: 마름 {T(dryT)} → 그을음 {T(charT)} → 연기 감지 {T(alarm)} → 문 닫음 {T(seal)} (연기 {room.Air.Smoke * 100:0}%)");
            Check("냄새 → 사람 반응 — 옆방까지 탄 냄새 · 누가 찾아와 수건을 치우고 적신다 · 연기가 걷히면 문이 다시 열린다", smell > 0 && doused > 0 && unseal > doused && towel.Char < 1f,
                $"탄 냄새 {T(smell)} · 치우고 적심 {T(doused)}({responder?.Name ?? "?"}) · 문 다시 엶 {T(unseal)} · 수건 그을음 {towel.Char:0.00} · 진짜 불 {m.Stats.Ignited}");
            _ = heater;
        }

        // ── 6) 컴퓨터 경고를 들은 사람이 미리 치운다 (★★ 주 컴퓨터 → 승무원) ──
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            RunUntilHour(w, 10f);
            var m = w.Matter;
            var room = MRoom(w, r => r.DataLinked && r.Kind is RoomType.Laundry or RoomType.Lounge or RoomType.Mess or RoomType.Gym or RoomType.Galley);
            var hub = MHub(w, room)!.Value;
            MHeater(w, room, hub);
            var towel = m.Add(ArticleKind.Towel, hub + new Cell(0, 1), "시험");
            towel.Water = towel.Spec.Capacity;
            for (int t = 0; t < SimTime.Hours(3) && m.Stats.HeededWarns == 0; t++) w.Step();
            Check("경고를 들은 승무원이 히터 곁 젖은 수건을 미리 치운다 (그을기 전에)", m.Stats.HeededWarns >= 1 && m.Stats.MovedFromHeat >= 1 && towel.Char < 0.04f && Math.Max(Math.Abs(towel.At.X - hub.X), Math.Abs(towel.At.Y - hub.Y)) >= 2,
                $"컴퓨터 경고 {m.Stats.ComputerWarns} · 따름 {m.Stats.HeededWarns} · 치움 {m.Stats.MovedFromHeat} · 수건 그을음 {towel.Char:0.00} · 히터에서 {Math.Max(Math.Abs(towel.At.X - hub.X), Math.Abs(towel.At.Y - hub.Y))}칸");
        }

        // ── 7) 폭발이 물건을 밀어 깨진 조각이 위험물로 남는다 → 사람들은 돌아가고 → 쓸어 낸다 ──
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            RunUntilHour(w, 9f);
            var m = w.Matter;
            m.QuietUntil = long.MaxValue;
            var (room, hub) = MWide(w, r => r.Kind is RoomType.Lounge or RoomType.Mess or RoomType.Storage or RoomType.Cargo or RoomType.Gym or RoomType.Quarters or RoomType.Galley or RoomType.Workshop or RoomType.Hydroponics)!.Value;
            var jar = m.Add(ArticleKind.GlassJar, hub + new Cell(1, 0), "시험");
            var mug = m.Add(ArticleKind.Mug, hub + new Cell(0, 1), "시험");
            var box = m.Add(ArticleKind.CardboardBox, hub + new Cell(-1, 0), "시험");
            var tool = m.Add(ArticleKind.Toolbox, hub + new Cell(0, -1), "시험");
            var towel = m.Add(ArticleKind.Towel, hub + new Cell(1, 1), "시험");
            var before = new[] { box, towel, tool }.Select(t => t.At).ToList();
            w.Blast.Detonate(hub, 0.3f, BlastKind.Generic, "시험");
            Run(w, 20);
            int moved = new[] { box, towel, tool }.Where((t, i) => t.At != before[i]).Count();
            var shards = m.Things.Where(t => t.Kind == ArticleKind.Shards).ToList();
            var sc = shards.FirstOrDefault(t => Matter.Sharp(t.Mat))?.At;
            bool hazard = sc is Cell s0 && w.Body.Mark(s0, CellMark.Glass) > 0.2f && w.Paths.CellBody[w.Ship.Grid.Index(s0)] >= BodySystem.GlassCost;
            Check("폭발이 물건을 같은 함수로 민다 — 가벼운 것은 멀리 · 유리 · 사기는 조각이 되어 위험물(칸 상태 유리 · 길 비용)", moved >= 2 && shards.Count >= 1 && hazard && m.Stats.Pushed >= 2,
                $"움직임 {moved}/3 · 조각 {shards.Count}({string.Join("·", shards.Select(t => Materials.Name(t.Mat)))}) · 유리 상태 {(sc is Cell s1 ? w.Body.Mark(s1, CellMark.Glass) : 0f):0.00} · 공구함 {(tool.At == before[2] ? "그대로" : "밀림")} · 상자 {box.Stage}");
            // 돌아가기: 조각 칸을 지나야 하는 길이면 둘러 간다
            bool detour = true;
            if (sc is Cell s2 && w.Ship.IsOpenFloor(s2 + new Cell(-1, 0)) && w.Ship.IsOpenFloor(s2 + new Cell(1, 0)) && w.Ship.IsOpenFloor(s2 + new Cell(-1, 1)) && w.Ship.IsOpenFloor(s2 + new Cell(1, 1)) && w.Ship.IsOpenFloor(s2 + new Cell(0, 1)))
            {
                var c = w.Crew.Where(x => x.CanAct && !x.IsChild).OrderBy(x => x.Id).First();
                Teleport(w, c, s2 + new Cell(-1, 0));
                Locomotion.SetDestination(c, w, s2 + new Cell(1, 0));
                detour = c.Path != null && !c.Path.Contains(s2);
            }
            int swept = 0;
            for (int t = 0; t < SimTime.Hours(6) && m.Things.Any(x => x.Kind == ArticleKind.Shards && Matter.Sharp(x.Mat)); t++) w.Step();
            swept = shards.Count(x => !m.Things.Contains(x));
            Check("유리 조각 돌아가기 · 손보기가 쓸어 내면 조각이 사라진다", detour && swept >= 1 && !m.Things.Any(x => x.Kind == ArticleKind.Shards && Matter.Sharp(x.Mat)),
                $"둘러 감 {detour} · 쓸어 냄 {swept}/{shards.Count} · 청소 {w.Body.Stats.Cleaned}");
        }

        // ── 8) 가루 → 분진 폭발 (Blast와 잇기) · 액체가 흐르고 격자로 빠진다 · 기름은 미끄럽다 ──
        {
            var w = MFresh(seed);
            var m = w.Matter;
            var room = MRoom(w, r => r.Kind is not (RoomType.Reactor or RoomType.Power));
            var hub = MHub(w, room)!.Value;
            var sack = m.Add(ArticleKind.PowderSack, hub, "시험");
            ObjectPhysics.Impact(w, sack, 5000f, "시험 — 떨어뜨림");
            float dust = m.DustAt(hub);
            int blasts0 = w.Blast.Stats.Detonations;
            w.Fire.Ignite(hub + new Cell(1, 0), 0.5f);
            Run(w, SimTime.Minutes(3));
            bool dustBlast = m.Stats.DustBlasts >= 1 && w.Blast.Recent.Any(r => r.Kind == BlastKind.Dust);
            Check("가루가 흩날리고 불씨가 닿으면 분진 폭발 (Blast)", dust > 0.3f && dustBlast && w.Blast.Stats.Detonations > blasts0, $"가루 {dust:0.00} · 분진 폭발 {m.Stats.DustBlasts} · 폭발 {w.Blast.Stats.Detonations - blasts0}");

            var w2 = MFresh(seed);
            var m2 = w2.Matter;
            var grate = MRoom(w2, r => Materials.FloorFor(r.Kind) == Material.Grate);
            var g0 = MHub(w2, grate)!.Value;
            var jug = m2.Add(ArticleKind.WaterJug, g0, "시험");
            ObjectPhysics.Impact(w2, jug, 3000f, "시험");
            Run(w2, SimTime.Minutes(4));
            int wetCells = grate.Cells.Count(c => w2.Body.Mark(c, CellMark.Wet) > 0.2f);
            Check("담긴 물이 쏟아져 흐르고 격자로 빠진다", jug.Stage >= BreakStage.Broken && wetCells >= 3 && m2.Stats.Drained > 0.5f,
                $"물통 {jug.Stage} · 젖은 칸 {wetCells} · 빠짐 {m2.Stats.Drained:0.0}L · 쏟음 {m2.Stats.Spilled:0}L");
            var tileRoom = MRoom(w2, r => Materials.FloorFor(r.Kind) is Material.Tile or Material.MetalPlate);
            var o0 = MOpen(w2, tileRoom)[0];
            float slip0 = w2.Body.SlipAt(w2.Ship.Grid.Index(o0));
            var can = m2.Add(ArticleKind.OilCan, o0 + new Cell(0, 0), "시험");
            ObjectPhysics.Impact(w2, can, 9000f, "시험");
            Run(w2, SimTime.Minutes(2));
            float slip1 = w2.Body.SlipAt(w2.Ship.Grid.Index(o0));
            Check("기름통이 깨져 기름 바닥 — 미끄러움이 오른다 (뛰면 미끄러진다)", w2.Body.Mark(o0, CellMark.Oil) > 0.3f && slip1 > slip0 + 0.2f, $"미끄러움 {slip0:0.00}→{slip1:0.00}");
        }

        // ── 9) 물건 물리 한 벌 공개 API — 질량 · 마찰 · 고정 · 기동 · 무중력 ──
        {
            var w = MFresh(seed);
            var m = w.Matter;
            var room = MRoom(w, r => r.Kind is not (RoomType.Reactor or RoomType.Power) && r.MaxX - r.MinX >= 5);
            var cells = MOpen(w, room);
            var a = cells[0];
            var light = m.Add(ArticleKind.Towel, a, "시험");
            var heavy = m.Add(ArticleKind.Toolbox, a, "시험");
            ObjectPhysics.Push(w, a, 4f, Vector2.UnitX, "시험");
            float dl = MathF.Abs(light.At.X - a.X) + MathF.Abs(light.At.Y - a.Y), dh = MathF.Abs(heavy.At.X - a.X) + MathF.Abs(heavy.At.Y - a.Y);
            var bolted = m.Add(ArticleKind.PlasticCrate, cells[2], "시험"); bolted.Fixed = true;
            bool held = !ObjectPhysics.Push(w, bolted, 20f, Vector2.UnitY, "시험");
            var iceC = cells.First(c => !m.Any(c));
            var ice = m.Add(ArticleKind.IceBlock, iceC, "시험");
            var box = m.Add(ArticleKind.Toolbox, cells.Where(c => !m.Any(c)).ElementAt(1), "시험");
            var ib = ice.At; var bb = box.At;
            int shoved = ObjectPhysics.Shove(w, room, new Vector2(0.2f, 0f), 2f, "시험 — 기동");
            bool maneuver = ice.At != ib && box.At == bb;
            ObjectPhysics.SetGravity(w, 0f);
            var floater = m.Add(ArticleKind.PaperStack, cells.Where(c => !m.Any(c)).ElementAt(2), "시험");
            ObjectPhysics.Push(w, floater, 0.6f, Vector2.UnitY, "시험 — 툭");
            var f0 = floater.At;
            if (Environment.GetEnvironmentVariable("MATTER_DEBUG") == "1") { Console.WriteLine($"   무중력 시작 {f0} 속도 {floater.Vel} 방 {room.Name}"); for (int k = 0; k < 8; k++) { Run(w, SimTime.Minutes(0.5f)); Console.WriteLine($"   {k} {floater.At} 속도 {floater.Vel} 든 사람 {floater.CarriedBy} 떠돎 {m.Stats.Drifted}"); } } else
            Run(w, SimTime.Minutes(4));
            bool drift = m.Stats.Drifted >= 1 || floater.At != f0;
            ObjectPhysics.SetGravity(w, 1f);
            Check("같은 힘에 가벼운 수건은 멀리 · 무거운 공구함은 덜 · 고정한 것은 그대로", dl > dh && held, $"수건 {dl}칸 · 공구함 {dh}칸 · 고정 {held}");
            Check("기동(가속 0.2G): 미끄러운 얼음은 밀리고 공구함은 마찰로 버틴다 · 무중력에선 떠돈다 (같은 함수)", maneuver && shoved >= 1 && drift,
                $"밀린 물건 {shoved} · 얼음 {ib}→{ice.At} · 공구함 {bb}→{box.At} · 떠돎 {m.Stats.Drifted}");
        }

        // ── 10) 접촉 오염 (장갑 → 손잡이 → 다음 사람) · 통로 점유 (짐 → 길) ──
        {
            var w = MFresh(seed);
            var m = w.Matter;
            var door = w.Ship.Doors.Where(d => !d.IsExternal && !d.Removed && d.RoomA != null && d.RoomB != null).OrderBy(d => d.Id).First();
            var crew = w.Crew.Where(c => !c.Dead).OrderBy(c => c.Id).Take(2).ToList();
            var a = crew[0]; var b = crew[1];
            a.Soil.Hands[(int)SoilKind.Oil] = 0.9f;
            a.Soil.Hands[(int)SoilKind.Bio] = 0.8f;
            b.Soil.Hands[(int)SoilKind.Bio] = 0f;
            Teleport(w, a, door.Cell);
            m.Touch(a, a.Soil, 0.01f);
            float oil = m.HandleSoil(door, SoilKind.Oil), germ = m.HandleSoil(door, SoilKind.Bio);
            Teleport(w, b, door.Cell);
            m.Touch(b, b.Soil, 0.01f);
            Check("접촉 오염 — 더러운 장갑 → 문 손잡이(기름 얼룩 · 균) → 다음 사람 손에 균", oil > 0.1f && germ > 0.1f && b.Soil.Hands[(int)SoilKind.Bio] > 0.05f,
                $"손잡이 기름 {oil:0.00} · 균 {germ:0.00} · {b.Name} 손 균 {b.Soil.Hands[(int)SoilKind.Bio]:0.00} (손잡이 재질 {Materials.Name(Material.Metal)} · 머금음 {Matter.Hold(Material.Metal):0.00})");
            var corr = w.Ship.LiveRooms.First(r => r.Kind == RoomType.Corridor);
            var cc = corr.Cells.First(c => w.Ship.IsOpenFloor(c) && w.Ship.DoorAt(c) == null && !m.Any(c));
            int i = w.Ship.Grid.Index(cc);
            int before = w.Paths.CellBody[i];
            m.Add(ArticleKind.CardboardBox, cc, "시험 — 내려놓은 짐");
            Run(w, 20);
            Check("통로 점유 — 통로에 내려놓은 짐이 길 비용을 올린다", w.Paths.CellBody[i] >= before + ArticleSpecs.Of(ArticleKind.CardboardBox).Bulk, $"{before}→{w.Paths.CellBody[i]}");
        }

        // ── 결정론 · 성능 ──
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 — 같은 시드 같은 지문", h1 == h2, $"{h1:x8} / {h2:x8}");
            Prof.On = true;
            Prof.Reset();
            var w = World.CreateDefault(seed, 0, "Hanbit");
            var sw = Stopwatch.StartNew();
            Run(w, SimTime.TicksPerDay);
            sw.Stop();
            var rep = Prof.Report();
            double mine = rep.Where(x => x.key == "sys.Matter").Sum(x => x.ms);
            double all = sw.Elapsed.TotalMilliseconds;
            Prof.On = false;
            Check("성능 — 재질 · 원소 · 물건 한 벌은 하루 시뮬레이션의 3% 안", mine < all * 0.03, $"재질 {mine:0}ms / 전체 {all:0}ms ({mine / all * 100:0.0}%) · 물건 {w.Matter.Things.Count} · 접속부 {w.Matter.Junctions.Count}");
            Console.WriteLine($"    {w.Matter.Stats}");
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails;
    }
}
