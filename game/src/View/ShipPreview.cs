using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.22 배 고르기 — 고른 배의 윤곽을 모든 배가 같은 축척으로 (작은 배는 작게, 큰 배는 크게) 어두운 유리판 위에 그린다.
/// 방은 방 색으로, 원자로 · 배전 · 주컴퓨터실은 테두리를 밝게. 아래에 등급 · 인원 · 방 수 · 방 종류 수 · 구획과 한 줄 소개.
/// </summary>
public partial class ShipPreview : Control
{
    private string _key = "auto";
    private int _crew = 6;
    private static readonly Dictionary<string, Ship?> Cache = new();
    /// <summary>모든 배가 같은 축척 — 가장 큰 기본 배(천마호)가 판 너비에 맞는다.</summary>
    private const float CellsAcross = 150f;

    public void Show(string key, int crew)
    {
        _key = key;
        _crew = crew;
        QueueRedraw();
    }

    private static Ship? Build(ShipTemplate t)
    {
        if (Cache.TryGetValue(t.Key, out var s)) return s;
        try { s = ShipBuilder.FromAscii(t.Name, t.Ascii); } catch { s = null; }
        Cache[t.Key] = s;
        return s;
    }

    public override void _Draw()
    {
        var size = Size;
        var r = new Rect2(Vector2.Zero, size);
        UiKit.Panel(this, r, Tone.Normal);
        var t = _key == "gen" ? null : ShipCatalog.Find(_key) ?? ShipCatalog.ForCrew(_crew);
        float pad = 12f;
        if (t == null)
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(pad, 24), "새 항해마다 다른 배", Ui.TextTitle, Palette.Text);
            Wrap("인원에 맞춰 그때그때 짓는다 — 용도와 뼈대, 설계사와 시작 상태가 매번 다르다.", pad, 44, size.X - 2 * pad, Palette.TextMuted);
            return;
        }
        var cls = ShipClasses.Of(t);
        Gfx.Text(this, Fonts.Bold, new Vector2(pad, 24), $"{t.Name}", Ui.TextTitle, Palette.Text);
        float nx = pad + Gfx.Width(Fonts.Bold, t.Name, Ui.TextTitle) + 8;
        Gfx.Text(this, Fonts.Body, new Vector2(nx, 24), $"{ShipClasses.Name(cls)} · {t.Crew}인", Ui.TextSmall, Palette.TextMuted);
        var ship = Build(t);
        float top = 34f, bottom = size.Y - 46f;
        if (ship != null)
        {
            int x0 = int.MaxValue, x1 = 0, y0 = int.MaxValue, y1 = 0;
            for (int i = 0; i < ship.Grid.CellCount; i++)
            {
                var c = ship.Grid.CellAt(i);
                if (ship.Grid.Kind(c) == TileKind.Void) continue;
                x0 = Mathf.Min(x0, c.X); x1 = Mathf.Max(x1, c.X); y0 = Mathf.Min(y0, c.Y); y1 = Mathf.Max(y1, c.Y);
            }
            float k = Mathf.Min((size.X - 2 * pad) / CellsAcross, (bottom - top) / 75f); // 같은 축척
            float w = (x1 - x0 + 1) * k, h = (y1 - y0 + 1) * k;
            var o = new Vector2(pad + (size.X - 2 * pad - w) * 0.5f, top + (bottom - top - h) * 0.5f);
            // 우주 쪽 은은한 빛
            this.Box(new Rect2(o - new Vector2(4, 4), new Vector2(w + 8, h + 8)), new Color(0.35f, 0.55f, 0.8f, 0.04f));
            for (int i = 0; i < ship.Grid.CellCount; i++)
            {
                var c = ship.Grid.CellAt(i);
                var kind = ship.Grid.Kind(c);
                if (kind == TileKind.Void) continue;
                var cell = new Rect2(o + new Vector2((c.X - x0) * k, (c.Y - y0) * k), new Vector2(k + 0.3f, k + 0.3f));
                if (kind == TileKind.Wall) { this.Box(cell, new Color("#2b3442")); continue; }
                if (kind == TileKind.Door) { this.Box(cell, new Color("#56657a")); continue; }
                var room = ship.RoomAt(c);
                var col = room == null || room.Type == RoomType.Corridor ? new Color("#3a4250") : Palette.Room(room.Kind).Darkened(0.35f);
                if (ship.Grid.FurnitureId(c) >= 0) col = col.Lightened(0.25f);
                this.Box(cell, col);
            }
            foreach (var room in ship.Rooms.Where(x => x.Type is RoomType.Reactor or RoomType.Power || x.Kind == RoomType.ComputerRoom))
            {
                var rr = new Rect2(o + new Vector2((room.MinX - x0) * k, (room.MinY - y0) * k), new Vector2((room.MaxX - room.MinX + 1) * k, (room.MaxY - room.MinY + 1) * k));
                var edge = room.Kind == RoomType.ComputerRoom ? new Color("#6fd6e8") : room.Type == RoomType.Reactor ? new Color("#ff9a4a") : new Color("#eed65a");
                this.Box(rr.Grow(0.5f), edge.WithAlpha(0.9f), false, 1.2f);
            }
            var (rooms, kinds) = ShipClasses.Count(ship);
            Gfx.TextRight(this, Fonts.Body, new Vector2(size.X - pad, 24), $"방 {rooms} · 종류 {kinds} · 구획 {Mathf.Max(1, ship.Compartments)}", Ui.TextSmall, Palette.TextMuted);
        }
        Wrap(ShipClasses.Blurb(cls), pad, size.Y - 28, size.X - 2 * pad, Palette.TextMuted);
    }

    private void Wrap(string text, float x, float y, float width, Color col)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (Gfx.Width(Fonts.Body, next, Ui.TextSmall) > width && line.Length > 0)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(x, y), line, Ui.TextSmall, col);
                y += 15;
                line = word;
            }
            else line = next;
        }
        if (line.Length > 0) Gfx.Text(this, Fonts.Body, new Vector2(x, y), line, Ui.TextSmall, col);
    }
}
