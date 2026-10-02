using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v11.3 승무원 성장과 회복.
///   배우기(도제): 어떤 솜씨를 가진 사람이 배에 하나뿐이면(0.6 넘게), 평화로울 때 후배 하나가 그 사람 곁에서 일을 따라 하며 배운다 —
///                 선배의 솜씨의 90%까지. 핵심 인력이 쓰러지거나 죽어도 그 일을 할 사람이 남는다 (교차 훈련).
///   재활: 다친 사람이 의무실(없으면 휴게실)에서 하루 한 시간 몸을 푼다 — 부상이 빨리 낫고, 의무관이 곁에 있으면 더.
///   후유증: 절반 넘게 다치면 무엇인가 남는다 (최대 체력·손이 조금 둔하다). 재활로 절반까지만 준다.
/// </summary>
public sealed class GrowthStats
{
    /// <summary>시험용: 배우기를 하지 않는 배.</summary>
    public bool NoTraining { get; set; }

    /// <summary>시험용: 재활을 하지 않는 배.</summary>
    public bool NoRehab { get; set; }
    /// <summary>시험용: 구급 키트가 없을 때 응급 처치도 하지 않는다 (재활만 견줄 때).</summary>
    public bool NoFirstAid { get; set; }
    public int Lessons;
    public int Milestones;
    public int RehabSessions;
}

public sealed partial class WorkBoard
{
    private static readonly Skill[] KeySkills = { Skill.Medicine, Skill.Engineering, Skill.Electrical, Skill.Piloting, Skill.Mechanics, Skill.Botany, Skill.Cooking };

    /// <summary>v11.3: 배우기(평화로울 때, 유일한 전문가 곁에서)와 재활(다친 사람이 스스로).</summary>
    private void ScanGrowth(Poster post)
    {
        var w = _world;
        var alive = w.Crew.Where(c => !c.Dead && !c.Down).ToList();
        // ── 재활 ──
        foreach (var c in w.Growth.NoRehab ? new List<CrewMember>() : alive)
        {
            var v = c.Vitals;
            if (v.Injury < 0.12f && v.Scar <= v.ScarFloor + 0.005f) continue;
            if (v.Injury > 0.55f || v.Health < 0.45f || c.CareBed != null) continue; // 아직 누워 있어야 한다
            if (v.Injury >= 0.3f && v.TreatedTick <= 0) continue; // 치료가 먼저 (가벼운 부상은 바로 재활)
            if (w.Tick - c.LastRehab < SimTime.Hours(20)) continue;
            var spot = RehabRoom(w);
            if (spot == null) continue;
            post(WorkKind.Rehab, WorkTarget.OfCrew(c), 0.45f, Skill.Medicine,
                $"부상 {v.Injury * 100:0}%" + (v.Scar > 0.01f ? $" · 후유증 {v.Scar * 100:0}%" : "") + $" · {spot.Name}에서 한 시간", circuit: c.Id);
        }
        // ── 배우기 ──
        if (w.Growth.NoTraining || !Evolution.Peaceful(w) || alive.Count < 3) return;
        var existing = _open.Values.Where(o => o.Kind == WorkKind.Train).ToList();
        var busy = new HashSet<int>();
        int posted = 0;
        foreach (var skill in KeySkills)
        {
            if (posted >= 2) break;
            var experts = alive.Where(c => c.RawSkill(skill) >= 0.6f).ToList();
            if (experts.Count == 0 || experts.Count >= Math.Max(2, alive.Count / 5)) continue;
            var mentor = experts.OrderByDescending(c => c.RawSkill(skill)).First();
            bool Fits(CrewMember c) => c != mentor && c.RawSkill(skill) < MathF.Min(0.55f, mentor.RawSkill(skill) * 0.85f) && c.Vitals.Injury < 0.3f && !busy.Contains(c.Id);
            // 배우던 사람이 있으면 그 사람이 이어서 (훑을 때마다 바뀌지 않게)
            var prev = existing.FirstOrDefault(o => (Skill)(o.Circuit % 10) == skill && o.Target.Crew == mentor);
            var learner = prev != null ? alive.FirstOrDefault(c => c.Id == prev.Circuit / 10 && Fits(c)) : null;
            learner ??= alive.Where(c => Fits(c) && !existing.Any(o => o.Circuit / 10 == c.Id && (Skill)(o.Circuit % 10) != skill))
                .OrderByDescending(c => 0.5f * c.RawSkill(skill) + 0.3f * c.Traits.Diligence + 0.2f * c.AffinityTo(mentor) + 0.6f * Overlap(c, mentor))
                .FirstOrDefault();
            if (learner == null) continue;
            busy.Add(learner.Id);
            post(WorkKind.Train, WorkTarget.OfCrew(mentor), 0.42f, skill,
                $"{Ko.EulReul(Skills.Name(skill))} 할 줄 아는 사람이 {mentor.Name}뿐 · {learner.Name} {learner.RawSkill(skill) * 100:0}% → 곁에서 따라 하며 배운다",
                circuit: learner.Id * 10 + (int)skill);
            posted++;
        }
    }

    /// <summary>둘이 함께 깨어 있는 시간의 몫 (0~1) — 잠드는 시각이 엇갈리면 배울 틈이 없다.</summary>
    private static float Overlap(CrewMember a, CrewMember b)
    {
        int both = 0;
        for (int h = 0; h < 24; h++)
            if (!SimTime.InWindow(h + 0.5f, a.Schedule.SleepStart, a.Schedule.SleepLength) && !SimTime.InWindow(h + 0.5f, b.Schedule.SleepStart, b.Schedule.SleepLength)) both++;
        return both / 16f;
    }

    /// <summary>재활할 곳: 의무실 → 휴게실.</summary>
    internal static Room? RehabRoom(World w) =>
        w.Ship.RoomsOf(RoomType.Medbay).Concat(w.Ship.RoomsOf(RoomType.Lounge))
            .FirstOrDefault(r => !r.Detached && !r.Abandoned && !r.OffLimits && !r.Leaking && Atmosphere.Danger(r) < 0.1f && w.Fire.CountIn(r) == 0);
}

public static partial class WorkPlanners
{
    /// <summary>배우기: 선배가 일하는 곁으로 가서 따라 한다 (선배는 하던 일을 계속한다).</summary>
    private static Job? Train(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var mentor = o.Target.Crew!;
        var skill = (Skill)(o.Circuit % 10);
        if (mentor.Dead || mentor.Down || mentor.Outside || mentor.Pose == Pose.Sleeping) { blocked = $"{Ko.IGa(mentor.Name)} 가르칠 수 없다"; return null; }
        if (mentor.Job?.Urgent == true) { blocked = $"{Ko.IGa(mentor.Name)} 급한 일 중"; return null; }
        // 선배 곁 빈 칸
        var near = Cell.Dirs8.Select(d => mentor.Cell + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (near is not Cell spot) { blocked = "곁에 설 자리가 없다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WorkToil(1.2f, skill, mentor.Position)
        {
            CanContinue = (cm, world) => !mentor.Dead && !mentor.Down && mentor.Pose != Pose.Sleeping && mentor.Job?.Urgent != true
                                          && (mentor.Position - cm.Position).LengthSquared() < 16f,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            float teacher = mentor.RawSkill(skill), before = cm.RawSkill(skill);
            float cap = teacher * 0.9f;
            if (before >= cap) return true;
            float gain = 0.035f * (teacher - before + 0.2f) * (0.7f + 0.6f * cm.Traits.Diligence);
            cm.SkillLevels[(int)skill] = MathF.Min(cap, before + gain);
            mentor.Practice(skill, 0.005f);
            cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.1f);
            mentor.Needs.Social = MathF.Min(1f, mentor.Needs.Social + 0.08f);
            cm.ChangeAffinity(mentor, 0.05f);
            mentor.ChangeAffinity(cm, 0.04f);
            cm.Stats.Lessons++;
            mentor.Stats.Taught++;
            world.Relations.Remember(cm, mentor, RelationReason.TaughtMe, $"{Ko.EulReul(Skills.Name(skill))} 가르쳐 줬다"); // v14.4
            world.Growth.Lessons++;
            world.Culture.OnLesson(mentor, cm); // v14.9 선배의 관행이 제자에게
            float after = cm.RawSkill(skill);
            foreach (float mark in new[] { 0.35f, 0.5f })
                if (before < mark && after >= mark)
                {
                    world.Growth.Milestones++;
                    string what = mark < 0.4f ? "기본은 한다" : "혼자서도 맡을 수 있다";
                    MarkLog.Add(cm.Memory.Marks, world.Tick, $"{mentor.Name}에게 {Ko.EulReul(Skills.Name(skill))} 배웠다 — {what}");
                    world.History.Add(world, HistoryKind.Bond, $"{Ko.IGa(cm.Name)} {mentor.Name}에게 {Ko.EulReul(Skills.Name(skill))} 배워 {what} ({after * 100:0}%)", cm.Room, new[] { cm, mentor }, log: true);
                }
            return true;
        }));
        return Wrap(a, o, c, w, "배우기", toils, $"{mentor.Name} 곁에서 {Ko.EulReul(Skills.Name(skill))} 배운다", LogKind.Life);
    }

    /// <summary>재활: 의무실(휴게실)에서 한 시간 몸을 푼다 — 의무관이 같은 방에 있으면 더 낫다.</summary>
    private static Job? Rehab(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = WorkBoard.RehabRoom(w);
        if (room == null) { blocked = "재활할 곳이 없다"; return null; }
        var spot = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell s) { blocked = "자리가 없다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(s));
        toils.Add(new WaitToil(SimTime.Hours(1), Pose.Working, room.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            var medic = world.Crew.Where(x => !x.Dead && !x.Down && x != cm && x.Room == room && x.Pose != Pose.Sleeping)
                .OrderByDescending(x => x.SkillLevel(Skill.Medicine)).FirstOrDefault();
            float help = medic != null ? medic.SkillLevel(Skill.Medicine) : 0.3f;
            var v = cm.Vitals;
            v.Injury = MathF.Max(0f, v.Injury - (0.035f + 0.045f * help));
            v.Scar = MathF.Max(v.ScarFloor, v.Scar - 0.012f * (0.5f + help));
            cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.05f);
            cm.LastRehab = world.Tick;
            cm.Stats.RehabSessions++;
            world.Growth.RehabSessions++;
            if (medic != null) { medic.Practice(Skill.Medicine, 0.01f); cm.ChangeAffinity(medic, 0.03f); }
            world.Log.Add(world.Tick, LogKind.Life, $"{room.Name}에서 재활 운동을 했다 (부상 {v.Injury * 100:0}%" + (medic != null ? $" · {Ko.IGa(medic.Name)} 봐 줬다)" : ")"), cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "재활", toils, null, LogKind.Life);
    }
}
