namespace ShipEngine;

/// <summary>
/// 몸의 사고 (월드 — 5단 A5): 넘어진 몸 · 마려움. 넘어질지 · 언제 갈지는 사람의 판단과 세상의 확률 (⑧ Inertia · BodyJolt · Floating · MindWash) —
/// 여기는 넘어진 몸이 어떻게 다치는지 · 몸이 얼마나 마려운지만 셈한다.
/// </summary>
public sealed partial class Life
{
    /// <summary>
    /// ⑧의 요청: 선 사람이 세기 g로 넘어졌다 (how — "점화에" · "덜컹임에" …) — 타박 (5% + 20% × g × 운) · 뼈 (3% + (g − 0.2) × 20% · 무중력에 약해진 뼈는 4%를 잃으면 두 배) ·
    /// 업혀 있던 이(carried — 몸 번호)도 바닥에 (타박 0.1) · 사실 '넘어졌다'. 난수는 '넘어져 다침' · '뼈' (틱 · rngId). 원인 마디를 돌려준다.
    /// </summary>
    public int Fall(Sim sim, int body, double g, string how, int parent, int actor, string name, int rngId, bool showG = true, int carried = -1)
    {
        if (body < 0 || body >= Bodies.Count) return 0;
        long t = sim.Tick;
        int cell = this[body].Cell;
        double u = Rng.Unit(sim.Seed, Rng.Key("넘어져 다침"), t, rngId);
        float boneLoss = this[body].BoneLoss;   // 무중력에서 약해진 뼈는 잘 부러진다 (4%를 잃으면 두 배)
        bool bone = Rng.Unit(sim.Seed, Rng.Key("뼈"), t, rngId) < (0.03 + 0.2 * Math.Max(0, g - 0.2)) * (boneLoss > 0 ? 1 + boneLoss / 0.04 : 1);
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(name)} {how} 넘어졌다{(showG ? $" ({g:0.00}g)" : "")}{(bone ? boneLoss > 0.02f ? " — 무중력에 약해진 뼈가 부러졌다" : " — 뼈를 다쳤다" : "")}", t, cell, parent, 0, actor);
        float blunt = (float)(0.05 + 0.2 * g * u + (bone ? 0.35 : 0));
        if (carried >= 0) Strike(sim, carried, 0.1f, 0, cause);   // 업혀 있던 이도 바닥에
        Strike(sim, body, blunt, 0, cause);
        sim.Facts.Emit(t, FactKind.Fell, cell, (float)g, cause, actor);
        return cause;
    }

    // ───────────── 흔들림 (5단 C9) ─────────────

    /// <summary>
    /// 몸의 흔들림 (0~2 — 손 · 발이 무딘 정도): 약 기운 × 1 + 아픔 × 0.6 + 숨 (산소 부족 − 0.05 · 일산화탄소 − 0.1) × 2 + 메스꺼움 × 0.5 + 떨림 (심부 35.5℃ 아래 1.5도에 다 차서) × 0.5.
    /// </summary>
    public static double Unsteady(in Body b) => Math.Clamp(UnsteadyParts(b).Sum, 0, 2);
    public double Unsteady(int body) => body >= 0 && body < Bodies.Count ? Unsteady(this[body]) : 0;

    private static (double Drug, double Pain, double Breath, double Sick, double Shiver, double Sum) UnsteadyParts(in Body b)
    {
        double drug = Sedation(b), pain = 0.6 * b.Pain, breath = 2 * (Math.Max(0, b.Hypoxia - 0.05) + Math.Max(0, b.COHb - 0.1)), sick = 0.5 * b.Nausea;
        double shiver = b.CoreK < 308.65 ? 0.5 * Math.Min(1, (308.65 - b.CoreK) / 1.5) : 0;
        return (drug, pain, breath, sick, shiver, drug + pain + breath + sick + shiver);
    }

    /// <summary>손이 미끄러질 몫의 배수 (순수 — 세상의 다른 몫에 곱한다): (1 + 졸림)(1 + 서두름)(1 + 흔들림).</summary>
    public static double MishapOdds(in Body b, double sleepy, double hasty) => (1 + sleepy) * (1 + hasty) * (1 + Unsteady(b));

    /// <summary>손이 무딘 까닭 — 몫이 가장 큰 것 (흔들림 갈래 · 졸림): '약 기운에' · '아파서' · '숨이 차' · '졸려서' · '메스꺼워' · '몸이 떨려' (모두 작으면 빈 글).</summary>
    public static string MishapWhy(in Body b, double sleepy)
    {
        var u = UnsteadyParts(b);
        (double v, string t)[] all = { (u.Drug, "약 기운에"), (u.Pain, "아파서"), (u.Breath, "숨이 차"), (sleepy, "졸려서"), (u.Sick, "메스꺼워"), (u.Shiver, "몸이 떨려") };
        var top = all[0];
        foreach (var x in all) if (x.v > top.v) top = x;
        return top.v >= 0.1 ? top.t : "";
    }

    /// <summary>
    /// ⑧의 요청 (5단 C9 — 정비 손상 요청 하나로): 사람이 장치의 한 부품을 손봤다 — 생명이 그 몸의 흔들림을 셈해 ⑥ 망에 '다른 부품을 상하게 했나'를 묻고 (Handled) ·
    /// 다쳤는지 (WorkHazard)를 셈한다. 마음은 몸 값을 다루지 않는다. 가장 큰 몫이 원인 글의 까닭이 된다.
    /// </summary>
    public void HandsOn(Sim sim, Network net, int body, int device, Part part, float skill, float sleepy, float hasty, int actor, string name, int workCause, int cell, double hotK, string devName)
    {
        if (body < 0 || body >= Bodies.Count || this[body].Alive == 0) return;
        double u = Unsteady(this[body]);
        string why = MishapWhy(this[body], sleepy);
        net.Handled(sim, device, part, skill, sleepy, hasty, actor, name, workCause, u, why);
        WorkHazard(sim, body, cell, hotK, skill, sleepy, hasty, actor, name, devName, workCause, u, why);
    }

    /// <summary>마려움 (0~1 — 1이면 참지 못한다): 방광 · 장 가운데 더 찬 쪽.</summary>
    public float Urge(int body)
    {
        if (body < 0 || body >= Bodies.Count) return 0;
        ref var b = ref this[body];
        return (float)Math.Max(b.Bladder / BladderMax, b.Bowel / BowelMax);
    }
}
