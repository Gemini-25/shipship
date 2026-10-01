using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.2 조용한 HUD 3단계: ① 평소엔 시간 · 배속 · 경보 · 핵심 자원만 ② 이상이 생긴 항목만 떠오른다
/// (자원이 줄기 시작 · 긴급 작업 · 아픈 사람) ③ 깊이 보기(승무원 목록 · 작업 · 기록 · 도구)는 열 때만.
/// 설정의 "전부 보기"를 켜면 예전처럼 모두 띄운다. 일시정지는 화면 테두리 · 채도로, 단축키는 ? 도움말로.
/// </summary>
public partial class Hud
{
    private bool _rosterOpen, _workOpen, _toolsOpen;

    /// <summary>? 도움말 창.</summary>
    public bool HelpOpen { get; set; }
    public void ToggleHelp() => HelpOpen = !HelpOpen;

    // ─────────────────────────── 승무원: 살펴볼 사람만 ───────────────────────────

    /// <summary>살펴볼 까닭 (없으면 null) — 아이콘 · 글 · 뜻.</summary>
    private (string icon, string text, Tone tone)? Attention(CrewMember c)
    {
        long now = _world.Tick;
        if (c.Dead) return c.DiedAt >= 0 && now - c.DiedAt < SimTime.Hours(6) ? ("dead", "사망", Tone.Disabled) : null;
        if (c.CarriedBy != null) return ("down", $"{c.CarriedBy.Name}에게 업혀 감", Tone.Danger);
        if (c.Down) return ("down", c.CareBed != null ? "의식 없음 · 치료 침대" : "쓰러짐 — 구조를 기다림", Tone.Danger);
        if (c.Mind.Panicking(now)) return ("panic", c.Mind.Frozen ? "공황 · 얼어붙음" : "공황", Tone.Danger);
        if (c.Suit is SuitState s && s.Oxygen < 0.75f) return ("suit", $"우주복 산소 {s.Oxygen:0.0}시간", Tone.Danger);
        if (c.Vitals.Oxygen < 0.85f) return ("oxygen", $"숨이 가쁘다 · 혈중 산소 {c.Vitals.Oxygen * 100:0}%", Tone.Danger);
        if (c.Vitals.Health < 0.5f) return ("health", $"체력 {c.Vitals.Health * 100:0}%", Tone.Danger);
        if (c.Vitals.Injury > 0.3f) return ("injury", $"부상 {c.Vitals.Injury * 100:0}%" + (c.Vitals.InjuryCause != null ? $" · {c.Vitals.InjuryCause}" : ""), Tone.Caution);
        if (DiseaseSystem.Sick(c)) return ("sick", "아프다", Tone.Caution);
        if (c.Needs.Food < 0.12f) return ("eat", "굶주림", Tone.Caution);
        if (c.Needs.Rest < 0.1f) return ("sleep", "탈진", Tone.Caution);
        if (c.Needs.Stress > 0.85f) return ("stress", "스트레스가 한계", Tone.Caution);
        if (c.Outside) return ("suit", "선외 작업", Tone.Info);
        return null;
    }

    /// <summary>조용한 승무원 칸: 머리글(인원 · 괜찮은지) + 살펴볼 사람만 + 작업 한 줄. 끝 y를 돌려준다.</summary>
    private float DrawCrewSummary(Vector2 mouse)
    {
        var crew = _world.Crew;
        var watch = new List<(CrewMember c, string icon, string text, Tone tone)>();
        foreach (var c in crew)
            if (Attention(c) is var (icon, text, tone)) watch.Add((c, icon, text, tone));
        watch = watch.OrderBy(a => a.tone == Tone.Danger ? 0 : a.tone == Tone.Caution ? 1 : 2).ThenBy(a => a.c.Id).ToList();
        const int maxRows = 6;
        int rows = Math.Min(maxRows, watch.Count);
        var all = _world.Board.Open.ToList();
        int urgent = all.Count(o => o.Urgency >= 0.9f);
        const float headH = 40f, rowH = 26f, footH = 34f;
        float x0 = Screen.X - Margin - RightColumnWidth;
        var card = new Rect2(x0, Margin, RightColumnWidth, headH + rows * rowH + (watch.Count > maxRows ? 18 : 0) + footH + 4);
        var worst = watch.Count == 0 ? Tone.Good : watch.Any(a => a.tone == Tone.Danger) ? Tone.Danger : Tone.Caution;
        Card(card, watch.Count > 0 ? worst : null);
        float x = x0 + Ui.Pad, right = card.End.X - Ui.Pad;

        // 머리글: 누르면 전체 목록
        var head = new Rect2(x0 + 6, card.Position.Y + 6, RightColumnWidth - 12, headH - 10);
        if (head.HasPoint(mouse)) Gfx.RoundRect(this, head, Ui.HoverSoft, Ui.RadiusControl);
        float hy = head.GetCenter().Y;
        int alive = crew.Count(c => !c.Dead);
        UiKit.IconText(this, "people", new Vector2(x, hy), $"승무원 {alive}명", Ui.TextLabel, Palette.Text, Fonts.Bold, Palette.TextDim);
        string state = watch.Count == 0 ? "모두 괜찮다" : $"살펴볼 사람 {watch.Count}";
        Icons.Draw(this, "chevron-down", new Vector2(right - 6, hy), 14, Palette.TextMuted);
        Gfx.TextRight(this, Fonts.Bold, new Vector2(right - 18, hy + Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), state, Ui.TextSmall, Ui.Of(worst));
        _buttons.Add((head, () => _rosterOpen = true));

        float y = card.Position.Y + headH;
        foreach (var (c, icon, text, tone) in watch.Take(maxRows))
        {
            var row = new Rect2(x0 + 8, y, RightColumnWidth - 16, rowH - 2);
            bool sel = _main.SelectedCrew == c;
            if (sel) Gfx.RoundRect(this, row, Palette.Crew(c.Id).WithAlpha(0.12f), 6, Palette.Crew(c.Id).WithAlpha(0.3f));
            else if (row.HasPoint(mouse)) Gfx.RoundRect(this, row, Ui.HoverSoft, 6);
            float cy = row.GetCenter().Y;
            Icons.Draw(this, icon, new Vector2(row.Position.X + 12, cy), Ui.IconM, Ui.Of(tone));
            DrawCircle(new Vector2(row.Position.X + 28, cy), 3.5f, Palette.Crew(c.Id), true, -1f, true);
            Gfx.Text(this, Fonts.Bold, new Vector2(row.Position.X + 36, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), c.Name, Ui.TextBody, Palette.Text);
            float nx = row.Position.X + 36 + Gfx.Width(Fonts.Bold, c.Name, Ui.TextBody) + 8;
            Gfx.TextRight(this, Fonts.Body, new Vector2(row.End.X - 8, cy + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), UiKit.Fit(text, row.End.X - 8 - nx, Ui.TextSmall), Ui.TextSmall, Ui.Of(tone));
            var target = c;
            _buttons.Add((row, () => { _main.Select(_main.SelectedCrew == target ? null : target); if (_main.SelectedCrew == target) FocusAt(target.Position); }));
            y += rowH;
        }
        if (watch.Count > maxRows)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), $"그 밖에 {watch.Count - maxRows}명 — 머리글을 누르면 모두", Ui.TextTiny, Palette.TextMuted);
            y += 18;
        }

        // 작업 한 줄: 누르면 작업 목록
        UiKit.Divider(this, x0 + 12, card.End.X - 12, y + 2);
        var foot = new Rect2(x0 + 6, y + 4, RightColumnWidth - 12, footH - 6);
        if (foot.HasPoint(mouse)) Gfx.RoundRect(this, foot, Ui.HoverSoft, Ui.RadiusControl);
        float fy = foot.GetCenter().Y;
        UiKit.IconText(this, "work", new Vector2(x, fy), $"작업 {all.Count}건", Ui.TextBody, Palette.TextDim);
        if (urgent > 0) UiKit.Badge(this, new Vector2(x + 90, fy), $"긴급 {urgent}", Tone.Danger, "alert");
        Icons.Draw(this, _workOpen ? "chevron-up" : "chevron-down", new Vector2(right - 6, fy), 14, Palette.TextMuted);
        _buttons.Add((foot, () => _workOpen = !_workOpen));
        return card.End.Y;
    }

    /// <summary>전체 목록을 열어 둔 조용한 HUD: 목록 위에 "접기" 단추.</summary>
    private void DrawRosterFold(float rosterBottom, Vector2 mouse)
    {
        var r = new Rect2(Screen.X - Margin - RightColumnWidth - 30, Margin + 6, 24, 24);
        bool hover = r.HasPoint(mouse);
        Gfx.RoundRect(this, r, hover ? new Color(0.09f, 0.11f, 0.15f, 0.97f) : Ui.PanelFill, 6, Ui.PanelEdge);
        _cards.Add(r);
        Icons.Draw(this, "chevron-up", r.GetCenter(), 14, hover ? Palette.Text : Palette.TextMuted);
        _buttons.Add((r, () => _rosterOpen = false));
    }

    // ─────────────────────────── 보기 · 사고 도구 (접힘) ───────────────────────────

    /// <summary>조용한 HUD: 보기 모드 · 사고 도구 막대를 한 장으로 접어 둔다 (누르면 펼침).</summary>
    private void DrawToolsFolded(Vector2 mouse)
    {
        string mode = ViewModes.Name(_main.ViewMode) + (_main.SecondaryView is ViewMode sv ? $" + {ViewModes.Name(sv)}" : "");
        bool special = _main.ViewMode != ViewMode.Normal || _main.SecondaryView != null;
        float w1 = 14 + 18 + Gfx.Width(Fonts.Bold, $"보기 · {mode}", Ui.TextBody) + 22;
        float w2 = 14 + 18 + Gfx.Width(Fonts.Bold, "사고 도구", Ui.TextBody) + 22;
        var card = new Rect2(Margin, Margin + Ui.TopBarH + 8f, w1 + w2 + 20, 40f);
        Card(card);
        var b1 = new Rect2(card.Position.X + 6, card.Position.Y + 6, w1, 28);
        var b2 = new Rect2(b1.End.X + 8, card.Position.Y + 6, w2, 28);
        foreach (var (r, icon, text, active, col) in new[]
                 {
                     (b1, "layers", $"보기 · {mode}", special, Palette.Accent),
                     (b2, "incident", "사고 도구", HazardSystem.RandomDays > 0f, Palette.Danger),
                 })
        {
            bool hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, active ? col.WithAlpha(0.14f) : hover ? Ui.Hover : new Color(0, 0, 0, 0), Ui.RadiusControl, active ? col.WithAlpha(0.4f) : null);
            var c = active ? col : hover ? Palette.Text : Palette.TextDim;
            UiKit.IconText(this, icon, new Vector2(r.Position.X + 8, r.GetCenter().Y), text, Ui.TextBody, c, Fonts.Bold);
            Icons.Draw(this, "chevron-down", new Vector2(r.End.X - 10, r.GetCenter().Y), 12, Palette.TextMuted);
            _buttons.Add((r, () => _toolsOpen = true));
        }
        _hazardMenuAt = new Vector2(Margin, card.End.Y + 6f);
    }

    /// <summary>펼친 도구 막대 오른쪽 끝의 "접기".</summary>
    private void DrawToolsFoldButton(float x, Vector2 mouse)
    {
        var r = new Rect2(x, Margin + Ui.TopBarH + 8f + 6f, 28, 28);
        bool hover = r.HasPoint(mouse);
        Gfx.RoundRect(this, r, hover ? Ui.Hover : Ui.PanelFill, Ui.RadiusControl, Ui.PanelEdge);
        Icons.Draw(this, "chevron-up", r.GetCenter(), 14, hover ? Palette.Text : Palette.TextMuted);
        _cards.Add(r);
        _buttons.Add((r, () => { _toolsOpen = false; _hazardMenu = false; }));
    }

    // ─────────────────────────── ? 도움말 · 상황 힌트 ───────────────────────────

    private static readonly (string title, (string key, string what)[] rows)[] HelpSections =
    {
        ("카메라", new[] { ("휠", "확대 · 축소"), ("우클릭 끌기", "화면 옮기기"), ("H", "배 전체 보기"), ("F", "고른 사람 따라가기"), ("Tab", "다음 사람") }),
        ("시간", new[] { ("Space", "일시정지 · 재개"), ("1–4", "배속"), ("L", "하이라이트(저절로 배속)"), ("U", "요약 진행"), ("R", "사건 직전으로 되감기") }),
        ("보기", new[] { ("V", "보기 모드 (Shift: 겹쳐 보기)"), ("G", "지도"), ("I", "설명서"), ("N", "최근 사건으로"), ("[ ]", "사고 넘기기") }),
        ("사고", new[] { ("Z X M", "운석 소 · 대 · 거대"), ("C", "화재"), ("B", "고장"), ("P", "배관 파열"), ("Esc", "취소 · 닫기") }),
        ("창", new[] { ("K", "인과 사슬"), ("J", "연대기"), ("T", "기술"), ("Y", "관제"), ("E", "방침 · 회의") }),
        ("저장", new[] { ("F5", "저장"), ("F9", "불러오기"), ("O", "설정 (전부 보기)"), ("?", "이 도움말") }),
    };

    private void DrawHelp(Vector2 mouse)
    {
        const float colW = 250f, pad = 22f, rowH = 21f;
        int cols = 3;
        int rowsPer = HelpSections.Max(s => s.rows.Length);
        float secH = 26 + rowsPer * rowH + 14;
        float w = pad * 2 + cols * colW + (cols - 1) * 16, h = 64 + 2 * secH + 20;
        var card = new Rect2((Screen.X - w) * 0.5f, (Screen.Y - h) * 0.5f, w, h);
        Card(card);
        _buttons.Add((card, () => { })); // 창 안을 눌러도 뒤의 단추로 새지 않게 (닫기 단추는 이 뒤에 넣어 먼저 받는다)
        float x = card.Position.X + pad, y = card.Position.Y + pad;
        Icons.Draw(this, "help", new Vector2(x + 10, y + 8), Ui.IconL, Palette.Accent);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 30, y + 15), "도움말 · 단축키", Ui.TextTitle + 1, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 30 + Gfx.Width(Fonts.Bold, "도움말 · 단축키", Ui.TextTitle + 1) + 12, y + 15),
            "평소엔 조용한 화면 — 이상이 생긴 것만 떠오른다 · 설정(O)에서 전부 보기", Ui.TextSmall, Palette.TextMuted);
        var close = new Rect2(card.End.X - pad - 24, y, 24, 24);
        Gfx.RoundRect(this, close, close.HasPoint(mouse) ? Ui.Hover : new Color(0, 0, 0, 0), 6);
        Icons.Draw(this, "close", close.GetCenter(), 16, Palette.TextDim);
        _buttons.Add((close, () => HelpOpen = false));
        y += 44;
        for (int i = 0; i < HelpSections.Length; i++)
        {
            var (title, rows) = HelpSections[i];
            float sx = x + (i % cols) * (colW + 16), sy = y + (i / cols) * secH;
            Gfx.Text(this, Fonts.Bold, new Vector2(sx, sy + 12), title, Ui.TextSmall, Palette.Accent);
            for (int k = 0; k < rows.Length; k++)
            {
                float ry = sy + 26 + k * rowH;
                float kw = Keycap(rows[k].key, new Vector2(sx, ry + 7));
                Gfx.Text(this, Fonts.Body, new Vector2(sx + Mathf.Max(kw, 64) + 10, ry + 11), rows[k].what, Ui.TextBody, Palette.TextDim);
            }
        }
    }

    /// <summary>키 모양 (왼쪽 · 세로 가운데). 너비를 돌려준다.</summary>
    private float Keycap(string key, Vector2 leftCenter)
    {
        float w = Gfx.Width(Fonts.Bold, key, Ui.TextSmall) + 12;
        var r = new Rect2(leftCenter.X, leftCenter.Y - 9, w, 18);
        Gfx.RoundRect(this, r, new Color(1, 1, 1, 0.06f), 4, new Color(1, 1, 1, 0.14f));
        Gfx.TextCentered(this, Fonts.Bold, r.GetCenter(), key, Ui.TextSmall, Palette.Text);
        return w;
    }

    /// <summary>지금 상황에 맞는 힌트 2~3개.</summary>
    private List<(string key, string what)> ContextHints()
    {
        var list = new List<(string, string)>();
        if (_main.Paused) list.Add(("Space", "재개"));
        if (_main.SelectedCrew != null)
        {
            list.Add(("F", _main.Following ? "따라가기 끄기" : "따라가기"));
            list.Add(("Tab", "다음 사람"));
        }
        else if (_main.SelectedRoom != null || _main.SelectedFurniture != null) list.Add(("Esc", "선택 해제"));
        var alert = _world.Alerts.LastOrDefault();
        if (alert != null && alert.Level == AlertLevel.Critical && _world.Tick - alert.Tick < SimTime.Hours(2)) { list.Add(("N", "사건 현장으로")); list.Add(("R", "직전으로 되감기")); }
        if (_world.Causes.Notable().Any(i => i.Open)) list.Add(("K", "인과 사슬"));
        if (Quiet && _main.SelectedCrew == null) list.Add(("클릭", "승무원 · 설비 고르기"));
        list.Add(("?", "도움말"));
        return list.Take(3).ToList();
    }

    /// <summary>오른쪽 아래: ? 단추 + 상황 힌트 (예전 단축키 한 줄 대신).</summary>
    private void DrawHelpCorner(Vector2 mouse)
    {
        var btn = new Rect2(Screen.X - Margin - 30, Screen.Y - Margin - 30, 30, 30);
        bool hover = btn.HasPoint(mouse);
        Gfx.RoundRect(this, btn, HelpOpen ? Palette.Accent.WithAlpha(0.18f) : hover ? new Color(0.09f, 0.11f, 0.15f, 0.97f) : Ui.PanelFill, 15, HelpOpen ? Palette.Accent.WithAlpha(0.5f) : Ui.PanelEdge);
        Icons.Draw(this, "help", btn.GetCenter(), 18, HelpOpen || hover ? Palette.Accent : Palette.TextDim);
        _cards.Add(btn);
        _buttons.Add((btn, ToggleHelp));
        if (HelpOpen) return;
        // 힌트: ? 단추 왼쪽에 한 줄 (오른쪽 칸 너비 안에서, 넘치면 뒤의 것을 버린다)
        var hints = ContextHints().Where(h => h.key != "?").ToList();
        float x = btn.Position.X - 10, cy = btn.GetCenter().Y, minX = Screen.X - Margin - RightColumnWidth;
        foreach (var (key, what) in hints)
        {
            float ww = Gfx.Width(Fonts.Body, what, Ui.TextSmall);
            float kw = Gfx.Width(Fonts.Bold, key, Ui.TextSmall) + 12;
            if (x - ww - 6 - kw < minX) break;
            Gfx.Text(this, Fonts.Body, new Vector2(x - ww, cy + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), what, Ui.TextSmall, Palette.TextMuted);
            x -= ww + 6 + kw;
            Keycap(key, new Vector2(x, cy));
            x -= 12;
        }
    }

    // ─────────────────────────── 일시정지: 테두리 · 채도 ───────────────────────────

    private float _pauseFade;
    private ColorRect? _tint;
    private ShaderMaterial? _tintMat;

    private const string TintShader = @"shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform float amount = 0.0;
void fragment() {
	vec3 c = texture(screen_tex, SCREEN_UV).rgb;
	float g = dot(c, vec3(0.299, 0.587, 0.114));
	COLOR = vec4(mix(c, vec3(g) * 0.9, amount), 1.0);
}";

    /// <summary>배 · 별 위, 화면 UI 아래에 채도를 빼는 막 (일시정지 때만 보인다).</summary>
    private void SetupPauseTint(Node main)
    {
        var layer = new CanvasLayer { Name = "PauseTint", Layer = 6 };
        main.AddChild(layer);
        _tintMat = new ShaderMaterial { Shader = new Shader { Code = TintShader } };
        _tint = new ColorRect { Name = "Tint", Material = _tintMat, MouseFilter = MouseFilterEnum.Ignore, Visible = false, Color = Colors.White };
        _tint.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.AddChild(_tint);
    }

    private void UpdatePauseTint(float delta)
    {
        _pauseFade = Mathf.MoveToward(_pauseFade, _main.Paused && _main.Replaying == null ? 1f : 0f, delta * 5f);
        if (_tint == null || _tintMat == null) return;
        _tint.Visible = _pauseFade > 0.01f;
        if (_tint.Visible) _tintMat.SetShaderParameter("amount", _pauseFade * 0.8f);
    }

    /// <summary>일시정지 테두리: 화면 가장자리에 주황 띠 (안쪽으로 번진다) + 네 귀퉁이 꺾쇠.</summary>
    private void DrawPauseFrame()
    {
        if (_pauseFade <= 0.01f) return;
        var s = Screen;
        var col = Palette.Warning;
        for (int i = 0; i < 5; i++)
        {
            float inset = i * 3f + 1.5f;
            DrawRect(new Rect2(inset, inset, s.X - inset * 2, s.Y - inset * 2), col.WithAlpha(_pauseFade * (0.55f - i * 0.1f)), false, 3f);
        }
        float L = 34f, t = 4f;
        var cc = col.WithAlpha(_pauseFade * 0.95f);
        foreach (var (p, dx, dy) in new[] { (new Vector2(6, 6), 1, 1), (new Vector2(s.X - 6, 6), -1, 1), (new Vector2(6, s.Y - 6), 1, -1), (new Vector2(s.X - 6, s.Y - 6), -1, -1) })
        {
            DrawLine(p, p + new Vector2(L * dx, 0), cc, t);
            DrawLine(p, p + new Vector2(0, L * dy), cc, t);
        }
    }
}
