using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v11.2 함교 홀로그램 테이블 (화면 장식 — 시뮬레이션은 모른다): 함교 바닥의 투사 패드 위에 배의 축소 모형이 떠 있다.
/// 방마다 상태 색(평상 · 급함 · 치명 · 버림), 다가오는 운석과 들어올 외벽, 연소 중인 엔진 불꽃, 잔해 지대의 부유 입자, 태양 폭풍의 잡음.
/// 함교에 전기가 없거나 주 컴퓨터가 꺼지면 모형이 지지직거리다 꺼진다.
/// </summary>
public partial class ShipView
{
    private Cell? _holoCell;
    private int _holoKey = -1;

    /// <summary>함교에서 2×2 빈 바닥 (사용 자리·문 앞이 아닌, 가운데에 가까운 곳).</summary>
    private Cell? HoloSpot()
    {
        var ship = _world.Ship;
        int key = ship.Furniture.Count * 31 + ship.Rooms.Count;
        if (key == _holoKey) return _holoCell;
        _holoKey = key;
        _holoCell = null;
        var bridge = ship.RoomsOf(RoomType.Bridge).FirstOrDefault(r => !r.Detached);
        if (bridge == null) return null;
        var reserved = new HashSet<Cell>(bridge.Furniture.SelectMany(f => f.UseSpots));
        foreach (var d in bridge.Doors) foreach (var n in Cell.Dirs8) reserved.Add(d.Cell + n);
        bool Free(Cell c) => ship.RoomAt(c) == bridge && ship.IsOpenFloor(c) && !reserved.Contains(c) && c != bridge.DamperSpot;
        _holoCell = bridge.Cells.Where(c => Free(c) && Free(c + new Cell(1, 0)) && Free(c + new Cell(0, 1)) && Free(c + new Cell(1, 1)))
            .OrderBy(c => (new System.Numerics.Vector2(c.X + 1f, c.Y + 1f) - bridge.Center).LengthSquared())
            .Cast<Cell?>().FirstOrDefault();
        return _holoCell;
    }

    /// <summary>바닥 패드 (정적 층).</summary>
    private void PaintHoloPad(CanvasItem ci)
    {
        if (HoloSpot() is not Cell c) return;
        var center = new Vector2((c.X + 1) * T, (c.Y + 1) * T);
        float r = T * 0.92f;
        ci.DrawCircle(center + new Vector2(2, 3), r, new Color(0, 0, 0, 0.3f), true, -1f, true);
        ci.DrawCircle(center, r, new Color("#0b1119"), true, -1f, true);
        ci.DrawArc(center, r, 0f, Mathf.Tau, 48, new Color("#2c3a4d"), 2f, true);
        ci.DrawArc(center, r * 0.72f, 0f, Mathf.Tau, 40, new Color("#1c2735"), 1.5f, true);
        // 투사기 눈 여섯 개
        for (int k = 0; k < 6; k++)
        {
            var d = Vector2.FromAngle(k * Mathf.Tau / 6f + 0.26f);
            ci.DrawCircle(center + d * r * 0.86f, 2.2f, new Color("#0f1a26"), true, -1f, true);
            ci.DrawCircle(center + d * r * 0.86f, 1.2f, new Color("#3b6c8f"), true, -1f, true);
        }
    }

    /// <summary>떠 있는 모형 (움직이는 층).</summary>
    private void PaintHoloTable(CanvasItem ci)
    {
        if (HoloSpot() is not Cell c) return;
        var bridge = _world.Ship.RoomAt(c);
        if (bridge == null) return;
        var center = new Vector2((c.X + 1) * T, (c.Y + 1) * T);
        bool powered = bridge.Powered;
        bool online = powered && _world.Automation.MainOnline;
        var cyan = new Color("#5fd8ff");
        // 투사기 눈: 켜져 있으면 빛난다
        float r = T * 0.92f;
        for (int k = 0; k < 6; k++)
        {
            var d = Vector2.FromAngle(k * Mathf.Tau / 6f + 0.26f);
            float a = powered ? 0.55f + 0.35f * Mathf.Sin(_time * 3f + k) : 0.08f;
            ci.DrawCircle(center + d * r * 0.86f, 1.6f, cyan.WithAlpha(a), true, -1f, true);
        }
        if (!powered) return;
        // 컴퓨터가 꺼지면 모형이 지지직거린다 (손으로 띄운 정적 도면만)
        float flicker = online ? 1f : (Mathf.Sin(_time * 23f) > 0.3f ? 0.35f : 0.08f);
        var storm = _world.Hazards.StormActive;
        if (storm) flicker *= 0.75f + 0.25f * Mathf.Sin(_time * 37f);

        // 빛기둥 (패드에서 위로)
        var top = center + new Vector2(0, -T * 0.55f);
        ci.DrawColoredPolygon(new[] { center + new Vector2(-r * 0.8f, 0), center + new Vector2(r * 0.8f, 0), top + new Vector2(T * 1.7f, 0), top + new Vector2(-T * 1.7f, 0) },
            cyan.WithAlpha(0.08f * flicker));
        ci.DrawCircle(center, r * 0.7f, cyan.WithAlpha(0.06f * flicker), true, -1f, true);

        // 모형: 배 전체를 가로 2.6칸 폭으로 줄이고, 위로 눌러 비스듬히
        var ship = _world.Ship;
        var b = Bounds;
        if (b.Size.X < 1f) return;
        float width = T * 3.4f;
        float s = width / b.Size.X;
        float squash = 0.55f;
        var origin = top + new Vector2(-width * 0.5f, -b.Size.Y * s * squash * 0.5f + Mathf.Sin(_time * 1.2f) * 1.5f);
        Vector2 P(Vector2 px) => origin + new Vector2((px.X - b.Position.X) * s, (px.Y - b.Position.Y) * s * squash);
        foreach (var room in ship.Rooms)
        {
            if (room.Detached || room.Cells.Count == 0) continue;
            var sev = Severity.Of(_world, room);
            var col = sev switch
            {
                RoomSeverity.Critical => Palette.Danger,
                RoomSeverity.Urgent => Palette.Warning,
                RoomSeverity.Abandoned => new Color("#6b7280"),
                _ => room == bridge ? new Color("#ffffff") : cyan,
            };
            float pulse = sev is RoomSeverity.Critical ? 0.6f + 0.4f * Mathf.Sin(_time * 7f) : 1f;
            var a = P(new Vector2(room.MinX * T, room.MinY * T));
            var e = P(new Vector2((room.MaxX + 1) * T, (room.MaxY + 1) * T));
            var rr = new Rect2(a, e - a);
            ci.DrawRect(rr, col.WithAlpha((room.Type == RoomType.Corridor ? 0.12f : 0.3f) * flicker * pulse));
            ci.DrawRect(rr, col.WithAlpha(0.85f * flicker * pulse), false, 1f);
        }
        // 선체 윤곽 (빛나는 테)
        var hullA = P(b.Position);
        var hullE = P(b.End);
        ci.DrawRect(new Rect2(hullA, hullE - hullA).Grow(2f), cyan.WithAlpha(0.35f * flicker), false, 1.2f);
        // 엔진 연소: 모형 뒤쪽 엔진 자리에서 불꽃
        if (_world.Propulsion.Burning || _world.Propulsion.CourseBurnVisible)
            foreach (var f in ship.FurnitureOf(FurnitureType.EngineCore))
            {
                if (f.Room.Detached) continue;
                var p = P(FurnitureRect(f).GetCenter());
                float len = 6f + 3f * Mathf.Sin(_time * 30f);
                ci.DrawLine(p, p + new Vector2(-len, 0), new Color("#ffb070").WithAlpha(0.9f * flicker), 2f, true);
            }
        // 다가오는 운석: 밖에서 들어올 외벽 쪽으로 (남은 시간만큼 떨어져서)
        foreach (var m in _world.Sensors.Incoming)
        {
            if (m.Warned == WarnLevel.None && online) continue; // 배가 아직 모른다
            var entry = P(CellRect(m.Entry).GetCenter());
            float left = m.MinutesLeft(_world.Tick) / Mathf.Max(0.1f, SensorSystem.ApproachMinutes);
            var dir = new Vector2(m.Direction.X, m.Direction.Y * squash).Normalized();
            var at = entry - dir * (6f + 26f * left);
            ci.DrawDashedLine(at, entry, Palette.Danger.WithAlpha(0.45f * flicker), 1f, 2.5f);
            ci.DrawCircle(at, 1.5f + m.Size * 1.5f, Palette.Danger.WithAlpha(0.95f * flicker), true, -1f, true);
            ci.DrawArc(entry, 3f + 1.5f * Mathf.Sin(_time * 8f), 0f, Mathf.Tau, 16, Palette.Danger.WithAlpha(0.7f * flicker), 1f, true);
        }
        // 잔해 지대: 모형 둘레에 떠도는 입자
        if (_world.Propulsion.Zone == ZoneKind.Debris)
            for (int k = 0; k < 14; k++)
            {
                float ph = _time * (0.15f + 0.03f * (k % 5)) + k * 0.91f;
                var p = top + new Vector2(Mathf.Cos(ph) * width * 0.62f, Mathf.Sin(ph * 1.3f) * width * 0.3f);
                ci.DrawCircle(p, 0.9f, new Color("#c9b48a").WithAlpha(0.7f * flicker), true, -1f, true);
            }
        // 태양 폭풍: 보라 잡음 줄
        if (storm)
            for (int k = 0; k < 3; k++)
            {
                float y = origin.Y + Mathf.PosMod(_time * 20f + k * 13f, b.Size.Y * s * squash + 6f) - 3f;
                ci.DrawLine(new Vector2(origin.X - 2, y), new Vector2(origin.X + width + 2, y), new Color("#c9a0ff").WithAlpha(0.35f), 0.8f);
            }
        // 스캔 줄
        float scan = Mathf.PosMod(_time * 0.6f, 1f);
        float sy = origin.Y + scan * b.Size.Y * s * squash;
        ci.DrawLine(new Vector2(origin.X, sy), new Vector2(origin.X + width, sy), cyan.WithAlpha(0.3f * flicker), 0.8f);
    }
}
