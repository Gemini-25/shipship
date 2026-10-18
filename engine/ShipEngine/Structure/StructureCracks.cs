namespace ShipEngine;

/// <summary>
/// ④ 구조 — 금 (깊이 바퀴 2 · docs/ENGINE.md ④ '금이 자란다'). 파괴 역학을 칸 위에서:
/// <para>
/// 벽 칸마다 금의 반길이 a (m · 칸 하나에 많아야 0.5 — 넘치면 벽 줄을 따라 이웃 칸으로). 응력이 오르내린 반 주기마다 파리스 법칙 da/dN = C·ΔK^m
/// (ΔK = M·Kt·Δσ·√(π·a_유효) · 문턱 아래면 자라지 않는다) · 젖은 곳에서 K가 K_ISCC를 넘으면 응력 부식으로 시간에 따라 ·
/// 판을 뚫으면 벌어진 넓이만큼 새고(새어 나옴이 먼저) · K가 파괴 인성(온도에 따라)을 넘으면 벽 줄을 따라 찢어진다 — 골조(벽이 만나는 곳 · 문틀 · 덧댄 곳)가 멈춘다.
/// </para>
/// M = Folias 불룩해짐 √(1 + 1.61a²/Rt) (골조가 붙잡아 1.5배까지) · Kt = 문 곁 2.5 · 벽이 만나는 곳 1.5 (작은 금일 때만 — 모서리의 응력 집중).
/// </summary>
public sealed partial class Structure
{
    /// <summary>배 단면의 반지름 (m — 불룩해짐 · 어림) · 되돌이 문턱 (MPa — 이보다 작은 출렁임은 주기로 세지 않는다).</summary>
    public const double ShellR = 5, CycleMPa = 2;
    /// <summary>판을 따라 달리는 금이 한 번에 찢을 수 있는 칸 (그 사이 기압이 빠져 멈춘다).</summary>
    public const int TearMax = 24;

    // ── 상태 (찍는다) ──
    /// <summary>금의 반길이 (m) · 원인 마디 · 막은(덧판) 길이 · 응력 되돌이 (지난 꺾임 · 지금 끝 · 방향).</summary>
    public float[] Crack, CrackPatch, SigTurn, SigRun;
    public int[] CrackCause;
    public sbyte[] SigDir;
    public long CrackGrowths, Tears, CrackLeaks;

    // ── 도면에서 (찍지 않는다) ──
    /// <summary>벽 칸의 결: 1 좌우로 이어짐 · 2 위아래 · 3 만나는 곳(골조 — 금이 멈춘다) · 0 외톨이.</summary>
    private byte[] _axis = Array.Empty<byte>();
    private float[] _kt = Array.Empty<float>(), _thick = Array.Empty<float>();

    private void BuildCrackGeometry()
    {
        _axis = new byte[L.N]; _kt = new float[L.N]; _thick = new float[L.N];
        bool S(int x, int y) => L.In(x, y) && L.Kind[L.Index(x, y)] is CellKind.Wall or CellKind.Door;
        bool D(int x, int y) => L.In(x, y) && L.Kind[L.Index(x, y)] == CellKind.Door;
        for (int c = 0; c < L.N; c++)
        {
            if (L.Kind[c] != CellKind.Wall) continue;
            int x = L.X(c), y = L.Y(c);
            bool h = S(x - 1, y) || S(x + 1, y), v = S(x, y - 1) || S(x, y + 1);
            int n = (S(x - 1, y) ? 1 : 0) + (S(x + 1, y) ? 1 : 0) + (S(x, y - 1) ? 1 : 0) + (S(x, y + 1) ? 1 : 0);
            _axis[c] = (byte)(h && v || n >= 3 ? 3 : h ? 1 : v ? 2 : 0);
            _kt[c] = D(x - 1, y) || D(x + 1, y) || D(x, y - 1) || D(x, y + 1) ? 2.5f : _axis[c] == 3 ? 1.5f : 1f;
            _thick[c] = L.Hull[c] ? 0.010f : 0.004f;
        }
    }

    /// <summary>파괴 인성 (MPa√m) — 강철은 연성-취성 전이 온도 아래에서 부서지기 쉽다.</summary>
    public static double Toughness(MaterialDef m, double tempK)
    {
        if (m.DbttK <= 0) return m.Kc;
        return m.KcLow + (m.Kc - m.KcLow) / (1 + Math.Exp(-(tempK - m.DbttK) / 15));
    }

    /// <summary>압력 외판의 불룩해짐 (Folias) — 금이 길수록 금 끝이 더 벌어진다 · 골조가 1m마다 판을 붙잡아 1.5배까지 (보강된 판).</summary>
    public double Bulge(int c, double a) => Math.Min(MaxBulge, Math.Sqrt(1 + 1.61 * a * a / (ShellR * Math.Max(1e-3, _thick[c]))));
    public const double MaxBulge = 1.5;

    /// <summary>이 칸이 받는 응력 (MPa): 압력 차를 열 약화 없는 정격으로 나눈 몫 × 재질 강도 + 점화의 관성 하중.</summary>
    public double Sigma(int c, double dp)
    {
        var m = T.Materials[L.Material[c]].Def;
        double heat = HeatFactor(c, m);
        double r = Rating(c) / Math.Max(0.1, heat);
        return m.Strength * dp / Math.Max(1e-3, r) + ThrustG * (_thrustSig.Length > c ? _thrustSig[c] : 0);
    }

    /// <summary>이 칸 금의 유효 반길이 (m): 제 금 + 벽 줄을 따라 이어진 다 갈라진 · 찢긴 칸의 반.</summary>
    public double CrackLength(int c)
    {
        double a = Crack[c];
        if (a < 0.3 || _axis[c] is 0 or 3) return a;
        int step = _axis[c] == 1 ? 1 : L.W, run = 0;
        foreach (int dir in new[] { -1, 1 })
            for (int k = 1, n = c + dir * step; k <= 8 && Along(c, n); k++, n += dir * step)
            {
                if (Crack[n] >= 0.5f || F.Hole[n] >= 2 && L.Kind[n] == CellKind.Wall) run++; else break;
            }
        return a + 0.5 * run;
    }

    private bool Along(int c, int n) => n >= 0 && n < L.N && (L.X(n) == L.X(c) || L.Y(n) == L.Y(c)) && L.Kind[n] == CellKind.Wall && F.Severed[n] == 0;

    /// <summary>금 끝의 응력 확대 계수 K (MPa√m).</summary>
    public double StressIntensity(int c, double sigma)
    {
        double a = CrackLength(c);
        if (a <= 0) return 0;
        double kt = 1 + (_kt[c] - 1) * Math.Exp(-Crack[c] / 0.01);
        return Bulge(c, a) * kt * sigma * Math.Sqrt(Math.PI * a);
    }

    /// <summary>
    /// 그 벽이 기압 차 dpKPa를 받으면 찢어짐에 얼마나 가까운가 (금 끝 K ÷ 파괴 인성 — 1이면 찢어진다 · 덧대 받친 벽은 정격이 높아 그만큼 낮다).
    /// 주컴퓨터의 판단용 (하중 센서 + 금 센서 — 비워진 방을 다시 채워도 되나). 금이 없으면 0.
    /// </summary>
    public double TearRatio(int c, double dpKPa)
    {
        if (c < 0 || c >= L.N || L.Kind[c] != CellKind.Wall || Crack[c] <= 0 && !(F.Hole[c] >= 2)) return 0;
        var m = T.Materials[L.Material[c]].Def;
        double k = StressIntensity(c, Sigma(c, dpKPa));
        return k / Math.Max(1e-6, Toughness(m, F.Ts[c]));
    }

    /// <summary>금을 심는다 (파편의 흠 · 과부하 · 이야기꾼의 숨은 금 · 부식 구멍) — 이미 더 크면 그대로.</summary>
    public void SeedCrack(int c, double a, int cause) => SeedCrack(null, c, a, cause);

    /// <summary>금을 심는다 — 세계를 주면 판을 뚫은 금은 곧장 새기 시작한다 ('금 사이로 공기가 샌다').</summary>
    public void SeedCrack(Sim? sim, int c, double a, int cause)
    {
        if (L.Kind[c] != CellKind.Wall || F.Severed[c] != 0 || a <= Crack[c]) return;
        Crack[c] = (float)Math.Min(0.5, a);
        if (cause != 0 || CrackCause[c] == 0) CrackCause[c] = cause;
        if (sim != null) Leak(sim, c, true);
    }

    /// <summary>하중 한 주기 (0 → σ → 0)를 이 칸에 준다 — 시험 · 바깥에서 오는 되풀이 하중(점화 · 도킹)을 한꺼번에 셀 때.</summary>
    public void LoadCycle(Sim sim, int c, double rangeMPa, int cycles = 1)
    {
        for (int i = 0; i < cycles; i++) { HalfCycle(sim, c, rangeMPa); HalfCycle(sim, c, rangeMPa); }
    }

    /// <summary>응력 되돌이 (반 주기 세기): 오르다 2MPa 넘게 꺾이면 반 주기 하나 — 그 폭으로 금이 자란다.</summary>
    private void Cycle(Sim sim, int c, double sigma)
    {
        float s = (float)sigma;
        if (SigDir[c] >= 0)
        {
            if (s >= SigRun[c]) { SigRun[c] = s; if (SigDir[c] == 0 && s > 0) SigDir[c] = 1; }
            else if (SigRun[c] - s > CycleMPa) { HalfCycle(sim, c, SigRun[c] - SigTurn[c]); SigTurn[c] = SigRun[c]; SigRun[c] = s; SigDir[c] = -1; }
        }
        else
        {
            if (s <= SigRun[c]) SigRun[c] = s;
            else if (s - SigRun[c] > CycleMPa) { HalfCycle(sim, c, SigTurn[c] - SigRun[c]); SigTurn[c] = SigRun[c]; SigRun[c] = s; SigDir[c] = 1; }
        }
    }

    private void HalfCycle(Sim sim, int c, double range)
    {
        if (Crack[c] <= 0 || range <= 0) return;
        var m = T.Materials[L.Material[c]].Def;
        if (m.ParisC <= 0) return;
        double dk = StressIntensity(c, range);
        if (dk < m.DKth) return;
        Grow(sim, c, 0.5 * m.ParisC * Math.Pow(dk, m.ParisM));
    }

    /// <summary>금이 da만큼 자란다 — 칸을 넘치면 벽 줄을 따라 이웃 칸으로 · 판을 뚫으면 새기 시작한다 · 크게 자라면 쩍.</summary>
    private void Grow(Sim sim, int c, double da)
    {
        if (da <= 0) return;
        double a0 = Crack[c], a1 = a0 + da;
        CrackGrowths++;
        if (a1 > 0.5)
        {
            Crack[c] = 0.5f;
            if (_axis[c] is 1 or 2)
            {
                int step = _axis[c] == 1 ? 1 : L.W;
                foreach (int n in new[] { c - step, c + step })
                    if (Along(c, n) && F.Hole[n] < 2)
                    {
                        // 골조(만나는 곳)에서는 4분의 1만 넘어간다
                        double over = (a1 - 0.5) / 2 * (_axis[n] == 3 ? 0.25 : 1);
                        if (Crack[n] <= 0) { Crack[n] = 0.001f; CrackCause[n] = CrackCause[c]; }
                        Crack[n] = (float)Math.Min(0.5, Crack[n] + over);
                        Leak(sim, n);
                    }
            }
        }
        else Crack[c] = (float)a1;
        // 음향 방출: 5mm 자랄 때마다 쩍 (금 끝이 튀며 내는 소리 — 판을 타고 온다)
        if ((int)(Crack[c] * 200) > (int)(a0 * 200))
            sim.Find<Sound>()?.Emit(sim, NearestAir(c), (float)(48 + 4 * Math.Log10(1 + 1000 * da)), SoundKind.Crack, CrackCause[c]);
        Leak(sim, c);
    }

    /// <summary>판을 뚫은 금의 벌어진 넓이 (m²): 2πσa·a_유효·M²/E (설계 기압 한 기압에서 · 막은 길이까지는 새지 않는다).</summary>
    public double CrackOpening(int c)
    {
        double a = Crack[c];
        if (a <= 0 || 2 * a <= _thick[c]) return 0;
        if (CrackPatch[c] > 0 && a <= CrackPatch[c] + 0.02f) return 0;
        var m = T.Materials[L.Material[c]].Def;
        double ae = CrackLength(c), sig = Sigma(c, 101.3), mb = Bulge(c, ae);
        double area = 2 * Math.PI * sig * a * ae * mb * mb / (m.E * 1000);
        return Math.Min(1.9, area);
    }

    private void Leak(Sim sim, int c, bool fresh = false)
    {
        double area = CrackOpening(c);
        if (area <= 2e-6) return;
        if (F.Hole[c] <= 0)
        {
            CrackLeaks++;
            int cause = sim.Causes.Add(CauseKind.Breach, fresh ? "금 사이로 공기가 샌다" : "자라던 금이 판을 뚫었다 — 공기가 샌다", sim.Tick, c, CrackCause[c]);
            DamageCause[c] = cause;
            Open(sim, c, (float)area, cause, FactKind.WallCrack, 60);
        }
        else if (area > F.Hole[c] * 1.25 && F.Hole[c] < 2) F.SetHole(c, (float)area);
    }

    /// <summary>금이 있는 칸 (매 틱): 금 끝의 K가 파괴 인성을 넘으면 찢어진다.</summary>
    private void Fracture(Sim sim, int c, double sigma)
    {
        if (sigma <= 0) return;
        var m = T.Materials[L.Material[c]].Def;
        double k = StressIntensity(c, sigma), kc = Toughness(m, F.Ts[c]);
        if (k < kc) return;
        bool cold = m.DbttK > 0 && F.Ts[c] < m.DbttK + 20;
        int cause = sim.Causes.Add(CauseKind.Breach, cold ? "차가워 부서지기 쉬워진 벽의 금이 갑자기 찢어졌다" : "자라던 금이 버티지 못하고 찢어졌다", sim.Tick, c, CrackCause[c] != 0 ? CrackCause[c] : DamageCause[c]);
        Tear(sim, c, cause);
    }

    /// <summary>찢어짐: 그 칸이 터지고 금이 벽 줄을 따라 달린다 — 찢긴 줄이 하나의 긴 금이 되어 끝의 K가 커진다 · 골조에서 멈춘다.</summary>
    private void Tear(Sim sim, int c, int cause)
    {
        Tears++;
        Open(sim, c, 2.5f, cause, FactKind.WallBreach);
        if (_axis[c] is not (1 or 2)) return;
        int step = _axis[c] == 1 ? 1 : L.W;
        int run = 0;
        foreach (int dir in new[] { -1, 1 })
        {
            double tip = CrackLength(c) + 0.5;
            int prev = c;
            for (int k = 0, n = c + dir * step; k < TearMax && Along(prev, n); k++, prev = n, n += dir * step)
            {
                if (F.Hole[n] >= 2) { tip += 0.5; continue; }
                var m = T.Materials[L.Material[n]].Def;
                double sig = Sigma(n, DeltaP(n));
                double kn = Bulge(n, tip) * sig * Math.Sqrt(Math.PI * tip);
                bool stopper = _axis[n] == 3 || Braced[n] != 0;
                if (kn < Toughness(m, F.Ts[n]) * (stopper ? 2 : 1))
                {
                    SeedCrack(n, 0.5, cause);   // 금 끝이 이 칸에서 멈췄다
                    break;
                }
                int c2 = sim.Causes.Add(CauseKind.Breach, "찢어짐이 벽을 따라 달렸다", sim.Tick, n, cause);
                Open(sim, n, 2.5f, c2, FactKind.WallBreach, 0);
                Tears++; run++;
                tip += 0.5;
            }
        }
        if (run > 0) sim.Find<Sound>()?.Emit(sim, NearestAir(c), (float)Math.Min(140, 120 + 3 * run), SoundKind.Bang, cause);
    }

    /// <summary>응력 부식 균열 (25틱마다): 금 끝의 K가 K_ISCC를 넘고 젖어 있으면 (습도 60% · 80% · 고인 물) 시간에 따라 자란다.</summary>
    private void StressCorrosion(Sim sim, double dt)
    {
        Span<int> nb = stackalloc int[4];
        for (int c = 0; c < L.N; c++)
        {
            if (Crack[c] <= 0 || L.Kind[c] != CellKind.Wall || F.Hole[c] >= 2) continue;
            var m = T.Materials[L.Material[c]].Def;
            if (m.Scc <= 0) continue;
            double k = StressIntensity(c, Sigma(c, DeltaP(c)));
            if (k <= m.Kiscc) continue;
            double wet = Wetness(c, nb);
            if (wet <= 0) continue;
            Grow(sim, c, m.Scc * wet * Math.Min(1, (k - m.Kiscc) / m.Kiscc + 0.3) * dt);
        }
    }

    /// <summary>벽이 젖은 정도 0~1 (곁 공기의 상대 습도 60% → 0.3 · 80% → 1 · 고인 물 1 · 염이 있으면 세 배 — ISO 9223 젖음 시간).</summary>
    public double Wetness(int c, Span<int> nb)
    {
        L.Neighbors(c, nb);
        double w = 0;
        foreach (int j in nb)
        {
            if (j < 0 || !L.IsSpace(j) || F.RegionOf.Length <= j || F.RegionOf[j] < 0) continue;
            double rh = F.Partial(T.gH2O, j) / Math.Max(1e-6, Water.SatKPa(F.Ts[c]));
            double x = rh > 0.8 ? 1 : rh > 0.6 ? 0.3 : 0;
            if (F.Stored(T.sWater, j) > 10) x = 1;
            if (T.sSalt >= 0 && F.Stored(T.sSalt, j) > 0.01) x = Math.Max(x, 0.3) * 3; // 염은 물기를 끌어당긴다 (소금 조해 — 습도 75%)
            w = Math.Max(w, x);
        }
        return w;
    }
}
