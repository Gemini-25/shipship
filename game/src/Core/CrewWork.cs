using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.7 승무원 일: 시신 수습(안치실로 모신다) · 의수·의족 만들어 달기 · 문병.

public sealed partial class WorkBoard
{
    private void ScanLife(Poster post)
    {
        var w = _world;
        // ── 시신 수습: 쓰러진 자리에 누운 동료를 안치실(없으면 냉동 창고·창고)로 ──
        var (morgue, k) = Facilities.Best(w.Ship, "morgue", r => !r.Detached && !r.Leaking && !r.OffLimits && Atmosphere.Danger(r) < 0.1f);
        if (morgue != null && w.History.Current == null)
            foreach (var d in w.Crew.Where(c => c.Dead && !c.Outside && c.CarriedBy == null && c.Room != null && c.Room != morgue && !c.Room.Detached && !c.Laid))
                post(WorkKind.RecoverBody, WorkTarget.OfCrew(d), MathF.Min(0.6f, 0.3f + 0.05f * (w.Tick - d.DiedAt) / SimTime.TicksPerHour), Skill.Medicine, // 오래 둘수록 마음이 쓰인다
                    $"{Ko.EulReul(d.Name)} {Ko.EuRo(morgue.Name)} 모신다" + (k < 1f ? $" (안치실이 없어 {morgue.Name})" : ""));
        // ── 의수·의족: 팔다리를 잃은 사람이 있고, 정비실에 작업대가 있으면 ──
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.Down))
            foreach (var wd in Wounds.Missing(c.Vitals).Take(1))
            {
                if (!wd.ProstheticMade && !w.Ship.FurnitureOf(FurnitureType.Workbench).Any(f => !f.Room.Detached && !f.Room.Abandoned)) continue;
                if (!wd.ProstheticMade && (Have(ItemKind.Electronics) < 2 || Have(ItemKind.Plate) < 1)) continue;
                post(WorkKind.FitProsthetic, WorkTarget.OfCrew(c), 0.4f, Skill.Mechanics,
                    $"{c.Name}의 {Wounds.PartName(wd.Part)} — {(Wounds.IsArm(wd.Part) ? "의수를" : "의족을")} 만들어 단다 (전자 부품 2 + 금속판 1)", minSkill: 0.35f);
            }
    }
}

public static partial class WorkPlanners
{
    /// <summary>시신 수습: 동료를 업어 안치실로 모신다. 가까웠던 사람에겐 힘든 일.</summary>
    private static Job? RecoverBody(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var body = o.Target.Crew!;
        if (!body.Dead || body.CarriedBy != null || body.Laid) { w.Board.Close(o); return null; }
        var (morgue, _) = Facilities.Best(w.Ship, "morgue", r => !r.Detached && !r.Leaking && !r.OffLimits);
        if (morgue == null) { blocked = "모실 곳이 없다"; return null; }
        var dest = morgue.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && !w.Crew.Any(d => d.Dead && d.Cell == x))
            .OrderBy(x => (x.Center - morgue.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
        if (dest is not Cell to) { blocked = "자리가 없다"; return null; }
        if (!dist.Reachable(body.Cell)) { blocked = "닿을 수 없다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(body.Cell));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!body.Dead || body.CarriedBy != null || (body.Position - cm.Position).Length() > 2.2f) return false;
            body.CarriedBy = cm;
            cm.CarryingPerson = body;
            return true;
        }));
        toils.Add(new GotoToil(to));
        toils.Add(new DoToil((cm, world) =>
        {
            if (cm.CarryingPerson != body) return false;
            body.CarriedBy = null;
            cm.CarryingPerson = null;
            body.Position = to.Center;
            body.PreviousPosition = to.Center;
            body.Room = world.Ship.RoomAt(to);
            body.Laid = true;
            world.Life.Stats.BodiesMoved++;
            float close = MathF.Max(0f, cm.AffinityTo(body));
            cm.Needs.Stress = MathF.Min(1f, cm.Needs.Stress + 0.05f + 0.15f * close);
            Life.Diary(world, cm, $"{Ko.EulReul(body.Name)} {Ko.EuRo(body.Room?.Name)} 모셨다.");
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Life, $"{Ko.EulReul(body.Name)} {Ko.EuRo(body.Room?.Name)} 모셨다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "시신 수습", toils, $"{Ko.EulReul(body.Name)} 모시러 간다", LogKind.Life);
    }

    /// <summary>의수·의족: 작업대에서 만들고(세 시간), 그 사람에게 가서 맞춘다(한 시간).</summary>
    private static Job? FitProsthetic(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var patient = o.Target.Crew!;
        var wd = Wounds.Missing(patient.Vitals).FirstOrDefault();
        if (patient.Dead || wd == null) { w.Board.Close(o); return null; }
        var bench = w.Ship.FurnitureOf(FurnitureType.Workbench).Where(f => !f.Room.Detached && f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0]))
            .OrderBy(f => dist.Get(f.UseSpots[0])).FirstOrDefault();
        if (bench == null && !wd.ProstheticMade) { blocked = "작업대에 닿을 수 없다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        if (!wd.ProstheticMade && bench != null)
        {
            if (w.Ship.CountStored(ItemKind.Electronics) < 2 || w.Ship.CountStored(ItemKind.Plate) < 1) { blocked = "재료가 없다"; return null; }
            toils.Add(new GotoToil(bench.UseSpots[0]));
            toils.Add(new WorkToil(3f, Skill.Mechanics, bench.Center));
            toils.Add(new DoToil((cm, world) => wd.ProstheticMade = Life.Take(world, ItemKind.Electronics, 2) && Life.Take(world, ItemKind.Plate, 1)));
        }
        toils.Add(new GotoToilLate(cm => Cell.Dirs8.Select(d => patient.Cell + d).Where(x => w.Ship.IsWalkable(x)).Cast<Cell?>().FirstOrDefault()));
        // v14.1 맞추는 동안 그 자리에 앉아 있어 달라고 한다 (돌아다니면 맞출 수 없다)
        toils.Add(new DoToil((cm, world) =>
        {
            if (patient.Dead || (patient.Position - cm.Position).LengthSquared() > 16f) return false;
            patient.HoldUntil = world.Tick + SimTime.Minutes(50);
            patient.HoldWhy = $"{Ko.IGa(cm.Name)} {(Wounds.IsArm(wd.Part) ? "의수를" : "의족을")} 맞춰 준다";
            patient.NextThinkTick = world.Tick;
            return true;
        }));
        toils.Add(new GotoToilLate(cm => Cell.Dirs8.Select(d => patient.Cell + d).Where(x => w.Ship.IsWalkable(x)).Cast<Cell?>().FirstOrDefault()));
        toils.Add(new WorkToil(0.6f, Skill.Medicine, patient.Position)
        {
            CanContinue = (cm, world) =>
            {
                if (patient.Dead || (patient.Position - cm.Position).LengthSquared() >= 9f) return false;
                patient.HoldUntil = Math.Max(patient.HoldUntil, world.Tick + SimTime.Minutes(5)); // 끝날 때까지 기다려 준다
                return true;
            },
        });
        toils.Add(new DoToil((cm, world) =>
        {
            wd.Prosthetic = true;
            patient.HoldUntil = -1;
            world.Life.Stats.Prosthetics++;
            string what = Wounds.IsArm(wd.Part) ? "의수" : "의족";
            patient.ChangeAffinity(cm, 0.15f);
            Life.Diary(world, patient, $"{Ko.IGa(cm.Name)} {Ko.EulReul(what)} 만들어 줬다. 다시 {(Wounds.IsArm(wd.Part) ? "일할" : "걸을")} 수 있다.");
            world.History.Add(world, HistoryKind.Bond, $"{Ko.IGa(cm.Name)} {patient.Name}에게 {Ko.EulReul(what)} 만들어 달았다 ({Wounds.PartName(wd.Part)})", patient.Room, new[] { cm, patient }, log: true);
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "의수·의족", toils, $"{patient.Name}의 {(Wounds.IsArm(wd.Part) ? "의수를" : "의족을")} 만든다");
    }
}

/// <summary>문병: 아프거나 다친 가까운 사람 곁에 가서 앉아 있어 준다.</summary>
public sealed class VisitActivity : Activity
{
    public override string Id => "visit";
    public override string Label => "문병";

    private static CrewMember? Patient(CrewMember c, World w) =>
        w.Crew.Where(p => p != c && !p.Dead && !p.Outside && p.Room != null && p.CarriedBy == null
                          && (p.Down || p.CareBed != null || p.Vitals.Injury > 0.35f || w.Disease.Severity(p) > 0.3f || p.GriefUntil > w.Tick || p.Fx.Worst > 0.4f)
                          && w.Tick - p.LastVisited > SimTime.Hours(10) && c.AffinityTo(p) > 0.15f
                          && !(DiseaseSystem.Sick(p) && p.Room.Kind is RoomType.Quarantine or RoomType.QuarantineLock))
            .OrderByDescending(p => c.AffinityTo(p)).FirstOrDefault();

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        // 문병은 성한 사람이 간다 (다친 사람끼리 오가면 치료하러 온 사람이 헛걸음한다)
        if (c.Down || c.Outside || c.Needs.Rest < 0.3f || c.Needs.Food < 0.3f || c.Vitals.Injury > 0.2f || c.Vitals.Health < 0.6f || DiseaseSystem.Sick(c)) return (0f, "—");
        if (w.Board.OpenUnsorted.Any(o => o.Kind == WorkKind.Treat && o.Target.Crew == c)) return (0f, "치료를 기다린다");
        var p = Patient(c, w);
        if (p == null || !dist.Reachable(p.Cell)) return (0f, "—");
        string why = p.GriefUntil > w.Tick && !p.Down ? "슬픔에 잠겨 있다" : p.Down ? "쓰러져 누워 있다" : DiseaseSystem.Sick(p) ? "열이 난다" : p.Fx.Worst > 0.4f ? "앓고 있다" : "다쳤다";
        return (0.2f + 0.35f * c.AffinityTo(p) + 0.1f * c.Traits.Sociability, $"{Ko.IGa(p.Name)} {why}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var p = Patient(c, w);
        if (p == null) return null;
        var spot = Cell.Dirs8.Select(d => p.Cell + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell s) return null;
        var toils = new List<Toil>
        {
            new GotoToil(s),
            new WaitToil(SimTime.Minutes(25), Pose.Sitting, p.Position),
            new DoToil((cm, world) =>
            {
                if (p.Dead || (p.Position - cm.Position).LengthSquared() > 9f) return false;
                p.LastVisited = world.Tick;
                p.Needs.Stress = MathF.Max(0f, p.Needs.Stress - 0.08f);
                p.Needs.Social = MathF.Min(1f, p.Needs.Social + 0.2f);
                cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.15f);
                p.ChangeAffinity(cm, 0.06f);
                cm.ChangeAffinity(p, 0.04f);
                world.Life.Stats.Visits++;
                Life.Diary(world, p, $"{Ko.IGa(cm.Name)} 곁에 있어 줬다.");
                return true;
            }),
        };
        return new Job(this, "문병", toils) { LogText = $"{Ko.EulReul(p.Name)} 보러 간다", LogKind = LogKind.Life, TargetRoom = p.Room };
    }
}

/// <summary>v14.1 잠깐 그 자리에서 기다린다 (누가 의수를 맞춰 주는 동안 등).</summary>
public sealed class HoldActivity : Activity
{
    public override string Id => "hold";
    public override string Label => "기다림";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist) =>
        c.HoldUntil > w.Tick && !c.Down && !c.Outside && EvacuateActivity.DangerHere(c, w) < 0.3f ? (1.4f, c.HoldWhy ?? "기다려 달라고 했다") : (0f, "—");

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var toils = new List<Toil> { new WaitToil(Math.Max(SimTime.Minutes(1), (int)(c.HoldUntil - w.Tick)), Pose.Sitting, c.Position) { DoneWhen = (cm, world) => cm.HoldUntil <= world.Tick } };
        return new Job(this, "기다림", toils) { LogText = c.HoldWhy, LogKind = LogKind.Life };
    }
}
