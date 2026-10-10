using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.9 도감 항목 그림: 표(CurioTable)의 작은 벡터 그림을 풀어 그린다 — 항목마다 실루엣이 다르고,
// 갈래마다 움직임이 다르다 (창밖: 흘러감 · 이상한 일: 깜빡임 · 표본: 천천히 돎 · 재능: 들썩임 · 비밀: 자물쇠).

public static class CurioArt
{
    private enum Op : byte { Poly, PolyLine, Line, Disc, Ring, Arc }
    private readonly record struct Cmd(Op Op, Vector2[] Pts, float R, float A0, float A1);
    private static readonly Dictionary<string, Cmd[]> _cache = new();

    private static Cmd[] Parse(string art)
    {
        if (_cache.TryGetValue(art, out var got)) return got;
        var list = new List<Cmd>();
        foreach (var part0 in art.Split(';'))
        {
            var part = part0.Trim();
            if (part.Length < 2) continue;
            char k = part[0];
            var nums = part[1..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
            if (k is 'O' or 'o' or 'A')
            {
                var v = nums[0].Split(',').Select(F).ToArray();
                list.Add(new Cmd(k == 'O' ? Op.Disc : k == 'o' ? Op.Ring : Op.Arc, new[] { new Vector2(v[0], v[1]) }, v[2], v.Length > 3 ? v[3] : 0f, v.Length > 4 ? v[4] : 360f));
            }
            else
            {
                var pts = nums.Select(n => { var xy = n.Split(','); return new Vector2(F(xy[0]), F(xy[1])); }).ToArray();
                list.Add(new Cmd(k == 'P' ? Op.Poly : k == 'p' ? Op.PolyLine : Op.Line, pts, 0f, 0f, 0f));
            }
        }
        var arr = list.ToArray();
        _cache[art] = arr;
        return arr;
    }

    /// <summary>그림 하나. found=false면 흐린 실루엣(테두리만). t = 화면 시각(움직임).</summary>
    public static void Draw(CanvasItem ci, CurioSpec spec, Vector2 c, float size, bool found, float t, float alpha = 1f)
    {
        var col = new Color(spec.Color);
        float rot = 0f, dy = 0f, a = alpha;
        float seed = (spec.Key.GetHashCode() & 0xff) / 40f;
        if (found)
            switch (spec.Kind)
            {
                case CurioKind.Passing: c += new Vector2(Mathf.Sin(t * 0.4f + seed) * size * 0.06f, 0); break;
                case CurioKind.Anomaly: a *= 0.7f + 0.3f * Mathf.Sin(t * 5.3f + seed) * Mathf.Sin(t * 2.1f); break;
                case CurioKind.Hidden when spec.Shelf == CodexShelf.Specimen: rot = Mathf.Sin(t * 0.6f + seed) * 0.12f; break;
                case CurioKind.Talent: dy = -Mathf.Abs(Mathf.Sin(t * 2.4f + seed)) * size * 0.06f; break;
            }
        c += new Vector2(0, dy);
        Vector2 P(Vector2 p) => c + p.Rotated(rot) * size;
        var fill = found ? col.WithAlpha(0.8f * a) : new Color(1, 1, 1, 0.04f * a);
        var edge = found ? col.Lightened(0.25f).WithAlpha(a) : new Color(1, 1, 1, 0.22f * a);
        float lw = Mathf.Max(1f, size * 0.07f);
        foreach (var cmd in Parse(spec.Art))
        {
            switch (cmd.Op)
            {
                case Op.Poly:
                {
                    var pts = cmd.Pts.Select(P).ToArray();
                    if (pts.Length >= 3 && found) TryPoly(ci, pts, fill);
                    ci.Polyline(pts.Append(pts[0]).ToArray(), edge, lw * 0.8f, true);
                    break;
                }
                case Op.PolyLine: { var pts = cmd.Pts.Select(P).ToArray(); ci.Polyline(pts.Append(pts[0]).ToArray(), edge, lw, true); break; }
                case Op.Line: ci.Polyline(cmd.Pts.Select(P).ToArray(), edge, lw, true); break;
                case Op.Disc: if (found) ci.Circle(P(cmd.Pts[0]), cmd.R * size, fill, true, -1f, true); ci.Arc(P(cmd.Pts[0]), cmd.R * size, 0, Mathf.Tau, 18, edge, lw * 0.7f, true); break;
                case Op.Ring: ci.Arc(P(cmd.Pts[0]), cmd.R * size, 0, Mathf.Tau, 22, edge, lw, true); break;
                case Op.Arc: ci.Arc(P(cmd.Pts[0]), cmd.R * size, Mathf.DegToRad(cmd.A0) + rot, Mathf.DegToRad(cmd.A1) + rot, 16, edge, lw, true); break;
            }
        }
    }

    private static void TryPoly(CanvasItem ci, Vector2[] pts, Color col)
    {
        // 꼬인 다각형은 그리기에서 실패하므로 삼각분할이 되는 것만 채운다
        if (Geometry2D.TriangulatePolygon(pts).Length >= 3) ci.Poly(pts, col);
    }

    /// <summary>도감 칸마다 다른 액자: 사진(필름 구멍) · 표본(핀 꽂은 이름표) · 유물(받침대) · 사람들(둥근 초상 틀).</summary>
    public static void Frame(CanvasItem ci, CodexShelf shelf, Rect2 r, Color tone)
    {
        var edge = tone.WithAlpha(0.35f);
        switch (shelf)
        {
            case CodexShelf.Photo:
                Gfx.RoundRect(ci, r, new Color(0.03f, 0.035f, 0.05f, 0.9f), 4f, edge);
                for (float x = r.Position.X + 6; x < r.End.X - 6; x += 10)
                {
                    ci.Box(new Rect2(x, r.Position.Y + 3, 5, 4), new Color(1, 1, 1, 0.08f));
                    ci.Box(new Rect2(x, r.End.Y - 7, 5, 4), new Color(1, 1, 1, 0.08f));
                }
                break;
            case CodexShelf.Specimen:
                Gfx.RoundRect(ci, r, new Color(0.09f, 0.1f, 0.08f, 0.9f), 3f, edge);
                ci.Circle(new Vector2(r.GetCenter().X, r.Position.Y + 7), 3f, tone.WithAlpha(0.7f), true, -1f, true);
                ci.Box(new Rect2(r.Position.X + 8, r.End.Y - 12, r.Size.X - 16, 6), new Color(1, 1, 1, 0.07f));
                break;
            case CodexShelf.Relic:
                Gfx.RoundRect(ci, r, new Color(0.06f, 0.06f, 0.08f, 0.9f), 6f, edge);
                ci.Poly(new[] { new Vector2(r.Position.X + 12, r.End.Y - 6), new Vector2(r.End.X - 12, r.End.Y - 6), new Vector2(r.End.X - 20, r.End.Y - 14), new Vector2(r.Position.X + 20, r.End.Y - 14) }, new Color(1, 1, 1, 0.07f));
                break;
            default:
                Gfx.RoundRect(ci, r, new Color(0.06f, 0.055f, 0.08f, 0.9f), 8f, edge);
                ci.Arc(r.GetCenter(), Mathf.Min(r.Size.X, r.Size.Y) * 0.44f, 0, Mathf.Tau, 32, tone.WithAlpha(0.25f), 2f, true);
                break;
        }
    }
}
