using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.26 ① 여러 단계 복구 계획 · ④ 복구 순서를 직접 짜고 고친다.
//  한 수를 견주던 것(ComputerForesee) → 수순(행동의 조합)을 견준다. 수순마다 성공 조건 · 중단 조건 · 필요한 자원 · 실패 때 대안 · 뒤따르는 문제 ·
//  모르는 부분은 범위로(35~50분). 원격 일과 사람 일을 한 계획에 — 사람이 붙은 설비는 재기동 잠금 · 늦으면 다시 짠다 · 부품이 오기 전에 사람을 세우지 않는다 ·
//  전문가에게 몰리면 나눈다 · 현장에서 "안 된다" 하면 대안 · 완료 = 명령이 아니라 기능이 돌아와 버팀(시험 운전 · 재고장 감시).
//  비교는 큰 변화(고장 · 보고 · 자원 부족) 때만 — 가벼운 모형(세계 복제 없음): 노심 온도 · 냉각 · 출력 · 배터리를 분 단위로 (ComputerPlanSteps.cs).
//  v16.25 해법 표 접점: FixBook.Branches — 문제 하나에 갈래(FixOption)를 더 내놓으면 같은 저울에 함께 올라간다 (걸음은 FixAction 하나).

/// <summary>걸음의 성격: 원격 · 사람 손 · 시험 운전 · 지켜보기 · 기다림.</summary>
public enum FixKind { Remote, Hands, Test, Watch, Wait }
public enum FixState { Wait, Run, Done, Failed, Skipped }

/// <summary>계획의 한 걸음 = 일반 행동 하나 (v16.25 해법 갈래도 이 모양으로 끼운다).</summary>
public abstract class FixAction
{
    public abstract string Name { get; }
    public virtual FixKind Kind => FixKind.Remote;
    /// <summary>시작할 수 있나: null = 된다 · 아니면 기다리는 까닭 (화면의 "다음 실행 조건").</summary>
    public virtual string? Ready(World w, FixPlan p, FixStep s) => null;
    public virtual void Begin(World w, FixPlan p, FixStep s) { }
    /// <summary>1분마다: Done · Failed · Run (까닭은 s.Note).</summary>
    public abstract FixState Tick(World w, FixPlan p, FixStep s);
    /// <summary>예상보다 늦다 — 고쳐 짤 수 있으면 까닭을 돌려준다.</summary>
    public virtual string? Late(World w, FixPlan p, FixStep s) => null;
    public virtual void Cancel(World w, FixPlan p, FixStep s) { }
    /// <summary>예상 시간 범위 (분).</summary>
    public virtual (float min, float max) Span(World w, FixCase c) => (1f, 2f);
    // ── 가벼운 모형에 주는 효과 (ComputerPlanSteps.FixModel) ──
    public virtual float CoolAdd(FixCase c) => 0f;
    public virtual float CapSet => -1f;
    public virtual bool Restores => false;
    /// <summary>위험한 원격 명령이면 유효 시간(분) — 끊겼다 이어지면 지나간 명령은 하지 않는다.</summary>
    public virtual float Valid => 0f;
    /// <summary>설비 Id (재기동 잠금 · 스스로 감시에 쓴다).</summary>
    public virtual int Target => -1;
}

public sealed class FixStep
{
    public FixAction Act { get; init; } = null!;
    public FixState State { get; set; }
    public float Min { get; set; }
    public float Max { get; set; }
    public long Start { get; set; } = -1;
    public long End { get; set; } = -1;
    /// <summary>다음 실행 조건 (무엇을 기다리나).</summary>
    public string Waiting { get; set; } = "";
    public string Note { get; set; } = "";
    public int Crew { get; set; } = -1;
    public int Order { get; set; } = -1;
    /// <summary>원격 명령을 보냈다 (예비로 넘어가도 두 번 안 나가게).</summary>
    public bool Issued { get; set; }
    public long IssuedAt { get; set; } = -1;
    public int Late { get; set; }
    public long Mark { get; set; }
    public string Name => Act.Name;
    public float Took(long now) => Start < 0 ? 0f : ((End >= 0 ? End : now) - Start) / (float)SimTime.Minutes(1);
}

/// <summary>견준 수순 하나.</summary>
public sealed class FixOption
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public List<FixAction> Steps { get; } = new();
    public float Min, Max, PeakMin, PeakMax, BatteryKwh, LostKwh, Risk, Score;
    public bool Allowed = true;
    public string Blocked = "";
    public string Note = "";
    public string Success = "", Abort = "", Needs = "", After = "";
}

/// <summary>문제 하나 (가벼운 모형의 시작 값 — 컴퓨터가 믿는 값).</summary>
public sealed class FixCase
{
    public string Problem = "";
    public Furniture? Target;
    public Door? Door;
    public Room? Room;
    public float T0, Scram, Warn, Demand, Cool, LostKw, BoostKw, RMax, Cap = 1f, BatteryKwh, BatteryCap;
    public bool ReactorOn, HavePart, Boosted;
    public string PartName = "";
    public float RepairHours;
    public CrewMember? Hand;
    public float Pace = 1f;
}

public sealed class FixPlan
{
    public int Id { get; init; }
    public long Tick { get; init; }
    /// <summary>냉각 · 문 · 설비 · 유량.</summary>
    public string Problem { get; init; } = "";
    public string Key { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public int TargetId { get; init; } = -1;
    public string Goal { get; set; } = "";
    public string Name { get; set; } = "";
    public string OptionKey { get; set; } = "";
    public List<FixStep> Steps { get; } = new();
    public int Cur { get; set; }
    public string Success { get; set; } = "";
    public string Abort { get; set; } = "";
    public string Needs { get; set; } = "";
    public string Fallback { get; set; } = "";
    public string FallbackKey { get; set; } = "";
    public string After { get; set; } = "";
    public float Min, Max, PeakMin, PeakMax, Risk, MarginC;
    public List<FixOption> Compared { get; } = new();
    public List<(long tick, string why)> Revisions { get; } = new();
    /// <summary>진행 · 성공 · 중단 · 넘김.</summary>
    public string State { get; set; } = "진행";
    public long Ended { get; set; } = -1;
    public long Changed { get; set; }
    public int Replans { get; set; }
    public FixStep? Step => Cur < Steps.Count ? Steps[Cur] : null;
    public bool Open => State == "진행";
    public float Elapsed(long now) => ((Ended >= 0 ? Ended : now) - Tick) / (float)SimTime.Minutes(1);
    public string Range => Max - Min < 1.5f ? $"{Min:0}분" : $"{Min:0}~{Max:0}분";
}

public sealed class FixBook
{
    private readonly World _w;
    private Rng? _rng;
    internal Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7643 + 3301));
    private int _next = 1;
    private long _tickNext;
    public List<FixPlan> Plans { get; } = new();
    public int Made, Succeeded, Aborted, Revised, Replans, Late, Steps, RemoteSteps, HandSteps, Compares, DoorDetours, TestFails, LockWaits, Spread;
    /// <summary>원자로 출력 상한 (계획의 감출력 걸음) — AutomationSystem.ReactorCap에 곱한다.</summary>
    public float ReactorCap { get; internal set; } = 1f;
    private readonly Dictionary<int, float> _drive = new();
    /// <summary>닫으라 한 문을 처음 열린 채로 본 틱 (이 배에서 문이 닫히는 시간을 잰다).</summary>
    internal Dictionary<int, long> DoorSeen { get; } = new();
    /// <summary>시험: 꺼 두고 견준다 (성능).</summary>
    public static bool Off { get; set; }
    /// <summary>v16.25 해법 표 접점: 문제 하나에 갈래를 더 내놓는다 (같은 저울에 올라간다).</summary>
    public static Func<World, FixCase, IEnumerable<FixOption>>? Branches { get; set; }
    /// <summary>계획이 쓴 시간 (지문에 넣지 않는다 · 성능 시험).</summary>
    public static long Stopwatch { get; set; }

    public FixBook(World w) => _w = w;

    public IEnumerable<FixPlan> Open => Plans.Where(p => p.Open);
    public FixPlan? Lead => Plans.Where(p => p.Open).OrderBy(p => p.Problem == "냉각" ? 0 : p.Problem == "문" ? 1 : 2).ThenBy(p => p.Id).FirstOrDefault();
    public FixPlan? For(string key) => Plans.LastOrDefault(p => p.Key == key && p.Open);

    /// <summary>Piping.ComputeCooling 훅: 계획이 펌프를 더 세게(예비 펌프로 시간 벌기) · 약하게(시험 운전) 돌린다.</summary>
    public float Drive(Furniture f) => _drive.TryGetValue(f.Id, out var d) ? d : 1f;
    internal void SetDrive(Furniture f, float d) { if (MathF.Abs(d - 1f) < 0.001f) _drive.Remove(f.Id); else _drive[f.Id] = d; }
    internal bool Boosting => _drive.Values.Any(v => v > 1.01f);

    /// <summary>사람이 붙어 일하는 설비 (재기동 잠금 — 원격으로 켜지 않는다).</summary>
    public static bool WorkLock(World w, Machine m) =>
        m.LockedOut || w.Crew.Any(c => !c.Dead && c.Job?.Order is WorkOrder o && o.Target.Furniture == m.Body && c.Pose == Pose.Working);

    // ───────────── 한 틱 (1분마다 · 큰 변화 때만 비교) ─────────────

    public void Update()
    {
        var w = _w;
        if (Off || w.Tick < _tickNext) return;
        _tickNext = w.Tick + SimTime.Minutes(1);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var a = w.Automation;
        if (a.CoreOnline)
        {
            Trigger();
            foreach (var p in Plans.Where(p => p.Open).ToList()) Run(p);
        }
        // 더 세게 돌리는 펌프는 그만큼 빨리 닳는다 (뒤따르는 문제)
        foreach (var (id, d) in _drive)
            if (d > 1.01f && w.Ship.Furniture.FirstOrDefault(f => f.Id == id)?.Machine is Machine bm) bm.Wear = MathF.Min(1f, bm.Wear + 0.0015f * (d - 1f) * 4f);
        // 계획이 없으면 걸어 둔 감출력 · 펌프 세기를 천천히 푼다 (주인이 없는 명령이 남지 않게)
        if (!Plans.Any(p => p.Open && p.Problem == "냉각"))
        {
            if (ReactorCap < 1f) ReactorCap = MathF.Min(1f, ReactorCap + 0.1f);
            if (_drive.Count > 0) _drive.Clear();
        }
        if (Plans.Count > 40) Plans.RemoveAll(p => !p.Open && Plans.IndexOf(p) < Plans.Count - 30);
        Stopwatch += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>큰 변화만 본다: 냉각 펌프 고장 · 핵심 설비 고장 · 닫으라 했는데 열린 문 (방 단위 · 설비 단위).</summary>
    private void Trigger()
    {
        var w = _w;
        var a = w.Automation;
        int open = Plans.Count(p => p.Open);
        if (open >= 5) return;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.CoolantPump))
        {
            var m = f.Machine!;
            if (!w.Power.ReactorOnline || m.Faults.Count == 0 || m.Faults.All(x => x.OutputFactor >= 0.9f)) continue;
            if (m.Faults.Where(x => x.OutputFactor < 0.9f).Max(x => x.Since) > w.Tick - 2 * World.SystemInterval) continue; // 계기 값이 들어온 뒤에 (냉각 수치가 고장을 반영한 다음)
            if (For($"냉각:{f.Id}") != null || Recent($"냉각:{f.Id}", 20f)) continue;
            FixSteps.MakeCooling(w, this, f);
            if (++open >= 5) return;
        }
        // 문 닫기 명령 뒤 센서가 열림 (기능이 안 돌아왔다)
        foreach (var r in w.Ship.LiveRooms)
        {
            if (!r.Lockdown || !a.DoorsIn(r)) continue;
            foreach (var d in r.Doors)
            {
                if (!d.Locked || d.Openness < 0.1f || d.IsExternal || d.Removed || d.HoldOpen) continue; // 10% 넘게 열려 있으면 기밀이 안 된다
                if (!FixSteps.DoorStuck(w, d)) continue;
                if (For($"문:{d.Id}") != null || Recent($"문:{d.Id}", 15f)) continue;
                FixSteps.MakeDoor(w, this, d, r);
                if (++open >= 5) return;
            }
        }
        if (a.SelfWatch.Thin("설비")) return; // 계산이 몰리면 급한 것만
        // 생명유지 · 전력 고리의 핵심 설비 고장 (냉각 펌프 말고)
        foreach (var m in w.Ship.Machines)
        {
            if (!m.Spec.Critical || m.Body.Type is FurnitureType.CoolantPump or FurnitureType.MainComputer || m.Faults.Count == 0) continue;
            if (m.Faults.All(x => x.Kind == FaultKind.BreakerTrip || x.OutputFactor >= 0.9f) || m.Body.Room.OffLimits) continue;
            if (For($"설비:{m.Body.Id}") != null || Recent($"설비:{m.Body.Id}", 60f)) continue;
            FixSteps.MakeMachine(w, this, m);
            if (++open >= 5) return;
        }
    }

    /// <summary>5틱마다 (잠근 방만): 닫으라 한 문이 열린 채인 때를 적고, 닫히면 걸린 시간을 이 배의 값으로 잰다.</summary>
    internal void WatchDoors()
    {
        var w = _w;
        if (Off) return;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (!r.Lockdown) continue;
            foreach (var d in r.Doors)
                if (d.Locked && d.Openness >= 0.08f && !d.IsExternal && !d.Removed && !DoorSeen.ContainsKey(d.Id)) DoorSeen[d.Id] = w.Tick;
        }
        if (DoorSeen.Count == 0) return;
        List<int>? gone = null;
        foreach (var (id, seen) in DoorSeen)
        {
            var d = w.Ship.Doors.FirstOrDefault(x => x.Id == id);
            if (d != null && d.Locked && d.Openness >= 0.08f) continue;
            if (d != null && d.Locked && For($"문:{id}") == null) w.Automation.Review.DoorClosed((w.Tick - seen) * 3600f / SimTime.TicksPerHour);
            (gone ??= new()).Add(id);
        }
        if (gone != null) foreach (var id in gone) DoorSeen.Remove(id);
    }

    private bool Recent(string key, float minutes) => Plans.Any(p => p.Key == key && p.Ended >= 0 && _w.Tick - p.Ended < SimTime.Minutes(minutes));

    // ───────────── 고르기 (수순 비교) ─────────────

    /// <summary>수순들을 견주고 하나를 골라 계획으로 세운다.</summary>
    internal FixPlan Choose(string problem, string key, Room? room, int target, string goal, FixCase c, List<FixOption> opts, FixPlan? replacing = null, string why = "")
    {
        var w = _w;
        var a = w.Automation;
        if (Branches != null) foreach (var extra in Branches(w, c)) opts.Add(extra); // v16.25 해법 갈래
        foreach (var o in opts) FixSteps.Score(w, c, o);
        Compares++;
        var allowed = opts.Where(o => o.Allowed).OrderBy(o => o.Score).ThenBy(o => o.Key).ToList();
        var pick = allowed.FirstOrDefault() ?? opts.OrderBy(o => o.Score).First();
        if (a.SelfWatch.Simple && allowed.Count > 0) pick = allowed.OrderBy(o => o.Risk).ThenBy(o => opts.IndexOf(o)).First(); // 분석 기능 이상 — 비교를 떼고 가장 안전한 정해진 순서로
        var second = allowed.FirstOrDefault(o => o != pick);
        var p = new FixPlan
        {
            Id = _next++, Tick = w.Tick, Problem = problem, Key = key, RoomId = room?.Id ?? -1, TargetId = target, Goal = goal, Name = pick.Name, OptionKey = pick.Key,
            Success = pick.Success, Abort = pick.Abort, Needs = pick.Needs, After = pick.After, Min = pick.Min, Max = pick.Max, PeakMin = pick.PeakMin, PeakMax = pick.PeakMax,
            Risk = pick.Risk, MarginC = c.Scram > 0 ? c.Scram - pick.PeakMax : 0f, Fallback = second?.Name ?? "손으로 — 사람에게 넘긴다", FallbackKey = second?.Key ?? "",
            Changed = w.Tick, Replans = replacing != null ? replacing.Replans + 1 : 0,
        };
        p.Compared.AddRange(opts);
        foreach (var act in pick.Steps)
        {
            var (mn, mx) = act.Span(w, c);
            p.Steps.Add(new FixStep { Act = act, Min = mn, Max = mx });
        }
        if (replacing != null)
        {
            replacing.State = "넘김";
            replacing.Ended = w.Tick;
            p.Revisions.AddRange(replacing.Revisions);
            p.Revisions.Add((w.Tick, why));
            Replans++;
        }
        Plans.Add(p);
        Made++;
        string cmp = string.Join(" / ", opts.Select(o => $"{o.Name} {(o.Allowed ? $"({o.Min:0}~{o.Max:0}분{(o.PeakMax > 0 ? $" · 노심 최고 {o.PeakMax:0}℃" : "")})" : $"({o.Blocked})")}"));
        string seq = string.Join(" → ", p.Steps.Select(s => s.Name));
        string reason = second == null ? "다른 수순이 없다" : FixSteps.Why(pick, second);
        a.Book.Add(ActKind.Plan, room, goal, $"수순 {opts.Count}가지를 견줘 봤다: {cmp}", $"{pick.Name}: {seq} — {reason}", pick.Needs, $"plan:{p.Id}", 0, MathF.Max(20f, p.Max + 20f),
            (world, act) => p.State switch
            {
                "성공" => (1, $"맞았다 — {p.Elapsed(world.Tick):0}분 만에 돌아와 버틴다"),
                "중단" => (-1, $"틀렸다 — {p.Revisions.LastOrDefault().why ?? "중단"}"),
                "넘김" => (2, "고쳐 짠 계획으로 넘겼다"),
                _ => ((int, string)?)null,
            });
        string line = a.Manner.Speak(replacing == null ? $"{goal} — {seq}. 예상 {p.Range}" : $"계획을 고칩니다 — {why}. 이제 {seq}");
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {line}");
        if (problem == "냉각" || replacing != null) a.Speak.Announce(a.Voice.Style(line), room, problem == "냉각" ? 2 : 1);
        return p;
    }

    // ───────────── 실행 · 확인 · 고침 ─────────────

    private void Run(FixPlan p)
    {
        var w = _w;
        var a = w.Automation;
        long now = w.Tick;
        // 중단 조건 (냉각): 노심이 긴급 정지 가까이 — 원자로를 낮추는 쪽으로 넘어간다
        if (p.Problem == "냉각" && w.Power.ReactorOnline && w.Power.ReactorTemperature >= FixSteps.ScramC(w) - 10f && p.OptionKey != "low")
        {
            Replan(p, $"노심 {w.Power.ReactorTemperature:0}℃ — 중단 조건에 닿았다 · 원자로를 최저로 낮춘다", "low");
            return;
        }
        for (int guard = 0; guard < 4; guard++)
        {
            var s = p.Step;
            if (s == null) { Finish(p, "성공"); return; }
            if (s.State == FixState.Wait)
            {
                string? why = s.Act.Ready(w, p, s);
                if (s.Act.Kind is FixKind.Remote or FixKind.Test && s.Act.Target >= 0 && w.Ship.Furniture.FirstOrDefault(f => f.Id == s.Act.Target) is Furniture tf
                    && !a.SelfWatch.Allow(CmdTarget.Machine, tf.Id, $"{tf.Name} {s.Name}")) why = "같은 명령이 되풀이돼 멈춰 두었다 — 원인부터 본다";
                if (why != null)
                {
                    if (s.Waiting != why) { s.Waiting = why; p.Changed = now; }
                    if (why.Contains("작업 중")) LockWaits++;
                    return;
                }
                s.Waiting = "";
                s.State = FixState.Run;
                s.Start = now;
                s.Act.Begin(w, p, s);
                Steps++;
                if (s.Act.Kind == FixKind.Remote) RemoteSteps++; else if (s.Act.Kind == FixKind.Hands) HandSteps++;
                p.Changed = now;
            }
            if (s.State == FixState.Run)
            {
                var r = s.Act.Tick(w, p, s);
                if (r == FixState.Run)
                {
                    if (s.Took(now) > s.Max * 1.25f + 2f && s.Late < 2 && s.Act.Late(w, p, s) is string late)
                    {
                        s.Late++;
                        Late++;
                        s.Max = s.Took(now) + (s.Max - s.Min) + 10f;
                        Revise(p, late);
                    }
                    return;
                }
                s.End = now;
                s.State = r;
                p.Changed = now;
                if (r == FixState.Failed)
                {
                    a.Review.StepFailed(p, s);
                    if (s.Act.Kind == FixKind.Test) TestFails++;
                    if (p.FallbackKey != "" && p.Replans < 3) Replan(p, $"{s.Name} — {s.Note}", p.FallbackKey);
                    else Finish(p, "중단", $"{s.Name} — {s.Note}");
                    return;
                }
                p.Cur++;
            }
            else if (s.State is FixState.Done or FixState.Skipped) p.Cur++;
        }
    }

    /// <summary>고친 까닭을 남긴다 (화면 "최근 수정 이유").</summary>
    internal void Revise(FixPlan p, string why)
    {
        var w = _w;
        p.Revisions.Add((w.Tick, why));
        if (p.Revisions.Count > 12) p.Revisions.RemoveAt(0);
        p.Changed = w.Tick;
        Revised++;
        w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: {p.Goal} — {w.Automation.Manner.Speak(why)}");
    }

    /// <summary>지금 상태에서 다시 견준다 (지난 걸음의 효과는 지금 값에 이미 들어 있다).</summary>
    internal void Replan(FixPlan p, string why, string prefer = "")
    {
        var w = _w;
        foreach (var s in p.Steps) if (s.State == FixState.Run) s.Act.Cancel(w, p, s);
        if (p.Problem == "냉각" && w.Ship.Furniture.FirstOrDefault(f => f.Id == p.TargetId) is Furniture f) FixSteps.MakeCooling(w, this, f, p, why, prefer);
        else if (p.Problem == "문" && w.Ship.Doors.FirstOrDefault(d => d.Id == p.TargetId) is Door d && w.Ship.Rooms.FirstOrDefault(r => r.Id == p.RoomId) is Room room) FixSteps.MakeDoor(w, this, d, room, p, why);
        else if (w.Ship.Furniture.FirstOrDefault(x => x.Id == p.TargetId)?.Machine is Machine m) FixSteps.MakeMachine(w, this, m, p, why);
        else Finish(p, "중단", why);
    }

    public void Finish(FixPlan p, string state, string why = "")
    {
        var w = _w;
        var a = w.Automation;
        foreach (var s in p.Steps) if (s.State == FixState.Run) s.Act.Cancel(w, p, s);
        FixSteps.Cleanup(w, p);
        p.State = state;
        p.Ended = w.Tick;
        p.Changed = w.Tick;
        if (state == "성공") Succeeded++; else Aborted++;
        if (why != "") p.Revisions.Add((w.Tick, why));
        a.Review.Plan(p);
        string line = state == "성공"
            ? $"{p.Goal} — 끝났습니다. 기능이 돌아와 버팁니다 ({p.Elapsed(w.Tick):0}분 · 예상 {p.Range})"
            : $"{p.Goal} — 이 계획은 접습니다 ({why}). 사람 판단에 맡깁니다";
        w.Log.Add(w.Tick, state == "성공" ? LogKind.Ship : LogKind.Warning, $"{a.Voice.Call}: {a.Manner.Speak(line)}");
        if (p.Elapsed(w.Tick) >= 20f) w.History.Add(w, state == "성공" ? HistoryKind.Response : HistoryKind.Decision,
            state == "성공" ? $"주 컴퓨터가 짠 순서대로 {Ko.EulReul(p.Goal.Split(" — ")[0])} 되살렸다 — {string.Join(" → ", p.Steps.Where(s => s.State == FixState.Done).Select(s => s.Name))}" : $"주 컴퓨터가 {p.Goal.Split(" — ")[0]} 계획을 접었다 — {why}",
            w.Ship.Rooms.FirstOrDefault(r => r.Id == p.RoomId));
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Made); I(Succeeded); I(Aborted); I(Revised); I(Replans); I(Late); I(Steps); I(DoorDetours); I(TestFails); F(ReactorCap); I(_drive.Count);
        foreach (var p in Plans) { I(p.Id); I(p.Cur); I(p.Steps.Count); I(p.State.Length); }
    }
}

public sealed partial class AutomationSystem
{
    private FixBook? _fix;
    /// <summary>v16.26 여러 단계 복구 계획 · 복구 순서 (ComputerPlan · ComputerPlanSteps).</summary>
    public FixBook Recovery => _fix ??= new FixBook(_world);
    internal FixBook? RecoveryOrNull => _fix;
    /// <summary>Piping 훅: 계획이 정한 펌프 세기.</summary>
    public float PumpDrive(Furniture f) => _fix?.Drive(f) ?? 1f;

    private long _v26Next;
    /// <summary>v16.26 한 틱 (Ship20 뒤): 계획 · 확인 · 예약 · 구획 · 스스로 감시 · 검토 · 성격 셋.</summary>
    private void Brain26()
    {
        if (!Present || FixBook.Off) return;
        Zones.Update(); // 구역 제어기는 빠른 규칙 (끊긴 방만)
        if (_world.Tick < _v26Next) return;
        _v26Next = _world.Tick + 5;
        Recovery.WatchDoors();
        SelfWatch.Update();
        Recovery.Update();
        Probe.Update();
        Reserve.Update();
        Review.Update();
        Manner.Update();
    }

    internal void Hash26(Action<long> I, Action<float> F)
    {
        _fix?.Hash(I, F);
        _probe?.Hash(I, F);
        _reserve?.Hash(I, F);
        _zones?.Hash(I, F);
        _self?.Hash(I, F);
        _review?.Hash(I, F);
        _manner?.Hash(I, F);
    }
}
