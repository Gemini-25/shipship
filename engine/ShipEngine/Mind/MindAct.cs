using System.Runtime.InteropServices;
namespace ShipEngine;

/// <summary>⑧ 마음 — 계획 단계 하기 · 걷기 · 문 · 길찾기.</summary>
public sealed partial class Mind
{
    public double WaterSprayed;   // 소화기에서 나온 물 (몰 — 장의 원소 장부 밖에서 들어온 것)

    // ───────────────────────── 단계 하기 ─────────────────────────

    private void Advance(Sim sim, Agent a, double dt)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        c.Speed = 0;
        // 업은 사람이 깨어났으면 내려놓는다
        if (c.Carrying >= 0 && (_life!.Bodies[c.Carrying].Conscious != 0 || _life.Bodies[c.Carrying].Alive == 0))
            DropCarried(sim, a, _life.Bodies[c.Carrying].Alive != 0);
        if (a.Plan.Count == 0 || c.StepIndex >= a.Plan.Count) { Activity(a, "rest"); return; }
        var st = a.Plan[c.StepIndex];
        switch (st.Kind)
        {
            case StepKind.GoTo:
            {
                if (st.Cell < 0)
                {
                    st.Cell = SafeDrop(a);
                    if (st.Cell < 0) { Fail(sim, a, "내려놓을 곳이 없다"); return; }
                    a.Plan[c.StepIndex] = st;
                }
                if (Arrived(c.Cell, st)) { Next(a, t); return; }
                if (a.Path.Count == 0 || a.PathIdx >= a.Path.Count)
                {
                    if (!FindPath(a, c.Cell, st.Cell, st.Arg)) { Fail(sim, a, "길이 없다"); return; }
                }
                Walk(sim, a, dt);
                if (Arrived(c.Cell, a.Plan[c.StepIndex])) Next(a, t);
                return;
            }
            case StepKind.Take:
            {
                var fu = L.Furniture[st.Target];
                if (Dist(c.Cell, fu.Cells[0]) > 2) { Fail(sim, a, "멀다"); return; }
                int k = st.Target * ItemCount + st.Arg;
                if (Stock[k] <= 0) { Fail(sim, a, "물건이 없다"); return; }
                if (c.Holding != 255) DropHeld(a);
                Stock[k]--;
                c.Holding = st.Arg; c.HoldCharge = (float)ItemCharge(st.Arg);
                Next(a, t);
                return;
            }
            case StepKind.Use: UseStep(sim, a, st); return;
            case StepKind.Spray: SprayStep(sim, a, st); return;
            case StepKind.Seal: SealStep(sim, a, st); return;
            case StepKind.Brace: BraceStep(sim, a, st); return;
            case StepKind.Craft: CraftStep(sim, a, st); return;
            case StepKind.Maintain: MaintainStep(sim, a, st); return;
            case StepKind.Clean: CleanStep(sim, a, st); return;
            case StepKind.Harvest: HarvestStep(sim, a, st); return;
            case StepKind.Stow: StowStep(sim, a, st); return;
            case StepKind.TakeFood: TakeFoodStep(sim, a, st); return;
            case StepKind.Fix: FixStep(sim, a, st); return;
            case StepKind.PickUp:
            {
                int vb = st.Target;
                var body = _life!.Bodies[vb];
                if (body.Alive == 0 || body.Conscious != 0) { Forget(a, b => b.Kind == BeliefKind.Injured && b.Subject == vb); EndPlan(a); return; }
                if (Dist(c.Cell, body.Cell) > 1) { Fail(sim, a, "멀다"); return; }
                if (SafeAt(body.Cell))
                {
                    // 이미 안전한 곳 — 옮길 까닭이 없다 (치료는 의무관의 몫)
                    a.Avoid.Add(new AvoidEntry { Goal = GoalKind.Rescue, Target = vb, Until = t + Time.Minutes(30) });
                    EndPlan(a);
                    return;
                }
                int va = AgentOfBody(vb);
                foreach (var o in Agents) if (o != a && o.C.Carrying == vb) { Fail(sim, a, "누가 이미 업었다"); return; }
                c.Carrying = vb;
                if (va >= 0) Agents[va].C.CarriedBy = a.Id;
                Move(a, c.Cell);
                Next(a, t);
                return;
            }
            case StepKind.Drop:
            {
                // 내려놓은 곳이 안전할 때만 '업어 냈다' — 배 어디도 안전하지 않으면(기압이 다 빠졌다) 내려놓고 한동안 다시 업지 않는다 (업고 내리기를 되풀이하지 않게)
                int carried = c.Carrying;
                bool safe = SafeAt(c.Cell);
                DropCarried(sim, a, safe);
                if (!safe && carried >= 0) a.Avoid.Add(new AvoidEntry { Goal = GoalKind.Rescue, Target = carried, Until = t + Time.Minutes(5) });
                Next(a, t);
                return;
            }
            case StepKind.Wear:
            {
                if (t == c.StepStart)
                {
                    int k = st.Target * ItemCount + st.Arg;
                    if (Stock[k] <= 0 || !a.IsPerson) { Fail(sim, a, "우주복이 없다"); return; }
                    Stock[k]--;
                    ref var body = ref _life![c.Body];
                    body.Suit = 1; body.SuitO2 = (float)ItemCharge(st.Arg);
                }
                Activity(a, "work");
                if (t - c.StepStart >= Time.Minutes(P.Items[ItemNames[st.Arg]].Minutes)) Next(a, t);
                return;
            }
            case StepKind.Look:
                // 끊긴 센서 선: 식었으면 잇는다 (주컴퓨터가 맡긴 자리 — 아직 타면 불부터)
                if (_net != null && _net.DataCutAt(st.Cell) && F.Tf[st.Cell] < 330 && (_chem == null || _chem.Burning[st.Cell] == 0))
                {
                    if (t - c.StepStart < Time.Minutes(a.IsPerson ? 4 : 2)) return;
                    _net.SpliceData(st.Cell);
                    int sc = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 끊긴 센서 선을 이었다", t, st.Cell, c.GoalCause, 0, a.Id);
                    sim.Facts.Emit(t, FactKind.Repaired, st.Cell, 0, sc, a.Id, -1);
                    if (c.AssignedGoal == GoalKind.Investigate && c.AssignedTarget == st.Cell) c.AssignedUntil = t;
                }
                // 감각이 이번 틱에 다시 확인하지 못한 소리 · 냄새 믿음은 지운다 ("아무것도 없었다")
                Forget(a, b => b.Kind is BeliefKind.Bang or BeliefKind.Strain or BeliefKind.Leak or BeliefKind.SmokeSmell or BeliefKind.Help or BeliefKind.Spark
                               && Dist(b.Cell, st.Cell) <= 3 && t - b.Seen > 5);
                a.Avoid.Add(new AvoidEntry { Goal = GoalKind.Investigate, Target = st.Cell, Until = t + Time.Minutes(10) });
                Next(a, t);
                return;
            default:
                Next(a, t);
                return;
        }
    }

    private bool Arrived(int here, PlanStep st) => st.Arg == 0 ? here == st.Cell : Dist(here, st.Cell) <= st.Arg;

    private void Next(Agent a, long t)
    {
        a.C.StepIndex++; a.C.StepStart = t + 1; a.Path.Clear(); a.PathIdx = 0; a.C.WaitTicks = 0;
    }

    private void EndPlan(Agent a) { a.Plan.Clear(); a.Path.Clear(); a.C.StepIndex = 0; }

    /// <summary>막혔다: 계획을 버리고 그 목표를 한동안(5분) 고르지 않는다.</summary>
    private void Fail(Sim sim, Agent a, string why)
    {
        ref var c = ref a.C;
        c.Failed++;
        if (c.Mode == 0) a.Avoid.Add(new AvoidEntry { Goal = c.Goal, Target = c.GoalTarget, Until = sim.Tick + Time.Minutes(5) });
        if (a.Avoid.Count > 16) a.Avoid.RemoveAt(0);
        EndPlan(a);
        if (c.Mode == 0) c.Goal = GoalKind.Idle;
        if (a.IsPerson) c.Anger = Math.Min(1, c.Anger + 0.05f);
    }

    private double ItemCharge(byte item)
    {
        var d = P.Items[ItemNames[item]];
        return d.Water > 0 ? d.Water : d.O2;
    }

    /// <summary>손에 든 물건을 써서 없앤다 (막기 · 덧대기 · 고치기 · 빈 소화기) — 장부에 '썼다'.</summary>
    private void UseHeld(Sim sim, Agent a)
    {
        if (a.C.Holding == 255) return;
        sim.Book.AddItem(a.C.Holding, Flow.Used, -1);
        a.C.Holding = 255; a.C.HoldCharge = 0;
    }

    private void DropHeld(Agent a)
    {
        if (a.C.Holding == 255) return;
        Loose.Add(new LooseItem { Item = a.C.Holding, Cell = a.C.Cell, Charge = a.C.HoldCharge });
        a.C.Holding = 255; a.C.HoldCharge = 0;
    }

    private void Activity(Agent a, string kind)
    {
        if (!a.IsPerson) return;
        ref var b = ref _life![a.C.Body];
        b.ActivityW = (float)(P.Watts.TryGetValue(kind, out var w) ? w : 90);
        if (b.CoreK > 312.15 && b.ActivityW > 300) b.ActivityW = 300; // 체온 39℃가 넘으면 지쳐서 더 몰아붙이지 못한다
    }

    private void UseStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        var use = (UseKind)st.Arg;
        if (t <= c.StepStart && use == UseKind.Sleep && a.IsPerson) { _life![c.Body].Asleep = 1; c.SleptAt = t; }
        Activity(a, use switch { UseKind.Sleep => "sleep", UseKind.Work or UseKind.Cook => "work", UseKind.Exercise => "hard", UseKind.None or UseKind.Relax or UseKind.Talk => "rest", _ => "rest" });
        bool done = t - c.StepStart >= st.Value;
        switch (use)
        {
            // 한번 누우면 다섯 시간은 잔다 · 목이 몹시 마르면 깬다 (마실 것이 없어도 30분은 눈을 붙인다)
            case UseKind.Sleep: done |= c.Sleepy < 0.03f && t - c.StepStart >= Time.Hours(5) || a.IsPerson && c.Thirst > WakeThirst && t - c.StepStart >= Time.Minutes(30); break;
            case UseKind.Charge: done |= c.Battery >= 0.98f && t - c.StepStart >= Time.Minutes(10); break; // 다 차면 독에서 10분씩 기다린다
            case UseKind.Work: done |= !InWindow(Time.HourOfDay(t), c.ShiftHour, 8); break;
        }
        if (use == UseKind.Cook) CookStep(sim, a, st, done);
        if (!done) return;
        switch (use)
        {
            case UseKind.Eat when a.IsPerson:
            {
                double got = 0;
                foreach (int x in L.Furniture[st.Target].Cells) { got += F.TakeStore(x, T.sFood, Math.Max(0, MealMol - got)); if (got >= MealMol) break; }
                if (got > 0) { _life!.Eat(c.Body, got); FoodEaten += got; sim.Book.Add(Res.Food, Flow.Eaten, -got); c.Hunger = (float)Math.Max(0, c.Hunger - got); c.Joy = Math.Min(1, c.Joy + 0.1f); c.AteAt = t; Meals++; }
                break;
            }
            case UseKind.Drink when a.IsPerson:
            {
                double want = Math.Max(15, c.Thirst * 0.02 * BodyWater0 + 10);
                double got = _net != null ? Math.Min(_net.WaterTank, want) : want;
                if (_net != null) { _net.WaterTank -= got; sim.Book.Add(Res.Water, Flow.Drunk, -got); }
                _life!.Drink(c.Body, got);
                WaterDrunk += got;
                c.DrankAt = t; Drinks++;
                break;
            }
            case UseKind.Sleep when a.IsPerson:
                _life![c.Body].Asleep = 0; c.WokeAt = t;
                break;
            case UseKind.Relax when a.IsPerson:
                FinishRelax(a);
                break;
            case UseKind.Exercise when a.IsPerson:
                // 땀 흘리고 나면 개운하다: 지루함 · 긴장이 풀린다 (운동과 기분 연구)
                c.Unfit = 0; c.ExercisedAt = t; Exercised++;
                c.Bored = Math.Max(0, c.Bored - 0.3f); c.Stress = Math.Max(0, c.Stress - 0.2f); c.Joy = Math.Min(1, c.Joy + 0.15f);
                break;
        }
        Next(a, t);
    }

    private void SprayStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int center = st.Cell;
        var fires = new List<int>();
        int x0 = L.X(center), y0 = L.Y(center);
        for (int y = Math.Max(0, y0 - 2); y <= Math.Min(L.H - 1, y0 + 2); y++)
            for (int x = Math.Max(0, x0 - 2); x <= Math.Min(L.W - 1, x0 + 2); x++)
            {
                int i = L.Index(x, y);
                if (_chem != null && _chem.Burning[i] != 0 && Dist(c.Cell, i) <= 3) fires.Add(i);
            }
        if (fires.Count == 0)
        {
            bool still = BurningNear(center, 2);
            if (!still)
            {
                if (t > c.StepStart)
                {
                    Extinguished++;
                    // 불씨가 된 조리대가 곁에 켜져 있으면 전원도 내린다 (안 끄면 다시 눌어 붙는다)
                    bool stove = false;
                    if (_net != null)
                        foreach (var dv in _net.Devices)
                            if (dv.Def.Role == "stove" && _net.On[dv.Id] != 0 && Dist(dv.Cell, center) <= 3)
                            {
                                if (_net.OnCause[dv.Id] != 0) sim.Facts.Emit(t, FactKind.Switched, dv.Cell, 0, _net.OnCause[dv.Id], a.Id, dv.Id);
                                _net.SetOn(dv.Id, false); stove = true;
                            }
                    int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 불을 껐다{(stove ? " · 조리대 전원도 내렸다" : "")}", t, center, c.GoalCause, 0, a.Id);
                    sim.Facts.Emit(t, FactKind.Extinguished, center, 0, cause, a.Id);
                }
                Forget(a, b => b.Kind == BeliefKind.Fire && Dist(b.Cell, center) <= 3);
                Next(a, t);
            }
            else Fail(sim, a, "불이 손 닿는 곳에 없다");
            return;
        }
        if (c.Holding != iExt || c.HoldCharge <= 0.5f)
        {
            if (c.Holding == iExt) UseHeld(sim, a); // 빈 소화기는 버린다
            Fail(sim, a, "소화기가 비었다");
            return;
        }
        Activity(a, "hard");
        int me = c.Cell;
        fires.Sort((p, q) => Dist(me, p).CompareTo(Dist(me, q)) is var k && k != 0 ? k : p.CompareTo(q));
        int n = Math.Min(4, fires.Count);
        double amount = Math.Min(c.HoldCharge, 25 * Time.SecondsPerTick);
        for (int k = 0; k < n; k++) F.AddStore(fires[k], T.sWater, amount / n);
        c.HoldCharge -= (float)amount;
        WaterSprayed += amount;
    }

    private void SealStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int wall = st.Cell;
        bool doorGap = F.Hole[wall] <= 0 && DoorGapLeaks(wall);
        if (F.Hole[wall] <= 0 && !doorGap) { Forget(a, b => b.Kind == BeliefKind.Breach && b.Cell == wall); Next(a, t); return; }
        if (Dist(c.Cell, wall) > 1) { Fail(sim, a, "멀다"); return; }
        if (a.IsPerson && c.Holding != iPatch) { Fail(sim, a, "봉합 꾸러미가 없다"); return; }
        Activity(a, "hard");
        var kit = P.Items["봉합 꾸러미"];
        double minutes = a.IsPerson ? kit.Minutes * (1.5 - c.SkSeal) : kit.Minutes;
        if (t - c.StepStart < Time.Minutes(minutes)) return;
        double area = F.Hole[wall], max = kit.MaxArea * (a.IsPerson ? 1 : 2);
        _st!.Patch(wall, (float)Math.Max(0, area - max));
        if (a.IsPerson) UseHeld(sim, a);
        Patched++;
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} {(doorGap ? "문틈" : "구멍")}을 막았다", t, wall,
            doorGap ? _st.LowSideCause(wall) : _st.DamageCause[wall], 0, a.Id);
        sim.Facts.Emit(t, FactKind.Patched, wall, (float)Math.Min(doorGap ? Field.LeakNormalDoor : area, max), cause, a.Id);
        if (F.Hole[wall] <= 0 && !DoorGapLeaks(wall))
        {
            Forget(a, b => b.Kind == BeliefKind.Breach && b.Cell == wall || b.Kind is BeliefKind.Leak or BeliefKind.Vacuum && Dist(b.Cell, wall) <= 6);
            a.Doubts.Add(new Doubt { Kind = BeliefKind.Leak, Cell = wall, Until = t + Time.Minutes(10) }); // 내가 막았다 — 한동안 '샌다'는 말을 반신반의
        }
        // 로봇: 막은 자리가 아직 휘어 있으면(또 금이 갈 것) 그 자리에서 바로 덧대 받친다
        if (!a.IsPerson && _st.Stress[wall] >= 0.6f && _st.Braced[wall] == 0 && L.Kind[wall] is CellKind.Wall or CellKind.Door)
            a.Plan.Insert(Math.Min(a.Plan.Count, c.StepIndex + 1), new PlanStep { Kind = StepKind.Brace, Cell = wall });
        Next(a, t);
    }

    private void BraceStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int wall = st.Cell;
        int door = L.Kind[wall] == CellKind.Door ? L.DoorAt[wall] : -1;
        bool jam = door >= 0 && _st != null && _st.DoorJam[door] != 0;
        if (_st == null || _st.Braced[wall] != 0 && !jam) { Forget(a, b => b.Kind == BeliefKind.Strain && Dist(b.Cell, wall) <= 1); Next(a, t); return; }
        if (Dist(c.Cell, wall) > 1) { Fail(sim, a, "멀다"); return; }
        if (a.IsPerson && c.Holding != iRepair) { Fail(sim, a, "수리 꾸러미가 없다"); return; }
        Activity(a, "hard");
        // 끼인 문: 틀을 비틀어 바로잡는다 (떨어져 나간 문을 다시 끼우는 것은 오래 걸린다)
        double minutes = (a.IsPerson ? 4 * (1.5 - c.SkRepair) : 3) * (jam && F.DoorOpen[door] > 0.5f ? 2.5 : 1);
        if (t - c.StepStart < Time.Minutes(minutes)) return;
        if (jam)
        {
            _st.FreeDoor(door);
            ClearDoorItems(wall); // 문틀에 걸린 물건을 치운다
            if (_st.DoorDeltaP(door) > 3) _st.RequestDoor(door, 0); // 바람이 빨려 든다 — 바로잡은 김에 닫는다
            if (a.IsPerson) UseHeld(sim, a);
            int jc = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 끼인 문을 비틀어 바로잡았다", t, wall, _st.DamageCause[wall], 0, a.Id);
            sim.Facts.Emit(t, FactKind.Repaired, wall, (float)_st.DoorDeltaP(door), jc, a.Id, -1);
            Forget(a, b => b.Kind == BeliefKind.Strain && Dist(b.Cell, wall) <= 1);
            if (c.AssignedGoal == GoalKind.Brace && c.AssignedTarget == wall) c.AssignedUntil = t;
            Next(a, t);
            return;
        }
        _st.Brace(wall);
        if (a.IsPerson) UseHeld(sim, a);
        string what = L.Kind[wall] == CellKind.Door ? "문" : "벽";
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 끼익거리는 {Ko.Eul(what)} 덧대 받쳤다", t, wall, _st.DamageCause[wall], 0, a.Id);
        sim.Facts.Emit(t, FactKind.Repaired, wall, _st.Stress[wall], cause, a.Id, -1);
        Forget(a, b => b.Kind == BeliefKind.Strain && Dist(b.Cell, wall) <= 1);
        Next(a, t);
    }

    // ───────────────────────── ⑪ 일 ─────────────────────────

    private void JobDone(Agent a, long t) { a.C.AssignedUntil = t; Next(a, t); }

    private void CraftStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int bench = st.Target, item = st.Arg;
        if (Dist(c.Cell, L.Furniture[bench].Cells[0]) > 2) { Fail(sim, a, "멀다"); return; }
        if (!P.Recipes.TryGetValue(ItemNames[item], out var r)) { JobDone(a, t); return; }
        Activity(a, "work");
        double minutes = a.IsPerson ? r.Minutes * (1.5 - c.SkRepair) : r.Minutes;
        if (t - c.StepStart < Time.Minutes(minutes)) return;
        foreach (var (need, n) in r.Needs)
            for (int k = 0; k < n; k++)
                if (!TakeOne(c.Cell, ItemIndex(need), sim)) { Fail(sim, a, "재료가 없다"); return; }
        Stock[bench * ItemCount + item]++; sim.Book.AddItem(item, Flow.Crafted, 1);
        if (_work != null) _work.Crafted++;
        if (a.IsPerson) c.SkRepair = Math.Min(1, c.SkRepair + 0.005f);
        JobDone(a, t);
    }

    private void MaintainStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int dev = st.Target, part = st.Arg;
        if (_net == null || _net.Broken[dev * 4 + part] != 0) { JobDone(a, t); return; }
        if (Dist(c.Cell, _net.Devices[dev].Cell) > 2) { Fail(sim, a, "멀다"); return; }
        if (a.IsPerson && c.Holding != iRepair) { Fail(sim, a, "수리 꾸러미가 없다"); return; }
        Activity(a, "work");
        double minutes = a.IsPerson ? 20 * (1.6 - c.SkRepair) : 15;
        if (t - c.StepStart < Time.Minutes(minutes)) return;
        if (a.IsPerson) UseHeld(sim, a);
        else if (!TakeOne(c.Cell, iRepair, sim)) { Fail(sim, a, "갈아 끼울 부품이 없다"); return; }
        _net.Repair(dev, (Part)part);
        if (_work != null) _work.Maintained++;
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} {_net.Devices[dev].Name}의 {Device.PartName((Part)part)} 부품을 미리 갈았다", t, _net.Devices[dev].Cell, 0, 0, a.Id);
        sim.Facts.Emit(t, FactKind.Repaired, _net.Devices[dev].Cell, part, cause, a.Id, dev);
        JobDone(a, t);
    }

    private void CleanStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int cell = st.Cell;
        if (Dist(c.Cell, cell) > 2) { Fail(sim, a, "멀다"); return; }
        Activity(a, "work");
        if (t - c.StepStart < Time.Minutes(10)) return;
        {
            int x0 = L.X(cell), y0 = L.Y(cell);
            for (int y = Math.Max(0, y0 - 1); y <= Math.Min(L.H - 1, y0 + 1); y++)
                for (int x = Math.Max(0, x0 - 1); x <= Math.Min(L.W - 1, x0 + 1); x++)
                {
                    int k = L.Index(x, y);
                    if (_life != null) _life.Mold[k] = 0;
                    // 고인 물을 닦아 오수 통으로 (재활용 장치가 걸러 다시 쓴다)
                    double w = F.Stored(T.sWater, k);
                    if (w > 0 && _net != null) _net.WasteWater += F.TakeStore(k, T.sWater, w);
                }
        }
        if (_work != null) _work.Cleaned++;
        JobDone(a, t);
    }

    /// <summary>⑪ 작물 거두기: 10분 — 다 자란 몫을 손에 든다 (장부: 식량이 들어왔다 · 들고 있어도 배의 식량).</summary>
    private void HarvestStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        if (_life == null || st.Target >= _life.Crops.Count) { JobDone(a, t); return; }
        ref var crop = ref CollectionsMarshal.AsSpan(_life.Crops)[st.Target];
        if (Dist(c.Cell, crop.Cell) > 2) { Fail(sim, a, "멀다"); return; }
        Activity(a, "work");
        if (t - c.StepStart < Time.Minutes(a.IsPerson ? 10 * (1.5 - c.SkFarm) : 10)) return;
        float got = crop.Biomass;
        if (got <= 0) { JobDone(a, t); return; }
        crop.Biomass = 0;
        if (c.Holding != 255) DropHeld(a);
        c.Holding = iHarvest; c.HoldCharge = got;
        sim.Book.Add(Res.Food, Flow.Harvest, got);
        if (_work != null) _work.Harvested++;
        if (a.IsPerson) c.SkFarm = Math.Min(1, c.SkFarm + 0.005f);
        Next(a, t);
    }

    /// <summary>든 수확물을 식량 창고에 넣는다 (1분).</summary>
    private void StowStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        var fu = L.Furniture[st.Target];
        if (!IsFood(c.Holding)) { JobDone(a, t); return; }
        if (Dist(c.Cell, fu.Cells[0]) > 2) { Fail(sim, a, "멀다"); return; }
        Activity(a, "work");
        if (t - c.StepStart < Time.Minutes(1)) return;
        F.AddStore(fu.Cells[0], T.sFood, c.HoldCharge);
        c.Holding = 255; c.HoldCharge = 0;
        JobDone(a, t);
    }

    public const double RationMol = 40; // 식량 상자 하나 (포도당 40몰 ≈ 포장 식량 12kg)

    /// <summary>식량 보관함에서 식량 상자를 꺼낸다 (2분) — 장부는 그대로 (배 안에서 옮길 뿐).</summary>
    private void TakeFoodStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        var fu = L.Furniture[st.Target];
        if (Dist(c.Cell, fu.Cells[0]) > 2) { Fail(sim, a, "멀다"); return; }
        Activity(a, "work");
        if (t - c.StepStart < Time.Minutes(2)) return;
        double got = 0;
        foreach (int x in fu.Cells) { got += F.TakeStore(x, T.sFood, RationMol - got); if (got >= RationMol) break; }
        if (got <= 1e-6) { JobDone(a, t); return; }
        if (c.Holding != 255) DropHeld(a);
        c.Holding = iRation; c.HoldCharge = (float)got;
        Next(a, t);
    }

    private void FixStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int dev = st.Target;
        if (st.Arg == PullFeederArg && _net != null)
        {
            // 배전반: 합선 난 급전선을 손으로 끊는다 (1분)
            int fd = _net.StuckFault(dev);
            if (fd < 0) { Forget(a, b => b.Kind == BeliefKind.Broken && b.Subject == dev); Next(a, t); return; }
            if (Dist(c.Cell, _net.Devices[dev].Cell) > 2) { Fail(sim, a, "멀다"); return; }
            Activity(a, "work");
            if (t - c.StepStart < Time.Minutes(1)) return;
            _net.PullFeeder(fd);
            string room = _net.Feeders[fd].Room >= 0 ? L.Rooms[_net.Feeders[fd].Room].Name : "";
            int pc = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 배전반에서 {room} 급전선을 손으로 끊었다 (차단기가 눌어붙어 있었다)", t, _net.Devices[dev].Cell, _net.FaultCause(fd), 0, a.Id);
            sim.Facts.Emit(t, FactKind.Repaired, _net.Devices[dev].Cell, st.Arg, pc, a.Id, dev);
            if (_net.StuckFault(dev) < 0) Forget(a, b => b.Kind == BeliefKind.Broken && b.Subject == dev);
            Next(a, t);
            return;
        }
        if (_net == null || _net.Broken[dev * 4 + st.Arg] == 0) { Forget(a, b => b.Kind == BeliefKind.Broken && b.Subject == dev); Next(a, t); return; }
        if (Dist(c.Cell, _net.Devices[dev].Cell) > 2) { Fail(sim, a, "멀다"); return; }
        if (a.IsPerson && c.Holding != iRepair) { Fail(sim, a, "수리 꾸러미가 없다"); return; }
        Activity(a, "work");
        double minutes = a.IsPerson ? P.Items["수리 꾸러미"].Minutes * (1.6 - c.SkRepair) : 15;
        if (t - c.StepStart < Time.Minutes(minutes)) return;
        _net.Repair(dev, (Part)st.Arg);
        if (a.IsPerson) { UseHeld(sim, a); c.SkRepair = Math.Min(1, c.SkRepair + 0.01f); }
        Repaired++;
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} {Ko.Eul(_net.Devices[dev].Name)} 고쳤다", t, _net.Devices[dev].Cell, c.GoalCause, 0, a.Id);
        sim.Facts.Emit(t, FactKind.Repaired, _net.Devices[dev].Cell, st.Arg, cause, a.Id, dev);
        Forget(a, b => b.Kind == BeliefKind.Broken && b.Subject == dev);
        Next(a, t);
    }

    /// <summary>업은 사람을 내려놓는다 (구했으면 사실 · 원인).</summary>
    private void DropCarried(Sim sim, Agent a, bool rescued)
    {
        ref var c = ref a.C;
        int vb = c.Carrying;
        if (vb < 0) return;
        int va = AgentOfBody(vb);
        _life!.Move(vb, c.Cell);
        if (va >= 0) { Agents[va].C.CarriedBy = -1; Agents[va].C.Cell = c.Cell; }
        c.Carrying = -1;
        if (!rescued) return;
        Rescued++;
        string victim = va >= 0 ? NameOf(va) : "사람";
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} {Ko.Eul(victim)} 업어 냈다", sim.Tick, c.Cell, _life.Bodies[vb].HurtCause, 0, a.Id);
        sim.Facts.Emit(sim.Tick, FactKind.Rescue, c.Cell, 0, cause, a.Id, va);
        Forget(a, b => b.Kind == BeliefKind.Injured && b.Subject == vb);
    }

    /// <summary>업은 사람을 내려놓을 곳: 가까운 의료 침대(안전하면) · 아니면 쓰러져 있던 방 밖의 안전한 칸 (그 칸에서 4칸 넘게).</summary>
    private int SafeDrop(Agent a)
    {
        int best = -1, bd = 60;
        foreach (int fu in _medBeds)
        {
            int cell = L.Furniture[fu].Cells[0];
            int d = Manhattan(a.C.Cell, cell);
            if (d < bd && SafeAt(cell) && !BelievedRoomBad(a, L.Room[cell])) { bd = d; best = cell; }
        }
        if (best >= 0) return best;
        int from = a.C.GoalCell >= 0 ? a.C.GoalCell : a.C.Cell;
        int refuge = FindRefuge(a, a.C.Cell, RoomOf(from), from);
        return refuge >= 0 ? refuge : FindSafe(a, a.C.Cell);
    }

    // ───────────────────────── 걷기 ─────────────────────────

    private double SpeedOf(Agent a)
    {
        ref var c = ref a.C;
        if (c.Kind == AgentKind.Robot) return P.Speed["robot"];
        if (c.Kind == AgentKind.Drone) return P.Speed["drone"];
        ref var b = ref _life![c.Body];
        double v = c.Mode == 1 || c.Goal == GoalKind.Evacuate ? P.Speed["run"] : c.Carrying >= 0 ? P.Speed["carry"] : P.Speed["walk"];
        if (SmokeBad(a, c.Cell)) v = Math.Min(v, P.Speed["crawl"]);
        if (b.Suit != 0) v *= 0.7;
        if (b.CoreK > 312.15) v = Math.Min(v, P.Speed["walk"]); // 더위에 지쳐 뛰지 못한다
        return v * Math.Max(0.3, 1 - b.Pain * 0.5);
    }

    private void Walk(Sim sim, Agent a, double dt)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        double v = SpeedOf(a);
        c.Speed = (float)v;
        Activity(a, c.Mode == 1 || c.Goal == GoalKind.Evacuate ? "run" : c.Carrying >= 0 ? "hard" : "walk");
        c.MoveBudget = (float)Math.Min(c.MoveBudget + v * dt, 8);
        while (c.MoveBudget >= 1 && a.PathIdx < a.Path.Count)
        {
            int n = a.Path[a.PathIdx];
            if (L.Kind[n] == CellKind.Door)
            {
                int d = L.DoorAt[n];
                if (!F.DoorPassable(d))
                {
                    _st?.RequestDoor(d, 1);
                    if (c.WaitDoor != d) { ReleaseDoor(a, c.WaitDoor); c.WaitDoor = d; c.WaitTicks = 0; } // 앞서 열어 달라던 다른 문은 거둔다
                    if (F.DoorBleed[d] == 0 || t % 25 == 0) c.WaitTicks++; // 압력 맞춤 밸브가 쉬익거리면 (기압을 맞추는 중) 더 기다린다 — 큰 에어락은 몇 분 걸린다 (12분까지)
                    if (c.OpenedDoor >= 0 && c.OpenedDoor != d) CloseBehind(sim, a, 1); // 에어락: 뒷문을 닫아야 앞문 밸브가 열린다
                    // 바깥문 앞: 이 방의 다른 문이 열려 있으면 (남이 열어 둔 것이라도) 닫는다 — 문 칸에 아무도 없을 때
                    if (L.Doors[d].External && _st != null && F.DoorBleed[d] == 0 && t % 5 == 0)
                    {
                        int room = RoomOf(c.Cell);
                        if (room >= 0)
                            foreach (int e in _st.RoomDoors(room))
                            {
                                if (e == d || F.DoorOpen[e] <= 0 && _st.DoorTarget[e] <= 0 || _st.DoorJam[e] != 0) continue;
                                int ec = L.Doors[e].Cell;
                                bool busy = false;
                                foreach (var o in Agents) if (o != a && o.C.Up != 0 && Dist(o.C.Cell, ec) <= 1) { busy = true; break; }
                                if (!busy && _life != null) foreach (var b in _life.Bodies) if (b.Cell == ec) { busy = true; break; }
                                if (!busy) _st.RequestDoor(e, 0);
                            }
                    }
                    if (c.WaitTicks > 12 || (_st != null && _st.DoorJam[d] != 0 && F.DoorOpen[d] < 0.5f))
                    {
                        Learn(a, t, BeliefKind.StuckDoor, n, d, 1, BeliefSource.Felt);
                        _st?.RequestDoor(d, 0); // 안 열리는 문 — 열어 달라던 것을 거둔다 (밸브도 닫힌다)
                        c.WaitTicks = 0; c.WaitDoor = -1;
                        a.Path.Clear(); a.PathIdx = 0;
                    }
                    c.MoveBudget = 0;
                    return;
                }
                if (c.WaitDoor == d && c.WaitTicks > 0) c.OpenedDoor = d;
                else if (_st != null && _st.DoorTarget[d] < 0.5f) { _st.RequestDoor(d, 1); c.OpenedDoor = d; } // 누가 닫으려던 문 — 지나가며 내가 닫는다
                c.WaitDoor = -1; c.WaitTicks = 0;
            }
            bool wasOut = L.Kind[c.Cell] == CellKind.Outside;
            Move(a, n);
            a.PathIdx++;
            if (!wasOut && L.Kind[n] == CellKind.Outside)
            {
                int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 에어락으로 나가 외판을 걷는다", t, n, c.GoalCause, 0, a.Id);
                sim.Facts.Emit(t, FactKind.WentOutside, n, 0, cause, a.Id);
            }
            c.MoveBudget -= L.FurnitureAt[n] >= 0 ? 1.5f : 1f;
            CloseBehind(sim, a);
        }
        if (a.PathIdx >= a.Path.Count) c.MoveBudget = Math.Min(c.MoveBudget, 1);
    }

    private void Move(Agent a, int cell)
    {
        ref var c = ref a.C;
        c.Cell = cell;
        if (a.IsPerson) _life!.Move(c.Body, cell);
        if (c.Carrying >= 0)
        {
            _life!.Move(c.Carrying, cell);
            int va = AgentOfBody(c.Carrying);
            if (va >= 0) Agents[va].C.Cell = cell;
        }
    }

    /// <summary>내가 연 문을 지나면 다시 닫는다 (배의 규칙). 공기 때문에 도망치는 중이면 지나자마자 — 뒤에 오는 사람이 있어도 (착한 사람은 기다린다).</summary>
    private void CloseBehind(Sim sim, Agent a, int minDist = 2)
    {
        ref var c = ref a.C;
        int d = c.OpenedDoor;
        if (d < 0 || _st == null) return;
        int dc = L.Doors[d].Cell;
        int dist = Dist(c.Cell, dc);
        if (dist < 1) return;
        foreach (var o in Agents) if (o != a && o.C.Cell == dc) return; // 문 칸에 누가 있다
        if (c.CloseHard != 0)
        {
            if (c.Kindness > 0.6f)
                foreach (var o in Agents)
                    if (o != a && o.IsPerson && o.C.Up != 0 && o.C.Mode == 1 && Dist(o.C.Cell, dc) <= 3 && Dist(o.C.Cell, c.Cell) > dist) return; // 뒤따르는 사람을 기다린다
        }
        else if (dist < minDist) return;
        _st.RequestDoor(d, 0);
        c.OpenedDoor = -1;
        if (c.CloseHard != 0)
        {
            int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 문을 닫았다", sim.Tick, dc, 0, 0, a.Id);
            sim.Facts.Emit(sim.Tick, FactKind.DoorSealed, dc, 0, cause, a.Id, d);
        }
    }

    // ───────────────────────── 길찾기 (A*) ─────────────────────────

    private readonly float[] _g;
    private readonly int[] _from, _stamp;
    private int _stampId;
    private readonly PriorityQueue<int, float> _pq = new();
    private readonly List<int> _dangerFire = new(), _dangerVac = new();

    public const int MaxExpand = 40000;

    /// <summary>4방향 A*: 가구 +0.5 · 닫힌 문 +2 · 안 열린다고 믿는 문은 막힘 · 불이라 믿는 곳 둘레 +15 · 진공이라 믿는 구역 +25.</summary>
    private bool FindPath(Agent a, int from, int to, int near)
    {
        a.Path.Clear(); a.PathIdx = 0;
        if (to < 0) return false;
        _dangerFire.Clear(); _dangerVac.Clear();
        bool suit = a.IsPerson && _life![a.C.Body].Suit != 0;
        bool eva = Eva(a);
        foreach (var b in a.Beliefs)
        {
            if (b.Conf < 0.3f) continue;
            if (b.Kind == BeliefKind.Fire && a.C.Goal != GoalKind.FightFire) _dangerFire.Add(b.Cell);
            else if (b.Kind == BeliefKind.Vacuum && a.IsPerson && !suit && F.RegionOf.Length > b.Cell && F.RegionOf[b.Cell] >= 0) _dangerVac.Add(F.RegionOf[b.Cell]);
        }
        if (++_stampId == int.MaxValue) { Array.Clear(_stamp); _stampId = 1; }
        _pq.Clear();
        _g[from] = 0; _from[from] = -1; _stamp[from] = _stampId;
        _pq.Enqueue(from, H(from, to, near));
        Span<int> nb = stackalloc int[4];
        int expanded = 0, goal = -1;
        while (_pq.TryDequeue(out int cur, out float f))
        {
            if (Goal(cur, to, near)) { goal = cur; break; }
            if (++expanded > MaxExpand) break;
            float gc = _g[cur];
            if (f - H(cur, to, near) > gc + 1e-3f) continue; // 낡은 항목
            L.Neighbors(cur, nb);
            foreach (int j in nb)
            {
                if (j < 0) continue;
                bool outside = !L.IsSpace(j);
                if (outside && !(eva && _hullOut[j])) continue;
                float cost = outside ? 2 : 1; // 외판 바깥은 자석 발로 천천히
                if (L.FurnitureAt[j] >= 0) cost += 0.5f;
                if (L.Kind[j] == CellKind.Door)
                {
                    int d = L.DoorAt[j];
                    if (!F.DoorPassable(d))
                    {
                        if (Believes(a, BeliefKind.StuckDoor, j, 0) > 0.3f) continue;
                        cost += 2;
                        if (!suit && Believes(a, BeliefKind.Leak, j, 0) > 0.4f) cost += 30; // 쉬익대는 닫힌 문 너머는 빠졌을지 모른다
                    }
                }
                foreach (int fc in _dangerFire) if (Dist(j, fc) <= 2) { cost += 15; break; }
                if (_dangerVac.Count > 0 && j < F.RegionOf.Length && F.RegionOf[j] >= 0 && _dangerVac.Contains(F.RegionOf[j]) && !_dangerVac.Contains(F.RegionOf[from] < 0 ? -2 : F.RegionOf[from])) cost += 25;
                float ng = gc + cost;
                if (_stamp[j] == _stampId && _g[j] <= ng) continue;
                _stamp[j] = _stampId; _g[j] = ng; _from[j] = cur;
                _pq.Enqueue(j, ng + H(j, to, near));
            }
        }
        if (goal < 0) return false;
        for (int x = goal; x != from && x >= 0; x = _from[x]) a.Path.Add(x);
        a.Path.Reverse();
        return true;
    }

    private bool Goal(int c, int to, int near) => near == 0 ? c == to : Dist(c, to) <= near;
    private float H(int c, int to, int near) => Math.Max(0, Manhattan(c, to) - near);
}
