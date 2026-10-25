namespace ShipEngine;

/// <summary>
/// ④ 물체가 받는 바람 · 구멍 (docs/PHYSICS.md §3-2 · §3-6 — PH4c): 감압 돌풍 (③ 장이 틱마다 쓰는 칸마다 동압 · 쪽 · 줄어드는 빠르기 · 처음 밀도) ·
/// 구멍 입 (③ <see cref="Field.HoleMouthPa"/>)이 물체가 덮은 칸마다 따로 민다 — F_c = ½ρ|u − v|(u − v) Cd A(φ) × 그 칸 도트 몫 · 힘 점 = 그 칸 도트의 가운데 + 압력 중심.
/// 그래서 고른 바람에 대칭 꼴은 돌지 않고 · 칸마다 바람이 다르면 (구멍 입 곁) 대칭 상자도 돌고 · 압력 중심이 어긋난 꼴은 고른 바람에도 돈다.
/// 놓인 것은 정지 타원 (μs = 1.25 μk)을 넘을 때만 깬다. 무게보다 세게 빨아들이는 구멍 벽에 닿으면 가장 작은 앞면이 구멍 × 1.2보다 크면 빨려 붙어 막고 (장의 PlugProp — 다음 틱 흐름부터) ·
/// 아니면 우주로 이어진 구멍일 때 구멍 밖 우주로 (장부 '우주로' — 비행은 PH11 · 빨아들이지 않는 구멍과 옆 방으로 이어진 구멍은 보통 벽). 막은 것은 구멍을 덧대거나 누르는 힘이 무게 아래가 되면 떨어진다.
/// 모두 찍힌 상태 + 같은 틱 장 값으로만 정한다 (캐시는 틱마다 비운다 — 되살린 세계와 같다).
/// </summary>
public sealed partial class Structure
{
    public const double PlugMargin = 1.2, PlugSeal = 0.9, StaticShare = 1.25, ShareTurn = 0.098;   // 막는 앞면 여유 · 막은 넓이 몫 · 정지 마찰 = 1.25 μk · 칸 몫을 다시 세는 돎 (5.6°)
    private const int MaxWindCells = 9, MouthSlots = 256;

    private bool _windy;                                   // 이번 틱 바람이 분 칸이 있다 (③ WindCells)
    private double _tickDt = Time.SecondsPerTick;
    // 구멍 입 틱 안 캐시 (찍지 않는다): 칸 번호 아래 8비트 자리마다 그 칸 · 동압 · 그 구멍 · 센 틱 — 온 배 크기 배열을 감압 첫 틱에 만들지 않는다 (물체 예산 고침 — 새뜰호 150KB) ·
    // 두 칸이 한 자리에 겹치면 다시 센다 (틱 안에서는 장이 바뀌지 않으니 — 물체 막음은 틱 끝에 쓴다 — 값이 같다)
    private readonly int[] _mouthKey = new int[MouthSlots];
    private readonly float[] _mouthQ = new float[MouthSlots];
    private readonly int[] _mouthAt = new int[MouthSlots];
    private readonly long[] _mouthTick = NewTicks(MouthSlots);
    private static long[] NewTicks(int n) { var a = new long[n]; Array.Fill(a, long.MinValue); return a; }
    private readonly List<int> _plugCells = new();         // PlugProp > 0인 칸 (되살리면 장의 배열에서 다시 세운다)
    private bool _plugStale = true;
    private readonly int[] _wc = new int[MaxWindCells];
    private readonly int[] _wn = new int[MaxWindCells];
    private readonly double[] _wsx = new double[MaxWindCells], _wsy = new double[MaxWindCells], _wrho = new double[MaxWindCells], _wux = new double[MaxWindCells], _wuy = new double[MaxWindCells];
    /// <summary>셈 (찍지 않는다 — 보기용): 바람에 깬 것 · 우주로 · 막음.</summary>
    public long PropWoken, PropSpaced, PropPlugged;

    /// <summary>꼴 발자국 표본 (무게중심에서 — 기둥 가운데 · 꼴 가운데에서 대칭으로 2도트 걸러) — 꼴에 한 번 세어 둔다 (<see cref="PropShape"/>).</summary>
    private static (double[] x, double[] y) FootOf(DotForm fm) => (fm.Shape.FootX, fm.Shape.FootY);

    private double _minMu = -1;
    /// <summary>재질 표에서 가장 미끄러운 마찰 (μ > 0 가운데 가장 작은 것 — 깨우기를 넉넉히 거를 때).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private double MinMu()
    {
        if (_minMu < 0) { _minMu = double.MaxValue; foreach (var m in T.Materials) if (m.Def.Mu > 0) _minMu = Math.Min(_minMu, m.Def.Mu); if (_minMu == double.MaxValue) _minMu = 0; }
        return _minMu;
    }

    /// <summary>가장 작은 앞면 (m² — 16갈래 바람 그림자 가운데 가장 작은 것: 구멍을 막을지 지날지).</summary>
    private static double MinFace(DotForm fm) => fm.AreaMin;

    /// <summary>
    /// 그 칸의 구멍 입 동압 · 그 구멍 칸 — 틱 처음에서 tau초 (장은 이미 흘렀으니 그 구역이 지수로 빈 빠르기 WindK로 기압을 되짚는다: p(τ) = p끝 · e^(k(Δt − τ))).
    /// 틱 처음 값은 칸마다 한 번만 센다 (틱 안 캐시 — 틱 안에서는 장이 바뀌지 않는다).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private double MouthAt(int c, double tau, out int mouth)
    {
        if (tau > 0) return F.HoleMouthPa(c, out mouth, Math.Exp(F.WindK[c] * (_tickDt - tau)));
        int k = c & (MouthSlots - 1);
        if (_mouthTick[k] != _now || _mouthKey[k] != c) { _mouthQ[k] = (float)F.HoleMouthPa(c, out _mouthAt[k], Math.Exp(F.WindK[c] * _tickDt)); _mouthKey[k] = c; _mouthTick[k] = _now; }
        mouth = _mouthAt[k];
        return _mouthQ[k];
    }

    /// <summary>그 칸 바람의 원인 (그 구역이 우주로 새는 틈을 낸 원인).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int WindCauseAt(int c)
    {
        int r = c >= 0 && c < F.RegionOf.Length ? F.RegionOf[c] : -1;
        if (r < 0) return 0;
        foreach (var k in F.Links) if (k.RA == r && k.RB < 0 && k.Via >= 0 && DamageCause[k.Via] != 0) return DamageCause[k.Via];
        return 0;
    }

    /// <summary>
    /// 물체가 받는 바람 (틱 처음에서 tau초 — 동압이 e^(−k tau)로 준다): 덮은 칸마다 발자국 표본 몫 · 그 칸 가운데 (돌풍 · 구멍 입 가운데 센 것) · 상대 빠르기.
    /// 힘 (N) · 무게중심 둘레 돌림힘 (N·m) — 바람이 없으면 거짓.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool WindForce(in Prop p, DotForm fm, double tau, out double fx, out double fy, out double tq)
    {
        fx = fy = tq = 0;
        var (xs, ys) = FootOf(fm);
        double cs = Math.Cos(p.Th), sn = Math.Sin(p.Th);
        int n = 0, total = 0;
        double ax = 0, ay = 0;
        for (int k = 0; k < xs.Length; k++)
        {
            double x = p.X + cs * xs[k] - sn * ys[k], y = p.Y + sn * xs[k] + cs * ys[k];
            int c = x >= 0 && y >= 0 && x < L.W && y < L.H ? L.Index((int)x, (int)y) : -1;
            if (c < 0) continue;
            int g = 0;
            while (g < n && _wc[g] != c) g++;
            if (g == n) { if (n == MaxWindCells) continue; _wc[n] = c; _wn[n] = 0; _wsx[n] = 0; _wsy[n] = 0; n++; }
            _wn[g]++; _wsx[g] += x; _wsy[g] += y; total++;
            ax += x; ay += y;
        }
        if (total == 0) return false;
        ax /= total; ay /= total;
        // 칸마다 바람 (돌풍 · 구멍 입 가운데 센 것): 동압 · 밀도 · 바람 빠르기
        bool same = true;
        for (int g = 0; g < n; g++)
        {
            int c = _wc[g];
            double q = F.WindPa[c], rho = 0, ux = 0, uy = 0;
            if (q > 0 && F.WindRho[c] > 0)
            {
                double decay = Math.Exp(-F.WindK[c] * tau), wl = Math.Sqrt(F.WindX[c] * (double)F.WindX[c] + F.WindY[c] * (double)F.WindY[c]);
                if (wl > 0) { double u = Math.Sqrt(2 * q / F.WindRho[c]); rho = F.WindRho[c] * decay; ux = u * F.WindX[c] / wl; uy = u * F.WindY[c] / wl; }
            }
            double gx = _wsx[g] / _wn[g], gy = _wsy[g] / _wn[g];
            double qm = MouthAt(c, tau, out int mouth);
            if (qm > (rho > 0 ? q * Math.Exp(-F.WindK[c] * tau) : 0) && mouth >= 0)
            {
                // 구멍 입: 그 칸 도트의 가운데에서 구멍 칸 가운데 쪽으로 (반구로 모이는 흐름 · 그때 밀도)
                double dx = L.X(mouth) + 0.5 - gx, dy = L.Y(mouth) + 0.5 - gy, dl = Math.Sqrt(dx * dx + dy * dy);
                double rc = F.Pressure(c) * Math.Exp(F.WindK[c] * (_tickDt - tau)) * 1000 * Field.AirMolar / (Field.R * F.Temp(c));
                if (dl > 0 && rc > 0) { double u = Math.Sqrt(2 * qm / rc); rho = rc; ux = u * dx / dl; uy = u * dy / dl; }
            }
            _wrho[g] = rho; _wux[g] = ux; _wuy[g] = uy;
            same &= rho == _wrho[0] && ux == _wux[0] && uy == _wuy[0];
        }
        if (same && n > 1) { n = 1; _wn[0] = total; _wsx[0] = ax * total; _wsy[0] = ay * total; }   // 덮은 칸이 모두 같은 바람이면 한 몫 (고른 바람에 대칭 꼴은 꼭 돌지 않는다)
        bool any = false;
        for (int g = 0; g < n; g++)
        {
            double rho = _wrho[g], ux = _wux[g], uy = _wuy[g];
            if (rho <= 0) continue;
            double rx = n == 1 ? 0 : _wsx[g] / _wn[g] - ax, ry = n == 1 ? 0 : _wsy[g] / _wn[g] - ay;   // 그 칸 몫의 가운데 (발자국 가운데에서) — 바람에 수직인 몫은 압력 중심이 더한다
            double wx = ux - (p.Vx - p.W * ry), wy = uy - (p.Vy + p.W * rx), wl2 = wx * wx + wy * wy;
            if (wl2 <= 0) continue;
            double wl1 = Math.Sqrt(wl2), phi = Math.Atan2(wy, wx) - p.Th;
            int a16 = (int)Math.Round(phi * 8 / Math.PI) % 16;
            if (a16 < 0) a16 += 16;
            double area = fm.Area16[a16], cop = fm.Cop16[a16], ang = a16 * Math.PI / 8;
            double bx = -Math.Sin(ang), by = Math.Cos(ang);   // 바람에 수직인 몸 축 u (압력 중심이 어긋난 쪽)
            rx += cop * (cs * bx - sn * by); ry += cop * (sn * bx + cs * by);
            double k2 = 0.5 * rho * wl1 * fm.Cd * area * _wn[g] / total;
            double f1 = k2 * wx, f2 = k2 * wy;
            fx += f1; fy += f2; tq += rx * f2 - ry * f1;
            any = true;
        }
        return any;
    }

    /// <summary>힘 · 돌림힘이 정지 타원 안인가 ((|F|/μsN)² + (|τ|/(μsNρ))² ≤ 1 — 무중력이면 어떤 힘이든 밖).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static bool Holds(double fx, double fy, double tq, double muS, double nForce, double rho)
    {
        double lim = muS * nForce;
        if (lim <= 0) return fx == 0 && fy == 0 && tq == 0;
        double a = (fx * fx + fy * fy) / (lim * lim), b = rho > 0 ? tq * tq / (lim * rho * lim * rho) : 0;
        return a + b <= 1;
    }

    /// <summary>깨우기의 바람 · 구멍 입 갈래: 놓인 것마다 둘레 칸에 바람이 있으면 그 힘이 정지 타원을 넘는지 본다 (넘으면 깨어 미끄러진다 — 원인은 그 구역이 새는 틈).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool WakeByWind(Span<Prop> all)
    {
        bool woke = false;
        for (int i = 0; i < all.Length; i++)
        {
            ref var p = ref all[i];
            if (p.State != 0) continue;
            int c = CellOf(p);
            if (c < 0) continue;
            double qMax = 0;
            int x0 = L.X(c), y0 = L.Y(c);
            for (int yy = y0 - 1; yy <= y0 + 1; yy++)
                for (int xx = x0 - 1; xx <= x0 + 1; xx++)
                {
                    if (!L.In(xx, yy)) continue;
                    int j = L.Index(xx, yy);
                    qMax = Math.Max(qMax, Math.Max(F.WindPa[j], MouthAt(j, 0, out _)));
                }
            if (qMax <= 0) continue;
            var fm = PropForm(p);
            // 넉넉한 어림으로 먼저 거른다: 가장 센 칸 바람 × 가장 넓은 앞면 (돌림힘 몫까지)이 가장 미끄러운 바닥의 정지 마찰도 못 넘으면 그대로
            double aMax = fm.AreaMax;
            double rg = 3 * SamplesOf(fm).reach / Math.Max(1e-3, fm.Rho);   // 힘 점은 무게중심에서 많아야 (몫의 가운데 차 2 + 압력 중심 1) × 닿는 반지름
            if (qMax * fm.Cd * aMax * Math.Sqrt(1 + rg * rg) <= StaticShare * MinMu() * WetShare * fm.Mass * Gravity * F.Grav[c]) continue;
            var q = p;
            if (q.Kg <= 0) FillBody(ref q);
            if (!WindForce(q, fm, 0, out double fx, out double fy, out double tq)) continue;
            double gp = Gravity * F.Grav[c], mu = T.Materials[Math.Min(q.Mat, T.Materials.Count - 1)].Def.Mu * (F.Stored(T.sWater, c) > WetMol ? WetShare : 1);
            if (Holds(fx, fy, tq, StaticShare * mu, q.Kg * gp, fm.Rho)) continue;
            q.State = 1; q.Asleep = 0; q.Still = 0;
            int cause = WindCauseAt(c);
            if (cause != 0) q.Cause = cause;
            p = q; woke = true; PropWoken++;
        }
        return woke;
    }

    /// <summary>
    /// 구멍 벽 칸에 닿았다 (또는 무게중심이 구멍 칸 · 바깥에 들었다 — hole = −1이면 이미 배 밖이다): 그 구멍이 무게보다 세게 빨아들일 때만 (붙음 · 떨어짐과 같은 문턱) —
    /// 가장 작은 앞면이 구멍 × 1.2보다 크면 빨려 붙어 막고 (State 6) · 아니면 구멍이 우주로 이어질 때만 구멍 밖 우주로 (State 5 — 틱 끝에 장부 '우주로').
    /// 빨아들이지 않는 구멍 (기압이 같은 두 방 사이 · 공기가 다 빠진 방)과 옆 방으로 이어진 구멍은 보통 벽이다 (거짓 — 부른 쪽이 튀거나 틈을 지나게 둔다).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool HoleHit(Sim sim, ref Prop p, int hole)
    {
        var fm = PropForm(p);
        string name = PropName(p);
        if (hole >= 0)
        {
            double face = MinFace(fm);
            if (!Sucked(p, hole, face)) return false;
            if (face > F.Hole[hole] * PlugMargin)
            {
                p.State = 6; p.PlugAt = hole + 1; p.Vx = p.Vy = p.W = 0; p.Still = 0; p.Since = _now;
                int pc = sim.Causes.Add(CauseKind.Pressure, $"{Ko.I(name)} 구멍에 빨려 붙었다 — 새는 것을 거의 막았다", sim.Tick, hole, p.Cause);
                sim.Facts.Emit(sim.Tick, FactKind.ObjectFlung, CellOf(p), p.Kg, pc, other: p.What);
                PropPlugged++;
                return true;
            }
            if (!HoleToSpace(hole)) return false;
        }
        p.State = 5;
        int sc = sim.Causes.Add(CauseKind.Pressure, $"{Ko.I(name)} 구멍 밖 우주로 빨려 나갔다", sim.Tick, hole, p.Cause);
        sim.Facts.Emit(sim.Tick, FactKind.ObjectFlung, hole >= 0 ? hole : Math.Max(0, CellOf(p)), p.Kg, sc, other: p.What);
        return true;
    }

    /// <summary>그 구멍이 우주로 이어지나 (③ 틈 가운데 그 칸을 지나 바깥으로 가는 것이 있다 — 안쪽 벽 구멍은 옆 방으로 이어질 뿐이다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool HoleToSpace(int hole)
    {
        foreach (var k in F.Links) if (k.Via == hole && k.RB < 0) return true;
        return false;
    }

    /// <summary>구멍이 그 물체를 무게보다 세게 빨아들이나 (누르는 힘 = 기압 차 × 덮은 넓이 > 무게 — 빨려 붙음 · 떨어짐 · 우주로의 같은 문턱).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool Sucked(in Prop p, int hole, double face)
    {
        if (hole < 0 || L.Kind[hole] != CellKind.Wall || F.Hole[hole] <= 0.01f || F.Severed[hole] != 0) return false;
        int c = HoleSide(p, hole);
        double weight = p.Kg * Gravity * (c >= 0 ? F.Grav[c] : 0);
        return Suction(c, hole, face) > weight;
    }

    /// <summary>구멍 이쪽의 트인 칸: 물체 무게중심 칸 (트였으면) · 아니면 (무게중심이 구멍 칸에 들었다) 구멍 곁 트인 칸 가운데 무게중심에 가장 가까운 것.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int HoleSide(in Prop p, int hole)
    {
        int c = CellOf(p);
        if (c >= 0 && c != hole && F.Passable(c)) return c;
        int best = -1;
        double bd = double.MaxValue;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(hole, nb);
        foreach (int j in nb)
        {
            if (j < 0 || F.IsOut(j) || !F.Passable(j)) continue;
            double dx = L.X(j) + 0.5 - p.X, dy = L.Y(j) + 0.5 - p.Y, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = j; }
        }
        return best;
    }

    /// <summary>우주로 간 것을 목록에서 뺀다 (장부 '우주로' · 가져간 운동량 · 각운동량 — Id 차례 그대로 · 몸이 있으면 세상 틀 몫은 반작용에서 이미 셌다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void RemoveSpaced(Sim sim, bool world)
    {
        bool any = false;
        foreach (var p in Props)
        {
            if (p.State != 5) continue;
            any = true;
            if (!world) { PropPxGone += p.Kg * p.Vx; PropPyGone += p.Kg * p.Vy; PropLGone += p.Kg * (p.X * p.Vy - p.Y * p.Vx) + p.Inertia * p.W; }
            if (p.Kind == 0) sim.Book.AddItem(p.What, Flow.Spaced, -1);
            PropSpaced++;
            Track?.Gone.Add(p);   // 그리는 쪽이 나간 때까지 그린다
        }
        if (any) Props.RemoveAll(p => p.State == 5);
    }

    /// <summary>구멍에 빨려 붙은 것: 구멍을 덧대거나 (끊겼거나) 누르는 힘 (기압 차 × 덮은 넓이)이 무게 아래가 되면 떨어진다 (깨어 — 남은 바람에 다시 밀릴 수 있다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool Unplug(Span<Prop> all)
    {
        bool woke = false;
        for (int i = 0; i < all.Length; i++)
        {
            ref var p = ref all[i];
            if (p.State != 6 || p.PlugAt == 0) continue;
            int h = p.PlugAt - 1;
            if (p.Kg <= 0) FillBody(ref p);
            if (Sucked(p, h, MinFace(PropForm(p)))) continue;
            p.PlugAt = 0; p.State = 1; p.Asleep = 0; p.Still = 0; p.Vx = p.Vy = p.W = 0;
            woke = true;
        }
        return woke;
    }

    /// <summary>구멍에 붙은 것을 누르는 힘 (N): 그 칸과 구멍 너머의 기압 차 × 덮은 넓이.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private double Suction(int cell, int hole, double face)
    {
        if (cell < 0 || hole < 0) return 0;
        int bx = 2 * L.X(hole) - L.X(cell), by = 2 * L.Y(hole) - L.Y(cell);
        double pOut = L.In(bx, by) && !F.IsOut(L.Index(bx, by)) && F.Passable(L.Index(bx, by)) ? F.Pressure(L.Index(bx, by)) : F.Outside.KPa;
        return Math.Max(0, F.Pressure(cell) - pOut) * 1000 * Math.Min(F.Hole[hole], face);
    }

    /// <summary>
    /// 물체 막음을 장에 쓴다 (틱 끝 — 장은 다음 틱 흐름부터 줄인다): 지난번에 쓴 칸을 0으로 돌리고 막은 것마다 min(구멍, 가장 작은 앞면) × 0.9 (칸마다 가장 큰 것).
    /// 쓴 칸 목록은 찍지 않는다 — 되살리면 장의 PlugProp > 0인 칸에서 다시 세운다 (풀린 막음이 남지 않게).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void WritePlugs(ReadOnlySpan<Prop> all, bool plugged)
    {
        if (_plugStale)
        {
            _plugCells.Clear();
            for (int c = 0; c < F.PlugProp.Length; c++) if (F.PlugProp[c] > 0) _plugCells.Add(c);
            _plugStale = false;
        }
        if (!plugged && _plugCells.Count == 0) return;
        foreach (int c in _plugCells) F.SetPlugProp(c, 0);
        _plugCells.Clear();
        if (!plugged) return;
        foreach (var p in all)
        {
            if (p.State != 6 || p.PlugAt == 0) continue;
            int h = p.PlugAt - 1;
            float area = (float)(Math.Min(F.Hole[h], MinFace(PropForm(p))) * PlugSeal);
            if (F.PlugProp[h] == 0) _plugCells.Add(h);
            if (area > F.PlugProp[h]) F.SetPlugProp(h, area);
        }
    }
}
