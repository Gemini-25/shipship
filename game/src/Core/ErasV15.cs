using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ShipSim.Core;

// v15.5 시대 기술 18 → 70: 시대마다 8~9개 · 열세 분야에 넷씩.
// 효과는 Mul(w, 수치) 하나로 모인다 — 익힌 새 기술들의 배율을 곱한 값. 기존 코드엔 그 값을 곱하는 짧은 줄만 있다:
//   마모(wear) · 고장 확률(fault) · 작물(grow) · 물 회수(water) — Systems.cs
//   산소 생산(o2) — Atmosphere.cs · 연구(research) — World.cs · 부상 회복(heal) — Needs.cs
//   일 속도(repair · craft · cook · treat) — Jobs.cs · 감지기 전조 감지(omen) — Prevention.cs
//   사고 무게(사고 키: HullCrack · meteor …) — EraSystem.RiskMul
// 새 위험은 기존 방식 그대로 (RiskKey · RiskMul).

public static class ErasV15
{
    public sealed record Row(EraTech Tech, (string stat, float mul)[] Fx);

    /// <summary>Mul 이 읽히는 수치 (사고 키 말고).</summary>
    public static readonly string[] Stats = { "wear", "fault", "grow", "water", "o2", "research", "heal", "repair", "craft", "cook", "treat", "omen" };

    private static string StatName(string s) => s switch
    {
        "wear" => "마모", "fault" => "고장", "grow" => "작물 성장", "water" => "물 회수", "o2" => "산소 생산", "research" => "연구",
        "heal" => "부상 회복", "repair" => "수리·정비", "craft" => "제작", "cook" => "조리", "treat" => "치료", "omen" => "감지기 전조 감지",
        "meteor" => "작은 운석", "bigmeteor" => "큰 운석", "fire" => "불", "break" => "고장", "pipe" => "배관 사고",
        _ => Hazards.All.FirstOrDefault(h => h.Kind.ToString() == s)?.Name ?? s,
    };

    private static string Describe((string stat, float mul)[] fx) =>
        string.Join(" · ", fx.Select(f => $"{StatName(f.stat)} {(f.mul >= 1f ? "+" : "−")}{MathF.Round(MathF.Abs(f.mul - 1f) * 100f):0}%"));

    private static (string, float)[] X(params (string, float)[] fx) => fx;

    private static Row T(string id, int era, TechField field, string name, float cost, string what, (string, float)[] fx, string risk = "없음", string? key = null, float mul = 1f)
        => new(new EraTech(id, era, field, name, cost, $"{what} ({Describe(fx)})", risk, key, mul), fx);

    public static readonly Row[] Rows =
    {
        // 1 근지구
        T("coolantdope", 1, TechField.Cooling, "냉각수 첨가제", 26f, "부식 억제제를 탄다", X(("wear", 0.92f))),
        T("seedbank", 1, TechField.Food, "종자 은행", 24f, "튼튼한 씨앗을 골라 둔다", X(("grow", 1.06f), (nameof(HazardKind.SeedRot), 0.7f))),
        T("amine", 1, TechField.Life, "아민 흡착제", 28f, "공기를 더 잘 거른다", X(("o2", 1.08f)), "세정제가 빨리 찬다", nameof(HazardKind.ScrubberSaturation), 1.2f),
        T("weldcode", 1, TechField.Hull, "용접 규격", 24f, "이음매를 규격대로 잇는다", X((nameof(HazardKind.WeldFatigue), 0.7f))),
        T("hygiene", 1, TechField.Medical, "위생 수칙", 22f, "손 씻기와 소독을 지킨다", X((nameof(HazardKind.SkinFungus), 0.6f), (nameof(HazardKind.FoodPoisoning), 0.75f))),
        T("labnotes", 1, TechField.Computing, "공유 연구 노트", 26f, "실험 기록을 함께 쓴다", X(("research", 1.1f))),
        T("toolboard", 1, TechField.Fabrication, "공구 정돈판", 22f, "공구를 찾느라 헤매지 않는다", X(("repair", 1.08f))),
        T("rcd", 1, TechField.Power, "누전 차단기", 28f, "새는 전류를 끊는다", X((nameof(HazardKind.GroundFault), 0.65f), (nameof(HazardKind.ArcFault), 0.8f))),
        T("vibelisten", 1, TechField.Sensors, "진동 청음기", 24f, "감지기가 설비 소리를 듣는다", X(("omen", 1.15f))),
        // 2 태양계
        T("cobotarm", 2, TechField.Robotics, "협동 로봇 팔", 44f, "로봇 팔이 부품을 잡아 준다", X(("repair", 1.1f)), "로봇 팔이 부품을 떨군다", nameof(HazardKind.HoistDrop), 1.25f),
        T("rcsthruster", 2, TechField.Propulsion, "자세 제어 추력기", 42f, "작은 돌을 비켜 간다", X(("meteor", 0.8f)), "추진제 배관이 늘어난다 — 배관 사고", "pipe", 1.2f),
        T("habflow", 2, TechField.Habitat, "생활 동선 설계", 40f, "부딪치지 않게 공간을 나눈다", X((nameof(HazardKind.PanicAttack), 0.7f), (nameof(HazardKind.HumiditySpike), 0.75f))),
        T("waterwall", 2, TechField.Defense, "물 차폐벽", 46f, "물탱크로 방사선을 막는다", X((nameof(HazardKind.RadiationBurst), 0.6f)), "벽 속 물 — 결로 침수", nameof(HazardKind.CondensateFlood), 1.25f),
        T("nftloop", 2, TechField.Food, "양액 순환 재배", 44f, "뿌리에 양액이 고르게 돈다", X(("grow", 1.1f)), "양액이 한 번에 오염된다", nameof(HazardKind.NutrientCrash), 1.3f),
        T("vcd", 2, TechField.Life, "증기 압축 증류", 46f, "쓴 물을 더 많이 되살린다", X(("water", 1.12f)), "탱크에 찌꺼기가 쌓인다", nameof(HazardKind.TankSludge), 1.25f),
        T("heatpipe", 2, TechField.Cooling, "열 파이프", 44f, "열을 조용히 옮긴다", X((nameof(HazardKind.Overheat), 0.7f)), "관 속 작동액이 언다 — 배관 동결", nameof(HazardKind.PipeFreeze), 1.25f),
        T("telemed", 2, TechField.Medical, "원격 진료", 40f, "지구 의사의 처방을 받는다", X(("heal", 1.15f)), "늦게 온 처방 — 투약 실수", nameof(HazardKind.MedError), 1.2f),
        T("additive", 2, TechField.Fabrication, "적층 제작", 48f, "부품을 한 층씩 찍는다", X(("craft", 1.15f)), "불량 묶음이 섞인다", nameof(HazardKind.BadBatch), 1.3f),
        // 3 핵융합
        T("supertrunk", 3, TechField.Power, "초전도 간선", 72f, "간선 손실이 거의 없다", X((nameof(HazardKind.TrunkSag), 0.5f), (nameof(HazardKind.InsulationCrack), 0.7f)), "극저온 냉매가 샌다 — 냉각 상실", nameof(HazardKind.CoolantLoss), 1.25f),
        T("predictive", 3, TechField.Computing, "예지 정비", 70f, "닳기 전에 갈 것을 안다", X(("fault", 0.88f)), "예측이 빗나간다 — 컴퓨터 오판단", nameof(HazardKind.ComputerMisjudge), 1.2f),
        T("fibernet", 3, TechField.Sensors, "광섬유 감지망", 66f, "설비마다 빛 신경을 깐다", X(("omen", 1.25f)), "케이블이 늘어난다 — 케이블 트레이 화재", nameof(HazardKind.CableTrayFire), 1.2f),
        T("crawler", 3, TechField.Robotics, "배관 기어 로봇", 68f, "좁은 관 속을 로봇이 본다", X(("repair", 1.1f), ("pipe", 0.75f)), "로봇 오작동이 잦다", nameof(HazardKind.RobotMalfunction), 1.2f),
        T("compositerib", 3, TechField.Hull, "복합재 늑골", 70f, "가볍고 질긴 골조", X((nameof(HazardKind.FrameCreak), 0.6f), (nameof(HazardKind.ThermalStress), 0.7f))),
        T("hvaczone", 3, TechField.Habitat, "공기 조화 구역", 64f, "방마다 습도를 맞춘다", X((nameof(HazardKind.HumiditySpike), 0.6f), (nameof(HazardKind.MoldOutbreak), 0.65f)), "덕트가 길어진다 — 덕트 화재", nameof(HazardKind.DuctFire), 1.25f),
        T("pdlaser", 3, TechField.Defense, "점 방어 레이저", 76f, "다가오는 돌을 태운다", X(("meteor", 0.7f)), "레이저 충전 — 차단기 연쇄", nameof(HazardKind.BreakerCascade), 1.25f),
        T("magnozzle", 3, TechField.Propulsion, "자기 플라스마 노즐", 72f, "잔해 속을 빨리 빠져나간다", X((nameof(HazardKind.DebrisCloud), 0.7f), (nameof(HazardKind.CometTail), 0.7f)), "노즐 열 — 열 응력", nameof(HazardKind.ThermalStress), 1.2f),
        T("regenmed", 3, TechField.Medical, "재생 의학", 76f, "상처가 빨리 아문다", X(("heal", 1.2f)), "배양실 균 — 전염병", nameof(HazardKind.Epidemic), 1.2f),
        // 4 성간 준비
        T("aeroponics", 4, TechField.Food, "공중 재배", 100f, "뿌리를 안개로 키운다", X(("grow", 1.12f)), "습한 재배실 — 곰팡이", nameof(HazardKind.MoldOutbreak), 1.3f),
        T("algaebio", 4, TechField.Life, "조류 광생물 반응기", 104f, "조류가 산소를 낸다", X(("o2", 1.15f)), "조류가 썩는다 — 물 오염", nameof(HazardKind.WaterContamination), 1.2f),
        T("liquidmetal", 4, TechField.Cooling, "액체 금속 냉각", 108f, "열을 빨리 빼 설비가 덜 닳는다", X(("wear", 0.9f), (nameof(HazardKind.CoolantLoss), 0.7f)), "금속이 새면 불이 붙는다", "fire", 1.15f),
        T("neuralnet", 4, TechField.Computing, "연구 신경망", 110f, "가설을 기계가 먼저 거른다", X(("research", 1.15f)), "학습 서버 과부하 — 컴퓨터 오류", nameof(HazardKind.ComputerFault), 1.25f),
        T("exosuit", 4, TechField.Robotics, "작업 외골격", 96f, "무거운 것을 혼자 든다", X(("repair", 1.08f), ("craft", 1.08f)), "외골격에 끼인다 — 작업 사고", nameof(HazardKind.WorkAccident), 1.2f),
        T("multispec", 4, TechField.Sensors, "다중 분광 감시", 98f, "열·연기·가스를 한눈에 본다", X(("omen", 1.2f), (nameof(HazardKind.Smolder), 0.7f))),
        T("solidcell", 4, TechField.Power, "고체 전지", 100f, "셀이 덜 늙고 수소가 안 샌다", X((nameof(HazardKind.CellAging), 0.5f), (nameof(HazardKind.HydrogenBuildup), 0.7f)), "고전압 셀 — 정전기 방전", nameof(HazardKind.StaticDischarge), 1.2f),
        T("iondeflector", 4, TechField.Defense, "이온 편향기", 112f, "대전 입자를 비켜 낸다", X((nameof(HazardKind.IonStorm), 0.6f), (nameof(HazardKind.SolarStorm), 0.7f)), "편향기 과부하 — 접지 불량", nameof(HazardKind.GroundFault), 1.3f),
        T("podcabin", 4, TechField.Habitat, "조립식 개인 선실", 92f, "제 문을 닫고 푹 쉰다", X((nameof(HazardKind.PanicAttack), 0.6f), ("heal", 1.05f)), "칸막이가 늘어난다 — 문 씰 손상", nameof(HazardKind.SealLeak), 1.2f),
        // 5 탈지구 공학
        T("assembler", 5, TechField.Fabrication, "분자 조립기", 160f, "원료에서 부품을 짜 맞춘다", X(("craft", 1.2f), ("repair", 1.05f)), "조립 찌꺼기 — 설비가 가끔 상한다", "break", 1.15f),
        T("shapememory", 5, TechField.Hull, "형상 기억 합금", 150f, "찌그러진 판이 제 모양으로 돌아온다", X((nameof(HazardKind.HullCrack), 0.7f), (nameof(HazardKind.WindowCrack), 0.6f), (nameof(HazardKind.HatchSeal), 0.7f))),
        T("nanomed", 5, TechField.Medical, "나노 의료", 156f, "핏속 기계가 상처를 꿰맨다", X(("heal", 1.2f), ("treat", 1.2f)), "나노 기계가 균을 옮긴다 — 피부 곰팡이", nameof(HazardKind.SkinFungus), 1.3f),
        T("inertialdamp", 5, TechField.Propulsion, "관성 감쇠", 150f, "흔들림을 지운다", X((nameof(HazardKind.WorkAccident), 0.7f), (nameof(HazardKind.HoistDrop), 0.6f)), "감쇠장 떨림 — 팬 진동", nameof(HazardKind.FanImbalance), 1.25f),
        T("sensormesh", 5, TechField.Sensors, "예지 센서망", 146f, "모든 설비가 제 상태를 말한다", X(("omen", 1.3f), ("fault", 0.95f)), "배선이 얽힌다 — 접지 불량", nameof(HazardKind.GroundFault), 1.2f),
        T("vatprotein", 5, TechField.Food, "배양 단백질", 146f, "고기를 통에서 키운다", X(("cook", 1.25f), ("grow", 1.06f)), "배양조 오염 — 식중독", nameof(HazardKind.FoodPoisoning), 1.3f),
        T("radiator", 5, TechField.Cooling, "복사 냉각 날개", 156f, "남는 열을 우주로 던진다", X(("wear", 0.9f), (nameof(HazardKind.Overheat), 0.8f)), "날개가 바깥에 — 미세 운석", nameof(HazardKind.MicroShower), 1.25f),
        T("photosynth", 5, TechField.Life, "인공 광합성", 164f, "빛으로 물과 산소를 만든다", X(("o2", 1.2f), ("water", 1.1f)), "산소가 짙다 — 산소관 누출", nameof(HazardKind.OxygenLeak), 1.25f),
        // 6 초공간
        T("zeropoint", 6, TechField.Power, "영점 배전", 240f, "전류의 흔들림을 지운다", X((nameof(HazardKind.BreakerCascade), 0.5f), (nameof(HazardKind.PowerSurge), 0.7f)), "배전 공명 — 배전반 아크", nameof(HazardKind.ArcFault), 1.3f),
        T("quantumcpu", 6, TechField.Computing, "양자 연산", 250f, "모든 경우를 한 번에 따진다", X(("research", 1.25f), ("fault", 0.9f)), "결맞음 붕괴 — 컴퓨터 오류", nameof(HazardKind.ComputerFault), 1.3f),
        T("selfreplicate", 6, TechField.Robotics, "자기 복제 로봇", 240f, "로봇이 로봇을 고친다", X(("repair", 1.12f), ("craft", 1.1f)), "로봇이 제멋대로 — 로봇 오작동", nameof(HazardKind.RobotMalfunction), 1.3f),
        T("phasehull", 6, TechField.Hull, "위상 선체", 250f, "선체가 충격을 흘려보낸다", X((nameof(HazardKind.HullCrack), 0.6f), ("meteor", 0.8f), (nameof(HazardKind.FrameCreak), 0.7f)), "위상 불안정 — 열 응력", nameof(HazardKind.ThermalStress), 1.25f),
        T("gravlens", 6, TechField.Defense, "중력 렌즈 방패", 240f, "큰 돌의 길을 휜다", X(("bigmeteor", 0.6f), (nameof(HazardKind.RadiationBurst), 0.7f)), "렌즈가 무너지면 — 잔해 구름", nameof(HazardKind.DebrisCloud), 1.3f),
        T("homeworld", 6, TechField.Habitat, "가상 고향", 230f, "꿈처럼 지구를 거닌다", X((nameof(HazardKind.PanicAttack), 0.5f), ("heal", 1.1f)), "현실이 흐려진다 — 작업 사고", nameof(HazardKind.WorkAccident), 1.2f),
        T("matterforge", 6, TechField.Fabrication, "물질 변환로", 260f, "무엇이든 원자부터 다시 짠다", X(("craft", 1.2f), ("repair", 1.08f), ("wear", 0.95f)), "변환 열 — 훈소", nameof(HazardKind.Smolder), 1.25f),
        T("hypernav", 6, TechField.Propulsion, "초공간 항법", 260f, "잔해 없는 길을 미리 고른다", X((nameof(HazardKind.DebrisCloud), 0.6f), (nameof(HazardKind.CometTail), 0.6f), ("meteor", 0.85f)), "도약 충격 — 고정 볼트 풀림", nameof(HazardKind.LooseMount), 1.3f),
    };

    private sealed class Memo
    {
        public int Count = -1;
        public long Hour = -1;
        public readonly Dictionary<string, float> Mul = new();
    }
    private static readonly ConditionalWeakTable<EraSystem, Memo> Memos = new();

    /// <summary>익힌 새 기술들이 이 수치에 거는 배율 (곱). 수치: Stats 또는 사고 키. 없으면 1.
    /// 기술은 늘기만 하니 익힌 수가 바뀔 때 다시 곱한다 (한 시간에 한 번은 그냥 다시 — 손으로 고친 목록도 따라가게).</summary>
    public static float Mul(World w, string stat)
    {
        var e = w.Eras;
        if (e is null || e.Known.Count == 0 || stat.Length == 0) return 1f;
        var memo = Memos.GetOrCreateValue(e);
        long hour = w.Tick / SimTime.TicksPerHour;
        if (memo.Count != e.Known.Count || memo.Hour != hour)
        {
            memo.Count = e.Known.Count;
            memo.Hour = hour;
            memo.Mul.Clear();
            foreach (var r in Rows) // 표 순서대로 곱한다 (결정론)
                if (e.Known.Contains(r.Tech.Id))
                    foreach (var (s, m) in r.Fx) memo.Mul[s] = (memo.Mul.TryGetValue(s, out var v) ? v : 1f) * m;
        }
        return memo.Mul.TryGetValue(stat, out var x) ? x : 1f;
    }

    /// <summary>일의 종류 → 그 일 속도를 바꾸는 수치 (WorkToil).</summary>
    public static string Work(WorkKind? k) => k switch
    {
        WorkKind.Repair or WorkKind.ResetBreaker or WorkKind.Maintain or WorkKind.RepairHull or WorkKind.RepairJoint or WorkKind.PatchPipe or WorkKind.ReplacePipe
            or WorkKind.RepairRadiator or WorkKind.RepairDoor or WorkKind.FixLights or WorkKind.RestoreCircuit or WorkKind.ReplacePanel or WorkKind.InstallSubstitute
            or WorkKind.RepairRobot or WorkKind.RepairNet or WorkKind.PreventiveCheck or WorkKind.Rewire or WorkKind.Reline or WorkKind.Calibrate => "repair",
        WorkKind.Fabricate or WorkKind.Upgrade or WorkKind.RestoreGrade or WorkKind.BuildOxygen or WorkKind.BuildComputer or WorkKind.BuildWorkshop => "craft",
        WorkKind.Cook => "cook",
        WorkKind.Treat or WorkKind.Rehab or WorkKind.FitProsthetic => "treat",
        _ => "",
    };
}
