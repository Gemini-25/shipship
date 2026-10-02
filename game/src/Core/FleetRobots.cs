using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.20b 선내 로봇의 두뇌 (Fleet.cs가 매 분 부른다): 서로 돕기(멈춘 로봇 끌고 오기 · 고장 난 로봇 고치기 · 무거운 것 같이 들기) ·
//  교대(배터리가 돌아갈 만큼만 남으면 쉬는 로봇에게 넘긴다) · 자기 보존(견딜 수 없는 방에서 물러난다) · 불 속에서 부서짐 · 시험 가동.

/// <summary>고친 뒤 시험 가동: 잠깐 돌려 보고 이상이 남았는지 본다 (잘 통한 방법을 배운다).</summary>
internal sealed class RTest : RobotStep
{
    private readonly Func<Robot, World, (bool ok, string note)> _check;
    private readonly string _method;
    private long _left;
    public RTest(Machine m, string method)
    {
        _method = method;
        _check = (r, w) => m.Faults.Count == 0 && !m.Stopped ? (true, $"{m.Name} 시험 가동 — 이상 없음") : (false, $"{m.Name} 시험 가동 — 아직 이상이 남았다 (사람이 봐야 한다)");
    }
    public RTest(Robot b)
    {
        _method = "fix:robot";
        _check = (r, w) => b.Fault == null ? (true, $"{b.Name} 시험 가동 — 다시 움직인다") : (false, $"{b.Name} 시험 가동 — 아직 안 움직인다");
    }
    public override float? Progress => 1f - _left / (float)SimTime.Minutes(2);
    public override void Begin(Robot r, World w) => _left = FleetSystem.Off ? 0 : SimTime.Minutes(2);

    private static bool Called(Robot r, World w)
    {
        var ls = w.Automation.Command.Lines;
        for (int i = ls.Count - 1; i >= 0 && i >= ls.Count - 40; i--)
            if (ls[i].Open && ls[i].Target == CmdTarget.Robot && ls[i].TargetId == r.Id && ls[i].WorkOrderId >= 0 && ls[i].WorkOrderId != r.Order?.Id) return true;
        return false;
    }
    public override ToilStatus Tick(Robot r, World w)
    {
        if (_left > 0 && _left % 20 == 0 && Called(r, w)) _left = 0; // 주 컴퓨터가 다음 일을 맡겼다 — 시험은 짧게
        if (_left-- > 0) return ToilStatus.Running;
        if (FleetSystem.Off) return ToilStatus.Succeeded;
        var (ok, note) = _check(r, w);
        var f = w.Fleet;
        f.Method(_method, ok);
        if (ok) f.Tests++; else f.TestFails++;
        r.Mind.Say(note, w.Tick);
        MarkLog.Add(r.Marks, w.Tick, note);
        if (!ok) w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name}: {note}");
        return ToilStatus.Succeeded;
    }
}

/// <summary>멈춘 로봇에 견인 고리를 건다.</summary>
internal sealed class RHitch : RobotStep
{
    private readonly Robot _b;
    public RHitch(Robot b) => _b = b;
    public override ToilStatus Tick(Robot r, World w) => w.Robots.Hitch(r, _b) ? ToilStatus.Succeeded : ToilStatus.Failed;
}

/// <summary>무거운 짐을 든 로봇 곁에 붙어 같이 든다 (짐을 내려놓을 때까지).</summary>
internal sealed class RWith : RobotStep
{
    private readonly Robot _c;
    public RWith(Robot carrier) => _c = carrier;
    public override ToilStatus Tick(Robot r, World w)
    {
        if (_c.State != RobotState.Active || !FleetSystem.Heavy(_c.Cargo) || _c.Partner != r)
        {
            if (_c.Partner == r) _c.Partner = null;
            r.Partner = null;
            return ToilStatus.Succeeded;
        }
        var side = new Vector2(-_c.Facing.Y, _c.Facing.X);
        var to = _c.Position + side * 0.6f - _c.Facing * 0.2f;
        var d = to - r.Position;
        float len = d.Length(), step = RobotSystem.Speed(r.Kind) * 1.3f;
        r.Position = len <= step ? to : r.Position + d / len * step;
        r.Facing = _c.Facing;
        return ToilStatus.Running;
    }
}

public sealed partial class RobotSystem
{
    internal static float FleetReturnCost(Robot r) => ReturnCost(r);

    /// <summary>다른 로봇이 끌고 가는 중 (매 틱): 고리가 풀리면 그 자리에 선다.</summary>
    private bool TowedByBot(Robot r)
    {
        if (r.TowBot is not Robot tb) return false;
        if (tb.Hauling != r || tb.State != RobotState.Active || tb.Steps == null)
        {
            r.TowBot = null;
            if (tb.Hauling == r) tb.Hauling = null;
            SetState(r, RobotState.Stalled);
            r.Doing = "끌던 로봇이 놓았다 — 멈춰 섰다";
            return true;
        }
        r.Position = tb.Position - tb.Facing * 0.62f;
        r.Facing = tb.Facing;
        return true;
    }

    internal bool Hitch(Robot r, Robot b)
    {
        if (b.State != RobotState.Stalled || b.TowedBy != null || b.TowBot != null || r.Hauling != null) return false;
        b.TowBot = r;
        r.Hauling = b;
        SetState(b, RobotState.Towed);
        b.Doing = $"{Ko.IGa(r.Name)} 충전대로 끌고 간다";
        r.Mind.Say($"{Ko.EulReul(b.Name)} 붙잡았다 — 충전대까지 끌고 간다", _world.Tick);
        return true;
    }

    private static bool Free(Robot r) => r.Operational && !r.Wrecked && r.Hauling == null && r.Fixing == null && r.Partner == null && r.TowBot == null && r.Battery >= 0.5f
        && (r.State == RobotState.Docked || r.State == RobotState.Active && r.Order == null && r.Cargo == null && !r.FightingFire && r.Helping == null);

    /// <summary>급하지 않은 일을 하던 로봇: 동료가 멈췄으면 내려놓고 간다 (주 컴퓨터가 지휘할 때만).</summary>
    private static bool Spare(Robot r, bool cmd) => Free(r) || cmd && r.Operational && !r.Wrecked && r.State == RobotState.Active && r.Order is { Urgency: < 0.5f }
        && r.Cargo == null && !r.FightingFire && !r.Homing && r.Hauling == null && r.Fixing == null && r.Partner == null && r.TowBot == null && r.Battery >= 0.5f;

    private void Release(Robot r)
    {
        if (r.State != RobotState.Active) return;
        if (r.Order != null) r.Mind.Say($"{Ko.EunNeun(r.Order.Title)} 급하지 않다 — 내려놓고 동료부터", _world.Tick);
        DropTask(r);
    }

    private static float Far(Vector2 a, Vector2 b) => MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y);

    private WorkOrder? JobFor(WorkKind k, Robot b)
    {
        foreach (var o in _world.Board.All)
            if (o.Kind == k && !o.Closed && o.Target.Robot == b) return o;
        return null;
    }

    /// <summary>동료를 고치러 · 끌러 가는 로봇: 작업 목록에 같은 일이 올라오면 내 몫으로 걸어 둔다.</summary>
    internal void LinkJob(Robot r)
    {
        var b = r.Fixing ?? r.Hauling;
        if (b == null || r.State != RobotState.Active) return;
        var job = JobFor(r.Fixing != null ? WorkKind.RepairRobot : WorkKind.FetchRobot, b);
        if (job == null || job.Robot != null) return;
        // 막 나서려던 사람(아직 손대지 않음)은 로봇에게 넘기고 하던 일로 — 이미 손댄 사람은 그대로 둔다
        if (job.Assignee is CrewMember c && job.Progress <= 0f && c.Job?.Order == job && (c.Position - b.Position).LengthSquared() > 4f)
        {
            c.EndJob(_world, ToilStatus.Interrupted);
            _world.Log.Add(_world.Tick, LogKind.Work, $"{Ko.IGa(r.Name)} {Ko.EulReul(b.Name)} 맡았다 — {Ko.EunNeun(c.Name)} 하던 일로 돌아간다", c.Id);
        }
        if (job.Assignee == null) job.Robot = r;
    }

    /// <summary>심한 고장이 난 그 순간: 고칠 수 있는 동료 로봇이 있으면 바로 보낸다.</summary>
    private void FleetBroke(Robot b)
    {
        var w = _world;
        if (FleetSystem.Off || b.Fault is not RobotFault ft || CanSelfRepair(b) || b.Dock.Room.Detached || Robots.Any(x => x.Fixing == b)) return;
        TryFix(w.Fleet, b, ft, w.Automation.Present && (w.Automation.MainOnline || w.Automation.Core.BackupCore));
    }

    /// <summary>매 분: 불 속 · 자기 보존 · 교대 · 서로 돕기 · 같이 들기.</summary>
    internal void FleetTick(FleetSystem f, bool cmd)
    {
        var w = _world;
        foreach (var r in Robots)
        {
            if (r.State == RobotState.Lost) continue;
            if (r.Hauling != null && (r.State != RobotState.Active || r.Steps == null || !r.Steps.Any(st => st is RHitch))) { /* 끌던 일을 놓았다 (다른 일로 바뀜) */ r.Hauling.TowBot = null; if (r.Hauling.State == RobotState.Towed) SetState(r.Hauling, RobotState.Stalled); r.Hauling = null; }
            if (r.Fixing != null && r.Steps == null) r.Fixing = null;
            if (r.Partner is Robot p && !(r.Steps?.Any(s => s is RWith) ?? false) && !(p.Steps?.Any(s => s is RWith) ?? false)) { r.Partner = null; if (p.Partner == r) p.Partner = null; }
            if (r.Helping is CrewMember h) f.Bond(r, h);
            // 불길 속: 방열 외피도 오래 버티지는 못한다
            if (r.State is RobotState.Active or RobotState.Stalled && w.Fire.Count > 0 && w.Fire.AnyWithin(r.Cell, 1.2f))
            {
                r.Condition = MathF.Max(0f, r.Condition - (RobotsV15.Fireproof(r.Kind) ? 0.004f : 0.02f) * f.Hurt);
                if (r.Condition <= 0.001f) { Wreck(r, $"{r.Room?.Name ?? "?"} 불길 속에서 타 버렸다"); continue; }
            }
            // 자기 보존
            if (r.State == RobotState.Active && !r.Homing && !r.FightingFire && r.Hauling == null && r.Room is Room room)
            {
                var (ok, what) = f.Judge(r, room);
                if (!ok)
                {
                    f.Retreats++;
                    r.Mind.Say($"{room.Name} — {what}. 견딜 수 없어 물러난다", w.Tick);
                    Abort(r, $"{room.Name} — {what}, 물러난다");
                    continue;
                }
            }
            // 교대: 맡은 일 도중 배터리가 돌아갈 만큼만 남았다
            if (r.State == RobotState.Active && r.Order is WorkOrder o && !r.Homing && r.Hauling == null && r.Battery < ReturnCost(r) + 0.12f)
                Relieve(f, r, o, cmd);
        }
        foreach (var b in Robots)
        {
            if (b.Wrecked || b.Disabled || b.State == RobotState.Lost || b.Dock.Room.Detached) continue;
            if (Robots.Any(x => x.Fixing == b || x.Hauling == b)) continue;
            if (b.Fault is RobotFault ft && !CanSelfRepair(b) && b.State is RobotState.Docked or RobotState.Stalled) TryFix(f, b, ft, cmd);
            else if (b.State == RobotState.Stalled && b.Fault == null && b.TowedBy == null && b.TowBot == null) TryHaul(f, b, cmd);
            else if (b.State == RobotState.Docked && b.Fault == null && b.Condition < 0.75f && f.Mode == "평시" && w.Tick >= _nextTune) TryTune(f, b, cmd);
        }
        foreach (var r in Robots)
            if (r.State == RobotState.Active && r.Partner == null && !r.Homing && FleetSystem.Heavy(r.Cargo)) TryLift(f, r);
    }

    private void Relieve(FleetSystem f, Robot r, WorkOrder o, bool cmd)
    {
        var w = _world;
        Robot? fresh = null;
        float bd = float.MaxValue;
        foreach (var x in Robots)
        {
            if (x == r || x.State != RobotState.Docked || !x.Operational || x.Battery < 0.8f || !CanDo(x.Kind, o.Kind) || !RobotsV15.Takes(x.Kind, o)) continue;
            float d = Far(x.DockPosition, r.Position);
            if (d < bd) { bd = d; fresh = x; }
        }
        if (fresh == null) return;
        r.Mind.Say($"배터리 {r.Battery * 100:0}% — 돌아갈 몫만 남아 {fresh.Name}에게 넘기고 충전하러", w.Tick);
        Abort(r, null);
        f.Reliefs++;
        w.Log.Add(w.Tick, LogKind.Work, $"{r.Name} → {fresh.Name} 교대: {o.Title} ({o.Progress * 100:0}%에서)");
        fresh.Mind.Say($"{Ko.WaGwa(r.Name)} 교대 — {o.Title} 이어서", w.Tick);
        if (!cmd || w.Automation.Command.Order(fresh, o, 0.9f, $"{r.Name} 배터리가 다 됐다 — 교대 ({o.Progress * 100:0}%부터)") == null) Redirect(fresh, 0.9f);
    }

    /// <summary>멈춘(방전) 로봇을 다른 로봇이 충전대까지 끌고 온다.</summary>
    private void TryHaul(FleetSystem f, Robot b, bool cmd)
    {
        var w = _world;
        var job = JobFor(WorkKind.FetchRobot, b);
        if (job != null && job.Assignee != null) return; // 사람이 이미 간다
        Robot? best = null;
        float bd = float.MaxValue;
        foreach (var r in Robots)
        {
            if (r == b || !Spare(r, cmd) || !(RobotsV15.Base(r.Kind) is RobotKind.Hauler or RobotKind.Maintainer)) continue;
            float d = Far(r.Position, b.Position) + (Free(r) ? 0f : 20f);
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null) return;
        var dist = w.Paths.Flood(best.Cell, Profile);
        Cell? near = null;
        int nc = int.MaxValue;
        foreach (var d in Cell.Dirs8)
        {
            var s = b.Cell + d;
            if (!w.Ship.IsWalkable(s)) continue;
            int dd = dist.Get(s);
            if (dd < 0 || dd >= nc) continue;
            nc = dd; near = s;
        }
        if (near is not Cell at) return;
        var home = b.Dock.UseSpots.Where(w.Ship.IsWalkable).OrderBy(s => Far(s.Center, b.Position)).Cast<Cell?>().FirstOrDefault();
        if (home is not Cell dockSpot) return;
        if (best.State == RobotState.Active) Release(best);
        var steps = new List<RobotStep>
        {
            new RGoto(at), new RHitch(b), new RGoto(dockSpot),
            new RDo((rb, world) => { world.Robots.Delivered(rb, b); return true; }),
        };
        Begin(best, steps, $"멈춘 {Ko.EulReul(b.Name)} 끌고 오기");
        if (job != null && job.Assignee == null) job.Robot = best; // 사람 손을 막아 둔다 (로봇의 맡은 일로는 걸지 않는다 — 목록이 닫아도 하던 일을 놓지 않게)
        FleetSystem.Stage(best, steps, null);
        best.Mind.Say(cmd ? $"주 컴퓨터가 보냈다 — {Ko.IGa(b.Name)} {b.Room?.Name ?? "?"}에 멈췄다" : $"{Ko.IGa(b.Name)} 멈춘 걸 봤다", w.Tick);
        f.Line(CmdTarget.Robot, best.Id, b.Room, $"{best.Name}: 멈춘 {b.Name} 끌고 오기", $"{b.Doing}", 0.7f, 40f);
    }

    private long _nextTune;

    /// <summary>서로 손보기: 닳은(고장 나기 전) 로봇을 쉬던 정비 로봇이 윤활유를 들고 와서 조이고 닦는다 — 닳을수록 고장이 잦다.</summary>
    private void TryTune(FleetSystem f, Robot b, bool cmd)
    {
        var w = _world;
        _nextTune = w.Tick + SimTime.Minutes(10);
        if (JobFor(WorkKind.ServiceRobot, b) is WorkOrder svc && svc.Assignee != null) return; // 사람이 이미 손본다
        Robot? best = null;
        float bd = float.MaxValue;
        foreach (var r in Robots)
        {
            if (r == b || !Free(r) || r.State != RobotState.Docked || r.Battery < 0.5f || r.Condition < 0.6f || !(RobotsV15.Assists(r.Kind) || RobotsV15.Base(r.Kind) == RobotKind.Maintainer)) continue;
            float d = Far(r.DockPosition, b.DockPosition);
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null) return;
        var dist = w.Paths.Flood(best.Cell, Profile);
        var steps = new List<RobotStep>();
        var (box, spot) = Nearest(w, dist, x => x.Storage!.Count(ItemKind.Lubricant) > 0);
        bool lube = box != null;
        if (box != null) { steps.Add(new RGoto(spot)); steps.Add(new RTake(box, ItemKind.Lubricant, 1)); }
        var near = b.Dock.UseSpots.Where(c => w.Ship.IsWalkable(c) && dist.Reachable(c)).OrderBy(c => c.X).ThenBy(c => c.Y).Cast<Cell?>().FirstOrDefault();
        if (near is not Cell at) return;
        steps.Add(new RGoto(at));
        steps.Add(new RWork(0.3f, null, b.DockPosition));
        steps.Add(new RDo((rb, world) => world.Robots.Tuned(rb, b, lube)));
        steps.Add(new RTest(b));
        best.Fixing = b;
        Begin(best, steps, $"{Ko.EulReul(b.Name)} 손보러 — 상태 {b.Condition * 100:0}%");
        FleetSystem.Stage(best, steps, null);
        best.Mind.Say($"{b.Name} 상태가 {b.Condition * 100:0}%다 — 고장 나기 전에 손봐 준다" + (lube ? "" : " (윤활유가 없어 조이고 닦기만)"), w.Tick);
        f.Line(CmdTarget.Robot, best.Id, b.Dock.Room, $"{best.Name}: {b.Name} 손보기", $"상태 {b.Condition * 100:0}%", 0.35f, 40f);
    }

    internal bool Tuned(Robot r, Robot b, bool lube)
    {
        var w = _world;
        r.Fixing = null;
        bool used = lube && r.Cargo is ItemStack c && c.Kind == ItemKind.Lubricant;
        if (used) r.Cargo = r.Cargo!.Value.Count > 1 ? new ItemStack(ItemKind.Lubricant, r.Cargo.Value.Count - 1) : null;
        if (!b.AtDock || b.Fault != null) { Done(r, null); return true; }
        b.Condition = MathF.Max(b.Condition, used ? 0.9f + 0.02f * w.Fleet.Tier : 0.8f);
        MarkLog.Add(b.Marks, w.Tick, $"{Ko.IGa(r.Name)} 손봤다 ({(used ? "윤활 · 조임" : "조임 · 청소")})");
        w.Fleet.Tunes++;
        w.Fleet.CloseLine(CmdTarget.Robot, r.Id, "손봤다");
        Done(r, null);
        return true;
    }

    internal void Delivered(Robot r, Robot b)
    {
        var w = _world;
        if (b.TowBot != r) return;
        b.TowBot = null;
        r.Hauling = null;
        Returned(b);
        MarkLog.Add(b.Marks, w.Tick, $"{Ko.IGa(r.Name)} 끌고 왔다");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(r.Name)} 멈춘 {Ko.EulReul(b.Name)} 충전대까지 끌고 왔다");
        if (JobFor(WorkKind.FetchRobot, b) is WorkOrder job) w.Board.Close(job);
        w.Fleet.Tows++;
        w.Fleet.Method("tow:robot", true);
        w.Fleet.CloseLine(CmdTarget.Robot, r.Id, "끌고 왔다");
    }

    /// <summary>고장 난 로봇을 정비 로봇이 부품을 들고 와서 고친다 (부품이 한 가지인 고장만 — 두 가지면 사람 손).</summary>
    private void TryFix(FleetSystem f, Robot b, RobotFault fault, bool cmd)
    {
        var w = _world;
        var parts = FaultParts(fault);
        if (parts.Length > 1) return;
        var job = JobFor(WorkKind.RepairRobot, b);
        if (job != null && job.Assignee != null) return;
        Robot? best = null;
        float bd = float.MaxValue;
        foreach (var r in Robots)
        {
            if (r == b || !Spare(r, cmd) || !(RobotsV15.Assists(r.Kind) || RobotsV15.Base(r.Kind) == RobotKind.Maintainer)) continue;
            float d = Far(r.Position, b.Position) + (Free(r) ? 0f : 20f);
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null) return;
        var dist = w.Paths.Flood(best.Cell, Profile);
        var steps = new List<RobotStep>();
        if (parts.Length == 1)
        {
            var (kind, count) = parts[0];
            var (box, spot) = Nearest(w, dist, x => x.Storage!.Count(kind) >= count);
            if (box == null) return;
            steps.Add(new RGoto(spot));
            steps.Add(new RTake(box, kind, count));
        }
        Cell? near = null;
        int nc = int.MaxValue;
        var bc = Cell.FromPosition(b.Position);
        foreach (var d in Cell.Dirs8)
        {
            var s = bc + d;
            if (!w.Ship.IsWalkable(s)) continue;
            int dd = dist.Get(s);
            if (dd < 0 || dd >= nc) continue;
            nc = dd; near = s;
        }
        if (near is not Cell at) return;
        if (best.State == RobotState.Active) Release(best);
        steps.Add(new RGoto(at));
        steps.Add(new RWork(FaultHours(fault) * 0.9f, null, b.Position));
        steps.Add(new RDo((rb, world) => world.Robots.FixedBy(rb, b, parts)));
        steps.Add(new RTest(b));
        best.Fixing = b;
        Begin(best, steps, $"{Ko.EulReul(b.Name)} 고치러 — {FaultName(fault)}");
        if (job != null && job.Assignee == null) job.Robot = best; // 사람 손을 막아 둔다 (로봇의 맡은 일로는 걸지 않는다 — 목록이 닫아도 하던 일을 놓지 않게)
        FleetSystem.Stage(best, steps, null);
        best.Mind.Say(cmd ? $"주 컴퓨터가 보냈다 — {b.Name} {FaultName(fault)}" : $"{b.Name} {FaultName(fault)} — 고칠 수 있는 고장", w.Tick);
        f.Line(CmdTarget.Robot, best.Id, b.Room, $"{best.Name}: {b.Name} 고치기 ({FaultName(fault)})", parts.Length == 0 ? "다시 맞추기만" : $"{ItemKinds.Name(parts[0].kind)} {parts[0].count}", 0.7f, 60f);
    }

    internal bool FixedBy(Robot r, Robot b, (ItemKind kind, int count)[] parts)
    {
        var w = _world;
        r.Fixing = null;
        if (b.Fault is not RobotFault ff) return true;
        if (parts.Length == 1)
        {
            if (r.Cargo is not ItemStack c || c.Kind != parts[0].kind || c.Count < parts[0].count) return false;
            r.Cargo = c.Count > parts[0].count ? new ItemStack(c.Kind, c.Count - parts[0].count) : null;
        }
        b.Fault = null;
        b.SelfRepairDone = 0f;
        b.SelfRepairs = 0;
        b.Condition = MathF.Max(b.Condition, 0.7f + 0.05f * w.Fleet.Tier);
        MarkLog.Add(b.Marks, w.Tick, $"{Ko.IGa(r.Name)} 고쳤다 ({FaultName(ff)})");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(r.Name)} {Ko.EulReul(b.Name)} 고쳤다 — {FaultName(ff)}");
        if (b.State == RobotState.Stalled && b.Battery > 0.08f) { SetState(b, RobotState.Active); GoHome(b, "고쳐져 충전대로"); }
        if (JobFor(WorkKind.RepairRobot, b) is WorkOrder job) w.Board.Close(job);
        w.Fleet.Fixes++;
        w.Fleet.CloseLine(CmdTarget.Robot, r.Id, "고쳤다");
        Done(r, null);
        return true;
    }

    /// <summary>무거운 짐(금속판 · 구조재 · 모터 · 펌프)을 든 로봇 곁에 쉬던 운반 로봇이 붙는다.</summary>
    private void TryLift(FleetSystem f, Robot c)
    {
        var w = _world;
        Robot? best = null;
        float bd = 14f;
        foreach (var r in Robots)
        {
            if (r == c || !Free(r) || r.State != RobotState.Docked || !(RobotsV15.Base(r.Kind) is RobotKind.Hauler or RobotKind.Maintainer)) continue;
            float d = Far(r.DockPosition, c.Position);
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null) return;
        var steps = new List<RobotStep> { new RGoto(rb => rb.Partner?.Cell), new RWith(c) };
        c.Partner = best;
        best.Partner = c;
        Begin(best, steps, $"{Ko.WaGwa(c.Name)} {c.Cargo} 같이 들기");
        FleetSystem.Stage(best, steps, null);
        best.Mind.Say($"{Ko.IGa(c.Name)} 무거운 짐({c.Cargo})을 혼자 끈다 — 같이 들면 빨라진다", w.Tick);
        c.Mind.Say($"{Ko.IGa(best.Name)} 같이 든다", w.Tick);
        f.Lifts++;
    }

    /// <summary>주 컴퓨터가 이 방의 불로 보낸다 (가장 가까운 불 곁 칸).</summary>
    internal bool FleetFire(Robot r, Room room)
    {
        var w = _world;
        if (r.FightingFire && r.Goal is Cell g && w.Ship.RoomAt(g) == room) return true;
        if (r.State is not (RobotState.Docked or RobotState.Active)) return false;
        var dist = w.Paths.Flood(r.Cell, Profile);
        Cell? best = null;
        int bc = int.MaxValue;
        foreach (var (cell, _) in w.Fire.Fires)
        {
            if (w.Ship.RoomAt(cell) != room) continue;
            foreach (var d in Cell.Dirs8)
            {
                var s = cell + d;
                if (!w.Ship.IsWalkable(s) || w.Fire.At(s) > 0f) continue;
                int dd = dist.Get(s);
                if (dd < 0 || dd >= bc) continue;
                bc = dd; best = s;
            }
        }
        if (best is not Cell at) return false;
        if (r.State == RobotState.Active) DropTask(r);
        r.Homing = false;
        FiresFought++;
        r.FightingFire = true;
        var steps = new List<RobotStep>
        {
            new RGoto(at), new RSpray(),
            new RDo((rb, world) => { Done(rb, $"{room.Name} 불에 거품을 뿌렸다 (거품 {rb.Foam * 100:0}%)"); return true; }),
        };
        Begin(r, steps, $"{room.Name} 불 끄러 — 사람보다 먼저");
        FleetSystem.Stage(r, steps, null);
        return true;
    }

    /// <summary>부서졌다 (되살릴 수 없다): 일 · 짐 · 고리를 놓고, 같이 일하던 사람이 아쉬워한다.</summary>
    internal void Wreck(Robot r, string why)
    {
        var w = _world;
        if (r.Hauling is Robot h) { h.TowBot = null; if (h.State == RobotState.Towed) SetState(h, RobotState.Stalled); r.Hauling = null; }
        if (r.TowBot is Robot tb) { tb.Hauling = null; r.TowBot = null; }
        if (r.Partner is Robot p) { if (p.Partner == r) p.Partner = null; r.Partner = null; }
        DropTask(r);
        r.TowedBy = null;
        r.FightingFire = false;
        r.Wrecked = true;
        r.Fault = null;
        SetState(r, RobotState.Lost);
        r.Doing = why;
        MarkLog.Add(r.Marks, w.Tick, why);
        w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(r.Name)} {why}", r.Room, log: true);
        w.Fleet.Mourn(r, why);
    }
}
