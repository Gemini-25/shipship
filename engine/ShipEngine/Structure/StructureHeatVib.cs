namespace ShipEngine;

/// <summary>
/// ④ 열 · 진동 (깊이 바퀴 3 · docs/ENGINE.md ④ '열 · 진동'):
/// <para>열 응력 — 벽 칸의 응력 신호에 막힌 열 응력 E·α·(겉면 − 293K) × 0.5(구속)를 더한다 (골조가 만나는 이음매는 × 3) · 되돌이를 세어 금은 파리스 법칙으로 자라고 ·
/// 금이 없으면 피로 한계(재질 피로 몫 × 강도)를 넘은 반 주기마다 피로가 쌓여 (마이너 — 넘친 몫의 세제곱) 0.85를 넘으면 금이 된다.</para>
/// <para>진동 — ⑥ 망이 돌아가는 기계마다 요청한다 (곁 2칸의 벽 · 응력 폭 · 횟수): 금이 있고 문턱 ΔK를 넘으면 횟수만큼 자란다 (작은 금은 그대로).</para>
/// </summary>
public sealed partial class Structure
{
    public const double ThermalRestraint = 0.5, JointKt = 3, SnK = 0.002;
    public long VibGrowths, ThermalCracks;

    /// <summary>
    /// 열 수축 · 팽창의 딱 소리 (소리 단계 S6 · docs/SOUND.md 결정 14 — 세상에서 실제로 나는 소리만): 외판 · 골조 이음매는 늘고 줄며 붙들린 자리가 미끄러질 때 딱 소리를 낸다
    /// (식는 차 · 밤의 집이 딱딱거리는 것과 같다). 지난번 미끄러진 뒤 겉면 온도가 이음매 6K · 외판 12K 넘게 바뀌면 한 번 — 44 ~ 56dB (바뀐 만큼).
    /// 위험한 소리가 아니다 (끼익 · 쩍과 다르다 — 사람은 '벽이 버틴다'로 듣지 않는다 · 잠귀가 밝은 이는 깰 수도 있다).
    /// </summary>
    public const double SlipJointK = 6, SlipPlateK = 12;
    public float[] TsSlip = Array.Empty<float>();
    /// <summary>열 수축 딱 소리 수 (확인용 · 찍는다).</summary>
    public long ThermalTicks;

    private void ThermalTick(Sim sim, int c)
    {
        bool joint = _axis.Length > c && _axis[c] == 3;
        if (!joint && !L.Hull[c]) return;
        if (TsSlip.Length != L.N) { TsSlip = new float[L.N]; Array.Fill(TsSlip, float.NaN); }
        float ts = (float)F.Ts[c];
        if (float.IsNaN(TsSlip[c])) { TsSlip[c] = ts; return; }   // 처음 본 온도 (딱 소리 없이 기준만)
        double d = Math.Abs(ts - TsSlip[c]), slip = joint ? SlipJointK : SlipPlateK;
        if (d < slip) return;
        TsSlip[c] = ts;
        ThermalTicks++;
        sim.Find<Sound>()?.Emit(sim, NearestAir(c), (float)(44 + 4 * Math.Min(3, d / slip)), SoundKind.Tick, 0);
    }

    /// <summary>막힌 열 응력 (MPa — 데우면 누르는 쪽 · 음수).</summary>
    public double ThermalSigma(int c)
    {
        var m = T.Materials[L.Material[c]].Def;
        if (m.E <= 0 || m.Alpha <= 0) return 0;
        double dT = F.Ts[c] - 293;
        return -m.E * 1000 * m.Alpha * dT * ThermalRestraint * (_axis.Length > c && _axis[c] == 3 ? JointKt : 1);
    }

    /// <summary>금 없는 칸의 반 주기 하나: 피로 한계를 넘었으면 피로가 쌓인다 (마이너) · 0.85를 넘으면 금이 된다.</summary>
    private void FatigueHalfCycle(Sim sim, int c, double range, double thRange)
    {
        if (L.Kind[c] != CellKind.Wall) return;
        var m = T.Materials[L.Material[c]].Def;
        double endurance = m.Fatigue * m.Strength;
        if (endurance <= 0 || range <= endurance) return;
        double x = (range - endurance) / endurance;
        Fatigue[c] = (float)Math.Min(1, Fatigue[c] + SnK * x * x * x);
        // 처음 피로가 쌓일 때 까닭을 남긴다 (뒤에 그 벽이 금 · 과부하로 터져도 이 뿌리에 잇는다) — 막힌 열 응력의 오르내림이 되돌이의 반을 넘으면 열 때문
        if (DamageCause[c] == 0)
        {
            bool heat = thRange * 2 >= range;
            string where = _axis.Length > c && _axis[c] == 3 ? "이음매" : "벽";
            DamageCause[c] = sim.Causes.Add(heat ? CauseKind.Heat : CauseKind.Wear, heat ? $"열 팽창 · 수축이 되풀이돼 {Ko.I(where)} 피로해졌다" : $"응력이 되풀이돼 {Ko.I(where)} 피로해졌다", sim.Tick, c);
        }
        if (Fatigue[c] < 0.85f || Crack[c] > 0) return;
        SeedCrack(sim, c, 0.005, DamageCause[c]);
        ThermalCracks++;
    }

    /// <summary>⑥의 요청: 돌아가는 기계의 진동 — 곁 2칸 벽의 금이 range MPa 폭 · cycles번 흔들린다 (문턱 ΔK를 넘을 때만 자란다).</summary>
    public void Vibrate(Sim sim, int cell, double rangeMPa, double cycles, int cause)
    {
        if (cell < 0 || rangeMPa <= 0 || cycles <= 0) return;
        int x0 = L.X(cell), y0 = L.Y(cell);
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = x0 + dx, y = y0 + dy;
                if (x < 0 || y < 0 || x >= L.W || y >= L.H) continue;
                int c = L.Index(x, y);
                if (L.Kind[c] != CellKind.Wall || Crack[c] <= 0 || F.Hole[c] >= 2) continue;
                var m = T.Materials[L.Material[c]].Def;
                if (m.ParisC <= 0) continue;
                double dk = StressIntensity(c, rangeMPa / (1 + Math.Max(Math.Abs(dx), Math.Abs(dy))));   // 멀수록 약하다
                if (dk < m.DKth) continue;
                if (CrackCause[c] == 0) CrackCause[c] = cause;
                VibGrowths++;
                Grow(sim, c, cycles * m.ParisC * Math.Pow(dk, m.ParisM));
            }
    }
}
