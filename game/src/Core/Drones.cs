using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

public enum DroneKind { Inspect, Repair, Tow, Build }

public enum DroneState
{
    Docked,     // 거치대에서 충전·대기
    Outbound,   // 일하러 나가는 중
    Working,    // 선체 밖에서 일하는 중
    Returning,  // 돌아오는 중
    Towing,     // 떨어진 조각(또는 떠내려간 드론)을 끌고 오는 중
    Adrift,     // 배터리가 바닥났거나 부서져 떠다닌다 (견인 드론이 건져 와야 한다)
    Lost,       // 너무 멀리 떠내려가 잃었다
}

/// <summary>
/// 외부 드론 한 대 (v8). 에어락의 거치대에서 충전하고, 외부 해치로 나가 선체 밖 일을 한다.
/// 전기(충전)와 정비(부품)와 자재(승무원이 거치대에 보급)가 있어야 일할 수 있다 — 승무원이 드론을 돌보고, 드론이 선체를 돌본다.
/// </summary>
public sealed class Drone
{
    public int Id { get; init; }
    public DroneKind Kind { get; init; }
    public string Name { get; init; } = "";
    /// <summary>v12.5 이 한 대의 버릇.</summary>
    public Quirk Quirk => Quirks.Of(Id, (int)Kind);
    public Furniture Dock { get; init; } = null!;
    public int Slot { get; init; }

    public Vector2 Position { get; set; }
    public Vector2 PreviousPosition { get; set; }
    public Vector2 Facing { get; set; } = new(0, 1);

    public DroneState State { get; internal set; }
    public long StateSince { get; internal set; }

    /// <summary>배터리 0~1.</summary>
    public float Battery { get; set; } = 1f;

    /// <summary>상태 0~1 (닳으면 고장이 잦아진다. 정비로 회복).</summary>
    public float Condition { get; set; } = 1f;

    /// <summary>고장: 거치대에서 고쳐야 다시 나간다.</summary>
    public bool Faulty { get; set; }

    /// <summary>부서졌다 (파편): 건져 와서 다시 짜 맞춰야 한다.</summary>
    public bool Wrecked { get; set; }

    /// <summary>맡은 일.</summary>
    public WorkOrder? Order { get; internal set; }

    /// <summary>끌고 오는 조각.</summary>
    public Fragment? Towing { get; internal set; }

    /// <summary>건지러 가는/건져 오는 드론.</summary>
    public Drone? Fetching { get; internal set; }

    /// <summary>싣고 나간 자재.</summary>
    public (ItemKind kind, int count)[] Cargo { get; internal set; } = Array.Empty<(ItemKind, int)>();

    internal List<Vector2> Route { get; } = new();
    internal int RouteIndex { get; set; }
    internal float WorkDone { get; set; }
    internal float WorkNeeded { get; set; }
    internal Vector2 Attach { get; set; }
    internal Vector2 DriftVelocity { get; set; }

    /// <summary>검사 순회: 들를 연결부들.</summary>
    internal List<Joint> Patrol { get; } = new();
    internal int PatrolIndex { get; set; }

    public string Doing { get; internal set; } = "대기";
    public float FlightHours { get; internal set; }
    public int Sorties { get; internal set; }
    public List<Mark> Marks { get; } = new();

    public bool Outside => State is not DroneState.Docked;
    public bool Operational => !Wrecked && !Faulty && State != DroneState.Lost && State != DroneState.Adrift && !Dock.Room.Detached;
    public float WorkProgress => WorkNeeded > 0 ? Math.Clamp(WorkDone / WorkNeeded, 0f, 1f) : 0f;

    public Vector2 DockPosition => Dock.Center + new Vector2(0f, (Slot - 1) * 0.55f);

    public string KindName => DroneSystem.KindName(Kind);
}

/// <summary>
/// 외부 드론 (리뷰어 안 10): 검사 드론은 외벽을 돌며 상한 연결부를 찾고, 수리 드론은 밖에서 연결부를 잇고,
/// 견인 드론은 떨어져 나간 방을 끌어오고, 건설 드론은 잃은 골조를 다시 세우고 임시 트러스를 덧댄다.
/// 드론이 없거나 멈추면 승무원이 우주복을 입고 나가야 한다 (EVA: 느리고, 위험하고, 무섭다).
/// </summary>
public sealed class DroneSystem
{
    private readonly World _world;
    public List<Drone> Drones { get; } = new();

    public int Sorties { get; private set; }
    public int JobsDone { get; private set; }
    public int Inspections { get; private set; }
    public int Tows { get; private set; }
    public int Losses { get; private set; }

    /// <summary>v9.2: 관제가 끊겼을 때 거치대 콘솔에서 드론을 손으로 모는 사람.</summary>
    public CrewMember? Pilot { get; set; }

    /// <summary>손 조종이 이어지는 동안 (자리를 잠깐 떠도 이만큼은 봐준다).</summary>
    public long ManualUntil { get; private set; } = -1;

    public int ManualSorties { get; private set; }

    /// <summary>드론이 움직일 수 있다: 주 컴퓨터의 관제, 아니면 사람이 콘솔에서.</summary>
    public bool Controlled => _world.Automation.DroneControl || _world.Tick < ManualUntil;

    /// <summary>사람이 몰고 있다 (지금 콘솔에 앉아 있다).</summary>
    public bool Manual => !_world.Automation.DroneControl && Pilot is { CanAct: true };

    public void TakeControl(CrewMember c)
    {
        var w = _world;
        bool first = Pilot == null && w.Tick >= ManualUntil;
        Pilot = c;
        ManualUntil = w.Tick + SimTime.Minutes(15);
        if (first) w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(c.Name)} 드론 거치대 콘솔에 앉아 드론을 손으로 몬다 — 관제가 끊겼다", c.Room, new[] { c });
    }

    /// <summary>손으로라도 드론을 내보낼 일이 있나 (까닭, 급한 정도). 밖에 나가 있는 드론이 있으면 돌아올 때까지 앉아 있어야 한다.</summary>
    public (string why, float urgency)? ManualWork()
    {
        var w = _world;
        var ready = Drones.Where(d => d.Operational && d.State == DroneState.Docked && !d.Dock.Room.Detached).ToList();
        var outside = Drones.FirstOrDefault(d => d.State is DroneState.Outbound or DroneState.Working or DroneState.Towing);
        var frag = TowNeeded() ?? outside?.Towing ?? (outside?.Order?.Kind == WorkKind.Retrieve ? outside.Order.Target.Room?.Fragment : null);
        if (frag != null)
        {
            bool people = w.Crew.Any(c => !c.Dead && c.Room == frag.Room);
            return ($"떨어져 나간 {Ko.EulReul(frag.Room.Name)} 끌어와야 한다" + (people ? " (사람이 타고 있다)" : ""), people ? 1.1f : 0.8f);
        }
        // 밖의 일(연결부·골조·방열판)은 급한 사고(파공·불·격벽)보다 뒤다 — 콘솔에 붙잡혀 배가 새는 걸 놓치지 않게
        if (outside != null) return ($"{Ko.IGa(outside.Name)} 밖에 있다 — {outside.Doing}", 0.55f);
        if (Drones.Any(d => d.State == DroneState.Adrift) && ready.Any(d => d.Kind == DroneKind.Tow))
            return ("떠내려가는 드론을 건져야 한다", 0.5f);
        var job = OtherDroneJob(ready);
        if (job != null) return ($"{job.Title}", MathF.Min(0.55f, 0.1f + job.Urgency * 0.5f));
        return null;
    }

    /// <summary>끌어와야 할 조각 (되찾기로 정했고, 아직 아무도 붙잡지 않았고, 나갈 견인 드론이 있다).</summary>
    private Fragment? TowNeeded()
    {
        var w = _world;
        if (!Drones.Any(d => d.Kind == DroneKind.Tow && d.Operational && d.State == DroneState.Docked && !d.Dock.Room.Detached)) return null;
        foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.Retrieve && Council.Cleared(o)))
            if (o.Target.Room?.Fragment is Fragment f && f.State is not (FragmentState.Lost or FragmentState.Moored) && f.Tug == null
                && !Drones.Any(d => d.Order == o))
                return f;
        return null;
    }

    private WorkOrder? OtherDroneJob(List<Drone> ready) =>
        _world.Board.Open.Where(o => o.Kind != WorkKind.Retrieve && Council.Cleared(o) && o.Drone == null && ready.Any(d => CanDo(d.Kind, o.Kind)))
            .OrderByDescending(o => o.Urgency).FirstOrDefault();

    public const float ChargePerHour = 0.5f;

    public static string KindName(DroneKind k) => k switch
    {
        DroneKind.Inspect => "검사 드론",
        DroneKind.Repair => "수리 드론",
        DroneKind.Tow => "견인 드론",
        DroneKind.Build => "건설 드론",
        _ => k.ToString(),
    };

    /// <summary>1틱에 나는 칸 수.</summary>
    public static float Speed(DroneKind k) => k switch
    {
        // v12.5 드론 개편: 2.5배 빠르게 (사람보다 한참 빠른 손)
        DroneKind.Inspect => 0.22f,
        DroneKind.Repair => 0.18f,
        DroneKind.Build => 0.15f,
        _ => 0.15f,
    };

    /// <summary>끌고 올 때 속도 (시간당 약 8칸).</summary>
    public const float TowSpeed = 0.011f; // v12.5 두 배

    /// <summary>시간당 배터리 소모 (날 때).</summary>
    public static float Drain(DroneKind k) => k switch
    {
        DroneKind.Inspect => 0.2f,
        DroneKind.Repair => 0.18f,
        DroneKind.Build => 0.18f,
        _ => 0.16f,
    };

    public static bool CanDo(DroneKind d, WorkKind k) => d switch
    {
        DroneKind.Inspect => k is WorkKind.InspectHull,
        DroneKind.Repair => k is WorkKind.RepairJoint or WorkKind.ReleaseJoint or WorkKind.Clamp or WorkKind.RepairRadiator,
        DroneKind.Build => k is WorkKind.RebuildFrame or WorkKind.InstallTruss or WorkKind.Clamp or WorkKind.RepairJoint or WorkKind.ReleaseJoint
            or WorkKind.RepairRadiator,
        DroneKind.Tow => k is WorkKind.Retrieve,
        _ => false,
    };

    /// <summary>드론이 그 일을 하는 데 걸리는 시간.</summary>
    public static float WorkHours(WorkKind k) => k switch
    {
        // v12.5 드론 개편: 작업 40% 빠르게
        WorkKind.RepairJoint => 0.6f,
        WorkKind.RebuildFrame => 0.9f,
        WorkKind.InstallTruss => 1.2f,
        WorkKind.Clamp => 0.45f,
        WorkKind.ReleaseJoint => 0.2f,
        WorkKind.Retrieve => 0.15f,
        WorkKind.RepairRadiator => 0.6f,
        _ => 0.05f,
    };

    public DroneSystem(World world)
    {
        _world = world;
        var docks = world.Ship.Furniture.Where(f => f.Type == FurnitureType.DroneDock).OrderBy(f => f.MinX).ToList();
        var plan = new List<(int dock, DroneKind kind)>();
        if (docks.Count > 0) { plan.Add((0, DroneKind.Inspect)); plan.Add((0, DroneKind.Repair)); plan.Add((0, DroneKind.Tow)); }
        if (docks.Count > 1) { plan.Add((1, DroneKind.Repair)); plan.Add((1, DroneKind.Build)); }
        else if (docks.Count == 1) plan.Add((0, DroneKind.Build));
        var slots = new Dictionary<int, int>();
        foreach (var (di, kind) in plan)
        {
            int slot = slots.GetValueOrDefault(di);
            slots[di] = slot + 1;
            int same = plan.Count(p => p.kind == kind);
            int nth = Drones.Count(x => x.Kind == kind) + 1;
            var d = new Drone
            {
                Id = Drones.Count, Kind = kind, Dock = docks[di], Slot = slot,
                Name = same > 1 ? $"{KindName(kind)} {nth}" : KindName(kind),
                Condition = world.Rng.Range(0.85f, 1f),
            };
            d.Position = d.DockPosition;
            d.PreviousPosition = d.Position;
            Drones.Add(d);
        }
        // 거치대 자재칸: 처음부터 조금 실려 있다
        foreach (var dock in docks)
        {
            dock.Storage!.Add(ItemKind.Structure, 2);
            dock.Storage.Add(ItemKind.Plate, 2);
            dock.Storage.Add(ItemKind.Sealant, 1);
        }
    }

    public bool Has(DroneKind k) => Drones.Any(d => d.Kind == k && d.Operational);

    /// <summary>검사 드론이 곧 나갈 수 있는지.</summary>
    public bool CanInspect() => Drones.Any(d => d.Kind == DroneKind.Inspect && d.Operational && DockWorking(d));

    /// <summary>이 일을 드론이 맡을 수 있는지 (승무원이 EVA로 나설 필요가 없는지).</summary>
    public bool WillHandle(WorkOrder o)
    {
        if (!o.External && o.Kind != WorkKind.Retrieve) return false;
        // 급한데 한 시간 넘게 아무 드론도 못 맡았으면 사람이 나간다
        if (o.Urgency >= 1.0f && _world.Tick - o.Posted > SimTime.Hours(1) && o.Drone == null) return false;
        foreach (var d in Drones)
        {
            if (!CanDo(d.Kind, o.Kind) || !d.Operational) continue;
            if (d.State == DroneState.Docked && !DockWorking(d) && d.Battery < 0.4f) continue;
            if (!HasMaterials(d, Materials(o))) continue;
            return true;
        }
        return false;
    }

    public static bool DockWorking(Drone d) => d.Dock.Machine is Machine m && m.Powered && !m.Stopped;

    /// <summary>드론이 싣고 나갈 자재 (거치대 자재칸에서).</summary>
    public static (ItemKind kind, int count)[] Materials(WorkOrder o) => o.Kind switch
    {
        WorkKind.RepairJoint => o.Target.Joint is Joint j && j.Known < 0.5f
            ? new[] { (ItemKind.Structure, 1), (ItemKind.Plate, 1) } : new[] { (ItemKind.Structure, 1) },
        WorkKind.RebuildFrame => new[] { (ItemKind.Structure, 2), (ItemKind.Plate, 1) },
        WorkKind.InstallTruss => new[] { (ItemKind.Structure, 2), (ItemKind.Plate, 1) },
        WorkKind.Clamp => new[] { (ItemKind.Structure, 1) },
        WorkKind.RepairRadiator => new[] { (ItemKind.Plate, 1) },
        _ => Array.Empty<(ItemKind, int)>(),
    };

    private static bool HasMaterials(Drone d, (ItemKind kind, int count)[] need) =>
        need.All(x => d.Dock.Storage!.Count(x.kind) >= x.count);

    /// <summary>자재가 모자라 드론이 기다리는 종류 (보급 작업을 서두르게).</summary>
    public HashSet<ItemKind> Waiting { get; } = new();

    // ─────────────────────────────── 매 틱: 움직임 ───────────────────────────────

    public void Step()
    {
        var w = _world;
        const float hour = 1f / SimTime.TicksPerHour;
        var hatch = Hatch(w);
        foreach (var d in Drones)
        {
            d.PreviousPosition = d.Position;
            // 해치 곁을 지나면 문을 연다 (드나들 때마다 에어락이 돈다)
            if (hatch != null && d.State is DroneState.Outbound or DroneState.Returning or DroneState.Towing
                && (d.Position - hatch.Cell.Center).LengthSquared() < 2.5f) hatch.RequestOverride();
            switch (d.State)
            {
                case DroneState.Docked:
                    // 거치대가 방째로 떨어져 나갔으면 조각에 묶인 채 함께 떠간다
                    d.Position = d.Dock.Room.Detached && d.Dock.Room.Fragment is Fragment carried ? carried.Carry(d.DockPosition) : d.DockPosition;
                    break;
                case DroneState.Outbound:
                case DroneState.Returning:
                case DroneState.Towing:
                {
                    float speed = (d.State == DroneState.Towing ? TowSpeed : Speed(d.Kind)) * d.Quirk.Speed;
                    float drain = d.State == DroneState.Towing ? 0.3f : Drain(d.Kind);
                    if (d.Fetching != null && d.State == DroneState.Towing) { speed = 0.02f; drain = 0.3f; }
                    d.Battery = MathF.Max(0f, d.Battery - drain * hour);
                    d.FlightHours += hour;
                    bool arrived = Move(d, speed);
                    if (d.State == DroneState.Towing) Drag(d);
                    if (arrived) Arrive(d);
                    else if (d.Battery <= 0f) Strand(d, "배터리가 바닥났다");
                    break;
                }
                case DroneState.Working:
                    d.Battery = MathF.Max(0f, d.Battery - Drain(d.Kind) * 0.8f * hour);
                    d.FlightHours += hour;
                    d.WorkDone += hour * d.Quirk.Work; // v12.5 버릇
                    if (d.WorkDone >= d.WorkNeeded) FinishWork(d);
                    else if (d.Battery <= 0f) Strand(d, "일하다 배터리가 바닥났다");
                    break;
                case DroneState.Adrift:
                    d.Position += d.DriftVelocity * hour;
                    break;
            }
        }
    }

    /// <summary>경로를 따라 한 틱 이동. 끝에 닿으면 true.</summary>
    private static bool Move(Drone d, float budget)
    {
        while (budget > 0f && d.RouteIndex < d.Route.Count)
        {
            var next = d.Route[d.RouteIndex];
            var delta = next - d.Position;
            float dist = delta.Length();
            if (dist > 0.0001f) d.Facing = delta / dist;
            if (dist <= budget)
            {
                d.Position = next;
                budget -= dist;
                d.RouteIndex++;
            }
            else
            {
                d.Position += delta / dist * budget;
                budget = 0f;
            }
        }
        return d.RouteIndex >= d.Route.Count;
    }

    /// <summary>끌고 오는 조각·드론을 드론에 붙여 옮긴다.</summary>
    private static void Drag(Drone d)
    {
        if (d.Towing is Fragment f)
        {
            f.Offset = d.Position - d.Attach - f.Room.Center;
            f.Angle *= 0.999f;
        }
        if (d.Fetching is Drone x) x.Position = d.Position + new Vector2(0.3f, 0.3f);
    }

    // ─────────────────────────────── 시스템 틱: 판단·충전 ───────────────────────────────

    public void SystemUpdate(float dt)
    {
        var w = _world;
        Waiting.Clear();
        if (Manual) ManualUntil = w.Tick + SimTime.Minutes(15); // v9.2: 사람이 콘솔에 앉아 있다
        var docksCharging = new HashSet<Furniture>();
        foreach (var d in Drones)
        {
            switch (d.State)
            {
                case DroneState.Docked:
                    if (d.Wrecked) break;
                    if (DockWorking(d) && d.Battery < 1f)
                    {
                        d.Battery = MathF.Min(1f, d.Battery + ChargePerHour * d.Dock.Machine!.Efficiency * dt);
                        docksCharging.Add(d.Dock);
                    }
                    if (d.Operational) Decide(d);
                    break;
                case DroneState.Outbound:
                case DroneState.Working:
                case DroneState.Towing:
                    // 움직이는 목표를 따라간다 (떠내려가는 조각·드론)
                    if (d.State == DroneState.Outbound && d.Route.Count > 0)
                    {
                        if (d.Order?.Kind == WorkKind.Retrieve && d.Order.Target.Room?.Fragment is Fragment ff) d.Route[^1] = ff.Center;
                        else if (d.Fetching is Drone fx) d.Route[^1] = fx.Position;
                    }
                    // 고장·닳음
                    d.Condition = MathF.Max(0f, d.Condition - 0.012f * dt);
                    if (!d.Faulty && w.Rng.Chance((0.004f + 0.05f * (1f - d.Condition) * (1f - d.Condition)) * dt))
                    {
                        d.Faulty = true;
                        MarkLog.Add(d.Marks, w.Tick, "밖에서 고장");
                        w.Log.Add(w.Tick, LogKind.Warning, $"{d.Name} 고장 — 하던 일을 두고 돌아온다");
                        Abort(d, "고장");
                        break;
                    }
                    // v9.2: 관제(주 컴퓨터)가 끊기면 하던 일을 두고 돌아온다 (끌던 조각은 세워 두고)
                    if (!Controlled && d.State != DroneState.Returning)
                    {
                        bool wasManual = ManualUntil > 0 && !w.Automation.DroneControl;
                        if (d.State == DroneState.Towing) ReleaseTow(d, wasManual ? "손으로 몰던 사람이 자리를 비웠다 — 잡아 세워 두고 돌아온다" : "관제가 끊겼다 — 잡아 세워 두고 돌아온다");
                        else Abort(d, wasManual ? "손으로 몰던 사람이 자리를 비웠다 — 돌아온다" : "관제가 끊겼다 — 돌아온다");
                        break;
                    }
                    // 맡은 일이 사라졌으면 (누가 끝냈거나 조건이 없어짐) 돌아온다
                    if (d.Order != null && (d.Order.Closed || w.Tick - d.Order.LastSeen > SimTime.Minutes(12)))
                    {
                        Abort(d, null);
                        break;
                    }
                    // 돌아올 배터리가 남았는지
                    if (d.State != DroneState.Towing && d.Battery < ReturnCost(d) + 0.06f) Abort(d, "배터리가 모자라 돌아온다");
                    else if (d.State == DroneState.Towing && d.Battery < ReturnCost(d) + 0.04f) ReleaseTow(d, "배터리가 모자라 잡아 세워 두고 돌아온다");
                    break;
                case DroneState.Adrift:
                    if ((d.Position - w.Structure.ShipCenter).Length() > StructureSystem.LostRange + 20f)
                    {
                        d.State = DroneState.Lost;
                        d.StateSince = w.Tick;
                        Losses++;
                        MarkLog.Add(d.Marks, w.Tick, "시야 밖으로 떠내려가 잃었다");
                        w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(d.Name)} 시야 밖으로 떠내려갔다 — 잃었다", log: true);
                    }
                    break;
            }
        }
        foreach (var dock in w.Ship.Furniture.Where(f => f.Type == FurnitureType.DroneDock))
            if (dock.Machine != null) dock.Machine.Active = docksCharging.Contains(dock);
    }

    /// <summary>거치대에 묶인 채 떨어져 나간 조각과 함께 잃었다.</summary>
    public void LoseWith(Drone d, Fragment f)
    {
        var w = _world;
        d.State = DroneState.Lost;
        d.StateSince = w.Tick;
        d.Doing = $"{Ko.WaGwa(f.Room.Name)} 함께 잃음";
        Losses++;
        MarkLog.Add(d.Marks, w.Tick, $"거치대에 묶인 채 {Ko.EulReul(f.Room.Name)} 따라 떠내려가 잃었다");
    }

    /// <summary>실제로 날아갈 길이 (선체를 돌아가는 길). 격자 밖은 곧장.</summary>
    private float FlightLength(Drone d, Vector2 from, Vector2 to)
    {
        var grid = _world.Ship.Grid;
        Vector2 Clamp(Vector2 p) => new(Math.Clamp(p.X, 0.5f, grid.Width - 0.5f), Math.Clamp(p.Y, 0.5f, grid.Height - 0.5f));
        var a = Cell.FromPosition(Clamp(from));
        var b = Cell.FromPosition(Clamp(to));
        if (!Flyable(a, d)) a = NearestFlyable(a, d);
        if (!Flyable(b, d)) b = NearestFlyable(b, d);
        var path = Bfs(a, b, d);
        float len = (Clamp(from) - from).Length() + (Clamp(to) - to).Length();
        if (path == null) return len + (to - from).Length() * 1.5f;
        var p = a.Center;
        foreach (var c in path) { len += (c.Center - p).Length(); p = c.Center; }
        return len + 1f;
    }

    /// <summary>돌아오는 데 드는 배터리 (돌아갈 길 ÷ 속도 × 소모).</summary>
    private float ReturnCost(Drone d)
    {
        float dist = FlightLength(d, d.Position, d.DockPosition) * 1.1f + 1f;
        return dist / (Speed(d.Kind) * SimTime.TicksPerHour) * Drain(d.Kind);
    }

    private float TripCost(Drone d, Vector2 target, float workHours)
    {
        float dist = FlightLength(d, d.DockPosition, target) * 1.1f + 2f;
        return 2f * dist / (Speed(d.Kind) * SimTime.TicksPerHour) * Drain(d.Kind) + workHours * Drain(d.Kind) * 0.8f + 0.08f;
    }

    /// <summary>거치대에 있는 드론이 할 일을 고른다.</summary>
    private void Decide(Drone d)
    {
        var w = _world;
        if (Hatch(w) == null) return; // 에어락이 없으면 나갈 수 없다
        if (!w.Automation.DroneControl)
        {
            // v9.2: 관제가 없으면 거치대 콘솔의 사람이 한 대씩 몬다
            if (!Manual) { d.Doing = "관제 없음 — 대기 (자동화 꺼짐)"; return; }
            if (Drones.Any(x => x != d && x.State is DroneState.Outbound or DroneState.Working or DroneState.Towing or DroneState.Returning))
            { d.Doing = "손 조종 — 차례를 기다린다 (한 대씩)"; return; }
            // 한 사람이 한 대씩이니 가장 급한 일부터: 끌어올 조각 → 밖의 일 → 정기 검사
            var ready = Drones.Where(x => x.Operational && x.State == DroneState.Docked && !x.Dock.Room.Detached).ToList();
            if (d.Kind != DroneKind.Tow && TowNeeded() != null) { d.Doing = "손 조종 — 견인이 먼저"; return; }
            if (d.Kind == DroneKind.Inspect && OtherDroneJob(ready) is WorkOrder first && first.Kind != WorkKind.InspectHull)
            { d.Doing = "손 조종 — 급한 일이 먼저"; return; }
        }
        if (d.Battery < 0.35f) return;
        switch (d.Kind)
        {
            case DroneKind.Inspect:
                PlanPatrol(d);
                return;
            case DroneKind.Tow:
                if (PlanTow(d)) return;
                PlanFetch(d);
                return;
        }
        foreach (var o in w.Board.OpenFor(d).Where(o => CanDo(d.Kind, o.Kind)).OrderByDescending(o => o.Urgency).ThenBy(o => o.Id))
        {
            var need = Materials(o);
            if (!HasMaterials(d, need))
            {
                foreach (var (k, _) in need) Waiting.Add(k);
                continue;
            }
            if (TargetOf(o) is not Vector2 at) continue;
            float cost = TripCost(d, at, WorkHours(o.Kind));
            if (cost > 0.97f) continue; // 한 번 충전으로는 갔다 올 수 없다
            if (d.Battery < cost)
            {
                if (d.Battery < 0.95f) return; // 충전을 기다린다
                continue;
            }
            foreach (var (k, n) in need) d.Dock.Storage!.Take(k, n);
            d.Cargo = need;
            d.Order = o;
            o.Drone = d;
            Launch(d, at, $"{o.Title}");
            return;
        }
    }

    /// <summary>드론이 일할 자리 (선체 밖 칸의 중심).</summary>
    public Vector2? TargetOf(WorkOrder o)
    {
        var ship = _world.Ship;
        switch (o.Target.Kind)
        {
            case TargetKind.Joint:
                return o.Target.Joint!.Spot.Center;
            case TargetKind.Outer:
                foreach (var dd in Cell.Dirs4)
                    if (ship.Grid.Kind(o.Target.Cell + dd) == TileKind.Void) return (o.Target.Cell + dd).Center;
                foreach (var dd in Cell.Dirs8)
                    if (ship.Grid.Kind(o.Target.Cell + dd) == TileKind.Void) return (o.Target.Cell + dd).Center;
                return null;
            case TargetKind.Exterior:
                return o.Target.Cell.Center;
            case TargetKind.Fragment:
                return o.Target.Room!.Fragment is Fragment f ? f.Center : null;
            case TargetKind.Radiator:
                return o.Target.Cell.Center;
        }
        return null;
    }

    private void Launch(Drone d, Vector2 target, string what)
    {
        var w = _world;
        d.State = DroneState.Outbound;
        d.StateSince = w.Tick;
        d.Doing = what;
        d.Sorties++;
        Sorties++;
        d.Condition = MathF.Max(0f, d.Condition - 0.004f);
        w.CycleAirlock(World.DronePortCost);
        PlanRoute(d, target);
        bool manual = !w.Automation.DroneControl;
        if (manual) ManualSorties++;
        w.Log.Add(w.Tick, LogKind.Work, $"{d.Name} 발진 — {what}" + (manual && Pilot != null ? $" ({Ko.IGa(Pilot.Name)} 손으로 몬다)" : ""));
    }

    /// <summary>경로: 거치대 → 외부 해치 → 우주 칸을 따라 목표로 (격자 밖이면 가장자리에서 곧장).</summary>
    private void PlanRoute(Drone d, Vector2 target)
    {
        var w = _world;
        var grid = w.Ship.Grid;
        d.Route.Clear();
        d.RouteIndex = 0;
        var start = Cell.FromPosition(d.Position);
        bool offGrid = target.X < 0.5f || target.Y < 0.5f || target.X > grid.Width - 0.5f || target.Y > grid.Height - 0.5f;
        var goalCell = Cell.FromPosition(new Vector2(Math.Clamp(target.X, 0.5f, grid.Width - 0.5f), Math.Clamp(target.Y, 0.5f, grid.Height - 0.5f)));
        if (!Flyable(goalCell, d)) goalCell = NearestFlyable(goalCell, d);
        var path = Bfs(start, goalCell, d);
        if (path != null) foreach (var c in path) d.Route.Add(c.Center);
        d.Route.Add(target); // 마지막은 정확한 자리 (격자 밖이면 가장자리에서 곧장)
    }

    private bool Flyable(Cell c, Drone d)
    {
        var ship = _world.Ship;
        var grid = ship.Grid;
        if (!grid.InBounds(c)) return false;
        var k = grid.Kind(c);
        if (k == TileKind.Void) return true;
        if (k == TileKind.Door) return ship.DoorAt(c) is Door door && door.IsExternal;
        return k == TileKind.Floor && ship.RoomAt(c) == d.Dock.Room;
    }

    private Cell NearestFlyable(Cell from, Drone d)
    {
        var grid = _world.Ship.Grid;
        for (int r = 1; r < Math.Max(grid.Width, grid.Height); r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                var c = new Cell(from.X + dx, from.Y + dy);
                if (Flyable(c, d) && grid.Kind(c) == TileKind.Void) return c;
            }
        return from;
    }

    private List<Cell>? Bfs(Cell start, Cell goal, Drone d)
    {
        var grid = _world.Ship.Grid;
        if (!grid.InBounds(start) || !grid.InBounds(goal)) return null;
        if (start == goal) return new List<Cell>();
        var prev = new int[grid.CellCount];
        Array.Fill(prev, -2);
        var q = new Queue<Cell>();
        q.Enqueue(start);
        prev[grid.Index(start)] = -1;
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (c == goal) break;
            for (int k = 0; k < 8; k++)
            {
                var dir = Cell.Dirs8[k];
                var n = c + dir;
                if (!grid.InBounds(n) || prev[grid.Index(n)] != -2 || !Flyable(n, d)) continue;
                if (dir.IsDiagonal && (!Flyable(new Cell(c.X + dir.X, c.Y), d) || !Flyable(new Cell(c.X, c.Y + dir.Y), d))) continue;
                prev[grid.Index(n)] = grid.Index(c);
                q.Enqueue(n);
            }
        }
        if (prev[grid.Index(goal)] == -2) return null;
        var path = new List<Cell>();
        for (int i = grid.Index(goal); i != grid.Index(start); i = prev[i]) path.Add(grid.CellAt(i));
        path.Reverse();
        return path;
    }

    /// <summary>외부 해치 (에어락). 에어락이 떨어져 나갔으면 없다.</summary>
    public static Door? Hatch(World w) => w.Ship.Doors.FirstOrDefault(d => d.IsExternal && !d.Removed);

    // ─────────────────────────────── 도착·일·복귀 ───────────────────────────────

    private void Arrive(Drone d)
    {
        var w = _world;
        switch (d.State)
        {
            case DroneState.Outbound:
                if (d.Kind == DroneKind.Inspect && d.Patrol.Count > 0)
                {
                    // 연결부 하나를 보고 다음으로
                    var j = d.Patrol[d.PatrolIndex];
                    j.Known = j.Strength;
                    j.SeenAt = w.Tick;
                    d.PatrolIndex++;
                    bool roomDone = d.PatrolIndex >= d.Patrol.Count || d.Patrol[d.PatrolIndex].Room != j.Room;
                    if (roomDone)
                    {
                        w.Structure.Inspect(j.Room, drone: d);
                        Inspections++;
                    }
                    if (d.PatrolIndex < d.Patrol.Count && d.Battery > ReturnCost(d) + 0.12f)
                    {
                        PlanRoute(d, d.Patrol[d.PatrolIndex].Spot.Center);
                        return;
                    }
                    GoHome(d);
                    return;
                }
                if (d.Kind == DroneKind.Tow && d.Fetching is Drone lost)
                {
                    d.State = DroneState.Towing;
                    d.StateSince = w.Tick;
                    d.Doing = $"{Ko.EulReul(lost.Name)} 건져 온다";
                    lost.DriftVelocity = Vector2.Zero; // 붙잡았다 (끌려오는 동안은 떠다니는 상태 그대로)
                    PlanRoute(d, d.DockPosition);
                    return;
                }
                d.State = DroneState.Working;
                d.StateSince = w.Tick;
                d.WorkDone = 0f;
                d.WorkNeeded = d.Order != null ? WorkHours(d.Order.Kind) : 0.1f;
                return;
            case DroneState.Towing:
                if (d.Towing is Fragment f)
                {
                    f.Offset = Vector2.Zero;
                    f.Velocity = Vector2.Zero;
                    f.Spin = 0f;
                    f.State = FragmentState.Moored;
                    f.StateSince = w.Tick;
                    f.Tug = null;
                    d.Towing = null;
                    Tows++;
                    MarkFootprint(f, true);
                    w.History.Add(w, HistoryKind.Structure, $"{Ko.IGa(d.Name)} 떨어져 나간 {Ko.EulReul(f.Room.Name)} 끌어왔다 — 제자리에 계류, 임시 도킹을 기다린다", f.Room, log: true);
                    MarkLog.Add(f.Room.Marks, w.Tick, $"{Ko.IGa(d.Name)} 끌어왔다");
                    if (d.Order != null) { w.Board.Close(d.Order); d.Order.Drone = null; d.Order = null; }
                    w.Board.RequestScan();
                    GoHome(d);
                    return;
                }
                if (d.Fetching is Drone x)
                {
                    // 거치대로 돌아왔다
                    x.State = DroneState.Docked;
                    x.StateSince = w.Tick;
                    x.Position = x.DockPosition;
                    x.Doing = x.Wrecked ? "부서짐 · 재조립 필요" : "건져 옴 · 충전";
                    d.Fetching = null;
                    Dock(d);
                    w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(d.Name)} 떠내려가던 {Ko.EulReul(x.Name)} 건져 왔다");
                    return;
                }
                GoHome(d);
                return;
            case DroneState.Returning:
                Dock(d);
                return;
        }
    }

    /// <summary>계류 중인 조각은 EVA로 들어가 일할 수 있는 칸이 된다.</summary>
    internal void MarkFootprint(Fragment f, bool on)
    {
        var paths = _world.Paths;
        foreach (var c in f.Room.Cells) { if (on) paths.MooredCells.Add(c); else paths.MooredCells.Remove(c); }
        foreach (var (c, _) in f.WallCells) { if (on) paths.MooredCells.Add(c); else paths.MooredCells.Remove(c); }
        paths.Invalidate();
    }

    private void FinishWork(Drone d)
    {
        var w = _world;
        var o = d.Order;
        if (o == null) { GoHome(d); return; }
        if (o.Kind == WorkKind.Retrieve && o.Target.Room!.Fragment is Fragment f)
        {
            // 붙잡았다: 먼저 떠내려가는 것을 멈추고, 제자리로 끌고 간다
            f.Velocity = Vector2.Zero;
            f.Spin = 0f;
            f.State = FragmentState.Towed;
            f.StateSince = w.Tick;
            f.Tug = d;
            d.Towing = f;
            d.Attach = d.Position - f.Center;
            d.State = DroneState.Towing;
            d.StateSince = w.Tick;
            d.Doing = $"{Ko.EulReul(f.Room.Name)} 끌고 온다";
            d.Route.Clear();
            d.RouteIndex = 0;
            d.Route.Add(f.Room.Center + d.Attach);
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(d.Name)} {Ko.EulReul(f.Room.Name)} 붙잡았다 — 끌고 온다 ({f.Distance:0}칸)");
            return;
        }
        ExternalWork.Apply(w, o, skill: 0.6f, crew: null, drone: d);
        d.Cargo = Array.Empty<(ItemKind, int)>();
        JobsDone++;
        o.Drone = null;
        d.Order = null;
        GoHome(d);
    }

    private void GoHome(Drone d)
    {
        var w = _world;
        d.State = DroneState.Returning;
        d.StateSince = w.Tick;
        d.Doing = "복귀";
        d.Patrol.Clear();
        d.PatrolIndex = 0;
        if (Hatch(w) == null || d.Dock.Room.Detached)
        {
            // 돌아갈 거치대가 없다: 선체 곁에 머물다 배터리가 떨어진다
            d.Route.Clear();
            d.RouteIndex = 0;
            d.Doing = "돌아갈 곳이 없다";
            return;
        }
        PlanRoute(d, d.DockPosition);
    }

    private void Dock(Drone d)
    {
        var w = _world;
        d.State = DroneState.Docked;
        d.StateSince = w.Tick;
        d.Position = d.DockPosition;
        d.Doing = d.Faulty ? "고장 · 수리 대기" : "충전";
        w.CycleAirlock(World.DronePortCost);
        // 쓰지 못한 자재는 자재칸에 되돌린다
        foreach (var (k, n) in d.Cargo) d.Dock.Storage!.Add(k, n);
        d.Cargo = Array.Empty<(ItemKind, int)>();
    }

    /// <summary>하던 일을 두고 돌아온다.</summary>
    private void Abort(Drone d, string? why)
    {
        var w = _world;
        if (d.Order != null)
        {
            d.Order.Drone = null;
            if (why != null) w.Board.Block(d.Order, null, 0.2f);
            d.Order = null;
        }
        if (d.Towing != null) { ReleaseTow(d, why ?? "놓고 돌아온다"); return; }
        if (why != null) w.Log.Add(w.Tick, LogKind.Warning, $"{d.Name}: {why}");
        GoHome(d);
    }

    private void ReleaseTow(Drone d, string why)
    {
        var w = _world;
        if (d.Towing is Fragment f)
        {
            f.State = FragmentState.Adrift; // 멈춰 세워 두었다 (속도 0) — 다시 와서 이어 끈다
            f.Velocity = Vector2.Zero;
            f.Tug = null;
            d.Towing = null;
            w.Log.Add(w.Tick, LogKind.Warning, $"{d.Name}: {why} ({f.Room.Name} {f.Distance:0}칸)");
        }
        if (d.Order != null) { d.Order.Drone = null; d.Order = null; }
        GoHome(d);
    }

    /// <summary>배터리가 바닥났다: 선체 밖에 멈춰 떠다닌다.</summary>
    private void Strand(Drone d, string why)
    {
        var w = _world;
        if (d.Towing is Fragment f) { f.State = FragmentState.Adrift; f.Tug = null; d.Towing = null; }
        if (d.Order != null) { d.Order.Drone = null; d.Order = null; }
        if (d.Fetching is Drone x) { x.State = DroneState.Adrift; d.Fetching = null; }
        d.State = DroneState.Adrift;
        d.StateSince = w.Tick;
        d.Doing = why;
        var away = d.Position - w.Structure.ShipCenter;
        d.DriftVelocity = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) * 0.6f : new Vector2(0, 0.6f);
        MarkLog.Add(d.Marks, w.Tick, why);
        w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(d.Name)} 선체 밖에서 멈췄다 — {why}", log: true);
    }

    // ─────────────────────────────── 드론별 일감 ───────────────────────────────

    /// <summary>검사 드론: 운석을 맞은 방부터, 아니면 오래 못 본 방을 돈다.</summary>
    private void PlanPatrol(Drone d)
    {
        var w = _world;
        var st = w.Structure;
        var rooms = new List<Room>();
        foreach (var (id, _) in st.Unseen.OrderBy(kv => kv.Value))
        {
            var r = w.Ship.Rooms[id];
            if (!r.Detached && r.Joints.Count > 0) rooms.Add(r);
        }
        bool urgent = rooms.Count > 0;
        if (!urgent)
        {
            if (d.Battery < 0.9f) return;
            // 이틀(교훈 뒤에는 하루) 넘게 못 본 방 다섯까지 (정기 순회)
            float every = w.History.Doctrine.FrequentInspection ? 24f : 48f;
            rooms = w.Ship.Rooms.Where(r => !r.Detached && r.Joints.Count > 0 && r.Joints.Min(j => j.SeenAt) < w.Tick - SimTime.Hours(every))
                .OrderBy(r => r.Joints.Min(j => j.SeenAt)).Take(5).ToList();
            if (rooms.Count < 3 && !rooms.Any(r => r.Joints.Min(j => j.SeenAt) < w.Tick - SimTime.Hours(every * 1.5f))) return; // 모아서 한 번에
        }
        d.Patrol.Clear();
        d.PatrolIndex = 0;
        foreach (var r in rooms.Take(5))
            d.Patrol.AddRange(r.Joints.Where(j => w.Ship.Grid.Kind(j.Spot) == TileKind.Void || w.Paths.IsSpace(j.Spot)).OrderBy(j => j.Index));
        if (d.Patrol.Count == 0) return;
        Launch(d, d.Patrol[0].Spot.Center, urgent ? $"{rooms[0].Name} 외벽 검사 (운석 뒤)" : $"정기 외벽 검사 ({string.Join("·", rooms.Select(r => r.Name))})");
    }

    /// <summary>견인 드론: 되찾기로 한 조각을 끌어온다.</summary>
    private bool PlanTow(Drone d)
    {
        var w = _world;
        foreach (var o in w.Board.OpenFor(d).Where(o => o.Kind == WorkKind.Retrieve).OrderByDescending(o => o.Urgency))
        {
            if (o.Target.Room!.Fragment is not Fragment f || f.State is FragmentState.Lost or FragmentState.Moored || f.Tug != null) continue;
            float cost = TripCost(d, f.Center, f.Distance / (TowSpeed * SimTime.TicksPerHour) * 0.6f);
            if (d.Battery < MathF.Min(0.95f, cost)) return d.Battery < 0.95f; // 충전을 기다린다
            d.Order = o;
            o.Drone = d;
            Launch(d, f.Center, $"떨어져 나간 {Ko.EulReul(f.Room.Name)} 붙잡으러");
            return true;
        }
        return false;
    }

    /// <summary>견인 드론: 떠내려가는 드론을 건져 온다.</summary>
    private void PlanFetch(Drone d)
    {
        if (d.Battery < 0.8f) return;
        var lost = Drones.Where(x => x != d && x.State == DroneState.Adrift).OrderBy(x => (x.Position - d.Position).Length()).FirstOrDefault();
        if (lost == null) return;
        d.Fetching = lost;
        Launch(d, lost.Position, $"떠내려가는 {Ko.EulReul(lost.Name)} 건지러");
    }

    // ─────────────────────────────── 파편 ───────────────────────────────

    /// <summary>운석 파편이 밖에 나와 있던 드론을 때린다 (거치대 안은 안전).</summary>
    public void OnImpact(Cell entry, float size)
    {
        var w = _world;
        foreach (var d in Drones)
        {
            if (d.State is DroneState.Docked or DroneState.Lost) continue;
            float dist = (d.Position - entry.Center).Length();
            float r = 3f + 2f * size;
            if (dist > r) continue;
            float hit = 0.6f * size * (1f - dist / (r + 0.5f)) * w.Rng.Range(0.6f, 1.2f);
            d.Condition = MathF.Max(0f, d.Condition - hit);
            if (d.Condition < 0.1f)
            {
                d.Wrecked = true;
                MarkLog.Add(d.Marks, w.Tick, "운석 파편에 부서졌다");
                w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(d.Name)} 운석 파편에 부서졌다", log: true);
                Strand(d, "파편에 부서졌다");
            }
            else if (w.Rng.Chance(0.5f * size))
            {
                d.Faulty = true;
                MarkLog.Add(d.Marks, w.Tick, "운석 파편에 맞아 고장");
                Abort(d, "파편에 맞아 고장");
            }
        }
    }

    /// <summary>거치대에서 드론 정비·수리·재조립을 마쳤다.</summary>
    public void Serviced(Drone d, CrewMember by, float skill)
    {
        var w = _world;
        string what = d.Wrecked ? "다시 짜 맞췄다" : d.Faulty ? "고쳤다" : "정비했다";
        if (d.Wrecked) { d.Wrecked = false; d.Condition = 0.7f + 0.15f * skill; d.Battery = MathF.Max(d.Battery, 0.1f); }
        else if (d.Faulty) { d.Faulty = false; d.Condition = MathF.Max(d.Condition, 0.6f + 0.2f * skill); }
        else d.Condition = MathF.Min(1f, 0.85f + 0.12f * skill);
        d.Doing = "충전";
        MarkLog.Add(d.Marks, w.Tick, $"{Ko.IGa(by.Name)} {what}");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.EulReul(d.Name)} {what} (상태 {d.Condition * 100:0}%)", by.Id);
    }

    /// <summary>드론이 거치대에 돌아와 있는데 고치거나 정비해야 하는지.</summary>
    public static (ItemKind kind, int count)[] ServiceCost(Drone d) =>
        d.Wrecked ? new[] { (ItemKind.Electronics, 2), (ItemKind.Motor, 1), (ItemKind.Plate, 2) }
        : d.Faulty ? new[] { (ItemKind.Electronics, 1) }
        : new[] { (ItemKind.Lubricant, 1) };
}
