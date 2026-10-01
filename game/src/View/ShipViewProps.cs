using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v15.8 방 안의 소품·장식: 벽에 건 액자·포스터·시계·깃발·지도·선반 · 바닥의 화분·러그·조명·모빌·트로피·어항 …
// 만든 사람의 색이 테두리에 조금 남는다. 고른 방은 소품 이름도 보인다. 겹쳐 보기에서는 흐리게.
public partial class ShipView
{
    private void PaintRoomProps(CanvasItem ci)
    {
        var ship = _world.Ship;
        var sel = _main.SelectedRoom;
        float a = _main.ViewMode == ViewMode.Normal ? 1f : 0.45f;
        foreach (var room in ship.Rooms)
        {
            if (room.Detached || room.Decor.Count == 0) continue;
            foreach (var p in room.Decor)
            {
                var cell = CellRect(p.At).GetCenter();
                Vector2 inward = Vector2.Zero, at;
                if (p.Wall >= 0)
                {
                    var d = Cell.Dirs4[p.Wall];
                    inward = new Vector2(-d.X, -d.Y);
                    at = cell - inward * (T * 0.5f - 6f); // 벽 바로 안쪽
                }
                else at = cell + new Vector2((p.Id % 2 == 0 ? -1f : 1f) * 7f, (p.Id / 2 % 2 == 0 ? -1f : 1f) * 6f); // 칸 모서리 쪽 (사람이 서는 가운데를 비운다)
                PropGlyph(ci, p, at, inward, a);
                if (room == sel) Gfx.TextCentered(ci, Fonts.Body, at + new Vector2(0f, 11f), p.Name, 8, new Color(0.95f, 0.93f, 0.85f, 0.85f * a));
            }
        }
    }

    private static float PropHue(int id) => Mathf.PosMod(id * 0.61803f, 1f);

    /// <summary>소품의 작은 그림 (모양마다).</summary>
    private void PropGlyph(CanvasItem ci, PlacedProp p, Vector2 c, Vector2 inward, float a)
    {
        var ink = new Color(0.07f, 0.08f, 0.1f, 0.9f * a);
        var wood = new Color(0.55f, 0.38f, 0.22f, a);
        var leaf = new Color(0.36f, 0.72f, 0.38f, a);
        var tint = Color.FromHsv(PropHue(p.Id + p.Spec.Id.Length), 0.45f, 0.85f, a);
        var maker = p.Maker >= 0 ? Palette.Crew(p.Maker).WithAlpha(0.8f * a) : ink;
        bool vert = Mathf.Abs(inward.X) > 0.5f; // 왼쪽·오른쪽 벽
        Rect2 Plate(float along, float depth)
        {
            var size = vert ? new Vector2(depth, along) : new Vector2(along, depth);
            return new Rect2(c - size * 0.5f, size);
        }
        switch (p.Spec.Shape)
        {
            case PropShape.Frame:
            {
                var r = Plate(10f, 8f);
                bool memorial = p.Spec.Id is "memorial" or "qualframe";
                ci.DrawRect(r.Grow(1.2f), memorial ? ink : wood);
                ci.DrawRect(r, memorial ? new Color(0.85f, 0.85f, 0.82f, a) : tint);
                if (p.Spec.Id == "memorial") ci.DrawLine(r.Position, r.Position + new Vector2(4f, 0f) + new Vector2(0f, 4f), ink, 1.5f, true); // 검은 리본
                ci.DrawRect(r.Grow(1.2f), maker, false, 0.8f);
                break;
            }
            case PropShape.Poster or PropShape.Board:
            {
                var r = Plate(11f, 7f);
                ci.DrawRect(r, p.Spec.Shape == PropShape.Board ? new Color(0.62f, 0.5f, 0.36f, a) : tint);
                for (int k = 0; k < 3; k++)
                {
                    var o = r.Position + r.Size * new Vector2(0.25f + 0.25f * k, 0.5f);
                    ci.DrawRect(new Rect2(o - new Vector2(1.5f, 1.5f), new Vector2(3f, 3f)), new Color(0.95f, 0.93f, 0.85f, 0.85f * a));
                }
                ci.DrawRect(r, maker, false, 0.8f);
                break;
            }
            case PropShape.Map:
            {
                var r = Plate(12f, 8f);
                ci.DrawRect(r, new Color(0.2f, 0.26f, 0.38f, a));
                ci.DrawLine(r.Position + new Vector2(2, r.Size.Y - 2), r.End - new Vector2(2, r.Size.Y - 2), new Color(1f, 0.85f, 0.4f, a), 1f, true);
                ci.DrawCircle(r.End - new Vector2(2, r.Size.Y - 2), 1.2f, new Color(1f, 0.85f, 0.4f, a));
                break;
            }
            case PropShape.Clock:
                ci.DrawCircle(c, 4.5f, new Color(0.92f, 0.9f, 0.84f, a));
                ci.DrawArc(c, 4.5f, 0f, Mathf.Tau, 16, ink, 1f, true);
                ci.DrawLine(c, c + Vector2.Up.Rotated(_time * 0.05f) * 3.5f, ink, 1f, true);
                ci.DrawLine(c, c + Vector2.Up.Rotated(_time * 0.6f) * 4f, new Color(0.8f, 0.2f, 0.2f, a), 0.8f, true);
                break;
            case PropShape.Flag:
            {
                var pole = c + (vert ? new Vector2(0, -5f) : new Vector2(-5f, 0));
                float wave = Mathf.Sin(_time * 2f + p.Id) * 1.2f;
                ci.DrawColoredPolygon(new[] { pole, pole + new Vector2(9f, 2f + wave), pole + new Vector2(0f, 6f) }, tint);
                ci.DrawLine(pole + new Vector2(0, -1f), pole + new Vector2(0, 8f), ink, 1f, true);
                break;
            }
            case PropShape.Shelf:
            {
                var r = Plate(12f, 5f);
                ci.DrawRect(r, wood);
                for (int k = 0; k < 3; k++)
                    ci.DrawRect(new Rect2(r.Position + r.Size * new Vector2(0.15f + 0.28f * k, 0.15f), new Vector2(2.5f, 2.5f)), Color.FromHsv(PropHue(p.Id + k), 0.5f, 0.9f, a));
                break;
            }
            case PropShape.Plaque:
            {
                var r = Plate(10f, 6f);
                bool scorch = p.Spec.Id == "scorch";
                ci.DrawRect(r, scorch ? new Color(0.22f, 0.2f, 0.18f, a) : new Color(0.75f, 0.65f, 0.35f, a));
                if (scorch) ci.DrawCircle(r.GetCenter(), 2.2f, new Color(0.05f, 0.04f, 0.03f, 0.8f * a)); // 그을음
                ci.DrawRect(r, ink, false, 0.8f);
                break;
            }
            case PropShape.Lights:
            {
                var r = Plate(16f, 2f);
                var from = vert ? new Vector2(r.GetCenter().X, r.Position.Y) : new Vector2(r.Position.X, r.GetCenter().Y);
                var step = vert ? new Vector2(0f, 4f) : new Vector2(4f, 0f);
                ci.DrawLine(from, from + step * 4f, ink, 0.8f, true);
                for (int k = 0; k <= 4; k++)
                {
                    float tw = 0.6f + 0.4f * Mathf.Sin(_time * 3f + k * 1.7f + p.Id);
                    ci.DrawCircle(from + step * k, 1.4f, Color.FromHsv(PropHue(k), 0.5f, 1f, tw * a));
                }
                break;
            }
            case PropShape.Pot:
                ci.DrawColoredPolygon(new[] { c + new Vector2(-3.5f, 1f), c + new Vector2(3.5f, 1f), c + new Vector2(2.5f, 5f), c + new Vector2(-2.5f, 5f) }, new Color(0.7f, 0.4f, 0.28f, a));
                ci.DrawCircle(c + new Vector2(-2f, -1.5f), 2.6f, leaf);
                ci.DrawCircle(c + new Vector2(2f, -2f), 2.4f, leaf);
                ci.DrawCircle(c + new Vector2(0f, -4f), 2.4f, leaf.Lightened(0.15f));
                break;
            case PropShape.Rug:
            {
                var r = new Rect2(c - new Vector2(9f, 6f), new Vector2(18f, 12f));
                Gfx.RoundRect(ci, r, tint.WithAlpha(0.55f * a), 3f, maker.WithAlpha(0.6f * a), 1);
                ci.DrawRect(r.Grow(-3.5f), Color.FromHsv(PropHue(p.Id + 3), 0.35f, 0.7f, 0.45f * a), false, 1f);
                break;
            }
            case PropShape.Cushion:
                Gfx.RoundRect(ci, new Rect2(c - new Vector2(4.5f, 3f), new Vector2(9f, 6f)), tint, 2.5f, maker, 1);
                break;
            case PropShape.Lamp:
            {
                float glow = 0.18f + 0.06f * Mathf.Sin(_time * 1.3f + p.Id);
                ci.DrawCircle(c, 8f, new Color(1f, 0.85f, 0.5f, glow * a));
                ci.DrawLine(c + new Vector2(0, 4f), c + new Vector2(0, -1f), ink, 1f, true);
                ci.DrawColoredPolygon(new[] { c + new Vector2(-3f, -1f), c + new Vector2(3f, -1f), c + new Vector2(2f, -4f), c + new Vector2(-2f, -4f) }, new Color(1f, 0.88f, 0.6f, a));
                break;
            }
            case PropShape.Candle:
            {
                float fl = 0.75f + 0.25f * Mathf.Sin(_time * 9f + p.Id * 2.1f);
                ci.DrawCircle(c, 6f, new Color(1f, 0.75f, 0.35f, 0.18f * fl * a));
                ci.DrawRect(new Rect2(c + new Vector2(-1.5f, -1f), new Vector2(3f, 5f)), new Color(0.95f, 0.93f, 0.88f, a));
                ci.DrawCircle(c + new Vector2(0, -2.2f), 1.3f * fl, new Color(1f, 0.8f, 0.35f, a));
                break;
            }
            case PropShape.Mobile:
            {
                float sw = Mathf.Sin(_time * 0.9f + p.Id) * 0.35f;
                var bar = Vector2.Right.Rotated(sw) * 5f;
                ci.DrawLine(c - bar, c + bar, ink, 0.8f, true);
                for (int k = -1; k <= 1; k++)
                {
                    var hang = c + bar * k;
                    ci.DrawLine(hang, hang + new Vector2(0, 3f + (k + 1)), ink.WithAlpha(0.5f * a), 0.6f, true);
                    ci.DrawCircle(hang + new Vector2(0, 3.5f + (k + 1)), 1.4f, Color.FromHsv(PropHue(p.Id + k + 2), 0.5f, 0.95f, a));
                }
                break;
            }
            case PropShape.Trophy:
                ci.DrawColoredPolygon(new[] { c + new Vector2(-3.5f, -4f), c + new Vector2(3.5f, -4f), c + new Vector2(1.2f, 0.5f), c + new Vector2(-1.2f, 0.5f) }, new Color(0.95f, 0.78f, 0.3f, a));
                ci.DrawRect(new Rect2(c + new Vector2(-2.5f, 2f), new Vector2(5f, 2f)), new Color(0.6f, 0.45f, 0.2f, a));
                ci.DrawLine(c + new Vector2(0, 0.5f), c + new Vector2(0, 2f), new Color(0.95f, 0.78f, 0.3f, a), 1.2f);
                break;
            case PropShape.Tank:
            {
                var r = new Rect2(c - new Vector2(5f, 3.5f), new Vector2(10f, 7f));
                ci.DrawRect(r, new Color(0.3f, 0.6f, 0.85f, 0.55f * a));
                float fx = Mathf.Sin(_time * 0.8f + p.Id) * 3f;
                ci.DrawCircle(r.GetCenter() + new Vector2(fx, 0.5f), 1.3f, new Color(1f, 0.55f, 0.25f, a));
                ci.DrawRect(r, new Color(0.8f, 0.9f, 1f, 0.7f * a), false, 0.8f);
                break;
            }
            case PropShape.Model:
                ci.DrawColoredPolygon(new[] { c + new Vector2(-5f, 1.5f), c + new Vector2(3f, 1.5f), c + new Vector2(5f, 0f), c + new Vector2(3f, -1.5f), c + new Vector2(-5f, -1.5f) },
                    p.Spec.Id == "zenstone" ? new Color(0.55f, 0.55f, 0.58f, a) : new Color(0.75f, 0.78f, 0.82f, a));
                ci.DrawLine(c + new Vector2(-3f, 2.5f), c + new Vector2(3f, 2.5f), wood, 1.2f);
                break;
            default: // Box
                ci.DrawRect(new Rect2(c - new Vector2(4f, 3f), new Vector2(8f, 6f)), tint);
                ci.DrawCircle(c + new Vector2(1.5f, 0f), 1.5f, ink);
                ci.DrawRect(new Rect2(c - new Vector2(4f, 3f), new Vector2(8f, 6f)), maker, false, 0.8f);
                break;
        }
    }
}
