using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.7 숨기는 몸짓 · 털어놓는 걸음: 사고 자리에 혼자 가서 흔적을 치운다 · 단말 앞에서 기록을 지운다(사람이 없을 때를 노린다) ·
// 무거워진 사람은 함장(없으면 가까운 사람)을 찾아가 털어놓는다. 다른 사람 눈에 띄면 그 사람이 기억한다 (InquirySystem).

public sealed class CoverActivity : Activity
{
    public override string Id => "cover";
    public override string Label => "혼자 다녀올 곳";

    private static Room? RoomOf(World w, int id) => id >= 0 && id < w.Ship.Rooms.Count ? w.Ship.Rooms[id] : null;

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (InquirySystem.Off || c.IsChild || c.Down || !c.CanAct || c.Outside || Crisis.Acting(w)) return (0f, "—");
        if (w.Inquiry.TaskOf(c) is not CoverTask t) return (0f, "—");
        var room = RoomOf(w, t.RoomId);
        switch (t.Kind)
        {
            case CoverTaskKind.Tidy:
            {
                if (room == null || room.Detached || w.Fire.CountIn(room) > 0 || room.Leaking) return (0f, "—");
                float s = 0.6f - (OnShift(c, w) ? 0.15f : 0f) + 0.25f * MathF.Min(1f, (w.Tick - t.Since) / (float)SimTime.Hours(3)); // 통합8 누가 먼저 보기 전에 — 시간이 갈수록 마음이 급해진다 (반나절을 미루다 흔적을 남겼다)
                return (s, "사고 자리를 한 번 더 둘러본다");
            }
            case CoverTaskKind.Wipe:
            {
                if (room == null || room.Detached) return (0f, "—");
                int others = w.Crew.Count(o => o != c && !o.Dead && o.IsAwake && o.Room == room);
                float s = 0.52f - 0.12f * MathF.Min(3, others) - (OnShift(c, w) ? 0.15f : 0f);
                return (MathF.Max(0f, s), "단말에서 정비 기록을 본다");
            }
            case CoverTaskKind.Confess:
            {
                var to = w.Crew.FirstOrDefault(o => o.Id == t.Other);
                if (to == null || to.Dead || !to.IsAwake || to.Outside) return (0.05f, "—");
                return (0.56f - (Bedtime(c, w) ? 0.3f : 0f), $"{to.Name}에게 할 말이 있다");
            }
        }
        return (0f, "—");
    }

    private static Cell? Near(World w, CrewMember c, DistanceField dist, Room room, Cell at) =>
        room.Cells.Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c))
            .OrderBy(x => (x.Center - at.Center).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).Cast<Cell?>().FirstOrDefault();

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Inquiry.TaskOf(c) is not CoverTask t) return null;
        var room = RoomOf(w, t.RoomId);
        var toils = new List<Toil>();
        switch (t.Kind)
        {
            case CoverTaskKind.Tidy:
            {
                if (room == null || Near(w, c, dist, room, t.At) is not Cell spot) return null;
                toils.Add(new GotoToil(spot));
                toils.Add(new WaitToil(SimTime.Minutes(6), Pose.Working, t.At.Center));
                toils.Add(new DoToil((cm, world) => { world.Inquiry.DidTidy(cm); return true; }));
                return new Job(this, "사고 자리를 한 번 더 둘러본다", toils) { TargetRoom = room, InterruptMargin = 0.2f };
            }
            case CoverTaskKind.Wipe:
            {
                if (room == null || Near(w, c, dist, room, t.At) is not Cell spot) return null;
                toils.Add(new GotoToil(spot));
                toils.Add(new WaitToil(SimTime.Minutes(8), Pose.Working, t.At.Center));
                toils.Add(new DoToil((cm, world) => { world.Inquiry.DidWipe(cm); return true; }));
                return new Job(this, "단말에서 정비 기록을 본다", toils) { TargetRoom = room, InterruptMargin = 0.2f };
            }
            case CoverTaskKind.Confess:
            {
                int who = t.Other;
                toils.Add(new GotoToilLate(cm =>
                {
                    var o = w.Crew.FirstOrDefault(x => x.Id == who);
                    if (o == null || o.Dead) return null;
                    var oc = o.Cell;
                    foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
                    {
                        var n = oc + d;
                        if (w.Ship.IsWalkable(n) && !w.IsSpotTaken(n, cm)) return n;
                    }
                    return oc;
                }));
                toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Standing));
                toils.Add(new DoToil((cm, world) => { world.Inquiry.DidConfess(cm); return true; }));
                return new Job(this, "할 말이 있다", toils) { InterruptMargin = 0.2f };
            }
        }
        return null;
    }
}
