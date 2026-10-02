using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.20b 로봇 카드의 "생각" 칸 (지금 · 다음 · 배터리 · 왜 · 맡은 일의 단계)과 관제 "지휘" 탭의 함대 목록.
///  읽기만 한다 (완전 관전).
/// </summary>
public partial class Hud
{
    private static int RobotMindHeight(Robot r) => 92 + (r.Mind.Stages.Count > 1 ? 22 : 0);

    /// <summary>로봇 카드: 지금 하는 일 · 다음 일 · 배터리 셈 · 판단 이유 · 단계 칩 (높이를 더한 y를 돌려준다).</summary>
    private float DrawRobotMind(Robot r, float x, float right, float ly)
    {
        var m = r.Mind;
        float w = right - x;
        SectionTitle(x, ly + 10, "생각", null);
        ly += 18;
        void Line(string label, string text, Color col)
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 13), label, Ui.TextSmall, Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ly + 13), Fit(text, w - 44, Ui.TextSmall, Fonts.Body), Ui.TextSmall, col);
            ly += 17;
        }
        Line("지금", m.Now, Palette.Text);
        Line("다음", m.Next == "" ? "—" : m.Next, Palette.TextDim);
        Line("배터리", m.Power == "" ? $"배터리 {Pct(r.Battery)}" : m.Power, r.Battery < 0.25f ? Palette.Warning : Palette.TextDim);
        Line("왜", m.Why == "" ? "—" : m.Why, ShipView.RobotColor(r.Kind).Lightened(0.35f));
        if (m.Stages.Count > 1)
        {
            float cx = x;
            for (int i = 0; i < m.Stages.Count; i++)
            {
                bool now = i == m.Stage && r.State == RobotState.Active, done = i < m.Stage;
                string label = m.Stages[i];
                float tw = Gfx.Width(Fonts.Body, label, Ui.TextSmall) + 14f;
                if (cx + tw > right) break;
                var col = now ? Palette.Accent : done ? Palette.Good : Palette.TextMuted;
                Gfx.RoundRect(this, new Rect2(cx, ly + 2, tw, 16), col.WithAlpha(now ? 0.22f : 0.08f), 6, col.WithAlpha(0.6f));
                Gfx.Text(this, Fonts.Body, new Vector2(cx + 7, ly + 14), label, Ui.TextSmall, done ? Palette.TextDim : Palette.Text);
                cx += tw;
                if (i < m.Stages.Count - 1 && cx + 10 < right) { Gfx.Text(this, Fonts.Body, new Vector2(cx + 1, ly + 14), "›", Ui.TextSmall, Palette.TextMuted); cx += 10; }
            }
            ly += 22;
        }
        return ly + 6;
    }

    /// <summary>지휘 탭: 로봇 · 드론 한 줄씩 — 몸 표식 · 이름 · 지금 하는 일 · 배터리 막대 · 판단 이유.</summary>
    private void DrawFleetList(Rect2 r)
    {
        var w = _world;
        if (r.Size.Y < 30f) return;
        Gfx.RoundRect(this, r, Glass, 8, new Color(1, 1, 1, 0.05f));
        float x = r.Position.X + 8, right = r.End.X - 8, y = r.Position.Y + 4;
        int rows = Math.Max(1, (int)((r.Size.Y - 8) / 15f));
        var units = w.Robots.Robots.Where(b => b.State != RobotState.Lost || b.Wrecked)
            .Select(b => (name: b.Name, col: ShipView.RobotColor(b.Kind), bot: true, bad: b.Fault != null || b.Wrecked || b.State is RobotState.Stalled or RobotState.Towed,
                busy: b.State == RobotState.Active, bat: b.Battery, now: b.Wrecked ? b.Doing : b.Mind.Now, why: b.Mind.Why))
            .Concat(w.Drones.Drones.Where(d => d.State != DroneState.Lost || d.Wrecked)
            .Select(d => (name: d.Name, col: ShipView.DroneColor(d.Kind), bot: false, bad: d.Faulty || d.Wrecked || d.State == DroneState.Adrift,
                busy: d.Outside, bat: d.Battery, now: d.Mind.Now, why: d.Mind.Why)))
            .OrderByDescending(u => u.bad).ThenByDescending(u => u.busy).ToList();
        float nameW = 74f, barW = 26f;
        float nowW = (right - x - nameW - barW - 18f) * 0.5f;
        foreach (var u in units.Take(rows))
        {
            float cy = y + 7;
            // 몸 표식: 로봇은 둥근 네모 · 드론은 날개 넷
            if (u.bot) Gfx.RoundRect(this, new Rect2(x, cy - 4, 8, 8), u.col.WithAlpha(u.bad ? 0.35f : 0.8f), 2, u.bad ? Palette.Danger : null);
            else
            {
                for (int k = 0; k < 4; k++) DrawCircle(new Vector2(x + 4, cy) + Vector2.FromAngle(Mathf.Pi / 4f + k * Mathf.Pi / 2f) * 3.4f, 1.3f, u.col.WithAlpha(0.8f), true, -1f, true);
                DrawCircle(new Vector2(x + 4, cy), 1.8f, u.bad ? Palette.Danger : u.col, true, -1f, true);
            }
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 13, cy + 4), Fit(u.name, nameW - 13, Ui.TextMicro, Fonts.Bold), Ui.TextMicro, u.bad ? Palette.Danger : u.busy ? Palette.Text : Palette.TextDim);
            float bx = x + nameW;
            DrawRect(new Rect2(bx, cy - 2, barW, 4), new Color(1, 1, 1, 0.08f));
            DrawRect(new Rect2(bx, cy - 2, barW * Mathf.Clamp(u.bat, 0f, 1f), 4), (u.bat < 0.25f ? Palette.Warning : Palette.Good).WithAlpha(0.85f));
            float tx = bx + barW + 8;
            Gfx.Text(this, Fonts.Body, new Vector2(tx, cy + 4), Fit(u.now, nowW, Ui.TextMicro, Fonts.Body), Ui.TextMicro, Palette.TextDim);
            Gfx.Text(this, Fonts.Body, new Vector2(tx + nowW + 6, cy + 4), Fit(u.why, right - tx - nowW - 6, Ui.TextMicro, Fonts.Body), Ui.TextMicro, u.col.Lightened(0.3f).WithAlpha(0.85f));
            y += 15f;
        }
        if (units.Count > rows) Gfx.TextRight(this, Fonts.Body, new Vector2(right, r.End.Y - 3), $"외 {units.Count - rows}대", Ui.TextMicro, Palette.TextMuted);
    }
}
