using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 강화: 운석 · 파편이 쏟아질 때 숨을 곳 — 외벽에 닿지 않은 안쪽 방 (모자라면 외벽이 가장 적은 방).
/// 복도 · 에어락 · 새는 방 · 불난 방 · 들어가지 못하는 방은 뺀다. 모두 들어갈 만큼 (바닥 칸 수로) 고른다.
/// </summary>
public static class MeteorShelter
{
    private static World? _cacheWorld;
    private static int _cacheVersion = -1;
    private static readonly Dictionary<int, int> HullCount = new();

    /// <summary>이 방 둘레의 외벽 칸 수.</summary>
    public static int HullWalls(World w, Room r)
    {
        if (!ReferenceEquals(w, _cacheWorld) || w.Structure.Version != _cacheVersion)
        {
            _cacheWorld = w;
            _cacheVersion = w.Structure.Version;
            HullCount.Clear();
            foreach (var (cell, wall) in w.Ship.Walls)
                if (wall.IsHull && Hull.InsideRoom(w.Ship, cell) is Room hr) HullCount[hr.Id] = HullCount.GetValueOrDefault(hr.Id) + 1;
        }
        return HullCount.GetValueOrDefault(r.Id);
    }

    /// <summary>지금 운석 · 파편이 쏟아지고 있나 (운석우 · 미세 운석 · 잔해 지대 · 파편을 뿌리는 대재난).</summary>
    public static bool Raining(World w) =>
        w.Hazards.Shower.Count > 0
        || w.Cosmic.Events.Any(e => e.Spec.Has(CosmicFx.Debris) && e.Phase is CosmicPhase.Brace or CosmicPhase.Impact);

    public static bool Usable(World w, Room r) =>
        !r.Detached && !r.Leaking && !r.OffLimits && !r.Abandoned && !r.Unbreathable // 잠긴 방도 (이웃이 새서 예방으로 잠갔을 뿐 — 갈 수 있나는 길이 판단한다)
        && r.Type != RoomType.Corridor && r.Type != RoomType.Airlock && w.Fire.CountIn(r) == 0 && w.Sensors.Threat(r) == null;

    /// <summary>숨을 방들: 외벽에 닿지 않은 방은 모두 (저마다 가까운 곳으로 — 멀리 외벽 쪽 복도를 건너지 않게), 모자라면 외벽이 적은 방을 더한다.</summary>
    public static List<Room> Pick(World w)
    {
        int need = Math.Max(1, w.Crew.Count(c => !c.Dead && !c.Away && !c.LeftShip));
        var rooms = w.Ship.LiveRooms.Where(r => Usable(w, r) && Floor(w, r) >= 2)
            .OrderBy(r => HullWalls(w, r)).ThenByDescending(r => Floor(w, r)).ThenBy(r => r.Id).ToList();
        var pick = new List<Room>();
        int room = 0;
        foreach (var r in rooms)
        {
            if (HullWalls(w, r) > 0 && room >= need * 3 / 2) break; // 안쪽 방은 다 넣고, 외벽이 있는 방은 자리가 모자랄 때만
            pick.Add(r);
            room += Floor(w, r);
        }
        return pick;
    }

    private static int Floor(World w, Room r) => r.Cells.Count(w.Ship.IsOpenFloor);
}

public sealed partial class AutomationSystem
{
    private bool _debrisOn;
    private long _debrisNext;
    private List<Room> _debrisShelters = new();

    /// <summary>강화: 운석이 쏟아지는 동안 숨을 안쪽 방들 (대피 방송이 살아 있을 때만).</summary>
    public IReadOnlyList<Room> DebrisShelters => _debrisOn ? _debrisShelters : Array.Empty<Room>();
    public bool DebrisShelterActive => _debrisOn && ShelterCall;

    /// <summary>이 사람이 갈 대피소: 운석 대피면 가장 가까운 안쪽 방, 아니면 방사선 대피소.</summary>
    public Room? ShelterFor(CrewMember c, DistanceField? dist)
    {
        if (DebrisShelterActive)
        {
            Room? best = null;
            int bestD = int.MaxValue;
            foreach (var r in _debrisShelters)
            {
                if (!MeteorShelter.Usable(_world, r)) continue;
                if (c.Room == r) return r;
                int d = int.MaxValue;
                if (dist != null) foreach (var cell in r.Cells) { int x = dist.Get(cell); if (x >= 0 && x < d) d = x; }
                else d = (int)(MathF.Abs(r.Center.X - c.Position.X) + MathF.Abs(r.Center.Y - c.Position.Y));
                if (d < bestD) { bestD = d; best = r; }
            }
            if (best != null) return best;
        }
        return ShelterRoom();
    }

    public bool IsShelter(Room? r) => r != null && (DebrisShelterActive ? _debrisShelters.Contains(r) : r == ShelterRoom());

    /// <summary>
    /// 강화: 운석 · 파편이 쏟아지기 시작하면 외벽에 닿지 않은 방들로 대피 방송 — 끝날 때까지 (사람이 외벽 쪽 복도 · 방에 모여 있다가 한꺼번에 맞지 않게).
    /// 끝나면 일상으로 돌려보낸다. 1분마다 본다.
    /// </summary>
    private void DebrisWatch()
    {
        var w = _world;
        if (w.Tick < _debrisNext) return;
        _debrisNext = w.Tick + SimTime.Minutes(1);
        bool raining = Present && (MainOnline || Core.BackupCore) && MeteorShelter.Raining(w);
        if (raining)
        {
            bool stale = _debrisShelters.Count == 0 || _debrisShelters.Count(r => MeteorShelter.Usable(w, r)) * 2 < _debrisShelters.Count; // 절반 넘게 못 쓰게 됐을 때만 다시 고른다
            if (!_debrisOn || stale)
            {
                var pick = MeteorShelter.Pick(w);
                if (pick.Count == 0) return;
                bool first = !_debrisOn;
                _debrisShelters = pick;
                _debrisOn = true;
                string names = string.Join("·", pick.Take(3).Select(r => r.Name)) + (pick.Count > 3 ? $" 외 {pick.Count - 3}곳" : "");
                Speak.Announce(Voice.Style(first ? $"운석 — 외벽에서 떨어져 가까운 안쪽 방({names})으로 피하라. 지나갈 때까지 머문다"
                    : $"대피할 방을 바꾼다 — {names}"), pick[0], 2, "shelter");
                Reason("debris-shelter", $"운석이 쏟아진다 — 외벽에 닿지 않은 방 {pick.Count}곳에 모두를 모은다 ({names})", SimTime.Minutes(30));
            }
            _shelterUntil = Math.Max(_shelterUntil, w.Tick + SimTime.Minutes(5));
        }
        else if (_debrisOn)
        {
            _debrisOn = false;
            Speak.Announce(Voice.Style("운석이 지나갔다 — 하던 일로 돌아가도 된다"), null, 1);
        }
    }
}
