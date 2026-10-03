using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.5 도킹 일: 기밀 확인 · 기압 맞추기 · 물자 교환 · 같이 손보기 · 난파선 뒤지기(우주복) · 자르기 · 기록 읽는 저녁.
public sealed partial class DockingSystem
{
    public enum Task : byte { None, Seal, Trade, Joint, Search, Cut, Memorial }
    private readonly Dictionary<int, long> _blocked = new();

    private int InsideCount(DockVisit v) => _w.Crew.Count(c => !c.Dead && (c.Job?.Activity is DockActivity && c.Job.WillDonSuit || InWreck(c, v)));

    /// <summary>이 사람이 지금 할 만한 도킹 일 (점수 · 이유).</summary>
    public (Task task, int room, float score, string why) Choose(CrewMember c)
    {
        var w = _w;
        if (Off || !c.CanAct || c.IsChild || Visits.Count == 0) return (Task.None, -1, 0f, "—");
        if (MemorialNow is DockVisit mv && !c.Outside && !(c.Job?.Urgent ?? false))
            return (Task.Memorial, -1, c.Room?.Id == mv.MemorialRoom ? 0.95f : 0.85f, $"{mv.Name} 사람들의 마지막 기록을 같이 듣는다");
        if (Active is not DockVisit v || c.Passenger || Crisis.Acting(w) || c.Outside) return (Task.None, -1, 0f, "—");
        if (_blocked.TryGetValue(c.Id, out var until) && w.Tick < until) return (Task.None, -1, 0f, "우주복이 없다");
        float mech = c.RawSkill(Skill.Mechanics);
        switch (v.Stage)
        {
            case DockStage.Seal when v.SealBy < 0 || v.SealBy == c.Id:
                return (Task.Seal, -1, 0.6f + 0.25f * mech, v.Reseats > 0 ? "도킹 고리를 다시 물려 시험한다" : "도킹 고리 기밀 확인 · 압력 유지 시험");
            case DockStage.Open when !v.Wreck:
                if (!v.JointDone && v.JointMachine >= 0 && (v.JointBy < 0 || v.JointBy == c.Id))
                    return (Task.Joint, -1, 0.5f + 0.3f * mech, $"{v.Name} 기관사와 같이 손본다");
                if (!v.TradeDone && (v.TradeBy < 0 || v.TradeBy == c.Id)) return (Task.Trade, -1, 0.48f, "해치 앞에서 물자를 바꾼다");
                break;
            case DockStage.Open or DockStage.Salvage when v.Wreck:
            {
                if (c.Vitals.Injury > 0.35f || c.Needs.Fatigue > 0.85f || InsideCount(v) >= 3) break;
                float fear = (c.Fears.Contains(Fear.Dark) ? 0.15f : 0f) + (c.Fears.Contains(Fear.Spacewalk) || c.Fears.Contains(Fear.Vacuum) ? 0.25f : 0f) + (c.Fears.Contains(Fear.Death) ? 0.1f : 0f);
                var sr = v.Rooms.Where(r => !r.Searched && r.SearchBy < 0).OrderBy(r => r.HazardKnown ? 1 : 0).ThenBy(r => Math.Abs(r.Spot.X - v.DockX)).FirstOrDefault();
                if (sr != null) return (Task.Search, sr.Id, 0.52f + 0.15f * c.Traits.Bravery - fear, $"{v.Name} {sr.Name}을 뒤진다 (어둠 · 진공)");
                // 컴퓨터가 흔들리는 격벽을 말렸으면 그 방은 자르지 않는다
                var cr = v.Rooms.Where(r => r.Searched && !r.Gone && !r.Collapsed && r.CutBy < 0 && !(r.Hazard == 2 && r.HazardKnown && ComputerOn))
                    .OrderBy(r => r.HazardKnown ? 1 : 0).ThenBy(r => r.Id).FirstOrDefault();
                if (cr != null) return (Task.Cut, cr.Id, 0.46f + 0.2f * mech - fear, $"{v.Name} {cr.Name}을 잘라 온다");
                break;
            }
        }
        return (Task.None, -1, 0f, "할 일 없음");
    }

    public Job? Plan(CrewMember c, World w, DistanceField dist, Activity act)
    {
        var (task, ri, _, why) = Choose(c);
        if (task == Task.None) return null;
        if (task == Task.Memorial && MemorialNow is DockVisit mv)
        {
            var room = w.Ship.Rooms[mv.MemorialRoom];
            var at = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(x => Math.Abs(x.X - mv.Plaque.X) + Math.Abs(x.Y - mv.Plaque.Y)).Cast<Cell?>().FirstOrDefault();
            if (at is not Cell spot) return null;
            long end = mv.MemorialAt + SimTime.Minutes(30);
            return new Job(act, "추모", new List<Toil>
            {
                new GotoToil(spot),
                new WaitToil((int)Math.Max(SimTime.Minutes(5), end - w.Tick), Pose.Standing, mv.Plaque.Center) { DoneWhen = (cm, world) => mv.Mourned },
            }) { LogText = $"{mv.Name} 사람들의 기록을 들으러 {room.Name}에", LogKind = LogKind.Life };
        }
        var v = Active!;
        var hatch = w.Ship.Doors[v.HatchDoor];
        if (Inner(hatch) is not Cell inner) return null;
        switch (task)
        {
            case Task.Seal:
                v.SealBy = c.Id;
                return new Job(act, "도킹 고리 기밀 확인", new List<Toil>
                {
                    new GotoToil(inner),
                    new WaitToil(SimTime.Minutes(50), Pose.Working, hatch.Cell.Center) { EveryTick = (cm, world) => world.Dock.SealWorkTick(cm, v), DoneWhen = (cm, world) => v.SealWork >= 1f || v.Stage != DockStage.Seal },
                    new DoToil((cm, world) => { world.Dock.FinishSeal(cm, v); return true; }),
                }) { LogText = why, LogKind = LogKind.Work, InterruptMargin = 0.3f };
            case Task.Trade:
                v.TradeBy = c.Id;
                return new Job(act, "물자 교환", new List<Toil>
                {
                    new GotoToil(inner),
                    new WaitToil(SimTime.Minutes(45), Pose.Working, hatch.Cell.Center) { EveryTick = (cm, world) => world.Dock.TradeTick(cm, v), DoneWhen = (cm, world) => v.TradeWork >= 1f || v.Stage != DockStage.Open },
                    new DoToil((cm, world) => { world.Dock.FinishTrade(cm, v); return true; }),
                }) { LogText = why, LogKind = LogKind.Work };
            case Task.Joint:
            {
                if (JointOf(v) is not Machine m) { v.JointDone = true; return null; }
                var spot = m.Body.Room.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x)).OrderBy(x => (x.Center - m.Body.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
                if (spot is not Cell sp) { v.JointDone = true; return null; }
                v.JointBy = c.Id;
                return new Job(act, "같이 손보기", new List<Toil>
                {
                    new GotoToil(sp),
                    new WaitToil(SimTime.Hours(2), Pose.Working, m.Body.Center) { EveryTick = (cm, world) => world.Dock.JointTick(cm, v), DoneWhen = (cm, world) => v.JointWork >= 1f || v.Stage != DockStage.Open },
                    new DoToil((cm, world) => { world.Dock.FinishJoint(cm, v); return true; }),
                }) { LogText = why, LogKind = LogKind.Work };
            }
            case Task.Search or Task.Cut:
            {
                var r = v.Rooms[ri];
                var toils = Plans.DropOff(c, w, dist);
                if (!WorkPlanners.EvaOutFor(c, w, dist, toils, out _)) { _blocked[c.Id] = w.Tick + SimTime.Minutes(40); return null; }
                if (task == Task.Search) r.SearchBy = c.Id; else r.CutBy = c.Id;
                bool search = task == Task.Search;
                toils.Add(new GotoToil(r.Spot, cm => v.Stage is DockStage.Open or DockStage.Salvage));
                toils.Add(new WaitToil(SimTime.Hours(1.5f), Pose.Working, r.Spot.Center + new System.Numerics.Vector2(0f, -1f))
                {
                    EveryTick = search ? (cm, world) => world.Dock.SearchTick(cm, v, r) : (cm, world) => world.Dock.CutTick(cm, v, r),
                    DoneWhen = (cm, world) => (search ? r.Search >= 1f : r.CutWork >= 1f || r.Collapsed) || v.Stage >= DockStage.Leaving || cm.Suit is { Oxygen: < 0.7f } || cm.Down,
                });
                toils.Add(new DoToil((cm, world) => { if (search) world.Dock.FinishSearch(cm, v, r); else world.Dock.FinishCut(cm, v, r); return true; }));
                WorkPlanners.EvaInFor(w, toils);
                toils.Add(new DoToil((cm, world) => { world.Dock.Deliver(cm); return true; }));
                Stats.Trips++;
                return new Job(act, search ? "난파선 뒤지기 (우주복)" : "난파선 자르기 (우주복)", toils)
                {
                    LogText = search ? $"우주복을 입고 {v.Name} {r.Name}으로 — 헬멧 등 하나로" : $"절단기를 들고 {v.Name} {r.Name}으로", LogKind = LogKind.Work, InterruptMargin = 0.5f,
                };
            }
        }
        return null;
    }

    /// <summary>하던 도킹 일을 이어 갈 점수 (중간에 다른 일로 새지 않게).</summary>
    public float DoingScore(CrewMember c) => c.Job?.Label switch
    {
        "추모" => 0.9f,
        "도킹 고리 기밀 확인" or "같이 손보기" or "물자 교환" => 0.7f,
        "난파선 뒤지기 (우주복)" or "난파선 자르기 (우주복)" => c.Outside || Carrying(c) ? 1.2f : 0.7f,
        _ => 0.6f,
    };

    /// <summary>일이 끊기면 붙잡아 둔 자리를 푼다.</summary>
    internal void Release(CrewMember c)
    {
        if (Active is not DockVisit v) return;
        if (v.SealBy == c.Id) v.SealBy = -1;
        if (v.TradeBy == c.Id) v.TradeBy = -1;
        if (v.JointBy == c.Id) v.JointBy = -1;
        foreach (var r in v.Rooms) { if (r.SearchBy == c.Id) r.SearchBy = -1; if (r.CutBy == c.Id) r.CutBy = -1; }
    }
}

/// <summary>v18.5 도킹 일 (기밀 확인 · 물자 교환 · 같이 손보기 · 난파선 뒤지기 · 자르기 · 기록 읽는 저녁).</summary>
public sealed class DockActivity : Activity
{
    public override string Id => "dock";
    public override string Label => "도킹";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var d = w.Dock;
        if (DockingSystem.Off || d.Visits.Count == 0) return (0f, "—");
        if (c.Job?.Activity == this) return (d.DoingScore(c), c.Job.Label);
        d.Release(c);
        var (task, _, s, why) = d.Choose(c);
        if (task == DockingSystem.Task.None) return (0f, why);
        if (c.Pose == Pose.Sleeping && task != DockingSystem.Task.Memorial) s *= 0.2f;
        return (MathF.Max(0f, s), why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist) => w.Dock.Plan(c, w, dist, this);
}
