using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.17 방 공사 · 쓰임 · 이름 보기 (읽기만 — 시뮬레이션을 바꾸지 않는다).
// · 공사: 떼어 낸 설비에 늘어진 전선(구리 끝이 흔들린다) · 붉은 마개를 씌운 관 · "분리" 꼬리표 /
//   들고 가는 설비는 끈 두른 나무 상자(설비 아이콘 · 무게 띠) — 둘이 들면 두 사람 사이에서 걸음마다 출렁, 카트면 손수레(L자 틀 · 바퀴 둘이 돈다) 위에 /
//   끊겨 내려놓은 상자는 바닥에 비스듬히 / 놓을 자리는 노란 점선 테 + 분필 X / 빠진 자리는 먼지 테두리 + 긁힌 자국 /
//   칸막이는 골조(세로 기둥 · 위아래 띠)가 진척만큼 차오르고 양옆에 노랑·검정 사선 띠 / 운동 기구를 짜는 동안 부품 더미(톱니 · 벨트 고리 · 볼트).
// · 이름: 문 곁 표지판 — 출처마다 생김이 다르다: 사람(나무 판 · 결 · 못 둘) / 사건(놋쇠 판 · 두 겹 테 · 새긴 줄) /
//   쓰임(칠판 · 나무 틀 · 분필 글씨 · 번진 자국) / 소품(파란 법랑 · 물고기 실루엣).
// · 쓰임: 방 구석의 용도 원판 — 침대 · 끈 두른 상자 · 아령 · 렌치 · 잎 · 십자 · 냄비 · 포크와 접시 · 김 나는 컵 (실루엣이 다르다).
//   설계와 다르게 쓰이면 설계 원판(흐리게) → 화살표 → 실제 원판, 방 윗변에 쓰임 색 점선 띠.
// · 사람: 헷갈린 사람 머리 위 흔들리는 물음표 말풍선 · 땀방에서 뛰는 사람 이마의 땀방울 · 정비실 구석에 세워 둔 손수레.
public partial class ShipView
{
    private static readonly Color RwTapeY = new("#f2c230"), RwTapeK = new("#16161a"), RwWood = new("#8a5a2b"), RwWoodDark = new("#5e3b1a"),
        RwBrass = new("#c39a3e"), RwBrassDark = new("#7a5a1c"), RwChalk = new("#2d3b33"), RwEnamel = new("#2f6fae"), RwCopper = new("#d9894a"),
        RwCable = new("#1c1f26"), RwCap = new("#d6453d"), RwCrate = new("#a5763f"), RwStrap = new("#3b2f22"), RwTruck = new("#8f2f2a"), RwChalkLine = new("#eeeadb");

    /// <summary>동적 층: 공사 (분리 · 상자 · 손수레 · 놓을 자리 · 골조) · 쓰임 원판 · 헷갈림 · 땀.</summary>
    private void PaintRoomWork(CanvasItem ci)
    {
        var w = _world;
        var rp = w.RoomPlans;
        bool fine = Zoom > 1.0f;
        if (Zoom > 0.45f) PaintUseBadges(ci, fine);
        // 손수레 (쉬는 자리)
        if (rp.CartHome is Cell home && rp.CartUser < 0) PaintHandTruck(ci, ToPx(new System.Numerics.Vector2(home.X + 0.5f, home.Y + 0.55f)), T * 0.42f, 0f, fine);
        foreach (var t in rp.Tasks)
        {
            if (t.Done) continue;
            var plan = rp.PlanOf(t);
            if (plan == null || plan.State != "공사") continue;
            var f = t.FurnitureId >= 0 && t.FurnitureId < w.Ship.Furniture.Count ? w.Ship.Furniture[t.FurnitureId] : null;
            switch (t.Kind)
            {
                case RoomTaskKind.Wall: PaintFrame(ci, t, fine); break;
                case RoomTaskKind.Assemble: PaintParts(ci, t, fine); break;
                case RoomTaskKind.Sign: if (t.Leader >= 0 && t.Progress > 0f) PaintPaintCan(ci, t); break;
                case RoomTaskKind.Haul when f != null: PaintHaul(ci, t, f, fine); break;
            }
        }
        // 떼어 낸 설비 (다시 잇기 전)
        foreach (var f in w.Ship.Furniture)
            if (!f.Stowed && !f.Room.Detached && rp.Disconnected(f.Id)) PaintDangling(ci, f, fine);
        // 사람: 물음표 · 땀
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.Outside) continue;
            var p = ToPx(c.Position);
            if (w.RoomUse.Puzzled.TryGetValue(c.Id, out var since) && w.Tick - since < SimTime.Minutes(5))
            {
                float wob = Mathf.Sin(_time * 5f + c.Id) * 3f;
                var b = p + new Vector2(wob * 0.4f, -T * 0.95f);
                ci.Circle(b, 7.5f, new Color(1f, 1f, 1f, 0.92f), true, -1f, true);
                ci.Poly(new[] { b + new Vector2(-3f, 5f), b + new Vector2(2f, 6f), b + new Vector2(-4f, 10f) }, new Color(1f, 1f, 1f, 0.92f));
                ci.Arc(b, 7.5f, 0f, Mathf.Tau, 18, new Color(0.15f, 0.15f, 0.2f, 0.6f), 1f, true);
                Gfx.TextCentered(ci, Fonts.Bold, b + new Vector2(0f, -0.5f), "?", 11, new Color(0.85f, 0.3f, 0.2f));
            }
            if (rp.WorkingOut(c) && c.Pose == Pose.Working)
                for (int k = 0; k < 3; k++)
                {
                    float ph = Mathf.PosMod(_time * 1.4f + k * 0.33f + c.Id * 0.17f, 1f);
                    var d = p + new Vector2(-5f + k * 5f, -T * 0.45f + ph * 9f);
                    ci.Poly(new[] { d + new Vector2(0f, -2.6f), d + new Vector2(1.6f, 0.6f), d + new Vector2(-1.6f, 0.6f) }, new Color(0.55f, 0.82f, 1f, 0.85f * (1f - ph)));
                    ci.Circle(d + new Vector2(0f, 0.8f), 1.6f, new Color(0.55f, 0.82f, 1f, 0.85f * (1f - ph)), true, -1f, true);
                }
        }
    }

    // ─────────────────────────────── 쓰임 원판 ───────────────────────────────

    private void PaintUseBadges(CanvasItem ci, bool fine)
    {
        var w = _world;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached || r.Cells.Count == 0 || RoomUseSystem.Design(r) is not RoomType design) continue;
            var use = r.UsedAs ?? design;
            float s = T * 0.3f;
            var corner = new Vector2(r.MinX * T + s + 3f, r.MinY * T + s + 3f);
            if (r.UsedAs is RoomType u)
            {
                // 설계 원판(흐리게) → 실제 원판 · 윗변에 쓰임 색 점선 띠
                PaintUseDisc(ci, corner, s * 0.72f, design, 0.35f, fine);
                var a0 = corner + new Vector2(s * 0.85f, 0f);
                var a1 = a0 + new Vector2(s * 0.8f, 0f);
                ci.DrawLine(a0, a1, new Color(1f, 1f, 1f, 0.6f), 1.4f, true);
                ci.Poly(new[] { a1 + new Vector2(2.5f, 0f), a1 + new Vector2(-1.5f, -2.5f), a1 + new Vector2(-1.5f, 2.5f) }, new Color(1f, 1f, 1f, 0.7f));
                PaintUseDisc(ci, a1 + new Vector2(s * 1.15f, 0f), s, u, 1f, fine);
                var col = Palette.Room(u);
                float y = r.MinY * T + 1.5f;
                for (float x = r.MinX * T + 2f; x < (r.MaxX + 1) * T - 4f; x += 9f)
                    ci.DrawLine(new Vector2(x, y), new Vector2(Mathf.Min(x + 5f, (r.MaxX + 1) * T - 2f), y), col with { A = 0.75f }, 2f);
            }
            else if (fine) PaintUseDisc(ci, corner, s * 0.8f, use, 0.55f, fine);
        }
    }

    private void PaintUseDisc(CanvasItem ci, Vector2 c, float s, RoomType use, float alpha, bool fine)
    {
        var col = Palette.Room(use);
        ci.Circle(c, s, new Color(0.06f, 0.07f, 0.1f, 0.85f * alpha), true, -1f, true);
        ci.Arc(c, s, 0f, Mathf.Tau, 20, col with { A = alpha }, 1.5f, true);
        var ink = new Color(0.95f, 0.95f, 0.92f, alpha);
        float g = s * 0.62f;
        switch (use)
        {
            case RoomType.Quarters: // 침대: 몸판 + 베개 + 머리판
                ci.Box(new Rect2(c + new Vector2(-g, -g * 0.15f), new Vector2(g * 2f, g * 0.7f)), ink);
                ci.Box(new Rect2(c + new Vector2(-g, -g * 0.55f), new Vector2(g * 0.6f, g * 0.4f)), ink);
                ci.DrawLine(c + new Vector2(-g, -g * 0.8f), c + new Vector2(-g, g * 0.75f), ink, 1.6f);
                break;
            case RoomType.Storage: // 끈 두른 상자
                ci.Box(new Rect2(c - new Vector2(g * 0.85f, g * 0.75f), new Vector2(g * 1.7f, g * 1.5f)), ink, false, 1.5f);
                ci.DrawLine(c + new Vector2(-g * 0.85f, -g * 0.75f), c + new Vector2(g * 0.85f, g * 0.75f), ink, 1f);
                ci.DrawLine(c + new Vector2(g * 0.85f, -g * 0.75f), c + new Vector2(-g * 0.85f, g * 0.75f), ink, 1f);
                break;
            case RoomType.Gym: // 아령
                ci.DrawLine(c + new Vector2(-g, 0f), c + new Vector2(g, 0f), ink, 1.6f);
                foreach (float sx in new[] { -1f, 1f })
                {
                    ci.Box(new Rect2(c + new Vector2(sx * g * 0.75f - g * 0.15f, -g * 0.6f), new Vector2(g * 0.3f, g * 1.2f)), ink);
                    ci.Box(new Rect2(c + new Vector2(sx * g * 0.45f - g * 0.1f, -g * 0.42f), new Vector2(g * 0.2f, g * 0.84f)), ink);
                }
                break;
            case RoomType.Workshop: // 렌치
                ci.DrawLine(c + new Vector2(-g * 0.8f, g * 0.8f), c + new Vector2(g * 0.35f, -g * 0.35f), ink, 2f);
                ci.Arc(c + new Vector2(g * 0.55f, -g * 0.55f), g * 0.42f, Mathf.Pi * 0.75f, Mathf.Pi * 2.25f, 10, ink, 1.8f, true);
                break;
            case RoomType.Hydroponics: // 잎 + 잎맥
                ci.Poly(new[] { c + new Vector2(-g * 0.8f, g * 0.8f), c + new Vector2(-g * 0.6f, -g * 0.3f), c + new Vector2(g * 0.2f, -g * 0.85f),
                    c + new Vector2(g * 0.85f, -g * 0.85f), c + new Vector2(g * 0.75f, -g * 0.1f), c + new Vector2(g * 0.1f, g * 0.6f) }, ink);
                ci.DrawLine(c + new Vector2(-g * 0.8f, g * 0.8f), c + new Vector2(g * 0.6f, -g * 0.6f), new Color(0.06f, 0.07f, 0.1f, alpha), 1f);
                break;
            case RoomType.Medbay: // 십자
                ci.Box(new Rect2(c - new Vector2(g * 0.25f, g * 0.8f), new Vector2(g * 0.5f, g * 1.6f)), ink);
                ci.Box(new Rect2(c - new Vector2(g * 0.8f, g * 0.25f), new Vector2(g * 1.6f, g * 0.5f)), ink);
                break;
            case RoomType.Galley: // 냄비 + 손잡이 + 김
                ci.Box(new Rect2(c + new Vector2(-g * 0.7f, -g * 0.15f), new Vector2(g * 1.4f, g * 0.85f)), ink);
                ci.DrawLine(c + new Vector2(-g, 0f), c + new Vector2(g, 0f), ink, 1.4f);
                if (fine) ci.Arc(c + new Vector2(0f, -g * 0.5f), g * 0.25f, Mathf.Pi, Mathf.Tau, 6, ink, 1f, true);
                break;
            case RoomType.Mess: // 접시 + 포크
                ci.Arc(c + new Vector2(g * 0.2f, 0f), g * 0.6f, 0f, Mathf.Tau, 14, ink, 1.4f, true);
                ci.DrawLine(c + new Vector2(-g * 0.8f, -g * 0.8f), c + new Vector2(-g * 0.8f, g * 0.8f), ink, 1.3f);
                for (int k = -1; k <= 1; k++) ci.DrawLine(c + new Vector2(-g * 0.8f + k * g * 0.18f, -g * 0.8f), c + new Vector2(-g * 0.8f + k * g * 0.18f, -g * 0.35f), ink, 0.8f);
                break;
            default: // 휴게실: 김 나는 컵
                ci.Box(new Rect2(c + new Vector2(-g * 0.6f, -g * 0.3f), new Vector2(g * 1.0f, g * 1.0f)), ink);
                ci.Arc(c + new Vector2(g * 0.45f, g * 0.2f), g * 0.3f, -Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, 6, ink, 1.2f, true);
                if (fine) for (int k = 0; k < 2; k++) ci.DrawLine(c + new Vector2(-g * 0.3f + k * g * 0.4f, -g * 0.45f), c + new Vector2(-g * 0.2f + k * g * 0.4f, -g * 0.85f + Mathf.Sin(_time * 3f + k) * 1.2f), ink, 0.9f);
                break;
        }
    }

    // ─────────────────────────────── 공사 ───────────────────────────────

    /// <summary>노랑·검정 사선 띠 (공사 구역 테두리).</summary>
    private static void PaintTape(CanvasItem ci, Vector2 a, Vector2 b, float width = 3f)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1f) return;
        var u = d / len;
        int n = Mathf.Max(1, Mathf.CeilToInt(len / 6f));
        for (int i = 0; i < n; i++)
        {
            var p0 = a + u * (i * len / n);
            var p1 = a + u * Mathf.Min(len, (i + 1) * len / n);
            ci.DrawLine(p0, p1, i % 2 == 0 ? RwTapeY : RwTapeK, width);
        }
    }

    private static void PaintTapeRect(CanvasItem ci, Rect2 r)
    {
        PaintTape(ci, r.Position, r.Position + new Vector2(r.Size.X, 0f));
        PaintTape(ci, r.Position + new Vector2(r.Size.X, 0f), r.End);
        PaintTape(ci, r.End, r.Position + new Vector2(0f, r.Size.Y));
        PaintTape(ci, r.Position + new Vector2(0f, r.Size.Y), r.Position);
    }

    /// <summary>칸막이 골조: 세로 기둥이 진척만큼 차오른다 · 위아래 띠 · 양옆 테이프.</summary>
    private void PaintFrame(CanvasItem ci, RoomTask t, bool fine)
    {
        if (t.Line.Count == 0) return;
        int n = t.Line.Count;
        int built = Mathf.Clamp(Mathf.CeilToInt(n * t.Progress), 0, n);
        var first = CellRect(t.Line[0]);
        var last = CellRect(t.Line[^1]);
        bool vertical = t.Line.Count > 1 && t.Line[1].X == t.Line[0].X;
        var span = first.Merge(last);
        PaintTapeRect(ci, span.Grow(T * 0.35f));
        for (int i = 0; i < n; i++)
        {
            var r = CellRect(t.Line[i]);
            if (i >= built) { ci.Box(r.Grow(-T * 0.38f), new Color(0.75f, 0.78f, 0.82f, 0.18f), false, 1f); continue; }
            var c = r.GetCenter();
            var steel = new Color(0.7f, 0.74f, 0.8f);
            if (vertical)
            {
                ci.Box(new Rect2(c.X - 2.5f, r.Position.Y, 5f, T), steel with { A = 0.85f });
                ci.DrawLine(new Vector2(c.X - 5f, r.Position.Y + 3f), new Vector2(c.X + 5f, r.Position.Y + 3f), steel, 1.5f);
            }
            else
            {
                ci.Box(new Rect2(r.Position.X, c.Y - 2.5f, T, 5f), steel with { A = 0.85f });
                ci.DrawLine(new Vector2(r.Position.X + 3f, c.Y - 5f), new Vector2(r.Position.X + 3f, c.Y + 5f), steel, 1.5f);
            }
            if (fine) ci.Circle(c, 1.3f, new Color(0.25f, 0.27f, 0.3f), true, -1f, true); // 볼트
        }
        if (t.Leader >= 0 && built < n && fine) // 용접 불꽃
        {
            var tip = CellRect(t.Line[Mathf.Min(built, n - 1)]).GetCenter();
            for (int k = 0; k < 4; k++)
            {
                float a = _time * 9f + k * 1.7f;
                ci.DrawLine(tip, tip + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (3f + 3f * Mathf.PosMod(_time * 3f + k, 1f)), new Color(1f, 0.85f, 0.4f, 0.8f), 1f);
            }
        }
    }

    /// <summary>운동 기구 부품 더미: 톱니 · 벨트 고리 · 볼트 · 진척 막대.</summary>
    private void PaintParts(CanvasItem ci, RoomTask t, bool fine)
    {
        var r = CellRect(t.Spot);
        var c = r.GetCenter();
        PaintTapeRect(ci, r.Grow(T * 0.12f));
        // 톱니
        var g = c + new Vector2(-T * 0.15f, -T * 0.08f);
        ci.Circle(g, T * 0.13f, new Color(0.55f, 0.58f, 0.62f), true, -1f, true);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.Tau / 8f + _time * (t.Leader >= 0 ? 0.6f : 0f);
            ci.DrawLine(g + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * T * 0.13f, g + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * T * 0.19f, new Color(0.55f, 0.58f, 0.62f), 2.2f);
        }
        ci.Circle(g, T * 0.04f, new Color(0.15f, 0.16f, 0.18f), true, -1f, true);
        // 벨트 고리
        ci.Arc(c + new Vector2(T * 0.18f, T * 0.12f), T * 0.12f, 0f, Mathf.Tau, 14, new Color(0.12f, 0.12f, 0.12f), 2.4f, true);
        if (fine) for (int k = 0; k < 3; k++) ci.Circle(c + new Vector2(-T * 0.3f + k * 4f, T * 0.3f), 1.3f, new Color(0.8f, 0.82f, 0.85f), true, -1f, true);
        Gfx.Bar(ci, new Rect2(r.Position.X + 3f, r.End.Y - 4f, T - 6f, 3f), t.Progress, Palette.Good);
    }

    /// <summary>표지판 칠: 페인트 통 + 붓.</summary>
    private void PaintPaintCan(CanvasItem ci, RoomTask t)
    {
        var c = CellRect(t.Spot).GetCenter() + new Vector2(T * 0.25f, T * 0.25f);
        ci.Box(new Rect2(c - new Vector2(4f, 4f), new Vector2(8f, 8f)), new Color(0.75f, 0.76f, 0.8f));
        ci.Box(new Rect2(c - new Vector2(4f, 4f), new Vector2(8f, 2.5f)), new Color(0.95f, 0.45f, 0.25f));
        ci.DrawLine(c + new Vector2(3f, -4f), c + new Vector2(8f, -10f + Mathf.Sin(_time * 6f) * 1.5f), RwWood, 1.6f);
    }

    /// <summary>나르기: 놓을 자리(노란 점선 · 분필 X) · 빠진 자리(먼지 테) · 상자 (둘이 · 카트 · 내려놓음).</summary>
    private void PaintHaul(CanvasItem ci, RoomTask t, Furniture f, bool fine)
    {
        var w = _world;
        // 놓을 자리
        if (t.ToCells.Count > 0)
        {
            var dst = CellRect(t.ToCells[0]);
            foreach (var c in t.ToCells) dst = dst.Merge(CellRect(c));
            var r = dst.Grow(-2f);
            float off = Mathf.PosMod(_time * 8f, 8f);
            for (float x = r.Position.X + off - 8f; x < r.End.X; x += 8f)
            {
                float x0 = Mathf.Max(r.Position.X, x), x1 = Mathf.Min(r.End.X, x + 4f);
                if (x1 > x0) { ci.DrawLine(new Vector2(x0, r.Position.Y), new Vector2(x1, r.Position.Y), RwTapeY, 1.5f); ci.DrawLine(new Vector2(x0, r.End.Y), new Vector2(x1, r.End.Y), RwTapeY, 1.5f); }
            }
            for (float y = r.Position.Y + off - 8f; y < r.End.Y; y += 8f)
            {
                float y0 = Mathf.Max(r.Position.Y, y), y1 = Mathf.Min(r.End.Y, y + 4f);
                if (y1 > y0) { ci.DrawLine(new Vector2(r.Position.X, y0), new Vector2(r.Position.X, y1), RwTapeY, 1.5f); ci.DrawLine(new Vector2(r.End.X, y0), new Vector2(r.End.X, y1), RwTapeY, 1.5f); }
            }
            if (fine)
            {
                var m = r.GetCenter();
                ci.DrawLine(m + new Vector2(-5f, -5f), m + new Vector2(5f, 5f), RwChalkLine with { A = 0.7f }, 1.4f);
                ci.DrawLine(m + new Vector2(5f, -5f), m + new Vector2(-5f, 5f), RwChalkLine with { A = 0.7f }, 1.4f);
            }
        }
        if (!t.Lifted && t.SetDown == null) return;
        // 빠진 자리: 먼지 테두리 + 긁힌 자국
        if (t.FromRoom >= 0)
        {
            var old = new Rect2(t.FromCell.X * T, t.FromCell.Y * T, t.W * T, t.H * T).Grow(-3f);
            ci.Box(old, new Color(0.62f, 0.58f, 0.5f, 0.35f), false, 2f);
            if (fine) for (int k = 0; k < 3; k++) ci.DrawLine(old.Position + new Vector2(4f + k * 6f, old.Size.Y - 4f), old.Position + new Vector2(10f + k * 6f, old.Size.Y - 9f), new Color(0.2f, 0.2f, 0.2f, 0.4f), 1f);
        }
        // 상자 자리
        Vector2 p;
        float tilt = 0f;
        CrewMember? lead = t.Leader >= 0 && t.Leader < w.Crew.Count ? w.Crew[t.Leader] : null;
        CrewMember? help = t.Helper >= 0 && t.Helper < w.Crew.Count ? w.Crew[t.Helper] : null;
        if (t.Lifted && lead != null)
        {
            var lp = ToPx(lead.Position);
            if (help != null && (help.Position - lead.Position).Length() < 2.6f)
            {
                p = (lp + ToPx(help.Position)) * 0.5f + new Vector2(0f, -T * 0.18f + Mathf.Sin(_time * 7f) * 1.2f); // 둘이 들고 출렁
                tilt = (ToPx(help.Position) - lp).Angle();
            }
            else if (t.Cart)
            {
                p = lp + new Vector2(T * 0.55f, T * 0.05f);
                PaintHandTruck(ci, p + new Vector2(0f, T * 0.1f), T * 0.42f, lead.IsMoving ? _time * 8f : 0f, fine);
                p += new Vector2(0f, -T * 0.12f);
            }
            else p = lp + new Vector2(T * 0.35f, -T * 0.1f);
        }
        else if (t.SetDown is Cell sd) { p = CellRect(sd).GetCenter(); tilt = 0.18f; }
        else return;
        PaintCrate(ci, p, t, f, tilt, fine);
    }

    /// <summary>끈 두른 나무 상자 (설비 아이콘 · 무거우면 붉은 띠).</summary>
    private void PaintCrate(CanvasItem ci, Vector2 p, RoomTask t, Furniture f, float tilt, bool fine)
    {
        float sw = Mathf.Clamp(t.W, 1, 3) * T * 0.42f, sh = Mathf.Clamp(t.H, 1, 3) * T * 0.36f;
        ci.DrawSetTransform(p, tilt, Vector2.One);
        var r = new Rect2(-sw * 0.5f, -sh * 0.5f, sw, sh);
        ci.Box(new Rect2(r.Position + new Vector2(2f, 2f), r.Size), new Color(0f, 0f, 0f, 0.3f));
        ci.Box(r, RwCrate);
        if (fine) for (int k = 1; k < 3; k++) ci.DrawLine(new Vector2(r.Position.X, r.Position.Y + k * sh / 3f), new Vector2(r.End.X, r.Position.Y + k * sh / 3f), RwWoodDark with { A = 0.6f }, 1f); // 판자 결
        ci.Box(r, RwWoodDark, false, 1.2f);
        ci.DrawLine(new Vector2(-sw * 0.2f, r.Position.Y), new Vector2(-sw * 0.2f, r.End.Y), RwStrap, 2f);
        ci.DrawLine(new Vector2(sw * 0.2f, r.Position.Y), new Vector2(sw * 0.2f, r.End.Y), RwStrap, 2f);
        if (t.Weight >= RoomPlanSystem.Heavy) ci.Box(new Rect2(r.Position.X, r.End.Y - 3f, sw, 3f), new Color(0.85f, 0.25f, 0.2f, 0.9f)); // 무거움 띠
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        if (fine) Icons.Draw(ci, Icons.Furniture(f.Type), p, Mathf.Min(sw, sh) * 0.7f, new Color(0.15f, 0.1f, 0.05f, 0.85f));
    }

    /// <summary>손수레: L자 틀 · 발판 · 바퀴 둘 (밀면 바퀴살이 돈다).</summary>
    private static void PaintHandTruck(CanvasItem ci, Vector2 p, float s, float spin, bool fine)
    {
        var top = p + new Vector2(-s * 0.15f, -s * 0.9f);
        var foot = p + new Vector2(-s * 0.15f, s * 0.35f);
        ci.DrawLine(top, foot, RwTruck, 2.6f);
        ci.DrawLine(top + new Vector2(s * 0.35f, 0f), foot + new Vector2(s * 0.35f, 0f), RwTruck, 2.6f);
        ci.DrawLine(top, top + new Vector2(s * 0.35f, 0f), RwTruck, 2f);
        ci.DrawLine(top + new Vector2(-s * 0.12f, -s * 0.1f), top + new Vector2(s * 0.5f, -s * 0.1f), new Color(0.12f, 0.12f, 0.12f), 2.4f); // 손잡이
        ci.DrawLine(foot + new Vector2(-s * 0.05f, 0f), foot + new Vector2(s * 0.6f, 0f), new Color(0.6f, 0.62f, 0.66f), 3f); // 발판
        foreach (float sx in new[] { -0.15f, 0.5f })
        {
            var wc = p + new Vector2(s * sx, s * 0.5f);
            ci.Circle(wc, s * 0.2f, new Color(0.1f, 0.1f, 0.11f), true, -1f, true);
            if (fine) for (int k = 0; k < 3; k++)
            {
                float a = spin + k * Mathf.Tau / 3f;
                ci.DrawLine(wc, wc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.17f, new Color(0.55f, 0.55f, 0.58f), 1f);
            }
        }
    }

    /// <summary>떼어 낸 설비: 늘어진 전선(구리 끝이 흔들린다) · 붉은 마개를 씌운 관 · "분리" 꼬리표.</summary>
    private void PaintDangling(CanvasItem ci, Furniture f, bool fine)
    {
        var box = new Rect2(f.MinX * T, f.MinY * T, f.Width * T, f.Height * T);
        var anchor = new Vector2(box.End.X - 3f, box.Position.Y + box.Size.Y * 0.3f);
        for (int k = 0; k < 3; k++)
        {
            float sway = Mathf.Sin(_time * 2.2f + k * 1.3f) * 2.5f;
            var a = anchor + new Vector2(0f, k * 4f);
            var mid = a + new Vector2(6f + k * 2f, 6f + sway);
            var end = a + new Vector2(9f + k * 3f, 12f + k * 2f + sway * 1.4f);
            ci.Polyline(new[] { a, mid, end }, RwCable, 1.6f, true);
            ci.Circle(end, 1.6f, RwCopper, true, -1f, true);
            if (fine && Mathf.PosMod(_time * 1.3f + k, 3f) < 0.08f) ci.Circle(end, 3f, new Color(1f, 0.9f, 0.5f, 0.5f), true, -1f, true); // 가끔 번쩍 (남은 전기)
        }
        if (Procedures.Plumbed(f.Type))
        {
            var pa = new Vector2(box.Position.X + 3f, box.End.Y - 4f);
            ci.DrawLine(pa, pa + new Vector2(-7f, 0f), new Color(0.45f, 0.55f, 0.65f), 3.5f);
            ci.Circle(pa + new Vector2(-8f, 0f), 2.8f, RwCap, true, -1f, true);
        }
        // 꼬리표
        var tag = new Vector2(box.Position.X + box.Size.X * 0.5f, box.Position.Y - 2f);
        ci.DrawLine(tag, tag + new Vector2(2f, 6f), new Color(0.9f, 0.9f, 0.9f), 0.8f);
        ci.Box(new Rect2(tag + new Vector2(-1f, 6f), new Vector2(Zoom > 1.2f ? 22f : 8f, 8f)), RwCap);
        if (Zoom > 1.2f) Gfx.Text(ci, Fonts.Bold, tag + new Vector2(1f, 13f), "분리", 7, Colors.White);
    }

    // ─────────────────────────────── 표지판 ───────────────────────────────

    /// <summary>승무원이 붙인 이름: 문 곁 표지판 (출처마다 생김이 다르다).</summary>
    private void PaintRoomSigns(CanvasItem ci)
    {
        var w = _world;
        if (Zoom < 0.55f) return;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.CustomName is not string name || r.Detached) continue;
            Door? door = null;
            foreach (var d in r.Doors) if (!d.Removed && !d.IsExternal) { door = d; break; }
            if (door == null) continue;
            string src = w.RoomPlans.NameSourceOf(r) ?? "";
            var dc = CellRect(door.Cell).GetCenter();
            // 문이 가로 벽에 있으면 문 옆 벽에, 세로 벽이면 문 위에
            var at = door.ConnectsVertically ? dc + new Vector2(T * 1.15f, 0f) : dc + new Vector2(0f, -T * 1.0f);
            int size = 9;
            float tw = Gfx.Width(Fonts.Bold, name, size);
            var rect = new Rect2(at.X - tw * 0.5f - 6f, at.Y - 7.5f, tw + 12f, 15f);
            switch (src)
            {
                case "사람": // 나무 판 · 결 · 못 둘
                    ci.Box(rect, RwWood);
                    for (int k = 1; k < 3; k++) ci.DrawLine(rect.Position + new Vector2(2f, k * 5f), rect.Position + new Vector2(rect.Size.X - 2f, k * 5f + Mathf.Sin(k * 2.1f) * 1.2f), RwWoodDark with { A = 0.5f }, 0.8f);
                    ci.Box(rect, RwWoodDark, false, 1f);
                    ci.Circle(rect.Position + new Vector2(3f, 3f), 1.2f, new Color(0.75f, 0.75f, 0.78f), true, -1f, true);
                    ci.Circle(rect.Position + new Vector2(rect.Size.X - 3f, 3f), 1.2f, new Color(0.75f, 0.75f, 0.78f), true, -1f, true);
                    Gfx.TextCentered(ci, Fonts.Bold, at, name, size, new Color(1f, 0.93f, 0.8f));
                    break;
                case "사건": // 놋쇠 판 · 두 겹 테 · 새긴 줄
                    Gfx.RoundRect(ci, rect, RwBrass, 2f, RwBrassDark, 1);
                    ci.Box(rect.Grow(-2.5f), RwBrassDark with { A = 0.6f }, false, 0.8f);
                    Gfx.TextCentered(ci, Fonts.Bold, at + new Vector2(0f, -0.5f), name, size, new Color(0.25f, 0.16f, 0.05f));
                    ci.DrawLine(new Vector2(rect.Position.X + 5f, rect.End.Y - 2.5f), new Vector2(rect.End.X - 5f, rect.End.Y - 2.5f), RwBrassDark, 0.7f);
                    break;
                case "소품": // 파란 법랑 · 물고기
                    Gfx.RoundRect(ci, new Rect2(rect.Position - new Vector2(10f, 0f), rect.Size + new Vector2(10f, 0f)), RwEnamel, 4f, new Color(0.9f, 0.9f, 0.95f), 1);
                    var fp = new Vector2(rect.Position.X - 3f, at.Y);
                    ci.Poly(new[] { fp + new Vector2(-4f, 0f), fp + new Vector2(0f, -2.5f), fp + new Vector2(3f, 0f), fp + new Vector2(0f, 2.5f) }, new Color(1f, 0.7f, 0.3f));
                    ci.Poly(new[] { fp + new Vector2(3f, 0f), fp + new Vector2(5.5f + Mathf.Sin(_time * 5f), -2.5f), fp + new Vector2(5.5f + Mathf.Sin(_time * 5f), 2.5f) }, new Color(1f, 0.7f, 0.3f));
                    Gfx.TextCentered(ci, Fonts.Bold, at, name, size, Colors.White);
                    break;
                default: // 쓰임: 칠판 · 나무 틀 · 분필 글씨
                    ci.Box(rect.Grow(1.5f), RwWood);
                    ci.Box(rect, RwChalk);
                    ci.DrawLine(rect.Position + new Vector2(3f, rect.Size.Y - 3f), rect.Position + new Vector2(9f, rect.Size.Y - 4f), new Color(1f, 1f, 1f, 0.18f), 2f); // 번진 자국
                    Gfx.TextCentered(ci, Fonts.Bold, at, name, size, RwChalkLine);
                    break;
            }
        }
    }
}
