using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.2 선내 생태계: 관상 식물 · 곡식 바구미 · 고양이 한 마리.
//  화분은 마르면 시들고(빛 · 열 · 무중력 · 고양이 이빨이 거든다) 돌보는 사람이 물을 준다 — 못 돌보면 다른 손이 거들거나 컴퓨터가 짚는다.
//  바구미는 창고 곡식 선반에 알로 실려 와 따뜻하면 불어나 비상식량을 갉는다 — 누가 선반 앞에서 보거나, 컴퓨터가 먹은 양보다 더 준 것을 짚으면
//   골라내 버리고 닦고 밀폐 통에 옮긴다 (버린 포대는 쓰레기통으로).
//  고양이는 배가 고프면 밥그릇 앞에서 울고(사료가 떨어지면 사람 몫을 나눠 준다) · 좋아하는 사람을 따라다니고 그 침대에서 자고 ·
//   경보 · 쿵 소리에 침대 밑 · 선반 뒤로 숨는다 — 좋아하는 사람이 찾아 데려온다(컴퓨터가 움직임으로 짚어 주기도) · 무중력엔 허우적 ·
//   죽으면 모두가 슬퍼한다.
public enum PlantKind : byte { Fern, Cactus, Ivy, Basil, Fig, Orchid }

public sealed class Plant
{
    public int Id { get; init; }
    public PlantKind Kind { get; init; }
    public Cell At { get; internal set; }
    public int RoomId { get; internal set; } = -1;
    public float Water { get; internal set; } = 0.8f;
    public float Health { get; internal set; } = 0.9f;
    public int Carer { get; internal set; } = -1;
    public bool Fixed { get; internal set; }
    public bool Floating { get; internal set; }
    public bool Spilled { get; internal set; }
    public bool Dead { get; internal set; }
    public string DeadWhy { get; internal set; } = "";
    public long Watered { get; internal set; }
    public int WateredBy { get; internal set; } = -1;
    public int Chewed { get; internal set; }
    public int ClaimedBy { get; internal set; } = -1;
    public long Hinted { get; internal set; } = -1;
    public string Name => EcoSystem.Name(Kind);
    /// <summary>0 싱싱 · 1 보통 · 2 시듦 · 3 죽음.</summary>
    public int Stage => Dead ? 3 : Health < 0.35f ? 2 : Health < 0.7f ? 1 : 0;
}

public enum CatState : byte { Roam, Nap, Beg, Eat, Hide, Carried, Float, Dead }

public sealed class ShipCat
{
    public string Name { get; init; } = "";
    public Vector2 Pos { get; internal set; }
    public Vector2 Vel { get; internal set; }
    public Vector2 Facing { get; internal set; } = new(1f, 0f);
    public Cell Cell => Cell.FromPosition(Pos);
    public int RoomId { get; internal set; } = -1;
    public float Hunger { get; internal set; } = 0.2f;
    public float Fear { get; internal set; }
    public CatState State { get; internal set; }
    public int Favorite { get; internal set; } = -1;
    public SortedDictionary<int, float> Fond { get; } = new();
    public int CarriedBy { get; internal set; } = -1;
    public long HiddenSince { get; internal set; } = -1;
    public int HideRoom { get; internal set; } = -1;
    public int HideNear { get; internal set; } = -1; // 숨은 가구 (침대 · 선반)
    public int FoundBy { get; internal set; } = -1;
    public int SearchBy { get; internal set; } = -1;
    public SortedSet<int> Searched { get; } = new();
    public SortedSet<int> KnownHides { get; } = new(); // 좋아하는 사람이 아는 숨는 곳 (전에 찾았던 방)
    public int Hint { get; internal set; } = -1;
    public bool HintHeard { get; internal set; }
    public long Purr { get; internal set; } = -1;
    public long Meow { get; internal set; } = -1;
    public long Until { get; internal set; }
    public long Died { get; internal set; } = -1;
    public string DiedWhy { get; internal set; } = "";
    public bool Covered { get; internal set; }
    public float Flail { get; internal set; }
    internal List<Cell>? Path;
    internal int PathAt;
    public bool Hidden => State == CatState.Hide && Path == null;
    public bool Alive => State != CatState.Dead;
}

public sealed class Weevils
{
    public int Id { get; init; }
    public int Shelf { get; init; }
    public int RoomId { get; init; }
    public float Pop { get; internal set; } = 0.02f;
    public bool Found { get; internal set; }
    public int FoundBy { get; internal set; } = -1;
    public long Since { get; init; }
    public int Eaten { get; internal set; }
    public bool Treated { get; internal set; }
    public int TreatBy { get; internal set; } = -1;
    public bool Suspect { get; internal set; }
}

public sealed class EcoStats
{
    public int Watered, Wilted, PlantDeaths, Chewed, Relief, Covered, PlantHints, Repotted;
    public int Infest, Bred, Eaten, FoundByCrew, FoundByComputer, Controlled, Discarded, Spread;
    public int Fed, Begged, Stole, Pets, Hid, Found, Returned, CatHints, Floated, Grabbed, Naps, Follows, Deaths, Mourned;
    public string Line() =>
        $"화분 물 {Watered} · 시듦 {Wilted} · 죽음 {PlantDeaths} · 뜯김 {Chewed} · 귀띔 {PlantHints} / 바구미 {Infest} · 번식 {Bred} · 먹힘 {Eaten} · 발견 {FoundByCrew}+{FoundByComputer} · 방제 {Controlled} · 버림 {Discarded} / "
        + $"고양이 밥 {Fed} · 조름 {Begged} · 훔침 {Stole} · 쓰다듬 {Pets} · 숨음 {Hid} · 찾음 {Found} · 데려옴 {Returned} · 컴퓨터 귀띔 {CatHints} · 허우적 {Floated} · 죽음 {Deaths}";
}

public sealed partial class EcoSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 4111));
    public EcoStats Stats { get; } = new();
    public static bool Off;
    public static long UpdateTicks;

    public List<Plant> Plants { get; } = new();
    public ShipCat? Cat { get; private set; }
    public List<Weevils> Pests { get; } = new();
    public SortedSet<int> Sealed { get; } = new(); // 밀폐 통으로 옮긴 선반
    public Cell? CatBed { get; private set; }
    public Cell? Bowl { get; private set; }
    public int Kibble { get; internal set; } = 40;
    public long BowlFilled { get; private set; } = -1;
    private bool _init;
    private int _next = 1;
    private long _nextPlant, _nextPest, _nextCat, _nextAudit;
    private int _auditSeen;

    public EcoSystem(World w) => _w = w;

    public static string Name(PlantKind k) => k switch
    {
        PlantKind.Fern => "고사리", PlantKind.Cactus => "선인장", PlantKind.Ivy => "아이비", PlantKind.Basil => "바질", PlantKind.Fig => "고무나무", _ => "난초",
    };

    private static float Thirst(PlantKind k) => k switch
    {
        PlantKind.Fern => 0.032f, PlantKind.Basil => 0.04f, PlantKind.Cactus => 0.006f, PlantKind.Ivy => 0.02f, PlantKind.Fig => 0.024f, _ => 0.015f,
    };

    internal CrewMember? CrewOf(int id) => id < 0 ? null : id < _w.Crew.Count && _w.Crew[id].Id == id ? _w.Crew[id] : _w.Crew.FirstOrDefault(c => c.Id == id);
    internal Room? RoomOf(int id) => id >= 0 && id < _w.Ship.Rooms.Count ? _w.Ship.Rooms[id] : null;

    // ───────────────────────────── 처음 ─────────────────────────────

    private void Init()
    {
        _init = true;
        var w = _w;
        var prefer = new[] { RoomType.Lounge, RoomType.Mess, RoomType.Bridge, RoomType.Medbay, RoomType.Quarters, RoomType.Observatory, RoomType.Garden, RoomType.Chapel, RoomType.Comms, RoomType.Quarters };
        int want = Math.Clamp(w.Crew.Count / 4, 3, 8), k = 0;
        var used = new HashSet<int>();
        foreach (var t in prefer)
        {
            if (Plants.Count >= want) break;
            var room = w.Ship.RoomsOf(t).FirstOrDefault(r => !used.Contains(r.Id) || t == RoomType.Lounge && Plants.Count(p => p.RoomId == r.Id) < 2);
            if (room == null || Corner(room, Plants.Select(p => p.At).ToList()) is not Cell at) continue;
            used.Add(room.Id);
            var kind = (PlantKind)((k++ + (int)((uint)w.Seed % 6u)) % 6);
            Plants.Add(new Plant { Id = _next++, Kind = kind, At = at, RoomId = room.Id, Water = R.Range(0.55f, 1f), Health = R.Range(0.75f, 1f), Fixed = kind == PlantKind.Ivy, Watered = w.Tick });
        }
        // 고양이: 휴게실 · 식당 · 선실 쪽
        var home = w.Ship.RoomsOf(RoomType.Lounge).Concat(w.Ship.RoomsOf(RoomType.Mess)).Concat(w.Ship.RoomsOf(RoomType.Quarters)).FirstOrDefault();
        if (home != null && w.Crew.Count >= 3)
        {
            CatBed = Corner(home, Plants.Select(p => p.At).ToList());
            var galley = w.Ship.RoomsOf(RoomType.Galley).Concat(w.Ship.RoomsOf(RoomType.Mess)).FirstOrDefault() ?? home;
            Bowl = Corner(galley, Plants.Select(p => p.At).Append(CatBed ?? new Cell(-9, -9)).ToList());
            var names = new[] { "보리", "나비", "깜이", "호박", "두부", "치즈" };
            var fav = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(c => c.Traits.Sociability + 0.3f * c.Traits.Calm + 0.2f * ((c.Id * 37 + w.Seed) % 7) / 7f).ThenBy(c => c.Id).FirstOrDefault();
            Cat = new ShipCat { Name = names[(int)((uint)w.Seed % (uint)names.Length)], Pos = (CatBed ?? home.Cells[home.Cells.Count / 2]).Center, RoomId = home.Id, Favorite = fav?.Id ?? -1 };
            if (fav != null) Cat.Fond[fav.Id] = 0.6f;
        }
    }

    private Cell? Corner(Room room, List<Cell> avoid)
    {
        var w = _w;
        Cell? best = null; int bs = int.MinValue;
        foreach (var c in room.Cells)
        {
            if (!w.Ship.IsWalkable(c) || w.Ship.FurnitureAt(c) != null || avoid.Contains(c)) continue;
            int walls = 0, door = 0;
            foreach (var d in Cell.Dirs4) { var k = w.Ship.Grid.Kind(c + d); if (k is TileKind.Wall or TileKind.Void) walls++; else if (k == TileKind.Door) door++; }
            if (walls == 0 || door > 0) continue;
            int s = walls * 10 - (c.X * 7 + c.Y * 13) % 5;
            if (s > bs) { bs = s; best = c; }
        }
        return best;
    }

    // ───────────────────────────── 느린 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_init) Init();
        var w = _w;
        if (w.Tick >= _nextPlant) { _nextPlant = w.Tick + SimTime.Minutes(10); PlantTick(SimTime.Minutes(10) / (float)SimTime.TicksPerHour); }
        if (w.Tick >= _nextPest) { _nextPest = w.Tick + SimTime.Minutes(15); PestTick(SimTime.Minutes(15) / (float)SimTime.TicksPerHour); }
        CatTick(dt);
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>무중력 시작 · 끝 (ZeroG가 부른다).</summary>
    public void OnZeroG(bool on)
    {
        if (Cat is not ShipCat cat || !cat.Alive) return;
        var w = _w;
        if (on)
        {
            if (cat.CarriedBy >= 0) return;
            cat.State = CatState.Float; cat.Path = null; cat.Fear = 1f;
            cat.Vel = new Vector2(R.Range(-0.01f, 0.01f), R.Range(-0.012f, -0.004f));
            Stats.Floated++; w.ZeroG.Stats.CatFloat++;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(cat.Name)} 공중에서 네 다리를 허우적거린다");
            return;
        }
        // 고양이는 발부터 내려앉는다 — 그래도 놀라서 숨는다
        if (cat.State == CatState.Float || cat.CarriedBy >= 0)
        {
            cat.CarriedBy = -1; cat.Vel = Vector2.Zero; cat.State = CatState.Roam; cat.Fear = 1f;
            if (w.Ship.RoomAt(cat.Cell) is Room r) cat.RoomId = r.Id;
            else if (RoomOf(cat.RoomId) is Room rr && rr.Cells.Count > 0) cat.Pos = rr.Cells.OrderBy(c => (c.Center - cat.Pos).LengthSquared()).ThenBy(c => c.X * 1000 + c.Y).First().Center;
        }
        Startle("중력이 돌아오며 쿵 소리가 났다");
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Plants.Count);
        foreach (var p in Plants) { F(p.Water); F(p.Health); I(p.Carer); I(p.Dead ? 1 : 0); }
        foreach (var x in Pests) { F(x.Pop); I(x.Found ? 1 : 0); I(x.Eaten); }
        if (Cat is ShipCat c) { F(c.Pos.X); F(c.Pos.Y); F(c.Hunger); I((int)c.State); I(c.Favorite); }
        I(Kibble); I(Stats.Fed); I(Stats.Returned); I(Stats.Controlled);
    }
}
