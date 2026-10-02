using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.20b 외부 드론의 두뇌 (Fleet.cs가 매 분 부른다): 주 컴퓨터가 맡긴 외벽 파공을 밖에서 막는다(실링폼 · 금속판 — 잘 통한 쪽) ·
//  운석 경보면 파공 곁 선체 그늘에 숨었다가 다시 붙는다 · 배터리가 돌아올 만큼만 남으면 쉬던 드론과 교대(한 만큼 이어서) ·
//  견인 드론이 없으면 다른 드론이 떠내려가는 드론을 건져 온다.

public sealed partial class DroneSystem
{
    public static bool CanSeal(DroneKind k) => RobotsV15.Base(k) is DroneKind.Repair or DroneKind.Build;

    internal float FleetReturnCost(Drone d) => ReturnCost(d);

    /// <summary>시험용: 드론을 선체 밖에 멈춰 떠다니게 한다.</summary>
    public void ForceStrand(Drone d, string why) => Strand(d, why);

    private WorkOrder? FindOrder(int id)
    {
        foreach (var o in _world.Board.All) if (o.Id == id) return o;
        return null;
    }

    /// <summary>파공 바깥 자리 (벽 칸 곁의 우주 칸).</summary>
    internal Vector2? WallSpot(WorkOrder o) => WallSpot(o.Target.Cell);

    internal Vector2? WallSpot(Cell c)
    {
        var grid = _world.Ship.Grid;
        foreach (var d in Cell.Dirs4) { var n = c + d; if (grid.InBounds(n) && grid.Kind(n) == TileKind.Void) return n.Center; }
        foreach (var d in Cell.Dirs8) { var n = c + d; if (grid.InBounds(n) && grid.Kind(n) == TileKind.Void) return n.Center; }
        return null;
    }

    /// <summary>싣고 갈 것: 실링폼(빠르다) · 금속판 두 장(오래간다) — 둘 다 있으면 잘 통한 쪽.</summary>
    private (ItemKind kind, int count)[]? SealKit(Drone d, WallState wall)
    {
        int s = Hull.SealantFor(wall);
        bool hs = d.Dock.Storage!.Count(ItemKind.Sealant) >= s, hp = d.Dock.Storage.Count(ItemKind.Plate) >= 2;
        if (hs && hp) return _world.Fleet.Rate("seal:sealant") + 0.1f >= _world.Fleet.Rate("seal:plate") ? new[] { (ItemKind.Sealant, s) } : new[] { (ItemKind.Plate, 2) };
        return hs ? new[] { (ItemKind.Sealant, s) } : hp ? new[] { (ItemKind.Plate, 2) } : null;
    }

    private float SealHours(Drone d) => WorkHours(WorkKind.SealBreach) * RobotsV15.Work(d.Kind) * _world.Fleet.Work;

    /// <summary>이 파공에 보낼 드론 (가장 빨리 닿는 · 배터리 · 자재).</summary>
    internal (Drone? d, float eta, string why) FleetSealer(WorkOrder o, Vector2 at)
    {
        var w = _world;
        if (w.Ship.WallAt(o.Target.Cell) is not WallState wall) return (null, 0f, "벽이 없다");
        string why = "밖에 나갈 수 있는 수리 · 건설 드론이 없다";
        Drone? best = null;
        float bestEta = float.MaxValue;
        foreach (var d in Drones)
        {
            if (!CanSeal(d.Kind) || !d.Operational || d.State != DroneState.Docked || w.Fleet.DroneTask.ContainsKey(d.Id) || d.Hurt.Swell > 0f) continue;
            if (SealKit(d, wall) == null) { why = "거치대에 실링폼도 금속판도 없다"; continue; }
            float hours = SealHours(d);
            float cost = TripCost(d, at, hours);
            if (cost > 0.97f) { why = "한 번 충전으로 다녀올 수 없는 자리"; continue; }
            float wait = d.Battery >= cost ? 0f : (cost - d.Battery) / ChargePerHour * 60f;
            if (wait > 20f || !DockWorking(d) && d.Battery < cost) { why = $"배터리가 모자라다 ({d.Name} {d.Battery * 100:0}% · 필요 {cost * 100:0}%)"; continue; }
            float eta = wait + FlightLength(d, d.DockPosition, at) / (Speed(d.Kind) * w.Fleet.DroneSpeed * SimTime.TicksPerHour / 60f) + hours * 60f + 1f;
            if (eta < bestEta - 0.01f) { bestEta = eta; best = d; }
        }
        return (best, best != null ? bestEta : 0f, why);
    }

    /// <summary>거치대의 드론: 주 컴퓨터가 맡긴 일이 먼저 (파공 · 건지기).</summary>
    private bool FleetDecide(Drone d)
    {
        var w = _world;
        var f = w.Fleet;
        if (FleetSystem.Off) return false;
        if (f.DroneTask.TryGetValue(d.Id, out int oid))
        {
            var o = FindOrder(oid);
            if (o == null || o.Closed || o.Assignee != null || o.Drone != null && o.Drone != d || w.Ship.WallAt(o.Target.Cell) is not WallState wall || WallSpot(o) is not Vector2 at)
            {
                f.DroneTask.Remove(d.Id);
                if (o != null && o.Drone == d) o.Drone = null;
                return false;
            }
            o.Drone = d;
            if (w.Sensors.Alarm is IncomingMeteor m && m.MinutesLeft(w.Tick) < 6f)
            {
                d.Doing = $"운석이 지나가길 기다린다 — {o.Target.Room!.Name} 파공으로 나갈 차례";
                return true;
            }
            var kit = SealKit(d, wall);
            float cost = TripCost(d, at, SealHours(d));
            float wait = d.Battery >= cost ? 0f : (cost - d.Battery) / ChargePerHour * 60f;
            if (kit == null || wait > 25f)
            {
                f.DroneTask.Remove(d.Id);
                o.Drone = null;
                d.Mind.Say(kit == null ? "실링폼도 금속판도 없어 사람에게 넘긴다" : "충전이 늦어 사람에게 넘긴다", w.Tick);
                f.CloseLine(CmdTarget.Drone, d.Id, kit == null ? "자재가 없다" : "충전이 늦다");
                return false;
            }
            if (d.Battery < cost) { d.Doing = $"충전 {d.Battery * 100:0}% — 파공까지 {cost * 100:0}% 필요"; return true; }
            foreach (var (k, n) in kit) d.Dock.Storage!.Take(k, n);
            d.Cargo = kit;
            d.Order = o;
            Launch(d, at, $"{o.Target.Room!.Name} 파공 — 밖에서 막는다");
            return true;
        }
        if (FleetHullCare(d)) return true;
        if (f.FetchTask.TryGetValue(d.Id, out int lid))
        {
            f.FetchTask.Remove(d.Id);
            var lost = lid >= 0 && lid < Drones.Count ? Drones[lid] : null;
            if (lost != null && lost.State == DroneState.Adrift && !Drones.Any(x => x.Fetching == lost) && d.Battery >= 0.7f)
            {
                d.Fetching = lost;
                Launch(d, lost.Position, $"떠내려가는 {Ko.EulReul(lost.Name)} 건지러");
                f.SaidFetch(lost, d);
                return true;
            }
        }
        return false;
    }

    /// <summary>평시 선외 수리: 쉬는 수리 · 건설 드론이 사람이 아직 손대지 않은 외벽 수리(용접)를 밖에서 맡는다 —
    ///  사람은 우주복도 사다리도 필요 없고, 다음 운석 전에 외벽이 단단해진다. 조용할 때만 (운석 경보 · 파편 · 비상이면 안 나간다).</summary>
    private bool FleetHullCare(Drone d)
    {
        var w = _world;
        var f = w.Fleet;
        if (!CanSeal(d.Kind) || d.Battery < 0.9f || f.Mode == "비상" || w.Sensors.Alarm != null || w.Hazards.Shower.Count > 0 || w.Hazards.StormActive) return false;
        if (w.Tick < f.NextHullCare) return false;
        f.NextHullCare = w.Tick + SimTime.Minutes(2); // 작업 목록은 2분에 한 번만 훑는다
        if (!(w.Automation.Present && (w.Automation.MainOnline || w.Automation.Core.BackupCore)) || !w.Automation.DroneControl) return false;
        if (d.Dock.Storage!.Count(ItemKind.Plate) < 1) return false;
        WorkOrder? best = null;
        Vector2 at = default;
        foreach (var o in w.Board.All)
        {
            if (o.Kind != WorkKind.RepairHull || o.Closed || o.Assignee != null || o.Drone != null || o.Target.Kind != TargetKind.Wall) continue;
            if (w.Tick - o.Posted < SimTime.Minutes(20)) continue; // 사람이 먼저 맡을 틈
            if (WallSpot(o) is not Vector2 spot || TripCost(d, spot, WorkHours(WorkKind.RepairHull) * _world.Fleet.Work) > 0.8f) continue;
            if (best == null || o.Urgency > best.Urgency || o.Urgency == best.Urgency && o.Id < best.Id) { best = o; at = spot; }
        }
        if (best == null) return FleetHullRound(d);
        d.Dock.Storage.Take(ItemKind.Plate, 1);
        d.Cargo = new[] { (ItemKind.Plate, 1) };
        d.Order = best;
        best.Drone = d;
        Launch(d, at, $"{best.Target.Label} — 밖에서 용접");
        d.Mind.Say($"조용한 틈에 {Ko.EulReul(best.Target.Label)} 밖에서 용접한다 — 사람이 우주복을 입지 않아도 된다", w.Tick);
        f.Line(CmdTarget.Drone, d.Id, best.Target.Room, $"{d.Name}: {best.Target.Label} 밖에서 용접", "평시 선외 수리 · 다음 운석 전에", 0.4f, 90f, -1, best.Id);
        f.HullJobs++;
        return true;
    }

    /// <summary>외벽 순찰: 아직 작업 목록에 오르지 않은 약해진 벽(운석 · 열 · 피로)을 밖에서 미리 용접한다 — 다음 충격에 뚫리지 않게.
    ///  자주 맞는 방 쪽을 먼저 (배운 것) · 몇 시간에 한 번만 찾아본다.</summary>
    private bool FleetHullRound(Drone d)
    {
        var w = _world;
        var f = w.Fleet;
        if (w.Tick < f.NextHullRound || d.Battery < 0.95f) return false;
        Cell? pick = null;
        WallState? pw = null;
        float bestScore = 0f;
        Vector2 at = default;
        foreach (var (cell, wall) in w.Ship.Walls)
        {
            if (!wall.IsHull || wall.FrameLost || wall.Breach > 0f || wall.Patched || !Hull.WorthWelding(wall) || wall.Integrity > wall.MaxIntegrity * 0.85f) continue;
            var room = Hull.InsideRoom(w.Ship, cell);
            if (room == null || room.Detached || room.Jettison != null) continue;
            float score = (1f - wall.Integrity / MathF.Max(0.01f, wall.MaxIntegrity)) + 0.05f * (f.RoomFaults.GetValueOrDefault(room.Id) + (f.Hits.TryGetValue(room.Id, out int h) ? h : 0));
            if (score <= bestScore + 0.0001f) continue;
            if (Drones.Any(x => x.HullCare == cell)) continue;
            if (WallSpot(cell) is not Vector2 spot || TripCost(d, spot, WorkHours(WorkKind.RepairHull)) > 0.6f) continue;
            bool taken = false;
            foreach (var o in w.Board.All) if (!o.Closed && o.Target.Kind == TargetKind.Wall && o.Target.Cell == cell) { taken = true; break; }
            if (taken) continue;
            bestScore = score; pick = cell; pw = wall; at = spot;
        }
        if (pick is not Cell c || pw == null)
        {
            f.NextHullRound = w.Tick + SimTime.Hours(3);
            return false;
        }
        f.NextHullRound = w.Tick + SimTime.Minutes(30);
        d.Dock.Storage!.Take(ItemKind.Plate, 1);
        d.Cargo = new[] { (ItemKind.Plate, 1) };
        d.HullCare = c;
        var rm = Hull.InsideRoom(w.Ship, c);
        Launch(d, at, $"외벽 순찰 — {rm?.Name ?? "?"} 외벽 강도 {pw.Integrity * 100:0}%");
        d.Mind.Say($"{rm?.Name ?? "?"} 외벽이 {pw.Integrity * 100:0}%로 약해졌다 — 다음 충격 전에 밖에서 덧댄다", w.Tick);
        f.Line(CmdTarget.Drone, d.Id, rm, $"{d.Name}: {rm?.Name ?? "?"} 외벽 순찰 · 용접", $"강도 {pw.Integrity * 100:0}% · 조용한 틈에", 0.3f, 90f);
        f.HullRounds++;
        return true;
    }

    /// <summary>순찰 중 약한 벽에 닿아 용접을 마쳤다.</summary>
    private bool FleetRoundWeld(Drone d)
    {
        var w = _world;
        if (d.HullCare is not Cell c) return false;
        d.HullCare = null;
        var plate = d.Cargo.Any(x => x.kind == ItemKind.Plate);
        d.Cargo = Array.Empty<(ItemKind, int)>();
        if (!plate || w.Ship.WallAt(c) is not WallState wall || wall.Breach > 0f) { GoHome(d); return true; }
        wall.MaxIntegrity = MathF.Max(0.25f, wall.MaxIntegrity - Hull.WeldFatigue(0.5f));
        wall.Integrity = MathF.Max(wall.Integrity, wall.MaxIntegrity * (0.86f + 0.02f * w.Fleet.Tier));
        wall.Welds++;
        wall.TotalWelds++;
        JobsDone++;
        var room = Hull.InsideRoom(w.Ship, c);
        MarkLog.Add(wall.Marks, w.Tick, $"{d.Name}: 순찰 중 밖에서 덧대 용접 (강도 {wall.Integrity * 100:0}%)");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(d.Name)} {room?.Name ?? "?"} 외벽을 밖에서 덧대 용접했다 (강도 {wall.Integrity * 100:0}%)");
        w.Fleet.Method("weld:round", true);
        w.Fleet.CloseLine(CmdTarget.Drone, d.Id, "덧댔다");
        d.Mind.Say($"{room?.Name ?? "?"} 외벽을 덧댔다 — 이제 강도 {wall.Integrity * 100:0}%", w.Tick);
        GoHome(d);
        return true;
    }

    /// <summary>밖에서 외벽을 용접했다 (사람 용접보다 조금 덜 매끈하다).</summary>
    private void FleetWeld(Drone d, WorkOrder o)
    {
        var w = _world;
        d.Order = null;
        o.Drone = null;
        d.Cargo = Array.Empty<(ItemKind, int)>();
        if (w.Ship.WallAt(o.Target.Cell) is not WallState wall || !Hull.WorthWelding(wall)) { if (!o.Closed) w.Board.Close(o); GoHome(d); return; }
        float q = 0.86f + 0.02f * w.Fleet.Tier;
        wall.MaxIntegrity = MathF.Max(0.25f, wall.MaxIntegrity - Hull.WeldFatigue(0.5f));
        wall.Integrity = MathF.Max(wall.Integrity, wall.MaxIntegrity * q);
        wall.Breach = Hull.BreachFromIntegrity(wall.Integrity);
        wall.Welds++;
        wall.TotalWelds++;
        if (wall.Breach <= 0f) wall.Patched = false;
        JobsDone++;
        MarkLog.Add(wall.Marks, w.Tick, $"{d.Name}: 밖에서 용접 (최대 강도 {wall.MaxIntegrity * 100:0}%)");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(d.Name)} {Ko.EulReul(o.Target.Label)} 밖에서 용접했다 (최대 강도 {wall.MaxIntegrity * 100:0}%)");
        w.Board.Close(o);
        w.Fleet.Method("weld:drone", true);
        w.Fleet.CloseLine(CmdTarget.Drone, d.Id, "용접했다");
        d.Mind.Say($"{o.Target.Label} 밖에서 용접했다", w.Tick);
        GoHome(d);
    }

    /// <summary>매 분: 맡긴 일 정리 · 교대 · 떠내려간 드론 건질 차례.</summary>
    internal void FleetTick(FleetSystem f, bool cmd)
    {
        var w = _world;
        foreach (var d in Drones)
        {
            if (d.HullCare != null && (d.State is DroneState.Docked or DroneState.Adrift or DroneState.Lost || d.Order != null)) d.HullCare = null;
            if (f.DroneTask.TryGetValue(d.Id, out int oid))
            {
                var o = FindOrder(oid);
                if (o == null || o.Closed || d.State is DroneState.Adrift or DroneState.Lost || d.Wrecked || o.Assignee != null || o.Drone != null && o.Drone != d)
                {
                    f.DroneTask.Remove(d.Id);
                    if (o != null && o.Drone == d) o.Drone = null;
                    f.CloseLine(CmdTarget.Drone, d.Id, o == null || o.Closed ? "막혔다" : "다른 손이 맡았다");
                }
                else if (o.Drone == null) o.Drone = d; // 그늘에 숨은 동안에도 이 파공은 이 드론 몫
            }
            if (cmd && d.State == DroneState.Working && d.Order is WorkOrder wo && wo.Kind == WorkKind.SealBreach && d.WorkProgress < 0.92f && d.Battery < ReturnCost(d) + 0.1f)
                Handoff(f, d, wo);
        }
        foreach (var lost in Drones)
        {
            if (lost.State != DroneState.Adrift) continue;
            var by = Drones.FirstOrDefault(x => x.Fetching == lost);
            if (by != null) { f.SaidFetch(lost, by); continue; }
            if (f.FetchTask.ContainsValue(lost.Id)) continue;
            if (Drones.Any(x => RobotsV15.Base(x.Kind) == DroneKind.Tow && x.Operational && x.State == DroneState.Docked && x.Battery >= 0.8f)) continue; // 견인 드론이 곧 나간다
            var alt = Drones.Where(x => x != lost && x.Operational && x.State == DroneState.Docked && x.Battery >= 0.7f && !f.DroneTask.ContainsKey(x.Id) && !f.FetchTask.ContainsKey(x.Id))
                .OrderByDescending(x => x.Battery).ThenBy(x => x.Id).FirstOrDefault();
            if (alt != null) f.FetchTask[alt.Id] = lost.Id;
        }
    }

    /// <summary>매 틱 배터리 검사에 걸렸다: 주 컴퓨터가 맡긴 파공이면 그냥 돌아오지 않고 교대를 부른다.</summary>
    private bool FleetLowBattery(Drone d)
    {
        var w = _world;
        if (FleetSystem.Off || d.State != DroneState.Working || d.Order is not WorkOrder o || o.Kind != WorkKind.SealBreach || !w.Fleet.DroneTask.ContainsKey(d.Id)) return false;
        if (!(w.Automation.Present && (w.Automation.MainOnline || w.Automation.Core.BackupCore))) return false;
        Handoff(w.Fleet, d, o);
        return true;
    }

    /// <summary>교대: 돌아올 몫만 남은 드론은 들어오고, 쉬던 드론이 한 만큼부터 이어서 한다.</summary>
    private void Handoff(FleetSystem f, Drone d, WorkOrder o)
    {
        var w = _world;
        var room = o.Target.Room;
        var fresh = Drones.Where(x => x != d && CanSeal(x.Kind) && x.Operational && x.State == DroneState.Docked && x.Battery >= 0.7f && !f.DroneTask.ContainsKey(x.Id))
            .OrderByDescending(x => x.Battery).ThenBy(x => x.Id).FirstOrDefault();
        // 쉬는 드론이 없으면 아직 나가지 않은(거치대에서 기다리는) 드론을 데려온다 — 반쯤 막힌 파공을 잇는 게 먼저
        if (fresh == null && Drones.Where(x => x != d && CanSeal(x.Kind) && x.Operational && x.State == DroneState.Docked && x.Battery >= 0.7f && f.DroneTask.ContainsKey(x.Id))
                .OrderByDescending(x => x.Battery).ThenBy(x => x.Id).FirstOrDefault() is Drone busy)
        {
            if (FindOrder(f.DroneTask[busy.Id]) is WorkOrder other && other.Drone == busy) { other.Drone = null; f.Forget(other); }
            f.DroneTask.Remove(busy.Id);
            f.CloseLine(CmdTarget.Drone, busy.Id, "교대가 먼저");
            fresh = busy;
        }
        f.Carry[o.Id] = d.WorkProgress;
        f.DroneTask.Remove(d.Id);
        o.Drone = null;
        d.Order = null;
        d.Mind.Say($"배터리 {d.Battery * 100:0}% — 돌아올 몫만 남아 {(fresh != null ? $"{Ko.WaGwa(fresh.Name)} " : "")}교대", w.Tick);
        w.Log.Add(w.Tick, LogKind.Work, $"{d.Name}: 배터리가 돌아올 만큼만 남았다 — {o.Title} {d.WorkProgress * 100:0}%에서 교대하러 들어온다");
        GoHome(d);
        f.Reliefs++;
        f.CloseLine(CmdTarget.Drone, d.Id, "교대");
        if (fresh == null) return;
        f.DroneTask[fresh.Id] = o.Id;
        o.Drone = fresh;
        fresh.Mind.Say($"{Ko.WaGwa(d.Name)} 교대 — {room?.Name ?? "?"} 파공 {f.Carry[o.Id] * 100:0}%부터 이어서", w.Tick);
        f.Line(CmdTarget.Drone, fresh.Id, room, $"{fresh.Name}: {d.Name} 교대 — {room?.Name ?? "?"} 파공 이어서", $"{d.Name} 배터리 {d.Battery * 100:0}%", 0.9f, 40f, -1, o.Id);
    }

    /// <summary>교대로 이어받은 일: 앞 드론이 한 만큼부터.</summary>
    private void FleetCarry(Drone d)
    {
        var c = _world.Fleet.Carry;
        if (d.Order is not WorkOrder o || !c.TryGetValue(o.Id, out float p)) return;
        c.Remove(o.Id);
        d.WorkDone = d.WorkNeeded * Math.Clamp(p, 0f, 0.95f);
    }

    /// <summary>밖에서 파공을 막았다 (실링폼 · 금속판) — 잘 통한 방법을 배운다.</summary>
    private void FleetSeal(Drone d, WorkOrder o)
    {
        var w = _world;
        var f = w.Fleet;
        var room = o.Target.Room!;
        bool plate = d.Cargo.Any(x => x.kind == ItemKind.Plate);
        string method = plate ? "seal:plate" : "seal:sealant";
        f.DroneTask.Remove(d.Id);
        o.Drone = null;
        d.Order = null;
        if (w.Ship.WallAt(o.Target.Cell) is not WallState wall || wall.Breach <= 0f && !wall.Patched)
        {
            GoHome(d);
            return;
        }
        float fail = (plate ? 0.06f : 0.16f - 0.03f * (f.Tier - 1)) + (wall.Breach >= 0.25f ? 0.06f : 0f) + 0.3f * (1f - d.Hurt.Arm);
        d.Cargo = Array.Empty<(ItemKind, int)>();
        if (f.Rng.Chance(fail))
        {
            f.SealFails++;
            f.Method(method, false);
            MarkLog.Add(wall.Marks, w.Tick, $"{d.Name}: 밖에서 {(plate ? "덧댄 금속판" : "실링폼")}이 붙지 않았다");
            w.Log.Add(w.Tick, LogKind.Warning, $"{d.Name}: {room.Name} 파공에 밖에서 {(plate ? "덧댄 금속판이 들떴다" : "뿌린 실링폼이 날려 갔다")} — 다시 해야 한다");
            d.Mind.Say($"{(plate ? "금속판" : "실링폼")}이 붙지 않았다 — 다음엔 다른 방법", w.Tick);
            GoHome(d);
            return;
        }
        if (plate)
        {
            wall.MaxIntegrity = MathF.Max(0.25f, wall.MaxIntegrity - 0.05f);
            wall.Integrity = MathF.Max(wall.Integrity, wall.MaxIntegrity * 0.85f);
            wall.Breach = Hull.BreachFromIntegrity(wall.Integrity);
            wall.Welds++;
            wall.TotalWelds++;
            if (wall.Breach > 0f) { wall.Patched = true; wall.PatchQuality = 0.85f; }
        }
        else
        {
            wall.Patched = true;
            wall.PatchQuality = 0.6f + 0.08f * f.Tier;
            wall.Seals++;
        }
        f.Seals++;
        f.Method(method, true);
        JobsDone++;
        MarkLog.Add(wall.Marks, w.Tick, $"{d.Name}: 밖에서 {(plate ? "금속판을 덧대 용접" : "실링폼 봉합")}");
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(d.Name)} 밖에서 {room.Name} 파공을 {(plate ? "금속판을 덧대 용접해" : "실링폼으로")} 막았다", room, null, o.Target.Cell);
        w.Board.Close(o);
        d.Mind.Say($"{room.Name} 파공을 밖에서 막았다 ({(plate ? "금속판" : "실링폼")})", w.Tick);
        GoHome(d);
    }

    /// <summary>운석 경보: 맡은 파공 곁이면 돌아가지 않고 선체 그늘에 숨는다 (배터리가 넉넉할 때).</summary>
    private bool FleetShelters(Drone d)
    {
        var w = _world;
        if (FleetSystem.Off || d.Order is not WorkOrder o || o.Kind != WorkKind.SealBreach || !w.Fleet.DroneTask.ContainsKey(d.Id)) return false;
        if (w.Sensors.Alarm is not IncomingMeteor inc || d.Battery < ReturnCost(d) + 0.2f || DroneShelter(d, inc.Entry.Center) == null) return false;
        if (d.State == DroneState.Working) w.Fleet.Carry[o.Id] = d.WorkProgress;
        w.Fleet.Dodges++;
        d.Mind.Say("운석 경보 — 파공 곁 선체 그늘에 숨었다가 다시 붙는다", w.Tick);
        return true;
    }

    /// <summary>파편이 지나갔다: 그늘에서 나와 맡은 파공으로 돌아간다.</summary>
    private bool FleetResume(Drone d)
    {
        var w = _world;
        var f = w.Fleet;
        if (FleetSystem.Off || !f.DroneTask.TryGetValue(d.Id, out int oid)) return false;
        var o = FindOrder(oid);
        if (o == null || o.Closed || o.Assignee != null || o.Drone != null && o.Drone != d || d.Cargo.Length == 0 || d.Faulty || d.Wrecked
            || WallSpot(o) is not Vector2 at || d.Battery < ReturnCost(d) + 0.15f)
        {
            f.DroneTask.Remove(d.Id);
            if (o != null && o.Drone == d) o.Drone = null;
            return false;
        }
        d.Order = o;
        o.Drone = d;
        d.State = DroneState.Outbound;
        d.StateSince = w.Tick;
        d.Doing = $"파편이 지나갔다 — {o.Target.Room!.Name} 파공으로 돌아간다";
        PlanRoute(d, at);
        d.Mind.Say("파편이 지나갔다 — 그늘에서 나와 하던 파공으로", w.Tick);
        return true;
    }
}
