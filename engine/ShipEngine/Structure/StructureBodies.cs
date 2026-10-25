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
    /// <summary>'돌기 시작했다' 원인 마디 (안의 것이 미끄러진 까닭).</summary>
    public int TumbleCause;
    /// <summary>지난 틱 끝의 빠르기 · 각속도 (덜컹임을 잰다) · 지난 틱에 받은 가속 (점화는 빼고 · 세상 틀 m/s²) · 각가속도 — 안의 것이 느끼는 힘 (⑧이 읽는다).</summary>
    public double PVx, PVy, PW, Ax, Ay, Al;
    /// <summary>이번 틱에 부딪혀 바뀐 빠르기 (m/s — 가장 큰 것: 부딪힘은 짧아 덜컹임보다 세게 휘청인다).</summary>
    public float Bump;
    /// <summary>원인(PushCause)이 된 밂의 크기 (N·s — 틱마다 반으로 준다: 더 큰 밂이 와야 원인이 바뀐다).</summary>
    public float PushImp;
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
    /// <summary>⑤의 요청: 그 점화의 원인 (점화에 깨어 미끄러진 물체의 원인 — 찍지 않는다 · ⑤가 틱마다 넣는다).</summary>
    public int ShipACause;
    public long BodySteps, Spins;
    public const double SubStepM = 0.25, SubStepRad = 0.1, SpinRadS = 0.05;
    public const int MaxSubSteps = 1024;   // 100m/s 넘는 몸도 한 걸음에 25cm 아래 (빠른 몸이 벽을 건너뛰지 않게)

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

    /// <summary>칸의 질량 (판 · 골조는 남은 잔칸만큼 + 설비).</summary>
    public double CellMass(int c) => Sub.Length == 0 || Sub[c] == SubFull ? _mass[c] : _mass[c] - _smass[c] * (1 - SubCount(Sub[c]) / 16.0);

    /// <summary>몸 b의 질량 모멘트 (제 틀 원점에 대해): 몸의 칸(구조가 남았든 아니든 — 통째로 나간 벽의 테두리 · 설비도 몸에 붙어 있다)은 잔칸마다 그 가운데에 (한 변 25cm 판 — 1/96) · 설비는 칸 가운데에 (1/6) ·
    /// 우주가 된 사이 칸에 남은 잔칸은 붙은 몸에 (찢긴 끝).</summary>
    private void Moments(int b, out double m, out double sx, out double sy, out double sxx, out (int x0, int y0, int x1, int y1) box)
    {
        m = sx = sy = sxx = 0;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        const double kSub = 1.0 / 96, kCell = 1.0 / 6;
        for (int c = 0; c < L.N; c++)
        {
            bool mine = Frag[c] == b && F.Severed[c] == 0;
            bool torn = !mine && F.Severed[c] != 0 && Sub[c] != 0 && _smass[c] > 0;
            if (!mine && !torn) continue;
            double cx = L.X(c) + 0.5, cy = L.Y(c) + 0.5;
            ushort mk = Sub.Length == 0 ? SubFull : Sub[c];
            if (mine)
            {
                double fur = _mass[c] - _smass[c];
                if (fur > 0) { m += fur; sx += fur * cx; sy += fur * cy; sxx += fur * (cx * cx + cy * cy + kCell); }
                if (L.Kind[c] == CellKind.Door && L.DoorAt[c] >= 0 && L.DoorAt[c] < DoorLeafOut.Length && DoorLeafOut[L.DoorAt[c]] != 0)   // 날아간 문짝 몫 (PH10 — 판 몫에서 뺀다)
                { double lk = LeafKg; m -= lk; sx -= lk * cx; sy -= lk * cy; sxx -= lk * (cx * cx + cy * cy + kCell); }
                if (mk == SubFull) { double ms = _smass[c]; m += ms; sx += ms * cx; sy += ms * cy; sxx += ms * (cx * cx + cy * cy + kCell); }
            }
            if (mk != SubFull || torn)
            {
                double each = _smass[c] / 16;
                for (int i = 0; i < 16; i++)
                {
                    if ((mk >> i & 1) == 0) continue;
                    if (torn && TornOwner(c, i % SubN, i / SubN) != b) continue;
                    double px = L.X(c) + (i % SubN + 0.5) / SubN, py = L.Y(c) + (i / SubN + 0.5) / SubN;
                    m += each; sx += each * px; sy += each * py; sxx += each * (px * px + py * py + kSub);
                }
            }
            if (mine && Solid(c)) { x0 = Math.Min(x0, L.X(c)); y0 = Math.Min(y0, L.Y(c)); x1 = Math.Max(x1, L.X(c)); y1 = Math.Max(y1, L.Y(c)); }
        }
        box = (x0, y0, x1, y1);
    }

    /// <summary>몸의 질량 · 무게중심 · 회전관성 · 상자를 칸에서 다시 센다 (갈라지면) — 움직임은 그대로 이어진다 (새 무게중심의 빠르기 = v + ω × r).</summary>
    private void Refit(int b)
    {
        Moments(b, out double m, out double sx, out double sy, out double sxx, out var bx);
        var (x0, y0, x1, y1) = bx;
        if (x0 == int.MaxValue) { x0 = y0 = 0; x1 = y1 = -1; }
        var o = Bodies[b];
        if (m <= 0) { o.Mass = 0; o.Inertia = 0; o.Asleep = 1; Bodies[b] = o; return; }
        double lx = sx / m, ly = sy / m;
        var (wx, wy) = WorldOf(b, lx, ly);
        double rx = wx - o.X, ry = wy - o.Y;
        o.Vx -= o.W * ry; o.Vy += o.W * rx;
        o.PVx -= o.PW * ry; o.PVy += o.PW * rx;
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
        o.PVx = p.PVx; o.PVy = p.PVy; o.PW = p.PW;
        if (Bodies.Count == id) Bodies.Add(o); else Bodies[id] = o;
        Refit(id);
        Refit(parent);
    }

    /// <summary>배의 몸을 처음 세운다: 제자리 (세상 틀 = 도면 틀).</summary>
    private void Refit0()
    {
        Moments(0, out double m, out double sx, out double sy, out _, out _);
        var o = Bodies[0];
        if (m > 0) { o.Lx = o.X = sx / m; o.Ly = o.Y = sy / m; }
        Bodies[0] = o;
        Refit(0);
    }

    /// <summary>
    /// 몸 b의 세상 자리 (wx, wy)에 충격량 (jx, jy)를 준다 (N·s — 갈라짐 · 몸끼리 부딪힘 · 부스러기 반동 · 붙음 · 시험의 요청) — 순간 바뀜이라 덜컹이다:
    /// 그 몸 위 모든 물체 (잠든 것도)의 바닥이 바뀐다 (세상 빠르기는 그대로 — 바닥에 대한 빠르기가 −(Δv + Δω × r)) · 몸 걸음 안이면 그 잔걸음 때에 (물체 걸음이 그때 준다) · 틱 사이면 곧장.
    /// </summary>
    public void Push(int b, double wx, double wy, double jx, double jy, int cause = 0)
    {
        if (b < 0 || b >= Bodies.Count) return;
        var o = Bodies[b];
        if (o.Mass <= 0) return;
        double dvx = jx / o.Mass, dvy = jy / o.Mass, dw = ((wx - o.X) * jy - (wy - o.Y) * jx) / o.Inertia;
        o.Vx += dvx; o.Vy += dvy; o.W += dw;
        o.Asleep = 0;
        double imp = Math.Sqrt(jx * jx + jy * jy);
        if (cause != 0 && imp >= o.PushImp) { o.PushCause = cause; o.PushImp = (float)imp; }
        Bodies[b] = o;
        double c = Math.Cos(o.Th), s = Math.Sin(o.Th), lx = c * dvx + s * dvy, ly = -s * dvx + c * dvy;   // 제 틀의 Δv
        if (_inBodies) _jolts.Add(new Jolt { B = b, Dvx = lx, Dvy = ly, Dw = dw, T = _subT, Cause = cause });
        else JoltProps(b, lx, ly, dw, cause);
    }

    /// <summary>덜컹 하나 (몸 · 제 틀의 Δv · Δω · 틱 처음에서 그 잔걸음 때 · 원인) — 찍지 않는다 (몸 걸음이 쌓고 같은 틱 물체 걸음이 쓴다).</summary>
    private struct Jolt { public int B; public double Dvx, Dvy, Dw, T; public int Cause; }
    private readonly List<Jolt> _jolts = new();
    private bool _inBodies;
    private double _subT;

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

    /// <summary>이번 틱 뿜는 틈 (몸 · 제 틀의 힘 N · 무게중심에 대한 돌림힘 N·m · 충격량 N·s — 그 틱 동안 고르다).</summary>
    private readonly List<(int b, double fx, double fy, double tq, double imp)> _vents = new();
    private double _burnDvx, _burnDvy;
    /// <summary>
    /// 이번 틱 몸마다 (찍지 않는다 — 몸 걸음이 쓰고 같은 틱 물체 걸음이 읽는다): 틱 처음 자세 · 움직임 (물체 장부) · 제 틀의 고른 가속 (힘 / 질량) · 각가속도 · 틱 처음 각속도.
    /// </summary>
    private struct BodySnap { public double X, Y, Th, Vx, Vy, W, Lx, Ly, Ax, Ay, Al; }
    private BodySnap[] _snap = Array.Empty<BodySnap>();

    /// <summary>
    /// 칸 c의 덜컹임 (도면 축 m/s — 그 몸의 틀 · 점화는 빼고): 지난 틱에 그 자리 바닥의 빠르기가 바뀐 만큼의 반대 —
    /// 고정 안 된 것은 이 빠르기로 미끄러지기 시작하고 (마찰로 v²/2μg만큼) · 선 사람은 이만큼 휘청인다 (뿜는 공기 · 갈라짐 · 부딪힘).
    /// </summary>
    public (double vx, double vy) JoltDv(int c)
    {
        int b = BodyOf(c);
        if (b < 0 || b >= Bodies.Count || Bodies[b].Mass <= 0) return (0, 0);
        var o = Bodies[b];
        double dt = Time.SecondsPerTick;
        double cs = Math.Cos(o.Th), sn = Math.Sin(o.Th);
        double lx = (cs * o.Ax + sn * o.Ay) * dt, ly = (-sn * o.Ax + cs * o.Ay) * dt, dw = o.Al * dt;
        double rx = L.X(c) + 0.5 - o.Lx, ry = L.Y(c) + 0.5 - o.Ly;
        return (-lx + dw * ry, -ly - dw * rx);
    }

    /// <summary>칸 c의 몸이 이번 틱에 부딪혀 바뀐 빠르기 (m/s).</summary>
    public double BumpAt(int c) { int b = BodyOf(c); return b >= 0 && b < Bodies.Count ? Bodies[b].Bump : 0; }

    /// <summary>덜컹임이 이어지는 때 (초) · 표준 중력 (m/s² — ⑤ 바깥의 G0와 같은 값 · 위층을 읽지 않으려 따로 둔다).</summary>
    public const double JoltSec = 1.0, G0 = 9.80665;

    /// <summary>
    /// 칸 c에 선 이가 느끼는 덜컹임의 세기 (g): 바닥이 1초 남짓에 그만큼 빠르기를 바꾼 것으로 (뿜는 공기 · 갈라짐 · 밀림) · 부딪힘은 0.2초에 —
    /// 둘 가운데 센 쪽. 넘어질지는 이 세기로 정한다 (⑧이 읽는다).
    /// </summary>
    public double JoltG(int c)
    {
        var (jx, jy) = JoltDv(c);
        return Math.Max(Math.Sqrt(jx * jx + jy * jy) / JoltSec, BumpAt(c) / BumpSec) / G0;
    }

    /// <summary>칸 c의 몸을 흔든 원인: 돌기 시작한 마디 → 민 것 → 갈라짐.</summary>
    public int BodyCause(int c)
    {
        int b = BodyOf(c);
        if (b < 0 || b >= Bodies.Count) return 0;
        var o = Bodies[b];
        return o.PushCause != 0 ? o.PushCause : o.TumbleCause != 0 ? o.TumbleCause : b > 0 && b <= Fragments.Count ? Fragments[b - 1].Cause : 0;
    }

    /// <summary>
    /// 몸이 움직인다 (매 틱 — 몸이 있을 때만): ③의 우주로 트인 틈마다 빠진 기체의 운동량을 그 자리에서 반대로 (F = 충격량 / dt) · 배의 점화 (m·ShipA) —
    /// 그 틱 동안 몸의 틀에서 고른 힘이라 잔걸음마다 고른 가속으로 적분한다 (x += v·h + ½a·h² · θ += ω·h + ½α·h² · 힘의 쪽은 잔걸음 처음 자세로 · 장부도 잔걸음마다 같은 값으로 —
    /// 틱 끝 자리 · 빠르기는 옛 반-차기와 같고 틱 가운데 길만 고르다). 잔걸음은 한 걸음에 25cm · 0.1rad 넘지 않게 (부딪힘 · 부스러기를 볼 걸음) · 그 사이 밂은 덜컹 (그 잔걸음 때).
    /// 조각의 떨어진 거리를 고친다.
    /// </summary>
    private void MoveBodies(Sim sim, double dt)
    {
        int n = Bodies.Count;
        _jolts.Clear();
        if (n == 0) return;
        _vents.Clear();
        for (int b = 0; b < n; b++) { var o = Bodies[b]; if (o.Bump != 0 || o.PushImp != 0) { o.Bump = 0; o.PushImp *= 0.5f; Bodies[b] = o; } }
        foreach (var k in F.Links)
        {
            if (k.RB >= 0 || k.Impulse <= 0 || k.Dx == 0 && k.Dy == 0) continue;
            int b = BodyOf(k.A);
            if (b < 0 || b >= n || Bodies[b].Mass <= 0) continue;
            double px = k.Via != k.A ? L.X(k.Via) + 0.5 : L.X(k.A) + 0.5 + 0.5 * k.Dx, py = k.Via != k.A ? L.Y(k.Via) + 0.5 : L.Y(k.A) + 0.5 + 0.5 * k.Dy;
            var o = Bodies[b];
            double fx = -k.Impulse / dt * k.Dx, fy = -k.Impulse / dt * k.Dy;   // 기체는 틈 바깥쪽으로 · 몸은 반대로
            _vents.Add((b, fx, fy, (px - o.Lx) * fy - (py - o.Ly) * fx, k.Impulse));
            int vc = VentCause(k);
            if (vc != 0 && k.Impulse >= o.PushImp) { o.PushCause = vc; o.PushImp = (float)k.Impulse; Bodies[b] = o; }
        }
        bool burn = ShipAx != 0 || ShipAy != 0;
        _burnDvx = _burnDvy = 0;
        // 이번 틱 몸마다 제 틀의 힘 · 돌림힘 (고르다) · 틱 처음 자세 (물체의 장부 · 틀)
        if (_snap.Length < n) _snap = new BodySnap[n];
        int steps = 1;
        for (int b = 0; b < n; b++)
        {
            var o = Bodies[b];
            double fx = 0, fy = 0, tq = 0;
            foreach (var v in _vents) if (v.b == b) { fx += v.fx; fy += v.fy; tq += v.tq; }
            if (b == 0 && burn && o.Mass > 0) { fx += o.Mass * ShipAx; fy += o.Mass * ShipAy; }
            bool pushed = fx != 0 || fy != 0 || tq != 0;
            if (pushed && o.Asleep != 0) { o.Asleep = 0; Bodies[b] = o; }
            _snap[b] = new BodySnap { X = o.X, Y = o.Y, Th = o.Th, Vx = o.Vx, Vy = o.Vy, W = o.W, Lx = o.Lx, Ly = o.Ly,
                Ax = o.Mass > 0 ? fx / o.Mass : 0, Ay = o.Mass > 0 ? fy / o.Mass : 0, Al = o.Mass > 0 ? tq / o.Inertia : 0 };
            if (o.Mass <= 0 || o.Asleep != 0) continue;
            // 잔걸음 수 (빠르기 · 각속도 · 고른 가속으로)
            double a = Math.Sqrt(fx * fx + fy * fy) / o.Mass, al = Math.Abs(tq) / o.Inertia;
            double turn = Math.Abs(o.W) * dt + 0.5 * al * dt * dt;
            double move = Math.Sqrt(o.Vx * o.Vx + o.Vy * o.Vy) * dt + 0.5 * a * dt * dt + turn * o.Reach;
            steps = Math.Max(steps, (int)Math.Min(MaxSubSteps, Math.Ceiling(Math.Max(move / SubStepM, turn / SubStepRad))));
        }
        steps = Math.Clamp(steps, 1, MaxSubSteps);
        double h = dt / steps;
        _inBodies = true;
        var tr = Track;
        if (tr != null) { if (_trackPrev.Length < n) _trackPrev = new RigidBody[n]; for (int b = 0; b < n; b++) { _trackPrev[b] = Bodies[b]; if (Bodies[b].Mass > 0 && Bodies[b].Asleep == 0) BodyKey(tr, b, 0, MotionTrack.EvNone); } }
        for (int k = 0; k < steps; k++)
        {
            for (int b = 0; b < n; b++)
            {
                var o = Bodies[b];
                if (o.Mass <= 0 || o.Asleep != 0) continue;
                Accelerate(ref o, b, h, burn);
                BodySteps++;
                Bodies[b] = o;
            }
            _subT = (k + 1) * h;
            sim.Facts.SubNow = (byte)Math.Clamp((int)Math.Round(_subT / dt * 255), 0, 255);   // 이 잔걸음에 난 부딪힘 · 부스러기 · 소리의 때
            int j0 = _jolts.Count;
            if (n > 1) Contacts(sim);   // (다) 부딪힘 — 잔걸음마다 (밂은 덜컹 — 그 때)
            MoveShards(sim, h);         // (마) 부스러기 알갱이
            if (tr != null) TrackBodies(tr, n, k * h, _subT, dt, j0);
        }
        _inBodies = false; _subT = 0;
        sim.Facts.SubNow = 0;
        for (int b = 0; b < n; b++)
        {
            var o = Bodies[b];
            if (o.Mass <= 0) continue;
            bool pushed = b == 0 && burn || _vents.Exists(v => v.b == b);
            // 안의 것이 느낄 가속: 지난 틱 끝에서 지금까지 바뀐 빠르기 (뿜는 힘 · 갈라짐 · 부딪힘 — 점화는 ⑤의 길로 따로)
            o.Ax = (o.Vx - o.PVx - (b == 0 ? _burnDvx : 0)) / dt; o.Ay = (o.Vy - o.PVy - (b == 0 ? _burnDvy : 0)) / dt; o.Al = (o.W - o.PW) / dt;
            o.PVx = o.Vx; o.PVy = o.Vy; o.PW = o.W;
            o.Asleep = (byte)(!pushed && Math.Abs(o.Vx) + Math.Abs(o.Vy) < 1e-7 && Math.Abs(o.W) < 1e-9 ? 1 : 0);
            // 빠지는 공기에 밀려 돌기 시작했다 (조각 — 한 번)
            if (b > 0 && o.Spun == 0 && Math.Abs(o.W) >= SpinRadS)
            {
                o.Spun = 1; Spins++;
                int fragCause = b <= Fragments.Count ? Fragments[b - 1].Cause : 0;
                int cause = sim.Causes.Add(CauseKind.Breach, $"떨어져 나간 조각이 {(o.PushCause != 0 ? "빠지는 공기에 밀려 " : "")}돌기 시작했다 (분에 {Math.Abs(o.W) * 60 / (2 * Math.PI):0.0}바퀴)", sim.Tick, CellNear(b), o.PushCause != 0 ? o.PushCause : fragCause);
                sim.Facts.Emit(sim.Tick, FactKind.Tumble, CellNear(b), (float)(o.Mass / 1000), cause, -1, b);
                o.TumbleCause = cause; o.PushCause = cause;   // 돌기 시작한 것이 안의 것의 까닭 (그 밂의 크기는 그대로 — 더 큰 밂이 오면 바뀐다)
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

    private RigidBody[] _trackPrev = Array.Empty<RigidBody>();

    private void BodyKey(MotionTrack tr, int b, double t, byte ev) { var o = Bodies[b]; tr.Add(MotionTrack.WhoBody, b, t, o.X, o.Y, o.Th, o.Vx, o.Vy, o.W, ev); }

    /// <summary>
    /// 틱 안 기록 — 몸 (보기용): 잔걸음 끝이 k/16을 지나면 열쇠틀 · 틱 끝 · 그 잔걸음에 밀렸으면 (부딪힘 · 반동 — 빠르기가 순간 바뀌었다) 잔걸음 처음 (밀리기 전 자세)과 끝 (부딪힘 — 곧은 토막) 둘.
    /// </summary>
    private void TrackBodies(MotionTrack tr, int n, double t0, double t1, double dt, int j0)
    {
        bool cross = Math.Floor(t1 * 16 / dt + 1e-9) > Math.Floor(t0 * 16 / dt + 1e-9) || t1 >= dt - 1e-9;
        for (int b = 0; b < n; b++)
        {
            var o = Bodies[b];
            if (o.Mass <= 0 || o.Asleep != 0 && _trackPrev[b].Asleep != 0) { _trackPrev[b] = o; continue; }
            bool pushed = false;
            for (int j = j0; j < _jolts.Count; j++) if (_jolts[j].B == b) { pushed = true; break; }
            if (pushed)
            {
                var q = _trackPrev[b];
                tr.Add(MotionTrack.WhoBody, b, t0, q.X, q.Y, q.Th, q.Vx, q.Vy, q.W);
                BodyKey(tr, b, t1, MotionTrack.EvHit);
            }
            else if (cross) BodyKey(tr, b, t1, MotionTrack.EvNone);
            _trackPrev[b] = o;
        }
    }

    /// <summary>
    /// 몸 하나의 잔걸음 h (고른 가속): 뿜는 틈마다 · 점화의 힘을 잔걸음 처음 자세로 세상 쪽으로 돌려 a = ΣF/m · α = Στ/I —
    /// 장부는 원점에 대한 각운동량까지 그 잔걸음의 참 적분으로 (∫X dt × F + τ·h · ∫X dt = X₀h + ½V₀h²) — 몸들의 운동량 = 점화 − 기체가 꼭 맞는다.
    /// </summary>
    private void Accelerate(ref RigidBody o, int b, double h, bool burn)
    {
        double c = Math.Cos(o.Th), s = Math.Sin(o.Th), ix = o.X * h + 0.5 * o.Vx * h * h, iy = o.Y * h + 0.5 * o.Vy * h * h;
        double fwx = 0, fwy = 0, tq = 0;
        foreach (var (vb, fx, fy, vtq, imp) in _vents)
        {
            if (vb != b) continue;
            double gx = c * fx - s * fy, gy = s * fx + c * fy;
            fwx += gx; fwy += gy; tq += vtq;
            GasPx -= gx * h; GasPy -= gy * h; GasL -= ix * gy - iy * gx + vtq * h; GasAbs += imp * h / _tickDt;   // 기체가 가져간 몫 (몸이 받은 것의 반대)
        }
        if (b == 0 && burn)
        {
            double gx = o.Mass * (c * ShipAx - s * ShipAy), gy = o.Mass * (s * ShipAx + c * ShipAy);
            fwx += gx; fwy += gy;
            BurnPx += gx * h; BurnPy += gy * h; BurnL += ix * gy - iy * gx; _burnDvx += gx / o.Mass * h; _burnDvy += gy / o.Mass * h;
        }
        double ax = fwx / o.Mass, ay = fwy / o.Mass, al = tq / o.Inertia;
        o.X += o.Vx * h + 0.5 * ax * h * h; o.Y += o.Vy * h + 0.5 * ay * h * h; o.Th += o.W * h + 0.5 * al * h * h;
        o.Vx += ax * h; o.Vy += ay * h; o.W += al * h;
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
        w.Arr(Sub); w.Arr(_blow); w.I64(SubCarved);
        SaveShards(w);
        w.F64(GasPx); w.F64(GasPy); w.F64(GasL); w.F64(BurnPx); w.F64(BurnPy); w.F64(BurnL); w.F64(GasAbs); w.I64(BodySteps); w.I64(Spins); w.I64(Crashes);
    }

    private void LoadBodies(StateReader r)
    {
        r.Tag("몸"); Bodies.Clear(); Bodies.AddRange(r.List<RigidBody>());
        r.ArrInto(Sub); r.ArrInto(_blow); SubCarved = r.I64(); _holeSeen = -1; _edgeVersion = -1;
        LoadShards(r);
        GasPx = r.F64(); GasPy = r.F64(); GasL = r.F64(); BurnPx = r.F64(); BurnPy = r.F64(); BurnL = r.F64(); GasAbs = r.F64(); BodySteps = r.I64(); Spins = r.I64(); Crashes = r.I64();
    }
}
