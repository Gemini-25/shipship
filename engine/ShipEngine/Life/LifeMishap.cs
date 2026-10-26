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

    /// <summary>손에서 놓칠 바탕 몫 (5단 C10 — 한 번 마실 때 컵 · 냄비를 내릴 때 · 기능 확인용 기본값).</summary>
    public double CupFumble = 0.004, PanFumble = 0.003;

    /// <summary>쏟은 수 (셈 — 찍지 않는다).</summary>
    public long Spills;

    /// <summary>
    /// ⑧의 요청 (5단 C10 — 마시기 요청 길): 그 몸이 설비 (furniture)에서 물을 want몰 마시려 한다 — 월드가 물탱크에서 빼고 (망이 없으면 그대로) ·
    /// 놓칠 몫 = 컵 0.004 × (1 + 졸림)(1 + 서두름)(1 + 흔들림) × (1 − 0.5 × 조심)이면 40~100%를 그 칸 바닥에 쏟는다 (낮은 중력이면 장이 방울로 띄운다) ·
    /// 장부 '마셨다' · '쏟았다' · 사실 '쏟았다' · 원인 '○○이 컵을 쏟았다 — 졸려서'. 마신 몰 · 쏟은 몰 · 원인을 돌려준다.
    /// </summary>
    public (double drunk, double spilled, int cause) Drink(Sim sim, int body, int furniture, double want, float sleepy, float hasty, float careful, int actor, string name)
    {
        _net ??= sim.Find<Network>();
        if (body < 0 || body >= Bodies.Count || want <= 0) return (0, 0, 0);
        Touch(body, furniture);   // 꼭지 · 컵을 만진다 (5단 C21)
        double got = _net != null ? Math.Min(Math.Max(0, _net.WaterTank), want) : want;
        if (_net != null) _net.WaterTank -= got;
        ref var b = ref this[body];
        double odds = CupFumble * MishapOdds(b, sleepy, hasty) * (1 - 0.5 * Math.Clamp(careful, 0, 1));
        double spilled = 0; int cause = 0;
        if (got > 0 && b.Cell >= 0 && Rng.Unit(sim.Seed, Rng.Key("쏟음"), sim.Tick, body) < odds)   // 칸 밖의 몸 (−1)은 쏟을 바닥이 없다
        {
            spilled = got * (0.4 + 0.6 * Rng.Unit(sim.Seed, Rng.Key("쏟은 몫"), sim.Tick, body));
            string why = MishapWhy(b, sleepy);
            int cell = b.Cell;
            cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(name)} 컵을 쏟았다{(why.Length > 0 ? " — " + why : "")}", sim.Tick, cell, 0, 0, actor);
            F.AddStore(cell, T.sWater, spilled, cause);
            if (_net != null) sim.Book.Add(Res.Water, Flow.Spilled, -spilled);
            sim.Facts.Emit(sim.Tick, FactKind.Spilled, cell, (float)spilled, cause, actor: body, other: furniture);
            Spills++;
        }
        double drunk = got - spilled;
        if (_net != null) sim.Book.Add(Res.Water, Flow.Drunk, -drunk);
        Drink(body, drunk);
        return (drunk, spilled, cause);
    }

    /// <summary>
    /// ⑧의 요청 (5단 C10): 그 몸이 조리대 (device)의 냄비를 내린다 — 놓칠 몫 = 냄비 0.003 × (1 + 졸림)(1 + 서두름)(1 + 흔들림) × (1 − 0.5 × 조심)이면
    /// 냄비 물 40~100%를 ⑥에 쏟게 하고 (바닥 · 겉면 열) · 330K 넘는 냄비면 쏟은 이가 덴다.
    /// </summary>
    public void HandlePan(Sim sim, int body, int device, float sleepy, float hasty, float careful, int actor, string name)
    {
        _net ??= sim.Find<Network>();
        if (_net == null || body < 0 || body >= Bodies.Count || device < 0 || device >= _net.Devices.Count || _net.PanWater[device] <= 0) return;
        ref var b = ref this[body];
        double odds = PanFumble * MishapOdds(b, sleepy, hasty) * (1 - 0.5 * Math.Clamp(careful, 0, 1));
        if (Rng.Unit(sim.Seed, Rng.Key("냄비 쏟음"), sim.Tick, body) >= odds) return;
        double share = 0.4 + 0.6 * Rng.Unit(sim.Seed, Rng.Key("냄비 쏟은 몫"), sim.Tick, body);
        string why = MishapWhy(b, sleepy);
        var dev = _net.Devices[device];
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(name)} 냄비를 쏟았다{(why.Length > 0 ? " — " + why : "")}", sim.Tick, dev.Cell, 0, 0, actor);
        var (mol, panK) = _net.SpillPan(sim, device, share, cause);
        sim.Facts.Emit(sim.Tick, FactKind.Spilled, dev.Cell, (float)mol, cause, actor: body, other: dev.Furniture);   // 설비는 가구 번호 (컵과 같게)
        Spills++;
        if (panK > 330) Scald(sim, body, (float)Math.Min(0.3, (panK - 330) / 150), cause);   // 뜨거운 물이 손 · 발에 (80℃ 0.15)
    }

    /// <summary>젖은 바닥 미끄러짐 (5단 C12 — 기능 확인용 기본값): 바탕 몫 · 걸음당 상한 (셈은 <see cref="Slips"/>).</summary>
    public double FloorSlipChance = 0.02, FloorSlipMax = 0.05;

    /// <summary>
    /// ⑧의 요청 (5단 C12 — 사람이 칸에 든다): 젖은 바닥에 미끄러지나 — 몫 = 0.02 × 젖음 (바닥 물 5 → 30몰에 0 → 1) × (빠르기 / 1.3)² × 든 것 1.5 × 조심 0.25 ×
    /// (1 + 졸림)(1 + 흔들림) · 걸음당 상한 0.05 · 0.3g 아래는 0. 미끄러지면 넘어짐은 <see cref="Fall"/> (세기 = 그 칸 중력 · 원인 '젖은 바닥에 미끄러져' ← 그 칸 물의 원인) — 그 원인을 돌려준다 (아니면 0).
    /// </summary>
    public int Footing(Sim sim, int body, int cell, double speed, bool laden, bool careful, float sleepy, int actor, string name, int carried = -1)
    {
        if (body < 0 || body >= Bodies.Count || cell < 0 || cell >= F.Cells) return 0;
        double water = F.Stored(T.sWater, cell);
        if (water <= 5) return 0;   // 마른 칸 (거의 모두)
        float g = F.Grav[cell];
        if (g < Field.LowG || this[body].Alive == 0) return 0;
        double wet = Math.Min(1, (water - 5) / 25), v = speed / 1.3;
        double odds = Math.Min(FloorSlipMax, FloorSlipChance * wet * v * v * (laden ? 1.5 : 1) * (careful ? 0.25 : 1) * MishapOdds(this[body], sleepy, 0));
        if (Rng.Unit(sim.Seed, Rng.Key("미끄러짐"), sim.Tick, (long)body * F.Cells + cell) >= odds) return 0;   // 한 틱에 여러 칸을 걷는다 — 몸 · 칸마다
        Slips++;
        return Fall(sim, body, g, "젖은 바닥에 미끄러져", F.WetCause[cell], actor, name, actor, false, carried);
    }

    /// <summary>마려움 (0~1 — 1이면 참지 못한다): 방광 · 장 가운데 더 찬 쪽.</summary>
    public float Urge(int body)
    {
        if (body < 0 || body >= Bodies.Count) return 0;
        ref var b = ref this[body];
        return (float)Math.Max(b.Bladder / BladderMax, b.Bowel / BowelMax);
    }
}
