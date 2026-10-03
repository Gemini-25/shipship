using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.1 선외 인간관계 — 고향의 가족 · 친구 · 옛 동료와 주고받는 편지.
//   편지는 집에서 쓰여 몇 시간 늦게(중계 · 빛 지연) 닿고, 통신 콘솔이 돌고 폭풍이 잦아야 들어온다 —
//   끊긴 동안 온 편지는 중계국에 쌓였다가 통신이 돌아오면 한꺼번에 들어온다. 나가는 답장도 마찬가지로 쌓인다.
//   소식이 기분(기쁨 · 걱정 · 슬픔 · 부끄러움) · 일기 · 입에 오르는 말이 된다: 결혼 · 출산 소식은 곁의 사람에게 자랑하고,
//   아프다는 편지엔 걱정하고, 부고엔 무너진다. 답장을 미루는 사람(게으름 · 미루는 버릇)은 재촉 편지를 받고,
//   답장 못 한 사람의 부고가 오면 오래 후회한다. 소포가 오면 맞바꿀 거리(초콜릿 · 커피)가 생긴다.
//   주 컴퓨터는 통신이 끊겨 편지가 못 오가는 걸 알리고, 돌아오면 밀린 편지를 나눠 주며, 부고를 받은 사람의 근무를 덜어 주자고 함장에게 올린다.

public enum News : byte { Hello, Wedding, Birth, Death, Ill, Nag, Parcel }

public sealed class Contact
{
    public int Id { get; init; }
    public int Crew { get; init; }
    public string Name { get; init; } = "";
    public string Rel { get; init; } = "";
    public bool Kin { get; init; }
    public bool Alive { get; set; } = true;
    /// <summary>답장 못 한 편지 수 · 그 첫 편지를 받은 때.</summary>
    public int Owed { get; set; }
    public long OwedSince { get; set; } = -1;
    public bool Replied { get; set; }
    public long Next { get; set; }
    public string Who => $"{Rel} {Name}";
}

public sealed class Letter
{
    public int Id { get; init; }
    public int Crew { get; init; }
    public int Contact { get; init; }
    public News Kind { get; init; }
    public bool Out { get; init; }
    public bool Thanks { get; init; }
    public long Sent { get; init; }
    public long Due { get; init; }
    /// <summary>배에 들어온 때 (나가는 편지면 떠난 때) · 읽은 때.</summary>
    public long Got { get; set; } = -1;
    public long Read { get; set; } = -1;
    public bool Batch { get; set; }
}

public sealed class MailStats
{
    public int Written, Arrived, Read, Batches, BatchLetters, Replies, Sent, Queued, Deaths, Weddings, Births, Ills, Nags, Parcels, Regrets, Bragged, Comforted, ComputerOut, ComputerBack, ComputerEase;
    public int MaxBatch;
    public string Summary() =>
        $"쓴 편지 {Written} · 들어옴 {Arrived} · 읽음 {Read} · 한꺼번에 {Batches}번({BatchLetters}통 · 최대 {MaxBatch}) · 답장 {Replies} (떠남 {Sent} · 쌓임 {Queued}) · " +
        $"부고 {Deaths} · 결혼 {Weddings} · 출산 {Births} · 병 {Ills} · 재촉 {Nags} · 소포 {Parcels} · 후회 {Regrets} · 자랑 {Bragged} · 위로 {Comforted} · " +
        $"컴퓨터: 끊김 알림 {ComputerOut} · 밀린 편지 {ComputerBack} · 근무 덜기 {ComputerEase}";
}

public sealed class MailSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6967 + 1231));
    private long _nextTen, _nextHour, _toldOut = -1_000_000;
    private int _nextId = 1, _nextContact = 1;
    private bool _seeded;
    private readonly SortedDictionary<int, (News kind, long until)> _reading = new();

    public List<Contact> Contacts { get; } = new();
    public List<Letter> Letters { get; } = new();
    public MailStats Stats { get; } = new();
    /// <summary>중계국 고장 · 전파 방해 (시험 · 사고가 건다).</summary>
    public bool Jam { get; set; }

    public MailSystem(World w) => _w = w;

    public bool CommsUp => !Jam && _w.Comms.Console != null && _w.Ambience.StormPower < 0.55f;
    public IEnumerable<Contact> Of(CrewMember c) => Contacts.Where(k => k.Crew == c.Id);
    public Contact? ContactById(int id) { foreach (var k in Contacts) if (k.Id == id) return k; return null; }
    /// <summary>화면: 지금 편지를 읽고 있다 (무슨 소식인지 · 봉투 모양이 다르다).</summary>
    public News? ReadingNow(CrewMember c) => _reading.TryGetValue(c.Id, out var r) && r.until > _w.Tick ? r.kind : null;
    public int Waiting => Letters.Count(l => !l.Out && l.Got < 0 && l.Due <= _w.Tick);
    public int Outbox => Letters.Count(l => l.Out && l.Got < 0);

    private static readonly string[] KinRel = { "어머니", "아버지", "누나", "형", "언니", "오빠", "동생", "할머니", "딸", "아들", "삼촌", "이모" };
    private static readonly string[] FriendRel = { "옛 동료", "친구", "옛 선생님", "동창", "옛 함장" };
    private static readonly string[] Given = { "순자", "영호", "미경", "철수", "지연", "도윤", "하늘", "보람", "정희", "민수", "다은", "상철", "은비", "태호", "수아", "경민" };

    private void Seed()
    {
        if (_seeded) return;
        _seeded = true;
        foreach (var c in _w.Crew.OrderBy(c => c.Id))
            if (!c.IsChild) AddContacts(c);
    }

    private void AddContacts(CrewMember c)
    {
        int n = Life.Has(c, Habit.Loner) ? 1 : Life.Has(c, Habit.Homesick) || Life.Has(c, Habit.Talker) ? 3 : 2;
        for (int i = 0; i < n; i++)
        {
            bool kin = i == 0 || R.Chance(0.5f);
            Contacts.Add(new Contact
            {
                Id = _nextContact++, Crew = c.Id, Kin = kin,
                Rel = kin ? KinRel[R.Range(0, KinRel.Length)] : FriendRel[R.Range(0, FriendRel.Length)],
                Name = Given[R.Range(0, Given.Length)],
                Next = _w.Tick + SimTime.Hours(R.Range(4f, 110f)),
            });
        }
    }

    private string Home(CrewMember c) => StorySystem.HomeShort[Math.Clamp(_w.Tales.RootsOf(c).Home, 0, StorySystem.HomeShort.Length - 1)];
    private CrewMember? P(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick >= _nextTen) { _nextTen = w.Tick + SimTime.Minutes(10); Seed(); Exchange(); ReadMail(); }
        if (w.Tick >= _nextHour) { _nextHour = w.Tick + SimTime.Hours(1); Homes(); }
    }

    // ───────────────────────────── 집에서 쓰는 편지 ─────────────────────────────

    private void Homes()
    {
        var w = _w;
        foreach (var k in Contacts)
        {
            if (!k.Alive || w.Tick < k.Next || P(k.Crew) is not { Dead: false } c) continue;
            k.Next = w.Tick + SimTime.Hours(R.Range(60f, 150f));
            News kind;
            if (k.Owed >= 2 && R.Chance(0.5f)) kind = News.Nag;
            else
            {
                float u = R.Float();
                kind = u < 0.62f ? News.Hello : u < 0.72f ? News.Ill : u < 0.79f ? News.Wedding : u < 0.86f ? News.Birth : u < 0.94f ? News.Parcel : k.Kin ? News.Death : News.Hello;
            }
            Post(c, k, kind);
        }
        // 나가는 편지가 쌓이고 통신이 죽었다 — 주 컴퓨터가 알린다 (반나절에 한 번)
        if (!CommsUp && (Outbox > 0 || Waiting > 0) && w.Automation.Present && w.Automation.MainOnline && w.Tick - _toldOut > SimTime.Hours(12))
        {
            _toldOut = w.Tick;
            Stats.ComputerOut++;
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 통신이 끊겨 편지가 오가지 못한다 — 나갈 답장 {Outbox}통이 쌓였고, 받을 편지도 중계국에 쌓이고 있을 것", -1);
        }
    }

    /// <summary>집에서 편지 한 통을 부친다 (중계 · 빛 지연으로 몇 시간 뒤 닿는다).</summary>
    public Letter Post(CrewMember c, Contact k, News kind, float delayHours = -1f)
    {
        var w = _w;
        var l = new Letter
        {
            Id = _nextId++, Crew = c.Id, Contact = k.Id, Kind = kind, Thanks = k.Replied && kind == News.Hello, Sent = w.Tick,
            Due = w.Tick + SimTime.Hours(delayHours >= 0f ? delayHours : R.Range(3f, 10f)),
        };
        if (kind == News.Death) k.Alive = false; // 집에선 이미 떠났다 — 배에선 편지가 닿아야 안다
        if (l.Thanks) k.Replied = false;
        Letters.Add(l);
        Stats.Written++;
        if (Letters.Count > 400) Letters.RemoveAll(x => x.Read >= 0 && x.Got >= 0 && w.Tick - x.Got > SimTime.TicksPerDay * 10);
        return l;
    }

    // ───────────────────────────── 오가기 (통신이 돌아야) ─────────────────────────────

    private void Exchange()
    {
        var w = _w;
        if (!CommsUp) return;
        var arrived = new SortedDictionary<int, int>();
        int total = 0;
        foreach (var l in Letters)
        {
            if (l.Got >= 0 || l.Due > w.Tick) continue;
            l.Got = w.Tick;
            if (l.Out) { Stats.Sent++; continue; }
            Stats.Arrived++;
            total++;
            arrived[l.Crew] = arrived.GetValueOrDefault(l.Crew) + 1;
        }
        if (total == 0) return;
        foreach (var (id, n) in arrived)
        {
            if (n < 2 || P(id) is not CrewMember c) continue;
            Stats.Batches++;
            Stats.BatchLetters += n;
            Stats.MaxBatch = Math.Max(Stats.MaxBatch, n);
            foreach (var l in Letters) if (l.Crew == id && !l.Out && l.Got == w.Tick) l.Batch = true;
            w.Log.Add(w.Tick, LogKind.Life, $"밀린 편지 {n}통이 한꺼번에 들어왔다", c.Id);
        }
        if (total >= 3 && w.Automation.Present && w.Automation.MainOnline && w.Tick - _toldOut < SimTime.TicksPerDay * 5)
        {
            Stats.ComputerBack++;
            _toldOut = -1_000_000;
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 통신이 돌아왔다 — 중계국에 밀려 있던 편지 {total}통을 각자에게 넘겼다", -1);
        }
    }

    private void ReadMail()
    {
        var w = _w;
        foreach (var l in Letters)
        {
            if (l.Out || l.Got < 0 || l.Read >= 0 || P(l.Crew) is not CrewMember c) continue;
            if (c.Dead) { l.Read = w.Tick; continue; }
            if (!c.IsAwake || c.Outside || c.Job?.Urgent == true) continue;
            if (ContactById(l.Contact) is not Contact k) { l.Read = w.Tick; continue; }
            l.Read = w.Tick;
            Stats.Read++;
            Read(c, k, l);
        }
    }

    private void Read(CrewMember c, Contact k, Letter l)
    {
        var w = _w;
        var emo = w.Brain2.Emotions;
        _reading[c.Id] = (l.Kind, w.Tick + SimTime.Minutes(l.Batch ? 40 : 15));
        string home = Home(c);
        if (l.Kind != News.Death)
        {
            if (k.Owed == 0) k.OwedSince = w.Tick;
            k.Owed++;
        }
        switch (l.Kind)
        {
            case News.Hello:
                emo.Feel(c, Feeling.Joy, l.Thanks ? 0.2f : 0.12f, $"{k.Who}의 편지");
                c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.1f);
                if (Life.Has(c, Habit.Homesick)) emo.Feel(c, Feeling.Sadness, 0.08f, $"{home} 생각");
                Life.Diary(w, c, Persona.Say(c, l.Thanks ? $"{Ko.IGa(k.Who)} 내 답장을 받고 기뻤다고 썼다" : $"{k.Who}에게서 편지. {home}엔 별일 없단다"));
                break;
            case News.Ill:
                Stats.Ills++;
                emo.Feel(c, Feeling.Fear, 0.25f, $"{k.Who}이 아프다");
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.1f);
                Life.Diary(w, c, Persona.Say(c, $"{Ko.IGa(k.Who)} 아프단다. 여기선 아무것도 해 줄 수가 없다"));
                Tell(c, $"{home}의 {Ko.IGa(k.Rel)} 아프대", false);
                break;
            case News.Wedding:
                Stats.Weddings++;
                emo.Feel(c, Feeling.Joy, 0.3f, $"{k.Who}의 결혼 소식");
                Life.Diary(w, c, Persona.Say(c, $"{Ko.IGa(k.Who)} 결혼한단다. 식장엔 못 가지만 축하 편지는 길게 써야지"));
                Tell(c, $"고향의 {Ko.IGa(k.Rel)} 결혼한대!", true);
                break;
            case News.Birth:
                Stats.Births++;
                emo.Feel(c, Feeling.Joy, 0.35f, $"{k.Who} 집에 아이가 태어났다");
                Life.Diary(w, c, Persona.Say(c, $"{k.Who} 집에 아이가 태어났다. 돌아가면 걸어 다니고 있겠지"));
                Tell(c, $"{k.Rel} 집에 아기가 태어났대", true);
                break;
            case News.Parcel:
                Stats.Parcels++;
                emo.Feel(c, Feeling.Joy, 0.15f, $"{k.Who}의 소포");
                var g = (Goods)R.Range(0, 3);
                w.Personal.Bets.Give(c, g, 2);
                w.Log.Add(w.Tick, LogKind.Life, $"{home}에서 소포가 왔다 — {WagerSystem.Name(g)} 두 개", c.Id);
                break;
            case News.Nag:
                Stats.Nags++;
                emo.Feel(c, Feeling.Shame, 0.25f, $"{k.Who}에게 답장을 미뤘다");
                c.Say(w, Persona.Say(c, "답장 좀 하라는 편지가 또 왔다"));
                Life.Diary(w, c, Persona.Say(c, $"{Ko.IGa(k.Who)} 왜 답이 없냐고 묻는다. 오늘은 꼭 써야지"));
                break;
            case News.Death:
                Stats.Deaths++;
                Grieve(c, k);
                break;
        }
    }

    /// <summary>부고: 슬픔 · 답장을 미뤘다면 후회 · 곁의 친구가 위로 · 주 컴퓨터가 근무를 덜어 주자고 올린다.</summary>
    private void Grieve(CrewMember c, Contact k)
    {
        var w = _w;
        var emo = w.Brain2.Emotions;
        emo.Feel(c, Feeling.Sadness, 0.6f, $"{k.Who}의 부고");
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.25f);
        w.Log.Add(w.Tick, LogKind.Life, $"{k.Who}의 부고가 편지로 왔다", c.Id);
        Life.Diary(w, c, Persona.Say(c, $"{k.Who}의 부고. 편지가 닿는 데만 몇 시간이 걸렸다 — 장례는 이미 끝났겠지"));
        MarkLog.Add(c.Memory.Marks, w.Tick, $"{k.Who}의 부고를 받았다");
        if (k.Owed > 0)
        {
            Stats.Regrets++;
            emo.Feel(c, Feeling.Shame, 0.4f, $"{k.Who}에게 답장을 미뤘다");
            Life.Diary(w, c, Persona.Say(c, $"{k.Who}에게 답장을 미루기만 했다. 이제 보낼 곳이 없다"));
            c.Say(w, Persona.Say(c, "답장… 쓴다 쓴다 하고 못 썼는데"));
        }
        k.Owed = 0;
        var friend = w.Crew.Where(o => !o.Dead && o != c && !o.IsChild && o.IsAwake && c.AffinityTo(o) > 0.1f).OrderByDescending(o => c.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
        if (friend != null)
        {
            Stats.Comforted++;
            friend.Say(w, Persona.Say(friend, "편지 받았다며. 오늘은 내가 옆에 있을게"));
            c.ChangeAffinity(friend, 0.06f);
            w.Relations.Remember(c, friend, RelationReason.Comforted, $"{k.Who}의 부고를 받은 날 곁에 있어 줬다");
        }
        if (w.Automation.Present && w.Automation.MainOnline)
        {
            Stats.ComputerEase++;
            c.ExcusedUntil = Math.Max(c.ExcusedUntil, w.Tick + SimTime.Hours(8));
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {c.Name}에게 고향의 부고가 왔다 — 오늘 근무를 덜어 주자고 함장에게 올렸다", c.Id);
        }
    }

    /// <summary>소식을 곁의 사람에게 말한다 (좋은 소식은 자랑 · 걱정은 털어놓기).</summary>
    private void Tell(CrewMember c, string text, bool good)
    {
        var w = _w;
        if (c.Room == null) return;
        var o = w.Crew.Where(x => !x.Dead && x != c && x.Room == c.Room && x.IsAwake && !x.IsChild).OrderByDescending(x => c.AffinityTo(x)).ThenBy(x => x.Id).FirstOrDefault();
        if (o == null) return;
        c.Say(w, Persona.Say(c, text));
        if (good) { Stats.Bragged++; o.Say(w, Persona.Say(o, "잘됐다! 축하해")); w.Brain2.Emotions.Feel(o, Feeling.Joy, 0.05f, $"{c.Name}의 고향 소식"); }
        else o.Say(w, Persona.Say(o, "많이 걱정되겠다"));
        c.ChangeAffinity(o, 0.02f);
        o.ChangeAffinity(c, 0.02f);
    }

    // ───────────────────────────── 답장 (LetterActivity가 부른다) ─────────────────────────────

    public Contact? MostOwed(CrewMember c) =>
        Contacts.Where(k => k.Crew == c.Id && k.Alive && k.Owed > 0).OrderByDescending(k => k.Owed).ThenBy(k => k.OwedSince).ThenBy(k => k.Id).FirstOrDefault();

    public void Reply(CrewMember c, Contact k)
    {
        var w = _w;
        var l = new Letter { Id = _nextId++, Crew = c.Id, Contact = k.Id, Kind = News.Hello, Out = true, Sent = w.Tick, Due = w.Tick };
        Letters.Add(l);
        Stats.Replies++;
        if (!CommsUp) Stats.Queued++;
        k.Owed = 0;
        k.OwedSince = -1;
        k.Replied = true;
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
        w.Log.Add(w.Tick, LogKind.Life, $"{k.Who}에게 답장을 썼다" + (CommsUp ? "" : " — 통신이 끊겨 보낼 편지함에 쌓였다"), c.Id);
        Life.Diary(w, c, Persona.Say(c, $"{k.Who}에게 답장을 썼다. {Home(c)} 이야기를 더 듣고 싶다고"));
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Contacts.Count); I(Letters.Count); I(_nextId); I(Stats.Read); I(Stats.Replies);
        foreach (var k in Contacts) { I(k.Id); I(k.Owed); I(k.Alive ? 1 : 0); I(k.Next); }
        foreach (var l in Letters) { I(l.Id); I(l.Got); I(l.Read); }
    }
}

/// <summary>v18.1 답장 쓰기: 쉬는 시간에 제 침대 곁에 앉아 쓴다 (미루는 버릇이면 자꾸 뒤로 · 오래 밀리면 마음이 쓰여 앉는다).</summary>
public sealed class LetterActivity : Activity
{
    public override string Id => "letter";
    public override string Label => "답장 쓰기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.IsChild || OnShift(c, w) || Bedtime(c, w) || c.Outside) return (0f, "—");
        var k = w.Personal.Mail.MostOwed(c);
        if (k == null) return (0f, "—");
        float h = Hour(w);
        if (h < 17f && h > 9f) return (0f, "—");
        float days = k.OwedSince < 0 ? 0f : (w.Tick - k.OwedSince) / (float)SimTime.TicksPerDay;
        float s = 0.18f + 0.05f * MathF.Min(3, k.Owed) + (Life.Has(c, Habit.Homesick) ? 0.1f : 0f) + (days > 4f ? 0.12f : 0f);
        if (Life.Has(c, Habit.Procrastinator)) s *= days > 6f ? 0.8f : 0.3f;
        return (s, $"{k.Who}에게 답장");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var k = w.Personal.Mail.MostOwed(c);
        if (k == null) return null;
        var toils = new List<Toil>();
        var bed = c.HomeBed ?? c.Bed;
        var spot = bed?.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).FirstOrDefault();
        if (spot is Cell at && bed!.Room.Type is not (RoomType.Medbay)) toils.Add(new GotoToil(at));
        toils.Add(new WaitToil(SimTime.Minutes(25), Pose.Sitting));
        toils.Add(new DoToil((cm, world) => { if (k.Alive && k.Owed > 0) world.Personal.Mail.Reply(cm, k); return true; }));
        return new Job(this, "답장 쓰기", toils) { LogText = $"{k.Who}에게 답장을 쓰러 간다", LogKind = LogKind.Life };
    }
}
