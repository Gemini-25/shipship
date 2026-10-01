using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v5 적응: 완벽하게 못 고칠 때 버티는 방법들의 판단 기준.
/// 무엇을 뜯을지, 어디에 임시 침실을 만들지, 임시 배선을 어느 회로에서 끌어올지.
/// 작업 목록(WorkBoard)과 작업 계획(WorkPlanners)이 같이 쓴다.
/// </summary>
public static class Adaptation
{
    /// <summary>
    /// 설비의 "쓸모" 점수. 낮을수록 먼저 뜯긴다.
    /// 전력 우선순위 + 핵심 설비 가산 + 방의 쓸모(포기한 구획은 이미 버린 곳) + 먹고 치료하는 설비는 아낀다.
    /// </summary>
    public static float Worth(Machine m, bool asDonor)
    {
        float v = m.Spec.Priority + (m.Spec.Critical ? 20f : 0f);
        if (m.Body.Room.Abandoned) v -= 10f;
        if (m.Body.Room.OffLimits) v -= 6f; // 사출할 방·잔해: 어차피 버린다
        if (asDonor && m.Stopped) v -= 3f; // 어차피 멈춰 있는 설비
        v += m.Body.Type switch
        {
            FurnitureType.Stove or FurnitureType.Fridge or FurnitureType.MealDispenser or FurnitureType.GrowBed => 4f,
            FurnitureType.Workbench => 9f,     // 소모품·연료를 만드는 곳: 뜯으면 다시는 못 만든다
            FurnitureType.AuxGenerator => 12f, // 암흑 우주선을 벗어나는 길
            FurnitureType.Battery => 4f,
            FurnitureType.WaterRecycler => 18f, // 물 → 작물 → 식량: 뜯으면 열흘 뒤 굶는다
            FurnitureType.Collector or FurnitureType.Refinery => 10f, // 재료가 들어오는 길
            FurnitureType.MedBed => 2f,
            _ => 0f,
        };
        return v;
    }

    /// <summary>
    /// 그 부품을 뜯어낼 설비. 받는 쪽보다 쓸모가 낮아야 하고, 핵심 설비·이미 뜯긴 설비·쓰는 중인 설비는 제외.
    /// </summary>
    public static Machine? BestDonor(World w, ItemKind part, float recipientWorth, ICollection<Machine> exclude, bool anything = false)
    {
        Machine? best = null;
        float bestWorth = float.MaxValue;
        if (anything) recipientWorth *= 1.5f; // v13.2 방침(부품 뜯기: 필요하면 무엇이든)
        foreach (var m in w.Ship.Machines)
        {
            if (m.Spec.Critical || exclude.Contains(m) || m.Has(FaultKind.Stripped)) continue;
            if (m.Body.Type == FurnitureType.WaterRecycler && !anything) continue; // 뜯으면 열흘 뒤 물 → 작물 → 식량이 무너진다 (60일 시험에서 확인)
            if (!Faults.SalvageOf(m.Body.Type).Contains(part)) continue;
            if (m.Body.ReservedBy != null) continue; // 누가 쓰고 있다 (치료 침대에 환자 등)
            if (w.Fire.CountIn(m.Body.Room) > 0) continue;
            float worth = Worth(m, asDonor: true);
            if (worth >= recipientWorth) continue;
            if (worth < bestWorth) { bestWorth = worth; best = m; }
        }
        return best;
    }

    /// <summary>소모품이 이만큼 아래로 떨어지면 핵심 설비에만 쓰고, 나머지는 임시 정비로 버틴다 (배급).</summary>
    public const int RationBelow = 4;

    public static bool Rationed(World w, Machine m) =>
        m.Spec.ServiceItem is ItemKind k && !m.Spec.Critical && w.Ship.CountStored(k) < RationBelow;

    /// <summary>원래 침대가 있는 방을 포기한 지 오래돼서 잘 곳이 없는 사람들.</summary>
    public static List<CrewMember> Homeless(World w, float hours = 6f) =>
        w.Crew.Where(c => !c.Dead && c.Bed != null && (c.Bed.Room.Abandoned || c.Bed.Room.OffLimits)
            && w.Tick - (c.Bed.Room.Jettison?.Started ?? c.Bed.Room.AbandonedSince) > SimTime.Hours(hours)).ToList();

    /// <summary>간이침대를 놓을 수 있는 칸: 빈 바닥, 문 옆이 아니고, 다른 가구의 사용 자리도 아닌 곳.</summary>
    /// <summary>v10.1: 가구를 놓아도 되는 빈 칸 (빈 바닥, 문 옆이 아니고, 다른 가구의 사용 자리·댐퍼 자리도 아닌 곳).</summary>
    public static IEnumerable<Cell> FreeCells(World w, Room room)
    {
        var ship = w.Ship;
        var used = new HashSet<Cell>(room.Furniture.SelectMany(f => f.UseSpots)) { room.DamperSpot };
        foreach (var cell in room.Cells)
        {
            if (!ship.IsOpenFloor(cell) || used.Contains(cell)) continue;
            if (Cell.Dirs4.Any(d => ship.Grid.Kind(cell + d) == TileKind.Door)) continue;
            yield return cell;
        }
    }

    public static List<Cell> CotCells(World w, Room room)
    {
        var ship = w.Ship;
        var used = new HashSet<Cell>(room.Furniture.SelectMany(f => f.UseSpots));
        used.Add(room.DamperSpot);
        var cells = new List<Cell>();
        foreach (var cell in room.Cells)
        {
            if (!ship.IsOpenFloor(cell) || used.Contains(cell)) continue;
            bool byDoor = false;
            foreach (var d in Cell.Dirs4)
                if (ship.Grid.Kind(cell + d) == TileKind.Door) byDoor = true;
            if (byDoor) continue;
            // 간이침대끼리 붙이지 않는다 (지나다닐 틈)
            if (cells.Any(o => Math.Abs(o.X - cell.X) + Math.Abs(o.Y - cell.Y) <= 1)) continue;
            if (room.Furniture.Any(f => f.Type == FurnitureType.Cot && f.UseSpots.Any(o => Math.Abs(o.X - cell.X) + Math.Abs(o.Y - cell.Y) <= 1))) continue;
            cells.Add(cell);
        }
        return cells;
    }

    /// <summary>
    /// 그 칸에 가구를 놓아도 방 안 길이 끊기지 않는지: 문 앞에서 출발해 방 안의 모든 가구 사용 자리(와 댐퍼 조작 자리)에
    /// 여전히 닿을 수 있어야 한다 (원래 닿던 곳만 따진다).
    /// </summary>
    public static bool SafeToBlock(World w, Room room, Cell blocked) => SafeToBlock(w, room, new[] { blocked });

    /// <summary>v10.1: 여러 칸을 한꺼번에 막아도 되는지 (두 칸짜리 재배대).</summary>
    public static bool SafeToBlock(World w, Room room, IReadOnlyCollection<Cell> blocked)
    {
        var ship = w.Ship;
        var skips = new HashSet<Cell>(blocked);
        HashSet<Cell> Reach(bool skipping)
        {
            var seen = new HashSet<Cell>();
            var stack = new Stack<Cell>();
            foreach (var d in room.Doors)
            foreach (var n in Cell.Dirs4)
            {
                var c = d.Cell + n;
                if (ship.RoomAt(c) == room && ship.IsWalkable(c) && !(skipping && skips.Contains(c)) && seen.Add(c)) stack.Push(c);
            }
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                foreach (var n in Cell.Dirs4)
                {
                    var x = c + n;
                    if ((skipping && skips.Contains(x)) || ship.RoomAt(x) != room || !ship.IsWalkable(x) || !seen.Add(x)) continue;
                    stack.Push(x);
                }
            }
            return seen;
        }
        var before = Reach(false);
        var after = Reach(true);
        var needed = room.Furniture.SelectMany(f => f.UseSpots).Append(room.DamperSpot).Where(before.Contains);
        return needed.All(c => !skips.Contains(c) && after.Contains(c));
    }

    private static readonly RoomType[] ShopCandidates = { RoomType.Storage, RoomType.Mess, RoomType.Lounge, RoomType.Medbay, RoomType.Bridge };

    /// <summary>임시 작업대를 둘 수 있는 칸: 간이침대 자리 조건 + 옆에 서서 일할 빈 바닥이 있어야 한다.</summary>
    public static Cell? BenchCell(World w, Room room)
    {
        foreach (var cell in CotCells(w, room))
            if (Cell.Dirs4.Any(d => w.Ship.IsOpenFloor(cell + d) && w.Ship.RoomAt(cell + d) == room)) return cell;
        return null;
    }

    /// <summary>임시 정비실로 쓸 방: 창고 → 식당 → 휴게실 → 의무실 → 함교 (안전하고 자리가 되는 곳).</summary>
    public static Room? WorkshopTarget(World w)
    {
        foreach (var type in ShopCandidates)
        foreach (var room in w.Ship.RoomsOf(type))
        {
            if (room.Abandoned || room.OffLimits || room.Leaking || Atmosphere.Danger(room) > 0.1f || w.Fire.CountIn(room) > 0) continue;
            if (BenchCell(w, room) != null) return room;
        }
        return null;
    }

    private static readonly RoomType[] ComputerCandidates = { RoomType.Power, RoomType.Workshop, RoomType.Storage, RoomType.Lounge, RoomType.Mess, RoomType.Medbay };

    /// <summary>v9.2 임시 제어 컴퓨터를 둘 방: 배전실 → 정비실 → 창고 … (전기가 있고, 환기되고, 자리가 되는 곳).</summary>
    public static Room? ComputerTarget(World w)
    {
        foreach (var type in ComputerCandidates)
        foreach (var room in w.Ship.RoomsOf(type))
        {
            if (room.Detached || room.Abandoned || room.OffLimits || room.Leaking || !room.Powered || Atmosphere.Danger(room) > 0.1f || w.Fire.CountIn(room) > 0) continue;
            if (BenchCell(w, room) != null) return room;
        }
        return null;
    }

    private static readonly RoomType[] DormCandidates = { RoomType.Lounge, RoomType.Mess, RoomType.Medbay, RoomType.Bridge, RoomType.Workshop };

    /// <summary>임시 침실로 쓸 방: 휴게실 → 식당 → 의무실 순으로, 안전하고 자리가 되는 곳.</summary>
    public static Room? DormTarget(World w, int beds)
    {
        foreach (var type in DormCandidates)
        foreach (var room in w.Ship.RoomsOf(type))
        {
            if (room.Abandoned || room.OffLimits || room.Leaking || Atmosphere.Danger(room) > 0.1f || w.Fire.CountIn(room) > 0) continue;
            if (CotCells(w, room).Count >= Math.Min(beds, 2)) return room;
        }
        return null;
    }
}
