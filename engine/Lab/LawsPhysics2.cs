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

    /// <summary>
    /// 법칙 — 짝 닿음의 팔은 제 껍질까지 (떨어져 다가오는 짝도): 제 돎은 제 겉에 받은 충격량으로만 — |I·Δω| ≤ r·m·|Δv| (r 꼴의 가장 먼 겉).
    /// 무중력 · 공기 없는 방에서 200m/s로 날아온 컵 · 접시 · 책이 비스듬히 식량 상자에 맞는다 (예전: 떨어진 짝의 팔을 두 물체 가운데 점까지 잡아 마찰 충격량이 수 m 팔로 돌렸다).
    /// </summary>
    private static void PairArmLaws()
    {
        if (!Do("짝 닿음 팔 법칙")) return;
        Console.WriteLine("\n[짝 닿음 팔 — 제 껍질까지]");
        double worst = 0; string at = "";
        int hits = 0;
        foreach (var (name, dy, vy) in new[] { ("컵", 0.10, 0.0), ("컵", 0.18, 40.0), ("접시", 0.2, -60.0), ("책", 0.15, 90.0), ("컵", -0.12, -120.0), ("접시", -0.25, 150.0), ("책", 0.0, -150.0), ("컵", 0.3, 180.0) })
        {
            var s = PropWorld(0);
            var st = s.Get<Structure>();
            st.Breakage = false;
            double y0 = 5 - vy * (3.0 / 200) / 2;   // 옆 빠르기가 있어도 3m 앞 상자 언저리에서 만나게
            int id = Slide(s, name, 3.0, y0, 200, vy);
            Slide(s, "식량 상자", 6.0, y0 + vy * (3.0 / 200) + dy, 0, 0);
            var p0 = st.Props[st.IndexOf(id)];
            var fm = st.PropForm(p0);
            double r = fm.Shape.Hit.reach, m = fm.Mass, I = fm.I;
            s.Step();
            var p1 = st.Props[st.IndexOf(id)];
            double dv = Math.Sqrt((p1.Vx - p0.Vx) * (p1.Vx - p0.Vx) + (p1.Vy - p0.Vy) * (p1.Vy - p0.Vy)), dl = I * Math.Abs(p1.W - p0.W);
            if (dv > 1) hits++;
            double ratio = dl / Math.Max(1e-9, r * m * dv);
            if (ratio > worst) { worst = ratio; at = $"{name} 비낌 {dy:0.00}m · 옆 {vy:0}m/s — I·Δω {dl:0.000} · r·m·Δv {r * m * dv:0.000} (Δω {p1.W - p0.W:0.0}rad/s · Δv {dv:0.0}m/s)"; }
        }
        Check("짝 닿음 팔: 무중력 · 공기 없는 방에서 200m/s (옆 빠르기 −150 ~ 180m/s)로 날아온 컵 · 접시 · 책이 3m 앞 식량 상자에 비스듬히 맞는다 — 제 돎의 바뀜이 제 겉에 받은 충격량의 몫을 넘지 않는다 (|I·Δω| ≤ r·m·|Δv|)",
            hits >= 5 && worst <= 1.0001,
            $"맞음 {hits} · 가장 큰 몫 {worst:0.000} ({at})");
    }

    /// <summary>
    /// 법칙 — 바람의 줄어드는 빠르기 (WindK)는 구역 전체의 몫: 이 틱에 차오른 구역은 0 · 빠진 구역은 처음/끝 몰의 로그 — ④가 그것으로 틱 처음 기압을 되짚는다 (구멍 입 · 빨림 · 나가는 빠르기).
    /// 끓는 행성 (92기압)에 내린 디딤호 두 틱: 이 틱에 기압이 오른 칸은 WindK가 0 (예전: 그 칸 돌풍이 가장 센 틈 하나의 몫을 써 101kPa에서 9.2MPa로 차오른 방을 비는 방으로 되짚어 22MPa — 구멍 곁 컵에 13MPa 빨림).
    /// </summary>
    private static void RegionDecayLaws()
    {
        if (!Do("구역 되짚기 법칙")) return;
        Console.WriteLine("\n[구역 되짚기 — 차오르는 방은 비는 방이 아니다]");
        var s = ShipWorld.Create("Didim", 3);
        var f = s.Get<Field>();
        s.Get<Outside>().Land("venuslike", s.Tick);
        int bad = 0, seen = 0; string at = "";
        var p0 = new double[f.Cells];
        for (int k = 0; k < 2; k++)
        {
            for (int c = 0; c < f.Cells; c++) p0[c] = f.Pressure(c);
            s.Step();
            for (int c = 0; c < f.Cells; c++)
            {
                if (f.RegionOf[c] < 0 || f.WindPa[c] <= 0) continue;
                seen++;
                if (f.WindK[c] > 0 && f.Pressure(c) > 1.01 * p0[c]) { if (bad++ == 0) at = $"틱 {k} 칸 {c} {p0[c]:0} → {f.Pressure(c):0}kPa · K {f.WindK[c]:0.00} (되짚음 {f.Pressure(c) * Math.Exp(f.WindK[c] * Time.SecondsPerTick):0}kPa)"; }
            }
        }
        Check("구역 되짚기: 끓는 행성 (92기압)에 내린 디딤호 두 틱 — 돌풍이 분 칸 가운데 이 틱에 기압이 오른 칸은 줄어드는 빠르기가 0 (되짚은 틱 처음 기압이 끝보다 높지 않다)",
            seen > 100 && bad == 0,
            $"돌풍 칸 {seen} · 어긋남 {bad}{(bad > 0 ? " (" + at + ")" : "")}");
    }

    /// <summary>
    /// 법칙 — 끓는 행성 되살림 (PH14~15 검토 — 옛 구멍): 큰 부서짐 뒤 찍고 되살려도 다음 틱이 바이트 같다.
    /// 예전: ① 장이 구역을 지은 뒤 ④가 끊은 칸을 되살린 세계는 새 구역으로 지었다 (끊긴 칸을 구역 찍음에 넣지 않았다) · ② 작은 덩어리가 떨어져 나가도 ⑥ 급전선 끊김을 다시 세지 않아 이어진 채 남았다 (되살린 세계는 끊김).
    /// </summary>
    private static void VenusSaveLaws()
    {
        if (!Do("끓는 행성 되살림 법칙")) return;
        Console.WriteLine("\n[끓는 행성 되살림 — 큰 부서짐 뒤 찍고 되살림]");
        var s = ShipWorld.Create("Didim", 3);
        s.Get<Outside>().Land("venuslike", s.Tick);
        s.Run(2);
        var r = new Sim(s.Setup); r.Load(s.Save());
        bool same = true; int at = -1;
        for (int k = 0; k < 2 && same; k++) { s.Step(); r.Step(); same = s.Save().AsSpan().SequenceEqual(r.Save()); if (!same) at = k; }
        var st = s.Get<Structure>();
        Check("끓는 행성 되살림: 92기압 행성에 내린 디딤호를 두 틱 뒤 (조각 · 떨어져 나간 칸이 생긴 뒤) 찍고 되살려 두 틱 — 바이트 같다 (구역을 지은 때의 끊긴 칸 · 급전선 끊김이 같다)",
            same && st.Props.Count > 100, same ? $"같음 · 물체 {st.Props.Count}" : $"{at}틱 뒤 다름");
    }

    /// <summary>법칙 — 판 조각 600 상한 (PH15): 넘으면 잠든 것부터 가루로 (그 몸이 운동량을 받는다) · 그래도 넘으면 가장 오래된 나는 것을 장부 '우주로' — 질량 · 운동량 장부가 그대로.</summary>
    private static void PlateLaws()
    {
        if (!Do("판 조각 법칙")) return;
        Console.WriteLine("\n[판 조각 — 600 상한 · 장부 (PH15)]");
        var s = PropWorld(1);
        var st = s.Get<Structure>();
        st.EnsureBody();
        s.Step();
        for (int k = 0; k < 590; k++) st.ThrowPlate(s, -40 - k % 25 * 2.0, -40 - k / 25 * 2.0, 0, 0, 20, 0);   // 배 밖 — 멈춰 떠 있다
        var inside = new List<int>();
        for (int k = 0; k < 10; k++) inside.Add(st.ThrowPlate(s, 3.5 + k % 5 * 1.5, 3.5 + k / 5 * 3, 1, 0, 25, 0));   // 방 안 — 바닥에 실려 미끄러지다 선다
        s.Run(3);   // 600 — 아직 넘지 않는다 · 방 안 것은 섰다
        bool settled = inside.All(id => st.IndexOf(id) >= 0 && st.Props[st.IndexOf(id)].State == 0);
        for (int k = 0; k < 5; k++) st.ThrowPlate(s, -40, 60 + k * 2.0, 0, 0, 20, 0);   // 605 — 잠든 것 다섯이 가루로
        var b0 = WorldBook(st);
        s.Step();
        var b1 = WorldBook(st);
        int n1 = st.Props.Count(p => p.Kind == 4), dust1 = inside.Count(id => st.IndexOf(id) < 0);
        double grit1 = st.GritKg;
        for (int k = 0; k < 10; k++) st.ThrowPlate(s, -40, 80 + k * 2.0, 0, 0, 20, 0);   // 610 — 남은 잠든 것 다섯이 가루로 · 그래도 넘는 다섯은 가장 오래된 나는 것이 우주로
        var b2 = WorldBook(st);
        s.Step();
        var b3 = WorldBook(st);
        int n2 = st.Props.Count(p => p.Kind == 4), dust2 = inside.Count(id => st.IndexOf(id) < 0);
        double kgSum = st.PlateKgNow + st.PlateKgLost + st.PlateKgDust, size = 600 * 25;
        bool book = Math.Abs(b1.px - b0.px) < 1e-9 * size && Math.Abs(b1.py - b0.py) < 1e-9 * size && Math.Abs(b1.l - b0.l) < 1e-9 * size * 100
            && Math.Abs(b3.px - b2.px) < 1e-9 * size && Math.Abs(b3.py - b2.py) < 1e-9 * size && Math.Abs(b3.l - b2.l) < 1e-9 * size * 100;
        Check("판 조각 600 상한: 605개 (방 안에 선 것 10)면 잠든 것 가운데 오래된 다섯이 가루로 (그 칸 바닥) · 610개면 남은 잠든 다섯이 가루로 · 그래도 넘는 다섯은 가장 오래된 나는 것이 장부 '우주로' — 질량 (만든 = 있는 + 우주로 + 가루) · 운동량 장부가 그대로 (1e-9)",
            settled && n1 == 600 && dust1 == 5 && Math.Abs(grit1 - 125) < 1e-6 && n2 == 600 && dust2 == 10 && Math.Abs(st.PlateKgLost - 100) < 1e-6 && Math.Abs(kgSum - st.PlateKgMade) < 1e-9 * st.PlateKgMade && book,
            $"섰다 {settled} · 605 → {n1} (가루로 {dust1} · {grit1:0}kg) · 610 → {n2} (가루로 {dust2} · 우주로 {st.PlateKgLost:0}kg) · 질량 {kgSum:0.000} / 만든 {st.PlateKgMade:0.000} · 장부 어긋남 ({b1.px - b0.px:0.0e0}, {b1.l - b0.l:0.0e0} / {b3.px - b2.px:0.0e0}, {b3.l - b2.l:0.0e0})");
    }
}
