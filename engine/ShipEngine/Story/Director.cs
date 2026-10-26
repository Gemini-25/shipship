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
    public List<Beat>? Beats;  // ⑬ 그 소동의 박자들 (원인 사슬의 기록과 같은 목록 — 컷 목록이 읽는다)
}

/// <summary>
/// ⑬ 연출 (docs/ENGINE.md §4 ⑬) — 엔진이 아니다: 세계 밖에서 매 틱 <see cref="Observe"/>로 읽기만 한다 (결정론 · 찍기와 무관).
/// <para>관심(사실의 무게 × 원인 사슬 깊이 × 처음 보는 일 · 위험한 칸의 사람) → 카메라(머물기 · 확대) · 시간(조용하면 빨리 · 조짐에 느리게 · 결정적 순간 아주 느리게) ·
/// 하이라이트(제목 · 첫 조짐부터) · 다시보기 고리(2분마다 찍기 1시간치).</para>
/// </summary>
public sealed partial class Director
{
    public const double HalfLifeSec = 40;       // 관심이 반으로 주는 시간 (게임 초)
    public const int SpotRadius = 8;
    public const double HoldScreenSec = 8;      // 한 장면에 적어도 (화면 초)
    public const double CalmSpeed = 60, OmenSpeed = 8, DangerSpeed = 2, CriticalSpeed = 0.5; // 화면 1초에 틱
    /// <summary>함장이 고른 하루 길이 (화면 분 · 0이면 예전 고정 빠르기). 고르면 조용함의 빠르기를 그때그때 셈한다 (남은 조용한 틱 ÷ 남은 화면 시간) ·
    /// 조짐 · 위험의 느린 시간은 하루 예산(목표의 4할)에서 꺼내 쓰고 · 다 쓰면 무거운 조짐(다툼 · 탓 · 감지)만 1분 늦춘다 · 결정적 순간은 예산 밖 (docs/REVIEW.md ① · 함장 결정).</summary>
    public double DayMinutes;
    public const double SlowShare = 0.4, CalmFloor = 20, CalmCeiling = 20_000;

    public const int RingEvery = 50, RingSize = 30;   // 2분마다 · 1시간치
    private bool _ringLate;   // 주컴퓨터 일꾼이 셈 중이라 미룬 다시보기 찍기
    public const float HighlightAt = 80;

    private readonly Layout L;
    public readonly float[] Heat;
    /// <summary>카메라만 쓰는 관심 (몸으로 읽는 조짐 · 주인공이 지나간 칸) — 하이라이트 문턱에는 넣지 않는다 (일어난 일로만 하이라이트).</summary>
    public readonly float[] Aux;
    public int FocusCell = -1;
    public int Zoom = 40;                       // 화면에 보이는 칸 수 (가로)
    public double Speed = CalmSpeed;            // 화면 1초에 틱
    public string Mood = "조용함";
    public string Caption = "";
    public readonly List<Highlight> Highlights = new();
    public readonly List<(long tick, byte[] snap, int len)> Ring = new();   // 찍은 것은 snap의 앞 len 바이트 (칸을 다시 쓴다)
    public bool KeepRing = true;
    public long Switches;
    public double ScreenSeconds;                // 지금까지 흐른 화면 시간
    public double CalmScreenSec, OmenScreenSec, DangerScreenSec, CriticalScreenSec;
    public readonly HashSet<FactKind> SeenKinds = new();
    private readonly HashSet<int> _seenTalk = new();
    public int Firsts;
    public int NearMisses;                      // 위험했다 산 사람 (산소 포화 0.75 아래 · 쓰러짐 뒤 깨어남)

    private long _cursor;
    private int _top = -1; private float _max;
    private bool _resting;                      // 볼 것이 없어 사람들의 일상(없으면 배 가운데)을 보고 있다 (첫 일에 바로 간다)
    private double _sinceSwitch;
    private double _criticalLeft;               // 아주 느리게 남은 화면 초
    private long _lastOmen = long.MinValue / 2, _lastDanger = long.MinValue / 2, _lastHeavyOmen = long.MinValue / 2;
    private int _omenRoot;                      // 지금 열린 조짐 장면의 뿌리 (같은 뿌리의 조짐은 다시 늘리지 않는다)
    private long _budgetDay = -1;
    private double _screenToday, _slowToday;    // 오늘 쓴 화면 초 · 그 가운데 조짐 · 위험에 쓴 초
    public double ScreenTodaySec => _screenToday;
    public double SlowTodaySec => _slowToday;
    private readonly Dictionary<int, byte> _wasInDanger = new();
    private readonly Dictionary<int, long> _lastNear = new(); // 한 사람의 간발의 차는 30분에 한 번만 (쓰러졌다 깨기를 되풀이해도 한 번)

    public Director(Layout layout)
    {
        L = layout;
        Heat = new float[L.N]; Aux = new float[L.N];
    }

    /// <summary>사실 종류의 무게 (볼 만한 정도).</summary>
    public static float Weight(FactKind k) => k switch
    {
        FactKind.Died => 100, FactKind.Collapsed => 40, FactKind.Rescue => 35, FactKind.Panicked => 30, FactKind.WallBreach => 30,
        FactKind.DoorBlown => 25, FactKind.Ignition => 20, FactKind.FireSpread => 8, FactKind.Extinguished => 25, FactKind.Patched => 20,
        FactKind.Repaired => 10, FactKind.ComputerSeal => 18, FactKind.ComputerAlarm => 10, FactKind.DebrisNear => 15, FactKind.Sensed => 8, FactKind.Letter => 6, FactKind.Impact => 12,
        FactKind.Hurt => 12, FactKind.Fled => 10, FactKind.DoorSealed => 6, FactKind.Warned => 5, FactKind.Noticed => 4,
        FactKind.MachineFail => 12, FactKind.Short => 15, FactKind.BreakerTrip => 5, FactKind.Flare => 20, FactKind.WallCrack => 15,
        FactKind.Woke => 2, FactKind.ComputerAssign => 4, FactKind.Sick => 8, FactKind.Gravity => 20, FactKind.Vomit => 6, FactKind.Arrest => 40, FactKind.Revived => 35, FactKind.Heart => 12, FactKind.Culture => 10,
        FactKind.Relationship => 12, FactKind.Grieved => 30, FactKind.TrustLost => 25, FactKind.WentOutside => 20,
        FactKind.Talk => 2, FactKind.Blamed => 15, FactKind.Fight => 60, FactKind.Sabotage => 40, FactKind.Confessed => 30,
        FactKind.Burn => 14, FactKind.Fell => 22, FactKind.Contact => 12, FactKind.Jumper => 16, FactKind.Salvaged => 14, FactKind.Aim => 18, FactKind.ShipSplit => 100, FactKind.Lie => 30, FactKind.LieExposed => 50, FactKind.LogShown => 6, FactKind.Shelter => 22, FactKind.Meeting => 25, FactKind.Verdict => 45, FactKind.Elected => 35, FactKind.Mutiny => 60, FactKind.Devised => 30, FactKind.Consulted => 14,
        FactKind.MethodWorked => 40, FactKind.MethodFailed => 25, FactKind.ManualChanged => 30,   // 궁리 끝에 해낸 일 · 매뉴얼대로 했다가 틀어진 일 · 회의가 매뉴얼을 고친 일
        FactKind.CableCut => 8, FactKind.PipeLeak => 10, FactKind.Tumble => 40, FactKind.Crash => 70,   // ⑥ 바퀴 3 — 선이 끊겼다 (탔다 · 찢겼다 · 피복이 갈라졌다) · 관이 샌다
        FactKind.Shattered => 8, FactKind.Toppled => 10, FactKind.ItemFell => 6, FactKind.TornLoose => 30, _ => 1,   // 물체 볼거리 (PH12 — 깨졌다는 사람 곁이면 15)
    };

    /// <summary>사실 하나의 무게 — 대화는 끝에 따라 (다툼 · 사과 · 위로 · 소문은 볼 만하고 잡담은 거의 0) · 끝장 몸싸움은 더.</summary>
    public static float WeightOf(in Fact f) => f.Kind switch
    {
        FactKind.Talk => (TalkEnd)(int)f.Size switch
        {
            TalkEnd.Argue => 40, TalkEnd.Apology => 30, TalkEnd.Comfort => 22, TalkEnd.Spread => 22, TalkEnd.Praise => 16, TalkEnd.Defend => 16,
            TalkEnd.Friction => 6, TalkEnd.Worry => 6, TalkEnd.Accept => 35, TalkEnd.Reject => 30, _ => 1,   // 고백 — 받아 줌 · 거절
        },
        FactKind.Fight => f.Size >= 2 ? 90 : 60,
        FactKind.Mistake => f.Size >= 2 ? 25 : 1,
        FactKind.Contact => f.Other switch { 1 => 30, 3 => 35, 5 => 40, _ => 12 },   // 조난 신호 · 붙었다 · 늦었다
        FactKind.Burn => f.Other == (int)BurnKind.Dodge ? 30 : 14,   // 숨긴 실수 · 숨겼다 (사람들은 모르지만 관객은 본다)
        _ => Weight(f.Kind),
    };

    /// <summary>사람 이야기 사실인가 (관계 · 대화 · 싸움 · 몰래 한 짓 · 슬픔).</summary>
    public static bool PeopleFact(FactKind k) => k is FactKind.Talk or FactKind.Blamed or FactKind.Fight or FactKind.Sabotage or FactKind.Relationship or FactKind.Grieved or FactKind.TrustLost or FactKind.Confessed or FactKind.Lie or FactKind.LieExposed or FactKind.Meeting or FactKind.Verdict or FactKind.Elected or FactKind.Mutiny or FactKind.ManualChanged;

    private static bool Critical(FactKind k) => k is FactKind.Died or FactKind.Collapsed or FactKind.Arrest or FactKind.WallBreach or FactKind.DoorBlown or FactKind.Panicked or FactKind.Fight or FactKind.ShipSplit or FactKind.Crash;
    private static bool Omen(FactKind k) => k is FactKind.DebrisNear or FactKind.Sensed or FactKind.ComputerAlarm or FactKind.WallCrack or FactKind.Flare or FactKind.Noticed or FactKind.Warned or FactKind.Blamed;
    private static bool Omen(in Fact f) => Omen(f.Kind) || f.Kind == FactKind.Talk && (TalkEnd)(int)f.Size is TalkEnd.Argue or TalkEnd.Spread;

    // ───────────────────────── 한 틱 ─────────────────────────

    public void Observe(Sim sim)
    {
        long t = sim.Tick;
        double screen = 1.0 / Math.Max(0.1, Speed);
        ScreenSeconds += screen; _sinceSwitch += screen;
        // 관심이 준다
        float decay = (float)Math.Exp(-Time.SecondsPerTick * 0.6931 / HalfLifeSec);
        for (int i = 0; i < Heat.Length; i++) { if (Heat[i] > 0.01f) Heat[i] *= decay; else Heat[i] = 0; if (Aux[i] > 0.01f) Aux[i] *= decay; else Aux[i] = 0; }

        var mind = sim.Find<Mind>(); var life = sim.Find<Life>();
        // 사실
        Fact best = default; float bestScore = 0;
        foreach (var f in sim.Facts.Since(_cursor))
        {
            int cell = f.Cell;
            if (cell < 0 && f.Actor >= 0 && mind != null && f.Actor < mind.Agents.Count) cell = mind.Agents[f.Actor].C.Cell;
            float w = WeightOf(f);
            if (f.Kind == FactKind.Shattered && PersonNear(mind, life, cell)) w = ShatterNearWeight;
            if (PropFact(f.Kind) && InView(cell)) _propNew = true;
            int depth = f.Cause != 0 ? sim.Causes.Depth(f.Cause) : 0;
            float score = w * (1 + 0.15f * depth);
            // 처음 보는 일은 두 배 (대화는 끝마다 따로 — 첫 다툼 · 첫 사과)
            if (w >= 5 && (f.Kind == FactKind.Talk ? _seenTalk.Add((int)f.Size) : SeenKinds.Add(f.Kind))) { score *= 2; Firsts++; }
            bool spread = Track(sim, f, w, out var rec);
            int root = rec?.Root ?? 0;
            if (root != 0 && root == SceneRoot) score *= ContinueBoost;     // 지금 보는 사슬이 이어진다
            if (spread && cell >= 0)                                           // 사슬이 새 엔진으로 번졌다 — 그 앞머리로
            {
                score *= SpreadBoost; _lastOmen = t;
                if (score > _pullScore || ScreenSeconds - _pullAt > CaughtSec) { _pullCell = cell; _pullAt = ScreenSeconds; _pullScore = score; _pullRoot = root; _pullActor = AgentOf(mind, f); }
            }
            Watch(cell, w, rec != null && rec.Count >= 2, f.Kind, root);
            DayNote(sim, f, w, cell, AgentOf(mind, f), rec);   // ⑬ 이야기 편집 — 오늘의 장
            if (w >= ChainFactAt && cell >= 0) Recent(cell, root, AgentOf(mind, f), score, t);
            if (cell >= 0 && cell < Heat.Length) Heat[cell] += score;
            if (Omen(f)) OmenSeen(f, w, root, t);
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
        if (mind != null && life != null && t % SenseEvery == 0) Sense(sim, mind, life, chem, t);
        Foresight(sim, t);
        Anticipate(sim, t);

        Camera(sim, t);
        Grade(sim, screen, _top, _max);
        PropShow(sim);
        Pace(t, screen);
        if (bestScore > 0) MaybeHighlight(sim, best, t);
        DayTick(sim, t);
        // 다시보기 고리 (처음 본 틱에도 한 장 — 첫 2분 안에 시작한 일도 첫 조짐부터 되살린다)
        bool ring = KeepRing && (t % RingEvery == 0 || Ring.Count == 0 || _ringLate);
        if (ring && sim.Find<ShipMind>() is { WorkerBusy: true }) { _ringLate = true; ring = false; }   // 주컴퓨터 일꾼이 셈 중이면 찍기가 기다린다 — 다음 틱으로 미룬다 (세계는 그대로)
        if (ring)
        {
            _ringLate = false;
            byte[]? buf = null;
            if (Ring.Count >= RingSize) { buf = Ring[0].snap; Ring.RemoveAt(0); }   // 가장 오래된 칸에 다시 찍는다
            int len = sim.SaveInto(ref buf);
            Ring.Add((t, buf!, len));
        }
    }

    // ───────────────────────── 카메라 ─────────────────────────

    /// <summary>칸 c 둘레(반경 SpotRadius) 카메라가 보는 관심 합 (일어난 일 + 카메라만 쓰는 관심).</summary>
    private float ViewScore(int c)
    {
        if (c < 0) return 0;
        int x0 = L.X(c), y0 = L.Y(c); float s = 0;
        for (int y = Math.Max(0, y0 - SpotRadius); y <= Math.Min(L.H - 1, y0 + SpotRadius); y++)
            for (int x = Math.Max(0, x0 - SpotRadius); x <= Math.Min(L.W - 1, x0 + SpotRadius); x++)
            { int i = L.Index(x, y); s += Heat[i] + Aux[i]; }
        return s;
    }

    /// <summary>칸 c 둘레(반경 SpotRadius) 일어난 일의 관심 합 (하이라이트 문턱).</summary>
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
        // 가장 뜨거운 칸 · 화면 밖에서 가장 뜨거운 칸 (두 곳 오가기)
        int top = -1, outTop = -1; float max = 0, outMax = 0;
        bool watching = FocusCell >= 0 && !_resting;
        for (int i = 0; i < Heat.Length; i++)
        {
            float h = Heat[i] + Aux[i];
            if (h <= outMax) continue;
            if (h > max) { max = h; top = i; }
            if (watching && !InView(i)) { outMax = h; outTop = i; }
        }
        _top = top; _max = max;
        if (top < 0 || max < 1)
        {
            // 볼 만한 일이 없다: 막 본 장면은 적어도 HoldScreenSec 머물고 · 그다음은 사람들의 일상을 비춘다
            if (FocusCell < 0 || _resting || ScreenSeconds - _cutAt >= HoldScreenSec) Daily(sim);
            Subject = -1; SceneRoot = 0; return;
        }
        float cur = _resting ? 0 : ViewScore(FocusCell);
        // 1. 사슬의 앞머리: 사슬이 새 엔진으로 번진 곳이 화면 밖이면 (결정적 순간이 아니고 · 지금 장면을 2초는 봤으면) 그리로 간다
        if (_pullCell >= 0)
        {
            if (ScreenSeconds - _pullAt > CaughtSec || InView(_pullCell)) _pullCell = -1;
            else if (_criticalLeft <= 0 && (_resting || _sinceSwitch >= PullHoldSec) && ViewScore(_pullCell) >= cur * 0.25f)
            {
                CutTo(sim, _pullCell, t, _pullRoot, _pullActor); Pulls++; _pullCell = -1;
                Frame(max); return;
            }
        }
        // 주인공이 이야기 속에서 움직이면 따라간다 — 그가 떠나온 자리(같은 장면의 식은 자리)로는 되돌아 자르지 않는다
        int sc = _resting || FocusCell < 0 ? -1 : SubjectCell(sim, t);
        bool following = sc >= 0 && Math.Max(Math.Abs(L.X(sc) - L.X(FocusCell)), Math.Abs(L.Y(sc) - L.Y(FocusCell))) <= FollowReach;
        float cand = ViewScore(top);
        bool urgent = cand > cur * 3 && cand > 50;
        bool held = t < _foreHold;   // 미리 본 큰 일을 기다리는 중 — 다른 곳으로 자르지 않는다
        if (!held && (FocusCell < 0 || _resting || urgent || cand > cur * 1.5f && _sinceSwitch >= HoldScreenSec) && !(following && SameScene(top, t)))
            CutTo(sim, top, t);
        // 2. 두 곳 오가기: 화면 밖에 지금 장면의 반쯤 되는 일이 오래 안 보였으면 잠깐 그리로 (영화의 엇갈려 보여 주기)
        else if (!held && outTop >= 0 && _sinceSwitch >= CrossHoldSec && _criticalLeft <= 0 && ViewScore(outTop) is float c2 && c2 > 30 && c2 >= cur * 0.5f && !SameScene(outTop, t))
        {
            CutTo(sim, outTop, t); CrossCuts++;
        }
        else
        {
            // 지금 장면 안: 주인공(업고 뛰는 사람 · 도망치는 사람 · 쓰러진 사람)을 따라가고, 아니면 가장 뜨거운 곳으로 '훑기' (장면 바꿈이 아니다)
            if (following)
            {
                Aux[sc] += SubjectHeat; FocusCell = sc; FollowTicks++;
            }
            else
            {
                int best = FocusCell; float bm = Heat[FocusCell] + Aux[FocusCell];
                int fx0 = L.X(FocusCell), fy0 = L.Y(FocusCell);
                for (int y = Math.Max(0, fy0 - SpotRadius); y <= Math.Min(L.H - 1, fy0 + SpotRadius); y++)
                    for (int x = Math.Max(0, fx0 - SpotRadius); x <= Math.Min(L.W - 1, fx0 + SpotRadius); x++)
                    {
                        int i = L.Index(x, y);
                        if (Heat[i] + Aux[i] > bm * 1.2f) { bm = Heat[i] + Aux[i]; best = i; }
                    }
                FocusCell = best;
            }
        }
        Frame(max);
    }

    /// <summary>확대: 관심이 큰 칸들의 넓이에 맞춘다.</summary>
    private void Frame(float max)
    {
        int minX = L.X(FocusCell), maxX = minX, minY = L.Y(FocusCell), maxY = minY;
        float cut = max * 0.1f;
        int fx = L.X(FocusCell), fy = L.Y(FocusCell);
        for (int y = Math.Max(0, fy - 20); y <= Math.Min(L.H - 1, fy + 20); y++)
            for (int x = Math.Max(0, fx - 30); x <= Math.Min(L.W - 1, fx + 30); x++)
                if (Heat[L.Index(x, y)] + Aux[L.Index(x, y)] > cut) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        Zoom = Math.Clamp(Math.Max(maxX - minX, (maxY - minY) * 2) + 10, 14, 64);
    }

    private bool Far(int a, int b) => Math.Max(Math.Abs(L.X(a) - L.X(b)), Math.Abs(L.Y(a) - L.Y(b))) > SpotRadius;

    // ───────────────────────── 시간 ─────────────────────────

    /// <summary>조짐은 장면 단위: 가벼운 알아챔 · 외침은 카메라만 (관심은 이미 더했다 — 큰 배에서 하나마다 3분씩 늦어 하루가 72~131분이던 것) ·
    /// 같은 뿌리는 열린 창을 다시 늘리지 않는다.</summary>
    private void OmenSeen(in Fact f, float w, int root, long t)
    {
        if (f.Kind is FactKind.Noticed or FactKind.Warned) return;
        if (Heavy(f)) _lastHeavyOmen = t;
        if (root != 0 && root == _omenRoot && t - _lastOmen < Time.Minutes(3)) return;
        _lastOmen = t; _omenRoot = root;
    }
    private static bool Heavy(in Fact f) => f.Kind is FactKind.Blamed or FactKind.Sensed or FactKind.ComputerAlarm or FactKind.WallCrack or FactKind.Flare or FactKind.DebrisNear
        || f.Kind == FactKind.Talk && (TalkEnd)(int)f.Size == TalkEnd.Argue;

    private void Pace(long t, double screen)
    {
        if (DayMinutes > 0)
        {
            long day = t / Time.TicksPerDay;
            if (day != _budgetDay) { _budgetDay = day; _screenToday = 0; _slowToday = 0; }
            _screenToday += screen;
        }
        if (_criticalLeft > 0) { _criticalLeft -= screen; Speed = CriticalSpeed; Mood = "결정적 순간"; CriticalScreenSec += screen; return; }   // 예산 밖
        bool budgetLeft = DayMinutes <= 0 || _slowToday < SlowShare * DayMinutes * 60;
        if (_propShowLeft > 0 && budgetLeft) { _propShowLeft--; Speed = PropShowSpeed; Mood = "볼거리"; PropScreenSec += screen; _slowToday += screen; return; }   // 물체 볼거리 (PH12 — 하루 느린 예산 안)
        if (t - _lastDanger < Time.Minutes(1)) { Speed = DangerSpeed; Mood = "위험"; DangerScreenSec += screen; _slowToday += screen; return; }
        if (t - _lastOmen < Time.Minutes(3) && (budgetLeft || t - _lastHeavyOmen < Time.Minutes(1))) { Speed = OmenSpeed; Mood = "조짐"; OmenScreenSec += screen; _slowToday += screen; return; }
        Speed = DayMinutes > 0 ? CalmNow(t) : CalmSpeed; Mood = "조용함"; CalmScreenSec += screen;
    }

    // ───────────────────────── 물체 볼거리 (docs/PHYSICS.md PH12) ─────────────────────────

    /// <summary>물체 볼거리 빠르기 (화면 1초에 1틱 — 실제의 2.4배) · 한 번에 많아야 3틱 · 깨어 움직이는 물체의 운동 에너지 합 문턱 (J) · 사람 곁에서 깨진 것의 무게.</summary>
    public const double PropShowSpeed = 1, PropShowJ = 20;
    public const int PropShowTicks = 3;
    public const float ShatterNearWeight = 15;
    public double PropScreenSec;
    /// <summary>물체 볼거리를 켠다 (시험이 끈 연출과 견준다).</summary>
    public bool PropShowOn = true;
    private int _propShowLeft;
    private bool _propNew, _propHot;

    private static bool PropFact(FactKind k) => k is FactKind.Shattered or FactKind.Toppled or FactKind.ItemFell or FactKind.TornLoose;

    /// <summary>칸 c 곁 (두 칸 안)에 산 사람이 있나.</summary>
    private bool PersonNear(Mind? mind, Life? life, int c)
    {
        if (mind == null || life == null || c < 0) return false;
        foreach (var a in mind.Agents)
        {
            if (!a.IsPerson || a.C.Cell < 0 || life.Bodies[a.C.Body].Alive == 0) continue;
            if (Math.Max(Math.Abs(L.X(a.C.Cell) - L.X(c)), Math.Abs(L.Y(a.C.Cell) - L.Y(c))) <= 2) return true;
        }
        return false;
    }

    /// <summary>
    /// 카메라 안에 새 물체 사실 (깨졌다 · 넘어졌다 · 떨어졌다 · 뜯겨 나왔다)이 났거나 · 깨어 움직이는 물체의 운동 에너지 합이 20J를 막 넘었으면 3틱을 화면 1초에 1틱으로 (월드 값만 읽는다).
    /// </summary>
    private void PropShow(Sim sim)
    {
        bool hot = false;
        if (sim.Find<Structure>() is { } st && st.Props.Count > 0)
        {
            double e = 0;
            foreach (var p in st.Props)
            {
                if (!Structure.Awake(p) || p.Kg <= 0) continue;
                int c = st.CellOf(p);
                if (c < 0 || !InView(c)) continue;
                e += 0.5 * p.Kg * (p.Vx * p.Vx + p.Vy * p.Vy + p.Vz * p.Vz) + 0.5 * p.Inertia * p.W * p.W;
            }
            hot = e >= PropShowJ;
        }
        if (PropShowOn && (_propNew || hot && !_propHot)) _propShowLeft = PropShowTicks;
        _propNew = false; _propHot = hot;
    }

    /// <summary>하루 예산의 조용함 빠르기: 남은 틱 ÷ 남은 화면 초 — 조용한 날은 꼭 목표만큼 · 조짐 · 위험에 쓴 만큼 남은 조용함이 빨라진다 (기계가 못 따라가면 넘는다 — 위 줄이 정직하게 보인다).</summary>
    private double CalmNow(long t)
    {
        long left = (_budgetDay + 1) * Time.TicksPerDay - t;
        double screenLeft = Math.Max(1, DayMinutes * 60 - _screenToday);
        return Math.Clamp(left / screenLeft, CalmFloor, CalmCeiling);
    }

    // ───────────────────────── 하이라이트 ─────────────────────────


    /// <summary>관찰용: 시간마다 가장 높았던 하이라이트 후보 (문턱에 못 미친 것도) — 평가 도구가 읽는다.</summary>
    public readonly List<(float spot, FactKind kind)> HourBest = new();

    private void MaybeHighlight(Sim sim, in Fact f, long t)
    {
        float spot = SpotScore(f.Cell);
        int hour = (int)(t / Time.TicksPerHour);
        while (HourBest.Count <= hour) HourBest.Add((0, FactKind.None));
        if (spot > HourBest[hour].spot) HourBest[hour] = (spot, f.Kind);
        if (spot < HighlightAt) return;   // 문턱은 고정 — 볼 만한 일은 세계가 저절로 만든다 (연출이 조용한 날 문턱을 낮추지 않는다 · 함장 기준)
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
                if (h.Beats == null && root != 0 && Chains.TryGetValue(root, out var hc)) h.Beats = hc.Beats;
                h.Last = Math.Max(h.Last, t);
                return;
            }
        Highlights.Add(new Highlight { Tick = t, Cell = f.Cell, Score = spot, Kind = f.Kind, Cause = f.Cause, Actor = f.Actor, Root = root, Title = TitleOf(sim, f), StartTick = RootTick(sim, f, t), Last = t,
            People = PeopleFact(f.Kind), Line = LineOf(sim, f), Beats = root != 0 && Chains.TryGetValue(root, out var nc) ? nc.Beats : null });
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
    public static string FactText(in Fact f) => FactNames.Of(f);

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
        return story.Length > 0 ? story : who + FactText(f);
    }

    // ───────────────────────── 다시보기 ─────────────────────────

    /// <summary>하이라이트의 시작(첫 조짐) 직전 찍기에서 되살린 세계 — 이어 돌리면 같은 일이 그대로 일어난다. 고리에 없으면 null.</summary>
    public Sim? Replay(Sim sim, Highlight h)
    {
        (long tick, byte[] snap, int len)? pick = null;
        foreach (var r in Ring) if (r.tick <= h.StartTick) pick = r;
        if (pick == null) { if (Ring.Count > 0 && Ring[0].tick <= h.Tick) pick = Ring[0]; else return null; }
        var w = new Sim(sim.Setup);
        w.Load(pick.Value.snap, pick.Value.len);
        return w;
    }

    /// <summary>재미 점수 재료 (Lab --fun).</summary>
    public double CalmShare => ScreenSeconds > 0 ? CalmScreenSec / ScreenSeconds : 1;
}
