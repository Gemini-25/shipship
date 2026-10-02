using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>v16.15 승무원 카드의 믿음 한 줄 (출처 · 확신 · 몇 시간 전 · 세계와 어긋나나).</summary>
public readonly record struct BrainBelief(string Text, BeliefSource Src, float Conf, float AgeHours, bool Wrong, Topic Topic);
public readonly record struct BrainGoal(GoalLayer Layer, string Text, string Why);
public readonly record struct BrainStep(string Text, StepState State, string? Note, bool Current);

/// <summary>v16.15 승무원 카드 "두뇌" 칸: 믿음 · 목표 층 · 계획 · 감정 · 배운 것 · 정전 때 고른 길.</summary>
public sealed class BrainCard
{
    public List<BrainBelief> Beliefs { get; } = new();
    public List<BrainGoal> Goals { get; } = new();
    public List<BrainStep> Plan { get; } = new();
    public string? PlanGoal;
    public string? PlanWhy;
    public List<(Feeling f, float v, string? why)> Emotions { get; } = new();
    public List<string> Lessons { get; } = new();
    public string? Stance;
    public string? Correction;
}

public static partial class CrewWhy
{
    /// <summary>
    /// v16.15 두뇌 2.0을 카드로 — 읽기 전용이다 (상태를 바꾸지 않고 난수도 쓰지 않는다).
    /// 믿음은 어긋난 것 · 위험 · 최근 것부터 (어긋남은 화면만 안다 — 그 사람은 모른다).
    /// </summary>
    public static BrainCard Brain2(CrewMember c, World w, int beliefs = 5)
    {
        var card = new BrainCard();
        if (c.Dead) return card;
        var b2 = w.Brain2;
        var bel = b2.Beliefs;
        long now = w.Tick;
        if (bel.Books.FirstOrDefault(x => x.crew == c.Id).book is BeliefBook book)
        {
            var rows = new List<(BrainBelief row, int rank)>();
            foreach (var b in book.All)
            {
                float eff = BeliefSystem.Eff(b, now);
                if (eff < 0.15f) continue;
                bool wrong = bel.Wrong(b);
                bool hazard = b.Value == 1 && b.Topic is Topic.Fire or Topic.Breach or Topic.Down or Topic.Air or Topic.Water or Topic.Warn or Topic.Shelter or Topic.Cosmic or Topic.Omen or Topic.Dark;
                bool person = b.Topic == Topic.Person && (c.AffinityTo(bel.CrewById(b.Id) ?? c) >= 0.3f || b.Value < 0);
                bool fresh = b.Changes > 0 && now - b.Tick < SimTime.Hours(3);
                if (!(wrong || hazard || person || fresh || b.Topic is Topic.Item or Topic.Outage)) continue;
                int rank = wrong ? 0 : hazard ? 1 : fresh ? 2 : 3;
                rows.Add((new BrainBelief(bel.Describe(b), b.Src, eff, (now - b.Tick) / (float)SimTime.TicksPerHour, wrong, b.Topic), rank));
            }
            foreach (var (row, _) in rows.OrderBy(r => r.rank).ThenByDescending(r => r.row.Conf).ThenBy(r => r.row.AgeHours).ThenBy(r => r.row.Text, StringComparer.Ordinal).Take(beliefs))
                card.Beliefs.Add(row);
            if (book.LastCorrectionText != null && now - book.LastCorrection < SimTime.TicksPerDay) card.Correction = book.LastCorrectionText;
        }
        foreach (var g in b2.Goals.Peek(c).Where(g => g.Layer == GoalLayer.Long && g.Until > now)) card.Goals.Add(new(GoalLayer.Long, g.Text, g.Why));
        foreach (var g in b2.Goals.Peek(c).Where(g => g.Layer == GoalLayer.Mid && g.Until > now)) card.Goals.Add(new(GoalLayer.Mid, g.Text, g.Why));
        foreach (var g in b2.Goals.Short(c)) card.Goals.Add(new(GoalLayer.Short, g.Text, g.Why));
        var plan = b2.Plans.Current(c) ?? b2.Plans.Past.LastOrDefault(p => p.Owner == c.Id && now - p.Since < SimTime.Hours(2));
        if (plan != null)
        {
            card.PlanGoal = plan.Done ? $"{plan.Goal} ({(plan.Failed ? "멈춤" : "끝")} — {plan.Result})" : plan.Goal;
            card.PlanWhy = plan.Why;
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                var s = plan.Steps[i];
                card.Plan.Add(new BrainStep(s.Text, s.State, s.Note, !plan.Done && i == plan.Index));
            }
        }
        var es = b2.Emotions.Peek(c);
        foreach (var f in EmotionSystem.All)
            card.Emotions.Add((f, b2.Emotions.Get(c, f), es?.Cause[(int)f]));
        card.Lessons.AddRange(b2.Learning.Lines(c, 3));
        if (b2.Plans.Stances.TryGetValue(c.Id, out var st) && now - st.tick < SimTime.Hours(3)) card.Stance = $"{LearningSystem.Name(st.m)} — {st.why}";
        return card;
    }
}
