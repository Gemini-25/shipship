using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>④ 칸 강체 규칙: 떨어져 나간 조각이 뿜으며 돌아 멀어지고 안의 것이 함께 간다 · 점화하면 조각이 뒤로 남는다 · 화면에 돌아간 조각이 그 자리에.</summary>
public static partial class Program
{
    // 큰 방 | 통로 | 방 | 통로 | 방 — 첫 통로를 끊으면 두 방짜리 조각이 떨어지고 · 둘째 통로를 끊으면 그 조각이 또 갈라진다
    private const string Triple = @"
#################################   #########   #########
#...............................#####.......#####.......#
#...............................P...P.......P...P.......#
#...............................#####.......#####.......#
#################################   #########   #########";
    private static void BodyRules()
    {
        if (!Do("칸 강체")) return;
        Console.WriteLine("\n[④ 칸 강체 — 몸 · 뿜는 힘 · 그림]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 뿜으며 돈다: 통로가 끊긴 아령 배 — 오른쪽 방 윗벽 끝이 뚫려 있으면 떨어진 조각이 공기를 뿜으며 돌아 멀어지고 · 안의 사람 · 설비가 함께 간다 ──
        {
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = Worlds.Full(lay, 2);
            s.Get<Mind>().Crew(s, 2, 0, 0);
            var st = s.Get<Structure>(); var mind = s.Get<Mind>();
            s.Run(Time.Minutes(1));
            int cause = s.Causes.Add(CauseKind.Setup, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), cause);
            st.Destroy(s, Worlds.At(lay, 18, 0), s.Causes.Add(CauseKind.Setup, "파편이 오른쪽 방 윗벽을 뚫었다", s.Tick));
            int right = lay.Room[Worlds.At(lay, 15, 3)];
            var inRight = mind.Agents.Where(a => a.IsPerson && lay.Room[a.C.Cell] == right).ToList();
            s.Run(Time.Minutes(2));
            var tumble = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Tumble);
            string story = tumble.Kind == FactKind.Tumble ? s.Causes.Story(tumble.Cause) : "";
            var frag = st.Bodies.Count > 1 ? st.Bodies[1] : default; var ship = st.Bodies.Count > 0 ? st.Bodies[0] : default;
            double moved = inRight.Count == 0 ? 0 : inRight.Min(a => { var (x, y) = st.ShipFrameOf(a.C.Cell); return Math.Sqrt((x - lay.X(a.C.Cell) - 0.5) * (x - lay.X(a.C.Cell) - 0.5) + (y - lay.Y(a.C.Cell) - 0.5) * (y - lay.Y(a.C.Cell) - 0.5)); });
            int pump = lay.Furniture.FindIndex(fu => fu.Cells.Length > 0 && lay.Room[fu.Cells[0]] == right);
            bool devWith = pump >= 0 && st.BodyOf(lay.Furniture[pump].Cells[0]) == 1;
            if (Debug) Console.WriteLine($"    {story}");
            Check("뿜으며 돈다: 떨어져 나간 조각의 뚫린 윗벽 끝으로 공기가 빠지며 조각을 반대로 밀어 돌리고 멀어진다 · 안의 사람 · 설비는 스크립트 없이 함께 간다 · 원인 사슬이 그 구멍까지",
                st.Bodies.Count == 2 && Math.Abs(frag.W) > 0.05 && st.Fragments[0].Dist > 20 && Math.Sqrt(ship.Vx * ship.Vx + ship.Vy * ship.Vy) < 0.1 &&
                inRight.Count > 0 && inRight.All(a => st.BodyOf(a.C.Cell) == 1) && moved > 20 && devWith && story.StartsWith("파편이 오른쪽 방 윗벽을 뚫었다") && story.Contains("돌기 시작했다"),
                $"조각 {frag.Mass / 1000:0.0}t · 분에 {frag.W * 60 / (2 * Math.PI):0.0}바퀴 · 떨어진 거리 {(st.Fragments.Count > 0 ? st.Fragments[0].Dist : 0):0}m · 배 빠르기 {Math.Sqrt(ship.Vx * ship.Vx + ship.Vy * ship.Vy):0.000}m/s · 사람 {inRight.Count}명 {moved:0}m 옮겨짐 · 설비 함께 {devWith} · \"{story}\"");

            // ── 그림: 화면은 돌아간 조각을 그 자리에 그린다 — 처음 자리는 우주가 되고 · 조각이 간 자리에 조각이 보인다 ──
            var p = new Painter(lay);
            double Bright(double cx, double cy)
            {
                var img = p.Frame(s, (int)Math.Floor(cx) - 2, (int)Math.Floor(cy) - 2, 5, 5, 0);
                double sum = 0; for (int i = 0; i < img.Px.Length; i += 4) sum += img.Px[i] + img.Px[i + 1] + img.Px[i + 2];
                return sum / (img.Px.Length / 4) / 3;
            }
            var (fx, fy) = st.LocalOf(0, frag.X, frag.Y);
            double there = Bright(fx, fy), was = Bright(frag.Lx, frag.Ly);
            Check("그림: 화면은 떨어져 나간 조각을 제 자세대로 돌려 간 자리에 그린다 — 조각이 있던 자리는 우주 (어둡다) · 간 자리는 조각 (밝다)",
                there > was + 10, $"간 자리 ({fx:0},{fy:0}) 밝기 {there:0} · 있던 자리 ({frag.Lx:0},{frag.Ly:0}) {was:0}");
        }

        // ── (나) 덜컹임: 떨어진 조각을 무언가 세게 밀면 (3m/s) 고정 안 된 상자는 바닥이 움직인 반대쪽으로 미끄러지고 (v²/2μg) · 선 사람은 넘어진다 ──
        // ── (나) 도는 조각: 분에 14바퀴로 돌면 무게중심에서 먼 상자는 원심(ω²r)이 마찰을 넘어 바깥 벽으로 밀려간다 ──
        {
            (Sim s, Structure st, Mind m, int person) Split()
            {
                var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
                var s = Worlds.Full(lay, 2);
                var m = s.Get<Mind>();
                m.Crew(s, 2, 0, 0);
                var st = s.Get<Structure>();
                s.Run(Time.Minutes(1));
                int cause = s.Causes.Add(CauseKind.Setup, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
                for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), cause);
                s.Run(3);
                int p = m.Agents.First(a => a.IsPerson).Id;
                ref var c = ref m[p].C; c.Cell = Worlds.At(lay, 16, 3); c.Sleepy = 0; s.Get<Life>()[c.Body].Asleep = 0;
                return (s, st, m, p);
            }
            var layD = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            {
                var (s, st, m, p) = Split();
                byte box = (byte)m.ItemIndex("식량 상자");
                int start = Worlds.At(layD, 16, 4);
                m.Loose.Add(new LooseItem { Item = box, Cell = start });
                var o = st.Bodies[1];
                int hit = s.Causes.Add(CauseKind.Setup, "떠도는 잔해가 조각을 들이받았다", s.Tick);
                st.Push(1, o.X, o.Y, o.Mass * 3, 0, hit);
                s.Step();
                s.Run(2);
                var it = m.Loose.FirstOrDefault(x => x.Item == box, new LooseItem { Cell = -1 });
                int moved = it.Cell < 0 ? 0 : layD.X(start) - layD.X(it.Cell);
                var fell = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Fell && x.Actor == p);
                string story = fell.Kind == FactKind.Fell ? s.Causes.Story(fell.Cause) : "";
                if (Debug) Console.WriteLine($"    {story}");
                Check("덜컹임: 떨어진 조각이 3m/s로 밀리면 고정 안 된 상자는 바닥이 움직인 반대쪽으로 미끄러지고 (마찰 0.3 — 1.5m) · 선 사람은 넘어진다 · 원인 사슬이 들이받은 것까지",
                    moved >= 1 && moved <= 3 && it.Cell >= 0 && layD.Y(it.Cell) == layD.Y(start) && story.Contains("덜컹임에 넘어졌다") && story.StartsWith("떠도는 잔해가 조각을 들이받았다"),
                    $"상자 {moved}칸 뒤로 · \"{story}\"");
            }
            {
                var (s, st, m, _) = Split();
                byte box = (byte)m.ItemIndex("식량 상자");
                int start = Worlds.At(layD, 18, 3);
                m.Loose.Add(new LooseItem { Item = box, Cell = start });
                var o = st.Bodies[1];
                double j = 1.5 * o.Inertia / 2;   // 짝힘 — 분에 14바퀴
                int spin = s.Causes.Add(CauseKind.Setup, "떠도는 잔해가 조각 끝을 스치고 지나갔다", s.Tick);
                st.Push(1, o.X, o.Y + 1, j, 0, spin); st.Push(1, o.X, o.Y - 1, -j, 0, spin);
                double R(int c) { var b = st.Bodies[1]; double dx = layD.X(c) + 0.5 - b.Lx, dy = layD.Y(c) + 0.5 - b.Ly; return Math.Sqrt(dx * dx + dy * dy); }
                double r0 = R(start);
                s.Run(12);
                var it = m.Loose.FirstOrDefault(x => x.Item == box, new LooseItem { Cell = -1 });
                double r1 = it.Cell >= 0 ? R(it.Cell) : 0;
                Span<int> nb = stackalloc int[4];
                bool byWall = false;
                if (it.Cell >= 0) { layD.Neighbors(it.Cell, nb); foreach (int q in nb) if (q >= 0 && layD.Kind[q] == CellKind.Wall) byWall = true; }
                Check("도는 조각: 분에 14바퀴로 돌면 무게중심에서 1.7m 떨어진 상자는 원심(ω²r 3.8m/s²)이 마찰(2.9m/s²)을 넘어 바깥 벽까지 밀려간다",
                    Math.Abs(st.Bodies[1].W) > 1.4 && r1 > r0 + 0.9 && byWall, $"분에 {Math.Abs(st.Bodies[1].W) * 60 / (2 * Math.PI):0.0}바퀴 · 상자 무게중심에서 {r0:0.0} → {r1:0.0}m · 벽 곁 {byWall}");
            }
        }

        // ── (다) 되돌아와 부딪힌다: 떨어진 조각의 먼 쪽 벽이 뚫려 있으면 뿜는 공기가 조각을 배 쪽으로 밀어 부딪힌다 — 배도 밀리고 · 둘은 튕겨 떨어진다 ──
        {
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = Worlds.Full(lay, 2);
            s.Get<Mind>().Crew(s, 2, 0, 0);
            var st = s.Get<Structure>();
            s.Run(Time.Minutes(1));
            int cause = s.Causes.Add(CauseKind.Setup, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), cause);
            st.Destroy(s, Worlds.At(lay, 20, 3), s.Causes.Add(CauseKind.Setup, "파편이 오른쪽 방 바깥 벽을 뚫었다", s.Tick));
            s.Run(Time.Minutes(1));
            var crash = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Crash);
            string story = crash.Kind == FactKind.Crash ? s.Causes.Story(crash.Cause) : "";
            var a = st.Bodies[0]; var b = st.Bodies.Count > 1 ? st.Bodies[1] : default;
            // 세상 틀로 잰다 (부딪혀 배도 돌면 배 틀에서는 조각이 배 둘레를 도는 것처럼 보인다)
            double gap0 = Math.Sqrt((b.Lx - a.Lx) * (b.Lx - a.Lx) + (b.Ly - a.Ly) * (b.Ly - a.Ly)), gap1 = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            double apart = (b.Vx - a.Vx) * (b.X - a.X) + (b.Vy - a.Vy) * (b.Y - a.Y);
            Check("되돌아와 부딪힌다: 떨어진 조각의 먼 쪽 벽이 뚫리면 뿜는 공기가 조각을 배 쪽으로 밀어 부딪히고 · 배도 밀려 움직이며 · 둘은 튕겨 떨어진다 · 원인 사슬이 그 구멍까지",
                st.Crashes >= 1 && story.StartsWith("파편이 오른쪽 방 바깥 벽을 뚫었다") && story.Contains("배에 부딪혔다") && Math.Sqrt(a.Vx * a.Vx + a.Vy * a.Vy) > 0.5 && gap1 > gap0 && apart > 0,
                $"부딪힘 {st.Crashes} · 배 빠르기 {Math.Sqrt(a.Vx * a.Vx + a.Vy * a.Vy):0.00}m/s · 무게중심 사이 {gap0:0.0} → {gap1:0.0}m · 멀어지는 중 {apart > 0} · \"{story}\"");
        }

        // ── (다) 세게 부딪히면: 50m/s로 들이받은 조각은 맞닿은 벽을 찢어 뚫고 (미노르스키 — 칸마다 으스러지는 에너지의 15% 넘게) · 방 공기가 샌다 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            int hit = s.Causes.Add(CauseKind.Setup, "떠도는 잔해가 조각을 배 쪽으로 세게 밀었다", s.Tick);
            st.Push(1, st.Bodies[1].X, st.Bodies[1].Y, -st.Bodies[1].Mass * 50, 0, hit);
            s.Run(10);
            var tear = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.WallBreach && s.Causes.Story(x.Cause).Contains("찢겨 뚫렸다"));
            string story = tear.Kind == FactKind.WallBreach ? s.Causes.Story(tear.Cause) : "";
            double p = f.Pressure(Worlds.At(lay, 2, 2));
            Check("세게 부딪히면: 50m/s로 들이받은 조각은 맞닿은 벽을 찢어 뚫고 방 공기가 샌다 · 원인 사슬 '밀었다 → 부딪혔다 → 찢겨 뚫렸다'",
                st.Crashes >= 1 && story.StartsWith("떠도는 잔해가") && story.Contains("부딪혔다") && p < 95, $"부딪힘 {st.Crashes} · 왼쪽 방 {p:0.0}kPa · \"{story}\"");
        }

        // ── (다) 조각도 또 갈라진다: 떨어져 나간 두 방짜리 조각의 목이 끊기면 그 조각이 다시 둘로 — 새 조각은 그 조각의 움직임을 이어받는다 ──
        {
            var lay = Worlds.Ascii(Triple, Worlds.MindLegend);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 1; y <= 3; y++) st.Destroy(s, Worlds.At(lay, 34, y), 0);
            s.Run(3);
            var f1 = st.Bodies.Count > 1 ? st.Bodies[1] : default;
            int cut = s.Causes.Add(CauseKind.Setup, "조각의 통로가 끊어졌다", s.Tick);
            for (int y = 1; y <= 3; y++) st.Destroy(s, Worlds.At(lay, 46, y), cut);
            s.Run(3);
            var split2 = s.Facts.Since(0).Where(x => x.Kind == FactKind.ShipSplit).ToList();
            string story = split2.Count > 1 ? s.Causes.Story(split2[1].Cause) : "";
            int midBody = st.BodyOf(Worlds.At(lay, 40, 2)), farBody = st.BodyOf(Worlds.At(lay, 52, 2)), shipBody = st.BodyOf(Worlds.At(lay, 8, 2));
            Check("조각도 또 갈라진다: 떨어져 나간 두 방짜리 조각의 통로가 끊기면 그 조각이 다시 둘로 갈라진다 ('떨어져 나간 조각이 또 갈라졌다') · 몸이 셋",
                st.Bodies.Count == 3 && shipBody == 0 && midBody != farBody && midBody > 0 && farBody > 0 && story.Contains("또 갈라졌다") && story.StartsWith("조각의 통로가 끊어졌다"),
                $"몸 {st.Bodies.Count} · 배 {shipBody} · 가운데 방 {midBody} · 끝 방 {farBody} · \"{story}\"");
        }

        // ── (라) 잔칸: 외판의 구멍은 넓이만큼 잔칸이 바깥 면에서 들쭉날쭉 이어져 파이고 · 통째로 나간 벽은 테두리 두셋만 · 그림의 빈 잔칸은 우주 ──
        {
            var lay = Worlds.Ascii(Twins);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            s.Run(2);
            int hull = Worlds.At(lay, 2, 0), gone = Worlds.At(lay, 7, 0);
            st.Impact(s, hull, 3e7, 0, 1, s.Causes.Add(CauseKind.Setup, "파편이 외판을 때렸다", s.Tick));
            st.Destroy(s, gone, 0);
            s.Run(2);
            ushort m = st.Sub[hull];
            int cleared = 16 - Structure.SubCount(m), want = (int)Math.Ceiling(16 * f.Hole[hull] / Field.WallFace);
            // 빈 잔칸이 한 덩이로 이어지고 바깥(위) 면에 닿는다
            var holes = Enumerable.Range(0, 16).Where(i => (m >> i & 1) == 0).ToList();
            var seen = new HashSet<int>(); var q = new Queue<int>();
            if (holes.Count > 0) { seen.Add(holes[0]); q.Enqueue(holes[0]); }
            while (q.Count > 0) { int i = q.Dequeue(); foreach (int j in new[] { i - 1, i + 1, i - 4, i + 4 }) if (j >= 0 && j < 16 && holes.Contains(j) && (Math.Abs(j % 4 - i % 4) + Math.Abs(j / 4 - i / 4)) == 1 && seen.Add(j)) q.Enqueue(j); }
            bool joined = seen.Count == holes.Count, outer = holes.Any(i => i / 4 == 0);
            int rim = Structure.SubCount(st.Sub[gone]);
            var p = new Painter(lay);
            var img = p.Frame(s, 0, 0, lay.W, lay.H, 0);
            int hi = holes.Count > 0 ? holes[0] : 0;
            int qx = lay.X(hull) * Painter.T + (hi % 4) * 8 + 4, qy = lay.Y(hull) * Painter.T + (hi / 4) * 8 + 4;
            var px = img.Get(qx, qy);
            bool spacePx = px.R < 30 && px.G < 30 && px.B < 40;
            Check("잔칸: 외판 구멍은 넓이만큼(벽 한 면 2.5m²가 16개) 잔칸이 바깥 면에서 한 덩이로 파이고 · 통째로 나간 벽은 테두리 두셋만 남고 · 그림의 빈 잔칸 자리는 우주",
                cleared == want && cleared >= 2 && joined && outer && rim >= 2 && rim <= 4 && spacePx,
                $"구멍 {f.Hole[hull]:0.00}m² · 빈 잔칸 {cleared} (넓이로 {want}) · 이어짐 {joined} · 바깥 면 {outer} · 통째로 나간 벽 테두리 {rim} · 그 자리 도트 {px.R},{px.G},{px.B}");
        }

        // ── (라) 찢긴 끝: 갈라진 사이 칸은 가운데가 들쭉날쭉 찢겨 · 남은 잔칸이 양쪽 몸에 붙는다 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            int left = 0, right = 0, total = 0;
            for (int y = 0; y < 5; y++)
            {
                int c = Worlds.At(lay, 6, y);
                for (int i = 0; i < 16; i++) if (st.SubAt(c, i % 4, i / 4)) { total++; int o = st.TornOwner(c, i % 4, i / 4); if (o == 0) left++; else if (o == 1) right++; }
            }
            Check("찢긴 끝: 갈라진 사이 칸(우주가 된 칸)에 남은 잔칸이 양쪽 몸에 붙는다 — 두 몸 다 들쭉날쭉한 찢긴 끝을 가진다",
                st.Bodies.Count == 2 && left > 0 && right > 0 && total == left + right, $"남은 잔칸 {total} · 왼쪽 몸 {left} · 오른쪽 몸 {right}");
        }

        // ── (라) 부딪힘은 잔칸으로: 맞은편 벽의 앞쪽 절반이 파여 있으면 50cm를 더 와서야 닿는다 ──
        {
            int TicksToHit(bool carved)
            {
                var lay = Worlds.Ascii(Sealed);
                var s = FieldStructure(lay, 5);
                var st = s.Get<Structure>();
                st.Debris = false;   // 몸만 (갈라질 때 튄 부스러기가 두 몸을 밀지 않게)
                s.Run(3);
                for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
                s.Run(3);
                if (carved) for (int y = 0; y < 5; y++) st.Sub[Worlds.At(lay, 7, y)] &= unchecked((ushort)~0x3333);   // 앞쪽(왼쪽) 두 줄 잔칸을 비운다
                st.Push(0, st.Bodies[0].X, st.Bodies[0].Y, st.Bodies[0].Mass * 0.1, 0);
                for (int k = 1; k <= 40; k++) { s.Step(); if (st.Crashes > 0) return k; }
                return 99;
            }
            int whole = TicksToHit(false), cut = TicksToHit(true);
            Check("부딪힘은 잔칸으로: 맞은편 벽의 앞쪽 절반(잔칸 두 줄)이 파여 있으면 50cm를 더 와서야 닿는다 (빈 잔칸에는 닿지 않는다)",
                whole < 99 && cut - whole >= 2 && cut - whole <= 5, $"닿기까지 성한 벽 {whole}틱 · 파인 벽 {cut}틱 (다가오는 빠르기 0.06m/s — 한 틱 14cm)");
        }

        // ── (마) 부스러기: 바깥에서 날아온 20kg 부스러기(300m/s · 900kJ)가 외판을 뚫는다 — 원인 사슬에 그 부스러기 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            s.Run(2);
            int wall = Worlds.At(lay, 2, 0);
            int src = s.Causes.Add(CauseKind.Setup, "이웃 배에서 떨어져 나온 철판 조각이 날아왔다", s.Tick);
            st.Throw(s, lay.X(wall) + 0.5, lay.Y(wall) - 6, 0, 300, 20, src);
            s.Run(1);
            var breach = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.WallBreach);
            string story = breach.Kind == FactKind.WallBreach ? s.Causes.Story(breach.Cause) : "";
            s.Run(20);
            double p = f.Pressure(Worlds.At(lay, 2, 2));
            if (Debug) Console.WriteLine($"    {story}");
            Check("부스러기: 바깥에서 날아온 20kg 부스러기(300m/s · 900kJ)가 외판을 뚫고 방 공기가 샌다 · 원인 사슬 '철판 조각이 날아왔다 → 날아온 부스러기가 부딪혔다 → 벽이 뚫렸다'",
                st.ShardHits == 1 && story.StartsWith("이웃 배에서") && story.Contains("부스러기") && f.Hole[wall] > 0 && p < 95,
                $"맞음 {st.ShardHits} · 구멍 {f.Hole[wall]:0.000}m² · 방 {p:0.0}kPa · \"{story}\"");
        }

        // ── (마) 뭉툭한 덩어리: 200kg 철판 덩어리가 12m/s(14kJ)로 외판에 부딪히면 찌그러뜨릴 뿐 뚫지 못한다 — 같은 에너지라도 2cm 파편(재질 표의 관통 값 9kJ)과 달리 (드 마르: 지름^1.5) ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            s.Run(2);
            int wall = Worlds.At(lay, 2, 0);
            st.Throw(s, lay.X(wall) + 0.5, lay.Y(wall) - 6, 0, 12, 200, s.Causes.Add(CauseKind.Setup, "떨어져 나간 판 덩어리가 천천히 다가왔다", s.Tick));
            s.Run(3);
            double p = f.Pressure(Worlds.At(lay, 2, 2));
            Check("뭉툭한 덩어리: 200kg 철판 덩어리가 12m/s(14kJ — 2cm 파편이면 외판을 뚫는 에너지)로 외판에 부딪혀도 뚫지 못한다 · 방 공기는 그대로",
                st.ShardHits == 1 && f.Hole[wall] == 0 && p > 100, $"맞음 {st.ShardHits} · 구멍 {f.Hole[wall]:0.000}m² · 방 {p:0.0}kPa");
        }

        // ── (마) 끊기만 한 벽 조각은 몸을 돌리지 않는다: 공기 없는 세 겹 벽의 가운데를 끊으면 부스러기는 천천히 흩어질 뿐 (0.3m/s) — 그 반동으로 조각이 돌지 않는다 ──
        //    공기가 있던 통로를 끊으면 빠지는 공기가 부스러기를 세게 민다 (기압 차 × 잔칸 면 × 0.05초 — 1기압이면 20m/s) · 그 운동량은 기체가 준 것이라 몸을 밀지 않는다
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            double w = st.Bodies.Count > 1 ? Math.Abs(st.Bodies[1].W) : 1, slow = 0;
            foreach (var sh in st.Shards) slow = Math.Max(slow, Math.Sqrt((sh.Vx - st.Bodies[1].Vx) * (sh.Vx - st.Bodies[1].Vx) + (sh.Vy - st.Bodies[1].Vy) * (sh.Vy - st.Bodies[1].Vy)));
            long made = st.ShardsMade;
            var lay2 = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s2 = FieldStructure(lay2, 2);
            var st2 = s2.Get<Structure>();
            s2.Run(3);
            for (int y = 2; y <= 4; y++) st2.Destroy(s2, Worlds.At(lay2, 10, y), 0);
            s2.Run(1);
            double fast = 0; int n2 = 0;
            foreach (var sh in st2.Shards) { var o = st2.Bodies[0]; fast += Math.Sqrt((sh.Vx - o.Vx) * (sh.Vx - o.Vx) + (sh.Vy - o.Vy) * (sh.Vy - o.Vy)); n2++; }
            fast /= Math.Max(1, n2);
            Check("부스러기의 반동: 공기 없는 벽을 끊으면 부스러기는 천천히 흩어지고 (1m/s 아래) 떨어진 조각은 돌지 않는다 · 공기 찬 통로를 끊으면 빠지는 공기가 부스러기를 세게 민다 (평균 10m/s 넘게)",
                made > 0 && slow < 1 && w < Structure.SpinRadS && st.Spins == 0 && n2 > 0 && fast > 10,
                $"끊은 벽 부스러기 {made} · 가장 빠른 {slow:0.00}m/s · 조각 각속도 {w:0.0000}rad/s · 통로 부스러기 {n2} · 평균 {fast:0.0}m/s");
        }

        // ── (마) 부스러기가 튀는 쪽: 통로를 끊으면 벽 조각은 우주 쪽(통로에 수직 — 위 · 아래)으로 날고 · 바닥 판은 빠지는 공기에 평면 밖(아래)으로 떠난다 ──
        //    (전에는 사이 칸 조각이 '몸 바깥쪽' = 상대 몸 쪽으로 · 바닥 판이 화면 안 비스듬히 20m/s로 튀어 곧장 옆 몸을 때렸다)
        {
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = FieldStructure(lay, 2);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), 0);
            s.Step();
            var o = st.Bodies[0];
            int side = 0, n = 0;
            foreach (var sh in st.Shards) { double vx = Math.Abs(sh.Vx - o.Vx), vy = Math.Abs(sh.Vy - o.Vy); n++; if (vx > vy) side++; }
            Check("부스러기가 튀는 쪽: 통로를 끊으면 벽 조각은 통로에 수직으로 (위 · 아래 우주 쪽) 날고 · 바닥 판은 평면 밖으로 떠난다 (장부 '우주로')",
                n > 0 && side == 0 && st.ShardKgLost > 0, $"날고 있는 {n} · 통로 쪽으로 튄 {side} · 평면 밖으로 {st.ShardKgLost:0}kg");
        }

        // ── (마) 부스러기는 몸을 민다: 떨어진 조각에 100kg 부스러기가 50m/s로 박히면 그 운동량만큼 (조각 12.5t — 0.4m/s) 밀린다 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>();
            st.Debris = false;
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            var f0 = st.Bodies[1];
            st.Throw(s, f0.X + 12, f0.Y, -10 + f0.Vx, f0.Vy, 100, 0);
            s.Run(3);
            var f1 = st.Bodies[1];
            double expect = 100 * 10 / (f0.Mass + 0.0);
            Check("부스러기는 몸을 민다: 떨어진 조각에 100kg 부스러기가 10m/s로 박히면 그 운동량만큼 밀린다 (5kJ — 벽은 찌그러지기만 · 조각 12.5t면 8cm/s)",
                st.ShardHits == 1 && Math.Abs((f0.Vx - f1.Vx) - expect) < 0.02 * expect, $"맞음 {st.ShardHits} · 조각 빠르기 {f0.Vx:0.000} → {f1.Vx:0.000} (바뀜 {f0.Vx - f1.Vx:0.000} · 셈 {expect:0.000}m/s)");
        }

        // ── (마) 갈라질 때 부스러기가 튀고 화면에 보인다 · 멀리 가면 장부 '우주로' ──
        {
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = FieldStructure(lay, 2);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), 0);
            s.Run(1);
            int made = (int)st.ShardsMade, flying = st.Shards.Count;
            // 빠지는 공기에 밀린 부스러기는 한 틱(2.4초)에 수십 m — 부스러기마다 그 둘레를 그려 본다
            var p = new Painter(lay);
            int seen = 0;
            foreach (var sh in st.Shards)
            {
                var (sx, sy) = st.LocalOf(0, sh.X, sh.Y);
                int x0 = (int)Math.Floor(sx) - 3, y0 = (int)Math.Floor(sy) - 3;
                var img = p.Frame(s, x0, y0, 7, 7, 0);
                int px = (int)Math.Round((sx - x0) * Painter.T), py = (int)Math.Round((sy - y0) * Painter.T);
                if (px < 0 || py < 0 || px >= img.W || py >= img.H) continue;
                var c = img.Get(px, py);
                if (c.R == 0x9a && c.G == 0xa2 && c.B == 0xb0) seen++;
            }
            s.Run(150);
            Check("갈라질 때 부스러기: 끊긴 통로의 부서진 판이 알갱이로 튀어 날고 화면에 보인다 · 300m 넘게 멀어지면 장부 '우주로'",
                made >= 3 && flying >= 3 && seen >= 1 && st.ShardKgLost > 0 && st.Shards.Count < flying,
                $"부스러기 {made} · 날고 있는 {flying} · 화면에 보인 {seen} · 150틱 뒤 날고 있는 {st.Shards.Count} · 우주로 {st.ShardKgLost:0}kg");
        }

        // ── (바) 관전 화면: 갈라진 조각은 틱(2.4초) 사이에도 미끄러지듯 움직이고 (지난 틱 → 지금 자세를 메운다 — 걸음처럼 한 틱 늦게) · 카메라는 도면 밖으로 멀어진 조각까지 간다 ──
        {
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = Worlds.Full(lay, 2);
            s.Get<Mind>().Crew(s, 2, 0, 0);
            s.Run(Time.Minutes(1));
            var st = s.Get<Structure>();
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), 0);
            st.Destroy(s, Worlds.At(lay, 20, 3), 0);
            var show = new ShipView.LiveShow(s, "아령") { SpeedOverride = 1, Follow = false };
            double jump = 0, tickMove = 0, px = double.NaN, py = 0, tx = double.NaN, ty = 0;
            for (int k = 0; k < 12 * 12; k++)   // 화면 12초 (틱 12) · 한 틱에 12장
            {
                int ran = show.Advance(1.0 / 12);
                var (x, y) = show.DrawnBody(1);
                if (double.IsNaN(x)) continue;
                if (!double.IsNaN(px)) jump = Math.Max(jump, Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)));
                (px, py) = (x, y);
                if (ran > 0)
                {
                    var b = st.Bodies[1]; var (lx, ly) = st.LocalOf(0, b.X, b.Y);
                    if (!double.IsNaN(tx)) tickMove = Math.Max(tickMove, Math.Sqrt((lx - tx) * (lx - tx) + (ly - ty) * (ly - ty)));
                    (tx, ty) = (lx, ly);
                }
            }
            s.Run(40);   // 멀리 간 뒤 — 조각 안 칸을 보라고 하면 그 조각이 그려진 곳으로 · 그다음 전체 보기
            int inFrag = Worlds.At(lay, 16, 3);
            show.LookAt(inFrag);
            var look = show.Camera(); var (gx, gy) = st.ShipFrameOf(inFrag);
            bool looked = st.BodyOf(inFrag) == 1 && gx >= look.left && gx <= look.left + look.w && gy >= look.top && gy <= look.top + look.h;
            show.Overview = true; show.Paused = true;
            for (int k = 0; k < 60; k++) show.Advance(1.0 / 12);   // 카메라는 부드럽게 옮겨 간다 (멈춘 채 화면 5초)
            var cam = show.Camera(); var (fx, fy) = show.DrawnBody(1);
            bool seen = fx >= cam.left && fx <= cam.left + cam.w && fy >= cam.top && fy <= cam.top + cam.h;
            Check("관전 화면: 갈라진 조각은 틱 사이에도 미끄러지듯 움직이고 (화면 한 장에 한 틱 움직임의 4분의 1 아래) · 조각 안 칸을 보라고 하면 카메라가 그 조각으로 가고 · 전체 보기 카메라는 도면 밖으로 멀어진 조각까지 담는다",
                tickMove > 0.5 && jump < 0.25 * tickMove && seen && looked && (fx < 0 || fx > lay.W || fy < 0 || fy > lay.H),
                $"조각 안 칸 보기 {looked} · 한 틱에 가장 멀리 {tickMove:0.00}칸 · 화면 한 장에 가장 멀리 {jump:0.00}칸 · 조각 그려진 자리 {fx:0},{fy:0} (도면 {lay.W}×{lay.H}) · 카메라 {cam.left:0},{cam.top:0} {cam.w:0}×{cam.h:0}");
        }

        // ── 점화: 배가 가속하면 배만 나아가고 떨어진 조각은 뒤에 남는다 — 멀어진 거리 = ½at² · 몸들의 운동량 = 점화가 넣은 운동량 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>();
            st.Debris = false;   // 몸만 (갈라질 때 튄 부스러기가 두 몸을 밀지 않게)
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            var f0 = st.Bodies.Count > 1 ? st.Bodies[1] : default; var s0 = st.Bodies[0];
            double d0 = st.Fragments.Count > 0 ? st.Fragments[0].Dist : 0;
            double a = 0.05 * 9.81; int ticks = 10;
            for (int k = 0; k < ticks; k++) { st.ShipAx = -a; st.ShipAy = 0; s.Step(); }   // 조각 반대쪽으로 (조각 쪽으로 켜면 들이받는다)
            st.ShipAx = 0;
            double t = ticks * Time.SecondsPerTick;
            var s1 = st.Bodies[0]; var f1 = st.Bodies.Count > 1 ? st.Bodies[1] : default;
            double shipGone = -(s1.X - s0.X - s0.Vx * t), expect = 0.5 * a * t * t;
            var (px, py, _, _) = st.BodyMomentum();
            Check("점화: 배가 0.05g로 24초 가속하면 배만 ½at²만큼 나아가고 떨어진 조각은 제 빠르기 그대로 뒤에 남는다 · 몸들의 운동량 = 점화가 넣은 운동량",
                st.Bodies.Count == 2 && Rel(shipGone, expect) < 1e-6 && f1.Vx == f0.Vx && Rel(px, st.BurnPx) < 1e-9 && Rel(Math.Sqrt((s0.Vx - s1.Vx) * (s0.Vx - s1.Vx) + (s0.Vy - s1.Vy) * (s0.Vy - s1.Vy)), a * t) < 1e-7 && st.Crashes == 0,   // 배 틀로 켠다 (갈라질 때 조금 돌았으면 켜는 동안 방향이 0.02° 돌아 크기가 10⁻⁸쯤 준다)
                $"배가 더 간 거리 {shipGone:0.0}m (½at² {expect:0.0}m) · 조각 빠르기 {f0.Vx:0.000} → {f1.Vx:0.000} · 떨어진 거리 {d0:0.0} → {(st.Fragments.Count > 0 ? st.Fragments[0].Dist : 0):0.0}m · 운동량 {px:0} / 점화 {st.BurnPx:0}N·s");
        }
    }

}
