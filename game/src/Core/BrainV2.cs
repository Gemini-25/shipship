using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.15 승무원 두뇌 2.0 — 이 게임의 중심.
// Brain.Think(점수 · 이유)는 그대로 두고, 그 위에 믿음(Beliefs) · 목표 층(Goals) · 계획(Plans) · 감정(Emotions) ·
// 사회적 추론(SocialMind) · 배우기(Learning)를 얹는다. Think는 장 · 중기 목표와 감정으로 행동 종류의 점수를 기울이고(Tilt),
// 새 활동(계획대로 · 정전 대처 · 불 확인 · 알리러 감)이 믿음으로 판단한다. 성능: 둘러보기는 4분의 1씩 나눠 · 목표는 30분 · 고치기 계획은 10분마다.

/// <summary>행동 종류 (목표 · 감정이 점수를 기울이는 단위).</summary>
public enum ActCat : byte { None, Survival, Work, Duty, Social, Hobby, Rest, Care, Explore, Food, Plan }

public sealed class BrainSystem
{
    /// <summary>두뇌 2.0을 끈다 (성능 비교 시험용 — 끄면 예전 두뇌와 같다).</summary>
    public static bool Enabled = true;

    private readonly World _w;
    public BeliefSystem Beliefs { get; }
    public EmotionSystem Emotions { get; }
    public GoalSystem Goals { get; }
    public PlanSystem Plans { get; }
    public SocialMind Social { get; }
    public LearningSystem Learning { get; }
    public int Tilts, Diaries;

    public BrainSystem(World w)
    {
        _w = w;
        Beliefs = new BeliefSystem(w);
        Emotions = new EmotionSystem(w);
        Goals = new GoalSystem(w);
        Plans = new PlanSystem(w);
        Social = new SocialMind(w);
        Learning = new LearningSystem(w);
    }

    public void Update(float dt)
    {
        if (!Enabled) return;
        long t = Prof.Now;
        Beliefs.Update(dt);
        t = Prof.Lap("brain2.beliefs", t);
        Emotions.Update(dt);
        Goals.Update(dt);
        Plans.Update(dt);
        Prof.Lap("brain2.rest", t);
        DiaryTime();
    }

    // ─────────────────────────── Think 훅: 점수 기울이기 ───────────────────────────

    private static readonly Dictionary<Activity, ActCat> _cats = new(ReferenceEqualityComparer.Instance);

    public static ActCat Cat(Activity a)
    {
        if (_cats.TryGetValue(a, out var k)) return k;
        k = a switch
        {
            EvacuateActivity or ShelterActivity or HeedBroadcastActivity or TakeCoverActivity or CosmicEvacuateActivity or CosmicShelterActivity or CosmicBraceActivity or EvaSurviveActivity => ActCat.Survival,
            ChoresActivity or BodyUpkeepActivity or PartTestActivity or ExtinguisherCheckActivity or FlushActivity or PortableActivity or BlastResponseActivity => ActCat.Work,
            DutyActivity or PatrolActivity or MeetingActivity or ShipRoundsActivity or CheckRoomActivity => ActCat.Duty,
            ChatActivity or ReachOutActivity or SharedMealActivity => ActCat.Social,
            HobbyActivity or CosmicLookActivity => ActCat.Hobby,
            RelaxActivity or WanderActivity => ActCat.Rest,
            VisitActivity or MendActivity or MemorialVisitActivity or EvaRescueActivity or OpenDoorActivity => ActCat.Care,
            ExpeditionActivity or InspectActivity or FollowSmellActivity => ActCat.Explore,
            EatActivity or SavedPlateActivity => ActCat.Food,
            _ => ActCat.None,
        };
        _cats[a] = k;
        return k;
    }

    /// <summary>Brain.Think에서 행동마다: 장 · 중기 목표와 감정이 점수를 기울인다 (크게 기울면 이유에 덧붙인다).</summary>
    public void Tilt(CrewMember c, Activity a, ref float score, ref string reason)
    {
        if (!Enabled || score <= 0f) return;
        var cat = Cat(a);
        if (cat == ActCat.None) return;
        float g = Goals.Tilt(c, cat);
        float e = Emotions.Tilt(c, cat);
        float m = g * e;
        if (MathF.Abs(m - 1f) < 0.01f) return;
        score *= m;
        Tilts++;
        if (MathF.Abs(m - 1f) < 0.1f) return;
        if (MathF.Abs(e - 1f) >= MathF.Abs(g - 1f))
        {
            var d = Emotions.Dominant(c, 0.15f);
            reason += d is { } dd ? $" · {EmotionSystem.Name(dd.f)}{(e > 1f ? "에 끌려" : " 때문에 덜")}" : "";
        }
        else if (Goals.Of(c).FirstOrDefault(x => x.Cat == cat && x.Until > _w.Tick) is CrewGoal goal)
            reason += $" · {(goal.Layer == GoalLayer.Long ? "꿈" : "목표")}: {goal.Text}";
    }

    // ─────────────────────────── ⑧ 말: 일기 ───────────────────────────

    private readonly Dictionary<int, int> _diaryDay = new();

    /// <summary>밤 10시 무렵 하루를 적는다 — 가장 컸던 감정과 까닭 · 오늘 고친 믿음 · 마음에 둔 목표.</summary>
    private void DiaryTime()
    {
        var w = _w;
        if (w.Tick % (World.SystemInterval * 20) != 0 || SimTime.HourOfDay(w.Tick) < 21.5f) return;
        int day = w.Day;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.IsChild || _diaryDay.GetValueOrDefault(c.Id, -1) == day) continue;
            _diaryDay[c.Id] = day;
            if (Line(c) is string line) { Life.Diary(w, c, Persona.Say(c, line)); Diaries++; }
            Emotions.NewDay(c);
        }
    }

    /// <summary>오늘의 일기 한 줄 (감정 · 믿음 · 목표). 쓸 게 없으면 null.</summary>
    public string? Line(CrewMember c)
    {
        var w = _w;
        var e = Emotions.Of(c);
        var parts = new List<string>();
        int top = -1;
        for (int i = 0; i < 6; i++) if (e.Peak[i] >= 0.3f && (top < 0 || e.Peak[i] > e.Peak[top])) top = i;
        if (top >= 0)
        {
            string word = (Feeling)top switch
            {
                Feeling.Anger => "화가 났다", Feeling.Fear => "무서웠다", Feeling.Joy => "좋은 날이었다",
                Feeling.Sadness => "슬펐다", Feeling.Shame => "부끄러웠다", _ => "뿌듯했다",
            };
            parts.Add(e.PeakCause[top] is string why ? $"오늘은 {word} — {why}." : $"오늘은 {word}.");
        }
        var book = Beliefs.Of(c);
        if (book.LastCorrection >= 0 && w.Tick - book.LastCorrection < SimTime.TicksPerDay && book.LastCorrectionText != null)
            parts.Add($"{book.LastCorrectionText}.");
        if (Plans.Past.LastOrDefault(p => p.Owner == c.Id && w.Tick - p.Since < SimTime.TicksPerDay) is CrewPlan pl)
            parts.Add(pl.Failed ? $"{pl.Goal} — 뜻대로 안 됐다 ({pl.Result})." : $"{pl.Goal} — 해냈다.");
        if (parts.Count == 0) return null;
        var goal = Goals.Layer(c, GoalLayer.Mid).FirstOrDefault() ?? Goals.Layer(c, GoalLayer.Long).FirstOrDefault();
        if (goal != null) parts.Add(goal.Layer == GoalLayer.Long ? $"그래도 언젠가 {goal.Text}." : $"요즘 마음: {goal.Text}.");
        return string.Join(" ", parts);
    }

    /// <summary>지문 (결정론 점검 — StateHash 끝에 들어간다).</summary>
    public long Hash() => unchecked(Beliefs.Hash() * 31 + Emotions.Hash() * 17 + Goals.Hash() * 13 + Plans.Hash() * 7 + Learning.Hash() * 5 + Social.Hash());
}
