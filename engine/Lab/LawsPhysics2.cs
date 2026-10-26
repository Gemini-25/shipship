using ShipEngine;

namespace Lab;

/// <summary>법칙 시험 — 물리 세계 뒤 커밋 (docs/PHYSICS.md §10 · §11 PH11~PH16).</summary>
public static partial class Program
{
    /// <summary>왼쪽 외판에 구멍을 낼 작은 방 (구멍 곁에 물건).</summary>
    internal const string SpaceRoom = @"
#########
#.......#
#.......#
#.......#
#########";

    private static void Physics2Laws()
    {
        if (!Do("우주의 물체")) return;
        Console.WriteLine("\n[우주의 물체 — 세상 틀로 난다 (PH11)]");
        var t = Tables.Default;
        int kit = t.People.Items.Keys.ToList().IndexOf("수리 꾸러미");
        // 법칙 16 · 법칙 6 '점화 중 나감': 0.3g 점화 중 외판 구멍 밖으로 나간 꾸러미는 세상 틀로 곧게 난다 (자리 = x₀ + v t · 빠르기 그대로 1e-9) ·
        //   배의 몸은 그동안 0.3g로 빨라진다 · 몸 + 물체 + 기체 − 점화 장부가 맞는다
        {
            var lay = Worlds.Ascii(FlingRooms, Worlds.Seats);
            var s = FieldStructure(lay);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            st.Debris = false;
            st.PutItem(kit, 1, Worlds.At(lay, 1, 2));
            st.RequestDoor(lay.DoorAt[Worlds.At(lay, 6, 2)], 1);   // 열어 둔 문 (통로 공기까지 — 바람이 몇 틱 분다)
            s.Run(10);
            const double a = 0.3 * Structure.Gravity;
            var book0 = WorldBook(st);
            f.SetHole(Worlds.At(lay, 0, 2), 1.5f);
            int fly = -1; long t0 = 0; double x0 = 0, y0 = 0, vx0 = 0, vy0 = 0, bv0 = 0;
            for (int k = 0; k < 200 && fly < 0; k++)
            {
                st.ShipAx = a; s.Step();
                int i = st.Props.FindIndex(p => Structure.Flying(p));
                if (i >= 0) { var p = st.Props[i]; fly = p.Id; t0 = s.Tick; x0 = p.X; y0 = p.Y; vx0 = p.Vx; vy0 = p.Vy; bv0 = st.Bodies[0].Vx; }
            }
            const int n = 2;   // 배가 점화로 멀어진다 — 300m 안에서 (두 틱 · 4.8초 — 나가는 기체가 꾸러미를 30m/s 넘게 밀어 낸다 · PH14)
            for (int k = 0; k < n; k++) { st.ShipAx = a; s.Step(); }
            int j = fly > 0 ? st.IndexOf(fly) : -1;
            var q = j >= 0 ? st.Props[j] : default;
            double dt = n * Time.SecondsPerTick, ex = x0 + vx0 * dt, ey = y0 + vy0 * dt;
            double bdv = st.Bodies.Count > 0 ? st.Bodies[0].Vx - bv0 : 0;
            var book1 = WorldBook(st);
            double gas = Math.Abs(st.GasPx) + Math.Abs(st.BurnPx) + 1;
            // 검토 고침: 이미 나는 것이 있어도 이번 틱에 나간 것은 그 틱에 날지 않는다 (나간 자리 그대로 세상 틀로 옮길 뿐) · 쓸어 담은 조각이 가져간 운동량도 장부에
            int cup = t.People.Items.Keys.ToList().IndexOf("컵");
            int late = st.PutItem(cup, 0, Worlds.At(lay, 3, 1));
            int li = st.IndexOf(late); var lp = st.Props[li];
            double lx0 = lp.X, ly0 = lp.Y;
            lp.State = 5; lp.Vx = 3; lp.Kg = 0.3f; st.Props[li] = lp;   // 시험 차림: 이번 틱에 빨려 나간 것처럼
            st.ShipAx = 0; s.Step();
            var lq = st.Props[st.IndexOf(late)];
            var (llx, lly) = st.LocalOf(0, lq.X, lq.Y);
            bool sameTick = Structure.Flying(lq) && Math.Abs(llx - lx0) < 1e-6 && Math.Abs(lly - ly0) < 1e-6;
            int shard = st.PutItem(cup, 0, Worlds.At(lay, 10, 2));
            { int si = st.IndexOf(shard); var sp = st.Props[si]; sp.Kind = 2; st.Props[si] = sp; }   // 깨진 조각처럼
            var bk0 = WorldBook(st);
            st.Sweep(new[] { Worlds.At(lay, 10, 2) });
            var bk1 = WorldBook(st);
            Check("검토 고침 (PH11 · PH13): 이미 나는 것이 있어도 이번 틱에 나간 컵은 그 틱에 날지 않는다 (나간 자리 그대로 — 배 틀 1e-6) · 움직이는 배에서 쓸어 담은 조각의 운동량이 장부에 남는다 (1e-9)",
                sameTick && Math.Abs(bk1.px - bk0.px) < 1e-9 * Math.Max(1, Math.Abs(bk0.px)) && Math.Abs(bk1.py - bk0.py) < 1e-9 * Math.Max(1, Math.Abs(bk0.py)),
                $"나간 컵 배 틀 ({lx0:0.000},{ly0:0.000}) → ({llx:0.000},{lly:0.000}) · 쓸어 담기 장부 어긋남 ({bk1.px - bk0.px:0.0e0}, {bk1.py - bk0.py:0.0e0})");
            Check("세상 빠르기 (법칙 16 · 6): 0.3g 점화 중 외판 구멍 밖으로 나간 수리 꾸러미는 세상 틀로 곧게 난다 — 두 틱 뒤 자리 x₀ + v t · 빠르기 그대로 (1e-9) · 배의 몸은 a·t (+ 뿜는 공기)만큼 빨라진다 · 몸 + 물체 + 기체 − 점화 장부가 그대로",
                fly > 0 && j >= 0 && Structure.Flying(q) && Math.Abs(q.X - ex) < 1e-9 * Math.Max(1, Math.Abs(ex)) && Math.Abs(q.Y - ey) < 1e-9 * Math.Max(1, Math.Abs(ey)) && q.Vx == vx0 && q.Vy == vy0
                && bdv > a * dt * 0.999 && Math.Abs(book1.px - book0.px) < 1e-6 * gas && Math.Abs(book1.py - book0.py) < 1e-6 * gas,
                $"나감 틱 {t0} · 꾸러미 ({q.X:0.000000}, {q.Y:0.000}) 바람 ({ex:0.000000}, {ey:0.000}) · 빠르기 ({q.Vx:0.000}, {q.Vy:0.000}) · 몸 Δv {bdv:0.0000} (a·t {a * dt:0.0000}) · 장부 어긋남 ({book1.px - book0.px:0.0e0}, {book1.py - book0.py:0.0e0}) / 크기 {gas:0.0e0}");
        }
    }

    /// <summary>법칙 — 몸 물체 (PH14): 끌려간 몸도 장부에 든다 (바람이 준 빠르기는 기체가 잃고 · 벽이 받은 것은 배의 몸이 되받고 · 다시 선 몸은 지닌 운동량을 가지고 나간다).</summary>
    private static void BodyPropLaws()
    {
        if (!Do("몸 물체 법칙")) return;
        Console.WriteLine("\n[몸 물체 — 장부 (PH14)]");
        var lay = Worlds.Ascii(SpaceRoom);
        var s = FieldStructure(lay);
        var st = s.Get<Structure>();
        st.EnsureBody();
        s.Run(2);
        var b = st.Bodies[0]; b.Vx = 0.4; b.W = 0.002; st.Bodies[0] = b;   // 배가 움직이며 돈다 (장부가 틀마다 맞는지)
        var book0 = WorldBook(st);
        st.Ragdoll(s, 0, Worlds.At(lay, 4, 2), false, false, s.Causes.Add(CauseKind.Setup, "시험 — 센 바람에 발이 떨어졌다", s.Tick), 8, 0);
        int moved = 0;
        for (int k = 0; k < 6; k++) { s.Step(); int i = st.RagdollOf(0); if (i >= 0 && st.Props[i].State == 0) { moved = k + 1; break; } }
        double hit = st.PropHits.Where(h => h.Tag == 0).Sum(h => (double)h.E);
        st.EndRagdoll(0);
        var book1 = WorldBook(st);
        double size = 70 * 8 + Math.Abs(st.GasPx) + 1;
        Check("몸 물체 장부: 움직이며 도는 배에서 8m/s로 끌려간 70kg 몸이 벽에 부딪혀 서고 다시 제 힘으로 — 몸 + 물체 + 기체 장부가 그대로 (1e-9) · 벽에 부딪힌 에너지가 몸의 맞음 장부에",
            moved > 0 && hit > Structure.BodyHitJ && Math.Abs(book1.px - book0.px) < 1e-9 * size && Math.Abs(book1.py - book0.py) < 1e-9 * size && Math.Abs(book1.l - book0.l) < 1e-9 * size * 10,
            $"선 틱 {moved} · 맞음 {hit:0}J · 장부 어긋남 ({book1.px - book0.px:0.0e0}, {book1.py - book0.py:0.0e0}, {book1.l - book0.l:0.0e0}) / 크기 {size:0}");
    }

    /// <summary>법칙 — 바람 끌림은 공기 빠르기를 넘지 못한다 (가볍고 넓은 것 · 긴 잔걸음): 끌림은 상대 빠르기를 줄이기만 한다 (닫힌 꼴 — 옮김 · 돎).</summary>
    private static void WindCapLaws()
    {
        if (!Do("바람 끌림 법칙")) return;
        Console.WriteLine("\n[바람 끌림 — 공기 빠르기를 넘지 않는다]");
        (double v, double air) Run(int detail)
        {
            var lay = Worlds.Ascii(SpaceRoom, Worlds.Seats);
            var s = Worlds.Full(lay, 1);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            st.Debris = false; st.Breakage = false; st.PropDetail = detail;
            foreach (var (name, x, y) in new[] { ("접시", 1, 1), ("접시", 1, 3), ("컵", 1, 2), ("책", 2, 1), ("접시", 2, 3), ("컵", 3, 2) }) Worlds.PutItem(s, name, Worlds.At(lay, x, y));
            for (int y = 1; y <= 3; y++) for (int x = 1; x <= 7; x++) f.AddGas(Worlds.At(lay, x, y), f.T.gCO2, 200, 293.15);   // 이산화탄소를 더해 세 기압 언저리 (벽은 버틴다 — 짙은 기체: 밀도 5kg/m³)
            s.Run(1);
            f.SetHole(Worlds.At(lay, 0, 2), 1.0f);
            double vMax = 0, air = 0;
            for (int k = 0; k < 3; k++)
            {
                s.Step();
                for (int c = 0; c < f.Cells; c++) if (f.WindPa[c] > 0 && f.WindRho[c] > 0) air = Math.Max(air, Math.Sqrt(2 * f.WindPa[c] / f.WindRho[c]));
                var o = st.Bodies.Count > 0 ? st.Bodies[0] : default;
                foreach (var p in st.Props)
                {
                    double vx = p.Vx, vy = p.Vy;
                    if (Structure.Flying(p)) { vx -= o.Vx; vy -= o.Vy; }
                    vMax = double.IsNaN(vx + vy) ? double.PositiveInfinity : Math.Max(vMax, Math.Sqrt(vx * vx + vy * vy));
                }
            }
            return (vMax, air);
        }
        var full = Run(1); var light = Run(0);
        const double sound = 343;
        Check("바람 끌림: 세 기압 이산화탄소 방 외판 1m² 구멍 곁 접시 · 컵 · 책 (가볍고 넓다) — 빨려 나가는 바람에 밀려도 (배에 대해) 소리 빠르기를 넘지 않는다 · 온전한 셈도 · 가벼운 셈 (주컴퓨터 상상 — 한 틱 네 잔걸음)도 (예전: 끌림을 앞으로 셈해 λh > 2면 상대 빠르기가 부호를 바꾸며 불었다)",
            full.v > 1 && full.v <= sound && light.v > 1 && light.v <= sound,
            $"가장 빠른 물체 — 온전 {full.v:0.0}m/s · 가벼운 셈 {light.v:0.0}m/s · 가장 센 돌풍 {Math.Max(full.air, light.air):0}m/s · 소리 빠르기 {sound:0}m/s");
    }
}
