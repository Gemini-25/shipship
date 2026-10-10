using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>배 전체의 위기 단계.</summary>
public enum CrisisLevel { Calm, Alert, Emergency, Survival }

/// <summary>
/// 승무원 AI: 배 전체가 지금 얼마나 위급한지, 무엇이 가장 급한지 (생존 사슬).
/// 사람 → 불 → 전기·냉각 → 공기 → 사람이 있는 방의 구멍 → 생명유지 설비 → 그 밖.
/// 비상·생존 위기에는 이 사슬에 걸린 일이 앞으로 오고, 급하지 않은 일(외벽 마감 용접·정비·개조·점검·배우기)은 뒤로 가고,
/// 비번인 사람도 불려 나오고, 잠은 미룬다 (탈진 직전이면 쪽잠). 사람은 기계가 아니라서 겁·공포·당황·피로는 그대로 남는다.
/// 한 번 계산하면 같은 틱 동안은 다시 쓰지 않는다.
/// </summary>
public static class Crisis
{
    /// <summary>시험용: 위기 판단을 끈 승무원 (예전 AI) — 위기 단계는 여전히 잰다.</summary>
    public static bool Disabled { get; set; }

    private static World? _cachedWorld;
    private static long _cachedTick = -1;
    private static Snapshot _snap = new();

    /// <summary>지금 배의 상태 (같은 틱에는 한 번만 잰다).</summary>
    public sealed class Snapshot
    {
        public CrisisLevel Level;
        public bool Power;        // 전기: 원자로가 멈췄거나 부하를 끊는 중이거나 배터리가 바닥
        public bool Cooling;      // 냉각: 원자로 열을 못 뺀다
        public bool Air;          // 공기: 산소 발생이 없고 공기 탱크도 바닥이거나, 사람이 있는 방의 산소가 낮다
        public int Fires;
        public int Breaches;      // 사람이 쓰는 방·핵심 방의 새는 구멍
        public int Down;          // 쓰러진 사람
        public float BatteryHours; // 배터리로 버틸 시간 (원자로가 멈췄을 때)
        public List<string> Reasons = new();
        public string Top => Reasons.Count > 0 ? Reasons[0] : "평온";
    }

    public static Snapshot Now(World w)
    {
        if (ReferenceEquals(_cachedWorld, w) && _cachedTick == w.Tick) return _snap;
        _cachedWorld = w;
        _cachedTick = w.Tick;
        _snap = Measure(w);
        return _snap;
    }

    public static CrisisLevel Level(World w) => Now(w).Level;

    public static string Name(CrisisLevel l) => l switch
    {
        CrisisLevel.Alert => "경계",
        CrisisLevel.Emergency => "비상",
        CrisisLevel.Survival => "생존 위기",
        _ => "평시",
    };

    private static Snapshot Measure(World w)
    {
        var s = new Snapshot();
        var p = w.Power;
        bool reactorDown = !p.ReactorOnline;
        bool shedding = p.Delivered < p.Demand * 0.9f;
        s.BatteryHours = p.Demand > 0.5f ? p.BatteryCharge / MathF.Max(0.5f, p.Demand - p.Delivered + (reactorDown ? 0f : 0f)) : 99f;
        s.Power = reactorDown || shedding || p.BatteryPercent < 0.2f && !p.ReactorOnline;
        s.Cooling = p.ReactorOnline && p.ReactorTemperature > 330f || w.Board.OpenUnsorted.Any(o => o.Kind == WorkKind.Repair && o.Target.Furniture?.Type == FurnitureType.CoolantPump);
        s.Fires = w.Fire.Count;
        s.Down = w.Crew.Count(c => c.Down && !c.Dead && c.CareBed == null);
        foreach (var r in w.Ship.LiveRooms)
        {
            if (!r.Leaking || r.Abandoned) continue;
            bool used = w.Crew.Any(c => !c.Dead && c.Room == r) || r.Furniture.Any(f => f.Machine is Machine m && m.Spec.Critical) || r.Type is RoomType.Corridor or RoomType.Quarters;
            if (used) s.Breaches++;
        }
        float o2Low = w.Crew.Where(c => !c.Dead && c.Room != null && c.Suit == null).Select(c => c.Room!.Air.O2).DefaultIfEmpty(21f).Min();
        s.Air = w.Air.O2Capacity < 1f && w.Air.Reserve < w.Air.ReserveCapacity * 0.3f || o2Low < 17f;

        if (s.Down > 0) s.Reasons.Add($"쓰러진 사람 {s.Down}");
        if (s.Fires > 0) s.Reasons.Add($"불 {s.Fires}칸");
        if (s.Power) s.Reasons.Add(reactorDown ? $"원자로 멈춤 · 배터리 {p.BatteryPercent * 100:0}%" : "전기 모자람 (부하 차단)");
        if (s.Cooling) s.Reasons.Add("냉각 부족");
        if (s.Air) s.Reasons.Add("공기 부족");
        if (s.Breaches > 0) s.Reasons.Add($"새는 방 {s.Breaches}");

        // 큰 사고의 흔적: 최근 30분 안의 치명 경보·폭발·충돌, 큰 구멍(사람 없는 방이어도), 사람 있는 방의 유독 가스·일산화탄소, 문을 막은 잔해
        bool recentCritical = w.Alerts.Any(a => a.Level == AlertLevel.Critical && w.Tick - a.Tick < SimTime.Minutes(30));
        bool recentBlast = w.Volatile.Blasts.Any(b => w.Tick - b.Tick < SimTime.Hours(1)) || w.Impacts.Any(i => w.Tick - i.Tick < SimTime.Hours(1) && i.Size >= 0.6f);
        bool bigHole = false;
        foreach (var (cell, _) in BigHoles(w.Ship)) if (Hull.InsideRoom(w.Ship, cell) is Room hr && !hr.Abandoned && !hr.Detached) { bigHole = true; break; }
        bool gas = w.Crew.Any(c => !c.Dead && c.Room != null && c.Suit == null && (c.Room.Air.Toxin > 0.15f || c.Room.Air.CO > 0.2f));
        bool blocked = w.Ship.Doors.Any(d => d.Blocked);
        if (recentBlast) s.Reasons.Add("폭발·충돌");
        if (bigHole && s.Breaches == 0) s.Reasons.Add("큰 구멍");
        if (gas) s.Reasons.Add("유독 가스·일산화탄소");
        int serious = (s.Down > 0 ? 1 : 0) + (s.Fires > 0 ? 1 : 0) + (reactorDown ? 1 : 0) + (s.Air ? 1 : 0) + (s.Breaches > 0 ? 1 : 0) + (s.Cooling ? 1 : 0)
                      + (recentBlast ? 1 : 0) + (bigHole ? 1 : 0) + (gas ? 1 : 0) + (blocked ? 1 : 0) + (recentCritical && serious0(s) == 0 ? 1 : 0);
        bool dying = reactorDown && p.BatteryPercent < 0.15f && !p.AuxRunning || s.Air && o2Low < 15f || s.Fires >= 6;
        s.Level = dying || serious >= 3 ? CrisisLevel.Survival
            : serious >= 1 ? CrisisLevel.Emergency
            : shedding || w.Ship.LiveRooms.Any(r => r.Leaking && !r.Abandoned) || w.Board.OpenUnsorted.Any(o => o.Urgency >= 0.9f) ? CrisisLevel.Alert
            : CrisisLevel.Calm;
        if (s.Level < CrisisLevel.Emergency && w.Scale?.ShipWide() is string big) { s.Level = CrisisLevel.Emergency; s.Reasons.Add(big); } // v16.18 배 전체 사고면 적어도 비상
        return s;
    }

    private static int serious0(Snapshot s) => s.Reasons.Count;

    // 통합 성능: 틱마다 모든 벽을 훑지 않는다 — 큰 구멍 후보(외벽 · 25% 넘게 뚫림 · 안 막음)는 벽이 바뀔 때만 다시 모은다 (방 조건은 매번 본다)
    private static Ship? _holeShip;
    private static int _holeWalls = -1, _holeVer = -1;
    private static readonly List<(Cell cell, WallState wall)> _holes = new();
    private static List<(Cell cell, WallState wall)> BigHoles(Ship ship)
    {
        if (!ReferenceEquals(_holeShip, ship) || _holeWalls != ship.WallsVersion || _holeVer != WallState.Version)
        {
            _holes.Clear();
            foreach (var kv in ship.Walls) if (kv.Value.IsHull && kv.Value.Breach >= 0.25f && !kv.Value.Patched) _holes.Add((kv.Key, kv.Value));
            _holeShip = ship; _holeWalls = ship.WallsVersion; _holeVer = WallState.Version;
        }
        return _holes;
    }

    /// <summary>전기를 만들고 나르는 설비 (원자로가 돌려면 필요한 것까지).</summary>
    public static bool PowerChain(FurnitureType t) => t is FurnitureType.ReactorCore or FurnitureType.CoolantPump or FurnitureType.PowerPanel or FurnitureType.Battery
        or FurnitureType.AuxGenerator or FurnitureType.HeatExchanger or FurnitureType.CapacitorBank or FurnitureType.MainComputer;

    /// <summary>숨 쉬는 데 드는 설비.</summary>
    public static bool AirChain(FurnitureType t) => t is FurnitureType.OxygenGenerator or FurnitureType.Scrubber;

    /// <summary>
    /// 이 일이 지금의 위기와 얼마나 맞닿아 있나 (0 = 전혀, 1 = 바로 그것). 평시에는 쓰지 않는다.
    /// </summary>
    public static float Relevance(World w, WorkOrder o)
    {
        var s = Now(w);
        var f = o.Target.Furniture;
        var m = f?.Machine;
        var room = o.Target.CurrentRoom;
        bool people = room != null && w.Crew.Any(c => !c.Dead && c.Room == room);
        switch (o.Kind)
        {
            // 사람부터
            case WorkKind.Rescue: return 1.2f;
            case WorkKind.Treat: return o.Urgency >= 0.9f ? 0.9f : 0.6f;
            // 불
            case WorkKind.Extinguish: return 1.1f;
            case WorkKind.OperateDamper or WorkKind.IsolateVent or WorkKind.CrankDoor or WorkKind.SealOffRoom or WorkKind.WeldBulkhead: return 0.95f;
            // 전기·냉각
            case WorkKind.RestartReactor or WorkKind.StartAux or WorkKind.ManualStart or WorkKind.Refuel or WorkKind.ResetBreaker or WorkKind.RestoreCircuit
                or WorkKind.InstallJumper or WorkKind.ShedLoad or WorkKind.IsolatePower or WorkKind.Brownout:
                return s.Power || s.Cooling ? 1.05f : 0.7f;
            case WorkKind.CloseValve or WorkKind.OpenValve or WorkKind.PatchPipe or WorkKind.ReplacePipe or WorkKind.LayBypass or WorkKind.RefillCoolant
                or WorkKind.RepairRadiator or WorkKind.IsolatePipes:
                return s.Power || s.Cooling ? 0.95f : 0.6f;
            // 공기
            case WorkKind.BuildOxygen: return 1f;
            case WorkKind.MeltIce: return s.Air ? 0.9f : 0.3f;
            // 구멍: 사람이 있는 방·핵심 방이면 급하다. 이미 막은 벽을 마저 용접하는 건 나중에
            case WorkKind.SealBreach:
                // 사람이 있는 방 > 큰 구멍이 난 핵심 방·통로 > 미세 누출 (작은 틈은 전기부터 살린 뒤에)
                if (people) return 0.95f;
                if (o.Urgency >= 1.2f) return room?.Furniture.Any(x => x.Machine is Machine mm && mm.Spec.Critical) == true || room?.Type is RoomType.Corridor ? 0.9f : 0.7f;
                return 0.45f;
            case WorkKind.RepairHull: return room?.Leaking == true ? 0.5f : 0.15f;
            // 구조: 떨어져 나가기 직전이면
            case WorkKind.RepairJoint or WorkKind.Clamp or WorkKind.InstallTruss or WorkKind.RebuildFrame: return o.Urgency >= 0.9f ? 0.8f : 0.3f;
            case WorkKind.InspectHull: return 0.1f;
            // v12.2 터지기 전에 식힌다 · 문을 막은 잔해 · 역화 · 산소관
            case WorkKind.CoolDown: return m != null && (m.Heat > 0.9f || m.Vapor > 0.5f) ? 1.1f : 0.85f;
            case WorkKind.ClearRubble: return o.Urgency >= 0.8f ? 0.85f : o.Urgency >= 0.6f ? 0.6f : 0.3f;
            case WorkKind.BleedRoom: return 0.75f;
            case WorkKind.SealO2Line: return 0.9f;
            case WorkKind.CleanUp: return 0.1f;
            case WorkKind.WakeCrew: return 0.95f;
            // v12.3 침수: 물 + 전기는 바로, 핵심 방 분전함은 급히 다시
            case WorkKind.IsolateRoom: return 1.0f;
            case WorkKind.ManualControl: return 0.8f;
            case WorkKind.PumpOut: return o.Urgency >= 0.7f ? 0.7f : 0.35f;
            case WorkKind.ShutRoomValve: return 0.7f;
            case WorkKind.OpenRoomValve: return 0.45f;
            case WorkKind.BreakerOn: return o.Urgency >= 0.9f ? (s.Power || s.Air ? 1.0f : 0.85f) : 0.45f;
            case WorkKind.RepairNet:
            {
                var l = w.Net.Links.FirstOrDefault(x => x.Id == o.Circuit);
                if (l == null || !l.Cut) return 0.3f;
                return l.Kind switch { NetKind.Power => 1.05f, NetKind.Air => s.Air ? 1f : 0.8f, NetKind.Data => 0.55f, _ => 0.6f };
            }
            case WorkKind.Rewire or WorkKind.Reline:
                if (m == null) return 0.4f;
                if (PowerChain(m.Body.Type)) return s.Power || s.Cooling ? 1.05f : 0.7f;
                if (AirChain(m.Body.Type)) return s.Air ? 1f : 0.7f;
                return m.Spec.Critical ? 0.6f : 0.3f;
            // 설비 수리: 전기·공기 사슬이면 앞으로, 전기가 없는 방의 설비는 전기가 돌아온 뒤에
            case WorkKind.Repair or WorkKind.InstallSubstitute or WorkKind.Cannibalize:
                if (m == null) return 0.5f;
                if (PowerChain(m.Body.Type)) return s.Power || s.Cooling ? 1.05f : 0.75f;
                if (AirChain(m.Body.Type)) return s.Air ? 1f : 0.75f;
                if (!m.Body.Room.Powered && m.Spec.PowerDraw > 0f && s.Power) return 0.2f;
                return m.Spec.Critical ? 0.65f : 0.3f;
            // 먹을 것은 굶기 전까지는 뒤로
            case WorkKind.Cook or WorkKind.Restock or WorkKind.Harvest or WorkKind.Tend: return o.Urgency >= 0.8f ? 0.5f : 0.2f;
            case WorkKind.ShelterFood: return o.Urgency >= 0.8f ? 0.7f : 0.1f; // 폭풍 속 대피소에 먹을 것을 — 숨은 사람들이 굶기 전에
            case WorkKind.Distress or WorkKind.RadarWatch or WorkKind.PilotDrones: return 0.7f;
            // 평시의 일
            case WorkKind.Maintain or WorkKind.PreventiveCheck or WorkKind.Upgrade or WorkKind.Fabricate or WorkKind.Train or WorkKind.Rehab or WorkKind.Calibrate
                or WorkKind.Drill or WorkKind.SuitCheck or WorkKind.Recycle or WorkKind.StowCot or WorkKind.StockCache or WorkKind.Salvage or WorkKind.RestoreGrade
                or WorkKind.ReplacePanel or WorkKind.RemoveJumper or WorkKind.UnstockDock or WorkKind.StockDock or WorkKind.ServiceDrone or WorkKind.ServiceRobot
                or WorkKind.EndBrownout or WorkKind.EndRation or WorkKind.ChangeCourse or WorkKind.RefillPropellant or WorkKind.DiscardFood or WorkKind.BuildWorkshop:
                return 0.1f;
            default: return o.Urgency >= 0.9f ? 0.8f : 0.45f;
        }
    }

    /// <summary>작업 매력에 더할 몫: 비상·생존 위기에 맞닿은 일은 올리고, 딴일은 내린다.</summary>
    public static float Bias(World w, WorkOrder o)
    {
        var level = Level(w);
        if (Disabled || level < CrisisLevel.Emergency) return 0f;
        float k = level == CrisisLevel.Survival ? 1.1f : 0.7f;
        return (Relevance(w, o) - 0.5f) * k;
    }

    /// <summary>비상 소집: 비번이어도 위기의 사슬에 걸린 일이면 나온다.</summary>
    public static bool AllHands(World w, WorkOrder o) => !Disabled && Level(w) >= CrisisLevel.Emergency && Relevance(w, o) >= 0.7f;

    /// <summary>위기 판단이 켜져 있고 비상 이상인지.</summary>
    public static bool Acting(World w) => !Disabled && Level(w) >= CrisisLevel.Emergency;

    /// <summary>
    /// 위기 때 쉬는 일(잠·휴식·수다·산책·당직·배고프지 않은 끼니)을 얼마나 미루나. 탈진 직전이면 쪽잠은 잔다.
    /// 사람마다 다르다: 성실한 사람은 더 버티고, 겁 많고 당황한 사람은 숨어 쉬기도 한다 (기계가 아니다).
    /// </summary>
    public static float Damp(CrewMember c, World w, Activity a, out string? note)
    {
        note = null;
        var level = Level(w);
        if (Disabled || level < CrisisLevel.Emergency) return 1f;
        // v13.1 교대: 지휘자가 쉬게 한 사람은 잔다
        if (w.Command.Resting(c) && a is SleepActivity or RelaxActivity or EatActivity) { note = "교대 — 쉬라는 지시"; return a is SleepActivity ? 1.6f : 1f; }
        bool survival = level == CrisisLevel.Survival;
        // v13.4 목표 계층: 조를 맡은 사람은 맡은 역할이 생활보다 먼저 — 굶주리거나 탈진 직전이 아니면 끼니·잠을 미룬다
        if (w.Command.Active && w.Command.TeamOf(c) is { Kind: not TeamKind.Reserve })
        {
            if (a is EatActivity && c.Needs.Hunger < 0.93f) { note = "조를 맡았다 — 끼니는 나중에"; return 0.2f; }
            if (a is SleepActivity && c.Needs.Fatigue < 0.95f && !w.Command.Resting(c)) { note = "조를 맡았다 — 잠은 나중에"; return 0.1f; }
        }
        // v13.2 방침(비상 소집: 해당 조만): 조에 들지 않은 비번은 그대로 잔다 (생존 위기는 빼고)
        if (!survival && w.Policies["muster"] == 0 && w.Command.Active && w.Command.TeamOf(c) is not { Kind: not TeamKind.Reserve }
            && a is SleepActivity or RelaxActivity) { note = "해당 조만 소집 — 비번"; return 1f; }
        float grit = Math.Clamp(0.55f * c.Traits.Diligence + 0.25f * c.Traits.Calm + 0.2f * c.Traits.Bravery - 0.3f * c.Needs.Stress, 0f, 1f);
        switch (a)
        {
            case SleepActivity:
                if (c.Needs.Fatigue >= 0.9f) { note = "탈진 직전 — 쪽잠"; return 0.8f; }
                note = "비상 — 잠을 미룬다";
                return (survival ? 0.12f : 0.3f) * (1.3f - 0.6f * grit);
            case RelaxActivity or ChatActivity or WanderActivity or HobbyActivity or TidyActivity or MendActivity or ReachOutActivity or InspectActivity:
                note = "비상 — 쉴 때가 아니다";
                return (survival ? 0.08f : 0.25f) * (1.4f - 0.8f * grit);
            case DutyActivity:
                return 0.5f;
            case EatActivity:
                if (c.Needs.Hunger > 0.75f) return 1f;
                note = "비상 — 끼니는 나중에";
                return 0.4f;
            default:
                return 1f;
        }
    }
}

public sealed partial class WorkBoard
{
    /// <summary>위기 중에 잠든 사람: 곯아떨어져 방송으로 못 깬 사람, 생존 위기인데 아직 자는 사람을 동료가 흔들어 깨운다.</summary>
    private void ScanWake(Poster post)
    {
        var w = _world;
        var level = Crisis.Level(w);
        if (Crisis.Disabled || level < CrisisLevel.Emergency) return;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Down || c.Pose != Pose.Sleeping) continue;
            if (!c.DeepAsleep && level < CrisisLevel.Survival) continue;
            if (c.Needs.Fatigue > 0.97f && level < CrisisLevel.Survival) continue; // 정말 쓰러지기 직전이면 둔다
            if (w.Policies["muster"] == 0 && level < CrisisLevel.Survival && w.Command.TeamOf(c) is not { Kind: not TeamKind.Reserve }) continue; // v13.2 해당 조만
            if (w.Policies["privacy"] == 1 && level < CrisisLevel.Survival && w.Command.TeamOf(c) is not { Kind: not TeamKind.Reserve }) continue; // v13.4 사생활 존중
            post(WorkKind.WakeCrew, WorkTarget.OfCrew(c), level == CrisisLevel.Survival ? 1.0f : 0.85f, Skill.Medicine,
                $"{Crisis.Now(w).Top} — {Ko.IGa(c.Name)} 아직 자고 있다");
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>잠든 동료를 흔들어 깨운다.</summary>
    private static Job? WakeCrew(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var sleeper = o.Target.Crew!;
        var near = Cell.Dirs8.Select(d => sleeper.Cell + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (near is not Cell spot) { blocked = "곁에 갈 수 없다"; return null; }
        var toils = new List<Toil> { new GotoToil(spot), new WaitToil(SimTime.Minutes(1), Pose.Standing, sleeper.Position) };
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            if (sleeper.Pose != Pose.Sleeping) return true;
            sleeper.DeepAsleep = false;
            sleeper.EndJob(world, ToilStatus.Interrupted);
            sleeper.Pose = Pose.Standing;
            sleeper.Interrupt(world);
            sleeper.Needs.Stress = MathF.Min(1f, sleeper.Needs.Stress + 0.08f);
            cm.Say(world, $"{sleeper.Name}, 일어나! {Crisis.Now(world).Top}");
            sleeper.Say(world, "뭐, 뭐야?!");
            world.Log.Add(world.Tick, LogKind.Warning, $"{Ko.EulReul(sleeper.Name)} 흔들어 깨웠다 ({Crisis.Now(world).Top})", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "깨우기", toils, $"{Ko.EulReul(sleeper.Name)} 깨우러 간다");
    }
}
