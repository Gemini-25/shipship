namespace ShipEngine;

/// <summary>몸 하나 (0: 배 · 1~: 떨어져 나간 조각) — 칸들은 도면 좌표 그대로 '제 몸의 틀'이고 세상 자리는 이 자세로 옮기고 돌린 것.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct RigidBody
{
    public int Id;
    /// <summary>무게중심의 세상 자리 (m — 첫 갈라짐 때 배가 서 있던 관성 틀) · 자세 (rad).</summary>
    public double X, Y, Th;
    public double Vx, Vy, W;
    public double Mass, Inertia;
    /// <summary>무게중심의 제 틀 자리 (도면 좌표 m — 칸 (x, y)의 가운데는 (x + 0.5, y + 0.5)).</summary>
    public double Lx, Ly;
    /// <summary>제 틀의 칸 상자 (도면 칸) · 무게중심에서 가장 먼 칸 모서리까지 (m).</summary>
    public int MinX, MinY, MaxX, MaxY;
    public double Reach;
    /// <summary>1이면 잔다 (빠르기 · 각속도가 아주 작고 미는 힘이 없다 — 셈에서 빠진다).</summary>
    public byte Asleep;
    /// <summary>이 몸을 마지막으로 크게 민 원인 (뿜는 구멍 · 갈라짐 — 돌기 시작했다는 사실의 원인).</summary>
    public int PushCause;
    public byte Spun;
}

/// <summary>
/// ④ 칸 강체 (가) 몸 · 뿜는 힘 (docs/ENGINE.md ④ '바퀴 3 설계 — 칸 강체' · 함장 요청).
/// <para>
/// 배가 처음 갈라지면 배와 조각이 몸이 된다 — 질량 · 무게중심 · 회전관성 · 자세 · 빠르기 · 각속도. 칸은 제 번호를 그대로 지녀 기체 · 열 · 사람 · 설비는 옮기지 않아도 함께 간다 (강체 안은 서로 움직이지 않는다).
/// 우주로 트인 틈에서 빠지는 기체가 그 자리를 반대로 민다 (③이 틈마다 남긴 운동량) — 무게중심을 벗어나면 돈다. 배가 점화하면 배만 가속한다 (⑤의 요청).
/// </para>
/// </summary>
public sealed partial class Structure
{
    public readonly List<RigidBody> Bodies = new();
    /// <summary>우주로 간 기체가 가져간 운동량 (N·s · 세상 틀) · 원점에 대한 각운동량 · 점화가 배에 넣은 운동량 (법칙 장부 — 몸들의 운동량 = 점화 − 기체).</summary>
    public double GasPx, GasPy, GasL, BurnPx, BurnPy, BurnL, GasAbs;
    /// <summary>⑤의 요청: 배의 가속 (m/s² · 도면 좌표 — 엔진이 미는 쪽). 찍지 않는다 (⑤가 틱마다 넣는다).</summary>
    public double ShipAx, ShipAy;
    public long BodySteps, Spins;
    public const double SubStepM = 0.25, SubStepRad = 0.1, SpinRadS = 0.05;
    public const int MaxSubSteps = 64;

    /// <summary>칸이 속한 몸 (-1: 몸이 없다 — 날아간 파편 · 우주가 된 칸).</summary>
    public int BodyOf(int c) => c < 0 || c >= L.N || F.Severed[c] != 0 ? -1 : Frag[c] < 0 ? -1 : Frag[c];

    /// <summary>몸 b의 제 틀 자리 (도면 좌표 m) → 세상 자리.</summary>
    public (double x, double y) WorldOf(int b, double px, double py)
    {
        if (b < 0 || b >= Bodies.Count) return (px, py);
        var o = Bodies[b];
        double c = Math.Cos(o.Th), s = Math.Sin(o.Th), dx = px - o.Lx, dy = py - o.Ly;
        return (o.X + c * dx - s * dy, o.Y + s * dx + c * dy);
    }

    /// <summary>세상 자리 → 몸 b의 제 틀 자리.</summary>
    public (double x, double y) LocalOf(int b, double wx, double wy)
    {
        if (b < 0 || b >= Bodies.Count) return (wx, wy);
        var o = Bodies[b];
        double c = Math.Cos(o.Th), s = Math.Sin(o.Th), dx = wx - o.X, dy = wy - o.Y;
        return (o.Lx + c * dx + s * dy, o.Ly - s * dx + c * dy);
    }

    /// <summary>칸 가운데의 배 틀 자리 (화면 · 배에서 본 자리 — 배의 칸이면 제 자리 그대로).</summary>
    public (double x, double y) ShipFrameOf(int c)
    {
        double px = L.X(c) + 0.5, py = L.Y(c) + 0.5;
        int b = BodyOf(c);
        if (b <= 0 || Bodies.Count == 0) return (px, py);
        var (wx, wy) = WorldOf(b, px, py);
        return LocalOf(0, wx, wy);
    }

    /// <summary>몸 b의 제 틀 → 배 틀: 배 틀 자리 = (cos, sin)으로 돌리고 (tx, ty)만큼 옮긴다 (화면이 조각을 그릴 때).</summary>
    public (double cos, double sin, double tx, double ty) ToShipFrame(int b)
    {
        if (b <= 0 || b >= Bodies.Count || Bodies.Count == 0) return (1, 0, 0, 0);
        double th = Bodies[b].Th - Bodies[0].Th, c = Math.Cos(th), s = Math.Sin(th);
        var (ox, oy) = LocalOf(0, WorldOf(b, 0, 0).x, WorldOf(b, 0, 0).y);
        return (c, s, ox, oy);
    }

    /// <summary>몸의 질량 · 무게중심 · 회전관성 · 상자를 칸에서 다시 센다 (갈라지면) — 움직임은 그대로 이어진다 (새 무게중심의 빠르기 = v + ω × r).</summary>
    private void Refit(int b)
    {
        double m = 0, sx = 0, sy = 0, sxx = 0;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        for (int c = 0; c < L.N; c++)
        {
            if (Frag[c] != b || F.Severed[c] != 0 || !Solid(c)) continue;
            double mc = _mass[c], px = L.X(c) + 0.5, py = L.Y(c) + 0.5;
            m += mc; sx += mc * px; sy += mc * py; sxx += mc * (px * px + py * py + 1.0 / 6);
            x0 = Math.Min(x0, L.X(c)); y0 = Math.Min(y0, L.Y(c)); x1 = Math.Max(x1, L.X(c)); y1 = Math.Max(y1, L.Y(c));
        }
        var o = Bodies[b];
        if (m <= 0) { o.Mass = 0; o.Inertia = 0; o.Asleep = 1; Bodies[b] = o; return; }
        double lx = sx / m, ly = sy / m;
        var (wx, wy) = WorldOf(b, lx, ly);
        double rx = wx - o.X, ry = wy - o.Y;
        o.Vx -= o.W * ry; o.Vy += o.W * rx;
        o.X = wx; o.Y = wy; o.Lx = lx; o.Ly = ly;
        o.Mass = m; o.Inertia = Math.Max(1e-9, sxx - m * (lx * lx + ly * ly));
        o.MinX = x0; o.MinY = y0; o.MaxX = x1; o.MaxY = y1;
        double reach = 0;
        foreach (var (cx, cy) in new[] { (x0, y0), (x1 + 1, y0), (x0, y1 + 1), (x1 + 1, y1 + 1) }) reach = Math.Max(reach, Math.Sqrt((cx - lx) * (cx - lx) + (cy - ly) * (cy - ly)));
        o.Reach = reach;
        Bodies[b] = o;
    }

    /// <summary>몸이 새로 생긴다 (갈라짐): 배는 처음 갈라질 때 제자리에 · 조각은 갈라져 나온 몸(parent)의 자세 · 움직임을 이어받는다.</summary>
    private void SpawnBody(int id, int parent)
    {
        if (Bodies.Count == 0)
        {
            Bodies.Add(new RigidBody { Id = 0 });
            var o0 = Bodies[0]; o0.Mass = 0; Bodies[0] = o0;
            Refit0();
        }
        while (Bodies.Count < id) Bodies.Add(new RigidBody { Id = Bodies.Count, Asleep = 1 });
        var p = Bodies[parent];
        var o = new RigidBody { Id = id, Th = p.Th, Vx = p.Vx, Vy = p.Vy, W = p.W, Lx = p.Lx, Ly = p.Ly, X = p.X, Y = p.Y };
        if (Bodies.Count == id) Bodies.Add(o); else Bodies[id] = o;
        Refit(id);
        Refit(parent);
    }

    /// <summary>배의 몸을 처음 세운다: 제자리 (세상 틀 = 도면 틀).</summary>
    private void Refit0()
    {
        double m = 0, sx = 0, sy = 0;
        for (int c = 0; c < L.N; c++) if (Frag[c] == 0 && F.Severed[c] == 0 && Solid(c)) { m += _mass[c]; sx += _mass[c] * (L.X(c) + 0.5); sy += _mass[c] * (L.Y(c) + 0.5); }
        var o = Bodies[0];
        if (m > 0) { o.Lx = o.X = sx / m; o.Ly = o.Y = sy / m; }
        Bodies[0] = o;
        Refit(0);
    }

    /// <summary>몸 b의 세상 자리 (wx, wy)에 충격량 (jx, jy)를 준다 (N·s — 갈라짐 · 부딪힘 · 시험의 요청).</summary>
    public void Push(int b, double wx, double wy, double jx, double jy, int cause = 0)
    {
        if (b < 0 || b >= Bodies.Count) return;
        var o = Bodies[b];
        if (o.Mass <= 0) return;
        o.Vx += jx / o.Mass; o.Vy += jy / o.Mass;
        o.W += ((wx - o.X) * jy - (wy - o.Y) * jx) / o.Inertia;
        o.Asleep = 0;
        if (cause != 0) o.PushCause = cause;
        Bodies[b] = o;
    }

    /// <summary>몸들의 운동량 · 원점에 대한 각운동량 · 운동 에너지 (법칙 시험).</summary>
    public (double px, double py, double l, double ke) BodyMomentum()
    {
        double px = 0, py = 0, l = 0, ke = 0;
        foreach (var o in Bodies)
        {
            if (o.Mass <= 0) continue;
            px += o.Mass * o.Vx; py += o.Mass * o.Vy;
            l += o.Mass * (o.X * o.Vy - o.Y * o.Vx) + o.Inertia * o.W;
            ke += 0.5 * o.Mass * (o.Vx * o.Vx + o.Vy * o.Vy) + 0.5 * o.Inertia * o.W * o.W;
        }
        return (px, py, l, ke);
    }

    private readonly List<(int b, double px, double py, double dx, double dy, double imp)> _vents = new();

    /// <summary>
    /// 몸이 움직인다 (매 틱 — 몸이 있을 때만): ③의 우주로 트인 틈마다 빠진 기체의 운동량을 그 자리에서 반대로 · 배의 점화 —
    /// 한 틱의 충격량을 틱 처음과 끝에 반씩 (고른 힘과 같은 자리 정확도 · 운동량 · 각운동량 장부가 꼭 맞는다) · 사이는 힘 없이 잔걸음 (한 걸음에 25cm · 0.1rad 넘지 않게 — 부딪힘을 볼 걸음).
    /// 조각의 떨어진 거리를 고친다.
    /// </summary>
    private void MoveBodies(Sim sim, double dt)
    {
        int n = Bodies.Count;
        if (n == 0) return;
        _vents.Clear();
        foreach (var k in F.Links)
        {
            if (k.RB >= 0 || k.Impulse <= 0 || k.Dx == 0 && k.Dy == 0) continue;
            int b = BodyOf(k.A);
            if (b < 0 || b >= n || Bodies[b].Mass <= 0) continue;
            double px = k.Via != k.A ? L.X(k.Via) + 0.5 : L.X(k.A) + 0.5 + 0.5 * k.Dx, py = k.Via != k.A ? L.Y(k.Via) + 0.5 : L.Y(k.A) + 0.5 + 0.5 * k.Dy;
            _vents.Add((b, px, py, k.Dx, k.Dy, k.Impulse));
            var o = Bodies[b];
            int vc = VentCause(k);
            if (vc != 0) { o.PushCause = vc; Bodies[b] = o; }
        }
        bool burn = ShipAx != 0 || ShipAy != 0;
        Kick(0.5 * dt, burn);
        // 잔걸음 수 (빠르기 · 각속도로) — 힘 없이 곧게 · 돈다
        int steps = 1;
        for (int b = 0; b < n; b++)
        {
            var o = Bodies[b];
            if (o.Mass <= 0 || o.Asleep != 0) continue;
            double move = Math.Sqrt(o.Vx * o.Vx + o.Vy * o.Vy) * dt + Math.Abs(o.W) * o.Reach * dt;
            steps = Math.Max(steps, (int)Math.Ceiling(Math.Max(move / SubStepM, Math.Abs(o.W) * dt / SubStepRad)));
        }
        steps = Math.Clamp(steps, 1, MaxSubSteps);
        double h = dt / steps;
        for (int b = 0; b < n; b++)
        {
            var o = Bodies[b];
            if (o.Mass <= 0 || o.Asleep != 0) continue;
            for (int k = 0; k < steps; k++) { o.X += o.Vx * h; o.Y += o.Vy * h; o.Th += o.W * h; }
            BodySteps += steps;
            Bodies[b] = o;
        }
        Kick(0.5 * dt, burn);
        for (int b = 0; b < n; b++)
        {
            var o = Bodies[b];
            if (o.Mass <= 0) continue;
            bool pushed = b == 0 && burn || _vents.Exists(v => v.b == b);
            o.Asleep = (byte)(!pushed && Math.Abs(o.Vx) + Math.Abs(o.Vy) < 1e-7 && Math.Abs(o.W) < 1e-9 ? 1 : 0);
            // 빠지는 공기에 밀려 돌기 시작했다 (조각 — 한 번)
            if (b > 0 && o.Spun == 0 && Math.Abs(o.W) >= SpinRadS)
            {
                o.Spun = 1; Spins++;
                int fragCause = b <= Fragments.Count ? Fragments[b - 1].Cause : 0;
                int cause = sim.Causes.Add(CauseKind.Breach, $"떨어져 나간 조각이 {(o.PushCause != 0 ? "빠지는 공기에 밀려 " : "")}돌기 시작했다 (분에 {Math.Abs(o.W) * 60 / (2 * Math.PI):0.0}바퀴)", sim.Tick, CellNear(b), o.PushCause != 0 ? o.PushCause : fragCause);
                sim.Facts.Emit(sim.Tick, FactKind.Tumble, CellNear(b), (float)(o.Mass / 1000), cause, -1, b);
            }
            Bodies[b] = o;
        }
        // 조각의 떨어진 거리 (배에서 본)
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Fragments);
        for (int i = 0; i < span.Length; i++)
        {
            int b = span[i].Id;
            if (b <= 0 || b >= n) continue;
            var o = Bodies[b];
            var (sx, sy) = LocalOf(0, o.X, o.Y);
            span[i].Dist = (float)Math.Sqrt((sx - o.Lx) * (sx - o.Lx) + (sy - o.Ly) * (sy - o.Ly));
        }
    }

    /// <summary>이번 틱 힘의 반 (frac초 몫): 뿜는 틈마다 지금 자세의 자리 · 방향으로 · 점화는 배의 무게중심에 — 장부도 같은 자리로.</summary>
    private void Kick(double frac, bool burn)
    {
        foreach (var (b, px, py, dx, dy, imp) in _vents)
        {
            var o = Bodies[b];
            double c = Math.Cos(o.Th), s = Math.Sin(o.Th);
            double wdx = c * dx - s * dy, wdy = s * dx + c * dy;
            var (wx, wy) = WorldOf(b, px, py);
            double gx = 0.5 * imp * wdx, gy = 0.5 * imp * wdy;   // 기체가 가져간 운동량 (반)
            GasPx += gx; GasPy += gy; GasL += wx * gy - wy * gx; GasAbs += 0.5 * imp;
            Push(b, wx, wy, -gx, -gy);
        }
        if (burn && Bodies[0].Mass > 0)
        {
            var o = Bodies[0];
            double c = Math.Cos(o.Th), s = Math.Sin(o.Th);
            double jx = o.Mass * (c * ShipAx - s * ShipAy) * frac, jy = o.Mass * (s * ShipAx + c * ShipAy) * frac;
            BurnPx += jx; BurnPy += jy; BurnL += o.X * jy - o.Y * jx;
            Push(0, o.X, o.Y, jx, jy);
        }
    }

    /// <summary>뿜는 틈의 원인: 구멍 칸 · 안 칸 · 바깥 이웃(우주가 된 사이 칸 — 갈라짐)의 상처 원인.</summary>
    private int VentCause(Link k)
    {
        if (DamageCause[k.Via] != 0) return DamageCause[k.Via];
        if (DamageCause[k.A] != 0) return DamageCause[k.A];
        Span<int> nb = stackalloc int[4];
        L.Neighbors(k.A, nb);
        foreach (int j in nb) if (j >= 0 && DamageCause[j] != 0 && F.IsOut(j)) return DamageCause[j];
        return 0;
    }

    /// <summary>몸의 무게중심에 가장 가까운 칸 (사실 · 원인의 자리).</summary>
    private int CellNear(int b)
    {
        var o = Bodies[b];
        int best = -1; double bd = double.MaxValue;
        for (int c = 0; c < L.N; c++)
        {
            if (Frag[c] != b || F.Severed[c] != 0) continue;
            double dx = L.X(c) + 0.5 - o.Lx, dy = L.Y(c) + 0.5 - o.Ly, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    private void SaveBodies(StateWriter w)
    {
        w.Tag("몸"); w.List(Bodies);
        w.F64(GasPx); w.F64(GasPy); w.F64(GasL); w.F64(BurnPx); w.F64(BurnPy); w.F64(BurnL); w.F64(GasAbs); w.I64(BodySteps); w.I64(Spins);
    }

    private void LoadBodies(StateReader r)
    {
        r.Tag("몸"); Bodies.Clear(); Bodies.AddRange(r.List<RigidBody>());
        GasPx = r.F64(); GasPy = r.F64(); GasL = r.F64(); BurnPx = r.F64(); BurnPy = r.F64(); BurnL = r.F64(); GasAbs = r.F64(); BodySteps = r.I64(); Spins = r.I64();
    }
}
