using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 사고와 그 흔적을 그리는 부분: 외벽 손상 단계, 새는 공기, 불과 연기, 그을음,
/// 봉합·용접 자국, 환기 댐퍼, 잠긴 격벽, 운석 충돌 연출, 사고 도구 미리보기.
/// 흔적은 사고가 끝나도 남는다 — 우주선의 역사가 벽에 쌓인다.
/// </summary>
public partial class ShipView
{
    private readonly Dictionary<Impact, float> _impactSeen = new();

    private static readonly Color SealantColor = new("#8fc8f0");
    private static readonly Color WeldColor = new("#c9d3e0");
    private static readonly Color FireOuter = new("#ff4a1c");
    private static readonly Color FireMid = new("#ff8a2a");
    private static readonly Color FireCore = new("#ffd76a");

    private static float Hash(int x, int y, int k = 0)
    {
        unchecked
        {
            int h = x * 73856093 ^ y * 19349663 ^ k * 83492791;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xffff) / 65535f;
        }
    }

    /// <summary>벽에서 우주 쪽을 향하는 방향 (외벽이 아니면 0).</summary>
    private Vector2 OutwardOf(Cell wall)
    {
        var g = _world.Ship.Grid;
        var sum = Vector2.Zero;
        foreach (var d in Cell.Dirs4)
            if (!g.InBounds(wall + d) || g.Kind(wall + d) == TileKind.Void) sum += new Vector2(d.X, d.Y);
        return sum == Vector2.Zero ? Vector2.Zero : sum.Normalized();
    }

    // ─────────────────────────────── 바닥: 그을음 ───────────────────────────────

    private void PaintScorch(CanvasItem ci)
    {
        foreach (var (cell, amount) in _world.Fire.Scorch)
        {
            if (amount < 0.03f || _world.Ship.Grid.Kind(cell) == TileKind.Void) continue; // 떨어져 나간 방의 그을음은 조각에 실려 갔다
            var c = CellRect(cell).GetCenter();
            for (int k = 0; k < 4; k++)
            {
                var off = new Vector2(Hash(cell.X, cell.Y, k) - 0.5f, Hash(cell.Y, cell.X, k + 7) - 0.5f) * T * 0.7f;
                float rad = T * (0.22f + 0.2f * Hash(cell.X, cell.Y, k + 3)) * (0.6f + 0.4f * amount);
                ci.DrawCircle(c + off, rad, new Color(0.02f, 0.015f, 0.01f, 0.35f * amount), true, -1f, true);
            }
        }
    }

    // ─────────────────────────────── 연기 ───────────────────────────────

    private void PaintSmoke(CanvasItem ci)
    {
        foreach (var room in _world.Ship.LiveRooms)
        {
            float smoke = room.Air.Smoke;
            if (smoke < 0.03f) continue;
            FillRoom(ci, room, new Color(0.3f, 0.3f, 0.32f, 0.5f * smoke));
            int i = 0;
            foreach (var cell in room.Cells)
            {
                if (((cell.X * 3 + cell.Y * 5) & 3) != 0) continue;
                float ph = _time * 0.25f + Hash(cell.X, cell.Y) * 6f;
                var p = CellRect(cell).GetCenter() + new Vector2(Mathf.Sin(ph) * 8f, Mathf.Cos(ph * 0.7f) * 6f);
                ci.DrawCircle(p, T * (0.55f + 0.2f * Mathf.Sin(ph * 1.3f)), new Color(0.42f, 0.42f, 0.45f, 0.18f * smoke), true, -1f, true);
                i++;
            }
        }
    }

    // ─────────────────────────────── 외벽 손상과 흔적 ───────────────────────────────

    private void PaintWalls(CanvasItem ci)
    {
        foreach (var (cell, wall) in _world.Ship.Walls)
        {
            if (wall.StageIndex == 0 && !wall.Patched && wall.Welds == 0 && wall.Scorch < 0.02f && wall.Replacements == 0) continue;
            PaintWallDamage(ci, cell, wall);
        }
    }

    private void PaintWallDamage(CanvasItem ci, Cell cell, WallState wall)
    {
        var r = CellRect(cell);
        var c = r.GetCenter();
        var outDir = OutwardOf(cell);
        int stage = wall.StageIndex;
        float j1 = Hash(cell.X, cell.Y, 1), j2 = Hash(cell.X, cell.Y, 2), j3 = Hash(cell.X, cell.Y, 3);
        var jitter = new Vector2(j1 - 0.5f, j2 - 0.5f) * 8f;

        if (wall.Scorch > 0.02f) ci.DrawRect(r, new Color(0.03f, 0.02f, 0.015f, 0.55f * wall.Scorch));

        // 통째로 간 패널: 주변 벽보다 조금 밝은 새 판과 네 귀퉁이 볼트 (용접 자국은 사라졌다)
        if (wall.Replacements > 0 && wall.Welds == 0)
        {
            var plate = r.Grow(-3f);
            ci.DrawRect(plate, new Color(0.75f, 0.8f, 0.88f, 0.1f));
            ci.DrawRect(plate, new Color(0.8f, 0.85f, 0.92f, 0.28f), false, 1f);
            foreach (var corner in new[] { plate.Position, new Vector2(plate.End.X, plate.Position.Y), plate.End, new Vector2(plate.Position.X, plate.End.Y) })
                ci.DrawCircle(corner + (plate.GetCenter() - corner).Normalized() * 3f, 1.2f, new Color(0.85f, 0.88f, 0.95f, 0.6f), true, -1f, true);
        }

        // 용접 흔적: 수리할 때마다 한 줄씩 늘어나는 밝은 이음매
        for (int k = 0; k < Mathf.Min(wall.Welds, 3); k++)
        {
            float a = (Hash(cell.X, cell.Y, 10 + k) - 0.5f) * 1.6f + (k * 1.1f);
            var d = Vector2.FromAngle(a) * T * 0.42f;
            var mid = c + new Vector2(Hash(cell.X, cell.Y, 20 + k) - 0.5f, Hash(cell.Y, cell.X, 20 + k) - 0.5f) * 10f;
            ci.DrawLine(mid - d, mid + d, WeldColor.WithAlpha(0.55f), 2f, true);
            for (int b = -2; b <= 2; b++)
                ci.DrawCircle(mid + d * (b / 2.5f), 1.3f, WeldColor.WithAlpha(0.8f), true, -1f, true);
        }

        if (stage >= 1)
        {
            // 찌그러짐: 움푹한 자국
            ci.DrawCircle(c + jitter, 7f + 3f * j3, new Color(0f, 0f, 0f, 0.35f), true, -1f, true);
            ci.DrawArc(c + jitter, 7f + 3f * j3, -2.4f, -0.6f, 10, new Color(1f, 1f, 1f, 0.12f), 1.5f, true);
        }
        if (stage >= 2)
        {
            // 균열: 벽을 가로지르는 들쭉날쭉한 금
            var pts = new List<Vector2>();
            var axis = outDir == Vector2.Zero ? Vector2.Right : new Vector2(-outDir.Y, outDir.X);
            for (int k = 0; k <= 5; k++)
            {
                float t = k / 5f - 0.5f;
                float off = (Hash(cell.X, cell.Y, 30 + k) - 0.5f) * 9f;
                pts.Add(c + jitter * 0.5f + axis * t * T * 0.95f + new Vector2(-axis.Y, axis.X) * off);
            }
            var crackCol = stage >= 3 ? new Color(0.95f, 0.75f, 0.7f, 0.8f) : new Color(0.8f, 0.85f, 0.95f, 0.55f);
            ci.DrawPolyline(pts.ToArray(), new Color(0, 0, 0, 0.7f), 3f, true);
            ci.DrawPolyline(pts.ToArray(), crackCol, 1.2f, true);
            // 가지 균열
            ci.DrawLine(pts[2], pts[2] + Vector2.FromAngle(j1 * 6f) * 7f, crackCol.WithAlpha(0.6f), 1f, true);
        }

        bool leaking = wall.Breach > 0f && !wall.Patched;
        if (stage >= 3)
        {
            float holeR = stage >= 4 ? 6f + 8f * wall.Breach : 2.5f;
            var hc = c + jitter * 0.4f;
            if (stage >= 4)
            {
                // 파공: 찢어진 구멍, 가장자리가 붉게 달아올라 있다
                var poly = new Vector2[10];
                for (int k = 0; k < poly.Length; k++)
                {
                    float a = k * Mathf.Tau / poly.Length;
                    float rr = holeR * (0.7f + 0.55f * Hash(cell.X, cell.Y, 40 + k));
                    poly[k] = hc + Vector2.FromAngle(a) * rr;
                }
                float glow = wall.Patched ? 0f : 0.5f + 0.3f * Mathf.Sin(_time * 5f + j1 * 5f);
                if (glow > 0f) ci.DrawCircle(hc, holeR + 5f, Palette.Danger.WithAlpha(0.18f * glow), true, -1f, true);
                ci.DrawColoredPolygon(poly, Palette.Space);
                var rim = new Vector2[poly.Length + 1];
                poly.CopyTo(rim, 0);
                rim[^1] = poly[0];
                ci.DrawPolyline(rim, wall.Patched ? new Color("#5a4a44") : new Color("#ff8a5c").WithAlpha(0.5f + 0.4f * glow), 1.5f, true);
                // 뜯겨 나간 철판 조각
                for (int k = 0; k < 4; k++)
                {
                    var a = Vector2.FromAngle(Hash(cell.X, cell.Y, 50 + k) * Mathf.Tau);
                    ci.DrawLine(hc + a * holeR * 0.8f, hc + a * (holeR + 5f), new Color("#6b7486"), 1.5f, true);
                }
            }
            else
            {
                ci.DrawCircle(hc, holeR, Palette.Space, true, -1f, true);
            }

            if (leaking) PaintHiss(ci, cell, hc, outDir, wall.Breach);
        }

        if (stage >= 5)
        {
            // 구조 연결 상실: 외판이 골조째 뜯겨 나가 칸 전체가 뚫렸다. 휜 골조 빔이 삐져나와 있다
            var hole = r.Grow(-2f);
            ci.DrawRect(hole, Palette.Space);
            for (int k = 0; k < 3; k++)
            {
                float t = (k + 0.5f) / 3f;
                var a0 = new Vector2(hole.Position.X + hole.Size.X * t, hole.Position.Y);
                var bend = new Vector2((Hash(cell.X, cell.Y, 80 + k) - 0.5f) * 14f, hole.Size.Y * (0.35f + 0.3f * Hash(cell.Y, cell.X, 80 + k)));
                ci.DrawLine(a0, a0 + bend, new Color("#5a6475"), 2.5f, true);
                ci.DrawLine(a0 + bend, a0 + bend + new Vector2(bend.X * 0.6f + 4f, 5f), new Color("#4a5262"), 2f, true);
            }
            ci.DrawRect(hole, new Color("#ff6b4a").WithAlpha(0.35f + 0.25f * Mathf.Sin(_time * 4f + j1 * 5f)), false, 1.5f);
        }

        if (wall.Patched)
        {
            // 실링폼: 거품처럼 부푼 푸른 덩어리
            var pc = c + jitter * 0.4f;
            float size = stage >= 4 ? 10f + 6f * wall.Breach : 6f;
            float q = wall.PatchQuality;
            for (int k = 0; k < 5; k++)
            {
                var off = new Vector2(Hash(cell.X, cell.Y, 60 + k) - 0.5f, Hash(cell.Y, cell.X, 60 + k) - 0.5f) * size;
                ci.DrawCircle(pc + off, size * (0.45f + 0.25f * Hash(cell.X, cell.Y, 70 + k)), SealantColor.WithAlpha(0.55f + 0.35f * q), true, -1f, true);
            }
            ci.DrawCircle(pc + new Vector2(-2, -3), size * 0.25f, new Color(1, 1, 1, 0.35f), true, -1f, true);
            if (q < 0.45f)
                ci.DrawArc(pc, size + 3f, 0f, Mathf.Tau, 20, Palette.Warning.WithAlpha(0.5f + 0.4f * Mathf.Sin(_time * 4f)), 1.5f, true);
        }
    }

    /// <summary>새는 곳: 공기가 쉭쉭 빠져나가는 입자.</summary>
    private void PaintHiss(CanvasItem ci, Cell cell, Vector2 hole, Vector2 outDir, float breach)
    {
        if (outDir == Vector2.Zero) return;
        var room = Hull.InsideRoom(_world.Ship, cell);
        float pressure = room?.Air.Pressure ?? 0f;
        if (pressure < 2f) return;
        float strength = Mathf.Clamp(pressure / 100f, 0.15f, 1f);
        int n = 4 + (int)(12 * Mathf.Clamp(breach * 3f, 0f, 1f));
        for (int k = 0; k < n; k++)
        {
            float ph = Mathf.PosMod(_time * (1.6f + breach * 2f) + k / (float)n + Hash(cell.X, cell.Y, 80 + k), 1f);
            float spread = (Hash(cell.X, cell.Y, 90 + k) - 0.5f) * 0.9f;
            var dir = outDir.Rotated(spread);
            var p = hole + dir * T * (0.2f + 2.4f * ph);
            ci.DrawCircle(p, 1.2f + 2.5f * ph, new Color(0.85f, 0.92f, 1f, 0.55f * (1f - ph) * strength), true, -1f, true);
        }
    }

    /// <summary>감압 중인 방: 공기가 구멍 쪽으로 빨려 가는 흐름.</summary>
    private void PaintVenting(CanvasItem ci)
    {
        var ship = _world.Ship;
        foreach (var (cell, wall) in ship.Walls)
        {
            if (!wall.IsHull || wall.Breach <= 0f || wall.Patched) continue;
            var room = Hull.InsideRoom(ship, cell);
            if (room == null || room.Air.Pressure < 3f) continue;
            var hole = CellRect(cell).GetCenter();
            float strength = Mathf.Clamp(room.Air.Pressure / 100f, 0.1f, 1f) * Mathf.Clamp(wall.Breach * 4f, 0.25f, 1f);
            int n = Mathf.Min(room.Cells.Count, 18);
            for (int k = 0; k < n; k++)
            {
                var from = room.Cells[(int)(Hash(cell.X, cell.Y, 100 + k) * room.Cells.Count) % room.Cells.Count];
                var start = CellRect(from).GetCenter();
                float ph = Mathf.PosMod(_time * (0.5f + strength) + Hash(cell.Y, cell.X, 110 + k), 1f);
                float eased = ph * ph;
                var p = start.Lerp(hole, eased);
                var dir = (hole - start).Normalized();
                ci.DrawLine(p - dir * (3f + 10f * eased), p, new Color(0.8f, 0.9f, 1f, 0.35f * strength * Mathf.Sin(ph * Mathf.Pi)), 1.2f, true);
            }
        }
    }

    // ─────────────────────────────── 불 ───────────────────────────────

    private void PaintFires(CanvasItem ci)
    {
        foreach (var (cell, intensity) in _world.Fire.Fires)
        {
            var r = CellRect(cell);
            var c = r.GetCenter() + new Vector2(0f, 5f);
            float seed = Hash(cell.X, cell.Y) * 10f;
            float flick = 0.85f + 0.15f * Mathf.Sin(_time * 13f + seed);
            ci.DrawCircle(c, T * (0.6f + 0.6f * intensity) * flick, FireOuter.WithAlpha(0.13f), true, -1f, true);
            ci.DrawCircle(c, T * (0.35f + 0.3f * intensity), FireMid.WithAlpha(0.18f), true, -1f, true);

            int tongues = 3 + (int)(intensity * 3f);
            for (int k = 0; k < tongues; k++)
            {
                float x = (k - (tongues - 1) * 0.5f) * 5f + Mathf.Sin(_time * 7f + k * 2.1f + seed) * 1.5f;
                float h = (8f + 14f * intensity) * (0.7f + 0.3f * Mathf.Sin(_time * (9f + k) + seed + k));
                float w = 4.5f + 2f * intensity;
                var b = c + new Vector2(x, 0f);
                var outer = new[] { b + new Vector2(-w, 0), b + new Vector2(0, -h), b + new Vector2(w, 0), b + new Vector2(0, w * 0.6f) };
                ci.DrawColoredPolygon(outer, FireOuter.WithAlpha(0.85f));
                var mid = new[] { b + new Vector2(-w * 0.6f, 0), b + new Vector2(0, -h * 0.7f), b + new Vector2(w * 0.6f, 0), b + new Vector2(0, w * 0.4f) };
                ci.DrawColoredPolygon(mid, FireMid.WithAlpha(0.9f));
                var core = new[] { b + new Vector2(-w * 0.3f, 0), b + new Vector2(0, -h * 0.35f), b + new Vector2(w * 0.3f, 0), b + new Vector2(0, w * 0.25f) };
                ci.DrawColoredPolygon(core, FireCore);
            }
            // 불똥
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(_time * 0.9f + k / 3f + seed, 1f);
                var p = c + new Vector2(Mathf.Sin(ph * 9f + k) * 6f, -ph * T * (0.8f + intensity));
                ci.DrawCircle(p, 1.3f, FireCore.WithAlpha(1f - ph), true, -1f, true);
            }
        }
    }

    // ─────────────────────────────── 환기 댐퍼 ───────────────────────────────

    private void PaintDampers(CanvasItem ci, bool showOpen)
    {
        foreach (var room in _world.Ship.LiveRooms)
        {
            if (room.VentOpen && !showOpen) continue;
            var p = DamperIconPos(room);
            var box = new Rect2(p.X - 6f, p.Y - 6f, 12f, 12f);
            if (room.VentOpen)
            {
                Gfx.RoundRect(ci, box, new Color("#0d1117").WithAlpha(0.85f), 2, Palette.Accent.WithAlpha(room.Powered ? 0.6f : 0.25f));
                for (int k = 0; k < 3; k++)
                    ci.DrawLine(new Vector2(box.Position.X + 3, box.Position.Y + 3.5f + k * 2.5f),
                        new Vector2(box.End.X - 3, box.Position.Y + 3.5f + k * 2.5f), Palette.Accent.WithAlpha(0.6f), 1f);
            }
            else
            {
                Gfx.RoundRect(ci, box, new Color("#1a0d10").WithAlpha(0.9f), 2, Palette.Danger.WithAlpha(0.8f));
                ci.DrawLine(box.Position + new Vector2(3, 3), box.End - new Vector2(3, 3), Palette.Danger, 1.5f, true);
                ci.DrawLine(new Vector2(box.End.X - 3, box.Position.Y + 3), new Vector2(box.Position.X + 3, box.End.Y - 3), Palette.Danger, 1.5f, true);
            }
        }
    }

    /// <summary>댐퍼 아이콘 자리: 조작하는 문 옆 벽 (통로 쪽).</summary>
    private Vector2 DamperIconPos(Room room)
    {
        var spot = room.DamperSpot;
        foreach (var d in room.Doors)
        {
            var n = spot - d.Cell;
            if (System.Math.Abs(n.X) + System.Math.Abs(n.Y) != 1) continue;
            var side = new Vector2(n.Y, n.X); // 벽을 따라 옆으로
            return CellRect(d.Cell).GetCenter() + side * T * 0.95f + new Vector2(n.X, n.Y) * T * 0.1f;
        }
        return CellRect(spot).GetCenter() + new Vector2(-9f, -9f);
    }

    // ─────────────────────────────── 격리된 구획 ───────────────────────────────

    /// <summary>
    /// 방의 사고 상태. 치명(진공·화재)은 방 전체를 하나의 강한 색으로, 긴급은 테두리만,
    /// 포기한 구획은 어둡게 가라앉힌 줄무늬로 (이미 끝난 일이라 시선을 끌지 않게).
    /// </summary>
    private void PaintRoomStates(CanvasItem ci)
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 3f);
        foreach (var room in _world.Ship.LiveRooms)
        {
            switch (Severity.Of(_world, room))
            {
                case RoomSeverity.Critical:
                {
                    bool fire = _world.Fire.IsKnown(room) && _world.Fire.CountIn(room) > 0;
                    var col = fire ? FireMid : Palette.Danger;
                    FillRoom(ci, room, col.WithAlpha(0.16f + 0.08f * pulse));
                    foreach (var (a, b) in Outline(room)) ci.DrawLine(a, b, col.WithAlpha(0.55f + 0.35f * pulse), 3f);
                    break;
                }
                case RoomSeverity.Urgent:
                {
                    var col = room.Leaking ? Palette.Danger : Palette.Warning;
                    foreach (var (a, b) in Outline(room)) ci.DrawLine(a, b, col.WithAlpha(0.3f + 0.3f * pulse), 2f);
                    break;
                }
                case RoomSeverity.Abandoned:
                {
                    FillRoom(ci, room, new Color(0.02f, 0.02f, 0.03f, 0.6f));
                    foreach (var c in room.Cells)
                    {
                        if ((c.X + c.Y) % 2 != 0) continue;
                        var r = CellRect(c);
                        ci.DrawLine(r.Position + new Vector2(0, T), r.Position + new Vector2(T, 0), new Color(1, 1, 1, 0.05f), 3f);
                    }
                    foreach (var (a, b) in Outline(room)) ci.DrawLine(a, b, new Color(0.6f, 0.62f, 0.68f, 0.35f), 2f);
                    break;
                }
            }
        }
    }

    // ─────────────────────────────── 운석 연출 ───────────────────────────────

    /// <summary>
    /// v10.1 다가오는 운석: 멀리서 선체 쪽으로 날아오는 불똥. 배가 알아채기 전에는 흐릿하게 (관찰자만 안다),
    /// 센서·통신실이 궤적을 읽으면 들어올 외벽에 붉은 표적과 남은 시간, 창밖 목격이면 "충격 대비"만.
    /// </summary>
    private void PaintIncoming(CanvasItem ci)
    {
        float tick = _world.Tick + _main.Alpha;
        foreach (var m in _world.Sensors.Incoming)
        {
            float u = Mathf.Clamp((tick - m.Launched) / Mathf.Max(1f, m.Arrive - m.Launched), 0f, 1f);
            var entry = CellRect(m.Entry).GetCenter();
            var dir = new Vector2(m.Direction.X, m.Direction.Y);
            var head = entry - dir * T * (14f + 70f * (1f - u));
            bool known = m.Warned != WarnLevel.None;
            float size = m.Size;
            var tail = head - dir * T * (2f + 2f * size);
            float a = known ? 0.9f : 0.35f;
            ci.DrawLine(tail, head, new Color(1f, 0.7f, 0.4f, 0.3f * a), 5f * (0.5f + size), true);
            ci.DrawCircle(head, 3f + 3f * size, new Color(1f, 0.93f, 0.8f, a), true, -1f, true);
            if (m.Warned >= WarnLevel.Manual)
            {
                // 궤적: 점선으로 외벽까지, 표적 고리
                float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 6f);
                for (float d = 0f; d < 1f; d += 0.06f)
                    ci.DrawLine(head.Lerp(entry, d), head.Lerp(entry, d + 0.03f), Palette.Danger.WithAlpha(0.55f), 1.5f, true);
                ci.DrawArc(entry, T * (0.9f + 0.3f * pulse), 0f, Mathf.Tau, 32, Palette.Danger.WithAlpha(0.9f), 2.5f, true);
                ci.DrawArc(entry, T * (1.6f + 0.5f * size), 0f, Mathf.Tau, 40, Palette.Danger.WithAlpha(0.35f + 0.3f * pulse), 1.5f, true);
                Gfx.TextCentered(ci, Fonts.Bold, entry - dir * T * 2.6f, $"{m.MinutesLeft(_world.Tick):0.0}분", 13, Palette.Danger);
            }
            else if (m.Warned == WarnLevel.Lookout)
                Gfx.TextCentered(ci, Fonts.Bold, head - dir * T * 1.5f, "충격 대비!", 12, Palette.Warning);
        }
    }

    private void PaintImpacts(CanvasItem ci)
    {
        foreach (var impact in _world.Impacts)
        {
            if (!_impactSeen.TryGetValue(impact, out float seen))
            {
                seen = _time;
                _impactSeen[impact] = seen;
                _main.Camera.Shake(impact.Size >= 0.7f ? 14f : 6f);
            }
            float age = _time - seen;
            if (age > 2.6f) continue;

            var entry = CellRect(impact.Entry).GetCenter();
            var dir = new Vector2(impact.Direction.X, impact.Direction.Y);
            float size = impact.Size;
            const float approach = 0.45f;
            if (age < approach)
            {
                float u = age / approach;
                var head = entry - dir * T * 14f * (1f - u);
                var tail = head - dir * T * (3f + 3f * size);
                ci.DrawLine(tail, head, new Color(1f, 0.7f, 0.4f, 0.35f), 6f * (0.5f + size), true);
                ci.DrawLine(tail.Lerp(head, 0.5f), head, new Color(1f, 0.9f, 0.7f, 0.8f), 2.5f * (0.5f + size), true);
                ci.DrawCircle(head, 4f + 4f * size, new Color(1f, 0.95f, 0.85f, 0.95f), true, -1f, true);
                continue;
            }
            float t = (age - approach) / (2.6f - approach);
            float flash = Mathf.Clamp(1f - t * 4f, 0f, 1f);
            if (flash > 0f) ci.DrawCircle(entry, T * (0.8f + 2.5f * size) * (0.6f + t * 3f), new Color(1f, 0.85f, 0.6f, 0.55f * flash), true, -1f, true);
            ci.DrawArc(entry, T * (1f + 6f * size) * Mathf.Sqrt(t), 0f, Mathf.Tau, 48, new Color(1f, 0.8f, 0.6f, 0.5f * (1f - t)), 2f, true);
            // 파편: 안쪽으로 튀는 불똥
            int n = 8 + (int)(14 * size);
            for (int k = 0; k < n; k++)
            {
                float spread = (Hash(impact.Entry.X, impact.Entry.Y, k) - 0.5f) * 1.4f;
                var d = dir.Rotated(spread);
                float reach = T * (2f + 5f * size) * (0.4f + 0.6f * Hash(impact.Entry.Y, impact.Entry.X, k));
                var p = entry + d * reach * Mathf.Sqrt(t);
                ci.DrawLine(p - d * 6f, p, new Color(1f, 0.75f, 0.45f, 0.9f * (1f - t)), 1.6f, true);
            }
        }
        // 오래된 기록 정리
        if (_impactSeen.Count > 24)
            foreach (var old in _impactSeen.Keys.Where(k => !_world.Impacts.Contains(k)).ToList()) _impactSeen.Remove(old);
    }

    // ─────────────────────────────── 사고 도구 미리보기 ───────────────────────────────

    private void PaintToolPreview(CanvasItem ci)
    {
        var tool = _main.Tool;
        if (tool == IncidentTool.None || _main.Hud.IsOverUi(GetViewport().GetMousePosition())) return;
        var mouse = GetGlobalMousePosition();
        var cell = CellAtPx(mouse);
        var ship = _world.Ship;
        float pulse = 0.6f + 0.4f * Mathf.Sin(_time * 6f);

        switch (tool)
        {
            case IncidentTool.SmallMeteor:
            case IncidentTool.BigMeteor:
            case IncidentTool.HugeMeteor:
            {
                float size = tool == IncidentTool.HugeMeteor ? 1.5f : tool == IncidentTool.BigMeteor ? 1f : 0.35f;
                Cell? entry = null;
                float best = float.MaxValue;
                foreach (var (wc, w) in ship.Walls)
                {
                    if (!w.IsHull) continue;
                    float d = (wc.Center - cell.Center).LengthSquared();
                    if (d < best) { best = d; entry = wc; }
                }
                if (entry is not Cell e) return;
                var ep = CellRect(e).GetCenter();
                var outDir = OutwardOf(e);
                ci.DrawArc(ep, T * (1f + 1.5f * size), 0f, Mathf.Tau, 40, Palette.Danger.WithAlpha(0.7f * pulse), 1.5f, true);
                ci.DrawDashedLine(ep + outDir * T * 5f, ep, Palette.Danger.WithAlpha(0.7f), 2f, 6f);
                ci.DrawDashedLine(ep, ToPx(cell.Center), Palette.Danger.WithAlpha(0.35f), 1.5f, 5f);
                ci.DrawCircle(ep, 3f, Palette.Danger, true, -1f, true);
                break;
            }
            case IncidentTool.Fire:
                if (ship.RoomAt(cell) != null)
                    Gfx.RoundRect(ci, CellRect(cell).Grow(-2f), FireMid.WithAlpha(0.2f), 5, FireMid.WithAlpha(0.8f * pulse), 2);
                break;
            case IncidentTool.Break:
                if (ship.FurnitureAt(cell) is Furniture f && f.Machine != null)
                    Gfx.RoundRect(ci, FurnitureRect(f).Grow(2f), Palette.Warning.WithAlpha(0.12f), 7, Palette.Warning.WithAlpha(0.9f * pulse), 2);
                break;
            case IncidentTool.PipeBurst:
                PaintPipeToolPreview(ci, cell, pulse);
                break;
        }
    }

    /// <summary>소화기 분사: 앞쪽으로 퍼지는 하얀 가루.</summary>
    private void PaintSpray(CanvasItem ci, Vector2 body, Vector2 facing, float rr)
    {
        var origin = body + facing * rr;
        for (int k = 0; k < 14; k++)
        {
            float ph = Mathf.PosMod(_time * 2.2f + k / 14f, 1f);
            var d = facing.Rotated((Hash(k, 3) - 0.5f) * 0.8f);
            var p = origin + d * T * 1.8f * ph;
            ci.DrawCircle(p, 1.5f + 5f * ph, new Color(0.92f, 0.95f, 1f, 0.5f * (1f - ph)), true, -1f, true);
        }
    }

    // ─────────────────────────────── 보조 발전기 ───────────────────────────────

    private static void PaintAuxGeneratorBody(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f);
        Gfx.RoundRect(ci, r.Grow(-3f), new Color("#1f1f17"), 6, new Color("#4d4a2c"), 2);
        var wheel = new Vector2(r.Position.X + r.Size.Y * 0.5f + 2f, r.GetCenter().Y);
        ci.DrawCircle(wheel, 10f, new Color("#15150f"), true, -1f, true);
        ci.DrawArc(wheel, 10f, 0f, Mathf.Tau, 32, new Color("#5a5634"), 1.5f, true);
        // 연료 탱크
        Gfx.RoundRect(ci, new Rect2(r.End.X - 26, r.Position.Y + 7, 18, r.Size.Y - 14), new Color("#2a2418"), 4, new Color("#5a4a2a"));
        // 시동 손잡이
        ci.DrawLine(new Vector2(wheel.X, r.End.Y - 5), new Vector2(wheel.X + 12, r.End.Y - 5), new Color("#f5d547").WithAlpha(0.5f), 2f);
    }

    private void PaintAuxGeneratorLife(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f);
        var p = _world.Power;
        var wheel = new Vector2(r.Position.X + r.Size.Y * 0.5f + 2f, r.GetCenter().Y);
        float spin = p.AuxRunning ? _time * 9f : 0.4f;
        var col = p.AuxRunning ? new Color("#f5d547") : new Color("#6b6a4a");
        for (int k = 0; k < 4; k++)
        {
            var d = Vector2.FromAngle(spin + k * Mathf.Pi * 0.5f);
            ci.DrawLine(wheel + d * 3f, wheel + d * 8.5f, col.WithAlpha(0.8f), 2f, true);
        }
        // 연료계
        var tank = new Rect2(r.End.X - 23, r.Position.Y + 10, 12, r.Size.Y - 20);
        float fuel = p.AuxFuel / PowerGrid.AuxFuelHours;
        float h = tank.Size.Y * fuel;
        ci.DrawRect(new Rect2(tank.Position.X, tank.End.Y - h, tank.Size.X, h), (fuel < 0.2f ? Palette.Danger : new Color("#e0b64a")).WithAlpha(0.6f));
        if (p.AuxRunning)
        {
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(_time * 1.2f + k / 3f, 1f);
                ci.DrawCircle(new Vector2(r.End.X - 17 + Mathf.Sin(ph * 6f) * 3f, r.Position.Y - ph * 18f), 2f + 4f * ph,
                    new Color(0.5f, 0.5f, 0.5f, 0.3f * (1f - ph)), true, -1f, true);
            }
        }
    }
}
