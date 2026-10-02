using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.16 주컴퓨터 두뇌 2.0 — 권한 · 협상 · 윤리 · 책임 · 말투.
//  권한 수준: 갈래마다(자원 · 일정 · 정비 · 위기) 조언만 / 제안 / 자동 실행. 승무원 회의가 주고 거둔다 (안건 · 토론 · 표결 — Meetings.Hold 훅).
//    위기 갈래는 방침 "컴퓨터 자동 실행"(autoscope)과 같은 것 — 회의가 바꾸면 소화 · 격벽 자동 조치도 따라 바뀐다.
//    컴퓨터가 상하면(등급 III 아래) 추론을 못 해 모든 갈래가 조언만.
//    근거로 안건이 올라온다: 자동 실행에서 실수가 잦으면 축소(컴퓨터가 스스로 청하기도) · 제안이 늘 맞으면 확대.
//  협상: 계획자가 회의 안건을 올리면 여기서 토론 · 표결한다 — 사람마다 컴퓨터 신뢰 · 예측 확신 · 가치관 · 근거 갈래(사례는 겪은 사람에게,
//    가치는 반대했던 사람의 가치관에 맞춰) · 설명을 들었나 · 지난 표 습관.
//  윤리 갈등: 배 우선 ↔ 사람 우선 · 사생활 ↔ 안전 — 방침에 따라 고르고, 갈등을 기록하고 말한다 (불편한 사람의 신뢰가 움직인다).
//  실수와 책임: 틀린 판단이 드러나면 인정하고 사과 방송 · 되돌리기 · 계기 확인 — 다친 신뢰가 일부 돌아온다 (전부는 아니다).
//    v16.6의 틀린 판단(제안 채점)도 사과한다. 실수가 잦으면 겸손해지고(말투) 권한 축소를 스스로 청한다.
//  말투: 배 문화(ComputerVoice.Tone) + 기억(실수 → 겸손 · 맞힘 → 자신) + 확신도를 말에 싣는다.

public enum Domain { Resources, Schedule, Maintenance, Crisis }
public enum AuthLevel { Advise, Propose, Auto }

public sealed record LearnedItem(long Tick, string Kind, string Text);
public sealed record EthicsCase(long Tick, string Kind, string Situation, string Choice, string Basis);
public sealed record AuthorityChange(long Tick, Domain Domain, AuthLevel From, AuthLevel To, string Why, int Yes, int No);

public sealed class Mistake
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public string Key { get; init; } = "";
    public Domain Domain { get; init; }
    public string What { get; init; } = "";
    public string Cause { get; init; } = "";
    public string Fix { get; init; } = "";
    public float TrustBefore { get; set; }
    public float TrustLow { get; set; }
    public float TrustAfter { get; set; }
    public int Heard { get; set; }
}

public sealed class AuthorityReview
{
    public Domain Domain { get; init; }
    public AuthLevel To { get; init; }
    public string Why { get; init; } = "";
    public long Tick { get; init; }
    public bool BySelf { get; init; }
}

public sealed class ComputerAuthority
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7333 + 601));
    private readonly Dictionary<Domain, AuthLevel> _levels = new() { [Domain.Resources] = AuthLevel.Propose, [Domain.Schedule] = AuthLevel.Auto, [Domain.Maintenance] = AuthLevel.Auto };
    private readonly HashSet<string> _admitted = new();
    private long _next;
    private int _mistakeNext = 1;
    private long _wrongTick = -1, _sorryAt = -SimTime.TicksPerDay;
    public List<LearnedItem> LearnedList { get; } = new();
    public List<EthicsCase> Ethics { get; } = new();
    public List<Mistake> Mistakes { get; } = new();
    public List<AuthorityReview> Reviews { get; } = new();
    public List<AuthorityChange> Changes { get; } = new();
    public int Apologies, Debates, Granted, Revoked;
    /// <summary>겸손 0~1 (실수할수록 · 하루에 조금씩 옅어진다).</summary>
    public float Humility { get; private set; }

    public static readonly Domain[] Domains = { Domain.Resources, Domain.Schedule, Domain.Maintenance, Domain.Crisis };

    public ComputerAuthority(World w) => _w = w;

    public static string DomainName(Domain d) => d switch { Domain.Resources => "자원", Domain.Schedule => "일정", Domain.Maintenance => "정비", _ => "위기" };
    /// <summary>틀린 판단의 조치 이름 (v16.6 기록의 종류 → 사람 말).</summary>
    public static string ActName(string kind) => kind switch
    {
        "vacuum" => "진공 소화 제안", "inert" => "질식 소화 제안", "Alarm" => "경보", "Damper" => "댐퍼", "Bulkhead" => "격벽", "Valve" => "밸브", "Breaker" => "차단기",
        "Suppress" => "소화", "Shed" => "부하 차단", "Module" => "모듈", "Zone" => "공기 구역", "Advice" => "조언", "Broadcast" => "방송", "Door" => "문", "Forecast" => "예측", _ => "",
    };

    public static string LevelName(AuthLevel l) => l switch { AuthLevel.Advise => "조언만", AuthLevel.Propose => "제안", _ => "자동 실행" };

    /// <summary>회의가 준 권한 (위기 = 방침 "컴퓨터 자동 실행").</summary>
    public AuthLevel Granted0(Domain d) => d == Domain.Crisis ? (AuthLevel)Math.Clamp(_w.Policies["autoscope"], 0, 2) : _levels[d];

    /// <summary>지금 쓸 수 있는 권한 (컴퓨터가 상해 추론을 못 하면 조언만 · 멎으면 조언도 못 한다).</summary>
    public AuthLevel Level(Domain d)
    {
        var a = _w.Automation;
        var g = Granted0(d);
        if (!a.Present || !a.MainOnline || a.Level < 4 && d != Domain.Crisis) return AuthLevel.Advise;
        return g;
    }

    /// <summary>권한을 바꾼다 (회의 · 시험).</summary>
    public void Set(Domain d, AuthLevel to, string why, int yes = 0, int no = 0)
    {
        var w = _w;
        var from = Granted0(d);
        if (from == to) return;
        if (d == Domain.Crisis) w.Policies.Set("autoscope", (int)to, why, yes, no);
        else _levels[d] = to;
        Changes.Add(new AuthorityChange(w.Tick, d, from, to, why, yes, no));
        if (Changes.Count > 30) Changes.RemoveAt(0);
        if (to > from) Granted++; else Revoked++;
        w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터 권한 — {DomainName(d)}: {LevelName(from)} → {LevelName(to)} ({why})", null, log: true);
        var a = w.Automation;
        a.Speak.Announce(Say(to > from ? $"{DomainName(d)} 권한을 받았다 — 이제 {LevelName(to)}. 결과로 보여 드리겠다" : $"{DomainName(d)} 권한이 {Ko.EuRo(LevelName(to))} 줄었다 — 회의 결정을 따른다"), null, 0);
        Learned("권한", $"{DomainName(d)} {LevelName(from)} → {LevelName(to)} — {why}");
    }

    public void QueueReview(Domain d, AuthLevel to, string why, bool bySelf = false)
    {
        if (Granted0(d) == to || Reviews.Any(r => r.Domain == d)) return;
        Reviews.Add(new AuthorityReview { Domain = d, To = to, Why = why, Tick = _w.Tick, BySelf = bySelf });
    }

    /// <summary>최근 배운 것 (카드 · 관제 화면).</summary>
    public void Learned(string kind, string text)
    {
        var w = _w;
        if (LearnedList.Count > 0 && LearnedList[^1].Text == text) return;
        LearnedList.Add(new LearnedItem(w.Tick, kind, text));
        if (LearnedList.Count > 40) LearnedList.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call} 배움 — {text}");
    }

    // ═══════════════════════════════ 말투 ═══════════════════════════════

    /// <summary>자신감 (오늘 · 어제 맞힌 비율).</summary>
    public float Confidence
    {
        get
        {
            var b = _w.Automation.Book;
            int n = b.Right + b.Wrong;
            return n == 0 ? 0.6f : (b.Right + 1f) / (n + 2f);
        }
    }

    /// <summary>말에 성격을 입힌다: 겸손하면 단서를 달고 · 확신도를 말하고 · 지난 실수를 기억하고 · 배 문화를 닮은 끝맺음.</summary>
    public string Say(string text, float? conf = null)
    {
        var v = _w.Automation.Voice;
        string head = Humility > 0.45f ? "제 판단이 틀릴 수도 있지만 — " : Humility > 0.2f && conf is < 0.6f ? "조심스럽게 말씀드리면 — " : Confidence > 0.85f && Humility < 0.1f ? "" : "";
        string mem = Humility > 0.2f && Mistakes.Count > 0 && conf != null ? $" (지난 {SimTime.Day(Mistakes[^1].Tick)}일처럼 틀리지 않게 두 번 확인했다)" : "";
        string c = conf is float cf && !text.Contains("확신") ? $" (확신 {cf * 100:0}%)" : "";
        return v.Style(head + text + c + mem);
    }

    /// <summary>성격 한 줄 (카드).</summary>
    public string Persona
    {
        get
        {
            var v = _w.Automation.Voice;
            string mood = Humility > 0.45f ? "겸손 — 실수를 기억한다" : Humility > 0.2f ? "조심스러움" : Confidence > 0.8f ? "자신 있음" : "차분함";
            return $"{mood}" + (v.Tone != "" ? $" · 배 문화 '{v.Tone}' 중시를 닮음" : "") + (v.Name != "" ? $" · '{v.Name}'(이)라 불림" : "");
        }
    }

    // ═══════════════════════════════ 윤리 갈등 ═══════════════════════════════

    public void Dilemma(string kind, string situation, string choice, string basis, IEnumerable<CrewMember> people)
    {
        var w = _w;
        var a = w.Automation;
        if (Ethics.Count > 0 && Ethics[^1].Kind == kind && Ethics[^1].Situation == situation && w.Tick - Ethics[^1].Tick < SimTime.Hours(6)) return;
        Ethics.Add(new EthicsCase(w.Tick, kind, situation, choice, basis));
        if (Ethics.Count > 30) Ethics.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call} 윤리 갈등 ({kind}) — {situation} · {choice} · 근거: {basis}");
        if (kind == "배 ↔ 사람")
        {
            a.Speak.Announce(Say($"어려운 선택이었다 — {choice}. {basis}에 따랐다"), null, 1);
            bool shipFirst = choice.Contains("배") || choice.Contains("탱크") || choice.Contains("재배대");
            foreach (var c in people)
            {
                if (c.Dead || c.IsChild) continue;
                if (c.Value == CrewValue.People && shipFirst) a.Trusts.Change(c, -0.02f, $"컴퓨터가 사람보다 배를 앞세웠다 ({situation})", quiet: true);
                else if (c.Value == CrewValue.Efficiency && shipFirst || c.Value == CrewValue.People && !shipFirst) a.Trusts.Change(c, 0.01f, "컴퓨터의 선택이 내 생각과 같다", quiet: true);
            }
        }
        Learned("윤리", $"{kind}: {choice} ({basis})");
    }

    // ═══════════════════════════════ 실수와 책임 ═══════════════════════════════

    /// <summary>틀린 판단을 인정한다: 다친 신뢰 → 사과 방송 → 되돌리기 → 일부 회복. 같은 열쇠는 한 번만.</summary>
    public Mistake? Admit(string key, Domain d, string what, string cause, string fix, IEnumerable<CrewMember> affected, Action? recover = null, float harm = 0.08f)
    {
        var w = _w;
        var a = w.Automation;
        if (!_admitted.Add(key)) return null;
        var ppl = affected.Where(c => !c.Dead && !c.IsChild).ToList();
        var m = new Mistake { Id = _mistakeNext++, Tick = w.Tick, Key = key, Domain = d, What = what, Cause = cause, Fix = fix, TrustBefore = a.Trusts.Average() };
        // 틀린 게 드러났다 — 신뢰가 다친다
        if (harm > 0f)
        {
            foreach (var c in ppl) a.Trusts.Change(c, -harm, $"컴퓨터가 틀렸다 — {what}", quiet: true);
            w.Minds.ComputerResult(-0.04f, $"컴퓨터 판단이 틀렸다 — {what}");
        }
        m.TrustLow = a.Trusts.Average();
        // 사과 · 인정 (들은 사람만) — 잘못을 인정하는 것을 사람 · 규칙을 아끼는 사람은 더 높이 산다
        Humility = MathF.Min(1f, Humility + 0.3f);
        Apologies++;
        var b = a.Speak.Announce(a.Voice.Style($"사과드린다 — {what}. 까닭: {cause}. {fix}"), null, 1);
        recover?.Invoke();
        if (b != null)
            foreach (var id in b.HeardBy)
            {
                if (w.Crew.FirstOrDefault(c => c.Id == id) is not CrewMember c || c.Dead || c.IsChild) continue;
                float back = harm > 0f ? harm * 0.45f : 0.02f;
                if (c.Value is CrewValue.People or CrewValue.Rules) back += 0.015f;
                if (c.Value == CrewValue.Freedom) back -= 0.01f;
                a.Trusts.Change(c, back, "컴퓨터가 잘못을 인정하고 사과했다", quiet: true);
                m.Heard++;
            }
        if (m.Heard > 0) w.Minds.ComputerResult(0.02f, "컴퓨터가 잘못을 인정했다");
        m.TrustAfter = a.Trusts.Average();
        Mistakes.Add(m);
        if (Mistakes.Count > 20) Mistakes.RemoveAt(0);
        w.History.Add(w, HistoryKind.Lesson, $"주 컴퓨터가 잘못을 인정했다 — {what} ({cause})", null, log: true);
        a.Book.Add(ActKind.Broadcast, null, what, $"까닭: {cause}", $"사과 · {fix}", "", "brain:sorry:" + m.Id, 0, 60f,
            (world, act) => (2, $"신뢰 {m.TrustBefore * 100:0}% → {m.TrustLow * 100:0}% → {m.TrustAfter * 100:0}%"));
        Learned("실수", $"{what} — {fix}");
        // 자동 실행에서 사흘 안에 두 번 틀리면 스스로 권한 축소를 청한다
        if (Granted0(d) == AuthLevel.Auto && Mistakes.Count(x => x.Domain == d && w.Tick - x.Tick < SimTime.TicksPerDay * 3) >= (d == Domain.Resources ? 1 : 2))
            QueueReview(d, AuthLevel.Propose, $"{DomainName(d)} 자동 실행에서 틀렸다 — 컴퓨터가 스스로 제안만 하겠다고 청했다", bySelf: true);
        return m;
    }

    /// <summary>부족 경고가 채점됐다 — 들은 사람의 신뢰가 움직인다.</summary>
    internal void ForecastGraded(ShortWarning wn, int score)
    {
        var w = _w;
        var a = w.Automation;
        if (score == 0 || score == 2) return;
        foreach (var id in wn.Heard)
            if (w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember c && !c.Dead)
                a.Trusts.Change(c, score > 0 ? 0.02f : -0.03f, score > 0 ? "컴퓨터 예측이 맞았다" : "컴퓨터 예측이 빗나갔다", quiet: true);
        if (score < 0 && wn.Confidence >= 0.6f)
            Admit("fc:" + wn.Tick + ":" + wn.Key, Domain.Resources, $"예측이 빗나갔다 — {wn.Text}", "흐름이 바뀌었는데 셈에 넣지 못했다", "확신을 낮춰 말하겠다", wn.Heard.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)).Where(c => c != null)!, harm: 0f);
    }

    // ═══════════════════════════════ 회의 ═══════════════════════════════

    /// <summary>Meetings.Hold 훅: 컴퓨터 안건 하나 · 권한 안건 하나.</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> attendees, CrewMember chair)
    {
        var w = _w;
        var a = w.Automation;
        var voters = attendees.Where(c => !c.IsChild && !c.Dead).ToList();
        if (voters.Count < 2 || !a.Present) return;
        var motion = a.Planner.Pitches.FirstOrDefault(p => p.Open && p.Via == "회의");
        if (motion != null) Motion(rec, voters, chair, motion);
        if (Reviews.Count > 0)
        {
            var rv = Reviews[0];
            Reviews.RemoveAt(0);
            Review(rec, voters, chair, rv);
        }
    }

    private float Expertise(CrewMember c) => 0.3f + 0.3f * c.SkillLevel(Skill.Electrical) + 0.2f * c.SkillLevel(Skill.Engineering);

    private void Motion(MeetingRecord rec, List<CrewMember> voters, CrewMember chair, Pitch p)
    {
        var w = _w;
        var a = w.Automation;
        var m = ShipForecast.Models.First(x => x.Key == p.Key);
        var f = a.Outlook.Get(p.Key);
        var item = new AgendaItem
        {
            Title = $"컴퓨터 안건 ({p.Attempt}번째): {p.Option} — {m.Name}", Topic = "computer:" + p.Key, Evidence = p.Basis,
            Computer = $"주 컴퓨터: {p.Basis} · 예상: {p.Effect}", ComputerSign = 1,
        };
        Debates++;
        var final = new Dictionary<CrewMember, (float s, string why)>();
        var (yes, no) = w.Meetings.Debate(voters, c => MotionOpinion(c, p, f), Expertise, item, chair, final);
        bool pass = yes.Count > no.Count || yes.Count == no.Count && yes.Contains(chair);
        item.Passed = pass;
        item.Outcome = pass ? "받았다" : "거절했다";
        rec.Items.Add(item);
        w.Meetings.Split(yes, no);
        w.Meetings.Record(item.Title, item.Topic, -1, chair, yes, no, "");
        foreach (var c in yes) a.CrewModel.Of(c).Accepts++;
        var objectors = no.Select(c => (c.Id, final.TryGetValue(c, out var v) ? v.why : "반대")).ToList();
        w.History.Add(w, HistoryKind.Decision, $"회의: 컴퓨터 안건 \"{p.Option}\" ({m.Name} · {p.Attempt}번째 · 근거 {p.Arg}) — 찬성 {yes.Count} · 반대 {no.Count} → {item.Outcome}", null, voters, log: true);
        a.Planner.Decided(p, pass, yes.Count, no.Count, objectors, chair.Name);
    }

    /// <summary>컴퓨터 안건에 대한 한 사람의 마음: 신뢰 · 확신 · 대책의 무게 · 가치관 · 근거 갈래 · 설명을 들었나 · 습관.</summary>
    private (float, string) MotionOpinion(CrewMember c, Pitch p, ResourceForecast? f)
    {
        var w = _w;
        var a = w.Automation;
        var terms = new List<(float v, string why)>();
        float trust = a.Trusts.Of(c);
        terms.Add(((trust - 0.5f) * 0.8f, trust >= 0.5f ? "컴퓨터 말은 믿을 만하다" : "컴퓨터 말만 믿을 순 없다"));
        float conf = f?.Confidence ?? p.Confidence;
        terms.Add(((conf - 0.5f) * 0.5f, conf >= 0.5f ? $"확신 {conf * 100:0}%라면" : $"확신이 {conf * 100:0}%뿐이다"));
        if (p.BelievedDays <= 2f) terms.Add((0.15f, "날이 얼마 없다"));
        switch (p.Option)
        {
            case "원정":
                terms.Add((-0.25f, "원정은 위험하다"));
                terms.Add(((c.Traits.Bravery - 0.5f) * 0.4f, c.Traits.Bravery > 0.5f ? "해 볼 만하다" : "밖에 사람을 보내기 싫다"));
                terms.Add((c.Value switch { CrewValue.Safety => -0.2f, CrewValue.People => -0.1f, CrewValue.Efficiency => 0.1f, CrewValue.Freedom => 0.05f, _ => 0f },
                    c.Value == CrewValue.Safety ? "안전이 먼저다" : c.Value == CrewValue.People ? "사람을 위험에 보낼 순 없다" : "가져오는 게 빠르다"));
                terms.Add((-0.4f * c.Memory.Trauma, "데인 기억"));
                break;
            case "절수":
                terms.Add((-0.06f, "샤워를 줄이자니 불편하다"));
                terms.Add((c.Value switch { CrewValue.Freedom => -0.15f, CrewValue.Safety => 0.1f, CrewValue.Rules => 0.05f, CrewValue.Efficiency => 0.05f, _ => 0f },
                    c.Value == CrewValue.Freedom ? "물까지 간섭받기 싫다" : "아끼는 게 맞다"));
                terms.Add((-0.1f * c.Needs.Stress, "지쳐서 더 참기 힘들다"));
                break;
            case "엄격 절수":
                terms.Add((-0.18f, "마실 물까지 줄인다"));
                terms.Add((c.Value switch { CrewValue.Freedom => -0.2f, CrewValue.People => -0.1f, CrewValue.Safety => 0.08f, _ => 0f }, c.Value == CrewValue.People ? "사람이 먼저다" : "버티려면 어쩔 수 없다"));
                break;
        }
        var prof = a.CrewModel.Of(c);
        switch (p.Arg)
        {
            case "사례":
                bool lived = c.Memory.Marks.Any(mk => mk.Text.Contains("물") || mk.Text.Contains("목마")) || c.Stats.Emergencies >= 3;
                terms.Add((lived ? 0.2f : 0.1f, lived ? "겪어 봐서 안다" : "지난 예측이 맞았다니"));
                break;
            case "대안":
                terms.Add((0.12f, "위험 없는 쪽이라면"));
                break;
            case "가치":
                var last = a.Planner.Pitches.LastOrDefault(x => x.Key == p.Key && x.Id < p.Id && x.Objectors.Count > 0);
                bool mine = last?.Objectors.Any(o => w.Crew.FirstOrDefault(x => x.Id == o.id)?.Value == c.Value) == true;
                terms.Add((mine ? 0.22f : 0.05f, mine ? "내 걱정을 들었구나" : "그렇게 말하면"));
                break;
        }
        if (prof.Explained > 0 && w.Tick - prof.LastExplained < SimTime.TicksPerDay * 2) terms.Add((0.08f, "따로 설명을 들었다"));
        if (prof.Rejects >= 2) terms.Add((-0.03f * Math.Min(3, prof.Rejects), "늘 반대했다"));
        float s = terms.Sum(t => t.v) + 0.04f * (R.Float() - 0.5f);
        var top = (s > 0f ? terms.Where(t => t.v > 0f).OrderByDescending(t => t.v) : terms.Where(t => t.v < 0f).OrderBy(t => t.v)).FirstOrDefault();
        return (s, top.why ?? (s > 0f ? "받자" : "아직은 아니다"));
    }

    private void Review(MeetingRecord rec, List<CrewMember> voters, CrewMember chair, AuthorityReview rv)
    {
        var w = _w;
        var a = w.Automation;
        var from = Granted0(rv.Domain);
        if (from == rv.To) return;
        bool grant = rv.To > from;
        var item = new AgendaItem
        {
            Title = $"컴퓨터 권한 — {DomainName(rv.Domain)}: {LevelName(from)} → {LevelName(rv.To)}", Topic = "computer:authority:" + rv.Domain, Evidence = rv.Why,
            Computer = rv.BySelf ? $"주 컴퓨터: 스스로 청한다 — {rv.Why}" : $"주 컴퓨터: {(grant ? "맡겨 주시면 결과로 보이겠다" : "회의 뜻에 따르겠다")}", ComputerSign = rv.BySelf || grant ? 1 : 0,
        };
        Debates++;
        var (yes, no) = w.Meetings.Debate(voters, c => ReviewOpinion(c, rv, grant), Expertise, item, chair);
        bool pass = yes.Count > no.Count || yes.Count == no.Count && yes.Contains(chair);
        item.Passed = pass;
        item.Outcome = pass ? "바꿨다" : "그대로";
        rec.Items.Add(item);
        w.Meetings.Split(yes, no);
        w.Meetings.Record(item.Title, item.Topic, -1, chair, yes, no, "crisis");
        if (pass) Set(rv.Domain, rv.To, $"회의 ({chair.Name} 의장) — {rv.Why}", yes.Count, no.Count);
        else w.History.Add(w, HistoryKind.Decision, $"회의: {item.Title}? 찬성 {yes.Count} · 반대 {no.Count} → 그대로", null, voters, log: true);
    }

    private (float, string) ReviewOpinion(CrewMember c, AuthorityReview rv, bool grant)
    {
        var w = _w;
        var a = w.Automation;
        float trust = a.Trusts.Of(c);
        var terms = new List<(float v, string why)>();
        int recent = Mistakes.Count(m => m.Domain == rv.Domain && w.Tick - m.Tick < SimTime.TicksPerDay * 3);
        float record = a.Book.Right + a.Book.Wrong == 0 ? 0.5f : a.Book.Right / (float)(a.Book.Right + a.Book.Wrong);
        if (grant)
        {
            terms.Add(((trust - 0.5f) * 1.0f, trust >= 0.5f ? "컴퓨터를 믿는다" : "컴퓨터에 맡기긴 이르다"));
            terms.Add(((record - 0.6f) * 0.6f, $"맞힌 비율 {record * 100:0}%"));
            terms.Add((-0.15f * recent, "최근에 틀렸다"));
            terms.Add((c.Value switch { CrewValue.Efficiency => 0.15f, CrewValue.Rules => 0.05f, CrewValue.Freedom => -0.2f, CrewValue.People => -0.05f, _ => 0f },
                c.Value == CrewValue.Freedom ? "기계가 정하는 건 싫다" : c.Value == CrewValue.Efficiency ? "맡기면 빠르다" : "절차대로라면"));
        }
        else
        {
            terms.Add((rv.BySelf ? 0.2f : 0f, "컴퓨터가 스스로 청했다"));
            terms.Add(((0.5f - trust) * 0.6f, trust < 0.5f ? "컴퓨터를 못 믿겠다" : "그래도 믿을 만했다"));
            terms.Add((0.1f * recent, "최근에 틀렸다"));
            terms.Add((c.Value switch { CrewValue.Freedom => 0.1f, CrewValue.Safety => 0.05f, CrewValue.Efficiency => -0.1f, _ => 0f }, c.Value == CrewValue.Efficiency ? "일일이 묻게 하면 느리다" : "사람이 정해야 한다"));
            if (Mistakes.LastOrDefault(m => m.Domain == rv.Domain) is Mistake mk && mk.Heard > 0) terms.Add((-0.05f, "잘못을 인정했으니"));
        }
        float s = terms.Sum(t => t.v) + 0.04f * (R.Float() - 0.5f);
        var top = (s > 0f ? terms.Where(t => t.v > 0f).OrderByDescending(t => t.v) : terms.Where(t => t.v < 0f).OrderBy(t => t.v)).FirstOrDefault();
        return (s, top.why ?? (s > 0f ? "바꾸자" : "그대로 두자"));
    }

    // ═══════════════════════════════ 틱 ═══════════════════════════════

    /// <summary>한 시간마다: v16.6 틀린 판단도 사과 · 근거로 권한 안건 · 겸손이 옅어진다.</summary>
    public void Update()
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || w.Tick < _next) return;
        _next = w.Tick + SimTime.Hours(1);
        Humility = MathF.Max(0f, Humility - 0.1f / 24f);
        if (!a.MainOnline) return;
        // v16.6 제안 채점에서 틀린 판단 → 인정 (신뢰는 이미 다쳤다 — 사과만)
        // (열두 시간에 한 번 · 모아서 — 사과가 잦으면 말이 가벼워진다)
        var fresh = new List<(long tick, int roomId, string kind, string why)>();
        long newest = _wrongTick;
        foreach (var x in a.Learn.WrongCalls)
        {
            if (x.tick > _wrongTick && w.Tick - x.tick <= SimTime.Hours(12)) fresh.Add(x);
            newest = Math.Max(newest, x.tick);
        }
        if (fresh.Count > 0 && w.Tick - _sorryAt >= SimTime.Hours(12))
        {
            _wrongTick = newest;
            _sorryAt = w.Tick;
            var rooms = fresh.Select(x => x.roomId >= 0 && x.roomId < w.Ship.Rooms.Count ? w.Ship.Rooms[x.roomId] : null).Where(r => r != null).Distinct().ToList();
            var first = fresh[0];
            string what = fresh.Count == 1 ? $"{(rooms.Count > 0 ? rooms[0]!.Name + " " : "")}{(ActName(first.kind) is string an && an != "" ? an + " " : "")}판단이 틀렸다" : $"최근 판단 {fresh.Count}건이 틀렸다 ({string.Join("·", rooms.Take(3).Select(r => r!.Name))})";
            Admit($"v16:{first.tick}:{first.roomId}:{first.kind}", Domain.Crisis, what, first.why, "같은 방에서는 사람을 먼저 보내 확인하겠다",
                w.Crew.Where(c => !c.Dead && c.Room != null && rooms.Contains(c.Room)), harm: 0f);
        }
        else if (fresh.Count == 0) _wrongTick = newest;
        // 근거로 권한 안건 (여섯 시간마다)
        if (w.Tick % SimTime.Hours(6) >= SimTime.Hours(1)) return;
        foreach (var d in new[] { Domain.Resources, Domain.Schedule, Domain.Maintenance })
        {
            var lvl = Granted0(d);
            int mistakes = Mistakes.Count(m => m.Domain == d && w.Tick - m.Tick < SimTime.TicksPerDay * 3);
            var pitches = a.Planner.Pitches.Where(p => w.Tick - p.Tick < SimTime.TicksPerDay * 4 && p.Grade is (int, string)).ToList();
            int right = pitches.Count(p => p.Grade is (1, _));
            if (lvl == AuthLevel.Auto && mistakes >= 2) QueueReview(d, AuthLevel.Propose, $"사흘 동안 {DomainName(d)} 판단이 {mistakes}번 틀렸다");
            else if (lvl == AuthLevel.Propose && d == Domain.Resources && mistakes == 0 && right >= 3 && a.Trusts.Average() >= 0.55f)
                QueueReview(d, AuthLevel.Auto, $"나흘 동안 자원 대책이 {right}번 맞았다 — 컴퓨터가 자동 실행 권한을 청한다", bySelf: true);
            else if (lvl == AuthLevel.Advise && mistakes == 0 && a.Trusts.Average() >= 0.6f) QueueReview(d, AuthLevel.Propose, "컴퓨터가 다시 제안하게 하자");
        }
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        foreach (var d in Domains) I((int)Granted0(d));
        I(Apologies); I(Debates); I(Granted); I(Revoked); I(Mistakes.Count); I(Ethics.Count); I(LearnedList.Count); I(Reviews.Count); F(Humility);
    }
}
