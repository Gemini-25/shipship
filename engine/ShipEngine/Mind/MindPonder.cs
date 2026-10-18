namespace ShipEngine;

/// <summary>
/// ⑧ 판단 엔진 (마-3) — 사람이 궁리한다 (docs/ENGINE.md 판단 엔진 (마)).
/// 급한 일(구멍 막기)이 문 · 길로 막히면 (같은 구멍은 10분에 한 번 · 공황이면 궁리하지 못한다 · 주컴퓨터 계획의 몸이면 주컴퓨터가 다시 푼다):
/// <list type="number">
/// <item>배우기 — 모르던 원리 때문에 막혔으면 그 분야를 조금 더 안다 (기압 차를 몰라 문을 그냥 열려다 막힘 → 기압 +0.05).</item>
/// <item>주컴퓨터가 그 방에 계획을 세웠으면 따른다 (따를 마음 = 0.4 + 주컴퓨터를 믿는 정도 × 0.4 + 성실 × 0.2 — 그 계획마다 한 번 정한다).</item>
/// <item>제 판단 — 제 믿음으로 그림을 그린다 (제 방 · 문이 열린 옆 방은 몸으로 안다 · 빠졌다고 믿는 방은 0kPa · 모르는 방은 보통 · 우주복 보관함 · 봉합 꾸러미) ·
///   제 지식으로 풀이기 (깊이 = 2 + 6 × 기압을 아는 정도 × 차분함 · 펼침도 그만큼) — 찾으면 '제 궁리' 목표로 몸이 하는 걸음만 (문 열기 · 닫기 · 지나기 · 바로잡기 · 틈 막기 · 구멍 막기 · 우주복).</item>
/// <item>혼자는 길이 없다 — 주컴퓨터가 켜져 있고 믿는 만큼 묻는다 (사실 Consulted: 주컴퓨터가 그 사건을 맡아 그 사람을 몸으로 함께 궁리한다 ·
///   주컴퓨터가 공기를 빼 주면 된다는 것까지 떠올렸으면 그 말을 한다).</item>
/// <item>그래도 없으면 '방법을 모르겠다'를 남기고 30분은 다른 일로 (원인 마디).</item>
/// </list>
/// 제 궁리로 해내면 그 분야 +0.03 · 주컴퓨터 계획의 몸으로 곁에서 보면 +0.02 (⑩이 알린다).
/// </summary>
public sealed partial class Mind
{
    public long Ponders, PondersOwn, PondersAsked, PondersLost, PondersDeferred, PondersLearned, OwnDone;
    private long[] _planRoomUntil = Array.Empty<long>(), _planRoomSince = Array.Empty<long>();
    private int _brainDevice = -2;

    /// <summary>⑩이 알린다: 그 방 사건에 주컴퓨터가 계획을 세웠다 (until까지).</summary>
    public void NoteComputerPlan(long now, int room, long until)
    {
        if (room < 0 || room >= L.Rooms.Count) return;
        if (_planRoomUntil.Length != L.Rooms.Count) { _planRoomUntil = new long[L.Rooms.Count]; _planRoomSince = new long[L.Rooms.Count]; }
        if (_planRoomUntil[room] <= now) _planRoomSince[room] = now;
        _planRoomUntil[room] = Math.Max(_planRoomUntil[room], until);
    }

    private bool ComputerPlanFor(int room) => room >= 0 && room < _planRoomUntil.Length && _planRoomUntil[room] > _nowTick;

    /// <summary>주컴퓨터가 켜져 있나 (사람이 아는 것 — 주컴퓨터 장치가 돌고 있다).</summary>
    private bool ComputerThere()
    {
        if (_net == null) return false;
        if (_brainDevice == -2) _brainDevice = _net.Devices.FirstOrDefault(d => d.Type == "MainComputer")?.Id ?? -1;
        return _brainDevice >= 0 && _net.Working(_brainDevice);
    }

    /// <summary>⑩이 알린다 (주컴퓨터 계획의 몸으로 곁에서 보았다) · 제 궁리로 해냈다 — 그 분야를 조금 더 안다.</summary>
    public void Learn(Sim sim, int agent, Domain d, float amount, string what)
    {
        if (agent < 0 || agent >= Agents.Count || !Agents[agent].IsPerson) return;
        ref var c = ref Agents[agent].C;
        ref float k = ref c.KnPressure;
        switch (d) { case Domain.Electric: k = ref c.KnElectric; break; case Domain.Chemistry: k = ref c.KnChem; break; case Domain.Body: k = ref c.KnBody; break; case Domain.Structure: k = ref c.KnStruct; break; }
        if (k >= 1) return;
        k = Math.Min(1, k + amount);
        PondersLearned++;
        sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(agent))} {what}", sim.Tick, c.Cell, c.GoalCause, 0, agent);
    }

    private static bool PonderSnag(SnagKind k) => DoorSnag(k) || k is SnagKind.NoPath or SnagKind.DoorStuckBefore;

    /// <summary>막혔다 — 궁리한다 (Fail이 목표를 거두기 전에).</summary>
    private void Ponder(Sim sim, Agent a, SnagKind k, int cell, int subject)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        if (!a.IsPerson || c.Mode != 0 || c.Goal != GoalKind.Patch || !PonderSnag(k)) return;
        if (c.AssignedArg == PlanBound && c.AssignedUntil > t) return;   // 주컴퓨터 계획의 몸 — 주컴퓨터가 다시 푼다
        if (_life != null && _life[c.Body].Conscious == 0) return;
        int wall = c.GoalTarget;
        int room = HoleRoom(a, wall);
        if (room < 0) return;
        if (c.PonderTarget == wall && t - c.PonderTick < Time.Minutes(10)) return;
        c.PonderTick = t; c.PonderTarget = wall; Ponders++;
        string who = Ko.I(NameOf(a.Id)), rn = L.Rooms[room].Name;
        var know = KnowOf(a);
        // ① 배우기: 모르던 원리 때문에 막혔다
        if (k is SnagKind.DoorVacuum or SnagKind.DoorPressure && !know.Has(Principle.DoorLimit))
        {
            Learn(sim, a.Id, Domain.Pressure, 0.05f, "기압 차가 큰 문은 열리지 않는다는 것을 겪어 알았다");
            know = KnowOf(a);
        }
        // ② 주컴퓨터의 계획
        if (ComputerPlanFor(room) && HeedsPlan(sim, a, room))
        {
            PondersDeferred++; c.PonderWay = 2;
            Thought(sim, a, $"생각: {rn} 구멍 — {SnagText(k)} → 주컴퓨터가 계획을 세웠다 · 따른다");
            AvoidFor(a, GoalKind.Patch, wall, t + Time.Minutes(30));
            return;
        }
        // ③ 제 판단 (제 믿음 · 제 지식)
        float calm = Math.Clamp((0.5f + 0.5f * c.Calm) * (1 - c.Fear), 0, 1);
        int depth = 2 + (int)MathF.Round(6 * c.KnPressure * calm);
        int budget = 150 + (int)(1500 * c.KnPressure * calm);
        var pic = PersonPicture(a, room, k, subject);
        if (pic == null) return;
        var res = Solver.Solve(pic, know, false, depth, budget);
        if (res.Found && res.Risk <= 0 && res.Steps.Count > 0 && OwnPlan(a, pic, res, know, wall))
        {
            PondersOwn++; c.PonderWay = 1;
            c.OwnUntil = t + Time.Minutes(res.Minutes * 3 + 15);
            string plan = string.Join(" → ", DescribeSteps(pic, Solver.Expand(pic, res.Steps, know, false).Select(x => x.step).ToList()));
            int cause = Thought(sim, a, $"생각: {rn} 구멍 — {SnagText(k)} → 제 궁리: {plan}");
            sim.Facts.Emit(t, FactKind.Devised, wall, res.Minutes, cause, a.Id, room);
            return;
        }
        // ④ 주컴퓨터에 묻는다 (켜져 있고 믿는 만큼 — 주컴퓨터 일까지 떠올렸으면 그 말을)
        var idea = Solver.Solve(pic, know, true, depth, budget);
        bool thought = idea.Found && idea.Risk <= 0;
        if (ComputerThere() && (thought || AsksComputer(sim, a, wall)))
        {
            PondersAsked++; c.PonderWay = 3;
            string why = thought && idea.Steps.Any(s => s.Act == ActKind.Pump) ? "혼자는 길이 없다 · 주컴퓨터가 앞방 공기를 빼 주면 들어갈 수 있다" : thought ? "혼자는 길이 없다 · 주컴퓨터가 도와주면 된다" : "방법을 모르겠다";
            int cause = Thought(sim, a, $"생각: {rn} 구멍 — {SnagText(k)} → {why} → 주컴퓨터에 묻는다");
            sim.Facts.Emit(t, FactKind.Consulted, wall, thought ? 1 : 0, cause, a.Id, room);
            AvoidFor(a, GoalKind.Patch, wall, t + Time.Minutes(20));
            return;
        }
        // ⑤ 방법을 모르겠다
        PondersLost++; c.PonderWay = 4;
        Thought(sim, a, $"{rn} 구멍을 막을 방법을 모르겠다 — {SnagText(k)}");
        AvoidFor(a, GoalKind.Patch, wall, t + Time.Minutes(30));
    }

    /// <summary>제 궁리를 하다 막혔다 — 그 계획을 버리고 (모르던 것을 배우고) 다시 궁리한다.</summary>
    private void OwnFailed(Sim sim, Agent a, SnagKind k, int cell, int subject)
    {
        ref var c = ref a.C;
        a.Own.Clear(); c.OwnUntil = 0;
        c.Goal = GoalKind.Patch; c.GoalTarget = c.PonderTarget;
        c.PonderTick = 0;   // 같은 구멍이어도 곧바로 다시 궁리한다 (해 보고 알았다)
        Ponder(sim, a, k, cell, subject);
    }

    /// <summary>제 궁리를 다 했다 — 구멍이 막혔으면 해낸 것을 배운다.</summary>
    private void OwnFinished(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        a.Own.Clear(); c.OwnUntil = 0;
        int wall = c.PonderTarget;
        if (wall < 0 || wall >= L.N || F.Hole[wall] > 0) return;
        OwnDone++;
        Learn(sim, a.Id, Domain.Pressure, 0.03f, "제 궁리로 구멍을 막아 냈다");
    }

    /// <summary>제 궁리 선택지 (그 계획이 살아 있는 동안 — 급한 일).</summary>
    private void OwnOption(Agent a)
    {
        ref var c = ref a.C;
        if (a.Own.Count == 0) return;
        if (c.OwnUntil <= _nowTick) { a.Own.Clear(); return; }
        Add(GoalKind.Improvise, c.PonderTarget, c.PonderTarget, 2.6f, c.GoalCause);
    }

    private int Thought(Sim sim, Agent a, string text)
    {
        a.LastThought = text; a.LastThoughtTick = sim.Tick;
        return sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} {text}", sim.Tick, a.C.Cell, a.C.GoalCause, 0, a.Id);
    }

    /// <summary>화면용: 그 사람의 최근 궁리 (within 안이면 — 아니면 빈 글).</summary>
    public string RecentThought(int agent, long now, long within) => agent >= 0 && agent < Agents.Count && now - Agents[agent].LastThoughtTick <= within ? Agents[agent].LastThought : "";

    private static void AvoidFor(Agent a, GoalKind g, int target, long until)
    {
        a.Avoid.Add(new AvoidEntry { Goal = g, Target = target, Until = until });
        if (a.Avoid.Count > 16) a.Avoid.RemoveAt(0);
    }

    private bool HeedsPlan(Sim sim, Agent a, int room)
    {
        float trust = _soc?.Trust(a.Id) ?? 0.5f;
        float will = 0.4f + 0.4f * trust + 0.2f * a.C.Diligent;
        return Rng.Unit(sim.Seed, Rng.Key("계획 따름"), a.Id, room * 7919L + _planRoomSince[room]) < Math.Clamp(will, 0.05f, 0.98f);
    }

    private bool AsksComputer(Sim sim, Agent a, int wall)
    {
        float trust = _soc?.Trust(a.Id) ?? 0.5f;
        return Rng.Unit(sim.Seed, Rng.Key("주컴퓨터에 묻기"), a.Id, wall * 31L + sim.Tick / Time.Hours(1)) < Math.Clamp(0.25f + 0.6f * trust, 0.05f, 0.95f);
    }

    /// <summary>구멍(벽 · 바닥 칸)이 난 방 — 곁 바닥 가운데 제가 있는 방이 아닌 쪽 (없으면 그 방).</summary>
    private int HoleRoom(Agent a, int wall)
    {
        if (wall < 0 || wall >= L.N) return -1;
        if (L.Kind[wall] == CellKind.Floor) return L.Room[wall];
        int mine = RoomOf(a.C.Cell), any = -1;
        int x0 = L.X(wall), y0 = L.Y(wall);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!L.In(x0 + dx, y0 + dy)) continue;
                int j = L.Index(x0 + dx, y0 + dy), r = L.Room[j];
                if (r < 0 || L.Kind[j] != CellKind.Floor) continue;
                if (r != mine) return r;
                any = r;
            }
        return any;
    }

    /// <summary>그 문 칸의 두 방.</summary>
    private (int a, int b) DoorRoomPair(int dc)
    {
        Span<int> nb = stackalloc int[4];
        L.Neighbors(dc, nb);
        int ra = -1, rb = -1;
        foreach (int j in nb)
        {
            if (j < 0 || L.Room[j] < 0) continue;
            if (ra < 0) ra = L.Room[j]; else if (L.Room[j] != ra) rb = L.Room[j];
        }
        return (ra, rb);
    }

    /// <summary>그 방을 그 믿음으로 믿나 (그 방 칸의 믿음 가운데 가장 센 것).</summary>
    private float BelievesRoom(Agent a, BeliefKind kind, int room)
    {
        float best = 0;
        foreach (var b in a.Beliefs) if (b.Kind == kind && b.Conf > best && b.Cell >= 0 && b.Cell < L.N && (L.Room[b.Cell] == room || HoleRoomOfCell(b.Cell) == room)) best = b.Conf;
        return best;
    }

    private int HoleRoomOfCell(int c)
    {
        if (L.Kind[c] != CellKind.Wall) return -1;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(c, nb);
        foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0 && L.Kind[j] == CellKind.Floor) return L.Room[j];
        return -1;
    }

    /// <summary>
    /// 그 사람의 그림 — 제 믿음으로 (주컴퓨터처럼 센서로 다 아는 것이 아니다): 제 방에서 문 셋 거리까지 + 구멍 난 방 ·
    /// 제 방 · 문이 열린 옆 방은 몸으로 아는 기압 · 빠졌다고 믿거나 '너머가 우주로 트였다'로 막힌 방은 0kPa · 모르는 방은 보통(101kPa) ·
    /// 구멍 크기는 모른다 (한 번에 막는 만큼) · 선체 바깥으로는 나가지 않는다 (사람은 선외 작업을 하지 않는다).
    /// </summary>
    private Picture? PersonPicture(Agent a, int goalRoom, SnagKind k, int snagDoor)
    {
        ref var c = ref a.C;
        int here = RoomOf(c.Cell);
        if (here < 0) { Span<int> nb0 = stackalloc int[4]; L.Neighbors(c.Cell, nb0); foreach (int j in nb0) if (j >= 0 && L.Room[j] >= 0) { here = L.Room[j]; break; } }
        if (here < 0) return null;
        var depth = new Dictionary<int, int> { [here] = 0 };
        var q = new Queue<int>(); q.Enqueue(here);
        while (q.Count > 0 && depth.Count < 14)
        {
            int r = q.Dequeue();
            if (depth[r] >= 3) continue;
            foreach (int dc in _roomDoors[r])
            {
                if (L.DoorAt[dc] < 0 || L.Doors[L.DoorAt[dc]].External) continue;
                var (ra, rb) = DoorRoomPair(dc);
                int o = ra == r ? rb : ra;
                if (o < 0 || depth.ContainsKey(o)) continue;
                depth[o] = depth[r] + 1; q.Enqueue(o);
            }
        }
        if (!depth.ContainsKey(goalRoom)) depth[goalRoom] = 9;
        var rooms = depth.Keys.OrderBy(r => depth[r]).ThenBy(r => r).ToList();
        var pic = new Picture();
        var placeOf = new Dictionary<int, int>();
        float RoomKPaAt(int r) => L.Rooms[r].Cells.Length > 0 ? (float)F.Pressure(L.Rooms[r].Cells[L.Rooms[r].Cells.Length / 2]) : 101.3f;
        bool OpenTo(int r) => _roomDoors[here].Any(dc => L.DoorAt[dc] >= 0 && F.DoorOpen[L.DoorAt[dc]] > 0.02f && DoorRoomPair(dc) is var p && (p.a == r || p.b == r));
        foreach (int r in rooms)
        {
            float kpa;
            if (r == here) kpa = (float)F.Pressure(c.Cell);
            else if (r == goalRoom && (k is SnagKind.DoorVacuum or SnagKind.DoorPressure || BelievesRoom(a, BeliefKind.Vacuum, r) > 0.3f)) kpa = 0;
            else if (BelievesRoom(a, BeliefKind.Vacuum, r) > 0.3f) kpa = 0;
            else if (OpenTo(r)) kpa = RoomKPaAt(r);   // 문이 열린 옆 방 — 바람 · 귀로 안다
            else kpa = 101.3f;                        // 모르는 방은 보통이라 여긴다
            byte people = 0;
            if (r == here && _life != null) foreach (var o in Agents) if (o.Id != a.Id && o.IsPerson && _life[o.C.Body].Alive != 0 && _life[o.C.Body].Suit == 0 && RoomOf(o.C.Cell) == r && people < 250) people++;
            placeOf[r] = pic.AddPlace(r, L.Rooms[r].Name, L.Rooms[r].Cells.Length, kpa, vent: true, hull: false,
                hole: r == goalRoom || BelievesRoom(a, BeliefKind.Breach, r) > 0.3f && kpa < 5, people: people);
        }
        var seen = new HashSet<int>();
        foreach (int r in rooms)
            foreach (int dc in _roomDoors[r])
            {
                int d = L.DoorAt[dc];
                if (d < 0 || L.Doors[d].External || !seen.Add(d)) continue;
                var (ra, rb) = DoorRoomPair(dc);
                if (ra < 0 || rb < 0 || !placeOf.TryGetValue(ra, out int pa) || !placeOf.TryGetValue(rb, out int pb)) continue;
                bool open = F.DoorOpen[d] > 0.02f;
                bool normal = L.Doors[d].Kind == "normal";
                bool jam = a.Beliefs.Any(b => b.Kind == BeliefKind.StuckDoor && b.Subject == d && b.Conf > 0.3f) || k == SnagKind.DoorJammed && snagDoor == d;
                pic.AddDoor(pa, pb, d, powered: _st == null || _st.DoorPowered[d] != 0, open: open, jam: jam, leaky: normal && !open && F.DoorTaped[d] == 0, normal: normal);
            }
        pic.Body = placeOf[here];
        pic.BodyBreathes = _life != null && _life[c.Body].Suit == 0;
        int kits = c.Holding == iPatch ? 1 : 0;
        foreach (var (r, p) in placeOf) { pic.Lockers[p] = StockIn(r, iSuit) >= 0; kits += CountIn(r, iPatch); }
        pic.Kits = kits;
        pic.PatchArea = (float)PatchKitArea;
        pic.Goal = placeOf[goalRoom];
        return pic;
    }

    /// <summary>풀이기의 걸음을 그 사람의 몸 걸음으로 (문 앞으로 가 열기 · 닫기 · 지나 그 방으로 · 꾸러미를 챙겨 바로잡기 · 틈 막기 · 구멍 막기 · 보관함에서 우주복).</summary>
    private bool OwnPlan(Agent a, Picture pic, SolveResult res, Know know, int wall)
    {
        var steps = Solver.Expand(pic, res.Steps, know, false);
        var plan = new List<PlanStep>();
        int body = pic.Body, at = a.C.Cell;
        byte hold = a.C.Holding;
        bool Kit(byte item)
        {
            if (hold == item) return true;
            if (hold != 255) return false;
            int src = NearestStock(at, item);
            if (src < 0) return false;
            plan.Add(Go(L.Furniture[src].Cells[0], 1));
            plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = item });
            hold = item;
            return true;
        }
        int Side(int dc, int room)
        {
            Span<int> nb = stackalloc int[4];
            L.Neighbors(dc, nb);
            foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Floor && L.Room[j] == room) return j;
            return -1;
        }
        foreach (var (s, _) in steps)
        {
            int dc = Solver.DoorAct(s.Act) && s.Obj >= 0 && s.Obj < pic.D && pic.Door[s.Obj] >= 0 ? L.Doors[pic.Door[s.Obj]].Cell : -1;
            switch (s.Act)
            {
                case ActKind.Open:
                case ActKind.Close:
                    if (dc < 0) return false;
                    plan.Add(Go(dc, 1));
                    plan.Add(new PlanStep { Kind = StepKind.Door, Cell = dc, Arg = (byte)(s.Act == ActKind.Open ? 1 : 0) });
                    break;
                case ActKind.Go:
                {
                    if (dc < 0) return false;
                    int to = pic.DA[s.Obj] == body ? pic.DB[s.Obj] : pic.DA[s.Obj];
                    int dest = Side(dc, pic.Room[to]);
                    if (dest < 0) return false;
                    plan.Add(Go(dest));
                    body = to; at = dest;
                    break;
                }
                case ActKind.Unjam:
                    if (dc < 0 || !Kit(iRepair)) return false;
                    plan.Add(Go(dc, 1));
                    plan.Add(new PlanStep { Kind = StepKind.Brace, Cell = dc });
                    hold = 255;
                    break;
                case ActKind.Tape:
                {
                    if (dc < 0 || !Kit(iPatch)) return false;
                    int stand = Side(dc, pic.Room[body]);
                    plan.Add(Go(stand >= 0 ? stand : dc, stand >= 0 ? 0 : 1));
                    plan.Add(new PlanStep { Kind = StepKind.Seal, Cell = dc });
                    hold = 255;
                    break;
                }
                case ActKind.Patch:
                {
                    if (!Kit(iPatch)) return false;
                    int stand = L.Kind[wall] == CellKind.Floor ? wall : StandBeside(wall);
                    if (stand < 0) return false;
                    plan.Add(Go(stand, L.Kind[wall] == CellKind.Floor ? 1 : 0));
                    plan.Add(new PlanStep { Kind = StepKind.Seal, Cell = wall });
                    hold = 255;
                    break;
                }
                case ActKind.Suit:
                {
                    int locker = StockIn(pic.Room[s.Obj], iSuit);
                    if (locker < 0) return false;
                    plan.Add(Go(L.Furniture[locker].Cells[0], 1));
                    plan.Add(new PlanStep { Kind = StepKind.Wear, Target = locker, Arg = iSuit });
                    break;
                }
                default: return false;   // 주컴퓨터 일 (되돌리기 · 채우기 · 내보내기) — 사람 혼자는 못 한다
            }
        }
        a.Own.Clear(); a.Own.AddRange(plan);
        return plan.Count > 0;
    }

    private static List<string> DescribeSteps(Picture pic, List<SolveStep> steps)
    {
        var parts = new List<string>();
        int body = pic.Body;
        foreach (var s in steps)
        {
            parts.Add(Solver.Text(pic, s, body));
            if (s.Act == ActKind.Go) body = pic.DA[s.Obj] == body ? pic.DB[s.Obj] : pic.DA[s.Obj];
        }
        return parts;
    }

    /// <summary>문을 열거나 닫는다 (제 궁리의 걸음 — 문 곁에서 · 1분 반 안에 안 되면 그 까닭으로 막힌다).</summary>
    private void DoorStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int d = st.Cell >= 0 && st.Cell < L.N ? L.DoorAt[st.Cell] : -1;
        if (d < 0 || _st == null) { Next(a, t); return; }
        bool open = st.Arg == 1;
        if (open ? F.DoorOpen[d] >= 0.9f : F.DoorOpen[d] <= 0.02f) { if (c.OpenedDoor == d) c.OpenedDoor = -1; Next(a, t); return; }
        if (Dist(c.Cell, st.Cell) > 1) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        _st.RequestDoor(d, open ? 1 : 0);
        if (t - c.StepStart > Time.Minutes(1.5))
        {
            var why = WhyDoor(d);
            Fail(sim, a, open ? "문이 열리지 않는다" : "문이 닫히지 않는다", why != SnagKind.None ? why : SnagKind.Other, st.Cell, d);
        }
    }

    /// <summary>제 궁리가 막으라고 한 문틈인가 (닫힌 보통 문 — 아직 기압 차가 없어 새지 않아도).</summary>
    private bool OwnTape(Agent a, int cell) => a.C.Goal == GoalKind.Improvise && L.Kind[cell] == CellKind.Door && L.DoorAt[cell] >= 0 && F.DoorGapOpen(L.DoorAt[cell]);

    private void SavePonder(StateWriter w)
    {
        w.Arr(_planRoomUntil); w.Arr(_planRoomSince);
        w.I64(Ponders); w.I64(PondersOwn); w.I64(PondersAsked); w.I64(PondersLost); w.I64(PondersDeferred); w.I64(PondersLearned); w.I64(OwnDone);
    }

    private void LoadPonder(StateReader r)
    {
        _planRoomUntil = r.Arr<long>(); _planRoomSince = r.Arr<long>();
        Ponders = r.I64(); PondersOwn = r.I64(); PondersAsked = r.I64(); PondersLost = r.I64(); PondersDeferred = r.I64(); PondersLearned = r.I64(); OwnDone = r.I64();
    }
}
