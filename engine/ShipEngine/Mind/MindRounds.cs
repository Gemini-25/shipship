namespace ShipEngine;

/// <summary>
/// 깊이 고침 DF5 — 평시의 손 (docs/DEPTH_FIX.md · 근거 docs/ENGINE_DEPTH_REVIEW.md §4-3 · §4-10 · §5).
/// <para>순회 — 할 일이 없는 로봇은 충전독에서 놀지 않고 제 구역(방마다 가까운 충전독의 로봇 · 고르게 나눈다)을 돈다 (침실 · 씻는 방 · 통로 · 에어락은 빼고). 방마다 마지막으로 둘러본 때를 기억해
/// 한 시간 넘게 못 본 방이 생기면 한 바퀴 — 그 바퀴에 아직 못 본 방을 오래된 차례로 들러 둘러본다 (방 가운데 2분 · 그 방의 장치 셋까지 곁에서 1분씩 —
/// 지나가기만 한 방은 둘러본 것이 아니다) · 다 돌면 충전독에서 다음 바퀴를 기다린다.
/// 가서 본 것(켜 둔 조리대 · 멈춘 장치 · 숨진 이)이 다음 일이 된다. 전지가 절반 아래면 돌지 않는다.</para>
/// <para>보면 끈다 — 켜 둔 채 곁에 쓰는 이가 없는 조리대 · 오븐을 본 사람 · 로봇은 가서 끈다 (손이 닿는 두 칸 안이면 하던 일 그대로 손을 뻗어). 성실할수록 · 배의 규칙('떠날 땐 끈다')을 아는 이 · 조리사가 먼저
/// (사람은 로봇이 가는 것을 보면 맡긴다). 끈 일은 켜 둔 이의 원인에 잇는다 — 켜 둔 이는 남이 끈 것을 알고 조심이 오른다 (⑧ 요리 · 배우기).</para>
/// <para>수습 — 숨진 이를 덮어 쉴 곳(추모실 · 의무실 · 식량 창고 — 없으면 그 자리)에 뉜다. 주컴퓨터가 로봇에게 먼저 맡기고 · 돌던 로봇이 보면 스스로 ·
/// 곁에 사람이 있으면 (가슴을 누르거나 · 슬퍼하는 동안) 기다린다 · 배에 움직이는 로봇이 없으면 사람이 한다. 덮는 것은 ⑦에 요청한다.</para>
/// </summary>
public sealed partial class Mind
{
    public const double PatrolHours = 1;
    public const int PatrolLookMin = 5;
    /// <summary>순회의 값: 충전독에서 쉬기(0.4)보다는 크고 · 본 바닥 물건 줍기(0.8) · 맡긴 일보다는 작다.</summary>
    public const float PatrolScore = 0.6f;
    public long Patrols, SwitchedOff, LaidOutBodies;
    private long[] _looked = Array.Empty<long>();     // 로봇 × 방 — 그 방을 마지막으로 본 틱 + 1 (0: 아직 못 봤다 — 출항 때 본 것으로 · 찍는다)
    private long[] _roundAt = Array.Empty<long>();    // 로봇마다 이번 바퀴를 시작한 틱 (찍는다)
    private int[] _beat = Array.Empty<int>();          // 방 → 그 방을 도는 로봇 (−1 돌지 않는 방 — 로봇 명단 · 충전독에서 다시 셈한다 · 찍지 않는다)
    private int _beatRobots = -1;
    private int[] _patrolSpot = Array.Empty<int>();    // 방 → 둘러볼 자리 (빈 바닥 · 문에서 먼)
    private int _restRoom = -2;                        // 숨진 이를 뉘는 방 (추모실 · 의무실 · 식량 창고 — −1 없음 · −2 아직 안 셈)

    /// <summary>로봇이 둘러보는 방을 본 것으로 적는다 (감각 — 순회의 둘러보기 동안).</summary>
    private void LookedAt(Agent a, int room, long t)
    {
        int R = L.Rooms.Count, need = Agents.Count * R;
        if (_looked.Length < need) Array.Resize(ref _looked, need);
        _looked[a.Id * R + room] = t + 1;
    }

    /// <summary>도는 방: 에어락 · 통로 · 격납고 · 침실 · 씻는 방은 아니다 (사람의 방 — 자는 이를 깨우거나 씻는 이의 줄에 서지 않게 · 씻는 방은 주컴퓨터의 계량기가 본다).</summary>
    private static bool Patrollable(string type) => type is not ("Airlock" or "Corridor" or "Hangar" or "Quarters" or "Washroom");

    /// <summary>방마다 둘러볼 자리 · 도는 로봇 (로봇 수가 바뀌면 다시) — 방 차례로 가까운 충전독의 로봇에게 (이미 맡은 방 하나마다 25칸 멀게 쳐 고르게).</summary>
    private void BindBeats()
    {
        int R = L.Rooms.Count, robots = 0;
        foreach (var a in Agents) if (a.C.Kind == AgentKind.Robot) robots++;
        if (_beat.Length == R && _beatRobots == robots) return;
        _beatRobots = robots;
        if (_patrolSpot.Length != R)
        {
            _patrolSpot = new int[R];
            for (int r = 0; r < R; r++) _patrolSpot[r] = QuietSpot(r);
        }
        _beat = Enumerable.Repeat(-1, R).ToArray();
        var list = Agents.Where(a => a.C.Kind == AgentKind.Robot && a.C.Dock >= 0).ToList();
        if (list.Count == 0) return;
        var load = new int[list.Count];
        for (int r = 0; r < R; r++)
        {
            if (!Patrollable(L.Rooms[r].Type) || _patrolSpot[r] < 0) continue;
            int best = -1, bd = int.MaxValue;
            for (int k = 0; k < list.Count; k++)
            {
                int d = Manhattan(list[k].C.Dock, _patrolSpot[r]) + 25 * load[k];
                if (d < bd) { bd = d; best = k; }
            }
            _beat[r] = list[best].Id; load[best]++;
        }
    }

    /// <summary>방 안의 빈 바닥 가운데 문에서 가장 먼 칸 (같으면 작은 번호 — 없으면 −1): 둘러볼 자리 · 숨진 이를 뉠 자리.</summary>
    private int QuietSpot(int room, bool noBody = false)
    {
        int best = -1, bd = -1;
        foreach (int x in L.Rooms[room].Cells)
        {
            if (L.Kind[x] != CellKind.Floor || L.FurnitureAt[x] >= 0) continue;
            if (noBody && _life != null && BodyAt(x)) continue;
            int d = int.MaxValue;
            foreach (int dc in _roomDoors[room]) d = Math.Min(d, Manhattan(x, dc));
            if (_roomDoors[room].Length == 0) d = 0;
            if (d > bd) { bd = d; best = x; }
        }
        return best;
    }

    private bool BodyAt(int cell)
    {
        foreach (var b in _life!.Bodies) if (b.Cell == cell) return true;
        return false;
    }

    /// <summary>순회 (로봇의 판단): 제 구역에 한 시간 넘게 못 본 방이 있으면 바퀴를 시작하고 — 돌던 중이면 이번 바퀴에 아직 못 본 방까지 — 가장 오래된 방으로 (같으면 가까운).</summary>
    private void PatrolOption(Agent a, int here)
    {
        ref var c = ref a.C;
        if (c.Kind != AgentKind.Robot || c.Battery < 0.5f || L.Rooms.Count == 0) return;
        BindBeats();
        int R = L.Rooms.Count;
        if (_looked.Length < Agents.Count * R) Array.Resize(ref _looked, Agents.Count * R);
        if (_roundAt.Length < Agents.Count) Array.Resize(ref _roundAt, Agents.Count);
        long due = Time.Hours(PatrolHours);
        bool onRound = c.Goal == GoalKind.Patrol;
        if (onRound && a.Plan.Count > 0 && c.StepIndex < a.Plan.Count && c.GoalTarget >= 0 && c.GoalTarget < R)
        {
            Add(GoalKind.Patrol, c.GoalTarget, _patrolSpot[c.GoalTarget], PatrolScore);   // 둘러보는 중인 방은 다 둘러볼 때까지 (본 것으로 적혀도)
            return;
        }
        int best = -1, bestD = 0; long bestAge = -1;
        for (int r = 0; r < R; r++)
        {
            if (_beat[r] != a.Id) continue;
            long seen = _looked[a.Id * R + r];
            long age = _nowTick - Math.Max(0, seen - 1);   // 아직 못 본 방은 출항 때 본 것으로 (배는 멀쩡히 떠났다 — 첫 바퀴는 한 시간 뒤)
            if (onRound ? seen > _roundAt[a.Id] : age < due) continue;   // 돌던 중이면 이번 바퀴에 아직 못 본 방 · 아니면 때가 된 방
            if (BelievedRoomBad(a, r)) continue;
            int d = Manhattan(here, _patrolSpot[r]);
            if (age > bestAge || age == bestAge && d < bestD) { bestAge = age; best = r; bestD = d; }
        }
        if (best < 0) return;
        if (!onRound) _roundAt[a.Id] = _nowTick;   // 새 바퀴 (고르지 않으면 다음 고르기에 다시 적는다 — 같은 틱이면 같다)
        Add(GoalKind.Patrol, best, _patrolSpot[best], PatrolScore * ClaimFactor(a, GoalKind.Patrol, best, 1));
    }

    /// <summary>한 방 둘러보기: 방 가운데에서 2분 · 그 방의 장치를 중요한 차례로 셋까지 곁에서 1분씩 (멈춘 장치는 가까이 가야 보인다 — 감각이 본다).</summary>
    private bool PlanPatrol(Agent a, Option o, List<PlanStep> plan)
    {
        int r = o.Target;
        if (r < 0 || r >= _patrolSpot.Length || _patrolSpot[r] < 0) return false;
        int spot = _patrolSpot[r];
        plan.Add(Go(spot, 1));
        plan.Add(Use(UseKind.None, Time.Minutes(PatrolLookMin - 3), spot));   // 둘러본다 (켜진 것 · 고인 물 · 숨진 이)
        if (_net != null)
            foreach (var dev in _net.Devices.Where(d => d.Cell >= 0 && L.Room[d.Cell] == r).OrderByDescending(d => Importance(d.Id)).ThenBy(d => d.Id).Take(3))
            {
                plan.Add(Go(dev.Cell, 2));
                plan.Add(Use(UseKind.None, Time.Minutes(1), dev.Cell));
            }
        Patrols++;
        return true;
    }

    // ───────────────────────── 보면 끈다 ─────────────────────────

    private bool IsStove(int dev) => _net != null && dev >= 0 && dev < _net.Devices.Count && _net.Devices[dev].Def.Role == "stove";

    /// <summary>그 칸 곁(한 칸)에 깨어 있는 사람이 있나 — 쓰는 중으로 보인다 (사람의 눈 · 주컴퓨터의 카메라가 읽는다).</summary>
    public bool Attended(int cell, int except = -1)
    {
        foreach (var o in Agents)
        {
            if (!o.IsPerson || o.Id == except || o.C.Up == 0 || IsSleeping(o)) continue;
            if (Dist(o.C.Cell, cell) <= 1) return true;
        }
        return false;
    }

    /// <summary>눈: 조리대 · 오븐이 켜져 있는데 곁에 쓰는 이가 없다 (내가 짓는 밥은 아니다) — '켜 둔 채 놓였다' · 꺼졌거나 누가 쓰면 지운다.</summary>
    private void SeeStove(Sim sim, Agent a, long t, int x, int dev)
    {
        if (_net!.On[dev] == 0 || a.C.CookOn == dev + 1 || Attended(x, a.Id)) { Forget(a, BeliefKind.LeftOn, dev); return; }
        // 손이 닿는 곳(두 칸)이면 하던 일을 두고 가지 않고 손을 뻗어 끈다 (곁에서 일하던 조리사 — 하던 일이 끄러 가기보다 앞서 끝내 눌어 연기가 났다)
        if (a.C.Up != 0 && a.C.Mode == 0 && Dist(a.C.Cell, x) <= 2 && (!a.IsPerson || !IsSleeping(a)) && HandsOk(a)) { SwitchNow(sim, a, dev); return; }
        Learn(a, t, BeliefKind.LeftOn, x, dev, 1, BeliefSource.Saw, _net.OnCause[dev]);
    }

    private bool IsCook(Agent a) => a.IsPerson && a.C.Role < RoleNames.Length && RoleNames[a.C.Role] == "조리사";

    /// <summary>켜 둔 조리대를 본 이의 선택지: 사람은 성실할수록 · 배의 규칙을 알면 · 조리사면 먼저 (로봇이 가는 중이면 맡긴다) · 로봇은 곧장.</summary>
    private void SwitchOption(Agent a, in Belief b, float df, float caution)
    {
        if (_net == null || !IsStove(b.Subject) || _net.On[b.Subject] == 0) return;
        ref var c = ref a.C;
        float s = a.IsPerson ? b.Conf * (0.4f + 0.8f * c.Consc + (StoveRule ? 0.8f : 0) + (IsCook(a) ? 0.6f : 0)) * caution : b.Conf * 2.5f;
        Add(GoalKind.SwitchOff, b.Subject, _net.Devices[b.Subject].Cell, s * df * ClaimFactor(a, GoalKind.SwitchOff, b.Subject, 1), b.Cause);
    }

    private bool PlanSwitch(Option o, List<PlanStep> plan)
    {
        if (_net == null || !IsStove(o.Target) || _net.On[o.Target] == 0) return false;
        plan.Add(Go(_net.Devices[o.Target].Cell, 1));
        plan.Add(new PlanStep { Kind = StepKind.Switch, Target = o.Target });
        return true;
    }

    /// <summary>끈다: 곁에 가서 아직 켜져 있으면.</summary>
    private void SwitchStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int d = st.Target;
        if (_net == null || !IsStove(d)) { EndPlan(a); return; }
        var dev = _net.Devices[d];
        if (Dist(c.Cell, dev.Cell) > 1) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        SwitchNow(sim, a, d);
        Next(a, t);
    }

    /// <summary>켜 둔 조리대를 끈다 — 원인은 켜 둔 이의 마디에 잇는다 (⑧ 배우기가 읽는다).</summary>
    private void SwitchNow(Sim sim, Agent a, int d)
    {
        long t = sim.Tick;
        var dev = _net!.Devices[d];
        if (_net.On[d] != 0)
        {
            int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 켜 둔 채 쓰는 이가 없던 {Ko.Eul(dev.Name)} 껐다", t, dev.Cell, _net.OnCause[d], 0, a.Id);
            sim.Facts.Emit(t, FactKind.Switched, dev.Cell, 0, cause, a.Id, d);
            _net.SetOn(d, false);
            SwitchedOff++;
            if (a.IsPerson) { a.LastThought = $"누가 {Ko.Eul(dev.Name)} 켜 두고 갔네"; a.LastThoughtTick = t; }
        }
        Forget(a, BeliefKind.LeftOn, d);
    }

    // ───────────────────────── 시신 수습 ─────────────────────────

    /// <summary>숨진 이를 뉘는 방: 추모실 → 의무실 → 식량 창고 (없으면 −1 — 그 자리에서 덮는다).</summary>
    private int RestRoom()
    {
        if (_restRoom != -2) return _restRoom;
        _restRoom = -1;
        foreach (string type in new[] { "Chapel", "Medbay", "Storage" })
        {
            foreach (var r in L.Rooms) if (r.Type == type && QuietSpot(r.Id) >= 0) { _restRoom = r.Id; break; }
            if (_restRoom >= 0) break;
        }
        return _restRoom;
    }

    private bool AnyRobotUp()
    {
        foreach (var o in Agents) if (o.C.Kind == AgentKind.Robot && o.C.Up != 0) return true;
        return false;
    }

    /// <summary>숨진 이를 본 이의 선택지: 덮이지 않았고 · 누가 옮기는 중이 아니고 · 곁에 사람이 없으면 — 로봇이 먼저 (사람은 배에 움직이는 로봇이 없을 때).</summary>
    private void LayOutOption(Agent a, in Belief b, float df, float caution)
    {
        if (_life == null || b.Subject < 0 || b.Subject >= _life.Bodies.Count) return;
        var body = _life.Bodies[b.Subject];
        if (body.Alive != 0 || body.Shroud != 0 || body.Cell < 0) return;
        int va = AgentOfBody(b.Subject);
        if (va >= 0 && Agents[va].C.CarriedBy >= 0 && Agents[va].C.CarriedBy != a.Id) return;
        if (a.IsPerson ? AnyRobotUp() : Attended(body.Cell, a.Id)) return;   // 로봇은 곁의 사람이 (누르거나 · 슬퍼하는 동안) 물러날 때까지 기다린다
        ref var c = ref a.C;
        float s = a.IsPerson ? b.Conf * (0.3f + 0.5f * c.Kindness + 0.5f * c.SkMedic) * caution : b.Conf * 1.5f;
        Add(GoalKind.LayOut, b.Subject, body.Cell, s * df * ClaimFactor(a, GoalKind.LayOut, b.Subject, 1), b.Cause);
    }

    private bool PlanLayOut(Agent a, Option o, List<PlanStep> plan)
    {
        ref var c = ref a.C;
        int vb = o.Target;
        if (_life == null || vb < 0 || vb >= _life.Bodies.Count || c.Carrying >= 0) return false;
        var body = _life.Bodies[vb];
        if (body.Alive != 0 || body.Shroud != 0 || body.Cell < 0) return false;
        if (a.IsPerson && BelievedRoomBad(a, RoomOf(body.Cell))) return false;
        int rest = RestRoom(), spot = rest >= 0 && RoomOf(body.Cell) != rest ? QuietSpot(rest, noBody: true) : -1;
        plan.Add(Go(body.Cell, 1));
        if (spot >= 0)
        {
            plan.Add(new PlanStep { Kind = StepKind.PickUp, Target = vb, Arg = 2 });   // 2: 숨진 이를 옮긴다
            plan.Add(Go(spot));
            plan.Add(new PlanStep { Kind = StepKind.Drop, Arg = 2 });
        }
        plan.Add(new PlanStep { Kind = StepKind.Shroud, Target = vb });
        return true;
    }

    /// <summary>덮는다 (2분): ⑦에 덮개를 요청하고 · 원인은 그 죽음에 잇는다.</summary>
    private void ShroudStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int vb = st.Target;
        bool job = c.AssignedGoal == GoalKind.LayOut && c.AssignedTarget == vb && c.AssignedUntil > t;
        if (_life == null || vb < 0 || vb >= _life.Bodies.Count) { EndPlan(a); return; }
        var body = _life.Bodies[vb];
        if (body.Alive != 0 || body.Shroud != 0) { if (job) JobDone(a, t); else Next(a, t); return; }
        if (Dist(c.Cell, body.Cell) > 1) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        Activity(a, "work");
        if (t - c.StepStart < Time.Minutes(2)) return;
        _life.Cover(vb);
        int va = AgentOfBody(vb), r = RoomOf(body.Cell);
        string who = va >= 0 ? NameOf(va) : "숨진 이", where = r >= 0 ? L.Rooms[r].Name : "그 자리";
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} {Ko.Eul(who)} 수습했다 — 덮어 {where}에 뉘었다", t, body.Cell, body.DeathCause, 0, a.Id);
        sim.Facts.Emit(t, FactKind.LaidOut, body.Cell, 0, cause, a.Id, vb);
        LaidOutBodies++;
        if (job) JobDone(a, t); else Next(a, t);
    }

    private void SaveRounds(StateWriter w)
    {
        w.Arr(_looked); w.Arr(_roundAt); w.I64(Patrols); w.I64(SwitchedOff); w.I64(LaidOutBodies);
    }

    private void LoadRounds(StateReader r)
    {
        _looked = r.Arr<long>(); _roundAt = r.Arr<long>(); Patrols = r.I64(); SwitchedOff = r.I64(); LaidOutBodies = r.I64();
        _beatRobots = -1;
    }
}
