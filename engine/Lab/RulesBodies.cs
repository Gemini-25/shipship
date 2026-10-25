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
    /// <summary>틱 처음 각속도 w0 · 끝 w1로 고르게 가속하는 몸이 t초에 돈 각 (W₀t + ½αt²).</summary>
    private static double SpinAt(double w0, double w1, double t) => w0 * t + 0.5 * (w1 - w0) / Time.SecondsPerTick * t * t;

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
            mind.AddPerson(s, Worlds.At(lay, 15, 3));   // 방 가운데 (뚫릴 윗벽에서 먼 이) — 조각과 함께 간다
            int cause = s.Causes.Add(CauseKind.Setup, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), cause);
            st.Destroy(s, Worlds.At(lay, 18, 0), s.Causes.Add(CauseKind.Setup, "파편이 오른쪽 방 윗벽을 뚫었다", s.Tick));
            int right = lay.Room[Worlds.At(lay, 15, 3)];
            var inRight = mind.Agents.Where(a => a.IsPerson && lay.Room[a.C.Cell] == right).ToList();
            s.Run(Time.Minutes(2));
            var tumble = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Tumble);
            string story = tumble.Kind == FactKind.Tumble ? s.Causes.Story(tumble.Cause) : "";
            var frag = st.Bodies.Count > 1 ? st.Bodies[1] : default; var ship = st.Bodies.Count > 0 ? st.Bodies[0] : default;
            // ③ 국소 현상: 뚫린 윗벽 바로 곁에 있던 이는 구멍 입으로 빨려 나간다 — 나머지는 조각과 함께 간다
            var spaced = inRight.Where(a => a.C.Adrift != 0).ToList();
            bool spacedTold = spaced.All(a => s.Facts.Since(0).Any(x => x.Kind == FactKind.WentOutside && x.Actor == a.Id));
            inRight = inRight.Where(a => a.C.Adrift == 0).ToList();
            double moved = inRight.Count == 0 ? 0 : inRight.Min(a => { var (x, y) = st.ShipFrameOf(a.C.Cell); return Math.Sqrt((x - lay.X(a.C.Cell) - 0.5) * (x - lay.X(a.C.Cell) - 0.5) + (y - lay.Y(a.C.Cell) - 0.5) * (y - lay.Y(a.C.Cell) - 0.5)); });
            int pump = lay.Furniture.FindIndex(fu => fu.Cells.Length > 0 && lay.Room[fu.Cells[0]] == right);
            bool devWith = pump >= 0 && st.BodyOf(lay.Furniture[pump].Cells[0]) == 1;
            if (Debug) Console.WriteLine($"    {story} · 사람: " + string.Join(" · ", inRight.Select(a => $"{mind.NameOf(a.Id)}@{lay.X(a.C.Cell)},{lay.Y(a.C.Cell)} 몸 {st.BodyOf(a.C.Cell)} 떠감 {a.C.Adrift} 붙음 {a.C.PinAt}")));
            Check("뿜으며 돈다: 떨어져 나간 조각의 뚫린 윗벽 끝으로 공기가 빠지며 조각을 반대로 밀어 돌리고 멀어진다 · 안의 사람 · 설비는 스크립트 없이 함께 간다 (구멍 바로 곁의 이는 빨려 나간다) · 원인 사슬이 그 구멍까지",
                st.Bodies.Count == 2 && Math.Abs(frag.W) > 0.05 && st.Fragments[0].Dist > 20 && Math.Sqrt(ship.Vx * ship.Vx + ship.Vy * ship.Vy) < 0.1 &&
                inRight.Count > 0 && inRight.All(a => st.BodyOf(a.C.Cell) == 1) && moved > 20 && spacedTold && devWith && story.StartsWith("파편이 오른쪽 방 윗벽을 뚫었다") && story.Contains("돌기 시작했다"),
                $"조각 {frag.Mass / 1000:0.0}t · 분에 {frag.W * 60 / (2 * Math.PI):0.0}바퀴 · 떨어진 거리 {(st.Fragments.Count > 0 ? st.Fragments[0].Dist : 0):0}m · 배 빠르기 {Math.Sqrt(ship.Vx * ship.Vx + ship.Vy * ship.Vy):0.000}m/s · 사람 {inRight.Count}명 {moved:0}m 옮겨짐 (구멍 곁에서 빨려 나간 이 {spaced.Count}) · 설비 함께 {devWith} · \"{story}\"");

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
                Worlds.PutItem(s, box, start);
                var o = st.Bodies[1];
                int hit = s.Causes.Add(CauseKind.Setup, "떠도는 잔해가 조각을 들이받았다", s.Tick);
                st.Push(1, o.X, o.Y, o.Mass * 3, 0, hit);
                s.Step();
                s.Run(2);
                var it = Worlds.Items(s).FirstOrDefault(x => x.Item == box, new Worlds.FloorItem(0, 0, -1, 0, 0));
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
                int start = Worlds.At(layD, 17, 5);   // 바깥 구석 쪽 바닥 (설비 칸 · 설비 곁이 아니다 — ④ 물체는 제자리 가구에 걸린다)
                Worlds.PutItem(s, box, start);
                var o = st.Bodies[1];
                double j = 1.5 * o.Inertia / 2;   // 짝힘 — 분에 14바퀴
                int spin = s.Causes.Add(CauseKind.Setup, "떠도는 잔해가 조각 끝을 스치고 지나갔다", s.Tick);
                st.Push(1, o.X, o.Y + 1, j, 0, spin); st.Push(1, o.X, o.Y - 1, -j, 0, spin);
                double R(int c) { var b = st.Bodies[1]; double dx = layD.X(c) + 0.5 - b.Lx, dy = layD.Y(c) + 0.5 - b.Ly; return Math.Sqrt(dx * dx + dy * dy); }
                double r0 = R(start);
                s.Run(12);
                var it = Worlds.Items(s).FirstOrDefault(x => x.Item == box, new Worlds.FloorItem(0, 0, -1, 0, 0));
                int pi = st.Props.FindIndex(x => x.Kind == 0 && x.What == box);
                double r1 = pi >= 0 ? Math.Sqrt(Math.Pow(st.Props[pi].X - st.Bodies[1].Lx, 2) + Math.Pow(st.Props[pi].Y - st.Bodies[1].Ly, 2)) : 0;   // 실수 자리 (④)
                Span<int> nb = stackalloc int[4];
                bool byWall = false;
                if (it.Cell >= 0) { layD.Neighbors(it.Cell, nb); foreach (int q in nb) if (q >= 0 && layD.Kind[q] == CellKind.Wall) byWall = true; }
                Check("도는 조각: 분에 14바퀴로 돌면 무게중심에서 먼 상자는 원심(ω²r)이 정지 마찰(3.7m/s²)을 넘어 바깥 벽까지 밀려간다 (④ 실수 자리 — 코리올리로 휘며)",
                    Math.Abs(st.Bodies[1].W) > 1.4 && r1 > r0 + 0.25 && byWall, $"분에 {Math.Abs(st.Bodies[1].W) * 60 / (2 * Math.PI):0.0}바퀴 · 상자 무게중심에서 {r0:0.0} → {r1:0.0}m · 벽 곁 {byWall}");
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
                    else tickMove = Math.Max(tickMove, Math.Sqrt((lx - x) * (lx - x) + (ly - y) * (ly - y)));   // 첫 틱: 그 틱 처음 그린 자리에서 (틱 안 기록은 그 틱의 참 길 — 가속해 틱 끝에 빠르다)
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

        // ── (사) 도는 조각 그림 각: 한 틱(2.4초)에 반 바퀴 넘게 도는 조각 · 배(별)도 틱 사이를 감지 않은 각으로 메운다 — 도는 쪽으로만 · 고르게 (감으면 4.8rad가 −1.48rad가 되어 거꾸로 돈다) ──
        {
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = Worlds.Full(lay, 2);
            s.Get<Mind>().Crew(s, 2, 0, 0);
            var st = s.Get<Structure>();
            s.Run(Time.Minutes(1));
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), 0);
            s.Run(3);
            var show = new ShipView.LiveShow(s, "아령") { SpeedOverride = 1, Follow = false };
            static void SpinTo(Structure st, int b, double w) { var o = st.Bodies[b]; double j = (o.W - w) * o.Inertia / 2; st.Push(b, o.X, o.Y + 1, j, 0); st.Push(b, o.X, o.Y - 1, -j, 0); }   // 짝힘 — 각속도를 w로
            void Spin(int b, double w) => SpinTo(st, b, w);
            double Rel1() => st.Bodies[1].Th - st.Bodies[0].Th;
            // 조각: 배에 대해 2.0rad/s
            Spin(1, st.Bodies[0].W + 2.0);
            double w1 = st.Bodies[1].W - st.Bodies[0].W, r0 = Rel1();
            int ran = show.Advance(1.0);
            double turned = Rel1() - r0;
            var th = new[] { 0, 0.25, 0.5, 0.75, 1 }.Select(f => show.DrawnTh(1, f)).ToArray();
            double worst = 0; bool forward = true;
            for (int k = 1; k < th.Length; k++) { double d = th[k] - th[k - 1]; forward &= d * w1 > 0; worst = Math.Max(worst, Math.Abs(d - w1 * Time.SecondsPerTick * 0.25)); }
            // 배: 2.0rad/s — 별이 도는 각
            Spin(0, 2.0);
            double w0 = st.Bodies[0].W, sky0 = st.Bodies[0].Th;
            ran += show.Advance(1.0);
            double skyTurn = st.Bodies[0].Th - sky0;
            ran += show.Advance(0.25);   // 틱 사이 4분의 1 (틱은 돌지 않는다)
            show.Render();
            double sky = show.Painter.SkyTh - sky0;
            Check("도는 조각 그림 각: 한 틱에 4.8rad (반 바퀴 넘게) 도는 조각은 틱 사이 장마다 도는 쪽으로만 · 고르게 돈다 (W · 2.4초 · 틱 사이 몫) · 배가 그만큼 돌면 별도 감지 않고 4분의 1만큼",
                ran == 2 && st.Bodies.Count == 2 && Math.Abs(w1 - 2.0) < 1e-9 && Math.Abs(turned) > Math.PI && forward && worst < 1e-6 &&
                Math.Abs(w0 - 2.0) < 1e-9 && skyTurn > Math.PI && Math.Abs(sky - w0 * Time.SecondsPerTick * 0.25) < 1e-6,
                $"조각 배에 대해 {w1:0.000}rad/s · 한 틱에 {turned:0.000}rad · 장마다 각 {string.Join(" → ", th.Select(t => (t - th[0]).ToString("0.000")))} (틱 사이 몫 0.25마다 {w1 * Time.SecondsPerTick * 0.25:0.000} · 가장 큰 어긋남 {worst:0.0e0}) · " +
                $"배 {w0:0.000}rad/s · 한 틱에 {skyTurn:0.000}rad · 4분의 1에 별 {sky:0.000}rad (기대 {w0 * Time.SecondsPerTick * 0.25:0.000})");

            // 갈라지지 않은 배: 구멍만 나면 부스러기와 함께 몸 0 하나가 선다 (몸 1) — 뿜어 도는 배의 별도 틱 사이 장마다 메운다 (몸이 둘 넘을 때만 메우면 별이 한 틱 서 있다가 틱마다 한 번에 뛴다)
            var lay1 = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s1 = Worlds.Full(lay1, 2);
            var st1 = s1.Get<Structure>();
            s1.Run(3);
            st1.Destroy(s1, Worlds.At(lay1, 18, 0), 0);   // 오른쪽 방 윗벽 끝 — 통로는 그대로
            var show1 = new ShipView.LiveShow(s1, "아령 구멍") { SpeedOverride = 1, Follow = false };
            int ran1 = show1.Advance(1.0);
            int one = st1.Bodies.Count;
            double vw = st1.Bodies[0].W;
            var vent = new List<double>();   // 뿜어 도는 배: 틱 사이 ¼ · ½ · ¾에 그린 별 각 − 지난 틱 끝 각
            double v0 = st1.Bodies[0].Th, vw0 = st1.Bodies[0].W;
            ran1 += show1.Advance(1.0);
            double ventTurn = st1.Bodies[0].Th - v0, vw1 = st1.Bodies[0].W;
            for (int k = 0; k < 3; k++) { ran1 += show1.Advance(0.25); show1.Render(); vent.Add(show1.Painter.SkyTh - v0); }
            double ventWorst = 0, dt1 = Time.SecondsPerTick;
            // 몸은 틱 안에서 고르게 가속한다 (PH4d — 뿜는 돌림힘이 그 틱 동안 고르다): 그린 각 = W₀t + ½αt² (α = 틱 끝 · 처음 각속도 차 / dt)
            for (int k = 0; k < 3; k++) { double t = dt1 * 0.25 * (k + 1); ventWorst = Math.Max(ventWorst, Math.Abs(vent[k] - (vw0 * t + 0.5 * (vw1 - vw0) / dt1 * t * t))); }
            ran1 += show1.Advance(0.25);   // 다음 틱이 돈다 (틱 사이 몫 1 → 0)
            // 배를 2.0rad/s로: 한 틱 뒤 ¼에 별이 W₀t + ½αt²만큼 (구멍으로 아직 뿜어 한 틱에 0.000005rad/s 는다 — 고른 가속)
            SpinTo(st1, 0, 2.0);
            double sw = st1.Bodies[0].W, s10 = st1.Bodies[0].Th;
            ran1 += show1.Advance(1.0);
            double spinTurn = st1.Bodies[0].Th - s10, sw1 = st1.Bodies[0].W;
            ran1 += show1.Advance(0.25);
            show1.Render();
            double sky1 = show1.Painter.SkyTh - s10;
            Check("도는 조각 그림 각 — 갈라지지 않은 배: 구멍만 난 배 (몸 0 하나)가 뿜어 돌거나 2rad/s로 돌면 별도 틱 사이 장마다 도는 쪽으로 고르게 (한 틱 서 있다가 틱마다 한 번에 뛰지 않는다)",
                ran1 == 4 && one == 1 && st1.Bodies.Count == 1 && Math.Abs(vw) > 0.01 && Math.Abs(ventTurn) > 0.01 && ventWorst < 1e-6 &&
                Math.Abs(sw - 2.0) < 1e-9 && Math.Abs(sky1 - SpinAt(sw, sw1, Time.SecondsPerTick * 0.25)) < 1e-6,
                $"몸 {one} → {st1.Bodies.Count} · 뿜어 {vw:0.000}rad/s · 한 틱에 {ventTurn:0.000}rad · ¼ · ½ · ¾에 별 {string.Join(" → ", vent.Select(v => v.ToString("0.000")))}rad (어긋남 {ventWorst:0.0e0}) · " +
                $"배 {sw:0.000} → {sw1:0.000}rad/s · 한 틱에 {spinTurn:0.000}rad · 4분의 1에 별 {sky1:0.000000}rad (기대 {SpinAt(sw, sw1, Time.SecondsPerTick * 0.25):0.000000})");

            // 돌던 조각이 또 갈라지면: 새 몸은 이번 틱에 났다 (지난 틱 자세가 없다) — 붙어 있던 자리 · 지금 각에 가장 가까운 온 바퀴에서 메운다 (각을 감지 않아도 한 장에 몇 바퀴를 돌지 않는다)
            var lay3 = Worlds.Ascii(Triple, Worlds.MindLegend);
            var s3 = Worlds.Full(lay3, 2);
            var st3 = s3.Get<Structure>();
            s3.Run(3);
            for (int y = 1; y <= 3; y++) st3.Destroy(s3, Worlds.At(lay3, 34, y), 0);
            s3.Run(3);
            var f3 = st3.Bodies[1];
            st3.Push(1, f3.X, f3.Y, f3.Mass, 0);   // 배에서 1m/s로 멀어지며 (돌다 배에 닿지 않게)
            SpinTo(st3, 1, st3.Bodies[0].W + 1.0);
            s3.Run(2);
            double was3 = st3.Bodies[1].Th - st3.Bodies[0].Th;
            var show3 = new ShipView.LiveShow(s3, "세 방") { SpeedOverride = 1, Follow = false };
            for (int y = 1; y <= 3; y++) st3.Destroy(s3, Worlds.At(lay3, 46, y), 0);
            int ran3 = show3.Advance(1.0);   // 이 틱에 조각의 통로가 끊겨 또 갈라진다
            double new0 = show3.DrawnTh(2, 0), new1 = show3.DrawnTh(2, 1);
            Check("도는 조각 그림 각 — 또 갈라진 몸: 배에 대해 반 바퀴 넘게 돈 조각에서 이번 틱에 갈라져 나온 몸은 지금 각에 가장 가까운 온 바퀴에서 메운다 (한 장에 몇 바퀴를 돌지 않는다)",
                ran3 == 1 && st3.Bodies.Count == 3 && Math.Abs(was3) > Math.PI && Math.Abs(new1 - new0) <= Math.PI + 1e-9,
                $"몸 {st3.Bodies.Count} · 갈라지기 전 조각 각 {was3:0.000}rad · 새 몸 틱 사이 {new0:0.000} → {new1:0.000}rad");
        }

        // ── (아) 부스러기 틱 사이: 틱 사이 장마다 그 때 자리 (지금 자리에서 진공을 곧게 뒤로 늘인다) · 제 돎만큼 판이 돈다 · 방의 빛을 받는다 · 이번 틱 충돌 잔걸음에 난 것은 나기 전 자리 (배 안 바닥)에 그리지 않는다 ──
        {
            static double Luma(Rgba c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            // 알갱이 하나만 둔 그림 − 하나도 없는 그림: 다른 도트 (세계는 그대로 돌려놓는다 — 그림은 읽기만)
            static List<(int x, int y, double l, bool core)> Dots(Painter p, Sim s, Structure st, int keep, int x0, int y0, int w, int h)
            {
                var all = st.Shards.ToList();
                st.Shards.Clear(); var bare = p.Frame(s, x0, y0, w, h, 0);
                if (keep >= 0) st.Shards.Add(all[keep]); else st.Shards.AddRange(all);
                var with = p.Frame(s, x0, y0, w, h, 0);
                st.Shards.Clear(); st.Shards.AddRange(all);
                var d = new List<(int, int, double, bool)>();
                for (int y = 0; y < with.H; y++)
                    for (int x = 0; x < with.W; x++)
                    {
                        int i = (y * with.W + x) * 4;
                        if (with.Px[i] != bare.Px[i] || with.Px[i + 1] != bare.Px[i + 1] || with.Px[i + 2] != bare.Px[i + 2]) { var c = with.Get(x, y); d.Add((x, y, Luma(c), c.R == 0x9a && c.G == 0xa2 && c.B == 0xb0)); }
                    }
                return d;
            }
            static (double x, double y) Mid(List<(int x, int y, double l, bool core)> d)   // 밝은 도트 (판 몸 — 우주에서는 빛을 받지 않는다)
            {
                var b = d.Where(q => q.core).ToList();
                return b.Count == 0 ? (double.NaN, double.NaN) : (b.Average(q => q.x), b.Average(q => q.y));
            }
            static string Shape(List<(int x, int y, double l, bool core)> d)
            {
                var b = d.Where(q => q.core).ToList();
                if (b.Count == 0) return "";
                int mx = b.Min(q => q.x), my = b.Min(q => q.y);
                return string.Join(";", b.Select(q => $"{q.x - mx},{q.y - my}").OrderBy(t => t, StringComparer.Ordinal));
            }
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = Worlds.Full(lay, 2);
            var st = s.Get<Structure>();
            s.Run(3);
            double sy = lay.H + 4;   // 배 아래 우주 (배는 서 있다)
            st.Throw(s, 2, sy, 20, 0, 5, 0);          // 20m/s — 한 틱에 48m
            st.Throw(s, 2, sy + 6, 20, 0, 225, 0);    // 5도트 판
            var spun = st.Shards[1]; spun.Spin = 225 * 0.25 * 0.25 / 6 * 1.0; st.Shards[1] = spun;   // 제 돎 1rad/s (보기 어림 — 25cm 판)
            var show = new ShipView.LiveShow(s, "아령") { SpeedOverride = 1, Follow = false };
            int ran = show.Advance(1.0);
            int wx0 = 0, wy0 = (int)sy - 2, ww = 64, wh = 10;
            ran += show.Advance(0.25); show.Render();
            var a0 = Dots(show.Painter, s, st, 0, wx0, wy0, ww, wh); var b0 = Dots(show.Painter, s, st, 1, wx0, wy0, ww, wh);
            ran += show.Advance(0.5); show.Render();
            var a1 = Dots(show.Painter, s, st, 0, wx0, wy0, ww, wh); var b1 = Dots(show.Painter, s, st, 1, wx0, wy0, ww, wh);
            var (ax0, ay0) = Mid(a0); var (ax1, ay1) = Mid(a1);
            double moved = ax1 - ax0, expect = 20 * Time.SecondsPerTick * 0.5 * Painter.T;
            string sh0 = Shape(b0), sh1 = Shape(b1), sa0 = Shape(a0), sa1 = Shape(a1);
            // 이번 틱 충돌 잔걸음에 난 것처럼: 왼쪽 방 아래 외판 바로 밖 · 아래로 2m/s · 난 때 = 이번 틱 → 틱 사이 0.1에 뒤로 늘인 자리는 방 바닥 (아직 나지 않았다)
            ran += show.Advance(0.35); show.Render();   // 다음 틱 · 틱 사이 0.1
            double f01 = 0.1;
            var crash = new Shard { X = 4.5, Y = lay.H - 0.2, Vx = 0, Vy = 2, Kg = 30, From = 0, Born = s.Tick - 1 };
            double backY = crash.Y - crash.Vy * Time.SecondsPerTick * (1 - f01);
            int backCell = lay.Index(4, (int)Math.Floor(backY));
            st.Shards.Add(crash);
            int ci = st.Shards.Count - 1;
            static int OnFloor(List<(int x, int y, double l, bool core)> d, Layout lay, Structure st, int x0, int y0) =>
                d.Count(q => { int cx = x0 + q.x / Painter.T, cy = y0 + q.y / Painter.T; if (!lay.In(cx, cy)) return false; int c = lay.Index(cx, cy); return lay.Kind[c] == CellKind.Floor && st.BodyOf(c) == 0; });
            var cNow = Dots(show.Painter, s, st, ci, -2, -2, lay.W + 4, lay.H + 4);
            var cEnd = Dots(new Painter(lay), s, st, ci, -2, -2, lay.W + 4, lay.H + 4);   // 틱 끝 (틱 사이 1) — 지금 자리에는 그린다
            int crashFloor = OnFloor(cNow, lay, st, -2, -2);
            st.Shards.RemoveAt(ci);
            // 방의 빛: 바닥에 멈춘 알갱이 — 켠 방 · 불 끈 방
            var lt = s.Get<Light>();
            st.Throw(s, 4.5, 4.5, 0, 0, 100, 0);
            int li = st.Shards.Count - 1;
            var pl = new Painter(lay);
            for (int r = 0; r < lay.Rooms.Count; r++) lt.Lamp[r] = 300;
            var lit = Dots(pl, s, st, li, 2, 2, 6, 5);
            for (int r = 0; r < lay.Rooms.Count; r++) lt.Lamp[r] = 0;
            var dim = Dots(pl, s, st, li, 2, 2, 6, 5);
            double litL = lit.Count > 0 ? lit.Max(q => q.l) : 0, dimL = dim.Count > 0 ? dim.Max(q => q.l) : 0;
            st.Shards.RemoveAt(li);
            // 아령 외판이 부서진 틱: 틱 사이 0.1 장에 부스러기가 배 안 바닥 칸 위에 없다 (외판 칸에서 바깥으로 — 틱 처음에 났다)
            var sD = Worlds.Full(lay, 2);
            var stD = sD.Get<Structure>();
            sD.Run(3);
            var showD = new ShipView.LiveShow(sD, "아령 외판") { SpeedOverride = 1, Follow = false };
            stD.Destroy(sD, Worlds.At(lay, 16, 0), 0);
            int ranD = showD.Advance(1.0);
            ranD += showD.Advance(0.1); showD.Render();
            var dD = Dots(showD.Painter, sD, stD, -1, -8, -8, lay.W + 16, lay.H + 16);
            int hullFloor = OnFloor(dD, lay, stD, -8, -8);
            Check("부스러기 틱 사이: 틱 사이 장마다 그 때 자리 (20m/s 알갱이가 틱 사이 ¼ → ¾에 24m) · 제 돎만큼 판이 돈다 (돌지 않으면 같은 꼴) · 불 끈 방에서는 어둡다 · 충돌 잔걸음에 난 것은 나기 전 자리 (배 안 바닥)에 그리지 않는다 · 외판이 부서진 틱에도 배 안 바닥 위에 없다",
                ran == 2 && a0.Count > 0 && a1.Count > 0 && Math.Abs(moved - expect) <= 2 && Math.Abs(ay1 - ay0) <= 1 && sa0 == sa1 && sh0 != "" && sh1 != "" && sh0 != sh1 &&
                lay.Kind[backCell] == CellKind.Floor && crashFloor == 0 && cEnd.Count > 0 && dimL < litL * 0.6 &&
                ranD == 1 && stD.Shards.Count > 0 && dD.Count > 0 && hullFloor == 0,
                $"틱 {ran} · ¼ → ¾ 밝은 도트 무게중심 {moved:0.0}도트 (기대 {expect:0}) · 세로 {ay1 - ay0:0.0} · 판 꼴 돎 {(sh0 != sh1 ? "바뀜" : "같음")} · 안 돎 {(sa0 == sa1 ? "같음" : "바뀜")} · " +
                $"충돌에 난 것 뒤로 늘인 자리 {lay.Kind[backCell]} · 그 장 바닥 위 도트 {crashFloor} · 틱 끝 도트 {cEnd.Count} · 밝기 켠 방 {litL:0} / 불 끈 방 {dimL:0} · " +
                $"외판 틱 {ranD} · 부스러기 {stD.Shards.Count} · 그린 도트 {dD.Count} · 배 안 바닥 위 {hullFloor}");
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
