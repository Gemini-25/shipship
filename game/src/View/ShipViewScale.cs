using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.18 배 도면 위의 사고 규모: 번진 방마다 규모별 테두리 (방 실선 · 계통 흐르는 점선 · 배 전체 맥박 겹선 · 우주급 별빛 번짐)
// + 배 전체 소집이면 모일 곳에 깃발과 점호 인원. 읽기만 한다 (방 윤곽은 칸 수가 바뀔 때만 다시 잰다).
public partial class ShipView
{
    private readonly Dictionary<int, (int cells, List<(Vector2 a, Vector2 b)> edges)> _scaleOutline = new();

    private List<(Vector2 a, Vector2 b)> RoomOutline(Room r)
    {
        if (_scaleOutline.TryGetValue(r.Id, out var c) && c.cells == r.Cells.Count) return c.edges;
        var set = new HashSet<Cell>(r.Cells);
        var edges = new List<(Vector2, Vector2)>();
        foreach (var cell in r.Cells)
        {
            float x = cell.X * T, y = cell.Y * T;
            if (!set.Contains(new Cell(cell.X, cell.Y - 1))) edges.Add((new Vector2(x, y), new Vector2(x + T, y)));
            if (!set.Contains(new Cell(cell.X, cell.Y + 1))) edges.Add((new Vector2(x, y + T), new Vector2(x + T, y + T)));
            if (!set.Contains(new Cell(cell.X - 1, cell.Y))) edges.Add((new Vector2(x, y), new Vector2(x, y + T)));
            if (!set.Contains(new Cell(cell.X + 1, cell.Y))) edges.Add((new Vector2(x + T, y), new Vector2(x + T, y + T)));
        }
        _scaleOutline[r.Id] = (r.Cells.Count, edges);
        return edges;
    }

    private void PaintScaleRooms(CanvasItem ci)
    {
        var sc = _world.Scale;
        foreach (var k in sc.OpenCases)
        {
            if (!k.Hot || k.Now < IncidentScale.Room) continue;
            var col = Hud.ScaleColor(k.Now);
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * (k.Now >= IncidentScale.Ship ? 5f : 2.5f) + k.Id);
            foreach (int id in k.Rooms)
            {
                if (id < 0 || id >= _world.Ship.Rooms.Count) continue;
                var room = _world.Ship.Rooms[id];
                if (room.Detached) continue;
                var edges = RoomOutline(room);
                foreach (var (a, b) in edges)
                {
                    switch (k.Now)
                    {
                        case IncidentScale.Room:
                            ci.DrawLine(a, b, col.WithAlpha(0.55f), 2f);
                            break;
                        case IncidentScale.System:
                        {
                            // 흐르는 점선 (망을 타고 번진다)
                            float len = (b - a).Length();
                            var dir = (b - a) / len;
                            float off = Mathf.PosMod(_time * 22f + (a.X + a.Y) * 0.5f, 12f);
                            for (float t0 = -off; t0 < len; t0 += 12f)
                            {
                                float s0 = Mathf.Max(0f, t0), s1 = Mathf.Min(len, t0 + 7f);
                                if (s1 > s0) ci.DrawLine(a + dir * s0, a + dir * s1, col.WithAlpha(0.8f), 2.4f);
                            }
                            break;
                        }
                        case IncidentScale.Ship:
                            ci.DrawLine(a, b, col.WithAlpha(0.45f + 0.45f * pulse), 3.2f);
                            ci.DrawLine(a, b, new Color(1, 1, 1, 0.25f * pulse), 1f);
                            break;
                        default:
                            ci.DrawLine(a, b, col.WithAlpha(0.18f + 0.12f * pulse), 7f);
                            ci.DrawLine(a, b, col.WithAlpha(0.75f), 1.6f);
                            break;
                    }
                }
                if (k.Now == IncidentScale.Cosmic && edges.Count > 0)
                    for (int i = 0; i < 4; i++)
                    {
                        var (a, b) = edges[(int)Mathf.PosMod(_time * 3f + i * 7 + id, edges.Count)];
                        ci.Circle(a.Lerp(b, Mathf.PosMod(_time * 0.7f + i * 0.31f, 1f)), 2f, Colors.White.WithAlpha(0.8f));
                    }
            }
            // 규모 아이콘을 사건이 시작된 방 위에
            if (k.RoomId >= 0 && k.RoomId < _world.Ship.Rooms.Count && !_world.Ship.Rooms[k.RoomId].Detached)
            {
                var c = ToPx(_world.Ship.Rooms[k.RoomId].Center) + new Vector2(0, -T * 0.9f);
                ci.Circle(c, 11f, new Color(0.04f, 0.05f, 0.08f, 0.85f));
                ci.Arc(c, 11f, 0, Mathf.Tau, 20, col.WithAlpha(0.85f), 1.5f, true);
                Hud.ScaleIcon(ci, k.Now, c, 14f, col, _time);
            }
            // 배 전체 소집: 모일 곳에 깃발 + 점호 인원
            if (k.Now >= IncidentScale.Ship && k.MusterRoom >= 0 && k.MusterAt >= 0 && k.MusterRoom < _world.Ship.Rooms.Count)
            {
                var m = ToPx(_world.Ship.Rooms[k.MusterRoom].Center);
                var pole = m + new Vector2(-6f, 10f);
                ci.DrawLine(pole, pole + new Vector2(0, -26f), new Color("#d8dee9"), 2f, true);
                float wave = Mathf.Sin(_time * 4f) * 2f;
                ci.Poly(new[] { pole + new Vector2(0, -26f), pole + new Vector2(16f, -22f + wave), pole + new Vector2(0, -16f) }, col);
                string label = k.MusterDone ? $"점호 끝 {k.Mustered.Count}" : $"점호 {k.Mustered.Count}";
                Gfx.TextCentered(ci, Fonts.Bold, m + new Vector2(4f, 22f), label, 11, col.Lerp(Colors.White, 0.3f));
            }
        }
    }
}
