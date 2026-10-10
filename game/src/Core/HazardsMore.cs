using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.4 연쇄를 잘 일으키는 사고: 냉각 상실 · 덕트 화재 · 수소 축적 · 컴퓨터 오판단 · 전염병 · 미세 운석 소나기 · 가스 탱크 파열 · 냉장고 고장.
public sealed partial class HazardSystem
{
    private readonly List<(long due, int roomId, int node)> _ductFires = new();
    private readonly List<(long due, int furnitureId, int node)> _spoil = new();

    /// <summary>냉각 상실: 냉각 루프 본관이 크게 터진다 → 냉각수가 빠지고 원자로가 긴급 정지한다.</summary>
    internal string? CoolantLoss()
    {
        var w = _w;
        var segs = w.Piping.Segments.Where(s => s.IsCoolant && s.Path.Count > 0 && s.Role is PipeRole.HotLeg or PipeRole.ColdLeg).ToList();
        if (segs.Count == 0) segs = w.Piping.Segments.Where(s => s.IsCoolant && s.Path.Count > 0).ToList();
        if (segs.Count == 0) return null;
        var seg = segs[w.Rng.Range(0, segs.Count)];
        var hit = w.Piping.Burst(seg.Path[seg.Path.Count / 2], 0.95f);
        if (hit == null) return null;
        w.RaiseAlert($"냉각 상실 — {Ko.IGa(hit.Name)} 크게 터졌다 · 냉각수가 쏟아진다", w.Ship.RoomAt(hit.LeakAt), AlertLevel.Critical, shipWide: true);
        w.Movement.Bang(w.Ship.RoomAt(hit.LeakAt), hit.LeakAt.Center, 0.6f, $"{Ko.IGa(hit.Name)} 터졌다"); // v14.5
        w.History.Add(w, HistoryKind.Incident, $"냉각 상실 — {hit.Name} 파열", w.Ship.RoomAt(hit.LeakAt), at: hit.LeakAt);
        return $"냉각 상실({hit.Name})";
    }

    /// <summary>덕트 화재: 덕트 속 먼지·기름때에 불이 붙어, 덕트를 타고 몇 분 뒤 옆방들로 번진다.</summary>
    internal string? DuctFire(Room? room)
    {
        var w = _w;
        if (room == null || room.Detached) return null;
        var floor = room.Cells.Where(w.Ship.IsOpenFloor).ToList();
        if (floor.Count == 0) return null;
        var start = floor.OrderBy(c => (c.Center - room.Center).LengthSquared()).First();
        if (!w.Fire.Ignite(start, 0.4f)) return null;
        int node = w.Causes.Context;
        // 덕트로 이어진 옆방 한둘 (환기 덕트 토막이 닿는 방)
        var next = w.Net.Links.Where(l => l.Kind == NetKind.Air && l.Door != null && (l.Door.RoomA == room || l.Door.RoomB == room))
            .Select(l => l.Door!.RoomA == room ? l.Door.RoomB : l.Door.RoomA).OfType<Room>()
            .Where(r => !r.Detached && r.Type != RoomType.Corridor).Distinct().OrderBy(r => r.Id).ToList();
        int n = Math.Min(next.Count, 1 + w.Rng.Range(0, 2));
        for (int i = 0; i < n; i++)
            _ductFires.Add((w.Tick + SimTime.Minutes(w.Rng.Range(6f, 18f)), next[(i + w.Rng.Range(0, next.Count)) % next.Count].Id, node));
        foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Air && l.Room == room)) w.Net.Hurt(l, 0.35f, "덕트 화재");
        w.RaiseAlert($"덕트 화재 — {room.Name} 환기구에서 연기 · 덕트를 타고 번질 수 있다 (댐퍼를 닫아라)", room, AlertLevel.Critical, shipWide: true);
        return $"덕트 화재({room.Name})";
    }

    /// <summary>수소 축적: 산소 발생기 방의 환기가 막혀 수소가 모인다 → 불꽃 하나에 폭발 (식히고 환기하면 막는다).</summary>
    internal string? HydrogenBuildup()
    {
        var w = _w;
        var gens = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Where(f => !f.Room.Detached && f.Machine != null).ToList();
        if (gens.Count == 0) return null;
        var g = gens[w.Rng.Range(0, gens.Count)];
        var room = g.Room;
        room.VentOpen = false;
        room.DamperStuck = true;
        g.Machine!.Vapor = MathF.Max(g.Machine.Vapor, 0.55f);
        MarkLog.Add(room.Marks, w.Tick, "환기 댐퍼가 걸려 닫힌 채 — 수소가 모인다");
        w.RaiseAlert($"수소 축적 — {room.Name} 환기 댐퍼가 닫힌 채 걸렸다 · {g.Machine.Name}의 수소가 빠지지 않는다", room, AlertLevel.Warning, shipWide: true);
        return $"수소 축적({room.Name})";
    }

    /// <summary>컴퓨터 오판단: 틀어진 감지기를 믿고 멀쩡한 핵심 방의 분전함을 내린다 (보수적인 자동화의 대가).</summary>
    internal string? ComputerMisjudge()
    {
        var w = _w;
        if (!w.Automation.MainOnline) return null;
        var rooms = w.Ship.LiveRooms.Where(r => !r.Detached && !r.BreakerOff && r.Type is RoomType.LifeSupport or RoomType.Cooling or RoomType.Medbay or RoomType.Hydroponics).ToList();
        if (rooms.Count == 0) return null;
        var room = rooms[w.Rng.Range(0, rooms.Count)];
        foreach (var f in room.Furniture) if (f.Machine is Machine m) m.SensorCal = MathF.Max(0.3f, m.SensorCal - 0.35f);
        w.Moisture.Isolate(room, null);
        w.RaiseAlert($"주 컴퓨터가 {room.Name} 누전 경보를 믿고 분전함을 내렸다 — 감지기가 틀어져 있었다 (멀쩡한 방이 꺼졌다)", room, AlertLevel.Warning, shipWide: true);
        w.History.Add(w, HistoryKind.Damage, $"컴퓨터 오판단 — 틀어진 감지기를 믿고 {room.Name} 분전함을 내렸다", room);
        return $"컴퓨터 오판단({room.Name})";
    }

    /// <summary>미세 운석 소나기: 20분 동안 아주 작은 운석 열 몇 개 — 외벽 곳곳에 작은 구멍 (실링폼이 바닥난다).</summary>
    internal string? MicroShower()
    {
        var w = _w;
        var rooms = w.Ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Corridor && w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r)).ToList();
        if (rooms.Count == 0) return null;
        int n = 10 + w.Rng.Range(0, 8);
        for (int i = 0; i < n; i++)
            Shower.Add((w.Tick + SimTime.Minutes(w.Rng.Range(0f, 20f)), Scenarios.OuterTarget(w, rooms[w.Rng.Range(0, rooms.Count)]), w.Rng.Range(0.07f, 0.17f)));
        Shower.Sort((a, b) => a.tick.CompareTo(b.tick));
        ShowerNode = w.Causes.Context;
        w.RaiseAlert($"미세 운석 소나기 — 20분 동안 작은 것 {n}개쯤 · 외벽 곳곳에 바늘구멍", null, AlertLevel.Warning, shipWide: true);
        return "미세 운석 소나기";
    }

    /// <summary>가스 탱크 파열: 생명유지실 고압 탱크 — 파편, 그리고 산소가 짙어지거나(산소 탱크) 질식할 만큼 옅어진다(질소 탱크).</summary>
    internal string? GasTankRupture()
    {
        var w = _w;
        var room = w.Ship.RoomsOf(RoomType.LifeSupport).FirstOrDefault(r => !r.Detached) ?? w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Select(f => f.Room).FirstOrDefault(r => !r.Detached);
        if (room == null) return null;
        var spot = room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => c.X).FirstOrDefault();
        if (spot == default) return null;
        bool oxygen = w.Rng.Chance(0.5f);
        w.Volatile.Blast(spot, 0.3f, oxygen ? "산소 탱크 파열" : "질소 탱크 파열");
        if (oxygen) room.Air.O2 = MathF.Min(60f, room.Air.O2 + 18f);
        else room.Air.O2 = MathF.Max(6f, room.Air.O2 - 9f);
        w.Air.Reserve = MathF.Max(0f, w.Air.Reserve - w.Air.ReserveCapacity * 0.25f);
        w.RaiseAlert(oxygen ? $"산소 탱크 파열 — {room.Name} 산소가 짙다 · 불꽃 하나가 불이 된다" : $"질소 탱크 파열 — {room.Name} 산소가 밀려났다 · 숨이 막힌다", room, AlertLevel.Critical, shipWide: true);
        return oxygen ? $"산소 탱크 파열({room.Name})" : $"질소 탱크 파열({room.Name})";
    }

    /// <summary>냉장고 고장: 압축기가 멈춘다 — 여섯 시간 안에 못 고치면 안의 식사가 상한다 (식중독).</summary>
    internal string? FreezerFailure(Furniture? f)
    {
        var w = _w;
        f ??= w.Ship.FurnitureOf(FurnitureType.Fridge).FirstOrDefault(x => !x.Room.Detached);
        if (f?.Machine is not Machine m) return null;
        if (w.Machines.Break(m, FaultKind.CompressorFail) == null) return null;
        _spoil.Add((w.Tick + SimTime.Hours(6), f.Id, w.Causes.Context));
        return $"냉장고 고장({f.Label})";
    }

    /// <summary>시스템 틱: 덕트를 타고 번지는 불, 상하는 음식.</summary>
    private void UpdateMore()
    {
        var w = _w;
        for (int i = _ductFires.Count - 1; i >= 0; i--)
        {
            var (due, roomId, node) = _ductFires[i];
            if (w.Tick < due) continue;
            _ductFires.RemoveAt(i);
            var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == roomId);
            if (room == null || room.Detached || !room.VentOpen) { if (room != null && !room.VentOpen) w.Log.Add(w.Tick, LogKind.Ship, $"{room.Name} 댐퍼가 닫혀 있어 덕트의 불이 넘어오지 못했다"); continue; }
            var spot = room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - room.Center).LengthSquared()).FirstOrDefault();
            if (spot == default) continue;
            using (w.Causes.Because(node))
                if (w.Fire.Ignite(spot, 0.35f)) w.RaiseAlert($"덕트를 타고 {room.Name}까지 불이 번졌다", room, AlertLevel.Critical, shipWide: true);
        }
        for (int i = _spoil.Count - 1; i >= 0; i--)
        {
            var (due, fid, node) = _spoil[i];
            var f = w.Ship.Furniture.FirstOrDefault(x => x.Id == fid);
            if (f?.Machine is not Machine m || !m.Has(FaultKind.CompressorFail)) { _spoil.RemoveAt(i); continue; }
            if (w.Tick < due) continue;
            _spoil.RemoveAt(i);
            using (w.Causes.Because(node)) Taint(f);
        }
    }
}
