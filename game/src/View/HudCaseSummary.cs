using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 연출: 사고 결산 카드 — 지휘 건이 닫히면 배 위 가운데에 크게 (무엇이 · 누가 · 잃은 것 · 지킨 것 · 로봇 활약 · 주컴퓨터 판단)
//   7초 뒤 왼쪽 위 작은 띠로 접힌다 (1분 동안 · 누르면 다시 편다). 작은 건(크게 띄울 것 없는)은 띠로만.
public partial class Hud
{
    private int _sumSeen;
    private float _sumAt = -1000f, _sumChipAt = -1000f;
    private CaseSummary? _sumShown;
    private const float SumOpenSec = 7f, SumChipSec = 60f;

    private void DrawCaseSummaryCard(Vector2 mouse)
    {
        var list = _world.Commander.Summaries;
        if (list.Count == 0) return;
        var last = list[^1];
        if (last.CaseId != _sumSeen)
        {
            _sumSeen = last.CaseId;
            _sumShown = last;
            if (last.Big) _sumAt = _time;
            _sumChipAt = last.Big ? _time + SumOpenSec : _time;
        }
        if (_sumShown is not CaseSummary s) return;
        float t = _time - _sumAt;
        if (t >= 0f && t < SumOpenSec + 0.4f) DrawSummaryBig(s, t, mouse);
        else if (_time - _sumChipAt < SumChipSec && _time >= _sumChipAt) DrawSummaryChip(s, mouse);
    }

    private static Color KindTint(CaseKind k) => k switch { CaseKind.Fire => new Color("#ff7a4a"), CaseKind.Breach => new Color("#9fb3ff"), _ => new Color("#ff8fa0") };

    private void DrawSummaryBig(CaseSummary s, float t, Vector2 mouse)
    {
        float right = _plan.Side.Empty ? ColX + RightColumnWidth : ColX - UiLayout.Gap;
        float w = MathF.Min(580f, right - Margin * 2f);
        float cx = (Margin + right) * 0.5f;
        int rows = Math.Max(Math.Max(s.Lost.Count, s.Kept.Count), 1);
        float h = 74f + rows * 18f + 70f;
        float a = Mathf.Clamp(t / 0.25f, 0f, 1f) * Mathf.Clamp((SumOpenSec + 0.4f - t) / 0.4f, 0f, 1f);
        float y0 = Margin + UiLayout.TopBarH + 66f - (1f - a) * 14f; // 경보 띠 아래
        var card = new Rect2(cx - w * 0.5f, y0, w, h);
        Card(card);
        var tint = KindTint(s.Kind);
        this.Box(new Rect2(card.Position + new Vector2(0, 8), new Vector2(4, h - 16)), tint.WithAlpha(a));
        float x = card.Position.X + 18f, r = card.End.X - 18f, y = y0 + 24f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), $"결산 · 지휘 건 #{s.CaseId}", Ui.TextTiny, tint.WithAlpha(a));
        Gfx.TextRight(this, Fonts.Body, new Vector2(r, y), $"{SimTime.Clock(s.Opened)} → {SimTime.Clock(s.Closed)} · {s.What}", Ui.TextTiny, Palette.TextMuted.WithAlpha(a));
        y += 24f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), UiKit.Fit(s.Title, r - x, Ui.TextTitle, Fonts.Bold), Ui.TextTitle, Palette.Text.WithAlpha(a));
        y += 22f;
        float mid = (x + r) * 0.5f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "잃은 것", Ui.TextSmall, Palette.Danger.WithAlpha(a));
        Gfx.Text(this, Fonts.Bold, new Vector2(mid + 8f, y), "지킨 것", Ui.TextSmall, Palette.Good.WithAlpha(a));
        for (int i = 0; i < rows; i++)
        {
            float ly = y + 18f + i * 18f;
            string lost = i < s.Lost.Count ? s.Lost[i] : i == 0 ? "없음" : "";
            string kept = i < s.Kept.Count ? s.Kept[i] : "";
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly), UiKit.Fit(lost, mid - x - 8f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, (s.Lost.Count == 0 ? Palette.TextMuted : Palette.Text).WithAlpha(a));
            Gfx.Text(this, Fonts.Body, new Vector2(mid + 8f, ly), UiKit.Fit(kept, r - mid - 8f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.Text.WithAlpha(a));
        }
        y += 18f + rows * 18f + 8f;
        Line("누가", s.Who.Count > 0 ? string.Join(" · ", s.Who) : "—");
        Line("로봇", s.Robots);
        Line("주컴퓨터", s.Computer);
        _buttons.Add((card, () => { _sumAt = _time - SumOpenSec; _sumChipAt = _time; })); // 누르면 바로 접는다

        void Line(string head, string text)
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y), head, Ui.TextSmall, Palette.TextDim.WithAlpha(a));
            Gfx.Text(this, Fonts.Body, new Vector2(x + 66f, y), UiKit.Fit(text, r - x - 66f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.Text.WithAlpha(a));
            y += 20f;
        }
    }

    private void DrawSummaryChip(CaseSummary s, Vector2 mouse)
    {
        string text = $"결산 #{s.CaseId} · {s.Title} ({s.Minutes:0}분)";
        float w = MathF.Min(Gfx.Width(Fonts.Bold, text, Ui.TextSmall) + 34f, 420f);
        var rect = new Rect2(Margin, Margin + UiLayout.TopBarH + UiLayout.Gap, w, 28f);
        bool hover = rect.HasPoint(mouse);
        Card(rect);
        var tint = KindTint(s.Kind);
        this.Box(new Rect2(rect.Position + new Vector2(0, 6), new Vector2(3, 16)), tint);
        float fade = Mathf.Clamp((SumChipSec - (_time - _sumChipAt)) / 3f, 0f, 1f);
        Gfx.Text(this, Fonts.Bold, new Vector2(rect.Position.X + 14f, rect.Position.Y + 19f), UiKit.Fit(text, w - 24f, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, (hover ? Palette.Text : Palette.TextDim).WithAlpha(fade));
        _buttons.Add((rect, () => { _sumAt = _time; _sumChipAt = _time + SumOpenSec; }));
    }
}
