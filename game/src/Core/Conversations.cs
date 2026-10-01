using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v14.4 목적 있는 대화 · 엇갈린 기억 · 관계의 이유가 판단에 걸린다
//
// 말에는 까닭과 뒷일이 있다:
//  · 힘들어 보이는 동료에게 이유를 묻고, 다음 근무를 대신 선다.
//  · 고장 소문(당직 기록)을 듣고 현장을 확인하러 간다 — 말한 사람을 믿으면 판단까지 받아들이고, 못 믿으면 직접 본다.
//  · 다친 사람을 위로하며 좋아하는 음식을 가져다준다.
//  · 신입(새로 탄 사람·커 가는 아이)에게 지난 사고와 그 뒤 바뀐 수칙을 일러 준다.
//  · 다툰 둘이 사과하고 화해한다.
//  · "나를 두고 갔다"고 기억하는 사람에게 진실을 말한다 (우주복을 가지러 갔던 거였다).
// 말은 아는 만큼 한다 — 직접 본 사람은 구체적으로, 들은 사람은 추측으로.

public enum TalkTopic { Worry, Rumor, Comfort, Teach, Reconcile, Truth }

public sealed class TalkStats
{
    public int Worries, Covers, Rumors, Checks, Distrusted, Comforts, Teachings, Apologies, Reconciled, Refused, Truths, Abandons, Covered, Blamed;
    public override string ToString() =>
        $"걱정 {Worries}(근무 대신 {Covers}) · 소문 {Rumors}(확인하러 감 {Checks} · 못 믿어 직접 {Distrusted}) · 위로 {Comforts} · 신입 가르침 {Teachings} · 사과 {Apologies}(화해 {Reconciled} · 거절 {Refused}) · 진실 {Truths} · 두고 감 {Abandons} · 감싸 줌 {Covered} · 탓함 {Blamed}";
}

/// <summary>목적을 가지고 누군가에게 말을 건다.</summary>
public sealed class ReachOutActivity : Activity
{
    public override string Id => "reachout";
    public override string Label => "말 걸기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.IsChild && c.Age < 12f || c.Down) return (0f, "—");
        // 내 몸부터 (치료를 기다리는 부상 · 누워야 할 병 · 녹초) — 남을 챙기러 돌아다니지 않는다
        if (NeedsCare(c, w)) return (0f, "내 몸부터");
        // 이미 말을 꺼냈으면 끝까지 듣고 말한다 (꺼낸 순간 "최근에 말했다"가 되어 점수가 0으로 떨어지던 것)
        if (c.Job?.Activity == this && c.TalkingTo is CrewMember now && !now.Dead && now.IsAwake) return (0.6f, $"{Ko.WaGwa(now.Name)} 이야기 중");
        var pick = w.Relations.PickTalk(c, dist);
        if (pick is not var (target, topic, urgency, why)) return (0f, "—");
        float score = 0.16f + 0.4f * urgency + 0.15f * c.Traits.Sociability + (c.Value == CrewValue.People ? 0.08f : 0f);
        if (OnShift(c, w) && topic is not (TalkTopic.Rumor or TalkTopic.Comfort)) score -= 0.12f;
        if (Bedtime(c, w)) score -= 0.3f;
        return (MathF.Max(0f, score), $"{target.Name} — {why}");
    }

    /// <summary>치료를 기다리거나 누워야 하는 사람 (말 걸러 다니지 않고, 위로는 치료가 끝난 뒤에).</summary>
    internal static bool NeedsCare(CrewMember c, World w) =>
        c.Vitals.Injury >= 0.25f && w.Tick - c.Vitals.TreatedTick > SimTime.Hours(6) || c.Vitals.Health < 0.5f || c.Fx.Bed > 0.3f || c.Needs.Rest < 0.12f;

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var pick = w.Relations.PickTalk(c, dist);
        if (pick is not var (target, topic, _, _)) return null;
        var spot = Cell.Dirs8.Select(d => target.Cell + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell s) return null;
        // 위로: 좋아하는 음식을 챙겨 간다 (있으면)
        var toils = topic == TalkTopic.Comfort && w.Ship.CountStored(ItemKind.Meal) > 0 ? WorkPlanners.FetchItem(c, w, dist, ItemKind.Meal, 1) ?? Plans.DropOff(c, w, dist) : Plans.DropOff(c, w, dist);
        toils.Add(new GotoToilLate(cm => Cell.Dirs8.Select(d => target.Cell + d).Where(x => w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - cm.Position).LengthSquared()).Cast<Cell?>().FirstOrDefault() ?? s));
        bool opened = false;
        toils.Add(new WaitToil(SimTime.Minutes(w.Rng.Range(10f, 18f)), Pose.Standing, null, minTicks: SimTime.Minutes(4))
        {
            EveryTick = (cm, world) =>
            {
                const float dt = 1f / SimTime.TicksPerHour;
                if ((target.Position - cm.Position).LengthSquared() > 6f || !target.IsAwake) return;
                cm.Facing = Vector2.Normalize(target.Position - cm.Position + new Vector2(0.0001f, 0f));
                cm.TalkingTo = target;
                cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 1.0f * dt);
                target.Needs.Social = MathF.Min(1f, target.Needs.Social + 0.8f * dt);
                if (!opened) { opened = true; world.Relations.Open(cm, target, topic); }
            },
            // 가 보니 이미 자리를 떴으면 오래 서 있지 않는다 (다음에 다시)
            DoneWhen = (cm, world) => target.Dead || target.Down || !target.IsAwake || target.Job?.Urgent == true || !opened && (target.Position - cm.Position).LengthSquared() > 36f,
        });
        toils.Add(new DoToil((cm, world) =>
        {
            cm.TalkingTo = null;
            if (target.Dead || (target.Position - cm.Position).LengthSquared() > 9f) return false;
            world.Relations.Close(cm, target, topic);
            return true;
        }));
        return new Job(this, "말 걸기", toils)
        {
            LogText = topic switch
            {
                TalkTopic.Worry => $"힘들어 보이는 {Ko.EulReul(target.Name)} 찾아간다",
                TalkTopic.Rumor => $"{target.Name}에게 들은 이야기를 전하러 간다",
                TalkTopic.Comfort => $"다친 {target.Name}에게 간다",
                TalkTopic.Teach => $"새로 온 {target.Name}에게 배의 일을 일러 주러 간다",
                TalkTopic.Reconcile => $"{target.Name}에게 사과하러 간다",
                _ => $"{target.Name}에게 그때 일을 말하러 간다",
            },
            LogKind = LogKind.Life,
            TargetRoom = target.Room,
            OnFinished = (cm, world, status) => { cm.TalkingTo = null; if (cm.Carrying?.Kind == ItemKind.Meal && status != ToilStatus.Succeeded) { /* 들고 가던 음식은 내려놓는 일로 */ } },
        };
    }
}

/// <summary>소문을 듣고 고장 기미를 직접 확인하러 간다.</summary>
public sealed class InspectActivity : Activity
{
    public override string Id => "inspect";
    public override string Label => "확인하러 감";

    private static ShiftNote? Note(CrewMember c, World w) =>
        c.CheckNote < 0 ? null : w.Watch.Notes.FirstOrDefault(n => n.Id == c.CheckNote && n.Open && !n.Machine.Body.Room.Detached);

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var n = Note(c, w);
        if (n == null) { c.CheckNote = -1; return (0f, "—"); }
        if (n.Machine.Body.UseSpots.Count == 0 || !dist.Reachable(n.Machine.Body.UseSpots[0])) return (0f, "닿을 수 없다");
        float s = 0.3f + (OnShift(c, w) ? 0.15f : 0f) + 0.1f * c.Traits.Diligence;
        if (Bedtime(c, w)) s -= 0.3f;
        return (MathF.Max(0f, s), $"{n.Machine.Name}이(가) 이상하다는 소문 — 직접 본다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var n = Note(c, w);
        if (n == null || n.Machine.Body.UseSpots.Count == 0) return null;
        var spot = n.Machine.Body.UseSpots[0];
        if (!dist.Reachable(spot)) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(8), Pose.Working, n.Machine.Body.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            n.Holders[cm.Id] = true;
            n.Trail.Add($"{SimTime.Clock(world.Tick)} {Ko.IGa(cm.Name)} 소문을 듣고 직접 봤다");
            cm.CheckNote = -1;
            world.Relations.Stats.Checks++;
            cm.Say(world, $"{n.Machine.Name}… {n.Observation}. 정말이네.");
            world.Log.Add(world.Tick, LogKind.Life, $"소문을 듣고 {Ko.EulReul(n.Machine.Name)} 직접 봤다 — {n.Observation}", cm.Id);
            return true;
        }));
        return new Job(this, "확인하러 감", toils) { LogText = $"{n.Machine.Name}이(가) 이상하다는 말을 듣고 보러 간다", LogKind = LogKind.Work, TargetRoom = n.Machine.Body.Room };
    }
}

public static partial class WorkPlanners
{
    /// <summary>v14.4 창고에서 물건 하나 꺼내 오기 (다른 활동에서 쓴다).</summary>
    internal static List<Toil>? FetchItem(CrewMember c, World w, DistanceField dist, ItemKind item, int count) => Fetch(c, w, dist, item, count);
}

public sealed partial class RelationSystem
{
    public TalkStats Stats { get; } = new();
    private readonly Dictionary<(int, int, TalkTopic), long> _talked = new();
    private readonly List<(int victim, int witness, long tick, string truth)> _pending = new();
    private readonly HashSet<int> _wasDown = new();
    /// <summary>시험용: 쓰러진 사람 곁에 있던 사람들 (아직 깨어나지 않았다).</summary>
    internal string PendingFor(CrewMember v) => string.Join(", ", _pending.Where(p => p.victim == v.Id).Select(p => $"{_w.Crew.FirstOrDefault(c => c.Id == p.witness)?.Name}({p.truth})"));

    private static readonly string[] Foods = { "김치찌개", "라면", "초콜릿", "커피", "갓 구운 빵", "과일", "볶음밥", "수프", "떡", "만두", "카레", "팬케이크" };

    /// <summary>좋아하는 음식 (사람마다 정해져 있다).</summary>
    public string Favorite(CrewMember c) => Foods[(int)((uint)(c.Id * 2654435761u + (uint)_w.Seed * 40503u) % (uint)Foods.Length)];

    private readonly Dictionary<(int, int, TalkTopic), long> _tried = new();
    private readonly Dictionary<int, long> _comforted = new();
    private int ComfortsToday(CrewMember o) => All.Count(m => m.Who == o.Id && m.Reason == RelationReason.Comforted && _w.Tick - m.Tick < SimTime.TicksPerDay);
    private bool Recently(CrewMember a, CrewMember b, TalkTopic t, int hours) =>
        _talked.TryGetValue((a.Id, b.Id, t), out var at) && _w.Tick - at < SimTime.Hours(hours)
        || _tried.TryGetValue((a.Id, b.Id, t), out var tr) && _w.Tick - tr < SimTime.Hours(2); // 말을 꺼냈다가 끊겼으면 두 시간 뒤에 다시

    private static bool Reachable(CrewMember o) => !o.Dead && !o.Down && o.IsAwake && o.Job?.Urgent != true && !o.Outside && o.Room != null;

    /// <summary>지금 누구에게 무슨 말을 할까 (가장 급한 것).</summary>
    public (CrewMember target, TalkTopic topic, float urgency, string why)? PickTalk(CrewMember c, DistanceField dist)
    {
        var w = _w;
        (CrewMember, TalkTopic, float, string)? best = null;
        void Offer(CrewMember o, TalkTopic t, float u, string why)
        {
            if (best == null || u > best.Value.Item3) best = (o, t, u, why);
        }
        foreach (var o in w.Crew)
        {
            if (o == c || !Reachable(o) || !dist.Reachable(o.Cell)) continue;
            float aff = c.AffinityTo(o);
            // 진실: 그 사람이 나를 "두고 갔다"고 기억한다 — 그때 일을 말한다
            if (All.FirstOrDefault(m => m.Who == o.Id && m.About == c.Id && m.Reason == RelationReason.AbandonedMe && !m.Revealed) is RelationMemory ab && !Recently(c, o, TalkTopic.Truth, 24))
                Offer(o, TalkTopic.Truth, 0.75f, "그때 일을 오해하고 있다");
            // 화해: 사흘 안에 다퉜고 사이가 나쁘다
            if (aff < 0f && c.Quarrel > 0 && w.Tick - c.Quarrel < SimTime.TicksPerDay * 3 && o.Quarrel > 0 && w.Tick - o.Quarrel < SimTime.TicksPerDay * 3
                && (Life.Has(c, Habit.Patient) || Life.Has(c, Habit.Generous) || Life.Has(c, Habit.Cheerful) || c.Value == CrewValue.People || c.Traits.Calm > 0.6f) && !Recently(c, o, TalkTopic.Reconcile, 20))
                Offer(o, TalkTopic.Reconcile, 0.5f - aff * 0.3f, "다툰 일을 풀고 싶다");
            // 위로: 가까운 사람이 다쳤거나 앓는다 — 치료를 받은 뒤에 (치료가 먼저: 아직이면 의무관을 기다린다)
            // 방금 다른 사람이 다녀갔으면 아주 가까운 사람만 (한꺼번에 몰려가지 않는다)
            if (aff > 0.15f && (o.Vitals.Injury > 0.12f || o.Fx.Worst > 0.35f || o.CareBed != null) && !Recently(c, o, TalkTopic.Comfort, 16)
                && w.Tick - o.Vitals.TreatedTick < SimTime.Hours(12)
                && (!_comforted.TryGetValue(o.Id, out var lastC) || w.Tick - lastC > SimTime.Hours(6) || aff > 0.5f && ComfortsToday(o) < 3))
                Offer(o, TalkTopic.Comfort, 0.45f + aff * 0.3f, o.Vitals.Injury > 0.12f ? "다쳤다" : "앓고 있다");
            // 걱정: 힘들어 보인다
            if (aff > -0.1f && (o.Needs.Stress > 0.6f || o.GriefUntil > w.Tick) && c.Needs.Stress < 0.5f && !Recently(c, o, TalkTopic.Worry, 18))
                Offer(o, TalkTopic.Worry, 0.3f + 0.4f * (o.Needs.Stress - 0.5f) + aff * 0.2f, o.GriefUntil > w.Tick ? "슬픔에 잠겨 있다" : "힘들어 보인다");
            // 신입: 새로 탄 사람·커 가는 아이에게 지난 사고와 바뀐 수칙을
            if (IsNewcomer(o) && !IsNewcomer(c) && !c.IsChild && LessonFor(c, o) != null && !Recently(c, o, TalkTopic.Teach, 30))
                Offer(o, TalkTopic.Teach, 0.35f + (w.Society.IsVeteran(c) ? 0.1f : 0f), "새로 왔다 — 배의 일을 모른다");
            // 소문: 내가 아는 고장 기미를 그 사람은 모른다 (그 일을 할 줄 안다)
            if (!c.IsChild && w.Watch.OpenNotes.FirstOrDefault(n => n.Holders.ContainsKey(c.Id) && !n.Holders.ContainsKey(o.Id) && o.RawSkill(n.Machine.Spec.Skill) >= 0.3f) is ShiftNote note
                && !Recently(c, o, TalkTopic.Rumor, 12))
                Offer(o, TalkTopic.Rumor, 0.28f, $"{note.Machine.Name} 이야기");
        }
        return best;
    }

    /// <summary>시험용: 왜 그 사람에게 말을 걸지 않는가.</summary>
    public string Explain(CrewMember c, CrewMember o, DistanceField dist) =>
        $"닿음 {Reachable(o)}/{dist.Reachable(o.Cell)} 신입 {IsNewcomer(o)}/{IsNewcomer(c)} 교훈 {LessonFor(c, o)?.Text ?? "없음"} 최근 {Recently(c, o, TalkTopic.Teach, 30)}";

    public bool IsNewcomer(CrewMember c) => c.IsChild && c.Age >= 10f || _w.Society.Recent(c);

    /// <summary>{teacher}가 {o}에게 일러 줄 지난 일 (교훈 · 큰 사고) — 그 사람이 아직 모르는 것.</summary>
    private HistoryEvent? LessonFor(CrewMember teacher, CrewMember o) =>
        _w.History.Events.Where(e => e.Kind == HistoryKind.Lesson && !o.Lessons.Contains(LessonKey(e))).OrderBy(e => e.Tick).FirstOrDefault()
        ?? _w.History.Events.Where(e => e.Kind == HistoryKind.Incident && !o.Lessons.Contains(LessonKey(e)) && e.Tick < _w.Tick - SimTime.Hours(6)).OrderBy(e => e.Tick).FirstOrDefault();

    public static string LessonKey(HistoryEvent e) => $"{e.Tick}:{e.Kind}";

    /// <summary>말을 꺼낸다 (말풍선).</summary>
    internal void Open(CrewMember c, CrewMember o, TalkTopic t)
    {
        var w = _w;
        _tried[(c.Id, o.Id, t)] = w.Tick;
        // 누가 말을 걸어오면 (급한 일이 아니면) 잠깐 멈춰 서서 듣는다
        if (o.Job?.Urgent != true && (o.Job?.Order == null || o.Job.Order.Urgency < 0.7f) && o.Job?.Activity is not RecoverActivity)
        {
            o.HoldUntil = w.Tick + SimTime.Minutes(20);
            o.HoldWhy = $"{Ko.IGa(c.Name)} 말을 걸어왔다";
            o.NextThinkTick = w.Tick;
        }
        switch (t)
        {
            case TalkTopic.Worry: c.Say(w, Persona.Say(c, "요즘 힘들어 보여. 무슨 일 있어?")); break;
            case TalkTopic.Comfort: c.Say(w, Persona.Say(c, c.Carrying?.Kind == ItemKind.Meal ? $"{Favorite(o)} 좀 가져왔어" : "좀 어때?")); break;
            case TalkTopic.Reconcile: c.Say(w, Persona.Say(c, "저번엔 내가 심했어. 미안해")); break;
            case TalkTopic.Truth: c.Say(w, Persona.Say(c, "그때 말이야 — 너를 두고 간 게 아니었어")); break;
            case TalkTopic.Teach:
                if (LessonFor(c, o) is HistoryEvent e) c.Say(w, Persona.Say(c, $"{SimTime.Day(e.Tick)}일째에 이런 일이 있었어 — {Short(e.Text)}"));
                break;
            case TalkTopic.Rumor:
                if (w.Watch.OpenNotes.FirstOrDefault(n => n.Holders.ContainsKey(c.Id) && !n.Holders.ContainsKey(o.Id)) is ShiftNote n)
                {
                    bool saw = n.AuthorId == c.Id || n.Holders[c.Id];
                    c.Say(w, Persona.Say(c, saw ? $"내가 봤는데, {n.Machine.Name}에서 {n.Observation}" : $"{n.Machine.Name}이(가) 좀 이상하다던데… {n.Observation}"));
                }
                break;
        }
    }

    private static string Short(string text) => text.Length > 46 ? text[..44] + "…" : text;

    /// <summary>말을 마친다: 뒷일.</summary>
    internal void Close(CrewMember c, CrewMember o, TalkTopic t)
    {
        var w = _w;
        _talked[(c.Id, o.Id, t)] = w.Tick;
        if (o.HoldWhy?.Contains("말을 걸어왔다") == true) o.HoldUntil = -1;
        switch (t)
        {
            case TalkTopic.Worry:
            {
                Stats.Worries++;
                string reason = Trouble(o);
                o.Say(w, Persona.Say(o, reason));
                o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.08f);
                o.ChangeAffinity(c, 0.05f); c.ChangeAffinity(o, 0.03f);
                // 다음 근무를 대신 선다 (근무 중이거나 두 시간 안에 근무면)
                bool soon = Activity_OnShiftSoon(o, w);
                if (soon && !c.IsChild && c.Fx.Worst < 0.3f && c.Needs.Rest > 0.4f && o.ExcusedUntil < w.Tick)
                {
                    Stats.Covers++;
                    o.ExcusedUntil = w.Tick + SimTime.Hours(6);
                    c.CoveringUntil = w.Tick + SimTime.Hours(6);
                    c.Say(w, Persona.Say(c, "오늘 근무는 내가 설게. 좀 쉬어"));
                    Remember(o, c, RelationReason.DidMyShift, "힘들 때 근무를 대신 서 줬다");
                    w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} 지친 {o.Name} 대신 근무를 섰다", c.Room, new[] { c, o }, log: true);
                    Life.Diary(w, o, Persona.Say(o, $"{Ko.IGa(c.Name)} 내 근무를 대신 서 줬다"));
                }
                break;
            }
            case TalkTopic.Comfort:
            {
                Stats.Comforts++;
                _comforted[o.Id] = w.Tick;
                bool food = c.Carrying?.Kind == ItemKind.Meal;
                if (food) { c.Carrying = null; o.Needs.Food = MathF.Min(1f, o.Needs.Food + 0.45f); }
                o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - (food ? 0.12f : 0.07f));
                o.ChangeAffinity(c, food ? 0.1f : 0.06f);
                o.Say(w, Persona.Say(o, food ? $"{Favorite(o)}… 고마워" : "와 줘서 고마워"));
                Remember(o, c, RelationReason.Comforted, food ? $"다쳤을 때 좋아하는 {Ko.EulReul(Favorite(o))} 가져다줬다" : "다쳤을 때 곁에 와 줬다");
                Life.Diary(w, o, Persona.Say(o, food ? $"{Ko.IGa(c.Name)} {Ko.EulReul(Favorite(o))} 가져왔다" : $"{Ko.IGa(c.Name)} 와 줬다"));
                break;
            }
            case TalkTopic.Teach:
            {
                if (LessonFor(c, o) is not HistoryEvent e) break;
                Stats.Teachings++;
                o.Lessons.Add(LessonKey(e));
                o.Say(w, Persona.Say(o, "그래서 그렇게 하는 거구나"));
                o.ChangeAffinity(c, 0.05f);
                Remember(o, c, RelationReason.TaughtMe, $"지난 일을 일러 줬다 — {Short(e.Text)}");
                Life.Diary(w, o, $"{Ko.IGa(c.Name)} {SimTime.Day(e.Tick)}일째 일을 이야기해 줬다.");
                break;
            }
            case TalkTopic.Reconcile:
            {
                Stats.Apologies++;
                float accept = 0.55f + 0.25f * MathF.Max(0f, o.AffinityTo(c) + 0.5f) + (Life.Has(o, Habit.Patient) ? 0.15f : 0f) - (Life.Has(o, Habit.ShortTempered) ? 0.25f : 0f) - 0.3f * o.Mind.Anger;
                if (_rng.Chance(Math.Clamp(accept, 0.1f, 0.95f)))
                {
                    Stats.Reconciled++;
                    o.ChangeAffinity(c, 0.18f); c.ChangeAffinity(o, 0.15f);
                    o.Mind.Anger = MathF.Max(0f, o.Mind.Anger - 0.2f);
                    o.Say(w, Persona.Say(o, "나도 미안해"));
                    Remember(o, c, RelationReason.Apologized, "먼저 사과했다");
                    w.History.Add(w, HistoryKind.Bond, $"{Ko.WaGwa(c.Name)} {o.Name}이(가) 다툰 일을 풀었다", c.Room, new[] { c, o }, log: true);
                    _reconciled.Add((Math.Min(c.Id, o.Id), Math.Max(c.Id, o.Id)));
                }
                else
                {
                    Stats.Refused++;
                    o.Say(w, Persona.Say(o, "아직은 아니야"));
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
                }
                break;
            }
            case TalkTopic.Truth:
            {
                var m = All.FirstOrDefault(x => x.Who == o.Id && x.About == c.Id && x.Reason == RelationReason.AbandonedMe && !x.Revealed);
                if (m == null) break;
                Stats.Truths++;
                Reveal(m, c, o, "직접 들었다");
                break;
            }
            case TalkTopic.Rumor:
            {
                var n = w.Watch.OpenNotes.FirstOrDefault(x => x.Holders.ContainsKey(c.Id) && !x.Holders.ContainsKey(o.Id));
                if (n == null) break;
                PassRumor(c, o, n, "찾아가서");
                break;
            }
        }
    }

    /// <summary>고장 기미를 말로 전한다: 말한 사람을 믿으면 판단까지 받아들이고, 못 믿으면 직접 본다.</summary>
    private void PassRumor(CrewMember c, CrewMember o, ShiftNote n, string how)
    {
        var w = _w;
        Stats.Rumors++;
        float trust = Trust(o, c) + 0.5f * (o.AffinityTo(c));
        bool judged = n.Holders[c.Id] && trust >= -0.05f;
        n.Holders[o.Id] = judged;
        n.Trail.Add($"{SimTime.Clock(w.Tick)} {Ko.IGa(c.Name)} {o.Name}에게 {how} 말로 ({(judged ? "판단까지" : "관측만")})");
        if (o.RawSkill(n.Machine.Spec.Skill) >= 0.3f && (!judged || o.Traits.Diligence > 0.5f)) o.CheckNote = n.Id;
        if (trust < -0.05f) { Stats.Distrusted++; o.Say(w, Persona.Say(o, $"{c.Name} 말은… 내가 직접 볼게")); }
        else o.Say(w, Persona.Say(o, "그래? 한번 봐야겠네"));
    }

    /// <summary>v14.4 수다 끝에 나온 이야기: 한쪽만 아는 고장 기미가 있으면 (그럴 법하면) 흘러간다.</summary>
    public void Gossip(CrewMember a, CrewMember b)
    {
        var w = _w;
        _rng ??= new Rng(unchecked(w.Seed * 4391 + 61));
        if (a.IsChild && b.IsChild) return;
        foreach (var (c, o) in new[] { (a, b), (b, a) })
        {
            if (c.IsChild) continue;
            var n = w.Watch.OpenNotes.FirstOrDefault(x => x.Holders.ContainsKey(c.Id) && !x.Holders.ContainsKey(o.Id));
            if (n == null || !_rng.Chance(0.5f + 0.3f * c.Traits.Sociability)) continue;
            bool saw = n.AuthorId == c.Id || n.Holders[c.Id];
            c.Say(w, Persona.Say(c, saw ? $"그러고 보니 {n.Machine.Name}에서 {n.Observation}" : $"{n.Machine.Name}이(가) 좀 이상하다던데… {n.Observation}"));
            PassRumor(c, o, n, "수다 끝에");
            return;
        }
    }

    private static bool Activity_OnShiftSoon(CrewMember o, World w) =>
        SimTime.InWindow(SimTime.HourOfDay(w.Tick), o.Schedule.WorkStart, o.Schedule.WorkLength)
        || SimTime.InWindow(SimTime.HourOfDay(w.Tick) + 2f, o.Schedule.WorkStart, o.Schedule.WorkLength);

    /// <summary>왜 힘든지 (본인의 말).</summary>
    private string Trouble(CrewMember o)
    {
        var w = _w;
        if (o.GriefUntil > w.Tick && w.Life.Memorial.Count > 0) return $"{w.Life.Memorial[^1].name}이(가) 자꾸 생각나";
        if (o.Fx.Worst > 0.3f) return $"몸이 안 좋아 — {w.Ailments.Line(o)}";
        if (o.Quarrel > 0 && w.Tick - o.Quarrel < SimTime.TicksPerDay * 2) return "요즘 다툰 일이 마음에 걸려";
        if (o.Needs.Rest < 0.3f) return "잠을 통 못 잤어";
        if (o.Needs.Food < 0.3f) return "배가 고파서 그래";
        if (o.Memory.Trauma > 0.4f) return $"{o.Memory.TraumaCause ?? "그때 일"}이(가) 자꾸 떠올라";
        if (Life.Has(o, Habit.Homesick)) return "집 생각이 나서";
        return "그냥… 지쳤어";
    }

    // ───────────────────────────── 엇갈린 기억 ─────────────────────────────

    private readonly HashSet<(int, int)> _reconciled = new();
    private Rng _rng = null!;

    /// <summary>시스템 틱: 쓰러진 사람 곁에서 떠난 사람 → (깨어나면) "나를 두고 갔다" · 화해한 둘이 함께 일하면 더 가까워진다.</summary>
    public void Update(float dt)
    {
        var w = _w;
        _rng ??= new Rng(unchecked(w.Seed * 4391 + 61));
        foreach (var v in w.Crew)
        {
            if (v.Dead) { _wasDown.Remove(v.Id); continue; }
            bool down = v.Down;
            if (down && _wasDown.Add(v.Id))
            {
                // 막 쓰러졌다: 곁에 있던 사람들 (그때 무엇을 하러 가고 있었나 — 그게 진실이다)
                foreach (var x in w.Crew)
                {
                    if (x == v || x.Dead || x.Down || !x.IsAwake || x.Room != v.Room || (x.Position - v.Position).LengthSquared() > 36f) continue;
                    string truth = x.Job?.WillDonSuit == true ? "우주복을 가지러 갔다"
                        : x.Job?.Order?.Kind == WorkKind.Extinguish || x.Carrying?.Kind == ItemKind.Extinguisher ? "소화기를 가지러 갔다"
                        : x.Job?.Activity is EvacuateActivity or PanicActivity ? "겁이 나서 빠져나갔다"
                        : x.Job?.Order is WorkOrder wo ? $"{wo.Title} 하러 갔다" : "사람을 부르러 갔다";
                    _pending.Add((v.Id, x.Id, w.Tick, truth));
                }
            }
            else if (!down && _wasDown.Remove(v.Id))
            {
                // 깨어났다: 구해 주지 않고 그 방을 떠난 사람을 "나를 두고 갔다"로 기억한다
                foreach (var p in _pending.Where(p => p.victim == v.Id).ToList())
                {
                    _pending.Remove(p);
                    var x = w.Crew.FirstOrDefault(c => c.Id == p.witness);
                    if (x == null || x.Dead || All.Any(m => m.Who == v.Id && m.About == x.Id && m.Reason == RelationReason.SavedMe && m.Tick >= p.tick)) continue;
                    Stats.Abandons++;
                    var m = Remember(v, x, RelationReason.AbandonedMe, "내가 쓰러졌을 때 두고 갔다");
                    m.Truth = p.truth;
                    m.Revealed = false; // 또 두고 갔다 — 이번 일의 진실은 아직 모른다
                    v.ChangeAffinity(x, -0.15f);
                    Life.Diary(w, v, Persona.Say(v, $"쓰러졌을 때 {Ko.IGa(x.Name)} 나를 두고 갔다"));
                    MarkLog.Add(x.Memory.Marks, w.Tick, $"쓰러진 {Ko.EulReul(v.Name)} 두고 나왔다 ({p.truth})");
                    x.Needs.Stress = MathF.Min(1f, x.Needs.Stress + 0.05f);
                }
            }
        }
        _pending.RemoveAll(p => w.Tick - p.tick > SimTime.TicksPerDay * 2);
        // 화해한 둘이 같은 방에서 일하면 사이가 회복된다
        if (w.Tick % SimTime.Minutes(10) < World.SystemInterval)
            foreach (var (a, b) in _reconciled)
            {
                var ca = w.Crew.FirstOrDefault(c => c.Id == a);
                var cb = w.Crew.FirstOrDefault(c => c.Id == b);
                if (ca == null || cb == null || ca.Dead || cb.Dead || ca.Room != cb.Room || ca.Pose != Pose.Working || cb.Pose != Pose.Working) continue;
                ca.ChangeAffinity(cb, 0.02f); cb.ChangeAffinity(ca, 0.02f);
            }
    }

    /// <summary>진실을 알게 된다 (직접 듣거나 · 회의에서 · 다시 도움을 받아서).</summary>
    public void Reveal(RelationMemory m, CrewMember accused, CrewMember who, string how)
    {
        var w = _w;
        m.Revealed = true;
        string truth = m.Truth ?? "어쩔 수 없었다";
        bool meant = !truth.Contains("겁이 나서");
        if (meant)
        {
            m.Weight = 0.05f;
            m.Text = $"두고 간 줄 알았는데 {truth} 거였다";
            who.ChangeAffinity(accused, 0.15f);
            who.Say(w, Persona.Say(who, "그랬구나… 몰랐어"));
        }
        else
        {
            m.Weight = -0.3f;
            m.Text = "정말 겁이 나서 두고 갔다 — 그래도 말해 줬다";
            who.ChangeAffinity(accused, 0.04f);
            who.Say(w, Persona.Say(who, "…그래도 말해 줘서 고마워"));
        }
        w.Log.Add(w.Tick, LogKind.Life, $"{accused.Name}이(가) 그때 {truth}는 걸 알았다 ({how})", who.Id);
        Life.Diary(w, who, Persona.Say(who, $"{Ko.IGa(accused.Name)} 그때 {truth}고 한다"));
    }

    /// <summary>다시 도움을 받았다: 오해가 풀린다.</summary>
    public void Rescued(CrewMember victim, CrewMember rescuer)
    {
        foreach (var m in All.Where(m => m.Who == victim.Id && m.About == rescuer.Id && m.Reason == RelationReason.AbandonedMe && m.Weight < 0f))
        {
            m.Weight += 0.4f;
            m.Text = "그때는 두고 갔지만 이번엔 구해 줬다";
            m.Revealed = true;
        }
    }

    /// <summary>숨긴 실수가 드러났을 때: 가까운 사람이 감싸 주기도 하고, 규칙파가 탓하기도 한다.</summary>
    public bool OnMistakeRevealed(CrewMember c)
    {
        var w = _w;
        _rng ??= new Rng(unchecked(w.Seed * 4391 + 61));
        var friend = w.Crew.Where(o => !o.Dead && o != c && !o.IsChild && o.AffinityTo(c) > 0.4f && (o.Value == CrewValue.People || Life.Has(o, Habit.Generous)))
            .OrderByDescending(o => o.AffinityTo(c)).FirstOrDefault();
        var blamer = w.Crew.Where(o => !o.Dead && o != c && !o.IsChild && o.Value == CrewValue.Rules && o.AffinityTo(c) < 0.2f).OrderBy(o => o.AffinityTo(c)).FirstOrDefault();
        if (blamer != null)
        {
            Stats.Blamed++;
            Remember(c, blamer, RelationReason.BlamedMe, "실수를 대놓고 탓했다");
        }
        if (friend != null && _rng.Chance(0.5f))
        {
            Stats.Covered++;
            Remember(c, friend, RelationReason.CoveredMyMistake, "모두 앞에서 내 실수를 감싸 줬다");
            friend.Say(w, Persona.Say(friend, $"{c.Name}만의 잘못은 아니야. 피곤한 교대였잖아"));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(friend.Name)} {c.Name}의 실수를 감쌌다", friend.Id);
            return true; // 감싸 주면 벌이 가벼워진다
        }
        return false;
    }
}
