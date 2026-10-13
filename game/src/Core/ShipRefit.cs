using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 설비 보충 (함장 승인): 사람 수에 비해 모자랐던 설비를 떠나기 전에 더 단다 (평시 굶주림 · 물 부족 진단에서 나온 숫자).
//   고래호 정수기 2 → 5 · 한울호 정수기 3 → 5 · 재배대 17 → 24 · 보름달호 재배대 9 → 14 · 배식기 3 → 5 (배식기에서 먼 쪽) · 보듬호 배터리 4 → 6.
//   같은 종류가 이미 있는 방의 빈 바닥부터 (통로를 막지 않고, 옆에 설 자리가 있는 곳). 놓을 자리가 없으면 그만큼 덜 단다.
public static class ShipRefit
{
    public static bool On = true;

    private static readonly Dictionary<string, (FurnitureType type, int target)[]> Plan = new()
    {
        ["Gorae"] = new[] { (FurnitureType.WaterRecycler, 5) },
        ["Hanul"] = new[] { (FurnitureType.WaterRecycler, 5), (FurnitureType.GrowBed, 24) },
        ["Boreumdal"] = new[] { (FurnitureType.GrowBed, 14), (FurnitureType.MealDispenser, 5) },
        ["Bodeum"] = new[] { (FurnitureType.Battery, 6) },
    };

    /// <summary>배를 띄울 때 한 번. 더 단 설비 수.</summary>
    public static int Apply(World w, string key)
    {
        if (!On || !Plan.TryGetValue(key, out var plan)) return 0;
        int added = 0;
        foreach (var (type, target) in plan)
        {
            int have = w.Ship.FurnitureOf(type).Count();
            for (int i = have; i < target; i++)
            {
                if (Place(w, type) is not Furniture f) break;
                if (f.Machine is Machine m) { m.Condition = 1f; m.Wear = 0f; m.Tier = Math.Max(m.Tier, Tech.Unlocked(w, type)); if (m.Crop != null) m.Crop.Care = 0.8f; MarkLog.Add(m.Marks, w.Tick, "떠나기 전 보충한 설비"); }
                added++;
            }
        }
        if (added > 0) w.Paths.Invalidate();
        return added;
    }

    private static Furniture? Place(World w, FurnitureType type)
    {
        var ship = w.Ship;
        var sample = ship.FurnitureOf(type).FirstOrDefault();
        int size = Math.Clamp(sample?.Cells.Count ?? 1, 1, 2);
        var existing = ship.FurnitureOf(type).ToList();
        IEnumerable<Room> rooms = type switch
        {
            // 배식기는 이미 있는 배식기에서 먼 방부터 (먹으러 배를 가로지르지 않게)
            FurnitureType.MealDispenser => ship.LiveRooms
                .Where(r => r.Type is RoomType.Mess or RoomType.Lounge or RoomType.Galley or RoomType.Quarters)
                .OrderByDescending(r => existing.Count == 0 ? 0f : existing.Min(d => (d.Center - r.Center).LengthSquared()))
                .ThenBy(r => r.Id),
            _ => existing.Select(f => f.Room).Distinct().OrderBy(r => r.Id)
                .Concat(PreferredRooms(type).SelectMany(t => ship.RoomsOf(t)).OrderBy(r => r.Id))
                .Distinct(),
        };
        foreach (var room in rooms)
        {
            if (room.Abandoned || room.Detached || room.Leaking || room.OffLimits) continue;
            var free = new HashSet<Cell>(Adaptation.FreeCells(w, room));
            foreach (var a in free.OrderBy(c => c.Y).ThenBy(c => c.X))
            {
                foreach (var d in size == 1 ? new[] { new Cell(0, 0) } : new[] { new Cell(1, 0), new Cell(0, 1) })
                {
                    var cells = size == 1 ? new[] { a } : new[] { a, a + d };
                    if (cells.Any(c => !free.Contains(c))) continue;
                    if (!Adaptation.SafeToBlock(w, room, cells)) continue;
                    if (!cells.SelectMany(c => Cell.Dirs4.Select(n => c + n)).Any(n => !cells.Contains(n) && ship.IsOpenFloor(n) && ship.RoomAt(n) == room)) continue;
                    return size == 1 ? ship.AddFurniture(type, a) : ship.AddFurniture(type, cells);
                }
            }
        }
        return null;
    }

    private static RoomType[] PreferredRooms(FurnitureType type) => type switch
    {
        FurnitureType.WaterRecycler => new[] { RoomType.LifeSupport, RoomType.Storage },
        FurnitureType.GrowBed => new[] { RoomType.Hydroponics, RoomType.Storage, RoomType.Lounge },
        FurnitureType.Battery => new[] { RoomType.Power, RoomType.Reactor, RoomType.LifeSupport, RoomType.Storage },
        _ => new[] { RoomType.Storage },
    };
}
