using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.3 물 · 습기 · 전기: 새는 관 → 바닥에 고인 물 → (전기가 살아 있으면) 누전·감전·전기 화재.
// 컴퓨터(또는 사람)가 방 분전함을 내려 격리하고, 사람이 급수 밸브를 잠그고 물을 퍼낸다(대부분 탱크로 돌아간다).
// 습한 방의 찬 배관에는 결로가 맺히고, 오래 습하면 관이 삭는다. 전기가 돌아올 때 설비가 한꺼번에 켜지면 차단기가 떨어진다(기동 전류).

public sealed class MoistureStats
{
    public int Floods, Shorts, Shocks, ElectricFires, AutoIsolations, CrewIsolations, Restores, Valves, Inrush;
    public float Condensed, Pumped, Recovered, Leaked;
    public override string ToString() =>
        $"침수 {Floods} · 누전 {Shorts} · 감전 {Shocks} · 전기 화재 {ElectricFires} · 분전함 차단 {AutoIsolations}(컴퓨터)+{CrewIsolations}(사람) · 다시 올림 {Restores} · " +
        $"밸브 {Valves} · 기동 전류 트립 {Inrush} · 샌 물 {Leaked:0}L · 결로 {Condensed:0}L · 퍼냄 {Pumped:0}L(탱크로 {Recovered:0}L)";
}

public sealed class MoistureSystem
{
    private readonly World _w;
    public MoistureStats Stats { get; } = new();
    private readonly Dictionary<int, bool> _wasPowered = new();
    private readonly Dictionary<int, (int tries, long last)> _inrush = new();
    private readonly Dictionary<int, long> _floodSeen = new();
    private readonly HashSet<int> _noticed = new();

    /// <summary>물이 찬 걸 누가 안다: 감지기(주 컴퓨터)가 보거나, 사람이 그 방·옆방에서 봤다.</summary>
    public bool Noticed(Room r) => _noticed.Contains(r.Id);

    public MoistureSystem(World w) => _w = w;

    /// <summary>물 깊이 0~1 (칸마다 20L면 1 — 발목쯤).</summary>
    public static float Depth(Room r) => r.Cells.Count == 0 ? 0f : r.Flood / (r.Cells.Count * 20f);
    public static float DepthCm(Room r) => Depth(r) * 12f;

    /// <summary>방마다 평소 습도 (재배실·주방은 습하다).</summary>
    public static float BaseHumidity(Room r) => r.Type switch
    {
        RoomType.Hydroponics => 0.68f, RoomType.Galley => 0.55f, RoomType.LifeSupport => 0.5f,
        RoomType.Quarters or RoomType.Mess or RoomType.Medbay => 0.45f, _ => 0.38f,
    };

    /// <summary>찬 배관이 지나는 방 (결로가 맺힌다).</summary>
    private bool Cold(Room r) => r.Type is RoomType.Cooling or RoomType.Reactor
        || _w.Piping.Segments.Any(s => s.IsCoolant && s.Path.Any(c => _w.Ship.RoomAt(c) == r));

    public void AddWater(Room? room, float liters)
    {
        if (room == null || room.Detached || liters <= 0f) return;
        room.Flood += liters;
        Stats.Leaked += liters;
    }

    public void Update(float dt)
    {
        var w = _w;
        var ship = w.Ship;
        foreach (var room in ship.Rooms)
        {
            if (room.Detached) { room.Flood = 0f; continue; }
            int cells = Math.Max(1, room.Cells.Count);

            // 진공: 물이 끓어 날아간다
            if (room.Air.Pressure < 30f) { room.Flood = MathF.Max(0f, room.Flood - 400f * dt); room.Humidity = MathF.Max(0f, room.Humidity - 2f * dt); continue; }

            // 1) 새는 곳: 설비 관 이음이 빠졌는데 밸브가 열려 있다
            foreach (var f in room.Furniture)
                if (f.Machine is Machine m && Procedures.Plumbed(f.Type) && m.Line < 0.3f && room.WaterLinked && !room.ValveShut && w.Water.Level > 1f)
                {
                    float l = MathF.Min(w.Water.Level, 6f * dt);
                    w.Water.Level -= l;
                    AddWater(room, l);
                }

            // 2) 습도: 방마다 평소 값으로, 환기가 돌면 빨리 마른다 · 고인 물은 증발해 습도를 올린다
            bool vented = Atmosphere.Vented(room);
            float depth = Depth(room);
            float target = BaseHumidity(room) + MathF.Min(0.3f, depth * 0.8f) + (vented ? -0.06f : 0.12f);
            room.Humidity = Math.Clamp(room.Humidity + (target - room.Humidity) * (vented ? 0.5f : 0.15f) * dt, 0.05f, 1f);
            if (room.Flood > 0f)
            {
                float evap = MathF.Min(room.Flood, 0.35f * cells * (vented ? 1.5f : 0.5f) * dt);
                room.Flood -= evap;
                room.Humidity = MathF.Min(1f, room.Humidity + evap / (cells * 40f));
            }

            // 3) 결로: 습한 방의 찬 배관에 물방울이 맺힌다
            if (room.Humidity > 0.72f && Cold(room))
            {
                float c = (room.Humidity - 0.72f) * 7f * dt;
                room.Flood += c;
                Stats.Condensed += c;
                room.Humidity -= c / (cells * 60f);
            }

            // 4) 오래 습하면 관이 삭고 감지기가 틀어진다
            if (room.Humidity > 0.8f)
            {
                foreach (var s in w.Piping.Segments)
                    if (s.Path.Count > 0 && ship.RoomAt(s.Path[s.Path.Count / 2]) == room) s.Integrity = MathF.Max(0.3f, s.Integrity - 0.003f * dt);
                foreach (var f in room.Furniture) if (f.Machine is Machine m) m.SensorCal = MathF.Max(0.3f, m.SensorCal - 0.01f / 24f * dt);
            }
        }

        // 5) 열린 문으로 물이 번진다 (깊은 쪽에서 얕은 쪽으로)
        foreach (var d in ship.Doors)
        {
            if (d.Removed || d.Openness < 0.5f || d.RoomA is not Room a || d.RoomB is not Room b || a.Detached || b.Detached) continue;
            float da = Depth(a), db = Depth(b);
            if (MathF.Abs(da - db) < 0.04f) continue;
            var (hi, lo) = da > db ? (a, b) : (b, a);
            float move = MathF.Min(hi.Flood * 0.5f, (MathF.Abs(da - db) * Math.Max(1, hi.Cells.Count) * 20f) * 0.3f * dt);
            hi.Flood -= move;
            lo.Flood += move;
        }

        // 6) 물 + 살아 있는 전기
        foreach (var room in ship.LiveRooms)
        {
            if (room.Detached) continue;
            float depth = Depth(room);
            if (depth > 0.12f && !_floodSeen.ContainsKey(room.Id)) { _floodSeen[room.Id] = w.Tick; Stats.Floods++; w.RaiseAlert($"{room.Name} 바닥에 물이 찼다 ({DepthCm(room):0}cm)", room, AlertLevel.Warning, shipWide: false); w.Board.RequestScan(); }
            if (depth < 0.04f) { _floodSeen.Remove(room.Id); _noticed.Remove(room.Id); }
            if (depth > 0.08f && !_noticed.Contains(room.Id)
                && (w.Automation.MainOnline && room.DataLinked || w.Crew.Any(c => c.CanAct && c.IsAwake && (c.Room == room || c.Room != null && room.Doors.Any(d => d.Openness > 0.3f && (d.RoomA == c.Room || d.RoomB == c.Room))))))
            {
                _noticed.Add(room.Id);
                w.Board.RequestScan();
            }
            if (depth <= 0.12f || !room.Powered) continue;
            int node = w.Causes.OpenNode($"flood:{room.Id}");
            using var because = w.Causes.Because(node);

            // 컴퓨터는 보수적으로: 물이 찬 방의 분전함을 통째로 내린다 (피해는 막지만 그 방 설비도 다 멈춘다)
            // v12.5 관제석에 사람이 있으면 좁게: 깊거나 물에 선 사람이 있을 때만 내린다 (그 전엔 지켜본다)
            var op = w.Automation.Operator;
            bool someoneInWater = w.Crew.Any(c => !c.Dead && c.Room == room && c.Suit == null);
            // (v13.2 깊은 물은 기다리지 않는다 — 확인하는 사이 누전이 먼저 나지 않게)
            if (w.Automation.Level >= 2 && w.Automation.MainOnline && room.DataLinked && _floodSeen.TryGetValue(room.Id, out var seen) && (depth > 0.25f || w.Tick - seen > SimTime.Minutes(3))
                && (op == null || depth > 0.25f || someoneInWater))
            {
                w.Automation.Reason($"flood:{room.Id}", $"{room.Name} 바닥 물 {DepthCm(room):0}cm · 습도 {room.Humidity * 100:0}% — 누전 위험 · 조치: 분전함 차단" + (op == null ? " (그 방 설비도 멈춘다)" : $" ({op.Name}: 더는 못 기다린다)") + " · 요청: 물 퍼내기, 새는 곳 점검", SimTime.Hours(2));
                Isolate(room, op);
                continue;
            }
            if (op != null && w.Automation.MainOnline && depth <= 0.25f && !someoneInWater)
                w.Automation.Reason($"floodwatch:{room.Id}", $"{room.Name} 바닥 물 {DepthCm(room):0}cm — 아직 얕다, 전기를 살려 두고 지켜본다 (분전함은 깊어지면)", SimTime.Hours(2));
            // 누전: 그 방 회로가 단락된다
            if (w.Rng.Chance(Matter.ShortRate(depth) * dt) && ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine panel && !panel.Faults.Any(f => f.Circuit == room.Circuit))
            {
                panel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = room.Circuit });
                panel.FaultCount++;
                w.Causes.OnFault(panel, panel.Faults[^1]);
                Stats.Shorts++;
                w.RaiseAlert($"{room.Name} 누전 — 바닥의 물로 {PowerGrid.CircuitName(room.Circuit)} 회로가 단락됐다", room, AlertLevel.Warning, shipWide: true);
            }
            // 전기 화재: 물에 잠긴 콘센트·설비에서 불꽃
            if (w.Rng.Chance(Matter.SparkRate * depth * dt))
            {
                var m = room.Furniture.Where(f => f.Machine is Machine mm && mm.Powered && mm.Spec.PowerDraw > 0f).Select(f => f.Machine!).FirstOrDefault();
                var spot = m?.Body.UseSpots.FirstOrDefault(s => ship.IsOpenFloor(s)) ?? room.Cells.FirstOrDefault(ship.IsOpenFloor);
                if (spot != default && w.Fire.Ignite(spot, 0.3f))
                {
                    Stats.ElectricFires++;
                    w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name} 물에 잠긴 {(m?.Name ?? "배선")}에서 불꽃이 튀어 불이 붙었다");
                }
            }
            // 감전: 물에 선 사람 (우주복은 막아 준다)
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Outside || c.Room != room || c.Suit != null) continue;
                if (!w.Rng.Chance(Matter.ShockRate * depth * dt * w.Matter.ShockMul(c))) continue; // v16.4 고무 매트 · 고무 바닥은 막는다
                float dmg = w.Rng.Range(0.08f, 0.25f) * (0.5f + depth);
                c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg);
                NeedsSystem.AddInjury(c.Vitals, dmg * 0.8f, "감전");
                c.Interrupt(w);
                Stats.Shocks++;
                w.Causes.Effect(CauseKind.Shock, "", $"{c.Name} 감전 ({room.Name} 바닥 물)", room, c.Position, lasting: false);
                w.Log.Add(w.Tick, LogKind.Warning, $"물에 선 채 감전됐다 (체력 {c.Vitals.Health * 100:0}%)", c.Id);
                MarkLog.Add(c.Memory.Marks, w.Tick, $"{room.Name}에서 감전");
                Memory.Shake(w, c, 0.08f, "물에 선 채 감전됐다");
            }
        }

        // v12.5 III 조정: 새는 설비의 방 급수 밸브를 원격으로 잠근다 · 관제석 사람은 오경보로 내린 분전함을 원격으로 되올린다
        foreach (var room in ship.LiveRooms)
        {
            if (room.Detached || !w.Automation.MainOnline || !room.DataLinked) continue;
            if (w.Automation.Level >= 3 && LeakSource(room) && room.WaterLinked && !room.ValveShut)
            {
                room.ValveShut = true;
                Stats.Valves++;
                w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터가 {room.Name} 급수 밸브를 원격으로 잠갔다 — 설비 관 이음에서 샌다");
                w.Automation.Reason($"valve:{room.Id}", $"{room.Name} 급수 유량이 새는 쪽으로 빠진다 — 원격 밸브 잠금 (그 방은 단수) · 요청: 관 이음 고치기");
            }
            if (w.Automation.Operator is CrewMember op2 && room.BreakerOff && Depth(room) < 0.05f && w.Fire.CountIn(room) == 0)
            {
                w.Automation.Reason($"restore:{room.Id}", $"{room.Name} 바닥이 말랐다 — 분전함 원격으로 다시 올림");
                Restore(room, op2);
            }
        }

        // 7) 기동 전류: 방에 전기가 돌아오는 순간 설비가 한꺼번에 켜진다
        foreach (var room in ship.LiveRooms)
        {
            bool was = _wasPowered.TryGetValue(room.Id, out var p) ? p : room.Powered;
            _wasPowered[room.Id] = room.Powered;
            if (was || !room.Powered || room.Detached) continue;
            float draw = room.Furniture.Where(f => f.Machine is Machine m && !m.Stopped && m.Spec.PowerDraw > 0f).Sum(f => f.Machine!.Spec.PowerDraw);
            if (draw < 4f) continue;
            var (tries, last) = _inrush.TryGetValue(room.Id, out var t) ? t : (0, 0L);
            if (w.Tick - last > SimTime.Hours(1)) tries = 0;
            float chance = MathF.Min(0.7f, 0.12f + 0.03f * draw) * MathF.Pow(0.5f, tries) * (w.Automation.Operator != null ? 0.2f : w.Automation.Level >= 3 ? 0.4f : 1f) * w.Automation.InrushMul(room.Circuit); // III부터 컴퓨터가 차례로 켠다, 사람이 조종하면 더 잘 · v16.20 원격으로 올린 회로는 큰 설비부터 하나씩
            if (!w.Rng.Chance(chance)) continue;
            if (ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is not Machine panel || panel.Faults.Any(f => f.Circuit == room.Circuit)) continue;
            _inrush[room.Id] = (tries + 1, w.Tick);
            using (w.Causes.Because(w.Causes.RecentNode(room.Id, CauseKind.Outage)))
            {
                panel.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = room.Circuit });
                panel.FaultCount++;
                w.Causes.OnFault(panel, panel.Faults[^1]);
            }
            Stats.Inrush++;
            w.Log.Add(w.Tick, LogKind.Warning, $"기동 전류 — {room.Name} 설비가 한꺼번에 켜지며 {PowerGrid.CircuitName(room.Circuit)} 회로 차단기가 떨어졌다 ({draw:0}kW)");
            MarkLog.Add(room.Marks, w.Tick, $"기동 전류로 차단기 트립 ({draw:0}kW)");
        }
    }

    /// <summary>방 분전함을 내린다 (그 방만 정전 — 누전·감전을 막는다).</summary>
    public void Isolate(Room room, CrewMember? by)
    {
        if (room.BreakerOff) return;
        room.BreakerOff = true;
        if (by == null) Stats.AutoIsolations++; else Stats.CrewIsolations++;
        string who = by == null ? "주 컴퓨터가" : Ko.IGa(by.Name);
        _w.Log.Add(_w.Tick, LogKind.Ship, $"{who} {room.Name} 분전함을 내렸다 — 바닥에 물 (누전·감전 방지)" + (by == null ? " · 그 방 설비도 모두 멈춘다" : ""), by?.Id ?? -1);
        MarkLog.Add(room.Marks, _w.Tick, $"{(by == null ? "주 컴퓨터" : by.Name)}: 분전함 차단 (침수)");
        _w.Board.RequestScan();
    }

    public void Restore(Room room, CrewMember by)
    {
        if (!room.BreakerOff) return;
        room.BreakerOff = false;
        Stats.Restores++;
        _w.Log.Add(_w.Tick, LogKind.Work, $"{room.Name} 분전함을 다시 올렸다", by.Id);
        MarkLog.Add(room.Marks, _w.Tick, $"{by.Name}: 분전함 다시 올림");
        _w.Board.RequestScan();
    }

    /// <summary>그 방에서 물이 새는 설비 (관 이음이 빠졌다).</summary>
    public static bool LeakSource(Room r) => r.Furniture.Any(f => f.Machine is Machine m && Procedures.Plumbed(f.Type) && m.Line < 0.3f);
}

public sealed partial class WorkBoard
{
    /// <summary>v12.3 침수: 분전함 내리기(물 + 전기) · 급수 밸브 잠그기/열기 · 물 퍼내기 · 분전함 다시 올리기.</summary>
    private void ScanMoisture(Poster post)
    {
        var w = _world;
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.Detached || room.Abandoned || room.OffLimits) continue;
            float depth = MoistureSystem.Depth(room);
            if (depth > 0.06f && !w.Moisture.Noticed(room)) continue; // 아무도 모르는 물
            var spot = room.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => (c.Center - room.Center).LengthSquared()).FirstOrDefault();
            if (spot == default) continue;
            bool vital = room.Type is RoomType.LifeSupport or RoomType.Reactor or RoomType.Cooling or RoomType.Power or RoomType.Medbay or RoomType.Bridge;
            if (depth > 0.12f && room.Powered && !room.BreakerOff && !w.Automation.MainOnline)
                post(WorkKind.IsolateRoom, WorkTarget.AtCell(spot, room), 1.0f, Skill.Electrical, $"바닥에 물 {MoistureSystem.DepthCm(room):0}cm — 전기가 살아 있다 (누전·감전)");
            if (MoistureSystem.LeakSource(room) && room.WaterLinked && !room.ValveShut)
                post(WorkKind.ShutRoomValve, WorkTarget.AtCell(spot, room), 0.75f, Skill.Mechanics, "설비 관 이음이 빠져 물이 샌다 — 급수 밸브를 잠근다");
            if (room.ValveShut && !MoistureSystem.LeakSource(room))
                post(WorkKind.OpenRoomValve, WorkTarget.AtCell(spot, room), vital || room.Type == RoomType.Hydroponics ? 0.7f : 0.45f, Skill.Mechanics, "새던 관을 고쳤다 — 급수 밸브를 다시 연다");
            if (depth > 0.06f)
                post(WorkKind.PumpOut, WorkTarget.AtCell(spot, room), MathF.Min(0.95f, 0.35f + depth * 0.8f + (room.Powered && !room.BreakerOff ? 0.15f : 0f) + (vital ? 0.1f : 0f)), Skill.Mechanics,
                    $"바닥에 물 {MoistureSystem.DepthCm(room):0}cm ({room.Flood:0}L)");
            if (room.BreakerOff && depth < 0.05f && w.Fire.CountIn(room) == 0)
                post(WorkKind.BreakerOn, WorkTarget.AtCell(spot, room), vital ? 0.95f : 0.6f, Skill.Electrical, "물을 퍼냈다 — 분전함을 다시 올린다 (기동 전류 조심)");
        }
    }
}

public static partial class WorkPlanners
{
    private static Job? RoomSwitch(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.CurrentRoom;
        if (room == null) { w.Board.Close(o); return null; }
        var toils = Plans.DropOff(c, w, dist);
        if (o.Kind is WorkKind.IsolateRoom or WorkKind.BreakerOn) w.Matter.Footing(c, dist, toils, at); // v16.4 젖은 바닥이면 고무 매트부터 (고무 = 절연)
        toils.Add(new GotoToil(at));
        float hours = o.Kind switch { WorkKind.IsolateRoom => 0.04f, WorkKind.BreakerOn => 0.12f, _ => 0.1f };
        toils.Add(new WorkToil(hours, o.Skill, at.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            switch (o.Kind)
            {
                case WorkKind.IsolateRoom: world.Moisture.Isolate(room, cm); break;
                case WorkKind.BreakerOn: world.Moisture.Restore(room, cm); break;
                case WorkKind.ShutRoomValve:
                    room.ValveShut = true;
                    world.Moisture.Stats.Valves++;
                    world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 급수 밸브를 잠갔다 — 새는 물이 멈춘다 (그 방은 단수)", cm.Id);
                    MarkLog.Add(room.Marks, world.Tick, $"{cm.Name}: 급수 밸브 잠금");
                    break;
                case WorkKind.OpenRoomValve:
                    room.ValveShut = false;
                    world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 급수 밸브를 다시 열었다", cm.Id);
                    MarkLog.Add(room.Marks, world.Tick, $"{cm.Name}: 급수 밸브 다시 엶");
                    break;
            }
            return true;
        }));
        return Wrap(a, o, c, w, WorkKinds.Name(o.Kind), toils, $"{room.Name} {WorkKinds.Name(o.Kind)}");
    }

    /// <summary>물 퍼내기: 양동이·손펌프로 30분에 100L쯤 — 대부분은 정수기로 돌려보낸다.</summary>
    private static Job? PumpOut(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.CurrentRoom;
        if (room == null || room.Flood < 1f) { if (room != null) w.Board.Close(o); return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, o.Skill, at.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            float take = MathF.Min(room.Flood, (90f + 40f * cm.SkillLevel(Skill.Mechanics)) * (world.History.Doctrine.FloodDrill ? 1.5f : 1f));
            room.Flood -= take;
            float back = take * 0.7f;
            world.Water.Level += back;
            world.Moisture.Stats.Pumped += take;
            world.Moisture.Stats.Recovered += back;
            if (MoistureSystem.Depth(room) < 0.06f) world.Board.Close(o);
            else o.Progress = 0f; // v16.7: 한 번 퍼낸 뒤 진척이 남아 있으면 다음 양동이가 2틱 만에 끝났다 (30분 일이 공짜로 되풀이됨)
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 바닥 물을 퍼냈다 ({take:0}L · {back:0}L는 정수기로)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "물 퍼내기", toils, $"{room.Name} 물 퍼내기");
    }
}
