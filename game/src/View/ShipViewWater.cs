using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.3 바닥에 고인 물(물결·반사) · 습한 방의 결로 물방울 · 분전함을 내린 방의 표시.
public partial class ShipView
{
    private void PaintWater(CanvasItem ci)
    {
        foreach (var room in _world.Ship.LiveRooms)
        {
            if (room.Detached) continue;
            float depth = MoistureSystem.Depth(room);
            if (depth > 0.01f)
            {
                float a = Mathf.Clamp(0.12f + 0.45f * depth, 0f, 0.6f);
                var deep = new Color(0.16f, 0.42f, 0.78f, a);
                foreach (var c in room.Cells)
                {
                    var r = CellRect(c);
                    ci.DrawRect(r, deep);
                    // 물결: 칸마다 어긋난 가는 선이 천천히 흐른다
                    float ph = Mathf.PosMod(_time * 0.6f + (c.X * 0.37f + c.Y * 0.21f), 1f);
                    float y = r.Position.Y + T * (0.25f + 0.5f * ph);
                    ci.DrawLine(new Vector2(r.Position.X + 3, y), new Vector2(r.End.X - 5, y + 1.5f), new Color(0.75f, 0.9f, 1f, 0.12f + 0.2f * depth), 1f, true);
                    // 반짝임
                    if ((c.X * 7 + c.Y * 13) % 5 == 0)
                    {
                        float tw = 0.5f + 0.5f * Mathf.Sin(_time * 2.3f + c.X + c.Y * 0.7f);
                        ci.DrawCircle(r.Position + new Vector2(T * 0.7f, T * 0.3f), 1.1f, new Color(1, 1, 1, 0.25f * tw * (0.5f + depth)));
                    }
                }
                // 전기가 살아 있는 물: 푸른 불꽃이 가끔 튄다
                if (room.Powered && depth > 0.12f && Mathf.PosMod(_time * 1.7f + room.Id, 1f) < 0.15f)
                {
                    var c0 = room.Cells[(int)(Mathf.PosMod(_time * 3f + room.Id * 5, room.Cells.Count))];
                    var p = CellRect(c0).GetCenter();
                    for (int k = 0; k < 4; k++)
                    {
                        float ang = k * 1.7f + _time * 11f;
                        ci.DrawLine(p, p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 7f, new Color(0.7f, 0.9f, 1f, 0.8f), 1.2f, true);
                    }
                }
            }
            // 결로: 습한 방 벽 가까이 물방울이 맺히고 흘러내린다
            if (room.Humidity > 0.72f)
            {
                float k = (room.Humidity - 0.72f) / 0.28f;
                int n = 0;
                foreach (var c in room.Cells)
                {
                    if (((c.X * 31 + c.Y * 17) & 3) != 0 || n++ > 24) continue;
                    var r = CellRect(c);
                    float drip = Mathf.PosMod(_time * 0.25f + c.X * 0.13f, 1f);
                    ci.DrawCircle(r.Position + new Vector2(T * 0.2f, T * (0.15f + 0.6f * drip)), 1.2f, new Color(0.7f, 0.85f, 1f, 0.35f * k));
                }
            }
            // 분전함을 내린 방: 문지방 쪽에 노란 줄무늬 띠
            if (room.BreakerOff)
            {
                var b = RoomBox(room);
                for (float x = b.Position.X; x < b.End.X - 6; x += 12f)
                    ci.DrawLine(new Vector2(x, b.Position.Y + 3), new Vector2(x + 6, b.Position.Y + 3), new Color(1f, 0.82f, 0.2f, 0.8f), 2f);
            }
        }
    }

    private static Rect2 RoomBox(Room r)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in r.Cells) { minX = Mathf.Min(minX, c.X); minY = Mathf.Min(minY, c.Y); maxX = Mathf.Max(maxX, c.X); maxY = Mathf.Max(maxY, c.Y); }
        return new Rect2(minX * T, minY * T, (maxX - minX + 1) * T, (maxY - minY + 1) * T);
    }
}
