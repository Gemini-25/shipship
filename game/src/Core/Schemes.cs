using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ShipSim.Core;

// v18.14 승무원이 스스로 꾸미는 일 (장난 · 몰래 하는 일 · 규칙 어기기 · 어울리기 · 주고받기 · 목소리 내기 · 혼자 하는 일 — 표는 SchemeTable.cs).
// 동기: 성격(장난기 · 손재주 · 가치관) · 욕구(배고픔 · 외로움 · 스트레스) · 지루함(아무 일 없는 항해가 길수록 쌓인다) · 관계(앙금 · 짝) · 겪은 일(떠난 사람 · 원정).
// 흐름: 마음먹는다 → 같이 할 사람을 귓속말로(몰래 하는 일) · 메신저로(모임) 모은다 → 몰래 준비(그 자리에 가서 손을 놀린다 — 남이 오면 손을 멈춘다)
//   → 무르익는다(술이 익는다 · 꽃이 핀다 · 밤 방송 · 판이 벌어진다) → 들킨다(본다 · 냄새 · 소리 · 장부 · 센서 · 입소문 · 불) 또는 그대로 간다
//   → 본 사람의 반응(눈감아 줌 · 같이 함 · 알린다 · 다독임) → 회의 표결(금지냐 정식이냐) · 재판 · 공개 경고 · 함장이 허락하거나 치우게 한다 · 관행이 된다.
// 누가 아나: 사람마다 아는 방식이 다르다 (꾸민 사람 · 귓속말로 듣고 거절한 사람 · 본 사람 · 방송만 들은 사람 · 메신저로 읽은 사람 · 함장이 들은 것).
// 주 컴퓨터: 센서 · 전력 · 장부 대조로 먼저 알 수 있다 — 함장에게 알릴지 · 당사자에게만 귀띔할지 · 지켜볼지는 컴퓨터 성격과 사생활 방침이 정한다.
// 남는 것: 관행(주점의 밤 · 동호회 모임) · 규칙(금지 · 허용) · 관계 · 흔적(벽화 · 화분 · 압수한 통) · 연대기 · 일기.

public enum SchemeStage : byte { Plan, Prep, Live, Vote, Done, Dropped }
public enum KnowHow : byte { Part, Asked, Saw, Heard, Chat, Told, Victim }
public enum Stance : byte { Cover, Join, Report, Laugh, Grudge, Shrug, Soothe }
public enum TraceState : byte { Hidden, Public, Official, Seized, Removed, Kept }

public sealed class Scheme
{
    public int Id { get; init; }
    public SchemeSpec Spec { get; init; } = null!;
    public int Lead { get; init; }
    public List<int> Crew { get; } = new();
    /// <summary>누가 · 어떻게 아나 (없으면 모른다).</summary>
    public SortedDictionary<int, KnowHow> Knows { get; } = new();
    public int RoomId { get; set; } = -1;
    public Cell Spot { get; set; }
    public int Target { get; set; } = -1;
    public SchemeStage Stage { get; set; }
    public float Progress { get; set; }
    /// <summary>무르익은 정도 (술 · 꽃 · 버섯 — 0~1).</summary>
    public float Ripe { get; set; }
    public long Born { get; init; }
    public long Since { get; set; }
    public int Sessions { get; set; }
    public int Turnout { get; set; }
    public int Weak { get; set; }
    public long SessionAt { get; set; } = -1;
    public long SessionEnd { get; set; } = -1;
    public int Finder { get; set; } = -1;
    public string FoundHow { get; set; } = "";
    public long FoundAt { get; set; } = -1;
    public List<(int who, Stance r)> Reactions { get; } = new();
    public int Motion { get; set; } = -1;
    public string Outcome { get; set; } = "";
    public long Ended { get; set; } = -1;
    public bool ComputerKnows { get; set; }
    /// <summary>−1 모름 · 0 지켜봄 · 1 함장에게 알림 · 2 당사자에게만.</summary>
    public int ComputerSaid { get; set; } = -1;
    public string ComputerWhy { get; set; } = "";
    public int Skimmed { get; set; }
    public int SkimSeen { get; set; }
    public int Invite { get; set; } = -1;
    public bool Identified { get; set; } = true;
    public bool Working { get; set; }
    public float SessionHourSet { get; set; } = -1f;
    public long Stopped { get; set; } = -1;
    /// <summary>이번 모임 · 행사에 온 사람.</summary>
    public SortedSet<int> Came { get; } = new();
    public bool Active => Stage is SchemeStage.Plan or SchemeStage.Prep or SchemeStage.Live or SchemeStage.Vote;
    public bool Hiding => Stage is SchemeStage.Prep or SchemeStage.Live;
    public bool Over() => Stage is SchemeStage.Done or SchemeStage.Dropped;
    public bool InSession(long t) => SessionAt >= 0 && t >= SessionAt && t < SessionEnd;
    public bool Knew(int id) => Knows.ContainsKey(id);
    /// <summary>누가 했는지까지 아나 (방송만 들은 사람은 모른다).</summary>
    public bool KnowsWho(int id) => Knows.TryGetValue(id, out var k) && k != KnowHow.Heard;
}

public sealed class Practice
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public int Scheme { get; init; } = -1;
    public int RoomId { get; set; } = -1;
    public int Every { get; init; } = 3;
    public float Hour { get; init; } = 20f;
    public float Length { get; init; } = 1.5f;
    public long Born { get; init; }
    public string Origin { get; init; } = "";
    public long Start { get; set; }
    public long Until { get; set; }
    public int Held { get; set; }
    public int LastTurnout { get; set; }
    public List<int> Followers { get; } = new();
    public SortedSet<int> Came { get; } = new();
    public bool Opened { get; set; }
    public bool Now(long t) => t >= Start && t < Until;
}

public sealed class SchemeTrace
{
    public int Id { get; init; }
    public string Key { get; init; } = "";
    public int Scheme { get; init; } = -1;
    public int RoomId { get; init; } = -1;
    public Cell At { get; init; }
    public int Owner { get; init; } = -1;
    public long Tick { get; set; }
    public TraceState State { get; set; }
    public string Text { get; set; } = "";
}

public sealed class Debt
{
    public int From { get; init; }
    public int To { get; init; }
    public int Amount { get; set; }
    public long Since { get; init; }
    public long Fought { get; set; } = -1;
    public int Fights { get; set; }
    public string Why { get; init; } = "";
    public int Scheme { get; init; } = -1;
}

public sealed class ShipRule
{
    public string Key { get; init; } = "";
    public string Text { get; init; } = "";
    public bool Allowed { get; init; }
    public long Tick { get; init; }
    public int Motion { get; init; } = -1;
}

public sealed class SchemeStats
{
    public int Started, Dropped, Found, Covered, Joined, Reported, Soothed, Pranks, Laughed, Grudges, Votes, Legit, Banned, Trials, Grievances,
        Adopted, Removed, Kept, Practices, Events, Sessions, ComputerFound, ComputerTold, ComputerQuiet, ComputerWhisper, Debts, Quarrels, Heard, Fires, Smelled, Hid;
    public string Line() =>
        $"꾸밈 {Started} (접음 {Dropped}) · 들킴 {Found} (눈감음 {Covered} · 같이 함 {Joined} · 알림 {Reported} · 다독임 {Soothed}) · 장난 {Pranks} (웃음 {Laughed} · 앙금 {Grudges}) · " +
        $"표결 {Votes} (정식 {Legit} · 금지 {Banned}) · 재판 {Trials} · 경고 {Grievances} · 공용 {Adopted} · 치움 {Removed} · 그대로 {Kept} · 관행 {Practices} · 행사 {Events} · 모임 {Sessions} · " +
        $"컴퓨터 알아챔 {ComputerFound} (함장에게 {ComputerTold} · 당사자에게 {ComputerWhisper} · 지켜봄 {ComputerQuiet}) · 빚 {Debts} · 다툼 {Quarrels} · 냄새로 {Smelled} · 불 {Fires} · 손 멈춤 {Hid}";
}

public sealed partial class SchemeSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7027 + 1913));

    public SchemeSystem(World w) => _w = w;

    /// <summary>시험용: 끄면 아무도 일을 꾸미지 않는다.</summary>
    public static bool Off { get; set; }
    /// <summary>시험용: 저절로 마음먹는 일을 막는다 (장면을 손으로 짤 때).</summary>
    public bool NoMotives { get; set; }
    public static long UpdateTicks;

    public List<Scheme> All { get; } = new();
    public List<Practice> Practices { get; } = new();
    public List<SchemeTrace> Traces { get; } = new();
    public List<Debt> Debts { get; } = new();
    public List<ShipRule> Rules { get; } = new();
    public SchemeStats Stats { get; } = new();
    /// <summary>배에서 해 본 일 (종류별 횟수).</summary>
    public SortedDictionary<string, int> Tried { get; } = new();
    private readonly SortedDictionary<int, float> _bored = new();
    private readonly SortedDictionary<int, long> _ledAt = new();
    private readonly SortedDictionary<int, int> _suspect = new(); // 사람 → 확인하러 갈 일 (냄새 · 소리)
    private readonly Dictionary<int, int> _motionOf = new(); // 안건 → 꾸민 일 (찾기만 한다)
    private readonly Dictionary<int, List<CrewMember>> _inRoom = new();
    private long _nextMin, _nextHour, _lastDull;
    private int _nextId = 1, _traceId = 1, _lastDay = -1;
    /// <summary>안내 방송 목소리가 바뀐 동안 (장난).</summary>
    public long VoiceUntil { get; private set; } = -1;

    public Scheme? Get(int id) { foreach (var s in All) if (s.Id == id) return s; return null; }
    public IEnumerable<Scheme> Open => All.Where(s => s.Active);
    public ShipRule? Rule(string key) { for (int i = Rules.Count - 1; i >= 0; i--) if (Rules[i].Key == key) return Rules[i]; return null; }
    public Practice? PracticeOf(string key) => Practices.FirstOrDefault(p => p.Key == key);
    public float Bored(CrewMember c) => _bored.TryGetValue(c.Id, out var b) ? b : 0.15f + (c.Id * 7 % 10) / 40f;
    public void SetBored(CrewMember c, float v) => _bored[c.Id] = Math.Clamp(v, 0f, 1f);
    public Scheme? LeadOf(CrewMember c) => All.FirstOrDefault(s => s.Active && s.Lead == c.Id);
    public IEnumerable<Scheme> In(CrewMember c) => All.Where(s => s.Active && s.Crew.Contains(c.Id));
    public IEnumerable<Scheme> KnownBy(CrewMember c) => All.Where(s => s.Knew(c.Id));
    public Room? RoomOf(Scheme s) => s.RoomId >= 0 && s.RoomId < _w.Ship.Rooms.Count ? _w.Ship.Rooms[s.RoomId] : null;
    private CrewMember? P(int id) { if (id < 0) return null; foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }
    private static bool Adult(CrewMember c) => !c.Dead && !(c.IsChild && c.Age < 12f);

    // ───────────────────────────── 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        long t0 = Stopwatch.GetTimestamp();
        if (w.Tick >= _nextMin) { _nextMin = w.Tick + SimTime.Minutes(2); Minute(); }
        if (w.Tick >= _nextHour) { _nextHour = w.Tick + SimTime.Hours(1); Hour(); }
        UpdateTicks += Stopwatch.GetTimestamp() - t0;
    }

    private bool _recentDeath;

    private void Hour()
    {
        var w = _w;
        _recentDeath = w.Crew.Any(o => o.Dead && o.DiedAt >= 0 && w.Tick - o.DiedAt < SimTime.TicksPerDay * 6);
        Boredom();
        if (!NoMotives) Motives();
        foreach (var s in All.ToList())
        {
            if (!s.Active) continue;
            if (P(s.Lead) is not { Dead: false } lead) { End(s, SchemeStage.Dropped, "꾸민 사람이 없다"); continue; }
            Advance(s, lead);
        }
        Leaks();
        PracticesHour();
        DebtsHour();
        ComputerHour();
        Groves();
        int day = SimTime.Day(w.Tick);
        if (day != _lastDay) { _lastDay = day; NewDay(); }
        if (All.Count > 160) All.RemoveAt(All.FindIndex(x => !x.Active) is int i && i >= 0 ? i : 0);
        if (Traces.Count > 80) Traces.RemoveAt(0);
        if (Debts.Count > 60) Debts.RemoveAt(0);
    }

    private void Minute()
    {
        var w = _w;
        foreach (var l in _inRoom.Values) l.Clear();
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room == null) continue;
            if (!_inRoom.TryGetValue(c.Room.Id, out var l)) _inRoom[c.Room.Id] = l = new List<CrewMember>();
            l.Add(c);
        }
        for (int i = 0; i < All.Count; i++)
        {
            var s = All[i];
            if (!s.Active) continue;
            Watch(s);
            ReadNews(s);
        }
        SessionsMinute();
        PracticesMinute();
    }

    private List<CrewMember> Here(Room? r) => r != null && _inRoom.TryGetValue(r.Id, out var l) ? l : NoOne;
    private static readonly List<CrewMember> NoOne = new();

    // ───────────────────────────── 지루함 ─────────────────────────────

    private void Boredom()
    {
        var w = _w;
        bool stir = Crisis.Acting(w) || w.Fire.Count > 0;
        if (stir) _lastDull = w.Tick;
        float dull = (w.Tick - _lastDull) > SimTime.TicksPerDay * 2 ? 1.4f : 1f; // 아무 일 없는 날이 길어질수록
        foreach (var c in w.Crew)
        {
            if (!Adult(c)) continue;
            float b = Bored(c);
            if (stir) b -= 0.12f;
            else if (c.IsAwake && !c.Outside) b += dull / 72f * (c.Job?.Activity is HobbyActivity ? 0.3f : 1f) * (1.15f - 0.3f * c.Traits.Diligence);
            if (w.Motions.Feasting) b -= 0.05f;
            SetBored(c, b);
        }
    }

    // ───────────────────────────── 동기 ─────────────────────────────

    /// <summary>이 사람의 이 동기가 얼마나 센가 (0~1).</summary>
    public float DriveOf(CrewMember c, Drive d)
    {
        var w = _w;
        bool H(Habit h) => c.Habits.Contains(h);
        bool Hb(Hobby h) => c.Hobbies.Contains(h);
        switch (d)
        {
            case Drive.Bored: return Bored(c);
            case Drive.Stress: return c.Needs.Stress;
            case Drive.Lonely: return 1f - c.Needs.Social;
            case Drive.Hungry: return MathF.Max(c.Needs.Hunger, w.Food.Rationing ? 0.55f : 0f) * (H(Habit.Snacker) ? 1.2f : 1f);
            case Drive.Homesick: return MathF.Min(1f, (H(Habit.Homesick) ? 0.75f : 0.15f) + (c.GriefUntil > w.Tick ? 0.3f : 0f));
            case Drive.Jest: return H(Habit.Prankster) ? 0.95f : H(Habit.Joker) ? 0.75f : H(Habit.Serious) ? 0f : H(Habit.Cheerful) ? 0.4f : 0.15f;
            case Drive.Free: return c.Value switch { CrewValue.Freedom => 0.85f, CrewValue.Rules => 0.05f, CrewValue.Safety => 0.15f, CrewValue.People => 0.35f, _ => 0.3f };
            case Drive.Craft:
                return MathF.Min(1f, (H(Habit.Tinkerer) ? 0.6f : 0.1f) + (c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician ? 0.3f : 0f)
                                     + (Hb(Hobby.Electronics) || Hb(Hobby.ModelBuilding) || Hb(Hobby.Woodwork) ? 0.35f : 0f));
            case Drive.Love:
            {
                if (c.Partner is int) return 0.8f;
                float best = 0f;
                foreach (var kv in c.Affinity) if (kv.Value > best) best = kv.Value;
                return best > 0.45f ? 0.35f + 0.5f * best : 0.08f;
            }
            case Drive.Grief: return c.GriefUntil > w.Tick ? 0.9f : _recentDeath ? 0.6f : 0f;
            case Drive.Greed: return MathF.Min(1f, 0.12f + (H(Habit.Hoarder) ? 0.45f : 0f) + (H(Habit.Collector) ? 0.25f : 0f) + (Hb(Hobby.Cards) || Hb(Hobby.Games) ? 0.4f : 0f));
            case Drive.Grudge:
            {
                float g = w.Motions.GrudgeOf(c) != null ? 0.55f : 0f;
                if (w.Command.Captain is CrewMember cap && cap != c)
                    g += MathF.Max(0f, 0.6f - w.Command.Trust) * 0.9f + 0.4f * MathF.Max(0f, -c.AffinityTo(cap)) + 0.3f * MathF.Max(0f, -w.Relations.Trust(c, cap));
                return MathF.Min(1f, g + 0.25f * MathF.Max(0f, c.Needs.Stress - 0.5f));
            }
            case Drive.Faith: return c.Background == Background.Monk ? 0.9f : Hb(Hobby.Meditation) || Hb(Hobby.Yoga) ? 0.6f : c.Value == CrewValue.People ? 0.2f : 0.08f;
            case Drive.Pride:
                return MathF.Min(1f, 0.12f + (H(Habit.GymRat) || c.Background == Background.Athlete ? 0.45f : 0f) + (Hb(Hobby.Workout) || Hb(Hobby.Singing) || Hb(Hobby.Dancing) ? 0.3f : 0f)
                                     + (H(Habit.Leader) ? 0.25f : 0f));
            case Drive.Fear: return MathF.Min(1f, (c.Fears.Count > 0 ? 0.15f : 0f) + 0.6f * MathF.Max(0f, c.Needs.Stress - 0.3f) + 0.3f * w.Brain2.Emotions.Get(c, Feeling.Fear));
            case Drive.Care: return MathF.Min(1f, (c.Value == CrewValue.People ? 0.4f : 0.1f) + (H(Habit.Generous) ? 0.3f : 0f) + 0.3f * c.Traits.Sociability);
        }
        return 0f;
    }

    private static readonly Drive[] Drives = Enum.GetValues<Drive>().Where(d => d != Drive.None).ToArray();

    /// <summary>이 사람이 이 일을 얼마나 꾸미고 싶어 하나 (0~1). 조건이 안 맞으면 0.</summary>
    public float Want(CrewMember c, SchemeSpec s)
    {
        var w = _w;
        float sum = 0f; int n = 0;
        foreach (var d in Drives) if ((s.Drives & d) != 0) { sum += DriveOf(c, d); n++; }
        float v = n > 0 ? sum / n : 0f;
        if (s.Hobbies.Length > 0) v += s.Hobbies.Any(c.Hobbies.Contains) ? 0.3f : -0.05f;
        switch (s.Cat)
        {
            case SchemeCat.Rule or SchemeCat.Secret or SchemeCat.Trade:
                v *= c.Value switch { CrewValue.Rules => 0.3f, CrewValue.Safety => 0.55f, CrewValue.Freedom => 1.3f, _ => 1f } * (1.2f - 0.45f * c.Traits.Diligence);
                if (s.Risk > 0.2f) v *= 0.6f + 0.6f * c.Traits.Bravery;
                break;
            case SchemeCat.Prank: if (c.Habits.Contains(Habit.Serious)) v *= 0.1f; break;
            case SchemeCat.Social: v *= 0.55f + c.Traits.Sociability; break;
            case SchemeCat.Politics: if (DriveOf(c, Drive.Grudge) < 0.35f) return 0f; break;
        }
        if (s.Team == Crewing.Group && c.Traits.Sociability < 0.3f) v *= 0.6f;
        if (!NeedOk(c, s)) return 0f;
        if (Rule(s.Key) is ShipRule r) { if (r.Allowed) return 0f; v *= c.Value == CrewValue.Freedom ? 0.7f : 0.3f; }
        if (PracticeOf(s.Key) != null) return 0f;
        int tried = Tried.GetValueOrDefault(s.Key);
        v /= 1f + 0.8f * tried;
        // 통합7 같은 갈래(모임 · 모임 · 모임)만 거듭되면 시들하다 — 이 배에서 아직 아무도 안 해 본 갈래에 더 끌린다
        int catTried = 0;
        foreach (var k in Tried.Keys) if (SchemeTable.Get(k)?.Cat == s.Cat) catTried++;
        v *= catTried == 0 ? 1.3f : catTried >= 3 ? 0.8f : 1f;
        foreach (var o in All) if (o.Active && o.Spec.Key == s.Key) return 0f;
        return MathF.Max(0f, v);
    }

    private bool NeedOk(CrewMember c, SchemeSpec s)
    {
        var w = _w;
        switch (s.Need)
        {
            case Need.Loot: return w.Expedition.Past.Any(e => e.Returned >= 0 && w.Tick - e.Returned < SimTime.TicksPerDay * 3 && e.Members.Any(m => m.Id == c.Id) && e.LootTotal > 0);
            case Need.Death: return DriveOf(c, Drive.Grief) > 0.3f;
            case Need.Couple: return Couple(c) != null;
            case Need.Partner: return Sweetheart(c) != null;
            case Need.Captain: return w.Command.Captain is CrewMember cap && cap != c;
            case Need.Plenty: return FoodPolicy.FoodDays(w) > 3f;
            case Need.Victim: return w.Crew.Count(o => Adult(o) && o != c) >= 2;
        }
        return true;
    }

    /// <summary>짝 (결혼식): 이미 짝이거나 서로 마음이 깊은 사람.</summary>
    public CrewMember? Couple(CrewMember c)
    {
        if (c.Partner is int pid && P(pid) is { Dead: false } p) return p;
        foreach (var s in All) if (s.Spec.Key == "secret_romance" && s.Crew.Contains(c.Id) && s.Sessions >= 2) return P(s.Crew.First(id => id != c.Id));
        return null;
    }

    /// <summary>몰래 만날 사람: 서로 호감이 깊다.</summary>
    private CrewMember? Sweetheart(CrewMember c)
    {
        if (c.Partner is int) return null;
        CrewMember? best = null; float bv = 0.4f;
        foreach (var o in _w.Crew)
        {
            if (o == c || !Adult(o) || o.Partner is int) continue;
            float v = MathF.Min(c.AffinityTo(o), o.AffinityTo(c));
            if (v > bv) { bv = v; best = o; }
        }
        return best;
    }

    private void Motives()
    {
        var w = _w;
        if (Crisis.Acting(w)) return;
        int adults = w.Crew.Count(Adult);
        int cap = Math.Max(4, adults / 3);
        int Busy() { int n = 0; foreach (var s in All) if (s.Active && s.Stage != SchemeStage.Vote) n++; return n; } // 회의를 기다리는 일은 세지 않는다
        if (Busy() >= cap) return;
        int slot = (int)(w.Tick / SimTime.Hours(1) % 2);
        var top = new List<(SchemeSpec s, float v)>(4);
        foreach (var c in w.Crew)
        {
            if (!Adult(c) || !c.CanAct || !c.IsAwake || c.Outside || (c.Id + slot) % 2 != 0) continue;
            if (_ledAt.TryGetValue(c.Id, out var led) && w.Tick - led < SimTime.TicksPerDay * 2) continue;
            if (LeadOf(c) != null) continue;
            top.Clear();
            foreach (var sp in SchemeTable.All)
            {
                float v = Want(c, sp);
                if (v < 0.25f) continue;
                if (top.Count < 3) top.Add((sp, v));
                else
                {
                    int k = 0;
                    for (int j = 1; j < 3; j++) if (top[j].v < top[k].v) k = j;
                    if (v > top[k].v) top[k] = (sp, v);
                }
            }
            if (top.Count == 0) continue;
            float best = top.Max(x => x.v);
            if (!R.Chance(0.09f * best * best + 0.03f * best)) continue;
            float roll = R.Float() * top.Sum(x => x.v), acc = 0f;
            var pick = top[^1].s;
            foreach (var (sp, v) in top) { acc += v; if (roll <= acc) { pick = sp; break; } }
            Start(pick, c);
            if (Busy() >= cap) return;
        }
    }

    // ───────────────────────────── 시작 ─────────────────────────────

    /// <summary>일을 꾸미기 시작한다 (마음먹은 사람이 이끈다).</summary>
    public Scheme Start(SchemeSpec spec, CrewMember lead, int target = -1, params CrewMember[] with)
    {
        var w = _w;
        var s = new Scheme { Id = _nextId++, Spec = spec, Lead = lead.Id, Born = w.Tick, Since = w.Tick, Stage = SchemeStage.Plan };
        s.Crew.Add(lead.Id);
        s.Knows[lead.Id] = KnowHow.Part;
        s.Target = target >= 0 ? target : PickTarget(spec, lead);
        if (s.Target >= 0 && spec.Fate != Fate.Laugh && spec.Key != "surprise_birthday" && spec.Key != "diary_peek") { s.Crew.Add(s.Target); s.Knows[s.Target] = KnowHow.Part; }
        var room = PickRoom(spec, lead, s.Target);
        s.RoomId = room?.Id ?? -1;
        s.Spot = room != null ? PickSpot(room, spec) : lead.Cell;
        foreach (var o in with) if (!s.Crew.Contains(o.Id)) { s.Crew.Add(o.Id); s.Knows[o.Id] = KnowHow.Part; }
        All.Add(s);
        Tried[spec.Key] = Tried.GetValueOrDefault(spec.Key) + 1;
        Stats.Started++;
        _ledAt[lead.Id] = w.Tick;
        string partner = s.Crew.Count > 1 ? $" ({string.Join(" · ", s.Crew.Skip(1).Select(id => P(id)?.Name ?? "?"))}와 함께)" : "";
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(lead.Name)} 일을 꾸민다 — {spec.Name}{partner}", lead.Id);
        Life.Diary(w, lead, spec.Plan + ".");
        w.Brain2.Goals.Push(lead, "scheme", spec.Name, spec.Plan, spec.Cat == SchemeCat.Social ? ActCat.Social : ActCat.Hobby, 48f, 0.8f);
        w.Brain2.Emotions.Feel(lead, Feeling.Joy, 0.12f, $"{spec.Name} — 생각만 해도 신난다");
        if (spec.Cat is SchemeCat.Social || spec.Fate == Fate.Club || spec.Secrecy < 0.25f) Announce(s, lead);
        int need = Needed(spec);
        if (s.Crew.Count >= need) Begin(s);
        return s;
    }

    private static int Needed(SchemeSpec s) => s.Team switch { Crewing.Solo => 1, Crewing.Pair => 2, _ => s.Fate is Fate.Club or Fate.Event ? 3 : s.Cat == SchemeCat.Politics ? 3 : 3 };

    private void Begin(Scheme s)
    {
        s.Stage = SchemeStage.Prep;
        s.Since = _w.Tick;
        if (s.Spec.Fate == Fate.Club) { s.Stage = SchemeStage.Live; NextSession(s); }
        else if (s.Spec.Key is "petition" or "log_demand") s.Progress = MathF.Max(s.Progress, 0.5f);
    }

    private int PickTarget(SchemeSpec spec, CrewMember lead)
    {
        var w = _w;
        if (spec.Need == Need.Captain && spec.Fate == Fate.Laugh) return w.Command.CaptainId;
        if (spec.Need == Need.Couple) return Couple(lead)?.Id ?? -1;
        if (spec.Need == Need.Partner) return Sweetheart(lead)?.Id ?? -1;
        if (spec.Need != Need.Victim) return -1;
        if (spec.Key == "surprise_birthday")
        {
            int today = SimTime.Day(w.Tick);
            return w.Crew.Where(o => Adult(o) && o != lead).OrderBy(o => ((Birthday(o) - today) % 30 + 30) % 30).ThenBy(o => o.Id).Select(o => o.Id).DefaultIfEmpty(-1).First();
        }
        if (spec.Key == "diary_peek")
        {
            var mate = w.Crew.Where(o => Adult(o) && o != lead && o.HomeBed?.Room != null && o.HomeBed.Room == lead.HomeBed?.Room).OrderBy(o => o.Id).FirstOrDefault();
            if (mate != null) return mate.Id;
        }
        // 장난: 사이가 아주 나쁘지 않은 사람 (친한 사람을 더 자주 놀린다)
        var cands = w.Crew.Where(o => Adult(o) && o != lead && lead.AffinityTo(o) > -0.4f).OrderBy(o => o.Id).ToList();
        if (cands.Count == 0) return -1;
        float tot = cands.Sum(o => 0.4f + MathF.Max(0f, lead.AffinityTo(o))), roll = R.Float() * tot;
        foreach (var o in cands) { roll -= 0.4f + MathF.Max(0f, lead.AffinityTo(o)); if (roll <= 0f) return o.Id; }
        return cands[^1].Id;
    }

    /// <summary>생일 (항해 30일 가운데 하루 — 사람마다 다르다).</summary>
    public static int Birthday(CrewMember c) => (c.Id * 53 + 11) % 30 + 1;

    private static RoomType[] Kinds(Place p) => p switch
    {
        Place.Hidden => new[] { RoomType.Storage, RoomType.Cargo, RoomType.PumpRoom, RoomType.HvacRoom, RoomType.Freezer, RoomType.Laundry },
        Place.Engine => new[] { RoomType.Engine, RoomType.Cooling, RoomType.Power, RoomType.PumpRoom, RoomType.Reactor },
        Place.Garden => new[] { RoomType.Hydroponics, RoomType.Garden, RoomType.AlgaeLab, RoomType.MushroomFarm },
        Place.Galley => new[] { RoomType.Galley, RoomType.Mess },
        Place.Mess => new[] { RoomType.Mess, RoomType.Lounge, RoomType.Galley },
        Place.Lounge => new[] { RoomType.Lounge, RoomType.Theater, RoomType.Mess },
        Place.Comms => new[] { RoomType.Comms, RoomType.Navigation, RoomType.Bridge },
        Place.Gym => new[] { RoomType.Gym, RoomType.Lounge },
        Place.Bridge => new[] { RoomType.Bridge, RoomType.BackupBridge },
        Place.Medbay => new[] { RoomType.Medbay, RoomType.Triage },
        Place.Workshop => new[] { RoomType.Workshop, RoomType.ElectronicsLab, RoomType.WeldingShop, RoomType.PartsPrep },
        Place.Airlock => new[] { RoomType.Airlock, RoomType.EvaPrep },
        Place.Escape => new[] { RoomType.EscapeBay, RoomType.ShuttleBay, RoomType.DockingBay, RoomType.Airlock },
        Place.Chapel => new[] { RoomType.Chapel, RoomType.Meditation, RoomType.Observatory, RoomType.Lounge },
        Place.Observatory => new[] { RoomType.Observatory, RoomType.Bridge, RoomType.Lounge },
        Place.Core => new[] { RoomType.ComputerRoom, RoomType.ServerRoom, RoomType.Bridge },
        Place.Corridor => new[] { RoomType.Corridor },
        Place.Cargo => new[] { RoomType.Cargo, RoomType.Storage },
        Place.Lab => new[] { RoomType.Lab, RoomType.Hydroponics, RoomType.Workshop },
        _ => new[] { RoomType.Quarters, RoomType.PrivateCabins, RoomType.QuietQuarters },
    };

    private Room? PickRoom(SchemeSpec spec, CrewMember lead, int target)
    {
        var w = _w;
        if (spec.Place == Place.Bunk)
        {
            var who = spec.Fate == Fate.Laugh || spec.Key == "diary_peek" ? P(target) ?? lead : lead;
            if (who.HomeBed?.Room is Room home && !home.Detached) return home;
        }
        foreach (var k in Kinds(spec.Place))
        {
            var rs = w.Ship.LiveRooms.Where(r => r.Kind == k && r.Cells.Count > 1).OrderBy(r => r.Id).ToList();
            if (rs.Count > 0) return rs[(lead.Id + spec.Key.Length) % rs.Count];
        }
        foreach (var k in new[] { RoomType.Storage, RoomType.Lounge, RoomType.Mess, RoomType.Quarters })
            if (w.Ship.LiveRooms.Where(r => r.Kind == k).OrderBy(r => r.Id).FirstOrDefault() is Room r0) return r0;
        return w.Ship.LiveRooms.Where(r => r.Kind != RoomType.Corridor).OrderBy(r => r.Id).FirstOrDefault();
    }

    /// <summary>몰래 하는 일은 구석에, 모이는 일은 방 가운데에.</summary>
    private Cell PickSpot(Room room, SchemeSpec spec)
    {
        var w = _w;
        var c0 = room.Center;
        bool corner = spec.Secrecy >= 0.45f;
        var cells = room.Cells.Where(x => w.Ship.IsWalkable(x)).ToList();
        if (cells.Count == 0) return room.Cells[0];
        return corner ? cells.OrderByDescending(x => (x.Center - c0).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).First()
                      : cells.OrderBy(x => (x.Center - c0).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).First();
    }

    // ───────────────────────────── 흔적 · 규칙 ─────────────────────────────

    private SchemeTrace Mark(Scheme s, TraceState st, string text)
    {
        var w = _w;
        var t = Traces.FirstOrDefault(x => x.Scheme == s.Id);
        if (t == null)
        {
            t = new SchemeTrace { Id = _traceId++, Key = s.Spec.Key, Scheme = s.Id, RoomId = s.RoomId, At = s.Spot, Owner = s.Lead, Tick = w.Tick, State = st, Text = text };
            Traces.Add(t);
        }
        else { t.State = st; t.Text = text; t.Tick = w.Tick; }
        if (RoomOf(s) is Room r) MarkLog.Add(r.Marks, w.Tick, text);
        return t;
    }

    private void AddRule(Scheme s, bool allowed, string text, int motion = -1)
    {
        Rules.Add(new ShipRule { Key = s.Spec.Key, Text = text, Allowed = allowed, Tick = _w.Tick, Motion = motion });
        if (Rules.Count > 40) Rules.RemoveAt(0);
    }

    private void End(Scheme s, SchemeStage st, string outcome)
    {
        var w = _w;
        s.Stage = st;
        s.Outcome = outcome;
        s.Ended = w.Tick;
        s.SessionAt = s.SessionEnd = -1;
        if (st == SchemeStage.Dropped)
        {
            Stats.Dropped++;
            if (P(s.Lead) is CrewMember lead && !lead.Dead) Life.Diary(w, lead, $"{Ko.EulReul(s.Spec.Name)} 접었다 — {outcome}.");
        }
    }

    // ───────────────────────────── 지문 ─────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(All.Count); I(_nextId); I(Practices.Count); I(Traces.Count); I(Debts.Count); I(Rules.Count);
        foreach (var s in All)
        {
            I(s.Id); I((int)s.Stage); I(s.Crew.Count); I(s.Knows.Count); F(s.Progress); F(s.Ripe); I(s.Sessions); I(s.Finder); I(s.Motion); I(s.ComputerSaid); I(s.Skimmed);
        }
        foreach (var p in Practices) { I(p.Held); I(p.Followers.Count); I(p.Start); }
        foreach (var d in Debts) { I(d.From); I(d.To); I(d.Amount); }
        foreach (var kv in _bored) { I(kv.Key); F(kv.Value); }
        I(R.Draws);
    }
}
