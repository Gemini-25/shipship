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
        ship.Rooms.Add(b);
        foreach (var c in p.SideB)
        {
            a.Cells.Remove(c);
            ship.Grid.SetRoomId(c, b.Id);
            b.Include(c);
        }
        a.RecomputeBounds();
        b.Air.O2 = a.Air.O2; b.Air.N2 = a.Air.N2; b.Air.CO2 = a.Air.CO2; b.Air.Temperature = a.Air.Temperature; b.Air.Smoke = a.Air.Smoke;
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
