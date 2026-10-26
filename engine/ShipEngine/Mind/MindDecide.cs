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
        _opts.Clear(); _followScore = 0;
        a.Avoid.RemoveAll(v => v.Until <= t);
        _deciding = a; _nowTick = t;
        if (c.Holding != c.HeldItem) { c.HeldItem = c.Holding; c.HeldSince = t; }   // 손에 든 것이 바뀌었다 (든 때를 적는다)
        if (a.IsPerson) TakeOffSuit(sim, a);
        if (a.IsPerson && c.Goal == GoalKind.Improvise && (a.Plan.Count == 0 || c.StepIndex >= a.Plan.Count)) OwnFinished(sim, a);   // 제 궁리를 다 했다
        Options(sim, a);
        _deciding = null;
        if (!a.IsPerson && !LegsOk(a)) _opts.Clear();                                  // ⑥ 다리가 망가졌다 — 그 자리에 있는다 (판단 이관 (사))
        else if (!a.IsPerson && !HandsOk(a)) _opts.RemoveAll(o => HandGoal(o.Goal));   // ⑥ 팔을 못 쓴다 — 손일은 고르지 않는다
        // ⑩ 판단 엔진: 계획에 묶인 로봇은 맡긴 걸음만 (그 자리의 구멍을 막는 것은 같은 일 · 배터리가 바닥나면 충전) — 제 판단으로 다른 구멍에 가지 않는다
        if (!a.IsPerson && c.AssignedArg == PlanBound && c.AssignedUntil > t)
        {
            int at = c.AssignedTarget; GoalKind ag = c.AssignedGoal; float battery = c.Battery;
            bool tape = at >= 0 && L.Kind[at] == CellKind.Door;   // 문틈 막기는 그 문만
            _opts.RemoveAll(o => !(o.Goal == ag && (o.Target == at || !tape && o.Cell >= 0 && at >= 0 && Dist(o.Cell, at) <= 3)
                || !tape && ag is GoalKind.Investigate or GoalKind.Patch && o.Goal == GoalKind.Patch && o.Cell >= 0 && at >= 0 && Dist(o.Cell, at) <= 10
                || o.Goal == GoalKind.Charge && battery < 0.15f));
        }
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
        if (busy && current < 0 && c.Goal is GoalKind.Sleep or GoalKind.Eat or GoalKind.Drink or GoalKind.Toilet or GoalKind.Wash && c.StepIndex > 0) current = 1.5f; // 하던 식사 · 잠 · 볼일 · 샤워는 웬만하면 마친다
        // 시작한 쉼 · 어울림도 마친다 (하는 동안 지루함 · 외로움이 풀려 선택지가 사라져도 — 일 · 끼니처럼 더 급한 것은 끼어든다)
        if (busy && current < 0 && c.Goal is GoalKind.Relax or GoalKind.Socialize && c.StepIndex > 0) current = 1.0f;
        // 주운 것 · 꺼낸 쓴 것을 제자리에 넣는 일은 마친다 (주운 뒤엔 바닥에 없어 그 선택지가 사라진다 — 불 · 위험처럼 급한 것은 끼어든다)
        if (busy && current < 0 && c.Goal is GoalKind.Gather or GoalKind.Stash or GoalKind.Refill && c.StepIndex > 0) current = 2.0f;

        for (int k = 0; k < Math.Min(4, _opts.Count); k++)
        {
            var o = _opts[k];
            if (busy && o.Goal == c.Goal && o.Target == c.GoalTarget) return;  // 지금 것이 가장 낫다
            if (busy && o.Score <= current) return;
            if (TryPlan(sim, a, o)) return;
            if (k == 0 && _buildFailed) NoteCantPlan(a, o.Goal, o.Target, o.Cell, sim.Tick);   // 판단 일지 (쓰기만 — 1순위의 계획을 짓지 못했을 때만)
        }
    }

    /// <summary>
    /// 우주복을 벗는다: 숨쉴 만한 방(80kPa · 산소 18kPa · 독 없음)에 왔고 우주복이 필요한 일(막기 · 구하기 · 불 끄기 · 대피) 중이 아니면 —
    /// 우주복 산소가 바닥나면 그런 일 중이어도 벗는다 (헬멧 속에서 숨이 막힌다). 벗은 것은 손에 들고 가장 가까운 보관함에 걸어가 넣는다 (돌려놓기).
    /// </summary>
    private void TakeOffSuit(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        ref var b = ref _life![c.Body];
        if (b.Suit == 0 && b.Mask == 0 || b.Alive == 0 || b.Conscious == 0) return;
        int here = c.Cell;
        bool air = F.Pressure(here) > 80 && F.Partial(T.gO2, here) > 18 && F.Ppm(T.gCO, here) < 50 && F.Ppm(T.gNH3, here) < 25 && F.Ppm(T.gSoot, here) < 30;
        if (!air) return;
        if (b.Mask != 0)
        {
            // 좋은 공기 — 마스크를 벗어 가장 가까운 마스크함에 돌려놓는다 (병이 반 넘게 남았으면)
            if (BelievedRoomBad(a, RoomOf(here))) return;
            float left = b.MaskO2;
            b.Mask = 0; b.MaskO2 = 0;
            HoldTakenOff(a, iMask, left);   // 손에 들고 마스크함에 걸어가 넣는다 (남은 산소 그대로 — 주컴퓨터가 채우기를 맡긴다)
            if (b.Suit == 0) return;
        }
        bool empty = b.SuitO2 < 0.1 * ItemCharge(iSuit);
        bool task = c.Goal is GoalKind.Patch or GoalKind.Rescue or GoalKind.FightFire or GoalKind.Evacuate or GoalKind.GetSuit && a.Plan.Count > 0 && c.StepIndex < a.Plan.Count;
        if (!empty && (task || BelievedRoomBad(a, RoomOf(here)))) return;
        float o2 = b.SuitO2;
        b.Suit = 0; b.SuitO2 = 0;
        HoldTakenOff(a, iSuit, o2);   // 손에 들고 우주복 보관함에 걸어가 넣는다 (예전엔 벗는 순간 보관함으로 옮아 갔다)
    }

    private bool _buildFailed;   // 판단 일지: 방금 TryPlan이 계획을 짓지 못해 졌나 (감점 · 피한 방 때문에 다른 것을 고른 것과 가른다)

    private bool TryPlan(Sim sim, Agent a, Option o)
    {
        var plan = new List<PlanStep>();
        _buildFailed = false;
        if (!BuildPlan(sim, a, o, plan)) { _buildFailed = true; return false; }
        if (o.Cell < 0 && Everyday(o.Goal) && a.IsPerson && LeavesHold(a, plan) is float k && k < 1)
        {
            // 자리를 정하지 않은 일상(물 · 쉼 · 어울림)은 고를 때 대피실 · 회의 밖인지 몰랐다 — 계획이 닿는 방으로 감점을 매긴다 (비판 검토: 칸 −1 선택지가 감점을 피해 갔다 · 대피실 문을 나갔다 되돌아왔다)
            float eff = o.Score * k;
            foreach (var other in _opts) if (!(other.Goal == o.Goal && other.Target == o.Target) && other.Score > eff) return false;
        }
        if (HoldBlocks(a, o.Goal))
        {
            // 손 비우기 (docs/MIND.md 3단): 일상의 일 앞에서 든 것을 먼저 — 꼼꼼한 이는 제자리에 · 덤벙대는 이(성실 0.35 아래)는 그 자리에 내려놓는다
            var pre = new List<PlanStep>();
            int h = a.C.Cell;
            FreeHands(a, ref h, pre, a.IsPerson && a.C.Consc < 0.35f);
            plan.InsertRange(0, pre);
        }
        // 일상의 걸음이 방금 빠져나온 위험한 방으로 들어가면 하지 않는다 (침대 · 밥 · 일터를 그 안에서 고른 계획)
        if (Everyday(o.Goal)) foreach (var st in plan) if (st.Kind == StepKind.GoTo && Shunned(a, st.Cell)) return false;
        ref var c = ref a.C;
        if (IsSleeping(a)) _life![c.Body].Asleep = 0;
        if (c.Carrying >= 0) DropCarried(sim, a, false);
        if (IsTask(c.Goal)) Claim(a, c.Goal, c.GoalTarget, -1);
        if (a.Plan.Count > 0 && c.StepIndex < a.Plan.Count && Journaled(a, c.Goal) && (c.Goal != o.Goal || c.GoalTarget != o.Target))
            Jot(a, new Judgment { Tick = sim.Tick, Goal = c.Goal, Target = c.GoalTarget, Cell = c.Cell, Method = (byte)o.Goal, Held = c.Holding, Out = JudgeOut.Dropped, By = -1 });   // 판단 일지: 하던 일을 그만두고 다른 일로 (방법 칸에 바꾼 목표)
        c.Goal = o.Goal; c.GoalTarget = o.Target; c.GoalCell = o.Cell; c.GoalCause = o.Cause; c.GoalScore = o.Score; c.GoalSince = sim.Tick;
        c.Failed = 0;
        a.Plan = plan; c.StepIndex = 0; c.StepStart = sim.Tick; a.Path.Clear(); a.PathIdx = 0; c.WaitTicks = 0;
        c.CloseHard = (byte)(o.Goal == GoalKind.Evacuate ? 1 : 0);
        if (o.Goal == GoalKind.Evacuate && _followScore > 0 && o.Score == _followScore) { Followed++; c.FollowedAt = sim.Tick; } // ⑨ 아는 이를 따라 나갔다
        if (IsTask(o.Goal)) Claim(a, o.Goal, o.Target, +1);
        if (Journaled(a, o.Goal)) Jot(a, new Judgment { Tick = sim.Tick, Goal = o.Goal, Target = o.Target, Cell = o.Cell, Method = FirstHand(plan), Held = c.Holding, Out = JudgeOut.Set, By = c.AssignedGoal == o.Goal && c.AssignedUntil > sim.Tick ? -2 : -1 });   // 판단 일지 (쓰기만)
        return true;
    }

    /// <summary>판단 일지에 적을 만한 목표인가 — 빈둥 · 넉넉한 전지로 독에서 쉬기는 빼낸다 (고리 여덟 칸을 되풀이로 채우지 않게).</summary>
    private static bool Journaled(Agent a, GoalKind g) => g != GoalKind.Idle && !(g == GoalKind.Charge && a.C.Battery >= 0.6f);

    /// <summary>판단 일지의 '방법': 계획의 첫 손 걸음 (가기 말고 — 없으면 255).</summary>
    private static byte FirstHand(List<PlanStep> plan)
    {
        foreach (var st in plan) if (st.Kind is not (StepKind.GoTo or StepKind.None)) return (byte)st.Kind;
        return 255;
    }

    /// <summary>판단 일지: 1순위를 세우지 못했다 — 같은 (목표 · 대상)이 일지 안에 있고 10분 안이면 때만 고친다 (2틱마다 되풀이해 칸을 채우지 않게).</summary>
    private void NoteCantPlan(Agent a, GoalKind g, int target, int cell, long t)
    {
        if (!Journal) return;
        for (int k = 0; k < (a.Judged?.Count ?? 0); k++)
        {
            ref var j = ref a.Judged!.Newest(k);
            if (j.Out == JudgeOut.CantPlan && j.Goal == g && j.Target == target && t - j.Tick < Time.Minutes(10)) { j.Tick = t; return; }
        }
        Jot(a, new Judgment { Tick = t, Goal = g, Target = target, Cell = cell, Method = 255, Held = a.C.Holding, Out = JudgeOut.CantPlan, By = -1 });
    }

    /// <summary>계획이 끝나는 방이 머물러야 할 방(대피실 · 회의) 밖이면 그 감점 (대피 0.15 · 회의 0.3 — 1이면 감점 없음).</summary>
    private float? LeavesHold(Agent a, List<PlanStep> plan)
    {
        int end = -1;
        for (int i = plan.Count - 1; i >= 0; i--) if (plan[i].Kind == StepKind.GoTo && plan[i].Cell >= 0) { end = plan[i].Cell; break; }
        if (end < 0) return 1;
        int room = RoomOf(end);
        float k = 1;
        if (Sheltering(a) && room != ShelterRoom) k *= 0.15f;
        if (Gathering(a) && room != GatherRoom) k *= 0.3f;
        return k;
    }

    // ───────────────────────── 점수 ─────────────────────────

    private float DistFactor(int from, int to) => 1f / (1f + Manhattan(from, to) / 40f);

    /// <summary>일감 표시에 따른 배율: 필요한 수가 찼으면 0.15 · 사람이 하려는데 로봇이 맡았으면 0.1 (로봇이 먼저).</summary>
    private float ClaimFactor(Agent a, GoalKind g, int target, int need)
    {
        if (!_claims.TryGetValue(ClaimKey(g, target), out var v)) return 1;
        int n = v.n - (a.C.Goal == g && a.C.GoalTarget == target ? 1 : 0);
        if (a.IsPerson && v.robot && g != GoalKind.Cpr && !(a.C.Goal == g && a.C.GoalTarget == target)) return 0.1f;   // 멎은 심장은 로봇이 제세동기를 가지러 간 사이 사람이 누른다
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
            if (Everyday(g) && Shunned(_deciding, cell)) return;
            ref var c = ref _deciding.C;
            if (c.AssignedGoal == g && c.AssignedUntil > _nowTick && (c.AssignedTarget < 0 || c.AssignedTarget == target
                || g is GoalKind.FightFire or GoalKind.Patch or GoalKind.Brace or GoalKind.Investigate && cell >= 0 && Dist(c.AssignedTarget, cell) <= 3))
                score += g is GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock or GoalKind.Haul or GoalKind.Tighten or GoalKind.ToiletCare or GoalKind.Refill or GoalKind.Gather or GoalKind.LayOut && _deciding.IsPerson ? (c.Goal == GoalKind.Work ? 2 : 1) : 3; // 주컴퓨터가 맡긴 일 (사람의 일상 일은 끼니 · 잠보다 앞서지 않게 · 근무 중에 받은 일은 일의 한 몫 — 하던 일 지킴을 넘는다)
        }
        if (_deciding != null && _deciding.IsPerson) score *= AimBoost(_deciding.C, g) * LessonBoost(_deciding.C, g);   // ⑧ 인생 목표 · 사고의 교훈
        if (_deciding != null && Everyday(g) && Sheltering(_deciding) && (cell >= 0 ? RoomOf(cell) != ShelterRoom : g == GoalKind.Eat)) score *= 0.15f; // 대피 중: 대피실 밖의 일상은 미룬다
        if (_deciding != null) score *= CouncilBoost(_deciding, g, cell);   // ⑨ 회의에 모이기 · 벌 당번 · 근신
        if (_deciding != null && _deciding.IsPerson) score *= CrisisDamp(_deciding, g) * (g == GoalKind.Socialize ? 1 + 0.5f * _deciding.C.MoodGood : 1);   // DF7 겪은 이의 쉼 · 들뜬 이는 어울리러 더 간다
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
            // 졸림이 0.6을 넘거나, 잠잘 시간대에 0.4를 넘으면 잔다 (덜 졸린데 누우면 짧게 깬다) — 잘 시간대는 몸의 시계만큼 옮겨 간다 (아침형 · 저녁형)
            bool night = InWindow(hour - c.Chrono, c.ShiftHour + 16, 8);
            int bedRoom = RoomOf(c.Bed >= 0 ? c.Bed : here);
            int bedCell = c.Bed >= 0 ? c.Bed : here;
            if (Sheltering(a) && RoomOf(bedCell) != ShelterRoom) { bedCell = RoomOf(here) == ShelterRoom ? here : ShelterCell; bedRoom = ShelterRoom; } // 대피 중에는 대피실 바닥에서 잔다
            if ((c.Sleepy > 0.6f || night && c.Sleepy > 0.4f) && !BelievedRoomBad(a, bedRoom)) // 위험하다고 믿는 방에서는 자지 않는다
            {
                int regenBed = _life![c.Body].Regen > 0 ? (IsMedBedCell(here) ? here : NearestFree(a, _medBeds)) : -1;   // ⑦ (라-2) 재생 젤이 든 동안은 치료 침대에서 잔다
                if (regenBed >= 0) Add(GoalKind.Sleep, regenBed, regenBed, c.Sleepy * c.Sleepy * 5 + (night ? 1.5f : 0) + 0.2f);
                else Add(GoalKind.Sleep, c.Bed, bedCell, c.Sleepy * c.Sleepy * 5 + (night ? 1.5f : 0)); // 몸의 시계는 '언제 눕나'만 옮긴다 (얼마나 간절한지는 쌓인 졸림)
            }
            // 욕구는 어느 만큼 차야 움직인다 (배가 조금 고프다고 바로 먹으러 가지 않는다)
            int kinMeal = c.Hunger > 0.35f && c.Hunger <= 0.55f ? KinMeal(a) : -1; // ⑨ 가까운 이가 먹으러 가면 조금 배고파도 따라간다
            float sick = _life != null && c.Body >= 0 ? Life.Queasy(_life[c.Body]) : 0;   // 우주 멀미 · 배탈 (5단 C22): 속이 울렁거려 덜 먹는다
            bool feast = c.Hunger > 0.3f && sick < 0.3f && MealTime(hour);   // ⑨ 함께 먹는 때가 배의 관습이 됐다
            if ((c.Hunger > 0.55f + 0.3f * sick + 0.25f * c.Depress || kinMeal >= 0 && sick < 0.3f && c.Depress < 0.3f || feast) && FoodLeft(a.Id) >= 0.2) // ⑪ 배급: 오늘 몫을 다 받았으면 배식기가 주지 않는다 (참는다)
            {
                float eat = c.Hunger * c.Hunger * 5 + (F.Ppm(T.gAroma, here) > 5 ? 0.5f : 0) + (kinMeal >= 0 ? 0.6f : 0) + (feast ? 0.8f : 0);
                Add(GoalKind.Eat, -1, -1, eat);
                // ⑧ 밥을 짓는다: 요리를 좋아하는 이 · 꺼진 조리대가 있고 같은 방에 먹을 것이 있으면 (먹기보다 조금 앞서게)
                if (WantsToCook(sim, a) && CookStove(a) is int sv && sv >= 0) Add(GoalKind.Cook, sv, _net!.Devices[sv].Cell, eat + 0.3f);
            }
            SickOptions(a, here);    // ⑦ 앓는 이는 눕는다 (덜 성실할수록)
            ChartOptions(a, here);   // ⑦ (다) 진료 기록대로 — 치료 침대에서 산소 · 제 침대에서 쉼
            AwaitCare(a, here);      // ⑦ 돌보러 오는 이가 있으면 기다린다
            PilferOption(a, here);   // ⑪ 몫을 다 먹고 몹시 배고프면 — 덜 성실하고 덜 다정한 이는 몰래 한 끼
            ShelterOption(a, here);  // ⑩ 대피 방송을 따르는 이는 대피실로
            KeepOutOption(a, here);  // ⑩ 출입 금지 방송을 따르는 이는 그 방을 나온다
            OwnOption(a);            // ⑧ 판단 엔진 (마): 제 궁리
            MeetingOption(a, here);  // ⑨ 회의에 갈 이는 식당으로
            if (c.Thirst > 0.3f && WaterAllowed(a.Id) >= 5) Add(GoalKind.Drink, -1, -1, c.Thirst * c.Thirst * 6 + (c.Thirst > 0.85f ? 2 : 0)); // 몹시 마른 목(몸의 물 1.7% 모자람)은 한밤의 잠보다 급하다
            WashOptions(a, here, hour);   // 화장실 · 씻기
            float grief = _soc?.GriefOf(a.Id) ?? 0; // ⑨ 슬픔: 일손을 놓고 사람을 찾는다
            if (InWindow(hour, c.ShiftHour, 8) && c.Station >= 0)
            {
                float work = (1.2f + c.Diligent) * (1 - 0.7f * grief) * WorkFactor(a);
                Add(GoalKind.Work, c.Station, L.Furniture[c.Station].Cells[0], work);
                FarmOption(a, here, work);   // 농부의 일: 일하는 재배실에 다 자란 작물을 보면 거둔다 (DF6)
            }
            if (c.Bored > 0.3f) Add(GoalKind.Relax, -1, -1, c.Bored * 1.5f);
            else if (c.Bored > 0.1f && !InWindow(hour, c.ShiftHour, 8)) Add(GoalKind.Relax, -1, -1, 0.35f); // 일 밖의 빈 시간: 우두커니 서 있기보다 좋아하는 것을 한다
            // ⑧ 운동: 하루 한 번쯤 — 성실 · 외향일수록 챙기고 신경성은 덜 (운동 습관 연구) · 일하는 시간 밖에서 · 졸리지 않을 때 (ISS 승무원은 하루 2시간 남짓)
            //   (교대에 맞춘 '밤' 시간이 아니라 졸림으로 — 잠이 밀려 깨어 있는 저녁에도 운동할 수 있게)
            if (c.Unfit > 0.5f && !InWindow(hour, c.ShiftHour, 8) && c.Sleepy < 0.6f)
                Add(GoalKind.Exercise, -1, -1, c.Unfit * (0.2f + 1.4f * c.Consc + 0.4f * c.Extra - 0.4f * c.Neuro));
            // 어울리기: 어울릴 사람이 깨어 있고 일하지 않을 때만 (모두 자거나 일하는데 혼자 휴게실에 앉아 몇 시간씩 기다리지 않는다)
            if ((c.Lonely > 0.3f || grief > 0.3f) && SomeoneFree(a)) Add(GoalKind.Socialize, -1, -1, (Math.Max(c.Lonely, grief) * (0.5f + c.Sociable) * 1.5f + grief) * (1 - c.Depress));   // ⑧ 우울하면 사람을 덜 찾는다
            // 아주 외향적인 이는 외롭지 않아도 빈 시간을 사람 곁에서 보낸다 (어울림이 그의 쉼 — 심심함을 사람과 푼다)
            if (c.Extra > 0.6f && c.Bored > 0.1f && !InWindow(hour, c.ShiftHour, 8) && SomeoneFree(a))
                Add(GoalKind.Socialize, -1, -1, (Math.Max(0.35f, c.Bored * 1.5f) + 0.1f) * (c.Extra - 0.5f) * 2.5f * (1 - c.Depress));
            CrisisOptions(a);        // DF7 위기 뒤: 겪은 이 · 말로만 들은 이는 사람 곁을 찾는다
            HeartOptions(a, here, InWindow(hour, c.ShiftHour, 8));   // ⑧ 공황 발작 · 침대에 틀어박힘
            StashOption(a, here);    // 손 비우기 · 돌려놓기 (docs/MIND.md 3단)
            GatherOption(a, here);   // 맡긴 줍기 · 꼼꼼한 이는 보이는 바닥 물건을 줍는다
            Add(GoalKind.Idle, -1, here, 0.3f * (1 - DreadAt(a, here)));   // 꺼리는 방에서는 우두커니 머물지 않는다
            JobOption(a);
            // ⑨ 몸싸움: 덤빈 이는 그 자리에서 · 맞는 이는 겁이 많으면 방을 빠져나가 문을 닫는다
            if (_soc != null && _soc.InBrawl(a.Id, out int foe, out bool attacker))
            {
                if (attacker || c.Brave >= 0.4f) Add(GoalKind.Brawl, foe, here, 8);
                else Add(GoalKind.Evacuate, RoomOf(here), Agents[foe].C.Cell, 8 + (1 - c.Brave));
            }
        }
        else
        {
            float low = c.Battery < 0.1f ? 8 : c.Battery < 0.3f ? 4 : c.Battery < 0.95f && c.Goal == GoalKind.Charge ? 1.5f : 0.4f;
            int dock = ChargeDock(a);   // 죽은 독은 피한다 (docs/MIND.md 3단 덧붙임)
            Add(GoalKind.Charge, dock, dock, low);
            if (!drone) { JobOption(a); StashOption(a, here); GatherOption(a, here); PatrolOption(a, here); }   // 돌려놓기 · 줍기 (docs/MIND.md 3단) · 순회 (DF5)
            else AssignedLook(a);   // 드론: 주컴퓨터가 맡긴 살피기 (쾅 소리 · 뜨거운 자리 · 진공이 된 방 — 예전엔 맡김을 쥐고만 있었다)
        }

        float caution = person ? 1 - c.Fear * (1 - c.Brave) : 1;
        if (person && _life![c.Body].Suit == 0)
        {
            // 내 방이 위험하다고 믿으면 나간다 (문은 닫고) — 압력이 다 빠지기 전, 문이 아직 열릴 때
            int myRoom = RoomOf(here);
            float worst = 0; int cell = -1, why = 0;
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
                if (s > worst) { worst = s; cell = b.Cell; why = b.Cause; }
            }
            // 이 방의 위험을 다루러 들어온 사람(불 끄기 · 구하기 · 막기)은 나가지 않는다 — 정말 위험하면 본능이 끊는다
            bool committed = c.Goal is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Cpr && a.Plan.Count > 0 && c.StepIndex < a.Plan.Count && InRoom(c.GoalCell, myRoom);
            // 지나가는 중: 지금 걸음이 위험하지 않다고 믿는 다른 방으로 가고 있으면 새로 대피하지 않는다 (통로를 지날 때마다 문을 여닫지 않게)
            bool passing = a.Plan.Count > 0 && c.StepIndex < a.Plan.Count && a.Plan[c.StepIndex].Kind == StepKind.GoTo && a.Plan[c.StepIndex].Cell >= 0
                && RoomOf(a.Plan[c.StepIndex].Cell) != myRoom && RoomBadness(a, RoomOf(a.Plan[c.StepIndex].Cell)) < 0.3f;
            if (worst > 0 && !committed && !passing) Add(GoalKind.Evacuate, myRoom, cell, worst, why != 0 ? why : GasSource(cell));   // 나가는 까닭(그 위험의 원인)에 잇는다 — 닫고 간 문 · 함께 넘긴 기억
            // ⑨ 따라 나간다: 짝 · 이끄는 이 · 같은 무리가 이 방에서 빠져나가면 위험을 몰라도 함께 나간다
            bool followedLately = c.FollowedAt > 0 && sim.Tick - c.FollowedAt < Time.Minutes(10);  // 방금 따라 나섰다 (큰 방에서 되풀이해 따라 나서지 않게)
            if (!committed && !passing && !followedLately && c.Goal != GoalKind.Evacuate && FollowOut(a, myRoom, out int danger) is float follow && follow > worst)
            {
                Add(GoalKind.Evacuate, myRoom, danger >= 0 ? danger : here, follow);
                _followScore = follow;
                if (DebugAgent == a.Id) Console.WriteLine($"    [따라 나간다 {a.Id}] {follow:0.0}");
            }
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
                case BeliefKind.Injured when !drone && b.Subject >= 0 && b.Subject < _life!.Bodies.Count && _life.Bodies[b.Subject].Alive != 0 && _life.Bodies[b.Subject].Arrest != 0:
                {
                    // ⑦ 멎은 심장: 쓰러진 이 곁에 가면 숨도 맥도 없음을 안다 — 안전한 곳이어도 가슴을 누르러 간다 (둘까지 — 하나는 제세동기를 가져온다)
                    float bond = person && _soc != null ? Math.Max(0, _soc.BondOf(a.Id, AgentOfBody(b.Subject))) : 0;
                    float s = person ? b.Conf * (1.5f + c.Kindness + 2 * c.SkMedic + 2 * bond) * 2.5f * (caution + bond * (1 - caution)) : b.Conf * 6.5f;
                    Add(GoalKind.Cpr, b.Subject, b.Cell, s * df * ClaimFactor(a, GoalKind.Cpr, b.Subject, 2), b.Cause);
                    break;
                }
                case BeliefKind.Injured when !drone && (b.Src is BeliefSource.Told or BeliefSource.Heard || !SafeAt(b.Cell)):
                {
                    float bond = person && _soc != null ? Math.Max(0, _soc.BondOf(a.Id, AgentOfBody(b.Subject))) : 0; // ⑨ 친구는 더 무릅쓰고 구한다
                    bool mate = person && _soc != null && _soc.PartnerOf(a.Id) == AgentOfBody(b.Subject);
                    if (mate) bond = 1;                                                                               // 짝은 누구보다 먼저 달려간다
                    float s = person ? b.Conf * (1 + c.Kindness + c.SkMedic + 2 * bond) * 2.2f * (caution + bond * (1 - caution)) : b.Conf * 5.5f;
                    Add(GoalKind.Rescue, b.Subject, b.Cell, s * df * (mate ? 1 : ClaimFactor(a, GoalKind.Rescue, b.Subject, 1)), b.Cause); // 남이 맡았어도 짝은 간다
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
                case BeliefKind.Grind when !drone && b.Subject >= 0 && _net != null && b.Subject < _net.Devices.Count && (!person || c.SkRepair >= GrindSkill):
                {
                    // 기계 소리가 이상하다: 로봇이 먼저 · 고치는 솜씨 있는 이가 기계 부품을 미리 간다 (기계 부품 — 멈췄으면 가서 보고 고친다)
                    float imp = Importance(b.Subject);
                    float s = person ? b.Conf * (0.3f + c.SkRepair) * 2 * imp * caution : b.Conf * 3.5f * imp;
                    int key = b.Subject * 4 + (int)Part.Mechanical;
                    Add(GoalKind.Maintain, key, _net.Devices[b.Subject].Cell, s * df * ClaimFactor(a, GoalKind.Maintain, key, 1), b.Cause);
                    break;
                }
                case BeliefKind.Strain when !drone && L.Kind[b.Cell] is CellKind.Wall or CellKind.Door && _st != null && (_st.Braced[b.Cell] == 0 || _st.JammedAt(b.Cell)):
                {
                    // 본 · 들은 끼익거리는 문 · 벽: 덧대 받친다 (사람은 수리 꾸러미)
                    float s = person ? b.Conf * (0.3f + c.SkRepair) * 3 * caution : b.Conf * 5;
                    Add(GoalKind.Brace, b.Cell, b.Cell, s * df * ClaimFactor(a, GoalKind.Brace, b.Cell, 1), b.Cause);
                    break;
                }
                // ⑨ 몸싸움: 맞는 이의 비명을 들었거나 맞는 것을 봤다 — 로봇은 말리러 간다 · 다정하고 용감한 이 · 이끄는 이 · 짝 · 같은 무리도 (같은 방에 들어서면 사회가 말림을 셈한다)
                case BeliefKind.Help when !drone && b.Subject >= 0 && b.Subject != a.Id && _soc != null && _soc.InBrawl(b.Subject, out int hitter, out bool att) && !att && hitter != a.Id:
                {
                    float k = person ? Kin(a.Id, b.Subject) : 0;
                    float s = person ? (c.Agree > 0.45f && c.Brave > 0.3f || k > 0 ? (c.Agree + c.Brave + 2 * Math.Max(0, k)) * 1.5f * caution : 0) : 6;
                    if (s > 0 && (RoomOf(here) != RoomOf(b.Cell) || Dist(here, b.Cell) > 3))
                        Add(GoalKind.Investigate, b.Cell, b.Cell, s * df * ClaimFactor(a, GoalKind.Investigate, b.Cell, 1), b.Cause);
                    break;
                }
                case BeliefKind.LeftOn when !drone: SwitchOption(a, b, df, caution); break;   // DF5 보면 끈다
                case BeliefKind.Dead when !drone: LayOutOption(sim, a, b, df, caution); break;     // DF5 시신 수습
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

    /// <summary>농부(기르는 일을 맡은 이)가 일하는 시간에 일터 방에서 다 자란 작물을 본다 — 거두기가 그의 일이다 (일보다 조금 앞서게 · 남이 거두러 가는 작물은 두고).</summary>
    private void FarmOption(Agent a, int here, float work)
    {
        ref var c = ref a.C;
        if (_life == null || _net == null || c.Station < 0 || c.Role >= RoleNames.Length || RoleNames[c.Role] != "농부") return;
        int room = L.Furniture[c.Station].Room;
        if (room < 0 || RoomOf(here) != room) return;   // 일터에 와서 본다
        for (int k = 0; k < _life.Crops.Count; k++)
        {
            var cr = _life.Crops[k];
            if (RoomOf(cr.Cell) != room || cr.Biomass < Work.HarvestAt * _life.CropArea(_net, k)) continue;
            bool taken = false;
            foreach (var o in Agents) if (o != a && o.C.Goal == GoalKind.Harvest && o.C.GoalTarget == k) { taken = true; break; }
            if (taken) continue;
            Add(GoalKind.Harvest, k, cr.Cell, work + 1.5f);
            return;
        }
    }

    /// <summary>그 방 문 너머의 빈 바닥 (다른 방 — 가까운 것 · 없으면 −1).</summary>
    private int OutsideDoor(int room, int from)
    {
        int best = -1, bd = int.MaxValue;
        Span<int> nb = stackalloc int[4];
        foreach (int dc in _roomDoors[room])
        {
            L.Neighbors(dc, nb);
            foreach (int j in nb)
                if (j >= 0 && L.Kind[j] == CellKind.Floor && L.FurnitureAt[j] < 0 && L.Room[j] >= 0 && L.Room[j] != room && Manhattan(from, j) < bd) { bd = Manhattan(from, j); best = j; }
        }
        return best;
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
                // ⑩ 계획에 묶인 로봇: 그 칸까지 가서 기다린다 (지어낸 계획의 '지나 간다' — 그 방 한가운데)
                if (c.AssignedArg == PlanBound && o.Target >= 0 && o.Target != here && RoomOf(here) != RoomOf(o.Target)) { plan.Add(Go(o.Target)); plan.Add(Use(UseKind.None, 10)); return true; }
                if (L.Kind[here] == CellKind.Door)
                {
                    // 문간에 서 있지 않는다
                    Span<int> nb = stackalloc int[4];
                    L.Neighbors(here, nb);
                    foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Floor) { plan.Add(Go(j)); break; }
                }
                else if (a.IsPerson && RoomOf(here) is int wr && wr >= 0 && L.Rooms[wr].Type == "Washroom" && OutsideDoor(wr, here) is int outside && outside >= 0)
                    plan.Add(Go(outside));   // 다 쓴 씻는 방에서 우두커니 서 있지 않는다 — 문밖으로 (서 있는 이를 주컴퓨터가 줄로 읽었다 · DF5)
                plan.Add(Use(UseKind.None, 10));
                return true;
            }
            case GoalKind.Sleep:
            {
                if (o.Target >= 0 && o.Target < L.N && IsMedBedCell(o.Target))   // ⑦ (다) 진료 기록: 치료 침대에 누워 산소를 마신다 · (라-2) 재생 젤
                {
                    plan.Add(Go(o.Target));
                    plan.Add(Use(UseKind.Sleep, Time.Hours(_life![c.Body].Regen > 0 ? 8 : 4)));
                    return true;
                }
                if (Sheltering(a) && (c.Bed < 0 || RoomOf(c.Bed) != ShelterRoom))
                {
                    int spot = StandSpot(a, ShelterRoom, ShelterCell);   // 문 칸 · 문 곁 · 남의 자리가 아닌 바닥 (5단 C1)
                    if (spot != here) plan.Add(Go(spot, 0));
                    plan.Add(Use(UseKind.Sleep, Time.Hours(10)));
                    return true;
                }
                bool shunned = c.Bed >= 0 && BedTaboo(c.Bed);   // ⑨ 숨진 이와 함께 쓰던 침대 — 다른 빈 침대가 없으면 그래도 눕는다
                bool torn = c.Bed >= 0 && _st != null && L.FurnitureAt[c.Bed] >= 0 && !_st.Usable(L.FurnitureAt[c.Bed]);   // 뜯긴 침대에는 눕지 않는다 (PH10 — 빈 침대 · 없으면 바닥)
                int bed = c.Bed >= 0 && !Taken(a, c.Bed) && !shunned && !torn ? c.Bed : NearestFree(a, _beds);
                if (shunned && !torn) { if (bed >= 0) BedsShunned++; else if (!Taken(a, c.Bed)) bed = c.Bed; }
                int airy = ZeroGSleepSpot(c, bed >= 0 ? bed : here);   // 무중력에서 날숨에 깬 적이 있으면 통풍구 앞에 침낭을 묶고 잔다
                if (airy >= 0) { plan.Add(Go(airy)); plan.Add(Use(UseKind.Sleep, Time.Hours(10))); return true; }
                if (bed >= 0) plan.Add(Go(bed));
                plan.Add(Use(UseKind.Sleep, Time.Hours(10)));
                return true;
            }
            case GoalKind.Eat:
            {
                int src = MealSource(a, here);   // ⑨ 가까운 이와 같은 자리 · 벌어진 무리 사람이 먹는 자리는 피한다
                if (src < 0) return false;
                if (a.IsPerson && a.C.Hunger <= 0.55f && MealTime(Time.HourOfDay(_nowTick))) CustomEats++;   // ⑨ 배가 덜 고파도 관습의 식사에 간다
                int cell = L.Furniture[src].Cells[0];
                plan.Add(Go(cell, 1));
                plan.Add(Use(UseKind.Eat, Time.Minutes(10), cell, src));
                return true;
            }
            case GoalKind.Cook: return BuildCook(a, o, plan);
            case GoalKind.Treat: return BuildTreat(a, o, plan, here);
            case GoalKind.Cpr: return BuildCpr(a, o, plan, here);
            case GoalKind.Surgery: return BuildSurgery(a, o, plan, here);
            case GoalKind.Attack or GoalKind.Withdraw: return BuildHeart(a, o, plan, here);
            case GoalKind.Counsel: return BuildCounsel(a, o, plan);
            case GoalKind.Pilfer:
            {
                if (o.Target < 0) return false;
                int cell = L.Furniture[o.Target].Cells[0];
                plan.Add(Go(cell, 1));
                plan.Add(Use(UseKind.Pilfer, Time.Minutes(3), cell, o.Target));
                return true;
            }
            case GoalKind.Drink:
            {
                if (_net != null && _net.WaterTank < 30) return false;
                int src = Nearest(here, _water, f => _net == null || _net.WaterAt(f));   // 물관이 끊긴 자리에선 물이 안 나온다
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
            case GoalKind.Relax: return BuildRelax(sim, a, plan);
            case GoalKind.Toilet: return PlanToilet(sim, a, o, plan);
            case GoalKind.Wash: return PlanWash(sim, a, o, plan);
            case GoalKind.ToiletCare: return PlanToiletCare(sim, a, o, plan);
            case GoalKind.Meeting:
            {
                if (GatherRoom < 0) return false;
                plan.Add(Go(StandSpot(a, GatherRoom, o.Cell), 0));   // 머물 자리 (5단 C1)
                plan.Add(Use(UseKind.None, Time.Minutes(20)));
                return true;
            }
            case GoalKind.Shelter:
            {
                if (ShelterRoom < 0) return false;
                plan.Add(Go(StandSpot(a, ShelterRoom, o.Cell), 0));   // 머물 자리 (5단 C1)
                plan.Add(Use(UseKind.None, Time.Minutes(5)));
                return true;
            }
            case GoalKind.Exercise:
            {
                // 달리기틀이 비어 있으면 거기서 · 없으면 침대 곁 맨몸 운동
                int gym = NearestFree(a, _gym);
                int at = gym >= 0 ? gym : c.Bed >= 0 ? c.Bed : here;
                plan.Add(Go(at, gym >= 0 ? 0 : 1));
                plan.Add(Use(UseKind.Exercise, Time.Minutes(45)));
                return true;
            }
            case GoalKind.Brawl:
                plan.Add(Use(UseKind.None, Time.Minutes(1)));
                return true;
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
                if (o.Target < 0) return false;
                plan.Add(Go(o.Target));
                plan.Add(Use(UseKind.Charge, Time.Hours(3)));
                return true;
            }
            case GoalKind.FightFire:
            {
                // ② (바) 소화기 고르기: 전기가 흐르는 곳의 불이면 아는 이(로봇 · 전기 0.3 넘게)는 이산화탄소 · 아니면 물 (식혀 다시 붙지 않게) · 모르면 가까운 것
                var (want, picky) = ChooseExtinguisher(a, o.Cell, here);
                bool held = (c.Holding == iExt || c.Holding == iCO2) && c.HoldCharge >= 20;
                // 꾸밈의 방화 담요: 막 붙은 작은 불(보이는 불꽃이 작다)이면 — 소화기가 없거나 담요가 훨씬 가까울 때 · 이미 들고 있으면 바로 덮는다
                if (!held && _chem != null && _chem.Hrr[o.Cell] <= Chemistry.BlanketMaxKw)
                {
                    int bl = c.Holding == iBlanket ? -2 : NearestStock(here, iBlanket), ext = NearestStock(here, want);
                    if (bl == -2 || bl >= 0 && (ext < 0 || Manhattan(here, L.Furniture[bl].Cells[0]) + 4 < Manhattan(here, L.Furniture[ext].Cells[0])))
                    {
                        if (bl >= 0)
                        {
                            plan.Add(Go(L.Furniture[bl].Cells[0], 1));
                            plan.Add(new PlanStep { Kind = StepKind.Take, Target = bl, Arg = iBlanket });
                        }
                        plan.Add(Go(o.Cell, 1));
                        plan.Add(new PlanStep { Kind = StepKind.Spray, Cell = o.Cell });
                        return true;
                    }
                }
                if (!held || picky && c.Holding != want)
                {
                    int src = NearestStock(here, want);   // 다른 것을 들고 있으면 소화기를 집을 때 내려놓는다 (수리 꾸러미를 든 채 불을 못 끄던 빈틈)
                    if (src < 0) { want = want == iCO2 ? iExt : iCO2; src = NearestStock(here, want); }
                    if (src < 0) { if (!held) return false; }
                    else
                    {
                        plan.Add(Go(L.Furniture[src].Cells[0], 1));
                        plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = want });
                    }
                }
                plan.Add(Go(o.Cell, 2));
                plan.Add(new PlanStep { Kind = StepKind.Spray, Cell = o.Cell });
                return true;
            }
            case GoalKind.GetSuit:
            {
                // 맡긴 우주복 입기: 그 보관함으로 가서 입는다 (손에 든 것은 든 채로)
                int locker = o.Target;
                if (locker < 0 || locker >= L.Furniture.Count || _life![c.Body].Suit != 0) return false;
                plan.Add(Go(L.Furniture[locker].Cells[0], 1));
                plan.Add(new PlanStep { Kind = StepKind.Wear, Target = locker, Arg = iSuit });
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
                int stand = TapeAssigned(a, wall) ? StandOnMySide(a, wall) : StandBeside(wall);   // 맡긴 문틈 막기: 제 쪽에서 (임시 에어락 — 비울 방 안에 남는다)
                if (stand < 0) return false;
                if (a.IsPerson)
                {
                    if (c.Holding != iPatch)
                    {
                        FreeHands(a, ref here, plan, true);   // 급하다 — 든 것을 그 자리에 내려놓는다
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
                if (part == PullFeederArg)
                {
                    if (a.IsPerson && c.Holding != iRepair)
                    {
                        FreeHands(a, ref here, plan, true);
                        int src = NearestStock(here, iRepair);
                        if (src < 0) return false;
                        plan.Add(Go(L.Furniture[src].Cells[0], 1));
                        plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = iRepair });
                    }
                }
                else if (Smashed(dev)) return PlanRebuild(a, dev, here, plan);   // ⑪ 부서진 설비: 틀부터 (외장 틀) — 부품은 그다음
                else if (!PlanPart(a, dev, part, here, plan)) return false;   // ⑥ 부품: 로봇 주머니 · 갈래 부품 · 없으면 덜 중요한 장치에서 떼어 온다
                plan.Add(Go(_net.Devices[dev].Cell, 1));
                plan.Add(new PlanStep { Kind = StepKind.Fix, Target = dev, Arg = (byte)part });
                return true;
            }
            case GoalKind.Rewire:
            {
                return PlanRewire(a, o.Target, plan);
            }
            case GoalKind.Improvise:
                plan.AddRange(a.Own);   // 제 궁리 그대로 (다시 고르면 처음부터 — 이미 된 걸음은 곧 지나간다)
                return plan.Count > 0;
            case GoalKind.Brace:
            {
                int wall = o.Target;
                int stand = StandBeside(wall);
                if (stand < 0 || _st == null || _st.Braced[wall] != 0 && !_st.JammedAt(wall)) return false;
                if (a.IsPerson && c.Holding != iRepair)
                {
                    FreeHands(a, ref here, plan, true);
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
                byte spare = SpareOf(part);   // ⑪ 그 갈래 부품
                if (!a.IsPerson && !InPouch(c, part) && NearestStock(here, spare) < 0) return false;   // 로봇: 주머니에도 배에도 그 갈래가 없다
                if (a.IsPerson && c.Holding != spare)
                {
                    FreeHands(a, ref here, plan, false);
                    int src = NearestStock(here, spare);
                    if (src < 0) return false;
                    plan.Add(Go(L.Furniture[src].Cells[0], 1));
                    plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = spare });
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
                if (_life == null || o.Target >= _life.Crops.Count) return false;
                if (c.Holding != iHarvest) FreeHands(a, ref here, plan, false);   // 든 것을 먼저 제자리에
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
            case GoalKind.Restock:
            {
                // 식량 보관함 → 식량 상자 → 주방 (맡긴 일: Target 냉장고 · 배식기, 맡긴 Arg 보관함 · 들고만 있으면 가까운 주방)
                if (c.Holding != iRation) FreeHands(a, ref here, plan, false);
                int point = o.Target >= 0 ? o.Target : Nearest(c.Cell, _food, _ => true);
                if (point < 0) return false;
                if (c.Holding != iRation)
                {
                    int locker = c.AssignedGoal == GoalKind.Restock ? c.AssignedArg : -1;
                    if (locker < 0 || locker >= L.Furniture.Count) return false;
                    plan.Add(Go(L.Furniture[locker].Cells[0], 1));
                    plan.Add(new PlanStep { Kind = StepKind.TakeFood, Target = locker });
                }
                plan.Add(Go(L.Furniture[point].Cells[0], 1));
                plan.Add(new PlanStep { Kind = StepKind.Stow, Target = point });
                return true;
            }
            case GoalKind.Haul:
            {
                // ⑪ 헌 부품: 놓인 설비 → 손에 들고 → 정제기 (맡긴 일: Target 정제기 · Arg 설비 × 256 + 물건 · 들고만 있으면 가까운 정제기)
                bool scrap = c.Holding == iScrap || c.Holding == iScrapElec;
                if (!scrap) FreeHands(a, ref here, plan, false);
                int refinery = o.Target >= 0 ? o.Target : NearestRefinery(c.Cell);
                if (refinery < 0) return false;
                if (!scrap)
                {
                    int src = c.AssignedGoal == GoalKind.Haul ? c.AssignedArg / 256 : -1, item = c.AssignedArg % 256;
                    if (src < 0 || src >= L.Furniture.Count || Stock[src * ItemCount + item] <= 0) return false;
                    plan.Add(Go(L.Furniture[src].Cells[0], 1));
                    plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = (byte)item });
                }
                plan.Add(Go(L.Furniture[refinery].Cells[0], 1));
                plan.Add(new PlanStep { Kind = StepKind.Put, Target = refinery });
                return true;
            }
            case GoalKind.SwitchOff: return PlanSwitch(o, plan);
            case GoalKind.Patrol: return PlanPatrol(a, o, plan);
            case GoalKind.LayOut: return PlanLayOut(a, o, plan);
            case GoalKind.Stash: return PlanStash(a, o, plan);
            case GoalKind.Refill: return PlanRefill(a, o, here, plan);
            case GoalKind.Gather: return PlanGather(a, o, here, plan);
            case GoalKind.Tighten:
            {
                // ④ 풀린 볼트: 장치 곁으로 가서 조인다 (연장은 몸에 — 사람 손 · 로봇 팔)
                if (_net == null || o.Target < 0 || o.Target >= _net.Devices.Count) return false;
                plan.Add(Go(_net.Devices[o.Target].Cell, 1));
                plan.Add(new PlanStep { Kind = StepKind.Tighten, Target = o.Target });
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
            if (L.Kind[c] == CellKind.Floor && L.Room[c] != room && L.Room[c] >= 0 && (danger < 0 || Dist(c, danger) > 4) && SafeAt(c) && !FledFrom(a, L.Room[c]) && !KeptOut(a, L.Room[c])
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

    /// <summary>일상 목표 (위기를 다루는 일이 아닌 것) — 빠져나온 위험한 방으로는 이것 때문에 돌아가지 않는다.</summary>
    private static bool Everyday(GoalKind g) => g is GoalKind.Sleep or GoalKind.Eat or GoalKind.Drink or GoalKind.Work or GoalKind.Relax or GoalKind.Socialize or GoalKind.Toilet or GoalKind.Wash
        or GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Exercise or GoalKind.Restock or GoalKind.Cook or GoalKind.Haul or GoalKind.Tighten or GoalKind.ToiletCare
        or GoalKind.Stash or GoalKind.Refill or GoalKind.Gather;

    /// <summary>
    /// 피하는 칸: 사람이 방금(10분) 빠져나온 방이고 아직 위험하다고 믿는다 (지금 그 방 안이면 아니다).
    /// 디딤호에서: 일터가 금 간 방 안이라 빠져나오고 · 일하러 들어가고 · 또 빠져나오며 5분 동안 문을 여닫았다.
    /// </summary>
    private bool Shunned(Agent a, int cell)
    {
        if (!a.IsPerson || cell < 0) return false;
        int r = RoomOf(cell);
        if (r < 0) return false;
        if (r == RoomOf(a.C.Cell)) return a.C.Goal == GoalKind.Evacuate && RoomBadness(a, r) >= 0.5f; // 빠져나가는 중에 그 방의 일상 일(밥 짓기 · 끼니)로 돌아서지 않는다
        return FledFrom(a, r) && RoomBadness(a, r) >= 0.3f;
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

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int NearestFree(Agent a, int[] furn, int habit = -1)
    {
        int best = -1, bd = int.MaxValue;
        foreach (int fu in furn)
        {
            int cell = L.Furniture[fu].Cells[0];
            if (Taken(a, cell) || BedTaboo(cell)) continue;   // ⑨ 숨진 이의 침대에는 눕지 않는다
            if (_st != null && !_st.Usable(fu)) continue;   // 뜯긴 · 밀려난 가구는 쓰지 않는다 (PH10)
            bool used = false;
            foreach (var o in Agents) if (o != a && o.C.Cell == cell && o.C.Goal is GoalKind.Relax or GoalKind.Socialize or GoalKind.Sleep) { used = true; break; }
            if (used) continue;
            int d = Manhattan(a.C.Cell, cell) + (int)(60 * DreadAt(a, cell)) - (habit >= 0 ? HabitPull(a, (Hobby)habit, cell) : 0); // 꺼리는 방은 멀게 · 단골 자리는 가깝게
            if (d < bd) { bd = d; best = cell; }
        }
        return best;
    }

    /// <summary>주컴퓨터가 로봇 · 드론에게 맡긴 살피기 (빠진 방 · 외판 둘러보기 · 쾅 소리 · 뜨거운 자리).</summary>
    private void AssignedLook(Agent a)
    {
        ref var c = ref a.C;
        if (!a.IsPerson && c.AssignedGoal == GoalKind.Investigate && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0) Add(GoalKind.Investigate, c.AssignedTarget, c.AssignedTarget, 1.2f);
    }

    /// <summary>⑪ 일감판이 맡긴 일 (만들기 · 정비 · 청소) — 맡긴 보너스(+3)가 붙는다. 주컴퓨터가 로봇에게 맡긴 살피기(빠진 방 · 외판 둘러보기)도.</summary>
    private void JobOption(Agent a)
    {
        ref var c = ref a.C;
        AssignedLook(a);
        // ⑩ 판단 엔진: 지어낸 계획이 '이 문틈을 막아라' (아직 기압 차가 없어 새지 않아도 — 공기를 빼기 전에)
        if (c.AssignedGoal == GoalKind.Patch && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0 && TapeAssigned(a, c.AssignedTarget)) Add(GoalKind.Patch, c.AssignedTarget, c.AssignedTarget, a.IsPerson ? 2.2f : 1.2f);
        // ⑩ 판단 엔진: 지어낸 계획이 '여기서 기다려라' — 주컴퓨터가 그 방을 비우는 동안 제 판단으로 문에 가지 않는다 (다친 이를 구하는 일은 앞선다)
        if (c.AssignedGoal == GoalKind.Idle && c.AssignedArg == PlanBound && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0) Add(GoalKind.Idle, c.AssignedTarget, c.AssignedTarget, 2.2f);
        // ⑩ 판단 엔진: 지어낸 계획이 '우주복을 입어라' (사람 — 그 보관함에서)
        if (a.IsPerson && c.AssignedGoal == GoalKind.GetSuit && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0 && _life![c.Body].Suit == 0) Add(GoalKind.GetSuit, c.AssignedTarget, L.Furniture[c.AssignedTarget].Cells[0], 2.2f);
        // ⑥ 맡긴 연장선: 그 방에 아직 전기가 없으면
        if (c.AssignedGoal == GoalKind.Rewire && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0 && _net != null && !_net.RoomPowered(c.AssignedTarget) && c.AssignedTarget < _net.Vent.Length)
            Add(GoalKind.Rewire, c.AssignedTarget, _net.Vent[c.AssignedTarget], 1.3f, _net.RoomFeeder[c.AssignedTarget] is int rf && rf >= 0 ? _net.FaultCause(rf) : 0);   // 원인: 그 방 급전선이 죽은 까닭
        // 맡긴 끼인 문: 믿음이 바래도 간다
        if (!a.IsPerson && c.AssignedGoal == GoalKind.Brace && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0 && _st != null
            && (_st.JammedAt(c.AssignedTarget) || L.Kind[c.AssignedTarget] == CellKind.Wall && _st.Braced[c.AssignedTarget] == 0)) Add(GoalKind.Brace, c.AssignedTarget, c.AssignedTarget, 1.2f);   // 끼인 문 · 주컴퓨터가 받치라고 한 금 간 벽 (금 센서 — 휘는 소리가 없어도)
        // ⑦ 맡긴 돌보기: 다친 이 곁으로 (맡김 가산 +3 — 사람이면 끼니보다 앞선다)
        if (c.AssignedGoal == GoalKind.Treat && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0 && c.AssignedTarget < _life!.Bodies.Count && _life.Bodies[c.AssignedTarget].Alive != 0)
            Add(GoalKind.Treat, c.AssignedTarget, _life.Bodies[c.AssignedTarget].Cell, 1.2f);
        // ⑧ 맡긴 상담 (주컴퓨터의 '마음 돌봄' 부탁): 그 사람 곁으로
        if (c.AssignedGoal == GoalKind.Counsel && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0 && c.AssignedTarget < _life!.Bodies.Count && _life.Bodies[c.AssignedTarget].Alive != 0)
            Add(GoalKind.Counsel, c.AssignedTarget, _life.Bodies[c.AssignedTarget].Cell, 1.2f);
        // ⑦ (라) 맡긴 수술 (주컴퓨터가 진료 기록을 읽었다)
        if (c.AssignedGoal == GoalKind.Surgery && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0 && c.AssignedTarget < _life!.Bodies.Count && _life.Bodies[c.AssignedTarget].Alive != 0)
            Add(GoalKind.Surgery, c.AssignedTarget, _life.Bodies[c.AssignedTarget].Cell, 1.4f);
        // ⑦ 맡긴 가슴 압박 (주컴퓨터가 멎은 심장을 알았다): 믿음이 없어도 간다
        if (c.AssignedGoal == GoalKind.Cpr && c.AssignedUntil > _nowTick && c.AssignedTarget >= 0 && c.AssignedTarget < _life!.Bodies.Count && _life.Bodies[c.AssignedTarget].Alive != 0 && _life.Bodies[c.AssignedTarget].Arrest != 0)
            Add(GoalKind.Cpr, c.AssignedTarget, _life.Bodies[c.AssignedTarget].Cell, 1.5f);
        // 거둔 것을 들고 있으면 (하던 일이 끊겼어도) 식량 창고에 넣는다
        if (c.Holding == iHarvest && _life != null && _life.Crops.Count > 0) Add(GoalKind.Harvest, 0, c.Cell, 1.0f);
        if (c.Holding == iRation) Add(GoalKind.Restock, c.AssignedGoal == GoalKind.Restock && c.AssignedUntil > _nowTick ? c.AssignedTarget : -1, c.Cell, 1.0f);
        // ⑪ 헌 부품을 들고 있으면 (나르기가 끊겼어도) 정제기에 넣는다
        if (c.Holding == iScrap || c.Holding == iScrapElec) Add(GoalKind.Haul, c.AssignedGoal == GoalKind.Haul && c.AssignedUntil > _nowTick ? c.AssignedTarget : -1, c.Cell, 1.0f);
        // 오물 통을 들고 있으면 (비우기가 끊겼어도) 비울 곳으로
        if (c.Holding == iWaste) Add(GoalKind.ToiletCare, -1, c.Cell, 1.0f);
        if (c.AssignedUntil <= _nowTick || c.AssignedGoal is not (GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock or GoalKind.Haul or GoalKind.Tighten or GoalKind.ToiletCare or GoalKind.Refill or GoalKind.LayOut)) return;
        int cell = c.AssignedGoal switch
        {
            GoalKind.Craft or GoalKind.Refill => c.AssignedTarget >= 0 && c.AssignedTarget < L.Furniture.Count && L.Furniture[c.AssignedTarget].Cells.Length > 0 ? L.Furniture[c.AssignedTarget].Cells[0] : -1,
            GoalKind.Maintain => _net != null ? _net.Devices[c.AssignedTarget / 4].Cell : -1,
            GoalKind.Tighten => _net != null && c.AssignedTarget < _net.Devices.Count ? _net.Devices[c.AssignedTarget].Cell : -1,
            GoalKind.Harvest => _life != null && c.AssignedTarget < _life.Crops.Count ? _life.Crops[c.AssignedTarget].Cell : -1,
            GoalKind.Restock => c.AssignedArg >= 0 && c.AssignedArg < L.Furniture.Count ? L.Furniture[c.AssignedArg].Cells[0] : -1,
            GoalKind.Haul => c.AssignedArg >= 0 && c.AssignedArg / 256 < L.Furniture.Count ? L.Furniture[c.AssignedArg / 256].Cells[0] : -1,
            GoalKind.LayOut => _life != null && c.AssignedTarget < _life.Bodies.Count && _life.Bodies[c.AssignedTarget].Alive == 0 && _life.Bodies[c.AssignedTarget].Shroud == 0 ? _life.Bodies[c.AssignedTarget].Cell : -1,   // DF5 시신 수습
            GoalKind.ToiletCare => c.AssignedArg == 3 ? LooseCell(c.AssignedTarget) : c.AssignedTarget >= 0 && c.AssignedTarget < L.Furniture.Count && L.Furniture[c.AssignedTarget].Cells.Length > 0 ? L.Furniture[c.AssignedTarget].Cells[0] : -1,   // 3: 내려놓인 그 통이 있는 칸 (물체 Id → 장부의 칸)
            _ => c.AssignedTarget,
        };
        if (cell >= 0) Add(c.AssignedGoal, c.AssignedTarget, cell, 1.2f);
    }

    /// <summary>그 방에 있는 이들에 대한 정의 합 (⑨ — 친구가 있는 곳으로 간다).</summary>
    private float FriendsIn(Agent a, int room)
    {
        if (_soc == null) return 0;
        float s = 0;
        foreach (var o in Agents) if (o != a && o.IsPerson && o.C.Up != 0 && RoomOf(o.C.Cell) == room)
            s += (_soc.Liking(a.Id, o.Id) >= 0 ? _soc.BondOf(a.Id, o.Id) : 2 * _soc.Liking(a.Id, o.Id)) - (_soc.Rival(a.Id, o.Id) ? 0.5f : 0); // 미운 이 · 벌어진 무리 사람이 있는 방은 피한다
        return s;
    }

    /// <summary>사람이 가장 많이 모인 사교 방의 빈자리.</summary>
    /// <summary>나 말고 깨어 있고 일하지 않는 사람이 있나 (어울릴 상대).</summary>
    private bool SomeoneFree(Agent a)
    {
        foreach (var o in Agents)
            if (o != a && o.IsPerson && o.C.Up != 0 && o.C.Goal is not (GoalKind.Work or GoalKind.Sleep) && (_life == null || _life[o.C.Body].Asleep == 0)) return true;
        return false;
    }

    // BusiestSeat가 한 번 부를 때마다 쓰는 표 (방마다 어울리는 이 수 · 아는 얼굴 · 꺼림 — 자리마다 사람 · 로봇을 다시 훑지 않게: 값 · 차례는 같다 · 찍지 않는다)
    private int[] _seatCount = Array.Empty<int>(), _seatRoomStamp = Array.Empty<int>(), _seatOcc = Array.Empty<int>();
    private float[] _seatFriends = Array.Empty<float>(), _seatShun = Array.Empty<float>();
    private int _seatStamp;
    private readonly List<(int cell, int room)> _seatLeisure = new();

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int BusiestSeat(Agent a)
    {
        if (_social.Length == 0) return -1;
        int R = L.Rooms.Count;
        if (_seatCount.Length != R + 1) { _seatCount = new int[R + 1]; _seatRoomStamp = new int[R + 1]; _seatFriends = new float[R + 1]; _seatShun = new float[R + 1]; }
        if (_seatOcc.Length != L.N) _seatOcc = new int[L.N];
        if (++_seatStamp == int.MaxValue) { Array.Clear(_seatOcc); Array.Clear(_seatRoomStamp); _seatStamp = 1; }
        int stamp = _seatStamp;
        Array.Clear(_seatCount);
        _seatLeisure.Clear();
        foreach (var o in Agents)
        {
            if (o == a) continue;
            if ((uint)o.C.Cell < (uint)_seatOcc.Length) _seatOcc[o.C.Cell] = stamp;   // 남이 선 칸 (빈자리가 아니다)
            if (!o.IsPerson || o.C.Up == 0 || !Society.Leisure(o.C.Goal)) continue;   // 쉬는 이 (어울리는 이도 쉬는 이다)
            int r = RoomOf(o.C.Cell);
            if (o.C.Goal == GoalKind.Socialize) _seatCount[r + 1]++;
            _seatLeisure.Add((o.C.Cell, r));
        }
        int best = -1; double bs = double.MinValue;
        foreach (int fu in _social)
        {
            int cell = L.Furniture[fu].Cells[0];
            if (_seatOcc[cell] == stamp) continue;
            int room = L.Furniture[fu].Room;
            if (_seatRoomStamp[room + 1] != stamp) { _seatRoomStamp[room + 1] = stamp; _seatFriends[room + 1] = FriendsIn(a, room); _seatShun[room + 1] = Shun(a.Id, room); }
            // 어울리러 가면 같은 방에서 쉬는 사람 곁 자리에 앉는다 (가까울수록 — 말이 닿는 거리면 크게) — 의자가 많은 방에서 혼자 먼 구석에 앉지 않게
            double near = 0;
            foreach (var (oc, or) in _seatLeisure) if (or == room) near += Math.Max(0, 10 - Dist(oc, cell));
            double s = _seatCount[room + 1] * 10 + _seatFriends[room + 1] * 15 + near - Manhattan(a.C.Cell, cell) * 0.1
                       - _seatShun[room + 1] * 30; // 꺼리는 방 · 금기의 방은 사람이 모여 있어도 덜 간다
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

    /// <summary>
    /// ② (바) 불에 들고 갈 소화기: 불 자리가 전기가 흐르는 곳이면 — 로봇 · 전기를 아는 사람(0.3 넘게)은 이산화탄소 (없으면 물) ·
    /// 아니면 — 로봇 · 화학을 아는 사람은 물 (식혀 다시 붙지 않게) · 모르는 사람은 가까운 것. picky: 아는 이는 다른 것을 들고 있으면 바꿔 든다.
    /// </summary>
    private (byte item, bool picky) ChooseExtinguisher(Agent a, int fire, int here)
    {
        bool live = false;
        if (_net != null && fire >= 0)
        {
            int fx = L.X(fire), fy = L.Y(fire);
            for (int y = Math.Max(0, fy - 1); y <= Math.Min(L.H - 1, fy + 1) && !live; y++)
                for (int x = Math.Max(0, fx - 1); x <= Math.Min(L.W - 1, fx + 1) && !live; x++)
                    if (_net.LiveAt(L.Index(x, y))) live = true;
        }
        bool knows = !a.IsPerson || (live ? a.C.KnElectric : a.C.KnChem) >= 0.3f;
        int w = NearestStock(here, iExt), g = NearestStock(here, iCO2);
        if (knows) return (live ? (g >= 0 ? iCO2 : iExt) : (w >= 0 ? iExt : iCO2), true);
        if (w < 0) return (iCO2, false);
        if (g < 0) return (iExt, false);
        return (Manhattan(here, L.Furniture[w].Cells[0]) <= Manhattan(here, L.Furniture[g].Cells[0]) ? iExt : iCO2, false);
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
    /// <summary>문 곁의 칸 가운데 제가 있는 방 쪽 (없으면 StandBeside).</summary>
    private int StandOnMySide(Agent a, int wall)
    {
        Span<int> nb = stackalloc int[4];
        L.Neighbors(wall, nb);
        int room = RoomOf(a.C.Cell);
        foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Floor && room >= 0 && RoomOf(j) == room) return j;
        return StandBeside(wall);
    }

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
