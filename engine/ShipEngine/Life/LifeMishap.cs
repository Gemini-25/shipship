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

    /// <summary>마려움 (0~1 — 1이면 참지 못한다): 방광 · 장 가운데 더 찬 쪽.</summary>
    public float Urge(int body)
    {
        if (body < 0 || body >= Bodies.Count) return 0;
        ref var b = ref this[body];
        return (float)Math.Max(b.Bladder / BladderMax, b.Bowel / BowelMax);
    }
}
