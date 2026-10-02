using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15 일상 사건 70: 사고가 아닌 날에도 배는 산다.
// 사건마다 "누가 · 왜(습관 · 취미 · 두려움 · 경력 · 관계) · 무엇이 바뀌었고(기분 · 관계 · 물건 · 방 · 설비) · 누가 기억하나(일기 · 관계 기억 · 기록)".
// 열 갈래 × 일곱: 식사 · 잠 · 물건 · 일 · 관계 · 몸 · 배 · 관행 · 여가 · 바깥.
// 위기 중엔 일어나지 않는다. 두세 시간에 하나꼴 (밤에는 잠 갈래만).

public enum DailyGroup { Meal, Sleep, Things, Work, Bond, Body, Ship, Custom, Leisure, Outside }

public sealed record DailySpec(string Id, string Name, DailyGroup Group, float Weight, Func<DailyCtx, bool> Run);

public sealed class DailyStats
{
    public int Fired;
    public readonly int[] ByGroup = new int[10];
    public readonly HashSet<string> Seen = new();
    public string Summary() => $"일상 사건 {Fired}번 · 겪은 종류 {Seen.Count}/{DailySystem.Catalog.Length} · " +
        string.Join(" ", Enum.GetValues<DailyGroup>().Select(g => $"{DailySystem.GroupName(g)} {ByGroup[(int)g]}"));
}

/// <summary>사건 하나를 꾸릴 때 쓰는 손잡이.</summary>
public sealed class DailyCtx
{
    public World W { get; }
    public Rng R { get; }
    public int Hour { get; }
    public bool Night => Hour < 6;
    public string? Text { get; set; }
    public List<CrewMember> Who { get; } = new();

    public DailyCtx(World w, Rng r) { W = w; R = r; Hour = (int)SimTime.HourOfDay(w.Tick); }

    private bool Free(CrewMember c, bool asleepOk) =>
        !c.Dead && !c.Down && !c.Outside && c.Room != null && c.Job?.Urgent != true && (asleepOk || c.IsAwake && c.CanAct);

    /// <summary>조건에 맞는 사람 하나 (깨어 있고 한가한).</summary>
    public CrewMember? One(Func<CrewMember, bool>? f = null, bool asleepOk = false, bool kids = false)
    {
        var list = W.Crew.Where(c => Free(c, asleepOk) && (kids || !c.IsChild) && (f == null || f(c))).ToList();
        return list.Count == 0 ? null : list[R.Range(0, list.Count)];
    }

    /// <summary>같은 방의 다른 사람 하나.</summary>
    public CrewMember? Near(CrewMember a, Func<CrewMember, bool>? f = null, bool asleepOk = false)
    {
        var list = W.Crew.Where(c => c != a && c.Room == a.Room && Free(c, asleepOk) && !c.IsChild && (f == null || f(c))).ToList();
        return list.Count == 0 ? null : list[R.Range(0, list.Count)];
    }

    /// <summary>배 어디의 다른 사람 하나.</summary>
    public CrewMember? Other(CrewMember a, Func<CrewMember, bool>? f = null) =>
        One(c => c != a && (f == null || f(c)));

    public List<CrewMember> InRoom(Room r) => W.Crew.Where(c => c.Room == r && Free(c, false)).ToList();

    public bool Has(CrewMember c, Habit h) => Life.Has(c, h);
    public bool Likes(CrewMember c, Hobby h) => c.Hobbies.Contains(h);
    public bool Was(CrewMember c, params Background[] b) => b.Contains(c.Background);

    public void Say(CrewMember c, string text) => c.Say(W, Persona.Say(c, text));
    public void Diary(CrewMember c, string text) => Life.Diary(W, c, Persona.Say(c, text));
    public void Mem(CrewMember who, CrewMember about, RelationReason r, string text) => W.Relations.Remember(who, about, r, text);
    public void Aff(CrewMember a, CrewMember b, float d) { a.ChangeAffinity(b, d); b.ChangeAffinity(a, d); }
    public void Stress(CrewMember c, float d) => c.Needs.Stress = Math.Clamp(c.Needs.Stress + d, 0f, 1f);
    public void Social(CrewMember c, float d) => c.Needs.Social = Math.Clamp(c.Needs.Social + d, 0f, 1f);
    public void Rest(CrewMember c, float d) => c.Needs.Rest = Math.Clamp(c.Needs.Rest + d, 0f, 1f);
    public void Food(CrewMember c, float d) => c.Needs.Food = Math.Clamp(c.Needs.Food + d, 0f, 1f);
    public void Mark(Room r, string text) => MarkLog.Add(r.Marks, W.Tick, text);

    /// <summary>사건을 남긴다 (기록 줄 · 관련된 사람).</summary>
    public bool Done(string text, params CrewMember[] who)
    {
        Text = text;
        Who.AddRange(who);
        W.Log.Add(W.Tick, LogKind.Life, text, who.Length > 0 ? who[0].Id : -1);
        return true;
    }

    /// <summary>오래 남을 일이면 배 기록에도.</summary>
    public bool Remembered(string text, params CrewMember[] who)
    {
        Text = text;
        Who.AddRange(who);
        W.History.Add(W, HistoryKind.Memory, text, who.FirstOrDefault()?.Room, who, log: true);
        return true;
    }

    public Room? RoomOf(params RoomType[] types) => W.Ship.LiveRooms.Where(r => types.Contains(r.Type) && !r.Abandoned).OrderBy(r => r.Id).FirstOrDefault();
}

public sealed class DailySystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 89));
    public DailyStats Stats { get; } = new();
    public List<(long tick, string id, string text)> Recent { get; } = new();
    private long _next = -1;
    /// <summary>시험용: 다음 사건을 당긴다.</summary>
    public bool Rush { get; set; }

    public DailySystem(World w) => _w = w;

    public static string GroupName(DailyGroup g) => g switch
    {
        DailyGroup.Meal => "식사", DailyGroup.Sleep => "잠", DailyGroup.Things => "물건", DailyGroup.Work => "일", DailyGroup.Bond => "관계",
        DailyGroup.Body => "몸", DailyGroup.Ship => "배", DailyGroup.Custom => "관행", DailyGroup.Leisure => "여가", _ => "바깥",
    };

    public void Update(float dt)
    {
        var w = _w;
        if (_next < 0) _next = w.Tick + SimTime.Hours(1f + 2f * R.Float());
        if (w.Tick < _next && !Rush) return;
        _next = w.Tick + SimTime.Hours(1.5f + 2.5f * R.Float());
        if (Crisis.Acting(w) || w.Crew.Count(c => !c.Dead) < 2) return;
        Fire(null);
    }

    /// <summary>뽑힐 몫: 최근 이틀 안에 일어난 건 덜 · 오늘 같은 갈래가 많았으면 덜 (같은 일이 되풀이되지 않게).</summary>
    private float Weigh(DailySpec s)
    {
        long now = _w.Tick;
        bool recent = Recent.Any(r => r.id == s.Id && now - r.tick < SimTime.TicksPerDay * 2);
        int sameGroup = Recent.Count(r => now - r.tick < SimTime.TicksPerDay && Catalog.First(c => c.Id == r.id).Group == s.Group);
        return s.Weight * (recent ? 0.15f : 1f) / (1f + sameGroup);
    }

    /// <summary>사건 하나를 일으킨다 (갈래나 종류를 정하면 그것만).</summary>
    public string? Fire(DailyGroup? group, string? id = null)
    {
        var w = _w;
        var ctx0 = new DailyCtx(w, R);
        var pool = Catalog.Where(s => (id == null || s.Id == id) && (group == null || s.Group == group) && (ctx0.Night == (s.Group == DailyGroup.Sleep) || id != null || group != null)).ToList();
        for (int tries = 0; tries < 8 && pool.Count > 0; tries++)
        {
            float total = pool.Sum(Weigh);
            float pick = R.Float() * total;
            var spec = pool[^1];
            foreach (var s in pool) { pick -= Weigh(s); if (pick <= 0f) { spec = s; break; } }
            var ctx = new DailyCtx(w, R);
            if (!spec.Run(ctx)) { pool.Remove(spec); continue; }
            Stats.Fired++;
            Stats.ByGroup[(int)spec.Group]++;
            Stats.Seen.Add(spec.Id);
            Recent.Add((w.Tick, spec.Id, ctx.Text ?? spec.Name));
            if (Recent.Count > 60) Recent.RemoveAt(0);
            return spec.Id;
        }
        return null;
    }

    // ═══════════════════════════════ 70 ═══════════════════════════════

    public static readonly DailySpec[] Catalog =
    {
        // ── 식사 ──
        new("burnt", "타 버린 저녁", DailyGroup.Meal, 1f, x =>
        {
            if (x.One(c => c.Job?.Label == "조리" || c.RawSkill(Skill.Cooking) > 0.3f && c.Room?.Type == RoomType.Galley) is not CrewMember cook) return false;
            var shelf = x.W.Ship.Furniture.FirstOrDefault(f => f.Storage != null && f.Storage.Count(ItemKind.Meal) > 0 && !f.Room.Detached);
            if (shelf == null) return false;
            shelf.Storage!.Take(ItemKind.Meal, 1);
            x.Stress(cook, 0.04f);
            x.Say(cook, "아, 태웠다…");
            var critic = x.Other(cook, c => x.Has(c, Habit.Grumbler) || x.Has(c, Habit.Joker));
            if (critic != null)
            {
                bool joke = x.Has(critic, Habit.Joker);
                x.Aff(cook, critic, joke ? 0.02f : -0.03f);
                x.Diary(cook, joke ? $"저녁을 태웠는데 {Ko.IGa(critic.Name)} 숯 맛이 난다며 웃었다" : $"저녁을 태웠다. {Ko.IGa(critic.Name)} 한참 투덜댔다");
            }
            return x.Done($"{Ko.IGa(cook.Name)} 저녁 한 끼를 태웠다", cook);
        }),
        new("favdish", "좋아하는 음식", DailyGroup.Meal, 1f, x =>
        {
            if (x.One(c => c.RawSkill(Skill.Cooking) > 0.25f) is not CrewMember cook) return false;
            if (x.Other(cook, c => c.AffinityTo(cook) > 0f || c.Needs.Stress > 0.5f) is not CrewMember o) return false;
            string fav = x.W.Relations.Favorite(o);
            bool spice = ItemsV15.Use(x.W, ItemKind.Spice); // v15 향신료가 있으면 고향 맛
            x.Stress(o, spice ? -0.09f : -0.06f); x.Food(o, 0.2f);
            x.Mem(o, cook, RelationReason.GaveMeGift, $"힘들 때 좋아하는 {Ko.EulReul(fav)} 만들어 줬다");
            x.Diary(o, $"{Ko.IGa(cook.Name)} 내가 좋아하는 {Ko.EulReul(fav)} 만들어 줬다");
            return x.Done($"{Ko.IGa(cook.Name)} {o.Name}에게 {Ko.EulReul(fav)} 해 줬다", cook, o);
        }),
        new("snack", "한밤의 간식", DailyGroup.Meal, 0.8f, x => x.W.Scenes.Snack(x)), // v16.1 장면으로 (Core/DailyScenes.cs)
        new("recipe", "고향의 조리법", DailyGroup.Meal, 0.8f, x =>
        {
            if (x.One(c => x.Was(c, Background.Chef, Background.Baker) || x.Likes(c, Hobby.Cooking) || x.Likes(c, Hobby.Baking)) is not CrewMember c) return false;
            if (x.Near(c) is not CrewMember o) return false;
            x.Social(c, 0.15f); x.Social(o, 0.15f); x.Aff(c, o, 0.03f);
            o.Practice(Skill.Cooking, 0.01f);
            x.Diary(o, $"{Ko.IGa(c.Name)} 고향 조리법을 알려 줬다. 다음엔 내가 해 봐야지");
            return x.Done($"{Ko.IGa(c.Name)} {o.Name}에게 고향 조리법을 알려 줬다", c, o);
        }),
        new("spill", "엎지른 국", DailyGroup.Meal, 0.8f, x => x.W.Scenes.Spill(x)), // v16.1 장면으로 (Core/DailyScenes.cs)
        new("coffee", "커피 한 잔씩", DailyGroup.Meal, 1f, x => x.W.Scenes.Coffee(x)), // v16.1 장면으로 (Core/DailyScenes.cs)
        new("tray", "치우지 않은 식판", DailyGroup.Meal, 0.7f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Messy) || x.Has(c, Habit.Procrastinator)) is not CrewMember c) return false;
            if (x.Other(c, o => x.Has(o, Habit.NeatFreak) || x.Has(o, Habit.ShortTempered)) is not CrewMember o) return false;
            x.Aff(c, o, -0.03f); x.Stress(o, 0.03f);
            x.Say(o, $"{c.Name}, 식판 좀 치워");
            x.Diary(o, $"{Ko.IGa(c.Name)} 또 식판을 그대로 두고 갔다");
            return x.Done($"{Ko.IGa(o.Name)} {c.Name}의 식판 때문에 한마디 했다", o, c);
        }),

        // ── 잠 (밤) ──
        new("snore", "코골이", DailyGroup.Sleep, 1f, x =>
        {
            if (x.One(c => c.Pose == Pose.Sleeping && x.Has(c, Habit.HeavySleeper), asleepOk: true) is not CrewMember s) return false;
            if (x.Near(s, o => o.Pose == Pose.Sleeping, asleepOk: true) is not CrewMember o) return false;
            if (ModulesV15.Has(s.Room, FurnitureType.NoiseDamper)) return false; // v15 방음재
            x.Rest(o, -0.08f); x.Aff(o, s, -0.02f);
            x.Diary(o, $"{Ko.IGa(s.Name)} 코를 골아서 한숨도 못 잤다");
            return x.Done($"{s.Name}의 코골이에 {Ko.IGa(o.Name)} 잠을 설쳤다", o, s);
        }),
        new("nightmare", "악몽", DailyGroup.Sleep, 1f, x =>
        {
            if (x.One(c => c.Pose == Pose.Sleeping && c.Fears.Count > 0 && (c.Needs.Stress > 0.4f || c.GriefUntil > x.W.Tick), asleepOk: true) is not CrewMember c) return false;
            var fear = Persona.Of(c.Fears[0]).Name;
            x.Stress(c, 0.08f); x.Rest(c, -0.05f);
            var o = x.Near(c, asleepOk: true);
            if (o != null) { x.Stress(c, -0.06f); x.Mem(c, o, RelationReason.Comforted, "악몽을 꾸고 깼을 때 곁에서 달래 줬다"); }
            x.Diary(c, $"{fear} 꿈을 꿨다" + (o != null ? $". {Ko.IGa(o.Name)} 깨서 괜찮다고 해 줬다" : ""));
            return x.Done($"{Ko.IGa(c.Name)} 악몽({fear})을 꾸다 깼다", c);
        }),
        new("latetalk", "잠 못 드는 밤의 대화", DailyGroup.Sleep, 1f, x =>
        {
            if (x.One(c => c.IsAwake && (x.Has(c, Habit.Insomniac) || x.Has(c, Habit.NightOwl))) is not CrewMember c) return false;
            if (x.Other(c, o => o.IsAwake) is not CrewMember o) return false;
            x.Aff(c, o, 0.05f); x.Social(c, 0.2f); x.Social(o, 0.2f); x.Rest(c, -0.03f); x.Rest(o, -0.03f);
            x.Diary(o, $"새벽에 {Ko.WaGwa(c.Name)} 한참 이야기했다");
            return x.Done($"{Ko.WaGwa(c.Name)} {o.Name}, 새벽까지 이야기했다", c, o);
        }),
        new("sleepwalk", "몽유병", DailyGroup.Sleep, 0.3f, x => x.W.Scenes.Sleepwalk(x)), // v16.1 장면으로 (Core/DailyScenes.cs)
        new("blanket", "빌려준 담요", DailyGroup.Sleep, 0.8f, x =>
        {
            var b = x.W.Belongings.All.FirstOrDefault(b => b.Kind == BelongingKind.Blanket && b.Owner >= 0 && b.Owner < x.W.Crew.Count && !x.W.Crew[b.Owner].Dead && b.BorrowedBy < 0);
            if (b == null) return false;
            var owner = x.W.Crew[b.Owner];
            if (x.Other(owner, o => o.Room?.Air.Temperature < 20f || o.Needs.Stress > 0.5f) is not CrewMember o) return false;
            b.BorrowedBy = o.Id; b.BorrowedAt = x.W.Tick;
            x.Rest(o, 0.05f);
            x.Mem(o, owner, RelationReason.GaveMeGift, $"추운 밤에 {Ko.EulReul(b.Name)} 빌려줬다");
            return x.Done($"{Ko.IGa(owner.Name)} {o.Name}에게 {Ko.EulReul(b.Name)} 빌려줬다", owner, o);
        }),
        new("prank", "알람 장난", DailyGroup.Sleep, 0.5f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Prankster)) is not CrewMember p) return false;
            if (x.Other(p, o => o.Pose == Pose.Sleeping) is not CrewMember v) return false;
            x.Rest(v, -0.05f);
            bool laughs = x.Has(v, Habit.Joker) || x.Has(v, Habit.Cheerful);
            x.Aff(p, v, laughs ? 0.03f : -0.04f);
            x.Diary(v, laughs ? $"{Ko.IGa(p.Name)} 알람을 새벽 네 시에 맞춰 놨다. 다음엔 갚아 준다" : $"{Ko.IGa(p.Name)} 알람을 새벽에 맞춰 놨다. 하나도 안 웃기다");
            return x.Done($"{Ko.IGa(p.Name)} {v.Name}의 알람을 새벽으로 바꿔 놨다", p, v);
        }),
        new("lullaby", "흥얼거림", DailyGroup.Sleep, 0.6f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Hummer) || x.Was(c, Background.Musician) || x.Likes(c, Hobby.Instrument)) is not CrewMember c) return false;
            var mates = x.W.Crew.Where(o => o != c && o.Room == c.Room && o.Pose == Pose.Sleeping).ToList();
            if (mates.Count == 0) return false;
            foreach (var o in mates) x.Rest(o, 0.03f);
            return x.Done($"{Ko.IGa(c.Name)} 잠든 {mates.Count}명 곁에서 낮게 흥얼거렸다", c);
        }),

        // ── 물건 ──
        new("found", "찾아 준 물건", DailyGroup.Things, 1f, x =>
        {
            var b = x.W.Belongings.All.FirstOrDefault(b => b.At != null && b.Holder < 0 && !b.Memorial && b.Owner >= 0 && b.Owner < x.W.Crew.Count && !x.W.Crew[b.Owner].Dead);
            if (b == null) return false;
            var owner = x.W.Crew[b.Owner];
            if (x.Other(owner) is not CrewMember finder) return false;
            b.At = null; b.Open = false;
            x.Mem(owner, finder, RelationReason.SavedMyThing, $"두고 온 {Ko.EulReul(b.Name)} 챙겨다 줬다");
            x.Diary(owner, $"{Ko.IGa(finder.Name)} 내가 두고 온 {Ko.EulReul(b.Name)} 찾아 줬다");
            return x.Done($"{Ko.IGa(finder.Name)} {owner.Name}의 {Ko.EulReul(b.Name)} 찾아 돌려줬다", finder, owner);
        }),
        new("overdue", "안 돌려준 물건", DailyGroup.Things, 0.8f, x =>
        {
            var b = x.W.Belongings.All.FirstOrDefault(b => b.BorrowedBy >= 0 && x.W.Tick - b.BorrowedAt > SimTime.TicksPerDay && b.Owner >= 0 && b.Owner < x.W.Crew.Count && b.BorrowedBy < x.W.Crew.Count);
            if (b == null) return false;
            CrewMember owner = x.W.Crew[b.Owner], bor = x.W.Crew[b.BorrowedBy];
            if (owner.Dead || bor.Dead) return false;
            bool sorry = bor.Traits.Diligence > 0.5f;
            b.BorrowedBy = -1;
            if (sorry) x.Mem(owner, bor, RelationReason.KeptPromise, $"빌려 간 {Ko.EulReul(b.Name)} 늦게라도 챙겨 돌려줬다");
            else x.Aff(owner, bor, -0.04f);
            x.Diary(owner, sorry ? $"{Ko.IGa(bor.Name)} {Ko.EulReul(b.Name)} 돌려주며 미안하다고 했다" : $"{Ko.EulReul(b.Name)} 돌려받으려고 {bor.Name}에게 세 번이나 말했다");
            return x.Done($"{Ko.IGa(bor.Name)} 빌려 간 {owner.Name}의 {Ko.EulReul(b.Name)} 돌려줬다" + (sorry ? "" : " (한참 걸렸다)"), bor, owner);
        }),
        new("handmade", "손으로 만든 선물", DailyGroup.Things, 1f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Tinkerer) || x.Likes(c, Hobby.ModelBuilding) || x.Likes(c, Hobby.Knitting) || x.Was(c, Background.Carpenter)) is not CrewMember c) return false;
            var friend = x.W.Crew.Where(o => o != c && !o.Dead && !o.IsChild).OrderByDescending(o => c.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
            if (friend == null || c.AffinityTo(friend) < 0.1f) return false;
            bool knit = x.Likes(c, Hobby.Knitting);
            var b = x.W.Belongings.Gift(friend, c, knit ? BelongingKind.Blanket : BelongingKind.Artwork, knit ? $"{Ko.IGa(c.Name)} 뜬 목도리" : $"{Ko.IGa(c.Name)} 깎은 작은 배 모형", $"{SimTime.Day(x.W.Tick)}일 {c.Name}에게 받은 것");
            x.Mem(friend, c, RelationReason.GaveMeGift, $"손수 만든 {Ko.EulReul(b.Name)} 줬다");
            x.Diary(friend, $"{Ko.IGa(c.Name)} {Ko.EulReul(b.Name)} 만들어 줬다. 아껴야지");
            return x.Remembered($"{Ko.IGa(c.Name)} {friend.Name}에게 {(knit ? "목도리를 떠" : "작은 배 모형을 깎아")} 줬다", c, friend);
        }),
        new("mended", "고쳐 준 소중한 것", DailyGroup.Things, 0.8f, x =>
        {
            var b = x.W.Belongings.All.FirstOrDefault(b => b.Condition < 0.7f && b.Owner >= 0 && b.Owner < x.W.Crew.Count && !x.W.Crew[b.Owner].Dead);
            if (b == null) return false;
            var owner = x.W.Crew[b.Owner];
            if (x.Other(owner, o => x.Has(o, Habit.Tinkerer) || o.RawSkill(Skill.Mechanics) > 0.5f) is not CrewMember fixer) return false;
            b.Condition = 1f; b.Unnoticed = false;
            MarkLog.Add(b.Marks, x.W.Tick, $"{Ko.IGa(fixer.Name)} 고쳐 줬다");
            x.Mem(owner, fixer, RelationReason.FixedMyThing, $"망가진 {Ko.EulReul(b.Name)} 고쳐 줬다");
            return x.Remembered($"{Ko.IGa(fixer.Name)} {owner.Name}의 {Ko.EulReul(b.Name)} 고쳐 줬다", fixer, owner);
        }),
        new("photo", "고향 사진", DailyGroup.Things, 1f, x =>
        {
            var b = x.W.Belongings.All.FirstOrDefault(b => b.Kind == BelongingKind.Photo && b.Owner >= 0 && b.Owner < x.W.Crew.Count && x.W.Crew[b.Owner] is { Dead: false, IsAwake: true });
            if (b == null) return false;
            var c = x.W.Crew[b.Owner];
            var o = x.Near(c);
            if (o != null) { x.Social(c, 0.15f); x.Aff(c, o, 0.03f); x.Stress(c, -0.03f); }
            else x.Stress(c, x.Has(c, Habit.Homesick) ? 0.05f : -0.02f);
            x.Diary(c, o != null ? $"{Ko.EulReul(b.Name)} 보다가 {Ko.WaGwa(o.Name)} 고향 이야기를 했다" : $"혼자 {Ko.EulReul(b.Name)} 오래 봤다");
            return x.Done($"{Ko.IGa(c.Name)} {Ko.EulReul(b.Name)} 꺼내 봤다" + (o != null ? $" — {o.Name}에게도 보여 줬다" : ""), c);
        }),
        new("lend", "빌려준 책", DailyGroup.Things, 0.8f, x =>
        {
            var b = x.W.Belongings.All.FirstOrDefault(b => b.Kind is BelongingKind.Book or BelongingKind.Puzzle or BelongingKind.Headphones && b.BorrowedBy < 0 && b.Owner >= 0 && b.Owner < x.W.Crew.Count && x.W.Crew[b.Owner] is { Dead: false });
            if (b == null) return false;
            var owner = x.W.Crew[b.Owner];
            if (x.Other(owner, o => o.Needs.Social < 0.6f || x.Has(o, Habit.Bookworm)) is not CrewMember o) return false;
            b.BorrowedBy = o.Id; b.BorrowedAt = x.W.Tick;
            x.Aff(owner, o, 0.03f);
            return x.Done($"{Ko.IGa(owner.Name)} {o.Name}에게 {Ko.EulReul(b.Name)} 빌려줬다", owner, o);
        }),
        new("stash", "들킨 비상식량", DailyGroup.Things, 0.5f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Hoarder) || x.Has(c, Habit.Collector)) is not CrewMember c) return false;
            if (x.Other(c) is not CrewMember o) return false;
            bool laugh = x.Has(o, Habit.Joker) || x.Has(o, Habit.Cheerful) || o.AffinityTo(c) > 0.3f;
            x.Aff(c, o, laugh ? 0.02f : -0.03f);
            x.Diary(c, laugh ? $"사물함 속 비상식량을 {Ko.IGa(o.Name)} 봤다. 같이 웃었다" : $"사물함 속 비상식량을 {Ko.IGa(o.Name)} 봤다. 눈빛이 곱지 않았다");
            return x.Done($"{Ko.IGa(o.Name)} {c.Name}의 사물함에서 쌓아 둔 비상식량을 봤다", o, c);
        }),

        // ── 일 ──
        new("tip", "선배의 요령", DailyGroup.Work, 1.2f, x =>
        {
            foreach (Skill s in Enum.GetValues<Skill>())
            {
                if (x.One(c => c.RawSkill(s) > 0.6f) is not CrewMember senior) continue;
                if (x.Near(senior, o => o.RawSkill(s) < senior.RawSkill(s) - 0.2f) is not CrewMember jr) continue;
                jr.Practice(s, 0.015f);
                x.Mem(jr, senior, RelationReason.TaughtMe, $"{Skills.Name(s)} 요령을 하나 알려 줬다");
                x.Diary(jr, $"{Ko.IGa(senior.Name)} {Skills.Name(s)} 요령을 알려 줬다");
                return x.Done($"{Ko.IGa(senior.Name)} {jr.Name}에게 {Skills.Name(s)} 요령을 알려 줬다", senior, jr);
            }
            return false;
        }),
        new("praise", "칭찬", DailyGroup.Work, 1f, x =>
        {
            if (x.One(c => c.Stats.Repairs > 0) is not CrewMember c) return false;
            if (x.Other(c, o => x.Has(o, Habit.Leader) || x.W.Command.Captain == o || x.Has(o, Habit.Generous)) is not CrewMember o) return false;
            x.Stress(c, -0.05f); x.Aff(c, o, 0.03f);
            x.Say(o, $"{c.Name}, 지난번 수리 깔끔하더라");
            x.Diary(c, $"{Ko.IGa(o.Name)} 내 수리를 칭찬했다");
            return x.Done($"{Ko.IGa(o.Name)} {c.Name}의 수리 솜씨를 칭찬했다", o, c);
        }),
        new("recheck", "다시 본 볼트", DailyGroup.Work, 1f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Methodical) || x.Has(c, Habit.Perfectionist)) is not CrewMember c) return false;
            var m = x.W.Ship.Machines.Where(m => m.Wear > 0.2f && !m.Body.Room.Detached).OrderByDescending(m => m.Wear).FirstOrDefault();
            if (m == null) return false;
            m.Wear = MathF.Max(0f, m.Wear - 0.04f);
            MarkLog.Add(m.Marks, x.W.Tick, $"{c.Name}: 다시 조였다");
            return x.Done($"{Ko.IGa(c.Name)} {Ko.EulReul(m.Name)} 한 번 더 보다가 헐거운 볼트를 조였다", c);
        }),
        new("shortcut", "지름길", DailyGroup.Work, 0.6f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Hasty) || x.Has(c, Habit.Daredevil)) is not CrewMember c) return false;
            var m = x.W.Ship.Machines.Where(m => !m.Body.Room.Detached).OrderBy(m => m.Body.Id).Skip(x.R.Range(0, Math.Max(1, x.W.Ship.Machines.Count()))).FirstOrDefault();
            if (m == null) return false;
            m.Wear = MathF.Min(1f, m.Wear + 0.02f);
            var o = x.Other(c, o => x.Has(o, Habit.Methodical) || x.Was(o, Background.SafetyInspector));
            if (o != null) { x.Aff(c, o, -0.02f); x.Say(o, "그렇게 건너뛰면 언젠가 탈 난다"); }
            return x.Done($"{Ko.IGa(c.Name)} {m.Name} 점검 순서를 건너뛰었다" + (o != null ? $" — {Ko.IGa(o.Name)} 한마디 했다" : ""), c);
        }),
        new("misplaced", "어디 뒀더라", DailyGroup.Work, 0.8f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Forgetful) || x.Has(c, Habit.Messy)) is not CrewMember c) return false;
            var tool = x.W.Belongings.Of(c).FirstOrDefault(b => b.Kind == BelongingKind.Toolset);
            var o = x.Other(c);
            if (o != null) { x.Aff(c, o, o.Traits.Diligence > 0.6f ? -0.01f : 0.01f); x.Mem(c, o, RelationReason.SavedMyThing, "잃어버린 공구를 같이 찾아 줬다"); }
            x.Stress(c, 0.03f);
            return x.Done($"{Ko.IGa(c.Name)} {Ko.EulReul((tool?.Name ?? "공구"))} 어디 뒀는지 몰라 한 시간을 찾았다" + (o != null ? $" — {Ko.IGa(o.Name)} 같이 찾았다" : ""), c);
        }),
        new("overtime", "야근 커피", DailyGroup.Work, 0.8f, x => x.W.Scenes.Overtime(x)), // v16.1 장면으로 (Core/DailyScenes.cs)
        new("hazardspot", "바닥의 기름", DailyGroup.Work, 0.8f, x =>
        {
            var room = x.W.Ship.LiveRooms.Where(r => x.W.Soil.RoomSoil(r)[(int)SoilKind.Oil] > 0.15f).OrderByDescending(r => x.W.Soil.RoomSoil(r)[(int)SoilKind.Oil]).FirstOrDefault();
            if (room == null) return false;
            if (x.One(c => x.Was(c, Background.SafetyInspector, Background.Firefighter) || x.Has(c, Habit.NeatFreak) || x.Has(c, Habit.Worrier)) is not CrewMember c) return false;
            x.W.Soil.RoomSoil(room)[(int)SoilKind.Oil] *= 0.3f;
            x.Mark(room, $"{c.Name}: 미끄러운 기름을 닦았다");
            return x.Done($"{Ko.IGa(c.Name)} {room.Name} 바닥의 기름을 보고 닦았다 — 누가 미끄러지기 전에", c);
        }),

        // ── 관계 ──
        new("apology", "먼저 건넨 사과", DailyGroup.Bond, 1f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Patient) || c.Traits.Sociability > 0.6f) is not CrewMember c) return false;
            if (x.Other(c, o => c.AffinityTo(o) < -0.05f) is not CrewMember o) return false;
            x.Aff(c, o, 0.08f);
            x.Mem(o, c, RelationReason.Apologized, "먼저 와서 사과했다");
            x.Diary(o, $"{Ko.IGa(c.Name)} 먼저 미안하다고 했다");
            return x.Remembered($"{Ko.IGa(c.Name)} 사이가 틀어졌던 {o.Name}에게 먼저 사과했다", c, o);
        }),
        new("joke", "둘만 아는 농담", DailyGroup.Bond, 1.2f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Joker) || x.Has(c, Habit.Cheerful)) is not CrewMember c) return false;
            if (x.Near(c) is not CrewMember o) return false;
            x.Aff(c, o, 0.04f); x.Social(c, 0.1f); x.Social(o, 0.1f); x.Stress(o, -0.03f);
            return x.Done($"{Ko.IGa(c.Name)} {o.Name}에게 둘만 아는 농담을 했다 — {Ko.IGa(o.Name)} 크게 웃었다", c, o);
        }),
        new("argue", "당번 다툼", DailyGroup.Bond, 0.8f, x =>
        {
            if (x.One(c => x.Has(c, Habit.ShortTempered) || x.Has(c, Habit.Grumbler)) is not CrewMember c) return false;
            if (x.Near(c, o => x.Has(o, Habit.Messy) || x.Has(o, Habit.Procrastinator) || o.AffinityTo(c) < 0f) is not CrewMember o) return false;
            x.Aff(c, o, -0.05f); x.Stress(c, 0.04f); x.Stress(o, 0.04f);
            x.Diary(o, $"{Ko.WaGwa(c.Name)} 청소 당번 때문에 다퉜다");
            return x.Done($"{Ko.WaGwa(c.Name)} {o.Name}, 청소 당번 문제로 언성을 높였다", c, o);
        }),
        new("birthday", "생일", DailyGroup.Bond, 0.6f, x =>
        {
            int day = SimTime.Day(x.W.Tick);
            if (x.One(c => (c.Id * 37 + x.W.Seed) % 30 == day % 30) is not CrewMember c) return false;
            var mates = x.W.Crew.Where(o => o != c && !o.Dead && o.IsAwake).ToList();
            foreach (var o in mates.Take(6)) { x.Aff(c, o, 0.02f); x.Social(o, 0.05f); }
            x.Social(c, 0.3f); x.Stress(c, -0.08f);
            x.Diary(c, $"다들 생일을 챙겨 줬다. {(mates.Count > 0 ? Ko.IGa(mates[0].Name) + " 노래를 시작했다" : "")}");
            return x.Remembered($"{c.Name}의 생일 — 다 같이 노래를 불렀다", c);
        }),
        new("secret", "털어놓은 비밀", DailyGroup.Bond, 0.7f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Worrier) || c.Needs.Stress > 0.5f) is not CrewMember c) return false;
            if (x.Other(c, o => c.AffinityTo(o) > 0.3f) is not CrewMember o) return false;
            x.Aff(c, o, 0.05f); x.Stress(c, -0.06f);
            x.Diary(c, $"{o.Name}에게만 털어놨다. 조금 가벼워졌다");
            return x.Done($"{Ko.IGa(c.Name)} {o.Name}에게 걱정을 털어놨다", c, o);
        }),
        new("matchmake", "엮어 주기", DailyGroup.Bond, 0.5f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Talker) || c.Traits.Sociability > 0.7f) is not CrewMember m) return false;
            var pair = x.W.Crew.Where(a => a != m && !a.Dead && !a.IsChild).SelectMany(a => x.W.Crew.Where(b => b.Id > a.Id && b != m && !b.Dead && !b.IsChild).Select(b => (a, b)))
                .Where(p => p.a.AffinityTo(p.b) > 0.25f && p.b.AffinityTo(p.a) > 0.25f).OrderBy(p => p.a.Id).FirstOrDefault();
            if (pair.a == null) return false;
            x.Aff(pair.a, pair.b, 0.05f);
            return x.Done($"{Ko.IGa(m.Name)} {Ko.WaGwa(pair.a.Name)} {Ko.EulReul(pair.b.Name)} 같은 조에 넣자고 넌지시 말했다", m, pair.a, pair.b);
        }),
        new("cardpeace", "카드로 푼 앙금", DailyGroup.Bond, 0.6f, x =>
        {
            if (x.One(c => x.Likes(c, Hobby.Cards) || x.Likes(c, Hobby.Games)) is not CrewMember c) return false;
            if (x.Other(c, o => c.AffinityTo(o) < 0f) is not CrewMember o) return false;
            x.Aff(c, o, 0.06f); x.Social(c, 0.1f); x.Social(o, 0.1f);
            x.Diary(o, $"{Ko.WaGwa(c.Name)} 카드를 쳤다. 말은 별로 안 했지만 괜찮았다");
            return x.Done($"{Ko.WaGwa(c.Name)} {o.Name}, 서먹하던 사이에 카드 한 판을 쳤다", c, o);
        }),

        // ── 몸 ──
        new("cut", "베인 손가락", DailyGroup.Body, 0.8f, x =>
        {
            if (x.One(c => c.Pose == Pose.Working) is not CrewMember c) return false;
            NeedsSystem.AddInjury(c.Vitals, 0.03f, "작업 중 베임");
            c.Soil.Hands[(int)SoilKind.Bio] = MathF.Min(1f, c.Soil.Hands[(int)SoilKind.Bio] + 0.2f);
            var medic = x.Other(c, o => o.RawSkill(Skill.Medicine) > 0.4f);
            if (medic != null) x.Mem(c, medic, RelationReason.NursedMe, "손가락을 베었을 때 감아 줬다");
            return x.Done($"{Ko.IGa(c.Name)} 일하다 손가락을 베었다" + (medic != null ? $" — {Ko.IGa(medic.Name)} 감아 줬다" : ""), c);
        }),
        new("backache", "허리", DailyGroup.Body, 0.7f, x =>
        {
            if (x.One(c => c.Age > 42f && (c.Carrying != null || c.Pose == Pose.Working)) is not CrewMember c) return false;
            x.Rest(c, -0.05f);
            var o = x.Near(c, o => o.Age < c.Age - 8f);
            if (o != null) x.Mem(c, o, RelationReason.DidMyShift, "허리가 아플 때 짐을 대신 들어 줬다");
            return x.Done($"{Ko.IGa(c.Name)} 허리를 붙잡았다" + (o != null ? $" — {Ko.IGa(o.Name)} 짐을 받아 들었다" : ""), c);
        }),
        new("workout", "같이 운동", DailyGroup.Body, 1f, x =>
        {
            if (x.One(c => x.Has(c, Habit.GymRat) || x.Likes(c, Hobby.Workout)) is not CrewMember c) return false;
            if (x.Other(c, o => o.Needs.Stress > 0.3f) is not CrewMember o) return false;
            x.Stress(c, -0.04f); x.Stress(o, -0.06f); x.Rest(o, -0.05f); x.Aff(c, o, 0.03f);
            return x.Done($"{Ko.IGa(c.Name)} {Ko.EulReul(o.Name)} 끌고 가 같이 운동했다", c, o);
        }),
        new("hiccup", "딸꾹질", DailyGroup.Body, 0.5f, x =>
        {
            if (x.One() is not CrewMember c) return false;
            var o = x.Near(c, o => x.Has(o, Habit.Joker) || x.Has(o, Habit.Prankster));
            if (o == null) return false;
            x.Aff(c, o, 0.02f); x.Social(c, 0.05f);
            return x.Done($"{Ko.IGa(c.Name)} 딸꾹질이 멈추지 않자 {Ko.IGa(o.Name)} 뒤에서 놀래켰다 — 멈췄다", c, o);
        }),
        new("sneeze", "재채기", DailyGroup.Body, 0.6f, x =>
        {
            if (x.One(c => c.Needs.Rest < 0.4f || c.Needs.Stress > 0.6f) is not CrewMember c) return false;
            if (x.W.Ailments.Catch(c, "cold", null, "지치고 추웠다") == null) return false;
            var o = x.Near(c, o => x.Has(o, Habit.Worrier) || x.Has(o, Habit.NeatFreak));
            if (o != null) x.Say(o, "마스크 써");
            return x.Done($"{Ko.IGa(c.Name)} 재채기를 하더니 감기 기운이 돈다" + (o != null ? $" — {Ko.IGa(o.Name)} 한 발 물러났다" : ""), c);
        }),
        new("haircut", "머리 잘라 주기", DailyGroup.Body, 0.7f, x =>
        {
            if (x.One(c => c.Room?.Type is RoomType.Quarters or RoomType.Lounge or RoomType.PrivateCabins) is not CrewMember c) return false;
            if (x.Near(c) is not CrewMember o) return false;
            bool good = c.Traits.Calm > 0.5f || x.Was(c, Background.Artist);
            x.Aff(c, o, good ? 0.04f : -0.01f); x.Social(o, 0.1f);
            x.Diary(o, good ? $"{Ko.IGa(c.Name)} 머리를 잘라 줬다. 생각보다 괜찮다" : $"{Ko.IGa(c.Name)} 머리를 잘라 줬다. 한동안 모자를 써야겠다");
            return x.Done($"{Ko.IGa(c.Name)} {o.Name}의 머리를 잘라 줬다", c, o);
        }),
        new("stretch", "아침 스트레칭", DailyGroup.Body, 0.8f, x =>
        {
            if (x.Hour < 6 || x.Hour > 10) return false;
            if (x.One(c => x.Likes(c, Hobby.Yoga) || x.Has(c, Habit.EarlyBird)) is not CrewMember c) return false;
            var mates = x.InRoom(c.Room!).Where(o => o != c).ToList();
            if (mates.Count == 0) return false;
            foreach (var o in mates) { x.Stress(o, -0.03f); o.ChangeAffinity(c, 0.01f); }
            return x.Done($"{Ko.IGa(c.Name)} {c.Room!.Name}에서 {mates.Count}명과 아침 스트레칭을 했다", c);
        }),

        // ── 배 ──
        new("noise", "이상한 소리", DailyGroup.Ship, 1f, x =>
        {
            var m = x.W.Ship.Machines.Where(m => m.Wear > 0.45f && !m.Body.Room.Detached).OrderByDescending(m => m.Wear).FirstOrDefault();
            if (m == null) return false;
            if (x.One(c => c.Room == m.Body.Room || x.Has(c, Habit.Worrier)) is not CrewMember c) return false;
            MarkLog.Add(m.Marks, x.W.Tick, $"{c.Name}: 끼익 소리를 들었다");
            if (c.RawSkill(m.Spec.Skill) > 0.4f) m.Wear = MathF.Max(0f, m.Wear - 0.03f);
            return x.Done($"{Ko.IGa(c.Name)} {m.Name}에서 나는 낯선 소리를 듣고 들여다봤다", c);
        }),
        new("flicker", "깜빡이는 불", DailyGroup.Ship, 0.8f, x =>
        {
            var room = x.W.Ship.LiveRooms.Where(r => r.PowerFlow < 0.95f && r.Powered).OrderBy(r => r.PowerFlow).FirstOrDefault()
                       ?? (x.W.Net.Links.FirstOrDefault(l => l.Temp)?.Room);
            if (room == null) return false;
            if (x.One(c => x.Has(c, Habit.Superstitious) || c.Fears.Contains(Fear.Dark)) is not CrewMember c) return false;
            x.Stress(c, 0.05f);
            var e = x.Other(c, o => o.RawSkill(Skill.Electrical) > 0.4f);
            if (e != null) { x.Stress(c, -0.04f); x.Say(e, "전압이 조금 떨어져서 그래. 귀신 아니야"); }
            return x.Done($"{room.Name} 불이 깜빡이자 {Ko.IGa(c.Name)} 질겁했다" + (e != null ? $" — {Ko.IGa(e.Name)} 까닭을 설명했다" : ""), c);
        }),
        new("drip", "맺힌 물방울", DailyGroup.Ship, 0.6f, x =>
        {
            var room = x.W.Ship.LiveRooms.Where(r => r.Air.Temperature < 18f || r.Type is RoomType.Hydroponics or RoomType.Laundry).OrderBy(r => r.Id).FirstOrDefault();
            if (room == null) return false;
            if (x.One(c => x.Has(c, Habit.NeatFreak) || x.Has(c, Habit.Methodical) || x.Was(c, Background.Plumber)) is not CrewMember c) return false;
            x.Mark(room, $"{c.Name}: 결로를 닦고 단열을 덧댔다");
            return x.Done($"{Ko.IGa(c.Name)} {room.Name} 천장에 맺힌 물방울을 보고 닦아 냈다", c);
        }),
        new("squeak", "삐걱이는 문", DailyGroup.Ship, 0.7f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Tinkerer) || x.Has(c, Habit.Fidgety) || x.Was(c, Background.AutoMechanic)) is not CrewMember c) return false;
            var room = c.Room!;
            x.Mark(room, $"{c.Name}: 문 경첩에 기름칠");
            foreach (var o in x.InRoom(room).Where(o => o != c && x.Has(o, Habit.Grumbler))) o.ChangeAffinity(c, 0.02f);
            return x.Done($"{Ko.IGa(c.Name)} {room.Name} 문이 삐걱대는 게 거슬려 기름을 쳤다", c);
        }),
        new("smell", "덕트로 온 냄새", DailyGroup.Ship, 0.7f, x =>
        {
            var g = x.RoomOf(RoomType.Galley);
            if (g == null || !g.DuctLinked) return false;
            var hungry = x.W.Crew.Where(c => !c.Dead && c.IsAwake && c.Room != g && c.Room?.DuctLinked == true).Take(4).ToList();
            if (hungry.Count == 0) return false;
            foreach (var c in hungry) c.Needs.Food = MathF.Max(0f, c.Needs.Food - 0.05f);
            return x.Done($"주방 냄새가 덕트를 타고 퍼졌다 — {string.Join("·", hungry.Select(c => c.Name))}의 배가 꼬르륵했다", hungry.ToArray());
        }),
        new("warmspot", "따뜻한 자리", DailyGroup.Ship, 0.6f, x =>
        {
            var room = x.W.Ship.LiveRooms.Where(r => r.Type is RoomType.Engine or RoomType.Reactor or RoomType.Cooling or RoomType.Power && r.Air.Temperature < 32f).OrderBy(r => r.Id).FirstOrDefault();
            if (room == null) return false;
            if (x.One(c => c.Needs.Rest < 0.5f && !c.Fears.Contains(Fear.Machines)) is not CrewMember c) return false;
            x.Rest(c, 0.06f);
            x.Mark(room, $"{c.Name}: 따뜻한 배관 곁에서 잠깐 졸았다");
            return x.Done($"{Ko.IGa(c.Name)} {room.Name} 따뜻한 배관 곁에서 잠깐 졸았다", c);
        }),
        new("beep", "헛삑", DailyGroup.Ship, 0.8f, x =>
        {
            var m = x.W.Ship.Machines.Where(m => m.SensorCal < 0.8f && !m.Body.Room.Detached).OrderBy(m => m.SensorCal).FirstOrDefault();
            if (m == null) return false;
            if (x.One(c => x.Has(c, Habit.Worrier) || c.Traits.Calm < 0.4f) is not CrewMember c) return false;
            x.Stress(c, 0.05f);
            var o = x.Other(c, o => x.Has(o, Habit.Methodical) || o.RawSkill(Skill.Electrical) > 0.5f);
            if (o != null) { x.Stress(c, -0.04f); MarkLog.Add(m.Marks, x.W.Tick, $"{o.Name}: 헛삑 — 감지기 교정이 틀어졌다"); }
            return x.Done($"{m.Name} 감지기가 괜히 삑 울려 {Ko.IGa(c.Name)} 뛰어갔다" + (o != null ? $" — {Ko.IGa(o.Name)} 교정이 틀어진 거라고 짚었다" : ""), c);
        }),

        // ── 관행 ──
        new("custom", "관행의 이유", DailyGroup.Custom, 1f, x =>
        {
            var cu = x.W.Culture.Customs.FirstOrDefault(k => !k.ReasonLost && k.Followers.Any(id => !k.Knowers.Contains(id)));
            if (cu == null) return false;
            var who = x.W.Crew.FirstOrDefault(c => cu.Followers.Contains(c.Id) && !cu.Knowers.Contains(c.Id) && !c.Dead && c.IsAwake);
            var teller = x.W.Crew.FirstOrDefault(c => cu.Knowers.Contains(c.Id) && !c.Dead && c.IsAwake);
            if (who == null || teller == null) return false;
            cu.Knowers.Add(who.Id);
            x.W.Culture.Stats.Explained++;
            x.Diary(who, $"{Ko.IGa(teller.Name)} 왜 {CultureSystem.Name(cu.Kind)}인지 이야기해 줬다 — {cu.Origin}");
            return x.Done($"{Ko.IGa(teller.Name)} {who.Name}에게 관행의 까닭을 들려줬다 ({CultureSystem.Name(cu.Kind)})", teller, who);
        }),
        new("toast", "무사한 열흘", DailyGroup.Custom, 0.6f, x =>
        {
            int day = SimTime.Day(x.W.Tick);
            if (day % 10 != 0 || x.Hour < 17) return false;
            var room = x.RoomOf(RoomType.Mess, RoomType.Lounge);
            if (room == null) return false;
            var mates = x.InRoom(room);
            if (mates.Count < 2) return false;
            foreach (var o in mates) { x.Social(o, 0.15f); x.Stress(o, -0.04f); }
            return x.Remembered($"출항 {day}일 — {room.Name}에서 {mates.Count}명이 잔을 들었다", mates.ToArray());
        }),
        new("olddays", "그날 이야기", DailyGroup.Custom, 0.8f, x =>
        {
            var e = x.W.History.Events.Where(e => e.Kind is HistoryKind.Incident or HistoryKind.Response or HistoryKind.Recovery && x.W.Tick - e.Tick > SimTime.TicksPerDay).OrderByDescending(e => e.Tick).FirstOrDefault();
            if (e == null) return false;
            if (x.One(c => x.W.Society.IsVeteran(c) || c.Age > 40f || x.Has(c, Habit.Talker)) is not CrewMember c) return false;
            var listeners = x.InRoom(c.Room!).Where(o => o != c).ToList();
            if (listeners.Count == 0) return false;
            foreach (var o in listeners) { x.Social(o, 0.08f); x.Diary(o, $"{Ko.IGa(c.Name)} {SimTime.Day(e.Tick)}일 이야기를 했다 — {e.Text}"); }
            return x.Done($"{Ko.IGa(c.Name)} {listeners.Count}명에게 {SimTime.Day(e.Tick)}일 이야기를 들려줬다", c);
        }),
        new("mention", "빈자리", DailyGroup.Custom, 0.8f, x =>
        {
            if (x.W.Life.Memorial.Count == 0) return false;
            var (name, _, _) = x.W.Life.Memorial[x.R.Range(0, x.W.Life.Memorial.Count)];
            var room = x.RoomOf(RoomType.Mess, RoomType.Lounge);
            if (room == null || x.InRoom(room) is not { Count: > 1 } mates) return false;
            foreach (var o in mates) { x.Stress(o, -0.02f); x.Social(o, 0.05f); }
            x.Diary(mates[0], $"밥 먹다 누가 {name} 이야기를 꺼냈다. 다들 잠깐 조용했다");
            return x.Done($"{room.Name}에서 누군가 {name} 이야기를 꺼냈다 — {mates.Count}명이 잠시 말이 없었다", mates.ToArray());
        }),
        new("song", "배의 노래", DailyGroup.Custom, 0.4f, x =>
        {
            if (x.One(c => x.Was(c, Background.Musician, Background.Writer) || x.Likes(c, Hobby.Instrument) || x.Likes(c, Hobby.Writing)) is not CrewMember c) return false;
            var mates = x.InRoom(c.Room!).Where(o => o != c).ToList();
            foreach (var o in mates) { x.Social(o, 0.1f); o.ChangeAffinity(c, 0.03f); }
            return x.Remembered($"{Ko.IGa(c.Name)} 이 배의 노래를 지었다 — {x.W.Ship.Name}의 노래", c);
        }),
        new("nickname", "설비의 별명", DailyGroup.Custom, 0.6f, x =>
        {
            var m = x.W.Ship.Machines.Where(m => m.Marks.Count >= 3 && !m.Body.Room.Detached).OrderByDescending(m => m.Marks.Count).FirstOrDefault();
            if (m == null || m.Marks.Any(k => k.Text.StartsWith("별명"))) return false;
            if (x.One(c => c.FamiliarityWith(m.Body.Type) > 0.2f || x.Has(c, Habit.Joker)) is not CrewMember c) return false;
            string[] names = { "고집쟁이", "할매", "투덜이", "늙은 말", "울보", "골칫덩이", "든든이" };
            string nick = names[(m.Body.Id + x.W.Seed) % names.Length];
            MarkLog.Add(m.Marks, x.W.Tick, $"별명: {nick} ({c.Name})");
            return x.Remembered($"{Ko.IGa(c.Name)} 자주 말썽인 {Ko.EulReul(m.Name)} '{nick}'라고 부르기 시작했다", c);
        }),
        new("trophy", "걸어 둔 물건", DailyGroup.Custom, 0.4f, x =>
        {
            var e = x.W.History.Events.Where(e => e.Kind == HistoryKind.Incident && x.W.Tick - e.Tick > SimTime.Hours(12)).OrderByDescending(e => e.Tick).FirstOrDefault();
            var room = x.RoomOf(RoomType.Mess, RoomType.Lounge);
            if (e == null || room == null || room.Marks.Any(m => m.Text.Contains(e.Text))) return false;
            if (x.One() is not CrewMember c) return false;
            x.Mark(room, $"벽에 걸린 것: {e.Text}의 흔적 ({c.Name})");
            return x.Remembered($"{Ko.IGa(c.Name)} {SimTime.Day(e.Tick)}일 일의 흔적(그을린 판 한 조각)을 {room.Name} 벽에 걸었다", c);
        }),

        // ── 여가 ──
        new("cards", "카드 한 판", DailyGroup.Leisure, 1f, x =>
        {
            if (x.One(c => x.Likes(c, Hobby.Cards) || x.Likes(c, Hobby.Games)) is not CrewMember c) return false;
            var mates = x.InRoom(c.Room!).Where(o => o != c).Take(3).ToList();
            if (mates.Count == 0) return false;
            var winner = mates[x.R.Range(0, mates.Count)];
            foreach (var o in mates) { x.Social(o, 0.1f); x.Aff(c, o, 0.02f); }
            return x.Done($"{Ko.IGa(c.Name)} {mates.Count}명과 카드를 쳤다 — {Ko.IGa(winner.Name)} 이겨서 한참 으스댔다", c, winner);
        }),
        new("chess", "체스", DailyGroup.Leisure, 0.8f, x => x.W.Scenes.Chess(x)), // v16.1 장면으로 (Core/DailyScenes.cs)
        new("jam", "즉흥 합주", DailyGroup.Leisure, 0.7f, x =>
        {
            var players = x.W.Crew.Where(c => !c.Dead && c.IsAwake && (x.Likes(c, Hobby.Instrument) || x.Was(c, Background.Musician))).ToList();
            if (players.Count < 1) return false;
            var room = x.RoomOf(RoomType.Lounge, RoomType.Theater, RoomType.Mess);
            if (room == null) return false;
            var listeners = x.InRoom(room);
            foreach (var o in listeners) { x.Social(o, 0.1f); x.Stress(o, -0.04f); }
            return x.Done($"{string.Join("·", players.Take(3).Select(p => p.Name))}의 즉흥 합주 — {room.Name}에 {listeners.Count}명이 모였다", players.ToArray());
        }),
        new("movie", "영화의 밤", DailyGroup.Leisure, 0.8f, x => x.W.Scenes.Movie(x)), // v16.1 장면으로 (Core/DailyScenes.cs)
        new("stars", "창밖의 별", DailyGroup.Leisure, 0.8f, x =>
        {
            if (x.One(c => x.Likes(c, Hobby.Stargazing) || x.Has(c, Habit.Gazer) || x.Was(c, Background.Astronomer)) is not CrewMember c) return false;
            x.Stress(c, -0.06f);
            var o = x.Near(c);
            if (o != null) { x.Aff(c, o, 0.03f); x.Diary(o, $"{Ko.IGa(c.Name)} 창밖 별자리 이름을 하나하나 알려 줬다"); }
            return x.Done($"{Ko.IGa(c.Name)} 창가에 오래 서서 별을 봤다", c);
        }),
        new("portrait", "그려 준 얼굴", DailyGroup.Leisure, 0.6f, x =>
        {
            if (x.One(c => x.Likes(c, Hobby.Painting) || x.Was(c, Background.Artist)) is not CrewMember c) return false;
            if (x.Other(c, o => c.AffinityTo(o) > 0.1f) is not CrewMember o) return false;
            var b = x.W.Belongings.Gift(o, c, BelongingKind.Artwork, $"{Ko.IGa(c.Name)} 그린 {o.Name}의 얼굴", $"{SimTime.Day(x.W.Tick)}일 {c.Name}에게 받은 그림");
            x.Mem(o, c, RelationReason.GaveMeGift, "내 얼굴을 그려 줬다");
            return x.Remembered($"{Ko.IGa(c.Name)} {o.Name}의 얼굴을 그려 줬다", c, o);
        }),
        new("bloom", "핀 꽃", DailyGroup.Leisure, 0.5f, x =>
        {
            var room = x.RoomOf(RoomType.Hydroponics, RoomType.Garden);
            if (room == null) return false;
            if (x.One(c => x.Likes(c, Hobby.Gardening) || x.Was(c, Background.Gardener, Background.FarmResearcher)) is not CrewMember c) return false;
            x.Mark(room, $"{c.Name}: 재배대 귀퉁이에 꽃이 피었다");
            foreach (var o in x.W.Crew.Where(o => !o.Dead && o.Room == room)) x.Stress(o, -0.05f);
            return x.Remembered($"{Ko.IGa(c.Name)} 몰래 심은 꽃이 {room.Name}에 피었다 — 다들 한 번씩 보러 갔다", c);
        }),

        // ── 바깥 ──
        new("letter", "집에서 온 소식", DailyGroup.Outside, 1f, x =>
        {
            var comms = x.RoomOf(RoomType.Comms);
            if (comms == null || !comms.Powered) return false;
            if (x.One() is not CrewMember c) return false;
            bool bad = x.R.Chance(0.2f);
            x.Stress(c, bad ? 0.12f : -0.08f);
            x.Diary(c, bad ? "집에서 소식이 왔다. 할머니가 편찮으시단다. 여기선 아무것도 할 수 없다" : "집에서 소식이 왔다. 다들 잘 지낸다고");
            if (bad && x.Other(c, o => o.AffinityTo(c) > 0.2f) is CrewMember o) x.Mem(c, o, RelationReason.Comforted, "집에서 나쁜 소식이 왔을 때 곁에 있어 줬다");
            return x.Done($"{c.Name}에게 집에서 소식이 왔다" + (bad ? " — 좋지 않은 소식이다" : ""), c);
        }),
        new("port", "다음 기항지", DailyGroup.Outside, 0.8f, x =>
        {
            string dest = x.W.Voyage.Destination;
            if (string.IsNullOrEmpty(dest)) return false;
            if (x.One() is not CrewMember c) return false;
            var mates = x.InRoom(c.Room!).Where(o => o != c).ToList();
            if (mates.Count == 0) return false;
            foreach (var o in mates) x.Social(o, 0.06f);
            return x.Done($"{Ko.IGa(c.Name)} {dest}에 닿으면 뭘 할지 이야기를 꺼냈다 — 다들 하나씩 보탰다", c);
        }),
        new("nebula", "성운", DailyGroup.Outside, 0.6f, x =>
        {
            var room = x.RoomOf(RoomType.Observatory, RoomType.Bridge, RoomType.Lounge);
            if (room == null) return false;
            var mates = x.InRoom(room);
            if (mates.Count == 0) return false;
            foreach (var o in mates) x.Stress(o, -0.05f);
            x.Diary(mates[0], "창밖으로 성운이 지나갔다. 사진으로는 담기지 않는다");
            return x.Remembered($"{room.Name} 창밖으로 성운이 지나갔다 — {Ko.IGa(string.Join("·", mates.Take(4).Select(m => m.Name)))} 오래 봤다", mates.ToArray());
        }),
        new("wreck", "스쳐 간 난파선", DailyGroup.Outside, 0.4f, x =>
        {
            if (x.One(c => c.Room?.Type is RoomType.Bridge or RoomType.Comms or RoomType.Observatory || x.Has(c, Habit.Gazer)) is not CrewMember c) return false;
            x.Stress(c, x.Has(c, Habit.Worrier) || c.Fears.Contains(Fear.Death) ? 0.08f : -0.01f);
            var o = x.Other(c, o => x.Has(o, Habit.Daredevil));
            if (o != null) x.Say(o, "저기 뭐 쓸 만한 거 없을까");
            return x.Done($"{Ko.IGa(c.Name)} 멀리 떠가는 난파선을 봤다" + (o != null ? $" — {Ko.IGa(o.Name)} 건지러 가자고 했다" : ""), c);
        }),
        new("static", "잡음 속 목소리", DailyGroup.Outside, 0.4f, x =>
        {
            var comms = x.RoomOf(RoomType.Comms);
            if (comms == null) return false;
            if (x.One(c => x.Has(c, Habit.Superstitious) || x.Has(c, Habit.Worrier) || c.Fears.Contains(Fear.Isolation)) is not CrewMember c) return false;
            x.Stress(c, 0.06f);
            var o = x.Other(c, o => x.Was(o, Background.Programmer, Background.SysAdmin, Background.Physicist, Background.Astronomer) || o.RawSkill(Skill.Electrical) > 0.5f);
            if (o != null) { x.Stress(c, -0.05f); x.Mem(c, o, RelationReason.Comforted, "통신 잡음이 무서웠을 때 까닭을 설명해 줬다"); }
            return x.Done($"{Ko.IGa(c.Name)} 통신 잡음 속에서 목소리를 들었다고 했다" + (o != null ? $" — {Ko.IGa(o.Name)} 우주 전파라고 설명했다" : ""), c);
        }),
        new("chart", "별자리 수업", DailyGroup.Outside, 0.6f, x =>
        {
            if (x.One(c => x.Was(c, Background.Astronomer, Background.CargoPilot) || c.RawSkill(Skill.Piloting) > 0.6f) is not CrewMember c) return false;
            if (x.Near(c, o => o.RawSkill(Skill.Piloting) < c.RawSkill(Skill.Piloting) - 0.2f) is not CrewMember o) return false;
            o.Practice(Skill.Piloting, 0.012f);
            x.Mem(o, c, RelationReason.TaughtMe, "별을 보고 위치를 잡는 법을 알려 줬다");
            return x.Done($"{Ko.IGa(c.Name)} {o.Name}에게 별을 보고 위치를 잡는 법을 알려 줬다", c, o);
        }),
        new("homesick", "명절", DailyGroup.Outside, 0.6f, x =>
        {
            if (x.One(c => x.Has(c, Habit.Homesick) || c.Needs.Social < 0.3f) is not CrewMember c) return false;
            x.Stress(c, 0.06f);
            var o = x.Other(c, o => o.AffinityTo(c) > 0.15f || x.Has(o, Habit.Generous));
            if (o != null) { x.Stress(c, -0.08f); x.Mem(c, o, RelationReason.Comforted, "고향 명절에 같이 상을 차려 줬다"); }
            x.Diary(c, o != null ? $"고향은 오늘 명절이다. {Ko.IGa(o.Name)} 작은 상을 같이 차려 줬다" : "고향은 오늘 명절이다. 여기는 그냥 화요일 같다");
            return x.Done($"{c.Name}의 고향 명절" + (o != null ? $" — {Ko.IGa(o.Name)} 작은 상을 차려 줬다" : ""), c);
        }),
    };
}
