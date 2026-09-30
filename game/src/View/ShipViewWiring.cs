using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v10.9 배선: 통로 벽을 따라 도는 케이블 트레이(회로 A~D 네 가닥), 방마다 문 옆 벽을 뚫고 들어가는 분기선과 분전함,
/// 분전함에서 벽을 따라 가는 간선과 설비로 내려가는 선. 선 색은 그 방의 회로 — 회로가 죽으면 어둡게, 임시 배선으로 살면 노랗게.
/// 배전실은 배전반에서 분전함까지 굵은 다발. 그림일 뿐 (전기 규칙은 PowerGrid).
/// </summary>
public partial class ShipView
{
    public static readonly Color[] CircuitColors = { new("#ff8a5c"), new("#ffd166"), new("#5fd0c8"), new("#a78bfa") };

    private sealed class RoomWiring
    {
        public Room Room = null!;
        public Vector2 Box, Normal;
        public Vector2 FeedA, FeedB;
        public Vector2 TrunkA, TrunkB;
        public readonly List<(Vector2 a, Vector2 b)> Drops = new();
        public Vector2? Bundle; // 배전반
    }

    private readonly List<(Vector2 a, Vector2 b, bool vertical)> _trays = new();
    private readonly List<RoomWiring> _wiring = new();
    private int _wiringVersion = -1, _wiringRooms = -1;
    private int _circuitState = -1;

    private void BuildWiring()
    {
        _trays.Clear();
        _wiring.Clear();
        var ship = _world.Ship;
        var g = ship.Grid;
        foreach (var corr in ship.RoomsOf(RoomType.Corridor))
        foreach (var c in corr.Cells)
        {
            var r = CellRect(c);
            if (g.Kind(c + new Cell(0, -1)) == TileKind.Wall)
                _trays.Add((new Vector2(r.Position.X, r.Position.Y + 5f), new Vector2(r.End.X, r.Position.Y + 5f), false));
            else if (g.Kind(c + new Cell(-1, 0)) == TileKind.Wall && g.Kind(c + new Cell(0, 1)) != TileKind.Wall)
                _trays.Add((new Vector2(r.Position.X + 5f, r.Position.Y), new Vector2(r.Position.X + 5f, r.End.Y), true));
        }

        foreach (var room in ship.Rooms)
        {
            if (room.Type == RoomType.Corridor || room.Detached) continue;
            var door = room.Doors.FirstOrDefault(d => (d.RoomA?.Type == RoomType.Corridor) || (d.RoomB?.Type == RoomType.Corridor)) ?? room.Doors.FirstOrDefault();
            if (door == null) continue;
            var dc = door.Cell;
            Cell? inward = null;
            foreach (var d in Cell.Dirs4)
                if (g.Kind(dc + d) == TileKind.Floor && ship.RoomAt(dc + d) == room) { inward = d; break; }
            if (inward is not Cell n) continue;
            var t = new Cell(n.Y != 0 ? 1 : 0, n.X != 0 ? 1 : 0);
            Cell? wallCell = null;
            foreach (int k in new[] { 1, -1, 2, -2 })
            {
                var w = dc + new Cell(t.X * k, t.Y * k);
                if (g.Kind(w) == TileKind.Wall && g.Kind(w + n) == TileKind.Floor && ship.RoomAt(w + n) == room
                    && g.Kind(w - n) is TileKind.Floor) { wallCell = w; break; }
            }
            if (wallCell is not Cell wc) continue;
            var nv = new Vector2(n.X, n.Y);
            var tv = new Vector2(t.X, t.Y);
            var wr = CellRect(wc).GetCenter();
            var rw = new RoomWiring { Room = room, Normal = nv };
            rw.FeedA = wr - nv * (T * 0.5f + 5f);
            rw.Box = wr + nv * (T * 0.5f + 5f);
            rw.FeedB = rw.Box;
            // 설비: 전기를 먹는 것 (배전실은 모두)
            var machines = room.Furniture.Where(f => f.Machine is Machine m && (m.Spec.PowerDraw > 0f || room.Type == RoomType.Power)).ToList();
            float lo = 0f, hi = 0f;
            foreach (var f in machines)
            {
                var fr = FurnitureRect(f);
                var fc = fr.GetCenter();
                float along = (fc - rw.Box).Dot(tv);
                lo = Mathf.Min(lo, along); hi = Mathf.Max(hi, along);
                var onTrunk = rw.Box + tv * along;
                // 설비의 트렁크 쪽 가장자리까지
                float depth = (fc - rw.Box).Dot(nv);
                float half = Mathf.Abs(nv.X) > 0.5f ? fr.Size.X * 0.5f : fr.Size.Y * 0.5f;
                var end = onTrunk + nv * Mathf.Max(0f, depth - half);
                if (f.Type == FurnitureType.PowerPanel) rw.Bundle = fc;
                else rw.Drops.Add((onTrunk, end));
            }
            rw.TrunkA = rw.Box + tv * lo;
            rw.TrunkB = rw.Box + tv * hi;
            _wiring.Add(rw);
        }
        _wiringVersion = _world.Structure.Version;
        _wiringRooms = ship.Rooms.Count;
    }

    private int CircuitHash()
    {
        var p = _world.Power;
        int h = 0;
        for (int i = 0; i < PowerGrid.CircuitCount; i++) h = h * 4 + (p.CircuitLive[i] ? 1 : 0) + (p.CircuitFed[i] ? 2 : 0);
        return h;
    }

    /// <summary>회로 상태가 바뀌면 정적 층을 다시 (배선 색).</summary>
    private void CheckCircuitsChanged()
    {
        int h = CircuitHash();
        if (h == _circuitState) return;
        _circuitState = h;
        RedrawStatic();
    }

    private Color WireColor(int circuit)
    {
        var p = _world.Power;
        var col = CircuitColors[circuit % CircuitColors.Length];
        if (!p.CircuitFed[circuit]) return new Color("#4a2020");
        if (!p.CircuitLive[circuit]) return new Color("#e0b64a");
        return col;
    }

    private void PaintWiring(CanvasItem ci)
    {
        if (_wiringVersion != _world.Structure.Version || _wiringRooms != _world.Ship.Rooms.Count) BuildWiring();
        _circuitState = CircuitHash();
        // 통로 트레이: 어두운 레일 위에 회로 네 가닥
        foreach (var (a, b, vertical) in _trays)
        {
            var across = vertical ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
            ci.DrawLine(a, b, new Color("#0c1016"), 8f);
            ci.DrawLine(a - across * 3.5f, b - across * 3.5f, new Color("#3a4454"), 1f);
            ci.DrawLine(a + across * 3.5f, b + across * 3.5f, new Color("#232a35"), 1f);
            for (int k = 0; k < PowerGrid.CircuitCount; k++)
                ci.DrawLine(a + across * (-2.2f + k * 1.5f), b + across * (-2.2f + k * 1.5f), WireColor(k).WithAlpha(0.55f), 1f);
        }
        // 트레이 받침 (네 칸마다)
        foreach (var (a, b, vertical) in _trays)
        {
            var c = CellAtPx(a + (vertical ? new Vector2(0, 1) : new Vector2(1, 0)));
            if ((vertical ? c.Y : c.X) % 4 != 0) continue;
            var across = vertical ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
            ci.DrawLine(a - across * 4.5f, a + across * 4.5f, new Color("#56627a"), 2f);
        }

        foreach (var rw in _wiring)
        {
            if (rw.Room.Detached) continue;
            var col = WireColor(rw.Room.Circuit);
            // 벽을 뚫는 분기선
            ci.DrawLine(rw.FeedA, rw.FeedB, new Color("#0c1016"), 4f);
            ci.DrawLine(rw.FeedA, rw.FeedB, col.WithAlpha(0.8f), 1.5f);
            // 간선 (벽을 따라)
            if (rw.TrunkA.DistanceTo(rw.TrunkB) > 1f)
            {
                ci.DrawLine(rw.TrunkA, rw.TrunkB, new Color("#0c1016").WithAlpha(0.85f), 3.5f);
                ci.DrawLine(rw.TrunkA, rw.TrunkB, col.WithAlpha(0.5f), 1.2f);
            }
            foreach (var (a, b) in rw.Drops)
            {
                if (a.DistanceTo(b) < 1f) continue;
                ci.DrawLine(a, b, new Color("#0c1016").WithAlpha(0.7f), 2.5f);
                ci.DrawLine(a, b, col.WithAlpha(0.35f), 1f);
                ci.DrawCircle(b, 1.8f, col.WithAlpha(0.6f), true, -1f, true);
            }
            if (rw.Bundle is Vector2 panel)
            {
                // 배전반 → 분전함: 굵은 다발
                var mid = new Vector2(rw.Normal.X != 0 ? panel.X : rw.Box.X, rw.Normal.X != 0 ? rw.Box.Y : panel.Y);
                var pts = new[] { rw.Box, mid, panel };
                ci.DrawPolyline(pts, new Color("#0c1016"), 9f, true);
                for (int k = 0; k < PowerGrid.CircuitCount; k++)
                {
                    var off = (rw.Normal.X != 0 ? new Vector2(0, 1) : new Vector2(1, 0)) * (-2.4f + k * 1.6f);
                    ci.DrawPolyline(pts.Select(p => p + off).ToArray(), WireColor(k).WithAlpha(0.8f), 1.2f, true);
                }
            }
            // 분전함: 벽에 붙은 작은 상자, 회로 표시등
            var box = new Rect2(rw.Box - new Vector2(6f, 5f), new Vector2(12f, 10f));
            ci.DrawRect(box.Grow(1f), new Color("#0a0d12"));
            ci.DrawRect(box, new Color("#2c3544"));
            ci.DrawRect(new Rect2(box.Position + new Vector2(2, 2), new Vector2(8, 2)), new Color("#46526a"));
            ci.DrawCircle(box.GetCenter() + new Vector2(0, 2f), 1.8f, rw.Room.Powered ? col : new Color("#3a1a1a"), true, -1f, true);
        }
    }
}
