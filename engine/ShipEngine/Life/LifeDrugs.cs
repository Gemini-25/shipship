namespace ShipEngine;

/// <summary>약 (주는 단위): 진통제 mg · 해열 진통제 mg · 항생제 시간 · 해독제 · 아드레날린 (한 번).</summary>
public enum Drug : byte { Opioid, Apap, Abx, Antidote, Epi }

/// <summary>
/// ⑦ 약 (깊이 바퀴 3 (나) — docs/ENGINE.md '장기 · 약 · 수술'). 한 칸 모형: 몸에 남은 양이 반감기마다 반으로 (간이 상하면 더디게) · 효과는 양 ÷ (1 + 내성).
/// <para><b>진통제</b> (모르핀 · 반감기 3시간): 덜어 주는 아픔 0.05 × 효과 (많아야 0.8) · 효과가 25mg를 넘으면 숨이 얕아져 피의 산소가 준다 (60mg이면 0.9배 줄어) ·
/// 내성은 효과 10mg를 하루 내내 받으면 1이 쌓이고 (네 시간마다 10mg 사흘이면 1.4) 일주일에 풀린다 · 의존은 내성의 반을 따라가고 열흘에 풀린다 — 약이 떨어지면(효과 2mg 아래) 의존만큼 아프다 (금단).</para>
/// <para><b>해열 진통제</b> (아세트아미노펜 · 반감기 2.5시간): 열의 설정점을 몸에 남은 양 1g에 70%까지 내리고 · 아픔 0.2 ·
/// 24시간치가 6g을 넘으면 넘은 만큼 간이 상한다 (12g이면 반나절에 1) · 간은 일주일에 다시 자란다 · 간이 다 상하면 죽는다.</para>
/// <para><b>항생제</b>: 다 먹지 않고(48시간 못 미쳐) 끊었는데 곪음이 남으면 내성균 — 다음 항생제는 0.4배로 걷는다.</para>
/// <para><b>해독제</b> (하이드록소코발라민 · 두 시간): 시안화수소 반감기 1시간 → 6분. <b>아드레날린</b>: 4분 동안 다시 뛸 몫 1.5배.</para>
/// </summary>
public sealed partial class Life
{
    public const double OpioidHalfH = 3, ApapHalfH = 2.5, ApapSafeMg = 6000, AntidoteHours = 2, EpiMinutes = 4;
    public long Doses, Overdoses, LiverInjuries, Resistances;

    /// <summary>약을 준다 (마음 · 시험). 원인 마디는 그 몸의 '약을 준 원인'으로 남는다 (과용 · 간 손상 · 내성균의 사슬).</summary>
    public void Give(Sim sim, int id, Drug d, double amount, int cause)
    {
        if (id < 0 || id >= Bodies.Count) return;
        ref var b = ref this[id];
        if (b.Alive == 0) return;
        switch (d)
        {
            case Drug.Opioid: b.Opioid += (float)amount; b.OpioidAt = sim.Tick; break;
            case Drug.Apap: b.Apap += (float)amount; b.ApapDay += (float)amount; break;
            case Drug.Abx: if (b.Abx <= 0) b.AbxCourse = 0; b.Abx = (float)Math.Max(b.Abx, amount); break;
            case Drug.Antidote: b.Antidote = (float)AntidoteHours; break;
            case Drug.Epi: b.EpiAt = sim.Tick; break;
        }
        b.DrugCause = cause; Doses++;
        sim.Facts.Emit(sim.Tick, FactKind.Dosed, b.Cell, (float)amount, cause, actor: id, other: (int)d);
    }

    /// <summary>진통제의 효과 (내성을 뺀 mg).</summary>
    public static double OpioidEffect(in Body b) => b.Opioid / (1 + b.OpioidTol);

    /// <summary>약이 덜어 주는 아픔 (진통제 많아야 0.8 · 해열 진통제 0.2).</summary>
    public static double Analgesia(in Body b) => Math.Min(0.8, 0.05 * OpioidEffect(b)) + Math.Min(0.2, b.Apap / 1000 * 0.2);

    /// <summary>느끼는 아픔 (0~1): 아픔 + 화상 · 타박 · 압력 상처 − 약.</summary>
    public static double FeltPain(in Body b) => Math.Clamp(b.Pain + b.Burn * 0.6 + b.Blunt * 0.5 + b.Barotrauma * 0.4 + AppendixPain(b) - Analgesia(b), 0, 1);

    /// <summary>진통제로 얕아진 숨 (피의 산소에 곱할 1 − 이 값).</summary>
    public static double RespDepression(in Body b) => Math.Clamp((OpioidEffect(b) - 25) / 35, 0, 0.9);

    /// <summary>약 때문에 졸린 정도 (마음이 졸림에 더한다).</summary>
    public static double Sedation(in Body b) => Math.Clamp(OpioidEffect(b) / 40, 0, 1);

    /// <summary>해열 진통제가 열의 설정점을 내리는 몫.</summary>
    public static double FeverCut(in Body b) => Math.Min(0.7, b.Apap / 1000 * 0.7);

    /// <summary>약 한 걸음 (몸 한 걸음 안): 몸속 변화 + 간이 상한 순간의 원인 마디.</summary>
    private void DrugStep(Sim sim, int id, ref Body b, double dt)
    {
        bool was = b.Liver < 0.3f;
        DrugKinetics(ref b, dt);
        if (was && b.Liver >= 0.3f) { LiverInjuries++; b.HurtCause = sim.Causes.Add(CauseKind.Biology, "해열제를 너무 많이 먹어 간이 상했다", sim.Tick, b.Cell, b.DrugCause); }
    }

    /// <summary>약의 몸속 변화 (순수 — 시험이 바로 돌린다): 몸에서 빠지고 · 내성 · 의존 · 금단 · 간 · 해독제.</summary>
    public static void DrugKinetics(ref Body b, double dt)
    {
        double h = dt / 3600, slow = 1 + 2 * b.Liver;
        if (b.Opioid > 0.01f) b.Opioid = (float)(b.Opioid * Math.Exp(-0.693 * h / (OpioidHalfH * slow))); else b.Opioid = 0;
        if (b.Apap > 0.5f) b.Apap = (float)(b.Apap * Math.Exp(-0.693 * h / (ApapHalfH * slow))); else b.Apap = 0;
        b.ApapDay = (float)(b.ApapDay * Math.Exp(-dt / 86400));
        // 내성 · 의존 (진통제)
        double eff = OpioidEffect(b);
        b.OpioidTol = (float)(b.OpioidTol * Math.Exp(-dt / (7 * 86400)) + eff / 10 * dt / 86400);   // 효과 10mg를 하루 내내면 1
        b.OpioidDep = (float)Math.Max(b.OpioidDep * Math.Exp(-dt / (10 * 86400)), 0.5 * b.OpioidTol);
        if (eff < 2 && b.OpioidDep > 0.3f) b.Pain = (float)Math.Max(b.Pain, Math.Min(0.6, 0.8 * b.OpioidDep));   // 금단 — 의존만큼 아프다 (마음이 다시 약을 찾는다)
        // 간: 24시간치가 6g을 넘으면 상하고 · 일주일에 다시 자란다
        if (b.ApapDay > ApapSafeMg) b.Liver = (float)Math.Min(1.2, b.Liver + (b.ApapDay - ApapSafeMg) / ApapSafeMg * dt / 43200);   // 24시간치 12g이면 반나절에 1
        else if (b.Liver > 0) b.Liver = (float)(b.Liver * Math.Exp(-dt / (7 * 86400)));
        if (b.Liver > b.LiverPeak) b.LiverPeak = b.Liver;
        if (b.Antidote > 0) b.Antidote = (float)Math.Max(0, b.Antidote - h);
    }

    /// <summary>아드레날린이 도는 동안 다시 뛸 몫 1.5배.</summary>
    private static double EpiBoost(Sim sim, in Body b) => sim.Tick - b.EpiAt < Time.Minutes(EpiMinutes) ? 1.5 : 1;
}
