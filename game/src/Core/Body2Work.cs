using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.1 몸의 변화 — 행동: 이발(자르는 사람 · 앉는 사람 · 혼자) · 우주복 치수 조정 · 달리기(몸 관리) · 떨어진 머리카락 치우기.

/// <summary>이발: 덥수룩해 보이는 친한 사람(또는 부탁한 사람)을 의자에 앉히고 잘라 준다. 손님 쪽도 이 활동으로 앉아 기다린다.</summary>
public sealed class HaircutActivity : Activity
{
    public override string Id => "haircut";
    public override string Label => "이발";

    /// <summary>머리를 맡길 만큼 한가한가 (쉬는 · 서성이는 · 이야기하는 · 취미 중).</summary>
    public static bool Available(CrewMember o, World w) =>
        o.IsAwake && o.CanAct && !o.Outside && o.Room != null && o.Vitals.Health > 0.4f && o.Job?.Urgent != true
        && (o.Job?.Activity is RelaxActivity or WanderActivity or ChatActivity or HobbyActivity or HoldActivity or SchemeActivity or null
            || o.Mind.Goal == GoalTier.Life && o.Job?.Activity is not (SleepActivity or EatActivity or RecoverActivity or SceneActivity) && !w.Society.OnNightWatch(o)) // 통합6 쉬는 시간의 일(꾸미는 일 · 실험 · 운동 · 퍼즐)도 잠깐 내려놓고 앉을 수 있다 — 잠 · 끼니 · 치료 · 함께하는 장면은 아니다
        && w.Body2.SessionOf(o) == null;

    private static (CrewMember? who, float value, string why) Pick(CrewMember c, World w, DistanceField dist)
    {
        var b2 = w.Body2;
        if (b2.AskedBy(c, out var asked) && asked != null && Available(asked, w) && !b2.RefusedRecently(c, asked) && dist.Reachable(asked.Cell))
            return (asked, 0.55f, $"{Ko.IGa(asked.Name)} 머리를 잘라 달라고 했다");
        var bl = b2.Of(c);
        CrewMember? best = null;
        float bestV = 0f;
        foreach (var o in w.Crew)
        {
            if (o == c || o.Dead) continue;
            float aff = c.AffinityTo(o);
            if (aff < 0.25f && !(o.IsChild && o.Parents.Contains(c.Id))) continue; // 친한 사람 · 자기 아이
            if (b2.Seen(c, o) is not LookImpression imp || w.Tick - imp.Tick > SimTime.TicksPerDay * 2) continue;
            var ol = b2.Of(o);
            if (imp.HairCm < BodyLook.ShagAt(ol.StyleCm) && imp.Uneven < 0.6f) continue; // 내가 본 그 사람은 덥수룩하지 않다
            if (!Available(o, w) || b2.RefusedRecently(c, o) || !dist.Reachable(o.Cell)) continue;
            float v = 0.25f * aff + 0.1f * MathF.Min(1f, imp.HairCm / BodyLook.ShagAt(ol.StyleCm) - 0.9f) + 0.12f * bl.Barber + 0.06f * c.Traits.Sociability
                      - (bl.Barber < 0.2f ? 0.08f : 0f) + (imp.Uneven >= 0.6f ? 0.06f : 0f) - dist.Get(o.Cell) / 4000f;
            if (v > bestV) { bestV = v; best = o; }
        }
        return best == null ? (null, 0f, "—") : (best, bestV, $"{best.Name}의 머리가 덥수룩해 보인다");
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!Body2System.Enabled) return (0f, "—");
        var b2 = w.Body2;
        if (b2.Sessions.Count > 0 && b2.SessionOf(c) is HairSession s)
        {
            if (Crisis.Acting(w) || EvacuateActivity.DangerHere(c, w) > 0.3f) { b2.Cancel(s); return (0f, "—"); } // 사고가 나면 가위를 내려놓는다
            return s.Client == c.Id && !s.Self ? (1.5f, "머리를 자르러 앉는다") : s.Barber == c.Id && c.Job?.Activity is HaircutActivity ? (0.85f, "머리를 자르는 중") : (0f, "—");
        }
        if (c.IsChild || !c.CanAct || c.Outside || Bedtime(c, w) || c.Vitals.Injury > 0.5f || Crisis.Acting(w)) return (0f, "—");
        var (who, value, why) = Pick(c, w, dist);
        if (who == null)
        {
            var l = b2.Of(c);
            if (l.ShaggySince >= 0 && w.Tick - l.ShaggySince > SimTime.TicksPerDay * 4 && !OnShift(c, w)) return (0.3f, "아무도 안 잘라 줘서 혼자 자른다");
            return (0f, "—");
        }
        float score = 0.22f + value - (OnShift(c, w) ? 0.25f : 0f);
        return (MathF.Max(0f, score), why);
    }

    private static Cell? Beside(World w, Cell at, CrewMember me, DistanceField dist)
    {
        Cell? best = null;
        int bc = int.MaxValue;
        foreach (var d in Cell.Dirs8)
        {
            var x = at + d;
            int k = dist.Get(x);
            if (k < 0 || k >= bc || !w.Ship.IsOpenFloor(x) || w.IsSpotTaken(x, me)) continue;
            best = x; bc = k;
        }
        return best;
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var b2 = w.Body2;
        // ── 손님: 의자에 가서 앉아 기다린다 ──
        if (b2.SessionOf(c) is HairSession cs)
        {
            if (cs.Client != c.Id || cs.Self) return null;
            var seatF = cs.SeatFurniture >= 0 ? w.Ship.Furniture.FirstOrDefault(f => f.Id == cs.SeatFurniture) : null;
            var toils = Plans.DropOff(c, w, dist);
            toils.Add(new GotoToil(cs.Seat));
            toils.Add(new WaitToil(SimTime.Minutes(90), Pose.Sitting, null)
            {
                EveryTick = (cm, world) =>
                {
                    cs.Seated = (cm.Position - cs.Seat.Center).LengthSquared() < 0.8f;
                    if (cs.Seated) cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.6f / SimTime.TicksPerHour);
                },
                DoneWhen = (cm, world) => cs.Done || cs.Canceled,
            });
            var job = new Job(this, "머리 자르는 중", toils)
            {
                LogText = b2.Crew(cs.Barber) is CrewMember bb ? $"{bb.Name}에게 머리를 맡긴다" : null,
                OnFinished = (cm, world, st) => { if (!cs.Done) world.Body2.Cancel(cs); },
            };
            return seatF != null && seatF.ReservedBy == null ? job.Reserve(seatF, c) : job;
        }
        // ── 혼자 자르기 (거울 앞) ──
        var (client, _, _) = Pick(c, w, dist);
        if (client == null)
        {
            var l = b2.Of(c);
            if (l.ShaggySince < 0) return null;
            var room = c.HomeBed?.Room ?? c.Room;
            if (room == null || CosmicCrew.SpotIn(room, c, w, dist) is not Cell spot) return null;
            var toils = Plans.DropOff(c, w, dist);
            toils.Add(new GotoToil(spot));
            toils.Add(new WaitToil(SimTime.Minutes(25), Pose.Working, null));
            toils.Add(new DoToil((cm, world) =>
            {
                var ses = world.Body2.Begin(cm, cm, cm.Cell, null, true);
                world.Body2.Finish(ses, cm, cm);
                return true;
            }));
            return new Job(this, "혼자 이발", toils) { LogText = "덥수룩한 머리를 혼자 자른다", TargetRoom = room };
        }
        // ── 자르는 사람 ──
        Furniture? seat = null;
        int bestCost = int.MaxValue;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.Seat))
        {
            if (f.ReservedBy != null || f.UseSpots.Count == 0 || f.Room.OffLimits) continue;
            if (f.Room.Type is not (RoomType.Quarters or RoomType.Lounge or RoomType.Mess or RoomType.QuietQuarters or RoomType.PrivateCabins or RoomType.Gym or RoomType.Laundry or RoomType.Garden)) continue;
            var u = f.UseSpots[0];
            int d = dist.Get(u);
            if (d < 0 || w.IsSpotTaken(u, client)) continue;
            int cost = d + 40 * (Math.Abs(u.X - client.Cell.X) + Math.Abs(u.Y - client.Cell.Y));
            if (cost < bestCost) { bestCost = cost; seat = f; }
        }
        var seatCell = seat?.UseSpots[0] ?? client.Cell;
        if (Beside(w, seatCell, c, dist) is not Cell stand) return null;
        bool tidy = c.Traits.Diligence > 0.6f || Life.Has(c, Habit.NeatFreak);
        HairSession? session = null;
        var list = Plans.DropOff(c, w, dist);
        list.Add(new GotoToilLate(cm => client.Room == null ? null : Cell.Dirs8.Select(d => client.Cell + d).Where(x => w.Ship.IsWalkable(x)).Cast<Cell?>().FirstOrDefault()));
        list.Add(new DoToil((cm, world) =>
        {
            if (!Available(client, world) || (client.Position - cm.Position).LengthSquared() > 12f) return false;
            if (world.Body2.Refuses(cm, client, out _)) return false;
            if (seat != null && seat.ReservedBy != null) return false;
            session = world.Body2.Begin(cm, client, seatCell, seat, false);
            cm.Say(world, Persona.Say(cm, world.Body2.Of(client).Uneven >= 0.6f ? "그 머리 내가 다듬어 줄게, 앉아 봐" : "머리 많이 자랐다 — 잘라 줄까? 이리 앉아"));
            client.NextThinkTick = world.Tick; // 손님이 바로 의자로
            return true;
        }));
        list.Add(new GotoToil(stand));
        float speed = (0.7f + 0.6f * b2.Of(c).Barber) * Wounds.HandFactor(c.Vitals);
        list.Add(new WaitToil(SimTime.Minutes(80), Pose.Working, seatCell.Center)
        {
            EveryTick = (cm, world) =>
            {
                if (session == null) return;
                if (session.Seated && client.Job?.Activity is HaircutActivity && (client.Position - cm.Position).LengthSquared() < 4.5f)
                {
                    session.Progress += speed / SimTime.Minutes(30);
                    cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.5f / SimTime.TicksPerHour);
                }
            },
            DoneWhen = (cm, world) => session == null || session.Canceled || session.Progress >= 1f || world.Tick - session.Start > SimTime.Minutes(35) && !session.Seated,
        });
        list.Add(new DoToil((cm, world) =>
        {
            if (session == null || session.Canceled || session.Progress < 1f) { world.Body2.Cancel(session); return false; }
            world.Body2.Finish(session, cm, client);
            return true;
        }));
        if (tidy)
        {
            list.Add(new WaitToil(SimTime.Minutes(5), Pose.Working, seatCell.Center));
            list.Add(new DoToil((cm, world) =>
            {
                if (world.Body2.Clips.LastOrDefault(x => x.Barber == cm.Id && x.Client == client.Id) is HairClip clip) world.Body2.Swept(clip, cm);
                return true;
            }));
        }
        var room2 = seat?.Room ?? client.Room;
        return new Job(this, "이발", list)
        {
            LogText = $"{client.Name}의 머리를 잘라 준다",
            TargetRoom = room2,
            OnFinished = (cm, world, st) => { if (session != null && !session.Done) world.Body2.Cancel(session); },
        };
    }
}

/// <summary>우주복 치수 조정: 보관함 앞에서 어깨끈 · 몸통 고리 · 목 고리를 지금 체중에 맞춘다 (컴퓨터가 올렸거나 입어 보고 알았다).</summary>
public sealed class SuitFitActivity : Activity
{
    public override string Id => "suitfit";
    public override string Label => "우주복 치수 조정";

    private static Furniture? Locker(World w, DistanceField dist) =>
        w.Ship.FurnitureOf(FurnitureType.SuitLocker).Where(f => f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0])).OrderBy(f => dist.Get(f.UseSpots[0])).FirstOrDefault();

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!Body2System.Enabled || c.IsChild || !c.CanAct || c.Outside || c.Suit != null || Bedtime(c, w)) return (0f, "—");
        var l = w.Body2.Peek(c);
        if (l == null || !(l.FitOrder || l.FitKnown) || MathF.Abs(l.Misfit) < 3f || Crisis.Acting(w)) return (0f, "—");
        if (Locker(w, dist) == null) return (0f, "보관함에 닿을 수 없다");
        float s = 0.38f + (l.FitOrder ? 0.2f : 0f) + 0.04f * MathF.Min(5f, MathF.Abs(l.Misfit) - Body2System.FitWarnKg);
        return (s, l.FitOrder ? "주 컴퓨터가 우주복 치수 조정을 올렸다" : l.Misfit > 0 ? "우주복이 꽉 낀다" : "우주복이 헐렁하다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var locker = Locker(w, dist);
        if (locker == null) return null;
        var spot = locker.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).First();
        float progress = 0f;
        float rate = (0.6f + 0.8f * c.SkillLevel(Skill.Mechanics)) * Wounds.HandFactor(c.Vitals) / SimTime.Minutes(40);
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(90), Pose.Working, locker.Center)
        {
            EveryTick = (cm, world) => progress += rate,
            DoneWhen = (cm, world) => progress >= 1f,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (progress < 1f) return false;
            if (world.Ship.CountStored(ItemKind.Thread) > 0) Life.Take(world, ItemKind.Thread, 1); // 덧댄 끈을 꿰맨다 (없으면 매듭으로)
            world.Body2.Adjusted(cm);
            cm.Practice(Skill.Mechanics, 0.004f);
            return true;
        }));
        return new Job(this, "우주복 치수 조정", toils) { LogText = "우주복 치수를 지금 몸에 맞춘다", LogKind = LogKind.Work, TargetRoom = locker.Room, Target = locker };
    }
}

/// <summary>몸 관리: 체중이 늘었다는 말 · 컴퓨터 권고 · 운동 습관 → 땀방 러닝머신, 없으면 복도를 달린다.</summary>
public sealed class JogActivity : Activity
{
    public override string Id => "jog";
    public override string Label => "달리기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!Body2System.Enabled || c.IsChild || !c.CanAct || c.Outside || Bedtime(c, w) || OnShift(c, w)) return (0f, "—");
        var l = w.Body2.Peek(c);
        if (l == null || w.Tick - w.Body2.LastJog(c) < SimTime.Hours(9)) return (0f, "—");
        if (c.Needs.Rest < 0.35f || c.Needs.Food < 0.3f || c.Vitals.Health < 0.6f || Wounds.LegFactor(c.Vitals) < 0.8f || Crisis.Acting(w)) return (0f, "—");
        float gain = l.Kg - l.StartKg;
        float want = w.Brain2.Goals.Has(c, "body:trim") ? 0.42f + (Life.Has(c, Habit.GymRat) ? 0.12f : 0f) : Life.Has(c, Habit.GymRat) && gain > 2f ? 0.3f : 0f; // 통합8 운동을 좋아하는 사람이 마음먹었으면 미루지 않는다 (0.42로는 영화 · 밤 모임에 밀려 이틀 동안 한 번도 안 뛰었다)
        if (want <= 0f) return (0f, "—");
        return (want + 0.03f * MathF.Max(0f, gain), gain > 0f ? $"몸이 {gain:0.0}kg 불었다" : "몸을 움직이기로 했다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var b2 = w.Body2;
        var toils = Plans.DropOff(c, w, dist);
        var mill = w.Ship.FurnitureOf(FurnitureType.Treadmill).Where(f => f.ReservedBy == null && f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0]))
            .OrderBy(f => dist.Get(f.UseSpots[0])).FirstOrDefault();
        if (mill != null)
        {
            toils.Add(new GotoToil(mill.UseSpots[0]));
            toils.Add(new WaitToil(SimTime.Minutes(35), Pose.Working, mill.Center)
            {
                EveryTick = (cm, world) =>
                {
                    cm.Fitness = MathF.Min(1f, cm.Fitness + 0.08f / SimTime.TicksPerHour);
                    cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.06f / SimTime.TicksPerHour);
                },
            });
            toils.Add(new DoToil((cm, world) => { world.Body2.Jogged(cm); return true; }));
            return new Job(this, "달리기", toils) { LogText = $"{mill.Room.Name} 러닝머신에서 달린다", TargetRoom = mill.Room, Target = mill }.Reserve(mill, c);
        }
        // 복도 왕복: 가장 먼 복도 칸까지 갔다가 돌아온다 (두 번)
        Cell? far = null;
        int fd = -1;
        foreach (var r in w.Ship.RoomsOf(RoomType.Corridor))
        {
            if (r.Detached || r.OffLimits) continue;
            foreach (var cell in r.Cells)
            {
                int d = dist.Get(cell);
                if (d > fd && d < 4000 && w.Ship.IsOpenFloor(cell)) { fd = d; far = cell; }
            }
        }
        if (far is not Cell f2 || fd < 300) return null;
        var home = c.Cell;
        bool turned = false, done = false;
        toils.Add(new GotoToil(f2));
        toils.Add(new DoToil((cm, world) => turned = true));
        toils.Add(new GotoToil(home));
        toils.Add(new DoToil((cm, world) => { done = true; world.Body2.Jogged(cm); return true; }));
        // 반환점을 돌았으면 끊겨도 한 번 달린 셈이다
        return new Job(this, "달리기", toils) { LogText = "복도를 왕복해 달린다", OnFinished = (cm, world, st) => { if (turned && !done) world.Body2.Jogged(cm); } };
    }
}

/// <summary>바닥에 떨어진 머리카락 치우기 (자른 사람 · 깔끔한 사람).</summary>
public sealed class SweepClipsActivity : Activity
{
    public override string Id => "sweepclips";
    public override string Label => "머리카락 치우기";

    private static HairClip? Target(CrewMember c, World w)
    {
        bool neat = Life.Has(c, Habit.NeatFreak) || c.Traits.Diligence > 0.75f;
        foreach (var x in w.Body2.Clips)
            if (x.Barber == c.Id && w.Tick - x.Tick > SimTime.Minutes(20) || neat && c.Room != null && x.RoomId == c.Room.Id) return x;
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Body2.Clips.Count == 0 || c.IsChild || !c.CanAct || c.Outside || !c.IsAwake || Bedtime(c, w)) return (0f, "—");
        var t = Target(c, w);
        if (t == null || !dist.Reachable(t.Cell) || Crisis.Acting(w)) return (0f, "—");
        return (t.Barber == c.Id ? 0.24f : 0.3f, t.Barber == c.Id ? "자른 머리카락이 바닥에 남았다" : "바닥에 머리카락이 흩어져 있다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var t = Target(c, w);
        if (t == null || !dist.Reachable(t.Cell)) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(t.Cell));
        toils.Add(new WaitToil(SimTime.Minutes(5), Pose.Working, t.Cell.Center));
        toils.Add(new DoToil((cm, world) => { if (world.Body2.Clips.Contains(t)) world.Body2.Swept(t, cm); return true; }));
        return new Job(this, "머리카락 치우기", toils) { LogText = "바닥의 머리카락을 쓸어 담는다" };
    }
}

public sealed partial class Body2System
{
    private readonly Dictionary<int, long> _lastJog = new();
    public long LastJog(CrewMember c) => _lastJog.TryGetValue(c.Id, out var t) ? t : -1_000_000;

    public void Jogged(CrewMember c)
    {
        _lastJog[c.Id] = _w.Tick;
        Stats.Jogs++;
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.04f);
        _w.Brain2.Emotions.Feel(c, Feeling.Pride, 0.05f, "땀을 흘렸다");
    }
}
