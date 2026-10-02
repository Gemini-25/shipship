using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.25 문제마다 여러 갈래 해법 — 표 (데이터).
// 문제 20종 × 갈래 5~12. 한 줄 = 한 갈래: 누가(사람 · 로봇 · 컴퓨터 · 설비) · 무엇으로(창고 물건 · 근처 물건 · 근처 설비) · 솜씨 · 허락 ·
//   걸리는 시간 · 기본 효과 · 몸 위험 · 배가 잃는 것 · 부작용 · 나중에 제대로 고칠 일 · 고른 사람이 하는 말 · 남는 그림.
// 기존 수순(소화기 일감 · 실링폼 · 보조 발전기 · 구조 · 제작 …)은 Order 가 붙은 한 갈래다 — 그 수순이 하고, 여기서는 지켜보고 기록한다.
// 성공 판정은 표의 숫자가 아니라 규칙(WaysRules: 재질 × 원소 · 물리)에서 나온다 — 표에 없는 물건도 재질이 맞으면 쓰인다.
// 새 갈래 = 표에 한 줄. 새 문제 = Snag 끝에 하나 + 이름 + 찾기(WaysSystem.Detect) 한 줄.

public enum Snag : byte { Fire, Smoke, Breach, Trapped, Blackout, Injured, NoPart, Oxygen, Food, Water, Overheat, ToxicGas, CommsDown, CargoLoose, Leak, Cold, Spark, Spill, Dark, Clog }

/// <summary>누가 하는 갈래인가.</summary>
public enum WayBy : byte { Crew, Robot, Computer, Ship }

/// <summary>갈래가 세계에 하는 일 (실행 규칙). Existing = 기존 수순이 한다 — 지켜보고 기록만.</summary>
public enum WayFx : byte
{
    Existing, Seal, Douse, CutDouse, Smother, Eject, LetBurn,
    Plug, Freeze, Brace,
    Crawl, Pry, Cut, RemoteOpen, Blow, Bypass, Signal,
    RobotBattery, Pedal, Lamp,
    Stretcher, Cart, TreatHere, Guided, Push,
    Strip, Improvise, Recycle, Lathe,
    Air, Gather, Ration, Strap, Wedge, TapeHose, Warmer, Mop, Generic,
    RobotHold, RobotCarry, Proper,
}

/// <summary>그 갈래가 남기는 그림 (화면이 이 값마다 다른 모양을 그린다).</summary>
public enum WayLook : byte
{
    None, Seal, Douse, HydroHose, Smother, Eject, Burnt,
    MattressPlug, TablePlug, PotPlug, CratePlug, MatPlug, FrostPlug, BackPlug, RobotBrace,
    CrawlHatch, PriedDoor, CutDoor, BlownDoor, WallHole, Jumper, Knock,
    RobotBattery, Pedal, Lamp, Stretcher, Cart, Bandage, Terminal,
    Stripped, Improvised, Shavings, Candle, Splitter, Huddle, RationBox, Porridge, ColdStash,
    SignalLamp, Runner, Radio, Strap, Wedge, TapeHose, PotWarmer, Towel, Bucket, FanDoor, WetCloth, Sawdust, Plunger, OffTag, Blanket, Condense, SuitShare,
}

/// <summary>단순 효과 (방 공기 · 물 · 전기 · 사람) — 표만으로 새 갈래를 더할 수 있게.</summary>
public readonly record struct WayGen(float O2 = 0f, float Smoke = 1f, float Toxin = 1f, float Temp = 0f, float Water = 0f, float Kwh = 0f,
    float Rest = 0f, float Stress = 0f, float Food = 0f, float Flood = 1f, float LightHours = 0f, float Injury = 0f, bool Close = false, bool Vent = false);

public sealed record Way(string Id, Snag Snag, string Name, WayBy By, WayFx Fx, float Minutes, float Power, float Risk, WayLook Look)
{
    public static readonly ArticleKind[] NoThings = Array.Empty<ArticleKind>();
    public static readonly FurnitureType[] NoNear = Array.Empty<FurnitureType>();
    /// <summary>창고에서 들고 갈 것.</summary>
    public ItemKind? Item { get; init; }
    public int ItemCount { get; init; } = 1;
    /// <summary>근처에 굴러다니는 물건 (재질로 판정 — 이 중 하나).</summary>
    public ArticleKind[] Things { get; init; } = NoThings;
    /// <summary>근처 설비 · 가구 (이 중 하나).</summary>
    public FurnitureType[] Near { get; init; } = NoNear;
    public Skill Skill { get; init; } = Skill.Mechanics;
    public float SkillMin { get; init; }
    /// <summary>허락이 필요하거나 규칙을 어기는 길 (함부로 안 고른다).</summary>
    public bool Leave { get; init; }
    /// <summary>기존 수순의 일감 (그 일감이 곧 이 갈래).</summary>
    public WorkKind? Order { get; init; }
    /// <summary>배가 잃는 것 0~1 (공기 · 설비 · 작물).</summary>
    public float ShipCost { get; init; }
    public WayGen Gen { get; init; }
    /// <summary>제대로 된 길 (규정) — 아니면 임시변통.</summary>
    public bool Book { get; init; }
    public string Side { get; init; } = "";
    public string Later { get; init; } = "";
    public string Line { get; init; } = "";
}

public static class WaysTable
{
    public static string Name(Snag s) => s switch
    {
        Snag.Fire => "불", Snag.Smoke => "연기", Snag.Breach => "파공", Snag.Trapped => "갇힘", Snag.Blackout => "정전", Snag.Injured => "부상자",
        Snag.NoPart => "부품 없음", Snag.Oxygen => "산소 부족", Snag.Food => "식량 부족", Snag.Water => "물 부족", Snag.Overheat => "과열", Snag.ToxicGas => "독가스",
        Snag.CommsDown => "통신 끊김", Snag.CargoLoose => "화물 이탈", Snag.Leak => "물 샘", Snag.Cold => "추위", Snag.Spark => "불꽃 튀는 배선", Snag.Spill => "쏟은 것",
        Snag.Dark => "조명 꺼짐", _ => "막힘",
    };

    /// <summary>문제마다 급한 정도 (시간이 얼마나 아픈가 0~1).</summary>
    public static float Urgency(Snag s) => s switch
    {
        Snag.Fire or Snag.Breach => 1f, Snag.Injured or Snag.ToxicGas => 0.9f, Snag.Smoke or Snag.Trapped or Snag.Oxygen => 0.7f, Snag.Spark or Snag.Overheat => 0.6f,
        Snag.Blackout or Snag.CargoLoose or Snag.Leak => 0.5f, Snag.NoPart or Snag.Cold => 0.35f, _ => 0.2f,
    };

    private static readonly ArticleKind[] Cloth = { ArticleKind.Rug, ArticleKind.Towel };
    private static readonly ArticleKind[] Wet = { ArticleKind.WaterJug };

    // 축약
    private static Way W(Snag s, string id, string name, WayBy by, WayFx fx, float min, float power, float risk, WayLook look) => new($"{Key(s)}.{id}", s, name, by, fx, min, power, risk, look);
    private static string Key(Snag s) => s.ToString().ToLowerInvariant();

    public static readonly Way[] All =
    {
        // ── 불 ──
        W(Snag.Fire, "ext", "소화기로 끈다", WayBy.Crew, WayFx.Existing, 8, 0.85f, 0.25f, WayLook.None) with { Order = WorkKind.Extinguish, Item = ItemKind.Extinguisher, Book = true, Line = "소화기부터", Side = "분말이 설비를 덮는다" },
        W(Snag.Fire, "system", "자동 소화 장치에 맡긴다", WayBy.Ship, WayFx.Existing, 5, 0.8f, 0f, WayLook.None) with { Book = true },
        W(Snag.Fire, "seal", "문을 닫아 숨을 끊는다", WayBy.Crew, WayFx.Seal, 4, 0.7f, 0.05f, WayLook.Seal) with { ShipCost = 0.25f, Side = "꺼질 때까지 방 안 것이 탄다 · 연기가 고인다", Later = "문을 열고 연기를 뺀다", Line = "문부터 닫자 — 숨을 못 쉬면 불도 죽는다" },
        W(Snag.Fire, "vacuum", "방을 비워 진공으로 끈다", WayBy.Computer, WayFx.Existing, 6, 0.95f, 0.1f, WayLook.None) with { ShipCost = 0.45f, Leave = true, Side = "안에 남은 사람 · 작물 · 공기를 잃는다", Later = "다시 가압한다" },
        W(Snag.Fire, "inert", "불활성 가스로 덮는다", WayBy.Computer, WayFx.Existing, 8, 0.8f, 0.05f, WayLook.None) with { ShipCost = 0.2f, Side = "가스가 준다" },
        W(Snag.Fire, "hydro", "수경 탱크 물을 퍼붓는다", WayBy.Crew, WayFx.Douse, 6, 0.6f, 0.35f, WayLook.HydroHose) with { Near = new[] { FurnitureType.GrowBed }, ShipCost = 0.15f, Side = "작물 양액이 빠진다 · 바닥이 젖는다 · 전기 불이면 감전", Later = "양액을 다시 채운다", Line = "수경 탱크 물이 바로 옆이다" },
        W(Snag.Fire, "jug", "물통 물을 붓는다", WayBy.Crew, WayFx.Douse, 3, 0.45f, 0.35f, WayLook.Bucket) with { Things = Wet, Side = "바닥이 젖는다 · 전기 불이면 감전", Line = "물통째 부어 버리자" },
        W(Snag.Fire, "cutwater", "전원을 내리고 물을 붓는다", WayBy.Crew, WayFx.CutDouse, 9, 0.65f, 0.08f, WayLook.Douse) with { Near = new[] { FurnitureType.GrowBed, FurnitureType.WaterRecycler }, Things = Wet, Skill = Skill.Electrical, SkillMin = 0.35f, Side = "그 방 전기가 나간다", Later = "차단기를 다시 올린다", Line = "전기 불이다 — 전원부터 내리고" },
        W(Snag.Fire, "blanket", "담요로 덮는다", WayBy.Crew, WayFx.Smother, 2, 0.55f, 0.4f, WayLook.Smother) with { Things = Cloth, Near = new[] { FurnitureType.FireBlanket, FurnitureType.Bed, FurnitureType.Cot }, Side = "덮은 천이 탄다 · 손을 데기 쉽다", Line = "작을 때 덮어 버리면 된다" },
        W(Snag.Fire, "eject", "불붙은 걸 들고 나간다", WayBy.Crew, WayFx.Eject, 2, 0.5f, 0.6f, WayLook.Eject) with { Side = "손 화상 · 가는 길에 불똥", Line = "저것만 치우면 된다" },
        W(Snag.Fire, "robot", "소방 로봇이 뿌린다", WayBy.Robot, WayFx.Existing, 6, 0.75f, 0f, WayLook.None) with { Book = true },
        W(Snag.Fire, "letburn", "타게 두고 가둔다", WayBy.Crew, WayFx.LetBurn, 3, 0.55f, 0.02f, WayLook.Burnt) with { ShipCost = 0.5f, Side = "방 안 설비가 다 탄다", Later = "탄 방을 치우고 다시 꾸민다", Line = "들어가면 다친다 — 가둬 두자" },

        // ── 연기 ──
        W(Snag.Smoke, "door", "문을 닫아 연기를 가둔다", WayBy.Crew, WayFx.Air, 2, 0.5f, 0.05f, WayLook.Towel) with { Gen = new(Close: true), Line = "옆방까지 번지지 않게", Side = "그 방은 더 탁해진다" },
        W(Snag.Smoke, "fan", "문을 열고 바람을 돌린다", WayBy.Crew, WayFx.Air, 6, 0.55f, 0.15f, WayLook.FanDoor) with { Gen = new(Smoke: 0.4f, Vent: true), Side = "옆방도 잠깐 매캐하다" },
        W(Snag.Smoke, "purge", "배기 밸브로 뽑는다", WayBy.Computer, WayFx.Air, 3, 0.8f, 0.05f, WayLook.None) with { Gen = new(Smoke: 0.15f, Toxin: 0.5f), ShipCost = 0.15f, Side = "공기를 조금 잃는다" },
        W(Snag.Smoke, "cloth", "젖은 천으로 입을 막는다", WayBy.Crew, WayFx.Air, 1, 0.35f, 0.2f, WayLook.WetCloth) with { Things = Cloth, Gen = new(Stress: -0.05f), Line = "젖은 수건이면 몇 분은 버틴다" },
        W(Snag.Smoke, "purifier", "공기 정화기를 최대로", WayBy.Crew, WayFx.Air, 3, 0.5f, 0.05f, WayLook.None) with { Near = new[] { FurnitureType.AirPurifier, FurnitureType.Scrubber }, Gen = new(Smoke: 0.5f, Toxin: 0.7f) },
        W(Snag.Smoke, "low", "엎드려 기어 나온다", WayBy.Crew, WayFx.Gather, 2, 0.4f, 0.25f, WayLook.None) with { Line = "연기는 위로 고인다 — 낮게" },

        // ── 파공 ──
        W(Snag.Breach, "sealant", "실링폼으로 막는다", WayBy.Crew, WayFx.Existing, 10, 0.85f, 0.3f, WayLook.None) with { Order = WorkKind.SealBreach, Item = ItemKind.Sealant, Book = true },
        W(Snag.Breach, "weld", "금속판을 덧대 용접한다", WayBy.Crew, WayFx.Existing, 25, 0.9f, 0.3f, WayLook.None) with { Order = WorkKind.SealBreach, Item = ItemKind.Plate, ItemCount = 2, Book = true },
        W(Snag.Breach, "mattress", "매트리스로 막는다", WayBy.Crew, WayFx.Plug, 5, 0.6f, 0.3f, WayLook.MattressPlug) with { Near = new[] { FurnitureType.Bed, FurnitureType.Cot, FurnitureType.MedBed }, Side = "오래 못 간다 — 몇 시간이면 다시 샌다 · 그 침대는 맨바닥", Later = "실링폼으로 다시 막고 매트리스를 돌려 놓는다", Line = "매트리스면 빨려 들어가 꽉 낀다" },
        W(Snag.Breach, "table", "탁자 상판을 대고 버틴다", WayBy.Crew, WayFx.Plug, 6, 0.55f, 0.3f, WayLook.TablePlug) with { Near = new[] { FurnitureType.Table, FurnitureType.GameTable }, Side = "가장자리로 샌다", Later = "실링폼으로 다시 막는다", Line = "상판이면 넓게 덮인다" },
        W(Snag.Breach, "pot", "냄비를 눌러 붙인다", WayBy.Crew, WayFx.Plug, 3, 0.5f, 0.3f, WayLook.PotPlug) with { Near = new[] { FurnitureType.Stove, FurnitureType.Oven }, Side = "작은 구멍에만 · 냄비를 잃는다", Later = "실링폼으로 다시 막는다", Line = "구멍이 작다 — 냄비면 된다" },
        W(Snag.Breach, "mat", "고무 매트를 붙인다", WayBy.Crew, WayFx.Plug, 3, 0.65f, 0.3f, WayLook.MatPlug) with { Things = new[] { ArticleKind.RubberMat }, Later = "실링폼으로 다시 막는다", Line = "고무는 들러붙는다" },
        W(Snag.Breach, "crate", "상자를 밀어 붙인다", WayBy.Crew, WayFx.Plug, 3, 0.4f, 0.3f, WayLook.CratePlug) with { Things = new[] { ArticleKind.PlasticCrate, ArticleKind.CardboardBox, ArticleKind.Rug }, Later = "실링폼으로 다시 막는다", Line = "아무거나 일단 대자" },
        W(Snag.Breach, "freeze", "냉매로 얼려 막는다", WayBy.Crew, WayFx.Freeze, 7, 0.6f, 0.35f, WayLook.FrostPlug) with { Near = new[] { FurnitureType.CoolantPump, FurnitureType.HeatExchanger, FurnitureType.Fridge }, Things = new[] { ArticleKind.IceBlock }, Skill = Skill.Engineering, SkillMin = 0.3f, Side = "녹으면 다시 샌다 · 냉매가 준다", Later = "실링폼으로 다시 막는다", Line = "냉매를 뿌리면 얼음 마개가 된다" },
        W(Snag.Breach, "back", "등으로 막고 버틴다", WayBy.Crew, WayFx.Brace, 1, 0.5f, 0.75f, WayLook.BackPlug) with { Side = "등에 멍 · 동상 · 오래 못 버틴다", Later = "누가 실링폼을 들고 와야 한다", Line = "내가 막고 있을게 — 빨리!" },
        W(Snag.Breach, "abandon", "문을 닫고 그 방을 버린다", WayBy.Computer, WayFx.Existing, 4, 0.9f, 0.05f, WayLook.None) with { Order = WorkKind.SealOffRoom, ShipCost = 0.6f, Leave = true, Side = "그 방을 잃는다 · 안에 남은 사람" },
        W(Snag.Breach, "drone", "드론이 밖에서 붙인다", WayBy.Robot, WayFx.Existing, 20, 0.8f, 0f, WayLook.None) with { Book = true },
        W(Snag.Breach, "robot", "로봇이 몸으로 막는다", WayBy.Robot, WayFx.RobotHold, 4, 0.5f, 0f, WayLook.RobotBrace) with { Side = "로봇이 묶인다 · 외장이 언다", Later = "실링폼으로 다시 막는다" },

        // ── 갇힘 ──
        W(Snag.Trapped, "crawl", "정비 통로로 기어 나온다", WayBy.Crew, WayFx.Crawl, 6, 0.75f, 0.15f, WayLook.CrawlHatch) with { Line = "벽 속 통로가 있다" },
        W(Snag.Trapped, "pry", "쇠지레로 문을 벌린다", WayBy.Crew, WayFx.Pry, 10, 0.7f, 0.2f, WayLook.PriedDoor) with { Things = new[] { ArticleKind.Toolbox }, Near = new[] { FurnitureType.Workbench, FurnitureType.ToolWall }, Side = "문틀이 휘어 다시 꽉 닫히지 않는다", Later = "문틀을 펴고 문을 다시 단다", Line = "틈만 있으면 쇠지레로 벌린다" },
        W(Snag.Trapped, "cut", "절단기로 문을 자른다", WayBy.Crew, WayFx.Cut, 18, 0.85f, 0.25f, WayLook.CutDoor) with { Near = new[] { FurnitureType.Workbench, FurnitureType.Lathe }, SkillMin = 0.45f, Side = "문짝이 없어진다 · 불똥", Later = "새 문짝을 단다", Line = "잘라 내는 게 확실하다" },
        W(Snag.Trapped, "remote", "컴퓨터가 잠금을 푼다", WayBy.Computer, WayFx.RemoteOpen, 1, 0.9f, 0f, WayLook.None),
        W(Snag.Trapped, "bypass", "문 모터에 선을 이어 연다", WayBy.Crew, WayFx.Bypass, 8, 0.7f, 0.15f, WayLook.Jumper) with { Item = ItemKind.Cable, Skill = Skill.Electrical, SkillMin = 0.35f, Later = "임시선을 걷어 낸다", Line = "모터만 살리면 열린다" },
        W(Snag.Trapped, "blow", "문을 폭파한다", WayBy.Crew, WayFx.Blow, 5, 0.9f, 0.8f, WayLook.BlownDoor) with { Leave = true, Side = "파편 · 안에 있는 사람이 다칠 수 있다", Later = "문틀째 다시 단다", Line = "시간이 없다 — 날려 버리자" },
        W(Snag.Trapped, "wall", "옆 벽을 뚫는다", WayBy.Crew, WayFx.Cut, 70, 0.8f, 0.2f, WayLook.WallHole) with { Near = new[] { FurnitureType.Workbench, FurnitureType.Lathe }, SkillMin = 0.3f, Side = "벽이 뚫린 채 남는다", Later = "벽을 다시 막는다", Line = "문이 안 되면 벽이다" },
        W(Snag.Trapped, "knock", "벽을 두드려 알리고 기다린다", WayBy.Crew, WayFx.Signal, 2, 0.3f, 0f, WayLook.Knock) with { Line = "누가 오겠지" },

        // ── 정전 ──
        W(Snag.Blackout, "aux", "보조 발전기를 돌린다", WayBy.Crew, WayFx.Existing, 15, 0.85f, 0.05f, WayLook.None) with { Order = WorkKind.StartAux, Book = true },
        W(Snag.Blackout, "shed", "급하지 않은 곳 전기를 끊는다", WayBy.Computer, WayFx.Existing, 2, 0.6f, 0f, WayLook.None) with { Order = WorkKind.ShedLoad, ShipCost = 0.1f },
        W(Snag.Blackout, "jumper", "임시 배선으로 이어 준다", WayBy.Crew, WayFx.Existing, 20, 0.7f, 0.2f, WayLook.None) with { Order = WorkKind.InstallJumper },
        W(Snag.Blackout, "robotbat", "로봇 배터리를 빼 쓴다", WayBy.Crew, WayFx.RobotBattery, 10, 0.45f, 0.05f, WayLook.RobotBattery) with { Near = new[] { FurnitureType.RobotDock }, Skill = Skill.Electrical, Side = "그 로봇이 못 움직인다", Later = "배터리를 다시 끼운다", Line = "로봇은 좀 쉬어도 된다" },
        W(Snag.Blackout, "pedal", "운동기구를 돌려 전기를 만든다", WayBy.Crew, WayFx.Pedal, 30, 0.3f, 0.05f, WayLook.Pedal) with { Near = new[] { FurnitureType.Treadmill }, Side = "기진맥진", Line = "다리라도 돌리자" },
        W(Snag.Blackout, "lamp", "손전등 · 작업등으로 버틴다", WayBy.Crew, WayFx.Lamp, 3, 0.3f, 0f, WayLook.Lamp) with { Gen = new(LightHours: 4f), Line = "불빛만 있으면 손은 움직인다" },
        W(Snag.Blackout, "reactor", "원자로 출력을 끝까지 올린다", WayBy.Computer, WayFx.Generic, 5, 0.5f, 0.1f, WayLook.None) with { Gen = new(Kwh: 4f), Leave = true, Side = "노심이 뜨거워진다" },

        // ── 부상자 ──
        W(Snag.Injured, "carry", "업어서 옮긴다", WayBy.Crew, WayFx.Existing, 6, 0.8f, 0.1f, WayLook.None) with { Order = WorkKind.Rescue, Book = true },
        W(Snag.Injured, "stretcher", "들것에 실어 둘이 든다", WayBy.Crew, WayFx.Stretcher, 8, 0.85f, 0.05f, WayLook.Stretcher) with { Line = "둘이 들면 흔들리지 않는다" },
        W(Snag.Injured, "cart", "짐수레에 눕혀 민다", WayBy.Crew, WayFx.Cart, 6, 0.75f, 0.05f, WayLook.Cart) with { Near = new[] { FurnitureType.MaintCart, FurnitureType.Hoist }, Line = "수레가 빠르다" },
        W(Snag.Injured, "robot", "로봇에 실어 보낸다", WayBy.Robot, WayFx.RobotCarry, 8, 0.7f, 0.05f, WayLook.None),
        W(Snag.Injured, "push", "띄워서 밀어 보낸다", WayBy.Crew, WayFx.Push, 3, 0.7f, 0.1f, WayLook.None) with { Line = "무게가 없으니 밀면 간다" },
        W(Snag.Injured, "here", "그 자리에서 치료한다", WayBy.Crew, WayFx.TreatHere, 12, 0.7f, 0.15f, WayLook.Bandage) with { Item = ItemKind.MedKit, Skill = Skill.Medicine, SkillMin = 0.25f, Later = "의무실로 옮겨 다시 본다", Line = "옮기다 더 다친다 — 여기서" },
        W(Snag.Injured, "guided", "단말이 알려 주는 대로 처치한다", WayBy.Crew, WayFx.Guided, 15, 0.6f, 0.1f, WayLook.Terminal) with { Item = ItemKind.MedKit, Near = new[] { FurnitureType.Console, FurnitureType.MainComputer }, Later = "의무실로 옮겨 다시 본다", Line = "컴퓨터, 다음은 뭐야?" },

        // ── 부품 없음 ──
        W(Snag.NoPart, "fab", "작업대에서 만든다", WayBy.Crew, WayFx.Existing, 90, 0.85f, 0f, WayLook.None) with { Order = WorkKind.Fabricate, Book = true },
        W(Snag.NoPart, "sub", "대체품을 단다", WayBy.Crew, WayFx.Existing, 60, 0.65f, 0f, WayLook.None) with { Order = WorkKind.InstallSubstitute },
        W(Snag.NoPart, "strip", "다른 설비에서 떼어 온다", WayBy.Crew, WayFx.Strip, 45, 0.85f, 0.05f, WayLook.Stripped) with { Order = WorkKind.Cannibalize, ShipCost = 0.3f, Side = "떼어 낸 설비가 멈춘다", Later = "새 부품이 생기면 돌려 단다", Line = "저건 당장 안 써도 된다" },
        W(Snag.NoPart, "improv", "생활 물건으로 임시로 만든다", WayBy.Crew, WayFx.Improvise, 40, 0.5f, 0.05f, WayLook.Improvised) with { Side = "금방 다시 고장 난다", Later = "제 부품으로 바꾼다", Line = "모양만 맞으면 돌아간다" },
        W(Snag.NoPart, "lathe", "정밀 가공기로 깎는다", WayBy.Crew, WayFx.Lathe, 70, 0.8f, 0.05f, WayLook.Shavings) with { Near = new[] { FurnitureType.Lathe, FurnitureType.Fabricator }, Item = ItemKind.Plate, Skill = Skill.Engineering, SkillMin = 0.4f, Line = "판 하나면 깎아 낸다" },
        W(Snag.NoPart, "recycle", "고철 더미에서 건진다", WayBy.Crew, WayFx.Recycle, 50, 0.4f, 0.05f, WayLook.None) with { Line = "버린 것 중에 쓸 만한 게 있을 거다" },
        W(Snag.NoPart, "trade", "원정 · 교역으로 구한다", WayBy.Computer, WayFx.Existing, 600, 0.6f, 0.1f, WayLook.None),

        // ── 산소 ──
        W(Snag.Oxygen, "gen", "산소 발생기를 고친다", WayBy.Crew, WayFx.Existing, 40, 0.85f, 0f, WayLook.None) with { Order = WorkKind.Repair, Book = true },
        W(Snag.Oxygen, "candle", "산소 양초를 태운다", WayBy.Crew, WayFx.Generic, 5, 0.6f, 0.2f, WayLook.Candle) with { Near = new[] { FurnitureType.SupplyCache, FurnitureType.SuitLocker }, Gen = new(O2: 4f, Temp: 1.5f), Side = "뜨겁다 · 불 곁에선 위험", Line = "양초 몇 개면 한숨 돌린다" },
        W(Snag.Oxygen, "split", "물을 갈라 산소를 만든다", WayBy.Crew, WayFx.Generic, 25, 0.55f, 0.15f, WayLook.Splitter) with { Gen = new(O2: 3f, Water: -15f, Kwh: -2f), Skill = Skill.Engineering, SkillMin = 0.35f, Side = "물과 전기를 먹는다 · 수소가 남는다", Line = "물이 있으면 산소가 있다" },
        W(Snag.Oxygen, "plants", "식물 곁으로 모인다", WayBy.Crew, WayFx.Gather, 5, 0.35f, 0f, WayLook.Huddle) with { Line = "잎이 숨을 만든다" },
        W(Snag.Oxygen, "rest", "다들 누워 숨을 아낀다", WayBy.Crew, WayFx.Generic, 10, 0.3f, 0f, WayLook.Huddle) with { Gen = new(O2: 0.8f, Rest: 0.1f, Stress: 0.05f), Line = "움직이지 마 — 숨을 아껴" },
        W(Snag.Oxygen, "suit", "우주복 산소를 나눠 쓴다", WayBy.Crew, WayFx.Generic, 6, 0.45f, 0.05f, WayLook.SuitShare) with { Near = new[] { FurnitureType.SuitLocker }, Gen = new(O2: 1.5f), Side = "우주복 산소가 빈다", Later = "우주복을 다시 채운다" },
        W(Snag.Oxygen, "gather", "한 방에 모여 문을 닫는다", WayBy.Crew, WayFx.Gather, 6, 0.45f, 0f, WayLook.Huddle) with { Line = "한 방만 지키자" },

        // ── 식량 ──
        W(Snag.Food, "ration", "비상식량을 푼다", WayBy.Crew, WayFx.Ration, 5, 0.6f, 0f, WayLook.RationBox) with { Book = true },
        W(Snag.Food, "porridge", "남은 걸 모아 죽을 쑨다", WayBy.Crew, WayFx.Generic, 40, 0.5f, 0f, WayLook.Porridge) with { Near = new[] { FurnitureType.Stove, FurnitureType.Oven }, Skill = Skill.Cooking, Gen = new(Food: 0.15f, Stress: -0.03f), Line = "한 솥이면 다 먹는다" },
        W(Snag.Food, "harvest", "덜 자란 걸 앞당겨 거둔다", WayBy.Crew, WayFx.Existing, 20, 0.5f, 0f, WayLook.None) with { Order = WorkKind.Harvest, ShipCost = 0.1f },
        W(Snag.Food, "fast", "하루 한 끼로 줄인다", WayBy.Crew, WayFx.Ration, 2, 0.4f, 0f, WayLook.None) with { Gen = new(Stress: 0.05f), Side = "배고프고 예민해진다" },
        W(Snag.Food, "coldwall", "차가운 외벽 옆에 음식을 둔다", WayBy.Crew, WayFx.Generic, 10, 0.35f, 0f, WayLook.ColdStash) with { Gen = new(Food: 0.05f), Line = "외벽 옆은 냉장고만큼 차다" },
        W(Snag.Food, "trade", "교역 · 원정으로 구한다", WayBy.Computer, WayFx.Existing, 600, 0.6f, 0.1f, WayLook.None),

        // ── 물 ──
        W(Snag.Water, "recycler", "정수기를 다시 돌린다", WayBy.Crew, WayFx.Existing, 40, 0.85f, 0f, WayLook.None) with { Order = WorkKind.Repair, Book = true },
        W(Snag.Water, "ice", "얼음을 녹인다", WayBy.Crew, WayFx.Existing, 30, 0.7f, 0f, WayLook.None) with { Order = WorkKind.MeltIce },
        W(Snag.Water, "condense", "벽에 맺힌 물을 받는다", WayBy.Crew, WayFx.Generic, 30, 0.35f, 0f, WayLook.Condense) with { Near = new[] { FurnitureType.Dehumidifier, FurnitureType.AirPurifier }, Gen = new(Water: 8f), Line = "공기 속에도 물이 있다" },
        W(Snag.Water, "ration", "물을 아껴 쓴다", WayBy.Crew, WayFx.Ration, 2, 0.4f, 0f, WayLook.None) with { Gen = new(Stress: 0.03f), Side = "씻기 · 빨래를 미룬다" },
        W(Snag.Water, "hydro", "수경 양액을 걸러 마신다", WayBy.Crew, WayFx.Generic, 20, 0.45f, 0.05f, WayLook.Bucket) with { Near = new[] { FurnitureType.GrowBed }, Gen = new(Water: 10f), ShipCost = 0.15f, Side = "작물이 마른다" },
        W(Snag.Water, "urine", "소변까지 걸러 쓴다", WayBy.Crew, WayFx.Generic, 20, 0.4f, 0f, WayLook.Splitter) with { Gen = new(Water: 6f, Stress: 0.05f), Line = "다 물이다" },

        // ── 과열 ──
        W(Snag.Overheat, "water", "물을 뿌려 식힌다", WayBy.Crew, WayFx.Generic, 6, 0.55f, 0.15f, WayLook.Bucket) with { Gen = new(Temp: -7f, Water: -10f), Side = "바닥이 젖는다" },
        W(Snag.Overheat, "door", "찬 옆방으로 문을 연다", WayBy.Crew, WayFx.Air, 2, 0.45f, 0f, WayLook.FanDoor) with { Gen = new(Temp: -5f, Vent: true) },
        W(Snag.Overheat, "off", "열 나는 설비를 끈다", WayBy.Crew, WayFx.Generic, 3, 0.6f, 0.05f, WayLook.OffTag) with { Gen = new(Temp: -6f), ShipCost = 0.1f, Side = "그 설비가 쉰다", Later = "식으면 다시 켠다" },
        W(Snag.Overheat, "ice", "얼음 덩어리를 들여 놓는다", WayBy.Crew, WayFx.Generic, 4, 0.45f, 0f, WayLook.ColdStash) with { Things = new[] { ArticleKind.IceBlock }, Gen = new(Temp: -5f) },
        W(Snag.Overheat, "coolant", "냉매를 더 채운다", WayBy.Crew, WayFx.Existing, 25, 0.8f, 0.05f, WayLook.None) with { Order = WorkKind.RefillCoolant, Book = true },
        W(Snag.Overheat, "leave", "방을 비우고 식기를 기다린다", WayBy.Crew, WayFx.Gather, 3, 0.35f, 0f, WayLook.None),

        // ── 독가스 ──
        W(Snag.ToxicGas, "seal", "문을 닫고 가둔다", WayBy.Crew, WayFx.Air, 2, 0.5f, 0.1f, WayLook.Towel) with { Gen = new(Close: true) },
        W(Snag.ToxicGas, "vent", "배기로 뽑아낸다", WayBy.Computer, WayFx.Air, 3, 0.85f, 0.05f, WayLook.None) with { Gen = new(Toxin: 0.2f, Smoke: 0.5f), ShipCost = 0.15f, Book = true },
        W(Snag.ToxicGas, "scrub", "스크러버를 최대로 돌린다", WayBy.Computer, WayFx.Air, 5, 0.6f, 0f, WayLook.None) with { Gen = new(Toxin: 0.5f) },
        W(Snag.ToxicGas, "cloth", "젖은 천으로 입을 막고 나온다", WayBy.Crew, WayFx.Air, 1, 0.35f, 0.3f, WayLook.WetCloth) with { Things = Cloth },
        W(Snag.ToxicGas, "suit", "우주복 헬멧을 쓴다", WayBy.Crew, WayFx.Generic, 4, 0.7f, 0f, WayLook.SuitShare) with { Near = new[] { FurnitureType.SuitLocker } },
        W(Snag.ToxicGas, "leave", "다들 나온다", WayBy.Crew, WayFx.Gather, 2, 0.5f, 0.05f, WayLook.None),

        // ── 통신 끊김 ──
        W(Snag.CommsDown, "runner", "사람이 뛰어 전한다", WayBy.Crew, WayFx.Signal, 6, 0.6f, 0f, WayLook.Runner) with { Line = "내가 가서 말할게" },
        W(Snag.CommsDown, "knock", "배관을 두드려 신호한다", WayBy.Crew, WayFx.Signal, 3, 0.35f, 0f, WayLook.Knock) with { Line = "세 번 · 두 번 — 다들 안다" },
        W(Snag.CommsDown, "lamp", "불빛으로 신호한다", WayBy.Crew, WayFx.Signal, 3, 0.4f, 0f, WayLook.SignalLamp) with { Line = "창 너머로 깜빡이면 보인다" },
        W(Snag.CommsDown, "radio", "휴대 무전기를 나눠 준다", WayBy.Crew, WayFx.Signal, 5, 0.65f, 0f, WayLook.Radio) with { Things = new[] { ArticleKind.Radio } },
        W(Snag.CommsDown, "drone", "드론이 중계한다", WayBy.Robot, WayFx.Existing, 5, 0.6f, 0f, WayLook.None),
        W(Snag.CommsDown, "fix", "끊긴 선을 다시 잇는다", WayBy.Crew, WayFx.Existing, 30, 0.85f, 0.05f, WayLook.None) with { Order = WorkKind.Reconnect, Book = true },

        // ── 화물 이탈 ──
        W(Snag.CargoLoose, "strap", "케이블로 묶는다", WayBy.Crew, WayFx.Strap, 8, 0.75f, 0.25f, WayLook.Strap) with { Item = ItemKind.Cable, Later = "제 고정끈으로 바꾼다" },
        W(Snag.CargoLoose, "wedge", "숟가락 · 쐐기를 끼운다", WayBy.Crew, WayFx.Wedge, 3, 0.5f, 0.2f, WayLook.Wedge) with { Line = "숟가락 하나면 바퀴가 선다", Side = "숟가락이 휜다" },
        W(Snag.CargoLoose, "robot", "로봇이 붙든다", WayBy.Robot, WayFx.RobotHold, 4, 0.6f, 0f, WayLook.RobotBrace),
        W(Snag.CargoLoose, "thrust", "추진을 멈춘다", WayBy.Computer, WayFx.Generic, 1, 0.6f, 0f, WayLook.None) with { Leave = true, ShipCost = 0.1f, Side = "항로가 늦어진다" },
        W(Snag.CargoLoose, "jettison", "밖으로 버린다", WayBy.Crew, WayFx.Existing, 15, 0.8f, 0.1f, WayLook.None) with { Order = WorkKind.Jettison, ShipCost = 0.3f },
        W(Snag.CargoLoose, "clear", "길을 비우고 비켜 선다", WayBy.Crew, WayFx.Gather, 1, 0.3f, 0f, WayLook.None),

        // ── 물 샘 ──
        W(Snag.Leak, "valve", "밸브를 잠근다", WayBy.Crew, WayFx.Existing, 5, 0.85f, 0.05f, WayLook.None) with { Order = WorkKind.CloseValve, Book = true },
        W(Snag.Leak, "patch", "배관을 땜질한다", WayBy.Crew, WayFx.Existing, 25, 0.85f, 0.05f, WayLook.None) with { Order = WorkKind.PatchPipe, Book = true },
        W(Snag.Leak, "tape", "테이프로 감고 호스로 돌린다", WayBy.Crew, WayFx.TapeHose, 8, 0.6f, 0.1f, WayLook.TapeHose) with { Line = "테이프에 호스 하나면 물길이 바뀐다", Later = "배관을 제대로 땜질한다" },
        W(Snag.Leak, "towel", "수건 · 양동이로 받아 낸다", WayBy.Crew, WayFx.Mop, 6, 0.35f, 0f, WayLook.Towel) with { Things = Cloth },
        W(Snag.Leak, "freeze", "얼려서 막는다", WayBy.Crew, WayFx.Generic, 6, 0.5f, 0.1f, WayLook.FrostPlug) with { Near = new[] { FurnitureType.CoolantPump, FurnitureType.Fridge }, Gen = new(Flood: 0.5f, Temp: -1f), Later = "배관을 제대로 땜질한다" },
        W(Snag.Leak, "pump", "펌프로 뽑아낸다", WayBy.Crew, WayFx.Generic, 15, 0.5f, 0f, WayLook.None) with { Near = new[] { FurnitureType.CoolantPump, FurnitureType.WaterRecycler }, Gen = new(Flood: 0.3f) },

        // ── 추위 ──
        W(Snag.Cold, "blanket", "담요를 두른다", WayBy.Crew, WayFx.Generic, 2, 0.4f, 0f, WayLook.Blanket) with { Things = Cloth, Near = new[] { FurnitureType.Bed, FurnitureType.Cot }, Gen = new(Stress: -0.03f) },
        W(Snag.Cold, "warmer", "냄비에 배터리를 물려 보온기를 만든다", WayBy.Crew, WayFx.Warmer, 12, 0.55f, 0.1f, WayLook.PotWarmer) with { Near = new[] { FurnitureType.Stove, FurnitureType.Oven }, Item = ItemKind.Cable, Skill = Skill.Electrical, SkillMin = 0.2f, Gen = new(Temp: 4f, Kwh: -0.5f), Side = "배터리를 먹는다", Line = "냄비 바닥에 열선을 붙이면 된다" },
        W(Snag.Cold, "huddle", "한데 붙어 앉는다", WayBy.Crew, WayFx.Gather, 3, 0.35f, 0f, WayLook.Huddle) with { Gen = new(Stress: -0.04f) },
        W(Snag.Cold, "move", "따뜻한 방으로 옮긴다", WayBy.Crew, WayFx.Gather, 4, 0.45f, 0f, WayLook.None),
        W(Snag.Cold, "jog", "몸을 움직인다", WayBy.Crew, WayFx.Generic, 10, 0.3f, 0f, WayLook.Pedal) with { Gen = new(Rest: -0.08f, Stress: -0.02f) },
        W(Snag.Cold, "heater", "난방을 고친다", WayBy.Crew, WayFx.Existing, 30, 0.85f, 0.05f, WayLook.None) with { Order = WorkKind.Repair, Book = true },

        // ── 불꽃 튀는 배선 ──
        W(Snag.Spark, "breaker", "차단기를 내린다", WayBy.Crew, WayFx.Existing, 4, 0.85f, 0.1f, WayLook.None) with { Order = WorkKind.IsolatePower, Book = true },
        W(Snag.Spark, "mat", "고무 매트 위에서 맨손 대신 절연 장갑으로", WayBy.Crew, WayFx.Generic, 10, 0.6f, 0.2f, WayLook.MatPlug) with { Things = new[] { ArticleKind.RubberMat }, Skill = Skill.Electrical, SkillMin = 0.3f, Line = "고무 위면 안 탄다" },
        W(Snag.Spark, "powder", "가루를 뿌려 덮는다", WayBy.Crew, WayFx.Generic, 3, 0.45f, 0.15f, WayLook.Sawdust) with { Things = new[] { ArticleKind.PowderSack } },
        W(Snag.Spark, "cut", "그 회로를 통째로 끊는다", WayBy.Computer, WayFx.Generic, 1, 0.75f, 0f, WayLook.None) with { ShipCost = 0.15f, Side = "그 회로 설비가 다 선다" },
        W(Snag.Spark, "back", "물러서서 지켜본다", WayBy.Crew, WayFx.Signal, 2, 0.2f, 0f, WayLook.None),

        // ── 쏟은 것 ──
        W(Snag.Spill, "mop", "걸레로 닦는다", WayBy.Crew, WayFx.Mop, 8, 0.7f, 0.05f, WayLook.Towel) with { Book = true },
        W(Snag.Spill, "powder", "가루를 뿌려 빨아들인다", WayBy.Crew, WayFx.Mop, 5, 0.6f, 0f, WayLook.Sawdust) with { Things = new[] { ArticleKind.PowderSack } },
        W(Snag.Spill, "towel", "수건 · 천으로 찍어 낸다", WayBy.Crew, WayFx.Mop, 6, 0.55f, 0f, WayLook.Towel) with { Things = Cloth },
        W(Snag.Spill, "robot", "청소 로봇에 맡긴다", WayBy.Robot, WayFx.Existing, 10, 0.6f, 0f, WayLook.None),
        W(Snag.Spill, "drain", "바닥 배수구로 쓸어 보낸다", WayBy.Crew, WayFx.Mop, 4, 0.4f, 0f, WayLook.None),

        // ── 조명 꺼짐 ──
        W(Snag.Dark, "fix", "조명을 고친다", WayBy.Crew, WayFx.Existing, 15, 0.85f, 0.05f, WayLook.None) with { Order = WorkKind.FixLights, Book = true },
        W(Snag.Dark, "lamp", "작업등을 끌어 온다", WayBy.Crew, WayFx.Lamp, 5, 0.6f, 0f, WayLook.Lamp) with { Gen = new(LightHours: 6f) },
        W(Snag.Dark, "torch", "손전등을 든다", WayBy.Crew, WayFx.Lamp, 1, 0.4f, 0f, WayLook.Lamp) with { Gen = new(LightHours: 2f) },
        W(Snag.Dark, "screen", "단말 화면 불빛으로 버틴다", WayBy.Crew, WayFx.Lamp, 1, 0.3f, 0f, WayLook.Terminal) with { Near = new[] { FurnitureType.Console }, Gen = new(LightHours: 2f) },
        W(Snag.Dark, "feel", "벽을 짚어 간다", WayBy.Crew, WayFx.Signal, 1, 0.2f, 0.1f, WayLook.None),

        // ── 막힘 ──
        W(Snag.Clog, "filter", "필터를 간다", WayBy.Crew, WayFx.Existing, 20, 0.85f, 0f, WayLook.None) with { Order = WorkKind.Repair, Item = ItemKind.Filter, Book = true },
        W(Snag.Clog, "plunger", "세게 밀어 뚫는다", WayBy.Crew, WayFx.Generic, 8, 0.5f, 0.05f, WayLook.Plunger) with { Line = "밀어서 안 되는 건 없다" },
        W(Snag.Clog, "flush", "물을 세게 흘려 뚫는다", WayBy.Crew, WayFx.Generic, 10, 0.55f, 0f, WayLook.Bucket) with { Gen = new(Water: -8f) },
        W(Snag.Clog, "cloth", "천을 겹쳐 임시 필터를 끼운다", WayBy.Crew, WayFx.Improvise, 12, 0.45f, 0f, WayLook.Improvised) with { Things = Cloth, Later = "제 필터로 바꾼다", Line = "천 세 겹이면 거른다" },
        W(Snag.Clog, "bypass", "우회관을 단다", WayBy.Crew, WayFx.Existing, 40, 0.7f, 0.05f, WayLook.None) with { Order = WorkKind.LayBypass },
    };

    private static readonly Dictionary<Snag, Way[]> _of = All.GroupBy(x => x.Snag).ToDictionary(g => g.Key, g => g.ToArray());
    private static readonly Dictionary<string, Way> _id = All.ToDictionary(x => x.Id);

    public static IReadOnlyList<Way> Of(Snag s) => _of.TryGetValue(s, out var a) ? a : Array.Empty<Way>();
    public static Way? Get(string id) => _id.TryGetValue(id, out var x) ? x : null;
    public static int Index(string id) { for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i; return -1; }

    /// <summary>시험용: 빈칸 점검 (문제마다 5갈래 이상 · 이름 · 그림).</summary>
    public static List<string> Audit()
    {
        var bad = new List<string>();
        foreach (var s in Enum.GetValues<Snag>())
            if (Of(s).Count < 5) bad.Add($"{Name(s)} 갈래 {Of(s).Count}");
        foreach (var w in All)
        {
            if (w.Name.Length == 0) bad.Add($"{w.Id} 이름 없음");
            if (w.Fx is not (WayFx.Existing or WayFx.Signal or WayFx.Gather or WayFx.Ration or WayFx.Generic or WayFx.RemoteOpen or WayFx.Push or WayFx.Recycle) && w.Look == WayLook.None
                && w.Fx is not (WayFx.Air or WayFx.RobotCarry or WayFx.Mop)) bad.Add($"{w.Id} 그림 없음");
        }
        return bad;
    }
}

/// <summary>
/// 성공 판정은 규칙에서: 재질 × 원소(Matter) · 물리. 표에 없는 물건도 재질이 맞으면 같은 식으로 판정된다.
/// </summary>
public static class WaysRules
{
    /// <summary>구멍을 막는 마개 품질 0~1: 부드러워 들러붙고(1-단단함) · 공기가 안 새고(1-흡수) · 버틸 만큼 단단해야 한다. 깨지는 것 · 종이는 못 버틴다.</summary>
    public static float PlugQuality(Material m, float bulk, float breach)
    {
        var s = Materials.Of(m);
        float conform = 1f - s.Hard, tight = 1f - s.Absorb, strong = s.Hard;
        float q = 0.2f + 0.3f * conform + 0.3f * tight + 0.2f * strong + bulk;
        if (Matter.Breakable(m)) q *= 0.3f; // 유리 · 사기 — 기압에 깨진다
        if (s.Hard < 0.1f && s.Absorb > 0.8f && bulk < 0.1f) q *= 0.5f; // 종이 · 얇은 천 — 빨려 나간다
        if (strong > 0.8f && breach >= 0.25f && bulk < 0.15f) q -= 0.2f; // 딱딱한 작은 것은 큰 구멍의 들쭉날쭉한 가장자리를 못 덮는다
        return Math.Clamp(q, 0f, 0.9f);
    }

    /// <summary>임시 마개가 시간당 닳는 양: 숨구멍이 많을수록(흡수) 바람에 뜯기고, 딱딱한 것은 가장자리로 샌다.</summary>
    public static float PlugDecayPerHour(Material m, float pressure)
    {
        var s = Materials.Of(m);
        float d = 0.03f + 0.12f * s.Absorb + 0.03f * s.Hard + (Matter.React(m, Element.Pressure) is Reaction.Crack or Reaction.Shatter ? 0.1f : 0f);
        return d * (pressure > 60f ? 1f : 0.5f);
    }

    /// <summary>천으로 불을 덮는다: 젖은 천은 안 붙고, 마른 천은 큰 불에 같이 탄다 (재질 × 불).</summary>
    public static float SmotherChance(Material m, float wet, float intensity, int cells)
    {
        float ign = Matter.Ignitability(m, wet);
        float p = 0.9f - 0.25f * MathF.Max(0, cells - 1) - ign * intensity * 0.8f;
        return Math.Clamp(p, 0.05f, 0.95f);
    }

    /// <summary>물 + 전기 = 감전 (Matter 원소 쌍) · 바닥재가 얼마나 통하나. 0이면 전기 없는 불.</summary>
    public static float ShockChance(World w, CrewMember c, float live)
    {
        if (live <= 0f) return 0f;
        float pair = Matter.Pair(Element.Water, Element.Electric).R == Reaction.Short ? 1f : 0.6f;
        return Math.Clamp(0.55f * live * pair * w.Matter.ShockMul(c) * (c.Suit != null ? 0.2f : 1f), 0f, 0.9f);
    }

    /// <summary>불 곁에 살아 있는 전기가 있나 (전기 불): 켜진 설비 · 살아 있는 접속부 · 그 방 차단기.</summary>
    public static float LiveNear(World w, Room room, Cell at)
    {
        if (!room.Powered || room.BreakerOff) return 0f;
        float live = 0f;
        foreach (var d in Cell.Dirs8)
            if (w.Ship.FurnitureAt(at + d)?.Machine is Machine m && m.Powered && m.Spec.PowerDraw > 0.3f) live = MathF.Max(live, 0.6f + 0.1f * MathF.Min(4f, m.Spec.PowerDraw));
        if (w.Ship.FurnitureAt(at)?.Machine is Machine m0 && m0.Powered) live = MathF.Max(live, 0.9f);
        foreach (var j in w.Matter.Junctions) if (j.Live && j.Room == room.Id && Math.Abs(j.At.X - at.X) + Math.Abs(j.At.Y - at.Y) <= 2) live = MathF.Max(live, 0.8f);
        return MathF.Min(1f, live);
    }

    /// <summary>근처 설비가 주는 물건의 재질과 덩치 (매트리스 · 상판 · 냄비 · 담요 …).</summary>
    public static (Material mat, float bulk, string name) FromFurniture(FurnitureType t) => t switch
    {
        FurnitureType.Bed or FurnitureType.Cot or FurnitureType.MedBed => (Material.Fabric, 0.12f, "매트리스"),
        FurnitureType.Table or FurnitureType.GameTable => (Material.Wood, 0.12f, "탁자 상판"),
        FurnitureType.Stove or FurnitureType.Oven => (Material.Metal, 0.08f, "냄비"),
        FurnitureType.FireBlanket => (Material.Fabric, 0.2f, "방화 담요"),
        FurnitureType.GrowBed => (Material.Liquid, 0f, "수경 탱크 물"),
        FurnitureType.WaterRecycler => (Material.Liquid, 0f, "정수기 물"),
        FurnitureType.CoolantPump or FurnitureType.HeatExchanger => (Material.Ice, 0.1f, "냉매"),
        FurnitureType.Fridge => (Material.Ice, 0.05f, "냉동실 얼음"),
        FurnitureType.Workbench or FurnitureType.ToolWall => (Material.Metal, 0f, "쇠지레"),
        FurnitureType.Lathe => (Material.Metal, 0f, "가공기"),
        FurnitureType.Treadmill => (Material.Metal, 0f, "달리기 기구"),
        FurnitureType.MaintCart or FurnitureType.Hoist => (Material.Metal, 0f, "짐수레"),
        FurnitureType.RobotDock => (Material.Metal, 0f, "로봇 배터리"),
        FurnitureType.Console or FurnitureType.MainComputer => (Material.Plastic, 0f, "단말"),
        FurnitureType.SuitLocker => (Material.Fabric, 0f, "우주복"),
        FurnitureType.SupplyCache => (Material.Metal, 0f, "비상 물자함"),
        _ => (Material.Metal, 0f, FurnitureTypes.Name(t)),
    };

    /// <summary>굴러다니는 물건의 덩치 (마개로 쓸 때).</summary>
    public static float Bulk(ArticleKind k) => k switch
    {
        ArticleKind.Rug => 0.1f, ArticleKind.RubberMat => 0.05f, ArticleKind.PlasticCrate => -0.15f /* 상자엔 틈 · 손잡이 구멍 */, ArticleKind.CardboardBox => 0f, _ => 0f,
    };

    /// <summary>부품마다 생활 물건으로 만드는 임시품 (없으면 null).</summary>
    public static string? Improvised(ItemKind part) => part switch
    {
        ItemKind.Bearing => "숟가락을 갈아 만든 부싱",
        ItemKind.Fuse => "구리선을 감은 퓨즈",
        ItemKind.Filter => "천을 세 겹 접은 필터",
        ItemKind.Cable => "다른 방 조명선을 뽑아 이은 선",
        ItemKind.Lubricant => "식용유",
        ItemKind.Seal or ItemKind.Gasket => "고무 매트를 오린 패킹",
        ItemKind.Valve => "볼펜 스프링으로 만든 밸브",
        _ => null,
    };
}
