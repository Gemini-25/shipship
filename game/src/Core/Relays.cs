using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v23 주컴퓨터 대개편 2단계: 구역 중계기.
//   배를 방 여럿씩 구역으로 나누고, 구역마다 벽에 무전 중계기를 단다. 로봇 · 사람의 무전은 중계기를 거쳐 주 컴퓨터에 닿는다.
//   (감지기는 데이터선으로 따로 간다 — 중계기가 꺼져도 감지기는 읽힌다.)
//   중계기가 꺼지면 (불에 타거나 · 폭발에 부서지거나 · 정전에 전지가 다 닳으면) 그 구역은 끊긴다:
//     주 컴퓨터는 그 구역 로봇의 눈 · 사람의 무전을 못 받고, 그 구역 로봇을 부르지 못한다.
//     끊긴 구역의 로봇은 스스로 판단한다 (눈앞의 불 · 피 흘리는 사람).
//   주 컴퓨터는 드론을 선체 밖 그 구역 곁에 띄워 임시로 잇고, 로봇(못 하면 사람)이 케이블로 중계기를 고친다.
//   주 컴퓨터실이 있는 구역은 직접 닿는다.

public sealed class RelayZone
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public List<int> Rooms { get; } = new();
    /// <summary>중계기가 달린 방 · 칸.</summary>
    public int RoomId { get; set; } = -1;
    public Cell At { get; set; }
    public float Health { get; set; } = 1f;
    public bool Broken { get; set; }
    /// <summary>정전 때 버티는 전지 (가득이면 30분).</summary>
    public float Battery { get; set; } = 1f;
    /// <summary>지금 무전이 주 컴퓨터에 닿나 (중계기 · 임시 중계 드론 · 주 컴퓨터실 구역).</summary>
    public bool Up { get; set; } = true;
    public long DownSince { get; set; } = -1;
    /// <summary>임시 중계를 맡은 드론 (-1 없음).</summary>
    public int DroneId { get; set; } = -1;
    public string Why { get; set; } = "";
}

public sealed class RelayStats
{
    public int Cuts, Restored, Burned, Blasted, Drained, DroneRelays, Fixed, SelfActs, Unheard;
    public string Line() => $"끊김 {Cuts} (불 {Burned} · 폭발 {Blasted} · 전지 {Drained}) · 되살아남 {Restored} · 드론 임시 중계 {DroneRelays} · 고침 {Fixed} · 끊긴 구역 로봇이 스스로 {SelfActs} · 못 들은 무전 {Unheard}";
}

public sealed class RelaySystem
{
    /// <summary>시험: 끄면 무전이 늘 닿는다 (예전처럼).</summary>
    public static bool On = true;
    private readonly World _w;
    public List<RelayZone> Zones { get; } = new();
    /// <summary>방 번호 → 구역 번호 (-1 아직 없음 — 증축한 방은 가까운 구역에 붙인다).</summary>
    public List<int> ZoneOfRoom { get; } = new();
    public RelayStats Stats { get; } = new();
    private long _nextSelf;

    public RelaySystem(World w) => _w = w;

    public RelayZone? ZoneOf(Room? r)
    {
        if (r == null) return null;
        Build();
        while (ZoneOfRoom.Count <= r.Id) ZoneOfRoom.Add(-1);
        if (ZoneOfRoom[r.Id] < 0 && Zones.Count > 0)
        {
            var z = Zones.OrderBy(z => Zones.Count == 0 ? 0f : (CenterOf(z) - r.Center).LengthSquared()).ThenBy(z => z.Id).First();
            z.Rooms.Add(r.Id);
            ZoneOfRoom[r.Id] = z.Id;
        }
        return ZoneOfRoom[r.Id] >= 0 ? Zones[ZoneOfRoom[r.Id]] : null;
    }

    /// <summary>이 방의 무전이 주 컴퓨터에 닿나 (방 밖 · 선외는 닿는다고 본다 — 선외 무전은 따로).</summary>
    public bool Reach(Room? r) => !On || r == null || ZoneOf(r) is not RelayZone z || z.Up;

    private Vector2 CenterOf(RelayZone z)
    {
        var rooms = z.Rooms.Where(i => i < _w.Ship.Rooms.Count).Select(i => _w.Ship.Rooms[i]).ToList();
        return rooms.Count == 0 ? Vector2.Zero : rooms.Aggregate(Vector2.Zero, (a, r) => a + r.Center) / rooms.Count;
    }

    /// <summary>처음 한 번: 방을 가로 순서로 묶어 구역을 만들고 구역마다 중계기 자리를 고른다 (복도 먼저 · 구역 한가운데).</summary>
    private void Build()
    {
        if (Zones.Count > 0) return;
        var ship = _w.Ship;
        var rooms = ship.LiveRooms.OrderBy(r => r.Center.X).ThenBy(r => r.Center.Y).ThenBy(r => r.Id).ToList();
        if (rooms.Count == 0) return;
        int n = Math.Clamp((int)MathF.Round(rooms.Count / 7f), 2, 8);
        if (rooms.Count < 4) n = 1;
        for (int i = 0; i < n; i++) Zones.Add(new RelayZone { Id = i, Name = $"{i + 1}구역" });
        while (ZoneOfRoom.Count < ship.Rooms.Count) ZoneOfRoom.Add(-1);
        for (int i = 0; i < rooms.Count; i++)
        {
            int zi = Math.Min(n - 1, i * n / rooms.Count);
            Zones[zi].Rooms.Add(rooms[i].Id);
            ZoneOfRoom[rooms[i].Id] = zi;
        }
        foreach (var z in Zones)
        {
            var c = CenterOf(z);
            var host = z.Rooms.Select(i => ship.Rooms[i]).Where(r => r.Cells.Any(ship.IsOpenFloor))
                .OrderBy(r => r.Type == RoomType.Corridor ? 0 : 1).ThenBy(r => (r.Center - c).LengthSquared()).ThenBy(r => r.Id).FirstOrDefault()
                ?? ship.Rooms[z.Rooms[0]];
            z.RoomId = host.Id;
            z.At = host.Cells.Where(ship.IsOpenFloor).OrderBy(x => (x.Center - host.Center).LengthSquared()).ThenBy(x => x.X).ThenBy(x => x.Y).FirstOrDefault();
        }
    }

    /// <summary>시스템 주기마다: 중계기 상태 (불 · 정전 · 떨어져 나감) · 구역이 닿나 · 임시 중계 드론 · 끊긴 구역 로봇의 판단.</summary>
    public void Update(float dt)
    {
        var w = _w;
        if (!On) return;
        Build();
        foreach (var z in Zones)
        {
            var host = z.RoomId >= 0 && z.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[z.RoomId] : null;
            if (host == null) continue;
            if (!z.Broken)
            {
                if (host.Detached) Break(z, "방이 떨어져 나갔다");
                else if (w.Fire.Count > 0 && w.Fire.AnyWithin(z.At, 2.2f))
                {
                    float heat = 0f;
                    foreach (var (c, v) in w.Fire.Fires) if ((c.X - z.At.X) * (c.X - z.At.X) + (c.Y - z.At.Y) * (c.Y - z.At.Y) <= 5) heat = MathF.Max(heat, v);
                    z.Health -= 2.5f * heat * dt; // 활활 타는 불 곁이면 15분 남짓
                    if (z.Health <= 0.3f) { Stats.Burned++; Break(z, "불에 탔다"); }
                }
            }
            bool powered = host.Powered && !host.Detached;
            if (powered) z.Battery = MathF.Min(1f, z.Battery + dt / 2f);
            else if (!z.Broken) z.Battery = MathF.Max(0f, z.Battery - dt / 0.5f);
            Eval(z);
            bool relay = RelayOn(z);
            bool direct = Direct(z);
            // 중계기가 살아나면 임시 중계 드론은 돌아간다
            if (relay && z.DroneId >= 0) { w.Drones.EndZoneRelay(z); z.DroneId = -1; }
            // 끊긴 지 1분: 드론을 선체 밖 그 구역 곁에 띄운다 (주 컴퓨터 · 보조 두뇌가 드론을 몰 수 있을 때)
            if (!relay && !direct && z.DroneId < 0 && z.DownSince >= 0 && w.Tick - z.DownSince >= SimTime.Minutes(1) && w.Automation.DroneControl && w.Commander.OpensDoors)
                if (w.Drones.StartZoneRelay(z) is Drone d) { z.DroneId = d.Id; Stats.DroneRelays++; }
        }
        if (w.Tick >= _nextSelf) { _nextSelf = w.Tick + 10; SelfAct(); }
    }

    private bool Powered(RelayZone z) => z.RoomId >= 0 && z.RoomId < _w.Ship.Rooms.Count && _w.Ship.Rooms[z.RoomId] is Room h && h.Powered && !h.Detached;
    private bool RelayOn(RelayZone z) => !z.Broken && (Powered(z) || z.Battery > 0f);
    private bool Direct(RelayZone z) => _w.Automation.ComputerBody?.Room is Room core && z.Rooms.Contains(core.Id) && _w.Automation.CoreOnline;

    /// <summary>지금 닿나 다시 따진다 (부서지거나 고친 그 자리에서 바로).</summary>
    private void Eval(RelayZone z)
    {
        var w = _w;
        bool relay = RelayOn(z), drone = DroneHolds(z);
        bool up = relay || drone || Direct(z);
        if (up == z.Up) return;
        z.Up = up;
        var host = z.RoomId >= 0 ? w.Ship.Rooms[z.RoomId] : null;
        if (!up)
        {
            z.DownSince = w.Tick;
            bool powered = Powered(z);
            z.Why = z.Broken ? z.Why : !powered ? "정전 — 전지가 다 닳았다" : "꺼졌다";
            if (!z.Broken && !powered) Stats.Drained++;
            Stats.Cuts++;
            w.Log.Add(w.Tick, LogKind.Warning, $"{w.Automation.Voice.Call}: {z.Name} 중계기가 꺼졌다 ({z.Why}) — 그 구역 무전이 끊겼다");
            w.History.Add(w, HistoryKind.Response, $"{z.Name} 무전이 끊겼다 — {z.Why}", host);
        }
        else
        {
            Stats.Restored++;
            w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: {z.Name} 무전이 다시 닿는다 ({(relay ? "중계기" : drone ? "드론 임시 중계" : "주 컴퓨터실")} · {(w.Tick - z.DownSince) / (float)SimTime.Minutes(1):0}분 만에)");
            z.DownSince = -1;
        }
    }

    private bool DroneHolds(RelayZone z) =>
        z.DroneId >= 0 && _w.Drones.Drones.FirstOrDefault(d => d.Id == z.DroneId) is Drone d && d.Errand == DroneErrand.Zone && d.GuardFor == z.Id && d.State == DroneState.Working && !d.Faulty && !d.Wrecked;

    public void Break(RelayZone z, string why)
    {
        if (z.Broken) return;
        z.Broken = true;
        z.Health = MathF.Min(z.Health, 0.3f);
        z.Why = why;
        var host = z.RoomId >= 0 ? _w.Ship.Rooms[z.RoomId] : null;
        if (host != null) MarkLog.Add(host.Marks, _w.Tick, $"{z.Name} 중계기 — {why}");
        _w.Board.RequestScan();
        Eval(z);
    }

    /// <summary>폭발이 그 방을 흔들었다 (깨진 조명의 비율만큼).</summary>
    public void Blast(Room room, float share)
    {
        if (!On) return;
        foreach (var z in Zones)
            if (z.RoomId == room.Id && !z.Broken && share >= 0.25f)
            {
                z.Health -= share;
                if (z.Health <= 0.3f) { Stats.Blasted++; Break(z, "폭발에 부서졌다"); }
            }
    }

    /// <summary>고쳤다 (케이블 1).</summary>
    public void Fix(RelayZone z)
    {
        z.Broken = false;
        z.Health = 1f;
        z.Why = "";
        Stats.Fixed++;
        Eval(z);
    }

    /// <summary>
    /// 끊긴 구역의 로봇은 스스로 판단한다 (지휘관이 부를 수 없다): 손이 비었으면 눈앞의 불(같은 방 · 6칸 안)에 거품을 뿌리고,
    /// 피 흘리는 사람이 같은 방에 있으면 곁에 가 상처를 누른다.
    /// </summary>
    private void SelfAct()
    {
        var w = _w;
        if (Zones.All(z => z.Up)) return;
        foreach (var r in w.Robots.Robots)
        {
            if (r.CaseId >= 0 || !RobotSystem.Callable(w, r) || r.State != RobotState.Active && r.State != RobotState.Docked) continue;
            if (r.Room is not Room room || Reach(room)) continue;
            if (r.Order is WorkOrder o && o.Urgency >= 0.9f) continue;
            if (r.FightingFire || r.Steps != null && r.Steps.Any(s => s is RPress)) continue;
            if (r.Foam > 0.2f && w.Fire.Count > 0 && w.Fire.CountIn(room) > 0 && w.Fire.AnyWithin(r.Cell, 6f))
            {
                w.Robots.DropTaskFor(r);
                if (w.Robots.FightIn(r, room, $"무전이 끊겼다 — 스스로 {room.Name} 불로"))
                {
                    Stats.SelfActs++;
                    r.Mind.Say("무전이 안 닿는다 — 눈앞의 불부터", w.Tick);
                    MarkLog.Add(r.Marks, w.Tick, $"무전이 끊긴 {room.Name}에서 스스로 불을 껐다");
                }
                continue;
            }
            var hurt = w.Crew.FirstOrDefault(c => !c.Dead && c.Room == room && w.Casualty.Of(c) is Trauma t && t.Kind == TraumaKind.Bleed && !t.Closed);
            if (hurt != null && !RobotBody.Gripless(r) && w.Robots.Robots.All(x => x == r || !(x.Steps != null && x.StepIndex < x.Steps.Count && x.Steps[x.StepIndex] is RPress rp && rp.Patient == hurt)))
            {
                w.Robots.DropTaskFor(r);
                if (w.Robots.GoTo(r, hurt, $"무전이 끊겼다 — 스스로 {hurt.Name} 곁으로"))
                {
                    Stats.SelfActs++;
                    r.Mind.Say($"무전이 안 닿는다 — {Ko.IGa(hurt.Name)} 피를 흘린다", w.Tick);
                }
            }
        }
    }

    /// <summary>무전을 못 들었다 (끊긴 구역에서 본 것 — 주 컴퓨터는 모른다).</summary>
    public void Unheard() => Stats.Unheard++;

    public string Summary()
    {
        var down = Zones.Where(z => !z.Up).ToList();
        return down.Count == 0 ? $"구역 {Zones.Count} · 모두 닿음" : $"끊김: {string.Join(" · ", down.Select(z => $"{z.Name}({z.Why})"))}";
    }
}
