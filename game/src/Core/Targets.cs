using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

public enum TargetKind
{
    Furniture, Cell, Room, Door, Wall, Crew,
    // v8 선체 밖
    Joint,    // 구조 연결부 (밖에서)
    Outer,    // 외벽 바깥면 (골조 재건)
    Exterior, // 방의 바깥 둘레 (외부 검사)
    Fragment, // 떨어져 나간 조각 (견인)
    Drone,    // 드론 (거치대에서 정비)
    // v9 배관
    Valve,    // 배관 구간의 격리 밸브
    Pipe,     // 배관 구간의 터진 자리
    Radiator, // 선체 밖 방열판 (밖에서)
    // v10.10
    Robot,    // 선내 로봇 (멈춘 자리에서 고치거나 끌어온다)
}

/// <summary>
/// 작업 대상. 설비만이 아니라 불난 칸, 방(환기 댐퍼), 문(격벽), 벽(파공), 사람(부상자)도 대상이 된다.
/// "어디에 서서 일하는지"(Spots)를 대상마다 알고 있어서, 작업 계획은 대상 종류를 몰라도 된다.
/// </summary>
public sealed class WorkTarget
{
    public TargetKind Kind { get; }
    public Furniture? Furniture { get; }
    public Cell Cell { get; }

    /// <summary>
    /// 대상이 있는 방. v10.3: 설비·문·연결부는 그 물건이 지금 속한 방을 따른다 (칸막이로 방이 나뉘면 작업 대상도 새 방을 본다).
    /// 칸·벽 같은 자리는 개조 때 <see cref="Rehome"/>로 옮긴다.
    /// </summary>
    public Room? Room => Robot != null ? Robot.Room ?? _room : Furniture?.Room ?? Joint?.Room ?? (Door != null ? Door.RoomA ?? Door.RoomB : null) ?? _room;
    private Room? _room;

    /// <summary>v10.3: 개조로 이 자리가 다른 방이 됐다.</summary>
    internal void Rehome(Room room) => _room = room;

    public Door? Door { get; }
    public CrewMember? Crew { get; }
    public Joint? Joint { get; private init; }
    public Drone? Drone { get; private init; }
    public PipeSegment? Pipe { get; private init; }
    public Robot? Robot { get; private init; }

    private readonly string? _key;

    private WorkTarget(TargetKind kind, Cell cell, Room? room, Furniture? furniture = null, Door? door = null, CrewMember? crew = null,
        string? key = null)
    {
        _key = key;
        Kind = kind;
        Cell = cell;
        _room = room;
        Furniture = furniture;
        Door = door;
        Crew = crew;
    }

    public static WorkTarget Of(Furniture f) => new(TargetKind.Furniture, f.Cells[0], f.Room, furniture: f);
    public static WorkTarget AtCell(Cell c, Room? room) => new(TargetKind.Cell, c, room);

    /// <summary>방의 불 (열쇠는 방 기준이라 불이 옮겨 다녀도 같은 일로 본다).</summary>
    public static WorkTarget FireIn(Room room, Cell hottest, int slot) => new(TargetKind.Cell, hottest, room, key: $"X{room.Id}.{slot}");
    public static WorkTarget OfWall(Cell wall, Room? inside) => new(TargetKind.Wall, wall, inside);
    public static WorkTarget OfRoom(Room room) => new(TargetKind.Room, room.DamperSpot, room);
    public static WorkTarget OfDoor(Door d) => new(TargetKind.Door, d.Cell, d.RoomA ?? d.RoomB, door: d);
    public static WorkTarget OfCrew(CrewMember c) => new(TargetKind.Crew, c.Cell, c.Room, crew: c);

    // ── v8 선체 밖 ──
    public static WorkTarget OfJoint(Joint j) => new(TargetKind.Joint, j.Cell, j.Room, key: $"J{j.Id}") { Joint = j };
    public static WorkTarget OfOuterWall(Cell wall, Room? inside) => new(TargetKind.Outer, wall, inside, key: $"O{wall}");
    public static WorkTarget OfExterior(Room room) =>
        new(TargetKind.Exterior, room.Joints.Count > 0 ? room.Joints[0].Spot : room.DamperSpot, room, key: $"E{room.Id}");
    public static WorkTarget OfFragment(Room room) => new(TargetKind.Fragment, room.DamperSpot, room, key: $"G{room.Id}");
    public static WorkTarget OfDrone(Drone d) => new(TargetKind.Drone, d.Dock.Cells[0], d.Dock.Room, furniture: d.Dock, key: $"Q{d.Id}") { Drone = d };

    // ── v10.10 선내 로봇: 멈춘 자리(충전대에 있으면 충전대 앞) ──
    public static WorkTarget OfRobot(Robot r) => new(TargetKind.Robot, r.Cell, r.Room, key: $"U{r.Id}") { Robot = r };

    // ── v9 배관 ──
    public static WorkTarget OfValve(PipeSegment s) => new(TargetKind.Valve, s.ValveCell, s.ValveRoom, key: $"V{s.Id}") { Pipe = s };
    public static WorkTarget OfPipe(PipeSegment s, Room? room) => new(TargetKind.Pipe, s.LeakAt, room ?? s.ValveRoom, key: $"L{s.Id}") { Pipe = s };
    public static WorkTarget OfRadiator(PipeSegment s) =>
        new(TargetKind.Radiator, s.Radiator[s.Radiator.Count / 2], s.Pump?.Room ?? s.ValveRoom, key: $"H{s.Id}") { Pipe = s };

    /// <summary>선체 밖에서 하는 일인지 (EVA·드론).</summary>
    public bool Outside => Kind is TargetKind.Joint or TargetKind.Outer or TargetKind.Exterior or TargetKind.Fragment or TargetKind.Radiator;

    /// <summary>작업 목록에서 같은 일을 가려내는 열쇠.</summary>
    public string Key => _key ?? Kind switch
    {
        TargetKind.Furniture => $"F{Furniture!.Id}",
        TargetKind.Crew => $"P{Crew!.Id}",
        TargetKind.Room => $"R{Room!.Id}",
        TargetKind.Door => $"D{Door!.Id}",
        _ => $"{Kind}{Cell}",
    };

    /// <summary>대상이 지금 있는 방 (사람은 움직인다).</summary>
    public Room? CurrentRoom => Crew?.Room ?? Room;

    public Vector2 Center => Kind switch
    {
        TargetKind.Furniture => Furniture!.Center,
        TargetKind.Crew => Crew!.Position,
        TargetKind.Robot => Robot!.Position,
        _ => Cell.Center,
    };

    public string Label => Kind switch
    {
        TargetKind.Drone => Drone!.Name,
        TargetKind.Robot => Robot!.Name,
        TargetKind.Valve => $"{Pipe!.Name} 밸브",
        TargetKind.Pipe => Pipe!.Name,
        TargetKind.Radiator => $"{Pipe!.Pump?.Label ?? Pipe!.Name} 방열판",
        TargetKind.Furniture => Furniture!.Label,
        TargetKind.Joint => Joint!.Label,
        TargetKind.Outer => $"{Room?.Name ?? "?"} 외벽 골조",
        TargetKind.Exterior => $"{Room!.Name} 바깥",
        TargetKind.Fragment => $"떨어져 나간 {Room!.Name}",
        TargetKind.Crew => Crew!.Name,
        TargetKind.Wall => $"{Room?.Name ?? "?"} 외벽",
        TargetKind.Room => Room!.Name,
        TargetKind.Door => "격벽",
        _ => Room?.Name ?? "?",
    };

    /// <summary>일하려면 서 있어야 하는 칸 후보.</summary>
    public IEnumerable<Cell> Spots(Ship ship)
    {
        switch (Kind)
        {
            case TargetKind.Furniture:
                return Furniture!.UseSpots;
            case TargetKind.Room:
                return new[] { Cell }.Concat(Cell.Dirs4.Select(d => Cell + d)).Where(ship.IsWalkable);
            case TargetKind.Crew:
                var at = Crew!.Cell;
                return Cell.Dirs8.Select(d => at + d).Where(ship.IsOpenFloor).Append(at);
            case TargetKind.Door:
            case TargetKind.Wall:
                return Near(ship, Cell);
            case TargetKind.Drone:
                return Furniture!.UseSpots;
            case TargetKind.Joint:
                return new[] { Joint!.Spot }.Concat(Cell.Dirs8.Select(d => Cell + d)).Where(c => ship.Grid.Kind(c) == TileKind.Void).Distinct();
            case TargetKind.Outer:
                return Cell.Dirs4.Concat(Cell.Dirs8).Select(d => Cell + d).Where(c => ship.Grid.Kind(c) == TileKind.Void).Distinct();
            case TargetKind.Exterior:
                return Room!.Joints.Select(j => j.Spot).Where(c => ship.Grid.Kind(c) == TileKind.Void);
            case TargetKind.Fragment:
                return Array.Empty<Cell>(); // 견인 드론만
            case TargetKind.Valve:
                return new[] { Cell }.Concat(Cell.Dirs4.Select(d => Cell + d)).Where(ship.IsWalkable);
            case TargetKind.Pipe:
            {
                // 터진 자리 옆 (벽 속이면 벽 앞), 없으면 두 칸 거리
                var leak = Cell;
                var ring = new[] { leak }.Concat(Cell.Dirs8.Select(d => leak + d)).Where(c => ship.Grid.Kind(c) == TileKind.Floor && ship.IsWalkable(c)).ToList();
                return ring.Count > 0 ? ring : Near(ship, leak);
            }
            case TargetKind.Radiator:
                return Pipe!.Radiator.Where(c => ship.Grid.Kind(c) == TileKind.Void);
            case TargetKind.Robot:
            {
                var r = Robot!;
                if (r.State == RobotState.Docked) return r.Dock.UseSpots;
                var rc = r.Cell;
                return new[] { rc }.Concat(Cell.Dirs8.Select(d => rc + d)).Where(c => ship.Grid.Kind(c) == TileKind.Floor && ship.IsWalkable(c));
            }
            default: // 불난 칸: 바로 옆에서
                return Cell.Dirs8.Select(d => Cell + d).Where(ship.IsWalkable);
        }
    }

    /// <summary>벽·문 앞: 바로 앞 칸 → 대각선 → 두 칸 거리 (선반 같은 가구가 막고 있으면 넘어서 손을 뻗는다).</summary>
    private static IEnumerable<Cell> Near(Ship ship, Cell at)
    {
        bool Ok(Cell c) => ship.Grid.Kind(c) == TileKind.Floor && ship.IsWalkable(c);
        var ring = Cell.Dirs4.Select(d => at + d).Where(Ok).ToList();
        if (ring.Count > 0) return ring;
        ring = Cell.Dirs8.Select(d => at + d).Where(Ok).ToList();
        if (ring.Count > 0) return ring;
        var far = new List<Cell>();
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == 2 && Ok(at + new Cell(dx, dy))) far.Add(at + new Cell(dx, dy));
        return far;
    }
}
