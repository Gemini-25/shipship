using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;
using Mat = ShipSim.Core.Material;

namespace ShipSim.View;

// v16.3 배 본체 그리기 (읽기만): 바닥재 5종 무늬 · 점검 뚜껑 · 칸 상태(물기 · 기름 · 서리 · 유리 · 그을음 · 테이프) · 닳은 길 ·
// 뗀 벽 패널(속 배선) · 얇은 칸막이 · 상한 단열재 · 관측창(덮개) · 문 잠금 장치(카드 · 지문 · 열쇠 · 함장 승인) · 문 상태 ·
// 벽 장착물(소화기 · 마스크함 · 손전등 · 게시판) · 비상 유도선 · 대피실.
// 바닥 무늬는 정적 층에 한 번 (재질마다 선을 모아 DrawMultiline 한 번씩), 움직이는 것만 동적 층에.
public partial class ShipView
{
    private static float BH(int x, int y, int k) => Hash(x, y, k);

    // ───────────────────────────── 정적: 바닥재 무늬 · 뚜껑 자리 · 칸막이 · 유도선 ─────────────────────────────

    private sealed class LineBatch
    {
        public readonly List<Vector2> Pts = new();
        public void Add(Vector2 a, Vector2 b) { Pts.Add(a); Pts.Add(b); }
        public void Draw(CanvasItem ci, Color c, float width) { if (Pts.Count >= 2) ci.DrawMultiline(Pts.ToArray(), c, width); }
    }

    private void PaintBodyFloors(CanvasItem ci)
    {
        var ship = _world.Ship;
        var body = _world.Body;
        var g = ship.Grid;
        // 재질마다 선 묶음 (한 번에 그린다)
        var grateHole = new LineBatch(); var grateBar = new LineBatch();
        var studLo = new LineBatch(); var studHi = new LineBatch();
        var grout = new LineBatch(); var groutHi = new LineBatch();
        var fiberA = new LineBatch(); var fiberB = new LineBatch();
        var bevelHi = new LineBatch(); var bevelLo = new LineBatch(); var rivet = new LineBatch(); var tread = new LineBatch();
        var hatch = new LineBatch(); var hatchScrew = new LineBatch();
        var guide = new LineBatch(); var guideEdge = new LineBatch();
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            if (g.Kind(c) != TileKind.Floor) continue;
            var room = ship.RoomAt(c);
            if (room == null || room.Detached) continue;
            var m = body.Floor[i];
            var r = CellRect(c);
            var p = r.Position;
            var tint = new Color((uint)(Materials.Of(m).Tint << 8 | 0xff));
            switch (m)
            {
                case Mat.Grate:
                {
                    // 격자: 어두운 구멍 사이로 가는 살 — 구멍 아래로 배관 그림자
                    ci.DrawRect(r.Grow(-1f), new Color(0.04f, 0.05f, 0.06f, 0.55f));
                    for (int k = 1; k < 6; k++)
                    {
                        float o = k * T / 6f;
                        grateBar.Add(new Vector2(p.X + o, p.Y + 1), new Vector2(p.X + o, p.Y + T - 1));
                        grateBar.Add(new Vector2(p.X + 1, p.Y + o), new Vector2(p.X + T - 1, p.Y + o));
                    }
                    grateHole.Add(new Vector2(p.X + 2, p.Y + T * 0.5f + 3), new Vector2(p.X + T - 2, p.Y + T * 0.5f + 3)); // 아래 배관 그림자
                    break;
                }
                case Mat.Rubber:
                {
                    // 고무: 엇갈린 둥근 돌기 (그림자 + 빛)
                    ci.DrawRect(r, tint.WithAlpha(0.35f));
                    for (int yy = 0; yy < 4; yy++)
                    for (int xx = 0; xx < 4; xx++)
                    {
                        float sx = p.X + 4 + xx * 8 + (yy % 2 == 0 ? 0 : 4), sy = p.Y + 4 + yy * 8;
                        if (sx > p.X + T - 3) continue;
                        studLo.Add(new Vector2(sx, sy + 1.2f), new Vector2(sx + 2.6f, sy + 1.2f));
                        studHi.Add(new Vector2(sx - 0.4f, sy - 0.3f), new Vector2(sx + 1.4f, sy - 0.3f));
                    }
                    break;
                }
                case Mat.Tile:
                {
                    // 타일: 칸마다 2×2 장, 장마다 살짝 다른 결 · 줄눈
                    for (int yy = 0; yy < 2; yy++)
                    for (int xx = 0; xx < 2; xx++)
                    {
                        float v = BH(c.X * 2 + xx, c.Y * 2 + yy, 11);
                        ci.DrawRect(new Rect2(p.X + xx * 16 + 1, p.Y + yy * 16 + 1, 14, 14), tint.Lightened(0.12f * v).WithAlpha(0.32f));
                    }
                    grout.Add(new Vector2(p.X, p.Y + 16), new Vector2(p.X + T, p.Y + 16));
                    grout.Add(new Vector2(p.X + 16, p.Y), new Vector2(p.X + 16, p.Y + T));
                    grout.Add(new Vector2(p.X, p.Y + 0.5f), new Vector2(p.X + T, p.Y + 0.5f));
                    grout.Add(new Vector2(p.X + 0.5f, p.Y), new Vector2(p.X + 0.5f, p.Y + T));
                    groutHi.Add(new Vector2(p.X + 2, p.Y + 2), new Vector2(p.X + 9, p.Y + 2));
                    groutHi.Add(new Vector2(p.X + 18, p.Y + 18), new Vector2(p.X + 25, p.Y + 18));
                    break;
                }
                case Mat.Carpet:
                {
                    // 카펫: 부드러운 바탕에 짧은 결 (칸마다 방향이 엇갈린다)
                    ci.DrawRect(r, tint.WithAlpha(0.45f));
                    bool alt = (c.X + c.Y) % 2 == 0;
                    var batch = alt ? fiberA : fiberB;
                    for (int k = 0; k < 14; k++)
                    {
                        float fx = p.X + 2 + BH(c.X, c.Y, k) * (T - 6), fy = p.Y + 2 + BH(c.Y, c.X, k + 20) * (T - 6);
                        batch.Add(new Vector2(fx, fy), alt ? new Vector2(fx + 3, fy + 2.5f) : new Vector2(fx + 3, fy - 2.5f));
                    }
                    break;
                }
                case Mat.MetalPlate:
                {
                    // 금속판: 모서리 빗각 · 귀퉁이 리벳 · 마름모 미끄럼 방지 돌기
                    bevelHi.Add(new Vector2(p.X + 1, p.Y + 1), new Vector2(p.X + T - 1, p.Y + 1));
                    bevelHi.Add(new Vector2(p.X + 1, p.Y + 1), new Vector2(p.X + 1, p.Y + T - 1));
                    bevelLo.Add(new Vector2(p.X + 1, p.Y + T - 1), new Vector2(p.X + T - 1, p.Y + T - 1));
                    bevelLo.Add(new Vector2(p.X + T - 1, p.Y + 1), new Vector2(p.X + T - 1, p.Y + T - 1));
                    foreach (var (rx, ry) in new[] { (4f, 4f), (T - 5f, 4f), (4f, T - 5f), (T - 5f, T - 5f) })
                        rivet.Add(new Vector2(p.X + rx, p.Y + ry), new Vector2(p.X + rx + 1.6f, p.Y + ry));
                    for (int yy = 0; yy < 3; yy++)
                    for (int xx = 0; xx < 3; xx++)
                    {
                        float tx = p.X + 8 + xx * 8, ty = p.Y + 8 + yy * 8;
                        if ((xx + yy) % 2 == 0) tread.Add(new Vector2(tx - 2, ty + 1.5f), new Vector2(tx + 2, ty - 1.5f));
                        else tread.Add(new Vector2(tx - 2, ty - 1.5f), new Vector2(tx + 2, ty + 1.5f));
                    }
                    break;
                }
            }
            // 점검 뚜껑 자리: 바닥에 사각 테두리 · 나사 넷 · 손잡이 홈
            if ((body.Under[i] & UnderFlags.HatchSpot) != 0)
            {
                float a = 5f, b2 = T - 5f;
                hatch.Add(new Vector2(p.X + a, p.Y + a), new Vector2(p.X + b2, p.Y + a));
                hatch.Add(new Vector2(p.X + b2, p.Y + a), new Vector2(p.X + b2, p.Y + b2));
                hatch.Add(new Vector2(p.X + b2, p.Y + b2), new Vector2(p.X + a, p.Y + b2));
                hatch.Add(new Vector2(p.X + a, p.Y + b2), new Vector2(p.X + a, p.Y + a));
                hatch.Add(new Vector2(p.X + T * 0.5f - 4, p.Y + T * 0.5f), new Vector2(p.X + T * 0.5f + 4, p.Y + T * 0.5f));
                foreach (var (sx, sy) in new[] { (a + 2.5f, a + 2.5f), (b2 - 3.5f, a + 2.5f), (a + 2.5f, b2 - 3.5f), (b2 - 3.5f, b2 - 3.5f) })
                    hatchScrew.Add(new Vector2(p.X + sx, p.Y + sy), new Vector2(p.X + sx + 1.2f, p.Y + sy));
            }
            // 비상 유도선: 야광 띠 (가운데) + 가장자리
            if (body.Guide[i])
            {
                bool horiz = ship.RoomAt(c + new Cell(1, 0)) == room && body.Guide[g.Index(c + new Cell(1, 0))] || ship.RoomAt(c + new Cell(-1, 0)) == room && body.Guide[g.Index(c + new Cell(-1, 0))];
                if (horiz) { guide.Add(new Vector2(p.X, p.Y + T * 0.5f), new Vector2(p.X + T, p.Y + T * 0.5f)); guideEdge.Add(new Vector2(p.X, p.Y + T * 0.5f - 2.5f), new Vector2(p.X + T, p.Y + T * 0.5f - 2.5f)); }
                else { guide.Add(new Vector2(p.X + T * 0.5f, p.Y), new Vector2(p.X + T * 0.5f, p.Y + T)); guideEdge.Add(new Vector2(p.X + T * 0.5f - 2.5f, p.Y), new Vector2(p.X + T * 0.5f - 2.5f, p.Y + T)); }
            }
        }
        grateHole.Draw(ci, new Color(0.25f, 0.42f, 0.5f, 0.35f), 3f);
        grateBar.Draw(ci, new Color(0.55f, 0.62f, 0.68f, 0.85f), 1.6f);
        studLo.Draw(ci, new Color(0f, 0f, 0f, 0.45f), 2.6f);
        studHi.Draw(ci, new Color(0.62f, 0.66f, 0.58f, 0.55f), 1.6f);
        grout.Draw(ci, new Color(0.32f, 0.36f, 0.4f, 0.75f), 1.2f);
        groutHi.Draw(ci, new Color(1f, 1f, 1f, 0.18f), 1f);
        fiberA.Draw(ci, new Color(0.62f, 0.45f, 0.55f, 0.45f), 1f);
        fiberB.Draw(ci, new Color(0.35f, 0.22f, 0.3f, 0.45f), 1f);
        bevelHi.Draw(ci, new Color(0.8f, 0.85f, 0.9f, 0.3f), 1.2f);
        bevelLo.Draw(ci, new Color(0f, 0f, 0f, 0.45f), 1.2f);
        rivet.Draw(ci, new Color(0.78f, 0.82f, 0.86f, 0.75f), 1.8f);
        tread.Draw(ci, new Color(0.65f, 0.7f, 0.76f, 0.35f), 1.4f);
        hatch.Draw(ci, new Color(0.1f, 0.12f, 0.14f, 0.7f), 1.4f);
        hatchScrew.Draw(ci, new Color(0.75f, 0.78f, 0.8f, 0.7f), 1.6f);
        guideEdge.Draw(ci, new Color(0.1f, 0.35f, 0.12f, 0.35f), 1f);
        guide.Draw(ci, new Color(0.45f, 0.95f, 0.5f, 0.32f), 2.2f);

        // 얇은 칸막이: 밝은 판 두 장 사이 이음선 (두꺼운 벽과 실루엣이 다르다)
        foreach (var wb in body.WallList)
        {
            if (!wb.Thin) continue;
            var r = CellRect(wb.Cell);
            ci.DrawRect(r.Grow(-6f), new Color(0.66f, 0.66f, 0.62f, 0.85f));
            bool vert = ship.RoomAt(wb.Cell + new Cell(1, 0)) != null && ship.RoomAt(wb.Cell + new Cell(-1, 0)) != null;
            for (int k = 0; k < 3; k++)
            {
                if (vert) ci.DrawLine(new Vector2(r.GetCenter().X, r.Position.Y + 4 + k * 9), new Vector2(r.GetCenter().X, r.Position.Y + 9 + k * 9), new Color(0.35f, 0.35f, 0.33f, 0.8f), 1f);
                else ci.DrawLine(new Vector2(r.Position.X + 4 + k * 9, r.GetCenter().Y), new Vector2(r.Position.X + 9 + k * 9, r.GetCenter().Y), new Color(0.35f, 0.35f, 0.33f, 0.8f), 1f);
            }
        }
    }

    // ───────────────────────────── 동적: 상태 · 뚜껑 · 패널 · 창 · 문 · 장착물 ─────────────────────────────

    private void PaintBody(CanvasItem ci)
    {
        var w = _world;
        var body = w.Body;
        var ship = w.Ship;
        var g = ship.Grid;
        float zoom = Zoom;
        bool detail = zoom > 0.9f;

        // 닳은 길: 카펫은 눌려 어둡고, 타일 · 금속판은 반들반들 밝다 (재질 마찰이 바뀐 만큼)
        var wear = body.Wear;
        for (int i = 0; i < wear.Length; i++)
        {
            float v = wear[i];
            if (v < 0.03f) continue;
            var c = g.CellAt(i);
            var r = CellRect(c);
            var m = body.Floor[i];
            if (m == Mat.Carpet)
            {
                ci.DrawRect(r.Grow(-3f), new Color(0.12f, 0.06f, 0.1f, 0.35f * v));
                if (detail) ci.DrawLine(r.Position + new Vector2(6, T * 0.5f), r.End - new Vector2(6, T * 0.5f), new Color(0.2f, 0.12f, 0.16f, 0.4f * v), 3f);
            }
            else if (m == Mat.Grate) ci.DrawRect(r.Grow(-4f), new Color(0.8f, 0.85f, 0.9f, 0.12f * v)); // 살 끝이 닳아 반짝
            else
            {
                ci.DrawRect(r.Grow(-2f), new Color(1f, 1f, 1f, 0.16f * v));
                if (detail)
                    for (int k = 0; k < 3; k++)
                    {
                        var a = r.Position + new Vector2(5 + BH(c.X, c.Y, k) * 20, 5 + BH(c.Y, c.X, k) * 20);
                        ci.DrawLine(a, a + new Vector2(5, 1.5f), new Color(0.15f, 0.15f, 0.15f, 0.35f * v), 1f); // 신발 자국 긁힘
                    }
            }
        }

        // 칸 상태
        foreach (var (i, s) in body.Marks)
        {
            var c = g.CellAt(i);
            var r = CellRect(c);
            var ctr = r.GetCenter();
            float wet = s.V[(int)CellMark.Wet], oil = s.V[(int)CellMark.Oil], frost = s.V[(int)CellMark.Frost], glass = s.V[(int)CellMark.Glass],
                soot = s.V[(int)CellMark.Soot], tape = s.V[(int)CellMark.Tape];
            if (soot > 0.05f)
            {
                // 그을음: 결 따라 번진 검댕 + 고운 알갱이
                ci.DrawRect(r.Grow(-1f), new Color(0.05f, 0.04f, 0.035f, 0.25f * soot));
                for (int k = 0; k < 7; k++)
                    ci.DrawRect(new Rect2(r.Position + new Vector2(BH(c.X, c.Y, k + 40) * (T - 3), BH(c.Y, c.X, k + 41) * (T - 3)), new Vector2(2, 2)), new Color(0.02f, 0.02f, 0.02f, 0.6f * soot));
            }
            string? wetWhy = wet > 0.05f ? s.Cause[(int)CellMark.Wet] : null;
            if (wet > 0.05f && wetWhy is "엎지른 국" or "쏟은 음식")
            {
                // 엎지른 국 · 쏟은 음식: 주황갈색 얼룩 + 건더기 · 갓 쏟았으면 김이 오른다
                var pts = new Vector2[7];
                for (int k = 0; k < 7; k++)
                {
                    float ang = k * Mathf.Tau / 7f + BH(c.X, c.Y, 140);
                    pts[k] = ctr + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * T * (0.2f + 0.16f * BH(c.X, c.Y, k + 141)) * (0.5f + 0.5f * wet);
                }
                ci.DrawColoredPolygon(pts, new Color(0.72f, 0.42f, 0.16f, 0.55f + 0.3f * wet));
                for (int k = 0; k < 4; k++)
                {
                    var bp = ctr + new Vector2(BH(c.X, c.Y, k + 150) - 0.5f, BH(c.Y, c.X, k + 151) - 0.5f) * T * 0.4f * wet;
                    ci.DrawRect(new Rect2(bp, new Vector2(2.2f, 1.6f)), k % 2 == 0 ? new Color(0.35f, 0.7f, 0.25f, 0.9f) : new Color(0.95f, 0.85f, 0.6f, 0.9f)); // 건더기
                }
                if (detail && w.Tick - s.Since[(int)CellMark.Wet] < SimTime.Minutes(20))
                    for (int k = 0; k < 2; k++)
                    {
                        float ph = Mathf.PosMod(_time * 0.6f + k * 0.5f, 1f);
                        var sp = ctr + new Vector2(-3 + k * 6 + Mathf.Sin(_time * 2f + k) * 1.5f, -ph * 12f);
                        ci.DrawArc(sp, 2f, 0f, Mathf.Pi, 6, new Color(1f, 1f, 1f, 0.35f * (1f - ph)), 1f, true); // 김
                    }
            }
            else if (wet > 0.05f && wetWhy == "결로")
            {
                // 결로: 차가운 벽 쪽 가장자리에 맺혀 흘러내린 물방울 줄
                ci.DrawRect(r.Grow(-1f), new Color(0.45f, 0.65f, 0.85f, 0.12f + 0.15f * wet));
                for (int k = 0; k < 6; k++)
                {
                    float x = r.Position.X + 3 + k * 5f, len = 3f + 7f * BH(c.X + k, c.Y, 160) * wet;
                    ci.DrawLine(new Vector2(x, r.Position.Y + 1), new Vector2(x, r.Position.Y + 1 + len), new Color(0.75f, 0.9f, 1f, 0.6f), 1f, true);
                    ci.DrawCircle(new Vector2(x, r.Position.Y + 1.5f + len), 1.2f, new Color(0.85f, 0.95f, 1f, 0.8f), true, -1f, true);
                }
            }
            else if (wet > 0.05f)
            {
                // 물기: 반투명 막 + 천천히 미끄러지는 반사 줄 + 물방울
                ci.DrawRect(r.Grow(-1f), new Color(0.35f, 0.6f, 0.95f, 0.18f + 0.22f * wet));
                float ph = Mathf.PosMod(_time * 0.35f + BH(c.X, c.Y, 3), 1f);
                float y = r.Position.Y + 4 + ph * (T - 8);
                ci.DrawLine(new Vector2(r.Position.X + 4 + ph * 6, y), new Vector2(r.End.X - 6, y - 4), new Color(0.9f, 0.97f, 1f, 0.35f * wet), 1.5f, true);
                if (detail)
                    for (int k = 0; k < 2; k++)
                    {
                        var dp = r.Position + new Vector2(6 + BH(c.X, c.Y, k + 5) * 20, 6 + BH(c.Y, c.X, k + 6) * 20);
                        ci.DrawCircle(dp, 1.6f, new Color(0.8f, 0.92f, 1f, 0.55f * wet), true, -1f, true);
                    }
            }
            if (wet > 0.3f && body.Floor[i] == Mat.Carpet && w.Tick - s.Since[(int)CellMark.Wet] > SimTime.Hours(3))
            {
                // 곰팡이: 오래 젖은 카펫에 회녹색 솜털 반점 (가장자리가 번진다)
                for (int k = 0; k < 4; k++)
                {
                    var mp = r.Position + new Vector2(5 + BH(c.X, c.Y, k + 170) * 22, 5 + BH(c.Y, c.X, k + 171) * 22);
                    float rad = 2f + 2.5f * BH(c.X, c.Y, k + 172);
                    ci.DrawCircle(mp, rad, new Color(0.32f, 0.4f, 0.28f, 0.55f), true, -1f, true);
                    if (detail) ci.DrawArc(mp, rad + 1.2f, 0f, Mathf.Tau, 10, new Color(0.62f, 0.7f, 0.55f, 0.45f), 0.8f, true);
                }
            }
            if (oil > 0.05f && s.Cause[(int)CellMark.Oil] == "조리 기름 튐")
            {
                // 조리 기름 튐: 웅덩이가 아니라 화구 쪽으로 흩뿌린 노르스름한 방울 + 번들거림
                for (int k = 0; k < 9; k++)
                {
                    var op = r.Position + new Vector2(3 + BH(c.X, c.Y, k + 180) * 26, 3 + BH(c.Y, c.X, k + 181) * 14);
                    float rad = 0.8f + 1.6f * BH(c.X, c.Y, k + 182) * oil;
                    ci.DrawCircle(op, rad, new Color(0.78f, 0.62f, 0.2f, 0.75f), true, -1f, true);
                    if (rad > 1.4f) ci.DrawCircle(op - new Vector2(0.5f, 0.5f), 0.5f, new Color(1f, 1f, 0.85f, 0.9f), true, -1f, true);
                }
            }
            else if (oil > 0.05f)
            {
                // 기름: 고르지 않은 검갈색 웅덩이 + 무지개 막이 일렁인다
                var pts = new Vector2[8];
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * Mathf.Tau / 8f;
                    float rad = T * (0.22f + 0.18f * BH(c.X, c.Y, k + 60)) * (0.6f + 0.4f * oil);
                    pts[k] = ctr + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                }
                ci.DrawColoredPolygon(pts, new Color(0.12f, 0.08f, 0.04f, 0.75f * oil));
                float sh = _time * 0.8f + c.X;
                ci.DrawArc(ctr + new Vector2(-2, -1), T * 0.16f, sh, sh + 2.2f, 10, new Color(0.85f, 0.3f, 0.85f, 0.5f * oil), 1.2f, true);
                ci.DrawArc(ctr + new Vector2(1, 1), T * 0.12f, sh + 2f, sh + 4.2f, 10, new Color(0.3f, 0.9f, 0.55f, 0.5f * oil), 1.2f, true);
                ci.DrawArc(ctr + new Vector2(2, -2), T * 0.08f, sh + 4f, sh + 6f, 8, new Color(0.35f, 0.6f, 1f, 0.5f * oil), 1.2f, true);
            }
            if (frost > 0.05f)
            {
                // 서리: 희뿌연 막 + 여섯 갈래 결정이 반짝인다
                ci.DrawRect(r.Grow(-1f), new Color(0.88f, 0.95f, 1f, 0.2f + 0.3f * frost));
                int n = detail ? 4 : 2;
                for (int k = 0; k < n; k++)
                {
                    var cp = r.Position + new Vector2(5 + BH(c.X, c.Y, k + 80) * 22, 5 + BH(c.Y, c.X, k + 81) * 22);
                    float len = 2.5f + 2.5f * frost;
                    float tw = 0.6f + 0.4f * Mathf.Sin(_time * 3f + k + c.X);
                    for (int a = 0; a < 3; a++)
                    {
                        var d = new Vector2(Mathf.Cos(a * Mathf.Pi / 3f), Mathf.Sin(a * Mathf.Pi / 3f)) * len;
                        ci.DrawLine(cp - d, cp + d, new Color(1f, 1f, 1f, 0.8f * tw * frost), 1f, true);
                    }
                }
            }
            if (glass > 0.05f)
            {
                // 유리 조각: 흩어진 작은 삼각 조각이 빛을 받아 반짝인다
                int n = 3 + (int)(glass * 5);
                for (int k = 0; k < n; k++)
                {
                    var gp = r.Position + new Vector2(4 + BH(c.X, c.Y, k + 100) * 24, 4 + BH(c.Y, c.X, k + 101) * 24);
                    float a0 = BH(c.X, c.Y, k + 102) * Mathf.Tau;
                    var tri = new[] { gp + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 3.5f, gp + new Vector2(Mathf.Cos(a0 + 2.3f), Mathf.Sin(a0 + 2.3f)) * 2.5f, gp + new Vector2(Mathf.Cos(a0 + 4.1f), Mathf.Sin(a0 + 4.1f)) * 2f };
                    ci.DrawColoredPolygon(tri, new Color(0.7f, 0.9f, 1f, 0.55f));
                    float glint = Mathf.Max(0f, Mathf.Sin(_time * 4f + k * 1.7f + c.Y));
                    if (glint > 0.85f) ci.DrawCircle(tri[0], 1.3f, new Color(1, 1, 1, 0.9f), true, -1f, true);
                }
            }
            if (wet > 0.35f && (c.X * 3 + c.Y) % 4 == 0 && ship.RoomAt(c) is Room wr && body.MopRequested(wr))
            {
                // 주 컴퓨터가 바닥에 비춘 "미끄럼 주의" 투영: 노란 삼각 · 깜빡이는 주사선
                float fl = 0.65f + 0.35f * Mathf.Sin(_time * 7f + c.X);
                var tri = new[] { ctr + new Vector2(0, -8), ctr + new Vector2(8, 6), ctr + new Vector2(-8, 6) };
                ci.DrawColoredPolygon(tri, new Color(1f, 0.85f, 0.15f, 0.22f * fl));
                ci.DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0] }, new Color(1f, 0.9f, 0.3f, 0.75f * fl), 1.2f, true);
                ci.DrawLine(ctr + new Vector2(0, -3), ctr + new Vector2(0, 1.5f), new Color(1f, 0.95f, 0.5f, 0.9f * fl), 1.5f);
                ci.DrawCircle(ctr + new Vector2(0, 3.5f), 0.9f, new Color(1f, 0.95f, 0.5f, 0.9f * fl), true, -1f, true);
                float scan = Mathf.PosMod(_time * 1.5f, 1f);
                ci.DrawLine(new Vector2(ctr.X - 8, ctr.Y - 8 + scan * 14), new Vector2(ctr.X + 8, ctr.Y - 8 + scan * 14), new Color(0.6f, 0.85f, 1f, 0.3f), 1f);
            }
            if (tape > 0.3f)
            {
                // 테이프 출입 금지선: 노랑 · 검정 빗금 띠가 칸을 두르고 바람에 살짝 떤다
                float flut = Mathf.Sin(_time * 5f + c.X * 1.3f) * 0.8f;
                var band = r.Grow(-2f);
                DrawHazardBand(ci, new Rect2(band.Position.X, band.Position.Y + flut, band.Size.X, 4f));
                DrawHazardBand(ci, new Rect2(band.Position.X, band.End.Y - 4f - flut, band.Size.X, 4f));
                ci.DrawRect(new Rect2(band.Position.X - 1, band.Position.Y, 2, band.Size.Y), new Color(0.4f, 0.4f, 0.42f, 0.9f)); // 기둥
                ci.DrawRect(new Rect2(band.End.X - 1, band.Position.Y, 2, band.Size.Y), new Color(0.4f, 0.4f, 0.42f, 0.9f));
            }
        }

        // 열린 점검 뚜껑: 속(배관 · 배선)이 보이는 구멍 + 들어 올린 뚜껑 · 잊혔으면 주황 경고가 깜빡인다
        foreach (var h in body.Hatches)
        {
            var r = CellRect(h.Cell);
            var pit = r.Grow(-5f);
            ci.DrawRect(pit, new Color(0.02f, 0.025f, 0.03f, 0.95f));
            var u = body.Under[g.Index(h.Cell)];
            if ((u & UnderFlags.Pipe) != 0)
            {
                ci.DrawLine(new Vector2(pit.Position.X, pit.Position.Y + 6), new Vector2(pit.End.X, pit.Position.Y + 6), new Color(0.25f, 0.55f, 0.75f), 3.5f);
                ci.DrawLine(new Vector2(pit.Position.X, pit.Position.Y + 6), new Vector2(pit.End.X, pit.Position.Y + 6), new Color(0.6f, 0.85f, 1f, 0.5f), 1f);
                ci.DrawRect(new Rect2(pit.Position.X + 8, pit.Position.Y + 3, 3, 6), new Color(0.6f, 0.62f, 0.66f)); // 이음쇠
            }
            if ((u & UnderFlags.Wiring) != 0)
                for (int k = 0; k < 3; k++)
                {
                    var col = k == 0 ? new Color(0.9f, 0.3f, 0.2f) : k == 1 ? new Color(0.95f, 0.8f, 0.25f) : new Color(0.3f, 0.5f, 0.95f);
                    float yy = pit.End.Y - 4 - k * 2.5f;
                    ci.DrawPolyline(new[] { new Vector2(pit.Position.X, yy), new Vector2(pit.GetCenter().X - 3, yy - 1.5f), new Vector2(pit.GetCenter().X + 3, yy + 1f), new Vector2(pit.End.X, yy) }, col, 1.2f, true);
                }
            // 들어 올린 뚜껑 (경첩 쪽으로 비스듬히)
            var lid = new[] { new Vector2(r.Position.X + 3, r.Position.Y + 1), new Vector2(r.End.X - 3, r.Position.Y + 1), new Vector2(r.End.X - 6, r.Position.Y - 7), new Vector2(r.Position.X + 6, r.Position.Y - 7) };
            ci.DrawColoredPolygon(lid, new Color(0.5f, 0.55f, 0.6f, 0.95f));
            ci.DrawLine(lid[2], lid[3], new Color(0.85f, 0.88f, 0.92f, 0.8f), 1f);
            ci.DrawRect(new Rect2(r.GetCenter().X - 3, r.Position.Y - 5, 6, 2), new Color(0.2f, 0.22f, 0.25f)); // 손잡이
            if (h.Forgotten)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 4f);
                ci.DrawRect(r.Grow(-1f), new Color(1f, 0.55f, 0.1f, 0.4f + 0.5f * pulse), false, 2f);
            }
            if (h.Flagged) DrawComputerTag(ci, r.End - new Vector2(4, 4));
        }

        foreach (var wb in body.WallList)
        {
            var r = CellRect(wb.Cell);
            Cell inward = default;
            foreach (var d in Cell.Dirs4) if (ship.RoomAt(wb.Cell + d) != null) { inward = d; break; }
            // 관측창: 별이 천천히 흐르는 유리창 · 폭풍이면 덮개(가로 살) · 금 간 창
            if (wb.Window)
            {
                var pane = r.Grow(-4f);
                ci.DrawRect(pane, new Color(0.02f, 0.04f, 0.1f, 1f));
                for (int k = 0; k < 4; k++)
                {
                    float sx = Mathf.PosMod(BH(wb.Cell.X, wb.Cell.Y, k) * pane.Size.X - _time * (1.5f + k), pane.Size.X);
                    ci.DrawRect(new Rect2(pane.Position.X + sx, pane.Position.Y + BH(wb.Cell.Y, wb.Cell.X, k) * pane.Size.Y, 1.2f, 1.2f), new Color(1, 1, 1, 0.5f + 0.4f * BH(k, wb.Cell.X, 9)));
                }
                ci.DrawLine(pane.Position + new Vector2(2, 2), pane.Position + new Vector2(pane.Size.X * 0.5f, 2), new Color(0.6f, 0.8f, 1f, 0.25f), 1f); // 유리 반사
                ci.DrawRect(pane, new Color(0.55f, 0.62f, 0.7f), false, 2f);
                if (ship.WallAt(wb.Cell) is WallState ws && ws.Integrity < 0.85f)
                    ci.DrawPolyline(new[] { pane.Position + new Vector2(3, 4), pane.GetCenter(), pane.End - new Vector2(4, 6), pane.GetCenter() + new Vector2(5, -6) }, new Color(1, 1, 1, 0.6f), 1f, true);
                if (wb.Shutter)
                {
                    ci.DrawRect(pane, new Color(0.3f, 0.33f, 0.36f));
                    for (int k = 1; k < 5; k++) ci.DrawLine(new Vector2(pane.Position.X, pane.Position.Y + k * pane.Size.Y / 5f), new Vector2(pane.End.X, pane.Position.Y + k * pane.Size.Y / 5f), new Color(0.12f, 0.13f, 0.15f), 1f);
                    DrawHazardBand(ci, new Rect2(pane.Position.X, pane.End.Y - 3, pane.Size.X, 3));
                }
            }
            // 상한 단열재: 차가운 벽 안쪽 면에 맺힌 물방울(결로) · 얼면 성에
            if (wb.Hull && wb.Insulation < 0.5f && inward != default)
            {
                var face = r.GetCenter() + new Vector2(inward.X, inward.Y) * (T * 0.5f - 2f);
                var along = new Vector2(Mathf.Abs(inward.Y), Mathf.Abs(inward.X));
                for (int k = -2; k <= 2; k++)
                {
                    var dp = face + along * k * 5f + new Vector2(inward.X, inward.Y) * (Mathf.PosMod(_time * 0.3f + k * 0.37f, 1f) * 3f);
                    ci.DrawCircle(dp, 1.4f, new Color(0.75f, 0.9f, 1f, 0.7f * (1f - wb.Insulation)), true, -1f, true);
                }
            }
            // 뗀 벽 패널: 구멍 속 단열재(노란 지그재그) · 배선 다발 · 떼어 기대 둔 판 · 잊었으면 불꽃이 튄다
            if (wb.PanelOff)
            {
                var hole = r.Grow(-3f);
                ci.DrawRect(hole, new Color(0.08f, 0.07f, 0.06f));
                var ins = new List<Vector2>();
                for (int k = 0; k <= 6; k++) ins.Add(new Vector2(hole.Position.X + k * hole.Size.X / 6f, hole.Position.Y + 4 + (k % 2) * 4));
                ci.DrawPolyline(ins.ToArray(), new Color(0.85f, 0.75f, 0.35f, wb.Insulation), 3f);
                var cols = new[] { new Color(0.9f, 0.25f, 0.2f), new Color(0.2f, 0.45f, 0.95f), new Color(0.95f, 0.85f, 0.3f), new Color(0.3f, 0.8f, 0.4f) };
                for (int k = 0; k < cols.Length; k++)
                {
                    float y0 = hole.Position.Y + 13 + k * 3f;
                    ci.DrawPolyline(new[] { new Vector2(hole.Position.X, y0), new Vector2(hole.GetCenter().X, y0 + (k % 2 == 0 ? 2 : -2)), new Vector2(hole.End.X, y0) }, cols[k], 1.4f, true);
                }
                ci.DrawRect(new Rect2(hole.GetCenter().X - 2, hole.Position.Y + 11, 4, 12), new Color(0.15f, 0.15f, 0.15f)); // 케이블 타이
                if (inward != default)
                {
                    var lean = r.GetCenter() + new Vector2(inward.X, inward.Y) * T * 0.85f + new Vector2(inward.Y, inward.X) * 6f;
                    var pts = new[] { lean + new Vector2(-6, -9), lean + new Vector2(5, -10), lean + new Vector2(6, 9), lean + new Vector2(-5, 10) };
                    ci.DrawColoredPolygon(pts, new Color(0.58f, 0.62f, 0.68f, 0.95f));
                    ci.DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, new Color(0.3f, 0.32f, 0.35f), 1f);
                }
                if (wb.PanelForgot && Mathf.PosMod(_time * 2.7f + wb.Cell.X, 1f) < 0.12f)
                    for (int k = 0; k < 3; k++) ci.DrawLine(hole.GetCenter(), hole.GetCenter() + new Vector2(Mathf.Cos(k * 2.1f + _time * 9f), Mathf.Sin(k * 2.1f + _time * 9f)) * 6f, new Color(1f, 0.9f, 0.4f), 1.2f);
            }
        }

        // 문: 잠금 장치 (방식마다 모양이 다르다) · 휜 문틀 · 휘파람 · 센서 고장 · 표시판 · 열어 둔 선실 문 · 노크
        foreach (var db in body.Doors)
        {
            if (db.Door >= ship.Doors.Count) continue;
            var d = ship.Doors[db.Door];
            if (d.Removed || d.IsExternal) continue;
            var r = CellRect(d.Cell);
            // 통제 구역 바깥쪽 벽에 단다
            Vector2 side = d.ConnectsVertically ? new Vector2(1, 0) : new Vector2(0, 1);
            Vector2 outward = Vector2.Zero;
            if (db.Inner >= 0)
            {
                var dirs = d.ConnectsVertically ? new[] { new Cell(0, -1), new Cell(0, 1) } : new[] { new Cell(-1, 0), new Cell(1, 0) };
                foreach (var dir in dirs) if (ship.RoomAt(d.Cell + dir) is Room rr && rr.Id != db.Inner) outward = new Vector2(dir.X, dir.Y);
            }
            var dev = r.GetCenter() + side * (T * 0.62f) + outward * (T * 0.32f);
            bool engaged = body.Engaged(db, d);
            var led = body.FireReleased && db.Lock != LockKind.None ? (Mathf.PosMod(_time * 2f, 1f) < 0.5f ? new Color(1f, 0.7f, 0.1f) : new Color(0.3f, 0.2f, 0.05f))
                : db.Pass >= 0 ? new Color(1f, 0.75f, 0.2f) : engaged ? new Color(1f, 0.2f, 0.15f) : new Color(0.25f, 0.95f, 0.4f);
            switch (db.Lock)
            {
                case LockKind.Card:
                    Gfx.RoundRect(ci, new Rect2(dev - new Vector2(4, 6), new Vector2(8, 12)), new Color(0.16f, 0.18f, 0.21f), 1.5f, new Color(0.45f, 0.5f, 0.56f));
                    ci.DrawLine(dev + new Vector2(-2.5f, -1), dev + new Vector2(2.5f, -1), new Color(0.02f, 0.02f, 0.02f), 1.5f); // 카드 슬롯
                    ci.DrawRect(new Rect2(dev + new Vector2(-2.5f, 2), new Vector2(5, 2.5f)), new Color(0.2f, 0.35f, 0.5f)); // 작은 화면
                    ci.DrawCircle(dev + new Vector2(0, -4), 1.2f, led, true, -1f, true);
                    break;
                case LockKind.Finger:
                    ci.DrawCircle(dev, 5f, new Color(0.14f, 0.16f, 0.2f), true, -1f, true);
                    for (int k = 1; k <= 3; k++) ci.DrawArc(dev + new Vector2(0, 1), k * 1.2f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 8, new Color(0.5f, 0.75f, 0.95f, 0.8f), 0.8f, true); // 지문 결
                    ci.DrawArc(dev, 5f, 0f, Mathf.Tau, 20, led, 1f, true);
                    break;
                case LockKind.Key:
                    ci.DrawCircle(dev, 4f, new Color(0.72f, 0.58f, 0.25f), true, -1f, true); // 놋쇠 실린더
                    ci.DrawCircle(dev + new Vector2(0, -0.8f), 1.2f, new Color(0.1f, 0.08f, 0.04f), true, -1f, true);
                    ci.DrawRect(new Rect2(dev + new Vector2(-0.5f, -0.5f), new Vector2(1, 3)), new Color(0.1f, 0.08f, 0.04f)); // 열쇠 구멍
                    if (db.OwnerLocked) ci.DrawArc(dev, 5.5f, -0.5f, 0.5f + Mathf.Pi, 12, new Color(1f, 0.3f, 0.2f, 0.8f), 1f, true); // 잠금 걸쇠
                    break;
                case LockKind.Captain:
                    Gfx.RoundRect(ci, new Rect2(dev - new Vector2(6, 5), new Vector2(12, 10)), new Color(0.12f, 0.12f, 0.16f), 2f, new Color(0.8f, 0.65f, 0.25f));
                    ci.DrawPolyline(new[] { dev + new Vector2(-3, 0), dev + new Vector2(0, -2.5f), dev + new Vector2(3, 0) }, new Color(0.95f, 0.8f, 0.3f), 1.2f, true); // 계급장 갈매기
                    ci.DrawPolyline(new[] { dev + new Vector2(-3, 2), dev + new Vector2(0, -0.5f), dev + new Vector2(3, 2) }, new Color(0.95f, 0.8f, 0.3f), 1.2f, true);
                    for (int k = 0; k < 3; k++) ci.DrawLine(dev + new Vector2(-4 + k * 1.5f, 3.5f), dev + new Vector2(-4 + k * 1.5f, 4.5f), new Color(0.5f, 0.5f, 0.55f), 0.8f); // 무전 그릴
                    ci.DrawCircle(dev + new Vector2(4, 3.5f), 1f, led, true, -1f, true);
                    break;
            }
            // 열어 둔 선실 문: 문 옆 초록 "열림" 쪽지
            if (db.HeldOpen) ci.DrawRect(new Rect2(r.GetCenter() - side * (T * 0.62f) - new Vector2(3, 2), new Vector2(6, 4)), new Color(0.4f, 0.9f, 0.5f, 0.85f));
            // 노크: 문에 퍼지는 작은 동그라미 두 겹
            if (db.KnockAt >= 0 && w.Tick - db.KnockAt < 5)
                for (int k = 1; k <= 2; k++) ci.DrawArc(r.GetCenter(), 4f + k * 4f + (w.Tick - db.KnockAt) * 1.5f, 0f, Mathf.Tau, 18, new Color(1f, 1f, 1f, 0.5f / k), 1f, true);
            // 휜 문틀: 비뚤어진 틀 선 · 틈
            if (d.Bent > 0.3f)
            {
                var a = r.Position + new Vector2(2, 2);
                ci.DrawPolyline(new[] { a, a + new Vector2(T * 0.45f, 3f), a + new Vector2(T - 4, -1f) }, new Color(0.9f, 0.5f, 0.2f, 0.9f), 2f, true);
                ci.DrawLine(r.GetCenter() - side * 3f, r.GetCenter() + side * 3f, new Color(0f, 0f, 0f, 0.8f), 2f);
            }
            // 휘파람: 틈으로 빨려 나가는 가는 공기 줄
            if (db.Whistling)
                for (int k = 0; k < 3; k++)
                {
                    float ph = Mathf.PosMod(_time * 2.5f + k * 0.33f, 1f);
                    var o = r.GetCenter() + side * ((k - 1) * 5f);
                    var dir = d.ConnectsVertically ? new Vector2(0, 1) : new Vector2(1, 0);
                    ci.DrawLine(o + dir * (ph * 10f - 5f), o + dir * (ph * 10f), new Color(0.85f, 0.95f, 1f, 0.6f * (1f - ph)), 1f, true);
                }
            // 센서 고장: 문 위 감지 눈에 빗금
            if (db.SensorBroken)
            {
                var eye = r.GetCenter() - side * (T * 0.4f);
                ci.DrawCircle(eye, 2.5f, new Color(0.2f, 0.2f, 0.22f), true, -1f, true);
                ci.DrawLine(eye - new Vector2(2.5f, 2.5f), eye + new Vector2(2.5f, 2.5f), new Color(1f, 0.35f, 0.2f), 1f);
                ci.DrawLine(eye - new Vector2(2.5f, -2.5f), eye + new Vector2(2.5f, -2.5f), new Color(1f, 0.35f, 0.2f), 1f);
            }
            // 반대편 진공 표시판: 고장이면 멈춘 값 · 금
            if (db.IndicatorBroken)
            {
                var pan = r.GetCenter() + side * (T * 0.62f) - outward * (T * 0.3f);
                ci.DrawRect(new Rect2(pan - new Vector2(4, 3), new Vector2(8, 6)), db.IndicatorSaysSafe ? new Color(0.15f, 0.45f, 0.2f) : new Color(0.55f, 0.15f, 0.12f));
                ci.DrawLine(pan + new Vector2(-4, -3), pan + new Vector2(2, 3), new Color(1, 1, 1, 0.7f), 0.8f);
            }
            if (db.Flagged && (db.Gasket < 0.25f || db.IndicatorBroken)) DrawComputerTag(ci, r.GetCenter() - side * (T * 0.62f) + outward * (T * 0.3f));
        }

        // 벽 장착물: 소화기 · 산소 마스크함 · 손전등 · 게시판 — 꺼내면 빈 걸이 (점선 윤곽)
        foreach (var m in body.Mounts)
        {
            var wr = CellRect(m.Wall);
            var dir = new Vector2(m.Spot.X - m.Wall.X, m.Spot.Y - m.Wall.Y);
            var at = wr.GetCenter() + dir * (T * 0.42f);
            var across = new Vector2(Mathf.Abs(dir.Y), Mathf.Abs(dir.X));
            DrawMount(ci, m, at, across, dir);
        }

        // 대피실: 독립 산소통 · 배터리 눈금
        foreach (var room in ship.LiveRooms)
        {
            if (room.Kind != RoomType.Shelter || room.Cells.Count == 0) continue;
            var c0 = CellRect(room.Cells[0]).Position + new Vector2(4, 4);
            Gfx.RoundRect(ci, new Rect2(c0, new Vector2(7, 16)), new Color(0.2f, 0.55f, 0.3f), 3f, new Color(0.8f, 0.9f, 0.8f));
            ci.DrawRect(new Rect2(c0 + new Vector2(2, -2), new Vector2(3, 2)), new Color(0.7f, 0.7f, 0.7f));
            var bat = new Rect2(c0 + new Vector2(11, 4), new Vector2(12, 8));
            ci.DrawRect(bat, new Color(0.1f, 0.1f, 0.1f));
            ci.DrawRect(new Rect2(bat.Position + new Vector2(1, 1), new Vector2(10 * body.ShelterCharge(room), 6)), new Color(0.4f, 0.9f, 0.4f));
            ci.DrawRect(new Rect2(bat.End.X, bat.Position.Y + 2, 1.5f, 4), new Color(0.6f, 0.6f, 0.6f));
        }

        // 비상 유도선: 어둡거나 연기가 차면 빛난다 (흐르는 화살표)
        foreach (var room in ship.LiveRooms)
        {
            if (!(room.Dark || room.Air.Smoke > 0.2f)) continue;
            foreach (var c in room.Cells)
            {
                int i = g.Index(c);
                if (!body.Guide[i]) continue;
                var r = CellRect(c);
                float pulse = 0.55f + 0.45f * Mathf.Sin(_time * 3f - c.X * 0.8f - c.Y * 0.8f);
                ci.DrawRect(r.Grow(-12f), new Color(0.4f, 1f, 0.5f, 0.5f * pulse));
                ci.DrawPolyline(new[] { r.GetCenter() + new Vector2(-4, -3), r.GetCenter() + new Vector2(0, 0), r.GetCenter() + new Vector2(-4, 3) }, new Color(0.7f, 1f, 0.7f, 0.8f * pulse), 1.5f, true);
            }
        }
    }

    /// <summary>주 컴퓨터 정비 요청 꼬리표: 파란 마름모 홀로그램 + 느낌표 + 도는 주사 고리.</summary>
    private void DrawComputerTag(CanvasItem ci, Vector2 at)
    {
        float bob = Mathf.Sin(_time * 2.4f + at.X * 0.1f) * 1.2f;
        var p = at + new Vector2(0, bob);
        var dia = new[] { p + new Vector2(0, -5), p + new Vector2(5, 0), p + new Vector2(0, 5), p + new Vector2(-5, 0) };
        ci.DrawColoredPolygon(dia, new Color(0.15f, 0.45f, 0.95f, 0.45f));
        ci.DrawPolyline(new[] { dia[0], dia[1], dia[2], dia[3], dia[0] }, new Color(0.55f, 0.85f, 1f, 0.95f), 1f, true);
        ci.DrawLine(p + new Vector2(0, -2.6f), p + new Vector2(0, 0.8f), new Color(1f, 1f, 1f, 0.95f), 1.2f);
        ci.DrawCircle(p + new Vector2(0, 2.4f), 0.7f, new Color(1f, 1f, 1f, 0.95f), true, -1f, true);
        float a = _time * 3f;
        ci.DrawArc(p, 7.5f, a, a + 1.6f, 8, new Color(0.5f, 0.8f, 1f, 0.6f), 1f, true);
        ci.DrawArc(p, 7.5f, a + Mathf.Pi, a + Mathf.Pi + 1.6f, 8, new Color(0.5f, 0.8f, 1f, 0.6f), 1f, true);
    }

    private static void DrawHazardBand(CanvasItem ci, Rect2 band)
    {
        ci.DrawRect(band, new Color(0.95f, 0.8f, 0.15f, 0.95f));
        for (float x = band.Position.X - band.Size.Y; x < band.End.X; x += 6f)
        {
            float x0 = Mathf.Max(band.Position.X, x), x1 = Mathf.Min(band.End.X, x + 3f);
            if (x1 <= x0) continue;
            ci.DrawColoredPolygon(new[] { new Vector2(x0, band.End.Y), new Vector2(x1, band.End.Y), new Vector2(Mathf.Min(band.End.X, x1 + band.Size.Y), band.Position.Y), new Vector2(Mathf.Min(band.End.X, x0 + band.Size.Y), band.Position.Y) },
                new Color(0.08f, 0.08f, 0.08f, 0.95f));
        }
    }

    private void DrawMount(CanvasItem ci, WallMount m, Vector2 at, Vector2 across, Vector2 dir)
    {
        var empty = new Color(0.6f, 0.65f, 0.7f, 0.6f);
        switch (m.Kind)
        {
            case MountKind.Extinguisher:
            {
                // 걸이 (고리 둘)
                ci.DrawLine(at - across * 4f, at - across * 4f + dir * 3f, new Color(0.45f, 0.48f, 0.52f), 1.5f);
                ci.DrawLine(at + across * 4f, at + across * 4f + dir * 3f, new Color(0.45f, 0.48f, 0.52f), 1.5f);
                if (m.Present)
                {
                    ci.DrawCircle(at + dir * 2f, 4.2f, new Color(0.85f, 0.12f, 0.1f), true, -1f, true); // 빨간 몸통 (위에서)
                    ci.DrawCircle(at + dir * 2f - across * 1.2f, 1.4f, new Color(1f, 0.6f, 0.55f, 0.8f), true, -1f, true); // 빛
                    ci.DrawLine(at + dir * 2f, at + dir * 2f + across * 6f + dir * 2f, new Color(0.05f, 0.05f, 0.05f), 1.4f); // 호스
                    ci.DrawRect(new Rect2(at + dir * 2f - new Vector2(1, 1), new Vector2(2, 2)), new Color(0.2f, 0.2f, 0.2f)); // 손잡이
                }
                else ci.DrawArc(at + dir * 2f, 4.2f, 0f, Mathf.Tau, 14, empty, 1f, true); // 빈 걸이: 윤곽만
                break;
            }
            case MountKind.OxygenMasks:
            {
                var box = new Rect2(at - new Vector2(5, 5) + dir * 1.5f, new Vector2(10, 10));
                ci.DrawRect(box, new Color(0.95f, 0.78f, 0.15f));
                ci.DrawRect(box, new Color(0.35f, 0.3f, 0.1f), false, 1f);
                if (m.Present)
                {
                    ci.DrawCircle(box.GetCenter() + new Vector2(-1.5f, 0), 1.8f, new Color(0.95f, 0.95f, 0.95f), true, -1f, true); // 마스크
                    ci.DrawCircle(box.GetCenter() + new Vector2(2f, 0.5f), 1.5f, new Color(0.95f, 0.95f, 0.95f), true, -1f, true);
                    ci.DrawRect(new Rect2(box.Position + new Vector2(1, 1), new Vector2(8, 2)), new Color(0.2f, 0.5f, 0.85f)); // O2 띠
                }
                else
                {
                    ci.DrawRect(box.Grow(-2f), new Color(0.15f, 0.12f, 0.05f)); // 빈 함
                    ci.DrawLine(box.Position + new Vector2(10, 0), box.Position + new Vector2(14, -3), new Color(0.95f, 0.78f, 0.15f), 1.5f); // 열린 뚜껑
                }
                break;
            }
            case MountKind.Flashlight:
            {
                ci.DrawLine(at - across * 3f, at + across * 3f, new Color(0.4f, 0.42f, 0.45f), 2f); // 클립
                if (m.Present)
                {
                    ci.DrawLine(at - across * 5f + dir * 2f, at + across * 3f + dir * 2f, new Color(0.15f, 0.15f, 0.15f), 3.5f); // 몸통
                    ci.DrawCircle(at + across * 4f + dir * 2f, 2.4f, new Color(0.95f, 0.85f, 0.3f), true, -1f, true); // 렌즈 머리
                    float blink = Mathf.PosMod(_time * 0.7f + m.Id, 1f) < 0.1f ? 1f : 0.3f;
                    ci.DrawCircle(at - across * 4f + dir * 2f, 0.9f, new Color(0.3f, 1f, 0.4f, blink), true, -1f, true); // 충전 표시
                }
                else ci.DrawLine(at - across * 5f + dir * 2f, at + across * 4f + dir * 2f, empty, 1f);
                break;
            }
            case MountKind.Board:
            {
                var board = new Rect2(at - across * 8f - new Vector2(Mathf.Abs(dir.X), Mathf.Abs(dir.Y)) * 2f, across * 16f + new Vector2(Mathf.Abs(dir.X), Mathf.Abs(dir.Y)) * 5f);
                board = new Rect2(new Vector2(Mathf.Min(board.Position.X, board.End.X), Mathf.Min(board.Position.Y, board.End.Y)), new Vector2(Mathf.Max(4, Mathf.Abs(board.Size.X)), Mathf.Max(4, Mathf.Abs(board.Size.Y))));
                ci.DrawRect(board, new Color(0.55f, 0.38f, 0.2f)); // 코르크
                ci.DrawRect(board, new Color(0.32f, 0.22f, 0.12f), false, 1f);
                int notes = 2 + (int)(Mathf.PosMod(_world.Log.Entries.Count * 0.13f + m.Id, 3f));
                for (int k = 0; k < notes; k++)
                {
                    var np = board.Position + new Vector2(1 + BH(m.Id, k, 1) * Mathf.Max(1, board.Size.X - 4), 1 + BH(k, m.Id, 2) * Mathf.Max(1, board.Size.Y - 4));
                    var col = k % 3 == 0 ? new Color(0.98f, 0.95f, 0.75f) : k % 3 == 1 ? new Color(0.75f, 0.9f, 0.98f) : new Color(0.98f, 0.8f, 0.85f);
                    ci.DrawRect(new Rect2(np, new Vector2(3, 3)), col);
                    ci.DrawCircle(np + new Vector2(1.5f, 0.3f), 0.6f, new Color(0.9f, 0.2f, 0.2f), true, -1f, true); // 압정
                }
                break;
            }
        }
    }
}
