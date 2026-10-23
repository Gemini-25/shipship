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
        or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock or GoalKind.Haul or GoalKind.Tighten or GoalKind.ToiletCare or GoalKind.Treat or GoalKind.Rewire or GoalKind.Cpr or GoalKind.Surgery or GoalKind.Cook
        or GoalKind.Stash or GoalKind.Refill or GoalKind.Gather;

    private static bool HandStep(in PlanStep st) => st.Kind is StepKind.Spray or StepKind.Seal or StepKind.Fix or StepKind.PickUp or StepKind.Brace or StepKind.Craft
        or StepKind.Maintain or StepKind.Clean or StepKind.Harvest or StepKind.Salvage or StepKind.Rewire or StepKind.Tighten or StepKind.Sanitize or StepKind.Gather or StepKind.Refill
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

    /// <summary>
    /// 충전할 독 (docs/MIND.md 3단 덧붙임 — 죽은 충전독): 제 독이 죽지 않았으면 제 독 · 아니면 죽지 않은 가장 가까운 독 (남이 차고 있는 독은 뒤로) ·
    /// 다 죽었으면 지금 자리에서 가장 가까운 독에서 기다린다 (전원이 돌아오면 그 자리에서 찬다). 죽은 독의 기억은 2시간 — 그 뒤엔 다시 믿어 본다.
    /// </summary>
    private int ChargeDock(Agent a)
    {
        ref var c = ref a.C;
        if (c.DeadDocks != 0 && _nowTick - c.DeadDocksAt > Time.Hours(2)) c.DeadDocks = 0;
        if (c.DeadDocks == 0) return c.Dock;
        var docks = c.Kind == AgentKind.Drone ? _droneDocks : _docks;
        int own = DockIndex(docks, c.Dock);
        if (own < 0 || (c.DeadDocks & (1UL << own)) == 0) return c.Dock;
        int best = -1, bd = int.MaxValue, near = -1, nd = int.MaxValue;
        for (int k = 0; k < docks.Length && k < 64; k++)
        {
            int cell = L.Furniture[docks[k]].Cells[0];
            int d = PathDist(c.Cell, cell);
            if (d < nd) { nd = d; near = cell; }
            if ((c.DeadDocks & (1UL << k)) != 0) continue;
            foreach (var o in Agents) if (o != a && o.C.Up != 0 && o.C.Goal == GoalKind.Charge && o.C.GoalTarget == cell) { d += 40; break; }
            if (d < bd) { bd = d; best = cell; }
        }
        return best >= 0 ? best : near >= 0 ? near : c.Dock;
    }

    private int DockIndex(int[] docks, int cell)
    {
        for (int k = 0; k < docks.Length && k < 64; k++) if (L.Furniture[docks[k]].Cells[0] == cell) return k;
        return -1;
    }

    /// <summary>붙은 독이 6분 동안 전지를 채우지 않았다 — 처음 겪는 독이면 죽은 독으로 적고 (막힘 · 원인 마디) 다른 독을 찾는다 · 이미 아는 죽은 독이면 (다 죽어 기다리는 중) 그대로 기다린다.</summary>
    private bool DockDied(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        int k = DockIndex(c.Kind == AgentKind.Drone ? _droneDocks : _docks, c.Cell);
        if (k < 0 || (c.DeadDocks & (1UL << k)) != 0) return false;
        c.DeadDocks |= 1UL << k; c.DeadDocksAt = sim.Tick;
        Fail(sim, a, "충전독이 전지를 채우지 않는다", SnagKind.DockDead, c.Cell, L.FurnitureAt[c.Cell]);
        return true;
    }
}
