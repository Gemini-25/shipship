using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15.8 소품·장식 70: 화분·포스터·액자·러그·조명·모빌·트로피·추모 액자·아이 그림·지도·깃발·시계 …
// 놓인 방이 조금 나아진다 — 쉬는 방은 더 풀리고(RelaxMul) 침실은 더 깊이 잔다(SleepAdd). 좋아하는 습관·취미가 맞는 사람은 곁에서 마음이 더 놓인다.
// 출처: 처음부터 있던 것 · 승무원이 취미로 만든 것(재료를 쓴다) · 기항지에서 산 것(돈을 쓴다) · 겪은 일에서 생긴 것(불 · 추모 · 관행 · 이정표).
// 불이 나면 종이·천 소품은 타고, 꺼진 뒤 그을린 벽판이 걸린다. 떠난 사람은 추모 액자로 남고, 추모일엔 그 앞에 촛불등이 켜진다.

public enum PropPlace { Rest, Sleep, Galley, Work, Command, Medical, Airlock, Corridor, Any }
public enum PropSource { Start, Craft, Port, Event }
public enum PropShape { Pot, Frame, Poster, Rug, Lamp, Mobile, Trophy, Clock, Flag, Map, Shelf, Candle, Plaque, Model, Cushion, Lights, Tank, Board, Box }

/// <summary>소품 한 줄: 이름 · 놓이는 방 · 모양 · 출처 · 쉼(곱에 더함) · 잠(더함) · 더 좋아하는 취미/습관 · 만드는 취미 · 재료 · 값 · 타는지 · 머무는 시간 · 생기는 때.</summary>
public sealed record PropSpec(string Id, string Name, PropPlace Place, PropShape Shape, PropSource Source, float Relax, float Sleep,
    Hobby? Hobby = null, Habit? Habit = null, Hobby? Maker = null, ItemKind? Material = null, float Cost = 0f, bool Flammable = false,
    int Hours = 0, bool Kid = false, Func<World, string?>? When = null)
{
    /// <summary>벽에 거는 것 (그림에서 벽 쪽에 붙는다).</summary>
    public bool Wall => Shape is PropShape.Frame or PropShape.Poster or PropShape.Clock or PropShape.Flag or PropShape.Map
        or PropShape.Shelf or PropShape.Plaque or PropShape.Lights or PropShape.Board;
}

/// <summary>방에 놓인 소품 하나.</summary>
public sealed class PlacedProp
{
    public int Id { get; init; }
    public PropSpec Spec { get; init; } = null!;
    public int RoomId { get; set; }
    public Cell At { get; set; }
    /// <summary>벽 쪽 (Cell.Dirs4 번호) · -1 = 바닥.</summary>
    public int Wall { get; set; } = -1;
    public int Maker { get; init; } = -1;
    public long Placed { get; init; }
    /// <summary>이때 치운다 (-1 = 계속 둔다).</summary>
    public long Until { get; init; } = -1;
    public string Origin { get; init; } = "";
    /// <summary>추모 액자의 이름 · 그을린 판의 날.</summary>
    public string? Label { get; init; }
    public string Name => Label != null ? $"{Spec.Name} ({Label})" : Spec.Name;
}

public static class Props
{
    public const float RelaxCap = 0.15f, SleepCap = 0.06f;

    public static readonly PropSpec[] All =
    {
        // ── 처음부터 12 ──
        new("clock", "벽시계", PropPlace.Galley, PropShape.Clock, PropSource.Start, 0.02f, 0f, Habit: Habit.Methodical),
        new("routemap", "항로 지도", PropPlace.Command, PropShape.Map, PropSource.Start, 0.02f, 0f, Habit: Habit.Gazer, Flammable: true),
        new("shipflag", "배 깃발", PropPlace.Command, PropShape.Flag, PropSource.Start, 0.02f, 0f, Habit: Habit.Leader, Flammable: true),
        new("readlamp", "독서등", PropPlace.Sleep, PropShape.Lamp, PropSource.Start, 0.01f, 0.01f, Hobby: Hobby.Reading),
        new("loungeplant", "고무나무 화분", PropPlace.Rest, PropShape.Pot, PropSource.Start, 0.04f, 0f, Hobby: Hobby.Gardening, Flammable: true),
        new("loungerug", "휴게실 러그", PropPlace.Rest, PropShape.Rug, PropSource.Start, 0.03f, 0f, Flammable: true),
        new("anatomy", "인체 해부도", PropPlace.Medical, PropShape.Poster, PropSource.Start, 0.01f, 0f, Habit: Habit.Serious, Flammable: true),
        new("safety", "안전 수칙 포스터", PropPlace.Work, PropShape.Poster, PropSource.Start, 0f, 0f, Habit: Habit.Worrier, Flammable: true),
        new("calendar", "벽 달력", PropPlace.Galley, PropShape.Board, PropSource.Start, 0.01f, 0f, Habit: Habit.Forgetful, Flammable: true),
        new("pillow", "메밀 베개", PropPlace.Sleep, PropShape.Cushion, PropSource.Start, 0f, 0.02f, Habit: Habit.Insomniac, Flammable: true),
        new("herbs", "주방 허브 화분", PropPlace.Galley, PropShape.Pot, PropSource.Start, 0.02f, 0f, Hobby: Hobby.Cooking, Flammable: true),
        new("launchphoto", "출항 단체 사진", PropPlace.Rest, PropShape.Frame, PropSource.Start, 0.03f, 0f, Habit: Habit.Homesick, Flammable: true),

        // ── 승무원이 만든다 34 (재료를 쓴다) ──
        new("knitrug", "뜨개 러그", PropPlace.Rest, PropShape.Rug, PropSource.Craft, 0.04f, 0f, Maker: Hobby.Knitting, Material: ItemKind.Thread, Flammable: true),
        new("quilt", "조각 이불", PropPlace.Sleep, PropShape.Rug, PropSource.Craft, 0f, 0.03f, Habit: Habit.Homesick, Maker: Hobby.Knitting, Material: ItemKind.Rag, Flammable: true),
        new("landscape", "고향 풍경화", PropPlace.Rest, PropShape.Frame, PropSource.Craft, 0.04f, 0f, Habit: Habit.Homesick, Maker: Hobby.Painting, Material: ItemKind.Paint, Flammable: true),
        new("mural", "통로 벽화", PropPlace.Corridor, PropShape.Poster, PropSource.Craft, 0.02f, 0f, Habit: Habit.Cheerful, Maker: Hobby.Painting, Material: ItemKind.Paint),
        new("starchart", "손으로 그린 별자리표", PropPlace.Command, PropShape.Map, PropSource.Craft, 0.02f, 0f, Hobby: Hobby.Stargazing, Maker: Hobby.Stargazing, Material: ItemKind.Paint, Flammable: true),
        new("starphoto", "별 사진 액자", PropPlace.Rest, PropShape.Frame, PropSource.Craft, 0.03f, 0f, Habit: Habit.Gazer, Maker: Hobby.Photography, Material: ItemKind.Glue, Flammable: true),
        new("crewphoto", "동료 사진 액자", PropPlace.Sleep, PropShape.Frame, PropSource.Craft, 0.02f, 0.01f, Habit: Habit.Talker, Maker: Hobby.Photography, Material: ItemKind.Glue, Flammable: true),
        new("mobile", "나무 모빌", PropPlace.Sleep, PropShape.Mobile, PropSource.Craft, 0f, 0.03f, Habit: Habit.HeavySleeper, Maker: Hobby.Woodwork, Material: ItemKind.Thread, Flammable: true),
        new("shipmodel", "배 모형", PropPlace.Rest, PropShape.Model, PropSource.Craft, 0.03f, 0f, Hobby: Hobby.ModelBuilding, Maker: Hobby.ModelBuilding, Material: ItemKind.Plate),
        new("fairylights", "자작 줄조명", PropPlace.Rest, PropShape.Lights, PropSource.Craft, 0.04f, 0f, Habit: Habit.Cheerful, Maker: Hobby.Electronics, Material: ItemKind.Lamp),
        new("nightlight", "수면등", PropPlace.Sleep, PropShape.Lamp, PropSource.Craft, 0f, 0.03f, Habit: Habit.Worrier, Maker: Hobby.Electronics, Material: ItemKind.Lamp),
        new("radio", "작업장 라디오", PropPlace.Work, PropShape.Box, PropSource.Craft, 0f, 0f, Habit: Habit.Hummer, Maker: Hobby.Electronics, Material: ItemKind.Electronics),
        new("poem", "시 한 편 액자", PropPlace.Rest, PropShape.Frame, PropSource.Craft, 0.02f, 0f, Habit: Habit.Bookworm, Maker: Hobby.Writing, Material: ItemKind.Glue, Flammable: true),
        new("bookshelf", "공용 책장", PropPlace.Rest, PropShape.Shelf, PropSource.Craft, 0.03f, 0f, Hobby: Hobby.Reading, Maker: Hobby.Woodwork, Material: ItemKind.Plate),
        new("teashelf", "찻잔 선반", PropPlace.Galley, PropShape.Shelf, PropSource.Craft, 0.02f, 0f, Habit: Habit.TeaLover, Maker: Hobby.Tea, Material: ItemKind.Plate),
        new("herbpot", "허브 화분", PropPlace.Sleep, PropShape.Pot, PropSource.Craft, 0.02f, 0.01f, Hobby: Hobby.Gardening, Maker: Hobby.Gardening, Material: ItemKind.Seed, Flammable: true),
        new("bonsai", "분재", PropPlace.Command, PropShape.Pot, PropSource.Craft, 0.02f, 0f, Habit: Habit.Patient, Maker: Hobby.Gardening, Material: ItemKind.Seed, Flammable: true),
        new("rockcase", "광석 진열장", PropPlace.Rest, PropShape.Shelf, PropSource.Craft, 0.02f, 0f, Habit: Habit.Collector, Maker: Hobby.Collecting, Material: ItemKind.MetalOre),
        new("puzzleframe", "완성한 퍼즐 액자", PropPlace.Rest, PropShape.Frame, PropSource.Craft, 0.03f, 0f, Hobby: Hobby.Puzzles, Maker: Hobby.Puzzles, Material: ItemKind.Glue, Flammable: true),
        new("scoreboard", "내기 점수판", PropPlace.Rest, PropShape.Board, PropSource.Craft, 0.02f, 0f, Habit: Habit.Joker, Maker: Hobby.Cards, Material: ItemKind.Paint, Flammable: true),
        new("wishboard", "소원 게시판", PropPlace.Rest, PropShape.Board, PropSource.Craft, 0.02f, 0f, Habit: Habit.Optimist, Material: ItemKind.Tape, Flammable: true),
        new("shadowboard", "공구 그림자판", PropPlace.Work, PropShape.Board, PropSource.Craft, 0f, 0f, Habit: Habit.NeatFreak, Maker: Hobby.Woodwork, Material: ItemKind.Paint),
        new("dreamcatcher", "드림캐처", PropPlace.Sleep, PropShape.Mobile, PropSource.Craft, 0f, 0.02f, Habit: Habit.Superstitious, Maker: Hobby.Knitting, Material: ItemKind.Thread, Flammable: true),
        new("cranes", "종이학 줄", PropPlace.Sleep, PropShape.Mobile, PropSource.Craft, 0.01f, 0.01f, Habit: Habit.Patient, Flammable: true),
        new("kiddrawing", "아이 그림", PropPlace.Rest, PropShape.Poster, PropSource.Craft, 0.04f, 0f, Habit: Habit.Cheerful, Kid: true, Flammable: true),
        new("handprint", "아이 손바닥 판", PropPlace.Rest, PropShape.Plaque, PropSource.Craft, 0.03f, 0f, Habit: Habit.Generous, Material: ItemKind.Paint, Kid: true),
        new("dancetape", "춤 연습 바닥 표시", PropPlace.Rest, PropShape.Rug, PropSource.Craft, 0.02f, 0f, Hobby: Hobby.Dancing, Maker: Hobby.Dancing, Material: ItemKind.Tape),
        new("songsheet", "노래 가사판", PropPlace.Rest, PropShape.Board, PropSource.Craft, 0.02f, 0f, Hobby: Hobby.Singing, Maker: Hobby.Singing, Material: ItemKind.Tape, Flammable: true),
        new("sharedmat", "공용 요가 매트", PropPlace.Rest, PropShape.Rug, PropSource.Craft, 0.03f, 0f, Hobby: Hobby.Yoga, Maker: Hobby.Yoga, Material: ItemKind.Rag, Flammable: true),
        new("zenstone", "명상 돌", PropPlace.Rest, PropShape.Model, PropSource.Craft, 0.03f, 0f, Hobby: Hobby.Meditation, Maker: Hobby.Meditation, Material: ItemKind.Silicate),
        new("recipeboard", "조리법 판", PropPlace.Galley, PropShape.Board, PropSource.Craft, 0.01f, 0f, Hobby: Hobby.Baking, Maker: Hobby.Cooking, Material: ItemKind.Paint, Flammable: true),
        new("playlist", "노래 목록판", PropPlace.Work, PropShape.Board, PropSource.Craft, 0f, 0f, Hobby: Hobby.Music, Maker: Hobby.Instrument, Material: ItemKind.Tape, Flammable: true),
        new("storyboard", "이야기 벽", PropPlace.Rest, PropShape.Board, PropSource.Craft, 0.02f, 0f, Hobby: Hobby.Storytelling, Maker: Hobby.Journaling, Material: ItemKind.Tape, Flammable: true),
        new("walkmap", "걷기 코스 지도", PropPlace.Corridor, PropShape.Map, PropSource.Craft, 0.01f, 0f, Hobby: Hobby.Walking, Maker: Hobby.Walking, Material: ItemKind.Paint, Flammable: true),

        // ── 기항지에서 산다 8 (돈) ──
        new("travelposter", "기항지 여행 포스터", PropPlace.Rest, PropShape.Poster, PropSource.Port, 0.03f, 0f, Habit: Habit.Daredevil, Cost: 1.5f, Flammable: true),
        new("aquarium", "작은 어항", PropPlace.Rest, PropShape.Tank, PropSource.Port, 0.05f, 0f, Habit: Habit.Gazer, Cost: 5f),
        new("silklamp", "비단 등", PropPlace.Sleep, PropShape.Lamp, PropSource.Port, 0.01f, 0.02f, Cost: 3f, Flammable: true),
        new("carpet", "손으로 짠 양탄자", PropPlace.Rest, PropShape.Rug, PropSource.Port, 0.05f, 0f, Cost: 4f, Flammable: true),
        new("musicbox", "오르골", PropPlace.Sleep, PropShape.Box, PropSource.Port, 0f, 0.03f, Habit: Habit.Homesick, Cost: 3f),
        new("pendulum", "진자 시계", PropPlace.Galley, PropShape.Clock, PropSource.Port, 0.02f, 0f, Habit: Habit.Methodical, Cost: 4f),
        new("cactus", "선인장", PropPlace.Command, PropShape.Pot, PropSource.Port, 0.02f, 0f, Habit: Habit.ShortTempered, Cost: 1.5f),
        new("globe", "천구의", PropPlace.Command, PropShape.Model, PropSource.Port, 0.02f, 0f, Hobby: Hobby.Stargazing, Cost: 4f),

        // ── 겪은 일에서 생긴다 16 (불 · 추모 · 관행 · 이정표) ──
        new("scorch", "그을린 판", PropPlace.Any, PropShape.Plaque, PropSource.Event, 0f, 0f, Habit: Habit.Serious),
        new("memorial", "추모 액자", PropPlace.Rest, PropShape.Frame, PropSource.Event, 0.02f, 0f, Flammable: true),
        new("candle", "추모 촛불등", PropPlace.Rest, PropShape.Candle, PropSource.Event, 0.01f, 0f, Hours: 18),
        new("extsign", "소화기 자리 안내판", PropPlace.Galley, PropShape.Board, PropSource.Event, 0f, 0f, Habit: Habit.Worrier),
        new("waterslogan", "물 아끼기 표어", PropPlace.Galley, PropShape.Poster, PropSource.Event, 0f, 0f, Habit: Habit.Serious, Flammable: true),
        new("washposter", "손 씻기 포스터", PropPlace.Galley, PropShape.Poster, PropSource.Event, 0f, 0f, Habit: Habit.NeatFreak, Flammable: true),
        new("listenrod", "귀 대는 막대 걸이", PropPlace.Work, PropShape.Board, PropSource.Event, 0f, 0f, Habit: Habit.Tinkerer),
        new("buddychart", "짝 점검표", PropPlace.Airlock, PropShape.Board, PropSource.Event, 0f, 0f, Habit: Habit.Follower, Flammable: true),
        new("tablecloth", "그날의 식탁보", PropPlace.Galley, PropShape.Rug, PropSource.Event, 0.04f, 0f, Flammable: true),
        new("meteorite", "운석 조각", PropPlace.Rest, PropShape.Shelf, PropSource.Event, 0.02f, 0f, Hobby: Hobby.Collecting,
            When: w => w.History.Meteors + w.History.Breaches >= 1 ? "선체를 때린 돌 조각을 주워 왔다" : null),
        new("postcards", "집에서 온 엽서", PropPlace.Sleep, PropShape.Board, PropSource.Event, 0.02f, 0f, Habit: Habit.Homesick, Flammable: true,
            When: w => w.Comms.SupplyDocked ? "보급 캡슐에 집에서 온 엽서가 딸려 왔다" : null),
        new("voyageflag", "항해 완주 깃발", PropPlace.Galley, PropShape.Flag, PropSource.Event, 0.03f, 0f, Habit: Habit.Optimist, Flammable: true,
            When: w => w.Voyage.Past.Count >= 1 ? $"{w.Voyage.Past[^1].To}까지 항해를 마쳤다" : null),
        new("trophy", "대국 트로피", PropPlace.Rest, PropShape.Trophy, PropSource.Event, 0.02f, 0f, Hobby: Hobby.Chess,
            When: w => w.Belongings.Stats.GamesDone >= 3 ? $"체스·카드 판이 {w.Belongings.Stats.GamesDone}번 끝났다" : null),
        new("qualframe", "자격증 액자", PropPlace.Sleep, PropShape.Frame, PropSource.Event, 0.01f, 0f, Habit: Habit.Perfectionist, Flammable: true,
            When: w => w.Life.Stats.QualsEarned >= 1 ? "배에서 자격을 땄다" : null),
        new("birthbanner", "탄생 축하 현수막", PropPlace.Rest, PropShape.Flag, PropSource.Event, 0.03f, 0f, Habit: Habit.Cheerful, Flammable: true, Hours: 72,
            When: w => w.Generation.Births >= 1 ? "배에서 아이가 태어났다" : null),
        new("derelictplaque", "난파선 명판", PropPlace.Command, PropShape.Plaque, PropSource.Event, 0.01f, 0f, Habit: Habit.Superstitious,
            When: w => w.Voyage.Salvaged >= 1 ? "난파선에서 이름판을 떼어 왔다" : null),
    };

    private static readonly Dictionary<string, PropSpec> _byId = All.ToDictionary(p => p.Id);
    public static PropSpec Get(string id) => _byId[id];

    public static string SourceName(PropSource s) => s switch
    {
        PropSource.Start => "처음부터", PropSource.Craft => "승무원이 만든다", PropSource.Port => "기항지 구입", _ => "겪은 일에서",
    };

    public static string PlaceName(PropPlace p) => p switch
    {
        PropPlace.Rest => "쉬는 방", PropPlace.Sleep => "침실", PropPlace.Galley => "주방·식당", PropPlace.Work => "기관·정비",
        PropPlace.Command => "함교·통신", PropPlace.Medical => "의무실", PropPlace.Airlock => "에어락", PropPlace.Corridor => "통로", _ => "어디든",
    };

    /// <summary>표 한 줄의 설명 (화면 · 시험).</summary>
    public static string Describe(PropSpec s)
    {
        var parts = new List<string> { s.Name, PlaceName(s.Place), SourceName(s.Source) };
        if (s.Relax > 0f) parts.Add($"쉼 +{s.Relax * 100:0}%");
        if (s.Sleep > 0f) parts.Add($"잠 +{s.Sleep * 100:0}%");
        if (s.Hobby is Hobby h) parts.Add($"{Persona.Of(h).Name} 취미에 더");
        if (s.Habit is Habit b) parts.Add($"{Persona.Of(b).Name} 버릇에 더");
        return string.Join(" · ", parts);
    }

    /// <summary>그 방에 어울리나.</summary>
    public static bool Fits(PropPlace p, Room r) => p switch
    {
        PropPlace.Rest => r.Type is RoomType.Lounge or RoomType.Mess || RoomCatalog.Has(r.Kind, RoomTag.Rest),
        PropPlace.Sleep => r.Type == RoomType.Quarters || RoomCatalog.Has(r.Kind, RoomTag.Sleep),
        PropPlace.Galley => r.Type is RoomType.Galley or RoomType.Mess,
        PropPlace.Work => r.Type is RoomType.Workshop or RoomType.Engine or RoomType.Power or RoomType.Cooling or RoomType.LifeSupport or RoomType.Reactor or RoomType.Storage,
        PropPlace.Command => r.Type is RoomType.Bridge or RoomType.Comms,
        PropPlace.Medical => r.Type == RoomType.Medbay,
        PropPlace.Airlock => r.Type == RoomType.Airlock,
        PropPlace.Corridor => r.Type == RoomType.Corridor,
        _ => r.Type != RoomType.Corridor,
    };

    /// <summary>방 하나에 둘 수 있는 수 (넓을수록 많이 — 두 개에서 여섯 개).</summary>
    public static int Cap(Room r) => Math.Clamp(r.Cells.Count / 6, 2, 6);

    /// <summary>쉬는 효과 (곱): 놓인 소품의 쉼을 더한다 (많아도 15%까지).</summary>
    public static float RelaxMul(Room? room)
    {
        if (room == null || room.Decor.Count == 0) return 1f;
        float s = 0f;
        foreach (var p in room.Decor) s += p.Spec.Relax;
        return 1f + MathF.Min(RelaxCap, s);
    }

    /// <summary>잠의 질 (더함): 베개·수면등·모빌… (많아도 6%까지).</summary>
    public static float SleepAdd(Room? room)
    {
        if (room == null || room.Decor.Count == 0) return 0f;
        float s = 0f;
        foreach (var p in room.Decor) s += p.Spec.Sleep;
        return MathF.Min(SleepCap, s);
    }
}

public sealed class PropStats
{
    public int Start, Made, Bought, Events, Burned, Expired, Soothed;
    public float Eased;
    public string Summary() =>
        $"처음 {Start} · 만듦 {Made} · 삼 {Bought} · 겪은 일에서 {Events} · 탐 {Burned} · 치움 {Expired} · 마음이 놓임 {Soothed}번 (스트레스 {Eased * 100:0}%p 덜)";
}

/// <summary>소품을 만들고 · 사고 · 겪은 일에서 걸고 · 타고 · 곁의 사람을 달랜다 (시스템 틱).</summary>
public sealed class PropSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7027 + 61));
    public PropStats Stats { get; } = new();
    /// <summary>배에 놓인 소품 (놓은 순서).</summary>
    public List<PlacedProp> Placed { get; } = new();
    private int _nextId;
    private bool _started;
    private long _nextHour = -1, _nextCraft = -1;
    private int[] _fireSeen = Array.Empty<int>();
    private readonly List<(int room, long at)> _scorch = new();
    private int _memorialSeen;
    private readonly List<(string name, string cause, long at)> _frames = new();
    private readonly HashSet<CustomKind> _customs = new();
    private readonly HashSet<string> _milestones = new();
    private int _portSeen;
    private long _candleKey = -1;
    private readonly Dictionary<int, long> _noticed = new();

    public PropSystem(World w) => _w = w;

    public int Count(PropSpec s) { int n = 0; foreach (var p in Placed) if (p.Spec == s) n++; return n; }
    public IEnumerable<PlacedProp> In(Room r) => r.Decor;

    public void Update(float dt)
    {
        var w = _w;
        if (!_started) { _started = true; Furnish(); }
        Soothe(dt);
        if (w.Tick >= _nextHour) { _nextHour = w.Tick + SimTime.TicksPerHour; Hourly(); }
        if (_nextCraft < 0) _nextCraft = w.Tick + SimTime.Hours(3f + 4f * R.Float());
        if (w.Tick >= _nextCraft)
        {
            _nextCraft = w.Tick + SimTime.Hours(6f + 6f * R.Float()); // 하루에 한두 번 — 만드는 데는 품이 든다
            if (!Crisis.Acting(w)) Craft();
        }
    }

    // ───────────────────────────── 놓는다 ─────────────────────────────

    private static bool Usable(Room r) => !r.OffLimits && !r.Abandoned;

    /// <summary>방 안의 빈 자리: 벽 소품은 벽에 닿은 칸, 바닥 소품은 지나다닐 수 있는 빈 칸.</summary>
    private (Cell at, int wall)? Spot(Room room, bool wall, Cell? near)
    {
        var ship = _w.Ship;
        var g = ship.Grid;
        var used = new HashSet<Cell>(room.Decor.Select(p => p.At));
        var list = new List<(Cell, int)>();
        foreach (var c in room.Cells)
        {
            if (used.Contains(c) || g.Kind(c) != TileKind.Floor || ship.FurnitureAt(c) != null) continue;
            if (wall)
            {
                int dir = -1;
                for (int i = 0; i < 4 && dir < 0; i++)
                {
                    var n = c + Cell.Dirs4[i];
                    if (g.Kind(n) == TileKind.Wall && ship.DoorAt(n) == null) dir = i;
                }
                if (dir >= 0) list.Add((c, dir));
            }
            else if (ship.IsWalkable(c)) list.Add((c, -1));
        }
        if (list.Count == 0) return null;
        if (near is Cell nc) return list.OrderBy(x => Math.Abs(x.Item1.X - nc.X) + Math.Abs(x.Item1.Y - nc.Y)).ThenBy(x => x.Item1.Y).ThenBy(x => x.Item1.X).First();
        return list[R.Range(0, list.Count)];
    }

    /// <summary>소품 하나를 그 방에 놓는다 (자리가 없으면 null). 기록은 부른 쪽이 남긴다.</summary>
    public PlacedProp? Place(PropSpec spec, Room room, CrewMember? by, string origin, string? label = null, Cell? near = null)
    {
        if (Spot(room, spec.Wall, near) is not var (at, dir)) return null;
        var p = new PlacedProp
        {
            Id = _nextId++, Spec = spec, RoomId = room.Id, At = at, Wall = dir, Maker = by?.Id ?? -1, Placed = _w.Tick,
            Until = spec.Hours > 0 ? _w.Tick + SimTime.Hours(spec.Hours) : -1, Origin = origin, Label = label,
        };
        room.Decor.Add(p);
        Placed.Add(p);
        MarkLog.Add(room.Marks, _w.Tick, by != null ? $"{by.Name}: {p.Name}" : p.Name);
        return p;
    }

    /// <summary>시험·화면용: 이름으로 놓는다.</summary>
    public PlacedProp? Place(string id, Room room, string origin = "") => Place(Props.Get(id), room, null, origin);

    private void Remove(PlacedProp p)
    {
        if (p.RoomId >= 0 && p.RoomId < _w.Ship.Rooms.Count) _w.Ship.Rooms[p.RoomId].Decor.Remove(p);
        Placed.Remove(p);
    }

    /// <summary>처음부터 있던 것: 방마다 어울리는 것 두어 개.</summary>
    private void Furnish()
    {
        var starts = Props.All.Where(s => s.Source == PropSource.Start).ToList();
        foreach (var room in _w.Ship.Rooms)
        {
            if (!Usable(room)) continue;
            var fit = starts.Where(s => Props.Fits(s.Place, room)).ToList();
            int n = Math.Min(2, Props.Cap(room));
            while (fit.Count > 0 && n-- > 0)
            {
                var s = fit[R.Range(0, fit.Count)];
                fit.Remove(s);
                if (Place(s, room, null, "출항 때부터") != null) Stats.Start++;
            }
        }
    }

    private bool Free(CrewMember c) =>
        !c.Dead && !c.Down && !c.Outside && c.Room != null && c.IsAwake && c.CanAct && c.Job?.Urgent != true && c.Pose != Pose.Working;

    private bool Adult(CrewMember c) => !c.Dead && !c.Down && !c.Outside && !c.IsChild && c.IsAwake && c.CanAct;

    /// <summary>이 사람이 그 소품을 좋아하나 (습관 · 취미 · 손수 만든 것 · 슬픔 중의 추모 액자).</summary>
    public bool Likes(CrewMember c, PlacedProp p) =>
        p.Spec.Habit is Habit h && c.Habits.Contains(h) || p.Spec.Hobby is Hobby b && c.Hobbies.Contains(b)
        || p.Maker == c.Id && p.Spec.Source == PropSource.Craft || p.Spec.Id == "memorial" && c.GriefUntil > _w.Tick;

    private bool Fan(CrewMember c, PropSpec s) => s.Habit is Habit h && c.Habits.Contains(h) || s.Hobby is Hobby b && c.Hobbies.Contains(b);

    /// <summary>어느 방에 둘까: 좋아할 사람의 침실 → 만든 사람이 있는 방 → 그 사람의 침실 → 덜 꾸민 방.</summary>
    private Room? RoomFor(CrewMember c, PropSpec s, CrewMember? fan, int extra = 0)
    {
        bool Ok(Room? r) => r != null && Usable(r) && Props.Fits(s.Place, r) && r.Decor.Count < Props.Cap(r) + extra;
        if (s.Place == PropPlace.Sleep && fan?.Bed?.Room is Room fr && Ok(fr)) return fr;
        if (Ok(c.Room)) return c.Room;
        if (s.Place == PropPlace.Sleep && c.Bed?.Room is Room br && Ok(br)) return br;
        return _w.Ship.Rooms.Where(r => Ok(r)).OrderBy(r => r.Decor.Count).ThenBy(r => r.Id).FirstOrDefault();
    }

    // ───────────────────────────── 만든다 ─────────────────────────────

    /// <summary>한가한 사람이 취미로 소품을 만든다 (재료를 쓴다) — 좋아할 동료를 떠올리며.</summary>
    private void Craft()
    {
        var w = _w;
        var options = new List<(CrewMember c, PropSpec s, float wt)>();
        foreach (var c in w.Crew)
        {
            if (!Free(c)) continue;
            foreach (var s in Props.All)
            {
                if (s.Source != PropSource.Craft || s.Kid != c.IsChild || Count(s) >= 2) continue;
                if (s.Maker is Hobby mh && !c.Hobbies.Contains(mh)) continue;
                if (s.Material is ItemKind m && w.Ship.CountStored(m) <= 0) continue;
                float wt = (s.Maker != null || s.Kid ? 3f : 1f) * (c.Job?.Activity is RelaxActivity or HobbyActivity ? 1.5f : 1f);
                options.Add((c, s, wt));
            }
        }
        if (options.Count == 0) return;
        float pick = R.Float() * options.Sum(o => o.wt);
        var (maker, spec, _) = options[^1];
        foreach (var o in options) { pick -= o.wt; if (pick <= 0f) { (maker, spec) = (o.c, o.s); break; } }
        if (!w.Scenes.Craft(maker, spec)) Make(maker, spec); // v16.1 진척 · 운반 · 설치 (장면을 못 열면 예전처럼 바로)
    }

    /// <summary>v16.1 만들기 전에 어디 둘지 정한다 (좋아할 사람 → 방).</summary>
    internal Room? Destination(CrewMember maker, PropSpec spec)
    {
        var fan = _w.Crew.Where(o => o != maker && !o.Dead && Fan(o, spec)).OrderByDescending(o => maker.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
        return RoomFor(maker, spec, fan != null && maker.AffinityTo(fan) >= -0.2f ? fan : null);
    }

    /// <summary>그 사람이 그 소품을 만들어 둔다 (시험에서도 부른다).</summary>
    public PlacedProp? Make(CrewMember maker, PropSpec spec, Room? into = null, bool paid = false, Cell? near = null) // v16.1 장면: 정한 방 · 이미 쓴 재료 · 선 자리
    {
        var w = _w;
        var fans = w.Crew.Where(o => o != maker && !o.Dead && Fan(o, spec)).ToList();
        var fan = fans.Count == 0 ? null : fans.OrderByDescending(o => maker.AffinityTo(o)).ThenBy(o => o.Id).First();
        if (fan != null && maker.AffinityTo(fan) < -0.2f) fan = null;
        var room = into ?? RoomFor(maker, spec, fan);
        if (room == null) return null;
        if (!paid && spec.Material is ItemKind m && !ItemsV15.Use(w, m)) return null;
        string why = fan != null ? $"{Ko.IGa(fan.Name)} 좋아할 것 같아서"
            : maker.Needs.Stress > 0.45f ? "마음을 달래려고"
            : room.Decor.Count == 0 ? $"{Ko.IGa(room.Name)} 휑해서"
            : maker.IsChild ? "그리고 싶어서"
            : spec.Maker is Hobby h ? $"{Persona.Of(h).Name} 취미 삼아" : "손이 심심해서";
        var p = Place(spec, room, maker, why, near: near);
        if (p == null) return null;
        Stats.Made++;
        maker.Needs.Stress = MathF.Max(0f, maker.Needs.Stress - 0.04f); // 만드는 보람
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(maker.Name)} {Ko.EulReul(spec.Name)} 만들어 {room.Name}에 두었다 — {why}", maker.Id);
        Life.Diary(w, maker, Persona.Say(maker, $"{Ko.EulReul(spec.Name)} 만들어 {room.Name}에 두었다" + (fan != null ? $". {Ko.IGa(fan.Name)} 좋아하면 좋겠다" : "")));
        if (fan != null)
        {
            fan.ChangeAffinity(maker, 0.03f);
            maker.ChangeAffinity(fan, 0.01f);
            w.Relations.Remember(fan, maker, RelationReason.GaveMeGift, $"{room.Name}에 {Ko.EulReul(spec.Name)} 만들어 두었다");
            Life.Diary(w, fan, Persona.Say(fan, $"{Ko.IGa(maker.Name)} {room.Name}에 {Ko.EulReul(spec.Name)} 두었다. 나 보라고 한 것 같다"));
        }
        return p;
    }

    // ───────────────────────────── 겪은 일 ─────────────────────────────

    private void Hourly()
    {
        Tidy();
        Fires();
        Memorials();
        Customs();
        Milestones();
        Port();
    }

    /// <summary>기한이 지난 것은 치우고, 방이 나뉘었으면 새 방으로 옮겨 센다.</summary>
    private void Tidy()
    {
        var ship = _w.Ship;
        foreach (var p in Placed.ToList())
        {
            if (p.Until >= 0 && _w.Tick >= p.Until) { Remove(p); Stats.Expired++; continue; }
            var r = ship.RoomAt(p.At);
            if (r == null) { Remove(p); continue; }
            if (r.Id != p.RoomId)
            {
                ship.Rooms[p.RoomId].Decor.Remove(p);
                p.RoomId = r.Id;
                r.Decor.Add(p);
            }
        }
    }

    /// <summary>불: 그 방의 종이·천 소품이 타고, 꺼진 뒤 누군가 그을린 벽판을 떼어 걸어 둔다.</summary>
    private void Fires()
    {
        var w = _w;
        var rooms = w.Ship.Rooms;
        if (_fireSeen.Length < rooms.Count) Array.Resize(ref _fireSeen, rooms.Count);
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].Fires <= _fireSeen[i]) continue;
            _fireSeen[i] = rooms[i].Fires;
            Burn(rooms[i]);
            if (!_scorch.Any(s => s.room == i)) _scorch.Add((i, w.Tick));
        }
        for (int k = 0; k < _scorch.Count; k++)
        {
            var (id, at) = _scorch[k];
            var room = rooms[id];
            if (w.Tick - at < SimTime.Hours(1) || w.Fire.BurningHours(room) > 0f) continue;
            var c = w.Crew.Where(Adult).OrderByDescending(x => x.Room == room).ThenByDescending(x => x.RawSkill(Skill.Mechanics)).ThenBy(x => x.Id).FirstOrDefault();
            if (c == null) continue;
            _scorch.RemoveAt(k--);
            var to = Usable(room) ? room : RoomFor(c, Props.Get("tablecloth"), null, 2);
            if (to == null || to.Decor.Count(p => p.Spec.Id == "scorch") >= 2) continue; // 한 방에 둘까지 (그 뒤로는 있는 판이 기억한다)
            string label = $"{SimTime.Day(at)}일 불";
            if (Place(Props.Get("scorch"), to, c, $"{room.Name} 화재", label) == null) continue;
            Stats.Events++;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 그을린 벽판을 떼어 '{label}'이라 새겨 {to.Name}에 걸었다 — 그날을 잊지 않으려고", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"{room.Name}의 그을린 벽판을 걸어 두었다. 다시는 그런 일이 없기를"));
        }
    }

    private void Burn(Room room)
    {
        var w = _w;
        var burnt = room.Decor.Where(p => p.Spec.Flammable).ToList();
        if (burnt.Count == 0) return;
        foreach (var p in burnt)
        {
            Remove(p);
            Stats.Burned++;
            if (p.Maker >= 0 && p.Maker < w.Crew.Count && w.Crew[p.Maker] is { Dead: false } mk)
            {
                mk.Needs.Stress = MathF.Min(1f, mk.Needs.Stress + 0.03f);
                Life.Diary(w, mk, Persona.Say(mk, $"내가 만든 {Ko.IGa(p.Spec.Name)} 불에 탔다"));
            }
            if (p.Spec.Id == "memorial" && p.Label != null) _frames.Add((p.Label, "불에 탄 액자를 다시", w.Tick + SimTime.Hours(6)));
        }
        w.Log.Add(w.Tick, LogKind.Life, $"{room.Name} 불에 {Ko.IGa(string.Join("·", burnt.Select(p => p.Name)))} 탔다");
    }

    /// <summary>떠난 사람: 장례 뒤 가까웠던 사람이 추모 액자를 건다 · 추모일엔 그 앞에 촛불등을 켠다.</summary>
    private void Memorials()
    {
        var w = _w;
        var mem = w.Life.Memorial;
        while (_memorialSeen < mem.Count)
        {
            var (name, tick, cause) = mem[_memorialSeen++];
            _frames.Add((name, cause, tick + SimTime.Hours(16)));
        }
        for (int k = 0; k < _frames.Count; k++)
        {
            var (name, cause, at) = _frames[k];
            if (w.Tick < at) continue;
            if (Frame(name, cause) != null) _frames.RemoveAt(k--);
        }
        // 추모일 (관행): 액자가 없으면 먼저 걸고, 그 앞에 촛불등
        if (w.Culture.Of(CustomKind.Memorial) is Custom cu && cu.Honoree != null && w.Culture.IsDay(CustomKind.Memorial) && _candleKey != cu.NextDay)
        {
            var frame = Placed.FirstOrDefault(p => p.Spec.Id == "memorial" && p.Label == cu.Honoree) ?? Frame(cu.Honoree, "추모일");
            var c = w.Crew.Where(x => Adult(x) && cu.Followers.Contains(x.Id)).OrderBy(x => x.Id).FirstOrDefault() ?? w.Crew.Where(Adult).OrderBy(x => x.Id).FirstOrDefault();
            if (frame != null && c != null)
            {
                _candleKey = cu.NextDay;
                var room = w.Ship.Rooms[frame.RoomId];
                if (Place(Props.Get("candle"), room, c, "추모일", cu.Honoree, frame.At) != null)
                {
                    Stats.Events++;
                    w.Log.Add(w.Tick, LogKind.Life, $"{cu.Honoree}의 추모일 — {Ko.IGa(c.Name)} 추모 액자 앞에 촛불등을 켰다", c.Id);
                    Life.Diary(w, c, Persona.Say(c, $"{cu.Honoree}의 사진 앞에 촛불등을 켜 두었다"));
                }
            }
        }
    }

    private PlacedProp? Frame(string name, string cause)
    {
        var w = _w;
        if (Placed.Any(p => p.Spec.Id == "memorial" && p.Label == name)) return Placed.First(p => p.Spec.Id == "memorial" && p.Label == name);
        var dead = w.Crew.FirstOrDefault(c => c.Dead && c.Name == name);
        var c = w.Crew.Where(Adult).OrderByDescending(x => dead == null ? 0f : x.AffinityTo(dead)).ThenBy(x => x.Id).FirstOrDefault();
        if (c == null) return null;
        var spec = Props.Get("memorial");
        var grief = Facilities.Best(w.Ship, "grief").room;
        var room = grief != null && Usable(grief) && grief.Decor.Count < Props.Cap(grief) + 2 ? grief : RoomFor(c, spec, null, 2);
        if (room == null) return null;
        var p = Place(spec, room, c, cause, name);
        if (p == null) return null;
        Stats.Events++;
        w.History.Add(w, HistoryKind.Memory, $"{Ko.IGa(c.Name)} {room.Name}에 {name}의 추모 액자를 걸었다", room, new[] { c }, log: true);
        Life.Diary(w, c, Persona.Say(c, $"{name}의 사진을 액자에 넣어 {room.Name}에 걸었다"));
        foreach (var o in w.Crew.Where(o => !o.Dead && o.GriefUntil > w.Tick)) o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.03f);
        return p;
    }

    private static string? CustomProp(CustomKind k) => k switch
    {
        CustomKind.FireCheck => "extsign", CustomKind.WaterThrift => "waterslogan", CustomKind.HandWash => "washposter",
        CustomKind.MaintainerWay => "listenrod", CustomKind.HatchBuddy => "buddychart", CustomKind.SurvivalMeal => "tablecloth", _ => null,
    };

    /// <summary>관행이 생기면 따르는 사람이 그걸 적어 붙인다 (몇 시간 뒤).</summary>
    private void Customs()
    {
        var w = _w;
        foreach (var cu in w.Culture.Customs)
        {
            if (_customs.Contains(cu.Kind) || w.Tick - cu.Born < SimTime.Hours(3) || CustomProp(cu.Kind) is not string id) continue;
            var c = w.Crew.Where(x => Adult(x) && cu.Followers.Contains(x.Id)).OrderByDescending(x => cu.Knowers.Contains(x.Id)).ThenBy(x => x.Id).FirstOrDefault();
            if (c == null) continue;
            var spec = Props.Get(id);
            var room = RoomFor(c, spec, null, 2);
            if (room == null) continue;
            _customs.Add(cu.Kind);
            if (Place(spec, room, c, $"관행 — {CultureSystem.Name(cu.Kind)}") == null) continue;
            Stats.Events++;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {room.Name}에 {Ko.EulReul(spec.Name)} 붙였다 — 이 배에선 {CultureSystem.Name(cu.Kind)}", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"{Ko.EulReul(spec.Name)} 써 붙였다. {cu.Origin}"));
        }
    }

    /// <summary>이정표: 운석 · 보급 · 항해 완주 · 대국 · 자격 · 탄생 · 난파선 — 한 번씩.</summary>
    private void Milestones()
    {
        var w = _w;
        foreach (var s in Props.All)
        {
            if (s.When == null || _milestones.Contains(s.Id) || s.When(w) is not string why) continue;
            var c = w.Crew.Where(Adult).OrderByDescending(x => Fan(x, s)).ThenBy(x => x.Id).FirstOrDefault();
            if (c == null) continue;
            var room = RoomFor(c, s, null, 1);
            if (room == null) continue;
            _milestones.Add(s.Id);
            if (Place(s, room, c, why) == null) continue;
            Stats.Events++;
            w.Log.Add(w.Tick, LogKind.Life, $"{why} — {Ko.IGa(c.Name)} {Ko.EulReul(s.Name)} {room.Name}에 두었다", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"{Ko.EulReul(s.Name)} {room.Name}에 두었다. {why}"));
        }
    }

    /// <summary>기항지: 마음이 지친 사람이나 좋아하는 사람이 한두 개 사 온다 (아껴 둘 돈은 남긴다).</summary>
    private void Port()
    {
        var w = _w;
        var v = w.Voyage;
        if (v.Legs.Count == 0 || v.Current.Kind != LegKind.Port || v.PortsVisited <= _portSeen) return;
        _portSeen = v.PortsVisited;
        var wares = Props.All.Where(s => s.Source == PropSource.Port && Count(s) == 0).ToList();
        for (int n = 0; n < 2 && wares.Count > 0; n++)
        {
            var s = wares[R.Range(0, wares.Count)];
            wares.Remove(s);
            if (v.Credits - v.Reserve < s.Cost) continue;
            var c = w.Crew.Where(Adult).OrderByDescending(x => Fan(x, s)).ThenByDescending(x => x.Needs.Stress).ThenBy(x => x.Id).FirstOrDefault();
            if (c == null) return;
            var room = RoomFor(c, s, c);
            if (room == null || Place(s, room, c, $"{v.Current.Name}에서 샀다") == null) continue;
            v.Credits -= s.Cost;
            Stats.Bought++;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {v.Current.Name}에서 {Ko.EulReul(s.Name)} 사 와 {room.Name}에 두었다 (돈 {s.Cost:0.#})", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"{v.Current.Name}에서 {Ko.EulReul(s.Name)} 샀다. 배가 조금 집 같아졌으면"));
        }
    }

    // ───────────────────────────── 곁에서 ─────────────────────────────

    /// <summary>좋아하는 소품 곁에서는 마음이 더 놓인다 (쉬면 더, 일하면 덜, 자면 조금).</summary>
    private void Soothe(float dt)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside || c.Room is not Room r || r.Decor.Count == 0) continue;
            int n = 0;
            PlacedProp? first = null;
            foreach (var p in r.Decor)
                if (Likes(c, p)) { n++; first ??= p; if (n >= 2) break; }
            if (n == 0 || first == null) continue;
            float rate = c.Pose == Pose.Sleeping ? 0.01f : c.Pose == Pose.Working ? 0.015f : 0.03f;
            float before = c.Needs.Stress;
            c.Needs.Stress = MathF.Max(0f, before - rate * n * dt);
            Stats.Eased += before - c.Needs.Stress;
            if (!c.IsAwake || before < 0.15f || _noticed.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.TicksPerDay || !R.Chance(0.3f * dt)) continue;
            _noticed[c.Id] = w.Tick;
            Stats.Soothed++;
            Life.Diary(w, c, Persona.Say(c, first.Spec.Id == "memorial" ? $"{first.Label}의 사진 앞에 잠깐 서 있었다"
                : first.Maker == c.Id ? $"{r.Name}에 둔 내 {first.Spec.Name}, 볼 때마다 뿌듯하다"
                : $"{r.Name}의 {Ko.EulReul(first.Spec.Name)} 보니 마음이 좀 놓인다"));
        }
    }
}
