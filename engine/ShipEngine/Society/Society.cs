using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>⑨ 이야깃거리 — 사람이 기억하는 일 · 대화의 주제.</summary>
public enum Topic : byte { Small, Work, Fire, Breach, Rescue, Hurt, Death, Blame, Gossip, Shortage, Fight, Heart, Peace }

/// <summary>⑨ 대화의 끝 — 어떻게 흘러갔나.</summary>
public enum TalkEnd : byte { Agree, Laugh, Awkward, Friction, Praise, Comfort, Apology, Argue, Spread, Defend, Worry, Accept, Reject }

/// <summary>사람 하나가 기억하는 일 (사람마다 많아야 12 · 닷새에 걸쳐 흐려진다).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Memory
{
    public int Owner;      // 기억하는 사람 (에이전트)
    public long Tick;
    public Topic Topic;
    public int About;      // 그 일의 사람 (구한 이 · 죽은 이 · 탓하는 이 · −1)
    public int Cause;      // 그 일의 원인 마디
    public float Weight;   // 얼마나 크게 남았나 (0~1)
    public int Cell;       // 그 일이 일어난 곳
}

/// <summary>한 마디 (화면의 말풍선 · 기록) — 글은 원인 마디의 글로 남는다.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Saying
{
    public long Tick;
    public int Speaker, Listener;
    public Topic Topic;
    public TalkEnd End;
    public int Line;       // 원인 마디 (글 = 말)
    public int Cell;
}

/// <summary>몸싸움 하나 (때리는 이 · 맞는 이 · 끝장을 보려는가).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Brawl
{
    public int A, B;
    public long Start, Until;
    public byte Lethal;
    public int Cause;
    public int Blows;
}

/// <summary>
/// ⑨ 사회 (docs/ENGINE.md §4 ⑨) — 사람 사이의 그물. 줄거리를 짜지 않는다: 함께 보낸 시간 · 함께 넘긴 위기 · 구해 준 일 · 죽음 · 남의 실수가
/// 정 · 믿음 · 원한 · 빚 · 기억을 바꾸고, 기억은 대화의 주제가 되고, 대화는 다시 관계를 바꾼다 (위로 · 사과 · 다툼 · 소문).
/// 원한이 깊고 성격이 거칠고 몰리면 다툼 → 피하기 → 몰래 방해 → 몸싸움 → 죽음까지 저절로 오른다 (함장 결정 — 드물게).
/// 마음(⑧)은 정 · 원한을 읽어 친구를 먼저 구하고 · 미운 이가 있는 방을 피하고 · 몸싸움에 휘말린다.
/// </summary>
public sealed partial class Society : IEngine
{
    public string Name => "사회";
    public Phase Phase => Phase.Decide;
    public int Every => 1;
    public int Offset => 0;

    public const int TogetherEvery = 25;          // 1분마다 함께 있음을 센다
    public const float FriendAt = 0.5f, CloseAt = 0.8f, GrudgeAt = 0.3f;
    public const int MemoriesEach = 12;
    public const double MemoryDays = 5;           // 기억이 흐려지는 빠르기 (e로 줄어드는 날)

    private readonly Layout L;
    private Mind? _mind;
    private Life? _life;
    private Network? _net;
    private TalkTable? _talk;

    /// <summary>정 [나 × 너] (−1 ~ 1) — 나에게서 너로 (구해 준 사람에게 진 빚은 한쪽만 크다).</summary>
    public float[] Bond = Array.Empty<float>();
    /// <summary>믿음 [나 × 너] (0 ~ 1, 처음 0.5) — 그의 말(소문)을 얼마나 믿나 · 그에게 맡기나.</summary>
    public float[] TrustOf = Array.Empty<float>();
    /// <summary>원한 [나 × 너] (0 ~ 1) — 그의 탓으로 내가 · 내 사람이 다쳤다 · 다퉜다.</summary>
    public float[] Grudge = Array.Empty<float>();
    /// <summary>빚 [나 × 너] (0 ~ 1) — 그가 나를 살렸다.</summary>
    public float[] Debt = Array.Empty<float>();
    /// <summary>아는 정도 [나 × 너] (0 ~ 1) — 함께 지낸 시간 (낯선 사이는 말을 덜 건다).</summary>
    public float[] Known = Array.Empty<float>();
    /// <summary>마지막으로 말을 나눈 때 [나 × 너] — 한 번 이야기한 둘은 한동안 (45분) 다시 말을 걸지 않는다.</summary>
    public long[] LastTalk = Array.Empty<long>();
    public const int TalkRest = 25 * 45;
    /// <summary>한 방에서 말이 닿는 거리 (칸 — 조용한 방에서 6m 남짓이면 말을 건다).</summary>
    public const int TalkReach = 6;
    /// <summary>사람마다 주컴퓨터를 믿는 정도 (0 ~ 1, 처음 0.8).</summary>
    public float[] TrustComputer = Array.Empty<float>();
    /// <summary>끌림 [나 × 너] (0 ~ 1) — 함께 쉬는 시간에 자란다 (둘 사이의 맞음 · 성격) · 떨어져 지내면 열흘에 반쯤 흐려진다. 한쪽만일 수 있다.</summary>
    public float[] Attract = Array.Empty<float>();
    /// <summary>짝 (−1: 없음) — 고백을 받아 주면 맺고 · 원한이 쌓이거나 정이 식으면 헤어진다.</summary>
    public int[] Partner = Array.Empty<int>();
    public const float ConfessAt = 0.6f, AcceptAt = 0.4f;
    /// <summary>사람마다 슬픔 (0 ~ 1) — 하루 반감.</summary>
    public float[] Grief = Array.Empty<float>();
    public List<Memory> Memories = new();
    public List<Saying> Sayings = new();         // 최근 128마디
    public List<Brawl> Brawls = new();           // 지금 벌어지는 몸싸움
    public int N;                                  // 배열이 다루는 에이전트 수
    public long Friendships, Griefs, TrustLosses, Talks, Arguments, Apologies, Rumors, Blames, Fights, Sabotages, Killings, Couples, Rejections, Breakups, PilferSeen;
    private long _cursor;
    private readonly List<(long tick, int room, int cause)> _sealedIn = new(); // 사람이 안에 있는데 봉쇄된 방 (그 뒤 죽음을 컴퓨터 탓으로)

    public Society(Layout layout) { L = layout; }

    public float BondOf(int a, int b) => a >= 0 && b >= 0 && a < N && b < N ? Bond[a * N + b] : 0;
    public float GrudgeOf(int a, int b) => a >= 0 && b >= 0 && a < N && b < N ? Grudge[a * N + b] : 0;
    public float TrustIn(int a, int b) => a >= 0 && b >= 0 && a < N && b < N ? TrustOf[a * N + b] : 0.5f;
    public float DebtOf(int a, int b) => a >= 0 && b >= 0 && a < N && b < N ? Debt[a * N + b] : 0;
    public float KnownOf(int a, int b) => a >= 0 && b >= 0 && a < N && b < N ? Known[a * N + b] : 0;
    public float Trust(int a) => a >= 0 && a < N ? TrustComputer[a] : 1;
    public float GriefOf(int a) => a >= 0 && a < N ? Grief[a] : 0;
    public float AttractOf(int a, int b) => a >= 0 && b >= 0 && a < N && b < N ? Attract[a * N + b] : 0;
    public int PartnerOf(int a) => a >= 0 && a < N ? Partner[a] : -1;
    /// <summary>둘 사이의 맞음 (0 ~ 1 · 짝마다 정해진 값 — 같은 시드면 같다): 왜 그 사람인지는 설명되지 않는 몫.</summary>
    public float Spark(Sim sim, int a, int b) => (float)Rng.Unit(sim.Seed, Rng.Key("끌림"), Math.Min(a, b), Math.Max(a, b));
    /// <summary>마음이 고를 때 쓰는 끌림: 정 − 원한 (미운 이가 있는 방은 피한다).</summary>
    public float Liking(int a, int b) => BondOf(a, b) - GrudgeOf(a, b);

    /// <summary>그 사람이 지금 몸싸움 중인가 (상대 · 때리는 쪽인가).</summary>
    public bool InBrawl(int agent, out int foe, out bool attacker)
    {
        foreach (var b in Brawls)
        {
            if (b.A == agent) { foe = b.B; attacker = true; return true; }
            if (b.B == agent) { foe = b.A; attacker = false; return true; }
        }
        foe = -1; attacker = false;
        return false;
    }

    /// <summary>그 사람이 마지막으로 한 말 (없으면 null) — 화면의 말풍선.</summary>
    public string? LastLine(Sim sim, int agent, long within)
    {
        for (int i = Sayings.Count - 1; i >= 0; i--)
        {
            var s = Sayings[i];
            if (sim.Tick - s.Tick > within) break;
            if (s.Speaker == agent) return sim.Causes.Text(s.Line);
        }
        return null;
    }

    private void Resize(int n)
    {
        if (n == N) return;
        float[] Grow(float[] old, float init)
        {
            var a = new float[n * n];
            if (init != 0) Array.Fill(a, init);
            for (int i = 0; i < Math.Min(n, N); i++) for (int j = 0; j < Math.Min(n, N); j++) a[i * n + j] = old[i * N + j];
            return a;
        }
        var b = Grow(Bond, 0); var tr = Grow(TrustOf, 0.5f); var gr = Grow(Grudge, 0); var de = Grow(Debt, 0); var kn = Grow(Known, 0); var at = Grow(Attract, 0);
        var pa = Enumerable.Repeat(-1, n).ToArray();
        for (int i = 0; i < Math.Min(n, N); i++) pa[i] = Partner[i];
        var t = new float[n]; var g = new float[n];
        for (int i = 0; i < n; i++) t[i] = 0.8f;
        for (int i = 0; i < Math.Min(n, N); i++) { t[i] = TrustComputer[i]; g[i] = Grief[i]; }
        var lt = new long[n * n];
        Array.Fill(lt, -TalkRest);
        for (int i = 0; i < Math.Min(n, N); i++) for (int j = 0; j < Math.Min(n, N); j++) lt[i * n + j] = LastTalk[i * N + j];
        Bond = b; TrustOf = tr; Grudge = gr; Debt = de; Known = kn; LastTalk = lt; TrustComputer = t; Grief = g; Attract = at; Partner = pa; _near = new byte[n * n]; N = n;
    }

    public void Step(Sim sim, double dt)
    {
        _mind ??= sim.Find<Mind>(); _life ??= sim.Find<Life>(); _net ??= sim.Find<Network>();
        if (_mind == null || _life == null) return;
        _talk ??= TalkTable.Load();
        Resize(_mind.Agents.Count);
        Events(sim);
        if (sim.Tick % TogetherEvery == 0)
        {
            Together(sim, TogetherEvery * dt);
            Hearts(sim, TogetherEvery * dt);
            BrawlStep(sim);
            if (sim.Tick % (TogetherEvery * 30) == 0) Mischief(sim);
        }
        if (sim.Tick % Time.TicksPerHour == 0) FormBands(sim);
    }

    private bool Person(int i) => i >= 0 && i < N && _mind!.Agents[i].IsPerson && _mind.Agents[i].C.Up != 0 && _life![_mind.Agents[i].C.Body].Alive != 0;
    private bool Awake(int i) => Person(i) && _life![_mind!.Agents[i].C.Body].Conscious != 0 && _life[_mind.Agents[i].C.Body].Asleep == 0;
    private ref AgentCore C(int i) => ref _mind!.Agents[i].C;
    private float Rnd(Sim sim, string key, long a, long b = 0) => (float)Rng.Unit(sim.Seed, Rng.Key(key), sim.Tick * 131 + a, b);

    private void Add(float[] m, int a, int b, float d, float lo = 0, float hi = 1) { int k = a * N + b; m[k] = Math.Clamp(m[k] + d, lo, hi); }

    // ───────────────────────── 함께 보낸 시간 ─────────────────────────

    private void Together(Sim sim, double dt)
    {
        var ag = _mind!.Agents;
        double decay = Math.Exp(-dt * 0.6931 / 86400);  // 슬픔은 하루 반감
        for (int i = 0; i < N; i++) Grief[i] = (float)(Grief[i] * decay);
        for (int i = 0; i < N; i++)
        {
            var a = ag[i];
            if (!a.IsPerson || a.C.Up == 0) continue;
            if (!Leisure(a.C.Goal)) continue;
            for (int j = 0; j < N; j++)
            {
                if (j == i) continue;
                var b = ag[j];
                if (!b.IsPerson || b.C.Up == 0) continue;
                if (!Leisure(b.C.Goal)) continue;
                if (_mind.RoomOf(a.C.Cell) != _mind.RoomOf(b.C.Cell) || _mind.Dist(a.C.Cell, b.C.Cell) > TalkReach) continue;
                Add(Known, i, j, (float)(0.02 * dt / 3600));
                // 끌림: 함께 쉬는 시간에 · 짝이 없거나 그 사람이 짝일 때 — 맞음 × (열림이 비슷할수록 · 상대가 다정할수록) · 시간당 많아야 0.04
                if ((Partner[i] < 0 || Partner[i] == j) && GrudgeOf(i, j) < GrudgeAt)
                {
                    float chem = Spark(sim, i, j) * (0.6f + 0.4f * (1 - Math.Abs(a.C.Open - b.C.Open))) * (0.7f + 0.3f * b.C.Agree);
                    Add(Attract, i, j, (float)(0.04 * chem * chem * dt / 3600));
                    _near[i * N + j] = 1;
                }
                // 어울림: 시간당 약 0.05 × 맞음 (성격이 비슷하고 둘 다 다정하면 빨리) — 미운 사이는 같이 있어도 정이 쌓이지 않는다
                if (GrudgeOf(i, j) < GrudgeAt)
                {
                    float fit = 1 - Math.Abs(a.C.Sociable - b.C.Sociable) * 0.5f + (a.C.Kindness + b.C.Kindness) * 0.25f;
                    float talk = a.C.Goal == GoalKind.Socialize || b.C.Goal == GoalKind.Socialize ? 1.5f : 0.7f;
                    Raise(sim, i, j, (float)(0.05 * fit * talk * dt / 3600), 0, "함께 지낸 시간");
                }
                // 말을 건다 (한 쌍에 한 번 — 앞 번호가 센다): 분당 약 3% × 외향 × 아는 정도
                if (i < j && Awake(i) && Awake(j) && sim.Tick - LastTalk[i * N + j] >= TalkRest)
                {
                    float p = 0.03f * (0.5f + Math.Max(a.C.Extra, b.C.Extra)) * (a.C.Goal == GoalKind.Socialize || b.C.Goal == GoalKind.Socialize ? 1.6f : 1) * (0.4f + KnownOf(i, j));
                    if (Rnd(sim, "말 걸기", i, j) < p) Talk(sim, i, j);
                }
                // 원한이 깊은 둘이 마주쳤다 — 화가 난 쪽이 먼저 덤빌 수 있다
                if (GrudgeOf(i, j) > 0.5f && C(i).Anger > 0.5f) Escalate(sim, i, j, 0.2f, 0);
            }
        }
    }

    private byte[] _near = Array.Empty<byte>();

    /// <summary>끌림이 흐려지고 (함께 쉬지 않은 쌍 — 열흘 반감) · 짝이 헤어진다 (원한 0.45 넘거나 정 0.15 아래) — 1분마다 (함께 보낸 시간 뒤).</summary>
    private void Hearts(Sim sim, double dt)
    {
        float fade = (float)Math.Exp(-dt * 0.6931 / (10 * 86400));
        for (int k = 0; k < N * N; k++) { if (_near[k] == 0) Attract[k] *= fade; _near[k] = 0; }
        for (int i = 0; i < N; i++)
        {
            int j = Partner[i];
            if (j < 0 || j < i) continue;
            if (!Person(i) || !Person(j)) { if (!Person(i) && !Person(j) || !Alive(i) || !Alive(j)) { Partner[i] = Partner[j] = -1; } continue; }
            if (Math.Max(GrudgeOf(i, j), GrudgeOf(j, i)) > 0.45f || Math.Min(BondOf(i, j), BondOf(j, i)) < 0.15f) BreakUp(sim, i, j);
        }
    }

    private bool Alive(int i) => i >= 0 && i < N && _mind!.Agents[i].IsPerson && _life![_mind.Agents[i].C.Body].Alive != 0;

    private void BreakUp(Sim sim, int i, int j)
    {
        Partner[i] = Partner[j] = -1; Breakups++;
        Attract[i * N + j] *= 0.3f; Attract[j * N + i] *= 0.3f;
        foreach (int x in new[] { i, j }) { ref var c = ref C(x); c.Sad = Math.Min(1, c.Sad + 0.35f); c.Stress = Math.Min(1, c.Stress + 0.15f); }
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.Gwa(_mind!.NameOf(i))} {Ko.I(_mind.NameOf(j))} 헤어졌다 — {(Math.Max(GrudgeOf(i, j), GrudgeOf(j, i)) > 0.45f ? "쌓인 원망을 넘지 못했다" : "마음이 식었다")}", sim.Tick, C(i).Cell, 0, 0, i);
        sim.Facts.Emit(sim.Tick, FactKind.Relationship, C(i).Cell, -1, cause, i, j);
        Remember(sim, i, Topic.Heart, j, cause, 0.8f, C(i).Cell); Remember(sim, j, Topic.Heart, i, cause, 0.8f, C(j).Cell);
    }

    /// <summary>함께 쉬는 때 (어울림 · 쉼 · 밥 · 빈둥거림) — 일하거나 자거나 위급할 때는 정이 쌓이지 않는다.</summary>
    private static bool Leisure(GoalKind g) => g is GoalKind.Socialize or GoalKind.Relax or GoalKind.Eat or GoalKind.Idle or GoalKind.Exercise;

    /// <summary>정을 바꾼다 — 오를 때는 문턱을 넘으면 '가까워졌다'가 남는다.</summary>
    private void Warm(Sim sim, int a, int b, float delta, string why = "함께 겪은 일")
    {
        if (delta > 0) Raise(sim, a, b, delta, 0, why);
        else Add(Bond, a, b, delta, -1, 1);
    }

    /// <summary>정을 더하고 문턱을 넘으면 사실로 남긴다.</summary>
    private void Raise(Sim sim, int a, int b, float delta, int cause, string why)
    {
        int k = a * N + b;
        float before = Bond[k];
        Bond[k] = Math.Clamp(before + delta, -1, 1);
        if (before < FriendAt && Bond[k] >= FriendAt && BondOf(b, a) >= FriendAt * 0.8f)
        {
            Friendships++;
            int c = sim.Causes.Add(CauseKind.Person, $"{Ko.Gwa(_mind!.NameOf(a))} {Ko.I(_mind.NameOf(b))} 가까워졌다 — {why}", sim.Tick, _mind.Agents[a].C.Cell, cause, 0, a);
            sim.Facts.Emit(sim.Tick, FactKind.Relationship, _mind.Agents[a].C.Cell, Bond[k], c, a, b);
        }
    }

    // ───────────────────────── 기억 ─────────────────────────

    private float Weight(in Memory m, long now) => m.Weight * (float)Math.Exp(-(now - m.Tick) * Time.SecondsPerTick / (MemoryDays * 86400));

    /// <summary>기억을 남긴다 — 같은 원인의 기억이 있으면 무게만 키우고, 자리가 없으면 가장 흐린 것을 지운다.</summary>
    public void Remember(Sim sim, int owner, Topic topic, int about, int cause, float weight, int cell)
    {
        if (!Person(owner)) return;
        int n = 0, weakest = -1; float wmin = float.MaxValue;
        for (int i = 0; i < Memories.Count; i++)
        {
            var m = Memories[i];
            if (m.Owner != owner) continue;
            n++;
            if (m.Topic == topic && m.Cause == cause && m.About == about)
            {
                m.Weight = Math.Min(1, Weight(m, sim.Tick) + weight * 0.5f); m.Tick = sim.Tick; Memories[i] = m;
                return;
            }
            float w = Weight(m, sim.Tick);
            if (w < wmin) { wmin = w; weakest = i; }
        }
        var mem = new Memory { Owner = owner, Tick = sim.Tick, Topic = topic, About = about, Cause = cause, Weight = Math.Clamp(weight, 0, 1), Cell = cell };
        if (n >= MemoriesEach && weakest >= 0) Memories[weakest] = mem;
        else Memories.Add(mem);
    }

    private static bool ChainHas(Sim sim, int cause, CauseKind kind)
    {
        foreach (int id in sim.Causes.Chain(cause)) if (sim.Causes[id].Kind == kind) return true;
        return false;
    }

    /// <summary>원인 사슬에서 사람의 행동을 찾는다 (뿌리 쪽부터) — 탓할 사람.</summary>
    public int Culprit(Sim sim, int cause, int except)
    {
        foreach (int id in sim.Causes.Chain(cause))
        {
            var n = sim.Causes[id];
            if (n.Kind == CauseKind.Person && n.Actor >= 0 && n.Actor < N && n.Actor != except && _mind!.Agents[n.Actor].IsPerson) return n.Actor;
        }
        return -1;
    }

    /// <summary>탓: a가 x를 원망한다 (다정한 사람은 덜) · 덜 믿는다.</summary>
    private void Blame(Sim sim, int a, int x, float w, int cause, int cell)
    {
        if (a == x || !Person(a) || x < 0) return;
        float before = GrudgeOf(a, x);
        Add(Grudge, a, x, w * (0.5f + 0.5f * (1 - C(a).Agree)));
        Add(TrustOf, a, x, -w * 0.3f);
        Warm(sim, a, x, -w * 0.2f);
        Remember(sim, a, Topic.Blame, x, cause, w, cell);
        if (before < GrudgeAt && GrudgeOf(a, x) >= GrudgeAt)
        {
            Blames++;
            int c = sim.Causes.Add(CauseKind.Person, $"{Ko.I(_mind!.NameOf(a))} {_mind.NameOf(x)} 탓을 한다", sim.Tick, cell, cause, 0, a);
            sim.Facts.Emit(sim.Tick, FactKind.Blamed, cell, GrudgeOf(a, x), c, a, x);
        }
    }

    /// <summary>그 칸 곁(같은 방 · 깨어 있는)의 사람들.</summary>
    private List<int> Near(int cell, int except = -1, int dist = 8)
    {
        var list = new List<int>();
        int room = _mind!.RoomOf(cell);
        for (int i = 0; i < N; i++)
            if (i != except && Awake(i) && (room >= 0 && _mind.RoomOf(C(i).Cell) == room || _mind.Dist(C(i).Cell, cell) <= dist)) list.Add(i);
        return list;
    }

    // ───────────────────────── 일어난 일 ─────────────────────────

    private void Events(Sim sim)
    {
        var mind = _mind!;
        foreach (var f in sim.Facts.Since(_cursor))
        {
            switch (f.Kind)
            {
                case FactKind.Rescue when f.Actor >= 0 && f.Other >= 0 && f.Other < N:
                {
                    // 업혀 나온 사람은 구한 이에게 목숨을 빚진다 (로봇에게도) · 본 사람들도 그를 다시 본다
                    int v = f.Other, r = f.Actor;
                    Warm(sim, v, r, 0.45f);
                    Add(Debt, v, r, 0.5f); Add(TrustOf, v, r, 0.2f);
                    if (mind.Agents[r].IsPerson) Warm(sim, r, v, 0.2f);
                    Remember(sim, v, Topic.Rescue, r, f.Cause, 1, f.Cell);
                    if (mind.Agents[r].IsPerson) Remember(sim, r, Topic.Rescue, r, f.Cause, 0.7f, f.Cell);
                    foreach (int o in Near(f.Cell, v))
                        if (o != r) { Warm(sim, o, r, 0.1f); Add(TrustOf, o, r, 0.1f); Remember(sim, o, Topic.Rescue, r, f.Cause, 0.5f, f.Cell); }
                    int c = sim.Causes.Add(CauseKind.Person, $"{Ko.I(mind.NameOf(v))} {mind.NameOf(r)}에게 목숨을 빚졌다", sim.Tick, f.Cell, f.Cause, 0, v);
                    sim.Facts.Emit(sim.Tick, FactKind.Relationship, f.Cell, BondOf(v, r), c, v, r);
                    break;
                }
                case FactKind.Extinguished or FactKind.Patched or FactKind.DoorSealed:
                {
                    // 함께 위기를 넘겼다: 그 방에 함께 있던 사람끼리 가까워지고 · 서로를 더 믿는다 · 함께 기억한다
                    var here = Near(f.Cell);
                    // 불 끄기 · 불이 타는 동안 닫은 문은 '불' 이야기 · 아니면 '구멍' (막기 · 공기 때문에 닫은 문)
                    Topic t = f.Kind == FactKind.Extinguished || ChainHas(sim, f.Cause, CauseKind.Fire) || (sim.Find<Chemistry>()?.BurningCount ?? 0) > 0 ? Topic.Fire : Topic.Breach;
                    int hero = f.Actor >= 0 && f.Actor < N && mind.Agents[f.Actor].IsPerson ? f.Actor : -1;
                    foreach (int i in here)
                    {
                        Remember(sim, i, t, hero, f.Cause, 0.8f, f.Cell);
                        foreach (int j in here)
                            if (i != j && !Shared(i, j, f.Cause)) { Warm(sim, i, j, 0.06f); Add(TrustOf, i, j, 0.05f); Add(Known, i, j, 0.1f); }
                        if (hero >= 0 && hero != i) { Add(TrustOf, i, hero, 0.05f); Warm(sim, i, hero, 0.03f); }
                    }
                    foreach (int i in here) MarkShared(i, f.Cause);
                    break;
                }
                case FactKind.Hurt:
                {
                    int v = mind.AgentOfBody(f.Actor);
                    if (v < 0) break;
                    Remember(sim, v, Topic.Hurt, v, f.Cause, 0.6f, f.Cell);
                    int x = Culprit(sim, f.Cause, v);
                    if (x >= 0)
                    {
                        Blame(sim, v, x, 0.5f, f.Cause, f.Cell);
                        foreach (int o in Near(f.Cell, v)) if (o != x) Blame(sim, o, x, 0.2f + 0.3f * Math.Max(0, BondOf(o, v)), f.Cause, f.Cell);
                    }
                    break;
                }
                case FactKind.Pilfer when f.Actor >= 0 && f.Actor < N:
                {
                    // ⑪ 배급 중 몰래 먹는 것을 봤다: 원망한다 (성실할수록 크게 — 몫을 지킨 사람일수록) · 덜 믿는다 · 기억한다 (따지기 · 험담으로 번진다)
                    int x = f.Actor;
                    foreach (int w in Near(f.Cell, x, 3))
                    {
                        PilferSeen++;
                        Blame(sim, w, x, 0.3f + 0.4f * C(w).Consc, f.Cause, f.Cell);
                        Add(TrustOf, w, x, -0.15f);
                    }
                    break;
                }
                case FactKind.ComputerSeal when f.Size > 0:
                    _sealedIn.Add((f.Tick, f.Other, f.Cause));
                    if (_sealedIn.Count > 32) _sealedIn.RemoveAt(0);
                    break;
                case FactKind.Died:
                {
                    int dead = mind.AgentOfBody(f.Actor);
                    if (dead < 0) break;
                    int x = Culprit(sim, f.Cause, dead);
                    var witnesses = Near(f.Cell, dead);
                    // 슬픔: 배에 함께 탄 사람 0.15 + 정 · 모두가 기억한다 · 탓할 사람이 있으면 (본 사람과 가까웠던 이가) 그를 원망한다
                    for (int i = 0; i < N; i++)
                    {
                        var p = mind.Agents[i];
                        if (i == dead || !p.IsPerson || _life![p.C.Body].Alive == 0) continue;
                        float bond = Math.Max(0, BondOf(i, dead));
                        float g = Math.Clamp(0.15f + bond, 0, 1);
                        if (g > Grief[i]) Grief[i] = g;
                        p.C.Sad = Math.Max(p.C.Sad, g);
                        p.C.Stress = Math.Min(1, p.C.Stress + g * 0.5f);
                        Remember(sim, i, Topic.Death, dead, f.Cause, 0.4f + bond, f.Cell);
                        if (x >= 0 && x != i && (witnesses.Contains(i) || bond > 0.3f)) Blame(sim, i, x, 0.4f + bond, f.Cause, f.Cell);
                        if (BondOf(i, dead) >= FriendAt * 0.6f)
                        {
                            Griefs++;
                            int c = sim.Causes.Add(CauseKind.Person, $"{Ko.I(mind.NameOf(i))} {Ko.Eul(mind.NameOf(dead))} 잃었다", sim.Tick, p.C.Cell, f.Cause, 0, i);
                            sim.Facts.Emit(sim.Tick, FactKind.Grieved, p.C.Cell, g, c, i, dead);
                        }
                    }
                    // 사람이 있는 채 봉쇄된 방에서 30분 안에 죽었다 → 주컴퓨터를 덜 믿는다 (정이 깊을수록 더)
                    int room = mind.RoomOf(f.Cell);
                    foreach (var (tick, sroom, scause) in _sealedIn)
                    {
                        if (sroom != room || f.Tick - tick > Time.Minutes(30)) continue;
                        for (int i = 0; i < N; i++)
                        {
                            var p = mind.Agents[i];
                            if (i == dead || !p.IsPerson || _life![p.C.Body].Alive == 0) continue;
                            TrustComputer[i] = Math.Clamp(TrustComputer[i] - 0.25f - 0.4f * Math.Max(0, BondOf(i, dead)), 0, 1);
                        }
                        TrustLosses++;
                        int c = sim.Causes.Add(CauseKind.Person, $"주컴퓨터가 닫은 문 안에서 {Ko.I(mind.NameOf(dead))} 죽었다 — 사람들이 주컴퓨터를 덜 믿는다", sim.Tick, f.Cell, scause, f.Cause);
                        sim.Facts.Emit(sim.Tick, FactKind.TrustLost, f.Cell, 0, c, dead, -2);
                        break;
                    }
                    break;
                }
            }
        }
        _cursor = sim.Facts.Total;
    }

    // 같은 일을 두 번 셈하지 않게: 사람마다 '함께 넘긴 위기'로 이미 친 원인 (최근 8개 · 찍기에 들어간다)
    private readonly Dictionary<int, int[]> _sharedSeen = new();
    private bool Shared(int i, int j, int cause) => _sharedSeen.TryGetValue(i, out var a) && Array.IndexOf(a, cause) >= 0 && _sharedSeen.TryGetValue(j, out var b) && Array.IndexOf(b, cause) >= 0;
    private void MarkShared(int i, int cause)
    {
        if (!_sharedSeen.TryGetValue(i, out var a)) _sharedSeen[i] = a = new int[8];
        if (Array.IndexOf(a, cause) >= 0) return;
        Array.Copy(a, 0, a, 1, a.Length - 1); a[0] = cause;
    }

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.I32(N); w.Arr(Bond); w.Arr(TrustOf); w.Arr(Grudge); w.Arr(Debt); w.Arr(Known); w.Arr(LastTalk); w.Arr(TrustComputer); w.Arr(Grief);
        w.List(Memories); w.List(Sayings); w.List(Brawls);
        w.I64(Friendships); w.I64(Griefs); w.I64(TrustLosses); w.I64(Talks); w.I64(Arguments); w.I64(Apologies); w.I64(Rumors); w.I64(Blames);
        w.I64(Fights); w.I64(Sabotages); w.I64(Killings); w.I64(_cursor); w.I64(PilferSeen);
        w.Arr(Attract); w.Arr(Partner); w.Arr(_near); w.I64(Couples); w.I64(Rejections); w.I64(Breakups);
        SaveBands(w);
        w.I32(_sealedIn.Count); foreach (var (t, r, c) in _sealedIn) { w.I64(t); w.I32(r); w.I32(c); }
        var keys = _sharedSeen.Keys.OrderBy(k => k).ToList();
        w.I32(keys.Count); foreach (var k in keys) { w.I32(k); w.Arr(_sharedSeen[k]); }
    }

    public void Load(StateReader r)
    {
        N = r.I32(); Bond = r.Arr<float>(); TrustOf = r.Arr<float>(); Grudge = r.Arr<float>(); Debt = r.Arr<float>(); Known = r.Arr<float>(); LastTalk = r.Arr<long>(); TrustComputer = r.Arr<float>(); Grief = r.Arr<float>();
        Memories = r.List<Memory>(); Sayings = r.List<Saying>(); Brawls = r.List<Brawl>();
        Friendships = r.I64(); Griefs = r.I64(); TrustLosses = r.I64(); Talks = r.I64(); Arguments = r.I64(); Apologies = r.I64(); Rumors = r.I64(); Blames = r.I64();
        Fights = r.I64(); Sabotages = r.I64(); Killings = r.I64(); _cursor = r.I64(); PilferSeen = r.I64();
        Attract = r.Arr<float>(); Partner = r.Arr<int>(); _near = r.Arr<byte>(); Couples = r.I64(); Rejections = r.I64(); Breakups = r.I64();
        LoadBands(r);
        _sealedIn.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) { long t = r.I64(); int room = r.I32(); int c = r.I32(); _sealedIn.Add((t, room, c)); }
        _sharedSeen.Clear(); n = r.I32(); for (int i = 0; i < n; i++) { int k = r.I32(); _sharedSeen[k] = r.Arr<int>(); }
    }
}
