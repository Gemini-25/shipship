namespace ShipEngine;

/// <summary>
/// ⑧ 고립의 피로 (깊이 바퀴 3) — 닫힌 배에서 같은 얼굴 · 같은 벽 · 같은 일과가 이어지면 몇 주에 걸쳐 쌓인다
/// (남극 겨울나기 증후군: 짜증 · 얕은 잠 · 무기력 · 반쯤 지나서 가장 처지는 '3분기 현상' — Bechtel &amp; Berning 1991 · 화성-500).
/// <para><b>쌓임</b> (깨어 있는 시간마다 · 다 찰수록 느리게): 25일에 1 − 1/e 꼴 × (0.6 + 0.8 × 신경성) × (0.7 + 0.6 × 열림 — 새것을 바라는 이는 단조로움에 더) ×
/// 붐빔 (곁에 둘 넘게 있으면 한 사람마다 +10% · 많아야 +50%) × 얕은 잠 (1 + 잠의 모자람) · 도착이 사흘 안이면 반 (끝이 보인다).</para>
/// <para><b>풀림</b>: 쉼 · 어울림 · 운동 중 시간당 0.004 (보통 사람은 0.7 언저리에서 멈춘다) · 반가운 소식 (모두 0.08) · 다른 배와 닿음 (0.1) · 도착 점화 (0.4).</para>
/// <para><b>효과</b> (⑨이 읽는다): 지친 둘의 잡담은 더 부딪치고 · 원망을 더 쉽게 꺼내고 · 화를 덜 참는다.</para>
/// </summary>
public sealed partial class Mind
{
    public const double WearyDays = 25;
    private long _wearyCursor;
    /// <summary>고립의 피로가 0.6을 넘은 사람이 생긴 번 (관찰용).</summary>
    public long WornOut;

    private void WearyNeeds(Sim sim, Agent a, double h, bool asleep, int others)
    {
        ref var c = ref a.C;
        if (asleep) return;
        _out ??= sim.Find<Outside>();
        double w0 = c.Weary;
        bool easing = c.Goal is GoalKind.Relax or GoalKind.Socialize or GoalKind.Exercise && c.StepIndex < a.Plan.Count && a.Plan[c.StepIndex].Kind == StepKind.Use;
        double crowd = 1 + Math.Min(0.5, 0.1 * Math.Max(0, others - 2));
        long now = sim.Tick;
        double end = _out != null && _out.Plan.Exists(p => p.Kind == BurnKind.Arrive && p.Due - now < Time.Hours(72)) ? 0.5 : 1;
        double grow = h / (16 * WearyDays);   // 깨어 있는 16시간이 하루
        grow *= (0.6 + 0.8 * c.Neuro) * (0.7 + 0.6 * c.Open) * crowd * (1 + c.SleepLoss) * end * (1 - c.Weary);
        double ease = easing ? 0.004 * h : 0;
        c.Weary = (float)Math.Clamp(c.Weary + grow - ease, 0, 1);
        if (w0 < 0.6 && c.Weary >= 0.6) WornOut++;
    }

    /// <summary>모두의 피로를 덜어 주는 일 (사실 흐름에서 · 25틱마다).</summary>
    private void WearyStep(Sim sim)
    {
        foreach (var f in sim.Facts.Since(_wearyCursor))
        {
            float k = f.Kind switch
            {
                FactKind.Contact => 0.1f,
                FactKind.Burn when f.Other == (int)BurnKind.Arrive => 0.4f,
                _ => 0,
            };
            if (k <= 0) continue;
            foreach (var a in Agents) if (a.IsPerson) a.C.Weary = Math.Max(0, a.C.Weary - k);
        }
        _wearyCursor = sim.Facts.Total;
    }

    private void SaveWeary(StateWriter w) { w.I64(_wearyCursor); w.I64(WornOut); }
    private void LoadWeary(StateReader r) { _wearyCursor = r.I64(); WornOut = r.I64(); }
}
