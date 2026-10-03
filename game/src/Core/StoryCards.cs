using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.17 대화 카드: 중요한 순간(다툼 중재 · 공황 진정 · 반란 설득 · 범인 추궁 · 고백 · 털어놓기 · 연인의 위기)에
//   말하는 사람 · 듣는 사람 · 표정 · 대사 · 선택지. 보는 사람은 고르지 않는다 — 말하는 사람이 제 성격대로 고른다.
//   선택지는 관계(깊은 사이만) · 성향(규칙 · 농담 · 앞장섬) · 상황(같은 고향 · 같이 겪은 일 · 자격)으로 열리고,
//   가능성을 근거(솜씨 · 관계 · 기분 · 상황)와 함께 셈한다. 실패도 새 갈래(모의가 앞당겨진다 · 함장에게 알린다 · 사이에 금이 간다 · 누명).
//   결과는 기록 · 일기 · 연대기 · 관계 · 감정 · 이야기 · 연애로 이어진다.

public enum CardKind : byte { Mediate, Calm, Persuade, Accuse, Confess, Heart, Mend }

public sealed class TalkOption
{
    public string Key { get; init; } = "";
    public string Text { get; init; } = "";
    public string Say { get; init; } = "";
    public bool Open { get; init; }
    public string Lock { get; init; } = "";
    public float Base { get; init; }
    public float Chance { get; set; }
    public List<(string label, float v)> Why { get; } = new();
    public float Pull { get; set; }
    public string PullWhy { get; set; } = "";
}

public sealed class TalkCard
{
    public int Id { get; init; }
    public CardKind Kind { get; init; }
    public long Tick { get; init; }
    public int Speaker { get; init; }
    public int Listener { get; init; }
    public int Third { get; init; } = -1;
    public int RoomId { get; init; } = -1;
    public int Ref { get; init; } = -1;
    public string Title { get; set; } = "";
    public string Scene { get; set; } = "";
    public string Opening { get; set; } = "";
    public string Answer { get; set; } = "";
    public FaceLook SpeakerFace { get; set; }
    public FaceLook ListenerFace { get; set; }
    public FaceLook ListenerAfter { get; set; }
    public List<TalkOption> Options { get; } = new();
    public int Chosen { get; set; } = -1;
    public string ChoseWhy { get; set; } = "";
    public float Roll { get; set; }
    public bool Success { get; set; }
    public string Outcome { get; set; } = "";
    public string Branch { get; set; } = "";
    public TalkOption? Pick => Chosen >= 0 && Chosen < Options.Count ? Options[Chosen] : null;
}

/// <summary>누가 누구에게 무슨 말을 하러 가는가 (StoryActivity가 그 사람에게 걸어간다).</summary>
public sealed record TalkIntent(int Speaker, int Listener, CardKind Kind, int Ref, long Born, int Third = -1);

public sealed partial class StorySystem
{
    public List<TalkCard> Cards { get; } = new();
    private int _nextCard = 1;
    private readonly List<TalkIntent> _talks = new();
    private readonly SortedSet<int> _plotSeen = new(), _prankSeen = new();
    private readonly SortedDictionary<long, long> _feudSeen = new();
    private readonly SortedDictionary<int, long> _panicSeen = new();

    public IReadOnlyList<TalkIntent> Talks => _talks;
    public TalkIntent? Talking(int speaker) { foreach (var t in _talks) if (t.Speaker == speaker) return t; return null; }
    public TalkCard? LastCard => Cards.Count > 0 ? Cards[^1] : null;

    public static string KindName(CardKind k) => k switch
    {
        CardKind.Mediate => "다툼 중재", CardKind.Calm => "공황 진정", CardKind.Persuade => "반란 설득", CardKind.Accuse => "추궁",
        CardKind.Confess => "고백", CardKind.Heart => "털어놓기", _ => "둘 사이의 고비",
    };

    public void Intend(CrewMember speaker, CrewMember listener, CardKind kind, int @ref = -1, int third = -1)
    {
        if (Talking(speaker.Id) != null) return;
        _talks.Add(new TalkIntent(speaker.Id, listener.Id, kind, @ref, _w.Tick, third));
    }

    private void ConfideIntent(Arc a, CrewMember c)
    {
        if (Talking(c.Id) != null) return;
        var f = P(a.Friend) is CrewMember fr && !fr.Dead && fr.AffinityTo(c) > 0.05f ? fr : FriendOf(c);
        if (f == null) return;
        a.Friend = f.Id;
        Intend(c, f, CardKind.Heart, a.Id);
    }

    // ───────────────────────────── 지켜보기: 카드가 필요한 순간 ─────────────────────────────

    private void PanicWatch()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.Mind.Panicking(w.Tick) || c.Room == null) continue;
            if (_panicSeen.TryGetValue(c.Id, out long until) && until == c.Mind.PanicUntil) continue;
            _panicSeen[c.Id] = c.Mind.PanicUntil;
            CrewMember? best = null; float bv = -1f;
            foreach (var o in w.Crew)
            {
                if (o == c || !Adult(o) || !o.CanAct || !o.IsAwake || o.Room != c.Room || o.Mind.Panicking(w.Tick)) continue;
                float v = c.AffinityTo(o) + (o.Quals.Contains(Qual.Counseling) ? 0.4f : 0f) + (o.Role == CrewRole.Medic ? 0.25f : 0f) + o.Traits.Calm * 0.3f;
                if (v > bv) { bv = v; best = o; }
            }
            if (best != null) Hold(CardKind.Calm, best, c);
        }
    }

    private void CardWatch()
    {
        var w = _w;
        // 반란 모의: 우두머리와 가장 가까운 사람이 말리러 간다 (모의에 끼지 않은 사람)
        foreach (var s in w.Schemes.All)
        {
            if (s.Spec.Key != "mutiny_plot" || s.Stage is not (SchemeStage.Plan or SchemeStage.Prep) || _plotSeen.Contains(s.Id)) continue;
            if (P(s.Lead) is not CrewMember lead || lead.Dead) continue;
            CrewMember? best = null; float bv = 0.12f;
            foreach (var o in w.Crew)
            {
                if (o == lead || !Adult(o) || !o.CanAct || s.Crew.Contains(o.Id) || o.Id == w.Command.CaptainId) continue;
                float v = MathF.Min(o.AffinityTo(lead), lead.AffinityTo(o)) + (s.Knew(o.Id) ? 0.2f : 0f);
                if (v > bv) { bv = v; best = o; }
            }
            if (best == null) continue;
            _plotSeen.Add(s.Id);
            if (!s.Knew(best.Id)) s.Knows[best.Id] = KnowHow.Heard;
            Intend(best, lead, CardKind.Persuade, s.Id);
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(best.Name)} {lead.Name}의 낌새를 눈치챘다 — 말리러 간다", best.Id);
        }
        // 누가 했는지 모르는 장난: 당한 사람이 짐작 가는 사람을 찾아가 따진다
        foreach (var s in w.Schemes.All)
        {
            if (s.Spec.Cat != SchemeCat.Prank || s.Identified || s.Target < 0 || _prankSeen.Contains(s.Id)) continue;
            if (s.Stage is not (SchemeStage.Live or SchemeStage.Done) || w.Tick - s.Since > SimTime.Hours(20)) continue;
            if (P(s.Target) is not CrewMember victim || !Adult(victim) || !victim.CanAct) continue;
            _prankSeen.Add(s.Id);
            var suspect = w.Crew.Where(o => o != victim && Adult(o) && o.CanAct)
                .OrderByDescending(o => (o.Habits.Contains(Habit.Prankster) ? 1f : 0f) + (o.Habits.Contains(Habit.Joker) ? 0.5f : 0f) - victim.AffinityTo(o) + (s.Knows.TryGetValue(victim.Id, out var kh) && kh == KnowHow.Saw && o.Id == s.Lead ? 2f : 0f))
                .ThenBy(o => o.Id).FirstOrDefault();
            if (suspect != null) Intend(victim, suspect, CardKind.Accuse, s.Id);
        }
        // 서로 말을 안 하는 사이: 둘 다와 가까운 사람이 중재에 나선다
        var adults = w.Crew.Where(c => Adult(c) && c.CanAct).ToList();
        for (int i = 0; i < adults.Count; i++)
            for (int j = i + 1; j < adults.Count; j++)
            {
                var a = adults[i]; var b = adults[j];
                if (a.AffinityTo(b) > -0.45f || b.AffinityTo(a) > -0.3f) continue;
                long key = a.Id * 4096L + b.Id;
                if (_feudSeen.TryGetValue(key, out long at) && w.Tick - at < SimTime.TicksPerDay * 2) continue;
                CrewMember? m = null; float mv = 0.05f;
                foreach (var o in adults)
                {
                    if (o == a || o == b || Talking(o.Id) != null) continue;
                    float v = MathF.Min(a.AffinityTo(o), b.AffinityTo(o)) + (o.Quals.Contains(Qual.Counseling) ? 0.3f : 0f) + (o.Traits.Sociability - 0.5f) * 0.3f;
                    if (v > mv) { mv = v; m = o; }
                }
                _feudSeen[key] = w.Tick;
                if (m != null) Intend(m, a, CardKind.Mediate, -1, b.Id);
            }
    }

    private void ExpireTalks()
    {
        var w = _w;
        for (int i = _talks.Count - 1; i >= 0; i--)
        {
            var t = _talks[i];
            var s = P(t.Speaker); var l = P(t.Listener);
            bool gone = s == null || l == null || s.Dead || l.Dead || w.Tick - t.Born > SimTime.Hours(10);
            if (!gone && t.Kind == CardKind.Persuade && w.Schemes.Get(t.Ref) is Scheme sc && sc.Stage is not (SchemeStage.Plan or SchemeStage.Prep)) gone = true;
            if (gone) { _talks.RemoveAt(i); continue; }
            // 이미 같은 방에 있으면 걸어갈 것 없이 그 자리에서
            if (s!.CanAct && l!.IsAwake && s.IsAwake && s.Room != null && s.Room == l.Room && (s.Position - l.Position).LengthSquared() < 9f && !Crisis.Acting(w)) Meet(s, t);
        }
    }

    /// <summary>찾아간 사람 곁에 닿았다 — 카드가 펼쳐진다.</summary>
    public TalkCard? Meet(CrewMember speaker, TalkIntent t)
    {
        _talks.Remove(t);
        var l = P(t.Listener);
        if (l == null || l.Dead) return null;
        return Hold(t.Kind, speaker, l, t.Ref, t.Third);
    }

    // ───────────────────────────── 카드 ─────────────────────────────

    /// <summary>대화 카드를 펼치고 · 말하는 사람이 고르고 · 판정하고 · 결과를 남긴다. roll을 주면 그 값으로 판정 (시험).</summary>
    public TalkCard Hold(CardKind kind, CrewMember speaker, CrewMember listener, int @ref = -1, int third = -1, float? roll = null)
    {
        var w = _w;
        var k = new TalkCard { Id = _nextCard++, Kind = kind, Tick = w.Tick, Speaker = speaker.Id, Listener = listener.Id, Third = third, RoomId = (speaker.Room ?? listener.Room)?.Id ?? -1, Ref = @ref };
        k.SpeakerFace = ZoomDetail.Face(w, speaker);
        k.ListenerFace = ZoomDetail.Face(w, listener);
        Build(k, speaker, listener);
        foreach (var o in k.Options) if (o.Open) Score(k, o, speaker, listener);
        Choose(k, speaker);
        var pick = k.Pick!;
        k.Roll = roll ?? R.Float();
        k.Success = k.Roll < pick.Chance;
        Stats.Cards++;
        if (k.Success) Stats.CardWins++; else Stats.CardLosses++;
        speaker.Say(w, Persona.Say(speaker, pick.Say));
        Resolve(k, speaker, listener, pick);
        k.ListenerAfter = k.Success ? (kind == CardKind.Calm ? FaceLook.Calm : FaceLook.Smile) : kind is CardKind.Confess or CardKind.Heart ? FaceLook.Sad : kind == CardKind.Calm ? FaceLook.Fear : FaceLook.Angry;
        if (k.Answer != "") listener.Say(w, Persona.Say(listener, k.Answer));
        if (!k.Success && k.Branch != "") Stats.CardBranches++;
        Cards.Add(k);
        if (Cards.Count > 80) Cards.RemoveAt(0);
        string line = $"{speaker.Name} → {listener.Name} · {KindName(kind)}: “{pick.Text}” ({pick.Chance * 100:0}%) — {k.Outcome}" + (k.Branch != "" ? $" · {k.Branch}" : "");
        w.Log.Add(w.Tick, LogKind.Life, line, speaker.Id);
        Life.Diary(w, speaker, Persona.Say(speaker, $"{listener.Name}에게 {pick.Text}. {k.Outcome}."));
        Life.Diary(w, listener, Persona.Say(listener, $"{Ko.IGa(speaker.Name)} 찾아왔다 — {k.Outcome}."));
        if (kind is CardKind.Persuade or CardKind.Accuse or CardKind.Confess or CardKind.Mend || k.Branch != "")
            w.History.Add(w, k.Success ? HistoryKind.Bond : HistoryKind.Memory, $"{KindName(kind)} — {Ko.IGa(speaker.Name)} {listener.Name}에게 “{pick.Text}” · {k.Outcome}", speaker.Room, new[] { speaker, listener });
        return k;
    }

    private void Build(TalkCard k, CrewMember s, CrewMember l)
    {
        var w = _w;
        float aff = MathF.Min(l.AffinityTo(s), s.AffinityTo(l));
        bool deep = aff >= 0.5f;
        bool shared = w.Relations.Of(l, s).Any(m => m.Reason is RelationReason.SharedHardship or RelationReason.SavedMe or RelationReason.NursedMe or RelationReason.Comforted);
        bool sameHome = RootsOf(s).Home == RootsOf(l).Home;
        bool joker = s.Habits.Contains(Habit.Joker) || s.Habits.Contains(Habit.Cheerful) || s.Habits.Contains(Habit.Prankster);
        bool leader = s.Habits.Contains(Habit.Leader) || s.Id == w.Command.CaptainId;
        var vo = w.Values.Peek(s);
        float rule = vo?.V[(int)Axis.Rule] ?? (s.Value == CrewValue.Rules ? 0.4f : 0f);
        float mercy = vo?.V[(int)Axis.Mercy] ?? (s.Value == CrewValue.People ? 0.4f : 0f);
        void Add(string key, string text, string say, float b, bool open, string lockWhy, float pull, string pullWhy)
            => k.Options.Add(new TalkOption { Key = key, Text = text, Say = say, Base = b, Open = open, Lock = open ? "" : lockWhy, Pull = pull, PullWhy = pullWhy });
        string home = HomeShort[RootsOf(s).Home];
        switch (k.Kind)
        {
            case CardKind.Persuade:
            {
                var cap = w.Command.Captain;
                k.Title = "반란을 말리러";
                k.Scene = $"{Ko.IGa(l.Name)} 몰래 사람을 모은다는 걸 알았다 — 함교 열쇠 이야기까지 나왔다";
                k.Opening = Persona.Say(s, $"{l.Name}, 잠깐 이야기 좀 해");
                Add("bond", "옛정에 기댄다", "우리 사이에 이러지 마. 너 이런 사람 아니잖아", 0.5f, deep, "둘 사이가 깊어야 꺼낼 수 있다", mercy * 0.3f + (s.Value == CrewValue.People ? 0.2f : 0f), "정에 약하다");
                Add("shared", "같이 버틴 날을 꺼낸다", "그날 같이 버텼잖아. 이번에도 같이 버티자", 0.4f, shared, "같이 겪은 일이 있어야", 0.1f, "겪은 일을 잊지 않는다");
                Add("home", $"{home} 사람끼리 말한다", $"{home}에선 배를 뒤엎는 사람을 뭐라고 부르는지 알지?", 0.35f, sameHome, "같은 고향이어야", 0.1f, "고향 사람에게 약하다");
                Add("council", "회의에 올리자고 권한다", $"{(cap != null ? cap.Name + " 함장" : "함장")}한테 불만이 있으면 회의에서 말해. 내가 서명할게", 0.32f, true, "", rule * 0.3f + 0.05f, "절차를 믿는다");
                Add("reason", "배가 어떻게 될지 따진다", "열쇠를 뺏으면 그다음은? 이 배는 누가 몰아?", 0.22f, true, "", s.Traits.Diligence * 0.15f, "따지는 성격이다");
                Add("warn", "알리겠다고 못 박는다", "멈추지 않으면 함장에게 말할 거야", 0.3f, rule > 0.15f || s.Value == CrewValue.Rules, "규칙을 앞세우는 사람만", rule * 0.4f, "규칙이 먼저다");
                break;
            }
            case CardKind.Calm:
                k.Title = "공황이 왔다";
                k.Scene = $"{Ko.IGa(l.Name)} 숨을 못 쉬고 떨고 있다 — {l.Room?.Name ?? "복도"}";
                k.Opening = Persona.Say(s, $"{l.Name}, 나 봐. 여기 있어");
                Add("breath", "숨을 같이 쉰다", "넷 세고 들이쉬고, 넷 세고 내쉬어", 0.42f, true, "", s.Traits.Calm * 0.25f, "침착하다");
                Add("hand", "이름을 부르며 손을 잡는다", "내 손 잡아. 아무 데도 안 가", 0.55f, aff >= 0.4f, "가까운 사이여야", mercy * 0.25f + 0.1f, "곁에 있어 주는 사람이다");
                Add("task", "할 일을 쥐여 준다", "이 손전등 들고 나 따라와. 할 수 있어", 0.38f, leader || s.Role == l.Role, "앞장서는 사람이거나 같은 일을 하는 사람만", leader ? 0.25f : 0.05f, "앞장서는 성격이다");
                Add("joke", "농담으로 숨통을 튼다", "야, 너 지금 얼굴 진짜 웃겨. 사진 찍어 둘까?", 0.32f, joker, "농담이 몸에 밴 사람만", 0.35f, "농담부터 나온다");
                Add("med", "진정제를 놓는다", "따끔해. 금방 가라앉을 거야", 0.72f, s.Role == CrewRole.Medic || s.Quals.Contains(Qual.Medic), "의무 자격이 있어야", 0.05f, "손이 먼저 움직인다");
                break;
            case CardKind.Mediate:
            {
                var b = P(k.Third);
                k.Title = "둘 사이를 풀러";
                k.Scene = $"{Ko.WaGwa(l.Name)} {b?.Name ?? "누군가"} 며칠째 서로 말을 안 한다";
                k.Opening = Persona.Say(s, "둘 다 앉아 봐. 차 한 잔씩 하자");
                bool sideOk = b != null && MathF.Abs(s.AffinityTo(l) - s.AffinityTo(b)) > 0.3f;
                Add("listen", "둘 다 앉혀 놓고 들어 준다", "한 사람씩 말해. 끝까지 들을게", 0.4f, true, "", mercy * 0.25f + s.Traits.Sociability * 0.1f, "들어 주는 사람이다");
                Add("judge", "잘잘못을 가린다", "기록 보면 누가 먼저였는지 나와. 같이 보자", 0.34f, leader || rule > 0.25f, "앞장서거나 규칙을 앞세우는 사람만", rule * 0.3f, "옳고 그름이 먼저다");
                Add("side", "한쪽 편을 든다", $"솔직히 이번엔 {l.Name} 말이 맞아", 0.3f, sideOk, "한쪽과 훨씬 가까워야", 0.12f, "가까운 쪽 마음이 먼저 보인다");
                Add("task", "같이 할 일을 맡긴다", "내일 배관 점검 둘이 같이 해. 말은 안 해도 돼", 0.38f, leader || b != null && l.Role == b.Role, "앞장서거나 둘이 같은 일을 할 때만", leader ? 0.2f : 0f, "일로 푸는 사람이다");
                Add("joke", "웃겨서 풀어 버린다", "둘이 그렇게 노려보면 배 온도가 떨어져", 0.3f, joker, "농담이 몸에 밴 사람만", 0.35f, "농담부터 나온다");
                break;
            }
            case CardKind.Accuse:
            {
                var sc = w.Schemes.Get(k.Ref);
                bool saw = sc != null && sc.Knows.TryGetValue(s.Id, out var kh) && kh == KnowHow.Saw;
                k.Title = "범인을 찾아서";
                k.Scene = $"{sc?.Spec.Legit ?? "누가 장난을 쳤다"} — {Ko.IGa(s.Name)} {Ko.EulReul(l.Name)} 의심한다";
                k.Opening = Persona.Say(s, $"{l.Name}, 너지?");
                Add("evidence", "본 것을 들이민다", "그때 거기서 너 봤어. 발뺌하지 마", 0.65f, saw, "직접 본 것이 있어야", 0.2f, "본 대로 말한다");
                Add("bluff", "다 안다는 듯 떠본다", "다 들었어. 네 입으로 듣고 싶을 뿐이야", 0.33f, true, "", joker ? 0.2f : 0.05f, "넘겨짚기를 잘한다");
                Add("friend", "친구로서 묻는다", "화 안 낼게. 그냥 솔직하게만 말해 줘", 0.5f, aff >= 0.45f, "가까운 사이여야", mercy * 0.3f + 0.05f, "사람을 먼저 믿는다");
                Add("public", "메신저에 올리겠다고 한다", "말 안 하면 단체방에 올린다", 0.38f, mercy < 0f || s.Habits.Contains(Habit.ShortTempered), "모진 말을 할 수 있는 사람만", 0.3f, "참을성이 없다");
                break;
            }
            case CardKind.Confess:
            {
                bool writer = s.Hobbies.Contains(Hobby.Writing) || s.Hobbies.Contains(Hobby.Reading) || s.Habits.Contains(Habit.Bookworm);
                bool stars = s.Hobbies.Contains(Hobby.Stargazing) || w.Ship.Rooms.Any(r => r.Kind == RoomType.Observatory);
                k.Title = "마음을 전하러";
                k.Scene = $"{Ko.IGa(s.Name)} 며칠째 {l.Name} 곁을 맴돌았다";
                k.Opening = Persona.Say(s, $"{l.Name}, 할 말이 있어");
                Add("plain", "솔직하게 말한다", "너 좋아해. 꽤 오래됐어", 0.42f, true, "", s.Traits.Bravery * 0.25f, "에두르지 못한다");
                Add("letter", "편지를 건넨다", "말로는 못 하겠어서… 이거 읽어 줘", 0.48f, writer, "글을 쓰는 사람만", 0.3f, "말보다 글이 편하다");
                Add("stars", "창가에서 별을 보며", "저 별 보여? 너랑 같이 보고 싶었어", 0.52f, stars, "별을 볼 자리가 있어야", 0.2f, "분위기를 아는 사람이다");
                Add("joke", "농담처럼 떠본다", "우리 둘이 사귀면 웃기겠지? …웃기지?", 0.3f, joker, "농담이 몸에 밴 사람만", 0.35f, "진지한 말을 못 한다");
                Add("shared", "같이 버틴 날을 꺼낸다", "그날 네가 옆에 있어서 버텼어. 그때부터야", 0.55f, shared, "같이 겪은 일이 있어야", 0.15f, "그날을 잊지 못한다");
                break;
            }
            case CardKind.Heart:
            {
                var a = Arcs.FirstOrDefault(x => x.Id == k.Ref);
                k.Title = "속을 털어놓으러";
                k.Scene = a != null ? $"{a.Title} — 혼자 안고 있기엔 무거워졌다" : "혼자 안고 있기엔 무거워졌다";
                k.Opening = Persona.Say(s, $"{l.Name}, 시간 있어? 아무한테도 말 안 한 건데");
                Add("all", "다 털어놓는다", "사실은… 처음부터 다 말할게", 0.48f, true, "", s.Traits.Sociability * 0.2f, "숨기는 게 더 힘들다");
                Add("half", "반만 말한다", "별일은 아닌데, 요즘 좀 그래", 0.66f, true, "", (1f - s.Traits.Sociability) * 0.3f, "속을 다 내보이지 않는다");
                Add("help", "도와 달라고 부탁한다", "네가 좀 도와줄 수 있을까. 너밖에 없어", 0.5f, deep, "둘 사이가 깊어야", 0.15f, "기댈 줄 안다");
                Add("joke", "웃으며 흘린다", "웃기지? 내 인생이 이래", 0.3f, joker || s.Habits.Contains(Habit.Optimist), "웃어넘기는 사람만", 0.3f, "무거운 말도 웃으며 한다");
                break;
            }
            case CardKind.Mend:
            {
                var lv = Loves.FirstOrDefault(x => x.Id == k.Ref);
                bool jealous = lv != null && lv.Rival >= 0;
                k.Title = "둘 사이의 고비";
                k.Scene = lv?.CrisisWhy ?? "요즘 둘 사이가 서먹하다";
                k.Opening = Persona.Say(s, "우리 얘기 좀 해");
                Add("sorry", "먼저 사과한다", "내가 미안해. 내가 먼저 말했어야 했어", 0.48f, true, "", mercy * 0.3f + (s.Habits.Contains(Habit.Patient) ? 0.2f : 0f), "먼저 숙이는 사람이다");
                Add("vent", "서운했던 걸 다 말한다", "나도 할 말 있어. 그동안 서운했던 거", 0.36f, true, "", s.Habits.Contains(Habit.ShortTempered) ? 0.4f : 0.05f, "참았던 말이 터진다");
                Add("time", "둘만의 시간을 만든다", "오늘 밤 전망창 앞에서 둘이만 있자", 0.54f, aff >= 0.55f, "아직 마음이 깊어야", 0.15f, "말보다 함께 있는 걸 택한다");
                Add("jealous", "질투를 인정한다", $"{Rang(lv != null && lv.Rival >= 0 ? Name(lv.Rival) : "그 사람")} 웃는 거 보고 질투 났어", 0.5f, jealous, "질투가 끼어 있어야", 0.1f, "솔직하다");
                Add("end", "그만하자고 한다", "우리 여기까지 하자", 0.9f, aff < 0.3f || s.Needs.Stress > 0.7f, "마음이 식었거나 지쳤을 때만", 0.45f, "지쳤다");
                break;
            }
        }
    }

    /// <summary>가능성 = 바탕 + 솜씨 + 관계 + 기분 + 상황 (근거마다 숫자로).</summary>
    private void Score(TalkCard k, TalkOption o, CrewMember s, CrewMember l)
    {
        var w = _w;
        float skill = (s.Traits.Sociability - 0.5f) * 0.25f + (s.Quals.Contains(Qual.Counseling) ? 0.12f : 0f);
        switch (k.Kind)
        {
            case CardKind.Calm: skill += s.SkillLevel(Skill.Medicine) * 0.12f + (s.Traits.Calm - 0.5f) * 0.2f; break;
            case CardKind.Persuade: skill += (s.Habits.Contains(Habit.Leader) ? 0.06f : 0f) + (s.Background is Background.Lawyer or Background.Psychologist or Background.Teacher ? 0.08f : 0f); break;
            case CardKind.Accuse: skill += s.Background is Background.Police or Background.Lawyer or Background.Reporter ? 0.1f : 0f; break;
        }
        float rel = l.AffinityTo(s) * 0.35f + w.Relations.Trust(l, s) * 0.1f;
        var e = w.Brain2.Emotions.Of(l);
        float mood = -MathF.Max(e[Feeling.Anger], l.Mind.Anger) * 0.22f - l.Needs.Stress * 0.12f + e[Feeling.Joy] * 0.1f;
        if (k.Kind == CardKind.Calm) mood -= e[Feeling.Fear] * 0.12f;
        float sit = (w.Society.Morale - 0.5f) * 0.15f - (Crisis.Acting(w) ? 0.08f : 0f);
        if (k.Kind == CardKind.Persuade) sit += (w.Values.Peek(l)?.Captain ?? 0f) * 0.15f;
        if (k.Kind == CardKind.Confess && (l.Partner is int || Loves.Any(x => x.Has(l.Id) && x.Together && !x.Has(s.Id)))) sit -= 0.3f;
        if (k.Kind == CardKind.Accuse && w.Schemes.Get(k.Ref) is Scheme sc && sc.Lead != l.Id && !sc.Crew.Contains(l.Id) && o.Key != "evidence") sit -= 0.05f;
        o.Why.Add(("솜씨", skill)); o.Why.Add(("관계", rel)); o.Why.Add(("기분", mood)); o.Why.Add(("상황", sit));
        o.Chance = Math.Clamp(o.Base + skill + rel + mood + sit, 0.05f, 0.95f);
    }

    /// <summary>말하는 사람이 고른다: 될 것 같은 정도 + 성격이 끌리는 정도.</summary>
    private void Choose(TalkCard k, CrewMember s)
    {
        int best = -1; float bv = float.MinValue; int likely = -1; float lc = -1f;
        for (int i = 0; i < k.Options.Count; i++)
        {
            var o = k.Options[i];
            if (!o.Open) continue;
            if (o.Chance > lc) { lc = o.Chance; likely = i; }
            float v = o.Chance + o.Pull * 0.4f + R.Float() * 0.03f;
            if (v > bv) { bv = v; best = i; }
        }
        k.Chosen = best;
        var p = k.Options[best];
        k.ChoseWhy = best == likely ? (p.Pull > 0.15f ? $"될 것 같았고, {p.PullWhy}" : "제일 될 것 같았다") : $"성격대로 — {p.PullWhy}";
    }

    // ───────────────────────────── 결과 ─────────────────────────────

    private void Resolve(TalkCard k, CrewMember s, CrewMember l, TalkOption o)
    {
        var w = _w;
        var em = w.Brain2.Emotions;
        bool ok = k.Success;
        switch (k.Kind)
        {
            case CardKind.Persuade:
            {
                var sc = w.Schemes.Get(k.Ref);
                if (ok)
                {
                    if (sc != null && sc.Active)
                    {
                        sc.Stage = SchemeStage.Dropped; sc.Outcome = $"{s.Name}의 말에 마음을 돌렸다"; sc.Ended = w.Tick; w.Schemes.Stats.Dropped++;
                        foreach (int id in sc.Crew) if (P(id) is CrewMember m && m != l) Life.Diary(w, m, Persona.Say(m, $"{Ko.IGa(l.Name)} 손을 뗐다. 우리도 접는다."));
                    }
                    l.Mind.Anger = MathF.Max(0f, l.Mind.Anger - 0.3f);
                    em.Feel(l, Feeling.Shame, 0.15f, "반란을 꾸몄던 일", s);
                    w.Relations.Remember(l, s, RelationReason.Comforted, "돌이킬 수 없는 일을 막아 줬다");
                    l.ChangeAffinity(s, 0.1f);
                    var vo = w.Values.Of(l); vo.Loyalty = MathF.Min(1f, vo.Loyalty + 0.15f); vo.Captain = MathF.Min(1f, vo.Captain + 0.1f);
                    k.Answer = o.Key == "bond" ? "…알았어. 너 때문에 접는다" : o.Key == "council" ? "좋아. 회의에서 말할게. 서명해 줘" : "알았어. 없던 일로 하자";
                    k.Outcome = "모의를 접었다";
                    if (o.Key == "council" && !MotionSystem.Off && w.Command.Captain is CrewMember cap && cap != l)
                    {
                        var m = w.Motions.Propose(l, MotionKind.Grievance, SittingKind.Regular, $"{cap.Name} 함장에게 할 말", "몰래 모이는 대신 회의에서 말하기로 했다");
                        w.Motions.Cosign(m, s.Id);
                        k.Outcome = "모의를 접고 회의에 올리기로 했다";
                        Stats.Agendas++;
                    }
                }
                else
                {
                    k.Answer = "넌 몰라. 이번엔 안 멈춰";
                    k.Outcome = "말이 닿지 않았다";
                    var vo = w.Values.Peek(s);
                    bool rules = o.Key == "warn" || (vo?.V[(int)Axis.Rule] ?? 0f) > 0.15f || s.Value == CrewValue.Rules;
                    if (rules && w.Command.Captain is CrewMember cap && cap != s)
                    {
                        if (sc != null) { sc.Knows[cap.Id] = KnowHow.Told; sc.Knows[s.Id] = KnowHow.Told; }
                        w.Relations.Remember(l, s, RelationReason.ToldOnMe, "함장에게 일러바쳤다");
                        l.ChangeAffinity(s, -0.25f);
                        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(s.Name)} 함장 {cap.Name}에게 갔다 — “{l.Name} 쪽에서 무슨 일을 꾸미고 있습니다”", s.Id);
                        k.Branch = "함장에게 알렸다 — 모의가 드러났다";
                    }
                    else if (MathF.Min(l.AffinityTo(s), s.AffinityTo(l)) >= 0.5f)
                    {
                        l.ChangeAffinity(s, -0.2f); s.ChangeAffinity(l, -0.15f);
                        em.Feel(s, Feeling.Sadness, 0.3f, $"{Ko.WaGwa(l.Name)} 금이 갔다", l);
                        k.Branch = "오랜 사이에 금이 갔다";
                    }
                    else
                    {
                        if (sc != null && sc.Active) sc.Progress = MathF.Min(0.95f, sc.Progress + 0.35f);
                        em.Feel(l, Feeling.Fear, 0.2f, "들켰다", s);
                        k.Branch = "들켰다는 생각에 모의를 서두른다";
                    }
                }
                break;
            }
            case CardKind.Calm:
                if (ok)
                {
                    l.Mind.PanicUntil = w.Tick;
                    em.Feel(l, Feeling.Fear, -0.25f, "곁에서 붙잡아 줬다", s);
                    w.Relations.Remember(l, s, RelationReason.Comforted, "공황이 왔을 때 곁에서 붙잡아 줬다");
                    l.ChangeAffinity(s, 0.08f);
                    em.Feel(s, Feeling.Pride, 0.15f, $"{Ko.EulReul(l.Name)} 진정시켰다", l);
                    k.Answer = "…후. 고마워. 이제 좀 숨이 쉬어져";
                    k.Outcome = "숨이 돌아왔다";
                }
                else
                {
                    l.Mind.PanicUntil += SimTime.Minutes(3);
                    em.Feel(s, Feeling.Fear, 0.12f, $"{l.Name}의 공황", l);
                    k.Answer = "저리 가! 아무것도 안 들려!";
                    k.Outcome = "더 겁에 질렸다";
                    var other = w.Crew.FirstOrDefault(o2 => o2 != s && o2 != l && Adult(o2) && o2.CanAct && o2.Room == l.Room && (o2.Role == CrewRole.Medic || o2.Quals.Contains(Qual.Counseling)));
                    k.Branch = other != null ? $"{Ko.IGa(other.Name)} 대신 나섰다" : "혼자 가라앉을 때까지 곁을 지켰다";
                    if (other != null) { l.Mind.PanicUntil = w.Tick + SimTime.Minutes(1); w.Relations.Remember(l, other, RelationReason.Comforted, "공황이 왔을 때 진정시켜 줬다"); }
                }
                break;
            case CardKind.Mediate:
            {
                var b = P(k.Third);
                if (ok && b != null)
                {
                    l.ChangeAffinity(b, 0.25f); b.ChangeAffinity(l, 0.25f);
                    w.Relations.Remember(l, b, RelationReason.Apologized, "중재 자리에서 먼저 손을 내밀었다");
                    w.Relations.Remember(b, s, RelationReason.Comforted, "둘 사이를 풀어 줬다");
                    l.ChangeAffinity(s, 0.05f); b.ChangeAffinity(s, 0.05f);
                    Stats.Makeups++;
                    k.Answer = $"…{b.Name}, 나도 심했어";
                    k.Outcome = "둘이 다시 말을 텄다";
                }
                else if (b != null)
                {
                    l.ChangeAffinity(b, -0.1f); b.ChangeAffinity(l, -0.1f);
                    k.Answer = "됐어. 쟤랑은 말 안 해";
                    k.Outcome = "더 틀어졌다";
                    if (o.Key == "side") { b.ChangeAffinity(s, -0.2f); w.Relations.Remember(b, s, RelationReason.BlamedMe, "중재한다더니 한쪽 편만 들었다"); k.Branch = $"{Ko.IGa(b.Name)} 중재한 사람까지 미워하게 됐다"; }
                    else { w.Brain2.Goals.Push(l, "story:avoid", $"{b.Name} 피하기", "근무 조를 바꿔 달라고 하고 싶다", ActCat.Rest, 36f, 0.7f); k.Branch = $"{Ko.IGa(l.Name)} 근무 조를 바꿔 달라고 했다"; }
                }
                break;
            }
            case CardKind.Accuse:
            {
                var sc = w.Schemes.Get(k.Ref);
                bool guilty = sc != null && (sc.Lead == l.Id || sc.Crew.Contains(l.Id));
                if (ok && guilty)
                {
                    sc!.Identified = true; sc.Knows[s.Id] = KnowHow.Saw;
                    w.Relations.Remember(s, l, RelationReason.OwnedUp, "추궁하자 털어놨다");
                    em.Feel(l, Feeling.Shame, 0.25f, "들켰다", s);
                    k.Answer = "…맞아, 나야. 미안해";
                    k.Outcome = "털어놨다";
                }
                else if (ok)
                {
                    w.Relations.Remember(l, s, RelationReason.ClearedMyName, "의심했지만 내 말을 믿어 줬다");
                    k.Answer = "진짜 아니야. 나도 당했으면 화났을 거야";
                    k.Outcome = "아니라는 말을 믿었다";
                }
                else if (guilty)
                {
                    s.ChangeAffinity(l, -0.1f);
                    k.Answer = "무슨 소리야. 증거 있어?";
                    k.Outcome = "끝까지 잡아뗐다";
                    w.Info.Chat.Post(s, ChatKind.Ask, ShipChat.Voice(s, "그 장난 친 사람, 본 사람 있으면 말해 줘", "그 일 보신 분 계시면 알려 주세요"));
                    k.Branch = "단체방에 물었다 — 소문이 돈다";
                }
                else
                {
                    l.ChangeAffinity(s, -0.22f);
                    w.Relations.Remember(l, s, RelationReason.FramedMe, "나를 범인으로 몰았다");
                    em.Feel(l, Feeling.Anger, 0.3f, "누명", s);
                    k.Answer = "나 아니라고! 사람을 뭘로 보고";
                    k.Outcome = "억울한 사람을 몰았다";
                    k.Branch = "누명 — 둘 사이에 앙금이 남았다";
                }
                break;
            }
            case CardKind.Heart:
            {
                var a = Arcs.FirstOrDefault(x => x.Id == k.Ref);
                if (ok)
                {
                    if (a != null) { a.Confided = true; a.Friend = l.Id; a.Support += o.Key == "help" ? 0.35f : o.Key == "half" ? 0.1f : 0.2f; }
                    w.Relations.Remember(s, l, RelationReason.KeptMySecret, a != null ? $"{a.Title} — 들어 줬다" : "속 이야기를 들어 줬다");
                    s.ChangeAffinity(l, 0.06f); l.ChangeAffinity(s, 0.05f);
                    s.Needs.Stress = MathF.Max(0f, s.Needs.Stress - 0.1f);
                    k.Answer = o.Key == "help" ? "당연하지. 같이 방법 찾아 보자" : "말해 줘서 고마워. 혼자 안고 있지 마";
                    k.Outcome = "마음이 조금 가벼워졌다";
                }
                else
                {
                    k.Answer = "에이, 다들 그 정도는 있어";
                    k.Outcome = "가볍게 넘겨졌다";
                    if (a != null) { a.Confided = true; a.Friend = -1; a.Support -= 0.1f; }
                    if (l.Habits.Contains(Habit.Talker))
                    {
                        w.Relations.Remember(s, l, RelationReason.ToldOnMe, "내 이야기를 여기저기 했다");
                        em.Feel(s, Feeling.Shame, 0.2f, "말이 새어 나갔다", l);
                        k.Branch = "말이 새어 나갔다 — 식당에서 수군거린다";
                        Stats.Gossip++;
                    }
                    else k.Branch = "다시는 말하지 않기로 했다";
                }
                break;
            }
            case CardKind.Confess:
                OnConfess(k, s, l, o);
                break;
            case CardKind.Mend:
                OnMend(k, s, l, o);
                break;
        }
    }
}
