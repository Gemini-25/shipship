using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.14 기술마다 고유 아이콘: 분야는 테두리 실루엣(동력 육각 · 냉각 눈꽃 고리 · 생명유지 둥근 네모 · 식량 잎 · 선체 모 깎은 판 · 의료 십자 ·
/// 컴퓨터 핀 달린 칩 · 로봇 톱니 · 추진 화살 삼각 · 센서 마름모 · 제작 팔각 · 거주 집 · 방어 방패), 기술은 가운데 큰 그림 + 오른쪽 아래 작은 딱지 그림.
/// 조리법은 Core의 TechWeb.Node(id).Icon ("주+딱지") — 111개가 모두 다르다. 그리기만 한다 (결정론에 닿지 않는다).
/// </summary>
public static class TechIcons
{
    public static Color FieldColor(TechField f) => f switch
    {
        TechField.Power => new Color("#f2c230"),
        TechField.Cooling => new Color("#7cd4ff"),
        TechField.Life => new Color("#6ee7b7"),
        TechField.Food => new Color("#a6d65a"),
        TechField.Hull => new Color("#b4bfcc"),
        TechField.Medical => new Color("#ff7a85"),
        TechField.Computing => new Color("#a394ff"),
        TechField.Robotics => new Color("#f2994a"),
        TechField.Propulsion => new Color("#ff8a5c"),
        TechField.Sensors => new Color("#5ec8e6"),
        TechField.Fabrication => new Color("#d9a35b"),
        TechField.Habitat => new Color("#e6a8d7"),
        _ => new Color("#8fa6ff"),
    };

    private static Vector2[] Ngon(Vector2 c, float r, int n, float rot = 0f)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = rot + Mathf.Tau * i / n; p[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        return p;
    }

    private static void Outline(CanvasItem ci, Vector2[] pts, Color col, float w)
    {
        var l = new Vector2[pts.Length + 1];
        Array.Copy(pts, l, pts.Length);
        l[^1] = pts[0];
        ci.Polyline(l, col, w, true);
    }

    /// <summary>분야 실루엣 (채움 · 테두리).</summary>
    public static Vector2[] FramePoints(TechField f, Vector2 c, float r)
    {
        switch (f)
        {
            case TechField.Power: return Ngon(c, r, 6, Mathf.Pi / 6f);
            case TechField.Fabrication: return Ngon(c, r, 8, Mathf.Pi / 8f);
            case TechField.Sensors: return Ngon(c, r * 1.08f, 4, -Mathf.Pi / 2f);
            case TechField.Propulsion: return new[] { c + new Vector2(r * 1.05f, 0f), c + new Vector2(-r * 0.8f, -r * 0.95f), c + new Vector2(-r * 0.45f, 0f), c + new Vector2(-r * 0.8f, r * 0.95f) };
            case TechField.Habitat: return new[] { c + new Vector2(0f, -r), c + new Vector2(r * 0.95f, -r * 0.2f), c + new Vector2(r * 0.8f, r * 0.9f), c + new Vector2(-r * 0.8f, r * 0.9f), c + new Vector2(-r * 0.95f, -r * 0.2f) };
            case TechField.Defense:
            {
                var l = new List<Vector2> { c + new Vector2(-r * 0.85f, -r * 0.8f), c + new Vector2(0f, -r), c + new Vector2(r * 0.85f, -r * 0.8f) };
                for (int i = 0; i <= 6; i++) { float t = i / 6f; l.Add(c + new Vector2(r * 0.85f * (1f - t), -r * 0.2f + r * 1.15f * Mathf.Sin(t * Mathf.Pi * 0.5f))); }
                for (int i = 5; i >= 0; i--) { float t = i / 6f; l.Add(c + new Vector2(-r * 0.85f * (1f - t), -r * 0.2f + r * 1.15f * Mathf.Sin(t * Mathf.Pi * 0.5f))); }
                return l.ToArray();
            }
            case TechField.Medical:
            {
                float a = r * 0.42f, b = r;
                return new[] { c + new Vector2(-a, -b), c + new Vector2(a, -b), c + new Vector2(a, -a), c + new Vector2(b, -a), c + new Vector2(b, a), c + new Vector2(a, a),
                    c + new Vector2(a, b), c + new Vector2(-a, b), c + new Vector2(-a, a), c + new Vector2(-b, a), c + new Vector2(-b, -a), c + new Vector2(-a, -a) };
            }
            case TechField.Food:
            {
                // 잎 (두 원호가 만나는 모양을 45° 눕힌다)
                var l = new List<Vector2>();
                float d = 0.55f * r, rr = Mathf.Sqrt(d * d + r * r), th = Mathf.Atan2(r, d);
                for (int i = 0; i <= 10; i++) { float a = Mathf.Lerp(-th, th, i / 10f); l.Add(c + new Vector2(-d + rr * Mathf.Cos(a), rr * Mathf.Sin(a)).Rotated(Mathf.Pi / 4f)); }
                for (int i = 1; i < 10; i++) { float a = Mathf.Lerp(Mathf.Pi - th, Mathf.Pi + th, i / 10f); l.Add(c + new Vector2(d + rr * Mathf.Cos(a), rr * Mathf.Sin(a)).Rotated(Mathf.Pi / 4f)); }
                return l.ToArray();
            }
            case TechField.Hull:
            {
                float k = r * 0.3f;
                return new[] { c + new Vector2(-r + k, -r), c + new Vector2(r - k, -r), c + new Vector2(r, -r + k), c + new Vector2(r, r - k), c + new Vector2(r - k, r), c + new Vector2(-r + k, r), c + new Vector2(-r, r - k), c + new Vector2(-r, -r + k) };
            }
            case TechField.Robotics:
            {
                var l = new List<Vector2>();
                for (int i = 0; i < 8; i++)
                {
                    float a0 = Mathf.Tau * i / 8f;
                    l.Add(c + new Vector2(Mathf.Cos(a0 - 0.2f), Mathf.Sin(a0 - 0.2f)) * r * 0.82f);
                    l.Add(c + new Vector2(Mathf.Cos(a0 - 0.12f), Mathf.Sin(a0 - 0.12f)) * r);
                    l.Add(c + new Vector2(Mathf.Cos(a0 + 0.12f), Mathf.Sin(a0 + 0.12f)) * r);
                    l.Add(c + new Vector2(Mathf.Cos(a0 + 0.2f), Mathf.Sin(a0 + 0.2f)) * r * 0.82f);
                }
                return l.ToArray();
            }
            case TechField.Life:
            {
                var l = new List<Vector2>();
                for (int i = 0; i < 24; i++) { float a = Mathf.Tau * i / 24f; float cs = Mathf.Cos(a), sn = Mathf.Sin(a); l.Add(c + new Vector2(Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), 0.6f), Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.6f)) * r * 0.92f); }
                return l.ToArray();
            }
            case TechField.Computing:
            {
                float k = r * 0.78f;
                return new[] { c + new Vector2(-k, -k), c + new Vector2(k, -k), c + new Vector2(k, k), c + new Vector2(-k, k) };
            }
            default: return Ngon(c, r * 0.95f, 24); // 냉각 (둥근 고리 — 눈꽃 홈은 따로)
        }
    }

    /// <summary>아이콘 하나. state: 0 보통 · 1 익힘(채움) · 2 흐림(못 고름) · 3 잠김(갈림길에서 버림).</summary>
    public static void Draw(CanvasItem ci, EraTech t, Vector2 c, float r, int state, float alpha = 1f)
    {
        var fc = FieldColor(t.Field);
        if (state == 3) fc = new Color(0.5f, 0.5f, 0.55f);
        var pts = FramePoints(t.Field, c, r);
        var bg = new Color(0.05f, 0.06f, 0.09f, 0.95f * alpha);
        var fill = state == 1 ? fc.WithAlpha(0.92f * alpha) : state == 2 ? fc.WithAlpha(0.07f * alpha) : fc.WithAlpha(0.18f * alpha);
        ci.Poly(pts, bg);
        ci.Poly(pts, fill);
        Outline(ci, pts, fc.WithAlpha((state == 2 ? 0.45f : 0.95f) * alpha), state == 1 ? 1.6f : 1.3f);
        if (t.Field == TechField.Computing) // 칩 다리
            for (int i = -1; i <= 1; i++)
            {
                float o = i * r * 0.42f;
                foreach (var (a, b) in new[] { (new Vector2(o, -r * 0.78f), new Vector2(o, -r)), (new Vector2(o, r * 0.78f), new Vector2(o, r)), (new Vector2(-r * 0.78f, o), new Vector2(-r, o)), (new Vector2(r * 0.78f, o), new Vector2(r, o)) })
                    ci.DrawLine(c + a, c + b, fc.WithAlpha(0.8f * alpha), 1.2f, true);
            }
        if (t.Field == TechField.Cooling) // 눈꽃 홈
            for (int i = 0; i < 6; i++) { var d = Vector2.FromAngle(Mathf.Tau * i / 6f - Mathf.Pi / 2f); ci.DrawLine(c + d * r * 0.95f, c + d * r * 1.12f, fc.WithAlpha(0.9f * alpha), 1.4f, true); }
        var parts = TechWeb.Node(t.Id).Icon.Split('+');
        var gc = state == 1 ? new Color(0.06f, 0.07f, 0.1f, alpha) : state == 3 ? new Color(0.6f, 0.6f, 0.65f, alpha) : fc.Lightened(0.15f).WithAlpha((state == 2 ? 0.55f : 1f) * alpha);
        Glyph(ci, parts[0], c - (parts.Length > 1 ? new Vector2(r * 0.08f, r * 0.08f) : Vector2.Zero), r * 0.5f, gc, MathF.Max(1.1f, r * 0.11f));
        if (parts.Length > 1)
        {
            var b = c + new Vector2(r * 0.62f, r * 0.62f);
            float br = r * 0.36f;
            ci.Circle(b, br, bg, true, -1f, true);
            ci.Circle(b, br, fc.WithAlpha((state == 1 ? 0.6f : 0.25f) * alpha), true, -1f, true);
            ci.Arc(b, br, 0f, Mathf.Tau, 16, fc.WithAlpha(0.9f * alpha), 1f, true);
            Glyph(ci, parts[1], b, br * 0.62f, state == 1 ? new Color(0.06f, 0.07f, 0.1f, alpha) : fc.Lightened(0.3f).WithAlpha(alpha), MathF.Max(0.9f, r * 0.07f));
        }
    }

    /// <summary>그림 하나 (반지름 s · 선 굵기 w).</summary>
    public static void Glyph(CanvasItem ci, string id, Vector2 c, float s, Color col, float w)
    {
        Vector2 P(float x, float y) => c + new Vector2(x * s, y * s);
        void L(float x0, float y0, float x1, float y1) => ci.DrawLine(P(x0, y0), P(x1, y1), col, w, true);
        void Arc(float x, float y, float rr, float a0, float a1) => ci.Arc(P(x, y), rr * s, a0, a1, 18, col, w, true);
        void Poly(params float[] xy) { var p = new Vector2[xy.Length / 2]; for (int i = 0; i < p.Length; i++) p[i] = P(xy[i * 2], xy[i * 2 + 1]); ci.Poly(p, col); }
        void Line(params float[] xy) { var p = new Vector2[xy.Length / 2]; for (int i = 0; i < p.Length; i++) p[i] = P(xy[i * 2], xy[i * 2 + 1]); ci.Polyline(p, col, w, true); }
        void Dot(float x, float y, float rr) => ci.Circle(P(x, y), rr * s, col, true, -1f, true);
        switch (id)
        {
            case "flame": Poly(0f, -1f, 0.45f, -0.25f, 0.6f, 0.35f, 0.3f, 0.85f, -0.3f, 0.85f, -0.6f, 0.35f, -0.4f, -0.1f, -0.15f, 0.1f); break;
            case "slash": L(-0.85f, -0.85f, 0.85f, 0.85f); break;
            case "cross": Poly(-0.25f, -0.85f, 0.25f, -0.85f, 0.25f, -0.25f, 0.85f, -0.25f, 0.85f, 0.25f, 0.25f, 0.25f, 0.25f, 0.85f, -0.25f, 0.85f, -0.25f, 0.25f, -0.85f, 0.25f, -0.85f, -0.25f, -0.25f, -0.25f); break;
            case "arrow": L(-0.85f, 0f, 0.7f, 0f); Line(0.25f, -0.45f, 0.8f, 0f, 0.25f, 0.45f); break;
            case "check": Line(-0.75f, 0f, -0.2f, 0.6f, 0.8f, -0.6f); break;
            case "book": Line(0f, -0.6f, -0.85f, -0.75f, -0.85f, 0.6f, 0f, 0.75f, 0.85f, 0.6f, 0.85f, -0.75f, 0f, -0.6f, 0f, 0.75f); L(-0.6f, -0.3f, -0.2f, -0.25f); L(0.2f, -0.25f, 0.6f, -0.3f); break;
            case "drop": Poly(0f, -0.95f, 0.5f, -0.05f, 0.55f, 0.35f, 0.35f, 0.75f, 0f, 0.88f, -0.35f, 0.75f, -0.55f, 0.35f, -0.5f, -0.05f); break;
            case "dot": Dot(0f, 0f, 0.45f); break;
            case "seed": Poly(0f, -0.85f, 0.45f, -0.35f, 0.5f, 0.3f, 0f, 0.85f, -0.5f, 0.3f, -0.45f, -0.35f); L(0f, -0.4f, 0f, 0.6f); break;
            case "cube": Line(0f, -0.9f, 0.8f, -0.45f, 0.8f, 0.45f, 0f, 0.9f, -0.8f, 0.45f, -0.8f, -0.45f, 0f, -0.9f); Line(-0.8f, -0.45f, 0f, 0f, 0.8f, -0.45f); L(0f, 0f, 0f, 0.9f); break;
            case "bubble": Arc(-0.3f, 0.25f, 0.5f, 0f, Mathf.Tau); Arc(0.45f, -0.35f, 0.32f, 0f, Mathf.Tau); Arc(0.4f, 0.55f, 0.18f, 0f, Mathf.Tau); break;
            case "plate": Line(-0.85f, -0.65f, 0.85f, -0.65f, 0.85f, 0.65f, -0.85f, 0.65f, -0.85f, -0.65f); Dot(-0.6f, -0.4f, 0.1f); Dot(0.6f, -0.4f, 0.1f); Dot(-0.6f, 0.4f, 0.1f); Dot(0.6f, 0.4f, 0.1f); break;
            case "bolt": Poly(0.15f, -1f, -0.55f, 0.1f, -0.05f, 0.1f, -0.2f, 1f, 0.55f, -0.15f, 0.05f, -0.15f); break;
            case "hand": Arc(0f, 0.35f, 0.42f, 0f, Mathf.Tau); L(-0.4f, 0.05f, -0.45f, -0.6f); L(-0.13f, -0.05f, -0.15f, -0.85f); L(0.13f, -0.05f, 0.15f, -0.85f); L(0.4f, 0.05f, 0.45f, -0.6f); L(-0.4f, 0.45f, -0.8f, 0.15f); break;
            case "plus": L(-0.75f, 0f, 0.75f, 0f); L(0f, -0.75f, 0f, 0.75f); break;
            case "wrench": L(-0.7f, 0.7f, 0.2f, -0.2f); Arc(0.42f, -0.42f, 0.35f, -2.2f, 2.0f); break;
            case "hex": Outline(ci, Ngon(c, s * 0.8f, 6), col, w); Dot(0f, 0f, 0.18f); break;
            case "wave": { var p = new Vector2[13]; for (int i = 0; i < 13; i++) { float x = -0.9f + 1.8f * i / 12f; p[i] = P(x, 0.45f * Mathf.Sin(x * 3.6f)); } ci.Polyline(p, col, w, true); break; }
            case "eye": Arc(0f, 0.55f, 0.95f, -2.55f, -0.6f); Arc(0f, -0.55f, 0.95f, 0.6f, 2.55f); Dot(0f, 0f, 0.25f); break;
            case "leaf": Poly(0f, -0.95f, 0.5f, -0.45f, 0.55f, 0.2f, 0f, 0.85f, -0.55f, 0.2f, -0.5f, -0.45f); L(0f, -0.6f, 0f, 0.95f); break;
            case "atom":
            {
                for (int k = 0; k < 3; k++)
                {
                    var p = new Vector2[17];
                    for (int i = 0; i < 17; i++) { float a = Mathf.Tau * i / 16f; p[i] = c + new Vector2(Mathf.Cos(a) * s * 0.9f, Mathf.Sin(a) * s * 0.32f).Rotated(k * Mathf.Pi / 3f); }
                    ci.Polyline(p, col, w * 0.8f, true);
                }
                Dot(0f, 0f, 0.18f);
                break;
            }
            case "magnet": Arc(0f, 0f, 0.6f, 0f, Mathf.Pi); L(-0.6f, 0f, -0.6f, -0.8f); L(0.6f, 0f, 0.6f, -0.8f); L(-0.8f, -0.8f, -0.4f, -0.8f); L(0.4f, -0.8f, 0.8f, -0.8f); break;
            case "shield": Line(-0.7f, -0.7f, 0f, -0.9f, 0.7f, -0.7f, 0.6f, 0.2f, 0f, 0.9f, -0.6f, 0.2f, -0.7f, -0.7f); L(0f, -0.6f, 0f, 0.55f); break;
            case "net":
                for (int i = -1; i <= 1; i++) { L(-0.75f, i * 0.65f, 0.75f, i * 0.65f); L(i * 0.65f, -0.75f, i * 0.65f, 0.75f); }
                Dot(0f, 0f, 0.16f); Dot(-0.65f, -0.65f, 0.12f); Dot(0.65f, 0.65f, 0.12f);
                break;
            case "wing": Line(-0.85f, 0.5f, -0.2f, -0.3f, 0.85f, -0.8f); Line(-0.85f, 0.5f, 0.2f, 0.05f, 0.75f, -0.35f); Line(-0.85f, 0.5f, 0.3f, 0.45f, 0.6f, 0.2f); break;
            case "gear":
                Arc(0f, 0f, 0.55f, 0f, Mathf.Tau); Dot(0f, 0f, 0.15f);
                for (int i = 0; i < 6; i++) { var d = Vector2.FromAngle(Mathf.Tau * i / 6f); ci.DrawLine(c + d * s * 0.55f, c + d * s * 0.9f, col, w * 1.4f, true); }
                break;
            case "house": Line(-0.75f, -0.05f, 0f, -0.8f, 0.75f, -0.05f); Line(-0.55f, -0.2f, -0.55f, 0.8f, 0.55f, 0.8f, 0.55f, -0.2f); Line(-0.15f, 0.8f, -0.15f, 0.3f, 0.15f, 0.3f, 0.15f, 0.8f); break;
            case "pipe": Line(-0.85f, -0.35f, 0.35f, -0.35f, 0.35f, 0.85f); L(-0.85f, -0.6f, -0.85f, -0.1f); L(0.1f, 0.85f, 0.6f, 0.85f); break;
            case "star":
            {
                var p = new Vector2[10];
                for (int i = 0; i < 10; i++) { float a = -Mathf.Pi / 2f + Mathf.Pi * i / 5f; p[i] = c + Vector2.FromAngle(a) * s * (i % 2 == 0 ? 0.95f : 0.4f); }
                ci.Poly(p, col);
                break;
            }
            case "heart": Dot(-0.32f, -0.2f, 0.36f); Dot(0.32f, -0.2f, 0.36f); Poly(-0.66f, -0.05f, 0.66f, -0.05f, 0f, 0.8f); break;
            case "ring": Arc(0f, 0f, 0.7f, 0f, Mathf.Tau); Arc(0f, 0f, 0.38f, 0f, Mathf.Tau); break;
            case "laser": Dot(-0.65f, 0f, 0.25f); L(-0.4f, 0f, 0.9f, 0f); L(0.5f, -0.3f, 0.85f, -0.55f); L(0.5f, 0.3f, 0.85f, 0.55f); break;
            case "snow":
                for (int i = 0; i < 3; i++) { var d = Vector2.FromAngle(Mathf.Pi / 2f + Mathf.Pi * i / 3f); ci.DrawLine(c - d * s * 0.85f, c + d * s * 0.85f, col, w, true); }
                for (int i = 0; i < 6; i++) { var d = Vector2.FromAngle(Mathf.Pi / 2f + Mathf.Pi * i / 3f); var m = c + d * s * 0.55f; ci.DrawLine(m, m + d.Rotated(0.7f) * s * 0.25f, col, w * 0.8f, true); ci.DrawLine(m, m + d.Rotated(-0.7f) * s * 0.25f, col, w * 0.8f, true); }
                break;
            case "lens": Arc(-0.15f, -0.15f, 0.55f, 0f, Mathf.Tau); L(0.25f, 0.25f, 0.85f, 0.85f); Arc(-0.15f, -0.15f, 0.32f, -2.6f, -1.6f); break;
            case "chip": Line(-0.5f, -0.5f, 0.5f, -0.5f, 0.5f, 0.5f, -0.5f, 0.5f, -0.5f, -0.5f); for (int i = -1; i <= 1; i++) { L(i * 0.3f, -0.5f, i * 0.3f, -0.85f); L(i * 0.3f, 0.5f, i * 0.3f, 0.85f); } Dot(0f, 0f, 0.15f); break;
            case "cell": Line(-0.75f, -0.45f, 0.65f, -0.45f, 0.65f, 0.45f, -0.75f, 0.45f, -0.75f, -0.45f); L(0.65f, -0.15f, 0.85f, -0.15f); L(0.65f, 0.15f, 0.85f, 0.15f); Poly(-0.6f, -0.3f, -0.15f, -0.3f, -0.15f, 0.3f, -0.6f, 0.3f); break;
            case "moon": Arc(0f, 0f, 0.75f, 0.9f, 5.4f); Arc(0.35f, -0.1f, 0.6f, 1.4f, 4.9f); break;
            case "sun": Dot(0f, 0f, 0.35f); for (int i = 0; i < 8; i++) { var d = Vector2.FromAngle(Mathf.Tau * i / 8f); ci.DrawLine(c + d * s * 0.55f, c + d * s * 0.9f, col, w, true); } break;
            case "flask": Line(-0.2f, -0.85f, -0.2f, -0.25f, -0.75f, 0.75f, 0.75f, 0.75f, 0.2f, -0.25f, 0.2f, -0.85f); L(-0.35f, -0.85f, 0.35f, -0.85f); Poly(-0.52f, 0.35f, 0.52f, 0.35f, 0.7f, 0.7f, -0.7f, 0.7f); break;
            case "key": Arc(-0.45f, 0f, 0.35f, 0f, Mathf.Tau); L(-0.1f, 0f, 0.85f, 0f); L(0.6f, 0f, 0.6f, 0.35f); L(0.8f, 0f, 0.8f, 0.3f); break;
            case "bug":
                Poly(0f, -0.6f, 0.38f, -0.25f, 0.4f, 0.4f, 0f, 0.75f, -0.4f, 0.4f, -0.38f, -0.25f);
                for (int i = -1; i <= 1; i++) { L(-0.35f, i * 0.3f, -0.8f, i * 0.4f + 0.05f); L(0.35f, i * 0.3f, 0.8f, i * 0.4f + 0.05f); }
                break;
            default: Dot(0f, 0f, 0.3f); break;
        }
    }

    /// <summary>열리는 조건 그림 (작은 딱지): 사고 · 방 · 솜씨 · 소재 · 유물 · 교류.</summary>
    public static string GateGlyph(GateKind k) => k switch
    {
        GateKind.Incident => "flame", GateKind.Room => "house", GateKind.Skill => "hand", GateKind.Material => "hex", GateKind.Relic => "key", GateKind.Contact => "wave", _ => "dot",
    };

    /// <summary>자물쇠 (잠김 · 조건).</summary>
    public static void Lock(CanvasItem ci, Vector2 c, float s, Color col)
    {
        ci.Arc(c + new Vector2(0f, -s * 0.25f), s * 0.42f, Mathf.Pi, Mathf.Tau, 12, col, MathF.Max(1f, s * 0.18f), true);
        Gfx.RoundRect(ci, new Rect2(c + new Vector2(-s * 0.6f, -s * 0.25f), new Vector2(s * 1.2f, s * 0.95f)), col, s * 0.15f);
        ci.Circle(c + new Vector2(0f, s * 0.2f), s * 0.14f, new Color(0, 0, 0, 0.7f), true, -1f, true);
    }
}
