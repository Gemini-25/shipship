namespace ShipEngine;

public sealed partial class Field
{
    public const double Cd = 0.6;        // 오리피스 유량 계수
    public const int FlowSubsteps = 4;
    /// <summary>바람을 계산할 만큼 센 흐름 (몰/초) — 틈 곁 칸에서 약 0.2m/s. 문틈으로 조금 새는 것은 바람이 아니다.</summary>
    public const double WindMinFlow = 20;
    /// <summary>기체를 단열 시험할 때 (외판 복사 · 햇빛 끔) — 법칙 시험.</summary>
    public bool Adiabatic { get; set; }

    // 구역 합계 (틈 흐름 동안 이어서 고친다)
    private double[] _rn = Array.Empty<double>(), _rU = Array.Empty<double>(), _rC = Array.Empty<double>(), _rM = Array.Empty<double>();
    private double[] _parcel = Array.Empty<double>();
    private int[] _distBuf = Array.Empty<int>();

    /// <summary>구역 평균 압력 (Pa).</summary>
    private double RegionP(int r)
    {
        int cnt = Regions[r].Length;
        if (_rC[r] <= 1e-12 || cnt == 0) return 0;
        double t = _rU[r] / _rC[r];
        return _rn[r] * R * t / (cnt * CellVolume);
    }

    private double RegionT(int r) => _rC[r] > 1e-12 ? _rU[r] / _rC[r] : 0;

    /// <summary>
    /// 구역의 기압 (kPa) — 이번 틱 틈 흐름을 셀 때의 구역 전체 값. 틈으로 나가는 기체는 틈 곁 칸에서 먼저 덜어 내므로 그 칸 하나의 기압은 잠깐 2 ~ 3kPa 튄다 —
    /// 틈의 소리(③ 소리 · 쉬익)는 칸이 아니라 이 값으로 잰다.
    /// </summary>
    public double ZoneKPa(int region) => region >= 0 && region < Regions.Count && region < _rn.Length ? RegionP(region) / 1000 : 0;

    /// <summary>
    /// 구역마다 몰 · 현열 · 열용량 · 질량의 합. 합은 칸 차례 · 성분 차례 그대로 하나씩 이어 더한다 (비트까지 같아야 한다 — 차례를 바꾸면 반올림이 달라진다).
    /// 이어 더하기는 앞 덧셈을 기다리므로 구역들을 두 줄로 나눠(<see cref="SplitLanes"/>) 한 고리에서 칸 하나씩 나란히 센다 — 두 줄의 합은 서로 기다리지 않아 겹쳐 돈다 (구역마다 더하는 차례는 그대로).
    /// </summary>
    private void RegionTotals()
    {
        int n = Regions.Count;
        if (_rn.Length < n) { _rn = new double[n]; _rU = new double[n]; _rC = new double[n]; _rM = new double[n]; }
        if (_ids.Length < n) { _ids = new int[n]; _laneA = new int[n]; _laneB = new int[n]; }
        for (int r = 0; r < n; r++) _ids[r] = r;
        SplitLanes(_ids.AsSpan(0, n), _laneA, _laneB, out int na, out int nb);
        int qa = 0, qb = 0, ka = 0, kb = 0, ra = -1, rb = -1;
        double an = 0, au = 0, ac = 0, am = 0, bn = 0, bu = 0, bc = 0, bm = 0;
        while (true)
        {
            if (ra < 0) { if (qa >= na) break; ra = _laneA[qa++]; ka = 0; an = au = ac = am = 0; }
            if (rb < 0) { if (qb >= nb) break; rb = _laneB[qb++]; kb = 0; bn = bu = bc = bm = 0; }
            int[] ca = Regions[ra], cb = Regions[rb];
            int m = Math.Min(ca.Length - ka, cb.Length - kb);
            ReadOnlySpan<int> xa = ca.AsSpan(ka, m), xb = cb.AsSpan(kb, m);
            ReadOnlySpan<double> cv = _cv, mo = _molar;
            for (int t = 0; t < xa.Length; t++)
            {
                int i = xa[t], j = xb[t];
                au += U[i]; bu += U[j];
                var ri = N.AsSpan(i * G, G); var rj = N.AsSpan(j * G, G);
                for (int g = 0; g < ri.Length; g++)
                {
                    double v = ri[g], w = rj[g];
                    an += v; ac += v * cv[g]; am += v * mo[g];
                    bn += w; bc += w * cv[g]; bm += w * mo[g];
                }
            }
            ka += m; kb += m;
            if (ka == ca.Length) { _rn[ra] = an; _rU[ra] = au; _rC[ra] = ac; _rM[ra] = am; ra = -1; }
            if (kb == cb.Length) { _rn[rb] = bn; _rU[rb] = bu; _rC[rb] = bc; _rM[rb] = bm; rb = -1; }
        }
        // 한 줄이 먼저 끝났다 — 남은 줄은 하나씩
        if (ra >= 0) TotalsFrom(ra, ka, an, au, ac, am);
        while (qa < na) TotalsFrom(_laneA[qa++], 0, 0, 0, 0, 0);
        if (rb >= 0) TotalsFrom(rb, kb, bn, bu, bc, bm);
        while (qb < nb) TotalsFrom(_laneB[qb++], 0, 0, 0, 0, 0);
    }
    private int[] _ids = Array.Empty<int>(), _laneA = Array.Empty<int>(), _laneB = Array.Empty<int>();

    /// <summary>구역 r의 합을 칸 from부터 이어서 (앞에서 센 합에서) 끝까지.</summary>
    private void TotalsFrom(int r, int from, double sn, double su, double sc, double sm)
    {
        var cells = Regions[r];
        ReadOnlySpan<double> cv = _cv, mo = _molar;
        for (int k = from; k < cells.Length; k++)
        {
            int i = cells[k];
            su += U[i];
            var row = N.AsSpan(i * G, G);
            for (int g = 0; g < row.Length; g++) { double v = row[g]; sn += v; sc += v * cv[g]; sm += v * mo[g]; }
        }
        _rn[r] = sn; _rU[r] = su; _rC[r] = sc; _rM[r] = sm;
    }

    /// <summary>구역들을 두 줄로: 앞에서부터 칸이 덜 찬 줄에 (같으면 첫 줄). 줄을 어떻게 나누든 구역마다 셈은 같다 — 빠르기만 바뀐다.</summary>
    private void SplitLanes(ReadOnlySpan<int> ids, Span<int> la, Span<int> lb, out int na, out int nb)
    {
        na = 0; nb = 0; long fa = 0, fb = 0;
        foreach (int r in ids)
        {
            if (fa <= fb) { la[na++] = r; fa += Regions[r].Length; }
            else { lb[nb++] = r; fb += Regions[r].Length; }
        }
    }

    /// <summary>
    /// 오리피스 흐름 (몰/초): 위쪽 압력 pHi · 아래쪽 pLo (Pa) · 위쪽 온도 · 평균 몰 질량 · 비열비 γ.
    /// 압력비가 임계(γ=1.4에서 0.528) 아래면 막힘 흐름 (음속).
    /// </summary>
    public static double Orifice(double area, double pHi, double pLo, double temp, double molar, double gamma)
    {
        if (pHi <= pLo || pHi <= 0 || temp <= 0 || molar <= 0) return 0;
        double r = pLo / pHi;
        double crit = Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
        double k = Math.Sqrt(gamma / (molar * R * temp));
        if (r <= crit)
            return Cd * area * pHi * k * Math.Pow(2 / (gamma + 1), (gamma + 1) / (2 * (gamma - 1)));
        double sub = Math.Sqrt(2 / (gamma - 1) * (Math.Pow(r, 2 / gamma) - Math.Pow(r, (gamma + 1) / gamma)));
        return Cd * area * pHi * k * sub;
    }

    private void FlowLinks(double dt)
    {
        if (Links.Count == 0) return;
        RegionTotals();
        if (_parcel.Length != G) _parcel = new double[G];
        double h = dt / FlowSubsteps;
        // 지난 틱에 크게 흐른 틈만 잘게 나눠 (틈끼리 서로 영향을 주는 빠른 흐름) — 나머지는 한 번에 (정확한 지수식이라 넘치지 않는다)
        foreach (var k in Links)
        {
            double nMin = k.RB < 0 ? _rn[k.RA] : Math.Min(_rn[k.RA], _rn[k.RB]);
            k.Fast = Math.Abs(k.Flow) * dt > 0.02 * Math.Max(1e-9, nMin);
            k.Flow = 0; k.Impulse = 0;
        }
        foreach (var k in Links)
        {
            if (k.Fast) continue;
            double moved = k.RB < 0 ? FlowExterior(k, dt) : FlowInternal(k, dt);
            k.Flow += moved / dt;
        }
        for (int sub = 0; sub < FlowSubsteps; sub++)
            foreach (var k in Links)
            {
                if (!k.Fast) continue;
                double moved = k.RB < 0 ? FlowExterior(k, h) : FlowInternal(k, h);
                k.Flow += moved / dt;
            }
        SplitImpulse();
        long t = Prof != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        foreach (var k in Links)
            if (Math.Abs(k.Flow) > WindMinFlow) Wind(k, dt);
        Lap("바람", t);
    }

    /// <summary>구역 사이 틈: 두 통이 고르게 되는 정확한 지수식 (선형화한 컨덕턴스로).</summary>
    private double FlowInternal(Link k, double h)
    {
        double pa = RegionP(k.RA), pb = RegionP(k.RB);
        if (Math.Abs(pa - pb) < 1e-3) return 0;
        bool aHigh = pa > pb;
        int hi = aHigh ? k.RA : k.RB, lo = aHigh ? k.RB : k.RA;
        double pHi = Math.Max(pa, pb), pLo = Math.Min(pa, pb);
        double tHi = RegionT(hi);
        double molar = _rn[hi] > 0 ? _rM[hi] / _rn[hi] : 0.029;
        double gamma = _rC[hi] > 0 ? 1 + _rn[hi] * R / _rC[hi] : 1.4;
        double ndot = Orifice(k.Area, pHi, pLo, tHi, molar, gamma);
        if (ndot <= 0) return 0;
        double G_ = ndot / (pHi - pLo);
        // dn/dP: 위쪽은 n/P, 아래쪽은 V/(R T) (진공이어도 정의된다 — 들어온 기체의 온도로)
        double cHi = _rn[hi] / pHi;
        double tLo = _rC[lo] > 1e-9 ? RegionT(lo) : tHi;
        double cLo = Regions[lo].Length * CellVolume / (R * Math.Max(1, tLo));
        double inv = 1 / cHi + 1 / cLo;
        double dn = (pHi - pLo) / inv * (1 - Math.Exp(-G_ * h * inv));
        dn = Math.Min(dn, _rn[hi] * 0.999);
        if (dn <= 0) return 0;
        int fromCell = aHigh ? k.A : k.B;
        var (nearFrom, wFrom) = aHigh ? (k.NearA, k.WA) : (k.NearB, k.WB);
        var (nearTo, wTo) = aHigh ? (k.NearB, k.WB) : (k.NearA, k.WA);
        double pu = Take(hi, nearFrom, wFrom, dn);
        Deposit(lo, nearTo, wTo, pu);
        return aHigh ? dn : -dn;
    }

    /// <summary>바깥과의 틈: 진공이면 정확한 지수 감소, 행성 대기면 무한히 큰 통과 고르기 + 섞임(확산).</summary>
    private double FlowExterior(Link k, double h)
    {
        if (!Outside.Vacuum && Outside.Frac.Length == G) ExteriorMix(k, h);
        int r = k.RA;
        double p = RegionP(r), pe = Outside.KPa * 1000;
        double t = RegionT(r);
        if (p > pe + 1e-3 && _rn[r] > 1e-9)
        {
            double molar = _rM[r] / _rn[r];
            double gamma = _rC[r] > 0 ? 1 + _rn[r] * R / _rC[r] : 1.4;
            double ndot = Orifice(k.Area, p, pe, t, molar, gamma);
            double dn;
            if (pe <= 0)
                dn = _rn[r] * (1 - Math.Exp(-ndot / _rn[r] * h)); // 막힘 흐름은 압력에 비례 → 정확한 지수
            else
            {
                double G_ = ndot / (p - pe), c = _rn[r] / p;
                dn = (p - pe) * c * (1 - Math.Exp(-G_ * h / c));
            }
            dn = Math.Min(dn, _rn[r]);
            if (dn <= 0) return 0;
            k.Impulse += dn * molar * ExitSpeed(p, pe, t, molar, gamma);
            Take(r, k.NearA, k.WA, dn);
            double vented = 0;
            for (int g = 0; g < G; g++) vented += _parcel[g];
            VentedTotal += vented;
            return dn;
        }
        if (pe > p + 1e-3 && Outside.Frac.Length == G)
        {
            // 바깥(행성 대기)에서 들어온다
            double te = Outside.Temp;
            double molar = 0, cv = 0;
            for (int g = 0; g < G; g++) { molar += Outside.Frac[g] * _molar[g]; cv += Outside.Frac[g] * _cv[g]; }
            double gamma = 1 + R / Math.Max(1e-9, cv);
            double ndot = Orifice(k.Area, pe, p, te, molar, gamma);
            double cIn = Regions[r].Length * CellVolume / (R * Math.Max(1, _rC[r] > 1e-9 ? t : te));
            double dn = (pe - p) * cIn * (1 - Math.Exp(-ndot / (pe - p) * h / cIn));
            if (dn <= 0) return 0;
            for (int g = 0; g < G; g++) _parcel[g] = Outside.Frac[g] * dn;
            Deposit(r, k.NearA, k.WA, dn * (cv + R) * te); // 들어오는 기체는 엔탈피를 가져온다
            return -dn;
        }
        return 0;
    }

    private double[] _impR = Array.Empty<double>(), _areaR = Array.Empty<double>();

    /// <summary>
    /// 한 구역의 우주로 트인 틈들은 같은 기압을 본다 — 틈을 차례로 셈하며 앞의 틈이 먼저 빼 간 몫(셈 순서의 흔적)을 지우려고
    /// 구역의 운동량 합을 틈 넓이대로 다시 나눈다 (합은 그대로 · 대칭인 틈은 똑같이 민다).
    /// </summary>
    private void SplitImpulse()
    {
        int nr = Regions.Count;
        bool any = false;
        foreach (var k in Links) if (k.RB < 0 && k.Impulse > 0) { any = true; break; }
        if (!any) return;
        if (_impR.Length < nr) { _impR = new double[nr]; _areaR = new double[nr]; }
        Array.Clear(_impR, 0, nr); Array.Clear(_areaR, 0, nr);
        foreach (var k in Links) if (k.RB < 0 && k.RA >= 0) { _impR[k.RA] += k.Impulse; _areaR[k.RA] += k.Area; }
        foreach (var k in Links) if (k.RB < 0 && k.RA >= 0 && _areaR[k.RA] > 0) k.Impulse = _impR[k.RA] * k.Area / _areaR[k.RA];
    }

    /// <summary>
    /// 틈으로 빠지는 기체의 실효 분출 속도 (m/s — 운동량 ÷ 질량): 막힘 흐름이면 목의 운동량 흐름 ṁa* + p*A* = ṁa*(1 + 1/γ) (a* = √(2γRT/((γ+1)M)) — 공기 20℃ 537m/s ·
    /// 노즐 추력 계수 1.27과 같다)에서 바깥 압력이 미는 몫을 뺀다 · 막히지 않으면 등엔트로피 분출 √(2c_pT(1 − (p_e/p)^((γ−1)/γ))).
    /// </summary>
    public static double ExitSpeed(double p, double pe, double t, double molar, double gamma)
    {
        if (p <= pe || molar <= 0) return 0;
        double r = pe / p, crit = Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
        if (r <= crit)
        {
            double aStar = Math.Sqrt(2 * gamma * R * t / ((gamma + 1) * molar));
            return Math.Max(0, aStar * (1 + 1 / gamma) - aStar / gamma * r / crit);
        }
        double cp = gamma / (gamma - 1) * R / molar;
        return Math.Sqrt(2 * cp * t * (1 - Math.Pow(r, (gamma - 1) / gamma)));
    }

    /// <summary>
    /// 행성 대기와 구멍으로 섞인다 (압력이 같아도): 틈 곁 칸의 기체 m몰이 나가고 같은 몰의 바깥 공기가 들어온다.
    /// m = 0.02/s/m² × 넓이 × 칸 몰 수 × h (구멍이 클수록 · 오래될수록).
    /// </summary>
    private void ExteriorMix(Link k, double h)
    {
        int c = k.A;
        double n = CellMoles(c);
        if (n <= 1e-9) return;
        double m = Math.Min(0.5, 0.02 * k.Area * h) * n;
        if (m <= 1e-12) return;
        double cg = GasC(c), t = cg > 1e-9 ? U[c] / cg : Outside.Temp;
        double f = m / n, cvOut = 0;
        for (int g = 0; g < G; g++)
        {
            int idx = c * G + g;
            N[idx] = N[idx] * (1 - f) + m * Outside.Frac[g];
            cvOut += Outside.Frac[g] * _cv[g];
        }
        U[c] = U[c] * (1 - f) + m * cvOut * Outside.Temp;
        MarkDirty(k.RA, f);
    }

    /// <summary>
    /// 구역 r에서 dn 몰을 떼어 _parcel에 담는다: 틈 곁 칸에서 먼저(무게 w · 칸마다 절반까지), 남는 것은 구역 전체에서 고르게.
    /// 남은 기체는 단열 팽창으로 식는다 (U ← U(1−f)^γ) — 빠져나가는 쪽은 엔탈피를 가져간다. 떼어 낸 에너지를 돌려준다.
    /// </summary>
    private double Take(int r, int[] near, double[] w, double dn)
    {
        Array.Clear(_parcel);
        double energy = 0, taken = 0;
        // 조금이면 틈 곁 한 칸에서만 (압력 고르기가 곧 메운다)
        if (dn <= 0.05 * CellMoles(near[0]))
        {
            double n0 = CellMoles(near[0]);
            energy = TakeFraction(near[0], dn / n0, ref taken);
            goto done;
        }
        double tw = 0;
        for (int k = 0; k < near.Length; k++) tw += w[k] * CellMoles(near[k]);
        double a1 = Math.Min(dn, 0.5 * tw);
        if (a1 > 0 && tw > 0)
        {
            double c = a1 / tw;
            for (int k = 0; k < near.Length; k++) energy += TakeFraction(near[k], c * w[k], ref taken);
        }
        double a2 = dn - taken;
        if (a2 > 1e-12)
        {
            double rest = _rn[r] - taken;
            if (rest > 1e-12)
            {
                double f = Math.Min(1, a2 / rest);
                foreach (int i in Regions[r]) energy += TakeFraction(i, f, ref taken);
            }
        }
        done:
        MarkDirty(r, taken / Math.Max(1e-9, CellMoles(near[0]) + taken));
        _rn[r] -= taken; _rU[r] -= energy;
        double c2 = 0, m2 = 0;
        for (int g = 0; g < G; g++) { c2 += _parcel[g] * _cv[g]; m2 += _parcel[g] * _molar[g]; }
        _rC[r] -= c2; _rM[r] -= m2;
        if (_rn[r] < 1e-12) { _rn[r] = 0; _rU[r] = 0; _rC[r] = 0; _rM[r] = 0; }
        return energy;
    }

    /// <summary>구역마다 쌓인 '압력이 흐트러졌을 만한 양' (칸 몰 수에 대한 비율의 합). 1e-5(약 1Pa)를 넘으면 고른다.</summary>
    private double[] _imb = Array.Empty<double>();
    public const double ImbalanceTolerance = 1e-5;

    /// <summary>이 구역의 압력이 rel만큼 흐트러졌을 수 있다 (다음 고르기에서 본다).</summary>
    private void MarkDirty(int region, double rel = 1)
    {
        if (region < 0) return;
        if (_imb.Length <= region) { var a = new double[Math.Max(Regions.Count, region + 1)]; Array.Fill(a, 1.0); _imb = a; }
        _imb[region] += Math.Abs(rel);
    }

    /// <summary>위층이 칸 기체를 직접 옮겼을 때 (덕트 등) — 모든 구역 압력을 다시 고른다.</summary>
    public void MarkDirtyAll() => MarkAllDirty();

    /// <summary>위층이 그 칸의 기체를 직접 옮겼을 때 — 그 칸의 구역만 다시 고른다.</summary>
    public void MarkCellDirty(int cell) { if (cell >= 0 && cell < RegionOf.Length) MarkDirty(RegionOf[cell]); }

    private void MarkAllDirty() { if (_imb.Length < Regions.Count) _imb = new double[Regions.Count]; Array.Fill(_imb, 1.0); }

    private double CellMoles(int i)
    {
        double n = 0;
        for (int g = 0; g < G; g++) n += N[i * G + g];
        return n;
    }

    private double TakeFraction(int i, double f, ref double taken)
    {
        if (f <= 0) return 0;
        f = Math.Min(f, 1);
        double n = 0, c = 0;
        for (int g = 0; g < G; g++)
        {
            int k = i * G + g;
            double v = N[k], t = v * f;
            n += v; c += v * _cv[g];
            _parcel[g] += t; N[k] = v - t; taken += t;
        }
        if (n <= 0) return 0;
        double gamma = c > 1e-12 ? 1 + n * R / c : 1.4;
        double u0 = U[i];
        // 단열 팽창: U(1−f)^γ — 작은 f는 2차까지 (Pow 없이)
        double u1 = f >= 1 ? 0 : f < 0.02 ? u0 * (1 - gamma * f + 0.5 * gamma * (gamma - 1) * f * f) : u0 * Math.Pow(1 - f, gamma);
        U[i] = u1;
        return u0 - u1;
    }

    /// <summary>_parcel과 그 에너지를 구역 r의 틈 곁 칸에 무게대로 넣는다.</summary>
    private void Deposit(int r, int[] near, double[] w, double energy)
    {
        double add = 0, c = 0, m = 0;
        for (int g = 0; g < G; g++) { add += _parcel[g]; c += _parcel[g] * _cv[g]; m += _parcel[g] * _molar[g]; }
        MarkDirty(r, add / Math.Max(1e-9, CellMoles(near[0])));
        if (add <= 0.05 * CellMoles(near[0]))
        {
            int i0 = near[0];
            for (int g = 0; g < G; g++) N[i0 * G + g] += _parcel[g];
            U[i0] += energy;
            _rn[r] += add; _rU[r] += energy; _rC[r] += c; _rM[r] += m;
            return;
        }
        double tw = 0;
        foreach (double x in w) tw += x;
        if (tw <= 0) return;
        for (int k = 0; k < near.Length; k++)
        {
            double s = w[k] / tw;
            int i = near[k];
            for (int g = 0; g < G; g++) N[i * G + g] += _parcel[g] * s;
            U[i] += energy * s;
        }
        _rn[r] += add; _rU[r] += energy; _rC[r] += c; _rM[r] += m;
    }

    /// <summary>
    /// 바람: 틈으로 빠지는(들어오는) 흐름 q가 구역에 고르게 퍼져 있다고 보고, 틈까지 거리 고리마다 지나가는 양을 그 고리의 칸 수로 나눈다.
    /// 속도 = q × (그 고리 밖 칸의 비율) / (고리 칸 수 × 칸의 몰 수) [m/s] — 방향은 틈 쪽 이웃.
    /// 칸의 몰 수는 틱 가운데 값으로 (감압은 한 틱 안에 구역을 비운다 — 틱 끝 값으로 나누면 속도가 부푼다) · 불기 시작할 때 밀도를 WindRho에.
    /// </summary>
    private void Wind(Link k, double dt)
    {
        void One(int start, int region, ref int[]? cache, double q)
        {
            if (start < 0 || region < 0) return;
            var cells = Regions[region];
            double nEnd = Math.Max(1e-6, _rn[region]);
            double gMid = Math.Max(0.05, 1 + 0.5 * q * dt / nEnd), gStart = Math.Max(0.05, 1 + q * dt / nEnd); // 이 틱에 잃은(+) · 얻은(−) 몫
            // 돌풍: 잃는 쪽은 처음 흐름이 평균의 ln(r)·r/(r−1)배 (r = n₀/n₁ — 지수로 줄었다) · 얻는 쪽은 평균 그대로
            double peak = gStart > 1.0001 ? Math.Log(gStart) * gStart / (gStart - 1) : 1;
            if (cache == null)
            {
                if (_distBuf.Length != Cells) _distBuf = new int[Cells];
                var d0 = DistanceFrom(start, region, _distBuf);
                cache = new int[cells.Length];
                for (int n = 0; n < cells.Length; n++) cache[n] = d0[cells[n]];
            }
            var dist = cache;
            int maxD = 0;
            foreach (int d in dist) maxD = Math.Max(maxD, d);
            var ring = new int[maxD + 2];
            foreach (int d in dist) if (d >= 0) ring[d]++;
            var beyond = new int[maxD + 2];
            for (int d = maxD; d >= 0; d--) beyond[d] = beyond[d + 1] + ring[d];
            int total = Math.Max(1, beyond[0]);
            Span<int> nb = stackalloc int[4];
            for (int n = 0; n < cells.Length; n++)
            {
                int c = cells[n], d = dist[n];
                if (d < 0) continue;
                double nCell = CellMoles(c);
                if (nCell < 1e-6) continue;
                double v = q * beyond[d] / (double)total / (ring[d] * nCell * gMid);
                v = Math.Clamp(v, -340, 340);
                double rho0 = nCell * gStart * 0.029 / CellVolume, v0 = Math.Min(340, Math.Abs(v) * gMid / gStart * peak); // 처음 밀도 · 처음 빠르기
                float q0 = (float)(0.5 * rho0 * v0 * v0);
                if (q0 > WindPa[c])
                {
                    // 가장 센 틈의 돌풍: 그 줄어드는 빠르기 (잃는 쪽 — 몰 수가 지수로 준 만큼) · 처음 밀도 (④가 같은 틱에 읽는다)
                    if (WindPa[c] == 0) WindCells.Add(c);
                    WindPa[c] = q0; WindK[c] = gStart > 1 ? (float)(Math.Log(gStart) / dt) : 0; WindRho[c] = (float)rho0;
                }
                double dx = 0, dy = 0;
                if (d == 0) { dx = L.X(k.Via) - L.X(c); dy = L.Y(k.Via) - L.Y(c); if (dx == 0 && dy == 0) ExteriorDir(c, out dx, out dy); }
                else
                {
                    L.Neighbors(c, nb);
                    foreach (int j in nb)
                    {
                        if (j < 0 || RegionOf[j] != region) continue;
                        int jn = Array.BinarySearch(cells, j);
                        if (jn >= 0 && dist[jn] == d - 1) { dx += L.X(j) - L.X(c); dy += L.Y(j) - L.Y(c); }
                    }
                }
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                WindX[c] += (float)(v * dx / len);
                WindY[c] += (float)(v * dy / len);
            }
        }
        // A 쪽에서 빠져나감(+) / 들어옴(−) — 구역 안 거리는 틈마다 한 번만 잰다 (구역이 바뀔 때까지)
        One(k.A, k.RA, ref k.DistA, k.Flow);
        if (k.B >= 0) One(k.B, k.RB, ref k.DistB, -k.Flow);
    }

    /// <summary>구멍 곁 칸의 것이 구멍 입에서 떨어진 거리 (m) — 구멍으로 드는 흐름은 반구로 모인다: v = Q / (2πr²) · Q = 0.6 × 구멍 × 구멍의 빠르기.</summary>
    public const double MouthR = 0.25;
    private static readonly (int, int)[] Dirs4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>구멍 입이 될 수 있는 칸인가 (지나갈 수 있고 · 네 이웃에 구멍 벽 · 트인 바깥이 있다) — 아니면 구멍 입 동압이 0이다 (부르는 쪽이 값비싼 셈을 건너뛴다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    public bool NearMouth(int cell)
    {
        if (cell < 0 || cell >= L.N || !Passable(cell)) return false;
        int x = L.X(cell), y = L.Y(cell);
        foreach (var (ox, oy) in Dirs4)
        {
            if (!L.In(x + ox, y + oy)) continue;
            int j = L.Index(x + ox, y + oy);
            if (IsOut(j) || L.Kind[j] == CellKind.Wall && Hole[j] > 0.01f) return true;
        }
        return false;
    }

    /// <summary>
    /// 구멍 입의 동압 (Pa — 순수 함수: 지금 기압 · 구멍 · 막음에서): 구멍 곁 칸(네 이웃이 구멍 · 트인 바깥)의 것이 받는다. 구멍의 빠르기 = min(소리 빠르기, √(2ΔP/ρ)) ·
    /// 드는 흐름 Q = 0.6 × (구멍 − 막음) × 그 빠르기가 반구로 모인다 → 25cm에서 v = min(구멍의 빠르기, Q / 2πr²) · ½ρv². 가장 센 구멍과 그 칸 (④ 물체 · ⑧ 끌림이 함께 읽는다).
    /// grow: 그 칸 기압을 이만큼 곱해 본다 (④는 장이 흐른 뒤에 부르니 틱 처음 쪽으로 되짚는다 — 그 구역은 지수로 비었다 · ⑧은 흐르기 전이라 1).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]   // ④ 감압 첫 틱에 물체 둘레 칸마다 (물체 예산 고침 — Structure.Hot)
    public double HoleMouthPa(int cell, out int mouth, double grow = 1)
    {
        mouth = -1;
        if (!NearMouth(cell)) return 0;   // 이웃에 구멍 벽 · 바깥이 없으면 기압을 세지 않는다 (물체 예산 고침 — 물체 둘레 칸 거의 모두)
        int x = L.X(cell), y = L.Y(cell);
        double p = Pressure(cell) * grow;
        if (p < 1) return 0;
        double best = 0;
        foreach (var (ox, oy) in Dirs4)
        {
            if (!L.In(x + ox, y + oy)) continue;
            int j = L.Index(x + ox, y + oy);
            bool open = IsOut(j) || L.Kind[j] == CellKind.Wall && Hole[j] >= 2f;
            if (!open && !(L.Kind[j] == CellKind.Wall && Hole[j] > 0.01f)) continue;
            double area = open ? WallFace : Math.Max(0, Hole[j] - PlugOf(j));
            int bx = x + 2 * ox, by = y + 2 * oy;
            double pOut = !open && L.In(bx, by) && !IsOut(L.Index(bx, by)) && Passable(L.Index(bx, by)) ? Pressure(L.Index(bx, by)) : Outside.KPa;
            double dp = (p - pOut) * 1000;
            if (dp <= 0 || area <= 0) continue;
            double temp = Temp(cell), rho = p * 1000 * AirMolar / (R * temp);
            double vHole = Math.Min(343 * Math.Sqrt(temp / 293.15), Math.Sqrt(2 * dp / rho));
            double v = Math.Min(vHole, 0.6 * area * vHole / (2 * Math.PI * MouthR * MouthR));
            double q = 0.5 * rho * v * v;
            if (q > best) { best = q; mouth = j; }
        }
        return best;
    }

    private void ExteriorDir(int c, out double dx, out double dy)
    {
        dx = 0; dy = 0;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(c, nb);
        foreach (int j in nb)
            if (j < 0 || L.Kind[j] == CellKind.Outside)
            {
                if (j < 0) continue;
                dx += L.X(j) - L.X(c); dy += L.Y(j) - L.Y(c);
            }
    }

    /// <summary>
    /// 구역 안 압력 고르기: 압력파는 한 틱(2.4초) 안에 구역을 건너므로 매 틱 고르게 맞춘다.
    /// 평균보다 높은 칸은 넘친 몰 수만큼을 '구역 평균 조성'의 기체로 내놓고(밀려나는 것은 둘레의 보통 공기다),
    /// 낮은 칸이 모자란 만큼 나눠 받는다 — 그래서 한 칸에 넣은 연기 · 이산화탄소는 그 칸에 남고(확산으로 천천히 퍼진다), 압력만 고르게 된다.
    /// 성분 · 에너지가 그대로 옮겨져 보존된다.
    /// </summary>
    /// <summary>고르기 한 구역의 첫 훑기 (칸마다 압력 · 몰 · 열용량 · 구역 평균 조성 · 압력 합 · 가장 큰 · 작은 압력 · 몰 합).</summary>
    private sealed class EqSet
    {
        public double[] Avg = Array.Empty<double>(), P = new double[256], Pn = new double[256], Pc = new double[256];
        public double Sum, Mx, Mn, Tot;
        public void Ready(int cells, int g)
        {
            if (Avg.Length != g) Avg = new double[g];
            if (P.Length < cells) { P = new double[cells]; Pn = new double[cells]; Pc = new double[cells]; }
            Array.Clear(Avg); Sum = 0; Mx = double.MinValue; Mn = double.MaxValue; Tot = 0;
        }
    }
    private sealed class EqBuf { public double[] Pool = Array.Empty<double>(), Give = Array.Empty<double>(); public readonly EqSet A = new(), B = new(); }
    [ThreadStatic] private static EqBuf? _eqBuf;

    private void Equalize()
    {
        int nr = Regions.Count;
        if (_imb.Length < nr) { var a = new double[nr]; Array.Fill(a, 1.0); Array.Copy(_imb, a, _imb.Length); _imb = a; }
        // 구역끼리는 칸이 겹치지 않아 어느 덩어리 · 어느 차례로 골라도 같다 (덩어리 크기는 빠르기만 바꾼다 — 줄 둘에 나눌 구역이 넉넉하게 16)
        Par.Items(nr, 16, (a, b) =>
        {
            // 일꾼마다 한 벌 (덩어리마다 새로 잡지 않는다 — 구역마다 다 덮어쓰거나 지우고 쓴다)
            var buf = _eqBuf ??= new EqBuf();
            if (buf.Pool.Length != G) { buf.Pool = new double[G]; buf.Give = new double[G]; }
            Span<double> tmp = stackalloc double[G];
            Span<int> act = stackalloc int[b - a], la = stackalloc int[b - a], lb = stackalloc int[b - a];
            int cnt = 0;
            for (int r = a; r < b; r++)
            {
                if (_imb[r] < ImbalanceTolerance) continue;
                _imb[r] = 0;
                if (Regions[r].Length < 2) continue;
                act[cnt++] = r;
            }
            // 고를 구역을 두 줄로: 첫 훑기(칸마다 성분을 이어 더한다 — 앞 덧셈을 기다린다)를 두 줄 나란히 한 고리에서 하고 · 훑기를 마친 구역부터 하나씩 고른다
            // (고르기는 제 구역 칸만 바꾸고 다른 줄의 훑기는 제 구역 칸만 읽는다 · 구역마다 더하는 차례는 그대로 — 비트까지 같다)
            SplitLanes(act[..cnt], la, lb, out int na, out int nb);
            int qa = 0, qb = 0, ka = 0, kb = 0, ra = -1, rb = -1;
            while (true)
            {
                if (ra < 0) { if (qa >= na) break; ra = la[qa++]; ka = 0; buf.A.Ready(Regions[ra].Length, G); }
                if (rb < 0) { if (qb >= nb) break; rb = lb[qb++]; kb = 0; buf.B.Ready(Regions[rb].Length, G); }
                int[] ca = Regions[ra], cb = Regions[rb];
                int m = Math.Min(ca.Length - ka, cb.Length - kb);
                ScanPair(ca, ka, buf.A, cb, kb, buf.B, m);
                ka += m; kb += m;
                if (ka == ca.Length) { Settle(ca, buf.A, buf, tmp); ra = -1; }
                if (kb == cb.Length) { Settle(cb, buf.B, buf, tmp); rb = -1; }
            }
            // 한 줄이 먼저 끝났다 — 남은 줄은 하나씩
            if (ra >= 0) { ScanFrom(Regions[ra], ka, buf.A); Settle(Regions[ra], buf.A, buf, tmp); }
            while (qa < na) { var c = Regions[la[qa++]]; buf.A.Ready(c.Length, G); ScanFrom(c, 0, buf.A); Settle(c, buf.A, buf, tmp); }
            if (rb >= 0) { ScanFrom(Regions[rb], kb, buf.B); Settle(Regions[rb], buf.B, buf, tmp); }
            while (qb < nb) { var c = Regions[lb[qb++]]; buf.B.Ready(c.Length, G); ScanFrom(c, 0, buf.B); Settle(c, buf.B, buf, tmp); }
        });
    }

    /// <summary>첫 훑기 — 칸 from부터 끝까지 하나씩 (구역 하나).</summary>
    private void ScanFrom(int[] cells, int from, EqSet e)
    {
        ReadOnlySpan<double> cv = _cv.AsSpan(0, G);
        var avg = e.Avg; var p = e.P; var pn = e.Pn; var pc = e.Pc;
        double sum = e.Sum, mx = e.Mx, mn = e.Mn, tot = e.Tot;   // 구역 평균 조성도 같은 훑기에서 (같은 차례로 더한다)
        for (int k = from; k < cells.Length; k++)
        {
            int i = cells[k];
            double n0 = 0, c0 = 0;
            var ni = N.AsSpan(i * G, G);
            Lanes.Add(avg, ni);   // avg[g] += N[i,g] (네 성분씩 — 성분마다 칸 차례는 그대로)
            for (int g = 0; g < ni.Length; g++) { double w = ni[g]; n0 += w; c0 += w * cv[g]; tot += w; }
            double v = c0 > 1e-9 ? n0 * R * (U[i] / c0) / CellVolume / 1000.0 : 0;   // Pressure(i)와 같은 셈
            p[k] = v; pn[k] = n0; pc[k] = c0; sum += v;
            if (v > mx) mx = v; if (v < mn) mn = v;
        }
        e.Sum = sum; e.Mx = mx; e.Mn = mn; e.Tot = tot;
    }

    /// <summary>첫 훑기 — 구역 둘(ka · kb 칸부터 m칸씩)을 칸 하나씩 번갈아 한 고리에서. 앞에서 센 합에 이어 더한다 · 구역마다 셈 · 차례는 <see cref="ScanFrom"/>과 같다.</summary>
    private void ScanPair(int[] ca, int ka, EqSet ea, int[] cb, int kb, EqSet eb, int m)
    {
        ReadOnlySpan<double> cv = _cv.AsSpan(0, G);
        var avgA = ea.Avg; var avgB = eb.Avg;
        ReadOnlySpan<int> xa = ca.AsSpan(ka, m), xb = cb.AsSpan(kb, m);
        Span<double> pA = ea.P.AsSpan(ka, m), pnA = ea.Pn.AsSpan(ka, m), pcA = ea.Pc.AsSpan(ka, m);
        Span<double> pB = eb.P.AsSpan(kb, m), pnB = eb.Pn.AsSpan(kb, m), pcB = eb.Pc.AsSpan(kb, m);
        double sumA = ea.Sum, mxA = ea.Mx, mnA = ea.Mn, totA = ea.Tot;
        double sumB = eb.Sum, mxB = eb.Mx, mnB = eb.Mn, totB = eb.Tot;
        for (int t = 0; t < xa.Length; t++)
        {
            int i = xa[t], j = xb[t];
            double nA = 0, cA = 0, nB = 0, cB = 0;
            var ni = N.AsSpan(i * G, G); var nj = N.AsSpan(j * G, G);
            Lanes.Add(avgA, ni); Lanes.Add(avgB, nj);
            for (int g = 0; g < ni.Length; g++)
            {
                double w = ni[g], x = nj[g];
                nA += w; cA += w * cv[g]; totA += w;
                nB += x; cB += x * cv[g]; totB += x;
            }
            double vA = cA > 1e-9 ? nA * R * (U[i] / cA) / CellVolume / 1000.0 : 0;
            double vB = cB > 1e-9 ? nB * R * (U[j] / cB) / CellVolume / 1000.0 : 0;
            pA[t] = vA; pnA[t] = nA; pcA[t] = cA; sumA += vA;
            if (vA > mxA) mxA = vA; if (vA < mnA) mnA = vA;
            pB[t] = vB; pnB[t] = nB; pcB[t] = cB; sumB += vB;
            if (vB > mxB) mxB = vB; if (vB < mnB) mnB = vB;
        }
        ea.Sum = sumA; ea.Mx = mxA; ea.Mn = mnA; ea.Tot = totA;
        eb.Sum = sumB; eb.Mx = mxB; eb.Mn = mnB; eb.Tot = totB;
    }

    /// <summary>첫 훑기를 마친 구역을 고른다: 평균보다 높은 칸은 넘친 몰을 평균 조성으로 내놓고 · 낮은 칸이 모자란 만큼 나눠 받는다.</summary>
    private void Settle(int[] cells, EqSet e, EqBuf buf, Span<double> tmp)
    {
        double mean = e.Sum / cells.Length;
        if (e.Mx - e.Mn <= 1e-9 * mean + 1e-9) return;
        double tot = e.Tot;
        if (tot <= 0) return;
        var avg = e.Avg; var p = e.P; var pn = e.Pn; var pc = e.Pc;
        var pool = buf.Pool; var give = buf.Give;
        ReadOnlySpan<double> cv = _cv.AsSpan(0, G);
        for (int g = 0; g < G; g++) avg[g] /= tot;
        Array.Clear(pool);
        double poolU = 0, deficit = 0;
        for (int k = 0; k < cells.Length; k++)
        {
            if (p[k] <= mean) { deficit += mean - p[k]; continue; }
            int i = cells[k];
            double n = pn[k], c = pc[k];   // 위에서 센 값 (그 사이 이 칸은 바뀌지 않았다)
            if (n <= 0 || c <= 0) continue;
            double ex = n * (p[k] - mean) / p[k];   // 내놓을 몰 수
            double tc = U[i] / c;                  // 이 칸 온도
            var ni = N.AsSpan(i * G, G);
            bool ok = Lanes.ScaleWithin(give, avg, ex, ni, 0.9);   // give[g] = ex · avg[g] · 하나라도 0.9 · N[i,g]를 넘으면 false
            if (!ok) Lanes.Scale(give, ni, ex / n); // 이 칸 조성이 평균과 너무 다르면 제 조성대로
            Lanes.Move(ni, pool, give);   // N[i,g] −= give[g] · pool[g] += give[g]
            double du = Lanes.SumProd(give, cv, tc, tmp);   // Σ give[g] · cv[g] · tc (성분 차례대로)
            du = Math.Min(du, U[i]);
            U[i] -= du; poolU += du;
        }
        if (deficit <= 0) return;
        for (int k = 0; k < cells.Length; k++)
        {
            if (p[k] >= mean) continue;
            int i = cells[k];
            double sk = (mean - p[k]) / deficit;
            Lanes.AddScaled(N.AsSpan(i * G, G), pool, sk);   // N[i,g] += pool[g] · sk
            U[i] += poolU * sk;
        }
    }
}
