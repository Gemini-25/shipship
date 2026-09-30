using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>배가 지나는 공간 (v11.2 항로).</summary>
public enum ZoneKind
{
    Normal, // 보통 항로: 잔해가 보통, 날아드는 것 없음
    Debris, // 잔해 지대: 채집이 1.7배, 대신 작은 운석이 가끔 날아든다
}

/// <summary>엔진 연소 하나 (회피 기동이나 항로 변경).</summary>
public sealed class Burn
{
    public bool Evasion { get; init; }
    public IncomingMeteor? Meteor { get; init; }
    public long Start { get; init; }
    public long Ignite { get; init; }
    public long End { get; init; }
    public float Control { get; init; }
    public string ControlBy { get; init; } = "";
    public float Thrust { get; init; }
    public float Lead { get; init; }
}

/// <summary>
/// v11.2 엔진실의 존재 이유: 추진과 항로.
///   A: 엔진 — 연소할 때만 전기를 크게 먹는다. 추진제(물을 전기로 쪼갠 반응 물질)를 태운다.
///   B: 조종 — 주 컴퓨터와 함교 조타 콘솔(자동), 아니면 함교에 앉은 사람(손 조종), 아니면 엔진실 사람(현장 조종).
///   C: 경보 — 센서가 일찍 볼수록 피할 틈이 있다.
/// 날아오는 운석에 경보가 울리면 배가 옆으로 비킨다 (회피 기동): 추력·조종·남은 시간이 넉넉하면 완전히 비껴가고, 모자라면 스치기만 한다.
/// 항로: 보통 항로와 잔해 지대 사이를 연소로 오간다. 잔해 지대는 채집이 좋지만 작은 운석이 날아든다 — 엔진이 멎으면 빠져나올 수 없다.
/// 추진제가 물이라서, 물 ↔ 정수기(전기) ↔ 엔진(추진제)의 고리가 생긴다.
/// </summary>
public sealed class PropulsionSystem
{
    private readonly World _w;

    public float Propellant { get; set; }
    public float Capacity { get; set; }
    public ZoneKind Zone { get; private set; } = ZoneKind.Normal;
    public long ZoneSince { get; private set; }
    public Burn? Current { get; private set; }

    // 기록
    public int Evasions { get; private set; }
    public int Dodged { get; private set; }
    public int Glanced { get; private set; }
    public int Missed { get; private set; }
    public int Transfers { get; private set; }
    public int AmbientHits { get; private set; }
    public int HitsThisZone { get; private set; }
    public long NextAmbient { get; private set; } = -1;

    /// <summary>회피 한 번에 드는 추진제 (kg, 배 크기에 비례).</summary>
    public float EvadeCost => 6f * Scale;
    /// <summary>항로를 바꾸는 데 드는 추진제.</summary>
    public float TransferCost => 30f * Scale;
    public float Scale { get; private set; }

    /// <summary>시험용: 회피 기동을 끈다 (운석이 맞았을 때의 결과를 재는 게이트).</summary>
    public bool EvasionEnabled { get; set; } = true;

    /// <summary>항로 변경 연소 중 (사람이 조타 콘솔에서).</summary>
    internal bool CourseBurn { get; set; }

    /// <summary>배 크기에 맞춘다 (배를 띄울 때).</summary>
    internal void SetScale(float scale)
    {
        Scale = MathF.Max(1f, scale);
        Capacity = 120f * Scale;
        Propellant = Capacity * 0.8f;
    }

    public const float BurnMinutes = 1.5f;

    public static string ZoneName(ZoneKind z) => z switch { ZoneKind.Debris => "잔해 지대", _ => "보통 항로" };

    /// <summary>그 공간의 잔해 밀도 평균 (채집 속도).</summary>
    public static float ZoneDensity(ZoneKind z) => z == ZoneKind.Debris ? 1.7f : 1f;

    public PropulsionSystem(World w, float scale)
    {
        _w = w;
        Scale = MathF.Max(1f, scale);
        Capacity = 120f * Scale;
        Propellant = Capacity * 0.8f;
    }

    public IEnumerable<Machine> Engines => _w.Ship.FurnitureOf(FurnitureType.EngineCore).Select(f => f.Machine!);
    private int DesignEngines => Math.Max(1, _w.Ship.Furniture.Count(f => f.Type == FurnitureType.EngineCore));

    /// <summary>쓸 수 있는 추력 0~1.5 (설계 엔진 모두 멀쩡하면 1, 이온 추진기면 더).</summary>
    public float Thrust
    {
        get
        {
            float sum = 0f;
            foreach (var m in Engines)
            {
                if (m.Body.Room.Abandoned || m.Stopped) continue;
                // 엔진은 평소 대기 중이라 전기는 연소할 때 따로 본다 — 여기선 고장·마모·단계만
                sum += m.FaultFactor * (1f - 0.25f * m.Wear * m.Wear) * (0.6f + 0.4f * m.Condition) * Tech.Of(m).Output * Grades.Output(m.Grade);
            }
            return sum / DesignEngines;
        }
    }

    /// <summary>조종: 자동(주 컴퓨터 + 함교 조타 콘솔) · 손 조종(함교에 깨어 있는 사람) · 현장 조종(엔진실 사람). 없으면 0.</summary>
    public (float quality, string by, CrewMember? who) Control()
    {
        var bridge = _w.Ship.RoomsOf(RoomType.Bridge).FirstOrDefault(r => !r.Abandoned);
        bool helm = bridge != null && bridge.Furniture.Any(f => f.Type == FurnitureType.Console && f.Machine is Machine m && m.Efficiency > 0f);
        if (_w.Automation.MainOnline && helm) return (0.85f, "자동 조종", null);
        static bool Awake(CrewMember c) => !c.Dead && !c.Down && !c.Outside && c.Pose != Pose.Sleeping && c.CarriedBy == null;
        var pilot = _w.Crew.Where(c => Awake(c) && c.Room == bridge).OrderByDescending(c => c.SkillLevel(Skill.Piloting)).FirstOrDefault();
        if (pilot != null && helm) return (0.3f + 0.55f * pilot.SkillLevel(Skill.Piloting), $"{pilot.Name}(손 조종)", pilot);
        var local = _w.Crew.Where(c => Awake(c) && c.Room?.Type == RoomType.Engine).OrderByDescending(c => c.SkillLevel(Skill.Engineering)).FirstOrDefault();
        // v11.1: 함교 조타를 못 쓰면 예비 조타석 (자동화가 살아 있으면 거기로 자동 조종, 아니면 거기 앉은 사람)
        if (!helm && AuxHelm is Furniture aux)
        {
            if (_w.Automation.MainOnline) return (0.7f, "자동 조종(예비 조타석)", null);
            var at = _w.Crew.Where(c => Awake(c) && c.Room == aux.Room).OrderByDescending(c => c.SkillLevel(Skill.Piloting)).FirstOrDefault();
            if (at != null) return (0.25f + 0.5f * at.SkillLevel(Skill.Piloting), $"{at.Name}(예비 조타석)", at);
        }
        if (local != null) return (0.2f + 0.4f * local.SkillLevel(Skill.Engineering), $"{local.Name}(엔진실 현장 조종)", local);
        return (0f, "조종할 사람이 없다", null);
    }

    /// <summary>v11.1 쓸 수 있는 예비 조타석.</summary>
    public Furniture? AuxHelm => _w.Ship.Furniture.FirstOrDefault(f => f.AuxHelm && !f.Stowed && !f.Room.Detached && !f.Room.Abandoned && f.Machine is Machine m && m.Efficiency > 0f);

    /// <summary>연소를 준비하는 시간 (분): 자동이면 금방, 사람이면 좀 걸린다.</summary>
    private static float SpinUp(float control, string by) => by.StartsWith("자동 조종") ? (by == "자동 조종" ? 0.4f : 0.6f) : by.Contains("현장") ? 1.6f : 1.1f;

    public bool Burning => Current != null && _w.Tick >= Current.Ignite && _w.Tick < Current.End;

    /// <summary>화면: 항로 변경 연소도 불꽃으로.</summary>
    public bool CourseBurnVisible => CourseBurn;

    /// <summary>운석 경보가 울렸다: 피할 수 있으면 연소를 건다.</summary>
    public void OnWarned(IncomingMeteor m)
    {
        if (Current != null || m.Evaded) return;
        var (control, by, who) = Control();
        float thrust = Thrust;
        float lead = m.MinutesLeft(_w.Tick);
        if (control <= 0f || thrust < 0.15f) { Note(m, control <= 0f ? "조종할 사람이 없어 피하지 못한다" : "엔진이 멎어 피하지 못한다"); return; }
        if (Propellant < EvadeCost) { Note(m, $"추진제가 모자라 피하지 못한다 ({Propellant:0}kg)"); return; }
        float spin = SpinUp(control, by);
        if (lead < spin + 0.3f) { Note(m, $"너무 늦게 봤다 — 엔진을 켤 틈이 없다 ({lead:0.#}분)"); return; }
        long ignite = _w.Tick + SimTime.Minutes(spin);
        Current = new Burn
        {
            Evasion = true, Meteor = m, Start = _w.Tick, Ignite = ignite, End = ignite + SimTime.Minutes(BurnMinutes),
            Control = control, ControlBy = by, Thrust = thrust, Lead = lead,
        };
        m.Evaded = true;
        Evasions++;
        if (who != null) MarkLog.Add(who.Memory.Marks, _w.Tick, "운석을 피하려 엔진을 켰다");
        _w.Log.Add(_w.Tick, LogKind.Ship, $"회피 기동 — {by} · 추력 {thrust * 100:0}% · {spin:0.#}분 뒤 점화 (추진제 {EvadeCost:0}kg)");
    }

    private void Note(IncomingMeteor m, string why)
    {
        if (m.EvadeNote != null) return;
        m.EvadeNote = why;
        _w.Log.Add(_w.Tick, LogKind.Warning, $"회피 기동 못 함 — {why}");
    }

    /// <summary>매 틱: 연소 중이면 엔진이 전기를 먹고, 끝나면 결과를 본다.</summary>
    public void Step()
    {
        var b = Current;
        bool burning = b != null && _w.Tick >= b.Ignite && _w.Tick < b.End;
        foreach (var m in Engines) m.Active = burning || CourseBurn; // 엔진은 연소할 때만 돈다 (평소엔 대기 전력)
        if (b == null) return;
        if (b.Meteor != null && !_w.Sensors.Incoming.Contains(b.Meteor) && _w.Tick < b.End)
        {
            // 연소가 끝나기 전에 부딪혔다: 연소한 만큼만 비켰다 (Incidents가 이미 맞았다)
            Current = null;
            return;
        }
        if (_w.Tick < b.End) return;
        Current = null;
        Resolve(b);
    }

    private void Resolve(Burn b)
    {
        var w = _w;
        Propellant = MathF.Max(0f, Propellant - EvadeCost);
        foreach (var e in Engines) e.Wear = MathF.Min(1f, e.Wear + 0.02f);
        // 전기가 모자랐으면 추력이 준다 (엔진이 전기를 못 받았다)
        float powered = Engines.Count() == 0 ? 0f : Engines.Count(e => e.Powered) / (float)Engines.Count();
        if (b.Meteor is not IncomingMeteor m || !w.Sensors.Incoming.Contains(m)) return;
        float lead = MathF.Min(1f, MathF.Max(0f, b.Lead - 0.3f) / 3f);
        float size = m.Size >= 1.3f ? 0.35f : m.Size >= 0.7f ? 0.7f : 0.9f;
        // 몇 분 만에 배를 옮길 수 있는 거리는 짧다: 완전히 비껴가는 건 잘해야 절반, 나머지는 스치거나 그대로 맞는다
        float p = 0.5f * b.Control * MathF.Min(1f, b.Thrust / 0.6f) * powered * (0.35f + 0.65f * lead) * size;
        float roll = w.Rng.Float();
        string where = m.Room?.Name ?? "선체";
        if (roll < p)
        {
            w.Sensors.Incoming.Remove(m);
            foreach (var d in m.SealedDoors) if (!(d.RoomA?.Lockdown ?? false) && !(d.RoomB?.Lockdown ?? false)) d.Locked = false;
            Dodged++;
            w.History.Add(w, HistoryKind.Response, $"회피 기동 성공 — {b.ControlBy} · {where}로 오던 운석이 비껴갔다", m.Room, log: true);
            w.RaiseAlert($"회피 기동 성공 — 운석이 비껴갔다 ({b.ControlBy})", null, AlertLevel.Notice, shipWide: true);
        }
        else if (roll < p + 0.35f * b.Control * MathF.Min(1f, b.Thrust / 0.6f) * powered)
        {
            m.Size *= 0.5f;
            Glanced++;
            w.Log.Add(w.Tick, LogKind.Warning, $"회피 기동 — 다 비키지 못했다, 스치듯 맞는다 ({where} · 충격 절반)");
        }
        else
        {
            Missed++;
            w.Log.Add(w.Tick, LogKind.Warning, $"회피 기동 실패 — 늦었다 ({b.ControlBy} · 추력 {b.Thrust * 100:0}%{(powered < 1f ? $" · 엔진 전기 {powered * 100:0}%" : "")})");
        }
    }

    /// <summary>시스템 틱: 항로(잔해 지대의 날아드는 것).</summary>
    public void SystemUpdate(float dt)
    {
        var w = _w;
        if (Zone != ZoneKind.Debris) { NextAmbient = -1; return; }
        if (NextAmbient < 0) NextAmbient = w.Tick + SimTime.Hours(w.Rng.Range(18f, 42f));
        if (w.Tick < NextAmbient) return;
        NextAmbient = w.Tick + SimTime.Hours(w.Rng.Range(18f, 42f));
        // 선체 벽 가운데 하나를 골라 작은 운석 (센서·회피 기동이 똑같이 작동한다 — 관찰자 기록이 아니라 배가 지나는 공간의 일)
        var hull = w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) is Room r && !r.Detached && r.Type != RoomType.Corridor).Select(kv => kv.Key).ToList();
        if (hull.Count == 0) return;
        var at = w.Rng.Pick(hull);
        var inside = Hull.InsideRoom(w.Ship, at)!;
        var target = Cell.Dirs4.Select(d => at + d).FirstOrDefault(c => w.Ship.RoomAt(c) == inside);
        if (w.Sensors.Launch(target == default ? at : target, w.Rng.Range(0.2f, 0.5f)) != null)
        {
            AmbientHits++;
            HitsThisZone++;
        }
    }

    /// <summary>항로를 바꿨다 (사람이 연소를 마쳤다).</summary>
    internal bool Transfer(ZoneKind to, CrewMember by, out string why)
    {
        var w = _w;
        why = "";
        if (Thrust < 0.5f) { why = $"엔진 추력이 모자라다 ({Thrust * 100:0}%)"; return false; }
        if (Propellant < TransferCost) { why = $"추진제가 모자라다 ({Propellant:0}/{TransferCost:0}kg)"; return false; }
        var from = Zone;
        Propellant -= TransferCost;
        foreach (var m in Engines) m.Wear = MathF.Min(1f, m.Wear + 0.05f);
        Zone = to;
        ZoneSince = w.Tick;
        HitsThisZone = 0;
        Transfers++;
        w.Space.SetMean(ZoneDensity(to));
        w.History.Add(w, HistoryKind.Milestone, $"항로를 바꿨다 — {ZoneName(from)} → {ZoneName(to)} ({Ko.IGa(by.Name)} 엔진을 태웠다 · 추진제 {TransferCost:0}kg)", null, new[] { by }, log: true);
        return true;
    }

    /// <summary>v11.2 잔해 구름: 배가 밀려났다 (연소 없이, 그 공간의 규칙이 곧바로 적용된다).</summary>
    internal void Drift(ZoneKind z)
    {
        Zone = z;
        ZoneSince = _w.Tick;
        HitsThisZone = 0;
        NextAmbient = -1;
        _w.Space.SetMean(ZoneDensity(z));
    }

    /// <summary>화면·시험용: 곧바로 그 공간으로 (기록되지 않는다).</summary>
    public void Place(ZoneKind z)
    {
        Zone = z;
        ZoneSince = _w.Tick;
        _w.Space.SetMean(ZoneDensity(z));
    }

    public string StatusText =>
        $"{ZoneName(Zone)} · 추진제 {Propellant:0}/{Capacity:0}kg · 추력 {Thrust * 100:0}% · {Control().by}" + (Burning ? " · 연소 중" : "");
}

public sealed partial class WorkBoard
{
    /// <summary>v11.2 항로와 추진: 추진제 보충, 항로 변경 (회의).</summary>
    private void ScanNavigation(Poster post)
    {
        var w = _world;
        var p = w.Propulsion;
        var engine = w.Ship.FurnitureOf(FurnitureType.EngineCore).FirstOrDefault(f => !f.Room.Abandoned && !f.Room.OffLimits);
        if (engine == null) return;
        // ── 추진제 보충: 물이 넉넉할 때 정수기 꼭지에서 엔진실로 ──
        if (p.Propellant < p.Capacity * 0.6f && w.Water.Level > w.Water.Capacity * 0.45f && Logistics.WaterTap(w) != null)
            post(WorkKind.RefillPropellant, WorkTarget.Of(engine), 0.18f + 0.4f * (1f - p.Propellant / p.Capacity), Skill.Engineering,
                $"추진제 {p.Propellant:0}/{p.Capacity:0}kg · 물 {w.Water.Level:0}L에서 덜어 온다");
        // ── 항로 변경: 원료가 모자라면 잔해 지대로, 거기서 맞거나 채울 만큼 채웠으면 빠져나온다 ──
        int raw = ItemKinds.RawKinds.Sum(k => Have(k));
        bool calm = Evolution.Peaceful(w) && w.Air.Reserve > w.Air.ReserveCapacity * 0.75f && Have(ItemKind.Sealant) >= 6;
        bool can = p.Thrust >= 0.5f && p.Propellant >= p.TransferCost;
        var bridge = w.Ship.RoomsOf(RoomType.Bridge).FirstOrDefault(r => !r.Abandoned && !r.Leaking);
        // 조타: 설 자리가 있는 함교 콘솔 → 조종석(의자) → 엔진실
        var helm = bridge?.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Console && f.Machine is Machine hm && !hm.Stopped && f.UseSpots.Count > 0)
                   ?? bridge?.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Seat && f.UseSpots.Count > 0)
                   ?? (p.AuxHelm is Furniture ax && ax.UseSpots.Count > 0 ? ax : null) // v11.1 예비 조타석
                   ?? engine;
        // 회의를 통과한 항로 변경은 미루지 않는다 (당직보다 앞)
        bool approved = _open.Values.Any(o => o.Kind == WorkKind.ChangeCourse && o.Decision == DecisionState.Approved);
        if (p.Zone == ZoneKind.Normal && raw < 12 && Have(ItemKind.Plate) < 10 && calm && can && w.Tick - p.ZoneSince > SimTime.Hours(72))
            post(WorkKind.ChangeCourse, WorkTarget.Of(helm), approved ? 0.7f : 0.4f, Skill.Piloting,
                $"원료 {raw}개 · 금속판 {Have(ItemKind.Plate)}개 → 잔해 지대로 (채집 1.7배 · 작은 운석이 날아든다 · 추진제 {p.TransferCost:0}kg)",
                circuit: (int)ZoneKind.Debris);
        else if (p.Zone == ZoneKind.Debris)
        {
            float days = (w.Tick - p.ZoneSince) / (float)SimTime.TicksPerDay;
            string? why = p.HitsThisZone >= 2 ? $"잔해 지대에서 {p.HitsThisZone}번 맞았다"
                : w.Air.Reserve < w.Air.ReserveCapacity * 0.6f ? "공기 탱크가 줄었다"
                : raw > 40 ? $"원료를 채웠다 ({raw}개)"
                : days > 6f ? $"잔해 지대에서 {days:0}일"
                : null;
            if (why != null)
                post(WorkKind.ChangeCourse, WorkTarget.Of(helm), can ? (approved ? 0.8f : 0.5f) + 0.1f * p.HitsThisZone : 0.2f, Skill.Piloting,
                    $"{why} → 보통 항로로 (추진제 {p.TransferCost:0}kg)" + (can ? "" : $" · {(p.Thrust < 0.5f ? "엔진 추력이 모자라 못 빠져나간다" : "추진제가 모자라다")}"),
                    circuit: (int)ZoneKind.Normal);
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>추진제 보충: 정수기 꼭지에서 물통으로 물을 받아 엔진의 추진제 탱크에 붓는다.</summary>
    private static Job? RefillPropellant(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var tap = Logistics.WaterTap(w);
        var tapSpot = tap?.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (tap == null || tapSpot is not Cell ts) { blocked = "물꼭지에 갈 수 없음"; return null; }
        float liters = 0f;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(ts));
        toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Working, tap.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            var p = world.Propulsion;
            liters = MathF.Min(25f, MathF.Min(p.Capacity - p.Propellant, world.Water.Level - world.Water.Capacity * 0.4f));
            if (liters < 1f) return false;
            world.Water.Level -= liters;
            return true;
        }));
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.25f, Skill.Engineering, o.Target.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            var p = world.Propulsion;
            p.Propellant = MathF.Min(p.Capacity, p.Propellant + liters);
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"추진제 탱크에 물 {liters:0}L를 부었다 (전기로 쪼개 반응 물질로 · {p.Propellant:0}/{p.Capacity:0}kg)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "추진제", toils, "엔진 추진제를 채우러 간다");
    }

    /// <summary>항로 변경: 조타 콘솔(없으면 엔진실)에서 한 시간 동안 연소를 지휘한다.</summary>
    private static Job? ChangeCourse(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var to = (ZoneKind)o.Circuit;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1f, Skill.Piloting, o.Target.Center)
        {
            OnBegin = (_, world) => world.Propulsion.CourseBurn = true,
            OnEnd = (_, world) => world.Propulsion.CourseBurn = false,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            if (!world.Propulsion.Transfer(to, cm, out string why))
            {
                world.Log.Add(world.Tick, LogKind.Warning, $"항로를 못 바꿨다 — {why}", cm.Id);
                return true;
            }
            cm.Practice(Skill.Piloting, 0.03f);
            return true;
        }));
        return Wrap(a, o, c, w, "항로 변경", toils, $"{PropulsionSystem.ZoneName(to)}로 엔진을 태운다");
    }
}
