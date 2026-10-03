using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.17 로맨스 아크: 호감 → 고백(대화 카드) → 연인(드러내거나 몰래 — v18.14 '몰래 만나기'로 이어진다) → 위기(질투 · 다툼 · 지침 · 슬픈 일)
//   → 고비 카드 → 이별 또는 결혼(v18.14 '결혼식'으로 이어진다). 거절 · 이별은 '끝난 사랑' 이야기를 연다.
// 주변 반응: 가까운 사람은 축하하고(메신저 · 기쁨), 먼 사람은 수군거리고(잡담 · 소문), 함장이 끼었거나 같은 조면 회의 안건이 된다.
// 주 컴퓨터: 연인이 된 둘의 근무가 엇갈리면 근무표를 맞춰 줄지 묻는다 (사생활 방침을 지킨다).

public enum LoveStage : byte { Crush, Lovers, Crisis, Married, Broken, Rejected }

public sealed class Love
{
    public int Id { get; init; }
    /// <summary>먼저 마음이 간 사람.</summary>
    public int A { get; init; }
    public int B { get; init; }
    public LoveStage Stage { get; set; }
    public long Born { get; init; }
    public long Since { get; set; }
    public bool Secret { get; set; }
    public bool Public { get; set; }
    public int SchemeId { get; set; } = -1;
    public int Rival { get; set; } = -1;
    public string CrisisWhy { get; set; } = "";
    public int Crises { get; set; }
    public int Fails { get; set; }
    public long NextCheck { get; set; }
    public List<(long t, string text)> Beats { get; } = new();
    public bool Has(int id) => A == id || B == id;
    public int Other(int id) => A == id ? B : A;
    public bool Together => Stage is LoveStage.Lovers or LoveStage.Crisis or LoveStage.Married;
    public bool Over => Stage is LoveStage.Broken or LoveStage.Rejected;
}

public sealed partial class StorySystem
{
    public List<Love> Loves { get; } = new();
    private int _nextLove = 1;
    private long _nextCrush;

    public static string LoveName(LoveStage s) => s switch
    {
        LoveStage.Crush => "마음이 간다", LoveStage.Lovers => "연인", LoveStage.Crisis => "고비", LoveStage.Married => "부부", LoveStage.Broken => "헤어졌다", _ => "거절당했다",
    };

    public Love? LoveOf(CrewMember c) { foreach (var l in Loves) if (l.Has(c.Id) && !l.Over) return l; return null; }

    private void LoveBeat(Love l, string text, bool history = false)
    {
        var w = _w;
        l.Beats.Add((w.Tick, text));
        w.Log.Add(w.Tick, LogKind.Life, text, l.A);
        if (history) w.History.Add(w, HistoryKind.Bond, text, P(l.A)?.Room, new[] { P(l.A)!, P(l.B)! });
    }

    /// <summary>호감을 연다 (시험에서 바로 부를 수도 있다).</summary>
    public Love Crush(CrewMember a, CrewMember b)
    {
        var w = _w;
        var l = new Love { Id = _nextLove++, A = a.Id, B = b.Id, Stage = LoveStage.Crush, Born = w.Tick, Since = w.Tick };
        Loves.Add(l);
        Stats.Crushes++;
        LoveBeat(l, $"{Ko.IGa(a.Name)} 자꾸 {b.Name} 쪽을 본다");
        Life.Diary(w, a, Persona.Say(a, $"{b.Name} 생각이 자꾸 난다. 이상하다."));
        w.Brain2.Goals.Push(a, "love:near", $"{b.Name} 곁에", $"{b.Name} 곁에 있고 싶다", ActCat.Social, 48f, 0.8f);
        w.Brain2.Emotions.Feel(a, Feeling.Joy, 0.15f, $"{b.Name} 생각", b);
        return l;
    }

    private void LoveTick()
    {
        var w = _w;
        bool crisis = Crisis.Acting(w);
        float pace = Math.Max(0.1f, Pace);
        // 새 호감 (두 시간마다)
        if (!crisis && !NoSeeds && w.Tick >= _nextCrush)
        {
            _nextCrush = w.Tick + SimTime.Hours(2);
            int adults = w.Crew.Count(Adult), crushes = Loves.Count(l => l.Stage == LoveStage.Crush);
            if (crushes < Math.Max(2, adults / 5))
                foreach (var a in w.Crew)
                {
                    if (!Adult(a) || a.Partner is int || LoveOf(a) != null) continue;
                    CrewMember? best = null; float bv = 0.4f;
                    foreach (var b in w.Crew)
                    {
                        if (b == a || !Adult(b) || b.Partner is int || LoveOf(b) != null || b.AffinityTo(a) < 0.25f) continue;
                        if (Loves.Any(x => x.Has(a.Id) && x.Has(b.Id) && w.Tick - x.Since < SimTime.TicksPerDay * 5)) continue;
                        float v = a.AffinityTo(b);
                        if (v > bv) { bv = v; best = b; }
                    }
                    if (best != null) { Crush(a, best); break; }
                }
        }
        for (int i = 0; i < Loves.Count; i++)
        {
            var l = Loves[i];
            if (l.Over || l.Stage == LoveStage.Married && l.Crises > 3) continue;
            var a = P(l.A); var b = P(l.B);
            if (a == null || b == null || a.Dead || b.Dead)
            {
                if (l.Together && (a?.Dead == false || b?.Dead == false)) { var live = a?.Dead == false ? a : b; Life.Diary(w, live!, Persona.Say(live!, "그 사람이 없는 배는 너무 넓다.")); }
                l.Stage = LoveStage.Broken; continue;
            }
            // 같은 방에 있으면 마음이 조금씩 깊어진다
            if (a.Room != null && a.Room == b.Room && a.IsAwake && b.IsAwake)
            {
                a.ChangeAffinity(b, l.Together ? 0.004f : 0.006f);
                if (l.Stage != LoveStage.Crush || b.AffinityTo(a) > 0.35f) b.ChangeAffinity(a, l.Together ? 0.004f : 0.003f);
            }
            if (crisis || w.Tick < l.NextCheck) continue;
            l.NextCheck = w.Tick + SimTime.Hours(1);
            switch (l.Stage)
            {
                case LoveStage.Crush:
                    if (w.Tick - l.Since >= SimTime.Hours(16f / pace) && a.AffinityTo(b) >= 0.5f && Talking(a.Id) == null) Intend(a, b, CardKind.Confess, l.Id);
                    break;
                case LoveStage.Lovers:
                    CrisisCheck(l, a, b, pace);
                    if (l.SchemeId >= 0 && !l.Public && w.Schemes.Get(l.SchemeId) is Scheme sc && sc.FoundAt >= 0) GoPublic(l, a, b, $"{(P(sc.Finder)?.Name ?? "누군가")}에게 들켰다");
                    if (l.Crises >= 1 && w.Tick - l.Since >= SimTime.Hours(30f / pace) && MathF.Min(a.AffinityTo(b), b.AffinityTo(a)) >= 0.6f && l.SchemeId != -2) Propose(l, a, b);
                    break;
                case LoveStage.Crisis:
                    if (Talking(a.Id) == null && Talking(b.Id) == null && w.Tick - l.Since >= SimTime.Hours(2f / pace))
                    {
                        // 먼저 말을 꺼내는 쪽: 덜 화난 쪽 · 참을성 있는 쪽
                        var (s, o) = a.Mind.Anger + (a.Habits.Contains(Habit.Patient) ? -0.2f : 0f) <= b.Mind.Anger + (b.Habits.Contains(Habit.Patient) ? -0.2f : 0f) ? (a, b) : (b, a);
                        Intend(s, o, CardKind.Mend, l.Id);
                    }
                    break;
                case LoveStage.Married when a.Partner != b.Id:
                    a.Partner = b.Id; b.Partner = a.Id;
                    break;
            }
            // 결혼식이 열렸다 (v18.14 '결혼식'이 짝을 맺는다)
            if (l.Stage != LoveStage.Married && a.Partner == b.Id && b.Partner == a.Id)
            {
                l.Stage = LoveStage.Married; l.Since = w.Tick;
                Stats.Weddings++;
                LoveBeat(l, $"{Ko.WaGwa(a.Name)} {b.Name} — 부부가 됐다", true);
            }
        }
    }

    /// <summary>시험: 지금 바로 고비가 오는지 살핀다.</summary>
    public void CheckLove(Love l) { if (P(l.A) is CrewMember a && P(l.B) is CrewMember b) CrisisCheck(l, a, b, Math.Max(0.1f, Pace)); }

    private void CrisisCheck(Love l, CrewMember a, CrewMember b, float pace)
    {
        var w = _w;
        if (w.Tick - l.Since < SimTime.Hours(20f / pace)) return;
        string? why = null; int rival = -1;
        // 질투: 연인이 다른 사람과 가까운데, 같은 방에서 그걸 본다
        foreach (var (x, y) in new[] { (a, b), (b, a) })
        {
            foreach (var c in w.Crew)
            {
                if (c == a || c == b || !Adult(c) || c.Room == null || c.Room != x.Room || y.Room != x.Room) continue;
                if (c.AffinityTo(x) >= 0.4f && x.AffinityTo(c) >= 0.35f)
                {
                    rival = c.Id;
                    w.Brain2.Emotions.Feel(y, Feeling.Anger, 0.25f, $"{Ko.WaGwa(x.Name)} {c.Name}", c);
                    y.ChangeAffinity(c, -0.15f);
                    y.Say(w, Persona.Say(y, $"…{Rang(c.Name)} 뭐가 그렇게 재밌어?"));
                    Stats.Jealousy++;
                    why = $"{Ko.IGa(y.Name)} {Ko.WaGwa(x.Name)} {c.Name} 사이를 질투한다";
                    break;
                }
            }
            if (why != null) break;
        }
        if (why == null && a.Quarrel > l.Since && a.Quarrel == b.Quarrel) why = "사소한 일로 크게 다퉜다";
        if (why == null && (a.Needs.Stress > 0.75f || b.Needs.Stress > 0.75f)) why = "둘 다 지쳐서 서로에게 날이 섰다";
        if (why == null && (ArcsOf(a).Any(x => x.End == ArcEnd.Tragic && x.EndedAt > l.Since) || ArcsOf(b).Any(x => x.End == ArcEnd.Tragic && x.EndedAt > l.Since))) why = "슬픈 일 이후로 말이 줄었다";
        if (why == null && w.Tick - l.Since > SimTime.Hours(50f / pace) && l.Crises == 0 && R.Chance(0.12f)) why = "서운한 일이 말없이 쌓였다";
        if (why == null) return;
        l.Stage = LoveStage.Crisis; l.Since = w.Tick; l.CrisisWhy = why; l.Rival = rival; l.Crises++;
        Stats.Crises++;
        LoveBeat(l, $"{Ko.WaGwa(a.Name)} {b.Name} — {why}");
        Life.Diary(w, a, Persona.Say(a, $"{Ko.WaGwa(b.Name)} 요즘 어긋난다. {why}."));
        Life.Diary(w, b, Persona.Say(b, $"{Ko.WaGwa(a.Name)} 요즘 어긋난다."));
    }

    /// <summary>둘이 드러낸다 (또는 들킨다): 축하 · 수군거림 · 회의 안건.</summary>
    private void GoPublic(Love l, CrewMember a, CrewMember b, string how)
    {
        var w = _w;
        l.Public = true;
        LoveBeat(l, $"{Ko.WaGwa(a.Name)} {Ko.IGa(b.Name)} 사귄다는 걸 다들 알게 됐다 — {how}", true);
        foreach (var o in w.Crew)
        {
            if (o == a || o == b || !Adult(o) || !o.CanAct) continue;
            float f = MathF.Max(o.AffinityTo(a), o.AffinityTo(b));
            if (f >= 0.3f)
            {
                Stats.Cheers++;
                w.Brain2.Emotions.Feel(o, Feeling.Joy, 0.12f, $"{Ko.WaGwa(a.Name)} {b.Name}", a);
                if (Stats.Cheers % 3 == 1) w.Info.Chat.Post(o, ChatKind.Talk, ShipChat.Voice(o, $"{a.Name} {b.Name} 축하해!! 언제부터였어?", $"{a.Name} 님, {b.Name} 님 축하드려요"));
            }
            else if (f < 0.1f || o.Habits.Contains(Habit.Talker)) Stats.Whispers++;
        }
        // 함장이 끼었거나 같은 일을 하면 규칙을 앞세우는 사람이 안건으로 올린다
        bool cap = a.Id == w.Command.CaptainId || b.Id == w.Command.CaptainId;
        if ((cap || a.Role == b.Role) && !MotionSystem.Off)
        {
            var critic = w.Crew.Where(o => o != a && o != b && Adult(o) && o.CanAct && (w.Values.Peek(o)?.V[(int)Axis.Rule] ?? 0f) > 0.2f)
                .OrderByDescending(o => w.Values.Peek(o)?.V[(int)Axis.Rule] ?? 0f).ThenBy(o => o.Id).FirstOrDefault();
            if (critic != null)
            {
                w.Motions.Propose(critic, MotionKind.RuleChange, SittingKind.Regular, cap ? "함장의 연애와 공정함" : $"연인끼리 같은 근무 조", cap ? "함장이 한 사람 편을 들 수 있다" : "둘이 같은 조면 일이 사사로이 흐른다");
                Stats.Agendas++;
            }
        }
        // 주 컴퓨터: 근무가 엇갈리면 맞춰 줄지 조용히 묻는다
        if (MathF.Abs(SimTime.Wrap(a.Schedule.WorkStart - b.Schedule.WorkStart)) > 4f && MathF.Abs(SimTime.Wrap(a.Schedule.WorkStart - b.Schedule.WorkStart)) < 20f)
        {
            var ai = w.Automation;
            w.Log.Add(w.Tick, LogKind.Ship, $"{ai.Voice.Call}: {ai.Manner.Speak($"{a.Name} 님, {b.Name} 님과 근무가 엇갈립니다 — 원하시면 쉬는 시간을 맞춰 드릴게요")}", a.Id);
            Stats.ComputerNotes++;
        }
    }

    private void Propose(Love l, CrewMember a, CrewMember b)
    {
        var w = _w;
        l.SchemeId = -2; // 청혼은 한 번
        if (SchemeTable.Get("wedding") is not SchemeSpec wed || SchemeSystem.Off) { a.Partner = b.Id; b.Partner = a.Id; return; }
        var s = w.Schemes.Start(wed, a, b.Id, b);
        LoveBeat(l, $"{Ko.IGa(a.Name)} {b.Name}에게 청혼했다 — 배 안에서 식을 올리기로 했다", true);
        Life.Diary(w, b, Persona.Say(b, $"{Ko.IGa(a.Name)} 청혼했다. 대답은 정해져 있었다."));
        _ = s;
    }

    // ───────────────────────────── 카드 결과 ─────────────────────────────

    private void OnConfess(TalkCard k, CrewMember s, CrewMember l, TalkOption o)
    {
        var w = _w;
        var lv = Loves.FirstOrDefault(x => x.Id == k.Ref) ?? Crush(s, l);
        Stats.Confessed++;
        var em = w.Brain2.Emotions;
        if (k.Success)
        {
            lv.Stage = LoveStage.Lovers; lv.Since = w.Tick;
            Stats.Couples++;
            s.ChangeAffinity(l, 0.12f); l.ChangeAffinity(s, 0.15f);
            em.Feel(s, Feeling.Joy, 0.45f, $"{l.Name}의 대답", l); em.Feel(l, Feeling.Joy, 0.35f, $"{s.Name}의 고백", s);
            k.Answer = o.Key == "joke" ? "…웃기긴. 나도 그 생각 했어" : "나도. 언제 말하나 기다렸어";
            k.Outcome = "마음이 닿았다 — 연인이 됐다";
            LoveBeat(lv, $"{Ko.WaGwa(s.Name)} {l.Name} — 연인이 됐다");
            // 몰래: 둘 다 말수가 적거나 함장이 끼었으면 당분간 숨긴다 (v18.14 '몰래 만나기')
            bool cap = s.Id == w.Command.CaptainId || l.Id == w.Command.CaptainId;
            if ((s.Traits.Sociability + l.Traits.Sociability < 0.95f || cap) && SchemeTable.Get("secret_romance") is SchemeSpec sr && !SchemeSystem.Off)
            {
                lv.Secret = true;
                var sc = w.Schemes.Start(sr, s, l.Id, l);
                lv.SchemeId = sc.Id;
            }
            else GoPublic(lv, s, l, "둘이 손을 잡고 식당에 들어왔다");
        }
        else
        {
            lv.Stage = LoveStage.Rejected; lv.Since = w.Tick;
            s.ChangeAffinity(l, -0.08f);
            em.Feel(s, Feeling.Sadness, 0.4f, $"{l.Name}의 대답", l); em.Feel(s, Feeling.Shame, 0.2f, "고백", l);
            k.Answer = l.Partner is int || Loves.Any(x => x.Has(l.Id) && x.Together) ? "미안… 난 이미 마음 둔 사람이 있어" : "고마워. 근데 난 그냥 좋은 동료로 지내고 싶어";
            k.Outcome = "마음이 닿지 않았다";
            LoveBeat(lv, $"{Ko.IGa(s.Name)} {l.Name}에게 마음을 전했지만 닿지 않았다");
            if (ArcOf(s) == null && StoryTable.Get("heartbreak") is ArcSpec hb) { Start(hb, s, null, $"{l.Name}에게 고백했다 거절당한 일"); k.Branch = "끝난 사랑 — 그 사람을 피해 다니기 시작했다"; }
            else k.Branch = "친구로 남기로 했다 — 어색함은 남았다";
        }
    }

    private void OnMend(TalkCard k, CrewMember s, CrewMember l, TalkOption o)
    {
        var w = _w;
        var lv = Loves.FirstOrDefault(x => x.Id == k.Ref);
        if (lv == null) { k.Outcome = "할 말을 잃었다"; return; }
        var em = w.Brain2.Emotions;
        bool end = o.Key == "end" && k.Success;
        if (k.Success && !end)
        {
            lv.Stage = LoveStage.Lovers; lv.Since = w.Tick; lv.Rival = -1;
            s.ChangeAffinity(l, 0.15f); l.ChangeAffinity(s, 0.15f);
            em.Feel(s, Feeling.Joy, 0.25f, "다시 가까워졌다", l); em.Feel(l, Feeling.Joy, 0.25f, "다시 가까워졌다", s);
            k.Answer = "나도 미안해. 우리 잘해 보자";
            k.Outcome = "고비를 넘겼다";
            LoveBeat(lv, $"{Ko.WaGwa(s.Name)} {l.Name} — 고비를 넘겼다");
            return;
        }
        lv.Fails++;
        if (end || lv.Fails >= 2 || MathF.Min(s.AffinityTo(l), l.AffinityTo(s)) < 0.25f)
        {
            lv.Stage = LoveStage.Broken; lv.Since = w.Tick;
            Stats.Breakups++;
            if (s.Partner == l.Id) { s.Partner = null; l.Partner = null; }
            s.ChangeAffinity(l, -0.15f); l.ChangeAffinity(s, -0.2f);
            em.Feel(s, Feeling.Sadness, 0.45f, "이별", l); em.Feel(l, Feeling.Sadness, 0.5f, "이별", s);
            w.Relations.Remember(l, s, RelationReason.AbandonedMe, "헤어지자고 했다");
            k.Answer = end ? "…그래. 알았어" : "그만하자. 지친다";
            k.Outcome = "헤어졌다";
            k.Branch = "둘은 다른 식탁에 앉기 시작했다";
            LoveBeat(lv, $"{Ko.WaGwa(s.Name)} {l.Name} — 헤어졌다", true);
            foreach (var x in new[] { s, l })
                if (ArcOf(x) == null && StoryTable.Get("heartbreak") is ArcSpec hb) Start(hb, x, null, $"{Ko.WaGwa((x == s ? l : s).Name)} 헤어진 일");
            // 친구들은 편을 든다
            foreach (var o2 in w.Crew)
                if (o2 != s && o2 != l && Adult(o2) && MathF.Abs(o2.AffinityTo(s) - o2.AffinityTo(l)) > 0.3f) { var cold = o2.AffinityTo(s) > o2.AffinityTo(l) ? l : s; o2.ChangeAffinity(cold, -0.04f); Stats.Whispers++; }
        }
        else
        {
            lv.Since = w.Tick;
            k.Answer = "지금은 아무 말도 듣고 싶지 않아";
            k.Outcome = "말이 엇나갔다";
            k.Branch = "하루 더 냉랭하게 지낸다 — 다음엔 끝일지도";
        }
    }
}
