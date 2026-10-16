using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 기술 체계 개편 (함장 결정 · docs/TECH_REDESIGN.md):
//   시대 일곱 × 줄기 여덟 = 본 기술 56. 시대 하나의 여덟 기술을 모두 익히면 다음 시대.
//   예전 시대 기술 · 그물 · 숨은 기술 142는 흡수했다 — 새 기술을 익히면 그 안에 담긴 옛 기술(아이디)도 함께 익힌 것으로 친다
//   (사고 배율 · 설비 단계 · 모듈처럼 옛 아이디로 효과를 읽는 곳은 그대로 돈다).
//   갈림길 여덟은 그 시대 기술 안의 두 갈래, 숨은 조합 13 · 겪어야 열리는 10은 곁가지(시대를 막지 않는다).
//   설비 단계(Lifts)는 기술을 익힌 결과다 — 로봇 · 승무원이 설치 공사를 해야 실제로 바뀐다.

public enum Branch : byte { Power, Life, Food, Medical, Hull, Machines, Navigation, Planet }

/// <summary>갈림길의 한 갈래: 옛 기술 아이디 · 이름 · 말.</summary>
public sealed record TreeVariant(string Id, string Name, string Says);

/// <summary>본 기술 · 곁가지 하나.</summary>
public sealed record TreeTech(
    string Id, int Era, Branch Branch, string Name, string Says,
    string[] Absorbs,
    (FurnitureType type, int tier)[] Lifts,
    Skill Keeper,
    (string key, float mul)[]? Fx = null,
    (string fork, string question, TreeVariant a, TreeVariant b)? Fork = null,
    bool Side = false,
    string? Hint = null,
    string[]? Combo = null,
    string? GateOld = null)
{
    public float Cost => TechTree.EraCost(Era) * (Side ? 0.7f : 1f);
}

public static class TechTree
{
    public static readonly string[] EraNames =
    {
        "", "핵분열 출항", "폐쇄 순환", "핵융합", "나노 조립 · 자율 기계", "반물질 · 인공 생태계", "중력 제어 · 성간 추진", "공간 접힘 · 살아 있는 배",
    };
    public const int Eras = 7;
    public static string EraName(int era) => EraNames[Math.Clamp(era, 1, Eras)];
    public static string Roman(int era) => era switch { 1 => "Ⅰ", 2 => "Ⅱ", 3 => "Ⅲ", 4 => "Ⅳ", 5 => "Ⅴ", 6 => "Ⅵ", _ => "Ⅶ" };
    /// <summary>시대가 오를수록 기술 하나가 무겁다 (보통 배에서 사흘 남짓 → 엿새 남짓).</summary>
    public static float EraCost(int era) => 40f + 12f * (era - 1);

    public static string BranchName(Branch b) => b switch
    {
        Branch.Power => "동력", Branch.Life => "생명유지", Branch.Food => "식량", Branch.Medical => "의료",
        Branch.Hull => "선체", Branch.Machines => "로봇 · 컴퓨터", Branch.Navigation => "항법 · 추진", _ => "행성 · 원정",
    };

    private static (FurnitureType, int)[] L(params (FurnitureType, int)[] l) => l;
    private static (string, float)[] X(params (string, float)[] x) => x;
    private static readonly (FurnitureType, int)[] None = Array.Empty<(FurnitureType, int)>();
    private static TreeTech T(string id, int era, Branch b, string name, string says, string[] absorbs, (FurnitureType, int)[]? lifts, Skill keeper, (string, float)[]? fx = null) =>
        new(id, era, b, name, says, absorbs, lifts ?? None, keeper, fx);
    private static TreeTech F(string id, int era, Branch b, string name, string says, string[] shared, (FurnitureType, int)[]? lifts, Skill keeper,
        string forkId, string question, TreeVariant a, TreeVariant v) =>
        new(id, era, b, name, says, shared, lifts ?? None, keeper, null, (forkId, question, a, v));
    private static TreeTech Combo(string old, Branch b, string name, string hint, string a, string bb, Skill keeper) =>
        new("side." + old, 0, b, name, "", new[] { old }, None, keeper, null, null, Side: true, Hint: hint, Combo: new[] { a, bb });
    private static TreeTech Gate(string old, Branch b, string name, string hint, Skill keeper) =>
        new("side." + old, 0, b, name, "", new[] { old }, None, keeper, null, null, Side: true, Hint: hint, GateOld: old);
    private static string[] A(params string[] ids) => ids;

    // ─────────────────────────────── 본 기술 56 ───────────────────────────────
    public static readonly TreeTech[] Main =
    {
        // ── 동력 ──
        T("p1", 1, Branch.Power, "안전 배전반", "새는 전류를 끊고, 냉각수에 녹 막이를 탄다", A("rcd", "coolantdope"), null, Skill.Electrical),
        T("p2", 2, Branch.Power, "개량 핵분열로", "제어봉과 배전을 다시 짜 원자로가 한결 세진다 · 보조 발전기는 연료전지로", A("smartgrid", "heatpipe"),
            L((FurnitureType.ReactorCore, 2), (FurnitureType.CoolantPump, 2), (FurnitureType.AuxGenerator, 2)), Skill.Engineering),
        T("p3", 3, Branch.Power, "소형 핵융합로", "플라스마를 자석으로 가둔다 — 원자로 출력이 두 배", A("fusioncore", "supertrunk", "lowdrawled"),
            L((FurnitureType.ReactorCore, 3)), Skill.Engineering),
        T("p4", 4, Branch.Power, "고체 전지 뱅크", "셀이 덜 늙고 오래 버틴다 · 냉각 펌프는 초전도로", A("solidcell", "supercap", "liquidmetal"),
            L((FurnitureType.Battery, 2), (FurnitureType.CoolantPump, 3)), Skill.Electrical),
        T("p5", 5, Branch.Power, "반물질 점화 핵융합", "반물질 한 점으로 큰 노심에 불을 붙인다 — 원자로 2.8배 · 초전도 저장고", A("radiator"),
            L((FurnitureType.ReactorCore, 4), (FurnitureType.Battery, 3)), Skill.Engineering),
        T("p6", 6, Branch.Power, "영점 배전", "전류의 흔들림을 지워 차단기가 연달아 떨어지지 않는다", A("zeropoint"), null, Skill.Electrical, X(("reactor", 1.1f))),
        T("p7", 7, Branch.Power, "진공 에너지 탭", "빈 공간의 요동에서 전기를 길어 올린다 — 원자로 +25%", A(), null, Skill.Engineering, X(("reactor", 1.25f))),

        // ── 생명유지 ──
        T("l1", 1, Branch.Life, "공기 정화 · 불연 내장", "아민으로 공기를 거르고, 불이 잘 안 붙는 내장재를 댄다", A("amine", "fireproof", "greasecode"), null, Skill.Mechanics),
        F("l2", 2, Branch.Life, "산소 순환", "쓴 물을 증류해 되살리고 산소 발생기를 바꾼다", A("vcd", "greywater"),
            L((FurnitureType.OxygenGenerator, 2), (FurnitureType.WaterRecycler, 2)), Skill.Mechanics,
            "oxygen", "산소를 무엇으로 만들까", new("chemox", "화학 산소", "산소 양초를 태워 급할 때 산소를 낸다"), new("algaeox", "조류 광합성", "조류 수조가 숨을 대신 쉰다")),
        T("l3", 3, Branch.Life, "생물 여과 순환", "균이 물을 거르고 찌꺼기는 흙으로 — 물 두 배 · 산소 +80%", A("biofilter", "compostloop", "habflow", "fireretard"),
            L((FurnitureType.WaterRecycler, 3), (FurnitureType.OxygenGenerator, 3)), Skill.Botany),
        T("l4", 4, Branch.Life, "공기 조화 구역", "방마다 습도를 맞추고 안쪽 방에 폭풍 대피 차폐를 두른다", A("hvaczone", "stormcellar", "algaebio"), null, Skill.Mechanics),
        T("l5", 5, Branch.Life, "완전 폐쇄 생태계", "물과 공기를 거의 잃지 않는다 · 빛으로 물과 산소를 만든다", A("closedloop", "photosynth"), null, Skill.Botany),
        T("l6", 6, Branch.Life, "인공 중력 거주구", "회전 거주구로 근육이 덜 빠지고, 조립식 선실에서 푹 쉰다", A("gravity", "podcabin"), null, Skill.Engineering),
        T("l7", 7, Branch.Life, "살아 있는 공기", "벽이 숨을 쉰다 — 산소 · 물 +20%", A(), null, Skill.Botany, X(("o2", 1.2f), ("water", 1.2f))),

        // ── 식량 ──
        F("f1", 1, Branch.Food, "곡물 지키기", "종자를 골라 두고, 절이고 발효해 오래 둔다", A("seedbank", "pickling", "sourdough", "plantstraps"), null, Skill.Cooking,
            "pests", "곡물 벌레를 무엇으로 막을까", new("catcare", "고양이 돌봄", "고양이가 창고를 지킨다"), new("sealedbins", "밀폐 곡물통", "곡물을 고무 패킹 통에 잠근다")),
        F("f2", 2, Branch.Food, "개량 재배", "유전자 개량 작물 · LED 재배대 — 생장 +35%", A("genecrops"), L((FurnitureType.GrowBed, 2)), Skill.Botany,
            "farming", "작물을 무엇에 키울까", new("nftloop", "양액 순환", "뿌리에 양액이 고르게 돈다"), new("soilbed", "흙 재배", "흙과 퇴비로 키운다 — 병에 강하다")),
        T("f3", 3, Branch.Food, "냉장 사슬 · 진공 포장", "만들고 나르고 쌓는 내내 차갑게, 남은 음식은 진공으로", A("coldchain", "vacpack", "pressurecook"), null, Skill.Cooking),
        T("f4", 4, Branch.Food, "공중 재배", "뿌리를 안개로 키운다 — 생장 +80%", A("aeroponics", "insectprotein"), L((FurnitureType.GrowBed, 3)), Skill.Botany),
        T("f5", 5, Branch.Food, "배양 단백질", "고기를 통에서 키운다 — 조리 +25%", A("vatprotein"), null, Skill.Cooking),
        T("f6", 6, Branch.Food, "인공 광합성 농장", "빛을 맞춘 작물은 밤낮이 없다 — 생장 +15%", A(), null, Skill.Botany, X(("grow", 1.15f))),
        T("f7", 7, Branch.Food, "자급자족 식탁", "무엇이든 다시 키워 다시 먹는다 — 생장 +10% · 음식이 덜 상한다", A(), null, Skill.Cooking, X(("grow", 1.1f), ("food.rot", 0.6f))),

        // ── 의료 ──
        T("m1", 1, Branch.Medical, "응급 분류 · 위생 수칙", "다친 사람을 빨리 가르고, 손 씻기와 세척을 지킨다", A("triage", "hygiene", "eyewashcode"), null, Skill.Medicine),
        T("m2", 2, Branch.Medical, "원격 진료 · 마음 쉼터", "지구 의사의 처방 · 발언 막대 · 함께 부르는 추모", A("telemed", "talkingstick", "remembrance"), null, Skill.Medicine),
        T("m3", 3, Branch.Medical, "재생 의학 · 수술 로봇", "상처가 빨리 아물고, 떨지 않는 손이 칼을 잡는다", A("regenmed", "surgbot"), null, Skill.Medicine),
        T("m4", 4, Branch.Medical, "저온 의료", "죽어 가는 사람을 얼려 붙잡는다", A("cryomed"), null, Skill.Medicine),
        T("m5", 5, Branch.Medical, "나노 의료", "핏속 기계가 상처를 꿰맨다", A("nanomed"), null, Skill.Medicine),
        T("m6", 6, Branch.Medical, "가상 고향", "꿈처럼 지구를 거닌다 — 마음 병이 크게 준다", A("homeworld"), null, Skill.Medicine),
        T("m7", 7, Branch.Medical, "유전자 수선", "몸이 스스로 고친다 — 회복 +30%", A(), null, Skill.Medicine, X(("heal", 1.3f))),

        // ── 선체 ──
        T("h1", 1, Branch.Hull, "용접 규격 · 안전띠", "이음매를 규격대로 잇고, 다섯 점으로 몸을 붙드는 띠", A("weldcode", "crashharness"), null, Skill.Mechanics),
        F("h2", 2, Branch.Hull, "방사선 막기", "선체에 붕소 · 납 층을 댄다", A("radshield"), null, Skill.Engineering,
            "radiation", "방사선을 무엇으로 막을까", new("waterwall", "물 차폐벽", "물탱크로 방사선을 막는다"), new("magshield", "자기장 차폐", "코일이 대전 입자를 휜다")),
        F("h3", 3, Branch.Hull, "운석 막기", "작은 균열은 저절로 닫히고, 골조는 가볍고 질겨진다", A("selfseal", "compositerib"), null, Skill.Mechanics,
            "armor", "운석을 무엇으로 막을까", new("heavyarmor", "무거운 장갑", "선체를 두껍게 덧댄다"), new("pdlaser", "점 방어 레이저", "다가오는 돌을 태운다")),
        T("h4", 4, Branch.Hull, "폭발 억제 · 이온 편향", "불꽃이 번지기 전에 거품이 덮고, 대전 입자를 비켜 낸다", A("blastfoam", "iondeflector"), null, Skill.Engineering),
        T("h5", 5, Branch.Hull, "형상 기억 선체", "찌그러진 판이 제 모양으로 돌아온다", A("shapememory"), null, Skill.Mechanics),
        T("h6", 6, Branch.Hull, "역장", "보이지 않는 막이 돌을 휘고 막는다 — 운석 피해 −50%", A("forcefield", "gravlens"), null, Skill.Electrical),
        T("h7", 7, Branch.Hull, "위상 선체", "선체가 충격을 흘려보낸다", A("phasehull"), null, Skill.Engineering),

        // ── 로봇 · 컴퓨터 ──
        T("c1", 1, Branch.Machines, "점검표 · 연구 노트", "실수가 줄고, 실험을 함께 적고, 공구를 찾느라 헤매지 않는다", A("checklist", "labnotes", "toolboard"), null, Skill.Electrical),
        F("c2", 2, Branch.Machines, "배를 다스릴 머리", "멎으면 스스로 다시 켜고 · 기록 사본을 둔다 · 부품을 한 층씩 찍는다", A("watchdog", "bootcache", "vaultcopy", "additive"),
            L((FurnitureType.MainComputer, 2), (FurnitureType.Workbench, 2)), Skill.Electrical,
            "brain", "배를 무엇이 다스릴까", new("centralcpu", "중앙 컴퓨터", "한 머리가 배 전체를 본다 — 더 멀리 내다본다"), new("distctrl", "분산 제어", "설비마다 작은 제어기가 서로 묻는다")),
        T("c3", 3, Branch.Machines, "드론 편대 · 예지 정비", "드론이 바깥을 고치고, 닳기 전에 갈 것을 안다", A("dronenet", "cobotarm", "predictive", "crawler", "modularfit", "quickcouple", "baler"), null, Skill.Mechanics),
        F("c4", 4, Branch.Machines, "닳은 것 고치기", "가설을 기계가 먼저 거른다 · 적층 제조기 · 정련기 개량", A("neuralnet"),
            L((FurnitureType.Workbench, 3), (FurnitureType.Refinery, 2)), Skill.Mechanics,
            "repair", "닳은 것을 누가 고칠까", new("nanorepair", "나노 수리", "설비가 스스로 아문다"), new("exosuit", "작업 외골격", "무거운 것을 혼자 든다")),
        T("c5", 5, Branch.Machines, "부함장 컴퓨터 · 분자 조립기", "회의를 돕는 보조 두뇌 · 원료에서 부품을 짜 맞춘다", A("aicaptain", "assembler"), null, Skill.Electrical),
        T("c6", 6, Branch.Machines, "양자 연산 · 자기 복제", "모든 경우를 한 번에 따지고, 로봇이 로봇을 고친다", A("quantumcpu", "selfreplicate"), null, Skill.Electrical),
        T("c7", 7, Branch.Machines, "물질 변환로", "무엇이든 원자부터 다시 짠다", A("matterforge"), null, Skill.Mechanics),

        // ── 항법 · 추진 ──
        F("n1", 1, Branch.Navigation, "무게가 사라질 때", "설비 소리를 듣고, 짐마다 끈 두 줄", A("vibelisten", "cargolash"), null, Skill.Piloting,
            "float", "무게가 사라지면 무엇에 기댈까", new("tetherdrill", "끈 매기 훈련", "먼저 몸을 묶는다"), new("magsoles", "자석 밑창", "신발이 바닥을 붙든다")),
        T("n2", 2, Branch.Navigation, "자세 제어 · 소리 진단", "작은 돌을 비켜 가고, 밸브를 날마다 짧게 시험한다", A("rcsthruster", "rcsdiag", "acoustic"), null, Skill.Piloting),
        T("n3", 3, Branch.Navigation, "핵융합 추진 · 양자 센서", "이온 추진기 · 위상 배열 레이더 — 표류해도 반은 간다 · 운석을 일찍 본다", A("fusiondrive", "magnozzle", "quantumsense", "fibernet"),
            L((FurnitureType.EngineCore, 2), (FurnitureType.SensorArray, 2)), Skill.Piloting),
        T("n4", 4, Branch.Navigation, "다중 분광 · 중력파 경보", "열 · 연기 · 가스를 한눈에 보고, 멀리서 오는 큰일을 일찍 안다", A("multispec", "gravwave"), null, Skill.Electrical),
        T("n5", 5, Branch.Navigation, "관성 감쇠 · 예지 센서망", "흔들림을 지우고, 모든 설비가 제 상태를 말한다", A("inertialdamp", "sensormesh"), null, Skill.Piloting),
        T("n6", 6, Branch.Navigation, "공간 왜곡 거품", "앞쪽 공간을 접어 항해가 두 배 빠르다", A("warpbubble"), null, Skill.Piloting),
        T("n7", 7, Branch.Navigation, "초공간 항법", "잔해 없는 길을 미리 고른다 — 항해 +25%", A("hypernav"), null, Skill.Piloting, X(("speed", 1.25f))),

        // ── 행성 · 원정 ──
        T("w1", 1, Branch.Planet, "원정 지도 · 방열복", "다녀온 길을 남기고, 은빛 겹옷이 열을 되비춘다", A("expmap", "heatsuit"), null, Skill.Piloting),
        T("w2", 2, Branch.Planet, "잔해 탐지 · 질긴 우주복", "잔해 속 옛 물건을 찾고, 우주복을 한 겹 더 · 자기 수집기", A("salvagescan", "aramidsuit", "antistatic"),
            L((FurnitureType.Collector, 2)), Skill.Mechanics),
        T("w3", 3, Branch.Planet, "겹 씰 접안 · 자가 봉합 우주복", "씰 두 겹이 맞물리고, 찢긴 우주복이 저절로 오므린다", A("dockseal", "selfpatchsuit"), null, Skill.Mechanics),
        T("w4", 4, Branch.Planet, "착륙 다리 완충", "땅이 흔들려도 배가 버틴다 — 착륙 사고 −40% · 환경 사고 부상 −20%", A(), null, Skill.Piloting, X(("landing", 0.6f), ("env", 0.8f))),
        T("w5", 5, Branch.Planet, "궤도 채굴 드론망", "드론 떼가 원료를 긁어 온다 — 채굴 +40%", A(), L((FurnitureType.Collector, 3)), Skill.Mechanics, X(("mining", 1.4f))),
        T("w6", 6, Branch.Planet, "환경 적응 거주 돔", "어떤 하늘 아래서도 버티는 돔 — 환경 사고 −30%", A(), null, Skill.Engineering, X(("env", 0.7f))),
        T("w7", 7, Branch.Planet, "행성 개조 씨앗", "죽은 땅에 숨을 불어넣는다 — 환경 사고 −30% · 채굴 +30%", A(), null, Skill.Botany, X(("env", 0.7f), ("mining", 1.3f))),
    };

    // ─────────────────────────────── 곁가지 23 (시대를 막지 않는다) ───────────────────────────────
    public static readonly TreeTech[] SideTechs =
    {
        Combo("safegrid", Branch.Power, "무아크 배전", "차단기와 오래 가는 전지를 함께 알면…", "rcd", "solidcell", Skill.Electrical),
        Combo("heatwater", Branch.Life, "폐열 증류", "열을 옮기는 법과 물을 되살리는 법을 함께 알면…", "heatpipe", "vcd", Skill.Mechanics),
        Combo("heirloom", Branch.Food, "고향 씨앗 복원", "종자 은행과 개량 작물을 함께 알면…", "seedbank", "genecrops", Skill.Botany),
        Combo("printpatch", Branch.Hull, "현장 인쇄 패치", "스스로 닫히는 선체와 적층 제작을 함께 알면…", "selfseal", "additive", Skill.Mechanics),
        Combo("flashshutter", Branch.Hull, "섬광 덧창", "방사선 차폐와 난파선 추적을 함께 알면…", "radshield", "wreckwatch", Skill.Engineering),
        Combo("diagai", Branch.Medical, "진단 보조기", "원격 진료와 연구 신경망을 함께 알면…", "telemed", "neuralnet", Skill.Medicine),
        Combo("smartgalley", Branch.Food, "조리 감시기", "냉장 사슬과 발효종을 함께 알면…", "coldchain", "sourdough", Skill.Cooking),
        Combo("digitaltwin", Branch.Machines, "디지털 쌍둥이", "광섬유 감지망과 예지 정비를 함께 알면…", "fibernet", "predictive", Skill.Electrical),
        Combo("swarmrepair", Branch.Machines, "군집 수리", "드론 편대와 배관 기어 로봇을 함께 알면…", "dronenet", "crawler", Skill.Mechanics),
        Combo("labsafety", Branch.Machines, "실험실 안전 절차", "점검표와 연구 노트를 함께 쓰면…", "checklist", "labnotes", Skill.Electrical),
        Combo("plasmatorch", Branch.Machines, "플라스마 절단기", "플라스마를 재는 법과 적층 제작을 함께 알면…", "plasmadiag", "additive", Skill.Mechanics),
        Combo("shipband", Branch.Medical, "배 악단", "발언 막대와 추모 의식이 자리 잡으면…", "talkingstick", "remembrance", Skill.Medicine),
        Combo("biolamp", Branch.Life, "생물 발광 조명", "생물 여과와 조류 광합성을 함께 알면…", "biofilter", "algaeox", Skill.Botany),
        Gate("scmagnet", Branch.Power, "초전도 자석", "희귀 소재가 창고에 있으면…", Skill.Electrical),
        Gate("blastvent", Branch.Hull, "폭압 배출구", "폭발을 한 번 겪고 나면…", Skill.Engineering),
        Gate("gripfloor", Branch.Life, "미끄럼 방지 바닥", "두 번쯤 미끄러져 넘어지고 나면…", Skill.Mechanics),
        Gate("plasmadiag", Branch.Power, "플라스마 진단", "전기에 솜씨 좋은 사람이 있으면…", Skill.Electrical),
        Gate("gyrostab", Branch.Navigation, "자이로 안정기", "중력이 한 번 끊겨 보면…", Skill.Piloting),
        Gate("wreckwatch", Branch.Navigation, "난파선 추적", "부서진 배 조각을 한 번 맞아 보면…", Skill.Piloting),
        Gate("convoylink", Branch.Planet, "선단 통신 규약", "다른 배와 교신하고 나면…", Skill.Electrical),
        Gate("wreckalloy", Branch.Hull, "난파선 합금 역설계", "원정에서 명판 · 기념패를 가져오면…", Skill.Mechanics),
        Gate("beaconcore", Branch.Navigation, "신호기 코어 역설계", "원정에서 신호기 부속을 가져오면…", Skill.Electrical),
        Gate("oldstation", Branch.Medical, "옛 정거장 생활 공법", "원정에서 엽서 묶음을 가져오면…", Skill.Medicine),
    };

    public static readonly TreeTech[] All = Main.Concat(SideTechs).ToArray();
    private static readonly Dictionary<string, TreeTech> ById = All.ToDictionary(t => t.Id);
    public static TreeTech? Find(string? id) => id != null && ById.TryGetValue(id, out var t) ? t : null;

    /// <summary>새 기술을 옛 모양으로 (실험 · 연대기 · 사고 기록이 EraTech를 읽는다). 늦게 만든다 — TechWeb과 서로 부르는 정적 초기화를 피한다.</summary>
    private static Dictionary<string, EraTech>? _asEra;
    public static EraTech? AsEra(string? id)
    {
        if (id == null) return null;
        _asEra ??= All.ToDictionary(t => t.Id, t => new EraTech(t.Id, Math.Max(1, t.Era), FieldOf(t.Branch), t.Name, t.Cost, t.Says.Length > 0 ? t.Says : (t.Hint ?? ""), "없음"));
        return _asEra.TryGetValue(id, out var e) ? e : null;
    }
    public static TechField FieldOf(Branch b) => b switch
    {
        Branch.Power => TechField.Power, Branch.Life => TechField.Life, Branch.Food => TechField.Food, Branch.Medical => TechField.Medical,
        Branch.Hull => TechField.Hull, Branch.Machines => TechField.Computing, Branch.Navigation => TechField.Propulsion, _ => TechField.Habitat,
    };

    /// <summary>옛 기술 아이디 → 그것을 담은 새 기술 (갈림길 갈래 포함).</summary>
    private static readonly Dictionary<string, TreeTech> Owner = BuildOwner();
    private static Dictionary<string, TreeTech> BuildOwner()
    {
        var d = new Dictionary<string, TreeTech>();
        foreach (var t in All)
        {
            foreach (var o in t.Absorbs) d[o] = t;
            if (t.Fork is { } f) { d[f.a.Id] = t; d[f.b.Id] = t; }
        }
        return d;
    }
    public static TreeTech? OwnerOf(string oldId) => Owner.TryGetValue(oldId, out var t) ? t : null;

    /// <summary>곁가지가 놓일 시대 (조합이면 두 재료 중 늦은 쪽 · 조건이면 옛 시대에 맞춘다).</summary>
    public static int SideEra(TreeTech t) =>
        t.Combo is { } c ? c.Select(o => OwnerOf(o)?.Era ?? 1).DefaultIfEmpty(1).Max()
        : TechWeb.Find(t.GateOld) is EraTech e ? Math.Clamp((int)MathF.Round(e.Era * 7f / 6f), 1, Eras) : 1;

    /// <summary>표 점검: 옛 기술 142가 빠짐없이 한 번씩 담겼나 (시험용).</summary>
    public static (int covered, string[] missing, string[] twice) Coverage()
    {
        var seen = new Dictionary<string, int>();
        foreach (var t in All)
        {
            foreach (var o in t.Absorbs) seen[o] = seen.GetValueOrDefault(o) + 1;
            if (t.Fork is { } f) { seen[f.a.Id] = seen.GetValueOrDefault(f.a.Id) + 1; seen[f.b.Id] = seen.GetValueOrDefault(f.b.Id) + 1; }
        }
        var missing = TechWeb.Every.Select(e => e.Id).Where(id => !seen.ContainsKey(id)).ToArray();
        var twice = seen.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToArray();
        return (seen.Count, missing, twice);
    }
}
