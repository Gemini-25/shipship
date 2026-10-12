using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v21 주컴퓨터 강화 — "맨날 틀린다"를 줄인다:
//  · 교차 확인: 화재 감지기 하나만 울리고 같은 방의 온도 · 연기 계기가 조용하면 경보부터 울리지 않고 먼저 확인한다 (헛불로 사람을 뛰게 하지 않는다).
//  · 로봇 눈: 확인은 로봇이 먼저 — 그 방에 로봇이 있으면 카메라로 바로, 없으면 가장 가까운 한가한 로봇을 보낸다. 사람은 로봇이 없을 때만 부른다.
//  · 예측: 하루 주기가 있는 값(기력)은 어제 같은 때로, 나머지는 열두 시간 추세를 눌러서 (사람이 대비하면 꺾인다) 내다본다.
//    대비해서 문턱을 안 넘었으면 "틀렸다"가 아니라 "참고"로 채점한다.
//  · 권한: 로봇 지휘(긴급 지정) 권한이 새로 생겼다 (RobotsV21.cs).

public sealed partial class AutomationSystem
{
    public int RobotChecks, RobotChecksInstant, GhostsFiltered;

    /// <summary>로봇 눈으로 확인 (사람 확인 대신). 맡겼으면 true.</summary>
    internal bool RobotEyes(Room room, string why, bool ghost)
    {
        var w = _world;
        if (!RobotsV21.General || FleetSystem.Off || room.Detached) return false;
        // 그 방에 이미 로봇이 있다 — 카메라로 바로 본다
        var here = w.Robots.Robots.FirstOrDefault(r => r.Operational && !r.Runaway && r.Room == room && r.State is RobotState.Active or RobotState.Docked);
        if (here != null)
        {
            RobotChecksInstant++;
            RobotSaw(room, here, why, ghost);
            return true;
        }
        var r = w.Robots.Dispatch(room, why);
        if (r == null) return false;
        RobotChecks++;
        Book.Add(ActKind.Advice, room, why, "감지기만 믿지 않는다 — 로봇 눈으로 본다", "로봇 확인", $"{r.Name} → {room.Name}", "rchk:" + room.Id + ":" + w.Tick, 0, 20f,
            (world, a) => (1, "확인 — 사람을 부르지 않고 로봇이 봤다"));
        w.Log.Add(w.Tick, LogKind.Ship, $"{Voice.Call}: {Ko.IGa(r.Name)} {room.Name}에 가서 본다 — {why}");
        return true;
    }

    /// <summary>로봇이 그 방을 봤다: 믿음을 실제에 맞춘다 (헛불이면 감지기를 덜 믿는다 · 사람이 있으면 센다).</summary>
    internal void RobotSaw(Room room, Robot r, string why, bool ghost)
    {
        var w = _world;
        var b = Belief.Of(room);
        int inside = w.Crew.Count(c => !c.Dead && c.Room == room && !c.Outside);
        bool wrongPeople = b.People != inside || b.Fault == SensorFault.Blind;
        bool isGhost = b.Fault == SensorFault.Ghost && w.Fire.CountIn(room) == 0;
        if (isGhost) b.FalseAlarms++;
        Belief.Reset(room, r.Name, r.Id, wrongPeople || isGhost);
        string what = isGhost ? "불은 없었다 — 감지기 오경보 (덜 믿는다)" : w.Fire.CountIn(room) > 0 ? $"불 {w.Fire.CountIn(room)}칸 — 진짜다" : inside > 0 ? $"안에 {inside}명" : "별일 없다";
        w.Log.Add(w.Tick, LogKind.Work, $"{r.Name}: {room.Name} 카메라로 확인 — {what}");
        MarkLog.Add(r.Marks, w.Tick, $"{room.Name} 확인 ({what})");
        r.Mind.Say($"주 컴퓨터 부탁 — {room.Name}: {what}", w.Tick);
    }

    /// <summary>교차 확인: 화재 감지기 말고 같은 방 계기(온도 · 연기)도 불을 말하나.</summary>
    internal bool Corroborated(Room r) => r.Air.Temperature > 38f || r.Air.Smoke > 0.08f || _world.Fire.CountIn(r) > 0;
}

public sealed partial class RobotSystem
{
    /// <summary>주 컴퓨터가 부탁한 확인: 가장 가까운 한가한 로봇 (의료 로봇은 빼고)을 그 방에 보낸다.</summary>
    internal Robot? Dispatch(Room room, string why)
    {
        var w = _world;
        Robot? best = null; float bd = float.MaxValue;
        foreach (var r in Robots)
        {
            if (!Free(r) || r.Runaway || r.Upgrading || RobotsV21.Medical(r.Kind)) continue;
            float d = Far(r.Position, room.Center);
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null || bd > 120f) return null;
        var dist = w.Paths.Flood(best.Cell, Profile);
        var spot = room.Cells.Where(w.Ship.IsWalkable).Where(dist.Reachable).OrderBy(c => (c.Center - room.Center).LengthSquared()).ThenBy(c => c.X).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell at) return null;
        if (best.State == RobotState.Docked) SetState(best, RobotState.Active);
        var target = room;
        Begin(best, new List<RobotStep>
        {
            new RGoto(at), new RWork(0.04f, null, room.Center),
            new RDo((rb, world) => { world.Automation.RobotSaw(target, rb, why, true); Done(rb, null); return true; }),
        }, $"{room.Name} 확인 (주 컴퓨터)");
        w.Fleet.Line(CmdTarget.Robot, best.Id, room, $"{best.Name}: {room.Name} 확인", why, 0.6f, 20f);
        return best;
    }
}
