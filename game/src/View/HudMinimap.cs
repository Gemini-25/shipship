using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v11.3 미니맵 (G): 배 전체를 작게 — 방마다 상태 색(평상 · 급함 · 치명 · 버림), 승무원 점, 로봇, 날아오는 운석, 지금 보는 영역.
/// 누르면 그곳으로 화면이 옮겨 간다. 큰 배(20·30인용)에서 어디서 무슨 일이 나는지 한눈에.
/// </summary>
public partial class Hud
{
    public bool MinimapOpen { get; set; } = true;

    public void ToggleMinimap() => MinimapOpen = !MinimapOpen;

    private Rect2 _minimapRect;

    private void DrawMinimap()
    {
        if (!MinimapOpen || ChronicleOpen || TechOpen) return;
        var bounds = _main.ShipView.Bounds;
        if (bounds.Size.X < 1f) return;
        float maxW = 280f, maxH = LogHeight - 30f;
        float scale = Mathf.Min(maxW / bounds.Size.X, maxH / bounds.Size.Y);
        var size = bounds.Size * scale;
        var card = new Rect2(Margin + 470f + 10f, Screen.Y - Margin - LogHeight, size.X + 24f, size.Y + 36f);
        Card(card);
        _minimapRight = card.End.X;
        _minimapRect = card;
        Gfx.Text(this, Fonts.Bold, new Vector2(card.Position.X + 12, card.Position.Y + 18), "지도", 11, Palette.TextDim);
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 12, card.Position.Y + 18), "G", 10, Palette.TextMuted);
        var origin = card.Position + new Vector2(12f, 26f);
        Vector2 M(Vector2 worldPx) => origin + (worldPx - bounds.Position) * scale;
        float t = ShipView.T;

        foreach (var room in _world.Ship.Rooms)
        {
            if (room.Detached || room.Cells.Count == 0) continue;
            var sev = Severity.Of(_world, room);
            var baseCol = Palette.Room(room.Kind);
            var col = sev switch
            {
                RoomSeverity.Critical => Palette.Danger.WithAlpha(0.55f + 0.35f * Mathf.Sin(_time * 6f)),
                RoomSeverity.Urgent => Palette.Warning.WithAlpha(0.6f),
                RoomSeverity.Abandoned => new Color("#3a3f48"),
                _ => baseCol.WithAlpha(room.Type == RoomType.Corridor ? 0.18f : 0.32f),
            };
            foreach (var c in room.Cells)
            {
                var p = M(new Vector2(c.X * t, c.Y * t));
                DrawRect(new Rect2(p, new Vector2(Mathf.Max(1f, t * scale), Mathf.Max(1f, t * scale))), col);
            }
        }
        // 날아오는 운석: 들어올 외벽
        foreach (var m in _world.Sensors.Incoming)
        {
            var p = M(ShipView.ToPx(m.Entry.Center));
            DrawArc(p, 3f + 1.5f * Mathf.Sin(_time * 8f), 0f, Mathf.Tau, 12, Palette.Danger, 1.2f, true);
        }
        // 로봇 · 승무원
        foreach (var r in _world.Robots.Robots)
        {
            if (r.State == RobotState.Lost) continue;
            var p = M(ShipView.ToPx(r.Position));
            DrawRect(new Rect2(p - new Vector2(1.2f, 1.2f), new Vector2(2.4f, 2.4f)), ShipView.RobotColor(r.Kind).WithAlpha(0.9f));
        }
        foreach (var c in _world.Crew)
        {
            if (c.Dead) continue;
            var p = M(ShipView.ToPx(c.Position));
            DrawCircle(p, c == _main.SelectedCrew ? 3f : 1.8f, c.Down ? Palette.Danger : Palette.Crew(c.Id), true, -1f, true);
        }
        // 지금 보는 영역
        var cam = _main.Camera;
        var view = GetViewportRect().Size / cam.Zoom;
        var vr = new Rect2(M(cam.Position - view * 0.5f), view * scale);
        var clip = new Rect2(origin, size);
        vr = vr.Intersection(clip);
        if (vr.Size.X > 0 && vr.Size.Y > 0) DrawRect(vr, new Color(1, 1, 1, 0.6f), false, 1f);
        var area = new Rect2(origin, size);
        _buttons.Add((area, () =>
        {
            var local = GetLocalMousePosition();
            _main.Camera.Position = bounds.Position + (local - origin) / scale;
        }));
    }
}
