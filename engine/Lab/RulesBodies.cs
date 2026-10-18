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
            int cause = s.Causes.Add(CauseKind.Story, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), cause);
            st.Destroy(s, Worlds.At(lay, 18, 0), s.Causes.Add(CauseKind.Story, "파편이 오른쪽 방 윗벽을 뚫었다", s.Tick));
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
                int cause = s.Causes.Add(CauseKind.Story, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
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
                int hit = s.Causes.Add(CauseKind.Story, "떠도는 잔해가 조각을 들이받았다", s.Tick);
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
                int spin = s.Causes.Add(CauseKind.Story, "떠도는 잔해가 조각 끝을 스치고 지나갔다", s.Tick);
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
            int cause = s.Causes.Add(CauseKind.Story, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), cause);
            st.Destroy(s, Worlds.At(lay, 20, 3), s.Causes.Add(CauseKind.Story, "파편이 오른쪽 방 바깥 벽을 뚫었다", s.Tick));
            s.Run(Time.Minutes(1));
            var crash = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Crash);
            string story = crash.Kind == FactKind.Crash ? s.Causes.Story(crash.Cause) : "";
            var a = st.Bodies[0]; var b = st.Bodies.Count > 1 ? st.Bodies[1] : default;
            var (sx, _) = st.LocalOf(0, b.X, b.Y);
            if (Debug) Console.WriteLine($"    {story}");
            Check("되돌아와 부딪힌다: 떨어진 조각의 먼 쪽 벽이 뚫리면 뿜는 공기가 조각을 배 쪽으로 밀어 부딪히고 · 배도 밀려 움직이며 · 둘은 튕겨 떨어진다 · 원인 사슬이 그 구멍까지",
                st.Crashes >= 1 && story.StartsWith("파편이 오른쪽 방 바깥 벽을 뚫었다") && story.Contains("배에 부딪혔다") && Math.Sqrt(a.Vx * a.Vx + a.Vy * a.Vy) > 0.5 && sx > b.Lx - 1,
                $"부딪힘 {st.Crashes} · 배 빠르기 {Math.Sqrt(a.Vx * a.Vx + a.Vy * a.Vy):0.00}m/s · 조각 무게중심 배 틀 x {sx:0.0} (처음 {b.Lx:0.0}) · \"{story}\"");
        }

        // ── (다) 세게 부딪히면: 50m/s로 들이받은 조각은 맞닿은 벽을 찢어 뚫고 (미노르스키 — 칸마다 으스러지는 에너지의 15% 넘게) · 방 공기가 샌다 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            int hit = s.Causes.Add(CauseKind.Story, "떠도는 잔해가 조각을 배 쪽으로 세게 밀었다", s.Tick);
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
            int cut = s.Causes.Add(CauseKind.Story, "조각의 통로가 끊어졌다", s.Tick);
            for (int y = 1; y <= 3; y++) st.Destroy(s, Worlds.At(lay, 46, y), cut);
            s.Run(3);
            var split2 = s.Facts.Since(0).Where(x => x.Kind == FactKind.ShipSplit).ToList();
            string story = split2.Count > 1 ? s.Causes.Story(split2[1].Cause) : "";
            int midBody = st.BodyOf(Worlds.At(lay, 40, 2)), farBody = st.BodyOf(Worlds.At(lay, 52, 2)), shipBody = st.BodyOf(Worlds.At(lay, 8, 2));
            Check("조각도 또 갈라진다: 떨어져 나간 두 방짜리 조각의 통로가 끊기면 그 조각이 다시 둘로 갈라진다 ('떨어져 나간 조각이 또 갈라졌다') · 몸이 셋",
                st.Bodies.Count == 3 && shipBody == 0 && midBody != farBody && midBody > 0 && farBody > 0 && story.Contains("또 갈라졌다") && story.StartsWith("조각의 통로가 끊어졌다"),
                $"몸 {st.Bodies.Count} · 배 {shipBody} · 가운데 방 {midBody} · 끝 방 {farBody} · \"{story}\"");
        }

        // ── 점화: 배가 가속하면 배만 나아가고 떨어진 조각은 뒤에 남는다 — 멀어진 거리 = ½at² · 몸들의 운동량 = 점화가 넣은 운동량 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>();
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
                st.Bodies.Count == 2 && Rel(shipGone, expect) < 1e-6 && f1.Vx == f0.Vx && Rel(px, st.BurnPx) < 1e-9 && Rel(s0.Vx - s1.Vx, a * t) < 1e-9 && st.Crashes == 0,
                $"배가 더 간 거리 {shipGone:0.0}m (½at² {expect:0.0}m) · 조각 빠르기 {f0.Vx:0.000} → {f1.Vx:0.000} · 떨어진 거리 {d0:0.0} → {(st.Fragments.Count > 0 ? st.Fragments[0].Dist : 0):0.0}m · 운동량 {px:0} / 점화 {st.BurnPx:0}N·s");
        }
    }

}
