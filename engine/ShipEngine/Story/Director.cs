namespace ShipEngine;

/// <summary>하이라이트 하나: 볼 만했던 장면.</summary>
public sealed class Highlight
{
    public long Tick;          // 정점
    public long StartTick;     // 다시보기 시작 — 원인 사슬의 뿌리(첫 조짐)
    public long Last;          // 이 장면에 마지막으로 이어진 일
    public int Cell;
    public float Score;
    public FactKind Kind;
    public int Cause;
    public int Actor;
    public int Root;           // 원인 사슬의 뿌리 (같은 뿌리의 일은 한 하이라이트로)
    public string Title = "";
    /// <summary>사람 이야기 (다툼 · 사과 · 위로 · 소문 · 몸싸움 · 몰래 한 짓) — 하이라이트 목록에서 사건과 나눠 보인다.</summary>
    public bool People;
    public string Line = "";   // 그 장면의 한 마디 (있으면)
}

/// <summary>
/// ⑬ 연출 (docs/ENGINE.md §4 ⑬) — 엔진이 아니다: 세계 밖에서 매 틱 <see cref="Observe"/>로 읽기만 한다 (결정론 · 찍기와 무관).
/// <para>관심(사실의 무게 × 원인 사슬 깊이 × 처음 보는 일 · 위험한 칸의 사람) → 카메라(머물기 · 확대) · 시간(조용하면 빨리 · 조짐에 느리게 · 결정적 순간 아주 느리게) ·
/// 하이라이트(제목 · 첫 조짐부터) · 다시보기 고리(2분마다 찍기 1시간치).</para>
/// </summary>
public sealed class Director
{
    public const double HalfLifeSec = 40;       // 관심이 반으로 주는 시간 (게임 초)
    public const int SpotRadius = 8;
    public const double HoldScreenSec = 8;      // 한 장면에 적어도 (화면 초)
    public const double CalmSpeed = 60, OmenSpeed = 8, DangerSpeed = 2, CriticalSpeed = 0.5; // 화면 1초에 틱
    public const int RingEvery = 50, RingSize = 30;   // 2분마다 · 1시간치
    public const float HighlightAt = 80;

    private readonly Layout L;
    public readonly float[] Heat;
    public int FocusCell = -1;
    public int Zoom = 40;                       // 화면에 보이는 칸 수 (가로)
    public double Speed = CalmSpeed;            // 화면 1초에 틱
    public string Mood = "조용함";
    public string Caption = "";
    public readonly List<Highlight> Highlights = new();
    public readonly List<(long tick, byte[] snap)> Ring = new();
    public bool KeepRing = true;
    public long Switches;
    public double ScreenSeconds;                // 지금까지 흐른 화면 시간
    public double CalmScreenSec, OmenScreenSec, DangerScreenSec, CriticalScreenSec;
    public readonly HashSet<FactKind> SeenKinds = new();
    private readonly HashSet<int> _seenTalk = new();
    public int Firsts;
    public int NearMisses;                      // 위험했다 산 사람 (산소 포화 0.75 아래 · 쓰러짐 뒤 깨어남)

    private long _cursor;
    private bool _resting;                      // 볼 것이 없어 배 가운데를 보고 있다 (첫 일에 바로 간다)
    private double _sinceSwitch;
    private double _criticalLeft;               // 아주 느리게 남은 화면 초
    private long _lastOmen = long.MinValue / 2, _lastDanger = long.MinValue / 2;
    private readonly Dictionary<int, byte> _wasInDanger = new();
    private readonly Dictionary<int, long> _lastNear = new(); // 한 사람의 간발의 차는 30분에 한 번만 (쓰러졌다 깨기를 되풀이해도 한 번)

    public Director(Layout layout)
    {
        L = layout;
        Heat = new float[L.N];
    }

    /// <summary>사실 종류의 무게 (볼 만한 정도).</summary>
    public static float Weight(FactKind k) => k switch
    {
        FactKind.Died => 100, FactKind.Collapsed => 40, FactKind.Rescue => 35, FactKind.Panicked => 30, FactKind.WallBreach => 30,
        FactKind.DoorBlown => 25, FactKind.Ignition => 20, FactKind.FireSpread => 8, FactKind.Extinguished => 25, FactKind.Patched => 20,
        FactKind.Repaired => 10, FactKind.ComputerSeal => 18, FactKind.ComputerAlarm => 10, FactKind.DebrisNear => 15, FactKind.Impact => 12,
        FactKind.StorySeed => 6, FactKind.Hurt => 12, FactKind.Fled => 10, FactKind.DoorSealed => 6, FactKind.Warned => 5, FactKind.Noticed => 4,
        FactKind.MachineFail => 12, FactKind.Short => 15, FactKind.BreakerTrip => 5, FactKind.Flare => 20, FactKind.WallCrack => 15,
        FactKind.Woke => 2, FactKind.ComputerAssign => 4, FactKind.Sick => 8,
        FactKind.Relationship => 12, FactKind.Grieved => 30, FactKind.TrustLost => 25, FactKind.WentOutside => 20,
        FactKind.Talk => 2, FactKind.Blamed => 15, FactKind.Fight => 60, FactKind.Sabotage => 40, _ => 1,
    };

    /// <summary>사실 하나의 무게 — 대화는 끝에 따라 (다툼 · 사과 · 위로 · 소문은 볼 만하고 잡담은 거의 0) · 끝장 몸싸움은 더.</summary>
    public static float WeightOf(in Fact f) => f.Kind switch
    {
        FactKind.Talk => (TalkEnd)(int)f.Size switch
        {
            TalkEnd.Argue => 40, TalkEnd.Apology => 30, TalkEnd.Comfort => 22, TalkEnd.Spread => 22, TalkEnd.Praise => 16, TalkEnd.Defend => 16,
            TalkEnd.Friction => 6, TalkEnd.Worry => 6, _ => 1,
        },
        FactKind.Fight => f.Size >= 2 ? 90 : 60,
        _ => Weight(f.Kind),
    };

    /// <summary>사람 이야기 사실인가 (관계 · 대화 · 싸움 · 몰래 한 짓 · 슬픔).</summary>
    public static bool PeopleFact(FactKind k) => k is FactKind.Talk or FactKind.Blamed or FactKind.Fight or FactKind.Sabotage or FactKind.Relationship or FactKind.Grieved or FactKind.TrustLost;

    private static bool Critical(FactKind k) => k is FactKind.Died or FactKind.Collapsed or FactKind.WallBreach or FactKind.DoorBlown or FactKind.Panicked or FactKind.Fight;
    private static bool Omen(FactKind k) => k is FactKind.DebrisNear or FactKind.ComputerAlarm or FactKind.StorySeed or FactKind.WallCrack or FactKind.Flare or FactKind.Noticed or FactKind.Warned or FactKind.Blamed;
    private static bool Omen(in Fact f) => Omen(f.Kind) || f.Kind == FactKind.Talk && (TalkEnd)(int)f.Size is TalkEnd.Argue or TalkEnd.Spread;

    // ───────────────────────── 한 틱 ─────────────────────────

    public void Observe(Sim sim)
    {
        long t = sim.Tick;
        double screen = 1.0 / Math.Max(0.1, Speed);
        ScreenSeconds += screen; _sinceSwitch += screen;
        // 관심이 준다
        float decay = (float)Math.Exp(-Time.SecondsPerTick * 0.6931 / HalfLifeSec);
        for (int i = 0; i < Heat.Length; i++) if (Heat[i] > 0.01f) Heat[i] *= decay; else Heat[i] = 0;

        var mind = sim.Find<Mind>(); var life = sim.Find<Life>();
        // 사실
        Fact best = default; float bestScore = 0;
        foreach (var f in sim.Facts.Since(_cursor))
        {
            int cell = f.Cell;
            if (cell < 0 && f.Actor >= 0 && mind != null && f.Actor < mind.Agents.Count) cell = mind.Agents[f.Actor].C.Cell;
            float w = WeightOf(f);
            int depth = f.Cause != 0 ? sim.Causes.Depth(f.Cause) : 0;
            float score = w * (1 + 0.15f * depth);
            // 처음 보는 일은 두 배 (대화는 끝마다 따로 — 첫 다툼 · 첫 사과)
            if (w >= 5 && (f.Kind == FactKind.Talk ? _seenTalk.Add((int)f.Size) : SeenKinds.Add(f.Kind))) { score *= 2; Firsts++; }
            if (cell >= 0 && cell < Heat.Length) Heat[cell] += score;
            if (Omen(f)) _lastOmen = t;
            if (Critical(f.Kind)) { _criticalLeft = 3; _lastDanger = t; }
            if (score > bestScore && cell >= 0) { bestScore = score; best = f; best.Cell = cell; }
        }
        _cursor = sim.Facts.Total;

        // 위험한 칸의 사람 (매 틱)
        bool danger = false;
        if (mind != null && life != null)
            foreach (var a in mind.Agents)
            {
                if (!a.IsPerson) continue;
                var b = life.Bodies[a.C.Body];
                if (b.Alive == 0) continue;
                float d = (1 - b.SpO2) * 60 + b.Hypoxia * 40 + (b.Conscious == 0 ? 30 : 0) + (a.C.Mode == 2 ? 10 : 0) + (a.C.Mode == 1 ? 8 : 0);
                if (d > 15) { Heat[b.Cell] += d * 0.1f; danger = true; }
                // 간발의 차: 위험했다가 살아난 사람
                bool now = b.SpO2 < 0.75f || b.Conscious == 0;
                if (now) _wasInDanger[a.Id] = 1;
                else if (_wasInDanger.Remove(a.Id) && b.SpO2 > 0.9f && t - _lastNear.GetValueOrDefault(a.Id, long.MinValue / 2) > Time.Minutes(30)) { NearMisses++; _lastNear[a.Id] = t; }
            }
        var chem = sim.Find<Chemistry>();
        if (chem != null && chem.BurningCount > 0) danger = true;
        if (danger) _lastDanger = t;

        Camera(sim, t);
        Pace(t, screen);
        if (bestScore > 0) MaybeHighlight(sim, best, t);
        // 다시보기 고리
        if (KeepRing && t % RingEvery == 0)
        {
            Ring.Add((t, sim.Save()));
            if (Ring.Count > RingSize) Ring.RemoveAt(0);
        }
    }

    // ───────────────────────── 카메라 ─────────────────────────

    /// <summary>칸 c 둘레(반경 SpotRadius) 관심 합.</summary>
    public float SpotScore(int c)
    {
        if (c < 0) return 0;
        int x0 = L.X(c), y0 = L.Y(c); float s = 0;
        for (int y = Math.Max(0, y0 - SpotRadius); y <= Math.Min(L.H - 1, y0 + SpotRadius); y++)
            for (int x = Math.Max(0, x0 - SpotRadius); x <= Math.Min(L.W - 1, x0 + SpotRadius); x++)
                s += Heat[L.Index(x, y)];
        return s;
    }

    private void Camera(Sim sim, long t)
    {
        int top = -1; float max = 0;
        for (int i = 0; i < Heat.Length; i++) if (Heat[i] > max) { max = Heat[i]; top = i; }
        if (top < 0 || max < 1) { if (FocusCell < 0) { FocusCell = L.Index(L.W / 2, L.H / 2); _resting = true; } return; }
        float cand = SpotScore(top), cur = _resting ? 0 : SpotScore(FocusCell);
        bool urgent = cand > cur * 3 && cand > 50;
        if (FocusCell < 0 || _resting || urgent || cand > cur * 1.5f && _sinceSwitch >= HoldScreenSec)
        {
            // 다른 곳으로 '자르기' (지금 장면 밖이면 장면 바꿈으로 센다)
            if (FocusCell >= 0 && !_resting && Far(top, FocusCell)) { Switches++; _sinceSwitch = 0; }
            FocusCell = top; _resting = false;
        }
        else
        {
            // 지금 장면 안에서는 가장 뜨거운 곳으로 '훑기' (장면 바꿈이 아니다)
            int best = FocusCell; float bm = Heat[FocusCell];
            int fx0 = L.X(FocusCell), fy0 = L.Y(FocusCell);
            for (int y = Math.Max(0, fy0 - SpotRadius); y <= Math.Min(L.H - 1, fy0 + SpotRadius); y++)
                for (int x = Math.Max(0, fx0 - SpotRadius); x <= Math.Min(L.W - 1, fx0 + SpotRadius); x++)
                {
                    int i = L.Index(x, y);
                    if (Heat[i] > bm * 1.2f) { bm = Heat[i]; best = i; }
                }
            FocusCell = best;
        }
        // 확대: 관심이 큰 칸들의 넓이에 맞춘다
        int minX = L.X(FocusCell), maxX = minX, minY = L.Y(FocusCell), maxY = minY;
        float cut = max * 0.1f;
        int fx = L.X(FocusCell), fy = L.Y(FocusCell);
        for (int y = Math.Max(0, fy - 20); y <= Math.Min(L.H - 1, fy + 20); y++)
            for (int x = Math.Max(0, fx - 30); x <= Math.Min(L.W - 1, fx + 30); x++)
                if (Heat[L.Index(x, y)] > cut) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        Zoom = Math.Clamp(Math.Max(maxX - minX, (maxY - minY) * 2) + 10, 14, 64);
    }

    private bool Far(int a, int b) => Math.Max(Math.Abs(L.X(a) - L.X(b)), Math.Abs(L.Y(a) - L.Y(b))) > SpotRadius;

    // ───────────────────────── 시간 ─────────────────────────

    private void Pace(long t, double screen)
    {
        if (_criticalLeft > 0) { _criticalLeft -= screen; Speed = CriticalSpeed; Mood = "결정적 순간"; CriticalScreenSec += screen; return; }
        if (t - _lastDanger < Time.Minutes(1)) { Speed = DangerSpeed; Mood = "위험"; DangerScreenSec += screen; return; }
        if (t - _lastOmen < Time.Minutes(3)) { Speed = OmenSpeed; Mood = "조짐"; OmenScreenSec += screen; return; }
        Speed = CalmSpeed; Mood = "조용함"; CalmScreenSec += screen;
    }

    // ───────────────────────── 하이라이트 ─────────────────────────

    private void MaybeHighlight(Sim sim, in Fact f, long t)
    {
        float spot = SpotScore(f.Cell);
        if (spot < HighlightAt) return;
        int root = 0;
        if (f.Cause != 0) { var ch = sim.Causes.Chain(f.Cause); root = ch.Count > 0 ? ch[0] : 0; }
        // 하나로 묶는다: 같은 뿌리에서 나온 일(30분 안) · 같은 곳 2분 안 · 한 소동(마지막 일에서 15분 안에 그 소동 동안 새로 시작된 일 — 잔해 폭풍 뒤 봉쇄 · 경보) — 더 크면 고친다 (정점 · 제목 · 시작은 더 이른 쪽)
        long rootTick = root != 0 ? sim.Causes[root].Tick : t;
        foreach (var h in Highlights)
            if (root != 0 && h.Root == root && t - h.Tick < Time.Minutes(30)
                || t - h.Tick < Time.Minutes(2) && Math.Max(Math.Abs(L.X(h.Cell) - L.X(f.Cell)), Math.Abs(L.Y(h.Cell) - L.Y(f.Cell))) <= SpotRadius * 2
                || t - h.Last < Time.Minutes(15) && rootTick >= h.StartTick - Time.Minutes(1))
            {
                if (spot > h.Score) { h.Score = spot; h.Tick = t; h.Cell = f.Cell; }
                if (WeightOf(f) > Weight(h.Kind)) { h.Kind = f.Kind; h.Cause = f.Cause; h.Actor = f.Actor; h.Title = TitleOf(sim, f); h.People = PeopleFact(f.Kind); h.Line = LineOf(sim, f); }
                h.StartTick = Math.Min(h.StartTick, RootTick(sim, f, t));
                if (h.Root == 0) h.Root = root;
                h.Last = Math.Max(h.Last, t);
                return;
            }
        Highlights.Add(new Highlight { Tick = t, Cell = f.Cell, Score = spot, Kind = f.Kind, Cause = f.Cause, Actor = f.Actor, Root = root, Title = TitleOf(sim, f), StartTick = RootTick(sim, f, t), Last = t,
            People = PeopleFact(f.Kind), Line = LineOf(sim, f) });
        Caption = Highlights[^1].Title;
    }

    private static long RootTick(Sim sim, in Fact f, long t)
    {
        if (f.Cause == 0) return Math.Max(0, t - Time.Minutes(1));
        var chain = sim.Causes.Chain(f.Cause);
        long root = chain.Count > 0 ? sim.Causes[chain[0]].Tick : t;
        return Math.Max(0, Math.Min(root, t) - Time.Minutes(0.5)); // 조짐 조금 앞부터
    }

    public static string FactText(FactKind k) => FactNames.Of(k);

    /// <summary>그 사실에 이어진 한 마디 (대화 · 싸움 — 사회의 말 기록에서).</summary>
    private static string LineOf(Sim sim, in Fact f)
    {
        if (f.Kind is not (FactKind.Talk or FactKind.Fight)) return "";
        var so = sim.Find<Society>();
        if (so == null) return "";
        for (int i = so.Sayings.Count - 1; i >= 0; i--)
        {
            var s = so.Sayings[i];
            if (s.Tick < f.Tick - 25) break;
            if (s.Speaker == f.Actor) return sim.Causes.Text(s.Line);
        }
        return "";
    }

    /// <summary>사람 이야기 · 사건 하이라이트 목록 (최근 것 먼저 · 화면 · 기록).</summary>
    public IEnumerable<Highlight> List(bool? people = null) => Highlights.AsEnumerable().Reverse().Where(h => people == null || h.People == people);

    private static string TitleOf(Sim sim, in Fact f)
    {
        var mind = sim.Find<Mind>();
        int actor = f.Kind is FactKind.Died or FactKind.Collapsed or FactKind.Hurt && mind != null ? mind.AgentOfBody(f.Actor) : f.Actor; // 생명의 사실은 몸 번호
        string who = actor >= 0 && mind != null && actor < mind.Agents.Count ? Ko.I(mind.NameOf(actor)) + " " : "";
        string story = f.Cause != 0 ? sim.Causes.Story(f.Cause) : "";
        return story.Length > 0 ? story : who + FactText(f.Kind);
    }

    // ───────────────────────── 다시보기 ─────────────────────────

    /// <summary>하이라이트의 시작(첫 조짐) 직전 찍기에서 되살린 세계 — 이어 돌리면 같은 일이 그대로 일어난다. 고리에 없으면 null.</summary>
    public Sim? Replay(Sim sim, Highlight h)
    {
        (long tick, byte[] snap)? pick = null;
        foreach (var r in Ring) if (r.tick <= h.StartTick) pick = r;
        if (pick == null) { if (Ring.Count > 0 && Ring[0].tick <= h.Tick) pick = Ring[0]; else return null; }
        var w = new Sim(sim.Setup);
        w.Load(pick.Value.snap);
        return w;
    }

    /// <summary>재미 점수 재료 (Lab --fun).</summary>
    public double CalmShare => ScreenSeconds > 0 ? CalmScreenSec / ScreenSeconds : 1;
}
