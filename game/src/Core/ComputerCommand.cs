using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.20 ③ 명령선: 원격으로 되는 건 컴퓨터가 직접(밸브 · 문 · 댐퍼 · 차단기 · 콘센트 · 발전기 · 원자로 · 배터리 · 설비 · 방송 · 단말),
//  손이 꼭 필요한 일만 사람 · 로봇에게 정확한 지시로 (누구에게 · 무엇을 · 왜 · 언제까지). 화면은 컴퓨터에서 대상까지 선을 긋는다.
//  로봇 · 드론 지휘의 바탕: Order(로봇, 일, 우선, 이유) — 로봇이 일을 고를 때 그 일에 무게가 실린다 (v16.20b가 계획 · 교대 충전 · 위험 판단을 얹는다).

public enum CmdTarget { Valve, Door, Damper, Breaker, Circuit, Outlet, Generator, Reactor, Battery, Machine, Room, Crew, Robot, Drone, Broadcast, Terminal, Self }

/// <summary>명령 한 줄.</summary>
public sealed class ComputerOrder
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public CmdTarget Target { get; init; }
    /// <summary>대상 번호 (설비 · 문 · 사람 · 로봇 Id · 회로 번호).</summary>
    public int TargetId { get; init; } = -1;
    public int RoomId { get; init; } = -1;
    public string What { get; init; } = "";
    public string Why { get; init; } = "";
    public float Priority { get; init; }
    public long Until { get; init; }
    /// <summary>"보냄" · "하는 중" · "끝" · "실패" · "취소".</summary>
    public string State { get; set; } = "보냄";
    public string Result { get; set; } = "";
    public string By { get; init; } = "";
    /// <summary>이 명령을 낳은 결정 (타임라인 Id · 없으면 −1).</summary>
    public int Decision { get; init; } = -1;
    public int WorkOrderId { get; init; } = -1;
    /// <summary>원격으로 직접 (선을 따라 빛) ↔ 사람 · 로봇 손으로.</summary>
    public bool Remote => Target is not (CmdTarget.Crew or CmdTarget.Robot or CmdTarget.Drone);
    public bool Open => State is "보냄" or "하는 중";
}

public sealed class ComputerCommand
{
    private readonly World _w;
    private int _next = 1;
    public List<ComputerOrder> Lines { get; } = new();
    public int Remote, Hands, RobotOrders, Woken, Done, Failed;
    private readonly Dictionary<int, ComputerOrder> _robot = new();
    private long _next2;

    public ComputerCommand(World w) => _w = w;

    private string By => _w.Automation.MainOnline ? "주 컴퓨터" : _w.Automation.Core.BackupCore ? "예비 연산기" : "방 제어기";

    /// <summary>명령 한 줄을 남긴다 (원격 조작 · 지시 모두).</summary>
    public ComputerOrder Line(CmdTarget t, int id, Room? room, string what, string why, float priority = 0.5f, float minutes = 10f, int decision = -1, int workOrder = -1, string state = "끝")
    {
        var o = new ComputerOrder
        {
            Id = _next++, Tick = _w.Tick, Target = t, TargetId = id, RoomId = room?.Id ?? -1, What = what, Why = why, Priority = priority,
            Until = _w.Tick + SimTime.Minutes(minutes), By = By, Decision = decision, WorkOrderId = workOrder, State = state,
        };
        Lines.Add(o);
        if (Lines.Count > 160) Lines.RemoveAt(0);
        if (o.Remote) Remote++; else Hands++;
        return o;
    }

    /// <summary>사람에게 정확한 지시: 개인 단말로 그 일을 부탁한다 (위기면 지휘 조 편성보다 앞서지는 않는다).</summary>
    public ComputerOrder? Assign(CrewMember c, WorkOrder job, string why, bool crisis, int decision = -1)
    {
        var w = _w;
        if (c.Dead || !c.CanAct) return null;
        if (c.Pose == Pose.Sleeping) Wake(c, $"{job.Title} — {why}");
        w.Automation.CrewModel.Ask(c, job, why, crisis ? 1f : 4f, crisis);
        w.Automation.Apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "지시", $"{job.Title} — {why}"));
        return Line(CmdTarget.Crew, c.Id, job.Target.CurrentRoom, $"{c.Name}: {job.Title}", why, job.Urgency, crisis ? 20f : 60f, decision, job.Id, "보냄");
    }

    /// <summary>깨우기: 개인 단말 진동 · 방 스피커 (손이 필요한 위기 · 위험한 방에서 자는 사람).</summary>
    public void Wake(CrewMember c, string why)
    {
        var w = _w;
        if (c.Dead || c.Pose != Pose.Sleeping) return;
        c.Jolt(w);
        Woken++;
        w.Log.Add(w.Tick, LogKind.Life, Persona.Say(c, $"단말이 울려 깼다 — {why}"), c.Id);
        Line(CmdTarget.Terminal, c.Id, c.Room, $"{c.Name} 깨우기", why, 0.8f, 5f);
    }

    /// <summary>로봇 명령 API (v16.20b가 쓴다): 이 로봇에게 이 일을 우선으로. 로봇이 일을 고를 때 무게가 실린다.</summary>
    public ComputerOrder? Order(Robot robot, WorkOrder job, float priority, string why, int decision = -1)
    {
        var w = _w;
        if (robot.Disabled || robot.State is RobotState.Lost or RobotState.Towed || robot.Fault != null) return null;
        if (_robot.TryGetValue(robot.Id, out var old) && old.Open) { if (old.WorkOrderId == job.Id) return old; old.State = "취소"; old.Result = "다른 일로 바꿨다"; }
        var o = Line(CmdTarget.Robot, robot.Id, job.Target.CurrentRoom, $"{robot.Name}: {job.Title}", why, priority, 60f, decision, job.Id, "보냄");
        _robot[robot.Id] = o;
        RobotOrders++;
        robot.NextDecide = Math.Min(robot.NextDecide, w.Tick); // 충전대에서 기다리던 로봇도 바로 생각한다
        return o;
    }

    /// <summary>로봇 일 고르기 훅: 컴퓨터가 시킨 일이면 점수를 얹는다.</summary>
    public float Bias(Robot r, WorkOrder o) =>
        _robot.Count > 0 && _robot.TryGetValue(r.Id, out var c) && c.Open && c.WorkOrderId == o.Id ? 0.5f + c.Priority : 0f;

    /// <summary>1분마다: 지시 · 로봇 명령의 끝을 본다 (일이 닫혔나 · 기한).</summary>
    public void Update()
    {
        var w = _w;
        if (w.Tick < _next2) return;
        _next2 = w.Tick + SimTime.Minutes(1);
        foreach (var o in Lines)
        {
            if (!o.Open) continue;
            var job = o.WorkOrderId >= 0 ? w.Board.All.FirstOrDefault(x => x.Id == o.WorkOrderId) : null;
            if (o.Target == CmdTarget.Robot && w.Robots.Robots.FirstOrDefault(r => r.Id == o.TargetId) is Robot rb && rb.Order?.Id == o.WorkOrderId) o.State = "하는 중";
            if (o.Target == CmdTarget.Crew && w.Crew.FirstOrDefault(c => c.Id == o.TargetId) is CrewMember cm && cm.Job?.Order?.Id == o.WorkOrderId) o.State = "하는 중";
            if (job != null && job.Closed) { o.State = "끝"; o.Result = "일이 끝났다"; Done++; continue; }
            if (job == null && o.WorkOrderId >= 0) { o.State = "끝"; o.Result = "일이 사라졌다 (다른 사람이 했거나 필요 없어졌다)"; Done++; continue; }
            if (w.Tick > o.Until) { o.State = "실패"; o.Result = "기한 안에 못 했다"; Failed++; }
        }
    }

    internal void Hash(Action<long> I, Action<float> F) { I(Lines.Count); I(Remote); I(Hands); I(RobotOrders); I(Woken); I(Done); I(Failed); }
}

public sealed partial class AutomationSystem
{
    private ComputerCommand? _command;
    /// <summary>v16.20 명령선 · 사람 지시 · 로봇 명령 API.</summary>
    public ComputerCommand Command => _command ??= new ComputerCommand(_world);
}
