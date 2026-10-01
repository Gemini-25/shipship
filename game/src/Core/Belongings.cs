using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v14.3 취미의 실제 행동 · 개인 물건 (레데리2 · 림월드급 디테일)
//
// 물건마다 주인과 이력이 있다 — 누가 만들었나 · 누구에게 받았나 · 어떤 사고에서 건졌나.
// 취미는 물건으로 한다: 사물함에서 꺼내 와 → 알맞은 자리에서 하고 → 경보가 나면 펼친 채 그 자리에 두고 → 나중에 돌아와 이어 하고 → 제자리에 돌려놓는다.
// 체스·카드는 상대를 기다리고 실제로 판이 진행되며, 끊긴 판은 다음에 이어진다. 그림·모형·뜨개는 조금씩 완성되어 방에 남고, 감상되고, 선물된다.
// 악기는 곁의 사람이 듣고 함께 하기도 하고, 밤에는 옆에서 자던 사람이 조용히 해 달라고 한다.
// 물에 젖거나 불에 탄 물건은 주인의 생활을 바꾸고, 솜씨 있는 친구가 고쳐 주기도 한다.
// 공구를 두고 나오면 동료 것을 빌리고, 돌려놓는지에 따라 사이가 달라진다. 떠난 사람의 공구는 제자에게, 사진은 가족에게 간다.

public enum BelongingKind
{
    Book, Instrument, ChessSet, Cards, Sketchbook, ModelKit, Knitting, Journal, Camera, Puzzle,
    GameDevice, TeaSet, CollectionBox, PlantPot, Dumbbells, YogaMat, Headphones,
    Mug, Toolset, Photo, Blanket, Artwork,
}

public sealed record BelongingSpec(BelongingKind Kind, string Name, bool Fragile, bool Flammable, string Doing);

public sealed class Belonging
{
    public int Id { get; init; }
    public BelongingKind Kind { get; init; }
    public string Name { get; set; } = "";
    public int Owner { get; set; } = -1;
    public int Maker { get; set; } = -1;
    public int From { get; set; } = -1;
    public string Origin { get; set; } = "";
    public List<Mark> Marks { get; } = new();
    public float Condition { get; set; } = 1f;
    /// <summary>읽은 만큼 · 작품 진척 (0~1).</summary>
    public float Progress { get; set; }
    /// <summary>들고 있는 사람 (-1: 아무도).</summary>
    public int Holder { get; set; } = -1;
    /// <summary>놓인 칸 (null이면 주인 침대 곁 사물함).</summary>
    public Cell? At { get; set; }
    /// <summary>펼친 채 내려놓았다 (돌아와 이어 한다).</summary>
    public bool Open { get; set; }
    public long OpenSince { get; set; } = -1;
    public int BorrowedBy { get; set; } = -1;
    public long BorrowedAt { get; set; } = -1;
    /// <summary>망가진 걸 주인이 아직 모른다.</summary>
    public bool Unnoticed { get; set; }
    /// <summary>떠난 사람의 물건 (기린다).</summary>
    public bool Memorial { get; set; }
    public bool Usable => Condition >= 0.3f;
}

/// <summary>체스·카드 한 판 (상대를 기다리고, 끊기면 이어 둔다).</summary>
public sealed class HobbyGame
{
    public int Id { get; init; }
    public Hobby Kind { get; init; }
    public int A { get; init; }
    public int B { get; set; } = -1;
    public int Item { get; init; } = -1;
    public Cell Table { get; init; }
    public float Moves { get; set; }
    public float Target { get; init; }
    public long Started { get; init; }
    public long LastPlayed { get; set; }
    public bool Done { get; set; }
    public int Winner { get; set; } = -1;
    public int Breaks { get; set; }
}

/// <summary>지금 하고 있는 취미 (곁의 사람이 듣고 · 함께 하고 · 조용히 해 달라 한다).</summary>
public sealed class HobbySession
{
    public int Crew { get; init; }
    public Hobby Hobby { get; init; }
    public Cell Spot { get; init; }
    public int Item { get; init; } = -1;
    public long Started { get; init; }
    public bool Hushed { get; set; }
    public HashSet<int> Listeners { get; } = new();
}

public sealed class BelongingStats
{
    public int ToolJobs; // 공구가 드는 일을 시작한 번 (기술자)
    public int Sessions, Resumed, PutDown, Returned, LeftOut, Games, GamesDone, GamesResumed, Artworks, Gifts, Admired, Listened, Jams, Hushed,
        Ruined, Noticed, Mended, Borrows, Unreturned, Reclaims, ToolsLeft, Retrieved, Tidied, Inherited, PhotoLooks, Rescued;
    public override string ToString() =>
        $"취미 {Sessions}(이어 함 {Resumed} · 펼친 채 둠 {PutDown} · 제자리 {Returned} · 그냥 둠 {LeftOut}) · 판 {Games}(끝남 {GamesDone} · 이어 둠 {GamesResumed}) · 작품 {Artworks}(선물 {Gifts} · 감상 {Admired}) · 들음 {Listened} · 합주 {Jams} · 조용히 {Hushed} · 망가짐 {Ruined}(앎 {Noticed} · 고침 {Mended}) · 빌림 {Borrows}(안 돌려줌 {Unreturned} · 되찾음 {Reclaims}) · 공구 두고 옴 {ToolsLeft} · 찾아옴 {Retrieved} · 치워 줌 {Tidied} · 물려받음 {Inherited} · 사진 {PhotoLooks} · 건짐 {Rescued}";
}

public sealed class BelongingSystem
{
    public static readonly BelongingSpec[] Specs =
    {
        new(BelongingKind.Book, "책", true, true, "책을 읽는다"),
        new(BelongingKind.Instrument, "기타", false, true, "기타를 친다"),
        new(BelongingKind.ChessSet, "체스판", false, true, "체스를 둔다"),
        new(BelongingKind.Cards, "카드", true, true, "카드를 친다"),
        new(BelongingKind.Sketchbook, "스케치북", true, true, "그림을 그린다"),
        new(BelongingKind.ModelKit, "모형 공구함", false, false, "모형을 만든다"),
        new(BelongingKind.Knitting, "뜨개 바구니", true, true, "뜨개질을 한다"),
        new(BelongingKind.Journal, "공책", true, true, "글을 쓴다"),
        new(BelongingKind.Camera, "카메라", true, false, "별을 찍는다"),
        new(BelongingKind.Puzzle, "퍼즐 상자", true, true, "퍼즐을 맞춘다"),
        new(BelongingKind.GameDevice, "휴대 게임기", true, false, "게임을 한다"),
        new(BelongingKind.TeaSet, "찻잔 세트", false, false, "차를 우린다"),
        new(BelongingKind.CollectionBox, "수집품 상자", false, true, "수집품을 정리한다"),
        new(BelongingKind.PlantPot, "화분", false, true, "화분을 돌본다"),
        new(BelongingKind.Dumbbells, "아령", false, false, "근력 운동을 한다"),
        new(BelongingKind.YogaMat, "요가 매트", true, true, "몸을 푼다"),
        new(BelongingKind.Headphones, "헤드폰", true, false, "음악을 듣는다"),
        new(BelongingKind.Mug, "컵", false, false, ""),
        new(BelongingKind.Toolset, "공구 가방", false, false, ""),
        new(BelongingKind.Photo, "사진", true, true, "사진을 본다"),
        new(BelongingKind.Blanket, "담요", true, true, ""),
        new(BelongingKind.Artwork, "작품", true, true, ""),
    };

    public static BelongingSpec Spec(BelongingKind k) => Specs[(int)k];

    /// <summary>취미에 쓰는 물건 (없으면 맨몸으로 하는 취미).</summary>
    public static BelongingKind? ItemFor(Hobby h) => h switch
    {
        Hobby.Reading => BelongingKind.Book, Hobby.Instrument => BelongingKind.Instrument, Hobby.Chess => BelongingKind.ChessSet,
        Hobby.Cards => BelongingKind.Cards, Hobby.Painting => BelongingKind.Sketchbook,
        Hobby.ModelBuilding or Hobby.Woodwork or Hobby.Electronics => BelongingKind.ModelKit,
        Hobby.Knitting => BelongingKind.Knitting, Hobby.Writing or Hobby.Journaling => BelongingKind.Journal,
        Hobby.Photography => BelongingKind.Camera, Hobby.Puzzles => BelongingKind.Puzzle,
        Hobby.Games or Hobby.VirtualReality => BelongingKind.GameDevice, Hobby.Tea => BelongingKind.TeaSet,
        Hobby.Collecting => BelongingKind.CollectionBox, Hobby.Gardening => BelongingKind.PlantPot,
        Hobby.Workout => BelongingKind.Dumbbells, Hobby.Yoga => BelongingKind.YogaMat, Hobby.Music or Hobby.Movies => BelongingKind.Headphones,
        _ => null,
    };

    /// <summary>작품이 남는 취미 (완성되면 방에 놓인다).</summary>
    public static bool Crafts(Hobby h) => h is Hobby.Painting or Hobby.ModelBuilding or Hobby.Woodwork or Hobby.Electronics or Hobby.Knitting or Hobby.Photography;
    public static bool Board(Hobby h) => h is Hobby.Chess or Hobby.Cards;
    public static bool Exercise(Hobby h) => h is Hobby.Workout or Hobby.Yoga or Hobby.Walking or Hobby.Dancing;

    private static readonly string[] BookTitles =
    {
        "『별을 건너는 법』", "『소금과 바다』", "『마지막 정거장』", "『고요한 궤도』", "『잃어버린 지도』", "『겨울의 기관사』", "『붉은 행성 일기』", "『작은 배의 노래』",
        "『두 번째 해돋이』", "『녹슨 나침반』", "『어둠 속의 정원』", "『천 개의 밤』", "『무중력 요리책』", "『우주 항법 입문』", "『바람이 없는 곳』", "『빛의 속도로 쓴 편지』",
    };
    private static readonly string[] ArtTitles =
    {
        "고향의 아침", "궤도 위의 정원", "창밖의 성운", "엔진실의 오후", "잠든 동료", "첫 수확", "검은 바다", "기항지의 불빛", "떠나온 집", "푸른 별", "작은 배", "등대",
    };
    private static readonly string[] Adjectives = { "낡은", "손때 묻은", "빨간", "파란", "새", "작은", "아끼는", "물려받은", "닳은", "반짝이는" };

    private readonly World _w;
    private readonly Rng _rng;
    private int _nextId, _nextGame;
    private float _clock;
    public List<Belonging> All { get; } = new();
    public List<HobbyGame> Games { get; } = new();
    public Dictionary<int, HobbySession> Sessions { get; } = new();
    public BelongingStats Stats { get; } = new();

    public BelongingSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 8017 + 59));
    }

    // ───────────────────────────── 찾기 ─────────────────────────────

    public IEnumerable<Belonging> Of(CrewMember c) => All.Where(b => b.Owner == c.Id);
    public Belonging? Get(int id) => id < 0 ? null : All.FirstOrDefault(b => b.Id == id);
    private CrewMember? CrewOf(int id) => id < 0 ? null : _w.Crew.FirstOrDefault(c => c.Id == id);

    /// <summary>사물함 자리: 주인 침대 곁 (없으면 침실 가운데 근처).</summary>
    public Cell Home(Belonging b)
    {
        var owner = CrewOf(b.Owner);
        if (owner?.Bed is Furniture bed && bed.UseSpots.Count > 0 && !bed.Room.Detached) return bed.UseSpots[0];
        var q = _w.Ship.RoomsOf(RoomType.Quarters).FirstOrDefault(r => !r.Detached) ?? _w.Ship.Rooms.First(r => !r.Detached);
        return q.Cells.Where(c => _w.Ship.IsOpenFloor(c)).OrderBy(c => (c.Center - q.Center).LengthSquared()).FirstOrDefault();
    }

    /// <summary>지금 그 물건이 있는 칸.</summary>
    public Cell CellOf(Belonging b) =>
        b.Holder >= 0 && CrewOf(b.Holder) is CrewMember h ? h.Cell : b.At ?? Home(b);

    public string Where(Belonging b)
    {
        if (b.Holder >= 0) return b.Holder == b.Owner ? "들고 있다" : $"{Ko.IGa(CrewOf(b.Holder)?.Name ?? "?")} 들고 있다";
        if (b.BorrowedBy >= 0) return $"{Ko.IGa(CrewOf(b.BorrowedBy)?.Name ?? "?")} 빌려 갔다";
        if (b.At is Cell c) return $"{_w.Ship.RoomAt(c)?.Name ?? "?"}에 {(b.Open ? "펼친 채 " : b.Kind == BelongingKind.Artwork ? "걸려 " : "")}있다";
        return "사물함";
    }

    /// <summary>그 취미에 쓸 물건 (자기 것, 쓸 만한 것).</summary>
    public Belonging? ItemFor(CrewMember c, Hobby h) =>
        ItemFor(h) is BelongingKind k ? All.FirstOrDefault(b => b.Owner == c.Id && b.Kind == k && b.Usable && (b.BorrowedBy < 0 || b.BorrowedBy == c.Id) && (b.Holder < 0 || b.Holder == c.Id)) : null;

    private void Mark(Belonging b, string text) => MarkLog.Add(b.Marks, _w.Tick, text);

    // ───────────────────────────── 처음 가진 것 ─────────────────────────────

    public void Seed(CrewMember c)
    {
        if (All.Any(b => b.Owner == c.Id)) return;
        var rng = new Rng(unchecked(_w.Seed * 6151 + c.Id * 7907 + 3));
        string bg = Life.Name(c.Background);
        string Adj() => Adjectives[rng.Range(0, Adjectives.Length)];
        foreach (var h in c.Hobbies)
        {
            if (ItemFor(h) is not BelongingKind k || All.Any(b => b.Owner == c.Id && b.Kind == k)) continue;
            string name = k switch
            {
                BelongingKind.Book => $"{BookTitles[rng.Range(0, BookTitles.Length)]}",
                BelongingKind.Instrument => $"{Adj()} {(rng.Chance(0.5f) ? "기타" : rng.Chance(0.5f) ? "바이올린" : "하모니카")}",
                _ => $"{Adj()} {Spec(k).Name}",
            };
            Add(c, k, name, rng.Chance(0.4f) ? $"{bg} 시절부터 쓰던 것" : "출항 전에 챙겨 온 것", progress: k == BelongingKind.Book ? rng.Range(0f, 0.5f) : 0f);
        }
        Add(c, BelongingKind.Mug, $"{Adj()} 컵", rng.Chance(0.5f) ? "고향에서 가져온 것" : "출항 기념으로 받은 것");
        if (rng.Chance(0.65f)) Add(c, BelongingKind.Photo, rng.Chance(0.5f) ? "가족사진" : rng.Chance(0.5f) ? "고향 바다 사진" : "졸업 사진", "늘 지니고 다니는 것");
        if (rng.Chance(0.45f)) Add(c, BelongingKind.Blanket, $"{Adj()} 담요", rng.Chance(0.5f) ? "어머니가 떠 준 것" : "고향에서 가져온 것");
        if (c.Role is CrewRole.Technician or CrewRole.Engineer or CrewRole.Electrician)
            Add(c, BelongingKind.Toolset, $"{c.Name}의 공구 가방", rng.Chance(0.5f) ? $"{bg} 시절부터 손에 익은 것" : "출항 때 지급받은 것");
    }

    /// <summary>새 물건을 하나 준다 (기항지에서 산 것 · 시험).</summary>
    public Belonging Seed2(CrewMember c, BelongingKind k, string? origin = null) =>
        Add(c, k, $"{c.Name}의 새 {Spec(k).Name}", origin ?? "새로 마련한 것");

    private Belonging Add(CrewMember owner, BelongingKind k, string name, string origin, float progress = 0f, int maker = -1)
    {
        var b = new Belonging { Id = _nextId++, Kind = k, Name = name, Owner = owner.Id, Origin = origin, Progress = progress, Maker = maker };
        All.Add(b);
        return b;
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        foreach (var c in w.Crew) if (!c.Dead && c.Profiled && !All.Any(b => b.Owner == c.Id)) Seed(c);
        _clock += dt;
        bool slow = _clock >= 1f / 6f; // 십 분마다
        float slowDt = _clock;
        if (slow) _clock = 0f;
        Damage(dt);
        if (!slow) return;
        Notice();
        Admire(slowDt);
        Photos(slowDt);
        Sessions_(slowDt);
        Borrowed();
        Rescue();
        // 사람마다 쓰는 효과 (일 손 · 잠자리)
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            var tools = All.FirstOrDefault(b => b.Owner == c.Id && b.Kind == BelongingKind.Toolset);
            bool toolUser = c.Role is CrewRole.Technician or CrewRole.Engineer or CrewRole.Electrician;
            c.ToolFactor = tools is { Usable: true } && (tools.At == null || tools.Holder == c.Id) && tools.BorrowedBy < 0 ? 1.04f
                : All.Any(b => b.Kind == BelongingKind.Toolset && b.BorrowedBy == c.Id) ? 1f : toolUser ? 0.97f : 1f;
            c.Comfy = All.Any(b => b.Owner == c.Id && b.Kind == BelongingKind.Blanket && b.Usable && b.At == null && b.Holder < 0);
        }
    }

    /// <summary>물·불: 젖고 타서 못 쓰게 된다 (들고 있는 것은 괜찮다).</summary>
    private void Damage(float dt)
    {
        var w = _w;
        if (w.Fire.Count == 0 && !w.Ship.Rooms.Any(r => r.Flood > 0f)) return;
        foreach (var b in All)
        {
            if (b.Holder >= 0 || b.Condition <= 0f) continue;
            var cell = CellOf(b);
            var room = w.Ship.RoomAt(cell);
            if (room == null) continue;
            var spec = Spec(b.Kind);
            float before = b.Condition;
            string? why = null;
            float depth = MoistureSystem.Depth(room);
            if (spec.Fragile && depth > 0.04f && b.Condition > 0.12f) { b.Condition = MathF.Max(0.12f, b.Condition - (0.6f + 3f * depth) * dt); why = "물에 젖었다"; } // 젖은 건 말리면 얼룩진 채로 쓴다
            if (spec.Flammable && (w.Fire.At(cell) > 0f || w.Fire.AnyWithin(cell, 1.5f))) { b.Condition = MathF.Max(0f, b.Condition - 1.2f * dt); why = "불에 탔다"; }
            else if (!spec.Flammable && w.Fire.At(cell) > 0.3f) { b.Condition = MathF.Max(0f, b.Condition - 0.4f * dt); why = "불에 그을렸다"; }
            if (before >= 0.3f && b.Condition < 0.3f && why != null)
            {
                Stats.Ruined++;
                b.Unnoticed = true;
                Mark(b, $"{room.Name}에서 {why}");
                w.Log.Add(w.Tick, LogKind.Life, $"{CrewOf(b.Owner)?.Name ?? "?"}의 {b.Name} — {why}");
            }
        }
    }

    /// <summary>주인이 망가진 걸 알게 된다 (그 방에 오거나 쓰려고 할 때) — 마음이 무너진다.</summary>
    private void Notice()
    {
        var w = _w;
        foreach (var b in All)
        {
            if (!b.Unnoticed || CrewOf(b.Owner) is not CrewMember o || o.Dead || !o.IsAwake) continue;
            if (o.Room == null || w.Ship.RoomAt(CellOf(b)) != o.Room) continue;
            Seen(o, b);
        }
    }

    private void Seen(CrewMember o, Belonging b)
    {
        var w = _w;
        b.Unnoticed = false;
        Stats.Noticed++;
        bool dear = b.Kind is BelongingKind.Photo or BelongingKind.Instrument or BelongingKind.Artwork or BelongingKind.Blanket || b.From >= 0 || b.Origin.Contains("시절") || b.Origin.Contains("고향");
        o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + (dear ? 0.15f : 0.07f));
        string last = b.Marks.Count > 0 ? b.Marks[^1].Text : "망가졌다";
        Life.Diary(w, o, Persona.Say(o, $"{Ko.IGa(b.Name)} 못 쓰게 됐다 ({last})"));
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(b.Name)} 망가진 걸 알았다{(dear ? " — 아끼던 것이다" : "")}", o.Id);
        MarkLog.Add(o.Memory.Marks, w.Tick, $"{Ko.EulReul(b.Name)} 잃었다");
    }

    /// <summary>방에 놓인 작품을 한참 바라본다 (만든 사람과 가까워진다).</summary>
    private void Admire(float dt)
    {
        var w = _w;
        foreach (var art in All)
        {
            if (art.Kind != BelongingKind.Artwork || art.At is not Cell at || !art.Usable) continue;
            var room = w.Ship.RoomAt(at);
            if (room == null) continue;
            foreach (var c in w.Crew)
            {
                if (c.Dead || !c.IsAwake || c.Room != room || c.Id == art.Maker || c.Id == art.Owner) continue;
                if (c.Job?.Activity is not (RelaxActivity or WanderActivity or ChatActivity or EatActivity or HobbyActivity)) continue;
                if (!_rng.Chance(0.25f * dt)) continue;
                Stats.Admired++;
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f);
                if (CrewOf(art.Maker) is CrewMember maker) c.ChangeAffinity(maker, 0.02f);
                if (_rng.Chance(0.3f)) w.Log.Add(w.Tick, LogKind.Life, $"{Ko.EulReul(art.Name)} 한참 바라봤다", c.Id);
            }
        }
    }

    /// <summary>슬프거나 지칠 때, 제 침실에서 사진을 꺼내 본다.</summary>
    private void Photos(float dt)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.Bed == null || c.Room != c.Bed.Room || c.Job?.Urgent == true) continue;
            if (c.GriefUntil <= w.Tick && c.Needs.Stress < 0.6f) continue;
            var photo = All.FirstOrDefault(b => b.Owner == c.Id && b.Kind == BelongingKind.Photo && b.Usable && b.At == null);
            if (photo == null || !_rng.Chance(0.5f * dt)) continue;
            Stats.PhotoLooks++;
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
            if (_rng.Chance(0.3f)) Life.Diary(w, c, Persona.Say(c, $"{Ko.EulReul(photo.Name)} 오래 봤다"));
        }
    }

    // ───────────────────────────── 하는 중의 취미 ─────────────────────────────

    private void Sessions_(float dt)
    {
        var w = _w;
        foreach (var (id, s) in Sessions.ToList())
        {
            var p = CrewOf(id);
            if (p == null || p.Dead || p.Job?.Activity is not HobbyActivity) { Sessions.Remove(id); continue; }
            if (s.Hobby is not (Hobby.Instrument or Hobby.Singing or Hobby.Music)) continue;
            bool loud = s.Hobby is Hobby.Instrument or Hobby.Singing;
            foreach (var o in w.Crew)
            {
                if (o == p || o.Dead || o.Room == null) continue;
                float d2 = (o.Position - p.Position).LengthSquared();
                // 곁에서 듣는다
                if (loud && o.IsAwake && d2 < 25f && o.Job?.Urgent != true)
                {
                    o.Needs.Social = MathF.Min(1f, o.Needs.Social + 0.05f);
                    o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.02f);
                    o.ChangeAffinity(p, 0.005f);
                    if (s.Listeners.Add(o.Id)) Stats.Listened++;
                }
                // 밤에 옆에서 자던 사람: 조용히 해 달라고 한다
                if (loud && !s.Hushed && o.Pose == Pose.Sleeping && !Life.Has(o, Habit.HeavySleeper)
                    && (o.Room == p.Room || p.Room != null && o.Room.Doors.Any(dd => dd.RoomA == p.Room || dd.RoomB == p.Room))
                    && _rng.Chance((Life.Has(o, Habit.Patient) ? 0.15f : 0.4f) * dt * 6f))
                {
                    s.Hushed = true;
                    Stats.Hushed++;
                    o.Jolt(w);
                    o.ChangeAffinity(p, -0.05f);
                    p.ChangeAffinity(o, -0.03f);
                    p.Needs.Stress = MathF.Min(1f, p.Needs.Stress + 0.03f);
                    w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(o.Name)} 잠에서 깨 {p.Name}에게 조용히 해 달라고 했다", o.Id);
                    Life.Diary(w, o, Persona.Say(o, $"{Ko.EunNeun(p.Name)} 밤에 {(s.Hobby == Hobby.Singing ? "노래를 부른다" : "악기를 친다")}"));
                    Life.Diary(w, p, $"{Ko.IGa(o.Name)} 조용히 해 달라고 했다.");
                    p.NextThinkTick = w.Tick;
                }
            }
        }
    }

    /// <summary>빌려 간 공구: 오래 안 돌려주면 주인이 서운해한다.</summary>
    private void Borrowed()
    {
        var w = _w;
        foreach (var b in All)
        {
            if (b.BorrowedBy < 0 || w.Tick - b.BorrowedAt < SimTime.Hours(20)) continue;
            if (CrewOf(b.Owner) is not CrewMember o || CrewOf(b.BorrowedBy) is not CrewMember x || o.Dead) { b.BorrowedBy = -1; continue; }
            if (b.Marks.Count > 0 && b.Marks[^1].Text.StartsWith("돌려주지 않았다")) continue;
            Stats.Unreturned++;
            o.ChangeAffinity(x, -0.06f);
            Mark(b, $"돌려주지 않았다 ({x.Name})");
            Life.Diary(w, o, Persona.Say(o, $"{Ko.IGa(x.Name)} 내 {Ko.EulReul(Spec(b.Kind).Name)} 가져가서 안 돌려준다"));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(x.Name)} 빌려 간 {Ko.EulReul(b.Name)} 하루가 지나도 돌려주지 않는다", o.Id);
            w.Relations.Remember(o, x, RelationReason.TookMyThing, $"{Ko.EulReul(b.Name)} 빌려 가서 안 돌려줬다");
        }
    }

    /// <summary>불·물이 번지는 방에 제 물건이 있고 곁에 있으면 챙겨 나온다 (이력이 남는다).</summary>
    private void Rescue()
    {
        var w = _w;
        if (w.Fire.Count == 0 && !w.Ship.Rooms.Any(r => r.Flood > 20f)) return;
        foreach (var b in All)
        {
            if (b.Holder >= 0 || !b.Usable || b.Kind == BelongingKind.Artwork && b.At == null) continue;
            var cell = CellOf(b);
            var room = w.Ship.RoomAt(cell);
            if (room == null || w.Fire.CountIn(room) == 0 && MoistureSystem.Depth(room) < 0.03f) continue;
            var c = w.Crew.FirstOrDefault(x => !x.Dead && x.CanAct && x.Room == room && (x.Id == b.Owner || x.AffinityTo(CrewOf(b.Owner)!) > 0.3f)
                && (x.Position - cell.Center).LengthSquared() < 4f && x.CarryingPerson == null);
            if (c == null) continue;
            Stats.Rescued++;
            b.At = c.Cell; // 손에 들고 나와 곁에 둔다 (나중에 제자리로)
            b.Open = false;
            string what = w.Fire.CountIn(room) > 0 ? "불길 속에서" : "물이 차는 방에서";
            Mark(b, c.Id == b.Owner ? $"{what} 챙겨 나왔다" : $"{Ko.IGa(c.Name)} {what} 건져 줬다");
            if (c.Id != b.Owner && CrewOf(b.Owner) is CrewMember o)
            {
                o.ChangeAffinity(c, 0.08f);
                w.Relations.Remember(o, c, RelationReason.SavedMyThing, $"{what} 내 {Ko.EulReul(b.Name)} 건져 줬다");
            }
            w.Log.Add(w.Tick, LogKind.Life, $"{what} {Ko.EulReul(b.Name)} 챙겼다", c.Id);
        }
    }

    // ───────────────────────────── 일과 공구 ─────────────────────────────

    private static bool ToolWork(WorkOrder? o) => o != null && o.Skill is Skill.Mechanics or Skill.Electrical or Skill.Engineering && !WorkKinds.IsEmergency(o.Kind);

    /// <summary>공구가 드는 일을 시작한다: 제 공구가 없으면 동료 것을 빌린다 · 제 공구를 누가 가져갔으면 되찾는다.</summary>
    public void OnJobStarted(CrewMember c, Job job)
    {
        var w = _w;
        if (!ToolWork(job.Order) || c.Role is not (CrewRole.Technician or CrewRole.Engineer or CrewRole.Electrician)) return;
        Stats.ToolJobs++;
        var own = All.FirstOrDefault(b => b.Owner == c.Id && b.Kind == BelongingKind.Toolset);
        if (own is { Usable: true } && own.BorrowedBy >= 0 && own.BorrowedBy != c.Id && CrewOf(own.BorrowedBy) is CrewMember x)
        {
            // 되찾는다 (말없이 가져간 사람에게 서운하다)
            Stats.Reclaims++;
            Mark(own, $"{x.Name}에게서 되찾았다");
            c.ChangeAffinity(x, -0.04f);
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(x.Name)} 쓰던 제 공구를 도로 가져왔다", c.Id);
            own.BorrowedBy = -1;
            return;
        }
        if (own is { Usable: true } && own.At == null && own.BorrowedBy < 0) return;
        if (All.Any(b => b.Kind == BelongingKind.Toolset && b.BorrowedBy == c.Id)) return;
        // 빌린다: 지금 공구 일을 하지 않는 동료의 공구 (사물함에 있는 것)
        var lend = All.Where(b => b.Kind == BelongingKind.Toolset && b.Owner != c.Id && b.Usable && b.At == null && b.Holder < 0 && b.BorrowedBy < 0
                                  && CrewOf(b.Owner) is CrewMember o && !o.Dead && !ToolWork(o.Job?.Order))
            .OrderByDescending(b => c.AffinityTo(CrewOf(b.Owner)!)).FirstOrDefault();
        if (lend == null) return;
        lend.BorrowedBy = c.Id;
        lend.BorrowedAt = w.Tick;
        Stats.Borrows++;
        Mark(lend, $"{Ko.IGa(c.Name)} 빌려 갔다");
        w.Log.Add(w.Tick, LogKind.Life, $"제 공구가 없어 {CrewOf(lend.Owner)?.Name}의 공구를 빌렸다", c.Id);
    }

    /// <summary>일이 끝났다: 빌린 공구를 돌려놓는가 (습관) · 위험해 대피하면 공구를 두고 나온다.</summary>
    public void OnJobEnded(CrewMember c, Job job, ToilStatus status)
    {
        var w = _w;
        if (!ToolWork(job.Order)) return;
        // 대피하느라 공구를 두고 나온다
        var own = All.FirstOrDefault(b => b.Owner == c.Id && b.Kind == BelongingKind.Toolset && b.At == null && b.Holder < 0 && b.BorrowedBy < 0);
        if (status != ToolStatusOk && own != null && c.Room != null && !c.Dead && EvacuateActivity.DangerHere(c, w) > 0.45f)
        {
            own.At = c.Cell;
            Stats.ToolsLeft++;
            Mark(own, $"{c.Room.Name}에 두고 나왔다");
            w.Log.Add(w.Tick, LogKind.Life, $"급히 나오느라 공구를 {c.Room.Name}에 두고 왔다", c.Id);
        }
        // 빌린 공구 돌려놓기
        var lent = All.FirstOrDefault(b => b.Kind == BelongingKind.Toolset && b.BorrowedBy == c.Id);
        if (lent == null) return;
        float p = Life.Has(c, Habit.NeatFreak) || Life.Has(c, Habit.Methodical) ? 1f : Life.Has(c, Habit.Forgetful) ? 0.45f : Life.Has(c, Habit.Messy) ? 0.6f : 0.85f;
        if (!_rng.Chance(p)) return;
        lent.BorrowedBy = -1;
        Stats.Returned++;
        Mark(lent, $"{Ko.IGa(c.Name)} 쓰고 제자리에 돌려놓았다");
        if (CrewOf(lent.Owner) is CrewMember o) o.ChangeAffinity(c, 0.01f);
    }

    private const ToilStatus ToolStatusOk = ToilStatus.Succeeded;

    // ───────────────────────────── 떠난 사람의 물건 ─────────────────────────────

    public void OnDeath(CrewMember dead)
    {
        var w = _w;
        var alive = w.Crew.Where(c => !c.Dead && c != dead && !c.IsChild).ToList();
        foreach (var b in All.Where(b => b.Owner == dead.Id).ToList())
        {
            if (b.Holder == dead.Id) { b.Holder = -1; b.At = dead.Cell; }
            CrewMember? heir = b.Kind switch
            {
                // 공구는 같은 일을 하는 가장 가까운 사람(제자)에게
                BelongingKind.Toolset => alive.Where(c => c.Role == dead.Role || c.Role is CrewRole.Technician or CrewRole.Engineer or CrewRole.Electrician)
                    .OrderByDescending(c => dead.AffinityTo(c) + (c.Role == dead.Role ? 0.3f : 0f)).FirstOrDefault(),
                // 사진은 가족에게 (짝 · 아이 · 가장 가까운 사람)
                BelongingKind.Photo => alive.FirstOrDefault(c => dead.Partner == c.Id) ?? alive.FirstOrDefault(c => c.Parents.Contains(dead.Id))
                    ?? alive.OrderByDescending(c => dead.AffinityTo(c)).FirstOrDefault(),
                // 취미 물건은 같은 취미를 가진 가까운 사람에게
                _ when ItemForKind(b.Kind) is { } hobbies => alive.Where(c => c.Hobbies.Any(hobbies.Contains)).OrderByDescending(c => dead.AffinityTo(c)).FirstOrDefault(),
                _ => null,
            };
            if (heir == null || b.Kind == BelongingKind.Artwork) { b.Memorial = true; Mark(b, $"{Ko.IGa(dead.Name)} 남긴 것"); continue; }
            b.From = dead.Id;
            b.Owner = heir.Id;
            b.BorrowedBy = -1;
            if (b.At != null && b.Open) b.Open = false;
            Stats.Inherited++;
            string how = b.Kind == BelongingKind.Photo && (dead.Partner == heir.Id || heir.Parents.Contains(dead.Id)) ? "가족에게 돌아갔다" : "물려받았다";
            Mark(b, $"{dead.Name}에게서 {heir.Name}에게 — {how}");
            w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(heir.Name)} {dead.Name}의 {Ko.EulReul(b.Name)} {how}", heir.Room, new[] { heir }, log: true);
            Life.Diary(w, heir, $"{dead.Name}의 {Ko.EulReul(b.Name)} {how}.");
        }
    }

    private static Hobby[]? ItemForKind(BelongingKind k)
    {
        var list = Enum.GetValues<Hobby>().Where(h => ItemFor(h) == k).ToArray();
        return list.Length > 0 ? list : null;
    }

    // ───────────────────────────── 취미를 한다 ─────────────────────────────

    /// <summary>하는 동안 (틱마다): 진척 · 판 · 작품 · 운동.</summary>
    internal void Practice(CrewMember c, Hobby h, Belonging? item, HobbyGame? game, float dt)
    {
        var w = _w;
        if (item != null)
        {
            float rate = h switch
            {
                Hobby.Reading => 1f / 8f,        // 여덟 시간이면 한 권
                Hobby.Painting or Hobby.Knitting => 1f / 7f,
                Hobby.ModelBuilding or Hobby.Woodwork or Hobby.Electronics => 1f / 9f,
                Hobby.Photography => 1f / 5f,
                _ => 0f,
            };
            if (rate > 0f) item.Progress += rate * dt;
            if (h == Hobby.Reading && item.Progress >= 1f)
            {
                item.Progress = 0f;
                Mark(item, $"{Ko.IGa(c.Name)} 또 끝까지 읽었다");
                Life.Diary(w, c, Persona.Say(c, $"{Ko.EulReul(item.Name)} 다 읽었다"));
            }
            if (Crafts(h) && item.Progress >= 1f) Finish(c, h, item);
        }
        if (game != null && !game.Done && game.B >= 0 && CrewOf(game.A == c.Id ? game.B : game.A) is CrewMember other
            && (other.Position - c.Position).LengthSquared() < 9f && other.Job?.Activity is HobbyActivity)
        {
            if (c.Id == game.A) game.Moves += dt * 40f; // 한 사람만 센다 (한 시간에 마흔 수)
            game.LastPlayed = w.Tick;
            if (game.Moves >= game.Target) EndGame(game);
        }
    }

    private void Finish(CrewMember c, Hobby h, Belonging kit)
    {
        var w = _w;
        kit.Progress = 0f;
        string title = ArtTitles[_rng.Range(0, ArtTitles.Length)];
        string what = h switch
        {
            Hobby.Painting => $"그림 '{title}'", Hobby.Knitting => _rng.Chance(0.5f) ? "목도리" : "스웨터", Hobby.Photography => $"사진 '{title}'",
            Hobby.Woodwork => "나무 조각", Hobby.Electronics => "작은 오르골", _ => _rng.Chance(0.5f) ? "우주선 모형" : "기관실 모형",
        };
        var art = Add(c, BelongingKind.Artwork, $"{c.Name}의 {what}", h == Hobby.Knitting ? "직접 떴다" : h == Hobby.Photography ? "직접 찍었다" : "직접 만들었다", maker: c.Id);
        Stats.Artworks++;
        // 휴게실·식당(없으면 제 침실)에 놓는다
        var show = w.Ship.RoomsOf(RoomType.Lounge).Concat(w.Ship.RoomsOf(RoomType.Mess)).FirstOrDefault(r => !r.Detached && !r.OffLimits);
        var friend = w.Crew.Where(o => !o.Dead && o != c && !o.IsChild && c.AffinityTo(o) > 0.4f).OrderByDescending(o => c.AffinityTo(o)).FirstOrDefault();
        if (h == Hobby.Knitting && friend != null || friend != null && _rng.Chance(0.35f))
        {
            // 가까운 사람에게 선물한다
            art.Owner = friend.Id;
            art.From = c.Id;
            Stats.Gifts++;
            Mark(art, $"{Ko.IGa(c.Name)} {friend.Name}에게 선물했다");
            friend.ChangeAffinity(c, 0.1f);
            c.ChangeAffinity(friend, 0.05f);
            friend.Needs.Stress = MathF.Max(0f, friend.Needs.Stress - 0.08f);
            w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} {Ko.EulReul(what)} 완성해 {friend.Name}에게 선물했다", c.Room, new[] { c, friend }, log: true);
            Life.Diary(w, friend, Persona.Say(friend, $"{Ko.IGa(c.Name)} {Ko.EulReul(what)} 만들어 줬다"));
            w.Relations.Remember(friend, c, RelationReason.GaveMeGift, $"{Ko.EulReul(what)} 만들어 줬다");
            return;
        }
        if (show != null && h != Hobby.Knitting)
        {
            art.At = show.Cells.Where(x => w.Ship.IsOpenFloor(x) && !All.Any(b => b.At == x)).OrderBy(x => (x.Center - show.Center).LengthSquared()).Skip(_rng.Range(0, 4)).FirstOrDefault();
            Mark(art, $"{show.Name}에 걸었다");
        }
        w.History.Add(w, HistoryKind.Milestone, $"{Ko.IGa(c.Name)} {Ko.EulReul(what)} 완성했다" + (art.At != null ? $" — {show!.Name}에 걸었다" : ""), c.Room, new[] { c }, log: true);
        Life.Diary(w, c, Persona.Say(c, $"{Ko.EulReul(what)} 완성했다"));
    }

    private void EndGame(HobbyGame g)
    {
        var w = _w;
        g.Done = true;
        Stats.GamesDone++;
        var a = CrewOf(g.A)!;
        var b = CrewOf(g.B)!;
        // 체스는 머리 (기관·전기 솜씨 + 침착), 카드는 운이 크다
        float sa = g.Kind == Hobby.Chess ? a.RawSkill(Skill.Engineering) + 0.5f * a.Traits.Calm : 0.5f;
        float sb = g.Kind == Hobby.Chess ? b.RawSkill(Skill.Engineering) + 0.5f * b.Traits.Calm : 0.5f;
        var win = _rng.Chance(sa / MathF.Max(0.01f, sa + sb)) ? a : b;
        var lose = win == a ? b : a;
        g.Winner = win.Id;
        win.Needs.Stress = MathF.Max(0f, win.Needs.Stress - 0.1f);
        lose.Needs.Stress = MathF.Max(0f, lose.Needs.Stress - (Life.Has(lose, Habit.ShortTempered) ? -0.04f : 0.05f));
        win.ChangeAffinity(lose, 0.04f);
        lose.ChangeAffinity(win, Life.Has(lose, Habit.ShortTempered) ? -0.03f : 0.03f);
        string game = g.Kind == Hobby.Chess ? "체스" : "카드";
        string text = $"{Ko.WaGwa(a.Name)} {b.Name}의 {game} — {Ko.IGa(win.Name)} 이겼다" + (g.Breaks > 0 ? $" (끊겼던 판을 {g.Breaks}번 이어 뒀다)" : "");
        w.Log.Add(w.Tick, LogKind.Life, text, win.Id);
        Life.Diary(w, win, Persona.Say(win, $"{lose.Name}에게 {Ko.EulReul(game)} 이겼다"));
        Life.Diary(w, lose, Persona.Say(lose, $"{win.Name}에게 {Ko.EulReul(game)} 졌다"));
        if (Get(g.Item) is Belonging set) Mark(set, $"{Ko.IGa(win.Name)} {Ko.EulReul(lose.Name)} 이긴 판");
    }

    /// <summary>판을 연다 (상대를 기다린다).</summary>
    internal HobbyGame OpenGame(CrewMember c, Hobby h, Belonging set, Cell table)
    {
        var g = new HobbyGame { Id = _nextGame++, Kind = h, A = c.Id, Item = set.Id, Table = table, Target = h == Hobby.Chess ? 40f : 25f, Started = _w.Tick, LastPlayed = _w.Tick };
        Games.Add(g);
        Stats.Games++;
        if (Games.Count > 60) Games.RemoveAll(x => x.Done && _w.Tick - x.LastPlayed > SimTime.TicksPerDay * 3);
        return g;
    }

    /// <summary>판이 끊겼다 (상대가 떠났다·경보) — 판은 그 자리에 그대로 남는다.</summary>
    internal void BreakGame(HobbyGame g)
    {
        if (g.Done) return;
        if (g.B < 0) { g.Done = true; return; } // 아무도 안 왔다 — 판을 거둔다
        g.Breaks++;
    }

    /// <summary>{c}가 할 수 있는 판: 이어 둘 판 (상대가 한가하다) · 상대를 기다리는 판.</summary>
    internal HobbyGame? GameFor(CrewMember c, Hobby h)
    {
        var w = _w;
        foreach (var g in Games)
        {
            if (g.Done || g.Kind != h) continue;
            // 끊긴 내 판 — 상대가 한가하면 이어 둔다
            if ((g.A == c.Id || g.B == c.Id) && g.B >= 0 && CrewOf(g.A == c.Id ? g.B : g.A) is CrewMember o && Free(o)) return g;
            // 남이 연 판 — 상대를 기다린다
            if (g.B < 0 && g.A != c.Id && CrewOf(g.A) is CrewMember host && host.Job?.Activity is HobbyActivity && (host.Position - g.Table.Center).LengthSquared() < 9f) return g;
        }
        return null;
    }

    /// <summary>판에 응할 만큼 한가하다.</summary>
    internal bool Free(CrewMember o) => !o.Dead && o.CanAct && o.IsAwake && o.Job?.Urgent != true
        && o.Job?.Activity is null or RelaxActivity or WanderActivity or ChatActivity or HobbyActivity;

    internal void PutDown(CrewMember c, Belonging item, bool open)
    {
        item.Holder = -1;
        item.At = c.Cell;
        item.Open = open;
        item.OpenSince = _w.Tick;
        if (open) { Stats.PutDown++; Mark(item, $"{c.Room?.Name ?? "?"}에 펼친 채 두었다"); }
    }

    internal void Stow(CrewMember c, Belonging item)
    {
        item.Holder = -1;
        item.At = null;
        item.Open = false;
        Stats.Returned++;
    }

    internal void PickUp(CrewMember c, Belonging item)
    {
        if (item.Unnoticed && item.Owner == c.Id) Seen(c, item);
        item.Holder = c.Id;
        item.At = null;
    }

    /// <summary>한 줄 요약 (화면).</summary>
    public string Line(Belonging b) =>
        $"{b.Name} · {Where(b)}" + (b.Condition < 0.95f ? $" · 상태 {b.Condition * 100:0}%" : "") + (b.Progress > 0.02f && b.Kind != BelongingKind.Artwork ? $" · 진척 {b.Progress * 100:0}%" : "");
}

// ─────────────────────────────── 취미 ───────────────────────────────

/// <summary>v14.3 취미: 물건을 가져와 → 알맞은 자리에서 하고 → 제자리에 둔다 (끊기면 펼친 채 두고, 나중에 이어 한다).</summary>
public sealed class HobbyActivity : Activity
{
    public override string Id => "hobby";
    public override string Label => "취미";

    private static (Hobby h, Belonging? item, HobbyGame? game, string why)? Choose(CrewMember c, World w)
    {
        var bs = w.Belongings;
        // 1) 이어 할 것: 펼친 채 둔 내 물건 · 끊긴 판
        foreach (var h in c.Hobbies)
        {
            if (BelongingSystem.Board(h) && bs.GameFor(c, h) is HobbyGame g)
                return (h, bs.Get(g.Item), g, g.B < 0 ? $"{bs.All.FirstOrDefault(b => b.Id == g.Item)?.Name ?? "판"} — 상대를 기다린다" : g.Breaks > 0 || w.Tick - g.LastPlayed > SimTime.Hours(2) ? "끊긴 판을 마저 둔다" : "판을 이어 둔다");
            if (bs.ItemFor(c, h) is Belonging open && open.Open && open.At != null) return (h, open, null, $"{open.Name} — 두고 온 자리에서 이어 한다");
        }
        // 2) 오늘의 취미 (날마다 바뀐다)
        int day = (int)(w.Tick / SimTime.TicksPerDay);
        for (int i = 0; i < c.Hobbies.Count; i++)
        {
            var h = c.Hobbies[(day + c.Id + i) % c.Hobbies.Count];
            var kind = BelongingSystem.ItemFor(h);
            var item = bs.ItemFor(c, h);
            if (kind != null && item == null) continue; // 물건이 없거나 망가졌다
            return (h, item, null, item != null ? $"{item.Name}" : Persona.Of(h).Name);
        }
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Hobbies.Count == 0 || c.IsChild && c.Age < 6f) return (0f, "—");
        var pick = Choose(c, w);
        if (pick is not var (h, item, game, why)) return (0f, "취미 물건이 없다");
        var s = c.Schedule;
        float hour = Hour(w);
        float score = 0.07f + 0.5f * c.Needs.Stress;
        float workEnd = s.WorkStart + s.WorkLength;
        if (SimTime.InWindow(hour, workEnd, SimTime.HoursFromTo(workEnd, s.SleepStart))) score += 0.17f;
        if (item is { Open: true }) score += 0.12f;
        if (game != null) score += game.B < 0 && game.A != c.Id ? 0.3f : 0.2f;
        if (Bedtime(c, w)) score -= 0.25f;
        if (OnShift(c, w)) score -= 0.15f;
        return (MathF.Max(0f, score), $"{Persona.Of(h).Name} — {why}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var pick = Choose(c, w);
        if (pick is not var (h, item, game, _)) return null;
        var bs = w.Belongings;
        var toils = Plans.DropOff(c, w, dist);
        bool resume = item is { Open: true, At: not null } && game == null;
        // 1) 가져온다 (판은 놓인 자리에서)
        if (item != null && item.Holder != c.Id && game == null)
        {
            var at = bs.CellOf(item);
            if (!dist.Reachable(at)) return null;
            toils.Add(new GotoToil(at));
            var it = item;
            toils.Add(new DoToil((cm, world) =>
            {
                if (it.Holder >= 0 && it.Holder != cm.Id || !it.Usable) return false;
                world.Belongings.PickUp(cm, it);
                if (resume) world.Belongings.Stats.Resumed++;
                return true;
            }));
        }
        // 2) 자리
        Cell spot;
        if (game != null) spot = Cell.Dirs8.Select(d => game.Table + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault() ?? game.Table;
        else if (resume) spot = item!.At!.Value;
        else if (Spot(c, w, dist, h) is Cell sp) spot = sp;
        else return null;
        if (!dist.Reachable(spot)) return null;
        // 체스·카드: 판을 연다 (탁자 곁)
        HobbyGame? g = game;
        if (BelongingSystem.Board(h) && g == null)
        {
            if (item == null) return null;
            toils.Add(new GotoToil(spot));
            var it = item;
            var table = spot;
            toils.Add(new DoToil((cm, world) =>
            {
                if (it.Holder != cm.Id) return false;
                world.Belongings.PutDown(cm, it, false);
                it.Open = true;
                g = world.Belongings.OpenGame(cm, h, it, table);
                world.Log.Add(world.Tick, LogKind.Life, $"{Ko.EulReul(it.Name)} 펼쳐 놓고 상대를 기다린다", cm.Id);
                return true;
            }));
        }
        else
        {
            toils.Add(new GotoToil(spot));
            if (game != null)
            {
                var gg = game;
                toils.Add(new DoToil((cm, world) =>
                {
                    if (gg.Done) return false;
                    if (gg.B < 0 && gg.A != cm.Id) { gg.B = cm.Id; world.Log.Add(world.Tick, LogKind.Life, $"{world.Crew.First(x => x.Id == gg.A).Name}의 판에 앉았다", cm.Id); }
                    else if (gg.Breaks > 0 || world.Tick - gg.LastPlayed > SimTime.Hours(2)) { world.Belongings.Stats.GamesResumed++; world.Log.Add(world.Tick, LogKind.Life, "끊긴 판을 마저 둔다", cm.Id); }
                    return true;
                }));
            }
        }
        // 3) 한다
        bool standing = BelongingSystem.Exercise(h);
        var session = (HobbySession?)null;
        long waited = 0;
        var it3 = item;
        toils.Add(new WaitToil(SimTime.Minutes(w.Rng.Range(40f, 80f)), standing ? Pose.Standing : Pose.Sitting, null, minTicks: SimTime.Minutes(10))
        {
            EveryTick = (cm, world) =>
            {
                const float dt = 1f / SimTime.TicksPerHour;
                if (session == null)
                {
                    session = new HobbySession { Crew = cm.Id, Hobby = h, Spot = cm.Cell, Item = it3?.Id ?? -1, Started = world.Tick };
                    world.Belongings.Sessions[cm.Id] = session;
                    world.Belongings.Stats.Sessions++;
                }
                if (g != null && g.B < 0) waited++;
                world.Belongings.Practice(cm, h, it3, g, dt);
            },
            DoneWhen = (cm, world) => session?.Hushed == true || g != null && (g.Done || g.B < 0 && waited > SimTime.Minutes(25)),
        });
        // 4) 뒤처리: 제자리에 둔다 (어지르는 사람은 그냥 둔다)
        if (item != null)
        {
            var it = item;
            bool messy = Life.Has(c, Habit.Messy) && !Life.Has(c, Habit.NeatFreak);
            if (!messy)
            {
                toils.Add(new GotoToilLate(cm => it.Holder == cm.Id || it.Open && g != null && (g.Done || g.B < 0) ? w.Belongings.Home(it) : (Cell?)null));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (it.Holder == cm.Id || g != null && (g.Done || g.B < 0) && it.Owner == cm.Id) world.Belongings.Stow(cm, it);
                    return true;
                }));
            }
            else
                toils.Add(new DoToil((cm, world) =>
                {
                    if (it.Holder == cm.Id) { world.Belongings.PutDown(cm, it, false); world.Belongings.Stats.LeftOut++; }
                    return true;
                }));
        }
        string doing = item != null && BelongingSystem.Spec(item.Kind).Doing.Length > 0 ? BelongingSystem.Spec(item.Kind).Doing : Persona.Of(h).Doing;
        return new Job(this, Persona.Of(h).Name, toils)
        {
            LogText = resume ? $"두고 온 {Ko.EulReul(item!.Name)} 다시 펼친다" : g != null && g.B < 0 && g.A != c.Id ? null : doing,
            LogKind = LogKind.Life,
            TargetRoom = w.Ship.RoomAt(spot),
            InterruptMargin = 0.12f,
            OnFinished = (cm, world, status) =>
            {
                bool active = world.Belongings.Sessions.Remove(cm.Id) && session != null; // 하던 중이었나 (가는 길이었나)
                if (g != null && status != ToilStatus.Succeeded) world.Belongings.BreakGame(g);
                // 하던 중에 끊겼으면 그 자리에 펼친 채 둔다 (돌아와 이어 한다) — 가는 길에 끊겼으면 그냥 내려놓는다 (다음엔 알맞은 자리를 다시 고른다)
                if (it3 != null && it3.Holder == cm.Id && status != ToilStatus.Succeeded)
                {
                    world.Belongings.PutDown(cm, it3, active);
                    if (status == ToilStatus.Interrupted && active) world.Log.Add(world.Tick, LogKind.Life, $"{Ko.EulReul(it3.Name)} 펼친 채 내려놓았다", cm.Id);
                }
            },
        };
    }

    /// <summary>취미를 할 자리: 취미의 방 · 의자(책은 불 켜진 곳) · 탁자(판·작업) · 체육관 바닥(운동).</summary>
    private static Cell? Spot(CrewMember c, World w, DistanceField dist, Hobby h)
    {
        var rooms = Persona.Of(h).Rooms;
        bool table = BelongingSystem.Board(h) || h is Hobby.Painting or Hobby.ModelBuilding or Hobby.Woodwork or Hobby.Electronics or Hobby.Puzzles or Hobby.Writing or Hobby.Journaling;
        bool floor = BelongingSystem.Exercise(h);
        Cell? best = null;
        float bestScore = float.MinValue;
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.OffLimits || room.Leaking || Atmosphere.Danger(room) > 0.1f) continue;
            float roomScore = rooms.Contains(room.Type) ? 1f : room.Type is RoomType.Lounge or RoomType.Mess ? 0.3f : -1f;
            if (roomScore < 0f) continue;
            if (h == Hobby.Reading && room.Dark) roomScore -= 0.8f; // 책은 불 켜진 곳에서
            if (floor)
            {
                foreach (var cell in room.Cells)
                {
                    if (!w.Ship.IsOpenFloor(cell) || !dist.Reachable(cell) || w.IsSpotTaken(cell, c)) continue;
                    float v = roomScore - dist.Get(cell) / 800f;
                    if (v > bestScore) { bestScore = v; best = cell; }
                }
                continue;
            }
            foreach (var f in room.Furniture)
            {
                if (f.Type != (table ? FurnitureType.Table : FurnitureType.Seat) && !(f.Type == FurnitureType.Seat && !table)) continue;
                foreach (var spot in table ? Cell.Dirs4.Select(d => f.Cells[0] + d) : f.UseSpots)
                {
                    if (!w.Ship.IsWalkable(spot) || !dist.Reachable(spot) || w.IsSpotTaken(spot, c)) continue;
                    float v = roomScore - dist.Get(spot) / 800f + (f.ReservedBy == null ? 0f : -2f);
                    if (v > bestScore) { bestScore = v; best = spot; }
                }
            }
        }
        return best;
    }

    /// <summary>운동하는 중인가 (체력 단련이 오른다).</summary>
    public static bool Exercising(CrewMember c, World w) =>
        c.Job?.Activity is HobbyActivity && w.Belongings.Sessions.TryGetValue(c.Id, out var s) && BelongingSystem.Exercise(s.Hobby);
}

/// <summary>v14.3 뒤처리: 두고 온 제 물건(대피하며 두고 온 공구 · 어질러 둔 것)을 찾아와 제자리에 둔다. 정리광은 남의 것도 치워 준다.</summary>
public sealed class TidyActivity : Activity
{
    public override string Id => "tidy";
    public override string Label => "정리";

    private static Belonging? Target(CrewMember c, World w)
    {
        var bs = w.Belongings;
        bool neat = Life.Has(c, Habit.NeatFreak);
        Belonging? best = null;
        float bestScore = 0f;
        foreach (var b in bs.All)
        {
            if (b.At is not Cell at || b.Holder >= 0 || b.Kind == BelongingKind.Artwork || b.Memorial) continue;
            bool mine = b.Owner == c.Id;
            if (!mine && !neat) continue;
            if (b.Open && w.Tick - b.OpenSince < SimTime.Hours(mine ? 30 : 12)) continue; // 펼친 채 둔 건 이어 할 것 — 오래되면 치운다
            if (bs.Games.Any(g => !g.Done && g.Item == b.Id)) continue;
            var room = w.Ship.RoomAt(at);
            if (room == null || room.OffLimits || room.Leaking || Atmosphere.Danger(room) > 0.1f || w.Fire.CountIn(room) > 0) continue;
            float v = mine ? (b.Kind == BelongingKind.Toolset ? 0.55f : 0.25f) : 0.18f;
            if (v > bestScore) { bestScore = v; best = b; }
        }
        return best;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var b = Target(c, w);
        if (b == null || !dist.Reachable(w.Belongings.CellOf(b))) return (0f, "—");
        float s = b.Owner == c.Id ? (b.Kind == BelongingKind.Toolset ? 0.55f : 0.22f) : 0.16f;
        if (Bedtime(c, w)) s -= 0.2f;
        return (MathF.Max(0f, s), b.Owner == c.Id ? $"두고 온 {b.Name}" : $"{w.Crew.FirstOrDefault(x => x.Id == b.Owner)?.Name}의 {Ko.IGa(b.Name)} 나와 있다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var b = Target(c, w);
        if (b == null) return null;
        var at = w.Belongings.CellOf(b);
        if (!dist.Reachable(at)) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new DoToil((cm, world) =>
        {
            if (b.Holder >= 0 || b.At == null) return false;
            world.Belongings.PickUp(cm, b);
            return true;
        }));
        toils.Add(new GotoToilLate(cm => w.Belongings.Home(b)));
        toils.Add(new DoToil((cm, world) =>
        {
            if (b.Holder != cm.Id) return false;
            world.Belongings.Stow(cm, b);
            if (b.Owner == cm.Id) { world.Belongings.Stats.Retrieved++; if (b.Kind == BelongingKind.Toolset) world.Log.Add(world.Tick, LogKind.Life, "두고 온 공구를 찾아왔다", cm.Id); }
            else if (world.Crew.FirstOrDefault(x => x.Id == b.Owner) is CrewMember o)
            {
                world.Belongings.Stats.Tidied++;
                MarkLog.Add(b.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 치워 제자리에 두었다");
                o.ChangeAffinity(cm, Life.Has(o, Habit.Messy) ? 0.01f : 0.03f);
            }
            return true;
        }));
        return new Job(this, "정리", toils)
        {
            LogText = b.Owner == c.Id ? $"두고 온 {Ko.EulReul(b.Name)} 가지러 간다" : $"나와 있는 {Ko.EulReul(b.Name)} 치운다",
            LogKind = LogKind.Life,
            OnFinished = (cm, world, status) => { if (b.Holder == cm.Id && status != ToilStatus.Succeeded) world.Belongings.PutDown(cm, b, false); },
        };
    }
}

/// <summary>v14.3 고쳐 주기: 솜씨 있는 친구가 망가진 물건을 작업대에서 고치거나 말려 준다.</summary>
public sealed class MendActivity : Activity
{
    public override string Id => "mend";
    public override string Label => "고쳐 주기";

    private static Belonging? Target(CrewMember c, World w) =>
        w.Belongings.All.Where(b => b.Condition is > 0.05f and < 0.3f && b.Holder < 0 && !b.Memorial
                                    && w.Crew.FirstOrDefault(o => o.Id == b.Owner) is CrewMember o && !o.Dead && (o == c || c.AffinityTo(o) > 0.25f))
            .OrderByDescending(b => b.Owner == c.Id ? 0f : 1f).FirstOrDefault();

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.RawSkill(Skill.Mechanics) < 0.35f || OnShift(c, w) || Bedtime(c, w)) return (0f, "—");
        var b = Target(c, w);
        if (b == null) return (0f, "—");
        return (0.2f + 0.15f * c.Traits.Sociability, $"{w.Crew.First(o => o.Id == b.Owner).Name}의 {Ko.EulReul(b.Name)} 고쳐 준다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var b = Target(c, w);
        if (b == null) return null;
        var at = w.Belongings.CellOf(b);
        var bench = w.Ship.FurnitureOf(FurnitureType.Workbench).Where(f => f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0])).OrderBy(f => dist.Get(f.UseSpots[0])).FirstOrDefault();
        if (bench == null || !dist.Reachable(at)) return null;
        bool dry = BelongingSystem.Spec(b.Kind).Fragile && b.Marks.Count > 0 && b.Marks[^1].Text.Contains("젖");
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new DoToil((cm, world) => { if (b.Holder >= 0) return false; world.Belongings.PickUp(cm, b); return true; }));
        toils.Add(new GotoToil(bench.UseSpots[0]));
        toils.Add(new WorkToil(dry ? 0.5f : 1.2f, Skill.Mechanics, bench.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            b.Condition = dry ? 0.55f : 0.75f;
            world.Belongings.Stats.Mended++;
            var o = world.Crew.First(x => x.Id == b.Owner);
            MarkLog.Add(b.Marks, world.Tick, o == cm ? (dry ? "말려서 다시 쓴다 (얼룩이 남았다)" : "손수 고쳤다") : $"{Ko.IGa(cm.Name)} {(dry ? "말려" : "고쳐")} 줬다");
            if (o != cm)
            {
                o.ChangeAffinity(cm, 0.12f);
                o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.08f);
                world.History.Add(world, HistoryKind.Bond, $"{Ko.IGa(cm.Name)} {o.Name}의 {Ko.EulReul(b.Name)} {(dry ? "말려" : "고쳐")} 줬다", cm.Room, new[] { cm, o }, log: true);
                Life.Diary(world, o, Persona.Say(o, $"{Ko.IGa(cm.Name)} 내 {Ko.EulReul(b.Name)} {(dry ? "말려" : "고쳐")} 줬다"));
                world.Relations.Remember(o, cm, RelationReason.FixedMyThing, $"{Ko.EulReul(b.Name)} {(dry ? "말려" : "고쳐")} 줬다");
            }
            return true;
        }));
        toils.Add(new GotoToilLate(cm => w.Belongings.Home(b)));
        toils.Add(new DoToil((cm, world) => { if (b.Holder == cm.Id) world.Belongings.Stow(cm, b); return true; }));
        return new Job(this, "고쳐 주기", toils)
        {
            LogText = $"{Ko.EulReul(b.Name)} {(dry ? "말리러" : "고치러")} 작업대로 가져간다",
            LogKind = LogKind.Life,
            OnFinished = (cm, world, status) => { if (b.Holder == cm.Id && status != ToilStatus.Succeeded) world.Belongings.PutDown(cm, b, false); },
        };
    }
}
