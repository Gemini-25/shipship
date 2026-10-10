using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.24 공통 부품 2: 카드 · 숫자 칩 · 고르개 칩 · 칸(타일) · 인용(일기) · 단계 줄(과정) · 규모 색 · 현장 그림 틀.
/// 어두운 유리 톤 그대로 — 글보다 아이콘 · 숫자 · 색으로 읽히게, 한 칸에 한 가지 뜻.
/// </summary>
public static partial class UiKit
{
    // ─────────────────────────── 색 ───────────────────────────

    /// <summary>사고 규모 다섯 단계 색 (개인 · 방 · 계통 · 배 전체 · 우주급). 어디서나 같은 색.</summary>
    public static Color ScaleColor(IncidentScale s) => new(ScaleTable.Hex(s));

    /// <summary>카드 안의 한 겹 더 어두운 면 (칸 · 타일 바탕).</summary>
    public static readonly Color Well = new(0.02f, 0.03f, 0.045f, 0.55f);

    // ─────────────────────────── 카드 머리 ───────────────────────────

    /// <summary>카드 제목 줄: 큰 아이콘 · 제목 · 작은 부제(오른쪽 끝을 넘지 않게). 제목 기준선 y.</summary>
    public static void CardTitle(CanvasItem ci, float x, float right, float y, string title, string? sub = null, string? icon = null, Color? color = null)
    {
        var col = color ?? Palette.Text;
        if (icon != null)
        {
            Icons.Draw(ci, icon, new Vector2(x + Ui.IconL * 0.5f, y - 6f), Ui.IconL, col);
            x += Ui.IconL + Ui.S2;
        }
        Gfx.Text(ci, Fonts.Bold, new Vector2(x, y), Fit(title, right - x, Ui.TextHeading, Fonts.Bold), Ui.TextHeading, col);
        if (sub != null)
        {
            float tx = x + Gfx.Width(Fonts.Bold, title, Ui.TextHeading) + Ui.S3;
            if (right - tx > 40f) Gfx.Text(ci, Fonts.Body, new Vector2(tx, y), Fit(sub, right - tx, Ui.TextBody), Ui.TextBody, Palette.TextMuted);
        }
    }

    // ─────────────────────────── 숫자 칩 ───────────────────────────

    /// <summary>아이콘 · 큰 숫자 · 작은 이름 (왼쪽 · 세로 가운데). 쓴 너비를 돌려준다.</summary>
    public static float Stat(CanvasItem ci, Vector2 leftCenter, string icon, string value, string label, Tone tone = Tone.Normal)
    {
        var col = tone == Tone.Normal ? Palette.Text : Ui.Of(tone);
        Icons.Draw(ci, icon, leftCenter + new Vector2(Ui.IconS * 0.5f, 0f), Ui.IconS, tone == Tone.Normal ? Palette.TextDim : col);
        float x = leftCenter.X + Ui.IconS + Ui.S1;
        Gfx.Text(ci, Fonts.Bold, new Vector2(x, leftCenter.Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextLabel)), value, Ui.TextLabel, col);
        x += Gfx.Width(Fonts.Bold, value, Ui.TextLabel) + 3f;
        Gfx.Text(ci, Fonts.Body, new Vector2(x, leftCenter.Y + Gfx.CenterOffset(Fonts.Body, Ui.TextTiny)), label, Ui.TextTiny, Palette.TextMuted);
        return x + Gfx.Width(Fonts.Body, label, Ui.TextTiny) - leftCenter.X;
    }

    /// <summary>숫자 칩 한 줄 (0인 것은 빼고): 칩 사이 간격을 두고 오른쪽 끝에서 멈춘다. 쓴 너비를 돌려준다.</summary>
    public static float Stats(CanvasItem ci, Vector2 leftCenter, float right, IEnumerable<(string icon, int value, string label, Tone tone)> stats)
    {
        float x = leftCenter.X;
        foreach (var (icon, value, label, tone) in stats)
        {
            if (value == 0) continue;
            float need = Ui.IconS + Gfx.Width(Fonts.Bold, value.ToString(), Ui.TextLabel) + Gfx.Width(Fonts.Body, label, Ui.TextTiny) + 10f;
            if (x + need > right) break;
            x += Stat(ci, new Vector2(x, leftCenter.Y), icon, value.ToString(), label, tone) + Ui.S3;
        }
        return x - leftCenter.X;
    }

    // ─────────────────────────── 칩 ───────────────────────────

    /// <summary>고르개 칩 (켜짐 · 마우스 올림). 너비를 글에 맞춘다. 그린 사각형을 돌려준다 — 누름은 부르는 쪽이 단다.</summary>
    public static Rect2 Chip(CanvasItem ci, Vector2 topLeft, string text, bool active, bool hover, string? icon = null, Color? accent = null)
    {
        var col = accent ?? Palette.Accent;
        float iw = icon != null ? Ui.IconS + Ui.S1 : 0f;
        float w = Gfx.Width(Fonts.Bold, text, Ui.TextSmall) + iw + Ui.S3 * 2;
        var r = new Rect2(topLeft, new Vector2(w, Ui.ChipH + 4f));
        Gfx.RoundRect(ci, r, active ? col.WithAlpha(0.18f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.02f), Ui.RadiusChip,
            active ? col.WithAlpha(0.55f) : Ui.PanelEdge);
        var tc = active ? col : hover ? Palette.Text : Palette.TextDim;
        float x = r.Position.X + Ui.S3;
        if (icon != null) { Icons.Draw(ci, icon, new Vector2(x + Ui.IconS * 0.5f, r.GetCenter().Y), Ui.IconS, tc); x += iw; }
        Gfx.Text(ci, Fonts.Bold, new Vector2(x, r.GetCenter().Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), text, Ui.TextSmall, tc);
        return r;
    }

    /// <summary>칩 너비 (자리 재기).</summary>
    public static float ChipWidth(string text, string? icon = null) =>
        Gfx.Width(Fonts.Bold, text, Ui.TextSmall) + (icon != null ? Ui.IconS + Ui.S1 : 0f) + Ui.S3 * 2;

    // ─────────────────────────── 칸 (타일) ───────────────────────────

    /// <summary>
    /// 칸 하나: 위에 아이콘 · 이름, 아래에 줄들(점 · 최대 maxLines). found=false면 흐리게 (기록이 없어 "없었다"만 적힌 칸).
    /// 쓴 높이를 돌려준다 (rect 높이를 넘지 않는다).
    /// </summary>
    public static void Tile(CanvasItem ci, Rect2 r, string icon, string title, IReadOnlyList<string> lines, Color accent, bool found = true, int maxLines = 3)
    {
        Gfx.RoundRect(ci, r, Well, Ui.RadiusControl, found ? accent.WithAlpha(0.28f) : Ui.PanelEdge);
        float x = r.Position.X + Ui.S3, right = r.End.X - Ui.S3, y = r.Position.Y + Ui.S3 + 8f;
        Icons.Draw(ci, icon, new Vector2(x + Ui.IconS * 0.5f, y - 4f), Ui.IconS, found ? accent : Palette.TextMuted);
        Gfx.Text(ci, Fonts.Bold, new Vector2(x + Ui.IconS + Ui.S2, y), title, Ui.TextSmall, found ? accent : Palette.TextMuted);
        y += 18f;
        int shown = 0;
        foreach (var line in lines)
        {
            if (shown >= maxLines || y > r.End.Y - 6f) break;
            var wrap = Wrap(line, right - x - 10f, Ui.TextSmall, Fonts.Body, 2);
            foreach (var wl in wrap)
            {
                if (y > r.End.Y - 6f) break;
                if (wl == wrap[0]) ci.Circle(new Vector2(x + 2.5f, y - 4f), 1.6f, found ? accent.WithAlpha(0.7f) : Palette.TextMuted, true, -1f, true);
                Gfx.Text(ci, Fonts.Body, new Vector2(x + 10f, y), wl, Ui.TextSmall, found ? Palette.Text.WithAlpha(0.9f) : Palette.TextMuted);
                y += 15f;
            }
            shown++;
        }
        if (lines.Count > shown && y <= r.End.Y - 4f)
            Gfx.TextRight(ci, Fonts.Body, new Vector2(right, r.End.Y - 6f), $"+{lines.Count - shown}", Ui.TextTiny, Palette.TextMuted);
    }

    // ─────────────────────────── 인용 (일기 · 말) ───────────────────────────

    /// <summary>인용 한 토막: 왼쪽 색 띠 · 누가(· 어떤 처지) · 글 (최대 maxLines). 쓴 높이를 돌려준다.</summary>
    public static float Quote(CanvasItem ci, float x, float right, float y, string who, string role, string text, Color color, int maxLines = 2)
    {
        var lines = Wrap($"“{text}”", right - x - 14f, Ui.TextBody, Fonts.Body, maxLines);
        float h = 16f + lines.Count * 16f;
        ci.Box(new Rect2(x, y + 2f, 2.5f, h - 4f), color.WithAlpha(0.8f));
        Gfx.Text(ci, Fonts.Bold, new Vector2(x + 10f, y + 12f), who, Ui.TextSmall, color);
        if (role != "") Gfx.Text(ci, Fonts.Body, new Vector2(x + 16f + Gfx.Width(Fonts.Bold, who, Ui.TextSmall), y + 12f), role, Ui.TextTiny, Palette.TextMuted);
        for (int i = 0; i < lines.Count; i++)
            Gfx.Text(ci, Fonts.Body, new Vector2(x + 10f, y + 28f + i * 16f), lines[i], Ui.TextBody, Palette.Text.WithAlpha(0.88f));
        return h + Ui.S2;
    }

    // ─────────────────────────── 말풍선 (손목 단말 · 메신저 자리) ───────────────────────────

    /// <summary>
    /// 말풍선 한 개: 보낸 사람 · 글 (내 쪽이면 오른쪽에 붙는다). 쓴 높이를 돌려준다.
    /// 손목 단말 메시지 · 엿들은 말 · 컴퓨터 알림을 같은 톤으로 (메신저 화면은 이 부품을 쓴다).
    /// </summary>
    public static float Bubble(CanvasItem ci, float x, float right, float y, string who, string text, bool mine, Color color, int maxLines = 3)
    {
        float maxW = (right - x) * 0.82f;
        var lines = Wrap(text, maxW - 16f, Ui.TextBody, Fonts.Body, maxLines);
        float w = 16f;
        foreach (var l in lines) w = Mathf.Max(w, Gfx.Width(Fonts.Body, l, Ui.TextBody) + 16f);
        w = Mathf.Max(w, Gfx.Width(Fonts.Bold, who, Ui.TextTiny) + 16f);
        float h = 20f + lines.Count * 16f;
        float bx = mine ? right - w : x;
        var r = new Rect2(bx, y, w, h);
        Gfx.RoundRect(ci, r, mine ? color.Darkened(0.55f).WithAlpha(0.85f) : Well, Ui.RadiusControl, color.WithAlpha(mine ? 0.5f : 0.3f));
        Gfx.Text(ci, Fonts.Bold, new Vector2(bx + 8f, y + 12f), who, Ui.TextTiny, color);
        for (int i = 0; i < lines.Count; i++)
            Gfx.Text(ci, Fonts.Body, new Vector2(bx + 8f, y + 28f + i * 16f), lines[i], Ui.TextBody, Palette.Text.WithAlpha(0.92f));
        return h + Ui.S1;
    }

    // ─────────────────────────── 단계 줄 (지나온 과정) ───────────────────────────

    /// <summary>세로 단계 줄: 점 · 시각 · 글. 마지막 단계는 굵게 · 뜻 색. 쓴 높이를 돌려준다.</summary>
    public static float Steps(CanvasItem ci, float x, float right, float y, IReadOnlyList<(long Tick, string Text)> steps, Tone last = Tone.Info, int max = 5)
    {
        float y0 = y;
        int start = System.Math.Max(0, steps.Count - max);
        for (int i = start; i < steps.Count; i++)
        {
            bool end = i == steps.Count - 1;
            var col = end ? Ui.Of(last) : Palette.TextDim;
            var dot = new Vector2(x + 4f, y + 7f);
            if (!end) ci.DrawLine(dot + new Vector2(0, 4f), dot + new Vector2(0, 16f), Ui.PanelEdge.Lightened(0.2f), 1f);
            ci.Circle(dot, end ? 3.5f : 2.5f, col, true, -1f, true);
            string clock = SimTime.Clock(steps[i].Tick);
            Gfx.Text(ci, Fonts.Body, new Vector2(x + 14f, y + 11f), clock, Ui.TextTiny, Palette.TextMuted);
            float tx = x + 18f + Gfx.Width(Fonts.Body, clock, Ui.TextTiny);
            Gfx.Text(ci, end ? Fonts.Bold : Fonts.Body, new Vector2(tx, y + 11f), Fit(steps[i].Text, right - tx, Ui.TextSmall, end ? Fonts.Bold : Fonts.Body), Ui.TextSmall, end ? col : Palette.Text.WithAlpha(0.85f));
            y += 16f;
        }
        return y - y0;
    }

    // ─────────────────────────── 규모 점 ───────────────────────────

    /// <summary>규모별 수를 작은 색 점으로 (①부터). 쓴 너비를 돌려준다.</summary>
    public static float ScaleDots(CanvasItem ci, Vector2 leftCenter, int[] byScale)
    {
        float x = leftCenter.X;
        for (int s = 0; s < byScale.Length; s++)
        {
            for (int n = 0; n < System.Math.Min(byScale[s], 6); n++)
            {
                float r = 2.5f + s * 0.6f;
                ci.Circle(new Vector2(x + r, leftCenter.Y), r, ScaleColor((IncidentScale)s), true, -1f, true);
                x += r * 2f + 2f;
            }
        }
        return x - leftCenter.X;
    }

    // ─────────────────────────── 현장 그림 틀 ───────────────────────────

    /// <summary>
    /// 그날의 현장: 방 도면을 작게 (방 색 칸) · 사건 자리에 규모 색 표시 · 테두리는 사진 틀.
    /// 사진이 생기면 이 틀 안에 사진을 건다.
    /// </summary>
    public static void SceneFrame(CanvasItem ci, Rect2 r, World w, int roomId, System.Numerics.Vector2? at, Color mark, string caption)
    {
        Gfx.RoundRect(ci, r, new Color(0.9f, 0.9f, 0.86f, 0.9f), 3f);
        var inner = new Rect2(r.Position + new Vector2(4f, 4f), r.Size - new Vector2(8f, 18f));
        ci.Box(inner, new Color(0.05f, 0.06f, 0.08f));
        Room? room = roomId >= 0 && roomId < w.Ship.Rooms.Count ? w.Ship.Rooms[roomId] : null;
        if (room != null && room.Cells.Count > 0)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in room.Cells) { minX = Mathf.Min(minX, c.X); minY = Mathf.Min(minY, c.Y); maxX = Mathf.Max(maxX, c.X); maxY = Mathf.Max(maxY, c.Y); }
            minX -= 1; minY -= 1; maxX += 1; maxY += 1;
            float cs = Mathf.Min(inner.Size.X / (maxX - minX + 1), inner.Size.Y / (maxY - minY + 1));
            var off = inner.Position + (inner.Size - new Vector2(maxX - minX + 1, maxY - minY + 1) * cs) * 0.5f;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var cell = new Cell(x, y);
                    if (w.Ship.RoomAt(cell) is not Room rr) continue;
                    var col = Palette.Room(rr.Kind).Darkened(rr == room ? 0.35f : 0.7f);
                    ci.Box(new Rect2(off + new Vector2(x - minX, y - minY) * cs, new Vector2(cs - 0.6f, cs - 0.6f)), col);
                }
            foreach (var f in w.Ship.Furniture)
            {
                if (f.Room != room) continue;
                ci.Box(new Rect2(off + new Vector2(f.MinX - minX, f.MinY - minY) * cs, new Vector2(f.Width, f.Height) * cs - new Vector2(1f, 1f)), new Color(0.75f, 0.78f, 0.82f, 0.55f));
            }
            if (at is System.Numerics.Vector2 p && p.X >= minX && p.X <= maxX + 1 && p.Y >= minY && p.Y <= maxY + 1)
            {
                var pp = off + new Vector2(p.X - minX, p.Y - minY) * cs;
                ci.Circle(pp, Mathf.Max(3f, cs * 0.9f), mark.WithAlpha(0.35f), true, -1f, true);
                ci.Circle(pp, Mathf.Max(1.6f, cs * 0.4f), mark, true, -1f, true);
            }
        }
        else Icons.Draw(ci, "photo", inner.GetCenter(), Mathf.Min(inner.Size.X, inner.Size.Y) * 0.4f, new Color(0.4f, 0.42f, 0.46f));
        Gfx.Text(ci, Fonts.Body, new Vector2(r.Position.X + 5f, r.End.Y - 4f), Fit(caption, r.Size.X - 10f, Ui.TextMicro), Ui.TextMicro, new Color(0.2f, 0.2f, 0.22f));
    }
}
