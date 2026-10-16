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
                DropCarried(sim, a, true);
                Next(a, t);
                return;
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

    private void DropHeld(Agent a)
    {
        if (a.C.Holding == 255) return;
        Loose.Add(new LooseItem { Item = a.C.Holding, Cell = a.C.Cell, Charge = a.C.HoldCharge });
        a.C.Holding = 255; a.C.HoldCharge = 0;
    }

    private void Activity(Agent a, string kind)
    {
        if (!a.IsPerson) return;
        _life![a.C.Body].ActivityW = (float)(P.Watts.TryGetValue(kind, out var w) ? w : 90);
    }

    private void UseStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        var use = (UseKind)st.Arg;
        if (t <= c.StepStart && use == UseKind.Sleep && a.IsPerson) { _life![c.Body].Asleep = 1; c.SleptAt = t; }
        Activity(a, use switch { UseKind.Sleep => "sleep", UseKind.Work => "work", UseKind.None or UseKind.Relax or UseKind.Talk => "rest", _ => "rest" });
        bool done = t - c.StepStart >= st.Value;
        switch (use)
        {
            case UseKind.Sleep: done |= c.Sleepy < 0.03f && t - c.StepStart >= Time.Hours(5); break; // 한번 누우면 다섯 시간은 잔다
            case UseKind.Charge: done |= c.Battery >= 0.98f && t - c.StepStart >= Time.Minutes(10); break; // 다 차면 독에서 10분씩 기다린다
            case UseKind.Work: done |= !InWindow(Time.HourOfDay(t), c.ShiftHour, 8); break;
        }
        if (!done) return;
        switch (use)
        {
            case UseKind.Eat when a.IsPerson:
            {
                double got = 0;
                foreach (int x in L.Furniture[st.Target].Cells) { got += F.TakeStore(x, T.sFood, Math.Max(0, 1 - got)); if (got >= 1) break; }
                if (got > 0) { _life!.Eat(c.Body, got); FoodEaten += got; c.Hunger = (float)Math.Max(0, c.Hunger - got); c.Joy = Math.Min(1, c.Joy + 0.1f); c.AteAt = t; Meals++; }
                break;
            }
            case UseKind.Drink when a.IsPerson:
            {
                double want = Math.Max(15, c.Thirst * 0.02 * BodyWater0 + 10);
                double got = _net != null ? Math.Min(_net.WaterTank, want) : want;
                if (_net != null) _net.WaterTank -= got;
                _life!.Drink(c.Body, got);
                WaterDrunk += got;
                c.DrankAt = t; Drinks++;
                break;
            }
            case UseKind.Sleep when a.IsPerson:
                _life![c.Body].Asleep = 0; c.WokeAt = t;
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
                    int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 불을 껐다", t, center, c.GoalCause, 0, a.Id);
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
            if (c.Holding == iExt) { c.Holding = 255; c.HoldCharge = 0; } // 빈 소화기는 버린다
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
        if (F.Hole[wall] <= 0) { Forget(a, b => b.Kind == BeliefKind.Breach && b.Cell == wall); Next(a, t); return; }
        if (Dist(c.Cell, wall) > 1) { Fail(sim, a, "멀다"); return; }
        if (a.IsPerson && c.Holding != iPatch) { Fail(sim, a, "봉합 꾸러미가 없다"); return; }
        Activity(a, "hard");
        var kit = P.Items["봉합 꾸러미"];
        double minutes = a.IsPerson ? kit.Minutes * (1.5 - c.SkSeal) : kit.Minutes;
        if (t - c.StepStart < Time.Minutes(minutes)) return;
        double area = F.Hole[wall], max = kit.MaxArea * (a.IsPerson ? 1 : 2);
        _st!.Patch(wall, (float)Math.Max(0, area - max));
        if (a.IsPerson) { c.Holding = 255; c.HoldCharge = 0; }
        Patched++;
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 구멍을 막았다", t, wall, _st.DamageCause[wall], 0, a.Id);
        sim.Facts.Emit(t, FactKind.Patched, wall, (float)Math.Min(area, max), cause, a.Id);
        if (F.Hole[wall] <= 0)
        {
            Forget(a, b => b.Kind == BeliefKind.Breach && b.Cell == wall || b.Kind is BeliefKind.Leak or BeliefKind.Vacuum && Dist(b.Cell, wall) <= 6);
            a.Doubts.Add(new Doubt { Kind = BeliefKind.Leak, Cell = wall, Until = t + Time.Minutes(10) }); // 내가 막았다 — 한동안 '샌다'는 말을 반신반의
        }
        Next(a, t);
    }

    private void FixStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int dev = st.Target;
        if (_net == null || _net.Broken[dev * 4 + st.Arg] == 0) { Forget(a, b => b.Kind == BeliefKind.Broken && b.Subject == dev); Next(a, t); return; }
        if (Dist(c.Cell, _net.Devices[dev].Cell) > 2) { Fail(sim, a, "멀다"); return; }
        if (a.IsPerson && c.Holding != iRepair) { Fail(sim, a, "수리 꾸러미가 없다"); return; }
        Activity(a, "work");
        double minutes = a.IsPerson ? P.Items["수리 꾸러미"].Minutes * (1.6 - c.SkRepair) : 15;
        if (t - c.StepStart < Time.Minutes(minutes)) return;
        _net.Repair(dev, (Part)st.Arg);
        if (a.IsPerson) { c.Holding = 255; c.HoldCharge = 0; c.SkRepair = Math.Min(1, c.SkRepair + 0.01f); }
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
                    if (c.WaitDoor != d) { c.WaitDoor = d; c.WaitTicks = 0; }
                    c.WaitTicks++;
                    if (c.WaitTicks > 12 || (_st != null && _st.DoorJam[d] != 0 && F.DoorOpen[d] < 0.5f))
                    {
                        Learn(a, t, BeliefKind.StuckDoor, n, d, 1, BeliefSource.Felt);
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
            Move(a, n);
            a.PathIdx++;
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
    private void CloseBehind(Sim sim, Agent a)
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
        else if (dist < 2) return;
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
                if (j < 0 || !L.IsSpace(j)) continue;
                float cost = 1;
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
