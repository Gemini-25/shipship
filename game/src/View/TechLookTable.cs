using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ShipSim.Core;

namespace ShipSim.View;

// v16.5b 기술과 설비가 티 나는 그림 — 표 (Godot 없이 읽힌다: 헤드리스 --techlookcheck 가 이 파일을 함께 빌드한다).
//   ① 기술 수준 미감 세트 3단계(초기 · 중간 · 고급): 팔레트 · 무늬 매개변수 · 테두리 · 배선 · 조명 방식.
//      바닥 · 벽 텍스처 생성기도 이 표(LookSet)를 읽어 쓸 수 있다 — 여기는 값만 둔다.
//   ② 기술마다 배 모습: TechWeb 의 Visual 열쇠 111개 → 어디에(설비 · 방 벽 · 외판 · 바깥 · 바닥 · 통로 · 모든 설비 · 배관) 무엇을.
//   ③ 설비 단계 II~IV 부품 · ④ 등급(Mk.1 임시 손질 · Mk.3 마감): 설비 70종마다 한 줄.
//   ⑤ 개조 칸이 새것인 정도 · ⑥ 설치 · 업그레이드 순간 — Core 의 기록 시각(칸막이 · 안건 완료 · 이력 줄 · 익힌 때)을 읽기만 한다.
// 그리기는 Core 상태를 바꾸지 않는다 (결정론).

public enum LookLevel { Early, Mid, Advanced }
public enum FrameStyle { Riveted, Seamed, Seamless }
public enum WireStyle { Exposed, Tray, Hidden }
public enum LampStyle { Fluorescent, LedStrip, Cove }

/// <summary>
/// 미감 세트 하나. 색은 0xRRGGBBAA.
/// 팔레트: 벽 · 벽 모서리 · 패널 · 장식 띠 · 빛 · 바닥 물들임 · 밑칠(벗겨지면 보이는 색) · 강조.
/// 무늬: 리벳 간격(0 = 없음) · 이음매 간격 · 칠 벗겨짐 · 때 · 광택 · 은은한 빛 · 모서리 둥글기 · 판 크기(칸) · 얼룩 잡음 · 경고 빗금.
/// 테두리 · 배선 · 조명 방식 · 깜빡임 · 등 간격(칸, 0 = 이어진 띠).
/// </summary>
public sealed record LookSet(
    LookLevel Level, string Name, string Note,
    uint Wall, uint WallEdge, uint Panel, uint Trim, uint Light, uint FloorTint, uint Primer, uint Accent,
    float RivetStep, float SeamStep, float Peel, float Grime, float Gloss, float Glow, float Corner,
    float PlateScale, float Noise, float Stripe,
    FrameStyle Frame, WireStyle Wires, LampStyle Lamp, float Flicker, int LampEvery);

/// <summary>기술 모습이 붙는 자리.</summary>
public enum VAnchor { Fix, Room, Wall, Hull, Exterior, Floor, Corridor, Machines, Pipes, Net }

/// <summary>Visual 열쇠 한 줄: 자리 · 대상 설비 · 방 종류(비면 모든 방) · 몇 개마다 하나 · 움직이는 부분이 있나 · 설명.</summary>
public sealed record VisualRow(string Key, VAnchor Anchor, FurnitureType? Fix, RoomType[] Rooms, int Every, bool Live, string What);

/// <summary>단계 부품 (설비마다 고른 조합 · 자리가 다르다).</summary>
public enum TierPart
{
    Fins, Tank, Coil, Pod, Cables, Strip, Screen, Antenna, Plating, Shroud, Rotor, Gauges, PipeLoop, Core, Holo, Field,
    Vents, Rail, Cushion, Lamp, Drawers, Filter, Dish, Glass, Chip, Quilt, Cells, Hood, Arm, Valve,
}

/// <summary>Mk.1 임시품의 손질 방식 (설비 성격마다).</summary>
public enum Improv { DuctTape, HoseClamp, JumperWire, ZipTies, Rope, Shim, TapedScreen, DripCan, BoltedFan, WeldBead, Cardboard }

/// <summary>Mk.3 개량형의 마감 (설비 성격마다).</summary>
public enum Trim { EdgeGlow, Chevron, GlassFace, Anodized, Pinstripe, HaloRing }

public readonly record struct PartAt(TierPart Part, float U, float V);

/// <summary>설비 한 종류의 단계 · 등급 모양: II · III · IV 에서 붙는 부품(쌓인다) · Mk.1 손질 · Mk.3 마감 · 빛 색.</summary>
public sealed record TierKit(FurnitureType Type, Improv Mk1, Trim Mk3, uint Glow, PartAt[] II, PartAt[] III, PartAt[] IV)
{
    public PartAt[] At(int tier) => tier switch { 2 => II, 3 => III, 4 => IV, _ => Array.Empty<PartAt>() };
    /// <summary>단계 부품 이름 (정렬) — 종류마다 · 단계마다 달라야 한다.</summary>
    public string Sig(int tier) => string.Join("+", At(tier).Select(p => p.Part.ToString()).OrderBy(s => s, StringComparer.Ordinal));
}

/// <summary>설치 · 업그레이드 순간의 종류.</summary>
public enum MomentKind { None, Install, Upgrade, Mk3, Mk1, Restore, Reassemble, Learned }

public static class TechLookTable
{
    // ═══════════════════════════════ ① 미감 세트 ═══════════════════════════════

    public static readonly LookSet[] Sets =
    {
        new(LookLevel.Early, "초기", "리벳 · 노출 배선 · 형광등 · 칠 벗겨짐",
            0x3a3f46ff, 0x6b7280ff, 0x4a5058ff, 0xc89a3aff, 0xe2f4d6ff, 0x2a2e33ff, 0xb5562eff, 0xd8b24aff,
            RivetStep: 6f, SeamStep: 32f, Peel: 0.35f, Grime: 0.4f, Gloss: 0.05f, Glow: 0f, Corner: 0f,
            PlateScale: 1f, Noise: 0.6f, Stripe: 0.3f,
            FrameStyle.Riveted, WireStyle.Exposed, LampStyle.Fluorescent, Flicker: 0.08f, LampEvery: 4),
        new(LookLevel.Mid, "중간", "이음매 패널 · 배선 덮개 · LED 띠",
            0x2f3744ff, 0x7d8a9eff, 0x3c4656ff, 0x5f8fb0ff, 0xeef3ffff, 0x262c36ff, 0x6b7380ff, 0x5fb0d0ff,
            RivetStep: 16f, SeamStep: 48f, Peel: 0.08f, Grime: 0.15f, Gloss: 0.25f, Glow: 0.3f, Corner: 3f,
            PlateScale: 1.5f, Noise: 0.3f, Stripe: 0.1f,
            FrameStyle.Seamed, WireStyle.Tray, LampStyle.LedStrip, Flicker: 0f, LampEvery: 3),
        new(LookLevel.Advanced, "고급", "매끈한 패널 · 간접 조명 · 숨은 배선",
            0x2a2f3aff, 0xb8c6d8ff, 0x3a4252ff, 0xcfe3f5ff, 0xffe6c8ff, 0x232833ff, 0x2a2f3aff, 0x8fd8ffff,
            RivetStep: 0f, SeamStep: 96f, Peel: 0f, Grime: 0.03f, Gloss: 0.6f, Glow: 0.8f, Corner: 8f,
            PlateScale: 3f, Noise: 0.1f, Stripe: 0f,
            FrameStyle.Seamless, WireStyle.Hidden, LampStyle.Cove, Flicker: 0f, LampEvery: 0),
    };

    public static LookSet Of(LookLevel l) => Sets[(int)l];

    /// <summary>중간 · 고급으로 넘어가는 점수.</summary>
    public const int MidAt = 10, AdvancedAt = 28;

    /// <summary>배의 기술 점수 = 익힌 기술 수 + 시대마다 4.</summary>
    public static int Score(World w) => w.Eras.Known.Count + 4 * (w.Eras.Era - 1);

    public static LookLevel LevelFor(int score) => score >= AdvancedAt ? LookLevel.Advanced : score >= MidAt ? LookLevel.Mid : LookLevel.Early;

    public static LookLevel LevelOf(World w) => LevelFor(Score(w));

    /// <summary>다음 단계까지 0~1 (텍스처 생성기 · 화면이 두 세트를 섞어 쓸 때).</summary>
    public static float Toward(World w)
    {
        int s = Score(w);
        return LevelFor(s) switch
        {
            LookLevel.Early => Math.Clamp(s / (float)MidAt, 0f, 1f),
            LookLevel.Mid => Math.Clamp((s - MidAt) / (float)(AdvancedAt - MidAt), 0f, 1f),
            _ => 1f,
        };
    }

    // ═══════════════════════════════ ② 기술마다 배 모습 ═══════════════════════════════

    private static VisualRow V(string key, VAnchor a, string what, int every = 1, bool live = false, FurnitureType? fix = null, params RoomType[] rooms)
        => new(key, a, fix, rooms, every, live, what);
    private static VisualRow F(string key, FurnitureType t, string what, bool live = false) => new(key, VAnchor.Fix, t, Array.Empty<RoomType>(), 1, live, what);
    private static VisualRow R(string key, string what, bool live, params RoomType[] rooms) => new(key, VAnchor.Room, null, rooms, 1, live, what);

    public static readonly VisualRow[] VisualRows =
    {
        // ── 1 근지구 ──
        V("wall.fireproof", VAnchor.Wall, "벽에 불연 광물 패널 · 붉은 팽창 띠", every: 3),
        R("medbay.triage", "분류 꼬리표 걸이 · 바닥에 빨강 · 노랑 · 초록 구역", false, RoomType.Medbay, RoomType.Triage, RoomType.Quarantine),
        F("console.checklist", FurnitureType.Console, "콘솔 옆에 걸린 점검표 판"),
        V("pipe.coolantdope", VAnchor.Pipes, "냉각관에 첨가제 색띠 · 투입 병", every: 4),
        R("storage.seedvault", "서리 낀 종자 서랍장", false, RoomType.SeedVault, RoomType.Storage, RoomType.Hydroponics, RoomType.Cargo),
        F("lifesupport.amine", FurnitureType.OxygenGenerator, "아민 흡착 통 둘 (거품 눈금)"),
        V("hull.weldseam", VAnchor.Hull, "외판의 고른 용접 비드", every: 1),
        R("galley.sink", "손 씻는 개수대 · 수도꼭지 · 비누", true, RoomType.Galley, RoomType.Mess, RoomType.Medbay),
        F("workshop.notes", FurnitureType.Workbench, "작업대 위 공유 노트 · 펼친 쪽"),
        R("workshop.toolboard", "구멍판에 공구 윤곽", false, RoomType.Workshop, RoomType.WeldingShop, RoomType.PartsPrep, RoomType.ElectronicsLab, RoomType.Lab),
        F("panel.rcd", FurnitureType.PowerPanel, "누전 차단기 묶음 (노란 시험 단추)"),
        V("machine.listener", VAnchor.Machines, "설비에 붙은 청음 감지기 · 선", every: 1, live: true),
        // ── 2 태양계 ──
        F("growbed.genecrops", FurnitureType.GrowBed, "나선 꼬리표 말뚝 · 밝은 잎"),
        V("hull.coilring", VAnchor.Hull, "외판을 감은 차폐 코일 무늬 · 장 반짝임", every: 1, live: true),
        F("panel.smartgrid", FurnitureType.PowerPanel, "부하 곡선 작은 화면 · 데이터 점", true),
        V("exterior.drones", VAnchor.Exterior, "외판 드론 거치대 · 도는 드론", live: true),
        F("workshop.cobot", FurnitureType.Workbench, "두 마디 협동 로봇 팔 (움직인다)", true),
        V("exterior.rcs", VAnchor.Exterior, "모서리 자세 제어 추력기 넷 · 가끔 분사", live: true),
        V("corridor.flowlines", VAnchor.Corridor, "통로 바닥 동선 화살표", every: 3),
        V("hull.waterwall", VAnchor.Hull, "외벽 안쪽 물주머니 벽 (출렁인다)", every: 1, live: true),
        F("growbed.nft", FurnitureType.GrowBed, "재배대 둘레 양액 홈관 · 흐르는 양액", true),
        F("waterplant.vcd", FurnitureType.WaterRecycler, "증기 압축 증류 드럼 · 도는 고리", true),
        V("pipe.heatpipe", VAnchor.Pipes, "구리 열 파이프 · 핀", every: 3),
        R("medbay.screen", "원격 진료 벽 화면 · 심박 선", true, RoomType.Medbay, RoomType.Triage),
        R("workshop.printer", "적층 제작기 (층이 쌓인다)", true, RoomType.Workshop, RoomType.Lab, RoomType.ElectronicsLab, RoomType.PartsPrep),
        // ── 3 핵융합 ──
        F("engine.fusion", FurnitureType.EngineCore, "엔진에 자홍빛 핵융합 고리", true),
        V("hull.selfseal", VAnchor.Hull, "외벽 안쪽 호박색 젤 벌집", every: 2),
        R("lifesupport.biofilter", "생물막 여과 기둥 · 거품", true, RoomType.LifeSupport, RoomType.WaterPlant, RoomType.AlgaeLab),
        F("sensor.quantum", FurnitureType.SensorArray, "서리 낀 극저온 통 · 얽힌 점 쌍", true),
        V("trunk.superconduct", VAnchor.Net, "전력 간선에 서리 · 파란 냉각 고리", every: 1),
        F("console.predict", FurnitureType.Console, "예측 곡선 (점선 앞날)", true),
        V("wall.fiber", VAnchor.Net, "데이터 선을 따라 흐르는 빛 신경", every: 1, live: true),
        V("pipe.crawler", VAnchor.Pipes, "관을 기는 작은 로봇", every: 1, live: true),
        V("hull.ribs", VAnchor.Hull, "탄소 섬유 늑골 띠", every: 3),
        V("duct.zones", VAnchor.Room, "구역 색 송풍구 · 바람 결", every: 1, live: true),
        V("exterior.pdlaser", VAnchor.Exterior, "외판 레이저 포탑 · 쓸고 가는 붉은 빔", live: true),
        V("engine.magnozzle", VAnchor.Exterior, "노즐을 감은 자기 코일 고리", live: true),
        R("medbay.vat", "재생 배양관 (분홍 액 · 거품)", true, RoomType.Medbay, RoomType.Lab, RoomType.Triage),
        // ── 4 성간 준비 ──
        V("hull.centrifuge", VAnchor.Exterior, "배를 두른 회전 고리 (돈다)", live: true),
        V("machine.nano", VAnchor.Machines, "설비 위를 기는 은빛 나노 점", every: 1, live: true),
        R("medbay.cryo", "서리 낀 저온 캡슐", true, RoomType.Medbay, RoomType.Morgue, RoomType.Quarantine, RoomType.Triage),
        F("growbed.mist", FurnitureType.GrowBed, "재배대 끝 분무 노즐 · 안개", true),
        R("lifesupport.algaetank", "초록 광생물 관 묶음", true, RoomType.LifeSupport, RoomType.AlgaeLab, RoomType.Hydroponics),
        V("pipe.liquidmetal", VAnchor.Pipes, "은빛 액체 금속이 흐르는 관", every: 1, live: true),
        F("server.neural", FurnitureType.MainComputer, "랙 위 신경망 마디 불빛", true),
        R("crew.exosuit", "외골격 거치대 (노란 관절)", false, RoomType.EvaPrep, RoomType.Workshop, RoomType.Airlock, RoomType.Storage, RoomType.Cargo),
        F("sensor.multispec", FurnitureType.SensorArray, "무지개 렌즈 묶음", true),
        F("battery.solid", FurnitureType.Battery, "육각 고체 셀 무늬"),
        V("exterior.ionring", VAnchor.Exterior, "뱃머리 이온 편향 고리 · 푸른 호", live: true),
        F("quarters.pods", FurnitureType.Bed, "침대를 덮는 개인 선실 덮개"),
        // ── 5 탈지구 공학 ──
        R("bridge.aicaptain", "부함장 보조 컴퓨터 눈 (숨 쉬듯 빛난다)", true, RoomType.Bridge, RoomType.BackupBridge, RoomType.Navigation),
        R("lifesupport.loop", "재활용 분류함 넷 · 순환 화살표", false, RoomType.LifeSupport, RoomType.Recycling, RoomType.WaterPlant),
        R("workshop.assembler", "분자 조립 상자 (빛 격자)", true, RoomType.Workshop, RoomType.Lab, RoomType.PartsPrep),
        V("hull.memoryalloy", VAnchor.Hull, "외벽 은빛 물결 판", every: 2),
        F("medbay.nano", FurnitureType.MedBed, "나노 주사 거치대 · 은빛 병", true),
        V("hull.damper", VAnchor.Room, "방 모서리 관성 감쇠 받침", every: 1),
        V("wall.sensormesh", VAnchor.Wall, "벽의 작은 감지 점 격자 (깜빡임)", every: 2, live: true),
        R("galley.vat", "배양 단백질 통 · 젓개", true, RoomType.Galley, RoomType.ProteinFarm, RoomType.Mess),
        V("exterior.radiator", VAnchor.Exterior, "큰 복사 냉각 날개 (달아오름)", live: true),
        R("lifesupport.photosynth", "잎맥 무늬 인공 광합성 판", true, RoomType.LifeSupport, RoomType.Hydroponics, RoomType.Garden, RoomType.AlgaeLab),
        // ── 6 초공간 ──
        V("engine.warp", VAnchor.Exterior, "배를 감싼 공간 왜곡 거품 윤곽", live: true),
        V("exterior.forcefield", VAnchor.Exterior, "외판 위 육각 역장 (쓸고 가는 빛)", live: true),
        F("panel.zeropoint", FurnitureType.PowerPanel, "영점 구슬 (떠서 빛난다)", true),
        R("server.quantum", "금빛 층층 양자 냉각대", true, RoomType.ServerRoom, RoomType.Bridge, RoomType.Lab),
        R("robotbay.replicator", "반쯤 짜인 로봇이 있는 복제 요람", true, RoomType.RobotBay, RoomType.DroneBay, RoomType.Workshop),
        V("hull.phase", VAnchor.Hull, "외판에 흐르는 위상 간섭 줄", every: 1, live: true),
        V("exterior.gravlens", VAnchor.Exterior, "별빛을 휘는 렌즈 고리", live: true),
        R("lounge.homeworld", "지구 풍경 창 (구름이 흐른다)", true, RoomType.Lounge, RoomType.Theater, RoomType.Garden, RoomType.Mess),
        R("workshop.forge", "플라스마 도가니 (달아오름)", true, RoomType.Workshop, RoomType.WeldingShop, RoomType.Crusher),
        R("bridge.hypernav", "별 지도 홀로그램 (도는 길)", true, RoomType.Bridge, RoomType.Navigation),
        // ── 새 기술 ──
        F("reactor.coils", FurnitureType.ReactorCore, "노심을 감은 초전도 코일"),
        F("reactor.diag", FurnitureType.ReactorCore, "진단 창 · 레이저 빛줄기", true),
        V("hull.radlayer", VAnchor.Hull, "외벽에 댄 납판 (Pb 찍힘 · 볼트)", every: 2),
        F("reactor.tokamak", FurnitureType.ReactorCore, "D자 코일 윤곽"),
        R("lifesupport.candles", "산소 양초 보관함 (붉은 뚜껑 줄)", false, RoomType.LifeSupport, RoomType.Storage, RoomType.GasStorage),
        R("lifesupport.algaetrough", "얕은 초록 조류 수조 (물결)", true, RoomType.LifeSupport, RoomType.Hydroponics, RoomType.AlgaeLab),
        V("hull.thickplate", VAnchor.Hull, "외판에 덧댄 두꺼운 판 · 큰 볼트", every: 1),
        F("growbed.soil", FurnitureType.GrowBed, "검은 흙 · 덩이 · 퇴비통"),
        R("bridge.mainframe", "중앙 컴퓨터 기둥 · 테이프 릴", true, RoomType.Bridge, RoomType.ServerRoom),
        V("panel.nodes", VAnchor.Machines, "설비마다 작은 제어 마디 · 이은 선", every: 1, live: true),
        V("hull.wreckalloy", VAnchor.Hull, "난파선 합금 덧판 (청동빛 · 옛 글씨)", every: 5),
        F("sensor.beacon", FurnitureType.SensorArray, "옛 신호기 부속 · 붉은 섬광", true),
        R("quarters.oldstation", "엽서 · 사진 · 뜨개 덮개", false, RoomType.Quarters, RoomType.PrivateCabins, RoomType.QuietQuarters, RoomType.Lounge),
        R("comms.convoy", "선단 깃발 · 연결 표시등", true, RoomType.Comms, RoomType.Bridge),
        R("workshop.safetysign", "노란 안전 표지 · 눈 세척대", false, RoomType.Workshop, RoomType.Lab, RoomType.ElectronicsLab, RoomType.WeldingShop),
        V("corridor.biolamp", VAnchor.Corridor, "통로 벽 생물 발광 띠 (숨 쉬는 청록)", every: 2, live: true),
        R("bridge.twinholo", "도는 철사 배 홀로그램", true, RoomType.Bridge, RoomType.BackupBridge),
        V("hull.printpatch", VAnchor.Hull, "찍어 붙인 벌집 패치", every: 4),
        F("waterplant.heat", FurnitureType.WaterRecycler, "폐열 증류기 · 응축 코일 · 김", true),
        V("exterior.swarm", VAnchor.Exterior, "외판을 기는 수리 로봇 떼", live: true),
        F("medbay.diagai", FurnitureType.MedBed, "진단 보조기 스캔 호 · 눈", true),
        F("growbed.heirloom", FurnitureType.GrowBed, "옛 씨앗 병 · 빨강 · 보라 열매"),
        F("panel.safegrid", FurnitureType.PowerPanel, "초록 무아크 차단기 줄", true),
        R("workshop.torch", "플라스마 절단기 거치대 · 호스", true, RoomType.Workshop, RoomType.WeldingShop),
        V("floor.retardant", VAnchor.Floor, "바닥 난연 코팅 광택 (주홍 테)", every: 5),
        V("floor.grip", VAnchor.Floor, "젖는 방 바닥 미끄럼 방지 돌기", every: 1, rooms: new[] { RoomType.Galley, RoomType.Hydroponics, RoomType.WaterPlant, RoomType.Laundry, RoomType.Decon, RoomType.Airlock, RoomType.Mess, RoomType.Garden }),
        F("galley.pressure", FurnitureType.Stove, "압력솥 · 김 빼는 추", true),
        R("galley.vacuum", "진공 포장기 · 납작한 봉지", false, RoomType.Galley, RoomType.Storage, RoomType.Freezer),
        R("portable.led", "저전력 작업등 충전 걸이", true, RoomType.Storage, RoomType.Workshop, RoomType.EvaPrep, RoomType.Cargo),
        R("portable.supercap", "초고용량 셀 충전대 (차오르는 막대)", true, RoomType.Storage, RoomType.Workshop, RoomType.EvaPrep, RoomType.BatteryRoom),
        F("suit.aramid", FurnitureType.SuitLocker, "우주복에 노란 아라미드 겹"),
        F("suit.selfpatch", FurnitureType.SuitLocker, "우주복 푸른 젤 패치"),
        V("exterior.salvagescan", VAnchor.Exterior, "잔해 탐지 접시 · 쓸고 가는 부채꼴", live: true),
        R("bridge.expmap", "원정 지도판 (다녀온 길)", false, RoomType.Bridge, RoomType.Navigation, RoomType.MeetingRoom),
        V("wall.blastvent", VAnchor.Wall, "터지면 열리는 배출 패널 · 화살표", every: 4, rooms: new[] { RoomType.Reactor, RoomType.Engine, RoomType.GasStorage, RoomType.PropellantTank, RoomType.FuelCell, RoomType.BatteryRoom, RoomType.Power }),
        V("wall.foamnozzle", VAnchor.Room, "천장 폭발 억제 거품 노즐", every: 1),
        V("sensor.gravwave", VAnchor.Exterior, "중력파 간섭계 두 팔 (빛 왕복)", live: true),
        V("shelter.cellar", VAnchor.Wall, "안쪽 방 두꺼운 차폐 블록 · 대피 표지", every: 1, rooms: new[] { RoomType.Shelter, RoomType.QuietQuarters, RoomType.Quarters, RoomType.Medbay }),
        F("server.watchdog", FurnitureType.MainComputer, "감시 타이머 숫자 (되감긴다)", true),
        V("floor.mounts", VAnchor.Machines, "설비 둘레 바닥 고정 레일 · 볼트 넷", every: 1),
        V("pipe.couplers", VAnchor.Pipes, "관 이음마다 빠른 이음 고리", every: 2),
        // ── 압축-마 기술 30 (TechWebV18) ──
        R("galley.picklejars", "줄지어 선 절임 병 · 소금 자루", false, RoomType.Galley, RoomType.Mess, RoomType.Storage),
        F("oven.starter", FurnitureType.BreadOven, "발효종 병 · 버들 바구니"),
        R("galley.templog", "서리 낀 온도 기록판 · 집게 연필", false, RoomType.Galley, RoomType.Medbay, RoomType.Freezer),
        F("stove.potring", FurnitureType.Stove, "냄비 둘레 감지 고리 · 작은 경고 화면", true),
        F("cattower.toys", FurnitureType.CatTower, "깃털 막대 · 긁개 판", true),
        R("storage.sealbins", "고무 패킹 곡물통 · 잠금 집게", false, RoomType.Storage, RoomType.Galley, RoomType.Cargo, RoomType.SeedVault),
        F("plantrack.straps", FurnitureType.PlantRack, "화분마다 초록 끈 · 받침 고리"),
        F("insectfarm.press", FurnitureType.InsectFarm, "가루 누름틀 · 말리는 채반"),
        F("composter.loop", FurnitureType.Composter, "퇴비에서 재배대로 가는 흙 관 · 온도 꽂이"),
        R("galley.greasejug", "기름 모으는 통 · 깔때기 · 손글씨 딱지", false, RoomType.Galley, RoomType.Mess),
        F("greywater.gauge", FurnitureType.GreywaterFilter, "맑기 눈금 띠 (탁하면 갈색)", true),
        F("compactor.bales", FurnitureType.Compactor, "철사로 묶은 덩어리 더미"),
        R("airlock.tetherboard", "끈 고리 판 · 고리쇠 줄", false, RoomType.Airlock, RoomType.Centrifuge, RoomType.EvaPrep, RoomType.Lounge),
        F("magboots.coils", FurnitureType.MagBootRack, "바닥 충전 코일 판 (빛이 돈다)", true),
        V("exterior.gyro", VAnchor.Exterior, "선체 옆 자이로 고리 (천천히 돈다)", live: true),
        F("cargonet.ratchet", FurnitureType.CargoNet, "노란 꼬리표 조임 고리 넷"),
        F("crashseat.harness", FurnitureType.CrashSeat, "빛나는 다섯 점 버클 · 어깨 패드"),
        V("hull.rcstags", VAnchor.Hull, "분사구 덮개마다 시험 꼬리표", every: 4),
        F("server.bootcache", FurnitureType.ServerRack, "곁에 붙은 예비 기억 상자 (파란 등)", true),
        F("vault.mirror", FurnitureType.RecorderVault, "광섬유로 이은 둘째 기록 상자", true),
        F("listening.scope", FurnitureType.ListeningPost, "주파수 막대 화면", true),
        R("mess.talkingstick", "새긴 발언 막대 · 말할 차례 판", false, RoomType.Mess, RoomType.MeetingRoom, RoomType.Lounge),
        F("memorial.candles", FurnitureType.MemorialWall, "촛불 줄 · 접은 종이꽃", true),
        R("lounge.bandposter", "악단 벽보 · 보면대", false, RoomType.Lounge, RoomType.Theater, RoomType.Mess),
        F("clothesrack.ions", FurnitureType.ClothesRack, "정전기 방지 손목띠 걸이 · 이온 막대", true),
        F("heatsuit.visor", FurnitureType.HeatSuitRack, "금빛 얼굴창 투구 선반 · 냉각 조끼"),
        R("workshop.eyewashsign", "초록 세척 표지 · 15분 점검표", false, RoomType.Workshop, RoomType.Lab, RoomType.WeldingShop, RoomType.ElectronicsLab),
        V("exterior.dockring", VAnchor.Exterior, "선체 옆 겹 씰 접안 고리"),
        V("exterior.wreckwatch", VAnchor.Exterior, "잔해 추적 안테나 (훑는다)", live: true),
        V("hull.shutters", VAnchor.Hull, "창마다 접힌 덧창", every: 3),
    };

    private static Dictionary<string, VisualRow>? _visuals;
    public static Dictionary<string, VisualRow> Visuals => _visuals ??= VisualRows.ToDictionary(r => r.Key);
    public static VisualRow? Visual(string key) => Visuals.TryGetValue(key, out var v) ? v : null;

    private static Dictionary<string, EraTech>? _techOf;
    /// <summary>Visual 열쇠 → 기술 (색 · 아이콘).</summary>
    public static EraTech? TechOf(string key)
    {
        _techOf ??= TechWeb.Every.GroupBy(TechWeb.Visual).ToDictionary(g => g.Key, g => g.First());
        return _techOf.TryGetValue(key, out var t) ? t : null;
    }

    /// <summary>익힌 기술의 Visual 열쇠 (표 순서 — 방 벽 자리를 고르는 차례가 늘 같다).</summary>
    public static List<VisualRow> Active(World w)
    {
        var res = new List<VisualRow>();
        foreach (var r in VisualRows)
            if (TechOf(r.Key) is EraTech t && w.Eras.Has(t.Id)) res.Add(r);
        return res;
    }

    /// <summary>갈림길에서 고른 쪽이면 배 전체에 더 짙게 (배 정체성). −1 아님 · 0 A · 1 B.</summary>
    public static bool Identity(World w, string key)
    {
        if (TechOf(key) is not EraTech t || TechWeb.ForkFor(t.Id) is not { } fs) return false;
        return w.TechWeb is TechWebSystem s && s.Side(fs.fork.Id) == fs.side && w.Eras.Has(t.Id);
    }

    // ═══════════════════════════════ ③④ 설비 단계 · 등급 ═══════════════════════════════

    private static PartAt[] P(string s)
    {
        var res = new List<PartAt>();
        foreach (var tok in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = tok.Split('@');
            var part = Enum.Parse<TierPart>(at[0], true);
            float u = 0.5f, v = 0.5f;
            if (at.Length > 1)
            {
                var uv = at[1].Split(',');
                u = float.Parse(uv[0], CultureInfo.InvariantCulture);
                v = float.Parse(uv[1], CultureInfo.InvariantCulture);
            }
            res.Add(new PartAt(part, u, v));
        }
        return res.ToArray();
    }

    private static TierKit K(FurnitureType t, Improv mk1, Trim mk3, uint glow, string ii, string iii, string iv) => new(t, mk1, mk3, glow, P(ii), P(iii), P(iv));

    public static readonly TierKit[] Kits =
    {
        K(FurnitureType.Bed, Improv.Rope, Trim.Pinstripe, 0xffc89aff, "cushion@.22,.5 lamp@.96,.12", "quilt@.62,.5 screen@.96,.82", "hood@.3,.5 strip@.5,.98"),
        K(FurnitureType.Seat, Improv.Rope, Trim.Pinstripe, 0xffb08aff, "cushion@.5,.55", "arm@.08,.5 chip@.92,.5", "shroud@.5,.15 strip@.5,.95"),
        K(FurnitureType.Table, Improv.Shim, Trim.Pinstripe, 0xe8c890ff, "drawers@.1,.82", "screen@.5,.5", "holo@.5,.5 strip@.5,.02"),
        K(FurnitureType.MealDispenser, Improv.DuctTape, Trim.EdgeGlow, 0xffd27aff, "gauges@.2,.2 vents@.8,.2", "screen@.5,.15 tank@.9,.72", "shroud@.5,.6 core@.5,.45"),
        K(FurnitureType.Console, Improv.ZipTies, Trim.GlassFace, 0x7fd4ffff, "gauges@.15,.3", "screen@.85,.3 antenna@.95,.05", "holo@.5,.4"),
        K(FurnitureType.ReactorCore, Improv.JumperWire, Trim.HaloRing, 0xffd36aff, "plating@.1,.1 gauges@.9,.1", "coil@.5,.02 cables@.02,.5", "field@.5,.5 shroud@.5,.96"),
        K(FurnitureType.EngineCore, Improv.WeldBead, Trim.HaloRing, 0x6cc8ffff, "coil@.82,.5", "field@.92,.5 fins@.3,.02", "core@.5,.5 strip@.5,.98"),
        K(FurnitureType.OxygenGenerator, Improv.DripCan, Trim.EdgeGlow, 0x6ee7b7ff, "pipeloop@.12,.5 gauges@.86,.18", "glass@.5,.5 filter@.9,.82", "field@.5,.5 strip@.5,.98"),
        K(FurnitureType.Workbench, Improv.BoltedFan, Trim.Chevron, 0xffb070ff, "rail@.5,.08", "arm@.86,.4 lamp@.08,.1", "holo@.5,.55 shroud@.5,.96"),
        K(FurnitureType.Shelf, Improv.Shim, Trim.Pinstripe, 0xd8c8a0ff, "drawers@.5,.5", "plating@.5,.95 chip@.95,.1", "strip@.5,.02 glass@.5,.5"),
        K(FurnitureType.MedBed, Improv.Cardboard, Trim.EdgeGlow, 0xff7a85ff, "screen@.92,.15", "arm@.5,.02 tank@.05,.8", "hood@.5,.5 holo@.9,.5"),
        K(FurnitureType.CoolantPump, Improv.HoseClamp, Trim.Anodized, 0x7cd4ffff, "gauges@.2,.2 valve@.8,.8", "coil@.5,.5 fins@.5,.02", "field@.5,.5 shroud@.5,.98"),
        K(FurnitureType.PowerPanel, Improv.JumperWire, Trim.HaloRing, 0xf2c230ff, "cables@.5,.98", "chip@.3,.3 screen@.75,.3", "core@.5,.5 strip@.02,.5"),
        K(FurnitureType.Battery, Improv.JumperWire, Trim.HaloRing, 0x5fe0d0ff, "cells@.5,.8", "fins@.5,.02 coil@.9,.5", "shroud@.5,.5 field@.5,.5"),
        K(FurnitureType.GrowBed, Improv.DripCan, Trim.EdgeGlow, 0xa6d65aff, "lamp@.5,.02", "pipeloop@.02,.5 pod@.98,.5", "glass@.5,.5 strip@.5,.98"),
        K(FurnitureType.WaterRecycler, Improv.HoseClamp, Trim.Anodized, 0x3a8fd9ff, "filter@.1,.8 valve@.9,.2", "tank@.9,.82 pipeloop@.5,.02", "glass@.5,.5 field@.5,.5"),
        K(FurnitureType.Stove, Improv.BoltedFan, Trim.Chevron, 0xff8a3cff, "hood@.5,.05", "gauges@.1,.9 vents@.9,.1", "glass@.5,.5 core@.5,.5"),
        K(FurnitureType.Fridge, Improv.DripCan, Trim.Anodized, 0x9ad0ffff, "gauges@.1,.1", "screen@.8,.2 fins@.5,.98", "shroud@.5,.5 strip@.5,.02"),
        K(FurnitureType.SuitLocker, Improv.Cardboard, Trim.GlassFace, 0xd8dee8ff, "drawers@.5,.95", "pod@.5,.1 cables@.98,.5", "glass@.5,.5 field@.5,.3"),
        K(FurnitureType.AuxGenerator, Improv.JumperWire, Trim.Anodized, 0x4aa3ffff, "vents@.1,.5 tank@.9,.8", "fins@.5,.02 gauges@.9,.1", "core@.5,.5 shroud@.5,.98"),
        K(FurnitureType.Cot, Improv.Rope, Trim.Pinstripe, 0xc8b890ff, "cushion@.5,.5", "quilt@.7,.5 lamp@.02,.1", "hood@.3,.5"),
        K(FurnitureType.Collector, Improv.WeldBead, Trim.Chevron, 0xe0b64aff, "arm@.5,.02", "dish@.9,.1 rotor@.1,.9", "field@.5,.02 holo@.5,.5"),
        K(FurnitureType.Refinery, Improv.BoltedFan, Trim.Chevron, 0xd69cffff, "coil@.5,.5", "fins@.5,.98 pipeloop@.02,.5", "glass@.5,.5 core@.5,.6"),
        K(FurnitureType.DroneDock, Improv.WeldBead, Trim.Chevron, 0x9fe0ffff, "antenna@.9,.1", "rail@.5,.9 lamp@.1,.1", "field@.5,.5 strip@.5,.98"),
        K(FurnitureType.MainComputer, Improv.ZipTies, Trim.GlassFace, 0x7fffd4ff, "cables@.5,.98", "fins@.98,.5 chip@.5,.2", "core@.5,.5 glass@.5,.5"),
        K(FurnitureType.SensorArray, Improv.ZipTies, Trim.GlassFace, 0x7fb2ffff, "dish@.8,.2", "pod@.2,.8 antenna@.9,.9", "field@.5,.5 holo@.5,.5"),
        K(FurnitureType.LedPanel, Improv.DuctTape, Trim.EdgeGlow, 0xff7fd0ff, "strip@.5,.02", "chip@.95,.5 fins@.5,.98", "holo@.5,.5"),
        K(FurnitureType.HeatExchanger, Improv.HoseClamp, Trim.Anodized, 0x56c8ffff, "fins@.5,.02", "valve@.1,.9 pipeloop@.9,.5", "coil@.5,.5 field@.5,.5"),
        K(FurnitureType.CapacitorBank, Improv.JumperWire, Trim.HaloRing, 0xc6e85aff, "cables@.5,.98", "coil@.5,.02 gauges@.9,.9", "core@.5,.5 field@.5,.5"),
        K(FurnitureType.Scrubber, Improv.DuctTape, Trim.Anodized, 0x7fd0c0ff, "filter@.9,.5", "rotor@.1,.2 vents@.5,.98", "glass@.5,.5 strip@.5,.02"),
        K(FurnitureType.Fabricator, Improv.BoltedFan, Trim.Chevron, 0xc8b27aff, "rail@.5,.02", "lamp@.95,.1 tank@.05,.9", "holo@.5,.5 arm@.9,.5"),
        K(FurnitureType.RobotDock, Improv.WeldBead, Trim.Chevron, 0xf2994aff, "cables@.5,.98", "lamp@.5,.02 screen@.9,.5", "field@.5,.5"),
        K(FurnitureType.SupplyCache, Improv.Cardboard, Trim.Pinstripe, 0xff6a5aff, "plating@.5,.5", "drawers@.5,.9 lamp@.9,.1", "glass@.5,.5 strip@.02,.5"),
        K(FurnitureType.PartTestBench, Improv.WeldBead, Trim.Chevron, 0xffd27aff, "gauges@.8,.2", "screen@.2,.2 arm@.9,.8", "holo@.5,.5 strip@.5,.98"),
        K(FurnitureType.Hoist, Improv.Rope, Trim.Chevron, 0xe0b040ff, "rail@.5,.02", "valve@.02,.5 lamp@.98,.5", "arm@.5,.5 strip@.5,.98"),
        K(FurnitureType.MaintCart, Improv.WeldBead, Trim.Chevron, 0xe0623eff, "drawers@.5,.5", "lamp@.9,.1 cables@.1,.9", "screen@.5,.2 shroud@.5,.98"),
        K(FurnitureType.VibrationMonitor, Improv.TapedScreen, Trim.GlassFace, 0x5ec8e6ff, "pod@.8,.8", "screen@.3,.3 antenna@.9,.1", "holo@.5,.5 chip@.1,.9"),
        K(FurnitureType.ThermalCamera, Improv.TapedScreen, Trim.GlassFace, 0xff9a5cff, "fins@.5,.98", "glass@.8,.5 pod@.1,.1", "field@.5,.5 strip@.5,.02"),
        K(FurnitureType.LeakDetector, Improv.TapedScreen, Trim.GlassFace, 0x4aa3e0ff, "pod@.5,.98", "valve@.1,.5 screen@.8,.3", "glass@.5,.5 core@.5,.5"),
        K(FurnitureType.CalibrationRig, Improv.ZipTies, Trim.Chevron, 0xa8e6ffff, "gauges@.2,.8", "rail@.5,.02 lamp@.9,.9", "holo@.5,.5 chip@.1,.1"),
        K(FurnitureType.Oven, Improv.BoltedFan, Trim.Chevron, 0xff7a3cff, "vents@.5,.02", "screen@.85,.15 gauges@.15,.85", "glass@.5,.5 strip@.5,.98"),
        K(FurnitureType.Lathe, Improv.BoltedFan, Trim.Chevron, 0xffc070ff, "lamp@.9,.1", "rail@.5,.98 rotor@.1,.5", "shroud@.5,.02 screen@.9,.9"),
        K(FurnitureType.SolderStation, Improv.BoltedFan, Trim.Chevron, 0xffa060ff, "lamp@.1,.1", "arm@.8,.3 vents@.5,.98", "holo@.5,.5 fins@.98,.5"),
        K(FurnitureType.DiagnosticScanner, Improv.TapedScreen, Trim.GlassFace, 0x8fe0ffff, "antenna@.9,.1", "screen@.5,.3 cables@.02,.8", "field@.5,.5 glass@.5,.5"),
        K(FurnitureType.NutrientDoser, Improv.HoseClamp, Trim.Anodized, 0xb8e05aff, "tank@.15,.5", "valve@.85,.5 gauges@.5,.1", "core@.5,.5 pipeloop@.5,.98"),
        K(FurnitureType.ToolWall, Improv.Shim, Trim.Pinstripe, 0xd9a35bff, "drawers@.5,.9", "lamp@.5,.02 chip@.9,.5", "strip@.5,.98 holo@.5,.5"),
        K(FurnitureType.ReactorSimulator, Improv.ZipTies, Trim.GlassFace, 0xffe08aff, "screen@.3,.3", "gauges@.8,.8 cables@.5,.98", "holo@.5,.5 core@.9,.1"),
        K(FurnitureType.NavComputer, Improv.ZipTies, Trim.GlassFace, 0x8fa6ffff, "chip@.2,.8", "antenna@.95,.05 fins@.5,.98", "holo@.5,.5 glass@.5,.5"),
        K(FurnitureType.DishWasher, Improv.HoseClamp, Trim.Anodized, 0x9ad0ffff, "valve@.1,.1", "tank@.9,.9 screen@.5,.1", "glass@.5,.5 strip@.5,.98"),
        K(FurnitureType.AirPurifier, Improv.DuctTape, Trim.EdgeGlow, 0xbff0ffff, "filter@.5,.5", "rotor@.5,.5 pod@.9,.1", "field@.5,.5 glass@.5,.5"),
        K(FurnitureType.Autoclave, Improv.HoseClamp, Trim.Anodized, 0xffe0c0ff, "gauges@.9,.1", "valve@.1,.9 vents@.5,.02", "shroud@.5,.5 core@.5,.5"),
        K(FurnitureType.DeconShower, Improv.HoseClamp, Trim.EdgeGlow, 0x8fffd0ff, "pipeloop@.5,.02", "valve@.9,.5 filter@.1,.9", "field@.5,.5 strip@.02,.5"),
        K(FurnitureType.WashingMachine, Improv.HoseClamp, Trim.Anodized, 0xa0c8ffff, "gauges@.2,.1", "tank@.9,.9 screen@.8,.1", "glass@.5,.5 strip@.5,.02"),
        K(FurnitureType.BlackoutCurtain, Improv.Rope, Trim.Pinstripe, 0x6a5fb0ff, "rail@.5,.02", "quilt@.5,.5", "chip@.95,.5 strip@.5,.98"),
        K(FurnitureType.NoiseDamper, Improv.Cardboard, Trim.Pinstripe, 0x8a7fd0ff, "quilt@.5,.5", "plating@.5,.02 pod@.9,.9", "field@.5,.5"),
        K(FurnitureType.WhiteNoise, Improv.Cardboard, Trim.EdgeGlow, 0xc0b8ffff, "vents@.5,.5", "antenna@.9,.1 screen@.1,.9", "holo@.5,.5 strip@.5,.02"),
        K(FurnitureType.CoffeeMachine, Improv.HoseClamp, Trim.Anodized, 0xc89060ff, "gauges@.2,.2", "tank@.9,.2 valve@.1,.8", "shroud@.5,.5 strip@.5,.98"),
        K(FurnitureType.Projector, Improv.TapedScreen, Trim.GlassFace, 0xe0e8ffff, "lamp@.5,.02", "fins@.5,.98 chip@.9,.5", "holo@.5,.5 glass@.5,.5"),
        K(FurnitureType.GameTable, Improv.Shim, Trim.Pinstripe, 0xb06a7eff, "screen@.5,.5", "drawers@.02,.5 strip@.5,.98", "holo@.5,.5 field@.5,.5"),
        K(FurnitureType.Bookshelf, Improv.Shim, Trim.Pinstripe, 0xc8a070ff, "lamp@.5,.02", "drawers@.5,.95 glass@.5,.5", "strip@.02,.5 screen@.9,.5"),
        K(FurnitureType.Aquarium, Improv.DripCan, Trim.EdgeGlow, 0x5fd0ffff, "filter@.9,.1", "lamp@.5,.02 pod@.1,.9", "field@.5,.5 strip@.5,.98"),
        K(FurnitureType.Treadmill, Improv.DuctTape, Trim.Chevron, 0xff9a7aff, "screen@.95,.5", "rail@.5,.02 fins@.5,.98", "holo@.9,.5 strip@.5,.5"),
        K(FurnitureType.PlantWall, Improv.DripCan, Trim.EdgeGlow, 0x8fd65aff, "pipeloop@.5,.98", "lamp@.5,.02 pod@.95,.5", "glass@.5,.5 strip@.02,.5"),
        K(FurnitureType.EmergencyLight, Improv.JumperWire, Trim.HaloRing, 0xff5c4cff, "cells@.5,.9", "glass@.5,.5 antenna@.9,.1", "core@.5,.5"),
        K(FurnitureType.SurgeProtector, Improv.JumperWire, Trim.HaloRing, 0xf2e030ff, "cables@.5,.98", "coil@.5,.5 gauges@.9,.1", "field@.5,.5 chip@.1,.1"),
        K(FurnitureType.FireBlanket, Improv.Cardboard, Trim.Pinstripe, 0xff6a4aff, "plating@.5,.9", "pod@.9,.1 strip@.5,.02", "glass@.5,.5"),
        K(FurnitureType.Dehumidifier, Improv.DripCan, Trim.Anodized, 0x8fc8e0ff, "tank@.9,.8", "rotor@.4,.4 filter@.9,.1", "glass@.5,.5 fins@.5,.02"),
        K(FurnitureType.AirlockPump, Improv.HoseClamp, Trim.Anodized, 0xa8b8d0ff, "valve@.1,.5", "gauges@.9,.1 pipeloop@.5,.98", "coil@.5,.5 shroud@.5,.02"),
        K(FurnitureType.SuitDryer, Improv.Cardboard, Trim.EdgeGlow, 0xffd0a0ff, "vents@.5,.02", "pod@.9,.9 rotor@.1,.1", "field@.5,.5 glass@.5,.5"),
        K(FurnitureType.SignalBooster, Improv.ZipTies, Trim.HaloRing, 0x5ec8e6ff, "antenna@.9,.1", "dish@.2,.2 coil@.5,.9", "field@.5,.5 holo@.5,.5"),
        // 압축-마 새 설비 30 (단계 II~IV 부품 · Mk.1 손질 · Mk.3 마감)
        K(FurnitureType.Fermenter, Improv.Rope, Trim.Anodized, 0xffb070ff, "gauges@.5,.1", "glass@.5,.85 valve@.9,.5", "shroud@.5,.5 screen@.1,.5"),
        K(FurnitureType.BreadOven, Improv.WeldBead, Trim.EdgeGlow, 0xff9a50ff, "glass@.5,.8", "gauges@.2,.2 vents@.8,.1", "screen@.9,.9 core@.5,.5"),
        K(FurnitureType.SpiceRack, Improv.Shim, Trim.Pinstripe, 0xe8c890ff, "drawers@.5,.95", "rail@.5,.02", "lamp@.5,.05 glass@.5,.5"),
        K(FurnitureType.IceMaker, Improv.DuctTape, Trim.GlassFace, 0x9ad8ffff, "filter@.95,.5", "pipeloop@.1,.9 tank@.9,.1", "chip@.5,.15 coil@.5,.9"),
        K(FurnitureType.PlantRack, Improv.ZipTies, Trim.Pinstripe, 0xc8a0ffff, "rail@.5,.3", "lamp@.5,.02 strip@.5,.98", "dish@.9,.1 pipeloop@.1,.5"),
        K(FurnitureType.CatTower, Improv.Rope, Trim.Pinstripe, 0xffc89aff, "cushion@.25,.7", "arm@.15,.7 quilt@.75,.7", "hood@.68,.12 lamp@.9,.9"),
        K(FurnitureType.PestTrap, Improv.TapedScreen, Trim.EdgeGlow, 0xa070ffff, "screen@.73,.1", "antenna@.08,.1 chip@.92,.9", "field@.5,.6 lamp@.5,.02"),
        K(FurnitureType.InsectFarm, Improv.ZipTies, Trim.Chevron, 0xb8e070ff, "valve@.02,.06", "hood@.5,.04 rotor@.9,.9", "pod@.1,.9 glass@.5,.5"),
        K(FurnitureType.GreaseTrap, Improv.HoseClamp, Trim.Anodized, 0xd8b44aff, "gauges@.78,.3", "coil@.5,.25 filter@.2,.8", "tank@.9,.8 chip@.1,.2"),
        K(FurnitureType.Compactor, Improv.WeldBead, Trim.Chevron, 0xe0b64aff, "cables@.5,.8", "hood@.5,.03 arm@.9,.4", "core@.5,.5 shroud@.5,.9"),
        K(FurnitureType.Composter, Improv.Rope, Trim.Pinstripe, 0xc8b070ff, "quilt@.5,.2", "pipeloop@.5,.02 vents@.9,.5", "gauges@.8,.8 rotor@.1,.48"),
        K(FurnitureType.GreywaterFilter, Improv.HoseClamp, Trim.GlassFace, 0x9ad0ffff, "lamp@.03,.5", "filter@.97,.5 valve@.5,.98", "chip@.5,.05 field@.5,.5"),
        K(FurnitureType.GrabRail, Improv.Rope, Trim.Chevron, 0x5fd0c8ff, "rail@.5,.7", "cushion@.1,.5 cables@.9,.5", "strip@.5,.2 lamp@.5,.9"),
        K(FurnitureType.CargoNet, Improv.ZipTies, Trim.Chevron, 0xe0a020ff, "gauges@.5,.49", "arm@.95,.49 rail@.5,.02", "field@.5,.5 chip@.05,.05"),
        K(FurnitureType.CrashSeat, Improv.DuctTape, Trim.Pinstripe, 0x6ee7b7ff, "cushion@.5,.3", "rail@.5,.95 arm@.05,.5", "hood@.5,.05 screen@.95,.5"),
        K(FurnitureType.MagBootRack, Improv.JumperWire, Trim.EdgeGlow, 0xffb070ff, "cells@.3,.22", "screen@.5,.08 coil@.5,.9", "field@.5,.5 antenna@.95,.05"),
        K(FurnitureType.ServerRack, Improv.JumperWire, Trim.GlassFace, 0x5fd0c8ff, "chip@.75,.2", "pipeloop@.02,.5 fins@.98,.5", "holo@.5,.5 core@.5,.9"),
        K(FurnitureType.RecorderVault, Improv.BoltedFan, Trim.Anodized, 0xe8641eff, "antenna@.25,.2", "plating@.5,.5 cables@.9,.9", "field@.5,.5 dish@.1,.1"),
        K(FurnitureType.ListeningPost, Improv.TapedScreen, Trim.HaloRing, 0x6ee7b7ff, "antenna@.15,.85", "screen@.75,.88 dish@.2,.2", "holo@.75,.7 field@.4,.4"),
        K(FurnitureType.MeetingBoard, Improv.Cardboard, Trim.Pinstripe, 0xf8e070ff, "drawers@.75,.4", "rail@.92,.3 lamp@.5,.02", "screen@.5,.4 holo@.5,.2"),
        K(FurnitureType.MemorialWall, Improv.Shim, Trim.HaloRing, 0xffcc66ff, "lamp@.85,.1", "strip@.5,.02 glass@.42,.12", "holo@.5,.35 field@.5,.8"),
        K(FurnitureType.MusicCorner, Improv.JumperWire, Trim.EdgeGlow, 0xff7a9aff, "dish@.55,.82", "drawers@.55,.65 antenna@.86,.06", "holo@.3,.2 strip@.5,.98"),
        K(FurnitureType.LabStill, Improv.DripCan, Trim.GlassFace, 0x6aa8ffff, "gauges@.2,.4", "glass@.54,.2 coil@.72,.45", "core@.3,.6 valve@.9,.9"),
        K(FurnitureType.ClothesRack, Improv.ZipTies, Trim.Pinstripe, 0x5fd0c8ff, "rail@.5,.05", "coil@.5,.36 vents@.5,.95", "vents@.9,.9 field@.5,.5"),
        K(FurnitureType.SewingMachine, Improv.Shim, Trim.Pinstripe, 0xfff0c0ff, "lamp@.24,.52", "dish@.36,.76 drawers@.5,.95", "arm@.9,.2 screen@.6,.05"),
        K(FurnitureType.EyeWash, Improv.HoseClamp, Trim.Chevron, 0x9ad0ffff, "cables@.85,.7", "tank@.9,.4 valve@.1,.6", "chip@.5,.95 shroud@.5,.6"),
        K(FurnitureType.OxygenMaskBox, Improv.DuctTape, Trim.EdgeGlow, 0xf0c020ff, "hood@.88,.82", "chip@.12,.82 tank@.5,.82", "field@.5,.35 filter@.95,.3"),
        K(FurnitureType.HeatSuitRack, Improv.Rope, Trim.Anodized, 0xd8a830ff, "quilt@.15,.4", "tank@.2,.85 fins@.8,.6", "shroud@.45,.4 coil@.9,.1"),
        K(FurnitureType.DockClampPanel, Improv.JumperWire, Trim.HaloRing, 0x5ec8e6ff, "valve@.4,.5", "holo@.4,.5 gauges@.15,.15", "field@.5,.5 plating@.9,.9"),
        K(FurnitureType.Telescope, Improv.Shim, Trim.GlassFace, 0x9ac8ffff, "rotor@.5,.62", "screen@.2,.75 antenna@.8,.2", "dish@.8,.2 core@.5,.62"),
        // 의료 2차
        K(FurnitureType.Dialyzer, Improv.HoseClamp, Trim.GlassFace, 0x6ef0b0ff, "filter@.9,.6", "pipeloop@.6,.7 screen@.5,.25", "pod@.3,.85 chip@.85,.15"),
        K(FurnitureType.Ecmo, Improv.TapedScreen, Trim.EdgeGlow, 0xff6a6aff, "tank@.9,.15", "coil@.5,.85 gauges@.2,.35", "core@.62,.5 holo@.2,.2"),
        K(FurnitureType.HeartPump, Improv.JumperWire, Trim.Pinstripe, 0xff8a9aff, "cells@.5,.65", "lamp@.5,.08 cables@.3,.9", "field@.5,.32 chip@.9,.9"),
        K(FurnitureType.OrganCooler, Improv.DuctTape, Trim.Anodized, 0xbfe6ffff, "fins@.83,.7", "glass@.45,.5 vents@.2,.95", "shroud@.45,.5 strip@.5,.96"),
        K(FurnitureType.BioPrinter, Improv.ZipTies, Trim.HaloRing, 0xe86a9aff, "arm@.5,.24", "lamp@.12,.45 drawers@.75,.88", "holo@.5,.4 rail@.5,.95"),
        K(FurnitureType.NegPressure, Improv.BoltedFan, Trim.Chevron, 0x5fb0ffff, "filter@.78,.4", "rotor@.36,.5 hood@.78,.05", "field@.36,.5 plating@.9,.9"),
    };

    private static Dictionary<FurnitureType, TierKit>? _kits;
    public static TierKit? Kit(FurnitureType t) => (_kits ??= Kits.ToDictionary(k => k.Type)).TryGetValue(t, out var k) ? k : null;

    public static string ImprovName(Improv i) => i switch
    {
        Improv.DuctTape => "은색 테이프", Improv.HoseClamp => "호스 조임쇠", Improv.JumperWire => "임시 점퍼선", Improv.ZipTies => "케이블 타이",
        Improv.Rope => "밧줄 묶음", Improv.Shim => "괸 쐐기", Improv.TapedScreen => "테이프 붙인 화면", Improv.DripCan => "받친 깡통",
        Improv.BoltedFan => "볼트로 단 선풍기", Improv.WeldBead => "거친 용접 덧살", _ => "판지 덧댐",
    };

    public static string TrimName(Trim t) => t switch
    {
        Trim.EdgeGlow => "빛나는 테두리", Trim.Chevron => "갈매기 문장", Trim.GlassFace => "유리 앞판", Trim.Anodized => "양극 산화 마감",
        Trim.Pinstripe => "가는 장식 줄", _ => "빛 고리",
    };

    // ═══════════════════════════════ ⑤ 개조 칸 ═══════════════════════════════

    /// <summary>새 패널이 낡아 보통이 되기까지 (일).</summary>
    public const float FreshDays = 12f;

    /// <summary>이 방을 마지막으로 고쳐 지은 때 (칸막이 · 승무원 안건 공사 완료 · 모듈 설치 · 칸막이 회의). 없으면 −1.</summary>
    public static long RemodelSince(World w, Room r)
    {
        long best = -1;
        if (r.SplitFrom != null) best = Math.Max(best, r.SplitSince);
        foreach (var o in w.Ship.Rooms)
            if (o.SplitFrom == r && !o.Merged) best = Math.Max(best, o.SplitSince);
        if (w.RoomPlans is RoomPlanSystem rp)
            foreach (var p in rp.Plans)
                if (p.Done >= 0 && (p.RoomId == r.Id || p.NewRoomId == r.Id || p.TargetRoomId == r.Id) && p.Kind != RoomPlanKind.Rename)
                    best = Math.Max(best, p.Done);
        foreach (var m in r.Marks)
            if (m.Text.Contains("설치") || m.Text.Contains("칸막이") || m.Text.Contains("들어옴")) best = Math.Max(best, m.Tick);
        return best;
    }

    /// <summary>새것인 정도 1(막 끝남) → 0(보통으로 낡음). 고친 적 없으면 0.</summary>
    public static float Freshness(long now, long since)
    {
        if (since < 0 || now < since) return 0f;
        float days = (now - since) / (float)SimTime.TicksPerDay;
        return Math.Clamp(1f - days / FreshDays, 0f, 1f);
    }

    // ═══════════════════════════════ ⑥ 설치 · 업그레이드 순간 ═══════════════════════════════

    /// <summary>순간 연출 길이 (틱 — 보통 빠르기에서 몇 초).</summary>
    public static readonly int MomentTicks = SimTime.Minutes(10);

    /// <summary>이력 줄 → 순간 종류 (Core 가 남긴 글을 읽기만 한다).</summary>
    public static MomentKind Classify(Machine m, string text)
    {
        if (text.Contains("Mk.3 개량")) return MomentKind.Mk3;
        if (text.Contains("Mk.1 임시품")) return MomentKind.Mk1;
        if (text.Contains("정품 복원")) return MomentKind.Restore;
        if (text.EndsWith(": 달았다")) return MomentKind.Install;
        if (text.EndsWith("단계로 손봤다")) return MomentKind.Upgrade; // 압축-마 기술이 새 설비 단계를 올렸다
        if (text.Contains("다시 짜 맞춤")) return MomentKind.Reassemble;
        if (text.Contains(" → "))
        {
            var tn = Tech.Of(m).Name;
            return m.Tier > 1 && tn.Length > 0 && text.EndsWith(tn) ? MomentKind.Upgrade : MomentKind.Reassemble;
        }
        return MomentKind.None;
    }

    /// <summary>이 설비의 가장 최근 설치 · 업그레이드 순간 (종류 · 때). 없으면 (None, −1).</summary>
    public static (MomentKind kind, long tick) MomentOf(Machine m)
    {
        for (int i = m.Marks.Count - 1; i >= 0; i--)
        {
            var k = Classify(m, m.Marks[i].Text);
            if (k != MomentKind.None) return (k, m.Marks[i].Tick);
        }
        return (MomentKind.None, -1);
    }

    /// <summary>순간 진행 0~1 (끝났거나 없으면 −1).</summary>
    public static float MomentPhase(long now, long tick)
    {
        if (tick < 0 || now < tick) return -1f;
        long d = now - tick;
        return d >= MomentTicks ? -1f : d / (float)MomentTicks;
    }

    /// <summary>방금 익힌 기술 (Visual 열쇠 · 때) — 그 모습이 처음 나타날 때 조립 · 불꽃 · 첫 점등.</summary>
    public static IEnumerable<(string key, long tick)> RecentLearned(World w)
    {
        if (w.TechWeb is not TechWebSystem s) yield break;
        for (int i = s.Learned.Count - 1; i >= 0; i--)
        {
            var (id, tick) = s.Learned[i];
            if (w.Tick - tick >= MomentTicks) break;
            if (TechWeb.Find(id) is EraTech t) yield return (TechWeb.Visual(t), tick);
        }
    }
}
