using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v13.0 대응 수순 연출: 진공 소화(배기 밸브로 빠져나가는 공기) · 질식 소화(밀려드는 불활성 가스 안개) ·
// 대피 카운트다운(붉은 테두리와 남은 시간) · 공기 구역(지킬 구역의 초록 테두리).
public partial class ShipView
{
    private void PaintResponse(CanvasItem ci)
    {
        var w = _world;
        var ship = w.Ship;
        foreach (var room in ship.LiveRooms)
        {
            if (room.Cells.Count == 0) continue;
            // 진공 소화: 외벽 밸브 쪽으로 공기가 줄지어 빠져나간다
            if (room.Purging && room.Air.Pressure > 2f)
            {
                var valve = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == room)
                    .Select(kv => kv.Key).OrderBy(c => (c.X - room.Center.X) * (c.X - room.Center.X) + (c.Y - room.Center.Y) * (c.Y - room.Center.Y)).FirstOrDefault();
                var hole = CellRect(valve).GetCenter();
                float strength = Mathf.Clamp(room.Air.Pressure / 100f, 0.15f, 1f);
                int n = Mathf.Min(room.Cells.Count, 26);
                for (int k = 0; k < n; k++)
                {
                    var from = room.Cells[(int)(Hash(room.Id, k, 300) * room.Cells.Count) % room.Cells.Count];
                    var start = CellRect(from).GetCenter();
                    float ph = Mathf.PosMod(_time * (0.9f + strength) + Hash(k, room.Id, 310), 1f);
                    var p = start.Lerp(hole, ph * ph);
                    var dir = (hole - start).Normalized();
                    ci.DrawLine(p - dir * (4f + 14f * ph), p, new Color(0.85f, 0.93f, 1f, 0.8f * strength * Mathf.Sin(ph * Mathf.Pi)), 2f, true);
                }
                // 밸브 바깥으로 뿜어지는 흰 기둥
                var outward = (hole - ToPx(room.Center)).Normalized();
                for (int k = 0; k < 6; k++)
                {
                    float ph = Mathf.PosMod(_time * 2.2f + k / 6f, 1f);
                    ci.DrawCircle(hole + outward * (T * (0.4f + 2.2f * ph)), T * (0.15f + 0.35f * ph), new Color(0.9f, 0.95f, 1f, 0.6f * strength * (1f - ph)), true, -1f, true);
                }
            }
            // 질식 소화: 푸른 흰 안개가 방을 채운다 (산소가 낮을수록 짙다)
            if (room.Inerting || room.Flushing || room.ResponseHold && room.Air.O2 < 14f && !room.Purging)
            {
                float thick = Mathf.Clamp((18f - room.Air.O2) / 14f, 0.15f, 1f);
                foreach (var c in room.Cells)
                {
                    float h = Hash(c.X, c.Y, 320);
                    float wob = 0.6f + 0.4f * Mathf.Sin(_time * 0.8f + h * 6.28f);
                    ci.DrawCircle(CellRect(c).GetCenter() + new Vector2(Mathf.Sin(_time * 0.5f + h * 9f) * 4f, Mathf.Cos(_time * 0.4f + h * 7f) * 3f),
                        T * (0.55f + 0.25f * h), new Color(0.7f, 0.85f, 1f, 0.09f * thick * wob), true, -1f, true);
                }
            }
            // 대피 카운트다운: 붉은 테두리가 깜빡이고, 남은 시간
            if (room.EvacuateBy >= 0)
            {
                float blink = 0.5f + 0.5f * Mathf.Sin(_time * 8f);
                PaintOutline(ci, room, new Color(1f, 0.25f, 0.2f, 0.45f + 0.45f * blink), false);
                var fc = w.Automation.FireCases.FirstOrDefault(f => f.RoomId == room.Id);
                float left = (room.EvacuateBy - w.Tick) / (float)SimTime.Minutes(1);
                string what = fc?.Method == "vacuum" ? "진공 소화" : "질식 소화";
                string text = left > 0f ? $"{what} {Mathf.FloorToInt(left)}:{Mathf.FloorToInt((left % 1f) * 60f):00} — 나가라" : $"{what} — 방이 비면";
                var at = ToPx(room.Center) + new Vector2(0, -T * 1.2f);
                float wdt = Gfx.Width(Fonts.Bold, text, 13) + 16;
                Gfx.RoundRect(ci, new Rect2(at - new Vector2(wdt / 2, 13), wdt, 22), new Color(0.35f, 0.04f, 0.04f, 0.85f), 6, new Color(1f, 0.35f, 0.3f, 0.9f), 1);
                Gfx.Text(ci, Fonts.Bold, at + new Vector2(-wdt / 2 + 8, 3), text, 13, new Color(1f, 0.92f, 0.9f));
            }
        }
        // 공기 구역: 지킬 구역의 방마다 초록 테두리
        if (w.Automation.ZoneActive)
        {
            float pulse = 0.35f + 0.2f * Mathf.Sin(_time * 2f);
            foreach (var room in ship.LiveRooms.Where(r => w.Automation.Zone.Contains(r.Id)))
                PaintOutline(ci, room, new Color(0.4f, 1f, 0.6f, pulse), false);
        }
    }
}
