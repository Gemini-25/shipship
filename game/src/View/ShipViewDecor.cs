using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v10.9 방 꾸밈 (그림일 뿐 — 길을 막거나 쓰이지 않는다):
/// - 벽에 붙은 것: 환기구, 계기판, 소화기, 압력계, 경고판, 개인 사물함, 화면, 화분, 구급함, 공구판, 방 번호판 — 방 종류마다 다르게
/// - 바닥: 원자로 둘레 경고 띠, 환기 댐퍼 자리의 바닥 격자, 수경재배실·주방 배수구, 휴게실·침실 깔개, 통로 구역 표시
/// </summary>
public partial class ShipView
{
    private enum Prop { Vent, Panel, Extinguisher, Gauge, Hazard, Locker, Screen, Plant, MedBox, ToolBoard, Sign, Tank, Pipe }

    private static float H3(int x, int y, int k) => Mathf.PosMod(Mathf.Sin(x * 12.9898f + y * 78.233f + k * 37.719f) * 43758.5453f, 1f);

    private static Prop[] PropsFor(RoomType t) => t switch
    {
        RoomType.Reactor => new[] { Prop.Hazard, Prop.Gauge, Prop.Panel, Prop.Pipe, Prop.Gauge },
        RoomType.Cooling => new[] { Prop.Gauge, Prop.Pipe, Prop.Tank, Prop.Panel },
        RoomType.Power => new[] { Prop.Panel, Prop.Hazard, Prop.Gauge, Prop.Panel },
        RoomType.Engine => new[] { Prop.Pipe, Prop.Gauge, Prop.Hazard, Prop.Panel },
        RoomType.Workshop => new[] { Prop.ToolBoard, Prop.ToolBoard, Prop.Panel, Prop.Locker },
        RoomType.Storage => new[] { Prop.Sign, Prop.Locker, Prop.Panel },
        RoomType.Galley => new[] { Prop.Locker, Prop.Panel, Prop.Screen },
        RoomType.Mess => new[] { Prop.Screen, Prop.Plant, Prop.Panel },
        RoomType.Quarters => new[] { Prop.Locker, Prop.Locker, Prop.Plant, Prop.Screen },
        RoomType.Medbay => new[] { Prop.MedBox, Prop.Screen, Prop.Locker },
        RoomType.Lounge => new[] { Prop.Screen, Prop.Plant, Prop.Plant },
        RoomType.Bridge => new[] { Prop.Screen, Prop.Screen, Prop.Panel },
        RoomType.Comms => new[] { Prop.Screen, Prop.Panel, Prop.Gauge },
        RoomType.Hydroponics => new[] { Prop.Tank, Prop.Plant, Prop.Pipe, Prop.Gauge },
        RoomType.LifeSupport => new[] { Prop.Tank, Prop.Gauge, Prop.Pipe, Prop.Panel },
        RoomType.Airlock => new[] { Prop.Hazard, Prop.Panel, Prop.Extinguisher },
        _ => new[] { Prop.Vent, Prop.Panel },
    };

    private static string RoomCode(RoomType t) => t switch
    {
        RoomType.Reactor => "RX", RoomType.Cooling => "CL", RoomType.Power => "PW", RoomType.Engine => "EN", RoomType.Workshop => "WS",
        RoomType.Storage => "ST", RoomType.Galley => "GL", RoomType.Mess => "MS", RoomType.Quarters => "QT", RoomType.Medbay => "MD",
        RoomType.Lounge => "LG", RoomType.Bridge => "BR", RoomType.Comms => "CM", RoomType.Hydroponics => "HY", RoomType.LifeSupport => "LS",
        RoomType.Airlock => "AL", _ => RoomCatalog.Of(t)?.Short ?? "CR",
    };

    /// <summary>벽에 붙은 것들. 방마다 안쪽 벽을 훑어 네댓 칸에 하나, 문 옆은 소화기와 번호판.</summary>
    private void PaintWallProps(CanvasItem ci)
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        var doors = new HashSet<Cell>(ship.Doors.Select(d => d.Cell));
        var windowCells = new HashSet<Cell>(_windows.Select(w => w.cell));
        foreach (var room in ship.Rooms)
        {
            if (room.Detached) continue;
            var props = PropsFor(room.Type);
            bool signDone = false;
            foreach (var c in room.Cells.OrderBy(c => c.Y).ThenBy(c => c.X))
            foreach (var d in Cell.Dirs4)
            {
                var w = c + d;
                if (g.Kind(w) != TileKind.Wall || windowCells.Contains(w)) continue;
                var inward = new Vector2(-d.X, -d.Y);
                bool nearDoor = Cell.Dirs4.Any(e => doors.Contains(w + e));
                // 문 옆: 번호판 (한 번) · 소화기
                if (nearDoor)
                {
                    if (!signDone && room.Type != RoomType.Corridor) { DrawProp(ci, Prop.Sign, w, inward, room); signDone = true; }
                    else if (H3(w.X, w.Y, 3) < 0.5f) DrawProp(ci, Prop.Extinguisher, w, inward, room);
                    continue;
                }
                float h = H3(w.X, w.Y, (int)room.Type);
                if (room.Type == RoomType.Corridor)
                {
                    if ((w.X + w.Y) % 9 == 0 && d.Y != 0) DrawProp(ci, h < 0.5f ? Prop.Vent : Prop.Panel, w, inward, room);
                    continue;
                }
                if (h > 0.3f) continue;
                DrawProp(ci, props[(int)(H3(w.X, w.Y, 11) * props.Length) % props.Length], w, inward, room);
            }
        }
    }

    private static void DrawProp(CanvasItem ci, Prop p, Cell wall, Vector2 inward, Room room)
    {
        var wr = CellRect(wall);
        var c = wr.GetCenter() + inward * (T * 0.5f - 4f);   // 벽 안쪽 가장자리
        bool vert = Mathf.Abs(inward.X) > 0.5f;              // 벽이 세로(왼쪽·오른쪽)
        Rect2 Plate(float along, float depth)
        {
            var size = vert ? new Vector2(depth, along) : new Vector2(along, depth);
            return new Rect2(c - size * 0.5f + inward * (depth * 0.5f - 2f), size);
        }
        var dark = new Color("#0a0d12");
        switch (p)
        {
            case Prop.Vent:
            {
                var r = Plate(18f, 7f);
                ci.Box(r.Grow(1f), dark);
                ci.Box(r, new Color("#262d39"));
                for (int k = 1; k < 5; k++)
                {
                    float f = k / 5f;
                    if (vert) ci.DrawLine(new Vector2(r.Position.X + 1, r.Position.Y + r.Size.Y * f), new Vector2(r.End.X - 1, r.Position.Y + r.Size.Y * f), dark, 1f);
                    else ci.DrawLine(new Vector2(r.Position.X + r.Size.X * f, r.Position.Y + 1), new Vector2(r.Position.X + r.Size.X * f, r.End.Y - 1), dark, 1f);
                }
                break;
            }
            case Prop.Panel:
            {
                var r = Plate(16f, 6f);
                ci.Box(r.Grow(1f), dark);
                ci.Box(r, new Color("#3a4454"));
                var led = r.GetCenter() - (vert ? new Vector2(0, 4) : new Vector2(4, 0));
                ci.Circle(led, 1.3f, new Color("#6ee7b7"), true, -1f, true);
                ci.Circle(led + (vert ? new Vector2(0, 4) : new Vector2(4, 0)), 1.3f, new Color("#ffd166"), true, -1f, true);
                break;
            }
            case Prop.Extinguisher:
            {
                var q = c + inward * 3f;
                ci.Circle(q + new Vector2(1, 1.5f), 4.2f, new Color(0, 0, 0, 0.4f), true, -1f, true);
                ci.Circle(q, 4f, new Color("#c0392b"), true, -1f, true);
                ci.Circle(q - new Vector2(1.2f, 1.2f), 1.5f, new Color("#ff8a7a"), true, -1f, true);
                ci.DrawLine(q, q - inward * 5f, new Color("#1a1a1a"), 1.5f);
                break;
            }
            case Prop.Gauge:
            {
                var q = c + inward * 3f;
                ci.Circle(q, 5.5f, dark, true, -1f, true);
                ci.Circle(q, 4.5f, new Color("#d8dee8"), true, -1f, true);
                float a = -2.2f + H3(wall.X, wall.Y, 5) * 2.8f;
                ci.DrawLine(q, q + Vector2.FromAngle(a) * 3.5f, new Color("#c0392b"), 1f, true);
                ci.Arc(q, 4.5f, 0.3f, 1.2f, 6, new Color("#c0392b"), 1f, true);
                break;
            }
            case Prop.Hazard:
            {
                var r = Plate(20f, 6f);
                ci.Box(r.Grow(1f), dark);
                ci.Box(r, new Color("#e0b64a"));
                for (int k = 0; k < 5; k++)
                {
                    float f = (k + 0.5f) / 5f;
                    var a = vert ? new Vector2(r.Position.X, r.Position.Y + r.Size.Y * f) : new Vector2(r.Position.X + r.Size.X * f, r.Position.Y);
                    var b = vert ? a + new Vector2(r.Size.X, 3f) : a + new Vector2(3f, r.Size.Y);
                    ci.DrawLine(a, b, new Color("#1a1710"), 2f);
                }
                break;
            }
            case Prop.Locker:
            {
                var r = Plate(22f, 9f);
                ci.Box(r.Grow(1f), dark);
                ci.Box(r, new Color("#3b4760").Lerp(Palette.Room(room.Kind), 0.15f));
                var mid = r.GetCenter();
                if (vert) ci.DrawLine(new Vector2(r.Position.X, mid.Y), new Vector2(r.End.X, mid.Y), dark, 1f);
                else ci.DrawLine(new Vector2(mid.X, r.Position.Y), new Vector2(mid.X, r.End.Y), dark, 1f);
                ci.Circle(mid + (vert ? new Vector2(0, -3) : new Vector2(-3, 0)), 0.9f, new Color("#9aa6b5"), true, -1f, true);
                ci.Circle(mid + (vert ? new Vector2(0, 3) : new Vector2(3, 0)), 0.9f, new Color("#9aa6b5"), true, -1f, true);
                break;
            }
            case Prop.Screen:
            {
                var r = Plate(20f, 5f);
                ci.Box(r.Grow(1.5f), dark);
                ci.Box(r, new Color("#0f2a36"));
                var glow = room.Type is RoomType.Bridge or RoomType.Comms ? new Color("#6ee7b7") : new Color("#7fb2ff");
                for (int k = 0; k < 3; k++)
                {
                    float f = (k + 1) / 4f, len = 0.35f + 0.5f * H3(wall.X, wall.Y, k);
                    if (vert) ci.DrawLine(new Vector2(r.Position.X + r.Size.X * f, r.Position.Y + 2), new Vector2(r.Position.X + r.Size.X * f, r.Position.Y + 2 + (r.Size.Y - 4) * len), glow.WithAlpha(0.7f), 1f);
                    else ci.DrawLine(new Vector2(r.Position.X + 2, r.Position.Y + r.Size.Y * f), new Vector2(r.Position.X + 2 + (r.Size.X - 4) * len, r.Position.Y + r.Size.Y * f), glow.WithAlpha(0.7f), 1f);
                }
                break;
            }
            case Prop.Plant:
            {
                var q = c + inward * 5f;
                ci.Circle(q, 5f, new Color("#4a3a2a"), true, -1f, true);
                for (int k = 0; k < 5; k++)
                {
                    var d = Vector2.FromAngle(k * 1.25f + H3(wall.X, wall.Y, 2) * 3f);
                    ci.Circle(q + d * 3.5f, 3.2f, new Color("#3f8f4a").Lerp(new Color("#8fd65a"), H3(wall.X, wall.Y, k)), true, -1f, true);
                }
                break;
            }
            case Prop.MedBox:
            {
                var r = Plate(14f, 8f);
                ci.Box(r.Grow(1f), dark);
                ci.Box(r, new Color("#e8ecf2"));
                var m = r.GetCenter();
                ci.Box(new Rect2(m - new Vector2(1.2f, 3.5f), new Vector2(2.4f, 7f)), new Color("#d64545"));
                ci.Box(new Rect2(m - new Vector2(3.5f, 1.2f), new Vector2(7f, 2.4f)), new Color("#d64545"));
                break;
            }
            case Prop.ToolBoard:
            {
                var r = Plate(26f, 6f);
                ci.Box(r.Grow(1f), dark);
                ci.Box(r, new Color("#4a4030"));
                for (int k = 0; k < 4; k++)
                {
                    float f = (k + 0.5f) / 4f;
                    var a = vert ? new Vector2(r.GetCenter().X, r.Position.Y + r.Size.Y * f) : new Vector2(r.Position.X + r.Size.X * f, r.GetCenter().Y);
                    var tool = k % 2 == 0 ? new Color("#9aa6b5") : new Color("#c0392b");
                    ci.DrawLine(a - (vert ? new Vector2(2, 0) : new Vector2(0, 2)), a + (vert ? new Vector2(2, 1) : new Vector2(1, 2)), tool, 1.5f);
                }
                break;
            }
            case Prop.Sign:
            {
                var r = Plate(20f, 9f);
                ci.Box(r.Grow(1f), dark);
                ci.Box(r, Palette.Room(room.Kind).Darkened(0.45f));
                ci.Box(r, Palette.Room(room.Kind).WithAlpha(0.8f), false, 1f);
                Gfx.TextCentered(ci, Fonts.Bold, r.GetCenter(), $"{RoomCode(room.Kind)}-{room.Id + 1}", 6, new Color("#e6edf3"));
                break;
            }
            case Prop.Tank:
            {
                var q = c + inward * 6f;
                ci.Circle(q + new Vector2(1, 1.5f), 7f, new Color(0, 0, 0, 0.35f), true, -1f, true);
                ci.Circle(q, 6.5f, new Color("#2c3a44"), true, -1f, true);
                ci.Arc(q, 6.5f, 0f, Mathf.Tau, 20, new Color("#6a7a88"), 1.5f, true);
                ci.Arc(q, 4f, 3.6f, 5.2f, 8, new Color(1, 1, 1, 0.3f), 1.2f, true);
                var liquid = room.Type == RoomType.Hydroponics ? new Color("#8fd65a") : room.Type == RoomType.Cooling ? new Color("#5fd0c8") : new Color("#8fd3ff");
                ci.Circle(q, 2.2f, liquid.WithAlpha(0.8f), true, -1f, true);
                break;
            }
            case Prop.Pipe:
            {
                // 벽을 따라 가는 짧은 관 두 줄과 받침
                var along = vert ? new Vector2(0, 1) : new Vector2(1, 0);
                var q = c + inward * 2f;
                for (int k = 0; k < 2; k++)
                {
                    var o = inward * (k * 3.5f);
                    ci.DrawLine(q + o - along * T * 0.5f, q + o + along * T * 0.5f, dark, 3.5f);
                    ci.DrawLine(q + o - along * T * 0.5f, q + o + along * T * 0.5f, new Color("#56627a"), 2f);
                }
                ci.DrawLine(q - inward * 1f, q + inward * 6f, new Color("#8894a8"), 2f);
                break;
            }
        }
    }

    /// <summary>바닥 꾸밈: 원자로 경고 띠, 바닥 격자, 배수구, 깔개, 통로 구역 표시.</summary>
    private void PaintFloorDecor(CanvasItem ci)
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        foreach (var room in ship.Rooms)
        {
            if (room.Detached) continue;
            // 바닥 격자 (환기 댐퍼 자리)
            if (room.Type != RoomType.Corridor && g.Kind(room.DamperSpot) == TileKind.Floor)
            {
                var r = CellRect(room.DamperSpot).Grow(-7f);
                ci.Box(r.Grow(1f), new Color("#07090d"));
                ci.Box(r, new Color("#1c222c"));
                for (int k = 1; k < 6; k++) ci.DrawLine(new Vector2(r.Position.X + r.Size.X * k / 6f, r.Position.Y + 1), new Vector2(r.Position.X + r.Size.X * k / 6f, r.End.Y - 1), new Color("#07090d"), 1.5f);
            }
            switch (room.Type)
            {
                case RoomType.Hydroponics:
                case RoomType.Galley:
                    foreach (var c in room.Cells.Where(c => ship.IsOpenFloor(c) && H3(c.X, c.Y, 21) < 0.06f))
                    {
                        var q = CellRect(c).GetCenter();
                        ci.Circle(q, 4.5f, new Color("#0a0d12"), true, -1f, true);
                        ci.Arc(q, 4.5f, 0f, Mathf.Tau, 16, new Color("#3a4454"), 1f, true);
                        ci.DrawLine(q - new Vector2(3, 0), q + new Vector2(3, 0), new Color("#3a4454"), 1f);
                        ci.DrawLine(q - new Vector2(0, 3), q + new Vector2(0, 3), new Color("#3a4454"), 1f);
                    }
                    break;
                case RoomType.Lounge:
                case RoomType.Mess:
                    foreach (var t in room.Furniture.Where(f => f.Type == FurnitureType.Table))
                    {
                        var r = FurnitureRect(t).Grow(T * 0.85f);
                        var rug = room.Type == RoomType.Lounge ? new Color("#3a2a3f") : new Color("#2e3326");
                        Gfx.RoundRect(ci, r, rug.WithAlpha(0.55f), 10, rug.Lightened(0.25f).WithAlpha(0.5f));
                        Gfx.RoundRect(ci, r.Grow(-4f), new Color(0, 0, 0, 0f), 8, rug.Lightened(0.15f).WithAlpha(0.35f));
                    }
                    break;
                case RoomType.Quarters:
                    foreach (var b in room.Furniture.Where(f => f.Type == FurnitureType.Bed))
                    {
                        var r = FurnitureRect(b);
                        var mat = new Rect2(r.Position.X - 2f, r.Position.Y + (b.Cells[0].Y == b.MinY ? r.Size.Y : -10f), r.Size.X + 4f, 10f);
                        Gfx.RoundRect(ci, mat, (b.Owner != null ? Palette.Crew(b.Owner.Id) : new Color("#4a5568")).Darkened(0.65f).WithAlpha(0.6f), 3);
                    }
                    break;
                case RoomType.Corridor:
                    // 구역 표시: 12칸마다 바닥에 스텐실
                    foreach (var c in room.Cells.Where(c => c.X % 12 == 6 && g.Kind(c + new Cell(0, -1)) == TileKind.Wall && ship.IsOpenFloor(c + new Cell(0, 1))))
                    {
                        var q = CellRect(c).GetCenter() + new Vector2(0, T * 0.5f);
                        Gfx.TextCentered(ci, Fonts.Bold, q, $"{(char)('A' + (c.Y / 8) % 26)}-{c.X / 12:00}", 9, new Color(1, 1, 1, 0.12f));
                    }
                    break;
            }
        }
        // 원자로 둘레 경고 띠 (노랑·검정 사선)
        foreach (var f in ship.FurnitureOf(FurnitureType.ReactorCore))
        {
            if (f.Room.Detached) continue;
            var r = FurnitureRect(f).Grow(9f);
            HazardFrame(ci, r, 5f);
        }
        foreach (var f in ship.FurnitureOf(FurnitureType.EngineCore))
        {
            if (f.Room.Detached) continue;
            HazardFrame(ci, FurnitureRect(f).Grow(5f), 4f);
        }
    }

    private static void HazardFrame(CanvasItem ci, Rect2 r, float w)
    {
        var yellow = new Color("#e0b64a").WithAlpha(0.75f);
        var black = new Color("#15130e").WithAlpha(0.85f);
        void Edge(Vector2 a, Vector2 b)
        {
            float len = a.DistanceTo(b);
            var dir = (b - a) / len;
            int n = Mathf.Max(1, (int)(len / 8f));
            for (int k = 0; k < n; k++)
                ci.DrawLine(a + dir * (len * k / n), a + dir * (len * (k + 1) / n), k % 2 == 0 ? yellow : black, w);
        }
        Edge(r.Position, new Vector2(r.End.X, r.Position.Y));
        Edge(new Vector2(r.End.X, r.Position.Y), r.End);
        Edge(r.End, new Vector2(r.Position.X, r.End.Y));
        Edge(new Vector2(r.Position.X, r.End.Y), r.Position);
    }
}
