using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 연출: 하루 주기 조명 · 사고 조명 (보이기만 — 시뮬레이션은 바꾸지 않는다).
//   밤(22~6시, 앞뒤 한 시간에 걸쳐): 복도는 야간등으로 어둡게 · 사람이 자는 침실은 따뜻한 수면등 · 빈 식당 · 휴게실은 낮춘다 (함교 · 기관 · 의무실은 그대로).
//   사고: 경보가 난 방(알려진 불 · 새는 방 · 지휘 건)은 문가에 붉은 회전등 + 방이 붉게 숨 쉰다 · 목숨이 걸린 건이 열려 있으면 복도도 붉게.
//   로봇 헤드램프: 어두운 방 · 밤 복도 · 지휘 건으로 뛰는 로봇은 앞을 비춘다.
public partial class ShipView
{
    private int[] _roomOcc = Array.Empty<int>(), _roomSleep = Array.Empty<int>();
    private bool[] _roomAlarm = Array.Empty<bool>();
    private bool _shipAlarm;

    /// <summary>밤 정도 (0 낮 · 1 깊은 밤): 21→22시 어두워지고 6→7시 밝아진다.</summary>
    private float NightLevel()
    {
        float h = SimTime.HourOfDay(_world.Tick);
        if (h >= 22f || h < 6f) return 1f;
        if (h >= 21f) return h - 21f;
        if (h < 7f) return 1f - (h - 6f);
        return 0f;
    }

    private static bool Sleepy(RoomType t) => t is RoomType.Quarters or RoomType.QuietQuarters or RoomType.PrivateCabins or RoomType.WaterWallCabin;
    private static bool Social(RoomType t) => t is RoomType.Mess or RoomType.Galley or RoomType.Lounge or RoomType.Gym or RoomType.Theater or RoomType.MeetingRoom
        or RoomType.School or RoomType.Chapel or RoomType.Garden or RoomType.Laundry or RoomType.Observatory or RoomType.Meditation;

    /// <summary>방마다 사람 · 자는 사람 · 경보 (빛 버퍼를 다시 셀 때 한 번).</summary>
    private void ScanRoomsForLight()
    {
        var w = _world;
        int n = w.Ship.Rooms.Count;
        if (_roomOcc.Length != n) { _roomOcc = new int[n]; _roomSleep = new int[n]; _roomAlarm = new bool[n]; }
        Array.Clear(_roomOcc); Array.Clear(_roomSleep); Array.Clear(_roomAlarm);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room is not Room r || r.Id >= n) continue;
            _roomOcc[r.Id]++;
            if (c.Pose == Pose.Sleeping) _roomSleep[r.Id]++;
        }
        _shipAlarm = false;
        foreach (var k in w.Commander.OpenCases)
        {
            if (k.RoomId >= 0 && k.RoomId < n && k.Kind != CaseKind.Casualty) _roomAlarm[k.RoomId] = true;
            if (k.Life) _shipAlarm = true;
        }
        foreach (var room in w.Ship.Rooms)
            if (!room.Detached && (room.Leaking || w.Fire.Count > 0 && w.Fire.IsKnown(room) && w.Fire.CountIn(room) > 0)) _roomAlarm[room.Id] = true;
    }

    /// <summary>밤에 그 방 불빛 배율 (바탕 · 천장등).</summary>
    private float NightMul(Room room)
    {
        float night = NightLevel();
        if (night <= 0f || room.Id >= _roomOcc.Length) return 1f;
        float target = room.Type == RoomType.Corridor ? 0.55f
            : Sleepy(room.Type) ? (_roomSleep[room.Id] > 0 ? 0.32f : _roomOcc[room.Id] > 0 ? 0.8f : 0.6f)
            : Social(room.Type) ? (_roomOcc[room.Id] > 0 ? 0.9f : 0.6f)
            : room.Type is RoomType.Storage or RoomType.Cargo or RoomType.GasStorage or RoomType.Freezer ? (_roomOcc[room.Id] > 0 ? 1f : 0.65f)
            : 1f;
        if (_roomAlarm.Length > room.Id && _roomAlarm[room.Id]) target = 1f; // 경보가 나면 다 켠다
        return 1f + (target - 1f) * night;
    }

    /// <summary>경보 색: 그 방이 붉게 숨 쉰다 · 목숨이 걸린 건이 열려 있으면 복도도.</summary>
    private (float r, float g, float b) AlarmTint(Room room)
    {
        bool here = room.Id < _roomAlarm.Length && _roomAlarm[room.Id];
        bool hall = _shipAlarm && room.Type == RoomType.Corridor;
        if (!here && !hall) return (1f, 1f, 1f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * (here ? 3.2f : 1.6f) + room.Id);
        float k = (here ? 0.28f : 0.14f) * pulse;
        return (1f + k * 0.4f, 1f - k, 1f - k);
    }

    /// <summary>사고 빛: 경보 난 방 문가의 붉은 회전등 · 로봇 헤드램프.</summary>
    private void AddIncidentLights()
    {
        var w = _world;
        var ship = w.Ship;
        for (int id = 0; id < _roomAlarm.Length; id++)
        {
            if (!_roomAlarm[id]) continue;
            var room = ship.Rooms[id];
            if (room.Detached) continue;
            int nth = 0;
            foreach (var d in room.Doors)
            {
                if (d.Removed || nth >= 3) continue;
                // 문 안쪽 칸에 회전등 — 돌아가는 빛이라 세기가 오르내린다
                foreach (var dir in Cell.Dirs4)
                {
                    var c = d.Cell + dir;
                    if (ship.RoomAt(c) != room) continue;
                    float spin = Mathf.Max(0f, Mathf.Sin(_time * 6f + nth * 2.1f + id));
                    float s = 0.25f + 0.75f * spin * spin;
                    _srcs.Add(new LightSrc(c.X + 0.5f, c.Y + 0.5f, 3.2f, 0.9f * s, 0.12f * s, 0.08f * s, room.Id, Mathf.Cos(_time * 6f + nth), Mathf.Sin(_time * 6f + nth)));
                    nth++;
                    break;
                }
            }
        }
        // 로봇 헤드램프 (스물넷까지)
        float night = NightLevel();
        int lamps = 0;
        foreach (var r in w.Robots.Robots)
        {
            if (lamps >= 24) break;
            if (r.Wrecked || r.State != RobotState.Active || r.Room is not Room room || room.Detached) continue;
            bool dark = !room.Powered || room.LightsOut || night > 0.5f && room.Type == RoomType.Corridor || room.Air.Smoke > 0.35f;
            if (!dark && r.CaseId < 0) continue;
            var f = r.Facing;
            float len = f.Length();
            if (len < 0.01f) continue;
            float s = r.CaseId >= 0 ? 0.6f : 0.45f;
            _srcs.Add(new LightSrc(r.Position.X, r.Position.Y, 3.4f, 0.55f * s, 0.62f * s, 0.7f * s, room.Id, f.X / len, f.Y / len));
            lamps++;
        }
    }
}
