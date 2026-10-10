using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

public sealed partial class WorkBoard
{
    /// <summary>
    /// v9 배관: 새는 관 → 밸브부터 (본관이면 원자로를 세울지 사람이 정한다) → 고친다 (교체 · 임시 밀봉 · 우회 배관) → 밸브를 다시 연다.
    /// 방열판은 선체 밖 (드론 또는 EVA). 냉각수가 줄면 정수 탱크의 물로 보충한다.
    /// </summary>
    private void ScanPiping(Poster post)
    {
        var w = _world;
        var net = w.Piping;
        if (net.Segments.Count == 0) return;
        var ship = w.Ship;
        int plates = Have(ItemKind.Plate);
        int sealant = Have(ItemKind.Sealant);

        foreach (var s in net.Segments)
        {
            var valveRoom = s.ValveRoom;
            var leakRoom = ship.RoomAt(s.LeakAt) ?? PipeRoomNear(ship, s.LeakAt) ?? valveRoom;
            bool valveOk = valveRoom != null && !valveRoom.Detached && !valveRoom.OffLimits;
            bool leakOk = leakRoom != null && !leakRoom.Detached && !leakRoom.OffLimits;
            bool main = s.Role is PipeRole.HotLeg or PipeRole.ColdLeg;
            // 사출 준비로 배관을 끊은 방의 관은 잠근 채로 둔다
            bool held = (valveRoom?.PipesCut ?? false) || (leakRoom?.PipesCut ?? false) || s.Path.Any(c => ship.RoomAt(c)?.Detached == true);

            // 계획 교체를 하려는데 금속판이 모자라졌으면 (다른 데 썼으면) 접는다 — 임시 밀봉한 채로 다시 연다
            if (s.PlannedReplace && plates < 2 && !Taken(WorkKind.ReplacePipe, WorkTarget.OfPipe(s, leakRoom))) s.PlannedReplace = false;

            // 1) 새는 관: 밸브부터 잠근다. 잠그면 냉각이 통째로 멈추는 관(본관, 마지막 남은 분기)은 사람이 정한다
            if (s.Leaking && valveOk)
            {
                bool stops = net.WouldStopCooling(s) && net.MainFlow && net.CoolingKw > 1f;
                // 새는 채로 돌리기로 했어도 물이나 냉각수가 바닥나 가면 다시 정한다
                if (s.LimpApproved && (w.Water.Level < 40f || net.CoolantFraction < 0.45f)) s.LimpApproved = false;
                if (stops && !s.Severed && !s.IsolateApproved && !s.LimpApproved)
                    post(WorkKind.IsolateMain, WorkTarget.OfValve(s), 0.9f, Skill.Engineering,
                        $"{s.LeakRate:0}L/시간 샘 · 냉각수 {net.CoolantFraction * 100:0}% · 잠그면 원자로가 선다");
                else if (!s.LimpApproved)
                    post(WorkKind.CloseValve, WorkTarget.OfValve(s),
                        s.Severed || s.LeakRate >= 10f ? (s.IsCoolant ? 1.05f : 0.85f) : (s.IsCoolant ? 0.7f : 0.55f), Skill.Mechanics,
                        s.Severed ? "끊어졌다 — 잠가야 멈춘다" : $"{s.LeakRate:0}L/시간 샘" + (s.IsolateApproved ? " · 원자로를 세우기로 했다" : ""));
                // 작은 누수는 잠그지 않고 압력이 걸린 채로 막을 수도 있다 (증기에 델 수 있다).
                // 잠가도 냉각이 멈추지 않는 관(분기·급수관)은 실링폼이 넉넉할 때만 — 마지막 실링폼은 선체 파공 몫으로 남긴다
                bool canClose = !net.WouldStopCooling(s) || !s.IsCoolant;
                if (!s.Severed && s.LeakRate < 14f && sealant > (canClose ? 2 : 0) && leakOk)
                    post(WorkKind.PatchPipe, WorkTarget.OfPipe(s, leakRoom), s.IsCoolant ? 0.8f : 0.6f, Skill.Mechanics,
                        $"실링폼 클램프 · 압력이 걸린 채로 ({s.LeakRate:0}L/시간)");
            }

            // 2) 잠근 관: 고친다 — 새 관(금속판 2) → 임시 밀봉(실링폼) → 우회 배관(금속판 1 + 실링폼 2)
            if (s.Closed && !s.Sound)
            {
                float u = main ? 0.9f : s.Role == PipeRole.Branch ? 0.8f : 0.6f;
                if (leakOk)
                {
                    if (plates >= 2)
                        post(WorkKind.ReplacePipe, WorkTarget.OfPipe(s, leakRoom), u, Skill.Mechanics, "금속판 2 · 잠근 채 새 관으로");
                    if (!s.Severed && sealant > (main ? 0 : 2) && !s.PlannedReplace)
                        post(WorkKind.PatchPipe, WorkTarget.OfPipe(s, leakRoom), u - 0.1f, Skill.Mechanics, "실링폼 클램프 · 잠근 채로");
                }
                float closedHours = (w.Tick - s.ClosedSince) / (float)SimTime.TicksPerHour;
                bool replacing = Taken(WorkKind.ReplacePipe, WorkTarget.OfPipe(s, leakRoom)) || Taken(WorkKind.PatchPipe, WorkTarget.OfPipe(s, leakRoom));
                bool stuck = !leakOk || plates < 2 || closedHours > 2f;
                bool canBypass = plates >= 1 && sealant >= 2 || plates >= 2;
                // 우회 배관이 없거나, 이음매가 새서 흐름이 반 아래로 줄었으면 (다시) 깐다
                if (s.Bypass < 0.45f && s.Severed && stuck && !replacing && valveOk && canBypass && !held)
                    post(WorkKind.LayBypass, WorkTarget.OfValve(s), s.Bypass > 0f ? u - 0.1f : u, Skill.Mechanics,
                        (s.Bypass > 0f ? $"우회 배관이 샌다 (흐름 {s.Bypass * 100:0}%) · " : "") +
                        (!leakOk ? "터진 자리에 갈 수 없다" : plates < 2 ? "새 관을 만들 금속판이 없다" : "교체가 늦어진다") +
                        (sealant >= 2 ? " · 금속판 1 + 실링폼 2" : " · 금속판 2 (용접)") + " · 흐름 70%");
                // 고칠 재료가 하나도 없고 원자로가 서 있다: 새는 채로 다시 열지 (냉각수를 부어 가며) 사람이 정한다
                bool noFix = plates < 2 && sealant == 0 && !canBypass;
                if (!s.Severed && s.Bypass <= 0f && noFix && valveOk && !held && !s.LimpApproved && !s.PlannedReplace && s.IsCoolant
                    && !w.Power.ReactorOnline && w.Water.Level > 60f && net.CoolantFraction >= 0.7f && closedHours > 0.5f)
                {
                    bool alone = !net.Branches.Any(b => b != s && b.Flow > 0f) || s.Role is PipeRole.HotLeg or PipeRole.ColdLeg;
                    if (alone)
                        post(WorkKind.LimpMain, WorkTarget.OfValve(s), 0.85f, Skill.Engineering,
                            $"고칠 재료가 없다 · {s.LeakRate:0}L/시간 샐 것 · 물 {w.Water.Level:0}L · 배터리 {w.Power.BatteryPercent * 100:0}%");
                }
            }
            // 새는 채로 열기로 했다
            if (s.Closed && !s.Sound && s.LimpApproved && valveOk && !held)
                post(WorkKind.OpenValve, WorkTarget.OfValve(s), 1.0f, Skill.Mechanics, "새는 채로 연다 — 냉각수를 부어 가며 돌린다");

            // 3) 고친 관: 밸브를 다시 연다
            if (s.Closed && s.Sound && !s.PlannedReplace && !held && valveOk)
                post(WorkKind.OpenValve, WorkTarget.OfValve(s), s.IsCoolant ? 0.85f : 0.6f, Skill.Mechanics, "고쳤다 — 다시 흐르게");

            // 3.5) v9.2 계획 교체: 임시 밀봉한 관은 금속판이 모이면 또 터지기 전에 새 관으로 간다.
            //      분기·급수관은 잠시 잠가도 된다 (그동안 냉각 절반). 본관은 원자로를 세워야 하니 회의로 정한다
            if (s.Patched && !s.Closed && !s.PlannedReplace && !held && valveOk && leakOk && plates >= 2)
            {
                bool stops = s.IsCoolant && net.WouldStopCooling(s) && net.MainFlow;
                if (!stops)
                {
                    if (!s.IsCoolant || w.Power.BatteryPercent > 0.4f) s.PlannedReplace = true;
                }
                else if (w.Power.ReactorOnline && !Taken(WorkKind.PlanRepipe, WorkTarget.OfValve(s)))
                    post(WorkKind.PlanRepipe, WorkTarget.OfValve(s), 0.5f, Skill.Engineering,
                        $"임시 밀봉 {s.Patches}번" + (s.PatchFails > 0 ? $" · 다시 터짐 {s.PatchFails}번" : "") + $" · 금속판 {plates} · 배터리 {w.Power.BatteryPercent * 100:0}%");
            }
            // 계획 교체는 한 사람이 금속판부터 챙겨 → 밸브를 잠그고 → 새 관으로 간다 (잠가 놓고 금속판을 딴 데 뺏기지 않게)
            if (s.PlannedReplace && s.Sound && valveOk && leakOk && !held && plates >= 2)
            {
                bool stops = s.IsCoolant && !s.Closed && net.WouldStopCooling(s) && net.MainFlow;
                post(WorkKind.ReplacePipe, WorkTarget.OfPipe(s, leakRoom), stops ? 0.6f : 0.45f, Skill.Mechanics,
                    "계획 교체 — 금속판 2를 챙겨 " + (s.Closed ? "" : stops ? "원자로를 세우고 밸브를 잠근 뒤 " : "밸브를 잠시 잠그고 ") + "임시 밀봉을 걷어 내고 새 관으로"
                    + (!s.Closed && s.IsCoolant && !stops ? " (그동안 냉각 절반)" : ""));
            }

            // 4) 방열판 (선체 밖)
            if (s.Radiator.Count > 0 && s.RadiatorCondition < 0.7f)
                post(WorkKind.RepairRadiator, WorkTarget.OfRadiator(s), net.FlowingBranches <= 1 ? 0.75f : 0.55f, Skill.Mechanics,
                    $"상태 {s.RadiatorCondition * 100:0}% · 금속판 1 · 선체 밖");
        }

        // 5) 냉각수 보충: 정수 탱크의 물로 (보충관이 끊기면 물통으로 나른다)
        if (net.Built && net.CoolantFraction < 0.85f && w.Water.Level > 5f)
        {
            var port = ship.RoomAt(net.FillPort);
            if (port != null && !port.Detached && !port.OffLimits)
                post(WorkKind.RefillCoolant, WorkTarget.AtCell(net.FillPort, port), net.CoolantFraction < 0.5f ? 1.0f : net.CoolantFraction < 0.7f ? 0.8f : 0.55f,
                    Skill.Mechanics, $"냉각수 {net.CoolantFraction * 100:0}% · " + (net.FeedLine ? "보충관으로" : "보충관이 끊겨 물통으로 나른다"));
        }
    }

    /// <summary>그 일을 누가(사람이든 드론이든) 벌써 맡고 있는지.</summary>
    private bool Taken(WorkKind kind, WorkTarget target)
    {
        var probe = new WorkOrder { Kind = kind, Target = target };
        return _open.TryGetValue(probe.Key, out var o) && (o.Assignee != null || o.Drone != null);
    }

    /// <summary>벽 속 칸이면 맞닿은 방.</summary>
    private static Room? PipeRoomNear(Ship ship, Cell c)
    {
        foreach (var d in Cell.Dirs4)
            if (ship.RoomAt(c + d) is Room r) return r;
        return null;
    }
}

public static partial class WorkPlanners
{
    /// <summary>밸브 잠그기·열기: 밸브 앞에 서서 돌린다 (새는 곳 가까우면 증기에 델 수 있다).</summary>
    private static Job ValveJob(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, bool close)
    {
        var s = o.Target.Pipe!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(close ? 0.2f : 0.15f, Skill.Mechanics, s.ValveCell.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (close) world.Piping.CloseValve(s, cm);
            else world.Piping.OpenValve(s, cm);
            if (close) cm.Stats.Emergencies++;
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, close ? "밸브 잠그기" : "밸브 열기", toils, $"{o.Title} ({o.Detail})");
    }

    /// <summary>실링폼 클램프로 임시 밀봉 (압력이 걸려 있으면 증기에 데면서).</summary>
    private static Job? PatchPipe(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var s = o.Target.Pipe!;
        var toils = Fetch(c, w, dist, ItemKind.Sealant, 1);
        if (toils == null) { blocked = "실링폼 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.4f, Skill.Mechanics, s.LeakAt.Center) { CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Sealant });
        toils.Add(new DoToil((cm, world) =>
        {
            if (s.Sound) { world.Board.Close(o); return true; }
            Consume(cm, ItemKind.Sealant);
            world.Piping.Patch(s, cm.SkillLevel(Skill.Mechanics), cm);
            cm.Practice(Skill.Mechanics, 0.03f);
            cm.Stats.Repairs++;
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "배관 임시 밀봉", toils, $"{o.Title} ({o.Detail})");
    }

    /// <summary>새 관으로 갈아 끼운다: 금속판 2, 잠근 채로.</summary>
    private static Job? ReplacePipe(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var s = o.Target.Pipe!;
        bool planned = s.PlannedReplace && !s.Closed;
        if (!s.Closed && !planned) { blocked = "밸브부터 잠가야 한다"; return null; }
        var need = new[] { (ItemKind.Plate, 2) };
        var toils = FetchAll(c, w, dist, need);
        if (toils == null) { blocked = "금속판 2 없음"; return null; }
        if (planned)
        {
            // v9.2 계획 교체: 금속판을 챙긴 뒤에 밸브를 잠근다
            if (Plans.WorkSpot(WorkTarget.OfValve(s), w, dist, c) is not Cell valveAt) { blocked = "밸브에 갈 수 없음"; return null; }
            toils.Add(new GotoToil(valveAt));
            toils.Add(new WorkToil(0.2f, Skill.Mechanics, s.ValveCell.Center) { CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Plate });
            toils.Add(new DoToil((cm, world) =>
            {
                if (!s.Closed) world.Piping.CloseValve(s, cm);
                return true;
            }));
        }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1.5f, Skill.Mechanics, s.LeakAt.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (s.Sound && !s.Patched && s.Bypass <= 0f) { world.Board.Close(o); return true; }
            if (!UseAll(cm, need)) return false;
            world.Piping.Replace(s, cm.SkillLevel(Skill.Mechanics), cm);
            cm.Practice(Skill.Mechanics, 0.05f);
            cm.Stats.Repairs++;
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "배관 교체", toils, $"{o.Title} ({o.Detail})");
    }

    /// <summary>우회 배관: 잠근 구간을 금속판과 실링폼으로 돌아 잇는다 (밸브 자리에서).</summary>
    private static Job? LayBypass(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var s = o.Target.Pipe!;
        // 실링폼이 있으면 금속판 하나를 둘러 감고 실링폼으로 막고, 없으면 금속판 둘을 용접해 잇는다
        var need = w.Ship.CountStored(ItemKind.Sealant) >= 2 ? new[] { (ItemKind.Plate, 1), (ItemKind.Sealant, 2) } : new[] { (ItemKind.Plate, 2) };
        var toils = FetchAll(c, w, dist, need);
        if (toils == null) { blocked = $"{Cost(need)} 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1.0f, Skill.Mechanics, s.ValveCell.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (s.Sound || s.Bypass >= 0.45f) { world.Board.Close(o); return true; }
            if (!UseAll(cm, need)) return false;
            world.Piping.LayBypass(s, cm.SkillLevel(Skill.Mechanics), cm);
            cm.Practice(Skill.Mechanics, 0.04f);
            cm.Stats.Emergencies++;
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "우회 배관", toils, $"{o.Title} ({o.Detail})", LogKind.Warning);
    }

    /// <summary>냉각수 보충: 보충관이 살아 있으면 보충구에서 바로, 끊겼으면 정수기에서 물통에 받아 나른다.</summary>
    private static Job? RefillCoolant(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var net = w.Piping;
        if (w.Water.Level < 5f) { blocked = "정수 탱크가 비었다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        bool line = net.FeedLine;
        if (!line)
        {
            var recycler = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).FirstOrDefault();
            var spot = recycler?.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
            if (spot is not Cell rs) { blocked = "물을 받을 곳에 갈 수 없다"; return null; }
            toils.Add(new GotoToil(rs));
            toils.Add(new WaitToil(SimTime.Minutes(6), Pose.Working, recycler!.Center));
        }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(line ? 0.3f : 0.2f, Skill.Mechanics, net.FillPort.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            float added = world.Piping.Refill(line ? 30f : 15f);
            world.Log.Add(world.Tick, LogKind.Work, $"냉각수를 {added:0}L 보충했다 ({world.Piping.CoolantFraction * 100:0}%)" + (line ? "" : " — 물통으로 날랐다"), cm.Id);
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "냉각수 보충", toils, $"{o.Title} ({o.Detail})");
    }

    /// <summary>방열판 수리 (선체 밖, EVA).</summary>
    private static Job? RepairRadiator(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked) =>
        Eva(a, o, c, w, dist, at, out blocked, 1.0f, "방열판 수리");
}
