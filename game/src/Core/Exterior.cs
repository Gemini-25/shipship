using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.6 외부 설비: 선체 밖에 달린 안테나·태양 날개. 운석·잔해·폭풍에 상하고, 수리 드론이 먼저 나가 고친다
// (드론이 없거나 다 고장이면 반나절 뒤 사람이 우주복을 입고 나간다). 상하면 효과가 줄어든다 —
// 안테나: 운석을 먼저 보는 시간(감지 품질) · 태양 날개: 배터리를 조금씩 채운다.

public enum ExtKind { Antenna, SolarWing }

public sealed class ExtFixture
{
    public int Id { get; init; }
    public ExtKind Kind { get; init; }
    /// <summary>붙은 선체 벽 칸과 바깥 방향 (0,-1)=위 (0,1)=아래.</summary>
    public Cell Anchor { get; init; }
    public Cell Out { get; init; }
    public Room? Room { get; init; }
    public float Condition { get; set; } = 1f;
    public long DamagedAt { get; set; } = -1;
    public long RepairDone { get; set; } = -1;
    public string? RepairBy { get; set; }
    public int Hits { get; set; }
    public string Name => Kind switch { ExtKind.Antenna => "주 안테나", _ => $"태양 날개 #{Id}" };
}

public sealed class ExteriorSystem
{
    private readonly World _w;
    public List<ExtFixture> All { get; } = new();
    public int Repairs, DroneRepairs;

    public const float SolarKw = 1.5f; // 날개 하나 (볕이 좋을 때) — 조명 몇 개 몫, 정전 때 배터리를 조금 늦출 뿐

    public ExteriorSystem(World w)
    {
        _w = w;
        Seed();
    }

    private void Seed()
    {
        var ship = _w.Ship;
        // 바깥 벽 칸: 그 방의 맨 위(또는 맨 아래) 줄 바로 바깥, 그 너머가 우주
        (Cell anchor, Cell dir)? Spot(Room r, bool top)
        {
            int y = top ? r.MinY - 1 : r.MaxY + 1;
            var dir = new Cell(0, top ? -1 : 1);
            int mid = (r.MinX + r.MaxX) / 2;
            foreach (int x in Enumerable.Range(r.MinX, r.MaxX - r.MinX + 1).OrderBy(x => Math.Abs(x - mid)))
            {
                var c = new Cell(x, y);
                if (ship.WallAt(c) is not { IsHull: true }) continue;
                if (ship.Grid.Kind(c + dir) != TileKind.Void || ship.Grid.Kind(c + dir + dir) != TileKind.Void) continue;
                if (All.Any(f => Math.Abs(f.Anchor.X - c.X) < 4 && f.Anchor.Y == c.Y)) continue;
                return (c, dir);
            }
            return null;
        }
        void Add(ExtKind k, Room? r, bool top)
        {
            if (r == null || Spot(r, top) is not (Cell a, Cell d)) return;
            All.Add(new ExtFixture { Id = All.Count(f => f.Kind == k) + 1, Kind = k, Anchor = a, Out = d, Room = r });
        }
        var rooms = ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Corridor).ToList();
        Add(ExtKind.Antenna, ship.RoomsOf(RoomType.Comms).FirstOrDefault() ?? ship.RoomsOf(RoomType.Bridge).FirstOrDefault(), true);
        // 태양 날개: 위 줄 가운데쯤 방 하나, 아래 줄 가운데쯤 방 하나 (냉각실 방열판과 겹치지 않게)
        int minY = rooms.Min(r => r.MinY), maxY = rooms.Max(r => r.MaxY);
        float cx = rooms.Average(r => r.Center.X);
        var topRoom = rooms.Where(r => r.MinY == minY && r.Type is not (RoomType.Cooling or RoomType.Comms or RoomType.Reactor)).OrderBy(r => Math.Abs(r.Center.X - cx)).FirstOrDefault();
        var botRoom = rooms.Where(r => r.MaxY == maxY && r.Type is not (RoomType.Cooling or RoomType.Airlock)).OrderBy(r => Math.Abs(r.Center.X - cx)).FirstOrDefault();
        Add(ExtKind.SolarWing, topRoom, true);
        Add(ExtKind.SolarWing, botRoom, false);
    }

    /// <summary>안테나 상태가 감지 품질에 곱해진다 (멀쩡하면 1).</summary>
    public float AntennaFactor => All.FirstOrDefault(f => f.Kind == ExtKind.Antenna) is ExtFixture a ? 0.45f + 0.55f * a.Condition : 1f;

    /// <summary>태양 날개가 지금 내는 전기 (kW).</summary>
    public float SolarNow => All.Where(f => f.Kind == ExtKind.SolarWing).Sum(f => SolarKw * f.Condition) * (_w.Hazards.StormActive ? 0.6f : 1f);

    /// <summary>충격이 선체 밖 설비 가까이를 지나갔다.</summary>
    public void Hit(Cell at, float size, string why)
    {
        foreach (var f in All)
        {
            float d = (f.Anchor.Center - at.Center).Length();
            if (d > 4f + 3f * size) continue;
            float dmg = Math.Clamp(size * (1.2f - d / (5f + 3f * size)), 0.05f, 0.9f);
            Damage(f, dmg, why);
        }
    }

    public void Damage(ExtFixture f, float dmg, string why)
    {
        var w = _w;
        float before = f.Condition;
        f.Condition = MathF.Max(0f, f.Condition - dmg);
        f.Hits++;
        if (f.DamagedAt < 0) f.DamagedAt = w.Tick;
        if (before >= 0.7f && f.Condition < 0.7f)
        {
            string effect = f.Kind == ExtKind.Antenna ? "운석을 늦게 본다" : "배터리 충전이 줄었다";
            w.Causes.Effect(CauseKind.Fault, $"ext:{f.Id}:{f.Kind}", $"{f.Name} 손상 ({why}) — {effect}", f.Room, f.Anchor.Center, w.Causes.Context);
            w.RaiseAlert($"{Ko.IGa(f.Name)} 상했다 ({why}) — {effect} · {f.Condition * 100:0}%", f.Room, AlertLevel.Warning, shipWide: false);
        }
    }

    /// <summary>시스템 틱: 태양 날개 충전, 폭풍의 마모, 드론 수리.</summary>
    public void Update(float dt)
    {
        var w = _w;
        float solar = SolarNow;
        if (solar > 0f && w.Power.BatteryCapacity > 0f)
            w.Power.BatteryCharge = MathF.Min(w.Power.BatteryCapacity, w.Power.BatteryCharge + solar * dt);
        if (w.Hazards.StormActive)
            foreach (var f in All.Where(f => f.Kind == ExtKind.SolarWing))
                if (w.Rng.Chance(0.04f * dt)) Damage(f, 0.08f, "태양 폭풍");
        foreach (var f in All)
        {
            if (f.RepairDone >= 0)
            {
                if (w.Tick < f.RepairDone) continue;
                f.Condition = 1f;
                f.RepairDone = -1;
                f.DamagedAt = -1;
                Repairs++;
                int n = w.Causes.OpenNode($"ext:{f.Id}:{f.Kind}");
                if (n >= 0) w.Causes.Resolve(n, $"{Ko.IGa(f.RepairBy)} {Ko.EulReul(f.Name)} 고쳤다", null);
                w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(f.RepairBy)} {Ko.EulReul(f.Name)} 고쳤다");
                f.RepairBy = null;
                continue;
            }
            if (f.Condition >= 0.7f || f.DamagedAt < 0 || w.Hazards.StormActive) continue;
            if (w.Ship.CountStored(ItemKind.Plate) < 1) continue;
            // 드론 먼저: 수리 드론이 거치대에 있으면 바로 나간다. 없으면 반나절 뒤 사람이 나간다
            var drone = w.Drones.Drones.FirstOrDefault(d => d.Kind == DroneKind.Repair && d.Operational && d.State == DroneState.Docked && d.Battery > 0.5f);
            bool crew = drone == null && w.Tick - f.DamagedAt > SimTime.Hours(12) && w.Crew.Any(c => c.CanAct && c.Suit == null);
            if (drone == null && !crew) continue;
            TakePlate();
            f.RepairBy = drone != null ? drone.Name : "선외 작업조";
            f.RepairDone = w.Tick + SimTime.Hours(drone != null ? (w.Eras.Has("dronenet") ? 0.8f : 1.5f) : 2.5f); // v12.8 드론 편대
            if (drone != null) { DroneRepairs++; drone.Battery = MathF.Max(0.2f, drone.Battery - 0.3f); }
            w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(f.RepairBy)} {Ko.EulReul(f.Name)} 고치러 나갔다 ({f.Condition * 100:0}%)");
        }
    }

    private void TakePlate()
    {
        foreach (var box in _w.Ship.Containers)
            if (box.Storage!.Take(ItemKind.Plate, 1) > 0) return;
    }
}
