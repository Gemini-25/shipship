using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>개조의 종류. 무엇을 겪었느냐가 무엇을 고칠지를 정한다.</summary>
public enum UpgradeKind
{
    ReinforceHull, // 운석이 자주 들어온 곳에 보강판 (구조)
    AddBattery,    // 정전을 겪은 배: 배터리 증설 (전력)
    Feeder,        // 회로가 자주 끊긴 배: 필수 회로 예비 배선 (전력망 이중화)
    Mk3,           // 자주 말썽이던 설비를 개량형으로
    SettleDorm,    // 오래 쓴 임시 침실을 제대로 된 침실로 (방 개조)
    Suppression,   // 불이 여러 번 난 방에 자동 소화 장치
    BraceRoom,     // v8: 연결부가 끊겼던 방에 연결부를 하나 더 박는다 (처음보다 단단한 방)
    ThickPipe,     // v9: 터졌던 냉각·급수 배관을 두꺼운 관으로 (같은 충격에 덜 샌다)
    BackupController, // v9.2: 자동화가 꺼졌던 배: 배전실에 예비 제어기 (격벽·댐퍼·경보만)
    AddGrowBed,    // v10.1: 굶주렸던 배: 수경재배실에 두 칸짜리 재배대 (먹을 것은 늘고 물은 더 든다)
    Partition,     // v10.2: 통째로 감압됐던 큰 방: 가운데 칸막이 벽과 격벽 문 (다음엔 절반만 잃는다 — 방 크기 개조)
    TierUp,        // v10.5: 연구로 풀린 다음 단계로 설비를 올린다 (원자로 → 개량 핵분열로 → 핵융합로 …)
    Module,        // v10.6: 방에 추가 모듈을 단다 (재배실 LED 등, 냉각 열교환기 …)
    SupplyCache,   // v10.10: 파공·불을 겪었거나 창고에서 먼 방에 비상 물자함 (실링폼·구급 키트·소화기를 나눠 둔다)
    RemovePartition, // v10.12: 오래 평화로웠던 방의 칸막이를 걷어 넓은 방으로 (벽 재료를 되찾는다)
    Relocate,      // v10.12: 여러 번 뚫리거나 버렸던 외벽 방의 설비를 안쪽 방으로 옮긴다
    AuxWorkshop,   // v11.1: 작업대가 한 방에만 있는 배: 안쪽 방에 보조 작업대 (정비실을 잃어도 만들 수 있다)
    BackupHelm,    // v11.1: 함교가 뚫렸거나 자동화가 꺼졌던 배: 엔진실에 예비 조타석 (함교를 잃어도 배를 몬다)
    RingMain,      // v12.3: 간선이 끊겨 정전을 겪은 배: 배전실에서 핵심 방으로 선체 속을 도는 보조 간선
    Repurpose,     // v12.6: 겪은 일에 맞춰 방의 세부 용도를 바꾼다 (창고 → 대피소, 휴게실 → 체력단련실·기도실, 침실 → 조용한 침실, 의무실 → 격리실)
    Robot,         // v15.7: 겪은 일이 부르는 로봇·드론을 짜 들인다 (굶주림 → 배식 로봇, 조명 고장 → 배선 로봇 … RobotsV15.cs)
}

/// <summary>개조 계획 하나: 무엇을, 왜(겪은 사고), 무엇으로.</summary>
public sealed record UpgradePlan(UpgradeKind Kind, WorkTarget Target, float Score, Skill Skill, string Why,
    (ItemKind kind, int count)[] Cost, float MinSkill, int Circuit = -1)
{
    public string Key => Target.Key + ":" + Kind;
    public float Urgency => 0.24f + 0.04f * MathF.Min(Score, 2f);
}

/// <summary>
/// 진화 (v7, 리뷰어 17~19). 평화가 열두 시간 넘게 이어지고 재료가 남으면, 겪은 사고가 가르쳐 준 곳을 고쳐 짠다.
/// - 운석이 많았던 배 → 맞은 자리 외벽에 보강판 (같은 충격에 덜 상한다)
/// - 원자로가 멈추고 캄캄했던 배 → 배터리 증설
/// - 배전반 회로가 자주 끊긴 배 → 필수 회로 예비 배선 (끊기면 저절로 넘어간다)
/// - 자주 고장 난 설비 → Mk.3 개량형
/// - 오래 쓴 임시 침실 → 제대로 된 침실
/// 개조는 회의에서 정한다 (Council). 비상용 재료는 남겨 둔다 (Reserve).
/// 같은 시드라도 사고의 순서와 종류가 다르면 한 달 뒤 우주선의 모습이 달라진다.
/// </summary>
public static class Evolution
{
    public static float PeaceHours = 12f;
    public static float GapHours = 16f;
    public const int MaxBatteries = 2;
    public const int MaxGrowBeds = 6;

    /// <summary>v10.1: 재배대 하나를 더 먹일 물이 남지 않는다 (재생기가 만드는 물보다 쓰는 물이 많거나 거의 같다).</summary>
    public static bool WaterShort(World w) => w.Water.Produced - w.Water.Consumed < WaterSystem.BedLitersPerHour * 0.5f;

    /// <summary>v10.1: 굶주린 사람·시간 (누적). 식량이 모자랐던 배의 교훈.</summary>
    public static float StarvedHours(World w) => w.Crew.Sum(c => c.Stats.TicksStarving) / (float)SimTime.TicksPerHour
                                                 + 0.5f * w.Food.RationHours; // v10.11 배급한 시간은 굶주림의 절반으로 친다

    public static string Name(UpgradeKind k) => k switch
    {
        UpgradeKind.ReinforceHull => "외벽 보강",
        UpgradeKind.AddBattery => "배터리 증설",
        UpgradeKind.Feeder => "예비 배선",
        UpgradeKind.RingMain => "보조 간선",
        UpgradeKind.Mk3 => "Mk.3 개량",
        UpgradeKind.SettleDorm => "침실 정비",
        UpgradeKind.Suppression => "자동 소화 장치",
        UpgradeKind.BraceRoom => "연결부 증설",
        UpgradeKind.ThickPipe => "배관 보강",
        UpgradeKind.BackupController => "예비 제어기",
        UpgradeKind.AddGrowBed => "재배대 증설",
        UpgradeKind.Partition => "칸막이",
        UpgradeKind.TierUp => "설비 단계 올리기",
        UpgradeKind.Module => "모듈 설치",
        UpgradeKind.SupplyCache => "비상 물자함",
        UpgradeKind.RemovePartition => "칸막이 철거",
        UpgradeKind.Relocate => "설비 옮기기",
        UpgradeKind.AuxWorkshop => "보조 작업대",
        UpgradeKind.BackupHelm => "예비 조타석",
        UpgradeKind.Repurpose => "방 용도 변경",
        UpgradeKind.Robot => "로봇 들이기",
        _ => k.ToString(),
    };

    public static string Title(WorkOrder o) => o.Upgrade switch
    {
        UpgradeKind.ReinforceHull => $"{o.Target.Label} 보강판",
        UpgradeKind.AddBattery => $"{o.Target.Room?.Name ?? "?"}에 배터리 증설",
        UpgradeKind.Feeder => $"{PowerGrid.CircuitName(o.Circuit)} 회로 예비 배선",
        UpgradeKind.Mk3 => $"{o.Target.Label} Mk.3 개량",
        UpgradeKind.SettleDorm => $"{o.Target.Label} 제대로 된 침실로",
        UpgradeKind.Suppression => $"{o.Target.Label} 자동 소화 장치",
        UpgradeKind.BraceRoom => $"{o.Target.Room?.Name ?? "?"} 연결부 증설",
        UpgradeKind.ThickPipe => $"{o.Target.Pipe?.Name ?? "배관"} 두꺼운 관으로",
        UpgradeKind.BackupController => "배전실에 예비 제어기",
        UpgradeKind.AddGrowBed => $"{o.Target.Room?.Name ?? "수경재배실"}에 재배대 증설",
        UpgradeKind.Partition => $"{o.Target.Room?.Name ?? "?"} 가운데에 칸막이",
        UpgradeKind.TierUp => o.Target.Furniture?.Machine is Machine tm && Tech.Next(tm) is TechTier nt ? $"{o.Target.Label} → {nt.Name}" : "설비 단계 올리기",
        UpgradeKind.Module => $"{o.Target.Room?.Name ?? "?"}에 {Modules.Name((FurnitureType)o.Circuit)}",
        UpgradeKind.SupplyCache => $"{o.Target.Room?.Name ?? "?"}에 비상 물자함",
        UpgradeKind.RemovePartition => $"{o.Target.Room?.SplitFrom?.Name ?? "?"} 칸막이 걷기",
        UpgradeKind.Relocate => $"{o.Target.Label} 안쪽 방으로 옮기기",
        UpgradeKind.AuxWorkshop => $"{o.Target.Room?.Name ?? "?"}에 보조 작업대",
        UpgradeKind.BackupHelm => $"{o.Target.Room?.Name ?? "엔진실"}에 예비 조타석",
        UpgradeKind.Repurpose => $"{Ko.EulReul(o.Target.Room?.Name ?? "?")} {Ko.EuRo(RoomTypes.Name((RoomType)o.Circuit))}",
        UpgradeKind.Robot => $"{RobotsV15.NameOf(o.Circuit)} 들이기",
        _ => "개조",
    };

    public static float Hours(UpgradeKind k) => k switch
    {
        UpgradeKind.ReinforceHull => 3f,
        UpgradeKind.AddBattery => 4f,
        UpgradeKind.Feeder => 2f,
        UpgradeKind.RingMain => 4f,
        UpgradeKind.Mk3 => 5f,
        UpgradeKind.Suppression => 3f,
        UpgradeKind.BraceRoom => 3f,
        UpgradeKind.ThickPipe => 2.5f,
        UpgradeKind.BackupController => 3f,
        UpgradeKind.AddGrowBed => 4f,
        UpgradeKind.Partition => 6f,
        UpgradeKind.TierUp => 5f,
        UpgradeKind.Module => 3f,
        UpgradeKind.SupplyCache => 1.5f,
        UpgradeKind.RemovePartition => 4f,
        UpgradeKind.Relocate => 3f,
        UpgradeKind.AuxWorkshop => 4f,
        UpgradeKind.BackupHelm => 3f,
        UpgradeKind.Repurpose => 3f,
        UpgradeKind.Robot => 3f,
        _ => 2f,
    };

    /// <summary>비상용으로 남겨 두는 재료 (개조에는 이 위로 남는 것만 쓴다).</summary>
    public static int Reserve(ItemKind k) => k switch
    {
        ItemKind.Plate => 6,
        ItemKind.Structure => 2,
        ItemKind.Electronics => 2,
        ItemKind.Cable => 4,
        ItemKind.Fuse => 2,
        ItemKind.ReactorControl => 2, // 원자로 제어부는 만들기 어려워 두 개는 늘 남긴다
        _ => ItemKinds.Tier(k) is ItemTier.General or ItemTier.Advanced ? 1 : 0,
    };

    /// <summary>v13.2 방침(비축)에 맞춘 비상용 몫.</summary>
    public static int Reserve(ItemKind k, World w) => w.History.Doctrine.StockScale == 1f ? Reserve(k) : (int)MathF.Round(Reserve(k) * w.History.Doctrine.StockScale);

    public static bool Affordable(WorkBoard b, (ItemKind kind, int count)[] cost) =>
        cost.All(x => b.Have(x.kind) - x.count >= Reserve(x.kind, b.World) + b.Held(x.kind));

    public static (ItemKind kind, int count)[] Cost(UpgradeKind k, Furniture? f) => k switch
    {
        UpgradeKind.ReinforceHull => new[] { (ItemKind.Plate, 4), (ItemKind.Structure, 2) },
        UpgradeKind.AddBattery => new[] { (ItemKind.PowerController, 1), (ItemKind.Electronics, 2), (ItemKind.Plate, 2) },
        UpgradeKind.Feeder => new[] { (ItemKind.Cable, 3), (ItemKind.Fuse, 1) },
        UpgradeKind.RingMain => new[] { (ItemKind.Cable, 4), (ItemKind.Plate, 1) },
        UpgradeKind.Mk3 => new[] { (Faults.KeyPart(f!.Type), 1), (ItemKind.Electronics, 2), (ItemKind.Plate, 1) },
        UpgradeKind.Suppression => new[] { (ItemKind.Pump, 1), (ItemKind.Cable, 2), (ItemKind.Plate, 2) },
        UpgradeKind.BraceRoom => new[] { (ItemKind.Structure, 3), (ItemKind.Plate, 2) },
        UpgradeKind.ThickPipe => new[] { (ItemKind.Plate, 3), (ItemKind.Sealant, 1) },
        UpgradeKind.BackupController => new[] { (ItemKind.Electronics, 3), (ItemKind.Sensor, 1), (ItemKind.Cable, 2) },
        UpgradeKind.AddGrowBed => new[] { (ItemKind.Plate, 2), (ItemKind.Cable, 2), (ItemKind.Sealant, 1) },
        UpgradeKind.Partition => new[] { (ItemKind.Plate, 4), (ItemKind.Structure, 2), (ItemKind.Cable, 2) },
        UpgradeKind.SupplyCache => new[] { (ItemKind.Plate, 1), (ItemKind.Cable, 1) },
        UpgradeKind.RemovePartition => new[] { (ItemKind.Cable, 1) },
        UpgradeKind.Relocate => new[] { (ItemKind.Cable, 2), (ItemKind.Plate, 1) },
        UpgradeKind.AuxWorkshop => new[] { (ItemKind.Plate, 3), (ItemKind.Cable, 2), (ItemKind.Motor, 1) },
        UpgradeKind.BackupHelm => new[] { (ItemKind.Electronics, 2), (ItemKind.Cable, 2), (ItemKind.Plate, 1) },
        UpgradeKind.Repurpose => new[] { (ItemKind.Plate, 2), (ItemKind.Structure, 1) },
        _ => new[] { (ItemKind.Plate, 2) },
    };

    public static (ItemKind kind, int count)[] Cost(WorkOrder o) =>
        o.Upgrade == UpgradeKind.TierUp && o.Target.Furniture?.Machine is Machine tm && Tech.Next(tm) is TechTier nt ? nt.Cost
        : o.Upgrade == UpgradeKind.Module ? Modules.Cost((FurnitureType)o.Circuit)
        : o.Upgrade == UpgradeKind.Robot ? RobotsV15.Cost(o.Circuit)
        : Cost(o.Upgrade ?? UpgradeKind.Mk3, o.Target.Furniture);

    /// <summary>개조하고도 비상용 위로 얼마나 남나 (0 = 딱 비상용만, 1 이상 = 넉넉).</summary>
    public static float Slack(World w, (ItemKind kind, int count)[] cost) =>
        cost.Length == 0 ? 1f : cost.Min(x => (w.Board.Have(x.kind) - x.count - Reserve(x.kind, w)) / (float)Math.Max(2, x.count));

    public static string ShortTitle(UpgradePlan p) => p.Kind switch
    {
        UpgradeKind.ReinforceHull => $"{p.Target.Room?.Name ?? "?"} 외벽",
        UpgradeKind.AddBattery => "배터리",
        UpgradeKind.Feeder => "예비 배선",
        UpgradeKind.RingMain => "보조 간선",
        UpgradeKind.Mk3 => p.Target.Label,
        UpgradeKind.Suppression => $"{p.Target.Room?.Name ?? "?"} 소화 장치",
        UpgradeKind.BraceRoom => $"{p.Target.Room?.Name ?? "?"} 연결부",
        UpgradeKind.ThickPipe => p.Target.Pipe?.Name ?? "배관",
        UpgradeKind.BackupController => "예비 제어기",
        UpgradeKind.AddGrowBed => "재배대",
        UpgradeKind.Partition => $"{p.Target.Room?.Name ?? "?"} 칸막이",
        UpgradeKind.TierUp => p.Target.Furniture?.Machine is Machine tm && Tech.Next(tm) is TechTier nt ? nt.Name : "단계",
        UpgradeKind.Module => Modules.Name((FurnitureType)p.Circuit),
        UpgradeKind.SupplyCache => $"{p.Target.Room?.Name ?? "?"} 물자함",
        UpgradeKind.RemovePartition => $"{p.Target.Room?.SplitFrom?.Name ?? "?"} 칸막이 철거",
        UpgradeKind.Relocate => $"{p.Target.Label} 옮기기",
        UpgradeKind.AuxWorkshop => "보조 작업대",
        UpgradeKind.BackupHelm => "예비 조타석",
        UpgradeKind.Repurpose => $"{p.Target.Room?.Name ?? "?"} → {RoomTypes.Name((RoomType)p.Circuit)}",
        UpgradeKind.Robot => RobotsV15.NameOf(p.Circuit),
        _ => "침실",
    };

    /// <summary>v12.6 방 용도 변경 후보: 겪은 일 → 그 일을 맡을 전용 방이 없으면, 본래 방 하나를 고친다.</summary>
    private static IEnumerable<UpgradePlan> RepurposeCandidates(World w)
    {
        var ship = w.Ship;
        var alive = w.Crew.Where(c => !c.Dead).ToList();
        if (alive.Count == 0) yield break;
        UpgradePlan? Plan(string fn, RoomType from, RoomType to, float score, string why, Func<Room, float>? order = null)
        {
            if (Facilities.Best(ship, fn).factor >= 1f) return null;
            var rooms = ship.RoomsOf(from).Where(r => r.Special == null && !r.Abandoned && !r.Detached && r.Jettison == null).ToList();
            if (rooms.Count == 0 || (from is RoomType.Lounge or RoomType.Medbay && rooms.Count < 1)) return null;
            var room = order != null ? rooms.OrderBy(order).First() : rooms.Last();
            var spot = room.Cells.Where(ship.IsOpenFloor).OrderBy(c => (c.Center - room.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
            if (spot is not Cell at) return null;
            return new UpgradePlan(UpgradeKind.Repurpose, WorkTarget.AtCell(at, room), score, Skill.Mechanics,
                $"{why} → {Ko.EulReul(room.Name)} {Ko.EuRo(RoomTypes.Name(to))} ({RoomCatalog.Of(to)?.Codex.How ?? ""})", Cost(UpgradeKind.Repurpose, null), 0.25f, (int)to);
        }
        // 태양 폭풍에 방사선을 쬐었다 → 창고 벽에 물자를 쌓아 대피소로 (배 안쪽 창고부터)
        float dose = alive.Max(c => c.Dose);
        if (dose > 0.25f && Plan("shelter", RoomType.Storage, RoomType.Shelter, 0.5f + dose, $"태양 폭풍에 방사선을 {dose:0.0}Sv까지 쬐었다", r => w.Ambience.Exposure(r)) is UpgradePlan a) yield return a;
        // 전염병이 돌았다 → 의무실 하나를 음압 격리실로
        if (w.Disease.Stats.Infections >= 3 && Plan("quarantine", RoomType.Medbay, RoomType.Quarantine, 0.4f + 0.1f * w.Disease.Stats.Infections, $"병이 {w.Disease.Stats.Infections}명에게 옮았다") is UpgradePlan b) yield return b;
        // 몸이 굳었다 → 휴게실 하나에 운동 기구
        float fit = alive.Average(c => c.Fitness);
        if (fit < 0.4f && ship.RoomsOf(RoomType.Lounge).Count() >= 1 && Plan("exercise", RoomType.Lounge, RoomType.Gym, 0.3f + (0.4f - fit), $"다들 몸이 굳었다 (체력 {fit * 100:0}%)") is UpgradePlan c) yield return c;
        // 동료를 잃었거나 마음의 상처가 깊다 → 조용한 기도실
        float trauma = alive.Average(c => c.Memory.Trauma);
        bool loss = w.Crew.Any(c => c.Dead);
        if ((loss || trauma > 0.3f) && Plan("grief", RoomType.Lounge, RoomType.Chapel, 0.35f + trauma + (loss ? 0.3f : 0f), loss ? "동료를 잃었다" : $"마음의 상처가 깊다 ({trauma * 100:0}%)") is UpgradePlan d) yield return d;
        // 시끄러운 침실 → 방음
        foreach (var q in ship.RoomsOf(RoomType.Quarters).Where(r => r.Special == null && r.Noise + r.Vibration > 0.35f).Take(1))
            if (Plan("rest", RoomType.Quarters, RoomType.QuietQuarters, 0.3f + q.Noise + q.Vibration, $"{Ko.IGa(q.Name)} 시끄러워 잠을 설친다 (소음 {q.Noise * 100:0}%)", r => -(r.Noise + r.Vibration)) is UpgradePlan e) yield return e;
    }

    /// <summary>이 사람에게 이 개조안이 얼마나 와닿는지: 교훈의 무게 + 무서워하는 방 + 내 분야 + 내 설비.</summary>
    public static float PersonalScore(World w, CrewMember c, UpgradePlan p) =>
        p.Score + 1.2f * c.Memory.FearOf(p.Target.CurrentRoom) + Interest(c, p.Kind)
        + (p.Target.Furniture is Furniture f && c.Stations.Contains(f.Room.Type) ? 0.3f : 0f)
        + (p.Kind == UpgradeKind.SettleDorm && c.Bed?.Room == p.Target.Room ? 0.5f : 0f);

    public static float PersonalScore(World w, CrewMember c, WorkOrder o) =>
        Candidates(w).FirstOrDefault(p => p.Kind == o.Upgrade && p.Target.Key == o.Target.Key) is UpgradePlan plan ? PersonalScore(w, c, plan) : 0f;

    /// <summary>
    /// 이 사람이 먼저 하고 싶은 개조. v10.2: 지금 재료로 할 수 있는 것 중에서만 — 재료가 없어 못 하는 안을 핑계로
    /// 다른 개조를 계속 막지 않는다 (운석 배가 "차라리 외벽부터"로 연결부 증설을 세 번 부결하고 외벽 보강도 못 하던 것).
    /// </summary>
    public static UpgradePlan? Favorite(World w, CrewMember c) =>
        Candidates(w).Where(p => Affordable(w.Board, p.Cost)).OrderByDescending(p => PersonalScore(w, c, p)).FirstOrDefault();

    public static float Interest(CrewMember c, UpgradeKind k) => (k, c.Role) switch
    {
        (UpgradeKind.AddBattery or UpgradeKind.Feeder or UpgradeKind.RingMain, CrewRole.Electrician) => 0.3f,
        (UpgradeKind.Mk3 or UpgradeKind.Feeder or UpgradeKind.AddBattery, CrewRole.Engineer) => 0.2f,
        (UpgradeKind.ReinforceHull or UpgradeKind.Mk3, CrewRole.Technician) => 0.25f,
        (UpgradeKind.SettleDorm, CrewRole.Medic or CrewRole.Botanist) => 0.2f,
        (UpgradeKind.Suppression, CrewRole.Botanist or CrewRole.Technician) => 0.2f,
        (UpgradeKind.BraceRoom, CrewRole.Technician or CrewRole.Engineer or CrewRole.Pilot) => 0.2f,
        (UpgradeKind.ThickPipe, CrewRole.Engineer or CrewRole.Technician) => 0.25f,
        (UpgradeKind.BackupController, CrewRole.Electrician or CrewRole.Pilot) => 0.3f,
        (UpgradeKind.BackupController, CrewRole.Engineer) => 0.2f,
        (UpgradeKind.AddGrowBed, CrewRole.Botanist) => 0.4f,
        (UpgradeKind.AddGrowBed, CrewRole.Cook) => 0.3f,
        (UpgradeKind.AddGrowBed, CrewRole.Medic) => 0.15f,
        (UpgradeKind.Partition, CrewRole.Technician or CrewRole.Engineer) => 0.2f,
        (UpgradeKind.TierUp or UpgradeKind.Module or UpgradeKind.Robot, CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician) => 0.25f,
        (UpgradeKind.TierUp or UpgradeKind.Module, CrewRole.Botanist) => 0.15f,
        (UpgradeKind.SupplyCache, CrewRole.Technician) => 0.2f,
        (UpgradeKind.SupplyCache, CrewRole.Medic) => 0.2f,
        (UpgradeKind.Relocate, CrewRole.Technician or CrewRole.Engineer) => 0.25f,
        (UpgradeKind.RemovePartition, CrewRole.Cook or CrewRole.Botanist) => 0.15f,
        (UpgradeKind.AuxWorkshop, CrewRole.Technician) => 0.3f,
        (UpgradeKind.AuxWorkshop, CrewRole.Electrician or CrewRole.Engineer) => 0.15f,
        (UpgradeKind.BackupHelm, CrewRole.Pilot) => 0.35f,
        (UpgradeKind.BackupHelm, CrewRole.Engineer) => 0.2f,
        _ => 0f,
    };

    public static bool Peaceful(World w) =>
        w.History.Current == null && w.Tick - w.History.PeaceSince >= SimTime.Hours(PeaceHours)
        && w.Power.ReactorOnline && !w.Power.LowPowerMode;

    /// <summary>이 개조안을 떠받치는 교훈의 무게 (회의의 압박).</summary>
    public static float LessonWeight(World w, WorkOrder o) =>
        Candidates(w).FirstOrDefault(p => p.Kind == o.Upgrade && p.Target.Key == o.Target.Key)?.Score ?? 0.5f;

    /// <summary>지금 올릴 개조안. 이미 밀고 있는 안이 있으면 그것을 유지한다.</summary>
    public static UpgradePlan? Plan(World w)
    {
        var h = w.History;
        // v13.4 방침(개조): 보수는 개조 사이를 두 배로, 적극은 절반으로
        float gap = GapHours * (w.Policies["upgrades"] switch { 0 => 2f, 2 => 0.5f, _ => 1f });
        if (!Peaceful(w) || w.Tick - h.LastUpgradeTick < SimTime.Hours(gap)) return null;
        var list = Candidates(w)
            .Where(p => !(h.UpgradeVetoedUntil.TryGetValue(p.Target.Key + ":" + p.Kind, out var until) && w.Tick < until))
            .OrderByDescending(p => p.Score).ToList();
        if (h.PlannedUpgrade != null && list.FirstOrDefault(p => p.Key == h.PlannedUpgrade) is UpgradePlan keep && Affordable(w.Board, keep.Cost))
            return keep;
        var pick = list.FirstOrDefault(p => Affordable(w.Board, p.Cost));
        h.PlannedUpgrade = pick?.Key;
        return pick;
    }

    /// <summary>겪은 사고에서 나온 개조 후보들.</summary>
    public static IEnumerable<UpgradePlan> Candidates(World w)
    {
        var h = w.History;
        var ship = w.Ship;

        // ── 외벽 보강: 운석이 들어온 자리 둘레 ──
        foreach (var impact in h.ImpactCells.Distinct())
        {
            int impacts = h.ImpactCells.Count(c => c == impact);
            if (h.ReinforcedAt.GetValueOrDefault(impact) >= impacts) continue; // 이 자리는 이미 보강했다 (다시 맞기 전까지)
            var wall = ReinforceTargets(w, impact).FirstOrDefault();
            if (wall == null) continue;
            var (cell, room) = wall.Value;
            int hits = h.BreachesByRoom.GetValueOrDefault(room.Id);
            float score = 0.5f + 0.3f * hits + 0.2f * (impacts - 1) + (room.TimesAbandoned > 0 ? 0.3f : 0f);
            yield return new UpgradePlan(UpgradeKind.ReinforceHull, WorkTarget.OfWall(cell, room), score, Skill.Mechanics,
                $"운석이 {room.Name} 외벽을 {ShipHistory.Times(Math.Max(1, hits))} 뚫었다 → 맞은 자리 둘레에 보강판 (금속판 4 + 구조재 2)",
                Cost(UpgradeKind.ReinforceHull, null), 0.3f);
        }

        // ── 배터리 증설: 원자로가 멈춰 캄캄했던 배 ──
        float dark = h.Scrams + h.DarkHours / 4f + 0.3f * h.CircuitFaults;
        if (h.BatteriesAdded < MaxBatteries && (h.Scrams >= 1 || h.DarkHours >= 3f) && dark >= 1.2f && BatterySpot(w) is { } bs)
            yield return new UpgradePlan(UpgradeKind.AddBattery, WorkTarget.AtCell(bs.cell, bs.room), 0.4f + 0.3f * h.Scrams + h.DarkHours / 12f,
                Skill.Electrical, $"원자로가 {ShipHistory.Times(Math.Max(1, h.Scrams))} 멈췄다 · 캄캄했던 {h.DarkHours:0}시간 → 배터리 모듈 하나 더",
                Cost(UpgradeKind.AddBattery, null), 0.35f);

        // ── 예비 배선: 배전반 회로가 자주 끊긴 배 (A → B 순) ──
        var panel = ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault();
        if (panel != null && h.CircuitFaults >= 2)
        {
            int to = !w.Power.Jumpers.Any(j => j.Permanent && j.To == 0) ? 0
                : h.CircuitFaults >= 4 && !w.Power.Jumpers.Any(j => j.Permanent && j.To == 1) ? 1 : -1;
            if (to >= 0)
                yield return new UpgradePlan(UpgradeKind.Feeder, WorkTarget.Of(panel), 0.45f + 0.2f * h.CircuitFaults - 0.3f * to, Skill.Electrical,
                    $"배전반 회로가 {ShipHistory.Times(h.CircuitFaults)} 끊겼다 → {PowerGrid.CircuitName(to)} 회로에 예비 배선 (끊기면 저절로 넘어간다)",
                    Cost(UpgradeKind.Feeder, null), 0.35f, to);
        }

        // ── v12.6 방 용도 변경: 겪은 일이 가르쳐 준 방 (전용 방이 없어 본래 방이 겸하던 일) ──
        foreach (var plan in RepurposeCandidates(w)) yield return plan;
        foreach (var plan in RobotsV15.Candidates(w)) yield return plan; // v15.7 로봇·드론 들이기

        // ── v12.3 보조 간선: 간선이 끊겨 방 여럿이 한꺼번에 정전된 배 ──
        if (panel != null && w.Net.Stats.Blackouts >= 1 && w.Net.RingTargets(NetKind.Power).FirstOrDefault() is Room ringTo)
            yield return new UpgradePlan(UpgradeKind.RingMain, WorkTarget.Of(panel), 0.5f + 0.25f * w.Net.Stats.Blackouts, Skill.Electrical,
                $"간선이 끊겨 방 여럿이 {ShipHistory.Times(w.Net.Stats.Blackouts)} 한꺼번에 정전됐다 → 배전실에서 {Ko.EuRo(ringTo.Name)} 선체 속을 도는 보조 간선 (한쪽이 끊겨도 반대쪽으로)",
                Cost(UpgradeKind.RingMain, null), 0.4f);

        // ── Mk.3 개량: 자주 고장 난 설비 ──
        foreach (var m in ship.Machines)
        {
            if (m.Grade != MachineGrade.Standard || m.Faults.Count > 0 || m.Body.Room.Abandoned) continue;
            if (m.Body.Type is FurnitureType.PowerPanel or FurnitureType.Battery) continue;
            if (!m.Spec.Critical && m.Spec.Priority < 6 && m.Body.Type is not (FurnitureType.Collector or FurnitureType.Refinery)) continue;
            if (m.FaultCount < 3) continue;
            float score = 0.2f + 0.12f * m.FaultCount + (m.Spec.Critical ? 0.3f : 0f) + (m.Substitutions > 0 ? 0.2f : 0f) + (m.Rebuilds > 0 ? 0.2f : 0f);
            yield return new UpgradePlan(UpgradeKind.Mk3, WorkTarget.Of(m.Body), score, m.Spec.Skill,
                $"{Ko.IGa(m.Name)} {ShipHistory.Times(m.FaultCount)} 고장 났다" + (m.Substitutions > 0 ? " · Mk.1을 거쳤다" : "") + " → Mk.3 개량형으로 고쳐 짠다",
                Cost(UpgradeKind.Mk3, m.Body), 0.45f);
        }

        // ── 자동 소화 장치: 불이 여러 번 난 방 ──
        foreach (var room in ship.Rooms)
        {
            int fires = h.FiresByRoom.GetValueOrDefault(room.Id);
            if (room.Suppression || room.Abandoned || fires == 0 || (fires < 2 && h.Fires < 2)) continue;
            var spot = room.Cells.Where(ship.IsOpenFloor).OrderBy(c => (c.Center - room.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
            if (spot is not Cell at) continue;
            yield return new UpgradePlan(UpgradeKind.Suppression, WorkTarget.AtCell(at, room), 0.5f + 0.3f * fires, Skill.Mechanics,
                $"{room.Name}에서 불이 {ShipHistory.Times(fires)} 났다 → 천장에 자동 소화 장치 (펌프 부품 1 + 케이블 2 + 금속판 2)",
                Cost(UpgradeKind.Suppression, null), 0.3f);
        }

        // ── 연결부 증설 (v8): 연결부가 끊겼거나 떨어져 나갔다 붙은 방에 연결부를 하나 더 ──
        foreach (var room in ship.Rooms)
        {
            if (room.Detached || room.Docked || room.Wreck || room.Abandoned || room.Jettison != null || room.DesignJoints <= 0) continue;
            int breaks = room.Joints.Sum(j => j.Breaks);
            int repairs = room.Joints.Sum(j => j.Repairs); // v9: 끊어지지는 않았어도 운석에 여러 번 상해 이어 붙인 방
            if (breaks == 0 && room.Detachments == 0 && repairs < 2) continue;
            int added = room.Joints.Count(j => !j.Truss) - room.DesignJoints;
            if (added >= 2 || (added >= 1 && breaks == 0 && room.Detachments == 0) || room.Joints.Any(j => j.Broken && !j.Released)) continue;
            if (w.Structure.TrussSpot(room) is not Cell spot) continue;
            float score = 0.35f + 0.25f * breaks + 0.5f * room.Detachments + 0.08f * repairs - 0.3f * added;
            yield return new UpgradePlan(UpgradeKind.BraceRoom, WorkTarget.OfWall(spot, room), score, Skill.Mechanics,
                (room.Detachments > 0 ? $"{Ko.IGa(room.Name)} 떨어져 나갔었다" : breaks > 0 ? $"{room.Name} 연결부가 {ShipHistory.Times(breaks)} 끊겼다"
                    : $"{room.Name} 연결부를 {ShipHistory.Times(repairs)} 이어 붙였다")
                + " → 연결부를 하나 더 박는다 (구조재 3 + 금속판 2)",
                Cost(UpgradeKind.BraceRoom, null), 0.35f);
        }

        // ── 배관 보강 (v9): 터졌던 관을 두꺼운 관으로 (냉각 본관이 가장 먼저) ──
        foreach (var seg in w.Piping.Segments)
        {
            if (seg.Breaks == 0 || seg.MaxIntegrity >= 1.3f || !seg.Sound || seg.Closed || seg.Patched || seg.Bypass > 0f) continue;
            if (seg.ValveRoom == null || seg.ValveRoom.Abandoned || seg.ValveRoom.Detached) continue;
            bool main = seg.Role is PipeRole.HotLeg or PipeRole.ColdLeg;
            float score = 0.45f + 0.3f * seg.Breaks + (main ? 0.3f : seg.IsCoolant ? 0.15f : 0f) + 0.1f * seg.Patches + 0.15f * seg.Bypasses;
            yield return new UpgradePlan(UpgradeKind.ThickPipe, WorkTarget.OfPipe(seg, seg.ValveRoom), score, Skill.Mechanics,
                $"{Ko.IGa(seg.Name)} {ShipHistory.Times(seg.Breaks)} 터졌다 → 두꺼운 관으로 갈아 끼운다 (금속판 3 + 실링폼 1)",
                Cost(UpgradeKind.ThickPipe, null), 0.35f);
        }

        // ── 예비 제어기 (v9.2): 자동화가 꺼져 격벽을 손으로 닫아야 했던 배 ──
        var auto = w.Automation;
        if (auto.Present && !auto.Backup && auto.Outages >= 1 && auto.OfflineHours >= 0.5f
            && ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault() is Furniture pp && !pp.Room.Abandoned)
            yield return new UpgradePlan(UpgradeKind.BackupController, WorkTarget.Of(pp), 0.5f + 0.3f * auto.Outages + MathF.Min(1f, auto.OfflineHours / 6f), Skill.Electrical,
                $"자동화가 {ShipHistory.Times(auto.Outages)} 꺼졌다 · {auto.OfflineHours:0.#}시간 손으로 버텼다 → 배전실에 예비 제어기 (격벽·댐퍼·경보만, 전자재 3 + 센서 1 + 케이블 2)",
                Cost(UpgradeKind.BackupController, null), 0.4f);

        // ── 재배대 증설 (v10.1): 사람이 굶주렸던 배 — 먹을 것은 늘지만 물을 더 먹는다 ──
        float starved = StarvedHours(w);
        // 물통이 바닥을 보이면 재배대는 안건에도 오르지 않는다 (먹을 것보다 마실 것이 먼저)
        if (h.GrowBedsAdded < MaxGrowBeds && starved >= 6f + 10f * h.GrowBedsAdded && w.Water.Level > 0.35f * w.Water.Capacity && GrowSpot(w) is { } gs)
        {
            // 물이 빠듯한 배는 재배대가 반갑지만은 않다 (재배대 하나가 사람 둘 몫의 물을 먹는다)
            float dry = WaterShort(w) ? 0.4f : 0f;
            float score = 0.45f + MathF.Min(1.2f, starved / 30f) - 0.2f * h.GrowBedsAdded - dry;
            yield return new UpgradePlan(UpgradeKind.AddGrowBed, WorkTarget.AtCell(gs.cells[0], gs.room), score, Skill.Botany,
                $"굶주린 시간이 {starved:0}사람·시간 쌓였다 → {gs.room.Name}에 두 칸짜리 재배대 (금속판 2 + 케이블 2 + 실링폼 1)"
                + (dry > 0f ? " · 물이 빠듯하다" : ""),
                Cost(UpgradeKind.AddGrowBed, null), 0.25f);
        }

        // ── 설비 단계 올리기 (v10.5): 연구로 풀린 단계 — 겪은 일이 무엇부터 올릴지 정한다 (종류마다 한 대씩, 가장 닳은 것부터) ──
        var seenTypes = new HashSet<FurnitureType>();
        foreach (var m in ship.Machines.OrderByDescending(x => x.Wear))
        {
            var type = m.Body.Type;
            if (!Tech.HasTree(type) || seenTypes.Contains(type) || m.Body.Room.Abandoned || m.Faults.Count > 0 || m.Grade == MachineGrade.Mk1) continue;
            if (Tech.Next(m) is not TechTier next || next.Tier > Tech.Unlocked(w, type)) continue;
            seenTypes.Add(type);
            // v11.3 전기 여유: 더 먹는 단계는 원자로가 대 줄 수 있을 때만 (미리내호가 LED 재배대를 줄줄이 올려 D 회로를 계속 끊던 것)
            float extraKw = m.Spec.PowerDraw * Grades.Power(m.Grade) * MathF.Max(0f, next.Power - Tech.Of(m).Power);
            if (extraKw > 0.05f && !PowerRoom(w, extraKw) && type is not (FurnitureType.ReactorCore or FurnitureType.Battery or FurnitureType.CapacitorBank)) continue;
            var (need, why) = TierNeed(w, type);
            float score = 0.3f + need - 0.1f * (m.Tier - 1);
            yield return new UpgradePlan(UpgradeKind.TierUp, WorkTarget.Of(m.Body), score, m.Spec.Skill,
                $"연구 {w.Research:0}점으로 {next.Name} 설계가 풀렸다{why} → {Ko.EulReul(m.Name)} {next.Name}로 ({next.Note} · {string.Join(" + ", next.Cost.Select(c => $"{ItemKinds.Name(c.kind)} {c.count}"))})",
                next.Cost, 0.35f + 0.1f * (next.Tier - 2));
        }

        // ── 모듈 (v10.6): 방에 붙이는 추가 설비 — 이 방이 모자랐던 일이 있으면 ──
        foreach (var plan in Modules.Candidates(w))
        {
            // v11.3: 전기를 먹는 모듈도 원자로 여유가 있을 때만 (축전 모듈은 예외 — 전기를 모아 준다)
            var code = (FurnitureType)plan.Circuit;
            float kw = MachineSpecs.For(code)?.PowerDraw ?? 0f;
            if (kw > 0.05f && code != FurnitureType.CapacitorBank && !PowerRoom(w, kw)) continue;
            yield return plan;
        }

        // ── 비상 물자함 (v10.10): 파공·불을 겪었거나 창고에서 먼 핵심 방 (배 크기에 따라 두셋까지) ──
        if (Logistics.Caches(w) < Math.Max(2, w.Crew.Count(c => !c.Dead) / 3))
            foreach (var (room, score, why) in Logistics.CacheRooms(w).OrderByDescending(x => x.score).Take(3))
            {
                if (Modules.Spot(w, room) is not Cell at) continue;
                yield return new UpgradePlan(UpgradeKind.SupplyCache, WorkTarget.AtCell(at, room), score, Skill.Mechanics,
                    $"{why} → 실링폼·구급 키트·소화기를 가까이 둔다 (창고까지 가는 시간을 던다 · 금속판 1 + 케이블 1)",
                    Cost(UpgradeKind.SupplyCache, null), 0f);
            }

        // ── 칸막이 (v10.2, 방 크기 개조): 운석에 통째로 감압됐던 큰 방을 둘로 나눈다 ──
        foreach (var room in ship.Rooms)
        {
            if (room.Partitioned || room.Cells.Count < Remodel.MinRoomCells) continue;
            int breaches = h.BreachesByRoom.GetValueOrDefault(room.Id) - room.UnsplitBreaches; // v10.12 칸막이를 걷은 뒤로 뚫린 것만
            if (breaches < 0) breaches = 0;
            if (breaches == 0 && room.TimesAbandoned == 0) continue;
            if (Remodel.FindSplit(w, room) is not Remodel.SplitPlan sp) continue;
            var across = sp.Vertical ? new Cell(1, 0) : new Cell(0, 1);
            var stand = sp.SideB.Contains(sp.Door + across) ? sp.Door - across : sp.Door + across;
            float score = 0.3f + 0.3f * breaches + (room.TimesAbandoned > 0 ? 0.4f : 0f) + 0.004f * (room.Cells.Count - Remodel.MinRoomCells);
            yield return new UpgradePlan(UpgradeKind.Partition, WorkTarget.AtCell(stand, room), score, Skill.Mechanics,
                $"{Ko.IGa(room.Name)} 통째로 감압됐다 {ShipHistory.Times(Math.Max(1, breaches))} ({room.Cells.Count}칸) → 가운데에 칸막이 벽과 격벽 문 (다음엔 절반만 잃는다 · 금속판 4 + 구조재 2 + 케이블 2)",
                Cost(UpgradeKind.Partition, null), 0.35f);
        }

        // ── 칸막이 철거 (v10.12): 가른 뒤로 한 번도 뚫리지 않고 오래 평화로웠던 방 — 넓은 방에서 모이던 사람들이 원한다 ──
        foreach (var inner in ship.Rooms)
        {
            if (inner.Merged || inner.SplitFrom is not Room outer || !Remodel2.CanMerge(w, inner)) continue;
            float days = (w.Tick - inner.SplitSince) / (float)SimTime.TicksPerDay;
            int since = h.BreachesByRoom.GetValueOrDefault(outer.Id) + h.BreachesByRoom.GetValueOrDefault(inner.Id) - inner.SplitBreaches;
            if (days < 12f || since > 0 || w.Tick - h.PeaceSince < SimTime.TicksPerDay * 3L) continue;
            bool social = outer.Type is RoomType.Mess or RoomType.Lounge or RoomType.Galley;
            var alive = w.Crew.Where(c => !c.Dead).ToList();
            float sociable = alive.Count == 0 ? 0.5f : alive.Average(c => c.Traits.Sociability);
            float score = 0.15f + MathF.Min(0.45f, (days - 12f) * 0.02f) + (social ? 0.25f : 0f) + 0.4f * (sociable - 0.5f);
            if (score < 0.2f) continue;
            var d = inner.SplitDoor!;
            var stand = Cell.Dirs4.Select(x => d.Cell + x).FirstOrDefault(c => ship.RoomAt(c) == inner && ship.IsOpenFloor(c));
            if (stand == default) continue;
            yield return new UpgradePlan(UpgradeKind.RemovePartition, WorkTarget.AtCell(stand, inner), score, Skill.Mechanics,
                $"{Ko.EulReul(outer.Name)} 가른 지 {days:0}일 · 그 뒤로 뚫린 적 없다" + (social ? " · 좁아진 방에서 사람들이 흩어졌다" : "")
                + $" → 칸막이를 걷어 넓은 {Ko.EuRo(outer.Name)} (금속판 3 + 구조재 1을 되찾는다 · 다음 운석에는 다시 통째로)",
                Cost(UpgradeKind.RemovePartition, null), 0.25f);
        }

        // ── 설비 옮기기 (v10.12): 여러 번 뚫리거나 버렸던 외벽 방의 설비를 안쪽 방으로 ──
        foreach (var room in ship.Rooms)
        {
            if (room.Detached || room.OffLimits || Remodel2.Interior(w, room)) continue;
            int hits = h.BreachesByRoom.GetValueOrDefault(room.Id);
            // v11.1 분산: 산소 발생기가 전부 한 외벽 방에 있고 그 방이 한 번이라도 뚫렸으면 하나를 나눈다
            bool allO2 = hits >= 1 && room.Furniture.Count(x => x.Type == FurnitureType.OxygenGenerator && !x.Stowed) >= 2
                         && ship.FurnitureOf(FurnitureType.OxygenGenerator).All(x => x.Room == room);
            if (allO2)
            {
                var gen = room.Furniture.Where(x => x.Type == FurnitureType.OxygenGenerator && !x.Stowed && x.Machine!.Faults.Count == 0).OrderByDescending(x => x.Id).FirstOrDefault();
                if (gen != null && Remodel2.FindSpot(w, gen) is { } gspot)
                {
                    yield return new UpgradePlan(UpgradeKind.Relocate, WorkTarget.Of(gen), 0.55f + 0.15f * MathF.Min(3, hits), Skill.Mechanics,
                        $"산소 발생기가 모두 {room.Name}에 있다 · 그 방이 {ShipHistory.Times(hits)} 뚫렸다 → {Ko.EulReul(gen.Label)} {Ko.EuRo(gspot.room.Name)} 나눠 둔다 (한 방을 잃어도 숨은 쉰다)",
                        Cost(UpgradeKind.Relocate, null), 0.25f);
                    continue;
                }
            }
            if (hits < 2 && room.TimesAbandoned == 0) continue;
            var f = room.Furniture.Where(x => !x.Stowed && x.Machine is Machine mm && Remodel2.Movable(x.Type) && mm.Faults.Count == 0)
                .OrderByDescending(x => x.Machine!.Spec.Critical).ThenBy(x => x.Id).FirstOrDefault();
            if (f == null || Remodel2.FindSpot(w, f) is not { } spot) continue;
            float score = 0.25f + 0.15f * MathF.Min(4, hits) + (room.TimesAbandoned > 0 ? 0.3f : 0f) + (f.Machine!.Spec.Critical ? 0.2f : 0f);
            yield return new UpgradePlan(UpgradeKind.Relocate, WorkTarget.Of(f), score, Skill.Mechanics,
                $"{Ko.IGa(room.Name)} {ShipHistory.Times(Math.Max(1, hits))} 뚫렸다" + (room.TimesAbandoned > 0 ? " · 버린 적도 있다" : "")
                + $" → {Ko.EulReul(f.Label)} 외벽이 없는 {Ko.EuRo(spot.room.Name)} 옮긴다 (케이블 2 + 금속판 1)",
                Cost(UpgradeKind.Relocate, null), 0.25f);
        }

        // ── 보조 작업대 (v11.1 분산 운영): 작업대가 한 방에만 있는데 그 방이 뚫렸거나, 임시 정비실을 짜야 했거나, 큰 배 ──
        {
            var benches = ship.FurnitureOf(FurnitureType.Workbench).Where(b => !b.Stowed && !b.Room.Detached).ToList();
            var shopRooms = benches.Select(b => b.Room).Distinct().ToList();
            if (benches.Count > 0 && shopRooms.Count == 1)
            {
                var shop = shopRooms[0];
                int hits = h.BreachesByRoom.GetValueOrDefault(shop.Id);
                int crew = w.Crew.Count(c => !c.Dead);
                bool lesson = hits > 0 || shop.TimesAbandoned > 0 || w.Adapt.Workshops > 0 || crew >= 12;
                var target = new[] { RoomType.Storage, RoomType.Power, RoomType.Lounge, RoomType.Mess, RoomType.Medbay }
                    .SelectMany(t => ship.RoomsOf(t)).FirstOrDefault(r => r != shop && !r.Detached && !r.Abandoned && !r.OffLimits && Remodel2.Interior(w, r) && Adaptation.BenchCell(w, r) != null)
                    ?? new[] { RoomType.Storage, RoomType.Power, RoomType.Lounge, RoomType.Mess }
                    .SelectMany(t => ship.RoomsOf(t)).FirstOrDefault(r => r != shop && !r.Detached && !r.Abandoned && !r.OffLimits && Adaptation.BenchCell(w, r) != null);
                if (lesson && target != null && Adaptation.BenchCell(w, target) is Cell bc)
                {
                    float score = 0.3f + 0.2f * MathF.Min(3, hits) + (shop.TimesAbandoned > 0 ? 0.3f : 0f) + (w.Adapt.Workshops > 0 ? 0.35f : 0f) + (crew >= 12 ? 0.15f : 0f);
                    string why = hits > 0 ? $"{Ko.IGa(shop.Name)} {ShipHistory.Times(hits)} 뚫렸다" : w.Adapt.Workshops > 0 ? "정비실을 잃고 임시 작업대를 짜야 했다" : $"{crew}명이 작업대 한 방에 기댄다";
                    yield return new UpgradePlan(UpgradeKind.AuxWorkshop, WorkTarget.AtCell(bc, target), score, Skill.Mechanics,
                        $"{why} → {target.Name}에 보조 작업대 (정비실을 잃어도 만들고 연구한다 · 금속판 3 + 케이블 2 + 모터 1)",
                        Cost(UpgradeKind.AuxWorkshop, null), 0.3f);
                }
            }
        }

        // ── 예비 조타석 (v11.1 분산 운영): 함교가 뚫렸거나 자동화가 꺼졌던 배, 큰 배 — 엔진실(없으면 배전실)에 ──
        if (!ship.Furniture.Any(f => f.AuxHelm && !f.Stowed) && ship.RoomsOf(RoomType.Bridge).FirstOrDefault() is Room br)
        {
            int hits = h.BreachesByRoom.GetValueOrDefault(br.Id);
            int crew = w.Crew.Count(c => !c.Dead);
            bool lesson = hits > 0 || br.TimesAbandoned > 0 || w.Automation.Outages > 0 || crew >= 12;
            var spot = new[] { RoomType.Engine, RoomType.Power, RoomType.Workshop }.SelectMany(t => ship.RoomsOf(t))
                .Where(r => !r.Detached && !r.Abandoned && !r.OffLimits).Select(r => (room: r, cell: Adaptation.BenchCell(w, r))).FirstOrDefault(x => x.cell != null);
            if (lesson && spot.room != null && spot.cell is Cell hc)
            {
                float score = 0.3f + 0.25f * MathF.Min(3, hits) + (br.TimesAbandoned > 0 ? 0.3f : 0f) + 0.15f * MathF.Min(2, w.Automation.Outages) + (crew >= 12 ? 0.1f : 0f);
                string why = hits > 0 ? $"함교가 {ShipHistory.Times(hits)} 뚫렸다" : w.Automation.Outages > 0 ? $"자동화가 {ShipHistory.Times(w.Automation.Outages)} 꺼졌다" : $"{crew}명이 함교 하나에 기댄다";
                yield return new UpgradePlan(UpgradeKind.BackupHelm, WorkTarget.AtCell(hc, spot.room), score, Skill.Electrical,
                    $"{why} → {spot.room.Name}에 예비 조타석 (함교를 잃어도 회피 기동·항로 변경 · 전자재 2 + 케이블 2 + 금속판 1)",
                    Cost(UpgradeKind.BackupHelm, null), 0.3f);
            }
        }

        // ── 침실 정비 (v10.1): 처음부터 간이침대에서 자는 사람 (침대가 모자란 배) — 사흘 넘게 ──
        foreach (var room in ship.Rooms)
        {
            if (room.Purpose == "임시 침실" || room.Abandoned || room.Detached) continue;
            var cots = room.Furniture.Where(f => f.Type == FurnitureType.Cot && !f.Improved && f.Owner is { Dead: false } o && o.HomeBed == null).ToList();
            if (cots.Count == 0 || w.Tick - h.FoundedTick < SimTime.Hours(72)) continue;
            yield return new UpgradePlan(UpgradeKind.SettleDorm, WorkTarget.OfRoom(room), 0.5f + 0.1f * cots.Count, Skill.Mechanics,
                $"{cots.Count}명이 처음부터 간이침대에서 잤다 → 제대로 된 침대로 (금속판 2)",
                Cost(UpgradeKind.SettleDorm, null), 0f);
        }

        // ── 침실 정비: 침실을 잃고 사흘 넘게 쓴 임시 침실 ──
        foreach (var room in ship.Rooms)
        {
            if (room.Purpose != "임시 침실") continue;
            var lost = w.Crew.Where(c => c.HomeBed != null && c.HomeBed.Room.Abandoned && c.Bed?.Room == room).Select(c => c.HomeBed!.Room).FirstOrDefault();
            if (lost == null || w.Tick - lost.AbandonedSince < SimTime.Hours(72)) continue;
            if (!room.Furniture.Any(f => f.Type == FurnitureType.Cot && !f.Improved)) continue;
            yield return new UpgradePlan(UpgradeKind.SettleDorm, WorkTarget.OfRoom(room), 0.8f, Skill.Mechanics,
                $"{Ko.EulReul(lost.Name)} 잃은 지 {(w.Tick - lost.AbandonedSince) / (float)SimTime.TicksPerDay:0}일 → 간이침대를 제대로 된 침대로 (금속판 2)",
                Cost(UpgradeKind.SettleDorm, null), 0f);
        }
    }

    /// <summary>v10.5: 이 종류를 먼저 올릴 까닭 (겪은 일·지금 모자란 것).</summary>
    /// <summary>v11.3: 다 켰을 때의 수요에 kw를 더해도 원자로 정격의 92% 안인가.</summary>
    internal static bool PowerRoom(World w, float kw) => w.Power.FullDemand + kw <= w.Power.ReactorRated * 0.92f;

    private static (float need, string why) TierNeed(World w, FurnitureType t)
    {
        var h = w.History;
        var p = w.Power;
        return t switch
        {
            FurnitureType.ReactorCore when p.Demand > 0.8f * p.ReactorRated || p.Brownouts > 0 => (0.5f, " · 전기가 빠듯하다"),
            FurnitureType.Battery when h.DarkHours > 1f || h.Scrams > 0 => (0.35f, $" · 캄캄했던 {h.DarkHours:0}시간"),
            FurnitureType.GrowBed when StarvedHours(w) > 6f => (0.5f, $" · 굶주린 {StarvedHours(w):0}사람·시간"),
            FurnitureType.WaterRecycler when w.Water.Level < 0.6f * w.Water.Capacity || WaterShort(w) => (0.45f, " · 물이 빠듯하다"),
            FurnitureType.OxygenGenerator when w.Air.Reserve < 0.8f * w.Air.ReserveCapacity => (0.35f, " · 공기 탱크가 줄었다"),
            FurnitureType.CoolantPump when h.Scrams > 0 => (0.3f, " · 원자로가 섰었다"),
            FurnitureType.Workbench => (0.25f, " · 연구와 제작이 빨라진다"),
            FurnitureType.Collector or FurnitureType.Refinery when w.Ship.CountStored(ItemKind.Plate) < 10 => (0.3f, " · 금속판이 모자라다"),
            FurnitureType.SensorArray when w.Sensors.Unwarned > 0 => (0.3f, " · 경보 없이 맞은 운석"),
            _ => (0.05f, ""),
        };
    }

    /// <summary>운석이 들어온 자리 둘레의 보강할 외벽 (가까운 순).</summary>
    private static IEnumerable<(Cell cell, Room room)?> ReinforceTargets(World w, Cell impact)
    {
        var ship = w.Ship;
        foreach (var (cell, wall) in ship.Walls.OrderBy(kv => (kv.Key.Center - impact.Center).LengthSquared()))
        {
            if ((cell.Center - impact.Center).Length() > 2.3f) break;
            if (!wall.IsHull || wall.Reinforced || wall.Breach > 0f || wall.Patched) continue;
            var inside = Hull.InsideRoom(ship, cell);
            if (inside == null || inside.Abandoned || inside.Leaking || inside.Air.Pressure < 85f) continue;
            yield return (cell, inside);
        }
    }

    /// <summary>
    /// v10.1: 두 칸짜리 재배대를 놓을 자리 (길을 막지 않고 옆에 서서 돌볼 수 있는 곳).
    /// 수경재배실이 차면 휴게실·식당·창고 한쪽을 재배실로 쓴다 (방의 용도가 바뀐다).
    /// </summary>
    public static (Cell[] cells, Room room)? GrowSpot(World w)
    {
        var rooms = new[] { RoomType.Hydroponics, RoomType.Lounge, RoomType.Mess, RoomType.Storage }
            .SelectMany(t => w.Ship.RoomsOf(t))
            .Where(r => r.Type == RoomType.Hydroponics || r.Purpose == null || r.Purpose == "제2 재배실");
        foreach (var room in rooms)
        {
            if (room.Abandoned || room.Leaking || room.OffLimits) continue;
            var free = new HashSet<Cell>(Adaptation.FreeCells(w, room));
            foreach (var a in free.OrderBy(c => c.Y).ThenBy(c => c.X))
            foreach (var d in new[] { new Cell(1, 0), new Cell(0, 1) })
            {
                var b = a + d;
                if (!free.Contains(b)) continue;
                var pair = new[] { a, b };
                if (!Adaptation.SafeToBlock(w, room, pair)) continue;
                // 옆에 서서 돌볼 빈 바닥이 있어야 한다
                if (!pair.SelectMany(c => Cell.Dirs4.Select(n => c + n)).Any(n => !pair.Contains(n) && w.Ship.IsOpenFloor(n) && w.Ship.RoomAt(n) == room)) continue;
                return (pair, room);
            }
        }
        return null;
    }

    /// <summary>배터리 모듈을 놓을 자리: 배전실 → 원자로실·냉각실·생명유지실 → 창고 (통로를 막지 않는 곳).</summary>
    public static (Cell cell, Room room)? BatterySpot(World w)
    {
        foreach (var type in new[] { RoomType.Power, RoomType.Reactor, RoomType.Cooling, RoomType.LifeSupport, RoomType.Storage })
        foreach (var room in w.Ship.RoomsOf(type))
        {
            if (room.Abandoned || room.Leaking) continue;
            foreach (var cell in Adaptation.CotCells(w, room))
                if (Adaptation.SafeToBlock(w, room, cell)) return (cell, room);
        }
        return null;
    }

    /// <summary>개조를 마쳤다: 우주선이 바뀌고, 역사에 남는다.</summary>
    public static bool Apply(World w, WorkOrder o, CrewMember cm)
    {
        var h = w.History;
        var kind = o.Upgrade ?? UpgradeKind.Mk3;
        string text;
        Room? room = o.Target.CurrentRoom;
        switch (kind)
        {
            case UpgradeKind.ReinforceHull:
            {
                // 가장 가까운 운석 자리를 중심으로 둘레 여섯 칸까지
                var impact = h.ImpactCells.OrderBy(c => (c.Center - o.Target.Cell.Center).LengthSquared()).DefaultIfEmpty(o.Target.Cell).First();
                var cells = new List<Cell>();
                foreach (var (cell, wall) in w.Ship.Walls.OrderBy(kv => (kv.Key.Center - impact.Center).LengthSquared()))
                {
                    if (cells.Count >= 6 || (cell.Center - impact.Center).Length() > 2.3f) break;
                    if (!wall.IsHull || wall.Reinforced || wall.Breach > 0f || wall.Patched) continue;
                    float skill = cm.SkillLevel(Skill.Mechanics);
                    wall.Reinforced = true;
                    wall.MaxIntegrity = 1.4f + 0.1f * skill;
                    wall.Integrity = wall.MaxIntegrity;
                    MarkLog.Add(wall.Marks, w.Tick, $"{cm.Name}: 보강판을 덧댔다");
                    cells.Add(cell);
                }
                if (cells.Count == 0) return false;
                h.ReinforcedAt[impact] = h.ImpactCells.Count(c => c == impact);
                int hits = room != null ? h.BreachesByRoom.GetValueOrDefault(room.Id) : 0;
                // v9: 보강판을 덧대는 김에 그 둘레의 구조 연결부도 함께 죄어 박는다 (운석이 상하게 한 연결부가 처음보다 단단해진다)
                int braced = 0;
                foreach (var j in w.Structure.Joints)
                {
                    if (j.Broken || j.Truss || j.Room.Detached || !cells.Any(c => (c.Center - j.Cell.Center).Length() <= 3.2f)) continue;
                    float before = j.MaxStrength;
                    j.MaxStrength = MathF.Max(j.MaxStrength, 1.05f + 0.1f * cm.SkillLevel(Skill.Mechanics));
                    j.Strength = MathF.Max(j.Strength, j.MaxStrength);
                    j.Known = j.Strength;
                    if (j.MaxStrength > before) { braced++; MarkLog.Add(j.Marks, w.Tick, $"{cm.Name}: 보강판과 함께 죄어 박았다"); }
                }
                text = $"{Ko.IGa(cm.Name)} {room?.Name ?? "?"} 외벽 {cells.Count}칸에 보강판을 덧댔다" + (braced > 0 ? $" (연결부 {braced}곳도 함께)" : "") +
                       $" — 운석이 {ShipHistory.Times(Math.Max(1, hits))} 들어온 곳";
                if (room != null) MarkLog.Add(room.Marks, w.Tick, $"외벽 {cells.Count}칸 보강");
                break;
            }
            case UpgradeKind.AddBattery:
            {
                var cell = o.Target.Cell;
                if (room == null || !w.Ship.IsOpenFloor(cell) || !Adaptation.SafeToBlock(w, room, cell))
                {
                    if (BatterySpot(w) is not { } spot) return false;
                    (cell, room) = spot;
                }
                var bat = w.Ship.AddFurniture(FurnitureType.Battery, cell);
                bat.Improved = true;
                bat.Machine!.Condition = 1f;
                bat.Machine.Wear = 0f;
                MarkLog.Add(bat.Machine.Marks, w.Tick, $"{cm.Name}: 항해 중에 증설");
                w.Paths.Invalidate();
                h.BatteriesAdded++;
                text = $"{Ko.IGa(cm.Name)} {room.Name}에 배터리 모듈을 하나 더 달았다 — 원자로가 {ShipHistory.Times(Math.Max(1, h.Scrams))} 멈춘 뒤로 (+{PowerGrid.BatteryKwh(bat):0}kWh)";
                MarkLog.Add(room.Marks, w.Tick, "배터리 증설");
                break;
            }
            case UpgradeKind.Feeder:
            {
                int to = o.Circuit;
                int from = to == 0 ? 2 : 0;
                if (w.Power.Jumpers.Any(j => j.Permanent && j.To == to)) return false;
                var panel = o.Target.Furniture!;
                w.Power.AddJumper(from, to, cm.Cell, 2f, permanent: true);
                MarkLog.Add(panel.Machine!.Marks, w.Tick, $"{cm.Name}: {PowerGrid.CircuitName(from)}→{PowerGrid.CircuitName(to)} 예비 배선");
                h.FeedersAdded++;
                text = $"{Ko.IGa(cm.Name)} {PowerGrid.CircuitName(to)} 회로에 예비 배선을 깔았다 ({PowerGrid.CircuitName(from)}→{PowerGrid.CircuitName(to)}) — 회로가 {ShipHistory.Times(h.CircuitFaults)} 끊긴 뒤로";
                break;
            }
            case UpgradeKind.Repurpose:
            {
                var newKind = (RoomType)o.Circuit;
                if (room == null || room.Special != null || RoomCatalog.BaseOf(newKind) != room.Type) return false;
                string before = room.Name;
                room.FormerPurposes.Add(before);
                room.Special = newKind;
                MarkLog.Add(room.Marks, w.Tick, $"{cm.Name}: {before} → {room.Name}");
                text = $"{Ko.IGa(cm.Name)} {Ko.EulReul(before)} {Ko.EuRo(room.Name)} 고쳤다 — {RoomCatalog.Of(newKind)?.Codex.What ?? ""}";
                break;
            }
            case UpgradeKind.RingMain:
            {
                if (w.Net.SourceRoom(NetKind.Power) is not Room src || w.Net.RingTargets(NetKind.Power).FirstOrDefault() is not Room to) return false;
                w.Net.AddRing(NetKind.Power, src, to);
                MarkLog.Add(o.Target.Furniture!.Machine!.Marks, w.Tick, $"{cm.Name}: {Ko.EuRo(to.Name)} 보조 간선");
                text = $"{Ko.IGa(cm.Name)} 배전실에서 {Ko.EuRo(to.Name)} 선체 속을 도는 보조 간선을 깔았다 — 간선이 끊겨 {ShipHistory.Times(w.Net.Stats.Blackouts)} 정전된 뒤로";
                break;
            }
            case UpgradeKind.Mk3:
            {
                var m = o.Target.Furniture!.Machine!;
                if (m.Grade != MachineGrade.Standard) return false;
                m.Grade = MachineGrade.Mk3;
                m.Condition = MathF.Max(m.Condition, 0.9f);
                m.Wear = 0.05f;
                MarkLog.Add(m.Marks, w.Tick, $"{cm.Name}: Mk.3 개량 (고장 {m.FaultCount}회 뒤)");
                text = $"{Ko.IGa(cm.Name)} {Ko.EulReul(m.Name)} Mk.3 개량형으로 고쳐 짰다 — {ShipHistory.Times(m.FaultCount)} 고장 난 끝에";
                break;
            }
            case UpgradeKind.Suppression:
            {
                var r = o.Target.Room!;
                if (r.Suppression) return false;
                r.Suppression = true;
                MarkLog.Add(r.Marks, w.Tick, $"{cm.Name}: 자동 소화 장치");
                text = $"{Ko.IGa(cm.Name)} {r.Name} 천장에 자동 소화 장치를 달았다 — 불이 {ShipHistory.Times(Math.Max(1, h.FiresByRoom.GetValueOrDefault(r.Id)))} 난 곳";
                break;
            }
            case UpgradeKind.BraceRoom:
            {
                var r = o.Target.Room!;
                if (r.Detached || w.Ship.WallAt(o.Target.Cell) is not WallState ws || !ws.IsHull) return false;
                float skill = cm.SkillLevel(Skill.Mechanics);
                var j = w.Structure.AddJoint(r, o.Target.Cell, 1.05f + 0.1f * skill, 1.15f, truss: false);
                foreach (var old in r.Joints)
                    if (!old.Truss && !old.Broken) { old.MaxStrength = MathF.Max(old.MaxStrength, 1f); old.Strength = MathF.Max(old.Strength, old.MaxStrength * 0.95f); old.Known = old.Strength; }
                MarkLog.Add(j.Marks, w.Tick, $"{cm.Name}: 증설");
                MarkLog.Add(r.Marks, w.Tick, $"연결부 증설 ({r.Joints.Count(x => !x.Broken)}개)");
                int breaks = r.Joints.Sum(x => x.Breaks);
                int fixes = r.Joints.Sum(x => x.Repairs);
                text = $"{Ko.IGa(cm.Name)} {r.Name}에 연결부를 하나 더 박았다 (연결부 {r.Joints.Count(x => !x.Broken)}개 · 처음 {r.DesignJoints}개) — " +
                       (r.Detachments > 0 ? $"{ShipHistory.Times(r.Detachments)} 떨어져 나갔던 방" : breaks > 0 ? $"연결부가 {ShipHistory.Times(breaks)} 끊겼던 방"
                           : $"운석에 상한 연결부를 {ShipHistory.Times(Math.Max(1, fixes))} 이어 붙였던 방");
                break;
            }
            case UpgradeKind.BackupController:
            {
                if (w.Automation.Backup) return false;
                w.Automation.Backup = true;
                var panel = o.Target.Furniture!;
                if (panel.Machine != null) MarkLog.Add(panel.Machine.Marks, w.Tick, $"{cm.Name}: 예비 제어기를 달았다");
                text = $"{Ko.IGa(cm.Name)} 배전실에 예비 제어기를 달았다 — 주 컴퓨터가 멈춰도 격벽·댐퍼·경보는 저절로 (자동화가 {ShipHistory.Times(w.Automation.Outages)} 꺼졌던 배)";
                room = panel.Room;
                break;
            }
            case UpgradeKind.ThickPipe:
            {
                var seg = o.Target.Pipe!;
                if (!seg.Sound || seg.Closed) return false;
                float skill = cm.SkillLevel(Skill.Mechanics);
                seg.MaxIntegrity = 1.3f + 0.1f * skill;
                seg.Integrity = seg.MaxIntegrity;
                seg.Patched = false;
                MarkLog.Add(seg.Marks, w.Tick, $"{cm.Name}: 두꺼운 관으로 갈아 끼웠다");
                text = $"{Ko.IGa(cm.Name)} {Ko.EulReul(seg.Name)} 두꺼운 관으로 갈아 끼웠다 — {ShipHistory.Times(Math.Max(1, seg.Breaks))} 터졌던 관";
                room = seg.ValveRoom;
                break;
            }
            case UpgradeKind.TierUp:
            {
                if (o.Target.Furniture?.Machine is not Machine tm || Tech.Next(tm) is not TechTier nt) return false;
                string before = Tech.TierName(tm);
                tm.Tier = nt.Tier;
                tm.Wear = 0f;
                tm.Condition = 1f;
                MarkLog.Add(tm.Marks, w.Tick, $"{cm.Name}: {before} → {nt.Name}");
                text = $"{Ko.IGa(cm.Name)} {Ko.EulReul(o.Target.Label)} {Ko.EuRo(nt.Name)} 올렸다 ({Tech.Roman(nt.Tier)}단계 · {nt.Note})";
                room = tm.Body.Room;
                break;
            }
            case UpgradeKind.Robot: // v15.7
                if (!RobotsV15.Install(w, o.Circuit, cm, out text, o.Target.Cell)) return false;
                break;
            case UpgradeKind.Module:
            {
                var code = (FurnitureType)o.Circuit;
                if (room == null || !Modules.Install(w, room, code, cm, o.Target.Cell)) return false;
                text = $"{Ko.IGa(cm.Name)} {room.Name}에 {Ko.EulReul(Modules.Name(code))} 달았다 — {Modules.Note(code)}";
                break;
            }
            case UpgradeKind.SupplyCache:
            {
                if (room == null) return false;
                var cell = o.Target.Cell;
                if (!w.Ship.IsOpenFloor(cell) || !Adaptation.SafeToBlock(w, room, cell))
                {
                    if (Modules.Spot(w, room) is not Cell spot) return false;
                    cell = spot;
                }
                Logistics.InstallCache(w, room, cell, cm);
                text = $"{Ko.IGa(cm.Name)} {room.Name}에 비상 물자함을 달았다 — 실링폼·구급 키트·소화기를 창고 밖에 나눠 둔다";
                break;
            }
            case UpgradeKind.Partition:
            {
                if (room == null || Remodel.FindSplit(w, room) is not Remodel.SplitPlan sp) return false;
                var inner = Remodel.Apply(w, sp, cm);
                if (inner == null) return false;
                h.Partitions++;
                MarkLog.Add(room.Marks, w.Tick, $"{cm.Name}: 칸막이 벽과 격벽 문 — {Ko.EulReul(inner.Name)} 떼어 냈다");
                MarkLog.Add(inner.Marks, w.Tick, $"{cm.Name}: {room.Name}에서 칸막이로 떼어 낸 칸");
                text = $"{Ko.IGa(cm.Name)} {room.Name} 가운데에 칸막이 벽과 격벽 문을 세웠다 — 이제 {room.Name}({room.Cells.Count}칸)과 {inner.Name}({inner.Cells.Count}칸) (통째로 감압됐던 방)";
                break;
            }
            case UpgradeKind.RemovePartition:
            {
                if (room == null || room.SplitFrom is not Room outer) return false;
                int cells = room.Cells.Count;
                string innerName = room.Name;
                if (Remodel2.Merge(w, room, cm) is not Room merged) return false;
                // 걷어 낸 벽 재료를 선반에
                foreach (var (k, n) in new[] { (ItemKind.Plate, 3), (ItemKind.Structure, 1) })
                {
                    int left = n;
                    foreach (var box in w.Ship.Containers.Where(b => b.Type == FurnitureType.Shelf))
                    {
                        left -= box.Storage!.Add(k, left);
                        if (left <= 0) break;
                    }
                }
                h.Unpartitions++;
                MarkLog.Add(merged.Marks, w.Tick, $"{cm.Name}: 칸막이를 걷었다 — {innerName}({cells}칸)을 다시 합쳤다");
                text = $"{Ko.IGa(cm.Name)} {merged.Name} 칸막이를 걷었다 — 다시 넓은 {Ko.EuRo(merged.Name)}({merged.Cells.Count}칸) · 금속판 3 + 구조재 1을 되찾았다 (다음 운석에는 다시 통째로 감압된다)";
                room = merged;
                break;
            }
            case UpgradeKind.Relocate:
            {
                var f = o.Target.Furniture;
                if (f == null || f.Stowed || Remodel2.FindSpot(w, f) is not { } spot) return false;
                var from = f.Room;
                Remodel2.Move(w, f, spot.room, spot.cells);
                h.Relocations++;
                MarkLog.Add(f.Machine?.Marks ?? from.Marks, w.Tick, $"{cm.Name}: {from.Name} → {spot.room.Name}로 옮겼다");
                MarkLog.Add(from.Marks, w.Tick, $"{Ko.EulReul(f.Label)} 안쪽 {Ko.EuRo(spot.room.Name)} 옮겼다");
                text = $"{Ko.IGa(cm.Name)} {Ko.EulReul(f.Label)} {from.Name}에서 외벽이 없는 {Ko.EuRo(spot.room.Name)} 옮겼다 — 뚫렸던 외벽 방에서 빼냈다";
                room = spot.room;
                break;
            }
            case UpgradeKind.AuxWorkshop:
            {
                if (room == null || Adaptation.BenchCell(w, room) is not Cell spot) return false;
                var bench = w.Ship.AddFurniture(FurnitureType.Workbench, spot);
                bench.Improved = true;
                w.Paths.Invalidate();
                w.Structure.Touch();
                h.AuxWorkshops++;
                MarkLog.Add(room.Marks, w.Tick, $"{cm.Name}: 보조 작업대");
                MarkLog.Add(bench.Machine!.Marks, w.Tick, $"{cm.Name}: 정비실을 잃을 때를 대비해 짰다");
                text = $"{Ko.IGa(cm.Name)} {room.Name}에 보조 작업대를 짰다 — 정비실을 잃어도 만들고 연구한다";
                break;
            }
            case UpgradeKind.BackupHelm:
            {
                if (room == null || Adaptation.BenchCell(w, room) is not Cell spot) return false;
                var helm = w.Ship.AddFurniture(FurnitureType.Console, spot);
                helm.AuxHelm = true;
                helm.Improved = true;
                helm.Label = "예비 조타석";
                w.Paths.Invalidate();
                w.Structure.Touch();
                h.BackupHelms++;
                MarkLog.Add(room.Marks, w.Tick, $"{cm.Name}: 예비 조타석");
                text = $"{Ko.IGa(cm.Name)} {room.Name}에 예비 조타석을 짰다 — 함교를 잃어도 여기서 배를 몬다";
                break;
            }
            case UpgradeKind.AddGrowBed:
            {
                if (GrowSpot(w) is not { } gs) return false;
                var bed = w.Ship.AddFurniture(FurnitureType.GrowBed, gs.cells);
                bed.Improved = true;
                bed.Machine!.Condition = 1f;
                bed.Machine.Wear = 0f;
                bed.Machine.Crop!.Care = 0.8f;
                MarkLog.Add(bed.Machine.Marks, w.Tick, $"{cm.Name}: 굶주린 뒤에 짜 넣은 재배대");
                w.Paths.Invalidate();
                h.GrowBedsAdded++;
                room = gs.room;
                if (room.Type != RoomType.Hydroponics && room.Purpose == null)
                {
                    room.Purpose = "제2 재배실";
                    w.Adapt.Repurposed++;
                }
                text = $"{Ko.IGa(cm.Name)} {room.Name}에 두 칸짜리 재배대를 짜 넣었다 — 굶주린 시간이 {StarvedHours(w):0}사람·시간 쌓인 뒤로 (먹을 것은 늘고 물은 더 든다)";
                MarkLog.Add(room.Marks, w.Tick, "재배대 증설");
                break;
            }
            default:
            {
                var r = o.Target.Room!;
                int n = 0;
                foreach (var cot in r.Furniture.Where(f => f.Type == FurnitureType.Cot && !f.Improved))
                {
                    cot.Improved = true;
                    n++;
                }
                if (n == 0) return false;
                if (r.Type == RoomType.Quarters)
                {
                    // v10.1: 침대가 모자라 처음부터 간이침대를 놓았던 침실 — 침실은 그대로 침실이다
                    MarkLog.Add(r.Marks, w.Tick, $"{cm.Name}: 간이침대 {n}개를 제대로 된 침대로");
                    text = $"{Ko.IGa(cm.Name)} {r.Name}의 간이침대 {n}개를 제대로 된 침대로 바꿨다 — 처음부터 침대가 모자랐다";
                    break;
                }
                r.FormerPurposes.Add(r.Purpose ?? "임시 침실");
                r.Purpose = "제2 침실";
                MarkLog.Add(r.Marks, w.Tick, $"{cm.Name}: 간이침대 {n}개를 제대로 된 침대로 — 제2 침실");
                text = $"{Ko.IGa(cm.Name)} {r.Name}의 간이침대 {n}개를 제대로 된 침대로 바꿨다 — 이제 제2 침실이다";
                break;
            }
        }
        h.Upgrades++;
        h.LastUpgradeTick = w.Tick;
        h.PlannedUpgrade = null;
        if (o.Verdict != null) text += $" (결정: {o.Decider?.Name ?? "?"})";
        h.Add(w, HistoryKind.Upgrade, text, room, new[] { cm }, o.Target.Cell, log: true);
        w.Board.RequestScan();
        return true;
    }
}
