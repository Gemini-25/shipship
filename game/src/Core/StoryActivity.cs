using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.16 · v18.17 이야기의 몸짓: 밤 모임에 가서 앉는다 · 할 말이 있는 사람을 찾아간다(설득 · 추궁 · 고백 · 털어놓기 · 중재 · 고비) ·
//   이야기의 장소를 찾아간다(통신실 앞 · 의무실 · 관측 창 · 재배실 …) · 연인 곁에 가 앉는다.
// 경보 · 위기에는 끊기고(점수 0), 위기가 지나면 남은 일을 다시 잡는다 (말할 사람 · 장소는 그대로 남아 있다).

public enum StoryTaskKind : byte { None, Camp, Talk, Visit, Date }

public readonly record struct StoryTask(StoryTaskKind Kind, float Score, string Label, int Other = -1, int RoomId = -1);

public sealed partial class StorySystem
{
    public StoryTask Task(CrewMember c)
    {
        var w = _w;
        if (Off || !Adult(c) || !c.CanAct || c.Outside || c.Mind.Panicking(w.Tick)) return default;
        float hour = SimTime.HourOfDay(w.Tick);
        bool shift = SimTime.InWindow(hour, c.Schedule.WorkStart, c.Schedule.WorkLength) && c.ExcusedUntil <= w.Tick || c.CoveringUntil > w.Tick;
        bool bed = SimTime.InWindow(hour, c.Schedule.SleepStart, c.Schedule.SleepLength);
        float tired = c.Needs.Hunger > 0.7f || c.Needs.Fatigue > 0.85f ? 0.4f : 1f;
        // 할 말이 있다
        if (Talking(c.Id) is TalkIntent t && P(t.Listener) is CrewMember l && !l.Dead)
        {
            float s = t.Kind switch { CardKind.Persuade => 0.78f, CardKind.Mend => 0.58f, CardKind.Mediate => 0.5f, CardKind.Accuse => 0.5f, CardKind.Confess => 0.48f, _ => 0.45f };
            if (shift) s -= t.Kind == CardKind.Persuade ? 0.1f : 0.3f;
            if (bed) s -= 0.35f;
            if (!l.IsAwake) s -= 0.4f;
            return new(StoryTaskKind.Talk, MathF.Max(0f, s * tired), $"{l.Name}에게 할 말이 있다", l.Id);
        }
        // 밤 모임
        if (Camp is CampNight camp && !shift && w.Tick < camp.End - SimTime.Minutes(15))
            return new(StoryTaskKind.Camp, (0.5f + 0.3f * (1f - c.Needs.Social) - (bed ? 0.2f : 0f)) * tired, "밤 모임", -1, camp.RoomId);
        if (shift || bed) return default;
        // 이야기의 장소
        if (ArcOf(c) is Arc a && a.Step + 1 < a.Spec.Steps.Length && a.Spec.Steps[a.Step + 1].Gate == ArcGate.Place && w.Tick - a.StepAt >= SimTime.Hours(a.Spec.Steps[a.Step + 1].Hours / Math.Max(0.1f, Pace) * 0.5f)
            && PlaceRoom(a, c) is Room pr)
            return new(StoryTaskKind.Visit, (0.34f + 0.1f * a.Push) * tired, a.Title, -1, pr.Id);
        // 연인 곁에
        if (LoveOf(c) is Love lv && lv.Together && P(lv.Other(c.Id)) is CrewMember mate && mate.IsAwake && mate.CanAct && mate.Room != c.Room && mate.Room != null
            && (lv.Public || mate.Room.Kind is RoomType.Observatory or RoomType.Garden or RoomType.Lounge or RoomType.Hydroponics))
            return new(StoryTaskKind.Date, 0.26f * tired, $"{mate.Name} 곁에", mate.Id);
        return default;
    }

    /// <summary>찾아간 사람 곁: 닿았으면 카드를 펼친다.</summary>
    public void Arrive(CrewMember c)
    {
        if (Talking(c.Id) is not TalkIntent t || P(t.Listener) is not CrewMember l) return;
        if ((l.Position - c.Position).LengthSquared() > 9f || !l.IsAwake) return;
        Meet(c, t);
    }

    public void Visited(CrewMember c) => Stats.Visits++;
}

public sealed class StoryActivity : Activity
{
    public override string Id => "story";
    public override string Label => "이야기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (StorySystem.Off || c.IsChild || Crisis.Acting(w)) return (0f, "—");
        var t = w.Tales.Task(c);
        if (t.Kind == StoryTaskKind.None) return (0f, "—");
        float s = t.Score + (c.Job?.Activity is StoryActivity ? 0.08f : 0f);
        return (MathF.Max(0f, s), t.Label);
    }

    private static Cell? Seat(World w, CrewMember c, DistanceField dist, Room? room, Vector2 near)
    {
        if (room == null) return null;
        Cell? best = null; float bd = float.MaxValue;
        foreach (var x in room.Cells)
        {
            if (!w.Ship.IsWalkable(x) || !dist.Reachable(x) || w.IsSpotTaken(x, c)) continue;
            float d = (x.Center - near).LengthSquared() + (x.X * 7 + x.Y * 3) % 5 * 0.01f;
            if (d < bd) { bd = d; best = x; }
        }
        return best;
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var t = w.Tales.Task(c);
        var room = t.RoomId >= 0 ? w.Ship.Rooms.FirstOrDefault(r => r.Id == t.RoomId) : null;
        var toils = new List<Toil>();
        switch (t.Kind)
        {
            case StoryTaskKind.Camp:
            {
                if (room == null || Seat(w, c, dist, room, room.Center + new Vector2((c.Id % 5 - 2) * 0.9f, (c.Id / 5 % 3 - 1) * 0.9f)) is not Cell seat) return null;
                toils.AddRange(Plans.DropOff(c, w, dist));
                toils.Add(new GotoToil(seat));
                toils.Add(new WaitToil(SimTime.Hours(1.6f), Pose.Sitting, room.Center)
                {
                    DoneWhen = (cm, world) => world.Tales.Camp == null,
                    EveryTick = (cm, _) => cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.0002f),
                });
                return new Job(this, "밤 모임", toils) { TargetRoom = room, InterruptMargin = 0.25f };
            }
            case StoryTaskKind.Talk:
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
                toils.Add(new DoToil((cm, world) => { world.Tales.Arrive(cm); return true; }));
                var face = w.Crew.FirstOrDefault(x => x.Id == who)?.Position;
                toils.Add(new WaitToil(SimTime.Minutes(6), Pose.Standing, face));
                return new Job(this, t.Label, toils) { InterruptMargin = 0.2f };
            }
            case StoryTaskKind.Visit:
            {
                if (Seat(w, c, dist, room, room?.Center ?? c.Position) is not Cell spot) return null;
                toils.AddRange(Plans.DropOff(c, w, dist));
                toils.Add(new GotoToil(spot));
                toils.Add(new DoToil((cm, world) => { world.Tales.Visited(cm); return true; }));
                toils.Add(new WaitToil(SimTime.Minutes(35), Pose.Sitting, room!.Center));
                return new Job(this, t.Label, toils) { TargetRoom = room, InterruptMargin = 0.2f };
            }
            case StoryTaskKind.Date:
            {
                int who = t.Other;
                toils.Add(new GotoToilLate(cm =>
                {
                    var o = w.Crew.FirstOrDefault(x => x.Id == who);
                    if (o == null || o.Dead) return null;
                    foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
                    {
                        var n = o.Cell + d;
                        if (w.Ship.IsWalkable(n) && !w.IsSpotTaken(n, cm)) return n;
                    }
                    return o.Cell;
                }));
                toils.Add(new WaitToil(SimTime.Minutes(30), Pose.Sitting, null)
                {
                    EveryTick = (cm, world) => { if (world.Tick % 300 == 0 && world.Crew.FirstOrDefault(x => x.Id == who) is CrewMember o && o.Room == cm.Room) cm.ChangeAffinity(o, 0.002f); },
                });
                return new Job(this, t.Label, toils) { InterruptMargin = 0.2f };
            }
        }
        return null;
    }
}
