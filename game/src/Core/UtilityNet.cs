using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>배 전체를 도는 망의 종류.</summary>
public enum NetKind { Power, Water, Air, Data } // v12.3 데이터선: 감지기·컴퓨터·문 제어

/// <summary>
/// 망의 한 토막: 방 안의 분기점 ↔ 문 앞, 또는 문을 건너 두 방의 문 앞 사이.
/// 끊기면(30% 아래) 그 너머로 전기·물·공기가 가지 않는다 — 다른 길로 돌아갈 수 있으면 간다.
/// </summary>
public sealed class NetLink
{
    public int Id { get; init; }
    public NetKind Kind { get; init; }
    public string Key { get; init; } = "";
    /// <summary>이어 주는 두 점 (망의 마디 번호).</summary>
    public int NodeA { get; init; }
    public int NodeB { get; init; }
    public Room Room { get; init; } = null!;   // 이 토막이 지나는 방 (문을 건너는 토막이면 한쪽)
    public Door? Door { get; init; }
    public List<Cell> Cells { get; init; } = new();
    public float Integrity { get; set; } = 1f;
    public bool Temp { get; set; }             // 임시로 이었다 (평온해지면 제대로)
    public string? Cause { get; set; }
    /// <summary>v14.8 임시로 이은 사람 (접촉 저항으로 달아오르면 그 사람이 기억한다).</summary>
    public int SplicedBy { get; set; } = -1;
    public bool Cut => Integrity < 0.3f;
    /// <summary>v12.2 인과 사슬: 이 토막이 끊긴 고리.</summary>
    public int Node { get; set; } = -1;
}

public sealed class NetStats
{
    public int Cuts;
    public int Repairs;
    public int TempRepairs;
    public int Rings;
    public int Blackouts; // 간선이 끊겨 방 셋 넘게 정전된 일 (보조 간선을 깔 까닭)
    public override string ToString() => $"끊김 {Cuts} · 다시 이음 {Repairs}(임시 {TempRepairs}) · 보조 간선 {Rings} · 간선 정전 {Blackouts}";
}

/// <summary>
/// 배 전체 유틸리티 망: 전력 간선(배전실에서), 급수(정수기가 있는 방에서), 환기 덕트(산소 발생기가 있는 방에서).
/// 방마다 분기점이 있고 문마다 간선이 나가 옆 방 분기점으로 이어진다. 한 토막이 끊기면 그 너머 방들만 정전·단수·환기 끊김.
/// 문이 여럿인 방(큰 배의 통로)은 다른 길로 돌아간다. 폭발·불·운석·방 분리가 가까운 토막을 상하게 하고, 사람이 다시 잇는다.
/// 단수: 재배대가 마르고, 주방이 요리를 못 하고, 산소 발생기가 전해할 물이 없다. 환기 끊김: 그 방은 산소가 안 들고 CO₂·연기가 안 빠진다.
/// </summary>
public sealed partial class UtilityNet
{
    private readonly World _w;
    private List<NetLink>[][] _adj = Array.Empty<List<NetLink>[]>();
    private readonly List<(int node, Room room)> _hubs = new();
    private int _nodes;
    private readonly Dictionary<int, bool> _powerFed = new();
    public List<NetLink> Links { get; } = new();
    public NetStats Stats { get; } = new();

    /// <summary>v12.3 보조 간선(링): 문을 거치지 않고 선체 속을 따라 두 방을 직접 잇는다 (종류, 방, 방).</summary>
    public List<(NetKind kind, int from, int to)> Rings { get; } = new();
    public int Version { get; private set; }

    public UtilityNet(World w) => _w = w;

    public static string Name(NetKind k) => k switch { NetKind.Power => "전력 간선", NetKind.Water => "급수관", NetKind.Data => "데이터선", _ => "환기 덕트" };

    /// <summary>물을 쓰는 방 (단수가 문제 되는 곳).</summary>
    public static bool NeedsWater(Room r) => r.Type is RoomType.Hydroponics or RoomType.Galley or RoomType.Medbay or RoomType.Quarters or RoomType.LifeSupport
        || r.Furniture.Any(f => f.Type is FurnitureType.GrowBed or FurnitureType.Stove or FurnitureType.OxygenGenerator);

    // ─────────────────────────────── 짓기 ───────────────────────────────

    // v14.2 구조 지문: 문(이어진 두 방) · 떨어지거나 합쳐진 방 · 보조 간선 — 숫자 배열로 비교한다 (예전엔 문자열을 틱마다 만들었다)
    private readonly List<int> _sigBuf = new();
    private int[] _sig = Array.Empty<int>();

    private bool SignatureChanged()
    {
        var b = _sigBuf;
        b.Clear();
        foreach (var d in _w.Ship.Doors)
            if (!d.Removed && !d.IsExternal && d.RoomA != null && d.RoomB != null) { b.Add(d.Id); b.Add(d.RoomA.Id); b.Add(d.RoomB.Id); }
        b.Add(-1);
        foreach (var r in _w.Ship.Rooms) if (r.Detached || r.Merged) b.Add(r.Id);
        b.Add(-2);
        foreach (var (k, from, to) in Rings) { b.Add((int)k); b.Add(from); b.Add(to); }
        if (b.Count == _sig.Length && System.Runtime.InteropServices.CollectionsMarshal.AsSpan(b).SequenceEqual(_sig)) return false;
        _sig = b.ToArray();
        return true;
    }

    /// <summary>문·방이 바뀌었으면 망을 다시 짠다 (남아 있는 토막의 상태는 이어받는다).</summary>
    public void EnsureBuilt()
    {
        if (!SignatureChanged()) return;
        var old = Links.ToDictionary(l => l.Key);
        Links.Clear();
        _hubs.Clear();
        _nodes = 0;
        var ship = _w.Ship;
        var hubOf = new Dictionary<int, (int node, Cell cell)>();
        foreach (var r in ship.Rooms.Where(r => !r.Detached && !r.Merged && r.Cells.Count > 0))
        {
            var hub = r.Cells.Where(c => ship.Grid.Kind(c) == TileKind.Floor).OrderBy(c => (c.Center - r.Center).LengthSquared()).ThenBy(c => c.Y).ThenBy(c => c.X).FirstOrDefault();
            if (hub == default) continue;
            hubOf[r.Id] = (_nodes, hub);
            _hubs.Add((_nodes, r));
            _nodes++;
        }
        int id = 0;
        foreach (var d in ship.Doors.Where(d => !d.Removed && !d.IsExternal && d.RoomA != null && d.RoomB != null).OrderBy(d => d.Id))
        {
            var a = d.RoomA!;
            var b = d.RoomB!;
            if (!hubOf.ContainsKey(a.Id) || !hubOf.ContainsKey(b.Id)) continue;
            int sideA = _nodes++, sideB = _nodes++;
            var inA = Inside(d, a);
            var inB = Inside(d, b);
            if (inA == null || inB == null) continue;
            foreach (NetKind k in Enum.GetValues<NetKind>())
            {
                // 방 분기점 → 문 앞 (방 안), 문을 건너, 문 앞 → 옆 방 분기점
                Add(k, $"{k}:{d.Id}:A", hubOf[a.Id].node, sideA, a, d, Route(a, hubOf[a.Id].cell, inA.Value));
                Add(k, $"{k}:{d.Id}:X", sideA, sideB, a, d, new List<Cell> { inA.Value, d.Cell, inB.Value });
                Add(k, $"{k}:{d.Id}:B", sideB, hubOf[b.Id].node, b, d, Route(b, inB.Value, hubOf[b.Id].cell));
            }
        }
        // v12.3 보조 간선: 선체 속을 따라 곧게 (배전실 문 앞이 끊겨도 반대쪽으로 들어온다)
        foreach (var (k, from, to) in Rings)
        {
            if (!hubOf.TryGetValue(from, out var ha) || !hubOf.TryGetValue(to, out var hb)) continue;
            var room = ship.Rooms.First(r => r.Id == from);
            Add(k, $"{k}:ring:{from}-{to}", ha.node, hb.node, room, null, Line(ha.cell, hb.cell));
        }
        foreach (var l in Links)
            if (old.TryGetValue(l.Key, out var o)) { l.Integrity = o.Integrity; l.Temp = o.Temp; l.Cause = o.Cause; l.Node = o.Node; l.SplicedBy = o.SplicedBy; }
        // v14.2 마디마다 닿는 토막 (종류별) — 도달 계산이 틱마다 목록을 새로 만들지 않게
        _adj = new List<NetLink>[Enum.GetValues<NetKind>().Length][];
        for (int k = 0; k < _adj.Length; k++)
        {
            _adj[k] = new List<NetLink>[_nodes];
            for (int n = 0; n < _nodes; n++) _adj[k][n] = new List<NetLink>();
        }
        foreach (var l in Links)
        {
            _adj[(int)l.Kind][l.NodeA].Add(l);
            if (l.NodeB != l.NodeA) _adj[(int)l.Kind][l.NodeB].Add(l);
        }
        Version++;

        void Add(NetKind k, string key, int na, int nb, Room room, Door? door, List<Cell> cells) =>
            Links.Add(new NetLink { Id = id++, Kind = k, Key = key, NodeA = na, NodeB = nb, Room = room, Door = door, Cells = cells });
    }

    /// <summary>두 칸 사이 곧은 줄 (보조 간선이 지나는 길 — 벽 속이어도 된다).</summary>
    private static List<Cell> Line(Cell a, Cell b)
    {
        var cells = new List<Cell>();
        int n = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        for (int i = 0; i <= n; i++)
        {
            float t = n == 0 ? 0f : i / (float)n;
            var c = new Cell((int)MathF.Round(a.X + (b.X - a.X) * t), (int)MathF.Round(a.Y + (b.Y - a.Y) * t));
            if (cells.Count == 0 || cells[^1] != c) cells.Add(c);
        }
        return cells;
    }

    /// <summary>보조 간선을 깐다 (다음 시스템 틱부터 망이 다시 짜인다).</summary>
    public bool AddRing(NetKind k, Room from, Room to)
    {
        if (from == to || Rings.Any(r => r.kind == k && (r.from == from.Id && r.to == to.Id || r.from == to.Id && r.to == from.Id))) return false;
        Rings.Add((k, from.Id, to.Id));
        Stats.Rings++;
        EnsureBuilt();
        Update(0f);
        return true;
    }

    /// <summary>보조 간선을 받을 핵심 방 (공급원 방에서 먼 순): 생명유지실 → 함교 → 의무실 → 냉각실.</summary>
    public List<Room> RingTargets(NetKind k)
    {
        var src = Sources(k).FirstOrDefault();
        if (src == null) return new();
        var have = Rings.Where(r => r.kind == k).SelectMany(r => new[] { r.from, r.to }).ToHashSet();
        return _w.Ship.LiveRooms.Where(r => r != src && !r.Detached && !have.Contains(r.Id)
                && r.Type is RoomType.LifeSupport or RoomType.Bridge or RoomType.Medbay or RoomType.Cooling)
            .OrderBy(r => r.Type switch { RoomType.LifeSupport => 0, RoomType.Bridge => 1, RoomType.Cooling => 2, _ => 3 }).ThenBy(r => r.Id).ToList();
    }

    public Room? SourceRoom(NetKind k) => Sources(k).FirstOrDefault();

    /// <summary>중형 이상 배는 처음부터 보조 간선이 있다 (전력: 생명유지실·함교·냉각실 · 데이터: 생명유지실).</summary>
    public void SeedRings(int designCrew)
    {
        if (designCrew < 12) return;
        EnsureBuilt();
        if (SourceRoom(NetKind.Power) is Room p) foreach (var r in RingTargets(NetKind.Power).Take(designCrew >= 20 ? 3 : 2)) Rings.Add((NetKind.Power, p.Id, r.Id));
        if (SourceRoom(NetKind.Data) is Room d) foreach (var r in RingTargets(NetKind.Data).Take(1)) Rings.Add((NetKind.Data, d.Id, r.Id));
        _sig = Array.Empty<int>(); // 다시 짠다
        EnsureBuilt();
    }

    private Cell? Inside(Door d, Room r)
    {
        foreach (var dir in Cell.Dirs4)
        {
            var c = d.Cell + dir;
            if (_w.Ship.RoomAt(c) == r && _w.Ship.Grid.Kind(c) == TileKind.Floor) return c;
        }
        return null;
    }

    /// <summary>방 안의 길 (가구를 가리지 않고 바닥을 따라 — 천장·바닥 밑을 지나는 셈).</summary>
    private List<Cell> Route(Room r, Cell from, Cell to)
    {
        var ship = _w.Ship;
        var prev = new Dictionary<Cell, Cell> { [from] = from };
        var q = new Queue<Cell>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (c == to) break;
            foreach (var d in Cell.Dirs4)
            {
                var n = c + d;
                if (prev.ContainsKey(n) || ship.RoomAt(n) != r || ship.Grid.Kind(n) != TileKind.Floor) continue;
                prev[n] = c;
                q.Enqueue(n);
            }
        }
        var path = new List<Cell>();
        if (!prev.ContainsKey(to)) return new List<Cell> { from, to };
        for (var c = to; ; c = prev[c]) { path.Add(c); if (c == from) break; }
        path.Reverse();
        return path;
    }

    // ─────────────────────────────── 이어진 곳 ───────────────────────────────

    /// <summary>시스템 틱: 망을 다시 짜고, 공급원에서 닿는 방을 표시한다.</summary>
    /// <summary>공급원에서 닿는 분기점들 (assumeFixed 토막은 이어진 셈 치고) — 상태를 바꾸지 않는다.</summary>
    private HashSet<int> Reach(NetKind k, List<Room> sources, IReadOnlyCollection<NetLink>? assumeFixed = null)
    {
        var reach = new HashSet<int>();
        var q = new Queue<int>();
        foreach (var r in sources)
        {
            var hub = _hubs.FirstOrDefault(h => h.room == r);
            if (hub.room == null) continue;
            if (reach.Add(hub.node)) q.Enqueue(hub.node);
        }
        var adj = _adj[(int)k];
        while (q.Count > 0)
        {
            int n = q.Dequeue();
            if (n < 0 || n >= adj.Length) continue;
            foreach (var l in adj[n])
            {
                if (l.Cut && (assumeFixed == null || !assumeFixed.Contains(l)) || l.Room.Detached) continue;
                int other = l.NodeA == n ? l.NodeB : l.NodeA;
                if (reach.Add(other)) q.Enqueue(other);
            }
        }
        return reach;
    }

    // 통합 성능: 시스템 틱의 Reach — 집합 대신 표시 배열(판 번호)과 배열 큐 (닿는 곳은 같다)
    private int[] _mark = Array.Empty<int>(), _bfs = Array.Empty<int>();
    private int _markGen;
    private void ReachMark(NetKind k, List<Room> sources)
    {
        int size = _nodes;
        foreach (var (node, _) in _hubs) if (node + 1 > size) size = node + 1;
        if (_mark.Length < size) { _mark = new int[size]; _bfs = new int[size]; _markGen = 0; }
        if (++_markGen == int.MaxValue) { Array.Clear(_mark); _markGen = 1; }
        int gen = _markGen, head = 0, tail = 0;
        foreach (var r in sources)
        {
            int hub = -1;
            foreach (var h in _hubs) if (h.room == r) { hub = h.node; break; }
            if (hub < 0 || _mark[hub] == gen) continue;
            _mark[hub] = gen; _bfs[tail++] = hub;
        }
        var adj = _adj[(int)k];
        while (head < tail)
        {
            int n = _bfs[head++];
            if (n < 0 || n >= adj.Length) continue;
            foreach (var l in adj[n])
            {
                if (l.Cut || l.Room.Detached) continue;
                int other = l.NodeA == n ? l.NodeB : l.NodeA;
                if (_mark[other] != gen) { _mark[other] = gen; _bfs[tail++] = other; }
            }
        }
    }

    public void Update(float dt)
    {
        var w = _w;
        EnsureBuilt();
        var ship = w.Ship;
        foreach (NetKind k in Enum.GetValues<NetKind>())
        {
            var sources = Sources(k);
            ReachMark(k, sources);
            int gen = _markGen;
            int newlyDark = 0;
            foreach (var (node, room) in _hubs)
            {
                bool fed = node >= 0 && node < _mark.Length && _mark[node] == gen || sources.Count == 0; // 공급원이 없는 배(시험용)는 예전처럼
                if (k == NetKind.Power)
                {
                    bool was = !_powerFed.TryGetValue(room.Id, out var pf) || pf;
                    if (was && !fed) newlyDark++;
                    _powerFed[room.Id] = fed;
                }
                switch (k)
                {
                    case NetKind.Power: room.PowerLinked = fed; break;
                    case NetKind.Water: room.WaterLinked = fed; break;
                    case NetKind.Air: room.DuctLinked = fed; break;
                    case NetKind.Data: room.DataLinked = fed; break;
                }
            }
            if (newlyDark >= 3) Stats.Blackouts++;
        }
    }

    /// <summary>공급원: 전력은 배전반이 있는 방, 급수는 돌아가는 정수기가 있는 방, 환기는 산소 발생기가 있는 방 (없으면 생명유지실).</summary>
    private List<Room> Sources(NetKind k)
    {
        var ship = _w.Ship;
        return k switch
        {
            NetKind.Power => ship.FurnitureOf(FurnitureType.PowerPanel).Select(f => f.Room).Distinct().ToList(),
            NetKind.Water => ship.FurnitureOf(FurnitureType.WaterRecycler).Select(f => f.Room).Distinct().ToList(),
            NetKind.Data => ship.FurnitureOf(FurnitureType.MainComputer).Select(f => f.Room).Distinct().ToList(),
            _ => ship.FurnitureOf(FurnitureType.OxygenGenerator).Select(f => f.Room).Concat(ship.RoomsOf(RoomType.LifeSupport)).Distinct().ToList(),
        };
    }

    // ─────────────────────────────── 상함 ───────────────────────────────

    /// <summary>한 점 둘레의 토막을 상하게 한다 (폭발·운석·불). 종류마다 약한 데가 다르다 (전선은 불에, 관은 충격에).</summary>
    public void DamageNear(Cell at, float radius, float amount, string cause, bool fire = false)
    {
        EnsureBuilt();
        foreach (var l in Links)
        {
            float best = float.MaxValue;
            foreach (var c in l.Cells) { float d = (c.Center - at.Center).LengthSquared(); if (d < best) best = d; }
            float dist = MathF.Sqrt(best);
            if (dist > radius) continue;
            float k = amount * (1f - dist / (radius + 0.5f)) * Durability.NetHit; // v16.19 전선관 · 배관 받침
            k *= fire ? l.Kind switch { NetKind.Power => 1.3f, NetKind.Data => 1.1f, NetKind.Air => 0.6f, _ => 0.25f } : l.Kind switch { NetKind.Water => 1.2f, NetKind.Air => 1f, _ => 0.9f };
            Hurt(l, k, cause);
        }
    }

    public void Hurt(NetLink l, float k, string cause)
    {
        bool was = l.Cut;
        l.Integrity = MathF.Max(0f, l.Integrity - k);
        if (!was && l.Cut)
        {
            l.Cause = cause;
            Stats.Cuts++;
            MarkLog.Add(l.Room.Marks, _w.Tick, $"{Name(l.Kind)} 끊김 ({cause})");
            _w.Log.Add(_w.Tick, LogKind.Warning, $"{l.Room.Name} {Ko.IGa(Name(l.Kind))} 끊겼다 ({cause})");
            _w.Board.RequestScan();
            _w.Causes.OnCut(l); // v12.2 인과 사슬
        }
    }

    /// <summary>방이 떨어져 나갔다: 그 방을 지나던 토막은 끊긴다.</summary>
    public void OnDetach(Room room)
    {
        EnsureBuilt();
        foreach (var l in Links.Where(l => l.Room == room || l.Door != null && (l.Door.RoomA == room || l.Door.RoomB == room)))
            if (!l.Cut) Hurt(l, 1f, $"{room.Name} 분리");
    }

    /// <summary>끊긴 토막이 고쳐지면 다시 닿는 방들 (이 토막만 이으면).</summary>
    public List<Room> Downstream(NetLink link) => Downstream(new[] { link });

    /// <summary>끊긴 토막들(다발)을 다 이으면 다시 닿는 방들.</summary>
    public List<Room> Downstream(IReadOnlyCollection<NetLink> links)
    {
        EnsureBuilt();
        var link = links.FirstOrDefault(l => l.Cut);
        if (link == null) return new List<Room>();
        var sources = Sources(link.Kind);
        if (sources.Count == 0) return new List<Room>();
        var now = Reach(link.Kind, sources);
        var fixedReach = Reach(link.Kind, sources, links);
        return _hubs.Where(h => fixedReach.Contains(h.node) && !now.Contains(h.node)).Select(h => h.room).ToList();
    }

    /// <summary>
    /// 강화: 이 토막과 한 다발 — 같은 방 · 같은 망 · 같은 까닭으로 끊긴 토막들.
    /// 방 분기점에 모인 간선(큰 통로는 문마다 수십 가닥)은 파편 한 번에 함께 끊긴다 — 잇는 것도 분기점에서 한 번에.
    /// </summary>
    public List<NetLink> Bundle(NetLink l) =>
        l.Cut ? Links.Where(x => x.Cut && x.Room == l.Room && x.Kind == l.Kind && x.Cause == l.Cause).ToList() : new List<NetLink> { l };

    /// <summary>토막(끊긴 다발이면 다발 전체)을 잇는다. 이은 토막 수.</summary>
    public int Mend(NetLink l, float integrity, bool temp, int by)
    {
        var group = Bundle(l);
        foreach (var x in group)
        {
            x.Integrity = integrity;
            x.SplicedBy = by;
            x.Temp = temp;
            if (temp) Stats.TempRepairs++; else Stats.Repairs++;
        }
        Update(0f);
        return group.Count;
    }

    public static bool Fed(NetKind k, Room r) => k switch { NetKind.Power => r.PowerLinked, NetKind.Water => r.WaterLinked, NetKind.Data => r.DataLinked, _ => r.DuctLinked };

    public NetLink? LinkAt(Cell c) => Links.FirstOrDefault(l => l.Cells.Contains(c));
}

public sealed partial class WorkBoard
{
    /// <summary>끊긴·상한 망 토막을 다시 잇는다 (끊겨서 끊긴 방이 많을수록, 핵심 방일수록 급하다).</summary>
    private void ScanNet(Poster post)
    {
        var w = _world;
        var net = w.Net;
        var bundled = new HashSet<(int, NetKind, string?)>();
        int posted = 0;
        foreach (var l in net.Links.Where(l => l.Integrity < 0.55f || l.Temp && (Crisis.Level(w) < CrisisLevel.Emergency || w.Flow.Hot(l))).OrderBy(l => l.Integrity))
        {
            if (posted >= 10) break;
            if (l.Room.Detached || l.Room.Abandoned || l.Room.OffLimits) continue;
            if (l.Cut && !bundled.Add((l.Room.Id, l.Kind, l.Cause))) continue; // 강화: 한 다발은 일감 하나로 (분기점에서 한 번에 잇는다)
            posted++;
            var group = l.Cut ? net.Bundle(l) : null;
            var spot = l.Cells.FirstOrDefault(c => w.Ship.IsWalkable(c));
            if (spot == default) spot = l.Cells[l.Cells.Count / 2];
            var down = group != null ? net.Downstream(group) : new List<Room>();
            bool vital = down.Any(r => r.Type is RoomType.LifeSupport or RoomType.Reactor or RoomType.Cooling or RoomType.Medbay or RoomType.Bridge or RoomType.Power or RoomType.Hydroponics);
            float u = l.Cut ? (l.Kind == NetKind.Power ? 0.85f : l.Kind == NetKind.Air ? 0.75f : l.Kind == NetKind.Data ? 0.55f : 0.6f) + (vital ? 0.2f : 0f) + 0.03f * down.Count : l.Temp ? (w.Flow.Hot(l) ? 0.75f : 0.3f) : 0.4f; // v14.8 달아오른 임시 이음은 급하다
            string detail = l.Cut
                ? $"{UtilityNet.Name(l.Kind)} 끊김 ({l.Cause}){(group!.Count > 1 ? $" · {group.Count}가닥 한 다발" : "")} — {(down.Count > 0 ? string.Join("·", down.Select(r => r.Name)) + (l.Kind == NetKind.Power ? " 정전" : l.Kind == NetKind.Water ? " 단수" : l.Kind == NetKind.Data ? " 감지기·원격 제어 끊김" : " 환기 끊김") : "다른 길로 돈다")}"
                : l.Temp ? (w.Flow.Hot(l) ? $"임시로 이은 {Ko.IGa(UtilityNet.Name(l.Kind))} 달아오른다 (접촉 저항) → 제대로 다시" : $"임시로 이은 {UtilityNet.Name(l.Kind)} → 제대로 다시") : $"{UtilityNet.Name(l.Kind)} 상함 ({l.Integrity * 100:0}%)";
            post(WorkKind.RepairNet, WorkTarget.AtCell(spot, l.Room), MathF.Min(1.15f, u), l.Kind is NetKind.Power or NetKind.Data ? Skill.Electrical : Skill.Mechanics, detail, circuit: l.Id);
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>망 토막을 다시 잇는다: 재료(전선=케이블, 급수관=금속판·실링폼, 덕트=금속판)가 있으면 제대로, 없고 급하면 임시로.</summary>
    private static Job? RepairNet(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var l = w.Net.Links.FirstOrDefault(x => x.Id == o.Circuit);
        if (l == null) { w.Board.Close(o); return null; }
        var item = l.Kind switch
        {
            NetKind.Power or NetKind.Data => ItemKind.Cable,
            NetKind.Water => w.Ship.CountStored(ItemKind.Sealant) > 0 ? ItemKind.Sealant : ItemKind.Plate,
            _ => ItemKind.Plate,
        };
        bool have = w.Ship.CountStored(item) > 0;
        bool temp = !have;
        if (temp && !l.Cut) { blocked = $"{ItemKinds.Name(item)} 없음"; return null; }
        var toils = have ? FetchAll(c, w, dist, new[] { (item, 1) }) : Plans.DropOff(c, w, dist);
        if (toils == null) { temp = true; toils = Plans.DropOff(c, w, dist); }
        toils.Add(new GotoToil(at));
        int strands = w.Net.Bundle(l).Count;
        toils.Add(new WorkToil((temp ? 0.25f : 0.5f) + 0.03f * Math.Min(10, strands - 1), o.Skill, at.Center) { Resume = o }); // 강화: 다발이면 조금 더 걸린다
        toils.Add(new DoToil((cm, world) =>
        {
            if (!temp && !UseAll(cm, new[] { (item, 1) })) return false;
            world.Board.Close(o);
            bool wasCut = l.Cut;
            bool tape = temp && l.Kind is NetKind.Power or NetKind.Data && ItemsV15.Use(world, ItemKind.Tape); // v15 절연 테이프로 감으면 덜 달아오른다
            int n = world.Net.Mend(l, temp ? (tape ? 0.72f : 0.6f) : 1f, temp, temp ? cm.Id : -1); // v14.8 · 강화: 끊긴 다발은 한 번에
            MarkLog.Add(l.Room.Marks, world.Tick, $"{cm.Name}: {UtilityNet.Name(l.Kind)} {(temp ? "임시로 이음" : "다시 이음")}");
            world.Log.Add(world.Tick, LogKind.Work, $"{l.Room.Name} {Ko.EulReul(UtilityNet.Name(l.Kind))}{(n > 1 ? $" {n}가닥" : "")} {(temp ? "임시로 이었다 — 나중에 제대로" : "다시 이었다")}", cm.Id);
            if (wasCut) world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 끊긴 {l.Room.Name} {Ko.EulReul(UtilityNet.Name(l.Kind))} {(temp ? "임시로 " : "")}이었다", l.Room, new[] { cm });
            return true;
        }));
        return Wrap(a, o, c, w, temp ? "임시로 잇기" : "망 잇기", toils, $"{l.Room.Name} {UtilityNet.Name(l.Kind)} {(temp ? "임시로 잇기" : "다시 잇기")}");
    }
}
