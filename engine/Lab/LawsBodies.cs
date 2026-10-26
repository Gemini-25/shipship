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
            st.Debris = false;   // 몸만의 역학 (부스러기 없이)
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
            if (Environment.GetEnvironmentVariable("LEDGER_DEBUG") == "1") Console.WriteLine("    ==== 장부 시작");
            for (int k = 0; k < 200; k++)
            {
                s.Step();
                if (Environment.GetEnvironmentVariable("LEDGER_DEBUG") == "1" && k < 30)
                {
                    var (ax, ay, al, _) = st.BodyMomentum(); var (bx, by, bl) = st.ShardMomentum();
                    Console.WriteLine($"    틱 {k}: 몸 {st.Bodies.Count} · 남는 {ax + bx + st.GasPx:0.000},{ay + by + st.GasPy:0.000} · 각 {al + bl + st.GasL:0.000} · 판 조각 {st.PlatesMade} 세게 맞음 {st.PlateHits} · 우주로 {st.PlateKgLost:0}kg");
                }
            }
            var (px, py, l, _) = st.BodyMomentum();
            var (sx, sy, sl) = st.ShardMomentum();
            var (qx, qy, ql) = st.PropMomentum();   // 판 조각 (PH15 — 나는 것 · 실린 것 · 우주로 간 것)
            // 기체 장부에는 뿜은 공기의 운동량에서 빠지는 공기가 부스러기에 준 것이 빠져 있다 (그만큼 부스러기가 가져간다) — 잣대는 뿜은 크기의 합
            double gas = st.GasAbs;
            double ex = px + sx + qx + st.GasPx, ey = py + sy + qy + st.GasPy, el = l + sl + ql + st.GasL;
            Check("운동량 장부: 갈라져 뿜는 두 방 — 몸들의 운동량 + 판 조각(나는 것 · 실린 것 · 우주로 간 것) + 우주로 간 기체가 가져간 운동량 = 0 · 원점에 대한 각운동량도 (뿜은 운동량의 1e-9 안)",
                st.Bodies.Count == 2 && gas > 1000 && st.ShardsMade > 0 && Math.Sqrt(ex * ex + ey * ey) < 1e-9 * gas && Math.Abs(el) < 1e-9 * gas * 10 && Math.Abs(st.Bodies[1].W) > 0.01,
                $"뿜은 {gas / 1000:0.0}kN·s (남은 기체 장부 {Math.Sqrt(st.GasPx * st.GasPx + st.GasPy * st.GasPy) / 1000:0.0}) · 판 조각 {st.PlatesMade} (세게 맞음 {st.PlateHits}) · 남는 운동량 {Math.Sqrt(ex * ex + ey * ey):0.0e0} · 남는 각운동량 {el:0.0e0} · 오른쪽 방 각속도 {st.Bodies[1].W:0.000}rad/s");
        }

        // ── 부딪힘: 닫힌 두 방을 갈라 서로 마주 밀면 부딪혀 튕긴다 — 앞뒤 운동량 · 각운동량이 같고 · 운동 에너지는 늘지 않는다 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 5);
            var st = s.Get<Structure>();
            st.Debris = false;   // 몸만의 역학 (부스러기 없이)
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

        // ── 잔칸 질량: 파편 · 갈라짐 · 세게 부딪힘으로 판이 부서져도 남은 잔칸 + 날고 있는 부스러기 + 붙은 것 + 우주로 간 것 = 처음 판 · 골조 질량 ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 7);
            var st = s.Get<Structure>();
            s.Run(3);
            st.Impact(s, Worlds.At(lay, 2, 0), 1.2e7, 0, 1, 0);   // 충격파가 나지 않을 만큼 (외판 구멍 하나)
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            if (st.Bodies.Count > 1)
            {
                // 배 무게중심 쪽으로 (갈라진 뒤 뿜는 공기에 조각이 비켜 있을 수 있다)
                double dx = st.Bodies[0].X - st.Bodies[1].X, dy = st.Bodies[0].Y - st.Bodies[1].Y, dl = Math.Sqrt(dx * dx + dy * dy);
                st.Push(1, st.Bodies[1].X, st.Bodies[1].Y, st.Bodies[1].Mass * 60 * dx / dl, st.Bodies[1].Mass * 60 * dy / dl, 0);
            }
            s.Run(60);
            double total = st.StructRemain() + st.ShardKgFlying + st.ShardKgStuck + st.ShardKgLost + st.PlateKgNow + st.PlateKgLost + st.PlateKgDust;
            Check("잔칸 질량: 파편 · 갈라짐 · 세게(60m/s) 부딪힘으로 판이 부서져도 남은 잔칸 + 판 조각 (나는 것 · 실린 것) + 우주로 간 것 + 가루로 합친 것 = 처음 판 · 골조 질량",
                st.ShardsMade > 10 && st.PlatesMade > 10 && st.Crashes > 0 && Rel(total, st.StructInitial) < 1e-9,
                $"처음 {st.StructInitial / 1000:0.000}t · 남은 잔칸 {st.StructRemain() / 1000:0.000}t · 판 조각 {st.PlateKgNow:0}kg · 우주로 {st.PlateKgLost + st.ShardKgLost:0}kg · 가루로 {st.PlateKgDust:0}kg · 판 조각 {st.PlatesMade} · 부딪힘 {st.Crashes}");
        }

        // ── 대칭: 위아래 벽이 같은 자리에서 똑같이 뚫린 방은 공기를 뿜어도 제자리 · 돌지 않는다 (무게중심이 어디든 — 힘과 돌림힘이 서로 지운다) ──
        {
            var lay = Worlds.Ascii(Sealed);
            var s = FieldStructure(lay, 3);
            var st = s.Get<Structure>();
            st.Debris = false;   // 몸만의 역학 (부스러기 없이)
            s.Run(3);
            for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 6, y), 0);
            s.Run(3);
            var b0 = st.Bodies.Count > 1 ? st.Bodies[1] : default;
            st.Destroy(s, Worlds.At(lay, 9, 0), 0); st.Destroy(s, Worlds.At(lay, 9, 4), 0);
            s.Run(100);
            var b1 = st.Bodies.Count > 1 ? st.Bodies[1] : default;
            Check("대칭: 위아래 벽이 같은 자리에서 똑같이 뚫린 방은 공기를 뿜어도 빠르기 · 각속도가 그대로다 (무게중심이 어디든 — 위아래 힘과 돌림힘이 서로 지운다)",
                st.Bodies.Count == 2 && st.GasAbs > 1000 && b1.Vx == b0.Vx && b1.Vy == b0.Vy && b1.W == b0.W,
                $"뿜은 운동량 {st.GasAbs / 1000:0.0}kN·s · 빠르기 {b0.Vx:0.0000},{b0.Vy:0.0000} → {b1.Vx:0.0000},{b1.Vy:0.0000} · 각속도 {b0.W:0.0e0} → {b1.W:0.0e0}");
        }
    }
}
