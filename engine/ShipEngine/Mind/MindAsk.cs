namespace ShipEngine;

/// <summary>
/// ⑩ → ⑧ 주컴퓨터의 부탁 (깊이 바퀴 2). 로봇이 못 할 때 주컴퓨터가 사람에게 일을 부탁한다 — 받을지는 그 사람이 정한다 (주컴퓨터는 부탁만 한다).
/// 받을 마음 = 0.15 + 주컴퓨터를 믿는 정도 × 0.45 + 성실 × 0.25 + 다정 × 0.2 + 그 일 솜씨 × 0.25 (+ 급하다는 말 0.25)
/// − 두려움 × (1 − 용기) × 0.8 − 졸림(0.6 넘는 만큼) × 0.6 − 하던 급한 일 0.5 — 공황 · 본능 중이면 못 받는다. 자던 이는 깨워서 묻는다.
/// 받으면 그 일이 맡은 일이 된다 (로봇처럼 30분) · 거절하면 이유를 말한다 (가장 큰 까닭).
/// </summary>
public sealed partial class Mind
{
    public long AsksAccepted, AsksDeclined;

    public static string JobName(GoalKind g) => g switch
    {
        GoalKind.FightFire => "불을 꺼", GoalKind.Rescue => "쓰러진 사람을 구해", GoalKind.Patch => "구멍을 막아", GoalKind.Investigate => "살펴봐",
        GoalKind.Repair => "고쳐", GoalKind.Brace => "받쳐", GoalKind.Treat => "다친 이를 돌봐", GoalKind.Surgery => "수술해", GoalKind.Counsel => "이야기를 들어", _ => "가 봐",
    };

    /// <summary>주컴퓨터가 그 사람에게 일을 부탁한다 — 받으면 true (그 일이 맡은 일이 된다) · reply는 그 사람의 대답.</summary>
    public bool Ask(Sim sim, int agent, GoalKind goal, int target, Belief b, long until, bool urgent, out string reply, int arg = 0)
    {
        reply = "";
        Bind(sim);   // 복제 세계(주컴퓨터가 앞일을 그려 볼 때)에서 마음 엔진이 아직 한 번도 돌지 않았어도
        if (agent < 0 || agent >= Agents.Count || !Agents[agent].IsPerson) return false;
        var a = Agents[agent];
        ref var c = ref a.C;
        ref var body = ref _life![c.Body];
        if (body.Alive == 0 || body.Conscious == 0) return false;                     // 답이 없다
        if (body.Asleep != 0) Wake(sim, a, "주컴퓨터가 깨웠다");
        float trust = _soc?.Trust(agent) ?? 0.5f;
        float skill = goal switch
        {
            GoalKind.FightFire => c.SkFire, GoalKind.Rescue or GoalKind.Treat or GoalKind.Surgery => c.SkMedic, GoalKind.Patch => c.SkSeal,
            GoalKind.Repair or GoalKind.Brace => c.SkRepair, GoalKind.Counsel => c.Agree, _ => 0.3f,
        };
        float fear = c.Fear * (1 - c.Brave), tired = Math.Max(0, c.Sleepy - 0.6f);
        bool busy = c.Goal is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Evacuate or GoalKind.GetSuit && a.Plan.Count > 0 && c.StepIndex < a.Plan.Count;
        float will = 0.15f + 0.45f * trust + 0.25f * c.Diligent + 0.2f * c.Kindness + 0.25f * skill + (urgent ? 0.25f : 0)
                     - 0.8f * fear - 0.6f * tired - (busy ? 0.5f : 0);
        if (c.Mode != 0 || c.Goal is GoalKind.Panic or GoalKind.Flee) will = 0;          // 공황 · 본능 — 들리지 않는다
        bool yes = Rng.Unit(sim.Seed, Rng.Key("부탁"), agent, sim.Tick) < Math.Clamp(will, 0.03f, 0.97f);
        string job = JobName(goal);
        if (yes)
        {
            Assign(sim, agent, goal, target, b, until, arg);
            AsksAccepted++;
            reply = urgent ? "알았어요, 바로 갈게요" : "알았어요, 제가 갈게요";
        }
        else
        {
            float[] why = { 0.8f * fear, 0.6f * tired, busy ? 0.5f : 0, 0.45f * (0.5f - trust) };
            int k = Array.IndexOf(why, why.Max());
            reply = c.Mode != 0 || c.Goal is GoalKind.Panic or GoalKind.Flee ? "(대답이 없다)"
                  : why[k] <= 0.05f ? "지금은 못 하겠어요"
                  : k switch { 0 => "무서워서 못 가겠어요", 1 => "너무 졸려서 못 하겠어요", 2 => "지금 하는 일이 더 급해요", _ => "주컴퓨터 말은 못 믿겠어요" };
            AsksDeclined++;
        }
        int cause = sim.Causes.Add(CauseKind.Person, yes ? $"{Ko.I(NameOf(agent))} 주컴퓨터의 부탁을 받았다 — {job} 달라는" : $"{Ko.I(NameOf(agent))} 주컴퓨터의 부탁을 거절했다 — \"{reply}\"",
            sim.Tick, c.Cell, b.Cause, 0, agent);
        sim.Facts.Emit(sim.Tick, FactKind.Decision, b.Cell >= 0 ? b.Cell : c.Cell, yes ? 1 : -1, cause, agent, (int)goal);
        return yes;
    }
}
