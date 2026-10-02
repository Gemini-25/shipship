using System;

namespace ShipSim.Core;

// v16.19 내구 · 고장률 재조정 — 한곳에 모은 배율.
//  "픽하면 고장" 이 안 되게: 설비 · 로봇 · 드론 · 전력망 · 데이터선의 고장률 · 충격 피해 · 과열을 여기서 고르고,
//  Legacy 를 켜면 예전 값으로 돌아간다 (시험의 전/후 측정표가 같은 실행 파일로 잰다 — 놀이에서는 늘 꺼져 있다).
//  목표(측정 1의 숫자로): 보통 재해 한 번에 필수 방이 몇 시간씩 캄캄하지 않다 · 로봇 · 드론이 첫 불에 그을려 멈추지 않는다 ·
//  설비 고장은 하루 한두 번 (닳은 것부터) · 운석 파편이 회로 · 망을 끊는 일은 큰 것에서만 자주.
public static class Durability
{
    /// <summary>시험용: 예전(v16.18까지) 값으로 잰다.</summary>
    public static bool Legacy;

    private static float L(float now) => Legacy ? 1f : now;

    // ── 설비 ──
    /// <summary>시간당 고장 확률 배율 (닳기 전엔 거의 안 난다 · 닳으면 예전처럼).</summary>
    public static float MachineFault(Machine m) => Legacy ? 1f : m.Wear < 0.5f ? 0.45f : 0.75f;
    /// <summary>파편이 설비를 맞혔을 때 수명 · 고장 배율 (강화 외함은 더 작다).</summary>
    public static float ShardOnMachine(Machine m) => Legacy ? 1f : Shielded(m.Body.Type) ? 0.35f : 0.65f;
    /// <summary>강화 외함 (원자로 · 배전반 · 주 컴퓨터 · 산소 발생기 · 배터리).</summary>
    public static bool Shielded(FurnitureType t) => t is FurnitureType.ReactorCore or FurnitureType.PowerPanel or FurnitureType.MainComputer
        or FurnitureType.OxygenGenerator or FurnitureType.Battery or FurnitureType.AuxGenerator;

    // ── 전력망 · 데이터선 ──
    /// <summary>운석 파편이 배전 회로를 끊을 확률 배율.</summary>
    public static float CircuitHit => L(0.45f);
    /// <summary>파편 · 불이 간선 · 관 · 덕트 · 데이터선을 상하게 하는 배율 (전선관 · 배관 받침).</summary>
    public static float NetHit => L(0.6f);

    // ── 로봇 ──
    /// <summary>로봇 닳음 고장률.</summary>
    public static float RobotFault => L(0.4f);
    /// <summary>불 곁에서 그을릴 확률 (방열 외피).</summary>
    public static float RobotScorch => L(0.35f);
    /// <summary>로봇이 닳는 속도.</summary>
    public static float RobotWear => L(0.6f);
    /// <summary>배터리 소모 배율 (큰 셀).</summary>
    public static float RobotDrain => L(0.75f);

    // ── 드론 ──
    /// <summary>드론 부위 피해 배율 (운석 · 파편 · 충돌).</summary>
    public static float DroneHurt => L(0.55f);
    /// <summary>드론 배터리 소모 배율.</summary>
    public static float DroneDrain => L(0.8f);

    // ── 공기 ──
    /// <summary>
    /// 벽 하나의 실제 누출 넓이: 미세 누출(균열 · 바늘구멍)은 쉬익 천천히 · 파공(0.25 넘는 구멍)은 그대로.
    /// 예전엔 미세 누출도 넓이 그대로라 작은 방이 몇 분 만에 비었다.
    /// </summary>
    public static float LeakArea(float breach) => Legacy || breach >= 0.25f ? breach : breach * 0.3f;
}

/// <summary>v16.19 필수도 — 전력이 모자라면 아래 등급부터 꺼진다 (비필수 → 지원 → 운항 핵심 → 생명).</summary>
public enum Essential : byte { Vital, Core, Support, Comfort }

/// <summary>
/// v16.19 설비 · 방마다 필수도 표 (공개). 우아한 저하의 순서: 편의 → 지원 → 운항 핵심 → 생명.
/// 컴퓨터 트리아지(v16.20)가 이 표로 전력을 몰아주고, 부하 차단 계전기(FailsafeSystem)는 컴퓨터 없이도 이 표로 내린다.
/// </summary>
public static class Essentials
{
    public static Essential Of(FurnitureType t) => t switch
    {
        FurnitureType.OxygenGenerator or FurnitureType.CoolantPump or FurnitureType.PowerPanel or FurnitureType.MainComputer
            or FurnitureType.MedBed or FurnitureType.ReactorCore or FurnitureType.Battery or FurnitureType.AuxGenerator
            or FurnitureType.Scrubber or FurnitureType.HeatExchanger or FurnitureType.AirPurifier or FurnitureType.EmergencyLight => Essential.Vital,
        FurnitureType.WaterRecycler or FurnitureType.Console or FurnitureType.SensorArray or FurnitureType.DroneDock or FurnitureType.RobotDock
            or FurnitureType.Fridge or FurnitureType.GrowBed or FurnitureType.LeakDetector or FurnitureType.NavComputer or FurnitureType.SurgeProtector
            or FurnitureType.CapacitorBank or FurnitureType.AirlockPump or FurnitureType.SignalBooster => Essential.Core,
        FurnitureType.MealDispenser or FurnitureType.Stove or FurnitureType.Refinery or FurnitureType.Fabricator or FurnitureType.Workbench
            or FurnitureType.EngineCore or FurnitureType.Collector or FurnitureType.Dehumidifier or FurnitureType.Autoclave or FurnitureType.DeconShower
            or FurnitureType.VibrationMonitor or FurnitureType.ThermalCamera or FurnitureType.CalibrationRig or FurnitureType.DiagnosticScanner
            or FurnitureType.NutrientDoser or FurnitureType.PartTestBench or FurnitureType.Hoist or FurnitureType.Oven or FurnitureType.LedPanel
            or FurnitureType.SuitLocker or FurnitureType.SuitDryer or FurnitureType.Lathe or FurnitureType.SolderStation or FurnitureType.ReactorSimulator => Essential.Support,
        _ => Essential.Comfort,
    };

    public static Essential Of(RoomType t) => t switch
    {
        RoomType.LifeSupport or RoomType.Reactor or RoomType.Cooling or RoomType.Power or RoomType.Medbay or RoomType.ServerRoom
            or RoomType.Substation or RoomType.BatteryRoom or RoomType.HvacRoom or RoomType.Triage or RoomType.Shelter => Essential.Vital,
        RoomType.Bridge or RoomType.BackupBridge or RoomType.Comms or RoomType.Navigation or RoomType.Airlock or RoomType.Hydroponics
            or RoomType.WaterPlant or RoomType.PumpRoom or RoomType.Corridor or RoomType.SuppressionRoom or RoomType.DroneBay or RoomType.RobotBay => Essential.Core,
        RoomType.Galley or RoomType.Storage or RoomType.Workshop or RoomType.Engine or RoomType.Quarters or RoomType.Freezer or RoomType.Mess
            or RoomType.EvaPrep or RoomType.Quarantine or RoomType.Decon or RoomType.Lab or RoomType.AlgaeLab or RoomType.ProteinFarm => Essential.Support,
        _ => Essential.Comfort,
    };

    /// <summary>방의 필수도: 방 종류와 안의 가장 필수인 설비 중 높은 쪽 (주 컴퓨터가 든 함교는 생명 등급).</summary>
    public static Essential Of(Room r)
    {
        var e = Of(r.Type);
        foreach (var f in r.Furniture)
            if (f.Machine != null && Of(f.Type) < e && (f.Type is FurnitureType.MainComputer or FurnitureType.OxygenGenerator or FurnitureType.MedBed or FurnitureType.PowerPanel)) e = Of(f.Type);
        return e;
    }

    public static string Name(Essential e) => e switch { Essential.Vital => "생명 유지", Essential.Core => "운항", Essential.Support => "작업", _ => "생활 편의" };
    public static string Mark(Essential e) => e switch { Essential.Vital => "Ⅰ", Essential.Core => "Ⅱ", Essential.Support => "Ⅲ", _ => "Ⅳ" };

    /// <summary>꺼지는 순서 (작을수록 먼저 꺼진다): 편의 0 · 지원 1 · 운항 핵심 2 · 생명 3.</summary>
    public static int ShedOrder(Essential e) => 3 - (int)e;

    /// <summary>두 갈래 급전을 받는 방 (생명 등급 · 함교 · 주 컴퓨터가 든 방).</summary>
    public static bool DualFeed(Room r) => !r.Detached && r.Type != RoomType.Corridor && (Of(r) == Essential.Vital || r.Type is RoomType.Bridge or RoomType.BackupBridge);
}
