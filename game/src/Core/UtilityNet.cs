using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>배 전체를 도는 망의 종류.</summary>
public enum NetKind { Power, Water, Air }

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
    public bool Cut => Integrity < 0.3f;
    /// <summary>v12.2 인과 사슬: 이 토막이 끊긴 고리.</summary>
    public int Node { get; set; } = -1;
}

public sealed class NetStats
{
    public int Cuts;
    public int Repairs;
    public int TempRepairs;
    public override string ToString() => $"끊김 {Cuts} · 다시 이음 {Repairs}(임시 {TempRepairs})";
}

/// <summary>
/// 배 전체 유틸리티 망: 전력 간선(배전실에서), 급수(정수기가 있는 방에서), 환기 덕트(산소 발생기가 있는 방에서).
/// 방마다 분기점이 있고 문마다 간선이 나가 옆 방 분기점으로 이어진다. 한 토막이 끊기면 그 너머 방들만 정전·단수·환기 끊김.
/// 문이 여럿인 방(큰 배의 통로)은 다른 길로 돌아간다. 폭발·불·운석·방 분리가 가까운 토막을 상하게 하고, 사람이 다시 잇는다.
/// 단수: 재배대가 마르고, 주방이 요리를 못 하고, 산소 발생기가 전해할 물이 없다. 환기 끊김: 그 방은 산소가 안 들고 CO₂·연기가 안 빠진다.
/// </summary>
public sealed class UtilityNet
{
    private readonly World _w;
    private string _signature = "";
    private readonly List<(int node, Room room)> _hubs = new();
    private int _nodes;
    public List<NetLink> Links { get; } = new();
    public NetStats Stats { get; } = new();
    public int Version { get; private set; }

    public UtilityNet(World w) => _w = w;

    public static string Name(NetKind k) => k switch { NetKind.Power => "전력 간선", NetKind.Water => "급수관", _ => "환기 덕트" };

    /// <summary>물을 쓰는 방 (단수가 문제 되는 곳).</summary>
    public static bool NeedsWater(Room r) => r.Type is RoomType.Hydroponics or RoomType.Galley or RoomType.Medbay or RoomType.Quarters or RoomType.LifeSupport
        || r.Furniture.Any(f => f.Type is FurnitureType.GrowBed or FurnitureType.Stove or FurnitureType.OxygenGenerator);

    // ─────────────────────────────── 짓기 ───────────────────────────────

    private string Signature() =>
        string.Join(",", _w.Ship.Doors.Where(d => !d.Removed && !d.IsExternal && d.RoomA != null && d.RoomB != null).Select(d => $"{d.Id}:{d.RoomA!.Id}-{d.RoomB!.Id}"))
        + "|" + string.Join(",", _w.Ship.Rooms.Where(r => r.Detached || r.Merged).Select(r => r.Id));

    /// <summary>문·방이 바뀌었으면 망을 다시 짠다 (남아 있는 토막의 상태는 이어받는다).</summary>
    public void EnsureBuilt()
    {
        var sig = Signature();
        if (sig == _signature) return;
        _signature = sig;
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
        foreach (var l in Links)
            if (old.TryGetValue(l.Key, out var o)) { l.Integrity = o.Integrity; l.Temp = o.Temp; l.Cause = o.Cause; }
        Version++;

        void Add(NetKind k, string key, int na, int nb, Room room, Door door, List<Cell> cells) =>
            Links.Add(new NetLink { Id = id++, Kind = k, Key = key, NodeA = na, NodeB = nb, Room = room, Door = door, Cells = cells });
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
    public void Update(float dt)
    {
        var w = _w;
        EnsureBuilt();
        var ship = w.Ship;
        foreach (NetKind k in Enum.GetValues<NetKind>())
        {
            var sources = Sources(k);
            var reach = new HashSet<int>();
            var q = new Queue<int>();
            foreach (var r in sources)
            {
                var hub = _hubs.FirstOrDefault(h => h.room == r);
                if (hub.room == null) continue;
                if (reach.Add(hub.node)) q.Enqueue(hub.node);
            }
            var adj = Links.Where(l => l.Kind == k && !l.Cut && !l.Room.Detached).ToLookup(l => l.NodeA);
            var adjB = Links.Where(l => l.Kind == k && !l.Cut && !l.Room.Detached).ToLookup(l => l.NodeB);
            while (q.Count > 0)
            {
                int n = q.Dequeue();
                foreach (var l in adj[n]) if (reach.Add(l.NodeB)) q.Enqueue(l.NodeB);
                foreach (var l in adjB[n]) if (reach.Add(l.NodeA)) q.Enqueue(l.NodeA);
            }
            foreach (var (node, room) in _hubs)
            {
                bool fed = reach.Contains(node) || sources.Count == 0; // 공급원이 없는 배(시험용)는 예전처럼
                switch (k)
                {
                    case NetKind.Power: room.PowerLinked = fed; break;
                    case NetKind.Water: room.WaterLinked = fed; break;
                    case NetKind.Air: room.DuctLinked = fed; break;
                }
            }
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
            float k = amount * (1f - dist / (radius + 0.5f));
            k *= fire ? l.Kind switch { NetKind.Power => 1.3f, NetKind.Air => 0.6f, _ => 0.25f } : l.Kind switch { NetKind.Water => 1.2f, NetKind.Air => 1f, _ => 0.9f };
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
            _w.Log.Add(_w.Tick, LogKind.Warning, $"{l.Room.Name} {Name(l.Kind)}이(가) 끊겼다 ({cause})");
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
    public List<Room> Downstream(NetLink link)
    {
        var before = new HashSet<Room>(_hubs.Where(h => Fed(link.Kind, h.room)).Select(h => h.room));
        float keep = link.Integrity;
        link.Integrity = 1f;
        Update(0f);
        var after = _hubs.Where(h => Fed(link.Kind, h.room)).Select(h => h.room).ToList();
        link.Integrity = keep;
        Update(0f);
        return after.Where(r => !before.Contains(r)).ToList();
    }

    public static bool Fed(NetKind k, Room r) => k switch { NetKind.Power => r.PowerLinked, NetKind.Water => r.WaterLinked, _ => r.DuctLinked };

    public NetLink? LinkAt(Cell c) => Links.FirstOrDefault(l => l.Cells.Contains(c));
}

public sealed partial class WorkBoard
{
    /// <summary>끊긴·상한 망 토막을 다시 잇는다 (끊겨서 끊긴 방이 많을수록, 핵심 방일수록 급하다).</summary>
    private void ScanNet(Poster post)
    {
        var w = _world;
        var net = w.Net;
        foreach (var l in net.Links.Where(l => l.Integrity < 0.55f || l.Temp && Crisis.Level(w) < CrisisLevel.Emergency).OrderBy(l => l.Integrity).Take(10))
        {
            if (l.Room.Detached || l.Room.Abandoned || l.Room.OffLimits) continue;
            var spot = l.Cells.FirstOrDefault(c => w.Ship.IsWalkable(c));
            if (spot == default) spot = l.Cells[l.Cells.Count / 2];
            var down = l.Cut ? net.Downstream(l) : new List<Room>();
            bool vital = down.Any(r => r.Type is RoomType.LifeSupport or RoomType.Reactor or RoomType.Cooling or RoomType.Medbay or RoomType.Bridge or RoomType.Power or RoomType.Hydroponics);
            float u = l.Cut ? (l.Kind == NetKind.Power ? 0.85f : l.Kind == NetKind.Air ? 0.75f : 0.6f) + (vital ? 0.2f : 0f) + 0.03f * down.Count : l.Temp ? 0.3f : 0.4f;
            string detail = l.Cut
                ? $"{UtilityNet.Name(l.Kind)} 끊김 ({l.Cause}) — {(down.Count > 0 ? string.Join("·", down.Select(r => r.Name)) + (l.Kind == NetKind.Power ? " 정전" : l.Kind == NetKind.Water ? " 단수" : " 환기 끊김") : "다른 길로 돈다")}"
                : l.Temp ? $"임시로 이은 {UtilityNet.Name(l.Kind)} → 제대로 다시" : $"{UtilityNet.Name(l.Kind)} 상함 ({l.Integrity * 100:0}%)";
            post(WorkKind.RepairNet, WorkTarget.AtCell(spot, l.Room), MathF.Min(1.15f, u), l.Kind == NetKind.Power ? Skill.Electrical : Skill.Mechanics, detail, circuit: l.Id);
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
            NetKind.Power => ItemKind.Cable,
            NetKind.Water => w.Ship.CountStored(ItemKind.Sealant) > 0 ? ItemKind.Sealant : ItemKind.Plate,
            _ => ItemKind.Plate,
        };
        bool have = w.Ship.CountStored(item) > 0;
        bool temp = !have;
        if (temp && !l.Cut) { blocked = $"{ItemKinds.Name(item)} 없음"; return null; }
        var toils = have ? FetchAll(c, w, dist, new[] { (item, 1) }) : Plans.DropOff(c, w, dist);
        if (toils == null) { temp = true; toils = Plans.DropOff(c, w, dist); }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(temp ? 0.25f : 0.5f, o.Skill, at.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!temp && !UseAll(cm, new[] { (item, 1) })) return false;
            world.Board.Close(o);
            bool wasCut = l.Cut;
            l.Integrity = temp ? 0.6f : 1f;
            l.Temp = temp;
            if (temp) world.Net.Stats.TempRepairs++; else world.Net.Stats.Repairs++;
            world.Net.Update(0f);
            MarkLog.Add(l.Room.Marks, world.Tick, $"{cm.Name}: {UtilityNet.Name(l.Kind)} {(temp ? "임시로 이음" : "다시 이음")}");
            world.Log.Add(world.Tick, LogKind.Work, $"{l.Room.Name} {UtilityNet.Name(l.Kind)}을(를) {(temp ? "임시로 이었다 — 나중에 제대로" : "다시 이었다")}", cm.Id);
            if (wasCut) world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 끊긴 {l.Room.Name} {UtilityNet.Name(l.Kind)}을(를) {(temp ? "임시로 " : "")}이었다", l.Room, new[] { cm });
            return true;
        }));
        return Wrap(a, o, c, w, temp ? "임시로 잇기" : "망 잇기", toils, $"{l.Room.Name} {UtilityNet.Name(l.Kind)} {(temp ? "임시로 잇기" : "다시 잇기")}");
    }
}
