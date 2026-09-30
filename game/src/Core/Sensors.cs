using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

/// <summary>운석 경보가 어디서 왔나 (먼저 알수록 위).</summary>
public enum WarnLevel { None, Lookout, Manual, Sensor }

/// <summary>다가오는 운석 하나. 부딪히기 전까지는 우주선 밖에 있다.</summary>
public sealed class IncomingMeteor
{
    public int Id { get; init; }
    public Cell Target { get; init; }
    /// <summary>크기 (v11.2: 회피 기동으로 스치면 줄어든다).</summary>
    public float Size { get; set; }

    /// <summary>v11.2 회피 기동을 걸었다 (한 운석에 한 번).</summary>
    public bool Evaded { get; set; }
    public string? EvadeNote { get; set; }
    public long Launched { get; init; }
    public long Arrive { get; init; }

    /// <summary>들어올 외벽 칸과 날아오는 방향 (궤적).</summary>
    public Cell Entry { get; init; }
    public Vector2 Direction { get; init; }

    /// <summary>들어올 방 (센서·사람이 궤적을 읽으면 알 수 있다).</summary>
    public Room? Room { get; init; }

    public WarnLevel Warned { get; set; }
    public long WarnedAt { get; set; } = -1;
    public string? WarnedBy { get; set; }

    /// <summary>자동화가 미리 닫은 격벽 (부딪힌 뒤 새지 않으면 다시 연다).</summary>
    public List<Door> SealedDoors { get; } = new();
    public bool Sealed { get; set; }

    public float MinutesLeft(long tick) => MathF.Max(0f, (Arrive - tick) / (float)SimTime.Minutes(1));
}

/// <summary>
/// 통신실 (v10.1). 장거리 센서가 운석을 먼저 본다.
///   A: 센서 — 안테나·레이더가 멀리서 운석을 잡는다. 전기가 있어야 돈다.
///   B: 주 컴퓨터 — 레이더 반사파에서 궤적을 계산해 "몇 분 뒤 어느 방"을 알려 주고, 그 방 격벽·댐퍼를 미리 닫는다.
///      컴퓨터는 센서 없이는 볼 게 없고, 센서는 컴퓨터 없이는 점 하나다.
///   비상 C: 통신실에 사람이 있으면 화면을 눈으로 읽어 늦게나마 방향을 알려 준다 (궤적 계산 없이 — 격벽은 사람이 닫는다).
///      그것도 없으면 함교·통신실 창밖을 보던 사람이 부딪히기 직전에야 "충격 대비"를 외친다.
///   아무도 없으면 경보 없이 맞는다.
/// 경보를 받은 사람은 그 방에서 빠져나가고 (대피), 선체 밖에 있던 사람은 에어락으로 돌아오고, 남은 사람은 몸을 숙인다.
/// 운석은 관찰자가 던진 순간부터 몇 분 동안 날아온다 — 누가 먼저 보느냐는 그때 배의 상태가 정한다.
/// </summary>
public sealed class SensorSystem
{
    public static float ApproachMinutes = 6f;

    public static float LeadMinutes(WarnLevel l) => l switch
    {
        WarnLevel.Sensor => 5f,
        WarnLevel.Manual => 2.5f,
        WarnLevel.Lookout => 0.6f,
        _ => 0f,
    };

    public static string LevelName(WarnLevel l) => l switch
    {
        WarnLevel.Sensor => "센서 경보",
        WarnLevel.Manual => "통신실 수동 판독",
        WarnLevel.Lookout => "창밖 목격",
        _ => "경보 없음",
    };

    private readonly World _w;
    private int _next;

    public List<IncomingMeteor> Incoming { get; } = new();

    // 기록
    public int Warned { get; set; }
    public int WarnedBySensor { get; set; }
    public int Unwarned { get; set; }
    public int PreSeals { get; set; }
    public long LastWarnTick { get; private set; } = -1;

    public SensorSystem(World w) => _w = w;

    public Furniture? Array => _w.Ship.FurnitureOf(FurnitureType.SensorArray).FirstOrDefault();
    public Room? CommsRoom => _w.Ship.RoomsOf(RoomType.Comms).FirstOrDefault();

    /// <summary>센서가 내는 몫 0~1 (전기·고장·마모). 방을 버렸거나 떨어져 나가면 0.</summary>
    public float Quality => Array is Furniture f && f.Machine is Machine m && !f.Room.Abandoned ? m.Efficiency * _w.Hazards.SensorFactor : 0f; // v11.2 태양 폭풍이면 잡음

    public bool Online => Quality > 0.05f;

    /// <summary>궤적 계산까지 되는지 (센서 + 주 컴퓨터).</summary>
    public bool Tracking => Online && _w.Automation.MainOnline;

    private static bool Awake(CrewMember c) => !c.Dead && !c.Down && !c.Outside && c.Pose != Pose.Sleeping && c.CarriedBy == null;

    /// <summary>통신실에서 깨어 있는 사람 (화면을 읽을 수 있다).</summary>
    public CrewMember? Operator => CommsRoom is Room r ? _w.Crew.FirstOrDefault(c => Awake(c) && c.Room == r) : null;

    /// <summary>창밖을 보고 있을 사람 (함교·통신실).</summary>
    public CrewMember? Lookout => _w.Crew.FirstOrDefault(c => Awake(c) && c.Room is { Type: RoomType.Bridge or RoomType.Comms });

    /// <summary>지금 배가 운석을 얼마나 먼저 볼 수 있나.</summary>
    public (WarnLevel level, float lead, CrewMember? who) Capability()
    {
        float q = Quality;
        if (Tracking) return (WarnLevel.Sensor, LeadMinutes(WarnLevel.Sensor) * Math.Clamp(q / 0.8f, 0.3f, 1f), null);
        if (Online && Operator is CrewMember op) return (WarnLevel.Manual, LeadMinutes(WarnLevel.Manual) * Math.Clamp(q / 0.8f, 0.3f, 1f), op);
        if (Lookout is CrewMember lo) return (WarnLevel.Lookout, LeadMinutes(WarnLevel.Lookout), lo);
        return (WarnLevel.None, 0f, null);
    }

    public string StatusText
    {
        get
        {
            var (level, lead, _) = Capability();
            string state = Array == null ? "센서 없음" : !Online ? (Array.Machine!.Faults.FirstOrDefault()?.Name ?? (Array.Machine.Powered ? "멈춤" : "전기 없음")) : $"{Quality * 100:0}%";
            return $"장거리 센서 {state} · {LevelName(level)}" + (lead > 0f ? $" ({lead:0.#}분 전)" : "");
        }
    }

    /// <summary>운석을 던진다: 몇 분 뒤 target 가까운 외벽으로 들어온다.</summary>
    public IncomingMeteor? Launch(Cell target, float size)
    {
        var ship = _w.Ship;
        Cell? entry = null;
        float best = float.MaxValue;
        foreach (var (cell, wall) in ship.Walls)
        {
            if (!wall.IsHull) continue;
            float d = (cell.Center - target.Center).LengthSquared();
            if (d < best) { best = d; entry = cell; }
        }
        if (entry is not Cell e) return null;
        var inside = Hull.InsideRoom(ship, e);
        var aim = target == e || ship.RoomAt(target) == null ? (inside?.Center ?? target.Center) : target.Center;
        var dir = aim - e.Center;
        dir = dir.LengthSquared() < 0.01f ? new Vector2(0, 1) : Vector2.Normalize(dir);
        var m = new IncomingMeteor
        {
            Id = _next++,
            Target = target,
            Size = size,
            Launched = _w.Tick,
            Arrive = _w.Tick + SimTime.Minutes(ApproachMinutes),
            Entry = e,
            Direction = dir,
            Room = inside ?? ship.RoomAt(target),
        };
        Incoming.Add(m);
        return m;
    }

    /// <summary>그 방으로 운석이 온다고 알려졌나 (궤적을 읽은 경보만).</summary>
    public IncomingMeteor? Threat(Room? room)
    {
        if (room == null) return null;
        foreach (var m in Incoming)
            if (m.Room == room && m.Warned >= WarnLevel.Manual) return m;
        return null;
    }

    /// <summary>경보가 울린 운석이 곧 온다 (몸을 숙이고, 선체 밖 사람은 돌아온다).</summary>
    public IncomingMeteor? Alarm => Incoming.FirstOrDefault(m => m.Warned != WarnLevel.None);

    /// <summary>그 방이 충돌 대비로 닫혀 있다 (댐퍼를 닫아 둔다).</summary>
    public bool Sealing(Room room) => Incoming.Any(m => m.Sealed && m.Room == room);

    /// <summary>매 틱: 경보를 내고, 격벽을 미리 닫고, 때가 되면 부딪힌다.</summary>
    public void Step()
    {
        if (Incoming.Count == 0) return;
        var (level, lead, who) = Capability();
        for (int i = 0; i < Incoming.Count; i++)
        {
            var m = Incoming[i];
            if (level > m.Warned && _w.Tick >= m.Arrive - SimTime.Minutes(lead)) Warn(m, level, who);
            if (m.Warned == WarnLevel.Sensor && !m.Sealed) TrySeal(m);
        }
        for (int i = 0; i < Incoming.Count; i++)
        {
            var m = Incoming[i];
            if (_w.Tick < m.Arrive) continue;
            Incoming.RemoveAt(i);
            i--;
            Strike(m);
        }
    }

    private void Warn(IncomingMeteor m, WarnLevel level, CrewMember? who)
    {
        bool first = m.Warned == WarnLevel.None;
        m.Warned = level;
        if (first) m.WarnedAt = _w.Tick;
        m.WarnedBy = who?.Name;
        LastWarnTick = _w.Tick;
        float mins = m.MinutesLeft(_w.Tick);
        string size = m.Size >= 1.3f ? "거대" : m.Size >= 0.7f ? "대형" : "작은";
        string where = m.Room?.Name ?? "선체";
        string text = level switch
        {
            WarnLevel.Sensor => $"센서: {size} 운석 접근 — {mins:0.#}분 뒤 {where} 외벽",
            WarnLevel.Manual => $"{who?.Name ?? "통신실"}(통신실): 레이더에 {size} 운석 — 약 {mins:0.#}분 뒤 {where} 쪽 (주 컴퓨터가 꺼져 화면을 눈으로 읽었다)",
            _ => $"{who?.Name ?? "누군가"}: 창밖에 불빛 — 곧 부딪힌다, 충격 대비!",
        };
        _w.RaiseAlert(text, level >= WarnLevel.Manual ? m.Room : null, AlertLevel.Critical, shipWide: true);
        if (_w.Propulsion.EvasionEnabled) _w.Propulsion.OnWarned(m); // v11.2 엔진으로 비킨다
        if (who != null) MarkLog.Add(who.Memory.Marks, _w.Tick, level == WarnLevel.Manual ? "레이더 화면으로 운석을 먼저 봤다" : "창밖으로 운석을 봤다");
        if (Array?.Machine is Machine sm && level == WarnLevel.Sensor) MarkLog.Add(sm.Marks, _w.Tick, $"{size} 운석을 {mins:0.#}분 전에 잡았다");
        // 경보: 궤적을 읽었으면 그 방 사람은 빠져나가고, 누구든 하던 일을 멈추고 몸을 숙인다
        foreach (var c in _w.Crew)
        {
            if (c.Dead || c.Down) continue;
            if (level >= WarnLevel.Manual && (c.Room == m.Room || c.Outside)) c.Interrupt(_w);
        }
        _w.Board.RequestScan();
    }

    /// <summary>자동화: 방이 비면 그 방 격벽을 닫고 댐퍼를 닫는다 (새도 옆방까지 번지지 않게).</summary>
    private void TrySeal(IncomingMeteor m)
    {
        if (m.Room is not Room room || room.Detached || !_w.Automation.Doors) return;
        bool occupied = _w.Crew.Any(c => !c.Dead && !c.Outside && c.Room == room);
        if (occupied) return;
        foreach (var d in room.Doors)
        {
            if (d.IsExternal || !d.Powered || d.Locked || d.Removed || d.JammedOpen) continue;
            d.Locked = true;
            m.SealedDoors.Add(d);
        }
        m.Sealed = true;
        PreSeals++;
        _w.Log.Add(_w.Tick, LogKind.Ship, $"자동화: {Ko.IGa(room.Name)} 비었다 — 충돌 대비로 격벽·댐퍼를 미리 닫았다 ({m.MinutesLeft(_w.Tick):0.#}분 전)");
        MarkLog.Add(room.Marks, _w.Tick, "충돌 대비로 격벽을 미리 닫았다");
    }

    private void Strike(IncomingMeteor m)
    {
        if (m.Warned == WarnLevel.None) Unwarned++;
        else
        {
            Warned++;
            if (m.Warned == WarnLevel.Sensor) WarnedBySensor++;
        }
        float lead = m.WarnedAt >= 0 ? (m.Arrive - m.WarnedAt) / (float)SimTime.Minutes(1) : 0f;
        Incidents.Meteor(_w, m.Target, m.Size, m.Warned, lead);
        // 부딪힌 방이 새지 않으면 미리 닫았던 격벽을 다시 연다 (새면 감압 잠금이 이어받는다)
        if (m.Room is Room room && !room.Lockdown && !room.Leaking)
            foreach (var d in m.SealedDoors)
            {
                var other = d.RoomA == room ? d.RoomB : d.RoomA;
                if (other == null || !other.Lockdown) d.Locked = false;
            }
    }
}
