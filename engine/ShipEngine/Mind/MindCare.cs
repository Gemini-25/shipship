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
        if (c.Holding != iMedkit && c.Holding == 255)
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
        bool kit = c.Holding == iMedkit;
        _life.Treat(sim, vb, kit);
        if (kit) UseHeld(sim, a); else TreatsBare++;
        Treats++;
        int va = AgentOfBody(vb);
        string who = va >= 0 ? NameOf(va) : "다친 이";
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot,
            kit ? $"{Ko.I(NameOf(a.Id))} {Ko.Eul(who)} 돌봤다 — 붕대 · 화상 덮개{(_life.Bodies[vb].Abx > 0 ? " · 항생제" : "")}" : $"{Ko.I(NameOf(a.Id))} 맨손으로 {Ko.Eul(who)} 돌봤다 — 구급 상자가 없었다",
            sim.Tick, c.Cell, _life.Bodies[vb].HurtCause, 0, a.Id);
        sim.Facts.Emit(sim.Tick, FactKind.Treated, c.Cell, kit ? 1 : 0, cause, a.Id, va);
        if (c.AssignedGoal == GoalKind.Treat && c.AssignedTarget == vb) c.AssignedUntil = sim.Tick;   // 맡은 일을 마쳤다
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
        return (Life.Sick(b) ? 0.85 : 1) * Math.Clamp(1 - 1.5 * Math.Max(0, loss - 0.1), 0.6, 1);
    }

    /// <summary>졸림이 차는 몫: 앓으면 1.5배 · 몸무게를 잃은 만큼 빨리 지친다.</summary>
    private double TireFactor(Agent a)
    {
        ref var b = ref _life![a.C.Body];
        return (Life.Sick(b) ? 1.5 : 1) * (1 + 2 * Math.Max(0, Life.WeightLoss(b) - 0.1));
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
