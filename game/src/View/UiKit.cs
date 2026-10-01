using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>툴팁 한 줄 (아이콘이 있으면 앞에).</summary>
public readonly record struct TipLine(string Text, Color Color, string? Icon = null);

/// <summary>
/// v16.2 공통 부품: 패널 · 머리글 · 줄 · 게이지 · 아이콘 · 배지 · 툴팁 · 작은 그래프 · 글 줄이기.
/// 모두 CanvasItem 위에 직접 그린다 (Gfx 위의 한 층). 크기 · 색은 Ui 표준에서.
/// </summary>
public static class UiKit
{
    // ─────────────────────────── 면 ───────────────────────────

    /// <summary>유리 패널. accent를 주면 왼쪽에 그 뜻의 띠.</summary>
    public static void Panel(CanvasItem ci, Rect2 r, Tone? accent = null, float radius = Ui.RadiusCard)
    {
        Gfx.RoundRect(ci, r, Ui.PanelFill, radius, accent is Tone t && Ui.IsAlarm(t) ? Ui.Of(t).WithAlpha(0.35f) : Ui.PanelEdge);
        if (accent is Tone a && a != Tone.Normal)
            Gfx.RoundRect(ci, new Rect2(r.Position.X + 4, r.Position.Y + 10, 3, Mathf.Max(4f, r.Size.Y - 20)), Ui.Of(a).WithAlpha(0.85f), 1.5f);
    }

    public static void Divider(CanvasItem ci, float x0, float x1, float y) =>
        ci.DrawLine(new Vector2(x0, y), new Vector2(x1, y), Ui.PanelEdge, 1f);

    /// <summary>머리글: (아이콘) 제목 · 오른쪽 부제. y는 글자 기준선.</summary>
    public static void Header(CanvasItem ci, float x, float right, float y, string title, string? sub = null, string? icon = null, Color? color = null)
    {
        var col = color ?? Palette.TextMuted;
        if (icon != null)
        {
            Icons.Draw(ci, icon, new Vector2(x + 6, y - 4), Ui.IconS, col);
            x += 18;
        }
        Gfx.Text(ci, Fonts.Bold, new Vector2(x, y), title, Ui.TextSmall, col);
        if (sub != null)
            Gfx.TextRight(ci, Fonts.Body, new Vector2(right, y), Fit(sub, right - x - Gfx.Width(Fonts.Bold, title, Ui.TextSmall) - 12, Ui.TextSmall), Ui.TextSmall, Palette.TextMuted);
    }

    // ─────────────────────────── 줄 · 게이지 ───────────────────────────

    /// <summary>라벨 · 게이지 · 값 한 줄 (승무원 · 방 상세의 수치 줄).</summary>
    public static void Row(CanvasItem ci, float x, float right, float y, string label, float value, Color color, string valueText, bool alarm = false)
    {
        Gfx.Text(ci, Fonts.Body, new Vector2(x, y + 14), label, Ui.TextBody, alarm ? Palette.Warning : Palette.TextDim);
        Gauge(ci, new Rect2(x + 70, y + 7, right - x - 70 - 46, Ui.GaugeH), value, alarm ? Palette.Warning : color);
        Gfx.TextRight(ci, Fonts.Body, new Vector2(right, y + 14), valueText, Ui.TextBody, Palette.Text);
    }

    /// <summary>게이지. mark(0~1)를 주면 그 자리에 문턱 눈금.</summary>
    public static void Gauge(CanvasItem ci, Rect2 r, float value, Color fill, float? mark = null)
    {
        Gfx.Bar(ci, r, value, fill, Ui.Track);
        if (mark is float m)
        {
            float mx = r.Position.X + r.Size.X * Mathf.Clamp(m, 0f, 1f);
            ci.DrawLine(new Vector2(mx, r.Position.Y - 2), new Vector2(mx, r.End.Y + 2), Palette.Warning.WithAlpha(0.7f), 1f);
        }
    }

    // ─────────────────────────── 아이콘 · 배지 ───────────────────────────

    public static void Icon(CanvasItem ci, string name, Vector2 center, float size, Color color) => Icons.Draw(ci, name, center, size, color);

    /// <summary>아이콘 + 글 (왼쪽 · 세로 가운데 기준). 쓴 너비를 돌려준다.</summary>
    public static float IconText(CanvasItem ci, string icon, Vector2 leftCenter, string text, int size, Color color, Font? font = null, Color? iconColor = null)
    {
        font ??= Fonts.Body;
        float isz = size + 3;
        Icons.Draw(ci, icon, leftCenter + new Vector2(isz * 0.5f, 0), isz, iconColor ?? color);
        float tx = leftCenter.X + isz + 4;
        Gfx.Text(ci, font, new Vector2(tx, leftCenter.Y + Gfx.CenterOffset(font, size)), text, size, color);
        return isz + 4 + Gfx.Width(font, text, size);
    }

    /// <summary>뜻 색 배지 (왼쪽 · 세로 가운데 기준). 그린 사각형을 돌려준다.</summary>
    public static Rect2 Badge(CanvasItem ci, Vector2 leftCenter, string text, Tone tone, string? icon = null, int size = Ui.TextTiny)
    {
        var col = Ui.Of(tone);
        float iw = icon != null ? size + 4 : 0f;
        float w = Gfx.Width(Fonts.Body, text, size) + 12 + iw;
        var r = new Rect2(leftCenter.X, leftCenter.Y - 8, w, 16);
        Gfx.RoundRect(ci, r, col.WithAlpha(0.14f), 4, col.WithAlpha(0.5f), 1);
        if (icon != null) Icons.Draw(ci, icon, new Vector2(r.Position.X + 6 + size * 0.5f, leftCenter.Y), size + 1, col);
        Gfx.Text(ci, Fonts.Body, new Vector2(r.Position.X + 6 + iw, leftCenter.Y + Gfx.CenterOffset(Fonts.Body, size)), text, size, col);
        return r;
    }

    /// <summary>화면 위 가운데 알림 띠: 뜻 색 테두리 · 아이콘 · 글 (pulse로 맥박). 그린 사각형을 돌려준다.</summary>
    public static Rect2 Banner(CanvasItem ci, Vector2 center, string text, Tone tone, float pulse = 1f, string? icon = null)
    {
        var col = Ui.Of(tone);
        var bg = new Color(0.03f + col.R * 0.05f, 0.04f + col.G * 0.03f, 0.06f + col.B * 0.03f, 0.92f);
        float iw = icon != null ? Ui.IconM + 6f : 0f;
        float w = Gfx.Width(Fonts.Bold, text, Ui.TextLabel) + 28f + iw, h = 28f;
        var r = new Rect2(center.X - w * 0.5f, center.Y - h * 0.5f, w, h);
        Gfx.RoundRect(ci, r, bg, h * 0.5f, col.WithAlpha(0.45f));
        float x = r.Position.X + 14f;
        if (icon != null)
        {
            Icons.Draw(ci, icon, new Vector2(x + Ui.IconM * 0.5f, center.Y), Ui.IconM, col.WithAlpha(pulse));
            x += iw;
        }
        Gfx.Text(ci, Fonts.Bold, new Vector2(x, center.Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextLabel)), text, Ui.TextLabel, col.WithAlpha(pulse));
        return r;
    }

    /// <summary>추세 화살표 아이콘 이름.</summary>
    public static string TrendIcon(TrendDir d) => d switch { TrendDir.Up => "trend-up", TrendDir.Down => "trend-down", _ => "trend-flat" };

    // ─────────────────────────── 툴팁 · 그래프 ───────────────────────────

    /// <summary>
    /// 툴팁: 제목 · 줄들 · (작은 그래프). anchor 아래에 띄우되 화면을 벗어나지 않게. 그린 사각형을 돌려준다.
    /// </summary>
    public static Rect2 Tooltip(CanvasItem ci, Vector2 anchor, Vector2 screen, string title, IReadOnlyList<TipLine> lines,
        IReadOnlyList<Sample>? graph = null, float? threshold = null, Color? graphColor = null, string? icon = null)
    {
        const float pad = 12f, lineH = 17f, graphH = 46f;
        float w = Gfx.Width(Fonts.Bold, title, Ui.TextLabel) + (icon != null ? 22 : 0);
        foreach (var l in lines) w = Mathf.Max(w, Gfx.Width(Fonts.Body, l.Text, Ui.TextSmall) + (l.Icon != null ? 16 : 0));
        w = Mathf.Clamp(w + pad * 2, 200f, 340f);
        bool hasGraph = graph != null && graph.Count >= 2;
        float h = pad + 18 + lines.Count * lineH + (hasGraph ? graphH + 10 : 0) + pad - 4;
        var pos = new Vector2(Mathf.Clamp(anchor.X - w * 0.5f, 8f, screen.X - w - 8f), anchor.Y + 8f);
        if (pos.Y + h > screen.Y - 8f) pos.Y = anchor.Y - h - 8f;
        var r = new Rect2(pos, new Vector2(w, h));
        Gfx.RoundRect(ci, r, Ui.TooltipFill, Ui.RadiusControl, Ui.PanelEdge.Lightened(0.15f));
        float x = r.Position.X + pad, y = r.Position.Y + pad + 11;
        if (icon != null)
        {
            Icons.Draw(ci, icon, new Vector2(x + 7, y - 4), Ui.IconM, graphColor ?? Palette.Text);
            Gfx.Text(ci, Fonts.Bold, new Vector2(x + 22, y), title, Ui.TextLabel, Palette.Text);
        }
        else Gfx.Text(ci, Fonts.Bold, new Vector2(x, y), title, Ui.TextLabel, Palette.Text);
        y += 7;
        if (hasGraph)
        {
            Sparkline(ci, new Rect2(x, y + 2, w - pad * 2, graphH), graph!, graphColor ?? Palette.Accent, threshold);
            y += graphH + 10;
        }
        foreach (var l in lines)
        {
            y += lineH;
            float lx = x;
            if (l.Icon != null) { Icons.Draw(ci, l.Icon, new Vector2(x + 6, y - 7), 12, l.Color); lx += 16; }
            Gfx.Text(ci, Fonts.Body, new Vector2(lx, y - 3), Fit(l.Text, w - pad * 2 - (lx - x), Ui.TextSmall), Ui.TextSmall, l.Color);
        }
        return r;
    }

    /// <summary>작은 선 그래프 (+ 문턱 점선 · 마지막 점).</summary>
    public static void Sparkline(CanvasItem ci, Rect2 r, IReadOnlyList<Sample> s, Color color, float? threshold = null)
    {
        Gfx.RoundRect(ci, r, new Color(1, 1, 1, 0.03f), 4);
        if (s.Count < 2) return;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var p in s) { lo = Mathf.Min(lo, p.Value); hi = Mathf.Max(hi, p.Value); }
        if (threshold is float th) { lo = Mathf.Min(lo, th); hi = Mathf.Max(hi, th); }
        if (hi - lo < 1e-4f) { hi += 0.5f; lo -= 0.5f; }
        float pad = (hi - lo) * 0.1f;
        lo -= pad; hi += pad;
        float t0 = s[0].Hour, t1 = Mathf.Max(t0 + 1e-3f, s[^1].Hour);
        Vector2 P(Sample p) => new(r.Position.X + (p.Hour - t0) / (t1 - t0) * r.Size.X, r.End.Y - (p.Value - lo) / (hi - lo) * r.Size.Y);
        if (threshold is float tv)
        {
            float ty = r.End.Y - (tv - lo) / (hi - lo) * r.Size.Y;
            for (float x = r.Position.X; x < r.End.X; x += 6f)
                ci.DrawLine(new Vector2(x, ty), new Vector2(Mathf.Min(x + 3f, r.End.X), ty), Palette.Danger.WithAlpha(0.6f), 1f);
        }
        var pts = new Vector2[s.Count];
        for (int i = 0; i < s.Count; i++) pts[i] = P(s[i]);
        ci.DrawPolyline(pts, color, 1.5f, true);
        ci.DrawCircle(pts[^1], 2.5f, color, true, -1f, true);
    }

    // ─────────────────────────── 글 ───────────────────────────

    /// <summary>너비를 넘으면 끝을 줄이고 "…".</summary>
    public static string Fit(string text, float width, int size, Font? font = null)
    {
        font ??= Fonts.Body;
        if (width <= 8f) return "";
        if (Gfx.Width(font, text, size) <= width) return text;
        while (text.Length > 2 && Gfx.Width(font, text + "…", size) > width) text = text[..^1];
        return text.TrimEnd() + "…";
    }

    /// <summary>너비에 맞춰 낱말 단위로 접는다 (낱말이 너무 길면 글자 단위로). maxLines를 넘으면 마지막 줄 끝에 "…".</summary>
    public static List<string> Wrap(string text, float width, int size, Font? font = null, int maxLines = 99)
    {
        font ??= Fonts.Body;
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (Gfx.Width(font, next, size) <= width) { line = next; continue; }
            if (line.Length > 0) lines.Add(line);
            line = word;
            while (Gfx.Width(font, line, size) > width && line.Length > 2)
            {
                int cut = line.Length - 1;
                while (cut > 1 && Gfx.Width(font, line[..cut], size) > width) cut--;
                lines.Add(line[..cut]);
                line = line[cut..];
            }
        }
        if (line.Length > 0) lines.Add(line);
        if (lines.Count > maxLines)
        {
            lines = lines.GetRange(0, maxLines);
            lines[^1] = lines[^1].TrimEnd() + "…";
        }
        return lines;
    }
}
