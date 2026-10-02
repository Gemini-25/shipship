using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.5 사고 뒤 며칠 · 꿈 · 장소의 기억 — 사고가 끝나도 배와 사람은 며칠에 걸쳐 돌아온다.
//  긴 항해 뒤 배의 모양과 생활 패턴만 보고 무슨 일을 겪었는지 알 수 있게 (누가 · 왜 · 무엇이 바뀌었고 · 누가 기억하나).
//  · 묵은 그을음 냄새: 불난 방은 며칠 냄새가 남는다 (닦은 벽 · 공기청정기 · 환기 · 주컴퓨터가 올린 환기량으로 빨리 빠진다) → Ambience의 방 냄새에 얹혀
//    잠 · 쉼을 깎고 공기청정기를 부른다 → 코가 예민한 사람부터 접시를 들고 다른 방에서 먹는다 → 냄새가 빠지면 하나둘 돌아온다.
//  · 젖은 침구: 물이 든 선실의 침구를 걷어 따뜻하고 마른 방에 넌다 (온도 · 습도 · 히터 · 세탁실이 말리는 속도 · 그 방이 눅눅해진다) → 마르면 걷어 온다 · 잊으면 걸린 채.
//    젖은 침구 · 맨 매트리스에서 잔 밤은 덜 쉬고 꿈자리가 사납다. 빨랫줄 고리는 벽에 남는다.
//  · 정전 뒤 냉장고: 멈춘 동안 냄비마다 몇 도까지 올랐나(주컴퓨터 기록) · 냄새(사람 코) → 골라 버리고 남긴다 · 냉장고 문에 쪽지.
//    남긴 냄비가 시면 고른 사람은 다음엔 컴퓨터 기록을 더 믿는다. 버린 자리엔 균이 남는다 (Soil).
//  · 개인 조명: 조명이 나간 방의 작업등은 차고 눈부시다 → 선실의 독서등을 가져다 놓는다 (그 방은 편해지고 내 선실 잠자리는 조금 허전) →
//    고친 뒤 도로 가져가거나 두고 쓰다가 회의에서 정식으로 등을 단다.
//  · 혼자 먹기: 가까운 사람을 잃었거나 크게 놀란 사람은 선실에서 혼자 먹다가 → 식당 구석 → 늘 앉던 자리로 (위로받으면 빨라진다).
//  · 떠난 사람의 자리: 늘 앉던 의자는 비워 둔다 (컵 하나) · 가까웠던 사람은 그 앞에서 잠깐 멈춘다 · 모르는 신입이 앉으면 누군가 그 사람 이야기를 한다.
//  · 불탄 그림: 그림 그리는 사람이 다시 그린다 — 그을린 귀퉁이를 일부러 남기거나, 떠난 사람이 그리던 것을 이어 그린다.
//  · 임시 배치가 굳는다: 며칠 쓰던 임시 식탁 · 옮겨 둔 등은 회의 안건이 되어 정식으로 꾸며진다 (RoomPlans).
//  · 꿈 · 잠버릇 · 아침 식탁 (AftermathDreams.cs) · 장소의 기억 (길 고르기 비용) · 흔적 목록 (AftermathTraces.cs) · 할 일 (AftermathWork.cs).
// 난수는 전용(시드 × 소수). 사전은 정렬된 것만 돈다. 5분마다 (방 · 사람 단위) · 한 시간마다 (굳음 · 주컴퓨터).

public enum AfterTraceKind : byte { FridgeNote, DryHooks, EmptySeat, PersonalLamp, Repainted, DiningCorner, LampFixture, SpotFlower }

/// <summary>사람 · 물건 · 사건과 이어진 흔적 하나 (일반 얼룩은 흔적 목록에서 방마다 묶는다).</summary>
public sealed class AfterTrace
{
    public AfterTraceKind Kind { get; init; }
    public int Room { get; init; } = -1;
    public Cell At { get; init; }
    public long Tick { get; init; }
    public string Text { get; set; } = "";
    public int Crew { get; init; } = -1;
    public int Item { get; init; } = -1;
    public string Event { get; init; } = "";
    public bool Gone { get; set; }
}

/// <summary>널어 둔 침구.</summary>
public sealed class DryLine
{
    public int Id { get; init; }
    public int Bed { get; init; }
    public int Owner { get; init; } = -1;
    public int By { get; init; } = -1;
    public int Room { get; init; }
    public Cell At { get; init; }
    public Cell Hook { get; init; }
    public long Hung { get; init; }
    public float Wet { get; set; } = 1f;
    public long Dried { get; set; } = -1;
    public long Fetched { get; set; } = -1;
    public int ClaimedBy { get; set; } = -1;
    public bool Forgot { get; set; }
    public bool Done => Fetched >= 0;
}

/// <summary>정전 뒤 냉장고 고르기 한 번.</summary>
public sealed class FridgeSort
{
    public int Fridge { get; init; }
    public long Off { get; init; }
    public long On { get; init; }
    public float Hours => (On - Off) / (float)SimTime.TicksPerHour;
    public SortedDictionary<int, float> MaxTemp { get; } = new();
    public SortedSet<int> ComputerSaysThrow { get; } = new();
    public long Done { get; set; } = -1;
    public int By { get; set; } = -1;
    public int ClaimedBy { get; set; } = -1;
    public int Thrown, Kept, ByNose, ByLog;
    public List<int> KeptIds { get; } = new();
    public bool Learned { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>옮겨 온 개인 조명.</summary>
public sealed class LampMove
{
    public int Id { get; init; }
    public int Owner { get; init; }
    public int Prop { get; init; }
    public int From { get; init; }
    public Cell Home { get; init; }
    public int To { get; init; }
    public long Asked { get; init; }
    public long Moved { get; set; } = -1;
    public long Returned { get; set; } = -1;
    public bool Stays { get; set; }
    public bool WantBack { get; set; }
    public long LitAgain { get; set; } = -1;
    public int ClaimedBy { get; set; } = -1;
    public int Carrying { get; set; } = -1;
    public bool Active => Moved >= 0 && Returned < 0;
}

/// <summary>불에 탄 그림 (다시 그릴 거리).</summary>
public sealed class BurnedPic
{
    public string Spec { get; init; } = "";
    public int Room { get; init; }
    public Cell At { get; init; }
    public int Maker { get; init; } = -1;
    public long Tick { get; init; }
    public string Name { get; init; } = "";
    public long Done { get; set; } = -1;
    public int ClaimedBy { get; set; } = -1;
    public float Progress { get; set; }
}

/// <summary>다시 그린 그림 (흔적을 남긴 방법).</summary>
public sealed class Repaint
{
    public int Prop { get; init; }
    public int Room { get; init; }
    public Cell At { get; init; }
    public int Painter { get; init; }
    public int Maker { get; init; } = -1;
    public bool ForDead { get; init; }
    public string Keep { get; init; } = "";
    public long Tick { get; init; }
    public string Name { get; init; } = "";
}

/// <summary>떠난 사람의 자리.</summary>
public sealed class EmptySeat
{
    public int Seat { get; init; }
    public int Room { get; init; }
    public int Crew { get; init; }
    public string Name { get; init; } = "";
    public long Since { get; init; }
    public SortedSet<int> Knew { get; } = new();
    public long Released { get; set; } = -1;
    public int ReleasedTo { get; set; } = -1;
    public bool Cup { get; set; }
    public int CupBy { get; set; } = -1;
    public int Pauses { get; set; }
    public int SatBy { get; set; } = -1;
    public long SatAt { get; set; } = -1;
    public bool Active => Released < 0;
}

/// <summary>며칠 쓰다 굳어 가는 임시 배치 (다른 방 식탁 · 옮겨 둔 등).</summary>
public sealed class TempSetup
{
    public byte Kind { get; init; } // 0 임시 식탁 · 1 옮겨 둔 등
    public int Room { get; init; }
    public long First { get; init; }
    public long Last { get; set; }
    public int Uses { get; set; }
    public SortedSet<int> Days { get; } = new();
    public SortedDictionary<int, int> Users { get; } = new();
    public string Why { get; init; } = "";
    public int Plan { get; set; } = -1;
    public string State { get; set; } = "";
    public int Lamp { get; init; } = -1;
}

public sealed class AfterStats
{
    public int SootRooms, AwayMeals, AwayEaters, SootReturns, VentBoosts, SootAdvice;
    public int WetBeds, Hung, Fetched, ForgotLines, WetNights, BareNights;
    public int FridgeSorts, Thrown, Kept, ByNose, ByLog, SourKept, NoseLessons;
    public int LampAsks, LampMoves, LampBack, LampStays, Glare;
    public int Alone, Corner, BackToSeat, Withdrawn;
    public int EmptySeats, Pauses, Cups, NewcomerTold, SeatsKept, SeatsReleased;
    public int Burned, Repainted, ForDead;
    public int Proposed, Hardened;
    public int Dreams, Nightmares, GriefDreams, HomeDreams, Wakes, WokeOthers, Diaries, TableTalks, SharedDreams, Teased, Kept2, SleepAdvice, Excused;
    public int Places, Detours, Flowers;
    public string Summary() =>
        $"그을음 냄새 남은 방 {SootRooms} · 다른 방에서 먹음 {AwayMeals}끼({AwayEaters}명 · 돌아옴 {SootReturns}) · 환기량 올림 {VentBoosts} · " +
        $"젖은 침구 {WetBeds}(널음 {Hung} · 걷음 {Fetched} · 잊음 {ForgotLines} · 젖은 채 잔 밤 {WetNights} · 맨 매트리스 {BareNights}) · " +
        $"냉장고 고르기 {FridgeSorts}(버림 {Thrown} · 둠 {Kept} · 코 {ByNose} · 기록 {ByLog} · 남긴 게 셈 {SourKept}) · " +
        $"개인 조명 {LampMoves}(눈부심 {Glare} · 도로 {LampBack} · 그대로 {LampStays}) · 혼자 먹음 {Alone} · 구석 {Corner} · 제자리 {BackToSeat} · " +
        $"빈자리 {EmptySeats}(멈춤 {Pauses} · 컵 {Cups} · 신입에게 이야기 {NewcomerTold} · 계속 비움 {SeatsKept} · 내줌 {SeatsReleased}) · " +
        $"탄 그림 {Burned}(다시 그림 {Repainted} · 떠난 사람 것 {ForDead}) · 굳음 안건 {Proposed}(정식 {Hardened}) · " +
        $"꿈 {Dreams}(악몽 {Nightmares} · 그리움 {GriefDreams} · 고향 {HomeDreams} · 깸 {Wakes} · 옆 사람 깸 {WokeOthers} · 식탁 이야기 {TableTalks} · 같은 꿈 {SharedDreams} · 놀림 {Teased} · 혼자 삭임 {Kept2}) · " +
        $"수면 기록 조언 {SleepAdvice}(아침 근무 늦춤 {Excused}) · 장소 기억 {Places}(돌아감 {Detours} · 꽃 {Flowers})";
}

public sealed partial class AftermathSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6421 + 233));
    /// <summary>성능 비교용: 끈다.</summary>
    public static bool Off { get; set; }
    public AfterStats Stats { get; } = new();
    public static readonly int Every = SimTime.Minutes(5);
    private long _next = -1, _nextHour = -1, _nextSpots = -1;
    private int _ids;

    private float[] _soot = Array.Empty<float>();
    private int[] _fires = Array.Empty<int>();
    private long[] _sootSince = Array.Empty<long>();
    private long[] _lampSince = Array.Empty<long>();
    private long[] _vent = Array.Empty<long>();
    private int[] _sootMarks = Array.Empty<int>();

    public SortedDictionary<int, AfterMind> Minds { get; } = new();
    public SortedDictionary<int, float> WetBeds { get; } = new();
    public List<DryLine> Lines { get; } = new();
    public List<FridgeSort> Fridges { get; } = new();
    public List<LampMove> Lamps { get; } = new();
    public List<BurnedPic> Burned { get; } = new();
    public List<Repaint> Repaints { get; } = new();
    public List<EmptySeat> Seats { get; } = new();
    public List<TempSetup> Setups { get; } = new();
    public List<AfterTrace> Traces { get; } = new();
    /// <summary>화면: 최근 다른 방에서 먹은 자리 (쟁반).</summary>
    public List<(long tick, Cell at, int crew)> AwayPlates { get; } = new();
    private readonly SortedSet<int> _dead = new();
    private long _fridgeOff = -1, _fridgeUp = -1;
    private int _fridgeId = -1;
    private readonly SortedDictionary<int, float> _potMax = new();
    private List<Furniture>? _beds;
    private int _bedsAt = -1;

    public AftermathSystem(World w) => _w = w;

    public AfterMind Mind(CrewMember c)
    {
        if (!Minds.TryGetValue(c.Id, out var m)) Minds[c.Id] = m = new AfterMind { Id = c.Id };
        return m;
    }

    public AfterMind? Peek(CrewMember c) => Minds.TryGetValue(c.Id, out var m) ? m : null;

    internal CrewMember? Crew(int id)
    {
        var list = _w.Crew;
        if (id >= 0 && id < list.Count && list[id].Id == id) return list[id];
        foreach (var c in list) if (c.Id == id) return c;
        return null;
    }

    internal Room? RoomById(int id) => id >= 0 && id < _w.Ship.Rooms.Count ? _w.Ship.Rooms[id] : null;
    internal static bool Adult(CrewMember c) => !c.Dead && !c.IsChild;

    /// <summary>며칠 남는 묵은 그을음 냄새 0~1 (Ambience가 방 냄새에 얹는다).</summary>
    public float StaleSoot(Room r) => r.Id < _soot.Length ? _soot[r.Id] : 0f;
    public long SootSince(Room r) => r.Id < _sootSince.Length ? _sootSince[r.Id] : -1;

    /// <summary>이 사람이 그 방에서 맡는 묵은 냄새 (코 · 깔끔함 · 불을 무서워함 · 그 방이 무서움).</summary>
    public float SootFor(CrewMember c, Room r)
    {
        float s = StaleSoot(r);
        if (s <= 0.01f) return 0f;
        float k = SmellSystem.Nose(c) * (c.Habits.Contains(Habit.NeatFreak) ? 1.25f : 1f) * (c.Fears.Contains(Fear.Fire) ? 1.4f : 1f) * (1f + 0.5f * c.Memory.FearOf(r));
        return s * k;
    }

    public const float SootAvoid = 0.3f;

    private void EnsureRooms(int n)
    {
        if (_soot.Length >= n) return;
        int old = _soot.Length;
        Array.Resize(ref _soot, n); Array.Resize(ref _fires, n); Array.Resize(ref _sootSince, n); Array.Resize(ref _lampSince, n); Array.Resize(ref _vent, n); Array.Resize(ref _sootMarks, n);
        for (int i = old; i < n; i++) { _fires[i] = _w.Ship.Rooms[i].Fires; _sootSince[i] = -1; _lampSince[i] = -1; _vent[i] = -1; }
    }

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        if (_next < 0) { _next = w.Tick + Every; EnsureRooms(w.Ship.Rooms.Count); foreach (var c in w.Crew) if (c.Dead) _dead.Add(c.Id); return; }
        if (w.Tick < _next) return;
        _next = w.Tick + Every;
        float h = Every / (float)SimTime.TicksPerHour;
        long pf = Prof.Now;
        EnsureRooms(w.Ship.Rooms.Count);
        Rooms(h);
        Deaths();
        Beds(h);
        FridgeWatch();
        LampWatch();
        Sleepers(h);
        Meals();
        Incidents();
        if (w.Tick >= _nextSpots) { _nextSpots = w.Tick + SimTime.Minutes(30); PlaceUpdate(); }
        if (w.Tick >= _nextHour) { _nextHour = w.Tick + SimTime.Hours(1); Hourly(); }
        Prof.Lap("after.Update", pf);
    }

    // ───────────────────────────── 묵은 그을음 냄새 ─────────────────────────────

    private void Rooms(float h)
    {
        var w = _w;
        var rooms = w.Ship.Rooms;
        Array.Clear(_sootMarks);
        bool any = false;
        for (int i = 0; i < rooms.Count; i++) if (_soot[i] > 0f || rooms[i].Fires > _fires[i]) { any = true; break; }
        if (any)
            foreach (var (idx, st) in w.Body.Marks)
            {
                if (st.V[(int)CellMark.Soot] < 0.1f) continue;
                var cell = new Cell(idx % w.Ship.Grid.Width, idx / w.Ship.Grid.Width);
                if (w.Ship.RoomAt(cell) is Room mr && mr.Id < _sootMarks.Length) _sootMarks[mr.Id]++;
            }
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            if (r.Fires > _fires[i])
            {
                _fires[i] = r.Fires;
                if (!r.Detached)
                {
                    if (_soot[i] < 0.05f) Stats.SootRooms++;
                    _soot[i] = MathF.Min(1f, _soot[i] + 0.75f);
                    _sootSince[i] = w.Tick;
                }
            }
            if (_soot[i] <= 0f) continue;
            if (w.Fire.Count > 0 && w.Fire.CountIn(r) > 0) { _sootSince[i] = w.Tick; continue; }
            float mul = 1f;
            if (w.Portable.SmellMul(r) < 1f) mul += 1.5f;            // 공기청정기
            if (_sootMarks[i] == 0) mul += 0.6f;                       // 벽 · 바닥 그을음을 닦았다
            if (r.VentOpen && r.AirFlow > 0.5f) mul += 0.3f;           // 환기
            if (_vent[i] >= 0 && w.Automation.MainOnline) mul += 0.4f; // 주컴퓨터가 환기량을 올렸다
            _soot[i] *= MathF.Exp(-0.018f * mul * h);
            if (_soot[i] < 0.02f) { _soot[i] = 0f; _vent[i] = -1; }
        }
    }

    public int SootMarks(Room r) => r.Id < _sootMarks.Length ? _sootMarks[r.Id] : 0;

    // ───────────────────────────── 죽음 → 빈자리 · 혼자 먹기 · 장소 ─────────────────────────────

    private void Deaths()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (!c.Dead || _dead.Contains(c.Id)) continue;
            _dead.Add(c.Id);
            OnDeathSeen(c);
        }
    }

    private void OnDeathSeen(CrewMember dead)
    {
        var w = _w;
        var m = Peek(dead);
        // 늘 앉던 자리
        int seat = -1;
        if (m != null && m.SeatUse.Count > 0)
        {
            int best = -1;
            foreach (var (id, n) in m.SeatUse) if (n > best) { best = n; seat = id; }
        }
        if (seat >= 0 && w.Ship.Furniture.FirstOrDefault(f => f.Id == seat) is Furniture sf && !Seats.Any(s => s.Seat == seat && s.Active))
        {
            var es = new EmptySeat { Seat = seat, Room = sf.Room.Id, Crew = dead.Id, Name = dead.Name, Since = w.Tick };
            foreach (var o in w.Crew) if (!o.Dead) es.Knew.Add(o.Id);
            Seats.Add(es);
            Stats.EmptySeats++;
            Traces.Add(new AfterTrace { Kind = AfterTraceKind.EmptySeat, Room = sf.Room.Id, At = sf.Cells[0], Tick = w.Tick, Crew = dead.Id, Item = seat, Event = $"{dead.Name}의 죽음", Text = $"{sf.Room.Name}의 빈 의자 — {Ko.IGa(dead.Name)} 늘 앉던 자리" });
        }
        // 남은 사람: 혼자 먹기 · 꿈거리
        foreach (var o in w.Crew)
        {
            if (o.Dead || o == dead) continue;
            float close = MathF.Max(0f, o.AffinityTo(dead));
            bool comrade = o.Memory.Comrades.Contains(dead.Id);
            bool saw = o.Room != null && o.Room == dead.Room;
            Impress(o, DreamKind.Loss, 0.25f + 0.75f * close + (comrade ? 0.2f : 0f) + (saw ? 0.2f : 0f), $"{dead.Name}의 죽음", dead.Id, dead.Room?.Id ?? -1, $"death:{dead.Id}");
            if (close > 0.3f || comrade)
            {
                var om = Mind(o);
                float wd = Math.Clamp(0.45f + 0.6f * close + (o.Habits.Contains(Habit.Loner) ? 0.15f : 0f) - 0.25f * o.Traits.Sociability, 0f, 1f);
                if (wd > om.Withdraw) { om.Withdraw = wd; om.WithdrawWhy = $"{Ko.EulReul(dead.Name)} 잃고"; om.WithdrawFrom = w.Tick; }
                Stats.Withdrawn++;
            }
        }
        // 장소: 그 사람이 쓰러진 자리
        if (dead.Room != null && w.Ship.Grid.InBounds(dead.Cell))
            AddPlace(dead.Cell, dead.Room, 0, $"{Ko.IGa(dead.Name)} 쓰러진 자리", dead.Id, witnessBoost: true);
    }

    // ───────────────────────────── 젖은 침구 ─────────────────────────────

    public IReadOnlyList<Furniture> BedList()
    {
        var w = _w;
        if (_beds == null || _bedsAt != w.Ship.Furniture.Count)
        {
            _bedsAt = w.Ship.Furniture.Count;
            _beds = w.Ship.Furniture.Where(f => f.Type is FurnitureType.Bed or FurnitureType.Cot && !f.Stowed).OrderBy(f => f.Id).ToList();
        }
        return _beds;
    }

    public float BedWet(Furniture bed) => WetBeds.TryGetValue(bed.Id, out var v) ? v : 0f;
    public DryLine? LineOf(Furniture bed) { foreach (var l in Lines) if (l.Bed == bed.Id && !l.Done) return l; return null; }
    /// <summary>침구를 걷어 널어 두었다 (맨 매트리스).</summary>
    public bool Bare(Furniture bed) => LineOf(bed) != null;

    private void Beds(float h)
    {
        var w = _w;
        foreach (var b in BedList())
        {
            if (b.Room.Detached || LineOf(b) != null) continue;
            float depth = MoistureSystem.Depth(b.Room);
            float mark = 0f;
            foreach (var cell in b.Cells) mark = MathF.Max(mark, w.Body.Mark(cell, CellMark.Wet));
            float cur = BedWet(b);
            if (depth > 0.03f || mark > 0.3f)
            {
                if (cur < 0.5f)
                {
                    Stats.WetBeds++;
                    var owner = b.Owner ?? w.Crew.FirstOrDefault(c => c.Bed == b);
                    if (owner != null && !owner.Dead)
                    {
                        MarkLog.Add(b.Room.Marks, w.Tick, $"{owner.Name}의 침구가 물에 젖었다");
                        Impress(owner, DreamKind.Water, 0.3f, $"{b.Room.Name} 물난리", -1, b.Room.Id, $"wetbed:{b.Room.Id}:{SimTime.Day(w.Tick)}");
                    }
                }
                WetBeds[b.Id] = 1f;
            }
            else if (cur > 0f)
            {
                // 그대로 두면 아주 천천히 마른다 (방이 따뜻하고 마르면 조금 빨리) — 그 사이 냄새가 밴다
                cur -= 0.012f * DryRate(b.Room) * h;
                if (cur <= 0.05f) WetBeds.Remove(b.Id); else WetBeds[b.Id] = cur;
                if (cur > 0.4f) b.Room.Humidity = MathF.Min(1f, b.Room.Humidity + 0.004f * h);
            }
        }
        // 널어 둔 침구가 마른다
        foreach (var l in Lines)
        {
            if (l.Done) continue;
            var room = RoomById(l.Room);
            if (room == null) continue;
            if (l.Wet > 0f)
            {
                float rate = 0.06f * DryRate(room);
                l.Wet = MathF.Max(0f, l.Wet - rate * h);
                room.Humidity = MathF.Min(1f, room.Humidity + 0.02f * h * MathF.Min(1f, l.Wet + 0.2f)); // 젖은 천이 방을 눅눅하게
                if (l.Wet < 0.06f && l.Dried < 0) { l.Wet = 0f; l.Dried = w.Tick; }
            }
            if (l.Dried >= 0 && !l.Forgot && w.Tick - l.Dried > SimTime.Hours(16))
            {
                l.Forgot = true;
                Stats.ForgotLines++;
                var owner = Crew(l.Owner);
                w.Log.Add(w.Tick, LogKind.Life, $"{room.Name}에 {(owner != null ? owner.Name + "의 " : "")}침구가 마른 채 그대로 걸려 있다", l.Owner);
                MarkLog.Add(room.Marks, w.Tick, $"걷지 않은 침구{(owner != null ? $" ({owner.Name})" : "")}");
            }
        }
    }

    /// <summary>그 방에서 젖은 천이 마르는 빠르기 (온도 · 습도 · 히터 · 세탁실 · 제습기).</summary>
    public float DryRate(Room r)
    {
        float t = Math.Clamp((r.Air.Temperature - 5f) / 15f, 0.15f, 2f);
        float dry = Math.Clamp(1.2f - r.Humidity, 0.15f, 1f);
        float k = t * dry;
        if (_w.Portable.Devices.Any(d => d.Kind == PortableKind.Heater && d.Running && d.Placed && _w.Ship.RoomAt(d.At) == r)) k *= 1.8f;
        if (r.Kind == RoomType.Laundry || ModulesV15.Has(r, FurnitureType.SuitDryer) || ModulesV15.Has(r, FurnitureType.Dehumidifier)) k *= 1.5f;
        if (MoistureSystem.Depth(r) > 0.01f) k *= 0.2f;
        return k;
    }

    // ───────────────────────────── 정전 뒤 냉장고 ─────────────────────────────

    private static bool FridgeDown(Furniture f) => f.Machine is Machine m && (m.Stopped || m.Efficiency < 0.2f);

    private void FridgeWatch()
    {
        var w = _w;
        Furniture? down = null;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.Fridge)) if (FridgeDown(f)) { down = f; break; }
        if (down != null)
        {
            if (_fridgeOff < 0) { _fridgeOff = w.Tick; _fridgeId = down.Id; _potMax.Clear(); }
            _fridgeUp = -1; // 잠깐 돌다 또 멈췄다 — 같은 정전으로 본다
            foreach (var b in w.Cooking.Batches)
                if (b.InFridge && !b.Spoiled) _potMax[b.Id] = MathF.Max(_potMax.GetValueOrDefault(b.Id, 4f), b.Temp);
            return;
        }
        if (_fridgeOff < 0) return;
        // 다시 돈다: 30분은 제대로 도는 걸 보고 나서 고른다
        if (_fridgeUp < 0) { _fridgeUp = w.Tick; return; }
        if (w.Tick - _fridgeUp < SimTime.Minutes(30)) return;
        float hrs = (_fridgeUp - _fridgeOff) / (float)SimTime.TicksPerHour;
        long off = _fridgeOff, on = _fridgeUp;
        _fridgeOff = -1;
        _fridgeUp = -1;
        var pots = w.Cooking.Batches.Where(b => b.InFridge && !b.Spoiled && !b.Jar && b.Portions > 0).ToList();
        if (hrs < 1.5f || pots.Count == 0) return;
        var fs = new FridgeSort { Fridge = _fridgeId, Off = off, On = on };
        foreach (var b in pots)
        {
            float mt = _potMax.GetValueOrDefault(b.Id, b.Temp);
            fs.MaxTemp[b.Id] = mt;
            // 주컴퓨터는 냄새를 못 맡는다 — 온도 기록만 본다: 10℃를 넘긴 냄비는 버리라고 (넉넉하게)
            if (mt >= 10f) fs.ComputerSaysThrow.Add(b.Id);
        }
        Fridges.Add(fs);
        if (Fridges.Count > 12) Fridges.RemoveAt(0);
        var fr = w.Ship.Furniture.FirstOrDefault(f => f.Id == fs.Fridge);
        var room = fr?.Room;
        w.Log.Add(w.Tick, LogKind.Ship, $"냉장고가 다시 돈다 — {hrs:0.#}시간 멈춰 있었다 · 냄비 {pots.Count}개를 골라야 한다");
        w.Automation.Book.Add(ActKind.Advice, room, $"냉장고 {hrs:0.#}시간 정지 · 냄비 {pots.Count}개 · 가장 높이 오른 온도 {fs.MaxTemp.Values.Max():0}℃",
            $"판단: 10℃를 넘긴 냄비 {fs.ComputerSaysThrow.Count}개는 상했을 수 있다 (온도 기록 — 냄새는 모른다)",
            "조치: 냄비마다 온도 기록을 조리사 화면에 띄움", $"요청: {(fs.ComputerSaysThrow.Count > 0 ? $"{fs.ComputerSaysThrow.Count}개는 버리고 " : "")}나머지는 먼저 먹기", $"after:fridge:{w.Tick}");
    }

    // ───────────────────────────── 개인 조명 ─────────────────────────────

    /// <summary>작업등만 켜진 어두운 방 (차고 눈부신 빛).</summary>
    public bool Glaring(Room r) => r.Id < _lampSince.Length && _lampSince[r.Id] >= 0 && !Lamps.Any(l => l.Active && l.To == r.Id);
    public LampMove? LampIn(Room r) { foreach (var l in Lamps) if (l.Active && l.To == r.Id) return l; return null; }

    /// <summary>그 사람 선실의 등 (침대 곁 · 아직 옮기지 않은).</summary>
    public PlacedProp? LampOf(CrewMember c)
    {
        if (c.Bed is not Furniture bed || bed.Room.Detached) return null;
        PlacedProp? best = null;
        float bd = float.MaxValue;
        foreach (var p in bed.Room.Decor)
        {
            if (p.Spec.Shape != PropShape.Lamp || Lamps.Any(l => l.Prop == p.Id && l.Returned < 0)) continue;
            float d = (p.At.Center - bed.Center).LengthSquared() - (p.Maker == c.Id ? 100f : 0f);
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    private static bool Sensitive(CrewMember c) => c.Habits.Contains(Habit.Bookworm) || c.Habits.Contains(Habit.Insomniac) || c.Habits.Contains(Habit.Worrier)
        || c.Habits.Contains(Habit.Gazer) || c.Habits.Contains(Habit.Homesick) || c.Habits.Contains(Habit.NeatFreak) || c.Fears.Contains(Fear.Dark) || c.Hobbies.Contains(Hobby.Reading);

    private static bool Resting(CrewMember c) => c.Job?.Activity is EatActivity or RelaxActivity or HobbyActivity or ChatActivity or SceneActivity or SharedMealActivity;

    private void LampWatch()
    {
        var w = _w;
        var rooms = w.Ship.Rooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            bool dark = (r.LightsOut || !r.Powered) && !r.Detached;
            bool work = dark && r.PortableLit > 0 && w.Portable.Devices.Any(d => d.Kind == PortableKind.WorkLamp && d.Running && d.Placed && w.Ship.RoomAt(d.At) == r);
            if (work) { if (_lampSince[i] < 0) _lampSince[i] = w.Tick; }
            else _lampSince[i] = -1;
            var lm = LampIn(r);
            // 다시 밝아졌다 → 돌려 가져갈지 · 둘지
            if (lm != null && !dark)
            {
                if (lm.LitAgain < 0) lm.LitAgain = w.Tick;
                else if (!lm.Stays && !lm.WantBack && w.Tick - lm.LitAgain > SimTime.Hours(1) && Crew(lm.Owner) is CrewMember own)
                {
                    var su = Setups.FirstOrDefault(s => s.Kind == 1 && s.Lamp == lm.Id);
                    bool keep = own.Habits.Contains(Habit.Generous) || own.Habits.Contains(Habit.Cheerful) || own.Habits.Contains(Habit.Optimist) || (su?.Uses ?? 0) >= 4 || own.Dead;
                    if (keep)
                    {
                        lm.Stays = true;
                        Stats.LampStays++;
                        w.Log.Add(w.Tick, LogKind.Life, $"{r.Name} 조명이 돌아왔지만 {Ko.IGa(own.Name)} 가져온 등은 그대로 두기로 했다 — 저녁엔 그 불빛이 낫다고", own.Id);
                        if (!own.Dead) Life.Diary(w, own, Persona.Say(own, $"{r.Name}에 둔 등은 그냥 거기 두기로 했다. 다들 그 아래 모이니까"));
                    }
                    else lm.WantBack = true;
                }
            }
            if (lm != null && dark) lm.LitAgain = -1;
            if (!work || lm != null || Lamps.Any(l => l.To == r.Id && l.Moved < 0 && l.Returned < 0)) continue;
            if (w.Tick - _lampSince[i] < SimTime.Hours(1.5f)) continue;
            // 작업등 아래에서 쉬는 사람: 눈부시다 → 선실의 등을 가져오기로
            CrewMember? who = null;
            foreach (var c in w.Crew)
            {
                if (!Adult(c) || c.Room != r || !c.IsAwake || !c.CanAct || !Resting(c) || !Sensitive(c) || LampOf(c) is null) continue;
                if (who == null || c.Id < who.Id) who = c;
            }
            if (who == null) continue;
            var prop = LampOf(who)!;
            Lamps.Add(new LampMove { Id = _ids++, Owner = who.Id, Prop = prop.Id, From = prop.RoomId, Home = prop.At, To = r.Id, Asked = w.Tick });
            Stats.LampAsks++;
            who.Say(w, Persona.Say(who, "작업등 불빛이 너무 차갑네 — 내 등을 가져올게"));
            w.Log.Add(w.Tick, LogKind.Life, $"{r.Name}의 작업등이 눈부시다 — {Ko.IGa(who.Name)} 선실의 {Ko.EulReul(prop.Spec.Name)} 가져오러 간다", who.Id);
        }
        // 눈부심: 작업등 아래에서 쉬는 사람은 마음이 덜 풀린다 · 옮겨 온 등 아래에서는 풀린다
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room is not Room cr || !c.IsAwake || !Resting(c)) continue;
            if (Glaring(cr)) { c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.004f); if (R.Chance(0.01f)) Stats.Glare++; }
            else if (LampIn(cr) != null && (cr.LightsOut || !cr.Powered)) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.003f); c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.002f); }
        }
        // 굳은 등: 회의에서 정식으로 등을 달았으면 주인의 등은 집으로
        foreach (var lm in Lamps)
        {
            if (!lm.Active || lm.Carrying >= 0) continue;
            var su = Setups.FirstOrDefault(s => s.Kind == 1 && s.Lamp == lm.Id);
            if (su?.State == "완료" && !lm.WantBack) { lm.WantBack = true; lm.Stays = false; }
        }
    }

    // ───────────────────────────── 불탄 그림 ─────────────────────────────

    /// <summary>Props.Burn 훅: 불에 탄 소품 중 그림 · 사진 · 아이 그림을 다시 그릴 거리로 적는다.</summary>
    public void OnPropBurned(PlacedProp p, Room room)
    {
        if (Off) return;
        var s = p.Spec;
        if (s.Maker != Hobby.Painting) return; // 그림만 (사진 · 포스터 · 아이 그림은 다시 그리지 않는다)
        Burned.Add(new BurnedPic { Spec = s.Id, Room = room.Id, At = p.At, Maker = p.Maker, Tick = _w.Tick, Name = p.Name });
        Stats.Burned++;
        if (Burned.Count > 24) Burned.RemoveAt(0);
    }

    // ───────────────────────────── 한 시간마다: 굳음 · 주컴퓨터 ─────────────────────────────

    private void Hourly()
    {
        var w = _w;
        Harden();
        // 주컴퓨터: 공기 감지기의 그을음 입자 → 환기량을 올리고 공기청정기 · 벽 닦기를 부탁한다
        var rooms = w.Ship.Rooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (_soot[i] < 0.35f || _vent[i] >= 0 || w.Fire.Count > 0 && w.Fire.CountIn(rooms[i]) > 0) continue;
            if (w.Tick - _sootSince[i] < SimTime.Hours(1)) continue;
            var r = rooms[i];
            int away = 0;
            foreach (var (_, m) in Minds) if (m.AwayRoom == r.Id && w.Tick - m.AwayLast < SimTime.Hours(12)) away++;
            float days = MathF.Log(_soot[i] / 0.1f) / (0.018f * 2f) / 24f;
            var act = w.Automation.Book.Add(ActKind.Advice, r, $"{r.Name} 공기 그을음 입자 {_soot[i] * 100:0} · 불 끈 지 {(w.Tick - _sootSince[i]) / (float)SimTime.TicksPerHour:0}시간 · 남은 그을음 {SootMarks(r)}칸",
                $"판단: 냄새가 {days:0.#}일쯤 남는다" + (away > 0 ? $" — 식사가 {away}명 다른 방으로 흩어졌다" : ""), "조치: 이 방 환기량을 올림",
                "요청: 공기청정기 · 벽 그을음 닦기", $"after:soot:{r.Id}", SimTime.Hours(12));
            if (act == null) continue;
            _vent[i] = w.Tick;
            Stats.VentBoosts++;
            Stats.SootAdvice++;
        }
        SleepAdvice();
        FridgeLessons();
        LampUses();
        // 흔적: 오래된 쟁반 자국은 지운다
        AwayPlates.RemoveAll(p => w.Tick - p.tick > SimTime.Hours(10));
    }

    private void Harden()
    {
        var w = _w;
        foreach (var su in Setups)
        {
            if (su.Plan >= 0)
            {
                var p = w.RoomPlans.Plans.FirstOrDefault(x => x.Id == su.Plan);
                string st = p?.State ?? "없음";
                if (st != su.State)
                {
                    su.State = st;
                    if (st == "완료")
                    {
                        Stats.Hardened++;
                        var room = RoomById(su.Room);
                        Traces.Add(new AfterTrace
                        {
                            Kind = su.Kind == 0 ? AfterTraceKind.DiningCorner : AfterTraceKind.LampFixture, Room = su.Room, At = room?.Cells.FirstOrDefault() ?? default, Tick = w.Tick,
                            Crew = p!.Proposer, Event = su.Why,
                            Text = su.Kind == 0 ? $"{room?.Name}의 식탁 자리 — {su.Why} 며칠 여기서 먹다 굳었다" : $"{room?.Name}에 따로 단 등 — {su.Why}",
                        });
                    }
                }
                continue;
            }
            if (su.Plan == -2) continue;
            var r = RoomById(su.Room);
            if (r == null || r.Detached) { su.Plan = -2; continue; }
            bool ready = su.Kind == 0 ? su.Uses >= 5 && su.Days.Count >= 2 : w.Tick - su.First > SimTime.Hours(36);
            if (!ready) continue;
            if (su.Kind == 1 && Lamps.FirstOrDefault(l => l.Id == su.Lamp) is { Stays: false } or null) continue;
            // 꾸밀 자리: 식탁 (앉은 사람이 비켜 줄 필요 없는 칸부터)
            var anchor = r.Furniture.Where(f => f.UseSpots.Count > 0 && !f.Stowed)
                .OrderBy(f => (f.Type == FurnitureType.Table ? 0 : f.Type == FurnitureType.GameTable ? 1 : f.Type == FurnitureType.Seat ? 3 : 2)
                              + (w.Ship.FurnitureAt(f.UseSpots[0]) is { Type: FurnitureType.Seat } ? 2 : 0))
                .ThenBy(f => f.Id).FirstOrDefault();
            if (anchor == null) { su.Plan = -2; continue; }
            // 낸 사람: 거기서 가장 많이 먹은 사람 (임시 식탁) · 등 주인 (옮겨 둔 등)
            CrewMember? by = null;
            if (su.Kind == 0) { int best = 0; foreach (var (id, n) in su.Users) if (n > best && Crew(id) is CrewMember c0 && Adult(c0)) { best = n; by = c0; } }
            else by = Lamps.FirstOrDefault(l => l.Id == su.Lamp) is LampMove lm0 ? Crew(lm0.Owner) : null;
            if (by == null || !Adult(by)) { su.Plan = -2; continue; }
            string prop = su.Kind == 0 ? "tablecloth" : "readlamp";
            string title = su.Kind == 0 ? $"{r.Name} 한쪽을 식탁 자리로 — {Props.Get(prop).Name}" : $"{r.Name}에 등을 따로 하나 — {by.Name}의 등은 선실로";
            string why = su.Kind == 0 ? $"{su.Why} 며칠 여기서 먹어 보니 좋았다" : $"{su.Why} 가져다 둔 등 아래 다들 모인다";
            var draft = new RoomPlan { Id = 0, Kind = RoomPlanKind.Decorate, Proposer = by.Id, RoomId = r.Id, FurnitureId = anchor.Id, Title = title, Why = why, Source = "겪은 일", Prop = prop, Proposed = w.Tick };
            var plan = w.RoomPlans.Propose(draft);
            su.Plan = plan.Id;
            su.State = plan.State;
            Stats.Proposed++;
        }
    }

    /// <summary>옮겨 둔 등 아래 모인 사람을 센다 (굳음의 근거).</summary>
    private void LampUses()
    {
        var w = _w;
        foreach (var lm in Lamps)
        {
            if (!lm.Active || RoomById(lm.To) is not Room r) continue;
            var su = Setups.FirstOrDefault(s => s.Kind == 1 && s.Lamp == lm.Id);
            if (su == null) continue;
            int n = 0;
            foreach (var c in w.Crew) if (!c.Dead && c.Room == r && c.IsAwake && Resting(c)) n++;
            if (n == 0) continue;
            su.Uses += n;
            su.Last = w.Tick;
            su.Days.Add(SimTime.Day(w.Tick));
        }
    }

    internal TempSetup Setup(byte kind, Room r, string why, int lamp = -1)
    {
        foreach (var s in Setups) if (s.Kind == kind && s.Room == r.Id && (kind == 0 || s.Lamp == lamp) && s.Plan != -2) return s;
        var su = new TempSetup { Kind = kind, Room = r.Id, First = _w.Tick, Last = _w.Tick, Why = why, Lamp = lamp };
        Setups.Add(su);
        return su;
    }

    internal void AddTrace(AfterTrace t)
    {
        Traces.Add(t);
        if (Traces.Count > 200) Traces.RemoveAt(0);
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Seats.Count); I(Lines.Count); I(Fridges.Count); I(Lamps.Count); I(Repaints.Count); I(Places.Count); I(Traces.Count); I(Setups.Count);
        foreach (var v in _soot) F(v);
        foreach (var (id, m) in Minds) { I(id); F(m.Withdraw); I(m.Dreams); I(m.Nightmares); I(m.AwayMeals); }
        foreach (var l in Lines) { I(l.Bed); F(l.Wet); I(l.Fetched); }
        I(Stats.Thrown); I(Stats.Kept); I(Stats.Pauses); I(Stats.TableTalks); I(Stats.Repainted);
    }
}
