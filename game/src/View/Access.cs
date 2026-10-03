using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.6 색약 팔레트: 색만으로 구분하지 않게 — 사람은 표식 모양 · 채움, 방은 바닥 무늬. 표는 Core/UiAccess (시험과 같은 표).

public static class ColorSafe
{
    public static bool On { get; set; }

    /// <summary>뜻 색을 색약 팔레트로 (꺼져 있으면 그대로).</summary>
    public static Color Tint(Color c) => c;

    /// <summary>사람 표식: 모양 + 채움 (색이 비슷해도 갈린다).</summary>
    public static void DrawMark(CanvasItem ci, int id, Vector2 c, float r, Color col, Color bg)
    {
        var mark = UiAccess.Mark(id);
        var fill = UiAccess.Fill(id);
        var pts = Shape(mark, c, r);
        if (fill == MarkFill.Solid) ci.DrawColoredPolygon(pts, col);
        else ci.DrawColoredPolygon(pts, bg);
        var ring = pts.Append(pts[0]).ToArray();
        ci.DrawPolyline(ring, col, Mathf.Max(1f, r * 0.22f), true);
        if (fill == MarkFill.Half)
        {
            // 아래 절반만 채운다
            var half = pts.Where(p => p.Y >= c.Y - 0.01f).Append(new Vector2(c.X + r, c.Y)).Append(new Vector2(c.X - r, c.Y)).ToArray();
            if (half.Length >= 3) { var hull = Hull(half); if (hull.Length >= 3) ci.DrawColoredPolygon(hull, col); }
        }
        else if (fill == MarkFill.Dot) ci.DrawCircle(c, r * 0.3f, col, true, -1f, true);
    }

    private static Vector2[] Shape(CrewMark m, Vector2 c, float r)
    {
        Vector2[] Poly(int n, float rot, float rr = 1f) => Enumerable.Range(0, n).Select(i => c + Vector2.FromAngle(rot + i * Mathf.Tau / n) * r * rr).ToArray();
        switch (m)
        {
            case CrewMark.Circle: return Poly(14, 0f);
            case CrewMark.Square: return Poly(4, Mathf.Pi / 4f, 1.15f);
            case CrewMark.Triangle: return Poly(3, -Mathf.Pi / 2f, 1.2f);
            case CrewMark.Diamond: return Poly(4, 0f, 1.2f);
            case CrewMark.Pentagon: return Poly(5, -Mathf.Pi / 2f, 1.08f);
            case CrewMark.Hexagon: return Poly(6, 0f, 1.05f);
            case CrewMark.Star:
                return Enumerable.Range(0, 10).Select(i => c + Vector2.FromAngle(-Mathf.Pi / 2f + i * Mathf.Pi / 5f) * r * (i % 2 == 0 ? 1.25f : 0.55f)).ToArray();
            default:
                float a = r * 0.38f, b = r * 1.1f;
                return new[] { new Vector2(-a, -b), new Vector2(a, -b), new Vector2(a, -a), new Vector2(b, -a), new Vector2(b, a), new Vector2(a, a), new Vector2(a, b), new Vector2(-a, b), new Vector2(-a, a), new Vector2(-b, a), new Vector2(-b, -a), new Vector2(-a, -a) }.Select(p => c + p).ToArray();
        }
    }

    private static Vector2[] Hull(Vector2[] pts)
    {
        var p = pts.OrderBy(v => v.X).ThenBy(v => v.Y).ToArray();
        if (p.Length < 3) return p;
        float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var h = new Vector2[p.Length * 2];
        int k = 0;
        for (int i = 0; i < p.Length; i++) { while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--; h[k++] = p[i]; }
        for (int i = p.Length - 2, t = k + 1; i >= 0; i--) { while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--; h[k++] = p[i]; }
        return h.Take(k - 1).ToArray();
    }

    /// <summary>방 바닥 무늬 (사각형 안). step = 무늬 간격.</summary>
    public static void DrawWeave(CanvasItem ci, Rect2 r, Weave w, Color col, float step)
    {
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        switch (w)
        {
            case Weave.Plain: break;
            case Weave.Hatch: Diag(ci, r, col, step, false); break;
            case Weave.BackHatch: Diag(ci, r, col, step, true); break;
            case Weave.CrossHatch: Diag(ci, r, col, step * 1.4f, false); Diag(ci, r, col, step * 1.4f, true); break;
            case Weave.Dots: for (float y = y0 + step * 0.5f; y < y1; y += step) for (float x = x0 + step * 0.5f; x < x1; x += step) ci.DrawCircle(new Vector2(x, y), step * 0.12f, col, true, -1f, true); break;
            case Weave.Vertical: for (float x = x0 + step * 0.5f; x < x1; x += step) ci.DrawLine(new Vector2(x, y0), new Vector2(x, y1), col, 1f); break;
            case Weave.Horizontal: for (float y = y0 + step * 0.5f; y < y1; y += step) ci.DrawLine(new Vector2(x0, y), new Vector2(x1, y), col, 1f); break;
            case Weave.Checker:
                for (float y = y0; y < y1; y += step) for (float x = x0; x < x1; x += step)
                    if (((int)((x - x0) / step) + (int)((y - y0) / step)) % 2 == 0) ci.DrawRect(new Rect2(x, y, Mathf.Min(step, x1 - x), Mathf.Min(step, y1 - y)), col.WithAlpha(col.A * 0.5f));
                break;
            case Weave.Wave:
                for (float y = y0 + step * 0.5f; y < y1; y += step)
                {
                    int n = Mathf.Max(2, (int)((x1 - x0) / (step * 0.25f)));
                    var pts = new Vector2[n + 1];
                    for (int i = 0; i <= n; i++) { float x = x0 + (x1 - x0) * i / n; pts[i] = new Vector2(x, y + Mathf.Sin((x - x0) / step * Mathf.Tau) * step * 0.18f); }
                    ci.DrawPolyline(pts, col, 1f, true);
                }
                break;
            case Weave.Zigzag:
                for (float y = y0 + step * 0.5f; y < y1; y += step)
                {
                    var pts = new System.Collections.Generic.List<Vector2>();
                    for (float x = x0; x <= x1; x += step * 0.5f) pts.Add(new Vector2(x, y + ((int)((x - x0) / (step * 0.5f)) % 2 == 0 ? -1 : 1) * step * 0.2f));
                    if (pts.Count >= 2) ci.DrawPolyline(pts.ToArray(), col, 1f, true);
                }
                break;
            case Weave.Rings: for (float y = y0 + step * 0.5f; y < y1; y += step) for (float x = x0 + step * 0.5f; x < x1; x += step) ci.DrawArc(new Vector2(x, y), step * 0.28f, 0, Mathf.Tau, 10, col, 1f, true); break;
            case Weave.Bricks:
                for (float y = y0; y < y1; y += step * 0.5f)
                {
                    ci.DrawLine(new Vector2(x0, y), new Vector2(x1, y), col, 1f);
                    float off = ((int)((y - y0) / (step * 0.5f)) % 2) * step * 0.5f;
                    for (float x = x0 + off; x < x1; x += step) ci.DrawLine(new Vector2(x, y), new Vector2(x, Mathf.Min(y1, y + step * 0.5f)), col, 1f);
                }
                break;
        }
    }

    private static void Diag(CanvasItem ci, Rect2 r, Color col, float step, bool back)
    {
        float w = r.Size.X, h = r.Size.Y;
        for (float k = -h; k < w; k += step)
        {
            float ax = Mathf.Max(0, k), ay = Mathf.Max(0, -k);
            float bx = Mathf.Min(w, k + h), by = bx - k;
            var a = new Vector2(ax, back ? h - ay : ay);
            var b = new Vector2(bx, back ? h - by : by);
            ci.DrawLine(r.Position + a, r.Position + b, col, 1f);
        }
    }
}
