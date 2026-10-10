using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v10.9 선체 바깥: 두꺼운 외판(비스듬한 벽은 매끈하게 잇는다), 판 이음매와 리벳, 창문(함교는 통유리 조종석),
/// 엔진 노즐, 꼬리 날개, 항해등(좌현 = 위 빨강, 우현 = 아래 초록, 코끝 흰 섬광), 창밖으로 새는 불빛.
/// 모두 그림일 뿐 시뮬레이션에는 없다 (운석·드론·EVA는 설계도의 외벽을 그대로 쓴다).
/// </summary>
public partial class ShipView
{
    private static readonly Color SkinEdge = new("#07090e");
    private static readonly Color SkinOuter = new("#232b38");
    private static readonly Color SkinInner = new("#3a4557");
    private static readonly Color SkinSeam = new("#141922");
    private static readonly Color Glass = new("#0d2633");
    private static readonly Color GlassLit = new("#4fa9c9");
    private static readonly Color PortLight = new("#ff4d4d");
    private static readonly Color StarboardLight = new("#4dff88");

    private const float NozzleLen = T * 0.85f;

    private HashSet<Cell>? _hullCells;
    private int _hullVersion = -1;
    private readonly List<(Cell cell, Cell outward, Room room, bool canopy)> _windows = new();
    private Vector2 _noseTip, _finTop, _finBottom;

    private static readonly Cell[] Half8 = { new(1, 0), new(0, 1), new(1, 1), new(1, -1) };

    private static bool Windowed(RoomType t) => t is RoomType.Bridge or RoomType.Lounge or RoomType.Mess or RoomType.Quarters
        or RoomType.Hydroponics or RoomType.Comms or RoomType.Medbay or RoomType.Galley;

    /// <summary>외벽 칸(우주와 맞닿은 벽·문)과 창문 자리를 모은다. 구조가 바뀌면 다시.</summary>
    private void BuildHull()
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        _hullCells = new HashSet<Cell>();
        _windows.Clear();
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            var k = g.Kind(c);
            if (k is not (TileKind.Wall or TileKind.Door)) continue;
            if (Cell.Dirs4.Any(d => g.Kind(c + d) == TileKind.Void)) _hullCells.Add(c);
        }
        foreach (var c in _hullCells)
        {
            if (g.Kind(c) != TileKind.Wall) continue;
            var voids = Cell.Dirs4.Where(d => g.Kind(c + d) == TileKind.Void).ToList();
            Room? room = null;
            foreach (var d in Cell.Dirs4)
                if (g.Kind(c + d) == TileKind.Floor && ship.RoomAt(c + d) is Room r) { room = r; break; }
            if (room == null || !Windowed(room.Type) || room.Detached) continue;
            bool canopy = room.Type == RoomType.Bridge;
            if (voids.Count == 1)
            {
                var o = voids[0];
                int along = o.X == 0 ? c.X : c.Y;
                if (canopy || along % 3 == 1) _windows.Add((c, o, room, canopy));
            }
            else if (canopy && voids.Count >= 1) _windows.Add((c, voids[0], room, true));
        }
        // 코끝(가장 오른쪽 외벽 가운데), 꼬리 날개 자리(엔진실 위아래)
        var right = _hullCells.Max(c => c.X);
        var tips = _hullCells.Where(c => c.X == right).ToList();
        _noseTip = CellRect(tips[tips.Count / 2]).GetCenter() + new Vector2(T * 0.5f + 6f, 0f);
        var eng = ship.RoomsOf(RoomType.Engine).FirstOrDefault();
        if (eng != null)
        {
            int ex = (eng.MinX + eng.MaxX) / 2;
            _finTop = new Vector2((ex + 0.5f) * T, (eng.MinY - 1) * T);
            _finBottom = new Vector2((ex + 0.5f) * T, (eng.MaxY + 2) * T);
        }
        _hullVersion = _world.Structure.Version;
    }

    /// <summary>외판: 외벽 칸 가운데를 잇는 굵은 선과 원 — 계단진 벽도 비스듬한 한 장의 판처럼 보인다.</summary>
    private void PaintHullSkin(CanvasItem ci)
    {
        if (_hullCells == null || _hullVersion != _world.Structure.Version) BuildHull();
        var cells = _hullCells!;
        PaintFins(ci);
        PaintNozzles(ci);
        void Layer(float radius, Color col)
        {
            foreach (var c in cells)
            {
                var a = CellRect(c).GetCenter();
                ci.Circle(a, radius, col, true, -1f, true);
                foreach (var d in Half8)
                    if (cells.Contains(c + d)) ci.DrawLine(a, CellRect(c + d).GetCenter(), col, radius * 2f, true);
            }
        }
        Layer(T * 0.5f + 10f, SkinEdge);
        Layer(T * 0.5f + 8f, SkinOuter);
        Layer(T * 0.5f + 3.5f, SkinInner);
    }

    /// <summary>외판 위 이음매·리벳 (벽을 그린 뒤, 우주 쪽 띠에만).</summary>
    private void PaintHullSeams(CanvasItem ci)
    {
        var g = _world.Ship.Grid;
        foreach (var c in _hullCells!)
        {
            var r = CellRect(c);
            // 계단진 모서리: 바깥 반쪽을 외판으로 덮어 45° 비스듬한 선체로
            bool up = g.Kind(c + new Cell(0, -1)) == TileKind.Void, down = g.Kind(c + new Cell(0, 1)) == TileKind.Void;
            bool left = g.Kind(c + new Cell(-1, 0)) == TileKind.Void, right = g.Kind(c + new Cell(1, 0)) == TileKind.Void;
            if (g.Kind(c) == TileKind.Wall && (up || down) && (left || right) && !(up && down) && !(left && right))
            {
                var corner = new Vector2(left ? r.Position.X : r.End.X, up ? r.Position.Y : r.End.Y);
                var ax = new Vector2(left ? r.End.X : r.Position.X, corner.Y);
                var ay = new Vector2(corner.X, up ? r.End.Y : r.Position.Y);
                var outDiag = new Vector2(left ? -1 : 1, up ? -1 : 1).Normalized();
                ci.Poly(new[] { corner + outDiag * 2f, ax + outDiag * 1.5f, ay + outDiag * 1.5f }, SkinInner);
                ci.DrawLine(ax, ay, Palette.WallEdge, 2f, true);
                ci.DrawLine(ax + outDiag * 7f, ay + outDiag * 7f, new Color(1, 1, 1, (up || left) ? 0.1f : 0.04f), 1f, true);
                continue;
            }
            foreach (var d in Cell.Dirs4)
            {
                if (g.Kind(c + d) != TileKind.Void) continue;
                bool horiz = d.Y != 0;   // 위아래가 우주 → 가로로 뻗은 외판
                // 외판 띠 (벽 바깥 0~8px)
                Vector2 edgeA, edgeB, outDir = new(d.X, d.Y);
                if (horiz)
                {
                    float y = d.Y < 0 ? r.Position.Y : r.End.Y;
                    edgeA = new Vector2(r.Position.X, y); edgeB = new Vector2(r.End.X, y);
                }
                else
                {
                    float x = d.X < 0 ? r.Position.X : r.End.X;
                    edgeA = new Vector2(x, r.Position.Y); edgeB = new Vector2(x, r.End.Y);
                }
                // 밝은 테두리 (빛이 왼쪽 위에서)
                float lit = d.Y < 0 || d.X < 0 ? 0.28f : 0.12f;
                ci.DrawLine(edgeA + outDir * 7.5f, edgeB + outDir * 7.5f, new Color(1, 1, 1, lit * 0.35f), 1f);
                // 두 칸마다 판 이음매
                if ((horiz ? c.X : c.Y) % 2 == 0)
                    ci.DrawLine(edgeA, edgeA + outDir * 8f, SkinSeam, 1.5f);
                // 리벳
                var mid = (edgeA + edgeB) * 0.5f + outDir * 4.5f;
                var side = horiz ? new Vector2(T * 0.3f, 0f) : new Vector2(0f, T * 0.3f);
                ci.Circle(mid - side, 1f, new Color("#566377"), true, -1f, true);
                ci.Circle(mid + side, 1f, new Color("#566377"), true, -1f, true);
            }
        }
    }

    /// <summary>창문: 방 종류마다 세 칸에 하나, 함교는 통유리. 불 켜진 방이면 유리가 밝다.</summary>
    private void PaintWindows(CanvasItem ci)
    {
        foreach (var (c, o, room, canopy) in _windows)
        {
            var r = CellRect(c);
            bool horiz = o.Y != 0;
            float thick = canopy ? 12f : 9f, len = canopy ? T : T * 0.7f;
            var center = r.GetCenter();
            var size = horiz ? new Vector2(len, thick) : new Vector2(thick, len);
            var rect = new Rect2(center - size * 0.5f, size);
            bool lit = room.Powered && !room.LightsOut && !room.Abandoned;
            var glass = lit ? Glass.Lerp(GlassLit, canopy ? 0.35f : 0.25f) : Glass;
            ci.Box(rect.Grow(2f), new Color("#5a6679"));
            ci.Box(rect, glass);
            // 반사 (유리 위쪽 가장자리)
            var g0 = horiz ? new Vector2(rect.Position.X + 2, rect.Position.Y + 2) : new Vector2(rect.Position.X + 2, rect.Position.Y + 2);
            var g1 = horiz ? new Vector2(rect.End.X - (canopy ? 2 : 8), rect.Position.Y + 2) : new Vector2(rect.Position.X + 2, rect.End.Y - (canopy ? 2 : 8));
            ci.DrawLine(g0, g1, new Color(1, 1, 1, lit ? 0.35f : 0.15f), 1.2f);
            if (canopy)
            {
                // 조종석 틀 (칸마다 세로 창살)
                if (horiz) ci.DrawLine(new Vector2(r.Position.X, rect.Position.Y), new Vector2(r.Position.X, rect.End.Y), new Color("#5a6679"), 2f);
                else ci.DrawLine(new Vector2(rect.Position.X, r.Position.Y), new Vector2(rect.End.X, r.Position.Y), new Color("#5a6679"), 2f);
            }
        }
    }

    /// <summary>창밖으로 새는 방 불빛 (더하기 섞기 층).</summary>
    private void PaintWindowGlow(CanvasItem ci)
    {
        foreach (var (c, o, room, canopy) in _windows)
        {
            if (!room.Powered || room.LightsOut || room.Abandoned) continue;
            var r = CellRect(c);
            var outDir = new Vector2(o.X, o.Y);
            var baseCenter = r.GetCenter() + outDir * T * 0.5f;
            var across = o.Y != 0 ? new Vector2(1, 0) : new Vector2(0, 1);
            float half = canopy ? T * 0.5f : T * 0.35f, reach = canopy ? T * 1.1f : T * 0.8f;
            var col = canopy ? new Color(0.35f, 0.75f, 1f, 0.16f) : new Color(1f, 0.85f, 0.6f, 0.12f);
            var clear = new Color(col.R, col.G, col.B, 0f);
            ci.Polygon(new[] { baseCenter - across * half, baseCenter + across * half, baseCenter + across * half * 1.8f + outDir * reach, baseCenter - across * half * 1.8f + outDir * reach },
                new[] { col, col, clear, clear });
        }
    }

    /// <summary>엔진 노즐: 엔진 코어마다 선체 뒤로 벌어진 종 모양.</summary>
    private void PaintNozzles(CanvasItem ci)
    {
        foreach (var f in _world.Ship.FurnitureOf(FurnitureType.EngineCore))
        {
            if (f.Room.Detached) continue;
            float xw = (f.MinX - 1) * T + 2f;               // 선체 바깥 가장자리 근처
            float y0 = f.MinY * T + 8f, y1 = (f.MaxY + 1) * T - 8f;
            float xe = xw - NozzleLen - 10f;
            var bell = new[] { new Vector2(xw, y0 + 4f), new Vector2(xw, y1 - 4f), new Vector2(xe, y1 + 6f), new Vector2(xe, y0 - 6f) };
            ci.Poly(bell, new Color("#1a1f28"));
            ci.Polyline(new[] { bell[0], bell[3], bell[2], bell[1] }, new Color("#56627a"), 2f, true);
            // 냉각 고리
            for (int k = 1; k <= 3; k++)
            {
                float x = Mathf.Lerp(xw, xe, k / 4f);
                float spread = Mathf.Lerp(0f, 10f, k / 4f);
                ci.DrawLine(new Vector2(x, y0 + 4f - spread), new Vector2(x, y1 - 4f + spread), new Color("#2f3848"), 2f);
            }
            ci.DrawLine(new Vector2(xe, y0 - 6f), new Vector2(xe, y1 + 6f), new Color("#8894a8"), 2.5f);
        }
    }

    /// <summary>꼬리 날개: 엔진실 위아래로 뒤로 젖힌 날개 (끝에 항해등).</summary>
    private void PaintFins(CanvasItem ci)
    {
        var eng = _world.Ship.RoomsOf(RoomType.Engine).FirstOrDefault();
        if (eng == null || eng.Detached) return;
        float x0 = eng.MinX * T, x1 = (eng.MaxX + 1) * T;
        float span = T * 2.4f;
        foreach (int dir in new[] { -1, 1 })
        {
            float yb = dir < 0 ? (eng.MinY - 1) * T + 4f : (eng.MaxY + 2) * T - 4f;
            var tip = new Vector2(x0 - T * 1.2f, yb + dir * span);
            var fin = new[] { new Vector2(x0 + T * 0.4f, yb), new Vector2(x1 + T * 0.6f, yb), new Vector2(x0 + T * 0.9f, yb + dir * span), tip };
            ci.Poly(fin, SkinOuter);
            ci.Polyline(new[] { fin[0], fin[3], fin[2], fin[1] }, SkinEdge, 3f, true);
            // 줄무늬 (뒤쪽 가장자리)
            var s0 = fin[0].Lerp(fin[3], 0.55f);
            var s1 = fin[1].Lerp(fin[2], 0.55f);
            ci.DrawLine(s0, s1, new Color("#c0392b").WithAlpha(0.7f), 3f, true);
            ci.DrawLine(s0.Lerp(fin[3], 0.25f), s1.Lerp(fin[2], 0.25f), new Color("#d8dee8").WithAlpha(0.4f), 1.5f, true);
            ci.DrawLine(fin[3], fin[2], SkinInner, 1.5f, true);
        }
        _finTop = new Vector2(x0 - T * 1.2f, (eng.MinY - 1) * T + 4f - span);
        _finBottom = new Vector2(x0 - T * 1.2f, (eng.MaxY + 2) * T - 4f + span);
    }

    /// <summary>항해등: 위(좌현) 빨강, 아래(우현) 초록이 천천히, 코끝 흰 섬광이 짧게.</summary>
    private void PaintNavLights(CanvasItem ci)
    {
        if (_hullCells == null) return;
        bool on = _world.Power.BatteryPercent > 0.02f || _world.Power.ReactorOnline;
        if (!on) return;
        float slow = 0.55f + 0.45f * Mathf.Sin(_time * 2f);
        void Lamp(Vector2 p, Color c, float a)
        {
            ci.Circle(p, 9f, c.WithAlpha(0.12f * a), true, -1f, true);
            ci.Circle(p, 4.5f, c.WithAlpha(0.35f * a), true, -1f, true);
            ci.Circle(p, 2.2f, c.Lightened(0.4f).WithAlpha(a), true, -1f, true);
        }
        var port = _finTop; var star = _finBottom;
        if (_world.Origin.Info is ShipInfo si && Shaped(si.Frame)) { BuildEdges(); port = Extreme(true) + new Vector2(-T * 0.6f, -6f); star = Extreme(false) + new Vector2(-T * 0.6f, 6f); } // v19 모양 있는 배: 맨 위 · 맨 아래 선체 끝
        Lamp(port, PortLight, slow);
        Lamp(star, StarboardLight, slow);
        float strobe = Mathf.PosMod(_time, 1.6f) < 0.12f ? 1f : 0.15f;
        Lamp(_noseTip, Colors.White, strobe);
    }
}
