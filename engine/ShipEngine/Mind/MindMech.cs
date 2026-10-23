namespace ShipEngine;

/// <summary>
/// ⑧ 로봇이 제 몸을 느낀다 (판단 이관 (사) — 몸 · 기계의 손상은 월드가 · docs/ENGINE.md): 전지 · 상함은 ⑥ 망이 계산하고 (<see cref="Network.Robots"/>),
/// 로봇의 마음은 그것을 느끼고 대응만 한다 — 다리가 망가지면 걷지 않고 · 팔을 못 쓰면 손일을 고르지 않고 놓으며 · 눈이 흐리면 가까이만 본다.
/// 못 하게 된 일은 막힘(까닭: 팔 · 다리)으로 남겨 주컴퓨터가 읽는다 — 주컴퓨터는 그 일을 할 수 있는 다른 로봇 · 사람에게 맡긴다.
/// </summary>
public sealed partial class Mind
{
    /// <summary>손일 — 팔이 상하면 못 한다.</summary>
    public static bool HandGoal(GoalKind g) => g is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Repair or GoalKind.Brace or GoalKind.Craft
        or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock or GoalKind.Haul or GoalKind.Tighten or GoalKind.ToiletCare or GoalKind.Treat or GoalKind.Rewire or GoalKind.Cpr or GoalKind.Surgery or GoalKind.Cook;

    private static bool HandStep(in PlanStep st) => st.Kind is StepKind.Spray or StepKind.Seal or StepKind.Fix or StepKind.PickUp or StepKind.Brace or StepKind.Craft
        or StepKind.Maintain or StepKind.Clean or StepKind.Harvest or StepKind.Salvage or StepKind.Rewire or StepKind.Tighten or StepKind.Sanitize
        || st.Kind == StepKind.Use && (UseKind)st.Arg is UseKind.Treat or UseKind.Cpr or UseKind.Surgery or UseKind.Cook;

    private bool HasMech(Agent a) => !a.IsPerson && _net != null && a.C.Mech >= 0 && a.C.Mech < _net.Robots.Count;

    /// <summary>로봇의 손이 쓸 만한가 (사람은 늘 참 — 사람의 손은 ⑦ 생명이 본다).</summary>
    public bool HandsOk(Agent a) => !HasMech(a) || Network.RobotHands(_net!.Robot(a.C.Mech));
    /// <summary>로봇이 걸을 수 있나.</summary>
    public bool LegsOk(Agent a) => !HasMech(a) || Network.RobotPace(_net!.Robot(a.C.Mech)) > 0;
    /// <summary>로봇의 몸이 그 일을 할 수 있나 (주컴퓨터가 맡기기 전에 읽는다 — 로봇의 몸 보고).</summary>
    public bool RobotCan(Agent a, GoalKind g) => LegsOk(a) && (!HandGoal(g) || HandsOk(a));

    private double MechPace(Agent a) => HasMech(a) ? Network.RobotPace(_net!.Robot(a.C.Mech)) : 1;
    private double MechSight(Agent a) => HasMech(a) ? Network.RobotSight(_net!.Robot(a.C.Mech)) : 1;

    /// <summary>몸을 느낀다: 전지 · 속(멈춤) · 새로 망가진 다리 · 팔 — 하던 일을 할 수 없게 됐으면 놓는다.</summary>
    private void FeelMech(Sim sim, Agent a)
    {
        if (!HasMech(a)) return;
        ref var c = ref a.C;
        ref var rb = ref _net!.Robot(c.Mech);
        c.Battery = rb.Battery; c.Damage = rb.Core;
        byte felt = (byte)((Network.RobotPace(rb) <= 0 ? 1 : 0) | (Network.RobotHands(rb) ? 0 : 2) | (rb.Eyes >= 0.5f ? 4 : 0));
        byte fresh = (byte)(felt & ~c.MechFelt);
        c.MechFelt = felt;
        if (fresh == 0 || c.Up == 0) return;
        bool planHands = a.Plan.Count > 0 && c.StepIndex < a.Plan.Count && a.Plan.Skip(c.StepIndex).Any(st => HandStep(st));
        if ((fresh & 1) != 0 && a.Plan.Count > 0) Fail(sim, a, "다리가 망가져 걸을 수 없다", SnagKind.NoLegs);
        else if ((fresh & 2) != 0 && (planHands || HandGoal(c.Goal))) Fail(sim, a, "팔이 상해 손을 쓸 수 없다", SnagKind.NoHands);
    }
}
