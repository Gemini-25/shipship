using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

public enum Skill { Engineering, Electrical, Mechanics, Medicine, Botany, Cooking, Piloting }

public static class Skills
{
    public static readonly Skill[] All = (Skill[])Enum.GetValues(typeof(Skill));

    public static string Name(Skill s) => s switch
    {
        Skill.Engineering => "기관",
        Skill.Electrical => "전기",
        Skill.Mechanics => "정비",
        Skill.Medicine => "의료",
        Skill.Botany => "재배",
        Skill.Cooking => "조리",
        Skill.Piloting => "항법",
        _ => s.ToString(),
    };
}

// ─────────────────────────────── 고장 ───────────────────────────────

public enum FaultKind
{
    BearingWear, PumpSeized, FilterClogged, ElectrolyzerFault, MembraneFouling,
    SensorDrift, ControlFault, BreakerTrip, ShortCircuit, CellDegradation,
    LightFailure, NutrientClog, HeatingElement, CompressorFail, Jam, DisplayFault,
    InjectorClog, WiringFault,
    Stripped, // 다른 설비를 살리려고 부품을 뜯어냈다
    Wrecked,  // 파손: 수명이 바닥나 통째로 갈아야 한다 (핵심 부품 + 금속판, 아니면 Mk.1 대체품)
    SensorFouling, ArmMotor, FurnaceFault, HopperJam, // 채집 장치·정제기
    ChargerFault, // 드론 거치대 충전 회로 (v8)
    Overheat, StorageFault, // 주 컴퓨터 과열 정지, 저장장치 오류 (v9)
    AntennaDrift, RadarFault, // 장거리 센서 안테나 정렬, 레이더 송수신기 (v10.1)
    GasLeak, // v11.2 냉매(유독 가스) 누출 — 사고로만 난다, 실링폼으로 막는다
}

/// <summary>고장 종류별 성질. 출력 배율 0이면 완전히 멈춘다.</summary>
public sealed record FaultSpec(FaultKind Kind, string Name, float OutputFactor, ItemKind? Part, float RepairHours);

public static class Faults
{
    private static readonly Dictionary<FaultKind, FaultSpec> Table = new[]
    {
        new FaultSpec(FaultKind.BearingWear, "베어링 마모", 0.5f, ItemKind.Bearing, 1.5f),
        new FaultSpec(FaultKind.PumpSeized, "펌프 고착", 0f, ItemKind.Pump, 2.5f),
        new FaultSpec(FaultKind.FilterClogged, "필터 막힘", 0.4f, ItemKind.Filter, 0.75f),
        new FaultSpec(FaultKind.ElectrolyzerFault, "전해조 고장", 0f, ItemKind.PowerController, 3f),
        new FaultSpec(FaultKind.MembraneFouling, "멤브레인 오염", 0.5f, ItemKind.Filter, 1f),
        new FaultSpec(FaultKind.SensorDrift, "센서 오차", 0.7f, null, 1f),
        new FaultSpec(FaultKind.ControlFault, "제어봉 구동 불량", 0.5f, ItemKind.ReactorControl, 3f),
        new FaultSpec(FaultKind.BreakerTrip, "차단기 트립", 1f, null, 0.25f),
        new FaultSpec(FaultKind.ShortCircuit, "단락", 1f, ItemKind.Fuse, 1.5f),
        new FaultSpec(FaultKind.CellDegradation, "셀 열화", 0.5f, ItemKind.PowerController, 2f),
        new FaultSpec(FaultKind.LightFailure, "생장등 고장", 0.3f, ItemKind.Cable, 0.75f),
        new FaultSpec(FaultKind.NutrientClog, "양액 펌프 막힘", 0.5f, null, 0.5f),
        new FaultSpec(FaultKind.HeatingElement, "열선 단선", 0f, ItemKind.Cable, 1f),
        new FaultSpec(FaultKind.CompressorFail, "압축기 고장", 0f, ItemKind.Motor, 2f),
        new FaultSpec(FaultKind.Jam, "배출구 걸림", 0f, null, 0.3f),
        new FaultSpec(FaultKind.DisplayFault, "화면 고장", 0f, ItemKind.Electronics, 0.5f),
        new FaultSpec(FaultKind.InjectorClog, "인젝터 막힘", 0.5f, ItemKind.Lubricant, 1f),
        new FaultSpec(FaultKind.WiringFault, "배선 불량", 0.5f, ItemKind.Cable, 1f),
        new FaultSpec(FaultKind.Stripped, "부품 적출", 0f, null, 1.5f),
        new FaultSpec(FaultKind.Wrecked, "파손", 0f, null, 4f),
        new FaultSpec(FaultKind.SensorFouling, "수집 센서 오염", 0.4f, ItemKind.Sensor, 1f),
        new FaultSpec(FaultKind.ArmMotor, "채집 팔 모터 고장", 0f, ItemKind.Motor, 2f),
        new FaultSpec(FaultKind.FurnaceFault, "가열로 고장", 0f, ItemKind.PowerController, 2.5f),
        new FaultSpec(FaultKind.HopperJam, "투입구 막힘", 0f, null, 0.5f),
        new FaultSpec(FaultKind.ChargerFault, "충전 회로 고장", 0f, ItemKind.Electronics, 1f),
        new FaultSpec(FaultKind.Overheat, "과열 정지", 0f, null, 0.5f),
        new FaultSpec(FaultKind.StorageFault, "저장장치 오류", 0f, ItemKind.Electronics, 2f),
        new FaultSpec(FaultKind.AntennaDrift, "안테나 정렬 틀어짐", 0.35f, null, 1f),
        new FaultSpec(FaultKind.RadarFault, "레이더 송수신기 고장", 0f, ItemKind.Sensor, 2f),
        new FaultSpec(FaultKind.GasLeak, "냉매 누출", 0.8f, ItemKind.Sealant, 0.75f),
    }.ToDictionary(f => f.Kind);

    public static FaultSpec Spec(FaultKind k) => Table[k];

    /// <summary>
    /// 이 설비를 뜯으면 얻을 수 있는 부품. 부품이 바닥났을 때 덜 중요한 설비를 뜯어 급한 설비를 살린다 (카니발라이징).
    /// </summary>
    public static ItemKind[] SalvageOf(FurnitureType t) => t switch
    {
        FurnitureType.Console => new[] { ItemKind.Cable, ItemKind.Fuse, ItemKind.Electronics, ItemKind.Sensor },
        FurnitureType.MedBed => new[] { ItemKind.Electronics, ItemKind.Sensor, ItemKind.Cable },
        FurnitureType.Workbench => new[] { ItemKind.Motor, ItemKind.Cable },
        FurnitureType.Fridge => new[] { ItemKind.Motor },
        FurnitureType.Stove => new[] { ItemKind.Cable },
        FurnitureType.MealDispenser => new[] { ItemKind.Motor, ItemKind.Fuse },
        FurnitureType.GrowBed => new[] { ItemKind.Cable },
        FurnitureType.SuitLocker => new[] { ItemKind.Cable, ItemKind.Fuse },
        FurnitureType.Battery => new[] { ItemKind.PowerController },
        FurnitureType.EngineCore => new[] { ItemKind.Motor, ItemKind.Bearing, ItemKind.Lubricant, ItemKind.Plate },
        FurnitureType.WaterRecycler => new[] { ItemKind.Filter, ItemKind.Pump },
        FurnitureType.OxygenGenerator => new[] { ItemKind.Filter, ItemKind.PowerController },
        FurnitureType.CoolantPump => new[] { ItemKind.Pump, ItemKind.Bearing },
        FurnitureType.AuxGenerator => new[] { ItemKind.Motor, ItemKind.Bearing },
        FurnitureType.Collector => new[] { ItemKind.Motor, ItemKind.Sensor },
        FurnitureType.Refinery => new[] { ItemKind.PowerController, ItemKind.Plate },
        FurnitureType.DroneDock => new[] { ItemKind.Electronics, ItemKind.Cable },
        FurnitureType.MainComputer => new[] { ItemKind.Electronics, ItemKind.Sensor },
        FurnitureType.SensorArray => new[] { ItemKind.Sensor, ItemKind.Electronics, ItemKind.Cable },
        FurnitureType.RobotDock => new[] { ItemKind.Electronics, ItemKind.Cable },
        _ => Array.Empty<ItemKind>(),
    };

    /// <summary>
    /// 그 설비의 핵심 부품: 파손된 설비를 다시 세우거나 Mk.1 임시품을 정품으로 되돌릴 때 필요하다.
    /// </summary>
    public static ItemKind KeyPart(FurnitureType t) => t switch
    {
        FurnitureType.CoolantPump or FurnitureType.WaterRecycler => ItemKind.Pump,
        FurnitureType.OxygenGenerator or FurnitureType.Battery or FurnitureType.Refinery or FurnitureType.PowerPanel => ItemKind.PowerController,
        FurnitureType.ReactorCore => ItemKind.ReactorControl,
        FurnitureType.Fridge or FurnitureType.AuxGenerator or FurnitureType.Workbench or FurnitureType.Collector
            or FurnitureType.MealDispenser or FurnitureType.EngineCore => ItemKind.Motor,
        FurnitureType.Console or FurnitureType.MedBed or FurnitureType.DroneDock or FurnitureType.MainComputer or FurnitureType.RobotDock => ItemKind.Electronics,
        FurnitureType.SensorArray => ItemKind.Sensor,
        _ => ItemKind.Cable,
    };

    /// <summary>
    /// Mk.1 임시 대체품을 현장에서 만드는 데 드는 기본 수리재 (정품 부품 없이, 작업대 없이).
    /// 전자 쪽 설비는 전자재가 더 든다.
    /// </summary>
    public static (ItemKind kind, int count)[] SubstituteCost(FurnitureType t)
    {
        var key = KeyPart(t);
        bool electronic = key is ItemKind.PowerController or ItemKind.ReactorControl or ItemKind.Electronics or ItemKind.Sensor;
        return electronic
            ? new[] { (ItemKind.Plate, 1), (ItemKind.Cable, 2), (ItemKind.Electronics, 1) }
            : new[] { (ItemKind.Plate, 2), (ItemKind.Cable, 1) };
    }
}

/// <summary>
/// 설비 등급. 처음 우주선은 모두 정품(공장 표준)이다.
/// 정품 부품이 없으면 기본 수리재로 현장에서 만든 Mk.1 임시품을 단다 — 없는 것보다 훨씬 낫지만
/// 출력이 낮고 전기를 더 먹고 빨리 닳고 자주 고장 난다. 부품이 생기면 정품으로 되돌린다.
/// </summary>
public enum MachineGrade { Mk1, Standard, Mk3 }

/// <summary>
/// v7: 평화로울 때 재료가 남으면, 자주 말썽이던 설비를 Mk.3 개량형으로 고쳐 짠다 (겪은 고장이 가르쳐 준 곳).
/// 출력이 조금 높고, 덜 닳고, 덜 고장 난다. 파손되거나 Mk.1로 바뀌면 개량은 사라진다.
/// </summary>
public static class Grades
{
    public static string Name(MachineGrade g) => g switch { MachineGrade.Mk1 => "Mk.1 임시품", MachineGrade.Mk3 => "Mk.3 개량형", _ => "정품" };
    public static float Output(MachineGrade g) => g switch { MachineGrade.Mk1 => 0.65f, MachineGrade.Mk3 => 1.15f, _ => 1f };
    public static float Power(MachineGrade g) => g switch { MachineGrade.Mk1 => 1.3f, MachineGrade.Mk3 => 0.95f, _ => 1f };
    public static float Wear(MachineGrade g) => g switch { MachineGrade.Mk1 => 1.8f, MachineGrade.Mk3 => 0.7f, _ => 1f };
    public static float FaultRate(MachineGrade g) => g switch { MachineGrade.Mk1 => 2f, MachineGrade.Mk3 => 0.6f, _ => 1f };
}

public sealed class Fault
{
    public FaultKind Kind { get; init; }
    public long Since { get; init; }

    /// <summary>배전반 고장이면 어느 회로인지.</summary>
    public int Circuit { get; init; } = -1;

    public FaultSpec Spec => Faults.Spec(Kind);

    /// <summary>뜯어낸 부품처럼 고장마다 필요한 부품이 다를 때.</summary>
    public ItemKind? PartOverride { get; init; }

    /// <summary>완전히 고치는 데 필요한 부품.</summary>
    public ItemKind? Part => PartOverride ?? Spec.Part;

    /// <summary>완전히 고치는 데 드는 재료 전부 (파손은 핵심 부품 + 금속판 둘).</summary>
    public (ItemKind kind, int count)[] Materials =>
        Kind == FaultKind.Wrecked && Part is ItemKind key ? new[] { (key, 1), (ItemKind.Plate, 2) }
        : Part is ItemKind p ? new[] { (p, 1) }
        : Array.Empty<(ItemKind, int)>();

    /// <summary>
    /// 부분 수리 단계. 0 = 그대로, 1 = 긴급 우회(최소 20% 출력), 2 = 부분 복구(최소 60% 출력).
    /// 완전 수리(고장 제거)에는 보통 부품이 필요하지만, 부품 없이도 60%까지는 살릴 수 있다.
    /// 임시로 살린 설비는 더 빨리 닳는다.
    /// </summary>
    public int Stage { get; set; }

    public static float StageFloor(int stage) => stage switch { 1 => 0.2f, 2 => 0.6f, _ => 0f };

    /// <summary>이 고장이 설비 출력에 곱하는 배율 (부분 수리 반영).</summary>
    public float OutputFactor => MathF.Max(Spec.OutputFactor, StageFloor(Stage));

    /// <summary>부분 수리로 나아질 여지가 있는지 (회로 고장과 이미 60% 이상인 고장은 단계가 없다).</summary>
    public bool Stageable => Circuit < 0 && Stage < 2 && Spec.OutputFactor < 0.6f && Kind is not (FaultKind.Stripped or FaultKind.Wrecked);

    public string StageName => Stage switch { 1 => "긴급 우회 20%", 2 => "부분 복구 60%", _ => "" };

    public string Name => Circuit >= 0 ? $"{Spec.Name} ({PowerGrid.CircuitName(Circuit)} 회로)"
        : Kind is FaultKind.Stripped or FaultKind.Wrecked && Part is ItemKind p ? $"{Spec.Name} ({ItemKinds.Name(p)})"
        : Stage > 0 ? $"{Spec.Name} · {StageName}" : Spec.Name;
}

// ─────────────────────────────── 설비 사양 ───────────────────────────────

/// <summary>설비 종류별 사양. 여기 숫자가 곧 우주선의 성격이다.</summary>
public sealed record MachineSpec(
    FurnitureType Type,
    float PowerDraw,        // kW (가동 중)
    int Priority,           // 전력이 모자랄 때 낮은 것부터 끊는다
    float WearDays,         // 쉬지 않고 돌면 이 일수 만에 마모 100%
    Skill Skill,            // 정비·수리에 쓰는 기술
    ItemKind? ServiceItem,  // 정기 정비에 드는 소모품
    float ServiceHours,
    bool Critical,          // 멈추면 생존에 직결 (경보가 크게 울림)
    FaultKind[] FaultKinds);

public static class MachineSpecs
{
    private static readonly Dictionary<FurnitureType, MachineSpec> Table = new[]
    {
        new MachineSpec(FurnitureType.ReactorCore, 0f, 10, 24f, Skill.Engineering, null, 1.5f, true,
            new[] { FaultKind.SensorDrift, FaultKind.ControlFault }),
        // 냉각 펌프가 가장 먼저 전기를 받는다: 블랙스타트(보조 발전기 → 펌프 → 원자로 재기동)의 핵심
        new MachineSpec(FurnitureType.CoolantPump, 3f, 11, 12f, Skill.Mechanics, ItemKind.Lubricant, 0.75f, true,
            new[] { FaultKind.BearingWear, FaultKind.PumpSeized }),
        new MachineSpec(FurnitureType.PowerPanel, 0f, 10, 30f, Skill.Electrical, null, 0.75f, true,
            new[] { FaultKind.BreakerTrip, FaultKind.BreakerTrip, FaultKind.ShortCircuit }),
        new MachineSpec(FurnitureType.Battery, 0f, 9, 40f, Skill.Electrical, null, 0.5f, false,
            new[] { FaultKind.CellDegradation }),
        new MachineSpec(FurnitureType.OxygenGenerator, 4f, 10, 12f, Skill.Mechanics, ItemKind.Filter, 0.75f, true,
            new[] { FaultKind.FilterClogged, FaultKind.FilterClogged, FaultKind.ElectrolyzerFault }),
        new MachineSpec(FurnitureType.WaterRecycler, 2f, 8, 14f, Skill.Mechanics, ItemKind.Filter, 0.75f, false,
            new[] { FaultKind.MembraneFouling, FaultKind.PumpSeized }),
        new MachineSpec(FurnitureType.GrowBed, 1f, 6, 20f, Skill.Botany, null, 0.5f, false,
            new[] { FaultKind.LightFailure, FaultKind.NutrientClog }),
        // v11.2: 엔진은 연소할 때만 전기를 크게 먹는다 (평소엔 대기 전력 10%) — 회피 기동·항로 변경
        new MachineSpec(FurnitureType.EngineCore, 8f, 2, 20f, Skill.Engineering, ItemKind.Lubricant, 1f, false,
            new[] { FaultKind.InjectorClog, FaultKind.WiringFault }),
        new MachineSpec(FurnitureType.Stove, 2f, 5, 20f, Skill.Mechanics, null, 0.5f, false,
            new[] { FaultKind.HeatingElement }),
        new MachineSpec(FurnitureType.Fridge, 0.6f, 6, 30f, Skill.Mechanics, null, 0.5f, false,
            new[] { FaultKind.CompressorFail }),
        new MachineSpec(FurnitureType.MealDispenser, 0.3f, 5, 30f, Skill.Mechanics, null, 0.3f, false,
            new[] { FaultKind.Jam }),
        new MachineSpec(FurnitureType.Console, 0.4f, 3, 40f, Skill.Electrical, null, 0.4f, false,
            new[] { FaultKind.DisplayFault, FaultKind.WiringFault }),
        new MachineSpec(FurnitureType.MedBed, 0.5f, 8, 40f, Skill.Electrical, null, 0.4f, false,
            new[] { FaultKind.WiringFault }),
        new MachineSpec(FurnitureType.Workbench, 0.8f, 3, 30f, Skill.Mechanics, null, 0.4f, false,
            new[] { FaultKind.WiringFault }),
        new MachineSpec(FurnitureType.SuitLocker, 0.2f, 3, 40f, Skill.Electrical, null, 0.3f, false,
            new[] { FaultKind.WiringFault }),
        new MachineSpec(FurnitureType.AuxGenerator, 0f, 10, 30f, Skill.Mechanics, ItemKind.Lubricant, 0.75f, false,
            new[] { FaultKind.InjectorClog, FaultKind.WiringFault }),
        // 선체 바깥에 달린 채집 장치: 주변 잔해 밀도만큼 원료를 호퍼에 모은다
        new MachineSpec(FurnitureType.Collector, 1.5f, 4, 20f, Skill.Mechanics, ItemKind.Lubricant, 0.6f, false,
            new[] { FaultKind.SensorFouling, FaultKind.ArmMotor, FaultKind.BearingWear }),
        // 정제기: 원료 → 기본 수리재 (돌릴 때만 전기를 많이 먹는다)
        new MachineSpec(FurnitureType.Refinery, 5f, 3, 25f, Skill.Mechanics, ItemKind.Filter, 0.6f, false,
            new[] { FaultKind.FurnaceFault, FaultKind.HopperJam }),
        // 드론 거치대 (v8): 드론을 충전할 때만 전기를 먹는다. 멈추면 드론이 못 나간다
        new MachineSpec(FurnitureType.DroneDock, 1.5f, 4, 40f, Skill.Electrical, null, 0.5f, false,
            new[] { FaultKind.ChargerFault, FaultKind.WiringFault }),
        // 주 컴퓨터 (v9): 격벽 자동 잠금·댐퍼·경보 중계·부하 우선순위·드론 관제·자동 제어봉. 열이 많이 나서 환기가 끊기면 과열한다
        new MachineSpec(FurnitureType.MainComputer, 2f, 9, 35f, Skill.Electrical, ItemKind.Filter, 0.5f, false,
            new[] { FaultKind.StorageFault, FaultKind.WiringFault }),
        // v10.6 방 모듈
        new MachineSpec(FurnitureType.LedPanel, 1.2f, 6, 40f, Skill.Electrical, null, 0.4f, false, new[] { FaultKind.LightFailure, FaultKind.WiringFault }),
        new MachineSpec(FurnitureType.HeatExchanger, 0.4f, 10, 40f, Skill.Mechanics, null, 0.5f, false, new[] { FaultKind.BearingWear, FaultKind.PumpSeized }),
        new MachineSpec(FurnitureType.CapacitorBank, 0f, 9, 50f, Skill.Electrical, null, 0.4f, false, new[] { FaultKind.CellDegradation }),
        new MachineSpec(FurnitureType.Scrubber, 0.8f, 9, 30f, Skill.Mechanics, ItemKind.Filter, 0.5f, false, new[] { FaultKind.FilterClogged, FaultKind.WiringFault }),
        new MachineSpec(FurnitureType.Fabricator, 1.2f, 3, 30f, Skill.Mechanics, ItemKind.Lubricant, 0.5f, false, new[] { FaultKind.WiringFault, FaultKind.BearingWear }),
        // 장거리 센서 (v10.1): 선체 밖 안테나와 레이더. 궤적 계산은 주 컴퓨터가, 컴퓨터가 없으면 통신실 사람이 화면을 읽는다
        new MachineSpec(FurnitureType.SensorArray, 0.5f, 5, 30f, Skill.Electrical, null, 0.5f, false,
            new[] { FaultKind.AntennaDrift, FaultKind.AntennaDrift, FaultKind.RadarFault, FaultKind.WiringFault }),
        // v10.10 로봇 충전대: 로봇을 충전할 때만 전기를 먹는다. 멈추면 로봇이 방전돼 사람이 그 일을 떠맡는다
        new MachineSpec(FurnitureType.RobotDock, 1.2f, 4, 45f, Skill.Electrical, null, 0.4f, false,
            new[] { FaultKind.ChargerFault, FaultKind.WiringFault }),
    }.ToDictionary(s => s.Type);

    public static MachineSpec? For(FurnitureType t) => Table.TryGetValue(t, out var s) ? s : null;
}

// ─────────────────────────────── 설비 ───────────────────────────────

/// <summary>수경재배대의 작물.</summary>
public sealed class CropState
{
    /// <summary>0 = 막 심음, 1 = 수확 가능.</summary>
    public float Growth { get; set; }

    /// <summary>돌봄 0~1. 낮으면 천천히 자란다.</summary>
    public float Care { get; set; } = 1f;

    /// <summary>v9: 물을 못 받은 시간. 오래 가면 말라 죽는다.</summary>
    public float DryHours { get; set; }

    /// <summary>v10.10: 물통으로 부어 준 물이 버티는 시간 (급수 본관이 끊겼을 때).</summary>
    public float HandWateredHours { get; set; }

    /// <summary>v11.2 병충해 0~1 (1이면 작물이 죽는다). 사람이 약을 쳐야 낫는다.</summary>
    public float Blight { get; set; }

    /// <summary>병충해를 알아챘는지 (잎에 반점이 번지면).</summary>
    public bool BlightKnown { get; set; }

    public bool Ripe => Growth >= 1f;
}

/// <summary>
/// 진짜 기계. 상태(수명), 마모(정비로 회복), 고장 목록, 전력, 출력을 가진다.
/// </summary>
public sealed class Machine
{
    public Furniture Body { get; }
    public MachineSpec Spec { get; }

    /// <summary>수명 건전성 1~0. 고장이 날 때마다 조금씩 영구적으로 깎인다.</summary>
    public float Condition { get; set; } = 1f;

    /// <summary>마모 0~1. 높을수록 고장 확률이 가파르게 오른다. 정비하면 내려간다.</summary>
    public float Wear { get; set; }

    public List<Fault> Faults { get; } = new();

    /// <summary>이번 전력 계산에서 전기를 받았는지.</summary>
    public bool Powered { get; set; } = true;

    /// <summary>지금 실제로 일하고 있는지 (조리대는 요리할 때만).</summary>
    public bool Active { get; set; } = true;

    /// <summary>v9.3 저출력 운영으로 사람이 내려 둔 설비 (고장이 아니다 — 전기가 돌아오면 다시 올린다).</summary>
    public bool Parked { get; set; }

    public long LastServiced { get; set; }
    public int ServiceCount { get; set; }
    public int FaultCount { get; set; }

    public CropState? Crop { get; set; }

    /// <summary>v11.0 사고 전조 (없으면 null): 몇 시간 뒤 올 고장의 기척.</summary>
    public Omen? Omen { get; set; }

    /// <summary>v12.0 감지기 교정 1~0.3 (틀어질수록 기척을 덜 잡고, 멀쩡한데 경보를 낸다).</summary>
    public float SensorCal { get; set; } = 1f;

    /// <summary>v12.0 주 컴퓨터가 마지막으로 이 설비를 잰 틱 (컴퓨터가 멎거나 전기가 없으면 멈춘다).</summary>
    public long LastReading { get; set; }

    /// <summary>v12.0 마지막으로 감지기를 다시 맞춘 틱.</summary>
    public long LastCalibrated { get; set; }

    /// <summary>v12.2 열·압력 스트레스 0~1.5 (0.3쯤이 평소, 0.9를 넘으면 종류마다 다르게 터질 수 있다).</summary>
    public float Heat { get; set; }

    /// <summary>v12.2 새어 고인 기체 0~1 (산소 발생기의 수소, 보조 발전기의 연료 증기) — 불꽃 하나면 터진다.</summary>
    public float Vapor { get; set; }

    /// <summary>v12.2 소화 분말·그을음 0~1 (효율이 떨어지고 전자 장비가 합선된다 — 닦아야 한다).</summary>
    public float Fouled { get; set; }

    /// <summary>v12.2 달아올라 사람이 내려 식히는 중 (식으면 다시 올린다).</summary>
    public bool CoolingDown { get; set; }

    /// <summary>v12.1 이 설비로 가는 전선 0~1 (0.3 아래면 설비가 멀쩡해도 전기가 안 들어간다).</summary>
    public float Feed { get; set; } = 1f;

    /// <summary>v12.1 이 설비에 달린 관 이음 0~1 (관이 달린 설비만 — 0.3 아래면 물·냉각수가 안 들어간다).</summary>
    public float Line { get; set; } = 1f;

    /// <summary>v12.1 전선을 임시로 이어 붙였다 (평온해지면 케이블로 다시).</summary>
    public bool Spliced { get; set; }

    /// <summary>v12.1 정비 절차로 전원을 잠가 둔 중.</summary>
    public bool LockedOut { get; set; }

    /// <summary>v12.1 재조립 불량 (시험 운전을 건너뛰었다): 이 틱에 같은 고장이 돌아온다.</summary>
    public FaultKind? Defect { get; set; }
    public long DefectDue { get; set; }
    public string? DefectBy { get; set; }

    /// <summary>정품인지 Mk.1 임시품인지.</summary>
    public MachineGrade Grade { get; set; } = MachineGrade.Standard;

    /// <summary>Mk.1로 바꾼 횟수, 정품으로 되돌린 횟수 (흔적).</summary>
    public int Substitutions { get; set; }

    // ── 역사 (v7) ──
    public int Restores { get; set; }
    public int TimesStripped { get; set; }
    public int Rebuilds { get; set; }
    public List<Mark> Marks { get; } = new();

    public Machine(Furniture body, MachineSpec spec)
    {
        Body = body;
        Spec = spec;
        if (spec.Type == FurnitureType.GrowBed) Crop = new CropState();
    }

    public string Name => Body.Label;

    /// <summary>지금 필요한 전력 (kW). 대기 상태면 10%.</summary>
    public float Demand => Spec.PowerDraw * Grades.Power(Grade) * Tech.Of(this).Power * (Active ? 1f : 0.1f);

    /// <summary>v10.5 테크 단계 (1 = 기본). 개조로 올린다.</summary>
    public int Tier { get; set; } = 1;

    /// <summary>
    /// v10.4 크기: 큰 배의 큰 설비는 칸 수만큼 더 낸다 (원자로 3×3 = 1, 4×4 = 1.78 …, 냉각 펌프 2×2 = 1, 보조 발전기 2칸 = 1).
    /// </summary>
    public float SizeFactor => Body.Type switch
    {
        FurnitureType.ReactorCore => Body.Cells.Count / 9f,
        FurnitureType.CoolantPump => Body.Cells.Count / 4f,
        FurnitureType.AuxGenerator => Body.Cells.Count / 2f,
        _ => 1f,
    };

    /// <summary>v10.4·10.5: 이 설비가 내는 몫의 배율 = 크기 × 테크 단계 출력.</summary>
    public float Rating => SizeFactor * Tech.Of(this).Output;

    /// <summary>고장만 따진 출력 배율.</summary>
    public float FaultFactor
    {
        get
        {
            float f = 1f;
            foreach (var fault in Faults) f *= fault.OutputFactor;
            return f;
        }
    }

    /// <summary>전기·고장·마모·수명을 모두 반영한 실제 효율 0~1.</summary>
    public float Efficiency
    {
        get
        {
            if (!Powered && Spec.PowerDraw > 0f) return 0f;
            return FaultFactor * Grades.Output(Grade) * (1f - 0.25f * Wear * Wear) * (0.6f + 0.4f * Condition)
                   * (Heat > 0.7f ? MathF.Max(0.5f, 1f - (Heat - 0.7f)) : 1f) * (1f - 0.35f * Fouled) // v12.2 달아오르면·분말을 뒤집어쓰면 덜 낸다
                   * (Line < 0.3f && Procedures.Plumbed(Body.Type) ? 0.3f : 1f) // v12.1 관 이음이 빠지면 물·냉각수가 안 든다
                   * (Body.Type == FurnitureType.OxygenGenerator && !Body.Room.WaterLinked ? 0.15f : 1f); // 단수면 전해할 물이 없다
        }
    }

    public bool Stopped => FaultFactor <= 0.001f;

    public bool Has(FaultKind k) => Faults.Any(f => f.Kind == k);

    /// <summary>한 시간에 고장 날 확률. 마모가 쌓이면 급격히 오른다.</summary>
    public float FaultChancePerHour =>
        0.0015f * (0.05f + Wear * Wear * Wear * 8f) * (1f + (1f - Condition) * 2f) * Grades.FaultRate(Grade) * Tech.Of(this).Faults;


    public string StatusText =>
        Faults.Count > 0 ? string.Join(", ", Faults.Select(f => f.Name))
        : Parked ? "저출력 운영으로 내림"
        : !Powered && Spec.PowerDraw > 0f ? "전력 없음"
        : Wear > 0.6f ? "정비 필요"
        : "정상";
}
