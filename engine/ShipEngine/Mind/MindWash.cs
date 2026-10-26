namespace ShipEngine;

/// <summary>
/// 화장실 · 샤워실의 판단 (⑧ — docs/WASH.md): 마려우면 가장 가까운 빈 변기로 (차 있으면 줄을 서고 · 너무 길면 다른 화장실로) ·
/// 더러우면 · 운동 뒤 · 자기 전에 씻는다 · 변기 뒤엔 손을 씻는다 (성실할수록). 변기가 없는 배에선 용변 봉투 · 샤워 칸이 없으면 물수건.
/// 몸이 차오르고 · 지리고 · 물이 오수로 가는 것은 월드(⑦ LifeWash)가 셈한다 — 여기서는 언제 · 어디로 가나만.
/// </summary>
public sealed partial class Mind
{
    private int[] _toilets = Array.Empty<int>(), _showers = Array.Empty<int>(), _sinks = Array.Empty<int>(), _dumps = Array.Empty<int>();
    private int[]? _washDev;   // 설비 → 망의 장치 (-1 없음)
    /// <summary>재기용: 변기에 비운 번 · 용변 봉투 · 샤워 · 물수건 · 손 씻기 · 줄을 선 번 · 줄이 길어 다른 곳으로 간 번.</summary>
    public long Toilets, Bags, ShowersTaken, Wipes, HandWashes, Queued, QueueGaveUp;
    /// <summary>(다) 재기용: 샤워 배급을 넘겨 다 쓴 번 · 주컴퓨터의 안내로 다른 화장실로 간 번 · 가 보니 더럽거나 '사용 금지'라 돌아선 번 · 변기를 닦은 · 뚫은 · 오물 통을 비운 번.</summary>
    public long ShowerOverRule, WashMoved, WashTurnedAway, Scrubbed, Unclogged, Emptied, ShowersLeftOn;
    /// <summary>사람마다 지금 틀어 둔 샤워 칸 (장치 + 1 · 0 없음) · 틀 때의 계량기.</summary>
    private int[] _showerDev = Array.Empty<int>();
    private double[] _showerFrom = Array.Empty<double>();

    // ── (다) 주컴퓨터의 요청 (⑩이 정하고 사람은 제 판단으로 따른다) ──
    /// <summary>샤워 배급 (0분이면 없다): 한 번 몇 분 · 다음 샤워까지 · 연 원인 · 연 때.</summary>
    public int ShowerRuleMin, ShowerRuleCause;
    public long ShowerRuleGap, ShowerRuleSince;
    private ulong _washSeed;
    /// <summary>변기마다 '사용 금지' 불 (주컴퓨터가 켠다 — 화장실 화면과 변기의 불로 모두 안다).</summary>
    public byte[] WashSign = Array.Empty<byte>();
    /// <summary>사람마다 주컴퓨터의 줄 안내: 들은 때 · 가 보라는 설비(−1이면 차례만) · 차례(1부터 · 0이면 없음).</summary>
    private long[] _callTick = Array.Empty<long>();
    private int[] _callTo = Array.Empty<int>(), _callOrder = Array.Empty<int>();

    // ── 5단 (라)(마): 냄새 · 줄 · 손 씻기 (사람 수에 맞춰 는다 — _callTick 꼴) ──
    /// <summary>사람마다 맡은 이 넷 (사람 × <see cref="SmellSlots"/>): 누구 · 맡은 때 (0이면 빈 칸) · 원인 마디 · 그때 그 사람의 칸.</summary>
    public const int SmellSlots = 4;
    private int[] _smellWho = Array.Empty<int>(), _smellCause = Array.Empty<int>(), _smellCell = Array.Empty<int>();
    private long[] _smellAt = Array.Empty<long>();
    /// <summary>사람마다 '냄새가 난다'는 말을 들은 때 (0 없음).</summary>
    private long[] _toldOdorAt = Array.Empty<long>();
    /// <summary>사람마다 줄 기억: 줄을 선 설비 · 제 줄 걸음을 시작한 때 · 먼저 드는 열쇠 · 부탁한 줄 · 투덜한 줄 (그 줄 걸음을 시작한 때 — 줄 하나에 한 번).</summary>
    private int[] _lineFu = Array.Empty<int>();
    private long[] _lineStart = Array.Empty<long>(), _lineKey = Array.Empty<long>(), _lineAsked = Array.Empty<long>(), _lineGrumbled = Array.Empty<long>();
    /// <summary>사람마다 '손을 씻어 주세요' 방송을 들은 때 (0 없음 — 들은 이만).</summary>
    public long[] HandRuleAt = Array.Empty<long>();
    /// <summary>샤워 칸 · 화장실 알림 (사람이 배 안 통신으로 — 주컴퓨터가 읽는다 · 많아야 16).</summary>
    public readonly List<WashReport> WashReports = new();
    /// <summary>재기용: 냄새 나는 이 곁을 피한 번 · 샤워 투덜 · 줄 새치기 · 차례를 부탁한 번 · 비켜 준 번.</summary>
    public long SmellAvoided, Grumbles, LineCuts, AskedTurns, TurnsGiven;
    /// <summary>오물을 비울 곳의 차례: 퇴비통 · 압축기 — 둘 다 없으면 화물 상자 (봉해 쌓아 둔다).</summary>
    private static readonly string[] DumpTypes = { "Composter", "Compactor" };

    /// <summary>줄을 서서 기다리는 가장 긴 시간 — 넘으면 다른 화장실로 (막히면 다른 길).</summary>
    public static readonly long QueueMax = Time.Minutes(15);
    public const double ShowerWater = Life.ShowerMol, HandWater = 28;   // 샤워 8L · 손 씻기 0.5L

    private void WashSetup()
    {
        int[] Furn(string type) => L.Furniture.Where(fu => fu.Cells.Length > 0 && fu.Type == type).Select(fu => fu.Id).ToArray();
        _toilets = Furn("Toilet"); _showers = Furn("Shower"); _sinks = Furn("Sink");
        _dumps = L.Furniture.Where(fu => fu.Cells.Length > 0 && DumpTypes.Contains(fu.Type)).Select(fu => fu.Id).ToArray();
        if (_dumps.Length == 0) _dumps = Furn("CargoCrate");
        WashSign = new byte[L.Furniture.Count];
    }

    // ───────────────────────── (다) 주컴퓨터의 요청 ─────────────────────────

    /// <summary>⑩이 요청: 샤워 배급 (minutes 0이면 푼다) — 지킬지는 사람마다 한 번 (성실할수록).</summary>
    public void SetShowerRule(Sim sim, int minutes, long gap, int cause)
    {
        if (minutes == ShowerRuleMin) return;
        ShowerRuleMin = minutes; ShowerRuleGap = gap; ShowerRuleCause = cause; ShowerRuleSince = sim.Tick; _washSeed = sim.Seed;
    }

    /// <summary>⑩이 요청: 변기의 '사용 금지' 불을 켜고 끈다.</summary>
    public void SignToilet(int fu, bool on) => SignWash(fu, 0, on);
    /// <summary>
    /// ⑩이 요청: 변기 · 샤워 칸의 '사용 금지' 불 — 까닭마다 비트 (0 막힘 · 가득 / 1 조절기 알림 / 2 무중력 · 5단 C23). 하나라도 켜져 있으면 불이 켜져 있다 (사람은 불만 본다).
    /// </summary>
    public void SignWash(int fu, int bit, bool on) { if (fu >= 0 && fu < WashSign.Length && bit is >= 0 and < 8) WashSign[fu] = (byte)(on ? WashSign[fu] | (1 << bit) : WashSign[fu] & ~(1 << bit)); }
    /// <summary>그 설비에 '사용 금지' 불이 켜져 있나 (눈 — 까닭은 모른다).</summary>
    public bool Signed(int fu) => fu >= 0 && fu < WashSign.Length && WashSign[fu] != 0;

    /// <summary>⑩이 요청: 줄에 선 이에게 안내 — 저 설비가 비었다 (to) · 또는 당신 차례는 몇 번째 (order).</summary>
    public void CallWash(long tick, int agent, int to, int order)
    {
        if (_callTick.Length < Agents.Count) { Array.Resize(ref _callTick, Agents.Count); Array.Resize(ref _callTo, Agents.Count); Array.Resize(ref _callOrder, Agents.Count); }
        if (agent < 0 || agent >= _callTick.Length) return;
        _callTick[agent] = tick; _callTo[agent] = to; _callOrder[agent] = order;
    }

    /// <summary>그 사람이 since 뒤에 들은 안내 (없으면 false).</summary>
    private bool Called(Agent a, long since, out int to, out int order)
    {
        to = -1; order = 0;
        if (a.Id >= _callTick.Length || _callTick[a.Id] <= 0 || _callTick[a.Id] < since) return false;
        to = _callTo[a.Id]; order = _callOrder[a.Id];
        return true;
    }

    /// <summary>샤워 배급을 지키는 이인가 (배급이 열릴 때마다 한 번 · 성실할수록 — 0.05면 넷에 하나 · 0.95면 거의 늘).</summary>
    private bool KeepsShowerRule(Agent a) => ShowerRuleMin > 0 && Rng.Unit(_washSeed, Rng.Key("샤워 배급"), a.Id, ShowerRuleSince) < 0.2 + 0.8 * a.C.Consc;

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

    /// <summary>마려움 (0~1 — 1이면 참지 못한다 · 몸이 안다 — ⑦ Life.Urge가 셈한다).</summary>
    public float Urge(Agent a) => !a.IsPerson || _life == null ? 0 : _life.Urge(a.C.Body);

    /// <summary>변기를 쓸 수 있나 (흡입 팬이 멀쩡하고 전기가 들어온다 — 고장 등이 켜져 있으면 안다 · 주컴퓨터가 '사용 금지' 불을 켜지 않았다).</summary>
    private bool ToiletOk(int fu) { int d = WashDev(fu); return (d < 0 || _net!.Working(d)) && (fu >= WashSign.Length || WashSign[fu] == 0); }

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
            int d = PathDist(a.C.Cell, cell) + 12 * WashLine(a, fu, g) + (int)(60 * DreadAt(a, cell));   // 길 거리 (벽 너머 가까운 곳은 멀다)
            if (Called(a, _nowTick - Time.Minutes(10), out int to, out _) && to == fu) d -= 40;   // 주컴퓨터가 비었다고 알려 준 곳
            if (a.C.Goal == g && a.C.GoalTarget == fu) d = d * 7 / 10;   // 고른 곳을 지킨다 (3할 여유 — 두 곳 사이를 오가지 않게)
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
        bool keep = KeepsShowerRule(a);   // (다) 샤워 배급을 지키는 이는 이틀에 한 번
        bool fresh = c.ShoweredAt > 0 && _nowTick - c.ShoweredAt < (keep ? ShowerRuleGap : Time.Hours(8));
        if (_showers.Length == 0)   // 샤워 칸이 없는 배: 몹시 더러워지면 빈 시간에 · 지렸으면 일하는 때에도 그 자리에서 물수건으로 (지린 이는 늘 씻는다 — 5단 C6)
        {
            if ((dirt > 0.6f && !work || b.Wet != 0) && c.Up != 0) { _life.Wipe(c.Body); Wipes++; }
        }
        else if ((dirt > 0.35f || b.Wet != 0) && (!work || dirt > 0.85f || b.Wet != 0) && (!fresh || b.Wet != 0))   // 지린 이는 늘 씻는다 (배급 중에도 · 방금 씻었어도)
        {
            float s = dirt * dirt * 3 + (b.Wet != 0 ? 3 : 0)
                    + (c.ExercisedAt > 0 && _nowTick - c.ExercisedAt < Time.Hours(1) ? 0.8f : 0)
                    + (InWindow(hour - c.Chrono, c.ShiftHour + 15, 2) ? 0.4f * c.Consc : 0);   // 잠자리에 들기 전
            bool floating = c.Floated != 0;   // 무중력: 물이 떠다녀 샤워를 못 쓴다
            int fu = floating ? -1 : PickWash(a, _showers, GoalKind.Wash, f => !Signed(f) && (_net == null || _net.WaterTank > ShowerWater * 2));   // 사용 금지 불이 켜진 칸은 고르지 않는다 (5단 C23)
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
        plan.Add(QueueSpot(a, o.Target) is int qs && qs >= 0 ? Go(qs, 0) : Go(cell, 1));   // 줄 서는 자리 — 문 칸 · 문 곁이 아닌 곳 (5단 C5)
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
        plan.Add(QueueSpot(a, o.Target) is int qs && qs >= 0 ? Go(qs, 0) : Go(cell, 1));   // 줄 서는 자리 (5단 C5)
        plan.Add(Use(UseKind.Queue, QueueMax, cell, o.Target));
        plan.Add(Go(cell, 0));
        plan.Add(Use(UseKind.Shower, KeepsShowerRule(a) ? Time.Minutes(ShowerRuleMin) : Time.Minutes(6), cell, o.Target));   // (다) 배급을 지키는 이는 짧게
        if (StepOff(cell) is int off && off >= 0) plan.Add(Go(off, 0));
        return true;
    }

    private static readonly (int dx, int dy)[] StepOffDirs = { (0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (-1, 1), (1, -1), (-1, -1) };

    /// <summary>설비 곁의 빈 바닥 칸 (다 쓰고 비켜설 자리 — 표 0 칸을 먼저 · 문 곁밖에 없으면 그 칸 · 없으면 -1 — 5단 C5).</summary>
    private int StepOff(int cell)
    {
        int x = L.X(cell), y = L.Y(cell), first = -1;
        foreach (var (dx, dy) in StepOffDirs)
        {
            if (!L.In(x + dx, y + dy)) continue;
            int j = L.Index(x + dx, y + dy);
            if (L.Kind[j] != CellKind.Floor || L.FurnitureAt[j] >= 0) continue;
            if (StandKindAt(j) == 0) return j;
            if (first < 0) first = j;
        }
        return first;
    }

    /// <summary>줄 서기 한 틱: 앞선 이가 없으면 들어간다 · 너무 길면 돌아선다 (그 변기는 한동안 멀게 친다).</summary>
    private void QueueStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        // (다) 와 보니: '사용 금지' 불이 켜졌다 · 넘쳐 몹시 더럽다 (몹시 급하면 더러워도 쓴다) → 다른 변기로
        if (Signed(st.Target)) { WashRedirect(a, st.Target, t, c.Goal == GoalKind.Toilet ? "변기에 사용 금지 불이 켜져 있다" : "샤워 칸에 사용 금지 불이 켜져 있다"); WashTurnedAway++; return; }   // 샤워 칸 불도 (5단 C23)
        if (c.Goal == GoalKind.Toilet && _life != null)
        {
            if (st.Target < _life.Grime.Length && _life.Grime[st.Target] >= 0.85f && Urge(a) < 0.9f) { WashRedirect(a, st.Target, t, "변기가 넘쳐 몹시 더럽다"); WashTurnedAway++; return; }
        }
        bool told = Called(a, c.StepStart - Time.Minutes(3), out _, out int order);   // 줄에 서기 전 · 걸어오는 동안 · 줄에 선 동안 들은 것 (줄에 선 내내 기억한다)
        if (!WashAhead(a, st.Target, c.Goal)) { Next(a, t); return; }
        if (t == c.StepStart) Queued++;
        bool turn = told && order > 0;   // 차례를 들은 이는 제 차례가 오는 줄 안다 — 덜 애태우고 더 오래 기다린다
        if (Urge(a) > 0.8f) c.Stress = Math.Min(1, c.Stress + (turn ? 0.001f : 0.002f));   // 급한데 기다리면 애가 탄다
        if (t - c.StepStart >= st.Value * (turn ? 2 : 1))
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
                _life!.Flush(sim, c.Body, st.Target, st.Cell);   // 월드: 막혔으면 넘친다 · 아니면 오수 · 오물 통으로
                c.ToiletAt = t; Toilets++;
                CountWashMoved(a, st.Target);
                break;
            case UseKind.Bag:
                _life!.Void(c.Body);
                c.ToiletAt = t; Bags++;
                break;
            case UseKind.Shower:
            {
                int d = WashDev(st.Target);
                bool hot = d < 0 || _net!.Working(d);
                if (d >= 0)
                {
                    double got = ShowerTaken(a, d);   // 흐른 물 (계량기의 차)만큼 씻겼다 (6분에 8L · 배급이면 3분에 4L)
                    _life!.Rinse(sim, c.Body, got, d);   // 물 온도 · 데임은 월드가 (5단 C17) — 잠그기 전의 물로 (검토 고침: 온수기를 끈 뒤에 읽으면 늘 찬물이었다)
                    ShowerOff(a, d);   // 잠근다
                    if (got < Network.ShowerFlow * 30)   // 물이 나오지 않았다 (물관이 끊겼다 · 물탱크가 비었다) — 씻은 것이 아니다: 이 샤워 칸은 한 시간 쓰지 않는다 (막히면 다른 길 — 물수건)
                    {
                        a.Avoid.Add(new AvoidEntry { Goal = GoalKind.Wash, Target = st.Target, Until = t + Time.Hours(1) });
                        if (a.Avoid.Count > 16) a.Avoid.RemoveAt(0);
                        a.LastFail = "Wash: 샤워 칸에서 물이 나오지 않는다";
                        Jot(a, new Judgment { Tick = t, Goal = c.Goal, Target = st.Target, Cell = c.Cell, Method = 255, Held = c.Holding, Out = JudgeOut.Blocked, Why = SnagKind.NoWater, By = -1, Note = "샤워 칸에서 물이 나오지 않는다" });   // 판단 일지 (쓰기만)
                        c.Anger = Math.Min(1, c.Anger + 0.05f);
                        break;
                    }
                }
                else   // 망이 없는 샤워 칸 (시험 배): 물을 직접 쓴다
                {
                    _life!.WashFromTap(sim, c.Body, ShowerWater * st.Value / Time.Minutes(6), 313, st.Cell);   // 물은 월드가 탱크에서 꺼낸다 (요청)
                }
                c.ShoweredAt = t; ShowersTaken++;
                CountWashMoved(a, st.Target);
                if (ShowerRuleMin > 0 && st.Value > Time.Minutes(ShowerRuleMin)) ShowerOverRule++;   // 배급을 넘겨 다 썼다 ((라)의 불만 씨앗)
                c.Stress = Math.Max(0, c.Stress - (hot ? 0.15f : 0.05f));
                c.Joy = Math.Min(1, c.Joy + (hot ? 0.12f : 0.02f));
                if (!hot) c.Anger = Math.Min(1, c.Anger + 0.05f);   // 찬물 샤워
                break;
            }
            case UseKind.Hands:
            {
                if (_net != null && !_net.WaterAt(st.Target)) break;   // 물관이 끊겼다 — 물이 나오지 않는다 (씻지 못했다)
                _life!.HandWash(sim, c.Body, HandWater, st.Target >= 0 && st.Target < L.Furniture.Count ? L.Furniture[st.Target].Cells[0] : -1);   // 손 씻는 물 (월드가 탱크에서 꺼내고 · 세면대 칸에서 쏴)
                c.HandsAt = t; HandWashes++;
                break;
            }
            case UseKind.Wipe:
                _life!.Wipe(c.Body);
                Wipes++;
                break;
        }
    }

    /// <summary>
    /// (다) 주컴퓨터가 다른 화장실이 비었다고 알려 줬다 (변기 · 샤워 칸에 들기 전 — 걸어오는 중이든 줄에 섰든): 그쪽이 쓸 만하고 멀지 않으면 옮긴다
    /// (기다리는 것보다 걷는 게 빠르다) — 안내 하나에 한 번만 (옮겨 가서 고른 곳이 또 다르면 다시 돌아서지 않는다).
    /// </summary>
    private void HeedWashCall(Agent a, long t)
    {
        ref var c = ref a.C;
        if (!a.IsPerson || c.Up == 0 || c.Mode != 0 || c.Goal is not (GoalKind.Toilet or GoalKind.Wash) || c.GoalTarget < 0 || a.Id >= _callOrder.Length || _callOrder[a.Id] < 0) return;
        if (!Called(a, t - Time.Minutes(3), out int to, out _) || to < 0 || to == c.GoalTarget || to >= L.Furniture.Count) return;
        int q = a.Plan.FindIndex(st => st.Kind == StepKind.Use && (UseKind)st.Arg == UseKind.Queue);
        if (q < 0 || c.StepIndex > q) return;   // 벌써 들어갔다
        if (L.Furniture[to].Type != (c.Goal == GoalKind.Toilet ? "Toilet" : "Shower") || c.Goal == GoalKind.Toilet && !ToiletOk(to) || Signed(to) || Manhattan(c.Cell, L.Furniture[to].Cells[0]) > 80) return;   // 기다리는 갈래가 아니다 · 쓸 수 없다 · 멀다
        foreach (var v in a.Avoid) if (v.Goal == c.Goal && v.Target == to) return;   // 내가 피하기로 한 곳
        _callOrder[a.Id] = -1;   // 이 안내로 한 번 돌아섰다
        WashRedirect(a, c.GoalTarget, t, "주컴퓨터가 다른 화장실이 비었다고 한다");
    }

    /// <summary>안내받은 곳에서 썼다 (재기용 — 판단이 줄 서기 전에 바로 그리로 바꿨든 · 줄에서 돌아섰든).</summary>
    private void CountWashMoved(Agent a, int fu)
    {
        if (!Called(a, _nowTick - Time.Minutes(15), out int to, out _) || to != fu) return;
        WashMoved++;
        _callTo[a.Id] = -1;
    }

    /// <summary>(다) 다른 변기 · 샤워 칸으로: 이것은 한동안(10분) 고르지 않는다 — 막힘이 아니라 제 판단으로 돌아선 것 (짜증 · 궁리 없이).</summary>
    private void WashRedirect(Agent a, int fu, long t, string why)
    {
        ref var c = ref a.C;
        a.Avoid.Add(new AvoidEntry { Goal = c.Goal, Target = fu, Until = t + Time.Minutes(10) });
        if (a.Avoid.Count > 16) a.Avoid.RemoveAt(0);
        a.LastFail = $"{c.Goal}: {why}";
        Jot(a, new Judgment { Tick = t, Goal = c.Goal, Target = fu, Cell = c.Cell, Method = 255, Held = c.Holding, Out = JudgeOut.Turned, By = -1, Note = why });   // 판단 일지 (쓰기만 — 막힘이 아니라 제 판단으로 돌아섰다)
        EndPlan(a);
        c.Goal = GoalKind.Idle;
    }

    // ───────────────────────── (다) 변기 손질 (맡긴 일 — 로봇 먼저) ─────────────────────────

    /// <summary>일이 끊겨 내려놓은 오물 통 (물체 Id — 내려놓은 이가 그 통을 주컴퓨터에 알린다 · 주컴퓨터는 이것만 안다).</summary>
    public readonly List<int> WasteDown = new();

    /// <summary>오물을 비울 가장 가까운 곳 (퇴비통 · 압축기 — 없으면 화물 상자 · −1 없음).</summary>
    private int WasteDump(int cell) => Nearest(cell, _dumps, _ => true);

    /// <summary>
    /// 변기 손질 계획 (Arg 0 닦기 · 1 뚫기 · 2 오물 통 비우기 · 3 내려놓인 통 주워 비우기): 변기로 가서 손질 — 통 비우기는 꽉 찬 통을 곁의 빈 통과 바꿔 들고 비울 곳으로 ·
    /// 오물 통을 들고 있으면 (하던 일이 끊겼어도) 비울 곳으로.
    /// </summary>
    private bool PlanToiletCare(Sim sim, Agent a, Option o, List<PlanStep> plan)
    {
        ref var c = ref a.C;
        if (_life == null) return false;
        if (c.Holding == iWaste)
        {
            int dump = WasteDump(c.Cell);
            if (dump < 0) return false;
            plan.Add(Go(L.Furniture[dump].Cells[0], 1));
            plan.Add(new PlanStep { Kind = StepKind.Sanitize, Target = dump, Arg = 3 });
            return true;
        }
        if (c.AssignedGoal == GoalKind.ToiletCare && c.AssignedArg == 3)   // 바닥에 내려놓인 오물 통 (대상: 그 통의 물체 Id) — 주워 비울 곳으로
        {
            int id = c.AssignedTarget, at = LooseCell(id), dump = at < 0 ? -1 : WasteDump(at);
            if (at < 0 || dump < 0 || _st!.Props[_st.IndexOf(id)].What != iWaste) return false;
            plan.Add(Go(at, 1));
            plan.Add(new PlanStep { Kind = StepKind.Sanitize, Cell = at, Target = id, Arg = 4 });
            plan.Add(Go(L.Furniture[dump].Cells[0], 1));
            plan.Add(new PlanStep { Kind = StepKind.Sanitize, Target = dump, Arg = 3 });
            return true;
        }
        int fu = o.Target;
        if (fu < 0 || fu >= L.Furniture.Count || L.Furniture[fu].Cells.Length == 0) return false;   // 든 것(불 끄고 남은 소화기 …)이 있어도 간다 — 통을 들 때만 곁에 내려놓는다 (찾아 고친 결함: 소화기를 든 로봇이 맡은 손질을 끝내 못 했다)
        int kind = c.AssignedGoal == GoalKind.ToiletCare && c.AssignedTarget == fu ? Math.Clamp(c.AssignedArg, 0, 2) : 0;
        int cell = L.Furniture[fu].Cells[0];
        plan.Add(Go(cell, 1));
        plan.Add(new PlanStep { Kind = StepKind.Sanitize, Target = fu, Arg = (byte)kind });
        if (kind == 2)
        {
            int dump = WasteDump(cell);
            if (dump < 0) return false;   // 비울 곳이 없다 — 통을 들지 않는다
            plan.Add(Go(L.Furniture[dump].Cells[0], 1));
            plan.Add(new PlanStep { Kind = StepKind.Sanitize, Target = dump, Arg = 3 });
        }
        return true;
    }

    /// <summary>변기 손질 한 틱: 닦기 5분 (때 · 둘레 고인 물) · 뚫기 10분 · 통 들기 3분 · 쏟기 3분 — 다 하면 월드에 요청한다.</summary>
    private void SanitizeStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        if (_life == null || st.Arg != 4 && (st.Target < 0 || st.Target >= L.Furniture.Count)) { JobDone(a, t); return; }
        if (st.Arg == 4)   // 바닥에 내려놓인 오물 통을 줍는다 (일이 끊겨 내려놓았던 그 통 — 곁 칸에서)
        {
            var book = _st!;
            int at = LooseCell(st.Target);
            if (at >= 0 && !Reach(sim, a, st.Target, at, iWaste)) return;
            WasteDown.Remove(st.Target);   // 가 보았다 — 알린 통은 지운다
            if (at < 0) { a.C.AssignedUntil = t; EndPlan(a); return; }   // 누가 벌써 주웠다 — 쏟기 걸음으로 넘어가지 않는다
            var bin = book.Props[book.IndexOf(st.Target)];
            if (c.Holding != 255) DropHeld(a);
            c.Holding = iWaste; c.HoldCharge = bin.Charge;
            book.TakeItem(bin.Id);
            Next(a, t);
            return;
        }
        int cell = L.Furniture[st.Target].Cells[0];
        if (Dist(c.Cell, cell) > 2) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        Activity(a, "work");
        if (t - c.StepStart < st.Arg switch { 0 => Time.Minutes(5), 1 => Time.Minutes(10), _ => Time.Minutes(3) }) return;
        switch (st.Arg)
        {
            case 0:
            case 1:
            {
                if (st.Arg == 1)
                {
                    int was = _life.ClogCause[st.Target];
                    _life.Unclog(st.Target);
                    if (_life.Clogged[st.Target] == 0)
                    {
                        Unclogged++;
                        sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{NameOf(a.Id)}가 막힌 변기를 뚫었다", t, cell, was, actor: a.Id);
                    }
                }
                else Scrubbed++;
                _life.Scrub(st.Target);   // 뚫고 나면 넘친 자리도 닦는다 (넘친 변기의 때가 남아 다들 돌아서던 것)
                // 둘레에 고인 물(넘친 오줌 · 튄 물)도 닦아 오수로
                int x0 = L.X(cell), y0 = L.Y(cell);
                for (int y = Math.Max(0, y0 - 1); y <= Math.Min(L.H - 1, y0 + 1); y++)
                    for (int x = Math.Max(0, x0 - 1); x <= Math.Min(L.W - 1, x0 + 1); x++)
                    {
                        int k = L.Index(x, y);
                        double w = F.Stored(T.sWater, k);
                        if (w > 0 && _net != null) _net.WasteWater += F.TakeStore(k, T.sWater, w);
                    }
                JobDone(a, t);
                return;
            }
            case 2:
                if (c.Holding != 255) DropHeld(a);   // 들고 있던 것은 변기 곁에 내려놓는다
                c.HoldCharge = (float)_life.LiftTank(st.Target);   // 꽉 찬 통을 들고 빈 통을 끼운다
                c.Holding = iWaste;
                sim.Book.AddItem(iWaste, Flow.Removed, 1);   // 물건 장부: 변기에서 떼어 낸 통 (들고 있는 동안 물건)
                Next(a, t);
                return;
            default:
                if (c.Holding != iWaste) { JobDone(a, t); return; }   // 든 오물 통이 없다 (잃었다) — 아무것도 쏟지 않는다
                _life.Dump(st.Target, c.HoldCharge);
                sim.Book.AddItem(iWaste, Flow.Used, -1);   // 물건 장부: 비운 통은 변기 곁 빈 통 자리로 (든 통이 사라진다)
                c.Holding = 255; c.HoldCharge = 0;
                Emptied++;
                JobDone(a, t);
                return;
        }
    }

    private void SaveWash(StateWriter w)
    {
        w.Tag("화장실 (다)");
        w.I32(ShowerRuleMin); w.I32(ShowerRuleCause); w.I64(ShowerRuleGap); w.I64(ShowerRuleSince); w.U64(_washSeed);
        w.Arr(WashSign); w.Arr(_callTick); w.Arr(_callTo); w.Arr(_callOrder);
        w.I64(Toilets); w.I64(Bags); w.I64(ShowersTaken); w.I64(Wipes); w.I64(HandWashes); w.I64(Queued); w.I64(QueueGaveUp);
        w.I64(ShowerOverRule); w.I64(WashMoved); w.I64(WashTurnedAway); w.I64(Scrubbed); w.I64(Unclogged); w.I64(Emptied); w.I64(ShowersLeftOn);
        w.Arr(_showerDev); w.Arr(_showerFrom); w.Arr(WasteDown.ToArray());
        w.Tag("화장실 (라)(마)");
        w.Arr(_smellWho); w.Arr(_smellAt); w.Arr(_smellCause); w.Arr(_smellCell); w.Arr(_toldOdorAt);
        w.Arr(_lineFu); w.Arr(_lineStart); w.Arr(_lineKey); w.Arr(_lineAsked); w.Arr(_lineGrumbled); w.Arr(HandRuleAt); w.List(WashReports);
        w.I64(SmellAvoided); w.I64(Grumbles); w.I64(LineCuts); w.I64(AskedTurns); w.I64(TurnsGiven);
    }

    private void LoadWash(StateReader r)
    {
        r.Tag("화장실 (다)");
        ShowerRuleMin = r.I32(); ShowerRuleCause = r.I32(); ShowerRuleGap = r.I64(); ShowerRuleSince = r.I64(); _washSeed = r.U64();
        WashSign = r.Arr<byte>(); _callTick = r.Arr<long>(); _callTo = r.Arr<int>(); _callOrder = r.Arr<int>();
        Toilets = r.I64(); Bags = r.I64(); ShowersTaken = r.I64(); Wipes = r.I64(); HandWashes = r.I64(); Queued = r.I64(); QueueGaveUp = r.I64();
        ShowerOverRule = r.I64(); WashMoved = r.I64(); WashTurnedAway = r.I64(); Scrubbed = r.I64(); Unclogged = r.I64(); Emptied = r.I64(); ShowersLeftOn = r.I64();
        _showerDev = r.Arr<int>(); _showerFrom = r.Arr<double>(); WasteDown.Clear(); WasteDown.AddRange(r.Arr<int>());
        r.Tag("화장실 (라)(마)");
        _smellWho = r.Arr<int>(); _smellAt = r.Arr<long>(); _smellCause = r.Arr<int>(); _smellCell = r.Arr<int>(); _toldOdorAt = r.Arr<long>();
        _lineFu = r.Arr<int>(); _lineStart = r.Arr<long>(); _lineKey = r.Arr<long>(); _lineAsked = r.Arr<long>(); _lineGrumbled = r.Arr<long>(); HandRuleAt = r.Arr<long>(); WashReports.Clear(); WashReports.AddRange(r.List<WashReport>());
        SmellAvoided = r.I64(); Grumbles = r.I64(); LineCuts = r.I64(); AskedTurns = r.I64(); TurnsGiven = r.I64();
    }

    /// <summary>샤워 칸을 쓰는 동안: 처음에 온수기를 켠다 (전기 3kW — 꺼지면 찬물).</summary>
    private void ShowerStart(Agent a, PlanStep st)
    {
        int d = WashDev(st.Target);
        if (d < 0) return;
        if (!_net!.ValveOpen(d)) _net.SetValve(d, true);   // 물을 튼다
        if (_net.On[d] == 0) _net.SetOn(d, true);         // 온수기를 켠다 (전력을 줄이려 꺼 두었으면 주컴퓨터가 다시 켤 때까지 찬물)
        if (_showerDev.Length < Agents.Count) { Array.Resize(ref _showerDev, Agents.Count); Array.Resize(ref _showerFrom, Agents.Count); }
        if (_showerDev[a.Id] != d + 1) { _showerDev[a.Id] = d + 1; _showerFrom[a.Id] = d < _net.ShowerMeter.Length ? _net.ShowerMeter[d] : 0; }
    }

    /// <summary>틀 때부터 흐른 물 (계량기의 차 — 몰).</summary>
    private double ShowerTaken(Agent a, int d) => a.Id < _showerDev.Length && _showerDev[a.Id] == d + 1 && d < _net!.ShowerMeter.Length ? Math.Max(0, _net.ShowerMeter[d] - _showerFrom[a.Id]) : 0;

    /// <summary>물을 잠그고 온수기를 끈다 — 흐른 물을 돌려준다.</summary>
    private double ShowerOff(Agent a, int d)
    {
        double got = ShowerTaken(a, d);
        _net!.SetValve(d, false); _net.SetOn(d, false);
        if (a.Id < _showerDev.Length) _showerDev[a.Id] = 0;
        return got;
    }

    /// <summary>
    /// 씻다 말고 나갔나 (계획이 바뀌었다 · 쓰러졌다) — 나가며 물을 잠근다 · 아니면 틀어 둔 채 (덜 성실할수록 · 급하게 뛰쳐나갈수록 · 배운 조심만큼 덜) —
    /// 흐른 만큼은 씻겼다. 틀어 둔 물은 계속 흐른다 (월드) — 주컴퓨터가 빈 샤워 칸을 보면 잠근다.
    /// </summary>
    private void WithdrawShower(Sim sim, Agent a)
    {
        if (a.Id >= _showerDev.Length || _showerDev[a.Id] == 0 || _net == null || _life == null) return;
        ref var c = ref a.C;
        bool still = c.Up != 0 && c.Goal == GoalKind.Wash && c.StepIndex < a.Plan.Count && a.Plan[c.StepIndex].Kind == StepKind.Use && (UseKind)a.Plan[c.StepIndex].Arg == UseKind.Shower;
        if (still) return;
        int d = _showerDev[a.Id] - 1;
        double got = d < _net.ShowerMeter.Length ? Math.Max(0, _net.ShowerMeter[d] - _showerFrom[a.Id]) : 0;
        _showerDev[a.Id] = 0;
        if (a.IsPerson) _life.Rinse(sim, c.Body, got, d);
        if (!_net.ValveOpen(d)) return;
        bool urgent = c.Up == 0 || c.Mode != 0 || c.Goal is GoalKind.Evacuate or GoalKind.Flee or GoalKind.Panic or GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Investigate or GoalKind.GetSuit or GoalKind.Down;
        float p = (0.1f + 0.25f * (1 - c.Consc)) * (urgent ? 1.5f : 0.6f) * (1 - c.Careful);
        if (Rng.Unit(sim.Seed, Rng.Key("샤워 깜빡"), a.Id, sim.Tick) >= p) { _net.SetValve(d, false); _net.SetOn(d, false); return; }
        string why = c.Up == 0 ? "쓰러졌다" : urgent ? "급한 일에 뛰쳐나갔다" : "다른 일에 정신이 팔렸다";
        var dev = _net.Devices[d];
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} 샤워 물을 틀어 둔 채 나갔다 — {why}", sim.Tick, dev.Cell, 0, 0, a.Id);
        _net.OnCause[d] = cause;
        ShowersLeftOn++;   // 사실 '실수'는 내지 않는다 — 크기 1 아래의 실수는 '켜 둔 조리대'로 읽힌다 (조리대 규칙 · 별명) · 틀어 둔 이는 주컴퓨터가 잠근 일(Switched ← 이 원인)로 배운다
    }
}
