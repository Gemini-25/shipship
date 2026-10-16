namespace ShipEngine;

/// <summary>⑧ 마음 — 목표 고르기(점수) · 계획 세우기(단계).</summary>
public sealed partial class Mind
{
    private struct Option
    {
        public GoalKind Goal;
        public int Target, Cell, Cause;
        public float Score;
    }

    private readonly List<Option> _opts = new();

    private void Decide(Sim sim, Agent a)
    {
        long t = sim.Tick;
        ref var c = ref a.C;
        _opts.Clear();
        a.Avoid.RemoveAll(v => v.Until <= t);
        _deciding = a;
        Options(sim, a);
        _deciding = null;
        if (_opts.Count == 0) return;
        _opts.Sort((x, y) => y.Score.CompareTo(x.Score) is var k && k != 0 ? k : x.Goal.CompareTo(y.Goal));

        bool busy = a.Plan.Count > 0 && c.StepIndex < a.Plan.Count;
        if (busy && c.Carrying >= 0) return; // 업은 사람을 내려놓기 전에는 다른 일을 하지 않는다
        float current = -1;
        if (busy)
            foreach (var o in _opts)
                if (o.Goal == c.Goal && o.Target == c.GoalTarget) { current = o.Score + 1.0f; break; }
        if (busy && current < 0 && c.Goal is GoalKind.Sleep or GoalKind.Eat or GoalKind.Drink && c.StepIndex > 0) current = 1.5f; // 하던 식사 · 잠은 웬만하면 마친다

        for (int k = 0; k < Math.Min(4, _opts.Count); k++)
        {
            var o = _opts[k];
            if (busy && o.Goal == c.Goal && o.Target == c.GoalTarget) return;  // 지금 것이 가장 낫다
            if (busy && o.Score <= current) return;
            if (TryPlan(sim, a, o)) return;
        }
    }

    private bool TryPlan(Sim sim, Agent a, Option o)
    {
        var plan = new List<PlanStep>();
        if (!BuildPlan(sim, a, o, plan)) return false;
        ref var c = ref a.C;
        if (IsSleeping(a)) _life![c.Body].Asleep = 0;
        if (c.Carrying >= 0) DropCarried(sim, a, false);
        if (IsTask(c.Goal)) Claim(a, c.Goal, c.GoalTarget, -1);
        c.Goal = o.Goal; c.GoalTarget = o.Target; c.GoalCell = o.Cell; c.GoalCause = o.Cause; c.GoalScore = o.Score; c.GoalSince = sim.Tick;
        c.Failed = 0;
        a.Plan = plan; c.StepIndex = 0; c.StepStart = sim.Tick; a.Path.Clear(); a.PathIdx = 0; c.WaitTicks = 0;
        c.CloseHard = (byte)(o.Goal == GoalKind.Evacuate ? 1 : 0);
        if (IsTask(o.Goal)) Claim(a, o.Goal, o.Target, +1);
        return true;
    }

    // ───────────────────────── 점수 ─────────────────────────

    private float DistFactor(int from, int to) => 1f / (1f + Manhattan(from, to) / 40f);

    /// <summary>일감 표시에 따른 배율: 필요한 수가 찼으면 0.15 · 사람이 하려는데 로봇이 맡았으면 0.1 (로봇이 먼저).</summary>
    private float ClaimFactor(Agent a, GoalKind g, int target, int need)
    {
        if (!_claims.TryGetValue(ClaimKey(g, target), out var v)) return 1;
        int n = v.n - (a.C.Goal == g && a.C.GoalTarget == target ? 1 : 0);
        if (a.IsPerson && v.robot && !(a.C.Goal == g && a.C.GoalTarget == target)) return 0.1f;
        return n >= need ? 0.15f : 1;
    }

    private Agent? _deciding;

    private void Add(GoalKind g, int target, int cell, float score, int cause = 0)
    {
        if (score <= 0.01f) return;
        if (_deciding != null)
            foreach (var v in _deciding.Avoid) if (v.Goal == g && v.Target == target) return;
        _opts.Add(new Option { Goal = g, Target = target, Cell = cell, Score = score, Cause = cause });
    }

    private void Options(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        int here = c.Cell;
        bool person = a.IsPerson;
        bool drone = c.Kind == AgentKind.Drone;
        double hour = Time.HourOfDay(sim.Tick);

        if (person)
        {
            // 졸림이 0.6을 넘거나, 잠잘 시간대에 0.4를 넘으면 잔다 (덜 졸린데 누우면 짧게 깬다)
            bool night = InWindow(hour, c.ShiftHour + 16, 8);
            if (c.Sleepy > 0.6f || night && c.Sleepy > 0.4f) Add(GoalKind.Sleep, c.Bed, c.Bed >= 0 ? c.Bed : here, c.Sleepy * c.Sleepy * 5 + (night ? 1.5f : 0));
            // 욕구는 어느 만큼 차야 움직인다 (배가 조금 고프다고 바로 먹으러 가지 않는다)
            if (c.Hunger > 0.55f) Add(GoalKind.Eat, -1, -1, c.Hunger * c.Hunger * 5 + (F.Ppm(T.gAroma, here) > 5 ? 0.5f : 0));
            if (c.Thirst > 0.3f) Add(GoalKind.Drink, -1, -1, c.Thirst * c.Thirst * 6);
            if (InWindow(hour, c.ShiftHour, 8) && c.Station >= 0) Add(GoalKind.Work, c.Station, L.Furniture[c.Station].Cells[0], 1.2f + c.Diligent);
            if (c.Bored > 0.3f) Add(GoalKind.Relax, -1, -1, c.Bored * 1.5f);
            if (c.Lonely > 0.3f) Add(GoalKind.Socialize, -1, -1, c.Lonely * (0.5f + c.Sociable) * 1.5f);
            Add(GoalKind.Idle, -1, here, 0.3f);
        }
        else
        {
            float low = c.Battery < 0.1f ? 8 : c.Battery < 0.3f ? 4 : c.Battery < 0.95f && c.Goal == GoalKind.Charge ? 1.5f : 0.4f;
            Add(GoalKind.Charge, c.Dock, c.Dock, low);
        }

        float caution = person ? 1 - c.Fear * (1 - c.Brave) : 1;
        if (person && _life![c.Body].Suit == 0)
        {
            // 내 방이 위험하다고 믿으면 나간다 (문은 닫고) — 압력이 다 빠지기 전, 문이 아직 열릴 때
            int myRoom = RoomOf(here);
            float worst = 0; int cell = -1;
            foreach (var b in a.Beliefs)
            {
                if (b.Conf < 0.35f || myRoom < 0 || !InRoom(b.Cell, myRoom)) continue;
                float s = b.Kind switch
                {
                    BeliefKind.Breach or BeliefKind.Vacuum => 8 * b.Conf,
                    BeliefKind.Leak when b.Conf >= 0.5f && L.Kind[b.Cell] != CellKind.Door => 6 * b.Conf, // 쉬익대는 닫힌 문은 그 너머가 빠진 것
                    BeliefKind.Smoke when b.Conf >= 0.5f => 4 * b.Conf,
                    BeliefKind.Fire => 3 * b.Conf * (1.3f - c.Brave),
                    BeliefKind.BadAir => 3 * b.Conf,
                    _ => 0,
                };
                if (s > worst) { worst = s; cell = b.Cell; }
            }
            // 이 방의 위험을 다루러 들어온 사람(불 끄기 · 구하기 · 막기)은 나가지 않는다 — 정말 위험하면 본능이 끊는다
            bool committed = c.Goal is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch && a.Plan.Count > 0 && c.StepIndex < a.Plan.Count && InRoom(c.GoalCell, myRoom);
            if (worst > 0 && !committed) Add(GoalKind.Evacuate, myRoom, cell, worst);
        }
        foreach (var b in a.Beliefs)
        {
            if (b.Conf < 0.25f) continue;
            float df = DistFactor(here, b.Cell);
            switch (b.Kind)
            {
                case BeliefKind.Fire:
                {
                    int target = b.Cell;
                    float s = person ? b.Conf * (0.5f + c.SkFire + c.Brave) * 2.5f * caution : b.Conf * 6;
                    Add(GoalKind.FightFire, target, b.Cell, s * df * ClaimFactor(a, GoalKind.FightFire, target, 2), b.Cause);
                    break;
                }
                case BeliefKind.Injured when !drone && (b.Src is BeliefSource.Told or BeliefSource.Heard || !SafeAt(b.Cell)):
                {
                    float s = person ? b.Conf * (1 + c.Kindness + c.SkMedic) * 2.2f * caution : b.Conf * 5.5f;
                    Add(GoalKind.Rescue, b.Subject, b.Cell, s * df * ClaimFactor(a, GoalKind.Rescue, b.Subject, 1), b.Cause);
                    break;
                }
                case BeliefKind.Breach when !drone && F.Hole[b.Cell] > 0 || b.Kind == BeliefKind.Breach && !drone && b.Conf > 0.6f:
                {
                    float s = person ? b.Conf * (0.3f + c.SkSeal) * 3 * caution : b.Conf * 5;
                    Add(GoalKind.Patch, b.Cell, b.Cell, s * df * ClaimFactor(a, GoalKind.Patch, b.Cell, 1), b.Cause);
                    break;
                }
                case BeliefKind.Broken when !drone && b.Subject >= 0:
                {
                    float imp = Importance(b.Subject);
                    float s = person ? b.Conf * c.SkRepair * 2.5f * imp : b.Conf * 3.5f * imp;
                    Add(GoalKind.Repair, b.Subject, b.Cell, s * df * ClaimFactor(a, GoalKind.Repair, b.Subject, 1), b.Cause);
                    break;
                }
                case BeliefKind.Bang or BeliefKind.Strain or BeliefKind.Leak or BeliefKind.SmokeSmell or BeliefKind.Help or BeliefKind.Spark:
                {
                    if (b.Src == BeliefSource.Saw && b.Kind != BeliefKind.Spark) break; // 이미 본 자리
                    if (Dist(here, b.Cell) <= 1) break;
                    float s = person ? b.Conf * ((c.Curious + c.Diligent) * 1.2f + (b.Kind == BeliefKind.Help ? c.Kindness * 2 : 0)) * caution : b.Conf * 3;
                    int key = b.Cell;
                    Add(GoalKind.Investigate, key, b.Cell, s * df * ClaimFactor(a, GoalKind.Investigate, key, 1), b.Cause);
                    break;
                }
            }
        }
    }

    private static bool InWindow(double hour, int start, int len)
    {
        double h = ((hour - start) % 24 + 24) % 24;
        return h < len;
    }

    /// <summary>장치의 중요도: 숨 · 열 · 전기에 관한 것이 높다.</summary>
    private float Importance(int dev)
    {
        if (_net == null || dev < 0 || dev >= _net.Devices.Count) return 1;
        return _net.Devices[dev].Def.Role switch
        {
            "o2gen" or "scrubber" or "pump" or "reactor" or "panel" => 2.0f,
            "battery" or "aux" or "hvac" or "recycler" or "computer" => 1.5f,
            _ => 1.0f,
        };
    }

    // ───────────────────────── 계획 ─────────────────────────

    private static PlanStep Go(int cell, int near = 0) => new() { Kind = StepKind.GoTo, Cell = cell, Arg = (byte)near };
    private static PlanStep Use(UseKind u, double ticks, int cell = -1, int target = -1) => new() { Kind = StepKind.Use, Arg = (byte)u, Value = (float)ticks, Cell = cell, Target = target };

    private bool BuildPlan(Sim sim, Agent a, Option o, List<PlanStep> plan)
    {
        ref var c = ref a.C;
        int here = c.Cell;
        switch (o.Goal)
        {
            case GoalKind.Idle:
            {
                if (L.Kind[here] == CellKind.Door)
                {
                    // 문간에 서 있지 않는다
                    Span<int> nb = stackalloc int[4];
                    L.Neighbors(here, nb);
                    foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Floor) { plan.Add(Go(j)); break; }
                }
                plan.Add(Use(UseKind.None, 10));
                return true;
            }
            case GoalKind.Sleep:
            {
                int bed = c.Bed >= 0 && !Taken(a, c.Bed) ? c.Bed : NearestFree(a, _beds);
                if (bed >= 0) plan.Add(Go(bed));
                plan.Add(Use(UseKind.Sleep, Time.Hours(10)));
                return true;
            }
            case GoalKind.Eat:
            {
                int src = Nearest(here, _food, fu => FoodAt(fu) >= 1);
                if (src < 0) return false;
                int cell = L.Furniture[src].Cells[0];
                plan.Add(Go(cell, 1));
                plan.Add(Use(UseKind.Eat, Time.Minutes(10), cell, src));
                return true;
            }
            case GoalKind.Drink:
            {
                if (_net != null && _net.WaterTank < 30) return false;
                int src = Nearest(here, _water, _ => true);
                if (src < 0) return false;
                int cell = L.Furniture[src].Cells[0];
                plan.Add(Go(cell, 1));
                plan.Add(Use(UseKind.Drink, Time.Minutes(1), cell, src));
                return true;
            }
            case GoalKind.Work:
            {
                if (o.Target < 0) return false;
                int cell = L.Furniture[o.Target].Cells[0];
                plan.Add(Go(cell, 1));
                plan.Add(Use(UseKind.Work, Time.Hours(8), cell, o.Target));
                return true;
            }
            case GoalKind.Relax:
            {
                int seat = NearestFree(a, _relax);
                if (seat < 0) return false;
                plan.Add(Go(seat));
                plan.Add(Use(UseKind.Relax, Time.Minutes(45)));
                return true;
            }
            case GoalKind.Socialize:
            {
                int seat = BusiestSeat(a);
                if (seat < 0) return false;
                plan.Add(Go(seat));
                plan.Add(Use(UseKind.Talk, Time.Minutes(30)));
                return true;
            }
            case GoalKind.Charge:
            {
                if (c.Dock < 0) return false;
                plan.Add(Go(c.Dock));
                plan.Add(Use(UseKind.Charge, Time.Hours(3)));
                return true;
            }
            case GoalKind.FightFire:
            {
                if (c.Holding != iExt || c.HoldCharge < 20)
                {
                    if (c.Holding != 255 && c.Holding != iExt) return false;
                    int src = NearestStock(here, iExt);
                    if (src < 0) return false;
                    plan.Add(Go(L.Furniture[src].Cells[0], 1));
                    plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = iExt });
                }
                plan.Add(Go(o.Cell, 2));
                plan.Add(new PlanStep { Kind = StepKind.Spray, Cell = o.Cell });
                return true;
            }
            case GoalKind.Rescue:
            {
                if (o.Target < 0 || c.Carrying >= 0) return false;
                int vb = o.Target;
                if (vb >= _life!.Bodies.Count || _life.Bodies[vb].Alive == 0) return false;
                plan.Add(Go(_life.Bodies[vb].Cell, 1));
                plan.Add(new PlanStep { Kind = StepKind.PickUp, Target = vb });
                plan.Add(new PlanStep { Kind = StepKind.GoTo, Cell = -1, Arg = 0 }); // 업은 뒤에 안전한 곳을 고른다
                plan.Add(new PlanStep { Kind = StepKind.Drop });
                return true;
            }
            case GoalKind.Patch:
            {
                int wall = o.Target;
                int stand = StandBeside(wall);
                if (stand < 0) return false;
                if (a.IsPerson)
                {
                    if (c.Holding != iPatch)
                    {
                        if (c.Holding != 255) return false;
                        int src = NearestStock(here, iPatch);
                        if (src < 0) return false;
                        plan.Add(Go(L.Furniture[src].Cells[0], 1));
                        plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = iPatch });
                    }
                    if (_life![c.Body].Suit == 0 && (F.Pressure(stand) < 50 || Believes(a, BeliefKind.Vacuum, stand, 8) > 0.4f))
                    {
                        int locker = NearestStock(here, iSuit);
                        if (locker < 0) return false;
                        // 우주복은 꾸러미를 든 채로 입는다
                        plan.Insert(0, new PlanStep { Kind = StepKind.Wear, Target = locker, Arg = iSuit });
                        plan.Insert(0, Go(L.Furniture[locker].Cells[0], 1));
                    }
                }
                plan.Add(Go(stand));
                plan.Add(new PlanStep { Kind = StepKind.Seal, Cell = wall });
                return true;
            }
            case GoalKind.Repair:
            {
                int dev = o.Target;
                if (_net == null || dev < 0) return false;
                int part = -1;
                for (int p = 0; p < 4; p++) if (_net.Broken[dev * 4 + p] != 0) { part = p; break; }
                if (part < 0) return false;
                if (a.IsPerson && c.Holding != iRepair)
                {
                    if (c.Holding != 255) return false;
                    int src = NearestStock(here, iRepair);
                    if (src < 0) return false;
                    plan.Add(Go(L.Furniture[src].Cells[0], 1));
                    plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = iRepair });
                }
                plan.Add(Go(_net.Devices[dev].Cell, 1));
                plan.Add(new PlanStep { Kind = StepKind.Fix, Target = dev, Arg = (byte)part });
                return true;
            }
            case GoalKind.Evacuate:
            {
                int refuge = FindRefuge(a, here, o.Target, o.Cell);
                if (refuge < 0) return false;
                plan.Add(Go(refuge));
                return true;
            }
            case GoalKind.Investigate:
                plan.Add(Go(o.Cell, 2));
                plan.Add(new PlanStep { Kind = StepKind.Look, Cell = o.Cell });
                return true;
        }
        return false;
    }

    /// <summary>칸이 그 방 안이거나 그 방 둘레의 벽 · 문인가.</summary>
    private bool InRoom(int cell, int room)
    {
        if (cell < 0 || room < 0) return false;
        if (L.Room[cell] == room) return true;
        if (L.Kind[cell] == CellKind.Wall) return Array.BinarySearch(_roomWalls[room], cell) >= 0;
        if (L.Kind[cell] == CellKind.Door) return Array.BinarySearch(_roomDoors[room], cell) >= 0;
        return false;
    }

    /// <summary>피할 곳: 위험한 방(room) 밖 · 위험한 칸에서 4칸 넘게 · 몸이 느끼기에 안전한 가장 가까운 칸.</summary>
    private int FindRefuge(Agent a, int from, int room, int danger)
    {
        var q = new Queue<int>();
        var seen = new HashSet<int> { from };
        q.Enqueue(from);
        Span<int> nb = stackalloc int[4];
        while (q.Count > 0 && seen.Count < 3000)
        {
            int c = q.Dequeue();
            if (L.Kind[c] == CellKind.Floor && L.Room[c] != room && L.Room[c] >= 0 && (danger < 0 || Dist(c, danger) > 4) && SafeAt(c)
                && Believes(a, BeliefKind.Vacuum, c, 3) < 0.3f && Believes(a, BeliefKind.Fire, c, 4) < 0.3f && !BelievedRoomBad(a, L.Room[c]))
            {
                // 문에서 한 칸 더 들어간다 (문간에 서지 않게)
                return c;
            }
            L.Neighbors(c, nb);
            foreach (int j in nb)
            {
                if (j < 0 || !L.IsSpace(j) || seen.Contains(j)) continue;
                if (L.Kind[j] == CellKind.Door && !F.DoorPassable(L.DoorAt[j]) && (Believes(a, BeliefKind.StuckDoor, j, 0) > 0.3f || Believes(a, BeliefKind.Leak, j, 0) > 0.4f)) continue; // 쉬익대는 문은 열지 않는다
                seen.Add(j); q.Enqueue(j);
            }
        }
        return -1;
    }

    /// <summary>그 방이 위험하다고 믿나: 구멍 · 공기 빠짐 · 새는 곳(문틈 말고) · 이상한 공기 · 연기 · 불.</summary>
    private bool BelievedRoomBad(Agent a, int room)
    {
        foreach (var b in a.Beliefs)
        {
            if (b.Conf < 0.3f || !InRoom(b.Cell, room)) continue;
            if (b.Kind is BeliefKind.BadAir or BeliefKind.Smoke or BeliefKind.Breach or BeliefKind.Vacuum or BeliefKind.Fire) return true;
            if (b.Kind == BeliefKind.Leak && b.Conf >= 0.5f && L.Kind[b.Cell] != CellKind.Door) return true;
        }
        return false;
    }

    private bool Taken(Agent a, int cell)
    {
        foreach (var o in Agents)
            if (o != a && o.IsPerson && o.C.Goal == GoalKind.Sleep && o.Plan.Count > 0 && o.Plan[0].Kind == StepKind.GoTo && o.Plan[0].Cell == cell) return true;
        return false;
    }

    private int NearestFree(Agent a, int[] furn)
    {
        int best = -1, bd = int.MaxValue;
        foreach (int fu in furn)
        {
            int cell = L.Furniture[fu].Cells[0];
            if (Taken(a, cell)) continue;
            bool used = false;
            foreach (var o in Agents) if (o != a && o.C.Cell == cell && o.C.Goal is GoalKind.Relax or GoalKind.Socialize or GoalKind.Sleep) { used = true; break; }
            if (used) continue;
            int d = Manhattan(a.C.Cell, cell);
            if (d < bd) { bd = d; best = cell; }
        }
        return best;
    }

    /// <summary>사람이 가장 많이 모인 사교 방의 빈자리.</summary>
    private int BusiestSeat(Agent a)
    {
        if (_social.Length == 0) return -1;
        var count = new Dictionary<int, int>();
        foreach (var o in Agents) if (o != a && o.IsPerson && o.C.Up != 0 && o.C.Goal == GoalKind.Socialize) { int r = RoomOf(o.C.Cell); count[r] = count.GetValueOrDefault(r) + 1; }
        int best = -1; double bs = double.MinValue;
        foreach (int fu in _social)
        {
            int cell = L.Furniture[fu].Cells[0];
            bool used = false;
            foreach (var o in Agents) if (o != a && o.C.Cell == cell) { used = true; break; }
            if (used) continue;
            double s = count.GetValueOrDefault(L.Furniture[fu].Room) * 10 - Manhattan(a.C.Cell, cell) * 0.1;
            if (s > bs) { bs = s; best = cell; }
        }
        return best;
    }

    private int Nearest(int from, int[] furn, Func<int, bool> ok)
    {
        int best = -1, bd = int.MaxValue;
        foreach (int fu in furn)
        {
            if (!ok(fu)) continue;
            int d = Manhattan(from, L.Furniture[fu].Cells[0]);
            if (d < bd) { bd = d; best = fu; }
        }
        return best;
    }

    private int NearestStock(int from, byte item)
    {
        int best = -1, bd = int.MaxValue;
        for (int fu = 0; fu < L.Furniture.Count; fu++)
        {
            if (Stock[fu * ItemCount + item] <= 0 || L.Furniture[fu].Cells.Length == 0) continue;
            int d = Manhattan(from, L.Furniture[fu].Cells[0]);
            if (d < bd) { bd = d; best = fu; }
        }
        return best;
    }

    private double FoodAt(int fu)
    {
        double s = 0;
        foreach (int c in L.Furniture[fu].Cells) s += F.Stored(T.sFood, c);
        return s;
    }

    /// <summary>벽 칸 옆에 설 수 있는 바닥 칸 (안쪽).</summary>
    private int StandBeside(int wall)
    {
        Span<int> nb = stackalloc int[4];
        L.Neighbors(wall, nb);
        foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Floor) return j;
        foreach (int j in nb) if (j >= 0 && L.IsSpace(j)) return j;
        return -1;
    }
}
