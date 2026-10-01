using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.17 방은 승무원이 정한다 — ② 제안 → 회의 → 실행 · ③ 이름 · ④ 기관 구역 · ⑤ 바뀐 배치의 뒤끝.
//
// 제안: 한 시간마다 승무원의 불편(몸이 굳었다 · 좁다) · 겪은 일(뚫린 기관실 · 그날의 식당 · 떠난 사람) · 인원 변화 · 개인 목표(선실 꾸미기)
//   · 쓰임(다들 벌써 그렇게 부르는 방) · 한 사람이 도맡은 방("서지안의 작업장")에서 안건이 나온다. 낸 사람이 있다.
// 주 컴퓨터: 안건마다 위험 · 공사 기간 · 멈추는 계통 · 공사 순서를 다섯 칸 기록에 남기고, 기관 설비 이전은 제안 카드(공사 순서)를 낸다.
//   카드를 받으면 순서대로 한다 (부하를 넘기고 · 차단하고 · 비우고 떼어 낸다 — 사고 위험이 준다).
// 회의: 정기 회의 안건으로 토론(낸 사람 · 몸이 굳은 사람 · 무서운 방 · 안전 가치관 · 이름의 주인 · 컴퓨터를 믿는 정도) → 표결.
// 실행은 실제 행동: 분리(점검 뚜껑 · 공사 테이프 · 설비가 멈춘다) → 들기(무거우면 둘이, 아니면 카트 · 혼자 들다 허리를 삐끗)
//   → 나르기(끊기면 그 자리에 내려놓은 상자 — 다음 사람이 이어 나른다) → 놓기(길 · 기억 · 습관) → 다시 잇기(케이블 · 호스).
//   칸막이(골조 · 문) · 표지판(이름) · 운동 기구 짜기 · 선실 꾸미기(소품).
// 뒤끝: 같이 든 사람은 가까워지고, 반대했는데 진 사람은 헷갈릴 때마다 투덜댄다. 새 땀방엔 몸이 굳은 사람이 운동하러 간다.

public enum RoomPlanKind { Convert, Move, MoveEngine, Split, Merge, Rename, Decorate }
public enum RoomTaskKind { Wall, Unwall, Disconnect, Haul, Reconnect, Assemble, Sign, Decorate }

/// <summary>승무원이 낸 방 안건 하나.</summary>
public sealed class RoomPlan
{
    public int Id { get; init; }
    public RoomPlanKind Kind { get; init; }
    public int Proposer { get; init; } = -1;
    public int RoomId { get; init; } = -1;
    public int FurnitureId { get; init; } = -1;
    public int TargetRoomId { get; set; } = -1;
    public RoomType? NewUse { get; init; }
    public string? NewName { get; set; }
    /// <summary>이름의 출처: 사람 · 사건 · 쓰임 · 소품.</summary>
    public string NameSource { get; init; } = "";
    /// <summary>이름의 주인 (사람 이름이면).</summary>
    public int NamedFor { get; init; } = -1;
    public string Title { get; init; } = "";
    public string Why { get; init; } = "";
    /// <summary>불편 · 겪은 일 · 인원 변화 · 개인 목표 · 쓰임.</summary>
    public string Source { get; init; } = "";
    public string? Prop { get; init; }
    public long Proposed { get; init; }
    /// <summary>제안 · 공사 · 완료 · 부결 · 중단.</summary>
    public string State { get; set; } = "제안";
    public long Decided { get; set; } = -1;
    public long Done { get; set; } = -1;
    public List<int> For { get; } = new();
    public List<int> Against { get; } = new();
    // ── 주 컴퓨터의 조언 ──
    public float Risk { get; set; }
    public float Hours { get; set; }
    public string Stops { get; set; } = "";
    public string Advice { get; set; } = "";
    public int Sign { get; set; }
    public List<string> Steps { get; } = new();
    public int CardId { get; set; } = -1;
    /// <summary>컴퓨터가 낸 공사 순서를 받았다 (사고 위험이 준다).</summary>
    public bool OrderKept { get; set; }
    public int NewRoomId { get; set; } = -1;
    public int Accidents { get; set; }
    public List<Cell> Hatches { get; } = new();
    public string Key => $"{Kind}:{RoomId}:{FurnitureId}:{NewName}";
}

/// <summary>공사 한 단계 (앞 단계가 끝나야 시작).</summary>
public sealed class RoomTask
{
    public int Id { get; init; }
    public int PlanId { get; init; }
    public RoomTaskKind Kind { get; init; }
    public int Step { get; init; }
    public int FurnitureId { get; init; } = -1;
    public int RoomId { get; set; } = -1;
    public Cell Spot { get; set; }
    public int ToRoom { get; set; } = -1;
    public List<Cell> ToCells { get; } = new();
    public int W { get; init; } = 1;
    public int H { get; init; } = 1;
    public float Hours { get; init; }
    public Skill Skill { get; init; } = Skill.Mechanics;
    public float Progress { get; set; }
    public int Need { get; set; } = 1;
    public int Leader { get; set; } = -1;
    public int Helper { get; set; } = -1;
    public bool Cart { get; set; }
    public bool Lifted { get; set; }
    /// <summary>나르다 끊겨 내려놓은 자리 (상자로 보인다).</summary>
    public Cell? SetDown { get; set; }
    public Cell FromCell { get; set; }
    /// <summary>칸막이 줄 (골조가 진척만큼 차오른다 — 화면).</summary>
    public List<Cell> Line { get; } = new();
    public int FromRoom { get; set; } = -1;
    public bool Done { get; set; }
    public long Since { get; set; } = -1;
    public float Weight { get; init; }
    public string? Label { get; init; }
}

public sealed class RoomPlanStats
{
    public int Proposed, Passed, Rejected, Finished, Stopped, Moves, CoCarry, CartHauls, SoloLifts, SoloHurt, Walls, Unwalls, Signs, Decorated,
        Assembled, Advices, Cards, Accidents, Workouts, SetDowns, Disconnects, Reconnects;
}

public sealed class RoomPlanSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6007 + 389));
    public List<RoomPlan> Plans { get; } = new();
    public List<RoomTask> Tasks { get; } = new();
    public RoomPlanStats Stats { get; } = new();
    private int _nextPlan = 1, _nextTask = 1;
    private long _nextThink = SimTime.Hours(2), _lastProposal = -SimTime.TicksPerDay;
    private readonly Dictionary<string, long> _cool = new();
    private readonly Dictionary<int, long> _lastWorkout = new();
    private readonly Dictionary<int, int> _doing = new(); // 사람 → 맡은 공사 단계 (-2: 운동)
    private readonly List<int> _parked = new();
    /// <summary>운반 카트(손수레) — 배에 하나. 정비실 구석에 세워 둔다.</summary>
    public Cell? CartHome { get; private set; }
    public int CartUser { get; private set; } = -1;
    /// <summary>시험용: 안건을 저절로 내지 않는다.</summary>
    public static bool ThinkOff { get; set; }

    public const float Heavy = 70f, VeryHeavy = 150f;

    public RoomPlanSystem(World w) => _w = w;

    private CrewMember? Crew(int id) => id >= 0 && id < _w.Crew.Count ? _w.Crew[id] : null;
    private Furniture? Furn(int id) => id >= 0 && id < _w.Ship.Furniture.Count ? _w.Ship.Furniture[id] : null;
    public RoomPlan? PlanOf(RoomTask t) { foreach (var p in Plans) if (p.Id == t.PlanId) return p; return null; }
    public RoomTask? TaskOf(CrewMember c) => _doing.TryGetValue(c.Id, out var id) && id >= 0 ? Tasks.FirstOrDefault(t => t.Id == id) : null;
    public bool WorkingOut(CrewMember c) => _doing.TryGetValue(c.Id, out var id) && id == -2;
    /// <summary>떼어 내 다시 잇기를 기다리는 설비 (화면: 늘어진 전선 · 마개 씌운 관).</summary>
    public bool Disconnected(int furniture) => _parked.Contains(furniture);

    /// <summary>그 방 이름이 어디서 왔나 (사람 · 사건 · 쓰임 · 소품 — 표지판 모양).</summary>
    public string? NameSourceOf(Room r)
    {
        if (r.CustomName == null) return null;
        for (int i = Plans.Count - 1; i >= 0; i--)
        {
            var p = Plans[i];
            if (p.State == "완료" && p.NewName == r.CustomName && (p.NewRoomId == r.Id || p.RoomId == r.Id)) return p.NameSource;
        }
        return "";
    }

    /// <summary>그 설비를 옮기기로 한 안건 (헷갈려 투덜댈 상대).</summary>
    public RoomPlan? MovedBy(Furniture f)
    {
        foreach (var t in Tasks) if (t.FurnitureId == f.Id && t.Kind == RoomTaskKind.Haul && PlanOf(t) is RoomPlan p) return p;
        return null;
    }

    // ─────────────────────────────── 무게 ───────────────────────────────

    public static float BaseWeight(FurnitureType t) => t switch
    {
        FurnitureType.Shelf => 45f, FurnitureType.Bed => 50f, FurnitureType.Cot => 15f, FurnitureType.Table => 30f, FurnitureType.Seat => 8f,
        FurnitureType.Treadmill => 85f, FurnitureType.Workbench => 110f, FurnitureType.Battery => 140f, FurnitureType.OxygenGenerator => 170f,
        FurnitureType.CoolantPump => 160f, FurnitureType.AuxGenerator => 220f, FurnitureType.WaterRecycler => 190f, FurnitureType.MedBed => 75f,
        FurnitureType.Stove => 80f, FurnitureType.Fridge => 95f, FurnitureType.GrowBed => 140f, FurnitureType.SuitLocker => 60f,
        FurnitureType.Refinery => 200f, FurnitureType.Fabricator => 120f, FurnitureType.Console => 35f, _ => 60f,
    };

    public static float Weight(Furniture f) => BaseWeight(f.Type) * MathF.Max(1f, f.Cells.Count / 2f) + 0.6f * (f.Storage?.Total ?? 0);

    /// <summary>옮길 수 있나 (원자로 · 엔진 · 주 컴퓨터 · 배전반 · 채집 장치 · 드론 거치대는 배에 박혀 있다).</summary>
    public static bool Movable(FurnitureType t) => t is not (FurnitureType.Bed or FurnitureType.Cot or FurnitureType.ReactorCore or FurnitureType.EngineCore or FurnitureType.MainComputer
        or FurnitureType.PowerPanel or FurnitureType.Collector or FurnitureType.DroneDock or FurnitureType.SensorArray or FurnitureType.GrowBed);

    /// <summary>기관 구역 설비 (배관 · 배선 · 무게 · 안전 판단이 드는 것).</summary>
    public static bool EngineGear(FurnitureType t) => t is FurnitureType.CoolantPump or FurnitureType.AuxGenerator or FurnitureType.Battery
        or FurnitureType.OxygenGenerator or FurnitureType.WaterRecycler or FurnitureType.HeatExchanger or FurnitureType.CapacitorBank;

    private static bool EngineRoom(Room r) => r.Type is RoomType.Engine or RoomType.Reactor or RoomType.Cooling or RoomType.Power or RoomType.LifeSupport;

    // ─────────────────────────────── 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick >= _nextThink)
        {
            _nextThink = w.Tick + SimTime.Hours(1);
            if (!ThinkOff) Think();
        }
        if (w.Tick % 300 == 0) Tend();
    }

    /// <summary>Power: 떼어 낸 설비는 다시 이을 때까지 돌지 않는다.</summary>
    public void Park()
    {
        foreach (var id in _parked)
            if (Furn(id) is { Machine: Machine m, Stowed: false }) m.Parked = true;
    }

    /// <summary>맡은 사람이 일을 놓았으면 풀어 준다 (들고 가던 것은 그 자리에 내려놓는다).</summary>
    private void Tend()
    {
        var w = _w;
        foreach (var t in Tasks)
        {
            if (t.Done) continue;
            if (t.Leader >= 0 && (Crew(t.Leader) is not CrewMember c || c.Dead || !_doing.TryGetValue(c.Id, out var id) || id != t.Id)) Release(t, Crew(t.Leader), leader: true);
            if (t.Helper >= 0 && (Crew(t.Helper) is not CrewMember h || h.Dead || !_doing.TryGetValue(h.Id, out var hid) || hid != t.Id)) t.Helper = -1;
        }
        if (CartUser >= 0 && (Crew(CartUser) is not CrewMember cu || cu.Dead || !_doing.ContainsKey(cu.Id))) CartUser = -1;
    }

    private void Release(RoomTask t, CrewMember? c, bool leader)
    {
        var w = _w;
        if (leader)
        {
            if (t.Lifted && c != null && !t.Done)
            {
                t.Lifted = false;
                t.SetDown = w.Ship.IsWalkable(c.Cell) ? c.Cell : (Cell?)FreeNear(c.Cell) ?? c.Cell;
                Stats.SetDowns++;
                if (Furn(t.FurnitureId) is Furniture f)
                    w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 나르던 {Ko.EulReul(f.Label)} {w.Ship.RoomAt(t.SetDown.Value)?.Name ?? "?"}에 내려놓았다 — 다른 일이 생겼다 (나중에 이어서)", c.Id);
            }
            t.Leader = -1;
            if (c != null && CartUser == c.Id) CartUser = -1;
            t.Cart = false;
        }
        else t.Helper = -1;
    }

    /// <summary>일이 끝났다 (성공이든 끊김이든).</summary>
    internal void Finished(CrewMember c, ToilStatus st)
    {
        if (!_doing.TryGetValue(c.Id, out var id)) return;
        _doing.Remove(c.Id);
        if (id < 0) return;
        var t = Tasks.FirstOrDefault(x => x.Id == id);
        if (t == null) return;
        if (t.Leader == c.Id) Release(t, c, leader: true);
        if (t.Helper == c.Id) t.Helper = -1;
    }

    private Cell? FreeNear(Cell at)
    {
        var ship = _w.Ship;
        foreach (var d in Cell.Dirs8) if (ship.IsWalkable(at + d)) return at + d;
        return null;
    }

    // ─────────────────────────────── 제안 ───────────────────────────────

    private List<CrewMember> Adults() => _w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Away).ToList();

    private void Think()
    {
        var w = _w;
        if (Crisis.Level(w) >= CrisisLevel.Emergency || w.Command.Active) return;
        if (Plans.Count(p => p.State is "제안" or "공사") >= 2) return;
        if (w.Tick - _lastProposal < SimTime.Hours(6)) return;
        var cands = new List<RoomPlan>();
        GymWish(cands);
        EngineMove(cands);
        Renames(cands);
        Decorations(cands);
        Headcount(cands);
        RoomPlan? pick = null;
        float best = 0.4f;
        foreach (var p in cands)
        {
            if (_cool.TryGetValue(p.Key, out var until) && w.Tick < until) continue;
            if (Plans.Any(x => x.Key == p.Key && x.State is "제안" or "공사" or "완료")) continue;
            float s = Score(p);
            if (s > best) { best = s; pick = p; }
        }
        if (pick != null) Propose(pick);
    }

    private float Score(RoomPlan p) => p.Source switch
    {
        "불편" => 0.75f,
        "겪은 일" => 0.7f,
        "인원 변화" => 0.6f,
        "쓰임" => 0.55f,
        "사람" => 0.55f,
        "개인 목표" => 0.5f,
        _ => 0.45f,
    } + (p.Kind == RoomPlanKind.Convert ? 0.1f : 0f);

    private RoomPlan New(RoomPlanKind kind, CrewMember by, Room room, string title, string why, string source, int furniture = -1, int target = -1,
        RoomType? use = null, string? name = null, string nameSource = "", int namedFor = -1, string? prop = null) => new()
    {
        Id = 0, Kind = kind, Proposer = by.Id, RoomId = room.Id, FurnitureId = furniture, TargetRoomId = target, NewUse = use, NewName = name,
        NameSource = nameSource, NamedFor = namedFor, Title = title, Why = why, Source = source, Prop = prop, Proposed = _w.Tick,
    };

    /// <summary>안건을 올린다 (컴퓨터가 조언을 붙인다 → 다음 정기 회의).</summary>
    public RoomPlan Propose(RoomPlan draft)
    {
        var w = _w;
        var p = new RoomPlan
        {
            Id = _nextPlan++, Kind = draft.Kind, Proposer = draft.Proposer, RoomId = draft.RoomId, FurnitureId = draft.FurnitureId, TargetRoomId = draft.TargetRoomId,
            NewUse = draft.NewUse, NewName = draft.NewName, NameSource = draft.NameSource, NamedFor = draft.NamedFor, Title = draft.Title, Why = draft.Why,
            Source = draft.Source, Prop = draft.Prop, Proposed = w.Tick,
        };
        Plans.Add(p);
        if (Plans.Count > 40) Plans.RemoveAt(0);
        _lastProposal = w.Tick;
        Stats.Proposed++;
        Advise(p);
        var by = Crew(p.Proposer);
        if (by != null)
        {
            w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(by.Name)} 안건을 냈다: {p.Title} — {p.Why} ({p.Source})" + (p.Advice != "" ? $" · 주 컴퓨터: {p.Advice}" : ""), by.Id);
            by.Say(w, Persona.Say(by, Pitch(p)));
            Life.Diary(w, by, Persona.Say(by, $"회의에 안건을 냈다 — {p.Title}. {p.Why}."));
        }
        return p;
    }

    // ── 불편: 몸이 굳었다 → 창고 절반을 운동실로 ──
    private void GymWish(List<RoomPlan> cands)
    {
        var w = _w;
        var adults = Adults();
        if (adults.Count < 2) return;
        float fit = adults.Average(c => c.Fitness);
        if (fit >= 0.45f || Facilities.Best(w.Ship, "exercise").factor >= 1f) return;
        if (GymPlan(adults.OrderByDescending(c => c.Habits.Contains(Habit.GymRat) ? 1 : 0).ThenBy(c => c.Fitness).ThenBy(c => c.Id).First(), fit) is RoomPlan p) cands.Add(p);
    }

    /// <summary>창고 절반을 운동실로 ("땀방"). 나눌 수 있는 창고가 없으면 null.</summary>
    public RoomPlan? GymPlan(CrewMember by, float fit)
    {
        var w = _w;
        var store = w.Ship.Rooms.Where(r => !r.Detached && !r.Abandoned && !r.OffLimits && RoomUseSystem.Actual(r) == RoomType.Storage && r.Special == null
                                            && r.Furniture.Any(f => f.Type == FurnitureType.Shelf) && Remodel.FindSplit(w, r, 2) != null)
            .OrderByDescending(r => r.Cells.Count).ThenBy(r => r.Id).FirstOrDefault();
        if (store == null) return null;
        string name = GymName(by);
        return New(RoomPlanKind.Convert, by, store, $"{store.Name} 절반을 운동실로 — '{name}'",
            $"다들 몸이 굳었다 (체력 {fit * 100:0}%) · 운동할 곳이 없다", "불편", use: RoomType.Gym, name: name, nameSource: "쓰임");
    }

    private string GymName(CrewMember by) => _w.Ship.Rooms.Any(r => r.CustomName == "땀방") ? (by.Habits.Contains(Habit.Joker) ? "근육 공장" : "숨찬 방") : "땀방";

    // ── 겪은 일: 뚫리거나 불난 기관실의 설비를 안쪽으로 ──
    private void EngineMove(List<RoomPlan> cands)
    {
        var w = _w;
        var adults = Adults();
        var by = adults.Where(c => c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician)
            .OrderByDescending(c => c.SkillLevel(Skill.Engineering) + c.SkillLevel(Skill.Electrical)).ThenBy(c => c.Id).FirstOrDefault();
        if (by == null) return;
        foreach (var room in w.Ship.Rooms)
        {
            if (room.Detached || room.Abandoned || !EngineRoom(room)) continue;
            int hits = w.History.BreachesByRoom.GetValueOrDefault(room.Id) + room.Fires;
            if (hits < 1 || Remodel2.Interior(w, room)) continue;
            var gear = room.Furniture.Where(f => !f.Stowed && EngineGear(f.Type) && Movable(f.Type)).OrderBy(f => f.Id).FirstOrDefault();
            if (gear == null) continue;
            var dest = w.Ship.Rooms.Where(r => r != room && !r.Detached && !r.Abandoned && !r.OffLimits && r.Type != RoomType.Corridor
                                               && (EngineRoom(r) || r.Type is RoomType.Storage or RoomType.Workshop) && Remodel2.Interior(w, r)
                                               && Spot(gear.Width, gear.Height, r, null) != null)
                .OrderBy(r => EngineRoom(r) ? 0 : 1).ThenBy(r => r.Id).FirstOrDefault();
            if (dest == null) continue;
            cands.Add(New(RoomPlanKind.MoveEngine, by, room, $"{Ko.EulReul(gear.Label)} {room.Name}에서 안쪽 {Ko.EuRo(dest.Name)}",
                $"{Ko.IGa(room.Name)} {hits}번 뚫리거나 불탔다 — 또 맞으면 {gear.Label}까지 잃는다", "겪은 일", gear.Id, dest.Id));
            return;
        }
    }

    /// <summary>시험 · 화면: 이 설비를 저 방으로 옮기자는 안건.</summary>
    public RoomPlan ProposeMove(CrewMember by, Furniture f, Room to, string why) =>
        Propose(New(EngineGear(f.Type) ? RoomPlanKind.MoveEngine : RoomPlanKind.Move, by, f.Room, $"{Ko.EulReul(f.Label)} {f.Room.Name}에서 {Ko.EuRo(to.Name)}", why, "겪은 일", f.Id, to.Id));

    // ── 이름: 사람 · 사건 · 소품 · 쓰임 ──
    /// <summary>'이름'으로 · '이름'로 (따옴표 안 이름의 받침을 본다).</summary>
    private static string QEuRo(string name) => $"'{name}'" + Ko.EuRo(name)[name.Length..];

    private static string Noun(RoomType use) => use switch
    {
        RoomType.Workshop => "작업장", RoomType.Hydroponics => "정원", RoomType.Galley => "부엌", RoomType.Lounge => "사랑방",
        RoomType.Medbay => "진료실", RoomType.Gym => "운동방", RoomType.Storage => "창고", RoomType.Mess => "식탁", RoomType.Quarters => "선실",
        _ => "방",
    };

    private static string Slang(RoomType use) => use switch
    {
        RoomType.Storage => "잡동사니 방", RoomType.Gym => "땀방", RoomType.Lounge => "수다방", RoomType.Quarters => "새우잠 방",
        RoomType.Workshop => "땜질방", RoomType.Hydroponics => "초록방", RoomType.Medbay => "약방", RoomType.Mess => "밥방", RoomType.Galley => "부뚜막",
        _ => "우리 방",
    };

    private void Renames(List<RoomPlan> cands)
    {
        var w = _w;
        var adults = Adults();
        if (adults.Count < 2) return;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached || r.Abandoned || r.OffLimits || r.CustomName != null || RoomUseSystem.Actual(r) is not RoomType use) continue;
            // 사건: 사람이 떠난 방 → 그 사람의 방 · 사고를 함께 넘긴 방 → 그날의 ○○
            var dead = w.Crew.Where(c => c.Dead && c.Room == r).OrderBy(c => c.Id).FirstOrDefault();
            if (dead != null)
            {
                var friend = adults.OrderByDescending(c => c.AffinityTo(dead)).ThenBy(c => c.Id).First();
                cands.Add(New(RoomPlanKind.Rename, friend, r, $"{Ko.EulReul(r.Name)} {QEuRo($"{dead.Name}의 {Noun(use)}")}", $"{Ko.IGa(dead.Name)} 여기서 떠났다 — 잊지 않으려고",
                    "겪은 일", name: $"{dead.Name}의 {Noun(use)}", nameSource: "사람", namedFor: dead.Id));
                continue;
            }
            if (r.Fires + r.Breaches >= 1 && r.Deaths == 0 && use is RoomType.Mess or RoomType.Galley or RoomType.Lounge or RoomType.Workshop or RoomType.Hydroponics or RoomType.Quarters)
            {
                var vet = adults.Where(c => c.Memory.FearOf(r) < 0.35f).OrderByDescending(c => c.Memory.FearOf(r)).ThenBy(c => c.Id).FirstOrDefault();
                if (vet != null)
                    cands.Add(New(RoomPlanKind.Rename, vet, r, $"{Ko.EulReul(r.Name)} {QEuRo($"그날의 {RoomTypes.Name(use)}")}", $"{Ko.IGa(r.Name)} {(r.Fires > 0 ? "불" : "구멍")}을 함께 넘겼다 — 그날을 기억하자",
                        "겪은 일", name: $"그날의 {RoomTypes.Name(use)}", nameSource: "사건"));
                continue;
            }
            // 소품: 어항이 있는 방
            if (r.Furniture.Any(f => f.Type == FurnitureType.Aquarium && !f.Stowed))
            {
                var fan = adults.OrderByDescending(c => c.Habits.Contains(Habit.Cheerful) ? 1 : 0).ThenByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).First();
                cands.Add(New(RoomPlanKind.Rename, fan, r, $"{Ko.EulReul(r.Name)} {QEuRo("물고기 방")}", "다들 어항 보러 간다", "쓰임", name: "물고기 방", nameSource: "소품"));
                continue;
            }
            // 쓰임: 설계와 다르게 하루 넘게 쓰인 방 → 다들 부르는 이름
            if (r.UsedAs is RoomType u && r.FormerPurposes.Count > 0)
            {
                var by = adults.OrderByDescending(c => w.RoomUse.Who(r, c)).ThenBy(c => c.Id).First();
                cands.Add(New(RoomPlanKind.Rename, by, r, $"{Ko.EulReul(r.Name)} {QEuRo(Slang(u))}", $"설계는 {RoomTypes.Name(r.Kind)}지만 다들 {Ko.EuRo(RoomUseSystem.UseName(u))} 쓴다",
                    "쓰임", name: Slang(u), nameSource: "쓰임"));
                continue;
            }
            // 사람: 한 사람이 도맡은 방 → "서지안의 작업장"
            if (use is RoomType.Workshop or RoomType.Hydroponics or RoomType.Galley or RoomType.Medbay or RoomType.Lounge or RoomType.Gym
                && w.RoomUse.Owner(r) is var (owner, hours, share) && hours >= 10f && share >= 0.45f && !owner.IsChild)
            {
                var friend = adults.Where(c => c != owner).OrderByDescending(c => c.AffinityTo(owner)).ThenBy(c => c.Id).FirstOrDefault();
                if (friend == null || friend.AffinityTo(owner) < 0f) continue;
                cands.Add(New(RoomPlanKind.Rename, friend, r, $"{Ko.EulReul(r.Name)} {QEuRo($"{owner.Name}의 {Noun(use)}")}", $"{Ko.IGa(owner.Name)} 여기서 산다 ({hours:0}시간 · {share * 100:0}%)",
                    "사람", name: $"{owner.Name}의 {Noun(use)}", nameSource: "사람", namedFor: owner.Id));
            }
        }
    }

    // ── 개인 목표: 내 침대 곁을 꾸민다 ──
    private void Decorations(List<RoomPlan> cands)
    {
        var w = _w;
        foreach (var c in Adults())
        {
            if (c.Bed is not Furniture bed || bed.Stowed || bed.Room.Detached) continue;
            if (!(c.Habits.Contains(Habit.Homesick) || c.Needs.Stress > 0.4f || c.Hobbies.Any(h => h is Hobby.Painting or Hobby.Photography or Hobby.Knitting))) continue;
            if (bed.Room.Decor.Any(d => d.Maker == c.Id)) continue;
            string prop = c.Hobbies.Contains(Hobby.Painting) ? "landscape" : c.Hobbies.Contains(Hobby.Knitting) ? "quilt" : c.Hobbies.Contains(Hobby.Reading) ? "readlamp" : "crewphoto";
            var spec = Props.Get(prop);
            cands.Add(New(RoomPlanKind.Decorate, c, bed.Room, $"{c.Name}의 침대 곁을 꾸미기 — {spec.Name}",
                c.Habits.Contains(Habit.Homesick) ? "집 생각이 난다 — 내 자리 같은 곳이 있으면" : "쉴 때 볼 것이 있으면 좋겠다", "개인 목표", bed.Id, prop: prop));
            return;
        }
    }

    // ── 인원 변화: 늘었으면 칸막이로 방 하나 더 · 줄었으면 빈 칸막이 걷기 ──
    private void Headcount(List<RoomPlan> cands)
    {
        var w = _w;
        var adults = Adults();
        if (adults.Count < 2) return;
        int alive = w.Crew.Count(c => !c.Dead);
        int beds = w.Ship.Furniture.Count(f => !f.Stowed && !f.Room.Detached && FurnitureTypes.Sleepable(f.Type) && f.Type != FurnitureType.MedBed);
        if (alive > w.StartCrew || alive > beds)
        {
            var q = w.Ship.Rooms.Where(r => !r.Detached && !r.Partitioned && RoomUseSystem.Actual(r) == RoomType.Quarters && Remodel.FindSplit(w, r, 2) != null)
                .OrderByDescending(r => r.Cells.Count).FirstOrDefault();
            var by = adults.OrderByDescending(c => c.Bed?.Room == q ? 1 : 0).ThenBy(c => c.Traits.Sociability).ThenBy(c => c.Id).First();
            if (q != null) cands.Add(New(RoomPlanKind.Split, by, q, $"{q.Name}에 칸막이 — 선실을 하나 더", $"사람이 늘었다 ({w.StartCrew}명 → {alive}명) · 좁다", "인원 변화"));
        }
        else if (alive < w.StartCrew)
        {
            var inner = w.Ship.Rooms.FirstOrDefault(r => r.SplitFrom != null && !r.Merged && !r.Detached && Remodel2.CanMerge(w, r)
                                                        && !w.Crew.Any(c => !c.Dead && c.Room == r) && w.RoomUse.Dwell(r).Sum() < 3f);
            var by = adults.OrderByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).First();
            if (inner != null) cands.Add(New(RoomPlanKind.Merge, by, inner, $"{inner.Name} 칸막이 걷기", $"사람이 줄었다 ({w.StartCrew}명 → {alive}명) · 빈 칸이 쓸쓸하다", "인원 변화"));
        }
    }

    // ─────────────────────────────── 주 컴퓨터의 조언 ───────────────────────────────

    private static float TypeRisk(FurnitureType t) => t switch
    {
        FurnitureType.CoolantPump => 0.38f, FurnitureType.AuxGenerator => 0.3f, FurnitureType.Battery => 0.28f, FurnitureType.OxygenGenerator => 0.3f,
        FurnitureType.WaterRecycler => 0.22f, FurnitureType.HeatExchanger => 0.25f, FurnitureType.CapacitorBank => 0.25f, _ => 0.06f,
    };

    private string StopsOf(Furniture f)
    {
        var w = _w;
        return f.Type switch
        {
            FurnitureType.CoolantPump => $"냉각 펌프 하나 (남은 {Math.Max(0, w.Ship.FurnitureOf(FurnitureType.CoolantPump).Count() - 1)}개로 원자로를 식힌다 — 출력을 낮춰야)",
            FurnitureType.Battery => $"비상 전력 {PowerGrid.BatteryKwh(f):0}kWh",
            FurnitureType.OxygenGenerator => $"산소 생산 {100f / Math.Max(1, w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Count()):0}%",
            FurnitureType.WaterRecycler => "물 재생 한 줄",
            FurnitureType.AuxGenerator => "보조 발전 (원자로가 멈추면 기댈 곳이 없다)",
            FurnitureType.Treadmill => "없음",
            _ => f.Machine != null ? $"{f.Label}" : "없음",
        };
    }

    /// <summary>위험 · 공사 기간 · 멈추는 계통 · 공사 순서 → 다섯 칸 기록 (기관 설비는 제안 카드).</summary>
    private void Advise(RoomPlan p)
    {
        var w = _w;
        var room = w.Ship.Rooms[p.RoomId];
        var f = Furn(p.FurnitureId);
        var crew = Adults();
        float skill = crew.Count == 0 ? 0f : crew.Max(c => c.SkillLevel(Skill.Engineering) + c.SkillLevel(Skill.Mechanics)) * 0.5f;
        switch (p.Kind)
        {
            case RoomPlanKind.Convert:
            {
                int shelves = room.Furniture.Count(x => x.Type == FurnitureType.Shelf) / 2;
                bool mill = w.Ship.FurnitureOf(FurnitureType.Treadmill).Any();
                p.Risk = 0.08f;
                p.Hours = 1.6f + 0.5f * shelves + (mill ? 1.2f : 1.4f) + 0.3f;
                p.Stops = $"{room.Name} 절반 (선반 {shelves}개쯤 다른 곳으로 — 한동안 물건 찾기 헷갈린다)";
                p.Steps.AddRange(new[] { "칸막이 골조 · 문", "선반은 둘이 들거나 카트로", mill ? "운동 기구를 떼어 옮기고 다시 잇는다" : "운동 기구를 짠다 (모터 · 벨트)", "표지판" });
                p.Sign = 1;
                break;
            }
            case RoomPlanKind.Move or RoomPlanKind.MoveEngine when f != null:
            {
                var to = p.TargetRoomId >= 0 ? w.Ship.Rooms[p.TargetRoomId] : null;
                float wt = Weight(f);
                bool plumbed = Procedures.Plumbed(f.Type);
                var o = w.Origin;
                p.Risk = Math.Clamp(TypeRisk(f.Type) + (plumbed ? 0.1f : 0f) + (wt > VeryHeavy ? 0.08f : 0f) + (room.Type == RoomType.Reactor ? 0.12f : 0f)
                                    + o.Designer switch { ShipDesigner.Military => -0.05f, ShipDesigner.Settler => 0.04f, _ => 0.05f }
                                    + (o.Start is ShipStart.Junk or ShipStart.Derelict ? 0.06f : 0f) - 0.12f * skill, 0.03f, 0.95f);
                float dist = to != null ? (to.Center - room.Center).Length() : 10f;
                p.Hours = (plumbed ? 0.9f : 0.5f) + dist / 25f + (plumbed ? 1.1f : 0.7f) * (o.Designer == ShipDesigner.Settler ? 0.8f : 1f);
                p.Stops = StopsOf(f);
                p.Steps.Add(f.Type == FurnitureType.CoolantPump ? "원자로 출력을 낮추고 부하를 다른 펌프로" : $"{f.Label}의 부하를 다른 쪽으로 넘긴다");
                p.Steps.Add(plumbed ? "분전함 차단 · 밸브 잠금 · 배관을 비운다" : "분전함 차단");
                p.Steps.Add("배선 · 배관 분리 (점검 뚜껑 · 테이프)");
                p.Steps.Add(wt >= VeryHeavy ? "둘이 카트로 옮긴다" : wt >= Heavy ? "둘이 들거나 카트로" : "들어 옮긴다");
                p.Steps.Add(plumbed ? "다시 잇고 누설 시험 · 시운전" : "다시 잇고 시운전");
                bool lesson = w.History.BreachesByRoom.GetValueOrDefault(room.Id) + room.Fires > 0;
                p.Sign = p.Risk > 0.5f ? -1 : lesson ? 1 : 0;
                break;
            }
            case RoomPlanKind.Split or RoomPlanKind.Merge:
                p.Risk = 0.05f; p.Hours = p.Kind == RoomPlanKind.Split ? 1.6f : 1f;
                p.Stops = $"공사하는 동안 {room.Name}";
                p.Steps.AddRange(p.Kind == RoomPlanKind.Split ? new[] { "골조를 세운다", "격벽 문을 단다" } : new[] { "칸막이를 뜯는다", "벽 재료를 되찾는다" });
                p.Sign = p.Kind == RoomPlanKind.Merge ? -1 : 1; // 칸막이를 걷으면 다음 운석에 통째로 감압된다
                break;
            default:
                p.Risk = 0f; p.Hours = 0.3f; p.Stops = "없음";
                p.Steps.Add(p.Kind == RoomPlanKind.Decorate ? "소품을 건다" : "표지판을 단다");
                p.Sign = 0;
                break;
        }
        string order = string.Join(" → ", p.Steps.Select((s, i) => $"{i + 1}) {s}"));
        string judge = $"위험 {p.Risk * 100:0}% · 공사 {p.Hours:0.#}시간 · 멈추는 계통: {p.Stops}" + (p.Sign < 0 ? (p.Kind == RoomPlanKind.Merge ? " — 칸막이가 감압을 반으로 막고 있다" : " — 위험이 크다") : "");
        var act = w.Automation.Book.Add(ActKind.Advice, room, $"{Crew(p.Proposer)?.Name ?? "?"}의 안건: {p.Title}", judge, "공사 순서: " + order,
            p.Sign < 0 ? "다시 생각해 주세요" : "회의에서 정해 주세요");
        if (act == null) return; // 컴퓨터가 멎었다 — 조언 없이 회의로
        Stats.Advices++;
        p.Advice = judge + (p.Sign > 0 ? " · 권한다" : p.Sign < 0 ? " · 권하지 않는다" : "") + $" · 순서: {order}";
        if (p.Kind == RoomPlanKind.MoveEngine && f != null)
        {
            var card = w.Automation.Asks.Propose("roomplan:" + p.Id, "relocate", room, $"{f.Label} 이전 — 공사 순서",
                $"위험 {p.Risk * 100:0}% · 공사 {p.Hours:0.#}시간 · 멈춤: {p.Stops}", $"순서대로 하면 위험 {p.Risk * 35:0}%까지 · {order}", 180f, null,
                (world, pr) => { p.OrderKept = true; world.Log.Add(world.Tick, LogKind.Ship, $"{f.Label} 이전 — 컴퓨터가 낸 공사 순서를 받았다"); },
                (world, pr) => p.State switch
                {
                    "완료" => p.Accidents == 0 ? (1, "맞았다 — 순서대로 사고 없이 옮겼다") : (p.OrderKept ? -1 : 1, p.OrderKept ? "틀렸다 — 순서대로 했는데도 사고가 났다" : "맞았다 — 순서를 건너뛰어 사고가 났다"),
                    "부결" or "중단" => (1, "참고 — 회의가 하지 않기로 했다"),
                    _ => null,
                });
            p.CardId = card.Id;
            Stats.Cards++;
        }
    }

    // ─────────────────────────────── 회의 ───────────────────────────────

    private string Pitch(RoomPlan p) => p.Kind switch
    {
        RoomPlanKind.Convert => $"창고 절반이면 된다 — '{p.NewName}'에서 땀 좀 흘리자",
        RoomPlanKind.MoveEngine => $"{Furn(p.FurnitureId)?.Label ?? "설비"}를 안쪽으로 옮기자 — 또 뚫리면 끝이다",
        RoomPlanKind.Move => $"{Furn(p.FurnitureId)?.Label ?? "설비"}를 옮기면 다니기 편하다",
        RoomPlanKind.Rename => $"'{p.NewName}' 어때? {p.Why}",
        RoomPlanKind.Decorate => "내 자리를 조금 꾸미고 싶다",
        RoomPlanKind.Split => "사람이 늘었다 — 칸막이로 하나 더 만들자",
        RoomPlanKind.Merge => "빈 칸막이 방, 걷어서 넓게 쓰자",
        _ => p.Title,
    };

    /// <summary>이 사람이 이 안건에 대해 처음 드는 생각 (+ 찬성).</summary>
    public (float s, string why) Opinion(CrewMember c, RoomPlan p)
    {
        var w = _w;
        if (c.Id == p.Proposer) return (0.9f, Pitch(p));
        float s = 0.05f;
        string why = "해 볼 만하다";
        var room = p.RoomId >= 0 ? w.Ship.Rooms[p.RoomId] : null;
        if (Crew(p.Proposer) is CrewMember by) s += 0.35f * c.AffinityTo(by);
        switch (p.Kind)
        {
            case RoomPlanKind.Convert:
            {
                float need = 0.5f - c.Fitness;
                s += 1.4f * need + (c.Habits.Contains(Habit.GymRat) || c.Hobbies.Any(BelongingSystem.Exercise) ? 0.3f : 0f);
                why = need > 0f ? "몸이 굳었다 — 운동할 곳이 있어야 한다" : "창고가 좁아진다";
                if (c.Habits.Contains(Habit.Hoarder)) { s -= 0.3f; why = "물건 둘 데가 줄어든다"; }
                if (c.Role is CrewRole.Technician or CrewRole.Engineer) { s -= 0.12f; if (need <= 0f) why = "선반을 옮기면 물건 찾기 힘들다"; }
                break;
            }
            case RoomPlanKind.Move or RoomPlanKind.MoveEngine:
            {
                float fear = c.Memory.FearOf(room);
                s += 0.6f * fear + 0.15f;
                why = fear > 0.2f ? $"{Ko.IGa(room?.Name ?? "그 방")} 또 뚫린다 — 안쪽이 낫다" : "옮겨 두면 안심이다";
                if (c.Value == CrewValue.Safety) { s -= 1.1f * p.Risk; if (p.Risk > 0.25f) why = $"기관 설비를 옮기다 사고 난다 (위험 {p.Risk * 100:0}%)"; }
                if (c.Role == CrewRole.Engineer && p.Risk > 0.5f) { s -= 0.2f; why = "냉각 · 배선을 떼는 건 생각보다 위험하다"; }
                break;
            }
            case RoomPlanKind.Rename:
            {
                if (p.NamedFor == c.Id)
                {
                    bool shy = c.Traits.Sociability < 0.35f;
                    s += shy ? -0.5f : 0.5f;
                    why = shy ? "내 이름을 문에 붙이다니 — 부끄럽다" : "내 이름이라니 좋다";
                }
                else if (Crew(p.NamedFor) is CrewMember named)
                {
                    s += 0.15f + 0.3f * c.AffinityTo(named);
                    why = named.Dead ? $"{Ko.EulReul(named.Name)} 잊지 말자" : $"{named.Name}의 방이 맞다";
                }
                else if (p.NameSource == "사건")
                {
                    float fear = c.Memory.FearOf(room);
                    s += fear > 0.35f ? -0.4f : 0.25f;
                    why = fear > 0.35f ? "그날을 떠올리고 싶지 않다" : "그날을 잊지 말자";
                }
                else { s += 0.2f + (c.Habits.Contains(Habit.Cheerful) || c.Habits.Contains(Habit.Joker) ? 0.15f : 0f); why = "다들 벌써 그렇게 부른다"; }
                if (c.Value == CrewValue.Rules) { s -= 0.15f; if (s <= 0f) why = "이름은 설계대로 두자"; }
                break;
            }
            case RoomPlanKind.Decorate:
            {
                bool mate = Furn(p.FurnitureId) is Furniture bed && c.Bed?.Room == bed.Room;
                s += 0.15f + (mate ? 0f : 0.05f);
                if (c.Habits.Contains(Habit.NeatFreak) && mate) { s -= 0.35f; why = "방이 어수선해진다"; }
                else why = "자기 자리는 자기 마음대로";
                break;
            }
            case RoomPlanKind.Split:
                s += c.Bed?.Room == room ? 0.35f : 0.1f;
                why = c.Bed?.Room == room ? "좁아서 잠을 설친다" : "사람이 늘었으니";
                if (c.Traits.Sociability > 0.7f) { s -= 0.2f; why = "다 같이 자는 게 좋은데"; }
                break;
            case RoomPlanKind.Merge:
                s += 0.3f * (c.Traits.Sociability - 0.4f);
                why = "넓게 쓰자";
                if (c.Value == CrewValue.Safety) { s -= 0.3f; why = "칸막이가 감압을 반으로 막는다"; }
                break;
        }
        if (p.Advice != "" && p.Sign != 0)
        {
            float trust = w.Automation.Trusts.Of(c);
            s += 0.25f * p.Sign * trust;
            if (p.Sign < 0 && trust > 0.6f && s <= 0f) why = $"컴퓨터가 위험하다고 한다 (위험 {p.Risk * 100:0}%)";
        }
        if (c.Value == CrewValue.Freedom) s += 0.05f;
        return (s, why);
    }

    private static float Expertise(CrewMember c, RoomPlan p) => p.Kind is RoomPlanKind.MoveEngine ? c.SkillLevel(Skill.Engineering) : c.SkillLevel(Skill.Mechanics) * 0.6f + 0.2f;

    /// <summary>정기 회의: 올라온 방 안건을 토론 · 표결한다 (한 번에 둘까지).</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> attendees, CrewMember chair)
    {
        var w = _w;
        var voters = attendees.Where(c => !w.Society.OnProbation(c)).ToList();
        if (voters.Count < 2) voters = attendees;
        if (voters.Count < 2) return;
        foreach (var p in Plans.Where(x => x.State == "제안").OrderBy(x => x.Id).Take(2).ToList())
        {
            if (!Valid(p)) { p.State = "중단"; Stats.Stopped++; continue; }
            var item = new AgendaItem
            {
                Title = $"방: {p.Title}", Topic = "room:" + p.Id, Evidence = $"{p.Why} ({p.Source})",
                Computer = p.Advice != "" ? $"주 컴퓨터: {p.Advice}" : null, ComputerSign = p.Sign,
            };
            var (yes, no) = w.Meetings.Debate(voters, c => Opinion(c, p), c => Expertise(c, p), item, chair);
            bool pass = yes.Count > no.Count || yes.Count == no.Count && Opinion(chair, p).s > 0f;
            item.Passed = pass;
            item.Outcome = pass ? "하기로 했다" : "하지 않기로 했다";
            rec.Items.Add(item);
            p.For.AddRange(yes.Select(c => c.Id));
            p.Against.AddRange(no.Select(c => c.Id));
            p.Decided = w.Tick;
            w.Meetings.Record(item.Title, "room", p.RoomId, chair, yes, no, p.Kind == RoomPlanKind.MoveEngine ? "기관 설비 이전" : "");
            w.Meetings.Split(yes, no);
            w.History.Add(w, HistoryKind.Decision, $"회의: {p.Title}? 찬성 {yes.Count} · 반대 {no.Count} → {(pass ? "한다" : "안 한다")}"
                + (item.FlippedBy != null ? $" ({item.FlippedBy}의 설득으로 뒤집혔다)" : "") + (p.Advice != "" ? $" · 컴퓨터 위험 {p.Risk * 100:0}%" : ""),
                p.RoomId >= 0 ? w.Ship.Rooms[p.RoomId] : null, voters, log: true);
            if (pass) { Stats.Passed++; Start(p); }
            else
            {
                p.State = "부결";
                Stats.Rejected++;
                _cool[p.Key] = w.Tick + SimTime.TicksPerDay * 3;
                if (Crew(p.Proposer) is CrewMember by) by.Needs.Stress = MathF.Min(1f, by.Needs.Stress + 0.03f);
            }
        }
    }

    private bool Valid(RoomPlan p)
    {
        var w = _w;
        if (p.RoomId < 0 || p.RoomId >= w.Ship.Rooms.Count) return false;
        var room = w.Ship.Rooms[p.RoomId];
        if (room.Detached || room.OffLimits) return false;
        if (p.FurnitureId >= 0 && Furn(p.FurnitureId) is not { Stowed: false }) return false;
        if (p.Kind == RoomPlanKind.Rename && room.CustomName != null) return false;
        return true;
    }

    // ─────────────────────────────── 공사 계획 ───────────────────────────────

    private RoomTask AddTask(RoomPlan p, RoomTaskKind kind, int step, Room room, Cell spot, float hours, Skill skill = Skill.Mechanics,
        Furniture? f = null, Room? to = null, string? label = null)
    {
        var t = new RoomTask
        {
            Id = _nextTask++, PlanId = p.Id, Kind = kind, Step = step, FurnitureId = f?.Id ?? -1, RoomId = room.Id, Spot = spot, ToRoom = to?.Id ?? -1,
            Hours = hours, Skill = skill, W = f?.Width ?? 1, H = f?.Height ?? 1, Weight = f != null ? Weight(f) : 0f, Label = label,
        };
        if (kind == RoomTaskKind.Haul) t.Need = t.Weight >= Heavy ? 2 : 1;
        Tasks.Add(t);
        return t;
    }

    /// <summary>회의가 정했다: 공사 단계를 짠다.</summary>
    public void Start(RoomPlan p)
    {
        var w = _w;
        var room = w.Ship.Rooms[p.RoomId];
        p.State = "공사";
        var f = Furn(p.FurnitureId);
        switch (p.Kind)
        {
            case RoomPlanKind.Convert or RoomPlanKind.Split:
            {
                if (Remodel.FindSplit(w, room, 2) is not Remodel.SplitPlan sp) { Stop(p, "칸막이 칠 자리가 없어졌다"); return; }
                var spot = Cell.Dirs4.Select(d => sp.Door + d).FirstOrDefault(c => w.Ship.IsOpenFloor(c) && w.Ship.RoomAt(c) == room);
                AddTask(p, RoomTaskKind.Wall, 0, room, spot, 1.6f).Line.AddRange(sp.Wall);
                break;
            }
            case RoomPlanKind.Merge:
            {
                var spot = room.SplitDoor != null ? Cell.Dirs4.Select(d => room.SplitDoor.Cell + d).FirstOrDefault(c => w.Ship.IsOpenFloor(c)) : room.Cells.First(w.Ship.IsOpenFloor);
                AddTask(p, RoomTaskKind.Unwall, 0, room, spot, 1f);
                break;
            }
            case RoomPlanKind.Move or RoomPlanKind.MoveEngine when f != null:
            {
                var to = w.Ship.Rooms[p.TargetRoomId];
                var spot = f.UseSpots.FirstOrDefault();
                bool machine = f.Machine != null || Procedures.Plumbed(f.Type);
                if (machine) AddTask(p, RoomTaskKind.Disconnect, 0, f.Room, spot, Procedures.Plumbed(f.Type) ? 0.9f : 0.5f, Skill.Electrical, f);
                var haul = AddTask(p, RoomTaskKind.Haul, 1, f.Room, spot, 0f, Skill.Mechanics, f, to);
                if (Spot(f.Width, f.Height, to, Reserved()) is List<Cell> cells) haul.ToCells.AddRange(cells);
                if (machine) AddTask(p, RoomTaskKind.Reconnect, 2, to, default, Procedures.Plumbed(f.Type) ? 1.1f : 0.6f, Skill.Electrical, f, to);
                break;
            }
            case RoomPlanKind.Rename:
                AddTask(p, RoomTaskKind.Sign, 0, room, SignSpot(room), 0.25f, label: p.NewName);
                break;
            case RoomPlanKind.Decorate when f != null:
                AddTask(p, RoomTaskKind.Decorate, 0, room, f.UseSpots.FirstOrDefault(), 0.3f, Skill.Mechanics, f, label: p.Prop);
                break;
            default:
                Stop(p, "대상이 없어졌다");
                return;
        }
        w.Log.Add(w.Tick, LogKind.Ship, $"방 공사 시작: {p.Title} — {Tasks.Count(t => t.PlanId == p.Id)}단계" + (p.Advice != "" ? $" (컴퓨터 예상 {p.Hours:0.#}시간)" : ""));
    }

    private void Stop(RoomPlan p, string why)
    {
        var w = _w;
        p.State = "중단";
        Stats.Stopped++;
        foreach (var t in Tasks.Where(t => t.PlanId == p.Id && !t.Done).ToList())
        {
            if (t.Lifted || t.SetDown != null) PutBack(t);
            t.Done = true;
        }
        Unpark(p);
        w.Log.Add(w.Tick, LogKind.Ship, $"방 공사 중단: {p.Title} — {why}");
    }

    /// <summary>문 곁 방 안쪽 칸 (표지판 다는 자리).</summary>
    private Cell SignSpot(Room room)
    {
        var ship = _w.Ship;
        foreach (var d in room.Doors.Where(d => !d.IsExternal && !d.Removed).OrderBy(d => d.Id))
            foreach (var n in Cell.Dirs4)
                if (ship.RoomAt(d.Cell + n) == room && ship.IsWalkable(d.Cell + n)) return d.Cell + n;
        return room.Cells.FirstOrDefault(ship.IsWalkable);
    }

    private HashSet<Cell> Reserved()
    {
        var set = new HashSet<Cell>();
        foreach (var t in Tasks) if (!t.Done) foreach (var c in t.ToCells) set.Add(c);
        return set;
    }

    /// <summary>그 방에서 w×h 설비를 놓을 자리 (벽에 붙고 · 문 앞 · 다른 가구 쓰는 자리를 막지 않고 · 길을 끊지 않는다).</summary>
    public List<Cell>? Spot(int wdt, int hgt, Room room, HashSet<Cell>? avoid)
    {
        var w = _w;
        var ship = w.Ship;
        foreach (var origin in room.Cells.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            var cells = new List<Cell>(wdt * hgt);
            bool ok = true;
            for (int dy = 0; dy < hgt && ok; dy++)
                for (int dx = 0; dx < wdt && ok; dx++)
                {
                    var c = origin + new Cell(dx, dy);
                    if (ship.RoomAt(c) != room || !ship.IsOpenFloor(c) || c == room.DamperSpot || avoid != null && avoid.Contains(c) || w.Body.HatchOpenAt(c)) ok = false;
                    else cells.Add(c);
                }
            if (!ok) continue;
            if (!cells.Any(c => Cell.Dirs4.Any(d => ship.Grid.Kind(c + d) == TileKind.Wall))) continue;
            if (room.Furniture.Any(o => o.UseSpots.Any(cells.Contains))) continue;
            if (room.Doors.Any(d => cells.Any(c => (c - d.Cell).X * (c - d.Cell).X + (c - d.Cell).Y * (c - d.Cell).Y <= 2))) continue;
            if (!cells.Any(c => Cell.Dirs4.Any(d => !cells.Contains(c + d) && ship.IsOpenFloor(c + d) && ship.RoomAt(c + d) == room))) continue;
            if (!Adaptation.SafeToBlock(w, room, cells)) continue;
            return cells;
        }
        return null;
    }

    // ─────────────────────────────── 일 고르기 (활동) ───────────────────────────────

    private int CurrentStep(RoomPlan p)
    {
        int s = int.MaxValue;
        foreach (var t in Tasks) if (t.PlanId == p.Id && !t.Done && t.Step < s) s = t.Step;
        return s;
    }

    /// <summary>들 칸 (내려놓은 상자 곁 · 설비 쓰는 자리).</summary>
    private Cell? Pickup(RoomTask t, DistanceField dist, Cell? not = null)
    {
        var w = _w;
        if (t.SetDown is Cell sd)
        {
            if (w.Ship.IsWalkable(sd) && dist.Reachable(sd) && sd != not) return sd;
            foreach (var d in Cell.Dirs8) if (w.Ship.IsWalkable(sd + d) && dist.Reachable(sd + d) && sd + d != not) return sd + d;
            return null;
        }
        if (Furn(t.FurnitureId) is not Furniture f) return null;
        Cell? best = null; int bd = int.MaxValue;
        foreach (var s in f.UseSpots) { int d = dist.Get(s); if (d >= 0 && d < bd && s != not) { bd = d; best = s; } }
        if (best == null)
            foreach (var c in f.Cells) foreach (var d4 in Cell.Dirs4)
            {
                var n = c + d4;
                if (!w.Ship.IsWalkable(n) || n == not || f.Cells.Contains(n)) continue;
                int d = dist.Get(n);
                if (d >= 0 && d < bd) { bd = d; best = n; }
            }
        return best;
    }

    /// <summary>놓을 자리 곁 칸.</summary>
    private Cell? DropSpot(RoomTask t, Cell? not = null)
    {
        var ship = _w.Ship;
        foreach (var c in t.ToCells)
            foreach (var d in Cell.Dirs4)
            {
                var n = c + d;
                if (!t.ToCells.Contains(n) && ship.IsWalkable(n) && n != not) return n;
            }
        return null;
    }

    public sealed record Choice(RoomTask? Task, bool Helper, bool Workout, float Score, string Why);

    /// <summary>이 사람이 지금 맡을 공사 단계 (또는 땀방 운동).</summary>
    public Choice? Choose(CrewMember c, DistanceField dist)
    {
        var w = _w;
        Choice? best = null;
        bool shift = SimTime.InWindow(SimTime.HourOfDay(w.Tick), c.Schedule.WorkStart, c.Schedule.WorkLength);
        bool tired = c.Needs.Rest < 0.22f || c.Needs.Stress > 0.75f;
        foreach (var p in Plans)
        {
            if (p.State != "공사") continue;
            int step = CurrentStep(p);
            foreach (var t in Tasks)
            {
                if (t.PlanId != p.Id || t.Done || t.Step != step) continue;
                bool helper = false;
                if (t.Leader >= 0)
                {
                    bool canHelp = t.Kind == RoomTaskKind.Haul && t.Need >= 2 && !t.Cart && t.Helper < 0
                                   || t.Kind is RoomTaskKind.Wall or RoomTaskKind.Assemble && t.Helper < 0;
                    if (!canHelp || t.Leader == c.Id) continue;
                    helper = true;
                }
                if (t.Kind == RoomTaskKind.Haul && c.Vitals.Injury > 0.35f) continue;
                if (t.Kind is RoomTaskKind.Disconnect or RoomTaskKind.Reconnect && p.Kind == RoomPlanKind.MoveEngine && c.SkillLevel(Skill.Electrical) + c.SkillLevel(Skill.Engineering) < 0.5f) continue;
                if (t.Kind == RoomTaskKind.Decorate && c.Id != p.Proposer) continue;
                if (AtFor(t, helper, dist) is not Cell spot) continue;
                float s = shift ? 0.38f + 0.14f * c.Traits.Diligence : 0.12f + 0.1f * c.Traits.Diligence;
                if (p.Proposer == c.Id) s += 0.12f;
                else if (p.For.Contains(c.Id)) s += 0.06f;
                else if (p.Against.Contains(c.Id)) s -= 0.08f;
                if (t.Kind is RoomTaskKind.Disconnect or RoomTaskKind.Reconnect or RoomTaskKind.Wall && c.Role is CrewRole.Technician or CrewRole.Engineer or CrewRole.Electrician) s += 0.08f;
                if (helper) s += 0.16f; // 누가 기다린다
                if (t.Kind == RoomTaskKind.Decorate) s = 0.3f + 0.2f * c.Needs.Stress;
                s -= 0.25f * c.Needs.Stress + (tired ? 0.25f : 0f);
                s -= 0.002f * MathF.Min(100, dist.Get(spot));
                string why = helper ? (t.Kind == RoomTaskKind.Haul ? $"{Crew(t.Leader)?.Name ?? "누가"} 같이 들어 달란다 — {Furn(t.FurnitureId)?.Label}" : $"{p.Title} — 거든다")
                    : $"{p.Title} — {TaskName(t)}";
                if (best == null || s > best.Score) best = new Choice(t, helper, false, s, why);
            }
        }
        if (WorkoutRoom(c) is (Room gym, Furniture _) && !shift)
        {
            float s = 0.3f + 0.8f * (0.6f - c.Fitness) + (c.Habits.Contains(Habit.GymRat) ? 0.12f : 0f) - (tired ? 0.3f : 0f);
            if (best == null || s > best.Score) best = new Choice(null, false, true, s, $"{gym.Name}에서 운동 (체력 {c.Fitness * 100:0}%)");
        }
        return best;
    }

    private Cell? AtFor(RoomTask t, bool helper, DistanceField dist)
    {
        if (t.Kind == RoomTaskKind.Haul) return helper && Crew(t.Leader) is CrewMember ld ? Pickup(t, dist, ld.Destination ?? ld.Cell) : Pickup(t, dist);
        if (t.Kind is RoomTaskKind.Reconnect or RoomTaskKind.Disconnect)
        {
            if (Furn(t.FurnitureId) is not Furniture f || f.Stowed) return null;
            foreach (var s in f.UseSpots) if (dist.Reachable(s)) return s;
            return null;
        }
        if (t.Kind == RoomTaskKind.Assemble && (!_w.Ship.IsOpenFloor(t.Spot) || _w.Ship.RoomAt(t.Spot)?.Id != t.RoomId) && AssembleSpot(_w.Ship.Rooms[t.RoomId]) is Cell a2) t.Spot = a2;
        return dist.Reachable(t.Spot) ? t.Spot : null;
    }

    /// <summary>운동 기구를 짤 칸 (벽에 붙고 · 문 앞이 아니고 · 길을 끊지 않는 곳 — 문에서 먼 곳부터).</summary>
    private Cell? AssembleSpot(Room gym)
    {
        var w = _w;
        var door = gym.Doors.FirstOrDefault(d => !d.Removed && !d.IsExternal)?.Cell ?? gym.Cells[0];
        foreach (var c in gym.Cells.OrderByDescending(c => (c.Center - door.Center).LengthSquared()).ThenBy(c => c.Y).ThenBy(c => c.X))
        {
            if (!w.Ship.IsOpenFloor(c) || c == gym.DamperSpot || w.Body.HatchOpenAt(c)) continue;
            if (!Cell.Dirs4.Any(d => w.Ship.Grid.Kind(c + d) == TileKind.Wall)) continue;
            if (gym.Doors.Any(d => Math.Abs(d.Cell.X - c.X) + Math.Abs(d.Cell.Y - c.Y) <= 1)) continue;
            if (gym.Furniture.Any(f => f.UseSpots.Count == 1 && f.UseSpots[0] == c)) continue;
            if (!Cell.Dirs4.Any(d => w.Ship.IsOpenFloor(c + d) && w.Ship.RoomAt(c + d) == gym)) continue;
            if (!Adaptation.SafeToBlock(w, gym, c)) continue;
            return c;
        }
        return null;
    }

    public string TaskName(RoomTask t) => t.Kind switch
    {
        RoomTaskKind.Wall => "칸막이 골조",
        RoomTaskKind.Unwall => "칸막이 뜯기",
        RoomTaskKind.Disconnect => $"{Furn(t.FurnitureId)?.Label} 떼어 내기",
        RoomTaskKind.Haul => $"{Furn(t.FurnitureId)?.Label} 나르기",
        RoomTaskKind.Reconnect => $"{Furn(t.FurnitureId)?.Label} 다시 잇기",
        RoomTaskKind.Assemble => "운동 기구 짜기",
        RoomTaskKind.Sign => $"'{t.Label}' 표지판",
        _ => "꾸미기",
    };

    /// <summary>운동할 방과 기구 (몸이 굳은 사람 · 열 시간에 한 번).</summary>
    public (Room room, Furniture mill)? WorkoutRoom(CrewMember c)
    {
        var w = _w;
        if (c.Fitness >= 0.6f || c.IsChild || _lastWorkout.TryGetValue(c.Id, out var last) && w.Tick - last < SimTime.Hours(10)) return null;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached || r.Abandoned || RoomUseSystem.Actual(r) != RoomType.Gym) continue;
            foreach (var f in r.Furniture)
                if (f.Type == FurnitureType.Treadmill && !f.Stowed && f.ReservedBy == null && f.UseSpots.Count > 0 && !_parked.Contains(f.Id)) return (r, f);
        }
        return null;
    }

    internal float DoingScore(CrewMember c)
    {
        if (!_doing.TryGetValue(c.Id, out var id)) return 0f;
        if (id == -2) return 0.55f;
        var t = Tasks.FirstOrDefault(x => x.Id == id);
        return t == null || t.Done ? 0f : t.Lifted && t.Leader == c.Id ? 0.9f : 0.62f;
    }

    // ─────────────────────────────── 일 짜기 ───────────────────────────────

    private Toil Work(RoomTask t, Skill skill, Vector2 face) => new WaitToil(SimTime.Hours(MathF.Max(0.2f, t.Hours) * 3f) + 10, Pose.Working, face)
    {
        EveryTick = (cm, world) =>
        {
            float sk = cm.SkillLevel(skill);
            t.Progress += (0.6f + 0.8f * sk) * (1f - 0.3f * cm.Vitals.Injury) * (cm.Room?.Dark == true ? 0.8f : 1f) / (MathF.Max(0.05f, t.Hours) * SimTime.TicksPerHour);
        },
        DoneWhen = (cm, world) => t.Progress >= 1f || t.Done,
    };

    public Job? Plan(CrewMember c, World w, DistanceField dist, Activity act)
    {
        if (Choose(c, dist) is not Choice ch) return null;
        var toils = ShipSim.Core.Plans.DropOff(c, w, dist);
        if (ch.Workout)
        {
            if (WorkoutRoom(c) is not (Room gym, Furniture mill)) return null;
            var spot = mill.UseSpots.FirstOrDefault(dist.Reachable);
            if (spot == default && !dist.Reachable(mill.UseSpots[0])) return null;
            _doing[c.Id] = -2;
            toils.Add(new GotoToil(spot == default ? mill.UseSpots[0] : spot));
            toils.Add(new WaitToil(SimTime.Minutes(40), Pose.Working, mill.Center)
            {
                EveryTick = (cm, world) =>
                {
                    cm.Fitness = MathF.Min(1f, cm.Fitness + 0.08f / SimTime.TicksPerHour);
                    cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.05f / SimTime.TicksPerHour);
                },
            });
            toils.Add(new DoToil((cm, world) => { world.RoomPlans.WorkedOut(cm, gym); return true; }));
            return new Job(act, "운동", toils) { TargetRoom = gym, Target = mill, LogText = $"{gym.Name}에서 땀을 뺀다", OnFinished = (cm, world, st) => world.RoomPlans.Finished(cm, st) }.Reserve(mill, c);
        }
        var t = ch.Task!;
        var p = PlanOf(t)!;
        var f = Furn(t.FurnitureId);
        if (ch.Helper)
        {
            if (t.Kind == RoomTaskKind.Haul)
            {
                var ld = Crew(t.Leader);
                if (Pickup(t, dist, ld?.Destination ?? ld?.Cell) is not Cell pick) return null;
                t.Helper = c.Id;
                toils.Add(new GotoToil(pick));
                toils.Add(new WaitToil(SimTime.Minutes(30), Pose.Standing, f?.Center) { DoneWhen = (cm, world) => t.Lifted || t.Leader < 0 || t.Done });
                toils.Add(new DoToil((cm, world) => t.Lifted && t.Leader >= 0));
                toils.Add(new GotoToilLate(cm => DropSpot(t, Crew(t.Leader)?.Destination)));
                toils.Add(new WaitToil(SimTime.Minutes(25), Pose.Standing) { DoneWhen = (cm, world) => t.Done || !t.Lifted });
                toils.Add(new DoToil((cm, world) => { world.RoomPlans.Helped(cm, t); return true; }));
            }
            else
            {
                t.Helper = c.Id;
                toils.Add(new GotoToil(FreeNear(t.Spot) ?? t.Spot));
                toils.Add(Work(t, t.Skill, w.Ship.Rooms[t.RoomId].Center));
            }
            _doing[c.Id] = t.Id;
            return new Job(act, $"거들기: {TaskName(t)}", toils) { TargetRoom = w.Ship.Rooms[t.RoomId], LogText = ch.Why, OnFinished = (cm, world, st) => world.RoomPlans.Finished(cm, st) };
        }
        t.Leader = c.Id;
        if (t.Since < 0) t.Since = w.Tick;
        _doing[c.Id] = t.Id;
        string label = TaskName(t);
        switch (t.Kind)
        {
            case RoomTaskKind.Wall or RoomTaskKind.Unwall or RoomTaskKind.Assemble or RoomTaskKind.Sign:
            {
                var room = w.Ship.Rooms[t.RoomId];
                toils.Add(new GotoToil(t.Spot));
                toils.Add(Work(t, t.Skill, t.Kind == RoomTaskKind.Sign ? t.Spot.Center : room.Center));
                toils.Add(new DoToil((cm, world) => world.RoomPlans.Finish(cm, t)));
                break;
            }
            case RoomTaskKind.Decorate:
                toils.Add(new GotoToil(t.Spot));
                toils.Add(Work(t, Skill.Mechanics, f?.Center ?? t.Spot.Center));
                toils.Add(new DoToil((cm, world) => world.RoomPlans.Finish(cm, t)));
                break;
            case RoomTaskKind.Disconnect when f != null:
            {
                var spot = f.UseSpots.FirstOrDefault(dist.Reachable);
                if (spot == default) return null;
                toils.Add(new GotoToil(spot));
                toils.Add(new DoToil((cm, world) => world.RoomPlans.OpenUp(cm, t, f)));
                toils.Add(Work(t, Skill.Electrical, f.Center));
                toils.Add(new DoToil((cm, world) => world.RoomPlans.Finish(cm, t)));
                break;
            }
            case RoomTaskKind.Reconnect when f != null:
            {
                var spot = f.UseSpots.FirstOrDefault(dist.Reachable);
                if (spot == default) return null;
                toils.Add(new GotoToil(spot));
                toils.Add(new DoToil((cm, world) => world.RoomPlans.OpenUp(cm, t, f)));
                toils.Add(Work(t, Skill.Electrical, f.Center));
                toils.Add(new DoToil((cm, world) => world.RoomPlans.Finish(cm, t)));
                break;
            }
            case RoomTaskKind.Haul when f != null:
            {
                if (Pickup(t, dist) is not Cell pick) return null;
                bool cart = !t.Lifted && t.Weight >= Heavy && CartUser < 0 && EnsureCart() is Cell home && dist.Reachable(home) && (t.Helper < 0 || t.Weight >= VeryHeavy);
                if (cart)
                {
                    CartUser = c.Id;
                    toils.Add(new GotoToil(CartHome!.Value));
                    toils.Add(new DoToil((cm, world) => { t.Cart = true; world.RoomPlans.Stats.CartHauls++; return true; }));
                }
                toils.Add(new GotoToil(pick));
                if (t.Need >= 2 && !cart)
                    toils.Add(new WaitToil(SimTime.Minutes(25), Pose.Standing, f.Center)
                    {
                        EveryTick = (cm, world) => { if ((world.Tick + cm.Id) % SimTime.Minutes(4) == 0) world.RoomPlans.CallHelp(cm, t, f); },
                        DoneWhen = (cm, world) => world.RoomPlans.HelperBeside(t, cm),
                    });
                toils.Add(new DoToil((cm, world) => world.RoomPlans.Lift(cm, t, f)));
                toils.Add(new GotoToilLate(cm => DropSpot(t)));
                toils.Add(new DoToil((cm, world) => world.RoomPlans.Place(cm, t, f)));
                label = t.Cart ? $"{f.Label} 카트로 나르기" : label;
                break;
            }
            default:
                t.Leader = -1;
                _doing.Remove(c.Id);
                return null;
        }
        return new Job(act, label, toils)
        {
            TargetRoom = w.Ship.Rooms[t.RoomId], Target = t.Kind is RoomTaskKind.Disconnect or RoomTaskKind.Reconnect ? f : null, LogText = ch.Why,
            OnFinished = (cm, world, st) => world.RoomPlans.Finished(cm, st), InterruptMargin = t.Lifted ? 0.3f : 0.15f,
        };
    }

    private Cell? EnsureCart()
    {
        var w = _w;
        if (CartHome is Cell h && w.Ship.IsWalkable(h) && w.Ship.RoomAt(h) is { Detached: false }) return h;
        CartHome = null;
        foreach (var r in w.Ship.Rooms.Where(r => !r.Detached && RoomUseSystem.Actual(r) is RoomType.Workshop or RoomType.Storage).OrderBy(r => RoomUseSystem.Actual(r) == RoomType.Workshop ? 0 : 1).ThenBy(r => r.Id))
            foreach (var c in r.Cells.OrderBy(c => c.Y).ThenBy(c => c.X))
                if (w.Ship.IsOpenFloor(c) && Cell.Dirs4.Count(d => w.Ship.Grid.Kind(c + d) == TileKind.Wall) >= 2 && !r.Furniture.Any(f => f.UseSpots.Contains(c))
                    && !r.Doors.Any(d => Math.Abs(d.Cell.X - c.X) + Math.Abs(d.Cell.Y - c.Y) <= 2))
                    return CartHome = c;
        return null;
    }

    internal bool HelperBeside(RoomTask t, CrewMember leader) =>
        t.Helper >= 0 && Crew(t.Helper) is CrewMember h && !h.IsMoving && (h.Position - leader.Position).Length() <= 2.3f;

    /// <summary>"누가 같이 좀 들어 줘!" — 곁의 쉬는 사람이 바로 다시 생각한다.</summary>
    internal void CallHelp(CrewMember c, RoomTask t, Furniture f)
    {
        var w = _w;
        if (t.Helper >= 0) return;
        if (c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, $"누가 {Ko.EulReul(f.Label)} 같이 좀 들어 줘!"));
        foreach (var o in w.Crew)
            if (o != c && o.CanAct && !o.Outside && o.IsAwake && (o.Position - c.Position).LengthSquared() < 15f * 15f && o.Job?.Urgent != true)
                o.NextThinkTick = Math.Min(o.NextThinkTick, w.Tick + 1 + o.Id % 4);
    }

    // ─────────────────────────────── 실제 동작 ───────────────────────────────

    /// <summary>점검 뚜껑을 열고 공사 테이프를 두른다 (분리 · 다시 잇기).</summary>
    internal bool OpenUp(CrewMember c, RoomTask t, Furniture f)
    {
        var w = _w;
        var p = PlanOf(t)!;
        var cell = f.UseSpots.FirstOrDefault(x => x != c.Cell);
        if (cell == default) cell = c.Cell;
        if (w.Body.OpenHatchAt(cell, -1, -1, t.Kind == RoomTaskKind.Disconnect ? "설비 옮기기 — 배선 · 배관 분리" : "설비 옮기기 — 다시 잇기") != null)
        {
            w.Body.SetMark(cell, CellMark.Tape, 1f, "공사 테이프");
            if (!p.Hatches.Contains(cell)) p.Hatches.Add(cell);
        }
        if (t.Kind == RoomTaskKind.Disconnect && f.Machine is Machine m) { m.Parked = true; if (!_parked.Contains(f.Id)) _parked.Add(f.Id); }
        return true;
    }

    /// <summary>일하는 단계를 마쳤다.</summary>
    internal bool Finish(CrewMember c, RoomTask t)
    {
        var w = _w;
        if (t.Done) return true;
        if (t.Progress < 1f) return false;
        var p = PlanOf(t)!;
        var room = w.Ship.Rooms[t.RoomId];
        var f = Furn(t.FurnitureId);
        switch (t.Kind)
        {
            case RoomTaskKind.Wall:
            {
                if (Remodel.FindSplit(w, room, 2) is not Remodel.SplitPlan sp) { t.Progress = 0.95f; return false; }
                if (!ItemsV15.Use(w, ItemKind.Plate)) w.Log.Add(w.Tick, LogKind.Ship, "칸막이 — 금속판이 없어 뜯어 둔 패널로", c.Id);
                ItemsV15.Use(w, ItemKind.Plate);
                ItemsV15.Use(w, ItemKind.Structure);
                var b = Remodel.Apply(w, sp, c);
                if (b == null) { t.Progress = 0.95f; return false; }
                p.NewRoomId = b.Id;
                Stats.Walls++;
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {room.Name}에 칸막이를 세웠다 — {b.Name}", c.Id);
                MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: 칸막이 (회의)");
                if (p.Kind == RoomPlanKind.Convert) FitOut(p, room, b);
                break;
            }
            case RoomTaskKind.Unwall:
            {
                var merged = Remodel2.Merge(w, room, c);
                if (merged == null) { t.Progress = 0.95f; return false; }
                Stats.Unwalls++;
                break;
            }
            case RoomTaskKind.Disconnect when f != null:
            {
                Stats.Disconnects++;
                if (f.Machine is Machine m) { m.Feed = 0f; if (Procedures.Plumbed(f.Type)) m.Line = 0f; }
                w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(c.Name)} {Ko.EulReul(f.Label)} 떼어 냈다 — 멈춤: {p.Stops}", c.Id);
                Mishap(c, p, f);
                break;
            }
            case RoomTaskKind.Reconnect when f != null:
            {
                Stats.Reconnects++;
                _parked.Remove(f.Id);
                bool cable = ItemsV15.Use(w, ItemKind.Cable);
                bool hose = !Procedures.Plumbed(f.Type) || ItemsV15.Use(w, ItemKind.Hose) || ItemsV15.Use(w, ItemKind.Sealant);
                if (f.Machine is Machine m)
                {
                    m.Parked = false;
                    m.Feed = cable ? 1f : 0.6f;
                    m.Spliced |= !cable;
                    m.Line = hose ? 1f : 0.5f;
                    m.Wear = MathF.Min(1f, m.Wear + 0.03f); // 떼었다 붙이면 조금 헐거워진다
                }
                foreach (var h in p.Hatches) if (w.Body.Hatches.FirstOrDefault(x => x.Cell == h) is OpenHatch oh) w.Body.CloseHatch(oh);
                p.Hatches.Clear();
                w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(c.Name)} {Ko.EulReul(f.Label)} {f.Room.Name}에서 다시 이었다" + (cable ? "" : " (케이블이 없어 임시로 이어 붙였다)") + (hose ? "" : " · 호스가 없어 이음이 헐겁다"), c.Id);
                break;
            }
            case RoomTaskKind.Assemble:
            {
                bool full = ItemsV15.Use(w, ItemKind.Motor) && ItemsV15.Use(w, ItemKind.Belt);
                if (!full) ItemsV15.Use(w, ItemKind.Plate);
                var at = w.Ship.IsOpenFloor(t.Spot) && w.Ship.RoomAt(t.Spot) == room && !w.Crew.Any(o => o.Cell == t.Spot && o != c) ? t.Spot : AssembleSpot(room) ?? t.Spot;
                if (!w.Ship.IsOpenFloor(at) || w.Ship.RoomAt(at) != room) return false;
                foreach (var o in w.Crew) if (!o.Dead && o.Cell == at && FreeNear(at) is Cell free) { o.Position = free.Center; o.PreviousPosition = o.Position; }
                var mill = w.Ship.AddFurniture(FurnitureType.Treadmill, at);
                mill.Improved = full;
                mill.Label = full ? "운동 기구" : "손으로 짠 운동 기구";
                w.Paths.Invalidate();
                Stats.Assembled++;
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {room.Name}에 {Ko.EulReul(mill.Label)} 짰다" + (full ? " (모터 · 벨트)" : " (모터가 없어 금속판으로 추를 달았다)"), c.Id);
                break;
            }
            case RoomTaskKind.Sign:
            {
                string before = room.Name;
                room.CustomName = t.Label;
                ItemsV15.Use(w, ItemKind.Paint);
                Stats.Signs++;
                MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: '{t.Label}' 표지판");
                w.History.Add(w, HistoryKind.Adaptation, $"{Ko.IGa(c.Name)} {before} 문에 '{t.Label}' 표지판을 달았다 — {p.Why}", room, new[] { c }, log: true);
                c.Say(w, Persona.Say(c, $"이제 여긴 '{t.Label}'이다"));
                foreach (var o in w.Crew) if (!o.Dead && o.Room == room && o != c) Life.Diary(w, o, Persona.Say(o, $"{before}에 '{t.Label}' 표지판이 붙었다."));
                if (Crew(p.NamedFor) is CrewMember named && !named.Dead)
                {
                    named.Needs.Stress = MathF.Max(0f, named.Needs.Stress - (named.Traits.Sociability < 0.35f ? -0.02f : 0.06f));
                    Life.Diary(w, named, Persona.Say(named, named.Traits.Sociability < 0.35f ? $"문에 내 이름이 붙었다. 쑥스럽다." : $"'{t.Label}' — 문에 내 이름이 붙었다."));
                    foreach (var o in w.Crew) if (!o.Dead && o != named && p.For.Contains(o.Id)) o.ChangeAffinity(named, 0.01f);
                }
                break;
            }
            case RoomTaskKind.Decorate:
            {
                if (p.Prop == null || f == null) break;
                var placed = w.Props.Place(Props.Get(p.Prop), room, c, "선실 꾸미기 (회의)", near: f.Cells.FirstOrDefault());
                if (placed == null) return false;
                Stats.Decorated++;
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.08f);
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 침대 곁에 {Ko.EulReul(placed.Name)} 걸었다 — {p.Why}", c.Id);
                Life.Diary(w, c, Persona.Say(c, $"침대 곁에 {Ko.EulReul(placed.Name)} 걸었다. 이제 좀 내 자리 같다."));
                break;
            }
        }
        t.Done = true;
        Advance(p);
        return true;
    }

    /// <summary>창고 절반 → 운동실: 칸막이가 서면 선반을 내보내고 운동 기구를 들이고 표지판을 단다.</summary>
    private void FitOut(RoomPlan p, Room rest, Room gym)
    {
        var w = _w;
        foreach (var f in gym.Furniture.Where(f => f.Storage != null && f.Type == FurnitureType.Shelf).OrderBy(f => f.Id).ToList())
            AddTask(p, RoomTaskKind.Haul, 1, gym, f.UseSpots.FirstOrDefault(), 0f, Skill.Mechanics, f, rest);
        var mill = w.Ship.Furniture.Where(f => f.Type == FurnitureType.Treadmill && !f.Stowed && !f.Room.Detached && RoomUseSystem.Actual(f.Room) != RoomType.Gym)
            .OrderBy(f => f.Id).FirstOrDefault();
        if (mill != null)
        {
            AddTask(p, RoomTaskKind.Disconnect, 1, mill.Room, mill.UseSpots.FirstOrDefault(), 0.3f, Skill.Electrical, mill);
            var h = AddTask(p, RoomTaskKind.Haul, 2, mill.Room, mill.UseSpots.FirstOrDefault(), 0f, Skill.Mechanics, mill, gym);
            h.ToRoom = gym.Id;
            AddTask(p, RoomTaskKind.Reconnect, 3, gym, default, 0.4f, Skill.Electrical, mill, gym);
        }
        else
        {
            AddTask(p, RoomTaskKind.Assemble, 2, gym, AssembleSpot(gym) ?? gym.Cells[0], 1.2f); // 선반이 다 나가면 자리를 다시 본다
        }
        AddTask(p, RoomTaskKind.Sign, 4, gym, SignSpot(gym), 0.25f, label: p.NewName);
    }

    /// <summary>계획이 다 끝났나.</summary>
    private void Advance(RoomPlan p)
    {
        var w = _w;
        if (Tasks.Any(t => t.PlanId == p.Id && !t.Done)) return;
        p.State = "완료";
        p.Done = w.Tick;
        Stats.Finished++;
        Unpark(p);
        foreach (var h in p.Hatches) if (w.Body.Hatches.FirstOrDefault(x => x.Cell == h) is OpenHatch oh) w.Body.CloseHatch(oh);
        p.Hatches.Clear();
        float hours = (p.Done - p.Decided) / (float)SimTime.TicksPerHour;
        var hands = Tasks.Where(t => t.PlanId == p.Id).SelectMany(t => new[] { t.Leader, t.Helper }).Where(id => id >= 0).Distinct().Count();
        w.History.Add(w, HistoryKind.Upgrade, $"회의가 정한 방 공사를 마쳤다: {p.Title} ({hours:0.#}시간" + (p.Advice != "" ? $" · 컴퓨터 예상 {p.Hours:0.#}시간" : "") + (p.Accidents > 0 ? $" · 사고 {p.Accidents}번" : "") + ")",
            p.NewRoomId >= 0 ? w.Ship.Rooms[p.NewRoomId] : w.Ship.Rooms[p.RoomId], log: true);
        if (Crew(p.Proposer) is CrewMember by && !by.Dead)
        {
            by.Needs.Stress = MathF.Max(0f, by.Needs.Stress - 0.05f);
            foreach (var id in p.For) if (Crew(id) is CrewMember o && o != by && !o.Dead) o.ChangeAffinity(by, 0.015f);
        }
        if (p.Kind == RoomPlanKind.Convert && p.NewRoomId >= 0 && p.NewUse is RoomType use)
            w.RoomUse.Add(w.Ship.Rooms[p.NewRoomId], use, 0.6f); // 표지판을 달며 한 번씩 뛰어 본다
    }

    private void Unpark(RoomPlan p)
    {
        foreach (var t in Tasks)
            if (t.PlanId == p.Id && t.FurnitureId >= 0 && _parked.Contains(t.FurnitureId) && (t.Kind != RoomTaskKind.Disconnect || p.State != "공사"))
            {
                _parked.Remove(t.FurnitureId);
                if (Furn(t.FurnitureId)?.Machine is Machine m) { m.Parked = false; if (m.Feed < 0.3f) { m.Feed = 0.6f; m.Spliced = true; } if (m.Line < 0.3f) m.Line = 0.5f; }
            }
    }

    /// <summary>떼어 내다 사고: 냉각수 · 물이 쏟아지고(물 × 전기), 배터리 · 발전기는 불꽃(불), 산소 발생기는 산소가 샌다. 순서를 지키면 덜하다.</summary>
    private void Mishap(CrewMember c, RoomPlan p, Furniture f)
    {
        var w = _w;
        if (!EngineGear(f.Type)) return;
        float risk = p.Risk * (p.OrderKept ? 0.35f : 1f) * (1.25f - 0.5f * c.SkillLevel(Skill.Electrical));
        if (!R.Chance(MathF.Min(0.9f, risk * 0.6f))) return;
        p.Accidents++;
        Stats.Accidents++;
        var room = f.Room;
        var cell = f.UseSpots.FirstOrDefault(x => x != c.Cell);
        if (cell == default) cell = c.Cell;
        string what;
        switch (f.Type)
        {
            case FurnitureType.CoolantPump or FurnitureType.WaterRecycler or FurnitureType.HeatExchanger:
                room.Flood += 25f;
                w.Body.RaiseMark(cell, CellMark.Wet, 1f, f.Type == FurnitureType.CoolantPump ? "냉각수" : "물기");
                what = f.Type == FurnitureType.CoolantPump ? "관을 비우기 전에 풀어 냉각수가 쏟아졌다" : "밸브를 덜 잠가 물이 쏟아졌다";
                break;
            case FurnitureType.OxygenGenerator:
                room.O2Leak += 0.6f;
                what = "산소관을 풀다 산소가 샌다";
                break;
            default:
                if (!room.BreakerOff) Incidents.Fire(w, cell);
                what = room.BreakerOff ? "단자가 튀었지만 차단해 둬서 괜찮았다" : "차단하지 않은 단자에서 불꽃이 튀었다";
                break;
        }
        w.Log.Add(w.Tick, LogKind.Warning, $"{f.Label} 떼어 내다 사고 — {what} ({room.Name})", c.Id);
        w.History.Add(w, HistoryKind.Incident, $"{Ko.IGa(c.Name)} {Ko.EulReul(f.Label)} 떼어 내다 — {what}" + (p.OrderKept ? "" : " (컴퓨터가 낸 순서를 건너뛰었다)"), room, new[] { c }, cell);
        w.Automation.Book.Add(ActKind.Advice, room, $"{f.Label} 분리 중 사고 — {what}", p.OrderKept ? "순서를 지켰는데도 났다" : "공사 순서를 건너뛰었다",
            f.Type == FurnitureType.CoolantPump ? "원자로 출력을 낮추고 바닥 물을 뺀다" : "분전함을 내리고 본다", "가까이 가지 마세요", "mishap:" + p.Id, SimTime.Hours(1));
    }

    /// <summary>든다 (무거우면 둘이 · 카트 · 혼자 들다 허리를 삐끗).</summary>
    internal bool Lift(CrewMember c, RoomTask t, Furniture f)
    {
        var w = _w;
        if (t.Done) return false;
        bool helper = HelperBeside(t, c);
        if (t.Need >= 2 && !helper && !t.Cart)
        {
            if (c.Traits.Bravery < 0.62f || c.Vitals.Injury > 0.2f) { c.Say(w, Persona.Say(c, "혼자선 무리다 — 나중에")); return false; }
            Stats.SoloLifts++;
            if (R.Chance(0.45f))
            {
                Stats.SoloHurt++;
                NeedsSystem.AddInjury(c.Vitals, 0.06f, "혼자 무거운 걸 들다 허리를 삐끗");
                w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 혼자 {Ko.EulReul(f.Label)} 들다 허리를 삐끗했다", c.Id);
                c.Say(w, Persona.Say(c, "아이고 허리야…"));
            }
        }
        if (t.ToCells.Count == 0 || !t.ToCells.All(x => w.Ship.IsOpenFloor(x) || !w.Ship.IsWalkable(x) && w.Ship.FurnitureAt(x) == null))
        {
            t.ToCells.Clear();
            if (Destination(t, f) is List<Cell> cells) t.ToCells.AddRange(cells);
            else if (t.SetDown == null && !t.Lifted)
            {
                // 둘 곳이 없다 — 그대로 둔다 (땀방 구석에 남은 선반)
                w.Log.Add(w.Tick, LogKind.Ship, $"{f.Label} — 둘 곳이 없어 그 자리에 남긴다", c.Id);
                t.Done = true;
                if (PlanOf(t) is RoomPlan pp) Advance(pp);
                return false;
            }
        }
        if (!t.Lifted && t.SetDown == null)
        {
            t.FromCell = f.Cells[0];
            t.FromRoom = f.Room.Id;
            LiftOff(f, c, Crew(t.Helper));
            w.Log.Add(w.Tick, LogKind.Life, helper ? $"{Ko.WaGwa(c.Name)} {Ko.IGa(Crew(t.Helper)!.Name)} {Ko.EulReul(f.Label)} 같이 들었다"
                : t.Cart ? $"{Ko.IGa(c.Name)} {Ko.EulReul(f.Label)} 카트에 실었다" : $"{Ko.IGa(c.Name)} {Ko.EulReul(f.Label)} 들었다", c.Id);
        }
        t.Lifted = true;
        t.SetDown = null;
        return true;
    }

    private List<Cell>? Destination(RoomTask t, Furniture f)
    {
        var w = _w;
        var avoid = Reserved();
        var rooms = new List<Room>();
        if (t.ToRoom >= 0) rooms.Add(w.Ship.Rooms[t.ToRoom]);
        if (f.Storage != null)
            rooms.AddRange(w.Ship.Rooms.Where(r => !r.Detached && !r.Abandoned && !r.OffLimits && r.Id != t.FromRoom && !rooms.Contains(r) && r.Type != RoomType.Corridor
                                                   && RoomUseSystem.Actual(r) is RoomType.Storage or RoomType.Workshop or RoomType.Lounge or RoomType.Mess or RoomType.Hydroponics)
                .OrderBy(r => RoomUseSystem.Actual(r) switch { RoomType.Storage => 0, RoomType.Workshop => 1, _ => 2 }).ThenBy(r => r.Id)); // 창고 · 정비실 · 그래도 없으면 휴게실 벽에
        foreach (var r in rooms)
            if (Spot(t.W, t.H, r, avoid) is List<Cell> cells) { t.ToRoom = r.Id; return cells; }
        return null;
    }

    /// <summary>격자에서 들어낸다 (목록에는 남는다 — 번호가 바뀌지 않게). 그 설비를 쓰러 오던 사람은 다시 생각한다.</summary>
    private void LiftOff(Furniture f, CrewMember leader, CrewMember? helper)
    {
        var w = _w;
        var ship = w.Ship;
        foreach (var c in f.Cells) if (ship.Grid.FurnitureId(c) == f.Id) ship.Grid.SetFurnitureId(c, -1);
        f.Room.Furniture.Remove(f);
        f.UseSpots.Clear();
        f.ReservedBy = null;
        f.Stowed = true;
        foreach (var c in w.Crew)
        {
            if (c == leader || c == helper || c.Dead || c.Job == null) continue;
            bool uses = c.Job.Target == f || c.Job.Reservations.Contains(f) || c.Job.Toils.Any(x => x is TakeToil tt && tt.From == f || x is PutToil pt && pt.Into == f || x is TakeKitToil tk && tk.From == f);
            if (!uses) continue;
            c.EndJob(w, ToilStatus.Interrupted);
            c.NextThinkTick = w.Tick;
        }
        w.Paths.Invalidate();
        w.Structure.Touch();
    }

    /// <summary>내려놓는다 — 새 자리에 (길 · 작업 대상 · 습관).</summary>
    internal bool Place(CrewMember c, RoomTask t, Furniture f)
    {
        var w = _w;
        var ship = w.Ship;
        if (!t.Lifted || t.Done) return false;
        if (t.ToCells.Count == 0 || t.ToCells.Any(x => !ship.IsOpenFloor(x) && ship.FurnitureAt(x) != null || ship.RoomAt(x) == null))
        {
            t.ToCells.Clear();
            if (Destination(t, f) is List<Cell> again) t.ToCells.AddRange(again);
            else return false;
        }
        var to = ship.RoomAt(t.ToCells[0])!;
        // 그 칸에 선 사람은 비켜선다
        foreach (var o in w.Crew)
        {
            if (o.Dead || !t.ToCells.Contains(o.Cell)) continue;
            if (FreeNear(o.Cell) is Cell free) { o.Position = free.Center; o.PreviousPosition = o.Position; }
        }
        var from = t.FromRoom >= 0 ? ship.Rooms[t.FromRoom] : f.Room;
        f.Cells.Clear();
        f.MinX = f.MinY = int.MaxValue;
        f.MaxX = f.MaxY = int.MinValue;
        f.Room = to;
        to.Furniture.Add(f);
        foreach (var cell in t.ToCells) { ship.Grid.SetFurnitureId(cell, f.Id); f.Include(cell); }
        f.Cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        if (FurnitureTypes.Walkable(f.Type)) f.UseSpots.Add(f.Cells[0]);
        else
        {
            var seen = new HashSet<Cell>();
            foreach (var cell in f.Cells)
                foreach (var d in Cell.Dirs4)
                {
                    var n = cell + d;
                    if (ship.IsOpenFloor(n) && ship.RoomAt(n) == to && seen.Add(n)) f.UseSpots.Add(n);
                }
        }
        f.Stowed = false;
        foreach (var o in w.Board.All) if (o.Target.Furniture == f) o.Target.Rehome(to);
        w.Paths.Invalidate();
        w.Structure.Touch();
        w.Board.RequestScan();
        t.Lifted = false;
        t.Done = true;
        if (CartUser == c.Id) CartUser = -1;
        Stats.Moves++;
        var helper = HelperBeside(t, c) || t.Helper >= 0 && Crew(t.Helper) is CrewMember hh && (hh.Position - c.Position).Length() < 4f ? Crew(t.Helper) : null;
        var carriers = helper != null ? new[] { c, helper } : new[] { c };
        w.RoomUse.NoteMove(f, t.FromCell, from, carriers);
        if (helper != null)
        {
            Stats.CoCarry++;
            c.ChangeAffinity(helper, 0.03f);
            helper.ChangeAffinity(c, 0.03f);
            w.Relations.Remember(helper, c, RelationReason.SharedHardship, $"{Ko.EulReul(f.Label)} 같이 들어 {Ko.EuRo(to.Name)} 옮겼다");
        }
        string how = helper != null ? $"{Ko.WaGwa(c.Name)} {helper.Name}이 같이 들어" : t.Cart ? $"{Ko.IGa(c.Name)} 카트로" : $"{Ko.IGa(c.Name)}";
        w.Log.Add(w.Tick, LogKind.Life, $"{how} {Ko.EulReul(f.Label)} {from.Name}에서 {Ko.EuRo(to.Name)} 옮겼다", c.Id);
        MarkLog.Add(to.Marks, w.Tick, $"{f.Label} 들어옴 ({from.Name}에서)");
        MarkLog.Add(from.Marks, w.Tick, $"{f.Label} 나감 ({to.Name}로)");
        if (f.Machine is Machine m) MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: {from.Name} → {to.Name}");
        t.Cart = false;
        if (PlanOf(t) is RoomPlan p) Advance(p);
        return true;
    }

    /// <summary>공사가 끊겨 들고 있던 것을 원래 자리(없으면 곁)에 되놓는다.</summary>
    private void PutBack(RoomTask t)
    {
        var w = _w;
        if (Furn(t.FurnitureId) is not Furniture f || !f.Stowed) return;
        var from = t.FromRoom >= 0 ? w.Ship.Rooms[t.FromRoom] : f.Room;
        t.ToCells.Clear();
        t.ToRoom = from.Id;
        if (Spot(t.W, t.H, from, null) is List<Cell> cells) t.ToCells.AddRange(cells);
        else return;
        t.Lifted = true;
        var c = w.Crew.First(x => !x.Dead);
        Place(c, t, f);
    }

    /// <summary>거든 사람: 같이 들었다.</summary>
    internal void Helped(CrewMember c, RoomTask t) { }

    internal void WorkedOut(CrewMember c, Room gym)
    {
        var w = _w;
        _lastWorkout[c.Id] = w.Tick;
        Stats.Workouts++;
        w.RoomUse.Add(gym, RoomType.Gym, 0.3f); // 표본 사이에 끝나도 적는다
        if (R.Chance(0.35f) && c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, gym.CustomName != null ? $"{gym.CustomName} 최고다" : "땀 좀 흘렸다"));
    }

    /// <summary>방이 막 정해진 용도 (회의가 바꾼 방 — 사흘은 그 용도를 밀어 준다).</summary>
    public (RoomType use, float bonus)? Intent(Room r)
    {
        foreach (var p in Plans)
            if (p.NewRoomId == r.Id && p.NewUse is RoomType u && p.State is "공사" or "완료" && (p.Done < 0 || _w.Tick - p.Done < SimTime.TicksPerDay * 3)) return (u, 2.5f);
        return null;
    }

    // ─────────────────────────────── 지문 ───────────────────────────────

    private static long Str(string? s)
    {
        if (s == null) return 0;
        uint h = 2166136261;
        foreach (char ch in s) { h ^= ch; h *= 16777619; }
        return h;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        var s = Stats;
        I(s.Proposed); I(s.Passed); I(s.Rejected); I(s.Finished); I(s.Moves); I(s.CoCarry); I(s.CartHauls); I(s.Walls); I(s.Signs); I(s.Accidents); I(s.Workouts); I(s.SetDowns);
        foreach (var p in Plans) { I((int)p.Kind); I(Str(p.State)); I(p.For.Count); I(p.Against.Count); F(p.Risk); I(p.OrderKept ? 1 : 0); I(Str(p.NewName)); }
        foreach (var t in Tasks) { I(t.Done ? 1 : 0); F(t.Progress); I(t.Leader); I(t.Helper); I(t.Lifted ? 1 : 0); I(t.ToCells.Count); }
        foreach (var r in _w.Ship.Rooms) if (r.CustomName != null) { I(r.Id); I(Str(r.CustomName)); }
        I(CartUser);
    }
}

/// <summary>v16.17 방 공사 · 땀방 운동.</summary>
public sealed class RoomWorkActivity : Activity
{
    public override string Id => "roomwork";
    public override string Label => "방 공사";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.IsChild) return (0f, "—");
        var rp = w.RoomPlans;
        if (c.Job?.Activity == this && rp.DoingScore(c) is float ds && ds > 0f) return (ds, c.Job.Label);
        if (rp.Tasks.Count == 0 && rp.WorkoutRoom(c) == null) return (0f, "—");
        if (rp.Choose(c, dist) is not RoomPlanSystem.Choice ch) return (0f, "할 일 없음");
        float s = ch.Score;
        if (c.Pose == Pose.Sleeping) s *= 0.2f;
        return (MathF.Max(0f, s), ch.Why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist) => w.RoomPlans.Plan(c, w, dist, this);
}
