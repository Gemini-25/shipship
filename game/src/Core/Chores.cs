using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 작업 목록(WorkBoard)에서 일을 골라 맡는 행동.
/// 명령받는 게 아니라, 각자 "이 일이 나한테 얼마나 맞고 얼마나 급한가"를 따져서 고른다.
/// 그래서 엔지니어는 원자로로, 재배사는 재배대로 자연스럽게 흩어지고,
/// 겁 많은 사람은 감압된 방의 일을 남에게 미룬다.
/// </summary>
public sealed class ChoresActivity : Activity
{
    public override string Id => "chores";
    public override string Label => "작업";

    /// <summary>
    /// 사고 대응용 거리장: 우주복을 챙겨 입고 잠긴 격벽도 비상 개방한다고 가정한 길.
    /// 평소 거리장으로는 감압된 방의 일이 "갈 수 없음"으로 보이기 때문.
    /// </summary>
    private static DistanceField EmergencyField(CrewMember c, World w) =>
        w.Paths.Flood(c.Cell, new PathProfile(c.PathProfile.HazardScale * 0.8f, true, true));

    private static bool NeedsEmergencyField(WorkOrder o) =>
        o.Urgency >= 0.9f || (o.Target.CurrentRoom is Room r && (r.Lockdown || r.Unbreathable));

    /// <summary>선체 밖 일(EVA)용 거리장: 우주복을 입고 외부 해치로 나가 선체 밖 칸을 따라가는 길.</summary>
    private static DistanceField EvaField(CrewMember c, World w) =>
        w.Paths.Flood(c.Cell, new PathProfile(c.PathProfile.HazardScale * 0.8f, true, true, c.PathProfile.Fear, Eva: true));

    internal static bool NeedsEvaField(WorkOrder o) =>
        o.External || o.Target.Outside || (o.Kind == WorkKind.Rescue && o.Target.Crew?.Outside == true);

    /// <summary>회의만 하는 일 (사람이 맡아 하는 일이 아니다).</summary>
    private static bool DecisionOnly(WorkKind k) => k is WorkKind.Jettison or WorkKind.Retrieve or WorkKind.RestoreRoom or WorkKind.IsolateMain or WorkKind.LimpMain
        or WorkKind.PlanRepipe;

    /// <summary>v14.0 두려움이 걸린 일을 꺼리는 정도 (급한 일이면 반쯤 이겨 낸다).</summary>
    private static float emergencyFear(WorkOrder o) => o.Urgency >= 0.9f ? 0.12f : 0.25f;

    private static long _onCallTick = -1;
    private static World? _onCallWorld;
    private static int _onCall = -1;

    /// <summary>지금 깨어 있는 사람 중 의료를 가장 잘하는 사람 (틱마다 한 번).</summary>
    public static int MedicOnCall(World w)
    {
        if (_onCallTick == w.Tick && _onCallWorld == w) return _onCall;
        _onCallTick = w.Tick; _onCallWorld = w; _onCall = -1;
        float best = 0.25f;
        foreach (var x in w.Crew)
        {
            if (!x.CanAct || x.IsChild || x.Outside || x.Pose == Pose.Sleeping) continue;
            float s = x.RawSkill(Skill.Medicine);
            if (s > best) { best = s; _onCall = x.Id; }
        }
        return _onCall;
    }

    /// <summary>승무원 한 명이 이 일을 얼마나 하고 싶은지.</summary>
    public static float Appeal(CrewMember c, World w, WorkOrder o, DistanceField dist, out int distance)
    {
        distance = -1;
        if (c.IsChild) return -1f; // v12.9 아이는 일하지 않는다
        if (o.Target.Crew == c && o.Kind != WorkKind.Rehab) return -1f; // 자기 자신은 치료 못 함 (v11.3 재활은 제 몸을 푼다)
        if (o.Kind == WorkKind.Drill && o.Circuit != c.Id) return -1f; // v11.0: 훈련은 제 몫만
        if (o.Kind == WorkKind.Train && o.Circuit / 10 != c.Id) return -1f; // v11.3: 배우는 사람만
        if (o.Kind == WorkKind.Rehab && o.Circuit != c.Id) return -1f; // v11.3: 재활은 다친 사람이
        if (o.Kind == WorkKind.Handover && o.Circuit != c.Id) return -1f; // v12.0: 인수인계는 기록을 든 사람이
        if (o.Kind == WorkKind.SafetyWatch && (w.Command.TeamOf(c) is not Team st || st.Watcher != c.Id || st.Worker != o.Circuit)) return -1f; // v13.1 정해진 짝만
        // v12.0 전조 손보기는 그 기록을 아는 사람만 (직접 봤거나 · 인계받았거나 · 컴퓨터 일지로 읽었다)
        if (o.Kind == WorkKind.PreventiveCheck && o.Target.Furniture?.Machine?.Omen?.Note is ShiftNote note && !w.Watch.Knows(note, c)) return -1f;
        if (DecisionOnly(o.Kind)) return -1f;
        if (!w.Minds.Aware(c, o)) return -1f; // v13.3 모르는 사고의 일은 하지 않는다
        // v13.4 근무 박탈 · 은퇴한 노인: 급한 일 말고는 하지 않는다
        if (o.Urgency < 0.9f && (w.Society.Suspended(c) || c.Age >= 65f && w.Policies["elders"] == 0)) return -1f;
        // v8: 선체 밖 일은 드론이 맡을 수 있으면 드론에게 맡긴다 (드론이 없거나 멈췄을 때만 사람이 나간다)
        bool eva = NeedsEvaField(o) && o.Kind != WorkKind.Rescue;
        if (eva && w.Drones.WillHandle(o)) return -1f;
        if (o.MinSkill > 0f && c.SkillLevel(o.Skill) < o.MinSkill) return -1f; // 할 줄 모르는 일
        var spot = Plans.WorkSpot(o.Target, w, dist, c);
        if (spot is not Cell s) return -1f;
        distance = dist.Get(s);

        float skill = c.SkillLevel(o.Skill);
        float fit = 0.55f + 0.45f * skill + (CrewRoles.Owns(c.Role, o) ? 0.15f : 0f);
        float score = o.Urgency * fit;
        // v11.3: 조리사가 깨어 근무 중이면 부엌은 그 사람 몫 — 다른 사람(재배 담당 포함)은 한 발 물러선다
        if (o.Kind == WorkKind.Cook && c.Role != CrewRole.Cook
            && w.Crew.Any(x => x.Role == CrewRole.Cook && !x.Dead && !x.Down && x.Pose != Pose.Sleeping && x.CareBed == null && OnShiftStatic(x, w)))
            score -= 0.2f;
        if (o.Kind == WorkKind.Cook) score += w.Cooking.CookBias(c); // v16.8 다친 조리사 대신 배우던 사람이
        // v12.7 자격: 자격 있는 사람이 깨어 있으면 자격 없는 사람은 한 발 물러선다 (없으면 서툴러도 한다)
        // (급한 일·다친 사람 돌보기는 누구든 — 자격은 솜씨와 실수에만)
        if (o.Urgency < 0.85f && o.Kind is not (WorkKind.Treat or WorkKind.Rescue) && Life.Needs(o) is Qual need && !Life.HasQual(c, need)
            && w.Crew.Any(x => x != c && x.CanAct && Life.HasQual(x, need) && x.Pose != Pose.Sleeping))
            score -= Persona.Core(need) ? 0.12f : 0.06f;
        // v14.0 두려움: 무서운 일은 꺼린다 (급하면 덜)
        if (c.Fears.Count > 0 && Persona.FearOf(c, o) is Fear) score -= emergencyFear(o);

        bool emergency = o.Urgency >= 0.9f;
        // 위기 판단: 비상·생존 위기에 맞닿은 일(사람·불·전기·공기·사람 있는 방의 구멍)은 앞으로, 딴일은 뒤로.
        // 비번·취침 시간이어도 불려 나온다 (비상 소집)
        score += Crisis.Bias(w, o);
        score += w.Command.Bias(c, o); // v13.1 현장 지휘: 맡은 조의 일
        score += w.Scale.Bias(c, o); // v16.18 규모마다 대응이 커진다 (곁의 사람 → 당직 → 여러 명 → 전원)
        bool allHands = Crisis.AllHands(w, o) || w.Scale.AllHands(c, o);
        // v12.0 교대 한 시간 전에는 새 점검을 벌이기보다 기록을 넘긴다
        if (o.Kind == WorkKind.PreventiveCheck && !emergency && OnShiftStatic(c, w)
            && !SimTime.InWindow(SimTime.HourOfDay(w.Tick) + 1f, c.Schedule.WorkStart, c.Schedule.WorkLength)) score -= 0.15f;
        // v14.4 다친 사람 치료는 배에서 의료를 가장 잘하는 사람이 비번이어도 불려 온다 (의무관이 없을 때 — 아는 사람이 맡는다)
        bool onCall = o.Kind == WorkKind.Treat && MedicOnCall(w) == c.Id;
        if (onCall) score += 0.1f;
        if (OnShiftStatic(c, w)) score += 0.08f + 0.1f * c.Traits.Diligence;
        else if (!emergency && !allHands && !onCall && o.Kind is not (WorkKind.Train or WorkKind.Rehab or WorkKind.Handover or WorkKind.FitProsthetic or WorkKind.RecoverBody))
            score -= w.Policies["leisure"] switch { 0 => 0.15f, 2 => 0.45f, _ => 0.3f }; // v11.3 배우기·재활은 비번에 하는 일 · v13.4 휴식·여가 방침
        // v12.1 인수인계는 몇 분짜리 말 — 성실한 사람일수록 넘기고 나서 쉰다 (자기 전에도)
        if (o.Kind == WorkKind.Handover) score += 0.18f + 0.22f * c.Traits.Diligence;
        if (BedtimeStatic(c, w)) score -= emergency || allHands ? 0.1f : o.Kind == WorkKind.Handover ? 0.15f : 0.5f;

        score -= distance / 6000f;
        score -= 0.15f * c.Needs.Stress;
        // 하던 일은 마저 끝내고 싶다 (교대 시간이 돼도 바로 손을 놓지 않음) — v13.4 조를 맡았으면 조의 일이 아닌 하던 일은 덜 붙든다
        if (o.Assignee == c) score += w.Command.TeamOf(c) is Team mt && mt.Kind != TeamKind.Reserve && CommandSystem.Group(o.Kind) != mt.Kind ? 0.05f : 0.25f;
        else if (o.Robot != null) score -= 0.15f; // v10.10: 로봇이 하고 있는 일에 합류 — 더 급한 일이 없을 때만
        if (c.Vitals.Health < 0.5f) score -= 0.3f;
        if (c.Fx.Worst > 0.45f && o.Urgency < 0.9f) score -= 0.3f * c.Fx.Worst; // v14.1 앓는 사람은 급하지 않은 일을 미룬다

        // EVA: 발밑이 우주다. 겁 많은 사람은 꺼리고, 긴장한 사람은 더 꺼린다
        if (eva) score -= 0.12f + 0.35f * MathF.Pow(1f - c.Traits.Bravery, 1.3f) + 0.3f * c.Memory.Trauma;
        // v11.2 태양 폭풍: 선체 밖은 방사선 — 급하지 않으면 지나갈 때까지 미룬다
        if (eva && (w.Hazards.StormActive || w.Cosmic.NoEva) && !emergency) score -= 0.8f; // v18.13 대재난 예보 · 본 사건

        // 겁 많은 사람은 위험한 방의 일을 꺼린다 (용감한 사람은 거의 개의치 않음)
        if (!eva && o.Target.CurrentRoom is Room room)
        {
            float danger = MathF.Max(Atmosphere.Danger(room), room.Leaking ? 0.6f : 0f);
            // 진공·저압은 우주복을 입고 가면 된다 (쓸 우주복이 있으면 겁을 덜 낸다) — 불은 그대로 무섭다
            if (danger > 0.2f && (c.Suit is { Oxygen: > 0.5f } || w.Ship.FurnitureOf(FurnitureType.SuitLocker).Any(l => l.Storage!.Count(ItemKind.Suit) > 0 && !l.Room.Leaking)))
                danger *= 0.35f;
            if (w.Fire.CountIn(room) > 0) danger = MathF.Max(danger, 0.5f);
            if (danger > 0.2f) score -= danger * MathF.Pow(1f - c.Traits.Bravery, 1.5f) * 1.2f;

            // v7: 무서운 방의 일은 남에게 미룬다 (급하면 덜, 성실하면 덜)
            float fear = c.Memory.FearOf(room);
            if (fear > 0.1f) score -= fear * (emergency ? 0.25f : 0.5f) * (1.2f - 0.4f * c.Traits.Diligence);
        }

        // v7: 가까운 사람이 쓰러졌거나 다쳤으면 먼저 달려간다 (전우면 더)
        if (o.Kind is WorkKind.Rescue or WorkKind.Treat && o.Target.Crew is CrewMember patient)
        {
            float aff = c.AffinityTo(patient);
            if (aff > 0.3f) score += 0.3f * aff + (Memory.AreComrades(c, patient) ? 0.15f : 0f);
            if (o.Kind == WorkKind.Rescue && c.Mind.Heroic(w.Tick) && c.Mind.HeroFor == patient.Id) score += 0.6f; // v13.3 영웅심
            // v14.4 기억: 나를 구해 줬던 사람이면 더 · 전에 두고 나왔던 사람이면 이번엔 꼭 (빚진 마음)
            if (o.Kind == WorkKind.Rescue && w.Relations.All.Count > 0)
            {
                score += 0.15f * MathF.Max(0f, w.Relations.Trust(c, patient));
                if (w.Relations.Of(patient, c).Any(m => m.Reason == RelationReason.AbandonedMe && m.Weight < 0f)) score += 0.2f;
            }
        }
        return score;
    }

    internal static bool OnShiftStatic(CrewMember c, World w) =>
        SimTime.InWindow(SimTime.HourOfDay(w.Tick), c.Schedule.WorkStart, c.Schedule.WorkLength) && c.ExcusedUntil <= w.Tick || c.CoveringUntil > w.Tick; // v14.4

    private static bool BedtimeStatic(CrewMember c, World w) =>
        SimTime.InWindow(SimTime.HourOfDay(w.Tick), c.Schedule.SleepStart, c.Schedule.SleepLength);

    // v14.2 판단 한 번 동안 같은 평가를 다시 하지 않는다 (점수 · 갈아타기 · 계획이 같은 후보를 본다) — 판단을 시작할 때 비운다
    private static CrewMember? _memoCrew;
    private static DistanceField? _memoDist;
    private static long _memoTick = -1;
    private static (WorkOrder? order, float score, DistanceField? field) _memo;
    internal static void ResetMemo() { _memoCrew = null; _memoDist = null; }

    private static (WorkOrder? order, float score, DistanceField? field) Best(CrewMember c, World w, DistanceField dist,
        HashSet<WorkOrder>? skip = null)
    {
        bool plain = skip == null || skip.Count == 0;
        if (plain && _memoCrew == c && _memoDist == dist && _memoTick == w.Tick) return _memo;
        var r = BestCore(c, w, dist, skip);
        if (plain) { _memoCrew = c; _memoDist = dist; _memoTick = w.Tick; _memo = r; }
        return r;
    }

    private static (WorkOrder? order, float score, DistanceField? field) BestCore(CrewMember c, World w, DistanceField dist,
        HashSet<WorkOrder>? skip)
    {
        WorkOrder? best = null;
        DistanceField? bestField = null;
        float bestScore = float.MinValue;
        DistanceField? emergency = null;
        DistanceField? evaField = null;
        foreach (var o in w.Board.AvailableTo(c))
        {
            if (skip != null && skip.Contains(o)) continue;
            if (o.MinSkill > 0f && c.SkillLevel(o.Skill) < o.MinSkill) continue;
            if (DecisionOnly(o.Kind)) continue;
            if (!w.Minds.Aware(c, o)) continue; // v13.3 모르는 사고
            var field = dist;
            if (NeedsEvaField(o)) field = evaField ??= EvaField(c, w);
            else if (NeedsEmergencyField(o)) field = emergency ??= EmergencyField(c, w);
            float s = Appeal(c, w, o, field, out _);
            if (s < 0f && o.Urgency < 0.9f) continue;
            if (s > bestScore) { bestScore = s; best = o; bestField = field; }
        }
        return (best, bestScore, bestField);
    }

    /// <summary>지금 맡은 일보다 훨씬 급한 일이 있는지.</summary>
    public bool ShouldSwitch(CrewMember c, World w, DistanceField dist, WorkOrder current)
    {
        var (best, bestScore, _) = Best(c, w, dist);
        if (best == null || best == current) return false;
        var field = NeedsEvaField(current) ? EvaField(c, w) : NeedsEmergencyField(current) ? EmergencyField(c, w) : dist;
        // v13.2 조를 맡았으면 조의 일로 곧장 갈아탄다 (하던 딴일을 붙들고 있지 않는다)
        float margin = w.Command.TeamOf(c) is Team t && t.Kind != TeamKind.Reserve && CommandSystem.Group(best.Kind) == t.Kind && CommandSystem.Group(current.Kind) != t.Kind ? 0.05f : 0.3f;
        return bestScore > Appeal(c, w, current, field, out _) + margin;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var (o, s, _) = Best(c, w, dist);
        if (o == null) return (0f, "할 작업 없음");
        return (MathF.Max(0f, s), $"{o.Title} ({o.Detail})");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var tried = new HashSet<WorkOrder>();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var (o, _, field) = Best(c, w, dist, tried);
            if (o == null || field == null) return null;
            tried.Add(o);
            var job = WorkPlanners.Build(this, o, c, w, field, out string? blocked);
            if (job != null)
            {
                o.Assignee = c;
                return job;
            }
            if (blocked != null) w.Board.Block(o, blocked, blocked == "냉장고가 가득 참" ? 4f : o.Urgency >= 0.9f ? 0.25f : 1f);
        }
        return null;
    }
}

/// <summary>소화기로 불을 끈다. 곁에 불이 없어질 때까지 뿌린다.</summary>
public sealed class SprayToil : Toil
{
    private readonly Cell _aim;
    private int _elapsed;
    private int _panic;

    public SprayToil(Cell aim) => _aim = aim;

    /// <summary>v13.2 소화기 대신 물 (느리고, 물을 쓰고, 바닥이 젖는다).</summary>
    public bool Water { get; init; }

    public override void Begin(CrewMember c, World w)
    {
        c.Pose = Pose.Working;
        Locomotion.Face(c, _aim.Center);
        // v7 침착함: 당황하면 처음 몇 분은 소화기를 엉뚱한 데 뿌린다 (불길 가장자리, 연기 쪽)
        float panic = 0.35f * (1f - c.Traits.Calm) * (0.4f + c.Needs.Stress);
        if (w.Rng.Chance(panic))
        {
            _panic = SimTime.Minutes(5);
            c.Stats.Panics++;
            w.Log.Add(w.Tick, LogKind.Warning, "당황해서 소화기를 엉뚱한 데 뿌렸다", c.Id);
        }
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        if (Water ? w.Water.Level < 1f : c.Carrying?.Kind != ItemKind.Extinguisher) return ToilStatus.Failed;
        _elapsed++;
        // 가장 가까운 불을 겨눈다
        Cell? nearest = null;
        float best = 2.6f * 2.6f;
        foreach (var (cell, _) in w.Fire.Fires)
        {
            float dx = cell.X + 0.5f - c.Position.X, dy = cell.Y + 0.5f - c.Position.Y;
            float d = dx * dx + dy * dy;
            if (d < best) { best = d; nearest = cell; }
        }
        if (nearest is not Cell aim) return ToilStatus.Succeeded;
        Locomotion.Face(c, aim.Center);
        float rate = (10f + 6f * c.SkillLevel(Skill.Mechanics)) * (0.8f + 0.4f * c.Traits.Calm) / SimTime.TicksPerHour;
        if (_panic > 0) { _panic--; rate *= 0.35f; }
        if (Water)
        {
            rate *= 0.6f;
            w.Fire.Suppress(aim, 1.5f, rate);
            float liters = 2f * 60f / SimTime.TicksPerHour; // 분당 2L
            w.Water.Level = MathF.Max(0f, w.Water.Level - liters);
            if (c.Room != null) w.Moisture.AddWater(c.Room, liters * 0.8f);
            if (_elapsed > SimTime.Minutes(15)) return ToilStatus.Succeeded;
            return ToilStatus.Running;
        }
        w.Fire.Suppress(aim, 1.5f, rate);
        // v12.2 소화 분말은 곁의 설비를 뒤덮는다 (닦아야 한다), 좁은 방에서는 공기가 탁해진다
        foreach (var d in Cell.Dirs8.Append(new Cell(0, 0)))
            if (w.Ship.FurnitureAt(aim + d)?.Machine is Machine fm) fm.Fouled = MathF.Min(1f, fm.Fouled + 0.9f / SimTime.TicksPerHour * 4f);
        if (c.Room is Room sprayRoom && sprayRoom.Volume < 30f) sprayRoom.Air.CO2 += 12f / SimTime.TicksPerHour / sprayRoom.Volume;
        // 소화기 한 통은 15분쯤 뿌리면 빈다 (다시 채울 방법은 없다)
        if (_elapsed > SimTime.Minutes(15))
        {
            c.Carrying = c.Carrying is ItemStack held && held.Count > 1 ? new ItemStack(ItemKind.Extinguisher, held.Count - 1) : null;
            w.Log.Add(w.Tick, LogKind.Warning, "소화기를 다 썼다", c.Id);
            return ToilStatus.Succeeded;
        }
        return ToilStatus.Running;
    }
}

/// <summary>작업 종류별로 "무엇을 어떤 순서로 할지"를 짠다.</summary>
public static partial class WorkPlanners
{
    public static Job? Build(Activity activity, WorkOrder o, CrewMember c, World w, DistanceField dist, out string? blocked)
    {
        blocked = null;
        var spot = Plans.WorkSpot(o.Target, w, dist, c);
        if (spot is not Cell at) { blocked = "갈 수 없음"; return null; }

        // 진공·저압인 곳의 일이거나, 가는 길이 숨 쉴 수 없는 방을 지나야 하면 우주복부터
        var suitUp = new List<Toil>();
        // 선체 밖 일은 계획이 알아서 (재료 → 우주복 → 에어락) 순서로 입는다
        bool selfSuit = o.Kind == WorkKind.SealBreach || ChoresActivity.NeedsEvaField(o);
        if (!selfSuit && c.Suit is not { Oxygen: > 1f } && NeedsSuit(o, c, w, at))
        {
            if (!SuitUp(c, w, dist, suitUp, allowDash: WorkKinds.IsEmergency(o.Kind) || o.Kind == WorkKind.Treat))
            {
                // v13.2 방침(구조: 무조건): 우주복이 없어도 숨을 참고 뛰어들어 끌어낸다
                bool hero = o.Kind == WorkKind.Rescue && c.Mind.Heroic(w.Tick) && o.Target.Crew?.Id == c.Mind.HeroFor; // v13.3 영웅심
                if (!hero && (o.Kind != WorkKind.Rescue || w.Policies["rescue"] != 0 || c.Traits.Bravery < 0.3f)) { blocked = "우주복 없음"; return null; }
                suitUp.Clear();
                suitUp.Add(new DoToil((cm, world) =>
                {
                    cm.Dashing = true;
                    world.Log.Add(world.Tick, LogKind.Warning, hero ? "우주복이 없다 — 영웅심에 숨을 참고 뛰어든다" : "우주복이 없다 — 숨을 참고 구하러 뛰어든다 (방침: 무조건 구조)", cm.Id);
                    return true;
                }));
            }
        }

        var job = o.Kind switch
        {
            WorkKind.Repair => Repair(activity, o, c, w, dist, at, out blocked),
            WorkKind.ResetBreaker => ResetBreaker(activity, o, c, w, dist, at),
            WorkKind.Maintain => Maintain(activity, o, c, w, dist, at),
            WorkKind.Harvest => Harvest(activity, o, c, w, dist, at, out blocked),
            WorkKind.Tend => Tend(activity, o, c, w, dist, at),
            WorkKind.Cook => Cook(activity, o, c, w, dist, at, out blocked),
            WorkKind.Restock => Restock(activity, o, c, w, dist, at, out blocked),
            WorkKind.SealBreach => SealBreach(activity, o, c, w, dist, at, out blocked),
            WorkKind.RepairHull => RepairHull(activity, o, c, w, dist, at, out blocked),
            WorkKind.OperateDamper => OperateDamper(activity, o, c, w, dist, at, out blocked),
            WorkKind.Extinguish => Extinguish(activity, o, c, w, dist, at, out blocked),
            WorkKind.Treat => Treat(activity, o, c, w, dist, at, out blocked),
            WorkKind.RestartReactor => RestartReactor(activity, o, c, w, dist, at),
            WorkKind.StartAux => StartAux(activity, o, c, w, dist, at),
            WorkKind.Fabricate => Fabricate(activity, o, c, w, dist, at, out blocked),
            WorkKind.Rescue => Rescue(activity, o, c, w, dist, at, out blocked),
            WorkKind.SealOffRoom => SealOffRoom(activity, o, c, w, dist, at),
            WorkKind.ReopenRoom => ReopenRoom(activity, o, c, w, dist, at),
            WorkKind.ManualStart => ManualStart(activity, o, c, w, dist, at),
            WorkKind.Refuel => Refuel(activity, o, c, w, dist, at, out blocked),
            WorkKind.InstallJumper => InstallJumper(activity, o, c, w, dist, at, out blocked),
            WorkKind.ShedLoad or WorkKind.RestoreCircuit => SwitchCircuit(activity, o, c, w, dist, at),
            WorkKind.Brownout or WorkKind.EndBrownout => SetBrownout(activity, o, c, w, dist, at),
            WorkKind.Cannibalize => Cannibalize(activity, o, c, w, dist, at, out blocked),
            WorkKind.RepurposeRoom => RepurposeRoom(activity, o, c, w, dist, at, out blocked),
            WorkKind.CrankDoor => CrankDoor(activity, o, c, w, dist, at),
            WorkKind.MeltIce => MeltIce(activity, o, c, w, dist, at, out blocked),
            WorkKind.InstallSubstitute => InstallSubstitute(activity, o, c, w, dist, at, out blocked),
            WorkKind.RestoreGrade => RestoreGrade(activity, o, c, w, dist, at, out blocked),
            WorkKind.ReplacePanel => ReplacePanel(activity, o, c, w, dist, at, out blocked),
            WorkKind.BuildWorkshop => BuildWorkshop(activity, o, c, w, dist, at, out blocked),
            WorkKind.BuildComputer => BuildComputer(activity, o, c, w, dist, at, out blocked),
            WorkKind.BuildOxygen => BuildOxygen(activity, o, c, w, dist, at, out blocked),
            WorkKind.Distress => SendDistress(activity, o, c, w, dist, at),
            WorkKind.Train => Train(activity, o, c, w, dist, at, out blocked),
            WorkKind.Rehab => Rehab(activity, o, c, w, dist, at, out blocked),
            WorkKind.RecoverBody => RecoverBody(activity, o, c, w, dist, at, out blocked),
            WorkKind.FitProsthetic => FitProsthetic(activity, o, c, w, dist, at, out blocked),
            WorkKind.Calibrate => Calibrate(activity, o, c, w, dist, at, out blocked),
            WorkKind.Handover => Handover(activity, o, c, w, dist, at, out blocked),
            WorkKind.CoolDown => CoolDown(activity, o, c, w, dist, at, out blocked),
            WorkKind.ClearRubble => ClearRubble(activity, o, c, w, dist, at, out blocked),
            WorkKind.CleanUp => CleanUp(activity, o, c, w, dist, at, out blocked),
            WorkKind.BleedRoom => BleedRoom(activity, o, c, w, dist, at, out blocked),
            WorkKind.SealO2Line => SealO2Line(activity, o, c, w, dist, at, out blocked),
            WorkKind.WakeCrew => WakeCrew(activity, o, c, w, dist, at, out blocked),
            WorkKind.Rewire => Rewire(activity, o, c, w, dist, at, out blocked),
            WorkKind.RepairNet => RepairNet(activity, o, c, w, dist, at, out blocked),
            WorkKind.IsolateRoom or WorkKind.BreakerOn or WorkKind.ShutRoomValve or WorkKind.OpenRoomValve => RoomSwitch(activity, o, c, w, dist, at, out blocked),
            WorkKind.PumpOut => PumpOut(activity, o, c, w, dist, at, out blocked),
            WorkKind.ManualControl => ManualControl(activity, o, c, w, dist, at, out blocked),
            WorkKind.SafetyWatch => SafetyWatch(activity, o, c, w, dist, at, out blocked),
            WorkKind.Reline => Reline(activity, o, c, w, dist, at, out blocked),
            WorkKind.UnloadSupply => UnloadSupply(activity, o, c, w, dist, at),
            WorkKind.AnswerSignal => AnswerSignal(activity, o, c, w, dist, at),
            WorkKind.Upgrade => Upgrade(activity, o, c, w, dist, at, out blocked),
            // v8 외부 작업과 구조
            WorkKind.RepairJoint => RepairJoint(activity, o, c, w, dist, at, out blocked),
            WorkKind.RebuildFrame => RebuildFrame(activity, o, c, w, dist, at, out blocked),
            WorkKind.InstallTruss => InstallTruss(activity, o, c, w, dist, at, out blocked),
            WorkKind.Clamp => Clamp(activity, o, c, w, dist, at, out blocked),
            WorkKind.ReleaseJoint => ReleaseJoint(activity, o, c, w, dist, at, out blocked),
            WorkKind.InspectHull => InspectHull(activity, o, c, w, dist, at, out blocked),
            WorkKind.ServiceDrone => ServiceDrone(activity, o, c, w, dist, at, out blocked),
            WorkKind.StockDock => StockDock(activity, o, c, w, dist, at, out blocked),
            WorkKind.UnstockDock => UnstockDock(activity, o, c, w, dist, at, out blocked),
            WorkKind.RepairDoor => RepairDoor(activity, o, c, w, dist, at, out blocked),
            WorkKind.FixLights => FixLights(activity, o, c, w, dist, at, out blocked),
            WorkKind.PilotDrones => PilotDrones(activity, o, c, w, dist, at, out blocked),
            WorkKind.RadarWatch => RadarWatch(activity, o, c, w, dist, at, out blocked),
            WorkKind.Salvage => Salvage(activity, o, c, w, dist, at, out blocked),
            WorkKind.IsolatePower => IsolatePower(activity, o, c, w, dist, at),
            WorkKind.IsolatePipes => IsolatePipes(activity, o, c, w, dist, at),
            WorkKind.IsolateVent => IsolateVent(activity, o, c, w, dist, at),
            WorkKind.WeldBulkhead => WeldBulkhead(activity, o, c, w, dist, at, out blocked),
            WorkKind.Eject => Eject(activity, o, c, w, dist, at),
            WorkKind.Reconnect => Reconnect(activity, o, c, w, dist, at, out blocked),
            // v9 배관·냉각
            WorkKind.CloseValve => ValveJob(activity, o, c, w, dist, at, close: true),
            WorkKind.OpenValve => ValveJob(activity, o, c, w, dist, at, close: false),
            WorkKind.PatchPipe => PatchPipe(activity, o, c, w, dist, at, out blocked),
            WorkKind.ReplacePipe => ReplacePipe(activity, o, c, w, dist, at, out blocked),
            WorkKind.LayBypass => LayBypass(activity, o, c, w, dist, at, out blocked),
            WorkKind.RefillCoolant => RefillCoolant(activity, o, c, w, dist, at, out blocked),
            WorkKind.RepairRadiator => RepairRadiator(activity, o, c, w, dist, at, out blocked),
            // v10.10 선내 로봇 · 자원 회복
            WorkKind.RepairRobot => RepairRobot(activity, o, c, w, dist, at, out blocked),
            WorkKind.FetchRobot => FetchRobot(activity, o, c, w, dist, at, out blocked),
            WorkKind.ServiceRobot => ServiceRobot(activity, o, c, w, dist, at, out blocked),
            WorkKind.CarryWater => CarryWater(activity, o, c, w, dist, at, out blocked),
            WorkKind.StockCache => StockCache(activity, o, c, w, dist, at, out blocked),
            WorkKind.RemoveJumper => RemoveJumper(activity, o, c, w, dist, at, out blocked),
            WorkKind.StowCot => StowCot(activity, o, c, w, dist, at, out blocked),
            WorkKind.Recycle => RecycleMachine(activity, o, c, w, dist, at, out blocked),
            // v11.0 예방과 안전
            WorkKind.PreventiveCheck => PreventiveCheck(activity, o, c, w, dist, at, out blocked),
            WorkKind.SuitCheck => SuitCheck(activity, o, c, w, dist, at, out blocked),
            WorkKind.Drill => Drill(activity, o, c, w, dist, at, out blocked),
            // v11.2 항로와 추진
            WorkKind.RefillPropellant => RefillPropellant(activity, o, c, w, dist, at, out blocked),
            WorkKind.ChangeCourse => ChangeCourse(activity, o, c, w, dist, at, out blocked),
            WorkKind.DiscardFood => DiscardFood(activity, o, c, w, dist, at, out blocked),
            WorkKind.Ration or WorkKind.EndRation => SetRation(activity, o, c, w, dist, at),
            _ => null,
        };
        if (job != null && suitUp.Count > 0) job.Prepend(suitUp);
        return job;
    }

    /// <summary>누구든 맨몸으로는 못 들어가는 방: 숨 쉴 수 없거나, 기압이 낮거나, 유독하다.</summary>
    public static bool Unsafe(Room r) => r.Unbreathable || r.Air.Pressure < 60f || r.Air.Toxin > 0.2f;

    /// <summary>v12.9.1 급하지 않은 일(차단기·수리·정비…)로는 맨몸으로 들어가지 않는 방: 산소가 묽거나, 새는 채 환기가 끊겼다 (곧 묽어진다).
    /// 비상 대응(봉합·소화·구조·격리…)은 각오하고 들어간다 — 그 판단은 계획마다 따로 한다.</summary>
    public static bool Hostile(Room r) =>
        Unsafe(r) || r.Air.O2 < 17f || r.Leaking && (r.Air.Pressure < 92f || !r.VentOpen || !r.DuctLinked);

    private static bool NeedsSuit(WorkOrder o, CrewMember c, World w, Cell at)
    {
        if (o.Target.CurrentRoom is Room r && (WorkKinds.IsEmergency(o.Kind) ? Unsafe(r) : Hostile(r))) return true; // v11.2 유독 가스 · v12.9.1 산소가 묽거나 새는 채 닫힌 방
        if (w.Ship.RoomAt(at) is Room ar && ar.Unbreathable) return true;
        // 우주복 없이 (비상 개방만 하고) 갈 수 있는지
        var profile = new PathProfile(c.PathProfile.HazardScale, false, o.Urgency >= 0.9f);
        return w.Paths.Find(c.Cell, at, profile) == null;
    }

    private static Job Wrap(Activity a, WorkOrder o, CrewMember c, World w, string label, List<Toil> toils, string? log,
        LogKind kind = LogKind.Work)
    {
        bool urgent = o.Urgency >= 0.9f;
        return new Job(a, label, toils)
        {
            Order = o,
            Target = o.Target.Furniture,
            TargetRoom = o.Target.CurrentRoom,
            LogText = log,
            LogKind = kind,
            AlwaysLog = true,
            Urgent = urgent,
            InterruptMargin = urgent ? 0.4f : 0.2f,
            OnFinished = (cm, world, status) =>
            {
                // v7: 사고 대응을 해낸 사람은 이번 사고의 대응자로 남는다 (사고가 끝나면 조금 침착해진다)
                if (status == ToilStatus.Succeeded && (WorkKinds.IsEmergency(o.Kind) || o.Kind is WorkKind.Rescue or WorkKind.Treat
                        || (o.Kind == WorkKind.Repair && o.Urgency >= 0.9f)))
                    world.History.Responded(cm);
                if (status == ToilStatus.Succeeded) world.Causes.Worked(o, cm); // v12.2 누가 되돌렸나
                if (status == ToilStatus.Succeeded) world.Life.AfterWork(cm, o); // v12.7 사람답게 틀린다 (왜 틀렸는지 남는다)
                if (status != ToilStatus.Succeeded) world.Board.Release(o, cm);
                if (status != ToilStatus.Succeeded)
                    World.Trace?.Invoke($"{SimTime.Clock(world.Tick)} {cm.Name} {o.Title} {status} @{cm.Job?.Current?.GetType().Name}");
                // 곧바로 실패한 일(길이 막힘 등)은 잠깐 미뤄서 같은 시도를 틱마다 되풀이하지 않게
                if (status == ToilStatus.Failed && !o.Closed) world.Board.Block(o, null, 0.1f);
            },
        };
    }

    /// <summary>필요한 부품을 가져오는 단계. 없으면 null.</summary>
    private static List<Toil>? Fetch(CrewMember c, World w, DistanceField dist, ItemKind item, int count, List<Toil>? toils = null)
    {
        toils ??= Plans.DropOff(c, w, dist);
        var (box, spot) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Count(item) >= count);
        if (box == null) return null;
        toils.Add(new GotoToil(spot));
        toils.Add(new TakeToil(box, item, count));
        return toils;
    }

    /// <summary>
    /// 여러 재료를 모은다: 첫 재료는 손에, 나머지는 공구 가방에. 한 선반에 다 없으면 여러 선반을 돈다. 하나라도 모자라면 null.
    /// </summary>
    private static List<Toil>? FetchAll(CrewMember c, World w, DistanceField dist, (ItemKind kind, int count)[] needs)
    {
        var toils = Plans.DropOff(c, w, dist);
        bool first = true;
        foreach (var (kind, count) in needs)
        {
            int left = count;
            var used = new HashSet<Furniture>();
            while (left > 0)
            {
                var (box, spot) = Plans.NearestContainer(w, dist, c, f => !used.Contains(f) && f.Storage!.Count(kind) > 0);
                if (box == null) return null;
                used.Add(box);
                int take = Math.Min(left, box.Storage!.Count(kind));
                toils.Add(new GotoToil(spot));
                toils.Add(first ? new TakeToil(box, kind, take) : new TakeKitToil(box, kind, take));
                left -= take;
            }
            first = false;
        }
        return toils;
    }

    /// <summary>손과 가방에 재료가 다 있으면 쓰고 true.</summary>
    private static bool UseAll(CrewMember c, (ItemKind kind, int count)[] needs)
    {
        if (needs.Length == 0) return true;
        var (main, n0) = needs[0];
        if (c.Carrying is not ItemStack held || held.Kind != main || held.Count < n0) return false;
        foreach (var (kind, n) in needs.Skip(1)) if (c.KitCount(kind) < n) return false;
        foreach (var (kind, n) in needs.Skip(1)) c.UseKit(kind, n);
        c.Carrying = held.Count > n0 ? new ItemStack(main, held.Count - n0) : null;
        return true;
    }

    private static string Cost((ItemKind kind, int count)[] needs) => string.Join(" + ", needs.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}"));

    /// <summary>우주복 보관함에서 우주복을 꺼내 입는다. 이미 입고 있으면 아무것도 안 한다.</summary>
    private static bool SuitUp(CrewMember c, World w, DistanceField dist, List<Toil> toils, bool allowDash = true)
    {
        if (c.Suit is { Oxygen: > 1f }) return true;
        // v12.9.1 급하지 않은 일은 마지막 한 벌을 남겨 둔다 (봉합·구조하러 갈 사람의 몫)
        int suitsLeft = w.Ship.FurnitureOf(FurnitureType.SuitLocker).Sum(f => f.Storage!.Count(ItemKind.Suit));
        if (!allowDash && suitsLeft <= 1) return false;
        // v13.1 방침(우주복: 비상조 먼저): 조 편성이 있으면 위험한 방에 가는 조 몫을 남긴다
        if (w.Policies["suits"] == 0 && w.Command.Active && w.Command.TeamOf(c) is not { Hazard: true })
        {
            int teamNeed = w.Command.Teams.Where(t => t.Hazard).SelectMany(t => new[] { t.Worker, t.Watcher }).Count(id => id >= 0 && w.Crew.Any(x => x.Id == id && x.Suit == null));
            if (suitsLeft <= teamNeed) return false;
        }
        // v13.2 방침(우주복: 한 사람 한 벌): 사람마다 정해 둔 한 벌 — 내 몫이 없으면 남의 것을 입지 않는다
        if (w.Policies["suits"] == 2)
        {
            int total = suitsLeft + w.Crew.Count(x => !x.Dead && x.Suit != null);
            int rank = w.Crew.Where(x => !x.Dead && !x.IsChild).OrderBy(x => x.Id).ToList().IndexOf(c);
            if (rank >= total) return false;
        }
        var (locker, spot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.SuitLocker && f.Storage!.Count(ItemKind.Suit) > 0);
        if (locker == null) return false;

        // 보관함이 진공 속에 있으면 (에어락이 뚫렸다든가) 우주복 없이는 갈 수 없다.
        // 대담한 사람만 숨을 참고 뛰어들어 입고 나온다 — 비상수단이지만 몸이 상한다.
        bool reachable = w.Paths.Find(c.Cell, spot, new PathProfile(c.PathProfile.HazardScale, false, true)) != null;
        if (!reachable)
        {
            if (c.Traits.Bravery < 0.55f || !allowDash) return false; // v12.9.1 급하지 않은 일로는 진공에 뛰어들지 않는다
            toils.Add(new DoToil((cm, world) =>
            {
                cm.Dashing = true;
                world.Log.Add(world.Tick, LogKind.Warning, "숨을 참고 진공 속 우주복 보관함으로 뛰어든다", cm.Id);
                return true;
            }));
        }
        toils.Add(new GotoToil(spot));
        // v11.0: 비상 훈련을 받은 사람은 우주복을 빨리 입는다
        toils.Add(new WaitToil(SimTime.Minutes(reachable ? (c.Drilled(w) ? 2.5f : 4f) : 1f), Pose.Working, locker.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            cm.Dashing = false;
            if (cm.Suit != null) return true;
            if (locker.Storage!.Take(ItemKind.Suit, 1) == 0) return false;
            cm.Suit = new SuitState();
            world.EvaRisk.OnIssue(cm, locker); // v16.11 멀쩡한 게 없으면 수리 대기 중인 걸 입는다
            // v11.0: 닷새 넘게 점검하지 않은 보관함의 우주복은 밸브가 새기도 한다
            if (world.Tick - locker.Checked > SimTime.Hours(120) && world.Rng.Chance(0.25f))
            {
                cm.Suit.Leak = 1.7f;
                world.Log.Add(world.Tick, LogKind.Warning, "우주복 밸브가 샌다 — 산소가 빨리 준다 (보관함을 오래 점검하지 않았다)", cm.Id);
            }
            else world.Log.Add(world.Tick, LogKind.Work, "우주복을 입었다", cm.Id);
            return true;
        }) { DonsSuit = true });
        return true;
    }

    private static void Consume(CrewMember c, ItemKind item, int n = 1)
    {
        if (c.Carrying is ItemStack held && held.Kind == item)
            c.Carrying = held.Count > n ? new ItemStack(item, held.Count - n) : null;
    }

    // ── 수리: 부품이 있으면 완전 수리, 없으면 부분 수리 단계(20% → 60%)까지. 급하면 일단 20%부터. ──
    private static Job? Repair(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var f = o.Target.Furniture!;
        var m = f.Machine!;
        var fault = m.Faults.FirstOrDefault(x => x.Kind == o.Fault && x.Circuit == o.Circuit);
        if (fault == null) { w.Board.Close(o); return null; }
        // v9.2: 과열로 멈춘 주 컴퓨터는 방이 식어야 다시 켠다 (환기부터)
        if (fault.Kind == FaultKind.Overheat && f.Room.Air.Temperature > AutomationSystem.RestartC)
        {
            blocked = $"{Ko.IGa(f.Room.Name)} 식어야 한다 ({f.Room.Air.Temperature:0}℃ — 환기부터)";
            return null;
        }
        var materials = fault.Materials;
        ItemKind? part = materials.Length > 0 ? materials[0].kind : null;

        // 재료를 구할 수 있나 (갈 수 있는 보관함에 있나). 파손이면 핵심 부품 + 금속판
        List<Toil>? fetch = materials.Length > 0 ? FetchAll(c, w, dist, materials) : Plans.DropOff(c, w, dist);
        bool havePart = fetch != null;

        // 무엇을 할지: 급한 핵심 설비가 멈춰 있고 완전 수리가 오래 걸리면 먼저 긴급 우회로 살린다
        bool rush = o.Urgency >= 0.9f && fault.Stage == 0 && fault.Stageable && fault.Spec.RepairHours >= 1.5f;
        int goal; // 1, 2 = 부분 수리 단계, 3 = 완전 수리
        if (havePart && !rush) goal = 3;
        else if (fault.Stageable) goal = fault.Stage + 1;
        else
        {
            var missing = materials.Where(x => w.Ship.CountStored(x.kind) < x.count).Select(x => ItemKinds.Name(x.kind));
            blocked = $"{string.Join("·", missing.DefaultIfEmpty(ItemKinds.Name(part!.Value)))} 없음" + (fault.Stage > 0 ? $" ({fault.StageName} 운전 중)" : "");
            return null;
        }

        var toils = goal == 3 ? fetch! : Plans.DropOff(c, w, dist);
        float hours = goal switch
        {
            1 => MathF.Max(0.2f, fault.Spec.RepairHours * 0.25f),
            2 => MathF.Max(0.3f, fault.Spec.RepairHours * 0.35f),
            _ => fault.Spec.RepairHours * (1f - 0.25f * fault.Stage),
        };
        toils.Add(new GotoToil(at));
        // v12.1 정비 절차 (완전 수리만): 차단·격리·잔압 → 수리 → 시험 운전·재가동
        var proc = goal == 3 && fault.Circuit < 0 ? Procedures.Plan(w, c, m, o.Urgency >= 0.9f || Crisis.Level(w) >= CrisisLevel.Emergency) : null;
        if (proc != null)
        {
            Procedures.Before(w, c, m, proc, toils, o);
            if (proc.Steps.Contains(ProcStep.Test)) hours += 0.15f;
            hours *= w.Parts.RepairFactor(m, fault.Kind, out _); // v14.6 무거운 부품(호이스트) · 정비 카트 · 비좁은 자리
        }
        toils.Add(new WorkToil(hours, m.Spec.Skill, f.Center)
        {
            Resume = goal == 3 ? o : null,
            CanContinue = (cm, _) => goal < 3 || part == null || cm.Carrying?.Kind == part,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!m.Faults.Contains(fault)) { world.Board.Close(o); return true; }
            float skill = cm.SkillLevel(m.Spec.Skill);
            float slip = MathF.Max(0f, 0.45f - skill) * 0.4f + (cm.Needs.Rest < 0.2f ? 0.1f : 0f) + (cm.Needs.Stress > 0.7f ? 0.1f : 0f)
                         + 0.15f * cm.Vitals.Injury
                         + (o.Urgency >= 0.9f ? 0.12f * (1f - cm.Traits.Calm) * (0.5f + cm.Needs.Stress) : 0f); // 급하면 손이 떨린다 (v7)
            cm.Practice(m.Spec.Skill, 0.04f);
            if (world.Rng.Chance(slip))
            {
                world.Log.Add(world.Tick, LogKind.Work, $"{m.Name} 수리에 실패했다. 다시 해야 한다", cm.Id);
                world.Board.Release(o, cm);
                o.Progress = 0f;
                return true;
            }
            if (goal < 3)
            {
                fault.Stage = goal;
                m.Wear = MathF.Min(1f, m.Wear + 0.08f);
                MarkLog.Add(m.Marks, world.Tick, $"{cm.Name}: {fault.Spec.Name} {(goal == 1 ? "긴급 우회 20%" : "부분 복구 60%")}");
                world.Board.Release(o, cm); // 완전 수리는 아직 남았다
                world.Log.Add(world.Tick, LogKind.Work, goal == 1
                    ? $"{Ko.EulReul(m.Name)} 임시로 우회해 20%로 돌렸다 ({fault.Spec.Name})"
                    : $"{Ko.EulReul(m.Name)} {(havePart ? "" : "부품 없이 ")}60%까지 살렸다 ({fault.Spec.Name})", cm.Id);
                return true;
            }
            if (!UseAll(cm, materials)) return false;
            cm.Stats.Repairs++;
            cm.Familiarize(m.Body.Type, 0.06f); // v12.0 만져 본 설비
            world.Board.Close(o);
            if (fault.Kind == FaultKind.Wrecked)
            {
                // 통째로 새로 짰다: 다른 고장도 같이 사라지고 정품이 된다
                m.Faults.RemoveAll(x => x.Circuit < 0);
                m.Grade = MachineGrade.Standard;
                m.Condition = 0.8f;
                m.Wear = 0.1f;
                world.Adapt.Rebuilt++;
                m.Rebuilds++;
                MarkLog.Add(m.Marks, world.Tick, $"{cm.Name}: 파손 뒤 다시 짜 맞춤");
                world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} 파손된 {Ko.EulReul(m.Name)} {ItemKinds.Name(part!.Value)}와 금속판으로 다시 짜 맞췄다",
                    m.Body.Room, new[] { cm });
                world.Log.Add(world.Tick, LogKind.Work, $"파손된 {Ko.EulReul(m.Name)} {ItemKinds.Name(part!.Value)}와 금속판으로 다시 짜 맞췄다", cm.Id);
                return true;
            }
            m.Faults.Remove(fault);
            m.Condition = MathF.Min(1f, m.Condition + 0.015f);
            m.Wear = MathF.Min(m.Wear, 0.3f);
            if (proc != null) Procedures.After(world, cm, m, proc, fault.Kind); // v12.1
            MarkLog.Add(m.Marks, world.Tick, $"{cm.Name}: {fault.Spec.Name} 수리" + (proc != null && proc.Skipped.Count > 0 ? $" (생략: {string.Join(",", proc.Skipped.Select(x => ProcPlan.Name(x.step)))})" : ""));
            world.Log.Add(world.Tick, LogKind.Work, $"{m.Name}의 {Ko.EulReul(fault.Spec.Name)} 고쳤다", cm.Id);
            return true;
        }));
        string what = goal switch { 1 => "긴급 우회", 2 => "부분 수리", _ => "수리" };
        // 수리를 그만두면 잠가 둔 설비·낮춘 원자로를 되돌린다
        var job = Wrap(a, o, c, w, what, toils, $"{m.Name} {what}하러 간다 ({fault.Spec.Name})" + (proc != null ? $" — {proc.Summary}" : ""));
        return job;
    }

    // ── 차단기 복구 ──
    private static Job ResetBreaker(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var f = o.Target.Furniture!;
        var m = f.Machine!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.25f, Skill.Electrical, f.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            m.Faults.RemoveAll(x => x.Kind == FaultKind.BreakerTrip && x.Circuit == o.Circuit);
            cm.Practice(Skill.Electrical, 0.02f);
            cm.Stats.Repairs++;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{PowerGrid.CircuitName(o.Circuit)} 회로 차단기를 올렸다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "차단기", toils, $"{PowerGrid.CircuitName(o.Circuit)} 회로 차단기 올리러 간다");
    }

    // ── 정비: 소모품이 있으면 완전 정비, 없으면 임시 정비(점검·청소·재조임)만 ──
    private static Job Maintain(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var f = o.Target.Furniture!;
        var m = f.Machine!;
        var item = m.Spec.ServiceItem;
        bool full = true;
        bool rationed = Adaptation.Rationed(w, m);
        List<Toil> toils;
        if (rationed) { full = false; toils = Plans.DropOff(c, w, dist); }
        else if (item is ItemKind needed)
        {
            var fetch = Fetch(c, w, dist, needed, 1);
            if (fetch == null) { full = false; toils = Plans.DropOff(c, w, dist); }
            else toils = fetch;
        }
        else toils = Plans.DropOff(c, w, dist);

        toils.Add(new GotoToil(at));
        // v10.10: 정비 로봇이 하던 정비에 합류하면 진척을 함께 채운다
        toils.Add(new WorkToil(m.Spec.ServiceHours * (full ? 1f : 0.6f), m.Spec.Skill, f.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (o.Closed) return true; // 로봇이 먼저 끝냈다
            float skill = cm.SkillLevel(m.Spec.Skill);
            if (full)
            {
                if (item is ItemKind used) Consume(cm, used);
                m.Wear = 0.1f * (1f - skill);
            }
            else
            {
                m.Wear = MathF.Max(0.35f, m.Wear - 0.2f);
                world.Log.Add(world.Tick, LogKind.Warning, rationed
                    ? $"{Ko.EulReul(ItemKinds.Name(item!.Value))} 핵심 설비에 아끼느라 {Ko.EulReul(m.Name)} 임시 정비만 했다"
                    : $"{Ko.IGa(ItemKinds.Name(item!.Value))} 없어 {Ko.EulReul(m.Name)} 임시 정비만 했다", cm.Id);
            }
            m.LastServiced = world.Tick;
            m.ServiceCount++;
            cm.Practice(m.Spec.Skill, 0.02f);
            cm.Familiarize(m.Body.Type, 0.03f); // v12.0
            cm.Stats.Services++;
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, full ? "정비" : "임시 정비", toils, $"{m.Name} {(full ? "정비" : "임시 정비")} ({o.Detail})");
    }

    // ── 수확 ──
    private static Job? Harvest(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var bed = o.Target.Furniture!;
        var crop = bed.Machine!.Crop!;
        int expected = FoodChain.HarvestYield + 3;
        var (fridge, fridgeSpot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.Fridge && f.Storage!.Free >= expected);
        if (fridge == null) { blocked = "냉장고가 가득 참"; return null; }

        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.35f, Skill.Botany, bed.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (o.Closed) return true; // 재배 로봇이 먼저 거뒀다
            if (!crop.Ripe) return false;
            float skill = cm.SkillLevel(Skill.Botany);
            int yield = (int)MathF.Round(FoodChain.HarvestYield * FoodChain.BedSize(bed) * (0.85f + 0.3f * skill) * (0.8f + 0.2f * bed.Machine!.Condition));
            crop.Growth = 0f;
            cm.Carrying = new ItemStack(ItemKind.Produce, yield);
            cm.Practice(Skill.Botany, 0.03f);
            cm.Stats.Harvests++;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{bed.Label}에서 채소 {yield}개를 수확했다", cm.Id);
            return true;
        }));
        toils.Add(new GotoToil(fridgeSpot));
        toils.Add(new PutToil(fridge));
        return Wrap(a, o, c, w, "수확", toils, null);
    }

    // ── 작물 돌보기 ──
    private static Job Tend(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var bed = o.Target.Furniture!;
        var crop = bed.Machine!.Crop!;
        bool blight = crop.Blight > 0f && crop.BlightKnown; // v11.2 병충해: 잎을 한 장씩 뒤집어 약을 친다
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(blight ? 0.9f : 0.3f, Skill.Botany, bed.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (o.Closed) return true;
            crop.Care = 1f;
            if (crop.Blight > 0f)
            {
                float skill = cm.SkillLevel(Skill.Botany);
                crop.Blight = MathF.Max(0f, crop.Blight - (0.55f + 0.5f * skill));
                if (crop.Blight <= 0f)
                {
                    crop.BlightKnown = false;
                    world.Hazards.BlightCured++;
                    MarkLog.Add(bed.Machine!.Marks, world.Tick, $"{cm.Name}: 병충해를 잡았다");
                    world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} {bed.Label}의 병충해를 잡았다", bed.Room, new[] { cm }, log: true, crewLog: cm.Id);
                }
                else world.Log.Add(world.Tick, LogKind.Work, $"{bed.Label}에 약을 쳤다 — 벌레가 아직 남았다 ({crop.Blight * 100:0}%)", cm.Id);
                cm.Practice(Skill.Botany, 0.03f);
            }
            cm.Practice(Skill.Botany, 0.01f);
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, blight ? "병충해 방제" : "재배", toils, blight ? $"{bed.Label} 병충해 방제" : null);
    }

    // ── 조리 ──
    private static Job? Cook(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var stove = o.Target.Furniture!;
        var fetch = Fetch(c, w, dist, ItemKind.Produce, FoodChain.ProducePerBatch);
        if (fetch == null) { blocked = "채소 부족"; return null; }

        var toils = fetch;
        toils.Add(new GotoToil(at));
        toils.AddRange(w.Soil.WashFirst(c, false, "조리")); // v14.7 조리 전에 손을 씻는다
        toils.Add(new WorkToil(FoodChain.CookHours * w.Cooking.HoursMul(stove, c), Skill.Cooking, stove.Center) // v16.8 레시피마다 조리 시간
        {
            CanContinue = (_, _) => stove.Machine!.Efficiency > 0f,
            OnBegin = (_, _) => stove.Machine!.Active = true,
            OnEnd = (_, _) => stove.Machine!.Active = false,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (cm.Carrying is not ItemStack held || held.Kind != ItemKind.Produce) return false;
            float skill = cm.SkillLevel(Skill.Cooking);
            int cooked = FoodChain.MealsPerBatch + (skill > 0.7f ? 1 : 0);
            cm.Carrying = new ItemStack(ItemKind.Meal, cooked);
            world.Soil.OnCooked(cm, cooked); // v14.7 손이 더러웠으면 몇 끼에 균
            world.Cooking.OnCooked(cm, stove, cooked); // v16.8 냄비 하나 (누가 · 무엇을 · 몇 도)
            cm.Practice(Skill.Cooking, 0.03f);
            cm.Stats.MealsCooked += cooked;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"식사 {cooked}인분을 만들었다", cm.Id);
            return true;
        }));
        var dispenser = w.Ship.FurnitureOf(FurnitureType.MealDispenser)
            .Where(d => d.UseSpots.Count > 0 && dist.Reachable(d.UseSpots[0]) && d.Storage!.Free > 0)
            .OrderByDescending(d => d.Storage!.Free).FirstOrDefault();
        if (dispenser != null)
        {
            toils.Add(new GotoToil(dispenser.UseSpots[0]));
            toils.Add(new PutToil(dispenser));
        }
        var (fridge, fridgeSpot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.Fridge);
        if (fridge != null)
        {
            toils.Add(new GotoToil(fridgeSpot, cm => cm.Carrying != null));
            toils.Add(new PutToil(fridge));
        }
        return Wrap(a, o, c, w, "조리", toils, "주방에서 식사를 만든다");
    }

    // ── 배식기 채우기 ──
    private static Job? Restock(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var dispenser = o.Target.Furniture!;
        int want = Math.Min(10, dispenser.Storage!.Free);
        var (fridge, fridgeSpot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.Fridge && f.Storage!.Count(ItemKind.Meal) > 0);
        if (fridge == null || want <= 0) { blocked = "냉장고에 식사 없음"; return null; }

        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(fridgeSpot));
        toils.Add(new TakeToil(fridge, ItemKind.Meal, want, partialOk: true));
        toils.Add(new GotoToil(at));
        toils.Add(new PutToil(dispenser));
        toils.Add(new DoToil((_, world) => { world.Board.Close(o); return true; }));
        toils.Add(new GotoToil(fridgeSpot, cm => cm.Carrying != null));
        toils.Add(new PutToil(fridge));
        return Wrap(a, o, c, w, "배식", toils, null);
    }

    // ── 제작·정제 (레시피대로): 첫 재료는 손에, 나머지는 가방에 → 작업대/정제기 → 만든 것을 선반에. 한 번에 최대 세 묶음 ──
    private static Job? Fabricate(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var station = o.Target.Furniture!;
        var product = o.Product ?? ItemKind.Filter;
        var stationKind = station.Type == FurnitureType.Refinery ? Station.Refinery : Station.Workbench;
        if (Recipes.For(product, stationKind) is not Recipe r) { w.Board.Close(o); return null; }
        // 전기가 없거나 멈춘 정제기·작업대에 재료를 들고 가 봐야 헛걸음이다 (재료만 들었다 놨다 되풀이하던 것)
        if (station.Machine!.Efficiency <= 0f) { blocked = station.Machine.Powered ? $"{station.Name} 멈춤" : $"{station.Name}에 전기가 없다"; return null; }
        int have = w.Board.Have(product);
        int batches = r.FromProduce ? 1 : Math.Clamp((Math.Max(w.History.Doctrine.Target(r), w.Board.Held(product)) - have + r.Yield - 1) / r.Yield, 1, 3); // v15.6 비축 임무
        foreach (var (kind, n) in r.Inputs) batches = Math.Min(batches, w.Ship.CountStored(kind) / n);
        if (batches <= 0) { blocked = $"재료 부족 ({Cost(r.Inputs)})"; return null; }
        var needs = r.Inputs.Select(x => (x.kind, x.count * batches)).ToArray();
        var toils = FetchAll(c, w, dist, needs);
        if (toils == null) { blocked = $"재료를 가져올 수 없음 ({Cost(r.Inputs)})"; return null; }
        int expect = r.Yield * batches;
        var (shelf, shelfSpot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.Shelf && f.Storage!.Accepts(product) && f.Storage.Free >= expect);
        if (shelf == null) (shelf, shelfSpot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.Shelf && f.Storage!.Accepts(product) && f.Storage.Free > 0);
        if (shelf == null) { blocked = "둘 선반 없음"; return null; }
        var main = needs[0].kind;
        float eff = MathF.Max(0.4f, station.Machine!.Efficiency);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(r.Hours * batches / eff, r.Skill, station.Center)
        {
            Resume = o,
            CanContinue = (cm, _) => cm.Carrying?.Kind == main && station.Machine!.Efficiency > 0f,
        });
        bool refinery = r.Station == Station.Refinery;
        toils.Add(new DoToil((cm, world) =>
        {
            if (!UseAll(cm, needs)) return false;
            int made = r.Yield * batches;
            cm.Carrying = new ItemStack(product, made);
            if (!refinery && !r.FromProduce) world.Parts.Made(product, made, cm, cm.SkillLevel(r.Skill)); // v14.6 만든 사람이 남는다
            cm.Practice(r.Skill, r.FromProduce ? 0.01f : 0.02f);
            world.Board.Close(o);
            if (product == ItemKind.Fuel) world.Adapt.FuelMade++;
            else if (refinery) world.Adapt.Refined += made;
            else if (!r.FromProduce) world.Adapt.PartsMade += made;
            string what = product == ItemKind.Fuel ? "채소를 발효·증류해 바이오 연료 한 통을 만들었다"
                : refinery ? $"{Ko.EulReul(Cost(needs))} 정제해 {ItemKinds.Name(product)} {made}개를 만들었다"
                : r.FromProduce ? $"채소로 {Ko.EulReul(ItemKinds.Name(product))} 만들었다"
                : $"{ItemKinds.Name(product)} {made}개를 만들었다 ({Cost(needs)})";
            world.Log.Add(world.Tick, LogKind.Work, what, cm.Id);
            return true;
        }));
        toils.Add(new GotoToil(shelfSpot));
        toils.Add(new PutToil(shelf));
        string label = refinery ? "정제" : "제작";
        return Wrap(a, o, c, w, label, toils, refinery ? $"정제기에서 {Ko.EulReul(ItemKinds.Name(product))} 뽑는다" : $"작업대에서 {Ko.EulReul(ItemKinds.Name(product))} 만든다");
    }

    // ── 얼음 처리: 호퍼의 얼음을 정제기에서 녹여 공기 탱크와 물 탱크에 보탠다 ──
    private static Job? MeltIce(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var station = o.Target.Furniture!;
        int n = Math.Min(6, w.Ship.CountStored(ItemKind.Ice));
        if (n < 1) { w.Board.Close(o); return null; }
        if (station.Machine!.Efficiency <= 0f) { blocked = station.Machine.Powered ? $"{station.Name} 멈춤" : $"{station.Name}에 전기가 없다"; return null; }
        var toils = FetchAll(c, w, dist, new[] { (ItemKind.Ice, n) });
        if (toils == null) { blocked = "얼음을 가져올 수 없음"; return null; }
        float eff = MathF.Max(0.4f, station.Machine!.Efficiency);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(Recipes.IceHoursEach * n / eff, Skill.Mechanics, station.Center)
        {
            Resume = o,
            CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Ice && station.Machine!.Efficiency > 0f,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (cm.Carrying is not ItemStack held || held.Kind != ItemKind.Ice) return false;
            int k = held.Count;
            cm.Carrying = null;
            world.Air.Reserve = MathF.Min(world.Air.ReserveCapacity, world.Air.Reserve + k * Recipes.IceAir);
            world.Water.Level = MathF.Min(world.Water.Capacity, world.Water.Level + k * Recipes.IceWater);
            world.Adapt.IceMelted += k;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work,
                $"얼음 {k}개를 녹여 공기 탱크에 보탰다 (탱크 {world.Air.Reserve / world.Air.ReserveCapacity * 100:0}%)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "얼음 처리", toils, "채집한 얼음을 정제기로 가져간다");
    }

    // ═════════════════════════════ 사고 대응 ═════════════════════════════

    // ── 파공 임시 봉합: (필요하면 우주복) → 실링폼 → 그 벽 앞으로 → 봉합 ──
    private static Job? SealBreach(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var cell = o.Target.Cell;
        var wall = w.Ship.WallAt(cell)!;
        var room = o.Target.Room!;
        int sealant = Hull.SealantFor(wall);
        bool needSuit = room.Unbreathable || room.Air.Pressure < 60f || wall.Breach >= 0.25f;

        var toils = Plans.DropOff(c, w, dist);
        if (needSuit && !SuitUp(c, w, dist, toils)) { blocked = "우주복 없음"; return null; }
        // v12.2 실링폼이 없으면 금속판을 덧대 용접한다 (느리지만 막은 뒤 다시 새지 않는다 — 진공에서도 된다)
        if (w.Ship.CountStored(ItemKind.Sealant) < sealant && w.Ship.CountStored(ItemKind.Plate) >= 2)
            return PlateWeld(a, o, c, w, dist, at, toils, wall, room, cell, needSuit, out blocked);
        if (Fetch(c, w, dist, ItemKind.Sealant, sealant, toils) == null) { blocked = "실링폼 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(wall.Breach >= 0.25f ? 0.6f : 0.35f, Skill.Mechanics, cell.Center)
        {
            CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Sealant,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            Consume(cm, ItemKind.Sealant, sealant);
            float skill = cm.SkillLevel(Skill.Mechanics);
            cm.Practice(Skill.Mechanics, 0.04f);
            cm.Stats.Emergencies++;
            // 서투르거나, 지쳤거나, 구멍이 크거나, 기압이 빠지는 중이면(바람에 거품이 날림) 제대로 안 붙는다
            float fail = MathF.Max(0f, 0.3f - 0.35f * skill) + (wall.Breach >= 0.25f ? 0.08f : 0f)
                         + (room.Air.Pressure > 20f ? 0.05f + 0.1f * (1f - cm.Traits.Calm) : 0f) // 바람에 당황하면 (v7 침착함)
                         + (cm.Needs.Rest < 0.2f ? 0.08f : 0f) + 0.15f * cm.Vitals.Injury;
            if (world.Rng.Chance(fail))
            {
                world.Board.Release(o, cm);
                world.Log.Add(world.Tick, LogKind.Warning, $"{room.Name} 파공에 실링폼이 제대로 붙지 않았다 — 실링폼 {sealant}개를 버렸다", cm.Id);
                MarkLog.Add(wall.Marks, world.Tick, $"{cm.Name}: 봉합 실패");
                return true;
            }
            wall.Patched = true;
            wall.PatchQuality = 0.55f + 0.4f * skill;
            wall.Seals++;
            MarkLog.Add(wall.Marks, world.Tick, $"{cm.Name}: 실링폼 봉합 ({wall.PatchQuality * 100:0}%)");
            world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} {room.Name} 파공을 실링폼으로 막았다", room, new[] { cm }, cell);
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 파공을 실링폼으로 막았다 (봉합 품질 {wall.PatchQuality * 100:0}%)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "파공 봉합", toils, $"{room.Name} 파공을 막으러 간다" + (needSuit ? " (우주복)" : ""));
    }

    /// <summary>v12.2 금속판 덧대 용접 봉합: 실링폼이 없을 때 (우주복을 입고 한 시간 남짓).</summary>
    private static Job? PlateWeld(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, List<Toil> toils,
        WallState wall, Room room, Cell cell, bool needSuit, out string? blocked)
    {
        blocked = null;
        if (Fetch(c, w, dist, ItemKind.Plate, 2, toils) == null) { blocked = "금속판 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1.2f, Skill.Mechanics, cell.Center) { Resume = o, CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Plate });
        toils.Add(new DoToil((cm, world) =>
        {
            Consume(cm, ItemKind.Plate, 2);
            float skill = cm.SkillLevel(Skill.Mechanics);
            cm.Practice(Skill.Mechanics, 0.05f);
            wall.MaxIntegrity = MathF.Max(0.25f, wall.MaxIntegrity - Hull.WeldFatigue(skill));
            wall.Integrity = MathF.Max(wall.Integrity, wall.MaxIntegrity * (0.8f + 0.12f * skill));
            wall.Breach = Hull.BreachFromIntegrity(wall.Integrity);
            wall.Welds++;
            wall.TotalWelds++;
            if (wall.Breach > 0f) { wall.Patched = true; wall.PatchQuality = 0.8f + 0.15f * skill; }
            MarkLog.Add(wall.Marks, world.Tick, $"{cm.Name}: 금속판을 덧대 용접했다");
            world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 실링폼 없이 {room.Name} 파공에 금속판을 덧대 용접했다", room, new[] { cm }, cell);
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 파공에 금속판을 덧대 용접했다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "금속판 용접", toils, $"{room.Name} 파공에 금속판을 덧대러 간다" + (needSuit ? " (우주복)" : ""));
    }

    // ── 외벽 수리: 예비 부품(판재)으로 용접 ──
    private static Job? RepairHull(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var cell = o.Target.Cell;
        var wall = w.Ship.WallAt(cell)!;
        var toils = Fetch(c, w, dist, ItemKind.Plate, 1);
        if (toils == null) { blocked = "금속판 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1.5f, Skill.Mechanics, cell.Center)
        {
            Resume = o,
            CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Plate,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            Consume(cm, ItemKind.Plate);
            float skill = cm.SkillLevel(Skill.Mechanics);
            wall.MaxIntegrity = MathF.Max(0.25f, wall.MaxIntegrity - Hull.WeldFatigue(skill));
            wall.Integrity = wall.MaxIntegrity * (0.92f + 0.07f * skill);
            wall.Breach = Hull.BreachFromIntegrity(wall.Integrity);
            wall.Welds++;
            wall.TotalWelds++;
            MarkLog.Add(wall.Marks, world.Tick, $"{cm.Name}: 용접 (최대 강도 {wall.MaxIntegrity * 100:0}%)");
            cm.Practice(Skill.Mechanics, 0.04f);
            cm.Stats.Repairs++;
            world.Board.Close(o);
            if (wall.Breach > 0f)
            {
                // 너무 여러 번 때운 벽: 용접해도 다시 샌다. 봉합을 유지하거나 구획을 포기해야 한다.
                wall.Patched = wall.Patched && wall.PatchQuality > 0.3f;
                world.Log.Add(world.Tick, LogKind.Warning, $"{Ko.EunNeun(o.Target.Label)} 너무 여러 번 때워서 용접해도 샌다 (최대 강도 {wall.MaxIntegrity * 100:0}%)", cm.Id);
            }
            else
            {
                wall.Patched = false;
                world.Log.Add(world.Tick, LogKind.Work,
                    $"{Ko.EulReul(o.Target.Label)} 용접으로 수리했다 (용접 {wall.Welds}회째 · 최대 강도 {wall.MaxIntegrity * 100:0}%)", cm.Id);
            }
            return true;
        }));
        return Wrap(a, o, c, w, "외벽 수리", toils, $"{o.Target.Label} 수리 ({o.Detail})");
    }

    // ── 환기 댐퍼 수동 조작 ──
    private static Job? OperateDamper(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        bool jammed = room.DamperJammed;
        float drill = w.History.Doctrine.ManualDrill ? 0.6f : 1f; // v9.2 교훈: 손으로 다루는 법을 익혔다
        toils.Add(new WorkToil((jammed ? 0.75f : room.DamperStuck ? 0.5f : 0.25f) * drill, Skill.Mechanics, room.DamperSpot.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            // v8: 열에 걸린 댐퍼는 힘으로 비틀어야 한다 (솜씨·침착함에 따라 될 수도 안 될 수도)
            if (room.DamperJammed)
            {
                float chance = 0.25f + 0.45f * cm.SkillLevel(Skill.Mechanics) + 0.15f * cm.Traits.Calm;
                if (!world.Rng.Chance(chance))
                {
                    world.Log.Add(world.Tick, LogKind.Warning, $"{room.Name} 환기 댐퍼가 열에 걸려 꿈쩍도 안 한다", cm.Id);
                    world.Board.Release(o, cm);
                    world.Board.Block(o, null, 0.3f);
                    return true;
                }
                room.DamperJammed = false;
                world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 열에 걸린 {room.Name} 환기 댐퍼를 비틀어 닫았다", room, new[] { cm });
            }
            if (room.DamperStuck)
            {
                room.DamperStuck = false;
                world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 걸린 {room.Name} 댐퍼 구동기를 손으로 풀었다", room, new[] { cm });
            }
            room.VentOpen = Hull.WantVentOpen(world, room);
            cm.Stats.Emergencies++;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 환기 댐퍼를 손으로 {(room.VentOpen ? "열었다" : "닫았다")}", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "댐퍼", toils, $"{room.Name} 환기 댐퍼를 {(room.VentOpen ? "닫으러" : "열러")} 간다");
    }

    // ── 화재 진압 ──
    private static Job? Extinguish(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;

        // 작업이 올라온 뒤 불이 옮겨 갔을 수 있으니, 지금 이 방에서 나한테 가장 가까운 불을 다시 찾는다
        Cell? aim = null;
        int best = int.MaxValue;
        foreach (var (cell, _) in w.Fire.Fires)
        {
            if (w.Ship.RoomAt(cell) != room) continue;
            foreach (var s in WorkTarget.AtCell(cell, room).Spots(w.Ship))
            {
                int d = dist.Get(s);
                if (d < 0) continue;
                if (w.IsSpotTaken(s, c)) d += 300;
                if (d < best) { best = d; aim = cell; at = s; }
            }
        }
        if (aim is not Cell fire) { w.Board.Close(o); return null; }

        List<Toil>? toils;
        bool water = false;
        if (c.Carrying?.Kind == ItemKind.Extinguisher) toils = new List<Toil>();
        else
        {
            toils = Fetch(c, w, dist, ItemKind.Extinguisher, 1);
            if (toils == null)
            {
                // v13.2 방침(불 끄는 수단: 물도 쓴다): 소화기가 없으면 소화전 호스로 물을 뿌린다 (바닥이 젖고 누전이 난다)
                if (w.Policies["firemethod"] != 2 || w.Water.Level < 20f) { blocked = "소화기 없음"; return null; }
                water = true;
                toils = Plans.DropOff(c, w, dist);
            }
        }
        toils.Add(new GotoToil(at));
        toils.Add(new SprayToil(fire) { Water = water });
        toils.Add(new DoToil((cm, world) =>
        {
            cm.Stats.Emergencies++;
            if (world.Fire.CountIn(room) == 0)
            {
                world.Board.Close(o);
                world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 불을 껐다", cm.Id);
                MarkLog.Add(room.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 불을 껐다");
                world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} {room.Name} 불을 껐다", room, new[] { cm });
            }
            else world.Board.Release(o, cm); // 다른 칸에 불이 남았다 → 다시 가장 가까운 불을 찾아 이어서
            return true;
        }));
        return Wrap(a, o, c, w, "화재 진압", toils, water ? $"소화기가 없다 — 호스를 끌고 {Ko.EuRo(room.Name)} 간다" : $"소화기를 들고 {Ko.EuRo(room.Name)} 간다");
    }

    // ── 구조: 쓰러진 사람을 업어서 치료 침대(없으면 안전한 방)로 ──
    private static Job? Rescue(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var patient = o.Target.Crew!;
        if (!patient.Down || patient.Dead || patient.CarriedBy != null) { w.Board.Close(o); return null; }

        // 내려놓을 곳: 비어 있는 치료 침대, 없으면 안전한 방의 빈 바닥
        Furniture? bed = w.Ship.FurnitureOf(FurnitureType.MedBed)
            .Where(b => (b.ReservedBy == null || b.ReservedBy == patient) && b.UseSpots.Count > 0 && dist.Reachable(b.UseSpots[0]))
            .OrderBy(b => dist.Get(b.UseSpots[0])).FirstOrDefault();
        Cell dest;
        if (bed != null) dest = bed.UseSpots[0];
        else
        {
            Cell? safe = null;
            int best = int.MaxValue;
            foreach (var room in w.Ship.Rooms)
            {
                if (Atmosphere.Danger(room) > 0.1f || room.Leaking || room.Abandoned || w.Fire.CountIn(room) > 0) continue;
                foreach (var cell in room.Cells)
                {
                    int d = dist.Get(cell);
                    if (d < 0 || d >= best || !w.Ship.IsOpenFloor(cell) || w.IsSpotTaken(cell, c)) continue;
                    best = d;
                    safe = cell;
                }
            }
            if (safe is not Cell s) { blocked = "옮길 곳 없음"; return null; }
            dest = s;
        }

        var toils = Plans.DropOff(c, w, dist);
        // v8: 선체 밖으로 튕겨 나간 사람은 우주복을 입고 에어락으로 나가서 데려온다
        if (patient.Outside && !EvaOut(c, w, dist, toils, out blocked)) return null;
        toils.Add(new GotoToil(at));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!patient.Down || patient.Dead || patient.CarriedBy != null) return false;
            if ((patient.Position - cm.Position).Length() > 2.2f) return false;
            patient.CarriedBy = cm;
            cm.CarryingPerson = patient;
            world.Log.Add(world.Tick, LogKind.Work, $"쓰러진 {Ko.EulReul(patient.Name)} 업었다", cm.Id);
            return true;
        }));
        toils.Add(new GotoToil(dest));
        toils.Add(new DoToil((cm, world) =>
        {
            if (cm.CarryingPerson != patient) return false;
            patient.CarriedBy = null;
            cm.CarryingPerson = null;
            patient.Position = dest.Center;
            patient.PreviousPosition = dest.Center;
            patient.Room = world.Ship.RoomAt(dest);
            if (bed != null && (bed.ReservedBy == null || bed.ReservedBy == patient))
            {
                bed.ReservedBy = patient;
                patient.CareBed = bed;
            }
            else patient.LaidSafe = true;
            cm.Stats.Rescues++;
            cm.Stats.Emergencies++;
            patient.ChangeAffinity(cm, 0.2f); // 목숨을 빚졌다
            world.Relations.Rescued(patient, cm); // v14.4 두고 갔다고 기억하던 사람이면 오해가 풀린다
            world.Relations.Remember(patient, cm, RelationReason.SavedMe, "쓰러진 나를 업어 옮겨 줬다");
            cm.ChangeAffinity(patient, 0.08f);
            MarkLog.Add(patient.Memory.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 업어 옮겨 줬다");
            MarkLog.Add(cm.Memory.Marks, world.Tick, $"쓰러진 {Ko.EulReul(patient.Name)} 업어 옮겼다");
            world.History.Add(world, HistoryKind.Response,
                $"{Ko.IGa(cm.Name)} 쓰러진 {Ko.EulReul(patient.Name)} 업어 {(bed != null ? "치료 침대로" : Ko.EuRo(patient.Room?.Name ?? "?"))} 옮겼다",
                patient.Room, new[] { cm, patient });
            world.Board.Close(o);
            world.Board.RequestScan();
            world.Log.Add(world.Tick, LogKind.Work,
                bed != null ? $"{Ko.EulReul(patient.Name)} 치료 침대에 눕혔다" : $"{Ko.EulReul(patient.Name)} {Ko.EuRo(patient.Room?.Name ?? "?")} 옮겼다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "구조", toils, $"쓰러진 {Ko.EulReul(patient.Name)} 구하러 간다");
    }

    // ── 구획 포기: 밖에서 격벽을 용접해 막고 환기 댐퍼를 닫는다 ──
    private static Job SealOffRoom(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var room = o.Target.Room!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, Skill.Mechanics, room.DamperSpot.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            room.Abandoned = true;
            room.AbandonedSince = world.Tick;
            room.AbandonReason = o.Detail;
            room.TimesAbandoned++;
            world.History.Abandons++;
            world.History.Lost($"{room.Name} 포기");
            MarkLog.Add(room.Marks, world.Tick, $"포기 — {o.Detail}" + (o.Decider != null ? $" ({o.Decider.Name} 결정)" : ""));
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.EulReul(room.Name)} 포기하고 격벽을 용접했다 — {o.Detail}" +
                (o.Decider != null ? $" (결정: {o.Decider.Name})" : ""), room, new[] { cm });
            room.VentOpen = false;
            room.Lockdown = true;
            foreach (var d in room.Doors)
                if (!d.IsExternal) d.Locked = true;
            cm.Stats.Emergencies++;
            world.Board.Close(o);
            world.Board.RequestScan();
            world.RaiseAlert($"{Ko.EulReul(room.Name)} 포기했다 — {o.Detail}", room, AlertLevel.Warning, shipWide: true);
            return true;
        }));
        return Wrap(a, o, c, w, "구획 폐쇄", toils, $"{room.Name} 격벽을 용접해 막으러 간다 ({o.Detail})");
    }

    // ── 구획 재개방: 용접을 끊고 댐퍼를 연다 → 공기 탱크로 재가압 ──
    private static Job ReopenRoom(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var room = o.Target.Room!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, Skill.Mechanics, room.DamperSpot.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (room.Leaking) return false;
            room.Abandoned = false;
            room.AbandonReason = null;
            room.VentOpen = true;
            if (room.Restoring)
            {
                // v8: 떨어져 나갔다 되찾은 방이 다시 숨 쉬기 시작한다
                room.Restoring = false;
                world.History.Add(world, HistoryKind.Structure, $"떨어져 나갔던 {Ko.EulReul(room.Name)} 완전히 되살렸다 — 재가압 시작", room, new[] { cm }, log: true);
                MarkLog.Add(room.Marks, world.Tick, "완전히 되살렸다");
            }
            MarkLog.Add(room.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 다시 열었다");
            world.History.Add(world, HistoryKind.Adaptation,
                $"{Ko.EulReul(room.Name)} {(world.Tick - room.AbandonedSince) / (float)SimTime.TicksPerHour:0}시간 만에 되찾았다" + (o.Decider != null ? $" (결정: {o.Decider.Name})" : ""),
                room, new[] { cm });
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{Ko.EulReul(room.Name)} 다시 열었다 — 재가압을 시작한다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "구획 재개방", toils, $"{Ko.EulReul(room.Name)} 다시 열러 간다");
    }

    // ── 부상 치료 ──
    private static Job? Treat(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var patient = o.Target.Crew!;
        var toils = Fetch(c, w, dist, ItemKind.MedKit, 1);
        bool kit = toils != null;
        if (!kit && w.Growth.NoFirstAid) { blocked = "구급 키트 없음"; return null; } // (시험: 재활만 견준다)
        // v15 키트가 바닥나면 손에 있는 천·소독약으로 응급 처치 (효과는 절반 — 키트는 배에서 못 만든다)
        toils ??= Plans.DropOff(c, w, dist);
        toils.AddRange(w.Soil.WashFirst(c, o.Urgency >= 0.9f, "치료")); // v14.7 급하지 않으면 손부터 (환자 곁에 가기 전에 — 씻는 사이 환자가 자리를 뜨지 않게)
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, Skill.Medicine, patient.Position)
        {
            CanContinue = (cm, _) => (!kit || cm.Carrying?.Kind == ItemKind.MedKit) && (patient.Position - cm.Position).Length() < 2.2f,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (kit) Consume(cm, ItemKind.MedKit);
            bool bandage = !kit && ItemsV15.Use(world, ItemKind.Bandage); // v15 키트가 없으면 붕대로
            float skill = cm.SkillLevel(Skill.Medicine), eff = kit ? 1f : bandage ? 0.75f : 0.5f;
            patient.Vitals.Health = MathF.Min(patient.Vitals.MaxHealth, patient.Vitals.Health + (0.15f + 0.25f * skill) * eff);
            patient.Vitals.Injury = MathF.Max(0f, patient.Vitals.Injury - (0.06f + 0.14f * skill) * eff);
            if (!kit) world.Log.Add(world.Tick, LogKind.Warning, $"구급 키트가 없어 {patient.Name}에게 응급 처치만 했다 ({(bandage ? "붕대" : "천과 소독약")})", cm.Id);
            patient.Vitals.TreatedTick = world.Tick;
            if (kit) world.Ailments.Treated(patient, cm); // v14.1 진단하고 약을 쓴다
            else world.Ailments.DiagnoseOnly(cm, patient); // v15.1 키트가 없으면 진단만 — 응급 처치는 약이 아니다
            world.Soil.OnTreated(cm, patient); // v14.7 더러운 손이면 상처가 곪기도
            if (patient != cm) world.Relations.Remember(patient, cm, RelationReason.NursedMe, "다쳤을 때 치료해 줬다"); // v14.4
            patient.ChangeAffinity(cm, 0.08f);
            cm.Practice(Skill.Medicine, 0.04f);
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{Ko.EulReul(patient.Name)} 치료했다 (체력 {patient.Vitals.Health * 100:0}%)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "치료", toils, $"{Ko.EulReul(patient.Name)} 치료하러 간다");
    }

    // ── 원자로 재기동 ──
    private static Job RestartReactor(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var f = o.Target.Furniture!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.75f, Skill.Engineering, f.Center)
        {
            CanContinue = (_, world) => !world.Power.ReactorOnline && world.Power.CoolingCapacity >= PowerGrid.RestartCoolingKw,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            cm.Practice(Skill.Engineering, 0.05f);
            cm.Stats.Emergencies++;
            // 서투르거나 냉각 여유가 빠듯하면 노심이 안정되지 않는다. 실패할 때마다 제어계가 상한다.
            float skill = cm.SkillLevel(Skill.Engineering);
            float margin = Math.Clamp((world.Power.CoolingCapacity - PowerGrid.RestartCoolingKw) / 16f, 0f, 1f);
            float success = 0.45f + 0.4f * skill + 0.25f * margin - (cm.Needs.Rest < 0.2f ? 0.1f : 0f) - (cm.Needs.Stress > 0.7f ? 0.1f : 0f)
                            + 0.1f * (cm.Traits.Calm - 0.5f);
            if (!world.Rng.Chance(success))
            {
                var core = f.Machine!;
                core.Wear = MathF.Min(1f, core.Wear + 0.12f);
                world.Board.Release(o, cm);
                world.Board.Block(o, null, 0.25f);
                world.Log.Add(world.Tick, LogKind.Warning, "원자로 재기동 실패 — 노심 반응이 안정되지 않았다", cm.Id);
                if (world.Rng.Chance(0.25f)) world.Machines.Break(core, FaultKind.ControlFault);
                return true;
            }
            world.Power.RestartReactor();
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, "원자로를 다시 켰다", cm.Id);
            world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 원자로를 다시 켰다", f.Room, new[] { cm });
            return true;
        }));
        return Wrap(a, o, c, w, "원자로 재기동", toils, "원자로를 재기동하러 간다");
    }

    // ── 보조 발전기 기동 ──
    private static Job StartAux(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var f = o.Target.Furniture!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.3f, Skill.Electrical, f.Center)
        {
            CanContinue = (_, world) => !world.Power.AuxRunning,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            cm.Stats.Emergencies++;
            // 오래 쉬던 기계라 한 번에 안 걸릴 때도 있다
            if (world.Rng.Chance(0.25f - 0.15f * cm.SkillLevel(Skill.Electrical)))
            {
                world.Board.Release(o, cm);
                world.Log.Add(world.Tick, LogKind.Warning, "보조 발전기 시동이 걸리지 않았다 — 다시 당긴다", cm.Id);
                return true;
            }
            world.Power.StartAux();
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, "보조 발전기 시동 손잡이를 당겼다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "보조 발전기", toils, "보조 발전기를 돌리러 간다");
    }

    // ═════════════════════════════ 적응: 우회와 땜질 (v5) ═════════════════════════════

    // ── 원자로 저출력 수동 기동: 펌프도 배터리도 보조 발전기도 없을 때, 제어봉을 손으로 조금씩 뺀다 ──
    private static Job ManualStart(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var f = o.Target.Furniture!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1f, Skill.Engineering, f.Center)
        {
            CanContinue = (_, world) => !world.Power.ReactorOnline,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            cm.Practice(Skill.Engineering, 0.06f);
            cm.Stats.Emergencies++;
            world.Adapt.ManualStarts++;
            var core = f.Machine!;
            float skill = cm.SkillLevel(Skill.Engineering);
            float success = 0.25f + 0.6f * skill - (cm.Needs.Rest < 0.2f ? 0.1f : 0f) - (cm.Needs.Stress > 0.7f ? 0.1f : 0f)
                            + 0.1f * (cm.Traits.Calm - 0.5f);
            if (!world.Rng.Chance(success))
            {
                world.Adapt.ManualStartFails++;
                core.Wear = MathF.Min(1f, core.Wear + 0.1f);
                world.Board.Release(o, cm);
                world.Board.Block(o, null, 0.5f);
                string hurt = "";
                if (world.Rng.Chance(0.2f))
                {
                    NeedsSystem.AddInjury(cm.Vitals, 0.15f, "원자로 수동 기동 중 화상");
                    hurt = " · 뜨거운 배관에 손을 데었다";
                }
                world.Log.Add(world.Tick, LogKind.Warning, "원자로 수동 기동 실패 — 노심이 반응하지 않는다" + hurt, cm.Id);
                if (world.Rng.Chance(0.2f)) world.Machines.Break(core, FaultKind.ControlFault);
                return true;
            }
            world.Power.ManualLowPowerStart();
            world.Board.Close(o);
            world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 제어봉을 손으로 빼 원자로를 저출력으로 살렸다", f.Room, new[] { cm });
            world.Log.Add(world.Tick, LogKind.Work, "제어봉을 손으로 빼서 원자로를 저출력으로 살렸다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "수동 기동", toils, "원자로를 손으로 기동하러 간다 (자연 순환 냉각)");
    }

    // ── 보조 발전기 급유: 연료통 → 보조 발전기 ──
    private static Job? Refuel(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var f = o.Target.Furniture!;
        var toils = Fetch(c, w, dist, ItemKind.Fuel, 1);
        if (toils == null) { blocked = "연료통 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.25f, Skill.Mechanics, f.Center)
        {
            CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Fuel,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            Consume(cm, ItemKind.Fuel);
            world.Power.AddFuel(PowerGrid.FuelCanHours);
            world.Adapt.Refuels++;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"보조 발전기에 연료통을 부었다 (연료 {world.Power.AuxFuel:0}시간분)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "급유", toils, "연료통을 들고 보조 발전기로 간다");
    }

    // ── 임시 배선: 케이블 두 타래로 살아 있는 회로에서 죽은 회로로 전기를 끌어온다 ──
    private static Job? InstallJumper(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var panel = o.Target.Furniture!;
        int to = o.Circuit;
        var toils = Fetch(c, w, dist, ItemKind.Cable, PowerGrid.JumperCables);
        if (toils == null) { blocked = "케이블 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1f, Skill.Electrical, panel.Center)
        {
            CanContinue = (cm, world) => cm.Carrying?.Kind == ItemKind.Cable && !world.Power.CircuitLive[to] && world.Power.FeedingJumper(to) == null,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            int from = world.Power.JumperSource(to);
            if (from < 0) return false;
            Consume(cm, ItemKind.Cable, PowerGrid.JumperCables);
            float skill = cm.SkillLevel(Skill.Electrical);
            var j = world.Power.AddJumper(from, to, cm.Cell, 0.8f + 0.4f * skill);
            cm.Practice(Skill.Electrical, 0.04f);
            cm.Stats.Repairs++;
            world.Adapt.Jumpers++;
            world.Board.Close(o);
            MarkLog.Add(panel.Machine!.Marks, world.Tick, $"{cm.Name}: {PowerGrid.CircuitName(from)}→{PowerGrid.CircuitName(to)} 임시 배선");
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} {PowerGrid.CircuitName(from)} 회로에서 {PowerGrid.CircuitName(to)} 회로로 임시 배선을 깔았다",
                panel.Room, new[] { cm }, cm.Cell);
            world.Log.Add(world.Tick, LogKind.Work,
                $"{PowerGrid.CircuitName(from)} 회로에서 {PowerGrid.CircuitName(to)} 회로로 임시 배선을 깔았다 (정격 {j.Capacity:0}kW — 넘기면 달아오른다)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "임시 배선", toils, $"{PowerGrid.CircuitName(to)} 회로에 임시 배선을 깔러 간다");
    }

    // ── 절전 차단 / 회로 복귀: 배전반에서 회로를 손으로 내리거나 올린다 ──
    private static Job SwitchCircuit(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var panel = o.Target.Furniture!;
        bool off = o.Kind == WorkKind.ShedLoad;
        int i = o.Circuit;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.2f, Skill.Electrical, panel.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Power.SetManualOff(i, off);
            if (off) world.Adapt.LoadSheds++;
            world.Board.Close(o);
            if (off)
                world.RaiseAlert($"{PowerGrid.CircuitName(i)} 회로({PowerGrid.CircuitRole(i)})를 일부러 내렸다 — 필수 회로에 전기를 몰아준다",
                    panel.Room, AlertLevel.Warning, shipWide: true);
            else world.Log.Add(world.Tick, LogKind.Work, $"{PowerGrid.CircuitName(i)} 회로({PowerGrid.CircuitRole(i)})를 다시 올렸다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, off ? "절전" : "회로 복귀", toils,
            off ? $"배전반에서 {PowerGrid.CircuitName(i)} 회로를 내리러 간다" : $"{PowerGrid.CircuitName(i)} 회로를 다시 올리러 간다");
    }

    // ── v9.4 문 구동기 수리: 모터를 갈거나, 없으면 케이블·금속판으로 임시 구동기를 엮는다 ──
    private static Job? RepairDoor(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var d = o.Target.Door!;
        bool motor = w.Ship.CountStored(ItemKind.Motor) >= 1;
        if (!d.MotorBroken && !(d.MotorMk1 && motor)) { w.Board.Close(o); return null; }
        var cost = motor ? new[] { (ItemKind.Motor, 1) } : new[] { (ItemKind.Cable, 1), (ItemKind.Plate, 1) };
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = motor ? "모터를 가져올 수 없음" : "모터도, 케이블 1 + 금속판 1도 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(motor ? 0.6f : 0.9f, Skill.Mechanics, d.Cell.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!d.MotorBroken && !(d.MotorMk1 && motor)) { world.Board.Close(o); return true; }
            if (!UseAll(cm, cost)) return false;
            d.MotorBroken = false;
            d.MotorMk1 = !motor;
            cm.Stats.Repairs++;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, motor ? $"문 구동기 모터를 갈았다 ({d.RoomA?.Name}·{d.RoomB?.Name})"
                : $"모터가 없어 케이블·금속판으로 임시 구동기를 엮었다 — 느리다 ({d.RoomA?.Name}·{d.RoomB?.Name})", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "문 수리", toils, $"{o.Title} ({o.Detail})");
    }

    // ── v9.4 조명 수리: 케이블 1 ──
    private static Job? FixLights(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        if (!room.LightsOut) { w.Board.Close(o); return null; }
        var cost = new[] { (ItemKind.Cable, 1) };
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = "케이블 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, Skill.Electrical, room.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!room.LightsOut) { world.Board.Close(o); return true; }
            if (!UseAll(cm, cost)) return false;
            float hours = (world.Tick - room.LightsOutSince) / (float)SimTime.TicksPerHour;
            room.LightsOut = false;
            cm.Stats.Repairs++;
            world.Board.Close(o);
            MarkLog.Add(room.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 조명을 고쳤다");
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 조명을 고쳤다 ({hours:0}시간 만에 밝아졌다)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "조명 수리", toils, $"{room.Name} 조명을 고치러 간다");
    }

    // ── v9.3 저출력 운영: 배전반에서 설비를 하나씩 내리고(펌프 하나·산소 발생기 하나·빈 치료 침대·엔진·콘솔) 먹을 것에 전기를 돌린다 ──
    private static Job SetBrownout(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var panel = o.Target.Furniture!;
        bool on = o.Kind == WorkKind.Brownout;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(on ? 0.4f : 0.25f, Skill.Electrical, panel.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            var p = world.Power;
            world.Board.Close(o);
            if (on == p.Brownout) return true;
            if (on)
            {
                p.Brownout = true;
                p.BrownoutSince = world.Tick;
                p.Brownouts++;
                world.Adapt.LoadSheds++;
                world.History.Add(world, HistoryKind.Adaptation,
                    $"{Ko.IGa(cm.Name)} 저출력 운영으로 돌렸다 — 원자로 {p.ReactorLimit:0}kW로 수요 {p.Demand:0}kW를 못 댄다: " +
                    "펌프·산소 발생기는 한 대로 버틸 수 있으면 하나씩, 빈 치료 침대·엔진·콘솔을 내리고 먹을 것에 전기를 돌린다", panel.Room, new[] { cm });
                world.RaiseAlert("저출력 운영 — 설비를 골라 내리고 재배대·조리에 전기를 돌린다", panel.Room, AlertLevel.Warning, shipWide: true);
            }
            else
            {
                float hours = (world.Tick - p.BrownoutSince) / (float)SimTime.TicksPerHour;
                p.Brownout = false;
                world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 저출력 운영을 풀었다 ({hours:0}시간 만) — 내린 설비를 다시 올렸다", panel.Room, new[] { cm });
            }
            world.Board.RequestScan();
            return true;
        }));
        return Wrap(a, o, c, w, on ? "저출력 운영" : "정상 운전", toils, on ? "배전반에서 설비를 골라 내리러 간다 (저출력 운영)" : "배전반에서 내린 설비를 다시 올리러 간다");
    }

    // ── 부품 뜯어 쓰기: 덜 중요한 설비를 열어 부품을 꺼내 선반에 둔다. 그 설비는 멈춘다. ──
    private static Job? Cannibalize(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var body = o.Target.Furniture!;
        var donor = body.Machine!;
        var part = o.Product ?? ItemKind.Motor;
        if (donor.Has(FaultKind.Stripped)) { w.Board.Close(o); return null; }
        var (shelf, shelfSpot) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.Shelf && f.Storage!.Accepts(part) && f.Storage.Free > 0);
        if (shelf == null) { blocked = "둘 선반 없음"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1.2f, Skill.Mechanics, body.Center)
        {
            CanContinue = (_, _) => !donor.Has(FaultKind.Stripped) && body.ReservedBy == null,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (donor.Has(FaultKind.Stripped)) return false;
            donor.Faults.Add(new Fault { Kind = FaultKind.Stripped, Since = world.Tick, PartOverride = part });
            world.UsedParts.Add(part); // v12.1 떼어 온 중고 부품 (검사하지 않고 쓰면 불량이 잦다)
            world.Parts.Salvaged(part, 1, donor.Name); // v14.6 어디서 떼어 왔는지
            donor.Condition = MathF.Max(0.2f, donor.Condition - 0.1f);
            donor.Active = false;
            cm.Carrying = new ItemStack(part, 1);
            cm.Practice(Skill.Mechanics, 0.03f);
            world.Adapt.Stripped++;
            donor.TimesStripped++;
            world.Board.Close(o);
            MarkLog.Add(donor.Marks, world.Tick, $"{cm.Name}: {ItemKinds.Name(part)} 적출");
            world.History.Lost($"{donor.Name} 적출");
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} {Ko.EulReul(donor.Name)} 뜯어 {Ko.EulReul(ItemKinds.Name(part))} 꺼냈다" +
                (o.Decider != null ? $" (결정: {o.Decider.Name})" : ""), body.Room, new[] { cm });
            world.Board.RequestScan();
            world.Log.Add(world.Tick, LogKind.Warning,
                $"{Ko.EulReul(donor.Name)} 뜯어 {Ko.EulReul(ItemKinds.Name(part))} 꺼냈다 — {Ko.EunNeun(donor.Name)} 이제 멈춘다", cm.Id);
            return true;
        }));
        toils.Add(new GotoToil(shelfSpot));
        toils.Add(new PutToil(shelf));
        return Wrap(a, o, c, w, "부품 뜯기", toils, $"{Ko.EulReul(donor.Name)} 뜯어 {Ko.EulReul(ItemKinds.Name(part))} 꺼내러 간다");
    }

    // ── 격벽 수동 폐쇄: 전기가 없는 문은 손잡이를 돌려 닫고 잠근다 ──
    private static Job CrankDoor(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var door = o.Target.Door!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        bool jammed = door.JammedOpen;
        toils.Add(new WorkToil((jammed ? 0.5f : 0.2f) * (w.History.Doctrine.ManualDrill ? 0.6f : 1f), Skill.Mechanics, door.Cell.Center)
        {
            CanContinue = (_, _) => jammed ? door.JammedOpen : !door.Locked,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (door.JammedOpen)
            {
                float chance = 0.3f + 0.4f * cm.SkillLevel(Skill.Mechanics) + 0.1f * cm.Traits.Calm;
                if (!world.Rng.Chance(chance))
                {
                    world.Log.Add(world.Tick, LogKind.Warning, "휜 문이 꿈쩍도 안 한다", cm.Id);
                    world.Board.Release(o, cm);
                    world.Board.Block(o, null, 0.3f);
                    return true;
                }
                door.JammedOpen = false;
                door.Openness = 0f;
                world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 불에 휜 문을 지렛대로 억지로 닫았다 ({door.RoomA?.Name}·{door.RoomB?.Name})", o.Target.Room, new[] { cm });
            }
            door.Locked = true;
            cm.Stats.Emergencies++;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, door.Powered ? $"자동화가 꺼진 격벽을 손으로 잠갔다 ({o.Target.Room?.Name})"
                : $"{(door.MotorBroken ? "구동기가 망가진" : "전기가 없는")} 격벽을 손으로 돌려 닫았다 ({o.Target.Room?.Name})", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "격벽", toils, $"{o.Target.Room?.Name} 격벽을 손으로 닫으러 간다");
    }

    // ── v10.1 비상수단: 주 컴퓨터 없이 통신실 레이더 화면을 사람이 지킨다 (앉아 있는 동안 수동 판독 경보) ──
    private static Job? RadarWatch(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var console = o.Target.Furniture!;
        if (w.Automation.MainOnline || !w.Sensors.Online) { w.Board.Close(o); return null; }
        if (w.Sensors.Operator is CrewMember op && op != c && op.Job?.Order?.Kind == WorkKind.RadarWatch) { blocked = $"{Ko.IGa(op.Name)} 보고 있다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(3f, Skill.Piloting, console.Center)
        {
            CanContinue = (cm, world) => !world.Automation.MainOnline && world.Sensors.Online && !o.Closed && world.Tick - o.LastSeen < SimTime.Minutes(20),
        });
        return Wrap(a, o, c, w, "레이더 감시", toils, $"{o.Title} — {o.Detail}");
    }

    // ── 방 용도 변경: 간이침대를 놓고 잘 곳 없는 사람들에게 배정한다 ──
    private static Job? RepurposeRoom(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        if (Adaptation.CotCells(w, room).Count == 0) { blocked = "놓을 자리 없음"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1.5f, Skill.Mechanics, room.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            var homeless = Adaptation.Homeless(world);
            var cells = Adaptation.CotCells(world, room);
            int placed = 0;
            foreach (var who in homeless)
            {
                if (placed >= cells.Count) break;
                var cot = world.Ship.AddFurniture(FurnitureType.Cot, cells[placed++]);
                cot.Owner = who;
                who.HomeBed ??= who.Bed;
                if (who.Bed != null && who.Bed.Owner == who && who.Bed != who.HomeBed) who.Bed.Owner = null;
                who.Bed = cot;
            }
            world.Board.Close(o);
            if (placed == 0) return true;
            world.Paths.Invalidate();
            if (room.Purpose == null || !room.Purpose.StartsWith("임시 침실") || room.Purpose.Contains("비어")) world.Adapt.Repurposed++;
            room.Purpose = "임시 침실";
            world.RaiseAlert($"{room.Name}에 간이침대 {placed}개를 놓았다 — 이제 임시 침실이다", room, AlertLevel.Notice, shipWide: false);
            MarkLog.Add(room.Marks, world.Tick, $"간이침대 {placed}개 — 임시 침실");
            world.History.Add(world, HistoryKind.Adaptation, $"{room.Name}에 간이침대 {placed}개 — 이제 임시 침실이다", room, new[] { cm });
            return true;
        }));
        return Wrap(a, o, c, w, "간이침대", toils, $"{Ko.EuRo(room.Name)} 간이침대를 옮기러 간다");
    }

    // ═════════════════════════════ 재료와 순환 (v6) ═════════════════════════════

    // ── Mk.1 임시품: 정품 부품을 구할 수도 만들 수도 없으면, 기본 수리재로 현장에서 짜 맞춘다 ──
    private static Job? InstallSubstitute(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var f = o.Target.Furniture!;
        var m = f.Machine!;
        var cost = Faults.SubstituteCost(f.Type);
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = $"기본 수리재 부족 ({Cost(cost)})"; return null; }
        var main = cost[0].kind;
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(3f, m.Spec.Skill, f.Center)
        {
            Resume = o,
            CanContinue = (cm, _) => cm.Carrying?.Kind == main,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!UseAll(cm, cost)) return false;
            m.Faults.RemoveAll(x => x.Circuit < 0 && x.Kind != FaultKind.Stripped);
            m.Grade = MachineGrade.Mk1;
            m.Condition = MathF.Max(m.Condition, 0.6f);
            m.Wear = 0.15f;
            m.Substitutions++;
            MarkLog.Add(m.Marks, world.Tick, $"{cm.Name}: Mk.1 임시품");
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} {m.Name}에 Mk.1 임시품을 달았다", m.Body.Room, new[] { cm });
            cm.Practice(m.Spec.Skill, 0.04f);
            cm.Stats.Repairs++;
            world.Adapt.Substitutes++;
            world.Board.Close(o);
            world.Board.RequestScan();
            world.RaiseAlert($"{m.Name}에 Mk.1 임시품을 달았다 — 출력 65% · 전력 130% · 빨리 닳고 자주 고장 난다", m.Body.Room,
                AlertLevel.Notice, shipWide: m.Spec.Critical);
            return true;
        }));
        return Wrap(a, o, c, w, "Mk.1 대체", toils, $"{m.Name}에 Mk.1 임시품을 짜 맞추러 간다 ({Cost(cost)})");
    }

    // ── 정품 복원: Mk.1 임시품을 떼고 제대로 된 부품으로 ──
    private static Job? RestoreGrade(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var f = o.Target.Furniture!;
        var m = f.Machine!;
        if (m.Grade != MachineGrade.Mk1) { w.Board.Close(o); return null; }
        var cost = new[] { (Faults.KeyPart(f.Type), 1), (ItemKind.Plate, 1) };
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = $"부품 부족 ({Cost(cost)})"; return null; }
        var main = cost[0].Item1;
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(2.5f, m.Spec.Skill, f.Center)
        {
            Resume = o,
            CanContinue = (cm, _) => cm.Carrying?.Kind == main,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!UseAll(cm, cost)) return false;
            m.Grade = MachineGrade.Standard;
            m.Condition = MathF.Max(m.Condition, 0.75f);
            m.Wear = 0.1f;
            cm.Practice(m.Spec.Skill, 0.03f);
            world.Adapt.Restored++;
            m.Restores++;
            MarkLog.Add(m.Marks, world.Tick, $"{cm.Name}: 정품 복원");
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} {Ko.EulReul(m.Name)} 정품으로 되돌렸다", m.Body.Room, new[] { cm });
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{m.Name}의 Mk.1 임시품을 떼고 정품으로 되돌렸다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "정품 복원", toils, $"{Ko.EulReul(m.Name)} 정품으로 되돌리러 간다");
    }

    // ── 외벽 패널 교체: 구조재와 금속판으로 피로한 패널을 통째로 간다 (용접과 달리 피로가 풀린다) ──
    private static Job? ReplacePanel(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var cell = o.Target.Cell;
        var wall = w.Ship.WallAt(cell)!;
        var cost = new[] { (ItemKind.Structure, 2), (ItemKind.Plate, 2) };
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = $"재료 부족 ({Cost(cost)})"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(3f, Skill.Mechanics, cell.Center)
        {
            Resume = o,
            CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Structure && wall.Breach <= 0f,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!UseAll(cm, cost)) return false;
            float skill = cm.SkillLevel(Skill.Mechanics);
            wall.MaxIntegrity = 0.94f + 0.06f * skill;
            wall.Integrity = wall.MaxIntegrity;
            wall.Breach = 0f;
            wall.Patched = false;
            wall.Welds = 0;
            wall.Replacements++;
            wall.Reinforced = false;
            MarkLog.Add(wall.Marks, world.Tick, $"{cm.Name}: 패널 통째로 교체");
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} {o.Target.Label} 패널을 통째로 갈았다", o.Target.Room, new[] { cm }, cell);
            cm.Practice(Skill.Mechanics, 0.04f);
            cm.Stats.Repairs++;
            world.Adapt.PanelsReplaced++;
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{o.Target.Label} 패널을 통째로 갈았다 (최대 강도 {wall.MaxIntegrity * 100:0}%)", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "패널 교체", toils, $"{o.Target.Label} 패널을 갈러 간다");
    }

    // ═════════════════════════════ 진화 (v7) ═════════════════════════════

    // ── 개조: 재료를 모아 → 그 자리에서 → 고쳐 짠다 (보강판, 배터리 모듈, 예비 배선, Mk.3, 침실 정비) ──
    private static Job? Upgrade(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var kind = o.Upgrade ?? UpgradeKind.Mk3;
        var cost = Evolution.Cost(o);
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = $"재료 부족 ({Cost(cost)})"; return null; }
        var main = cost[0].kind;
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(Evolution.Hours(kind), o.Skill, o.Target.Center)
        {
            Resume = o,
            CanContinue = (cm, _) => cm.Carrying?.Kind == main,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!UseAll(cm, cost)) return false;
            cm.Practice(o.Skill, 0.04f);
            world.Board.Close(o);
            if (!Evolution.Apply(world, o, cm))
            {
                // 그새 자리가 사라졌다: 재료는 되돌린다
                foreach (var (k, n) in cost)
                    foreach (var box in world.Ship.Containers)
                        if (box.Type == FurnitureType.Shelf && box.Storage!.Add(k, n) > 0) break;
                world.History.PlannedUpgrade = null;
            }
            return true;
        }));
        return Wrap(a, o, c, w, "개조", toils, $"{o.Title} ({Cost(cost)})");
    }

    // ── 임시 정비실: 다른 방 한쪽에 Mk.1 작업대를 짠다 (정비실을 포기했을 때) ──
    private static Job? BuildWorkshop(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        var cost = new[] { (ItemKind.Plate, 2), (ItemKind.Cable, 1) };
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = $"재료 부족 ({Cost(cost)})"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(3f, Skill.Mechanics, room.Center)
        {
            Resume = o,
            CanContinue = (cm, _) => cm.Carrying?.Kind == ItemKind.Plate,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (Adaptation.BenchCell(world, room) is not Cell spot) return false;
            if (!UseAll(cm, cost)) return false;
            var bench = world.Ship.AddFurniture(FurnitureType.Workbench, spot);
            bench.Machine!.Grade = MachineGrade.Mk1;
            bench.Machine.Condition = 0.7f;
            world.Paths.Invalidate();
            room.Purpose = "임시 정비실";
            world.Adapt.Workshops++;
            MarkLog.Add(room.Marks, world.Tick, "임시 작업대 — 임시 정비실");
            world.History.Add(world, HistoryKind.Adaptation, $"{room.Name} 한쪽에 임시 작업대를 짰다 — 이제 임시 정비실이다", room, new[] { cm });
            world.Board.Close(o);
            world.Board.RequestScan();
            world.RaiseAlert($"{room.Name} 한쪽에 임시 작업대를 짰다 — 이제 임시 정비실이다", room, AlertLevel.Notice, shipWide: true);
            return true;
        }));
        return Wrap(a, o, c, w, "임시 작업대", toils, $"{room.Name}에 임시 작업대를 짜러 간다");
    }

    /// <summary>v11.1: 산소 발생기를 모두 잃은 배가 다른 방 한쪽에 임시 산소 발생기를 짠다 (전해조를 손으로 엮은 Mk.1 — 몫이 작다).</summary>
    private static Job? BuildOxygen(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        // 다른 사람이 이미 하나 짰으면 (방이 바뀌어 일감이 둘로 갈렸어도) 더 짜지 않는다 — 전기가 없어 안 도는 건 발생기가 없는 게 아니다
        static bool HaveOne(World world) => world.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Any(f => !f.Stowed && !f.Room.Detached && !f.Room.Abandoned
                                                                                                        && !f.Machine!.Has(FaultKind.Wrecked) && !f.Machine.Has(FaultKind.Stripped));
        if (HaveOne(w)) { blocked = "이미 산소 발생기가 있다"; return null; }
        var cost = new[] { (ItemKind.PowerController, 1), (ItemKind.Cable, 2), (ItemKind.Plate, 2) };
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = $"재료 부족 ({Cost(cost)})"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(2.5f, Skill.Mechanics, room.Center) { Resume = o, CanContinue = (cm, world) => !HaveOne(world) });
        toils.Add(new DoToil((cm, world) =>
        {
            if (HaveOne(world)) { world.Board.Close(o); return true; }
            if (Adaptation.BenchCell(world, room) is not Cell spot) return false;
            if (!UseAll(cm, cost)) return false;
            var gen = world.Ship.AddFurniture(FurnitureType.OxygenGenerator, spot);
            gen.Machine!.Grade = MachineGrade.Mk1;
            gen.Machine.Condition = 0.7f;
            world.Paths.Invalidate();
            world.Structure.Touch();
            world.Adapt.Substitutes++;
            MarkLog.Add(room.Marks, world.Tick, "임시 산소 발생기");
            MarkLog.Add(gen.Machine.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 전해조를 손으로 엮어 짰다");
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} {room.Name} 한쪽에 임시 산소 발생기를 짰다 — 몫은 작지만 숨은 쉰다", room, new[] { cm });
            world.RaiseAlert($"{room.Name}에 임시 산소 발생기 — 몫은 작지만 숨은 쉰다", room, AlertLevel.Notice, shipWide: true);
            world.Board.Close(o);
            world.Board.RequestScan();
            return true;
        }));
        return Wrap(a, o, c, w, "임시 산소 발생기", toils, $"{room.Name}에 임시 산소 발생기를 짜러 간다", LogKind.Warning);
    }

    /// <summary>v9.2: 주 컴퓨터를 잃은 배가 다른 방 한쪽에 임시 제어 컴퓨터를 짠다 (콘솔 부품을 엮은 Mk.1).</summary>
    private static Job? BuildComputer(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        var cost = new[] { (ItemKind.Electronics, 2), (ItemKind.Cable, 2) };
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = $"재료 부족 ({Cost(cost)})"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(4f, Skill.Electrical, room.Center)
        {
            Resume = o,
            CanContinue = (cm, world) => world.Automation.Gone && cm.Carrying?.Kind == ItemKind.Electronics,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (Adaptation.BenchCell(world, room) is not Cell spot) return false;
            if (!UseAll(cm, cost)) return false;
            var comp = world.Ship.AddFurniture(FurnitureType.MainComputer, spot);
            comp.Machine!.Grade = MachineGrade.Mk1;
            comp.Machine.Condition = 0.7f;
            world.Paths.Invalidate();
            MarkLog.Add(room.Marks, world.Tick, "임시 제어 컴퓨터");
            MarkLog.Add(comp.Machine.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 콘솔 부품을 엮어 짰다");
            world.History.Add(world, HistoryKind.Adaptation, $"{Ko.IGa(cm.Name)} {room.Name} 한쪽에 임시 제어 컴퓨터를 짰다 — 느리지만 자동화가 돌아온다", room, new[] { cm });
            world.Board.Close(o);
            world.Board.RequestScan();
            return true;
        }));
        return Wrap(a, o, c, w, "임시 제어 컴퓨터", toils, $"{room.Name}에 임시 제어 컴퓨터를 짜러 간다");
    }
}
