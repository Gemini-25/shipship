using System;

namespace ShipSim.Core;

// v19 모양 있는 새 배 여덟 척 — 설계도는 ShipShapesAscii.cs (tools/shipgen/gen_shapes.py 가 만든다).
//   송골매호(화살촉형) · 귀상어호(망치머리형) · 보름달호(원반형) · 삼지창호(삼지창형) ·
//   가오리호(가오리형) · 고래호(고래형) · 잠자리호(잠자리형) · 한울호(쐐기형)
// 선체 모양이 다르면 사고가 닿는 곳도 다르다 — 날개 끝 방은 멀고, 가는 목 · 척추 · 꼬리 통로는 하나뿐이다.

public static partial class ShipBlueprints
{
    public static readonly ShipTemplate SonggolmaeShip = new("Songgolmae", SonggolmaeName, 10, Songgolmae, "화살촉형 정찰 연구선 · 뒤로 젖힌 날개 · 꼬리 홈의 엔진 넷 · 통유리 함교",
        new ShipInfo(ShipPurpose.Research, ShipFrame.Arrow, ShipDesigner.Military, ShipStart.New, 2240,
            new[] { (ItemKind.Sensor, 4), (ItemKind.Electronics, 8), (ItemKind.Ration, 30) }, null,
            new[] { "2240년 군 조선소에서 정찰 연구선으로 진수", "시험 비행 두 번 · 아직 먼 곳에 가 본 적이 없다" },
            "엔진 넷 — 빠르다 · 격벽과 좋은 부품 · 통로 고리 둘 사이 심장부",
            "날개 끝 방(격납고 · 정비실 · 에어락)은 날개 통로 끝이라 멀다 · 뱃머리가 좁아 함교가 작다", 3,
            "새로 뽑은 정찰 연구선. 날개를 뒤로 젖힌 채 아무도 가 보지 않은 곳을 먼저 들여다본다."));

    public static readonly ShipTemplate GwisangeoShip = new("Gwisangeo", GwisangeoName, 8, Gwisangeo, "망치머리형 심우주 탐사선 · 가는 척추 · 망치 양 끝 관측실과 교정실",
        new ShipInfo(ShipPurpose.Research, ShipFrame.Hammerhead, ShipDesigner.Civilian, ShipStart.Used, 2224,
            new[] { (ItemKind.Sensor, 6), (ItemKind.Lamp, 4), (ItemKind.Ration, 24) }, "심해 7호",
            new[] { "2224년 학술 재단이 심우주 관측선으로 건조", "2233년 혜성 꼬리 관측 — 망치 위쪽 끝이 얼음에 긁혔다", "2239년 재단이 문을 닫으며 팔렸다" },
            "망치 양 끝 관측실 · 교정실 — 멀리 본다 · 넓은 함교",
            "척추 통로 둘이 끊기면 엔진 블록과 망치가 갈린다 · 민간 설계 — 배전반 하나에 다 걸려 있다", 3,
            "앞머리에 커다란 망치를 단 관측선. 망치 양 끝에서 하늘을 넓게 본다."));

    public static readonly ShipTemplate BoreumdalShip = new("Boreumdal", BoreumdalName, 20, Boreumdal, "원반형 여객 탐사선 · 원반 거주구 · 가는 목 · 기둥에 매단 나셀 둘",
        new ShipInfo(ShipPurpose.General, ShipFrame.Saucer, ShipDesigner.Civilian, ShipStart.New, 2238,
            new[] { (ItemKind.Ration, 60), (ItemKind.Seed, 20), (ItemKind.Coffee, 10), (ItemKind.Paint, 4) }, null,
            new[] { "2238년 칼리스토 궤도 조선소에서 진수", "첫 장거리 운항 — 스무 명이 원반에서 산다" },
            "원반 안 네모 고리 통로 — 어디든 두 갈래 길 · 넓은 식당 · 회의실 · 정원 · 관측실",
            "원반과 기관 선체를 잇는 목 통로가 하나뿐이다 · 나셀은 기둥 통로 끝이라 멀다", 3,
            "둥근 원반에 사람이 살고, 뒤쪽 기관 선체가 원반을 민다. 목 하나로 둘이 이어져 있다."));

    public static readonly ShipTemplate SamjichangShip = new("Samjichang", SamjichangName, 12, Samjichang, "삼지창형 소행성 채굴선 · 갈래 셋 · 파쇄실 · 용접실 · 드론 격납고",
        new ShipInfo(ShipPurpose.Mining, ShipFrame.Trident, ShipDesigner.Settler, ShipStart.Used, 2215,
            new[] { (ItemKind.MetalOre, 30), (ItemKind.Plate, 12), (ItemKind.Structure, 6), (ItemKind.Gloves, 6) }, "갈퀴 3호",
            new[] { "2215년 세레스 채굴 조합이 건조", "2229년 위쪽 갈래 끝 파쇄실을 새로 달았다", "2236년 개척민 가족들이 사들였다" },
            "갈래 끝 파쇄실 · 용접실 — 캐고 녹이고 붙인다 · 척추 통로가 뱃머리 함교까지 곧다",
            "갈래 끝이 멀다 · 갈래 사이는 우주 — 갈래 하나가 다치면 그 갈래만 외롭다 · 비표준 부품", 3,
            "소행성을 붙잡는 갈래 셋. 가운데 갈래 끝에서 함장이 바위를 고른다."));

    public static readonly ShipTemplate GaoriShip = new("Gaori", GaoriName, 16, Gaori, "가오리형 농업선 · 날개 가득 재배실 · 뿔 끝 통신실과 항법실",
        new ShipInfo(ShipPurpose.Farm, ShipFrame.Manta, ShipDesigner.Civilian, ShipStart.Used, 2226,
            new[] { (ItemKind.Seed, 40), (ItemKind.Nutrient, 20), (ItemKind.Ration, 20) }, "푸른들 2호",
            new[] { "2226년 농업 협동조합이 건조", "2234년 날개 끝 재배실을 넓혔다", "곡물 항로 아홉 번 완주" },
            "날개마다 재배실 — 식량이 넉넉하다 · 버섯 · 조류 · 단백질 · 정원",
            "날개 끝 방은 날개 통로 두 층을 지나야 닿는다 · 넓은 선체 — 운석이 닿을 자리도 넓다", 2,
            "넓게 펼친 날개 안에서 밀과 버섯과 조류가 자란다. 정거장들이 이 배를 기다린다."));

    public static readonly ShipTemplate GoraeShip = new("Gorae", GoraeName, 24, Gorae, "고래형 세대선 · 둥근 몸통 · 극장 · 학교 · 꼬리 지느러미 엔진",
        new ShipInfo(ShipPurpose.Colony, ShipFrame.Whale, ShipDesigner.Settler, ShipStart.Junk, 2190,
            new[] { (ItemKind.Seed, 30), (ItemKind.Thread, 10), (ItemKind.Tape, 8), (ItemKind.Ration, 40) }, "느린 바다",
            new[] { "2190년 이민 공동체가 손수 지었다", "2212년 꼬리 통로를 늘려 엔진을 지느러미로 옮겼다", "2231년 셋째 세대가 태어났다", "주인은 언제나 이 배에 사는 사람들이었다" },
            "둥근 몸통 — 고리 통로와 척추 통로로 어디든 두 갈래 · 극장 · 학교 · 예배실 · 넓은 식당",
            "엔진까지 가는 길은 꼬리 통로 하나뿐 · 오래된 배 — 많이 닳았다 · 비표준 부품", 4,
            "몇 세대가 태어나고 늙은 둥근 배. 꼬리 끝 지느러미가 아직도 천천히 민다."));

    public static readonly ShipTemplate JamjariShip = new("Jamjari", JamjariName, 6, Jamjari, "잠자리형 소형 연구선 · 가는 몸통 · 날개 넷 · 둥근 머리 함교",
        new ShipInfo(ShipPurpose.Research, ShipFrame.Dragonfly, ShipDesigner.Civilian, ShipStart.New, 2241,
            new[] { (ItemKind.Sensor, 2), (ItemKind.Seed, 10), (ItemKind.Ration, 30) }, null,
            new[] { "2241년 대학 연구소가 진수", "첫 항해 · 여섯 명이 날개마다 실험을 걸었다" },
            "날개 넷 — 조류 · 정원 · 교정실 · 관측실 · 드론 격납고 · 작은 배치고 할 수 있는 게 많다",
            "몸통이 가늘어 원자로 · 배전실 · 주컴퓨터실이 선체 바로 안쪽 — 운석 한 방이 아프다 · 날개 끝은 방을 지나야 닿는다", 4,
            "가는 몸통에 날개 넷을 단 작은 연구선. 날개마다 다른 실험이 자란다."));

    public static readonly ShipTemplate HanulShip = new("Hanul", HanulName, 30, Hanul, "쐐기형 함대 기함 · 축 통로 · 층층이 방 띠 · 뒷면 가득 엔진 · 꼬리 쪽 지휘탑",
        new ShipInfo(ShipPurpose.General, ShipFrame.Wedge, ShipDesigner.Military, ShipStart.New, 2240,
            new[] { (ItemKind.Plate, 20), (ItemKind.Structure, 10), (ItemKind.Electronics, 10), (ItemKind.MedKit, 10), (ItemKind.Ration, 60) }, null,
            new[] { "2240년 함대 조선소에서 기함으로 진수", "취역식 — 서른 명이 지휘탑 앞에 섰다" },
            "격벽 · 이중 배선 · 좋은 부품 · 서버실 · 보안실 · 교정실 · 넓은 함교",
            "쐐기 끝으로 갈수록 방이 좁다 · 날개 끝 띠까지 통로 셋을 건너야 한다 · 큰 배 — 손이 많이 간다", 3,
            "함대의 맨 앞에 서는 거대한 쐐기. 꼬리 쪽 지휘탑에서 서른 명의 하루가 시작된다."));
}
