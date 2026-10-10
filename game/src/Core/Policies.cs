using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v13.0 방침: 승무원 회의가 정하고, 중앙 컴퓨터와 사람은 그 안에서 움직인다.
// 방침 하나 = 주제 · 선택지 · 지금 값 · 정한 날과 찬반 · 근거. 사고 결과·정기 회의·사람이 바뀔 때 다시 본다 (v13.2).
// 가치관 기울기: 안전 → 신중한 쪽, 효율 → 배·결과 쪽, 사람 → 인명 쪽, 규칙 → 정해진 절차 쪽, 자유 → 재량 쪽.

public sealed record PolicySpec(string Id, string Area, string Name, string[] Options, int Default, string Note);

public sealed class PolicyChange
{
    public long Tick { get; init; }
    public string Id { get; init; } = "";
    public int From { get; init; }
    public int To { get; init; }
    public string Why { get; init; } = "";
    public int Yes { get; init; }
    public int No { get; init; }
}

public sealed class PolicySystem
{
    public static readonly PolicySpec[] All =
    {
        new("decompress", "재난", "감압 격벽", new[] { "사람 우선", "배 우선" }, 0,
            "감압된 방에 사람이 있으면 격벽을 2분 기다린다 ↔ 바로 닫는다"),
        new("vacuumfire", "재난", "진공 소화", new[] { "금지", "빈 방만", "대피 카운트다운 뒤", "컴퓨터 판단" }, 1,
            "불난 방의 공기를 바깥으로 빼서 끈다 — 확실하고 빠르지만 공기·작물을 잃는다"),
        new("inertfire", "재난", "질식 소화", new[] { "금지", "빈 방만", "경보 30초 뒤" }, 1,
            "불활성 가스로 산소를 몰아내 끈다 — 공기는 지키지만 가스가 한정돼 있고 안에 있으면 숨이 막힌다"),
        new("zoneabandon", "재난", "구역 포기 시점", new[] { "일찍", "보통", "끝까지" }, 0,
            "아무도 못 막는 새는 방을 언제 포기하나 (1시간 반 / 4시간 / 실링폼이 떨어질 때까지)"),
        new("risktaking", "재난", "위험 감수", new[] { "신중", "보통", "과감" }, 0,
            "위험한 방의 일: 2인 1조(한 명은 문 밖에서 지킨다) / 숨 쉴 수 없는 방만 2인 1조 / 혼자"),
        // v13.1 지휘·조직
        new("command", "지휘", "현장 지휘", new[] { "사람", "컴퓨터", "상황 따라" }, 2,
            "위기 때 누가 조를 짜나 — 함장(못 하면 다음 사람) / V 지휘 컴퓨터 / 컴퓨터가 멀쩡하고 더 믿을 만하면 컴퓨터"),
        new("rotation", "지휘", "위기 교대", new[] { "2시간", "4시간", "끝날 때까지" }, 1,
            "비상 일을 오래 한 사람을 대기조와 바꿔 재운다"),
        new("election", "지휘", "함장 선출", new[] { "직책 순서", "다수결", "경력" }, 1,
            "불신임으로 함장이 물러나면 누가 맡나"),
        new("noconfidence", "지휘", "함장 불신임", new[] { "과반", "3분의 2" }, 0,
            "신뢰가 무너진 함장을 저녁 회의에서 물러나게 하는 문턱"),
        new("suits", "자원", "우주복", new[] { "비상조 먼저", "먼저 쓰는 사람", "한 사람 한 벌" }, 0,
            "모자란 우주복을 누가 입나 — 위험한 방에 가는 조부터 / 먼저 집는 사람 / 사람마다 정해 둔 한 벌"),
        // v13.2 방침 1차 — 재난
        new("rescue", "재난", "구조", new[] { "무조건", "구조자 안전부터", "가망 있을 때만" }, 1,
            "쓰러진 사람을 언제 구하러 들어가나 — 우주복이 없어도 숨을 참고 / 구조자가 안전할 때 / 살 가망이 있는 사람부터(분류)"),
        new("evac", "재난", "대피 기준", new[] { "일찍", "보통", "버티며 작업" }, 1,
            "산소가 묽어지는 방에서 언제 일을 두고 나오나 (17 / 16.5 / 15kPa)"),
        new("reactor", "재난", "원자로 운전", new[] { "보수", "표준", "출력 유지" }, 1,
            "냉각이 흔들릴 때 — 작은 이상에도 세운다 / 정해진 대로 / 정지를 미루고 버틴다(노심이 닳는다)"),
        new("firemethod", "재난", "불 끄는 수단", new[] { "소화기", "자동 소화 먼저", "물도 쓴다" }, 0,
            "소화기로 / 자동 소화 장치가 있는 방은 장치가 끄게 기다린다 / 소화기가 없으면 물로 (누전 위험)"),
        new("jettison", "재난", "사출", new[] { "금지", "최후 수단", "적극" }, 1,
            "망가진 방을 떼어 내는 결정 — 하지 않는다 / 다른 길이 없을 때 / 일찍 떼어 낸다"),
        new("shed", "재난", "전원 차단", new[] { "넓게", "보통", "좁게" }, 1,
            "전기가 모자랄 때 회로를 언제 내리나 — 배터리가 넉넉할 때 일찍(안전) / 보통 / 바닥 가까이까지 버틴다(손해 적게)"),
        // v13.2 방침 1차 — 지휘·조직
        new("muster", "지휘", "비상 소집", new[] { "해당 조만", "모두 깨운다" }, 1,
            "위기 때 비번인 사람도 깨우나 — 조에 든 사람만 / 모두"),
        new("autoscope", "지휘", "컴퓨터 자동 실행", new[] { "경보만", "차단까지", "전부" }, 2,
            "컴퓨터가 스스로 해도 되는 일 — 알리기만 / 격벽·댐퍼까지 / 소화 수순까지"),
        new("controlseat", "지휘", "관제석", new[] { "위기 때 사람이 앉는다", "컴퓨터에 맡긴다" }, 1,
            "위기 때 관제석에 사람이 앉아 손으로 조종하나 — 평소엔 컴퓨터에 맡기고, 컴퓨터가 III 아래로 떨어지면 누구든 앉는다"),
        new("minutes", "지휘", "정보 공개", new[] { "회의록 모두 공개", "함장만" }, 0,
            "누가 어디에 표를 던졌는지 모두 아나 — 결정이 틀리면 비난이 찬성한 사람에게 / 함장에게"),
        // v13.2 방침 1차 — 자원
        new("rations", "자원", "식량 배급", new[] { "똑같이", "일하는 사람 먼저", "아픈 사람 먼저", "줄인다" }, 0,
            "먹을 것이 모자랄 때 — 모두 똑같이 줄인다 / 비상 일을 하는 사람은 덜 줄인다 / 다친 사람은 덜 줄인다 / 나흘치 아래면 미리 줄인다"),
        new("water", "자원", "물", new[] { "자유", "아낀다", "엄격" }, 0,
            "씻고 마시는 물 — 마음껏 / 샤워·세탁을 줄인다 / 마실 만큼만 (날카로워진다)"),
        new("stock", "자원", "비축", new[] { "적게", "보통", "많이" }, 1,
            "수리재·부품을 얼마나 쌓아 두나 (제작 목표 · 개조에 손대지 않는 몫)"),
        new("cannibalize", "자원", "부품 뜯기", new[] { "금지", "필수 아닌 것만", "필요하면 무엇이든" }, 1,
            "부품이 바닥났을 때 다른 설비에서 뜯나 — 뜯지 않는다 / 덜 중요한 설비에서만 / 정수기라도"),
        new("portspend", "자원", "기항지 지출", new[] { "아낀다", "보통", "넉넉히" }, 1,
            "기항지에서 돈을 얼마나 쓰나 — 꼭 필요한 것만 / 보통 / 넉넉히 사고 낡은 설비도 손본다"),
        new("medicine", "자원", "의약품", new[] { "아낀다", "필요한 대로" }, 1,
            "구급 키트 — 크게 다친 사람에게만 / 다치면 바로"),
        // v13.4 방침 2차 — 일·생활
        new("shifts", "생활", "근무", new[] { "3교대", "2교대", "각자 일과" }, 2,
            "잠자는 시간을 배 전체로 맞추나 — 여덟 시간씩 셋 / 열두 시간씩 둘 / 사람마다 제 일과"),
        new("nightwatch", "생활", "야간 당직", new[] { "1명", "2명", "컴퓨터에 맡긴다" }, 2,
            "밤(0~6시)에 깨어 배를 도는 사람 — 데이터선이 끊긴 방의 사고도 눈으로 찾는다"),
        new("maint", "생활", "정비", new[] { "예방 먼저", "고장 나면" }, 0,
            "닳기 전에 손보나 — 정비·전조 점검을 미리 / 고장 난 뒤에 (일은 적지만 고장이 잦다)"),
        new("upgrades", "생활", "개조", new[] { "보수", "균형", "적극" }, 1,
            "배를 얼마나 자주 고치나 — 개조 사이를 두 배로 / 보통 / 절반으로"),
        new("research", "생활", "연구 방향", new[] { "균형", "안전", "효율", "탐사" }, 0,
            "다음 연구를 고를 때 — 겪은 일대로 / 생활·의료·선체 / 전력·제작·컴퓨터 / 추진·감지기·로봇"),
        new("drills", "생활", "비상 훈련", new[] { "없음", "주 1회", "이틀마다" }, 1,
            "우주복·격벽·소화 훈련 — 훈련한 사람은 우주복을 빨리 입는다"),
        new("leisure", "생활", "휴식·여가", new[] { "일 먼저", "균형", "여가 보장" }, 1,
            "비번에 쉬는 것과 일하는 것 — 일을 더 하지만 지친다 / 보통 / 잘 쉬지만 일이 밀린다"),
        new("privacy", "생활", "사생활", new[] { "공용", "개인 공간 존중" }, 0,
            "침실은 모두의 공간인가 — 존중하면 자는 사람을 웬만해서는 깨우지 않고 혼자 쉬면 더 풀린다"),
        // v13.4 방침 2차 — 사회·문화
        new("funeral", "사회", "장례", new[] { "우주장", "안치 후 기항지", "재순환" }, 1,
            "죽은 사람을 어떻게 보내나 — 에어락으로 별에 / 안치실에 모셔 기항지로 / (세대선) 물과 흙으로 되돌린다"),
        new("memorial", "사회", "추모", new[] { "기념일", "조용히" }, 0,
            "떠난 사람을 기억하는 방식 — 날마다 정기 회의에서 이름을 부른다 / 각자 마음속으로"),
        new("conflict", "사회", "갈등 해결", new[] { "중재", "함장 판단", "그냥 둔다" }, 0,
            "말다툼이 나면 — 곁에 있는 사람이 달랜다 / 함장이 한쪽 손을 들어 준다 / 저희끼리 풀게 둔다"),
        new("violations", "사회", "규칙 위반", new[] { "경고", "근무 박탈", "없음" }, 0,
            "명령 무시·숨긴 실수가 드러나면 — 경고 / 여덟 시간 근무에서 뺀다 / 넘어간다 (무거운 벌은 실수를 숨기게 만든다)"),
        new("newcrew", "사회", "새 승무원", new[] { "바로 동등", "수습 기간" }, 0,
            "구조하거나 기항지에서 태운 사람 — 처음부터 표를 던진다 / 사흘 동안 회의에서 표가 없고 위험한 조에 넣지 않는다"),
        // v13.4 방침 2차 — 세대선
        new("education", "세대선", "아이 교육", new[] { "실무", "학교", "자유" }, 1,
            "아이들이 무엇을 배우나 — 어른 곁에서 일 / 학교에서 고르게 / 마음대로 (덜 배우지만 덜 지친다)"),
        new("birth", "세대선", "짝·출산", new[] { "자유", "수용력에 맞춘 허가" }, 0,
            "아이를 가질지 — 짝이 정한다 / 침대와 먹을 것이 넉넉할 때만"),
        new("elders", "세대선", "노인", new[] { "은퇴", "일할 수 있는 만큼" }, 1,
            "예순다섯이 넘은 사람 — 급한 일 말고는 쉰다 / 할 수 있는 만큼 일한다"),
        // v13.4 방침 2차 — 항해·외부
        new("route", "항해", "항로 성향", new[] { "보통", "안전", "빠르게", "탐사" }, 0,
            "구간을 고를 때 — 그때그때 / 소행성대·방사선대를 피한다 / 순항을 줄인다 / 난파선·성운을 찾아간다"),
        new("distress", "항해", "구조 신호", new[] { "늘 간다", "여유 있을 때", "무시" }, 1,
            "남의 구조 신호를 들으면 — 늘 응한다 / 배가 넉넉할 때만 / 응하지 않는다"),
        new("wrecks", "항해", "난파선", new[] { "조심", "적극 건진다" }, 0,
            "난파선을 지날 때 — 바깥만 훑는다 / 안까지 들어가 더 건지지만 위험하다"),
        new("comms", "항해", "교신", new[] { "정기 보고", "필요할 때만" }, 1,
            "본부·기항지와의 교신 — 날마다 보고한다(전기를 쓰지만 기항지가 반긴다) / 필요할 때만"),
        new("doorfire", "재난", "화재 때 출입 통제", new[] { "자동 해제", "그대로" }, 0,
            "불이 나면 카드 · 지문 · 함장 승인 문을 모두 푸나 — 사람이 빠져나가고 들어가기 쉽다 ↔ 약품고 · 원자로실은 잠긴 채로"), // v16.3
        // v16.6 제안 → 승인
        new("computerask", "지휘", "컴퓨터 제안", new[] { "바로 실행", "위험한 조치는 묻는다", "모두 묻는다" }, 0,
            "컴퓨터가 위험한 조치(진공·질식 소화)를 바로 하나 — 바로 / 제안 카드를 내고 받거나 거절을 기다린다(기한이 지나면 지휘하는 사람이 정한다) / 일 쉬기·재부팅까지 묻는다"),
        // v16.12 원정 (관찰자 지시 — 화면의 원정 창이 바꾼다)
        new("expedition", "원정", "원정 결정", new[] { "회의·함장에게 맡김", "당장 보낸다", "보내지 않는다" }, 0,
            "재료가 바닥나 배가 멈추면 — 회의와 함장이 정한다 / 관찰자 지시로 바로 보낸다 / 보내지 않는다(멈춘 채 버틴다)"),
        new("expsite", "원정", "원정 목적지", new[] { "알아서", "1번 후보", "2번 후보", "3번 후보", "4번 후보", "5번 후보", "6번 후보" }, 0,
            "센서가 찾은 후보 중 어디로 — 번호가 없어지면 다시 알아서"),
    };

    /// <summary>
    /// 가치관마다 기우는 선택지 (첫 출항 회의·정기 회의). 없는 가치관은 처음 값 쪽. Bold: 번호가 클수록 과감하면 +1, 작을수록 과감하면 -1.
    /// </summary>
    private static readonly Dictionary<string, (int[] lean, int bold)> Leans = new()
    {
        //                    Safety Efficiency People Rules Freedom
        ["decompress"] = (new[] { 1, 1, 0, 0, 0 }, 0),
        ["vacuumfire"] = (new[] { 1, 2, 1, 1, 1 }, 1),
        ["inertfire"] = (new[] { 1, 2, 1, 1, 1 }, 1),
        ["zoneabandon"] = (new[] { 0, 0, 1, 0, 2 }, 1),
        ["risktaking"] = (new[] { 0, 1, 0, 0, 2 }, 1),
        ["command"] = (new[] { 2, 1, 0, 2, 0 }, 0),
        ["rotation"] = (new[] { 0, 2, 0, 1, 1 }, 1),
        ["election"] = (new[] { 2, 2, 1, 0, 1 }, 0),
        ["noconfidence"] = (new[] { 1, 1, 0, 1, 0 }, 0),
        ["suits"] = (new[] { 0, 0, 2, 2, 1 }, 0),
        ["rescue"] = (new[] { 1, 2, 0, 1, 0 }, -1),
        ["evac"] = (new[] { 0, 2, 0, 1, 1 }, 1),
        ["reactor"] = (new[] { 0, 2, 1, 1, 1 }, 1),
        ["firemethod"] = (new[] { 1, 2, 0, 0, 0 }, 1),
        ["jettison"] = (new[] { 2, 1, 1, 1, 0 }, 0),
        ["shed"] = (new[] { 0, 2, 1, 1, 1 }, 1),
        ["muster"] = (new[] { 1, 0, 1, 1, 0 }, 0),
        ["autoscope"] = (new[] { 2, 2, 1, 2, 0 }, 0),
        ["controlseat"] = (new[] { 1, 1, 1, 1, 0 }, 0), // 멀쩡한 컴퓨터(IV · V)에 맡기자 — 손으로 잡고 싶어 하는 건 자유를 중시하는 사람
        ["minutes"] = (new[] { 0, 1, 0, 0, 0 }, 0),
        ["rations"] = (new[] { 3, 1, 2, 0, 0 }, 0),
        ["water"] = (new[] { 1, 1, 0, 2, 0 }, 0),
        ["stock"] = (new[] { 2, 0, 1, 1, 0 }, 0),
        ["cannibalize"] = (new[] { 1, 2, 1, 0, 2 }, 0),
        ["portspend"] = (new[] { 0, 1, 2, 0, 2 }, 0),
        ["medicine"] = (new[] { 1, 0, 1, 0, 1 }, 0),
        //                    Safety Efficiency People Rules Freedom
        ["shifts"] = (new[] { 0, 1, 2, 0, 2 }, 0),
        ["nightwatch"] = (new[] { 1, 2, 0, 1, 2 }, 0),
        ["maint"] = (new[] { 0, 0, 0, 0, 1 }, 0),
        ["upgrades"] = (new[] { 0, 2, 1, 0, 2 }, 1),
        ["research"] = (new[] { 1, 2, 1, 0, 3 }, 0),
        ["drills"] = (new[] { 2, 1, 1, 2, 0 }, 0),
        ["leisure"] = (new[] { 1, 0, 2, 1, 2 }, 0),
        ["privacy"] = (new[] { 0, 0, 1, 0, 1 }, 0),
        ["funeral"] = (new[] { 1, 2, 1, 1, 0 }, 0),
        ["memorial"] = (new[] { 0, 1, 0, 0, 1 }, 0),
        ["conflict"] = (new[] { 0, 1, 0, 1, 2 }, 0),
        ["violations"] = (new[] { 0, 1, 0, 1, 2 }, 0),
        ["newcrew"] = (new[] { 1, 0, 0, 1, 0 }, 0),
        ["education"] = (new[] { 1, 0, 1, 1, 2 }, 0),
        ["birth"] = (new[] { 1, 1, 0, 1, 0 }, 0),
        ["elders"] = (new[] { 0, 1, 0, 1, 1 }, 0),
        ["route"] = (new[] { 1, 2, 0, 0, 3 }, 0),
        ["distress"] = (new[] { 1, 1, 0, 1, 1 }, 0),
        ["wrecks"] = (new[] { 0, 1, 0, 0, 1 }, 0),
        ["comms"] = (new[] { 0, 1, 1, 0, 1 }, 0),
    };

    /// <summary>이 사람이 바라는 선택지 (가치관 + 대담함 + 겪은 일).</summary>
    public static int Preferred(CrewMember c, string id)
    {
        var spec = Spec(id);
        if (id == "computerask" && c.ComputerFaith >= 0f) return c.ComputerFaith < 0.2f ? 2 : c.ComputerFaith < 0.42f ? 1 : 0; // v16.6 컴퓨터에 데인 사람은 묻게 하자고 한다
        if (!Leans.TryGetValue(id, out var l)) return spec.Default;
        int v = l.lean[(int)c.Value];
        if (l.bold != 0)
        {
            // 아주 대담하면 한 칸 과감하게, 겁이 많거나 크게 데인 적이 있으면 한 칸 신중하게
            if (c.Traits.Bravery > 0.78f) v += l.bold;
            else if (c.Traits.Bravery < 0.22f || c.Memory.Trauma > 0.35f) v -= l.bold;
        }
        return Math.Clamp(v, 0, spec.Options.Length - 1);
    }

    /// <summary>방침마다 말에 무게가 실리는 솜씨 (전문성).</summary>
    public static float Expertise(CrewMember c, string id) => id switch
    {
        "decompress" or "zoneabandon" or "jettison" => MathF.Max(c.SkillLevel(Skill.Mechanics), c.SkillLevel(Skill.Engineering)),
        "vacuumfire" or "inertfire" or "firemethod" or "risktaking" => MathF.Max(c.SkillLevel(Skill.Mechanics), 0.5f * c.Stats.Emergencies / 20f),
        "rescue" or "evac" or "medicine" => c.SkillLevel(Skill.Medicine),
        "reactor" or "shed" or "autoscope" or "controlseat" => MathF.Max(c.SkillLevel(Skill.Engineering), c.SkillLevel(Skill.Electrical)),
        "rations" or "water" => c.SkillLevel(Skill.Cooking),
        "maint" or "upgrades" or "research" => MathF.Max(c.SkillLevel(Skill.Mechanics), c.SkillLevel(Skill.Engineering)),
        "route" or "distress" or "wrecks" or "comms" => c.SkillLevel(Skill.Piloting),
        "funeral" or "memorial" or "education" or "birth" or "elders" => c.SkillLevel(Skill.Medicine),
        "stock" or "cannibalize" or "portspend" or "suits" => c.SkillLevel(Skill.Mechanics),
        _ => CommandSystem.Leadership(c),
    };

    public static PolicySpec Spec(string id) => All.First(p => p.Id == id);

    private readonly World _w;
    private readonly Dictionary<string, int> _value = new();
    private readonly Dictionary<string, long> _setAt = new();
    public List<PolicyChange> Changes { get; } = new();

    public PolicySystem(World w)
    {
        _w = w;
        foreach (var p in All) _value[p.Id] = p.Default;
    }

    public int this[string id]
    {
        get => _value.TryGetValue(id, out var v) ? v : Spec(id).Default;
    }

    public string Option(string id) => Spec(id).Options[Math.Clamp(this[id], 0, Spec(id).Options.Length - 1)];

    /// <summary>방침을 바꾼다 (회의·시험). 바뀐 이력이 남는다.</summary>
    public void Set(string id, int value, string why = "", int yes = 0, int no = 0)
    {
        var spec = Spec(id);
        value = Math.Clamp(value, 0, spec.Options.Length - 1);
        int from = this[id];
        _value[id] = value;
        Sync();
        if (from == value) return;
        _setAt[id] = _w.Tick;
        Changes.Add(new PolicyChange { Tick = _w.Tick, Id = id, From = from, To = value, Why = why, Yes = yes, No = no });
    }

    /// <summary>시험용: 모든 방침을 처음 값으로 (첫 출항 회의가 바꾼 것을 되돌린다).</summary>
    public void ResetDefaults()
    {
        foreach (var p in All) _value[p.Id] = p.Default;
        Sync();
    }

    /// <summary>방침이 다른 시스템의 값으로 이어지는 것 (비축 목표).</summary>
    private void Sync() => _w.History.Doctrine.StockScale = this["stock"] switch { 0 => 0.75f, 2 => 1.35f, _ => 1f };

    /// <summary>마지막으로 바꾼 틱 (-1: 처음 그대로).</summary>
    public long SetAt(string id) => _setAt.TryGetValue(id, out var t) ? t : -1;

    /// <summary>구역 포기까지 기다리는 시간 (시간). 끝까지면 무한.</summary>
    public float AbandonHours => this["zoneabandon"] switch { 0 => 1.5f, 1 => 4f, _ => float.PositiveInfinity };
}
