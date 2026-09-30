using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>우주선 설계도. 텍스트로 편집할 수 있다.</summary>
public static partial class ShipBlueprints
{
    public const string MirinaeName = "미리내호";

    // 범례
    //   ' ' 우주 공간   '#' 벽   '+' 문 (한쪽이 우주면 외부 해치)   '.' 바닥
    //   소문자 = 방 라벨 (방마다 정확히 하나, 그 칸은 바닥으로 취급)
    //     c 중앙 통로  b 함교  e 엔진실  r 원자로실  k 냉각실  p 배전실  l 생명유지실
    //     w 정비실  s 창고  j 주방  m 식당  f 수경재배실  a 에어락  q 침실  h 의무실  g 휴게실
    //   대문자 = 가구/설비 (같은 글자가 붙어 있으면 하나로 묶임. 의자/콘솔/배식기는 제외)
    //     B 침대  S 의자  T 테이블  D 배식기  C 콘솔  R 원자로  E 엔진  O 산소 발생기
    //     W 작업대  K 선반  M 치료 침대  P 냉각 펌프  X 배전반  Y 배터리  G 재배대
    //     U 정수기  V 조리대  F 냉장고  L 우주복 보관함  Z 보조 발전기  H 채집 장치(선체 바깥 채집 팔 + 호퍼)  N 정제기
    //     Q 드론 거치대 (v8: 에어락 해치 옆, 드론 셋씩)  I 주 컴퓨터 (v9: 함교, 격벽·댐퍼·경보·부하·드론·제어봉을 자동으로)
    public const string Mirinae = """
       ###################################################
       #r.....CC#PPkPP#pXXX.#wWWWHH#KKsKK#VVjFF#DDm......#
########..RRR...#PP.PP#.....#......#KK.KK#.....#...SS.SS.#
#e....C#..RRR...+.....#...ZZ#......+.....#.....+...TT.TT.######
#EEE...+..RRR...#.....#.....#.....K#.....#.TT..#...SS.SS.#o..C##
#EEE...#........#.....#YY.YY#NN...K#KK.KK#.....#.........#...AA##
#EEE...#........#C....#YY.YY#NN...K#KK.KK#.....#.........#......#
#......#####+######+#####+#####+######+#####+#######+#####+######
#......+c................................................+bTT.SC#
#......+.................................................+.TT.SC#
#......#####+#########+#######+#######+#########+#####+###......#
#EEE...#l.....UU#f..........#a..#KKq.........#h...C#g....#...II.#
#EEE...#......UU#.GGGG.GGGG.#L.L#............#.....#S.SS.#...II##
#EEE...+OO.OO...#...........#L.L#............#.....#S.TTS#...C##
#.....C#OO.OO...+...........#...#............#.M.M.#S.TTS######
########OO.OO...#.GGGG.GGGG.#Q.Q#B.B.B.B.B.B.#.M.M.#..SS.#
       #OO.OO..C#...........#Q.Q#B.B.B.B.B.B.#K....#.....#
       #######################+###########################
""";
}

/// <summary>방, 가구, 문, 벽, 타일을 모두 가진 우주선 구조물.</summary>
public sealed class Ship
{
    public string Name { get; }
    public ShipGrid Grid { get; }
    public List<Room> Rooms { get; } = new();
    public List<Furniture> Furniture { get; } = new();
    public List<Door> Doors { get; } = new();

    private readonly Dictionary<Cell, Door> _doorsByCell = new();
    private readonly Dictionary<Cell, WallState> _walls = new();

    internal Ship(string name, ShipGrid grid)
    {
        Name = name;
        Grid = grid;
    }

    internal void AddDoor(Door d)
    {
        Doors.Add(d);
        _doorsByCell[d.Cell] = d;
    }

    internal void AddWall(Cell c, WallState w) => _walls[c] = w;
    internal void RemoveWall(Cell c) => _walls.Remove(c);

    /// <summary>문을 격자에서 뺀다 (한쪽 방이 떨어져 나갔을 때). 문 목록에는 남는다 (번호가 바뀌지 않게).</summary>
    internal void UnmapDoor(Door d) => _doorsByCell.Remove(d.Cell);
    internal void MapDoor(Door d) => _doorsByCell[d.Cell] = d;

    /// <summary>떨어져 나가지 않은 방.</summary>
    public IEnumerable<Room> LiveRooms => Rooms.Where(r => !r.Detached);

    public Room? RoomAt(Cell c)
    {
        int id = Grid.RoomId(c);
        return id >= 0 ? Rooms[id] : null;
    }

    /// <summary>운항 중에 가구를 새로 놓는다 (간이침대 등). 길찾기 캐시는 부른 쪽이 다시 만든다.</summary>
    public Furniture AddFurniture(FurnitureType type, Cell cell)
    {
        var room = RoomAt(cell) ?? throw new InvalidOperationException("방 밖에는 가구를 놓을 수 없다");
        var f = new Furniture { Id = Furniture.Count, Type = type, Room = room };
        Furniture.Add(f);
        room.Furniture.Add(f);
        Grid.SetFurnitureId(cell, f.Id);
        f.Include(cell);
        // 올라설 수 있는 가구(간이침대)는 그 칸에서, 아니면 옆 빈 바닥에서 쓴다
        if (FurnitureTypes.Walkable(type)) f.UseSpots.Add(cell);
        else foreach (var d in Cell.Dirs4) if (IsOpenFloor(cell + d) && RoomAt(cell + d) == room) f.UseSpots.Add(cell + d);
        f.Label = $"{f.Name} #{Furniture.Count(x => x.Type == type)}";
        if (MachineSpecs.For(type) is MachineSpec spec) f.Machine = new Machine(f, spec);
        return f;
    }

    /// <summary>v10.1: 여러 칸짜리 가구 (항해 중에 짜 넣는 재배대).</summary>
    public Furniture AddFurniture(FurnitureType type, IReadOnlyList<Cell> cells)
    {
        var f = AddFurniture(type, cells[0]);
        for (int i = 1; i < cells.Count; i++)
        {
            Grid.SetFurnitureId(cells[i], f.Id);
            f.Include(cells[i]);
        }
        f.Cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        if (!FurnitureTypes.Walkable(type))
        {
            f.UseSpots.Clear();
            var seen = new HashSet<Cell>();
            foreach (var c in f.Cells)
            foreach (var d in Cell.Dirs4)
            {
                var n = c + d;
                if (IsOpenFloor(n) && RoomAt(n) == f.Room && seen.Add(n)) f.UseSpots.Add(n);
            }
        }
        return f;
    }

    public Furniture? FurnitureAt(Cell c)
    {
        int id = Grid.FurnitureId(c);
        return id >= 0 ? Furniture[id] : null;
    }

    public Door? DoorAt(Cell c) => _doorsByCell.TryGetValue(c, out var d) ? d : null;
    public WallState? WallAt(Cell c) => _walls.TryGetValue(c, out var w) ? w : null;
    public IEnumerable<KeyValuePair<Cell, WallState>> Walls => _walls;

    /// <summary>v12.2 잔해 (칸 → 0~1): 반 넘게 쌓이면 지나갈 수 없다 — 치워야 한다.</summary>
    public Dictionary<Cell, float> Rubble { get; } = new();

    public bool IsWalkable(Cell c)
    {
        if (Rubble.Count > 0 && Rubble.TryGetValue(c, out var rb) && rb >= 0.5f) return false;
        var kind = Grid.Kind(c);
        if (kind == TileKind.Door)
        {
            var d = DoorAt(c);
            return d != null && !d.IsExternal; // 잠긴 격벽은 길찾기가 따로 따진다 (비상 개방)
        }
        if (kind != TileKind.Floor) return false;
        var f = FurnitureAt(c);
        return f == null || FurnitureTypes.Walkable(f.Type);
    }

    /// <summary>가구가 없는 빈 바닥.</summary>
    public bool IsOpenFloor(Cell c) => Grid.Kind(c) == TileKind.Floor && Grid.FurnitureId(c) < 0;

    // 떨어져 나간 방의 가구·설비는 우주선에 없는 것으로 친다 (조각에 실려 있다)
    public IEnumerable<Room> RoomsOf(RoomType type) => Rooms.Where(r => r.Type == type && !r.Detached);
    public IEnumerable<Furniture> FurnitureOf(FurnitureType type) => Furniture.Where(f => f.Type == type && !f.Room.Detached && !f.Stowed);
    public IEnumerable<Machine> Machines => Furniture.Where(f => f.Machine != null && !f.Room.Detached && !f.Stowed).Select(f => f.Machine!);

    /// <summary>배에 놓여 있는 가구 (치운 것 빼고).</summary>
    public IEnumerable<Furniture> Placed => Furniture.Where(f => !f.Stowed);

    /// <summary>승무원이 쓰는 보관함 (드론 거치대의 자재칸은 드론 몫이라 뺀다).</summary>
    public IEnumerable<Furniture> Containers => Furniture.Where(f => f.Storage != null && !f.Room.Detached && !f.Stowed && f.Type != FurnitureType.DroneDock);

    /// <summary>
    /// v10.10: 가구를 치운다 (간이침대를 접어 창고로, 뜯긴 설비를 해체). 번호가 바뀌지 않게 목록에는 남기고, 격자·방에서만 뺀다.
    /// 길찾기 캐시는 부른 쪽이 다시 만든다.
    /// </summary>
    public void Stow(Furniture f)
    {
        if (f.Stowed) return;
        foreach (var c in f.Cells)
            if (Grid.FurnitureId(c) == f.Id) Grid.SetFurnitureId(c, -1);
        f.Room.Furniture.Remove(f);
        f.UseSpots.Clear();
        f.ReservedBy = null;
        f.Owner = null;
        f.Stowed = true;
    }

    /// <summary>우주선 전체에 있는 물건 개수 (보관함만, 손에 든 것 제외).</summary>
    public int CountStored(ItemKind k) => Containers.Sum(f => f.Storage!.Count(k));
}

/// <summary>텍스트 설계도 → Ship.</summary>
public static class ShipBuilder
{
    public static Ship FromAscii(string name, string map, int margin = 3)
    {
        var lines = map.Replace("\r", "").Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        if (lines.Count == 0) throw new FormatException("설계도가 비어 있습니다.");

        int width = lines.Max(l => l.Length) + margin * 2;
        int height = lines.Count + margin * 2;
        var grid = new ShipGrid(width, height);
        var ship = new Ship(name, grid);

        var labels = new List<(Cell cell, RoomType type)>();
        var furnitureChars = new Dictionary<Cell, char>();

        // 1) 타일 종류
        for (int row = 0; row < lines.Count; row++)
        {
            string line = lines[row];
            for (int col = 0; col < line.Length; col++)
            {
                char ch = line[col];
                var cell = new Cell(col + margin, row + margin);
                switch (ch)
                {
                    case ' ': grid.SetKind(cell, TileKind.Void); break;
                    case '#': grid.SetKind(cell, TileKind.Wall); break;
                    case '+': grid.SetKind(cell, TileKind.Door); break;
                    case '.': grid.SetKind(cell, TileKind.Floor); break;
                    default:
                        grid.SetKind(cell, TileKind.Floor);
                        if (RoomTypes.FromLabel(ch) is RoomType rt) labels.Add((cell, rt));
                        else if (FurnitureTypes.FromChar(ch) != null) furnitureChars[cell] = ch;
                        else throw new FormatException($"설계도 {row + 1}행 {col + 1}열: 알 수 없는 글자 '{ch}'");
                        break;
                }
            }
        }

        // 2) 방: 라벨에서 바닥을 따라 퍼져 나가며 채운다
        foreach (var (start, type) in labels)
        {
            if (grid.RoomId(start) >= 0)
                throw new FormatException($"{start} 방에 라벨이 두 개 있습니다.");

            var room = new Room { Id = ship.Rooms.Count, Type = type };
            ship.Rooms.Add(room);

            var stack = new Stack<Cell>();
            stack.Push(start);
            grid.SetRoomId(start, room.Id);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                room.Include(c);
                foreach (var d in Cell.Dirs4)
                {
                    var n = c + d;
                    if (grid.Kind(n) == TileKind.Floor && grid.RoomId(n) < 0)
                    {
                        grid.SetRoomId(n, room.Id);
                        stack.Push(n);
                    }
                }
            }
        }

        for (int i = 0; i < grid.CellCount; i++)
        {
            var c = grid.CellAt(i);
            if (grid.Kind(c) == TileKind.Floor && grid.RoomId(c) < 0)
                throw new FormatException($"{c} 바닥이 어느 방에도 속하지 않습니다. 방 라벨을 넣어 주세요.");
        }

        // 3) 가구: 같은 글자끼리 붙은 덩어리를 하나로
        foreach (var (start, ch) in furnitureChars.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
        {
            if (grid.FurnitureId(start) >= 0) continue;
            var type = FurnitureTypes.FromChar(ch)!.Value;
            var f = new Furniture { Id = ship.Furniture.Count, Type = type, Room = ship.RoomAt(start)! };
            ship.Furniture.Add(f);
            f.Room.Furniture.Add(f);

            var stack = new Stack<Cell>();
            stack.Push(start);
            grid.SetFurnitureId(start, f.Id);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                f.Include(c);
                if (!FurnitureTypes.Groups(type)) continue;
                foreach (var d in Cell.Dirs4)
                {
                    var n = c + d;
                    if (grid.FurnitureId(n) < 0 && furnitureChars.TryGetValue(n, out var nch) && nch == ch)
                    {
                        grid.SetFurnitureId(n, f.Id);
                        stack.Push(n);
                    }
                }
            }
            f.Cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        }

        // 4) 사용 위치: 올라서는 가구는 자기 칸(침대는 머리 쪽), 나머지는 옆의 빈 바닥
        foreach (var f in ship.Furniture)
        {
            if (FurnitureTypes.Walkable(f.Type))
            {
                f.UseSpots.Add(f.Cells[0]);
                continue;
            }
            var seen = new HashSet<Cell>();
            foreach (var c in f.Cells)
            foreach (var d in Cell.Dirs4)
            {
                var n = c + d;
                if (ship.IsOpenFloor(n) && ship.RoomAt(n) == f.Room && seen.Add(n))
                    f.UseSpots.Add(n);
            }
        }

        // 5) 설비와 보관함, 이름표
        var counts = ship.Furniture.GroupBy(f => f.Type).ToDictionary(g => g.Key, g => g.Count());
        var seq = new Dictionary<FurnitureType, int>();
        foreach (var f in ship.Furniture)
        {
            seq[f.Type] = seq.TryGetValue(f.Type, out int n) ? n + 1 : 1;
            f.Label = counts[f.Type] > 1 ? $"{f.Name} #{seq[f.Type]}" : f.Name;
            if (MachineSpecs.For(f.Type) is MachineSpec spec) f.Machine = new Machine(f, spec);
            f.Storage = f.Type switch
            {
                FurnitureType.Shelf => new Inventory(60, ItemKinds.Shelved),
                FurnitureType.Collector => new Inventory(CollectionSystem.BinSize * 4 + 5, ItemKinds.RawKinds), // 채집 호퍼 (종류별 칸)
                FurnitureType.SuitLocker => new Inventory(2, ItemKind.Suit),
                FurnitureType.Fridge => new Inventory(150, ItemKind.Produce, ItemKind.Meal),
                FurnitureType.DroneDock => new Inventory(12, ItemKind.Structure, ItemKind.Plate, ItemKind.Sealant), // 드론이 싣고 나갈 자재
                FurnitureType.MealDispenser => new Inventory(20, ItemKind.Meal),
                _ => null,
            };
        }

        // 6) 문 (한쪽이 우주면 외부 해치)
        for (int i = 0; i < grid.CellCount; i++)
        {
            var c = grid.CellAt(i);
            if (grid.Kind(c) != TileKind.Door) continue;

            var up = c + new Cell(0, -1);
            var down = c + new Cell(0, 1);
            var left = c + new Cell(-1, 0);
            var right = c + new Cell(1, 0);
            bool vertical = Opening(grid, up) && Opening(grid, down) && (IsPassable(grid, up) || IsPassable(grid, down));
            bool horizontal = Opening(grid, left) && Opening(grid, right) && (IsPassable(grid, left) || IsPassable(grid, right));
            if (!vertical && !horizontal)
                throw new FormatException($"{c} 문이 두 공간을 잇지 않습니다.");

            var a = vertical ? up : left;
            var b = vertical ? down : right;
            bool external = grid.Kind(a) == TileKind.Void || grid.Kind(b) == TileKind.Void;
            var door = new Door
            {
                Id = ship.Doors.Count, Cell = c, ConnectsVertically = vertical,
                IsExternal = external, Locked = external,
            };
            door.RoomA = ship.RoomAt(a);
            door.RoomB = ship.RoomAt(b);
            door.RoomA?.Doors.Add(door);
            door.RoomB?.Doors.Add(door);
            ship.AddDoor(door);
        }

        // 7) 환기 댐퍼 조작 위치: 가능하면 방 밖(통로 쪽) 문 옆
        foreach (var room in ship.Rooms) AssignDamperSpot(ship, room);

        // 8) 벽 상태: 우주에 닿은 벽은 선체
        for (int i = 0; i < grid.CellCount; i++)
        {
            var c = grid.CellAt(i);
            if (grid.Kind(c) != TileKind.Wall) continue;
            bool hull = Cell.Dirs8.Any(d => grid.Kind(c + d) == TileKind.Void);
            ship.AddWall(c, new WallState { IsHull = hull });
        }

        return ship;
    }

    /// <summary>환기 댐퍼 조작 위치: 가능하면 방 밖(통로 쪽) 문 옆 (v10.2: 칸막이로 나눈 뒤에도 다시 정한다).</summary>
    public static void AssignDamperSpot(Ship ship, Room room)
    {
        {
            Cell? spot = null;
            foreach (var d in room.Doors.OrderBy(d => (d.RoomA?.Type == RoomType.Corridor || d.RoomB?.Type == RoomType.Corridor) ? 0 : 1))
            {
                if (d.IsExternal) continue;
                var other = d.RoomA == room ? d.RoomB : d.RoomA;
                if (other == null || other == room) continue;
                foreach (var n in Cell.Dirs4)
                {
                    var c = d.Cell + n;
                    if (ship.RoomAt(c) == other && ship.IsOpenFloor(c)) { spot = c; break; }
                }
                if (spot != null) break;
            }
            room.DamperSpot = spot ?? room.Cells.Where(ship.IsOpenFloor).OrderBy(c => (c.Center - room.Center).LengthSquared())
                .DefaultIfEmpty(room.Cells[0]).First();
        }
    }

    private static bool IsPassable(ShipGrid g, Cell c) => g.Kind(c) is TileKind.Floor or TileKind.Door;
    private static bool Opening(ShipGrid g, Cell c) => g.Kind(c) is TileKind.Floor or TileKind.Door or TileKind.Void;
}
