namespace ShipEngine;

/// <summary>
/// ④ 몸 위의 물체 (docs/PHYSICS.md §3-2 '바닥 몸의 관성' · '몸이 없는 배' · §4-1 깨우기 — PH4d): 물체 값은 그 칸 몸의 틀 (도면 좌표 · 바닥에 대한 빠르기)이다 —
/// 몸이 틱 안에서 고르게 가속하면 −a · 돌면 원심 ω²r · 코리올리 −2ω×v · 오일러 −α×r를 느낀다 (몸과 같은 적분식이라 마찰 없는 물체는 세상에 선다 — 갈릴레이).
/// 몸이 없는 배 (평시)는 무한 질량 틀이라 점화의 −m·ShipA만 느끼고 · 바닥 · 벽이 준 몫은 ToHost에 센다.
/// 덜컹 (Push — 순간 바뀜)은 바닥이 바뀐 것: 바닥에 대한 빠르기만 −(Δv + Δω × r) (세상 빠르기는 그대로 — 장부도 그대로) · 잠든 것도 깬다.
/// 잠든 것은 그 틀의 힘이 정지 마찰 (지금 칸 값 — 젖으면 0.33배)이나 넘어짐 문턱 b/Hc를 넘을 때 깬다 (넘어짐은 PH7).
/// 몸이 있으면 바닥 · 벽 · 가구가 물체에 준 몫을 틱 끝에 그 몸이 되받는다 — 그 몸에 실려 가는 잠든 것 · 막은 것과 함께 (운동량 · 원점에 대한 각운동량이 꼭 맞는다).
/// 모두 찍힌 상태 + 같은 틱 값으로만 정한다 (틱 안 표는 찍지 않는다).
/// </summary>
public sealed partial class Structure
{
    private double[] _pw0 = Array.Empty<double>();    // 물체마다 틱 처음 세상 운동량 · 각운동량 (몸이 있을 때 — 셋씩)
    private int[] _hostOf = Array.Empty<int>();        // 물체마다 이번 틱 실린 몸 (−1: 없음 · 몸이 없는 배)
    private bool[] _stepped = Array.Empty<bool>();     // 이번 틱 걸음을 셈한 물체
    private double[] _rJ = Array.Empty<double>();      // 몸마다 이번 틱 되받을 몫 (운동량 둘 · 원점에 대한 각운동량 — 바람이 준 몫은 뺀다)
    private bool _framed;                              // 이번 틱 물체가 느끼는 틀의 힘이 있다 (점화 · 뿜는 틈 · 도는 몸)
    private readonly Dictionary<DotForm, byte> _baseMat = new();

    /// <summary>물체의 겉 재질 (아직 깨지 않았으면 꼴에서 — 몸 값을 채우지 않고).</summary>
    private byte MatOf(in Prop p)
    {
        if (p.Kg > 0) return p.Mat;
        var fm = PropForm(p);
        if (!_baseMat.TryGetValue(fm, out byte m)) _baseMat[fm] = m = BaseMat(fm);
        return m;
    }

    /// <summary>잠든 물체를 깨운다 (몸 값을 채우고 · 원인) — 깼으면 참.</summary>
    private bool Wake(ref Prop p, int cause)
    {
        if (p.Kg <= 0) FillBody(ref p);
        bool was = Awake(p);
        if (!was) p.State = 1;
        p.Asleep = 0; p.Still = 0;
        if (cause != 0) p.Cause = cause;
        if (!was) PropWoken++;
        return !was;
    }

    /// <summary>덜컹이 잠든 것을 깨우나: 바닥이 바뀐 몫 (Δv · 제 돎 ρΔω)으로 미끄러질 길 v²/2μg'가 1mm를 넘으면 (무중력이면 1mm/s 넘게) — 그보다 작으면 실려 간다.</summary>
    private bool JoltWakes(in Prop p, int c, double ux, double uy, double dw)
    {
        double rho = PropForm(p).Rho, v2 = ux * ux + uy * uy + rho * rho * dw * dw;
        double g = c >= 0 ? Gravity * F.Grav[c] : 0, mu = c >= 0 ? T.Materials[Math.Min(MatOf(p), T.Materials.Count - 1)].Def.Mu * (F.Stored(T.sWater, c) > WetMol ? WetShare : 1) : 0;
        return g * mu > 0 ? v2 > 2 * mu * g * JoltWakeM : v2 > SleepV * SleepV;
    }

    /// <summary>덜컹이 깨우는 미끄러질 길 (m — 이보다 짧으면 잠든 채 실려 간다: 부스러기 반동 같은 작은 밂이 배 안 물건을 다 깨우지 않게).</summary>
    public const double JoltWakeM = 1e-3;

    /// <summary>
    /// 덜컹 (틱 사이 — 시험의 요청 · 갈라짐 · 부스러기 반동): 그 몸 위 물체마다 바닥에 대한 빠르기 −(Δv + Δω × r) · 제 돎 −Δω (세상 빠르기는 그대로) · 잠든 것은 1mm 넘게 미끄러질 만큼이면 깬다 —
    /// 깨지 않은 것 (잠든 것 · 막은 것 · 낀 것)은 바닥과 함께 실려 가 그만큼 세상 운동량을 얻으니 곧장 몸이 되받는다 (몸 걸음 안의 덜컹은 틱 끝 반작용이 — 장부가 밀자마자 맞는다).
    /// </summary>
    private void JoltProps(int b, double lx, double ly, double dw, int cause)
    {
        if (lx == 0 && ly == 0 && dw == 0) return;
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);
        var o = Bodies[b];
        double cs = Math.Cos(o.Th), sn = Math.Sin(o.Th), jx = 0, jy = 0, k = 0;
        for (int i = 0; i < all.Length; i++)
        {
            ref var p = ref all[i];
            if (p.State == 5) continue;
            int c = CellOf(p);
            if (BodyOf(c) != b) continue;
            double dx = p.X - o.Lx, dy = p.Y - o.Ly, ux = lx - dw * dy, uy = ly + dw * dx;
            if (Awake(p) || p.State == 0 && JoltWakes(p, c, ux, uy, dw))
            {
                Wake(ref p, cause != 0 ? cause : BodyCause(c));
                p.Vx -= ux; p.Vy -= uy; p.W -= dw;
                continue;
            }
            var (m, I) = MassOf(p);   // 실려 간다 — 세상 빠르기가 바닥과 함께 (Δv + Δω × r) 바뀐 몫
            double gx = cs * ux - sn * uy, gy = sn * ux + cs * uy, rx = cs * dx - sn * dy, ry = sn * dx + cs * dy;
            jx += m * gx; jy += m * gy; k += m * ((o.X + rx) * gy - (o.Y + ry) * gx) + I * dw;
        }
        if (jx != 0 || jy != 0 || k != 0) TakeBack(all, b, jx, jy, k, false);
    }

    /// <summary>
    /// 틱 처음 (몸 걸음 뒤 · 물체 걸음 앞): 물체마다 실린 몸 · 몸이 있으면 틱 처음 세상 운동량 (몸의 틱 처음 자세로) · 되받을 몫을 비운다 ·
    /// 이번 틱 틀의 힘이 있나 (몸이 없는 배는 점화 · 몸이 있으면 몸마다 고른 가속 · 각가속도 · 각속도 · 덜컹).
    /// </summary>
    private void BeginFrames(ReadOnlySpan<Prop> all, bool world)
    {
        int n = all.Length, nb = Bodies.Count;
        if (_hostOf.Length < n) { _hostOf = new int[n + 16]; _stepped = new bool[n + 16]; _pw0 = new double[3 * (n + 16)]; }
        Array.Clear(_stepped, 0, n);
        if (!world) { _framed = ShipAx != 0 || ShipAy != 0; Array.Fill(_hostOf, -1, 0, n); return; }
        _framed = _jolts.Count > 0;
        for (int b = 0; b < nb; b++) if (Bodies[b].Mass > 0 && (_snap[b].Ax != 0 || _snap[b].Ay != 0 || _snap[b].Al != 0 || _snap[b].W != 0)) _framed = true;
        if (_rJ.Length < 3 * nb) _rJ = new double[3 * nb + 6];
        Array.Clear(_rJ, 0, 3 * nb);
        for (int i = 0; i < n; i++)
        {
            int b = BodyOf(CellOf(all[i]));
            _hostOf[i] = b >= 0 && b < nb && Bodies[b].Mass > 0 ? b : -1;
            if (_hostOf[i] < 0) continue;
            var (px, py, l) = WorldMomentum(all[i], _snap[b].X, _snap[b].Y, _snap[b].Th, _snap[b].Vx, _snap[b].Vy, _snap[b].W, _snap[b].Lx, _snap[b].Ly);
            _pw0[3 * i] = px; _pw0[3 * i + 1] = py; _pw0[3 * i + 2] = l;
        }
    }

    /// <summary>몸의 자세 · 움직임 (X · Y · θ · V · ω · 무게중심의 제 틀 자리)으로 본 물체의 세상 운동량 · 원점에 대한 각운동량.</summary>
    private (double px, double py, double l) WorldMomentum(in Prop p, double X, double Y, double th, double vx, double vy, double w, double lx, double ly)
    {
        var (m, I) = MassOf(p);
        double c = Math.Cos(th), s = Math.Sin(th), dx = p.X - lx, dy = p.Y - ly, rx = c * dx - s * dy, ry = s * dx + c * dy;
        double ux = vx - w * ry + c * p.Vx - s * p.Vy, uy = vy + w * rx + s * p.Vx + c * p.Vy;
        return (m * ux, m * uy, m * ((X + rx) * uy - (Y + ry) * ux) + I * (w + p.W));
    }

    /// <summary>
    /// 물체가 그 몸의 틀에서 느끼는 힘 (잔걸음 처음 때 τ · 코리올리는 빼고 — 부른 쪽이 빠르기를 돌린다): −m·a몸 · 원심 m ω² r · 오일러 m α (r_y, −r_x) · 제 돎 −I α ·
    /// 몸이 없는 배는 −m·ShipA. 그 몸의 각속도 w (τ 때 — 덜컹 몫 jw 포함). 힘이 없으면 거짓.
    /// </summary>
    private bool FrameForce(in Prop p, int b, double tau, double jw, double m, double I, out double fx, out double fy, out double tq, out double w)
    {
        fx = fy = tq = w = 0;
        if (b < 0)
        {
            if (Bodies.Count > 0 || ShipAx == 0 && ShipAy == 0) return false;
            fx = -m * ShipAx; fy = -m * ShipAy;
            return true;
        }
        var f = _snap[b];
        w = f.W + f.Al * tau + jw;
        if (f.Ax == 0 && f.Ay == 0 && f.Al == 0 && w == 0) return false;
        var o = Bodies[b];
        double dx = p.X - o.Lx, dy = p.Y - o.Ly, w2 = w * w;
        fx = m * (-f.Ax + w2 * dx + f.Al * dy); fy = m * (-f.Ay + w2 * dy - f.Al * dx); tq = -I * f.Al;
        return true;
    }

    /// <summary>
    /// 깨우기 — 옆 가속 (§4-1): 잠든 것마다 그 틀의 힘 (빠르기 0일 때) |a| > g' · min(μs 지금 칸, b/Hc)면 깬다 — μs는 지금 칸 값 (젖으면 0.33배) ·
    /// b는 힘이 미는 쪽 받침 끝까지 (넘어짐 여유 · 넘어짐 자체는 PH7) · 무중력이면 조금이라도. 캐시 없이 틱마다 하나씩 본다.
    /// </summary>
    private bool WakeByFrame(Span<Prop> all, bool world)
    {
        bool woke = false;
        for (int i = 0; i < all.Length; i++)
        {
            ref var p = ref all[i];
            if (p.State != 0) continue;
            int c = CellOf(p);
            if (c < 0) continue;
            int b = _hostOf[i];
            if (world && b < 0) continue;
            if (!FrameForce(p, b, 0, 0, 1, 0, out double ax, out double ay, out _, out _)) continue;
            double a2 = ax * ax + ay * ay;
            if (a2 == 0) continue;
            double g = Gravity * F.Grav[c];
            double mu = StaticShare * T.Materials[Math.Min(MatOf(p), T.Materials.Count - 1)].Def.Mu * (F.Stored(T.sWater, c) > WetMol ? WetShare : 1);
            var fm = PropForm(p);
            double cs = Math.Cos(p.Th), sn = Math.Sin(p.Th), u = cs * ax + sn * ay, v = -sn * ax + cs * ay;   // 꼴 틀의 힘 쪽
            int side = Math.Abs(u) >= Math.Abs(v) ? (u > 0 ? 0 : 2) : (v > 0 ? 1 : 3);
            double tip = fm.Hc > 0 ? fm.TipB[side] / fm.Hc : double.PositiveInfinity;
            double lim = g * Math.Min(mu, tip);
            if (a2 <= lim * lim) continue;
            woke |= Wake(ref p, b < 0 ? ShipACause : BodyCause(c));
        }
        return woke;
    }

    /// <summary>깨우기 — 덜컹 (몸 걸음 안의 밂 — 몸끼리 부딪힘 · 부스러기): 그 몸 위 잠든 것마다 바닥이 바뀐 몫으로 1mm 넘게 미끄러질 만큼이면 깬다 (빠르기는 그 때 물체 걸음이 준다 · 작으면 실려 간다 — 장부는 틱 끝 반작용이 맞춘다).</summary>
    private bool WakeByJolts(Span<Prop> all)
    {
        bool woke = false;
        foreach (var j in _jolts)
        {
            var o = Bodies[j.B];
            for (int i = 0; i < all.Length; i++)
            {
                ref var p = ref all[i];
                if (p.State != 0 || _hostOf[i] != j.B) continue;
                double dx = p.X - o.Lx, dy = p.Y - o.Ly, ux = j.Dvx - j.Dw * dy, uy = j.Dvy + j.Dw * dx;
                int c = CellOf(p);
                if (!JoltWakes(p, c, ux, uy, j.Dw)) continue;
                woke |= Wake(ref p, j.Cause != 0 ? j.Cause : BodyCause(c));
            }
        }
        return woke;
    }

    /// <summary>
    /// 물체가 받은 바람 몫을 세상 틀로 (몸이 있을 때 — 그 몸의 τ 때 자세로 어림): 기체가 그만큼 잃고 (장부 Gas) · 몸은 그 몫을 되받지 않는다.
    /// </summary>
    private void WindToWorld(in Prop p, int b, double tau, double fx, double fy, double tq, double h)
    {
        var f = _snap[b];
        double th = f.Th + f.W * tau + 0.5 * f.Al * tau * tau, c = Math.Cos(th), s = Math.Sin(th), c0 = Math.Cos(f.Th), s0 = Math.Sin(f.Th);
        double X = f.X + f.Vx * tau + 0.5 * (c0 * f.Ax - s0 * f.Ay) * tau * tau, Y = f.Y + f.Vy * tau + 0.5 * (s0 * f.Ax + c0 * f.Ay) * tau * tau;
        var o = Bodies[b];
        double dx = p.X - o.Lx, dy = p.Y - o.Ly, x = X + c * dx - s * dy, y = Y + s * dx + c * dy;
        double gx = (c * fx - s * fy) * h, gy = (s * fx + c * fy) * h, k = x * gy - y * gx + tq * h;
        GasPx -= gx; GasPy -= gy; GasL -= k;
        _rJ[3 * b] -= gx; _rJ[3 * b + 1] -= gy; _rJ[3 * b + 2] -= k;
    }

    /// <summary>
    /// 틱 끝 반작용 (몸이 있을 때): 물체마다 세상 운동량이 바뀐 몫 (바람 몫은 뺐다)을 그 몸이 되받는다 — 걸음을 셈한 것 · 우주로 간 것 (가져간 몫은 장부 '우주로') ·
    /// 움직인 몸에 실려 간 잠든 것 · 막은 것 — 그 몸과 실려 가는 것을 한 덩어리로 (TakeBack).
    /// </summary>
    private void Reactions(Span<Prop> all)
    {
        int nb = Bodies.Count;
        Span<bool> moved = nb <= 64 ? stackalloc bool[nb] : new bool[nb];
        for (int b = 0; b < nb; b++) { var o = Bodies[b]; moved[b] = o.Mass > 0 && (o.Vx != _snap[b].Vx || o.Vy != _snap[b].Vy || o.W != _snap[b].W || o.W != 0); }
        foreach (var j in _jolts) moved[j.B] = true;
        for (int i = 0; i < all.Length; i++)
        {
            int b = _hostOf[i];
            if (b < 0 && all[i].State == 5) { var q = all[i]; PropPxGone += q.Kg * q.Vx; PropPyGone += q.Kg * q.Vy; PropLGone += q.Kg * (q.X * q.Vy - q.Y * q.Vx) + q.Inertia * q.W; }   // 몸 밖 칸에서 나간 것 (도면 틀 그대로)
            if (b < 0 || !_stepped[i] && !moved[b]) continue;
            var o = Bodies[b];
            var (px, py, l) = WorldMomentum(all[i], o.X, o.Y, o.Th, o.Vx, o.Vy, o.W, o.Lx, o.Ly);
            if (all[i].State == 5) { PropPxGone += px; PropPyGone += py; PropLGone += l; }   // 우주로 — 세상 틀로 가져갔다
            _rJ[3 * b] += px - _pw0[3 * i]; _rJ[3 * b + 1] += py - _pw0[3 * i + 1]; _rJ[3 * b + 2] += l - _pw0[3 * i + 2];
        }
        for (int b = 0; b < nb; b++)
        {
            double jx = _rJ[3 * b], jy = _rJ[3 * b + 1], k = _rJ[3 * b + 2];
            if (jx != 0 || jy != 0 || k != 0) TakeBack(all, b, jx, jy, k, true);
        }
    }

    /// <summary>
    /// 몸 b가 물체가 얻은 세상 운동량 (jx, jy) · 원점에 대한 각운동량 k를 되받는다 — 그 몸과 실려 가는 것 (깨지 않은 것 — 잠든 것 · 막은 것 · 낀 것)을 한 덩어리로 (질량 S · 첫 모멘트 Q · 둘째 R²)
    /// (M + S)ΔV + Δω × Q = −J · Q × ΔV + (I + R²)Δω = −K를 풀고 · 깨어 있는 것은 덜컹처럼 바닥에 대한 빠르기만 고친다 (세상 빠르기 그대로). 사람이 느끼는 덜컹에는 넣지 않는다.
    /// 실린 몸은 틱 끝이면 틱 처음 것 (tickEnd) · 틱 사이면 지금 칸의 몸.
    /// </summary>
    private void TakeBack(Span<Prop> all, int b, double jx, double jy, double k, bool tickEnd)
    {
        var o = Bodies[b];
        if (o.Mass <= 0) return;
        double S = 0, qx = 0, qy = 0, r2 = 0, c = Math.Cos(o.Th), s = Math.Sin(o.Th);
        for (int i = 0; i < all.Length; i++)
        {
            if ((tickEnd ? _hostOf[i] : BodyOf(CellOf(all[i]))) != b || Awake(all[i]) || all[i].State == 5) continue;
            var (m, I) = MassOf(all[i]);
            double dx = all[i].X - o.Lx, dy = all[i].Y - o.Ly, rx = c * dx - s * dy, ry = s * dx + c * dy;
            S += m; qx += m * rx; qy += m * ry; r2 += m * (rx * rx + ry * ry) + I;
        }
        double M = o.Mass + S, kx = k - (o.X * jy - o.Y * jx);   // 몸 무게중심에 대한 몫
        double dw = (-kx + (qx * jy - qy * jx) / M) / (o.Inertia + r2 - (qx * qx + qy * qy) / M);
        double dvx = (-jx + dw * qy) / M, dvy = (-jy - dw * qx) / M;
        o.Vx += dvx; o.Vy += dvy; o.W += dw; o.PVx += dvx; o.PVy += dvy; o.PW += dw;
        if (dvx != 0 || dvy != 0 || dw != 0) o.Asleep = 0;
        Bodies[b] = o;
        double lx = c * dvx + s * dvy, ly = -s * dvx + c * dvy;
        for (int i = 0; i < all.Length; i++)
        {
            ref var p = ref all[i];
            if ((tickEnd ? _hostOf[i] : BodyOf(CellOf(p))) != b || !Awake(p)) continue;
            double dx = p.X - o.Lx, dy = p.Y - o.Ly;
            p.Vx -= lx - dw * dy; p.Vy -= ly + dw * dx; p.W -= dw;
        }
    }
}
