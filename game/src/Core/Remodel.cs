using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 방 크기 개조 (v10.2). 큰 방 한가운데에 칸막이 벽과 격벽 문을 세워 방 하나를 둘로 나눈다.
/// 왜: 큰 방은 운석 하나에 통째로 감압된다 (공기 탱크를 크게 먹고, 방 전체를 잃는다). 칸막이가 있으면 다음엔 절반만 잃는다.
/// 대가: 문이 하나 늘어 오가는 길이 느려지고, 넓은 방에서 모이던 사람들이 흩어진다 (식당·휴게실).
/// 설계도가 바뀐다 — 격자(벽·문), 방 목록, 가구·문·연결부의 방, 공기, 댐퍼 자리, 길찾기, 사람들의 기억.
/// </summary>
public static class Remodel
{
    /// <summary>나눌 계획: 칸막이 벽 칸들, 그 가운데 문, 새 방으로 떼어 낼 쪽.</summary>
    public sealed record SplitPlan(Room Room, List<Cell> Wall, Cell Door, bool Vertical, HashSet<Cell> SideB);

    public const int MinRoomCells = 30;
    public const int MinSideCells = 12;

    /// <summary>이 방을 나눌 수 있는 칸막이 줄 (방 가운데에 가까운 것부터). 없으면 null.</summary>
    public static SplitPlan? FindSplit(World w, Room room)
    {
        var ship = w.Ship;
        if (room.Type is RoomType.Corridor or RoomType.Airlock || room.Detached || room.Abandoned || room.OffLimits || room.Docked
            || room.Leaking || room.Lockdown || room.Cells.Count < MinRoomCells) return null;
        var cells = new HashSet<Cell>(room.Cells);
        var candidates = new List<(bool vertical, int at, float score)>();
        for (int x = room.MinX + 3; x <= room.MaxX - 3; x++) candidates.Add((true, x, MathF.Abs(x + 0.5f - room.Center.X)));
        for (int y = room.MinY + 3; y <= room.MaxY - 3; y++) candidates.Add((false, y, MathF.Abs(y + 0.5f - room.Center.Y) + 0.25f));
        foreach (var (vertical, at, _) in candidates.OrderBy(c => c.score))
        {
            var line = cells.Where(c => vertical ? c.X == at : c.Y == at).OrderBy(c => vertical ? c.Y : c.X).ToList();
            if (line.Count < 3) continue;
            // 벽에서 벽까지 곧게 이어져야 한다 (끝이 문이면 문이 벽에 막힌다)
            bool straight = true;
            for (int i = 1; i < line.Count; i++)
                if ((vertical ? line[i].Y - line[i - 1].Y : line[i].X - line[i - 1].X) != 1) straight = false;
            if (!straight) continue;
            var along = vertical ? new Cell(0, 1) : new Cell(1, 0);
            var across = vertical ? new Cell(1, 0) : new Cell(0, 1);
            if (ship.Grid.Kind(line[0] - along) != TileKind.Wall || ship.Grid.Kind(line[^1] + along) != TileKind.Wall) continue;
            // 줄 위에는 가구·사람·불이 없어야 한다
            if (line.Any(c => !ship.IsOpenFloor(c) || w.Fire.AnyWithin(c, 0.6f))) continue;
            // 두 쪽: 각각 이어진 한 덩어리, 충분히 넓게
            var sideA = cells.Where(c => vertical ? c.X < at : c.Y < at).ToHashSet();
            var sideB = cells.Where(c => vertical ? c.X > at : c.Y > at).ToHashSet();
            if (sideA.Count < MinSideCells || sideB.Count < MinSideCells || !Connected(sideA) || !Connected(sideB)) continue;
            // 문: 양옆이 빈 바닥인 칸 중 가운데에 가까운 곳
            Cell? door = null;
            foreach (var c in line.OrderBy(c => MathF.Abs((vertical ? c.Y : c.X) + 0.5f - (vertical ? room.Center.Y : room.Center.X))))
                if (ship.IsOpenFloor(c + across) && ship.IsOpenFloor(c - across)) { door = c; break; }
            if (door is not Cell dc) continue;
            // 가구마다 쓸 자리가 하나는 남아야 한다
            var lineSet = line.ToHashSet();
            if (room.Furniture.Any(f => f.UseSpots.Count > 0 && f.UseSpots.All(lineSet.Contains))) continue;
            // 연결부는 양쪽에 하나 이상 (각 칸이 선체에 따로 붙어 있어야 한다)
            int ja = 0, jb = 0;
            foreach (var j in room.Joints.Where(j => !j.Released))
                if (JointSide(j, sideA, sideB, ja, jb)) jb++; else ja++;
            if (ja == 0 || jb == 0) continue;
            // 떼어 낼 쪽: 원래 문(통로 쪽)이 없는 쪽을 새 방으로 (없으면 작은 쪽)
            bool aHasDoor = room.Doors.Any(d => !d.IsExternal && Cell.Dirs4.Any(n => sideA.Contains(d.Cell + n)));
            bool bHasDoor = room.Doors.Any(d => !d.IsExternal && Cell.Dirs4.Any(n => sideB.Contains(d.Cell + n)));
            var split = aHasDoor && !bHasDoor ? sideB : bHasDoor && !aHasDoor ? sideA : sideB.Count <= sideA.Count ? sideB : sideA;
            return new SplitPlan(room, line, dc, vertical, split);
        }
        return null;
    }

    /// <summary>연결부가 어느 쪽 외벽에 박혔나 (true = B). 칸막이 바로 끝에 박힌 것은 연결부가 적은 쪽으로.</summary>
    private static bool JointSide(Joint j, HashSet<Cell> sideA, HashSet<Cell> sideB, int countA, int countB)
    {
        bool a = Cell.Dirs8.Any(d => sideA.Contains(j.Cell + d));
        bool b = Cell.Dirs8.Any(d => sideB.Contains(j.Cell + d));
        if (a && !b) return false;
        if (b && !a) return true;
        return countB < countA;
    }

    /// <summary>
    /// v10.3 개조의 공통 뒷정리: 떼어 낸 쪽 자리를 가리키던 작업 대상은 새 방으로 (설비·문·연결부는 물건을 따라가므로 저절로),
    /// 그쪽을 향하던 일과 새 벽을 가로지르던 걸음은 내려놓고 다시 생각한다 (칸막이를 세우는 사람 자신은 빼고).
    /// </summary>
    private static void Rehome(World w, Room a, Room b, HashSet<Cell> sideB, HashSet<Cell> line, CrewMember? builder)
    {
        var sideA = a.Cells.ToHashSet();
        foreach (var o in w.Board.All)
        {
            var t = o.Target;
            if (t.Furniture != null || t.Joint != null || t.Door != null || t.Crew != null || t.Kind == TargetKind.Room) continue;
            if (t.Room != a) continue;
            bool inB = sideB.Contains(t.Cell)
                       || (!sideA.Contains(t.Cell) && Cell.Dirs8.Any(d => sideB.Contains(t.Cell + d)) && !Cell.Dirs8.Any(d => sideA.Contains(t.Cell + d)));
            if (inB) t.Rehome(b);
        }
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Job == null || c == builder) continue;
            bool crosses = c.Path != null && c.Path.Skip(c.PathIndex).Any(line.Contains);
            bool aimed = c.Job.TargetRoom == a || c.Job.Order?.Target.Room == b;
            if (!crosses && !aimed) continue;
            c.EndJob(w, ToilStatus.Interrupted);
            c.NextThinkTick = w.Tick;
        }
    }

    private static bool Connected(HashSet<Cell> set)
    {
        if (set.Count == 0) return false;
        var seen = new HashSet<Cell>();
        var stack = new Stack<Cell>();
        var first = set.First();
        stack.Push(first);
        seen.Add(first);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            foreach (var d in Cell.Dirs4)
            {
                var n = c + d;
                if (set.Contains(n) && seen.Add(n)) stack.Push(n);
            }
        }
        return seen.Count == set.Count;
    }

    /// <summary>칸막이를 세운다. 새로 생긴 방을 돌려준다 (사람이 줄 위에 서 있으면 null — 비키면 다시).</summary>
    public static Room? Apply(World w, SplitPlan p, CrewMember? builder = null)
    {
        var ship = w.Ship;
        var a = p.Room;
        var line = p.Wall.ToHashSet();
        if (line.Any(c => !ship.IsOpenFloor(c) || w.Fire.AnyWithin(c, 0.6f))) return null;
        // 줄 위에 서 있던 사람은 한 칸 비켜선다
        foreach (var c in w.Crew)
        {
            if (c.Dead || !line.Contains(c.Cell)) continue;
            var to = a.Cells.Where(x => !line.Contains(x) && ship.IsOpenFloor(x)).OrderBy(x => (x.Center - c.Position).LengthSquared()).FirstOrDefault();
            c.Position = to.Center;
            c.PreviousPosition = c.Position;
            c.Interrupt(w);
        }

        // 1) 격자: 벽과 문
        foreach (var c in p.Wall)
        {
            a.Cells.Remove(c);
            ship.Grid.SetRoomId(c, -1);
            if (c == p.Door) ship.Grid.SetKind(c, TileKind.Door);
            else
            {
                ship.Grid.SetKind(c, TileKind.Wall);
                ship.AddWall(c, new WallState { IsHull = false });
            }
        }

        // 2) 새 방: 떼어 낸 쪽의 칸, 공기, 전기, 용도
        var b = new Room { Id = ship.Rooms.Count, Type = a.Type, NameOverride = $"{a.Name} 안쪽 칸", Partitioned = true };
        a.Partitioned = true;
        // v10.12: 걷을 때를 위해 기억해 둔다
        b.SplitFrom = a;
        b.SplitWall.AddRange(p.Wall.Where(c => c != p.Door));
        b.SplitSince = w.Tick;
        b.SplitBreaches = w.History.BreachesByRoom.GetValueOrDefault(a.Id);
        ship.Rooms.Add(b);
        foreach (var c in p.SideB)
        {
            a.Cells.Remove(c);
            ship.Grid.SetRoomId(c, b.Id);
            b.Include(c);
        }
        a.RecomputeBounds();
        b.Air.O2 = a.Air.O2; b.Air.N2 = a.Air.N2; b.Air.CO2 = a.Air.CO2; b.Air.Temperature = a.Air.Temperature; b.Air.Smoke = a.Air.Smoke; b.Air.Toxin = a.Air.Toxin;
        b.Circuit = a.Circuit;
        b.Powered = a.Powered;
        b.VentOpen = a.VentOpen;
        b.LightsOut = a.LightsOut;
        b.LightsOutSince = a.LightsOutSince;
        b.Purpose = a.Purpose;
        b.Suppression = a.Suppression;

        // 3) 가구: 떼어 낸 쪽에 놓인 것은 새 방으로, 줄 위의 사용 자리는 지운다
        foreach (var f in a.Furniture.ToList())
        {
            f.UseSpots.RemoveAll(line.Contains);
            if (!f.Cells.Any(p.SideB.Contains)) continue;
            a.Furniture.Remove(f);
            b.Furniture.Add(f);
            f.Room = b;
        }

        // 4) 문: 떼어 낸 쪽에 붙은 문은 새 방의 문으로, 칸막이 문을 새로 단다
        foreach (var d in a.Doors.ToList())
        {
            if (!Cell.Dirs4.Any(n => p.SideB.Contains(d.Cell + n))) continue;
            a.Doors.Remove(d);
            b.Doors.Add(d);
            if (d.RoomA == a) d.RoomA = b;
            if (d.RoomB == a) d.RoomB = b;
        }
        var first = p.Vertical ? p.Door + new Cell(-1, 0) : p.Door + new Cell(0, -1);
        var second = p.Vertical ? p.Door + new Cell(1, 0) : p.Door + new Cell(0, 1);
        var door = new Door { Id = ship.Doors.Count, Cell = p.Door, ConnectsVertically = !p.Vertical, IsExternal = false };
        door.RoomA = ship.RoomAt(first);
        door.RoomB = ship.RoomAt(second);
        a.Doors.Add(door);
        b.Doors.Add(door);
        ship.AddDoor(door);
        b.SplitDoor = door;

        // 5) 연결부: 떼어 낸 쪽 외벽에 박힌 것은 새 방의 것 (설계 연결부 수도 나눈다)
        var sideA = a.Cells.ToHashSet();
        int ka = 0, kb = 0;
        foreach (var j in a.Joints.ToList())
        {
            bool toB = JointSide(j, sideA, p.SideB, ka, kb);
            if (!toB) { ka++; continue; }
            kb++;
            a.Joints.Remove(j);
            b.Joints.Add(j);
            j.Room = b;
        }
        a.DesignJoints = Math.Max(1, a.Joints.Count(j => !j.Truss));
        b.DesignJoints = Math.Max(1, b.Joints.Count(j => !j.Truss));

        // 6) 배관 밸브가 떼어 낸 쪽에 있으면 그 방의 것
        foreach (var seg in w.Piping.Segments)
            if (seg.ValveRoom == a && p.SideB.Contains(seg.ValveCell)) seg.ValveRoom = b;

        // 7) 댐퍼 자리, 사람들의 기억(무서운 방은 두 칸 다 무섭다), 길찾기, 화면
        ShipBuilder.AssignDamperSpot(ship, a);
        ShipBuilder.AssignDamperSpot(ship, b);
        foreach (var c in w.Crew)
        {
            c.Memory.GrowRooms(ship.Rooms.Count);
            c.Memory.Fear[b.Id] = c.Memory.Fear[a.Id];
            c.Memory.FearCause[b.Id] = c.Memory.FearCause[a.Id];
            c.Memory.FearNoted[b.Id] = c.Memory.FearNoted[a.Id];
            if (c.Room == a && p.SideB.Contains(c.Cell)) c.Room = b;
        }
        Rehome(w, a, b, p.SideB, line, builder);
        w.Paths.Invalidate();
        w.Structure.Touch();
        w.Board.RequestScan();
        return b;
    }
}

/// <summary>
/// v10.12 공간 개조 (되돌리기와 옮기기).
/// 칸막이 철거: 칸막이로 떼어 냈던 칸을 원래 방에 도로 합친다 — 벽·문이 바닥이 되고, 칸의 가구·문·연결부·밸브·공기·기억이 원래 방으로.
///   떼어 낸 칸은 번호가 바뀌지 않게 목록에 빈 칸으로 남고(Merged → Detached), 모든 계통이 건너뛴다.
/// 설비 옮기기: 여러 번 뚫리거나 버렸던 외벽 방의 설비를 외벽이 없는 안쪽 방 빈 자리로 (설비의 이력·마모·단계는 그대로 따라간다).
/// </summary>
public static class Remodel2
{
    /// <summary>이 칸의 칸막이를 걷을 수 있나.</summary>
    public static bool CanMerge(World w, Room b)
    {
        if (b.Merged || b.SplitFrom is not Room a || a.Merged || b.SplitDoor is not Door d || d.Removed) return false;
        foreach (var r in new[] { a, b })
            if (r.Detached || r.Abandoned || r.OffLimits || r.Leaking || r.Lockdown || w.Fire.CountIn(r) > 0 || r.Air.Toxin > 0.1f) return false;
        // 두 칸의 기압이 비슷해야 (한쪽이 진공이면 벽을 트는 순간 빨려 나간다)
        if (MathF.Abs(a.Air.Pressure - b.Air.Pressure) > 15f) return false;
        return b.SplitWall.All(c => w.Ship.WallAt(c) is WallState ws && !ws.IsHull);
    }

    /// <summary>칸막이를 걷는다: b를 원래 방에 합친다. 합친 방을 돌려준다.</summary>
    public static Room? Merge(World w, Room b, CrewMember? builder = null)
    {
        if (!CanMerge(w, b)) return null;
        var a = b.SplitFrom!;
        var ship = w.Ship;
        var grid = ship.Grid;
        float va = a.Volume, vb = b.Volume;

        // 1) 벽과 문 → 원래 방의 바닥
        var opened = new List<Cell>();
        foreach (var c in b.SplitWall)
        {
            ship.RemoveWall(c);
            grid.SetKind(c, TileKind.Floor);
            grid.SetRoomId(c, a.Id);
            a.Include(c);
            opened.Add(c);
        }
        var door = b.SplitDoor!;
        ship.UnmapDoor(door);
        door.Removed = true;
        door.Openness = 0f;
        door.Locked = false;
        grid.SetKind(door.Cell, TileKind.Floor);
        grid.SetRoomId(door.Cell, a.Id);
        a.Include(door.Cell);
        opened.Add(door.Cell);
        a.Doors.Remove(door);
        b.Doors.Remove(door);

        // 2) 칸 → 원래 방 (공기는 부피대로 섞는다)
        foreach (var c in b.Cells)
        {
            grid.SetRoomId(c, a.Id);
            a.Include(c);
        }
        float total = MathF.Max(1f, va + vb);
        a.Air.O2 = (a.Air.O2 * va + b.Air.O2 * vb) / total;
        a.Air.N2 = (a.Air.N2 * va + b.Air.N2 * vb) / total;
        a.Air.CO2 = (a.Air.CO2 * va + b.Air.CO2 * vb) / total;
        a.Air.Temperature = (a.Air.Temperature * va + b.Air.Temperature * vb) / total;
        a.Air.Smoke = (a.Air.Smoke * va + b.Air.Smoke * vb) / total;
        a.Air.Toxin = (a.Air.Toxin * va + b.Air.Toxin * vb) / total;
        b.Cells.Clear();
        b.Air.O2 = 0f; b.Air.N2 = 0f; b.Air.CO2 = 0f; b.Air.Smoke = 0f; b.Air.Toxin = 0f; b.Air.Leak = 0f;
        b.VentOpen = false;
        b.Powered = false;

        // 3) 가구: 칸의 것은 원래 방으로, 새로 트인 바닥은 옆 가구의 사용 자리가 된다
        foreach (var f in b.Furniture.ToList())
        {
            f.Room = a;
            a.Furniture.Add(f);
        }
        b.Furniture.Clear();
        foreach (var f in a.Furniture)
        {
            if (f.Stowed || FurnitureTypes.Walkable(f.Type)) continue;
            foreach (var c in f.Cells)
                foreach (var dd in Cell.Dirs4)
                {
                    var n = c + dd;
                    if (opened.Contains(n) && ship.IsOpenFloor(n) && !f.UseSpots.Contains(n)) f.UseSpots.Add(n);
                }
        }

        // 4) 칸의 다른 문·연결부·밸브
        foreach (var d in b.Doors.ToList())
        {
            if (d.RoomA == b) d.RoomA = a;
            if (d.RoomB == b) d.RoomB = a;
            if (!a.Doors.Contains(d)) a.Doors.Add(d);
        }
        b.Doors.Clear();
        foreach (var j in b.Joints.ToList())
        {
            j.Room = a;
            a.Joints.Add(j);
        }
        b.Joints.Clear();
        a.DesignJoints = Math.Max(1, a.Joints.Count(j => !j.Truss));
        foreach (var seg in w.Piping.Segments)
            if (seg.ValveRoom == b) seg.ValveRoom = a;
        foreach (var m in w.Sensors.Incoming)
            if (m.Room == b) m.Room = a;

        // 5) 방의 상태
        a.LightsOut |= b.LightsOut;
        a.Suppression |= b.Suppression;
        if (a.Purpose == null && b.Purpose != null) a.Purpose = b.Purpose;
        b.Merged = true;
        b.Partitioned = false;
        var h = w.History;
        h.BreachesByRoom[a.Id] = h.BreachesByRoom.GetValueOrDefault(a.Id) + h.BreachesByRoom.GetValueOrDefault(b.Id);
        a.UnsplitBreaches = h.BreachesByRoom.GetValueOrDefault(a.Id);
        a.Partitioned = ship.Rooms.Any(r => r.SplitFrom == a && !r.Merged);
        ShipBuilder.AssignDamperSpot(ship, a);

        // 6) 사람: 칸에 있던 사람, 무서운 방의 기억 (합친 방은 두 칸 중 더 무서운 쪽만큼)
        foreach (var c in w.Crew)
        {
            if (c.Room == b) c.Room = a;
            c.Memory.GrowRooms(ship.Rooms.Count);
            if (c.Memory.Fear[b.Id] > c.Memory.Fear[a.Id])
            {
                c.Memory.Fear[a.Id] = c.Memory.Fear[b.Id];
                c.Memory.FearCause[a.Id] = c.Memory.FearCause[b.Id];
            }
            c.Memory.Fear[b.Id] = 0f;
            // 칸막이 문을 지나던 길은 다시 찾는다
            if (c.Job != null && c != builder && c.Path != null && c.Path.Skip(c.PathIndex).Any(opened.Contains))
            {
                c.EndJob(w, ToilStatus.Interrupted);
                c.NextThinkTick = w.Tick;
            }
        }
        foreach (var r in w.Robots.Robots)
            if (r.Room == b) r.Room = a;
        foreach (var o in w.Board.All)
            if (o.Target.Room == b) o.Target.Rehome(a);

        w.Paths.Invalidate();
        w.Structure.Touch();
        w.Board.RequestScan();
        return a;
    }

    // ─────────────────────────────── 설비 옮기기 ───────────────────────────────

    /// <summary>옮길 수 있는 설비 (배관·덕트·굴뚝에 물린 것은 못 옮긴다).</summary>
    public static bool Movable(FurnitureType t) => t is FurnitureType.OxygenGenerator or FurnitureType.Battery or FurnitureType.Workbench
        or FurnitureType.MedBed or FurnitureType.Fabricator or FurnitureType.Refinery or FurnitureType.SuitLocker or FurnitureType.Shelf;

    /// <summary>외벽이 없는 방 (운석이 바로 들어오지 않는다).</summary>
    public static bool Interior(World w, Room r) =>
        !w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r);

    /// <summary>그 설비를 놓을 안쪽 방의 빈 자리 (모양 그대로, 길을 막지 않는 곳). 없으면 null.</summary>
    public static (Room room, List<Cell> cells)? FindSpot(World w, Furniture f)
    {
        var ship = w.Ship;
        int wdt = f.Width, hgt = f.Height;
        foreach (var room in ship.Rooms.Where(r => !r.Detached && !r.Abandoned && !r.OffLimits && r != f.Room && r.Type != RoomType.Corridor
                                                   && r.Type is not (RoomType.Airlock or RoomType.Reactor or RoomType.Engine) && Interior(w, r))
                     .OrderBy(r => r.Type == RoomType.Storage ? 0 : r.Type == RoomType.Workshop ? 1 : 2).ThenBy(r => r.Id))
        {
            foreach (var origin in room.Cells.OrderBy(c => c.Y).ThenBy(c => c.X))
            {
                var cells = new List<Cell>();
                for (int dy = 0; dy < hgt; dy++)
                    for (int dx = 0; dx < wdt; dx++) cells.Add(origin + new Cell(dx, dy));
                if (!cells.All(c => ship.RoomAt(c) == room && ship.IsOpenFloor(c) && c != room.DamperSpot)) continue;
                // 벽에 붙고, 둘레에 쓸 자리가 있고, 문 앞·다른 가구의 사용 자리를 막지 않는다
                bool wall = cells.Any(c => Cell.Dirs4.Any(d => ship.Grid.Kind(c + d) == TileKind.Wall));
                if (!wall) continue;
                if (room.Furniture.Any(o => o.UseSpots.Any(cells.Contains))) continue;
                if (room.Doors.Any(d => cells.Any(c => (c - d.Cell).X * (c - d.Cell).X + (c - d.Cell).Y * (c - d.Cell).Y <= 2))) continue;
                if (!cells.Any(c => Cell.Dirs4.Any(d => !cells.Contains(c + d) && ship.IsOpenFloor(c + d) && ship.RoomAt(c + d) == room))) continue;
                if (!cells.All(c => Adaptation.SafeToBlock(w, room, c))) continue;
                return (room, cells);
            }
        }
        return null;
    }

    /// <summary>설비를 그 자리로 옮긴다 (가구 번호·설비 이력은 그대로).</summary>
    public static void Move(World w, Furniture f, Room to, List<Cell> cells)
    {
        var ship = w.Ship;
        var from = f.Room;
        foreach (var c in f.Cells)
            if (ship.Grid.FurnitureId(c) == f.Id) ship.Grid.SetFurnitureId(c, -1);
        from.Furniture.Remove(f);
        f.Cells.Clear();
        f.MinX = f.MinY = int.MaxValue;
        f.MaxX = f.MaxY = int.MinValue;
        f.UseSpots.Clear();
        f.ReservedBy = null;
        f.Room = to;
        to.Furniture.Add(f);
        foreach (var c in cells)
        {
            ship.Grid.SetFurnitureId(c, f.Id);
            f.Include(c);
        }
        f.Cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        var seen = new HashSet<Cell>();
        foreach (var c in f.Cells)
            foreach (var d in Cell.Dirs4)
            {
                var n = c + d;
                if (ship.IsOpenFloor(n) && ship.RoomAt(n) == to && seen.Add(n)) f.UseSpots.Add(n);
            }
        foreach (var o in w.Board.All)
            if (o.Target.Furniture == f) o.Target.Rehome(to);
        w.Paths.Invalidate();
        w.Structure.Touch();
        w.Board.RequestScan();
    }
}
