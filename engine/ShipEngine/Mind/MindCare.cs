namespace ShipEngine;

/// <summary>
/// ⑦ → ⑧ 돌봄 · 앓음 · 잠의 질 · 몸무게 (깊이 바퀴 2).
/// <para><b>돌보기</b> (맡은 일 — 주컴퓨터의 '다친 사람' 사건 · 로봇이 먼저): 구급 상자를 꺼내 들고 다친 이 곁에서 5분 — 붕대 · 화상 덮개 · 항생제 (⑦ Life.Treat) ·
/// 상자가 없으면 맨손으로 (피만 멈추고 이틀 덮는다). 다친 이가 멀어지면 다시 간다. 사실 Treated — 돌봄을 받은 이는 빚을 진다 (⑨).</para>
/// <para><b>앓음</b>: 앓는 이는 쉽게 졸리고(1.5배) · 느리다(0.85배) · 덜 성실할수록 일을 놓고 눕는다 (성실한 이는 무리해 일하다 더 옮긴다).</para>
/// <para><b>잠의 질</b>: 잠자리의 소리(밤 40dB 넘으면 — WHO 밤 소음 기준) · 빛(30lx 넘으면) · 온도(18~26℃ 밖) · 아픔 · 열 · 긴장이 잠을 얕게 한다 —
/// 졸림이 (0.4 + 0.6 × 질)만큼만 풀린다 (SleepLoss = 1 − 질).</para>
/// <para><b>몸무게</b>: 처음의 10%를 넘게 잃으면 기운이 빠진다 — 빨리 지치고(졸림) · 느리다 (20%면 0.85배) — ⑪ 배급과 이어진다.</para>
/// </summary>
public sealed partial class Mind
{
    public long Treats, TreatsBare;

    /// <summary>돌보기 계획: 구급 상자를 들고(없으면 맨손) 다친 이 곁으로 · 5분.</summary>
    private bool BuildTreat(Agent a, Option o, List<PlanStep> plan, int here)
    {
        ref var c = ref a.C;
        int vb = o.Target;
        if (vb < 0 || vb >= _life!.Bodies.Count || _life.Bodies[vb].Alive == 0 || vb == c.Body) return false;
        if (c.Holding != iMedkit && c.Holding == 255 && Life.NeedsDressing(_life.Bodies[vb]))   // 약만 드는 몸에는 구급 상자를 쓰지 않는다
        {
            int src = NearestStock(here, iMedkit);
            if (src >= 0)
            {
                plan.Add(Go(L.Furniture[src].Cells[0], 1));
                plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = iMedkit });
            }
        }
        int cell = _life.Bodies[vb].Cell;
        plan.Add(Go(cell, 1));
        plan.Add(Use(UseKind.Treat, Time.Minutes(5), cell, vb));
        return true;
    }

    /// <summary>돌보는 중: 다친 이가 2칸 넘게 멀어지면 다시 간다.</summary>
    private bool TreatNear(Agent a, PlanStep st) => st.Target >= 0 && st.Target < _life!.Bodies.Count && Dist(a.C.Cell, _life.Bodies[st.Target].Cell) <= 2;

    /// <summary>돌보기를 마쳤다.</summary>
    private void TreatDone(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        int vb = st.Target;
        if (vb < 0 || vb >= _life!.Bodies.Count || _life.Bodies[vb].Alive == 0) return;
        bool kit = c.Holding == iMedkit, dress = Life.NeedsDressing(_life.Bodies[vb]);
        double pain = Life.FeltPain(_life.Bodies[vb]);   // 와서 본 아픔 (덮기 전)
        _life.Treat(sim, vb, kit, abxDays: 0);
        if (kit && dress) UseHeld(sim, a); else if (dress) TreatsBare++;
        Treats++;
        if (a.IsPerson) c.SkMedic = Math.Min(1, c.SkMedic + 0.004f * Practice(c, "치료") * UseSkill(a, 1, sim.Tick));
        int va = AgentOfBody(vb);
        string who = va >= 0 ? NameOf(va) : "다친 이";
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot,
            !dress ? $"{Ko.I(NameOf(a.Id))} {Ko.Eul(who)} 살폈다" : kit ? $"{Ko.I(NameOf(a.Id))} {Ko.Eul(who)} 돌봤다 — 붕대 · 화상 덮개" : $"{Ko.I(NameOf(a.Id))} 맨손으로 {Ko.Eul(who)} 돌봤다 — 구급 상자가 없었다",
            sim.Tick, c.Cell, _life.Bodies[vb].HurtCause, 0, a.Id);
        sim.Facts.Emit(sim.Tick, FactKind.Treated, c.Cell, kit ? 1 : 0, cause, a.Id, va);
        Dispense(sim, a, vb, who, pain, cause);
        if (c.AssignedGoal == GoalKind.Treat && c.AssignedTarget == vb) c.AssignedUntil = sim.Tick;   // 맡은 일을 마쳤다
    }

    // ── ⑦ 바퀴 3 (나): 약 — 돌보는 이가 약품고(의료 침대)의 약을 준다 ──
    public long PainDoses, DoubleDoses, MissedHours, FeverDoses, OverDay, AbxCourses, AbxShort, Antidotes, EpiDoses;

    /// <summary>그 몸에 줄 약이 약품고에 있나 (주컴퓨터가 '다친 사람'을 알릴지 — 없는 약 때문에 되풀이해 부르지 않는다).</summary>
    public bool HasDrugFor(in Body b) => Life.NeedsAntidote(b) && TotalOf(iAntidote) > 0 || Life.NeedsPainkiller(b) && TotalOf(iOpioid) > 0;

    /// <summary>
    /// 돌보며 약을 준다 (약품고 — 가장 가까운 의료 침대에서). 솜씨 0.5 넘는 이 · 로봇은 셈을 하고 · 서툰 이는 실수한다:
    /// 아픔 0.4 넘으면 진통제 — 10mg × (1 + 내성) · 4시간 안에 준 적이 있으면 주지 않는다 / 서툰 이는 늘 10mg · 열에 둘은 두 배 · 4시간을 세지 않는다.
    /// 열 1℃ 넘으면 해열제 1g — 하루 4g까지 / 서툰 이는 세지 않는다. 곪았거나 크게 데면 항생제 사흘 한 벌 — 약품고에 모자라면 있는 만큼 (내성균의 씨앗).
    /// 시안화수소 중독이면 해독제. 약마다 원인 마디 (돌봄 아래) — 과용 · 간 · 내성균의 사슬이 여기서 시작한다.
    /// </summary>
    private void Dispense(Sim sim, Agent a, int vb, string who, double pain, int parent)
    {
        ref var c = ref a.C;
        var b = _life!.Bodies[vb];
        bool skilled = !a.IsPerson || c.SkMedic >= 0.5f;
        var kind = a.IsPerson ? CauseKind.Person : CauseKind.Robot;
        string me = Ko.I(NameOf(a.Id));
        // 진통제
        bool recent = sim.Tick - b.OpioidAt < Time.Hours(4);
        if (pain > 0.4 && (!skilled || !recent) && TakeOne(c.Cell, iOpioid, sim))
        {
            double mg = skilled ? Math.Round(10 * (1 + b.OpioidTol)) : 10;
            bool twice = !skilled && Rng.Unit(sim.Seed, Rng.Key("진통제 두 배"), sim.Tick, a.Id) < 0.2;
            if (twice) { mg *= 2; DoubleDoses++; }
            if (!skilled && recent) MissedHours++;
            string note = twice ? " — 서툴러 두 배를 놓았다" : !skilled && recent ? " — 네 시간 안에 놓은 것을 몰랐다" : skilled && b.OpioidTol > 0.3f ? " — 내성만큼 늘렸다" : "";
            int k = sim.Causes.Add(kind, $"{me} {who}에게 진통제 {mg:0}mg을 놓았다{note}", sim.Tick, c.Cell, parent, 0, a.Id);
            _life.Give(sim, vb, Drug.Opioid, mg, k); PainDoses++;
        }
        // 해열제
        b = _life.Bodies[vb];
        bool over = b.ApapDay + 1000 > 4000;
        if (Life.FeverK(b) * (1 - Life.FeverCut(b)) > 1 && (!skilled || !over) && TakeOne(c.Cell, iApap, sim))
        {
            if (over) OverDay++;
            int k = sim.Causes.Add(kind, $"{me} {who}에게 해열제 1g을 먹였다{(over ? " — 하루 양을 세지 않았다" : "")}", sim.Tick, c.Cell, parent, 0, a.Id);
            _life.Give(sim, vb, Drug.Apap, 1000, k); FeverDoses++;
        }
        // 항생제 — 사흘 한 벌 (하루 한 알)
        if (Life.NeedsAbx(_life.Bodies[vb]))
        {
            int pills = 0;
            while (pills < 3 && TakeOne(c.Cell, iAbx, sim)) pills++;
            if (pills < 3) AbxShort++;
            if (pills > 0)
            {
                int k = sim.Causes.Add(kind, pills < 3 ? $"약품고에 항생제가 모자라 {me} {who}에게 {pills}일치만 먹였다" : $"{me} {who}에게 항생제 사흘 치를 먹였다",
                    sim.Tick, c.Cell, parent, 0, a.Id);
                _life.Give(sim, vb, Drug.Abx, 24.0 * pills, k); AbxCourses++;
            }
        }
        // 해독제
        if (Life.NeedsAntidote(_life.Bodies[vb]) && TakeOne(c.Cell, iAntidote, sim))
        {
            int k = sim.Causes.Add(kind, $"{me} {who}에게 해독제를 놓았다", sim.Tick, c.Cell, parent, 0, a.Id);
            _life.Give(sim, vb, Drug.Antidote, 1, k); Antidotes++;
        }
    }

    // ── ⑦ 바퀴 3: 멎은 심장 — 곁에서 가슴을 누르고 · 제세동기를 가져와 친다 ──
    public long CprStarts, AedShocks;

    /// <summary>그 몸에게 제세동기를 누가 이미 들고 있거나 가지러 갔나.</summary>
    private bool AedComing(Agent me, int vb)
    {
        foreach (var o in Agents)
        {
            if (o == me || o.C.Up == 0 || o.C.Goal != GoalKind.Cpr || o.C.GoalTarget != vb) continue;
            if (o.C.Holding == iAed) return true;
            for (int i = o.C.StepIndex; i < o.Plan.Count; i++) if (o.Plan[i].Kind == StepKind.Take && o.Plan[i].Arg == iAed) return true;
        }
        return false;
    }

    /// <summary>그 몸 곁에서 누가 이미 가슴을 누르나 (또는 누르러 가나).</summary>
    private bool Compressing(Agent me, int vb)
    {
        foreach (var o in Agents) if (o != me && o.C.Up != 0 && o.C.Goal == GoalKind.Cpr && o.C.GoalTarget == vb) return true;
        return false;
    }

    /// <summary>
    /// 가슴 압박 계획: 제세동기를 아무도 가져오지 않으면 (로봇은 늘 · 사람은 누가 이미 누를 때) 가까운 것을 들고 간다 ·
    /// 쓰러진 곳이 안전하지 않으면 먼저 업어 낸다 · 곁에서 누른다 (다시 뛰거나 숨지거나 40분).
    /// </summary>
    private bool BuildCpr(Agent a, Option o, List<PlanStep> plan, int here)
    {
        ref var c = ref a.C;
        int vb = o.Target;
        if (vb < 0 || vb >= _life!.Bodies.Count || vb == c.Body) return false;
        var body = _life.Bodies[vb];
        if (body.Alive == 0 || body.Arrest == 0 || c.Carrying >= 0) return false;
        if (c.Holding == 255 && !AedComing(a, vb) && (!a.IsPerson || Compressing(a, vb)))
        {
            int src = NearestStock(here, iAed);
            if (src >= 0 && Dist(here, L.Furniture[src].Cells[0]) <= 80)
            {
                plan.Add(Go(L.Furniture[src].Cells[0], 1));
                plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = iAed });
            }
        }
        if (!SafeAt(body.Cell) && c.Holding == 255 && plan.Count == 0)
        {
            plan.Add(Go(body.Cell, 1));
            plan.Add(new PlanStep { Kind = StepKind.PickUp, Target = vb });
            plan.Add(new PlanStep { Kind = StepKind.GoTo, Cell = -1, Arg = 0 });
            plan.Add(new PlanStep { Kind = StepKind.Drop });
        }
        else plan.Add(Go(body.Cell, 1));
        plan.Add(Use(UseKind.Cpr, Time.Minutes(40), body.Cell, vb));
        CprStarts++;
        return true;
    }

    /// <summary>가슴 압박 중 (틱마다): 곁이면 누르고 · 제세동기를 들었으면 2분마다 읽어 잔떨림이면 친다 (붙이고 읽는 데 10초).
    /// 1 마쳤다 (다시 뛴다 · 숨졌다) · 0 이어 누른다 · −1 쓰러진 이가 멀어졌다 (다시 계획한다 — 피하지 않는다).</summary>
    private int CprTick(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        int vb = st.Target;
        if (vb < 0 || vb >= _life!.Bodies.Count) return 1;
        var body = _life.Bodies[vb];
        if (body.Alive == 0 || body.Arrest == 0) return 1;
        if (Dist(c.Cell, body.Cell) > 1) { EndPlan(a); return -1; }
        _life.Compress(sim, vb);
        // ⑦ (나) 아드레날린 — 제세동기 가방에 든 것 (약품고에서) · 치기 앞서 · 4분마다 · 로봇 · 솜씨 0.5 넘는 이
        if (c.Holding == iAed && (!a.IsPerson || c.SkMedic >= 0.5f) && sim.Tick - c.StepStart >= 4
            && sim.Tick - body.EpiAt >= Time.Minutes(Life.EpiMinutes) && TakeOne(c.Cell, iEpi, sim))
        {
            int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 아드레날린을 놓았다", sim.Tick, c.Cell, body.ArrestCause, 0, a.Id);
            _life.Give(sim, vb, Drug.Epi, 1, cause); EpiDoses++;
        }
        body = _life.Bodies[vb];
        if (c.Holding == iAed && body.Arrest == 1 && sim.Tick - c.StepStart >= 4 && sim.Tick - body.ShockAt >= Time.Minutes(2))
        {
            int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 제세동기를 붙여 쳤다", sim.Tick, c.Cell, body.ArrestCause, 0, a.Id);
            AedShocks++;
            _life.Defibrillate(sim, vb, cause);
        }
        return _life.Bodies[vb].Arrest == 0 ? 1 : 0;
    }

    /// <summary>가슴 압박을 마쳤다 (다시 뛰었거나 숨졌거나 40분) — 제세동기는 그 자리에 둔다 · 솜씨가 는다.</summary>
    private void CprDone(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        if (c.Holding == iAed) DropHeld(a);
        if (a.IsPerson) c.SkMedic = Math.Min(1, c.SkMedic + 0.006f * Practice(c, "치료") * UseSkill(a, 1, sim.Tick));
        if (c.AssignedGoal == GoalKind.Cpr && c.AssignedTarget == st.Target) c.AssignedUntil = sim.Tick;
    }

    /// <summary>돌보러 오는 이가 있으면 그 자리에서 기다린다 (의식이 있는 다친 이).</summary>
    private void AwaitCare(Agent a, int here)
    {
        ref var c = ref a.C;
        foreach (var o in Agents)
            if (o != a && o.C.Up != 0 && o.C.AssignedGoal == GoalKind.Treat && o.C.AssignedTarget == c.Body && o.C.AssignedUntil > _nowTick)
            { Add(GoalKind.Idle, -1, here, 2.6f); return; }
    }

    /// <summary>앓는 이의 쉬기: 덜 성실할수록 눕는다 (일은 성실할수록 놓지 않는다).</summary>
    private void SickOptions(Agent a, int here)
    {
        ref var c = ref a.C;
        if (!Life.Sick(_life![c.Body])) return;
        int bed = c.Bed >= 0 && !Taken(a, c.Bed) ? c.Bed : NearestFree(a, _beds);
        Add(GoalKind.Sleep, bed, bed >= 0 ? bed : here, 1.2f + 1.2f * (1 - c.Consc));
    }

    /// <summary>앓음 · 몸무게가 일 점수에 주는 몫.</summary>
    private float WorkFactor(Agent a)
    {
        ref var b = ref _life![a.C.Body];
        float f = Life.Sick(b) ? 0.4f + 0.6f * a.C.Consc : 1;
        return f;
    }

    /// <summary>걸음 빠르기에 곱하는 몫: 앓으면 0.85 · 몸무게를 10% 넘게 잃으면 줄어 20%면 0.85.</summary>
    public double BodyPace(Agent a)
    {
        if (!a.IsPerson) return 1;
        ref var b = ref _life![a.C.Body];
        double loss = Life.WeightLoss(b);
        return (Life.Sick(b) ? 0.85 : 1) * Math.Clamp(1 - 1.5 * Math.Max(0, loss - 0.1), 0.6, 1) * Life.OrganPace(b);   // ⑦ 상한 심장 · 뇌는 느리다
    }

    /// <summary>졸림이 차는 몫: 앓으면 1.5배 · 몸무게를 잃은 만큼 빨리 지친다.</summary>
    private double TireFactor(Agent a)
    {
        ref var b = ref _life![a.C.Body];
        return (Life.Sick(b) ? 1.5 : 1) * (1 + 2 * Math.Max(0, Life.WeightLoss(b) - 0.1)) * (1 + 2 * b.Heart);   // ⑦ 상한 심장은 빨리 지친다
    }

    /// <summary>잠의 질 (자는 동안 · 30분에 걸쳐 따라간다): 소리 · 빛 · 온도 · 아픔 · 열 · 긴장.</summary>
    private void SleepQuality(Agent a, double h)
    {
        ref var c = ref a.C;
        ref var b = ref _life![c.Body];
        int cell = c.Cell;
        double noise = Math.Clamp((c.SleepNoise - 35) / 25, 0, 1);
        double lux = _light?.Lux[cell] ?? 0;
        double light = lux > 30 ? Math.Min(1, lux / 300) : 0;
        double tc = F.Temp(cell) - 273.15;
        double temp = Math.Min(1, tc < 18 ? (18 - tc) / 6 : tc > 26 ? (tc - 26) / 6 : 0);
        double body = b.Pain * 0.8 + Math.Min(0.4, Life.FeverK(b) * 0.25);
        double q = Math.Clamp(1 - 0.5 * noise - 0.3 * light - 0.4 * temp - 0.6 * body - 0.3 * c.Stress, 0.15, 1);
        c.SleepLoss = (float)(c.SleepLoss + ((1 - q) - c.SleepLoss) * Math.Min(1, h * 2));
        c.SleepNoise = (float)(c.SleepNoise * Math.Exp(-h * 6));   // 10분이면 흐려진다
    }
}
