using System;
using System.Collections.Generic;
using System.Linq;

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

    public int Get(Cell c) { if (!_grid.InBounds(c)) return -1; int i = _grid.Index(c); return i < _cost.Length ? _cost[i] : -1; } // v16.10 증축 전에 만든 거리장은 새 칸을 모른다
    public bool Reachable(Cell c) => Get(c) >= 0;
}

/// <summary>
/// 길을 고르는 사람의 성향. "갈 수는 있는데 가고 싶지는 않다"를 표현한다.
/// 겁 많은 사람은 위험 비용을 크게 느끼고, 급한 일을 맡은 책임감 있는 사람은 덜 느낀다.
/// </summary>
public readonly record struct PathProfile(float HazardScale = 1f, bool Suit = false, bool Responder = false, float[]? Fear = null, bool Eva = false, bool Robot = false, bool NoCrawl = false, int Who = -1)
{
    // Who: 길을 고르는 사람 (출입 통제 문 권한을 따진다 · -1 = 따지지 않음 — 급한 일은 비상 해제 손잡이로 지나간다)
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
    /// <summary>권한 없이는 못 여는 출입 통제 문 (원자로실 카드 잠금 · 잠근 선실 …): 다른 길이 200칸 가까이 더 멀어도 돌아간다 — 그 문밖에 길이 없을 때만 문 앞에 가서 권한자를 부른다.</summary>
    private const int BarredDoorPenalty = 2000;

    /// <summary>v16.26 이 사람이 지금 못 여는 출입 통제 문 (문 번호 · 통제되는 방) 목록을 채운다 — 배 본체가 건다.</summary>
    public Func<int, List<(int door, int inner)>, bool>? BarredDoors { get; set; }
    private readonly List<(int door, int inner)> _barred = new();
    private int[] _barMark = Array.Empty<int>();
    private int _barRun;

    /// <summary>이 길에서 비켜 갈 출입 통제 문을 표시하고, 거리장 캐시 열쇠에 넣을 서명을 낸다 (0 = 없음).</summary>
    private int MarkBarred(PathProfile profile, int startRoom)
    {
        _barRun++;
        if (profile.Who < 0 || BarredDoors == null || !BarredDoors(profile.Who, _barred)) return 0;
        int nd = _ship.Doors.Count;
        if (_barMark.Length < nd) _barMark = new int[nd];
        int sig = 0;
        foreach (var (d, inner) in _barred)
        {
            if (d < 0 || d >= nd || inner == startRoom) continue; // 안에 있는 사람은 언제나 나간다
            _barMark[d] = _barRun;
            sig = unchecked(sig * 31 + d + 1);
        }
        return sig;
    }

    private bool Barred(int door) => door >= 0 && door < _barMark.Length && _barMark[door] == _barRun;

    private readonly Ship _ship;
    private readonly int _w;
    private int _n;
    private int[] _g;
    private int[] _parent;
    private int[] _stamp;
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
    public int[] CellHazard { get; private set; }
    /// <summary>v16.3 배 본체가 채우는 칸 비용 (열린 점검 뚜껑 · 테이프 · 유리 · 기름) — 사람들은 돌아간다.</summary>
    public int[] CellBody { get; private set; }
    /// <summary>v16.3 정비 통로 (벽 속을 기어서 지나는 칸) — 배 본체가 연다 · 로봇 · 업은 사람 · 다친 사람은 못 지난다.</summary>
    public bool[] Crawl { get; private set; }
    public void CrawlChanged() => _hazardVersion++;
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
        CellBody = new int[_n];
        Crawl = new bool[_n];
        _offsets = new int[8];
        for (int i = 0; i < 8; i++) _offsets[i] = _dy[i] * _w + _dx[i];
        Invalidate();
    }

    /// <summary>v16.10 증축: 격자가 아래로 자랐다 — 칸 번호는 그대로라 배열 뒤만 늘린다 (너비가 같아 이웃 칸 간격도 그대로).</summary>
    public void Grow()
    {
        int n = _ship.Grid.CellCount;
        if (n == _n) return;
        _n = n;
        Array.Resize(ref _g, n); Array.Resize(ref _parent, n); Array.Resize(ref _stamp, n);
        var h = CellHazard; Array.Resize(ref h, n); CellHazard = h;
        var b = CellBody; Array.Resize(ref b, n); CellBody = b;
        var cr = Crawl; Array.Resize(ref cr, n); Crawl = cr;
        _floods.Clear();
        _hazardSeen = Array.Empty<int>(); _bodySeen = Array.Empty<int>();
        Invalidate();
    }

    /// <summary>벽·문·가구가 바뀌면 호출 (사고로 구조가 바뀔 때).</summary>
    public void Invalidate()
    {
        _structure++;
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

    private bool Passable(int i, PathProfile p) => Walk(i) || (p.Eva && (_space[i] || Hatch(i))) || Crawl[i] && !p.Robot && !p.NoCrawl;

    private bool Walk(int i)
    {
        if (!_walk[i]) return false;
        int d = _door[i];
        return d < 0 || !_ship.Doors[d].IsExternal;
    }

    private int _avoid = -1;

    /// <summary>v14.5 한 칸(일하는 사람이 막고 선 곳)을 되도록 피해 가는 길.</summary>
    public List<Cell>? FindAvoiding(Cell start, Cell goal, PathProfile profile, Cell avoid)
    {
        _avoid = _ship.Grid.InBounds(avoid) ? _ship.Grid.Index(avoid) : -1;
        try { return Find(start, goal, profile); }
        finally { _avoid = -1; }
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
        MarkBarred(profile, startRoom);
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
    // ── v14.2 거리장 캐시: 결과를 바꾸는 입력(구조 · 문 · 방 · 불의 칸 위험 · 성향 · 두려움)이 모두 같으면 다시 쓴다 ──
    private int _structure;              // Invalidate마다
    private int _hazardVersion;          // 불의 칸 위험을 다시 채울 때마다
    private int _stateVersion;           // 문·방 상태가 바뀔 때마다
    private int[] _state = Array.Empty<int>(), _scratch = Array.Empty<int>();
    private sealed class FloodEntry { public int Version; public float[]? Fear; public DistanceField Field = null!; }
    private readonly Dictionary<(int start, float scale, int flags, int bar), FloodEntry> _floods = new();
    public int FloodHits { get; private set; }
    public int FloodMisses { get; private set; }

    private int[] _hazardSeen = Array.Empty<int>();

    /// <summary>불의 칸 위험(CellHazard)을 다시 채웠다 (화재 시스템) — 실제로 달라졌을 때만 판 번호를 올린다.</summary>
    public void HazardChanged()
    {
        if (_hazardSeen.Length == CellHazard.Length && CellHazard.AsSpan().SequenceEqual(_hazardSeen)) return;
        _hazardSeen = (int[])CellHazard.Clone();
        _hazardVersion++;
    }

    private int[] _bodySeen = Array.Empty<int>();
    /// <summary>v16.3 배 본체 칸 비용을 다시 채웠다.</summary>
    public void BodyChanged()
    {
        if (_bodySeen.Length == CellBody.Length && CellBody.AsSpan().SequenceEqual(_bodySeen)) return;
        _bodySeen = (int[])CellBody.Clone();
        _hazardVersion++;
    }

    /// <summary>문·방의 길에 걸리는 상태를 훑어, 바뀌었으면 판 번호를 올린다.</summary>
    private int StateVersion()
    {
        int nd = _ship.Doors.Count, nr = _ship.Rooms.Count;
        int len = 2 + nd + nr * 2;
        if (_scratch.Length != len) _scratch = new int[len];
        var s = _scratch;
        s[0] = _structure; s[1] = _hazardVersion;
        for (int i = 0; i < nd; i++)
        {
            var d = _ship.Doors[i];
            s[2 + i] = (d.Locked ? 1 : 0) | (d.Powered ? 2 : 0) | (d.Removed ? 4 : 0) | (d.IsExternal ? 8 : 0) | (d.Welded ? 16 : 0);
        }
        for (int i = 0; i < nr; i++)
        {
            var r = _ship.Rooms[i];
            s[2 + nd + i * 2] = r.HazardCost;
            s[3 + nd + i * 2] = (r.Unbreathable ? 1 : 0) | (r.Abandoned ? 2 : 0);
        }
        if (_state.Length != len || !s.AsSpan().SequenceEqual(_state))
        {
            _state = (int[])s.Clone();
            _stateVersion++;
        }
        return _stateVersion;
    }

    public DistanceField Flood(Cell start, PathProfile profile = default)
    {
        long pf = Prof.Now;
        if (profile.HazardScale == 0f) profile = PathProfile.Default;
        var grid = _ship.Grid;
        int version = StateVersion();
        int si = grid.InBounds(start) ? grid.Index(start) : -1;
        int bar = MarkBarred(profile, si >= 0 ? _room[si] : -1);
        var key = (si, profile.HazardScale, (profile.Suit ? 1 : 0) | (profile.Responder ? 2 : 0) | (profile.Eva ? 4 : 0) | (profile.Robot ? 8 : 0) | (profile.NoCrawl ? 16 : 0), bar);
        if (_floods.TryGetValue(key, out var e) && e.Version == version && SameFear(e.Fear, profile.Fear))
        {
            FloodHits++;
            Prof.Lap("path.Flood(캐시)", pf);
            return e.Field;
        }
        var field = FloodCore(start, profile);
        if (_floods.Count > 512) _floods.Clear();
        _floods[key] = new FloodEntry { Version = version, Fear = profile.Fear == null ? null : (float[])profile.Fear.Clone(), Field = field };
        FloodMisses++;
        Prof.Lap("path.Flood", pf);
        return field;
    }

    private static bool SameFear(float[]? a, float[]? b) =>
        a == null ? b == null || b.All(x => x <= 0.05f) : b == null ? a.All(x => x <= 0.05f) : a.AsSpan().SequenceEqual(b);

    // v14.2 거리장 계산을 빠르게: 방·문·칸의 성질을 계산마다 한 번 배열로 펴 두고 (같은 식, 같은 값), 큐를 다시 쓴다.
    private readonly PriorityQueue<int, int> _open = new();
    private bool[] _pass = Array.Empty<bool>();
    private int[] _roomAdd = Array.Empty<int>(), _doorAdd = Array.Empty<int>();
    private bool[] _roomBlocked = Array.Empty<bool>(), _doorBlocked = Array.Empty<bool>();

    private DistanceField FloodCore(Cell start, PathProfile profile)
    {
        if (profile.HazardScale == 0f) profile = PathProfile.Default;
        var grid = _ship.Grid;
        var cost = new int[_n];
        Array.Fill(cost, -1);
        if (!grid.InBounds(start)) return new DistanceField(grid, cost);

        int s = grid.Index(start);
        int startRoom = _room[s];
        // 칸: 지나갈 수 있나 (Passable과 같다)
        if (_pass.Length != _n) _pass = new bool[_n];
        for (int i = 0; i < _n; i++) _pass[i] = Passable(i, profile);
        // 방: 위험·공포 비용, 숨 못 쉬는 방 (StepCost·CanStep과 같은 식)
        int nr = _ship.Rooms.Count;
        if (_roomAdd.Length < nr) { _roomAdd = new int[nr]; _roomBlocked = new bool[nr]; }
        for (int r = 0; r < nr; r++)
        {
            var room = _ship.Rooms[r];
            int add = 0;
            int hazard = room.HazardCost;
            if (hazard > 0) add += (int)(hazard * profile.HazardScale);
            if (profile.Fear is { } fear && r < fear.Length && fear[r] > 0.05f)
                add += (int)(fear[r] * PathProfile.FearCost * (profile.Responder ? 0.4f : 1f));
            _roomAdd[r] = add;
            _roomBlocked[r] = !profile.Suit && !profile.Robot && r != startRoom && room.Unbreathable;
        }
        // 문: 잠긴 격벽을 못 지나는가, 전기 없는 문·잠긴 문 비용
        int nd0 = _ship.Doors.Count;
        if (_doorAdd.Length < nd0) { _doorAdd = new int[nd0]; _doorBlocked = new bool[nd0]; }
        for (int d = 0; d < nd0; d++)
        {
            var door = _ship.Doors[d];
            int add = 0;
            if (!door.Powered) add += ManualDoorPenalty;
            if (door.Locked) add += ManualDoorPenalty * 2;
            if (Barred(d)) add += BarredDoorPenalty;
            _doorAdd[d] = add;
            bool blocked = false;
            if (door.Locked)
            {
                bool leaving = !profile.Robot && ((door.RoomA?.Id ?? -1) == startRoom || (door.RoomB?.Id ?? -1) == startRoom); // v16.20b 로봇은 잠긴 격벽을 못 연다 (나가는 길이어도)
                bool sealedOff = (door.RoomA?.Abandoned ?? false) || (door.RoomB?.Abandoned ?? false);
                blocked = !leaving && !profile.Suit && (sealedOff || !profile.Responder);
                if (door.Welded && !profile.Suit) blocked = true; // 용접한 격벽은 비상 개방이 안 된다 (잘라야 한다 — 우주복 입고)
            }
            _doorBlocked[d] = blocked;
        }
        float cellScale = (profile.Suit ? 0.5f : 1f);
        var open = _open;
        open.Clear();
        cost[s] = 0;
        open.Enqueue(s, 0);
        while (open.TryDequeue(out int cur, out int dist))
        {
            if (dist > cost[cur]) continue;
            int dcur = _door[cur];
            for (int k = 0; k < 8; k++)
            {
                int ni = cur + _offsets[k];
                // ── CanStep ──
                if (ni < 0 || ni >= _n || !_pass[ni]) continue;
                int dr = _door[ni];
                if (dr >= 0 && _doorBlocked[dr]) continue;
                int r = _room[ni];
                if (r >= 0 && _roomBlocked[r]) continue;
                if (k >= 4)
                {
                    if (dcur >= 0 || dr >= 0) continue;
                    int a = cur + _dx[k];
                    int b = cur + _dy[k] * _w;
                    if (!_pass[a] || !_pass[b] || _door[a] >= 0 || _door[b] >= 0) continue;
                }
                // ── StepCost (goal 없음) ──
                int step = k < 4 ? Straight : Diagonal;
                if (_space[ni]) step += PathProfile.SpaceCost;
                if (_furniture[ni]) step += FurniturePenalty;
                if (r >= 0) step += _roomAdd[r];
                if (dr >= 0) step += _doorAdd[dr];
                int h = CellHazard[ni];
                if (h > 0) step += (int)(h * cellScale * profile.HazardScale);
                step += CellBody[ni]; // v16.3
                int nd = dist + step;
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
            bool leaving = !profile.Robot && ((door.RoomA?.Id ?? -1) == startRoom || (door.RoomB?.Id ?? -1) == startRoom); // v16.20b 로봇은 잠긴 격벽을 못 연다 (나가는 길이어도)
            bool sealedOff = (door.RoomA?.Abandoned ?? false) || (door.RoomB?.Abandoned ?? false);
            if (!leaving && !profile.Suit && (sealedOff || !profile.Responder)) return false;
            if (door.Welded && !profile.Suit) return false; // 용접한 격벽은 비상 개방이 안 된다
        }
        if (!profile.Suit && !profile.Robot) // v10.10: 로봇은 숨을 쉬지 않는다 (진공도 지나간다 — 잠긴 격벽은 못 연다)
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
        if (to == _avoid) cost += 400;
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
        if (d >= 0 && Barred(d)) cost += BarredDoorPenalty;
        int h = CellHazard[to];
        if (h > 0) cost += (int)(h * (profile.Suit ? 0.5f : 1f) * profile.HazardScale);
        cost += CellBody[to]; // v16.3
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
