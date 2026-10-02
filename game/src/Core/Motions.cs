using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.18 승무원이 스스로 여는 회의 · 파벌 · 재판 · 선거.
//
// 누구나 안건을 낸다: 그 사람의 욕구(배고픔 · 지침) · 가치관 · 관계(장부 불만 · 서운함) · 겪은 일(불 · 병 · 사고 뒤 기억)이 동기.
// 안건 → 서명 모으기(직접 찾아가 설득 — 관계 · 말솜씨 · 그 사람이 아는 것) → 서명이 차면 회의 소집
//   (정기 회의에 올리거나, 긴급 회의 · 재판 · 선장 선거 · 사고 조사 · 잔치 의논을 따로 연다).
// 회의: 모여 앉는다 → 의장이 연다 → 발언(각자 근거 — 기억 · 가치관 · 본 것) · 주 컴퓨터가 기록과 예측을 내놓는다(표는 없다) → 설득 → 표결(손 들기 · 비밀).
// 파벌: 안건마다 강하게 같은 쪽에 선 사람끼리 묶이고, 다음 안건에서 다시 갈라지면 흩어진다. 이름은 승무원이 붙인 별명.
// 실행 · 감시: 정한 일을 어기면(배급을 줄였는데 몰래 꺼내 먹는다 · 벌 근무를 빼먹는다) 다시 안건이 된다.
// 소수파: 크게 반대했는데 진 사람은 며칠 불만을 품는다 — 스트레스 · 일기 · 다음 안건에서 그쪽 서명과 찬성을 꺼린다.
// 재판: 본 사람만 증언한다 → 변론 → 처벌 표결(용서 · 경고 · 근무 추가 · 배급 감소 · 특권 박탈) → 관계가 바뀐다.
// 선거: 선장 불신임 → 후보 · 연설(위험 · 컴퓨터에 맡길 몫) → 비밀 투표 → 새 선장의 방침이 바뀐다 · 진 쪽 불만.

public enum MotionKind { Grievance, Proposal, Accusation, Celebration, Crisis, RuleChange, Confidence, Punishment, Allocation, Crew, Practice }
public enum SittingKind { Regular, Emergency, Trial, Election, Inquiry, Feast }
public enum MotionStage { Signing, Ready, Sitting, Decided, Dropped }
public enum Penalty { Forgive, Warning, ExtraDuty, RationCut, Privilege }
public enum LineRole { Chair, Speech, Accuser, Witness, Defense, Computer, Candidate, Hearsay }

public sealed class Motion
{
    public int Id { get; init; }
    public MotionKind Kind { get; init; }
    public SittingKind Sitting { get; init; }
    public int Proposer { get; init; }
    /// <summary>고발당한 사람 · 선장 · 원정에서 뺄 사람 · 불만의 대상.</summary>
    public int Target { get; init; } = -1;
    /// <summary>원정에 넣을 사람.</summary>
    public int Other { get; init; } = -1;
    public string Title { get; init; } = "";
    /// <summary>낸 사람의 동기 (그 사람의 말).</summary>
    public string Why { get; init; } = "";
    public string Policy { get; init; } = "";
    public int To { get; init; } = -1;
    public CustomKind? Custom { get; init; }
    public int Theft { get; init; } = -1;
    public long Born { get; init; }
    public long Deadline { get; set; }
    public int Need { get; set; }
    public bool Secret { get; set; }
    public MotionStage Stage { get; set; }
    public List<int> Signers { get; } = new();
    public HashSet<int> Asked { get; } = new();
    public List<int> Refused { get; } = new();
    /// <summary>서명 종이를 들고 다니는 사람 (낸 사람 · 거드는 사람).</summary>
    public List<int> Carriers { get; } = new();
    // ── 결과 ──
    public AgendaItem? Item { get; set; }
    public long Decided { get; set; } = -1;
    public bool Passed { get; set; }
    public string Outcome { get; set; } = "";
    public Penalty Verdict { get; set; }
    public int Winner { get; set; } = -1;
    public List<(int who, int about, bool thinksAgainst, bool truth)> Guesses { get; } = new();
    public Dictionary<int, float> Final { get; } = new();
    /// <summary>설득 전 처음 생각 (얼마나 마음을 썼나).</summary>
    public Dictionary<int, float> Initial { get; } = new();
    /// <summary>얼마나 마음을 쓰나: 처음과 마지막이 같은 쪽이면 더 큰 쪽 (설득에 밀려 약해져도 처음 마음은 남는다).</summary>
    public float Care(int id) { float f = Final.GetValueOrDefault(id); float i = Initial.GetValueOrDefault(id, f); return MathF.Sign(f) == MathF.Sign(i) ? MathF.Max(MathF.Abs(f), MathF.Abs(i)) : MathF.Abs(f); }
    /// <summary>이 안건에서 생기거나 다시 뭉친 파벌.</summary>
    public List<int> FactionIds { get; } = new();
    /// <summary>낸 사람이 회의에 없어 미룬 횟수.</summary>
    public int Deferred { get; set; }
}

public sealed class Faction
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public int Hue { get; init; }
    public List<int> Members { get; set; } = new();
    public int Leader { get; set; } = -1;
    public long Born { get; init; }
    public long Last { get; set; }
    public int Motions { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public bool Gone { get; set; }
    public string GoneWhy { get; set; } = "";
}

public sealed class Grudge
{
    public int Who { get; init; }
    public int Motion { get; init; }
    /// <summary>원망하는 쪽 (이긴 쪽 앞장선 사람 · 고발한 사람).</summary>
    public int Against { get; init; } = -1;
    public long Since { get; init; }
    public long Until { get; set; }
    public string Why { get; init; } = "";
    public long LastSaid { get; set; } = -1;
}

public sealed class Theft
{
    public int Id { get; init; }
    public int Thief { get; init; }
    public long Tick { get; init; }
    public int RoomId { get; init; } = -1;
    public string Room { get; init; } = "";
    public string Item { get; init; } = "";
    public List<int> Witnesses { get; } = new();
    /// <summary>배급을 줄이기로 한 결정을 어긴 것인가 (그 결정의 안건 번호).</summary>
    public int Breach { get; init; } = -1;
    public bool Noticed { get; set; }
    public bool Accused { get; set; }
    public bool SeenAtOnce { get; set; }
}

public readonly record struct SittingLine(int Who, string Text, bool Pro, LineRole Role);

public sealed class Sitting
{
    public Motion Motion { get; init; } = null!;
    public SittingKind Kind { get; init; }
    public Room Venue { get; init; } = null!;
    public long Called { get; init; }
    public long Opened { get; set; } = -1;
    public long VoteAt { get; set; }
    public long End { get; set; }
    public HashSet<int> Invited { get; } = new();
    public HashSet<int> Present { get; } = new();
    public int Chair { get; set; } = -1;
    public MeetingRecord? Record { get; set; }
    public List<SittingLine> Script { get; } = new();
    public List<int> Voters { get; } = new();
    /// <summary>표결 때 손을 든 사람 (공개 표결) · 비밀이면 투표함에 넣은 사람.</summary>
    public List<int> Hands { get; } = new();
    internal Action? Apply;
}

public sealed class MotionStats
{
    public int Proposed, Signed, Refused, Ready, Dropped, Sittings, Trials, Elections, Inquiries, Feasts, Passed, Failed,
        FactionsBorn, FactionsGone, Grudges, Guesses, WrongGuesses, Thefts, Witnessed, Breaches, DutyDone, DutySkipped, ComputerLines, Testimonies;
    public string Line() =>
        $"안건 {Proposed} · 서명 {Signed}/거절 {Refused} · 소집 {Sittings}(재판 {Trials} · 선거 {Elections} · 조사 {Inquiries} · 잔치 {Feasts}) · 가결 {Passed}/부결 {Failed} · 파벌 {FactionsBorn}/흩어짐 {FactionsGone} · 불만 {Grudges} · 추측 {Guesses}(틀림 {WrongGuesses}) · 빼돌림 {Thefts}(목격 {Witnessed}) · 어김 {Breaches} · 벌 근무 {DutyDone}/빼먹음 {DutySkipped}";
}

public sealed partial class MotionSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6151 + 1187));
    public MotionSystem(World w) => _w = w;

    /// <summary>시험용: 이 시스템을 끈다 (성능 견주기).</summary>
    public static bool Off { get; set; }
    /// <summary>시험용: 저절로 안건을 내지 않는다 (꾸민 장면만).</summary>
    public bool Quiet { get; set; }
    public static long UpdateTicks;

    public List<Motion> All { get; } = new();
    public List<Faction> Factions { get; } = new();
    public List<Grudge> Grudges { get; } = new();
    public List<Theft> Thefts { get; } = new();
    public Sitting? Now { get; private set; }
    public MotionStats Stats { get; } = new();
    /// <summary>지난 회의 (회의록 창).</summary>
    public List<Sitting> Past { get; } = new();

    private int _nextId = 1, _nextFaction = 1, _nextTheft = 1;
    private long _next, _nextMotive, _lastProposed = -1000000;
    private readonly Dictionary<int, long> _proposedAt = new();
    private readonly Dictionary<int, long> _tempted = new();
    private readonly Dictionary<int, long> _stoleAt = new();
    private readonly Dictionary<int, (long until, float share)> _rationCut = new();
    private readonly Dictionary<int, long> _noVote = new();
    /// <summary>벌 근무: 남은 시간(시) · 기한 · 안건.</summary>
    public Dictionary<int, (float hours, long due, int motion)> Duty { get; } = new();
    private readonly Dictionary<int, long> _sawTheft = new();
    private long _feastAt = -1, _feastEnd = -1;
    public Room? FeastRoom { get; private set; }

    public IEnumerable<Motion> Open => All.Where(m => m.Stage is MotionStage.Signing or MotionStage.Ready);
    public Motion? Get(int id) => All.FirstOrDefault(m => m.Id == id);
    private CrewMember? P(int id) => id >= 0 && id < _w.Crew.Count && _w.Crew[id].Id == id ? _w.Crew[id] : _w.Crew.FirstOrDefault(c => c.Id == id);
    private static bool Adult(CrewMember c) => !c.Dead && !c.IsChild && c.Profiled;

    public static string KindName(MotionKind k) => k switch
    {
        MotionKind.Grievance => "불만", MotionKind.Proposal => "제안", MotionKind.Accusation => "고발", MotionKind.Celebration => "축하",
        MotionKind.Crisis => "위기 대응", MotionKind.RuleChange => "규칙 바꾸기", MotionKind.Confidence => "선장 신임", MotionKind.Punishment => "처벌",
        MotionKind.Allocation => "배분", MotionKind.Crew => "원정 인선", _ => "새 관행",
    };

    public static string SittingName(SittingKind k) => k switch
    {
        SittingKind.Emergency => "긴급 회의", SittingKind.Trial => "재판", SittingKind.Election => "선장 선거",
        SittingKind.Inquiry => "사고 조사", SittingKind.Feast => "잔치 의논", _ => "정기 회의",
    };

    public static string PenaltyName(Penalty p) => p switch
    {
        Penalty.Forgive => "용서", Penalty.Warning => "경고", Penalty.ExtraDuty => "근무 추가", Penalty.RationCut => "배급 감소", _ => "특권 박탈",
    };

    // ── 다른 시스템이 읽는 것 ──

    /// <summary>재판에서 배급을 깎인 사람이 한 끼에 받는 몫 (1 = 그대로).</summary>
    public float MealShare(CrewMember c) => _rationCut.TryGetValue(c.Id, out var r) && r.until > _w.Tick ? r.share : 1f;
    /// <summary>특권을 박탈당해 서명 · 표결을 못 한다.</summary>
    public bool NoVote(CrewMember c) => _noVote.TryGetValue(c.Id, out var t) && t > _w.Tick;
    public Grudge? GrudgeOf(CrewMember c) => Grudges.FirstOrDefault(g => g.Who == c.Id && g.Until > _w.Tick);
    public Faction? FactionOf(CrewMember c) => Factions.FirstOrDefault(f => !f.Gone && f.Members.Contains(c.Id));
    public bool Summoned(CrewMember c) => Now is { } s && s.Invited.Contains(c.Id) && (s.End <= 0 || _w.Tick < s.End);
    public bool Feasting => _feastAt >= 0 && _w.Tick >= _feastAt && _w.Tick < _feastEnd;
    /// <summary>승무원이 선장 불신임 서명을 돌리고 있다 (정기 회의의 자동 불신임 대신).</summary>
    public bool ConfidencePending => !Off && All.Any(m => m.Kind == MotionKind.Confidence && m.Stage is MotionStage.Signing or MotionStage.Ready or MotionStage.Sitting);
    public bool SawTheftLately(CrewMember c) => _sawTheft.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Minutes(40);

    // ───────────────────────────── 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        if (w.Tick < _next) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _next = w.Tick + SimTime.Minutes(10);
        Signatures();
        Sittings();
        Watch();
        Mood();
        Feast();
        if (!Quiet && w.Tick >= _nextMotive)
        {
            _nextMotive = w.Tick + SimTime.Hours(1);
            Motives();
            Temptation();
        }
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    // ───────────────────────────── 안건 내기 ─────────────────────────────

    /// <summary>안건을 낸다 (낸 사람은 첫 서명). 서명이 몇 장 필요한지는 회의 종류가 정한다.</summary>
    public Motion Propose(CrewMember c, MotionKind kind, SittingKind sitting, string title, string why, string policy = "", int to = -1,
        int target = -1, int other = -1, CustomKind? custom = null, int theft = -1)
    {
        var w = _w;
        int adults = w.Crew.Count(Adult);
        int need = sitting switch
        {
            SittingKind.Trial => Math.Max(2, (int)MathF.Ceiling(adults * 0.2f)),
            SittingKind.Election => Math.Max(2, (int)MathF.Ceiling(adults / 3f)),
            SittingKind.Emergency => Math.Max(2, (int)MathF.Ceiling(adults * 0.3f)),
            SittingKind.Feast => 2,
            _ => Math.Max(2, (int)MathF.Ceiling(adults * 0.25f)),
        };
        var m = new Motion
        {
            Id = _nextId++, Kind = kind, Sitting = sitting, Proposer = c.Id, Title = title, Why = why, Policy = policy, To = to,
            Target = target, Other = other, Custom = custom, Theft = theft, Born = w.Tick,
            Deadline = w.Tick + (sitting == SittingKind.Emergency ? SimTime.Hours(8) : SimTime.TicksPerDay * 2), Need = Math.Min(need, Math.Max(1, adults - (target >= 0 ? 1 : 0))),
            Secret = sitting is SittingKind.Election or SittingKind.Trial,
        };
        m.Signers.Add(c.Id);
        m.Asked.Add(c.Id);
        m.Carriers.Add(c.Id);
        if (target >= 0) m.Asked.Add(target); // 고발당한 사람 · 선장에게는 서명을 받지 않는다
        All.Add(m);
        if (All.Count > 60) All.RemoveAt(All.FindIndex(x => x.Stage is MotionStage.Decided or MotionStage.Dropped) is int i && i >= 0 ? i : 0);
        _proposedAt[c.Id] = w.Tick;
        Stats.Proposed++;
        w.Log.Add(w.Tick, LogKind.Life, $"안건을 냈다 — {title} · 서명 {need}장이 모이면 {SittingName(sitting)}", c.Id);
        Life.Diary(w, c, $"{title}. {why}. 서명을 받으러 다닌다.");
        c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1);
        if (m.Signers.Count >= m.Need) Ready(m);
        return m;
    }

    private bool Busy(CrewMember c) => _proposedAt.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.TicksPerDay
                                       || Open.Any(m => m.Proposer == c.Id);

    private void Motives()
    {
        var w = _w;
        if (Crisis.Acting(w) || Open.Count() >= 3) return;
        int slot = (int)(w.Tick / SimTime.Hours(1) % 3);
        foreach (var c in w.Crew)
        {
            if (!Adult(c) || !c.CanAct || !c.IsAwake || c.Outside || (c.Id + slot) % 3 != 0 || Busy(c) || NoVote(c)) continue;
            if (Motive(c) is not { } mv) continue;
            if (!R.Chance(Math.Clamp(mv.s, 0f, 0.9f) * 0.5f)) continue;
            if (w.Tick - _lastProposed < SimTime.Hours(2) && mv.s < 0.5f) continue; // 안건이 한꺼번에 쏟아지지 않게 (급한 건 예외)
            _lastProposed = w.Tick;
            mv.make();
            if (Open.Count() >= 3) return;
        }
    }

    /// <summary>이 사람이 지금 내고 싶은 안건 (가장 강한 동기 하나).</summary>
    public (float s, Action make)? Motive(CrewMember c)
    {
        var w = _w;
        (float s, Action make)? best = null;
        void Consider(float s, Action make) { if (s > 0.2f && (best == null || s > best.Value.s)) best = (s, make); }
        bool Recent(MotionKind k) => All.Any(m => m.Kind == k && (m.Stage is MotionStage.Signing or MotionStage.Ready or MotionStage.Sitting || w.Tick - Math.Max(m.Born, m.Decided) < SimTime.TicksPerDay * 2));
        var g = GrudgeOf(c);
        // 1) 배분: 먹을 것이 줄어든다 — 배급을 줄이자 (창고를 보는 사람 · 안전 · 규칙 · 효율)
        float days = FoodPolicy.FoodDays(w);
        int rations = w.Policies["rations"];
        if (rations != 3 && days < 5f && !w.Food.Disabled && !Pending("rations"))
        {
            float s = 0.25f + (5f - days) * 0.1f + c.Value switch { CrewValue.Safety => 0.15f, CrewValue.Rules => 0.12f, CrewValue.Efficiency => 0.12f, CrewValue.People => -0.1f, _ => 0f }
                      + (c.Role == CrewRole.Cook ? 0.2f : 0f) - 0.5f * c.Needs.Hunger;
            string why = c.Role == CrewRole.Cook ? "창고를 날마다 보는데 줄어드는 게 눈에 보인다" : $"먹을 것이 {days:0.#}일치밖에 안 남았다";
            Consider(s, () => Propose(c, MotionKind.Allocation, SittingKind.Regular, "배급을 줄이자", why, "rations", 3));
        }
        // 1b) 배급을 줄였는데 넉넉해졌고 배가 고프다 — 되돌리자 (진 쪽의 앙금도)
        if (rations == 3 && !Pending("rations") && (days > 6f || c.Needs.Hunger > 0.55f))
        {
            float s = 0.15f + 0.5f * c.Needs.Hunger + (days > 6f ? 0.15f : 0f) + (g != null && Get(g.Motion)?.Policy == "rations" ? 0.25f : 0f);
            Consider(s, () => Propose(c, MotionKind.RuleChange, SittingKind.Regular, "배급을 다시 똑같이", c.Needs.Hunger > 0.55f ? "배가 고파서 손이 떨린다" : "이제 먹을 것이 넉넉하다", "rations", 0));
        }
        // 2) 고발: 배급을 빼돌리는 걸 봤다
        foreach (var t in Thefts)
        {
            if (t.Accused || !t.Witnesses.Contains(c.Id) || P(t.Thief) is not { Dead: false } thief) continue;
            float aff = c.AffinityTo(thief) + 0.5f * w.Relations.Trust(c, thief);
            if (aff > 0.4f) continue; // 가까운 사이는 말하지 않는다
            float s = 0.45f + (c.Value == CrewValue.Rules ? 0.25f : c.Value == CrewValue.People ? -0.1f : 0f) - 0.4f * aff + (t.Breach >= 0 ? 0.15f : 0f) + 0.2f * c.Needs.Hunger;
            string why = $"{SimTime.Clock(t.Tick)}에 {t.Room}에서 {Ko.EulReul(t.Item)} 몰래 꺼내 먹는 걸 내 눈으로 봤다";
            var tt = t;
            Consider(s, () =>
            {
                tt.Accused = true;
                Propose(c, MotionKind.Accusation, SittingKind.Trial, $"{thief.Name} 고발 — 배급을 빼돌렸다", why, target: thief.Id, theft: tt.Id);
            });
        }
        // 3) 선장 신임: 믿음이 무너졌거나 선장과 골이 깊다
        if (w.Command.Captain is CrewMember cap && cap != c && !Open.Any(m => m.Kind == MotionKind.Confidence))
        {
            float trust = w.Command.Trust;
            float aff = c.AffinityTo(cap) + 0.5f * w.Relations.Trust(c, cap);
            float s = (0.5f - trust) * 1.4f - 0.45f * aff + 0.25f * c.Mind.Anger + (g != null && g.Against == cap.Id ? 0.2f : 0f) + (w.Meetings.Guilt(cap) > 0.2f ? 0.2f : 0f)
                      - (c.Value == CrewValue.Rules ? 0.15f : 0f);
            if (trust < 0.5f || aff < -0.3f)
            {
                string why = w.Meetings.Guilt(cap) > 0.2f ? "선장이 정한 일로 사람이 다쳤다" : aff < -0.3f ? "선장은 우리 말을 듣지 않는다" : "다들 선장을 못 믿는다";
                Consider(s, () => Propose(c, MotionKind.Confidence, SittingKind.Election, $"선장 {cap.Name} 불신임", why, target: cap.Id));
            }
        }
        // 4) 규칙 바꾸기 · 위기 대응 · 컴퓨터 권한 (그 사람의 몸과 가치관이 바라는 쪽)
        RuleMotive(c, Consider);
        // 5) 축하: 큰일을 넘겼거나 다들 처졌다 — 잔치를 열자 (사교적인 사람)
        if (_feastAt < 0 || w.Tick - _feastEnd > SimTime.TicksPerDay * 3)
        {
            var ev = w.History.Events;
            bool survived = false;
            for (int i = ev.Count - 1; i >= 0 && i >= ev.Count - 40; i--) if (ev[i].Kind == HistoryKind.Recovery && w.Tick - ev[i].Tick < SimTime.Hours(14)) { survived = true; break; }
            float s = (c.Traits.Sociability - 0.45f) + (survived ? 0.25f : 0f) + (0.55f - w.Society.Morale) * 0.8f + (c.Value is CrewValue.People or CrewValue.Freedom ? 0.1f : 0f) - (days < 3f ? 0.35f : 0f);
            if (!Recent(MotionKind.Celebration))
                Consider(s, () => Propose(c, MotionKind.Celebration, SittingKind.Feast, survived ? "큰일을 넘긴 축하 자리" : "다 같이 한 끼 — 잔치를 열자",
                    survived ? "다들 버텼다 — 한 번은 같이 웃어야 한다" : "요즘 다들 얼굴이 굳었다"));
        }
        // 6) 새 관행: 겪은 일이 관행이 되자고 한다
        PracticeMotive(c, Consider);
        // 7) 불만: 궂은일을 남에게 미루는 사람이 있다 (장부 · 서운함)
        foreach (var mem in w.Relations.All)
        {
            if (mem.Who != c.Id || mem.Weight > -0.18f || mem.Reason is not (RelationReason.FreeRide or RelationReason.IgnoredMyWarning or RelationReason.CutInLine)) continue;
            if (P(mem.About) is not { Dead: false } o || Open.Any(m => m.Kind == MotionKind.Grievance && m.Target == o.Id)) continue;
            float s = 0.15f - mem.Weight + (c.Value == CrewValue.Rules ? 0.1f : 0f) + 0.2f * c.Needs.Stress;
            var oo = o; var mm = mem;
            Consider(s, () => Propose(c, MotionKind.Grievance, SittingKind.Regular, $"{oo.Name}에 대한 불만", mm.Text, target: oo.Id));
        }
        // 8) 원정 인선: 못 미더운 사람이 원정대에 들었다 (자원한 사람과 바꾸자)
        if (w.Expedition.Pending is { State: "기다림" } p && p.Team.Count > 0 && !Open.Any(m => m.Kind == MotionKind.Crew))
        {
            foreach (int id in p.Team)
            {
                if (P(id) is not { } mem || mem == c) continue;
                float doubt = -c.AffinityTo(mem) - 0.6f * w.Relations.Trust(c, mem) + (mem.Vitals.Injury > 0.2f ? 0.3f : 0f) + (mem.Needs.Stress > 0.6f ? 0.2f : 0f);
                if (doubt < 0.3f) continue;
                var swap = p.Volunteers.Select(v => P(v.who)).Where(x => x != null && !p.Team.Contains(x.Id) && !x.Dead).OrderByDescending(x => c.AffinityTo(x!)).FirstOrDefault()
                           ?? (p.Team.Contains(c.Id) ? null : c);
                if (swap == null) continue;
                var mm2 = mem; var sw = swap;
                Consider(0.15f + doubt * 0.6f, () => Propose(c, MotionKind.Crew, SittingKind.Regular, $"원정 인선 — {mm2.Name} 대신 {sw.Name}",
                    mm2.Vitals.Injury > 0.2f ? $"{Ko.EunNeun(mm2.Name)} 아직 다친 데가 낫지 않았다" : $"{mm2.Name}에게 목숨을 맡기기 어렵다", target: mm2.Id, other: sw.Id));
                break;
            }
        }
        // 9) 사고 조사: 사람을 잃었다 — 왜 그렇게 됐는지 따져 보자
        if (w.Life.Memorial.Count > 0 && w.Tick - w.Life.Memorial[^1].tick < SimTime.TicksPerDay && !All.Any(m => m.Sitting == SittingKind.Inquiry && m.Born > w.Life.Memorial[^1].tick))
        {
            float s = 0.3f + (c.Value is CrewValue.Safety or CrewValue.Rules ? 0.2f : 0f) + 0.2f * c.Needs.Stress;
            var (name, _, cause) = w.Life.Memorial[^1];
            Consider(s, () => Propose(c, MotionKind.Crisis, SittingKind.Inquiry, $"{name}의 일 — 왜 그렇게 됐나", $"{cause}. 다시는 이러면 안 된다"));
        }
        return best;
    }

    /// <summary>그 방침을 두고 서명을 받는 중이거나, 정한 지(접은 지) 이틀이 안 됐다.</summary>
    private bool Pending(string policy) => All.Any(m => m.Policy == policy && (m.Stage is MotionStage.Signing or MotionStage.Ready or MotionStage.Sitting || _w.Tick - Math.Max(m.Born, m.Decided) < SimTime.TicksPerDay * 2));

    private void RuleMotive(CrewMember c, Action<float, Action> consider)
    {
        var w = _w;
        void Rule(string id, int to, MotionKind kind, SittingKind sitting, float s, string title, string why)
        {
            if (w.Policies[id] == to || Pending(id)) return;
            long set = w.Policies.SetAt(id);
            if (set >= 0 && w.Tick - set < SimTime.TicksPerDay) s -= 0.3f; // 정한 지 하루도 안 됐다
            consider(s, () => Propose(c, kind, sitting, title, why, id, to));
        }
        // 쉴 틈이 없다
        if (w.Policies["leisure"] == 0 && c.Needs.Fatigue > 0.5f && c.Needs.Stress > 0.45f)
            Rule("leisure", 1, MotionKind.RuleChange, SittingKind.Regular, 0.2f + c.Needs.Stress * 0.5f + (c.Value == CrewValue.Freedom ? 0.1f : 0f), "쉬는 시간을 지키자", "쉴 틈이 없어 손이 굳는다");
        // 물이 줄어든다 — 위기 대응
        float wf = w.Water.Capacity > 0 ? w.Water.Level / w.Water.Capacity : 1f;
        if (wf < 0.3f && w.Policies["water"] < 2)
            Rule("water", 2, MotionKind.Crisis, SittingKind.Emergency, 0.3f + (0.3f - wf) * 2f + (c.Value == CrewValue.Safety ? 0.15f : 0f), "물을 엄격하게 아끼자", $"물탱크가 {wf * 100:0}%다 — 오늘 정해야 한다");
        // 겪은 사고가 무섭다 — 비상 훈련을 자주
        if (w.Policies["drills"] < 2 && (c.Fears.Contains(Fear.Fire) || c.Fears.Contains(Fear.Vacuum)) && c.Value == CrewValue.Safety)
            Rule("drills", 2, MotionKind.Crisis, SittingKind.Regular, 0.3f, "비상 훈련을 이틀마다", c.Fears.Contains(Fear.Fire) ? "불길 앞에서 몸이 굳었다 — 손에 익혀야 한다" : "공기가 새던 날 다들 우왕좌왕했다");
        // 고장 나고 고치면 늦다 (정비하는 사람)
        if (w.Policies["maint"] == 1 && c.Role is CrewRole.Technician or CrewRole.Engineer && c.Stats.Repairs >= 3)
            Rule("maint", 0, MotionKind.RuleChange, SittingKind.Regular, 0.25f + MathF.Min(0.2f, c.Stats.Repairs / 40f), "예방 정비를 먼저", "고장 나고 고치면 늘 한밤중이다");
        // 가치관대로: 지금 방침이 내 생각과 다르다 (평소의 안건 — 가치관이 같은 사람끼리 서명이 모인다)
        foreach (var id in Everyday)
        {
            int pref = PolicySystem.Preferred(c, id);
            if (pref == w.Policies[id]) continue;
            var spec = PolicySystem.Spec(id);
            float s = 0.12f + 0.15f * (c.Traits.Sociability - 0.4f) + 0.12f * c.Needs.Stress + (c.Value is CrewValue.Rules or CrewValue.Freedom ? 0.06f : 0f);
            Rule(id, pref, MotionKind.RuleChange, SittingKind.Regular, s, EverydayTitle(id, pref) ?? $"{spec.Name}: {spec.Options[pref]}", EverydayWhy(id, pref) ?? ValueWhy(c.Value));
        }
        // 컴퓨터에 맡길 몫: 데인 사람은 줄이자 · 믿는 사람은 늘리자
        if (c.ComputerFaith >= 0f)
        {
            int cur = w.Policies["autoscope"];
            if (c.ComputerFaith < 0.25f && cur > 0)
                Rule("autoscope", cur - 1, MotionKind.Proposal, SittingKind.Regular, 0.2f + (0.25f - c.ComputerFaith) * 1.5f, "컴퓨터가 혼자 하는 일을 줄이자", "컴퓨터가 혼자 문을 닫는 걸 봤다 — 사람이 정해야 한다");
            else if (c.ComputerFaith > 0.8f && cur < 2)
                Rule("autoscope", cur + 1, MotionKind.Proposal, SittingKind.Regular, 0.1f + (c.ComputerFaith - 0.8f) * 1.5f, "컴퓨터에 더 맡기자", "밤에도 컴퓨터는 깨어 있다");
        }
    }

    private static readonly string[] Everyday = { "privacy", "nightwatch", "conflict", "violations", "memorial", "leisure", "drills" };

    private static string? EverydayTitle(string id, int to) => (id, to) switch
    {
        ("privacy", 0) => "공간은 다 같이 쓰자", ("privacy", _) => "개인 공간을 지켜 주자",
        ("nightwatch", 0) => "야간 당직을 한 명 세우자", ("nightwatch", 1) => "야간 당직을 두 명으로", ("nightwatch", _) => "밤에는 컴퓨터에 맡기자",
        ("conflict", 0) => "다툼은 누가 가운데 서서 풀자", ("conflict", 1) => "다툼은 선장이 정하자", ("conflict", _) => "다툼은 당사자끼리 풀자",
        ("violations", 0) => "규칙을 어기면 경고만", ("violations", 1) => "규칙을 어기면 근무에서 빼자", ("violations", _) => "규칙 위반을 따지지 말자",
        ("memorial", 0) => "기념일마다 떠난 사람 이름을 부르자", ("memorial", _) => "추모는 조용히 하자",
        ("leisure", 0) => "일부터 하자", ("leisure", 1) => "일과 쉼을 반반으로", ("leisure", _) => "쉬는 시간을 보장하자",
        ("drills", 0) => "비상 훈련을 그만하자", ("drills", 1) => "비상 훈련은 주에 한 번", ("drills", _) => "비상 훈련을 이틀마다",
        _ => null,
    };

    private static string? EverydayWhy(string id, int to) => (id, to) switch
    {
        ("privacy", 1) => "남의 침대에 걸터앉는 사람이 있다", ("privacy", _) => "다 같이 쓰면 자리가 남는다",
        ("nightwatch", 2) => "밤엔 컴퓨터가 더 잘 본다", ("nightwatch", _) => "밤에 아무도 안 보면 불안하다",
        ("conflict", 0) => "다투면 누가 가운데 서 줘야 한다", ("conflict", 1) => "선장이 정하면 빨리 끝난다", ("conflict", _) => "어른끼리 알아서 한다",
        ("violations", 0) => "한 번 실수로 근무까지 빼는 건 심하다", ("violations", 1) => "말로 해서는 안 바뀐다", ("violations", _) => "서로 감시하는 배는 싫다",
        ("memorial", 0) => "떠난 사람 이름을 불러야 한다", ("memorial", _) => "조용히 기억하는 게 낫다",
        ("leisure", 0) => "일이 밀렸다", ("leisure", 1) => "쉬어야 오래 간다", ("leisure", _) => "쉬는 시간은 지켜 줘야 한다",
        ("drills", 0) => "훈련하느라 일이 멈춘다", ("drills", 1) => "주에 한 번이면 된다", ("drills", _) => "손에 익어야 산다",
        _ => null,
    };

    private void PracticeMotive(CrewMember c, Action<float, Action> consider)
    {
        var w = _w;
        void Practice(CustomKind k, float s, string title, string why)
        {
            if (w.Culture.Of(k) != null || Open.Any(m => m.Custom == k)) return;
            consider(s, () => Propose(c, MotionKind.Practice, SittingKind.Regular, title, why, custom: k));
        }
        if (c.Fears.Contains(Fear.Fire)) Practice(CustomKind.FireCheck, 0.2f + (c.Value is CrewValue.Rules or CrewValue.Safety ? 0.15f : 0f), "날마다 소화기 자리를 보자", "불이 났을 때 소화기가 제자리에 없었다");
        if (DiseaseSystem.Sick(c) || c.Fears.Contains(Fear.Disease)) Practice(CustomKind.HandWash, 0.25f, "밥 먹기 전에 손을 씻자", "병이 한 사람씩 옮아 갔다");
        float wf = w.Water.Capacity > 0 ? w.Water.Level / w.Water.Capacity : 1f;
        if (wf < 0.45f) Practice(CustomKind.WaterThrift, 0.15f + (0.45f - wf), "씻는 물을 아끼자", "물탱크 눈금이 날마다 내려간다");
        if (c.Fears.Contains(Fear.Spacewalk)) Practice(CustomKind.HatchBuddy, 0.2f, "밖에 나갈 땐 둘이 서로 봐 주자", "혼자 나갔다가 줄이 엉켰다");
    }

    // ───────────────────────────── 서명 ─────────────────────────────

    /// <summary>{Ko.IGa(asker)} {who}에게 서명을 부탁한다 — 안건에 대한 생각 · 관계 · 말솜씨 · 그 사람이 아는 것.</summary>
    public bool Ask(CrewMember asker, CrewMember who, Motion m)
    {
        var w = _w;
        m.Asked.Add(who.Id);
        if (NoVote(who)) { m.Refused.Add(who.Id); return false; }
        var (op, why) = Opinion(who, m);
        float s = op + 0.3f * who.AffinityTo(asker) + 0.25f * w.Relations.Trust(who, asker) + 0.25f * (w.Meetings.Persuasion(asker, 0.3f) - 0.35f);
        if (m.Theft >= 0 && Thefts.FirstOrDefault(t => t.Id == m.Theft) is Theft th && th.Witnesses.Contains(who.Id)) s += 0.6f; // 자기도 봤다
        if (FactionOf(who) is Faction f && f.Members.Contains(asker.Id)) s += 0.2f;
        if (GrudgeOf(who) is Grudge g && (g.Against == asker.Id || FactionOf(asker) is Faction fa && fa.Members.Contains(g.Against))) s -= 0.3f;
        // 서명은 '회의에서 이야기해 보자'는 뜻이라 찬성보다 문턱이 낮다 — 고발 · 불신임은 이름을 거는 일이라 조금 더 신중하다
        bool sign = s > (m.Kind is MotionKind.Accusation or MotionKind.Confidence ? 0f : -0.06f);
        if (sign)
        {
            m.Signers.Add(who.Id);
            Stats.Signed++;
            w.Relations.Remember(asker, who, RelationReason.BackedMe, $"'{m.Title}'에 서명해 줬다");
            // 강하게 찬성하는 말 잘하는 사람은 종이를 한 장 더 들고 다닌다
            if (m.Carriers.Count < 2 && op > 0.45f && who.Traits.Sociability > 0.55f && !who.IsChild) m.Carriers.Add(who.Id);
            w.Log.Add(w.Tick, LogKind.Life, $"{asker.Name}의 '{m.Title}'에 서명했다 — {why}", who.Id);
        }
        else
        {
            m.Refused.Add(who.Id);
            Stats.Refused++;
            asker.ChangeAffinity(who, -0.01f);
            w.Log.Add(w.Tick, LogKind.Life, $"{asker.Name}의 서명 부탁을 거절했다 — {why}", who.Id);
        }
        if (m.Stage == MotionStage.Signing && m.Signers.Count >= m.Need) Ready(m);
        return sign;
    }

    private void Ready(Motion m)
    {
        var w = _w;
        m.Stage = MotionStage.Ready;
        Stats.Ready++;
        var who = P(m.Proposer);
        string when = m.Sitting == SittingKind.Regular ? "다음 정기 회의에 올린다" : $"{Ko.EulReul(SittingName(m.Sitting))} 연다";
        w.Log.Add(w.Tick, LogKind.Ship, $"서명 {m.Signers.Count}장이 모였다 — '{m.Title}' · {when}", m.Proposer);
        if (who != null) Life.Diary(w, who, $"서명 {m.Signers.Count}장이 모였다. {when}.");
    }

    private void Signatures()
    {
        var w = _w;
        foreach (var m in All)
        {
            if (m.Stage != MotionStage.Signing && m.Stage != MotionStage.Ready) continue;
            var who = P(m.Proposer);
            bool gone = who == null || who.Dead || m.Target >= 0 && P(m.Target) is not { Dead: false };
            if (!gone && w.Tick < m.Deadline) continue;
            if (m.Stage == MotionStage.Ready && !gone && w.Tick < m.Deadline + SimTime.TicksPerDay * 2) continue; // 모인 안건은 회의를 기다린다
            m.Stage = MotionStage.Dropped;
            Stats.Dropped++;
            if (who != null && !who.Dead && !gone)
            {
                who.Needs.Stress = MathF.Min(1f, who.Needs.Stress + 0.04f);
                Life.Diary(w, who, $"'{m.Title}' — 서명이 {m.Signers.Count}장뿐이었다. 아무도 들어주지 않았다.");
                w.Log.Add(w.Tick, LogKind.Life, $"'{m.Title}' 서명이 모이지 않아 접었다 ({m.Signers.Count}/{m.Need})", who.Id);
            }
        }
    }

    /// <summary>다음에 서명을 받으러 갈 사람: 가까운 사이 · 같은 편부터, 깨어 있는 사람.</summary>
    public CrewMember? NextAsk(CrewMember asker, Motion m)
    {
        var w = _w;
        CrewMember? best = null;
        float bs = float.MinValue;
        foreach (var c in w.Crew)
        {
            if (c == asker || !Adult(c) || !c.CanAct || !c.IsAwake || c.Outside || m.Asked.Contains(c.Id)) continue;
            if (c.Job?.Activity is SittingActivity or MeetingActivity) continue;
            float s = asker.AffinityTo(c) + (FactionOf(c) is Faction f && f.Members.Contains(asker.Id) ? 0.3f : 0f) - 0.03f * (c.Position - asker.Position).Length()
                      + (m.Theft >= 0 && Thefts.FirstOrDefault(t => t.Id == m.Theft)?.Witnesses.Contains(c.Id) == true ? 0.8f : 0f);
            if (s > bs) { bs = s; best = c; }
        }
        return best;
    }

    // ───────────────────────────── 정기 회의에 올리기 ─────────────────────────────

    /// <summary>정기 회의 안건 (MeetingSystem.Hold 에서): 서명이 모인 안건 둘까지.</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> attendees, CrewMember chair)
    {
        if (Off) return;
        var ready = All.Where(m => m.Stage == MotionStage.Ready && (m.Sitting == SittingKind.Regular || m.Sitting == SittingKind.Inquiry && Now == null)).Take(2).ToList();
        foreach (var m in ready)
        {
            var voters = attendees.Where(c => !NoVote(c) && !_w.Society.OnProbation(c) && c.Id != m.Target).ToList();
            if (voters.Count < 2) continue;
            // 낸 사람이 자리에 없으면 다음 회의로 미룬다 (두 번까지)
            if (!attendees.Any(c => c.Id == m.Proposer) && m.Deferred < 2 && P(m.Proposer) is { Dead: false }) { m.Deferred++; m.Deadline = Math.Max(m.Deadline, _w.Tick + SimTime.TicksPerDay); continue; }
            var lines = new List<SittingLine>();
            var item = Resolve(m, voters, attendees, chair, lines, out var apply);
            rec.Items.Add(item);
            apply?.Invoke();
        }
    }
}
