using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.6 방 70종: 표(데이터)로 둔다. 새 방은 본래 방의 기능을 이어받고(격리실 = 의무실 기능 + 옮김을 막는다),
// 이름·색·설명서·소음·냄새·특수 효과만 더한다. 새 방이 없으면 본래 방이 대신한다 — 겸용의 대가(효율이 떨어진다).

[Flags]
public enum RoomTag
{
    None = 0,
    Quiet = 1,      // 소리를 막는다 (방음)
    Shielded = 2,   // 방사선을 막는다 (물벽·차폐)
    Wet = 4,        // 물을 쓴다 (습하다)
    Explosive = 8,  // 터질 것이 있다 (가스·추진제·수소)
    Cold = 16,      // 차게 둔다
    Sleep = 32,     // 자는 방
    Rest = 64,      // 쉬는 방
}

/// <summary>방 하나의 표 한 줄. Noise/Vibration/Smell/Radiation은 이 방이 내는 것 (옆방으로 절반쯤 번진다).</summary>
public sealed record RoomSpec(RoomType Kind, RoomType Base, string Name, int Tier, string Color, string Short, RoomTag Tags,
    float Noise, float Vibration, float Smell, float Radiation, string Furnish, CodexEntry Codex);

/// <summary>배가 해야 하는 일 하나: 가장 잘하는 방 → 대신할 수 있는 방(효율).</summary>
public sealed record ShipFunction(string Key, string Name, RoomType[] Best, (RoomType kind, float factor)[] Fallback, string Missing);

public static class RoomCatalog
{
    private static CodexEntry E(string what, string how, string needs, string stopped, string care, string danger) => new(what, how, needs, stopped, care, danger);

    // Furnish: 생성기가 채울 설비 — "글자 가로×세로 개수" 공백으로 (예: "M1x2x2 C1x1x1")
    private static readonly RoomSpec[] Specs =
    {
        // ── 표준 (소형·중형 배에도) ──
        new(RoomType.WaterPlant, RoomType.LifeSupport, "정수실", 1, "#5fb8d9", "WP", RoomTag.Wet, 0.2f, 0.1f, 0f, 0f, "U2x2x2",
            E("정수실. 쓰고 버린 물·오줌·땀을 다시 마실 물로.", "여과 → 증류 → 살균. 생명유지실에서 물 쪽만 떼어 낸 방.", "전기, 급수 본관.",
              "물이 다시 돌지 않는다 — 탱크가 바닥날 때까지만 버틴다.", "여과막을 갈고 배관 이음새를 본다.", "누수 → 바닥 침수 → 누전.")),
        new(RoomType.Laundry, RoomType.Storage, "세탁실", 1, "#9cc3d5", "LD", RoomTag.Wet, 0.3f, 0.2f, 0.15f, 0f, "K2x2x1",
            E("세탁실. 옷과 시트를 빤다.", "물을 아껴 쓰는 세탁기와 건조기.", "급수, 전기.", "옷에서 냄새가 난다 — 조금씩 예민해진다.",
              "보풀 거름망 청소.", "건조기 보풀 화재, 누수.")),
        new(RoomType.Freezer, RoomType.Storage, "냉동 창고", 1, "#bfe3f5", "FZ", RoomTag.Cold, 0.15f, 0f, 0f, 0f, "F2x1x2 K2x2x1",
            E("냉동 창고. 오래 둘 식량과 약.", "단열 벽과 압축기.", "전기 (끊기면 몇 시간 뒤부터 녹는다).",
              "식량이 상한다.", "문을 꼭 닫고 성에를 제거한다.", "냉매 누출, 갇히면 저체온.")),
        new(RoomType.BatteryRoom, RoomType.Power, "배터리실", 1, "#e8d35a", "BT", RoomTag.Explosive, 0.15f, 0f, 0f, 0f, "Y2x2x4",
            E("배터리실. 원자로가 멈췄을 때 배를 살리는 저장 전기.", "배터리 묶음과 충방전 제어기.", "환기 (충전 때 가스가 나온다).",
              "원자로가 멈추면 곧바로 정전.", "셀 온도와 전압을 본다.", "열폭주 — 불이 번지고 유독 가스.")),
        new(RoomType.EvaPrep, RoomType.Storage, "선외 준비실", 1, "#aebfd6", "EV", RoomTag.None, 0.1f, 0f, 0f, 0f, "L1x2x2 K2x2x1",
            E("선외 준비실. 우주복을 입고 점검하고, 도구를 챙긴다.", "우주복 거치대와 점검대.", "전기 (충전), 산소 보충.",
              "에어록 안에서 입어야 해서 나가는 데 오래 걸린다.", "우주복 봉인 점검.", "봉인이 안 된 우주복으로 나가면 위험.")),
        new(RoomType.Morgue, RoomType.Storage, "안치실", 1, "#8a93a8", "MG", RoomTag.Cold | RoomTag.Quiet, 0f, 0f, 0.1f, 0f, "K2x2x1",
            E("안치실. 떠난 사람을 모신다.", "차게 유지하는 서랍.", "전기 (냉각).", "시신을 둘 곳이 없다 — 산 사람이 오래 힘들어한다.",
              "조용히 둔다.", "없음.")),
        new(RoomType.Quarantine, RoomType.Medbay, "격리실", 1, "#f09bb0", "QR", RoomTag.Quiet, 0f, 0f, 0f, 0f, "M1x2x2 C1x1x1",
            E("격리실. 옮는 병에 걸린 사람을 따로 돌본다.", "음압 — 공기가 밖으로 새지 않게 안쪽 기압을 조금 낮춘다.", "전기, 환기, 의무관.",
              "의무실에서 함께 지낸다 — 병이 덜 막힌다.", "문을 닫고, 들어갈 땐 마스크.", "음압이 깨지면 병이 복도로.")),
        new(RoomType.Shelter, RoomType.Storage, "방사선 대피소", 1, "#b7a4e0", "SH", RoomTag.Shielded, 0f, 0f, 0f, 0f, "K2x2x2 B1x2x2",
            E("방사선 대피소. 태양 폭풍 때 몇 시간 숨는 곳.", "물탱크와 식량 상자로 벽을 두껍게 둘렀다.", "없음.",
              "창고 선반 뒤에 숨는다 — 덜 막아 준다.", "대피 훈련.", "좁다 — 오래 있으면 답답하다.")),
        new(RoomType.Recycling, RoomType.Workshop, "재활용실", 1, "#9fae7a", "RC", RoomTag.None, 0.35f, 0.2f, 0.55f, 0f, "N2x2x1 K1x3x1",
            E("재활용실. 부서진 것·쓰레기에서 쓸 것을 되살린다.", "분쇄·선별·녹이기.", "전기.",
              "정비실이 대신한다 — 느리다.", "선별기 청소.", "냄새, 먼지 화재.")),
        new(RoomType.DroneBay, RoomType.Storage, "드론 격납고", 1, "#9eb2c9", "DB", RoomTag.None, 0.25f, 0f, 0f, 0f, "Q1x2x2",
            E("드론 격납고. 선외 드론이 쉬고 충전한다.", "도킹 거치대와 충전기.", "전기, 자재.", "에어록에서 드론을 다룬다 — 느리다.",
              "도킹 커넥터 점검.", "없음.")),
        new(RoomType.RobotBay, RoomType.Workshop, "로봇 정비소", 1, "#c7ad7d", "RB", RoomTag.None, 0.2f, 0f, 0f, 0f, "J1x2x2 W3x1x1",
            E("로봇 정비소. 선내 로봇이 충전하고 고쳐진다.", "충전대와 작업대.", "전기.", "정비실 구석에서 고친다.", "관절 기름칠.", "없음.")),
        new(RoomType.Gym, RoomType.Lounge, "체력단련실", 1, "#d88fc2", "GY", RoomTag.Rest, 0.4f, 0.35f, 0.1f, 0f, "S2x1x2",
            E("체력단련실. 무중력·저중력에서 근육과 뼈를 지킨다.", "저항 운동 기구와 러닝머신.", "전기.",
              "휴게실에서 맨몸 운동 — 덜 된다. 오래 안 하면 쉽게 다친다.", "기구 점검.", "쿵쿵거린다 — 옆 침실이 못 잔다.")),
        new(RoomType.QuietQuarters, RoomType.Quarters, "조용한 침실", 1, "#9aa7ee", "QQ", RoomTag.Quiet | RoomTag.Sleep, 0f, 0f, 0f, 0f, "B1x2x4",
            E("조용한 침실. 방음벽을 둘렀다.", "이중 벽과 진동을 먹는 바닥.", "전기, 공기.", "그냥 침실에서 잔다.", "없음.", "경보를 조금 늦게 듣는다.")),
        new(RoomType.PartsPrep, RoomType.Workshop, "부품 준비실", 1, "#c2a878", "PP", RoomTag.None, 0.15f, 0f, 0f, 0f, "W3x1x1 K1x3x1",
            E("부품 준비실. 정비 전에 부품을 꺼내 맞추고 시험한다.", "시험대와 부품 선반.", "전기.", "정비실에서 한다 — 정비가 조금 느리다.", "정리.", "없음.")),

        // ── 대형 배 ──
        new(RoomType.FuelCell, RoomType.Power, "연료전지실", 2, "#e3c75a", "FC", RoomTag.Explosive, 0.15f, 0f, 0f, 0f, "Z2x1x2",
            E("연료전지실. 수소와 산소로 전기를 만든다 (예비 전력).", "연료전지 묶음.", "수소·산소 (생명유지실에서).", "예비 전력이 줄어든다.",
              "수소 누출 감지기 점검.", "수소 누출 → 폭발.")),
        new(RoomType.EscapeBay, RoomType.Storage, "탈출정 격납고", 2, "#9aa5bd", "ES", RoomTag.None, 0f, 0f, 0f, 0f, "K2x2x1",
            E("탈출정 격납고. 배를 버려야 할 때.", "탈출정과 발사 레일.", "전기 (탈출정 충전).", "탈출할 수 없다.", "월 1회 점검.", "없음.")),
        new(RoomType.ServerRoom, RoomType.Comms, "서버실", 2, "#7fe0c0", "SV", RoomTag.None, 0.3f, 0f, 0f, 0f, "I2x2x1 C1x1x1",
            E("서버실. 주 컴퓨터의 두뇌와 기록.", "서버 선반과 냉각.", "전기, 냉각, 데이터선.", "주 컴퓨터가 함교에서 버틴다 — 덜 똑똑하다.",
              "먼지·온도 관리.", "과열, 누수 → 누전.")),
        new(RoomType.Calibration, RoomType.Workshop, "교정실", 2, "#cfae6d", "CL", RoomTag.Quiet, 0f, 0f, 0f, 0f, "W3x1x1",
            E("교정실. 감지기를 기준값에 맞춘다.", "기준 가스·기준 저항·진동 없는 받침대.", "전기.", "현장에서 교정 — 덜 정확하다.", "기준물 교체.", "없음.")),
        new(RoomType.Decon, RoomType.Storage, "제염실", 2, "#a6d0cf", "DC", RoomTag.Wet, 0.1f, 0f, 0.1f, 0f, "L1x2x1",
            E("제염실. 선외에서 묻혀 온 먼지·방사성 입자를 씻는다.", "샤워와 흡진기.", "급수, 전기.", "먼지가 배 안으로 들어온다.", "필터 교체.", "없음.")),
        new(RoomType.Observatory, RoomType.Lounge, "관측실", 2, "#8fb3f0", "OB", RoomTag.Rest | RoomTag.Quiet, 0f, 0f, 0f, 0f, "S2x1x2 C1x1x1",
            E("관측실. 큰 창 너머로 별을 본다.", "두꺼운 창과 망원경.", "없음.", "휴게실 화면으로 본다 — 마음이 덜 풀린다.",
              "창 점검 (금이 가면 위험).", "창이 가장 약한 벽 — 운석.")),
        new(RoomType.Lab, RoomType.Workshop, "연구실", 2, "#b6c96a", "LB", RoomTag.None, 0.05f, 0f, 0.05f, 0f, "W3x1x1 C1x1x1",
            E("연구실. 새 기술을 시험한다.", "시험대·분석기·기록.", "전기, 데이터선.", "정비실 구석에서 연구 — 느리다.", "시료 관리.", "시험 중 사고.")),
        new(RoomType.SeedVault, RoomType.Storage, "종자 보관소", 2, "#9fc97a", "SD", RoomTag.Cold | RoomTag.Quiet, 0f, 0f, 0f, 0f, "K2x2x2",
            E("종자 보관소. 모든 작물의 씨앗 원본.", "차고 마른 서랍.", "전기 (냉각).", "재배실이 망하면 다시 시작할 씨앗이 없다.", "습도 관리.", "없음.")),
        new(RoomType.Chapel, RoomType.Lounge, "기도실", 2, "#c9b1e8", "CP", RoomTag.Rest | RoomTag.Quiet, 0f, 0f, 0f, 0f, "S2x1x2",
            E("기도실. 슬픔과 두려움을 내려놓는 곳.", "조용한 방과 작은 불빛.", "없음.", "마음을 추스를 곳이 없다 — 상처가 오래 간다.",
              "없음.", "없음.")),
        new(RoomType.Cargo, RoomType.Storage, "화물칸", 2, "#8f98ad", "CG", RoomTag.None, 0f, 0f, 0f, 0f, "K2x2x4",
            E("화물칸. 교역품과 큰 짐.", "고정 끈과 레일.", "없음.", "창고가 비좁다.", "짐 고정.", "가속 때 짐이 풀리면 다친다.")),
        new(RoomType.DockingBay, RoomType.Storage, "도킹 포트", 2, "#a3b4cc", "DK", RoomTag.None, 0.3f, 0.1f, 0f, 0f, "L1x2x1",
            E("도킹 포트. 다른 배·기항지와 이어 붙는다.", "도킹 고리와 기밀 통로.", "전기.", "에어록으로 오간다 — 짐을 못 옮긴다.",
              "고리 봉인 점검.", "도킹 중 충돌.")),
        new(RoomType.PropellantTank, RoomType.Storage, "추진제 탱크실", 2, "#e0a070", "PT", RoomTag.Explosive, 0.05f, 0f, 0f, 0f, "K2x2x1",
            E("추진제 탱크실. 엔진이 쓸 물과 가스.", "고압 탱크와 밸브.", "없음.", "엔진이 오래 못 탄다.", "밸브 점검.", "파열 → 폭발·파편.")),
        new(RoomType.HvacRoom, RoomType.LifeSupport, "공조실", 2, "#5ccab8", "HV", RoomTag.None, 0.45f, 0.25f, 0f, 0f, "O2x2x1",
            E("공조실. 배 전체의 공기를 섞고 데우고 식힌다.", "큰 송풍기와 열교환기.", "전기.", "생명유지실이 대신한다 — 방마다 공기가 고르지 않다.",
              "필터·벨트 점검.", "덕트 화재.")),
        new(RoomType.PumpRoom, RoomType.Cooling, "펌프실", 2, "#6fcfe8", "PM", RoomTag.Wet, 0.5f, 0.45f, 0f, 0f, "P2x2x2",
            E("펌프실. 냉각수를 돌리는 큰 펌프들.", "펌프와 열교환기.", "전기, 급수.", "냉각실이 모두 떠맡는다.", "베어링·실 점검.", "누수, 진동.")),
        new(RoomType.Substation, RoomType.Power, "변전실", 2, "#eed65a", "SS", RoomTag.None, 0.25f, 0f, 0f, 0f, "X3x1x1",
            E("변전실. 먼 구역으로 전기를 나눠 보낸다.", "변압기와 차단기.", "간선.", "먼 방의 전압이 떨어진다.", "절연 점검.", "누전·아크 화재.")),
        new(RoomType.GasStorage, RoomType.Storage, "가스 저장실", 2, "#b5c2a8", "GS", RoomTag.Explosive, 0f, 0f, 0f, 0f, "K2x2x2",
            E("가스 저장실. 산소·질소·소화 가스 예비.", "고압 실린더.", "없음.", "예비 공기가 줄어든다.", "밸브·압력계 점검.", "파열.")),
        new(RoomType.SuppressionRoom, RoomType.Storage, "소화 설비실", 2, "#e07f7f", "SP", RoomTag.None, 0f, 0f, 0f, 0f, "K2x2x1",
            E("소화 설비실. 불을 끄는 가스와 물을 방마다 보낸다.", "소화 가스 탱크와 분배 밸브.", "데이터선 (원격으로 쏜다).",
              "사람이 소화기를 들고 뛴다 — 불이 더 번진다.", "탱크 압력 점검.", "잘못 쏘면 사람이 숨막힌다.")),
        new(RoomType.AlgaeLab, RoomType.Hydroponics, "조류 배양실", 2, "#7fc36a", "AL", RoomTag.Wet, 0.1f, 0f, 0.3f, 0f, "G4x1x2",
            E("조류 배양실. 조류로 산소와 단백질을.", "빛을 받는 배양관.", "빛(전기), 물.", "재배실만으로 먹는다.", "배양액 관리.", "오염되면 한꺼번에 죽는다.")),
        new(RoomType.ElectronicsLab, RoomType.Workshop, "전자 작업실", 2, "#d2b36c", "EL", RoomTag.None, 0.05f, 0f, 0.05f, 0f, "W3x1x1",
            E("전자 작업실. 회로·감지기·제어기를 고친다.", "납땜대와 계측기.", "전기.", "정비실에서 — 섬세한 수리가 느리다.", "정전기 관리.", "작은 불.")),
        new(RoomType.WeldingShop, RoomType.Workshop, "용접실", 2, "#e0a060", "WD", RoomTag.None, 0.5f, 0.1f, 0.3f, 0f, "W3x1x1",
            E("용접실. 선체판과 구조재를 붙인다.", "용접기와 환기 후드.", "전기, 환기.", "정비실에서 — 느리다.", "후드 청소.", "불티 → 화재, 연기.")),
        new(RoomType.Crusher, RoomType.Workshop, "파쇄실", 2, "#b39a70", "CR", RoomTag.None, 0.75f, 0.7f, 0.2f, 0f, "N2x2x1",
            E("파쇄실. 운석 조각·고철을 부순다.", "분쇄기.", "전기.", "정제기가 통째로 받는다 — 느리다.", "날 교체.", "시끄럽고 흔들린다 — 옆방이 괴롭다.")),
        new(RoomType.Triage, RoomType.Medbay, "응급 처치실", 2, "#f29aa0", "TR", RoomTag.None, 0f, 0f, 0f, 0f, "M1x2x2",
            E("응급 처치실. 다친 사람을 먼저 살린다.", "처치대와 구급 선반.", "전기, 의무관.", "의무실이 붐빈다.", "구급 키트 채우기.", "없음.")),
        new(RoomType.PrivateCabins, RoomType.Quarters, "개인 선실", 2, "#a3aef0", "PC", RoomTag.Quiet | RoomTag.Sleep, 0f, 0f, 0f, 0f, "B1x2x2",
            E("개인 선실. 한두 사람의 방.", "문 달린 칸.", "공기, 전기.", "여럿이 한방에서 잔다 — 덜 쉰다.", "없음.", "없음.")),
        new(RoomType.MeetingRoom, RoomType.Mess, "회의실", 2, "#e3bd7a", "MT", RoomTag.None, 0.05f, 0f, 0f, 0f, "T2x1x2 S2x1x4",
            E("회의실. 방침을 정하고 투표한다.", "큰 탁자와 화면.", "없음.", "식당에서 회의 — 오래 끈다.", "없음.", "없음.")),
        new(RoomType.Navigation, RoomType.Comms, "항법실", 2, "#7dd9b8", "NV", RoomTag.Quiet, 0f, 0f, 0f, 0f, "C1x1x2",
            E("항법실. 항로를 계산하고 별을 잰다.", "항법 콘솔과 별 추적기.", "전기, 데이터선.", "함교가 겸한다 — 항로 계산이 느리다.", "없음.", "없음.")),
        new(RoomType.BackupBridge, RoomType.Bridge, "예비 함교", 2, "#6aaee0", "BB", RoomTag.None, 0f, 0f, 0f, 0f, "C1x1x2 S1x1x1",
            E("예비 함교. 함교를 잃었을 때 배를 조종한다.", "조타 콘솔 한 벌.", "전기, 데이터선.", "함교를 잃으면 엔진실에서만 조종.", "월 1회 시험.", "없음.")),

        // ── 세대선·거대선 ──
        new(RoomType.ProteinFarm, RoomType.Hydroponics, "단백질 농장", 3, "#b7c46a", "PF", RoomTag.Wet, 0.1f, 0f, 0.55f, 0f, "G4x1x2",
            E("단백질 농장. 곤충·배양육으로 단백질을.", "사육 상자와 배양조.", "물, 전기, 먹이.", "채소만 먹는다 — 오래 가면 기운이 없다.",
              "위생 관리.", "냄새, 병.")),
        new(RoomType.WaterWallCabin, RoomType.Quarters, "물벽 선실", 3, "#8fb0f0", "WW", RoomTag.Shielded | RoomTag.Sleep | RoomTag.Quiet, 0f, 0f, 0f, 0f, "B1x2x4",
            E("물벽 선실. 벽 속의 물이 방사선을 막는다.", "벽 안에 물주머니.", "급수.", "그냥 침실 — 태양 폭풍 때 대피소로 가야 한다.",
              "물주머니 점검.", "새면 방이 젖는다.")),
        new(RoomType.Hyperbaric, RoomType.Medbay, "고압 치료실", 3, "#f0a8b8", "HB", RoomTag.None, 0.1f, 0f, 0f, 0f, "M1x2x1",
            E("고압 치료실. 감압병·깊은 화상·유독 가스 중독.", "고압 산소 챔버.", "산소, 전기.", "의무실에서 — 감압 후유증이 남는다.", "챔버 봉인 점검.", "산소가 짙다 → 불.")),
        new(RoomType.QuarantineLock, RoomType.Medbay, "격리 에어록", 3, "#f3b0c0", "QL", RoomTag.None, 0f, 0f, 0f, 0f, "L1x2x1",
            E("격리 에어록. 병 걸린 사람과 물건을 오가게 하는 방.", "두 문과 살균.", "전기.", "격리실 문으로 드나든다 — 가끔 샌다.", "살균등 교체.", "없음.")),
        new(RoomType.Garden, RoomType.Lounge, "정원", 3, "#8fd07a", "GD", RoomTag.Rest | RoomTag.Wet, 0f, 0f, 0f, 0f, "G4x1x1 S2x1x1",
            E("정원. 흙과 나무 — 사람이 가장 편해지는 곳.", "흙 화단과 햇빛 등.", "물, 빛.", "화분 몇 개로.", "가지치기.", "없음.")),
        new(RoomType.Theater, RoomType.Lounge, "극장", 3, "#d99ad0", "TH", RoomTag.Rest, 0.35f, 0.05f, 0f, 0f, "S2x1x4",
            E("극장. 모여서 보고 웃는다.", "화면과 좌석.", "전기.", "휴게실 화면으로.", "없음.", "없음.")),
        new(RoomType.Archive, RoomType.Storage, "기록 보관소", 3, "#a6a0c0", "AR", RoomTag.Quiet, 0f, 0f, 0f, 0f, "K2x2x2",
            E("기록 보관소. 배의 역사와 배운 것.", "기록 선반과 서버.", "전기.", "교훈이 흐려진다.", "없음.", "없음.")),
        new(RoomType.Meditation, RoomType.Lounge, "명상실", 3, "#b8b0e8", "MD", RoomTag.Rest | RoomTag.Quiet, 0f, 0f, 0f, 0f, "S1x1x2",
            E("명상실. 긴장을 내려놓는다.", "조용한 방.", "없음.", "긴장이 오래 남는다.", "없음.", "없음.")),
        new(RoomType.ShuttleBay, RoomType.Storage, "셔틀 격납고", 3, "#9fb0c8", "SB", RoomTag.Explosive, 0.35f, 0.15f, 0f, 0f, "K2x2x1",
            E("셔틀 격납고. 기항지·잔해로 오가는 작은 배.", "셔틀과 발사 문.", "전기, 추진제.", "배째로 다가가야 한다.", "셔틀 점검.", "추진제 누출.")),
        new(RoomType.CraneControl, RoomType.Comms, "크레인 조종실", 3, "#86d9b0", "CC", RoomTag.None, 0.05f, 0f, 0f, 0f, "C1x1x1",
            E("크레인 조종실. 선외 크레인으로 잔해와 화물을 잡는다.", "조종 콘솔.", "전기, 데이터선.", "드론이 끈다 — 느리다.", "없음.", "없음.")),
        new(RoomType.HeatStorage, RoomType.Cooling, "축열실", 3, "#f0b070", "HS", RoomTag.None, 0.1f, 0f, 0f, 0f, "K2x2x1",
            E("축열실. 남는 열을 모아 두었다 추울 때 쓴다.", "소금 축열조.", "냉각 루프.", "원자로 열이 그냥 버려진다.", "없음.", "뜨겁다.")),
        new(RoomType.Security, RoomType.Comms, "보안실", 3, "#8ad0c0", "SC", RoomTag.None, 0f, 0f, 0f, 0f, "C1x1x2",
            E("보안실. 배 곳곳을 보는 화면.", "감시 화면.", "전기, 데이터선.", "사고를 늦게 본다.", "없음.", "없음.")),
        new(RoomType.Centrifuge, RoomType.Lounge, "원심 거주구", 3, "#cf9ad8", "CF", RoomTag.Rest, 0.25f, 0.55f, 0f, 0f, "S2x1x2",
            E("원심 거주구. 돌면서 중력을 만든다.", "도는 고리.", "전기.", "무중력 — 뼈와 근육이 약해진다.", "베어링 점검.", "흔들린다.")),
        new(RoomType.School, RoomType.Lounge, "학교", 3, "#e0b0d0", "SC", RoomTag.Rest, 0.3f, 0.05f, 0f, 0f, "T2x1x2 S2x1x2",
            E("학교. 다음 세대가 배를 배운다.", "교실과 모형.", "없음.", "일하면서 어깨너머로 — 느리게 배운다.", "없음.", "없음.")),
    };

    private static readonly Dictionary<RoomType, RoomSpec> ByKind = Specs.ToDictionary(s => s.Kind);

    public static IReadOnlyList<RoomSpec> All => Specs;
    public static RoomSpec? Of(RoomType t) => ByKind.TryGetValue(t, out var s) ? s : null;
    public static RoomType BaseOf(RoomType t) => Of(t)?.Base ?? t;
    public static int Tier(RoomType t) => Of(t)?.Tier ?? 0;
    public static bool Has(RoomType t, RoomTag tag) => (Tags(t) & tag) == tag;

    /// <summary>본래 방(17종)의 성질 — 표에 없는 방은 여기서.</summary>
    public static RoomTag Tags(RoomType t) => Of(t)?.Tags ?? t switch
    {
        RoomType.Quarters => RoomTag.Sleep,
        RoomType.Lounge => RoomTag.Rest,
        RoomType.Hydroponics or RoomType.Galley or RoomType.LifeSupport or RoomType.Cooling => RoomTag.Wet,
        _ => RoomTag.None,
    };

    /// <summary>이 방이 내는 소음·진동·냄새·방사선 (본래 방 포함).</summary>
    public static (float noise, float vib, float smell, float rad) Emits(RoomType t) => Of(t) is RoomSpec s ? (s.Noise, s.Vibration, s.Smell, s.Radiation) : t switch
    {
        RoomType.Engine => (0.55f, 0.6f, 0.05f, 0f),
        RoomType.Reactor => (0.3f, 0.2f, 0f, 0.35f),
        RoomType.Cooling => (0.4f, 0.3f, 0f, 0f),
        RoomType.Power => (0.2f, 0f, 0f, 0f),
        RoomType.LifeSupport => (0.3f, 0.1f, 0f, 0f),
        RoomType.Workshop => (0.35f, 0.15f, 0.1f, 0f),
        RoomType.Galley => (0.15f, 0f, 0.15f, 0f),
        RoomType.Mess => (0.2f, 0f, 0.05f, 0f),
        RoomType.Lounge => (0.15f, 0f, 0f, 0f),
        RoomType.Airlock => (0.15f, 0.05f, 0f, 0f),
        _ => (0f, 0f, 0f, 0f),
    };

    /// <summary>설계도 범례의 이름 → 방 종류 ("Gym" 또는 "체력단련실").</summary>
    public static RoomType? Parse(string name)
    {
        name = name.Trim();
        if (Enum.TryParse<RoomType>(name, true, out var t)) return t;
        foreach (var s in Specs) if (s.Name == name) return s.Kind;
        return null;
    }

    // ── 배가 해야 하는 일과 겸용의 대가 ──
    public static readonly ShipFunction[] Functions =
    {
        new("quarantine", "격리", new[] { RoomType.Quarantine, RoomType.QuarantineLock }, new[] { (RoomType.Medbay, 0.4f) }, "격리실이 없다 — 의무실에서 함께 지내 병이 덜 막힌다"),
        new("shelter", "방사선 대피", new[] { RoomType.Shelter, RoomType.WaterWallCabin }, new[] { (RoomType.Storage, 0.55f), (RoomType.Cargo, 0.6f) }, "대피소가 없다 — 창고 선반 뒤에 숨는다 (덜 막는다)"),
        new("exercise", "운동", new[] { RoomType.Gym, RoomType.Centrifuge }, new[] { (RoomType.Lounge, 0.5f) }, "체력단련실이 없다 — 휴게실에서 맨몸 운동 (절반)"),
        new("grief", "마음 추스르기", new[] { RoomType.Chapel, RoomType.Meditation, RoomType.Garden }, new[] { (RoomType.Lounge, 0.5f), (RoomType.Observatory, 0.8f) }, "기도실이 없다 — 휴게실에서 (상처가 더디게 아문다)"),
        new("research", "연구", new[] { RoomType.Lab, RoomType.ElectronicsLab }, new[] { (RoomType.Workshop, 0.6f) }, "연구실이 없다 — 정비실 구석에서 (느리다)"),
        new("learning", "가르치기", new[] { RoomType.School }, new[] { (RoomType.Lounge, 0.6f), (RoomType.Workshop, 0.7f) }, "학교가 없다 — 일하면서 어깨너머로"),
        new("fire", "자동 소화", new[] { RoomType.SuppressionRoom }, new[] { (RoomType.Storage, 0f) }, "소화 설비실이 없다 — 사람이 소화기를 들고 뛴다"),
        new("council", "회의", new[] { RoomType.MeetingRoom }, new[] { (RoomType.Mess, 0.7f) }, "회의실이 없다 — 식당에서 (오래 끈다)"),
        new("stars", "별 보기", new[] { RoomType.Observatory }, new[] { (RoomType.Lounge, 0.5f), (RoomType.Bridge, 0.4f) }, "관측실이 없다 — 화면으로 본다"),
        new("calibration", "교정", new[] { RoomType.Calibration }, new[] { (RoomType.Workshop, 0.7f) }, "교정실이 없다 — 현장에서 (덜 정확하다)"),
        new("rest", "깊은 잠", new[] { RoomType.QuietQuarters, RoomType.PrivateCabins, RoomType.WaterWallCabin }, new[] { (RoomType.Quarters, 0.85f) }, "조용한 침실이 없다 — 여럿이 한방에서"),
        new("morgue", "안치", new[] { RoomType.Morgue }, new[] { (RoomType.Freezer, 0.8f), (RoomType.Storage, 0.4f) }, "안치실이 없다 — 창고에 모신다 (산 사람이 힘들어한다)"),
    };

    public static ShipFunction Function(string key) => Functions.First(f => f.Key == key);
}

/// <summary>배의 기능이 어느 방에서 얼마나 잘 되는지 (겸용의 대가).</summary>
public static class Facilities
{
    /// <summary>그 일을 할 방과 효율 (전용 방 1, 대신하는 방은 표의 값, 없으면 null·0).</summary>
    public static (Room? room, float factor) Best(Ship ship, string key, Func<Room, bool>? ok = null)
    {
        var fn = RoomCatalog.Function(key);
        foreach (var k in fn.Best)
            foreach (var r in ship.KindOf(k))
                if (!r.Abandoned && r.UsedAs == null && (ok == null || ok(r))) return (r, 1f); // v16.17 다른 용도로 쓰이는 방은 뺀다
        if (RoomUseSystem.BestUsed(ship, fn, ok) is Room used) return (used, 1f); // v16.17 쓰임으로 그 용도가 된 방 (창고 절반의 땀방)
        foreach (var (k, f) in fn.Fallback)
            foreach (var r in ship.KindOf(k))
                if (!r.Abandoned && (ok == null || ok(r))) return (r, RoomUseSystem.Factor(fn, r, f));
        return (null, 0f);
    }

    /// <summary>이 방이 그 일을 얼마나 잘 하는지 (전용 1 · 대신 표의 값 · 못 함 0).</summary>
    public static float Factor(Room? room, string key)
    {
        if (room == null) return 0f;
        var fn = RoomCatalog.Function(key);
        float b = 0f;
        if (fn.Best.Contains(room.Kind)) b = 1f;
        else foreach (var (k, f) in fn.Fallback) if (room.Kind == k) b = f;
        return RoomUseSystem.Factor(fn, room, b); // v16.17 쓰임이 설계를 이긴다
    }
}
