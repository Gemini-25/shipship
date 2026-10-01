using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v14.8 배관 · 배선의 전달량: "이어졌나"에서 "얼마나 오나"로.
// 토막마다 굵기가 있다 — 상하면 가늘어지고, 임시로 이은 곳은 더 가늘고, 보조 간선은 처음부터 가늘다.
// 평소에 지나던 만큼으로 굵기를 맞춰 깔았으니, 한 곳이 끊겨 돌아가는 길로 몰리면 거기서 모자란다 (우회의 한계).
// 멀수록 압이 떨어지고(문을 지날 때마다), 임시로 이은 전선은 접촉 저항으로 달아오른다.
// 수압이 떨어진 방의 더러운 물은 급수관으로 거꾸로 든다. 보조 간선이 주 간선과 같은 방을 지나면 한 번에 둘 다 끊긴다.
// 문 양쪽 기압이 다르면 손으로 못 연다 — 균압 밸브로 맞추고 연다. 에어락은 점검 → 감압 → 나감 → 가압 → 털기.

public sealed class FlowStats
{
    public int Brownouts, LowPressure, Bypassed, SpliceHot, SpliceSparks, Backflows, Sickened, CommonMode, Equalized, Flushes;
    public int AirlockChecks, AirlockDumps, AirlockSlow, AirlockDecons;
    public string Summary() =>
        $"전압 강하 {Brownouts} · 수압 낮음 {LowPressure} · 보조 간선으로 돎 {Bypassed} · 임시 이음 달아오름 {SpliceHot}(불꽃 {SpliceSparks}) · " +
        $"역류 {Backflows}(탈 난 사람 {Sickened} · 관 씻어 냄 {Flushes}) · 같은 길을 지나는 보조 간선 {CommonMode} · 문 균압 {Equalized} · " +
        $"에어락 점검 {AirlockChecks}(급히 공기 버림 {AirlockDumps} · 전기 없어 손으로 {AirlockSlow} · 나오며 털기 {AirlockDecons})";
}

/// <summary>한 방이 받는 몫과, 무엇이 그걸 막는지.</summary>
public readonly record struct FlowShare(float Frac, int Doors, NetLink? Limit, bool ViaRing, string? Why);

public sealed partial class UtilityNet
{
    /// <summary>보조 간선의 굵기 (평소 간선에 견준 몫)와, 몇 방 몫을 나르는지.</summary>
    public static float RingCap(NetKind k) => k switch { NetKind.Power => 0.55f, NetKind.Water => 0.45f, _ => 0.5f };
    public static float RingRooms(NetKind k) => k switch { NetKind.Power => 4f, NetKind.Water => 3f, _ => 3f };
    /// <summary>문을 하나 지날 때마다 남는 압 (전기는 거의 그대로, 물·공기는 조금씩 준다).</summary>
    public static float HopKeep(NetKind k) => k switch { NetKind.Power => 0.995f, NetKind.Water => 0.965f, _ => 0.96f };

    private readonly Dictionary<string, int> _design = new(); // 토막마다 깔 때 맞춘 굵기 (평소 지나는 방 수)
    private int _designVersion = -1;

    public static bool IsRing(NetLink l) => l.Key.Contains(":ring:");

    /// <summary>토막의 굵기 0~1: 상한 만큼 · 임시로 이었으면 · 보조 간선이면.</summary>
    public static float Cap(NetLink l)
    {
        if (l.Cut || l.Room.Detached) return 0f;
        float c = 0.4f + 0.6f * Math.Clamp((l.Integrity - 0.3f) / 0.7f, 0f, 1f);
        if (l.Temp) c *= 0.8f; // 임시로 이은 곳은 가늘다
        if (IsRing(l)) c *= RingCap(l.Kind);
        return c;
    }

    /// <summary>한 종류의 망에서 방마다 받는 몫 (가장 넓은 길 · 문마다 압이 떨어지고 · 한 토막을 나눠 쓰면 모자란다).</summary>
    public Dictionary<int, FlowShare> Throughput(NetKind k)
    {
        EnsureBuilt();
        var result = new Dictionary<int, FlowShare>();
        var sources = Sources(k);
        if (sources.Count == 0 || _nodes == 0) return result;
        if (_designVersion != Version) { Design(); _designVersion = Version; }
        var adj = _adj[(int)k];
        var best = new float[_nodes];
        var doors = new int[_nodes];
        var via = new NetLink?[_nodes];
        var done = new bool[_nodes];
        var pq = new PriorityQueue<int, (float, int)>();
        foreach (var r in sources)
        {
            var hub = _hubs.FirstOrDefault(h => h.room == r);
            if (hub.room == null) continue;
            best[hub.node] = 1f;
            pq.Enqueue(hub.node, (-1f, 0));
        }
        while (pq.TryDequeue(out int n, out _))
        {
            if (done[n]) continue;
            done[n] = true;
            foreach (var l in adj[n])
            {
                float cap = Cap(l);
                if (cap <= 0f) continue;
                int o = l.NodeA == n ? l.NodeB : l.NodeA;
                if (done[o]) continue;
                float b = MathF.Min(best[n], cap);
                int d = doors[n] + (l.Key.EndsWith(":X") ? 1 : 0);
                if (b > best[o] + 1e-4f || MathF.Abs(b - best[o]) <= 1e-4f && via[o] != null && d < doors[o])
                {
                    best[o] = b; doors[o] = d; via[o] = l;
                    pq.Enqueue(o, (-b, d));
                }
            }
        }
        // 한 토막을 지나는 방 수 (방마다 공급원까지 거슬러 올라가며)
        var load = new Dictionary<NetLink, int>();
        foreach (var (node, room) in _hubs)
        {
            if (best[node] <= 0f || sources.Contains(room)) continue;
            for (int n = node, guard = 0; via[n] is NetLink l && guard < 400; guard++)
            {
                load[l] = load.TryGetValue(l, out var x) ? x + 1 : 1;
                n = l.NodeA == n ? l.NodeB : l.NodeA;
            }
        }
        foreach (var (node, room) in _hubs)
        {
            if (sources.Contains(room)) { result[room.Id] = new FlowShare(1f, 0, null, false, null); continue; }
            if (best[node] <= 0f) { result[room.Id] = new FlowShare(0f, 0, null, false, "끊김"); continue; }
            float frac = 1f;
            NetLink? limit = null;
            bool ring = false;
            for (int n = node, guard = 0; via[n] is NetLink l && guard < 400; guard++)
            {
                if (IsRing(l)) ring = true;
                float carry = IsRing(l) ? RingRooms(k) : MathF.Max(2f, _design.TryGetValue(l.Key, out var dd) ? dd : 2f) * 1.25f;
                float eff = Cap(l) * MathF.Min(1f, carry * Cap(l) / MathF.Max(1, load.TryGetValue(l, out var ld) ? ld : 1) / MathF.Max(0.01f, Cap(l)));
                if (eff < frac) { frac = eff; limit = l; }
                n = l.NodeA == n ? l.NodeB : l.NodeA;
            }
            frac *= MathF.Pow(HopKeep(k), doors[node]);
            string? why = limit == null ? (doors[node] >= 6 ? $"문 {doors[node]}개 너머" : null)
                : IsRing(limit) ? "보조 간선으로 돈다 (가늘다)"
                : limit.Temp ? $"{limit.Room.Name} 임시로 이은 곳"
                : limit.Integrity < 0.95f ? $"{limit.Room.Name} 상한 토막 {limit.Integrity * 100:0}%"
                : load.TryGetValue(limit, out var lo) && lo > (_design.TryGetValue(limit.Key, out var de) ? de : 2) ? $"{limit.Room.Name} 토막에 {lo}방이 몰렸다 (평소 {de})"
                : null;
            result[room.Id] = new FlowShare(MathF.Min(1f, frac), doors[node], limit, ring, why);
        }
        return result;
    }

    /// <summary>깔 때의 굵기: 모두 멀쩡하고 보조 간선이 없을 때 토막마다 지나는 방 수.</summary>
    private void Design()
    {
        _design.Clear();
        foreach (NetKind k in Enum.GetValues<NetKind>())
        {
            var sources = Sources(k);
            if (sources.Count == 0) continue;
            var adj = _adj[(int)k];
            var via = new NetLink?[_nodes];
            var seen = new bool[_nodes];
            var q = new Queue<int>();
            foreach (var r in sources)
            {
                var hub = _hubs.FirstOrDefault(h => h.room == r);
                if (hub.room == null || seen[hub.node]) continue;
                seen[hub.node] = true; q.Enqueue(hub.node);
            }
            while (q.Count > 0)
            {
                int n = q.Dequeue();
                foreach (var l in adj[n])
                {
                    if (IsRing(l) || l.Room.Detached) continue;
                    int o = l.NodeA == n ? l.NodeB : l.NodeA;
                    if (seen[o]) continue;
                    seen[o] = true; via[o] = l; q.Enqueue(o);
                }
            }
            foreach (var (node, room) in _hubs)
            {
                if (sources.Contains(room)) continue;
                for (int n = node, guard = 0; via[n] is NetLink l && guard < 400; guard++)
                {
                    _design[l.Key] = _design.TryGetValue(l.Key, out var x) ? x + 1 : 1;
                    n = l.NodeA == n ? l.NodeB : l.NodeA;
                }
            }
        }
    }

    /// <summary>보조 간선이 주 간선과 같은 방을 지나는가 (그 방에서 한 번 터지면 둘 다 끊긴다). 겹치는 방들.</summary>
    public List<Room> SharedWithMain(NetLink ring)
    {
        var shared = new List<Room>();
        if (!IsRing(ring)) return shared;
        var parts = ring.Key.Split(':')[2].Split('-');
        int from = int.Parse(parts[0]), to = int.Parse(parts[1]);
        var ship = _w.Ship;
        // 주 간선이 지나는 방: 공급원에서 그 방까지 문을 따라 (보조 간선 없이)
        var mainRooms = MainPathRooms(ring.Kind, to);
        foreach (var c in ring.Cells)
        {
            if (ship.RoomAt(c) is not Room r || r.Id == from || r.Id == to || shared.Contains(r)) continue;
            if (mainRooms.Contains(r.Id)) shared.Add(r);
        }
        return shared;
    }

    private HashSet<int> MainPathRooms(NetKind k, int toRoom)
    {
        var set = new HashSet<int>();
        var sources = Sources(k);
        var adj = _adj[(int)k];
        var via = new NetLink?[_nodes];
        var seen = new bool[_nodes];
        var q = new Queue<int>();
        foreach (var r in sources)
        {
            var hub = _hubs.FirstOrDefault(h => h.room == r);
            if (hub.room == null || seen[hub.node]) continue;
            seen[hub.node] = true; q.Enqueue(hub.node);
        }
        while (q.Count > 0)
        {
            int n = q.Dequeue();
            foreach (var l in adj[n])
            {
                if (IsRing(l)) continue;
                int o = l.NodeA == n ? l.NodeB : l.NodeA;
                if (seen[o]) continue;
                seen[o] = true; via[o] = l; q.Enqueue(o);
            }
        }
        var target = _hubs.FirstOrDefault(h => h.room.Id == toRoom);
        if (target.room == null) return set;
        for (int n = target.node, guard = 0; via[n] is NetLink l && guard < 400; guard++)
        {
            set.Add(l.Room.Id);
            if (l.Door != null) { if (l.Door.RoomA != null) set.Add(l.Door.RoomA.Id); if (l.Door.RoomB != null) set.Add(l.Door.RoomB.Id); }
            n = l.NodeA == n ? l.NodeB : l.NodeA;
        }
        return set;
    }
}

/// <summary>v14.8 전달량의 결과: 전압 강하 · 수압 · 역류 · 접촉 저항 · 같은 길 보조 간선 · 문 균압 · 에어락 절차.</summary>
public sealed class FlowSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 5821 + 79));
    public FlowStats Stats { get; } = new();
    private readonly Dictionary<int, FlowShare>[] _share = { new(), new(), new(), new() };
    private readonly HashSet<int> _dim = new(), _lowP = new(), _bypass = new();
    private readonly Dictionary<string, float> _spliceHeat = new();
    private readonly HashSet<string> _hotWarned = new();
    private int _checkedVersion = -1;
    private long _sig = long.MinValue;

    /// <summary>급수의 깨끗함 0~1 (역류가 들면 떨어지고, 정수기가 천천히 되돌린다).</summary>
    public float WaterQuality { get; set; } = 1f;
    public string? TaintFrom { get; private set; }
    public List<(string ring, string rooms)> CommonMode { get; } = new();

    public FlowSystem(World w) => _w = w;

    public FlowShare Share(NetKind k, Room r) => _share[(int)k].TryGetValue(r.Id, out var s) ? s : new FlowShare(1f, 0, null, false, null);
    public float Of(NetKind k, Room r) => Share(k, r).Frac;

    /// <summary>임시로 이은 전선의 열 0~1.</summary>
    public float SpliceHeat(NetLink l) => _spliceHeat.TryGetValue(l.Key, out var h) ? h : 0f;

    public void Update(float dt)
    {
        var w = _w;
        var net = w.Net;
        if (net.Links.Count == 0) return;
        long sig = net.Version;
        foreach (var l in net.Links) sig = sig * 31 + (l.Cut ? 7 : 0) + (l.Temp ? 3 : 0) + (int)(l.Integrity * 40f) + (l.Room.Detached ? 11 : 0);
        foreach (var src in w.Ship.FurnitureOf(FurnitureType.PowerPanel).Concat(w.Ship.FurnitureOf(FurnitureType.WaterRecycler)).Concat(w.Ship.FurnitureOf(FurnitureType.OxygenGenerator))) sig = sig * 17 + src.Room.Id;
        bool same = sig == _sig;
        _sig = sig;
        foreach (var k in new[] { NetKind.Power, NetKind.Water, NetKind.Air })
        {
            var t = same ? _share[(int)k] : net.Throughput(k);
            _share[(int)k] = t;
            foreach (var r in w.Ship.Rooms)
            {
                if (r.Detached) continue;
                var s = t.TryGetValue(r.Id, out var x) ? x : new FlowShare(1f, 0, null, false, null);
                switch (k)
                {
                    case NetKind.Power: r.PowerFlow = s.Frac; break;
                    case NetKind.Water: r.WaterFlow = s.Frac; break;
                    default: r.AirFlow = s.Frac; break;
                }
                if (s.Frac <= 0f) continue; // 끊긴 건 망이 따로 알린다
                if (k == NetKind.Power) Mark(_dim, r, s.Frac < 0.75f, () => { Stats.Brownouts++; w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name} 전압이 떨어졌다 ({s.Frac * 100:0}% — {s.Why ?? "멀다"}) · 불이 흐리고 설비가 덜 돈다"); MarkLog.Add(r.Marks, w.Tick, $"전압 강하 {s.Frac * 100:0}% ({s.Why})"); });
                if (k == NetKind.Water && UtilityNet.NeedsWater(r)) Mark(_lowP, r, s.Frac < 0.6f, () => { Stats.LowPressure++; w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name} 수압이 낮다 ({s.Frac * 100:0}% — {s.Why ?? "멀다"})"); MarkLog.Add(r.Marks, w.Tick, $"수압 낮음 {s.Frac * 100:0}% ({s.Why})"); });
                if (k != NetKind.Air) Mark(_bypass, r, s.ViaRing, () => { Stats.Bypassed++; w.Log.Add(w.Tick, LogKind.Ship, $"{r.Name} {UtilityNet.Name(k)}이 보조 간선으로 돌아 들어온다 ({s.Frac * 100:0}% — 보조 간선은 가늘다)"); });
            }
        }
        Splices(dt);
        Backflow(dt);
        if (_checkedVersion != net.Version) { _checkedVersion = net.Version; CheckIndependence(); }
    }

    private static void Mark(HashSet<int> set, Room r, bool on, Action first)
    {
        if (on) { if (set.Add(r.Id)) first(); }
        else set.Remove(r.Id);
    }

    // ───────────────────────────── 접촉 저항 ─────────────────────────────

    /// <summary>임시로 이은 전선: 많이 흐를수록 이음매가 달아오르고, 오래 두면 녹아 불꽃이 튄다 (이은 사람이 기억한다).</summary>
    private void Splices(float dt)
    {
        var w = _w;
        foreach (var l in w.Net.Links)
        {
            if (l.Kind != NetKind.Power || !l.Temp || l.Cut)
            {
                if (_spliceHeat.Remove(l.Key)) _hotWarned.Remove(l.Key);
                continue;
            }
            // 지나는 방 수만큼 흐른다 (이 이음매 너머에서 받는 방들)
            int carried = 0;
            foreach (var r in w.Ship.Rooms) if (!r.Detached && Share(NetKind.Power, r).Limit == l || r == l.Room) carried++;
            float load = MathF.Min(1.5f, 0.25f + 0.15f * carried);
            float h = SpliceHeat(l);
            h = Math.Clamp(h + (0.5f * load * load - 0.35f * h) * dt, 0f, 1f);
            _spliceHeat[l.Key] = h;
            if (h > 0.55f)
            {
                l.Integrity = MathF.Max(0.31f, l.Integrity - 0.03f * dt); // 이음매가 녹는다
                if (_hotWarned.Add(l.Key))
                {
                    Stats.SpliceHot++;
                    string who = l.SplicedBy >= 0 && l.SplicedBy < w.Crew.Count ? w.Crew[l.SplicedBy].Name : "누군가";
                    w.Log.Add(w.Tick, LogKind.Warning, $"{l.Room.Name} 임시로 이은 전선이 달아오른다 ({Ko.IGa(who)} 이은 곳 · 접촉 저항) — 제대로 다시 이어야 한다");
                    MarkLog.Add(l.Room.Marks, w.Tick, $"임시 이음 달아오름 ({who})");
                    w.Board.RequestScan();
                }
                if (h > 0.8f && R.Chance(0.25f * dt))
                {
                    Stats.SpliceSparks++;
                    var at = l.Cells[l.Cells.Count / 2];
                    w.Fire.Ignite(at, 0.3f);
                    string who = l.SplicedBy >= 0 && l.SplicedBy < w.Crew.Count ? w.Crew[l.SplicedBy].Name : "누군가";
                    w.History.Add(w, HistoryKind.Damage, $"{l.Room.Name} 임시로 이은 전선에서 불꽃이 튀었다 ({Ko.IGa(who)} 급히 이은 곳)", l.Room, log: true);
                    if (l.SplicedBy >= 0 && l.SplicedBy < w.Crew.Count && w.Crew[l.SplicedBy] is { Dead: false } sp)
                        Life.Diary(w, sp, Persona.Say(sp, $"{l.Room.Name}에 급히 이어 둔 전선에서 불꽃이 튀었다. 제대로 다시 이을 걸"));
                    _spliceHeat[l.Key] = 0.5f;
                }
            }
            else if (h < 0.4f) _hotWarned.Remove(l.Key);
        }
    }

    public bool Hot(NetLink l) => SpliceHeat(l) > 0.55f;

    // ───────────────────────────── 역류 ─────────────────────────────

    /// <summary>수압이 떨어진 방에 더러운 물(의무실 · 세탁실 · 균이 묻은 바닥)이 있으면 급수관으로 거꾸로 든다 → 마신 사람이 탈이 난다.</summary>
    private void Backflow(float dt)
    {
        var w = _w;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached || !r.WaterLinked || r.ValveShut || r.WaterFlow <= 0f || r.WaterFlow >= BackflowBelow) continue;
            float dirty = w.Soil.RoomSoil(r)[(int)SoilKind.Bio] + (r.Type is RoomType.Medbay or RoomType.Laundry ? 0.4f : 0f) + (r.Type == RoomType.Hydroponics ? 0.2f : 0f);
            if (dirty < 0.3f || !R.Chance(0.5f * dirty * dt)) continue;
            Stats.Backflows++;
            WaterQuality = MathF.Max(0f, WaterQuality - 0.3f);
            TaintFrom = r.Name;
            w.History.Add(w, HistoryKind.Damage, $"{r.Name} 수압이 떨어져 더러운 물이 급수관으로 거꾸로 들었다 (수압 {r.WaterFlow * 100:0}%)", r, log: true);
            MarkLog.Add(r.Marks, w.Tick, "역류");
        }
        if (WaterQuality < 0.999f)
        {
            // 정수기가 천천히 되돌린다
            float clean = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).Sum(f => f.Machine!.Efficiency);
            WaterQuality = MathF.Min(1f, WaterQuality + 0.04f * clean * dt);
            // 마신 사람이 탈이 난다
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Outside || !c.IsAwake || c.Ailments.Any(a => a.Id == "gastro")) continue;
                if (!R.Chance(0.12f * (1f - WaterQuality) * dt)) continue;
                if (w.Ailments.Catch(c, "gastro", null, $"역류한 물 ({TaintFrom})") != null)
                {
                    Stats.Sickened++;
                    Life.Diary(w, c, Persona.Say(c, "물맛이 이상하더니 배탈이 났다"));
                }
            }
            if (WaterQuality >= 0.999f) TaintFrom = null;
        }
    }

    /// <summary>이 아래로 수압이 떨어지면 더러운 물이 거꾸로 들 수 있다.</summary>
    public const float BackflowBelow = 0.45f;

    /// <summary>급수관을 씻어 낸다: 물을 버리고 깨끗해진다.</summary>
    public void Flush(CrewMember c)
    {
        var w = _w;
        float use = MathF.Min(w.Water.Level, 20f);
        w.Water.Level -= use;
        WaterQuality = 1f;
        Stats.Flushes++;
        w.Log.Add(w.Tick, LogKind.Work, $"급수관을 씻어 냈다 (역류 · 물 {use:0}L 버림)", c.Id);
        w.History.Add(w, HistoryKind.Recovery, $"{Ko.IGa(c.Name)} 역류로 더러워진 급수관을 씻어 냈다 ({TaintFrom ?? "어딘가"}에서 든 물)", null, new[] { c });
        TaintFrom = null;
    }

    // ───────────────────────────── 같은 길 ─────────────────────────────

    /// <summary>보조 간선이 주 간선과 같은 방을 지나면 독립이 아니다 — 그 방 하나에서 둘 다 끊긴다.</summary>
    private void CheckIndependence()
    {
        var w = _w;
        var before = CommonMode.Select(x => x.ring).ToHashSet();
        CommonMode.Clear();
        foreach (var l in w.Net.Links.Where(UtilityNet.IsRing))
        {
            var shared = w.Net.SharedWithMain(l);
            if (shared.Count == 0) continue;
            string rooms = string.Join("·", shared.Select(r => r.Name));
            CommonMode.Add((l.Key, rooms));
            if (!before.Contains(l.Key))
            {
                Stats.CommonMode++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{UtilityNet.Name(l.Kind)} 보조 간선이 주 간선과 같은 {rooms}을(를) 지난다 — 거기서 한 번 터지면 둘 다 끊긴다");
            }
        }
    }

    // ───────────────────────────── 문 균압 ─────────────────────────────

    /// <summary>문 양쪽 기압 차이 (kPa) — 손으로 열 수 없을 만큼이면 균압 밸브로 먼저 맞춘다.</summary>
    public static float DoorDelta(Room a, Room b) => MathF.Abs(a.Air.Pressure - b.Air.Pressure);
    public const float EqualizeAbove = 12f;

    /// <summary>균압 밸브: 두 방 공기를 부피만큼 섞는다 (k: 얼마나 맞추나 0~1).</summary>
    public void Equalize(Room a, Room b, float k)
    {
        float va = MathF.Max(1f, a.Volume), vb = MathF.Max(1f, b.Volume);
        static (float, float) Mix(float x, float y, float va, float vb, float k)
        {
            float avg = (x * va + y * vb) / (va + vb);
            return (x + (avg - x) * k, y + (avg - y) * k);
        }
        (a.Air.O2, b.Air.O2) = Mix(a.Air.O2, b.Air.O2, va, vb, k);
        (a.Air.N2, b.Air.N2) = Mix(a.Air.N2, b.Air.N2, va, vb, k);
        (a.Air.CO2, b.Air.CO2) = Mix(a.Air.CO2, b.Air.CO2, va, vb, k);
        (a.Air.Smoke, b.Air.Smoke) = Mix(a.Air.Smoke, b.Air.Smoke, va, vb, k);
        (a.Air.Toxin, b.Air.Toxin) = Mix(a.Air.Toxin, b.Air.Toxin, va, vb, k);
        (a.Air.CO, b.Air.CO) = Mix(a.Air.CO, b.Air.CO, va, vb, k);
    }

    // ───────────────────────────── 에어락 절차 ─────────────────────────────

    /// <summary>나가는 절차: 짝 점검(우주복 산소·봉인) → 안쪽 문 닫힘 확인 → 감압 (전기가 모자라면 손 펌프로 느리게 · 급하면 공기를 버리고 빨리).</summary>
    public IEnumerable<Toil> AirlockOut(CrewMember c, Door hatch, Cell inner, bool urgent)
    {
        var w = _w;
        var room = w.Ship.RoomAt(inner);
        float power = room != null && room.Powered ? room.PowerFlow : 0f;
        if (!urgent)
        {
            yield return new DoToil((cm, world) => { cm.Say(world, "우주복 점검 — 산소 · 봉인 · 통신"); return true; });
            yield return new WaitToil(SimTime.Minutes(1), Pose.Working, hatch.Cell.Center);
        }
        int pump = urgent ? SimTime.Minutes(1) : power >= 0.6f ? SimTime.Minutes(2) : SimTime.Minutes(5);
        string say = urgent ? "감압 — 급하니 공기를 버린다" : power >= 0.6f ? "감압" : "감압 — 전기가 모자라 손 펌프로";
        yield return new DoToil((cm, world) => { cm.Say(world, say); return true; });
        yield return new WaitToil(pump, Pose.Working, hatch.Cell.Center);
        yield return new DoToil((cm, world) =>
        {
            var f = world.Flow;
            if (!urgent) { f.Stats.AirlockChecks++; MarkLog.Add(cm.Memory.Marks, world.Tick, "에어락: 우주복 점검 → 감압"); }
            if (urgent) { f.Stats.AirlockDumps++; world.CycleAirlock(World.AirlockCycleCost * 1.5f); world.Log.Add(world.Tick, LogKind.Warning, $"{cm.Name}: 급해서 에어락 공기를 회수하지 않고 버렸다", cm.Id); }
            else if (power < 0.6f) { f.Stats.AirlockSlow++; world.Log.Add(world.Tick, LogKind.Work, $"{cm.Name}: 에어락 전압이 낮아 손 펌프로 감압했다", cm.Id); }
            return true;
        });
    }

    /// <summary>들어오는 절차: 가압 → (급하지 않으면) 에어락 안에서 우주복을 턴다.</summary>
    public IEnumerable<Toil> AirlockIn(Door hatch, Cell inner)
    {
        yield return new WaitToil(SimTime.Minutes(2), Pose.Working, hatch.Cell.Center);
        yield return new DoToil((cm, world) =>
        {
            if (cm.Job?.Urgent != true && cm.Soil.SuitDust > 0.05f)
            {
                world.Soil.Decon(cm);
                world.Flow.Stats.AirlockDecons++;
            }
            return true;
        });
    }
}

/// <summary>역류로 더러워진 급수관을 씻어 낸다 (정수기 곁에서).</summary>
public sealed class FlushActivity : Activity
{
    public override string Id => "flush";
    public override string Label => "급수관 씻어 내기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var f = w.Flow;
        if (f.WaterQuality > 0.8f || c.IsChild || c.Down || Crisis.Acting(w) && f.WaterQuality > 0.4f) return (0f, "—");
        if (w.Crew.Any(o => o != c && o.Job?.Activity is FlushActivity)) return (0f, "다른 사람이 씻어 내는 중");
        return (0.45f + 0.5f * (1f - f.WaterQuality) + 0.1f * c.SkillLevel(Skill.Mechanics) / 10f, $"급수가 더럽다 ({f.TaintFrom}에서 역류 · 깨끗함 {f.WaterQuality * 100:0}%)");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var rec = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).Where(r => !r.Room.Detached).SelectMany(r => r.UseSpots).Where(s => dist.Reachable(s) && !w.IsSpotTaken(s, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (rec is not Cell at) return null;
        var toils = new List<Toil>
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(30), Pose.Working, at.Center),
            new DoToil((cm, world) => { world.Flow.Flush(cm); return true; }),
        };
        return new Job(this, "급수관 씻어 내기", toils) { LogText = $"역류한 물을 빼고 급수관을 씻어 낸다 ({w.Flow.TaintFrom})", LogKind = LogKind.Work };
    }
}
