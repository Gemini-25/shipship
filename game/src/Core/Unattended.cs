using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// 로봇이 먼저: 사람이 없어도 배가 굴러간다. 손일은 주 컴퓨터 · 로봇이 기본으로 맡고, 로봇 · 컴퓨터가 멎거나 모자라면 사람이 한다.
//  주 컴퓨터(켜져 있으면): 차단기 원격 올림(ComputerTriage) · 보조 발전기 원격 시동.
//  로봇:
//   · 누구든(의료 로봇 빼고): 차단기 올리기 · 보조 발전기 시동 손잡이 · 밸브 잠그기/열기
//   · 운반 계열(운반 · 배식 · 급수 · 적재 · 잡역) + 정비 계열: 보조 발전기 급유 · 냉각수 보충
//   · 정비 계열(정비 · 배선 · 정밀 정비 · 조수 · 잡역): 설비 수리(부품 한 가지까지 · 파손은 빼고) · 관 이음 · 전선 다시 걸기 · 배관 임시 밀봉 ·
//     원자로 재기동(아무도 없을 때 · 주 컴퓨터가 절차를 짚어 줄 때) · 무거운 부품 수리(아무도 없을 때 · 지그로)
//  사람보다 느리고(손이 서툴다) 가끔 헛손질한다. 쓸 만한 로봇이 놀고 있으면 사람은 급한 일(1.0 넘게)이 아니면 로봇에게 미룬다.
//  무인 운항: 배에 손을 쓸 사람이 없으면 주 컴퓨터가 "가서 볼 사람"을 찾지 않는다.
//  (사람이 다 떠난 배: 운석이 냉각 관을 뚫어 냉각수가 빠지자 원자로가 멎었고, 보조 발전기를 켤 손도 급유할 손도 없어 이틀 넘게 정전 · 주 컴퓨터도 꺼졌다)

public sealed partial class AutomationSystem
{
    /// <summary>배에 손을 쓸 수 있는 사람이 없다 — 로봇이 사람 몫의 손일을 맡는다.</summary>
    public bool Unattended { get; private set; }
    public long UnattendedSince { get; private set; } = -1;
    /// <summary>무인 운항 동안 로봇이 사람 대신 한 손일.</summary>
    public int UnattendedFixes { get; internal set; }

    private long _auxTryAt;
    /// <summary>주 컴퓨터가 원격으로 켠 보조 발전기.</summary>
    public int RemoteAuxStarts { get; private set; }

    /// <summary>주 컴퓨터가 손 대신: 보조 발전기 원격 시동 (시동 일감이 나왔고 · 컴퓨터가 켜져 있고 · 아직 아무도 안 붙었으면 — 4분마다, 넷에 셋은 걸린다).
    /// 컴퓨터가 멎었으면 로봇이 · 로봇도 없으면 사람이 손잡이를 당긴다.</summary>
    private void RemoteHands()
    {
        var w = _world;
        if (RobotSystem.HandsOff || !Present || !CoreOnline || w.Tick < _auxTryAt || w.Power.AuxRunning) return;
        var o = w.Board.Open.FirstOrDefault(x => x.Kind == WorkKind.StartAux && x.Assignee == null && x.Robot == null);
        if (o == null || o.Target.Furniture is not Furniture aux || !aux.Room.DataLinked) return; // 발전기 방 데이터선이 끊겼으면 원격으로 못 켠다 — 로봇 · 사람이 손으로
        _auxTryAt = w.Tick + SimTime.Minutes(4);
        if (!w.Rng.Chance(0.75f)) { w.Log.Add(w.Tick, LogKind.Ship, $"{Voice.Call}: 보조 발전기 원격 시동 — 걸리지 않았다 · 4분 뒤 다시"); return; }
        w.Power.StartAux();
        w.Board.Close(o);
        RemoteAuxStarts++;
        w.Log.Add(w.Tick, LogKind.Ship, $"{Voice.Call}: 보조 발전기를 원격으로 켰다 ({o.Detail})");
    }

    /// <summary>무인 운항: 사람이 정해야 하는 배관 결정(본관 잠그기 · 새는 채로 돌리기)을 주 컴퓨터가 대신 정한다 —
    /// 올라온 지 10분 지나도 정할 사람이 없으면. 잠그면 원자로가 서지만 로봇이 때우고 다시 열 수 있다.</summary>
    private void DecideAlone()
    {
        var w = _world;
        if (!Unattended || !Present || !CoreOnline) return;
        foreach (var o in w.Board.Open.Where(x => x.Kind is WorkKind.IsolateMain or WorkKind.LimpMain && x.Assignee == null && w.Tick - x.Posted >= SimTime.Minutes(10)).ToList())
        {
            if (o.Target.Pipe is not PipeSegment s) continue;
            if (o.Kind == WorkKind.IsolateMain) s.IsolateApproved = true; else s.LimpApproved = true;
            w.Board.Close(o);
            w.Board.RequestScan();
            UnattendedFixes++;
            string what = o.Kind == WorkKind.IsolateMain ? $"{s.Name} 밸브를 잠그고 고친다 (원자로가 선다 — 냉각수가 다 새는 것보다 낫다)" : $"{s.Name}을 새는 채로 열어 냉각수를 부어 가며 돌린다";
            w.Log.Add(w.Tick, LogKind.Ship, $"{Voice.Call}: 정할 사람이 없다 — {what}");
            w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터가 혼자 정했다: {what}", s.ValveRoom);
        }
    }

    private void TrackUnattended()
    {
        var w = _world;
        bool none = !w.Crew.Any(c => c.CanAct && !c.IsChild);
        if (none == Unattended) return;
        Unattended = none;
        if (none)
        {
            UnattendedSince = w.Tick;
            UnattendedFixes = 0;
            w.Log.Add(w.Tick, LogKind.Ship, $"{Voice.Call}: 배에 손을 쓸 사람이 없다 — 무인 운항 · 수리 · 차단기 · 보조 발전기는 로봇이 맡는다");
        }
        else
        {
            float h = (w.Tick - UnattendedSince) / (float)SimTime.TicksPerHour;
            w.Log.Add(w.Tick, LogKind.Ship, $"{Voice.Call}: 사람이 돌아왔다 — 무인 운항을 푼다 ({h:0.#}시간 · 로봇이 손쓴 일 {UnattendedFixes})");
            if (h >= 1f) w.History.Add(w, HistoryKind.Response, $"사람 없이 {h:0.#}시간 — 주 컴퓨터와 로봇이 배를 지켰다 (손쓴 일 {UnattendedFixes})", null);
            UnattendedSince = -1;
        }
        w.Board.RequestScan();
    }
}

public sealed partial class RobotSystem
{
    /// <summary>시험: 로봇 손일 · 주 컴퓨터 원격 시동을 끈다 (사람의 위기 행동만 견주는 측정).</summary>
    public static bool HandsOff;
    /// <summary>로봇이 사람 몫의 손일을 해낸 수.</summary>
    public int HandsDone { get; private set; }

    public static bool Fixer(RobotKind k) => k is RobotKind.Maintainer or RobotKind.Lineman or RobotKind.Overhauler or RobotKind.Assistant or RobotKind.Utility;
    public static bool Carrier(RobotKind k) => k is RobotKind.Hauler or RobotKind.Courier or RobotKind.Tanker or RobotKind.Stocker or RobotKind.Utility;
    /// <summary>로봇이 맡을 수 있는 사람 몫의 손일 (종류만).</summary>
    public static bool HandWork(WorkKind k) => k is WorkKind.ResetBreaker or WorkKind.StartAux or WorkKind.CloseValve or WorkKind.OpenValve or WorkKind.Refuel or WorkKind.RefillCoolant
        or WorkKind.Repair or WorkKind.Reline or WorkKind.Rewire or WorkKind.PatchPipe or WorkKind.RestartReactor or WorkKind.StockDock or WorkKind.Fabricate;

    /// <summary>이 로봇이 사람 몫의 손일을 맡을 수 있나.</summary>
    private bool Stands(Robot r, WorkOrder o)
    {
        var w = _world;
        if (HandsOff || !HandWork(o.Kind) || r.Kind is RobotKind.Stretcher or RobotKind.Nurse) return false;
        if (o.Kind is WorkKind.ResetBreaker or WorkKind.StartAux or WorkKind.CloseValve or WorkKind.OpenValve) return true;
        if (o.Kind == WorkKind.StockDock) return w.Automation.Unattended; // 운반 로봇이 없는 작은 배: 드론이 외벽을 막을 금속판을 거치대에 (아무 로봇이나 나른다)
        if (o.Kind is WorkKind.Refuel or WorkKind.RefillCoolant) return Carrier(r.Kind) || Fixer(r.Kind);
        // 무인 운항: 정비 로봇이 모자라면 다른 로봇도 주 컴퓨터 안내로 가벼운 수리를 (무거운 부품 없이 · 느리고 자주 헛짚는다)
        if (!Fixer(r.Kind)) return o.Kind == WorkKind.Repair && w.Automation.Unattended && RepairOk(o) && o.Target.Furniture?.Machine?.Faults.FirstOrDefault(x => x.Kind == o.Fault && x.Circuit == o.Circuit) is Fault lf && !lf.Materials.Any(x => PartsSystem.Heavy(x.kind));
        return o.Kind switch
        {
            WorkKind.Reline or WorkKind.Rewire or WorkKind.PatchPipe => true,
            WorkKind.RestartReactor => w.Automation.CoreOnline && w.Automation.Unattended, // 노심을 다루는 일 — 사람 기관사가 있으면 사람이 (로봇은 느리고 자주 헛짚는다) · 아무도 없을 때 주 컴퓨터가 절차를 짚어 주면
            WorkKind.Repair => RepairOk(o),
            WorkKind.Fabricate => w.Automation.Unattended && FabOk(o) is not null, // 무인 운항: 작업대에서 간단한 부품을 (주 컴퓨터가 도면을 짚어 준다)
            _ => false,
        };
    }

    /// <summary>로봇이 작업대 · 정제기에서 만들 수 있는 것: 숙련이 필요 없는 것 (재료를 넣고 주 컴퓨터가 짚어 주는 대로).</summary>
    private static Recipe? FabOk(WorkOrder o) =>
        o.Product is ItemKind p && o.Target.Furniture is Furniture st && Recipes.For(p, st.Type == FurnitureType.Refinery ? Station.Refinery : Station.Workbench) is Recipe r && r.MinSkill <= 0f ? r : null;

    /// <summary>로봇이 혼자 할 수 있는 수리: 파손 아님 · 부품 한 가지까지 · 무거운 부품(모터 · 펌프 · 제어부)은 둘이 잡고 다는 일이라 사람이 있으면 사람 짝에게 (아무도 없을 때만 로봇이 지그로).</summary>
    private bool RepairOk(WorkOrder o) =>
        o.Target.Furniture?.Machine?.Faults.FirstOrDefault(x => x.Kind == o.Fault && x.Circuit == o.Circuit) is Fault f
        && f.Kind != FaultKind.Wrecked && f.Materials.Select(x => x.kind).Distinct().Count() <= 1
        && (_world.Automation.Unattended || !f.Materials.Any(x => PartsSystem.Heavy(x.kind)));

    private long _freeTick = -1;
    private readonly HashSet<WorkKind> _freeKinds = new();
    /// <summary>이 손일을 맡을 수 있는 로봇이 놀고 있다 (충전대에서 기다리거나 · 하는 일 없이) — 사람은 급하지 않으면 미룬다.</summary>
    public bool HandsFree(WorkOrder o)
    {
        var w = _world;
        if (HandsOff || !HandWork(o.Kind)) return false;
        if (_freeTick != w.Tick)
        {
            _freeTick = w.Tick;
            _freeKinds.Clear();
            foreach (var r in Robots)
            {
                if (!r.Operational || r.State is not (RobotState.Docked or RobotState.Active) || r.Order != null || r.Steps != null && r.State == RobotState.Active || r.Battery < 0.4f) continue;
                if (r.Kind is RobotKind.Stretcher or RobotKind.Nurse) continue;
                _freeKinds.Add(WorkKind.ResetBreaker); _freeKinds.Add(WorkKind.StartAux); _freeKinds.Add(WorkKind.CloseValve); _freeKinds.Add(WorkKind.OpenValve);
                if (Carrier(r.Kind) || Fixer(r.Kind)) { _freeKinds.Add(WorkKind.Refuel); _freeKinds.Add(WorkKind.RefillCoolant); }
                if (Fixer(r.Kind)) { _freeKinds.Add(WorkKind.Repair); _freeKinds.Add(WorkKind.Reline); _freeKinds.Add(WorkKind.Rewire); _freeKinds.Add(WorkKind.PatchPipe); if (w.Automation.CoreOnline && w.Automation.Unattended) _freeKinds.Add(WorkKind.RestartReactor); }
            }
        }
        return _freeKinds.Contains(o.Kind) && (o.Kind != WorkKind.Repair || RepairOk(o));
    }

    /// <summary>사람 몫의 손일 단계 (사람보다 느리다).</summary>
    private List<RobotStep>? PlanHands(Robot r, WorkOrder o, Cell at, DistanceField dist, out string? blocked)
    {
        blocked = null;
        var w = _world;
        var steps = new List<RobotStep>();
        void Hand(string what) { if (w.Automation.Unattended) w.Automation.UnattendedFixes++; HandsDone++; Done(r, what); }
        if (o.Target.Pipe is PipeSegment s)
        {
            switch (o.Kind)
            {
                case WorkKind.CloseValve or WorkKind.OpenValve:
                {
                    bool close = o.Kind == WorkKind.CloseValve;
                    steps.Add(new RGoto(at));
                    steps.Add(new RWork(close ? 0.3f : 0.25f, null, s.ValveCell.Center));
                    steps.Add(new RDo((rb, world) =>
                    {
                        if (o.Closed) return true;
                        if (close) world.Piping.CloseValve(s, null); else world.Piping.OpenValve(s, null);
                        world.Board.Close(o);
                        Hand($"{s.Name} 밸브를 {(close ? "잠갔다" : "열었다")}");
                        return true;
                    }));
                    return steps;
                }
                case WorkKind.PatchPipe:
                {
                    var (box, spot) = Nearest(w, dist, b => b.Storage!.Count(ItemKind.Sealant) > 0);
                    if (box == null) { blocked = "실링폼 없음"; return null; }
                    steps.Add(new RGoto(spot));
                    steps.Add(new RTake(box, ItemKind.Sealant, 1));
                    steps.Add(new RGoto(at));
                    steps.Add(new RWork(0.6f, o, s.LeakAt.Center));
                    steps.Add(new RDo((rb, world) =>
                    {
                        if (s.Sound) { world.Board.Close(o); return true; }
                        if (rb.Cargo?.Kind != ItemKind.Sealant) return false;
                        rb.Cargo = rb.Cargo.Value.Count > 1 ? new ItemStack(ItemKind.Sealant, rb.Cargo.Value.Count - 1) : null;
                        world.Piping.Patch(s, 0.4f, rb.Name);
                        world.Board.Close(o);
                        Hand($"{Ko.EulReul(s.Name)} 실링폼 클램프로 임시로 막았다");
                        return true;
                    }));
                    return steps;
                }
            }
            return null;
        }
        if (o.Kind == WorkKind.RefillCoolant)
        {
            var net = w.Piping;
            if (w.Water.Level < 5f) { blocked = "정수 탱크가 비었다"; return null; }
            if (!net.FeedLine) { blocked = "보충관이 끊겼다 — 물통으로 나르는 건 사람 손"; return null; }
            steps.Add(new RGoto(at));
            steps.Add(new RWork(0.4f, null, net.FillPort.Center));
            steps.Add(new RDo((rb, world) =>
            {
                float added = world.Piping.Refill(30f);
                world.Board.Close(o);
                Hand($"냉각수를 {added:0}L 보충했다 ({world.Piping.CoolantFraction * 100:0}%)");
                return true;
            }));
            return steps;
        }
        var f = o.Target.Furniture;
        if (f == null) return null;
        switch (o.Kind)
        {
            case WorkKind.ResetBreaker:
            {
                var m = f.Machine!;
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.35f, null, f.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed) return true;
                    if (world.Automation.TriageOrNull?.Holding(o.Circuit) is string hold) { world.Board.Close(o); Done(rb, $"{PowerGrid.CircuitName(o.Circuit)} 회로 차단기는 그대로 둔다 — {hold}"); return true; }
                    m.Faults.RemoveAll(x => x.Kind == FaultKind.BreakerTrip && x.Circuit == o.Circuit);
                    world.Board.Close(o);
                    Hand($"{PowerGrid.CircuitName(o.Circuit)} 회로 차단기를 올렸다");
                    return true;
                }));
                return steps;
            }
            case WorkKind.StartAux:
            {
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.45f, null, f.Center) { CanContinue = (_, world) => !world.Power.AuxRunning });
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed || world.Power.AuxRunning) { world.Board.Close(o); return true; }
                    if (world.Rng.Chance(0.25f)) { world.Log.Add(world.Tick, LogKind.Work, $"{rb.Name}: 보조 발전기 시동이 걸리지 않았다 — 다시 당긴다"); return false; }
                    world.Power.StartAux();
                    world.Board.Close(o);
                    Hand("보조 발전기 시동 손잡이를 당겼다");
                    return true;
                }));
                return steps;
            }
            case WorkKind.RestartReactor:
            {
                steps.Add(new RGoto(at));
                steps.Add(new RWork(1.1f, null, f.Center) { CanContinue = (_, world) => !world.Power.ReactorOnline && world.Power.CoolingCapacity >= PowerGrid.RestartCoolingKw && world.Automation.CoreOnline });
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed || world.Power.ReactorOnline) { world.Board.Close(o); return true; }
                    float margin = Math.Clamp((world.Power.CoolingCapacity - PowerGrid.RestartCoolingKw) / 16f, 0f, 1f);
                    if (!world.Rng.Chance(0.55f + 0.25f * margin)) // 사람 기관사보다 서툴다 — 주 컴퓨터가 짚어 주는 대로만
                    {
                        var core = f.Machine!;
                        core.Wear = MathF.Min(1f, core.Wear + 0.08f);
                        world.Board.Block(o, null, 0.5f);
                        world.Log.Add(world.Tick, LogKind.Warning, $"{rb.Name}: 원자로 재기동 실패 — 노심 반응이 안정되지 않았다 (주 컴퓨터가 30분 뒤 다시)");
                        return true;
                    }
                    world.Power.RestartReactor();
                    world.Board.Close(o);
                    Hand("주 컴퓨터가 짚어 주는 대로 원자로를 다시 켰다");
                    world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(rb.Name)} 주 컴퓨터의 절차대로 원자로를 다시 켰다", f.Room);
                    return true;
                }));
                return steps;
            }
            case WorkKind.Reline or WorkKind.Rewire:
            {
                var m = f.Machine!;
                bool line = o.Kind == WorkKind.Reline;
                ItemKind? need = line ? (w.Ship.CountStored(ItemKind.Sealant) > 0 ? ItemKind.Sealant : w.Ship.CountStored(ItemKind.Plate) > 0 ? ItemKind.Plate : null)
                                      : w.Ship.CountStored(ItemKind.Cable) > 0 ? ItemKind.Cable : null;
                if (line && need == null) { blocked = "실링폼·금속판 없음"; return null; }
                if (need is ItemKind item)
                {
                    var (box, spot) = Nearest(w, dist, b => b.Storage!.Count(item) > 0);
                    if (box == null) { if (line) { blocked = "실링폼·금속판 없음"; return null; } need = null; }
                    else { steps.Add(new RGoto(spot)); steps.Add(new RTake(box, item, 1)); }
                }
                steps.Add(new RGoto(at));
                steps.Add(new RWork(line ? 0.6f : need != null ? 0.75f : 0.3f, o, f.Center));
                ItemKind? used = need;
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed) return true;
                    if (used is ItemKind it) { if (rb.Cargo?.Kind != it) return false; rb.Cargo = rb.Cargo.Value.Count > 1 ? new ItemStack(it, rb.Cargo.Value.Count - 1) : null; }
                    world.Board.Close(o);
                    if (line) { m.Line = 1f; world.Procs.Relined++; }
                    else if (used != null) { m.Feed = 1f; m.Spliced = false; world.Procs.Rewired++; }
                    else { m.Feed = 0.65f; m.Spliced = true; world.Procs.Spliced++; }
                    MarkLog.Add(m.Marks, world.Tick, $"{rb.Name}: {(line ? "관 이음을 다시 이었다" : used != null ? "전선을 새로 걸었다" : "전선을 임시로 이어 붙였다")}");
                    Hand($"{m.Name} {(line ? "관 이음을 다시 이었다" : used != null ? "전선을 새로 걸었다" : "전선을 임시로 이어 붙였다")}");
                    return true;
                }));
                return steps;
            }
            case WorkKind.Refuel:
            {
                var (box, spot) = Nearest(w, dist, b => b.Storage!.Count(ItemKind.Fuel) > 0);
                if (box == null) { blocked = "연료통 없음"; return null; }
                steps.Add(new RGoto(spot));
                steps.Add(new RTake(box, ItemKind.Fuel, 1));
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.35f, null, f.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    if (rb.Cargo?.Kind != ItemKind.Fuel) return false;
                    rb.Cargo = rb.Cargo.Value.Count > 1 ? new ItemStack(ItemKind.Fuel, rb.Cargo.Value.Count - 1) : null;
                    world.Power.AddFuel(PowerGrid.FuelCanHours);
                    world.Adapt.Refuels++;
                    world.Board.Close(o);
                    Hand($"보조 발전기에 연료통을 부었다 (연료 {world.Power.AuxFuel:0}시간분)");
                    return true;
                }));
                return steps;
            }
            case WorkKind.Fabricate:
            {
                if (FabOk(o) is not Recipe rc) { blocked = "로봇이 만들 수 없는 것"; return null; }
                if (f.Machine!.Efficiency <= 0f) { blocked = f.Machine.Powered ? $"{f.Name} 멈춤" : $"{f.Name}에 전기가 없다"; return null; }
                if (!rc.Inputs.All(x => w.Ship.CountStored(x.kind) >= x.count)) { blocked = "재료 부족"; return null; }
                var (main, mainN) = rc.Inputs[0];
                var (box, spot) = Nearest(w, dist, b => b.Storage!.Count(main) >= mainN);
                if (box == null) { blocked = $"{ItemKinds.Name(main)} 없음"; return null; }
                var product = rc.Product;
                steps.Add(new RGoto(spot));
                steps.Add(new RTake(box, main, mainN));
                steps.Add(new RGoto(at));
                steps.Add(new RWork(rc.Hours * 1.5f / MathF.Max(0.4f, f.Machine.Efficiency), o, f.Center)); // 사람보다 느리다
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed) return true;
                    if (rb.Cargo is not ItemStack held || held.Kind != main || held.Count < mainN) return false;
                    // 나머지 재료는 작업대 곁 선반에서 (부품 준비실 · 작업대 서랍) — 없으면 들고 온 것을 도로 들고 간다
                    if (!rc.Inputs.Skip(1).All(x => world.Ship.CountStored(x.kind) >= x.count)) { o.BlockedUntil = world.Tick + SimTime.Minutes(30); o.BlockedReason = "재료 부족"; return true; }
                    foreach (var (k, n) in rc.Inputs.Skip(1))
                    {
                        int left = n;
                        foreach (var c in world.Ship.Containers.Where(c => !c.Room.Detached && c.Type != FurnitureType.DroneDock).OrderBy(c => (c.Center - f.Center).LengthSquared()))
                        {
                            if (left <= 0) break;
                            left -= c.Storage!.Take(k, left);
                        }
                    }
                    rb.Cargo = new ItemStack(product, rc.Yield);
                    world.Adapt.PartsMade += rc.Yield;
                    world.Board.Close(o);
                    Hand($"{(f.Type == FurnitureType.Refinery ? "정제기" : "작업대")}에서 {ItemKinds.Name(product)} {rc.Yield}개를 만들었다 ({string.Join(" · ", rc.Inputs.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}"))})");
                    return true;
                }));
                return steps;
            }
            case WorkKind.Repair:
            {
                var m = f.Machine!;
                var fault = m.Faults.FirstOrDefault(x => x.Kind == o.Fault && x.Circuit == o.Circuit);
                if (fault == null) { w.Board.Close(o); return null; }
                if (fault.Kind == FaultKind.Overheat && f.Room.Air.Temperature > AutomationSystem.RestartC) { blocked = "방이 식어야 한다"; return null; }
                var mats = fault.Materials;
                if (mats.Length > 0)
                {
                    var (kind, count) = (mats[0].kind, mats.Where(x => x.kind == mats[0].kind).Sum(x => x.count));
                    var (box, spot) = Nearest(w, dist, b => b.Storage!.Count(kind) >= count);
                    if (box == null) { blocked = $"{ItemKinds.Name(kind)} 없음"; return null; }
                    steps.Add(new RGoto(spot));
                    steps.Add(new RTake(box, kind, count));
                }
                steps.Add(new RGoto(at));
                bool trained = Fixer(r.Kind);
                steps.Add(new RWork(fault.Spec.RepairHours * (1f - 0.25f * fault.Stage) * (trained ? 1.4f : 2.2f), o, f.Center)); // 사람보다 느리다 · 수리 로봇이 아니면 더
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed || !m.Faults.Contains(fault)) { world.Board.Close(o); return true; }
                    if (mats.Length > 0)
                    {
                        int need = mats.Sum(x => x.count);
                        if (rb.Cargo is not ItemStack held || held.Kind != mats[0].kind || held.Count < need) return false;
                        rb.Cargo = held.Count > need ? new ItemStack(held.Kind, held.Count - need) : null;
                    }
                    if (world.Rng.Chance(trained ? 0.12f : 0.25f)) { o.Progress = 0f; Done(rb, $"{m.Name} 수리를 헛짚었다 — 처음부터 다시"); return true; }
                    m.Faults.Remove(fault);
                    m.Condition = MathF.Min(1f, m.Condition + 0.01f);
                    m.Wear = MathF.Min(m.Wear, 0.35f);
                    world.Board.Close(o);
                    MarkLog.Add(m.Marks, world.Tick, $"{rb.Name}: {fault.Spec.Name} 수리");
                    Hand($"{m.Name}의 {Ko.EulReul(fault.Spec.Name)} 고쳤다");
                    return true;
                }));
                steps.Add(new RTest(m, "fix:hands")); // 고친 뒤 시험 가동
                return steps;
            }
        }
        return null;
    }
}

/// <summary>시험 · 화면: 승무원이 모두 배를 떠난다 (하던 일은 내려놓고 · 들고 있던 것은 선반에) — 주 컴퓨터 · 로봇 · 드론만 남는다.</summary>
public static class Unmanned
{
    public static int Leave(World w, string why = "모두 배를 비웠다")
    {
        int n = 0;
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.Away).ToList())
        {
            c.EndJob(w, ToilStatus.Interrupted);
            if (c.Carrying is ItemStack held) { VoyageV15.Put(w, held.Kind, held.Count); c.Carrying = null; }
            w.Values.Depart(c, why);
            n++;
        }
        return n;
    }
}
