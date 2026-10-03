using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.3 배수 · 쓰레기 (가볍게): 개수대 · 바닥 배수구 · 세탁 배수관 · 샤워 홈통은 쓸수록 찌꺼기가 쌓인다 (음식물을 같이 버리면 더 빨리).
//  느려지면 컴퓨터가 유량으로 먼저 짚고 · 막히면 회색물이 거꾸로 올라와 바닥이 젖고 하수 냄새가 퍼진다 → 사람들은 그 방에서 밥을 안 먹는다.
//  손재주 있는 사람이 뚫는다 — 음식물 찌꺼기로 막혔으면 배에 '나눠 버리기' 관행이 생긴다.
//  쓰레기통은 차면 넘치고(냄새) 누가 비운다 · 나눠 버리면 금속 · 플라스틱이 고철로 되살아난다.
public enum DrainKind : byte { Sink, Floor, Laundry, Shower }

public sealed class Drain
{
    public int Id { get; init; }
    public DrainKind Kind { get; init; }
    public int RoomId { get; init; }
    public Cell At { get; init; }
    public float Clog { get; internal set; }
    public float Food { get; internal set; } // 막힌 것 가운데 음식물 찌꺼기 몫
    public bool Backflow { get; internal set; }
    public long Since { get; internal set; } = -1;
    public float Stink { get; internal set; }
    public bool Warned { get; internal set; }
    public bool Noticed { get; internal set; }
    public int ClaimedBy { get; internal set; } = -1;
    public long Cleared { get; internal set; } = -1;
}

public sealed class Bin
{
    public int Id { get; init; }
    public int RoomId { get; init; }
    public Cell At { get; init; }
    public float Fill { get; internal set; }
    public float Food { get; internal set; }
    public bool Over => Fill >= 1f;
    public int ClaimedBy { get; internal set; } = -1;
    public long Emptied { get; internal set; } = -1;
}

public sealed class DrainStats
{
    public int Slow, Warned, Backflows, Cleared, Avoided, Complaints, Overflows, Emptied, Sorted, Recycled, Customs;
    public string Line() => $"느려짐 {Slow} · 컴퓨터 {Warned} · 역류 {Backflows} · 뚫음 {Cleared} · 피해 먹음 {Avoided} · 투덜 {Complaints} / 쓰레기통 넘침 {Overflows} · 비움 {Emptied} · 나눔 {Sorted} · 되살림 {Recycled}";
}

public sealed class DrainSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6007 + 2903));
    public DrainStats Stats { get; } = new();
    public static bool Off;
    public static long UpdateTicks;
    public List<Drain> Drains { get; } = new();
    public List<Bin> Bins { get; } = new();
    private bool _init;
    private int _next = 1;
    private long _nextTick;
    private int[] _occ = Array.Empty<int>();
    private readonly SortedDictionary<int, long> _complained = new();

    public DrainSystem(World w) => _w = w;

    public static string Name(DrainKind k) => k switch { DrainKind.Sink => "개수대 배수구", DrainKind.Laundry => "세탁 배수관", DrainKind.Shower => "샤워 홈통", _ => "바닥 배수구" };

    private void Init()
    {
        _init = true;
        var w = _w;
        var taken = new List<Cell>();
        foreach (var r in w.Ship.Rooms.OrderBy(r => r.Id))
        {
            if (r.Detached) continue;
            DrainKind? dk = r.Kind switch
            {
                RoomType.Galley or RoomType.Mess => DrainKind.Sink, RoomType.Laundry => DrainKind.Laundry, RoomType.Decon or RoomType.Gym => DrainKind.Shower,
                RoomType.Medbay or RoomType.Hydroponics or RoomType.WaterPlant => DrainKind.Floor, _ => null,
            };
            if (r.Kind == RoomType.Quarters && Drains.Count(d => d.Kind == DrainKind.Shower) < 2) dk = DrainKind.Shower;
            if (dk is DrainKind k && Spot(r, taken) is Cell at) { Drains.Add(new Drain { Id = _next++, Kind = k, RoomId = r.Id, At = at, Clog = R.Range(0f, 0.25f) }); taken.Add(at); }
            if (r.Kind is RoomType.Galley or RoomType.Mess or RoomType.Workshop or RoomType.Medbay or RoomType.Lounge or RoomType.Quarters or RoomType.Storage && Spot(r, taken) is Cell bt)
            { Bins.Add(new Bin { Id = _next++, RoomId = r.Id, At = bt, Fill = R.Range(0f, 0.4f) }); taken.Add(bt); }
        }
    }

    private Cell? Spot(Room r, List<Cell> avoid)
    {
        var w = _w;
        Cell? best = null; int bs = int.MinValue;
        foreach (var c in r.Cells)
        {
            if (!w.Ship.IsWalkable(c) || w.Ship.FurnitureAt(c) != null || avoid.Contains(c)) continue;
            if (w.Eco.Plants.Any(p => p.At == c) || w.Eco.CatBed == c || w.Eco.Bowl == c) continue;
            int walls = 0, door = 0, wet = 0;
            foreach (var d in Cell.Dirs4) { var k = w.Ship.Grid.Kind(c + d); if (k is TileKind.Wall or TileKind.Void) walls++; else if (k == TileKind.Door) door++; }
            foreach (var d in Cell.Dirs8) if (w.Ship.FurnitureAt(c + d) is Furniture f && f.Type is FurnitureType.Stove or FurnitureType.DishWasher or FurnitureType.WashingMachine or FurnitureType.DeconShower or FurnitureType.WaterRecycler or FurnitureType.MealDispenser or FurnitureType.GrowBed) wet++;
            if (walls == 0 || door > 0) continue;
            int s = walls * 4 + wet * 6 - (c.X * 3 + c.Y * 5) % 3;
            if (s > bs) { bs = s; best = c; }
        }
        return best;
    }

    /// <summary>방의 하수 냄새 (역류 · 막힌 배수구 · 넘친 쓰레기통).</summary>
    public float Stink(Room r)
    {
        float s = 0f;
        foreach (var d in Drains) if (d.RoomId == r.Id) s = MathF.Max(s, d.Stink);
        foreach (var b in Bins) if (b.RoomId == r.Id && b.Over) s = MathF.Max(s, 0.35f);
        return s;
    }

    public const float AvoidAt = 0.45f;

    /// <summary>EatActivity 훅: 식당에 하수 냄새가 나면 다른 방에서 먹는다.</summary>
    public AwayPlan? EatAway(CrewMember c, Furniture? seat, DistanceField dist)
    {
        if (Off || c.IsChild) return null;
        var w = _w;
        Room? mess = seat?.Room;
        if (mess == null) foreach (var r in w.Ship.RoomsOf(RoomType.Mess)) if (!r.OffLimits) { mess = r; break; }
        if (mess == null || Stink(mess) * SmellSystem.Nose(c) < AvoidAt) return null;
        var plan = w.After.AltDining(c, mess, dist, "하수 냄새로", r => Stink(r) < 0.2f);
        if (plan != null) Stats.Avoided++;
        return plan;
    }

    /// <summary>쓰레기를 버린다 (바구미 먹은 포대 · 깨진 것 · 음식물).</summary>
    public void Toss(Room room, float v, bool food)
    {
        var bin = Bins.Where(b => b.RoomId == room.Id).FirstOrDefault() ?? Bins.OrderBy(b => b.Id).FirstOrDefault();
        if (bin == null) return;
        bin.Fill = MathF.Min(1.2f, bin.Fill + v);
        if (food) bin.Food = MathF.Min(1f, bin.Food + v);
    }

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        if (!_init) { if (w.Tick < 30) return; Init(); }
        if (w.Tick < _nextTick) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        const int every = 75; // 3분
        _nextTick = w.Tick + every;
        float h = every / (float)SimTime.TicksPerHour;
        if (_occ.Length != w.Ship.Rooms.Count) _occ = new int[w.Ship.Rooms.Count];
        Array.Clear(_occ);
        foreach (var c in w.Crew) if (!c.Dead && c.Room is Room r && r.Id < _occ.Length && c.IsAwake) _occ[r.Id]++;
        bool sort = w.Culture.Of(CustomKind.SortWaste) != null;
        foreach (var d in Drains)
        {
            var room = w.Ship.Rooms[d.RoomId];
            if (room.Detached) continue;
            float use = d.RoomId < _occ.Length ? _occ[d.RoomId] : 0;
            bool food = d.Kind == DrainKind.Sink;
            float add = (0.002f + 0.0045f * use) * h * (food ? (sort ? 0.45f : 1.4f) : d.Kind == DrainKind.Shower ? 0.9f : 0.6f) * (room.WaterLinked ? 1f : 0.2f);
            d.Clog = MathF.Min(1f, d.Clog + add);
            if (food) d.Food = MathF.Min(1f, d.Food + add * (sort ? 0.3f : 0.8f));
            // 느려짐: 쓰던 사람이 알아챈다 · 컴퓨터는 유량으로 먼저 안다
            if (d.Clog > 0.6f && !d.Noticed && use > 0 && R.Chance(0.2f))
            {
                d.Noticed = true; Stats.Slow++;
                foreach (var c in w.Crew) if (!c.Dead && c.Room == room && c.IsAwake) { c.Say(w, Persona.Say(c, "물이 잘 안 빠지네…")); break; }
            }
            if (d.Clog > 0.72f && !d.Warned && w.Automation.CoreOnline)
            {
                d.Warned = true; Stats.Warned++;
                w.Automation.Speak.Announce(w.Automation.Voice.Style($"{room.Name} {Name(d.Kind)} 물 빠지는 속도가 절반으로 줄었습니다 — 막히기 전에 뚫어 주십시오."), room, 1);
                w.Automation.Book.Add(ActKind.Advice, room, $"{room.Name} {Name(d.Kind)} 유량 {1f - d.Clog:0.00}", d.Food > 0.4f ? "음식물 찌꺼기가 쌓였다 — 막히면 회색물이 거꾸로 올라온다" : "머리카락 · 비누 찌꺼기", "배수구 뚫기 부탁", "", $"drain:{d.Id}", SimTime.Hours(8));
            }
            if (d.Clog >= 1f && !d.Backflow && use > 0) BackUp(d, room);
            if (d.Backflow)
            {
                d.Stink = MathF.Min(1f, d.Stink + 0.5f * h * 6f);
                if (R.Chance(0.5f)) w.Matter.Pour(d.At, Material.Liquid, 0.08f, $"막힌 {Name(d.Kind)}에서 회색물이 올라왔다");
                w.Smells.Emit(room, SmellKind.Foul, 0.25f);
            }
            else d.Stink = MathF.Max(0f, d.Stink - 0.4f * h);
        }
        foreach (var b in Bins)
        {
            var room = w.Ship.Rooms[b.RoomId];
            if (room.Detached) continue;
            float use = b.RoomId < _occ.Length ? _occ[b.RoomId] : 0;
            bool was = b.Over;
            float add = 0.0035f * use * h * (room.Kind is RoomType.Galley or RoomType.Mess ? 1.6f : 1f);
            b.Fill = MathF.Min(1.2f, b.Fill + add);
            if (room.Kind is RoomType.Galley or RoomType.Mess) b.Food = MathF.Min(1f, b.Food + add);
            if (!was && b.Over) { Stats.Overflows++; MarkLog.Add(room.Marks, w.Tick, "쓰레기통이 넘쳤다"); }
            if (b.Over) w.Smells.Emit(room, SmellKind.Foul, 0.08f + 0.1f * b.Food);
        }
        // 냄새나는 방에 들어온 사람: 투덜 (가끔)
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.Room is not Room r) continue;
            float s = Stink(r);
            if (s < AvoidAt) continue;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.01f * s);
            if (_complained.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.Hours(4)) continue;
            _complained[c.Id] = w.Tick; Stats.Complaints++;
            c.Say(w, Persona.Say(c, Drains.Any(d => d.RoomId == r.Id && d.Backflow) ? "윽, 하수 냄새 — 배수구가 또 올라왔어" : "쓰레기통 좀 누가 비워"));
        }
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    private void BackUp(Drain d, Room room)
    {
        var w = _w;
        d.Backflow = true; d.Since = w.Tick; Stats.Backflows++;
        w.Matter.Pour(d.At, Material.Liquid, 0.6f, $"막힌 {Name(d.Kind)}에서 회색물이 거꾸로 올라왔다");
        w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name}: {Name(d.Kind)}가 막혀 회색물이 거꾸로 올라왔다 — 하수 냄새");
        MarkLog.Add(room.Marks, w.Tick, $"{Name(d.Kind)} 역류 · 하수 냄새");
        foreach (var c in w.Crew) if (!c.Dead && c.Room == room && c.IsAwake) { c.Say(w, Persona.Say(c, "어어, 배수구에서 물이 올라와!")); c.Jolt(w); break; }
    }

    /// <summary>시험 · 사건용: 이 배수구가 막힌다.</summary>
    public void ForceClog(Drain d, float food) { d.Clog = 1f; d.Food = food; var room = _w.Ship.Rooms[d.RoomId]; if (!d.Backflow) BackUp(d, room); }

    /// <summary>뚫었다 (DrainActivity).</summary>
    internal void Clear(Drain d, CrewMember c)
    {
        var w = _w;
        var room = w.Ship.Rooms[d.RoomId];
        bool food = d.Food > 0.4f, back = d.Backflow;
        d.Clog = 0f; d.Food = 0f; d.Backflow = false; d.Warned = false; d.Noticed = false; d.ClaimedBy = -1; d.Cleared = w.Tick; d.Stink *= 0.5f;
        Stats.Cleared++;
        Toss(room, 0.1f, food);
        MarkLog.Add(c.Memory.Marks, w.Tick, $"{room.Name} {Name(d.Kind)}를 뚫었다");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(c.Name)} {room.Name} {Ko.EulReul(Name(d.Kind))} 뚫었다" + (food ? " — 음식물 찌꺼기 덩어리가 나왔다" : ""), c.Id);
        if (back && food && w.Culture.Of(CustomKind.SortWaste) == null)
        {
            w.Culture.Adopt(CustomKind.SortWaste, $"{room.Name} 배수구가 음식물 찌꺼기로 막혀 회색물이 거꾸로 올라왔다", c.Name);
            Stats.Customs++;
            c.Say(w, Persona.Say(c, "이제 음식물은 따로 모으자. 개수대에 그냥 버리면 또 이렇게 돼"));
        }
    }

    /// <summary>비웠다 (나눠 버리는 배면 금속 · 플라스틱은 고철로).</summary>
    internal void Empty(Bin b, CrewMember c)
    {
        var w = _w;
        bool sort = w.Culture.Follows(c, CustomKind.SortWaste);
        float amount = b.Fill;
        b.Fill = 0f; b.Food = 0f; b.ClaimedBy = -1; b.Emptied = w.Tick;
        Stats.Emptied++;
        if (sort) { Stats.Sorted++; float back = 0.4f * amount; w.Scrap.AddScrap(back); if (back > 0.1f) Stats.Recycled++; }
        MarkLog.Add(c.Memory.Marks, w.Tick, sort ? "쓰레기를 나눠 버렸다 (금속 · 플라스틱은 재활용함으로)" : "쓰레기통을 비웠다");
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Drains.Count); foreach (var d in Drains) { F(d.Clog); I(d.Backflow ? 1 : 0); }
        I(Bins.Count); foreach (var b in Bins) F(b.Fill);
        I(Stats.Cleared); I(Stats.Emptied);
    }
}
