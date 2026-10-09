using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.6 외부 설비: 선체 밖의 안테나와 태양 날개. 상하면 깨진 칸이 어둡고, 고치는 중이면 드론이 곁을 돈다.
public partial class ShipView
{
    private void PaintExterior(CanvasItem ci)
    {
        foreach (var f in _world.Exterior.All)
        {
            var anchor = CellRect(f.Anchor);
            var dir = new Vector2(f.Out.X, f.Out.Y);
            var baseP = anchor.GetCenter() + dir * T * 0.5f;
            float cond = f.Condition;
            var metal = new Color("#8d96a8").Lerp(new Color("#5a4a40"), 1f - cond);
            if (f.Kind == ExtKind.Antenna)
            {
                // 기둥 (상하면 휘었다) + 접시 + 깜빡이는 표시등
                float bend = (1f - cond) * 0.6f;
                var tip = baseP + dir.Rotated(bend) * T * 1.8f;
                ci.DrawLine(baseP, tip, metal, 3f, true);
                var face = -dir.Rotated(bend);
                float ang = face.Angle();
                ci.Arc(tip + dir.Rotated(bend) * 5f, 9f, ang - 1.1f, ang + 1.1f, 12, metal.Lightened(0.2f), 2.5f, true);
                ci.Circle(tip, 2.5f, metal);
                bool blink = Mathf.PosMod(_time, 1.6f) < 0.2f;
                if (cond > 0.3f && blink) ci.Circle(tip + dir.Rotated(bend) * 3f, 2.2f, new Color("#ff6b6b"));
            }
            else
            {
                // 붐 + 패널 (세로 2칸 × 가로 6칸 격자). 상한 만큼 칸이 깨져 어둡다
                var boom = baseP + dir * T * 0.6f;
                ci.DrawLine(baseP, boom, metal, 3f, true);
                int cols = 6, rows = 2;
                float cw = T * 0.9f, ch = T * 0.7f;
                var origin = boom + new Vector2(-cols * cw / 2f, dir.Y < 0 ? -rows * ch : 0f);
                int broken = Mathf.RoundToInt((1f - cond) * cols * rows);
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < cols; x++)
                    {
                        int idx = (x * 7 + y * 3 + f.Id) % (cols * rows);
                        bool dead = idx < broken;
                        var r = new Rect2(origin + new Vector2(x * cw, y * ch), new Vector2(cw - 1.5f, ch - 1.5f));
                        ci.Box(r, dead ? new Color("#1c1f26") : new Color("#24467a"));
                        if (!dead) ci.DrawLine(r.Position + new Vector2(1, 1), r.Position + new Vector2(r.Size.X * 0.6f, 1), new Color("#6fa8ff").WithAlpha(0.6f), 1f);
                        else ci.DrawLine(r.Position, r.End, new Color("#4a4f5c"), 1f);
                    }
                ci.Box(new Rect2(origin - new Vector2(1, 1), new Vector2(cols * cw + 0.5f, rows * ch + 0.5f)), metal, false, 1.5f);
            }
            // 수리 중: 드론(또는 사람)이 곁을 돈다
            if (f.RepairDone >= 0)
            {
                var c = baseP + dir * T * 1.4f + new Vector2(Mathf.Cos(_time * 2f), Mathf.Sin(_time * 2f)) * T * 1.2f;
                ci.Circle(c, 4f, new Color("#e6edf3"));
                ci.Circle(c, 2f, new Color("#f5d547"));
                if (Mathf.PosMod(_time * 3f, 1f) < 0.3f) ci.Circle(c + new Vector2(3, 3), 1.5f, new Color("#ffd166"));
            }
            else if (cond < 0.7f)
                Gfx.TextCentered(ci, Fonts.Bold, baseP + dir * T * 2.6f, $"{cond * 100:0}%", 12, cond < 0.4f ? Palette.Danger : Palette.Warning);
        }
    }
}
