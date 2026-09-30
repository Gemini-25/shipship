using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v12.2 사고 카드 · 인과 사슬 (K).
/// 화면 아래 빈 곳에 진행 중인 사고 카드 — 누르면 원인 → 결과 나무가 열리고, 배 도면 위에 화살표가 겹친다.
/// </summary>
public partial class Hud
{
    public CauseIncident? ChainIncident { get; private set; }
    public bool ChainOpen => ChainIncident != null;
    private int _chainScroll;
    private Rect2 _chainRect;
    private float _minimapRight;

    public void OpenChain(CauseIncident? inc)
    {
        ChainIncident = inc;
        _chainScroll = 0;
        if (inc != null) { ChronicleOpen = false; TechOpen = false; }
    }

    /// <summary>K: 가장 최근의 큰 사고 사슬을 연다 / 닫는다.</summary>
    public void ToggleChain()
    {
        if (ChainOpen) { OpenChain(null); return; }
        var inc = _world.Causes.Notable().FirstOrDefault();
        if (inc == null) { _main.ShowNotice("아직 번진 사고가 없다"); return; }
        OpenChain(inc);
        FocusNode(_world.Causes.Node(inc.Root));
    }

    public static Color KindColor(CauseKind k) => k switch
    {
        CauseKind.Impact => new Color("#c9a27a"),
        CauseKind.Explosion => new Color("#ff7a3d"),
        CauseKind.Fire => new Color("#ff5a3c"),
        CauseKind.Breach => new Color("#9fb3ff"),
        CauseKind.Suffocation => new Color("#7fb2ff"),
        CauseKind.Gas => new Color("#b7d15a"),
        CauseKind.Cut => new Color("#e8b84a"),
        CauseKind.Outage => new Color("#f0d060"),
        CauseKind.NoWater => new Color("#4f9fdc"),
        CauseKind.NoAir => new Color("#8fcfc4"),
        CauseKind.Fault => new Color("#d69a5a"),
        CauseKind.Stop => new Color("#c98a4a"),
        CauseKind.Scram => new Color("#ff3d6e"),
        CauseKind.Casualty => new Color("#ff8fa0"),
        CauseKind.Death => new Color("#ffffff"),
        CauseKind.Detach => new Color("#b0b8c8"),
        CauseKind.Recovery => new Color("#6fd08c"),
        _ => new Color("#c0c6d0"),
    };

    public static string KindLabel(CauseKind k) => k switch
    {
        CauseKind.Impact => "충돌", CauseKind.Explosion => "폭발", CauseKind.Fire => "화재", CauseKind.Breach => "감압",
        CauseKind.Suffocation => "산소", CauseKind.Gas => "가스", CauseKind.Cut => "끊김", CauseKind.Outage => "정전",
        CauseKind.NoWater => "단수", CauseKind.NoAir => "환기", CauseKind.Fault => "고장", CauseKind.Stop => "멈춤",
        CauseKind.Scram => "원자로", CauseKind.Casualty => "쓰러짐", CauseKind.Death => "사망", CauseKind.Detach => "분리",
        CauseKind.Recovery => "복구", _ => "사고",
    };

    private static string Dur(float h) => h < 1f ? $"{Mathf.Max(1f, h * 60f):0}분" : h < 48f ? $"{h:0.#}시간" : $"{h / 24f:0.#}일";

    // ─────────────────────────────── 사고 카드 ───────────────────────────────

    private void DrawIncidentCards(Vector2 mouse)
    {
        if (ChronicleOpen || TechOpen) return;
        var log = _world.Causes;
        var list = log.Notable().Where(i => i.Open || _world.Tick - i.End < SimTime.Hours(3)).Take(3).ToList();
        if (list.Count == 0) return;
        float x0 = (MinimapOpen ? _minimapRight : Margin + 470f) + 10f;
        float right = Screen.X - RightColumnWidth - Margin * 2;
        float w = Mathf.Min(420f, right - x0);
        if (w < 220f) return;
        const float h = 54f, gap = 6f;
        float y = Screen.Y - Margin - LogHeight;
        foreach (var inc in list)
        {
            var rect = new Rect2(x0, y, w, h);
            bool hover = rect.HasPoint(mouse);
            bool sel = ChainIncident == inc;
            var root = log.Node(inc.Root);
            int lasting = inc.Nodes.Count(i => log.Node(i).Lasting);
            int open = inc.Nodes.Count(i => log.Node(i).Open);
            var sev = inc.Deaths > 0 ? Palette.Danger : inc.Open ? (inc.Casualties > 0 || open > 3 ? Palette.Danger : Palette.Warning) : Palette.Good;
            Gfx.RoundRect(this, rect, sel ? new Color(0.10f, 0.12f, 0.16f, 0.97f) : Palette.Panel, 10, sel ? Palette.Accent.WithAlpha(0.6f) : hover ? Palette.Text.WithAlpha(0.3f) : Palette.PanelBorder);
            _cards.Add(rect);
            // 왼쪽 띠: 진행 중이면 맥박
            float pulse = inc.Open ? 0.65f + 0.35f * Mathf.Sin(_time * 4f) : 1f;
            DrawRect(new Rect2(rect.Position + new Vector2(0, 8), new Vector2(3, h - 16)), sev.WithAlpha(pulse));
            float x = rect.Position.X + 14;
            string title = root.Text;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, rect.Position.Y + 19), Fit(title, w - 120, 13, Fonts.Bold), 13, Palette.Text);
            string status = inc.Open ? $"진행 중 · {Dur((_world.Tick - inc.Start) / (float)SimTime.TicksPerHour)}" : $"수습 · {Dur((inc.End - inc.Start) / (float)SimTime.TicksPerHour)}";
            Gfx.TextRight(this, Fonts.Bold, new Vector2(rect.End.X - 12, rect.Position.Y + 19), status, 11, sev);
            // 둘째 줄: 번진 것 · 사람 · 복구 막대
            string meta = $"번진 것 {inc.Nodes.Count(i => log.Node(i).Kind != CauseKind.Recovery) - 1}" + (inc.Casualties > 0 ? $" · 쓰러짐 {inc.Casualties}" : "") + (inc.Deaths > 0 ? $" · 사망 {inc.Deaths}" : "");
            Gfx.Text(this, Fonts.Body, new Vector2(x, rect.Position.Y + 36), meta, 11, Palette.TextDim);
            float done = lasting > 0 ? (lasting - open) / (float)lasting : 1f;
            var bar = new Rect2(rect.End.X - 112, rect.Position.Y + 30, 100, 5);
            Gfx.Bar(this, bar, done, Palette.Good);
            Gfx.TextRight(this, Fonts.Body, new Vector2(rect.End.X - 12, rect.Position.Y + 47), $"복구 {lasting - open}/{lasting}", 10, Palette.TextMuted);
            // 셋째 줄: 가장 최근에 일어난 일
            var last = log.Node(inc.Nodes[^1]);
            Gfx.Text(this, Fonts.Body, new Vector2(x, rect.Position.Y + 49), Fit($"{SimTime.Clock(last.Tick)} {last.Text}", w - 140, 10, Fonts.Body), 10, (last.Kind == CauseKind.Recovery ? Palette.Good : KindColor(last.Kind)).WithAlpha(0.85f));
            var captured = inc;
            _buttons.Add((rect, () => { OpenChain(ChainIncident == captured ? null : captured); if (ChainIncident != null) FocusNode(log.Node(captured.Root)); }));
            y += h + gap;
        }
    }

    private static string Fit(string text, float width, int size, Font font)
    {
        if (Gfx.Width(font, text, size) <= width) return text;
        while (text.Length > 2 && Gfx.Width(font, text + "…", size) > width) text = text[..^1];
        return text + "…";
    }

    /// <summary>그 점이 사슬 패널 오른쪽 빈 곳 가운데에 오게 카메라를 옮긴다.</summary>
    private void FocusAt(System.Numerics.Vector2 at)
    {
        float panelRight = ChainOpen ? Margin + Mathf.Min(640f, Screen.X - RightColumnWidth - Margin * 3) : 0f;
        float regionCenter = (panelRight + Screen.X - RightColumnWidth - Margin) * 0.5f;
        float delta = regionCenter - Screen.X * 0.5f;
        _main.Camera.Position = ShipView.ToPx(at) - new Vector2(delta, 0f) / _main.Camera.Zoom.X;
    }

    private void FocusNode(CauseNode n)
    {
        if (n.At is System.Numerics.Vector2 at) { FocusAt(at); return; }
        // 자리가 없는 뿌리(시나리오 등): 번진 곳들의 한가운데
        if (ChainIncident is CauseIncident inc)
        {
            var pts = inc.Nodes.Select(i => _world.Causes.Node(i).At).OfType<System.Numerics.Vector2>().ToList();
            if (pts.Count > 0) FocusAt(pts.Aggregate(System.Numerics.Vector2.Zero, (a, b) => a + b) / pts.Count);
        }
    }

    // ─────────────────────────────── 사슬 (나무) ───────────────────────────────

    private void DrawChain(Vector2 mouse)
    {
        var inc = ChainIncident!;
        var log = _world.Causes;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f + 64f + 10f;
        float w = Mathf.Min(640f, Screen.X - RightColumnWidth - Margin * 3);
        float height = Screen.Y - y0 - LogHeight - Margin - 10f;
        var card = new Rect2(x0, y0, w, height);
        _chainRect = card;
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;
        var root = log.Node(inc.Root);

        // 머리: 뿌리 · 상태 · 숫자
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y0 + 30), Fit(root.Text, right - x - 150, 17, Fonts.Bold), 17, Palette.Text);
        Button(new Rect2(right - 58, y0 + 12, 58, 26), "K 닫기", false, mouse, () => OpenChain(null), 11);
        int lasting = inc.Nodes.Count(i => log.Node(i).Lasting);
        int open = inc.Nodes.Count(i => log.Node(i).Open);
        string when = $"{SimTime.Day(inc.Start)}일 {SimTime.Clock(inc.Start)}" + (inc.ByObserver ? " · 관찰자가 일으킴" : "");
        string state = inc.Open ? $"진행 중 {Dur((_world.Tick - inc.Start) / (float)SimTime.TicksPerHour)}" : $"{Dur((inc.End - inc.Start) / (float)SimTime.TicksPerHour)} 만에 수습";
        Gfx.Text(this, Fonts.Body, new Vector2(x, y0 + 50), $"{when} · {state} · 번진 것 {inc.Nodes.Count(i => log.Node(i).Kind != CauseKind.Recovery) - 1}" +
            (inc.Casualties > 0 ? $" · 쓰러짐 {inc.Casualties}" : "") + (inc.Deaths > 0 ? $" · 사망 {inc.Deaths}" : ""), 12, Palette.TextDim);
        var bar = new Rect2(x, y0 + 60, right - x, 6);
        Gfx.Bar(this, bar, lasting > 0 ? (lasting - open) / (float)lasting : 1f, Palette.Good);
        Gfx.TextRight(this, Fonts.Body, new Vector2(right, y0 + 80), $"되돌린 것 {lasting - open}/{lasting} · 아직 {open}", 11, open > 0 ? Palette.Warning : Palette.Good);
        // 종류 범례 (이 사고에 나온 것만)
        float lx = x;
        foreach (var k in inc.Nodes.Select(i => log.Node(i).Kind).Distinct().OrderBy(k => (int)k))
        {
            string lab = KindLabel(k);
            DrawCircle(new Vector2(lx + 4, y0 + 76), 3.5f, KindColor(k), true, -1f, true);
            Gfx.Text(this, Fonts.Body, new Vector2(lx + 11, y0 + 80), lab, 10, Palette.TextMuted);
            lx += 16 + Gfx.Width(Fonts.Body, lab, 10);
            if (lx > right - 170) break;
        }
        Divider(x, right, y0 + 90);

        // 나무를 줄로 편다 (깊이 우선)
        var rows = new List<CauseNode>();
        var stack = new Stack<int>();
        stack.Push(inc.Root);
        while (stack.Count > 0)
        {
            var n = log.Node(stack.Pop());
            rows.Add(n);
            for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
        const float rowH = 22f;
        float top = y0 + 98;
        int fit = Math.Max(1, (int)((card.End.Y - top - 22) / rowH));
        _chainScroll = Math.Clamp(_chainScroll, 0, Math.Max(0, rows.Count - fit));
        var yOf = new Dictionary<int, float>();
        for (int i = 0; i < rows.Count; i++) yOf[rows[i].Id] = top + (i - _chainScroll) * rowH + rowH * 0.5f;
        for (int i = _chainScroll; i < Math.Min(rows.Count, _chainScroll + fit); i++)
        {
            var n = rows[i];
            float cy = yOf[n.Id];
            float ix = x + 8 + n.Depth * 16;
            var col = KindColor(n.Kind);
            var rowRect = new Rect2(x, cy - rowH * 0.5f, right - x, rowH);
            bool hover = rowRect.HasPoint(mouse);
            if (hover) Gfx.RoundRect(this, rowRect, new Color(1, 1, 1, 0.05f), 5);
            // 가지: 부모 점에서 내려와 이 점으로
            if (n.Parent >= 0 && yOf.TryGetValue(n.Parent, out var py))
            {
                float px = x + 8 + (n.Depth - 1) * 16;
                float fromY = Mathf.Max(py + 5, top - 4);
                DrawLine(new Vector2(px, fromY), new Vector2(px, cy), Palette.PanelBorder.Lightened(0.2f), 1.2f);
                DrawLine(new Vector2(px, cy), new Vector2(ix - 5, cy), Palette.PanelBorder.Lightened(0.2f), 1.2f);
            }
            bool isOpen = n.Open;
            float r = n.Depth == 0 ? 5.5f : 4f;
            if (isOpen) DrawCircle(new Vector2(ix, cy), r + 3f + 1.5f * Mathf.Sin(_time * 5f + n.Id), col.WithAlpha(0.18f), true, -1f, true);
            if (n.Kind == CauseKind.Recovery)
            {
                DrawArc(new Vector2(ix, cy), 4.5f, 0.6f, Mathf.Tau - 0.3f, 12, col, 1.6f, true);
                DrawLine(new Vector2(ix + 3.4f, cy - 3.8f), new Vector2(ix + 5.6f, cy - 1.2f), col, 1.6f, true);
            }
            else if (n.Lasting && !isOpen) DrawArc(new Vector2(ix, cy), r, 0, Mathf.Tau, 14, col, 1.5f, true);
            else DrawCircle(new Vector2(ix, cy), r, col, true, -1f, true);
            float tx = ix + 12;
            Gfx.Text(this, Fonts.Body, new Vector2(tx, cy + 4), SimTime.Clock(n.Tick), 10, Palette.TextMuted);
            tx += 36;
            if (n.Kind != CauseKind.Recovery)
            {
                string lab = KindLabel(n.Kind);
                float lw = Gfx.Width(Fonts.Bold, lab, 9) + 8;
                Gfx.RoundRect(this, new Rect2(tx, cy - 7, lw, 14), col.WithAlpha(0.16f), 4);
                Gfx.Text(this, Fonts.Bold, new Vector2(tx + 4, cy + 4), lab, 9, col);
                tx += lw + 6;
            }
            // 오른쪽: 상태
            string chip = n.Kind == CauseKind.Recovery ? "" : isOpen ? $"진행 중 {Dur(n.Hours(_world.Tick))}" : n.Lasting ? $"↺ {SimTime.Clock(n.ResolvedAt)}" : "";
            float chipW = chip.Length > 0 ? Gfx.Width(Fonts.Bold, chip, 10) : 0f;
            if (chip.Length > 0) Gfx.TextRight(this, Fonts.Bold, new Vector2(right - 4, cy + 4), chip, 10, isOpen ? Palette.Warning : Palette.Good.WithAlpha(0.8f));
            string text = n.Text + (n.Repeats > 0 ? $" ×{n.Repeats + 1}" : "");
            var tcol = n.Kind == CauseKind.Recovery ? Palette.Good : n.Kind == CauseKind.Death ? Palette.Danger : isOpen ? Palette.Text : Palette.TextDim;
            Gfx.Text(this, n.Depth == 0 ? Fonts.Bold : Fonts.Body, new Vector2(tx, cy + 4), Fit(text, right - tx - chipW - 14, 12, Fonts.Body), 12, tcol);
            var captured = n;
            _buttons.Add((rowRect, () => FocusNode(captured)));
        }
        if (rows.Count > fit)
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, card.End.Y - 8), $"{_chainScroll + 1}–{Math.Min(rows.Count, _chainScroll + fit)} / {rows.Count} · 휠로 넘기기 · 줄을 누르면 그곳으로", 10, Palette.TextMuted);
        else
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, card.End.Y - 8), "줄을 누르면 그곳으로 · 도면 위 화살표가 번진 길", 10, Palette.TextMuted);
    }

    private bool ScrollChain(InputEventMouseButton mb)
    {
        if (!ChainOpen || !_chainRect.HasPoint(mb.Position)) return false;
        if (mb.ButtonIndex == MouseButton.WheelUp) _chainScroll = Math.Max(0, _chainScroll - 3);
        else if (mb.ButtonIndex == MouseButton.WheelDown) _chainScroll += 3;
        else return false;
        return true;
    }
}
