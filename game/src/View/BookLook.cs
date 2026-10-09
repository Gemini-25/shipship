using Godot;

namespace ShipSim.View;

// v17.6 일기 · 연대기를 책처럼: 어두운 유리 톤 안에서 종이 결(따뜻한 어둠) · 줄 · 제본 그늘 · 접힌 귀퉁이 · 쪽 번호.
// (손글씨 무료 글꼴이 저장소에 없어 기존 글꼴을 쓴다 — 잉크 색과 줄 맞춤으로 손으로 쓴 공책 느낌만.)

public static class BookLook
{
    public static readonly Color Paper = new(0.11f, 0.102f, 0.09f, 0.97f);
    public static readonly Color PaperEdge = new(0.85f, 0.78f, 0.62f, 0.14f);
    public static readonly Color Rule = new(0.85f, 0.78f, 0.62f, 0.06f);
    public static readonly Color Margin = new(0.95f, 0.45f, 0.4f, 0.22f);
    public static readonly Color Ink = new("#ddd3c0");
    public static readonly Color InkDim = new("#a99f8c");
    public static readonly Color InkFaint = new("#7a7264");

    /// <summary>한 쪽: 종이 · 줄(lineH 간격, top부터) · 왼쪽 여백선 · 제본 그늘(binding 쪽) · 접힌 귀 · 쪽 번호.</summary>
    public static void Page(CanvasItem ci, Rect2 r, int pageNo, bool bindLeft = true, float lineH = 19f, float top = 0f, float marginX = 0f)
    {
        Gfx.RoundRect(ci, r, Paper, 6f, PaperEdge);
        // 줄
        if (lineH > 0f)
            for (float y = r.Position.Y + (top > 0f ? top : lineH * 1.6f); y < r.End.Y - 12f; y += lineH)
                ci.DrawLine(new Vector2(r.Position.X + 8f, y), new Vector2(r.End.X - 8f, y), Rule, 1f);
        if (marginX > 0f) ci.DrawLine(new Vector2(r.Position.X + marginX, r.Position.Y + 6f), new Vector2(r.Position.X + marginX, r.End.Y - 6f), Margin, 1f);
        // 제본 그늘
        for (int i = 0; i < 6; i++)
        {
            float a = 0.10f * (1f - i / 6f);
            float x = bindLeft ? r.Position.X + 1f + i * 2f : r.End.X - 3f - i * 2f;
            ci.Box(new Rect2(x, r.Position.Y + 3f, 2f, r.Size.Y - 6f), new Color(0, 0, 0, a));
        }
        // 접힌 귀 (바깥 아래)
        float k = 12f;
        var c = bindLeft ? new Vector2(r.End.X, r.End.Y) : new Vector2(r.Position.X, r.End.Y);
        float s = bindLeft ? -1f : 1f;
        ci.Poly(new[] { c + new Vector2(s * k, 0), c + new Vector2(0, -k), c + new Vector2(s * k, -k) }, new Color(0.2f, 0.185f, 0.16f, 1f));
        ci.DrawLine(c + new Vector2(s * k, 0), c + new Vector2(0, -k), PaperEdge, 1f);
        if (pageNo > 0) Gfx.TextCentered(ci, Fonts.Body, new Vector2(r.GetCenter().X, r.End.Y - 8f), $"— {pageNo} —", Ui.TextMicro, InkFaint);
    }
}
