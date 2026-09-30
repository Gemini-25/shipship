using System;
using System.Numerics;

namespace ShipSim.Core;

/// <summary>타일 좌표. 칸의 중심은 (X+0.5, Y+0.5).</summary>
public readonly record struct Cell(int X, int Y)
{
    public static Cell operator +(Cell a, Cell b) => new(a.X + b.X, a.Y + b.Y);
    public static Cell operator -(Cell a, Cell b) => new(a.X - b.X, a.Y - b.Y);

    public Vector2 Center => new(X + 0.5f, Y + 0.5f);
    public bool IsDiagonal => X != 0 && Y != 0;

    public static Cell FromPosition(Vector2 p) => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));

    public static readonly Cell[] Dirs4 = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    public static readonly Cell[] Dirs8 =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    public override string ToString() => $"({X},{Y})";
}

public enum TileKind : byte
{
    Void,   // 우주 공간
    Floor,  // 바닥 (가구가 올라갈 수 있음)
    Wall,   // 벽/선체
    Door,   // 문
}

/// <summary>순수 타일 데이터. 방/가구 소속은 id로만 저장한다.</summary>
public sealed class ShipGrid
{
    public int Width { get; }
    public int Height { get; }

    private readonly TileKind[] _kinds;
    private readonly int[] _roomIds;
    private readonly int[] _furnitureIds;

    public ShipGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _kinds = new TileKind[width * height];
        _roomIds = new int[width * height];
        _furnitureIds = new int[width * height];
        Array.Fill(_roomIds, -1);
        Array.Fill(_furnitureIds, -1);
    }

    public int CellCount => Width * Height;
    public bool InBounds(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;
    public int Index(Cell c) => c.Y * Width + c.X;
    public Cell CellAt(int index) => new(index % Width, index / Width);

    public TileKind Kind(Cell c) => InBounds(c) ? _kinds[Index(c)] : TileKind.Void;
    public void SetKind(Cell c, TileKind kind) => _kinds[Index(c)] = kind;

    public int RoomId(Cell c) => InBounds(c) ? _roomIds[Index(c)] : -1;
    public void SetRoomId(Cell c, int id) => _roomIds[Index(c)] = id;

    public int FurnitureId(Cell c) => InBounds(c) ? _furnitureIds[Index(c)] : -1;
    public void SetFurnitureId(Cell c, int id) => _furnitureIds[Index(c)] = id;
}
