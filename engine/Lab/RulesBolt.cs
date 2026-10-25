using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>
/// 볼트 규칙 (PH10 — docs/PHYSICS.md §3-7 · §5-4 · §10 묶음 '볼트'): 35kPa 폭풍에 탁자는 밀리고 · 선반은 뜯기고 · 원자로는 그대로 ·
/// 풀린 냉각 펌프 (시험이 net.Loose = 1.2를 바로 써도 ⑥이 틱마다 볼트 단을 맞춘다)는 4m/s 덜컹에 뜯기고 꺼진다 · 조인 것은 그대로 ·
/// 뜯긴 침대에서는 자지 않는다 · 넘어진 선반을 길이 돌아간다 · 압력에 날아간 문짝은 물체가 되어 날아간다 (폭풍이면 그 반대쪽으로).
/// </summary>
public static partial class Program
{
    private static void BoltRules()
    {
        if (!Do("볼트")) return;
        Console.WriteLine("\n[볼트 — 뜯김 · 밀림 · 덜컹 · 조임 · 침대 · 길 · 문짝]");

        // ── 35kPa 폭풍: 세 칸 떨어진 곳에서 터져 그 가구 칸에 첨두 35kPa — 탁자 (놓였다 · 20kg)는 밀리고 · 선반 (9g 볼트 · 35kg)은 뜯기고 · 원자로 (40g · 2.4t)는 그대로 ──
        {
            double w3 = 1;   // 첨두 35kPa가 3칸 거리에 오는 TNT 당량의 세제곱근 (나눠 찾는다)
            { double lo = 0.01, hi = 5; for (int k = 0; k < 60; k++) { double mid = (lo + hi) / 2; if (Structure.BlastKPa(3 / mid) < 35) lo = mid; else hi = mid; } w3 = (lo + hi) / 2; }
            double joules = w3 * w3 * w3 * Structure.TntJ;
            (bool placed, bool broken, double moved, string story) Hit(string type, string art)
            {
                var s = PropWorld(1, 9, art, new Dictionary<char, string> { ['x'] = type });
                var st = s.Get<Structure>(); var L = s.Layout;
                var fu = L.Furniture[0];
                int near = fu.Cells.OrderBy(c => c).First(), boom = L.Index(L.X(near) - 3, L.Y(near));
                s.Step();
                st.Blast(s, boom, joules, s.Causes.Add(CauseKind.Setup, "배터리가 터졌다", s.Tick));
                int pid = st.FurnProp[0], i = pid >= 0 ? st.IndexOf(pid) : -1;
                double moved = i >= 0 ? Math.Sqrt(st.Props[i].Vx * st.Props[i].Vx + st.Props[i].Vy * st.Props[i].Vy) : 0;   // 폭풍이 준 빠르기 (놓인 탁자는 조금 밀리면 다시 앉는다)
                for (int k = 0; k < 4; k++) s.Step();
                var torn = s.Facts.Since(0).FirstOrDefault(f => f.Kind == FactKind.TornLoose);
                return (st.FurnPlaced[0] != 0 && st.TornLoose == 0, st.FurnBroken[0] != 0, moved, torn.Kind == FactKind.TornLoose ? s.Causes.Story(torn.Cause) : "-");
            }
            const string Room = "\n#############\n#...........#\n#...........#\n#.....xx....#\n#.....xx....#\n#...........#\n#...........#\n#############";
            const string Wall = "\n#############\n#.....xx....#\n#...........#\n#...........#\n#...........#\n#############";
            var table = Hit("Table", Room); var shelf = Hit("Shelf", Wall); var core = Hit("ReactorCore", Room);
            Check("볼트: 35kPa 폭풍에 탁자 (놓였다)는 밀리고 · 선반 (9g 볼트)은 뜯기고 · 원자로 (40g)는 그대로 (예전엔 설비가 영원히 붙어 있었다)",
                !table.placed && !table.broken && table.moved > 0.1 && !shelf.placed && shelf.broken && shelf.story.Contains("배터리가 터졌다") && core.placed,
                $"탁자 붙음 {table.placed} · 부서짐 {table.broken} · 밀린 빠르기 {table.moved:0.00}m/s / 선반 붙음 {shelf.placed} · 부서짐 {shelf.broken} · \"{shelf.story}\" / 원자로 붙음 {core.placed} (첨두 35kPa · {joules / 1e6:0.00}MJ)");
        }

        // ── 볼트 단은 ⑥ 한 벌: 풀린 냉각 펌프 (Loose 1.2 → 단 2 → 3.2g)는 4m/s 덜컹 (4.1g)에 뜯기고 꺼진다 · 조인 펌프 (Loose 1.2였다가 Tighten → 단 10 → 16g)는 그대로 ──
        {
            var s = ShipWorld.Create("Hwasal", 3);
            var st = s.Get<Structure>(); var net = s.Get<Network>();
            var pumps = Enumerable.Range(0, net.Devices.Count).Where(d => net.Devices[d].Type == "CoolantPump").ToList();
            int loose = pumps[0], tight = pumps[1];
            int fl = net.Devices[loose].Furniture, ft = net.Devices[tight].Furniture;
            s.Run(30);
            bool ran0 = net.Working(loose), ran1 = net.Working(tight);
            net.Loose[loose] = 1.2f; net.Loose[tight] = 1.2f; net.Tighten(tight);
            s.Run(2);
            int a0 = st.FurnAnchor[fl], a1 = st.FurnAnchor[ft];
            st.EnsureBody();
            var o = st.Bodies[0];
            st.Push(0, o.X, o.Y, o.Mass * 4, 0, s.Causes.Add(CauseKind.Setup, "다른 배와 부딪혔다", s.Tick));
            s.Run(2);
            bool off = !net.Working(loose);   // 뜯긴 틱에 관 · 선이 끊긴다 (마모 25틱을 기다리지 않는다)
            s.Run(28);
            var torn = s.Facts.Since(0).FirstOrDefault(f => f.Kind == FactKind.TornLoose && f.Other == fl);
            Check("볼트: ⑥이 틱마다 볼트 단을 맞춘다 — 풀린 냉각 펌프 (시험이 Loose 1.2를 바로 써도 단 2 · 3.2g)는 4m/s 덜컹에 뜯기고 그 틱에 꺼진다 · 조인 펌프 (단 10 · 16g)는 그대로 돈다",
                ran0 && ran1 && a0 == 2 && a1 == 10 && off && st.FurnPlaced[fl] == 0 && st.FurnBroken[fl] != 0 && !net.Working(loose) && st.FurnPlaced[ft] != 0 && net.Working(tight) && torn.Kind == FactKind.TornLoose,
                $"단 {a0} · {a1} · 풀린 펌프 붙음 {st.FurnPlaced[fl]} · 2틱 뒤 꺼짐 {off} · 돈다 {net.Working(loose)} / 조인 펌프 붙음 {st.FurnPlaced[ft]} · 돈다 {net.Working(tight)} · \"{(torn.Kind == FactKind.TornLoose ? s.Causes.Story(torn.Cause) : "-")}\"");
        }

        // ── 뜯긴 침대에서는 자지 않는다: 졸린 이의 침대를 뜯어 옆으로 밀어 두면 — 다른 데서 잔다 ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 3, 0, 0);
            var st = s.Get<Structure>(); var m = s.Get<Mind>(); var L = s.Layout;
            s.Run(Time.Minutes(5));
            var p = m.Agents.First(a => a.IsPerson && a.C.Bed >= 0);
            int bed = L.FurnitureAt[p.C.Bed];
            var cells = L.Furniture[bed].Cells.ToHashSet();
            int id = st.TearLoose(s, bed, 0, 0, 0, s.Causes.Add(CauseKind.Setup, "시험 — 침대 볼트를 풀었다", s.Tick), "볼트가 풀린");
            { int i = st.IndexOf(id); var q = st.Props[i]; q.X += 0.35; st.Props[i] = q; }
            p.C.Sleepy = 1f;
            bool slept = false, onBed = false;
            for (long t = 0; t < Time.Hours(2); t++)
            {
                s.Step();
                if (p.C.Goal == GoalKind.Sleep && p.C.StepIndex > 0 && p.Plan.Count > 0 && p.Plan[Math.Min(p.C.StepIndex, p.Plan.Count - 1)].Kind == StepKind.Use) { slept = true; if (cells.Contains(p.C.Cell)) onBed = true; }
            }
            Check("볼트: 뜯긴 침대에서는 자지 않는다 — 졸린 이는 다른 침대 · 바닥에서 잔다 (예전엔 뜯긴 침대 칸으로 가서 누웠다)",
                id > 0 && slept && !onBed, $"잠 {slept} · 뜯긴 침대 칸에서 {onBed} · 쓸 수 있나 {st.Usable(bed)} · {m.Thought(p.Id)}");
        }

        // ── 넘어진 선반을 길이 돌아간다: 방 한가운데 누운 선반 칸 — 곧은 길 (4걸음) 대신 옆 줄로 비켜 간다 ──
        {
            var s = ShipWorld.Crewed("Didim", 7, 1, 0, 0);
            var st = s.Get<Structure>(); var m = s.Get<Mind>(); var L = s.Layout;
            bool Free(int x, int y) => L.In(x, y) && L.Kind[L.Index(x, y)] == CellKind.Floor && L.FurnitureAt[L.Index(x, y)] < 0;
            int mid = Enumerable.Range(0, L.N).First(c => L.Room[c] >= 0 && Enumerable.Range(-2, 5).All(dx => Free(L.X(c) + dx, L.Y(c)) && Free(L.X(c) + dx, L.Y(c) - 1) && Free(L.X(c) + dx, L.Y(c) + 1)));
            int from = mid - 2, to = mid + 2;
            var before = m.PathOf(0, from, to);
            int shelf = Enumerable.Range(0, L.Furniture.Count).First(f => L.Furniture[f].Type == "Shelf");
            int id = st.TearLoose(s, shelf, 0, 0, 0, s.Causes.Add(CauseKind.Setup, "시험 — 선반이 넘어졌다", s.Tick), "볼트가 풀린");
            { int i = st.IndexOf(id); var q = st.Props[i]; q.X = L.X(mid) + 0.5; q.Y = L.Y(mid) + 0.5; q.Pose = 1; q.State = 0; q.Vx = q.Vy = q.W = 0; st.Props[i] = q; st.ClutterChanged(); }
            var after = m.PathOf(0, from, to);
            Check("볼트: 넘어진 선반을 길이 돌아간다 — 그 칸을 지나던 곧은 길이 옆 줄로 비켜 간다 (예전엔 넘어진 선반 위로 곧장 갔다)",
                before.Contains(mid) && after.Count > 0 && !after.Contains(mid) && after[^1] == to, $"앞 {before.Count}칸 (선반 칸 지남 {before.Contains(mid)}) → 뒤 {after.Count}칸 (지남 {after.Contains(mid)}) · 길 값 {st.ClutterAt(mid)}");
        }

        // ── 길 값은 미끄러지는 가구를 따라간다: 선반을 3m/s로 밀어 뜯은 그 틱에 길을 물어도 — 잠든 선반 칸에 길 값이 서고 · 찍고 되살린 세계와 같다 ──
        {
            var s = PropWorld(1, 5, FurnRoom, new Dictionary<char, string> { ['s'] = "Shelf", ['t'] = "Table" });
            var st = s.Get<Structure>(); var L = s.Layout;
            int shelf = L.Furniture.FindIndex(x => x.Type == "Shelf");
            st.EnsureBody();
            s.Step();
            int id = st.TearLoose(s, shelf, 3, 0, 0, s.Causes.Add(CauseKind.Setup, "시험 — 선반을 밀어 뜯었다", s.Tick), "볼트가 풀린");
            st.ClutterAt(0);   // 뜯은 그 틱에 길찾기가 묻는다
            for (int k = 0; k < 200 && Structure.Awake(st.Props[st.IndexOf(id)]); k++) s.Step();
            var r = new Sim(s.Setup); r.Load(s.Save());
            List<int> Cl(Structure x) => Enumerable.Range(0, L.N).Where(c => x.ClutterAt(c) > 0).ToList();
            var a = Cl(st); var b = Cl(r.Get<Structure>());
            int at = st.CellOf(st.Props[st.IndexOf(id)]);
            Check("볼트: 길 값은 미끄러지는 가구를 따라간다 — 밀어 뜯은 그 틱에 길을 물어도 잠든 선반 칸에 길 값이 서고 · 찍고 되살린 세계와 같다 (예전엔 빈 제자리에 굳었다)",
                !L.Furniture[shelf].Cells.Contains(at) && a.Contains(at) && a.SequenceEqual(b), $"선반 칸 {at} · 길 값 칸 [{string.Join(",", a)}] / 되살린 [{string.Join(",", b)}]");
        }

        // ── 날아간 문짝: 한쪽이 진공인 닫힌 문이 압력에 날아가면 문짝이 물체 (Kind 3)가 되어 진공 쪽으로 날아간다 ──
        {
            const string art = "\n##########\n#...#....#\n#...D....#\n#...#....#\n##########";
            var lay = Worlds.Ascii(art);
            var s = FieldStructure(lay, 4);
            var f = s.Get<Field>(); var st = s.Get<Structure>();
            f.FillAir(250);   // 왼쪽 방은 250kPa (보통 문은 120kPa를 버틴다 — 1.5배 넘게 밀리면 문짝이 떨어져 난다)
            int door = 0, dc = lay.Doors[0].Cell;
            f.SetDoor(door, 0); st.RequestDoor(door, 0);
            foreach (int c in lay.Rooms.First(r => r.Cells.All(x => lay.X(x) > lay.X(dc))).Cells) { for (int g = 0; g < f.G; g++) f.N[c * f.G + g] = 0; f.U[c] = 0; }   // 오른쪽 방은 진공
            st.Track = new MotionTrack();   // 틱 안 기록: 문짝이 난 틱의 첫 열쇠틀 빠르기
            int leaf = -1; double vx = 0;
            for (int k = 0; k < 40 && leaf < 0; k++)
            {
                s.Step();
                int i = st.Props.FindIndex(p => p.Kind == 3);
                if (i >= 0) { leaf = st.Props[i].Id; var keys = st.Track.Of(MotionTrack.WhoProp, leaf); if (keys.Length > 0) vx = keys[0].Vx; }
            }
            s.Run(3);
            var lp = leaf > 0 && st.IndexOf(leaf) >= 0 ? st.Props[st.IndexOf(leaf)] : default;
            Check("볼트: 압력에 날아간 문짝이 물체 (문짝 꼴)가 되어 진공 쪽으로 날아간다 — 문은 열린 채 (예전엔 열린 채 끼일 뿐이었다)",
                leaf > 0 && vx > 5 && lp.Kind == 3 && Math.Abs(lp.Kg - st.LeafKg) < 1e-3 && st.DoorsBlown > 0 && f.DoorOpen[door] > 0.9f, $"문짝 {leaf} ({lp.Kg:0}kg) · 처음 빠르기 {vx:0.0}m/s (진공 쪽 +) · 지금 x {lp.X:0.00} · 날아간 문 {st.DoorsBlown} · 열림 {f.DoorOpen[door]:0.00}");
            // 문짝이 없는 문: 끼임을 풀고 (바로잡기) 닫으라 해도 열린 채 · 바로잡을 끼인 문으로 읽히지 않는다
            bool jamRead = st.JammedAt(dc);
            st.FreeDoor(door); st.RequestDoor(door, 0);
            s.Run(10);
            Check("볼트: 문짝이 날아간 문은 바로잡고 닫으라 해도 열린 채 — 바로잡을 끼인 문으로 읽히지 않는다 (예전엔 문짝 없는 문이 닫혀 진공 쪽을 막았다)",
                leaf > 0 && !jamRead && f.DoorOpen[door] > 0.9f && st.WhyShut(door) == DoorShut.None && !st.JammedAt(dc),
                $"끼인 문으로 읽힘 {jamRead} → {st.JammedAt(dc)} · 열림 {f.DoorOpen[door]:0.00} · 낌 {st.DoorJam[door]} · 안 열리는 까닭 {st.WhyShut(door)}");
        }

        // ── 폭풍에 날아간 문짝: 양쪽 기압이 같아도 (1기압) 문 바로 앞에서 터진 폭풍 (문 칸 첨두 95kPa — 되튀어 1.6배)에 문짝은 폭풍의 반대쪽으로 난다 ──
        {
            const string art = "\n##########\n#...#....#\n#...D....#\n#...#....#\n##########";
            var lay = Worlds.Ascii(art);
            var s = FieldStructure(lay, 4);
            var f = s.Get<Field>(); var st = s.Get<Structure>();
            int dc = lay.Doors[0].Cell;
            f.SetDoor(0, 0); st.RequestDoor(0, 0);
            s.Step();
            double w3 = 1;
            { double lo = 0.01, hi = 5; for (int k = 0; k < 60; k++) { double mid = (lo + hi) / 2; if (Structure.BlastKPa(4 / mid) < 95) lo = mid; else hi = mid; } w3 = (lo + hi) / 2; }
            st.Track = new MotionTrack();
            st.Blast(s, lay.Index(lay.X(dc) - 1, lay.Y(dc)), w3 * w3 * w3 * Structure.TntJ, s.Causes.Add(CauseKind.Setup, "배터리가 터졌다", s.Tick));
            s.Step();
            int i = st.Props.FindIndex(p => p.Kind == 3);
            double vx = 0;
            if (i >= 0) { var keys = st.Track.Of(MotionTrack.WhoProp, st.Props[i].Id); if (keys.Length > 0) vx = keys[0].Vx; }
            Check("볼트: 폭풍에 날아간 문짝은 양쪽 기압이 같아도 폭풍의 반대쪽으로 난다 (반사 첨두의 충격량 — 예전엔 기압 차가 없어 제자리에 떨어졌다)",
                i >= 0 && vx > 5 && st.DoorsBlown == 1, $"문짝 {(i >= 0 ? st.Props[i].Id : -1)} · 처음 빠르기 {vx:0.0}m/s (폭풍 반대쪽 +) · 날아간 문 {st.DoorsBlown}");
        }
    }
}
