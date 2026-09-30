using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v9 배관 그리기: 벽과 바닥 밑을 지나는 관(냉각 고온관·분기·귀환관, 급수관), 격리 밸브, 새는 곳(증기·물),
/// 임시 밀봉·우회 배관의 흔적, 선체 밖 방열판. 배관 보기에서는 굵게, 흐름이 움직인다.
/// </summary>
public partial class ShipView
{
    private static readonly Color HotPipe = new("#ff8a4a");
    private static readonly Color BranchPipe = new("#5fd0c8");
    private static readonly Color ColdPipe = new("#4aa8ff");
    private static readonly Color WaterPipe = new("#8fd3ff");
    private static readonly Color BypassColor = new("#ffd166");

    public static Color PipeColor(PipeSegment s) => s.Role switch
    {
        PipeRole.HotLeg => HotPipe,
        PipeRole.Branch => BranchPipe,
        PipeRole.ColdLeg => ColdPipe,
        _ => WaterPipe,
    };

    /// <summary>겹치는 관이 나란히 보이게 조금씩 비켜 그린다.</summary>
    private static Vector2 PipeOffset(PipeSegment s) => s.Role switch
    {
        PipeRole.HotLeg => new Vector2(0f, -4f),
        PipeRole.ColdLeg => new Vector2(0f, 4f),
        PipeRole.Branch => new Vector2(s.Branch == 0 ? -4f : 4f, 0f),
        PipeRole.WaterMain => new Vector2(0f, 5f),
        _ => new Vector2(5f, 0f),
    };

    private bool PipeFlowing(PipeSegment s)
    {
        var net = _world.Piping;
        if (s.Flow <= 0f) return false;
        if (!s.IsCoolant) return _world.Water.Level > 1f;
        if (!net.MainFlow || net.Pressure < 0.5f) return false;
        if (s.Role == PipeRole.Branch) return s.Pump?.Machine is Machine m && m.Efficiency > 0.05f;
        return net.FlowingBranches > 0;
    }

    /// <summary>선체 밖 방열판: 냉각실 위 외벽에 붙은 판. 열을 버리면 붉게 달아오르고, 부서지면 금이 간다.</summary>
    private void PaintRadiators(CanvasItem ci)
    {
        var net = _world.Piping;
        float heat = Mathf.Clamp(_world.Power.ReactorOutput / Mathf.Max(1f, _world.Power.ReactorRated), 0f, 1f);
        foreach (var b in net.Branches)
        {
            if (b.Radiator.Count == 0 || (b.Pump?.Room.Detached ?? false)) continue;
            bool flowing = PipeFlowing(b);
            float cond = b.RadiatorCondition;
            foreach (var c in b.Radiator)
            {
                var r = CellRect(c);
                // 선체로 이어지는 받침
                ci.DrawLine(new Vector2(r.GetCenter().X, r.End.Y), new Vector2(r.GetCenter().X, r.End.Y + 4f), new Color("#3a4455"), 2f);
                var body = new Rect2(r.Position.X + 1.5f, r.Position.Y + T * 0.3f, r.Size.X - 3f, T * 0.62f);
                ci.DrawRect(body, new Color("#2a3240").Lerp(new Color("#1a1414"), 1f - cond));
                for (int k = 0; k < 5; k++)
                {
                    float x = body.Position.X + 2f + k * (body.Size.X - 4f) / 4f;
                    var fin = flowing ? new Color("#c8663a").Lerp(new Color("#ffb070"), heat).WithAlpha(0.55f + 0.35f * heat) : new Color("#58657a");
                    if (cond < 0.7f && Hash(c.X, c.Y, k) < 1f - cond) fin = new Color("#3a2f2f");
                    ci.DrawLine(new Vector2(x, body.Position.Y + 1f), new Vector2(x, body.End.Y - 1f), fin, 1.5f);
                }
                ci.DrawRect(body, new Color("#5a6678").WithAlpha(0.8f), false, 1f);
                if (cond < 0.7f)
                {
                    // 부서진 판: 금과 휘어진 모서리
                    var a = body.Position + new Vector2(body.Size.X * Hash(c.X, c.Y, 7), 0f);
                    ci.DrawLine(a, a + new Vector2(4f, body.Size.Y * 0.6f), Palette.Danger.WithAlpha(0.8f), 1.5f);
                    ci.DrawLine(a + new Vector2(4f, body.Size.Y * 0.6f), a + new Vector2(-2f, body.Size.Y), Palette.Danger.WithAlpha(0.6f), 1.2f);
                }
            }
        }
    }

    /// <summary>관, 밸브, 새는 곳, 밀봉·우회 흔적. focus = 배관 보기 (굵게, 흐름 표시).</summary>
    private void PaintPipes(CanvasItem ci, bool focus)
    {
        var net = _world.Piping;
        if (net.Segments.Count == 0) return;
        var ship = _world.Ship;
        float width = focus ? 5f : 3.4f;
        float alpha = focus ? 0.97f : 0.82f;
        float heat = Mathf.Clamp(_world.Power.ReactorOutput / Mathf.Max(1f, _world.Power.ReactorRated), 0f, 1f);
        foreach (var s0 in net.Segments)
        {
            // v12.2 떨어져 나간 방을 지나던 구간은 없다 — 남은 토막만 그리고, 찢긴 끝을 표시한다
            foreach (var run in AttachedRuns(s0.Path))
            {
                PaintPipeRun(s0, run, run.Count < s0.Path.Count);
            }
        }

        void PaintPipeRun(PipeSegment s, List<Cell> path, bool torn)
        {
            if (path.Count < 2) return;
            var off = PipeOffset(s);
            var pts = path.Select(c => CellRect(c).GetCenter() + off).ToArray();
            var col = PipeColor(s);
            bool dead = (s.Closed && s.Bypass <= 0f) || s.Severed;
            var body = dead ? col.Darkened(0.55f) : col;
            // v10.9: 금속 관 — 그림자, 관 몸통(유체 색이 비친 강철), 윗면 광택
            if (s.Role == PipeRole.HotLeg && PipeFlowing(s) && heat > 0.1f)
                ci.DrawPolyline(pts, HotPipe.WithAlpha(0.1f * heat), width + 10f, true); // 고온관의 열기
            ci.DrawPolyline(pts.Select(p => p + new Vector2(1.5f, 2f)).ToArray(), new Color(0f, 0f, 0f, alpha * 0.45f), width + 2f, true);
            ci.DrawPolyline(pts, new Color("#0a0d12").WithAlpha(alpha), width + 2f, true);
            var metal = body.Darkened(0.3f).Lerp(new Color("#6a7486"), 0.3f);
            ci.DrawPolyline(pts, metal.WithAlpha(alpha), width, true);
            ci.DrawPolyline(pts.Select(p => p + new Vector2(-0.6f, -0.8f)).ToArray(), body.Lightened(0.35f).WithAlpha(alpha * 0.55f), Mathf.Max(1f, width * 0.3f), true);
            // 플랜지(세 칸마다)와 벽 관통부 칼라
            for (int i = 1; i < pts.Length - 1; i++)
            {
                var dir = (pts[i + 1] - pts[i - 1]).Normalized();
                var perp = new Vector2(-dir.Y, dir.X);
                if (ship.Grid.Kind(path[i]) == TileKind.Wall)
                {
                    var q = pts[i];
                    ci.DrawRect(new Rect2(q - new Vector2(width + 3f, width + 3f), new Vector2(2 * width + 6f, 2 * width + 6f)), new Color("#0a0d12").WithAlpha(alpha));
                    ci.DrawRect(new Rect2(q - new Vector2(width + 2f, width + 2f), new Vector2(2 * width + 4f, 2 * width + 4f)), new Color("#3c4658").WithAlpha(alpha), false, 1.5f);
                }
                else if (i % 3 == 0 && Mathf.Abs(dir.Dot((pts[i] - pts[i - 1]).Normalized())) > 0.9f)
                {
                    float h = width * 0.5f + 2.5f;
                    ci.DrawLine(pts[i] - perp * h, pts[i] + perp * h, new Color("#0a0d12").WithAlpha(alpha), 4f);
                    ci.DrawLine(pts[i] - perp * (h - 0.5f), pts[i] + perp * (h - 0.5f), metal.Lightened(0.25f).WithAlpha(alpha), 2f);
                }
            }
            // 분기는 방열판까지 이어진다
            if (s.Radiator.Count > 0)
            {
                var top = pts[^1];
                var rad = CellRect(s.Radiator[s.Radiator.Count / 2]).GetCenter();
                ci.DrawLine(top, new Vector2(top.X, rad.Y + T * 0.3f), body.WithAlpha(alpha), width, true);
            }
            // 흐름: 관을 따라 움직이는 작은 화살 (배관 보기에서는 촘촘하고 밝게)
            if (PipeFlowing(s))
            {
                float total = 0f;
                for (int i = 1; i < pts.Length; i++) total += pts[i].DistanceTo(pts[i - 1]);
                float speed = (s.IsCoolant ? 38f : 22f) * (s.Closed ? s.Bypass : 1f) * (s.IsCoolant ? net.CoolantFactor : 1f);
                float gap = focus ? 14f : 26f;
                for (float d0 = Mathf.PosMod(_time * speed, gap); d0 < total - 2f; d0 += gap)
                {
                    var p = PointAlong(pts, d0);
                    var q = PointAlong(pts, d0 + 2f);
                    var dir = (q - p).Normalized();
                    if (dir == Vector2.Zero) continue;
                    var perp = new Vector2(-dir.Y, dir.X);
                    float k = focus ? 2.4f : 1.6f;
                    var tip = p + dir * k;
                    var chev = new Color(1f, 1f, 1f, focus ? 0.8f : 0.4f);
                    ci.DrawLine(tip, p - dir * k + perp * k, chev, 1.2f, true);
                    ci.DrawLine(tip, p - dir * k - perp * k, chev, 1.2f, true);
                }
            }
            // 우회 배관: 밸브 둘레를 도는 노란 점선
            if (s.Bypass > 0f)
            {
                var v = CellRect(s.ValveCell).GetCenter() + off;
                var loop = new[] { v + new Vector2(-T * 0.7f, -3f), v + new Vector2(-T * 0.7f, -T * 0.55f), v + new Vector2(T * 0.7f, -T * 0.55f), v + new Vector2(T * 0.7f, -3f) };
                for (int i = 1; i < loop.Length; i++)
                    ci.DrawDashedLine(loop[i - 1], loop[i], BypassColor.WithAlpha(0.9f), focus ? 2.5f : 2f, 4f);
            }
            // 임시 밀봉: 노란 띠
            if (s.Patched)
            {
                var p = CellRect(s.LeakAt).GetCenter() + off;
                ci.DrawRect(new Rect2(p - new Vector2(4f, 4f), new Vector2(8f, 8f)), new Color("#e0b64a").WithAlpha(0.95f));
                ci.DrawRect(new Rect2(p - new Vector2(4f, 4f), new Vector2(8f, 8f)), new Color("#6b5520"), false, 1f);
            }
            // 끊어진 곳: 붉은 X
            if (s.Severed)
            {
                var p = CellRect(s.LeakAt).GetCenter() + off;
                float k = 5f;
                ci.DrawLine(p + new Vector2(-k, -k), p + new Vector2(k, k), Palette.Danger, 2f, true);
                ci.DrawLine(p + new Vector2(-k, k), p + new Vector2(k, -k), Palette.Danger, 2f, true);
            }
            // 새는 곳: 냉각수는 하얀 증기, 물은 파란 물방울
            if (s.Leaking && (s.IsCoolant ? net.Pressure > 0.1f : _world.Water.Level > 1f))
            {
                var p = CellRect(s.LeakAt).GetCenter() + off;
                float strength = Mathf.Clamp(s.LeakRate / (s.IsCoolant ? 60f : 30f), 0.15f, 1f);
                int n = 6 + (int)(10 * strength);
                for (int k = 0; k < n; k++)
                {
                    float ph = Mathf.PosMod(_time * (s.IsCoolant ? 1.6f : 1.1f) + k / (float)n, 1f);
                    var dir = Vector2.Up.Rotated((Hash(s.Id, k, 5) - 0.5f) * 2.2f);
                    var q = p + dir * T * (0.3f + 1.2f * strength) * ph;
                    if (s.IsCoolant)
                        ci.DrawCircle(q, 3f + 9f * ph * (0.4f + strength), new Color(0.95f, 0.97f, 1f, 0.55f * (1f - ph)), true, -1f, true);
                    else
                        ci.DrawCircle(q + new Vector2(0f, T * 0.6f * ph * ph), 1.6f, WaterPipe.WithAlpha(0.8f * (1f - ph)), true, -1f, true);
                }
            }
            if (torn && path.Count >= 1)
            {
                // 찢긴 끝: 너덜한 관 끝과 튀는 물방울
                var off2 = PipeOffset(s);
                foreach (var endCell in new[] { path[0], path[^1] })
                {
                    if (!Cell.Dirs4.Any(d => ship.RoomAt(endCell + d)?.Detached == true || ship.Grid.Kind(endCell + d) == TileKind.Void)) continue;
                    var q = CellRect(endCell).GetCenter() + off2;
                    ci.DrawCircle(q, width * 0.9f, new Color("#1a1f28"), true, -1f, true);
                    for (int k = 0; k < 4; k++)
                        ci.DrawLine(q, q + Vector2.FromAngle(k * 1.7f + 0.4f) * (width + 3f), Palette.Danger.WithAlpha(0.7f), 1.2f, true);
                }
            }
        }
        // 밸브: 손잡이 바퀴(바퀴살 넷) — 열림 = 초록 표시등, 잠김 = 바퀴가 45° 돌고 붉은 표시등
        foreach (var s in net.Segments)
        {
            var v = CellRect(s.ValveCell).GetCenter() + PipeOffset(s) * 0.5f;
            float r = focus ? 6.5f : 5f;
            ci.DrawCircle(v + new Vector2(1.5f, 2f), r + 2f, new Color(0f, 0f, 0f, 0.45f), true, -1f, true);
            ci.DrawCircle(v, r + 1.5f, new Color("#0a0d12"), true, -1f, true);
            ci.DrawArc(v, r, 0f, Mathf.Tau, 24, new Color("#9aa6b5"), 2f, true);
            float rot = s.Closed ? Mathf.Pi * 0.25f : 0f;
            for (int k = 0; k < 4; k++)
            {
                var d = Vector2.FromAngle(rot + k * Mathf.Pi * 0.5f);
                ci.DrawLine(v + d * 1.5f, v + d * (r - 0.5f), new Color("#6f7b8e"), 1.3f, true);
            }
            ci.DrawCircle(v, 1.8f, PipeColor(s), true, -1f, true);
            var lamp = s.Closed ? (s.Bypass > 0f ? BypassColor : Palette.Danger) : Palette.Good;
            ci.DrawCircle(v + new Vector2(r + 3f, -r - 1f), 1.8f, lamp, true, -1f, true);
        }
        // 냉각수 보충구
        if (focus && net.Built)
        {
            var p = CellRect(net.FillPort).GetCenter();
            ci.DrawRect(new Rect2(p - new Vector2(4f, 4f), new Vector2(8f, 8f)), WaterPipe.WithAlpha(0.8f), false, 1.5f);
        }
    }

    private static Vector2 PointAlong(Vector2[] pts, float d)
    {
        for (int i = 1; i < pts.Length; i++)
        {
            float seg = pts[i].DistanceTo(pts[i - 1]);
            if (d <= seg) return pts[i - 1].Lerp(pts[i], seg > 0.001f ? d / seg : 0f);
            d -= seg;
        }
        return pts[^1];
    }

    /// <summary>배관 보기: 냉각실·원자로실을 냉각 상태로 물들인다 (냉각이 모자라면 붉게).</summary>
    private void PaintPipeOverlay(CanvasItem ci)
    {
        var net = _world.Piping;
        var p = _world.Power;
        float need = Mathf.Max(1f, p.ReactorOutput);
        float margin = Mathf.Clamp((net.CoolingKw - need) / 30f, -1f, 1f);
        var coolCol = margin < 0f ? Palette.Danger : Palette.Good;
        foreach (var room in _world.Ship.LiveRooms)
        {
            if (room.Type is RoomType.Reactor or RoomType.Cooling)
                FillRoom(ci, room, coolCol.WithAlpha(0.08f + 0.1f * Mathf.Abs(margin)));
            else if (room.Type == RoomType.Hydroponics && !net.WaterTo(room))
                FillRoom(ci, room, Palette.Warning.WithAlpha(0.14f));
        }
    }

    /// <summary>떨어져 나간 방의 칸을 뺀, 이어진 토막들.</summary>
    private List<List<Cell>> AttachedRuns(List<Cell> path)
    {
        var ship = _world.Ship;
        var runs = new List<List<Cell>>();
        var cur = new List<Cell>();
        foreach (var c in path)
        {
            if (ship.RoomAt(c)?.Detached == true || ship.Grid.Kind(c) == TileKind.Void)
            {
                if (cur.Count > 0) { runs.Add(cur); cur = new List<Cell>(); }
                continue;
            }
            cur.Add(c);
        }
        if (cur.Count > 0) runs.Add(cur);
        return runs;
    }

    /// <summary>배관 파손 도구 미리보기: 가장 가까운 관을 밝힌다.</summary>
    private void PaintPipeToolPreview(CanvasItem ci, Cell cell, float pulse)
    {
        var s = _world.Piping.Nearest(cell.Center, 4f, out var at);
        if (s == null) return;
        var off = PipeOffset(s);
        var pts = s.Path.Select(c => CellRect(c).GetCenter() + off).ToArray();
        ci.DrawPolyline(pts, Palette.Danger.WithAlpha(0.5f * pulse), 7f, true);
        ci.DrawCircle(CellRect(at).GetCenter() + off, 6f, Palette.Danger.WithAlpha(0.8f), false, 2f, true);
    }
}
