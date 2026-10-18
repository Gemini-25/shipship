using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>④ 칸 강체 규칙: 떨어져 나간 조각이 뿜으며 돌아 멀어지고 안의 것이 함께 간다 · 점화하면 조각이 뒤로 남는다 · 화면에 돌아간 조각이 그 자리에.</summary>
public static partial class Program
{
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
            for (int k = 0; k < ticks; k++) { st.ShipAx = a; st.ShipAy = 0; s.Step(); }
            st.ShipAx = 0;
            double t = ticks * Time.SecondsPerTick;
            var s1 = st.Bodies[0]; var f1 = st.Bodies.Count > 1 ? st.Bodies[1] : default;
            double shipGone = s1.X - s0.X - s0.Vx * t, expect = 0.5 * a * t * t;
            var (px, py, _, _) = st.BodyMomentum();
            Check("점화: 배가 0.05g로 24초 가속하면 배만 ½at²만큼 나아가고 떨어진 조각은 제 빠르기 그대로 뒤에 남는다 · 몸들의 운동량 = 점화가 넣은 운동량",
                st.Bodies.Count == 2 && Rel(shipGone, expect) < 1e-6 && f1.Vx == f0.Vx && Rel(px, st.BurnPx) < 1e-9 && Rel(s1.Vx - s0.Vx, a * t) < 1e-9,
                $"배가 더 간 거리 {shipGone:0.0}m (½at² {expect:0.0}m) · 조각 빠르기 {f0.Vx:0.000} → {f1.Vx:0.000} · 떨어진 거리 {d0:0.0} → {(st.Fragments.Count > 0 ? st.Fragments[0].Dist : 0):0.0}m · 운동량 {px:0} / 점화 {st.BurnPx:0}N·s");
        }
    }

}
