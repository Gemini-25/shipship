using System;
using System.Linq;

namespace ShipSim.Core;

// v18.13 우주 규모 대재난 30: 별 · 천체 충돌 · 인공 · 이상 현상.
// 표 한 줄 = 이름 · 갈래 · 예보 길이 · 무엇으로 보나 · 단계별 효과 · 피할 수 있나 · 연출 색/모양 · 후유증 · 대비 요점.
// 효과는 이미 있는 것을 쓴다 (방 방사선 · 벽 손상 · 설비 고장 · 로봇 · 드론 · 문 · 조명 · 화재 · 온도 · 항로 · 센서).

public enum CosmicGroup { Star, Body, Artificial, Anomaly }

public enum CosmicKind
{
    // 별 11
    Supernova, GammaBurst, SuperFlare, CoronalMass, PulsarBeam, MagnetarStorm, NeutronStar, BlackHoleTide, RedGiantShell, BinaryEclipse, WolfRayetWind,
    // 천체 충돌 7
    BigAsteroid, CometCore, PlanetRing, ShatteredPlanet, Kessler, HyperDust, RoguePlanet,
    // 인공 8
    ReactorBlast, StationCollapse, AntimatterBreach, FusionRunaway, MineField, OrbitalEmp, Freighter, PirateFleet,
    // 이상 현상 4
    DarkNebula, CosmicRayShower, IonNebula, GravityWave,
}

/// <summary>한 단계가 하는 일 (여럿 겹친다).</summary>
[Flags]
public enum CosmicFx
{
    None = 0,
    Light = 1,       // 눈부신 섬광: 센서가 잠깐 멀고, 창가 사람이 놀란다
    Radiation = 2,   // 방사선: 방 방사선 → 피폭 → 방사선 병 · 작물
    Heat = 4,        // 열: 바깥 쪽 방이 달아오르고 설비가 뜨거워진다
    Cold = 8,        // 급랭: 바깥 쪽 방이 식고 관이 언다
    Shock = 16,      // 충격파: 외벽 · 넘어짐 · 물건 낙하 · 불씨
    Emp = 32,        // 전자기 펄스: 전자 장비 · 컴퓨터 · 로봇 · 드론 · 문 구동기 · 조명
    Debris = 64,     // 잔해: 운석이 날아든다 (센서 · 회피 기동이 그대로 작동)
    Blind = 128,     // 센서 먹통
    Nav = 256,       // 항법 먹통: 항로가 밀린다
    Tidal = 512,     // 조석 응력: 골조가 늘어나고 기계가 닳는다
    Quake = 1024,    // 진동: 넘어짐 · 마운트 균열 · 잠을 설침
    Strike = 2048,   // 거대한 것 하나가 부딪힌다 (피하거나, 구획을 버린다)
    Plasma = 4096,   // 플라스마 · 먼지 침식: 바깥 설비 · 창 · 외판
    Hostile = 8192,  // 겨냥한 타격 (기뢰 · 해적)
}

/// <summary>단계 하나: 본 사건(도착)으로부터 At시간 뒤, Hours 동안.</summary>
public sealed record CosmicStage(float At, float Hours, CosmicFx Fx, float Power, string Text, bool CloseOnly = false);

/// <summary>지나간 뒤 하늘에 남는 것.</summary>
public enum CosmicRemnant { None, Nebula, GlowCloud, Ring, Streak, Wreck, Bubble, Scar }

public sealed record CosmicSpec(
    CosmicKind Kind, string Id, string Name, CosmicGroup Group,
    float LeadMin, float LeadMax, string Detect,
    CosmicStage[] Stages,
    bool Avoid, float AvoidFuel,
    string Hex, string Hex2, string Shape,
    CosmicRemnant Remnant, string After, string Brace, float Weight)
{
    public CosmicFx AllFx => Stages.Aggregate(CosmicFx.None, (a, s) => a | s.Fx);
    public bool Has(CosmicFx f) => (AllFx & f) != 0;
    public float Span => Stages.Max(s => s.At + s.Hours);
    /// <summary>v16.18 사고 다섯 규모 (개인 · 방 · 계통 · 배 · 우주급) — 이 30종은 모두 우주급.</summary>
    public string Scale => CosmicCatalog.Scale;
}

public static class CosmicCatalog
{
    /// <summary>사고 규모 표시 (v16.18에서 다섯 규모로 묶는다): 이 목록은 모두 우주급.</summary>
    public const string Scale = "우주급";
    public const string ScaleId = "cosmic";

    private static CosmicStage S(float at, float h, CosmicFx fx, float p, string text, bool close = false) => new(at, h, fx, p, text, close);
    private const CosmicFx L = CosmicFx.Light, R = CosmicFx.Radiation, H = CosmicFx.Heat, C = CosmicFx.Cold, K = CosmicFx.Shock, E = CosmicFx.Emp,
        D = CosmicFx.Debris, B = CosmicFx.Blind, N = CosmicFx.Nav, T = CosmicFx.Tidal, Q = CosmicFx.Quake, X = CosmicFx.Strike, P = CosmicFx.Plasma, A = CosmicFx.Hostile;

    public static readonly CosmicSpec[] All =
    {
        // ─── 별 ───
        new(CosmicKind.Supernova, "supernova", "초신성 폭발", CosmicGroup.Star, 30f, 60f, "천문 관측 — 무너지는 별의 중성미자",
            new[] { S(0f, 1f, L | B, 1f, "하늘이 하얗게 탔다"), S(18f, 12f, R, 1.2f, "방사선 파도가 닿았다"), S(30f, 0.6f, K | H, 0.7f, "충격파 앞머리가 배를 때렸다", close: true) },
            false, 0f, "#fff6e0", "#ff7ad0", "하얀 섬광 → 퍼지는 고리 → 분홍·하늘빛 성운",
            CosmicRemnant.Nebula, "하늘에 새 성운 · 방사선 병 · 삭은 회로", "대피소 · 물벽 · 물자 옮기기 · 선외 작업 중단", 0.5f),
        new(CosmicKind.GammaBurst, "gammaburst", "감마선 폭발", CosmicGroup.Star, 2f, 8f, "중력계 — 먼 별의 붕괴 떨림",
            new[] { S(0f, 0.5f, R | E | L, 4f, "보랏빛 줄기가 배를 꿰뚫었다"), S(0.5f, 3f, R, 0.3f, "잔광이 남았다") },
            false, 0f, "#b48cff", "#ffffff", "보랏빛 줄기 · 잔광 꼬리",
            CosmicRemnant.Streak, "잔광 줄기 · 방사선 병 · 전자 장비 고장", "대피소 · 민감 장비 끄기", 0.35f),
        new(CosmicKind.SuperFlare, "superflare", "초대형 항성 플레어", CosmicGroup.Star, 6f, 16f, "태양 관측 — 흑점이 꼬인다",
            new[] { S(0f, 0.6f, L | H, 0.7f, "주황빛 고리가 솟구쳤다"), S(0.6f, 6f, R | H, 0.65f, "양성자 비가 쏟아진다") },
            false, 0f, "#ffb347", "#ff5a1f", "주황 플레어 고리 · 솟구치는 플라스마 아치",
            CosmicRemnant.Scar, "그을린 외판 · 방사선", "대피소 · 물벽 · 선외 작업 중단", 1.1f),
        new(CosmicKind.CoronalMass, "cme", "코로나 질량 방출", CosmicGroup.Star, 12f, 36f, "태양 관측 — 부풀어 오르는 코로나",
            new[] { S(0f, 8f, R | E | B, 0.45f, "플라스마 구름이 덮쳤다 — 오로라가 배를 감쌌다") },
            false, 0f, "#ff9a3c", "#6cf0a0", "부풀어 오는 주황 구름 · 초록·보라 오로라",
            CosmicRemnant.None, "센서 교정 틀어짐 · 전자 장비 튐", "민감 장비 끄기 · 선외 작업 중단", 1.2f),
        new(CosmicKind.PulsarBeam, "pulsar", "펄서 빔 통과", CosmicGroup.Star, 8f, 24f, "전파 관측 — 주기를 계산했다",
            new[] { S(0f, 3f, R | B | E, 0.5f, "펄서 빔이 배를 쓸고 지나간다 — 째깍째깍") },
            true, 0.6f, "#7fd1ff", "#e8f7ff", "돌아가는 두 줄기 등대 빔 · 째깍이는 섬광",
            CosmicRemnant.None, "센서 교정 · 방사선", "대피소 · 항로 비키기", 0.8f),
        new(CosmicKind.MagnetarStorm, "magnetar", "마그네타 자기 폭풍", CosmicGroup.Star, 6f, 20f, "자기계 — 바늘이 미쳐 돈다",
            new[] { S(0f, 0.3f, E | L, 1f, "자기장 벼락 — 전자 장비가 한꺼번에 꺼졌다"), S(0.3f, 4f, E | B, 0.35f, "자기력선이 일렁인다") },
            false, 0f, "#c46cff", "#ff4fd8", "일렁이는 자기력선 · 보랏빛 쌍극 고리",
            CosmicRemnant.Scar, "전자 장비 대량 고장 · 컴퓨터 재시동 · 삭은 회로", "민감 장비 끄기 · 예비 부품", 0.6f),
        new(CosmicKind.NeutronStar, "neutron", "중성자별 근접", CosmicGroup.Star, 24f, 50f, "중력계 — 보이지 않는 무게",
            new[] { S(0f, 10f, T | R, 0.5f, "중성자별 곁 — 골조가 늘어난다") },
            true, 1f, "#d8f0ff", "#6fa8ff", "작고 시린 푸른 점 · 일그러지는 별빛 고리",
            CosmicRemnant.None, "골조 피로 · 기계 마모", "항로 비키기 · 흔들림 대비 고정", 0.6f),
        new(CosmicKind.BlackHoleTide, "blackhole", "블랙홀 근접 조석 응력", CosmicGroup.Star, 40f, 90f, "중력 렌즈 관측 — 별빛이 휜다",
            new[] { S(0f, 14f, T | N | Q, 0.7f, "블랙홀 조석 — 배가 길게 당겨진다") },
            true, 1.4f, "#0a0a0a", "#ffb347", "검은 원반 · 주황 강착 고리 · 휘어 도는 별빛",
            CosmicRemnant.None, "골조가 늘어남 · 항로가 밀림", "항로 비키기 · 흔들림 대비 고정", 0.3f),
        new(CosmicKind.RedGiantShell, "redgiant", "적색 거성 외층 통과", CosmicGroup.Star, 24f, 48f, "천문 관측 — 부푼 외층",
            new[] { S(0f, 12f, H | P | B, 0.6f, "붉은 플라스마 안개 속 — 외판이 달아오른다") },
            true, 1f, "#ff4b2b", "#ff9f6b", "붉은 플라스마 안개 · 소용돌이 덩어리",
            CosmicRemnant.GlowCloud, "그을린 외판 · 바깥 설비 침식", "항로 비키기 · 바깥 방 비우기", 0.6f),
        new(CosmicKind.BinaryEclipse, "eclipse", "쌍성 식 급랭", CosmicGroup.Star, 12f, 30f, "궤도 계산 — 두 별이 겹친다",
            new[] { S(0f, 10f, C, 0.8f, "쌍성이 겹쳐 하늘이 어두워졌다 — 바깥 쪽 방이 얼어붙는다") },
            false, 0f, "#3a6bff", "#ffe08a", "겹치는 두 별 · 서리 낀 푸른 하늘",
            CosmicRemnant.None, "언 관 · 터진 이음", "보온 · 바깥 방 비우기", 0.9f),
        new(CosmicKind.WolfRayetWind, "wolfrayet", "울프-레이에 항성풍", CosmicGroup.Star, 18f, 40f, "분광 관측 — 뜨거운 바람",
            new[] { S(0f, 8f, H | P | R, 0.5f, "청백색 항성풍이 외판을 깎는다") },
            true, 0.8f, "#9fe8ff", "#ffffff", "비스듬히 흐르는 청백 바람 줄기 · 겹 껍질",
            CosmicRemnant.Bubble, "깎인 외판 · 바깥 설비 침식", "항로 비키기 · 대피소", 0.5f),
        // ─── 천체 충돌 ───
        new(CosmicKind.BigAsteroid, "bigasteroid", "큰 소행성 충돌 경로", CosmicGroup.Body, 12f, 30f, "레이더 · 광학 — 궤적 계산",
            new[] { S(0f, 0.08f, X | K, 1f, "큰 소행성이 들이받았다") },
            true, 2.0f, "#8b7a66", "#ffcf8a", "다가오는 거대한 바위 · 회피 궤적 · 충돌 구획",
            CosmicRemnant.Streak, "버린 구획 · 파편 띠", "회피 기동(추진제) · 안 되면 구획 비우고 봉쇄", 1.0f),
        new(CosmicKind.CometCore, "cometcore", "혜성 핵 근접 가스 분출", CosmicGroup.Body, 10f, 28f, "광학 — 밝아지는 코마",
            new[] { S(0f, 5f, D | B | P, 0.6f, "혜성 핵이 가스를 뿜는다 — 얼음 알갱이 비") },
            true, 0.6f, "#9be7ff", "#ffe9a8", "혜성 핵 · 곧은 푸른 이온 꼬리 · 휜 먼지 꼬리 · 분출 줄기",
            CosmicRemnant.GlowCloud, "먼지 덮인 센서 · 외판 자국", "항로 비키기 · 선외 작업 중단", 0.9f),
        new(CosmicKind.PlanetRing, "ring", "행성 고리 통과", CosmicGroup.Body, 6f, 18f, "항법 계산 — 고리면",
            new[] { S(0f, 3f, D, 1.5f, "고리면을 지난다 — 얼음 입자 폭풍 · 연속 회피 기동") },
            true, 0.5f, "#e6d3a3", "#c9a66b", "가로지르는 고리 띠 · 빽빽한 입자 폭풍",
            CosmicRemnant.None, "외판 자국 · 창 금", "항로 비키기 · 회피 기동 준비", 1.0f),
        new(CosmicKind.ShatteredPlanet, "shattered", "행성 파괴 잔해 구름", CosmicGroup.Body, 20f, 50f, "광학 — 빛나는 파편",
            new[] { S(0f, 10f, D | H, 0.6f, "부서진 행성의 빛나는 파편 속을 지난다") },
            true, 1f, "#ff7a3c", "#5a4a40", "쪼개진 행성 · 마그마 금 · 빛나는 파편 구름",
            CosmicRemnant.GlowCloud, "외판 자국 · 하늘에 빛나는 잔해", "항로 비키기 · 회피 기동 준비", 0.4f),
        new(CosmicKind.Kessler, "kessler", "연쇄 충돌 잔해", CosmicGroup.Body, 4f, 12f, "레이더 — 파편이 파편을 부른다",
            new[] { S(0f, 6f, D, 0.8f, "연쇄 충돌 — 잔해가 잔해를 부른다") },
            true, 0.6f, "#a0a8b8", "#ffd166", "궤도 선을 따라 부서지는 위성 · 충돌 불꽃",
            CosmicRemnant.Ring, "잔해 고리 · 외판 자국", "항로 비키기 · 선외 작업 중단", 0.8f),
        new(CosmicKind.HyperDust, "hyperdust", "초고속 먼지 구름", CosmicGroup.Body, 2f, 8f, "레이더 — 흐린 반사",
            new[] { S(0f, 3f, D | P | B, 0.6f, "초고속 먼지가 외판을 사포질한다") },
            false, 0f, "#d9c7a0", "#8a7a5a", "빗금처럼 흐르는 먼지 줄 · 모래빛 아지랑이",
            CosmicRemnant.None, "흐려진 창 · 깎인 안테나", "선외 작업 중단 · 바깥 설비 접기", 1.0f),
        new(CosmicKind.RoguePlanet, "rogue", "떠돌이 행성 근접", CosmicGroup.Body, 30f, 70f, "광학 — 별을 가리는 검은 원",
            new[] { S(0f, 8f, T | D | B, 0.5f, "떠돌이 행성이 하늘을 가리며 지난다 — 작은 위성 부스러기") },
            true, 1f, "#2b2f3a", "#5a8fb0", "별을 가리는 검은 원반 · 푸른 테두리 · 작은 위성들",
            CosmicRemnant.None, "골조 피로 · 외판 자국", "항로 비키기 · 흔들림 대비 고정", 0.5f),
        // ─── 인공 ───
        new(CosmicKind.ReactorBlast, "reactorblast", "다른 배 원자로 폭발", CosmicGroup.Artificial, 0.5f, 3f, "교신 — 조난 신호와 경고",
            new[] { S(0f, 0.15f, L | K | R, 1f, "멀리서 배 하나가 터졌다 — 충격파"), S(0.15f, 4f, R, 0.5f, "낙진 구름이 지나간다") },
            false, 0f, "#7cff6b", "#ffffff", "먼 배 실루엣 → 초록 섬광 · 퍼지는 충격파 · 빛나는 잔해",
            CosmicRemnant.Wreck, "빛나는 잔해 · 방사선", "대피소 · 흔들림 대비 고정", 0.8f),
        new(CosmicKind.StationCollapse, "station", "정거장 붕괴", CosmicGroup.Artificial, 6f, 18f, "교신 — 붕괴 경보 방송",
            new[] { S(0f, 6f, D, 0.7f, "무너지는 정거장의 큰 조각이 날아든다") },
            true, 0.7f, "#c0c8d8", "#ff6b6b", "바퀴살 정거장이 부서지며 흩어지는 조각",
            CosmicRemnant.Wreck, "잔해 · 외판 자국 · 그날 들은 마지막 교신", "항로 비키기 · 회피 기동 준비", 0.6f),
        new(CosmicKind.AntimatterBreach, "antimatter", "반물질 격납 실패", CosmicGroup.Artificial, 1f, 5f, "교신 · 감마 신호",
            new[] { S(0f, 0.1f, L | K | R | E, 1f, "반물질 섬광 — 하얀 점이 하늘을 찢었다") },
            false, 0f, "#ffffff", "#ff3cf0", "하얀·분홍 점 섬광 · 빛살 · 퍼지는 고리",
            CosmicRemnant.GlowCloud, "방사선 · 전자 장비 · 외판", "대피소 · 민감 장비 끄기 · 흔들림 대비", 0.35f),
        new(CosmicKind.FusionRunaway, "fusionrunaway", "핵융합 엔진 폭주", CosmicGroup.Artificial, 1f, 4f, "교신 — 폭주하는 배의 비명",
            new[] { S(0f, 1f, H | R | L, 0.7f, "폭주하는 배의 푸른 배기 불꽃이 스쳤다") },
            true, 0.5f, "#6fd3ff", "#ffffff", "하늘을 가르는 긴 푸른 배기 불꽃",
            CosmicRemnant.Streak, "그을린 외판 · 방사선", "항로 비키기 · 대피소", 0.6f),
        new(CosmicKind.MineField, "mines", "전쟁 잔해 기뢰 지대", CosmicGroup.Artificial, 4f, 14f, "레이더 — 옛 해도",
            new[] { S(0f, 8f, A, 0.6f, "기뢰 지대 — 근접 신관이 깨어난다") },
            true, 0.6f, "#ff5050", "#3b3f48", "가시 돋은 기뢰 · 깜빡이는 빨간 불 · 터지는 섬광",
            CosmicRemnant.Wreck, "외판 파공 · 기뢰 파편", "항로 비키기 · 회피 기동 준비 · 선외 작업 중단", 0.6f),
        new(CosmicKind.OrbitalEmp, "orbitalemp", "궤도 무기 EMP", CosmicGroup.Artificial, 0.5f, 3f, "교신 — 경고 방송",
            new[] { S(0f, 0.15f, E | L, 1f, "궤도 무기가 전자기 펄스를 쐈다") },
            false, 0f, "#5cf0ff", "#ffffff", "충전하는 궤도 포대 · 퍼지는 청록 구 · 화면 지지직",
            CosmicRemnant.None, "전자 장비 고장 · 컴퓨터 재시동", "민감 장비 끄기", 0.6f),
        new(CosmicKind.Freighter, "freighter", "거대 화물선 충돌 경로", CosmicGroup.Artificial, 3f, 10f, "레이더 · 교신 — 응답 없는 배",
            new[] { S(0f, 0.08f, X | K, 0.8f, "거대 화물선이 스치며 들이받았다") },
            true, 0.5f, "#ffb000", "#4a4f5c", "커지는 상자꼴 화물선 · 항해등 · 충돌 경로선",
            CosmicRemnant.None, "버린 구획 · 외판", "회피 기동 · 안 되면 구획 비우고 봉쇄", 0.7f),
        new(CosmicKind.PirateFleet, "pirates", "해적 함대", CosmicGroup.Artificial, 6f, 20f, "교신 감청 — 낯선 호출 부호",
            new[] { S(0f, 3f, A | E | B, 0.6f, "해적 함대가 쏘아 댄다 — 전파 방해") },
            true, 1.2f, "#ff3b3b", "#ffd166", "쐐기꼴 배 넷 · 빨간 불 · 예광탄",
            CosmicRemnant.None, "외판 파공 · 전자 장비 · 두려움", "항로 비키기(도망) · 민감 장비 끄기", 0.7f),
        // ─── 이상 현상 ───
        new(CosmicKind.DarkNebula, "darknebula", "암흑 성운", CosmicGroup.Anomaly, 24f, 60f, "광학 — 별이 하나둘 사라진다",
            new[] { S(0f, 16f, B | N, 0.9f, "암흑 성운 속 — 별이 사라졌다 · 센서와 항법이 먹통") },
            true, 0.8f, "#0b0d14", "#3b2f4a", "별을 삼키는 검은 구름 · 희미한 보랏빛 가장자리",
            CosmicRemnant.None, "늦어진 항로 · 놓친 운석", "항로 비키기 · 손 조종 준비", 0.8f),
        new(CosmicKind.CosmicRayShower, "cosmicray", "우주선 소나기", CosmicGroup.Anomaly, 8f, 24f, "입자 검출기 — 세기가 오른다",
            new[] { S(0f, 10f, R | E, 0.45f, "우주선 소나기 — 반짝이는 줄이 배를 꿰뚫는다") },
            false, 0f, "#a8ffcf", "#ffffff", "화면을 긋는 짧고 밝은 입자 줄 · 반짝이는 눈",
            CosmicRemnant.None, "삭은 회로 · 방사선", "대피소 · 민감 장비 끄기", 1.0f),
        new(CosmicKind.IonNebula, "ionnebula", "거대 이온 성운", CosmicGroup.Anomaly, 20f, 50f, "분광 관측 — 빛나는 이온",
            new[] { S(0f, 12f, E | B | P, 0.5f, "이온 성운 속 — 번개가 외판을 긁는다") },
            true, 0.8f, "#4fc3ff", "#e0f7ff", "푸르게 빛나는 실타래 · 번지는 번개",
            CosmicRemnant.GlowCloud, "센서 교정 · 정전기 · 바깥 설비", "민감 장비 끄기 · 항로 비키기", 0.8f),
        new(CosmicKind.GravityWave, "gravitywave", "중력파 진동", CosmicGroup.Anomaly, 1f, 5f, "중력계 — 먼 블랙홀 쌍의 합쳐짐",
            new[] { S(0f, 2f, Q | T, 0.7f, "중력파가 지나간다 — 배가 늘었다 줄었다") },
            false, 0f, "#e0e0ff", "#8f8fff", "번지는 동심 물결 · 흔들리는 별",
            CosmicRemnant.None, "마운트 균열 · 멀미 · 넘어짐", "흔들림 대비 고정 · 앉아 있기", 0.8f),
    };

    public static CosmicSpec Spec(CosmicKind k) => All[(int)k];
    public static CosmicSpec? Find(string id) => All.FirstOrDefault(s => s.Id == id || s.Kind.ToString() == id);

    public static string GroupName(CosmicGroup g) => g switch
    {
        CosmicGroup.Star => "별", CosmicGroup.Body => "천체 충돌", CosmicGroup.Artificial => "인공", _ => "이상 현상",
    };

    public static string RemnantName(CosmicRemnant r) => r switch
    {
        CosmicRemnant.Nebula => "새 성운", CosmicRemnant.GlowCloud => "빛나는 구름", CosmicRemnant.Ring => "잔해 고리", CosmicRemnant.Streak => "빛 줄기",
        CosmicRemnant.Wreck => "빛나는 잔해", CosmicRemnant.Bubble => "바람 거품", CosmicRemnant.Scar => "오로라 흔적", _ => "",
    };

    /// <summary>구간마다 잘 일어나는 것 (무게 배율).</summary>
    public static float LegMul(LegKind leg, CosmicKind k) => (leg, k) switch
    {
        (LegKind.Pulsar, CosmicKind.PulsarBeam or CosmicKind.MagnetarStorm or CosmicKind.NeutronStar) => 4f,
        (LegKind.Nebula, CosmicKind.DarkNebula or CosmicKind.IonNebula) => 4f,
        (LegKind.AsteroidBelt, CosmicKind.BigAsteroid or CosmicKind.Kessler or CosmicKind.ShatteredPlanet) => 4f,
        (LegKind.RadiationBelt, CosmicKind.CoronalMass or CosmicKind.SuperFlare or CosmicKind.CosmicRayShower) => 3f,
        (LegKind.Derelict or LegKind.Graveyard, CosmicKind.MineField or CosmicKind.StationCollapse or CosmicKind.ReactorBlast) => 4f,
        (LegKind.TradeLane, CosmicKind.Freighter or CosmicKind.PirateFleet or CosmicKind.FusionRunaway) => 4f,
        (LegKind.PatrolLane, CosmicKind.OrbitalEmp or CosmicKind.PirateFleet) => 4f,
        (LegKind.GasGiant or LegKind.IceRing, CosmicKind.PlanetRing or CosmicKind.CometCore) => 4f,
        (LegKind.DeepVoid, CosmicKind.GammaBurst or CosmicKind.RoguePlanet or CosmicKind.GravityWave or CosmicKind.BlackHoleTide or CosmicKind.Supernova) => 3f,
        (LegKind.SolarWind or LegKind.Perihelion, CosmicKind.SuperFlare or CosmicKind.CoronalMass or CosmicKind.BinaryEclipse or CosmicKind.WolfRayetWind) => 3f,
        (LegKind.CometTrail, CosmicKind.CometCore or CosmicKind.HyperDust) => 4f,
        (LegKind.MagneticField, CosmicKind.MagnetarStorm or CosmicKind.IonNebula) => 4f,
        (LegKind.Port, _) => 0.2f, // 정박 중엔 드물다
        _ => 1f,
    };
}
