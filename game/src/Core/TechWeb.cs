using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.14 기술 트리를 그물처럼.
// 시대 기술 70(EraSystem.All)은 그대로 두고, 그 위에 그물을 친다 — 표는 여기, 실험은 TechResearch.cs.
// ① 선행: 기술마다 다른 분야의 선행 기술 (핵융합로 = 초전도 자석(동력) + 플라스마 진단(센서) + 방사선 차폐(선체)).
// ② 갈림길 7쌍: 같은 문제의 두 해법 — 회의가 고르고(가치관 · 솜씨 · 겪은 일 · 컴퓨터 조언), 고른 쪽이 배의 이름이 되고, 다른 쪽은 잠긴다(다시 꺼내면 값이 두 배).
// ③ 열리는 조건: 겪은 사고 · 방 · 솜씨 좋은 사람 · 희귀 소재 · 원정 유물(역설계) · 다른 배와의 교류.
// ⑤ 조합 숨은 기술 10: A와 B를 익히면 C가 보인다.
// ⑥ 부작용 연쇄: 새 기술에 서툰 이틀 · 늘어난 설비가 낳는 새 위험 · 새 관행(Culture).
// ⑦ 새 시스템마다 기술 줄기: 배 본체 · 음식 · 이동식 장비 · 선외 · 원정 · 폭발 · 대재난 대비 · 컴퓨터 · 방 공사 — 효과는 그 시스템이 읽는 배율(TechWeb.Mul).
// ⑧ 노드마다 아이콘 조리법(Icon)과 배 모습 열쇠(Visual — v16.5b가 읽는다).
// 새 기술 41은 EraSystem.All(70) 밖에 둔다 (시대 기술 시험이 70을 센다) — 회의 · 연구 흐름은 TechWeb.Every(111)를 본다.

public enum GateKind : byte { None, Incident, Room, Skill, Material, Relic, Contact }

/// <summary>열리는 조건 하나: 종류 · 열쇠(사고 키 · 방 · 솜씨 · 유물 소품 id …) · 문턱 · 화면 문구.</summary>
public sealed record TechGate(GateKind Kind, string Key, float Need, string Text);

/// <summary>그물의 마디: 선행 · 아이콘 조리법 · 배 모습 열쇠 · 조건 · 숨김 · 조합 · 실험으로만(역설계).</summary>
public sealed record WebNode(string Id, string[] Pre, string Icon, string Visual, TechGate? Gate = null, bool Hidden = false, string[]? Combo = null, bool Trial = false);

/// <summary>갈림길: 문제 · 두 해법 · 고른 배의 이름 · 고른 쪽의 약점(이 사고가 거듭되면 다른 쪽을 다시 꺼낸다).</summary>
public sealed record TechFork(string Id, string Problem, string A, string B, string TitleA, string TitleB, string[] WeakA, string[] WeakB);

/// <summary>부작용 연쇄: 익히고 몇 시간 뒤 — 사고 키 배율(며칠, 0이면 계속) · 문구 · 생기는 관행.</summary>
public sealed record TechChainSpec(string Tech, float Hours, string Key, float Mul, float Days, string Text, CustomKind? Custom = null);

public static class TechWeb
{
    public sealed record Row(EraTech Tech, (string key, float mul)[] Fx);

    /// <summary>새 시스템이 읽는 배율 (TechWeb.Mul). 사고 키는 EraSystem.RiskMul이 따로 읽는다.</summary>
    public static readonly string[] Keys =
    {
        "body.burn", "body.slip", "cook.hours", "food.rot", "portable.drain", "eva.suit", "exp.relic", "exp.risk",
        "blast.power", "cosmic.conf", "cosmic.expose", "room.hours", "room.risk", "lab.accident", "lab.speed",
    };

    public static string KeyName(string s) => s switch
    {
        "body.burn" => "바닥 불 번짐", "body.slip" => "미끄러짐", "cook.hours" => "조리 시간", "food.rot" => "음식 상함", "portable.drain" => "이동식 장비 전력",
        "eva.suit" => "우주복 손상", "exp.relic" => "원정 유물 발견", "exp.risk" => "원정 위험", "blast.power" => "폭발 세기", "cosmic.conf" => "대재난 예보 확신",
        "cosmic.expose" => "대재난 노출", "room.hours" => "방 공사 시간", "room.risk" => "방 공사 사고", "lab.accident" => "실험 사고", "lab.speed" => "실험 속도",
        "meteor" => "작은 운석", "bigmeteor" => "큰 운석", "fire" => "불", "break" => "고장", "pipe" => "배관 사고",
        _ => Hazards.All.FirstOrDefault(h => h.Kind.ToString() == s)?.Name ?? s,
    };

    private static string Describe((string key, float mul)[] fx) =>
        string.Join(" · ", fx.Select(f => $"{KeyName(f.key)} {(f.mul >= 1f ? "+" : "−")}{MathF.Round(MathF.Abs(f.mul - 1f) * 100f):0}%"));

    private static (string, float)[] X(params (string, float)[] fx) => fx;
    private static string H(HazardKind k) => k.ToString();

    private static Row T(string id, int era, TechField field, string name, float cost, string what, (string, float)[] fx, string risk = "없음", string? key = null, float mul = 1f)
        => new(new EraTech(id, era, field, name, cost, fx.Length > 0 ? $"{what} ({Describe(fx)})" : what, risk, key, mul), fx);

    // ─────────────────────────────── 새 기술 41 ───────────────────────────────
    public static readonly Row[] Rows =
    {
        // ① 핵융합로로 가는 세 분야
        T("scmagnet", 2, TechField.Power, "초전도 자석", 46f, "코일을 극저온으로 감는다", X((H(HazardKind.TrunkSag), 0.85f)), "냉매가 샌다 — 냉각 상실", H(HazardKind.CoolantLoss), 1.1f),
        T("plasmadiag", 2, TechField.Sensors, "플라스마 진단", 44f, "뜨거운 기체 속을 빛으로 잰다", X((H(HazardKind.ReactorTransient), 0.8f))),
        T("radshield", 2, TechField.Hull, "방사선 차폐", 44f, "선체에 붕소 · 납 층을 댄다", X((H(HazardKind.RadiationBurst), 0.75f), (H(HazardKind.SolarStorm), 0.85f))),
        T("fusioncore", 3, TechField.Power, "핵융합로", 84f, "원자로 III · IV 단계(핵융합) 설계가 열린다", X((H(HazardKind.ReactorTransient), 0.9f)), "플라스마가 흔들린다 — 원자로 과도", H(HazardKind.ReactorTransient), 1.2f),
        // ② 갈림길의 새 쪽
        T("chemox", 2, TechField.Life, "화학 산소", 40f, "산소 양초를 태워 산소를 낸다", X((H(HazardKind.Co2Spike), 0.7f), (H(HazardKind.ScrubberSaturation), 0.75f)), "양초가 뜨겁다 — 불", "fire", 1.12f),
        T("algaeox", 2, TechField.Life, "조류 광합성", 44f, "조류 수조가 산소를 낸다", X((H(HazardKind.Co2Spike), 0.6f)), "조류가 썩는다 — 물 오염", H(HazardKind.WaterContamination), 1.15f),
        T("heavyarmor", 3, TechField.Hull, "무거운 장갑", 70f, "선체를 두껍게 덧댄다", X(("meteor", 0.7f), (H(HazardKind.HullCrack), 0.8f)), "무거운 판이 골조를 누른다 — 골조 삐걱임", H(HazardKind.FrameCreak), 1.25f),
        T("soilbed", 2, TechField.Food, "흙 재배", 40f, "흙과 퇴비로 작물을 키운다", X((H(HazardKind.NutrientCrash), 0.5f), (H(HazardKind.CropBlight), 0.85f)), "축축한 흙 — 곰팡이", H(HazardKind.MoldOutbreak), 1.2f),
        T("centralcpu", 2, TechField.Computing, "중앙 컴퓨터", 42f, "한 컴퓨터가 배 전체를 본다 (앞날 예측 모듈)", X((H(HazardKind.ComputerMisjudge), 0.8f)), "한 곳이 멎으면 다 멎는다 — 컴퓨터 오류", H(HazardKind.ComputerFault), 1.2f),
        T("distctrl", 2, TechField.Computing, "분산 제어", 42f, "설비마다 작은 제어기가 서로 묻는다 (전력 나눔 모듈)", X((H(HazardKind.ComputerFault), 0.7f)), "제어기끼리 엇갈린다 — 컴퓨터 오판단", H(HazardKind.ComputerMisjudge), 1.15f),
        // ③ 조건으로 열리는 숨은 기술 (원정 유물 역설계 · 다른 배와의 교류)
        T("wreckalloy", 1, TechField.Hull, "난파선 합금 역설계", 36f, "명판의 합금을 깎아 본다", X((H(HazardKind.HullCrack), 0.75f), (H(HazardKind.FrameCreak), 0.8f))),
        T("beaconcore", 1, TechField.Sensors, "신호기 코어 역설계", 36f, "옛 신호기의 수신 회로를 뜯어 본다", X(("cosmic.conf", 1.1f), (H(HazardKind.DebrisAlert), 0.8f))),
        T("oldstation", 1, TechField.Habitat, "옛 정거장 생활 공법", 32f, "엽서 속 정거장 사람들의 살림법", X((H(HazardKind.PanicAttack), 0.8f), ("room.hours", 0.9f))),
        T("convoylink", 2, TechField.Computing, "선단 통신 규약", 40f, "다른 배와 주고받은 규약", X(("exp.risk", 0.9f), (H(HazardKind.ComputerMisjudge), 0.9f))),
        // ⑤ 조합 숨은 기술
        T("labsafety", 1, TechField.Fabrication, "실험실 안전 절차", 26f, "점검표와 노트를 실험에 붙인다", X(("lab.accident", 0.6f))),
        T("biolamp", 3, TechField.Habitat, "생물 발광 조명", 62f, "조류가 어둠 속에서 빛난다", X((H(HazardKind.LightsOut), 0.6f))),
        T("digitaltwin", 4, TechField.Computing, "디지털 쌍둥이", 98f, "배를 통째로 컴퓨터 안에 띄운다", X((H(HazardKind.ComputerMisjudge), 0.7f), ("lab.speed", 1.15f))),
        T("printpatch", 3, TechField.Hull, "현장 인쇄 패치", 64f, "갈라진 자리에 바로 찍어 붙인다", X((H(HazardKind.HullCrack), 0.85f), (H(HazardKind.SealLeak), 0.7f))),
        T("heatwater", 3, TechField.Life, "폐열 증류", 62f, "설비 열로 물을 끓여 거른다", X((H(HazardKind.TankSludge), 0.7f), (H(HazardKind.PipeFreeze), 0.7f))),
        T("swarmrepair", 4, TechField.Robotics, "군집 수리", 100f, "드론과 기어 로봇이 함께 고친다", X((H(HazardKind.RobotMalfunction), 0.8f), ("pipe", 0.85f))),
        T("diagai", 4, TechField.Medical, "진단 AI", 96f, "원격 처방에 신경망을 붙인다", X((H(HazardKind.MedError), 0.5f), (H(HazardKind.Epidemic), 0.85f))),
        T("heirloom", 3, TechField.Food, "고향 씨앗 복원", 60f, "종자 은행의 옛 씨앗을 되살린다", X((H(HazardKind.SeedRot), 0.6f), (H(HazardKind.CropBlight), 0.85f))),
        T("safegrid", 4, TechField.Power, "무아크 배전", 100f, "차단기와 고체 전지가 아크를 지운다", X((H(HazardKind.ArcFault), 0.6f), (H(HazardKind.StaticDischarge), 0.7f))),
        T("plasmatorch", 3, TechField.Fabrication, "플라스마 절단기", 66f, "두꺼운 판을 빨리 자른다", X(("room.hours", 0.85f), (H(HazardKind.WeldFatigue), 0.8f)), "절단 불똥 — 불", "fire", 1.08f),
        // ⑦ 새 시스템마다 기술 줄기
        //   배 본체 (재질)
        T("fireretard", 2, TechField.Habitat, "난연 바닥 코팅", 38f, "바닥재에 난연제를 먹인다", X(("body.burn", 0.7f))),
        T("gripfloor", 2, TechField.Habitat, "미끄럼 방지 바닥", 36f, "젖어도 덜 미끄러운 바닥", X(("body.slip", 0.6f))),
        //   음식
        T("pressurecook", 2, TechField.Food, "압력 조리", 36f, "뚜껑을 잠가 빨리 익힌다", X(("cook.hours", 0.8f))),
        T("vacpack", 2, TechField.Food, "진공 포장", 38f, "남은 음식을 진공으로 싼다", X(("food.rot", 0.6f)), "진공 속 균 — 식중독", H(HazardKind.FoodPoisoning), 1.08f),
        //   이동식 장비
        T("lowdrawled", 2, TechField.Power, "저전력 작업등", 34f, "같은 빛을 적은 전기로", X(("portable.drain", 0.7f))),
        T("supercap", 4, TechField.Power, "초고용량 휴대 셀", 96f, "이동식 장비가 오래 간다", X(("portable.drain", 0.8f)), "작은 셀에 큰 전하 — 정전기 방전", H(HazardKind.StaticDischarge), 1.1f),
        //   선외
        T("aramidsuit", 2, TechField.Defense, "아라미드 우주복 겹", 40f, "질긴 섬유를 한 겹 더", X(("eva.suit", 0.75f))),
        T("selfpatchsuit", 3, TechField.Hull, "자가 봉합 우주복", 66f, "찢긴 우주복이 저절로 오므린다", X(("eva.suit", 0.8f))),
        //   원정
        T("salvagescan", 2, TechField.Sensors, "잔해 탐지기", 40f, "잔해 속 옛 물건을 찾는다", X(("exp.relic", 1.8f))),
        T("expmap", 2, TechField.Propulsion, "원정 지도 공유", 38f, "다녀온 길을 지도에 남긴다", X(("exp.risk", 0.8f))),
        //   폭발
        T("blastvent", 2, TechField.Hull, "폭압 배출구", 40f, "터지면 압력이 바깥으로 빠진다", X(("blast.power", 0.8f))),
        T("blastfoam", 3, TechField.Defense, "폭발 억제 거품", 66f, "불꽃이 번지기 전에 거품이 덮는다", X(("blast.power", 0.85f))),
        //   대재난 대비
        T("gravwave", 4, TechField.Sensors, "중력파 조기 경보", 100f, "멀리서 오는 큰일을 일찍 본다", X(("cosmic.conf", 1.15f))),
        T("stormcellar", 3, TechField.Habitat, "폭풍 대피 차폐", 64f, "안쪽 방에 두꺼운 차폐를 두른다", X(("cosmic.expose", 0.75f))),
        //   컴퓨터 (중앙 · 분산은 갈림길에)
        T("watchdog", 2, TechField.Computing, "감시 타이머", 36f, "멎은 프로그램을 스스로 다시 켠다", X((H(HazardKind.ComputerFault), 0.7f))),
        //   방 공사
        T("modularfit", 2, TechField.Fabrication, "모듈식 고정구", 36f, "설비를 볼트 넷으로 뗐다 붙인다", X(("room.hours", 0.75f))),
        T("quickcouple", 3, TechField.Fabrication, "빠른 이음 배관", 62f, "배관을 돌려 끼워 잇는다", X(("room.risk", 0.7f))),
    };

    public static readonly EraTech[] Extra = Rows.Select(r => r.Tech).ToArray();
    /// <summary>시대 기술 70 + 새 기술 41 (회의 · 연구 흐름 · 화면이 본다).</summary>
    public static readonly EraTech[] Every = EraSystem.All.Concat(Extra).ToArray();
    private static readonly Dictionary<string, EraTech> ById = Every.ToDictionary(t => t.Id);
    private static readonly Dictionary<string, Row> RowById = Rows.ToDictionary(r => r.Tech.Id);
    public static EraTech? Find(string? id) => id != null && ById.TryGetValue(id, out var t) ? t : null;
    public static Row? RowOf(string id) => RowById.TryGetValue(id, out var r) ? r : null;
    public static bool IsExtra(string id) => RowById.ContainsKey(id);

    // ─────────────────────────────── ①③⑤⑧ 마디 ───────────────────────────────
    private static TechGate G(GateKind k, string key, float need, string text) => new(k, key, need, text);
    private static WebNode N(string id, string pre, string icon, string visual = "", TechGate? gate = null, bool hidden = false, string? combo = null, bool trial = false)
        => new(id, pre.Length == 0 ? Array.Empty<string>() : pre.Split(' '), icon, visual, gate, hidden, combo?.Split(' '), trial);

    private static readonly WebNode[] NodeList =
    {
        // ── 1 근지구 ──
        N("fireproof", "", "flame+slash", "wall.fireproof"),
        N("triage", "", "cross+arrow", "medbay.triage"),
        N("checklist", "", "check+book", "console.checklist"),
        N("coolantdope", "", "drop+dot", "pipe.coolantdope"),
        N("seedbank", "", "seed+cube", "storage.seedvault"),
        N("amine", "", "bubble+dot", "lifesupport.amine"),
        N("weldcode", "", "plate+bolt", "hull.weldseam"),
        N("hygiene", "", "hand+drop", "galley.sink"),
        N("labnotes", "", "book+plus", "workshop.notes"),
        N("toolboard", "", "wrench+hex", "workshop.toolboard"),
        N("rcd", "", "bolt+slash", "panel.rcd"),
        N("vibelisten", "", "wave+eye", "machine.listener"),
        // ── 2 태양계 ──
        N("genecrops", "seedbank labnotes", "leaf+atom", "growbed.genecrops"),
        N("magshield", "rcd scmagnet", "magnet+shield", "hull.coilring"),
        N("smartgrid", "rcd checklist", "net+bolt", "panel.smartgrid"),
        N("dronenet", "toolboard vibelisten", "wing+net", "exterior.drones"),
        N("cobotarm", "toolboard", "hand+gear", "workshop.cobot"),
        N("rcsthruster", "weldcode", "arrow+dot", "exterior.rcs"),
        N("habflow", "fireproof", "house+arrow", "corridor.flowlines"),
        N("waterwall", "weldcode", "drop+shield", "hull.waterwall"),
        N("nftloop", "seedbank", "leaf+pipe", "growbed.nft"),
        N("vcd", "amine coolantdope", "drop+arrow", "waterplant.vcd"),
        N("heatpipe", "coolantdope", "pipe+flame", "pipe.heatpipe"),
        N("telemed", "triage labnotes", "cross+wave", "medbay.screen"),
        N("additive", "toolboard labnotes", "cube+plus", "workshop.printer"),
        // ── 3 핵융합 ──
        N("fusiondrive", "fusioncore rcsthruster", "atom+arrow", "engine.fusion"),
        N("selfseal", "weldcode additive", "plate+heart", "hull.selfseal"),
        N("biofilter", "vcd hygiene", "bug+drop", "lifesupport.biofilter"),
        N("quantumsense", "vibelisten smartgrid", "eye+atom", "sensor.quantum"),
        N("supertrunk", "scmagnet smartgrid", "bolt+snow", "trunk.superconduct"),
        N("predictive", "checklist vibelisten", "gear+eye", "console.predict"),
        N("fibernet", "vibelisten smartgrid", "net+star", "wall.fiber"),
        N("crawler", "cobotarm vcd", "pipe+gear", "pipe.crawler"),
        N("compositerib", "weldcode additive", "hex+plate", "hull.ribs"),
        N("hvaczone", "habflow heatpipe", "wave+house", "duct.zones"),
        N("pdlaser", "smartgrid vibelisten", "laser+dot", "exterior.pdlaser"),
        N("magnozzle", "rcsthruster scmagnet", "magnet+arrow", "engine.magnozzle"),
        N("regenmed", "telemed hygiene", "heart+plus", "medbay.vat"),
        // ── 4 성간 준비 ──
        N("gravity", "habflow compositerib", "ring+arrow", "hull.centrifuge"),
        N("nanorepair", "additive predictive", "gear+dot", "machine.nano"),
        N("cryomed", "regenmed heatpipe", "cross+snow", "medbay.cryo"),
        N("aeroponics", "nftloop vcd", "leaf+wave", "growbed.mist"),
        N("algaebio", "algaeox biofilter", "bubble+leaf", "lifesupport.algaetank"),
        N("liquidmetal", "heatpipe supertrunk", "drop+bolt", "pipe.liquidmetal"),
        N("neuralnet", "predictive labnotes", "chip+net", "server.neural"),
        N("exosuit", "cobotarm triage", "hand+bolt", "crew.exosuit"),
        N("multispec", "fibernet quantumsense", "lens+star", "sensor.multispec"),
        N("solidcell", "rcd supertrunk", "cell+hex", "battery.solid"),
        N("iondeflector", "scmagnet radshield", "shield+wave", "exterior.ionring"),
        N("podcabin", "habflow hvaczone", "house+moon", "quarters.pods"),
        // ── 5 탈지구 공학 ──
        N("aicaptain", "neuralnet centralcpu", "chip+star", "bridge.aicaptain"),
        N("closedloop", "biofilter aeroponics", "ring+leaf", "lifesupport.loop"),
        N("assembler", "additive neuralnet", "cube+atom", "workshop.assembler"),
        N("shapememory", "compositerib selfseal", "plate+arrow", "hull.memoryalloy"),
        N("nanomed", "regenmed additive", "heart+dot", "medbay.nano"),
        N("inertialdamp", "magnozzle gravity", "wave+slash", "hull.damper"),
        N("sensormesh", "multispec distctrl", "net+eye", "wall.sensormesh"),
        N("vatprotein", "genecrops biofilter", "flask+heart", "galley.vat"),
        N("radiator", "heatpipe compositerib", "wing+snow", "exterior.radiator"),
        N("photosynth", "algaebio solidcell", "sun+leaf", "lifesupport.photosynth"),
        // ── 6 초공간 ──
        N("warpbubble", "fusiondrive gravity", "bubble+arrow", "engine.warp"),
        N("forcefield", "iondeflector solidcell", "shield+star", "exterior.forcefield"),
        N("zeropoint", "solidcell supertrunk", "bolt+ring", "panel.zeropoint"),
        N("quantumcpu", "neuralnet quantumsense", "chip+atom", "server.quantum"),
        N("selfreplicate", "assembler crawler", "gear+plus", "robotbay.replicator"),
        N("phasehull", "shapememory inertialdamp", "plate+wave", "hull.phase"),
        N("gravlens", "gravity iondeflector", "lens+ring", "exterior.gravlens"),
        N("homeworld", "podcabin neuralnet", "house+star", "lounge.homeworld"),
        N("matterforge", "assembler fusioncore", "flame+atom", "workshop.forge"),
        N("hypernav", "quantumsense inertialdamp", "star+arrow", "bridge.hypernav"),
        // ── 새 기술 ──
        N("scmagnet", "rcd", "magnet+snow", "reactor.coils", G(GateKind.Material, nameof(ItemKind.Rare), 1, "희귀 소재가 창고에 있어야 코일을 감는다")),
        N("plasmadiag", "vibelisten", "eye+flame", "reactor.diag", G(GateKind.Skill, nameof(Skill.Electrical), 0.55f, "전기 솜씨 좋은 사람(0.55)이 있어야 잰다")),
        N("radshield", "weldcode", "shield+plate", "hull.radlayer"),
        N("fusioncore", "scmagnet plasmadiag radshield", "atom+ring", "reactor.tokamak"),
        N("chemox", "amine", "flame+bubble", "lifesupport.candles"),
        N("algaeox", "amine seedbank", "leaf+bubble", "lifesupport.algaetrough", G(GateKind.Room, nameof(RoomType.Hydroponics), 1, "수경재배실이 있어야 조류를 키운다")),
        N("heavyarmor", "weldcode radshield", "plate+plus", "hull.thickplate"),
        N("soilbed", "seedbank", "seed+leaf", "growbed.soil"),
        N("centralcpu", "checklist rcd", "chip+dot", "bridge.mainframe"),
        N("distctrl", "checklist vibelisten", "net+gear", "panel.nodes"),
        N("wreckalloy", "", "plate+key", "hull.wreckalloy", G(GateKind.Relic, "derelictplaque", 1, "원정에서 가져온 명판 · 기념패가 있어야 깎아 본다"), hidden: true, trial: true),
        N("beaconcore", "", "eye+key", "sensor.beacon", G(GateKind.Relic, "meteorite", 1, "원정에서 가져온 신호기 부속이 있어야 뜯어 본다"), hidden: true, trial: true),
        N("oldstation", "", "house+key", "quarters.oldstation", G(GateKind.Relic, "postcards", 1, "원정에서 가져온 엽서 묶음이 있어야 읽는다"), hidden: true, trial: true),
        N("convoylink", "checklist", "wave+key", "comms.convoy", G(GateKind.Contact, "배", 1, "다른 배와 교신해야 규약을 얻는다"), hidden: true),
        N("labsafety", "", "flask+check", "workshop.safetysign", hidden: true, combo: "checklist labnotes"),
        N("biolamp", "", "leaf+sun", "corridor.biolamp", hidden: true, combo: "biofilter algaeox"),
        N("digitaltwin", "", "chip+cube", "bridge.twinholo", hidden: true, combo: "fibernet predictive"),
        N("printpatch", "", "plate+cube", "hull.printpatch", hidden: true, combo: "selfseal additive"),
        N("heatwater", "", "drop+flame", "waterplant.heat", hidden: true, combo: "heatpipe vcd"),
        N("swarmrepair", "", "wing+gear", "exterior.swarm", hidden: true, combo: "dronenet crawler"),
        N("diagai", "", "cross+chip", "medbay.diagai", hidden: true, combo: "telemed neuralnet"),
        N("heirloom", "", "seed+heart", "growbed.heirloom", hidden: true, combo: "seedbank genecrops"),
        N("safegrid", "", "bolt+check", "panel.safegrid", hidden: true, combo: "rcd solidcell"),
        N("plasmatorch", "", "flame+laser", "workshop.torch", hidden: true, combo: "plasmadiag additive"),
        N("fireretard", "fireproof", "flame+plate", "floor.retardant"),
        N("gripfloor", "habflow", "hand+wave", "floor.grip", G(GateKind.Incident, "fall", 2, "두 번은 미끄러져 넘어져 봐야 바닥을 바꾼다")),
        N("pressurecook", "hygiene", "flask+flame", "galley.pressure"),
        N("vacpack", "hygiene seedbank", "cube+slash", "galley.vacuum"),
        N("lowdrawled", "rcd", "sun+dot", "portable.led"),
        N("supercap", "solidcell", "cell+bolt", "portable.supercap"),
        N("aramidsuit", "weldcode", "hand+shield", "suit.aramid"),
        N("selfpatchsuit", "selfseal aramidsuit", "heart+shield", "suit.selfpatch"),
        N("salvagescan", "vibelisten", "lens+key", "exterior.salvagescan"),
        N("expmap", "rcsthruster", "arrow+book", "bridge.expmap"),
        N("blastvent", "weldcode", "arrow+slash", "wall.blastvent", G(GateKind.Incident, "blast", 1, "폭발을 한 번은 겪어야 압력이 빠질 길을 낸다")),
        N("blastfoam", "fireproof blastvent", "bubble+shield", "wall.foamnozzle"),
        N("gravwave", "quantumsense", "wave+ring", "sensor.gravwave"),
        N("stormcellar", "radshield habflow", "house+shield", "shelter.cellar"),
        N("watchdog", "rcd labnotes", "eye+check", "server.watchdog"),
        N("modularfit", "toolboard", "hex+bolt", "floor.mounts"),
        N("quickcouple", "additive", "pipe+check", "pipe.couplers"),
    };

    public static readonly Dictionary<string, WebNode> Nodes = NodeList.ToDictionary(n => n.Id);
    private static readonly WebNode Plain = new("", Array.Empty<string>(), "dot", "");
    public static WebNode Node(string id) => Nodes.TryGetValue(id, out var n) ? n : Plain;
    public static string Visual(EraTech t) => Node(t.Id).Visual is { Length: > 0 } v ? v : $"{t.Field.ToString().ToLowerInvariant()}.{t.Id}";

    // ─────────────────────────────── ② 갈림길 ───────────────────────────────
    public static readonly TechFork[] Forks =
    {
        new("oxygen", "산소를 무엇으로 만들까", "chemox", "algaeox", "약품 산소의 배", "초록 숨의 배",
            new[] { "fire", H(HazardKind.OxygenLeak) }, new[] { H(HazardKind.WaterContamination), H(HazardKind.MoldOutbreak), H(HazardKind.Co2Spike) }),
        new("armor", "운석을 무엇으로 막을까", "heavyarmor", "pdlaser", "두꺼운 껍질의 배", "빛 방패의 배",
            new[] { H(HazardKind.FrameCreak), H(HazardKind.ThermalStress) }, new[] { H(HazardKind.BreakerCascade), "meteor" }),
        new("brain", "배를 무엇이 다스릴까", "centralcpu", "distctrl", "한 머리의 배", "여러 손의 배",
            new[] { H(HazardKind.ComputerFault) }, new[] { H(HazardKind.ComputerMisjudge), H(HazardKind.GroundFault) }),
        new("repair", "닳은 것을 누가 고칠까", "nanorepair", "exosuit", "스스로 아무는 배", "쇠 팔의 배",
            new[] { "break" }, new[] { H(HazardKind.WorkAccident) }),
        new("radiation", "방사선을 무엇으로 막을까", "waterwall", "magshield", "물벽의 배", "자기장의 배",
            new[] { H(HazardKind.CondensateFlood) }, new[] { H(HazardKind.PowerSurge) }),
        new("farming", "작물을 무엇에 키울까", "nftloop", "soilbed", "물뿌리의 배", "흙냄새 나는 배",
            new[] { H(HazardKind.NutrientCrash) }, new[] { H(HazardKind.MoldOutbreak), H(HazardKind.SkinFungus) }),
        new("heat", "열을 어디로 버릴까", "liquidmetal", "radiator", "은빛 핏줄의 배", "날개 단 배",
            new[] { "fire", H(HazardKind.CoolantLoss) }, new[] { H(HazardKind.MicroShower) }),
    };
    private static readonly Dictionary<string, (TechFork f, int side)> ForkOf =
        Forks.SelectMany(f => new[] { (f.A, (f, 0)), (f.B, (f, 1)) }).ToDictionary(x => x.Item1, x => x.Item2);
    public static (TechFork fork, int side)? ForkFor(string id) => ForkOf.TryGetValue(id, out var x) ? x : null;

    // ─────────────────────────────── ⑥ 부작용 연쇄 ───────────────────────────────
    public static readonly TechChainSpec[] Chains =
    {
        new("supertrunk", 20f, H(HazardKind.CoolantLoss), 1.2f, 3f, "초전도 간선 — 극저온 배관이 늘어 냉매가 새기 쉽다"),
        new("fibernet", 16f, H(HazardKind.CableTrayFire), 1.1f, 0f, "광섬유 감지망 — 케이블이 늘었다, 소화기 자리를 챙기자", CustomKind.FireCheck),
        new("hvaczone", 16f, H(HazardKind.DuctFire), 1.1f, 0f, "공기 조화 구역 — 덕트가 길어졌다, 소화기 자리를 챙기자", CustomKind.FireCheck),
        new("predictive", 24f, "break", 0.95f, 0f, "예지 정비 — 닳기 전에 가는 손이 관행이 된다", CustomKind.MaintainerWay),
        new("biofilter", 12f, H(HazardKind.SkinFungus), 1.1f, 2f, "생물 여과 — 균 배양조를 만진 손, 꼭 씻자", CustomKind.HandWash),
        new("liquidmetal", 24f, "fire", 1.1f, 0f, "액체 금속 냉각 — 배관 이음에 금속 찌꺼기가 쌓인다"),
        new("algaeox", 24f, H(HazardKind.MoldOutbreak), 1.1f, 2f, "조류 광합성 — 수조 곁이 늘 축축하다"),
        new("chemox", 10f, H(HazardKind.OxygenLeak), 1.15f, 2f, "화학 산소 — 산소 양초 보관함 곁에 산소가 짙다"),
        new("fusioncore", 18f, H(HazardKind.ReactorTransient), 1.2f, 2f, "핵융합로 설계 — 노심을 다시 맞추느라 제어가 들뜬다"),
        new("neuralnet", 20f, H(HazardKind.ComputerFault), 1.15f, 2f, "연구 신경망 — 학습 서버가 뜨겁다"),
        new("heavyarmor", 20f, H(HazardKind.LooseMount), 1.15f, 3f, "무거운 장갑 — 덧댄 판의 볼트가 늘었다"),
        new("pdlaser", 20f, H(HazardKind.BreakerCascade), 1.1f, 2f, "점 방어 레이저 — 충전할 때마다 차단기가 떤다"),
        new("distctrl", 18f, H(HazardKind.GroundFault), 1.1f, 2f, "분산 제어 — 제어기 배선이 사방으로 늘었다"),
        new("centralcpu", 18f, H(HazardKind.ComputerFault), 1.1f, 2f, "중앙 컴퓨터 — 모든 판단이 한 곳으로 몰린다"),
        new("soilbed", 20f, H(HazardKind.SkinFungus), 1.1f, 2f, "흙 재배 — 흙 묻은 손", CustomKind.HandWash),
        new("vacpack", 12f, H(HazardKind.FoodPoisoning), 1.05f, 2f, "진공 포장 — 봉지를 다시 여는 손이 늘었다", CustomKind.HandWash),
        new("plasmatorch", 12f, "fire", 1.05f, 2f, "플라스마 절단기 — 불똥이 바닥에 튄다", CustomKind.FireCheck),
    };

    /// <summary>사고 키 하나의 지금까지 횟수 (갈림길 약점 · 열리는 조건이 읽는다).</summary>
    public static int Incidents(World w, string key)
    {
        switch (key)
        {
            case "fire": return w.History.Fires;
            case "meteor": case "bigmeteor": return w.History.Meteors;
            case "breach": return w.History.Breaches;
            case "blast": return w.Blast.Stats.Detonations;
            case "fall": return w.Body.Stats.Falls;
            case "break": { int n = 0; foreach (var m in w.Ship.Machines) n += m.FaultCount; return n; }
            default: return Enum.TryParse<HazardKind>(key, out var k) && (int)k < w.Hazards.Count.Length ? w.Hazards.Count[(int)k] : 0;
        }
    }

    /// <summary>새 시스템이 읽는 배율 (익힌 새 기술의 곱 · 없으면 1). 세계가 아직 다 안 만들어졌으면 1.</summary>
    public static float Mul(World w, string key) => w.TechWeb is TechWebSystem t ? t.Mul(key) : 1f;

    /// <summary>설비 단계도 그물을 따른다: 원자로 III · IV는 핵융합로 설계가 있어야, 갈림길에서 버린 쪽 설비는 막힌다.</summary>
    public static bool TierOk(World w, FurnitureType type, int tier)
    {
        if (w.TechWeb is not TechWebSystem t) return true;
        return type switch
        {
            FurnitureType.ReactorCore when tier >= 3 => w.Eras.Has("fusioncore"),
            FurnitureType.OxygenGenerator when tier >= 3 => t.Side("oxygen") != 0, // 조류 광합성조 — 화학 산소를 고른 배는 막힌다
            FurnitureType.MainComputer when tier >= 2 => t.Side("brain") != 0, // 분산 제어 컴퓨터 — 중앙 컴퓨터를 고른 배는 막힌다
            FurnitureType.GrowBed when tier >= 3 => t.Side("farming") != 1, // 에어로포닉스 — 흙 재배를 고른 배는 막힌다
            _ => true,
        };
    }
}

/// <summary>갈림길 하나의 지금 (−1 아직 · 0 A · 1 B).</summary>
public sealed class ForkState
{
    public string Id { get; init; } = "";
    public int Side { get; set; } = -1;
    public long Decided { get; set; } = -1;
    public string Why { get; set; } = "";
    public string By { get; set; } = "";
    public int Yes { get; set; }
    public int No { get; set; }
    public long PendingSince { get; set; } = -1;
    public bool Reopened { get; set; }
    public int[] Base { get; set; } = Array.Empty<int>();
    public string Advice { get; set; } = "";
    public int AdviceSign { get; set; }
}

/// <summary>불붙은 부작용 하나 (때 · 키 · 배율 · 끝).</summary>
public sealed class ChainFx
{
    public string Tech { get; init; } = "";
    public string Key { get; init; } = "";
    public float Mul { get; init; } = 1f;
    public long From { get; init; }
    public long Until { get; init; } = -1;
    public string Text { get; init; } = "";
    public CustomKind? Custom { get; init; }
    public bool Fired { get; set; }
}

public sealed class TechWebStats
{
    public int Reveals, Combos, Gates, Relics, Contacts, ForkVotes, ForkJudged, ForkLearned, Reopens, Chains, Customs, Fresh,
        Advices, Followed, Ignored, ForkAdvices, Experiments, Successes, Failures, Accidents, Breakthroughs, Interrupts, Resumed, Collabs,
        Notes, NotesBurned, NotesRead, RelicStudies, Warnings, Heeded, Shaken, Avoided;
    public override string ToString() =>
        $"드러남 {Reveals}(조합 {Combos} · 조건 {Gates} · 유물 {Relics} · 교류 {Contacts}) · 갈림길 표결 {ForkVotes} · 선장 판단 {ForkJudged} · 익혀서 정함 {ForkLearned} · 다시 꺼냄 {Reopens} · "
        + $"부작용 {Chains}(관행 {Customs}) · 컴퓨터 추천 {Advices}(따름 {Followed} · 안 따름 {Ignored}) · 갈림길 조언 {ForkAdvices} · "
        + $"실험 {Experiments}(성공 {Successes} · 실패 {Failures} · 사고 {Accidents} · 돌파구 {Breakthroughs}) · 중단 {Interrupts} · 재개 {Resumed} · 협업 {Collabs} · "
        + $"노트 {Notes}(읽음 {NotesRead} · 탐 {NotesBurned}) · 유물 연구 {RelicStudies} · 실험 경고 {Warnings}(들음 {Heeded}) · 놀란 연구자 {Shaken} · 실험실 피함 {Avoided}";
}

/// <summary>
/// v16.14 기술 그물: 보임 · 선행 · 조건 · 갈림길 · 조합 · 부작용. 연구 행동(실험 · 노트 · 컴퓨터 추천)은 TechResearch.cs.
/// </summary>
public sealed partial class TechWebSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 1543));

    public TechWebStats Stats { get; } = new();
    /// <summary>숨은 기술 중 드러난 것 (드러난 차례).</summary>
    public List<string> Revealed { get; } = new();
    private readonly HashSet<string> _revealed = new();
    /// <summary>조건이 풀린 기술 (풀린 차례).</summary>
    public List<string> Opened { get; } = new();
    private readonly HashSet<string> _opened = new();
    /// <summary>드러난 · 열린 까닭.</summary>
    public Dictionary<string, string> Why { get; } = new();
    public Dictionary<string, ForkState> ForkStates { get; } = new();
    public List<ChainFx> ChainList { get; } = new();
    /// <summary>흐름으로 익힌 때 (서툰 이틀).</summary>
    public List<(string id, long tick)> Learned { get; } = new();
    /// <summary>배의 이름 (갈림길에서 고른 쪽).</summary>
    public List<string> Epithets { get; } = new();
    /// <summary>화면용 최근 일 (때 · 글 · 0 보통 · 1 좋음 · 2 나쁨).</summary>
    public List<(long tick, string text, int tone)> Recent { get; } = new();

    private long _nextScan;
    private int _knownSeen = -1;
    private readonly float[] _mul = new float[TechWeb.Keys.Length];
    private static readonly Dictionary<string, int> KeyIndex = TechWeb.Keys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);

    public TechWebSystem(World w)
    {
        _w = w;
        foreach (var f in TechWeb.Forks) ForkStates[f.Id] = new ForkState { Id = f.Id };
        for (int i = 0; i < _mul.Length; i++) _mul[i] = 1f;
    }

    private void Note(string text, int tone = 0)
    {
        Recent.Add((_w.Tick, text, tone));
        if (Recent.Count > 24) Recent.RemoveAt(0);
    }

    // ─────────────────────────────── 배율 ───────────────────────────────

    /// <summary>익힌 새 기술의 배율 (새 시스템 키). 익힌 수가 바뀔 때만 다시 곱한다.</summary>
    public float Mul(string key)
    {
        if (!KeyIndex.TryGetValue(key, out int i)) return 1f;
        var known = _w.Eras?.Known;
        if (known == null || known.Count == 0) return 1f;
        if (known.Count != _knownSeen)
        {
            _knownSeen = known.Count;
            for (int k = 0; k < _mul.Length; k++) _mul[k] = 1f;
            foreach (var r in TechWeb.Rows) // 표 순서대로 (결정론)
                if (known.Contains(r.Tech.Id))
                    foreach (var (s, m) in r.Fx)
                        if (KeyIndex.TryGetValue(s, out int j)) _mul[j] *= m;
        }
        return _mul[i];
    }

    /// <summary>사고 무게: 익힌 새 기술의 효과 · 위험 + 불붙은 부작용 + 갓 익힌 기술에 서툰 이틀 (EraSystem.RiskMul이 곱한다).</summary>
    public float RiskMul(string key)
    {
        var e = _w.Eras;
        float m = 1f;
        foreach (var r in TechWeb.Rows)
        {
            if (!e.Known.Contains(r.Tech.Id)) continue;
            foreach (var (s, x) in r.Fx) if (s == key) m *= x;
            if (r.Tech.RiskKey == key) m *= r.Tech.RiskMul;
        }
        foreach (var c in ChainList)
            if (c.Fired && c.Key == key && (c.Until < 0 || _w.Tick < c.Until)) m *= c.Mul;
        foreach (var (id, at) in Learned)
        {
            long age = _w.Tick - at;
            if (age >= SimTime.TicksPerDay * 2 || TechWeb.Find(id)?.RiskKey != key) continue;
            m *= 1f + 0.25f * (1f - age / (float)(SimTime.TicksPerDay * 2)); // 서툰 이틀: 처음엔 +25%, 이틀에 걸쳐 0으로
        }
        return m;
    }

    // ─────────────────────────────── 보임 · 선행 · 조건 · 잠김 ───────────────────────────────

    public bool Visible(EraTech t) => !TechWeb.Node(t.Id).Hidden || _revealed.Contains(t.Id);
    public bool Known(string id) => _w.Eras.Known.Contains(id);
    public bool PreMet(EraTech t) { foreach (var p in TechWeb.Node(t.Id).Pre) if (!Known(p)) return false; return true; }
    public bool GateMet(EraTech t) => TechWeb.Node(t.Id).Gate == null || _opened.Contains(t.Id);
    public bool IsRevealed(string id) => _revealed.Contains(id);

    /// <summary>갈림길에서 버린 쪽 (다시 꺼내기 전까지 잠긴다).</summary>
    public bool Locked(EraTech t) => TechWeb.ForkFor(t.Id) is var (f, side) && ForkStates[f.Id] is { Side: >= 0 } st && st.Side != side && !st.Reopened;
    /// <summary>갈림길에서 버린 쪽이다 (다시 꺼냈어도 값이 두 배).</summary>
    public bool Rival(EraTech t) => TechWeb.ForkFor(t.Id) is var (f, side) && ForkStates[f.Id] is { Side: >= 0 } st && st.Side != side;
    /// <summary>아직 정하지 않은 갈림길의 한쪽 (회의가 먼저 정한다).</summary>
    public bool Undecided(EraTech t) => TechWeb.ForkFor(t.Id) is var (f, _) && ForkStates[f.Id].Side < 0;
    public int Side(string fork) => ForkStates.TryGetValue(fork, out var s) ? s.Side : -1;

    /// <summary>고를 수 있나 (시대 규칙은 EraSystem이 본다): 보이고 · 선행을 익혔고 · 조건이 풀렸고 · 갈림길에서 버리지 않았다.</summary>
    public bool Open(EraTech t) => Visible(t) && PreMet(t) && GateMet(t) && !Locked(t);

    /// <summary>값: 갈림길에서 버린 쪽을 다시 꺼내면 두 배.</summary>
    public float CostOf(EraTech t) => t.Cost * (Rival(t) ? 2f : 1f);

    /// <summary>연구 흐름이 이 기술에 들어가는 몫: 역설계(실험으로만)는 노트만으로는 더디다.</summary>
    public float FlowMul(EraTech t) => TechWeb.Node(t.Id).Trial ? 0.25f : 1f;

    /// <summary>왜 아직 못 고르나 / 왜 열렸나 (화면 · 컴퓨터).</summary>
    public string Status(EraTech t)
    {
        if (Known(t.Id)) return "익힘";
        if (!Visible(t)) return "숨음";
        if (Locked(t)) return $"갈림길에서 버림 — 다시 꺼내면 값이 두 배";
        var miss = TechWeb.Node(t.Id).Pre.Where(p => !Known(p)).Select(p => TechWeb.Find(p)?.Name ?? p).ToList();
        if (miss.Count > 0) return "선행: " + string.Join(" · ", miss);
        if (!GateMet(t)) return "조건: " + TechWeb.Node(t.Id).Gate!.Text;
        if (t.Era > _w.Eras.Era) return $"{EraSystem.EraName(t.Era)}에 열린다";
        if (Undecided(t)) return "갈림길 — 회의가 정한다";
        return "고를 수 있다";
    }

    private void Reveal(string id, string why, int tone = 1)
    {
        if (!_revealed.Add(id)) return;
        Revealed.Add(id);
        Why[id] = why;
        Stats.Reveals++;
        var t = TechWeb.Find(id)!;
        Note($"숨은 기술이 보인다 — {t.Name} ({why})", tone);
        _w.History.Add(_w, HistoryKind.Milestone, $"숨은 기술이 보인다 — {t.Name}: {why}", log: true);
        _w.Log.Add(_w.Tick, LogKind.Ship, $"기술 지도에 새 마디 — {t.Name} ({why}) · {t.Effect}");
    }

    private void OpenGate(string id, string why)
    {
        if (!_opened.Add(id)) return;
        Opened.Add(id);
        Stats.Gates++;
        var t = TechWeb.Find(id)!;
        if (TechWeb.Node(id).Hidden) Reveal(id, why);
        else
        {
            Why[id] = why;
            Note($"조건이 풀렸다 — {t.Name} ({why})", 1);
            _w.History.Add(_w, HistoryKind.Milestone, $"{t.Name} 연구 조건이 풀렸다 — {why}", log: true);
        }
    }

    /// <summary>조건 · 조합을 다시 본다 (한 시간마다 · 기술을 익힐 때 · 시험).</summary>
    public void Scan()
    {
        var w = _w;
        foreach (var n in TechWeb.Nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            if (n.Combo != null && !_revealed.Contains(n.Id) && n.Combo.All(Known))
            {
                Stats.Combos++;
                Reveal(n.Id, $"{string.Join(" + ", n.Combo.Select(p => TechWeb.Find(p)!.Name))}을(를) 익혀 보였다");
            }
            if (n.Gate is not TechGate g || _opened.Contains(n.Id)) continue;
            string? why = g.Kind switch
            {
                GateKind.Incident => TechWeb.Incidents(w, g.Key) >= g.Need ? $"{IncidentName(g.Key)}을(를) {TechWeb.Incidents(w, g.Key)}번 겪었다" : null,
                GateKind.Room => Enum.TryParse<RoomType>(g.Key, out var rt) && w.Ship.RoomsOf(rt).FirstOrDefault(r => !r.Abandoned) is Room room ? $"{room.Name}이(가) 있다" : null,
                GateKind.Skill => Enum.TryParse<Skill>(g.Key, out var sk) && w.Crew.Where(c => !c.Dead && !c.IsChild && c.RawSkill(sk) >= g.Need).OrderByDescending(c => c.RawSkill(sk)).ThenBy(c => c.Id).FirstOrDefault() is CrewMember who
                    ? $"{who.Name}의 {Skills.Name(sk)} 솜씨({who.RawSkill(sk):0.00})" : null,
                GateKind.Material => w.Ship.CountStored(ItemKind.Rare) >= g.Need ? $"창고에 희귀 소재 {w.Ship.CountStored(ItemKind.Rare)}" : null,
                GateKind.Relic => RelicFor(g.Key) is PlacedProp p ? $"{p.Name} — {p.Origin}" : null,
                GateKind.Contact => ContactWhy(),
                _ => null,
            };
            if (why == null) continue;
            if (g.Kind == GateKind.Relic) Stats.Relics++;
            if (g.Kind == GateKind.Contact) Stats.Contacts++;
            OpenGate(n.Id, why);
        }
    }

    private static string IncidentName(string key) => key switch { "fall" => "미끄러져 넘어짐", "blast" => "폭발", "breach" => "선체 파공", _ => TechWeb.KeyName(key) };

    /// <summary>원정에서 가져와 배에 둔 그 소품 (역설계 거리).</summary>
    public PlacedProp? RelicFor(string propId)
    {
        foreach (var p in _w.Props.Placed)
            if (p.Spec.Id == propId && p.Origin.StartsWith("원정", StringComparison.Ordinal)) return p;
        return null;
    }

    private string? ContactWhy()
    {
        foreach (var s in OutsideSystem.Catalog)
            if ((s.Group == "배" || s.Group == "만남") && s.Comms && _w.Outside.Seen.Contains(s.Id)) return $"{s.Name}과(와) 교신했다";
        return null;
    }

    // ─────────────────────────────── 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        ResearchTick(dt); // TechResearch.cs: 실험 진행 · 중단 감지 · 노트
        if (w.Tick < _nextScan) return;
        _nextScan = w.Tick + SimTime.TicksPerHour;
        Scan();
        ForkTick();
        ChainTick();
        ResearchHour(); // TechResearch.cs: 실험 차례 · 연구자 · 컴퓨터 경고
    }

    /// <summary>EraSystem이 기술을 익혔다 (흐름으로).</summary>
    public void OnLearned(EraTech t)
    {
        var w = _w;
        Learned.Add((t.Id, w.Tick));
        if (Learned.Count > 40) Learned.RemoveAt(0);
        if (t.RiskKey != null) Stats.Fresh++;
        // 갈림길: 정하기 전에 한쪽을 익혔으면 (옛 기록 · 임무 보상) 그쪽으로 정해진다
        if (TechWeb.ForkFor(t.Id) is var (f, side) && ForkStates[f.Id].Side < 0) Decide(f, side, "먼저 익혔다", "", 0, 0, learned: true);
        foreach (var c in TechWeb.Chains)
        {
            if (c.Tech != t.Id) continue;
            ChainList.Add(new ChainFx
            {
                Tech = t.Id, Key = c.Key, Mul = c.Mul, From = w.Tick + SimTime.Hours(c.Hours),
                Until = c.Days > 0f ? w.Tick + SimTime.Hours(c.Hours) + (long)(c.Days * SimTime.TicksPerDay) : -1, Text = c.Text, Custom = c.Custom,
            });
        }
        if (t.Id == "centralcpu") w.Automation.Install(ComputerModule.Foresight, "중앙 컴퓨터");
        if (t.Id == "distctrl") w.Automation.Install(ComputerModule.PowerShare, "분산 제어");
        CreditResearchers(t); // TechResearch.cs
        Scan();
        Note($"익혔다 — {t.Name}", 1);
    }

    // ─────────────────────────────── ② 갈림길 ───────────────────────────────

    private void ForkTick()
    {
        var w = _w;
        foreach (var f in TechWeb.Forks)
        {
            var st = ForkStates[f.Id];
            if (st.Side < 0)
            {
                var a = TechWeb.Find(f.A)!;
                var b = TechWeb.Find(f.B)!;
                bool ready = Visible(a) && PreMet(a) && a.Era <= w.Eras.Era || Visible(b) && PreMet(b) && b.Era <= w.Eras.Era;
                if (!ready) continue;
                if (st.PendingSince < 0)
                {
                    st.PendingSince = w.Tick;
                    ForkAdvice(f, st);
                    Note($"갈림길 — {f.Problem}: {a.Name}냐 {b.Name}냐 (다음 회의 안건)");
                    w.Log.Add(w.Tick, LogKind.Ship, $"기술 갈림길 — {f.Problem}: {a.Name} · {b.Name} — 다음 정기 회의에서 정한다" + (st.Advice != "" ? $" · 주 컴퓨터: {st.Advice}" : ""));
                }
                else if (w.Tick - st.PendingSince > SimTime.Hours(30)) JudgeFork(f, st); // 회의가 열리지 않으면 선장이 정한다
                continue;
            }
            // 고른 쪽의 약점이 거듭 드러나면 다른 쪽을 다시 꺼낸다 (값은 두 배)
            if (st.Reopened || st.Base.Length == 0) continue;
            var weak = st.Side == 0 ? f.WeakA : f.WeakB;
            int hits = 0;
            for (int i = 0; i < weak.Length && i < st.Base.Length; i++) hits += TechWeb.Incidents(w, weak[i]) - st.Base[i];
            if (hits < 2) continue;
            st.Reopened = true;
            Stats.Reopens++;
            var chosen = TechWeb.Find(st.Side == 0 ? f.A : f.B)!;
            var rival = TechWeb.Find(st.Side == 0 ? f.B : f.A)!;
            string text = $"{chosen.Name}의 약점이 거듭 드러났다({string.Join(" · ", weak.Select(TechWeb.KeyName))} {hits}번) — 회의가 {Ko.EulReul(rival.Name)} 다시 꺼냈다 · 값은 두 배";
            Note(text, 2);
            w.History.Add(w, HistoryKind.Decision, text, log: true);
        }
    }

    /// <summary>주 컴퓨터: 두 해법의 득실을 이 배의 기록으로 따진다 (+ A · − B).</summary>
    public (float score, string text) ForkJudge(TechFork f)
    {
        var w = _w;
        float Side(string id)
        {
            var t = TechWeb.Find(id)!;
            float s = 0f;
            var fx = TechWeb.RowOf(id)?.Fx ?? ErasV15.Rows.FirstOrDefault(r => r.Tech.Id == id)?.Fx ?? Array.Empty<(string, float)>();
            foreach (var (k, m) in fx) s += (1f - m) * (1f + 0.5f * MathF.Min(4, TechWeb.Incidents(w, k)));
            if (t.RiskKey != null) s -= (t.RiskMul - 1f) * (1f + 0.6f * MathF.Min(4, TechWeb.Incidents(w, t.RiskKey)));
            s -= 0.002f * t.Cost;
            return s;
        }
        float a = Side(f.A), b = Side(f.B);
        var ta = TechWeb.Find(f.A)!;
        var tb = TechWeb.Find(f.B)!;
        // 이 배의 형편: 방 · 겪은 일
        if (f.Id == "oxygen" && w.Ship.RoomsOf(RoomType.Hydroponics).Any()) b += 0.1f;
        if (f.Id == "oxygen" && w.History.Fires >= 2) a -= 0.15f;
        if (f.Id == "armor" && w.History.Meteors >= 3) a += 0.1f;
        if (f.Id == "brain" && !w.Automation.MainOnline) b += 0.2f;
        if (f.Id == "heat" && w.History.Fires >= 2) a -= 0.1f;
        string Fx(EraTech t) => t.Effect.Contains('(') ? t.Effect[(t.Effect.IndexOf('(') + 1)..].TrimEnd(')') : t.Effect;
        string text = $"{ta.Name}: {Fx(ta)}" + (ta.Risk != "없음" ? $" · 위험 {ta.Risk}" : "") + $" / {tb.Name}: {Fx(tb)}" + (tb.Risk != "없음" ? $" · 위험 {tb.Risk}" : "")
                      + $" → 이 배라면 {(a >= b ? ta.Name : tb.Name)} (점수 {a:0.00} 대 {b:0.00})";
        return (a - b, text);
    }

    private void ForkAdvice(TechFork f, ForkState st)
    {
        var w = _w;
        var (score, text) = ForkJudge(f);
        if (!w.Automation.Present) return;
        var act = w.Automation.Book.Add(ActKind.Advice, null, $"기술 갈림길 — {f.Problem}", text, $"권고: {(score >= 0f ? TechWeb.Find(f.A)!.Name : TechWeb.Find(f.B)!.Name)}",
            "회의에서 정해 주세요", "techfork:" + f.Id, SimTime.TicksPerDay);
        if (act == null) return;
        st.Advice = text;
        st.AdviceSign = score >= 0f ? 1 : -1;
        Stats.ForkAdvices++;
    }

    /// <summary>이 사람이 갈림길에서 드는 마음 (+ A · − B) — 가치관 · 솜씨 · 내력 · 겪은 일 · 컴퓨터를 믿는 정도.</summary>
    public (float s, string why) ForkOpinion(CrewMember c, TechFork f)
    {
        var w = _w;
        var a = TechWeb.Find(f.A)!;
        var b = TechWeb.Find(f.B)!;
        float s = 0f;
        string why = "";
        void Add(float d, string text) { s += d; if (MathF.Abs(d) >= 0.12f && (why == "" || MathF.Abs(d) > 0.2f)) why = text; }
        bool fireScared = c.Memory.Fear.Any(x => x > 0.3f) && w.History.Fires >= 1;
        switch (f.Id)
        {
            case "oxygen":
                if (c.Role == CrewRole.Botanist || c.Background is Background.FarmResearcher or Background.Gardener) Add(-0.45f, "조류는 내가 키울 수 있다 — 살아 있는 산소");
                if (c.Background is Background.Chemist or Background.Firefighter) Add(c.Background == Background.Chemist ? 0.35f : -0.3f, c.Background == Background.Chemist ? "산소 양초는 반응식대로 나온다 — 확실하다" : "산소 양초는 불덩이다");
                if (fireScared) Add(-0.3f, "불을 겪었다 — 뜨거운 양초는 싫다");
                if (c.Value == CrewValue.Efficiency) Add(0.2f, "화학 산소가 싸고 바로 된다");
                if (c.Value == CrewValue.People) Add(-0.15f, "초록이 보이면 다들 숨이 트인다");
                if (c.Value == CrewValue.Rules) Add(0.15f, "검증된 쪽으로");
                break;
            case "armor":
                if (c.Value == CrewValue.Safety || c.Traits.Bravery < 0.35f) Add(0.3f, "전기가 나가도 장갑은 남는다");
                if (c.Role is CrewRole.Electrician or CrewRole.Pilot) Add(-0.25f, "다가오는 돌은 쏘아 맞히면 된다");
                if (c.Habits.Contains(Habit.Daredevil)) Add(-0.2f, "두꺼운 껍질에 숨는 건 답답하다");
                if (w.History.Meteors >= 2 && c.Memory.Fear.Any(x => x > 0.3f)) Add(0.25f, $"운석에 {w.History.Meteors}번 뚫렸다 — 두껍게 덮자");
                if (c.Value == CrewValue.Efficiency) Add(-0.15f, "무게가 늘면 추진제가 든다");
                break;
            case "brain":
            {
                float trust = w.Automation.Trusts.Of(c);
                Add(0.6f * (trust - 0.5f), trust >= 0.5f ? "컴퓨터 하나를 믿고 맡기자" : "컴퓨터 하나에 다 걸 수는 없다");
                if (c.Background is Background.Programmer or Background.SysAdmin) Add(0.25f, "한 곳에 모으면 고치기 쉽다");
                if (c.Value == CrewValue.Freedom) Add(-0.2f, "설비마다 제 머리가 있어야 한다");
                if (c.Value == CrewValue.Rules) Add(0.15f, "명령은 한 곳에서");
                if (w.Automation.Reboots > 0) Add(-0.25f, $"주 컴퓨터가 {w.Automation.Reboots}번 멎었다");
                break;
            }
            case "repair":
                if (c.Role is CrewRole.Technician or CrewRole.Engineer && c.RawSkill(Skill.Mechanics) >= 0.6f) Add(-0.35f, "고치는 건 손이다 — 외골격이면 혼자 든다");
                if (c.Habits.Contains(Habit.Tinkerer)) Add(-0.2f, "나노가 고치면 손 볼 게 없다");
                if (c.Value == CrewValue.Efficiency) Add(0.25f, "닳기 전에 스스로 아물면 일이 준다");
                if (c.Vitals.Injury > 0.2f || c.Fitness < 0.4f) Add(0.2f, "몸이 예전 같지 않다");
                break;
            case "radiation":
                if (c.Value == CrewValue.Safety) Add(0.25f, "물벽은 전기가 없어도 막는다");
                if (c.Role == CrewRole.Electrician) Add(-0.3f, "자기장 코일이 더 가볍다");
                if (c.Dose > 0.2f) Add(0.2f, "방사선을 쬐어 봤다 — 확실한 쪽으로");
                if (c.Value == CrewValue.Efficiency) Add(-0.15f, "물을 벽에 가둬 두면 아깝다");
                break;
            case "farming":
                if (c.Habits.Contains(Habit.Homesick) || c.Background is Background.Gardener or Background.FarmResearcher) Add(-0.4f, "흙냄새가 그립다");
                if (c.Role == CrewRole.Botanist && c.RawSkill(Skill.Botany) >= 0.7f) Add(0.2f, "양액이 계산대로 자란다");
                if (c.Value == CrewValue.Efficiency) Add(0.25f, "양액이 빠르다");
                if (c.Habits.Contains(Habit.NeatFreak)) Add(0.2f, "흙은 사방에 묻는다");
                break;
            case "heat":
                if (c.Role == CrewRole.Engineer) Add(0.3f, "액체 금속이 열을 확실히 뺀다");
                if (fireScared) Add(-0.3f, "금속이 새면 불이 붙는다");
                if (c.Quals.Contains(Qual.Eva) || c.Habits.Contains(Habit.Daredevil)) Add(-0.2f, "날개는 바깥에서 손보면 된다");
                if (c.Value == CrewValue.Safety) Add(-0.1f, "새면 불붙는 건 싫다");
                break;
        }
        var st = ForkStates[f.Id];
        if (st.AdviceSign != 0)
        {
            float trust = w.Automation.Trusts.Of(c);
            float d = 0.3f * st.AdviceSign * trust;
            s += d;
            if (MathF.Abs(s) < 0.15f && trust > 0.55f) why = $"컴퓨터가 {(st.AdviceSign > 0 ? a.Name : b.Name)} 쪽이 낫다고 한다";
        }
        s += 0.02f * ((c.Id * 7 + f.Id.Length) % 5 - 2); // 그날의 기분 (결정론)
        if (why == "") why = s > 0f ? $"{a.Name} 쪽이 무난하다" : $"{b.Name} 쪽이 끌린다";
        return (s, why);
    }

    private static float Expertise(CrewMember c, TechFork f) => f.Id switch
    {
        "oxygen" or "farming" => 0.2f + 0.6f * c.SkillLevel(Skill.Botany),
        "brain" or "armor" or "radiation" => 0.2f + 0.6f * c.SkillLevel(Skill.Electrical),
        _ => 0.2f + 0.6f * c.SkillLevel(Skill.Engineering),
    };

    /// <summary>정기 회의: 기다리는 갈림길 하나를 토론 · 표결한다.</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> attendees, CrewMember chair)
    {
        var w = _w;
        var voters = attendees.Where(c => !w.Society.OnProbation(c)).ToList();
        if (voters.Count < 2) voters = attendees;
        if (voters.Count < 2) return;
        foreach (var f in TechWeb.Forks)
        {
            var st = ForkStates[f.Id];
            if (st.Side >= 0 || st.PendingSince < 0) continue;
            var a = TechWeb.Find(f.A)!;
            var b = TechWeb.Find(f.B)!;
            var item = new AgendaItem
            {
                Title = $"기술 갈림길: {f.Problem} — {a.Name}(찬성) 냐 {b.Name}(반대) 냐", Topic = "techfork:" + f.Id,
                Evidence = $"{a.Name}: {a.Effect} / {b.Name}: {b.Effect}",
                Computer = st.Advice != "" ? $"주 컴퓨터: {st.Advice}" : null, ComputerSign = st.AdviceSign,
            };
            var (yes, no) = w.Meetings.Debate(voters, c => ForkOpinion(c, f), c => Expertise(c, f), item, chair);
            bool pickA = yes.Count > no.Count || yes.Count == no.Count && ForkOpinion(chair, f).s > 0f;
            item.Passed = pickA;
            item.Outcome = pickA ? $"{Ko.EulReul(a.Name)} 고른다" : $"{Ko.EulReul(b.Name)} 고른다";
            rec.Items.Add(item);
            w.Meetings.Record(item.Title, "techfork", -1, chair, yes, no, "");
            w.Meetings.Split(yes, no);
            Stats.ForkVotes++;
            bool followed = st.AdviceSign != 0 && (st.AdviceSign > 0) == pickA;
            Decide(f, pickA ? 0 : 1, $"회의 표결 {yes.Count}:{no.Count}" + (item.FlippedBy != null ? $" ({item.FlippedBy}의 설득)" : "") + (st.AdviceSign != 0 ? (followed ? " · 컴퓨터 권고대로" : " · 컴퓨터 권고와 달리") : ""),
                chair.Name, yes.Count, no.Count, voters: voters);
            // 진 쪽에서 크게 바란 사람은 서운하다 (일기)
            foreach (var c in pickA ? no : yes)
            {
                var (s, why) = ForkOpinion(c, f);
                if (MathF.Abs(s) > 0.35f) Life.Diary(w, c, Persona.Say(c, $"회의가 {(pickA ? a.Name : b.Name)}로 정했다. {why} — 그렇게 말했는데."));
            }
            return; // 한 번에 하나
        }
    }

    private void JudgeFork(TechFork f, ForkState st)
    {
        var w = _w;
        var judge = Council.Judge(w, false);
        if (judge == null) return;
        var (s, why) = ForkOpinion(judge, f);
        Stats.ForkJudged++;
        Decide(f, s > 0f ? 0 : 1, $"회의가 열리지 않아 {Ko.IGa(judge.Name)} 정했다 — {why}", judge.Name, 1, 0, voters: new List<CrewMember> { judge });
    }

    /// <summary>갈림길을 정한다: 버린 쪽은 잠기고 · 배의 이름이 붙는다.</summary>
    public void Decide(TechFork f, int side, string why, string by, int yes, int no, bool learned = false, List<CrewMember>? voters = null)
    {
        var w = _w;
        var st = ForkStates[f.Id];
        if (st.Side >= 0) return;
        st.Side = side;
        st.Decided = w.Tick;
        st.Why = why;
        st.By = by;
        st.Yes = yes;
        st.No = no;
        st.Base = (side == 0 ? f.WeakA : f.WeakB).Select(k => TechWeb.Incidents(w, k)).ToArray();
        if (learned) Stats.ForkLearned++;
        var chosen = TechWeb.Find(side == 0 ? f.A : f.B)!;
        var rival = TechWeb.Find(side == 0 ? f.B : f.A)!;
        string title = side == 0 ? f.TitleA : f.TitleB;
        Epithets.Add(title);
        string text = $"갈림길 — {f.Problem}: {Ko.EulReul(chosen.Name)} 골랐다 ({why}) · {rival.Name}은(는) 잠긴다 · 배의 이름 '{title}'";
        Note(text, 1);
        w.History.Add(w, HistoryKind.Decision, text, null, voters, log: true);
        w.History.Add(w, HistoryKind.Milestone, $"{w.Ship.Name}에 붙은 이름 — '{title}' ({f.Problem}에 {Ko.EulReul(chosen.Name)} 고른 배)", log: true);
        foreach (var c in w.Crew) if (!c.Dead && !c.IsChild) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.01f); // 배가 제 얼굴을 갖는다
        // 주 컴퓨터 채점: 권고와 같은 쪽이었나
        if (st.AdviceSign != 0 && w.Automation.Present)
        {
            bool same = (st.AdviceSign > 0) == (side == 0);
            foreach (var c in voters ?? new List<CrewMember>())
                w.Automation.Trusts.Change(c, same ? 0.01f : -0.005f, same ? $"갈림길에서 컴퓨터 권고와 같은 쪽을 골랐다 ({chosen.Name})" : $"갈림길에서 컴퓨터 권고와 다른 쪽을 골랐다 ({chosen.Name})", quiet: true);
        }
    }

    // ─────────────────────────────── ⑥ 부작용 연쇄 ───────────────────────────────

    private void ChainTick()
    {
        var w = _w;
        foreach (var c in ChainList)
        {
            if (c.Fired || w.Tick < c.From) continue;
            c.Fired = true;
            Stats.Chains++;
            var t = TechWeb.Find(c.Tech)!;
            string fx = $"{TechWeb.KeyName(c.Key)} {(c.Mul >= 1f ? "+" : "−")}{MathF.Abs(c.Mul - 1f) * 100f:0}%" + (c.Until > 0 ? $" ({(c.Until - c.From) / (float)SimTime.TicksPerDay:0.#}일)" : "");
            Note($"부작용 — {c.Text} ({fx})", c.Mul > 1f ? 2 : 1);
            w.History.Add(w, HistoryKind.Lesson, $"새 기술의 뒤끝 — {c.Text} ({fx})", log: true);
            if (c.Custom is CustomKind k)
            {
                var founder = w.Crew.Where(x => !x.Dead && !x.IsChild && x.CanAct).OrderByDescending(x => x.Traits.Diligence).ThenBy(x => x.Id).FirstOrDefault();
                bool fresh = w.Culture.Of(k) == null;
                w.Culture.Adopt(k, $"{t.Name}을(를) 들이고 — {c.Text}", founder?.Name);
                if (fresh) { Stats.Customs++; if (founder != null) Life.Diary(w, founder, Persona.Say(founder, $"{t.Name} 뒤로 할 일이 늘었다. {c.Text}.")); }
            }
            if (c.Mul > 1f && w.Automation.Present) // 주 컴퓨터가 읽는다: 새 위험을 예보하고 점검을 권한다
                w.Automation.Book.Add(ActKind.Forecast, null, $"{t.Name}을(를) 들인 뒤 {TechWeb.KeyName(c.Key)} 위험이 늘었다", $"예측: {fx}",
                    $"점검 순서에 {TechWeb.KeyName(c.Key)}을(를) 앞당긴다", "관련 설비 둘레를 한 번씩 봐 주세요", "techchain:" + c.Tech + c.Key, SimTime.TicksPerDay);
        }
    }

    // ─────────────────────────────── 화면 · 지문 ───────────────────────────────

    /// <summary>배의 이름 한 줄 (갈림길에서 고른 것들).</summary>
    public string Identity => Epithets.Count == 0 ? "아직 갈림길을 고르지 않았다" : string.Join(" · ", Epithets);

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Revealed.Count); foreach (var id in Revealed) I(Array.IndexOf(TechWeb.Every, TechWeb.Find(id))); // 문자열 해시는 실행마다 바뀐다 — 표의 자리로
        I(Opened.Count);
        foreach (var f in TechWeb.Forks) { var st = ForkStates[f.Id]; I(st.Side); I(st.Decided); I(st.Reopened ? 1 : 0); I(st.Yes); I(st.No); }
        I(ChainList.Count); I(ChainList.Count(c => c.Fired));
        I(Stats.Experiments); I(Stats.Successes); I(Stats.Failures); I(Stats.Accidents); I(Stats.Breakthroughs); I(Stats.Interrupts); I(Stats.Resumed); I(Stats.Notes); I(Stats.Advices);
        I(Notes.Count); foreach (var n in Notes) { I(n.At.X * 1000 + n.At.Y); F(n.Progress); }
        if (Trial is ExperimentState x) { I(x.Lead); I(x.Partner); F(x.Progress); I(x.Sessions); }
        I(R.Draws);
    }
}
