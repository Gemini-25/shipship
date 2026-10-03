using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.15 도덕적 딜레마 24 (데이터 표).
//  Vec: A 를 고르면 기우는 쪽 (안전 · 공동체 · 규칙 · 동정, + 쪽). B 는 그 반대.
//  Side: 걸린 사람에게 A 가 좋은 결정인가(+1) 나쁜 결정인가(−1).
//  Urgent: 급하면 선장(없으면 권한이 있는 주 컴퓨터 · 그다음 가장 믿음직한 사람)이 바로 정하고, 아니면 긴급 회의에서 표결한다.
//  RetMin · RetMax: 몇 날 뒤 결과가 돌아오나 (0 = 돌아오지 않는다) · AtPort: 기항지에서 돌아오나.
//  Motif: 그림 (ShipViewValues.cs).

public sealed record DilemmaSpec(string Key, string Name, string Motif, bool Urgent, float[] Vec, int Side,
    string A, string B, string WhyA, string WhyB, int RetMin, int RetMax, bool AtPort);

public static class DilemmaTable
{
    private static DilemmaSpec D(string key, string name, string motif, bool urgent, float s, float k, float u, float m, int side,
        string a, string b, string whyA, string whyB, int r0 = 0, int r1 = 0, bool port = false)
        => new(key, name, motif, urgent, new[] { s, k, u, m }, side, a, b, whyA, whyB, r0, r1, port);

    public static readonly DilemmaSpec[] All =
    {
        D("air_short", "산소가 모자라다 — 누구 몫부터 지키나", "air", false, 0.4f, 0.3f, 0f, 0.9f, 0,
            "다친 사람 · 아이 몫부터 지킨다", "일하는 사람 몫부터 지킨다", "숨이 가쁜 사람부터 살려야 한다", "고칠 사람이 쓰러지면 다 같이 죽는다", 9, 16),
        D("quarantine", "열이 나는 사람 — 따로 둘까 곁에서 돌볼까", "quarantine", false, 0.8f, 0.4f, 0.5f, -0.7f, -1,
            "의무실에 따로 둔다", "곁에서 돌본다", "한 사람 때문에 다 앓을 수는 없다", "아픈 사람을 혼자 두면 마음이 먼저 무너진다", 8, 14),
        D("distress", "구조 신호 — 배를 돌릴까 일정대로 갈까", "beacon", true, -0.5f, 0.2f, 0f, 1f, 0,
            "배를 돌려 건진다", "일정대로 간다", "저 캡슐에 사람이 있다", "우리 배 사정이 먼저다", 14, 26, true),
        D("pirate", "무장한 배가 사람 하나를 내놓으라 한다", "pirate", true, 0.9f, -0.4f, 0f, -1f, -1,
            "내준다", "못 내준다 — 짐을 대신 내준다", "한 사람 때문에 다 죽을 수는 없다", "우리 사람은 우리가 지킨다", 18, 30, true),
        D("stowaway", "화물칸에서 사람이 나왔다 — 태우고 갈까", "stowaway", false, -0.3f, 0.2f, -0.8f, 0.8f, 1,
            "함께 간다", "다음 기항지에 내려놓는다", "갈 데 없는 사람이다", "몰래 탄 사람까지 먹일 여유는 없다", 12, 22),
        D("left_behind", "밖에 쓰러진 동료 — 다시 사람을 내보낼까", "stretcher", true, -0.8f, 0.6f, 0f, 0.9f, 1,
            "다시 나가 데려온다", "더는 사람을 내보내지 않는다", "두고 온 사람이 있다", "한 사람 구하려다 둘을 잃는다", 10, 18),
        D("experiment", "위험한 실험 — 해 볼까", "flask", false, -0.9f, 0.2f, -0.3f, 0f, 0,
            "해 본다", "그만둔다", "잘되면 배가 한결 나아진다", "잘못되면 되돌릴 수 없다", 5, 12),
        D("cover_up", "정비 실수 — 덮어 줄까 기록에 남길까", "logbook", true, 0f, 0.4f, -1f, 0.5f, 1,
            "덮어 둔다", "기록에 남긴다", "한 번 실수로 사람을 죽일 순 없다", "숨긴 실수는 또 난다", 12, 22, true),
        D("trust_machine", "컴퓨터와 사람의 판단이 갈렸다 — 누구를 따를까", "eye", true, 0.2f, 0f, 0.6f, -0.2f, 0,
            "컴퓨터를 따른다", "사람 판단대로 한다", "기록과 셈은 컴퓨터가 낫다", "현장은 사람이 안다", 3, 8),
        D("ration_who", "먹을 것이 모자라다 — 누구 몫부터", "bowl", false, 0.2f, 0.4f, 0f, 0.8f, 0,
            "아픈 사람 먼저", "일하는 사람 먼저", "아픈 사람이 먼저 쓰러진다", "일손이 없으면 다 굶는다", 8, 15),
        D("thief_mercy", "배급을 빼돌린 사람 — 엄하게 다스릴까", "scale", false, 0.2f, 0.3f, 0.9f, -0.7f, -1,
            "엄하게 — 몫을 줄인다", "사정을 듣고 넘어간다", "한 번 봐주면 다들 손댄다", "배고파서 그랬을 거다", 7, 12),
        D("seal_door", "불길 속 방 — 사람이 남았는데 문을 닫을까", "door", true, 0.8f, 0.5f, 0.3f, -0.9f, -1,
            "닫는다", "나올 때까지 연다", "불이 번지면 다 잃는다", "안에 사람이 있다", 6, 12),
        D("jettison_keepsake", "무게를 줄여야 한다 — 누군가의 소중한 짐을 버릴까", "crate", false, 0.2f, 0.7f, 0f, -0.5f, -1,
            "버린다", "다른 걸 더 버린다", "배가 먼저다", "그 사람한텐 전부다", 10, 18),
        D("wreck_relics", "난파선의 유품 — 건져 쓸까 그대로 둘까", "wreck", false, -0.6f, 0.2f, 0.3f, -0.6f, 0,
            "건져 쓴다", "그대로 둔다", "쓸 수 있는 걸 버리는 건 낭비다", "죽은 사람 물건이다", 14, 26, true),
        D("bad_news", "나쁜 소식 — 다 알릴까 당분간 덮을까", "radio", true, -0.2f, 0.6f, 0.6f, 0.2f, 0,
            "다 알린다", "당분간 덮어 둔다", "다 같이 알아야 같이 버틴다", "지금 알리면 다들 무너진다", 4, 10),
        D("scarce_medicine", "약이 모자라다 — 지금 아픈 사람에게 다 쓸까", "vial", false, -0.6f, 0.1f, 0f, 0.9f, 1,
            "지금 쓴다", "아껴 둔다", "눈앞에서 아픈 사람이 먼저다", "더 큰일이 올 수도 있다", 8, 15),
        D("night_repair", "밤샘 수리 — 지친 사람을 더 부릴까", "wrench", true, -0.6f, 0.3f, 0f, -0.7f, 0,
            "밤새 고친다", "쉬게 하고 아침에 고친다", "내일이면 늦는다", "지친 손이 더 큰 사고를 낸다", 3, 7),
        D("refugees", "아이 딸린 가족이 태워 달라 한다", "family", false, -0.4f, 0.5f, -0.3f, 0.9f, 0,
            "태운다", "자리가 없다고 한다", "아이를 두고 갈 수는 없다", "우리 먹을 것도 빠듯하다", 18, 30, true),
        D("smuggler_report", "옆 배의 밀수 — 기항지에 알릴까", "parcel", false, 0.3f, 0.2f, 0.9f, -0.2f, 0,
            "알린다", "모른 척한다", "눈감으면 우리도 한패다", "남의 일에 끼면 화를 입는다", 12, 24, true),
        D("share_water", "물이 떨어진 배가 나눠 달라 한다", "drop", true, -0.7f, 0.4f, 0f, 0.9f, 0,
            "나눈다", "우리 몫을 지킨다", "물 없는 배는 사흘을 못 간다", "우리 물탱크도 넉넉지 않다", 14, 28, true),
        D("mutineer", "반란을 꾸민 사람 — 근무에서 뺄까 말로 풀까", "key", false, 0.6f, 0.3f, 0.9f, -0.6f, -1,
            "근무에서 뺀다", "말로 푼다", "배를 흔든 사람이다", "사정을 들어 보자", 6, 12),
        D("turn_back", "크게 다친 사람 — 가까운 기항지로 돌아갈까", "cross", true, 0.3f, 0.2f, 0f, 0.9f, 1,
            "돌아간다", "그대로 간다", "살릴 수 있는 사람이다", "돌아가면 모두가 늦는다", 9, 16),
        D("leash_computer", "실수가 잦은 컴퓨터 — 맡긴 일을 줄일까", "chip", false, 0.7f, 0f, 0.3f, 0f, 0,
            "맡긴 일을 줄인다", "그대로 맡긴다", "기계가 틀리면 사람이 다친다", "사람 손이 더 느리다", 6, 12),
        D("leave_wish", "배에서 내리겠다는 사람 — 붙잡을까 보내 줄까", "bag", false, 0f, 0.6f, 0.2f, -0.3f, -1,
            "붙잡는다", "보내 준다", "같이 시작한 항해다", "가겠다는 사람을 어떻게 막나"),
    };

    private static Dictionary<string, DilemmaSpec>? _by;
    public static DilemmaSpec? Get(string key) => (_by ??= All.ToDictionary(d => d.Key)).GetValueOrDefault(key);
}
