using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>v11.2 관찰자가 낼 수 있는 사고 (운석·화재·고장·배관 말고). 무작위 사고도 이 목록에서 뽑는다.</summary>
public enum HazardKind
{
    MeteorShower,      // 운석우: 30분 동안 작은 운석 여럿
    SolarStorm,        // 태양 폭풍: 센서가 흐려지고 전자 장비·로봇이 오작동, 선체 밖이 위험
    PowerSurge,        // 전력 서지: 차단기·단락·임시 배선·배터리·조명
    GasLeak,           // 유독 가스(냉매) 누출: 방이 우주복 없이는 못 들어가는 곳이 된다
    CropBlight,        // 병충해: 재배대에서 재배대로 번진다
    FoodPoisoning,     // 식중독: 균이 든 식사
    ComputerFault,     // 주 컴퓨터 저장장치 오류
    RobotMalfunction,  // 로봇 제어기 오작동
    HullCrack,         // 선체 피로 균열
    ReactorTransient,  // 원자로 제어봉 이상 + 노심 온도 급상승
    DoorJam,           // 문 구동기 고장
    LightsOut,         // 조명 나감
    WorkAccident,      // 작업 사고 (부상)
    DebrisCloud,       // 잔해 구름에 휩쓸림
    WaterContamination, // 물 오염 (탱크 일부를 버리고 정수기 필터가 막힌다)
    RescueSignal,       // v11.2 구조 요청 수신: 탈출 캡슐 (사고라기보다 사건 — 건질지 회의)
    OxygenLeak,         // v12.2 산소관 누출: 방에 산소가 짙어진다 — 불꽃 하나가 불이 된다
    Overheat,           // v12.2 설비 과열: 식히지 않으면 종류마다 다르게 터진다 (열폭주·수소·아크·연료·파열)
    // v12.4 연쇄를 잘 일으키는 사고
    CoolantLoss, DuctFire, HydrogenBuildup, ComputerMisjudge, Epidemic, MicroShower, GasTankRupture, FreezerFailure,
    // v15 사고 70 (HazardsV15.cs): 전기 · 물 · 공기 · 기계 · 구조 · 바깥 · 불 · 생물 · 사람
    BreakerCascade, ArcFault, GroundFault, CellAging, InsulationCrack, TrunkSag,
    PipeFreeze, ValveSeize, Backflow, TankSludge, CondensateFlood, SewageBackup,
    Co2Spike, ScrubberSaturation, InsulationSmoke, SealLeak, Ozone, HumiditySpike,
    BearingSeize, FanImbalance, ShaftMisalign, LooseMount, HoistDrop, BadBatch,
    WeldFatigue, WindowCrack, HatchSeal, ThermalStress, FrameCreak,
    RadiationBurst, IonStorm, DebrisAlert, CometTail, StaticDischarge,
    GreaseFire, DryerFire, CableTrayFire, Smolder,
    MoldOutbreak, SeedRot, NutrientCrash, SkinFungus,
    PanicAttack, MedError,
}

/// <summary>사고를 어디에 거는지.</summary>
public enum HazardTarget { Ship, Room, Machine, Hull, Door, Crew, Robot }

public sealed record HazardSpec(HazardKind Kind, string Name, HazardTarget Target, float Weight, string Hint);

public static class Hazards
{
    public static readonly HazardSpec[] All = Base().Concat(HazardsV15.Specs).ToArray(); // v15 26 → 70

    private static HazardSpec[] Base() => new HazardSpec[]
    {
        new(HazardKind.MeteorShower, "운석우", HazardTarget.Ship, 4f, "운석우 — 30분 동안 잔해 무리가 배를 훑는다 (작은 운석 5~8개, 가끔 큰 것 하나)"),
        new(HazardKind.SolarStorm, "태양 폭풍", HazardTarget.Ship, 4f, "태양 폭풍 — 8시간쯤: 센서가 흐려지고 전자 장비·로봇이 오작동하고, 선체 밖은 방사선으로 위험하다 (선외 작업을 미룬다)"),
        new(HazardKind.PowerSurge, "전력 서지", HazardTarget.Ship, 6f, "전력 서지 — 배전망에 순간 과전압: 차단기가 떨어지고, 단락·임시 배선이 타고, 배터리가 줄고, 조명이 나간다"),
        new(HazardKind.GasLeak, "유독 가스", HazardTarget.Room, 6f, "유독 가스 — 방을 클릭: 그 방 설비의 냉매(암모니아)가 샌다 · 우주복 없이는 못 들어가고 실링폼으로 막아야 한다"),
        new(HazardKind.CropBlight, "병충해", HazardTarget.Machine, 6f, "병충해 — 재배대를 클릭: 잎 뒷면에 벌레가 붙는다 · 식물학 솜씨로 약을 쳐야 하고, 두면 옆 재배대로 옮아 작물이 죽는다"),
        new(HazardKind.FoodPoisoning, "식중독", HazardTarget.Machine, 5f, "식중독 — 냉장고·배식기를 클릭: 식사 몇 끼에 균이 든다 · 먹은 사람이 앓아눕고, 원인을 찾으면 버린다"),
        new(HazardKind.ComputerFault, "컴퓨터 오류", HazardTarget.Ship, 3f, "주 컴퓨터 오류 — 저장장치가 망가져 자동 제어(문·댐퍼·제어봉·궤적 계산·드론)가 멈춘다"),
        new(HazardKind.RobotMalfunction, "로봇 오작동", HazardTarget.Robot, 4f, "로봇 오작동 — 로봇을 클릭: 제어기가 꼬여 비틀거리다 멈춘다 · 옆 사람을 칠 수 있고 사람이 고쳐야 한다"),
        new(HazardKind.HullCrack, "선체 균열", HazardTarget.Hull, 5f, "선체 균열 — 외벽 가까이를 클릭: 쌓인 피로로 외판이 갈라진다 · 조금씩 새고, 용접해도 예전만 못하다"),
        new(HazardKind.ReactorTransient, "원자로 이상", HazardTarget.Ship, 3f, "원자로 이상 — 제어봉 구동 불량과 노심 온도 급상승 · 냉각이 모자라면 긴급 정지"),
        new(HazardKind.DoorJam, "문 고장", HazardTarget.Door, 6f, "문 고장 — 문을 클릭: 구동기가 타서 손으로 돌려 열어야 한다"),
        new(HazardKind.LightsOut, "조명 나감", HazardTarget.Room, 6f, "조명 나감 — 방을 클릭: 어두운 방은 일이 느려지고 사람들이 꺼린다"),
        new(HazardKind.WorkAccident, "작업 사고", HazardTarget.Crew, 5f, "작업 사고 — 승무원을 클릭: 넘어지거나 손을 다친다 · 의무실에서 치료해야 한다"),
        new(HazardKind.DebrisCloud, "잔해 구름", HazardTarget.Ship, 2f, "잔해 구름 — 배가 잔해 지대로 밀려난다: 작은 운석이 날아들고, 엔진을 태워 빠져나와야 한다"),
        new(HazardKind.WaterContamination, "물 오염", HazardTarget.Ship, 4f, "물 오염 — 탱크 물에 녹 찌꺼기가 섞였다: 물 일부를 버리고 정수기 필터가 막힌다"),
        new(HazardKind.RescueSignal, "구조 요청", HazardTarget.Ship, 2f, "구조 요청 수신 — 탈출 캡슐의 생존자 1~3명: 하루 안에 회의로 건질지 정한다 (추진제 · 먹을 입 · 통신실이 멀쩡해야 듣는다)"),
        new(HazardKind.OxygenLeak, "산소관 누출", HazardTarget.Room, 3f, "산소관 누출 — 방을 클릭: 공기 탱크의 산소가 그 방으로 샌다 · 산소가 짙어지면 합선·용접 불꽃 하나가 불이 되고 불이 빨리 번진다 · 실링폼으로 막고 환기한다"),
        new(HazardKind.Overheat, "설비 과열", HazardTarget.Machine, 4f, "설비 과열 — 설비를 클릭: 배터리는 열폭주, 산소 발생기는 수소 폭발, 배전반은 아크 섬광, 보조 발전기는 연료 화재, 펌프는 과압 파열 · 먼저 알아채고 내려 식히면 막는다"),
        // v12.4
        new(HazardKind.CoolantLoss, "냉각 상실", HazardTarget.Ship, 2f, "냉각 상실 — 냉각 루프 본관이 크게 터진다: 냉각수가 쏟아지고 바닥에 고이고, 원자로가 긴급 정지한다 · 밸브로 격리하고 관을 갈아야 다시 돈다"),
        new(HazardKind.DuctFire, "덕트 화재", HazardTarget.Room, 3f, "덕트 화재 — 방을 클릭: 덕트 속 먼지·기름때에 불이 붙어 몇 분 뒤 덕트로 이어진 옆방에서도 불이 난다 · 댐퍼를 닫아 두면 막힌다"),
        new(HazardKind.HydrogenBuildup, "수소 축적", HazardTarget.Ship, 2.5f, "수소 축적 — 산소 발생기 방의 환기 댐퍼가 닫힌 채 걸린다: 수소가 모여 불꽃 하나에 터진다 · 식히고 댐퍼를 고쳐 환기한다"),
        new(HazardKind.ComputerMisjudge, "컴퓨터 오판단", HazardTarget.Ship, 2.5f, "컴퓨터 오판단 — 틀어진 감지기를 믿고 멀쩡한 핵심 방의 분전함을 내린다 (보수적인 자동화의 대가) · 사람이 가서 확인하고 다시 올린다"),
        new(HazardKind.Epidemic, "전염병", HazardTarget.Crew, 1.5f, "전염병 — 승무원을 클릭: 열병이 시작된다 · 같은 방에 있으면 옮고(환기가 돌면 덜), 이틀째 가장 아프고, 나흘쯤 낫는다 · 치료 침대에 누우면 빨리 낫고 덜 옮긴다"),
        new(HazardKind.MicroShower, "미세 운석 소나기", HazardTarget.Ship, 3f, "미세 운석 소나기 — 20분 동안 아주 작은 운석 열 몇 개: 외벽 곳곳에 바늘구멍 · 실링폼이 바닥난다"),
        new(HazardKind.GasTankRupture, "가스 탱크 파열", HazardTarget.Ship, 1.5f, "가스 탱크 파열 — 생명유지실 고압 탱크: 파편, 공기 탱크 4분의 1을 잃고, 산소가 짙어지거나(불) 질소가 밀어내 숨이 막힌다"),
        new(HazardKind.FreezerFailure, "냉장고 고장", HazardTarget.Machine, 2.5f, "냉장고 고장 — 냉장고를 클릭: 압축기가 멈춘다 · 여섯 시간 안에 못 고치면 안의 식사가 상한다 (식중독)"),
    };

    public static HazardSpec Spec(HazardKind k) => All[(int)k];

    private static string? EpidemicStart(World w, CrewMember c)
    {
        w.Disease.Infect(c, null);
        w.RaiseAlert($"전염병 — {Ko.IGa(c.Name)} 열이 난다 · 같은 방에 있으면 옮는다", c.Room, AlertLevel.Warning, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, $"전염병 — {Ko.IGa(c.Name)} 처음 앓기 시작했다", c.Room, new[] { c });
        return $"전염병({c.Name})";
    }
    public static string Name(HazardKind k) => Spec(k).Name;

    /// <summary>폭풍·서지가 건드리는 전자 장비.</summary>
    private static readonly FurnitureType[] Electronics =
        { FurnitureType.SensorArray, FurnitureType.MainComputer, FurnitureType.Console, FurnitureType.DroneDock, FurnitureType.RobotDock, FurnitureType.CapacitorBank };

    /// <summary>냉매·가스가 도는 설비 (가스가 샐 수 있는 곳).</summary>
    private static readonly FurnitureType[] GasMachines =
        { FurnitureType.CoolantPump, FurnitureType.OxygenGenerator, FurnitureType.WaterRecycler, FurnitureType.Fridge, FurnitureType.Scrubber,
          FurnitureType.HeatExchanger, FurnitureType.ReactorCore, FurnitureType.EngineCore, FurnitureType.Stove, FurnitureType.Refinery };

    /// <summary>그 칸에서 그 사고가 걸 대상을 찾는다 (화면 미리보기와 기록 되풀이가 같은 규칙을 쓴다).</summary>
    public static Room? RoomAt(World w, Cell at) => w.Ship.RoomAt(at) ?? (w.Ship.WallAt(at) != null ? Hull.InsideRoom(w.Ship, at) : null);

    public static Furniture? MachineAt(World w, HazardKind k, Cell at)
    {
        var f = w.Ship.FurnitureAt(at);
        bool Fits(Furniture? x) => x != null && !x.Stowed && k switch
        {
            HazardKind.CropBlight => x.Machine?.Crop != null,
            HazardKind.FoodPoisoning => x.Type is FurnitureType.Fridge or FurnitureType.MealDispenser && x.Storage != null,
            HazardKind.FreezerFailure => x.Type == FurnitureType.Fridge && x.Machine != null,
            HazardKind.Overheat => x.Machine != null && VolatileSystem.Mode(x.Type) != BlowKind.None,
            _ => x.Machine != null && HazardsV15.Fits(k, x),
        };
        if (Fits(f)) return f;
        // 설비 옆 칸을 눌렀으면 가까운 것
        return Cell.Dirs8.Select(d => w.Ship.FurnitureAt(at + d)).FirstOrDefault(Fits);
    }

    public static Door? DoorAt(World w, Cell at) =>
        w.Ship.DoorAt(at) is Door d && !d.IsExternal ? d
        : Cell.Dirs4.Select(dd => w.Ship.DoorAt(at + dd)).FirstOrDefault(d => d != null && !d.IsExternal);

    /// <summary>클릭한 곳에서 가장 가까운 선체 벽 (3칸 안).</summary>
    public static Cell? HullAt(World w, Cell at)
    {
        Cell? best = null;
        float bestD = 9.5f;
        foreach (var (cell, wall) in w.Ship.Walls)
        {
            if (!wall.IsHull) continue;
            float d = (cell.Center - at.Center).LengthSquared();
            if (d < bestD) { bestD = d; best = cell; }
        }
        return best;
    }

    /// <summary>
    /// 사고를 건다. arg는 대상: 칸(방·설비·벽·문) 또는 승무원·로봇 번호. 걸었으면 무엇을 했는지 설명을 돌려주고, 못 걸었으면 null.
    /// 기록(Player.Hazard)과 무작위 사고가 같이 쓴다 — 여기서 쓰는 난수는 모두 배의 난수라 되풀이하면 같은 일이 난다.
    /// </summary>
    public static string? Apply(World w, HazardKind k, Cell at, int id)
    {
        // v12.2 인과 사슬: 사고 하나가 뿌리 — 이 안에서 생긴 피해는 이 사고의 자식
        var room = w.Ship.RoomAt(at);
        // 자리: 사람·로봇에게 건 사고는 그 사람 자리, 배 전체 사고는 자리 없음 (칸 (0,0)은 '고르지 않음')
        System.Numerics.Vector2? where = at == default ? null : at.Center;
        if (Spec(k).Target == HazardTarget.Crew && w.Crew.FirstOrDefault(c => c.Id == id) is CrewMember who) { where = who.Position; room = who.Room; }
        if (Spec(k).Target == HazardTarget.Robot && w.Robots.Robots.FirstOrDefault(r => r.Id == id) is Robot bot) { where = bot.Position; room = w.Ship.RoomAt(Cell.FromPosition(bot.Position)); }
        if (Spec(k).Target == HazardTarget.Ship) where = null;
        int node = w.Causes.Root(CauseKind.Hazard, Name(k), room, where, observer: w.Causes.ConsumeObserver());
        w.Scale.Tag(node, k.ToString()); // v16.18 표의 열쇠 (기본 규모)
        string? what;
        using (w.Causes.Because(node)) what = ApplyCore(w, k, at, id);
        if (what == null) w.Causes.Discard(node);
        else w.Causes.Node(node).Text = what;
        return what;
    }

    private static string? ApplyCore(World w, HazardKind k, Cell at, int id)
    {
        var sys = w.Hazards;
        string? what = k switch
        {
            HazardKind.MeteorShower => sys.StartShower(),
            HazardKind.SolarStorm => sys.StartStorm(),
            HazardKind.PowerSurge => sys.Surge(),
            HazardKind.GasLeak => sys.Gas(RoomAt(w, at)),
            HazardKind.CropBlight => sys.Blight(MachineAt(w, k, at)),
            HazardKind.FoodPoisoning => sys.Taint(MachineAt(w, k, at)),
            HazardKind.ComputerFault => sys.Computer(),
            HazardKind.RobotMalfunction => sys.RobotHaywire(w.Robots.Robots.FirstOrDefault(r => r.Id == id)),
            HazardKind.HullCrack => sys.Crack(HullAt(w, at)),
            HazardKind.ReactorTransient => sys.Reactor(),
            HazardKind.DoorJam => sys.Jam(DoorAt(w, at)),
            HazardKind.LightsOut => sys.Lights(RoomAt(w, at)),
            HazardKind.WorkAccident => sys.Accident(w.Crew.FirstOrDefault(c => c.Id == id)),
            HazardKind.DebrisCloud => sys.Cloud(),
            HazardKind.WaterContamination => sys.Water(),
            HazardKind.RescueSignal => w.Comms.ReceiveSignal(),
            HazardKind.OxygenLeak => RoomAt(w, at) is Room ol && !ol.Detached ? O2Leak(w, ol) : null,
            HazardKind.Overheat => MachineAt(w, k, at) is Furniture hf ? Overheat(w, hf) : null,
            HazardKind.CoolantLoss => sys.CoolantLoss(),
            HazardKind.DuctFire => sys.DuctFire(RoomAt(w, at)),
            HazardKind.HydrogenBuildup => sys.HydrogenBuildup(),
            HazardKind.ComputerMisjudge => sys.ComputerMisjudge(),
            HazardKind.Epidemic => w.Crew.FirstOrDefault(c => c.Id == id && !c.Dead) is CrewMember pz && pz.InfectedAt < 0 && !pz.Immune ? EpidemicStart(w, pz) : null,
            HazardKind.MicroShower => sys.MicroShower(),
            HazardKind.GasTankRupture => sys.GasTankRupture(),
            HazardKind.FreezerFailure => sys.FreezerFailure(MachineAt(w, k, at)),
            _ => sys.V15(k, at, id), // v15 새 사고 44
        };
        if (what == null) return null;
        sys.Count[(int)k]++;
        w.History.NoteCause(w, what);
        w.Board.RequestScan();
        return what;
    }

    private static string O2Leak(World w, Room room)
    {
        room.O2Leak = w.Rng.Range(3f, 6f);
        w.RaiseAlert($"{room.Name} 산소관 누출", room, AlertLevel.Warning, shipWide: false);
        w.History.Add(w, HistoryKind.Incident, $"{room.Name} 산소관이 샌다 — 방에 산소가 짙어진다", room);
        return $"{room.Name} 산소관 누출";
    }

    private static string? Overheat(World w, Furniture f)
    {
        var m = f.Machine!;
        m.Heat = MathF.Max(m.Heat, 0.95f);
        if (f.Type is FurnitureType.OxygenGenerator or FurnitureType.AuxGenerator) m.Vapor = MathF.Max(m.Vapor, 0.45f);
        w.History.Add(w, HistoryKind.Incident, $"{Ko.IGa(m.Name)} 달아오른다 ({VolatileSystem.Name(VolatileSystem.Mode(f.Type))} 위험)", f.Room);
        return $"{m.Name} 과열";
    }

    internal static bool IsElectronic(Furniture f) => Electronics.Contains(f.Type);
    internal static bool IsGasMachine(Furniture f) => GasMachines.Contains(f.Type);
}

/// <summary>
/// v11.2 사고의 시간 흐름: 운석우(몇십 분에 걸쳐), 태양 폭풍(몇 시간), 유독 가스(샌 곳에서 계속 나온다), 병충해(번진다),
/// 오염된 식사(나르면 따라간다) — 그리고 무작위 사고 (Tuning `incident.days` 평균 간격, 0이면 끔).
/// </summary>
public sealed partial class HazardSystem
{
    private readonly World _w;

    /// <summary>무작위 사고용 난수 (배의 난수와 따로 — 꺼 두면 한 번도 안 뽑아서 역사가 그대로다).</summary>
    public Rng RandomRng { get; }

    /// <summary>무작위 사고 평균 간격 (일). 0이면 끔.</summary>
    public static float RandomDays;

    public long NextRandom { get; private set; } = -1;
    public int RandomCount { get; private set; }
    public HazardKind? LastRandom { get; private set; }
    public string LastRandomText { get; private set; } = "";

    public int[] Count { get; } = new int[Enum.GetValues<HazardKind>().Length];

    // 운석우
    public List<(long tick, Cell target, float size)> Shower { get; } = new();
    /// <summary>v12.2 인과 사슬: 지금 도는 운석우의 고리.</summary>
    public int ShowerNode { get; private set; } = -1;

    // 태양 폭풍
    public long StormUntil { get; private set; } = -1;
    public long StormSince { get; private set; } = -1;
    /// <summary>v16.26 이번 폭풍의 처음 세 시간 양성자 세기 (보통 0.45~0.8 · 센 것 1~1.6).</summary>
    public float StormPeak { get; private set; } = 0.6f;
    public bool StormActive => _w.Tick < StormUntil;
    public float StormHoursLeft => StormActive ? (StormUntil - _w.Tick) / (float)SimTime.TicksPerHour : 0f;
    public int StormGlitches { get; private set; }

    /// <summary>센서가 내는 몫 배율 (폭풍 중에는 잡음이 많다).</summary>
    public float SensorFactor => StormActive ? 0.4f : 1f;

    // 기록
    public int Poisoned { get; private set; }
    public int FoodDiscarded { get; internal set; }
    public int BlightCured { get; internal set; }
    public int BlightKilled { get; private set; }
    public int GasSealed { get; internal set; }

    public HazardSystem(World w, int seed)
    {
        _w = w;
        RandomRng = new Rng(seed * 7919 + 104729);
    }

    /// <summary>그 방에 가스가 새는 설비가 있나.</summary>
    public Machine? GasSource(Room r) => r.Furniture.Select(f => f.Machine).FirstOrDefault(m => m != null && m.Has(FaultKind.GasLeak));

    // ─────────────────────────────── 사고 걸기 ───────────────────────────────

    internal string? StartShower()
    {
        var w = _w;
        // v16.26 외벽에 닿은 방 모두 (복도 · 침실 · 식당 — 사람이 있는 곳도), 외벽이 넓을수록 · 잔해 무리가 오는 쪽일수록 많이 맞는다
        var hullCells = new Dictionary<int, int>();
        foreach (var (cell, wall) in w.Ship.Walls)
            if (wall.IsHull && Hull.InsideRoom(w.Ship, cell) is Room hr && !hr.Detached) hullCells[hr.Id] = hullCells.GetValueOrDefault(hr.Id) + 1;
        var rooms = hullCells.Keys.OrderBy(id => id).Select(id => w.Ship.Rooms[id]).ToList();
        if (rooms.Count == 0) return null;
        int n = 5 + w.Rng.Range(0, 4);
        bool big = w.Rng.Chance(0.3f);
        var mid = new System.Numerics.Vector2(rooms.Average(r => r.Center.X), rooms.Average(r => r.Center.Y));
        int side = w.Rng.Range(0, 4);
        var from = side switch { 0 => new System.Numerics.Vector2(1, 0), 1 => new System.Numerics.Vector2(-1, 0), 2 => new System.Numerics.Vector2(0, 1), _ => new System.Numerics.Vector2(0, -1) };
        var weight = rooms.Select(r => hullCells[r.Id] * (System.Numerics.Vector2.Dot(r.Center - mid, from) > 0f ? 3f : 1f)).ToList();
        float wsum = weight.Sum();
        for (int i = 0; i < n; i++)
        {
            float u = w.Rng.Float() * wsum;
            int pick = 0;
            while (pick < rooms.Count - 1 && (u -= weight[pick]) > 0f) pick++;
            var room = rooms[pick];
            long t = w.Tick + SimTime.Minutes(w.Rng.Range(0f, 30f));
            float size = big && i == 0 ? w.Rng.Range(0.75f, 0.95f) : w.Rng.Range(0.18f, 0.45f);
            Shower.Add((t, Scenarios.OuterTarget(w, room), size));
        }
        Shower.Sort((a, b) => a.tick.CompareTo(b.tick));
        ShowerNode = w.Causes.Context;
        w.RaiseAlert($"운석우 — 잔해 무리가 다가온다 (30분 동안 {n}개쯤 · 한쪽 외벽에 몰린다{(big ? " · 큰 것 하나" : "")}) — 외벽에서 떨어져라", null, AlertLevel.Critical, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, $"운석우가 배를 훑기 시작했다 — 30분 동안 {n}개쯤" + (big ? " (큰 것 하나)" : ""));
        return "운석우";
    }

    internal string? StartStorm()
    {
        var w = _w;
        float hours = w.Rng.Range(6f, 10f);
        bool extend = StormActive;
        if (!extend) StormSince = w.Tick;
        float peak = w.Rng.Chance(0.25f) ? w.Rng.Range(1.6f, 3f) : w.Rng.Range(0.45f, 0.9f); // v16.26 센 양성자 폭풍은 대피소 밖이 위험하다 · 통합: 센 것은 바깥 방에서 세 시간이면 방사선 병 (4Sv 넘게)
        StormPeak = extend ? MathF.Max(StormPeak, peak) : peak;
        StormUntil = Math.Max(StormUntil, w.Tick + SimTime.Hours(hours));
        // 처음 몰아칠 때: 전자 장비 두셋이 튀고, 움직이던 로봇 몇이 센서를 잃는다
        int glitches = 2 + w.Rng.Range(0, 2);
        for (int i = 0; i < glitches; i++) Glitch("태양 폭풍");
        foreach (var r in w.Robots.Robots.Where(r => r.State == RobotState.Active && r.Fault == null).ToList())
            if (w.Rng.Chance(0.35f)) w.Robots.ForceFault(r, RobotFault.Sensor);
        foreach (var c in w.Crew.Where(c => !c.Dead && c.Outside))
            w.Log.Add(w.Tick, LogKind.Warning, "태양 폭풍 — 선체 밖은 방사선이 세다, 서둘러 돌아간다", c.Id);
        w.RaiseAlert($"태양 폭풍{(extend ? " 다시" : "")} — {(StormPeak >= 1f ? "센 양성자 폭풍 · 대피소로 · " : "")}{StormHoursLeft:0}시간쯤 · 센서 흐림 · 선외 작업 금지", null, AlertLevel.Critical, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, $"태양 폭풍이 몰아쳤다 — {hours:0}시간쯤 · 전자 장비가 튀고 센서가 흐려졌다");
        return "태양 폭풍";
    }

    /// <summary>전자 장비 하나가 튄다 (폭풍·서지).</summary>
    private bool Glitch(string why)
    {
        var w = _w;
        var pool = w.Ship.Machines.Where(m => Hazards.IsElectronic(m.Body) && !m.Body.Stowed && !m.Body.Room.Detached && m.Faults.Count == 0).ToList();
        if (pool.Count == 0) return false;
        var m = w.Rng.Pick(pool);
        if (w.Machines.Break(m) is not Fault f) return false;
        StormGlitches++;
        MarkLog.Add(m.Marks, w.Tick, $"{why}에 {f.Spec.Name}");
        return true;
    }

    internal string? Surge()
    {
        var w = _w;
        var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine;
        if (panel == null) return null;
        var parts = new List<string>();
        // 1) 차단기 둘셋이 떨어지고, 한 회로는 단락
        var circuits = Enumerable.Range(0, PowerGrid.CircuitCount).Where(i => !panel.Faults.Any(f => f.Circuit == i)).ToList();
        bool guard = ModulesV15.SurgeGuard(w); // v15 서지 보호기: 차단기 하나만
        int trips = Math.Min(circuits.Count, guard ? 1 : 2 + w.Rng.Range(0, 2));
        for (int i = 0; i < trips; i++)
        {
            int c = circuits[w.Rng.Range(0, circuits.Count)];
            circuits.Remove(c);
            var kind = i == 0 && w.Rng.Chance(0.6f) ? FaultKind.ShortCircuit : FaultKind.BreakerTrip;
            panel.Faults.Add(new Fault { Kind = kind, Since = w.Tick, Circuit = c });
            w.Causes.OnFault(panel, panel.Faults[^1]);
            panel.FaultCount++;
            w.History.CircuitFaults++;
            MarkLog.Add(panel.Marks, w.Tick, $"전력 서지 — {PowerGrid.CircuitName(c)} 회로 {Faults.Spec(kind).Name}");
            parts.Add($"{PowerGrid.CircuitName(c)} {Faults.Spec(kind).Name}");
        }
        // 2) 임시 배선은 과전압에 약하다
        int burnt = 0;
        foreach (var j in w.Power.Jumpers.Where(j => !j.Burnt && !j.Permanent))
            if (w.Rng.Chance(guard ? 0.2f : 0.6f)) { j.Burnt = true; burnt++; }
        if (guard) parts.Add("서지 보호기가 받아 냈다");
        if (burnt > 0) parts.Add($"임시 배선 {burnt}가닥 탐");
        // 3) 배터리 셀이 일부 방전되고, 전자 장비 하나가 튄다
        float lost = w.Power.BatteryCharge * w.Rng.Range(0.2f, 0.4f);
        w.Power.BatteryCharge -= lost;
        if (Glitch("전력 서지")) parts.Add("전자 장비 고장");
        // 4) 조명 하나둘, 그리고 배전반에서 불꽃이 튈 수 있다
        var lit = w.Ship.Rooms.Where(r => !r.LightsOut && !r.Detached && r.Type != RoomType.Corridor).ToList();
        for (int i = 0; i < 2 && lit.Count > 0; i++)
        {
            var r = lit[w.Rng.Range(0, lit.Count)];
            lit.Remove(r);
            if (w.Rng.Chance(0.6f)) w.Fixtures.LightsFail(r, "전력 서지");
        }
        if (w.Rng.Chance(0.3f))
        {
            var spot = Cell.Dirs8.Select(d => panel.Body.Cells[0] + d).FirstOrDefault(w.Ship.IsOpenFloor);
            if (spot != default && w.Fire.Ignite(spot, 0.3f)) parts.Add("배전반 불꽃");
        }
        w.RaiseAlert($"전력 서지 — {string.Join(" · ", parts)}", panel.Body.Room, AlertLevel.Critical, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, $"전력 서지가 배전망을 훑었다 — {string.Join(" · ", parts)}", panel.Body.Room);
        return "전력 서지";
    }

    internal string? Gas(Room? room)
    {
        var w = _w;
        if (room == null || room.Detached) return null;
        var pool = room.Furniture.Where(f => f.Machine != null && !f.Stowed && !f.Machine.Has(FaultKind.GasLeak)).ToList();
        var src = pool.Where(Hazards.IsGasMachine).OrderBy(f => f.Id).FirstOrDefault() ?? pool.OrderBy(f => f.Id).FirstOrDefault();
        if (src == null) return null;
        w.Machines.Break(src.Machine!, FaultKind.GasLeak);
        room.Air.Toxin = MathF.Min(1.5f, room.Air.Toxin + 0.3f);
        foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == room))
        {
            Memory.Frighten(w, c, room, 0.2f, "유독 가스가 샜다");
            c.Interrupt(w);
        }
        w.History.Add(w, HistoryKind.Incident, $"{src.Label}에서 냉매(유독 가스)가 새기 시작했다 — {room.Name}", room, at: src.Cells[0]);
        return $"유독 가스({room.Name})";
    }

    internal string? Blight(Furniture? bed)
    {
        var w = _w;
        if (bed?.Machine?.Crop is not CropState crop || crop.Blight > 0f) return null;
        crop.Blight = 0.06f;
        crop.BlightKnown = false;
        MarkLog.Add(bed.Machine.Marks, w.Tick, "잎 뒷면에 벌레가 붙었다");
        w.Log.Add(w.Tick, LogKind.Ship, $"{bed.Label} 잎 뒷면에 벌레가 붙었다 — 아직 아무도 모른다");
        w.History.Add(w, HistoryKind.Incident, $"{bed.Label}에 병충해가 들었다", bed.Room, at: bed.Cells[0]);
        return $"병충해({bed.Label})";
    }

    internal string? Taint(Furniture? box)
    {
        var w = _w;
        if (box?.Storage is not Inventory inv) return null;
        int meals = inv.Count(ItemKind.Meal) - inv.Tainted;
        if (meals <= 0) return null;
        int n = Math.Min(meals, 3 + w.Rng.Range(0, 3));
        inv.Taint(n);
        MarkLog.Add(box.Machine?.Marks ?? box.Room.Marks, w.Tick, $"식사 {n}끼에 균이 들었다");
        w.Log.Add(w.Tick, LogKind.Ship, $"{box.Label}의 식사 {n}끼에 균이 들었다 — 아직 아무도 모른다");
        w.History.Add(w, HistoryKind.Incident, $"{box.Label}의 식사에 균이 들었다 ({n}끼)", box.Room, at: box.Cells[0]);
        return $"식중독({box.Label})";
    }

    internal string? Computer()
    {
        var w = _w;
        var m = w.Ship.FurnitureOf(FurnitureType.MainComputer).FirstOrDefault(f => !f.Room.Detached)?.Machine;
        if (m == null || m.Has(FaultKind.StorageFault)) return null;
        w.Machines.Break(m, FaultKind.StorageFault);
        w.History.Add(w, HistoryKind.Incident, "주 컴퓨터 저장장치가 망가졌다 — 자동 제어가 멈춘다", m.Body.Room);
        return "주 컴퓨터 오류";
    }

    internal string? RobotHaywire(Robot? r)
    {
        var w = _w;
        if (r == null || r.State is RobotState.Lost or RobotState.Towed || r.Fault is RobotFault.Controller) return null;
        // 멈추기 전에 비틀거린다: 가까이 있던 사람이 로봇 팔·몸체에 부딪힌다
        var hit = w.Crew.Where(c => !c.Dead && !c.Outside && (c.Position - r.Position).LengthSquared() < 2.6f * 2.6f)
            .OrderBy(c => (c.Position - r.Position).LengthSquared()).FirstOrDefault();
        if (hit != null)
        {
            float dmg = w.Rng.Range(0.06f, 0.14f);
            hit.Vitals.Health = MathF.Max(0.05f, hit.Vitals.Health - dmg);
            NeedsSystem.AddInjury(hit.Vitals, dmg * 0.8f, "오작동한 로봇");
            Memory.Shake(w, hit, 0.08f, "오작동한 로봇에 부딪혔다");
            MarkLog.Add(hit.Memory.Marks, w.Tick, $"오작동한 {r.Name}에 부딪혔다");
            w.Log.Add(w.Tick, LogKind.Warning, $"오작동한 {r.Name}에 부딪혀 다쳤다", hit.Id);
        }
        r.Condition = MathF.Max(0.1f, r.Condition - 0.15f);
        w.Robots.ForceFault(r, RobotFault.Controller);
        w.RaiseAlert($"{r.Name} 오작동 — 제어기 오류로 비틀거리다 멈췄다" + (hit != null ? $" · {Ko.IGa(hit.Name)} 부딪혔다" : ""), r.Room, AlertLevel.Warning, shipWide: false);
        w.History.Add(w, HistoryKind.Incident, $"{Ko.IGa(r.Name)} 오작동했다 — 제어기 오류" + (hit != null ? $" · {Ko.IGa(hit.Name)} 부딪혀 다쳤다" : ""), r.Room, hit != null ? new[] { hit } : null);
        return $"로봇 오작동({r.Name})";
    }

    internal string? Crack(Cell? at)
    {
        var w = _w;
        if (at is not Cell c0 || w.Ship.WallAt(c0) is not WallState first || !first.IsHull) return null;
        var room = Hull.InsideRoom(w.Ship, c0);
        if (room == null || room.Detached) return null;
        // 이어진 외벽 두세 칸을 따라 갈라진다
        var cells = new List<Cell> { c0 };
        var cur = c0;
        int len = 2 + w.Rng.Range(0, 2);
        for (int i = 1; i < len; i++)
        {
            var next = Cell.Dirs4.Select(d => cur + d).Where(n => !cells.Contains(n) && w.Ship.WallAt(n) is WallState ws && ws.IsHull && Hull.InsideRoom(w.Ship, n) == room).ToList();
            if (next.Count == 0) break;
            cur = next[w.Rng.Range(0, next.Count)];
            cells.Add(cur);
        }
        foreach (var c in cells)
        {
            var wall = w.Ship.WallAt(c)!;
            wall.MaxIntegrity = MathF.Max(0.4f, wall.MaxIntegrity - w.Rng.Range(0.08f, 0.14f));
            Hull.Damage(w.Ship, c, c == c0 ? w.Rng.Range(0.5f, 0.62f) : w.Rng.Range(0.25f, 0.4f));
            MarkLog.Add(wall.Marks, w.Tick, "피로 균열");
        }
        // 가운데 칸은 실금이 뚫려 조금씩 샌다 (보강판이 버텨 덜 상했어도)
        var core = w.Ship.WallAt(c0)!;
        if (core.Integrity > 0.3f) Hull.Damage(w.Ship, c0, core.Integrity - w.Rng.Range(0.18f, 0.28f));
        MarkLog.Add(room.Marks, w.Tick, $"외벽 피로 균열 ({cells.Count}칸)");
        w.RaiseAlert($"선체 피로 균열 — {room.Name} 외벽 {cells.Count}칸", room, AlertLevel.Warning, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, $"{room.Name} 외벽이 피로로 갈라졌다 ({cells.Count}칸)", room, at: c0);
        return $"선체 균열({room.Name})";
    }

    internal string? Reactor()
    {
        var w = _w;
        var m = w.Power.Reactor;
        if (m == null || !w.Power.ReactorOnline) return null;
        w.Machines.Break(m, FaultKind.ControlFault);
        float rise = w.Rng.Range(55f, 85f);
        w.Power.Heat(rise);
        w.RaiseAlert($"원자로 이상 — 제어봉이 걸리고 노심 온도가 {rise:0}℃ 뛰었다 ({w.Power.ReactorTemperature:0}℃)", m.Body.Room, AlertLevel.Critical, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, $"원자로 제어봉이 걸렸다 — 노심 {w.Power.ReactorTemperature:0}℃", m.Body.Room);
        return "원자로 이상";
    }

    internal string? Jam(Door? d)
    {
        var w = _w;
        if (d == null || d.MotorBroken || d.IsExternal) return null;
        w.Fixtures.BreakDoor(d, "오작동 · 구동기가 탔다");
        return $"문 고장({d.RoomA?.Name ?? "?"}·{d.RoomB?.Name ?? "?"})";
    }

    internal string? Lights(Room? r)
    {
        var w = _w;
        if (r == null || r.LightsOut || r.Detached) return null;
        // 복도는 반쯤만 나가는 규칙이 있어서, 관찰자가 고른 방은 바로 끈다
        r.LightsOut = true;
        r.LightsOutSince = w.Tick;
        w.Fixtures.LightFailures++;
        MarkLog.Add(r.Marks, w.Tick, "조명이 나갔다 (배선 불량)");
        w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name} 조명이 나갔다 — 배선 불량");
        w.History.Add(w, HistoryKind.Damage, $"{r.Name} 조명이 나갔다 — 배선 불량", r);
        return $"조명 나감({r.Name})";
    }

    private static readonly string[] AccidentKinds =
        { "공구에 손을 베였다", "사다리에서 떨어졌다", "무거운 부품에 발을 찧었다", "전기 충격을 받았다", "뜨거운 관에 팔을 데었다", "미끄러져 허리를 삐었다" };

    internal string? Accident(CrewMember? c)
    {
        var w = _w;
        if (c == null || c.Dead) return null;
        string how = AccidentKinds[w.Rng.Range(0, AccidentKinds.Length)];
        float dmg = w.Rng.Range(0.3f, 0.45f);
        // v16.24 대개는 손을 다치고 끝나지만, 가끔 크게 (높은 데서 머리부터 · 센 전기 · 혼자 무거운 걸 들다) — 지친 사람 · 서두른 사람이 더
        bool bad = w.Rng.Chance(0.1f + 0.1f * (c.Needs.Rest < 0.2f ? 1f : 0f) + (c.Job?.Urgent == true ? 0.08f : 0f));
        if (bad) dmg = w.Rng.Range(0.55f, 0.75f);
        c.Vitals.Health = MathF.Max(0.05f, c.Vitals.Health - dmg * (bad ? 1.2f : 0.6f));
        string cause = how.Contains("전기") ? "작업 중 감전" : how.Contains("데었다") ? "작업 중 화상" : how.Contains("떨어") ? "사다리에서 떨어짐" : "작업 사고";
        NeedsSystem.AddInjury(c.Vitals, dmg, cause);
        c.Interrupt(w);
        Memory.Shake(w, c, 0.1f, how);
        MarkLog.Add(c.Memory.Marks, w.Tick, $"작업 사고 — {how}" + (c.Room != null ? $" ({c.Room.Name})" : ""));
        w.Log.Add(w.Tick, LogKind.Warning, $"{how} (체력 {c.Vitals.Health * 100:0}%)", c.Id);
        w.RaiseAlert($"작업 사고 — {Ko.IGa(c.Name)} {how}", c.Room, AlertLevel.Warning, shipWide: false);
        w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} {how} — 작업 사고", c.Room, new[] { c });
        return $"작업 사고({c.Name})";
    }

    internal string? Cloud()
    {
        var w = _w;
        var p = w.Propulsion;
        if (p.Zone == ZoneKind.Debris) return null;
        p.Drift(ZoneKind.Debris);
        // 들어서자마자 한두 개가 날아든다
        var rooms = w.Ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Corridor && w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r)).ToList();
        int n = 1 + w.Rng.Range(0, 2);
        for (int i = 0; i < n && rooms.Count > 0; i++)
            Shower.Add((w.Tick + SimTime.Minutes(w.Rng.Range(5f, 40f)), Scenarios.OuterTarget(w, w.Rng.Pick(rooms)), w.Rng.Range(0.2f, 0.4f)));
        Shower.Sort((a, b) => a.tick.CompareTo(b.tick));
        w.RaiseAlert("잔해 구름 — 배가 잔해 지대로 밀려났다 (엔진을 태워 빠져나와야 한다)", null, AlertLevel.Critical, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, "잔해 구름에 휩쓸려 잔해 지대로 밀려났다");
        return "잔해 구름";
    }

    internal string? Water()
    {
        var w = _w;
        var rec = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).FirstOrDefault(f => !f.Room.Detached)?.Machine;
        if (rec == null || w.Water.Level < 20f) return null;
        float dump = w.Water.Level * w.Rng.Range(0.18f, 0.3f);
        w.Water.Level -= dump;
        if (!rec.Has(FaultKind.FilterClogged)) w.Machines.Break(rec, FaultKind.FilterClogged);
        w.RaiseAlert($"물 오염 — 녹 찌꺼기가 섞여 {dump:0}L를 버렸다 · 정수기 필터가 막혔다", rec.Body.Room, AlertLevel.Warning, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, $"물탱크가 오염됐다 — {dump:0}L를 버리고 정수기 필터가 막혔다", rec.Body.Room);
        return "물 오염";
    }

    // ─────────────────────────────── 틱 ───────────────────────────────

    /// <summary>매 틱: 운석우의 다음 운석.</summary>
    public void Step()
    {
        while (Shower.Count > 0 && Shower[0].tick <= _w.Tick)
        {
            var (_, target, size) = Shower[0];
            Shower.RemoveAt(0);
            var im = _w.Sensors.Launch(target, size);
            if (im != null) im.CauseNode = ShowerNode; // v12.2 운석우의 한 알
        }
    }

    /// <summary>시스템 틱: 폭풍, 가스, 병충해, 오염된 식사, 무작위 사고.</summary>
    public void SystemUpdate(float dt)
    {
        var w = _w;
        UpdateStorm(dt);
        UpdateGas(dt);
        UpdateBlight(dt);
        UpdateTaint();
        UpdateMore(); // v12.4 덕트를 타는 불 · 상하는 음식
        if (Storyteller.Persona != StoryPersona.Off) w.Story.Update(); // v12.4 이야기꾼
        else UpdateRandom();
    }

    private void UpdateStorm(float dt)
    {
        var w = _w;
        if (StormSince >= 0 && !StormActive)
        {
            float hours = (w.Tick - StormSince) / (float)SimTime.TicksPerHour;
            w.Log.Add(w.Tick, LogKind.Ship, $"태양 폭풍이 지나갔다 ({hours:0}시간 · 튄 장비 {StormGlitches}대) — 선외 작업을 다시 한다");
            w.History.Add(w, HistoryKind.Recovery, $"태양 폭풍이 지나갔다 — {hours:0}시간");
            StormSince = -1;
            StormGlitches = 0;
            w.Board.RequestScan();
            return;
        }
        if (!StormActive) return;
        // 선체 밖은 방사선: 우주복이 막아 주지 못한다
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.Outside) continue;
            c.Vitals.Health = MathF.Max(0.05f, c.Vitals.Health - 0.06f * dt);
            NeedsSystem.AddInjury(c.Vitals, 0.05f * dt, "방사선 (태양 폭풍)");
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.08f * dt);
        }
        // 폭풍 동안 가끔 또 튄다
        if (w.Rng.Chance(0.2f * dt)) Glitch("태양 폭풍");
        foreach (var r in w.Robots.Robots)
            if (r.State == RobotState.Active && r.Fault == null && w.Rng.Chance(0.05f * dt)) w.Robots.ForceFault(r, RobotFault.Sensor);
    }

    private void UpdateGas(float dt)
    {
        var w = _w;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached) { r.Air.Toxin = 0f; continue; }
            if (GasSource(r) is Machine m) r.Air.Toxin = MathF.Min(1.5f, r.Air.Toxin + 45f * dt / r.Volume * (0.6f + 0.4f * m.Rating));
            if (r.Air.Toxin <= 0f) continue;
            // 환기가 도는 방은 세정기가 걸러 내고, 막힌 방은 느리게만 가라앉는다
            float clear = Atmosphere.Vented(r) ? 0.45f : 0.06f;
            r.Air.Toxin = MathF.Max(0f, r.Air.Toxin - clear * dt);
            if (r.Air.Toxin < 0.005f) r.Air.Toxin = 0f;
        }
    }

    private void UpdateBlight(float dt)
    {
        var w = _w;
        foreach (var bed in w.Ship.FurnitureOf(FurnitureType.GrowBed).ToList())
        {
            if (bed.Machine?.Crop is not CropState crop || crop.Blight <= 0f) continue;
            if (crop.Growth <= 0.02f && !crop.Ripe) { crop.Blight = 0f; crop.BlightKnown = false; continue; } // 빈 재배대에는 붙을 게 없다
            crop.Blight = MathF.Min(1f, crop.Blight + dt / 22f);
            crop.Care = MathF.Max(0f, crop.Care - dt / 30f);
            if (!crop.BlightKnown && crop.Blight >= 0.16f)
            {
                crop.BlightKnown = true;
                MarkLog.Add(bed.Machine.Marks, w.Tick, "잎에 반점 — 병충해를 알아챘다");
                w.RaiseAlert($"{bed.Label} 병충해 — 잎에 반점이 번진다 (약을 쳐야 한다)", bed.Room, AlertLevel.Warning, shipWide: false);
                w.Board.RequestScan();
            }
            if (crop.Blight >= 1f)
            {
                crop.Growth = 0f;
                crop.Blight = 0f;
                crop.BlightKnown = false;
                w.Machines.CropsLost++;
                BlightKilled++;
                MarkLog.Add(bed.Machine.Marks, w.Tick, "병충해로 작물이 죽었다");
                w.Log.Add(w.Tick, LogKind.Warning, $"{bed.Label}의 작물이 병충해로 죽었다");
                w.History.Add(w, HistoryKind.Damage, $"{bed.Label}의 작물이 병충해로 죽었다", bed.Room);
                continue;
            }
            // 번진다: 같은 방의 다른 재배대로
            if (crop.Blight > 0.35f && w.Rng.Chance(0.12f * dt))
            {
                var next = bed.Room.Furniture.Where(f => f != bed && f.Machine?.Crop is CropState oc && oc.Blight <= 0f && oc.Growth > 0.05f).ToList();
                if (next.Count > 0)
                {
                    var to = w.Rng.Pick(next);
                    to.Machine!.Crop!.Blight = 0.05f;
                    MarkLog.Add(to.Machine.Marks, w.Tick, $"{bed.Label}에서 벌레가 옮아왔다");
                    w.Log.Add(w.Tick, LogKind.Warning, $"병충해가 {bed.Label}에서 {Ko.EuRo(to.Label)} 번졌다");
                }
            }
        }
    }

    private void UpdateTaint()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.CarryTaint > 0 && c.Carrying is not { Kind: ItemKind.Meal }) c.CarryTaint = 0;
            if (c.PoisonAt >= 0 && w.Tick >= c.PoisonAt)
            {
                c.PoisonAt = -1;
                if (!c.Dead) Poison(c, c.PoisonSource);
                c.PoisonSource = null;
            }
        }
        foreach (var r in w.Robots.Robots)
            if (r.CarryTaint > 0 && r.Cargo is not { Kind: ItemKind.Meal }) r.CarryTaint = 0;
        foreach (var f in w.Ship.Containers)
            f.Storage!.ClampTaint();
    }

    /// <summary>오염된 식사를 먹었다 (먹은 곳을 알면 같은 묶음을 찾아 버리게 된다).</summary>
    public void Poison(CrewMember c, Furniture? source)
    {
        var w = _w;
        Poisoned++;
        c.Vitals.Health = MathF.Max(0.08f, c.Vitals.Health - w.Rng.Range(0.08f, 0.16f));
        NeedsSystem.AddInjury(c.Vitals, w.Rng.Range(0.15f, 0.26f), "식중독");
        c.Needs.Food = MathF.Max(0f, c.Needs.Food - 0.35f);
        c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.15f);
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.12f);
        MarkLog.Add(c.Memory.Marks, w.Tick, $"식중독 — {source?.Label ?? "식사"}에서 먹은 것");
        w.Log.Add(w.Tick, LogKind.Warning, "배를 움켜쥐고 토했다 — 식중독", c.Id);
        // 알아챈다: 같은 날 만든 식사 묶음을 따라가 본다 → 오염된 곳마다 버리는 일
        bool traced = false;
        foreach (var f in w.Ship.Containers)
            if (f.Storage!.Tainted > 0 && !f.Storage.TaintKnown) { f.Storage.TaintKnown = true; traced = true; }
        w.RaiseAlert($"식중독 — {Ko.IGa(c.Name)} 앓아누웠다" + (traced ? " · 같은 묶음의 식사를 버린다" : ""), c.Room, AlertLevel.Warning, shipWide: false);
        w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} 식중독으로 앓아누웠다" + (source != null ? $" ({source.Label})" : ""), c.Room, new[] { c });
        w.Board.RequestScan();
    }

    // ─────────────────────────────── 무작위 사고 ───────────────────────────────

    private void UpdateRandom()
    {
        var w = _w;
        if (RandomDays <= 0f) { NextRandom = -1; return; }
        // 출항 첫날은 조용히, 그다음부터 평균 간격으로 (지수 분포)
        if (NextRandom < 0) { NextRandom = Math.Max(w.Tick, w.StartTickOf + SimTime.TicksPerDay) + Gap(); return; }
        if (w.Tick < NextRandom) return;
        // 한창 불을 끄거나 새는 곳을 막는 중이면 조금 미룬다 (잦게보다 느린 설정에서만)
        if (RandomDays >= 1f && (w.Fire.Count > 0 || w.Ship.Rooms.Any(r => r.Leaking && !r.Abandoned && !r.Detached)))
        {
            NextRandom = w.Tick + SimTime.Hours(2);
            return;
        }
        NextRandom = w.Tick + Gap();
        FireRandom();
    }

    private long Gap()
    {
        float u = RandomRng.Float();
        float days = -MathF.Log(1f - MathF.Min(u, 0.995f)) * RandomDays;
        return SimTime.Hours(MathF.Max(4f, days * 24f));
    }

    /// <summary>무작위 사고 하나 (기록되지 않는다 — 같은 시드·같은 수치면 같은 때에 같은 일이 난다).</summary>
    public string? FireRandom()
    {
        var w = _w;
        var rr = RandomRng;
        // 고전 사고(운석·화재·고장·배관)와 새 사고를 무게대로
        var pool = new List<(string key, float weight)>
        {
            ("meteor", 12f), ("bigmeteor", 4f), ("fire", 9f), ("break", 12f),
        };
        if (w.Piping.Segments.Count > 0) pool.Add(("pipe", 5f));
        foreach (var s in Hazards.All)
        {
            if (s.Kind == HazardKind.RobotMalfunction && !w.Robots.Robots.Any(r => r.State is RobotState.Active or RobotState.Docked)) continue;
            if (s.Kind == HazardKind.DebrisCloud && (w.Propulsion.Zone == ZoneKind.Debris || !w.Ship.FurnitureOf(FurnitureType.EngineCore).Any())) continue;
            if (s.Kind == HazardKind.SolarStorm && StormActive) continue;
            pool.Add((s.Kind.ToString(), s.Weight));
        }
        MajorIncidentSystem.AddToPool(w, pool, false, 1f); MajorIncidentSystem.ShapeByScale(pool); // v16.19 계통 · 배 전체 사고 · 규모 비율
        for (int i = 0; i < pool.Count; i++) pool[i] = (pool[i].key, pool[i].weight * w.Voyage.HazardMul(pool[i].key) * w.Eras.RiskMul(pool[i].key)); // v12.8 구간 · 새 기술의 위험
        for (int tries = 0; tries < 6; tries++)
        {
            float total = pool.Sum(p => p.weight), roll = rr.Float() * total;
            string key = pool[^1].key;
            foreach (var p in pool) { roll -= p.weight; if (roll <= 0f) { key = p.key; break; } }
            string? what = RandomOne(key);
            if (what == null) continue;
            RandomCount++;
            LastRandom = Enum.TryParse<HazardKind>(key, out var hk) ? hk : null;
            LastRandomText = what;
            w.Log.Add(w.Tick, LogKind.Ship, $"무작위 사고: {what}");
            return what;
        }
        return null;
    }

    /// <summary>v12.4 이야기꾼이 고른 사고를 건다 (room이 있으면 그 방에).</summary>
    public string? FireStory(string key, Room? room)
    {
        var what = RandomOne(key, room);
        if (what != null) { RandomCount++; LastRandom = Enum.TryParse<HazardKind>(key, out var hk) ? hk : null; LastRandomText = what; }
        return what;
    }

    /// <summary>v16.24 사고는 대개 쓰는 중에 난다: 깨어 있는 사람이 있는 방을 더 자주 고른다 (한 방에 셋까지 · 빈 방도 가끔).</summary>
    private T Busy<T>(List<T> pool, Func<T, Room> roomOf, Rng rr, bool sleepers = false)
    {
        var w = _w;
        var wt = new float[pool.Count];
        float sum = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            var r = roomOf(pool[i]);
            int n = 0;
            foreach (var c in w.Crew) if (!c.Dead && c.Room == r && (c.IsAwake || sleepers && c.Pose == Pose.Sleeping)) n++;
            sum += wt[i] = 1f + 1.5f * Math.Min(3, n);
        }
        float x = rr.Float() * sum;
        for (int i = 0; i < pool.Count; i++) { x -= wt[i]; if (x <= 0f) return pool[i]; }
        return pool[^1];
    }

    private string? RandomOne(string key, Room? prefer = null)
    {
        var w = _w;
        var rr = RandomRng;
        var ship = w.Ship;
        var rooms = ship.Rooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor).ToList();
        if (rooms.Count == 0) return null;
        if (key.StartsWith("major:")) return w.Major.Fire(key, prefer); // v16.19
        switch (key)
        {
            case "meteor":
            case "bigmeteor":
            {
                var hullRooms = rooms.Where(r => ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r)).ToList();
                if (hullRooms.Count == 0) return null;
                var room = prefer != null && hullRooms.Contains(prefer) ? prefer : Busy(hullRooms, r => r, rr, sleepers: true); // 통합: 사람이 지내는 외벽 방에도 (자는 방 · 일하는 방)
                float size = key == "bigmeteor" ? 0.8f + 0.2f * rr.Float() : 0.25f + 0.3f * rr.Float();
                var m = w.Sensors.Launch(Scenarios.OuterTarget(w, room), size);
                if (m == null) return null;
                string what = $"{(size >= 0.7f ? "큰" : "작은")} 운석({room.Name})";
                w.History.NoteCause(w, what);
                return what;
            }
            case "fire":
            {
                var room = prefer != null && rooms.Contains(prefer) ? prefer : Busy(rooms, r => r, rr, sleepers: true); // 통합: 불도 대개 쓰는 방에서 난다 (조리 · 용접 · 전기 기구 · 잠든 방의 충전기)
                var floor = room.Cells.Where(ship.IsOpenFloor).ToList();
                if (floor.Count == 0 || !Incidents.Fire(w, floor[rr.Range(0, floor.Count)])) return null;
                string what = $"화재({room.Name})";
                w.History.NoteCause(w, what);
                return what;
            }
            case "break":
            {
                var machines = ship.Machines.Where(m => !m.Body.Stowed && !m.Body.Room.Detached && m.Body.Type is not (FurnitureType.Cot or FurnitureType.SupplyCache)).ToList();
                if (machines.Count == 0) return null;
                var m = machines[rr.Range(0, machines.Count)];
                if (w.Machines.Break(m) == null) return null;
                string what = $"고장({m.Name})";
                w.History.NoteCause(w, what);
                return what;
            }
            case "pipe":
            {
                var seg = w.Piping.Segments[rr.Range(0, w.Piping.Segments.Count)];
                if (seg.Path.Count == 0) return null;
                var hit = w.Piping.Burst(seg.Path[rr.Range(0, seg.Path.Count)], 0.3f + 0.6f * rr.Float());
                if (hit == null) return null;
                string what = $"배관 파손({hit.Name})";
                w.History.NoteCause(w, what);
                return what;
            }
        }
        if (!Enum.TryParse<HazardKind>(key, out var k)) return null;
        var spec = Hazards.Spec(k);
        Cell at = default;
        int id = -1;
        switch (spec.Target)
        {
            case HazardTarget.Room:
                at = (prefer != null && rooms.Contains(prefer) ? prefer : Busy(rooms, r => r, rr)).Cells[0]; // v16.24 쓰는 방에서 더 자주 (돌리고 · 켜고 · 끓이는 중에 난다)
                break;
            case HazardTarget.Machine:
            {
                var pool = ship.Furniture.Where(f => !f.Stowed && !f.Room.Detached && Hazards.MachineAt(w, k, f.Cells[0]) == f).ToList();
                if (pool.Count == 0) return null;
                var pick = prefer != null ? pool.FirstOrDefault(f => f.Room == prefer) : null;
                at = (pick ?? Busy(pool, f => f.Room, rr)).Cells[0]; // v16.24 쓰는 설비가 더 자주 탈 난다
                break;
            }
            case HazardTarget.Hull:
            {
                var hull = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) is Room r && !r.Detached && r.Type != RoomType.Corridor).Select(kv => kv.Key).ToList();
                if (hull.Count == 0) return null;
                at = hull[rr.Range(0, hull.Count)];
                break;
            }
            case HazardTarget.Door:
            {
                var doors = ship.Doors.Where(d => !d.IsExternal && !d.MotorBroken).ToList();
                if (doors.Count == 0) return null;
                at = doors[rr.Range(0, doors.Count)].Cell;
                break;
            }
            case HazardTarget.Crew:
            {
                // 일하던 사람이 다친다 (자는 사람은 안 다친다)
                var working = w.Crew.Where(c => !c.Dead && !c.Down && c.Pose == Pose.Working).ToList();
                if (working.Count == 0) return null;
                id = working[rr.Range(0, working.Count)].Id;
                break;
            }
            case HazardTarget.Robot:
            {
                var bots = w.Robots.Robots.Where(r => r.State is RobotState.Active or RobotState.Docked && r.Fault == null).ToList();
                if (bots.Count == 0) return null;
                id = bots[rr.Range(0, bots.Count)].Id;
                break;
            }
        }
        return Hazards.Apply(w, k, at, id);
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v11.2 사고 뒷정리: 오염을 알아챈 식사 묶음을 버린다 (병충해·가스는 작물 돌보기·수리로 올라간다).</summary>
    private void ScanHazards(Poster post)
    {
        var w = _world;
        foreach (var f in w.Ship.Containers)
        {
            var inv = f.Storage!;
            if (inv.Tainted <= 0 || !inv.TaintKnown || f.Room.OffLimits || f.Room.Abandoned) continue;
            post(WorkKind.DiscardFood, WorkTarget.Of(f), 0.75f, Skill.Cooking, $"균이 든 식사 {inv.Tainted}끼 · 더 먹기 전에 버린다");
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>상한 식사 버리기: 같은 묶음을 골라 재활용 통에 넣는다 (먹을 것이 그만큼 준다).</summary>
    private static Job? DiscardFood(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var box = o.Target.Furniture!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.25f, Skill.Cooking, box.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            var inv = box.Storage!;
            int n = inv.Tainted;
            if (n > 0) inv.Take(ItemKind.Meal, n); // 균이 든 것부터 나온다
            inv.ClampTaint();
            world.Hazards.FoodDiscarded += n;
            world.Board.Close(o);
            if (n > 0)
            {
                MarkLog.Add(box.Machine?.Marks ?? box.Room.Marks, world.Tick, $"{cm.Name}: 균이 든 식사 {n}끼를 버렸다");
                world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} {box.Label}에서 균이 든 식사 {n}끼를 버렸다", box.Room, new[] { cm }, log: true, crewLog: cm.Id);
            }
            return true;
        }));
        return Wrap(a, o, c, w, "위생", toils, o.Title);
    }
}
