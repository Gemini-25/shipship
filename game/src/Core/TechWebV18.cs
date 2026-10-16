using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 압축-마 기술 111 → 141: 새 시스템마다 기술 줄기 (조리 · 생태 · 배수 · 무중력 · 기동 · 컴퓨터 · 소리 · 사람 · 옷 · 도킹 · 우주급) 30.
// 한 줄 = 기술 하나: 시대 · 분야 · 이름 · 값 · 무엇을 · 효과(사고 키 배율 · 새 시스템 키) · 위험 + 마디(선행 · 아이콘 · 배 모습 열쇠 · 조건 · 숨김 · 조합)
//   + 설비 단계(Lifts: 익히면 그 설비가 II~IV 단계로 — 모양이 바뀌고 효과가 커진다 · 없던 설비는 달고 싶어진다).
// 갈림길 2쌍(곡물 벌레 · 무중력) · 숨은 기술 4(조합 셋 · 사고로 열리는 것 하나) · 부작용 연쇄 3.

public static class TechWebV18
{
    private static (string, float)[] X(params (string, float)[] fx) => fx;
    private static string H(HazardKind k) => k.ToString();

    private static string KeyName(string s) => Enum.TryParse<HazardKind>(s, out var k) ? Hazards.Name(k) : s switch
    {
        "food.rot" => "음식 상함", "cook.hours" => "조리 시간", "room.risk" => "방 공사 사고", "exp.relic" => "원정 유물 발견", "cosmic.expose" => "대재난 노출", _ => s,
    };

    private static TechWeb.Row T(string id, int era, TechField field, string name, float cost, string what, (string, float)[] fx, string risk = "없음", string? key = null, float mul = 1f)
    {
        string d = string.Join(" · ", fx.Select(f => $"{KeyName(f.Item1)} {(f.Item2 >= 1f ? "+" : "−")}{MathF.Round(MathF.Abs(f.Item2 - 1f) * 100f):0}%"));
        return new(new EraTech(id, era, field, name, cost, fx.Length > 0 ? $"{what} ({d})" : what, risk, key, mul), fx);
    }

    public static readonly TechWeb.Row[] Rows =
    {
        // 조리
        T("pickling", 1, TechField.Food, "절임 비법", 30f, "채소를 소금물에 담가 오래 둔다", X(("food.rot", 0.85f), (H(HazardKind.FermentBurst), 0.8f)), "항아리가 늘었다 — 터짐", H(HazardKind.FermentBurst), 1.05f),
        T("sourdough", 1, TechField.Food, "천연 발효종", 32f, "밀가루와 물로 키운 발효종으로 빵을 굽는다", X((H(HazardKind.PotFire), 0.9f), ("cook.hours", 0.95f))),
        T("coldchain", 2, TechField.Food, "냉장 사슬", 42f, "만들고 나르고 쌓는 내내 차갑게 둔다", X((H(HazardKind.FoodPoisoning), 0.85f), (H(HazardKind.HeatExhaustion), 0.85f), ("food.rot", 0.85f))),
        T("smartgalley", 3, TechField.Computing, "조리 감시기", 62f, "불 위에 둔 냄비를 감지기가 지켜본다", X((H(HazardKind.PotFire), 0.6f), (H(HazardKind.ScaldSpill), 0.8f), (H(HazardKind.GreaseFire), 0.85f))),
        // 생태
        T("catcare", 1, TechField.Habitat, "고양이 돌봄법", 30f, "고양이가 숨을 곳과 높은 자리를 마련한다", X((H(HazardKind.CatScratch), 0.6f), (H(HazardKind.CatLost), 0.7f), (H(HazardKind.WeevilSwarm), 0.85f))),
        T("sealedbins", 1, TechField.Food, "밀폐 곡물통", 30f, "곡물을 고무 패킹 통에 잠근다", X((H(HazardKind.WeevilSwarm), 0.55f)), "통 속 습기 — 곰팡이", H(HazardKind.MoldOutbreak), 1.05f),
        T("plantstraps", 1, TechField.Habitat, "화분 고정법", 28f, "화분마다 끈과 받침을 단다", X((H(HazardKind.PlantTopple), 0.5f), (H(HazardKind.SpaceSick), 0.95f))),
        T("insectprotein", 2, TechField.Food, "곤충 단백질", 44f, "귀뚜라미를 길러 가루를 낸다", X(("food.rot", 0.9f), (H(HazardKind.SeedRot), 0.9f))),
        T("compostloop", 2, TechField.Life, "퇴비 순환", 44f, "음식물 찌꺼기를 흙으로 되돌린다", X((H(HazardKind.NutrientCrash), 0.85f), (H(HazardKind.SeedRot), 0.85f), (H(HazardKind.DrainBackflow), 0.9f)), "퇴비 곁 습기 — 곰팡이", H(HazardKind.MoldOutbreak), 1.08f),
        // 배수 · 재활용
        T("greasecode", 1, TechField.Life, "기름 따로 모으기", 28f, "기름은 개수대에 붓지 않고 통에 모은다", X((H(HazardKind.DrainBackflow), 0.7f), (H(HazardKind.GreywaterJam), 0.85f))),
        T("greywater", 2, TechField.Life, "회색수 재활용", 46f, "씻은 물을 걸러 다시 쓴다", X((H(HazardKind.GreywaterJam), 0.6f), (H(HazardKind.TankSludge), 0.85f), (H(HazardKind.Backflow), 0.9f))),
        T("baler", 2, TechField.Fabrication, "압축 결속", 42f, "쓰레기를 눌러 철사로 묶는다", X(("room.risk", 0.95f), (H(HazardKind.DrainBackflow), 0.95f))),
        // 무중력 · 기동
        T("tetherdrill", 1, TechField.Habitat, "끈 매기 훈련", 28f, "무게가 사라지면 먼저 몸을 묶는다", X((H(HazardKind.SpaceSick), 0.8f), (H(HazardKind.GravityHiccup), 0.8f), (H(HazardKind.GravityFailure), 0.9f))),
        T("magsoles", 1, TechField.Defense, "자석 밑창", 34f, "신발 밑창에 전자석을 넣는다", X((H(HazardKind.SpaceSick), 0.7f), (H(HazardKind.GravityFailure), 0.85f)), "쇳가루 · 정전기", H(HazardKind.StaticZap), 1.08f),
        T("gyrostab", 3, TechField.Propulsion, "자이로 안정기", 66f, "도는 바퀴로 배의 흔들림을 붙잡는다", X((H(HazardKind.GravityHiccup), 0.6f), (H(HazardKind.GravityFailure), 0.6f), (H(HazardKind.ManeuverJolt), 0.8f), (H(HazardKind.TidalPull), 0.8f))),
        T("cargolash", 1, TechField.Fabrication, "화물 결속법", 28f, "짐마다 끈 두 줄 · 걸쇠 하나", X((H(HazardKind.ManeuverJolt), 0.8f), (H(HazardKind.TidalPull), 0.85f), (H(HazardKind.PlantTopple), 0.9f))),
        T("crashharness", 2, TechField.Defense, "다점 안전띠", 42f, "다섯 점으로 몸을 붙드는 띠", X((H(HazardKind.ManeuverJolt), 0.75f), (H(HazardKind.HatchFall), 0.9f))),
        T("rcsdiag", 2, TechField.Propulsion, "분사 밸브 진단", 44f, "자세 제어 밸브를 날마다 짧게 시험한다", X((H(HazardKind.ManeuverJolt), 0.6f))),
        // 컴퓨터 · 기록 · 소리
        T("bootcache", 2, TechField.Computing, "예비 부팅 기억장치", 42f, "다시 켤 때 읽을 것을 따로 둔다", X((H(HazardKind.RebootGlitch), 0.6f), (H(HazardKind.ComputerFault), 0.9f))),
        T("vaultcopy", 2, TechField.Computing, "기록 사본 금고", 44f, "블랙박스 기록을 다른 곳에 한 벌 더 둔다", X((H(HazardKind.BlackboxGap), 0.5f))),
        T("acoustic", 2, TechField.Sensors, "소리 진단", 42f, "도는 설비의 소리 결을 귀로 익힌다", X((H(HazardKind.BearingWhine), 0.6f), (H(HazardKind.FanImbalance), 0.85f), (H(HazardKind.BearingSeize), 0.9f))),
        // 사람
        T("talkingstick", 1, TechField.Habitat, "발언 막대 규칙", 26f, "막대를 쥔 사람만 말한다", X((H(HazardKind.MeetingBrawl), 0.6f), (H(HazardKind.PanicAttack), 0.95f))),
        T("remembrance", 1, TechField.Habitat, "추모 의식", 26f, "떠난 사람의 이름을 함께 부른다", X((H(HazardKind.PanicAttack), 0.85f))),
        T("shipband", 2, TechField.Habitat, "배 악단", 40f, "저녁마다 모여 악기를 맞춘다", X((H(HazardKind.MeetingBrawl), 0.8f), (H(HazardKind.PanicAttack), 0.9f))),
        // 옷 · 안전
        T("antistatic", 2, TechField.Defense, "정전기 방지 섬유", 40f, "옷감에 가는 금속실을 섞는다", X((H(HazardKind.StaticZap), 0.5f), (H(HazardKind.StaticDischarge), 0.85f))),
        T("heatsuit", 2, TechField.Defense, "알루미늄 방열복", 42f, "은빛 겹옷이 열을 되비춘다", X((H(HazardKind.HeatExhaustion), 0.5f))),
        T("eyewashcode", 1, TechField.Medical, "세척 수칙", 26f, "튀면 먼저 15분 씻는다", X((H(HazardKind.PumpShock), 0.8f), (H(HazardKind.ScaldSpill), 0.85f), (H(HazardKind.StaticZap), 0.9f))),
        // 도킹 · 바깥 · 우주급
        T("dockseal", 3, TechField.Hull, "겹 씰 접안 고리", 64f, "씰 두 겹이 차례로 맞물린다", X((H(HazardKind.DockSealFail), 0.5f), (H(HazardKind.HatchSeal), 0.85f))),
        T("wreckwatch", 3, TechField.Sensors, "난파선 추적", 62f, "부서진 배의 조각을 하나하나 쫓는다", X((H(HazardKind.WreckDrift), 0.6f), (H(HazardKind.DebrisAlert), 0.85f), ("exp.relic", 1.2f))),
        T("flashshutter", 4, TechField.Hull, "섬광 덧창", 92f, "창마다 순간에 닫히는 덧창", X((H(HazardKind.GammaFlash), 0.5f), (H(HazardKind.MagnetarPulse), 0.7f), ("cosmic.expose", 0.9f))),
    };

    private static TechGate G(GateKind k, string key, float need, string text) => new(k, key, need, text);
    private static WebNode N(string id, string pre, string icon, string visual, TechGate? gate = null, bool hidden = false, string? combo = null)
        => new(id, pre.Length == 0 ? Array.Empty<string>() : pre.Split(' '), icon, visual, gate, hidden, combo?.Split(' '));

    public static readonly WebNode[] Nodes =
    {
        N("pickling", "seedbank hygiene", "seed+drop", "galley.picklejars"),
        N("sourdough", "hygiene", "flame+seed", "oven.starter"),
        N("coldchain", "pickling coolantdope", "snow+drop", "galley.templog"),
        N("smartgalley", "", "chip+flame", "stove.potring", hidden: true, combo: "coldchain sourdough"),
        N("catcare", "hygiene", "hand+heart", "cattower.toys"),
        N("sealedbins", "seedbank", "bug+slash", "storage.sealbins"),
        N("plantstraps", "toolboard", "leaf+hex", "plantrack.straps"),
        N("insectprotein", "nftloop vcd", "bug+heart", "insectfarm.press"),
        N("compostloop", "genecrops", "seed+ring", "composter.loop"),
        N("greasecode", "hygiene", "drop+slash", "galley.greasejug"),
        N("greywater", "greasecode coolantdope", "drop+ring", "greywater.gauge"),
        N("baler", "toolboard hygiene", "cube+gear", "compactor.bales"),
        N("tetherdrill", "checklist", "hand+ring", "airlock.tetherboard"),
        N("magsoles", "rcd", "magnet+hand", "magboots.coils"),
        N("gyrostab", "rcsthruster magshield", "ring+gear", "exterior.gyro", G(GateKind.Incident, nameof(HazardKind.GravityHiccup), 1, "중력이 한 번은 끊겨 봐야 흔들림을 붙잡을 생각을 한다"), hidden: true),
        N("cargolash", "weldcode", "cube+net", "cargonet.ratchet"),
        N("crashharness", "cargolash triage", "heart+ring", "crashseat.harness"),
        N("rcsdiag", "rcsthruster vibelisten", "arrow+eye", "hull.rcstags"),
        N("bootcache", "labnotes rcd", "chip+arrow", "server.bootcache"),
        N("vaultcopy", "bootcache weldcode", "book+shield", "vault.mirror"),
        N("acoustic", "vibelisten toolboard", "wave+gear", "listening.scope"),
        N("talkingstick", "checklist", "book+hand", "mess.talkingstick"),
        N("remembrance", "triage", "star+heart", "memorial.candles"),
        N("shipband", "", "wave+heart", "lounge.bandposter", hidden: true, combo: "talkingstick remembrance"),
        N("antistatic", "rcd hygiene", "bolt+hex", "clothesrack.ions"),
        N("heatsuit", "fireproof coolantdope", "flame+shield", "heatsuit.visor"),
        N("eyewashcode", "toolboard", "drop+eye", "workshop.eyewashsign"),
        N("dockseal", "weldcode habflow", "ring+plate", "exterior.dockring"),
        N("wreckwatch", "vibelisten rcsthruster", "lens+plate", "exterior.wreckwatch", G(GateKind.Incident, nameof(HazardKind.WreckDrift), 1, "부서진 배의 조각을 한 번은 맞아 봐야 쫓을 생각을 한다")),
        N("flashshutter", "", "sun+shield", "hull.shutters", hidden: true, combo: "radshield wreckwatch"),
    };

    public static readonly TechFork[] Forks =
    {
        new("pests", "곡물 벌레를 무엇으로 막을까", "catcare", "sealedbins", "고양이가 지키는 배", "봉인된 창고의 배",
            new[] { H(HazardKind.WeevilSwarm) }, new[] { H(HazardKind.MoldOutbreak), H(HazardKind.CatScratch) }),
        new("float", "무게가 사라지면 무엇에 기댈까", "tetherdrill", "magsoles", "끈으로 묶인 배", "쇠 발의 배",
            new[] { H(HazardKind.GravityFailure), H(HazardKind.SpaceSick) }, new[] { H(HazardKind.StaticZap), H(HazardKind.MagnetarPulse) }),
    };

    public static readonly TechChainSpec[] Chains =
    {
        new("pickling", 12f, H(HazardKind.FermentBurst), 1.1f, 2f, "절임 비법 — 항아리가 늘었다, 날마다 김을 빼자"),
        new("magsoles", 16f, H(HazardKind.StaticZap), 1.1f, 2f, "자석 밑창 — 바닥에 쇳가루가 붙고 손끝이 자주 튄다"),
        new("compostloop", 12f, H(HazardKind.MoldOutbreak), 1.08f, 2f, "퇴비 순환 — 퇴비 통 곁이 늘 눅눅하다", CustomKind.HandWash),
    };

    /// <summary>기술 → 설비 단계: 익히면 그 설비가 이 단계로 (모양 · 효과).</summary>
    public static readonly (string tech, FurnitureType type, int tier)[] Lifts =
    {
        ("pickling", FurnitureType.Fermenter, 2), ("coldchain", FurnitureType.Fermenter, 3), ("smartgalley", FurnitureType.Fermenter, 4),
        ("sourdough", FurnitureType.BreadOven, 2), ("smartgalley", FurnitureType.BreadOven, 3), ("sourdough", FurnitureType.SpiceRack, 2),
        ("coldchain", FurnitureType.IceMaker, 2), ("smartgalley", FurnitureType.IceMaker, 3),
        ("catcare", FurnitureType.CatTower, 2), ("sealedbins", FurnitureType.PestTrap, 2), ("plantstraps", FurnitureType.PlantRack, 2),
        ("insectprotein", FurnitureType.InsectFarm, 2), ("compostloop", FurnitureType.InsectFarm, 3), ("compostloop", FurnitureType.Composter, 2),
        ("greasecode", FurnitureType.GreaseTrap, 2), ("greywater", FurnitureType.GreaseTrap, 3), ("greywater", FurnitureType.GreywaterFilter, 2), ("baler", FurnitureType.Compactor, 2),
        ("tetherdrill", FurnitureType.GrabRail, 2), ("magsoles", FurnitureType.GrabRail, 3), ("gyrostab", FurnitureType.GrabRail, 4),
        ("magsoles", FurnitureType.MagBootRack, 2), ("gyrostab", FurnitureType.MagBootRack, 3),
        ("cargolash", FurnitureType.CargoNet, 2), ("crashharness", FurnitureType.CargoNet, 3), ("crashharness", FurnitureType.CrashSeat, 2), ("rcsdiag", FurnitureType.CrashSeat, 3),
        ("bootcache", FurnitureType.ServerRack, 2), ("vaultcopy", FurnitureType.ServerRack, 3), ("vaultcopy", FurnitureType.RecorderVault, 2), ("flashshutter", FurnitureType.RecorderVault, 3),
        ("acoustic", FurnitureType.ListeningPost, 2),
        ("talkingstick", FurnitureType.MeetingBoard, 2), ("shipband", FurnitureType.MeetingBoard, 3), ("remembrance", FurnitureType.MemorialWall, 2), ("shipband", FurnitureType.MemorialWall, 3), ("shipband", FurnitureType.MusicCorner, 2),
        ("antistatic", FurnitureType.ClothesRack, 2), ("antistatic", FurnitureType.SewingMachine, 2), ("heatsuit", FurnitureType.SewingMachine, 3), ("heatsuit", FurnitureType.HeatSuitRack, 2),
        ("eyewashcode", FurnitureType.EyeWash, 2), ("eyewashcode", FurnitureType.OxygenMaskBox, 2),
        ("dockseal", FurnitureType.DockClampPanel, 2), ("wreckwatch", FurnitureType.DockClampPanel, 3), ("wreckwatch", FurnitureType.Telescope, 2), ("flashshutter", FurnitureType.Telescope, 3), ("flashshutter", FurnitureType.LabStill, 2),
    };

    public static int TierFor(World w, FurnitureType t)
    {
        int best = 1;
        foreach (var (tech, type, tier) in Lifts) if (type == t && tier > best && w.Eras.Known.Contains(tech)) best = tier;
        return best;
    }

    /// <summary>그 설비를 다루는 기술 가운데 익힌 것 (없으면 null).</summary>
    public static EraTech? TechFor(World w, FurnitureType t)
    {
        foreach (var (tech, type, _) in Lifts) if (type == t && w.Eras.Known.Contains(tech)) return TechWeb.Find(tech);
        return null;
    }
}
