using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

/// <summary>선내 로봇 종류 (v10.10).</summary>
public enum RobotKind
{
    Hauler,     // 운반: 배식기 채우기, 드론 거치대 보급, 물통 나르기, 비상 물자 나르기
    Maintainer, // 정비: 정기 정비, 조명 갈기, 사람 옆에서 거들기 (긴 수리·제작이 빨라진다)
    Gardener,   // 재배: 작물 돌보기, 수확해 냉장고로
    Safety,     // 방재: 순찰(불·사고 전조 발견), 소화 거품
}

public enum RobotState
{
    Docked,   // 충전대에서 충전·대기
    Active,   // 일하러 다니는 중 (걷기·일하기)
    Stalled,  // 멈춤 (배터리 방전·고장) — 사람이 고치거나 끌고 와야 한다
    Towed,    // 사람이 끌고 가는 중
    Lost,     // 방째로 떨어져 나가 잃었다
}

/// <summary>로봇 고장 (고치는 데 드는 부품·시간이 다르다).</summary>
public enum RobotFault { Drive, Sensor, Controller, Cell, Scorched }

/// <summary>
/// 선내 로봇 한 대 (v10.10). 드론이 선체 밖을 돌보듯, 로봇은 선체 안의 반복되는 일을 맡는다.
/// 충전대에서 전기를 받아야 움직이고, 닳으면 고장 나고, 멈추면 사람이 고치거나 끌고 와야 한다 —
/// 로봇이 멈추면 그 일은 다시 작업 목록으로 돌아가 사람이 한다. 한가한 정비 로봇은 긴 일을 하는 사람 옆에서 거든다.
/// </summary>
public sealed class Robot
{
    public int Id { get; init; }
    public RobotKind Kind { get; init; }
    public string Name { get; init; } = "";
    public Furniture Dock { get; init; } = null!;
    public int Slot { get; init; }

    public Vector2 Position { get; set; }
    public Vector2 PreviousPosition { get; set; }
    public Vector2 Facing { get; set; } = new(0, 1);
    public Cell Cell => Cell.FromPosition(Position);
    public Room? Room { get; internal set; }

    public RobotState State { get; internal set; }
    public long StateSince { get; internal set; }

    /// <summary>배터리 0~1 (가득 차면 일곱 시간쯤 움직인다).</summary>
    public float Battery { get; set; } = 1f;

    /// <summary>상태 0~1 (닳으면 고장이 잦아진다. 사람이 정비하면 돌아온다).</summary>
    public float Condition { get; set; } = 1f;

    /// <summary>고장 (null이면 멀쩡하다).</summary>
    public RobotFault? Fault { get; internal set; }

    /// <summary>시험용: 꺼 둔 로봇 (로봇 없는 배와 견줄 때).</summary>
    public bool Disabled { get; set; }

    /// <summary>방재 로봇의 소화 거품 0~1 (충전대에서 다시 찬다).</summary>
    public float Foam { get; set; } = 1f;

    /// <summary>맡은 일.</summary>
    public WorkOrder? Order { get; internal set; }

    /// <summary>옆에서 거드는 사람 (정비 로봇).</summary>
    public CrewMember? Helping { get; internal set; }

    /// <summary>끌고 가는 사람.</summary>
    public CrewMember? TowedBy { get; internal set; }

    /// <summary>싣고 다니는 것.</summary>
    public ItemStack? Cargo { get; internal set; }

    public string Doing { get; internal set; } = "대기";

    internal List<RobotStep>? Steps { get; set; }
    internal int StepIndex { get; set; }
    internal List<Cell>? Path { get; set; }
    internal int PathIndex { get; set; }
    internal Cell? Goal { get; set; }
    internal long IdleSince { get; set; }
    /// <summary>다음에 일을 고를 틱 (충전대에서 기다릴 때 너무 자주 생각하지 않게).</summary>
    internal long NextDecide { get; set; }
    /// <summary>충전대로 돌아가는 중 (배터리 점검으로 다시 되돌리지 않게).</summary>
    internal bool Homing { get; set; }
    internal long NextPatrol { get; set; }

    // ── 기록 ──
    public float ActiveHours { get; internal set; }
    public float AssistHours { get; internal set; }
    public int JobsDone { get; internal set; }
    public int Breakdowns { get; internal set; }
    public int Fetched { get; internal set; }
    public List<Mark> Marks { get; } = new();

    public bool AtDock => State == RobotState.Docked;
    public bool Operational => !Disabled && Fault == null && State is RobotState.Docked or RobotState.Active && !Dock.Room.Detached;
    public Vector2 DockPosition => Dock.Cells[Math.Min(Slot, Dock.Cells.Count - 1)].Center;
    public string KindName => RobotSystem.KindName(Kind);

    /// <summary>지금 하는 단계의 진척 (화면).</summary>
    public float? Progress => Steps != null && StepIndex < Steps.Count ? Steps[StepIndex].Progress : null;
}

// ─────────────────────────────── 로봇의 작은 단계 (승무원의 Toil과 같은 생각) ───────────────────────────────

internal abstract class RobotStep
{
    public virtual void Begin(Robot r, World w) { }
    public abstract ToilStatus Tick(Robot r, World w);
    public virtual float? Progress => null;
    public virtual bool Moving => false;
}

internal sealed class RGoto : RobotStep
{
    private readonly Func<Robot, Cell?> _pick;
    private bool _ok;
    public RGoto(Cell target) => _pick = _ => target;
    public RGoto(Func<Robot, Cell?> pick) => _pick = pick;
    public override bool Moving => true;

    public override void Begin(Robot r, World w)
    {
        _ok = true;
        if (_pick(r) is not Cell goal) { r.Path = null; return; }
        _ok = RobotSystem.SetDestination(r, w, goal);
    }

    public override ToilStatus Tick(Robot r, World w)
    {
        if (!_ok) return ToilStatus.Failed;
        return RobotSystem.Move(r, w) switch { true => ToilStatus.Succeeded, false when r.Path == null => ToilStatus.Failed, _ => ToilStatus.Running };
    }
}

internal sealed class RTake : RobotStep
{
    private readonly Furniture _from;
    private readonly ItemKind _kind;
    private readonly int _count;
    private readonly bool _partialOk;
    public RTake(Furniture from, ItemKind kind, int count, bool partialOk = false) { _from = from; _kind = kind; _count = count; _partialOk = partialOk; }

    public override ToilStatus Tick(Robot r, World w)
    {
        if (_from.Storage == null || (r.Cargo is ItemStack held && held.Kind != _kind)) return ToilStatus.Failed;
        if (!_partialOk && _from.Storage.Count(_kind) < _count) return ToilStatus.Failed;
        int got = _from.Storage.Take(_kind, _count);
        if (got <= 0) return ToilStatus.Failed;
        r.Cargo = new ItemStack(_kind, (r.Cargo?.Count ?? 0) + got);
        return ToilStatus.Succeeded;
    }
}

internal sealed class RPut : RobotStep
{
    private readonly Furniture _into;
    public RPut(Furniture into) => _into = into;

    public override ToilStatus Tick(Robot r, World w)
    {
        if (r.Cargo is not ItemStack held || _into.Storage == null) return ToilStatus.Succeeded;
        int put = _into.Storage.Add(held.Kind, held.Count);
        r.Cargo = held.Count - put > 0 ? new ItemStack(held.Kind, held.Count - put) : null;
        return ToilStatus.Succeeded;
    }
}

internal sealed class RWork : RobotStep
{
    private readonly float _hours;
    private readonly WorkOrder? _shared;
    private readonly Vector2? _face;
    private float _done, _needed;
    public Func<Robot, World, bool>? CanContinue { get; init; }
    public RWork(float hours, WorkOrder? shared, Vector2? face) { _hours = hours; _shared = shared; _face = face; }
    public override float? Progress => _shared != null ? _shared.Progress : _needed > 0 ? _done / _needed : 0f;

    public override void Begin(Robot r, World w)
    {
        _needed = SimTime.Hours(_hours) * RobotSystem.WorkFactor(r.Kind);
        if (_face is Vector2 f && (f - r.Position).LengthSquared() > 0.0001f) r.Facing = Vector2.Normalize(f - r.Position);
    }

    public override ToilStatus Tick(Robot r, World w)
    {
        if (CanContinue != null && !CanContinue(r, w)) return ToilStatus.Failed;
        // 사람과 같은 일을 하면 진척을 함께 쓴다 (누가 먼저 끝내든 한 번만 끝난다)
        if (_shared != null)
        {
            if (_shared.Closed) return ToilStatus.Failed;
            _shared.Progress = MathF.Min(1f, _shared.Progress + 1f / MathF.Max(1f, _needed));
            return _shared.Progress >= 1f ? ToilStatus.Succeeded : ToilStatus.Running;
        }
        _done += 1f;
        return _done >= _needed ? ToilStatus.Succeeded : ToilStatus.Running;
    }
}

internal sealed class RDo : RobotStep
{
    private readonly Func<Robot, World, bool> _action;
    public RDo(Func<Robot, World, bool> action) => _action = action;
    public override ToilStatus Tick(Robot r, World w) => _action(r, w) ? ToilStatus.Succeeded : ToilStatus.Failed;
}

/// <summary>사람 옆에 서서 거든다: 그 사람이 긴 일(WorkToil)을 하는 동안만.</summary>
internal sealed class RAssist : RobotStep
{
    private readonly CrewMember _who;
    private readonly Job _job;
    public RAssist(CrewMember who, Job job) { _who = who; _job = job; }

    public override void Begin(Robot r, World w)
    {
        r.Helping = _who;
        _who.Helper = r;
        r.Doing = $"{Ko.EulReul(_who.Name)} 거든다 — {_job.Order?.Title ?? _job.Label}";
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(r.Name)} 옆에서 부품을 잡아 주고 공구를 건넨다 ({_job.Order?.Title ?? _job.Label})", _who.Id);
    }

    public override ToilStatus Tick(Robot r, World w)
    {
        if (_who.Job != _job || !_who.CanAct || _who.Helper != r) return ToilStatus.Succeeded;
        if (_job.Current is not WorkToil) return (_job.Current is GotoToil or GotoToilLate) ? ToilStatus.Succeeded : ToilStatus.Running;
        if ((_who.Position - r.Position).LengthSquared() > 2.6f * 2.6f) return ToilStatus.Succeeded; // 사람이 자리를 옮겼다
        if ((_who.Position - r.Position).LengthSquared() > 0.0001f) r.Facing = Vector2.Normalize(_who.Position - r.Position);
        r.AssistHours += 1f / SimTime.TicksPerHour;
        return ToilStatus.Running;
    }
}

/// <summary>불 옆에서 소화 거품을 뿌린다 (방재 로봇). 곁에 불이 없거나 거품이 떨어지면 끝.</summary>
internal sealed class RSpray : RobotStep
{
    public override ToilStatus Tick(Robot r, World w)
    {
        Cell? nearest = null;
        float best = 2.6f * 2.6f;
        foreach (var (cell, _) in w.Fire.Fires)
        {
            float d = (cell.Center - r.Position).LengthSquared();
            if (d < best) { best = d; nearest = cell; }
        }
        if (nearest is not Cell aim || r.Foam <= 0.01f) return ToilStatus.Succeeded;
        r.Facing = Vector2.Normalize(aim.Center - r.Position + new Vector2(0.0001f, 0f));
        w.Fire.Suppress(aim, 1.4f, RobotSystem.FoamRate / SimTime.TicksPerHour);
        r.Foam = MathF.Max(0f, r.Foam - 1f / (RobotSystem.FoamHours * SimTime.TicksPerHour));
        return ToilStatus.Running;
    }
}

// ─────────────────────────────── 로봇 체계 ───────────────────────────────

public sealed class RobotSystem
{
    private readonly World _world;
    public List<Robot> Robots { get; } = new();

    public int JobsDone { get; private set; }
    public int Breakdowns { get; private set; }
    public int Stalls { get; private set; }
    public int FiresFought { get; private set; }
    public int Patrols { get; private set; }

    /// <summary>1틱에 움직이는 칸 수 (사람 0.1).</summary>
    public static float Speed(RobotKind k) => k switch { RobotKind.Hauler => 0.085f, RobotKind.Safety => 0.09f, _ => 0.075f };

    /// <summary>같은 일에 드는 시간 배율 (사람의 보통 솜씨 = 1). 로봇은 꾸준하지만 느리다.</summary>
    public static float WorkFactor(RobotKind k) => k switch { RobotKind.Gardener => 1.25f, RobotKind.Maintainer => 1.4f, _ => 1.3f };

    /// <summary>시간당 배터리 소모: 움직일 때 · 일할 때 · 밖에서 기다릴 때.</summary>
    public const float DrainMove = 0.15f, DrainWork = 0.17f, DrainIdle = 0.03f, DrainSpray = 0.3f;
    public const float ChargePerHour = 0.3f;
    public const float FoamRate = 11f;      // 시간당 불 줄이는 양 (소화기 든 사람 10~16)
    public const float FoamHours = 0.6f;    // 가득 찬 거품으로 뿌릴 수 있는 시간
    public const float FoamRefillPerHour = 0.5f;

    /// <summary>사람 옆에서 거들면 그 사람의 긴 일이 이만큼 빨라진다.</summary>
    public const float AssistBonus = 0.35f;

    public static string KindName(RobotKind k) => k switch
    {
        RobotKind.Hauler => "운반 로봇",
        RobotKind.Maintainer => "정비 로봇",
        RobotKind.Gardener => "재배 로봇",
        RobotKind.Safety => "방재 로봇",
        _ => k.ToString(),
    };

    public static string FaultName(RobotFault f) => f switch
    {
        RobotFault.Drive => "구동 모터 고장",
        RobotFault.Sensor => "주행 센서 오차",
        RobotFault.Controller => "제어기 오류",
        RobotFault.Cell => "배터리 셀 열화",
        RobotFault.Scorched => "열에 그을림",
        _ => f.ToString(),
    };

    /// <summary>고장을 고치는 데 드는 부품 (없으면 빈 배열 — 다시 맞추기만).</summary>
    public static (ItemKind kind, int count)[] FaultParts(RobotFault f) => f switch
    {
        RobotFault.Drive => new[] { (ItemKind.Motor, 1) },
        RobotFault.Controller => new[] { (ItemKind.Electronics, 1) },
        RobotFault.Cell => new[] { (ItemKind.PowerController, 1) },
        RobotFault.Scorched => new[] { (ItemKind.Cable, 1), (ItemKind.Plate, 1) },
        _ => Array.Empty<(ItemKind, int)>(),
    };

    /// <summary>부품이 없을 때 임시로 되살리는 재료 (느리고 금방 다시 닳는다).</summary>
    public static (ItemKind kind, int count)[] MakeshiftParts(RobotFault f) => f switch
    {
        RobotFault.Drive => new[] { (ItemKind.Cable, 1), (ItemKind.Bearing, 1) },
        RobotFault.Controller => new[] { (ItemKind.Cable, 2) },
        RobotFault.Cell => new[] { (ItemKind.Cable, 1), (ItemKind.Electronics, 1) },
        _ => Array.Empty<(ItemKind, int)>(),
    };

    public static float FaultHours(RobotFault f) => f switch
    {
        RobotFault.Drive => 1.2f, RobotFault.Sensor => 0.4f, RobotFault.Controller => 0.8f, RobotFault.Cell => 1f, RobotFault.Scorched => 1f, _ => 1f,
    };

    public static Skill FaultSkill(RobotFault f) => f is RobotFault.Drive or RobotFault.Scorched ? Skill.Mechanics : Skill.Electrical;

    /// <summary>로봇이 맡는 작업 목록의 일.</summary>
    public static bool CanDo(RobotKind k, WorkKind w) => k switch
    {
        RobotKind.Hauler => w is WorkKind.Restock or WorkKind.StockDock or WorkKind.CarryWater or WorkKind.StockCache or WorkKind.StowCot,
        RobotKind.Maintainer => w is WorkKind.Maintain or WorkKind.FixLights,
        RobotKind.Gardener => w is WorkKind.Tend or WorkKind.Harvest,
        _ => false,
    };

    /// <summary>사람이 로봇이 하는 일에 합류할 수 있는 일 (같은 진척을 함께 채운다).</summary>
    public static bool Joinable(WorkOrder o) => o.Robot != null && o.Kind is WorkKind.Maintain or WorkKind.Tend or WorkKind.Harvest;

    /// <summary>정비 로봇이 옆에서 거들 수 있는 사람의 일 (오래 걸리는 손일).</summary>
    public static bool Assistable(WorkKind k) =>
        k is WorkKind.Repair or WorkKind.Fabricate or WorkKind.Upgrade or WorkKind.ReplacePanel or WorkKind.InstallSubstitute or WorkKind.RestoreGrade
            or WorkKind.RepairHull or WorkKind.ReplacePipe or WorkKind.LayBypass or WorkKind.BuildWorkshop or WorkKind.BuildComputer or WorkKind.RepairDoor
            or WorkKind.Maintain or WorkKind.MeltIce or WorkKind.ServiceDrone or WorkKind.PatchPipe or WorkKind.PreventiveCheck;

    /// <summary>로봇의 길: 숨 쉴 필요가 없어 진공도 지나가지만, 잠긴 격벽은 열지 못한다 (나가는 쪽만).</summary>
    public static readonly PathProfile Profile = new(0.3f, false, false, null, false, Robot: true);

    internal World World => _world;

    public RobotSystem(World world)
    {
        _world = world;
        // 충전대는 설계도에 그리지 않고, 배를 띄울 때 방마다 길을 막지 않는 벽가에 단다 (한 칸에 한 대)
        var kinds = world.Ship.FurnitureOf(FurnitureType.RobotDock).Any() ? null : InstallDocks(world);
        var docks = world.Ship.FurnitureOf(FurnitureType.RobotDock).OrderBy(f => f.Id).ToList();
        var plan = new List<(Furniture dock, int slot, RobotKind kind)>();
        foreach (var dock in docks)
            for (int s = 0; s < dock.Cells.Count; s++)
                plan.Add((dock, s, kinds != null && kinds.TryGetValue(dock, out var k) ? k : dock.Room.Type switch
                {
                    RoomType.Workshop => s % 2 == 0 ? RobotKind.Maintainer : RobotKind.Safety,
                    RoomType.Hydroponics => RobotKind.Gardener,
                    _ => RobotKind.Hauler,
                }));
        foreach (var (dock, slot, kind) in plan)
        {
            int same = plan.Count(p => p.kind == kind);
            int nth = Robots.Count(x => x.Kind == kind) + 1;
            var r = new Robot
            {
                Id = Robots.Count, Kind = kind, Dock = dock, Slot = slot,
                Name = same > 1 ? $"{KindName(kind)} {nth}" : KindName(kind),
                Condition = world.Rng.Range(0.85f, 1f),
            };
            r.Position = r.DockPosition;
            r.PreviousPosition = r.Position;
            r.Room = dock.Room;
            r.NextPatrol = world.Tick + SimTime.Hours(1 + r.Id % 3);
            Robots.Add(r);
        }
    }

    /// <summary>배 크기(침대 수)에 맞춘 로봇 구성.</summary>
    public static RobotKind[] Complement(int beds) => beds switch
    {
        <= 4 => new[] { RobotKind.Maintainer, RobotKind.Gardener },
        <= 6 => new[] { RobotKind.Maintainer, RobotKind.Gardener, RobotKind.Hauler },
        <= 12 => new[] { RobotKind.Maintainer, RobotKind.Safety, RobotKind.Gardener, RobotKind.Hauler, RobotKind.Hauler },
        <= 20 => new[] { RobotKind.Maintainer, RobotKind.Safety, RobotKind.Maintainer, RobotKind.Gardener, RobotKind.Hauler, RobotKind.Hauler, RobotKind.Gardener },
        _ => new[] { RobotKind.Maintainer, RobotKind.Safety, RobotKind.Maintainer, RobotKind.Gardener, RobotKind.Gardener, RobotKind.Hauler, RobotKind.Hauler, RobotKind.Hauler, RobotKind.Safety },
    };

    /// <summary>로봇 종류마다 충전대를 둘 방 (먼저 것부터, 자리가 없으면 다음).</summary>
    private static RoomType[] HomeRooms(RobotKind k) => k switch
    {
        RobotKind.Maintainer or RobotKind.Safety => new[] { RoomType.Workshop, RoomType.Storage, RoomType.Corridor },
        RobotKind.Gardener => new[] { RoomType.Hydroponics, RoomType.Galley, RoomType.Storage, RoomType.Corridor },
        _ => new[] { RoomType.Storage, RoomType.Galley, RoomType.Mess, RoomType.Lounge, RoomType.Corridor },
    };

    private static Dictionary<Furniture, RobotKind> InstallDocks(World w)
    {
        var ship = w.Ship;
        var result = new Dictionary<Furniture, RobotKind>();
        int beds = ship.Furniture.Count(f => f.Type == FurnitureType.Bed);
        foreach (var kind in Complement(beds))
        {
            foreach (var type in HomeRooms(kind))
            {
                Cell? spot = null;
                foreach (var room in ship.RoomsOf(type).OrderBy(r => r.Id))
                {
                    var cells = Adaptation.FreeCells(w, room)
                        .Where(c => Cell.Dirs4.Any(d => ship.Grid.Kind(c + d) == TileKind.Wall))           // 벽가에
                        .Where(c => Cell.Dirs4.Any(d => ship.IsOpenFloor(c + d) && ship.RoomAt(c + d) == room)) // 앞에 설 자리
                        .OrderBy(c => c.Y).ThenBy(c => c.X);
                    foreach (var c in cells)
                        if (Adaptation.SafeToBlock(w, room, c)) { spot = c; break; }
                    if (spot != null) break;
                }
                if (spot is not Cell at) continue;
                var dock = ship.AddFurniture(FurnitureType.RobotDock, at);
                result[dock] = kind;
                break;
            }
        }
        if (result.Count > 0) w.Paths.Invalidate();
        return result;
    }

    public string Summary
    {
        get
        {
            int ok = Robots.Count(r => r.Operational);
            int busy = Robots.Count(r => r.State == RobotState.Active);
            int down = Robots.Count(r => r.Fault != null || r.State is RobotState.Stalled or RobotState.Towed);
            return $"{ok}/{Robots.Count}" + (busy > 0 ? $" · 일 {busy}" : "") + (down > 0 ? $" · 멈춤 {down}" : "") + $" · 한 일 {JobsDone}";
        }
    }

    public static bool DockWorking(Robot r) => DockWorking(r.Dock);
    public static bool DockWorking(Furniture dock) => dock.Machine is Machine m && m.Powered && !m.Stopped && !dock.Room.Detached;

    // ─────────────────────────────── 매 틱: 움직임·단계 ───────────────────────────────

    public void Step()
    {
        var w = _world;
        foreach (var r in Robots)
        {
            r.PreviousPosition = r.Position;
            switch (r.State)
            {
                case RobotState.Docked:
                    r.Position = r.DockPosition;
                    break;
                case RobotState.Towed:
                    if (r.TowedBy is not CrewMember c || !c.CanAct || c.Job?.Order?.Target.Robot != r)
                    {
                        // 끌던 사람이 손을 놓았다 — 그 자리에 선다
                        r.TowedBy = null;
                        SetState(r, RobotState.Stalled);
                        break;
                    }
                    r.Position = c.Position - c.Facing * 0.55f;
                    break;
                case RobotState.Active:
                    if (r.Steps == null) break;
                    RunSteps(r, w);
                    break;
            }
            if (r.State != RobotState.Lost) r.Room = w.Ship.RoomAt(r.Cell) ?? (r.AtDock ? r.Dock.Room : r.Room);
        }
    }

    private void RunSteps(Robot r, World w)
    {
        var steps = r.Steps!;
        for (int guard = 0; guard < 6; guard++)
        {
            if (r.StepIndex >= steps.Count) { FinishTask(r, true); return; }
            var s = steps[r.StepIndex];
            var st = s.Tick(r, w);
            if (st == ToilStatus.Running) return;
            if (st != ToilStatus.Succeeded) { FinishTask(r, false); return; }
            r.StepIndex++;
            if (r.StepIndex >= steps.Count) { FinishTask(r, true); return; }
            steps[r.StepIndex].Begin(r, w);
            if (steps[r.StepIndex].Moving || steps[r.StepIndex] is RWork or RAssist or RSpray) return;
        }
    }

    internal static bool SetDestination(Robot r, World w, Cell goal)
    {
        var path = w.Paths.Find(r.Cell, goal, Profile);
        if (path == null) { r.Path = null; return false; }
        r.Path = path;
        r.PathIndex = 0;
        r.Goal = goal;
        return true;
    }

    /// <summary>한 틱 이동. 도착하면 true, 길이 끊겨 못 가면 false + Path = null.</summary>
    internal static bool Move(Robot r, World w)
    {
        var path = r.Path;
        if (path == null) return true;
        float budget = Speed(r.Kind) * (r.Fault == null && r.Battery > 0.02f ? 1f : 0.5f);
        bool repathed = false;
        while (budget > 0f && r.PathIndex < path.Count)
        {
            for (int k = r.PathIndex; k < Math.Min(path.Count, r.PathIndex + 2); k++)
                if (w.Ship.DoorAt(path[k]) is Door ahead && !ahead.Locked) ahead.Request();
            var next = path[r.PathIndex];
            var door = w.Ship.DoorAt(next);
            if (Locomotion.Blocked(w.Ship, next) || (door != null && door.Locked && !Leaving(r, door)))
            {
                var goal = r.Goal ?? path[^1];
                if (repathed || !SetDestination(r, w, goal)) { r.Path = null; return false; }
                path = r.Path!;
                repathed = true;
                continue;
            }
            if (door != null && door.Openness < 0.8f) break;
            var delta = next.Center - r.Position;
            float dist = delta.Length();
            if (dist > 0.0001f) r.Facing = delta / dist;
            if (dist <= budget)
            {
                r.Position = next.Center;
                budget -= dist;
                r.PathIndex++;
            }
            else
            {
                r.Position += delta / dist * budget;
                budget = 0f;
            }
        }
        if (r.PathIndex >= path.Count) { r.Path = null; return true; }
        return false;
    }

    private static bool Leaving(Robot r, Door d) => d.RoomA == r.Room || d.RoomB == r.Room;

    // ─────────────────────────────── 시스템 틱: 배터리·고장·판단 ───────────────────────────────

    public void SystemUpdate(float dt)
    {
        var w = _world;
        var charging = new HashSet<Furniture>();
        foreach (var r in Robots)
        {
            if (r.State == RobotState.Lost) continue;
            // 방째로 떨어져 나갔다
            if (r.Room is Room here && here.Detached)
            {
                Lose(r, here);
                continue;
            }
            switch (r.State)
            {
                case RobotState.Docked:
                {
                    if (DockWorking(r))
                    {
                        float eff = r.Dock.Machine!.Efficiency;
                        if (r.Battery < 1f) { r.Battery = MathF.Min(MaxCharge(r), r.Battery + ChargePerHour * eff * dt); charging.Add(r.Dock); }
                        if (r.Kind == RobotKind.Safety && r.Foam < 1f) { r.Foam = MathF.Min(1f, r.Foam + FoamRefillPerHour * eff * dt); charging.Add(r.Dock); }
                    }
                    if (r.Disabled) { r.Doing = "꺼 둠"; break; }
                    if (r.Fault != null) { r.Doing = $"{FaultName(r.Fault.Value)} — 수리를 기다린다"; break; }
                    if (!DockWorking(r) && r.Battery < 0.3f) { r.Doing = r.Dock.Machine!.Powered ? "충전대 멈춤 — 충전 못 함" : "충전대에 전기가 없다 — 충전 못 함"; break; }
                    if (r.Battery < 0.35f) { r.Doing = $"충전 {r.Battery * 100:0}%"; break; }
                    Decide(r);
                    if (r.State == RobotState.Docked) r.Doing = r.Battery < 0.99f && DockWorking(r) ? $"충전 {r.Battery * 100:0}% · 대기" : "대기";
                    break;
                }
                case RobotState.Active:
                {
                    bool working = r.Steps != null && r.StepIndex < r.Steps.Count && r.Steps[r.StepIndex] is RWork or RAssist or RSpray;
                    bool moving = r.Path != null;
                    float drain = r.Steps != null && r.StepIndex < r.Steps.Count && r.Steps[r.StepIndex] is RSpray ? DrainSpray
                        : moving ? DrainMove : working ? DrainWork : DrainIdle;
                    r.Battery = MathF.Max(0f, r.Battery - drain * dt);
                    r.ActiveHours += dt;
                    r.Condition = MathF.Max(0f, r.Condition - (working || moving ? 0.008f : 0.002f) * dt);
                    // 불 곁에서 그을리거나, 닳아서 고장
                    if (w.Fire.AnyWithin(r.Cell, 1.2f) && r.Kind != RobotKind.Safety && w.Rng.Chance(0.6f * dt)) { Break(r, RobotFault.Scorched); break; }
                    if (w.Fire.AnyWithin(r.Cell, 0.8f) && r.Kind == RobotKind.Safety) r.Condition = MathF.Max(0f, r.Condition - 0.05f * dt);
                    float wearRisk = 0.0025f + 0.045f * (1f - r.Condition) * (1f - r.Condition);
                    if (w.Rng.Chance(wearRisk * dt)) { Break(r, PickFault(r)); break; }
                    if (r.Battery <= 0.001f) { Stall(r, "배터리가 바닥났다"); break; }
                    // 맡은 일이 사라졌으면 (누가 끝냈거나 조건이 없어졌다) 손을 놓는다
                    if (r.Order != null && (r.Order.Closed || w.Tick - r.Order.LastSeen > SimTime.Minutes(12)))
                    {
                        Abort(r, null);
                        break;
                    }
                    // 배터리가 모자라면 돌아간다 (돌아갈 만큼 남기고) — 맡은 일이든, 사람을 거들든, 순찰이든
                    if (!r.Homing && r.Steps != null && r.Battery < ReturnCost(r) + 0.05f) { Abort(r, "배터리가 모자라 충전대로 돌아간다"); break; }
                    if (r.Steps == null) Decide(r);
                    break;
                }
                case RobotState.Stalled:
                    r.Doing = r.Fault != null ? $"{FaultName(r.Fault.Value)} — 멈춰 섰다 ({r.Room?.Name ?? "?"})" : $"방전 — 멈춰 섰다 ({r.Room?.Name ?? "?"})";
                    break;
            }
        }
        foreach (var dock in w.Ship.FurnitureOf(FurnitureType.RobotDock))
            if (dock.Machine != null) dock.Machine.Active = charging.Contains(dock);
    }

    /// <summary>배터리 셀이 열화하면 끝까지 차지 않는다.</summary>
    private static float MaxCharge(Robot r) => 0.55f + 0.45f * MathF.Min(1f, 0.3f + r.Condition);

    private RobotFault PickFault(Robot r)
    {
        float x = _world.Rng.Float();
        return x < 0.35f ? RobotFault.Drive : x < 0.65f ? RobotFault.Sensor : x < 0.85f ? RobotFault.Controller : RobotFault.Cell;
    }

    /// <summary>충전대까지 돌아가는 데 드는 배터리 (곧은 거리 × 2 — 통로를 돌아가고 문 앞에서 기다린다 — 에 여유).</summary>
    private static float ReturnCost(Robot r)
    {
        float dist = MathF.Abs(r.DockPosition.X - r.Position.X) + MathF.Abs(r.DockPosition.Y - r.Position.Y);
        dist = dist * 1.4f + 6f;
        return dist / (Speed(r.Kind) * SimTime.TicksPerHour) * DrainMove + 0.03f;
    }

    private void SetState(Robot r, RobotState s)
    {
        r.State = s;
        r.StateSince = _world.Tick;
    }

    // ─────────────────────────────── 일 고르기 ───────────────────────────────

    private void Decide(Robot r)
    {
        var w = _world;
        if (r.Disabled || r.Fault != null || r.Dock.Room.Detached) return;
        // 충전대가 떨어져 나갔거나 포기한 구획이면 나가지 않는다
        if (r.Dock.Room.Abandoned || r.Dock.Room.OffLimits) { if (!r.AtDock) GoHome(r, null); return; }
        float reserve = r.AtDock ? 0.35f : ReturnCost(r) + 0.18f;
        if (r.Battery < reserve) { if (!r.AtDock) GoHome(r, "충전하러"); return; }
        // 충전대에서 기다릴 때는 2분에 한 번만 생각한다 (거리장은 비싸다)
        if (r.AtDock && w.Tick < r.NextDecide) return;
        r.NextDecide = w.Tick + SimTime.Minutes(2);
        bool fire = r.Kind == RobotKind.Safety && r.Foam > 0.15f && w.Fire.Count > 0;
        bool work = w.Board.OpenForRobot().Any(o => CanDo(r.Kind, o.Kind));
        bool assist = r.Kind == RobotKind.Maintainer && w.Crew.Any(c => c.CanAct && c.Helper == null && c.Job?.Order is WorkOrder jo && Assistable(jo.Kind) && c.Job.Current is WorkToil);
        bool patrol = r.Kind == RobotKind.Safety && w.Tick >= r.NextPatrol && r.Battery > 0.7f;
        if (!fire && !work && !assist && !patrol) { if (!r.AtDock) GoHome(r, null); return; }

        var dist = w.Paths.Flood(r.Cell, Profile);

        // 방재 로봇: 불이 먼저
        if (r.Kind == RobotKind.Safety && r.Foam > 0.15f && PlanFire(r, dist)) return;

        WorkOrder? best = null;
        Cell bestSpot = default;
        float bestScore = float.MinValue;
        foreach (var o in w.Board.OpenForRobot())
        {
            if (!CanDo(r.Kind, o.Kind)) continue;
            // 원자로 정비는 사람 몫 (제어봉·계측을 손보는 기관 일이다)
            if (o.Kind == WorkKind.Maintain && o.Target.Furniture?.Type == FurnitureType.ReactorCore) continue;
            if (o.Target.CurrentRoom is Room room && (room.Abandoned || room.OffLimits || w.Fire.CountIn(room) > 0)) continue;
            if (Spot(r, o, dist) is not Cell spot) continue;
            float score = o.Urgency - dist.Get(spot) / 9000f;
            if (score > bestScore) { bestScore = score; best = o; bestSpot = spot; }
        }
        if (best != null)
        {
            var steps = Plan(r, best, bestSpot, dist, out string? blocked);
            if (steps != null)
            {
                best.Robot = r;
                r.Order = best;
                Begin(r, steps, best.Title);
                return;
            }
            if (blocked != null) w.Board.Block(best, null, 0.25f);
        }

        if (r.Kind == RobotKind.Maintainer && TryAssist(r, dist)) return;
        if (r.Kind == RobotKind.Safety && w.Tick >= r.NextPatrol && r.Battery > 0.7f && PlanPatrol(r, dist)) return;
        if (!r.AtDock) GoHome(r, null);
    }

    /// <summary>로봇이 일할 칸 (사람이 선 자리는 피하고, 가장 가까운 곳).</summary>
    private Cell? Spot(Robot r, WorkOrder o, DistanceField dist)
    {
        Cell? best = null;
        int bestCost = int.MaxValue;
        foreach (var s in o.Target.Spots(_world.Ship))
        {
            int d = dist.Get(s);
            if (d < 0) continue;
            if (_world.Crew.Any(c => !c.Dead && c.Cell == s)) d += 300;
            if (Robots.Any(x => x != r && x.State == RobotState.Active && x.Cell == s)) d += 300;
            if (d < bestCost) { best = s; bestCost = d; }
        }
        return best;
    }

    private static (Furniture? box, Cell spot) Nearest(World w, DistanceField dist, Func<Furniture, bool> match)
    {
        Furniture? best = null;
        Cell bestSpot = default;
        int bestCost = int.MaxValue;
        foreach (var f in w.Ship.Containers)
        {
            if (!match(f)) continue;
            foreach (var s in f.UseSpots)
            {
                int d = dist.Get(s);
                if (d < 0 || d >= bestCost) continue;
                best = f; bestSpot = s; bestCost = d;
            }
        }
        return (best, bestSpot);
    }

    private List<RobotStep>? Plan(Robot r, WorkOrder o, Cell at, DistanceField dist, out string? blocked)
    {
        blocked = null;
        var w = _world;
        var steps = new List<RobotStep>();
        switch (o.Kind)
        {
            case WorkKind.Maintain:
            {
                var f = o.Target.Furniture!;
                var m = f.Machine!;
                bool full = !Adaptation.Rationed(w, m);
                if (full && m.Spec.ServiceItem is ItemKind item)
                {
                    var (box, spot) = Nearest(w, dist, b => b.Storage!.Count(item) > 0);
                    if (box == null) full = false;
                    else { steps.Add(new RGoto(spot)); steps.Add(new RTake(box, item, 1)); }
                }
                steps.Add(new RGoto(at));
                steps.Add(new RWork(m.Spec.ServiceHours * (full ? 1f : 0.6f), o, f.Center));
                bool fullService = full;
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed) return true;
                    // 소모품이 드는 설비는 소모품을 써야 제대로 된 정비다 (없으면 점검·청소만)
                    bool used = fullService && m.Spec.ServiceItem == null;
                    if (fullService && m.Spec.ServiceItem is ItemKind it && rb.Cargo?.Kind == it) { rb.Cargo = rb.Cargo.Value.Count > 1 ? new ItemStack(it, rb.Cargo.Value.Count - 1) : null; used = true; }
                    m.Wear = used ? 0.12f : MathF.Max(0.35f, m.Wear - 0.2f);
                    m.LastServiced = world.Tick;
                    m.ServiceCount++;
                    world.Board.Close(o);
                    Done(rb, used ? null : $"{Ko.EulReul(m.Name)} 임시 정비했다 (소모품 없음)");
                    return true;
                }));
                return steps;
            }
            case WorkKind.FixLights:
            {
                var room = o.Target.Room!;
                var (box, spot) = Nearest(w, dist, b => b.Storage!.Count(ItemKind.Cable) > 0);
                if (box == null) { blocked = "케이블 없음"; return null; }
                steps.Add(new RGoto(spot));
                steps.Add(new RTake(box, ItemKind.Cable, 1));
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.6f, null, room.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    if (rb.Cargo?.Kind != ItemKind.Cable) return false;
                    rb.Cargo = null;
                    room.LightsOut = false;
                    world.Board.Close(o);
                    Done(rb, $"{room.Name} 조명을 갈았다");
                    return true;
                }));
                return steps;
            }
            case WorkKind.Tend:
            {
                var bed = o.Target.Furniture!;
                var crop = bed.Machine!.Crop!;
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.3f, o, bed.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed) return true;
                    crop.Care = MathF.Max(crop.Care, 0.9f);
                    world.Board.Close(o);
                    Done(rb, null);
                    return true;
                }));
                return steps;
            }
            case WorkKind.Harvest:
            {
                var bed = o.Target.Furniture!;
                var crop = bed.Machine!.Crop!;
                int expected = FoodChain.HarvestYield + 3;
                var (fridge, fridgeSpot) = Nearest(w, dist, f => f.Type == FurnitureType.Fridge && f.Storage!.Free >= expected);
                if (fridge == null) { blocked = "냉장고가 가득 참"; return null; }
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.35f, o, bed.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed || !crop.Ripe) return true;
                    // 로봇 솜씨는 보통 사람쯤 (0.5)
                    int yield = (int)MathF.Round(FoodChain.HarvestYield * FoodChain.BedSize(bed) * 1.0f * (0.8f + 0.2f * bed.Machine!.Condition));
                    crop.Growth = 0f;
                    rb.Cargo = new ItemStack(ItemKind.Produce, yield);
                    world.Board.Close(o);
                    Done(rb, $"{bed.Label}에서 채소 {yield}개를 거뒀다");
                    return true;
                }));
                steps.Add(new RGoto(_ => r.Cargo != null ? fridgeSpot : null));
                steps.Add(new RPut(fridge));
                return steps;
            }
            case WorkKind.Restock:
            {
                var dispenser = o.Target.Furniture!;
                int want = Math.Min(12, dispenser.Storage!.Free);
                var (fridge, fridgeSpot) = Nearest(w, dist, f => f.Type == FurnitureType.Fridge && f.Storage!.Count(ItemKind.Meal) > 0);
                if (fridge == null || want <= 0) { blocked = "냉장고에 식사 없음"; return null; }
                steps.Add(new RGoto(fridgeSpot));
                steps.Add(new RTake(fridge, ItemKind.Meal, want, partialOk: true));
                steps.Add(new RGoto(at));
                steps.Add(new RPut(dispenser));
                steps.Add(new RDo((rb, world) => { world.Board.Close(o); Done(rb, null); return true; }));
                steps.Add(new RGoto(_ => r.Cargo != null ? fridgeSpot : null));
                steps.Add(new RPut(fridge));
                return steps;
            }
            case WorkKind.StockDock:
            {
                var dock = o.Target.Furniture!;
                var kind = o.Product ?? ItemKind.Structure;
                int want = Math.Min((kind == ItemKind.Sealant ? 1 : 2) - dock.Storage!.Count(kind), dock.Storage.Free);
                var (shelf, shelfSpot) = Nearest(w, dist, f => f.Type == FurnitureType.Shelf && f.Storage!.Count(kind) > 0);
                if (shelf == null || want <= 0) { blocked = $"{ItemKinds.Name(kind)} 없음"; return null; }
                steps.Add(new RGoto(shelfSpot));
                steps.Add(new RTake(shelf, kind, want, partialOk: true));
                steps.Add(new RGoto(at));
                steps.Add(new RPut(dock));
                steps.Add(new RDo((rb, world) => { world.Board.Close(o); Done(rb, $"{dock.Label}에 {ItemKinds.Name(kind)}를 실었다"); return true; }));
                steps.Add(new RGoto(_ => r.Cargo != null ? shelfSpot : null));
                steps.Add(new RPut(shelf));
                return steps;
            }
            case WorkKind.StowCot:
            {
                var cot = o.Target.Furniture!;
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.3f, null, cot.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    if (cot.Owner != null || cot.Stowed) { world.Board.Close(o); return true; }
                    var room = cot.Room;
                    world.Ship.Stow(cot);
                    world.Paths.Invalidate();
                    world.Structure.Touch();
                    world.Adapt.CotsStowed++;
                    world.Board.Close(o);
                    if (!room.Furniture.Any(f => f.Type == FurnitureType.Cot) && room.Purpose != null && room.Purpose.StartsWith("임시 침실"))
                    {
                        if (!room.FormerPurposes.Contains("임시 침실")) room.FormerPurposes.Add("임시 침실");
                        room.Purpose = null;
                        MarkLog.Add(room.Marks, world.Tick, $"{rb.Name}: 마지막 간이침대를 접었다 — 원래 {room.Name}으로");
                    }
                    Done(rb, $"{room.Name}의 빈 간이침대를 접어 창고로 옮겼다");
                    return true;
                }));
                return steps;
            }
            case WorkKind.CarryWater:
                return Logistics.RobotCarryWater(this, r, o, at, dist, out blocked);
            case WorkKind.StockCache:
                return Logistics.RobotStockCache(this, r, o, at, dist, out blocked);
        }
        return null;
    }

    internal static (Furniture? box, Cell spot) NearestFor(World w, DistanceField dist, Func<Furniture, bool> match) => Nearest(w, dist, match);

    /// <summary>한 일을 기록한다.</summary>
    internal void Done(Robot r, string? what)
    {
        r.JobsDone++;
        JobsDone++;
        if (what != null) _world.Log.Add(_world.Tick, LogKind.Work, $"{r.Name}: {what}");
    }

    private void Begin(Robot r, List<RobotStep> steps, string what)
    {
        r.Homing = false;
        r.Steps = steps;
        r.StepIndex = 0;
        r.Doing = what;
        if (r.AtDock) SetState(r, RobotState.Active);
        steps[0].Begin(r, _world);
    }

    /// <summary>일이 끝났다 (성공이든 실패든): 짐은 둘 곳에 두고, 다음 일을 찾는다.</summary>
    private void FinishTask(Robot r, bool ok)
    {
        var w = _world;
        bool wasHome = r.Homing;
        r.Homing = false;
        if (r.Order is WorkOrder o)
        {
            if (o.Robot == r) o.Robot = null;
            if (!ok && !o.Closed) w.Board.Block(o, null, 0.1f);
        }
        if (r.Helping is CrewMember h && h.Helper == r) h.Helper = null;
        r.Helping = null;
        r.Order = null;
        r.Steps = null;
        r.Path = null;
        if (r.State == RobotState.Docked || wasHome) return;
        if (r.Cargo != null) { StowCargo(r); return; }
        r.IdleSince = w.Tick;
        Decide(r);
    }

    /// <summary>들고 있는 걸 둘 곳에 두고 온다 (없으면 충전대 옆 선반, 그것도 없으면 그 자리에 내려놓는다 — 사라지지 않게 가까운 보관함에).</summary>
    private void StowCargo(Robot r)
    {
        var w = _world;
        var held = r.Cargo!.Value;
        var dist = w.Paths.Flood(r.Cell, Profile);
        var (box, spot) = Nearest(w, dist, f => f.Storage!.Accepts(held.Kind) && f.Storage.Free > 0);
        if (box == null)
        {
            foreach (var f in w.Ship.Containers)
            {
                int put = f.Storage!.Add(held.Kind, held.Count);
                held = new ItemStack(held.Kind, held.Count - put);
                if (held.Count <= 0) break;
            }
            r.Cargo = null;
            GoHome(r, null);
            return;
        }
        Begin(r, new List<RobotStep> { new RGoto(spot), new RPut(box) }, $"{held} 두러 간다");
    }

    /// <summary>하던 일을 내려놓는다 (진척은 작업 목록에 남는다). why가 있으면 기록.</summary>
    public void Abort(Robot r, string? why)
    {
        var w = _world;
        if (r.Order is WorkOrder o && o.Robot == r) o.Robot = null;
        if (r.Helping is CrewMember h && h.Helper == r) h.Helper = null;
        r.Helping = null;
        r.Order = null;
        r.Steps = null;
        r.Path = null;
        if (why != null) w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name}: {why}");
        if (r.State != RobotState.Active) return;
        if (r.Cargo != null && r.Battery > 0.05f) { StowCargo(r); return; }
        GoHome(r, why != null ? "충전하러" : null);
    }

    private void GoHome(Robot r, string? why)
    {
        var w = _world;
        if (r.AtDock || r.State != RobotState.Active) return;
        var spots = r.Dock.UseSpots.Where(s => w.Ship.IsWalkable(s)).ToList();
        if (spots.Count == 0) spots = r.Dock.Cells.SelectMany(c => Cell.Dirs4.Select(d => c + d)).Where(w.Ship.IsWalkable).ToList();
        var dist = w.Paths.Flood(r.Cell, Profile);
        var spot = spots.Where(s => dist.Reachable(s)).OrderBy(s => dist.Get(s)).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell at)
        {
            r.Doing = "충전대로 가는 길이 막혔다 — 기다린다";
            r.Steps = null;
            return;
        }
        r.Steps = new List<RobotStep>
        {
            new RGoto(at),
            new RDo((rb, world) =>
            {
                rb.Position = rb.DockPosition;
                rb.Path = null;
                rb.Homing = false;
                SetState(rb, RobotState.Docked);
                rb.Doing = "대기";
                return true;
            }),
        };
        r.Homing = true;
        r.StepIndex = 0;
        r.Doing = "충전대로" + (why != null ? $" ({why})" : "");
        r.Steps[0].Begin(r, w);
    }

    // ─────────────────────────────── 거들기 · 불 · 순찰 ───────────────────────────────

    private bool TryAssist(Robot r, DistanceField dist)
    {
        var w = _world;
        CrewMember? who = null;
        Cell spot = default;
        int bestCost = int.MaxValue;
        foreach (var c in w.Crew)
        {
            if (!c.CanAct || c.Helper != null || c.Outside || c.Job?.Order is not WorkOrder o || !Assistable(o.Kind)) continue;
            if (c.Job.Current is not WorkToil wt || (wt.Progress ?? 0f) > 0.85f) continue;
            if (c.Room == null || c.Room.Abandoned || w.Fire.CountIn(c.Room) > 0) continue;
            foreach (var d in Cell.Dirs8)
            {
                var s = c.Cell + d;
                if (!w.Ship.IsOpenFloor(s) || w.Ship.RoomAt(s) != c.Room) continue;
                if (w.Crew.Any(x => !x.Dead && x.Cell == s) || Robots.Any(x => x != r && x.Cell == s && x.State == RobotState.Active)) continue;
                int dd = dist.Get(s);
                if (dd < 0 || dd >= bestCost) continue;
                bestCost = dd; who = c; spot = s;
            }
        }
        if (who == null || bestCost > 900) return false;
        var job = who.Job!;
        Begin(r, new List<RobotStep> { new RGoto(spot), new RAssist(who, job) }, $"{Ko.EulReul(who.Name)} 거들러 간다");
        return true;
    }

    private bool PlanFire(Robot r, DistanceField dist)
    {
        var w = _world;
        Cell? best = null;
        int bestCost = int.MaxValue;
        foreach (var (cell, _) in w.Fire.Fires)
        {
            var room = w.Ship.RoomAt(cell);
            if (room == null || room.Detached || room.Abandoned) continue;
            foreach (var d in Cell.Dirs8)
            {
                var s = cell + d;
                if (!w.Ship.IsWalkable(s) || w.Fire.At(s) > 0f) continue;
                int dd = dist.Get(s);
                if (dd < 0 || dd >= bestCost) continue;
                bestCost = dd; best = s;
            }
        }
        if (best is not Cell at) return false;
        FiresFought++;
        var room0 = w.Ship.RoomAt(at);
        Begin(r, new List<RobotStep>
        {
            new RGoto(at),
            new RSpray(),
            new RDo((rb, world) => { Done(rb, $"{room0?.Name ?? "?"} 불에 거품을 뿌렸다 (거품 {rb.Foam * 100:0}%)"); return true; }),
        }, $"{room0?.Name ?? "?"} 불 끄러 간다");
        return true;
    }

    /// <summary>방재 로봇의 순찰: 설비가 있는 방을 몇 곳 돌며 들여다본다 (불·사고 전조를 먼저 본다).</summary>
    private bool PlanPatrol(Robot r, DistanceField dist)
    {
        var w = _world;
        var rooms = w.Ship.LiveRooms
            .Where(x => !x.Abandoned && !x.OffLimits && x.Type != RoomType.Corridor && x.Furniture.Any(f => f.Machine != null))
            .OrderBy(x => w.Robots.LastPatrolled.GetValueOrDefault(x.Id, -1_000_000))
            .ThenBy(x => x.Id)
            .Take(4).ToList();
        var steps = new List<RobotStep>();
        foreach (var room in rooms)
        {
            var spot = room.Cells.Where(w.Ship.IsOpenFloor).Where(dist.Reachable).OrderBy(c => (c.Center - room.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
            if (spot is not Cell s) continue;
            steps.Add(new RGoto(s));
            steps.Add(new RWork(0.1f, null, room.Center));
            var inspected = room;
            steps.Add(new RDo((rb, world) => { world.Robots.Inspect(rb, inspected); return true; }));
        }
        if (steps.Count == 0) return false;
        r.NextPatrol = w.Tick + SimTime.Hours(3);
        Patrols++;
        Begin(r, steps, "순찰 — " + string.Join(" · ", rooms.Select(x => x.Name)));
        return true;
    }

    /// <summary>방마다 마지막으로 순찰한 틱.</summary>
    public Dictionary<int, long> LastPatrolled { get; } = new();

    /// <summary>순찰로 방을 들여다봤다: 모르던 불을 알리고, 사고 전조를 찾는다 (v11.0).</summary>
    internal void Inspect(Robot r, Room room)
    {
        var w = _world;
        LastPatrolled[room.Id] = w.Tick;
        Prevention.Inspect(w, room, r.Name, robot: true);
    }

    /// <summary>불을 본 로봇 (방재 로봇이면 경보가 없어도 알린다).</summary>
    public Robot? Witness(Room room) =>
        Robots.FirstOrDefault(r => r.Operational && r.State == RobotState.Active && r.Room == room && r.Kind == RobotKind.Safety);

    // ─────────────────────────────── 시험용 ───────────────────────────────

    /// <summary>시험·화면 확인용: 로봇을 고장 낸다.</summary>
    public void ForceFault(Robot r, RobotFault f) => Break(r, f);

    /// <summary>시험·화면 확인용: 로봇을 그 칸에서 방전된 채 멈춰 세운다.</summary>
    public void ForceStall(Robot r, Cell at)
    {
        DropTask(r);
        r.Position = at.Center;
        r.PreviousPosition = r.Position;
        r.Room = _world.Ship.RoomAt(at);
        r.Battery = 0f;
        SetState(r, RobotState.Active);
        Stall(r, "배터리가 바닥났다 (시험)");
    }

    // ─────────────────────────────── 고장 · 멈춤 · 잃음 ───────────────────────────────

    private void Break(Robot r, RobotFault f)
    {
        var w = _world;
        r.Fault = f;
        r.Breakdowns++;
        Breakdowns++;
        MarkLog.Add(r.Marks, w.Tick, $"{FaultName(f)} ({r.Room?.Name ?? "?"})");
        w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name} {FaultName(f)} — {r.Room?.Name ?? "?"}에 멈춰 섰다" + (r.Order != null ? $" (하던 일 {r.Order.Title}은 작업 목록으로)" : ""));
        DropTask(r);
        SetState(r, RobotState.Stalled);
        w.Board.RequestScan();
    }

    private void Stall(Robot r, string why)
    {
        var w = _world;
        Stalls++;
        MarkLog.Add(r.Marks, w.Tick, $"{why} ({r.Room?.Name ?? "?"})");
        w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name}: {why} — {r.Room?.Name ?? "?"}에 멈춰 섰다");
        DropTask(r);
        SetState(r, RobotState.Stalled);
        w.Board.RequestScan();
    }

    /// <summary>일을 놓는다 (진척은 작업 목록에, 짐은 가까운 보관함에 — 사람이 가져간 셈).</summary>
    private void DropTask(Robot r)
    {
        var w = _world;
        if (r.Order is WorkOrder o && o.Robot == r) o.Robot = null;
        if (r.Helping is CrewMember h && h.Helper == r) h.Helper = null;
        r.Helping = null;
        r.Order = null;
        r.Steps = null;
        r.Path = null;
        if (r.Cargo is ItemStack held)
        {
            int left = held.Count;
            foreach (var f in w.Ship.Containers.OrderBy(f => (f.Center - r.Position).LengthSquared()))
            {
                left -= f.Storage!.Add(held.Kind, left);
                if (left <= 0) break;
            }
            r.Cargo = null;
        }
    }

    private void Lose(Robot r, Room room)
    {
        var w = _world;
        DropTask(r);
        r.TowedBy = null;
        SetState(r, RobotState.Lost);
        r.Doing = $"{Ko.WaGwa(room.Name)} 함께 떨어져 나갔다";
        MarkLog.Add(r.Marks, w.Tick, $"{Ko.WaGwa(room.Name)} 함께 떨어져 나갔다");
        w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(r.Name)} {Ko.WaGwa(room.Name)} 함께 떨어져 나갔다", room, log: true);
    }

    // ─────────────────────────────── 사람이 하는 일 (수리·끌어오기·정비) ───────────────────────────────

    /// <summary>사람이 고쳤다.</summary>
    internal void Repaired(Robot r, CrewMember by, bool makeshift)
    {
        var w = _world;
        var f = r.Fault;
        r.Fault = null;
        r.Condition = MathF.Max(r.Condition, makeshift ? 0.45f : 0.85f);
        MarkLog.Add(r.Marks, w.Tick, $"{Ko.IGa(by.Name)} {(makeshift ? "임시로 " : "")}고쳤다 ({(f is RobotFault ff ? FaultName(ff) : "")})");
        if (r.State == RobotState.Stalled)
        {
            if (r.Battery > 0.08f) { SetState(r, RobotState.Active); GoHome(r, "고쳐져 충전대로"); }
        }
    }

    /// <summary>사람이 충전대까지 끌고 왔다.</summary>
    internal void Returned(Robot r)
    {
        r.TowedBy = null;
        r.Position = r.DockPosition;
        r.Path = null;
        r.Steps = null;
        r.Fetched++;
        SetState(r, RobotState.Docked);
        r.Doing = r.Fault != null ? $"{FaultName(r.Fault.Value)} — 수리를 기다린다" : "충전";
    }

    internal void StartTow(Robot r, CrewMember c)
    {
        r.TowedBy = c;
        SetState(r, RobotState.Towed);
        r.Doing = $"{Ko.IGa(c.Name)} 충전대로 끌고 간다";
    }

    internal void Serviced(Robot r)
    {
        r.Condition = 0.97f;
        MarkLog.Add(r.Marks, _world.Tick, "정비 (윤활·조임·센서 청소)");
    }
}
