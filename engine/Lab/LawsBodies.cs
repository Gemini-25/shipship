using ShipEngine;

namespace Lab;

/// <summary>④ 칸 강체 법칙: 힘이 없으면 그대로 · 몸들의 운동량 + 우주로 간 기체의 운동량 = 0 (각운동량도) · 대칭이면 돌지 않는다.</summary>
public static partial class Program
{
    // 방 둘 사이에 벽 세 겹 — 가운데 겹을 끊으면 두 방은 닫힌 채 갈라진다 (뿜는 것이 없다)
    private const string Sealed = @"
#############
#....###....#
#....###....#
#....###....#
#############";

    private static void BodyLaws()
    {
        if (!Do("몸")) return;
        Console.WriteLine("\n[④ 칸 강체 — 몸의 운동량 · 각운동량 · 대칭]");

        // ── 힘이 없으면 그대로: 닫힌 두 방을 갈라 각각 비스듬히 밀고 500틱 — 빠르기 · 각속도 · 운동 에너지가 그대로 · 자리는 곧게 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            bool split = st.Bodies.Count == 2;
            if (split)
            {
                st.Push(1, st.Bodies[1].X + 1.3, st.Bodies[1].Y - 0.7, 4000, 2500);
                st.Push(0, st.Bodies[0].X - 0.4, st.Bodies[0].Y + 1.1, -1500, 900);
            }
            var a0 = split ? st.Bodies[1] : default; var e0 = st.BodyMomentum();
            long t0 = s.Tick;
            s.Run(500);
            var a1 = split ? st.Bodies[1] : default; var e1 = st.BodyMomentum();
            double t = (s.Tick - t0) * Time.SecondsPerTick;
            double dx = a1.X - (a0.X + a0.Vx * t), dth = a1.Th - (a0.Th + a0.W * t);
            Check("힘이 없으면 그대로: 닫힌 두 방을 갈라 비스듬히 밀면 500틱 뒤에도 빠르기 · 각속도 · 운동 에너지가 같고 · 자리 · 자세는 곧게 (뿜는 것이 없다)",
                split && a1.Vx == a0.Vx && a1.Vy == a0.Vy && a1.W == a0.W && Rel(e0.ke, e1.ke) < 1e-12 && Math.Abs(dx) < 1e-6 && Math.Abs(dth) < 1e-9 && st.GasPx == 0,
                $"몸 {st.Bodies.Count} · 빠르기 {a0.Vx:0.0000},{a0.Vy:0.0000} → {a1.Vx:0.0000},{a1.Vy:0.0000} · 각속도 {a0.W:0.000000} → {a1.W:0.000000} · 에너지 {e0.ke:0.0} → {e1.ke:0.0}J · 자리 어긋남 {dx:0.0e0}m");
        }

        // ── 운동량 장부: 갈라진 두 방이 뿜으며 멀어진다 — 몸들의 운동량 + 우주로 간 기체가 가져간 운동량 = 0 · 각운동량도 ──
        {
            var lay = Worlds.Ascii(Twins);
            var s = FieldStructure(lay, 3);
            var st = s.Get<Structure>();
            s.Run(5);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 5, y), 0);
            // 오른쪽 방 윗벽 끝에도 구멍 — 비대칭으로 뿜어 돈다
            st.Destroy(s, Worlds.At(lay, 9, 0), 0);
            s.Run(200);
            var (px, py, l, _) = st.BodyMomentum();
            double gas = Math.Sqrt(st.GasPx * st.GasPx + st.GasPy * st.GasPy);
            double ex = px + st.GasPx, ey = py + st.GasPy, el = l + st.GasL;
            Check("운동량 장부: 갈라져 뿜는 두 방 — 몸들의 운동량 + 우주로 간 기체가 가져간 운동량 = 0 · 원점에 대한 각운동량도 (기체 운동량의 1e-9 안)",
                st.Bodies.Count == 2 && gas > 1000 && Math.Sqrt(ex * ex + ey * ey) < 1e-9 * gas && Math.Abs(el) < 1e-9 * gas * 10 && Math.Abs(st.Bodies[1].W) > 0.01,
                $"기체 {gas / 1000:0.0}kN·s · 남는 운동량 {Math.Sqrt(ex * ex + ey * ey):0.0e0} · 남는 각운동량 {el:0.0e0} · 오른쪽 방 각속도 {st.Bodies[1].W:0.000}rad/s");
        }

        // ── 부딪힘: 닫힌 두 방을 갈라 서로 마주 밀면 부딪혀 튕긴다 — 앞뒤 운동량 · 각운동량이 같고 · 운동 에너지는 늘지 않는다 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            st.Push(0, st.Bodies[0].X, st.Bodies[0].Y + 0.8, st.Bodies[0].Mass * 0.4, 0);
            st.Push(1, st.Bodies[1].X, st.Bodies[1].Y - 0.3, -st.Bodies[1].Mass * 0.3, 0);
            var e0 = st.BodyMomentum();
            s.Run(15);
            var e1 = st.BodyMomentum();
            double scale = st.Bodies[0].Mass * 0.4;
            Check("부딪힘: 닫힌 두 방을 마주 밀면 부딪혀 튕긴다 — 앞뒤 운동량 · 원점에 대한 각운동량이 같고 (1e-9) · 운동 에너지는 줄었다 · 다가오던 빠르기(0.7m/s)가 작게 뒤집혀 멀어진다 (반발 0.1 — 찌그러짐)",
                st.Crashes >= 1 && Math.Abs(e1.px - e0.px) < 1e-9 * scale && Math.Abs(e1.py - e0.py) < 1e-9 * scale && Math.Abs(e1.l - e0.l) < 1e-9 * scale * 10 && e1.ke < e0.ke && st.Bodies[1].Vx - st.Bodies[0].Vx > 0 && st.Bodies[1].Vx - st.Bodies[0].Vx < 0.3 * 0.7 && st.GasAbs == 0,
                $"부딪힘 {st.Crashes} · 운동량 {e0.px:0.000} → {e1.px:0.000} · 각운동량 차 {e1.l - e0.l:0.0e0} · 에너지 {e0.ke / 1000:0.00} → {e1.ke / 1000:0.00}kJ · 멀어지는 빠르기 {st.Bodies[1].Vx - st.Bodies[0].Vx:0.000}m/s");
        }

        // ── 대칭: 닫힌 두 방을 갈라 서로 마주 보는 벽 가운데를 똑같이 뚫으면 같은 빠르기로 반대로 뿜으며 멀어지고 · 돌지 않는다 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 3);
            var st = s.Get<Structure>();
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            st.Destroy(s, Worlds.At(lay, 5, 2), 0); st.Destroy(s, Worlds.At(lay, 7, 2), 0);
            s.Run(100);
            var a = st.Bodies[0]; var b = st.Bodies.Count > 1 ? st.Bodies[1] : default;
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    배 L {a.Lx:R},{a.Ly:R} m {a.Mass:R} · 조각 L {b.Lx:R},{b.Ly:R} m {b.Mass:R} · 기체 {st.GasPx:R},{st.GasPy:R} · 크기 {st.GasAbs:R}");
            Check("대칭: 닫힌 두 방을 갈라 마주 보는 벽 가운데를 똑같이 뚫으면 서로 같은 빠르기로 반대로 뿜으며 멀어지고 · 둘 다 돌지 않는다",
                st.Bodies.Count == 2 && Rel(a.Vx, -b.Vx) < 1e-9 && Math.Abs(a.Vx) > 0.2 && Math.Abs(a.W) < 1e-12 && Math.Abs(b.W) < 1e-12 && Math.Abs(a.Vy) < 1e-12 && Math.Abs(b.Vy) < 1e-12,
                $"배 {a.Vx:0.0000},{a.Vy:0.0e0} ω {a.W:0.0e0} · 조각 {b.Vx:0.0000},{b.Vy:0.0e0} ω {b.W:0.0e0} · 뿜은 운동량 {st.GasAbs / 1000:0.0}kN·s");
        }
    }
}
