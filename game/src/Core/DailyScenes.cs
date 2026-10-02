using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.1 일상의 실제 행동화: 일상 사건이 "기록 한 줄"이 아니라 장면으로 일어난다.
// 공통 틀 (DailyScene): 계기 → 초대/참여 판단 → 이동 → 진행 → 중단(경보 · 위기 · 잠 · 부상) / 재개 → 결과.
// 진행 상태(진척 · 놓인 물건 · 참여자)는 장면에 남는다 — 끊기면 그 자리에 남았다가 돌아와 이어 한다.
// 결과(기분 · 관계 · 일기 · 기록)는 끝까지 한 만큼만 남는다.
// 같은 틀 위에: 체스 · 커피 · 영화의 밤 · 엎지른 국 · 한밤의 간식 · 몽유병 · 소품 제작.
// 교대 인수인계: 근무가 끝날 때 아는 것(이상한 소리 · 미완료 작업)을 말 · 메모로 넘긴다 — 빠뜨리면 다음 근무자는 모른다.
// 쪽지 · 게시판: 냉장고 쪽지 · 당번표 낙서 · 돌려 가며 서명하는 생일 카드 · 인수인계 메모 — 물건으로 남고, 읽은 사람만 안다.

public enum SceneKind { Chess, Coffee, Movie, Spill, Snack, Sleepwalk, Craft }
public enum SceneStage { Gather, Run, Paused, Done, Dropped }
public enum ThingKind { Board, Cup, Stain, Screen, Work, Snack }

/// <summary>장면에 놓인 물건 하나 (판 · 잔 · 자국 · 스크린 · 만들다 만 것).</summary>
public sealed class SceneThing
{
    public ThingKind Kind { get; init; }
    public Cell At { get; set; }
    public int Owner { get; init; } = -1;
    public long Since { get; init; }
    public long Until { get; set; } = -1;
    public bool Tea { get; init; }
    /// <summary>남은 정도 (자국 · 잔에 남은 양).</summary>
    public float Amount { get; set; } = 1f;
}

/// <summary>일상 장면 하나: 누가 · 어디서 · 얼마나 했나 · 무엇이 놓였나 · 몇 번 끊겼나.</summary>
public sealed class DailyScene
{
    public int Id { get; init; }
    public SceneKind Kind { get; init; }
    public string Title { get; set; } = "";
    public SceneStage Stage { get; set; } = SceneStage.Gather;
    public long Opened { get; init; }
    public long Closed { get; set; } = -1;
    /// <summary>연 사람 (청한 사람 · 타는 사람 · 엎은 사람 · 먹는 사람 · 걷는 사람 · 만드는 사람).</summary>
    public int Host { get; init; } = -1;
    /// <summary>상대 · 닦는 사람 · 찾아낸 당직.</summary>
    public int Other { get; set; } = -1;
    public int RoomId { get; set; } = -1;
    /// <summary>연 사람의 자리 · 커피 타는 곳 · 국 자국 · 일하는 자리.</summary>
    public Cell Spot { get; set; }
    /// <summary>상대 자리 · 스크린 · 작업대 · 냉장고.</summary>
    public Cell Spot2 { get; set; }
    /// <summary>탁자 칸 (판이 놓인 곳).</summary>
    public Cell Table { get; set; }
    /// <summary>진척 0~1.</summary>
    public float Progress { get; set; }
    public List<int> Invited { get; } = new();
    public List<int> Declined { get; } = new();
    /// <summary>한 번이라도 온 사람 (온 순서).</summary>
    public List<int> Joined { get; } = new();
    /// <summary>지금 자리에 있는 사람.</summary>
    public List<int> Here { get; } = new();
    /// <summary>자리가 모자라 바닥에 앉은 사람.</summary>
    public List<int> Floor { get; } = new();
    /// <summary>함께한 만큼 (결과는 이만큼만).</summary>
    public Dictionary<int, float> Share { get; } = new();
    /// <summary>본 사람 (자국 · 몽유병).</summary>
    public List<int> Seen { get; } = new();
    /// <summary>커피를 받을 사람 (순서대로) · 놓아 준 수.</summary>
    public List<int> Targets { get; } = new();
    public int Delivered { get; set; }
    public List<SceneThing> Things { get; } = new();
    public string? PauseWhy { get; set; }
    public int Pauses { get; set; }
    public long PausedAt { get; set; } = -1;
    /// <summary>체스: 판(HobbyGame) · 체스판(Belonging).</summary>
    public int Game { get; set; } = -1;
    public int Item { get; set; } = -1;
    /// <summary>소품: 무엇을 · 어느 방에 · 재료를 썼나.</summary>
    public PropSpec? Prop { get; set; }
    public int PropRoom { get; set; } = -1;
    public bool Material { get; set; }
    /// <summary>손에 들었다 (커피 · 걸레 · 다 만든 소품 · 당직이 팔을 잡았다).</summary>
    public bool Holding { get; set; }
    public bool Tea { get; set; }
    /// <summary>간식: 남이 둔 것이었다 · 본 사람.</summary>
    public int Victim { get; set; } = -1;
    public int Witness { get; set; } = -1;
    /// <summary>몽유병: 당직이 아침에 말해 줬다 · 개인 비서(주 컴퓨터)가 감지 기록을 알려 줬다.</summary>
    public bool Told { get; set; }
    public bool PcTold { get; set; }
    public float Hours { get; set; } = 1f;
    /// <summary>연 사람이 자리에 온 때 · 끝까지 함께한 사람 수.</summary>
    public long Since { get; set; } = -1;
    public int Finished { get; set; }
    /// <summary>주 컴퓨터가 이 장면을 보고 한 일 (몽유병: 복도 움직임을 보고 당직 호출 · 영화: 잠든 옆방 때문에 볼륨을 낮춤) — 다섯 칸 기록 번호.</summary>
    public int ComputerAct { get; set; } = -1;
    /// <summary>간식: 이름표 붙은 남의 접시를 먹었다 (Cooking 접시 번호).</summary>
    public int Plate { get; set; } = -1;
    /// <summary>영화: 주 컴퓨터 오락 보관함에서 트는 영화 (보관함이 꺼지면 멈춘다) — 없으면 가져온 파일.</summary>
    public string? Film { get; set; }
    /// <summary>국: 미끄러진 사람 (배 본체 바닥 규칙으로 넘어진 사람).</summary>
    public List<int> Slipped { get; } = new();
    public List<string> Trail { get; } = new();
    public bool Open => Stage is SceneStage.Gather or SceneStage.Run or SceneStage.Paused;
}

public enum NoteKind { Fridge, Roster, Card, Memo }

/// <summary>쪽지 · 게시물 한 장: 위치 · 쓴 사람 · 내용 · 읽은 사람 (읽은 사람만 안다).</summary>
public sealed class ShipNote
{
    public int Id { get; init; }
    public NoteKind Kind { get; init; }
    public Cell At { get; set; }
    public int RoomId { get; set; }
    public int Author { get; init; } = -1;
    public string Text { get; set; } = "";
    public long Written { get; init; }
    /// <summary>받을 사람 · 다룬 사람 (메모: 다음 근무자 · 카드: 생일인 사람 · 낙서: 놀림 받은 사람).</summary>
    public int For { get; init; } = -1;
    /// <summary>이름 없이 (낙서) — 읽은 사람은 누가 썼는지 모른다.</summary>
    public bool Anonymous { get; init; }
    public List<int> Readers { get; } = new();
    public List<int> Signers { get; } = new();
    /// <summary>메모에 적힌 것 (인수인계).</summary>
    public List<Concern> Items { get; } = new();
    public int Thief { get; init; } = -1;
    public int Witness { get; init; } = -1;
    public bool Given { get; set; }
    public bool Gone { get; set; }
    /// <summary>주 컴퓨터가 받을 사람에게 "게시판에 메모가 있다"고 알렸다.</summary>
    public bool Pinged { get; set; }
}

public enum ConcernKind { Sound, Unfinished }

/// <summary>한 사람이 아는 이상 (세계 ≠ 아는 것): 직접 들음 · 말로 들음 · 메모로 읽음.</summary>
public sealed class Concern
{
    public int Id { get; init; }
    public int Who { get; init; }
    public ConcernKind Kind { get; init; }
    public Machine? Machine { get; init; }
    public Furniture? Place { get; init; }
    public string Text { get; init; } = "";
    public string How { get; init; } = "직접 들음";
    public int From { get; init; } = -1;
    public long Since { get; init; }
    public bool Passed { get; set; }
    public bool Checked { get; set; }
    public string? Result { get; set; }
}

/// <summary>교대 인수인계 한 번: 누가 누구에게 · 넘긴 것 · 빠뜨린 것 · 말 · 메모.</summary>
public sealed class Handoff
{
    public int Id { get; init; }
    public int From { get; init; }
    public int To { get; init; } = -1;
    public long Tick { get; init; }
    public List<string> Told { get; } = new();
    public List<string> Dropped { get; } = new();
    public bool Verbal { get; set; }
    public int Memo { get; set; } = -1;
    public string? Why { get; set; }
}

public sealed class SceneStats
{
    public int Opened, Done, Dropped, Paused, Resumed, Late, FloorSeats, Cups, Empty, Cleaned, Snacks, Escorts, Crafts,
        Handoffs, Verbal, Memos, Omitted, Checks, Found, Notes, Reads, Signed, Laughs, Annoyed, Burned, Slips, Hushed, PowerCuts,
        PcPages, PcQuiet, PcMemo, PcStock, Plates, Dark;
    public string Summary() =>
        $"장면 {Opened}(끝 {Done} · 접음 {Dropped} · 멈춤 {Paused} · 이어 함 {Resumed} · 늦게 옴 {Late} · 바닥 {FloorSeats}) · 커피 {Cups}잔(통 빔 {Empty}) · 닦음 {Cleaned} · 간식 {Snacks} · 침대로 {Escorts} · 소품 {Crafts} · " +
        $"인수인계 {Handoffs}(말 {Verbal} · 메모 {Memos} · 빠뜨림 {Omitted}) · 확인 {Checks}(찾음 {Found}) · 쪽지 {Notes}(읽음 {Reads} · 서명 {Signed} · 웃음 {Laughs} · 짜증 {Annoyed} · 불에 탐 {Burned}) · 미끄러짐 {Slips} · 소리 줄임 {Hushed} · 정전 {PowerCuts} · 어두움 {Dark} · " +
        $"이름표 접시 {Plates} · 컴퓨터(당직 호출 {PcPages} · 볼륨 {PcQuiet} · 메모 알림 {PcMemo} · 재고 {PcStock})";
}

public sealed class DailySceneSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6421 + 157));
    public SceneStats Stats { get; } = new();
    public List<DailyScene> Scenes { get; } = new();
    public List<ShipNote> Notes { get; } = new();
    public List<Concern> Concerns { get; } = new();
    public List<Handoff> Handoffs { get; } = new();
    private int _nextScene = 1, _nextNote = 1, _nextConcern = 1, _nextHand = 1;
    private bool[] _onDuty = Array.Empty<bool>();
    private long[] _dutyStart = Array.Empty<long>();
    private long[] _boardSeen = Array.Empty<long>();
    private readonly Dictionary<int, int> _jobScene = new();
    private long _nextHour = -1, _nextRead = -1;
    private readonly List<(int victim, int thief, int witness, long at, Cell fridge, bool plate)> _missing = new();

    /// <summary>시험용: true면 인수인계 때 모두 빠뜨리고, false면 하나도 빠뜨리지 않는다 (null = 사람마다).</summary>
    public bool? ForceOmit { get; set; }

    public DailySceneSystem(World w) => _w = w;

    public IEnumerable<DailyScene> Active => Scenes.Where(s => s.Open);

    // ═══════════════════════════════ 손잡이 ═══════════════════════════════

    internal CrewMember? CrewOf(int id) => id < 0 ? null : id < _w.Crew.Count && _w.Crew[id].Id == id ? _w.Crew[id] : _w.Crew.FirstOrDefault(c => c.Id == id);

    private static bool Able(CrewMember c) => !c.Dead && !c.Down && c.CanAct && !c.Outside && c.Room != null;

    /// <summary>다른 장면에 묶여 있다 (연 사람 · 상대 · 지금 자리에 있음).</summary>
    public bool Busy(CrewMember c)
    {
        foreach (var s in Scenes)
            if (s.Open && (s.Host == c.Id || s.Other == c.Id || s.Here.Contains(c.Id) || s.Invited.Contains(c.Id) && s.Kind is SceneKind.Chess or SceneKind.Movie)) return true;
        return false;
    }

    private bool OnDuty(CrewMember c) => WatchLog.OnShift(c, _w) || _w.Society.OnNightWatch(c);

    private bool Bedtime(CrewMember c) => SimTime.InWindow(SimTime.HourOfDay(_w.Tick), c.Schedule.SleepStart, c.Schedule.SleepLength);

    private static string Pct(float p) => $"{Math.Clamp(p, 0f, 1f) * 100:0}%";

    private void Log(string text, int who = -1) => _w.Log.Add(_w.Tick, LogKind.Life, text, who);

    private void Trail(DailyScene s, string text) { s.Trail.Add($"{SimTime.Clock(_w.Tick)} {text}"); if (s.Trail.Count > 30) s.Trail.RemoveAt(0); }

    private void Diary(CrewMember c, string text) => Life.Diary(_w, c, Persona.Say(c, text));

    private void Say(CrewMember c, string text) => c.Say(_w, Persona.Say(c, text));

    private DailyScene New(SceneKind kind, CrewMember host, string title, Room room)
    {
        var s = new DailyScene { Id = _nextScene++, Kind = kind, Host = host.Id, Title = title, Opened = _w.Tick, RoomId = room.Id };
        Scenes.Add(s);
        Stats.Opened++;
        Trail(s, $"{host.Name}: {title}");
        if (Scenes.Count > 80) Scenes.RemoveAll(x => !x.Open && _w.Tick - x.Closed > SimTime.TicksPerDay);
        return s;
    }

    private Room? RoomById(int id) => id >= 0 && id < _w.Ship.Rooms.Count ? _w.Ship.Rooms[id] : null;

    /// <summary>방 안의 빈 바닥 칸 (가까운 순 · 결정적).</summary>
    private Cell? FreeCell(Room room, Cell near, CrewMember? me, int skip = 0, Func<Cell, bool>? ok = null)
    {
        var ship = _w.Ship;
        var list = room.Cells.Where(x => ship.IsWalkable(x) && ship.FurnitureAt(x) == null && !_w.Portable.Occupied(x) && (me == null || !_w.IsSpotTaken(x, me)) && (ok == null || ok(x))) // 꺼내 둔 이동식 장비 · 카트 자리는 비켜 앉는다
            .OrderBy(x => Math.Abs(x.X - near.X) + Math.Abs(x.Y - near.Y)).ThenBy(x => x.Y).ThenBy(x => x.X).Skip(skip).Take(1).ToList();
        return list.Count == 0 ? null : list[0];
    }

    /// <summary>벽에 붙은 칸 (게시판 · 스크린).</summary>
    private Cell? WallCell(Room room)
    {
        var g = _w.Ship.Grid;
        var c0 = Cell.FromPosition(room.Center);
        foreach (var x in room.Cells.OrderBy(x => Math.Abs(x.X - c0.X) + Math.Abs(x.Y - c0.Y)).ThenBy(x => x.Y).ThenBy(x => x.X))
        {
            if (!_w.Ship.IsWalkable(x) || _w.Ship.FurnitureAt(x) != null) continue;
            for (int i = 0; i < 4; i++)
                if (g.Kind(x + Cell.Dirs4[i]) == TileKind.Wall && _w.Ship.DoorAt(x + Cell.Dirs4[i]) == null) return x;
        }
        return null;
    }

    /// <summary>가구 곁의 서는 칸.</summary>
    private Cell? SpotBy(Furniture f) => f.UseSpots.Where(_w.Ship.IsWalkable).Cast<Cell?>().FirstOrDefault()
        ?? f.Cells.SelectMany(c => Cell.Dirs4.Select(d => c + d)).Where(x => _w.Ship.IsWalkable(x) && _w.Ship.FurnitureAt(x) == null).OrderBy(x => x.Y).ThenBy(x => x.X).Cast<Cell?>().FirstOrDefault();

    private bool Usable(Room r) => !r.OffLimits && !r.Abandoned && !r.Detached;

    // ═══════════════════════════════ 일상 사건이 장면을 연다 ═══════════════════════════════

    /// <summary>체스: 체스판을 가져와 탁자에 펴고 상대를 기다린다.</summary>
    public bool Chess(DailyCtx x)
    {
        if (x.One(c => x.Likes(c, Hobby.Chess) && !Busy(c) && _w.Belongings.ItemFor(c, Hobby.Chess) != null) is not CrewMember c) return false;
        if (x.Other(c, o => (x.Likes(o, Hobby.Chess) || o.Traits.Calm > 0.6f) && !Busy(o)) is not CrewMember o) return false;
        var s = OpenChess(c, o);
        return s != null && x.Done($"{Ko.IGa(c.Name)} {o.Name}에게 체스 한 판을 청했다" + (s.Declined.Count > 0 ? $" — {Ko.EunNeun(o.Name)} 지금은 어렵단다" : ""), c, o);
    }

    public DailyScene? OpenChess(CrewMember a, CrewMember b)
    {
        var bs = _w.Belongings;
        var set = bs.ItemFor(a, Hobby.Chess) ?? bs.ItemFor(b, Hobby.Chess);
        if (set == null || !Able(a)) return null;
        // 탁자: 게임 탁자 → 휴게실 · 식당 · 극장의 탁자 → 아무 탁자 (다른 판이 없는 곳)
        var used = Scenes.Where(s => s.Open && s.Kind == SceneKind.Chess).Select(s => s.Table).ToList();
        Furniture? table = null;
        Cell sa = default, sb = default;
        foreach (var f in _w.Ship.Furniture.Where(f => f.Type is FurnitureType.GameTable or FurnitureType.Table && Usable(f.Room) && !f.Stowed)
                     .OrderBy(f => f.Type == FurnitureType.GameTable ? 0 : f.Room.Type is RoomType.Lounge ? 1 : f.Room.Type is RoomType.Mess or RoomType.Theater ? 2 : 3).ThenBy(f => f.Id))
        {
            if (used.Any(u => f.Cells.Contains(u))) continue;
            var seats = f.Cells.SelectMany(c => Cell.Dirs4.Select(d => c + d)).Distinct()
                .Where(x => _w.Ship.IsWalkable(x) && _w.Ship.FurnitureAt(x) is null or { Type: FurnitureType.Seat }).OrderBy(x => x.Y).ThenBy(x => x.X).ToList();
            if (seats.Count < 2) continue;
            table = f;
            sa = seats[0];
            sb = seats.OrderByDescending(x => Math.Abs(x.X - sa.X) + Math.Abs(x.Y - sa.Y)).ThenBy(x => x.Y).ThenBy(x => x.X).First();
            break;
        }
        if (table == null) return null;
        var s = New(SceneKind.Chess, a, $"{Ko.WaGwa(a.Name)} {b.Name}의 체스", table.Room);
        s.Spot = sa;
        s.Spot2 = sb;
        s.Table = table.Cells.OrderBy(x => Math.Abs(x.X - sa.X) + Math.Abs(x.Y - sa.Y)).ThenBy(x => x.Y).ThenBy(x => x.X).First();
        s.Item = set.Id;
        s.Hours = 1f;
        // 상대의 판단: 체스를 좋아하나 · 사이 · 근무 · 피로
        float p = 0.45f + (b.Hobbies.Contains(Hobby.Chess) ? 0.3f : 0f) + 0.3f * b.AffinityTo(a) - (OnDuty(b) ? 0.35f : 0f) - 0.3f * b.Needs.Fatigue - 0.4f * b.Vitals.Injury;
        if (R.Chance(Math.Clamp(p, 0.1f, 0.95f))) { s.Other = b.Id; s.Invited.Add(b.Id); Trail(s, $"{b.Name}: 좋아, 곧 갈게"); }
        else { s.Declined.Add(b.Id); Trail(s, $"{b.Name}: 지금은 어려워"); }
        a.Interrupt(_w);
        if (s.Other >= 0) b.Interrupt(_w);
        return s;
    }

    /// <summary>커피 · 차: 타서 들고 가 놓아 준다 (재고를 쓴다).</summary>
    public bool Coffee(DailyCtx x)
    {
        if (x.One(c => (x.Has(c, Habit.CoffeeAddict) || x.Has(c, Habit.TeaLover)) && !Busy(c)) is not CrewMember c) return false;
        // 판을 두거나 영화를 보는 사람이 있으면 그쪽으로, 아니면 같은 방 사람들
        var to = Scenes.Where(s => s.Open && s.Kind is SceneKind.Chess or SceneKind.Movie && s.Stage == SceneStage.Run && !s.Here.Contains(c.Id))
            .SelectMany(s => s.Here).Distinct().Take(3).Select(CrewOf).OfType<CrewMember>().ToList();
        if (to.Count == 0) to = x.InRoom(c.Room!).Where(o => o != c).Take(4).ToList();
        if (to.Count == 0) return false;
        var s2 = OpenCoffee(c, to);
        return s2 != null && x.Done($"{Ko.IGa(c.Name)} {to.Count}명 몫의 {(s2.Tea ? "차" : "커피")}를 타러 간다", c);
    }

    /// <summary>야근 커피: 늦게까지 일하는 사람에게 한 잔.</summary>
    public bool Overtime(DailyCtx x)
    {
        if (x.One(c => c.Job?.Order != null && c.Needs.Rest < 0.5f) is not CrewMember c) return false;
        if (x.Other(c, o => o.AffinityTo(c) > 0f && !Busy(o)) is not CrewMember o) return false;
        var s = OpenCoffee(o, new List<CrewMember> { c });
        return s != null && x.Done($"{Ko.IGa(o.Name)} 늦게까지 일하는 {c.Name}에게 {(s.Tea ? "차" : "커피")}를 타 주러 간다", o, c);
    }

    public DailyScene? OpenCoffee(CrewMember maker, List<CrewMember> to)
    {
        if (!Able(maker) || to.Count == 0) return null;
        var machine = _w.Ship.Furniture.Where(f => f.Type == FurnitureType.CoffeeMachine && Usable(f.Room) && !f.Stowed).OrderBy(f => f.Id).FirstOrDefault()
            ?? _w.Ship.Furniture.Where(f => f.Type is FurnitureType.Stove or FurnitureType.MealDispenser && Usable(f.Room) && !f.Stowed).OrderBy(f => f.Id).FirstOrDefault();
        if (machine == null || SpotBy(machine) is not Cell at) return null;
        bool tea = Life.Has(maker, Habit.TeaLover) || !Life.Has(maker, Habit.CoffeeAddict) && maker.Id % 3 == 0;
        var s = New(SceneKind.Coffee, maker, $"{maker.Name}의 {(tea ? "차" : "커피")}", machine.Room);
        s.Tea = tea;
        s.Spot = at;
        s.Spot2 = machine.Cells[0];
        foreach (var o in to.OrderBy(o => o.Id)) s.Targets.Add(o.Id);
        maker.Interrupt(_w);
        return s;
    }

    /// <summary>영화의 밤: 제안 → 저마다 갈지 정한다 → 늦게 오는 사람 · 자리가 모자라면 바닥.</summary>
    public bool Movie(DailyCtx x)
    {
        if (x.Hour < 19) return false;
        if (x.One(c => x.Likes(c, Hobby.Movies) && !Busy(c)) is not CrewMember c) return false;
        var room = x.RoomOf(RoomType.Theater, RoomType.Lounge);
        if (room == null) return false;
        var s = OpenMovie(c, room);
        return s != null && x.Done($"{Ko.IGa(c.Name)} {room.Name}에서 영화의 밤을 열자고 했다 — {s.Invited.Count}명이 온단다" + (s.Declined.Count > 0 ? $" ({s.Declined.Count}명은 못 온다)" : ""), c);
    }

    public DailyScene? OpenMovie(CrewMember host, Room room)
    {
        if (!Able(host) || Scenes.Any(s => s.Open && s.Kind == SceneKind.Movie && s.RoomId == room.Id)) return null;
        var proj = room.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Projector);
        var screen = WallCell(room);
        if (screen == null) return null;
        var s = New(SceneKind.Movie, host, $"{room.Name} 영화의 밤", room);
        // 주 컴퓨터 오락 보관함이 돌면 거기서 고른다 (보관함이 꺼지면 영화도 멈춘다)
        if (_w.Automation.MainOnline && _w.Automation.Active(ComputerModule.MediaVault))
        {
            s.Film = ComputerV16.Films[(SimTime.Day(_w.Tick) * 3 + room.Id + host.Id) % ComputerV16.Films.Length];
            s.Title = $"{room.Name} 영화의 밤 「{s.Film}」";
        }
        s.Spot2 = screen.Value;
        s.Spot = proj?.Cells[0] ?? screen.Value;
        s.Hours = 1.8f;
        s.Things.Add(new SceneThing { Kind = ThingKind.Screen, At = screen.Value, Owner = host.Id, Since = _w.Tick });
        foreach (var o in _w.Crew)
        {
            if (o == host || o.Dead || !Able(o) || o.IsChild || !o.IsAwake || Busy(o)) continue;
            float p = 0.3f + (o.Hobbies.Contains(Hobby.Movies) ? 0.35f : 0f) + 0.3f * (1f - o.Needs.Social) + 0.25f * o.AffinityTo(host)
                      - (OnDuty(o) ? 0.5f : 0f) - 0.3f * o.Needs.Fatigue - (Bedtime(o) ? 0.2f : 0f) - 0.4f * o.Vitals.Injury;
            if (R.Chance(Math.Clamp(p, 0.05f, 0.95f))) s.Invited.Add(o.Id);
            else s.Declined.Add(o.Id);
            if (s.Invited.Count >= 8) break;
        }
        Trail(s, $"초대 {s.Invited.Count}명 · 못 옴 {s.Declined.Count}명");
        host.Interrupt(_w);
        return s;
    }

    /// <summary>엎지른 국: 바닥에 자국이 남고 · 둘레가 반응하고 · 누군가 도구를 가져와 닦는다.</summary>
    public bool Spill(DailyCtx x)
    {
        if (x.One(c => (x.Has(c, Habit.Hasty) || x.Has(c, Habit.Fidgety) || x.Has(c, Habit.Forgetful)) && c.Room?.Type is RoomType.Mess or RoomType.Galley) is not CrewMember c) return false;
        var s = OpenSpill(c);
        return s != null && x.Done($"{Ko.IGa(c.Name)} {RoomById(s.RoomId)!.Name} 바닥에 국을 엎었다" + (s.Other >= 0 ? $" — {Ko.IGa(CrewOf(s.Other)!.Name)} 걸레를 가지러 갔다" : ""), c);
    }

    public DailyScene? OpenSpill(CrewMember c)
    {
        if (c.Room is not Room room || c.Outside) return null;
        var at = _w.Ship.IsWalkable(c.Cell) ? c.Cell : FreeCell(room, c.Cell, null) ?? c.Cell;
        var soil = _w.Soil.RoomSoil(room);
        soil[(int)SoilKind.Bio] = MathF.Min(1f, soil[(int)SoilKind.Bio] + 0.15f);
        c.Soil.Clothes[(int)SoilKind.Bio] = MathF.Min(1f, c.Soil.Clothes[(int)SoilKind.Bio] + 0.3f);
        MarkLog.Add(room.Marks, _w.Tick, $"{c.Name}: 국을 엎었다");
        var s = New(SceneKind.Spill, c, $"{room.Name} 바닥의 국 자국", room);
        s.Stage = SceneStage.Run;
        s.Spot = at;
        s.Things.Add(new SceneThing { Kind = ThingKind.Stain, At = at, Owner = c.Id, Since = _w.Tick });
        // 배 본체 바닥: 국물은 젖음, 국 기름은 기름 — 같은 미끄럼 규칙(밟으면 실제로 미끄러진다)과 길찾기 비용(사람들이 돌아간다)을 탄다
        _w.Body.RaiseMark(at, CellMark.Wet, 0.75f, "엎지른 국");
        _w.Body.RaiseMark(at, CellMark.Oil, 0.4f, "국 기름");
        foreach (var d in Cell.Dirs4)
            if (_w.Ship.IsWalkable(at + d) && R.Chance(0.5f)) _w.Body.RaiseMark(at + d, CellMark.Wet, 0.35f, "튄 국물");
        Say(c, "앗, 뜨거…!");
        foreach (var o in _w.Crew.Where(o => o.Room == room && o.IsAwake && Able(o)).OrderBy(o => o.Id).ToList()) See(s, o);
        // 엎은 사람이 꼼꼼하면 직접 닦는다
        if (s.Other < 0 && c.Traits.Diligence >= 0.45f && !Life.Has(c, Habit.Hasty)) { s.Other = c.Id; Trail(s, $"{c.Name}: 내가 닦을게"); }
        return s;
    }

    /// <summary>자국을 봤다 — 반응 (깔끔이 짜증 · 농담꾼 웃음 · 투덜이) · 닦을 사람.</summary>
    private void See(DailyScene s, CrewMember o)
    {
        if (s.Seen.Contains(o.Id)) return;
        s.Seen.Add(o.Id);
        if (CrewOf(s.Host) is not CrewMember c || o == c) return;
        if (Life.Has(o, Habit.NeatFreak)) { o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + 0.03f); Say(o, "바닥 좀…"); Stats.Annoyed++; }
        else if (Life.Has(o, Habit.Joker)) { c.ChangeAffinity(o, 0.01f); o.ChangeAffinity(c, 0.01f); Say(o, "국이 바닥을 먹었네"); Stats.Laughs++; }
        else if (Life.Has(o, Habit.Grumbler) || Life.Has(o, Habit.ShortTempered)) { c.ChangeAffinity(o, -0.02f); o.ChangeAffinity(c, -0.02f); Diary(o, $"{Ko.IGa(c.Name)} 또 국을 엎었다. 바닥이 끈적하다"); Stats.Annoyed++; }
        if (s.Other < 0 && (Life.Has(o, Habit.NeatFreak) || Life.Has(o, Habit.Generous) || o.Traits.Diligence > 0.75f) && !o.IsChild)
        {
            s.Other = o.Id;
            Trail(s, $"{o.Name}: 걸레 가져올게");
            o.Interrupt(_w);
        }
    }

    /// <summary>한밤의 간식: 냉장고까지 가서 꺼내 먹는다 (식량 재고를 쓴다).</summary>
    public bool Snack(DailyCtx x)
    {
        if (x.One(c => (x.Has(c, Habit.Snacker) || x.Has(c, Habit.NightOwl)) && !Busy(c)) is not CrewMember c) return false;
        var s = OpenSnack(c);
        return s != null && x.Done($"{Ko.IGa(c.Name)} 출출해서 냉장고로 간다", c);
    }

    public DailyScene? OpenSnack(CrewMember c, bool? takePlate = null)
    {
        if (!Able(c)) return null;
        // 식탁에 이름표를 붙여 덜어 둔 남의 몫 (음식 · 늦게 오는 사람 접시) — 밤에 출출한 사람은 이름표를 보고도 먹기도 한다
        // (사이가 좋은 사람 몫 · 꼼꼼한 사람은 손대지 않는다)
        Plate? plate = null;
        if (takePlate != false)
            foreach (var p in _w.Cooking.Plates)
                if (!p.Eaten && !p.Spoiled && !p.Found && p.For != c.Id && Usable(p.Table.Room) && CrewOf(p.For) is CrewMember po && !po.Dead && (takePlate == true || c.AffinityTo(po) < 0.35f)
                    && (plate == null || p.Id < plate.Id)) plate = p;
        if (plate != null && SpotBy(plate.Table) is Cell pat && (takePlate == true || (Life.Has(c, Habit.Snacker) || c.Needs.Hunger > 0.6f) && c.Traits.Diligence < 0.75f && R.Chance(0.7f)))
        {
            var sp = New(SceneKind.Snack, c, $"{c.Name}의 간식", plate.Table.Room);
            sp.Spot = pat;
            sp.Spot2 = plate.Table.Cells[0];
            sp.Plate = plate.Id;
            sp.Victim = plate.For;
            c.Interrupt(_w);
            return sp;
        }
        var fridge = Fridge(ItemKind.Meal) ?? Fridge(ItemKind.Produce);
        if (fridge == null || SpotBy(fridge) is not Cell at) return null;
        var s = New(SceneKind.Snack, c, $"{c.Name}의 간식", fridge.Room);
        s.Spot = at;
        s.Spot2 = fridge.Cells[0];
        // 냉장고에 둔 남의 것일 수도 있다 (먹는 사람은 모른다)
        var victims = _w.Crew.Where(o => o != c && !o.Dead && !o.IsChild && (Life.Has(o, Habit.Grumbler) || Life.Has(o, Habit.NeatFreak) || Life.Has(o, Habit.Snacker))).OrderBy(o => o.Id).ToList();
        if (victims.Count > 0 && R.Chance(0.6f)) s.Victim = victims[R.Range(0, victims.Count)].Id;
        c.Interrupt(_w);
        return s;
    }

    private Furniture? Fridge(ItemKind k) =>
        _w.Ship.Furniture.Where(f => f.Storage != null && !f.Stowed && Usable(f.Room) && f.Storage.Count(k) > 0)
            .OrderBy(f => f.Type == FurnitureType.Fridge ? 0 : 1).ThenBy(f => f.Id).FirstOrDefault();

    /// <summary>몽유병: 자던 사람이 잠결에 복도로 걸어 나온다 — 당직이 찾아 침대로 데려간다.</summary>
    public bool Sleepwalk(DailyCtx x)
    {
        if (x.One(c => c.Pose == Pose.Sleeping && c.Needs.Stress > 0.5f && !Busy(c), asleepOk: true) is not CrewMember c) return false;
        var s = OpenSleepwalk(c);
        return s != null && x.Done($"{Ko.IGa(c.Name)} 자다가 일어나 걷기 시작했다", c);
    }

    public DailyScene? OpenSleepwalk(CrewMember c)
    {
        if (c.Dead || c.Down || c.Outside || c.Room == null || c.Bed == null && c.HomeBed == null) return null;
        var dist = _w.Paths.Flood(c.Cell, c.PathProfile);
        var hall = _w.Ship.LiveRooms.Where(r => r.Type == RoomType.Corridor && Usable(r)).SelectMany(r => r.Cells)
            .Where(x => _w.Ship.IsWalkable(x) && dist.Reachable(x)).OrderBy(x => Math.Abs(dist.Get(x) - 14)).ThenBy(x => x.Y).ThenBy(x => x.X).Cast<Cell?>().FirstOrDefault();
        if (hall is not Cell to) return null;
        var s = New(SceneKind.Sleepwalk, c, $"{c.Name}의 몽유병", _w.Ship.RoomAt(to) ?? c.Room);
        s.Stage = SceneStage.Run;
        s.Spot = to;
        var job = SleepwalkJob(s, c);
        c.EndJob(_w, ToilStatus.Interrupted);
        c.StartJob(job, _w, null);
        _jobScene[c.Id] = s.Id;
        return s;
    }

    /// <summary>소품 제작: 재료를 쓰고 · 작업대에서 진척을 쌓고 · 들고 가서 · 설치한다 (Props.Craft에서).</summary>
    public bool Craft(CrewMember maker, PropSpec spec)
    {
        if (!Able(maker) || maker.Dead || Busy(maker)) return false;
        if (spec.Material is ItemKind m && _w.Ship.CountStored(m) <= 0) return false;
        var room = _w.Props.Destination(maker, spec);
        if (room == null) return false;
        var bench = _w.Ship.Furniture.Where(f => f.Type == FurnitureType.Workbench && Usable(f.Room) && !f.Stowed).OrderBy(f => f.Id).FirstOrDefault()
            ?? _w.Ship.Furniture.Where(f => f.Type == FurnitureType.Table && Usable(f.Room) && !f.Stowed && f.Room.Type is RoomType.Lounge or RoomType.Mess).OrderBy(f => f.Id).FirstOrDefault();
        Cell spot, work;
        if (bench != null && SpotBy(bench) is Cell bs) { spot = bs; work = bench.Cells[0]; }
        else if (maker.Room != null && FreeCell(maker.Room, maker.Cell, null) is Cell mc) { spot = mc; work = mc; }
        else return false;
        var s = New(SceneKind.Craft, maker, $"{maker.Name}의 {spec.Name}", bench?.Room ?? maker.Room!);
        s.Prop = spec;
        s.PropRoom = room.Id;
        s.Spot = spot;
        s.Spot2 = work;
        s.Hours = spec.Maker != null ? 2.5f : 2f;
        maker.Interrupt(_w);
        return true;
    }

    // ═══════════════════════════════ 흐름 ═══════════════════════════════

    public void Update(float dt)
    {
        var w = _w;
        bool crisis = Crisis.Acting(w);
        // 1) 진행 · 경보 · 시간 초과
        for (int i = 0; i < Scenes.Count; i++)
        {
            var s = Scenes[i];
            if (!s.Open) continue;
            if (crisis && s.Stage != SceneStage.Paused)
            {
                if (s.Kind == SceneKind.Sleepwalk) { WakeUp(s, "경보에 놀라 깼다"); continue; }
                if (s.Kind != SceneKind.Spill) { Pause(s, "경보"); continue; }
            }
            Advance(s, dt, crisis);
        }
        // 2) 교대: 근무가 끝나는 사람은 넘기고, 시작하는 사람은 게시판을 본다
        Shifts();
        // 3) 쪽지 읽기 · 자국 보기 (5분마다 · 방 단위)
        if (w.Tick >= _nextRead) { _nextRead = w.Tick + SimTime.Minutes(5); Look(); }
        // 4) 한 시간마다: 이상한 소리 · 없어진 간식 · 몽유병 이야기 · 낙서 · 생일 카드 · 치우기
        if (_nextHour < 0) _nextHour = w.Tick + SimTime.TicksPerHour;
        if (w.Tick >= _nextHour) { _nextHour = w.Tick + SimTime.TicksPerHour; Hourly(); }
    }

    private void Advance(DailyScene s, float dt, bool crisis)
    {
        var w = _w;
        long age = w.Tick - (s.Stage == SceneStage.Paused ? s.PausedAt : s.Opened);
        switch (s.Kind)
        {
            case SceneKind.Chess:
            {
                var g = GameOf(s);
                if (CrewOf(s.Host) is not { Dead: false } || s.Stage == SceneStage.Gather && age > SimTime.Hours(3)) { Drop(s, "판이 벌어지지 않았다"); return; }
                if (s.Stage == SceneStage.Paused && age > SimTime.TicksPerDay * 2) { Drop(s, "끊긴 판을 끝내 잇지 못했다"); return; }
                if (g != null && s.Other >= 0 && s.Here.Contains(s.Host) && s.Here.Contains(s.Other))
                {
                    if (s.Stage == SceneStage.Paused) Resume(s, $"그대로 남은 판으로 돌아와 이어 둔다 ({g.Moves:0}수째부터)");
                    s.Stage = SceneStage.Run;
                    g.Moves += dt * 40f;
                    g.LastPlayed = w.Tick;
                    s.Progress = g.Moves / MathF.Max(1f, g.Target);
                    Together(s, dt, 0.1f);
                    if (g.Moves >= g.Target) FinishChess(s, g);
                }
                break;
            }
            case SceneKind.Movie:
            {
                if (s.Stage == SceneStage.Gather && age > SimTime.Hours(2) || s.Stage == SceneStage.Paused && age > SimTime.Hours(3)) { FinishMovie(s, false); return; }
                // 여는 사람이 와서 몇 명 모이면(또는 20분 기다리면) 튼다 — 그 뒤에 온 사람은 늦게 온 것
                bool ready = s.Stage != SceneStage.Gather || s.Here.Contains(s.Host) && (s.Here.Count >= Math.Min(3, 1 + s.Invited.Count) || w.Tick - s.Since > SimTime.Minutes(20))
                             || s.Here.Count > 0 && age > SimTime.Minutes(45);
                // 정전: 영사기가 꺼진다 — 앉은 채 기다리다 전기가 돌아오면 다시 튼다
                var mroom = RoomById(s.RoomId);
                bool power = mroom == null || mroom.Powered;
                if (!power && s.Stage == SceneStage.Run) { Pause(s, "정전"); break; }
                // 보관함에서 트는 영화: 컴퓨터가 멎거나(재부팅) 부하로 보관함을 끄면 화면이 멈춘다 — 돌아오면 그 자리부터
                bool vault = s.Film == null || w.Automation.MainOnline && w.Automation.Active(ComputerModule.MediaVault);
                if (!vault && s.Stage == SceneStage.Run) { Pause(s, "보관함 꺼짐"); break; }
                power &= vault;
                if (s.Here.Count > 0 && !crisis && ready && power)
                {
                    if (s.Stage == SceneStage.Gather) Trail(s, $"{s.Here.Count}명이 모여 틀었다");
                    if (s.Stage == SceneStage.Paused) Resume(s, s.PauseWhy == "정전" ? $"전기가 돌아와 다시 튼다 ({Pct(s.Progress)})" : s.PauseWhy == "보관함 꺼짐" ? $"보관함이 돌아와 다시 튼다 ({Pct(s.Progress)})" : $"남은 {s.Here.Count}명이 멈춘 데서부터 다시 튼다 ({Pct(s.Progress)})");
                    s.Stage = SceneStage.Run;
                    s.Progress += dt / s.Hours;
                    Together(s, dt, 0.08f);
                    // 영화 소리: 그 방과 옆방이 시끄러워진다 (잠의 질 · 소리를 줄여 달라면 줄인다)
                    if (mroom != null)
                    {
                        float vol = s.Holding ? 0.18f : 0.45f;
                        mroom.Noise = MathF.Max(mroom.Noise, vol);
                        foreach (var (o, door) in w.Ambience.Neighbors(mroom)) o.Noise = MathF.Max(o.Noise, vol * (door ? 0.55f : 0.4f));
                    }
                    if (s.Progress >= 1f) FinishMovie(s, true);
                }
                break;
            }
            case SceneKind.Coffee:
                if (age > SimTime.Hours(s.Stage == SceneStage.Paused ? 1 : 2)) { Close(s, s.Delivered > 0 ? SceneStage.Done : SceneStage.Dropped, s.Delivered > 0 ? $"{s.Delivered}잔까지 놓아 주고 말았다" : "결국 못 탔다"); }
                break;
            case SceneKind.Snack:
                if (age > SimTime.Hours(2)) Drop(s, "먹으러 가다 말았다");
                break;
            case SceneKind.Craft:
                if (s.Stage == SceneStage.Gather && age > SimTime.TicksPerDay || s.Stage == SceneStage.Paused && age > SimTime.TicksPerDay * 2) { Drop(s, s.Progress > 0f ? $"{Pct(s.Progress)}까지 만들다 말았다" : "손을 대지 못했다"); return; }
                if (!s.Holding && s.Here.Contains(s.Host) && s.Progress < 1f)
                {
                    // 캄캄한 작업대: 천장 불이 없고 이동식 작업등도 없으면 손을 놓고 기다린다 (등이 오면 등빛에 기대 · 조금 느리게)
                    if (RoomById(s.RoomId) is Room br && PortableSystem.Unlit(br) && br.PortableLit == 0)
                    {
                        if (s.Stage == SceneStage.Run) { Pause(s, "어두움"); Stats.Dark++; }
                        break;
                    }
                    if (s.Stage == SceneStage.Paused) Resume(s, s.PauseWhy == "어두움" ? $"불이 들어와 다시 손을 댄다 ({Pct(s.Progress)})" : $"만들다 둔 것을 이어 만든다 ({Pct(s.Progress)})");
                    s.Stage = SceneStage.Run;
                    var mk = CrewOf(s.Host);
                    float hand = mk == null ? 1f : w.Portable.LampWorkMul(mk) * (1f - 0.5f * mk.Vitals.Injury); // 등빛 · 다친 손은 느리다
                    s.Progress = MathF.Min(1f, s.Progress + dt / s.Hours * hand);
                    var wip = s.Things.FirstOrDefault(t => t.Kind == ThingKind.Work);
                    if (wip == null) s.Things.Add(new SceneThing { Kind = ThingKind.Work, At = s.Spot2, Owner = s.Host, Since = w.Tick });
                }
                break;
            case SceneKind.Spill:
            {
                var stain = s.Things.FirstOrDefault(t => t.Kind == ThingKind.Stain);
                if (stain != null) stain.Amount = 1f - s.Progress;
                // 자국을 밟고 넘어진 사람 (배 본체의 바닥 규칙이 굴렸다) — 엎은 사람 탓을 하고 · 꼼꼼한 사람은 닦겠다고 나선다
                foreach (var c in w.Crew)
                {
                    if (c.Room?.Id != s.RoomId || !w.Body.Fallen(c) || s.Slipped.Contains(c.Id) || (c.Position - s.Spot.Center).LengthSquared() > 4f) continue;
                    s.Slipped.Add(c.Id);
                    Stats.Slips++;
                    c.Soil.Clothes[(int)SoilKind.Bio] = MathF.Min(1f, c.Soil.Clothes[(int)SoilKind.Bio] + 0.1f);
                    Trail(s, $"{c.Name}: 국 자국을 밟고 미끄러졌다");
                    if (CrewOf(s.Host) is CrewMember sp && sp != c) { c.ChangeAffinity(sp, -0.02f); if (!s.Seen.Contains(c.Id)) Diary(c, $"{Ko.IGa(sp.Name)} 엎은 국에 미끄러졌다"); }
                    if (s.Other < 0 && c.Traits.Diligence > 0.4f && !c.IsChild) { s.Other = c.Id; Trail(s, $"{c.Name}: 안 되겠다, 닦아야지"); }
                    if (!s.Seen.Contains(c.Id)) s.Seen.Add(c.Id);
                }
                if (w.Tick - s.Opened > SimTime.TicksPerDay * 2) Drop(s, "말라붙은 채 남았다");
                else if (s.Other >= 0 && CrewOf(s.Other) is CrewMember cl && (!Able(cl) || w.Tick - s.Opened > SimTime.Hours(4) && !s.Here.Contains(cl.Id) && !_jobScene.TryGetValue(cl.Id, out _)))
                { Trail(s, $"{cl.Name}: 닦으러 오지 못했다"); s.Other = -1; }
                break;
            }
            case SceneKind.Sleepwalk:
            {
                var c = CrewOf(s.Host);
                if (c == null || c.Dead || c.Down) { Drop(s, "멈췄다"); return; }
                if (c.Job?.Activity != SceneActivity.Instance) { if (s.Open) WakeUp(s, "잠에서 깼다"); return; }
                if (c.Cell == s.Spot && s.Since < 0) s.Since = w.Tick;
                // 주 컴퓨터: 새벽 복도의 움직임 — 목적지 없이 제자리에 선 사람 → 잠결 걸음으로 보고 당직을 부른다
                if (s.Other < 0 && s.ComputerAct < 0 && s.Since >= 0 && RoomById(s.RoomId) is Room hall && Sees(hall)
                    && w.Tick - s.Since >= SimTime.Minutes(w.Automation.Active(ComputerModule.Access) ? 2 : 5))
                    PageWatch(s, c, hall);
                // 깨어 있는 사람이 가까이 지나가면 알아챈다 (야간 당직 먼저)
                if (s.Other < 0 && c.Cell == s.Spot)
                {
                    CrewMember? found = null;
                    foreach (var o in w.Crew)
                    {
                        if (o == c || !Able(o) || !o.IsAwake || o.IsChild || Busy(o)) continue;
                        if ((o.Position - c.Position).LengthSquared() > 49f && o.Room != c.Room) continue;
                        if (found == null || w.Society.OnNightWatch(o) && !w.Society.OnNightWatch(found)) found = o;
                    }
                    if (found != null)
                    {
                        s.Other = found.Id;
                        s.Seen.Add(found.Id);
                        Trail(s, $"{found.Name}: 복도에서 멍하니 선 {Ko.EulReul(c.Name)} 발견");
                        Log($"{Ko.IGa(found.Name)} 복도에서 잠결에 서 있는 {Ko.EulReul(c.Name)} 발견했다", found.Id);
                        found.Interrupt(w);
                    }
                }
                break;
            }
        }
    }

    /// <summary>함께하는 동안: 함께한 만큼 · 조금씩 마음이 풀린다.</summary>
    private void Together(DailyScene s, float dt, float social)
    {
        foreach (var id in s.Here)
        {
            if (CrewOf(id) is not CrewMember c) continue;
            s.Share[id] = (s.Share.TryGetValue(id, out var v) ? v : 0f) + dt / s.Hours;
            c.Needs.Social = MathF.Min(1f, c.Needs.Social + social * dt);
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f * dt);
        }
    }

    private HobbyGame? GameOf(DailyScene s) => s.Game < 0 ? null : _w.Belongings.Games.FirstOrDefault(g => g.Id == s.Game);

    // ───────────────────────────── 중단 · 재개 · 끝 ─────────────────────────────

    /// <summary>멈춘다: 진척 · 물건은 그 자리에 남는다. 경보면 자리에 있던 사람을 일으킨다.</summary>
    public void Pause(DailyScene s, string why)
    {
        if (!s.Open || s.Stage == SceneStage.Paused) return;
        s.Stage = SceneStage.Paused;
        s.PauseWhy = why;
        s.PausedAt = _w.Tick;
        s.Pauses++;
        Stats.Paused++;
        if (why == "정전") Stats.PowerCuts++;
        Trail(s, $"멈춤 — {why} (진척 {Pct(s.Progress)})");
        if (s.Kind == SceneKind.Chess && GameOf(s) is HobbyGame g && g.B >= 0) _w.Belongings.BreakGame(g);
        if (s.Progress > 0.02f && s.Kind is SceneKind.Chess or SceneKind.Movie or SceneKind.Craft)
            Log($"{s.Title} — {why}에 멈췄다 ({Pct(s.Progress)}에서 그대로 남는다)", s.Host);
        if (why == "경보") Kick(s);
    }

    private void Resume(DailyScene s, string text)
    {
        Stats.Resumed++;
        Trail(s, $"다시 — {text}");
        Log($"{s.Title} — {text}", s.Host);
        s.PauseWhy = null;
    }

    /// <summary>장면에 와 있거나 오던 사람을 일으킨다 (경보).</summary>
    private void Kick(DailyScene s)
    {
        foreach (var c in _w.Crew)
        {
            if (!_jobScene.TryGetValue(c.Id, out var sid) || sid != s.Id || c.Job?.Activity != SceneActivity.Instance) continue;
            c.EndJob(_w, ToilStatus.Interrupted);
            c.Interrupt(_w);
        }
        s.Here.Clear();
    }

    private void Drop(DailyScene s, string why) => Close(s, SceneStage.Dropped, why);

    private void Close(DailyScene s, SceneStage stage, string why)
    {
        if (!s.Open) return;
        s.Stage = stage;
        s.Closed = _w.Tick;
        if (stage == SceneStage.Done) Stats.Done++; else Stats.Dropped++;
        Trail(s, why);
        if (s.Kind == SceneKind.Chess)
        {
            var g = GameOf(s);
            if (g != null && !g.Done) g.Done = true;
            if (_w.Belongings.Get(s.Item) is Belonging set && set.Holder < 0 && set.At != null && CrewOf(s.Host) is CrewMember h) _w.Belongings.Stow(h, set);
        }
        if (stage == SceneStage.Dropped && s.Kind is not SceneKind.Spill and not SceneKind.Coffee) Log($"{s.Title} — {why}", s.Host);
        s.Things.RemoveAll(t => t.Kind is ThingKind.Board or ThingKind.Screen or ThingKind.Work or ThingKind.Snack || t.Kind == ThingKind.Stain && stage == SceneStage.Done);
        s.Here.Clear();
    }

    /// <summary>장면 일이 끝났다 (끝까지 했거나 · 끊겼거나).</summary>
    internal void Left(DailyScene s, CrewMember c, ToilStatus status)
    {
        if (_jobScene.TryGetValue(c.Id, out var sid) && sid == s.Id) _jobScene.Remove(c.Id);
        bool was = s.Here.Remove(c.Id);
        if (!s.Open) return;
        if (s.Kind == SceneKind.Sleepwalk && c.Id == s.Host && status != ToilStatus.Succeeded) { WakeUp(s, "잠에서 깼다"); return; }
        if (status == ToilStatus.Succeeded && s.Kind != SceneKind.Craft) return;
        string why = Crisis.Acting(_w) ? "경보" : c.Down || c.Dead ? "부상" : c.Needs.Fatigue > 0.8f || c.Pose == Pose.Sleeping ? "졸음" : c.Needs.Hunger > 0.8f ? "배고픔" : status == ToilStatus.Succeeded ? "잠시 손을 놓았다" : "다른 일";
        if (was) Trail(s, $"{c.Name} 자리를 떴다 — {why}");
        switch (s.Kind)
        {
            case SceneKind.Chess when was || c.Id == s.Host && s.Stage != SceneStage.Gather:
            case SceneKind.Movie when s.Here.Count == 0 && s.Stage == SceneStage.Run:
            case SceneKind.Craft when c.Id == s.Host && !s.Holding:
                Pause(s, why);
                break;
            case SceneKind.Coffee when c.Id == s.Host:
                Pause(s, why);
                break;
            case SceneKind.Spill when c.Id == s.Other:
                s.Holding = false;
                break;
        }
    }

    // ───────────────────────────── 결과 (끝까지 한 만큼만) ─────────────────────────────

    private void FinishChess(DailyScene s, HobbyGame g)
    {
        s.Progress = 1f;
        _w.Belongings.EndGame(g); // 이긴 사람 · 일기 · 관계 · 체스판에 남는 자국
        foreach (var id in new[] { s.Host, s.Other })
            if (CrewOf(id) is CrewMember c) c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.05f);
        if (s.Pauses > 0 && CrewOf(s.Host) is CrewMember a && CrewOf(s.Other) is CrewMember b)
        {
            a.ChangeAffinity(b, 0.02f);
            b.ChangeAffinity(a, 0.02f);
            Diary(a, $"{Ko.WaGwa(b.Name)} 두던 판이 경보로 끊겼는데, 돌아와 보니 그대로였다. 끝까지 뒀다");
        }
        Close(s, SceneStage.Done, $"판이 끝났다 ({g.Moves:0}수 · 끊김 {s.Pauses}번)");
    }

    private void FinishMovie(DailyScene s, bool end)
    {
        var host = CrewOf(s.Host);
        var room = RoomById(s.RoomId);
        var finishers = s.Here.Where(id => s.Share.TryGetValue(id, out var v) && v >= 0.5f).OrderBy(id => id).Select(CrewOf).OfType<CrewMember>().ToList();
        foreach (var id in s.Joined)
        {
            if (CrewOf(id) is not CrewMember c || c.Dead) continue;
            float share = Math.Clamp(s.Share.TryGetValue(id, out var v) ? v : 0f, 0f, 1f);
            c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.12f * share);
            c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.03f * share);
            if (end && finishers.Contains(c))
            {
                if (s.Pauses > 0) Diary(c, $"영화가 경보로 끊겼다가, 남은 사람끼리 마저 봤다" + (s.Floor.Contains(id) ? " (바닥에 앉아서)" : ""));
                else if (s.Floor.Contains(id)) Diary(c, "자리가 모자라 바닥에 앉아 영화를 봤다. 그래도 좋았다");
            }
            else if (share > 0.05f) Diary(c, $"영화 결말을 못 봤다 ({Pct(share)}쯤 봤다)");
        }
        for (int i = 0; i < finishers.Count && i < 6; i++)
            for (int j = i + 1; j < finishers.Count && j < 6; j++) { finishers[i].ChangeAffinity(finishers[j], 0.02f); finishers[j].ChangeAffinity(finishers[i], 0.02f); }
        if (!end)
        {
            Close(s, s.Progress > 0.3f ? SceneStage.Done : SceneStage.Dropped, s.Progress > 0.02f ? $"{Pct(s.Progress)}에서 끝내 다시 틀지 못했다" : "아무도 오지 않았다");
            return;
        }
        s.Finished = finishers.Count;
        if (s.Film != null && finishers.Count > 0) _w.Automation.Apps.Watched[s.Film] = _w.Automation.Apps.Watched.GetValueOrDefault(s.Film) + finishers.Count; // 보관함이 본 사람 수를 센다
        string text = $"{room?.Name ?? "?"} 영화의 밤 — {finishers.Count}명이 끝까지 봤다" + (s.Pauses > 0 ? $" (경보로 {s.Pauses}번 멈췄다가 남은 사람이 이어 봤다)" : "") + (s.Floor.Count > 0 ? $" · {s.Floor.Count}명은 바닥에서" : "");
        if (host != null && !host.Dead) Diary(host, $"영화의 밤 — {finishers.Count}명이 끝까지 봤다");
        if (s.Pauses > 0 && finishers.Count > 0) _w.History.Add(_w, HistoryKind.Memory, text, room, finishers.ToArray(), log: true);
        else Log(text, s.Host);
        Close(s, SceneStage.Done, text);
    }

    // endJob=false: 몽유병 일의 단계 안에서 부를 때 — 그 단계가 실패를 돌려 일이 스스로 끝난다 (안에서 끝내면 목록 밖을 읽는다)
    private void WakeUp(DailyScene s, string why, bool endJob = true)
    {
        if (!s.Open) return;
        Close(s, SceneStage.Done, why + " — 혼자 침대로 돌아갔다");
        if (CrewOf(s.Host) is not CrewMember c) return;
        if (endJob && _jobScene.TryGetValue(c.Id, out var sid) && sid == s.Id && c.Job?.Activity == SceneActivity.Instance)
        {
            _jobScene.Remove(c.Id);
            c.EndJob(_w, ToilStatus.Interrupted);
            c.Interrupt(_w);
        }
        if (!c.Dead) Diary(c, $"깨어 보니 {RoomById(s.RoomId)?.Name ?? "복도"}였다. 어떻게 나왔는지 모르겠다");
    }

    // ═══════════════════════════════ 사람마다 할 일 (SceneActivity) ═══════════════════════════════

    public enum Role { None, Host, Join, Brew, Clean, Eat, Escort, Craft, Check, Board }

    internal (float score, string why, DailyScene? s, Role role, Concern? k) Best(CrewMember c, DistanceField dist)
    {
        var w = _w;
        (float, string, DailyScene?, Role, Concern?) best = (0f, "—", null, Role.None, null);
        if (!Able(c) || c.Dead) return best;
        bool crisis = Crisis.Acting(w);
        float lf = w.Society.LeisureFactor;
        bool duty = OnDuty(c), bed = Bedtime(c);
        void Offer(float sc, string why, DailyScene? s, Role r, Concern? k = null) { if (sc > best.Item1) best = (sc, why, s, r, k); }

        foreach (var s in Scenes)
        {
            if (!s.Open) continue;
            if (s.Kind == SceneKind.Sleepwalk)
            {
                if (s.Other == c.Id && !s.Holding && c.IsAwake) Offer(1.3f, $"잠결에 걷는 {CrewOf(s.Host)?.Name} — 침대로", s, Role.Escort);
                if (s.Host == c.Id && c.Job?.Activity == SceneActivity.Instance) Offer(0.95f, "잠결", s, Role.Host);
                continue;
            }
            if (crisis) continue;
            float leisure = (0.55f + (s.Stage == SceneStage.Paused ? 0.12f : 0f)) * lf - (duty ? 0.25f : 0f) - (bed ? 0.25f : 0f) - (c.Needs.Hunger > 0.75f ? 0.25f : 0f) - 0.4f * c.Vitals.Injury; // 다친 몸은 덜 끌린다
            switch (s.Kind)
            {
                case SceneKind.Chess:
                    if (s.Host == c.Id) Offer(leisure, s.Stage == SceneStage.Paused ? "끊긴 판으로 돌아간다" : s.Game < 0 ? "체스판을 가져와 편다" : "판 앞에서 상대를 기다린다", s, Role.Host);
                    else if (s.Other == c.Id) Offer(leisure + (s.Stage == SceneStage.Gather && _w.Tick - s.Opened < SimTime.Hours(1) ? 0.25f : 0f), s.Stage == SceneStage.Paused ? $"{CrewOf(s.Host)?.Name}와 두던 판으로" : $"{CrewOf(s.Host)?.Name}의 체스 청 — 곧 간다고 했다", s, Role.Join); // 받아들였으면 약속이다 (한 시간 안엔 더 끌린다)
                    else if (s.Other < 0 && s.Game >= 0 && c.Room?.Id == s.RoomId && !c.IsChild && (c.Hobbies.Contains(Hobby.Chess) || c.Traits.Calm > 0.6f) && !Busy(c))
                        Offer(leisure - 0.05f, $"{CrewOf(s.Host)?.Name}가 펴 둔 판 — 상대가 없다", s, Role.Join);
                    break;
                case SceneKind.Movie:
                    if (s.Host == c.Id || s.Invited.Contains(c.Id) || s.Joined.Contains(c.Id))
                    {
                        if (s.Here.Contains(c.Id)) break;
                        // 끊겼다 다시 갈지: 피곤하거나 늦었으면 덜
                        float sc = leisure - (s.Stage == SceneStage.Paused ? 0.25f * c.Needs.Fatigue : 0f);
                        Offer(sc, s.Stage == SceneStage.Paused ? "멈춘 영화를 마저 보러" : s.Stage == SceneStage.Run ? "영화가 벌써 시작했다 — 늦었다" : "영화의 밤", s, s.Host == c.Id ? Role.Host : Role.Join);
                    }
                    break;
                case SceneKind.Coffee:
                    if (s.Host == c.Id) Offer(0.62f - (bed ? 0.2f : 0f), $"{(s.Tea ? "차" : "커피")}를 타 준다", s, Role.Brew);
                    break;
                case SceneKind.Snack:
                    if (s.Host == c.Id) Offer(0.6f, "출출하다 — 냉장고로", s, Role.Eat);
                    break;
                case SceneKind.Spill:
                    if (s.Other == c.Id) Offer(0.5f - (bed ? 0.2f : 0f), s.Host == c.Id ? "엎은 국을 닦는다" : $"{CrewOf(s.Host)?.Name}가 엎은 국 — 걸레를 가져와 닦는다", s, Role.Clean);
                    break;
                case SceneKind.Craft:
                    if (s.Host == c.Id) Offer((0.3f + (s.Progress > 0f ? 0.12f : 0f) + (s.Holding ? 0.3f : 0f)) * lf + (AfterWork(c) ? 0.15f : 0f) - (duty ? 0.25f : 0f) - (bed ? 0.25f : 0f),
                        s.Holding ? $"다 만든 {s.Prop!.Name} — 가져다 둔다" : s.Progress > 0f ? $"만들다 둔 {s.Prop!.Name} ({Pct(s.Progress)})" : $"{s.Prop!.Name}을(를) 만든다", s, Role.Craft);
                    break;
            }
        }
        if (crisis) return best;
        // 인수인계로 알게 된 이상 — 확인하러 간다
        foreach (var k in Concerns)
        {
            if (k.Who != c.Id || k.Checked || k.Kind == ConcernKind.Unfinished || k.Machine == null && k.Place == null) continue; // 미완료 작업은 알고만 있는다 (작업 목록이 맡긴다)
            bool heard = k.How != "직접 들음";
            if (!heard && c.Traits.Diligence < 0.7f) continue; // 직접 들은 건 넘기거나, 꼼꼼한 사람만 바로 본다
            if (!duty && bed) continue;
            Offer(0.6f + (duty ? 0.15f : 0f), $"{k.How}: {k.Text} — 확인하러", null, Role.Check, k);
        }
        // 근무를 시작하면 당직 게시판부터 (메모가 있나)
        int i = c.Id;
        if (duty && i < _dutyStart.Length && _dutyStart[i] > 0 && _boardSeen[i] < _dutyStart[i] && _w.Tick - _dutyStart[i] < SimTime.Hours(2) && !c.IsChild && DutyBoard() != null)
            Offer(0.58f + 0.2f * c.Traits.Diligence, "근무 시작 — 당직 게시판을 본다", null, Role.Board);
        // 주 컴퓨터 알림: 게시판에 내 앞으로 온 메모가 있다
        foreach (var n in Notes)
            if (n.Pinged && !n.Gone && n.For == c.Id && !n.Readers.Contains(c.Id)) { Offer(0.82f, "컴퓨터 알림 — 게시판에 인수인계 메모", null, Role.Board); break; }
        return best;
    }

    private bool AfterWork(CrewMember c)
    {
        float end = c.Schedule.WorkStart + c.Schedule.WorkLength;
        return SimTime.InWindow(SimTime.HourOfDay(_w.Tick), end, SimTime.HoursFromTo(end, c.Schedule.SleepStart));
    }

    internal Job? Plan(CrewMember c, DistanceField dist)
    {
        var (_, why, s, role, k) = Best(c, dist);
        Job? job = role switch
        {
            Role.Host when s!.Kind == SceneKind.Chess => ChessJob(s, c, dist, true),
            Role.Join when s!.Kind == SceneKind.Chess => ChessJob(s, c, dist, false),
            Role.Host or Role.Join when s!.Kind == SceneKind.Movie => MovieJob(s, c, dist),
            Role.Brew => CoffeeJob(s!, c, dist),
            Role.Eat => SnackJob(s!, c, dist),
            Role.Clean => CleanJob(s!, c, dist),
            Role.Escort => EscortJob(s!, c, dist),
            Role.Craft => CraftJob(s!, c, dist),
            Role.Check => CheckJob(k!, c, dist),
            Role.Board => BoardJob(c, dist),
            _ => null,
        };
        if (job != null && s != null) _jobScene[c.Id] = s.Id;
        return job;
    }

    private Job MakeJob(DailyScene? s, string label, List<Toil> toils, float margin = 0.25f, string? log = null) =>
        new(SceneActivity.Instance, label, toils)
        {
            InterruptMargin = margin,
            LogText = log,
            LogKind = LogKind.Life,
            AlwaysLog = log != null,
            OnFinished = s == null ? null : (cm, world, st) => world.Scenes.Left(s, cm, st),
        };

    /// <summary>자리에 왔다.</summary>
    private void Arrive(DailyScene s, CrewMember c)
    {
        if (!s.Here.Contains(c.Id)) s.Here.Add(c.Id);
        if (!s.Joined.Contains(c.Id)) s.Joined.Add(c.Id);
        if (c.Id == s.Host && s.Since < 0) s.Since = _w.Tick;
    }

    // ── 체스 ──
    private Job? ChessJob(DailyScene s, CrewMember c, DistanceField dist, bool host)
    {
        var bs = _w.Belongings;
        var spot = host ? s.Spot : s.Spot2;
        if (!dist.Reachable(spot)) return null;
        var toils = Plans.DropOff(c, _w, dist);
        var face = s.Table.Center;
        if (host && s.Game < 0)
        {
            var set = bs.Get(s.Item);
            if (set == null || !set.Usable) { Drop(s, "체스판이 없었다"); return null; }
            var at = bs.CellOf(set);
            if (set.Holder != c.Id)
            {
                if (!dist.Reachable(at)) return null;
                toils.Add(new GotoToil(at));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (set.Holder >= 0 && set.Holder != cm.Id || !set.Usable) return false;
                    world.Belongings.PickUp(cm, set);
                    return true;
                }));
            }
            toils.Add(new GotoToil(spot));
            toils.Add(new DoToil((cm, world) =>
            {
                if (!s.Open || set.Holder != cm.Id) return false;
                world.Belongings.PutDown(cm, set, false);
                set.At = s.Table;
                set.Open = true;
                var g = world.Belongings.OpenGame(cm, Hobby.Chess, set, s.Table);
                g.Scene = s.Id;
                s.Game = g.Id;
                if (s.Other >= 0 && s.Here.Contains(s.Other)) g.B = s.Other;
                s.Things.Add(new SceneThing { Kind = ThingKind.Board, At = s.Table, Owner = cm.Id, Since = world.Tick });
                Arrive(s, cm);
                Trail(s, $"{cm.Name}: {Ko.EulReul(set.Name)} 펴 놓고 상대를 기다린다");
                return true;
            }));
        }
        else
        {
            toils.Add(new GotoToil(spot));
            toils.Add(new DoToil((cm, world) =>
            {
                if (!s.Open) return false;
                if (!host && s.Other < 0) s.Other = cm.Id;
                if (!host && s.Other != cm.Id) return false;
                if (GameOf(s) is HobbyGame g && g.B < 0 && !host) { g.B = cm.Id; world.Log.Add(world.Tick, LogKind.Life, $"{CrewOf(s.Host)?.Name}의 판에 앉았다", cm.Id); }
                Arrive(s, cm);
                return true;
            }));
        }
        toils.Add(new WaitToil(SimTime.Hours(3), Pose.Sitting, face)
        {
            DoneWhen = (cm, world) => !s.Open || host && s.Stage == SceneStage.Gather && world.Tick - s.Opened > SimTime.Minutes(50) && (s.Other < 0 || !s.Here.Contains(s.Other)),
        });
        if (host)
            toils.Add(new DoToil((cm, world) =>
            {
                if (s.Open && s.Stage == SceneStage.Gather) Drop(s, "상대가 오지 않아 판을 접었다");
                return true;
            }));
        string other = CrewOf(host ? s.Other : s.Host)?.Name ?? "상대";
        return MakeJob(s, "체스", toils, 0.25f, s.Stage == SceneStage.Paused ? $"{Ko.WaGwa(other)} 두던 판으로 돌아간다" : host ? null : $"{other}의 체스 판으로 간다");
    }

    // ── 영화 ──
    private Job? MovieJob(DailyScene s, CrewMember c, DistanceField dist)
    {
        var room = RoomById(s.RoomId);
        if (room == null) return null;
        var toils = Plans.DropOff(c, _w, dist);
        // 자리: 빈 의자 → 없으면 바닥
        var seat = room.Furniture.Where(f => f.Type == FurnitureType.Seat && f.ReservedBy == null && f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0]) && !_w.IsSpotTaken(f.UseSpots[0], c))
            .OrderBy(f => (f.Center - s.Spot2.Center).LengthSquared()).ThenBy(f => f.Id).FirstOrDefault();
        Cell at;
        bool floor = seat == null;
        if (seat != null) at = seat.UseSpots[0];
        else if (FreeCell(room, s.Spot2, c, 1, x => dist.Reachable(x) && x != s.Spot2) is Cell fc) at = fc;
        else return null;
        toils.Add(new GotoToil(at));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!s.Open) return false;
            bool late = s.Stage != SceneStage.Gather && s.Progress > 0.05f && !s.Joined.Contains(cm.Id);
            Arrive(s, cm);
            if (floor && !s.Floor.Contains(cm.Id)) { s.Floor.Add(cm.Id); Stats.FloorSeats++; Trail(s, $"{cm.Name}: 자리가 모자라 바닥에 앉았다"); }
            if (late) { Stats.Late++; Trail(s, $"{cm.Name}: 늦게 왔다 ({Pct(s.Progress)} 지나서)"); }
            return true;
        }));
        toils.Add(new WaitToil(SimTime.Hours(3), Pose.Sitting, s.Spot2.Center) { DoneWhen = (cm, world) => !s.Open });
        var job = MakeJob(s, "영화의 밤", toils, 0.25f, s.Stage == SceneStage.Paused ? "멈춘 영화를 마저 보러 간다" : null);
        if (seat != null) job.Reserve(seat, c);
        return job;
    }

    // ── 커피 ──
    private Job? CoffeeJob(DailyScene s, CrewMember c, DistanceField dist)
    {
        if (!dist.Reachable(s.Spot)) return null;
        var toils = Plans.DropOff(c, _w, dist);
        if (!s.Holding)
        {
            toils.Add(new GotoToil(s.Spot));
            toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Working, s.Spot2.Center));
            toils.Add(new DoToil((cm, world) =>
            {
                if (!s.Open) return false;
                // 정전이면 물을 못 끓인다 (재고는 그대로)
                if (world.Ship.RoomAt(s.Spot2) is Room kr && !kr.Powered)
                {
                    Stats.PowerCuts++;
                    Say(cm, "전기가 없네… 커피는 나중에");
                    Close(s, SceneStage.Dropped, "정전 — 물을 못 끓였다");
                    return false;
                }
                int pots = Math.Max(1, (s.Targets.Count - s.Delivered + 2) / 3);
                bool ok = true;
                for (int i = 0; i < pots && ok; i++) ok = ItemsV15.Use(world, s.Tea ? ItemKind.TeaLeaf : ItemKind.Coffee);
                StockAdvice(s.Tea ? ItemKind.TeaLeaf : ItemKind.Coffee, world.Ship.RoomAt(s.Spot2)); // 주 컴퓨터가 재고를 본다
                if (!ok)
                {
                    Stats.Empty++;
                    cm.Needs.Stress = MathF.Min(1f, cm.Needs.Stress + 0.04f);
                    Log($"{Ko.IGa(cm.Name)} {(s.Tea ? "차" : "커피")}를 타려다 통이 빈 걸 알았다 — 다음 기항지 목록 맨 위에 적었다", cm.Id);
                    Close(s, SceneStage.Dropped, "통이 비었다");
                    return false;
                }
                s.Holding = true;
                s.Stage = SceneStage.Run;
                Trail(s, $"{cm.Name}: {s.Targets.Count - s.Delivered}잔을 탔다");
                return true;
            }));
        }
        foreach (var id in s.Targets.ToList())
        {
            int tid = id;
            toils.Add(new GotoToilLate(cm => s.Open && s.Holding && !Got(s, tid) && CrewOf(tid) is CrewMember t && Able(t) && !t.Dead ? NextTo(t, cm) : null));
            toils.Add(new DoToil((cm, world) => { if (s.Open && s.Holding && !Got(s, tid)) PlaceCup(s, cm, tid); return true; }));
        }
        toils.Add(new DoToil((cm, world) =>
        {
            if (!s.Open) return true;
            if (s.Delivered > 0) Log($"{Ko.IGa(cm.Name)} {s.Delivered}명에게 {(s.Tea ? "차" : "커피")}를 타서 놓아 줬다", cm.Id);
            Close(s, s.Delivered > 0 ? SceneStage.Done : SceneStage.Dropped, $"{s.Delivered}/{s.Targets.Count}잔");
            return true;
        }));
        return MakeJob(s, s.Tea ? "차 타기" : "커피 타기", toils, 0.3f);
    }

    private static bool Got(DailyScene s, int id) => s.Things.Any(t => t.Kind == ThingKind.Cup && t.Owner == id && t.Since >= s.Opened);

    private Cell? NextTo(CrewMember t, CrewMember me)
    {
        foreach (var d in Cell.Dirs8)
        {
            var x = t.Cell + d;
            if (_w.Ship.IsWalkable(x) && !_w.IsSpotTaken(x, me)) return x;
        }
        return t.Cell;
    }

    private void PlaceCup(DailyScene s, CrewMember maker, int tid)
    {
        var t = CrewOf(tid);
        if (t == null) return;
        bool near = (t.Position - maker.Position).LengthSquared() < 6.25f && t.IsAwake && Able(t);
        // 잔은 받는 사람 곁에 놓인다 (판 앞이면 탁자 위) — 사람이 없으면 들고 간 자리에 두고 온다
        var at = !near ? maker.Cell : Scenes.FirstOrDefault(x => x.Open && x.Kind == SceneKind.Chess && x.Here.Contains(tid)) is DailyScene chess ? chess.Table : t.Cell;
        s.Things.Add(new SceneThing { Kind = ThingKind.Cup, At = at, Owner = tid, Since = _w.Tick, Until = _w.Tick + SimTime.Minutes(near ? 45 : 120), Tea = s.Tea });
        Stats.Cups++;
        if (!near) { Trail(s, $"{t.Name} 자리에 놓았다 — 사람이 없어 식어 간다"); return; }
        s.Delivered++;
        t.Needs.Rest = MathF.Min(1f, t.Needs.Rest + 0.04f);
        t.Needs.Social = MathF.Min(1f, t.Needs.Social + 0.06f);
        t.Needs.Stress = MathF.Max(0f, t.Needs.Stress - 0.02f);
        t.ChangeAffinity(maker, 0.03f);
        maker.ChangeAffinity(t, 0.01f);
        string what = s.Tea ? "차" : "커피";
        string when = t.Job?.Order != null ? "늦게까지 일할 때" : Scenes.Any(x => x.Open && x.Kind == SceneKind.Chess && x.Here.Contains(tid)) ? "체스를 둘 때" : "쉴 때";
        _w.Relations.Remember(t, maker, RelationReason.Comforted, $"{when} {Ko.EulReul(what)} 타서 놓아 줬다");
        Say(maker, $"{what} 여기 둘게");
        Say(t, "고마워");
        Trail(s, $"{t.Name} 곁에 {Ko.EulReul(what)} 놓았다");
    }

    // ── 간식 ──
    private Job? SnackJob(DailyScene s, CrewMember c, DistanceField dist)
    {
        if (!dist.Reachable(s.Spot)) return null;
        var toils = Plans.DropOff(c, _w, dist);
        toils.Add(new GotoToil(s.Spot));
        toils.Add(new WaitToil(SimTime.Minutes(1), Pose.Standing, s.Spot2.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!s.Open) return false;
            var box = world.Ship.FurnitureAt(s.Spot2);
            bool took;
            if (s.Plate >= 0)
            {
                // 이름표 붙은 접시: 음식의 접시가 비고 (주인은 늦은 끼니를 못 찾는다) · 먹은 사람은 누구 몫인지 안다
                var p = world.Cooking.Plates.FirstOrDefault(x => x.Id == s.Plate);
                took = p != null && !p.Eaten && !p.Spoiled && !p.Found;
                if (!took) { Close(s, SceneStage.Dropped, "접시가 이미 없었다"); return false; }
                p!.Eaten = true;
                Stats.Plates++;
                string owner = CrewOf(p.For)?.Name ?? "누군가";
                Trail(s, $"{cm.Name}: '{owner} 몫' 이름표를 보고도 {Ko.EulReul(p.Spec.Name)} 먹었다");
                Diary(cm, $"식탁에 '{owner} 몫' 이름표가 붙은 {p.Spec.Name}이 있었다. 배가 고파서 그만… 모른 척해야지");
            }
            else took = box?.Storage != null && (box.Storage.Take(ItemKind.Meal, 1) > 0 || box.Storage.Take(ItemKind.Produce, 1) > 0);
            if (!took) { Close(s, SceneStage.Dropped, "냉장고가 비어 있었다"); Say(cm, "아무것도 없네…"); return false; }
            s.Stage = SceneStage.Run;
            s.Holding = true;
            Arrive(s, cm);
            s.Things.Add(new SceneThing { Kind = ThingKind.Snack, At = cm.Cell, Owner = cm.Id, Since = world.Tick });
            Stats.Snacks++;
            // 본 사람 (깨어 있는 같은 방 사람)
            var wit = world.Crew.Where(o => o != cm && o.Room == cm.Room && o.IsAwake && Able(o)).OrderBy(o => o.Id).FirstOrDefault();
            if (wit != null) { s.Witness = wit.Id; Trail(s, $"{wit.Name}: 냉장고 앞의 {Ko.EulReul(cm.Name)} 봤다"); }
            return true;
        }));
        int eat = SimTime.Minutes(10);
        toils.Add(new WaitToil(eat, Pose.Standing, s.Spot2.Center) { EveryTick = (cm, _) => cm.Needs.Food = MathF.Min(1f, cm.Needs.Food + 0.15f / eat) });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!s.Open) return true;
            if (s.Victim >= 0)
            {
                _missing.Add((s.Victim, cm.Id, s.Witness, world.Tick, s.Spot2, s.Plate >= 0));
                if (s.Plate < 0) Trail(s, $"{CrewOf(s.Victim)?.Name}이(가) 남겨 둔 것이었다 (먹은 사람은 모른다)");
            }
            Close(s, SceneStage.Done, "다 먹었다");
            return true;
        }));
        return MakeJob(s, "간식", toils, 0.3f, "출출해서 냉장고를 연다");
    }

    // ── 국 닦기 ──
    private Job? CleanJob(DailyScene s, CrewMember c, DistanceField dist)
    {
        var room = RoomById(s.RoomId);
        if (room == null) return null;
        var toils = Plans.DropOff(c, _w, dist);
        if (!s.Holding)
        {
            // 도구 (걸레 · 대걸레): 가까운 보관함에서
            var (box, spot) = Plans.NearestContainer(_w, dist, c, f => true);
            if (box == null || !dist.Reachable(spot)) return null;
            toils.Add(new GotoToil(spot));
            toils.Add(new WaitToil(SimTime.Minutes(1), Pose.Standing, box.Center));
            toils.Add(new DoToil((cm, world) => { if (!s.Open) return false; s.Holding = true; Trail(s, $"{cm.Name}: {box.Room.Name}에서 걸레를 챙겼다"); return true; }));
        }
        var at = FreeCell(room, s.Spot, c, 0, x => dist.Reachable(x)) ?? s.Spot;
        toils.Add(new GotoToil(at));
        toils.Add(new DoToil((cm, world) => { if (!s.Open) return false; Arrive(s, cm); return true; }));
        int work = SimTime.Minutes(10);
        toils.Add(new WaitToil(work * 2, Pose.Working, s.Spot.Center)
        {
            EveryTick = (cm, world) =>
            {
                if (!s.Open) return;
                s.Progress = MathF.Min(1f, s.Progress + 1f / work);
                // 닦은 만큼 기름이 걷힌다 (배 본체 바닥 상태)
                float oil = world.Body.Mark(s.Spot, CellMark.Oil);
                if (oil > 0.4f * (1f - s.Progress)) world.Body.SetMark(s.Spot, CellMark.Oil, 0.4f * (1f - s.Progress), "국 기름");
            },
            DoneWhen = (cm, world) => !s.Open || s.Progress >= 1f,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!s.Open || s.Progress < 1f) return true;
            var soil = world.Soil.RoomSoil(room);
            soil[(int)SoilKind.Bio] *= 0.3f;
            // 걸레질한 바닥은 잠깐 축축하다 (배 본체가 말린다) · 기름은 걷혔다
            world.Body.SetMark(s.Spot, CellMark.Oil, 0f, "");
            world.Body.SetMark(s.Spot, CellMark.Wet, 0.3f, "걸레질");
            Stats.Cleaned++;
            var spiller = CrewOf(s.Host);
            if (spiller != null && spiller != cm)
            {
                world.Relations.Remember(spiller, cm, RelationReason.FixedMyThing, "국을 엎었을 때 걸레를 가져와 닦아 줬다");
                spiller.ChangeAffinity(cm, 0.03f);
                Log($"{Ko.IGa(cm.Name)} {spiller.Name}이(가) 엎은 국을 걸레로 닦았다", cm.Id);
            }
            else Log($"{Ko.IGa(cm.Name)} 엎은 국을 직접 닦았다", cm.Id);
            Close(s, SceneStage.Done, $"{cm.Name}: 다 닦았다");
            return true;
        }));
        return MakeJob(s, "국 닦기", toils, 0.25f);
    }

    // ── 몽유병 ──
    private Job SleepwalkJob(DailyScene s, CrewMember c)
    {
        var toils = new List<Toil>
        {
            new GotoToil(s.Spot),
            new WaitToil(SimTime.Minutes(90), Pose.Standing) { DoneWhen = (cm, world) => s.Holding || !s.Open },
            new DoToil((cm, world) =>
            {
                if (!s.Open) return false;
                if (!s.Holding) { WakeUp(s, "아무도 못 보고 혼자 깼다", endJob: false); return false; }
                return true;
            }),
            new GotoToilLate(cm => BedSpot(cm)),
            new DoToil((cm, world) =>
            {
                if (!s.Open) return true;
                Stats.Escorts++;
                var esc = CrewOf(s.Other);
                if (esc != null)
                {
                    Log($"{Ko.IGa(esc.Name)} 잠결에 걷던 {Ko.EulReul(cm.Name)} 침대로 데려다 눕혔다", esc.Id);
                    Diary(esc, $"새벽에 {Ko.IGa(cm.Name)} 잠결에 복도를 걷고 있었다. 침대로 데려다줬다");
                }
                s.Things.Clear();
                s.Stage = SceneStage.Done;
                s.Closed = world.Tick;
                Stats.Done++;
                Trail(s, "침대로 돌아왔다 (본인은 모른다)");
                return true;
            }),
        };
        return MakeJob(s, "잠결", toils, 5f);
    }

    private Cell? BedSpot(CrewMember c)
    {
        var bed = c.Bed ?? c.HomeBed;
        return bed == null ? null : SpotBy(bed);
    }

    private Job? EscortJob(DailyScene s, CrewMember c, DistanceField dist)
    {
        var walker = CrewOf(s.Host);
        if (walker == null) return null;
        var toils = new List<Toil>
        {
            new GotoToilLate(cm => s.Open ? NextTo(walker, cm) : null),
            new DoToil((cm, world) =>
            {
                if (!s.Open) return false;
                s.Holding = true;
                Say(cm, $"{walker.Name}, 어디 가? 자, 침대로 가자");
                Trail(s, $"{cm.Name}: 팔을 잡고 침대로");
                return true;
            }),
            new GotoToilLate(cm => BedSpot(walker) is Cell b ? NextFree(b, cm) : null),
            new WaitToil(SimTime.Minutes(2), Pose.Standing) { DoneWhen = (cm, world) => !s.Open || s.Stage == SceneStage.Done },
        };
        return MakeJob(s, "침대로 데려다주기", toils, 0.6f, $"잠결에 걷는 {Ko.EulReul(walker.Name)} 침대로 데려간다");
    }

    private Cell? NextFree(Cell b, CrewMember me)
    {
        foreach (var d in Cell.Dirs8)
        {
            var x = b + d;
            if (_w.Ship.IsWalkable(x) && !_w.IsSpotTaken(x, me)) return x;
        }
        return b;
    }

    // ── 소품 제작 ──
    private Job? CraftJob(DailyScene s, CrewMember c, DistanceField dist)
    {
        var toils = Plans.DropOff(c, _w, dist);
        var room = RoomById(s.PropRoom);
        if (room == null || s.Prop == null) { Drop(s, "둘 곳이 없어졌다"); return null; }
        if (!s.Holding)
        {
            if (!dist.Reachable(s.Spot)) return null;
            toils.Add(new GotoToil(s.Spot));
            toils.Add(new DoToil((cm, world) =>
            {
                if (!s.Open) return false;
                if (!s.Material && s.Prop.Material is ItemKind m)
                {
                    if (!ItemsV15.Use(world, m)) { Close(s, SceneStage.Dropped, "재료가 떨어졌다"); return false; }
                    Trail(s, $"{cm.Name}: 재료를 꺼냈다");
                }
                s.Material = true;
                Arrive(s, cm);
                return true;
            }));
            toils.Add(new WaitToil(SimTime.Hours(1.5f), Pose.Working, s.Spot2.Center) { DoneWhen = (cm, world) => !s.Open || s.Progress >= 1f });
            toils.Add(new DoToil((cm, world) =>
            {
                if (!s.Open || s.Progress < 1f) return true;
                s.Holding = true;
                s.Here.Remove(cm.Id);
                s.Things.RemoveAll(t => t.Kind == ThingKind.Work);
                Trail(s, $"{cm.Name}: 다 만들었다 — {room.Name}에 가져다 둔다");
                return true;
            }));
        }
        toils.Add(new GotoToilLate(cm => s.Open && s.Holding ? FreeCell(room, Cell.FromPosition(room.Center), cm) : null));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!s.Open || !s.Holding) return true;
            var p = world.Props.Make(cm, s.Prop!, room, paid: true, near: cm.Cell);
            if (p == null) { Close(s, SceneStage.Dropped, "둘 자리가 없었다"); return true; }
            Stats.Crafts++;
            Close(s, SceneStage.Done, $"{room.Name}에 설치했다");
            return true;
        }));
        return MakeJob(s, s.Holding ? $"{s.Prop!.Name} 옮기기" : $"{s.Prop!.Name} 만들기", toils, 0.25f, s.Progress > 0f && !s.Holding ? $"만들다 둔 {Ko.EulReul(s.Prop.Name)} 이어 만든다" : null);
    }

    // ═══════════════════════════════ 교대 인수인계 ═══════════════════════════════

    private void Shifts()
    {
        var w = _w;
        int n = w.Crew.Count;
        if (_onDuty.Length != n)
        {
            var old = _onDuty;
            Array.Resize(ref _dutyStart, n);
            Array.Resize(ref _boardSeen, n);
            _onDuty = new bool[n];
            for (int i = 0; i < n; i++) _onDuty[i] = i < old.Length ? old[i] : OnDuty(w.Crew[i]);
        }
        for (int i = 0; i < n; i++)
        {
            var c = w.Crew[i];
            bool now = !c.Dead && OnDuty(c);
            if (_onDuty[i] && !now && !c.Dead) ShiftEnd(c);
            if (!_onDuty[i] && now) _dutyStart[i] = w.Tick;
            _onDuty[i] = now;
        }
    }

    /// <summary>이 사람이 알고 있는 이상 (직접 들었거나 전해 들은 것 · 아직 확인 안 한 것).</summary>
    public IEnumerable<Concern> KnownBy(CrewMember c) => Concerns.Where(k => k.Who == c.Id && !k.Checked);

    /// <summary>이상한 소리를 들었다 (직접) — 아직 정식으로 알린 건 아니다.</summary>
    public Concern? Notice(CrewMember c, Machine m, string? text = null)
    {
        if (Concerns.Any(k => k.Who == c.Id && !k.Checked && k.Machine == m)) return null;
        var k = new Concern { Id = _nextConcern++, Who = c.Id, Kind = ConcernKind.Sound, Machine = m, Place = m.Body, Text = text ?? SoundText(m), Since = _w.Tick };
        Concerns.Add(k);
        if (Concerns.Count > 200) Concerns.RemoveAll(x => (x.Checked || x.Passed) && _w.Tick - x.Since > SimTime.TicksPerDay * 2);
        return k;
    }

    private static string SoundText(Machine m) => m.Omen?.Kind switch
    {
        OmenKind.Vibration => $"{m.Name} 소리가 이상하다",
        OmenKind.Heat => $"{m.Name} 쪽에서 탄내 비슷한 게 난다",
        OmenKind.Pressure => $"{m.Name} 흐름이 약한 것 같다",
        _ => $"{m.Name} 계기가 가끔 튄다",
    };

    private Concern Learn(CrewMember c, Concern src, string how, int from)
    {
        var k = new Concern { Id = _nextConcern++, Who = c.Id, Kind = src.Kind, Machine = src.Machine, Place = src.Place, Text = src.Text, How = how, From = from, Since = _w.Tick };
        Concerns.Add(k);
        if (src.Machine?.Omen?.Note is ShiftNote n && !n.Holders.ContainsKey(c.Id)) n.Holders[c.Id] = false; // 당직 일지에도: 관측만 전해 들었다
        // v16 통합: 말 · 메모(쪽지)로 넘겨받은 고장 기미는 믿음 장부로 — 누구에게서 들었는지 남는다 (가서 보면 굳거나 고쳐진다)
        if (src.Machine != null) _w.Brain2.Beliefs.Learn(c, Topic.Omen, src.Machine.Body.Id, 1, BeliefSource.Told, how.Contains("메모") ? 0.75f : 0.65f, from);
        return k;
    }

    /// <summary>근무가 끝났다: 아는 것을 다음 근무자에게 말로 · 메모로 넘긴다 (피로 · 서두름 · 사이가 나쁘면 빠뜨린다).</summary>
    public Handoff? ShiftEnd(CrewMember c)
    {
        var w = _w;
        var items = Concerns.Where(k => k.Who == c.Id && !k.Checked && !k.Passed).OrderBy(k => k.Id).ToList();
        if (c.Job?.Order is WorkOrder o && !o.Closed && o.Progress > 0.05f && o.Progress < 1f)
            items.Add(new Concern { Id = _nextConcern++, Who = c.Id, Kind = ConcernKind.Unfinished, Place = o.Furniture, Machine = o.Furniture?.Machine, Text = $"{o.Title} — {Pct(o.Progress)}까지 했다", Since = w.Tick });
        if (items.Count == 0) return null;
        var relief = Relief(c);
        var h = new Handoff { Id = _nextHand++, From = c.Id, To = relief?.Id ?? -1, Tick = w.Tick };
        Handoffs.Add(h);
        if (Handoffs.Count > 60) Handoffs.RemoveAt(0);
        Stats.Handoffs++;
        var told = new List<Concern>();
        foreach (var k in items)
        {
            k.Passed = true;
            float tired = c.Needs.Fatigue, hasty = Life.Has(c, Habit.Hasty) ? 0.15f : 0f, forget = Life.Has(c, Habit.Forgetful) ? 0.2f : 0f;
            float cold = relief != null && c.AffinityTo(relief) < -0.25f ? 0.2f : 0f, hungry = c.Needs.Hunger > 0.75f ? 0.1f : 0f;
            float p = ForceOmit switch { true => 1f, false => 0f, _ => Math.Clamp(0.05f + 0.4f * tired + hasty + forget + cold + hungry - 0.25f * c.Traits.Diligence, 0f, 0.9f) };
            if (R.Chance(p))
            {
                h.Dropped.Add(k.Text);
                Stats.Omitted++;
                h.Why ??= ForceOmit == true ? "깜빡" : cold > 0f ? "사이가 껄끄러워 말을 아꼈다" : tired > 0.6f ? "지쳐서" : hasty > 0f ? "서두르다" : forget > 0f ? "깜빡" : hungry > 0f ? "배가 고파 서둘러" : "깜빡";
                continue;
            }
            told.Add(k);
            h.Told.Add(k.Text);
        }
        if (told.Count > 0)
        {
            bool verbal = relief != null && relief.IsAwake && Able(relief) && c.IsAwake && (relief.Room == c.Room || (relief.Position - c.Position).LengthSquared() < 100f);
            if (verbal)
            {
                h.Verbal = true;
                Stats.Verbal++;
                foreach (var k in told) Learn(relief!, k, "말로 들음", c.Id);
                Say(c, $"인수인계: {told[0].Text}" + (told.Count > 1 ? $" 외 {told.Count - 1}건" : ""));
                Say(relief!, "알았어, 확인할게");
            }
            // 곁에 없으면 메모 — 꼼꼼한 사람은 말로 넘기고도 적어 둔다
            if ((!verbal || R.Chance(0.2f + 0.6f * c.Traits.Diligence)))
            {
                var note = Write(NoteKind.Memo, c, $"인수인계 — {string.Join(" · ", told.Select(k => k.Text))}", relief?.Id ?? -1);
                if (note != null)
                {
                    foreach (var k in told) note.Items.Add(k);
                    h.Memo = note.Id;
                    Stats.Memos++;
                }
            }
        }
        string to = relief?.Name ?? "다음 근무자";
        string text = $"{c.Name} → {to} 인수인계: " + (h.Told.Count > 0 ? string.Join(" · ", h.Told) + (h.Verbal ? " (말로)" : "") + (h.Memo >= 0 ? " (메모)" : "") : "넘긴 것 없음")
                      + (h.Dropped.Count > 0 ? $" — 빠뜨림: {string.Join(" · ", h.Dropped)} ({h.Why})" : "");
        Log(text, c.Id);
        return h;
    }

    /// <summary>다음 근무자: 지금 막 시작하거나 한 시간 안에 시작하는 사람 → 같은 일 → 가까운 사람.</summary>
    private CrewMember? Relief(CrewMember c)
    {
        var w = _w;
        float hour = SimTime.HourOfDay(w.Tick);
        CrewMember? best = null;
        float bestScore = float.MinValue;
        foreach (var x in w.Crew)
        {
            if (x == c || x.Dead || !x.CanAct || x.IsChild || x.Outside) continue;
            bool starting = SimTime.InWindow(hour + 1f, x.Schedule.WorkStart, 1.5f);
            bool on = WatchLog.OnShift(x, w);
            if (!starting && !on) continue;
            float sc = (starting ? 10f : 0f) + (x.Role == c.Role ? 3f : 0f) - (x.Position - c.Position).Length() * 0.01f;
            if (sc > bestScore) { bestScore = sc; best = x; }
        }
        return best;
    }

    private Job? CheckJob(Concern k, CrewMember c, DistanceField dist)
    {
        var f = k.Machine?.Body ?? k.Place;
        if (f == null) { k.Checked = true; return null; }
        if (Plans.WorkSpot(f, _w, dist, c) is not Cell spot) return null;
        var toils = Plans.DropOff(c, _w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Working, f.Center));
        toils.Add(new DoToil((cm, world) => { Check(k, cm); return true; }));
        return MakeJob(null, "인수인계 확인", toils, 0.25f, $"{(k.How == "메모로 읽음" ? "인수인계 메모" : k.How == "말로 들음" ? "인수인계" : "들은 소리")}대로 {f.Label} 쪽을 확인하러 간다");
    }

    /// <summary>가서 봤다: 정말 이상하면 정식으로 알린다 (전조 발견 · 정비로 이어진다).</summary>
    public void Check(Concern k, CrewMember c)
    {
        if (k.Checked) return;
        k.Checked = true;
        Stats.Checks++;
        var from = CrewOf(k.From);
        if (k.Machine is Machine m && m.Omen is Omen o)
        {
            Prevention.Detect(_w, m, o, "당직", c);
            k.Result = "정말 이상했다";
            Stats.Found++;
            Say(c, k.Kind == ConcernKind.Sound ? "정말 소리가 다르네" : "이어서 손봐야겠다");
            Log($"{Ko.IGa(c.Name)} {(from != null ? $"{from.Name}의 인수인계대로" : "들은 대로")} {Ko.EulReul(m.Name)} 확인했다 — 정말 이상하다 (정비로 넘긴다)", c.Id);
            if (from != null) { c.ChangeAffinity(from, 0.02f); _w.Relations.Remember(c, from, RelationReason.KeptPromise, $"인수인계에 {Ko.EulReul(m.Name)} 적어 둔 덕에 일찍 찾았다"); }
        }
        else
        {
            k.Result = "별일 없었다";
            Log($"{Ko.IGa(c.Name)} {k.Text} — 확인해 보니 별일 없었다", c.Id);
        }
        foreach (var other in Concerns) if (other.Machine == k.Machine && k.Machine != null && other.Who == c.Id) other.Checked = true;
    }

    private Job? BoardJob(CrewMember c, DistanceField dist)
    {
        if (DutyBoard() is not var (room, at) || FreeCell(room, at, c, 0, x => dist.Reachable(x)) is not Cell spot) return null;
        var toils = Plans.DropOff(c, _w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(1), Pose.Standing, at.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (cm.Id < _boardSeen.Length) _boardSeen[cm.Id] = world.Tick;
            foreach (var n in Notes.Where(n => !n.Gone && n.RoomId == room.Id).ToList()) if (!n.Readers.Contains(cm.Id) && CanRead(n, cm)) Read(n, cm);
            return true;
        }));
        return MakeJob(null, "당직 게시판", toils, 0.2f);
    }

    /// <summary>당직 게시판: 함교 콘솔 곁 (없으면 식당 벽).</summary>
    public (Room room, Cell at)? DutyBoard()
    {
        foreach (var t in new[] { RoomType.Bridge, RoomType.BackupBridge, RoomType.Comms })
            foreach (var r in _w.Ship.RoomsOf(t))
            {
                if (!Usable(r)) continue;
                var con = r.Furniture.Where(f => f.Type is FurnitureType.Console or FurnitureType.MainComputer).OrderBy(f => f.Id).FirstOrDefault();
                if (con != null && SpotBy(con) is Cell s) return (r, s);
                if (WallCell(r) is Cell wc) return (r, wc);
            }
        return RosterBoard();
    }

    /// <summary>당번표 게시판: 식당(없으면 주방) 벽.</summary>
    public (Room room, Cell at)? RosterBoard()
    {
        foreach (var t in new[] { RoomType.Mess, RoomType.Galley, RoomType.Lounge })
            foreach (var r in _w.Ship.RoomsOf(t))
                if (Usable(r) && WallCell(r) is Cell wc) return (r, wc);
        return null;
    }

    // ═══════════════════════════════ 쪽지 · 게시판 ═══════════════════════════════

    /// <summary>쪽지를 쓴다 (냉장고 · 당번표 · 카드 · 메모) — 그 자리에 물건으로 남는다.</summary>
    public ShipNote? Write(NoteKind kind, CrewMember author, string text, int forId = -1, bool anonymous = false, int thief = -1, int witness = -1, Furniture? on = null)
    {
        Room room;
        Cell at;
        switch (kind)
        {
            case NoteKind.Fridge:
                var fr = on ?? _w.Ship.Furniture.Where(f => f.Type == FurnitureType.Fridge && Usable(f.Room) && !f.Stowed).OrderBy(f => f.Id).FirstOrDefault();
                if (fr == null) return null;
                room = fr.Room;
                at = SpotBy(fr) ?? fr.Cells[0];
                break;
            case NoteKind.Memo:
                if (DutyBoard() is not var (dr, da)) return null;
                (room, at) = (dr, da);
                break;
            default:
                if (RosterBoard() is not var (rr, ra)) return null;
                (room, at) = (rr, ra);
                break;
        }
        var n = new ShipNote { Id = _nextNote++, Kind = kind, At = at, RoomId = room.Id, Author = author.Id, Text = text, Written = _w.Tick, For = forId, Anonymous = anonymous, Thief = thief, Witness = witness };
        n.Readers.Add(author.Id);
        Notes.Add(n);
        Stats.Notes++;
        if (Notes.Count > 60) Notes.RemoveAll(x => x.Gone || _w.Tick - x.Written > SimTime.TicksPerDay * 4 && x.Kind != NoteKind.Card);
        return n;
    }

    /// <summary>읽을 수 있나: 메모는 받을 사람(없으면 누구나) · 생일 카드는 주인공 몰래.</summary>
    private static bool CanRead(ShipNote n, CrewMember c) => n.Kind switch
    {
        NoteKind.Memo => n.For < 0 || n.For == c.Id,
        NoteKind.Card => c.Id != n.For || n.Given,
        _ => true,
    };

    /// <summary>5분마다: 쪽지 곁에 선 사람이 읽고 · 국 자국이 있는 방에 온 사람이 본다.</summary>
    private void Look()
    {
        var w = _w;
        foreach (var n in Notes)
        {
            if (n.Gone) continue;
            // 불: 종이 쪽지는 탄다 — 아직 못 읽은 메모면 다음 근무자는 끝내 모른다
            if (w.Fire.Count > 0 && w.Fire.AnyWithin(n.At, 2.5f))
            {
                n.Gone = true;
                Stats.Burned++;
                Log($"{RoomById(n.RoomId)?.Name} 불길에 {(n.Kind switch { NoteKind.Memo => "인수인계 메모", NoteKind.Card => "생일 카드", NoteKind.Fridge => "냉장고 쪽지", _ => "당번표" })}가 탔다" + (n.Kind == NoteKind.Memo && n.For >= 0 && !n.Readers.Contains(n.For) ? " — 받을 사람은 아직 못 읽었다" : ""), n.Author);
                continue;
            }
            var center = n.At.Center;
            foreach (var c in w.Crew)
            {
                if (c.Room?.Id != n.RoomId || !c.IsAwake || !Able(c) || c.IsChild && c.Age < 8f || n.Readers.Contains(c.Id)) continue;
                if ((c.Position - center).LengthSquared() > 6.25f || !CanRead(n, c)) continue;
                Read(n, c);
            }
        }
        foreach (var s in Scenes)
        {
            if (!s.Open || s.Kind != SceneKind.Spill) continue;
            foreach (var c in w.Crew)
            {
                if (c.Room?.Id != s.RoomId || !c.IsAwake || !Able(c)) continue;
                if (!s.Seen.Contains(c.Id)) See(s, c);
            }
            // 닦을 사람이 없다 (맡았던 사람이 못 왔다): 그 방에 있는 꼼꼼한 사람이 나서고, 두 시간 넘게 남으면 보다 못한 누구라도
            if (s.Other < 0)
            {
                long age = w.Tick - s.Opened;
                CrewMember? v = null;
                foreach (var c in w.Crew)
                {
                    if (c.Room?.Id != s.RoomId || !c.IsAwake || !Able(c) || c.IsChild || Busy(c)) continue;
                    bool keen = Life.Has(c, Habit.NeatFreak) || Life.Has(c, Habit.Generous) || c.Traits.Diligence > 0.6f || c.Id == s.Host && c.Traits.Diligence >= 0.3f;
                    if (!keen && age < SimTime.Hours(2)) continue;
                    if (v == null || c.Traits.Diligence > v.Traits.Diligence) v = c;
                }
                if (v != null)
                {
                    s.Other = v.Id;
                    Trail(s, age >= SimTime.Hours(2) ? $"{v.Name}: 보다 못해 걸레를 든다" : $"{v.Name}: 내가 닦을게");
                    v.Interrupt(w);
                }
            }
        }
        // 주 컴퓨터: 영화 소리 × 옆방 잠 · 안 읽은 인수인계 메모
        foreach (var s in Scenes)
            if (s.Kind == SceneKind.Movie && s.Stage == SceneStage.Run && !s.Holding) QuietMovie(s);
        RemindMemos();
    }

    // ═══════════════════════════════ 주 컴퓨터가 본다 ═══════════════════════════════
    // 컴퓨터는 감지기 · 단말 · 출입 기록으로 아는 것만 안다 (사람이 아는 것과 다르다 — 빠뜨린 인수인계는 컴퓨터도 모른다):
    //  · 새벽 복도의 움직임: 목적지 없이 선 사람 → 잠결 걸음으로 보고 야간 당직을 부른다 (복도 조명 낮게)
    //  · 영화 소리 × 옆방에서 자는 사람 → 볼륨을 낮춘다 (야간 소음 관리 · 높은 등급) / 아니면 연 사람에게 단말 알림 (사람이 정한다)
    //  · 당직 게시판 단말: 받을 사람이 근무를 시작하고도 안 연 인수인계 메모 → 개인 알림 → 게시판으로 간다
    //  · 커피 · 찻잎 재고가 바닥 → 보급 목록 맨 위에 (제안)

    /// <summary>컴퓨터가 그 방을 볼 수 있나: 주 컴퓨터가 돌고 · 방에 전기와 데이터선이 있다.</summary>
    private bool Sees(Room? r) => r != null && _w.Automation.Present && _w.Automation.MainOnline && r.Powered && r.DataLinked && !r.Detached;

    private void Msg(CrewMember c, string text)
    {
        var ms = _w.Automation.Apps.Messages;
        ms.Add(new PersonalMessage(_w.Tick, c.Id, "알림", text));
        if (ms.Count > 120) ms.RemoveAt(0);
        _w.Automation.Book.Today.Messages++;
    }

    private void PageWatch(DailyScene s, CrewMember z, Room hall)
    {
        var w = _w;
        CrewMember? watch = null;
        float best = float.MaxValue;
        foreach (var o in w.Crew)
        {
            if (o == z || !Able(o) || !o.IsAwake || o.IsChild || Busy(o)) continue;
            bool nw = w.Society.OnNightWatch(o);
            // 야간 당직 먼저 · 근무 중인 사람 · 가까운 사람 — 아무도 근무 중이 아니면 깨어 있는 어른 누구든 (컴퓨터가 쉬라고 근무에서 뺀 사람도 깨어 있으면 부른다)
            float d = (o.Position - z.Position).LengthSquared() - (nw ? 10000f : 0f) + (nw || OnDuty(o) ? 0f : 10000f);
            if (d < best) { best = d; watch = o; }
        }
        int mins = (int)((w.Tick - s.Since) / (float)SimTime.Minutes(1));
        var a = w.Automation.Book.Add(ActKind.Advice, hall, $"{SimTime.Clock(w.Tick)} {hall.Name} 움직임 — {z.Name} · {mins}분째 제자리 · 침대가 비었다",
            "잠결 걸음으로 본다 (깨어 걷는 걸음이 아니다)", watch != null ? "야간 당직 호출 · 복도 조명 낮게" : "복도 조명 낮게 (깨어 있는 사람이 없다)",
            watch != null ? $"{watch.Name} → {hall.Name}" : "", "sw:" + s.Id, 0, 90f,
            (world, act) => s.Stage == SceneStage.Done && s.Holding ? (1, $"{CrewOf(s.Other)?.Name}이(가) 침대로 데려갔다") : !s.Open ? (2, "혼자 깼다") : null);
        if (a == null) return;
        s.ComputerAct = a.Id;
        Stats.PcPages++;
        Trail(s, $"주 컴퓨터: 복도 움직임을 봤다 → {(watch != null ? $"{watch.Name} 호출" : "깨어 있는 사람 없음")}");
        if (watch == null) return;
        s.Other = watch.Id;
        if (!s.Seen.Contains(watch.Id)) s.Seen.Add(watch.Id);
        Msg(watch, $"{hall.Name}에 {Ko.IGa(z.Name)} 잠결에 서 있습니다 — 침대로 데려가 주세요");
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {watch.Name}, {hall.Name}에 {Ko.IGa(z.Name)} 잠결에 서 있습니다 — 침대로 데려가 주세요", watch.Id);
        watch.Interrupt(w);
    }

    /// <summary>영화 소리가 옆방에서 자는 사람을 깨울 것 같으면: 볼륨을 낮추거나(야간 소음 관리 · 등급 3 이상) 연 사람에게 알린다.</summary>
    private void QuietMovie(DailyScene s)
    {
        var w = _w;
        if (RoomById(s.RoomId) is not Room mr || !Sees(mr) || CrewOf(s.Host) is not CrewMember host) return;
        Room? nb = null;
        int sleepers = 0;
        foreach (var (o, _) in w.Ambience.Neighbors(mr))
        {
            if (o.Noise < 0.12f) continue;
            int n = 0;
            foreach (var c in w.Crew) if (!c.Dead && c.Room == o && c.Pose == Pose.Sleeping) n++;
            if (n > sleepers) { sleepers = n; nb = o; }
        }
        if (nb == null) return;
        var au = w.Automation;
        bool can = au.Active(ComputerModule.QuietNight) || au.Level >= 3;
        var room = nb;
        int before = sleepers;
        var a = au.Book.Add(ActKind.Advice, mr, $"{mr.Name} 영화 소리 · 옆 {room.Name} 소음 {room.Noise * 100:0}% · {sleepers}명 자는 중", "잠을 깨울 소리다",
            can ? "스피커 볼륨을 낮춤" : $"{host.Name}에게 단말 알림", can ? "" : "소리를 줄여 달라", "mvq:" + s.Id, SimTime.Hours(1), 30f,
            (world, act) =>
            {
                int still = 0;
                foreach (var c in world.Crew) if (!c.Dead && c.Room == room && c.Pose == Pose.Sleeping) still++;
                return still >= before ? (1, $"옆방 {still}명 계속 잠") : (2, $"옆방 {before - still}명 깸");
            });
        if (a == null) return;
        s.ComputerAct = a.Id;
        if (can)
        {
            s.Holding = true;
            Stats.PcQuiet++;
            au.Book.Today.Quiet++;
            Trail(s, $"주 컴퓨터: 옆 {room.Name}에 {sleepers}명이 자서 볼륨을 낮췄다");
            return;
        }
        // 알림만: 연 사람이 정한다 (꼼꼼하면 줄인다)
        Msg(host, $"옆 {room.Name}에 {sleepers}명이 자고 있습니다 — 영화 소리를 줄여 주세요");
        if (R.Chance(Math.Clamp(0.35f + 0.6f * host.Traits.Diligence, 0.1f, 0.95f)))
        {
            s.Holding = true;
            Stats.Hushed++;
            Say(host, "아, 옆방에서 자는구나 — 소리 줄일게");
            Trail(s, $"{host.Name}: 컴퓨터 알림을 보고 소리를 줄였다");
        }
        else Trail(s, $"{host.Name}: 컴퓨터 알림을 못 본 척했다");
    }

    /// <summary>당직 게시판 단말: 받을 사람이 근무를 시작한 지 45분이 지나도 안 연 인수인계 메모 → 개인 알림.</summary>
    private void RemindMemos()
    {
        var w = _w;
        foreach (var n in Notes)
        {
            if (n.Gone || n.Pinged || n.Kind != NoteKind.Memo || n.For < 0 || n.Items.Count == 0 || n.Readers.Contains(n.For)) continue;
            if (CrewOf(n.For) is not CrewMember c || c.Dead || !c.IsAwake || !OnDuty(c) || c.Id >= _dutyStart.Length || _dutyStart[c.Id] <= 0) continue;
            long on = w.Tick - _dutyStart[c.Id];
            if (on < SimTime.Minutes(45) || RoomById(n.RoomId) is not Room br || !Sees(br)) continue;
            var note = n;
            var a = w.Automation.Book.Add(ActKind.Advice, br, $"당직 게시판 인수인계 메모 — {c.Name} 근무 {on / SimTime.Minutes(1)}분째 · 아직 안 열어 봄",
                "받을 사람이 모르고 지나갈 수 있다", "개인 단말 알림", $"{c.Name} → {br.Name} 게시판", "memo:" + n.Id, 0, 120f,
                (world, act) => note.Readers.Contains(note.For) ? (1, "읽었다") : note.Gone ? (2, "메모가 없어졌다") : null);
            if (a == null) continue;
            n.Pinged = true;
            Stats.PcMemo++;
            Msg(c, $"당직 게시판에 {CrewOf(n.Author)?.Name ?? "앞 근무자"}의 인수인계 메모가 있습니다");
            Log($"주 컴퓨터: {c.Name}에게 — 당직 게시판에 인수인계 메모가 있습니다 (아직 안 읽음)", c.Id);
        }
    }

    /// <summary>커피 · 찻잎이 바닥나 간다 → 보급 목록 맨 위에 (제안).</summary>
    private void StockAdvice(ItemKind k, Room? room)
    {
        var w = _w;
        int left = w.Ship.CountStored(k);
        if (left > 2) return;
        string what = k == ItemKind.TeaLeaf ? "찻잎" : "커피";
        float days = MathF.Max(1f, SimTime.Day(w.Tick) + 1f);
        float perDay = MathF.Max(0.5f, Stats.Cups / 3f / days);
        var a = w.Automation.Book.Add(ActKind.Advice, room, $"{what} 재고 {left}통 · 하루 {perDay:0.#}통꼴로 준다", left == 0 ? "다 떨어졌다" : $"{left / perDay:0.#}일이면 떨어진다",
            "보급 목록 맨 위에 올림", "다음 기항지에서 사기", "stock:" + (int)k, SimTime.TicksPerDay, 60f);
        if (a != null) Stats.PcStock++;
    }

    /// <summary>읽었다 — 읽은 사람만 안다. 반응 (웃음 · 짜증 · 관계 · 서명 · 확인하러 감).</summary>
    public void Read(ShipNote n, CrewMember c)
    {
        if (n.Readers.Contains(c.Id)) return;
        n.Readers.Add(c.Id);
        Stats.Reads++;
        var author = CrewOf(n.Author);
        switch (n.Kind)
        {
            case NoteKind.Memo:
                foreach (var k in n.Items)
                    if (!Concerns.Any(x => x.Who == c.Id && !x.Checked && x.Machine == k.Machine && x.Text == k.Text)) Learn(c, k, "메모로 읽음", n.Author);
                if (n.Items.Count > 0) { Say(c, $"메모 봤어 — {n.Items[0].Text}, 확인해 볼게"); Log($"{Ko.IGa(c.Name)} {author?.Name}의 인수인계 메모를 읽었다: {n.Text}", c.Id); }
                break;
            case NoteKind.Fridge:
                if (c.Id == n.Thief)
                {
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
                    Diary(c, $"냉장고에 \"{n.Text}\" 쪽지가 붙었다. 어젯밤 그건 {author?.Name}의 것이었구나… 모른 척해야지");
                }
                else if (c.Id == n.Witness && author != null && CrewOf(n.Thief) is CrewMember thief && c.AffinityTo(author) >= c.AffinityTo(thief))
                {
                    // 본 사람이 쪽지를 읽고 말해 준다 — 그제야 주인이 안다
                    author.ChangeAffinity(thief, -0.04f);
                    _w.Relations.Remember(author, thief, RelationReason.TookMyThing, "냉장고에 둔 간식을 몰래 먹었다");
                    Say(c, $"그거 {thief.Name}이(가) 먹던데");
                    Log($"{Ko.IGa(c.Name)} 냉장고 쪽지를 보고 {author.Name}에게 귀띔했다 — 범인은 {thief.Name}", c.Id);
                }
                else if (Life.Has(c, Habit.Joker) || Life.Has(c, Habit.Prankster)) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.02f); Say(c, "푸딩 전쟁이네"); Stats.Laughs++; if (author != null) author.ChangeAffinity(c, -0.01f); }
                else if (Life.Has(c, Habit.Grumbler)) { Say(c, "쪽지까지 붙일 일이야?"); Stats.Annoyed++; if (author != null) c.ChangeAffinity(author, -0.01f); }
                else c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.01f);
                break;
            case NoteKind.Roster:
                if (c.Id == n.For)
                {
                    // 놀림 받은 사람: 누가 썼는지 모른다 — 싫어하던 사람을 의심한다 (틀릴 수 있다)
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f);
                    Stats.Annoyed++;
                    Say(c, "누가 당번표에 이런 걸 써 놨어");
                    var suspect = _w.Crew.Where(o => o != c && !o.Dead && !o.IsChild).OrderBy(o => c.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
                    if (suspect != null)
                    {
                        c.ChangeAffinity(suspect, -0.03f);
                        bool right = suspect.Id == n.Author;
                        Diary(c, $"당번표에 나를 놀리는 낙서가 있다. {Ko.EulReul(suspect.Name)} 의심한다" + (right ? "" : ""));
                        Log($"{Ko.IGa(c.Name)} 당번표 낙서를 보고 {Ko.EulReul(suspect.Name)} 의심했다" + (right ? " — 맞았다" : " — 쓴 사람은 따로 있다"), c.Id);
                    }
                }
                else if (Life.Has(c, Habit.Joker) || Life.Has(c, Habit.Prankster) || Life.Has(c, Habit.Cheerful)) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.02f); c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.03f); Stats.Laughs++; Say(c, "하하, 누가 썼어"); }
                else if (Life.Has(c, Habit.NeatFreak) || Life.Has(c, Habit.Serious)) { Stats.Annoyed++; Say(c, "당번표에 낙서하지 마"); }
                break;
            case NoteKind.Card:
                if (c.Id == n.For)
                {
                    var signers = n.Signers.Select(CrewOf).OfType<CrewMember>().ToList();
                    c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f - 0.01f * signers.Count);
                    c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.2f);
                    foreach (var o in signers) c.ChangeAffinity(o, 0.03f);
                    Diary(c, $"생일 카드에 {signers.Count}명이 한마디씩 적어 줬다" + (signers.Count > 0 ? $" — {string.Join(", ", signers.Take(4).Select(o => o.Name))}" : ""));
                    _w.History.Add(_w, HistoryKind.Bond, $"{c.Name}의 생일 카드 — {signers.Count}명이 돌려 가며 서명했다", c.Room, new[] { c }, log: true);
                    n.Gone = true;
                }
                else if (!n.Given && c.Id != n.For && CrewOf(n.For) is CrewMember who && c.AffinityTo(who) > -0.2f && !(Life.Has(c, Habit.Grumbler) && R.Chance(0.5f)))
                {
                    n.Signers.Add(c.Id);
                    Stats.Signed++;
                    Say(c, $"{who.Name} 카드에 한마디 적었다");
                    who.ChangeAffinity(c, 0f);
                }
                break;
        }
    }

    // ═══════════════════════════════ 한 시간마다 ═══════════════════════════════

    private void Hourly()
    {
        var w = _w;
        int day = SimTime.Day(w.Tick);
        float hour = SimTime.HourOfDay(w.Tick);
        // 근무 중인 사람이 이상한 소리를 듣는다 (그 설비를 만져 본 사람일수록) — 아직 정식으로 알린 건 아니다
        if (!Crisis.Acting(w))
            foreach (var c in w.Crew)
            {
                if (!Able(c) || !c.IsAwake || c.IsChild || !OnDuty(c)) continue;
                foreach (var m in w.Ship.Machines)
                {
                    if (m.Body.Room != c.Room || m.Omen is not Omen o || o.Known) continue;
                    if (R.Chance(0.1f + 0.35f * c.FamiliarityWith(m.Body.Type))) Notice(c, m);
                }
            }
        // 없어진 간식: 주인이 냉장고를 열어 보면 안다 → 냉장고 쪽지
        for (int i = _missing.Count - 1; i >= 0; i--)
        {
            var (vid, thief, wit, at, box, plate) = _missing[i];
            int roomId = _w.Ship.RoomAt(box)?.Id ?? -1;
            var v = CrewOf(vid);
            if (v == null || v.Dead || w.Tick - at > SimTime.TicksPerDay * 2) { _missing.RemoveAt(i); continue; }
            if (!v.IsAwake || !Able(v) || hour < 6f || v.Room?.Id != roomId && w.Tick - at < SimTime.Hours(10)) continue;
            _missing.RemoveAt(i);
            bool knows = wit == vid;
            var t = CrewOf(thief);
            string mine = plate ? "이름표 붙여 둔 내 몫" : "냉장고에 둔 내 푸딩";
            Diary(v, $"{mine}이 없어졌다" + (knows && t != null ? $". {Ko.IGa(t.Name)} 먹는 걸 봤다" : ". 누구지"));
            var note = Write(NoteKind.Fridge, v, plate ? (knows && t != null ? $"{t.Name}, 이름표 붙은 건 남의 거야" : "이름표 붙은 접시 먹은 사람? 내 저녁이었어") : knows && t != null ? $"{t.Name}, 내 푸딩 먹지 마" : "내 푸딩 먹지 마 — 이름 적어 둔 거 안 보여?",
                thief: thief, witness: wit, on: plate ? null : _w.Ship.FurnitureAt(box));
            if (knows && t != null) { v.ChangeAffinity(t, -0.04f); _w.Relations.Remember(v, t, RelationReason.TookMyThing, "냉장고에 둔 간식을 몰래 먹었다"); }
            if (note != null) Log($"{Ko.IGa(v.Name)} 냉장고에 쪽지를 붙였다: \"{note.Text}\"", v.Id);
        }
        // 몽유병: 데려다준 사람이 아침에 말해 준다 — 그제야 본인이 안다
        foreach (var s in Scenes)
        {
            if (s.Kind != SceneKind.Sleepwalk || s.Stage != SceneStage.Done || s.Told || s.Other < 0) continue;
            var c = CrewOf(s.Host);
            var e = CrewOf(s.Other);
            if (c == null || e == null || c.Dead || e.Dead || w.Tick - s.Closed > SimTime.TicksPerDay) { s.Told = true; continue; }
            // 개인 비서가 아침에 감지 기록을 알려 주기도 한다 — 컴퓨터가 본 것(복도 감지)만, 누가 데려갔는지는 호출 기록으로
            if (s.ComputerAct >= 0 && !s.PcTold && c.IsAwake && hour >= 6f && w.Automation.MainOnline && w.Automation.Active(ComputerModule.Assistant))
            {
                s.PcTold = true;
                Msg(c, $"어젯밤 {RoomById(s.RoomId)?.Name ?? "복도"}에서 잠결 걸음이 감지됐습니다 — {e.Name}님을 호출했습니다");
                Diary(c, $"개인 비서 기록을 보니 어젯밤 복도에서 잠결 걸음이 잡혔단다. 기억이 하나도 없다");
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
            }
            if (!c.IsAwake || !e.IsAwake || c.Room != e.Room) continue;
            s.Told = true;
            bool joke = Life.Has(e, Habit.Joker) || Life.Has(e, Habit.Prankster);
            Say(e, joke ? "어젯밤 복도에서 산책하던데?" : "어젯밤에 자다가 걸어 나왔었어");
            // 팔을 잡고 데려다줬을 때만 고마워한다 (보기만 했으면 들은 것만 안다)
            Diary(c, $"내가 어젯밤 자다가 복도까지 걸어 나왔단다. " + (s.Holding ? $"{Ko.IGa(e.Name)} 침대로 데려다줬다고" : $"{Ko.IGa(e.Name)} 봤다고") + (joke ? " — 아침 내내 놀렸다" : ""));
            if (s.Holding) _w.Relations.Remember(c, e, RelationReason.Comforted, "잠결에 복도를 걷던 나를 침대로 데려다줬다");
            c.ChangeAffinity(e, joke ? 0.01f : s.Holding ? 0.04f : 0.01f);
            if (joke) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
        }
        // 영화 소리에 옆방에서 자던 사람이 잠을 설친다 → 따지러 오면 소리를 줄인다
        foreach (var s in Scenes)
        {
            if (s.Kind != SceneKind.Movie || s.Stage != SceneStage.Run || RoomById(s.RoomId) is not Room mr || CrewOf(s.Host) is not CrewMember host) continue;
            foreach (var c in w.Crew)
            {
                if (c.Pose != Pose.Sleeping || c.Dead || c.Room == null || c.Room == mr || c.Room.Noise < 0.12f || s.Seen.Contains(c.Id)) continue;
                if (!w.Ambience.Neighbors(mr).Any(nb => nb.room == c.Room)) continue;
                s.Seen.Add(c.Id);
                c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.03f);
                c.ChangeAffinity(host, -0.02f);
                Diary(c, $"옆방 영화 소리에 잠을 설쳤다");
                if (!s.Holding && (Life.Has(c, Habit.Grumbler) || Life.Has(c, Habit.ShortTempered) || Life.Has(c, Habit.Insomniac) || c.Needs.Rest < 0.4f))
                {
                    s.Holding = true; // 소리를 줄였다
                    Stats.Hushed++;
                    Trail(s, $"{c.Name}: 옆방에서 소리 좀 줄여 달라고 왔다");
                    Log($"{Ko.IGa(c.Name)} 잠을 설치다 {mr.Name}에 와서 영화 소리를 줄여 달라고 했다", c.Id);
                }
            }
        }
        // 놓인 잔은 식고 · 다 마시면 치운다 · 받을 사람이 읽은 메모는 반나절 뒤 뗀다
        foreach (var s in Scenes) s.Things.RemoveAll(t => t.Until >= 0 && w.Tick >= t.Until);
        foreach (var n in Notes)
            if (n.Kind == NoteKind.Memo && !n.Gone && w.Tick - n.Written > SimTime.Hours(12) && (n.For >= 0 ? n.Readers.Contains(n.For) : n.Readers.Count > 1)) n.Gone = true;
        if (Crisis.Acting(w)) return;
        // 생일 하루 전: 누군가 카드를 돌린다 · 생일엔 건넨다
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            int bday = (c.Id * 37 + w.Seed) % 30;
            var card = Notes.FirstOrDefault(n => n.Kind == NoteKind.Card && n.For == c.Id && !n.Gone && w.Tick - n.Written < SimTime.TicksPerDay * 3);
            if (card == null && bday == (day + 1) % 30 && hour >= 9f && hour < 20f)
            {
                var org = w.Crew.Where(o => o != c && !o.Dead && !o.IsChild && o.IsAwake && Able(o)).OrderByDescending(o => o.AffinityTo(c)).ThenBy(o => o.Id).FirstOrDefault();
                if (org != null && Write(NoteKind.Card, org, $"{c.Name} 생일 축하해! — 한마디씩", c.Id) is ShipNote n)
                    Log($"{Ko.IGa(org.Name)} {c.Name} 몰래 생일 카드를 돌리기 시작했다 (게시판에서 한마디씩)", org.Id);
            }
            else if (card != null && !card.Given && bday == day % 30 && hour >= 12f && c.IsAwake && Able(c))
            {
                card.Given = true;
                card.At = c.Cell;
                card.RoomId = c.Room!.Id;
                if (CrewOf(card.Author) is CrewMember org) Say(org, $"{c.Name}, 생일 축하해 — 다들 한마디씩 적었어");
                Read(card, c);
            }
        }
        // 당번표 낙서: 장난꾸러기 · 투덜이가 가끔 (이름 없이)
        if (R.Chance(1f / 30f) && !Notes.Any(n => n.Kind == NoteKind.Roster && !n.Gone && w.Tick - n.Written < SimTime.TicksPerDay * 2))
        {
            var author = w.Crew.Where(o => Able(o) && o.IsAwake && !o.IsChild && (Life.Has(o, Habit.Prankster) || Life.Has(o, Habit.Joker) || Life.Has(o, Habit.Grumbler))).OrderBy(o => o.Id).ToList();
            if (author.Count > 0)
            {
                var a = author[R.Range(0, author.Count)];
                var target = w.Crew.Where(o => o != a && !o.Dead && !o.IsChild).OrderByDescending(o => Life.Has(o, Habit.Messy) || Life.Has(o, Habit.Procrastinator) ? 1 : 0).ThenBy(o => a.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
                if (target != null)
                {
                    string[] lines = { $"{target.Name} 차례: 영원히 미정", $"{target.Name} — 설거지 당번 또 건너뜀", $"{target.Name}의 양말 실종 사건 담당", $"{target.Name} 당번 = 전설 속 이야기" };
                    var n = Write(NoteKind.Roster, a, lines[R.Range(0, lines.Length)], target.Id, anonymous: true);
                    if (n != null) Trail0($"{a.Name}이(가) 당번표에 낙서했다 (이름 없이)");
                }
            }
        }
    }

    private void Trail0(string text) => _w.Log.Add(_w.Tick, LogKind.Life, text, -1);

    // ═══════════════════════════════ 지문 ═══════════════════════════════

    public int Hash()
    {
        unchecked
        {
            int h = 17;
            void I(int v) => h = h * 31 + v;
            I(Scenes.Count); I(Notes.Count); I(Concerns.Count); I(Handoffs.Count); I(Stats.Opened); I(Stats.Done); I(Stats.Cups); I(Stats.Reads); I(Stats.Checks);
            foreach (var s in Scenes) { I(s.Id); I((int)s.Stage); I(BitConverter.SingleToInt32Bits(s.Progress)); I(s.Here.Count); I(s.Pauses); I(s.Things.Count); }
            foreach (var n in Notes) { I(n.Id); I(n.Readers.Count); I(n.Signers.Count); I(n.Gone ? 1 : 0); }
            foreach (var k in Concerns) { I(k.Who); I(k.Checked ? 1 : 0); I(k.Passed ? 1 : 0); }
            return h;
        }
    }
}

/// <summary>v16.1 일상 장면의 내 몫: 판 · 영화 · 커피 · 간식 · 닦기 · 침대로 · 소품 · 인수인계 확인 · 당직 게시판.</summary>
public sealed class SceneActivity : Activity
{
    public static readonly SceneActivity Instance = new();
    public override string Id => "scene";
    public override string Label => "일상 장면";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var (score, why, _, _, _) = w.Scenes.Best(c, dist);
        return (MathF.Max(0f, score), why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist) => w.Scenes.Plan(c, dist);
}
