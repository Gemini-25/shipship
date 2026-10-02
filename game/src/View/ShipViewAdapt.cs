using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v5 적응의 흔적: 임시 배선, 뜯긴 설비, 간이침대.
/// 우주선이 원래 설계와 달라진 곳은 사고가 끝난 뒤에도 계속 보인다.
/// </summary>
public partial class ShipView
{
    public static readonly Color JumperColor = new("#ff9a3c");
    private int _furnitureCount = -1;

    /// <summary>간이침대 같은 가구가 새로 생기면 정적 레이어를 다시 그린다.</summary>
    private void CheckFurnitureChanged()
    {
        int n = _world.Ship.Furniture.Count * 1000 + _world.Ship.Furniture.Count(f => f.Stowed); // v10.10: 치운 가구도
        n = unchecked(n * 31 + FixtureArt.Signature(_world.Ship)) & 0x7FFFFFFF; // v16.5c 단계 · 등급 · 주인이 바뀌면 몸체를 다시 그린다
        if (n == _furnitureCount) return;
        if (_furnitureCount >= 0) RedrawStatic();
        _furnitureCount = n;
    }

    // ── 간이침대: 접이식 틀 위에 천, 주인 색 담요를 말아 둔 것 ──
    private static void PaintCotBody(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f).Grow(-4f);
        var frame = new Color("#3b3a2f");
        // 다리 (X자)
        ci.DrawLine(r.Position + new Vector2(2, 2), new Vector2(r.End.X - 2, r.End.Y - 2), frame.Darkened(0.3f), 1.5f, true);
        ci.DrawLine(new Vector2(r.End.X - 2, r.Position.Y + 2), new Vector2(r.Position.X + 2, r.End.Y - 2), frame.Darkened(0.3f), 1.5f, true);
        var canvas = new Rect2(r.Position.X + 2, r.Position.Y + 1, r.Size.X - 4, r.Size.Y - 2);
        Gfx.RoundRect(ci, canvas, new Color("#5d5a44"), 3, frame);
        ci.DrawLine(new Vector2(canvas.Position.X + 3, canvas.GetCenter().Y), new Vector2(canvas.End.X - 3, canvas.GetCenter().Y), new Color("#6b6850"), 1f);
        var blanket = f.Owner != null ? Palette.Crew(f.Owner.Id).Darkened(0.45f) : new Color("#3a3f4a");
        Gfx.RoundRect(ci, new Rect2(canvas.Position.X + 2, canvas.End.Y - 8, canvas.Size.X - 4, 6), blanket, 3);
    }

    // ── 뜯긴 설비: 덮개가 열려 있고 속이 비었다. 경고처럼 깜빡이지 않는 조용한 흔적 ──
    private void PaintStripped(CanvasItem ci, Furniture f, Machine m)
    {
        var r = FurnitureRect(f).Grow(-5f);
        var hole = new Rect2(r.Position.X + r.Size.X * 0.2f, r.Position.Y + r.Size.Y * 0.2f, r.Size.X * 0.6f, r.Size.Y * 0.55f);
        ci.DrawRect(hole, new Color(0.02f, 0.02f, 0.03f, 0.85f));
        ci.DrawRect(hole, new Color("#5a606b").WithAlpha(0.8f), false, 1f);
        // 끊어진 전선 몇 가닥
        Color[] wires = { new("#c85a3c"), new("#d8b64a"), new("#5a8fc8") };
        for (int k = 0; k < 3; k++)
        {
            float x = hole.Position.X + hole.Size.X * (0.25f + 0.25f * k);
            var a = new Vector2(x, hole.Position.Y + 1);
            var b = a + new Vector2(k % 2 == 0 ? 3f : -3f, hole.Size.Y * (0.45f + 0.15f * k));
            ci.DrawLine(a, b, wires[k].WithAlpha(0.8f), 1.3f, true);
            ci.DrawCircle(b, 1.3f, wires[k].Lightened(0.3f), true, -1f, true);
        }
        // 떼어 낸 덮개가 옆에 기대 있다
        var cover = new Rect2(r.End.X - r.Size.X * 0.35f, r.End.Y - 6f, r.Size.X * 0.4f, 5f);
        Gfx.RoundRect(ci, cover, new Color("#4a505b"), 2, new Color("#6b7280"));
        // 점선 테두리
        var o = FurnitureRect(f).Grow(-1f);
        DashedRect(ci, o, new Color("#9aa3b5").WithAlpha(0.45f), 5f, 4f);
        // 모서리 표시: 빈 상자 (부품을 꺼냈다)
        var p = new Vector2(FurnitureRect(f).End.X - 5, FurnitureRect(f).Position.Y + 5);
        ci.DrawCircle(p, 6f, new Color("#171a20"), true, -1f, true);
        ci.DrawRect(new Rect2(p.X - 3.5f, p.Y - 2f, 7f, 5f), new Color("#9aa3b5"), false, 1.2f);
        ci.DrawLine(new Vector2(p.X - 3.5f, p.Y - 2f), new Vector2(p.X - 1f, p.Y - 4.5f), new Color("#9aa3b5"), 1.2f);
    }

    private static void DashedRect(CanvasItem ci, Rect2 r, Color c, float dash, float gap)
    {
        void Side(Vector2 a, Vector2 b)
        {
            float len = a.DistanceTo(b);
            var d = (b - a) / len;
            for (float s = 0; s < len; s += dash + gap)
                ci.DrawLine(a + d * s, a + d * Mathf.Min(len, s + dash), c, 1f);
        }
        Side(r.Position, new Vector2(r.End.X, r.Position.Y));
        Side(new Vector2(r.End.X, r.Position.Y), r.End);
        Side(r.End, new Vector2(r.Position.X, r.End.Y));
        Side(new Vector2(r.Position.X, r.End.Y), r.Position);
    }

    /// <summary>배전반 앞면의 회로 스위치 위치.</summary>
    public static Vector2 PanelSwitch(Furniture panel, int circuit)
    {
        var r = FurnitureRect(panel);
        float x = r.Position.X + 10 + circuit * (r.Size.X - 20) / (PowerGrid.CircuitCount - 1);
        return new Vector2(x, r.Position.Y + 14);
    }

    /// <summary>임시 배선 한 가닥이 바닥에 늘어진 가장 낮은 곳 (라벨 자리).</summary>
    public static Vector2 JumperSag(Jumper j, Furniture panel, int index)
    {
        var at = ToPx(j.At.Center);
        var a = PanelSwitch(panel, j.From);
        var b = PanelSwitch(panel, j.To);
        var mid = (a + b) * 0.5f;
        var dir = (at - mid).Normalized();
        return at + dir * (4f + index * 5f) + new Vector2(index * 6f, 0f);
    }

    // ── 임시 배선: 배전반의 두 스위치를 잇는 굵은 주황 케이블이 바닥으로 늘어져 있다 ──
    private void PaintJumpers(CanvasItem ci)
    {
        var panel = _world.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault();
        if (panel == null) return;
        var jumpers = _world.Power.Jumpers;
        int feeders = 0;
        for (int i = 0; i < jumpers.Count; i++)
        {
            var j = jumpers[i];
            if (j.Permanent) { PaintFeeder(ci, panel, j, feeders++); continue; }
            var a = PanelSwitch(panel, j.From) + new Vector2(0, 4);
            var b = PanelSwitch(panel, j.To) + new Vector2(0, 4);
            var sag = JumperSag(j, panel, i);
            var pts = new Vector2[17];
            for (int k = 0; k <= 16; k++)
            {
                float t = k / 16f;
                // 두 번 꺾인 베지어: 스위치 → 바닥 → 다른 스위치
                var p0 = a.Lerp(sag, t);
                var p1 = sag.Lerp(b, t);
                pts[k] = p0.Lerp(p1, t);
            }

            Color col;
            float width = 3f;
            if (j.Burnt) { col = new Color("#2a2220"); width = 2.5f; }
            else if (!j.Active) col = JumperColor.Darkened(0.45f).WithAlpha(0.7f); // 흔적: 회로를 고친 뒤에도 남은 배선
            else col = JumperColor;

            if (j.Active && j.Load > j.Capacity)
            {
                // 정격을 넘겨 달아오른다
                float heat = Mathf.Clamp((j.Load / j.Capacity - 1f) * 4f, 0.2f, 1f);
                float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 5f + i);
                ci.DrawPolyline(pts, Palette.Danger.WithAlpha(0.25f * heat * pulse + 0.1f), 9f, true);
                col = col.Lerp(new Color("#ff4a2c"), heat * pulse);
            }
            ci.DrawPolyline(pts, new Color(0, 0, 0, 0.45f), width + 2f, true);
            ci.DrawPolyline(pts, col, width, true);
            // 절연 테이프 감은 자리
            foreach (int k in new[] { 5, 11 })
                ci.DrawCircle(pts[k], 2.6f, j.Burnt ? new Color("#15110f") : new Color("#e6e1d3").WithAlpha(0.85f), true, -1f, true);
            // 스위치 쪽 집게
            ci.DrawCircle(a, 3f, col.Darkened(0.2f), true, -1f, true);
            ci.DrawCircle(b, 3f, col.Darkened(0.2f), true, -1f, true);
            if (j.Burnt)
            {
                // 탄 자국
                ci.DrawCircle(sag, 7f, new Color(0.05f, 0.04f, 0.03f, 0.55f), true, -1f, true);
                ci.DrawCircle(sag + new Vector2(4, -2), 4f, new Color(0.08f, 0.06f, 0.05f, 0.5f), true, -1f, true);
            }
        }
    }
}
