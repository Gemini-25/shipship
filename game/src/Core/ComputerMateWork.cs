using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.27 승무원 행동 셋 (주컴퓨터가 일을 만들고 사람이 실제로 움직인다):
//  · 훈련 집결: 훈련 방송을 들은 사람이 비상 배치표 자리로 걷는다 (걸린 시간이 결과가 된다).
//  · 안부: 컴퓨터가 넌지시 부탁한 동료가 찾아가 말을 건다 (끼니를 거른 사람에겐 먹을 것을 들고).
//  · 개조 공사: 회의가 받은 개조안(코어 랙 · 감지기 · 데이터선 · 받침)을 전기 솜씨 있는 사람이 단다.

public sealed class MateActivity : Activity
{
    public override string Id => "mate";
    public override string Label => "주컴퓨터와 함께";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Automation.MateOrNull is not ShipMate m || ShipMate.Off || c.IsChild || !c.CanAct || c.Down || ShipMate.Skip.Contains('a')) return (0f, "—");
        if (m.ActiveDrill != null && m.DrillSpot(c) is Cell) return Crisis.Acting(w) ? (0f, "진짜 비상") : (0.92f, "훈련 — 비상 배치표 자리로");
        if (Crisis.Acting(w)) return (0f, "—");
        if (m.HintFor(c) is CareHint h && w.Crew.FirstOrDefault(x => x.Id == h.About) is CrewMember t && !t.Dead && t.IsAwake && t.Room != null && !t.Outside && c.IsAwake)
            return (Bedtime(c, w) ? 0.2f : 0.58f, $"{t.Name}에게 안부 — 컴퓨터가 넌지시");
        if (m.ShelterFor(c) is Room && c.IsAwake && !c.Outside)
            return (OnShift(c, w) ? 0.52f : 0.3f, "대피소 점검 — 날씨 예보");
        if (m.GearFor(c) is GearUpgrade u && c.IsAwake)
            return (OnShift(c, w) ? 0.5f : 0.22f, $"{u.Room} {u.Name} 공사");
        return (0f, "—");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Automation.MateOrNull is not ShipMate m) return null;
        if (m.DrillSpot(c) is Cell spot)
        {
            if (!dist.Reachable(spot)) { m.Arrived(c); return null; }
            var toils = new List<Toil>
            {
                new GotoToil(spot),
                new DoToil((cm, world) => { world.Automation.Mate.Arrived(cm); return true; }),
                new WaitToil(SimTime.Minutes(4), Pose.Standing),
            };
            return new Job(this, "비상 훈련 — 제 자리로", toils) { LogText = "훈련 — 비상 배치표 자리로", LogKind = LogKind.Work };
        }
        if (m.HintFor(c) is CareHint h && w.Crew.FirstOrDefault(x => x.Id == h.About) is CrewMember t && !t.Dead)
        {
            var near = Cell.Dirs8.Select(d => t.Cell + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
            if (near is not Cell at) return null;
            var hint = h;
            var toils = new List<Toil>
            {
                new GotoToil(at),
                new DoToil((cm, world) =>
                {
                    if (world.Crew.FirstOrDefault(x => x.Id == hint.About) is not CrewMember tt || tt.Dead || (tt.Position - cm.Position).LengthSquared() > 9f) return false;
                    cm.Say(world, Persona.Say(cm, hint.Signal.StartsWith("끼니", StringComparison.Ordinal) ? $"{tt.Name}, 같이 뭐 좀 먹자" : $"{tt.Name}, 요즘 어때?"));
                    return true;
                }),
                new WaitToil(SimTime.Minutes(12), Pose.Standing, t.Position),
                new DoToil((cm, world) =>
                {
                    if (world.Crew.FirstOrDefault(x => x.Id == hint.About) is CrewMember tt && !tt.Dead) world.Automation.Mate.Delivered(hint, cm, tt);
                    return true;
                }),
            };
            return new Job(this, $"{t.Name}에게 안부", toils) { LogText = $"{t.Name}에게 말을 건다", LogKind = LogKind.Life };
        }
        if (m.ShelterFor(c) is Room sh)
        {
            var inside = sh.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
            if (inside is not Cell at) return null;
            var shelter = sh;
            var toils = new List<Toil>
            {
                new DoToil((cm, world) => { world.Automation.Mate.ShelterTaken(cm); return true; }),
                new GotoToil(at),
                new WaitToil(SimTime.Minutes(10), Pose.Working),
                new DoToil((cm, world) => { world.Automation.Mate.ShelterChecked(cm, shelter); return true; }),
            };
            return new Job(this, "대피소 점검", toils) { LogText = $"{sh.Name} 점검 (물 · 마스크)", LogKind = LogKind.Work };
        }
        if (m.GearFor(c) is GearUpgrade u)
        {
            var target = u.Spot;
            var at = Cell.Dirs8.Prepend(new Cell(0, 0)).Select(d => target + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
            if (at is not Cell s) return null;
            var up = u;
            var toils = new List<Toil>
            {
                new GotoToil(s),
                new DoToil((cm, world) => { world.Automation.Mate.GearJoin(up, cm); return true; }),
                new WorkToil(0.75f, Skill.Electrical, target.Center),
                new DoToil((cm, world) => { world.Automation.Mate.GearProgress(up, cm, 0.75f); return true; }),
            };
            return new Job(this, $"{u.Name} 공사", toils) { LogText = $"{u.Room} {u.Name} 공사", LogKind = LogKind.Work };
        }
        return null;
    }
}
