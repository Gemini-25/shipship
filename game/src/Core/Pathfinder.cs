using System;
using System.Collections.Generic;

namespace ShipSim.Core;

/// <summary>한 지점에서 모든 칸까지의 이동 비용. -1이면 갈 수 없음.</summary>
public sealed class DistanceField
{
    private readonly ShipGrid _grid;
    private readonly int[] _cost;

    internal DistanceField(ShipGrid grid, int[] cost)
    {
        _grid = grid;
        _cost = cost;
    }

    public int Get(Cell c) => _grid.InBounds(c) ? _cost[_grid.Index(c)] : -1;
    public bool Reachable(Cell c) => Get(c) >= 0;
}

/// <summary>
/// 길을 고르는 사람의 성향. "갈 수는 있는데 가고 싶지는 않다"를 표현한다.
/// 겁 많은 사람은 위험 비용을 크게 느끼고, 급한 일을 맡은 책임감 있는 사람은 덜 느낀다.
/// </summary>
public readonly record struct PathProfile(float HazardScale = 1f, bool Suit = false, bool Responder = false, float[]? Fear = null, bool Eva = false)
{
    /// <summary>선체 밖 한 칸을 지나는 추가 비용 (손으로 짚어 가며 느리게).</summary>
    public const int SpaceCost = 14;

    public static readonly PathProfile Default = new(1f, false, false);

    /// <summary>공포 1인 방을 한 칸 지날 때 더 드는 비용 (급한 일로 달려갈 때는 40%만).</summary>
    public const int FearCost = 45;
}

/// <summary>
/// 8방향 A*. 대각선은 양옆이 모두 비어 있을 때만, 문은 정면으로만 통과한다.
/// 칸 비용 = 이동 + 가구 위(돌아가기) + 방의 위험(저산소·고온·어둠) × 성향 + 전기 없는 문.
/// 숨 쉴 수 없는 방은 우주복이 없으면 못 지나간다.
/// 칸 정보는 배열로 캐시해 두고, 구조가 바뀌면 Invalidate()로 다시 만든다.
/// </summary>
public sealed class Pathfinder
{
    private const int Straight = 10;
    private const int Diagonal = 14;
    private const int FurniturePenalty = 25;
    private const int ManualDoorPenalty = 30;

    private readonly Ship _ship;
    private readonly int _w;
    private readonly int _n;
    private readonly int[] _g;
    private readonly int[] _parent;
    private readonly int[] _stamp;
    private int _run;

    // 캐시: 걸을 수 있나, 방 번호, 문 번호(없으면 -1), 가구 위인가
    private bool[] _walk = Array.Empty<bool>();
    private int[] _room = Array.Empty<int>();
    private int[] _door = Array.Empty<int>();
    private bool[] _furniture = Array.Empty<bool>();
    private bool[] _space = Array.Empty<bool>();
    private readonly int[] _offsets;

    /// <summary>계류 중인 조각이 차지한 칸 (EVA로 들어가 일할 수 있다).</summary>
    public HashSet<Cell> MooredCells { get; } = new();

    /// <summary>EVA로 떠다닐 수 있는 우주 칸인지 (선체에서 두 칸 안, 또는 계류 중인 조각).</summary>
    public bool IsSpace(Cell c) => _ship.Grid.InBounds(c) && _space[_ship.Grid.Index(c)];

    /// <summary>칸마다 덧붙는 위험 비용 (불과 그 주변). 화재 시스템이 채운다.</summary>
    public int[] CellHazard { get; }
    private readonly int[] _dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private readonly int[] _dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

    public Pathfinder(Ship ship)
    {
        _ship = ship;
        _w = ship.Grid.Width;
        _n = ship.Grid.CellCount;
        _g = new int[_n];
        _parent = new int[_n];
        _stamp = new int[_n];
        CellHazard = new int[_n];
        _offsets = new int[8];
        for (int i = 0; i < 8; i++) _offsets[i] = _dy[i] * _w + _dx[i];
        Invalidate();
    }

    /// <summary>벽·문·가구가 바뀌면 호출 (사고로 구조가 바뀔 때).</summary>
    public void Invalidate()
    {
        var grid = _ship.Grid;
        _walk = new bool[_n];
        _room = new int[_n];
        _door = new int[_n];
        _furniture = new bool[_n];
        _space = new bool[_n];
        for (int i = 0; i < _n; i++)
        {
            var c = grid.CellAt(i);
            _walk[i] = _ship.IsWalkable(c);
            _room[i] = grid.RoomId(c);
            _door[i] = _ship.DoorAt(c)?.Id ?? -1;
            _furniture[i] = grid.FurnitureId(c) >= 0;
        }
        // 선체 밖: 우주 칸 중 선체에서 두 칸 안 (손잡이·안전줄이 닿는 곳)
        for (int i = 0; i < _n; i++)
        {
            var c = grid.CellAt(i);
            if (grid.Kind(c) != TileKind.Void) continue;
            if (c.X <= 0 || c.Y <= 0 || c.X >= grid.Width - 1 || c.Y >= grid.Height - 1) continue;
            bool near = MooredCells.Contains(c);
            for (int dy = -2; dy <= 2 && !near; dy++)
            for (int dx = -2; dx <= 2 && !near; dx++)
            {
                var n = new Cell(c.X + dx, c.Y + dy);
                if (grid.InBounds(n) && grid.Kind(n) != TileKind.Void) near = true;
            }
            _space[i] = near;
        }
    }

    /// <summary>EVA로 드나드는 외부 해치.</summary>
    private bool Hatch(int i)
    {
        int d = _door[i];
        return d >= 0 && _ship.Doors[d].IsExternal && !_ship.Doors[d].Removed;
    }

    private bool Passable(int i, PathProfile p) => Walk(i) || (p.Eva && (_space[i] || Hatch(i)));

    private bool Walk(int i)
    {
        if (!_walk[i]) return false;
        int d = _door[i];
        return d < 0 || !_ship.Doors[d].IsExternal;
    }

    /// <summary>start 다음 칸부터 goal까지의 경로. 못 가면 null, 이미 도착했으면 빈 목록.</summary>
    public List<Cell>? Find(Cell start, Cell goal, PathProfile profile = default)
    {
        if (profile.HazardScale == 0f) profile = PathProfile.Default;
        var grid = _ship.Grid;
        if (start == goal) return new List<Cell>();
        if (!grid.InBounds(goal) || !grid.InBounds(start)) return null;
        int goalIndex = grid.Index(goal);
        if (!Passable(goalIndex, profile)) return null;

        _run++;
        var open = new PriorityQueue<int, int>();
        int s = grid.Index(start);
        int startRoom = _room[s];
        Touch(s, 0, -1);
        open.Enqueue(s, Heuristic(s, goalIndex));

        while (open.TryDequeue(out int cur, out int priority))
        {
            if (priority - Heuristic(cur, goalIndex) > _g[cur]) continue;
            if (cur == goalIndex) return Rebuild(goalIndex, s);

            for (int k = 0; k < 8; k++)
            {
                int ni = cur + _offsets[k];
                if (!CanStep(cur, k, ni, profile, startRoom)) continue;
                int cost = _g[cur] + StepCost(k, ni, goalIndex, profile);
                if (_stamp[ni] == _run && cost >= _g[ni]) continue;
                Touch(ni, cost, cur);
                open.Enqueue(ni, cost + Heuristic(ni, goalIndex));
            }
        }
        return null;
    }

    /// <summary>다익스트라로 모든 칸까지의 비용을 한 번에 (가장 가까운 X 찾기용).</summary>
    public DistanceField Flood(Cell start, PathProfile profile = default)
    {
        if (profile.HazardScale == 0f) profile = PathProfile.Default;
        var grid = _ship.Grid;
        var cost = new int[_n];
        Array.Fill(cost, -1);
        if (!grid.InBounds(start)) return new DistanceField(grid, cost);

        int s = grid.Index(start);
        int startRoom = _room[s];
        var open = new PriorityQueue<int, int>();
        cost[s] = 0;
        open.Enqueue(s, 0);
        while (open.TryDequeue(out int cur, out int dist))
        {
            if (dist > cost[cur]) continue;
            for (int k = 0; k < 8; k++)
            {
                int ni = cur + _offsets[k];
                if (!CanStep(cur, k, ni, profile, startRoom)) continue;
                int nd = dist + StepCost(k, ni, -1, profile);
                if (cost[ni] >= 0 && nd >= cost[ni]) continue;
                cost[ni] = nd;
                open.Enqueue(ni, nd);
            }
        }
        return new DistanceField(grid, cost);
    }

    private bool CanStep(int from, int k, int to, PathProfile profile, int startRoom)
    {
        // 설계도 둘레에 빈 여백이 있으므로 이웃 인덱스가 배열 밖으로 나가지 않는다
        if (to < 0 || to >= _n) return false;
        if (!Passable(to, profile)) return false;
        int dr = _door[to];
        if (dr >= 0 && _ship.Doors[dr].Locked)
        {
            // 잠긴 격벽: 그 방에서 나가는 사람, 우주복 입은 사람, 급한 일로 달려가는 사람만 비상 개방으로 통과.
            // 포기한 구획으로 들어가는 문은 용접돼 있어서 우주복을 입어야만 (절단하고) 들어간다.
            var door = _ship.Doors[dr];
            bool leaving = (door.RoomA?.Id ?? -1) == startRoom || (door.RoomB?.Id ?? -1) == startRoom;
            bool sealedOff = (door.RoomA?.Abandoned ?? false) || (door.RoomB?.Abandoned ?? false);
            if (!leaving && !profile.Suit && (sealedOff || !profile.Responder)) return false;
        }
        if (!profile.Suit)
        {
            int r = _room[to];
            if (r >= 0 && r != startRoom && _ship.Rooms[r].Unbreathable) return false;
        }
        if (k < 4) return true;
        if (_door[from] >= 0 || _door[to] >= 0) return false;
        int a = from + _dx[k];
        int b = from + _dy[k] * _w;
        return Passable(a, profile) && Passable(b, profile) && _door[a] < 0 && _door[b] < 0;
    }

    private int StepCost(int k, int to, int goal, PathProfile profile)
    {
        int cost = k < 4 ? Straight : Diagonal;
        if (_space[to]) cost += PathProfile.SpaceCost;
        if (to != goal && _furniture[to]) cost += FurniturePenalty;
        int r = _room[to];
        if (r >= 0)
        {
            int hazard = _ship.Rooms[r].HazardCost;
            if (hazard > 0) cost += (int)(hazard * profile.HazardScale);
            if (profile.Fear is { } fear && r < fear.Length && fear[r] > 0.05f)
                cost += (int)(fear[r] * PathProfile.FearCost * (profile.Responder ? 0.4f : 1f));
        }
        int d = _door[to];
        if (d >= 0 && !_ship.Doors[d].Powered) cost += ManualDoorPenalty;
        if (d >= 0 && _ship.Doors[d].Locked) cost += ManualDoorPenalty * 2;
        int h = CellHazard[to];
        if (h > 0) cost += (int)(h * (profile.Suit ? 0.5f : 1f) * profile.HazardScale);
        return cost;
    }

    private int Heuristic(int a, int b)
    {
        int dx = Math.Abs(a % _w - b % _w), dy = Math.Abs(a / _w - b / _w);
        return Straight * (dx + dy) + (Diagonal - 2 * Straight) * Math.Min(dx, dy);
    }

    private void Touch(int index, int g, int parent)
    {
        _stamp[index] = _run;
        _g[index] = g;
        _parent[index] = parent;
    }

    private List<Cell> Rebuild(int goal, int start)
    {
        var path = new List<Cell>();
        for (int i = goal; i != start; i = _parent[i]) path.Add(_ship.Grid.CellAt(i));
        path.Reverse();
        return path;
    }
}
