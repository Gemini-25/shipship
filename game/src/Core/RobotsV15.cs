using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15.7 로봇·드론 8 → 25: 선내 로봇 11 · 선외 드론 6.
// 새 종류는 이미 있는 행동 원형(운반·정비·재배·방재 로봇 · 검사·수리·견인·건설 드론)을 그대로 쓰고 특기만 다르다 —
//   맡는 일 · 속도 · 배터리(소모 배율) · 고장률 · 일 속도 · (원형마다) 거품 세기·순찰 간격·거드는 힘·수확량·돌봄·견인 힘.
// 배에 들이는 길은 개조 하나: 겪은 일이 그 로봇을 부르면 (굶주림 → 배식 로봇, 조명 고장 → 배선 로봇, 운석 → 정찰 드론 …)
// 회의를 거쳐 재료로 짜 들인다. 로봇은 원형의 집 방 벽가에 새 충전대를 달고, 드론은 드론 거치대의 빈자리에 든다. 종류마다 한 대.

public static class RobotsV15
{
    /// <summary>선내 로봇 한 종류의 특기 (원형의 행동을 쓴다).</summary>
    public sealed record BotRow(RobotKind Kind, string Name, RobotKind Base, WorkKind[] Jobs, float Speed, float Drain, float Fault, float Work,
        string Note, (ItemKind kind, int count)[] Cost, Func<World, (float, string)> Need)
    {
        public bool Fight { get; init; }
        public bool Patrol { get; init; }
        public bool Assist { get; init; }
        public float PatrolHours { get; init; } = 3f;
        public int PatrolRooms { get; init; } = 4;
        public float FoamRate { get; init; } = RobotSystem.FoamRate;
        public float FoamHours { get; init; } = RobotSystem.FoamHours;
        /// <summary>불 곁에서 닳는 배율 (방재 원형만).</summary>
        public float HeatWear { get; init; } = 1f;
        public float AssistBonus { get; init; } = RobotSystem.AssistBonus;
        public float Yield { get; init; } = 1f;
        public float Care { get; init; } = 0.9f;
        /// <summary>정비를 맡는 설비의 기술 (null이면 모두).</summary>
        public Skill? Only { get; init; }
    }

    /// <summary>선외 드론 한 종류의 특기.</summary>
    public sealed record DroneRow(DroneKind Kind, string Name, DroneKind Base, WorkKind[] Jobs, float Speed, float Drain, float Fault, float Work,
        string Note, (ItemKind kind, int count)[] Cost, Func<World, (float, string)> Need)
    {
        /// <summary>끌고 오는 힘 (견인 원형).</summary>
        public float Tow { get; init; } = 1f;
        /// <summary>한 번 나가 도는 방 수 (검사 원형).</summary>
        public int PatrolRooms { get; init; } = 5;
    }

    private static WorkKind[] J(params WorkKind[] x) => x;
    private static (ItemKind, int)[] C(params (ItemKind, int)[] x) => x;
    private static (float, string) No => (0f, "");
    private static int Sum(World w, Func<CrewMember, int> f) => w.Crew.Sum(f);
    private static IEnumerable<CropState> Crops(World w) =>
        w.Ship.FurnitureOf(FurnitureType.GrowBed).Where(f => !f.Room.Detached).Select(f => f.Machine?.Crop).OfType<CropState>();

    public static readonly BotRow[] Bots =
    {
        // ── 운반 원형 ──
        new(RobotKind.Courier, "배식 로봇", RobotKind.Hauler, J(WorkKind.Restock), 0.105f, 1.3f, 1f, 1.1f,
            "배식기 채우기만 · 빠르지만 배터리가 작다",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 1), (ItemKind.Plate, 1)),
            w => Evolution.StarvedHours(w) > 2f ? (0.4f, $"굶주린 {Evolution.StarvedHours(w):0}사람·시간") : No),
        new(RobotKind.Tanker, "급수 로봇", RobotKind.Hauler, J(WorkKind.CarryWater, WorkKind.RefillPropellant), 0.07f, 0.65f, 0.8f, 1.25f,
            "물통 급수 · 추진제 채우기 · 느리지만 배터리가 크다",
            C((ItemKind.Motor, 1), (ItemKind.Pump, 1), (ItemKind.Plate, 1)),
            w => Crops(w).Any(c => c.DryHours > 4f) ? (0.4f, "재배대가 말랐다")
                : w.Propulsion.Capacity > 0f && w.Propulsion.Propellant < 0.35f * w.Propulsion.Capacity ? (0.25f, "추진제가 바닥나 간다") : No),
        new(RobotKind.Stocker, "적재 로봇", RobotKind.Hauler, J(WorkKind.StockDock, WorkKind.StockCache, WorkKind.StowCot), 0.08f, 1f, 0.55f, 1.2f,
            "드론 자재 보급 · 비상 물자함 · 간이침대 접기 · 튼튼하다",
            C((ItemKind.Motor, 1), (ItemKind.Bearing, 1), (ItemKind.Plate, 1)),
            w => w.Drones.Waiting.Count > 0 ? (0.35f, "드론이 자재를 기다렸다")
                : w.Ledger.Mode != StockMode.Normal ? (0.2f, $"비축 방침 {Logistics.ModeName(w.Ledger.Mode)}") : No),
        // ── 정비 원형 ──
        new(RobotKind.Lineman, "배선 로봇", RobotKind.Maintainer, J(WorkKind.FixLights, WorkKind.Maintain), 0.08f, 1.1f, 1f, 1.15f,
            "조명 갈기 · 전기 설비 정비만 · 손이 빠르다",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 1), (ItemKind.Cable, 2)),
            w => w.Fixtures.LightFailures >= 2 ? (MathF.Min(0.6f, 0.25f + 0.05f * w.Fixtures.LightFailures), $"조명이 {w.Fixtures.LightFailures}번 나갔다") : No) { Only = Skill.Electrical },
        new(RobotKind.Assistant, "조수 로봇", RobotKind.Maintainer, J(), 0.085f, 0.8f, 0.9f, 1.4f,
            "사람 옆에서 거들기만 · 긴 손일이 55% 빨라진다",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 1), (ItemKind.Sensor, 1)),
            w => Sum(w, c => c.Stats.Repairs) >= 12 ? (0.3f, $"사람 손 수리 {Sum(w, c => c.Stats.Repairs)}번") : No) { Assist = true, AssistBonus = 0.55f },
        new(RobotKind.Overhauler, "정밀 정비 로봇", RobotKind.Maintainer, J(WorkKind.Maintain), 0.07f, 1.35f, 1.25f, 1.05f,
            "정기 정비 · 거들기 · 사람만큼 빠르지만 배터리를 많이 먹고 잘 고장 난다",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 2), (ItemKind.Sensor, 1)),
            w => w.Ship.Machines.Sum(m => m.FaultCount) is int n && n >= 8 ? (0.35f, $"설비 고장 {n}번") : No) { Assist = true },
        // ── 재배 원형 ──
        new(RobotKind.Harvester, "수확 로봇", RobotKind.Gardener, J(WorkKind.Harvest), 0.085f, 1.1f, 1f, 1.1f,
            "수확만 · 덜 흘려 20% 더 거둔다",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 1), (ItemKind.Plate, 1)),
            w => Evolution.StarvedHours(w) > 4f ? (0.35f, $"굶주린 {Evolution.StarvedHours(w):0}사람·시간")
                : Sum(w, c => c.Stats.Harvests) >= 8 ? (0.2f, $"사람이 {Sum(w, c => c.Stats.Harvests)}번 거뒀다") : No) { Yield = 1.2f },
        new(RobotKind.Tender, "돌봄 로봇", RobotKind.Gardener, J(WorkKind.Tend), 0.065f, 0.75f, 0.8f, 1f,
            "작물 돌보기만 · 끝까지 돌본다 · 느리다",
            C((ItemKind.Motor, 1), (ItemKind.Sensor, 1), (ItemKind.Nozzle, 1)),
            w => w.Hazards.Count[(int)HazardKind.CropBlight] + w.Hazards.Count[(int)HazardKind.NutrientCrash] + w.Hazards.Count[(int)HazardKind.SeedRot] >= 1
                ? (0.35f, "작물이 병들고 시들었다") : No) { Care = 1f },
        // ── 방재 원형 ──
        new(RobotKind.Sentry, "순찰 로봇", RobotKind.Safety, J(), 0.1f, 0.85f, 1f, 1f,
            "순찰만 (한 시간 반마다 여섯 방) · 거품이 없다",
            C((ItemKind.Motor, 1), (ItemKind.Sensor, 2), (ItemKind.Electronics, 1)),
            w => w.Hazards.Count.Sum() is int n && n >= 6 ? (0.3f, $"사고 {n}번") : No) { Patrol = true, PatrolHours = 1.5f, PatrolRooms = 6 },
        new(RobotKind.Firefighter, "소방 로봇", RobotKind.Safety, J(), 0.095f, 1.2f, 0.9f, 1f,
            "불 끄기만 · 거품이 세고 오래 간다 · 열에 강하다",
            C((ItemKind.Motor, 1), (ItemKind.Pump, 1), (ItemKind.Extinguisher, 1)),
            w => w.History.Fires >= 2 ? (0.45f, $"불이 {ShipHistory.Times(w.History.Fires)} 났다") : No) { Fight = true, FoamRate = 16f, FoamHours = 1f, HeatWear = 0.5f },
        // ── 여럿을 섞은 원형 (운반 몸) ──
        new(RobotKind.Utility, "잡역 로봇", RobotKind.Hauler, J(WorkKind.Restock, WorkKind.Tend, WorkKind.FixLights, WorkKind.StowCot), 0.075f, 1f, 1.1f, 1.6f,
            "배식기 · 작물 돌보기 · 조명 · 간이침대 — 무엇이든 하지만 서툴다",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 1), (ItemKind.Plate, 2)),
            w => w.History.Deaths >= 1 ? (0.45f, "사람을 잃어 손이 모자라다")
                : w.History.Collapses >= 2 ? (0.25f, $"쓰러진 일 {w.History.Collapses}번") : No),
        // 의료 3차 (MedBots.cs) — 일은 작업 목록이 아니라 주컴퓨터가 위중한 사람부터 맡긴다
        new(RobotKind.Stretcher, "들것 로봇", RobotKind.Hauler, J(), 0.08f, 1.2f, 0.9f, 1.2f,
            "쓰러진 사람을 들것에 실어 치료 침대 · 수술대로 · 가는 길에 상처를 누른다",
            C((ItemKind.Motor, 2), (ItemKind.Plate, 2), (ItemKind.Bearing, 1)),
            w => w.MedBots.LateRescues >= 1 ? (0.5f, $"쓰러진 사람을 늦게 옮겼다 ({w.MedBots.LateRescues}번)")
                : w.History.Collapses >= 2 ? (0.3f, $"쓰러진 일 {w.History.Collapses}번") : No),
        new(RobotKind.Nurse, "간호 로봇", RobotKind.Hauler, J(), 0.085f, 0.9f, 0.9f, 1.1f,
            "피 · 약 · 구급 키트 나르기 · 상처 누르기 · 인공 폐 손 펌프 · 격리실 소독",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 1), (ItemKind.Sensor, 1), (ItemKind.Pump, 1)),
            w => w.Blood.Transfusions + w.Organs.Stats.Cranked >= 3 ? (0.4f, $"수혈 · 손 펌프 {w.Blood.Transfusions + w.Organs.Stats.Cranked}번")
                : w.Infection.Stats.Isolated >= 1 ? (0.3f, "격리실을 썼다") : No),
    };

    public static readonly DroneRow[] Flyers =
    {
        new(DroneKind.Scout, "정찰 드론", DroneKind.Inspect, J(WorkKind.InspectHull), 0.3f, 0.27f, 1.5f, 1f,
            "외벽 검사 · 아주 빠르지만 배터리가 작고 잘 고장 난다",
            C((ItemKind.Motor, 1), (ItemKind.Sensor, 1), (ItemKind.Electronics, 1)),
            w => w.History.ImpactCells.Count >= 2 ? (MathF.Min(0.6f, 0.2f + 0.05f * w.History.ImpactCells.Count), $"운석이 {w.History.ImpactCells.Count}번 들어왔다") : No),
        new(DroneKind.Surveyor, "측량 드론", DroneKind.Inspect, J(WorkKind.InspectHull), 0.15f, 0.11f, 0.7f, 1f,
            "외벽 검사 · 느리지만 한 번에 여덟 방을 돈다",
            C((ItemKind.Motor, 1), (ItemKind.Sensor, 2), (ItemKind.Electronics, 1)),
            w => w.History.Doctrine.FrequentInspection ? (0.3f, "외벽을 자주 보기로 했다")
                : w.Structure.Joints.Count(j => j.KnownBroken) >= 2 ? (0.25f, "끊어진 연결부를 늦게 알았다") : No) { PatrolRooms = 8 },
        new(DroneKind.Welder, "용접 드론", DroneKind.Repair, J(WorkKind.RepairJoint, WorkKind.Clamp), 0.17f, 0.2f, 1f, 0.65f,
            "연결부 잇기 · 죔쇠 · 용접이 빠르다",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 1), (ItemKind.Plate, 1)),
            w => w.History.BreachesByRoom.Values.Sum() >= 2 ? (0.3f, "외벽이 여러 번 뚫렸다") : No),
        new(DroneKind.Radiator, "방열판 드론", DroneKind.Repair, J(WorkKind.RepairRadiator, WorkKind.ReleaseJoint), 0.2f, 0.16f, 0.9f, 0.55f,
            "방열판 수리 · 연결부 풀기 · 방열판을 절반 시간에",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 1), (ItemKind.Hose, 1)),
            w => w.History.Scrams >= 1 || w.Piping.Segments.Any(s => s.Radiator.Count > 0 && s.RadiatorCondition < 0.7f)
                ? (0.3f, w.History.Scrams >= 1 ? $"원자로가 {ShipHistory.Times(w.History.Scrams)} 섰다" : "방열판이 상했다") : No),
        new(DroneKind.Tug, "예인 드론", DroneKind.Tow, J(WorkKind.Retrieve), 0.12f, 0.13f, 0.8f, 1f,
            "떨어진 방·드론 끌어오기 · 느리게 날지만 1.7배 세게 끈다",
            C((ItemKind.Motor, 2), (ItemKind.Electronics, 1), (ItemKind.Plate, 1)),
            w => w.Ship.Rooms.Any(r => r.Detached && !r.Merged) ? (0.45f, "방을 떠나보낸 적이 있다")
                : w.Drones.Drones.Any(d => d.State is DroneState.Adrift or DroneState.Lost) ? (0.3f, "드론을 잃어 봤다") : No) { Tow = 1.7f },
        new(DroneKind.Rigger, "골조 드론", DroneKind.Build, J(WorkKind.RebuildFrame, WorkKind.InstallTruss, WorkKind.RepairJoint), 0.14f, 0.19f, 0.6f, 0.7f,
            "골조 다시 세우기 · 임시 트러스 · 연결부 · 튼튼하고 손이 빠르다",
            C((ItemKind.Motor, 1), (ItemKind.Electronics, 1), (ItemKind.Structure, 1)),
            w => w.Structure.FramesLost.Values.Sum() >= 1 ? (0.4f, $"골조를 {w.Structure.FramesLost.Values.Sum()}번 잃었다") : No),
    };

    private static readonly BotRow?[] BotOf = Index(Bots, b => (int)b.Kind, Enum.GetValues<RobotKind>().Length);
    private static readonly DroneRow?[] FlyerOf = Index(Flyers, d => (int)d.Kind, Enum.GetValues<DroneKind>().Length);
    private static T?[] Index<T>(T[] rows, Func<T, int> key, int n) where T : class
    {
        var a = new T?[n];
        foreach (var r in rows) a[key(r)] = r;
        return a;
    }

    public static BotRow? Bot(RobotKind k) => (int)k < BotOf.Length ? BotOf[(int)k] : null;
    public static DroneRow? Flyer(DroneKind k) => (int)k < FlyerOf.Length ? FlyerOf[(int)k] : null;

    /// <summary>모든 로봇·드론 종류 수 (8 → 25).</summary>
    public static int KindCount => Enum.GetValues<RobotKind>().Length + Enum.GetValues<DroneKind>().Length;

    // ── 로봇: 원형과 특기 (원래 넷은 원래 값 그대로) ──

    public static RobotKind Base(RobotKind k) => Bot(k)?.Base ?? k;
    public static bool CanDo(RobotKind k, WorkKind w) => Bot(k) is { } b && Array.IndexOf(b.Jobs, w) >= 0;
    /// <summary>그 일을 맡을지 (전기 설비만 정비하는 배선 로봇처럼 설비를 가리는 종류).</summary>
    public static bool Takes(RobotKind k, WorkOrder o) =>
        Bot(k) is not { Only: Skill only } || o.Kind != WorkKind.Maintain || o.Target.Furniture?.Machine?.Spec.Skill == only;
    public static float Speed(RobotKind k) => Bot(k)?.Speed ?? 0.075f;
    public static float Work(RobotKind k) => Bot(k)?.Work ?? 1.3f;
    public static float Drain(RobotKind k) => Bot(k)?.Drain ?? 1f;
    public static float Fault(RobotKind k) => Bot(k)?.Fault ?? 1f;
    public static bool Fights(RobotKind k) => k == RobotKind.Safety || Bot(k)?.Fight == true;
    public static bool Patrols(RobotKind k) => k == RobotKind.Safety || Bot(k)?.Patrol == true;
    public static bool Assists(RobotKind k) => k == RobotKind.Maintainer || Bot(k)?.Assist == true;
    /// <summary>불 곁에서 그을리지 않는다 (방재 원형).</summary>
    public static bool Fireproof(RobotKind k) => Base(k) == RobotKind.Safety;
    public static float HeatWear(RobotKind k) => Bot(k)?.HeatWear ?? 1f;
    public static float FoamRate(RobotKind k) => Bot(k)?.FoamRate ?? RobotSystem.FoamRate;
    public static float FoamHours(RobotKind k) => Bot(k)?.FoamHours ?? RobotSystem.FoamHours;
    public static float PatrolHours(RobotKind k) => Bot(k)?.PatrolHours ?? 3f;
    public static int PatrolRooms(RobotKind k) => Bot(k)?.PatrolRooms ?? 4;
    public static float AssistBonus(RobotKind k) => Bot(k)?.AssistBonus ?? RobotSystem.AssistBonus;
    public static float Yield(RobotKind k) => Bot(k)?.Yield ?? 1f;
    public static float Care(RobotKind k) => Bot(k)?.Care ?? 0.9f;

    // ── 드론 ──

    public static DroneKind Base(DroneKind k) => Flyer(k)?.Base ?? k;
    public static bool CanDo(DroneKind k, WorkKind w) => Flyer(k) is { } d && Array.IndexOf(d.Jobs, w) >= 0;
    public static float Speed(DroneKind k) => Flyer(k)?.Speed ?? 0.15f;
    public static float Drain(DroneKind k) => Flyer(k)?.Drain ?? 0.16f;
    public static float Fault(DroneKind k) => Flyer(k)?.Fault ?? 1f;
    public static float Work(DroneKind k) => Flyer(k)?.Work ?? 1f;
    public static float Tow(DroneKind k) => Flyer(k)?.Tow ?? 1f;
    public static int PatrolRooms(DroneKind k) => Flyer(k)?.PatrolRooms ?? 5;

    // ─────────────────────────────── 개조로 들이기 ───────────────────────────────
    // 개조 안의 Circuit 칸에 종류를 적는다: 로봇은 (int)RobotKind, 드론은 DroneCode + (int)DroneKind.

    public const int DroneCode = 100;
    public static int Code(RobotKind k) => (int)k;
    public static int Code(DroneKind k) => DroneCode + (int)k;

    public static string NameOf(int code) =>
        code >= DroneCode ? Flyer((DroneKind)(code - DroneCode))?.Name ?? "드론" : Bot((RobotKind)code)?.Name ?? "로봇";

    public static (ItemKind kind, int count)[] Cost(int code) =>
        (code >= DroneCode ? Flyer((DroneKind)(code - DroneCode))?.Cost : Bot((RobotKind)code)?.Cost) ?? new[] { (ItemKind.Motor, 1), (ItemKind.Electronics, 1) };

    private static bool Aboard(World w, RobotKind k) => w.Robots.Robots.Any(r => r.Kind == k && r.State != RobotState.Lost);
    private static bool Aboard(World w, DroneKind k) => w.Drones.Drones.Any(d => d.Kind == k && d.State != DroneState.Lost);

    /// <summary>개조 후보: 지금 가장 부르는 로봇·드론 하나 (겪은 일이 있어야 · 종류마다 한 대 · 둘 자리가 있어야).</summary>
    public static IEnumerable<UpgradePlan> Candidates(World w)
    {
        BotRow? bot = null; DroneRow? flyer = null;
        float best = 0f; string why = "";
        foreach (var b in Bots)
        {
            if (Aboard(w, b.Kind)) continue;
            var (need, text) = b.Need(w);
            if (need > best) { best = need; why = text; bot = b; flyer = null; }
        }
        bool hangar = DroneSystem.Hatch(w) != null && DroneDock(w) != null;
        if (hangar)
            foreach (var d in Flyers)
            {
                if (Aboard(w, d.Kind)) continue;
                var (need, text) = d.Need(w);
                if (need > best) { best = need; why = text; flyer = d; bot = null; }
            }
        if (bot != null && DockSpot(w, bot.Base) is { } spot)
            yield return new UpgradePlan(UpgradeKind.Robot, WorkTarget.AtCell(spot.at, spot.room), 0.2f + best, Skill.Electrical,
                $"{why} → {spot.room.Name}에 {bot.Name} ({bot.Note} · {Costs(bot.Cost)})", bot.Cost, 0.3f, Code(bot.Kind));
        else if (flyer != null && DroneDock(w) is Furniture dock)
            yield return new UpgradePlan(UpgradeKind.Robot, WorkTarget.Of(dock), 0.2f + best, Skill.Electrical,
                $"{why} → {dock.Label}에 {flyer.Name} ({flyer.Note} · {Costs(flyer.Cost)})", flyer.Cost, 0.3f, Code(flyer.Kind));
    }

    private static string Costs((ItemKind kind, int count)[] cost) => string.Join(" + ", cost.Select(c => $"{ItemKinds.Name(c.kind)} {c.count}"));

    /// <summary>새 충전대 자리: 원형의 집 방 벽가, 앞에 설 자리가 있고 길을 막지 않는 곳.</summary>
    public static (Cell at, Room room)? DockSpot(World w, RobotKind baseKind, Cell? prefer = null)
    {
        var ship = w.Ship;
        bool Fits(Room room, Cell c) =>
            Cell.Dirs4.Any(d => ship.Grid.Kind(c + d) == TileKind.Wall) && Cell.Dirs4.Any(d => ship.IsOpenFloor(c + d) && ship.RoomAt(c + d) == room);
        if (prefer is Cell p && ship.RoomAt(p) is Room pr && !pr.OffLimits && !pr.Abandoned && Adaptation.FreeCells(w, pr).Contains(p) && Fits(pr, p)
            && Adaptation.SafeToBlock(w, pr, p))
            return (p, pr);
        foreach (var type in RobotSystem.HomeRooms(baseKind))
            foreach (var room in ship.RoomsOf(type).OrderBy(r => r.Id))
            {
                if (room.OffLimits || room.Abandoned || room.Leaking) continue;
                foreach (var c in Adaptation.FreeCells(w, room).Where(c => Fits(room, c)).OrderBy(c => c.Y).ThenBy(c => c.X))
                    if (Adaptation.SafeToBlock(w, room, c)) return (c, room);
            }
        return null;
    }

    /// <summary>새 드론이 들 거치대: 드론이 가장 적은 곳.</summary>
    public static Furniture? DroneDock(World w) =>
        w.Ship.FurnitureOf(FurnitureType.DroneDock).Where(f => !f.Room.OffLimits && !f.Room.Abandoned)
            .OrderBy(f => w.Drones.Drones.Count(d => d.Dock == f)).ThenBy(f => f.MinX).FirstOrDefault();

    /// <summary>들인다: 로봇은 새 충전대와 함께, 드론은 거치대 빈자리에. 개조를 마친 사람(by)이 짜 넣는다.</summary>
    public static bool Install(World w, int code, CrewMember? by, out string text, Cell? at = null)
    {
        text = "";
        string who = by != null ? Ko.IGa(by.Name) + " " : "";
        if (code >= DroneCode)
        {
            if (Flyer((DroneKind)(code - DroneCode)) is not { } row || DroneDock(w) is not Furniture dock) return false;
            var ds = w.Drones;
            int nth = ds.Drones.Count(x => x.Kind == row.Kind) + 1;
            var d = new Drone
            {
                Id = ds.Drones.Count, Kind = row.Kind, Dock = dock, Slot = ds.Drones.Count(x => x.Dock == dock),
                Name = nth > 1 ? $"{row.Name} {nth}" : row.Name, Condition = 1f,
            };
            d.Position = d.DockPosition;
            d.PreviousPosition = d.Position;
            ds.Drones.Add(d);
            MarkLog.Add(d.Marks, w.Tick, by != null ? $"{by.Name}: 짜 들였다" : "들였다");
            MarkLog.Add(dock.Room.Marks, w.Tick, $"{row.Name} 들임");
            text = $"{who}{dock.Label}에 {Ko.EulReul(row.Name)} 짜 들였다 — {row.Note}";
            return true;
        }
        if (Bot((RobotKind)code) is not { } bot || DockSpot(w, bot.Base, at) is not { } place) return false;
        var (cell, room) = place;
        var charger = w.Ship.AddFurniture(FurnitureType.RobotDock, cell);
        if (charger.Machine != null) { charger.Machine.Wear = 0f; charger.Machine.Condition = 1f; }
        w.Paths.Invalidate();
        w.Structure.Touch();
        var rs = w.Robots;
        int n = rs.Robots.Count(x => x.Kind == bot.Kind) + 1;
        var r = new Robot
        {
            Id = rs.Robots.Count, Kind = bot.Kind, Dock = charger, Slot = 0,
            Name = n > 1 ? $"{bot.Name} {n}" : bot.Name, Condition = 1f,
        };
        r.Position = r.DockPosition;
        r.PreviousPosition = r.Position;
        r.Room = room;
        r.NextPatrol = w.Tick + SimTime.Minutes(30);
        rs.Robots.Add(r);
        MarkLog.Add(r.Marks, w.Tick, by != null ? $"{by.Name}: 짜 들였다" : "들였다");
        MarkLog.Add(room.Marks, w.Tick, $"{bot.Name} 충전대 설치");
        text = $"{who}{room.Name}에 충전대를 달고 {Ko.EulReul(bot.Name)} 짜 들였다 — {bot.Note}";
        return true;
    }
}
