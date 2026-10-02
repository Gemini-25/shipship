using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.15 승무원 카드 "두뇌" 칸 (읽기 전용): 감정 여섯(그림 + 막대) · 꿈과 목표 · 계획 단계(✔ ✘ ▶) · 믿음(출처 · 확신 · 몇 시간 전 · 세계와 어긋남) · 고친 믿음 · 배운 것.
public partial class Hud
{
    private float DrawBrainSection(CrewMember c, float x, float right, float y, Func<float, bool> fits)
    {
        var w = _world;
        if (c.Dead || !fits(40)) return y;
        var card = CrewWhy.Brain2(c, w, 3);
        UiKit.Header(this, x, right, y + 10, "두뇌", card.Stance ?? card.PlanGoal ?? "감정 · 목표 · 믿음", "why");
        y += 16;

        // 감정 여섯: 각자 다른 그림 + 막대 (가장 큰 것은 밝게)
        var top = card.Emotions.OrderByDescending(e => e.v).First();
        float cw = (right - x) / 6f;
        for (int i = 0; i < card.Emotions.Count; i++)
        {
            var (f, v, _) = card.Emotions[i];
            float cx = x + i * cw;
            bool lead = f == top.f && v >= 0.2f;
            EmotionGlyphs.Draw(this, f, new Vector2(cx + 8, y + 9), 5.5f, _time, i * 1.3f, lead ? 1f : 0.3f, 0.35f + 0.65f * Mathf.Clamp(v * 2f, 0f, 1f), true);
            Gfx.Bar(this, new Rect2(cx + 17, y + 6, cw - 21, 5), Mathf.Clamp(v, 0f, 1f), EmotionGlyphs.Of(f).WithAlpha(lead ? 1f : 0.7f));
        }
        y += 18;
        if (top.v >= 0.2f && top.why != null && fits(14))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 9), UiKit.Fit($"{EmotionSystem.Name(top.f)} — {top.why}", right - x, Ui.TextTiny), Ui.TextTiny, EmotionGlyphs.Of(top.f));
            y += 13;
        }

        // 꿈 · 목표
        foreach (var g in card.Goals.Where(g => g.Layer != GoalLayer.Short).Take(2))
        {
            if (!fits(14)) break;
            var gc = g.Layer == GoalLayer.Long ? new Color("#c9b6ff") : Palette.Accent;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 10), g.Layer == GoalLayer.Long ? "꿈" : "목표", Ui.TextTiny, gc);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 26, y + 10), UiKit.Fit(g.Why.Length > 0 ? $"{g.Text} — {g.Why}" : g.Text, right - x - 26, Ui.TextTiny), Ui.TextTiny, Palette.TextDim);
            y += 13;
        }

        // 계획: 단계 칩 (✔ 된 · ✘ 막힌 · ▶ 지금 · · 남은)
        if (card.PlanGoal != null && card.Plan.Count > 0 && fits(30))
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 10), "계획", Ui.TextTiny, Palette.Accent);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 26, y + 10), UiKit.Fit(card.PlanGoal, right - x - 26, Ui.TextTiny), Ui.TextTiny, Palette.TextDim);
            y += 14;
            float px = x;
            foreach (var s in card.Plan)
            {
                string mark = s.State switch { StepState.Done => "✔", StepState.Failed => "✘", StepState.Skipped => "–", _ => s.Current ? "▶" : "·" };
                var sc = s.State switch { StepState.Done => Palette.Good, StepState.Failed => Palette.Warning, StepState.Skipped => Palette.TextMuted, _ => s.Current ? Palette.Accent : Palette.TextDim };
                string label = UiKit.Fit($"{mark} {s.Text}", 120, Ui.TextMicro);
                float bw = Gfx.Width(Fonts.Body, label, Ui.TextMicro) + 8;
                if (px + bw > right) { px = x; y += 15; if (!fits(15)) break; }
                Gfx.RoundRect(this, new Rect2(px, y, bw, 13), sc.WithAlpha(0.12f), 4, sc.WithAlpha(0.5f), 1);
                Gfx.Text(this, Fonts.Body, new Vector2(px + 4, y + 10), label, Ui.TextMicro, sc);
                px += bw + 4;
            }
            y += 17;
        }

        // 믿음 (두뇌 2.0): 출처 · 확신 · 몇 시간 전 — 세계와 어긋난 것은 주황
        foreach (var b in card.Beliefs)
        {
            if (!fits(14)) break;
            var bc = b.Wrong ? Palette.Warning : Palette.TextDim;
            ci_SourceDot(new Vector2(x + 5, y + 7), b.Src, bc);
            string tail = $"{BeliefSystem.SourceName(b.Src)} {b.Conf * 100f:0}% · {(b.AgeHours < 1f ? $"{b.AgeHours * 60f:0}분" : $"{b.AgeHours:0.#}시간")} 전{(b.Wrong ? " · 어긋남" : "")}";
            float tw = Gfx.Width(Fonts.Body, tail, Ui.TextMicro);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 13, y + 10), UiKit.Fit(b.Text, right - x - 18 - tw, Ui.TextTiny), Ui.TextTiny, bc);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, y + 10), tail, Ui.TextMicro, b.Wrong ? Palette.Warning : Palette.TextMuted);
            y += 13;
        }
        if (card.Correction != null && fits(13))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 10), UiKit.Fit($"고친 믿음 — {card.Correction}", right - x, Ui.TextMicro), Ui.TextMicro, Palette.TextMuted);
            y += 12;
        }
        if (card.Lessons.Count > 0 && fits(13))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 10), UiKit.Fit($"배운 것 — {string.Join(" · ", card.Lessons)}", right - x, Ui.TextMicro), Ui.TextMicro, Palette.TextMuted);
            y += 12;
        }
        return y + 6;
    }

    /// <summary>출처마다 다른 작은 표시: 본 것(눈 · 꽉 찬 원) · 경보/방송(물결) · 들은 것(말풍선 꼬리) · 소문/엿들음(속 빈 원) · 컴퓨터(네모) · 짐작(점선).</summary>
    private void ci_SourceDot(Vector2 p, BeliefSource s, Color col)
    {
        switch (s)
        {
            case BeliefSource.Seen: DrawCircle(p, 3.2f, col); DrawCircle(p, 1.3f, new Color(0.05f, 0.06f, 0.09f)); break;
            case BeliefSource.Alarm or BeliefSource.Broadcast or BeliefSource.Radio:
                DrawArc(p + new Vector2(-2, 0), 2f, -0.9f, 0.9f, 6, col, 1f, true); DrawArc(p + new Vector2(-2, 0), 4f, -0.8f, 0.8f, 8, col, 1f, true); break;
            case BeliefSource.Told:
                DrawCircle(p, 3f, col.WithAlpha(0.6f)); DrawColoredPolygon(new[] { p + new Vector2(-1, 2), p + new Vector2(-4, 5), p + new Vector2(1, 3) }, col.WithAlpha(0.6f)); break;
            case BeliefSource.Computer: DrawRect(new Rect2(p - new Vector2(3, 3), new Vector2(6, 6)), col, false, 1.2f); break;
            case BeliefSource.Guess: for (int i = 0; i < 6; i++) DrawCircle(p + new Vector2(Mathf.Cos(i * 1.05f), Mathf.Sin(i * 1.05f)) * 3f, 0.6f, col); break;
            default: DrawArc(p, 3f, 0, Mathf.Tau, 12, col, 1f, true); break;
        }
    }
}
