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
    public int Height { get; private set; }

    private TileKind[] _kinds;
    private int[] _roomIds;
    private int[] _furnitureIds;

    /// <summary>v16.10 격자가 자란 횟수 (증축) — 칸 배열을 쥔 쪽이 늘려야 하는지 본다.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// v16.10 증축: 아래(+Y)로 줄을 덧붙인다. 칸 번호 = Y·Width + X 라 기존 칸의 번호 · 좌표는 하나도 바뀌지 않는다 —
    /// 칸 번호를 쥔 배열은 뒤를 늘리기만 하면 되고(새 칸은 빈 우주), 번호를 키로 쓴 사전 · 기록은 그대로 맞는다.
    /// (옆으로 넓히면 모든 번호가, 위 · 왼쪽으로 넓히면 모든 좌표가 바뀐다 — 그래서 아래로만.)
    /// </summary>
    public void GrowRows(int rows)
    {
        if (rows <= 0) return;
        int n = Width * (Height + rows), old = Width * Height;
        Array.Resize(ref _kinds, n);
        Array.Resize(ref _roomIds, n);
        Array.Resize(ref _furnitureIds, n);
        Array.Fill(_roomIds, -1, old, n - old);
        Array.Fill(_furnitureIds, -1, old, n - old);
        Height += rows;
        Version++;
    }

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
