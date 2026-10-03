using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.7 사고 조사 회의: 큰 사고(사망 · 계통 이상 · 실수에서 번진 사고)가 가라앉으면 안건이 저절로 올라온다 (v18.18 회의).
//  자리에서: 의장이 연다 → 주 컴퓨터가 블랙박스를 읽어 낸다(표는 없다 — 단말 기록 · 빈 구간과 그 시각 단말 방에 있던 사람 · 경보) →
//   본 사람의 증언(그 자리 · 치우는 걸 · 단말 앞) → 흔적(이름 새긴 렌치 · 반대로 돈 밸브 · 깨끗해진 사고 자리) →
//   당사자(이미 털어놓음 · 몰리면 털어놓음 · 남 탓 · 부인) · 탓을 들은 사람의 변론(위치 기록이 알리바이) → 결론 → 벌 표결(용서 · 경고 · 근무 · 배급 · 손 들기) → 재발 방지 규칙.
//  블랙박스가 타 버렸으면 본 것과 말뿐이다 — 믿을 만한 사람의 남 탓이 그대로 결론이 되기도 한다 (그때 세계의 진실과 결론이 갈린다).
//  남는 것: 사고 카드 "원인" 칸 · 연대기 · 일기(사람마다 다르게) · 관계 기억(숨겼다 · 뒤집어씌웠다 · 털어놓았다) · 말의 신용 · 규칙.

public sealed partial class InquirySystem
{
    /// <summary>한 시간마다: 가라앉은 큰 사고에 조사 안건을 올린다.</summary>
    private void Cases_()
    {
        var w = _w;
        if (MotionSystem.Off) return;
        var incs = w.Causes.Incidents;
        for (int i = incs.Count - 1; i >= 0; i--)
        {
            var inc = incs[i];
            if (w.Tick - inc.Start > SimTime.TicksPerDay * 3) break;
            if (_seenRoots.Contains(inc.Root)) continue;
            if (inc.Open && w.Tick - inc.LastActivity < SimTime.Hours(3)) continue;
            _seenRoots.Add(inc.Root);
            var slip = Slips.FirstOrDefault(s => s.Bit && s.Node >= 0 && w.Causes.IncidentOf(s.Node) == inc);
            var k = w.Scale.Cases.FirstOrDefault(x => x.Root == inc.Root);
            bool big = inc.Deaths > 0 || k != null && k.Peak >= IncidentScale.System
                       || slip != null && (slip.Fire || inc.Casualties > 0 || k == null || k.Peak >= IncidentScale.Room);
            if (!big || slip == null && w.Tick < _nextCase) continue;
            OpenCase(inc, slip);
        }
    }

    /// <summary>조사 안건을 올린다 (그 사고 자리에 있던 사람 · 안전을 중히 여기는 사람이 내고, 다들 바로 서명한다).</summary>
    public InquiryCase OpenCase(CauseIncident inc, Slip? slip)
    {
        var w = _w;
        var root = w.Causes.Node(inc.Root);
        var cs = new InquiryCase { Id = _caseId++, Root = inc.Root, Title = ChronicleBook.Short(root.Text, 34), Start = inc.Start, Opened = w.Tick, Slip = slip?.Id ?? -1 };
        foreach (var id in inc.Nodes) { int r = w.Causes.Node(id).RoomId; if (r >= 0 && !cs.Rooms.Contains(r)) cs.Rooms.Add(r); }
        if (slip != null && !cs.Rooms.Contains(slip.RoomId)) cs.Rooms.Add(slip.RoomId);
        Cases.Add(cs);
        Stats.Cases++;
        _seenRoots.Add(inc.Root);
        _nextCase = w.Tick + SimTime.Hours(36);
        if (MotionSystem.Off) return cs;
        var open = w.Motions.All.FirstOrDefault(m => m.Sitting == SittingKind.Inquiry && m.Stage is MotionStage.Signing or MotionStage.Ready && m.Born >= inc.Start);
        if (open != null && !Cases.Any(k => k.Motion == open.Id)) { cs.Motion = open.Id; return cs; }
        int culprit = slip?.Who ?? -1;
        var adults = w.Crew.Where(c => Adult(c) && c.CanAct && c.Id != culprit).ToList();
        if (adults.Count < 2) return cs;
        float Want(CrewMember c) => (c.Value is CrewValue.Safety or CrewValue.Rules ? 1f : 0f) + (c.Id == w.Command.CaptainId ? 0.6f : 0f)
                                    + (c.Room != null && cs.Rooms.Contains(c.Room.Id) ? 0.3f : 0f) + c.Vitals.Injury;
        var order = adults.OrderByDescending(Want).ThenBy(c => c.Id).ToList();
        var prop = order[0];
        var mo = w.Motions.Propose(prop, MotionKind.Crisis, SittingKind.Inquiry, $"{cs.Title} — 무엇이 잘못됐나", "블랙박스와 각자 본 것을 맞춰 보자. 다시는 이러면 안 된다");
        cs.Motion = mo.Id;
        foreach (var c in order.Skip(1)) { if (mo.Stage != MotionStage.Signing) break; w.Motions.Cosign(mo, c.Id); }
        return cs;
    }

    private float Cred(CrewMember c) => _w.Info.Cred(c);
    private string Name(int id) => P(id)?.Name ?? "누군가";

    /// <summary>조사 자리 (MotionsResolve.Resolve 훅): 블랙박스 · 증언 · 흔적을 맞춰 결론을 내고 벌을 표결한다. 이 배의 조사 안건이 아니면 null.</summary>
    public Action? Hear(Motion m, List<CrewMember> voters, List<CrewMember> attendees, CrewMember chair, AgendaItem item, List<SittingLine> lines)
    {
        var w = _w;
        if (Off || m.Sitting != SittingKind.Inquiry) return null;
        var cs = Cases.FirstOrDefault(k => k.Motion == m.Id);
        if (cs == null)
        {
            cs = Cases.LastOrDefault(k => k.Motion < 0 && !k.Heard && m.Born >= k.Start);
            if (cs == null) return null;
            cs.Motion = m.Id;
        }
        cs.Heard = true;
        Stats.Hearings++;
        var bx = w.Blackbox;
        var f = new Finding { Case = cs.Id, Root = cs.Root, Tick = w.Tick };
        var s = Get(cs.Slip);
        var culprit = s != null ? P(s.Who) : null;
        var sp = s != null ? Spec(s.Kind) : null;
        var ev = new SortedDictionary<int, float>();
        void Add(int id, float v) { if (id >= 0) ev[id] = ev.GetValueOrDefault(id) + v; }
        var testified = new List<CrewMember>();
        lines.Add(new SittingLine(chair.Id, Persona.Say(chair, $"블랙박스와 각자 본 것을 맞춰 보자 — {cs.Title}"), true, LineRole.Chair));

        // ① 블랙박스 (주 컴퓨터가 읽는다 · 컴퓨터가 멎었으면 손재주 있는 사람이 상자를 열어 읽는다) — 표는 없다
        long from = (s?.Tick ?? cs.Start) - SimTime.Hours(1), to = (s?.BitAt ?? cs.Start) + SimTime.Minutes(10);
        bool online = w.Automation.Present && w.Automation.CoreOnline;
        bool read = bx.Present && !bx.Wrecked && bx.FreshSince <= from;
        f.BoxRead = read;
        string comp;
        if (read)
        {
            var rec = bx.Read(from, to, e => cs.Rooms.Contains(e.Room) && e.Kind is BoxKind.Work or BoxKind.Silence or BoxKind.Valve or BoxKind.Alarm) ?? new();
            var gaps = bx.GapsIn(from, to).Where(g => g.Room < 0 || cs.Rooms.Contains(g.Room)).ToList();
            int hi = s != null ? rec.FindLastIndex(e => e.Ref == s.Machine && e.Who >= 0 && e.Kind is BoxKind.Work or BoxKind.Silence or BoxKind.Valve) : -1;
            int ai = rec.FindIndex(e => e.Kind == BoxKind.Alarm && e.Tick >= (s?.BitAt ?? cs.Start) - SimTime.Minutes(2));
            string alarm = ai >= 0 ? $" · {bx.Line(rec[ai])}" : "";
            if (hi >= 0)
            {
                comp = $"블랙박스 — {bx.Line(rec[hi])}{alarm}";
                Add(rec[hi].Who, 1f);
                f.Basis.Add("블랙박스 단말 기록");
            }
            else if (gaps.Count > 0)
            {
                var g = gaps[0];
                f.GapSeen = true;
                var near = bx.WhoWasIn(g.Terminal, g.At).Where(id => P(id) is CrewMember pc && (g.Access == "" || bx.AccessOf(pc) == g.Access)).ToList();
                comp = $"블랙박스 — {bx.GapLine(g)}" + (near.Count > 0 ? $" · 그 시각 {bx.RoomName(g.Terminal)} 위치 기록: {string.Join(" · ", near.Select(Name))}" : " · 그 시각 단말 방 위치 기록도 없습니다");
                foreach (var id in near) Add(id, 0.95f / near.Count);
                f.Basis.Add("지운 자리 (빈 구간 · 지운 시각 · 권한)");
            }
            else comp = s != null ? $"블랙박스 — 그 앞뒤로 {s.MachineName}에 사람 손이 닿은 기록이 없습니다{alarm}" : $"블랙박스 — 앞선 한 시간 사이 사람 손이 닿은 기록이 없습니다{alarm}";
        }
        else comp = !bx.Present || bx.Wrecked ? "블랙박스 기록 칩이 타서 읽을 수 없습니다 — 남은 건 여러분이 본 것뿐입니다" : "그 시각 기록은 칩을 갈기 전이라 남아 있지 않습니다 — 남은 건 여러분이 본 것뿐입니다";
        var reader = online ? null : attendees.OrderByDescending(c => c.SkillLevel(Skill.Electrical)).ThenBy(c => c.Id).First();
        string compLine = online ? "주 컴퓨터: " + w.Automation.Manner.Speak(comp) : Persona.Say(reader!, "상자를 직접 열어 읽었다 — " + comp);
        item.Computer = compLine;
        item.ComputerSign = 0;
        lines.Add(new SittingLine(reader?.Id ?? -1, compLine, false, online ? LineRole.Computer : LineRole.Witness));

        // ② 본 사람 · ③ 흔적 · ④ 당사자
        bool late = false, blamed = false;
        if (s != null && sp != null && culprit != null)
        {
            string when = SimTime.Clock(s.Tick);
            foreach (int id in s.Saw.Take(3))
            {
                if (P(id) is not CrewMember wit || !attendees.Contains(wit) || wit == culprit) continue;
                lines.Add(new SittingLine(wit.Id, Persona.Say(wit, $"봤다 — {when}에 {Ko.IGa(culprit.Name)} {Ko.EulReul(s.MachineName)} 손보고 있었다"), true, LineRole.Witness));
                Add(culprit.Id, 0.45f * Cred(wit));
                testified.Add(wit);
                if (!f.Basis.Contains("본 사람")) f.Basis.Add("본 사람");
            }
            if (P(s.TidySeenBy) is CrewMember t2 && attendees.Contains(t2) && t2 != culprit)
            {
                lines.Add(new SittingLine(t2.Id, Persona.Say(t2, $"사고 뒤에 {Ko.IGa(culprit.Name)} 혼자 {s.MachineName} 안을 뒤지고 있었다"), true, LineRole.Witness));
                Add(culprit.Id, 0.4f * Cred(t2));
                testified.Add(t2);
                f.Basis.Add("치우는 걸 본 사람");
            }
            if (P(s.WipeSeenBy) is CrewMember t3 && attendees.Contains(t3) && t3 != culprit)
            {
                lines.Add(new SittingLine(t3.Id, Persona.Say(t3, $"단말 앞에 {Ko.IGa(culprit.Name)} 오래 서 있었다 — 내가 보자 화면을 껐다"), true, LineRole.Witness));
                Add(culprit.Id, 0.5f * Cred(t3));
                testified.Add(t3);
                f.Basis.Add("단말 앞을 본 사람");
            }
            var finder = attendees.Where(c => c != culprit).OrderByDescending(c => c.SkillLevel(Skill.Mechanics) + c.SkillLevel(Skill.Engineering)).ThenBy(c => c.Id).FirstOrDefault();
            if (finder != null && sp.Trace != "")
            {
                if (s.TraceLeft)
                {
                    lines.Add(new SittingLine(finder.Id, Persona.Say(finder, "사고 자리에서 나왔다 — " + string.Format(sp.Trace, s.MachineName, culprit.Name)), true, LineRole.Witness));
                    if (s.Kind == SlipKind.ToolLeft) Add(culprit.Id, 0.8f);
                    f.Basis.Add("흔적");
                }
                else lines.Add(new SittingLine(finder.Id, Persona.Say(finder, "사고 자리가 이상하게 깨끗했다 — 누가 먼저 다녀간 것 같다"), true, LineRole.Witness));
            }
            float e0 = ev.GetValueOrDefault(culprit.Id);
            if (s.Confessed)
                lines.Add(new SittingLine(culprit.Id, Persona.Say(culprit, $"내가 그랬다 — {sp.What}. 이미 말했다"), false, LineRole.Defense));
            else if (attendees.Contains(culprit) && !culprit.Dead)
            {
                bool cornered = e0 >= 0.8f;
                if (cornered && (culprit.Value is CrewValue.Rules or CrewValue.Safety || culprit.Traits.Diligence > 0.6f || s.Guilt > 0.7f))
                {
                    late = true;
                    lines.Add(new SittingLine(culprit.Id, Persona.Say(culprit, $"맞다 — 내가 그랬다. {sp.What}. 말을 못 했다"), false, LineRole.Defense));
                }
                else if (P(s.Scapegoat) is CrewMember goat && !goat.Dead)
                {
                    blamed = true;
                    lines.Add(new SittingLine(culprit.Id, Persona.Say(culprit, $"마지막으로 만진 건 {goat.Name}다 — 나는 아니다"), false, LineRole.Accuser));
                    Add(goat.Id, 0.5f + 0.6f * Cred(culprit));
                    bool touched = read && (bx.Read(s.Tick - SimTime.Hours(36), s.Tick, e => e.Kind == BoxKind.Work && e.Ref == s.Machine && e.Who == goat.Id)?.Count ?? 0) > 0;
                    if (touched) Add(goat.Id, 0.3f);
                    bool alibi = read && !bx.WhoWasIn(s.RoomId, s.Tick).Contains(goat.Id);
                    if (attendees.Contains(goat))
                        lines.Add(new SittingLine(goat.Id, Persona.Say(goat, alibi ? $"{when}에 나는 그 방에 없었다 — 위치 기록을 봐라" : "나는 아니다 — 그날 그 설비에 손대지 않았다"), false, LineRole.Defense));
                    Add(goat.Id, alibi ? -1.2f : -0.3f * Cred(goat));
                    if (alibi) f.Basis.Add("위치 기록");
                }
                else lines.Add(new SittingLine(culprit.Id, Persona.Say(culprit, e0 > 0.3f ? "나는 제대로 했다 — 왜 나를 보나" : "나는 모르는 일이다"), false, LineRole.Defense));
            }
        }

        // ⑤ 결론
        int top = ev.Where(kv => kv.Value >= 0.6f).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).DefaultIfEmpty(-1).First();
        if (s != null && (s.Confessed || late)) top = s.Who;
        f.Blamed = top;
        bool wiped = s?.Wipe >= 0;
        string how = string.Join(" · ", f.Basis.Take(2));
        if (s == null || sp == null)
        {
            f.Text = $"조사 결과 — 사람 손 탓이 아니었다 ({cs.Title})";
            f.Right = true;
        }
        else if (top == s.Who)
        {
            f.Right = true;
            f.Hid = s.Hidden && !(s.Confessed && s.ConfessedAt < cs.Opened);
            f.Framed = blamed || s.Scapegoat >= 0 && s.Ways.Contains(CoverWay.Blame) && f.Hid;
            f.Text = $"조사 결과 — {culprit?.Name ?? Name(s.Who)}의 실수: {sp.What}"
                     + (s.Confessed && s.ConfessedAt < cs.Opened ? (s.Hidden ? " · 한동안 숨겼다가 스스로 털어놓았다" : " · 바로 털어놓았다")
                        : late ? " · 조사 자리에서 털어놓았다"
                        : f.Framed ? $" · {Name(s.Scapegoat)}에게 돌렸다가 {how}{(how.EndsWith("록") ? "으로" : "로")} 드러났다"
                        : wiped ? " · 기록까지 지웠지만 지운 자리로 드러났다"
                        : $" · 숨겼다가 {how}{(how.EndsWith("록") ? "으로" : "로")} 드러났다");
        }
        else if (top >= 0)
        {
            f.Right = false;
            f.Text = $"조사 결과 — {Name(top)}의 실수로 결론: {sp.What}";
        }
        else f.Text = $"조사 결과 — {s.MachineName} 일이 누구 손에서 비롯됐는지 가리지 못했다";
        if (s != null && sp != null && (top >= 0 || s.TraceLeft || read) && !Ruled(s.Kind)) f.Rule = sp.Rule;

        // ⑥ 벌 표결 (결론 난 사람이 있을 때) — 저마다 무게를 정한다 → 가운데 값
        Dictionary<int, Penalty>? choice = null;
        var acc = P(f.Blamed);
        if (s != null && acc != null && !acc.Dead && voters.Count >= 2)
        {
            bool early = s.Confessed && s.ConfessedAt < cs.Opened && f.Right;
            var inc = w.Causes.IncidentOf(s.Node);
            (float, string) Sev(CrewMember c)
            {
                if (c == acc) return (-2f, "그 정도면 됐다");
                var terms = new List<(float v, string why)>
                {
                    (c.Value switch { CrewValue.Rules => 0.5f, CrewValue.Safety => 0.3f, CrewValue.Efficiency => 0.2f, CrewValue.People => -0.5f, _ => -0.3f },
                        c.Value switch { CrewValue.Rules => "규칙은 규칙이다", CrewValue.Safety => "다칠 뻔했다 — 다시는 안 된다", CrewValue.Efficiency => "실수 하나로 설비가 멈췄다", CrewValue.People => "사람은 실수한다", _ => "벌보다 고치는 게 먼저다" }),
                    (early ? -1f : late ? -0.4f : f.Hid && f.Right ? 0.35f : 0f, early ? "먼저 털어놓았다" : late ? "끝내 털어놓았다" : "숨겼다"),
                    (f.Framed && f.Right ? 0.9f : 0f, "남에게 뒤집어씌우려 했다"),
                    (wiped && f.Right ? 0.5f : 0f, "기록까지 지웠다"),
                    (inc != null ? 0.25f * MathF.Min(3, inc.Casualties) + (inc.Deaths > 0 ? 0.6f : 0f) : 0f, "사람이 다쳤다"),
                    (-1.2f * c.AffinityTo(acc) - 0.6f * w.Relations.Trust(c, acc), c.AffinityTo(acc) > 0.2f ? $"{Ko.EulReul(acc.Name)} 안다 — 일부러 그럴 사람이 아니다" : $"{Ko.EunNeun(acc.Name)} 전에도 그랬다"),
                };
                float sev = terms.Sum(x => x.v);
                var why = (sev > 0f ? terms.Where(x => x.v > 0f).OrderByDescending(x => x.v) : terms.Where(x => x.v < 0f).OrderBy(x => x.v)).Select(x => x.why).FirstOrDefault() ?? "가운데로 하자";
                return (sev / 2f, why);
            }
            var final = new Dictionary<CrewMember, (float s, string why)>();
            var (harsh, soft) = w.Meetings.Debate(voters, Sev, c => 0.3f + 0.3f * c.Traits.Diligence + (testified.Contains(c) ? 0.3f : 0f), item, chair, final);
            foreach (var spc in item.Speeches) if (spc.Who != acc.Id) lines.Add(new SittingLine(spc.Who, spc.Text, spc.For, LineRole.Speech));
            choice = new Dictionary<int, Penalty>();
            foreach (var c in voters)
            {
                float v = final.TryGetValue(c, out var fv) ? fv.s : 0f;
                m.Final[c.Id] = v;
                choice[c.Id] = (Penalty)Math.Clamp((int)MathF.Round(1.5f + 2f * v), 0, 4);
            }
            var sorted = choice.Values.OrderBy(p => (int)p).ToList();
            f.Verdict = sorted.Count > 0 ? sorted[(sorted.Count - 1) / 2] : Penalty.Warning;
            m.Verdict = f.Verdict.Value;
            item.Yes = harsh.Count; item.No = soft.Count;
        }
        if (f.Rule != "") lines.Add(new SittingLine(chair.Id, Persona.Say(chair, $"앞으로는 {f.Rule}"), true, LineRole.Chair));
        item.Passed = true;
        item.Outcome = ChronicleBook.Short(f.Text.Replace("조사 결과 — ", ""), 60) + (f.Verdict is Penalty pv ? $" · {MotionSystem.PenaltyName(pv)}" : "");
        cs.Finding = f;
        var ch = choice;
        bool lt = late;
        var att = attendees.ToList();
        return () => Settle(cs, f, s, m, chair, att, testified, ch, lt);
    }

    /// <summary>결론을 실행한다: 관계 · 신용 · 벌 · 규칙 · 연대기 · 일기.</summary>
    private void Settle(InquiryCase cs, Finding f, Slip? s, Motion m, CrewMember chair, List<CrewMember> attendees, List<CrewMember> testified, Dictionary<int, Penalty>? choice, bool late)
    {
        var w = _w;
        var culprit = s != null ? P(s.Who) : null;
        var sp = s != null ? Spec(s.Kind) : null;
        var acc = P(f.Blamed);
        if (s != null && sp != null && culprit != null)
        {
            bool early = s.Confessed && s.ConfessedAt < cs.Opened;
            if (f.Right && f.Blamed == s.Who)
            {
                if (late) { s.Confessed = true; s.ConfessedAt = w.Tick; Stats.LateConfessed++; }
                else if (!s.Confessed) { s.Revealed = true; Stats.Revealed++; }
                bool wiped = s.Wipe >= 0;
                foreach (var o in w.Crew)
                {
                    if (o == culprit || !Adult(o)) continue;
                    float d = early ? -0.005f : late ? -0.03f : -0.07f;
                    if (f.Framed) d -= 0.06f;
                    if (wiped && !early) d -= 0.03f;
                    d *= o.Value == CrewValue.Rules ? 1.3f : o.Value == CrewValue.People ? 0.7f : 1f;
                    o.ChangeAffinity(culprit, d);
                    if (!early && attendees.Contains(o))
                        w.Relations.Remember(o, culprit, RelationReason.LiedToUs, f.Framed ? "제 실수를 남에게 뒤집어씌우려 했다" : wiped ? "실수를 숨기고 기록까지 지웠다" : late ? "사고 조사 자리에서야 털어놓았다" : "사고 조사 전까지 제 실수를 숨겼다");
                }
                w.Info.CredAdd(culprit, early ? 0.02f : f.Framed ? -0.3f : late ? -0.06f : -0.15f);
                if (f.Framed && P(s.Scapegoat) is CrewMember goat && !goat.Dead)
                {
                    w.Relations.Remember(goat, culprit, RelationReason.FramedMe, "자기 실수를 나에게 뒤집어씌웠다");
                    goat.ChangeAffinity(culprit, -0.3f);
                    goat.Needs.Stress = MathF.Max(0f, goat.Needs.Stress - 0.05f);
                    MindSystem.Anger(goat, 0.2f);
                    Life.Diary(w, goat, $"조사에서 다 밝혀졌다. {Ko.IGa(culprit.Name)} 내 이름을 댔었다니. 얼굴을 못 보겠다.");
                }
                s.Settled = w.Tick;
                Life.Diary(w, culprit, early ? "조사에서 내가 말한 그대로 적혔다. 다들 고개만 끄덕였다."
                    : late ? "조사 자리에서 결국 말했다. 다들 나를 보는 눈이 달라졌다."
                    : f.Framed ? $"다 드러났다. {Name(s.Scapegoat)} 얼굴을 볼 수가 없다." : wiped ? "지운 자리가 오히려 나를 가리켰다." : "블랙박스에 내 단말 기록이 남아 있었다. 숨길 수 없었다.");
            }
            else if (acc != null && f.Blamed != s.Who)
            {
                // 틀린 결론: 엉뚱한 사람이 탓을 쓴다 — 진짜 당사자는 더 무거워진다
                Stats.Wrong++;
                acc.Needs.Stress = MathF.Min(1f, acc.Needs.Stress + 0.1f);
                MindSystem.Anger(acc, 0.2f);
                foreach (var o in w.Crew) if (o != acc && Adult(o) && o != culprit) o.ChangeAffinity(acc, -0.04f);
                if (s.Scapegoat == acc.Id) w.Relations.Remember(acc, culprit, RelationReason.BlamedMe, "조사에서 내 탓이 됐다 — 처음 내 이름을 댄 사람");
                w.Info.CredAdd(acc, -0.08f);
                s.Guilt += 0.5f;
                Life.Diary(w, acc, $"{s.MachineName} 일이 내 탓으로 적혔다. 나는 그 설비에 손대지 않았다. 아무도 믿어 주지 않는다.");
                Life.Diary(w, culprit, $"{acc.Name}에게 탓이 돌아갔다. 말해야 하는데 입이 안 떨어진다.");
            }
            else
            {
                Stats.Unresolved++;
                if (s.Hidden && !s.Confessed) s.Guilt += 0.2f;
                Life.Diary(w, culprit, "조사가 흐지부지 끝났다. 아무도 모른다. 나만 안다.");
            }
            foreach (var wit in testified) Life.Diary(w, wit, $"사고 조사에서 본 대로 말했다. {Ko.IGa(culprit.Name)} 나를 보지 않았다.");
        }
        // 벌 (용서도 표결로)
        if (acc != null && !acc.Dead && f.Verdict is Penalty verdict)
        {
            if (verdict == Penalty.Forgive) Stats.Forgiven++; else Stats.Punished++;
            if (verdict >= Penalty.ExtraDuty) w.Motions.Sentence(acc, verdict, m.Id);
            acc.Needs.Stress = MathF.Min(1f, acc.Needs.Stress + 0.03f * (int)verdict);
            if (choice != null)
                foreach (var (id, p) in choice)
                    if (P(id) is CrewMember c && c != acc && p == Penalty.Forgive) w.Relations.Remember(acc, c, RelationReason.ForgaveMe, "사고 조사에서 용서하자고 했다");
            Life.Diary(w, acc, verdict switch
            {
                Penalty.Forgive => "다들 용서해 줬다. 다음엔 두 번 세 번 확인한다.",
                Penalty.Warning => "공개 경고를 받았다.",
                Penalty.ExtraDuty => "벌로 식당 청소 · 설거지 두 시간.",
                Penalty.RationCut => "사흘 동안 배급을 덜 받는다.",
                _ => "사흘 동안 회의에서 손을 들 수 없다.",
            });
        }
        // 재발 방지 규칙
        if (s != null && f.Rule != "" && !Rules.ContainsKey(s.Kind))
        {
            Rules[s.Kind] = w.Tick;
            Stats.Rules++;
            w.History.Add(w, HistoryKind.Lesson, $"재발 방지 — {f.Rule} ({cs.Title} 조사에서 정했다)", s.RoomId >= 0 ? w.Ship.Rooms[s.RoomId] : null, attendees, log: true);
        }
        var crew = new List<CrewMember> { chair };
        if (acc != null) crew.Add(acc);
        w.History.Add(w, HistoryKind.Decision, $"사고 조사 — {cs.Title}: {f.Text.Replace("조사 결과 — ", "")}" + (f.Verdict is Penalty v2 ? $" · {MotionSystem.PenaltyName(v2)}" : ""),
            s != null && s.RoomId >= 0 ? w.Ship.Rooms[s.RoomId] : null, crew, log: true);
        Life.Diary(w, chair, f.BoxRead ? $"블랙박스를 열어 맞춰 봤다. {f.Text.Replace("조사 결과 — ", "")}." : $"블랙박스가 없으니 말뿐이었다. {f.Text.Replace("조사 결과 — ", "")}. 이게 맞는지 모르겠다.");
        if (culprit != null && f.Right && f.Hid && attendees.FirstOrDefault(o => o != chair && o != culprit && !testified.Contains(o) && o.AffinityTo(culprit) > 0.1f) is CrewMember friend)
            Life.Diary(w, friend, $"{Ko.IGa(culprit.Name)} 숨겼다니. 믿고 일을 맡겼는데.");
    }

    /// <summary>사고 카드 "원인" 칸 (UiIncident 훅): 조사 결과 · 털어놓은 실수.</summary>
    public string? FindingFor(int root)
    {
        foreach (var k in Cases) if (k.Root == root && k.Finding != null) return k.Finding.Text;
        foreach (var s in Slips)
            if (s.Confessed && s.Node >= 0 && _w.Causes.IncidentOf(s.Node) is CauseIncident inc && inc.Root == root)
                return $"털어놓았다 — {Name(s.Who)}의 실수: {Spec(s.Kind).What}";
        return null;
    }
}
