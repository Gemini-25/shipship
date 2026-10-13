using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 극한 환경 사고 (docs/EXTREME_ENVIRONMENTS.md):
//  지금 머무는 곳(내려앉은 행성 · 항로 구간 · 우주 재난)에서 환경을 읽는다 — 온도 · 날씨 · 대기 · 중력 · 방사선 · 빛 · 자기장 · 지형.
//  환경은 요인 기제 22가지(결빙 · 과열 · 부식 · 정전기 · 먼지 · 방사선 · 압력 · 고중력 · 저중력 · 강풍 · 흔들림 · 낙뢰 · 유독 가스 · 어둠 · 자기장 · 고립
//  · 가연 가스 · 침수 · 충돌 · 화산 · 포자 · 시간 왜곡)의 세기로 나타나고, 사고는 기제 × 대상(규모마다: 개인 10 · 방 13 · 계통 12 · 배 전체 10 · 우주급 10)이다.
//  사고는 낌새 → (주컴퓨터가 알아채면 로봇을 보내 막는다) → 터짐 → 기존 사고 · 부상 · 병 · 온도 · 피폭으로 번짐 → 뒤끝(흔적 · 기록 · 적응).
//  환경마다 규모별 30가지가 넘는다 (EnvHazardSystem.Catalog — --envtest가 센다).

public enum EnvKind : byte { None, Frozen, Molten, HighG, LowG, RadMoon, GasGiant, IceOcean, Venus, Titan, Io, TidalLock, DeepDark, Pulsar, BlackHole, Nebula, Comet, Spores }

public enum EnvMech : byte { Freeze, Heat, Corrode, Static, Dust, Radiation, Pressure, HeavyG, LightG, Wind, Quake, Lightning, Toxic, Dark, Magnetic, Isolation, Volatile, Flood, Impact, Volcanic, Bio, TimeWarp }

/// <summary>지금 머무는 곳의 환경.</summary>
public sealed class EnvProfile
{
    public const int Mechs = 22;
    public EnvKind Kind { get; set; }
    public string Where { get; set; } = "";
    public float TempC { get; set; } = 20f;
    public float Gravity { get; set; } = 1f;
    public float Pressure { get; set; } = 100f;
    public string Weather { get; set; } = "";
    public float[] Mech { get; } = new float[Mechs];

    public float this[EnvMech m] { get => Mech[(int)m]; set => Mech[(int)m] = Math.Clamp(value, 0f, 1f); }
    public void Add(EnvMech m, float v) => this[m] = this[m] + v;

    /// <summary>센 기제 넷의 평균 (0 평온 ~ 1 극한).</summary>
    public float Severity => Mech.OrderByDescending(x => x).Take(4).Average();
    public IEnumerable<EnvMech> Active => Enumerable.Range(0, Mechs).Where(i => Mech[i] >= EnvHazardSystem.ActiveAt).OrderByDescending(i => Mech[i]).Select(i => (EnvMech)i);
    public string Name => EnvHazardSystem.KindName(Kind);
    public string Line() => Kind == EnvKind.None ? "평온" : $"{Name} · {Where} · {TempC:0}°C" + (Weather.Length > 0 ? $" · {Weather}" : "") + $" · 위험 {Severity * 100:0}% · " + string.Join("·", Active.Take(4).Select(EnvHazardSystem.MechName));
}

public sealed record EnvTarget(string Key, string Name, IncidentScale Scale, string Class);

public sealed record EnvCombo(EnvMech Mech, EnvTarget Target, string Name);

/// <summary>낌새가 보인 뒤 터지기를 기다리는 사고.</summary>
public sealed class EnvCase
{
    public int Id { get; init; }
    public EnvMech Mech { get; init; }
    public string Target { get; init; } = "";
    public IncidentScale Scale { get; init; }
    public string Name { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public long StrikeAt { get; init; }
    public float Sev { get; set; }
    public bool Detected { get; set; }
    public int BotId { get; set; } = -1;
}

public sealed class EnvStats
{
    public int[] Fired { get; } = new int[5];
    public int[] Prevented { get; } = new int[5];
    public int[] Omens { get; } = new int[5];
    public int[] ByMech { get; } = new int[EnvProfile.Mechs];
    public int Hazards, Injured, Adapted, EarlyLifts;
    public string Line() => $"환경 사고 {Fired.Sum()} (개인 {Fired[0]} · 방 {Fired[1]} · 계통 {Fired[2]} · 배 {Fired[3]} · 우주급 {Fired[4]}) · 막음 {Prevented.Sum()} · 낌새 {Omens.Sum()} · 이어진 사고 {Hazards} · 다친 사람 {Injured} · 적응 {Adapted}";
}

public sealed class EnvHazardSystem
{
    /// <summary>시험: 끄면 환경 사고가 없다.</summary>
    public static bool Off;
    public const float ActiveAt = 0.15f;

    private readonly World _w;
    private readonly Rng _rng;
    private long _nextHour, _nextCheck, _cosmicAfter, _shipAfter;
    private int _nextId;
    public EnvProfile Now { get; private set; } = new();
    /// <summary>시험 · 이야기: 이 환경으로 본다.</summary>
    public EnvProfile? Force { get; set; }
    /// <summary>우주급 사고 뒤 한동안 환경이 거칠어진다.</summary>
    public long IntenseUntil { get; private set; } = -1;
    public List<EnvCase> Pending { get; } = new();
    public EnvStats Stats { get; } = new();
    public int[] Seen { get; } = new int[EnvProfile.Mechs];
    public List<(long tick, IncidentScale scale, string text, bool prevented)> Recent { get; } = new();

    public EnvHazardSystem(World w) { _w = w; _rng = new Rng(unchecked(w.Seed * 31 + 4242)); }

    // ─────────────────────────────── 이름 ───────────────────────────────

    public static string KindName(EnvKind k) => k switch
    {
        EnvKind.Frozen => "얼음 행성", EnvKind.Molten => "작열 · 용암 행성", EnvKind.HighG => "고중력 행성", EnvKind.LowG => "저중력 위성 · 소행성", EnvKind.RadMoon => "방사선대 위성",
        EnvKind.GasGiant => "가스 거인 구름층", EnvKind.IceOcean => "얼음 위성 속 바다", EnvKind.Venus => "금성형 행성", EnvKind.Titan => "메탄 바다 위성", EnvKind.Io => "화산 위성",
        EnvKind.TidalLock => "조석 고정 행성", EnvKind.DeepDark => "떠돌이 · 깊은 우주", EnvKind.Pulsar => "쌍성 · 펄서 · 마그네타", EnvKind.BlackHole => "블랙홀 근처",
        EnvKind.Nebula => "성운", EnvKind.Comet => "혜성 꼬리 · 먼지 띠", EnvKind.Spores => "포자 구름", _ => "평온",
    };

    private static readonly string[] MechNames = { "결빙", "과열", "부식", "정전기", "먼지", "방사선", "압력", "고중력", "저중력", "강풍", "흔들림", "낙뢰", "유독 가스", "어둠", "자기장", "고립", "가연 가스", "침수", "충돌", "화산", "포자", "시간 왜곡" };
    public static string MechName(EnvMech m) => MechNames[(int)m];

    /// <summary>방 · 계통 · 배에 붙는 이름 (○○ 결빙).</summary>
    private static readonly string[] Noun = { "결빙", "과열", "부식", "정전기 방전", "먼지 막힘", "방사선 손상", "압력 균열", "하중 사고", "떠오름 사고", "강풍 피해", "흔들림 피해", "낙뢰", "유독 가스", "어둠 속 사고", "자기장 이상", "고립 사고", "가연 가스 누출", "침수", "충돌", "화산 피해", "포자 오염", "시간 왜곡" };
    /// <summary>사람이 입는 것.</summary>
    private static readonly string[] Hurt = { "동상", "열사병", "화학 화상", "감전", "먼지 흡입", "피폭", "압력 손상", "낙상", "튕겨 나감", "날려 넘어짐", "흔들려 다침", "벼락", "가스 중독", "어둠 속 넘어짐", "날아온 공구", "고립 공황", "가스 질식", "물에 빠짐", "파편 맞음", "화산재 화상", "포자 흡입", "시간 멀미" };
    /// <summary>우주급 (그 땅 · 그 하늘 전체).</summary>
    private static readonly string[] Great = { "대한파", "대열파", "산성 폭풍", "정전기 폭풍", "대먼지 폭풍", "방사선 폭풍", "압력 붕괴", "중력 우물", "표류", "초강풍", "대지진", "번개 폭풍", "독가스 폭풍", "긴 밤", "자기 폭풍", "완전 고립", "가스 폭발 띠", "대범람", "유성 폭풍", "대분화", "포자 폭풍", "시간 지연" };
    /// <summary>낌새 (주컴퓨터가 보는 것).</summary>
    private static readonly string[] Omen = { "온도가 빠르게 떨어진다", "열이 차오른다", "금속 표면이 변색된다", "전하가 쌓인다", "먼지 농도가 오른다", "방사선 계수가 치솟는다", "압력계가 흔들린다", "골조 하중이 늘었다", "고정 장치가 헐겁다", "풍속이 오른다", "진동이 커진다", "대기 전위가 오른다", "가스 감지기가 울린다", "빛이 줄어든다", "자기장이 요동친다", "통신이 끊기기 시작한다", "가연 가스가 감지된다", "수위가 오른다", "물체가 다가온다", "땅 밑이 달아오른다", "포자 농도가 오른다", "시계가 어긋난다" };

    private static readonly EnvTarget[] Targets =
    {
        // 개인
        new("outside", "선외 작업자", IncidentScale.Personal, "crew"), new("suit", "우주복", IncidentScale.Personal, "suit"), new("worker", "일하던 사람", IncidentScale.Personal, "crew"),
        new("sleeper", "자던 사람", IncidentScale.Personal, "crew"), new("body", "한 사람의 몸", IncidentScale.Personal, "crew"), new("mind", "한 사람의 마음", IncidentScale.Personal, "mind"),
        new("elder", "노약자", IncidentScale.Personal, "crew"), new("hand", "손 · 발", IncidentScale.Personal, "crew"), new("robot", "로봇 한 대", IncidentScale.Personal, "robot"),
        new("drone", "드론 한 대", IncidentScale.Personal, "drone"),
        // 방
        new("airlock", "에어록", IncidentScale.Room, "airlock"), new("window", "외벽 창", IncidentScale.Room, "window"), new("door", "문", IncidentScale.Room, "door"),
        new("pipe", "방 배관", IncidentScale.Room, "pipe"), new("roomair", "방 공기", IncidentScale.Room, "roomair"), new("machine", "방 설비", IncidentScale.Room, "machine"),
        new("galley", "주방 · 냉장고", IncidentScale.Room, "galley"), new("crops", "재배실", IncidentScale.Room, "crops"), new("quarters", "침실", IncidentScale.Room, "quarters"),
        new("medbay", "의무실", IncidentScale.Room, "medbay"), new("storage", "창고", IncidentScale.Room, "storage"), new("lights", "방 조명", IncidentScale.Room, "lights"),
        new("hull", "외벽 한 칸", IncidentScale.Room, "hull"),
        // 계통
        new("water", "급수 계통", IncidentScale.System, "water"), new("power", "전력 계통", IncidentScale.System, "power"), new("air", "공기 계통", IncidentScale.System, "air"),
        new("cooling", "냉각 계통", IncidentScale.System, "cooling"), new("comms", "통신 계통", IncidentScale.System, "comms"), new("computer", "주컴퓨터 저장 · 판단", IncidentScale.System, "computer"),
        new("robots", "로봇 무리", IncidentScale.System, "robots"), new("cropsys", "재배 계통", IncidentScale.System, "crops"), new("food", "식량 계통", IncidentScale.System, "food"),
        new("fuel", "연료 계통", IncidentScale.System, "fuel"), new("sensors", "센서 · 항법", IncidentScale.System, "sensors"), new("frame", "골조", IncidentScale.System, "frame"),
        // 배 전체
        new("hullall", "선체 전체", IncidentScale.Ship, "hull"), new("crewall", "모든 사람", IncidentScale.Ship, "crew"), new("reactor", "원자로 · 전력 전체", IncidentScale.Ship, "power"),
        new("stance", "배 자세 · 착륙 다리", IncidentScale.Ship, "frame"), new("climate", "배 안 온도", IncidentScale.Ship, "roomair"), new("airall", "배 공기 전체", IncidentScale.Ship, "air"),
        new("brain", "주컴퓨터", IncidentScale.Ship, "computer"), new("morale", "사기", IncidentScale.Ship, "mind"), new("fleet", "로봇 함대", IncidentScale.Ship, "robots"),
        new("supplies", "물자 전체", IncidentScale.Ship, "food"),
        // 우주급 (그 땅 · 하늘 전체가 배를 덮친다 — 어디로 번지나)
        new("c-hull", "선체", IncidentScale.Cosmic, "hull"), new("c-crew", "사람들", IncidentScale.Cosmic, "crew"), new("c-power", "전력", IncidentScale.Cosmic, "power"),
        new("c-stance", "배 자세", IncidentScale.Cosmic, "frame"), new("c-air", "공기", IncidentScale.Cosmic, "air"), new("c-brain", "주컴퓨터", IncidentScale.Cosmic, "computer"),
        new("c-morale", "마음", IncidentScale.Cosmic, "mind"), new("c-fleet", "로봇 · 드론", IncidentScale.Cosmic, "robots"), new("c-supplies", "물자", IncidentScale.Cosmic, "food"),
        new("c-comms", "통신", IncidentScale.Cosmic, "comms"),
    };

    /// <summary>특별한 이름 (환경 사고표 문서의 사고들).</summary>
    private static readonly Dictionary<(EnvMech, string), string> Named = new()
    {
        [(EnvMech.Freeze, "airlock")] = "에어록 결빙 — 문틈이 얼어 닫히지 않는다", [(EnvMech.Freeze, "suit")] = "우주복 히터 고장", [(EnvMech.Freeze, "pipe")] = "배관 동파",
        [(EnvMech.Freeze, "stance")] = "착륙 다리 결빙 — 땅에 얼어붙었다", [(EnvMech.Freeze, "robot")] = "윤활유가 굳은 로봇", [(EnvMech.Freeze, "outside")] = "선외 작업자 동상",
        [(EnvMech.Freeze, "hullall")] = "선체 위 눈더미 · 성에", [(EnvMech.Freeze, "climate")] = "난방 과부하 — 모두 한 방에 모인다", [(EnvMech.Freeze, "sleeper")] = "자다가 저체온",
        [(EnvMech.Freeze, "power")] = "배터리 급감", [(EnvMech.Freeze, "window")] = "창 성에 균열",
        [(EnvMech.Heat, "cooling")] = "냉각 한계 — 원자로 출력을 깎는다", [(EnvMech.Heat, "suit")] = "우주복 냉각 고장", [(EnvMech.Heat, "crewall")] = "열사병 집단 발생",
        [(EnvMech.Heat, "food")] = "냉장고 과부하 — 식량 부패", [(EnvMech.Heat, "stance")] = "녹는 땅 — 배가 기운다", [(EnvMech.Heat, "robot")] = "배터리 열폭주 직전 로봇",
        [(EnvMech.Corrode, "hullall")] = "선체 부식 — 외판이 매일 얇아진다", [(EnvMech.Corrode, "robots")] = "로봇 관절 부식", [(EnvMech.Corrode, "sensors")] = "바깥 센서 부식",
        [(EnvMech.Static, "outside")] = "선외 작업자 감전", [(EnvMech.Static, "power")] = "정전기 방전 화재 위험", [(EnvMech.Dust, "robots")] = "먼지에 갉힌 로봇 관절",
        [(EnvMech.Dust, "air")] = "흡입구 막힘", [(EnvMech.Radiation, "crewall")] = "방사선 병 집단 발생", [(EnvMech.Radiation, "computer")] = "전자 기억 뒤집힘",
        [(EnvMech.Radiation, "cropsys")] = "씨앗 변이", [(EnvMech.Radiation, "brain")] = "주컴퓨터 기억 손상", [(EnvMech.Pressure, "window")] = "압력 차 — 창이 터질 듯",
        [(EnvMech.Pressure, "hullall")] = "가라앉으며 짓눌리는 선체", [(EnvMech.HeavyG, "crewall")] = "몸이 못 버틴다 — 일 속도 절반", [(EnvMech.HeavyG, "stance")] = "이륙 연료 부족 — 짐을 버려야 뜬다",
        [(EnvMech.HeavyG, "frame")] = "골조 처짐", [(EnvMech.HeavyG, "elder")] = "심장 부담", [(EnvMech.LightG, "outside")] = "튕겨 날아간 사람", [(EnvMech.LightG, "stance")] = "닻 풀림 — 배가 표류한다",
        [(EnvMech.Wind, "outside")] = "초강풍에 끊긴 안전줄", [(EnvMech.Quake, "stance")] = "조석 융기 — 기지가 갈라진다", [(EnvMech.Lightning, "power")] = "초거대 번개 서지",
        [(EnvMech.Toxic, "airlock")] = "황산 안개가 밴 에어록", [(EnvMech.Dark, "morale")] = "끝없는 밤의 우울", [(EnvMech.Dark, "cropsys")] = "빛이 모자라 시드는 작물",
        [(EnvMech.Magnetic, "medbay")] = "의료 기기 오작동", [(EnvMech.Magnetic, "hand")] = "날아든 철 공구", [(EnvMech.Isolation, "mind")] = "환청 · 시간 감각 상실",
        [(EnvMech.Isolation, "morale")] = "고향 소식이 끊겼다", [(EnvMech.Volatile, "roomair")] = "새는 극저온 연료 — 소리 없는 질식", [(EnvMech.Volatile, "airlock")] = "에어록 혼합 — 산소와 메탄",
        [(EnvMech.Flood, "stance")] = "메탄 호수 위 표류", [(EnvMech.Impact, "hull")] = "미세 운석 직격", [(EnvMech.Volcanic, "hullall")] = "화산탄 · 화산재 비",
        [(EnvMech.TimeWarp, "morale")] = "시간 지연 편지 폭탄 — 몇 년 치 부고", [(EnvMech.TimeWarp, "frame")] = "조석 응력 균열", [(EnvMech.Bio, "crops")] = "작물 대역병",
    };

    // 기제 → 이어지는 기존 사고 (열쇠 · 맞는 대상 부류)
    private static readonly (string key, EnvMech[] mechs, string[] cls)[] Links =
    {
        ("PipeFreeze", new[] { EnvMech.Freeze }, new[] { "pipe", "water" }),
        ("ValveSeize", new[] { EnvMech.Freeze, EnvMech.Corrode, EnvMech.Dust }, new[] { "pipe", "water", "cooling" }),
        ("CondensateFlood", new[] { EnvMech.Freeze, EnvMech.Flood }, new[] { "roomair", "quarters", "water", "storage" }),
        ("CellAging", new[] { EnvMech.Freeze, EnvMech.Heat, EnvMech.Dark }, new[] { "power", "robots" }),
        ("DoorJam", new[] { EnvMech.Freeze, EnvMech.Dust, EnvMech.Corrode, EnvMech.Quake, EnvMech.Magnetic }, new[] { "door", "airlock" }),
        ("SealLeak", new[] { EnvMech.Freeze, EnvMech.Corrode, EnvMech.Pressure, EnvMech.Heat }, new[] { "airlock", "window", "hull", "air" }),
        ("HatchSeal", new[] { EnvMech.Freeze, EnvMech.Pressure, EnvMech.Corrode, EnvMech.Wind }, new[] { "airlock" }),
        ("WindowCrack", new[] { EnvMech.Freeze, EnvMech.Heat, EnvMech.Pressure, EnvMech.Impact, EnvMech.Quake, EnvMech.Wind }, new[] { "window", "hull" }),
        ("ThermalStress", new[] { EnvMech.Freeze, EnvMech.Heat }, new[] { "hull", "frame", "cooling" }),
        ("BearingSeize", new[] { EnvMech.Freeze, EnvMech.Dust }, new[] { "machine", "robots", "air" }),
        ("Overheat", new[] { EnvMech.Heat, EnvMech.Volcanic }, new[] { "machine", "power", "cooling" }),
        ("HeaterOverload", new[] { EnvMech.Freeze }, new[] { "power", "quarters" }),
        ("CoolantLoss", new[] { EnvMech.Heat, EnvMech.Volcanic }, new[] { "cooling" }),
        ("InsulationSmoke", new[] { EnvMech.Heat, EnvMech.Corrode, EnvMech.Lightning }, new[] { "power", "roomair" }),
        ("FreezerFailure", new[] { EnvMech.Heat }, new[] { "galley", "food" }),
        ("FoodPoisoning", new[] { EnvMech.Heat, EnvMech.Bio }, new[] { "food", "galley" }),
        ("InsulationCrack", new[] { EnvMech.Corrode, EnvMech.Freeze, EnvMech.Radiation }, new[] { "power" }),
        ("WeldFatigue", new[] { EnvMech.Corrode, EnvMech.Pressure, EnvMech.Quake, EnvMech.HeavyG, EnvMech.TimeWarp }, new[] { "hull", "frame" }),
        ("HullCrack", new[] { EnvMech.Corrode, EnvMech.Pressure, EnvMech.Impact, EnvMech.Quake, EnvMech.Freeze, EnvMech.Heat, EnvMech.TimeWarp, EnvMech.Volcanic }, new[] { "hull", "frame" }),
        ("TankSludge", new[] { EnvMech.Corrode, EnvMech.Dust }, new[] { "water", "fuel" }),
        ("StaticDischarge", new[] { EnvMech.Static, EnvMech.Lightning, EnvMech.Magnetic }, new[] { "power", "comms", "machine" }),
        ("ArcFault", new[] { EnvMech.Static, EnvMech.Lightning, EnvMech.Magnetic, EnvMech.Flood }, new[] { "power", "machine" }),
        ("GroundFault", new[] { EnvMech.Static, EnvMech.Flood, EnvMech.Corrode }, new[] { "power" }),
        ("PowerSurge", new[] { EnvMech.Lightning, EnvMech.Static, EnvMech.Magnetic, EnvMech.Radiation }, new[] { "power" }),
        ("BreakerCascade", new[] { EnvMech.Lightning, EnvMech.Static }, new[] { "power" }),
        ("ComputerFault", new[] { EnvMech.Radiation, EnvMech.Static, EnvMech.Magnetic, EnvMech.Lightning }, new[] { "computer", "comms" }),
        ("RebootGlitch", new[] { EnvMech.Radiation, EnvMech.Magnetic, EnvMech.Static }, new[] { "computer" }),
        ("BlackboxGap", new[] { EnvMech.Radiation, EnvMech.Magnetic, EnvMech.TimeWarp }, new[] { "computer" }),
        ("RobotMalfunction", new[] { EnvMech.Radiation, EnvMech.Magnetic, EnvMech.Static, EnvMech.Dust, EnvMech.Freeze, EnvMech.Heat, EnvMech.Corrode }, new[] { "robot", "robots" }),
        ("ScrubberSaturation", new[] { EnvMech.Dust, EnvMech.Toxic, EnvMech.Volcanic, EnvMech.Bio }, new[] { "air" }),
        ("Co2Spike", new[] { EnvMech.Dust, EnvMech.Toxic, EnvMech.Volcanic }, new[] { "air", "roomair" }),
        ("FanImbalance", new[] { EnvMech.Dust, EnvMech.Volcanic, EnvMech.Quake }, new[] { "air", "cooling", "machine" }),
        ("GasLeak", new[] { EnvMech.Toxic, EnvMech.Volatile, EnvMech.Corrode }, new[] { "roomair", "air" }),
        ("Ozone", new[] { EnvMech.Radiation, EnvMech.Lightning, EnvMech.Toxic }, new[] { "roomair", "air" }),
        ("HydrogenBuildup", new[] { EnvMech.Volatile, EnvMech.Heat }, new[] { "power", "fuel" }),
        ("GasTankRupture", new[] { EnvMech.Volatile, EnvMech.Pressure, EnvMech.Heat, EnvMech.Freeze }, new[] { "fuel", "storage" }),
        ("OxygenLeak", new[] { EnvMech.Volatile, EnvMech.Pressure }, new[] { "roomair", "air" }),
        ("RadiationBurst", new[] { EnvMech.Radiation }, new[] { "crew" }),
        ("GammaFlash", new[] { EnvMech.Radiation, EnvMech.TimeWarp }, new[] { "crew" }),
        ("IonStorm", new[] { EnvMech.Static, EnvMech.Radiation, EnvMech.Magnetic }, new[] { "comms", "sensors" }),
        ("MagnetarPulse", new[] { EnvMech.Magnetic }, new[] { "computer", "sensors" }),
        ("MicroShower", new[] { EnvMech.Impact }, new[] { "hull" }),
        ("meteor", new[] { EnvMech.Impact, EnvMech.Volcanic }, new[] { "hull", "window" }),
        ("bigmeteor", new[] { EnvMech.Impact }, new[] { "hull" }),
        ("DebrisCloud", new[] { EnvMech.Impact, EnvMech.Dust }, new[] { "hull", "sensors" }),
        ("CometTail", new[] { EnvMech.Impact, EnvMech.Dust, EnvMech.Volatile }, new[] { "hull", "sensors" }),
        ("FrameCreak", new[] { EnvMech.Quake, EnvMech.HeavyG, EnvMech.Pressure, EnvMech.TimeWarp, EnvMech.Wind }, new[] { "frame", "hull" }),
        ("LooseMount", new[] { EnvMech.Quake, EnvMech.HeavyG, EnvMech.Wind, EnvMech.LightG }, new[] { "machine", "frame" }),
        ("HoistDrop", new[] { EnvMech.HeavyG, EnvMech.Quake }, new[] { "storage" }),
        ("PartitionFall", new[] { EnvMech.Quake, EnvMech.HeavyG }, new[] { "quarters", "storage" }),
        ("PlantTopple", new[] { EnvMech.Quake, EnvMech.HeavyG, EnvMech.LightG, EnvMech.Wind }, new[] { "crops" }),
        ("ShaftMisalign", new[] { EnvMech.Quake, EnvMech.HeavyG }, new[] { "machine", "fuel" }),
        ("TrunkSag", new[] { EnvMech.HeavyG, EnvMech.Heat }, new[] { "power" }),
        ("GravityHiccup", new[] { EnvMech.LightG, EnvMech.Magnetic, EnvMech.TimeWarp }, new[] { "crew", "frame" }),
        ("WreckDrift", new[] { EnvMech.LightG, EnvMech.Wind }, new[] { "storage" }),
        ("TidalPull", new[] { EnvMech.TimeWarp, EnvMech.Quake }, new[] { "frame" }),
        ("LightsOut", new[] { EnvMech.Dark, EnvMech.Static, EnvMech.Lightning }, new[] { "lights", "roomair", "quarters" }),
        ("NutrientCrash", new[] { EnvMech.Dark, EnvMech.Freeze, EnvMech.Heat }, new[] { "crops" }),
        ("SeedRot", new[] { EnvMech.Radiation, EnvMech.Flood, EnvMech.Bio, EnvMech.Dark }, new[] { "crops" }),
        ("CropBlight", new[] { EnvMech.Bio, EnvMech.Radiation, EnvMech.Dark }, new[] { "crops" }),
        ("MoldOutbreak", new[] { EnvMech.Flood, EnvMech.Bio }, new[] { "roomair", "quarters", "storage" }),
        ("WeevilSwarm", new[] { EnvMech.Bio }, new[] { "food", "storage" }),
        ("HumiditySpike", new[] { EnvMech.Flood, EnvMech.Heat }, new[] { "roomair", "medbay" }),
        ("Backflow", new[] { EnvMech.Flood, EnvMech.Quake }, new[] { "water", "pipe" }),
        ("SewageBackup", new[] { EnvMech.Flood, EnvMech.Quake }, new[] { "water" }),
        ("pipe", new[] { EnvMech.Freeze, EnvMech.Quake, EnvMech.Pressure, EnvMech.Heat }, new[] { "pipe", "water", "cooling" }),
        ("fire", new[] { EnvMech.Volcanic, EnvMech.Lightning, EnvMech.Static, EnvMech.Heat, EnvMech.Volatile }, new[] { "roomair", "machine", "storage", "galley", "quarters" }),
        ("WaterContamination", new[] { EnvMech.Bio, EnvMech.Toxic, EnvMech.Flood, EnvMech.Volcanic }, new[] { "water" }),
        ("ReactorTransient", new[] { EnvMech.Heat, EnvMech.Radiation, EnvMech.Quake }, new[] { "power" }),
        ("Smolder", new[] { EnvMech.Heat, EnvMech.Volcanic }, new[] { "storage", "roomair" }),
        ("DuctFire", new[] { EnvMech.Heat, EnvMech.Volcanic, EnvMech.Static }, new[] { "air" }),
        ("CableTrayFire", new[] { EnvMech.Lightning, EnvMech.Static, EnvMech.Heat }, new[] { "power" }),
        ("MedError", new[] { EnvMech.Magnetic, EnvMech.Radiation }, new[] { "medbay" }),
    };

    /// <summary>부류마다 기제를 가리지 않는 마지막 수단 (그 부류에서 흔한 고장).</summary>
    private static readonly Dictionary<string, string[]> Fallback = new()
    {
        ["water"] = new[] { "ValveSeize", "pipe" }, ["power"] = new[] { "BreakerCascade", "CellAging" }, ["air"] = new[] { "ScrubberSaturation", "FanImbalance" },
        ["cooling"] = new[] { "CoolantLoss", "ValveSeize" }, ["comms"] = new[] { "ComputerFault" }, ["computer"] = new[] { "RebootGlitch", "ComputerFault" },
        ["robots"] = new[] { "RobotMalfunction" }, ["crops"] = new[] { "NutrientCrash", "SeedRot" }, ["food"] = new[] { "FreezerFailure" }, ["fuel"] = new[] { "TankSludge", "ShaftMisalign" },
        ["sensors"] = new[] { "IonStorm" }, ["frame"] = new[] { "FrameCreak", "LooseMount" }, ["hull"] = new[] { "HullCrack", "WindowCrack" },
    };

    // ─────────────────────────────── 환경 읽기 ───────────────────────────────

    /// <summary>환경 종류 하나의 본보기 (시험 · 도감 — 그 환경에 있는 기제들).</summary>
    public static EnvProfile Sample(EnvKind k)
    {
        var p = new EnvProfile { Kind = k, Where = "본보기" };
        void M(EnvMech m, float v) => p.Add(m, v);
        switch (k)
        {
            case EnvKind.Frozen: p.TempC = -120f; M(EnvMech.Freeze, 0.9f); M(EnvMech.Wind, 0.4f); M(EnvMech.Dark, 0.3f); M(EnvMech.Isolation, 0.2f); M(EnvMech.Quake, 0.2f); break;
            case EnvKind.Molten: p.TempC = 380f; M(EnvMech.Heat, 0.95f); M(EnvMech.Volcanic, 0.8f); M(EnvMech.Toxic, 0.4f); M(EnvMech.Quake, 0.3f); break;
            case EnvKind.HighG: p.Gravity = 2.5f; M(EnvMech.HeavyG, 0.9f); M(EnvMech.Wind, 0.3f); M(EnvMech.Pressure, 0.3f); M(EnvMech.Isolation, 0.2f); break;
            case EnvKind.LowG: p.Gravity = 0.02f; M(EnvMech.LightG, 0.9f); M(EnvMech.Dust, 0.6f); M(EnvMech.Impact, 0.5f); M(EnvMech.Static, 0.4f); M(EnvMech.Radiation, 0.3f); break;
            case EnvKind.RadMoon: M(EnvMech.Radiation, 1f); M(EnvMech.Impact, 0.4f); M(EnvMech.Freeze, 0.3f); M(EnvMech.Static, 0.3f); M(EnvMech.Isolation, 0.2f); break;
            case EnvKind.GasGiant: p.Pressure = 2000f; M(EnvMech.Wind, 0.9f); M(EnvMech.Lightning, 0.9f); M(EnvMech.Pressure, 0.8f); M(EnvMech.Volatile, 0.6f); M(EnvMech.Magnetic, 0.5f); M(EnvMech.Corrode, 0.3f); break;
            case EnvKind.IceOcean: p.TempC = -170f; M(EnvMech.Freeze, 0.8f); M(EnvMech.Pressure, 0.7f); M(EnvMech.Flood, 0.7f); M(EnvMech.Quake, 0.5f); M(EnvMech.Radiation, 0.4f); M(EnvMech.Bio, 0.3f); break;
            case EnvKind.Venus: p.TempC = 460f; p.Pressure = 9000f; M(EnvMech.Corrode, 1f); M(EnvMech.Heat, 0.95f); M(EnvMech.Pressure, 0.9f); M(EnvMech.Toxic, 0.7f); M(EnvMech.Lightning, 0.4f); M(EnvMech.Dark, 0.4f); break;
            case EnvKind.Titan: p.TempC = -180f; M(EnvMech.Volatile, 0.9f); M(EnvMech.Freeze, 0.9f); M(EnvMech.Flood, 0.6f); M(EnvMech.Dark, 0.6f); M(EnvMech.Toxic, 0.4f); M(EnvMech.LightG, 0.4f); break;
            case EnvKind.Io: M(EnvMech.Volcanic, 1f); M(EnvMech.Radiation, 0.9f); M(EnvMech.Quake, 0.9f); M(EnvMech.Toxic, 0.5f); M(EnvMech.Dust, 0.5f); M(EnvMech.LightG, 0.4f); break;
            case EnvKind.TidalLock: M(EnvMech.Wind, 0.7f); M(EnvMech.Heat, 0.5f); M(EnvMech.Freeze, 0.5f); M(EnvMech.Dark, 0.4f); M(EnvMech.Isolation, 0.3f); break;
            case EnvKind.DeepDark: p.TempC = -240f; M(EnvMech.Dark, 1f); M(EnvMech.Isolation, 0.9f); M(EnvMech.Freeze, 0.6f); M(EnvMech.Radiation, 0.3f); break;
            case EnvKind.Pulsar: M(EnvMech.Radiation, 1f); M(EnvMech.Magnetic, 1f); M(EnvMech.Static, 0.5f); M(EnvMech.Heat, 0.3f); break;
            case EnvKind.BlackHole: M(EnvMech.TimeWarp, 1f); M(EnvMech.Radiation, 0.7f); M(EnvMech.Isolation, 0.8f); M(EnvMech.Quake, 0.5f); M(EnvMech.Heat, 0.3f); break;
            case EnvKind.Nebula: M(EnvMech.Static, 0.8f); M(EnvMech.Dust, 0.6f); M(EnvMech.Isolation, 0.6f); M(EnvMech.Dark, 0.4f); M(EnvMech.Radiation, 0.3f); break;
            case EnvKind.Comet: M(EnvMech.Impact, 0.9f); M(EnvMech.Dust, 0.7f); M(EnvMech.Volatile, 0.4f); M(EnvMech.Static, 0.4f); M(EnvMech.Freeze, 0.4f); M(EnvMech.LightG, 0.3f); break;
            case EnvKind.Spores: M(EnvMech.Bio, 1f); M(EnvMech.Toxic, 0.6f); M(EnvMech.Dust, 0.3f); M(EnvMech.Isolation, 0.2f); break;
        }
        return p;
    }

    /// <summary>내려앉은 행성의 환경 (지금 날씨까지).</summary>
    public static EnvProfile FromPlanet(Planet pl, PlanetWeather wx)
    {
        var p = new EnvProfile();
        void M(EnvMech m, float v) => p.Add(m, v);

            p.Where = pl.Name; p.TempC = pl.TempC; p.Gravity = pl.Gravity; p.Pressure = pl.Pressure; p.Weather = PlanetGen.WeatherName(pl, wx);
            if (pl.TempC < -20f) M(EnvMech.Freeze, (-20f - pl.TempC) / 120f);
            if (pl.TempC > 50f) M(EnvMech.Heat, (pl.TempC - 50f) / 300f);
            if (pl.Pressure > 500f) M(EnvMech.Pressure, 0.3f + (pl.Pressure - 500f) / 5000f);
            if (pl.Gravity > 1.3f) M(EnvMech.HeavyG, (pl.Gravity - 1.3f) / 1.2f);
            if (pl.Gravity < 0.3f) M(EnvMech.LightG, (0.3f - pl.Gravity) / 0.3f);
            if (pl.Pressure <= 0.5f) { M(EnvMech.Impact, 0.35f); M(EnvMech.Radiation, 0.25f); }
            foreach (var f in pl.Features)
                switch (f)
                {
                    case PlanetFeature.Quakes: M(EnvMech.Quake, 0.6f); break;
                    case PlanetFeature.Geysers: M(EnvMech.Flood, 0.2f); M(EnvMech.Quake, 0.1f); break;
                    case PlanetFeature.Vents: M(EnvMech.Volcanic, 0.6f); M(EnvMech.Toxic, 0.3f); break;
                    case PlanetFeature.MeteorRain: M(EnvMech.Impact, 0.6f); break;
                    case PlanetFeature.Tides: M(EnvMech.Flood, 0.4f); M(EnvMech.Quake, 0.2f); break;
                    case PlanetFeature.Sinkholes: M(EnvMech.Quake, 0.3f); break;
                    case PlanetFeature.Static: M(EnvMech.Static, 0.6f); break;
                    case PlanetFeature.NoMagnet: M(EnvMech.Radiation, 0.5f); break;
                    case PlanetFeature.StrongMagnet: M(EnvMech.Magnetic, 0.6f); break;
                    case PlanetFeature.TidalLock: M(EnvMech.Wind, 0.4f); M(EnvMech.Dark, 0.3f); break;
                    case PlanetFeature.ToxicDust: M(EnvMech.Toxic, 0.6f); M(EnvMech.Dust, 0.3f); break;
                    case PlanetFeature.CrystalField: M(EnvMech.Static, 0.2f); break;
                    case PlanetFeature.Rings: M(EnvMech.Impact, 0.2f); break;
                }
            switch (pl.Surface)
            {
                case PlanetSurface.Lava: M(EnvMech.Volcanic, 0.6f); M(EnvMech.Heat, 0.3f); break;
                case PlanetSurface.Ash: M(EnvMech.Dust, 0.5f); M(EnvMech.Volcanic, 0.3f); break;
                case PlanetSurface.Dunes: M(EnvMech.Dust, 0.4f); break;
                case PlanetSurface.Shallows: M(EnvMech.Flood, 0.4f); break;
                case PlanetSurface.Mud: M(EnvMech.Flood, 0.3f); break;
                case PlanetSurface.IceSheet: M(EnvMech.Freeze, 0.2f); M(EnvMech.Quake, 0.1f); break;
            }
            switch (wx)
            {
                case PlanetWeather.Wind: M(EnvMech.Wind, 0.4f); break;
                case PlanetWeather.DustStorm: M(EnvMech.Dust, 0.6f); M(EnvMech.Static, 0.3f); M(EnvMech.Wind, 0.3f); break;
                case PlanetWeather.Rain: M(EnvMech.Flood, 0.4f); break;
                case PlanetWeather.AcidRain: M(EnvMech.Corrode, 0.6f); break;
                case PlanetWeather.Thunder: M(EnvMech.Lightning, 0.7f); break;
                case PlanetWeather.Typhoon: M(EnvMech.Wind, 0.9f); M(EnvMech.Flood, 0.3f); break;
                case PlanetWeather.Blizzard: M(EnvMech.Freeze, 0.3f); M(EnvMech.Wind, 0.4f); M(EnvMech.Dark, 0.2f); break;
                case PlanetWeather.HeatWave: M(EnvMech.Heat, 0.3f); break;
            }
            switch (pl.Body)
            {
                case PlanetBody.GasGiant: M(EnvMech.Wind, 0.5f); M(EnvMech.Lightning, 0.4f); M(EnvMech.Volatile, 0.6f); M(EnvMech.Pressure, 0.3f); M(EnvMech.Corrode, 0.3f); break;
                case PlanetBody.IceMoon: M(EnvMech.Pressure, 0.5f); M(EnvMech.Flood, 0.5f); break;
                case PlanetBody.Venus: M(EnvMech.Corrode, 0.6f); M(EnvMech.Toxic, 0.4f); M(EnvMech.Dark, 0.4f); break;
                case PlanetBody.Titan: M(EnvMech.Volatile, 0.9f); M(EnvMech.Flood, 0.4f); M(EnvMech.Dark, 0.6f); break;
                case PlanetBody.Io: M(EnvMech.Radiation, 0.5f); M(EnvMech.Volcanic, 0.4f); M(EnvMech.Quake, 0.3f); break;
                case PlanetBody.Comet: M(EnvMech.Impact, 0.3f); M(EnvMech.Volatile, 0.4f); M(EnvMech.Dust, 0.4f); break;
                case PlanetBody.Asteroid: M(EnvMech.Dust, 0.5f); M(EnvMech.Impact, 0.3f); break;
                case PlanetBody.Rogue: M(EnvMech.Dark, 1f); M(EnvMech.Isolation, 0.6f); break;
            }
            p.Kind = pl.Body switch
            {
                PlanetBody.GasGiant => EnvKind.GasGiant, PlanetBody.IceMoon => EnvKind.IceOcean, PlanetBody.Venus => EnvKind.Venus, PlanetBody.Titan => EnvKind.Titan, PlanetBody.Io => EnvKind.Io,
                PlanetBody.Comet => EnvKind.Comet, PlanetBody.Asteroid => EnvKind.LowG, PlanetBody.Rogue => EnvKind.DeepDark,
                _ => pl.Has(PlanetFeature.TidalLock) ? EnvKind.TidalLock : pl.Gravity > 1.5f ? EnvKind.HighG : pl.Climate == PlanetClimate.Scorched || pl.Surface == PlanetSurface.Lava ? EnvKind.Molten
                    : pl.Climate == PlanetClimate.Frozen ? EnvKind.Frozen : pl.Gravity < 0.3f ? EnvKind.LowG : pl.Climate == PlanetClimate.Airless && pl.Has(PlanetFeature.NoMagnet) ? EnvKind.RadMoon
                    : pl.Climate == PlanetClimate.Hot ? EnvKind.Molten : EnvKind.None,
            };
            if (p.Kind != EnvKind.None) M(EnvMech.Isolation, 0.15f); // 극한의 땅에 머물면 갇힌 느낌이 든다
        
        return p;
    }

    /// <summary>지금 머무는 곳을 읽는다: 내려앉은 행성 → 우주 재난 → 항로 구간.</summary>
    public EnvProfile Read()
    {
        var w = _w;
        if (Force != null) return Force;
        var p = new EnvProfile();
        void M(EnvMech m, float v) => p.Add(m, v);
        if (w.Planet.Landed && w.Planet.Here is Planet pl)
        {
            p = FromPlanet(pl, w.Planet.Weather);
        }
        else
        {
            var leg = w.Voyage.Current.Kind;
            p.Where = VoyageSystem.KindName(leg);
            p.Kind = leg switch
            {
                LegKind.AsteroidBelt => EnvKind.LowG, LegKind.Nebula => EnvKind.Nebula, LegKind.RadiationBelt or LegKind.SolarWind => EnvKind.RadMoon, LegKind.CometTrail or LegKind.IceRing => EnvKind.Comet,
                LegKind.GasGiant => EnvKind.GasGiant, LegKind.Pulsar or LegKind.MagneticField => EnvKind.Pulsar, LegKind.DeepVoid => EnvKind.DeepDark, LegKind.Perihelion => EnvKind.Molten,
                LegKind.SporeCloud => EnvKind.Spores, _ => EnvKind.None,
            };
            foreach (var e in w.Cosmic.Active)
            {
                var k = e.Kind switch
                {
                    CosmicKind.DarkNebula or CosmicKind.IonNebula or CosmicKind.HyperDust => EnvKind.Nebula, CosmicKind.PulsarBeam or CosmicKind.MagnetarStorm or CosmicKind.NeutronStar or CosmicKind.BinaryEclipse => EnvKind.Pulsar,
                    CosmicKind.BlackHoleTide => EnvKind.BlackHole, CosmicKind.RoguePlanet => EnvKind.DeepDark, CosmicKind.CometCore => EnvKind.Comet, _ => EnvKind.None,
                };
                if (k != EnvKind.None) { p.Kind = k; p.Where = e.Spec.Name; }
            }
            if (p.Kind != EnvKind.None)
            {
                var s = Sample(p.Kind);
                for (int i = 0; i < EnvProfile.Mechs; i++) p.Mech[i] = s.Mech[i] * 0.85f; // 우주에서는 배 바깥이 대신 받는다
                p.TempC = s.TempC; p.Pressure = 0f; p.Gravity = 1f;
            }
        }
        if (w.Tick < IntenseUntil) for (int i = 0; i < EnvProfile.Mechs; i++) p.Mech[i] = MathF.Min(1f, p.Mech[i] * 1.3f);
        if (p.Kind != EnvKind.None && p.Active.Count() < 3) M(EnvMech.Isolation, ActiveAt); // 극한 환경이면 갇힌 느낌 · 적어도 셋
        return p;
    }

    // ─────────────────────────────── 사고표 ───────────────────────────────

    public static IEnumerable<EnvTarget> TargetsOf(IncidentScale s) => Targets.Where(t => t.Scale == s);

    public static string NameOf(EnvMech m, EnvTarget t)
    {
        if (Named.TryGetValue((m, t.Key), out var n)) return n;
        return t.Scale switch
        {
            IncidentScale.Personal => t.Class switch
            {
                "robot" or "drone" or "suit" => $"{t.Name} {Noun[(int)m]}",
                "mind" => $"{MechName(m)} 공포 — {t.Name}이 흔들린다",
                _ => $"{t.Name} {Hurt[(int)m]}",
            },
            IncidentScale.Cosmic => $"{Great[(int)m]} — {t.Name}까지 덮친다",
            _ => $"{t.Name} {Noun[(int)m]}",
        };
    }

    /// <summary>이 환경에서 날 수 있는 사고 (규모마다).</summary>
    public static List<EnvCombo> Catalog(EnvProfile p, IncidentScale s) =>
        p.Active.SelectMany(m => TargetsOf(s).Select(t => new EnvCombo(m, t, NameOf(m, t)))).ToList();

    private static IEnumerable<string> LinksFor(EnvMech m, string cls) =>
        Links.Where(l => Array.IndexOf(l.mechs, m) >= 0 && Array.IndexOf(l.cls, cls) >= 0).Select(l => l.key);

    // ─────────────────────────────── 시간 ───────────────────────────────

    private float RateMul => Off || Storyteller.Persona == StoryPersona.Off && HazardSystem.RandomDays <= 0f && Force == null ? 0f : 0.55f + 0.12f * Storyteller.Level;

    public void Update(float dt)
    {
        var w = _w;
        if (Off) return;
        if (w.Tick >= _nextCheck)
        {
            _nextCheck = w.Tick + SimTime.Minutes(5);
            foreach (var c in Pending.Where(c => w.Tick >= c.StrikeAt).ToList()) { Pending.Remove(c); Strike(c); }
        }
        if (w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        Now = Read();
        float mul = RateMul;
        if (Now.Kind == EnvKind.None || mul <= 0f || Pending.Count >= 2) return;
        if (w.Commander.OpenCases.Count() >= 2) return; // 이미 큰일을 치르는 중
        if (!_rng.Chance(MathF.Min(0.6f, Now.Severity * 0.16f * mul))) return;
        float x = _rng.Float();
        var s = x < 0.42f ? IncidentScale.Personal : x < 0.74f ? IncidentScale.Room : x < 0.9f ? IncidentScale.System : x < 0.97f ? IncidentScale.Ship : IncidentScale.Cosmic;
        if (s == IncidentScale.Cosmic && w.Tick < _cosmicAfter) s = IncidentScale.Ship;
        if (s == IncidentScale.Ship && w.Tick < _shipAfter) s = IncidentScale.System;
        Begin(s, null, null);
    }

    /// <summary>사고 하나를 고른다 (기제 · 대상을 주면 그것으로). 개인은 곧바로, 나머지는 낌새부터.</summary>
    public EnvCase? Begin(IncidentScale s, EnvMech? mech, string? target, bool now = false)
    {
        var w = _w;
        var prof = Now.Kind == EnvKind.None || Force != null ? (Now = Read()) : Now;
        var pool = Catalog(prof, s).Where(c => (mech == null || c.Mech == mech) && (target == null || c.Target.Key == target)).ToList();
        if (pool.Count == 0) return null;
        float total = pool.Sum(c => prof[c.Mech]);
        float roll = _rng.Float() * total;
        var pick = pool[^1];
        foreach (var c in pool) { roll -= prof[c.Mech]; if (roll <= 0f) { pick = c; break; } }
        var room = RoomFor(pick.Target.Class, pick.Target.Scale);
        float sev = Math.Clamp(prof[pick.Mech] * _rng.Range(0.6f, 1.1f), 0.2f, 1f) * (1f - MathF.Min(0.35f, 0.04f * Seen[(int)pick.Mech]));
        int lead = s switch { IncidentScale.Personal => 0, IncidentScale.Room => _rng.Range(15, 40), IncidentScale.System => _rng.Range(25, 60), IncidentScale.Ship => _rng.Range(60, 180), _ => _rng.Range(180, 360) };
        var k = new EnvCase { Id = ++_nextId, Mech = pick.Mech, Target = pick.Target.Key, Scale = s, Name = pick.Name, RoomId = room?.Id ?? -1, StrikeAt = w.Tick + (now ? 0 : SimTime.Minutes(lead)), Sev = sev };
        if (s == IncidentScale.Cosmic) _cosmicAfter = w.Tick + SimTime.Hours(48);
        if (s == IncidentScale.Ship) _shipAfter = w.Tick + SimTime.Hours(12);
        if (lead == 0 || now) { Strike(k); return k; }
        Watch(k, room);
        Pending.Add(k);
        return k;
    }

    /// <summary>낌새: 주컴퓨터가 알아채면 알리고 로봇을 보낸다 (큰 것은 모두 대비 — 덜 다친다).</summary>
    private void Watch(EnvCase k, Room? room)
    {
        var w = _w;
        float see = (w.Automation.MainOnline ? 0.55f : 0.2f) + (EyeNet.On ? 0.15f : 0f) + MathF.Min(0.15f, 0.02f * Seen[(int)k.Mech]);
        if (!_rng.Chance(see)) return;
        k.Detected = true;
        Stats.Omens[(int)k.Scale]++;
        string where = room != null ? $"{room.Name} — " : "";
        long mins = (k.StrikeAt - w.Tick) / SimTime.Minutes(1);
        w.Automation.Reason($"env:{k.Id}", $"낌새: {where}{Omen[(int)k.Mech]} ({MechName(k.Mech)}) — {mins}분 안에 '{k.Name}' 우려", SimTime.Minutes(5));
        if (k.Scale >= IncidentScale.Ship) { k.Sev *= 0.7f; w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {k.Name} 대비 — 바깥 작업을 멈추고 몸을 고정하세요"); return; }
        if (room != null && w.Robots.SendCheck(room, $"낌새 살피기 — {Omen[(int)k.Mech]}", out var bot)) k.BotId = bot.Id;
    }

    private void Strike(EnvCase k)
    {
        var w = _w;
        var room = k.RoomId >= 0 ? w.Ship.Rooms.FirstOrDefault(r => r.Id == k.RoomId) : null;
        if (k.BotId >= 0 && w.Robots.Robots.FirstOrDefault(r => r.Id == k.BotId) is Robot bot && room != null && bot.Room == room && bot.Operational
            && _rng.Chance(k.Scale == IncidentScale.Room ? 0.65f : 0.4f))
        {
            Stats.Prevented[(int)k.Scale]++;
            string msg = $"{Ko.IGa(bot.Name)} 미리 손써 막았다 — {k.Name} ({room.Name})";
            bot.Mind.Say($"{k.Name} — 미리 막았다", w.Tick);
            MarkLog.Add(room.Marks, w.Tick, msg);
            w.Log.Add(w.Tick, LogKind.Work, msg);
            Remember(k, msg, true);
            return;
        }
        if (k.BotId >= 0) k.Sev *= 0.7f; // 가는 길이었으면 조금은 덜하다
        string text = Apply(k, room);
        Stats.Fired[(int)k.Scale]++;
        Stats.ByMech[(int)k.Mech]++;
        if (++Seen[(int)k.Mech] == 5) { Stats.Adapted++; w.Log.Add(w.Tick, LogKind.Life, $"다들 {MechName(k.Mech)}에 익숙해졌다 — 대비가 몸에 붙었다 (덜 다친다)"); }
        Remember(k, text, false);
    }

    private void Remember(EnvCase k, string text, bool prevented)
    {
        Recent.Add((_w.Tick, k.Scale, text, prevented));
        if (Recent.Count > 30) Recent.RemoveAt(0);
    }

    // ─────────────────────────────── 대상 고르기 ───────────────────────────────

    private Room? RoomFor(string cls, IncidentScale s)
    {
        var w = _w;
        var ship = w.Ship;
        var rooms = ship.Rooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor).ToList();
        if (rooms.Count == 0) return null;
        RoomType[] want = cls switch
        {
            "airlock" => new[] { RoomType.Airlock, RoomType.EvaPrep, RoomType.DockingBay },
            "galley" or "food" => new[] { RoomType.Galley, RoomType.Freezer, RoomType.Mess },
            "crops" => new[] { RoomType.Hydroponics, RoomType.Garden, RoomType.AlgaeLab, RoomType.MushroomFarm, RoomType.ProteinFarm },
            "quarters" => new[] { RoomType.Quarters, RoomType.QuietQuarters, RoomType.PrivateCabins },
            "medbay" => new[] { RoomType.Medbay, RoomType.Triage },
            "storage" => new[] { RoomType.Storage, RoomType.Cargo },
            "water" or "pipe" => new[] { RoomType.WaterPlant, RoomType.LifeSupport, RoomType.PumpRoom, RoomType.Galley },
            "power" => new[] { RoomType.Power, RoomType.Substation, RoomType.BatteryRoom, RoomType.Reactor },
            "air" => new[] { RoomType.LifeSupport, RoomType.HvacRoom },
            "cooling" => new[] { RoomType.Cooling, RoomType.Reactor },
            "comms" or "sensors" => new[] { RoomType.Comms, RoomType.Bridge, RoomType.Navigation },
            "computer" => new[] { RoomType.ComputerRoom, RoomType.ServerRoom, RoomType.Bridge },
            "robots" => new[] { RoomType.RobotBay, RoomType.Workshop },
            "fuel" => new[] { RoomType.PropellantTank, RoomType.FuelCell, RoomType.Engine },
            "frame" => new[] { RoomType.Engine, RoomType.Cargo, RoomType.Workshop },
            _ => Array.Empty<RoomType>(),
        };
        foreach (var t in want)
        {
            var hit = rooms.Where(r => r.Kind == t || r.Type == t).ToList();
            if (hit.Count > 0) return hit[_rng.Range(0, hit.Count)];
        }
        if (cls is "window" or "hull" or "airlock")
        {
            var hull = rooms.Where(r => ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r)).ToList();
            if (hull.Count > 0) return hull[_rng.Range(0, hull.Count)];
        }
        if (cls == "machine") return rooms.OrderByDescending(r => r.Furniture.Count(f => f.Machine != null)).ThenBy(r => r.Id).First();
        if (s == IncidentScale.Personal) return null;
        var busy = rooms.Where(r => w.Crew.Any(c => !c.Dead && c.Room == r)).ToList();
        return busy.Count > 0 ? busy[_rng.Range(0, busy.Count)] : rooms[_rng.Range(0, rooms.Count)];
    }

    private CrewMember? CrewFor(string key)
    {
        var w = _w;
        var aboard = w.Crew.Where(c => !c.Dead && !c.Away && !c.LeftShip).ToList();
        if (aboard.Count == 0) return null;
        List<CrewMember> pool = key switch
        {
            "outside" or "suit" => aboard.Where(c => c.Outside).ToList() is { Count: > 0 } o ? o : aboard.Where(c => c.Room?.Kind is RoomType.Airlock or RoomType.EvaPrep || c.Pose == Pose.Working).ToList(),
            "worker" or "hand" => aboard.Where(c => c.Pose == Pose.Working).ToList(),
            "sleeper" => aboard.Where(c => c.Pose == Pose.Sleeping).ToList(),
            "elder" => aboard.OrderByDescending(c => c.IsChild ? 100f : c.Age).Take(2).ToList(),
            _ => aboard.Where(c => c.IsAwake).ToList(),
        };
        if (pool.Count == 0) pool = aboard;
        return pool[_rng.Range(0, pool.Count)];
    }

    // ─────────────────────────────── 피해 ───────────────────────────────

    private string Apply(EnvCase k, Room? room)
    {
        var w = _w;
        var t = Targets.First(x => x.Key == k.Target);
        var m = k.Mech;
        float sev = k.Sev;
        var hits = new List<string>();
        switch (k.Scale)
        {
            case IncidentScale.Personal:
                if (t.Class == "robot") HurtRobot(m, sev, hits);
                else if (t.Class == "drone") HurtDrone(m, sev, hits);
                else if (CrewFor(t.Key) is CrewMember c) HurtCrew(c, m, sev * (t.Class == "mind" ? 0.6f : 1f), t.Class == "mind", hits);
                break;
            case IncidentScale.Room:
                Link(m, t.Class, room, 1, hits);
                RoomDirect(m, room, sev, hits);
                break;
            case IncidentScale.System:
                Link(m, t.Class, room, sev > 0.6f ? 2 : 1, hits);
                if (hits.Count == 0) Link(null, t.Class, room, 1, hits);
                SystemDirect(m, t.Class, sev, hits);
                break;
            case IncidentScale.Ship:
            case IncidentScale.Cosmic:
            {
                bool cosmic = k.Scale == IncidentScale.Cosmic;
                Link(m, t.Class, room, cosmic ? 2 : 1, hits);
                foreach (var cls in new[] { "hull", "power", "air", "crew", "frame", "computer", "water" }.Where(x => x != t.Class).OrderBy(_ => _rng.Float()).Take(cosmic ? 2 : 1))
                    Link(m, cls, RoomFor(cls, k.Scale), 1, hits);
                ShipDirect(m, sev * (cosmic ? 1.4f : 1f), hits);
                if (t.Class is "crew" or "mind") foreach (var c in w.Crew.Where(c => !c.Dead && !c.Away && !c.LeftShip).OrderBy(_ => _rng.Float()).Take(cosmic ? 3 : 2)) HurtCrew(c, m, sev * 0.6f, t.Class == "mind", hits);
                if (cosmic)
                {
                    IntenseUntil = w.Tick + SimTime.Hours(24);
                    if (w.Planet.Landed && w.Planet.Phase == PlanetPhase.Landed && w.Planet.Here != null && sev > 0.5f && !w.Crew.Any(c => !c.Dead && (c.Outside || c.Away)))
                    {
                        Stats.EarlyLifts++;
                        w.Automation.Reason("env:lift", $"{Great[(int)m]} — 이 땅에 더 머물 수 없다, 일찍 뜬다", SimTime.Hours(6));
                        w.Planet.Lift("일찍");
                        hits.Add("일찍 떠남");
                    }
                }
                break;
            }
        }
        if (k.Scale >= IncidentScale.Room && !hits.Any(h => !h.Contains("°C") && !h.Contains("마음"))) Link(null, t.Class, room, 1, hits); // 눈에 보이는 피해가 없으면 그 부류의 흔한 고장으로
        string where = room != null && k.Scale is IncidentScale.Room or IncidentScale.System ? $" ({room.Name})" : "";
        string scaleName = k.Scale switch { IncidentScale.Personal => "개인", IncidentScale.Room => "방", IncidentScale.System => "계통", IncidentScale.Ship => "배 전체", _ => "우주급" };
        string text = $"[{Now.Name} · {scaleName}] {k.Name}{where}" + (hits.Count > 0 ? " — " + string.Join(" · ", hits.Distinct().Take(4)) : "");
        w.Log.Add(w.Tick, k.Scale >= IncidentScale.System ? LogKind.Warning : LogKind.Ship, text);
        if (k.Scale >= IncidentScale.Room)
        {
            w.History.Add(w, HistoryKind.Incident, text, room);
            if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{k.Name} ({MechName(m)})");
        }
        if (k.Scale >= IncidentScale.Ship) w.RaiseAlert(k.Name, room, AlertLevel.Warning, shipWide: true);
        return text;
    }

    /// <summary>이어지는 기존 사고를 건다 (그 방에 · 없으면 배 어디든).</summary>
    private void Link(EnvMech? m, string cls, Room? room, int n, List<string> hits)
    {
        var w = _w;
        var keys = (m is EnvMech mm ? LinksFor(mm, cls) : Fallback.TryGetValue(cls, out var fb) ? fb : Enumerable.Empty<string>()).OrderBy(_ => _rng.Float()).ToList();
        foreach (var key in keys)
        {
            if (n <= 0) break;
            string? what;
            try { what = w.Hazards.FireStory(key, room) ?? (room != null ? w.Hazards.FireStory(key, null) : null); } // 그 방에 맞는 게 없으면 배 어디든
            catch (InvalidOperationException) { what = null; }
            if (what == null) continue;
            hits.Add(what);
            Stats.Hazards++;
            n--;
        }
    }

    private static string Cause(EnvMech m) => Hurt[(int)m];

    private void HurtCrew(CrewMember c, EnvMech m, float sev, bool mindOnly, List<string> hits)
    {
        var w = _w;
        if (mindOnly || m is EnvMech.Isolation or EnvMech.TimeWarp or EnvMech.Dark && _rng.Chance(0.6f))
        {
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.2f * sev + 0.05f);
            if (m is EnvMech.Isolation or EnvMech.TimeWarp) w.Homeland.Bump(c, 0.06f * sev);
            if (sev > 0.7f && m is EnvMech.Isolation or EnvMech.Dark) w.Ailments.Catch(c, "insomnia", null, MechName(m));
            w.Brain2.Emotions.Feel(c, Feeling.Fear, 0.3f * sev, $"{MechName(m)}", null);
            Life.Diary(w, c, m switch { EnvMech.Dark => "빛이 없다. 숨이 막힌다.", EnvMech.Isolation => "아무 소리도 안 들린다. 내 목소리가 낯설다.", EnvMech.TimeWarp => "시계가 맞지 않는다. 고향은 지금 몇 년일까.", _ => $"{MechName(m)} 때문에 잠을 못 이뤘다." });
            hits.Add($"{c.Name} 마음이 흔들렸다");
            return;
        }
        float inj = 0.12f + 0.3f * sev;
        string? ail = m switch
        {
            EnvMech.Freeze => "hypothermia", EnvMech.Heat => sev > 0.5f ? "heatstroke" : "dehydration", EnvMech.Corrode or EnvMech.Toxic or EnvMech.Volcanic => "chemburn",
            EnvMech.Pressure => "bends", EnvMech.Volatile => "cobrain", EnvMech.Bio => "moldlung", EnvMech.Radiation => null, _ => null,
        };
        if (m == EnvMech.Radiation) { c.Dose += 0.15f + 0.45f * sev; inj = 0.05f; }
        c.Vitals.Injury = MathF.Max(c.Vitals.Injury, inj);
        c.Vitals.InjuryCause = Cause(m);
        c.Vitals.Health = MathF.Max(0.15f, c.Vitals.Health - 0.15f * sev);
        if (ail != null) w.Ailments.Catch(c, ail, null, MechName(m));
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.1f);
        c.Interrupt(w);
        Stats.Injured++;
        hits.Add($"{c.Name} {Cause(m)}");
    }

    private void HurtRobot(EnvMech m, float sev, List<string> hits)
    {
        var w = _w;
        var bots = w.Robots.Robots.Where(r => !r.Wrecked && r.State != RobotState.Lost && r.Fault == null).ToList();
        if (bots.Count == 0) return;
        var r = bots[_rng.Range(0, bots.Count)];
        var part = m switch
        {
            EnvMech.Freeze or EnvMech.Dust or EnvMech.Quake or EnvMech.Wind or EnvMech.LightG or EnvMech.HeavyG or EnvMech.Flood => BotPart.Drive,
            EnvMech.Heat or EnvMech.Lightning or EnvMech.Volcanic => BotPart.Power,
            EnvMech.Radiation or EnvMech.Magnetic or EnvMech.Static or EnvMech.TimeWarp => BotPart.Brain,
            EnvMech.Corrode or EnvMech.Toxic or EnvMech.Volatile or EnvMech.Bio => RobotBody.HasArms(r.Kind) ? BotPart.ArmL : BotPart.Frame,
            EnvMech.Dark or EnvMech.Isolation => BotPart.Sensor,
            _ => BotPart.Frame,
        };
        w.Robots.Hurt(r, part, 0.25f + 0.45f * sev, MechName(m));
        hits.Add($"{r.Name} {RobotBody.Name(r.Kind, part)}");
    }

    private void HurtDrone(EnvMech m, float sev, List<string> hits)
    {
        var w = _w;
        var ds = w.Drones.Drones.Where(d => d.State != DroneState.Lost).ToList();
        if (ds.Count == 0) { HurtRobot(m, sev, hits); return; }
        var d = ds[_rng.Range(0, ds.Count)];
        w.Drones.HitDrone(d, 0.3f + 0.5f * sev, d.Position, MechName(m));
        hits.Add($"{d.Name} 맞았다");
    }

    private void RoomDirect(EnvMech m, Room? room, float sev, List<string> hits)
    {
        var w = _w;
        if (room == null) return;
        switch (m)
        {
            case EnvMech.Freeze: room.Air.Temperature -= 10f + 10f * sev; hits.Add($"{room.Name} {room.Air.Temperature:0}°C"); break;
            case EnvMech.Heat or EnvMech.Volcanic: room.Air.Temperature += 10f + 12f * sev; hits.Add($"{room.Name} {room.Air.Temperature:0}°C"); break;
            case EnvMech.Toxic or EnvMech.Volatile: room.Air.Toxin = MathF.Min(1f, room.Air.Toxin + 0.2f + 0.25f * sev); hits.Add($"{room.Name} 독한 공기"); break;
            case EnvMech.Dust: room.Air.Smoke = MathF.Min(1f, room.Air.Smoke + 0.15f * sev); hits.Add($"{room.Name} 먼지"); break;
            case EnvMech.Radiation: foreach (var c in _w.Crew.Where(c => !c.Dead && c.Room == room)) c.Dose += 0.1f * sev; break;
            case EnvMech.Isolation or EnvMech.TimeWarp or EnvMech.Dark or EnvMech.Magnetic: foreach (var c in _w.Crew.Where(c => !c.Dead && c.Room == room)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.12f * sev); break;
        }
        if (hits.Count == 0) // 이어질 사고가 없으면 그 방 설비 하나가 탈 난다
        {
            var mach = room.Furniture.Where(f => f.Machine != null && !f.Stowed).Select(f => f.Machine!).ToList();
            if (mach.Count > 0 && w.Machines.Break(mach[_rng.Range(0, mach.Count)]) is Fault f) hits.Add($"{room.Name} 설비 고장");
        }
    }

    private void SystemDirect(EnvMech m, string cls, float sev, List<string> hits)
    {
        var w = _w;
        switch (cls)
        {
            case "water" when m is EnvMech.Freeze or EnvMech.Heat or EnvMech.Flood or EnvMech.Bio or EnvMech.Toxic:
                float lost = MathF.Min(w.Water.Level, 15f + 40f * sev);
                w.Water.Level -= lost;
                hits.Add($"물 {lost:0}L 잃음");
                break;
            case "food" when m is EnvMech.Heat or EnvMech.Bio or EnvMech.Radiation:
                hits.Add("식량 일부를 버렸다");
                for (int i = 0; i < 1 + (int)(4 * sev); i++) ItemsV15.Use(w, ItemKind.Meal);
                break;
        }
    }

    private void ShipDirect(EnvMech m, float sev, List<string> hits)
    {
        var w = _w;
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.Away && !c.LeftShip)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.06f * sev);
        switch (m)
        {
            case EnvMech.Freeze: foreach (var r in w.Ship.Rooms.Where(r => !r.Detached)) r.Air.Temperature -= 4f * sev; hits.Add("배 안이 식는다"); break;
            case EnvMech.Heat or EnvMech.Volcanic: foreach (var r in w.Ship.Rooms.Where(r => !r.Detached)) r.Air.Temperature += 4f * sev; hits.Add("배 안이 달아오른다"); break;
            case EnvMech.Radiation: foreach (var c in w.Crew.Where(c => !c.Dead && !c.Away)) c.Dose += 0.08f * sev; hits.Add("모두 피폭"); break;
            case EnvMech.TimeWarp or EnvMech.Isolation: foreach (var c in w.Crew.Where(c => !c.Dead && !c.Away && !c.BornAboard)) w.Homeland.Bump(c, 0.05f * sev); hits.Add("고향이 멀어졌다"); break;
        }
    }

    // ─────────────────────────────── 보이기 ───────────────────────────────

    public string? Line() => Off || Now.Kind == EnvKind.None ? null : $"환경: {Now.Line()}" + (Pending.Count > 0 ? $" · 낌새 {Pending.Count}" : "");

    public void Hash(Action<long> I, Action<float> F)
    {
        I((long)Now.Kind); I(Stats.Fired.Sum()); I(Stats.Prevented.Sum()); I(Pending.Count);
    }
}

public sealed partial class RobotSystem
{
    /// <summary>v23 환경 사고 낌새: 쉬는 로봇 하나를 그 방으로 보내 살피게 한다.</summary>
    internal bool SendCheck(Room room, string what, out Robot bot)
    {
        bot = null!;
        var w = _world;
        var pick = Robots.Where(r => Free(r) && r.Kind is not (RobotKind.Companion or RobotKind.Stretcher)).OrderBy(r => Far(r.Position, room.Center)).ThenBy(r => r.Id).FirstOrDefault();
        if (pick == null) return false;
        var spot = room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - room.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell at) return false;
        var steps = new List<RobotStep> { new RGoto(at), new RWork(0.4f, null, room.Center) };
        Begin(pick, steps, $"{room.Name} {what}");
        FleetSystem.Stage(pick, steps, null);
        bot = pick;
        return true;
    }
}
