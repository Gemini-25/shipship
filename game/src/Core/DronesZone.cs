using System;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v23 구역 중계기가 꺼진 구역: 드론이 선체 밖 그 구역 곁에 떠서 무전을 임시로 잇는다 (Relays.cs).
public sealed partial class DroneSystem
{
    /// <summary>임시 중계를 띄운다 — 중계 드론 먼저 · 없으면 점검 드론 · 아무 드론. 띄웠으면 그 드론.</summary>
    public Drone? StartZoneRelay(RelayZone z)
    {
        var w = _world;
        if (Hatch(w) == null) return null;
        if (NearestVoid(z.At, 14) is not Vector2 at) return null;
        var d = Drones.Where(x => x.Operational && x.State == DroneState.Docked && x.Errand == DroneErrand.None && x.Order == null && x.Battery >= 0.5f)
            .OrderBy(x => x.Kind == DroneKind.Relay ? 0 : RobotsV15.Base(x.Kind) == DroneKind.Inspect ? 1 : 2).ThenBy(x => x.Id).FirstOrDefault();
        if (d == null || TripCost(d, at, 1f) > d.Battery) return null;
        d.Errand = DroneErrand.Zone;
        d.GuardFor = z.Id;
        d.ErrandSpot = at;
        Launch(d, at, $"{z.Name} 무전 임시 중계");
        d.Mind.Say($"{z.Name} 중계기가 꺼졌다 — 선체 밖 그 곁에서 무전을 잇는다", w.Tick);
        return d;
    }

    /// <summary>중계기가 살아났다 — 임시 중계 드론은 돌아간다.</summary>
    public void EndZoneRelay(RelayZone z)
    {
        var d = Drones.FirstOrDefault(x => x.Id == z.DroneId);
        if (d == null || d.Errand != DroneErrand.Zone || d.GuardFor != z.Id) return;
        d.GuardFor = -1;
        if (d.State is DroneState.Working or DroneState.Outbound) { _world.Log.Add(_world.Tick, LogKind.Work, $"{d.Name}: {z.Name} 중계기가 살아났다 — 돌아간다"); GoHome(d); }
        else d.Errand = DroneErrand.None;
    }

    /// <summary>임시 중계 드론: 그 구역이 아직 끊겼고 전지가 남으면 더 머문다.</summary>
    private bool ZoneStays(Drone d)
    {
        var w = _world;
        if (d.GuardFor < 0 || d.GuardFor >= w.Relays.Zones.Count) return false;
        var z = w.Relays.Zones[d.GuardFor];
        bool relayDown = z.Broken || !(z.RoomId >= 0 && w.Ship.Rooms[z.RoomId].Powered) && z.Battery <= 0f;
        if (!relayDown || d.Battery <= ReturnCost(d) + 0.15f)
        {
            if (z.DroneId == d.Id) z.DroneId = -1;
            if (relayDown) w.Log.Add(w.Tick, LogKind.Work, $"{d.Name}: 전지가 모자라다 — {z.Name} 임시 중계를 접고 돌아간다");
            return false;
        }
        d.WorkDone = 0f;
        d.WorkNeeded = 0.5f;
        return true;
    }

    private Vector2? NearestVoid(Cell from, int reach)
    {
        var grid = _world.Ship.Grid;
        Cell? best = null;
        int bestD = int.MaxValue;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                var c = new Cell(from.X + dx, from.Y + dy);
                if (!grid.InBounds(c) || grid.Kind(c) != TileKind.Void) continue;
                int dd = dx * dx + dy * dy;
                if (dd < bestD) { bestD = dd; best = c; }
            }
        return best?.Center;
    }
}
