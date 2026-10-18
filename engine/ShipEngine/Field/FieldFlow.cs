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

    private void RegionTotals()
    {
        int n = Regions.Count;
        if (_rn.Length < n) { _rn = new double[n]; _rU = new double[n]; _rC = new double[n]; _rM = new double[n]; }
        for (int r = 0; r < n; r++)
        {
            double sn = 0, su = 0, sc = 0, sm = 0;
            foreach (int i in Regions[r])
            {
                su += U[i];
                for (int g = 0; g < G; g++) { double v = N[i * G + g]; sn += v; sc += v * _cv[g]; sm += v * _molar[g]; }
            }
            _rn[r] = sn; _rU[r] = su; _rC[r] = sc; _rM[r] = sm;
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
                WindPa[c] = (float)Math.Max(WindPa[c], 0.5 * rho0 * v0 * v0);
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
    private void Equalize()
    {
        int nr = Regions.Count;
        if (_imb.Length < nr) { var a = new double[nr]; Array.Fill(a, 1.0); Array.Copy(_imb, a, _imb.Length); _imb = a; }
        Par.Items(nr, 4, (a, b) =>
        {
            var pool = new double[G];
            var avg = new double[G];
            var give = new double[G];
            var p = new double[256];
            var pn = new double[256];   // 칸의 몰 · 열용량 (압력을 셀 때 한 번 — 넘친 칸에서 다시 세지 않는다)
            var pc = new double[256];
            for (int r = a; r < b; r++)
            {
                if (_imb[r] < ImbalanceTolerance) continue;
                _imb[r] = 0;
                var cells = Regions[r];
                if (cells.Length < 2) continue;
                if (p.Length < cells.Length) { p = new double[cells.Length]; pn = new double[cells.Length]; pc = new double[cells.Length]; }
                double sum = 0, mx = double.MinValue, mn = double.MaxValue;
                for (int k = 0; k < cells.Length; k++)
                {
                    int i = cells[k], bi = i * G;
                    double n0 = 0, c0 = 0;
                    for (int g = 0; g < G; g++) { double w = N[bi + g]; n0 += w; c0 += w * _cv[g]; }
                    double v = c0 > 1e-9 ? n0 * R * (U[i] / c0) / CellVolume / 1000.0 : 0;   // Pressure(i)와 같은 셈
                    p[k] = v; pn[k] = n0; pc[k] = c0; sum += v;
                    if (v > mx) mx = v; if (v < mn) mn = v;
                }
                double mean = sum / cells.Length;
                if (mx - mn <= 1e-9 * mean + 1e-9) continue;
                // 구역 평균 조성
                Array.Clear(avg);
                double tot = 0;
                foreach (int i in cells) for (int g = 0; g < G; g++) { double v = N[i * G + g]; avg[g] += v; tot += v; }
                if (tot <= 0) continue;
                for (int g = 0; g < G; g++) avg[g] /= tot;
                Array.Clear(pool);
                double poolU = 0, deficit = 0;
                for (int k = 0; k < cells.Length; k++)
                {
                    if (p[k] <= mean) { deficit += mean - p[k]; continue; }
                    int i = cells[k];
                    double n = pn[k], c = pc[k];   // 위에서 센 값 (그 사이 이 칸은 바뀌지 않았다)
                    if (n <= 0 || c <= 0) continue;
                    double e = n * (p[k] - mean) / p[k];   // 내놓을 몰 수
                    double tc = U[i] / c;                  // 이 칸 온도
                    bool ok = true;
                    for (int g = 0; g < G; g++) { give[g] = e * avg[g]; if (give[g] > 0.9 * N[i * G + g]) ok = false; }
                    if (!ok) for (int g = 0; g < G; g++) give[g] = e / n * N[i * G + g]; // 이 칸 조성이 평균과 너무 다르면 제 조성대로
                    double du = 0;
                    for (int g = 0; g < G; g++) { N[i * G + g] -= give[g]; pool[g] += give[g]; du += give[g] * _cv[g] * tc; }
                    du = Math.Min(du, U[i]);
                    U[i] -= du; poolU += du;
                }
                if (deficit <= 0) continue;
                for (int k = 0; k < cells.Length; k++)
                {
                    if (p[k] >= mean) continue;
                    int i = cells[k];
                    double s = (mean - p[k]) / deficit;
                    for (int g = 0; g < G; g++) N[i * G + g] += pool[g] * s;
                    U[i] += poolU * s;
                }
            }
        });
    }
}
