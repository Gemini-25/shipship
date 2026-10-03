using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>행동 후보 하나에 대한 평가 결과. 인스펙터의 "판단" 목록이 이것이다.</summary>
public readonly record struct Evaluation(Activity Activity, float Score, string Reason);

/// <summary>
/// Utility AI의 행동 후보. Score로 "지금 이게 얼마나 중요한가"를 매기고,
/// 선택되면 Plan으로 구체적인 작업(Job)을 만든다.
/// </summary>
public abstract class Activity
{
    public abstract string Id { get; }
    public abstract string Label { get; }
    private string? _scoreKey, _planKey;
    public string ScoreKey => _scoreKey ??= "score." + Id; // v14.2 구간별 시간
    public string PlanKey => _planKey ??= "plan." + Id;
    public abstract (float score, string reason) Score(CrewMember c, World w, DistanceField dist);
    public abstract Job? Plan(CrewMember c, World w, DistanceField dist);

    protected static float Hour(World w) => SimTime.HourOfDay(w.Tick);

    protected static bool OnShift(CrewMember c, World w) =>
        SimTime.InWindow(Hour(w), c.Schedule.WorkStart, c.Schedule.WorkLength) && c.ExcusedUntil <= w.Tick || c.CoveringUntil > w.Tick; // v14.4 근무를 대신 서 준다

    protected static bool Bedtime(CrewMember c, World w) =>
        SimTime.InWindow(Hour(w), c.Schedule.SleepStart, c.Schedule.SleepLength);
}

// ─────────────────────────────── 식사 ───────────────────────────────

public sealed class EatActivity : Activity
{
    public override string Id => "eat";
    public override string Label => "식사";

    private enum Source { None, Dispenser, Fridge, Ration, Produce }

    private static (Furniture? box, Cell spot, Source src) FindFood(CrewMember c, World w, DistanceField dist)
    {
        var (d, ds) = Plans.NearestContainer(w, dist, c, f =>
            f.Type == FurnitureType.MealDispenser && f.Storage!.Count(ItemKind.Meal) > 0 && f.Machine!.Efficiency > 0f);
        if (d != null) return (d, ds, Source.Dispenser);
        var (fr, fs) = Plans.NearestContainer(w, dist, c, f =>
            f.Type == FurnitureType.Fridge && f.Storage!.Count(ItemKind.Meal) > 0);
        if (fr != null) return (fr, fs, Source.Fridge);
        var (r, rs) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Count(ItemKind.Ration) > 0);
        if (r != null) return (r, rs, Source.Ration);
        // 마지막 수단: 조리할 수 없고 비상식량도 떨어졌으면 채소를 날로 (v7에서 고침: 전에는 채소가 쌓여 있는데 굶었다)
        var (p, ps) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.Fridge && f.Storage!.Count(ItemKind.Produce) > 0);
        if (p != null) return (p, ps, Source.Produce);
        return (null, default, Source.None);
    }

    /// <summary>끼니때인가 (기상 후 0.5h · 6h · 11.5h 전후, 조금이라도 출출하면) — 냄새를 따라가는 것도 끼니를 찾아가는 것이다.</summary>
    public static bool MealTime(CrewMember c, World w)
    {
        if (c.Needs.Hunger <= 0.3f) return false;
        float sinceWake = SimTime.HoursFromTo(c.Schedule.WakeHour, Hour(w));
        return MathF.Abs(sinceWake - 0.5f) < 0.75f || MathF.Abs(sinceWake - 6f) < 0.75f || MathF.Abs(sinceWake - 11.5f) < 0.75f;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var (_, _, src) = FindFood(c, w, dist);
        if (src == Source.None) return (0f, "먹을 것이 없음");
        float hunger = c.Needs.Hunger;
        float score = Curve.Smooth(hunger, 0.35f, 0.9f) * 1.1f;
        string reason = hunger > 0.8f ? "몹시 배고픔" : hunger > 0.5f ? "배고픔" : "아직 배부름";
        // 굶어 쓰러질 지경이면 자다가도 깬다 (사고 뒤 끼니를 놓친 채 잠든 경우)
        if (hunger > 0.93f)
        {
            score += 0.6f;
            reason = "굶주림";
        }

        // 기상 후 0.5h, 6h, 11.5h가 식사 시간. 잠들기 1시간 전쯤 출출하면 야식.
        float sinceWake = SimTime.HoursFromTo(c.Schedule.WakeHour, Hour(w));
        if (MealTime(c, w))
        {
            score += 0.35f;
            reason += " · 식사 시간";
        }
        if (MathF.Abs(sinceWake - 15f) < 0.6f && hunger > 0.4f)
        {
            score += 0.3f;
            reason += " · 자기 전 야식";
        }
        if (src != Source.Ration && src != Source.Produce && w.Cooking.HomeCraving(c) is float home and > 0f) { score += home; reason += " · 고향 음식이 있다"; } // v16.8
        if (src == Source.Ration) reason += " · 비상식량뿐";
        if (src == Source.Produce) reason += " · 날채소뿐";
        // 자는 중에는 웬만큼 배고파서는 깨지 않는다
        if (c.Pose == Pose.Sleeping && hunger < 0.85f) score *= 0.6f;
        return (score, reason);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (box, spot, src) = FindFood(c, w, dist);
        if (box == null) return null;
        var kind = src switch { Source.Ration => ItemKind.Ration, Source.Produce => ItemKind.Produce, _ => ItemKind.Meal };

        var seat = w.Ship.RoomsOf(RoomType.Mess).Where(r => !r.OffLimits).SelectMany(r => r.Furniture)
            .Where(f => f.Type == FurnitureType.Seat && f.ReservedBy == null && dist.Reachable(f.UseSpots[0])
                        && !w.IsSpotTaken(f.UseSpots[0], c))
            .OrderBy(f => (f.Center - box.Center).LengthSquared() + w.Coop.Queues.SeatBias(c, f) + w.After.SeatBias(c, f)) // v17.4 줄에서 다툰 사람 곁은 피하고 양보해 준 사람 곁으로 · v17.5 떠난 사람의 의자 · 구석
            .OrderBy(f => (f.Center - box.Center).LengthSquared() + w.Coop.Queues.SeatBias(c, f) + w.Info.SeatBias(c, f)) // v17.3 늘 앉던 자리 · 친한 사람 · 소음 · 조명 · 다툰 사람 · v17.4 줄에서 다툰 사람 곁은 피하고 양보해 준 사람 곁으로
            .FirstOrDefault();
        var away = w.After.EatAway(c, seat, dist) ?? w.Drains.EatAway(c, seat, dist); // v17.5 묵은 그을음 냄새 · 혼자 먹기 → 다른 방 · 선실 · v18.3 하수 냄새
        if (away != null) seat = null;

        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new QueueToil(QueueKind.Meal, box, spot)); // v17.4 배식 줄
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(2), Pose.Standing, box.Center));
        toils.Add(new TakeToil(box, kind, 1));
        toils.Add(new DoToil((cm, world) =>
        {
            cm.Carrying = null; // 받아 들었으면 바로 먹을 준비
            cm.Stats.Meals++;
            // v11.2: 균이 든 식사 — 40분쯤 뒤에 탈이 난다 (먹다가 불려 가도)
            if (cm.CarryTaint > 0 && cm.PoisonAt < 0) { cm.PoisonAt = world.Tick + SimTime.Minutes(40); cm.PoisonSource = box; }
            cm.CarryTaint = 0;
            world.Cooking.Serve(cm, box, kind); // v16.8 어느 냄비의 몇 도짜리 접시인가
            if (kind == ItemKind.Ration) world.FoodSources.Note(FoodSrc.Stored, 1); // v16.22 저장 식량을 뜯었다
            return true;
        }));
        toils.AddRange(w.Cooking.ReheatToils()); // v16.8 식었으면 데운다 (전기가 모자라면 그냥)
        toils.AddRange(w.After.PauseToils(c, seat, dist)); // v17.5 떠난 사람의 빈자리 앞에서 잠깐
        if (seat != null) toils.Add(new GotoToilLate(cm => w.Coop.Queues.SeatFor(cm, seat))); // v17.4 받고 나서 다시 본다 (방금 다툰 사람 곁이면 떨어진 자리로)
        if (away != null) toils.AddRange(w.After.AwayToils(away)); // v17.5

        var table = seat == null ? null : seat.Room.Furniture.Where(f => f.Type == FurnitureType.Table)
            .OrderBy(f => (f.Center - seat.Center).LengthSquared()).FirstOrDefault();
        bool ration = kind is ItemKind.Ration or ItemKind.Produce;
        float fill = (kind == ItemKind.Produce ? 0.5f : ration ? 0.7f : 0.95f) * w.Motions.MealShare(c); // v18.18 재판에서 배급을 깎였다
        int eatTicks = SimTime.Minutes(ration ? 10 : 25);
        toils.Add(new WaitToil(eatTicks + SimTime.Minutes(8), seat != null || away != null ? Pose.Sitting : Pose.Standing,
            away?.Face ?? table?.Center ?? box.Center, minTicks: SimTime.Minutes(ration ? 8 : 15))
        {
            EveryTick = (cm, _) =>
            {
                cm.Needs.Food += fill / eatTicks;
                if (ration) cm.Needs.Stress += 0.02f / eatTicks;
            },
            DoneWhen = (cm, _) => cm.Needs.Food >= 0.99f,
        });

        var job = new Job(this, kind == ItemKind.Produce ? "날채소" : ration ? "비상식량" : "식사", toils)
        {
            LogText = kind == ItemKind.Produce ? "먹을 게 없어 채소를 날로 씹는다" : ration ? "식사가 없어 비상식량을 뜯는다" : $"식사하러 {Ko.EuRo(box.Room.Name)} 간다",
            TargetRoom = box.Room,
            InterruptMargin = 0.35f,
        };
        if (seat != null) job.Reserve(seat, c);
        return job;
    }
}

// ─────────────────────────────── 수면 ───────────────────────────────

public sealed class SleepActivity : Activity
{
    public override string Id => "sleep";
    public override string Label => "수면";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        float fatigue = c.Needs.Fatigue;
        string reason = fatigue > 0.85f ? "탈진 직전" : fatigue > 0.6f ? "피곤함" : "기력 충분";
        bool ownBed = c.Bed != null && dist.Reachable(c.Bed.UseSpots[0]);
        if (!ownBed) reason += " · 침대에 갈 수 없어 다른 데서";
        if (Bedtime(c, w))
            return (0.5f + Curve.Smooth(fatigue, 0.1f, 0.6f) * 0.5f, reason + " · 취침 시간");
        return (MathF.Max(0f, Curve.Smooth(fatigue, 0.6f, 0.95f) - 0.05f), reason);
    }

    /// <summary>
    /// 잘 곳: 내 침대 → 비어 있는 다른 침대 → 휴게실·식당 같은 안전한 방의 바닥 (임시 침실).
    /// 침실을 포기하면 승무원들은 휴게실에서 자게 되고, 그게 우주선의 새 모습이 된다.
    /// </summary>
    private static (Cell spot, Room? room, string how)? FindBed(CrewMember c, World w, DistanceField dist)
    {
        bool Off(Room r) => r.OffLimits || r.Abandoned || CosmicEvacuateActivity.InLine(r, w); // v18.13 비우고 봉쇄한 구획 · 파편이 지나갈 방에서는 자지 않는다
        if (c.Bed != null && !Off(c.Bed.Room) && dist.Reachable(c.Bed.UseSpots[0])) return (c.Bed.UseSpots[0], c.Bed.Room, "");
        foreach (var bed in w.Ship.FurnitureOf(FurnitureType.Bed))
        {
            var s = bed.UseSpots[0];
            if (bed.ReservedBy == null && !Off(bed.Room) && dist.Reachable(s) && !w.IsSpotTaken(s, c)) return (s, bed.Room, "남의 침대에서");
        }
        RoomType[] prefer = { RoomType.Lounge, RoomType.Mess, RoomType.Medbay, RoomType.Bridge, RoomType.Workshop };
        foreach (var type in prefer)
        foreach (var room in w.Ship.RoomsOf(type))
        {
            if (Off(room) || room.Leaking || Atmosphere.Danger(room) > 0.1f) continue;
            foreach (var cell in room.Cells)
                if ((w.Ship.IsOpenFloor(cell) || w.Ship.FurnitureAt(cell)?.Type == FurnitureType.Seat) && dist.Reachable(cell) && !w.IsSpotTaken(cell, c))
                    return (cell, room, $"침실에 갈 수 없어 {room.Name}에서");
        }
        return null;
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (FindBed(c, w, dist) is not var (spot, room, how)) return null;

        bool fellAsleep = false;
        // 위기 중 탈진: 두 시간 쪽잠만 (기운이 조금 돌아오면 다시 나간다)
        bool nap = Crisis.Acting(w);
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Hours(nap ? 2 : 12), Pose.Sleeping, minTicks: SimTime.Minutes(30))
        {
            EveryTick = (_, _) => fellAsleep = true,
            DoneWhen = (cm, world) => nap
                ? cm.Needs.Rest > 0.35f
                : !SimTime.InWindow(SimTime.HourOfDay(world.Tick), cm.Schedule.SleepStart, cm.Schedule.SleepLength) && cm.Needs.Rest > 0.6f,
        });
        return new Job(this, nap || how.Length > 0 ? "쪽잠" : "수면", toils)
        {
            LogText = nap ? "비상 중 탈진 — 두 시간만 눈을 붙인다" : how.Length > 0 ? $"{how} 잔다" : "잠자리에 든다",
            TargetRoom = room,
            InterruptMargin = 0.45f,
            OnFinished = (cm, world, _) =>
            {
                if (fellAsleep) world.Log.Add(world.Tick, LogKind.Life, "잠에서 깼다", cm.Id);
            },
        };
    }
}

// ─────────────────────────────── 당직 ───────────────────────────────

/// <summary>할 일이 따로 없을 때 자기 근무지 설비 곁에서 감시·기록하는 당직.</summary>
public sealed class DutyActivity : Activity
{
    public override string Id => "duty";
    public override string Label => "당직";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.IsChild) return (0f, "아이는 당직을 서지 않는다"); // v12.9
        float score;
        string reason;
        if (OnShift(c, w))
        {
            score = 0.3f + 0.15f * c.Traits.Diligence;
            reason = "근무 시간";
        }
        else
        {
            score = 0.05f * c.Traits.Diligence;
            reason = "근무 외 시간";
        }
        score -= 0.25f * c.Needs.Stress;
        if (c.Needs.Stress > 0.5f) reason += " · 스트레스로 의욕 저하";
        // 통합: 당직은 할 일이 따로 없을 때 — 붙을 만한 일이 있으면 그 일보다 앞서지 않는다 (배가 커져 일터가 멀어지자 고장 난 로봇을 두고 반나절 당직만 섰다)
        if (score > 0.05f && OnShift(c, w) && ChoresActivity.BestScore(c, w, dist) is float job && job > 0.12f && job < score + 0.01f) { score = job * 0.9f; reason += " · 할 일이 있다"; }
        return (MathF.Max(0f, score), reason);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var candidates = new List<(Furniture f, Cell spot)>();
        foreach (var roomType in c.Stations)
        foreach (var room in w.Ship.RoomsOf(roomType).Where(r => !r.OffLimits))
        foreach (var f in room.Furniture)
        {
            if (!(FurnitureTypes.IsDutyStation(f.Type) || f.Machine != null) || !f.IsFreeFor(c)) continue;
            var spots = f.UseSpots.Where(sp => dist.Reachable(sp) && !w.IsSpotTaken(sp, c)).ToList();
            if (spots.Count > 0) candidates.Add((f, w.Rng.Pick(spots)));
        }
        if (candidates.Count == 0) return null;

        var (target, spot) = w.Rng.Pick(candidates);
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(w.Rng.Range(50f, 120f)), Pose.Working, target.Center));
        var job = new Job(this, "당직", toils)
        {
            LogText = $"{target.Room.Name}에서 당직을 선다",
            TargetRoom = target.Room,
            Target = target,
        };
        return job.Reserve(target, c);
    }
}

// ─────────────────────────────── 휴식 ───────────────────────────────

public sealed class RelaxActivity : Activity
{
    public override string Id => "relax";
    public override string Label => "휴식";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var s = c.Schedule;
        float hour = Hour(w);
        float stress = c.Needs.Stress;
        float score = 0.08f + 0.6f * stress;
        string reason = stress > 0.5f ? "스트레스 해소 필요" : "여가";

        float workEnd = s.WorkStart + s.WorkLength;
        if (SimTime.InWindow(hour, workEnd, SimTime.HoursFromTo(workEnd, s.SleepStart)))
        {
            score += 0.15f;
            reason += " · 퇴근 후";
        }
        if (Bedtime(c, w)) score -= 0.2f;
        return (MathF.Max(0f, score), reason);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        Furniture? best = null;
        float bestScore = float.MinValue;
        foreach (var seat in w.Ship.FurnitureOf(FurnitureType.Seat))
        {
            if (seat.ReservedBy != null) continue;
            var spot = seat.UseSpots[0];
            int d = dist.Get(spot);
            if (d < 0 || w.IsSpotTaken(spot, c)) continue;
            var hobby = c.Hobbies.Count > 0 ? Persona.HobbyIn(c, seat.Room) : null; // v14.0 취미의 방 (도서실·체육관·관측실·공방…)
            if (seat.Room.Type is not (RoomType.Lounge or RoomType.Mess) && hobby == null || seat.Room.OffLimits) continue;

            float value = seat.Room.Type == RoomType.Lounge ? 1f : seat.Room.Type == RoomType.Mess ? 0.4f : 0.5f;
            if (hobby != null) value += 0.6f;
            value *= AmbienceSystem.RelaxFactor(seat.Room); // v12.6 관측실·정원은 더 끌린다, 시끄러운 곳은 덜
            value += 0.5f * (0.6f - c.Fitness) * Facilities.Factor(seat.Room, "exercise"); // 몸이 굳었으면 운동하러
            value += 0.8f * c.Memory.Trauma * Facilities.Factor(seat.Room, "grief"); // 마음이 무거우면 기도실로
            foreach (var o in w.Crew)
            {
                if (o == c || (o.Position - seat.Center).Length() > 3f) continue;
                value += (c.Traits.Sociability - 0.5f) * 0.6f + c.AffinityTo(o) * 0.5f;
            }
            value -= d / 600f;
            value += w.Rng.Range(0f, 0.3f);
            if (value > bestScore) { bestScore = value; best = seat; }
        }
        if (best == null) return null;
        var chosen = best;

        var table = chosen.Room.Furniture.Where(f => f.Type == FurnitureType.Table)
            .OrderBy(f => (f.Center - chosen.Center).LengthSquared()).FirstOrDefault();
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(chosen.UseSpots[0]));
        toils.Add(new WaitToil(SimTime.Minutes(w.Rng.Range(40f, 90f)), Pose.Sitting,
            table?.Center ?? chosen.Room.Center, minTicks: SimTime.Minutes(20))
        {
            // 곁에 앉은 사람과는 말없이도 조금씩 가까워진다
            EveryTick = (cm, world) =>
            {
                const float dt = 1f / SimTime.TicksPerHour;
                foreach (var o in world.Crew)
                {
                    if (o == cm || !o.IsAwake || (o.Position - cm.Position).Length() > 2.5f) continue;
                    cm.Needs.Social += 0.12f * dt;
                    cm.ChangeAffinity(o, (Persona.Shared(cm, o) != null ? 0.012f : 0.006f) * dt); // v14.0 같은 취미면 더 빨리 가까워진다
                }
            },
            DoneWhen = (cm, _) => cm.Needs.Stress < 0.03f,
        });
        var hb = c.Hobbies.Count > 0 ? Persona.HobbyIn(c, chosen.Room) : null;
        if (hb != null) w.Life.Stats.HobbyRests++;
        var job = new Job(this, hb is Hobby hby ? Persona.Of(hby).Name : "휴식", toils)
        {
            LogText = hb is Hobby hbl ? $"{chosen.Room.Name}에서 {Persona.Of(hbl).Doing}" : $"{chosen.Room.Name}에서 쉰다",
            TargetRoom = chosen.Room,
        };
        return job.Reserve(chosen, c);
    }
}

// ─────────────────────────────── 서성이기 ───────────────────────────────

public sealed class WanderActivity : Activity
{
    public override string Id => "wander";
    public override string Label => "산책";

    private static readonly RoomType[] Places =
        { RoomType.Corridor, RoomType.Lounge, RoomType.Mess, RoomType.Bridge, RoomType.Hydroponics };

    public override (float, string) Score(CrewMember c, World w, DistanceField dist) => (0.1f, "할 일 없음");

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var rooms = w.Ship.Rooms.Where(r => Places.Contains(r.Type) && !r.OffLimits && !r.Abandoned).ToList();
        if (rooms.Count == 0) return null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var room = w.Rng.Pick(rooms);
            var cell = w.Rng.Pick(room.Cells);
            if (!w.Ship.IsOpenFloor(cell) || !dist.Reachable(cell) || w.IsSpotTaken(cell, c)) continue;
            var toils = Plans.DropOff(c, w, dist);
            toils.Add(new GotoToil(cell));
            toils.Add(new WaitToil(SimTime.Minutes(w.Rng.Range(5f, 20f)), Pose.Standing));
            return new Job(this, "산책", toils) { TargetRoom = room };
        }
        return null;
    }
}

// ─────────────────────────────── 대피 ───────────────────────────────

/// <summary>
/// 숨쉬기 힘들거나, 너무 뜨겁거나, 불이 가까운 곳에서 빠져나간다.
/// 우주복을 입고 있으면 기압·산소 문제는 무섭지 않다 — 탱크가 바닥나기 전까지는.
/// </summary>
public sealed class EvacuateActivity : Activity
{
    public override string Id => "evacuate";
    public override string Label => "대피";

    public static float DangerHere(CrewMember c, World w)
    {
        if (c.Room == null) return 0f;
        float danger = Atmosphere.DangerFor(c, c.Room);
        // 사출하기로 한 방: 그 방 일(물품 회수 등)을 하는 사람 말고는 나간다
        if (c.Room.Jettison != null && c.Job?.Order?.Target.Room != c.Room) danger = MathF.Max(danger, 0.5f);
        // v13.0 소화 대응 카운트다운: 질식·진공 소화를 앞둔 방 — 모두 나간다
        if (c.Room.EvacuateBy >= 0 || c.Room.Purging || c.Room.Inerting) danger = MathF.Max(danger, c.Room.Purging || c.Room.Inerting || w.Automation.Trusts.Obeys(c) ? 1f : 0.35f); // v16.6 컴퓨터를 아주 못 믿으면 대피 지시를 바로 따르지 않는다
        if (c.Suit is { Oxygen: > 0f and < 0.4f } && Atmosphere.Danger(c.Room) > 0.3f) danger = 1f; // 탱크가 바닥나 간다
        // v12.9.1 맨몸으로 산소가 묽어지는 방에 있으면 일을 두고 나온다 (머리가 먼저 흐려진다 — 쓰러지기 전에)
        // v13.2 방침(대피 기준): 일찍 17 · 보통 16.5 · 버티며 작업 15
        float leave = w.Policies["evac"] switch { 0 => 17f, 2 => 15f, _ => 16.5f };
        if (c.Suit is not { Oxygen: > 0.05f } && c.Room.Air.O2 < leave) danger = MathF.Max(danger, 0.6f + (leave - c.Room.Air.O2) * 0.1f);
        // v9: 새는 냉각수의 증기 (그 관을 고치러 온 사람은 각오하고 버틴다)
        if (c.Job?.Order?.Target.Pipe == null)
        {
            float steam = w.Piping.SteamAt(c.Cell);
            if (steam > 0.1f) danger = MathF.Max(danger, 0.3f + 0.4f * steam);
        }
        // v10.1: 운석이 이 방으로 온다는 경보 (궤적을 읽은 경보만 — 구하러 뛰어든 사람도 일단 빠진다)
        if (w.Sensors.Threat(c.Room) is IncomingMeteor inc && inc.MinutesLeft(w.Tick) > 0f)
            danger = MathF.Max(danger, 0.45f + 0.25f * MathF.Min(1.5f, inc.Size));
        danger = MathF.Max(danger, w.Perils.HeatDanger(c)); // v16.26 몸에 열이 차오른다 — 어지러워지기 전에 나간다
        if (c.Dashing) danger *= 0.3f; // 이미 각오하고 뛰어들었다
        bool armed = c.Carrying?.Kind == ItemKind.Extinguisher;
        if (w.Fire.AnyWithin(c.Cell, 2.2f)) danger = MathF.Max(danger, armed ? 0.3f : 0.75f);
        else if (w.Fire.IsKnown(c.Room) && w.Fire.CountIn(c.Room) > 0) danger = MathF.Max(danger, armed ? 0.1f : 0.4f); // 같은 방에 불
        return danger;
    }

    /// <summary>v12.9.1 우주복 없이 산소 14kPa 아래 방에 있다 (몇 분 안에 쓰러진다).</summary>
    public static bool Breathless(CrewMember c, World w) =>
        c.Room != null && c.Suit is not { Oxygen: > 0.05f } && c.Room.Air.O2 < (w.Policies["evac"] switch { 0 => 15f, 2 => 13f, _ => 14f }) && c.CarryingPerson == null && !c.Dashing;

    private static bool Safe(CrewMember c, World w, Room room) =>
        Atmosphere.Danger(room) <= 0.1f && !room.Leaking && w.Fire.CountIn(room) == 0 && w.Sensors.Threat(room) == null
        && room.EvacuateBy < 0 && !room.ResponseHold && w.Brain2.Beliefs.SafeEnough(c, room); // v16.15 위험하다고 믿는 방(불 · 구멍 · 컴퓨터 경고)은 피한다

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        // v8: 선체 밖 — EVA 일을 하는 중이 아니면 에어락으로 돌아온다 (튕겨 나갔거나, 일이 끊겼거나)
        if (c.Outside)
        {
            bool working = c.Job?.Order is WorkOrder o && (o.External || o.Kind == WorkKind.Rescue) && c.Suit is { Oxygen: > 0.6f };
            // v10.1: 운석 경보 — 궤적을 읽은 경보면 선체 밖 일을 두고 돌아온다
            if (w.Sensors.Alarm is IncomingMeteor inc && inc.Warned >= WarnLevel.Manual && inc.MinutesLeft(w.Tick) > 0f && c.CarryingPerson == null && !w.EvaRisk.Staying(c)) // v16.11 그늘 · 마저 끝내기
                return (1.3f, $"운석 경보 — {inc.MinutesLeft(w.Tick):0.#}분 뒤 · 에어락으로");
            if (working) return (0f, "선체 밖 작업 중");
            float s = c.Suit == null ? 1.5f : c.Suit.Oxygen < 0.6f ? 1.35f : 0.95f;
            return (s, c.Suit == null ? "선체 밖 — 우주복 없음" : c.Suit.Oxygen < 0.6f ? "선체 밖 — 우주복 산소가 바닥난다" : "선체 밖 — 에어락으로 돌아간다");
        }
        if (c.Room == null) return (0f, "—");
        // v12.9.1 맨몸으로 숨이 찰 만큼 산소가 묽다 — 무슨 일이든 두고 당장 나간다 (사람을 업고 있거나 각오하고 뛰어든 사람은 빼고)
        if (Breathless(c, w)) return (3f, $"{c.Room.Name} 산소 {c.Room.Air.O2:0}kPa — 숨이 차다, 당장 나간다");
        float danger = DangerHere(c, w);
        if (danger < 0.2f) return (0f, "안전함");
        float fear = 1.2f - 0.4f * c.Traits.Bravery;
        string why = w.Fire.AnyWithin(c.Cell, 2.2f) ? "불이 가까움"
            : w.Fire.CountIn(c.Room) > 0 ? "같은 방에 불"
            : c.Room.Leaking ? "공기가 새고 있음"
            : "공기 위험";
        return ((0.55f + danger) * fear, $"{c.Room.Name} {why} {danger * 100:0}%");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        Cell? best = null;
        int bestCost = int.MaxValue;
        // v13.0 공기 구역이 있으면 그 구역부터 (닿을 수 없으면 아무 안전한 방)
        for (int pass = w.Automation.ZoneActive ? 0 : 1; pass < 2 && best == null; pass++)
            foreach (var room in w.Ship.Rooms)
            {
                if (room == c.Room || room.Detached || !Safe(c, w, room) || pass == 0 && !w.Automation.InZone(room)) continue;
                foreach (var cell in room.Cells)
                {
                    int d = dist.Get(cell);
                    if (d < 0 || d >= bestCost || !w.Ship.IsOpenFloor(cell) || w.IsSpotTaken(cell, c) || w.Piping.SteamAt(cell) > 0.1f) continue;
                    best = cell;
                    bestCost = d;
                }
            }
        // 안전한 방이 없거나 갈 수 없으면, 적어도 불에서 떨어진 칸으로
        if (best == null && c.Room != null && w.Fire.AnyWithin(c.Cell, 1))
        {
            foreach (var cell in c.Room.Cells)
            {
                int d = dist.Get(cell);
                if (d < 0 || d >= bestCost || !w.Ship.IsOpenFloor(cell) || w.Fire.AnyWithin(cell, 2)) continue;
                best = cell;
                bestCost = d;
            }
        }
        if (best is not Cell target) return null;
        var toils = new List<Toil>
        {
            new GotoToil(target),
            // v13.1 조를 맡은 사람은 숨을 고르면(2분) 바로 제 일로 돌아간다
            new WaitToil(SimTime.Minutes(20), Pose.Standing, null, SimTime.Minutes(2))
            {
                DoneWhen = (cm, world) => world.Command.Active && world.Command.TeamOf(cm) is { Kind: not TeamKind.Reserve } && DangerHere(cm, world) < 0.2f,
            },
        };
        return new Job(this, "대피", toils)
        {
            LogText = $"{c.Room?.Name ?? "선체 밖"}에서 대피한다",
            LogKind = LogKind.Warning,
            TargetRoom = w.Ship.RoomAt(target),
            Urgent = true,
            InterruptMargin = 0.4f,
        };
    }
}

// ─────────────────────────────── 우주복 벗기 ───────────────────────────────

/// <summary>일이 끝나고 안전한 곳이면 우주복을 보관함에 돌려놓는다 (보관함에서 산소를 다시 채운다).</summary>
public sealed class StowSuitActivity : Activity
{
    public override string Id => "stowsuit";
    public override string Label => "우주복 반납";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Suit == null || c.Room == null) return (0f, "우주복 없음");
        if (Atmosphere.Danger(c.Room) > 0.1f || c.Room.Leaking) return (0f, "아직 위험한 곳");
        float s = 0.7f + (c.Suit.Oxygen < 1f ? 0.25f : 0f);
        return (s, $"우주복 산소 {c.Suit.Oxygen:0.0}시간 남음");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        // 보관함에 갈 수 없으면 (격벽이 잠겼거나) 벗지 않고 그대로 입고 있는다 — 우주복을 아무 데나 두지 않는다
        var (locker, spot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.SuitLocker && f.Storage!.Free > 0);
        if (locker == null) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Working, locker.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (cm.Suit == null) return true;
            if (cm.Room is Room rr && (Atmosphere.Danger(rr) > 0.1f || rr.Leaking) && !CrisisCrewSystem.Off) return false; // v16.21 와 보니 공기가 빠지는 중 — 벗지 않는다
            if (locker.Storage!.Add(ItemKind.Suit, 1) == 0) return false;
            world.EvaRisk.OnStow(cm, locker); // v16.11 상한 우주복은 수리 대기
            world.Log.Add(world.Tick, LogKind.Work, "우주복을 벗어 보관함에 걸었다", cm.Id);
            cm.Suit = null;
            return true;
        }));
        return new Job(this, "우주복 반납", toils) { TargetRoom = locker.Room };
    }
}

/// <summary>
/// v8: 우주복 산소가 바닥나 가는데 벗고 쉴 만큼 안전한 곳이 없다 (배 전체가 숨이 막힌다, 진공 속 일이 남았다).
/// 보관함에서 공기 탱크의 공기로 우주복을 다시 채운다 — 탱크가 비면 이것도 끝이다.
/// </summary>
public sealed class RefillSuitActivity : Activity
{
    public const float Cost = 30f;
    public override string Id => "refillsuit";
    public override string Label => "우주복 산소 보충";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Suit == null || c.Outside || c.Room == null || c.Down) return (0f, "—");
        if (c.Suit.Oxygen >= 0.8f) return (0f, $"우주복 산소 {c.Suit.Oxygen:0.0}시간");
        // 벗을 수 있으면 벗는다 (반납하면 보관함이 채워 둔다)
        if (Atmosphere.Danger(c.Room) <= 0.1f && !c.Room.Leaking) return (0f, "벗으면 된다");
        if (w.Air.Reserve < Cost) return (0f, "공기 탱크가 비었다");
        var (locker, _) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.SuitLocker);
        if (locker == null) return (0f, "갈 수 있는 보관함이 없다");
        float s = c.Suit.Oxygen < 0.4f ? 1.45f : 1.2f;
        return (s, $"우주복 산소 {c.Suit.Oxygen * 60:0}분 — 공기 탱크에서 채운다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (locker, spot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.SuitLocker);
        if (locker == null) return null;
        var toils = new List<Toil>
        {
            new GotoToil(spot),
            new WaitToil(SimTime.Minutes(3), Pose.Working, locker.Center),
            new DoToil((cm, world) =>
            {
                if (cm.Suit == null) return true;
                if (world.Air.Reserve < Cost) return false;
                world.Air.Reserve -= Cost;
                cm.Suit.Oxygen = SuitState.TankHours;
                world.SuitRefills++;
                world.Log.Add(world.Tick, LogKind.Work, "공기 탱크에서 우주복 산소를 채웠다", cm.Id);
                return true;
            }),
        };
        return new Job(this, "우주복 산소 보충", toils) { TargetRoom = locker.Room };
    }
}

// ─────────────────────────────── 회복 ───────────────────────────────

/// <summary>체력이 떨어지면 의무실 치료 침대에 눕는다.</summary>
public sealed class RecoverActivity : Activity
{
    public override string Id => "recover";
    public override string Label => "치료";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        float h = c.Vitals.Health;
        float injury = c.Vitals.Injury;
        float byHealth = h < 0.75f ? (0.75f - h) * 2.2f : 0f;
        float byInjury = injury > 0.25f ? (injury - 0.25f) * 1.2f : 0f; // 크게 다쳤으면 누워서 낫는다
        float byIll = c.Fx.Bed > 0.35f ? (c.Fx.Bed - 0.2f) * 1.3f : 0f; // v14.1 누워야 낫는 병
        if (RadBed(c, w)) byIll = MathF.Max(byIll, 0.97f); // 통합6 7Sv 넘게 쬔 몸: 토하고 기운이 없어 의무실에 눕는다 (간호 · 수혈을 받을 자리)
        if (byIll > MathF.Max(byHealth, byInjury)) return (byIll, RadBed(c, w) ? $"방사선 병 — 피폭 {c.Dose:0.0}Sv · 토하고 기운이 없다" : $"앓는다 — {w.Ailments.Line(c)}");
        if (byHealth <= 0f && byInjury <= 0f) return (0f, "건강함");
        return (MathF.Max(byHealth, byInjury), injury > 0.05f ? $"체력 {h * 100:0}% · 부상 {injury * 100:0}% ({c.Vitals.InjuryCause})" : $"체력 {h * 100:0}%");
    }

    /// <summary>통합6 골수가 무너진 몸 (7Sv 넘고 고비를 아직 못 넘김).</summary>
    public static bool RadBed(CrewMember c, World w) => w.Perils.RadStage(c) >= 2 && w.RadCare.Of(c)?.Stable != true;

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var bed = w.Ship.FurnitureOf(FurnitureType.MedBed)
            .Where(b => b.ReservedBy == null && dist.Reachable(b.UseSpots[0]))
            .OrderBy(b => dist.Get(b.UseSpots[0])).FirstOrDefault();
        if (bed == null) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(bed.UseSpots[0]));
        toils.Add(new WaitToil(SimTime.Hours(8), Pose.Sleeping, minTicks: SimTime.Hours(1))
        {
            EveryTick = (cm, world) =>
            {
                if (bed.Machine!.Efficiency > 0f) cm.Vitals.Health += 0.12f * world.Perils.MarrowMul(cm) / SimTime.TicksPerHour; // 통합6 골수가 무너진 몸은 침대도 기운을 못 채운다
            },
            DoneWhen = (cm, world) => cm.Vitals.Health >= MathF.Min(0.92f, cm.Vitals.MaxHealth - 0.02f) && cm.Vitals.Injury < 0.25f && cm.Fx.Bed < 0.25f && !RadBed(cm, world),
        });
        var job = new Job(this, "치료", toils)
        {
            LogText = "의무실 치료 침대에 눕는다",
            TargetRoom = bed.Room,
            InterruptMargin = 0.35f,
        };
        return job.Reserve(bed, c);
    }
}

/// <summary>아무것도 못 할 때 잠깐 멈춰 서 있기.</summary>
public static class IdleJob
{
    public static Job Create(int ticks = 0) =>
        new(null, "대기", new Toil[] { new WaitToil(ticks > 0 ? ticks : SimTime.Minutes(10), Pose.Standing) });
}

// ─────────────────────────────── v12.6 방사선 대피 ───────────────────────────────

/// <summary>태양 폭풍의 양성자 비: 바깥벽 방에 있으면 대피소(없으면 창고 선반 뒤, 그것도 없으면 배 안쪽 방)로 가서 기다린다.</summary>
public sealed class ShelterActivity : Activity
{
    public override string Id => "shelter";
    public override string Label => "방사선 대피";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Outside || c.Room == null || w.Ambience.StormPower < 0.3f) return (0f, "—");
        if (c.Room.Radiation < 0.2f)
            return c.Job?.Activity is ShelterActivity ? (0.95f, "태양 폭풍이 지나가길 기다린다")
                : Facilities.Factor(c.Room, "shelter") > 0f ? (0.92f, $"태양 폭풍 — {c.Room.Name}에서 지나가길 기다린다 (밖은 쬔다)") // v16.9 대피소에 닿았으면 폭풍이 갈 때까지 머문다 (방송대로 온 사람도)
                : (0f, "여기는 괜찮다");
        return (0.8f + 0.4f * c.Room.Radiation, $"태양 폭풍 — {c.Room.Name} 방사선 {c.Room.Radiation * 100:0}%");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        // 이미 덜 쬐는 곳: 그 자리에서 기다린다 (졸리면 앉은 채 존다)
        if (c.Room is Room here && here.Radiation < 0.2f)
            return new Job(this, "방사선 대피", new List<Toil> { new WaitToil(SimTime.Minutes(30), c.Needs.Rest < 0.35f ? Pose.Sleeping : Pose.Sitting) }) { TargetRoom = here };
        var (shelter, factor) = Facilities.Best(w.Ship, "shelter", r => !r.Detached && !r.OffLimits && !r.Leaking);
        var rooms = shelter != null ? new List<Room> { shelter } : new List<Room>();
        rooms.AddRange(w.Ship.Rooms.Where(r => !r.Detached && !r.OffLimits && !r.Leaking && r != c.Room && r.Radiation < c.Room!.Radiation - 0.1f).OrderBy(r => r.Radiation));
        foreach (var room in rooms)
        {
            Cell? best = null;
            int bestCost = int.MaxValue;
            foreach (var cell in room.Cells)
            {
                int d = dist.Get(cell);
                if (d < 0 || d >= bestCost || !w.Ship.IsOpenFloor(cell) || w.IsSpotTaken(cell, c)) continue;
                best = cell;
                bestCost = d;
            }
            if (best is not Cell target) continue;
            string why = room == shelter && factor >= 1f ? "대피소로" : room == shelter ? $"{room.Name} 선반 뒤로 (대피소가 없다)" : $"안쪽 {Ko.EuRo(room.Name)}";
            return new Job(this, "방사선 대피", new List<Toil> { new GotoToil(target), new WaitToil(SimTime.Minutes(40), Pose.Sitting) })
            {
                LogText = $"태양 폭풍 — {why}",
                LogKind = LogKind.Warning,
                TargetRoom = room,
                Urgent = true,
                InterruptMargin = 0.3f,
            };
        }
        return null;
    }
}

// ─────────────────────────────── v12.6 격리 ───────────────────────────────

/// <summary>옮는 병에 걸려 열이 나면 격리실로 가서 쉰다 (격리실이 없으면 이 행동은 없다 — 의무실·침실에서 앓는다).</summary>
public sealed class QuarantineActivity : Activity
{
    public override string Id => "quarantine";
    public override string Label => "격리";

    private static Room? Ward(World w) => Facilities.Best(w.Ship, "quarantine", r => !r.Detached && !r.OffLimits && !r.Leaking) is (Room r, >= 1f) ? r : null;

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Outside || c.Room == null || !DiseaseSystem.Sick(c) || w.Disease.Severity(c) < 0.08f) return (0f, "—");
        if (Ward(w) is not Room ward) return (0f, "격리실 없음");
        return c.Room == ward ? (0.9f, "격리실에서 앓는다") : (0.95f, "열이 난다 — 격리실로");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Ward(w) is not Room ward) return null;
        Cell? best = null;
        int bestCost = int.MaxValue;
        foreach (var cell in ward.Cells)
        {
            int d = dist.Get(cell);
            if (d < 0 || d >= bestCost || !w.Ship.IsOpenFloor(cell) && w.Ship.FurnitureAt(cell)?.Type != FurnitureType.MedBed || w.IsSpotTaken(cell, c)) continue;
            best = cell;
            bestCost = d;
        }
        if (best is not Cell target) return null;
        var toils = new List<Toil>();
        if (c.Cell != target) toils.Add(new GotoToil(target));
        toils.Add(new WaitToil(SimTime.Hours(2), Pose.Sleeping));
        return new Job(this, "격리", toils)
        {
            LogText = c.Room == ward ? null : "열이 난다 — 옮기 전에 격리실로 간다",
            TargetRoom = ward,
        };
    }
}
