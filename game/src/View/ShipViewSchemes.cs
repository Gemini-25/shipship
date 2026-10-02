using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.14 승무원이 꾸미는 일의 그림 (읽기만):
// 바닥 — 일마다 다른 그림(부품 두세 개를 겹친 실루엣) · 만드는 중엔 반쯤 선 뼈대와 톱밥 · 손이 멈추면 덮개(가까이 보면 속이 비친다) ·
//   익는 술의 방울과 냄새 줄기 · 자라는 꽃 · 밤 방송의 전파 · 회의에 오른 일엔 빨간 꼬리표 · 컴퓨터가 본 일엔 파란 눈 ·
//   끝난 뒤의 흔적: 모두의 것(나무 팻말) · 정식(팻말 + 작은 깃발) · 압수(노란 띠를 두른 상자) · 치운 자리(먼지 자국) · 장난의 뒤끝(점점 흐려진다).
// 사람 위 — 몰래 손을 놀리는 사람의 "쉿" · 귓속말 점선 · 냄새를 따라가는 물결과 물음표 · 일손을 놓은 사람의 팻말 · 모임에 가는 사람 머리 위 작은 그림 ·
//   빚 다툼 뒤의 빨간 동전 · 고른 사람이 아는 일 (본 눈 · 들은 귀 · 귓속말 입술 · 같이 한 손).
public partial class ShipView
{
    private static readonly Vector2[] RecipeAt = { new(0f, 0f), new(0.62f, 0.12f), new(-0.6f, 0.15f), new(0.05f, -0.6f) };
    private static readonly float[] RecipeScale = { 1f, 0.75f, 0.7f, 0.6f };

    private void DrawRecipe(CanvasItem ci, SchemeSpec spec, Vector2 basePx, float scale, float alpha, bool close, int seed)
    {
        int i = 0;
        foreach (var part in SchemeTable.Parts(spec))
        {
            if (i >= RecipeAt.Length) break;
            var o = RecipeAt[i] * T * 0.5f * scale;
            DrawGlyph(ci, part, basePx + o, T * 0.42f * scale * RecipeScale[i], alpha, close, seed + i);
            i++;
        }
    }

    private static Vector2 Floor(Cell c) => ToPx(c.Center) + new Vector2(0, T * 0.32f);

    /// <summary>덮개: 숨겨 둔 일 위의 천 (가까이 보면 반쯤 비친다).</summary>
    private void Tarp(CanvasItem ci, Vector2 p, bool close)
    {
        var col = new Color(0.36f, 0.38f, 0.33f, close ? 0.55f : 0.92f);
        var pts = new Vector2[9];
        for (int i = 0; i < 7; i++) { float k = i / 6f; pts[i] = p + new Vector2((k - 0.5f) * T * 0.95f, -T * (0.42f + 0.1f * Mathf.Sin(i * 1.9f))); }
        pts[7] = p + new Vector2(T * 0.5f, T * 0.05f);
        pts[8] = p + new Vector2(-T * 0.5f, T * 0.05f);
        ci.DrawColoredPolygon(new[] { pts[8], pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], pts[6], pts[7] }, col);
        ci.DrawLine(p + new Vector2(-T * 0.3f, -T * 0.35f), p + new Vector2(-T * 0.2f, 0), col.Darkened(0.3f), 1f, true);
        ci.DrawLine(p + new Vector2(T * 0.15f, -T * 0.4f), p + new Vector2(T * 0.25f, 0), col.Darkened(0.3f), 1f, true);
    }

    private void Plaque(CanvasItem ci, Vector2 p, bool official)
    {
        var board = new Rect2(p + new Vector2(-T * 0.3f, T * 0.05f), new Vector2(T * 0.6f, T * 0.16f));
        ci.DrawRect(board, new Color(0.62f, 0.45f, 0.26f));
        ci.DrawRect(board, new Color(0.35f, 0.24f, 0.12f), false, 1f);
        ci.DrawLine(board.Position + new Vector2(3, board.Size.Y * 0.5f), board.End - new Vector2(3, board.Size.Y * 0.5f), new Color(0.2f, 0.12f, 0.06f, 0.8f), 1f);
        if (!official) return;
        // 정식: 작은 삼각 깃발 줄
        var a = p + new Vector2(-T * 0.45f, -T * 0.75f); var b = p + new Vector2(T * 0.45f, -T * 0.75f);
        ci.DrawLine(a, b, new Color(0.85f, 0.85f, 0.8f, 0.8f), 0.8f, true);
        for (int i = 0; i < 5; i++)
        {
            var q = a.Lerp(b, (i + 0.5f) / 5f);
            ci.DrawColoredPolygon(new[] { q + new Vector2(-3, 0), q + new Vector2(3, 0), q + new Vector2(0, 6) }, i % 2 == 0 ? new Color(0.95f, 0.75f, 0.25f) : new Color(0.3f, 0.7f, 0.9f));
        }
    }

    private void Seized(CanvasItem ci, Vector2 p)
    {
        var box = new Rect2(p + new Vector2(-T * 0.35f, -T * 0.5f), new Vector2(T * 0.7f, T * 0.55f));
        ci.DrawRect(box, new Color(0.45f, 0.42f, 0.38f));
        for (int i = 0; i < 4; i++)
        {
            float x = box.Position.X + i * box.Size.X / 4f;
            ci.DrawLine(new Vector2(x, box.Position.Y), new Vector2(x + box.Size.X / 4f, box.End.Y), i % 2 == 0 ? new Color(0.95f, 0.8f, 0.1f) : new Color(0.1f, 0.1f, 0.1f), 3f);
        }
        ci.DrawRect(box, new Color(0.2f, 0.2f, 0.2f), false, 1.2f);
        ci.DrawRect(new Rect2(box.End - new Vector2(T * 0.2f, T * 0.15f), new Vector2(T * 0.16f, T * 0.1f)), new Color(0.95f, 0.95f, 0.9f));
    }

    private void PaintSchemesFloor(CanvasItem ci)
    {
        var w = _world;
        var sy = w.Schemes;
        if (SchemeSystem.Off) return;
        bool close = Zoom > 0.9f;
        long now = w.Tick;
        // 끝난 일의 흔적
        foreach (var tr in sy.Traces)
        {
            if (SchemeTable.Get(tr.Key) is not SchemeSpec spec) continue;
            if (sy.Get(tr.Scheme) is { Active: true }) continue;
            long age = now - tr.Tick;
            var p = Floor(tr.At);
            switch (tr.State)
            {
                case TraceState.Removed:
                    if (age > SimTime.TicksPerDay) continue;
                    ci.DrawRect(new Rect2(p + new Vector2(-T * 0.4f, -T * 0.3f), new Vector2(T * 0.8f, T * 0.35f)), new Color(0.7f, 0.65f, 0.55f, 0.25f * (1f - age / (float)SimTime.TicksPerDay)), false, 1f);
                    continue;
                case TraceState.Seized:
                    Seized(ci, p);
                    continue;
            }
            float a = 1f;
            if (spec.Cat == SchemeCat.Prank) { if (age > SimTime.Hours(12)) continue; a = 1f - age / (float)SimTime.Hours(12); }
            DrawRecipe(ci, spec, p, 1f, a, close, tr.Id);
            if (tr.State is TraceState.Public or TraceState.Official && spec.Cat != SchemeCat.Prank) Plaque(ci, p, tr.State == TraceState.Official);
            if (tr.State is TraceState.Kept or TraceState.Hidden && spec.Secrecy >= 0.45f) Tarp(ci, p, close);
        }
        // 꾸미는 중인 일
        foreach (var s in sy.All)
        {
            if (!s.Active || s.Stage == SchemeStage.Plan) continue;
            var spec = s.Spec;
            var p = Floor(s.Spot);
            bool session = s.InSession(now);
            if (spec.Fate == Fate.Club && s.Stage == SchemeStage.Live && !session) continue; // 동호회는 모일 때만
            if (s.Stage == SchemeStage.Prep)
            {
                float k = s.Progress;
                // 뼈대: 다 만들면 이만하다는 점선 틀
                var r = new Rect2(p + new Vector2(-T * 0.45f, -T * 0.75f), new Vector2(T * 0.9f, T * 0.8f));
                for (int i = 0; i < 8; i++) { var q = r.Position + new Vector2(r.Size.X * (i % 4) / 3f, i < 4 ? 0 : r.Size.Y); ci.DrawCircle(q, 1f, new Color(0.8f, 0.8f, 0.7f, 0.35f)); }
                DrawRecipe(ci, spec, p, 0.55f + 0.45f * k, 0.35f + 0.6f * k, close, s.Id);
                if (s.Working) for (int i = 0; i < 4; i++) { float u = (_time * 1.5f + i * 0.25f) % 1f; ci.DrawCircle(p + new Vector2(Mathf.Sin(i * 2.3f) * T * 0.4f, -u * T * 0.5f), 1.2f, new Color(0.85f, 0.75f, 0.5f, 1f - u)); }
                else if (spec.Secrecy >= 0.45f) Tarp(ci, p, close);
                if (close) { var bar = new Rect2(p + new Vector2(-T * 0.35f, T * 0.12f), new Vector2(T * 0.7f, 2.5f)); ci.DrawRect(bar, new Color(0, 0, 0, 0.35f)); ci.DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * k, bar.Size.Y)), new Color(0.85f, 0.7f, 0.35f, 0.8f)); }
            }
            else
            {
                float grow = spec.Key is "secret_garden" or "bunk_mushroom" or "herb_tea" ? 0.65f + 0.35f * s.Ripe : 1f;
                DrawRecipe(ci, spec, p, (spec.Fate == Fate.Event && session ? 1.35f : 1f) * grow, 1f, close, s.Id);
                if (spec.Key is "moonshine" or "fruit_wine" or "kimchi_jar")
                {
                    // 익는 술: 방울 · 냄새 줄기 (익을수록 진하다)
                    for (int i = 0; i < 3; i++) { float u = (_time * 0.8f + i / 3f) % 1f; ci.DrawCircle(p + new Vector2(-T * 0.05f + i * 3f, -T * (0.45f + 0.35f * u)), 1.3f, new Color(0.95f, 0.9f, 0.6f, (1f - u) * 0.8f)); }
                    if (s.Ripe > 0.3f && close)
                        for (int i = 0; i < 2; i++)
                        {
                            var pts = new Vector2[6];
                            for (int j = 0; j < 6; j++) pts[j] = p + new Vector2(T * (0.2f + i * 0.2f) + Mathf.Sin(_time * 2f + j * 0.9f + i) * 3f, -T * (0.5f + j * 0.12f));
                            ci.DrawPolyline(pts, new Color(0.75f, 0.8f, 0.4f, 0.35f * s.Ripe), 1f, true);
                        }
                }
                if (spec.Secrecy >= 0.45f && !session && !s.Working && s.Stage == SchemeStage.Live && spec.Fate != Fate.Laugh) Tarp(ci, p, close);
                if (spec.Fate == Fate.Event && session)
                {
                    // 행사: 방 위쪽에 깃발 줄
                    var room = sy.RoomOf(s);
                    if (room != null) PaintGarland(ci, room);
                }
            }
            if (s.Stage == SchemeStage.Vote)
            {
                // 회의에 오른 일: 빨간 꼬리표
                var tag = p + new Vector2(T * 0.35f, -T * 0.55f);
                ci.DrawLine(p + new Vector2(T * 0.1f, -T * 0.4f), tag, new Color(0.9f, 0.9f, 0.85f, 0.8f), 0.8f, true);
                ci.DrawColoredPolygon(new[] { tag, tag + new Vector2(8, -3), tag + new Vector2(8, 5), tag + new Vector2(0, 3) }, new Color(0.85f, 0.2f, 0.2f));
            }
            if (s.ComputerKnows && close)
            {
                // 컴퓨터가 본 일: 파란 눈
                var e = p + new Vector2(-T * 0.42f, -T * 0.7f);
                ci.DrawColoredPolygon(new[] { e + new Vector2(-4, 0), e + new Vector2(0, -2.5f), e + new Vector2(4, 0), e + new Vector2(0, 2.5f) }, new Color(0.4f, 0.85f, 1f, 0.85f));
                ci.DrawCircle(e, 1.2f, new Color(0.05f, 0.15f, 0.25f));
            }
        }
        // 관행이 열리는 동안: 그 방에 그림과 깃발 줄
        foreach (var pr in sy.Practices)
        {
            if (!pr.Now(now) || SchemeTable.Get(pr.Key) is not SchemeSpec spec || pr.RoomId < 0 || pr.RoomId >= w.Ship.Rooms.Count) continue;
            var room = w.Ship.Rooms[pr.RoomId];
            DrawRecipe(ci, spec, ToPx(room.Center) + new Vector2(0, T * 0.3f), 1.2f, 1f, close, pr.Held);
            PaintGarland(ci, room);
        }
        // 안내 목소리 장난: 컴퓨터실 위에 오리
        if (sy.VoiceUntil > now)
            foreach (var room in w.Ship.LiveRooms)
                if (room.Kind is RoomType.ComputerRoom or RoomType.Bridge)
                {
                    DrawGlyph(ci, "duck", ToPx(room.Center) + new Vector2(0, -T * 0.6f + Mathf.Sin(_time * 4f) * 2f), T * 0.5f, 1f, close, room.Id);
                    break;
                }
    }

    private void PaintGarland(CanvasItem ci, Room room)
    {
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue;
        foreach (var c in room.Cells) { minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X); minY = Math.Min(minY, c.Y); }
        var a = new Vector2(minX * T + 4, minY * T + 6); var b = new Vector2((maxX + 1) * T - 4, minY * T + 6);
        var mid = (a + b) * 0.5f + new Vector2(0, 8);
        ci.DrawPolyline(new[] { a, mid, b }, new Color(0.9f, 0.9f, 0.85f, 0.7f), 0.8f, true);
        int n = Math.Max(4, (maxX - minX + 1) * 2);
        for (int i = 0; i < n; i++)
        {
            float k = (i + 0.5f) / n;
            var q = k < 0.5f ? a.Lerp(mid, k * 2f) : mid.Lerp(b, k * 2f - 1f);
            var col = (i % 3) switch { 0 => new Color(0.95f, 0.4f, 0.4f), 1 => new Color(0.95f, 0.85f, 0.3f), _ => new Color(0.4f, 0.75f, 0.95f) };
            ci.DrawColoredPolygon(new[] { q + new Vector2(-3.5f, 0), q + new Vector2(3.5f, 0), q + new Vector2(0, 7) }, col);
        }
    }

    private void PaintSchemesOver(CanvasItem ci)
    {
        var w = _world;
        var sy = w.Schemes;
        if (SchemeSystem.Off) return;
        bool close = Zoom > 0.9f;
        long now = w.Tick;
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            var head = CrewPx(c) + new Vector2(0, -CrewRadius * 2.1f);
            if (c.Job?.Activity is SchemeActivity)
            {
                var t = sy.Task(c);
                var sch = sy.Get(t.Scheme);
                switch (t.Kind)
                {
                    case SchemeTaskKind.Work when sch != null && sch.Spec.Secrecy >= 0.45f:
                        // 쉿: 입 앞에 세운 손가락
                        ci.DrawCircle(head, 5f, new Color(0.15f, 0.15f, 0.2f, 0.75f));
                        ci.DrawLine(head + new Vector2(0, -3.5f), head + new Vector2(0, 3.5f), new Color(1f, 0.85f, 0.7f), 1.6f, true);
                        ci.DrawLine(head + new Vector2(-3f, 1f), head + new Vector2(3f, 1f), new Color(0.9f, 0.4f, 0.4f), 1f, true);
                        break;
                    case SchemeTaskKind.Recruit when w.Crew.FirstOrDefault(o => o.Id == t.Other) is CrewMember o && !o.Dead:
                    {
                        var a = CrewPx(c); var b = CrewPx(o);
                        if ((b - a).Length() < T * 6f)
                            for (int i = 1; i < 8; i++) { var q = a.Lerp(b, i / 8f) + new Vector2(0, -Mathf.Sin(i / 8f * Mathf.Pi) * T * 0.5f); ci.DrawCircle(q, 1f, new Color(1f, 0.9f, 0.6f, 0.6f)); }
                        for (int i = 0; i < 3; i++) ci.DrawCircle(head + new Vector2(-4 + i * 4, 0), 1.3f, new Color(1f, 1f, 1f, 0.8f));
                        break;
                    }
                    case SchemeTaskKind.Check:
                    {
                        // 냄새 · 소리를 따라: 물결과 물음표
                        var pts = new Vector2[5];
                        for (int i = 0; i < 5; i++) pts[i] = head + new Vector2(-8 + i * 4, Mathf.Sin(_time * 5f + i) * 2f);
                        ci.DrawPolyline(pts, new Color(0.75f, 0.85f, 0.45f, 0.8f), 1f, true);
                        ci.DrawArc(head + new Vector2(8, -5), 2.5f, -Mathf.Pi, Mathf.Pi * 0.4f, 6, new Color(1f, 1f, 1f, 0.9f), 1.2f, true);
                        ci.DrawCircle(head + new Vector2(8, 1), 0.9f, new Color(1f, 1f, 1f, 0.9f));
                        break;
                    }
                    case SchemeTaskKind.Sit:
                    {
                        // 일손을 놓은 사람: 머리 위 팻말
                        ci.DrawLine(head + new Vector2(5, 6), head + new Vector2(5, -6), new Color(0.55f, 0.4f, 0.25f), 1.5f);
                        var bd = new Rect2(head + new Vector2(-4, -14), new Vector2(18, 10));
                        ci.DrawRect(bd, new Color(0.95f, 0.92f, 0.85f));
                        ci.DrawPolyline(new[] { bd.Position + new Vector2(2, 7), bd.Position + new Vector2(6, 3), bd.Position + new Vector2(10, 7), bd.Position + new Vector2(15, 3) }, new Color(0.8f, 0.15f, 0.15f), 1.2f, true);
                        break;
                    }
                    case SchemeTaskKind.Attend or SchemeTaskKind.Session or SchemeTaskKind.Practice:
                    {
                        var spec = sch?.Spec ?? (t.Practice >= 0 && t.Practice < sy.Practices.Count ? SchemeTable.Get(sy.Practices[t.Practice].Key) : null);
                        if (spec != null && close) DrawGlyph(ci, SchemeTable.Parts(spec).First(), head + new Vector2(0, 4), T * 0.22f, 0.9f, false, c.Id);
                        break;
                    }
                }
            }
            // 빚 다툼 뒤: 빨간 동전
            foreach (var d in sy.Debts)
                if (d.From == c.Id && d.Amount > 0 && d.Fought >= 0 && now - d.Fought < SimTime.Hours(3))
                {
                    var q = head + new Vector2(-10, 2);
                    ci.DrawCircle(q, 4f, new Color(0.85f, 0.25f, 0.2f, 0.9f));
                    ci.DrawLine(q + new Vector2(-2, 0), q + new Vector2(2, 0), new Color(1, 1, 1), 1.2f);
                    break;
                }
        }
        // 고른 사람이 아는 일: 본 눈 · 들은 귀 · 귓속말 입술 · 같이 한 손
        if (_main.SelectedCrew is CrewMember sel)
            foreach (var s in sy.All)
            {
                if (!s.Active || !s.Knows.TryGetValue(sel.Id, out var how)) continue;
                var q = Floor(s.Spot) + new Vector2(T * 0.42f, -T * 0.85f);
                ci.DrawCircle(q, 5.5f, new Color(0.08f, 0.1f, 0.14f, 0.8f));
                var col = new Color(1f, 0.92f, 0.6f);
                switch (how)
                {
                    case KnowHow.Part:
                        ci.DrawCircle(q + new Vector2(0, 1), 2.5f, col);
                        for (int i = 0; i < 3; i++) ci.DrawLine(q + new Vector2(-2 + i * 2, -1), q + new Vector2(-2 + i * 2, -4), col, 1f);
                        break;
                    case KnowHow.Saw or KnowHow.Victim:
                        ci.DrawColoredPolygon(new[] { q + new Vector2(-4, 0), q + new Vector2(0, -2.5f), q + new Vector2(4, 0), q + new Vector2(0, 2.5f) }, col);
                        ci.DrawCircle(q, 1.2f, new Color(0.1f, 0.1f, 0.15f));
                        break;
                    case KnowHow.Heard:
                        ci.DrawArc(q, 3f, -Mathf.Pi * 0.6f, Mathf.Pi * 0.9f, 8, col, 1.3f, true);
                        ci.DrawArc(q + new Vector2(0.5f, 0.5f), 1.3f, -Mathf.Pi * 0.5f, Mathf.Pi * 0.6f, 5, col, 1f, true);
                        break;
                    default:
                        ci.DrawColoredPolygon(Ellipse(q, 3.5f, 1.8f, 8), new Color(0.95f, 0.45f, 0.5f));
                        ci.DrawLine(q + new Vector2(-3, 0), q + new Vector2(3, 0), new Color(0.5f, 0.15f, 0.2f), 0.8f);
                        break;
                }
            }
    }
}
