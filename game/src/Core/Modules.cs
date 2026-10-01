using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v10.6 방 모듈. 방의 기능을 키우는 추가 설비 — 개조(회의)로 단다. 전기를 먹고, 닳고, 고장 난다 (고장 나면 보너스가 사라진다).
///   수경재배실: LED 생장 모듈 (재배대 생장 +25%씩)      냉각실: 열교환 모듈 (냉각 +20%씩)
///   배전실: 축전 모듈 (배터리 +60kWh씩)                 생명유지실: CO₂ 세정 모듈 (산소 몫 +15%씩)
///   정비실: 정밀 가공 모듈 (연구 +2/일씩)
/// 방마다 넣을 수 있는 수에는 한도가 있다 (자리를 먹는다).
/// </summary>
public static class Modules
{
    public sealed record Spec(FurnitureType Type, RoomType Room, int Max, (ItemKind kind, int count)[] Cost, string Note, float Bonus);

    public static readonly Spec[] All =
    {
        new(FurnitureType.LedPanel, RoomType.Hydroponics, 2, new[] { (ItemKind.Cable, 2), (ItemKind.Electronics, 1), (ItemKind.Plate, 1) }, "재배대 생장 +25%", 0.25f),
        new(FurnitureType.HeatExchanger, RoomType.Cooling, 2, new[] { (ItemKind.Plate, 3), (ItemKind.Pump, 1) }, "냉각 +20%", 0.2f),
        new(FurnitureType.CapacitorBank, RoomType.Power, 2, new[] { (ItemKind.PowerController, 1), (ItemKind.Electronics, 2), (ItemKind.Cable, 2) }, "배터리 +60kWh", 60f),
        new(FurnitureType.Scrubber, RoomType.LifeSupport, 2, new[] { (ItemKind.Filter, 3), (ItemKind.Plate, 2), (ItemKind.Motor, 1) }, "산소 몫 +15%", 0.15f),
        new(FurnitureType.Fabricator, RoomType.Workshop, 1, new[] { (ItemKind.Motor, 1), (ItemKind.Electronics, 2), (ItemKind.Plate, 2) }, "연구 +2/일", 2f),
        // v14.6 정비 장비
        new(FurnitureType.PartTestBench, RoomType.Workshop, 1, new[] { (ItemKind.Electronics, 2), (ItemKind.Sensor, 1), (ItemKind.Plate, 1) }, "부품 시험이 두 배 빠르고 더 많이", 1f),
        new(FurnitureType.Hoist, RoomType.Workshop, 1, new[] { (ItemKind.Plate, 3), (ItemKind.Motor, 1), (ItemKind.Cable, 2) }, "무거운 부품 교체가 빠르고 허리를 다치지 않는다", 1f),
        new(FurnitureType.MaintCart, RoomType.Workshop, 1, new[] { (ItemKind.Plate, 2), (ItemKind.Bearing, 1) }, "정비실 밖 수리 −10%", 1f),
    };

    public static Spec? Of(FurnitureType t) => All.FirstOrDefault(s => s.Type == t);
    public static bool IsModule(FurnitureType t) => Of(t) != null;
    public static string Name(FurnitureType t) => FurnitureTypes.Name(t);
    public static string Note(FurnitureType t) => Of(t)?.Note ?? "";
    public static (ItemKind kind, int count)[] Cost(FurnitureType t) => Of(t)?.Cost ?? Array.Empty<(ItemKind, int)>();

    /// <summary>그 방(없으면 온 배)에서 일하고 있는 모듈 수.</summary>
    public static int Working(World w, FurnitureType t, Room? room = null) =>
        w.Ship.FurnitureOf(t).Count(f => (room == null || f.Room == room) && !f.Room.Abandoned && f.Machine is Machine m && m.Faults.Count == 0 && (m.Powered || m.Spec.PowerDraw <= 0f));

    /// <summary>모듈 보너스 합 (Bonus × 일하는 수).</summary>
    public static float Bonus(World w, FurnitureType t, Room? room = null) => (Of(t)?.Bonus ?? 0f) * Working(w, t, room);

    /// <summary>모듈 자리: 그 방의 빈 칸 중 길을 막지 않는 곳.</summary>
    public static Cell? Spot(World w, Room room)
    {
        foreach (var c in Adaptation.CotCells(w, room))
            if (Adaptation.SafeToBlock(w, room, c)) return c;
        return null;
    }

    /// <summary>개조 후보: 이 방이 모자랐던 일이 있거나 연구가 쌓였으면.</summary>
    public static IEnumerable<UpgradePlan> Candidates(World w)
    {
        var h = w.History;
        foreach (var spec in All)
        foreach (var room in w.Ship.RoomsOf(spec.Room))
        {
            if (room.Abandoned || room.Leaking || room.OffLimits) continue;
            int have = room.Furniture.Count(f => f.Type == spec.Type);
            if (have >= spec.Max) continue;
            var (need, why) = Need(w, spec.Type);
            if (need <= 0f) continue;
            if (Spot(w, room) is not Cell at) continue;
            float score = 0.25f + need - 0.15f * have;
            yield return new UpgradePlan(UpgradeKind.Module, WorkTarget.AtCell(at, room), score, Skill.Mechanics,
                $"{why} → {room.Name}에 {Name(spec.Type)} ({spec.Note} · {string.Join(" + ", spec.Cost.Select(c => $"{ItemKinds.Name(c.kind)} {c.count}"))})",
                spec.Cost, 0.3f, (int)spec.Type);
        }
    }

    private static (float need, string why) Need(World w, FurnitureType t)
    {
        var h = w.History;
        return t switch
        {
            FurnitureType.LedPanel => Evolution.StarvedHours(w) > 6f ? (0.5f, $"굶주린 {Evolution.StarvedHours(w):0}사람·시간") : w.Research >= 15f ? (0.15f, $"연구 {w.Research:0}점") : (0f, ""),
            FurnitureType.HeatExchanger => h.Scrams > 0 ? (0.45f, $"원자로가 {ShipHistory.Times(h.Scrams)} 섰다") : w.Power.Demand > 0.85f * w.Piping.CoolingKw ? (0.3f, "냉각이 빠듯하다") : (0f, ""),
            FurnitureType.CapacitorBank => h.DarkHours > 1f ? (0.4f, $"캄캄했던 {h.DarkHours:0}시간") : w.Power.Brownouts > 0 ? (0.3f, "저출력 운영을 겪었다") : (0f, ""),
            FurnitureType.Scrubber => w.Air.Reserve < 0.8f * w.Air.ReserveCapacity ? (0.35f, "공기 탱크가 줄었다") : w.Research >= 30f ? (0.1f, $"연구 {w.Research:0}점") : (0f, ""),
            FurnitureType.Fabricator => w.Research >= 10f ? (0.2f, $"연구 {w.Research:0}점") : (0f, ""),
            FurnitureType.PartTestBench => w.Parts.Stats.EarlyFailures + w.Procs.Defects >= 2 ? (0.45f, $"부품이 일찍 나가거나 재조립 불량이 {w.Parts.Stats.EarlyFailures + w.Procs.Defects}번")
                : w.Parts.Stats.ByOrigin[(int)PartOrigin.Salvage] + w.Parts.Stats.ByOrigin[(int)PartOrigin.Handmade] >= 3 ? (0.3f, "시험 안 한 중고·손으로 만든 부품을 달았다") : (0f, ""),
            FurnitureType.Hoist => w.Parts.Stats.Strains > 0 ? (0.5f, $"무거운 부품을 들다 허리를 다친 일 {w.Parts.Stats.Strains}번") : w.Parts.Stats.HeavyLifts >= 3 ? (0.3f, $"무거운 부품을 맨손으로 {w.Parts.Stats.HeavyLifts}번") : (0f, ""),
            FurnitureType.MaintCart => w.Parts.Stats.FarRepairs >= 8 ? (0.25f, $"정비실에서 먼 수리 {w.Parts.Stats.FarRepairs}번") : (0f, ""),
            _ => (0f, ""),
        };
    }

    public static bool Install(World w, Room room, FurnitureType t, CrewMember cm, Cell preferred)
    {
        var cell = w.Ship.IsOpenFloor(preferred) && Adaptation.SafeToBlock(w, room, preferred) ? preferred : Spot(w, room);
        if (cell is not Cell at) return false;
        var f = w.Ship.AddFurniture(t, at);
        f.Machine!.Wear = 0f;
        f.Machine.Condition = 1f;
        MarkLog.Add(f.Machine.Marks, w.Tick, $"{cm.Name}: 달았다");
        MarkLog.Add(room.Marks, w.Tick, $"{Name(t)} 설치");
        w.Paths.Invalidate();
        w.Structure.Touch();
        return true;
    }
}
