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
    /// <summary>조사용: 이 에이전트의 고르기를 찍는다 (MIND_DEBUG_AGENT).</summary>
    public static readonly int DebugAgent = int.TryParse(Environment.GetEnvironmentVariable("MIND_DEBUG_AGENT"), out var v) ? v : -1;

    private void Decide(Sim sim, Agent a)
    {
        long t = sim.Tick;
        ref var c = ref a.C;
        _opts.Clear();
        a.Avoid.RemoveAll(v => v.Until <= t);
        _deciding = a; _nowTick = t;
        if (a.IsPerson) TakeOffSuit(sim, a);
        Options(sim, a);
        _deciding = null;
        if (_opts.Count == 0) return;
        _opts.Sort((x, y) => y.Score.CompareTo(x.Score) is var k && k != 0 ? k : x.Goal.CompareTo(y.Goal));

        if (DebugAgent == a.Id) Console.WriteLine($"    [고르기 {a.Id}] " + string.Join(" · ", _opts.Take(5).Select(o => $"{o.Goal}:{o.Target}={o.Score:0.00}")));
        bool busy = a.Plan.Count > 0 && c.StepIndex < a.Plan.Count;
        if (busy && c.Carrying >= 0) return; // 업은 사람을 내려놓기 전에는 다른 일을 하지 않는다
        if (busy && a.Plan[c.StepIndex].Kind is StepKind.Spray or StepKind.Seal or StepKind.Fix or StepKind.PickUp or StepKind.Drop or StepKind.Wear or StepKind.Take)
            return; // 손을 쓰는 단계는 마친다 (단계가 스스로 끝을 확인한다)
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

    /// <summary>
    /// 우주복을 벗는다: 숨쉴 만한 방(80kPa · 산소 18kPa · 독 없음)에 왔고 우주복이 필요한 일(막기 · 구하기 · 불 끄기 · 대피) 중이 아니면 —
    /// 우주복 산소가 바닥나면 그런 일 중이어도 벗는다 (헬멧 속에서 숨이 막힌다). 가장 가까운 우주복 보관함에 걸어 둔다.
    /// </summary>
    private void TakeOffSuit(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        ref var b = ref _life![c.Body];
        if (b.Suit == 0 || b.Alive == 0 || b.Conscious == 0) return;
        int here = c.Cell;
        bool air = F.Pressure(here) > 80 && F.Partial(T.gO2, here) > 18 && F.Ppm(T.gCO, here) < 50 && F.Ppm(T.gNH3, here) < 25 && F.Ppm(T.gSoot, here) < 30;
        if (!air) return;
        bool empty = b.SuitO2 < 0.1 * ItemCharge(iSuit);
        bool task = c.Goal is GoalKind.Patch or GoalKind.Rescue or GoalKind.FightFire or GoalKind.Evacuate or GoalKind.GetSuit && a.Plan.Count > 0 && c.StepIndex < a.Plan.Count;
        if (!empty && (task || BelievedRoomBad(a, RoomOf(here)))) return;
        b.Suit = 0; b.SuitO2 = 0;
        int locker = -1, bd = int.MaxValue;
        for (int fu = 0; fu < L.Furniture.Count; fu++)
        {
            if (L.Furniture[fu].Type != "SuitLocker" || L.Furniture[fu].Cells.Length == 0) continue;
            int d = Manhattan(here, L.Furniture[fu].Cells[0]);
            if (d < bd) { bd = d; locker = fu; }
        }
        if (locker >= 0) Stock[locker * ItemCount + iSuit]++;
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
    private long _nowTick;

    private void Add(GoalKind g, int target, int cell, float score, int cause = 0)
    {
        if (score <= 0.01f) return;
        if (_deciding != null)
        {
            foreach (var v in _deciding.Avoid) if (v.Goal == g && v.Target == target) return;
            ref var c = ref _deciding.C;
            if (c.AssignedGoal == g && c.AssignedUntil > _nowTick && (c.AssignedTarget < 0 || c.AssignedTarget == target
                || g is GoalKind.FightFire or GoalKind.Patch or GoalKind.Brace or GoalKind.Investigate && cell >= 0 && Dist(c.AssignedTarget, cell) <= 3))
                score += g is GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest && _deciding.IsPerson ? 1 : 3; // 주컴퓨터가 맡긴 일 (사람의 일상 일은 끼니 · 잠보다 앞서지 않게)
        }
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
            int bedRoom = RoomOf(c.Bed >= 0 ? c.Bed : here);
            if ((c.Sleepy > 0.6f || night && c.Sleepy > 0.4f) && !BelievedRoomBad(a, bedRoom)) // 위험하다고 믿는 방에서는 자지 않는다
                Add(GoalKind.Sleep, c.Bed, c.Bed >= 0 ? c.Bed : here, c.Sleepy * c.Sleepy * 5 + (night ? 1.5f : 0));
            // 욕구는 어느 만큼 차야 움직인다 (배가 조금 고프다고 바로 먹으러 가지 않는다)
            if (c.Hunger > 0.55f) Add(GoalKind.Eat, -1, -1, c.Hunger * c.Hunger * 5 + (F.Ppm(T.gAroma, here) > 5 ? 0.5f : 0));
            if (c.Thirst > 0.3f) Add(GoalKind.Drink, -1, -1, c.Thirst * c.Thirst * 6);
            float grief = _soc?.GriefOf(a.Id) ?? 0; // ⑨ 슬픔: 일손을 놓고 사람을 찾는다
            if (InWindow(hour, c.ShiftHour, 8) && c.Station >= 0) Add(GoalKind.Work, c.Station, L.Furniture[c.Station].Cells[0], (1.2f + c.Diligent) * (1 - 0.7f * grief));
            if (c.Bored > 0.3f) Add(GoalKind.Relax, -1, -1, c.Bored * 1.5f);
            if (c.Lonely > 0.3f || grief > 0.3f) Add(GoalKind.Socialize, -1, -1, Math.Max(c.Lonely, grief) * (0.5f + c.Sociable) * 1.5f + grief);
            Add(GoalKind.Idle, -1, here, 0.3f);
            JobOption(a);
        }
        else
        {
            float low = c.Battery < 0.1f ? 8 : c.Battery < 0.3f ? 4 : c.Battery < 0.95f && c.Goal == GoalKind.Charge ? 1.5f : 0.4f;
            Add(GoalKind.Charge, c.Dock, c.Dock, low);
            if (!drone) JobOption(a);
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
                    BeliefKind.Breach when L.Kind[b.Cell] == CellKind.Door => 0, // 새는 문틈: 위험은 문 너머다 (나가면 진공으로 간다)
                    BeliefKind.Breach or BeliefKind.Vacuum => 8 * b.Conf,
                    BeliefKind.Leak when b.Conf >= 0.5f && L.Kind[b.Cell] != CellKind.Door => 6 * b.Conf, // 쉬익대는 닫힌 문은 그 너머가 빠진 것
                    BeliefKind.Smoke when b.Conf >= 0.5f => 4 * b.Conf,
                    BeliefKind.Fire => 3 * b.Conf * (1.3f - c.Brave),
                    BeliefKind.BadAir => (b.Src == BeliefSource.Screen ? 7 : 3) * b.Conf, // 주컴퓨터 경보는 믿는다
                    _ => 0,
                };
                if (s > worst) { worst = s; cell = b.Cell; }
            }
            // 이 방의 위험을 다루러 들어온 사람(불 끄기 · 구하기 · 막기)은 나가지 않는다 — 정말 위험하면 본능이 끊는다
            bool committed = c.Goal is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch && a.Plan.Count > 0 && c.StepIndex < a.Plan.Count && InRoom(c.GoalCell, myRoom);
            // 지나가는 중: 지금 걸음이 위험하지 않다고 믿는 다른 방으로 가고 있으면 새로 대피하지 않는다 (통로를 지날 때마다 문을 여닫지 않게)
            bool passing = a.Plan.Count > 0 && c.StepIndex < a.Plan.Count && a.Plan[c.StepIndex].Kind == StepKind.GoTo && a.Plan[c.StepIndex].Cell >= 0
                && RoomOf(a.Plan[c.StepIndex].Cell) != myRoom && RoomBadness(a, RoomOf(a.Plan[c.StepIndex].Cell)) < 0.3f;
            if (worst > 0 && !committed && !passing) Add(GoalKind.Evacuate, myRoom, cell, worst);
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
                    float bond = person && _soc != null ? Math.Max(0, _soc.BondOf(a.Id, AgentOfBody(b.Subject))) : 0; // ⑨ 친구는 더 무릅쓰고 구한다
                    float s = person ? b.Conf * (1 + c.Kindness + c.SkMedic + 2 * bond) * 2.2f * (caution + bond * (1 - caution)) : b.Conf * 5.5f;
                    Add(GoalKind.Rescue, b.Subject, b.Cell, s * df * ClaimFactor(a, GoalKind.Rescue, b.Subject, 1), b.Cause);
                    break;
                }
                case BeliefKind.Breach when !drone && (F.Hole[b.Cell] > 0 || DoorGapLeaks(b.Cell)) || b.Kind == BeliefKind.Breach && !drone && b.Conf > 0.6f:
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
                case BeliefKind.Strain when !drone && L.Kind[b.Cell] is CellKind.Wall or CellKind.Door && _st != null && (_st.Braced[b.Cell] == 0 || _st.JammedAt(b.Cell)):
                {
                    // 본 · 들은 끼익거리는 문 · 벽: 덧대 받친다 (사람은 수리 꾸러미)
                    float s = person ? b.Conf * (0.3f + c.SkRepair) * 3 * caution : b.Conf * 5;
                    Add(GoalKind.Brace, b.Cell, b.Cell, s * df * ClaimFactor(a, GoalKind.Brace, b.Cell, 1), b.Cause);
                    break;
                }
                case BeliefKind.Bang or BeliefKind.Strain or BeliefKind.Leak or BeliefKind.SmokeSmell or BeliefKind.Help or BeliefKind.Spark:
                {
                    if (b.Src == BeliefSource.Saw && b.Kind != BeliefKind.Spark) break; // 이미 본 자리
                    if (person && b.Src == BeliefSource.Screen) break;                 // 주컴퓨터가 알린 것은 로봇이 살핀다
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
                if (Eva(a) && L.Hull[wall])
                {
                    // 로봇: 구멍 곁 아무 칸이나 — 안쪽에 못 들어가면(진공 방 문이 안 열린다) 에어락으로 나가 외판 바깥에서 막는다
                    plan.Add(Go(wall, 1));
                    plan.Add(new PlanStep { Kind = StepKind.Seal, Cell = wall });
                    return true;
                }
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
                if (part < 0 && _net.Devices[dev].Def.Role == "panel" && _net.StuckFault(dev) >= 0) part = PullFeederArg; // 눌어붙은 차단기: 손으로 끊는다
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
            case GoalKind.Brace:
            {
                int wall = o.Target;
                int stand = StandBeside(wall);
                if (stand < 0 || _st == null || _st.Braced[wall] != 0 && !_st.JammedAt(wall)) return false;
                if (a.IsPerson && c.Holding != iRepair)
                {
                    if (c.Holding != 255) return false;
                    int src = NearestStock(here, iRepair);
                    if (src < 0) return false;
                    plan.Add(Go(L.Furniture[src].Cells[0], 1));
                    plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = iRepair });
                }
                plan.Add(Go(stand));
                plan.Add(new PlanStep { Kind = StepKind.Brace, Cell = wall });
                return true;
            }
            case GoalKind.Craft:
            {
                int bench = o.Target;
                plan.Add(Go(L.Furniture[bench].Cells[0], 1));
                plan.Add(new PlanStep { Kind = StepKind.Craft, Target = bench, Arg = (byte)c.AssignedArg });
                return true;
            }
            case GoalKind.Maintain:
            {
                if (_net == null) return false;
                int dev = o.Target / 4, part = o.Target % 4;
                if (a.IsPerson && c.Holding != iRepair)
                {
                    if (c.Holding != 255) return false;
                    int src = NearestStock(here, iRepair);
                    if (src < 0) return false;
                    plan.Add(Go(L.Furniture[src].Cells[0], 1));
                    plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = iRepair });
                }
                plan.Add(Go(_net.Devices[dev].Cell, 1));
                plan.Add(new PlanStep { Kind = StepKind.Maintain, Target = dev, Arg = (byte)part });
                return true;
            }
            case GoalKind.Clean:
            {
                plan.Add(Go(o.Target, 1));
                plan.Add(new PlanStep { Kind = StepKind.Clean, Cell = o.Target });
                return true;
            }
            case GoalKind.Harvest:
            {
                // 다 자란 작물 → 손에 들고 → 가까운 식량 창고 (냉장고가 먼저 — 썩지 않게)
                if (_life == null || o.Target >= _life.Crops.Count || c.Holding != 255 && c.Holding != iHarvest) return false;
                int crop = _life.Crops[o.Target].Cell;
                int store = Nearest(crop, _food, fu => L.Furniture[fu].Type == "Fridge");
                if (store < 0) store = Nearest(crop, _food, _ => true);
                if (store < 0) return false;
                if (c.Holding != iHarvest)
                {
                    plan.Add(Go(crop, 1));
                    plan.Add(new PlanStep { Kind = StepKind.Harvest, Target = o.Target });
                }
                plan.Add(Go(L.Furniture[store].Cells[0], 1));
                plan.Add(new PlanStep { Kind = StepKind.Stow, Target = store });
                return true;
            }
            case GoalKind.Evacuate:
            {
                int refuge = FindRefuge(a, here, o.Target, o.Cell);
                if (refuge < 0) return false;
                plan.Add(Go(refuge));
                a.Avoid.Add(new AvoidEntry { Goal = GoalKind.Flee, Target = o.Target, Until = sim.Tick + Time.Minutes(10) });
                if (a.Avoid.Count > 16) a.Avoid.RemoveAt(0);
                return true;
            }
            case GoalKind.Investigate:
                if (L.Kind[o.Cell] == CellKind.Outside)
                {
                    // 바깥에서 살핀다: 그 방 외판을 따라 걸으며 구멍을 찾는다 (주컴퓨터가 맡긴 둘러보기)
                    if (!Eva(a)) return false;
                    foreach (int wp in SurveyRoute(o.Cell)) plan.Add(Go(wp, 1));
                    plan.Add(new PlanStep { Kind = StepKind.Look, Cell = o.Cell });
                    return true;
                }
                plan.Add(Go(o.Cell, 2));
                plan.Add(new PlanStep { Kind = StepKind.Look, Cell = o.Cell });
                return true;
        }
        return false;
    }

    /// <summary>외판 둘러보기 길: 시작 칸(외판 바깥)에 맞닿은 방의 외판 바깥 칸들을 가까운 차례로 잇고 3칸마다 들른다.</summary>
    private List<int> SurveyRoute(int start)
    {
        var route = new List<int>();
        int room = OutsideRoom(start);
        if (room < 0) { route.Add(start); return route; }
        var cells = new List<int>();
        Span<int> nb = stackalloc int[4];
        foreach (int w in _roomWalls[room])
        {
            if (!L.Hull[w]) continue;
            L.Neighbors(w, nb);
            foreach (int j in nb) if (j >= 0 && _hullOut[j] && !cells.Contains(j)) cells.Add(j);
        }
        cells.Sort();
        int cur = start, n = 0;
        route.Add(start);
        cells.Remove(start);
        while (cells.Count > 0)
        {
            int best = cells[0];
            foreach (int c in cells) if (Manhattan(cur, c) < Manhattan(cur, best)) best = c;
            cells.Remove(best); cur = best;
            if (++n % 3 == 0 || cells.Count == 0) route.Add(best);
        }
        return route;
    }

    /// <summary>외판 바깥 칸에 맞닿은 방 (외판 벽 너머).</summary>
    public int OutsideRoom(int cell)
    {
        Span<int> nb = stackalloc int[4], nb2 = stackalloc int[4];
        L.Neighbors(cell, nb);
        foreach (int w in nb)
        {
            if (w < 0 || L.Kind[w] != CellKind.Wall) continue;
            L.Neighbors(w, nb2);
            foreach (int j in nb2) if (j >= 0 && L.Room[j] >= 0) return L.Room[j];
        }
        return -1;
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

    /// <summary>피할 곳: 위험한 방(room) 밖 · 위험한 칸에서 4칸 넘게 · 몸이 느끼기에 안전한 칸 중
    /// 위험하다고 믿는 정도가 가장 낮은 방의 가장 가까운 칸 — 그런 방이 없으면 지금 방보다 확실히 나은 곳만 (없으면 -1: 옮기지 않는다).</summary>
    private int FindRefuge(Agent a, int from, int room, int danger)
    {
        var q = new Queue<int>();
        var seen = new HashSet<int> { from };
        q.Enqueue(from);
        Span<int> nb = stackalloc int[4];
        float here = RoomBadness(a, room);
        int best = -1; float bestBad = float.MaxValue;
        while (q.Count > 0 && seen.Count < 3000)
        {
            int c = q.Dequeue();
            if (L.Kind[c] == CellKind.Floor && L.Room[c] != room && L.Room[c] >= 0 && (danger < 0 || Dist(c, danger) > 4) && SafeAt(c) && !FledFrom(a, L.Room[c])
                && Believes(a, BeliefKind.Vacuum, c, 3) < 0.3f && Believes(a, BeliefKind.Fire, c, 4) < 0.3f)
            {
                float bad = RoomBadness(a, L.Room[c]);
                if (bad <= 0) return c;                       // 위험하다고 믿지 않는 가장 가까운 방
                if (bad < bestBad) { bestBad = bad; best = c; }
            }
            L.Neighbors(c, nb);
            foreach (int j in nb)
            {
                if (j < 0 || !L.IsSpace(j) || seen.Contains(j)) continue;
                if (L.Kind[j] == CellKind.Door && !F.DoorPassable(L.DoorAt[j]) && (Believes(a, BeliefKind.StuckDoor, j, 0) > 0.3f || Believes(a, BeliefKind.Leak, j, 0) > 0.4f)) continue; // 쉬익대는 문은 열지 않는다
                seen.Add(j); q.Enqueue(j);
            }
        }
        return best >= 0 && bestBad < here - 0.15f ? best : -1;
    }

    /// <summary>그 방이 위험하다고 믿는 정도 (0 = 믿지 않음 · 가장 확신하는 위험 믿음).</summary>
    private float RoomBadness(Agent a, int room)
    {
        float worst = 0;
        foreach (var b in a.Beliefs)
        {
            if (b.Conf < 0.3f || !InRoom(b.Cell, room)) continue;
            bool bad = b.Kind is BeliefKind.Smoke or BeliefKind.Vacuum or BeliefKind.Fire or BeliefKind.BadAir
                       || b.Kind is BeliefKind.Leak or BeliefKind.Breach && L.Kind[b.Cell] != CellKind.Door; // 새는 문틈은 문 너머가 나쁜 것
            if (bad && b.Conf > worst) worst = b.Conf;
        }
        return worst;
    }

    /// <summary>최근(10분) 빠져나온 방인가 — 그리로 다시 피하지 않는다 (두 방을 오가며 문을 여닫지 않게).</summary>
    private bool FledFrom(Agent a, int room)
    {
        foreach (var v in a.Avoid) if (v.Goal == GoalKind.Flee && v.Target == room && v.Until > _nowTick) return true; // '빠져나온 방' 기억은 Flee로 적는다 (점수 고르기에는 Flee가 없다)
        return false;
    }

    /// <summary>그 방이 위험하다고 믿나: 구멍 · 공기 빠짐 · 새는 곳(문틈 말고) · 이상한 공기 · 연기 · 불.</summary>
    private bool BelievedRoomBad(Agent a, int room)
    {
        foreach (var b in a.Beliefs)
        {
            if (b.Conf < 0.3f || !InRoom(b.Cell, room)) continue;
            if (b.Kind is BeliefKind.Smoke or BeliefKind.Vacuum or BeliefKind.Fire) return true;
            if (b.Kind == BeliefKind.Breach && L.Kind[b.Cell] != CellKind.Door) return true;
            if (b.Kind == BeliefKind.BadAir && b.Conf >= 0.5f) return true;
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

    /// <summary>⑪ 일감판이 맡긴 일 (만들기 · 정비 · 청소) — 맡긴 보너스(+3)가 붙는다. 주컴퓨터가 로봇에게 맡긴 살피기(빠진 방 · 외판 둘러보기)도.</summary>
    private void JobOption(Agent a)
    {
        ref var c = ref a.C;
        if (!a.IsPerson && c.AssignedGoal == GoalKind.Investigate && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0) Add(GoalKind.Investigate, c.AssignedTarget, c.AssignedTarget, 1.2f);
        // 맡긴 끼인 문: 믿음이 바래도 간다
        if (!a.IsPerson && c.AssignedGoal == GoalKind.Brace && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0 && _st != null && _st.JammedAt(c.AssignedTarget)) Add(GoalKind.Brace, c.AssignedTarget, c.AssignedTarget, 1.2f);
        // 거둔 것을 들고 있으면 (하던 일이 끊겼어도) 식량 창고에 넣는다
        if (c.Holding == iHarvest && _life != null && _life.Crops.Count > 0) Add(GoalKind.Harvest, 0, c.Cell, 1.0f);
        if (c.AssignedUntil <= _nowTick || c.AssignedGoal is not (GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest)) return;
        int cell = c.AssignedGoal switch
        {
            GoalKind.Craft => L.Furniture[c.AssignedTarget].Cells[0],
            GoalKind.Maintain => _net != null ? _net.Devices[c.AssignedTarget / 4].Cell : -1,
            GoalKind.Harvest => _life != null && c.AssignedTarget < _life.Crops.Count ? _life.Crops[c.AssignedTarget].Cell : -1,
            _ => c.AssignedTarget,
        };
        if (cell >= 0) Add(c.AssignedGoal, c.AssignedTarget, cell, 1.2f);
    }

    /// <summary>그 방에 있는 이들에 대한 정의 합 (⑨ — 친구가 있는 곳으로 간다).</summary>
    private float FriendsIn(Agent a, int room)
    {
        if (_soc == null) return 0;
        float s = 0;
        foreach (var o in Agents) if (o != a && o.IsPerson && o.C.Up != 0 && RoomOf(o.C.Cell) == room) s += Math.Max(0, _soc.BondOf(a.Id, o.Id));
        return s;
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
            double s = count.GetValueOrDefault(L.Furniture[fu].Room) * 10 + FriendsIn(a, L.Furniture[fu].Room) * 15 - Manhattan(a.C.Cell, cell) * 0.1;
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
    /// <summary>닫힌 문의 틈으로 공기가 새고 있나 (막을 수 있는 문틈).</summary>
    private bool DoorGapLeaks(int cell) => L.Kind[cell] == CellKind.Door && L.DoorAt[cell] >= 0 && F.DoorGapOpen(L.DoorAt[cell]) && DoorDeltaKPa(cell) > 5;

    /// <summary>구멍 · 문틈 곁 설 자리 — 공기가 있는 쪽(기압이 높은 바닥)을 고른다.</summary>
    private int StandBeside(int wall)
    {
        Span<int> nb = stackalloc int[4];
        L.Neighbors(wall, nb);
        int best = -1; double bp = -1;
        foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Floor && F.Pressure(j) > bp) { bp = F.Pressure(j); best = j; }
        if (best >= 0) return best;
        foreach (int j in nb) if (j >= 0 && L.IsSpace(j)) return j;
        return -1;
    }
}
