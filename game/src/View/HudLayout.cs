using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.6 화면 배치 (UiLayout 규칙: 쌓고 · 모자라면 접는다) · 보기 선택판 하나 + 보기마다 범례.

public partial class Hud
{
    private HudPlan _plan = new();
    /// <summary>지난 그림의 배치 (카메라가 배를 맞출 자리 · 시험).</summary>
    public HudPlan Plan => _plan;
    private Vector2 _profileSz, _legendSz;
    private float _cosmicH;
    private bool _viewPickerOpen;

    private static Rect2 R(UiRect r) => new(r.X, r.Y, r.W, r.H);

    /// <summary>이번 그림의 배치를 정한다 (패널 크기는 지난 그림에서 잰 값).</summary>
    private void PlanLayout()
    {
        var a = _world.Automation;
        var bounds = _main.ShipView.Bounds;
        var mm = MinimapSize();
        bool tools = !Quiet || _toolsOpen || _hazardMenu || _main.Tool != IncidentTool.None;
        var n = new HudNeeds
        {
            W = Screen.X, H = Screen.Y, TopBarW = TopBarWidth(), FoldedToolsW = tools ? 0f : _foldedToolsW,
            ProfileW = _profileSz.X, ProfileH = _profileSz.Y, CosmicH = _cosmicH,
            LegendW = _legendSz.X, LegendH = _legendSz.Y,
            Computer = a.Present && !ControlOpen && !ChronicleOpen && !TechOpen && !PolicyOpen && !ChainOpen && !CollectionOpen && !VoyageOpen,
            ComputerH = ComputerFullHeight(), ComputerUserFolded = ComputerFolded,
            LogH = LogHeight, MinimapW = mm.X, MinimapH = mm.Y,
            Voyage = mm.X > 0f && _world.Voyage.TotalDays > 0f, Tools = tools,
            ShipW = Mathf.Max(1f, bounds.Size.X), ShipH = Mathf.Max(1f, bounds.Size.Y),
        };
        _plan = UiLayout.Plan(n);
    }

    /// <summary>주 컴퓨터 카드를 다 펼친 높이 (제안 카드 · 방금 정한 제안 하나 포함).</summary>
    private float ComputerFullHeight()
    {
        var a = _world.Automation;
        if (!a.Present) return 0f;
        int open = a.Asks.Open.Count() + (a.Asks.All.LastOrDefault(p => p.State != ProposalState.Pending && p.DecidedAt >= 0 && _world.Tick - p.DecidedAt < SimTime.Minutes(20)) != null ? 1 : 0);
        return UiLayout.ComputerHeight(a.Modules.Count, open, false);
    }

    private Vector2 MinimapSize()
    {
        if (!MinimapOpen || ChronicleOpen || TechOpen) return Vector2.Zero;
        var b = _main.ShipView.Bounds;
        if (b.Size.X < 1f) return Vector2.Zero;
        float scale = Mathf.Min(280f / b.Size.X, (LogFullHeight - 30f) / b.Size.Y);
        return b.Size * scale + new Vector2(24f, 36f);
    }

    private float TopBarWidth()
    {
        float nameW = Gfx.Width(Fonts.Bold, _world.Ship.Name, Ui.TextTitle);
        float clockW = Gfx.Width(Fonts.Bold, "00:00", Ui.TextClock);
        float dayW = Gfx.Width(Fonts.Body, $"{_world.Day}일차", Ui.TextBody);
        const float btnW = 40f, btnGap = 4f;
        float speedW = 5 * btnW + 4 * btnGap;
        return 18 + 16 + nameW + 20 + 20 + clockW + 8 + dayW + 20 + 14 + speedW + 8 + 52 + 12;
    }

    // ─────────────────────────────── 보기 선택판 ───────────────────────────────

    /// <summary>보기 고르기: 단추 하나 (지금 보기) → 누르면 선택판. Shift를 누른 채 고르면 겹쳐 보기.</summary>
    private float DrawViewPicker(Vector2 mouse)
    {
        string cur = ViewModes.Name(_main.ViewMode) + (_main.SecondaryView is ViewMode sv ? $" + {ViewModes.Name(sv)}" : "");
        float bw = Gfx.Width(Fonts.Bold, cur, Ui.TextBody) + 64f;
        var card = new Rect2(Margin, Margin + 52f + 8f, bw + 20f, 40f);
        Card(card);
        var btn = new Rect2(card.Position.X + 6, card.Position.Y + 6, bw, 28);
        bool hover = btn.HasPoint(mouse);
        Gfx.RoundRect(this, btn, _viewPickerOpen ? Palette.Accent.WithAlpha(0.16f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.02f), Ui.RadiusControl, _viewPickerOpen ? Palette.Accent.WithAlpha(0.45f) : null);
        ViewGlyph(_main.ViewMode, new Vector2(btn.Position.X + 14, btn.GetCenter().Y), 7f, _main.ViewMode == ViewMode.Normal ? Palette.TextDim : Palette.Accent);
        Gfx.Text(this, Fonts.Bold, new Vector2(btn.Position.X + 28, btn.GetCenter().Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), cur, Ui.TextBody, _main.ViewMode == ViewMode.Normal ? Palette.Text : Palette.Accent);
        Gfx.TextRight(this, Fonts.Body, new Vector2(btn.End.X - 8, btn.GetCenter().Y + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), _viewPickerOpen ? "▴" : "V ▾", Ui.TextSmall, Palette.TextMuted);
        _buttons.Add((btn, () => _viewPickerOpen = !_viewPickerOpen));
        if (_viewPickerOpen) _pickerAt = new Vector2(card.Position.X, card.End.Y + 6f);
        return card.End.X;
    }

    private Vector2 _pickerAt;

    /// <summary>선택판: 보기마다 그림 · 이름 · 한 줄 뜻 (떠 있는 판이라 맨 나중에 그린다).</summary>
    private void DrawViewPickerPanel(Vector2 mouse)
    {
        if (!_viewPickerOpen) return;
        var modes = ViewModes.All;
        const float cw = 168f, ch = 46f;
        int cols = 3, rows = (modes.Length + cols - 1) / cols;
        var card = new Rect2(_pickerAt, new Vector2(cols * cw + 20f, rows * ch + 42f));
        Card(card);
        Gfx.Text(this, Fonts.Bold, card.Position + new Vector2(14, 22), "무엇을 볼까", Ui.TextSmall, Palette.TextDim);
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 14, card.Position.Y + 22), "Shift — 겹쳐 보기", Ui.TextTiny, Palette.TextMuted);
        for (int i = 0; i < modes.Length; i++)
        {
            var m = modes[i];
            var r = new Rect2(card.Position.X + 10 + (i % cols) * cw, card.Position.Y + 32 + (i / cols) * ch, cw - 6, ch - 6);
            bool on = _main.ViewMode == m, second = _main.SecondaryView == m && !on, hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, on ? Palette.Accent.WithAlpha(0.14f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.02f), 8, on ? Palette.Accent.WithAlpha(0.45f) : second ? Palette.Warning.WithAlpha(0.6f) : null);
            ViewGlyph(m, new Vector2(r.Position.X + 18, r.GetCenter().Y), 9f, on ? Palette.Accent : Palette.TextDim);
            Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X + 34, r.Position.Y + 17), ViewModes.Name(m), Ui.TextBody, on ? Palette.Accent : Palette.Text);
            Gfx.Text(this, Fonts.Body, new Vector2(r.Position.X + 34, r.Position.Y + 32), Fit(ViewNote(m), cw - 46, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
            var mode = m;
            _buttons.Add((r, () =>
            {
                if (Input.IsKeyPressed(Key.Shift) && mode != ViewMode.Normal) _main.SecondaryView = _main.SecondaryView == mode ? null : mode;
                else { _main.ViewMode = mode; _viewPickerOpen = false; }
            }));
        }
    }

    private static string ViewNote(ViewMode m) => m switch
    {
        ViewMode.Normal => "배 그대로",
        ViewMode.Power => "어디에 전기가 가나",
        ViewMode.Air => "산소 · 기압 · 새는 곳",
        ViewMode.Temperature => "더운 곳 · 찬 곳",
        ViewMode.Condition => "설비가 얼마나 닳았나",
        ViewMode.Trace => "지나간 일의 자국",
        ViewMode.Structure => "뼈대에 걸린 힘",
        ViewMode.Pipes => "물 · 냉각수 흐름",
        ViewMode.Sensors => "감지기가 본 것",
        ViewMode.Ambience => "소리 · 빛 · 진동",
        ViewMode.Belief => "컴퓨터가 아는 배",
        _ => "",
    };

    /// <summary>보기마다 다른 작은 그림 (글자 없이 구분).</summary>
    private void ViewGlyph(ViewMode m, Vector2 c, float s, Color col)
    {
        switch (m)
        {
            case ViewMode.Normal: this.Box(new Rect2(c.X - s, c.Y - s * 0.6f, s * 2f, s * 1.2f), col, false, 1.3f); DrawLine(c + new Vector2(-s, 0), c + new Vector2(s, 0), col, 1f); break;
            case ViewMode.Power: this.Polyline(new[] { c + new Vector2(s * 0.2f, -s), c + new Vector2(-s * 0.5f, s * 0.1f), c + new Vector2(s * 0.3f, s * 0.1f), c + new Vector2(-s * 0.2f, s) }, col, 1.6f, true); break;
            case ViewMode.Air: for (int k = 0; k < 3; k++) this.Arc(c + new Vector2(0, (k - 1) * s * 0.6f), s * 0.8f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 8, col, 1.2f, true); break;
            case ViewMode.Temperature: this.Box(new Rect2(c.X - 1.5f, c.Y - s, 3f, s * 1.4f), col, false, 1.2f); this.Circle(c + new Vector2(0, s * 0.6f), s * 0.4f, col, true, -1, true); break;
            case ViewMode.Condition: this.Arc(c, s * 0.8f, 0, Mathf.Tau, 14, col, 1.3f, true); DrawLine(c, c + Vector2.FromAngle(-0.9f) * s * 0.7f, col, 1.5f, true); break;
            case ViewMode.Trace: for (int k = 0; k < 3; k++) this.Circle(c + new Vector2((k - 1) * s * 0.7f, (k % 2) * s * 0.5f - s * 0.2f), s * 0.25f, col, true, -1, true); break;
            case ViewMode.Structure: this.Polyline(new[] { c + new Vector2(-s, s * 0.7f), c + new Vector2(0, -s * 0.7f), c + new Vector2(s, s * 0.7f), c + new Vector2(-s, s * 0.7f) }, col, 1.3f, true); DrawLine(c + new Vector2(0, -s * 0.7f), c + new Vector2(0, s * 0.7f), col, 1f); break;
            case ViewMode.Pipes: this.Polyline(new[] { c + new Vector2(-s, -s * 0.4f), c + new Vector2(0, -s * 0.4f), c + new Vector2(0, s * 0.4f), c + new Vector2(s, s * 0.4f) }, col, 2.2f, true); break;
            case ViewMode.Sensors: this.Circle(c, s * 0.25f, col, true, -1, true); this.Arc(c, s * 0.6f, -0.8f, 0.8f, 6, col, 1.2f, true); this.Arc(c, s, -0.8f, 0.8f, 8, col, 1.2f, true); break;
            case ViewMode.Ambience: this.Polyline(Enumerable.Range(0, 9).Select(k => c + new Vector2(-s + k * s / 4f, Mathf.Sin(k * 1.4f) * s * 0.5f)).ToArray(), col, 1.3f, true); break;
            case ViewMode.Belief: this.Arc(c, s * 0.8f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 8, col, 1.3f, true); this.Arc(c, s * 0.8f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 8, col, 1.3f, true); this.Circle(c, s * 0.3f, col, true, -1, true); break;
        }
    }

    // ─────────────────────────────── 범례 ───────────────────────────────

    /// <summary>범례 한 칸의 표시: 색만으로 갈리지 않게 모양 · 무늬가 함께 다르다.</summary>
    private enum Swatch { Solid, Hatch, Dots, Ring, Wave, Bar, Cross, Line }

    private static (Swatch s, Color c, string label)[] LegendOf(ViewMode m) => m switch
    {
        ViewMode.Power => new[] { (Swatch.Solid, Palette.Good, "전기가 들어온다"), (Swatch.Hatch, Palette.Warning, "모자라 줄였다"), (Swatch.Cross, Palette.Danger, "끊겼다"), (Swatch.Line, Palette.TextMuted, "일부러 내린 회로") },
        ViewMode.Air => new[] { (Swatch.Solid, Palette.Accent, "숨쉬기 좋다"), (Swatch.Dots, Palette.Warning, "산소가 옅다"), (Swatch.Hatch, Palette.Danger, "기압이 빠졌다") },
        ViewMode.Temperature => new[] { (Swatch.Wave, new Color("#6cb8ff"), "차갑다"), (Swatch.Solid, Palette.Good, "알맞다"), (Swatch.Hatch, new Color("#ff7a5c"), "뜨겁다") },
        ViewMode.Condition => new[] { (Swatch.Bar, Palette.Good, "멀쩡하다"), (Swatch.Dots, Palette.Warning, "닳았다"), (Swatch.Cross, Palette.Danger, "고장") },
        ViewMode.Trace => new[] { (Swatch.Dots, new Color("#c97a6b"), "자국 (피 · 물 · 그을음)"), (Swatch.Ring, Palette.Warning, "고른 사람이 겁내는 곳"), (Swatch.Hatch, Palette.TextMuted, "버렸던 방") },
        ViewMode.Structure => new[] { (Swatch.Solid, Palette.Good, "버틴다"), (Swatch.Hatch, Palette.Warning, "힘이 몰린다"), (Swatch.Cross, Palette.Danger, "갈라졌다") },
        ViewMode.Pipes => new[] { (Swatch.Line, Palette.Good, "흐른다"), (Swatch.Dots, Palette.Warning, "막혔다 · 잠갔다"), (Swatch.Cross, Palette.Danger, "샌다") },
        ViewMode.Sensors => new[] { (Swatch.Solid, Palette.Good, "방금 쟀다"), (Swatch.Dots, Palette.Warning, "오래됐다"), (Swatch.Cross, Palette.Danger, "멈췄다"), (Swatch.Ring, new Color("#b58cff"), "헛경보") },
        ViewMode.Ambience => new[] { (Swatch.Wave, Palette.Warning, "시끄럽다"), (Swatch.Line, new Color("#b58cff"), "떨린다"), (Swatch.Solid, Palette.Good, "조용하다") },
        ViewMode.Belief => new[] { (Swatch.Solid, Palette.Accent, "컴퓨터가 안다"), (Swatch.Hatch, Palette.Warning, "아는 것과 다르다"), (Swatch.Dots, Palette.TextMuted, "모른다 (선이 끊김)") },
        _ => Array.Empty<(Swatch, Color, string)>(),
    };

    /// <summary>지금 보기의 범례 (일반 보기면 없음). 자리는 왼쪽 위 더미에서.</summary>
    private void DrawViewLegend()
    {
        var items = LegendOf(_main.ViewMode);
        if (items.Length == 0 || ControlOpen || ChronicleOpen || TechOpen || PolicyOpen || ChainOpen || CouncilOpen || VoyageOpen) { _legendSz = Vector2.Zero; return; }
        float w = 26f + items.Max(i => Gfx.Width(Fonts.Body, i.label, Ui.TextSmall)) + 34f;
        w = Mathf.Max(w, Gfx.Width(Fonts.Bold, ViewModes.Name(_main.ViewMode) + " 보기", Ui.TextSmall) + 28f);
        float h = 32f + items.Length * 20f + 6f;
        _legendSz = new Vector2(w, h);
        if (_plan.Legend.Empty) return; // 자리가 없으면 이번엔 숨긴다 (배치 규칙)
        var card = new Rect2(_plan.Legend.X, _plan.Legend.Y, w, h);
        Card(card);
        ViewGlyph(_main.ViewMode, card.Position + new Vector2(18, 17), 6f, Palette.Accent);
        Gfx.Text(this, Fonts.Bold, card.Position + new Vector2(30, 21), ViewModes.Name(_main.ViewMode) + " 보기", Ui.TextSmall, Palette.TextDim);
        for (int i = 0; i < items.Length; i++)
        {
            var (s, c, label) = items[i];
            float y = card.Position.Y + 32 + i * 20;
            DrawSwatch(new Rect2(card.Position.X + 14, y + 3, 18, 12), s, ColorSafe.Tint(c));
            Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 40, y + 13), label, Ui.TextSmall, Palette.Text);
        }
    }

    private void DrawSwatch(Rect2 r, Swatch s, Color c)
    {
        Gfx.RoundRect(this, r, c.WithAlpha(s == Swatch.Solid ? 0.75f : 0.16f), 3, c.WithAlpha(0.8f));
        switch (s)
        {
            case Swatch.Hatch: for (float k = -r.Size.Y; k < r.Size.X; k += 4f) DrawLine(new Vector2(r.Position.X + Mathf.Max(0, k), r.End.Y - Mathf.Max(0, -k)), new Vector2(r.Position.X + Mathf.Min(r.Size.X, k + r.Size.Y), r.Position.Y + Mathf.Max(0, k + r.Size.Y - r.Size.X)), c, 1f); break;
            case Swatch.Dots: for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++) this.Circle(r.Position + new Vector2(4 + i * 5, 4 + j * 5), 1.3f, c, true, -1, true); break;
            case Swatch.Ring: this.Arc(r.GetCenter(), 4f, 0, Mathf.Tau, 12, c, 1.4f, true); break;
            case Swatch.Wave: this.Polyline(Enumerable.Range(0, 7).Select(k => new Vector2(r.Position.X + 2 + k * 2.4f, r.GetCenter().Y + Mathf.Sin(k * 1.6f) * 3f)).ToArray(), c, 1.3f, true); break;
            case Swatch.Bar: this.Box(new Rect2(r.Position.X + 3, r.GetCenter().Y - 1.5f, r.Size.X - 6, 3), c); break;
            case Swatch.Cross: DrawLine(r.Position + new Vector2(4, 2), r.End - new Vector2(4, 2), c, 1.5f, true); DrawLine(new Vector2(r.Position.X + 4, r.End.Y - 2), new Vector2(r.End.X - 4, r.Position.Y + 2), c, 1.5f, true); break;
            case Swatch.Line: DrawLine(new Vector2(r.Position.X + 2, r.GetCenter().Y), new Vector2(r.End.X - 2, r.GetCenter().Y), c, 2f, true); break;
        }
    }
}
