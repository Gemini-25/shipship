using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.25 로봇도 갈래 하나를 맡는다: 몸으로 구멍을 막고 버틴다 · 미끄러지는 짐을 붙든다 (주컴퓨터가 견줘 보내거나 함대 지휘가 고른다).
public sealed partial class RobotSystem
{
    internal void WayHold(WaysSystem ways, WayCase k, Way way)
    {
        var w = _world;
        Robot? best = null;
        float bd = float.MaxValue;
        foreach (var r in Robots)
        {
            if (!Free(r) || r.Steps != null && r.State == RobotState.Active && r.Order is { Urgency: >= 0.5f }) continue;
            float d = MathF.Abs(r.Position.X - k.At.X) + MathF.Abs(r.Position.Y - k.At.Y);
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null) return;
        var room = ways.RoomOf(k);
        Cell? spot = null;
        foreach (var d in Cell.Dirs8)
        {
            var s = k.At + d;
            if (!w.Ship.IsWalkable(s) || room != null && w.Ship.RoomAt(s) != room) continue;
            spot = s; break;
        }
        if (spot is not Cell at) return;
        var t = ways.RobotTry(k, way, best);
        if (best.State == RobotState.Active) DropTask(best);
        var steps = new List<RobotStep>
        {
            new RGoto(at),
            new RWork(way.Minutes / 60f, null, k.At.Center),
            new RDo((rb, world) => world.Ways.RobotApply(t, rb, at)),
            new RWork(2f, null, k.At.Center) { CanContinue = (rb, world) => world.Ways.Holding(t) },
        };
        Begin(best, steps, way.Name);
        FleetSystem.Stage(best, steps, null);
        best.Mind.Say(way.Snag == Snag.Breach ? "주 컴퓨터가 보냈다 — 몸으로 막는다" : "주 컴퓨터가 보냈다 — 짐을 붙든다", w.Tick);
    }
}

public sealed partial class WaysSystem
{
    internal WayTry RobotTry(WayCase k, Way way, Robot r)
    {
        var t = new WayTry { Id = _nextTry++, CaseId = k.Id, WayId = way.Id, RobotId = r.Id, Why = k.ComputerWhy, Chosen = _w.Tick, State = 1 };
        t.Started = _w.Tick;
        Tries.Add(t);
        Stats.Robots++;
        return t;
    }

    internal bool RobotApply(WayTry t, Robot r, Cell from)
    {
        var w = _w;
        var k = Case(t.CaseId);
        if (k == null || t.State == 3) return false;
        var way = t.Way;
        var room = RoomOf(k);
        if (way.Snag == Snag.Breach && w.Ship.WallAt(k.At) is WallState wall && !wall.Patched && wall.Breach > 0f && room != null)
        {
            wall.Patched = true;
            wall.PatchQuality = MathF.Min(0.85f, WaysRules.PlugQuality(Material.Metal, 0.2f, wall.Breach));
            int fid = NextFollow();
            Plugs.Add(new WayPlug { Wall = k.At, Mat = Material.Metal, Decay = 0.05f, Name = r.Name, Since = w.Tick, Follow = fid, Robot = r.Id });
            AddFollow(new WayFollow { Id = fid, WayId = way.Id, Text = $"{room.Name} 구멍 — {Ko.EulReul(r.Name)} 비키고 제대로 막기", Kind = 0, At = k.At, RoomId = room.Id, Since = w.Tick });
            AddMark(new WayMark { Look = WayLook.RobotBrace, At = k.At, RoomId = room.Id, Since = w.Tick, Try = t.Id, Who = -1, Mat = Material.Metal, Q = wall.PatchQuality, Dir = from - k.At, Active = false });
            MarkLog.Add(r.Marks, w.Tick, $"{room.Name} 구멍을 몸으로 막았다");
            t.State = 2;
            w.Log.Add(w.Tick, LogKind.Work, $"{r.Name}: {room.Name} 구멍에 몸을 대고 버틴다");
            return true;
        }
        if (way.Snag == Snag.CargoLoose && room != null)
        {
            foreach (var a in w.Matter.Things) if (a.Loose && w.Ship.RoomAt(a.At) == room) a.Vel = default;
            AddMark(new WayMark { Look = WayLook.RobotBrace, At = from, RoomId = room.Id, Since = w.Tick, Try = t.Id, Who = -1, Until = w.Tick + SimTime.Hours(4) });
            t.State = 2;
            return true;
        }
        Finish(t, false, "할 게 없었다", null);
        return false;
    }

    /// <summary>로봇이 아직 버티고 있어야 하나 (제대로 막으면 비킨다).</summary>
    internal bool Holding(WayTry t)
    {
        var k = Case(t.CaseId);
        if (k == null || t.State == 3) return false;
        if (t.Way.Snag == Snag.Breach)
        {
            var plug = Plugs.FirstOrDefault(p => p.Wall == k.At && p.Robot >= 0 && !p.Gone);
            if (plug == null) { Finish(t, true, "제대로 막을 때까지 버텼다", null); return false; }
            return true;
        }
        if (_w.Tick - t.Started > SimTime.Hours(1.5f) || !k.Open) { Finish(t, true, "짐이 멎을 때까지 붙들었다", null); return false; }
        return true;
    }
}
