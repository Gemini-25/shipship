using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.3 감지기 보기: 설비마다 마지막 측정이 몇 분 전인지, 감지기 교정(사람이 짐작하는 값 ↔ 실제), 데이터선이 끊긴 방.
public partial class ShipView
{
    private void PaintSensorOverlay(CanvasItem ci)
    {
        var w = _world;
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.Detached) continue;
            if (!room.DataLinked) FillRoom(ci, room, new Color("#b58cff").WithAlpha(0.12f));
        }
        foreach (var f in w.Ship.Furniture.Where(f => !f.Stowed && !f.Room.Detached && f.Machine != null))
        {
            var m = f.Machine!;
            var r = FurnitureRect(f);
            float age = (w.Tick - m.LastReading) / (float)SimTime.TicksPerHour;
            bool stale = age > 0.25f;
            // 측정 나이: 초록(방금) → 노랑 → 빨강(한 시간 넘게 멈춤)
            var col = !stale ? Palette.Good : age < 1f ? Palette.Warning : Palette.Danger;
            var p = new Vector2(r.End.X - 7, r.Position.Y + 7);
            ci.DrawCircle(p, 5.5f, new Color("#0b0e13"));
            float pulse = stale ? 1f : 0.6f + 0.4f * Mathf.Sin(_time * 4f + f.Id);
            ci.DrawArc(p, 4f, 0, Mathf.Tau, 14, col.WithAlpha(pulse), 1.6f, true);
            if (!stale) ci.DrawCircle(p, 1.6f, col);
            else Gfx.TextCentered(ci, Fonts.Bold, p + new Vector2(0, 14), age < 1f ? $"{age * 60:0}분" : $"{age:0.#}h", 9, col);
            // 교정: 막대 (틀어질수록 짧고 붉다) — 실제 값, 옆의 옅은 선은 사람이 짐작하는 값
            float cal = m.SensorCal, known = WatchLog.KnownCal(m, w);
            var bar = new Rect2(r.Position.X + 3, r.End.Y - 5, (r.Size.X - 6), 3);
            ci.DrawRect(bar, new Color(0, 0, 0, 0.5f));
            ci.DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * cal, bar.Size.Y)), (cal > 0.8f ? Palette.Good : cal > 0.6f ? Palette.Warning : Palette.Danger).WithAlpha(0.85f));
            float kx = bar.Position.X + bar.Size.X * known;
            ci.DrawLine(new Vector2(kx, bar.Position.Y - 2), new Vector2(kx, bar.End.Y + 1), Colors.White.WithAlpha(0.6f), 1f);
            // 계기 오류(헛경보)가 확인된 설비
            if (m.Omen is { Cause: OmenCause.Phantom }) Gfx.TextCentered(ci, Fonts.Bold, r.GetCenter(), "헛경보?", 9, Palette.Warning);
        }
    }
}
