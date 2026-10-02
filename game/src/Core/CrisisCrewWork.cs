using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.21 한 작업에 여러 명 · 비상 배치 자리로 · 사고 뒤 훈련 — CrisisCrew.cs 의 행동 쪽.

public sealed partial class WorkBoard
{
    /// <summary>
    /// v16.21 이 일에 함께 붙을 수 있는 사람 수 (이끄는 사람 포함): 일감의 크기 · 사고 규모(계통 +1 · 배 전체 +1) · 일터 둘레 공간. 1이면 혼자 하는 일.
    /// </summary>
    public int MaxHands(WorkOrder o)
    {
        if (CrisisCrewSystem.Off) return 1;
        var w = _world;
        int b = o.Kind switch
        {
            WorkKind.ClearRubble or WorkKind.RebuildFrame => 4,
            WorkKind.Extinguish or WorkKind.SealBreach or WorkKind.RepairHull or WorkKind.ReplacePipe or WorkKind.PumpOut or WorkKind.BuildOxygen
                or WorkKind.InstallTruss or WorkKind.ReplacePanel => 3,
            WorkKind.WeldBulkhead or WorkKind.SealOffRoom or WorkKind.PatchPipe or WorkKind.RepairNet or WorkKind.CoolDown or WorkKind.Rewire
                or WorkKind.Reline or WorkKind.RepairJoint or WorkKind.CrankDoor => 2,
            WorkKind.Repair => o.Target.Furniture is Furniture f ? (f.Width * f.Height >= 4 || f.Machine?.Spec.Critical == true ? 3 : 2) : 1,
            _ => 1,
        };
        if (b <= 1 || o.External) return 1;
        if (o.Target.CurrentRoom is Room r && w.Scale.RoomScale(r) is IncidentScale s)
        {
            if (s >= IncidentScale.System) b++;
            if (s >= IncidentScale.Ship) b++;
        }
        if (BigJob(o)) b++; // 오래 걸리는 수리 (몇 시간짜리)
        b = Math.Min(4, b);
        // 공간: 설비면 몸체를 둘러싼 칸 · 아니면 일터 둘레(5×5)의 걸을 수 있는 칸 — 둘이 한 칸을 나눠 쓸 수는 없다
        int room = 0;
        if (o.Target.Furniture is Furniture fu)
        {
            _ring.Clear();
            foreach (var fc in fu.Cells)
                foreach (var d in Cell.Dirs8)
                {
                    var x = fc + d;
                    if (!fu.Cells.Contains(x) && w.Ship.IsWalkable(x)) _ring.Add(x);
                }
            room = _ring.Count;
        }
        else
        {
            var at = Cell.FromPosition(o.Target.Center);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    if (w.Ship.IsWalkable(new Cell(at.X + dx, at.Y + dy))) room++;
            room /= 2;
        }
        return Math.Max(1, Math.Min(b, room));
    }

    private readonly HashSet<Cell> _ring = new();

    /// <summary>v16.21 몇 시간짜리 큰 일 (여럿이 붙을 만하다): 고장 수리 2시간 넘게 · 잔해 · 골조 · 선체 · 외판 · 관 교체 · 산소 설비.</summary>
    public static bool BigJob(WorkOrder o) => o.Kind switch
    {
        WorkKind.ClearRubble or WorkKind.RebuildFrame or WorkKind.RepairHull or WorkKind.ReplacePanel or WorkKind.ReplacePipe or WorkKind.BuildOxygen or WorkKind.InstallTruss => true,
        WorkKind.Repair => o.Target.Furniture?.Machine is Machine m && m.Faults.Any(f => Faults.Spec(f.Kind).RepairHours >= 2f),
        _ => false,
    };

    /// <summary>v16.21 사고 뒤 훈련: 공황을 겪은 사람은 평온해지면 곧 다시 훈련한다.</summary>
    private void ScanCrisisDrills(Poster post)
    {
        var w = _world;
        if (CrisisCrewSystem.Off || Crisis.Acting(w)) return;
        var airlock = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault(r => !r.Abandoned && !r.Leaking && !r.OffLimits);
        if (airlock == null) return;
        foreach (int id in w.CrisisCrew.DrillDueIds)
        {
            var c = w.Brain2.Beliefs.CrewById(id);
            if (c == null || !c.CanAct) continue;
            post(WorkKind.Drill, WorkTarget.OfRoom(airlock), 0.32f, Skill.Mechanics, $"{c.Name} · 사고 뒤 훈련 (우주복 · 격벽 · 소화 · 마스크)", circuit: c.Id);
        }
    }
}

public sealed partial class CrisisCrewSystem
{
    // ───────────────────────────── 한 작업에 여러 명 ─────────────────────────────

    private readonly Dictionary<int, List<CrewMember>> _hands = new();
    private readonly Dictionary<int, int> _helping = new();
    private static readonly float[] Gain = { 0.7f, 0.45f, 0.25f };

    /// <summary>이 사람이 거드는 일 번호 (없으면 -1).</summary>
    public int HelpingOrder(CrewMember c) => _helping.TryGetValue(c.Id, out var o) ? o : -1;
    public IReadOnlyList<CrewMember> HelpersOf(WorkOrder o) => _hands.TryGetValue(o.Id, out var l) ? l : Array.Empty<CrewMember>();

    internal void Join(WorkOrder o, CrewMember c)
    {
        if (_helping.ContainsKey(c.Id)) Leave(c);
        if (!_hands.TryGetValue(o.Id, out var l)) _hands[o.Id] = l = new List<CrewMember>();
        l.Add(c);
        _helping[c.Id] = o.Id;
        _candTick = -1;
        Joins++;
    }

    internal void Leave(CrewMember c)
    {
        if (!_helping.Remove(c.Id, out var id)) return;
        if (_hands.TryGetValue(id, out var l)) { l.Remove(c); if (l.Count == 0) _hands.Remove(id); }
    }

    /// <summary>
    /// 이끄는 사람의 손 빠르기 배율: 거드는 사람마다 덜 붙는다(0.7 · 0.45 · 0.25 × 솜씨 · 공구) · 자리보다 많으면 서로 방해 ·
    /// 거드는 사람 중 더 솜씨 좋은 사람이 있으면 그 사람이 이끈다(손을 잡고 짚어 준다).
    /// </summary>
    public float HandsMul(CrewMember lead)
    {
        if (_helping.Count == 0 || lead.Job?.Order is not WorkOrder o || !_hands.TryGetValue(o.Id, out var list) || list.Count == 0) return 1f;
        float own = lead.SkillLevel(o.Skill), best = own, mul = 1f;
        int k = 0;
        foreach (var h in list)
        {
            if (h.Dead || h.Down || (h.Position - lead.Position).LengthSquared() > 9f) continue;
            float sk = h.SkillLevel(o.Skill);
            mul += Gain[Math.Min(k, Gain.Length - 1)] * (0.6f + 0.4f * sk) * MathF.Min(1.1f, h.ToolFactor);
            best = MathF.Max(best, sk);
            k++;
        }
        if (k == 0) return 1f;
        if (k + 1 > _w.Board.MaxHands(o)) mul *= 0.85f; // 비좁다 — 서로 걸린다
        if (best > own + 0.1f) mul *= (1.6f - 0.8f * own) / (1.6f - 0.8f * best);
        return mul;
    }

    private readonly List<(WorkOrder o, int cap, int have, bool calm, int pair)> _cands = new();
    private long _candTick = -1;

    /// <summary>거들 만한 일 (틱마다 한 번): 누가 붙어 하고 있는 큰 일 · 급한 일 · 인원 상한이 둘 넘는다.</summary>
    private List<(WorkOrder o, int cap, int have, bool calm, int pair)> Cands()
    {
        var w = _w;
        if (_candTick == w.Tick) return _cands;
        _candTick = w.Tick;
        _cands.Clear();
        bool acting = Crisis.Acting(w);
        foreach (var o in w.Board.All)
        {
            if (o.Closed || o.Assignee is not CrewMember lead || lead.Job?.Order != o) continue;
            if (lead.Pose != Pose.Working && o.Urgency < 0.9f) continue; // 급한 일은 이끄는 사람이 가는 중에도 따라붙는다
            bool calm = o.Urgency < 0.85f && !(acting && o.Urgency >= 0.6f);
            if (calm && !(o.Urgency >= 0.5f && WorkBoard.BigJob(o))) continue; // 급하지 않아도 몇 시간짜리 큰 일은 손 빈 사람이 거든다
            int cap = w.Board.MaxHands(o);
            if (cap <= 1) continue;
            int have = 1 + (_hands.TryGetValue(o.Id, out var l) ? l.Count : 0), pair = -1;
            // 이미 "누가 좀 잡아 줘"로 불려 온 짝 (Cooperation) 도 한 사람 — 겹쳐 부르지 않는다
            foreach (var call in w.Coop.Calls)
                if (call.OrderId == o.Id && !call.Done && call.Helper >= 0) { have++; pair = call.Helper; }
            _cands.Add((o, cap, have, calm, pair));
        }
        return _cands;
    }

    /// <summary>그 일을 거드는 사람 중 그 자리 몫이 아닌 사람 (제 자리 사람이 오면 넘기고 물러난다).</summary>
    internal CrewMember? Relievable(WorkOrder o, StationRole role)
    {
        if (!_hands.TryGetValue(o.Id, out var l)) return null;
        foreach (var h in l)
            if (!_active.TryGetValue(h.Id, out var hr) || hr != role) return h;
        return null;
    }

    /// <summary>거들 일 고르기: 누가 이미 하고 있는 큰 일 · 자리가 남았다 · 아는 사고 · 갈 수 있다.</summary>
    internal (WorkOrder? o, float score, string why) HelpPick(CrewMember c, DistanceField dist)
    {
        var w = _w;
        if (Off || c.IsChild || !c.CanAct || c.Outside || c.Mind.Panicking(w.Tick)) return (null, 0f, "");
        var cands = Cands();
        if (cands.Count == 0) return (null, 0f, "");
        WorkOrder? best = null;
        float bs = 0f;
        string bw = "";
        DistanceField? field = null;
        int mine = _helping.TryGetValue(c.Id, out var m0) ? m0 : -1;
        foreach (var (o, cap, have0, calm, pair) in cands)
        {
            var lead = o.Assignee!;
            if (lead == c || pair == c.Id) continue;
            int have = mine == o.Id ? have0 - 1 : have0;
            var role = RoleFor(o);
            bool mineRole = role != StationRole.None && _active.TryGetValue(c.Id, out var myR) && myR == role;
            if (have >= cap && !(mineRole && mine != o.Id && Relievable(o, role) != null)) continue; // 꽉 찼어도 제 자리 사람은 대신 서 있던 사람과 바꾼다
            if (o.MinSkill > 0f && c.SkillLevel(o.Skill) < o.MinSkill || !w.Minds.Aware(c, o)) continue;
            field ??= o.Urgency >= 0.9f ? w.Paths.Flood(c.Cell, new PathProfile(c.PathProfile.HazardScale * 0.8f, true, true)) : dist;
            float a = ChoresActivity.Appeal(c, w, o, field, out int d);
            if (a <= 0f || d < 0) continue;
            // 지휘: 다른 조를 맡았으면 거들러 가지 않는다 · 대기조와 같은 조는 거든다
            a -= w.Command.Bias(c, o);
            if (w.Command.Active && w.Command.TeamOf(c) is Team t)
                a += t.Kind == TeamKind.Reserve || CommandSystem.Group(o.Kind) == t.Kind ? 0.08f : t.Watcher == c.Id ? -1f : -0.4f;
            float s = 0.82f * a - (calm ? 0.12f : 0.04f) - 0.05f * (have - 1) + 0.12f * MathF.Max(0f, c.AffinityTo(lead)) + (Memory.AreComrades(c, lead) ? 0.05f : 0f);
            // 제 비상 자리의 일이면 자리에 서 있기보다 붙는다
            if (mineRole) s += 0.1f;
            if (s <= bs) continue;
            bs = s;
            best = o;
            bw = $"{lead.Name}의 {o.Title} 거들기 ({have}/{cap}명)";
        }
        return (best, bs, bw);
    }

    // ───────────────────────────── 비상 배치 자리 ─────────────────────────────

    /// <summary>제 비상 자리로 갈 칸 (없으면 null): 전력 = 보조 발전기 곁 · 소화 = 불난 방 문 앞 · 격벽 = 가까운 격벽 문 · 의료 = 의무실 · 선외 = 에어락 · 대피 유도 = 공황에 빠진 사람 곁.</summary>
    internal (Cell? spot, string where) StationSpot(CrewMember c, StationRole r, DistanceField dist)
    {
        var w = _w;
        Cell? Near(Cell at, int radius = 1)
        {
            Cell? best = null;
            int bc = int.MaxValue;
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    var x = new Cell(at.X + dx, at.Y + dy);
                    int d = dist.Get(x);
                    if (d < 0 || d >= bc || !w.Ship.IsWalkable(x) || w.IsSpotTaken(x, c) || w.Fire.AnyWithin(x, 1.5f)) continue;
                    best = x; bc = d;
                }
            return best;
        }
        Cell? InRoom(Room room)
        {
            Cell? best = null;
            int bc = int.MaxValue;
            foreach (var x in room.Cells)
            {
                int d = dist.Get(x);
                if (d < 0 || d >= bc || !w.Ship.IsOpenFloor(x) || w.IsSpotTaken(x, c)) continue;
                best = x; bc = d;
            }
            return best;
        }
        switch (r)
        {
            case StationRole.Power:
            {
                var aux = w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault();
                if (aux != null && !w.Power.AuxRunning && aux.UseSpots.Where(s => dist.Get(s) >= 0).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault() is Cell s0) return (s0, "보조 발전기 곁");
                var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault();
                if (panel != null && panel.UseSpots.Where(s => dist.Get(s) >= 0).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault() is Cell s1) return (s1, "배전반 앞");
                return (null, "");
            }
            case StationRole.Fire:
            {
                Room? fire = null;
                foreach (var (room, _, _) in w.Fire.KnownFires())
                    if (c.Mind.Knows.ContainsKey($"fire:{room.Id}")) { fire = room; break; }
                if (fire == null) return (null, "");
                foreach (var d in fire.Doors.OrderBy(d => dist.Get(d.Cell) < 0 ? int.MaxValue : dist.Get(d.Cell)))
                    if (Near(d.Cell) is Cell s && w.Ship.RoomAt(s) != fire) return (s, $"{fire.Name} 문 앞");
                return (null, "");
            }
            case StationRole.Bulkhead:
            {
                foreach (var d in w.Ship.Doors.Where(d => d.Bulkhead && !d.Removed && dist.Get(d.Cell) >= 0).OrderBy(d => dist.Get(d.Cell)).Take(3))
                    if (Near(d.Cell) is Cell s) return (s, "격벽 문 곁");
                return (null, "");
            }
            case StationRole.Medical:
            {
                var med = w.Ship.RoomsOf(RoomType.Medbay).FirstOrDefault(m => !m.Abandoned && !m.Leaking);
                return med != null && InRoom(med) is Cell s ? (s, med.Name) : (null, "");
            }
            case StationRole.Eva:
            {
                var al = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault(m => !m.Abandoned && !m.Leaking);
                return al != null && InRoom(al) is Cell s ? (s, al.Name) : (null, "");
            }
            case StationRole.Guide:
            {
                CrewMember? p = null;
                int pd = int.MaxValue;
                foreach (var x in w.Crew)
                {
                    if (x == c || !x.Mind.Panicking(w.Tick) || x.Down || x.Dead) continue;
                    int d = dist.Get(x.Cell);
                    if (d >= 0 && d < pd) { pd = d; p = x; }
                }
                if (p != null && Near(p.Cell) is Cell s) return (s, $"{p.Name} 곁 (진정시키러)");
                return (null, "");
            }
        }
        return (null, "");
    }

    /// <summary>제 자리의 일이 열려 있다 (서 있던 사람이 바로 일을 잡게).</summary>
    internal bool RoleWorkOpen(CrewMember c, StationRole r)
    {
        foreach (var o in _w.Board.All)
            if (!o.Closed && o.Assignee == null && o.BlockedUntil <= _w.Tick && RoleFor(o) == r && o.Urgency >= 0.8f && _w.Minds.Aware(c, o)) return true;
        // 누가 붙어 하는 제 자리의 일에 손이 모자라면 거들러 간다
        foreach (var (o, cap, have, _, _) in Cands())
            if (have < cap && RoleFor(o) == r && o.Assignee != c && _w.Minds.Aware(c, o)) return true;
        return false;
    }

    internal void Mustered() => Musters++;
}

/// <summary>v16.21 거들기: 누가 이끄는 큰 일 곁에서 잡아 주고 · 건네고 · 받친다 (이끄는 사람의 손이 빨라진다).</summary>
public sealed class HelpActivity : Activity
{
    public override string Id => "help";
    public override string Label => "거들기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (CrisisCrewSystem.Off) return (0f, "—");
        if (c.Job?.Activity is HelpActivity && w.CrisisCrew.HelpingOrder(c) >= 0) return (0.95f, "거드는 중");
        var (o, s, why) = w.CrisisCrew.HelpPick(c, dist);
        return o == null ? (0f, "거들 일 없음") : (s, why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (o, _, _) = w.CrisisCrew.HelpPick(c, dist);
        if (o?.Assignee is not CrewMember lead) return null;
        var field = o.Urgency >= 0.9f ? w.Paths.Flood(c.Cell, new PathProfile(c.PathProfile.HazardScale * 0.8f, true, true)) : dist;
        Cell? spot = null;
        int bc = int.MaxValue;
        foreach (var s in o.Target.Spots(w.Ship).Concat(Cell.Dirs8.Select(d => lead.Cell + d)))
        {
            int d = field.Get(s);
            if (d < 0 || d >= bc || !w.Ship.IsWalkable(s) || w.IsSpotTaken(s, c) || s == lead.Cell
                || (s.Center - lead.Position).LengthSquared() > 6.5f && (s.Center - o.Target.Center).LengthSquared() > 9f) continue;
            spot = s; bc = d;
        }
        if (spot is not Cell at) return null;
        var toils = Plans.DropOff(c, w, field);
        toils.Add(new GotoToil(at));
        toils.Add(new AssistToil(o, lead));
        w.CrisisCrew.Join(o, c); // 가는 동안에도 자리를 잡아 둔다 (여럿이 한꺼번에 몰리지 않게)
        return new Job(this, "거들기", toils)
        {
            LogText = $"{Ko.IGa(lead.Name)} 하는 {Ko.EulReul(o.Title)} 거든다",
            LogKind = LogKind.Work,
            AlwaysLog = true,
            Urgent = o.Urgency >= 0.9f,
            TargetRoom = o.Target.CurrentRoom,
            InterruptMargin = 0.3f,
            OnFinished = (cm, world, _) => world.CrisisCrew.Leave(cm),
        };
    }
}

/// <summary>곁에서 거든다: 이끄는 사람이 그 일을 놓거나 일이 끝나면 함께 손을 뗀다.</summary>
public sealed class AssistToil : Toil
{
    private readonly WorkOrder _o;
    private readonly CrewMember _lead;
    private int _elapsed;
    private bool _crowded;
    public AssistToil(WorkOrder o, CrewMember lead) { _o = o; _lead = lead; }

    public override void Begin(CrewMember c, World w)
    {
        c.Pose = Pose.Working;
        Locomotion.Face(c, _o.Target.Center);
        if (w.CrisisCrew.HelpingOrder(c) != _o.Id) w.CrisisCrew.Join(_o, c);
        // 제 자리 사람이 왔는데 자리가 모자라면 대신 붙어 있던 사람이 넘기고 물러난다
        var role = CrisisCrewSystem.RoleFor(_o);
        if (role != StationRole.None && w.CrisisCrew.Active(c) == role && 1 + w.CrisisCrew.HelpersOf(_o).Count > w.Board.MaxHands(_o)
            && w.CrisisCrew.Relievable(_o, role) is CrewMember off && off != c)
        {
            off.Say(w, Persona.Say(off, $"{c.Name}, 여기 — 넘길게"));
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.WaGwa(c.Name)} 자리를 바꿨다 ({CrisisCrewSystem.RoleName(role)} 담당이 왔다)", off.Id);
            w.CrisisCrew.Leave(off);
            off.EndJob(w, ToilStatus.Succeeded);
        }
        // 그래도 비좁으면 늦게 온 사람이 물러난다 (제 자리로 돌아간다)
        _crowded = 1 + w.CrisisCrew.HelpersOf(_o).Count > w.Board.MaxHands(_o) + 1;
        // 더 솜씨 좋은 사람이 오면 그 사람이 짚어 가며 이끈다
        if (c.SkillLevel(_o.Skill) > _lead.SkillLevel(_o.Skill) + 0.1f)
        {
            w.CrisisCrew.Leads++;
            c.Say(w, Persona.Say(c, "여기 먼저 잡아 — 내가 짚을게"));
        }
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        _elapsed++;
        if (_crowded || _o.Closed || _lead.Dead || _lead.Down || _lead.Job?.Order != _o) return ToilStatus.Succeeded;
        if (_elapsed % 30 == 0) Locomotion.Face(c, _o.Target.Center);
        w.CrisisCrew.HelpHours += 1f / SimTime.TicksPerHour;
        // 곁에서 보며 손에 익는다 (솜씨 좋은 사람 곁이면 더)
        if (_elapsed % SimTime.Minutes(10) == 0 && _lead.SkillLevel(_o.Skill) > c.SkillLevel(_o.Skill)) c.ChangeAffinity(_lead, 0.01f);
        return _elapsed >= SimTime.Minutes(45) ? ToilStatus.Succeeded : ToilStatus.Running;
    }

    public override void End(CrewMember c, World w)
    {
        w.CrisisCrew.Leave(c);
        if (_o.Closed && !_lead.Dead)
        {
            c.ChangeAffinity(_lead, 0.03f);
            _lead.ChangeAffinity(c, 0.03f);
            if (c.Job != null) w.CrisisCrew.Did(c, _o); // 같이 한 일도 손에 붙는다
        }
    }
}

/// <summary>v16.21 비상 배치: 경보를 알면 각자 제 자리로 (전력 = 발전기 곁 · 소화 = 불난 방 문 앞 · 의료 = 의무실 · 대피 유도 = 공황에 빠진 사람 곁).</summary>
public sealed class StationActivity : Activity
{
    public override string Id => "station";
    public override string Label => "비상 배치";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (CrisisCrewSystem.Off) return (0f, "—");
        var r = w.CrisisCrew.Active(c);
        if (r == StationRole.None || c.Outside || c.Mind.Panicking(w.Tick)) return (0f, "—");
        if (w.CrisisCrew.RoleWorkOpen(c, r)) return (0f, "제 자리 일이 있다");
        if (r == StationRole.Guide && !w.CrisisCrew.AnyPanic) return (0f, "진정시킬 사람이 없다");
        float s = 0.74f + 0.12f * c.Traits.Diligence + (w.CrisisCrew.BillRole(c) == r ? 0.04f : 0f);
        if (c.Job?.Activity is StationActivity) return (s + 0.08f, $"비상 배치 {CrisisCrewSystem.RoleName(r)} — 자리를 지킨다");
        return (s, $"비상 배치 {CrisisCrewSystem.RoleName(r)} — 제 자리로");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var r = w.CrisisCrew.Active(c);
        if (r == StationRole.None) return null;
        var (spot, where) = w.CrisisCrew.StationSpot(c, r, dist);
        if (spot is not Cell at) return null;
        var rr = r;
        w.CrisisCrew.Mustered();
        return new Job(this, $"비상 배치 · {CrisisCrewSystem.RoleName(r)}", new List<Toil>
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(rr == StationRole.Guide ? 3 : 20), Pose.Standing, null, SimTime.Minutes(0.3f))
            {
                DoneWhen = (cm, world) => world.CrisisCrew.Active(cm) != rr || world.Tick % 30 == 0 && world.CrisisCrew.RoleWorkOpen(cm, rr),
            },
        })
        {
            LogText = $"비상 배치 {CrisisCrewSystem.RoleName(r)} — {where}",
            LogKind = LogKind.Warning,
            TargetRoom = w.Ship.RoomAt(at),
            Urgent = true,
            InterruptMargin = 0.25f,
        };
    }
}
