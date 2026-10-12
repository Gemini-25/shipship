using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v23 사고 지휘관이 로봇을 부리는 손잡이: 지휘 건에 묶기 · 풀기 · 그 방 불로 곧장 · 다친 사람 곁으로.
//   지휘 건에 묶인 로봇은 하던 평소 일을 바로 내려놓고, 주 컴퓨터가 잠긴 격벽을 열어 준다 (급한 일과 같은 길).

public sealed partial class Robot
{
    /// <summary>v23 묶인 지휘 건 (-1 없음).</summary>
    public int CaseId { get; internal set; } = -1;
    /// <summary>v23 지휘 건이 끝난 뒤 이때까지는 나가는 길의 잠긴 격벽도 열어 준다 (봉쇄된 방에 갇히지 않게).</summary>
    public long ExitUntil { get; internal set; } = -1;
    /// <summary>v23 거점: 평소 할 일이 없으면 정비대 대신 이 칸에서 기다린다 (불이 잘 나는 방 곁).</summary>
    public Cell? Post { get; internal set; }
    public int PostRoom { get; internal set; } = -1;
}

public sealed partial class RobotSystem
{
    /// <summary>지휘 건 로봇이 손이 비었나 (할 일 없이 · 또는 다음 지시를 기다리는 중).</summary>
    internal static bool Idle(Robot r) => (r.State == RobotState.Active || r.State == RobotState.Docked && r.Fault == null && r.Battery >= 0.5f) // v23 정비대에 붙어 쉬는 지휘 건 로봇도 다시 부른다
        && (r.Steps == null || r.Order == null && r.Steps.Count == 1 && r.Steps[0] is RWait);

    /// <summary>지휘 건에 묶여 움직이는 중 — 주 컴퓨터 · 보조 두뇌가 지휘하면 잠긴 격벽도 열어 준다 (로봇 대장은 문 제어가 없다).</summary>
    internal static bool Enlisted(World w, Robot r) => (r.CaseId >= 0 || r.ExitUntil > w.Tick) && w.Commander.OpensDoors;

    /// <summary>v23 위급하면 뛴다: 지휘 건에 묶여 가는 중이면 두 배 (전지를 더 쓰고 구동부가 조금씩 닳는다 — 절뚝이면 덜 뛴다).</summary>
    internal static float Sprint(World w, Robot r) => r.CaseId >= 0 && w.Commander.Active && r.Path != null ? (RobotBody.StageOf(r, BotPart.Drive) >= PartStage.Dented ? 1.4f : 2f) : 1f;

    /// <summary>뛰는 로봇의 구동부가 닳는다 (1분마다).</summary>
    internal void SprintWear()
    {
        var w = _world;
        foreach (var r in Robots)
            if (Sprint(w, r) > 1f && RB.Chance(0.35f)) Hurt(r, BotPart.Drive, 0.004f, "지휘 건으로 뛰었다");
    }

    /// <summary>지휘관이 부를 수 있는 로봇 (멀쩡하고 · 전지가 있고 · 폭주 · 개량 · 끌려가는 중이 아니다).</summary>
    internal static bool Callable(World w, Robot r) =>
        r.Operational && !r.Wrecked && !r.Runaway && !r.Upgrading && r.Aboard == null && r.TowedBy == null && r.Hauling == null && r.Fixing == null
        && r.Battery >= 0.3f && r.State is RobotState.Docked or RobotState.Active;

    /// <summary>지휘 건에 묶는다: 하던 일을 내려놓고 (짐은 가까운 보관함에) · 긴급 지정 · 정비대에서 일어난다.</summary>
    internal void Enlist(Robot r, CommandCase k, DraftKind draft, Room? room, string why)
    {
        var w = _world;
        DropTask(r);
        r.Homing = false;
        r.CaseId = k.Id;
        r.Draft = draft;
        r.DraftRoom = room?.Id ?? -1;
        r.DraftUntil = w.Tick + SimTime.Minutes(60);
        if (draft == DraftKind.Fire) r.Foam = MathF.Max(r.Foam, 0.6f); // 정비대 곁 소화기를 집어 든다
        if (r.State == RobotState.Docked) SetState(r, RobotState.Active);
        if (!k.Served.Contains(r.Id)) { k.Served.Add(r.Id); k.ScaresAt0[r.Id] = r.Scares; }
        r.Mind.Say(RobotBrain.Terse(r, $"#{k.Id} {room?.Name ?? "?"}", why), w.Tick); // v23 위급하면 짧게
        MarkLog.Add(r.Marks, w.Tick, $"지휘 건 #{k.Id}: {why}");
        w.Fleet.Line(CmdTarget.Robot, r.Id, room, $"{r.Name}: 지휘 건 #{k.Id}", why, 1f, 30f);
    }

    /// <summary>지휘 건에서 푼다 (긴급 지정도 함께) — 다음 판단에서 제 일로 돌아간다.</summary>
    internal void Release(Robot r, string why)
    {
        var w = _world;
        if (r.CaseId < 0) return;
        r.CaseId = -1;
        r.ExitUntil = w.Tick + SimTime.Minutes(20);
        r.Draft = DraftKind.None;
        r.DraftRoom = -1;
        r.FightingFire = false;
        if (r.Operational) r.Mind.Say(RobotBrain.Calm(r, why, w.Tick), w.Tick); // v23 평소 말투로 돌아온다
        r.NextDecide = w.Tick;
        if (r.State == RobotState.Active && r.Order == null && r.Steps != null && !r.Homing) { r.Steps = null; r.Path = null; }
    }

    /// <summary>그 방의 불로 곧장 (잠긴 격벽은 주 컴퓨터가 연다) — 갈 칸이 없으면 false.</summary>
    internal bool FightIn(Robot r, Room room, string? label = null)
    {
        var w = _world;
        var dist = w.Paths.Flood(r.Cell, w.Commander.OpensDoors ? UrgentProfile : Profile);
        Cell? best = null;
        int bestCost = int.MaxValue;
        foreach (var (cell, _) in w.Fire.Fires)
        {
            if (w.Ship.RoomAt(cell) != room) continue;
            foreach (var d in Cell.Dirs8)
            {
                var s = cell + d;
                if (!w.Ship.IsWalkable(s) || w.Fire.At(s) > 0f) continue;
                int dd = dist.Get(s);
                if (dd < 0 || dd >= bestCost) continue;
                bestCost = dd; best = s;
            }
        }
        if (best is not Cell at) return false;
        FiresFought++;
        r.FightingFire = true;
        r.Mind.Say(RobotBrain.Terse(r, $"{room.Name} 불 {w.Fire.CountIn(room)}칸", "진입"), w.Tick);
        Begin(r, new List<RobotStep>
        {
            new RGoto(at),
            new RSpray(),
            new RDo((rb, world) => { Done(rb, $"{room.Name} 불에 거품을 뿌렸다 (거품 {rb.Foam * 100:0}%)"); return true; }),
        }, label ?? $"{room.Name} 불 — 지휘 건 #{r.CaseId}");
        return true;
    }

    /// <summary>v23 새는 방 문 앞(새지 않는 쪽)에서 기압이 떨어지길 1분 기다린다 — 갈 곳이 없으면 false.</summary>
    internal bool WaitAtDoor(Robot r, Room room)
    {
        var w = _world;
        var field = w.Paths.Flood(r.Cell, Profile);
        Cell? best = null; int bd = int.MaxValue;
        foreach (var d in room.Doors)
        {
            if (d.Removed || d.IsExternal) continue;
            var other = d.RoomA == room ? d.RoomB : d.RoomA;
            if (other == null || other.Leaking || other.Detached) continue;
            foreach (var dir in Cell.Dirs4)
            {
                var c = d.Cell + dir;
                if (w.Ship.RoomAt(c) != other || !w.Ship.IsWalkable(c)) continue;
                int dd = field.Get(c);
                if (dd >= 0 && dd < bd) { bd = dd; best = c; }
            }
        }
        if (best is not Cell at) return false;
        Begin(r, new List<RobotStep> { new RGoto(at), new RWait(SimTime.Minutes(1)) }, $"{room.Name} 문 앞 — 기압이 떨어지길 기다린다 (지휘 건 #{r.CaseId})");
        return true;
    }

    /// <summary>하던 일을 내려놓는다 (끊긴 구역의 로봇이 눈앞의 일을 스스로 맡을 때).</summary>
    internal void DropTaskFor(Robot r) => DropTask(r);

    /// <summary>
    /// 다친 사람 곁으로: 피가 나면 곧장 상처를 누른다 (주 컴퓨터의 지시 — 어느 로봇이든 손으로 누를 수는 있다) ·
    /// 의료 로봇은 제 판단표(구급 키트 · 피 나르기 · 들것)로 · 아니면 곁에 서서 지킨다.
    /// </summary>
    internal bool GoTo(Robot r, CrewMember pt, string what)
    {
        var w = _world;
        if (w.Casualty.Of(pt) is Trauma t && t.Kind == TraumaKind.Bleed && !t.Closed && !RobotBody.Gripless(r) && w.Robots.Robots.All(x => x == r || !(x.Steps != null && x.StepIndex < x.Steps.Count && x.Steps[x.StepIndex] is RPress rp && rp.Patient == pt)))
        {
            Begin(r, new List<RobotStep>
            {
                new RGoto(rb => Cell.Dirs8.Select(d => pt.Cell + d).Where(c => world0().Ship.IsWalkable(c)).OrderBy(c => (c.Center - rb.Position).LengthSquared()).ThenBy(c => c.X).ThenBy(c => c.Y).Cast<Cell?>().FirstOrDefault()),
                new RPress(pt, w.MedBots),
                new RDo((rb, world) => { Done(rb, $"{Ko.EulReul(pt.Name)} 상처를 눌렀다"); return true; }),
            }, $"{pt.Name} 상처를 누르러 — 지휘 건 #{r.CaseId}");
            return true;
        }
        if (w.MedBots.Wants(r))
        {
            var dist = w.Paths.Flood(r.Cell, w.Commander.OpensDoors ? UrgentProfile : Profile);
            if (w.MedBots.Plan(r, dist)) return true;
        }
        Begin(r, new List<RobotStep>
        {
            new RGoto(rb => Cell.Dirs8.Select(d => pt.Cell + d).Where(c => world0().Ship.IsWalkable(c)).OrderBy(c => (c.Center - rb.Position).LengthSquared()).ThenBy(c => c.X).ThenBy(c => c.Y).Cast<Cell?>().FirstOrDefault()),
            new RDo((rb, world) => { rb.Facing = pt.Position - rb.Position == Vector2.Zero ? rb.Facing : Vector2.Normalize(pt.Position - rb.Position); Done(rb, $"{Ko.EulReul(pt.Name)} 곁에 닿았다 — 지킨다"); return true; }),
        }, what);
        return true;
        World world0() => _world;
    }

    /// <summary>
    /// 거점에서 기다리기 (평소 할 일이 없을 때 정비대 대신): 원자로가 전지를 채우니 정비대에 앉지 않아도 된다.
    /// 전지가 모자라거나 고장 · 상처가 깊으면 정비대로. 3분마다 다시 일을 살핀다.
    /// </summary>
    private bool GoPost(Robot r)
    {
        var w = _world;
        if (r.CaseId >= 0 || r.Post is not Cell post || !RobotsV21.Reactor || r.Battery < 0.5f || r.Fault != null || RobotBody.Worst(r) < 0.5f || !w.Ship.IsWalkable(post)) return false;
        var room = r.PostRoom >= 0 && r.PostRoom < w.Ship.Rooms.Count ? w.Ship.Rooms[r.PostRoom] : null;
        if (room == null || room.Detached || room.Abandoned) { r.Post = null; r.PostRoom = -1; return false; }
        if (r.Cell == post)
        {
            r.Steps = new List<RobotStep> { new RWait(SimTime.Minutes(3)) };
            r.StepIndex = 0;
            r.Doing = $"거점 대기 — {room.Name} 곁";
            r.Steps[0].Begin(r, w);
            return true;
        }
        r.Steps = new List<RobotStep> { new RGoto(post), new RWait(SimTime.Minutes(3)) };
        r.StepIndex = 0;
        r.Homing = false;
        r.Doing = $"거점으로 — {room.Name} 곁";
        r.Steps[0].Begin(r, w);
        return true;
    }

    /// <summary>지휘관이 거점을 준다 · 거둔다.</summary>
    internal void SetPost(Robot r, Room? room, Cell? at)
    {
        r.Post = at;
        r.PostRoom = room?.Id ?? -1;
        if (room == null) return;
        r.Mind.Say($"거점 — {room.Name} 곁에서 기다린다 (불이 나면 바로)", _world.Tick);
        if (r.State == RobotState.Docked && r.Operational) { SetState(r, RobotState.Active); if (!GoPost(r)) GoHome(r, "거점으로 갈 길이 없다"); } // 정비대에서 일어나 거점으로
        else if (r.State == RobotState.Active && r.Steps == null) GoPost(r);
    }

    /// <summary>
    /// 지휘 건 로봇이 그 일에 직접 붙는다 — 사람 몫으로 묶인 일이어도 진척을 함께 채운다 (봉합 · 수리).
    /// 재료는 가까운 보관함에서 · 잠긴 격벽은 연다. 갈 칸이 없거나 재료가 없으면 false.
    /// </summary>
    internal bool WorkOn(Robot r, WorkOrder o, string? label = null)
    {
        var w = _world;
        if (o.Closed || o.Robot != null && o.Robot != r) return false;
        var field = w.Paths.Flood(r.Cell, w.Commander.OpensDoors ? UrgentProfile : Profile);
        if (Spot(r, o, field) is not Cell spot) return false;
        var steps = Plan(r, o, spot, field, out string? blocked);
        if (steps == null) { if (blocked != null) r.Mind.Say($"{o.Title} — {blocked}", w.Tick); return false; }
        o.Robot = r;
        r.Order = o;
        Begin(r, steps, label ?? $"{o.Title} — 지휘 건 #{r.CaseId}");
        return true;
    }

    /// <summary>파공 · 침수: 긴급 지정을 걸고 다음 판단에서 그 방 일을 고르게 한다 (봉합 일은 급한 길로).</summary>
    internal void Steer(Robot r)
    {
        r.NextDecide = _world.Tick;
        Decide(r);
    }
}

/// <summary>v23 그 자리에서 잠깐 기다린다 (거점).</summary>
internal sealed class RWait : RobotStep
{
    private readonly int _ticks;
    private long _until;
    public RWait(int ticks) => _ticks = ticks;
    public override void Begin(Robot r, World w) => _until = w.Tick + _ticks;
    public override ToilStatus Tick(Robot r, World w) => w.Tick >= _until ? ToilStatus.Succeeded : ToilStatus.Running;
}
