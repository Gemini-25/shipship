using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.14 꾸민 일의 흐름: 무르익기 · 들킴(눈 · 냄새 · 소리 · 불 · 입소문) · 본 사람의 반응 · 회의 · 재판 · 관행 · 흔적.
public sealed partial class SchemeSystem
{
    // ───────────────────────────── 관심 · 찬반 ─────────────────────────────

    /// <summary>이 일에 마음이 가나 (끼고 싶나 · 0~1) — 같은 일이 벌써 있는지는 보지 않는다.</summary>
    public float Interest(CrewMember c, SchemeSpec s)
    {
        float sum = 0f; int n = 0;
        foreach (var d in Drives) if ((s.Drives & d) != 0) { sum += DriveOf(c, d); n++; }
        float v = n > 0 ? sum / n : 0f;
        if (s.Hobbies.Length > 0 && s.Hobbies.Any(c.Hobbies.Contains)) v += 0.3f;
        if (s.Cat is SchemeCat.Rule or SchemeCat.Secret or SchemeCat.Trade) v *= c.Value switch { CrewValue.Rules => 0.3f, CrewValue.Safety => 0.55f, CrewValue.Freedom => 1.3f, _ => 1f };
        if (s.Cat == SchemeCat.Social) v *= 0.55f + c.Traits.Sociability;
        if (s.Cat == SchemeCat.Prank && c.Habits.Contains(Habit.Serious)) v *= 0.1f;
        return Math.Clamp(v, 0f, 1f);
    }

    /// <summary>이 일을 좋게 보나 (+ 눈감아 줌 · 찬성 / − 알린다 · 반대) 와 그 사람의 말.</summary>
    public (float v, string why) Approve(CrewMember f, Scheme s)
    {
        var w = _w;
        var spec = s.Spec;
        var lead = P(s.Lead);
        var t = new List<(float v, string why)>(10);
        switch (spec.Cat)
        {
            case SchemeCat.Prank: t.Add((0.05f + 0.25f * DriveOf(f, Drive.Jest), "장난 좀 친 걸 가지고")); break;
            case SchemeCat.Social: t.Add((0.25f, "같이 놀자는 건데")); break;
            case SchemeCat.Rule: t.Add((-0.25f, "정해 둔 걸 어겼다")); break;
            case SchemeCat.Trade: t.Add((-0.1f, "판이 커지면 꼭 탈이 난다")); break;
            case SchemeCat.Politics: t.Add((DriveOf(f, Drive.Grudge) - 0.4f, DriveOf(f, Drive.Grudge) > 0.4f ? "할 말은 해야 한다" : "배를 흔드는 일이다")); break;
            default: t.Add((0.05f, "누구 해치는 일도 아니다")); break;
        }
        bool sly = spec.Cat is SchemeCat.Rule or SchemeCat.Secret or SchemeCat.Trade;
        switch (f.Value)
        {
            case CrewValue.Rules when sly: t.Add((-0.3f, "정해 둔 대로 해야 한다")); break;
            case CrewValue.Safety: t.Add((-(0.08f + spec.Risk), spec.Risk > 0.15f ? spec.Place == Place.Engine ? "기관실에 저런 걸 두면 불 난다" : "저러다 누가 다친다" : "괜히 일 만들지 말자")); break;
            case CrewValue.Freedom: t.Add((0.25f, "숨 쉴 구멍은 있어야 한다")); break;
            case CrewValue.People when spec.Cat is SchemeCat.Social or SchemeCat.Personal or SchemeCat.Secret: t.Add((0.12f, "사람 사는 데 이 정도는")); break;
            case CrewValue.Efficiency: t.Add((-0.12f, "일이 밀린다")); break;
        }
        t.Add((0.35f * (f.Needs.Stress - 0.3f), f.Needs.Stress > 0.4f ? "다들 지쳤다 — 이런 거라도 있어야" : ""));
        t.Add((0.35f * Interest(f, spec), "나도 끼고 싶다"));
        if (lead != null && lead != f)
        {
            float aff = 0.4f * f.AffinityTo(lead) + 0.2f * w.Relations.Trust(f, lead);
            t.Add((aff, aff > 0f ? $"{lead.Name}라면 믿는다" : $"{Ko.IGa(lead.Name)} 하는 일이라"));
        }
        if (LedgerItem(spec) is ItemKind k && ItemKinds.IsFood(k)) t.Add((-0.45f * (f.Needs.Hunger + (w.Food.Rationing ? 0.4f : 0f)) - 0.1f, "다들 줄여 먹는데"));
        if (f.Id == w.Command.CaptainId) t.Add((-0.12f, "배를 맡은 사람으로서 그냥 넘길 수 없다"));
        if (Rule(spec.Key) is { Allowed: false }) t.Add((f.Value == CrewValue.Rules ? -0.4f : -0.2f, "회의에서 금지한 일이다"));
        if (s.Knows.TryGetValue(f.Id, out var how) && how == KnowHow.Part) t.Add((0.6f, "나도 같이 했다"));
        if (spec.Key is "escape_pod" or "mutiny_plot") t.Add((-0.5f, spec.Key == "escape_pod" ? "배를 버리는 일이다" : "배를 뒤엎는 일이다"));
        if (spec.Key == "secret_garden" || spec.Key == "mural" || spec.Key == "book_cache") t.Add((0.15f, "보기만 해도 숨이 트인다"));
        float sum = 0f;
        foreach (var x in t) sum += x.v;
        string why = (sum > 0f ? t.Where(x => x.v > 0f && x.why != "").OrderByDescending(x => x.v) : t.Where(x => x.v < 0f && x.why != "").OrderBy(x => x.v))
            .Select(x => x.why).FirstOrDefault() ?? (sum > 0f ? "그럴 수도 있지" : "글쎄다");
        return (sum, why);
    }

    private Stance React(Scheme s, CrewMember f)
    {
        var spec = s.Spec;
        var lead = P(s.Lead);
        if (spec.Key == "escape_pod")
            return f.Value == CrewValue.People || f.Traits.Sociability > 0.65f || lead != null && f.AffinityTo(lead) > 0.25f ? Stance.Soothe : Stance.Report;
        if (spec.Cat == SchemeCat.Social || spec.Fate is Fate.Club or Fate.Event)
            return f.Id != s.Target && Interest(f, spec) > 0.3f && s.Crew.Count < 8 ? Stance.Join : Stance.Shrug;
        var (a, _) = Approve(f, s);
        if (a > 0.35f && spec.Team != Crewing.Solo && Interest(f, spec) > 0.35f && s.Crew.Count < 5) return Stance.Join;
        if (a > -0.12f) return Stance.Cover;
        return Stance.Report;
    }

    private static string ReactName(Stance r) => r switch
    {
        Stance.Cover => "못 본 척했다", Stance.Join => "같이 하기로 했다", Stance.Report => "알렸다", Stance.Soothe => "붙잡고 다독였다",
        Stance.Laugh => "웃어넘겼다", Stance.Grudge => "얼굴이 굳었다", _ => "그냥 지나갔다",
    };

    // ───────────────────────────── 들킴 ─────────────────────────────

    /// <summary>{f}가 이 일을 알아챘다 ({how}). 반응에 따라 눈감아 주거나 · 끼거나 · 알린다.</summary>
    public void Discover(Scheme s, CrewMember f, string how)
    {
        var w = _w;
        if (!s.Active || s.KnowsWho(f.Id) || s.Crew.Contains(f.Id)) return;
        var spec = s.Spec;
        var lead = P(s.Lead);
        if (lead == null) return;
        // 깜짝 잔치는 주인공이 눈치채면 김이 샌다
        if (spec.Key == "surprise_birthday" && f.Id == s.Target) { s.Knows[f.Id] = KnowHow.Saw; s.FoundHow = "눈치챘다"; Life.Diary(w, f, "다들 뭔가 숨기는 눈치다. 모른 척해 주기로 했다."); return; }
        s.Knows[f.Id] = KnowHow.Saw;
        Stats.Found++;
        if (s.Finder < 0) { s.Finder = f.Id; s.FoundHow = how; s.FoundAt = w.Tick; }
        var r = React(s, f);
        s.Reactions.Add((f.Id, r));
        string where = RoomOf(s)?.Name ?? "배 안";
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(f.Name)} {how} — {where}의 {spec.Name} · {ReactName(r)}", f.Id);
        Life.Diary(w, f, $"{where}에서 {Ko.EulReul(spec.Name)} 알게 됐다 ({how}). {ReactName(r)}.");
        if (f.Room == lead.Room && f.IsAwake) f.Say(w, Persona.Say(f, r switch { Stance.Report => "이건 말해야겠다", Stance.Join => "나도 끼워 줘", Stance.Soothe => "잠깐, 얘기 좀 하자", _ => "…못 본 걸로 할게" }));
        switch (r)
        {
            case Stance.Join:
                Stats.Joined++;
                s.Crew.Add(f.Id);
                s.Knows[f.Id] = KnowHow.Part;
                w.Relations.Remember(lead, f, RelationReason.KeptMySecret, $"{spec.Name} — 들키고 나서 같이 하자고 했다");
                w.Brain2.Emotions.Feel(f, Feeling.Joy, 0.12f, $"{spec.Name}에 끼었다", lead);
                break;
            case Stance.Cover:
                Stats.Covered++;
                w.Relations.Remember(lead, f, RelationReason.KeptMySecret, $"{spec.Name} — 못 본 척해 줬다");
                if (spec.Fate == Fate.Adopt && Approve(f, s).v > 0.2f) Resolve(s, f, true);
                break;
            case Stance.Soothe:
                Stats.Soothed++;
                w.Relations.Remember(lead, f, RelationReason.Comforted, "떠나려던 나를 붙잡고 이야기를 들어 줬다");
                lead.Needs.Stress = MathF.Max(0f, lead.Needs.Stress - 0.2f);
                w.Brain2.Emotions.Feel(lead, Feeling.Sadness, -0.1f, "누가 붙잡아 줬다", f);
                Mark(s, TraceState.Removed, "탈출 포드 짐을 도로 내렸다");
                End(s, SchemeStage.Done, $"{Ko.IGa(f.Name)} 붙잡아 마음을 돌렸다");
                Life.Diary(w, lead, $"{Ko.IGa(f.Name)} 나를 붙잡았다. 짐을 도로 내렸다.");
                break;
            case Stance.Report:
                Stats.Reported++;
                w.Relations.Remember(lead, f, RelationReason.ToldOnMe, $"{spec.Name} — 나를 일러바쳤다");
                Resolve(s, f, false);
                break;
        }
    }

    /// <summary>들킨 뒤: 회의 · 재판 · 공개 경고 · 함장이 정한다 · 모두의 것이 된다.</summary>
    private void Resolve(Scheme s, CrewMember by, bool liked)
    {
        var w = _w;
        if (!s.Active || s.Stage == SchemeStage.Vote) return;
        var spec = s.Spec;
        var lead = P(s.Lead);
        if (lead == null || lead.Dead) { End(s, SchemeStage.Dropped, "꾸민 사람이 없다"); return; }
        switch (spec.Fate)
        {
            case Fate.Vote: ProposeVote(s, lead, by); break;
            case Fate.Trial: Accuse(s, lead, by, $"{RoomOf(s)?.Name ?? "배 안"}에서 {Ko.EulReul(spec.Name)} 봤다"); break;
            case Fate.Grievance or Fate.Debt or Fate.Motion: Grieve(s, lead, by); break;
            case Fate.Adopt: if (liked) Adopt(s, by); else CaptainDecides(s, by, true); break;
            case Fate.Keep: CaptainDecides(s, by, false); break;
            case Fate.Laugh when w.Command.Captain is CrewMember cap: FirePrank(s, cap); break;
        }
    }

    private void CaptainDecides(Scheme s, CrewMember by, bool adopt)
    {
        var w = _w;
        var lead = P(s.Lead)!;
        var cap = w.Command.Captain;
        if (cap == null) { if (adopt) Adopt(s, by); else Tolerate(s, by, "함장이 없어 그대로 뒀다"); return; }
        if (!s.KnowsWho(cap.Id)) s.Knows[cap.Id] = KnowHow.Told;
        var (a, why) = Approve(cap, s);
        if (s.Crew.Contains(cap.Id)) a += 1f;
        if (by != cap) w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(by.Name)} {cap.Name}에게 알렸다 — {s.Spec.Name}", by.Id);
        if (a > -0.15f)
        {
            if (cap.Room == lead.Room) cap.Say(w, Persona.Say(cap, adopt ? "다 같이 보게 두자" : "이번엔 못 본 걸로 하지"));
            if (adopt) Adopt(s, cap); else Tolerate(s, cap, why);
        }
        else Remove(s, cap, why);
    }

    // ───────────────────────────── 결과 ─────────────────────────────

    private string LegitName(SchemeSpec s) => s.Legit != "" ? s.Legit : s.Name;

    private void Adopt(Scheme s, CrewMember by)
    {
        var w = _w;
        var lead = P(s.Lead)!;
        string legit = LegitName(s.Spec);
        Stats.Adopted++;
        Mark(s, TraceState.Public, legit);
        End(s, SchemeStage.Done, $"모두의 것이 됐다 — {legit}");
        w.Brain2.Emotions.Feel(lead, Feeling.Pride, 0.3f, $"{legit} — 다들 좋아한다");
        w.History.Add(w, HistoryKind.Memory, $"{Ko.IGa(lead.Name)} 몰래 하던 {Ko.IGa(s.Spec.Name)} {Ko.EuRo(legit)} 모두의 것이 됐다", RoomOf(s), new[] { lead, by }, log: true);
        var msg = w.Info.Chat.Post(by, ChatKind.Notice, ShipChat.Voice(by, $"{RoomOf(s)?.Name} 가 봐. {lead.Name}가 만든 {legit} — 다 같이 쓰자", $"{RoomOf(s)?.Name}에 {lead.Name} 님이 만든 {legit}, 다 같이 쓰면 좋겠습니다"));
        s.Invite = msg.Id;
        Life.Diary(w, lead, $"들켰는데 다들 좋아했다. 이제 {legit}이다.");
    }

    private void Tolerate(Scheme s, CrewMember by, string why)
    {
        var w = _w;
        var lead = P(s.Lead)!;
        Stats.Kept++;
        Mark(s, TraceState.Kept, s.Spec.Name);
        End(s, SchemeStage.Done, $"그대로 두기로 했다 ({why})");
        if (by != lead) w.Relations.Remember(lead, by, RelationReason.KeptMySecret, $"{s.Spec.Name} — 그냥 두라고 했다");
        Life.Diary(w, lead, $"{Ko.IGa(by.Name)} {Ko.EulReul(s.Spec.Name)} 알았는데 그냥 두라고 했다.");
    }

    private void Remove(Scheme s, CrewMember by, string why)
    {
        var w = _w;
        var lead = P(s.Lead)!;
        Stats.Removed++;
        Mark(s, TraceState.Removed, $"{s.Spec.Name} — 치웠다");
        End(s, SchemeStage.Done, $"치우라고 했다 ({why})");
        if (s.Spec.Cat == SchemeCat.Rule) AddRule(s, false, $"{s.Spec.Name} 금지");
        w.Relations.Remember(lead, by, RelationReason.BlamedMe, $"{s.Spec.Name} — 치우라고 했다");
        w.Brain2.Emotions.Feel(lead, Feeling.Sadness, 0.25f, $"{s.Spec.Name}을 치웠다", by);
        lead.Needs.Stress = MathF.Min(1f, lead.Needs.Stress + 0.05f);
        if (by.Room == lead.Room) by.Say(w, Persona.Say(by, $"{why} — 치워"));
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(by.Name)} {Ko.EulReul(s.Spec.Name)} 치우게 했다 — {why}", by.Id);
        Life.Diary(w, lead, $"{Ko.IGa(by.Name)} {Ko.EulReul(s.Spec.Name)} 치우라고 했다. {why}라나.");
    }

    private string LeadWhy(Scheme s, CrewMember lead)
    {
        var d = Drives.Where(x => (s.Spec.Drives & x) != 0).OrderByDescending(x => DriveOf(lead, x)).FirstOrDefault();
        return d switch
        {
            Drive.Bored => "아무 일 없는 날이 너무 길다", Drive.Stress => "다들 지쳤다 — 숨 돌릴 데가 있어야 한다", Drive.Free => "몰래 하느니 떳떳하게 하자",
            Drive.Lonely => "모여서 하면 덜 외롭다", Drive.Craft => "손으로 만든 걸 다 같이 쓰자", Drive.Hungry => "먹을 걸 늘리는 일이다", Drive.Greed => "정해진 날에만 열면 탈이 없다",
            Drive.Homesick => "고향 생각이 덜 난다", Drive.Pride => "보여 주고 싶었다", Drive.Love => "좋은 걸 나누고 싶다", _ => "해 보니 괜찮더라",
        };
    }

    private void ProposeVote(Scheme s, CrewMember lead, CrewMember by)
    {
        var w = _w;
        var spec = s.Spec;
        string legit = LegitName(spec);
        if (!s.KnowsWho(by.Id)) s.Knows[by.Id] = KnowHow.Saw;
        string where = RoomOf(s)?.Name ?? "배 안";
        var post = w.Info.Chat.Post(by, ChatKind.Notice, ShipChat.Voice(by, $"{where}에서 {spec.Name} 판이 나왔다. 회의에 올린다", $"{where}에서 {Ko.IGa(spec.Name)} 나왔습니다. 회의에서 정하겠습니다"));
        foreach (var c in w.Crew) if (Adult(c) && !s.Knew(c.Id) && w.Info.Chat.HasRead(c, post.Id)) s.Knows[c.Id] = KnowHow.Chat;
        if (MotionSystem.Off || lead.Dead)
        {
            float sum = 0f; int n = 0;
            foreach (var c in w.Crew) if (Adult(c)) { sum += Approve(c, s).v; n++; }
            if (n > 0 && sum / n > 0f) Legitimize(s, -1); else Ban(s, "모여서 정했다", -1);
            return;
        }
        var m = w.Motions.Propose(lead, MotionKind.Proposal, SittingKind.Regular, $"{Ko.EulReul(spec.Name)} 금지할까, {Ko.EuRo(legit)} 정할까", LeadWhy(s, lead));
        _motionOf[m.Id] = s.Id;
        s.Motion = m.Id;
        s.Stage = SchemeStage.Vote;
        Stats.Votes++;
        foreach (int id in s.Crew.ToList()) if (id != lead.Id && P(id) is { Dead: false }) w.Motions.Cosign(m, id);
        w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(by.Name)} 알렸다 — {spec.Name} · 회의에서 금지냐 {Ko.EuRo(legit)} 하느냐를 정한다", by.Id);
        Life.Diary(w, lead, $"{Ko.IGa(by.Name)} {Ko.EulReul(spec.Name)} 알렸다. 들킨 김에 회의에 올렸다 — {legit}.");
    }

    private void Accuse(Scheme s, CrewMember lead, CrewMember by, string why)
    {
        var w = _w;
        var spec = s.Spec;
        if (by == lead) return;
        Stats.Trials++;
        if (MotionSystem.Off) { Ban(s, "함장이 벌을 줬다", -1); return; }
        int theft = -1;
        if (LedgerItem(spec) is ItemKind k && ItemKinds.IsFood(k) && RoomOf(s) is Room room)
        {
            var th = new Theft { Id = 9000 + s.Id, Thief = lead.Id, Tick = w.Tick, RoomId = room.Id, Room = room.Name, Item = k == ItemKind.Ration ? "비상식량" : "식사", Noticed = true, Accused = true };
            foreach (var kv in s.Knows) if (kv.Value == KnowHow.Saw && kv.Key != lead.Id) th.Witnesses.Add(kv.Key);
            if (!th.Witnesses.Contains(by.Id) && s.ComputerSaid != 1) th.Witnesses.Add(by.Id);
            w.Motions.Thefts.Add(th);
            theft = th.Id;
        }
        var m = w.Motions.Propose(by, MotionKind.Accusation, SittingKind.Trial, $"{lead.Name} 고발 — {spec.Name}", why, target: lead.Id, theft: theft);
        _motionOf[m.Id] = s.Id;
        s.Motion = m.Id;
        s.Stage = SchemeStage.Vote;
        foreach (var kv in s.Knows.ToList()) if (kv.Value == KnowHow.Saw && kv.Key != by.Id && kv.Key != lead.Id && P(kv.Key) is CrewMember wit && Approve(wit, s).v < 0f) w.Motions.Cosign(m, wit.Id);
        w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(by.Name)} {Ko.EulReul(lead.Name)} 고발했다 — {spec.Name}", by.Id);
    }

    private void Grieve(Scheme s, CrewMember lead, CrewMember by)
    {
        var w = _w;
        if (by == lead) return;
        Stats.Grievances++;
        if (MotionSystem.Off) { End(s, SchemeStage.Done, "한 소리 들었다"); return; }
        var m = w.Motions.Propose(by, MotionKind.Grievance, SittingKind.Regular, $"{lead.Name}에 대한 불만 — {s.Spec.Name}", $"{RoomOf(s)?.Name ?? "배 안"}에서 {Ko.EulReul(s.Spec.Name)} 봤다", target: lead.Id);
        _motionOf[m.Id] = s.Id;
        s.Motion = m.Id;
        s.Stage = SchemeStage.Vote;
    }

    private Practice AddPractice(Scheme s, string name, int every, float hour, IEnumerable<int> followers, string origin)
    {
        var w = _w;
        var spec = s.Spec;
        int room = spec.Place is Place.Hidden or Place.Engine or Place.Cargo or Place.Bunk or Place.Core or Place.Comms
            ? (w.Ship.LiveRooms.Where(r => r.Kind is RoomType.Lounge or RoomType.Mess).OrderBy(r => r.Kind == RoomType.Lounge ? 0 : 1).ThenBy(r => r.Id).FirstOrDefault()?.Id ?? s.RoomId)
            : s.RoomId;
        if (spec.Key is "engine_sauna" or "secret_garden") room = s.RoomId;
        var p = new Practice { Key = spec.Key, Name = name, Scheme = s.Id, RoomId = room, Every = every, Hour = hour, Born = w.Tick, Origin = origin, Length = spec.Key is "meditation" or "run_club" ? 0.75f : 1.5f };
        foreach (int id in followers) if (!p.Followers.Contains(id)) p.Followers.Add(id);
        Schedule(p, true);
        Practices.Add(p);
        Stats.Practices++;
        w.History.Add(w, HistoryKind.Lesson, $"관행이 생겼다: {name} ({origin})", w.Ship.Rooms.ElementAtOrDefault(room), log: true);
        return p;
    }

    private void Legitimize(Scheme s, int motion)
    {
        var w = _w;
        var spec = s.Spec;
        var lead = P(s.Lead);
        string legit = LegitName(spec);
        Stats.Legit++;
        var yes = w.Motions.Get(motion) is Motion m ? m.Final.Where(kv => kv.Value > 0f).Select(kv => kv.Key) : Enumerable.Empty<int>();
        AddPractice(s, legit, spec.Key is "pirate_radio" ? 1 : spec.Key is "gambling_den" or "betting_pool" ? 2 : 4, spec.Key is "coffee_roast" ? 7.5f : 20f, s.Crew.Concat(yes), $"{Ko.IGa(spec.Name)} 들킨 뒤 회의에서 정했다");
        AddRule(s, true, $"{legit} — 정해진 날에는 괜찮다", motion);
        Mark(s, TraceState.Official, legit);
        End(s, SchemeStage.Done, $"{Ko.EuRo(legit)} 정했다");
        if (lead != null)
        {
            w.Brain2.Emotions.Feel(lead, Feeling.Pride, 0.35f, $"{legit} — 회의가 받아 줬다");
            lead.Needs.Stress = MathF.Max(0f, lead.Needs.Stress - 0.1f);
            Life.Diary(w, lead, $"회의가 받아 줬다. 이제 {legit}이다.");
        }
        w.Log.Add(w.Tick, LogKind.Ship, $"회의가 정했다 — {Ko.EunNeun(spec.Name)} 이제 '{legit}'");
    }

    private void Ban(Scheme s, string why, int motion)
    {
        var w = _w;
        var spec = s.Spec;
        Stats.Banned++;
        AddRule(s, false, $"{spec.Name} 금지", motion);
        Mark(s, TraceState.Seized, $"압수 — {spec.Name}");
        End(s, SchemeStage.Done, $"금지됐다 ({why})");
        if (s.Skimmed > s.SkimSeen && LedgerItem(spec) is ItemKind k) Return(k, s.Skimmed - s.SkimSeen);
        foreach (int id in s.Crew)
            if (P(id) is CrewMember c && !c.Dead)
            {
                w.Brain2.Emotions.Feel(c, Feeling.Shame, 0.2f, $"{spec.Name} 금지");
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
                SetBored(c, Bored(c) + 0.1f);
                Life.Diary(w, c, $"{spec.Name} — 금지됐다. {why}.");
            }
        w.History.Add(w, HistoryKind.Decision, $"금지 — {spec.Name} ({why})", RoomOf(s), log: true);
    }

    private void Return(ItemKind k, int n)
    {
        foreach (var box in _w.Ship.Containers)
        {
            n -= box.Storage!.Add(k, n);
            if (n <= 0) return;
        }
    }

    // ───────────────────────────── 회의 훅 (MotionsResolve) ─────────────────────────────

    /// <summary>꾸민 일이 올라온 안건에 대한 이 사람의 생각 (안건의 다른 근거에 더한다).</summary>
    public (float v, string why)? Opinion(CrewMember c, Motion m)
    {
        if (!_motionOf.TryGetValue(m.Id, out var sid) || Get(sid) is not Scheme s) return null;
        var (a, why) = Approve(c, s);
        return m.Kind switch
        {
            MotionKind.Proposal => (0.9f * a, why),
            MotionKind.Accusation or MotionKind.Grievance => (-0.35f * a, why),
            _ => null,
        };
    }

    public string? Effect(Motion m, bool pass)
    {
        if (!_motionOf.TryGetValue(m.Id, out var sid) || Get(sid) is not Scheme s || m.Kind != MotionKind.Proposal) return null;
        if (s.Spec.Fate is Fate.Event or Fate.Club) return pass ? $"{LegitName(s.Spec)} — 정식 행사로" : "다음에 또 보자";
        return pass ? $"{LegitName(s.Spec)} — 정식으로" : $"{s.Spec.Name} 금지";
    }

    public void Decided(Motion m, bool pass)
    {
        if (!_motionOf.TryGetValue(m.Id, out var sid) || Get(sid) is not Scheme s || s.Stage != SchemeStage.Vote) return;
        var w = _w;
        switch (m.Kind)
        {
            case MotionKind.Proposal when s.Spec.Fate is Fate.Event or Fate.Club:
                if (pass) { AddPractice(s, LegitName(s.Spec), 7, 19f, s.Crew.Concat(m.Final.Where(kv => kv.Value > 0f).Select(kv => kv.Key)), $"{s.Spec.Name} 뒤 회의에서 정식 행사로 정했다"); Mark(s, TraceState.Official, LegitName(s.Spec)); }
                End(s, SchemeStage.Done, pass ? "정식 행사가 됐다" : "한 번으로 끝났다");
                break;
            case MotionKind.Proposal when s.Spec.Fate == Fate.Vote:
                if (pass) Legitimize(s, m.Id); else Ban(s, "회의에서 반대가 많았다", m.Id);
                break;
            case MotionKind.Accusation:
                if (pass) { Ban(s, $"재판 — {MotionSystem.PenaltyName(m.Verdict)}", m.Id); s.Outcome = $"재판에서 벌을 받았다 ({MotionSystem.PenaltyName(m.Verdict)})"; }
                else { Mark(s, TraceState.Kept, s.Spec.Name); End(s, SchemeStage.Done, "재판에서 너그럽게 넘어갔다"); }
                break;
            case MotionKind.Grievance:
                Mark(s, pass ? TraceState.Removed : TraceState.Kept, s.Spec.Name);
                End(s, SchemeStage.Done, pass ? "회의에서 공개 경고를 받았다" : "회의가 넘어가 줬다");
                break;
            default:
                End(s, SchemeStage.Done, pass ? "회의에서 받아들여졌다" : "회의에서 떨어졌다");
                if (pass && s.Spec.Key is "no_confidence" or "mutiny_plot" or "election_posters") w.History.Add(w, HistoryKind.Decision, $"{P(s.Lead)?.Name}의 {s.Spec.Name} — 함장 신임 표결로 이어졌다", log: false);
                break;
        }
    }

    // ───────────────────────────── 지켜보기 (2분마다) ─────────────────────────────

    private static SmellKind SmellOf(SchemeSpec s) => (s.Tells & Tell.Smoke) != 0 ? SmellKind.Burnt : s.Key is "herb_tea" or "flower_perfume" ? SmellKind.Cooking : SmellKind.Foul;
    public bool Smelly(SchemeSpec s) => (s.Tells & (Tell.Smell | Tell.Smoke)) != 0;
    public bool WorkingNow(Scheme s) => s.Working && _w.Tick - s.Stopped < SimTime.Minutes(2);

    /// <summary>냄새 (SmellSystem.Sources 훅): 익어 가는 술 · 절인 배추 · 볶는 콩 · 몰래 피우는 담배.</summary>
    public void AddSmells(SmellSystem sm)
    {
        if (Off) return;
        foreach (var s in All)
        {
            if (!s.Hiding || !Smelly(s.Spec) || RoomOf(s) is not Room r) continue;
            float v = (s.Spec.Tells & Tell.Smoke) != 0 ? (WorkingNow(s) || s.InSession(_w.Tick) ? 0.55f : 0f)
                : s.Stage == SchemeStage.Live ? 0.2f + 0.6f * s.Ripe : s.Progress > 0.4f ? 0.15f * s.Progress : 0f;
            if (s.InSession(_w.Tick)) v += 0.15f;
            if (v > 0.01f) sm.Emit(r, SmellOf(s.Spec), MathF.Min(1f, v));
        }
    }

    private void Watch(Scheme s)
    {
        var w = _w;
        var room = RoomOf(s);
        if (room == null) return;
        var spec = s.Spec;
        if (s.Working && w.Tick - s.Stopped > SimTime.Minutes(4)) s.Working = false;
        var here = Here(room);
        // 장난: 걸릴 사람이 오면 터진다
        if (spec.Fate == Fate.Laugh && s.Stage == SchemeStage.Live)
        {
            foreach (var c in here)
                if (c.IsAwake && Adult(c) && c.Id != s.Lead && (c.Id == s.Target || s.Target < 0 || w.Tick - s.Since > SimTime.TicksPerDay * 2))
                { FirePrank(s, c); return; }
            return;
        }
        if (!s.Hiding) return;
        // 불: 끄러 온 사람이 본다
        if (w.Fire.CountIn(room) > 0 && s.Progress + s.Ripe > 0.1f)
        {
            foreach (var c in here)
                if (!s.Crew.Contains(c.Id) && c.CanAct && Adult(c)) { Discover(s, c, "불을 끄다가"); return; }
        }
        bool busy = s.InSession(w.Tick) || WorkingNow(s) && here.Any(c => s.Crew.Contains(c.Id) && c.Job?.Activity is SchemeActivity);
        float exposed = busy ? 0.2f : s.Stage == SchemeStage.Live || s.Progress > 0.25f ? 0.02f : 0.005f;
        bool reek = Smelly(spec) && w.Smells.Level(room, SmellOf(spec)) > SmellSystem.Threshold(SmellOf(spec));
        if (reek) exposed += 0.12f;
        float vis = 1f - spec.Secrecy;
        foreach (var c in here)
        {
            if (!Adult(c) || !c.IsAwake || s.KnowsWho(c.Id) || s.Crew.Contains(c.Id) ) continue;
            float curious = 0.8f + (c.Habits.Contains(Habit.NeatFreak) ? 0.3f : 0f) + (c.Value == CrewValue.Rules ? 0.2f : 0f);
            bool checking = _suspect.TryGetValue(c.Id, out var sus) && sus == s.Id;
            if (checking || R.Chance(exposed * vis * curious))
            {
                if (checking) _suspect.Remove(c.Id);
                bool nose = checking ? Smelly(spec) : reek && !busy;
                string how = nose ? "냄새를 따라가 보니" : checking ? "소리를 따라가 보니" : busy ? "하는 걸 봤다" : "숨겨 둔 걸 봤다";
                if (nose) Stats.Smelled++;
                Discover(s, c, how);
                if (!s.Active || s.Stage == SchemeStage.Vote) return;
            }
        }
        // 냄새가 새어 나간다: 맡은 사람이 그쪽으로 확인하러 간다
        if (Smelly(spec) && s.Hiding)
        {
            var kind = SmellOf(spec);
            foreach (var c in w.Crew)
            {
                if (!Adult(c) || !c.IsAwake || c.Room == room || s.Knew(c.Id) || _suspect.ContainsKey(c.Id)) continue;
                if (w.Smells.Smelled(c, kind, SimTime.Minutes(15)) is not { } sn || sn.Strength < SmellSystem.Threshold(kind)) continue;
                if (w.Smells.Likely(c, kind) != room) continue;
                float nosy = (c.Habits.Contains(Habit.NeatFreak) ? 0.5f : 0.2f) + (c.Value is CrewValue.Rules or CrewValue.Safety ? 0.2f : 0f) + (kind == SmellKind.Burnt ? 0.4f : 0f);
                if (R.Chance(nosy * 0.25f)) _suspect[c.Id] = s.Id;
            }
        }
    }

    /// <summary>지금 그 자리에 모르는 사람이 있나 (몰래 하는 일은 손을 멈춘다).</summary>
    public bool Watched(Scheme s, CrewMember worker)
    {
        if (s.Spec.Secrecy < 0.45f) return false;
        foreach (var c in _w.Crew)
            if (c.Room == worker.Room && c != worker && c.IsAwake && Adult(c) && !s.KnowsWho(c.Id) && !s.Crew.Contains(c.Id)) return true;
        return false;
    }

    // ───────────────────────────── 장난 ─────────────────────────────

    private float LaughOdds(CrewMember v, CrewMember? lead, SchemeSpec spec)
    {
        bool H(Habit h) => v.Habits.Contains(h);
        return 0.45f + 0.35f * DriveOf(v, Drive.Jest) + 0.3f * (v.Traits.Sociability - 0.5f) + (lead != null ? 0.35f * v.AffinityTo(lead) : 0f)
               - 0.6f * MathF.Max(0f, v.Needs.Stress - 0.35f) - (H(Habit.Serious) ? 0.25f : 0f) - (H(Habit.ShortTempered) ? 0.2f : 0f) - (H(Habit.Grumbler) ? 0.12f : 0f)
               + (H(Habit.Cheerful) ? 0.15f : 0f) - (spec.Need == Need.Captain ? 0.1f : 0f) - (v.Mind.Anger > 0.3f ? 0.2f : 0f);
    }

    /// <summary>시험용: 장난을 바로 터뜨린다 (watched = 꾸민 사람이 그 자리에서 보고 있었나).</summary>
    public void Spring(Scheme s, CrewMember victim, bool? watched = null) { if (s.Active) FirePrank(s, victim, watched); }

    private void FirePrank(Scheme s, CrewMember victim, bool? watched = null)
    {
        var w = _w;
        var spec = s.Spec;
        var lead = P(s.Lead);
        Stats.Pranks++;
        s.Target = victim.Id;
        s.Knows[victim.Id] = KnowHow.Victim;
        s.Identified = lead != null && (watched ?? (lead.Room == victim.Room || R.Chance(lead.Habits.Contains(Habit.Prankster) ? 0.55f : 0.35f))); // 장난꾼은 자랑을 못 참는다
        bool laugh = LaughOdds(victim, lead, spec) > 0.5f;
        victim.Say(w, Persona.Say(victim, laugh ? (victim.Id % 3 == 0 ? "하하, 이거 누구야!" : victim.Id % 3 == 1 ? "아 진짜… 웃기네" : "당했다!") : (victim.Id % 2 == 0 ? "…장난도 정도가 있지" : "누구야, 이거")));
        string outcome;
        foreach (var o in Here(victim.Room))
            if (o != victim && o.IsAwake && Adult(o))
            {
                if (!s.Knew(o.Id)) s.Knows[o.Id] = s.Identified ? KnowHow.Saw : KnowHow.Heard;
                w.Brain2.Emotions.Feel(o, Feeling.Joy, laugh ? 0.12f : 0.04f, $"{victim.Name} — {spec.Legit}", victim);
                o.Needs.Social = MathF.Min(1f, o.Needs.Social + 0.05f);
            }
        if (s.Identified && lead != null)
        {
            if (laugh)
            {
                Stats.Laughed++;
                w.Relations.Remember(victim, lead, RelationReason.LaughedTogether, $"{spec.Name} — 같이 웃었다");
                victim.ChangeAffinity(lead, 0.06f); lead.ChangeAffinity(victim, 0.05f);
                w.Brain2.Emotions.Feel(victim, Feeling.Joy, 0.2f, $"{spec.Name} — 웃겼다", lead);
                outcome = "웃어넘겼다";
            }
            else
            {
                Stats.Grudges++;
                w.Relations.Remember(victim, lead, RelationReason.PrankedMe, $"{spec.Name} — 나를 놀림감으로 삼았다");
                victim.ChangeAffinity(lead, -0.1f);
                w.Brain2.Emotions.Feel(victim, Feeling.Anger, 0.3f, $"{spec.Name}", lead);
                MindSystem.Anger(victim, 0.12f);
                if (lead.Value == CrewValue.People || lead.Traits.Diligence > 0.6f) w.Brain2.Emotions.Feel(lead, Feeling.Shame, 0.15f, $"{victim.Name}이 정말 화났다", victim);
                outcome = "앙금이 남았다";
            }
        }
        else
        {
            // 누가 했는지 모른다 → 메신저에 묻는다 · 장난칠 만한 사람을 짐작한다 (틀릴 수도 있다)
            w.Info.Chat.Post(victim, ChatKind.Ask, ShipChat.Voice(victim, $"{spec.Legit} — 누가 그랬어?", $"{spec.Legit} — 하신 분 계신가요?"));
            var guess = w.Crew.Where(o => Adult(o) && o != victim).OrderByDescending(o => DriveOf(o, Drive.Jest) + 0.2f * (o.Room == victim.Room ? 1f : 0f)).ThenBy(o => o.Id).FirstOrDefault();
            // 웃어넘기는 걸 보면 장난꾼은 자기가 했다고 밝히고 싶어 못 견딘다 (사이가 좋아진다)
            if (laugh && lead != null && lead.CanAct && (lead.Habits.Contains(Habit.Prankster) || lead.Habits.Contains(Habit.Joker) || R.Chance(0.4f)))
            {
                Stats.Laughed++;
                s.Identified = true;
                s.Knows[victim.Id] = KnowHow.Chat;
                var ask = w.Info.Chat.All.Count > 0 ? w.Info.Chat.All[^1] : null;
                w.Info.Chat.Post(lead, ChatKind.Joke, ShipChat.Voice(lead, "그거 나야 ㅋㅋ 표정 봤어야 했는데", "죄송해요, 그거 제가 했어요"), reply: ask?.Author == victim.Id ? ask.Id : -1);
                w.Relations.Remember(victim, lead, RelationReason.LaughedTogether, $"{spec.Name} — 알고 보니 {Ko.IGa(lead.Name)} 했다 · 같이 웃었다");
                victim.ChangeAffinity(lead, 0.05f); lead.ChangeAffinity(victim, 0.04f);
                w.Brain2.Emotions.Feel(victim, Feeling.Joy, 0.15f, $"{spec.Name} — 웃겼다", lead);
                outcome = $"웃어넘기자 {Ko.IGa(lead.Name)} 자기가 했다고 털어놨다";
            }
            else if (laugh) { Stats.Laughed++; outcome = "누가 했는지 모른 채 웃어넘겼다"; }
            else
            {
                Stats.Grudges++;
                if (guess != null && lead != null)
                {
                    var mem = w.Relations.Remember(victim, guess, RelationReason.PrankedMe, $"{spec.Name} — {Ko.IGa(guess.Name)} 그랬을 거다");
                    if (guess != lead) mem.Truth = $"{spec.Name}은 {Ko.IGa(lead.Name)} 한 장난이었다";
                    victim.ChangeAffinity(guess, -0.06f);
                }
                outcome = guess != null && guess != lead ? $"엉뚱하게 {Ko.EulReul(guess.Name)} 의심한다" : "앙금이 남았다";
            }
        }
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(victim.Name)} 장난에 걸렸다 — {spec.Legit} · {outcome}", victim.Id);
        Life.Diary(w, victim, $"{spec.Legit}. {(laugh ? "어이가 없어서 웃었다" : "기분이 나빴다")}.");
        if (lead != null) { SetBored(lead, Bored(lead) - 0.35f); Life.Diary(w, lead, $"{victim.Name} — {spec.Legit}. {(laugh ? "같이 웃었다" : "생각보다 화를 냈다")}."); }
        Mark(s, TraceState.Public, spec.Legit);
        End(s, SchemeStage.Done, outcome);
    }

    /// <summary>컴퓨터 목소리 장난: 컴퓨터가 바로 안다 — 성격에 따라 하루 맞춰 주거나 되돌리고 함장에게 알린다.</summary>
    private void VoicePrank(Scheme s, CrewMember lead)
    {
        var w = _w;
        var a = w.Automation;
        var ch = a.Character;
        s.ComputerKnows = true;
        Stats.ComputerFound++;
        Stats.Pranks++;
        int laughs = 0, sour = 0;
        foreach (var c in w.Crew)
        {
            if (!Adult(c) || !c.IsAwake || c == lead || c.Outside) continue;
            if (!s.Knew(c.Id)) s.Knows[c.Id] = KnowHow.Heard;
            if (LaughOdds(c, null, s.Spec) > 0.5f) { laughs++; w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.1f, "꽥꽥거리는 안내 방송"); }
            else { sour++; w.Brain2.Emotions.Feel(c, Feeling.Anger, 0.06f, "꽥꽥거리는 안내 방송"); }
        }
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 꽥 — 정기 점검 시간입니다 — 꽥");
        bool along = ch.Caution < 0.15f && ch.PeopleTilt > -0.2f && laughs >= sour;
        if (along)
        {
            VoiceUntil = w.Tick + SimTime.Hours(8);
            s.ComputerSaid = 0;
            s.ComputerWhy = "다들 웃는다 — 하루는 맞춰 준다";
            Stats.ComputerQuiet++;
            w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 누가 제 목소리를 바꿨군요. 다들 웃으시니 오늘 하루는 이대로 하겠습니다 — 꽥");
            Stats.Laughed++;
            End(s, SchemeStage.Done, $"컴퓨터가 하루 맞춰 줬다 (웃음 {laughs} · 짜증 {sour})");
        }
        else
        {
            VoiceUntil = w.Tick + SimTime.Minutes(20);
            s.ComputerSaid = 1;
            s.ComputerWhy = ch.Caution >= 0.15f ? "안내 방송은 비상 때 알아들어야 한다" : "짜증 내는 사람이 더 많다";
            Stats.ComputerTold++;
            w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 안내 목소리를 되돌렸습니다 — 비상 방송을 못 알아들으면 안 됩니다. 손댄 시각은 기록에 남겼습니다");
            if (w.Command.Captain is CrewMember cap && cap != lead)
            {
                s.Knows[cap.Id] = KnowHow.Told;
                bool ok = LaughOdds(cap, lead, s.Spec) > 0.55f;
                w.Relations.Remember(cap, lead, ok ? RelationReason.LaughedTogether : RelationReason.PrankedMe, "컴퓨터 목소리를 오리 소리로 바꿨다");
                if (ok) Stats.Laughed++; else Stats.Grudges++;
                End(s, SchemeStage.Done, ok ? "함장이 웃어넘겼다" : "함장에게 한 소리 들었다");
            }
            else End(s, SchemeStage.Done, "컴퓨터가 되돌렸다");
        }
        ch.Nudge(0.01f, 0f, "누가 안내 목소리를 바꿨다");
        Mark(s, TraceState.Public, "꽥꽥거린 안내 방송");
    }
}
