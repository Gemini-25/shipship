using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.19 배가 스스로 버티는 장치의 그림 — 원래 그 배에 있던 것처럼 (설명 딱지 없이):
///   장갑 벽(두꺼운 판 · 사선 보강 · 리벳) · 압력 경계 문(문틀 양끝 노랑 · 검정 빗살) · 걸린 차압 문(붉은 걸쇠 · 낮은 쪽으로 쏠린 화살 · 압력계 바늘) ·
///   못 닫힌 문(틈 사이로 빨려 나가는 공기 줄기 · 깜빡임) · 비상 칸막이(접힌 두루마리 → 펼친 막이 구멍 쪽으로 부푼다) ·
///   예비 회로로 넘어간 방(구석의 전환 스위치가 둘째 접점으로 넘어가 호박색으로 깜빡) · 선체 속 보조 간선(두 가닥 구리 줄 · 고정 클램프) ·
///   부하 차단 계전기가 내린 설비(열린 접점 · 꺼진 표시등).
/// 그리기는 시뮬레이션 상태를 읽기만 한다.
/// </summary>
public partial class ShipView
{
    private static readonly Color ArmorPlate = new("#5b6577");
    private static readonly Color ArmorEdge = new("#9aa6b8");
    private static readonly Color LatchRed = new("#ff4d5e");
    private static readonly Color HazardYellow = new("#f5d547");
    private static readonly Color Copper = new("#d08a4a");

    private void PaintFailsafe(CanvasItem ci, ViewMode mode)
    {
        var w = _world;
        var fs = w.Failsafe;
        float zoom = _main.Camera.Zoom.X;
        bool near = zoom >= 0.55f;

        // ── 장갑 벽: 가까이서만 사선 보강 · 리벳 (멀리서는 두꺼운 테두리 하나) ──
        foreach (var (cell, wall) in w.Ship.Walls)
        {
            if (wall.Armor >= 0.999f) continue;
            var r = CellRect(cell);
            bool heavy = wall.Armor <= 0.55f;
            ci.DrawRect(r.Grow(-2f), ArmorPlate.WithAlpha(heavy ? 0.55f : 0.35f), false, heavy ? 2.4f : 1.4f);
            if (!near) continue;
            ci.DrawLine(r.Position + new Vector2(4, T - 4), r.Position + new Vector2(T - 4, 4), ArmorEdge.WithAlpha(0.35f), 1.2f, true);
            if (heavy) ci.DrawLine(r.Position + new Vector2(4, 4), r.Position + new Vector2(T - 4, T - 4), ArmorEdge.WithAlpha(0.25f), 1.2f, true);
            foreach (var p in new[] { new Vector2(5, 5), new Vector2(T - 5, T - 5), new Vector2(T - 5, 5), new Vector2(5, T - 5) })
                if (heavy || p.X == p.Y) ci.DrawCircle(r.Position + p, 1.4f, ArmorEdge.WithAlpha(0.7f), true, -1f, true);
        }

        // ── 선체 속 보조 간선: 두 가닥 구리 줄 + 고정 클램프 (전력 보기에서는 진하게) ──
        bool power = mode == ViewMode.Power || _main.SecondaryView == ViewMode.Power;
        foreach (var l in w.Net.Links)
        {
            if (l.Kind != NetKind.Power || !l.Key.Contains(":ring:") || l.Cells.Count < 2 || l.Room.Detached) continue;
            float a = power ? 0.8f : near ? 0.22f : 0f;
            if (a <= 0f) continue;
            var col = l.Cut ? Palette.Danger : Copper;
            for (int i = 0; i + 1 < l.Cells.Count; i++)
            {
                var p0 = CellRect(l.Cells[i]).GetCenter() + new Vector2(T * 0.32f, T * 0.32f);
                var p1 = CellRect(l.Cells[i + 1]).GetCenter() + new Vector2(T * 0.32f, T * 0.32f);
                var n = (p1 - p0).Normalized().Orthogonal() * 1.6f;
                ci.DrawLine(p0 + n, p1 + n, col.WithAlpha(a), 1.3f, true);
                ci.DrawLine(p0 - n, p1 - n, col.Darkened(0.25f).WithAlpha(a), 1.3f, true);
                if (i % 3 == 0) ci.DrawRect(new Rect2(p0 - new Vector2(2.5f, 2.5f), new Vector2(5f, 5f)), ArmorEdge.WithAlpha(a * 0.9f), false, 1f);
            }
        }

        // ── 문: 압력 경계 표식 · 걸린 차압 문 · 못 닫힌 문 ──
        var failed = new HashSet<int>();
        foreach (var e in fs.Events) if (e.Kind == "fail" && w.Tick - e.Tick < SimTime.Minutes(40) && e.Door >= 0) failed.Add(e.Door);
        foreach (var d in w.Ship.Doors)
        {
            if (d.Removed || d.IsExternal) continue;
            var r = CellRect(d.Cell);
            var c = r.GetCenter();
            var along = d.ConnectsVertically ? new Vector2(1, 0) : new Vector2(0, 1); // 문짝이 미끄러지는 방향 (벽 방향)
            var across = new Vector2(along.Y, along.X);
            if (fs.Rated(d) && near)
            {
                // 압력 경계 문: 문틀 양끝에 노랑 · 검정 빗살
                for (int s = -1; s <= 1; s += 2)
                {
                    var end = c + along * (T * 0.5f - 3f) * s;
                    for (int k = 0; k < 3; k++)
                    {
                        var p = end - along * (k * 2.6f) * s;
                        ci.DrawLine(p - across * 4.5f, p + across * 4.5f, (k % 2 == 0 ? HazardYellow : new Color("#20232a")).WithAlpha(0.75f), 1.6f);
                    }
                }
            }
            if (fs.Latched(d))
            {
                // 붉은 걸쇠 막대 + 낮은 쪽으로 쏠린 공기 화살 + 압력계
                ci.DrawLine(c - along * (T * 0.42f), c + along * (T * 0.42f), LatchRed.WithAlpha(0.9f), 3f, true);
                for (int k = -1; k <= 1; k += 2) ci.DrawCircle(c + along * (T * 0.42f) * k, 2.2f, LatchRed, true, -1f, true);
                var low = d.RoomA != null && d.RoomB != null && d.RoomA.Air.Pressure < d.RoomB.Air.Pressure ? d.RoomA : d.RoomB;
                if (low != null)
                {
                    var to = (ToPx(low.Center) - c).Normalized();
                    if (Mathf.Abs(to.Dot(across)) < 0.2f) to = across;
                    to = across * Mathf.Sign(to.Dot(across));
                    float ph = Mathf.PosMod(_time * 1.6f + d.Id * 0.3f, 1f);
                    var tip = c + to * (6f + 8f * ph);
                    ci.DrawLine(c + to * 3f, tip, new Color(1, 1, 1, 0.55f * (1f - ph)), 1.2f, true);
                    ci.DrawLine(tip, tip - to * 3f + along * 2.5f, new Color(1, 1, 1, 0.55f * (1f - ph)), 1.2f, true);
                    ci.DrawLine(tip, tip - to * 3f - along * 2.5f, new Color(1, 1, 1, 0.55f * (1f - ph)), 1.2f, true);
                }
                if (near)
                {
                    var g = c - across * (T * 0.32f) + along * (T * 0.3f);
                    ci.DrawCircle(g, 4.2f, new Color("#1b1f27"), true, -1f, true);
                    ci.DrawArc(g, 4.2f, 0f, Mathf.Tau, 14, ArmorEdge, 1f, true);
                    float dp = d.RoomA != null && d.RoomB != null ? Mathf.Abs(d.RoomA.Air.Pressure - d.RoomB.Air.Pressure) : 0f;
                    float ang = Mathf.Pi * (0.75f + 1.5f * Mathf.Clamp(dp / 100f, 0f, 1f));
                    ci.DrawLine(g, g + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 3.4f, LatchRed, 1.1f, true);
                }
            }
            else if (failed.Contains(d.Id))
            {
                // 못 닫힌 문: 틈으로 빨려 나가는 공기 줄기 + 주황 깜빡임
                float blink = 0.5f + 0.5f * Mathf.Sin(_time * 7f);
                ci.DrawRect(r.Grow(-4f), Palette.Warning.WithAlpha(0.25f + 0.35f * blink), false, 2f);
                for (int k = 0; k < 4; k++)
                {
                    float ph = Mathf.PosMod(_time * 2.2f + k * 0.25f, 1f);
                    var p = c + along * ((k - 1.5f) * 4f);
                    ci.DrawLine(p - across * 10f * (1f - ph), p + across * (2f + 10f * ph), new Color(0.85f, 0.92f, 1f, 0.5f * (1f - ph)), 1f, true);
                }
            }
        }

        // ── 비상 칸막이 · 예비 회로 전환 스위치 ──
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.Partition > 0 && room.MaxX >= room.MinX) PaintPartition(ci, room, near);
            if (fs.OnAlt(room) && near) PaintTransfer(ci, room);
        }

        // ── 부하 차단 계전기가 내린 설비: 열린 접점 · 꺼진 표시등 ──
        if (fs.ShedLevel > 0 && near)
            foreach (var m in w.Ship.Machines)
            {
                if (!m.Parked || m.Spec.PowerDraw <= 0f || m.Body.Stowed || m.Body.Room.Detached) continue;
                var e = Essentials.Of(m.Body.Type);
                if (e == Essential.Comfort || fs.ShedLevel >= 2 && e == Essential.Support)
                {
                    var fr = FurnitureRect(m.Body);
                    var p = new Vector2(fr.Position.X + 6f, fr.End.Y - 6f);
                    ci.DrawCircle(p, 4.5f, new Color("#1b1f27").WithAlpha(0.85f), true, -1f, true);
                    ci.DrawLine(p + new Vector2(-3, 1.5f), p + new Vector2(-0.5f, 1.5f), ArmorEdge, 1f, true);
                    ci.DrawLine(p + new Vector2(-0.5f, 1.5f), p + new Vector2(2.5f, -2f), ArmorEdge, 1f, true); // 열린 칼날
                    ci.DrawCircle(p + new Vector2(3f, 1.5f), 0.9f, new Color("#5a3a3a"), true, -1f, true); // 꺼진 표시등
                }
            }
    }

    /// <summary>비상 칸막이: 접혀 있으면 긴 벽을 따라 두루마리 · 펼치면 방 가운데를 가로지르는 막이 구멍 쪽으로 부푼다.</summary>
    private void PaintPartition(CanvasItem ci, Room room, bool near)
    {
        bool wide = room.MaxX - room.MinX >= room.MaxY - room.MinY;
        float x0 = room.MinX * T, x1 = (room.MaxX + 1) * T, y0 = room.MinY * T, y1 = (room.MaxY + 1) * T;
        var membrane = new Color("#d9e6f2");
        if (room.Partition == 1)
        {
            if (!near) return;
            // 접힌 두루마리: 천장 레일 끝에 감긴 막 (가운데 줄을 따라 짧게)
            var a = wide ? new Vector2((x0 + x1) * 0.5f, y0 + 4f) : new Vector2(x0 + 4f, (y0 + y1) * 0.5f);
            var dir = wide ? new Vector2(0, 1) : new Vector2(1, 0);
            ci.DrawLine(a, a + dir * 9f, membrane.WithAlpha(0.45f), 4f, true);
            ci.DrawLine(a, a + dir * 9f, ArmorEdge.WithAlpha(0.6f), 1f, true);
            ci.DrawCircle(a, 2f, HazardYellow.WithAlpha(0.7f), true, -1f, true);
            return;
        }
        // 펼친 막: 방 가운데를 가로질러 · 구멍 쪽(새는 쪽)으로 부풀어 흔들린다
        int n = 14;
        var pts = new Vector2[n + 1];
        float bulge = room.Leaking ? 7f : 3f;
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float sway = Mathf.Sin(t * Mathf.Pi) * (bulge + 1.5f * Mathf.Sin(_time * 3f + t * 6f));
            pts[i] = wide ? new Vector2((x0 + x1) * 0.5f + sway, y0 + t * (y1 - y0)) : new Vector2(x0 + t * (x1 - x0), (y0 + y1) * 0.5f + sway);
        }
        ci.DrawPolyline(pts, membrane.WithAlpha(0.75f), 3f, true);
        ci.DrawPolyline(pts, HazardYellow.WithAlpha(0.6f), 1f, true);
        for (int i = 1; i < n; i += 3) ci.DrawCircle(pts[i], 1.6f, ArmorEdge, true, -1f, true); // 고정 고리
    }

    /// <summary>예비 회로로 넘어간 방: 구석의 전환 스위치 칼날이 둘째 접점에 붙어 호박색으로 깜빡.</summary>
    private void PaintTransfer(CanvasItem ci, Room room)
    {
        var p = new Vector2(room.MinX * T + 10f, room.MinY * T + 10f);
        float blink = 0.6f + 0.4f * Mathf.Sin(_time * 4f);
        ci.DrawRect(new Rect2(p - new Vector2(7, 7), new Vector2(14, 14)), new Color("#1b1f27").WithAlpha(0.85f));
        ci.DrawRect(new Rect2(p - new Vector2(7, 7), new Vector2(14, 14)), ArmorEdge.WithAlpha(0.7f), false, 1f);
        var pivot = p + new Vector2(-4, 4);
        ci.DrawCircle(p + new Vector2(4, -4), 1.4f, new Color("#5a5f6a"), true, -1f, true);      // 주 접점 (꺼짐)
        ci.DrawCircle(p + new Vector2(4, 3), 1.6f, new Color("#ffb347").WithAlpha(blink), true, -1f, true); // 예비 접점 (붙음)
        ci.DrawLine(pivot, p + new Vector2(4, 3), new Color("#ffb347"), 1.4f, true);
        ci.DrawCircle(pivot, 1.3f, ArmorEdge, true, -1f, true);
    }
}
