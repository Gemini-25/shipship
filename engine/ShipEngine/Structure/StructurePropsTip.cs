namespace ShipEngine;

/// <summary>
/// ④ 높이 · 받침 · 떨어짐 · 넘어짐 · 떠 있음 (docs/PHYSICS.md §3-3 · §3-4 · PH7 — 2.5D): 물체는 밑면 높이 Z · 오르내림 Vz를 갖는다.
/// 받침 = 바닥 (0) · 제자리 가구 기둥의 윗면 (그 꼴의 위층 — 탁자 0.75m) 가운데 무게중심 아래 · 밑면 아래의 가장 높은 것. 받침보다 높으면 떨어진다 (z'' = −g' — 닿는 때를 이차식으로 정확히 끊는다) ·
/// 닿으면 Vz' = −e Vz (|e Vz| &lt; 0.3m/s면 선다 · e = 둘 가운데 작은 것) · 평면 빠르기 · 각속도는 마찰 충격량 μ m |ΔVz| 안에서 준다 · 흡수 E = ½ m Vz² (1 − e²).
/// 가구 기둥은 높이 [아래 층, 위 층)에서만 막는다 — 탁자 위 컵은 바닥 상자와 부딪히지 않고 · 바닥 상자는 탁자 다리 사이로 지난다 (물체끼리도 높이가 겹칠 때만 닿는다).
/// 넘어짐 (하우스너 1963 흔들 블록): 옆 힘 F가 높이 h_F에 걸려 F h_F &gt; m g' b이고 F &lt; μs N이면 모서리를 축으로 넘어진다 (아니면 미끄러진다) · 덜컹 문턱 ½ Iₚ ω₀² ≥ m g' (R − Hc) ·
/// 넘어지는 동안 Iₚ ψ'' = F h_F cos ψ − m g' R sin(β − ψ) (잔걸음 RK4) · ψ ≥ π/2면 누운 꼴 (그 쪽으로 b + Hc 옮긴다 · 닿는 에너지 ½ Iₚ ψ'²는 마지막 토막을 일로 정확히) · β 앞에서 힘이 빠지면 흔들려 돌아와 선다.
/// 누운 둥근 것은 구른다 (구르는 쪽은 구름 저항 0.02 · 구르는 관성 1.5m · 축 쪽은 바닥 마찰). 무중력 (Grav &lt; 0.01)이면 떠 있음: 바닥 ~ 천장 2.5m 사이에서 튀고 ·
/// 평면 부딪힘의 0.2|Δv|가 오르내림으로 · 공조 바람 (통풍구 둘레 — 통풍구에서 밖으로)에 흘러간다 · 중력이 사라지는 틱에 놓인 것은 조금 들뜨고 · 돌아오면 그 높이에서 떨어진다.
/// </summary>
public sealed partial class Structure
{
    public const double Ceiling = 2.5, LandStop = 0.3, FloatG = 0.01, TipG = 0.05, HandZ = 1.0, RollMu = 0.02, RollMass = 1.5, AirToVz = 0.2;
    private const double ZEps = 1e-6, TipH = 0.005, LiftV = 0.02;

    // 높이 창 (막힌 도트를 물을 때 — 그 물체가 차지한 높이 [_zLo, _zHi) · 끝없음이면 온 높이: 가구 기둥은 이 창과 겹칠 때만 막는다)
    private double _zLo, _zHi = double.PositiveInfinity;

    /// <summary>꼴의 높이 (m — 층 수 × 1/16).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static double HeightOf(DotForm fm) => fm.H * DotForm.Layer;

    /// <summary>두 물체의 높이 범위가 겹치나 (겹칠 때만 서로 닿는다 — 탁자 위 컵과 바닥 상자는 지나간다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool ZMeet(in Prop a, in Prop b) => a.Z < b.Z + HeightOf(PropForm(b)) - ZEps && b.Z < a.Z + HeightOf(PropForm(a)) - ZEps;

    /// <summary>높이 창을 그 물체가 차지한 높이로 (부른 쪽이 끝에 <see cref="FullHeight"/>로 되돌린다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void HeightWindow(in Prop p, DotForm fm) { _zLo = p.Z; _zHi = p.Z + HeightOf(fm); }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void FullHeight() { _zLo = 0; _zHi = double.PositiveInfinity; }

    /// <summary>가구 기둥 (꼴 도트 x, y)이 높이 창과 겹치나 (기둥은 [아래 층, 위 층) — 받침으로 선 윗면 · 다리 사이 빈 곳은 막지 않는다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool ColumnBlocks(DotForm fm, int x, int y)
    {
        int i = y * fm.W + x;
        double bot = fm.Bot[i] * DotForm.Layer, top = fm.Top[i] * DotForm.Layer;
        return bot < _zHi - ZEps && top > _zLo + ZEps;
    }

    /// <summary>
    /// 받침 (그 점 (x, y) 아래 · 높이 z 아래의 가장 높은 것): 바닥 0 · 제자리 가구 기둥의 윗면 (윗면이 z + 작은 몫 아래일 때 — 위에 선 것) — on: 0 바닥 · 가구 + 1 · 재질 (반발 · 마찰).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private double SupportAt(double x, double y, double z, out int on, out byte mat)
    {
        on = 0;
        if (_steel < 0) _steel = Math.Max(0, T.Materials.FindIndex(m => m.Id == "steel"));
        mat = (byte)_steel;
        int dx = (int)Math.Floor(x * 32), dy = (int)Math.Floor(y * 32), cx = dx >> 5, cy = dy >> 5;
        if (!L.In(cx, cy)) return 0;
        int c = L.Index(cx, cy);
        if (L.Kind[c] != CellKind.Floor) return 0;
        int fu = L.FurnitureAt[c];
        if (fu < 0 || fu >= FurnPlaced.Length || FurnPlaced[fu] == 0) return 0;
        var (fm, ox, oy) = FurnForm(fu);
        if (fm == null) return 0;
        int lx = dx - ox, ly = dy - oy;
        if (!fm.Solid(lx, ly)) return 0;
        double top = fm.Top[ly * fm.W + lx] * DotForm.Layer;
        if (top > z + 1e-3) return 0;   // 그 기둥 아래에 있다 (탁자 밑) · 기둥 속은 평면 닿음이 민다
        on = fu + 1; mat = fm.Mat[ly * fm.W + lx];
        return top;
    }

    /// <summary>그 점에 손 높이 (1m)에서 내려놓으면 설 받침 높이 (탁자 위면 그 윗면 · 아니면 바닥).</summary>
    public double RestHeight(double x, double y) => SupportAt(x, y, HandZ, out _, out _);

    /// <summary>
    /// 잔걸음 처음의 높이 (깨어 있는 것): 무중력이면 떠 있음 · 받침보다 높거나 오르는 중이면 떨어지는 중 · 아니면 받침에 선다 (자리를 받침에 맞춘다).
    /// 이미 받침 · 바닥 · 천장에 닿아 다가가는 것은 곧장 튄다 (길이 0인 잔걸음을 만들지 않는다). 닿는 때 Hz (떨어짐은 이차식 · 떠 있음은 곧게 — 이 잔걸음 h 안이면 그 때로 끊는다).
    /// 참이면 떠 있다 (바닥이 누르지 않는다).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool HeightBegin(Sim sim, ref PropRun r, ref Prop p, int c, double tau, ref double h)
    {
        double g = Gravity * F.Grav[c];
        r.Hz = double.PositiveInfinity; r.Gz = g;
        r.Zs = SupportAt(p.X, p.Y, p.Z, out r.On, out r.ZMat);
        p.G = F.Grav[c];
        if (F.Grav[c] < FloatG)
        {
            if (p.State != 4) { p.State = 4; if (p.Tilt != 0) { p.Tilt = 0; p.TiltW = 0; } }
            double top = Ceiling - HeightOf(r.Fm), e = Rest(r);
            if (p.Z <= r.Zs + ZEps && p.Vz < 0) { p.Z = (float)r.Zs; p.Vz = (float)(-e * p.Vz); }
            else if (p.Z >= top - ZEps && p.Vz > 0) { p.Z = (float)top; p.Vz = (float)(-e * p.Vz); }
            r.Hz = p.Vz < 0 ? (p.Z - r.Zs) / -p.Vz : p.Vz > 0 ? (top - p.Z) / p.Vz : double.PositiveInfinity;
            if (r.Hz < h) h = r.Hz;
            return true;
        }
        if (p.State == 4) p.State = 3;   // 중력이 돌아왔다 — 그 높이에서 떨어진다
        if (p.State == 3 && p.Z <= r.Zs + ZEps && p.Vz <= 0) Land(sim, ref r, ref p, -p.Vz, tau);   // 이미 닿았다
        if (p.State == 3 || p.Z > r.Zs + ZEps || p.Vz > 0)
        {
            if (p.State != 3) p.State = 3;
            double dz = Math.Max(0, p.Z - r.Zs);
            r.Hz = (p.Vz + Math.Sqrt(p.Vz * p.Vz + 2 * g * dz)) / g;
            if (r.Hz < h) h = r.Hz;
            return true;
        }
        if (p.Z != r.Zs) p.Z = (float)r.Zs;
        p.On = r.On; p.Vz = 0;
        return false;
    }

    /// <summary>물체와 받침 (바닥 · 가구 윗면 · 천장은 강철) 가운데 작은 반발.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private double Rest(in PropRun r) => Math.Min(r.Mat.Rest, T.Materials[Math.Min(r.ZMat, T.Materials.Count - 1)].Def.Rest);

    /// <summary>
    /// 잔걸음 끝의 높이 (떠 있는 것 · 떨어지는 것): Z · Vz를 h만큼 (떨어지면 z'' = −g') · 닿는 때로 끊은 잔걸음이면 닿는다 — 떨어지던 것은 <see cref="Land"/> ·
    /// 떠 있는 것은 바닥 · 천장에서 −e Vz로 튄다.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void HeightEnd(Sim sim, ref PropRun r, ref Prop p, double tau, double h)
    {
        double g = r.Gz;
        bool floating = p.State == 4, land = h >= r.Hz - 1e-12;
        double t = land ? r.Hz : h, vz0 = p.Vz;
        if (floating) p.Z += (float)(p.Vz * t);
        else { p.Z = (float)(p.Z + p.Vz * t - 0.5 * g * t * t); p.Vz = (float)(vz0 - g * t); }
        if (!land) { if (p.Z < r.Zs) p.Z = (float)r.Zs; return; }
        if (floating)
        {
            double top = Ceiling - HeightOf(r.Fm), e = Rest(r);
            p.Z = (float)(p.Vz > 0 ? top : r.Zs); p.Vz = (float)(-e * p.Vz);
            return;
        }
        Land(sim, ref r, ref p, Math.Max(0, g * t - vz0), tau + t);
    }

    /// <summary>
    /// 떨어지던 것이 받침에 닿았다 (다가오는 빠르기 vin · 때 t1): Vz' = e vin (0.3m/s 아래면 선다) · 평면 빠르기 · 각속도는 마찰 충격량 μ (vin + Vz') 안에서 (타원 한계면 — 같은 비로) ·
    /// 흡수 에너지 · 사실 '떨어졌다' (떨어진 높이 vin²/2g' — 5cm 넘게) · 틱 안 기록 사건 '닿음'.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void Land(Sim sim, ref PropRun r, ref Prop p, double vin, double t1)
    {
        double g = r.Gz, vout = Rest(r) * vin;
        bool stop = vout < LandStop;
        p.Z = (float)r.Zs; p.On = r.On;
        double dv = Math.Sqrt(r.Mat.Mu * T.Materials[Math.Min(r.ZMat, T.Materials.Count - 1)].Def.Mu) * (vin + (stop ? 0 : vout));
        double rw = r.Rho * p.W, s = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy + rw * rw);
        if (s > 0) { double k = Math.Max(0, 1 - dv / s); p.Vx *= k; p.Vy *= k; p.W *= k; }
        double fell = g > 0 ? vin * vin / (2 * g) : 0;
        LastLandJ = 0.5 * r.M * (vin * vin - (stop ? 0 : vout * vout));
        if (stop) { p.Vz = 0; p.State = 1; } else p.Vz = (float)vout;
        if (fell >= 0.05) sim.Facts.Emit(sim.Tick, FactKind.ItemFell, CellOf(p), (float)fell, p.Cause, -1, p.Id);
        if (Track != null) { Track.Add(MotionTrack.WhoProp, p.Id, t1, p.X, p.Y, p.Th, p.Vx, p.Vy, p.W, MotionTrack.EvLand, p.Z, p.Vz); r.LastKey = t1; }
    }

    /// <summary>마지막 닿음 (떨어져 닿음 · 넘어져 누움)이 흡수한 에너지 (J — 보기용 · 찍지 않는다: 법칙 시험 · 깨짐은 PH8).</summary>
    public double LastLandJ;

    // ───────────────────────── 넘어짐 ─────────────────────────

    /// <summary>꼴 틀 쪽 (0 +x · 1 +y · 2 −x · 3 −y)의 세상 단위 벡터 (자세 th).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static (double x, double y) SideDir(double th, int side)
    {
        double c = Math.Cos(th), s = Math.Sin(th);
        return side switch { 0 => (c, s), 1 => (-s, c), 2 => (-c, -s), _ => (s, -c) };
    }

    /// <summary>힘 (fx, fy — 세상)이 꼴을 넘기려는 쪽 · 그 쪽 몫 (꼴 틀에서 더 큰 축).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static int TipSide(double th, double fx, double fy, out double along)
    {
        double c = Math.Cos(th), s = Math.Sin(th), u = c * fx + s * fy, v = -s * fx + c * fy;
        if (Math.Abs(u) >= Math.Abs(v)) { along = Math.Abs(u); return u > 0 ? 0 : 2; }
        along = Math.Abs(v); return v > 0 ? 1 : 3;
    }

    /// <summary>모서리 축 값 (그 쪽): b · Hc · R = √(b² + Hc²) · β = atan(b/Hc) · Iₚ = I + m R² (꼴을 b × Hc 상자로 어림 — 무게중심 둘레 m R²/3).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static (double b, double hc, double R, double beta, double ip) Pivot(DotForm fm, int side, double m)
    {
        double b = fm.TipB[side], hc = fm.Hc, R = Math.Sqrt(b * b + hc * hc);
        return (b, hc, R, Math.Atan2(b, hc), 4.0 / 3 * m * R * R);
    }

    /// <summary>옆 힘이 넘길 만한가 (선 꼴 · 받침에 선 것 · 무게): 그 쪽 몫 × 힘 높이 &gt; m g' b (바람은 꼴 높이의 반 · 틀의 힘은 무게중심 높이).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static bool Tips(DotForm fm, double th, double m, double g, double wfx, double wfy, double ffx, double ffy, out int side, out double hF)
    {
        double fx = wfx + ffx, fy = wfy + ffy;
        side = TipSide(th, fx, fy, out double along);
        double w = Math.Sqrt(wfx * wfx + wfy * wfy), f = Math.Sqrt(ffx * ffx + ffy * ffy);
        hF = w + f > 0 ? (w * HeightOf(fm) / 2 + f * fm.Hc) / (w + f) : fm.Hc;
        return fm.Lie == 0 && along * hF > m * g * fm.TipB[side];
    }

    /// <summary>넘어지기 시작한다 (그 쪽 · 각속도 ω₀): 평면 빠르기는 받침 모서리가 붙든다.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static void StartTip(ref Prop p, int side, double w0)
    {
        p.State = 2; p.TiltDir = (sbyte)side; p.Tilt = 0; p.TiltW = (float)w0; p.Still = 0;
    }

    /// <summary>
    /// 넘어지는 동안 (잔걸음 h — 옆 힘의 그 쪽 몫 fa · 힘 높이 hF는 잔걸음 처음 값): RK4로 ψ · ψ' — ψ ≥ π/2면 눕는다 (마지막 토막은 일로 정확히: ½Iₚψ'² = ½Iₚψ₀'² + fa hF (1 − sin ψ₀) + m g' R (cos(β − ψ₀) − sin β)) ·
    /// ψ ≤ 0으로 돌아오면 다시 선다. 평면 빠르기 · 각속도는 받침이 붙든다 (0).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void TipStep(Sim sim, ref PropRun r, ref Prop p, double h, double t1)
    {
        p.Vx = p.Vy = p.W = 0;
        int side = p.TiltDir;
        var (b, hc, R, beta, ip) = Pivot(r.Fm, side, r.M);
        double g = r.Gz, fa = r.TipFa, hF = r.TipHf, m = r.M;
        double Acc(double psi) => (fa * hF * Math.Cos(psi) - m * g * R * Math.Sin(beta - psi)) / ip;
        double y0 = p.Tilt, v0 = p.TiltW;
        double k1y = v0, k1v = Acc(y0);
        double k2y = v0 + 0.5 * h * k1v, k2v = Acc(y0 + 0.5 * h * k1y);
        double k3y = v0 + 0.5 * h * k2v, k3v = Acc(y0 + 0.5 * h * k2y);
        double k4y = v0 + h * k3v, k4v = Acc(y0 + h * k3y);
        double y1 = y0 + h / 6 * (k1y + 2 * k2y + 2 * k3y + k4y), v1 = v0 + h / 6 * (k1v + 2 * k2v + 2 * k3v + k4v);
        if (y1 >= Math.PI / 2)
        {
            double w2 = v0 * v0 + 2 / ip * (fa * hF * (1 - Math.Sin(y0)) + m * g * R * (Math.Cos(beta - y0) - Math.Sin(beta)));
            double wl = Math.Sqrt(Math.Max(0, w2));
            LastLandJ = 0.5 * ip * wl * wl;
            LieDown(sim, ref r, ref p, side, b + hc);
            return;
        }
        if (y1 <= 0 && v1 <= 0) { p.Tilt = 0; p.TiltW = 0; p.State = 1; return; }   // 흔들려 돌아와 선다
        p.Tilt = (float)y1; p.TiltW = (float)v1;
    }

    /// <summary>눕는다: 누운 꼴 (그 쪽 — 1 +x · 2 +y · 3 −x · 4 −y)로 바꾸고 무게중심을 그 쪽으로 b + Hc 옮긴다 (막히면 제자리) · 몸 값을 다시 · 사실 '넘어졌다'.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void LieDown(Sim sim, ref PropRun r, ref Prop p, int side, double shift)
    {
        var (dx, dy) = SideDir(p.Th, side);
        p.Pose = (byte)(side + 1); p.Tilt = 0; p.TiltW = 0; p.State = 1; p.Still = 0;
        var fm = PropForm(p);
        double nx = p.X + dx * shift, ny = p.Y + dy * shift;
        HeightWindow(p, fm);
        bool fits = FitsAt(fm, nx, ny);
        FullHeight();
        if (fits) { p.X = nx; p.Y = ny; }
        p.Form = fm.Id; p.Inertia = (float)fm.I; p.Mat = BaseMatOf(fm);
        r.Fm = fm; r.Sh = fm.Shape; r.Sm = r.Sh.Hit; r.Hs = r.Sh; r.Reach = r.Sm.reach; r.In = p.Inertia; r.Rho = fm.Rho;
        Toppled++;
        sim.Facts.Emit(sim.Tick, FactKind.Toppled, CellOf(p), (float)LastLandJ, p.Cause, -1, p.Id);
    }

    // ───────────────────────── 구름 (누운 둥근 것) ─────────────────────────

    /// <summary>누운 둥근 것이 구르나 · 그 축 (세상 단위 벡터 — 누운 쪽이 축이다: 원기둥의 긴 축).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static bool Roller(in Prop p, DotForm fm, out double ax, out double ay)
    {
        ax = ay = 0;
        if (!fm.Round || p.Pose == 0) return false;
        (ax, ay) = SideDir(p.Th, p.Pose - 1);
        return true;
    }

    /// <summary>
    /// 구르는 것의 바닥 마찰 (잔걸음 h — k = μk g'): 축 쪽 빠르기 · 돎은 바닥 마찰 (타원 그대로) · 구르는 쪽은 구름 저항 μr g' / 1.5 (구르는 관성) — 0에 닿으면 멈춘다 · exact면 자리도.
    /// 구른 각 (그림의 띠)을 더한다.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static void RollFriction(ref Prop p, double h, double k, double g, double ax, double ay, double m, double I, double rho, double radius, bool exact, out double stopAt)
    {
        double u = p.Vx * ax + p.Vy * ay, q = -p.Vx * ay + p.Vy * ax;   // 축 쪽 · 구르는 쪽
        double kr = RollMu * g / RollMass, tq = q != 0 ? Math.Abs(q) / kr : double.PositiveInfinity, t = Math.Min(h, tq), sq = Math.Sign(q);
        double dq = exact ? q * t - 0.5 * sq * kr * t * t : 0, q1 = t >= tq ? 0 : q - sq * kr * h;
        var a = p; a.Vx = u * ax; a.Vy = u * ay;
        Friction(ref a, h, k, m, I, rho, exact, out stopAt);
        double au = a.Vx * ax + a.Vy * ay;
        p.Vx = au * ax - q1 * ay; p.Vy = au * ay + q1 * ax; p.W = a.W; p.Th = a.Th;
        if (exact) { p.X = a.X - ay * dq; p.Y = a.Y + ax * dq; }
        if (q1 != 0 || au != 0 || p.W != 0) stopAt = -1;
        if (radius > 0) p.Roll += (float)((exact ? dq : q * h) / radius);
    }

    // ───────────────────────── 무중력 · 공조 바람 ─────────────────────────

    /// <summary>공조 바람 (③ Airflow — 통풍구 칸 0.5m/s · 곁 칸 0.25m/s): 곁 칸은 통풍구에서 밖으로 · 통풍구 칸은 평면 바람 없음 (m/s).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void AirAt(int c, out double ux, out double uy)
    {
        ux = uy = 0;
        float a = F.Airflow[c];
        if (a <= 0) return;
        int x = L.X(c), y = L.Y(c);
        for (int k = 0; k < 4; k++)
        {
            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
            if (!L.In(nx, ny) || F.Airflow[L.Index(nx, ny)] <= a) continue;
            ux = (x - nx) * a; uy = (y - ny) * a;   // 더 센 곳 (통풍구)에서 밖으로
            return;
        }
    }

    /// <summary>공조 바람이 떠 있는 것을 끄는 힘 (½ρ|u − v|(u − v) Cd A — A는 가장 넓은 앞면 · ρ는 그 칸 기압 몫): 힘이 있으면 참.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool AirForce(in Prop p, DotForm fm, int c, out double fx, out double fy)
    {
        fx = fy = 0;
        if (c < 0 || F.Airflow[c] <= 0) return false;
        AirAt(c, out double ux, out double uy);
        double rx = ux - p.Vx, ry = uy - p.Vy, s = Math.Sqrt(rx * rx + ry * ry);
        if (s == 0) return false;
        double rho = 1.2 * Math.Clamp(F.Pressure(c) / 101.3, 0, 1.5), k = 0.5 * rho * fm.Cd * fm.AreaMax * s;
        fx = k * rx; fy = k * ry;
        return fx != 0 || fy != 0;
    }

    /// <summary>
    /// 깨우기 — 중력 (§3-3 무중력): 놓인 것마다 칸 중력이 0.01g 아래로 막 내려갔으면 (지난 틱 바닥 중력 G가 위였다) 조금 들뜨며 (위로 2 ~ 6cm/s — Id로 정한다) 떠 있음이 된다 ·
    /// 바닥 중력이 바뀌면 G만 고친다. 캐시 없이 틱마다 하나씩 본다.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool WakeByGravity(Span<Prop> all)
    {
        bool woke = false;
        for (int i = 0; i < all.Length; i++)
        {
            ref var p = ref all[i];
            if (p.State != 0) continue;
            int c = CellOf(p);
            if (c < 0) continue;
            float g = F.Grav[c];
            if (g == p.G) continue;
            bool lift = g < FloatG && p.G >= FloatG;
            p.G = g;
            if (!lift) continue;
            if (Wake(ref p, 0)) woke = true;
            p.State = 4; p.Vz = (float)(LiftV * (1 + 2 * (DotHash(p.Id, 71) % 1000) / 1000.0));
        }
        return woke;
    }
}
