using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 항해의 목적: 임무 띠(왼쪽 위 더미 — 대재난 · 행성이 없을 때) · 도착 카드(크게 → 접힘) · 기록 탭 '항해의 책'.
public partial class Hud
{
    private long _arrivalSeen = -1;
    private float _arrivalAt = -1000f;
    private int _bookPage;
    private const float ArrivalSec = 12f;

    private static Color MissionTint(MissionKind k) => k switch
    {
        MissionKind.Cargo => new Color("#e8b45a"), MissionKind.Survey => new Color("#8fd0ff"), MissionKind.Rescue => new Color("#ff8fa0"), MissionKind.Colony => new Color("#9fe0b0"), _ => new Color("#c7b2ff"),
    };

    /// <summary>임무 띠 (두 줄): 무엇을 · 얼마나 · 기한. 그렸으면 true.</summary>
    private bool DrawMissionPanel(float x, float y, float width, Vector2 mouse)
    {
        var w = _world;
        var pu = w.Purpose;
        if (pu.Now is not VoyageMission m) return false;
        const float h = 54f;
        _cosmicH = h;
        if (_plan.Cosmic.Empty) return true;
        var card = new Rect2(x, y, width, _plan.Cosmic.H);
        Card(card, pu.Behind ? Tone.Caution : Tone.Info);
        var tint = MissionTint(m.Kind);
        Gfx.Pill(this, Fonts.Bold, new Vector2(x + 30f, y + 18f), PurposeSystem.KindName(m.Kind), Ui.TextTiny, tint, new Color(0.03f, 0.04f, 0.07f, 0.9f), tint.WithAlpha(0.5f), 7f, 3f);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 58f, y + 22f), Fit(m.Title, width - 70f, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, Palette.Text);
        float left = pu.DaysLeft;
        string time = left >= 0f ? $"기한 {left:0.0}일 · 남은 길 {pu.RouteLeft:0.0}일" : $"기한 {-left:0.0}일 지남";
        Gfx.Text(this, Fonts.Body, new Vector2(x + 12f, y + 42f), Fit($"{PurposeSystem.Progress(m)} · {time}" + (pu.Behind ? " · 빠듯하다" : ""), width - 24f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, pu.Behind ? Palette.Warning : Palette.TextDim);
        if (card.HasPoint(mouse))
        {
            var at = new Vector2(x, card.End.Y + 4f);
            var lines = new System.Collections.Generic.List<TipLine> { new(m.Brief, Palette.TextDim) };
            foreach (var n in m.Notes.TakeLast(5)) lines.Add(new TipLine(n, Palette.TextMuted));
            _tip = () => UiKit.Tooltip(this, at, Screen, $"{m.Client} — {m.Title}", lines);
        }
        return true;
    }

    /// <summary>도착 카드: 목적지에 닿으면 크게 (임무 결과 · 항해의 책 첫 장들).</summary>
    private void DrawArrivalCard(Vector2 mouse)
    {
        var pu = _world.Purpose;
        if (pu.LastArrival < 0 || pu.Books.Count == 0 || pu.Past.Count == 0) return;
        if (pu.LastArrival != _arrivalSeen)
        {
            _arrivalSeen = pu.LastArrival;
            _arrivalAt = _world.Tick - pu.LastArrival < SimTime.Hours(2) ? _time : -1000f;
        }
        float t = _time - _arrivalAt;
        if (t < 0f || t > ArrivalSec + 0.4f) return;
        var b = pu.Books[^1];
        var m = pu.Past[^1];
        float a = Mathf.Clamp(t / 0.3f, 0f, 1f) * Mathf.Clamp((ArrivalSec + 0.4f - t) / 0.4f, 0f, 1f);
        float right = _plan.Side.Empty ? ColX + RightColumnWidth : ColX - UiLayout.Gap;
        float wd = MathF.Min(620f, right - Margin * 2f);
        int ch = Math.Min(5, b.Chapters.Count);
        float h = 110f + ch * 18f + 40f;
        float cx = (Margin + right) * 0.5f, y0 = Margin + UiLayout.TopBarH + 66f - (1f - a) * 14f;
        var card = new Rect2(cx - wd * 0.5f, y0, wd, h);
        Gfx.RoundRect(this, card, new Color(0.05f, 0.06f, 0.09f, 0.97f * a), 8f); // 뒤의 카드가 비치지 않게
        Card(card);
        var tint = m.Success ? Palette.Good : Palette.Warning;
        this.Box(new Rect2(card.Position + new Vector2(0, 8), new Vector2(4, h - 16)), tint.WithAlpha(a));
        float x = card.Position.X + 18f, r = card.End.X - 18f, y = y0 + 24f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), $"{b.Number}번째 항해 끝 · {b.From} → {b.To} · {b.Days:0.0}일", Ui.TextTiny, tint.WithAlpha(a));
        y += 26f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), UiKit.Fit($"{b.To} 도착 — {m.Title}", r - x, Ui.TextTitle, Fonts.Bold), Ui.TextTitle, Palette.Text.WithAlpha(a));
        y += 22f;
        Gfx.Text(this, Fonts.Body, new Vector2(x, y), UiKit.Fit(m.Result, r - x, Ui.TextSmall, Fonts.Body), Ui.TextSmall, tint.WithAlpha(a));
        y += 24f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "항해의 책", Ui.TextSmall, Palette.TextDim.WithAlpha(a));
        y += 18f;
        foreach (var c in b.Chapters.Take(ch)) { Gfx.Text(this, Fonts.Body, new Vector2(x, y), UiKit.Fit(c, r - x, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Text.WithAlpha(a)); y += 18f; }
        y += 6f;
        string ppl = (b.Lost.Count > 0 ? $"잃은 사람 {string.Join("·", b.Lost)} · " : "") + (b.Left.Count > 0 ? $"내린 사람 {string.Join("·", b.Left.Take(4))}{(b.Left.Count > 4 ? " …" : "")} · " : "") + (b.Joined.Count > 0 ? $"탄 사람 {string.Join("·", b.Joined.Take(4))} · " : "");
        Gfx.Text(this, Fonts.Body, new Vector2(x, y), UiKit.Fit(ppl + $"다음 항해로 — {b.Carried}", r - x, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted.WithAlpha(a));
        _buttons.Add((card, () => _arrivalAt = _time - ArrivalSec));
    }

    /// <summary>기록 탭 '항해의 책': 지금 임무 · 지난 항해의 책 (새 것이 위).</summary>
    private void DrawBookPage(Vector2 mouse)
    {
        var pu = _world.Purpose;
        var card = PageCard(_dockBody.Size.Y);
        float x = card.Position.X + 18f, y = card.Position.Y + 30f, right = card.End.X - 18f, bottom = card.End.Y - 12f, wd = right - x;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "항해의 책", Ui.TextTitle, Palette.Text);
        Gfx.TextRight(this, Fonts.Body, new Vector2(right, y), $"지난 항해 {pu.Books.Count}권", Ui.TextTiny, Palette.TextMuted);
        y += 26f;
        if (pu.Now is VoyageMission m)
        {
            var tint = MissionTint(m.Kind);
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y), UiKit.Fit($"지금 — {_world.Voyage.Number}번째 항해 · {PurposeSystem.KindName(m.Kind)}: {m.Title}", wd, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, tint);
            y += 18f;
            Gfx.Text(this, Fonts.Body, new Vector2(x, y), UiKit.Fit($"{m.Client} · {PurposeSystem.Progress(m)} · 기한 {pu.DaysLeft:0.0}일 · 남은 길 {pu.RouteLeft:0.0}일 · 보수 {m.Reward:0}", wd, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
            y += 16f;
            foreach (var n in m.Notes.TakeLast(4)) { Gfx.Text(this, Fonts.Body, new Vector2(x + 8f, y), UiKit.Fit(n, wd - 8f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted); y += 15f; }
            y += 10f;
        }
        if (pu.Books.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 10f), "아직 끝난 항해가 없다 — 목적지에 닿으면 첫 권이 쓰인다.", Ui.TextSmall, Palette.TextDim); return; }
        var books = pu.Books.AsEnumerable().Reverse().ToList();
        _bookPage = Math.Clamp(_bookPage, 0, books.Count - 1);
        var b = books[_bookPage];
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), UiKit.Fit($"{b.Number}권 — {b.From} → {b.To} · {b.Days:0.0}일", wd - 140f, Ui.TextSubtitle, Fonts.Bold), Ui.TextSubtitle, b.Success ? Palette.Good : Palette.Warning);
        if (books.Count > 1)
        {
            var prev = new Rect2(right - 130f, y - 14f, 60f, 20f);
            var next = new Rect2(right - 60f, y - 14f, 60f, 20f);
            Gfx.Text(this, Fonts.Body, new Vector2(prev.Position.X, y), "◀ 새 권", Ui.TextTiny, _bookPage > 0 ? Palette.Accent : Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(next.Position.X, y), "옛 권 ▶", Ui.TextTiny, _bookPage < books.Count - 1 ? Palette.Accent : Palette.TextMuted);
            _buttons.Add((prev, () => _bookPage = Math.Max(0, _bookPage - 1)));
            _buttons.Add((next, () => _bookPage++));
        }
        y += 20f;
        void Line(string head, string text, Color col)
        {
            if (y > bottom - 14f) return;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y), head, Ui.TextTiny, Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 56f, y), UiKit.Fit(text, wd - 56f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, col);
            y += 17f;
        }
        Line("임무", b.Mission, Palette.Text);
        Line("결과", b.Result, b.Success ? Palette.Good : Palette.Warning);
        if (b.Planets.Count > 0) Line("행성", string.Join(" · ", b.Planets), Palette.Text);
        if (b.Lost.Count > 0) Line("잃은 사람", string.Join(" · ", b.Lost), Palette.Danger);
        if (b.Left.Count > 0) Line("내린 사람", string.Join(" · ", b.Left), Palette.TextDim);
        if (b.Joined.Count > 0) Line("탄 사람", string.Join(" · ", b.Joined), Palette.TextDim);
        Line("이어진 것", b.Carried, Palette.TextDim);
        y += 6f;
        foreach (var c in b.Chapters) { if (y > bottom - 10f) break; Gfx.Text(this, Fonts.Body, new Vector2(x + 8f, y), UiKit.Fit("· " + c, wd - 8f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Text); y += 16f; }
    }
}
