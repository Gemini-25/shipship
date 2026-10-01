using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v13.2 회의 개편: 네 종류의 회의 · 의견의 근거 · 토론과 설득 · 결정의 무게 · 파벌 · 첫 출항 회의.
//
// 긴급 판단: 급하면(압박 0.9 이상) 선장이 혼자 정한다 (선장이 못 하면 지휘 순서대로).
// 현장 협의: 위기 중(조가 짜여 있을 때) 결정이 필요하면 지휘자·조장들이 무전으로 1~2분 안에.
// 정기 회의: 하루 한 번 저녁 7시, 회의실(없으면 식당)에 실제로 모여 안건을 몰아서 — 사후 검토 · 선장 불신임 · 미뤄 둔 결정 · 방침 하나.
// 사후 검토: 큰 사고 뒤 (사람이 죽었거나 방침이 사고를 키웠다) 다음 정기 회의에 그 방침을 다시 올린다.
// 토론: 처음 의견 → 확신 있고 설득력 있는 사람이 먼저 말한다 → 듣는 사람의 의견이 움직인다(관계·같은 가치관·고집) → 표결.
//   처음 다수와 마지막 다수가 다르면 "○○의 설득으로 뒤집혔다".
// 설득력: 그 일의 솜씨(전문성) · 사교성 · 침착 · 쌓인 신용 − 스트레스 − 죄책감 (+ 선장은 신뢰만큼).
// 결정의 무게: 정한 일로 사람이 죽으면 찬성한 사람에게 죄책감, 반대한 사람과 가까웠던 사람의 비난 → 신용·관계·선장 신뢰가 떨어진다.
//   사람이 죽지 않고 넘기면 찬성한 사람의 신용이 오른다. 정보 공개 방침이 "선장만"이면 비난은 선장에게 간다.
// 파벌: 가치관이 같은 사람끼리 서로의 말에 더 움직이고, 표가 가치관대로 갈리면 그 둘 사이에 골이 깊어진다 (말다툼이 잦아진다).
// 첫 출항 회의: 출항하자마자 승무원의 가치관대로 방침을 정한다 (과반이 같은 쪽을 바라면) → 배마다 문화가 다르다.

public enum MeetingKind { Maiden, Regular, Review, Field, Emergency, AdHoc }

public sealed class Speech
{
    public int Who { get; init; }
    public bool For { get; init; }
    public string Text { get; init; } = "";
    public int Moved { get; set; }
}

public sealed class AgendaItem
{
    public string Title { get; init; } = "";
    public string Topic { get; init; } = "";
    /// <summary>사후 검토의 근거 (겪은 일).</summary>
    public string? Evidence { get; init; }
    /// <summary>컴퓨터의 권고와 근거 (투표권은 없다).</summary>
    public string? Computer { get; set; }
    public int ComputerSign { get; set; }
    public List<Speech> Speeches { get; } = new();
    public List<(int who, bool yes, string why)> Votes { get; } = new();
    public int Yes { get; set; }
    public int No { get; set; }
    public bool Passed { get; set; }
    public string? FlippedBy { get; set; }
    public string Outcome { get; set; } = "";
}

public sealed class MeetingRecord
{
    public int Id { get; init; }
    public MeetingKind Kind { get; set; }
    public long Tick { get; init; }
    public long End { get; set; }
    public int Chair { get; init; } = -1;
    public string Venue { get; init; } = "";
    public List<int> Attendees { get; init; } = new();
    public List<AgendaItem> Items { get; } = new();
}

/// <summary>정한 일 하나 (결정의 무게를 재려고 남겨 둔다).</summary>
public sealed class DecisionRecord
{
    public long Tick { get; init; }
    public string Title { get; init; } = "";
    public string Topic { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public int Decider { get; init; } = -1;
    public List<int> Yes { get; init; } = new();
    public List<int> No { get; init; } = new();
    public string Hazard { get; init; } = "";
    public bool Judged { get; set; }
}

public sealed class MeetingSystem
{
    private readonly World _w;
    private readonly Rng _rng;
    public MeetingSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 7919 + 83));
    }

    public static string KindName(MeetingKind k) => k switch
    {
        MeetingKind.Maiden => "첫 출항 회의",
        MeetingKind.Regular => "정기 회의",
        MeetingKind.Review => "사후 검토",
        MeetingKind.Field => "현장 협의",
        MeetingKind.Emergency => "긴급 판단",
        _ => "임시 회의",
    };

    public static string ValueName(CrewValue v) => v switch
    {
        CrewValue.Safety => "안전", CrewValue.Efficiency => "효율", CrewValue.People => "사람", CrewValue.Rules => "규칙", _ => "자유",
    };

    private static string ValueWhy(CrewValue v) => v switch
    {
        CrewValue.Safety => "안전이 먼저다",
        CrewValue.Efficiency => "배가 살아야 모두 산다",
        CrewValue.People => "사람이 먼저다",
        CrewValue.Rules => "정해 둔 대로 해야 한다",
        _ => "현장에서 판단하게 두자",
    };

    public List<MeetingRecord> Minutes { get; } = new();
    public List<DecisionRecord> Decisions { get; } = new();
    /// <summary>사후 검토로 다음 정기 회의에 올릴 방침 (방침 · 바꿀 값 · 근거).</summary>
    public List<(string id, int to, string why)> Reviews { get; } = new();
    private readonly Dictionary<int, float> _cred = new();
    private readonly Dictionary<int, float> _guilt = new();
    private readonly Dictionary<(CrewValue, CrewValue), float> _tension = new();
    public int Held, Postponed, Flips, Blames, Guilts, Praises, FactionSplits, PolicyChanges;
    public bool MaidenDone { get; private set; }
    /// <summary>시험용: 첫 출항 회의를 하지 않는다 (방침이 처음 값 그대로 — 특정 동작을 재는 점검용).</summary>
    public static bool MaidenOff { get; set; }
    /// <summary>첫 출항 회의가 정한 이 배의 문화 (가장 많은 가치관).</summary>
    public string Culture { get; private set; } = "";

    // ── 정기 회의 상태 ──
    /// <summary>정기 회의 시각: 저녁 7시 근처에서 일과표상 가장 많이 깨어 있는 때 (교대 근무 배는 교대가 겹치는 때).</summary>
    public float Hour { get; private set; } = 19f;
    private long _hourDay = -1;

    private void PickHour()
    {
        long day = _w.Tick / SimTime.TicksPerDay;
        if (day == _hourDay) return;
        _hourDay = day;
        var crew = _w.Crew.Where(c => !c.Dead && !c.IsChild).ToList();
        if (crew.Count == 0) return;
        float best = 19f; int bestN = -1;
        foreach (float h in new[] { 19f, 18f, 20f, 17f, 21f, 16f, 15f, 14f, 13f, 12f })
        {
            int n = crew.Count(c => SimTime.HoursFromTo(c.Schedule.SleepStart, h) >= c.Schedule.SleepLength + 0.5f && SimTime.HoursFromTo(c.Schedule.SleepStart, h) <= 23.5f);
            if (n > bestN) { bestN = n; best = h; }
        }
        Hour = best;
    }
    public bool Gathering { get; private set; }
    public MeetingRecord? Session { get; private set; }
    public Room? Venue { get; private set; }
    public HashSet<int> Invited { get; } = new();
    public HashSet<int> Present { get; } = new();
    private long _gatherStart = -1, _sessionEnd = -1, _lastDay = -1;
    public long SessionStart { get; private set; } = -1;
    public long SessionEnd => _sessionEnd;

    /// <summary>쌓인 신용 (-0.4~0.4): 옳았던 결정 · 뒤집은 설득 ↔ 사람이 죽은 결정 · 비난.</summary>
    public float Credibility(CrewMember c) => _cred.TryGetValue(c.Id, out var v) ? v : 0f;
    public float Guilt(CrewMember c) => _guilt.TryGetValue(c.Id, out var v) ? v : 0f;
    private void AddCred(CrewMember c, float d) => _cred[c.Id] = Math.Clamp(Credibility(c) + d, -0.4f, 0.4f);

    /// <summary>두 가치관 사이의 골 (0~1).</summary>
    public float Tension(CrewValue a, CrewValue b)
    {
        if (a == b) return 0f;
        var key = a < b ? (a, b) : (b, a);
        return _tension.TryGetValue(key, out var v) ? v : 0f;
    }

    private void AddTension(CrewValue a, CrewValue b, float d)
    {
        if (a == b) return;
        var key = a < b ? (a, b) : (b, a);
        _tension[key] = Math.Clamp(Tension(a, b) + d, 0f, 1f);
    }

    /// <summary>파벌: 같은 가치관을 가진 사람이 둘 이상인 무리.</summary>
    public IEnumerable<(CrewValue value, List<CrewMember> members)> Factions() =>
        _w.Crew.Where(c => !c.Dead && !c.IsChild).GroupBy(c => c.Value).Where(g => g.Count() >= 2).OrderByDescending(g => g.Count())
            .Select(g => (g.Key, g.ToList()));

    public IEnumerable<((CrewValue a, CrewValue b) pair, float tension)> Rifts() =>
        _tension.Where(kv => kv.Value >= 0.05f).OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value));

    /// <summary>설득력: 전문성 · 사교성 · 침착 · 신용 − 스트레스 · 죄책감 (+ 선장은 신뢰만큼).</summary>
    public float Persuasion(CrewMember c, float expertise)
    {
        float p = 0.3f + 0.35f * expertise + 0.15f * c.Traits.Sociability + 0.1f * c.Traits.Calm + Credibility(c)
                  - 0.2f * c.Needs.Stress - 0.15f * Guilt(c) + MathF.Min(0.12f, c.Stats.Emergencies * 0.01f);
        if (c.Id == _w.Command.CaptainId) p += 0.12f * _w.Command.Trust;
        if (c.Habits.Count > 0) p += Persona.Add(c, h => h.Persuade); // v14.0 앞장서기·진지함 / 비관적·따르기
        return Math.Clamp(p, 0.05f, 1.2f);
    }

    /// <summary>고집: 덜 사교적이고 규칙을 앞세우고 예민할수록 잘 안 움직인다.</summary>
    public static float Stubborn(CrewMember c) =>
        Math.Clamp(0.35f + 0.25f * (1f - c.Traits.Sociability) + (c.Value == CrewValue.Rules ? 0.1f : 0f) + 0.2f * c.Needs.Stress, 0.15f, 0.85f);

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (!MaidenDone) Maiden();
        // 죄책감은 천천히 옅어진다 (하루 0.06)
        foreach (var id in _guilt.Keys.ToList())
        {
            _guilt[id] = MathF.Max(0f, _guilt[id] - 0.06f * dt / 24f);
            if (_guilt[id] <= 0f) _guilt.Remove(id);
        }
        foreach (var k in _tension.Keys.ToList()) _tension[k] = MathF.Max(0f, _tension[k] - 0.02f * dt / 24f);
        Regular();
    }

    private List<CrewMember> Adults() => _w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).ToList();

    // ───────────────────────────── 첫 출항 회의 ─────────────────────────────

    private void Maiden()
    {
        var w = _w;
        var voters = Adults();
        if (voters.Count == 0 || voters.Any(c => !c.Profiled)) return;
        MaidenDone = true;
        if (MaidenOff) { Culture = "(시험: 처음 값)"; return; }
        var top = voters.GroupBy(c => c.Value).OrderByDescending(g => g.Count()).ThenBy(g => (int)g.Key).First();
        Culture = $"{ValueName(top.Key)} 중시";
        var rec = new MeetingRecord
        {
            Id = Minutes.Count + 1, Kind = MeetingKind.Maiden, Tick = w.Tick, End = w.Tick, Chair = w.Command.CaptainId,
            Venue = "출항 전", Attendees = voters.Select(c => c.Id).ToList(),
        };
        var changed = new List<string>();
        foreach (var spec in PolicySystem.All)
        {
            var prefs = voters.Select(c => (c, pref: PolicySystem.Preferred(c, spec.Id))).ToList();
            var best = prefs.GroupBy(x => x.pref).OrderByDescending(g => g.Count()).ThenBy(g => g.Key == spec.Default ? 0 : 1).First();
            if (best.Key == spec.Default || best.Count() * 2 <= voters.Count) continue;
            int yes = best.Count(), no = voters.Count - yes;
            w.Policies.Set(spec.Id, best.Key, $"첫 출항 회의 — {Culture}", yes, no);
            var item = new AgendaItem { Title = $"{spec.Name}: {spec.Options[best.Key]}", Topic = "policy:" + spec.Id, Yes = yes, No = no, Passed = true, Outcome = "정했다" };
            var voice = best.OrderByDescending(x => PolicySystem.Expertise(x.c, spec.Id) + x.c.Traits.Sociability).First().c;
            item.Speeches.Add(new Speech { Who = voice.Id, For = true, Text = Persona.Say(voice, ValueWhy(voice.Value)) });
            foreach (var (c, pref) in prefs) item.Votes.Add((c.Id, pref == best.Key, ValueWhy(c.Value)));
            rec.Items.Add(item);
            changed.Add($"{spec.Name} {spec.Options[best.Key]}");
        }
        Minutes.Add(rec);
        string text = changed.Count > 0
            ? $"첫 출항 회의 ({voters.Count}명 · {Culture}): " + string.Join(" · ", changed)
            : $"첫 출항 회의 ({voters.Count}명 · {Culture}): 처음 정해 둔 방침 그대로";
        w.History.Add(w, HistoryKind.Decision, text, null, voters, log: true);
    }

    // ───────────────────────────── 정기 회의 ─────────────────────────────

    private List<CrewMember> Eligible() =>
        _w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct && c.IsAwake && !c.Outside && c.Room != null && c.Vitals.Health > 0.35f).ToList();

    /// <summary>v13.4 표를 던지는 사람 (수습 중인 새 승무원은 듣기만 한다).</summary>
    private List<CrewMember> Voters(List<CrewMember> attendees)
    {
        var v = attendees.Where(c => !_w.Society.OnProbation(c)).ToList();
        return v.Count >= 2 ? v : attendees;
    }

    private Room? PickVenue()
    {
        var w = _w;
        bool Ok(Room r) => !r.Abandoned && !r.Detached && !r.OffLimits && !r.Leaking && Atmosphere.Danger(r) < 0.1f && w.Fire.CountIn(r) == 0;
        return w.Ship.RoomsOf(RoomType.MeetingRoom).FirstOrDefault(Ok)
               ?? w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault(Ok)
               ?? w.Ship.RoomsOf(RoomType.Lounge).FirstOrDefault(Ok);
    }

    private bool Calm() => Crisis.Level(_w) < CrisisLevel.Emergency && !_w.Command.Active;

    private void Regular()
    {
        var w = _w;
        float hour = SimTime.HourOfDay(w.Tick);
        long day = w.Tick / SimTime.TicksPerDay;
        if (Session != null)
        {
            if (w.Tick >= _sessionEnd) { Session.End = w.Tick; Session = null; Venue = null; Present.Clear(); Invited.Clear(); }
            return;
        }
        if (Gathering) { Gather(day); return; }
        PickHour();
        if (day == _lastDay || hour < Hour || hour >= MathF.Min(23.5f, Hour + 4f)) return;
        if (!Calm())
        {
            if (hour >= MathF.Min(23f, Hour + 3f)) { _lastDay = day; Postponed++; w.Log.Add(w.Tick, LogKind.Ship, "정기 회의 — 위기라 오늘은 걸렀다"); }
            return;
        }
        var eligible = Eligible();
        if (eligible.Count < 2) { _lastDay = day; return; }
        _lastDay = day;
        Venue = PickVenue();
        if (Venue == null)
        {
            // 모일 곳이 없다 — 무전으로 짧게
            Hold(MeetingKind.Regular, eligible, "무전");
            return;
        }
        Gathering = true;
        _gatherStart = w.Tick;
        Invited.Clear();
        Present.Clear();
        foreach (var c in eligible) { Invited.Add(c.Id); c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1 + c.Id % 5); }
        w.Log.Add(w.Tick, LogKind.Ship, $"정기 회의 — {Venue.Name}에 모인다 ({eligible.Count}명)");
    }

    private void Gather(long day)
    {
        var w = _w;
        if (!Calm() || Venue == null || Venue.Leaking || w.Fire.CountIn(Venue) > 0)
        {
            Gathering = false;
            Postponed++;
            _lastDay = -1; // 위기가 지나고 시간이 남으면 다시 모인다
            Invited.Clear(); Present.Clear();
            w.Log.Add(w.Tick, LogKind.Ship, "정기 회의 — 비상이라 흩어졌다");
            return;
        }
        foreach (var id in Invited.ToList())
        {
            var c = w.Crew.FirstOrDefault(x => x.Id == id);
            if (c == null || c.Dead || c.Down || c.Pose == Pose.Sleeping && c.Room != Venue) { Invited.Remove(id); Present.Remove(id); continue; }
            if (c.Room == Venue) Present.Add(id);
        }
        int need = Math.Max(2, (int)MathF.Ceiling(Invited.Count * 0.6f));
        bool late = w.Tick - _gatherStart >= SimTime.Minutes(35);
        // 다 모이면 바로, 아니면 10분은 늦는 사람을 기다린다
        bool all = Present.Count >= Invited.Count && Present.Count >= 2;
        if (all || Present.Count >= need && w.Tick - _gatherStart >= SimTime.Minutes(10) || late && Present.Count >= 2)
        {
            Gathering = false;
            var attendees = Present.Select(id => w.Crew.First(c => c.Id == id)).ToList();
            var rec = Hold(MeetingKind.Regular, attendees, Venue.Name);
            int talk = rec.Items.Sum(i => Math.Max(1, i.Speeches.Count));
            Session = rec;
            SessionStart = w.Tick;
            _sessionEnd = w.Tick + SimTime.Minutes(Math.Clamp(8 + 3 * talk, 10, 40));
            return;
        }
        if (w.Tick - _gatherStart >= SimTime.Minutes(60))
        {
            Gathering = false;
            Postponed++;
            Invited.Clear(); Present.Clear();
            w.Log.Add(w.Tick, LogKind.Ship, "정기 회의 — 사람이 모이지 않아 걸렀다");
        }
    }

    /// <summary>이 사람이 지금 회의 자리에 가야 하나 (모이는 중이고 부름을 받았다).</summary>
    public bool Summoned(CrewMember c) => (Gathering || Session != null) && Invited.Contains(c.Id);

    /// <summary>안건을 모아 정한다 (정기 회의·사후 검토).</summary>
    public MeetingRecord Hold(MeetingKind kind, List<CrewMember> attendees, string venue)
    {
        var w = _w;
        var cap = w.Command.Captain;
        var chair = cap != null && attendees.Contains(cap) ? cap : attendees.OrderByDescending(CommandSystem.Leadership).First();
        var rec = new MeetingRecord
        {
            Id = Minutes.Count + 1, Kind = kind, Tick = w.Tick, Chair = chair.Id, Venue = venue,
            Attendees = attendees.Select(c => c.Id).ToList(),
        };
        var listeners = attendees;
        attendees = Voters(attendees);
        // v13.4 방침(추모: 기념일): 떠난 사람의 이름을 부른다
        if (w.Policies["memorial"] == 0 && w.Life.Memorial.Count > 0 && kind == MeetingKind.Regular)
        {
            foreach (var c in listeners)
            {
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f);
                if (c.GriefUntil > w.Tick) c.GriefUntil -= SimTime.Hours(2);
            }
            rec.Items.Add(new AgendaItem { Title = $"추모 — {string.Join("·", w.Life.Memorial.TakeLast(3).Select(m => m.name))}", Topic = "memorial", Passed = true, Outcome = "이름을 불렀다" });
        }
        // 1) 사후 검토: 사고가 남긴 방침 안건
        if (Reviews.Count > 0) rec.Kind = MeetingKind.Review;
        foreach (var (id, to, why) in Reviews.Take(3).ToList()) ResolvePolicy(rec, attendees, chair, id, to, why);
        Reviews.Clear();
        // 2) 선장 불신임
        if (w.Command.Trust < 0.35f && attendees.Count >= 3 && cap != null) rec.Items.Add(w.Command.VoteNoConfidence(attendees, this));
        // 3) 미뤄 둔 결정: 모인 김에 정한다
        foreach (var o in w.Board.Open.Where(o => Council.Needs(o.Kind) && o.Decision == DecisionState.Pending && !o.Alone && !o.Closed).ToList())
            Council.DecideNow(w, o, attendees, rec);
        w.Cosmic.Agenda(rec, attendees); // v18.13 대재난 대비 계획 · 그날을 기리는 관행
        w.Expedition.Agenda(rec, attendees, chair); // v16.12 원정 안건
        w.Automation.Authority.Agenda(rec, attendees, chair); // v16.16 컴퓨터 안건 (계획 · 권한)
        // 4) 방침 하나: 모인 사람 다수가 바라는 쪽이 지금과 다르고, 바꾼 지 사흘이 지났으면 올린다
        if (rec.Items.Count(i => i.Topic.StartsWith("policy:")) == 0 && attendees.Count >= 3)
        {
            (PolicySpec spec, int to, int want)? pick = null;
            foreach (var spec in PolicySystem.All)
            {
                long set = w.Policies.SetAt(spec.Id);
                if (set >= 0 && w.Tick - set < SimTime.TicksPerDay * 3) continue;
                int cur = w.Policies[spec.Id];
                var best = attendees.GroupBy(c => PolicySystem.Preferred(c, spec.Id)).OrderByDescending(g => g.Count()).First();
                int stay = attendees.Count(c => PolicySystem.Preferred(c, spec.Id) == cur);
                if (best.Key == cur || best.Count() * 2 <= attendees.Count || best.Count() - stay < 2) continue;
                if (pick == null || best.Count() > pick.Value.want) pick = (spec, best.Key, best.Count());
            }
            if (pick is { } p) ResolvePolicy(rec, attendees, chair, p.spec.Id, p.to, null);
        }
        Held++;
        Minutes.Add(rec);
        if (Minutes.Count > 60) Minutes.RemoveAt(0);
        string head = $"{KindName(rec.Kind)} ({venue} · {attendees.Count}명 · 의장 {chair.Name})";
        if (rec.Items.Count == 0)
            w.Log.Add(w.Tick, LogKind.Ship, $"{head}: 안건 없음 — 하루를 돌아보고 흩어졌다");
        foreach (var c in attendees)
        {
            c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.1f);
            // 함께 모이면 조금 가까워진다 (골이 깊은 사이는 빼고)
        }
        return rec;
    }

    private void ResolvePolicy(MeetingRecord rec, List<CrewMember> voters, CrewMember chair, string id, int to, string? evidence)
    {
        var w = _w;
        var spec = PolicySystem.Spec(id);
        int from = w.Policies[id];
        to = Math.Clamp(to, 0, spec.Options.Length - 1);
        if (to == from) return;
        var item = new AgendaItem
        {
            Title = $"{spec.Name}: {spec.Options[from]} → {spec.Options[to]}", Topic = "policy:" + id, Evidence = evidence,
        };
        (item.Computer, item.ComputerSign) = ComputerAdvice(id, from, to, evidence);
        var (yes, no) = Debate(voters, c => PolicyOpinion(c, id, from, to, evidence, item.ComputerSign), c => PolicySystem.Expertise(c, id), item, chair);
        bool pass = yes.Count > no.Count || yes.Count == no.Count && yes.Contains(chair);
        item.Passed = pass;
        item.Outcome = pass ? "바꿨다" : "그대로";
        rec.Items.Add(item);
        string flip = item.FlippedBy != null ? $" · {item.FlippedBy}의 설득으로 뒤집혔다" : "";
        string because = evidence != null ? $" — {evidence}" : "";
        if (pass)
        {
            w.Policies.Set(id, to, (evidence ?? KindName(rec.Kind)) + flip, yes.Count, no.Count);
            PolicyChanges++;
            Record(item.Title, "policy:" + id, -1, chair, yes, no, Hazard(id));
        }
        // 기존 기록과 이어지게: "배 우선"처럼 선택지 이름이 연대기에 남는다
        w.History.Add(w, HistoryKind.Decision,
            $"{KindName(rec.Kind)}: 방침 '{spec.Name}' {spec.Options[from]} → {spec.Options[to]}?{because} · 찬성 {yes.Count} · 반대 {no.Count} → {(pass ? "바꿨다" : "그대로")}{flip}",
            null, voters, log: true);
        w.History.DecisionsMade++;
        Split(yes, no);
    }

    /// <summary>방침이 걸린 위험 (결정의 무게를 잴 때 어떤 죽음과 이어지나).</summary>
    public static string Hazard(string id) => id switch
    {
        "vacuumfire" or "inertfire" or "firemethod" => "fire",
        "decompress" or "zoneabandon" or "evac" or "autoscope" => "air",
        "reactor" or "shed" => "power",
        "risktaking" or "rescue" or "rotation" or "muster" or "command" or "suits" or "controlseat" => "crisis",
        _ => "",
    };

    /// <summary>컴퓨터의 권고 (투표권 없음): 지난 사고들의 결과로 근거를 든다. 컴퓨터를 믿는 만큼 말이 먹힌다.</summary>
    private (string? text, int sign) ComputerAdvice(string id, int from, int to, string? evidence)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline || a.Level < 4) return (null, 0);
        int sign = 0;
        string why;
        switch (id)
        {
            case "vacuumfire":
            case "inertfire":
                sign = to > from ? (a.Smothered + a.Vacuumed > 0 ? 1 : 0) : (w.History.Deaths > 0 && evidence != null ? 1 : -1);
                why = $"질식 소화 {a.Smothered}번 · 진공 소화 {a.Vacuumed}번 · 불 {w.History.Fires}번";
                break;
            case "decompress":
                sign = to == 1 ? (a.LateSeals >= 2 ? 1 : -1) : (a.TrappedCasualties >= 1 ? 1 : -1);
                why = $"늦게 닫아 잃은 일 {a.LateSeals} · 닫힌 방에서 쓰러짐 {a.TrappedCasualties}";
                break;
            case "reactor":
                sign = to == 0 ? (w.History.Scrams >= 2 ? 1 : -1) : to == 2 ? -1 : 0;
                why = $"원자로 정지 {w.History.Scrams}번";
                break;
            case "command":
                sign = to == 1 || to == 2 ? (w.Command.ComputerTrust >= 0.5f ? 1 : -1) : -1;
                why = $"컴퓨터 신뢰 {w.Command.ComputerTrust * 100:0}%";
                break;
            default:
                sign = evidence != null ? 1 : 0;
                why = evidence ?? "판단할 기록이 모자라다";
                break;
        }
        string verdict = sign > 0 ? "찬성 권고" : sign < 0 ? "반대 권고" : "판단 보류";
        return ($"주 컴퓨터: {verdict} — {why}", sign);
    }

    private (float, string) PolicyOpinion(CrewMember c, string id, int from, int to, string? evidence, int computer)
    {
        var w = _w;
        var terms = new List<(float v, string why)>();
        int pref = PolicySystem.Preferred(c, id);
        string vw = ValueWhy(c.Value);
        // 사후 검토에서는 가치관보다 겪은 일이 무겁다
        float lean = evidence != null ? 0.2f : 0.32f;
        if (pref == to) terms.Add((lean, vw));
        else if (pref == from) terms.Add((-lean, vw));
        else terms.Add((Math.Abs(pref - to) < Math.Abs(pref - from) ? 0.12f : -0.12f, vw));
        terms.Add((-0.07f, "익숙한 대로 하자"));
        // 사후 검토: 겪은 일이 무겁다 (그 사고를 겪은 사람일수록, 침착할수록 근거를 본다)
        if (evidence != null) terms.Add((0.36f + 0.15f * c.Traits.Calm + (c.Memory.Trauma > 0.1f ? 0.1f : 0f), evidence));
        // 죄책감이 남은 사람은 신중한 쪽으로
        var spec = PolicySystem.Spec(id);
        float g = Guilt(c);
        if (g > 0.05f && spec.Default != to)
        {
            bool cautious = PolicySystem.Preferred(c, id) <= to && Hazard(id) != "";
            if (cautious) terms.Add((0.25f * g, "다시는 사람을 잃고 싶지 않다"));
        }
        // 지치고 예민하면 바꾸는 게 귀찮다
        if (c.Needs.Stress > 0.6f) terms.Add((-0.08f, "지금 바꿀 때가 아니다"));
        // 베테랑은 자기 판단을 믿는다
        if (c.Stats.Emergencies >= 10) terms.Add((pref == to ? 0.1f : pref == from ? -0.1f : 0f, "겪어 봐서 안다"));
        // 컴퓨터 권고 (컴퓨터를 믿는 만큼)
        if (computer != 0) terms.Add((0.15f * w.Command.ComputerTrust * computer * (c.Value == CrewValue.Freedom ? 0.5f : 1f), computer > 0 ? "컴퓨터 권고도 그렇다" : "컴퓨터도 말린다"));
        float s = terms.Sum(t => t.v) + 0.04f * (_rng.Float() - 0.5f);
        string reason = s > 0f ? terms.Where(t => t.v > 0f).OrderByDescending(t => t.v).Select(t => t.why).DefaultIfEmpty("바꾸자").First()
            : terms.Where(t => t.v < 0f).OrderBy(t => t.v).Select(t => t.why).DefaultIfEmpty("그대로 두자").First();
        return (s, reason);
    }

    // ───────────────────────────── 토론 ─────────────────────────────

    /// <summary>
    /// 처음 의견 → 확신 있고 설득력 있는 사람부터 말한다(양쪽 다 한 번은) → 듣는 사람이 움직인다 → 표결.
    /// 처음 다수와 마지막 다수가 다르면 가장 많이 움직인 발언자가 뒤집은 사람.
    /// </summary>
    public (List<CrewMember> yes, List<CrewMember> no) Debate(List<CrewMember> voters, Func<CrewMember, (float s, string why)> opinion,
        Func<CrewMember, float> expertise, AgendaItem item, CrewMember? chair, Dictionary<CrewMember, (float s, string why)>? final = null)
    {
        var s = voters.ToDictionary(c => c, opinion);
        int yes0 = s.Count(kv => kv.Value.s > 0f);
        bool major0 = yes0 * 2 > voters.Count;
        if (voters.Count >= 2)
        {
            var order = voters.OrderByDescending(c => MathF.Abs(s[c].s) * Persuasion(c, expertise(c))).ThenBy(c => c.Id).ToList();
            var speakers = order.Take(Math.Min(3, voters.Count)).ToList();
            // 소수 쪽도 한 번은 말한다
            bool hasYes = speakers.Any(c => s[c].s > 0f), hasNo = speakers.Any(c => s[c].s <= 0f);
            var minority = order.FirstOrDefault(c => !speakers.Contains(c) && (!hasYes && s[c].s > 0f || !hasNo && s[c].s <= 0f));
            if (minority != null) speakers.Add(minority);
            // 의장이 마지막에 정리한다
            if (chair != null && voters.Contains(chair) && !speakers.Contains(chair) && voters.Count >= 4) speakers.Add(chair);
            foreach (var sp in speakers)
            {
                var (ss, why) = s[sp];
                bool pro = ss > 0f;
                float power = Persuasion(sp, expertise(sp)) * MathF.Min(1f, MathF.Abs(ss) + 0.25f);
                var speech = new Speech { Who = sp.Id, For = pro, Text = Persona.Say(sp, why) }; // v14.0 말버릇
                foreach (var c in voters)
                {
                    if (c == sp) continue;
                    float rel = Math.Clamp(c.AffinityTo(sp) + 0.5f * _w.Relations.Trust(c, sp), -0.6f, 0.9f); // v14.4 그 사람에 대한 기억 (구해 줬다 · 경고를 무시했다…)
                    float faction = c.Value == sp.Value ? 0.3f : 0f;
                    float k = 0.2f * power * (1f - Stubborn(c)) * (0.7f + 0.5f * rel + faction - 0.6f * Tension(c.Value, sp.Value));
                    if (k <= 0f) continue;
                    var (before, bw) = s[c];
                    float after = before + (pro ? k : -k);
                    if (before > 0f != after > 0f)
                    {
                        speech.Moved++;
                        s[c] = (after, $"{sp.Name}의 말에 — {why}");
                    }
                    else s[c] = (after, bw);
                }
                item.Speeches.Add(speech);
            }
        }
        var yes = voters.Where(c => s[c].s > 0f).ToList();
        var no = voters.Where(c => s[c].s <= 0f).ToList();
        // v13.3 크게 반대했는데 진 사람은 화가 난다
        var losers = yes.Count > no.Count ? no : yes;
        foreach (var c in losers) if (MathF.Abs(s[c].s) > 0.4f) MindSystem.Anger(c, 0.05f);
        item.Yes = yes.Count;
        item.No = no.Count;
        foreach (var c in voters) item.Votes.Add((c.Id, s[c].s > 0f, s[c].why));
        bool major1 = yes.Count * 2 > voters.Count;
        if (voters.Count >= 3 && major0 != major1 && yes.Count * 2 != voters.Count)
        {
            var top = item.Speeches.Where(x => x.For == major1).OrderByDescending(x => x.Moved).FirstOrDefault();
            if (top != null && top.Moved > 0 && _w.Crew.FirstOrDefault(c => c.Id == top.Who) is CrewMember who)
            {
                item.FlippedBy = who.Name;
                Flips++;
                AddCred(who, 0.04f);
            }
        }
        if (final != null) foreach (var kv in s) final[kv.Key] = kv.Value;
        return (yes, no);
    }

    /// <summary>표가 가치관대로 갈렸으면 그 가치관 사이에 골이 깊어진다 (파벌).</summary>
    public void Split(List<CrewMember> yes, List<CrewMember> no)
    {
        if (yes.Count < 2 || no.Count < 2) return;
        var vy = yes.GroupBy(c => c.Value).OrderByDescending(g => g.Count()).First();
        var vn = no.GroupBy(c => c.Value).OrderByDescending(g => g.Count()).First();
        if (vy.Key == vn.Key || vy.Count() * 2 <= yes.Count || vn.Count() * 2 <= no.Count) return;
        FactionSplits++;
        AddTension(vy.Key, vn.Key, 0.08f);
    }

    // ───────────────────────────── 결정의 무게 ─────────────────────────────

    public void Record(string title, string topic, int roomId, CrewMember? decider, List<CrewMember> yes, List<CrewMember> no, string hazard)
    {
        Decisions.Add(new DecisionRecord
        {
            Tick = _w.Tick, Title = title, Topic = topic, RoomId = roomId, Decider = decider?.Id ?? -1,
            Yes = yes.Select(c => c.Id).ToList(), No = no.Select(c => c.Id).ToList(), Hazard = hazard,
        });
        if (Decisions.Count > 80) Decisions.RemoveAt(0);
    }

    private static string DeathKind(CrewMember dead)
    {
        string cause = dead.Vitals.InjuryCause ?? "";
        if (cause.Contains("불") || cause.Contains("화상") || cause.Contains("연기") || cause.Contains("열")) return "fire";
        if (cause.Contains("질식") || cause.Contains("산소") || cause.Contains("감압") || cause.Contains("진공") || dead.Vitals.Oxygen < 0.5f) return "air";
        return "";
    }

    /// <summary>누가 죽었다: 그 죽음과 이어지는 결정에 죄책감과 비난이 남고, 다음 정기 회의에 사후 검토를 올린다.</summary>
    public void OnDeath(CrewMember dead, WorkKind? job, Team? team, Room? room)
    {
        var w = _w;
        string kind = DeathKind(dead);
        foreach (var d in Decisions.Where(d => !d.Judged).ToList())
        {
            bool policy = d.Topic.StartsWith("policy:");
            long window = policy ? SimTime.TicksPerDay * 3 : SimTime.Hours(12);
            if (w.Tick - d.Tick > window) continue;
            bool linked = policy
                ? d.Hazard == "crisis" ? w.Command.Active : d.Hazard != "" && d.Hazard == kind
                : room != null && d.RoomId == room.Id;
            if (linked) Weigh(d, dead);
        }
        // 사후 검토 안건
        var p = w.Policies;
        void Q(string id, int to, string why)
        {
            if (to == p[id] || Reviews.Any(r => r.id == id)) return;
            Reviews.Add((id, to, why));
        }
        if (room != null && (room.Purging || room.Inerting || room.EvacuateBy >= 0 || room.ResponseHold))
        {
            var fc = w.Automation.FireCases.FirstOrDefault(f => f.RoomId == room.Id);
            if (fc != null && !fc.Casualty) { fc.Casualty = true; w.Minds.ComputerResult(-0.15f, $"{room.Name} 소화 수순 중에 {Ko.IGa(dead.Name)} 죽었다"); }
            string id = fc?.Method == "vacuum" ? "vacuumfire" : "inertfire";
            Q(id, Math.Min(p[id], 1) == p[id] ? 0 : 1, $"{room.Name} 소화 수순 중에 {Ko.IGa(dead.Name)} 죽었다");
        }
        if (kind == "air" && p["evac"] > 0 && job is not WorkKind.Rescue) Q("evac", p["evac"] - 1, $"{Ko.IGa(dead.Name)} 산소가 묽은 방에서 버티다 죽었다");
        if (team is { Hazard: true, Watcher: < 0 } && p["risktaking"] == 2) Q("risktaking", 0, $"{Ko.IGa(dead.Name)} 혼자 위험한 방에 들어갔다 죽었다");
        if (job == WorkKind.Rescue && p["rescue"] == 0) Q("rescue", 1, $"{Ko.IGa(dead.Name)} 구하러 들어갔다가 죽었다");
        if (w.Command.Active && w.Command.ComputerCommands && p["command"] != 0) Q("command", 0, $"컴퓨터가 지휘하던 중에 {Ko.IGa(dead.Name)} 죽었다");
        if (kind == "fire" && p["firemethod"] == 1) Q("firemethod", 0, $"자동 소화를 기다리다 {Ko.IGa(dead.Name)} 죽었다");
    }

    private void Weigh(DecisionRecord d, CrewMember dead)
    {
        var w = _w;
        d.Judged = true;
        CrewMember? Find(int id) => w.Crew.FirstOrDefault(c => c.Id == id && !c.Dead);
        var decider = Find(d.Decider);
        // 죄책감: 정한 사람과 찬성한 사람
        foreach (var id in d.Yes.Append(d.Decider).Distinct())
        {
            var c = Find(id);
            if (c == null) continue;
            float add = id == d.Decider ? 0.35f : 0.22f;
            _guilt[c.Id] = MathF.Min(1f, Guilt(c) + add);
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.1f);
            AddCred(c, id == d.Decider ? -0.1f : -0.05f);
            Life.Diary(w, c, $"{d.Title} — 내가 {(id == d.Decider ? "정했다" : "찬성했다")}. {Ko.IGa(dead.Name)} 죽었다.");
            Guilts++;
        }
        // 비난: 반대했던 사람 · 죽은 사람과 가까웠던 사람 (회의록이 공개면 찬성한 사람에게, 아니면 선장에게)
        bool open = w.Policies["minutes"] == 0;
        var blamers = w.Crew.Where(c => !c.Dead && c != dead && (d.No.Contains(c.Id) || c.AffinityTo(dead) > 0.3f) && !d.Yes.Contains(c.Id) && c.Id != d.Decider).ToList();
        if (blamers.Count == 0) return;
        Blames++;
        var cap = w.Command.Captain;
        var targets = open ? d.Yes.Append(d.Decider).Distinct().Select(Find).Where(c => c != null).Cast<CrewMember>().ToList()
            : cap != null ? new List<CrewMember> { cap } : new List<CrewMember>();
        foreach (var b in blamers)
        {
            MindSystem.Anger(b, 0.15f); // v13.3 분노
            foreach (var t in targets)
                b.ChangeAffinity(t, t == decider || !open ? -0.08f : -0.04f);
        }
        if (!open || decider == cap)
        {
            w.Command.Trust = MathF.Max(0f, w.Command.Trust - (open ? 0.05f : 0.08f));
            if (cap != null) AddCred(cap, -0.08f);
        }
        var who = targets.FirstOrDefault();
        if (who != null)
            w.History.Add(w, HistoryKind.Decision,
                $"비난: {string.Join("·", blamers.Take(3).Select(c => c.Name))} → {(open ? string.Join("·", targets.Take(3).Select(c => c.Name)) : $"선장 {who.Name}")} — '{d.Title}' 뒤에 {Ko.IGa(dead.Name)} 죽었다",
                null, blamers.Concat(targets).Distinct().ToList(), log: true);
    }

    /// <summary>위기가 사람을 잃지 않고 끝났다: 그동안의 결정에 찬성한 사람의 신용이 오른다.</summary>
    public void OnIncidentClosed(bool deaths)
    {
        if (deaths) return;
        var w = _w;
        foreach (var d in Decisions.Where(d => !d.Judged && w.Tick - d.Tick < SimTime.Hours(12)))
        {
            d.Judged = true;
            foreach (var id in d.Yes.Append(d.Decider).Distinct())
                if (w.Crew.FirstOrDefault(c => c.Id == id && !c.Dead) is CrewMember c) AddCred(c, id == d.Decider ? 0.05f : 0.025f);
            Praises++;
        }
    }

    /// <summary>시험·사고 기록: 사후 검토를 손으로 올린다.</summary>
    public void QueueReview(string id, int to, string why)
    {
        if (Reviews.Any(r => r.id == id)) return;
        Reviews.Add((id, to, why));
    }

    /// <summary>회의록 한 장을 위한 발언 재생 (지금 말하는 사람과 말).</summary>
    public (CrewMember who, string text, bool pro)? Speaking()
    {
        var rec = Session;
        if (rec == null) return null;
        var all = rec.Items.SelectMany(i => i.Speeches.Select(s => (i, s))).ToList();
        if (all.Count == 0) return null;
        long span = Math.Max(1, (_sessionEnd - SessionStart) / (all.Count + 1));
        int k = (int)((_w.Tick - SessionStart) / span);
        if (k >= all.Count) return null;
        var (_, sp) = all[k];
        var who = _w.Crew.FirstOrDefault(c => c.Id == sp.Who);
        return who == null ? null : (who, sp.Text, sp.For);
    }
}

/// <summary>v13.2 정기 회의: 부름을 받으면 회의실(식당)로 가서 앉아 끝날 때까지 있는다.</summary>
public sealed class MeetingActivity : Activity
{
    public override string Id => "meeting";
    public override string Label => "회의";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var m = w.Meetings;
        if (!m.Summoned(c) || m.Venue is not Room v || c.Down || c.Outside) return (0f, "—");
        if (!v.Cells.Any(dist.Reachable)) return (0f, "회의실에 갈 수 없다");
        if (c.Job?.Activity is MeetingActivity) return (0.95f, "회의 중");
        float s = 0.8f + 0.1f * c.Traits.Diligence + (c.Id == w.Command.CaptainId ? 0.1f : 0f);
        return (s, m.Session != null ? "회의가 이미 시작됐다" : $"정기 회의 — {v.Name}에 모인다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var m = w.Meetings;
        if (m.Venue is not Room v) return null;
        // 빈 의자, 없으면 빈 바닥
        var seat = v.Furniture.Where(f => f.Type == FurnitureType.Seat && f.ReservedBy == null && f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0]) && !w.IsSpotTaken(f.UseSpots[0], c))
            .OrderBy(f => dist.Get(f.UseSpots[0])).FirstOrDefault();
        Cell spot;
        if (seat != null) spot = seat.UseSpots[0];
        else
        {
            var free = v.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(x => (x.Center - v.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
            if (free is not Cell f) return null;
            spot = f;
        }
        var table = v.Furniture.Where(f => f.Type is FurnitureType.Table).OrderBy(f => (f.Center - spot.Center).LengthSquared()).FirstOrDefault();
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(75), seat != null ? Pose.Sitting : Pose.Standing, table?.Center ?? v.Center)
        {
            DoneWhen = (cm, world) => !world.Meetings.Summoned(cm),
        });
        var job = new Job(this, "회의", toils) { LogText = $"정기 회의 — {Ko.EuRo(v.Name)} 간다", LogKind = LogKind.Life, TargetRoom = v, InterruptMargin = 0.3f };
        if (seat != null) job.Reserve(seat, c);
        return job;
    }
}
