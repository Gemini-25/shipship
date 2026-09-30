using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

public sealed partial class WorkBoard
{
    /// <summary>v10.10 선내 로봇: 멈춘 로봇은 사람이 고치거나 끌어오고, 닳은 로봇은 충전대에서 정비한다.</summary>
    private void ScanRobots(Poster post)
    {
        var w = _world;
        var robots = w.Robots.Robots;
        if (robots.Count == 0) return;
        int down = robots.Count(r => r.Fault != null || r.State == RobotState.Stalled);
        foreach (var r in robots)
        {
            if (r.Disabled || r.State is RobotState.Lost or RobotState.Towed || r.Dock.Room.Detached) continue;
            if (r.Room is Room room && (room.Abandoned || room.OffLimits)) continue;
            if (r.Fault is RobotFault f)
            {
                // 가벼운 고장은 로봇이 스스로 고친다 (충전대로 돌아가는 중이거나, 충전대에 전기가 있을 때)
                if (RobotSystem.CanSelfRepair(r) && (r.State == RobotState.Active || (r.AtDock && RobotSystem.DockWorking(r)))) continue;
                var parts = RobotSystem.FaultParts(f);
                bool have = parts.All(x => Have(x.kind) >= x.count);
                var alt = RobotSystem.MakeshiftParts(f);
                bool makeshift = !have && alt.Length > 0 && alt.All(x => Have(x.kind) >= x.count);
                if (!have && !makeshift && parts.Length > 0) continue;
                string need = parts.Length == 0 ? "다시 맞추기만" : have ? string.Join(" + ", parts.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}"))
                    : "임시로 " + string.Join(" + ", alt.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}"));
                post(WorkKind.RepairRobot, WorkTarget.OfRobot(r), 0.3f + 0.08f * down + (r.Kind == RobotKind.Safety ? 0.05f : 0f), RobotSystem.FaultSkill(f),
                    $"{RobotSystem.FaultName(f)} · {need}" + (RobotSystem.WhyNotSelf(r) is string why && RobotSystem.Minor(f) ? $" · {why}" : "") + (r.AtDock ? "" : $" · {r.Room?.Name ?? "?"}에 멈춤"));
                continue;
            }
            if (r.State == RobotState.Stalled)
            {
                post(WorkKind.FetchRobot, WorkTarget.OfRobot(r), 0.28f + 0.06f * down, Skill.Mechanics,
                    $"배터리 {r.Battery * 100:0}% · {r.Room?.Name ?? "?"}에서 {r.Dock.Room.Name} 충전대까지");
                continue;
            }
            if (r.AtDock && r.Condition < 0.5f && Have(ItemKind.Lubricant) > 0)
                post(WorkKind.ServiceRobot, WorkTarget.OfRobot(r), 0.18f + (0.5f - r.Condition) * 0.6f, Skill.Mechanics,
                    $"상태 {r.Condition * 100:0}% · 윤활유 1");
        }
    }
}

public static partial class WorkPlanners
{
    // ═════════════════════════════ v10.10 선내 로봇 ═════════════════════════════

    /// <summary>로봇 수리: 부품을 들고 로봇이 멈춘 자리로 가서 고친다 (부품이 없으면 임시로).</summary>
    private static Job? RepairRobot(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var r = o.Target.Robot!;
        if (r.Fault is not RobotFault f) { w.Board.Close(o); return null; }
        var parts = RobotSystem.FaultParts(f);
        bool makeshift = parts.Length > 0 && !parts.All(x => w.Board.Have(x.kind) >= x.count);
        var need = makeshift ? RobotSystem.MakeshiftParts(f) : parts;
        var toils = need.Length > 0 ? FetchAll(c, w, dist, need) : Plans.DropOff(c, w, dist);
        if (toils == null) { blocked = $"{Cost(need)} 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(RobotSystem.FaultHours(f) * (makeshift ? 1.3f : 1f), o.Skill, r.Position) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (r.Fault == null) { world.Board.Close(o); return true; }
            if (need.Length > 0 && !UseAll(cm, need)) return false;
            world.Robots.Repaired(r, cm, makeshift);
            cm.Practice(o.Skill, 0.02f);
            cm.Stats.Repairs++;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{Ko.EulReul(r.Name)} {(makeshift ? "임시로 " : "")}고쳤다 ({RobotSystem.FaultName(f)})", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "로봇 수리", toils, $"{o.Title} ({o.Detail})");
    }

    /// <summary>멈춘 로봇 끌어오기: 로봇 곁으로 가서 끌고 충전대까지 (사람 걸음이 느려진다).</summary>
    private static Job? FetchRobot(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var r = o.Target.Robot!;
        if (r.State != RobotState.Stalled) { w.Board.Close(o); return null; }
        var home = r.Dock.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (home is not Cell dockSpot) { blocked = "충전대에 갈 수 없음"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new DoToil((cm, world) =>
        {
            if (r.State != RobotState.Stalled) return false;
            world.Robots.StartTow(r, cm);
            world.Log.Add(world.Tick, LogKind.Work, $"멈춘 {Ko.EulReul(r.Name)} 끌고 충전대로 간다", cm.Id);
            return true;
        }));
        toils.Add(new GotoToil(dockSpot));
        toils.Add(new DoToil((cm, world) =>
        {
            if (r.TowedBy != cm) return false;
            world.Robots.Returned(r);
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "로봇 끌기", toils, o.Title);
    }

    /// <summary>로봇 정비: 충전대에서 윤활·조임·센서 청소.</summary>
    private static Job? ServiceRobot(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var r = o.Target.Robot!;
        if (!r.AtDock) { blocked = "로봇이 나가 있다"; return null; }
        var toils = Fetch(c, w, dist, ItemKind.Lubricant, 1);
        if (toils == null) { blocked = "윤활유 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.4f, Skill.Mechanics, r.Dock.Center) { CanContinue = (_, _) => r.AtDock });
        toils.Add(new DoToil((cm, world) =>
        {
            Consume(cm, ItemKind.Lubricant);
            world.Robots.Serviced(r);
            cm.Stats.Services++;
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "로봇 정비", toils, o.Title);
    }

    // ═════════════════════════════ v10.10 자원 회복 ═════════════════════════════

    /// <summary>물통 급수: 정수기 꼭지에서 물통을 채워 재배대에 붓는다 (급수 본관이 끊겼을 때).</summary>
    private static Job? CarryWater(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var bed = o.Target.Furniture!;
        var tap = Logistics.WaterTap(w);
        var tapSpot = tap?.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (tap == null || tapSpot is not Cell ts) { blocked = "물꼭지에 갈 수 없음"; return null; }
        float liters = Logistics.JugLiters(bed);
        bool filled = false;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(ts));
        toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Working, tap.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (world.Water.Level < liters + 5f) return false;
            world.Water.Level -= liters;
            filled = true;
            return true;
        }));
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.12f, Skill.Botany, bed.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!filled || bed.Machine?.Crop is not CropState crop) return false;
            crop.HandWateredHours = Logistics.HandWaterHours;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{bed.Label}에 물통으로 물 {liters:0.0}L를 부었다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "물통", toils, $"급수 본관이 끊겨 {Ko.EulReul(bed.Label)} 물통으로 적시러 간다");
    }

    /// <summary>비상 물자함 채우기: 창고 선반에서 꺼내(창고 몫은 남기고) 물자함에.</summary>
    private static Job? StockCache(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var cache = o.Target.Furniture!;
        var kind = o.Product ?? ItemKind.Sealant;
        int want = Logistics.WantIn(w, cache, kind);
        var (shelf, spot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.Shelf && f.Storage!.Count(kind) > Logistics.Keep(kind));
        if (shelf == null || want <= 0) { blocked = $"{ItemKinds.Name(kind)} 여유 없음"; return null; }
        int take = Math.Min(want, shelf.Storage!.Count(kind) - Logistics.Keep(kind));
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new TakeToil(shelf, kind, take));
        toils.Add(new GotoToil(at));
        toils.Add(new PutToil(cache));
        toils.Add(new DoToil((_, world) => { world.Board.Close(o); return true; }));
        return Wrap(a, o, c, w, "비상 물자", toils, o.Title);
    }

    /// <summary>임시 배선 걷기: 배전반에서 풀어 케이블을 되찾는다 (탄 배선은 못 되찾는다). 흔적은 남는다.</summary>
    private static Job? RemoveJumper(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var p = w.Power;
        var j = p.Jumpers.FirstOrDefault(x => !x.Permanent && x.To == o.Circuit);
        if (j == null) { w.Board.Close(o); return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, Skill.Electrical, o.Target.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!world.Power.Jumpers.Remove(j)) return true;
            world.Power.RemovedJumpers.Add((j.At, j.From, j.To, j.Since, world.Tick));
            int back = j.Burnt ? 0 : PowerGrid.JumperCables;
            if (back > 0) cm.Carrying = new ItemStack(ItemKind.Cable, back);
            world.Adapt.JumpersRemoved++;
            float days = (world.Tick - j.Since) / (float)SimTime.TicksPerDay;
            MarkLog.Add(o.Target.Furniture!.Machine!.Marks, world.Tick, $"{cm.Name}: {PowerGrid.CircuitName(j.From)}→{PowerGrid.CircuitName(j.To)} 임시 배선을 걷었다 ({days:0.0}일 버텼다)");
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} {PowerGrid.CircuitName(j.To)} 회로 임시 배선을 걷었다 — {days:0.0}일 동안 버틴 배선" + (back > 0 ? $" (케이블 {back}개를 되찾았다)" : " (타 버려 못 쓴다)"), null, new[] { cm });
            world.Board.Close(o);
            return true;
        }));
        var (shelf, spot) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Accepts(ItemKind.Cable) && f.Storage.Free >= PowerGrid.JumperCables);
        if (shelf != null)
        {
            toils.Add(new GotoToil(spot, cm => cm.Carrying != null));
            toils.Add(new PutToil(shelf));
        }
        return Wrap(a, o, c, w, "배선 정리", toils, o.Title);
    }

    /// <summary>간이침대 치우기: 접어서 창고로 (방은 원래 용도로 돌아간다 — 용도 기록은 남는다).</summary>
    private static Job? StowCot(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var cot = o.Target.Furniture!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.3f, Skill.Mechanics, cot.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (cot.Owner != null || cot.Stowed) { world.Board.Close(o); return true; }
            var room = cot.Room;
            world.Ship.Stow(cot);
            world.Paths.Invalidate();
            world.Structure.Touch();
            world.Adapt.CotsStowed++;
            world.Board.Close(o);
            if (!room.Furniture.Any(f => f.Type == FurnitureType.Cot) && room.Purpose != null && room.Purpose.StartsWith("임시 침실"))
            {
                if (!room.FormerPurposes.Contains("임시 침실")) room.FormerPurposes.Add("임시 침실");
                room.Purpose = null;
                MarkLog.Add(room.Marks, world.Tick, $"{cm.Name}: 간이침대를 모두 치웠다 — 원래 {room.Name}으로");
                world.History.Add(world, HistoryKind.Adaptation, $"{room.Name}의 마지막 간이침대를 접었다 — 임시 침실이 원래 {Ko.EuRo(room.Name)} 돌아갔다", room, new[] { cm });
            }
            return true;
        }));
        return Wrap(a, o, c, w, "간이침대 정리", toils, o.Title);
    }

    /// <summary>재활용: 뜯긴 설비를 해체해 금속판·케이블을 되찾는다 (설비는 사라진다).</summary>
    private static Job? RecycleMachine(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var f = o.Target.Furniture!;
        var m = f.Machine!;
        if (!m.Has(FaultKind.Stripped)) { w.Board.Close(o); return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1f, Skill.Mechanics, f.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (f.Stowed || !m.Has(FaultKind.Stripped)) { world.Board.Close(o); return true; }
            var got = Recycle.Yield(f.Type);
            var room = f.Room;
            world.Ship.Stow(f);
            world.Paths.Invalidate();
            world.Structure.Touch();
            foreach (var (kind, n) in got.Skip(1))
            {
                int left = n;
                foreach (var box in world.Ship.Containers.Where(b => b.Type == FurnitureType.Shelf))
                {
                    left -= box.Storage!.Add(kind, left);
                    if (left <= 0) break;
                }
            }
            cm.Carrying = new ItemStack(got[0].kind, got[0].count);
            world.Adapt.Recycled++;
            MarkLog.Add(room.Marks, world.Tick, $"{cm.Name}: 뜯긴 {Ko.EulReul(f.Label)} 해체해 재활용");
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} 뜯긴 {Ko.EulReul(f.Label)} 해체했다 — {string.Join(" + ", got.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}"))}을(를) 되찾았다", room, new[] { cm });
            world.Board.Close(o);
            return true;
        }));
        var (shelf, spot) = Plans.NearestContainer(w, dist, c, b => b.Type == FurnitureType.Shelf && b.Storage!.Free >= 3);
        if (shelf != null)
        {
            toils.Add(new GotoToil(spot, cm => cm.Carrying != null));
            toils.Add(new PutToil(shelf));
        }
        return Wrap(a, o, c, w, "재활용", toils, o.Title);
    }

    // ═════════════════════════════ v11.0 예방 ═════════════════════════════

    /// <summary>
    /// 예방 점검: 설비 대상이면 알아챈 전조를 싼 재료로 손본다 (고장을 막는다).
    /// 방 대상이면 순찰 — 방을 한 바퀴 둘러보며 전조를 찾는다 (방재 로봇이 없을 때 사람이 한다).
    /// </summary>
    private static Job? PreventiveCheck(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        if (o.Target.Kind == TargetKind.Room)
        {
            var room = o.Target.Room!;
            var look = room.Cells.Where(w.Ship.IsOpenFloor).Where(dist.Reachable).OrderBy(x => (x.Center - room.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault() ?? at;
            var rounds = Plans.DropOff(c, w, dist);
            rounds.Add(new GotoToil(look));
            rounds.Add(new WorkToil(0.25f, Skill.Mechanics, room.Center));
            rounds.Add(new DoToil((cm, world) =>
            {
                Prevention.Inspect(world, room, cm.Name, robot: false, cm);
                cm.Practice(Skill.Mechanics, 0.005f);
                world.Board.Close(o);
                return true;
            }));
            return Wrap(a, o, c, w, "순찰", rounds, $"{room.Name} 순찰 점검");
        }
        var f = o.Target.Furniture!;
        var m = f.Machine!;
        if (m.Omen is not Omen omen) { w.Board.Close(o); return null; }
        var cost = Prevention.FixCost(omen.Kind);
        var toils = cost.Length > 0 ? FetchAll(c, w, dist, cost) : Plans.DropOff(c, w, dist);
        if (toils == null) { blocked = $"{Cost(cost)} 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(Prevention.FixHours(omen.Kind), m.Spec.Skill, f.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (m.Omen == null) { world.Board.Close(o); return true; }
            if (cost.Length > 0 && !UseAll(cm, cost)) return false;
            Prevention.Fixed(world, m, cm, null);
            cm.Practice(m.Spec.Skill, 0.02f);
            cm.Stats.Services++;
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "예방 정비", toils, $"{m.Name} {Prevention.Name(omen.Kind)} 손보기 ({o.Detail})");
    }
}
