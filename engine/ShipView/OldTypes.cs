// 옛 게임의 설비 종류 이름표 — game/src/Core/ShipParts.cs 에서 그대로 옮겼다.
// 도트 화가(DotMachines)가 이 이름으로 설비를 그린다. 새 엔진의 설비 종류(문자열)를 이 이름으로 바꿔 넘긴다.
namespace ShipSim.Core;

public enum FurnitureType
{
    Bed, Seat, Table, MealDispenser, Console,
    ReactorCore, EngineCore, OxygenGenerator, Workbench, Shelf, MedBed,
    CoolantPump, PowerPanel, Battery, GrowBed, WaterRecycler, Stove, Fridge, SuitLocker,
    AuxGenerator,
    Cot, // 간이침대 (방 용도를 바꿀 때 깐다. 설계도에는 없다)
    Collector, Refinery, // 채집 장치(선체 바깥 채집 팔 + 호퍼), 정제기
    DroneDock, // 드론 거치대 (충전·정비·자재 적재) v8
    MainComputer, // 주 컴퓨터 (함교, 자동화의 두뇌) v9
    SensorArray, // 장거리 센서 (통신실, 운석 조기 경보) v10.1
    // v10.6 방 모듈 (개조로 단다)
    LedPanel, HeatExchanger, CapacitorBank, Scrubber, Fabricator,
    RobotDock, // v10.10 선내 로봇 충전대 (칸마다 로봇 한 대)
    SupplyCache, // v10.10 비상 물자함 (실링폼·구급 키트·소화기를 창고 밖에 나눠 둔다 — 개조로 단다)
    PartTestBench, Hoist, MaintCart, // v14.6 정비 장비 (정비실에 단다)
    // v15 설비 70 (ModulesV15.cs): 개조로 다는 방 모듈 34
    VibrationMonitor, ThermalCamera, LeakDetector, CalibrationRig, Oven, Lathe, SolderStation, DiagnosticScanner, NutrientDoser, ToolWall,
    ReactorSimulator, NavComputer, DishWasher, AirPurifier, Autoclave, DeconShower, WashingMachine, BlackoutCurtain, NoiseDamper, WhiteNoise,
    CoffeeMachine, Projector, GameTable, Bookshelf, Aquarium, Treadmill, PlantWall, EmergencyLight, SurgeProtector, FireBlanket,
    Dehumidifier, AirlockPump, SuitDryer, SignalBooster,
    // 압축-마 설비 30 (ModulesV18.cs)
    Fermenter, BreadOven, SpiceRack, IceMaker, PlantRack, CatTower, PestTrap, InsectFarm, GreaseTrap, Compactor,
    Composter, GreywaterFilter, GrabRail, CargoNet, CrashSeat, MagBootRack, ServerRack, RecorderVault, ListeningPost, MeetingBoard,
    MemorialWall, MusicCorner, LabStill, ClothesRack, SewingMachine, EyeWash, OxygenMaskBox, HeatSuitRack, DockClampPanel, Telescope,
    // 의료 1차
    OperatingTable, SurgicalLamp, AnesthesiaMachine, BloodFridge, MedCabinet,
    // 의료 2차 — 장기 · 이식 · 감염 · 격리 (Organs.cs OrganGear)
    Dialyzer, Ecmo, HeartPump, OrganCooler, BioPrinter, NegPressure,
    // 의료 3차 — 수술 로봇 팔 (SurgicalArm.cs)
    SurgicalArm,
    // v23 마음 돌봄 — 오락실 (MindCare.cs)
    ArcadeCabinet, VrChair,
    // 재료 — 배 안에서 되살린다 (MatWorks.cs): 주 컴퓨터가 탄소가 모자랄 때 단다
    CarbonReclaimer,
}
