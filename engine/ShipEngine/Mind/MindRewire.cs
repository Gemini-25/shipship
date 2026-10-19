namespace ShipEngine;

/// <summary>
/// ⑥ 고쳐 쓰기 · 임시 배선 — 사람 · 로봇 쪽 (docs/ENGINE.md ⑥ 바퀴 2).
/// <list type="bullet">
/// <item>로봇은 부품 주머니에 갈래마다 새 부품 하나를 지니고 다닌다 (⑪ 공급 사슬 — 전기 · 기계 · 봉함 · 거르개 · 그 갈래를 쓰면 작업대의 그 갈래 부품을 가지러 간다).</item>
/// <item>떼어 쓰기: 그 갈래 부품이 배에 없으면 덜 중요한 장치에서 같은 갈래 부품을 떼어 와 (사람 10분 · 로봇 5분) 고친다 — 헌 부품이라 수명을 쓴 몫이 그대로.</item>
/// <item>연장선: 맡은 방(급전선이 죽었다)에 전기가 남는 옆 방에서 끌어온다 — 옆 방 통풍구에서 꽂고(1분) 그 방까지 깔고 잇는다(사람 2분 · 로봇 1분 30초).</item>
/// </list>
/// </summary>
public sealed partial class Mind
{
    public const byte RobotSpares = 0x0F;   // 갈래마다 하나
    public long Salvages, Rewires;

    /// <summary>⑪ 그 갈래의 갈아 끼울 부품 (전기 부품 · 기계 부품 · 봉함재 · 거르개).</summary>
    public byte SpareOf(int part) => iSpare[Math.Clamp(part, 0, 3)];

    /// <summary>로봇 부품 주머니에 그 갈래가 있나.</summary>
    public static bool InPouch(in AgentCore c, int part) => (c.Spares >> part & 1) != 0;

    /// <summary>고칠 부품이 있나 (그 갈래): 로봇 부품 주머니 · 손에 든 그 갈래 부품 · 떼어 낸 부품 (떼어 쓰기는 같은 갈래에서만).</summary>
    private bool HasPart(Agent a, int part) => !a.IsPerson && InPouch(a.C, part) || a.C.Holding == SpareOf(part) || a.C.Holding == iSalvage;

    /// <summary>부품을 마련하는 계획 (없으면 false): 그 갈래 부품(작업대 · 선반) → 없으면 덜 중요한 장치에서 떼어 온다 (떼어 낸 장치는 다시 떼어 고치지 않는다).</summary>
    private bool PlanPart(Agent a, int dev, int part, int here, List<PlanStep> plan)
    {
        if (HasPart(a, part)) return true;
        if (a.C.Holding != 255) return false;
        int src = NearestStock(here, SpareOf(part));
        if (src >= 0)
        {
            plan.Add(Go(L.Furniture[src].Cells[0], 1));
            plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = SpareOf(part) });
            return true;
        }
        if (_net == null || part >= 4 || _net.IsStripped(dev)) return false;
        int donor = _net.Donor(dev, (Part)part, _net.Devices[dev].Cell);
        if (donor < 0) return false;
        plan.Add(Go(_net.Devices[donor].Cell, 1));
        plan.Add(new PlanStep { Kind = StepKind.Salvage, Target = donor, Arg = (byte)part, Cell = dev });
        return true;
    }

    /// <summary>떼어 낸다: 그 장치의 부품을 빼 손에 든다 (떼어 낸 부품 — 수명을 쓴 몫을 함께).</summary>
    private void SalvageStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int donor = st.Target;
        if (_net == null || _net.Broken[donor * 4 + st.Arg] != 0 || _net.Broken[st.Cell * 4 + st.Arg] == 0) { EndPlan(a); return; }   // 이미 떼었거나 고칠 것이 고쳐졌다
        if (Dist(c.Cell, _net.Devices[donor].Cell) > 2) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        Activity(a, "work");
        WorkNoise(sim, a, SoundKind.Tool, 70);   // 떼어 내기: 렌치
        if (t - c.StepStart < Time.Minutes(a.IsPerson ? 10 : 5)) return;
        float worn = _net.TakePart(donor, (Part)st.Arg);
        if (c.Holding != 255) DropHeld(a);
        c.Holding = iSalvage; c.HoldCharge = worn;
        sim.Book.AddItem(iSalvage, Flow.Salvaged, 1);
        Salvages++;
        string part = st.Arg switch { 0 => "회로", 1 => "모터", 2 => "봉함", _ => "거르개" };
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot,
            $"{Ko.I(NameOf(a.Id))} {_net.Devices[donor].Name}에서 {Ko.Eul(part)} 떼어 냈다 — {Ko.Eul(_net.Devices[st.Cell].Name)} 고치려고 ({Ko.I(ItemNames[SpareOf(st.Arg)])} 없다)", t, _net.Devices[donor].Cell, c.GoalCause, 0, a.Id);
        sim.Facts.Emit(t, FactKind.Salvaged, _net.Devices[donor].Cell, st.Arg, cause, a.Id, donor);
        c.GoalCause = cause;
        Next(a, t);
    }

    /// <summary>연장선 계획: 옆 방 통풍구로 가서 꽂고 → 그 방 통풍구까지 깔고 잇는다.</summary>
    private bool PlanRewire(Agent a, int room, List<PlanStep> plan)
    {
        if (_net == null || room < 0 || room >= L.Rooms.Count || _net.RoomPowered(room) || _net.Jumpers.Exists(j => j.To == room)) return false;
        int donor = _net.BestDonorRoom(room);
        if (donor < 0) return false;
        plan.Add(Go(_net.Vent[donor], 1));
        plan.Add(new PlanStep { Kind = StepKind.Rewire, Target = room, Cell = donor, Arg = 0 });
        plan.Add(Go(_net.Vent[room], 1));
        plan.Add(new PlanStep { Kind = StepKind.Rewire, Target = room, Cell = donor, Arg = 1 });
        return true;
    }

    private void RewireStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int room = st.Target, donor = st.Cell;
        if (_net == null || _net.OwnFeederLive(room) || _net.Jumpers.Exists(j => j.To == room)) { EndPlan(a); return; }
        if (!_net.OwnFeederLive(donor)) { Fail(sim, a, "옆 방도 전기가 끊겼다"); return; }
        int at = st.Arg == 0 ? _net.Vent[donor] : _net.Vent[room];
        if (Dist(c.Cell, at) > 2) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        Activity(a, "work");
        WorkNoise(sim, a, SoundKind.Tool, 58);   // 연장선 잇기: 드라이버
        double minutes = st.Arg == 0 ? 1 : a.IsPerson ? 2 * (1.6 - c.SkRepair) : 1.5;
        if (t - c.StepStart < Time.Minutes(minutes)) return;
        if (st.Arg == 1)
        {
            int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 연장선을 깔아 이었다", t, at, c.GoalCause, 0, a.Id);
            if (_net.LayJumper(sim, donor, room, cause)) Rewires++;
            if (a.C.AssignedGoal == GoalKind.Rewire) a.C.AssignedUntil = t;
        }
        Next(a, t);
    }
}
