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
    public static bool Enabled = Environment.GetEnvironmentVariable("SHIPSIM_BRAIN2") != "0";

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
        DiaryWords.Plain("오늘은 좋은 날이었다 — 정비 — 해냈다."); // v19 60프레임: 일기 말다듬기를 미리 준비 (첫 일기 날 밤 한 틱이 30ms 멈췄다) — 결과는 버린다
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
            ExpeditionActivity or InspectActivity => ActCat.Explore,
            EatActivity or SavedPlateActivity or FollowSmellActivity => ActCat.Food, // v16 통합: 빵 냄새를 따라가는 건 먹고 싶은 마음 — "배고프다" 목표가 끈다 (꿈 · 두려움이 탐험처럼 끌거나 막지 않는다)
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
        // v19 급한 일(중상자 치료 · 위기 대응)은 슬픔 · 두려움이 크게 막지 못한다 — 동료를 잃고 슬픈 대신 치료자가 중상자 치료를 17시간 미루고 쉬었다 · 위중하면 조금도 깎지 않는다
        if (cat is ActCat.Work or ActCat.Care && m < 1f) m = score >= 1.3f ? 1f : score >= 0.8f ? MathF.Max(m, 0.9f) : m;
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
        if (SimTime.HourOfDay(w.Tick) < 21.5f) return;
        int day = w.Day, left = 4; // v19 60프레임: 한 번에 넷씩 (60인 배는 일기를 한 틱에 몰아 써 40ms 멈췄다)
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.IsChild || _diaryDay.GetValueOrDefault(c.Id, -1) == day) continue;
            if (left-- <= 0) break;
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
        return DiaryWords.Plain(string.Join(" ", parts)); // v16.24 메모 같은 말 → 일기 문장
    }

    /// <summary>지문 (결정론 점검 — StateHash 끝에 들어간다).</summary>
    public long Hash() => unchecked(Beliefs.Hash() * 31 + Emotions.Hash() * 17 + Goals.Hash() * 13 + Plans.Hash() * 7 + Learning.Hash() * 5 + Social.Hash());
}
