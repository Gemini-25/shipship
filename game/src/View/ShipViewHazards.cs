using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>v11.2 사고 종류의 그림: 유독 가스 안개, 병충해 반점, 균이 든 식사 표시, 사고 더 보기의 대상 미리보기.</summary>
public partial class ShipView
{
    private static readonly Color Toxin = new("#b5e34d");

    /// <summary>유독 가스: 노란 연둣빛 안개가 바닥에 깔리고, 샌 곳에서 김이 솟는다.</summary>
    private void PaintToxin(CanvasItem ci)
    {
        foreach (var room in _world.Ship.LiveRooms)
        {
            float tox = Mathf.Min(1f, room.Air.Toxin);
            if (tox < 0.02f) continue;
            FillRoom(ci, room, new Color(0.55f, 0.7f, 0.18f, 0.22f * tox));
            foreach (var cell in room.Cells)
            {
                if (((cell.X * 5 + cell.Y * 3) & 3) != 1) continue;
                float ph = _time * 0.35f + Hash(cell.X, cell.Y, 7) * 6f;
                var p = CellRect(cell).GetCenter() + new Vector2(Mathf.Sin(ph) * 9f, Mathf.Cos(ph * 0.8f) * 5f);
                ci.Circle(p, T * (0.45f + 0.18f * Mathf.Sin(ph * 1.7f)), Toxin.WithAlpha(0.12f * tox), true, -1f, true);
            }
            if (_world.Hazards.GasSource(room) is Machine m)
            {
                var r = FurnitureRect(m.Body);
                var top = new Vector2(r.GetCenter().X, r.Position.Y + 4);
                for (int k = 0; k < 6; k++)
                {
                    float ph = Mathf.PosMod(_time * 0.9f + k / 6f, 1f);
                    var p = top + new Vector2(Mathf.Sin(_time * 3f + k) * 5f * ph, -T * 1.4f * ph);
                    ci.Circle(p, 2f + 6f * ph, Toxin.WithAlpha(0.45f * (1f - ph)), true, -1f, true);
                }
                // 위험 표지 (노란 삼각형)
                var c = new Vector2(r.End.X - 9, r.Position.Y + 9);
                float blink = 0.55f + 0.45f * Mathf.Sin(_time * 5f);
                ci.Poly(new[] { c + new Vector2(0, -6), c + new Vector2(6, 5), c + new Vector2(-6, 5) }, new Color("#f5d547").WithAlpha(0.85f * blink));
                ci.DrawLine(c + new Vector2(0, -2), c + new Vector2(0, 2), new Color("#1a1a10"), 1.5f);
            }
        }
    }

    /// <summary>병충해: 잎이 누렇게 변하고 검은 반점과 벌레가 보인다 (재배대 그림 위에).</summary>
    private void PaintBlight(CanvasItem ci, Furniture f, CropState crop)
    {
        if (crop.Blight <= 0f) return;
        var r = FurnitureRect(f);
        float b = crop.Blight;
        ci.Box(new Rect2(r.Position.X + 5, r.Position.Y + 7, r.Size.X - 10, r.Size.Y - 14), new Color("#8a7a2a").WithAlpha(0.25f * b));
        int spots = 3 + (int)(b * 10f);
        for (int k = 0; k < spots; k++)
        {
            float hx = Hash(f.Id, k, 11), hy = Hash(f.Id, k, 12);
            var p = new Vector2(r.Position.X + 8 + hx * (r.Size.X - 16), r.Position.Y + 8 + hy * (r.Size.Y - 16));
            ci.Circle(p, 1.2f + 1.3f * Hash(f.Id, k, 13), new Color("#2e2410").WithAlpha(0.75f), true, -1f, true);
        }
        if (b > 0.3f)
            for (int k = 0; k < 4; k++)
            {
                float ph = _time * (0.6f + 0.2f * k) + k * 1.7f;
                var p = r.GetCenter() + new Vector2(Mathf.Sin(ph) * r.Size.X * 0.35f, Mathf.Cos(ph * 1.3f) * r.Size.Y * 0.25f);
                ci.Circle(p, 1.1f, new Color("#1b1b1b"), true, -1f, true);
            }
        if (crop.BlightKnown)
            Gfx.RoundRect(ci, r.Grow(1f), new Color(0, 0, 0, 0), 6, Palette.Warning.WithAlpha(0.45f + 0.3f * Mathf.Sin(_time * 3f)), 2);
    }

    /// <summary>균이 든 식사: 관찰자에게만 보이는 작은 초록 점 (배가 알아채면 테두리가 깜박인다).</summary>
    private void PaintTaint(CanvasItem ci, Furniture f)
    {
        if (f.Storage is not Inventory inv || inv.Tainted <= 0) return;
        var r = FurnitureRect(f);
        var c = new Vector2(r.Position.X + 8, r.End.Y - 8);
        ci.Circle(c, 3.5f, Toxin.WithAlpha(0.8f), true, -1f, true);
        ci.Circle(c, 1.5f, new Color("#1c2a08"), true, -1f, true);
        if (inv.TaintKnown)
            Gfx.RoundRect(ci, r.Grow(1f), new Color(0, 0, 0, 0), 6, Toxin.WithAlpha(0.4f + 0.35f * Mathf.Sin(_time * 4f)), 2);
    }

    /// <summary>사고 더 보기: 고른 사고가 걸릴 대상을 미리 보여 준다.</summary>
    private void PaintHazardPreview(CanvasItem ci, Vector2 mouse, float pulse)
    {
        var k = _main.ToolHazard;
        var (at, id, ok) = _main.HazardAim(mouse);
        var col = Palette.Danger;
        if (!ok)
        {
            ci.Arc(mouse, 10f, 0f, Mathf.Tau, 24, Palette.TextMuted.WithAlpha(0.6f), 1.5f, true);
            return;
        }
        switch (Hazards.Spec(k).Target)
        {
            case HazardTarget.Room:
                if (Hazards.RoomAt(_world, at) is Room room)
                    FillRoom(ci, room, (k == HazardKind.GasLeak ? Toxin : col).WithAlpha(0.12f + 0.08f * pulse));
                break;
            case HazardTarget.Machine:
                if (_world.Ship.FurnitureAt(at) is Furniture f)
                    Gfx.RoundRect(ci, FurnitureRect(f).Grow(2f), col.WithAlpha(0.1f), 7, col.WithAlpha(0.9f * pulse), 2);
                break;
            case HazardTarget.Hull:
            {
                var p = CellRect(at).GetCenter();
                ci.Arc(p, T * 1.6f, 0f, Mathf.Tau, 32, Palette.Warning.WithAlpha(0.8f * pulse), 1.5f, true);
                // 균열이 번질 방향 (이어진 외벽)
                foreach (var d in Cell.Dirs4)
                    if (_world.Ship.WallAt(at + d) is WallState ws && ws.IsHull)
                        ci.DrawDashedLine(p, CellRect(at + d).GetCenter(), Palette.Warning.WithAlpha(0.6f), 1.5f, 3f);
                break;
            }
            case HazardTarget.Door:
                Gfx.RoundRect(ci, CellRect(at).Grow(3f), col.WithAlpha(0.1f), 5, col.WithAlpha(0.9f * pulse), 2);
                break;
            case HazardTarget.Crew:
                if (_world.Crew.FirstOrDefault(c => c.Id == id) is CrewMember cm)
                    ci.Arc(ToPx(cm.Position), T * 0.7f, 0f, Mathf.Tau, 28, col.WithAlpha(0.9f * pulse), 2f, true);
                break;
            case HazardTarget.Robot:
                if (_world.Robots.Robots.FirstOrDefault(r => r.Id == id) is Robot rb)
                    ci.Arc(ToPx(rb.Position), T * 0.65f, 0f, Mathf.Tau, 28, col.WithAlpha(0.9f * pulse), 2f, true);
                break;
        }
    }
}
