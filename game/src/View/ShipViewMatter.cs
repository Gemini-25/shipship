using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;
using NVec = System.Numerics.Vector2;
using Mat = ShipSim.Core.Material;

namespace ShipSim.View;

// v16.4 재질 · 원소 · 물건 물리 보기 (읽기만 — 시뮬레이션을 바꾸지 않는다). 재질마다 · 상태마다 다르게:
//   러그(테두리 · 마름모 무늬 · 술 — 젖으면 짙어지고 물빛 반짝임이 흐르고, 다 머금으면 가장자리로 물방울 · 널면 빨랫줄에 처진 주름 · 그을면 갈색 얼룩) ·
//   수건(접힌 모서리 · 두 줄 띠 — 젖어 달궈지면 김 · 그을면 숯 자국과 숨 쉬는 불씨 · 잿빛 연기) ·
//   고무 매트(말아 둔 원통 · 깔면 검은 골 무늬와 노랑 모서리) · 서류(겹친 장 · 흩어지면 바람에 펄럭 · 젖으면 번진 잉크) ·
//   종이 상자(테이프 십자 · 젖으면 처진다) · 플라스틱 상자(골 · 손잡이 구멍 — 녹으면 흘러내린 덩어리와 연둣빛 유독 연기) ·
//   유리병(투명한 원통 · 담긴 물 높이 · 빛 줄 — 금 · 쓰러짐 · 반짝이는 조각) · 사기 컵(손잡이 · 흰 조각) ·
//   물통 · 기름통(뚜껑 · 손잡이 · X 각인 — 깨지면 갈라진 금) · 가루 포대(묶은 주둥이 · 박음질 — 터지면 납작한 자루와 가루 더미) ·
//   얼음(반투명 덩어리 · 속 금 · 서리 반짝 — 녹을수록 작아진다) · 공용 공구함(빨간 상자 · 손잡이 · 손때 · 균 점) · 식량 상자(나무 살) · 무전기(안테나 · 표시등).
// 바닥: 쏟은 물(물빛 웅덩이 · 흐르는 결) · 기름(무지개 막) · 냉각수(청록 · 서리 결정) · 흩날린 가루(베이지 입자) ·
// 칸 장: 뜨거운 칸의 열기 일렁임 · 찬 벽의 서리 반짝 · 젖은 바닥을 기는 파란 전기 불꽃 · 문틈 · 환기구 바람 줄 · 드러난 젖은 접속부.
public partial class ShipView
{
    private static readonly Color MxInk = new("#15171c"), MxWater = new("#6fb8ff"), MxWaterHi = new("#e2f4ff"), MxOil = new("#3a2f12"), MxCoolant = new("#4fe3d6"),
        MxSpark = new("#9fe0ff"), MxSparkCore = new("#ffffff"), MxEmber = new("#ff6a1f"), MxSmoke = new("#6d6d6d"), MxToxic = new("#a6d84a"), MxFrost = new("#eaf6ff"),
        MxRugA = new("#7a3f52"), MxRugB = new("#d9b56a"), MxTowelA = new("#e6e2d6"), MxTowelB = new("#4f86c6"), MxRubber = new("#22252a"), MxHazard = new("#f2c230"),
        MxPaper = new("#f4efe1"), MxCard = new("#b08850"), MxTape = new("#d8c79a"), MxCrate = new("#5b7fa6"), MxGlass = new("#bfe4ff"), MxCeramic = new("#f2f0ea"),
        MxJug = new("#4f9ad8"), MxCan = new("#a33a2a"), MxSack = new("#d8c9a0"), MxIce = new("#cfeeff"), MxTool = new("#c8362c"), MxWood = new("#8c6a3e"), MxRadio = new("#2b2f36");

    private static float MH(int a, int b, int k) => Hash(a, b, k + 900);

    /// <summary>동적 층: 바닥 액체 · 장 · 물건 · 가루.</summary>
    private void PaintMatter(CanvasItem ci)
    {
        var m = _world.Matter;
        bool fine = Zoom > 1.15f, mid = Zoom > 0.55f;
        PaintSpills(ci, fine);
        if (mid) PaintFields(ci, fine);
        PaintJunctions(ci, fine);
        foreach (var t in m.Things)
        {
            if (t.CarriedBy >= 0) continue;
            DrawArticle(ci, t, ToPx(t.At.Center + t.Off), t.Angle, fine, 1f);
        }
        foreach (var t in m.Things)
        {
            if (t.CarriedBy < 0) continue;
            CrewMember? who = null;
            foreach (var c in _world.Crew) if (c.Id == t.CarriedBy) { who = c; break; }
            if (who == null) continue;
            DrawArticle(ci, t, CrewPx(who) + new Vector2(CrewRadius * 0.7f, -CrewRadius * 0.2f), 0.3f, fine, 0.7f);
        }
        PaintDust(ci, fine);
    }

    // ───────────────────────────── 바닥 액체 ─────────────────────────────

    private void PaintSpills(CanvasItem ci, bool fine)
    {
        var m = _world.Matter;
        var g = _world.Ship.Grid;
        foreach (var (i, s) in m.Spills)
        {
            if (s.Liters < 0.03f) continue;
            var c = g.CellAt(i);
            var ctr = CellRect(c).GetCenter();
            float size = Mathf.Clamp(0.25f + s.Liters * 0.25f, 0.25f, 0.62f);
            var pts = new Vector2[9];
            for (int k = 0; k < 9; k++)
            {
                float ang = k * Mathf.Tau / 9f + MH(c.X, c.Y, 1) * 2f;
                pts[k] = ctr + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * T * size * (0.75f + 0.35f * MH(c.X, c.Y, k + 2));
            }
            var body = s.Kind == Mat.Oil ? MxOil : s.Coolant ? MxCoolant : MxWater;
            ci.DrawColoredPolygon(pts, new Color(body, s.Kind == Mat.Oil ? 0.75f : 0.42f));
            if (s.Kind == Mat.Oil)
            {
                // 기름: 무지갯빛 얇은 막이 천천히 돈다
                for (int k = 0; k < 3; k++)
                {
                    float a0 = _time * 0.3f + k * 2.1f + MH(c.X, c.Y, 20);
                    var col = Color.FromHsv(Mathf.PosMod(0.55f + k * 0.2f + _time * 0.05f, 1f), 0.6f, 1f, 0.45f);
                    ci.DrawArc(ctr, T * size * (0.35f + 0.18f * k), a0, a0 + 1.4f, 10, col, 1.2f, true);
                }
            }
            else
            {
                // 물: 빛 반짝임 줄 · 흐르는 결(물이 적은 이웃 쪽으로)
                float sh = Mathf.PosMod(_time * 0.5f + MH(c.X, c.Y, 30), 1f);
                ci.DrawLine(ctr + new Vector2(-T * size * 0.6f + sh * T * size, -T * size * 0.3f), ctr + new Vector2(-T * size * 0.3f + sh * T * size, -T * size * 0.45f), new Color(MxWaterHi, 0.6f), 1.3f, true);
                if (fine)
                    foreach (var d in Cell.Dirs4)
                    {
                        var n = c + d;
                        if (m.LitersAt(n) + 0.15f >= s.Liters || g.Kind(n) != TileKind.Floor) continue;
                        float ph = Mathf.PosMod(_time * 0.9f + MH(c.X, c.Y, 40), 1f);
                        var dir = new Vector2(d.X, d.Y);
                        var p = ctr + dir * T * (0.2f + 0.45f * ph);
                        ci.DrawLine(p, p + dir * 4f + new Vector2(-dir.Y, dir.X) * 1.5f, new Color(MxWaterHi, 0.55f * (1f - ph)), 1f, true);
                    }
                if (s.Coolant)
                    for (int k = 0; k < 3; k++)
                    {
                        var fp = ctr + new Vector2(MH(c.X, c.Y, k + 50) - 0.5f, MH(c.Y, c.X, k + 51) - 0.5f) * T * size;
                        DrawFrostStar(ci, fp, 2.2f);
                    }
            }
        }
    }

    private void DrawFrostStar(CanvasItem ci, Vector2 p, float r)
    {
        var col = new Color(MxFrost, 0.85f);
        ci.DrawLine(p - new Vector2(r, 0), p + new Vector2(r, 0), col, 0.8f, true);
        ci.DrawLine(p - new Vector2(r * 0.5f, r * 0.87f), p + new Vector2(r * 0.5f, r * 0.87f), col, 0.8f, true);
        ci.DrawLine(p - new Vector2(-r * 0.5f, r * 0.87f), p + new Vector2(-r * 0.5f, r * 0.87f), col, 0.8f, true);
    }

    // ───────────────────────────── 칸 장: 열기 · 서리 · 전기 · 바람 ─────────────────────────────

    private void PaintFields(CanvasItem ci, bool fine)
    {
        var m = _world.Matter;
        var g = _world.Ship.Grid;
        foreach (var (i, v) in m.HeatField)
        {
            var c = g.CellAt(i);
            var r = CellRect(c);
            if (v > 30f)
            {
                // 열기 일렁임: 위로 흔들리며 오르는 투명한 결
                float a = Mathf.Clamp((v - 30f) / 120f, 0.08f, 0.45f);
                ci.DrawRect(r.Grow(-2f), new Color(1f, 0.45f, 0.15f, a * 0.25f));
                for (int k = 0; k < (fine ? 3 : 2); k++)
                {
                    float x = r.Position.X + T * (0.25f + 0.25f * k);
                    var pts = new Vector2[6];
                    for (int s = 0; s < 6; s++)
                    {
                        float y = r.End.Y - 3f - s * (T - 6f) / 5f;
                        pts[s] = new Vector2(x + Mathf.Sin(_time * 4f + s * 1.3f + k * 2f + c.X) * 1.6f, y);
                    }
                    ci.DrawPolyline(pts, new Color(1f, 0.85f, 0.6f, a), 1f, true);
                }
            }
            else if (v < -6f && fine)
            {
                // 찬 벽 곁: 바닥 서리 결정이 반짝인다
                for (int k = 0; k < 3; k++)
                {
                    var p = r.Position + new Vector2(4 + MH(c.X, c.Y, k + 60) * (T - 8), 4 + MH(c.Y, c.X, k + 61) * (T - 8));
                    float tw = 0.5f + 0.5f * Mathf.Sin(_time * 3f + k * 2f + c.X);
                    DrawFrostStar(ci, p, 1.6f + tw);
                }
            }
        }
        // 전기: 젖은 바닥을 기는 파란 불꽃 (보이는 칸만 — 러그 밑은 안 보인다)
        foreach (var (i, v) in m.LiveField)
        {
            if (v < 0.3f) continue;
            var c = g.CellAt(i);
            if (m.UnderRug(c)) continue;
            var ctr = CellRect(c).GetCenter();
            int flick = (int)(_time * 12f) + c.X * 7 + c.Y * 3;
            if ((flick & 3) == 0) continue;
            for (int k = 0; k < (fine ? 2 : 1); k++)
            {
                float a0 = MH(c.X + flick, c.Y, k + 70) * Mathf.Tau;
                var p0 = ctr + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * T * 0.3f;
                var p1 = p0 + new Vector2(Mathf.Cos(a0 + 2f), Mathf.Sin(a0 + 2f)) * T * 0.35f;
                var mid = (p0 + p1) * 0.5f + new Vector2(MH(flick, c.Y, k + 71) - 0.5f, MH(c.X, flick, k + 72) - 0.5f) * 8f;
                ci.DrawPolyline(new[] { p0, mid, p1 }, new Color(MxSpark, 0.5f + 0.4f * v), 1.4f, true);
                ci.DrawPolyline(new[] { p0, mid, p1 }, new Color(MxSparkCore, 0.6f * v), 0.6f, true);
            }
        }
        // 바람: 문틈 · 환기구 쪽으로 흐르는 줄 (흐름이 셀수록 길고 빠르다)
        foreach (var (i, v) in m.DraftField)
        {
            float len = v.Length();
            if (len < 0.15f) continue;
            var c = g.CellAt(i);
            var ctr = CellRect(c).GetCenter();
            var dir = new Vector2(v.X, v.Y) / len;
            float ph = Mathf.PosMod(_time * (0.6f + len) + MH(c.X, c.Y, 80), 1f);
            var side = new Vector2(-dir.Y, dir.X);
            for (int k = -1; k <= 1; k += 2)
            {
                var p = ctr + side * k * 5f + dir * T * (ph - 0.5f) * 0.8f;
                ci.DrawLine(p, p + dir * Mathf.Min(10f, 4f + len * 6f), new Color(0.85f, 0.9f, 1f, 0.35f * (1f - Mathf.Abs(ph - 0.5f) * 2f)), 1f, true);
            }
        }
    }

    /// <summary>바닥 아래 접속부: 찾았거나(러그를 걷었다) 드러났을 때만 (숨은 건 안 그린다 — 세계 ≠ 아는 것).</summary>
    private void PaintJunctions(CanvasItem ci, bool fine)
    {
        var m = _world.Matter;
        foreach (var j in m.Junctions)
        {
            if (!j.Known || m.UnderRug(j.At)) continue;
            var ctr = CellRect(j.At).GetCenter();
            // 열어 둔 바닥 판 · 접속함
            ci.DrawRect(new Rect2(ctr - new Vector2(9, 7), new Vector2(18, 14)), new Color(0.08f, 0.09f, 0.11f, 0.9f));
            ci.DrawRect(new Rect2(ctr - new Vector2(5, 4), new Vector2(10, 8)), new Color(0.35f, 0.38f, 0.42f, 1f));
            ci.DrawLine(ctr + new Vector2(-9, -2), ctr + new Vector2(-5, -1), new Color(0.9f, 0.3f, 0.2f), 1.4f, true);
            ci.DrawLine(ctr + new Vector2(5, 1), ctr + new Vector2(9, 3), new Color(0.2f, 0.4f, 0.95f), 1.4f, true);
            if (fine) foreach (var sp in new[] { new Vector2(-4, -3), new Vector2(4, -3), new Vector2(-4, 3), new Vector2(4, 3) }) ci.DrawCircle(ctr + sp, 0.8f, new Color(0.8f, 0.82f, 0.86f), true, -1f, true);
            if (j.Wet > 0.05f) ci.DrawArc(ctr, 10f, 0f, Mathf.Tau, 16, new Color(MxWater, 0.25f + 0.5f * j.Wet), 1.5f, true); // 물기 고리
            if (j.Taped)
            {
                ci.DrawLine(ctr + new Vector2(-6, -4), ctr + new Vector2(6, 4), MxInk, 2.4f);
                ci.DrawLine(ctr + new Vector2(-6, 4), ctr + new Vector2(6, -4), MxInk, 2.4f);
            }
            if (j.Live && _world.Tick - j.LastSpark < SimTime.Minutes(2))
            {
                float f = Mathf.PosMod(_time * 8f, 1f);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * 1.57f + f * 3f;
                    var tip = ctr + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (6f + 5f * f);
                    ci.DrawLine(ctr, tip, new Color(MxSpark, 1f - f), 1.2f, true);
                }
                ci.DrawCircle(ctr, 2.2f, new Color(MxSparkCore, 0.9f), true, -1f, true);
            }
            if (!j.Fixed) DrawComputerTag(ci, ctr + new Vector2(0, -T * 0.55f));
        }
    }

    // ───────────────────────────── 가루 ─────────────────────────────

    private void PaintDust(CanvasItem ci, bool fine)
    {
        var m = _world.Matter;
        var g = _world.Ship.Grid;
        foreach (var (i, v) in m.DustField)
        {
            var c = g.CellAt(i);
            var r = CellRect(c);
            ci.DrawRect(r, new Color(0.93f, 0.89f, 0.78f, 0.12f + 0.35f * v));
            int n = fine ? 10 : 4;
            for (int k = 0; k < n; k++)
            {
                float ph = Mathf.PosMod(_time * (0.15f + 0.1f * MH(c.X, c.Y, k)) + MH(c.Y, c.X, k), 1f);
                var p = r.Position + new Vector2(MH(c.X, c.Y, k + 90) * T, (1f - ph) * T);
                ci.DrawCircle(p + new Vector2(Mathf.Sin(_time + k) * 2f, 0), 0.9f + v, new Color(0.96f, 0.92f, 0.82f, 0.55f * v * (1f - Mathf.Abs(ph - 0.5f))), true, -1f, true);
            }
        }
    }

    // ───────────────────────────── 물건 ─────────────────────────────

    private void DrawArticle(CanvasItem ci, Article t, Vector2 at, float angle, bool fine, float scale)
    {
        float s = T * scale;
        int seed = t.Id * 13 + 7;
        float wet = t.WetFrac;
        Color Wet(Color c) => c.Lerp(new Color(c.R * 0.45f, c.G * 0.5f, c.B * 0.6f, c.A), Mathf.Clamp(wet, 0f, 1f) * 0.7f); // 젖으면 짙다
        Color Char(Color c) => c.Lerp(new Color(0.12f, 0.08f, 0.05f, c.A), Mathf.Clamp(t.Char, 0f, 1f) * 0.85f);   // 그을면 갈색 → 검다
        Color Tint(Color c) => Char(Wet(c));
        var tr = Transform2D.Identity.Rotated(angle).Translated(at);
        Vector2 P(float x, float y) => tr * new Vector2(x * s, y * s);
        Vector2[] Poly(params (float x, float y)[] xs) { var a = new Vector2[xs.Length]; for (int k = 0; k < xs.Length; k++) a[k] = P(xs[k].x, xs[k].y); return a; }
        void Box(float x0, float y0, float x1, float y1, Color c) => ci.DrawColoredPolygon(Poly((x0, y0), (x1, y0), (x1, y1), (x0, y1)), c);
        void Edge(float x0, float y0, float x1, float y1, Color c, float wdt = 1f) { var p = Poly((x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0)); ci.DrawPolyline(p, c, wdt, true); }

        switch (t.Kind)
        {
            case ArticleKind.Rug when t.Hung:
            {
                // 널어 말리는 러그: 줄에 걸쳐 처진 천 · 주름 · 떨어지는 물방울
                ci.DrawLine(P(-0.45f, -0.38f), P(0.45f, -0.38f), new Color(0.75f, 0.78f, 0.8f), 1.2f, true);
                ci.DrawColoredPolygon(Poly((-0.38f, -0.38f), (0.38f, -0.38f), (0.34f, 0.32f), (-0.34f, 0.36f)), Tint(MxRugA));
                for (int k = 0; k < 4; k++) ci.DrawLine(P(-0.28f + k * 0.18f, -0.34f), P(-0.26f + k * 0.18f, 0.3f), new Color(0, 0, 0, 0.25f), 1f, true);
                if (wet > 0.1f)
                    for (int k = 0; k < 3; k++)
                    {
                        float ph = Mathf.PosMod(_time * 1.2f + k * 0.33f + t.Id * 0.17f, 1f);
                        ci.DrawCircle(P(-0.2f + k * 0.2f, 0.36f + ph * 0.25f), 1.2f, new Color(MxWater, 0.8f * (1f - ph) * wet), true, -1f, true);
                    }
                break;
            }
            case ArticleKind.Rug:
            {
                Box(-0.46f, -0.36f, 0.46f, 0.36f, Tint(MxRugA));
                Edge(-0.4f, -0.3f, 0.4f, 0.3f, Tint(MxRugB), 1.6f);
                ci.DrawColoredPolygon(Poly((0f, -0.22f), (0.22f, 0f), (0f, 0.22f), (-0.22f, 0f)), Tint(MxRugB.Darkened(0.2f))); // 가운데 마름모
                if (fine)
                {
                    ci.DrawColoredPolygon(Poly((0f, -0.1f), (0.1f, 0f), (0f, 0.1f), (-0.1f, 0f)), Tint(MxRugA.Lightened(0.25f)));
                    for (int k = 0; k < 7; k++) // 술
                    {
                        float y = -0.3f + k * 0.1f;
                        ci.DrawLine(P(-0.46f, y), P(-0.52f, y + 0.01f), Tint(MxRugB), 1f, true);
                        ci.DrawLine(P(0.46f, y), P(0.52f, y + 0.01f), Tint(MxRugB), 1f, true);
                    }
                }
                if (wet > 0.3f) // 물빛 반짝임이 천천히 흐른다
                {
                    float sh = Mathf.PosMod(_time * 0.35f + t.Id * 0.3f, 1f) * 0.9f - 0.45f;
                    ci.DrawLine(P(sh - 0.08f, -0.3f), P(sh + 0.08f, 0.3f), new Color(MxWaterHi, 0.45f * wet), 2f, true);
                }
                if (wet > 0.95f) // 다 머금었다 — 가장자리로 번지는 물
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * 1.05f + t.Id;
                        ci.DrawCircle(P(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.4f), 1.4f + Mathf.Sin(_time * 2f + k) * 0.4f, new Color(MxWater, 0.7f), true, -1f, true);
                    }
                if (t.Char > 0.05f) DrawCharSpots(ci, P, t, seed);
                break;
            }
            case ArticleKind.Towel:
            {
                Box(-0.22f, -0.16f, 0.22f, 0.16f, Tint(MxTowelA));
                Box(-0.22f, -0.07f, 0.22f, -0.02f, Tint(MxTowelB));
                Box(-0.22f, 0.04f, 0.22f, 0.08f, Tint(MxTowelB));
                ci.DrawColoredPolygon(Poly((0.22f, -0.16f), (0.22f, 0f), (0.1f, -0.16f)), Tint(MxTowelA.Darkened(0.2f))); // 접힌 모서리
                if (fine) for (int k = 0; k < 5; k++) ci.DrawLine(P(-0.2f + k * 0.1f, 0.16f), P(-0.2f + k * 0.1f, 0.19f), Tint(MxTowelA), 1f, true); // 올
                if (wet > 0.1f && t.Temp > 45f) DrawSteam(ci, P(0f, -0.2f), seed, wet);
                if (t.Char > 0.03f) DrawCharSpots(ci, P, t, seed);
                break;
            }
            case ArticleKind.RubberMat when t.Stowed:
            {
                // 말아 둔 매트: 원통 · 노랑 띠
                Box(-0.3f, -0.1f, 0.3f, 0.1f, MxRubber);
                ci.DrawCircle(P(-0.3f, 0f), 0.1f * s, MxRubber.Lightened(0.15f), true, -1f, true);
                ci.DrawArc(P(-0.3f, 0f), 0.06f * s, 0f, Mathf.Tau, 10, MxRubber.Lightened(0.35f), 1f, true);
                Box(-0.05f, -0.1f, 0.05f, 0.1f, MxHazard);
                break;
            }
            case ArticleKind.RubberMat:
            {
                Box(-0.47f, -0.47f, 0.47f, 0.47f, new Color(MxRubber, 0.95f));
                for (int k = 0; k < 8; k++) ci.DrawLine(P(-0.4f, -0.38f + k * 0.11f), P(0.4f, -0.38f + k * 0.11f), MxRubber.Lightened(0.18f), 1.2f, true); // 골 무늬
                foreach (var (cx, cy) in new[] { (-0.47f, -0.47f), (0.33f, -0.47f), (-0.47f, 0.33f), (0.33f, 0.33f) }) Box(cx, cy, cx + 0.14f, cy + 0.14f, MxHazard);
                if (_world.Matter.LiveAt(t.At) == 0f && NearLive(t.At)) // 곁에 전기가 흐르는데 매트 위는 0 — 막힌 번개 표시
                {
                    var c0 = P(0f, 0f);
                    ci.DrawPolyline(new[] { c0 + new Vector2(-2, -6), c0 + new Vector2(2, -1), c0 + new Vector2(-2, 1), c0 + new Vector2(2, 6) }, new Color(MxSpark, 0.8f), 1.4f, true);
                    ci.DrawLine(c0 + new Vector2(-6, -6), c0 + new Vector2(6, 6), new Color(1f, 0.3f, 0.3f, 0.85f), 1.4f, true);
                }
                break;
            }
            case ArticleKind.PaperStack:
            {
                bool scattered = t.LastMoved >= 0 || t.Ruined;
                int sheets = scattered ? 4 : 3;
                for (int k = 0; k < sheets; k++)
                {
                    float ox = scattered ? (MH(seed, k, 1) - 0.5f) * 0.7f : k * 0.02f, oy = scattered ? (MH(seed, k, 2) - 0.5f) * 0.6f : -k * 0.02f;
                    float flap = scattered && _world.Matter.DraftAt(t.At) is NVec dv && dv.Length() > 0.15f ? Mathf.Sin(_time * 9f + k) * 0.03f : 0f;
                    var col = t.Ruined ? new Color(0.78f, 0.78f, 0.74f) : Tint(MxPaper);
                    ci.DrawColoredPolygon(Poly((ox - 0.16f, oy - 0.2f + flap), (ox + 0.16f, oy - 0.2f), (ox + 0.16f, oy + 0.2f), (ox - 0.16f, oy + 0.2f - flap)), col);
                    if (fine) for (int l = 0; l < 4; l++) ci.DrawLine(P(ox - 0.11f, oy - 0.12f + l * 0.07f), P(ox + 0.09f, oy - 0.12f + l * 0.07f), new Color(0.3f, 0.32f, 0.4f, t.Ruined ? 0.25f : 0.55f), 0.8f, true);
                    if (t.Ruined) ci.DrawCircle(P(ox + 0.03f, oy), 0.07f * s, new Color(0.25f, 0.3f, 0.5f, 0.35f), true, -1f, true); // 번진 잉크
                }
                if (t.Char > 0.03f) DrawCharSpots(ci, P, t, seed);
                break;
            }
            case ArticleKind.CardboardBox:
            {
                float sag = t.WetFrac * 0.06f;
                ci.DrawColoredPolygon(Poly((-0.3f, -0.3f), (0.3f, -0.3f), (0.3f + sag, 0.3f), (-0.3f - sag, 0.3f + sag)), Tint(MxCard));
                Box(-0.04f, -0.3f, 0.04f, 0.3f, Tint(MxTape));
                Box(-0.3f, -0.04f, 0.3f, 0.04f, Tint(MxTape));
                if (fine) { ci.DrawLine(P(-0.3f, -0.3f), P(-0.04f, -0.04f), new Color(0, 0, 0, 0.2f), 1f, true); ci.DrawLine(P(0.3f, -0.3f), P(0.04f, -0.04f), new Color(0, 0, 0, 0.2f), 1f, true); }
                if (_world.Matter.InAisle(t.At)) DrawHazardBand(ci, new Rect2(P(-0.32f, 0.33f), new Vector2(0.64f * s, 3f)));
                if (t.Char > 0.03f) DrawCharSpots(ci, P, t, seed);
                break;
            }
            case ArticleKind.PlasticCrate when t.Melt > 0.1f:
                DrawMelted(ci, P, t, MxCrate, seed);
                break;
            case ArticleKind.PlasticCrate:
            {
                Box(-0.32f, -0.26f, 0.32f, 0.26f, Tint(MxCrate));
                for (int k = 0; k < 5; k++) ci.DrawLine(P(-0.26f + k * 0.13f, -0.2f), P(-0.26f + k * 0.13f, 0.2f), Tint(MxCrate.Darkened(0.3f)), 1.4f, true); // 골
                Box(-0.1f, -0.24f, 0.1f, -0.17f, MxInk); // 손잡이 구멍
                Box(-0.1f, 0.17f, 0.1f, 0.24f, MxInk);
                if (_world.Matter.InAisle(t.At)) DrawHazardBand(ci, new Rect2(P(-0.34f, 0.29f), new Vector2(0.68f * s, 3f)));
                break;
            }
            case ArticleKind.GlassJar:
            {
                bool down = t.Stage >= BreakStage.Broken;
                var glass = new Color(MxGlass, 0.45f);
                if (down) ci.DrawColoredPolygon(Poly((-0.25f, -0.1f), (0.2f, -0.1f), (0.25f, 0.1f), (-0.25f, 0.1f)), glass);
                else
                {
                    ci.DrawCircle(P(0f, 0f), 0.17f * s, glass, true, -1f, true);
                    if (t.Contents > 0f) ci.DrawCircle(P(0f, 0.02f), 0.12f * s, new Color(MxWater, 0.6f), true, -1f, true); // 담긴 물
                    ci.DrawArc(P(0f, 0f), 0.17f * s, 0f, Mathf.Tau, 14, new Color(MxGlass.Lightened(0.3f), 0.9f), 1.2f, true);
                    ci.DrawArc(P(0f, 0f), 0.12f * s, 3.6f, 4.6f, 6, new Color(1f, 1f, 1f, 0.85f), 1.2f, true); // 빛 줄
                    if (t.Temp > 60f) ci.DrawArc(P(0f, 0f), 0.2f * s, 0f, Mathf.Tau, 14, new Color(1f, 0.5f, 0.2f, 0.45f), 1.5f, true); // 달궈졌다
                }
                if (t.Stage >= BreakStage.Cracked) DrawCrack(ci, P, seed);
                break;
            }
            case ArticleKind.Mug:
            {
                ci.DrawCircle(P(0f, 0f), 0.14f * s, MxCeramic, true, -1f, true);
                ci.DrawCircle(P(0f, 0f), 0.1f * s, new Color(0.45f, 0.3f, 0.2f), true, -1f, true); // 차
                ci.DrawArc(P(0.17f, 0f), 0.06f * s, -1.4f, 1.4f, 6, MxCeramic, 2f, true); // 손잡이
                if (t.Stage >= BreakStage.Cracked) DrawCrack(ci, P, seed);
                break;
            }
            case ArticleKind.WaterJug or ArticleKind.OilCan:
            {
                bool oil = t.Kind == ArticleKind.OilCan;
                var body = oil ? MxCan : new Color(MxJug, 0.8f);
                Box(-0.24f, -0.3f, 0.24f, 0.3f, Char(body));
                Box(-0.08f, -0.38f, 0.08f, -0.3f, oil ? MxInk : new Color(0.9f, 0.9f, 0.95f)); // 뚜껑
                Edge(-0.16f, -0.26f, 0.16f, -0.18f, oil ? MxInk : new Color(0.85f, 0.9f, 1f), 1.2f); // 손잡이
                if (oil) { ci.DrawLine(P(-0.15f, -0.1f), P(0.15f, 0.2f), MxInk, 1.2f, true); ci.DrawLine(P(0.15f, -0.1f), P(-0.15f, 0.2f), MxInk, 1.2f, true); }
                else if (t.Contents > 0f) Box(-0.2f, 0.3f - 0.5f * Mathf.Clamp(t.Contents / t.Spec.Contents, 0f, 1f), 0.2f, 0.28f, new Color(MxWaterHi, 0.35f)); // 물 높이
                if (t.Stage >= BreakStage.Broken) DrawCrack(ci, P, seed);
                break;
            }
            case ArticleKind.PowderSack:
            {
                if (t.Contents <= 0f)
                {
                    ci.DrawColoredPolygon(Poly((-0.3f, -0.12f), (0.3f, -0.15f), (0.32f, 0.14f), (-0.3f, 0.16f)), Tint(MxSack)); // 터진 자루
                    for (int k = 0; k < 5; k++) ci.DrawCircle(P((MH(seed, k, 3) - 0.5f) * 0.8f, (MH(seed, k, 4) - 0.5f) * 0.8f), 0.08f * s, new Color(0.97f, 0.95f, 0.9f, 0.8f), true, -1f, true); // 가루 더미
                }
                else
                {
                    ci.DrawColoredPolygon(Poly((-0.26f, -0.22f), (0.26f, -0.22f), (0.3f, 0.3f), (-0.3f, 0.3f)), Tint(MxSack));
                    ci.DrawColoredPolygon(Poly((-0.08f, -0.22f), (0.08f, -0.22f), (0.04f, -0.34f), (-0.04f, -0.34f)), Tint(MxSack.Darkened(0.15f))); // 묶은 주둥이
                    if (fine) for (int k = 0; k < 6; k++) ci.DrawLine(P(-0.24f + k * 0.09f, 0.27f), P(-0.2f + k * 0.09f, 0.27f), new Color(0.4f, 0.3f, 0.2f, 0.7f), 1f, true); // 박음질
                }
                break;
            }
            case ArticleKind.IceBlock:
            {
                float k0 = Mathf.Clamp(t.Mass / t.Spec.Mass, 0.15f, 1f) * 0.32f;
                ci.DrawColoredPolygon(Poly((-k0, -k0), (k0, -k0 * 0.8f), (k0 * 0.9f, k0), (-k0 * 0.9f, k0 * 0.9f)), new Color(MxIce, 0.7f));
                ci.DrawLine(P(-k0 * 0.5f, -k0 * 0.3f), P(k0 * 0.4f, k0 * 0.5f), new Color(1f, 1f, 1f, 0.6f), 1f, true); // 속 금
                float tw = 0.5f + 0.5f * Mathf.Sin(_time * 4f + t.Id);
                DrawFrostStar(ci, P(k0 * 0.4f, -k0 * 0.5f), 1.5f + tw * 1.5f);
                break;
            }
            case ArticleKind.Toolbox:
            {
                Box(-0.32f, -0.2f, 0.32f, 0.24f, Char(MxTool));
                Edge(-0.14f, -0.32f, 0.14f, -0.2f, new Color(0.15f, 0.15f, 0.17f), 1.6f); // 손잡이
                Box(-0.26f, -0.16f, -0.18f, -0.1f, new Color(0.85f, 0.85f, 0.88f)); // 잠금쇠
                Box(0.18f, -0.16f, 0.26f, -0.1f, new Color(0.85f, 0.85f, 0.88f));
                if (fine) ci.DrawLine(P(-0.2f, 0.08f), P(0.18f, 0.08f), new Color(0.3f, 0.05f, 0.05f), 1f, true);
                float oil = t.Soil[(int)SoilKind.Oil], bio = t.Soil[(int)SoilKind.Bio];
                for (int k = 0; k < (int)(oil * 6f); k++) ci.DrawCircle(P((MH(seed, k, 5) - 0.5f) * 0.55f, (MH(seed, k, 6) - 0.5f) * 0.35f), 1.3f, new Color(0.1f, 0.08f, 0.04f, 0.6f), true, -1f, true); // 손때
                for (int k = 0; k < (int)(bio * 8f); k++) ci.DrawCircle(P((MH(seed, k, 7) - 0.5f) * 0.55f, (MH(seed, k, 8) - 0.5f) * 0.35f), 0.9f, new Color(0.45f, 0.85f, 0.3f, 0.75f), true, -1f, true); // 균
                break;
            }
            case ArticleKind.FoodCrate:
            {
                Box(-0.3f, -0.26f, 0.3f, 0.26f, Tint(MxWood));
                for (int k = 0; k < 3; k++) Box(-0.3f, -0.2f + k * 0.17f, 0.3f, -0.16f + k * 0.17f, Tint(MxWood.Darkened(0.25f))); // 살
                ci.DrawCircle(P(-0.12f, -0.27f), 0.06f * s, t.Ruined ? new Color(0.35f, 0.45f, 0.2f) : new Color(0.85f, 0.3f, 0.2f), true, -1f, true);
                ci.DrawCircle(P(0.1f, -0.27f), 0.06f * s, t.Ruined ? new Color(0.35f, 0.45f, 0.2f) : new Color(0.4f, 0.75f, 0.3f), true, -1f, true);
                if (t.Ruined && fine) for (int k = 0; k < 2; k++) ci.DrawCircle(P(Mathf.Sin(_time * 3f + k * 3f) * 0.25f, -0.4f + Mathf.Cos(_time * 4f + k) * 0.06f), 0.8f, MxInk, true, -1f, true); // 파리
                break;
            }
            case ArticleKind.Radio:
            {
                Box(-0.1f, -0.18f, 0.1f, 0.2f, MxRadio);
                ci.DrawLine(P(0.06f, -0.18f), P(0.08f, -0.4f), MxRadio.Lightened(0.3f), 1.4f, true); // 안테나
                bool bad = t.Ruined;
                float blink = bad ? (Mathf.PosMod(_time * 3f, 1f) < 0.5f ? 1f : 0.2f) : 1f;
                ci.DrawCircle(P(-0.04f, -0.12f), 1.4f, bad ? new Color(1f, 0.25f, 0.2f, blink) : new Color(0.3f, 1f, 0.45f), true, -1f, true);
                if (fine) for (int k = 0; k < 3; k++) ci.DrawLine(P(-0.07f, -0.02f + k * 0.06f), P(0.07f, -0.02f + k * 0.06f), MxRadio.Lightened(0.2f), 1f, true); // 스피커 망
                if (bad && Mathf.PosMod(_time * 2.3f + t.Id, 1f) < 0.12f) ci.DrawLine(P(0.1f, 0f), P(0.18f, -0.06f), MxSpark, 1.2f, true); // 합선 불꽃
                break;
            }
            case ArticleKind.Shards:
            {
                // 조각: 재질마다 — 유리는 반짝이는 날카로운 삼각형 · 사기는 흰 조각 · 플라스틱은 색 조각 · 얼음은 녹는 결정
                var col = t.Mat switch { Mat.Glass => new Color(MxGlass, 0.85f), Mat.Ceramic => MxCeramic, Mat.Ice => new Color(MxIce, 0.8f), _ => MxCrate.Lightened(0.2f) };
                for (int k = 0; k < 7; k++)
                {
                    var c0 = P((MH(seed, k, 9) - 0.5f) * 0.8f, (MH(seed, k, 10) - 0.5f) * 0.8f);
                    float a = MH(seed, k, 11) * Mathf.Tau, r = 2f + MH(seed, k, 12) * 2.5f;
                    var tri = new[] { c0 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, c0 + new Vector2(Mathf.Cos(a + 2.3f), Mathf.Sin(a + 2.3f)) * r * 0.6f, c0 + new Vector2(Mathf.Cos(a + 4f), Mathf.Sin(a + 4f)) * r * 0.8f };
                    ci.DrawColoredPolygon(tri, col);
                    if (Matter.Sharp(t.Mat) && fine && Mathf.PosMod(_time * 0.7f + MH(seed, k, 13), 1f) < 0.12f) DrawFrostStar(ci, tri[0], 2.2f); // 반짝
                }
                break;
            }
        }
        // 공통 상태: 녹음 · 연기 · 서리 · 컴퓨터가 짚음
        if (t.Smolder && t.CarriedBy < 0) DrawSmolder(ci, at, seed, t.Char);
        if (t.Temp < -2f && t.Mat != Mat.Ice && fine) DrawFrostStar(ci, at + new Vector2(s * 0.2f, -s * 0.2f), 2f);
        if (t.Flagged) DrawComputerTag(ci, at + new Vector2(0, -s * 0.55f));
    }

    private bool NearLive(Cell c)
    {
        var m = _world.Matter;
        foreach (var d in Cell.Dirs8) if (m.LiveAt(c + d) > 0.3f) return true;
        return false;
    }

    private static void DrawCharSpots(CanvasItem ci, Func<float, float, Vector2> P, Article t, int seed)
    {
        int n = 2 + (int)(t.Char * 6f);
        for (int k = 0; k < n; k++)
            ci.DrawCircle(P((MH(seed, k, 14) - 0.5f) * 0.6f, (MH(seed, k, 15) - 0.5f) * 0.45f), 1.6f + 3f * t.Char * MH(seed, k, 16), new Color(0.1f, 0.06f, 0.03f, 0.35f + 0.5f * t.Char), true, -1f, true);
    }

    private static void DrawCrack(CanvasItem ci, Func<float, float, Vector2> P, int seed)
    {
        var pts = new Vector2[5];
        for (int k = 0; k < 5; k++) pts[k] = P(-0.15f + k * 0.075f, (MH(seed, k, 17) - 0.5f) * 0.2f);
        ci.DrawPolyline(pts, new Color(0.1f, 0.12f, 0.15f, 0.9f), 1f, true);
        ci.DrawLine(pts[2], P(0.02f, 0.16f), new Color(0.1f, 0.12f, 0.15f, 0.8f), 0.8f, true);
    }

    private void DrawMelted(CanvasItem ci, Func<float, float, Vector2> P, Article t, Color c, int seed)
    {
        // 녹아 흘러내린 덩어리 · 방울 · 연둣빛 유독 연기
        var pts = new Vector2[10];
        for (int k = 0; k < 10; k++)
        {
            float a = k * Mathf.Tau / 10f;
            float r = 0.22f + 0.12f * MH(seed, k, 18) + (Mathf.Sin(a) > 0.3f ? 0.12f * t.Melt : 0f);
            pts[k] = P(Mathf.Cos(a) * r * 1.3f, Mathf.Sin(a) * r);
        }
        ci.DrawColoredPolygon(pts, c.Darkened(0.25f));
        for (int k = 0; k < 3; k++) ci.DrawCircle(P(-0.2f + k * 0.2f, 0.32f + 0.05f * Mathf.Sin(_time + k)), 1.6f, c.Darkened(0.35f), true, -1f, true);
        for (int k = 0; k < 3; k++)
        {
            float ph = Mathf.PosMod(_time * 0.4f + k * 0.33f + seed * 0.01f, 1f);
            ci.DrawCircle(P(-0.1f + k * 0.1f + Mathf.Sin(_time + k) * 0.05f, -0.2f - ph * 0.6f), 2f + 3f * ph, new Color(MxToxic, 0.35f * (1f - ph)), true, -1f, true);
        }
    }

    private void DrawSteam(CanvasItem ci, Vector2 at, int seed, float wet)
    {
        for (int k = 0; k < 3; k++)
        {
            float ph = Mathf.PosMod(_time * 0.6f + k * 0.33f + seed * 0.01f, 1f);
            var p = at + new Vector2(-5 + k * 5 + Mathf.Sin(_time * 2f + k) * 1.5f, -ph * 12f);
            ci.DrawArc(p, 2.2f, 0f, Mathf.Pi, 6, new Color(1f, 1f, 1f, 0.45f * (1f - ph) * wet), 1f, true);
        }
    }

    private void DrawSmolder(CanvasItem ci, Vector2 at, int seed, float charV)
    {
        // 숨 쉬는 불씨 · 잿빛 연기 덩어리가 오른다
        float pulse = 0.55f + 0.45f * Mathf.Sin(_time * 5f + seed);
        ci.DrawCircle(at + new Vector2(2, 1), 2.5f + 1.5f * pulse, new Color(MxEmber, 0.35f + 0.4f * pulse), true, -1f, true);
        ci.DrawCircle(at + new Vector2(2, 1), 1.2f, new Color(1f, 0.85f, 0.4f, 0.8f * pulse), true, -1f, true);
        for (int k = 0; k < 4; k++)
        {
            float ph = Mathf.PosMod(_time * 0.35f + k * 0.25f + seed * 0.013f, 1f);
            var p = at + new Vector2(Mathf.Sin(_time * 1.3f + k * 1.7f) * 4f + ph * 3f, -4f - ph * 22f);
            ci.DrawCircle(p, 2.5f + 5f * ph, new Color(MxSmoke, (0.45f + 0.3f * charV) * (1f - ph)), true, -1f, true);
        }
    }
}
