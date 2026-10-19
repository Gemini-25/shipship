namespace ShipEngine;

/// <summary>사슬 하나: 한 뿌리(첫 원인)에서 번진 일들 — 어느 엔진들을 어떤 차례로 건넜나.</summary>
public sealed class CauseChain
{
    public int Root;
    public long First, Last;
    public int Engines;            // 건넌 엔진 (비트 1~13)
    public readonly List<byte> Order = new();   // 엔진이 처음 들어온 차례
    public int Facts;              // 이 뿌리에서 나온 무게 있는 일
    public int Depth;              // 가장 긴 사슬
    public int Cause;              // 가장 깊은 무게 있는 일의 원인 (이야기 글)
    public float Weight;           // 무게 합
    public readonly HashSet<int> Links = new();  // 사슬의 마디들 (가지 수를 센다)
    public readonly HashSet<int> Leaves = new(); // 끝 마디 (무게 있는 일이 붙은 마디)
    public int Line;               // 한 줄(뿌리 → 한 일)로 건넌 엔진 수의 최대 — 넓게(파편이 여러 곳)가 아니라 깊게(불 → 연기 → 쓰러짐 → 구함)
    public int LineCause;          // 그 한 줄의 끝 원인
    public string LinePath = "";
    public int Count => System.Numerics.BitOperations.PopCount((uint)Engines);
    public string Path => string.Join(" → ", Order.Select(e => Director.EngineName[e]));
}

/// <summary>
/// ⑬ 연출 — 사슬 기록 · 카메라 성적 (깊이 바퀴 2): 사실마다 원인 사슬을 거슬러 뿌리별로 '건넌 엔진 · 차례 · 가지 · 깊이'를 모은다 —
/// 카메라가 사슬의 앞머리(새 엔진으로 번진 곳)를 따라가는 데 쓰고, 재미 측정기가 '여섯 엔진을 건넌 재난'을 센다.
/// 카메라 성적: 무게 있는 일이 났을 때 이미 화면 안이었나(먼저 와 있음) · 3초 안에 갔나 · 10초 안에 못 봤나(놓침) · 연쇄의 일을 몇이나 봤나.
/// </summary>
public sealed partial class Director
{
    public static readonly string[] EngineName = { "", "기반", "물질", "장", "구조", "바깥", "망", "생명", "마음", "사회", "주컴퓨터", "일·물자", "자연 사건", "연출" };

    /// <summary>화면에 쓰는 갈래 이름 (개발 말 없이).</summary>
    public static readonly string[] PlainName = { "", "", "불·화학", "공기·열", "선체", "우주", "전기·장치", "몸", "사람", "관계", "주컴퓨터", "물자", "", "" };

    /// <summary>이 뿌리의 사슬이 셋 넘는 갈래를 건넜으면 '우주 → 선체 → 사람 → 공기·열' (화면 아래 줄) — 아니면 빈 글.</summary>
    public string SpreadText(int root)
    {
        if (root == 0) return "";
        var rec = Chains.GetValueOrDefault(root) ?? BestChains.FirstOrDefault(c => c.Root == root);
        if (rec == null) return "";
        var names = rec.Order.Select(e => PlainName[e]).Where(n => n.Length > 0).ToList();
        return names.Count >= 3 ? string.Join(" → ", names) : "";
    }

    /// <summary>사실이 나온 엔진 (1~13 · 0 모름).</summary>
    public static int EngineOf(FactKind k) => k switch
    {
        FactKind.Ignition or FactKind.FireOut or FactKind.FireSpread or FactKind.SmokeThick or FactKind.Toxic => 2,
        FactKind.RegionVented or FactKind.PressureDrop or FactKind.Condensation or FactKind.Flood or FactKind.LoudSound => 3,
        FactKind.WallCrack or FactKind.WallBreach or FactKind.Impact or FactKind.DoorJammed or FactKind.DoorBlown or FactKind.ObjectFlung or FactKind.ShipSplit or FactKind.Tumble or FactKind.Crash => 4,
        FactKind.DebrisNear or FactKind.Flare or FactKind.Burn or FactKind.Contact => 5,
        FactKind.Sensed => 6,
        FactKind.BreakerTrip or FactKind.Short or FactKind.Overheat or FactKind.PipeLeak or FactKind.MachineFail or FactKind.SensorCut or FactKind.Jumper or FactKind.CableCut or FactKind.Gravity => 6,
        FactKind.Hurt or FactKind.Collapsed or FactKind.Died or FactKind.Sick or FactKind.Treated => 7,
        FactKind.Noticed or FactKind.Rescue or FactKind.Mistake or FactKind.Warned or FactKind.Panicked or FactKind.Fled or FactKind.Extinguished
            or FactKind.Patched or FactKind.Repaired or FactKind.Blocked or FactKind.Devised or FactKind.DoorSealed or FactKind.Woke or FactKind.WentOutside or FactKind.Switched or FactKind.Fell or FactKind.Salvaged or FactKind.Aim => 8,
        FactKind.Relationship or FactKind.Grieved or FactKind.TrustLost or FactKind.Talk or FactKind.Blamed or FactKind.Fight or FactKind.Sabotage or FactKind.Confessed or FactKind.Lie or FactKind.LieExposed or FactKind.LogAsked or FactKind.Meeting or FactKind.Verdict or FactKind.Elected or FactKind.Mutiny or FactKind.ManualChanged or FactKind.Letter => 9,
        FactKind.Decision or FactKind.ComputerAlarm or FactKind.ComputerSeal or FactKind.ComputerAssign or FactKind.LoadShed or FactKind.BreakerReset
            or FactKind.ComputerDown or FactKind.ComputerJournal or FactKind.ComputerBroadcast or FactKind.Rationed or FactKind.LogShown or FactKind.Shelter or FactKind.MethodWorked or FactKind.MethodFailed => 10,
        FactKind.Pilfer => 11,
        _ => 0,
    };

    /// <summary>원인 마디의 엔진 (갈래가 둘인 것 — 낡음 · 샘 — 은 0: 그 뒤의 사실이 말해 준다).</summary>
    public static int EngineOf(CauseKind k) => k switch
    {
        CauseKind.Corrosion or CauseKind.Fire or CauseKind.Reaction => 2, CauseKind.Pressure or CauseKind.Heat => 3,
        CauseKind.Breach => 4, CauseKind.Impact or CauseKind.Weather or CauseKind.Flare or CauseKind.Route or CauseKind.Contact => 5, CauseKind.Electric or CauseKind.Machine or CauseKind.Sensor => 6,
        CauseKind.Biology => 7, CauseKind.Person or CauseKind.Robot => 8, CauseKind.Society => 9, CauseKind.Computer => 10, CauseKind.Supply => 11, _ => 0,
    };

    public const float ChainFactAt = 5;          // 사슬에 세는 일의 무게
    public const float WatchAt = 20;             // 카메라 성적에 세는 일의 무게
    public const double CaughtSec = 3, MissSec = 10;
    public readonly Dictionary<int, CauseChain> Chains = new();
    /// <summary>끝난 사슬 중 가장 넓은 것들 (엔진 수 · 일 수 순 · 8개).</summary>
    public readonly List<CauseChain> BestChains = new();
    public readonly int[] ChainsByEngines = new int[14];   // 끝난 사슬: 건넌 엔진 수별 개수
    public readonly Dictionary<FactKind, int> Orphans = new();  // 원인 없는 큰 일 (무게 ≥ 20)
    public readonly Dictionary<FactKind, int> MissedKinds = new();
    private readonly List<int> _line = new();
    public int Spreads;                          // 사슬이 새 엔진으로 번진 순간
    // 카메라 성적
    public int Watched, SeenBefore, SeenAfter, Missed, MissedStory, ChainWatched, ChainSeen;
    public double IdleScreenSec;                 // 다른 곳만 뜨거운데 화면은 엉뚱한 곳
    private readonly List<(int cell, double at, bool chain, FactKind kind, int root)> _pending = new();
    private long _lastPrune;

    /// <summary>화면에 보이는 칸인가 (관전 화면과 같은 넓이 — 확대 20~48칸 · 16:9).</summary>
    public bool InView(int cell)
    {
        if (cell < 0 || FocusCell < 0) return false;
        double w = Math.Clamp(Zoom, 20, 48), h = w * 9 / 16;
        return Math.Abs(L.X(cell) - L.X(FocusCell)) <= w / 2 && Math.Abs(L.Y(cell) - L.Y(FocusCell)) <= h / 2;
    }

    /// <summary>사실 하나를 사슬에 넣는다 — 새 엔진으로 번졌으면 true.</summary>
    private bool Track(Sim sim, in Fact f, float w, out CauseChain? rec)
    {
        rec = null;
        if (w < ChainFactAt) return false;
        if (f.Cause == 0) { if (w >= WatchAt && f.Kind != FactKind.Talk) Orphans[f.Kind] = Orphans.GetValueOrDefault(f.Kind) + 1; return false; }
        var chain = sim.Causes.Chain(f.Cause);
        if (chain.Count == 0) return false;
        int root = chain[0];
        if (!Chains.TryGetValue(root, out rec)) { rec = new CauseChain { Root = root, First = sim.Causes[root].Tick }; Chains[root] = rec; }
        int before = rec.Engines, line = 0;
        _line.Clear();
        foreach (int c in chain) { rec.Links.Add(c); int e = EngineOf(sim.Causes[c].Kind); Enter(rec, e); Line(e); }
        Enter(rec, EngineOf(f.Kind)); Line(EngineOf(f.Kind));
        if (_line.Count > rec.Line) { rec.Line = _line.Count; rec.LineCause = f.Cause; rec.LinePath = string.Join(" → ", _line.Select(e => EngineName[e])); }
        void Line(int e) { if (e > 0 && (line & 1 << e) == 0) { line |= 1 << e; _line.Add(e); } }
        rec.Leaves.Add(f.Cause);
        rec.Facts++; rec.Weight += w; rec.Last = f.Tick;
        if (chain.Count >= rec.Depth) { rec.Depth = chain.Count; rec.Cause = f.Cause; }
        bool spread = before != 0 && rec.Engines != before;
        if (spread) Spreads++;
        return spread;
    }

    private static void Enter(CauseChain rec, int e)
    {
        if (e <= 0 || (rec.Engines & 1 << e) != 0) return;
        rec.Engines |= 1 << e; rec.Order.Add((byte)e);
    }

    /// <summary>카메라 성적: 무게 있는 일이 났을 때 (카메라가 움직이기 전) — 이미 화면 안이면 먼저 와 있었다.</summary>
    private void Watch(int cell, float w, bool chain, FactKind kind, int root)
    {
        if (w < WatchAt || cell < 0) return;
        Watched++; if (chain) ChainWatched++;
        if (InView(cell)) { SeenBefore++; if (chain) ChainSeen++; return; }
        _pending.Add((cell, ScreenSeconds, chain, kind, root));
    }

    /// <summary>카메라가 움직인 뒤: 기다리는 일이 화면에 들어왔나 · 놓쳤나 · 엉뚱한 곳만 보고 있나.</summary>
    private void Grade(Sim sim, double screen, int top, float max)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            double age = ScreenSeconds - p.at;
            if (InView(p.cell)) { if (age <= CaughtSec) { SeenAfter++; if (p.chain) ChainSeen++; } _pending.RemoveAt(i); }
            else if (age > MissSec)
            {
                Missed++; _pending.RemoveAt(i);
                if (p.root != 0 && p.root == SceneRoot) continue;   // 그 일은 못 봤어도 같은 이야기(같은 뿌리)를 보고 있었다 — 파편 폭풍이 다른 곳을 때림
                MissedStory++; MissedKinds[p.kind] = MissedKinds.GetValueOrDefault(p.kind) + 1;
            }
        }
        if (top >= 0 && max >= 20 && !InView(top)) IdleScreenSec += screen;
        // 끝난 사슬 (6시간 조용) 은 셈에 넣고 치운다 — 90일 항해에도 기억이 늘지 않게
        if (sim.Tick - _lastPrune < Time.Hours(1)) return;
        _lastPrune = sim.Tick;
        List<int>? done = null;
        foreach (var (root, rec) in Chains) if (sim.Tick - rec.Last > Time.Hours(6)) (done ??= new()).Add(root);
        if (done == null) return;
        foreach (int root in done) { Close(Chains[root]); Chains.Remove(root); }
    }

    private void Close(CauseChain rec)
    {
        ChainsByEngines[Math.Min(13, rec.Count)]++;
        if (rec.Count < 3) return;
        BestChains.Add(rec);
        BestChains.Sort((a, b) => b.Count != a.Count ? b.Count.CompareTo(a.Count) : b.Facts.CompareTo(a.Facts));
        if (BestChains.Count > 8) BestChains.RemoveAt(8);
    }

    /// <summary>끝났든 아직이든 모든 사슬 (재미 측정기 끝에).</summary>
    public IEnumerable<CauseChain> AllChains() => Chains.Values.Concat(BestChains);

    /// <summary>건넌 엔진 수별 사슬 수 (끝난 것 + 아직 이어지는 것).</summary>
    public int ChainsWith(int engines) => Enumerable.Range(engines, 14 - engines).Sum(i => ChainsByEngines[i]) + Chains.Values.Count(c => c.Count >= engines);

    /// <summary>카메라 성적 한 줄 (Lab --fun).</summary>
    public string CameraLine()
    {
        double pct(int a, int b) => b > 0 ? 100.0 * a / b : 0;
        return $"먼저 와 있음 {pct(SeenBefore, Watched):0}% · 3초 안 {pct(SeenAfter, Watched):0}% · 놓침 {pct(Missed, Watched):0}% (그 이야기도 못 봄 {pct(MissedStory, Watched):0}%) (큰 일 {Watched})"
            + $" · 연쇄의 일 본 것 {pct(ChainSeen, ChainWatched):0}% ({ChainWatched}) · 자르기 {Switches / Math.Max(1e-9, ScreenSeconds / 60):0.0}/분"
            + $" · 엉뚱한 곳 {100 * IdleScreenSec / Math.Max(1e-9, ScreenSeconds):0}%"
            + $" · 끌림 {Pulls} · 엇갈림 {CrossCuts} · 따라감 {FollowTicks}틱 · 미리 보기 (복제 {Foresights} · 앞선 틱 {AheadSteps} · 다시 맞춤 {Resyncs} · {ForeseeMs / 1000:0.0}초 · 본 일 {Predicted} · 미리 자름 {ForeCuts} · 까닭 {string.Join(" ", BrewWhy.Select(kv => $"{kv.Key} {kv.Value}"))}) · 공기 조짐 {AirWarnings}"
            + (AirWhy.Count > 0 ? " (" + string.Join(" ", AirWhy.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")) + ")" : "")
            + (MissedKinds.Count > 0 ? " · 못 본 이야기 " + string.Join(" ", MissedKinds.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{FactNames.Of(kv.Key)} {kv.Value}")) : "");
    }
}
