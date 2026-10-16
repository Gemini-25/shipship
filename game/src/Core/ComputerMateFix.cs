using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.27 ③ 스스로 알아내고 고치기.
//  사각지대 정찰: 감지기가 안 닿거나(데이터선 끊김) 틀어진(문 감지기 · 값 멈춤) 방은 로봇을 보내 직접 본다 (로봇이 없으면 드론이 바깥에서 들여다본다).
//    본 것으로 믿음을 고친다 (사람 수 · 불 · 기압) — 모르던 연기를 보면 경보를 올린다.
//  원인 분석: 같은 방이 자꾸 안 보이거나 자꾸 고장 나면 까닭을 따진다 — 데이터선이 자꾸 끊긴다 → 데이터선 교체 · 감지기가 틀어진다 → 감지기 증설 ·
//    설비가 떨려 고장 난다 → 방진 받침 · 연산이 몰려 경보를 놓친다 → 코어 랙 증설. 개조안은 회의에 올라가 표결 → 통과하면 전기 솜씨 있는 사람이 공사한다.
//  늘어난 실력은 실제 장비로 보인다 (ShipViewMate — 랙 · 감지기 · 새 데이터선 · 받침 · 설치 장면) · 효과도 실제다 (연산 · 감지기 고장 · 고장률).

public enum MateGear { CoreRack, SensorNode, DataLine, Mount }
public enum GearState { Proposed, Approved, Working, Done, Rejected }

public sealed class GearUpgrade
{
    public int Id { get; init; }
    public MateGear Kind { get; init; }
    public int RoomId { get; init; }
    public string Room { get; init; } = "";
    public string Cause { get; init; } = "";
    public string Evidence { get; init; } = "";
    public long Tick { get; init; }
    public GearState State { get; set; }
    public long Decided = -1, Started = -1, Installed = -1;
    public float Need { get; init; } = 2f;
    public float Done;
    public bool Paid;
    public Cell Spot { get; set; }
    public int Yes, No;
    public SortedDictionary<int, long> Workers { get; } = new();
    public string Name => ShipMate.GearName(Kind);
}

public sealed class ScoutRun
{
    public int Id { get; init; }
    public int RoomId { get; init; }
    public string Room { get; init; } = "";
    public long Sent { get; init; }
    public string By { get; init; } = "";
    public string Unit { get; init; } = "";
    public int RobotId { get; init; } = -1;
    public bool Drone { get; init; }
    public long Seen = -1;
    public string Found = "";
    public bool Failed;
}

public sealed partial class ShipMate
{
    public List<GearUpgrade> Upgrades { get; } = new();
    public List<ScoutRun> Scouts { get; } = new();
    public int UpgradesDone, ScoutsSeen;
    private readonly SortedDictionary<int, int> _blind = new(), _faults = new(), _breaks = new();
    private readonly SortedDictionary<int, int> _faultSeen = new();
    private long _causeNext;
    private int _gearId = 1, _scoutId = 1, _overloads;

    public static string GearName(MateGear k) => k switch
    {
        MateGear.CoreRack => "코어 랙 증설", MateGear.SensorNode => "감지기 증설", MateGear.DataLine => "데이터선 교체", _ => "방진 받침",
    };

    private bool Has(MateGear k, int roomId) { foreach (var u in Upgrades) if (u.Kind == k && u.RoomId == roomId && u.State == GearState.Done) return true; return false; }
    public int CoreRacks => Upgrades.Count(u => u.Kind == MateGear.CoreRack && u.State == GearState.Done);
    /// <summary>ShipCore.Capacity 훅: 늘린 코어 랙.</summary>
    public float CapacityMul => 1f + 0.25f * CoreRacks;
    /// <summary>감지기를 더 단 방 (문 감지기 · 값이 덜 틀어진다).</summary>
    public bool SensorNode(Room r) => Has(MateGear.SensorNode, r.Id);
    /// <summary>데이터선을 바꾼 방 (차폐 — 방사선 값 멈춤이 덜하다).</summary>
    public bool Hardened(Room r) => Has(MateGear.DataLine, r.Id);
    /// <summary>방진 받침을 단 방의 설비 고장률 배율 (Systems 훅).</summary>
    public float FaultMul(Machine m) => Upgrades.Count == 0 || !Has(MateGear.Mount, m.Body.Room.Id) ? 1f : 0.6f;
    /// <summary>BeliefModel 훅: 이 방 감지기가 틀어질 확률 배율.</summary>
    public float SensorFaultMul(Room r) => Upgrades.Count == 0 ? 1f : SensorNode(r) ? 0.3f : Hardened(r) ? 0.5f : 1f;

    // ───────────── 사각지대 정찰 ─────────────

    private bool Blind(Room r, out string why)
    {
        var a = A;
        var b = a.Belief.Of(r);
        why = !r.DataLinked && !ComputerV15.Relay(_w) ? "데이터선이 끊겼다" : b.Fault == SensorFault.Blind ? "문 감지기가 틀어졌다" : b.Fault == SensorFault.Stuck ? "값이 멈췄다" : b.Trust < 0.5f ? "감지기를 못 믿는다" : "";
        return why != "";
    }

    private void FixTick()
    {
        var w = _w;
        if (!A.Present || !A.CoreOnline) return;
        // 보낸 정찰 정리
        foreach (var s in Scouts)
        {
            if (s.Seen >= 0 || s.Failed) continue;
            if (w.Tick - s.Sent > SimTime.Hours(3)) { s.Failed = true; continue; }
            if (s.Drone && w.Ship.Rooms.FirstOrDefault(r => r.Id == s.RoomId) is Room dr && dr.Joints.Any(j => j.SeenAt >= s.Sent)) SawRoom(s, dr, "드론");
        }
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Type == RoomType.Corridor || r.OffLimits || !Blind(r, out var why)) continue;
            if (Scouts.Any(s => s.RoomId == r.Id && (s.Seen < 0 && !s.Failed || w.Tick - s.Sent < SimTime.Hours(12)))) continue;
            _blind[r.Id] = _blind.GetValueOrDefault(r.Id) + 1;
            if (!r.DataLinked) _breaks[r.Id] = _breaks.GetValueOrDefault(r.Id) + 1;
            SendScout(r, why);
        }
        if (Scouts.Count > 40) Scouts.RemoveRange(0, Scouts.Count - 40);
        if (w.Tick >= _causeNext) { _causeNext = w.Tick + SimTime.Hours(6); Causes(); }
        GearWork();
    }

    /// <summary>정찰을 보낸다 (시험도 부른다): 로봇 → 없으면 드론.</summary>
    public ScoutRun? SendScout(Room r, string why)
    {
        var w = _w;
        bool spare = Crisis.Level(w) < CrisisLevel.Emergency && w.Fire.Count == 0;
        // 순찰하는 로봇(방재)만 — 고치는 로봇 · 거드는 로봇은 제 일을 둔다 · 한 번에 하나
        bool busy = Scouts.Any(s => s.Seen < 0 && !s.Failed && !s.Drone);
        var bot = !spare || busy ? null : w.Robots.Robots.Where(x => RobotsV15.Patrols(x.Kind) && !RobotsV15.Assists(x.Kind) && x.Operational && x.Order == null && x.AtDock && !x.FightingFire && x.Battery > 0.8f && !x.Dock.Room.Detached && !w.Board.OpenForRobot().Any(o => RobotSystem.CanDo(x.Kind, o.Kind)))
            .OrderBy(x => (x.Position - r.Center).LengthSquared()).ThenBy(x => x.Id).FirstOrDefault();
        ScoutRun? run = null;
        if (bot != null)
        {
            var sr = new ScoutRun { Id = _scoutId++, RoomId = r.Id, Room = r.Name, Sent = w.Tick, By = "로봇", Unit = bot.Name, RobotId = bot.Id };
            if (w.Robots.Scout(bot, r, (rb, world) => SawRoom(sr, r, rb.Name))) run = sr;
        }
        if (run == null && w.Drones.CanInspect() && r.Joints.Count > 0)
        {
            run = new ScoutRun { Id = _scoutId++, RoomId = r.Id, Room = r.Name, Sent = w.Tick, By = "드론", Unit = "점검 드론", Drone = true };
            w.Structure.Unseen.TryAdd(r.Id, w.Tick); // 검사 드론이 먼저 도는 방 (창 너머 · 연결부)
        }
        if (run == null) return null;
        Scouts.Add(run);
        A.Command.Line(run.Drone ? CmdTarget.Drone : CmdTarget.Robot, run.RobotId, r, $"{run.Unit}: {r.Name} 정찰", why, 0.6f, 30f, -1, -1, "보냄");
        Say($"{r.Name} — {why}. {Ko.EulReul(run.Unit)} 보내 직접 본다", r, -1);
        return run;
    }

    private void SawRoom(ScoutRun s, Room r, string unit)
    {
        var w = _w;
        if (s.Seen >= 0) return;
        s.Seen = w.Tick;
        ScoutsSeen++;
        var b = A.Belief.Of(r);
        int people = BeliefModel.Actual(w, r);
        bool fire = w.Fire.CountIn(r) > 0 || r.Air.Smoke > 0.25f;
        b.People = people; b.Fire = fire; b.Pressure = r.Air.Pressure; b.O2 = r.Air.O2; b.Updated = w.Tick;
        s.Found = $"사람 {people} · {(fire ? "연기" : "불 없음")} · 기압 {r.Air.Pressure:0}";
        Say($"{unit} 눈으로 {Ko.EulReul(r.Name)} 봤다 — {s.Found}", r, -1);
        if (fire && !w.Fire.IsKnown(r)) w.RaiseAlert($"{Ko.IGa(unit)} {r.Name}에서 연기를 봤다 — 감지기가 안 닿는 방", r, AlertLevel.Warning, true);
    }

    // ───────────── 원인 분석 → 개조안 ─────────────

    private void Causes()
    {
        var w = _w;
        // 설비 고장을 방마다 센다 (새로 생긴 고장만)
        foreach (var m in w.Ship.Machines)
        {
            int n = m.FaultCount;
            int was = _faultSeen.GetValueOrDefault(m.Body.Id, n);
            if (n > was) _faults[m.Body.Room.Id] = _faults.GetValueOrDefault(m.Body.Room.Id) + (n - was);
            _faultSeen[m.Body.Id] = n;
        }
        if (Upgrades.Any(u => u.State is GearState.Proposed or GearState.Approved or GearState.Working)) return; // 한 번에 하나
        foreach (var r in w.Ship.LiveRooms.OrderBy(r => r.Id))
        {
            if (r.OffLimits) continue;
            int blind = _blind.GetValueOrDefault(r.Id), breaks = _breaks.GetValueOrDefault(r.Id), faults = _faults.GetValueOrDefault(r.Id);
            if (blind >= 2)
            {
                var kind = breaks * 2 >= blind ? MateGear.DataLine : MateGear.SensorNode;
                if (Has(kind, r.Id) || Upgrades.Any(u => u.RoomId == r.Id && u.Kind == kind && w.Tick - u.Tick < SimTime.TicksPerDay * 3)) continue;
                Propose(kind, r, kind == MateGear.DataLine ? $"데이터선이 {breaks}번 끊겼다" : $"감지기가 {blind - breaks}번 틀어졌다", $"{Ko.EulReul(r.Name)} {blind}번 못 봤다 (정찰 {Scouts.Count(s => s.RoomId == r.Id)}번)");
                return;
            }
            if (faults >= 3 && !Has(MateGear.Mount, r.Id) && !Upgrades.Any(u => u.RoomId == r.Id && u.Kind == MateGear.Mount && w.Tick - u.Tick < SimTime.TicksPerDay * 3))
            {
                bool shaky = r.Noise > 0.3f || w.Ship.Machines.Any(m => m.Body.Room == r && (m.Omen?.Kind == OmenKind.Vibration || TrendWord(m) == "진동"));
                Propose(shaky ? MateGear.Mount : MateGear.SensorNode, r, shaky ? "옆 설비 떨림이 받침을 타고 번진다" : "원인을 보려면 눈이 더 필요하다", $"{r.Name} 설비 고장 {faults}번");
                return;
            }
        }
        if (_overloads >= 2 && A.ComputerBody?.Room is Room cr && CoreRacks < 2 && !Upgrades.Any(u => u.Kind == MateGear.CoreRack && w.Tick - u.Tick < SimTime.TicksPerDay * 3))
            Propose(MateGear.CoreRack, cr, "연산이 몰려 덜 급한 경보를 놓쳤다", $"과부하 {_overloads}번 · 놓친 경보 {MissedAlarms.Count}건");
    }

    /// <summary>개조안 (시험도 부른다): 회의에 올라간다.</summary>
    public GearUpgrade Propose(MateGear kind, Room r, string cause, string evidence)
    {
        var w = _w;
        var u = new GearUpgrade
        {
            Id = _gearId++, Kind = kind, RoomId = r.Id, Room = r.Name, Cause = cause, Evidence = evidence, Tick = w.Tick, State = GearState.Proposed,
            Need = kind switch { MateGear.CoreRack => 4f, MateGear.DataLine => 3f, MateGear.Mount => 2.5f, _ => 2f },
        };
        u.Spot = GearSpot(kind, r) ?? r.Cells.FirstOrDefault(w.Ship.IsWalkable);
        Upgrades.Add(u);
        if (Upgrades.Count > 30) Upgrades.RemoveAt(0);
        Say($"까닭을 따져 봤다 — {r.Name}: {cause} ({evidence}). 다음 회의에 {Ko.EulReul(u.Name)} 올린다");
        return u;
    }

    private Cell? GearSpot(MateGear kind, Room r)
    {
        var w = _w;
        Vector2 at = kind == MateGear.CoreRack && A.ComputerBody is Furniture cb ? cb.Center : kind == MateGear.Mount && w.Ship.Machines.FirstOrDefault(m => m.Body.Room == r) is Machine mm ? mm.Body.Center : r.Center;
        Cell? best = null; float bd = float.MaxValue;
        foreach (var c in r.Cells) { if (!w.Ship.IsOpenFloor(c)) continue; float d = (c.Center - at).LengthSquared(); if (d < bd) { bd = d; best = c; } }
        return best;
    }

    private static (ItemKind k, int n)[] Cost(MateGear k) => k switch
    {
        MateGear.CoreRack => new[] { (ItemKind.Electronics, 2), (ItemKind.Plate, 1), (ItemKind.Cable, 1) },
        MateGear.SensorNode => new[] { (ItemKind.Sensor, 1), (ItemKind.Cable, 1) },
        MateGear.DataLine => new[] { (ItemKind.Cable, 2), (ItemKind.Electronics, 1) },
        _ => new[] { (ItemKind.Plate, 1) },
    };

    internal void UpgradeAgenda(MeetingRecord rec, List<CrewMember> voters, CrewMember chair)
    {
        var w = _w;
        var a = A;
        var u = Upgrades.FirstOrDefault(x => x.State == GearState.Proposed);
        if (u == null) return;
        var cost = Cost(u.Kind);
        bool scarce = cost.Any(c => w.Ship.CountStored(c.k) < c.n + 1);
        var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == u.RoomId);
        bool quarters = room?.Type is RoomType.Quarters or RoomType.QuietQuarters;
        var item = new AgendaItem
        {
            Title = $"주 컴퓨터 개조안: {u.Room} {u.Name}", Topic = "computer:gear:" + u.Kind, Evidence = $"{u.Cause} — {u.Evidence}",
            Computer = $"주 컴퓨터: {u.Cause}. {u.Name}이면 {GearEffect(u.Kind)} (자재 {string.Join(" · ", cost.Select(c => $"{ItemKinds.Name(c.k)} {c.n}"))})", ComputerSign = 1,
        };
        var (yes, no) = w.Meetings.Debate(voters, c =>
        {
            var terms = new List<(float v, string why)>
            {
                ((a.Trusts.Of(c) - 0.5f) * 0.6f, a.Trusts.Of(c) >= 0.5f ? "컴퓨터 말이 그럴듯하다" : "컴퓨터 말만 믿을 순 없다"),
                (0.3f * c.SkillLevel(Skill.Electrical), "배선을 아는 사람으로서 필요하다"),
                (0.12f, $"{u.Room} 일이 반복된다"),
            };
            if (c.Room?.Id == u.RoomId) terms.Add((0.15f, "그 방에서 겪어 봤다"));
            if (scarce) terms.Add((-0.2f, "자재가 아깝다"));
            if (quarters && u.Kind == MateGear.SensorNode && c.Value == CrewValue.Freedom) terms.Add((-0.4f, "침실에 눈을 더 달 순 없다"));
            if (KnowsSnoop.Contains(c.Id) && u.Kind == MateGear.SensorNode) terms.Add((-0.15f, "더 보게 해 주면 더 들여다본다"));
            if (u.Kind == MateGear.CoreRack && c.Value == CrewValue.Efficiency) terms.Add((0.1f, "경보를 놓치면 안 된다"));
            var top = terms.OrderByDescending(t => MathF.Abs(t.v)).First();
            return (terms.Sum(t => t.v), top.why);
        }, c => 0.3f + 0.5f * c.SkillLevel(Skill.Electrical), item, chair);
        bool pass = yes.Count > no.Count || yes.Count == no.Count && yes.Contains(chair);
        item.Passed = pass;
        item.Outcome = pass ? "받았다 — 공사한다" : "거절했다";
        rec.Items.Add(item);
        w.Meetings.Record(item.Title, item.Topic, u.RoomId, chair, yes, no, "");
        u.Yes = yes.Count; u.No = no.Count;
        u.Decided = w.Tick;
        u.State = pass ? GearState.Approved : GearState.Rejected;
        w.History.Add(w, HistoryKind.Decision, $"회의: {item.Title} ({u.Cause}) — 찬성 {yes.Count} · 반대 {no.Count} → {item.Outcome}", room, voters, log: true);
        if (!pass) { a.Authority.Learned("개조", $"{u.Room} {Ko.EunNeun(u.Name)} 회의가 거절했다 — 더 겪고 근거를 모아 다시"); if (u.Kind != MateGear.CoreRack) _blind[u.RoomId] = 0; }
    }

    public static string GearEffect(MateGear k) => k switch
    {
        MateGear.CoreRack => "연산이 넉넉해져 몰려도 덜 급한 경보까지 본다",
        MateGear.SensorNode => "문 감지기 · 공기 감지기가 둘이 되어 하나가 틀어져도 본다",
        MateGear.DataLine => "차폐한 새 선이라 끊기거나 방사선에 값이 멈추는 일이 준다",
        _ => "설비 떨림이 바닥으로 덜 번져 고장이 준다",
    };

    // ───────────── 공사 ─────────────

    /// <summary>공사 중인 개조 (설치 행동이 읽는다).</summary>
    public GearUpgrade? Building => Upgrades.FirstOrDefault(u => u.State is GearState.Approved or GearState.Working);

    private void GearWork()
    {
        var w = _w;
        var u = Building;
        if (u == null) return;
        foreach (var (id, t) in u.Workers.ToList()) if (w.Tick - t > SimTime.Hours(2) || Crew(id) is not CrewMember c || c.Dead || c.Job?.Activity is not MateActivity) u.Workers.Remove(id);
        if (!u.Paid)
        {
            var cost = Cost(u.Kind);
            if (cost.All(c => w.Ship.CountStored(c.k) >= c.n)) { foreach (var (k, n) in cost) Life.Take(w, k, n); u.Paid = true; }
        }
    }

    /// <summary>설치 행동: 이 사람이 거들 공사.</summary>
    public GearUpgrade? GearFor(CrewMember c)
    {
        var u = Building;
        if (u == null || !u.Paid || c.SkillLevel(Skill.Electrical) < 0.25f && c.SkillLevel(Skill.Mechanics) < 0.35f) return null;
        if (u.Workers.Count >= 2 && !u.Workers.ContainsKey(c.Id)) return null;
        return u;
    }

    internal void GearJoin(GearUpgrade u, CrewMember c)
    {
        u.Workers[c.Id] = _w.Tick;
        if (u.State == GearState.Approved) { u.State = GearState.Working; u.Started = _w.Tick; Say($"{u.Room} {u.Name} 공사 시작 — {c.Name}", null, -1, c.Id); }
    }

    internal void GearProgress(GearUpgrade u, CrewMember c, float hours)
    {
        var w = _w;
        u.Done += hours * (0.6f + 0.6f * MathF.Max(c.SkillLevel(Skill.Electrical), c.SkillLevel(Skill.Mechanics)));
        c.Practice(Skill.Electrical, 0.01f);
        if (u.Done < u.Need || u.State == GearState.Done) return;
        u.State = GearState.Done;
        u.Installed = w.Tick;
        UpgradesDone++;
        u.Workers.Clear();
        var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == u.RoomId);
        if (room != null && u.Kind is MateGear.SensorNode or MateGear.DataLine)
        {
            var b = A.Belief.Of(room);
            if (b.Fault != SensorFault.None) { b.Fault = SensorFault.None; b.FaultWhy = ""; }
            b.Trust = MathF.Max(b.Trust, 0.9f);
            if (u.Kind == MateGear.DataLine) room.DataLinked = true;
            _blind[room.Id] = 0; _breaks[room.Id] = 0;
        }
        if (u.Kind == MateGear.Mount && room != null) { _faults[room.Id] = 0; foreach (var m in w.Ship.Machines.Where(m => m.Body.Room == room)) m.Wear = MathF.Max(0f, m.Wear - 0.05f); }
        if (u.Kind == MateGear.CoreRack) _overloads = 0;
        Life.Diary(w, c, Persona.Say(c, $"{u.Room}에 {u.Name} — 다 달았다. 불이 들어오는 걸 봤다"));
        Say($"{u.Room} {Ko.EulReul(u.Name)} 마쳤다 — {GearEffect(u.Kind)}. {c.Name}에게 고맙다", room, 0, c.Id);
        w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터 개조: {u.Room} {u.Name} — {u.Cause} 때문에 회의가 정했고 {Ko.IGa(c.Name)} 달았다", room, new[] { c }, log: false);
    }

    internal void Overloaded() => _overloads++;
}

public sealed partial class RobotSystem
{
    /// <summary>v16.27 정찰: 그 방에 가서 직접 본다 (감지기가 안 닿는 방).</summary>
    internal bool Scout(Robot r, Room room, Action<Robot, World> seen)
    {
        var w = _world;
        var dist = w.Paths.Flood(r.Cell, Profile);
        var spot = room.Cells.Where(w.Ship.IsOpenFloor).Where(dist.Reachable).OrderBy(c => (c.Center - room.Center).LengthSquared()).ThenBy(c => c.X).ThenBy(c => c.Y).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell s) return false;
        var steps = new List<RobotStep>
        {
            new RGoto(s),
            new RWork(0.08f, null, room.Center),
            new RDo((rb, world) => { Inspect(rb, room); seen(rb, world); return true; }),
        };
        Begin(r, steps, $"정찰 — {room.Name} (감지기가 안 닿는다)");
        r.Mind.Say($"{Ko.EulReul(room.Name)} 직접 보러 간다", w.Tick);
        return true;
    }
}
