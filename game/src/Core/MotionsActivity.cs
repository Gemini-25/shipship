using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.18 회의를 둘러싼 몸짓: 서명 종이를 들고 찾아가 설득 · 회의 자리에 둘러앉기(재판석 · 의장석) · 벌 근무 · 몰래 꺼내 먹기 · 잔치.

/// <summary>서명 받기: 낸 사람(과 거드는 사람)이 종이를 들고 한 사람씩 찾아가 부탁한다.</summary>
public sealed class PetitionActivity : Activity
{
    public override string Id => "petition";
    public override string Label => "서명 받기";

    private static Motion? Mine(CrewMember c, World w)
    {
        foreach (var m in w.Motions.All)
            if (m.Stage == MotionStage.Signing && m.Carriers.Contains(c.Id) && w.Tick < m.Deadline) return m;
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (MotionSystem.Off || c.IsChild || !c.CanAct || c.Outside || Crisis.Acting(w) || Mine(c, w) is not Motion m) return (0f, "—");
        if (c.Job?.Activity is PetitionActivity) return (0.7f, "서명을 받는 중");
        bool urgent = m.Sitting == SittingKind.Emergency;
        if (Bedtime(c, w) && !urgent) return (0f, "서명은 내일 받는다");
        float s = 0.5f + 0.12f * c.Traits.Sociability + (urgent ? 0.25f : 0f) + (m.Signers.Count + 1 >= m.Need ? 0.08f : 0f);
        // 통합8 서명 기한이 다가올수록 마음이 급해진다 (일상 거리 · 꾸밈에 밀려 이틀 동안 서명 하나 못 받고 안건이 흐지부지됐다)
        if (m.Deadline > m.Born) s += 0.25f * Math.Clamp((w.Tick - m.Born) / (float)(m.Deadline - m.Born), 0f, 1f);
        if (OnShift(c, w) && !urgent) s -= 0.22f;
        if (w.Motions.NextAsk(c, m) is not CrewMember o) return (0f, "서명을 부탁할 사람이 없다");
        return (s, $"'{m.Title}' — {o.Name}에게 서명을 받으러 간다 ({m.Signers.Count}/{m.Need})");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Mine(c, w) is not Motion m || w.Motions.NextAsk(c, m) is not CrewMember o || !dist.Reachable(o.Cell)) return null;
        var toils = Plans.DropOff(c, w, dist);
        Approach(w, toils, o);
        toils.Add(new DoToil((cm, world) =>
        {
            if (o.Dead || !o.IsAwake || (o.Position - cm.Position).LengthSquared() > 9f || m.Stage != MotionStage.Signing) return true;
            Locomotion.Face(cm, o.Position);
            world.Motions.Ask(cm, o, m);
            return true;
        }));
        toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Standing, null) { EveryTick = (cm, _) => cm.Facing = Vector2.Normalize(o.Position - cm.Position + new Vector2(0.0001f, 0f)) });
        return new Job(this, "서명 받기", toils) { LogText = $"'{m.Title}' — {o.Name}에게 서명을 받으러 간다", LogKind = LogKind.Life, TargetRoom = o.Room, InterruptMargin = 0.2f };
    }

    internal static void Approach(World w, List<Toil> toils, CrewMember o)
    {
        Cell? Near(CrewMember cm) => Cell.Dirs8.Select(d => o.Cell + d).Where(x => w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - cm.Position).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).Cast<Cell?>().FirstOrDefault();
        toils.Add(new GotoToilLate(Near));
        toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Standing, null) { DoneWhen = (cm, world) => (o.Position - cm.Position).LengthSquared() <= 6f || o.Dead });
        toils.Add(new GotoToilLate(cm => (o.Position - cm.Position).LengthSquared() <= 6f ? null : Near(cm)));
    }
}

/// <summary>따로 연 회의(긴급 · 재판 · 선거 · 조사 · 잔치 의논): 부름을 받으면 모여 둘러앉는다 — 의장은 윗자리, 고발당한 사람은 재판석에 선다.</summary>
public sealed class SittingActivity : Activity
{
    public override string Id => "sitting";
    public override string Label => "회의";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var mo = w.Motions;
        if (MotionSystem.Off || !mo.Summoned(c) || mo.Now is not Sitting s || c.Down || c.Outside) return (0f, "—");
        if (!s.Venue.Cells.Any(dist.Reachable)) return (0f, "회의 자리에 갈 수 없다");
        if (c.Job?.Activity is SittingActivity) return (0.96f, MotionSystem.SittingName(s.Kind));
        float score = 0.86f + 0.08f * c.Traits.Diligence + (c.Id == s.Motion.Target ? 0.05f : 0f);
        if (MathF.Abs(s.Motion.Initial.TryGetValue(c.Id, out var op0) ? op0 : mo.Opinion(c, s.Motion).Item1) >= 0.3f) score += 0.12f; // 통합7 내 몫이 걸린 안건(배급을 줄이자에 배고픈 사람)엔 배가 고파도 먼저 간다 — 끼니에 밀려 반대할 사람이 빠진 채 표결하던 것
        // 원망이 깊은 사람은 늦게 온다
        if (mo.GrudgeOf(c) is Grudge g && g.Against == s.Motion.Proposer) score -= 0.1f;
        return (score, $"{MotionSystem.SittingName(s.Kind)} — {s.Venue.Name}에 모인다");
    }

    /// <summary>회의 자리: 고발당한 사람은 방 끝(재판석), 선장은 윗자리, 나머지는 의자부터 가운데 둘레로.</summary>
    public static Cell? SeatFor(CrewMember c, Sitting s, World w, DistanceField dist, out bool chair, out Furniture? seat)
    {
        chair = false;
        seat = null;
        var v = s.Venue;
        var floor = v.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(x => x.X * 1000 + x.Y).ToList();
        if (floor.Count == 0) return null;
        var center = v.Center;
        if (c.Id == s.Motion.Target && s.Kind == SittingKind.Trial)
            return floor.OrderByDescending(x => (x.Center - center).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).First();
        if (c.Id == w.Command.CaptainId && s.Kind != SittingKind.Election)
        {
            chair = true;
            return floor.OrderBy(x => x.Y).ThenBy(x => MathF.Abs(x.Center.X - center.X)).ThenBy(x => x.X).First();
        }
        seat = v.Furniture.Where(f => f.Type == FurnitureType.Seat && f.ReservedBy == null && f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0]) && !w.IsSpotTaken(f.UseSpots[0], c))
            .OrderBy(f => (f.UseSpots[0].Center - center).LengthSquared()).ThenBy(f => f.Id).FirstOrDefault();
        if (seat != null) return seat.UseSpots[0];
        return floor.OrderBy(x => (x.Center - center).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).First();
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Motions.Now is not Sitting s) return null;
        if (SeatFor(c, s, w, dist, out bool chair, out var seat) is not Cell spot) return null;
        bool dock = c.Id == s.Motion.Target && s.Kind == SittingKind.Trial;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(90), dock ? Pose.Standing : Pose.Sitting, s.Venue.Center)
        {
            DoneWhen = (cm, world) => !world.Motions.Summoned(cm),
        });
        var job = new Job(this, MotionSystem.SittingName(s.Kind), toils)
        {
            LogText = dock ? $"재판 — {Ko.EuRo(s.Venue.Name)} 불려 간다" : $"{MotionSystem.SittingName(s.Kind)} — {Ko.EuRo(s.Venue.Name)} 간다{(chair ? " (의장석)" : "")}",
            LogKind = LogKind.Life, TargetRoom = s.Venue, InterruptMargin = 0.3f,
        };
        if (seat != null) job.Reserve(seat, c);
        return job;
    }
}

/// <summary>벌 근무: 재판이 정한 시간만큼 식당을 닦고 설거지를 한다 (안 하면 다시 안건이 된다).</summary>
public sealed class PenaltyDutyActivity : Activity
{
    public override string Id => "penaltyduty";
    public override string Label => "벌 근무";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (MotionSystem.Off || !w.Motions.Duty.TryGetValue(c.Id, out var d) || c.IsChild || !c.CanAct || c.Outside || Crisis.Acting(w)) return (0f, "—");
        if (c.Job?.Activity is PenaltyDutyActivity) return (0.75f, "벌 근무 중");
        if (c.Mind.Anger > 0.5f && c.Traits.Diligence < 0.45f) return (0f, "억울해서 벌 근무를 미룬다");
        if (Bedtime(c, w)) return (0f, "—");
        float left = (d.due - w.Tick) / (float)SimTime.Hours(1);
        float s = 0.45f + 0.2f * c.Traits.Diligence + (left < 8f ? 0.2f : 0f) - (OnShift(c, w) ? 0.25f : 0f);
        return (s, $"벌 근무 — {d.hours:0.#}시간 남았다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var spot = w.Ship.FurnitureOf(FurnitureType.MealDispenser).Concat(w.Ship.FurnitureOf(FurnitureType.Stove)).Concat(w.Ship.FurnitureOf(FurnitureType.Table))
            .Where(f => !f.Room.Abandoned && f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0]) && !w.IsSpotTaken(f.UseSpots[0], c)).OrderBy(f => dist.Get(f.UseSpots[0])).FirstOrDefault();
        if (spot == null) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot.UseSpots[0]));
        toils.Add(new WaitToil(SimTime.Hours(1), Pose.Working, spot.Center, minTicks: SimTime.Hours(1)));
        toils.Add(new DoToil((cm, world) => { world.Motions.DidDuty(cm, 1f); return true; }));
        return new Job(this, "벌 근무", toils) { LogText = "벌 근무 — 식당 청소 · 설거지", LogKind = LogKind.Life, TargetRoom = spot.Room, InterruptMargin = 0.25f, AlwaysLog = true };
    }
}

/// <summary>몰래 꺼내 먹기: 배급이 줄어 배고픈 사람이 아무도 없는 틈에 창고로 간다 — 먹는 동안 누가 들어오면 그 사람이 본다.</summary>
public sealed class SneakFoodActivity : Activity
{
    public override string Id => "sneakfood";
    public override string Label => "잠깐 창고에";

    private static List<Furniture>? _boxes;
    private static long _boxesAt = -1;
    private static World? _boxesW;

    private static List<Furniture> Boxes(World w)
    {
        if (_boxes == null || _boxesW != w || w.Tick - _boxesAt > SimTime.Hours(1) || w.Tick < _boxesAt)
        {
            _boxes = w.Ship.Containers.Where(f => !f.Room.Abandoned && f.UseSpots.Count > 0 && f.Storage != null && (f.Storage.Count(ItemKind.Ration) > 0 || f.Storage.Count(ItemKind.Meal) > 0)).OrderBy(f => f.Id).ToList();
            _boxesAt = w.Tick;
            _boxesW = w;
        }
        return _boxes;
    }

    private static Furniture? Pick(CrewMember c, World w, DistanceField dist)
    {
        Furniture? best = null;
        int bd = int.MaxValue;
        foreach (var f in Boxes(w))
        {
            if (f.Storage == null || f.Storage.Count(ItemKind.Ration) + f.Storage.Count(ItemKind.Meal) == 0 || !dist.Reachable(f.UseSpots[0])) continue;
            bool eyes = false;
            foreach (var o in w.Crew) if (o != c && !o.Dead && o.IsAwake && o.Room == f.Room) { eyes = true; break; }
            if (eyes) continue;
            int d = dist.Get(f.UseSpots[0]);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (MotionSystem.Off || !w.Motions.Tempted(c) || c.IsChild || !c.CanAct || !c.IsAwake || c.Outside || Crisis.Acting(w) || c.Needs.Hunger < 0.35f) return (0f, "—");
        if (c.Job?.Activity is SneakFoodActivity) return (0.8f, "—");
        if (Pick(c, w, dist) is null) return (0f, "보는 눈이 있다");
        return (0.66f + 0.2f * c.Needs.Hunger, "배가 고프다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Pick(c, w, dist) is not Furniture box) return null;
        Theft? t = null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(box.UseSpots[0]));
        toils.Add(new DoToil((cm, world) => { t = world.Motions.Steal(cm, box); return true; }));
        toils.Add(new WaitToil(SimTime.Minutes(6), Pose.Standing, box.Center)
        {
            EveryTick = (cm, world) =>
            {
                if (t == null || world.Tick % 20 != 0) return;
                foreach (var o in world.Crew) if (o != cm && !o.Dead && o.IsAwake && o.Room == box.Room) world.Motions.See(o, t);
            },
        });
        return new Job(this, "잠깐 창고에", toils) { LogText = $"{Ko.EuRo(box.Room.Name)} 간다", LogKind = LogKind.Life, TargetRoom = box.Room, InterruptMargin = 0.3f };
    }
}

/// <summary>잔치: 회의가 정한 저녁, 정한 방에 모여 함께 먹고 웃는다.</summary>
public sealed class FeastActivity : Activity
{
    public override string Id => "feast";
    public override string Label => "잔치";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var mo = w.Motions;
        if (MotionSystem.Off || !mo.Feasting || mo.FeastRoom is not Room r || c.Down || c.Outside || !c.IsAwake || c.IsChild && c.Age < 4f) return (0f, "—");
        if (c.Job?.Activity is FeastActivity) return (0.8f, "잔치 중");
        if (!r.Cells.Any(dist.Reachable)) return (0f, "—");
        return (0.58f + 0.15f * c.Traits.Sociability - (OnShift(c, w) ? 0.25f : 0f) - (mo.GrudgeOf(c) is not null ? 0.08f : 0f), $"잔치 — {r.Name}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Motions.FeastRoom is not Room r) return null;
        var spot = r.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(x => (x.Center - r.Center).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell s) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(s));
        toils.Add(new WaitToil(SimTime.Hours(2), Pose.Sitting, r.Center)
        {
            DoneWhen = (cm, world) => !world.Motions.Feasting,
            EveryTick = (cm, _) => cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.0004f),
        });
        return new Job(this, "잔치", toils) { LogText = $"잔치 — {Ko.EuRo(r.Name)} 간다", LogKind = LogKind.Life, TargetRoom = r, InterruptMargin = 0.25f };
    }
}
