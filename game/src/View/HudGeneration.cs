using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 세대: 조용한 달을 넘기면 배 위 가운데에 한 장 (그동안 생긴 일 · 9초 · 누르면 접는다).
public partial class Hud
{
    private long _quietSeen = -1;
    private float _quietAt = -1000f;
    private const float QuietSec = 9f;

    private void DrawQuietCard(Vector2 mouse)
    {
        var g = _world.Generation;
        if (g.LastSkip < 0 || g.Spans.Count == 0) return;
        if (g.LastSkip != _quietSeen)
        {
            _quietSeen = g.LastSkip;
            _quietAt = _world.Tick - g.LastSkip < SimTime.Hours(2) ? _time : -1000f;
        }
        if (_time - _sumAt < SumOpenSec + 0.4f || _time - _arrivalAt < ArrivalSec + 0.4f) _quietAt = Mathf.Max(_quietAt, _time - 0.01f); // 다른 큰 카드가 먼저
        float t = _time - _quietAt;
        if (t < 0f || t > QuietSec + 0.4f) return;
        var s = g.Spans[^1];
        float a = Mathf.Clamp(t / 0.4f, 0f, 1f) * Mathf.Clamp((QuietSec + 0.4f - t) / 0.4f, 0f, 1f);
        float right = _plan.Side.Empty ? ColX + RightColumnWidth : ColX - UiLayout.Gap;
        float wd = MathF.Min(520f, right - Margin * 2f);
        int rows = Math.Max(1, Math.Min(6, s.Lines.Count));
        float h = 74f + rows * 19f + 16f;
        float cx = (Margin + right) * 0.5f, y0 = Margin + UiLayout.TopBarH + 66f - (1f - a) * 14f;
        var card = new Rect2(cx - wd * 0.5f, y0, wd, h);
        Gfx.RoundRect(this, card, new Color(0.05f, 0.06f, 0.09f, 0.97f * a), 8f);
        Card(card);
        var tint = new Color("#c7b2ff");
        float x = card.Position.X + 18f, r = card.End.X - 18f, y = y0 + 24f;
        string mo = s.Months switch { 1 => "한 달", 2 => "두 달", 3 => "석 달", 4 => "넉 달", 5 => "다섯 달", 6 => "여섯 달", _ => $"{s.Months}달" };
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), $"조용한 {mo} · {s.Leg}", Ui.TextTiny, tint.WithAlpha(a));
        Gfx.TextRight(this, Fonts.Body, new Vector2(r, y), $"배의 달력 {g.Calendar}", Ui.TextTiny, Palette.TextMuted.WithAlpha(a));
        y += 26f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), s.Lines.Count == 0 ? "별일 없이 시간이 흘렀다" : $"{mo} 동안 배에서는", Ui.TextTitle, Palette.Text.WithAlpha(a));
        y += 22f;
        if (s.Lines.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, y), "모두 한 살씩 가까워졌고, 설비는 조금씩 낡았다.", Ui.TextSmall, Palette.TextDim.WithAlpha(a)); y += 19f; }
        foreach (var line in s.Lines.Take(6)) { Gfx.Text(this, Fonts.Body, new Vector2(x, y), UiKit.Fit("· " + line, r - x, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.Text.WithAlpha(a)); y += 19f; }
        _buttons.Add((card, () => _quietAt = _time - QuietSec));
    }
}
