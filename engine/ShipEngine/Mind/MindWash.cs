namespace ShipEngine;

/// <summary>
/// 화장실 · 샤워실의 판단 (⑧ — docs/WASH.md): 마려우면 가장 가까운 빈 변기로 (차 있으면 줄을 서고 · 너무 길면 다른 화장실로) ·
/// 더러우면 · 운동 뒤 · 자기 전에 씻는다 · 변기 뒤엔 손을 씻는다 (성실할수록). 변기가 없는 배에선 용변 봉투 · 샤워 칸이 없으면 물수건.
/// 몸이 차오르고 · 지리고 · 물이 오수로 가는 것은 월드(⑦ LifeWash)가 셈한다 — 여기서는 언제 · 어디로 가나만.
/// </summary>
public sealed partial class Mind
{
    private int[] _toilets = Array.Empty<int>(), _showers = Array.Empty<int>(), _sinks = Array.Empty<int>();
    private int[]? _washDev;   // 설비 → 망의 장치 (-1 없음)
    /// <summary>재기용: 변기에 비운 번 · 용변 봉투 · 샤워 · 물수건 · 손 씻기 · 줄을 선 번 · 줄이 길어 다른 곳으로 간 번.</summary>
    public long Toilets, Bags, ShowersTaken, Wipes, HandWashes, Queued, QueueGaveUp;

    /// <summary>줄을 서서 기다리는 가장 긴 시간 — 넘으면 다른 화장실로 (막히면 다른 길).</summary>
    public static readonly long QueueMax = Time.Minutes(15);
    public const double ShowerWater = Life.ShowerMol, HandWater = 28;   // 샤워 8L · 손 씻기 0.5L

    private void WashSetup()
    {
        int[] Furn(string type) => L.Furniture.Where(fu => fu.Cells.Length > 0 && fu.Type == type).Select(fu => fu.Id).ToArray();
        _toilets = Furn("Toilet"); _showers = Furn("Shower"); _sinks = Furn("Sink");
    }

    private int WashDev(int fu)
    {
        if (_net == null) return -1;
        if (_washDev == null)
        {
            _washDev = Enumerable.Repeat(-1, L.Furniture.Count).ToArray();
            foreach (var d in _net.Devices) if (d.Furniture >= 0 && d.Furniture < _washDev.Length) _washDev[d.Furniture] = d.Id;
        }
        return _washDev[fu];
    }

    /// <summary>마려움 (0~1 — 1이면 참지 못한다): 방광 · 장 가운데 더 찬 쪽 (몸이 안다).</summary>
    public float Urge(Agent a)
    {
        if (!a.IsPerson || _life == null) return 0;
        ref var b = ref _life[a.C.Body];
        return (float)Math.Max(b.Bladder / Life.BladderMax, b.Bowel / Life.BowelMax);
    }

    /// <summary>변기를 쓸 수 있나 (흡입 팬이 멀쩡하고 전기가 들어온다 — 고장 등이 켜져 있으면 안다).</summary>
    private bool ToiletOk(int fu) { int d = WashDev(fu); return d < 0 || _net!.Working(d); }

    /// <summary>그 설비를 지금 누가 쓰나 · 나보다 먼저 줄 선 이가 있나 (계획에서 읽는다 — 따로 적어 두지 않는다).</summary>
    private bool WashAhead(Agent a, int fu, GoalKind g)
    {
        int cell = L.Furniture[fu].Cells[0];
        foreach (var o in Agents)
        {
            if (o != a && o.C.Cell == cell) return true;   // 누가 그 위에 서 있다
            if (o == a || o.C.Goal != g || o.C.GoalTarget != fu || o.C.Up == 0 || o.C.StepIndex >= o.Plan.Count) continue;
            var st = o.Plan[o.C.StepIndex];
            if (st.Kind == StepKind.GoTo && st.Cell == cell && st.Arg == 0) return true;   // 들어가는 중
            if (st.Kind == StepKind.Use && (UseKind)st.Arg is UseKind.Toilet or UseKind.Shower && st.Target == fu) return true;   // 쓰는 중
            if (st.Kind == StepKind.Use && (UseKind)st.Arg == UseKind.Queue && st.Target == fu && o.C.StepIndex > 0
                && (o.C.StepStart < a.C.StepStart || o.C.StepStart == a.C.StepStart && o.Id < a.Id)) return true;   // 먼저 줄 선 이
        }
        return false;
    }

    /// <summary>그 설비를 노리는 이 (줄 길이 — 고를 때 멀게 친다).</summary>
    private int WashLine(Agent a, int fu, GoalKind g)
    {
        int n = 0;
        foreach (var o in Agents) if (o != a && o.C.Goal == g && o.C.GoalTarget == fu && o.C.Up != 0) n++;
        return n;
    }

    private int PickWash(Agent a, int[] furn, GoalKind g, Func<int, bool> ok)
    {
        int best = -1, bd = int.MaxValue;
        foreach (int fu in furn)
        {
            if (!ok(fu)) continue;
            bool avoided = false;
            foreach (var v in a.Avoid) if (v.Goal == g && v.Target == fu) { avoided = true; break; }   // 줄이 길어 돌아선 곳은 한동안 고르지 않는다
            if (avoided) continue;
            int cell = L.Furniture[fu].Cells[0];
            int d = Manhattan(a.C.Cell, cell) + 12 * WashLine(a, fu, g) + (int)(60 * DreadAt(a, cell));
            if (d < bd) { bd = d; best = fu; }
        }
        return best;
    }

    /// <summary>화장실 · 씻기의 선택지 (사람).</summary>
    private void WashOptions(Agent a, int here, double hour)
    {
        if (_life == null) return;
        ref var c = ref a.C;
        ref var b = ref _life[c.Body];
        // 마려움: 반쯤 차면 가고 싶고 · 찰수록 급하다 (0.85면 끼니 · 잠보다 · 0.95면 웬만한 일보다)
        float urge = Urge(a);
        // 변기가 없는 배: 하던 일을 끊지 않고 그 자리에서 용변 봉투 (3분의 2쯤 차면)
        if (_toilets.Length == 0)
        {
            if (urge > 0.67f && c.Up != 0) { _life.Void(c.Body); c.ToiletAt = _nowTick; Bags++; }
        }
        else if (urge > 0.5f)
        {
            float s = 1 + 25 * (urge - 0.5f) * (urge - 0.5f);
            int fu = PickWash(a, _toilets, GoalKind.Toilet, ToiletOk);
            if (fu >= 0 || urge > 0.9f) Add(GoalKind.Toilet, fu, fu >= 0 ? L.Furniture[fu].Cells[0] : here, s);   // 변기가 없는 배 · 쓸 변기가 없는데 참지 못하겠으면 용변 봉투
        }
        // 씻기: 더러울수록 · 지렸으면 바로 · 땀 흘린 운동 뒤 · 자기 전 (성실한 이) — 일하는 시간엔 아주 더럽거나 지렸을 때만
        float dirt = b.Dirt;
        bool work = InWindow(hour, c.ShiftHour, 8);
        bool fresh = c.ShoweredAt > 0 && _nowTick - c.ShoweredAt < Time.Hours(8);
        if (_showers.Length == 0)   // 샤워 칸이 없는 배: 몹시 더러워지면 빈 시간에 그 자리에서 물수건으로
        {
            if (dirt > 0.6f && !work && c.Up != 0) { _life.Wipe(c.Body); Wipes++; }
        }
        else if ((dirt > 0.35f || b.Wet != 0) && (!work || dirt > 0.85f || b.Wet != 0) && !fresh)
        {
            float s = dirt * dirt * 3 + (b.Wet != 0 ? 3 : 0)
                    + (c.ExercisedAt > 0 && _nowTick - c.ExercisedAt < Time.Hours(1) ? 0.8f : 0)
                    + (InWindow(hour - c.Chrono, c.ShiftHour + 15, 2) ? 0.4f * c.Consc : 0);   // 잠자리에 들기 전
            bool floating = c.Floated != 0;   // 무중력: 물이 떠다녀 샤워를 못 쓴다
            int fu = floating ? -1 : PickWash(a, _showers, GoalKind.Wash, f => _net == null || _net.WaterTank > ShowerWater * 2);
            Add(GoalKind.Wash, fu, fu >= 0 ? L.Furniture[fu].Cells[0] : c.Bed >= 0 ? c.Bed : here, s);
        }
    }

    /// <summary>화장실 계획: 줄 → 들어가 비운다 → (성실할수록) 손을 씻는다 · 변기가 없으면 그 자리에서 용변 봉투.</summary>
    private bool PlanToilet(Sim sim, Agent a, Option o, List<PlanStep> plan)
    {
        if (o.Target < 0)
        {
            plan.Add(Use(UseKind.Bag, Time.Minutes(2), a.C.Cell, -1));
            return true;
        }
        int cell = L.Furniture[o.Target].Cells[0];
        bool bowel = _life![a.C.Body].Bowel > Life.BowelMax * 0.4;
        plan.Add(Go(cell, 1));
        plan.Add(Use(UseKind.Queue, QueueMax, cell, o.Target));
        plan.Add(Go(cell, 0));
        plan.Add(Use(UseKind.Toilet, bowel ? Time.Minutes(6) : Time.Minutes(2), cell, o.Target));
        if (StepOff(cell) is int off && off >= 0) plan.Add(Go(off, 0));   // 다 쓰면 비켜선다 (다음 사람이 들어오게)
        // 손 씻기: 성실할수록 늘 (0.35 ~ 0.95) — 가까운 세면대 (20칸 안)
        int sink = Nearest(cell, _sinks, f => Manhattan(cell, L.Furniture[f].Cells[0]) <= 20);
        if (sink >= 0 && Rng.Unit(sim.Seed, Rng.Key("손 씻기"), a.Id, sim.Tick) < 0.35 + 0.6 * a.C.Consc)
        {
            int sc = L.Furniture[sink].Cells[0];
            plan.Add(Go(sc, 1));
            plan.Add(Use(UseKind.Hands, Time.Minutes(0.5), sc, sink));
        }
        return true;
    }

    /// <summary>씻기 계획: 줄 → 샤워 칸 → 씻는다 · 샤워 칸이 없거나 떠 있으면 제 침대에서 물수건.</summary>
    private bool PlanWash(Sim sim, Agent a, Option o, List<PlanStep> plan)
    {
        if (o.Target < 0)
        {
            int bed = a.C.Bed >= 0 && a.C.Floated == 0 ? a.C.Bed : a.C.Cell;
            plan.Add(Go(bed, 1));
            plan.Add(Use(UseKind.Wipe, Time.Minutes(5), bed, -1));
            return true;
        }
        int cell = L.Furniture[o.Target].Cells[0];
        plan.Add(Go(cell, 1));
        plan.Add(Use(UseKind.Queue, QueueMax, cell, o.Target));
        plan.Add(Go(cell, 0));
        plan.Add(Use(UseKind.Shower, Time.Minutes(6), cell, o.Target));
        if (StepOff(cell) is int off && off >= 0) plan.Add(Go(off, 0));
        return true;
    }

    /// <summary>설비 곁의 빈 바닥 칸 (다 쓰고 비켜설 자리 — 없으면 -1).</summary>
    private int StepOff(int cell)
    {
        int x = L.X(cell), y = L.Y(cell);
        foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (-1, 1), (1, -1), (-1, -1) })
        {
            if (!L.In(x + dx, y + dy)) continue;
            int j = L.Index(x + dx, y + dy);
            if (L.Kind[j] == CellKind.Floor && L.FurnitureAt[j] < 0) return j;
        }
        return -1;
    }

    /// <summary>줄 서기 한 틱: 앞선 이가 없으면 들어간다 · 너무 길면 돌아선다 (그 변기는 한동안 멀게 친다).</summary>
    private void QueueStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        if (!WashAhead(a, st.Target, c.Goal)) { Next(a, t); return; }
        if (t == c.StepStart) Queued++;
        if (Urge(a) > 0.8f) c.Stress = Math.Min(1, c.Stress + 0.002f);   // 급한데 기다리면 애가 탄다
        if (t - c.StepStart >= st.Value)
        {
            QueueGaveUp++;
            Fail(sim, a, c.Goal == GoalKind.Toilet ? "화장실 줄이 너무 길다" : "샤워 줄이 너무 길다", SnagKind.Other, st.Target);
        }
    }

    /// <summary>화장실 · 씻기 쓰기가 끝났다 (월드에 요청 — 몸 · 물은 ⑦ · 망이 셈한다).</summary>
    private void WashDone(Sim sim, Agent a, PlanStep st, UseKind use)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        switch (use)
        {
            case UseKind.Toilet:
            case UseKind.Bag:
                _life!.Void(c.Body);
                c.ToiletAt = t;
                if (use == UseKind.Toilet) Toilets++; else Bags++;
                break;
            case UseKind.Shower:
            {
                int d = WashDev(st.Target);
                bool hot = d < 0 || _net!.Working(d);
                if (d >= 0) _net!.SetOn(d, false);
                double got = _net != null ? Math.Min(_net.WaterTank, ShowerWater) : ShowerWater;
                if (_net != null) { _net.WaterTank -= got; sim.Book.Add(Res.Water, Flow.Washed, -got); }
                _life!.Wash(sim, c.Body, got, hot ? 313 : 291, st.Cell);
                c.ShoweredAt = t; ShowersTaken++;
                c.Stress = Math.Max(0, c.Stress - (hot ? 0.15f : 0.05f));
                c.Joy = Math.Min(1, c.Joy + (hot ? 0.12f : 0.02f));
                if (!hot) c.Anger = Math.Min(1, c.Anger + 0.05f);   // 찬물 샤워
                break;
            }
            case UseKind.Hands:
            {
                double got = _net != null ? Math.Min(_net.WaterTank, HandWater) : HandWater;
                if (_net != null) { _net.WaterTank -= got; sim.Book.Add(Res.Water, Flow.Washed, -got); }
                _life!.Wash(sim, c.Body, got, 300, -1);
                c.HandsAt = t; HandWashes++;
                break;
            }
            case UseKind.Wipe:
                _life!.Wipe(c.Body);
                Wipes++;
                break;
        }
    }

    /// <summary>샤워 칸을 쓰는 동안: 처음에 온수기를 켠다 (전기 3kW — 꺼지면 찬물).</summary>
    private void ShowerStart(PlanStep st)
    {
        int d = WashDev(st.Target);
        if (d >= 0 && _net!.On[d] == 0) _net.SetOn(d, true);
    }
}
