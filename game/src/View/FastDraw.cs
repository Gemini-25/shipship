using System;
using System.Collections.Generic;
using Godot;

namespace ShipSim.View;

/// <summary>
/// 60프레임: Godot 의 원 · 호 · 다각형 · 꺾은선 · 테두리 사각형 그리기는 한 번에 3~20µs 든다 (점을 만들고 삼각형으로 나눈다).
/// 사각형 · 직선은 0.1~0.4µs. 같은 모양을 미리 그려 둔 원 · 고리 그림 한 장에서 잘라 쓰거나 (사각형 하나 값) · 짧은 선 묶음 · 기본 도형으로 그린다.
/// 이름과 인자는 Godot 의 것과 같다 (ci.DrawCircle → ci.Circle).
/// </summary>
public static class FastDraw
{
    // 원 그림 한 장: 칸 0 은 채운 원, 칸 1~7 은 굵기가 다른 고리 (굵기 ÷ 바깥 반지름)
    private const int Cell = 256, Cols = 4;
    private const float Outer = 116f; // 칸 안 원의 바깥 반지름 (가장자리 여백 — 작게 줄인 그림에서 이웃 칸이 번지지 않게)
    private static readonly float[] RingRatios = { 0.04f, 0.07f, 0.11f, 0.16f, 0.24f, 0.36f, 0.55f };
    private static ImageTexture? _atlas;
    private static Texture2D Atlas => _atlas ??= BuildAtlas();

    private static ImageTexture BuildAtlas()
    {
        int n = RingRatios.Length + 1;
        int rows = (n + Cols - 1) / Cols, w = Cell * Cols, h = Cell * rows;
        var data = new byte[w * h * 2]; // 밝기 · 알파 (흰색에 알파만 — 그릴 때 색을 곱한다)
        for (int k = 0; k < n; k++)
        {
            float inner = k == 0 ? -1f : Outer * (1f - RingRatios[k - 1]);
            int ox = k % Cols * Cell, oy = k / Cols * Cell;
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    float dx = x + 0.5f - Cell * 0.5f, dy = y + 0.5f - Cell * 0.5f;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    float a = Math.Clamp(Outer - d + 0.5f, 0f, 1f);
                    if (inner > 0f) a = MathF.Min(a, Math.Clamp(d - inner + 0.5f, 0f, 1f));
                    int i = ((oy + y) * w + ox + x) * 2;
                    data[i] = 255;
                    data[i + 1] = (byte)MathF.Round(a * 255f);
                }
        }
        var img = Image.CreateFromData(w, h, false, Image.Format.La8, data);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static Rect2 CellRect(int k) => new(k % Cols * Cell, k / Cols * Cell, Cell, Cell);

    /// <summary>고리 굵기에 가장 가까운 칸 (너무 가늘면 -1 — 선으로 그린다).</summary>
    private static int RingCell(float ratio)
    {
        if (ratio >= 0.8f) return 0;
        if (ratio < RingRatios[0] * 0.75f) return -1;
        int best = 0;
        float bd = float.MaxValue;
        for (int i = 0; i < RingRatios.Length; i++)
        {
            float d = MathF.Abs(MathF.Log(ratio / RingRatios[i]));
            if (d < bd) { bd = d; best = i; }
        }
        return best + 1;
    }

    // 화면 배율 (가는 선 굵기 · 호 조각 수) — 같은 프레임 같은 캔버스는 한 번만 묻는다
    private static CanvasItem? _scaleCi;
    private static ulong _scaleFrame;
    private static float _scale = 1f;
    private static float ScaleOf(CanvasItem ci)
    {
        ulong f = Engine.GetProcessFrames();
        if (!ReferenceEquals(ci, _scaleCi) || f != _scaleFrame)
        {
            _scaleCi = ci;
            _scaleFrame = f;
            _scale = MathF.Max(0.02f, ci.GetGlobalTransformWithCanvas().X.Length());
        }
        return _scale;
    }

    /// <summary>DrawCircle 과 같다: 채운 원은 원 그림 한 장, 테두리는 고리 그림 (아주 가는 큰 고리는 선 묶음).</summary>
    public static void Circle(this CanvasItem ci, Vector2 position, float radius, Color color, bool filled = true, float width = -1f, bool antialiased = false)
    {
        if (radius <= 0f || color.A <= 0f) return;
        if (filled)
        {
            float o = radius * Cell * 0.5f / Outer;
            ci.DrawTextureRectRegion(Atlas, new Rect2(position.X - o, position.Y - o, 2f * o, 2f * o), CellRect(0), color);
            return;
        }
        Ring(ci, position, radius, color, width);
    }

    private static void Ring(CanvasItem ci, Vector2 c, float r, Color color, float width)
    {
        if (width < 0f) width = 1f / ScaleOf(ci);
        float outer = r + width * 0.5f;
        int k = RingCell(width / outer);
        if (k < 0) { ArcLines(ci, c, r, 0f, Mathf.Tau, 64, color, width); return; }
        float o = outer * Cell * 0.5f / Outer;
        ci.DrawTextureRectRegion(Atlas, new Rect2(c.X - o, c.Y - o, 2f * o, 2f * o), CellRect(k), color);
    }

    /// <summary>DrawArc 와 같다: 한 바퀴면 고리 그림, 아니면 화면 크기에 맞춘 짧은 선 묶음 (한 번에).</summary>
    public static void Arc(this CanvasItem ci, Vector2 center, float radius, float startAngle, float endAngle, int pointCount, Color color, float width = -1f, bool antialiased = false)
    {
        if (radius <= 0f || color.A <= 0f) return;
        if (MathF.Abs(endAngle - startAngle) >= Mathf.Tau - 1e-3f) { Ring(ci, center, radius, color, width); return; }
        if (width > 2.5f) { ci.DrawArc(center, radius, startAngle, endAngle, pointCount, color, width, antialiased); return; } // 굵은 호는 이음매가 벌어지지 않게 그대로
        ArcLines(ci, center, radius, startAngle, endAngle, pointCount, color, width);
    }

    private static void ArcLines(CanvasItem ci, Vector2 c, float r, float a0, float a1, int pointCount, Color color, float width)
    {
        float s = ScaleOf(ci);
        int segs = Math.Clamp((int)MathF.Ceiling(MathF.Abs(a1 - a0) * r * s / 3f), 2, Math.Max(2, pointCount - 1));
        var pts = Buffer(segs * 2);
        var prev = c + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r;
        for (int i = 1; i <= segs; i++)
        {
            float a = a0 + (a1 - a0) * i / segs;
            var p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            pts[2 * i - 2] = prev;
            pts[2 * i - 1] = p;
            prev = p;
        }
        ci.DrawMultiline(pts, color, width);
    }

    // 선 묶음 점 배열 (길이마다 하나씩 다시 쓴다 — 그리기는 한 줄기에서만)
    private static readonly Dictionary<int, Vector2[]> Buffers = new();
    private static Vector2[] Buffer(int n)
    {
        if (!Buffers.TryGetValue(n, out var b)) Buffers[n] = b = new Vector2[n];
        return b;
    }

    private static readonly Color[] Col3 = new Color[3], Col4 = new Color[4];

    /// <summary>DrawColoredPolygon 과 같다: 점 셋 · 넷은 기본 도형 (삼각형으로 나누지 않는다), 그보다 많으면 그대로.</summary>
    public static void Poly(this CanvasItem ci, Vector2[] points, Color color, Vector2[]? uvs = null, Texture2D? texture = null)
    {
        if (uvs == null && texture == null && color.A > 0f)
        {
            if (points.Length == 3) { Col3[0] = Col3[1] = Col3[2] = color; ci.DrawPrimitive(points, Col3, null); return; }
            if (points.Length == 4 && Convex4(points)) { Col4[0] = Col4[1] = Col4[2] = Col4[3] = color; ci.DrawPrimitive(points, Col4, null); return; }
        }
        ci.DrawColoredPolygon(points, color, uvs, texture);
    }

    /// <summary>DrawPolygon 과 같다 (점마다 색): 점 셋 · 넷이고 볼록하면 기본 도형, 아니면 그대로.</summary>
    public static void Polygon(this CanvasItem ci, Vector2[] points, Color[] colors, Vector2[]? uvs = null, Texture2D? texture = null)
    {
        if (uvs == null && texture == null && colors.Length == points.Length && (points.Length == 3 || points.Length == 4 && Convex4(points)))
        {
            ci.DrawPrimitive(points, colors, null);
            return;
        }
        ci.DrawPolygon(points, colors, uvs, texture);
    }

    /// <summary>네 점이 볼록한 사각형인가 (기본 도형은 0-1-2 · 0-2-3 으로 나눠 그린다).</summary>
    private static bool Convex4(Vector2[] p)
    {
        float c0 = (p[1] - p[0]).Cross(p[2] - p[1]), c1 = (p[2] - p[1]).Cross(p[3] - p[2]);
        float c2 = (p[3] - p[2]).Cross(p[0] - p[3]), c3 = (p[0] - p[3]).Cross(p[1] - p[0]);
        return (c0 >= 0f && c1 >= 0f && c2 >= 0f && c3 >= 0f) || (c0 <= 0f && c1 <= 0f && c2 <= 0f && c3 <= 0f);
    }

    /// <summary>DrawPolyline 과 같다: 짧은 선 묶음 한 번 (이음매만 다르다 — 굵은 선은 그대로).</summary>
    public static void Polyline(this CanvasItem ci, Vector2[] points, Color color, float width = -1f, bool antialiased = false)
    {
        if (points.Length < 2 || color.A <= 0f) return;
        if (width > 2.5f) { ci.DrawPolyline(points, color, width, antialiased); return; }
        var pts = Buffer((points.Length - 1) * 2);
        for (int i = 1; i < points.Length; i++) { pts[2 * i - 2] = points[i - 1]; pts[2 * i - 1] = points[i]; }
        ci.DrawMultiline(pts, color, width);
    }

    /// <summary>DrawRect 와 같다: 테두리는 가는 사각형 넷 (Godot 테두리 사각형은 채운 것의 30배 든다).</summary>
    public static void Box(this CanvasItem ci, Rect2 rect, Color color, bool filled = true, float width = -1f, bool antialiased = false)
    {
        if (filled) { ci.DrawRect(rect, color); return; }
        if (color.A <= 0f) return;
        if (width < 0f) width = 1f / ScaleOf(ci);
        rect = rect.Abs();
        float h = width * 0.5f;
        var p = rect.Position;
        var e = rect.End;
        ci.DrawRect(new Rect2(p.X - h, p.Y - h, rect.Size.X + width, width), color);
        ci.DrawRect(new Rect2(p.X - h, e.Y - h, rect.Size.X + width, width), color);
        ci.DrawRect(new Rect2(p.X - h, p.Y + h, width, MathF.Max(0f, rect.Size.Y - width)), color);
        ci.DrawRect(new Rect2(e.X - h, p.Y + h, width, MathF.Max(0f, rect.Size.Y - width)), color);
    }

    /// <summary>둥근 사각형: 가운데 · 옆 띠는 사각형, 네 모서리는 원 그림의 4분의 1 (테두리는 고리 그림의 4분의 1).</summary>
    public static void RoundRect(CanvasItem ci, Rect2 rect, Color fill, float radius, Color? border, float borderWidth)
    {
        rect = rect.Abs();
        float r = MathF.Max(0f, MathF.Min(radius, MathF.Min(rect.Size.X, rect.Size.Y) * 0.5f));
        var p = rect.Position;
        var e = rect.End;
        if (fill.A > 0f)
        {
            if (r < 0.5f) ci.DrawRect(rect, fill);
            else
            {
                ci.DrawRect(new Rect2(p.X + r, p.Y, rect.Size.X - 2f * r, rect.Size.Y), fill);
                if (rect.Size.Y > 2f * r)
                {
                    ci.DrawRect(new Rect2(p.X, p.Y + r, r, rect.Size.Y - 2f * r), fill);
                    ci.DrawRect(new Rect2(e.X - r, p.Y + r, r, rect.Size.Y - 2f * r), fill);
                }
                Corners(ci, rect, r, 0, fill);
            }
        }
        if (border is not Color bc || borderWidth <= 0f || bc.A <= 0f) return;
        float bw = MathF.Min(borderWidth, MathF.Min(rect.Size.X, rect.Size.Y) * 0.5f);
        if (r < 0.5f) { ci.DrawRect(new Rect2(p, new Vector2(rect.Size.X, bw)), bc); ci.DrawRect(new Rect2(p.X, e.Y - bw, rect.Size.X, bw), bc); ci.DrawRect(new Rect2(p.X, p.Y + bw, bw, rect.Size.Y - 2f * bw), bc); ci.DrawRect(new Rect2(e.X - bw, p.Y + bw, bw, rect.Size.Y - 2f * bw), bc); return; }
        ci.DrawRect(new Rect2(p.X + r, p.Y, rect.Size.X - 2f * r, bw), bc);
        ci.DrawRect(new Rect2(p.X + r, e.Y - bw, rect.Size.X - 2f * r, bw), bc);
        if (rect.Size.Y > 2f * r)
        {
            ci.DrawRect(new Rect2(p.X, p.Y + r, bw, rect.Size.Y - 2f * r), bc);
            ci.DrawRect(new Rect2(e.X - bw, p.Y + r, bw, rect.Size.Y - 2f * r), bc);
        }
        int k = RingCell(bw / r);
        if (k < 0) k = 1;
        Corners(ci, rect, r, k, bc);
    }

    /// <summary>네 모서리에 칸 k 의 4분의 1씩.</summary>
    private static void Corners(CanvasItem ci, Rect2 rect, float r, int k, Color color)
    {
        var cell = CellRect(k);
        float o = r * Cell * 0.5f / Outer; // 그림 반쪽이 화면에서 차지하는 길이 (원 바깥 여백 포함)
        float m = o - r; // 여백
        var half = new Vector2(Cell * 0.5f, Cell * 0.5f);
        var p = rect.Position;
        var e = rect.End;
        // 모서리 사각형 (여백만큼 바깥으로) ↔ 그림의 4분의 1
        ci.DrawTextureRectRegion(Atlas, new Rect2(p.X - m, p.Y - m, o, o), new Rect2(cell.Position, half), color);
        ci.DrawTextureRectRegion(Atlas, new Rect2(e.X - r, p.Y - m, o, o), new Rect2(cell.Position + new Vector2(half.X, 0f), half), color);
        ci.DrawTextureRectRegion(Atlas, new Rect2(p.X - m, e.Y - r, o, o), new Rect2(cell.Position + new Vector2(0f, half.Y), half), color);
        ci.DrawTextureRectRegion(Atlas, new Rect2(e.X - r, e.Y - r, o, o), new Rect2(cell.Position + half, half), color);
    }
}
