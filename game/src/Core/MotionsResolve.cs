using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.18 회의를 연다 · 발언 · 설득 · 표결 · 파벌 · 소수파 불만 · 비밀 투표 추측 · 재판 · 선거 · 회의록.
public sealed partial class MotionSystem
{
    // ───────────────────────────── 의견 ─────────────────────────────

    /// <summary>이 사람이 이 안건을 어떻게 보나 (+ 찬성 · − 반대) 와 그 근거 (자기 말).</summary>
    public (float s, string why) Opinion(CrewMember c, Motion m)
    {
        var w = _w;
        if (c.Id == m.Proposer) return (0.9f, m.Why);
        var t = new List<(float v, string why)>();
        var prop = P(m.Proposer);
        float days = FoodPolicy.FoodDays(w);
        if (m.Policy != "")
        {
            int pref = PolicySystem.Preferred(c, m.Policy), cur = w.Policies[m.Policy];
            if (pref == m.To) t.Add((0.25f, ValueWhy(c.Value)));
            else if (pref == cur) t.Add((-0.2f, "지금대로가 낫다"));
            switch (m.Policy)
            {
                case "rations":
                {
                    float sign = m.To == 3 ? 1f : -1f;
                    // 창고를 보는 사람(조리 · 재배 · 안전을 따지는 사람)은 남은 날을 무겁게 본다
                    float knows = c.Role is CrewRole.Cook or CrewRole.Botanist || c.Value == CrewValue.Safety ? 1f : 0.5f;
                    t.Add((sign * knows * MathF.Min(0.45f, (4.5f - days) * 0.12f), days < 4.5f ? $"이대로면 {days:0.#}일 뒤 바닥난다" : "아직은 먹을 것이 있다"));
                    float h = c.Needs.Hunger;
                    t.Add((-sign * (h > 0.3f ? 0.8f * (h - 0.3f) : -0.05f), h > 0.4f ? "배가 고프면 손이 안 움직인다" : "조금 덜 먹어도 버틴다"));
                    if (c.Value == CrewValue.Freedom) t.Add((-sign * 0.12f, "먹는 것까지 정해 두면 숨 막힌다"));
                    if (c.Role is CrewRole.Engineer or CrewRole.Technician) t.Add((-sign * 0.15f, "일하는 사람은 먹어야 한다"));
                    if (c.Traits.Appetite > 1.1f) t.Add((-sign * 0.5f * (c.Traits.Appetite - 1f), "원래 먹는 양이 많다"));
                    if (c.Role == CrewRole.Cook) t.Add((sign * 0.25f, "창고를 날마다 보면 안다"));
                    if (c.Value == CrewValue.People) t.Add((-sign * 0.12f, "아픈 사람 몫까지 줄일 순 없다"));
                    break;
                }
                case "leisure": t.Add((0.6f * (c.Needs.Fatigue - 0.35f) + 0.3f * (c.Needs.Stress - 0.3f), c.Needs.Fatigue > 0.4f ? "다들 지쳤다" : "")); if (c.Value == CrewValue.Efficiency) t.Add((-0.2f, "일이 밀린다")); break;
                case "water": { float wf = w.Water.Capacity > 0 ? w.Water.Level / w.Water.Capacity : 1f; t.Add(((0.35f - wf) * 1.5f, wf < 0.35f ? "물탱크 눈금을 봐라" : "물은 아직 있다")); if (c.Value == CrewValue.Freedom) t.Add((-0.15f, "씻는 것까지 재면 숨 막힌다")); break; }
                case "drills": t.Add((c.Value == CrewValue.Safety ? 0.25f : c.Value == CrewValue.Efficiency ? -0.2f : 0f, c.Value == CrewValue.Efficiency ? "훈련하느라 일이 멈춘다" : "손에 익어야 산다")); if (c.Fears.Contains(Fear.Fire) || c.Fears.Contains(Fear.Vacuum)) t.Add((0.2f, "그날을 잊을 수 없다")); break;
                case "maint": if (c.Role is CrewRole.Technician or CrewRole.Engineer) t.Add((0.2f, "미리 손보면 밤에 안 깬다")); break;
                case "autoscope":
                    if (c.ComputerFaith >= 0f) t.Add((MathF.Sign(m.To - cur) * (c.ComputerFaith - 0.5f) * 0.9f, c.ComputerFaith < 0.4f ? "컴퓨터가 혼자 문을 닫는 건 싫다" : "컴퓨터는 지치지 않는다"));
                    break;
            }
        }
        switch (m.Kind)
        {
            case MotionKind.Practice:
                t.Add((c.Value is CrewValue.Rules or CrewValue.Safety ? 0.2f : c.Value == CrewValue.Freedom ? -0.25f : 0.05f, c.Value == CrewValue.Freedom ? "그런 걸 정해 두면 숨 막힌다" : "몸에 배야 위급할 때 나온다"));
                break;
            case MotionKind.Celebration:
                t.Add((0.45f * (c.Needs.Stress - 0.25f) + 0.3f * (c.Traits.Sociability - 0.45f), c.Needs.Stress > 0.35f ? "숨 좀 돌리자" : "한 번쯤은 괜찮다"));
                if (days < 3f) t.Add((-0.35f, "먹을 것도 모자란데 잔치라니"));
                if (c.Value is CrewValue.Efficiency or CrewValue.Rules) t.Add((-0.12f, "일이 밀렸다"));
                if (FactionOf(c) is Faction rf && Get(rf.Motions > 0 ? LastMotionOf(rf) : -1) is Motion fm && fm.Policy == "rations" && fm.To == 3 && fm.Final.GetValueOrDefault(c.Id) > 0f)
                    t.Add((-0.2f, "배급을 줄여 놓고 잔치는 말이 안 된다"));
                break;
            case MotionKind.Grievance when P(m.Target) is CrewMember g:
                t.Add((-0.45f * c.AffinityTo(g) - 0.35f * w.Relations.Trust(c, g), c.AffinityTo(g) > 0.2f ? $"{g.Name}도 사정이 있다" : $"{Ko.EunNeun(g.Name)} 늘 그렇다"));
                if (c.Value == CrewValue.People) t.Add((-0.12f, "사람을 앞에 세워 놓고 따지는 건 아니다"));
                if (c.Id == g.Id) t.Add((-2f, "억울하다"));
                break;
            case MotionKind.Crew when P(m.Target) is CrewMember out1 && P(m.Other) is CrewMember in1:
                t.Add((0.35f * (c.AffinityTo(in1) - c.AffinityTo(out1)) + (out1.Vitals.Injury > 0.2f ? 0.25f : 0f), out1.Vitals.Injury > 0.2f ? $"{Ko.EunNeun(out1.Name)} 아직 다친 데가 있다" : $"{Ko.IGa(in1.Name)} 낫다"));
                if (c.Id == out1.Id) t.Add((-1f, "내가 가야 한다"));
                if (c.Id == in1.Id) t.Add((0.6f, "내가 가겠다"));
                break;
            case MotionKind.Confidence when w.Command.Captain is CrewMember cap:
                t.Add((0.5f - w.Command.Trust, w.Command.Trust < 0.3f ? "아무도 선장을 믿지 않는다" : "믿음이 흔들린다"));
                t.Add((-0.5f * c.AffinityTo(cap) - 0.3f * w.Relations.Trust(c, cap), c.AffinityTo(cap) > 0.2f ? "선장 편이다" : "선장과 사이가 나쁘다"));
                if (w.Meetings.Guilt(cap) > 0.2f) t.Add((0.15f, "선장이 정한 일로 사람이 다쳤다"));
                if (c.Value == CrewValue.Rules) t.Add((-0.12f, "선장을 함부로 바꾸면 안 된다"));
                if (c == cap) t.Add((-2f, "끝까지 맡겠다"));
                break;
            case MotionKind.Accusation when P(m.Target) is CrewMember ac:
                t.Add((c.Value switch { CrewValue.Rules => 0.3f, CrewValue.Safety => 0.15f, CrewValue.Efficiency => 0.12f, CrewValue.People => -0.12f, _ => -0.05f }, c.Value == CrewValue.People ? "따로 불러 이야기하면 될 일이다" : "그냥 넘어가면 다음에 또 그런다"));
                t.Add((0.35f * c.Needs.Hunger, "다들 줄여 먹는데"));
                t.Add((-0.6f * c.AffinityTo(ac) - 0.3f * w.Relations.Trust(c, ac), c.AffinityTo(ac) > 0.2f ? $"{Ko.IGa(ac.Name)} 그랬을 리 없다" : $"{ac.Name}{(Ko.IGa(ac.Name).EndsWith("이") ? "이라면" : "라면")} 그럴 만하다"));
                if (c.Id == ac.Id) t.Add((-2f, "억울하다"));
                break;
            case MotionKind.Crisis when m.Sitting == SittingKind.Inquiry:
                t.Add((c.Value is CrewValue.Safety or CrewValue.Rules ? 0.3f : 0.1f, "누가 무엇을 봤는지 맞춰 봐야 한다"));
                if (w.Meetings.Guilt(c) > 0.1f) t.Add((-0.3f, "그때는 그게 최선이었다"));
                break;
            case MotionKind.Punishment when P(m.Target) is CrewMember pt:
                t.Add((c.Value == CrewValue.Rules ? 0.3f : 0.1f, "정한 벌은 지켜야 한다"));
                t.Add((-0.4f * c.AffinityTo(pt), c.AffinityTo(pt) > 0.2f ? $"{pt.Name}에게 사정이 있었겠지" : ""));
                if (c.Id == pt.Id) t.Add((-2f, "그 정도면 됐다"));
                break;
        }
        // 낸 사람과의 사이 · 지난 안건의 앙금 · 같은 편 · 컴퓨터 기록
        if (prop != null)
        {
            float rel = 0.22f * c.AffinityTo(prop) + 0.15f * w.Relations.Trust(c, prop);
            // 그 사람에 대한 기억이 근거가 된다 (구해 줬다 · 내 경고를 무시했다 · 서명해 줬다…)
            var mem = MathF.Abs(rel) > 0.05f ? w.Relations.Why(c, prop) : null;
            string relWhy = mem != null && MathF.Abs(mem.Weight) >= 0.15f && MathF.Sign(mem.Weight) == MathF.Sign(rel) ? $"{Ko.EunNeun(prop.Name)} {mem.Text}"
                : rel > 0f ? $"{prop.Name}의 말이라면 믿는다" : $"{Ko.IGa(prop.Name)} 낸 안건이라";
            if (MathF.Abs(rel) > 0.05f) t.Add((rel, relWhy));
            if (GrudgeOf(c) is Grudge g && (g.Against == prop.Id || FactionOf(prop) is Faction pf && pf.Members.Contains(g.Against)))
                t.Add((-0.25f, $"지난번 '{Get(g.Motion)?.Title ?? "그 일"}' 때도 저쪽 뜻대로였다"));
            if (FactionOf(c) is Faction f && f.Members.Contains(prop.Id)) t.Add((0.15f, "우리 쪽 안건이다"));
        }
        if (m.Item is { ComputerSign: not 0 } it && c.ComputerFaith >= 0f)
            t.Add((0.3f * it.ComputerSign * (c.ComputerFaith - 0.3f), it.ComputerSign > 0 ? "컴퓨터 기록도 그렇게 말한다" : "컴퓨터 기록은 반대다"));
        float s = t.Sum(x => x.v);
        var why = (s > 0f ? t.Where(x => x.v > 0f && x.why != "").OrderByDescending(x => x.v) : t.Where(x => x.v < 0f && x.why != "").OrderBy(x => x.v)).Select(x => x.why).FirstOrDefault()
                  ?? (s > 0f ? "해 볼 만하다" : "글쎄다");
        return (s, why);
    }

    private int LastMotionOf(Faction f) => All.Where(x => x.Decided >= 0 && x.Final.Count > 0 && f.Members.Count(id => x.Final.ContainsKey(id)) >= 2).Select(x => x.Id).DefaultIfEmpty(-1).Last();

    private static string ValueWhy(CrewValue v) => v switch
    {
        CrewValue.Safety => "안전이 먼저다", CrewValue.Efficiency => "배가 살아야 모두 산다", CrewValue.People => "사람이 먼저다",
        CrewValue.Rules => "정해 둔 대로 해야 한다", _ => "현장에서 판단하게 두자",
    };

    private float Expertise(CrewMember c, Motion m) => m.Policy switch
    {
        "rations" => c.Role == CrewRole.Cook ? 0.75f : 0.25f + 0.3f * c.SkillLevel(Skill.Cooking),
        "leisure" => c.Role == CrewRole.Medic ? 0.7f : 0.3f,
        "autoscope" => 0.25f + 0.4f * c.SkillLevel(Skill.Electrical),
        "maint" => 0.25f + 0.4f * c.SkillLevel(Skill.Engineering),
        _ => 0.3f + 0.3f * c.Traits.Diligence,
    };

    // ───────────────────────────── 주 컴퓨터의 근거 (표는 없다) ─────────────────────────────

    private void ComputerAdvice(Motion m, AgendaItem item)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.CoreOnline) return;
        float days = FoodPolicy.FoodDays(w);
        string? text = null;
        int sign = 0;
        int Recent(HistoryKind k, float d)
        {
            int n = 0;
            var ev = w.History.Events;
            for (int i = ev.Count - 1; i >= 0 && w.Tick - ev[i].Tick < SimTime.TicksPerDay * d; i--) if (ev[i].Kind == k) n++;
            return n;
        }
        switch (m.Policy)
        {
            case "rations":
            {
                var f = a.Outlook.Get("food");
                float grow = FoodPolicy.GrowingPerDay(w), need = w.Crew.Count(c => !c.Dead) * FoodPolicy.MealsPerPersonDay;
                bool shortRun = f != null ? f.DaysToShort < 30f : grow < need;
                float later = days / FoodPolicy.RationDecay - days;
                string head = $"식량 {days:0.#}일치 · 재배 하루 {grow:0}끼 · 먹는 양 하루 {need:0}끼";
                text = m.To == 3
                    ? days < 0.3f ? $"{head} · 창고가 이미 비었습니다 — 배급을 줄여도 늦출 날이 없습니다"
                    : shortRun ? $"{head} · {(f != null ? f.Line + " · " : "")}배급을 줄이면 바닥나는 날이 {later:0.#}일 늦어집니다" : $"{head} · 지금 속도면 바닥나지 않습니다"
                    : $"{head} · 배급을 풀면 하루 {need * (1f - FoodPolicy.RationDecay):0.#}끼가 더 나갑니다";
                sign = m.To == 3 ? (shortRun && days < 4.5f ? 1 : -1) : (days > 6f || !shortRun ? 1 : -1);
                break;
            }
            case "water": text = a.Outlook.Get("water")?.Line ?? "물 기록이 없습니다"; sign = w.Water.Level < w.Water.Capacity * 0.3f ? 1 : 0; break;
            case "drills": case "maint": { int inc = Recent(HistoryKind.Incident, 3f); text = $"지난 사흘 사고 {inc}건 · 고장 {Recent(HistoryKind.Damage, 3f)}건 기록"; sign = inc > 0 ? 1 : 0; break; }
            case "autoscope": text = $"지난 사흘 제가 남긴 판단 기록 {a.Reasoning.Count(r => w.Tick - r.tick < SimTime.TicksPerDay * 3)}줄 · 정하는 건 여러분입니다"; break;
        }
        if (text == null)
            switch (m.Kind)
            {
                case MotionKind.Celebration: text = $"식량 {days:0.#}일치 · 잔치에 {w.Crew.Count(c => !c.Dead) / 2}끼쯤 듭니다"; sign = days < 3f ? -1 : 0; break;
                case MotionKind.Confidence when w.Command.Captain is CrewMember cap:
                    text = $"지난 사흘 선장이 정한 일 {w.Meetings.Decisions.Count(d => d.Decider == cap.Id && w.Tick - d.Tick < SimTime.TicksPerDay * 3)}건 · 다치거나 쓰러진 기록 {Recent(HistoryKind.Casualty, 3f)}건";
                    break;
                case MotionKind.Crisis when m.Sitting == SittingKind.Inquiry:
                {
                    var ev = w.History.Events.LastOrDefault(e => e.Kind is HistoryKind.Death or HistoryKind.Casualty);
                    text = ev != null ? $"{SimTime.Clock(ev.Tick)} 기록 — {ev.Text}" : "그날 기록이 남아 있지 않습니다";
                    break;
                }
                case MotionKind.Crew when w.Expedition.Pending?.Computer is string pc: text = pc; sign = 0; break;
                case MotionKind.Grievance when P(m.Target) is CrewMember g: text = $"공동 장부 사흘 치 — {g.Name} 설거지 {w.Info.Share(g, LedgerKind.Dishes)}번"; break;
                case MotionKind.Accusation when Thefts.FirstOrDefault(x => x.Id == m.Theft) is Theft th:
                    text = $"창고 수량 기록 — {SimTime.Clock(th.Tick)} 무렵 {th.Room}에서 {th.Item} 하나가 배식 기록 없이 빠졌습니다 · 누가 꺼냈는지는 기록에 없습니다";
                    break;
            }
        if (text == null) return;
        item.Computer = "주 컴퓨터: " + text;
        item.ComputerSign = sign;
        Stats.ComputerLines++;
    }

    // ───────────────────────────── 회의에서 정하기 ─────────────────────────────

    private AgendaItem Resolve(Motion m, List<CrewMember> voters, List<CrewMember> attendees, CrewMember chair, List<SittingLine> lines, out Action? apply)
    {
        var w = _w;
        var item = new AgendaItem { Title = m.Title, Topic = "motion:" + m.Id, Evidence = m.Why };
        m.Item = item;
        m.Stage = MotionStage.Sitting;
        ComputerAdvice(m, item);
        var prop = P(m.Proposer);
        lines.Add(new SittingLine(chair.Id, Persona.Say(chair, prop != null ? $"{Ko.IGa(prop.Name)} 서명 {m.Signers.Count}장을 모아 올린 안건이다 — {m.Title}" : m.Title), true, LineRole.Chair));
        if (m.Kind == MotionKind.Accusation) apply = Trial(m, voters, attendees, chair, item, lines);
        else if (m.Kind == MotionKind.Confidence) apply = Election(m, voters, chair, item, lines);
        else apply = Vote(m, voters, chair, item, lines);
        m.Stage = MotionStage.Decided;
        m.Decided = w.Tick;
        m.Passed = item.Passed;
        m.Outcome = item.Outcome;
        if (item.Passed) Stats.Passed++; else Stats.Failed++;
        return item;
    }

    private Action? Vote(Motion m, List<CrewMember> voters, CrewMember chair, AgendaItem item, List<SittingLine> lines)
    {
        var w = _w;
        var prop = P(m.Proposer);
        if (prop != null && voters.Contains(prop)) lines.Add(new SittingLine(prop.Id, Persona.Say(prop, m.Why), true, LineRole.Speech));
        if (item.Computer != null) lines.Add(new SittingLine(-1, item.Computer, item.ComputerSign > 0, LineRole.Computer));
        var final = new Dictionary<CrewMember, (float s, string why)>();
        // 앙금이 깊으면(골이 있으면) 비밀 투표로 한다
        if (m.Sitting != SittingKind.Regular || Factions.Count(f => !f.Gone) >= 2 && Grudges.Count(g => g.Until > w.Tick) >= 2) m.Secret = true;
        foreach (var c in voters) m.Initial[c.Id] = Opinion(c, m).s;
        var (yes, no) = w.Meetings.Debate(voters, c => Opinion(c, m), c => Expertise(c, m), item, chair, final);
        foreach (var sp in item.Speeches) if (sp.Who != m.Proposer) lines.Add(new SittingLine(sp.Who, sp.Text, sp.For, LineRole.Speech));
        foreach (var kv in final) m.Final[kv.Key.Id] = kv.Value.s;
        bool pass = yes.Count > no.Count || yes.Count == no.Count && final.TryGetValue(chair, out var cs) && cs.s > 0f;
        item.Passed = pass;
        item.Outcome = pass ? Effect(m, true) : "부결";
        w.Meetings.Split(yes, no);
        return () => After(m, voters, chair, yes, no, pass);
    }

    /// <summary>가결된 안건을 실행한다 (방침 · 관행 · 잔치 · 원정 인선 · 공개 경고 · 벌).</summary>
    private string Effect(Motion m, bool pass)
    {
        var w = _w;
        var prop = P(m.Proposer);
        string by = prop != null ? $"{Ko.IGa(prop.Name)} 서명을 모아 올린 안건" : "회의";
        switch (m.Kind)
        {
            case MotionKind.Practice: return "관행으로 정했다";
            case MotionKind.Celebration: return "잔치를 연다";
            case MotionKind.Crew: return "원정대를 바꿨다";
            case MotionKind.Grievance: return "공개 경고";
            case MotionKind.Punishment: return "배급을 이틀 줄인다";
            case MotionKind.Crisis when m.Sitting == SittingKind.Inquiry: return "조사해 기록으로 남긴다";
        }
        if (m.Policy != "") return $"{PolicySystem.Spec(m.Policy).Name}: {PolicySystem.Spec(m.Policy).Options[m.To]}";
        return by;
    }

    private void After(Motion m, List<CrewMember> voters, CrewMember chair, List<CrewMember> yes, List<CrewMember> no, bool pass)
    {
        var w = _w;
        var prop = P(m.Proposer);
        if (pass)
        {
            if (m.Policy != "" && m.To >= 0)
            {
                if (m.Kind == MotionKind.Crisis && m.Policy == "water") _waterRevert = (m.Id, w.Policies["water"]); // 물이 다시 차면 푼다 (급할 때만의 규칙)
                w.Policies.Set(m.Policy, m.To, $"{prop?.Name ?? "누군가"}의 안건 — {m.Why}", yes.Count, no.Count);
                w.Meetings.Record(m.Title, "policy:" + m.Policy, -1, chair, yes, no, MeetingSystem.Hazard(m.Policy));
                if (m.Policy == "rations" && m.To == 3) RationsMotion = m.Id;
            }
            switch (m.Kind)
            {
                case MotionKind.Practice when m.Custom is CustomKind k && prop != null:
                    var cu = w.Culture.Adopt(k, $"회의에서 정했다 — {m.Why}", prop.Name);
                    foreach (var c in yes) { cu.Knowers.Add(c.Id); cu.Followers.Add(c.Id); }
                    break;
                case MotionKind.Celebration:
                    float hour = SimTime.HourOfDay(w.Tick);
                    long day0 = w.Tick - w.Tick % SimTime.TicksPerDay;
                    _feastAt = hour < 18f ? day0 + SimTime.Hours(19f) : w.Tick + SimTime.Hours(1);
                    _feastEnd = _feastAt + SimTime.Hours(1.5f);
                    _feastDone = false;
                    FeastRoom = Now?.Venue ?? PickVenue();
                    w.Log.Add(w.Tick, LogKind.Ship, $"잔치 — {SimTime.Clock(_feastAt)} {FeastRoom?.Name ?? "식당"}");
                    break;
                case MotionKind.Crew when w.Expedition.Pending is { State: "기다림" } p && p.Team.Contains(m.Target) && !p.Team.Contains(m.Other):
                    p.Team[p.Team.IndexOf(m.Target)] = m.Other;
                    break;
                case MotionKind.Grievance when P(m.Target) is CrewMember g:
                    g.Needs.Stress = MathF.Min(1f, g.Needs.Stress + 0.06f);
                    if (prop != null) w.Relations.Remember(g, prop, RelationReason.BlamedMe, "회의에서 나를 앞에 세웠다");
                    foreach (var o in yes) if (o != g) o.ChangeAffinity(g, -0.02f);
                    Life.Diary(w, g, $"회의에서 공개 경고를 받았다 — {m.Why}.");
                    break;
                case MotionKind.Punishment when P(m.Target) is CrewMember pt:
                    _rationCut[pt.Id] = (w.Tick + SimTime.TicksPerDay * 2, 0.7f);
                    MindSystem.Anger(pt, 0.1f);
                    break;
                case MotionKind.Crisis when m.Sitting == SittingKind.Inquiry:
                    foreach (var d in w.Meetings.Decisions.Where(d => w.Tick - d.Tick < SimTime.TicksPerDay * 2 && d.Decider >= 0).TakeLast(1))
                        if (P(d.Decider) is CrewMember dec && prop != null && dec != prop)
                        {
                            w.Relations.Remember(dec, prop, RelationReason.BlamedMe, "사고 조사에서 내 결정을 따졌다");
                            dec.Needs.Stress = MathF.Min(1f, dec.Needs.Stress + 0.05f);
                        }
                    break;
            }
        }
        else if (m.Kind == MotionKind.Grievance && P(m.Target) is CrewMember g2 && prop != null)
        {
            w.Relations.Remember(g2, prop, RelationReason.BlamedMe, "회의에서 나를 탓했다");
            prop.Needs.Stress = MathF.Min(1f, prop.Needs.Stress + 0.04f);
        }
        Aftermath(m, voters, yes, no, pass);
    }

    /// <summary>배급을 줄이기로 한 안건 (이걸 어기면 결정 위반).</summary>
    public int RationsMotion { get; private set; } = -1;
    private bool _feastDone;
    private (int motion, int to) _waterRevert = (-1, 0);

    /// <summary>표결 뒤: 파벌 · 소수파 불만 · 비밀 투표 추측 · 회의록과 일기.</summary>
    private void Aftermath(Motion m, List<CrewMember> voters, List<CrewMember> yes, List<CrewMember> no, bool pass)
    {
        var w = _w;
        var winners = pass ? yes : no;
        var losers = pass ? no : yes;
        FormFactions(m, voters, pass);
        // 크게 반대했는데 진 사람은 며칠 불만을 품는다
        var lead = winners.OrderByDescending(c => MathF.Abs(m.Final.GetValueOrDefault(c.Id)) * w.Meetings.Persuasion(c, Expertise(c, m))).ThenBy(c => c.Id).FirstOrDefault();
        int against = pass ? m.Proposer : lead?.Id ?? -1;
        foreach (var c in losers)
        {
            if (m.Care(c.Id) < 0.22f || c.Id == against) continue;
            AddGrudge(c, m, against, $"'{m.Title}' — {(pass ? "통과됐다" : "떨어졌다")}");
            if (!m.Secret && P(against) is CrewMember ag) w.Relations.Remember(c, ag, RelationReason.VotedAgainstMe, $"'{m.Title}' 때 반대편에 섰다");
        }
        if (m.Secret) Guess(m, voters, losers, winners);
        // 회의록: 연대기에는 정해진 것, 일기에는 저마다 다르게
        var item = m.Item!;
        string tally = m.Kind == MotionKind.Accusation ? $"엄하게 {yes.Count} · 너그럽게 {no.Count}" : $"찬성 {yes.Count} · 반대 {no.Count}";
        w.History.Add(w, HistoryKind.Decision, $"{SittingName(m.Sitting)}: {m.Title} — {item.Outcome} ({tally}{(m.Secret ? " · 비밀 투표" : " · 손 들기")})"
            + (item.FlippedBy != null ? $" · {item.FlippedBy}의 설득으로 뒤집혔다" : ""), Now?.Venue, voters, log: true);
        foreach (var c in voters)
        {
            float s = m.Final.GetValueOrDefault(c.Id);
            bool won = s > 0f == pass;
            string line = c.Id == m.Proposer ? (pass ? $"내가 낸 '{m.Title}'{Ko.IGa(m.Title)[(m.Title).Length..]} 통과됐다. 서명해 준 사람들이 고맙다." : $"'{m.Title}'{Ko.EunNeun(m.Title)[(m.Title).Length..]} 떨어졌다. {(item.FlippedBy != null ? $"{item.FlippedBy}의 말에 다들 넘어갔다." : "아직 때가 아닌가 보다.")}")
                : MathF.Abs(s) < 0.2f ? $"'{m.Title}' — 어느 쪽이든 상관없었다."
                : won ? $"'{m.Title}' — {(pass ? "잘 정해졌다" : "막아서 다행이다")}."
                : c.Value switch
                {
                    CrewValue.Rules => $"'{m.Title}' — 정해졌으니 따르겠다. 하지만 틀렸다.",
                    CrewValue.Freedom => $"'{m.Title}' — 또 저쪽 뜻대로다. 두고 보자.",
                    CrewValue.People => $"'{m.Title}' — 다들 사람 사정은 안 본다.",
                    _ => $"'{m.Title}' — 내 말은 아무도 듣지 않았다.",
                };
            Life.Diary(w, c, line);
        }
    }

    private void AddGrudge(CrewMember c, Motion m, int against, string why)
    {
        var w = _w;
        Grudges.RemoveAll(g => g.Who == c.Id && g.Until <= w.Tick);
        Grudges.Add(new Grudge { Who = c.Id, Motion = m.Id, Against = against, Since = w.Tick, Until = w.Tick + SimTime.Hours(R.Range(48f, 96f)), Why = why });
        if (Grudges.Count > 80) Grudges.RemoveAt(0);
        Stats.Grudges++;
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
        MindSystem.Anger(c, 0.06f);
    }

    /// <summary>비밀 투표 뒤: 진 쪽(고발당한 사람 · 진 후보)이 누가 저쪽에 넣었을지 짐작한다 — 말한 사람은 맞히고, 안 한 사람은 가치관과 사이로 넘겨짚는다.</summary>
    private void Guess(Motion m, List<CrewMember> voters, List<CrewMember> losers, List<CrewMember> winners, CrewMember? subject = null)
    {
        var w = _w;
        var guessers = subject != null ? new List<CrewMember> { subject } : losers.Where(c => m.Care(c.Id) > 0.25f).Take(3).ToList();
        var spoke = m.Item!.Speeches.ToDictionary(s => s.Who, s => s.For);
        foreach (var g in guessers)
        {
            bool gSide = subject != null ? false : m.Final.GetValueOrDefault(g.Id) > 0f;
            foreach (var o in voters.Where(o => o != g).OrderByDescending(o => MathF.Abs(g.AffinityTo(o))).ThenBy(o => o.Id).Take(4))
            {
                bool truth = m.Final.GetValueOrDefault(o.Id) > 0f != gSide; // 실제로 나와 반대쪽에 넣었나
                bool thinks;
                if (spoke.TryGetValue(o.Id, out bool f)) thinks = f != gSide; // 말한 사람은 어느 쪽인지 안다
                else
                {
                    float p = 0.45f + (o.Value != g.Value ? 0.2f : -0.15f) - 0.45f * g.AffinityTo(o) + (FactionOf(o) is Faction fo && !fo.Members.Contains(g.Id) && fo.Members.Any(id => winners.Any(x => x.Id == id)) ? 0.2f : 0f);
                    thinks = R.Chance(Math.Clamp(p, 0.05f, 0.95f));
                }
                m.Guesses.Add((g.Id, o.Id, thinks, truth));
                Stats.Guesses++;
                if (thinks != truth) Stats.WrongGuesses++;
                if (thinks)
                {
                    var mem = w.Relations.Remember(g, o, RelationReason.VotedAgainstMe, $"비밀 투표였지만 '{m.Title}'에 저쪽으로 넣었을 것이다");
                    if (!truth) { mem.Truth = "사실은 같은 쪽에 넣었다"; mem.Revealed = false; }
                }
            }
        }
    }

    // ───────────────────────────── 파벌 ─────────────────────────────

    private static readonly int Hues = 8;

    private void FormFactions(Motion m, List<CrewMember> voters, bool pass)
    {
        var w = _w;
        if (voters.Count < 4) return;
        var sides = new[] { true, false }.Select(pro => (pro, ids: voters.Where(c => (pro ? m.Final.GetValueOrDefault(c.Id) > 0f : m.Final.GetValueOrDefault(c.Id) <= 0f) && m.Care(c.Id) > 0.12f).Select(c => c.Id).OrderBy(i => i).ToList())).ToList();
        var touched = new HashSet<int>();
        // 표가 갈린 안건에서만 새 파벌이 생긴다 (양쪽 다 둘 이상) — 한쪽으로 쏠린 안건은 있던 파벌만 다시 뭉친다
        bool contested = sides.All(x => x.ids.Count >= 2);
        foreach (var (pro, ids) in sides)
        {
            if (ids.Count < 2) continue;
            Faction? best = null;
            float bo = 0f;
            foreach (var f in Factions)
            {
                if (f.Gone || touched.Contains(f.Id)) continue;
                float o = f.Members.Count(ids.Contains) / (float)Math.Min(f.Members.Count, ids.Count);
                if (o > bo) { bo = o; best = f; }
            }
            var leader = ids.Select(P).Where(c => c != null).OrderByDescending(c => w.Meetings.Persuasion(c!, Expertise(c!, m))).ThenBy(c => c!.Id).First()!;
            if (best != null && bo >= 0.6f)
            {
                best.Members = ids; best.Last = w.Tick; best.Motions++; best.Leader = leader.Id;
                if (pro == pass) best.Wins++; else best.Losses++;
                touched.Add(best.Id);
                m.FactionIds.Add(best.Id);
                continue;
            }
            if (!contested) continue;
            int hue = Enumerable.Range(0, Hues).FirstOrDefault(h => !Factions.Any(f => !f.Gone && f.Hue == h), _nextFaction % Hues);
            var nf = new Faction { Id = _nextFaction++, Name = Nick(m, pro, leader), Hue = hue, Members = ids, Leader = leader.Id, Born = w.Tick, Last = w.Tick, Motions = 1 };
            if (pro == pass) nf.Wins++; else nf.Losses++;
            Factions.Add(nf);
            touched.Add(nf.Id);
            m.FactionIds.Add(nf.Id);
            Stats.FactionsBorn++;
            w.Log.Add(w.Tick, LogKind.Life, $"사람들이 {string.Join("·", ids.Select(i => P(i)?.Name))} 쪽을 '{nf.Name}'{(Ko.IGa(nf.Name).EndsWith("이") ? "이라고" : "라고")} 부르기 시작했다", leader.Id);
        }
        // 이번 안건에서 갈라진 파벌은 흩어진다
        foreach (var f in Factions)
        {
            if (f.Gone || touched.Contains(f.Id)) continue;
            int a = f.Members.Count(id => m.Final.GetValueOrDefault(id, 0f) > 0.15f), b = f.Members.Count(id => m.Final.GetValueOrDefault(id, 0f) < -0.15f);
            if (Math.Min(a, b) >= 1 && Math.Min(a, b) * 3 >= a + b) Dissolve(f, $"'{m.Title}'에서 서로 다른 쪽에 섰다");
        }
        if (Factions.Count > 40) Factions.RemoveAll(f => f.Gone && w.Tick - f.Last > SimTime.TicksPerDay * 5);
    }

    private void Dissolve(Faction f, string why)
    {
        var w = _w;
        f.Gone = true;
        f.GoneWhy = why;
        Stats.FactionsGone++;
        w.Log.Add(w.Tick, LogKind.Life, $"'{f.Name}'{Ko.EunNeun(f.Name)[(f.Name).Length..]} 흩어졌다 — {why}", f.Leader);
    }

    private string Nick(Motion m, bool pro, CrewMember leader)
    {
        int opt = pro ? m.To : m.Policy != "" ? _w.Policies[m.Policy] : -1;
        string[] names = (m.Policy, opt) switch
        {
            ("privacy", 1) => new[] { "칸막이파", "문 닫는 쪽" }, ("privacy", _) => new[] { "한솥밥 쪽", "문 열어 두는 쪽" },
            ("nightwatch", 2) => new[] { "푹 자자 쪽", "기계 당번 쪽" }, ("nightwatch", _) => new[] { "불침번파", "밤눈 쪽" },
            ("violations", 1) => new[] { "원칙파", "호루라기 쪽" }, ("violations", 2) => new[] { "너그러운 쪽", "눈감자 쪽" }, ("violations", _) => new[] { "말로 하자 쪽", "경고파" },
            ("leisure", 0) => new[] { "일벌레들", "일 먼저 쪽" }, ("leisure", _) => new[] { "쉬자 쪽", "느긋파" },
            ("drills", 0) => new[] { "훈련 질린 쪽", "그만하자 쪽" }, ("drills", _) => new[] { "훈련파", "비상벨 쪽" },
            ("conflict", 0) => new[] { "중재파", "가운데 쪽" }, ("conflict", 1) => new[] { "선장 말 쪽", "위계파" }, ("conflict", _) => new[] { "알아서 쪽", "각자파" },
            ("memorial", 0) => new[] { "이름 부르는 쪽", "기억파" }, ("memorial", _) => new[] { "조용한 쪽", "묵념파" },
            _ => Array.Empty<string>(),
        };
        if (names.Length == 0) names = (m.Policy, m.Kind) switch
        {
            ("rations", _) when m.To == 3 == pro => new[] { "허리띠파", "아껴 먹자 쪽", "창고지기네" },
            ("rations", _) => new[] { "밥그릇파", "한 숟갈 더 쪽", "제 몫 쪽" },
            ("autoscope", _) when m.To > _w.Policies["autoscope"] == pro => new[] { "기계 믿는 쪽", "단말파" },
            ("autoscope", _) => new[] { "손으로 하자 쪽", "스위치파" },
            (_, MotionKind.Accusation) or (_, MotionKind.Punishment) => pro ? new[] { "엄벌파", "원칙파" } : new[] { "봐주자 쪽", "용서파" },
            (_, MotionKind.Confidence) => pro ? new[] { "갈아 보자 쪽", "새 얼굴 쪽" } : new[] { "선장 편", "그대로 쪽" },
            (_, MotionKind.Celebration) => pro ? new[] { "잔치파", "한잔 쪽" } : new[] { "검소파", "일 먼저 쪽" },
            (_, MotionKind.Crisis) => pro ? new[] { "당장 하자 쪽", "비상파" } : new[] { "두고 보자 쪽", "침착파" },
            (_, MotionKind.Practice) => pro ? new[] { "관행파", "손에 익히자 쪽" } : new[] { "각자 알아서 쪽", "자유파" },
            (_, MotionKind.Grievance) => pro ? new[] { "따지자 쪽", "들고일어난 쪽" } : new[] { "참자 쪽", "좋게 넘기자 쪽" },
            (_, MotionKind.RuleChange) => pro ? new[] { "바꾸자 쪽", "새 규칙파" } : new[] { "그대로 쪽", "옛날 식 쪽" },
            _ => pro ? new[] { "밀자 쪽" } : new[] { "말리자 쪽" },
        };
        string name = R.Chance(0.3f) ? $"{leader.Name}네" : R.Pick(names);
        if (Factions.Any(f => !f.Gone && f.Name == name)) name = $"{leader.Name}네";
        return name;
    }

    // ───────────────────────────── 재판 ─────────────────────────────

    private Action? Trial(Motion m, List<CrewMember> voters, List<CrewMember> attendees, CrewMember chair, AgendaItem item, List<SittingLine> lines)
    {
        var w = _w;
        Stats.Trials++;
        var acc = P(m.Target)!;
        var th = Thefts.FirstOrDefault(t => t.Id == m.Theft);
        var prop = P(m.Proposer);
        string when = th != null ? $"{SimTime.Clock(th.Tick)} {th.Room}" : "그날";
        var testified = new List<CrewMember>();
        // 고발한 사람: 본 사람이면 본 대로, 아니면 들은 대로
        if (prop != null)
        {
            bool saw = th != null && th.Witnesses.Contains(prop.Id);
            lines.Add(new SittingLine(prop.Id, Persona.Say(prop, saw ? $"내가 봤다 — {when}에서 {Ko.EulReul(th!.Item)} 꺼내 먹었다" : $"들은 이야기다 — {m.Why}"), true, saw ? LineRole.Accuser : LineRole.Hearsay));
            if (saw) testified.Add(prop);
        }
        // 증인: 본 사람만 증언한다 (그 자리에 있던 사람)
        if (th != null)
            foreach (int id in th.Witnesses)
            {
                if (id == m.Proposer || P(id) is not CrewMember wit || !attendees.Contains(wit)) continue;
                lines.Add(new SittingLine(wit.Id, Persona.Say(wit, $"나도 봤다 — {when}, 창고 앞에 {Ko.IGa(acc.Name)} 있었다"), true, LineRole.Witness));
                testified.Add(wit);
                Stats.Testimonies++;
            }
        // 못 본 사람은 들은 말뿐이다
        var heard = attendees.FirstOrDefault(c => c != acc && !testified.Contains(c) && m.Signers.Contains(c.Id) && c.Id != m.Proposer);
        if (heard != null) lines.Add(new SittingLine(heard.Id, Persona.Say(heard, "직접 보지는 못했다 — 요즘 창고가 빈다는 말만 들었다"), true, LineRole.Hearsay));
        if (item.Computer != null) lines.Add(new SittingLine(-1, item.Computer, false, LineRole.Computer));
        // 변론: 털어놓는다 · 부인한다 · 사정을 말한다
        bool confess = testified.Count > 0 && (acc.Value is CrewValue.Rules or CrewValue.Safety || acc.Traits.Diligence > 0.65f || acc.Mind.Anger < 0.15f && acc.Value == CrewValue.People);
        bool justify = !confess && (acc.Value is CrewValue.Freedom or CrewValue.People) && testified.Count > 0;
        string defense = confess ? "배가 너무 고팠다 — 잘못했다" : justify ? "일하는 사람 몫이 너무 적다 — 나만 그런 게 아니다" : "나는 아니다 — 잘못 본 거다";
        lines.Add(new SittingLine(acc.Id, Persona.Say(acc, defense), false, LineRole.Defense));
        item.Speeches.Add(new Speech { Who = acc.Id, For = false, Text = Persona.Say(acc, defense) });
        // 처벌 표결: 저마다 무게를 정한다 (설득으로 움직인다) → 가운데 값
        (float, string) Sev(CrewMember c)
        {
            var terms = new List<(float v, string why)>
            {
                (c.Value switch { CrewValue.Rules => 0.9f, CrewValue.Efficiency => 0.4f, CrewValue.Safety => 0.25f, CrewValue.People => -0.6f, _ => -0.4f },
                    c.Value switch { CrewValue.Rules => "규칙은 규칙이다", CrewValue.Efficiency => "다들 줄여 먹는데 혼자 더 먹었다", CrewValue.Safety => "창고가 비면 다 같이 굶는다", CrewValue.People => "배고픈 건 다 같다", _ => "한 끼 갖고 사람을 세우지 말자" }),
                (testified.Count == 0 ? -1.4f : 0.25f * MathF.Min(2, testified.Count), testified.Count == 0 ? "본 사람이 없다" : "본 사람이 있다"),
                (th != null && th.Witnesses.Contains(c.Id) ? 0.5f : 0f, "내 눈으로 봤다"),
                (c.Needs.Hunger > 0.45f ? -0.45f : 0f, "나도 배가 고프다"),
                (-1.3f * c.AffinityTo(acc) - 0.7f * w.Relations.Trust(c, acc), c.AffinityTo(acc) > 0.2f ? $"{Ko.EulReul(acc.Name)} 안다 — 그럴 사람이 아니다" : $"{Ko.EunNeun(acc.Name)} 전에도 그랬다"),
                (confess ? -0.6f : testified.Count > 0 ? 0.45f : 0f, confess ? "털어놓았으니 봐주자" : "끝까지 아니라고 한다"),
                (th?.Breach >= 0 && Get(th.Breach) is Motion bm && bm.Final.GetValueOrDefault(c.Id) > 0f ? 0.4f : 0f, "다 같이 정한 걸 어겼다"),
                (GrudgeOf(c) is Grudge g && g.Against == acc.Id ? 0.35f : 0f, "지난번 일도 있다"),
            };
            if (c == acc) return (-2f, defense);
            float sev = 1.5f + terms.Sum(x => x.v);
            var why = (sev > 1.5f ? terms.Where(x => x.v > 0f).OrderByDescending(x => x.v) : terms.Where(x => x.v < 0f).OrderBy(x => x.v)).Select(x => x.why).FirstOrDefault() ?? "가운데로 하자";
            return ((sev - 1.5f) / 2f, why);
        }
        var final = new Dictionary<CrewMember, (float s, string why)>();
        var (harsh, soft) = w.Meetings.Debate(voters, Sev, c => 0.3f + 0.3f * c.Traits.Diligence + (testified.Contains(c) ? 0.3f : 0f), item, chair, final);
        foreach (var sp in item.Speeches) if (sp.Who != acc.Id) lines.Add(new SittingLine(sp.Who, sp.Text, sp.For, LineRole.Speech));
        var choice = new Dictionary<int, Penalty>();
        foreach (var c in voters)
        {
            float s = final.TryGetValue(c, out var f) ? f.s : 0f;
            m.Final[c.Id] = s;
            choice[c.Id] = (Penalty)Math.Clamp((int)MathF.Round(1.5f + 2f * s), 0, 4);
        }
        var sorted = choice.Values.OrderBy(p => (int)p).ToList();
        var verdict = sorted.Count > 0 ? sorted[(sorted.Count - 1) / 2] : Penalty.Warning;
        m.Verdict = verdict;
        string tally = string.Join(" · ", Enum.GetValues<Penalty>().Select(p => (p, n: choice.Values.Count(x => x == p))).Where(x => x.n > 0).Select(x => $"{PenaltyName(x.p)} {x.n}"));
        item.Passed = verdict != Penalty.Forgive;
        item.Outcome = $"{PenaltyName(verdict)} ({tally})";
        item.Yes = harsh.Count; item.No = soft.Count;
        return () =>
        {
            Punish(acc, verdict, m, testified, choice);
            if (prop != null) w.Relations.Remember(acc, prop, RelationReason.TestifiedAgainstMe, "나를 고발했다");
            Aftermath(m, voters, harsh, soft, verdict >= Penalty.ExtraDuty);
            Guess(m, voters, soft, harsh, acc);
        };
    }

    private void Punish(CrewMember acc, Penalty verdict, Motion m, List<CrewMember> testified, Dictionary<int, Penalty> choice)
    {
        var w = _w;
        foreach (var wit in testified) if (wit != acc) w.Relations.Remember(acc, wit, RelationReason.TestifiedAgainstMe, "재판에서 나를 봤다고 말했다");
        foreach (var (id, p) in choice)
        {
            if (P(id) is not CrewMember c || c == acc) continue;
            if (p == Penalty.Forgive) w.Relations.Remember(acc, c, RelationReason.ForgaveMe, "재판에서 용서하자고 했다");
            else if (!m.Secret && p >= Penalty.RationCut) w.Relations.Remember(acc, c, RelationReason.VotedAgainstMe, "재판에서 무거운 벌에 손을 들었다");
            c.ChangeAffinity(acc, verdict == Penalty.Forgive ? -0.01f : -0.03f);
        }
        string text;
        switch (verdict)
        {
            case Penalty.Forgive:
                acc.Needs.Stress = MathF.Max(0f, acc.Needs.Stress - 0.05f);
                text = "다들 용서해 줬다. 다시는 안 그런다.";
                break;
            case Penalty.Warning:
                acc.Needs.Stress = MathF.Min(1f, acc.Needs.Stress + 0.05f);
                text = "공개 경고를 받았다. 다들 나를 보는 눈이 달라졌다.";
                break;
            case Penalty.ExtraDuty:
                Duty[acc.Id] = (2f, w.Tick + SimTime.Hours(36), m.Id);
                acc.Needs.Stress = MathF.Min(1f, acc.Needs.Stress + 0.06f);
                text = "벌로 식당 청소 · 설거지 두 시간. 창피하다.";
                break;
            case Penalty.RationCut:
                _rationCut[acc.Id] = (w.Tick + SimTime.TicksPerDay * 3, 0.7f);
                acc.Needs.Stress = MathF.Min(1f, acc.Needs.Stress + 0.08f);
                MindSystem.Anger(acc, 0.1f);
                text = "사흘 동안 배급을 덜 받는다. 배고파서 그랬는데 더 배고파졌다.";
                break;
            default:
                _noVote[acc.Id] = w.Tick + SimTime.TicksPerDay * 3;
                acc.Needs.Stress = MathF.Min(1f, acc.Needs.Stress + 0.1f);
                MindSystem.Anger(acc, 0.15f);
                text = "사흘 동안 회의에서 손을 들 수 없다. 이 배에서 내 말은 없다.";
                break;
        }
        Life.Diary(w, acc, text);
        if (verdict >= Penalty.ExtraDuty && P(m.Proposer) is CrewMember pr && acc.Mind.Anger > 0.2f) AddGrudge(acc, m, pr.Id, "나를 재판에 세웠다");
        foreach (var wit in testified) if (wit != acc) Life.Diary(w, wit, $"재판에서 본 대로 말했다. {acc.Name}의 눈을 볼 수 없었다.");
    }

    // ───────────────────────────── 선거 ─────────────────────────────

    private static string RiskWords(int r) => r switch { 0 => "조심해서 가겠다", 2 => "할 수 있을 때 밀어붙이겠다", _ => "무리하지 않되 멈추지도 않겠다" };
    private static string ComputerWords(int a) => a switch { 0 => "컴퓨터에는 경보만 맡기겠다", 1 => "급한 차단까지만 컴퓨터에 맡기겠다", _ => "컴퓨터가 할 수 있는 건 다 맡기겠다" };

    /// <summary>후보의 방침: 위험 감수 · 컴퓨터에 맡길 몫 (가치관 · 컴퓨터에 대한 믿음).</summary>
    public static (int risk, int computer) Platform(CrewMember c)
    {
        int risk = PolicySystem.Preferred(c, "risktaking");
        int comp = c.ComputerFaith >= 0f ? (c.ComputerFaith < 0.35f ? 0 : c.ComputerFaith < 0.7f ? 1 : 2) : PolicySystem.Preferred(c, "autoscope");
        return (risk, comp);
    }

    private Action? Election(Motion m, List<CrewMember> voters, CrewMember chair, AgendaItem item, List<SittingLine> lines)
    {
        var w = _w;
        Stats.Elections++;
        w.Command.NoConfidence++;
        m.Secret = true;
        var cap = w.Command.Captain;
        var prop = P(m.Proposer);
        if (prop != null) lines.Add(new SittingLine(prop.Id, Persona.Say(prop, m.Why), true, LineRole.Speech));
        if (item.Computer != null) lines.Add(new SittingLine(-1, item.Computer, false, LineRole.Computer));
        if (cap != null) lines.Add(new SittingLine(cap.Id, Persona.Say(cap, "끝까지 맡겠다 — 한 번만 더 믿어 달라"), false, LineRole.Defense));
        var final = new Dictionary<CrewMember, (float s, string why)>();
        foreach (var c in voters) m.Initial[c.Id] = Opinion(c, m).s;
        var (yes, no) = w.Meetings.Debate(voters, c => Opinion(c, m), CommandSystem.Leadership, item, chair, final);
        foreach (var sp in item.Speeches) lines.Add(new SittingLine(sp.Who, sp.Text, sp.For, LineRole.Speech));
        foreach (var kv in final) m.Final[kv.Key.Id] = kv.Value.s;
        int need = w.Policies["noconfidence"] == 0 ? voters.Count / 2 + 1 : (int)MathF.Ceiling(voters.Count * 2f / 3f);
        bool pass = yes.Count >= need;
        if (!pass || cap == null)
        {
            item.Passed = false;
            item.Outcome = $"불신임 부결 (찬성 {yes.Count} · 필요 {need})";
            return () =>
            {
                w.Command.Trust = MathF.Min(1f, w.Command.Trust + 0.06f);
                if (cap != null && prop != null) { w.Relations.Remember(cap, prop, RelationReason.BlamedMe, "나를 끌어내리려 했다"); AddGrudge(prop, m, cap.Id, "선장을 못 바꿨다"); }
                Aftermath(m, voters, yes, no, false);
            };
        }
        // 후보 · 연설
        var cands = voters.Where(c => c != cap).OrderByDescending(CommandSystem.Leadership).ThenBy(c => c.Id).Take(2).ToList();
        if (prop != null && prop != cap && !cands.Contains(prop) && CommandSystem.Leadership(prop) > 0.3f && voters.Contains(prop)) cands.Add(prop);
        if (cands.Count == 0) { item.Passed = false; item.Outcome = "나설 사람이 없다"; return null; }
        foreach (var cd in cands)
        {
            var (r, a) = Platform(cd);
            lines.Add(new SittingLine(cd.Id, Persona.Say(cd, $"{RiskWords(r)} · {ComputerWords(a)}"), true, LineRole.Candidate));
        }
        var tally = cands.ToDictionary(c => c.Id, _ => 0);
        var ballot = new Dictionary<int, int>();
        foreach (var v in voters)
        {
            var (vr, va) = Platform(v);
            var pick = cands.OrderByDescending(cd =>
            {
                var (r, a) = Platform(cd);
                return 0.8f * CommandSystem.Leadership(cd) + (cd == v ? 0.3f : v.AffinityTo(cd) + 0.5f * w.Relations.Trust(v, cd))
                       + (r == vr ? 0.2f : 0f) + (a == va ? 0.15f : 0f) + (FactionOf(v) is Faction f && f.Members.Contains(cd.Id) ? 0.25f : 0f)
                       - (GrudgeOf(v) is Grudge g && g.Against == cd.Id ? 0.3f : 0f);
            }).ThenBy(cd => cd.Id).First();
            tally[pick.Id]++;
            ballot[v.Id] = pick.Id;
        }
        var winner = cands.OrderByDescending(c => tally[c.Id]).ThenByDescending(CommandSystem.Leadership).First();
        m.Winner = winner.Id;
        var (wr, wa) = Platform(winner);
        item.Passed = true;
        item.Outcome = $"새 선장 {winner.Name} ({string.Join(" · ", cands.Select(c => $"{c.Name} {tally[c.Id]}표"))})";
        return () =>
        {
            w.Command.Install(winner, $"선거 {tally[winner.Id]}표");
            string why = $"새 선장 {winner.Name}의 방침";
            if (w.Policies["risktaking"] != wr) w.Policies.Set("risktaking", wr, why);
            if (w.Policies["autoscope"] != wa) w.Policies.Set("autoscope", wa, why);
            if (winner.ComputerFaith >= 0f) w.Command.ComputerTrust = 0.5f * w.Command.ComputerTrust + 0.5f * winner.ComputerFaith;
            // 후보마다 지지자가 파벌이 되고, 진 후보 쪽은 불만을 품는다
            foreach (var cd in cands)
            {
                var sup = ballot.Where(kv => kv.Value == cd.Id).Select(kv => kv.Key).OrderBy(i => i).ToList();
                if (sup.Count >= 2 && !Factions.Any(f => !f.Gone && f.Members.SequenceEqual(sup)))
                {
                    var nf = new Faction { Id = _nextFaction++, Name = $"{cd.Name} 편", Hue = Enumerable.Range(0, Hues).FirstOrDefault(h => !Factions.Any(f => !f.Gone && f.Hue == h), 0), Members = sup, Leader = cd.Id, Born = w.Tick, Last = w.Tick, Motions = 1, Wins = cd == winner ? 1 : 0, Losses = cd == winner ? 0 : 1 };
                    Factions.Add(nf);
                    Stats.FactionsBorn++;
                }
                if (cd == winner) continue;
                foreach (int id in sup) if (P(id) is CrewMember s && s != winner) AddGrudge(s, m, winner.Id, $"{Ko.IGa(cd.Name)} 선거에서 졌다");
            }
            if (cap != null)
            {
                AddGrudge(cap, m, prop?.Id ?? winner.Id, "선장 자리에서 끌려 내려왔다");
                cap.Needs.Stress = MathF.Min(1f, cap.Needs.Stress + 0.12f);
                Life.Diary(w, cap, "불신임으로 선장 자리를 내려놓았다. 누가 나를 찍었을까.");
            }
            Life.Diary(w, winner, $"선장이 됐다. {RiskWords(wr)}. {ComputerWords(wa)}.");
            Aftermath(m, voters, yes, no, true);
            if (cap != null)
            {
                // 내려온 선장이 누가 불신임에 넣었을지 짐작한다
                m.Final[cap.Id] = -1f;
                Guess(m, voters, no, yes, cap);
            }
        };
    }
}
