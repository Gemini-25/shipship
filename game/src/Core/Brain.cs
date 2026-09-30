using System.Collections.Generic;

namespace ShipSim.Core;

/// <summary>
/// 승무원의 판단. 모든 행동 후보에 점수를 매기고 가장 급한 것을 고른다.
/// 평소에는 10분마다 다시 생각하고, 경보나 위험을 알아채면 그 즉시 다시 생각한다(긴급 인터럽트).
/// </summary>
public static class Brain
{
    public static readonly Activity[] Activities =
    {
        new EvacuateActivity(),
        new RecoverActivity(),
        new StowSuitActivity(),
        new RefillSuitActivity(),
        new EatActivity(),
        new SleepActivity(),
        new ChoresActivity(),
        new DutyActivity(),
        new ChatActivity(),
        new RelaxActivity(),
        new WanderActivity(),
    };

    public const float Noise = 0.05f;

    public static readonly int ThinkInterval = SimTime.Minutes(10);

    /// <summary>판단 횟수 (성능 점검용).</summary>
    public static long ThinkCount;

    public static void Think(CrewMember c, World w)
    {
        ThinkCount++;
        var dist = w.Paths.Flood(c.Cell, c.PathProfile);
        var evals = new List<Evaluation>(Activities.Length);
        foreach (var a in Activities)
        {
            var (score, reason) = a.Score(c, w, dist);
            if (score > 0f) score += w.Rng.Range(-Noise, Noise);
            evals.Add(new Evaluation(a, score < 0f ? 0f : score, reason));
        }
        evals.Sort((x, y) => y.Score.CompareTo(x.Score));
        c.LastEvaluations = evals;
        c.LastThinkTick = w.Tick;

        if (c.Job != null)
        {
            float current = 0f;
            foreach (var e in evals)
                if (e.Activity == c.Job.Activity) { current = e.Score; break; }

            var best = evals[0];
            if (best.Activity == c.Job.Activity)
            {
                // 같은 "작업"이라도 훨씬 급한 일이 올라오면 하던 정비를 내려놓고 간다
                if (best.Activity is ChoresActivity chores && c.Job.Order is WorkOrder cur && chores.ShouldSwitch(c, w, dist, cur))
                {
                    var urgent = chores.Plan(c, w, dist);
                    if (urgent != null)
                    {
                        c.EndJob(w, ToilStatus.Interrupted);
                        c.StartJob(urgent, w, best);
                    }
                }
                return;
            }
            // 손에 익은 일을 절반 넘게 했으면 더 버틴다
            float margin = c.Job.InterruptMargin + (c.Job.Current is WorkToil { Progress: > 0.4f } ? 0.2f : 0f);
            // v10.5: 긴 개조·정비 중에도 굶주리면 손을 놓고 먹으러 간다 (급한 일은 예외 — 불 끄던 사람은 버틴다)
            if (!c.Job.Urgent && c.Needs.Hunger > 0.9f && best.Activity is EatActivity) margin = 0f;
            if (best.Score < current + margin) return;

            var next = best.Activity.Plan(c, w, dist);
            if (next == null) return;
            c.EndJob(w, ToilStatus.Interrupted);
            c.StartJob(next, w, best);
            return;
        }

        foreach (var e in evals)
        {
            if (e.Score <= 0f) continue;
            var job = e.Activity.Plan(c, w, dist);
            if (job == null) continue;
            c.StartJob(job, w, e);
            return;
        }
        c.StartJob(IdleJob.Create(), w, null);
    }
}
