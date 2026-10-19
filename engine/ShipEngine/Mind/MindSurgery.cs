namespace ShipEngine;

/// <summary>
/// ⑦ (라-1) 수술 — 주컴퓨터가 진료 기록(속 출혈 · 맹장염)을 읽고 맡긴다 (로봇이 먼저 · 없으면 사람에게 부탁).
/// 다친 이가 치료 침대에 눕기를 기다리고 (쓰러졌으면 업어 온다) → 마취 (진통제 30mg × (1 + 내성)에서 몸에 남은 것을 뺀 만큼 — 없으면 마취 없이) → 한 시간 × (1.5 − 솜씨) →
/// 잘될 몫 0.45 + 0.5 × 솜씨 (+0.1 진단 장치 · −0.2 피 3.5L 아래) — 못 되면 합병증 (출혈 · 감염 — 소독기가 있으면 감염이 반).
/// </summary>
public sealed partial class Mind
{
    public long Surgeries, SurgeryOk, SurgeryBad, NoAnesthesia;
    private int _sterile = -1;

    /// <summary>그 몸을 곁에서 수술하고 있나 (주컴퓨터가 수술대 위에서 정신을 잃은 이를 '쓰러진 사람'으로 부르지 않게).</summary>
    public bool UnderSurgery(int body)
    {
        foreach (var o in Agents) if (o.C.Up != 0 && o.C.Goal == GoalKind.Surgery && o.C.GoalTarget == body && _life != null && body < _life.Bodies.Count && Dist(o.C.Cell, _life.Bodies[body].Cell) <= 1) return true;
        return false;
    }

    /// <summary>수술 솜씨: 로봇은 주컴퓨터의 수술 절차 (0.6) · 사람은 의료 솜씨.</summary>
    public static float SurgeonSkill(Agent a) => a.IsPerson ? a.C.SkMedic : 0.6f;

    /// <summary>배에 소독기가 있나 (수술 도구를 끓여 소독한다).</summary>
    private bool Sterile => (_sterile < 0 ? _sterile = L.Furniture.Any(fu => fu.Type == "Autoclave") ? 1 : 0 : _sterile) == 1;

    /// <summary>그 몸이 누울 치료 침대 칸: 이미 누웠으면 그 칸 · 아니면 그 사람이 고를 빈 침대 · 없으면 가장 가까운 침대.</summary>
    private int SurgeryBed(int vb)
    {
        var body = _life!.Bodies[vb];
        if (IsMedBedCell(body.Cell)) return body.Cell;
        int pa = AgentOfBody(vb);
        int bed = pa >= 0 ? NearestFree(Agents[pa], _medBeds) : -1;
        if (bed >= 0) return bed;
        int bd = int.MaxValue;
        foreach (int fu in _medBeds) { int c = L.Furniture[fu].Cells[0], d = Manhattan(body.Cell, c); if (d < bd) { bd = d; bed = c; } }
        return bed;
    }

    /// <summary>수술 계획: 쓰러졌으면 업어 치료 침대에 눕히고 · 깨어 있으면 침대 곁에서 기다린다 · 수술 (한 시간 × (1.5 − 솜씨)).</summary>
    private bool BuildSurgery(Agent a, Option o, List<PlanStep> plan, int here)
    {
        ref var c = ref a.C;
        int vb = o.Target;
        if (vb < 0 || vb >= _life!.Bodies.Count || vb == c.Body || c.Carrying >= 0) return false;
        var body = _life.Bodies[vb];
        if (body.Alive == 0) return false;
        int bed = SurgeryBed(vb);
        if (bed < 0) return false;
        if (body.Conscious == 0 && body.Cell != bed)
        {
            plan.Add(Go(body.Cell, 1));
            plan.Add(new PlanStep { Kind = StepKind.PickUp, Target = vb, Arg = 1 });   // 안전한 곳이어도 옮긴다 (수술하러)
            plan.Add(Go(bed));
            plan.Add(new PlanStep { Kind = StepKind.Drop });
        }
        else plan.Add(Go(bed, 1));
        plan.Add(Use(UseKind.Surgery, (long)(Time.Hours(1) * (1.5 - SurgeonSkill(a))), bed, vb));
        return true;
    }

    /// <summary>수술 중 (틱마다): 다친 이가 침대에 누울 때까지 기다리고 (한 시간 넘게 오지 않으면 그만둔다) · 누우면 마취 (한 번) · 마취가 없으면 아프다.
    /// 1 마쳤다 (숨졌다) · 0 이어 한다 · −1 그만두었다.</summary>
    private int SurgeryTick(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        int vb = st.Target;
        if (vb < 0 || vb >= _life!.Bodies.Count) return 1;
        ref var body = ref _life[vb];
        if (body.Alive == 0) return 1;
        if (body.Cell != st.Cell && Dist(body.Cell, st.Cell) > 0 || Dist(c.Cell, body.Cell) > 1)
        {
            c.StepStart = sim.Tick;   // 누운 때부터 센다
            if (sim.Tick - c.GoalSince > Time.Hours(1)) { EndPlan(a); return -1; }
            return 0;
        }
        if (c.OpAnesAt < c.GoalSince)
        {
            c.OpAnesAt = sim.Tick;
            var kind = a.IsPerson ? CauseKind.Person : CauseKind.Robot;
            int pa = AgentOfBody(vb);
            string who = pa >= 0 ? NameOf(pa) : "다친 이";
            double mg = Math.Round(30 * (1 + body.OpioidTol) - body.Opioid);   // 몸에 남은 진통제만큼 덜 (앞서 맞은 것 · 다시 잡은 수술)
            if (mg < 5) { }   // 이미 마취가 되어 있다
            else if (TakeOne(c.Cell, iOpioid, sim))
            {
                int k = sim.Causes.Add(kind, $"{Ko.I(NameOf(a.Id))} {who}에게 마취를 했다 (진통제 {mg:0}mg)", sim.Tick, c.Cell, body.HurtCause, 0, a.Id);
                _life.Give(sim, vb, Drug.Opioid, mg, k);
            }
            else
            {
                NoAnesthesia++;
                int k = sim.Causes.Add(kind, $"약품고에 진통제가 없어 {Ko.I(NameOf(a.Id))} 마취 없이 {who}의 배를 열었다", sim.Tick, c.Cell, body.HurtCause, 0, a.Id);
                body.HurtCause = k;
                if (Rng.Unit(sim.Seed, Rng.Key("마취 없는 수술"), sim.Tick, vb) < 0.1) _life.StopHeart(sim, vb, 1, "마취 없이 수술하다 심장이 떨린다", k);
            }
        }
        if (Life.OpioidEffect(body) < 10) body.Pain = 1;   // 마취가 없거나 다 풀렸다
        return body.Alive == 0 ? 1 : 0;
    }

    /// <summary>수술을 마쳤다 — 잘됐나 · 합병증 · 꿰맨 상처 · 항생제 하루 · 진료 기록 (수술 뒤 쉼) · 솜씨 +0.02.</summary>
    private void SurgeryDone(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        int vb = st.Target;
        if (vb < 0 || vb >= _life!.Bodies.Count) return;
        var body = _life.Bodies[vb];
        if (body.Alive == 0) return;
        int pa = AgentOfBody(vb);
        string who = pa >= 0 ? NameOf(pa) : "다친 이";
        Ail seen = pa >= 0 ? Agents[pa].C.DxAil : Life.Ailment(body);
        Op op = seen == Ail.InternalBleed && body.BleedIn > 0 ? Op.Bleed : seen == Ail.Appendicitis && body.Appendix > 0 ? Op.Appendix : Op.Explore;
        float skill = SurgeonSkill(a);
        int room = RoomOf(body.Cell);
        bool scan = room >= 0 && (_roomScan ??= RoomsWith("DiagnosticScanner"))[room];
        double p = 0.45 + 0.5 * skill + (scan ? 0.1 : 0) - (body.BloodL < 3.5f ? 0.2 : 0);
        double u = Rng.Unit(sim.Seed, Rng.Key("수술"), sim.Tick, vb * 64 + a.Id);
        bool ok = op != Op.Explore && u < p, bleed = Rng.Unit(sim.Seed, Rng.Key("합병증"), sim.Tick, vb) < 0.5;
        string name = op == Op.Bleed ? "속 출혈" : op == Op.Appendix ? "맹장" : Life.AilName(seen);
        string me = Ko.I(NameOf(a.Id));
        string text = op == Op.Explore ? $"{me} {who}의 배를 열어 보았다 — {Ko.Euro(Life.AilName(seen))} 보았으나 고칠 것이 없었다"
            : ok ? $"{me} {who}의 {name} 수술을 했다 — 잘됐다"
            : $"{me} {who}의 {name} 수술을 했다 — {(bleed ? "피를 많이 흘렸다" : "수술 자리가 곪았다")}{(skill < 0.5f ? " (솜씨가 모자랐다)" : "")}";
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, text, sim.Tick, body.Cell, pa >= 0 ? Agents[pa].C.DxCause : body.HurtCause, body.HurtCause, a.Id);
        _life.Operate(sim, vb, op, ok, bleed, Sterile, cause);
        Surgeries++; if (ok) SurgeryOk++; else if (op != Op.Explore) SurgeryBad++;
        if (_life.Bodies[vb].Abx <= 0 && TakeOne(c.Cell, iAbx, sim)) _life.Give(sim, vb, Drug.Abx, 24, cause);   // 수술 뒤 항생제 하루
        if (pa >= 0)
        {
            ref var pc = ref Agents[pa].C;
            pc.DxAil = Ail.Bruise; pc.DxAt = sim.Tick; pc.DxBy = a.Id; pc.DxCause = cause; pc.DxSym = Life.Symptoms(_life.Bodies[vb]); pc.DxAwake = _life.Bodies[vb].Conscious;   // 수술 뒤 쉼
        }
        sim.Facts.Emit(sim.Tick, FactKind.Surgery, body.Cell, ok ? 1 : 0, cause, a.Id, pa);
        if (a.IsPerson) c.SkMedic = Math.Min(1, c.SkMedic + 0.02f);
        if (c.AssignedGoal == GoalKind.Surgery && c.AssignedTarget == vb) c.AssignedUntil = sim.Tick;
    }
}
