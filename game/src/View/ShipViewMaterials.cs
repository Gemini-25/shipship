using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v6 재료와 순환: 채집 장치(선체 밖으로 뻗은 채집 팔과 호퍼), 정제기, Mk.1 임시품, 파손된 설비.
/// </summary>
public partial class ShipView
{
    private static readonly Color Hazard = new("#e0b64a");

    // ── 채집 장치: 호퍼(원료 칸) + 선체를 뚫고 나간 팔의 뿌리 ──
    private static void PaintCollectorBody(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f).Grow(-3f);
        Gfx.RoundRect(ci, r, new Color("#1c2027"), 5, new Color("#4a5260"), 2);
        // 원료 칸 다섯
        for (int k = 0; k < 5; k++)
        {
            var bin = new Rect2(r.Position.X + 5 + k * (r.Size.X - 10) / 5f, r.Position.Y + 9, (r.Size.X - 10) / 5f - 2f, r.Size.Y - 14);
            Gfx.RoundRect(ci, bin, new Color("#12151a"), 2);
        }
        // 선체 쪽 관통부
        ci.DrawRect(new Rect2(r.GetCenter().X - 6, r.Position.Y - 6, 12, 7), new Color("#3a414d"));
    }

    private void PaintCollectorLife(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f).Grow(-3f);
        var m = f.Machine!;
        bool working = m.Efficiency > 0f;
        // 호퍼 칸마다 채워진 만큼
        var kinds = ItemKinds.RawKinds;
        for (int k = 0; k < kinds.Length; k++)
        {
            var bin = new Rect2(r.Position.X + 5 + k * (r.Size.X - 10) / 5f, r.Position.Y + 9, (r.Size.X - 10) / 5f - 2f, r.Size.Y - 14);
            int cap = kinds[k] == ItemKind.Rare ? 5 : CollectionSystem.BinSize;
            float fill = Mathf.Clamp(f.Storage!.Count(kinds[k]) / (float)cap, 0f, 1f);
            float h = bin.Size.Y * fill;
            ci.DrawRect(new Rect2(bin.Position.X + 1, bin.End.Y - h, bin.Size.X - 2, h), Palette.Item(kinds[k]).WithAlpha(0.75f));
        }

        // 선체 밖으로 뻗은 채집 팔 (벽 너머 우주 쪽)
        var root = new Vector2(r.GetCenter().X, f.MinY * T - T * 0.1f);
        float sweep = working ? Mathf.Sin(_time * 0.6f + f.Id) * 0.7f : 0.2f;
        var elbow = root + new Vector2(0, -T * 1.2f) + Vector2.FromAngle(-Mathf.Pi / 2 + sweep) * 4f;
        var hand = elbow + Vector2.FromAngle(-Mathf.Pi / 2 + sweep * 1.6f) * T * 1.1f;
        var armCol = working ? new Color("#8a93a3") : new Color("#4a505b");
        ci.DrawLine(root, elbow, new Color(0, 0, 0, 0.5f), 8f, true);
        ci.DrawLine(root, elbow, armCol, 5f, true);
        ci.DrawLine(elbow, hand, new Color(0, 0, 0, 0.5f), 6f, true);
        ci.DrawLine(elbow, hand, armCol, 4f, true);
        if (working) ci.DrawCircle(hand, 12f, new Color("#8ee6ff").WithAlpha(0.06f + 0.04f * Mathf.Sin(_time * 2f)), true, -1f, true);
        ci.DrawCircle(elbow, 3.5f, armCol.Darkened(0.2f), true, -1f, true);
        // 국자 모양 채집구
        var dir = (hand - elbow).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        ci.DrawColoredPolygon(new[] { hand + side * 7f + dir * 4f, hand - side * 7f + dir * 4f, hand - side * 4f - dir * 3f, hand + side * 4f - dir * 3f },
            working ? new Color("#6b7385") : new Color("#3a3f48"));
        if (!working) return;
        // 빨려 드는 먼지와 얼음 알갱이 (밀도가 높을수록 많이)
        int motes = 3 + (int)(_world.Space.Density * 4f);
        for (int k = 0; k < motes; k++)
        {
            float phase = Mathf.PosMod(_time * 0.35f + k / (float)motes, 1f);
            float ang = Hash(f.Id, k, 3) * Mathf.Tau;
            var from = hand + Vector2.FromAngle(ang) * T * (1.4f + Hash(k, f.Id, 4));
            var p = from.Lerp(hand, phase);
            var col = Palette.Item(SpaceEnvironment.Composition[k % 4].kind);
            ci.DrawCircle(p, 1.4f, col.WithAlpha(0.8f * Mathf.Sin(phase * Mathf.Pi)), true, -1f, true);
        }
    }

    // ── 정제기: 도가니와 배출구. 누가 돌리는 중이면 달아오른다 ──
    private static void PaintRefineryBody(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f).Grow(-4f);
        Gfx.RoundRect(ci, r, new Color("#211b18"), 8, new Color("#5a4636"), 2);
        var c = r.GetCenter() + new Vector2(0, -4);
        ci.DrawCircle(c, 16f, new Color("#15110f"), true, -1f, true);
        ci.DrawArc(c, 16f, 0f, Mathf.Tau, 32, new Color("#5a4636"), 2f, true);
        // 배출 트레이
        Gfx.RoundRect(ci, new Rect2(r.Position.X + 8, r.End.Y - 12, r.Size.X - 16, 6), new Color("#2a2420"), 2);
    }

    private void PaintRefineryLife(CanvasItem ci, Furniture f)
    {
        var m = f.Machine!;
        var r = FurnitureRect(f).Grow(-4f);
        var c = r.GetCenter() + new Vector2(0, -4);
        bool hot = m.Active && m.Efficiency > 0f;
        float pulse = 0.6f + 0.4f * Mathf.Sin(_time * 3f + f.Id);
        var glow = hot ? new Color("#ff8a3c") : new Color("#4a2a1a");
        ci.DrawCircle(c, 11f, glow.WithAlpha(hot ? 0.35f * pulse + 0.2f : 0.5f), true, -1f, true);
        ci.DrawCircle(c, 6f, (hot ? new Color("#ffd08a") : new Color("#3a2418")).WithAlpha(hot ? pulse : 0.8f), true, -1f, true);
        if (hot)
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(_time * 0.8f + k / 3f, 1f);
                ci.DrawCircle(c + new Vector2((k - 1) * 6f, -ph * 18f), 2f + 2f * ph, new Color(0.6f, 0.55f, 0.5f, 0.35f * (1f - ph)), true, -1f, true);
            }
    }

    // ── Mk.1 임시품: 현장에서 짜 맞춘 티가 나게 (경고 줄무늬 테이프, 덧댄 판, 리벳, "Mk.1" 딱지) ──
    private void PaintMk1(CanvasItem ci, Furniture f, Machine m)
    {
        var r = FurnitureRect(f).Grow(-2f);
        // 덧댄 판
        var patch = new Rect2(r.Position.X + r.Size.X * 0.12f, r.Position.Y + r.Size.Y * 0.55f, r.Size.X * 0.42f, r.Size.Y * 0.32f);
        ci.DrawRect(patch, new Color("#5d6470").WithAlpha(0.55f));
        ci.DrawRect(patch, new Color("#9aa3b5").WithAlpha(0.5f), false, 1f);
        // 리벳
        foreach (var p in new[] { patch.Position + new Vector2(3, 3), new Vector2(patch.End.X - 3, patch.Position.Y + 3), patch.End - new Vector2(3, 3), new Vector2(patch.Position.X + 3, patch.End.Y - 3) })
            ci.DrawCircle(p, 1.2f, new Color("#c8ced8"), true, -1f, true);
        // 아래 모서리 경고 테이프
        float y0 = r.End.Y - 5f;
        ci.DrawRect(new Rect2(r.Position.X, y0, r.Size.X, 4f), new Color("#1a1710"));
        for (float x = r.Position.X; x < r.End.X - 2f; x += 7f)
            ci.DrawColoredPolygon(new[] { new Vector2(x, y0 + 4f), new Vector2(x + 3.5f, y0), new Vector2(x + 6f, y0), new Vector2(x + 2.5f, y0 + 4f) }, Hazard.WithAlpha(0.8f));
        // 딱지
        var tag = new Rect2(r.Position.X + 2, r.Position.Y + 2, 22, 11);
        Gfx.RoundRect(ci, tag, Hazard.WithAlpha(0.9f), 3);
        Gfx.TextCentered(ci, Fonts.Bold, tag.GetCenter(), "Mk.1", 8, new Color("#1a1710"));
    }

    // ── 파손: 찌그러지고 그을린 몸체, 금, 가끔 튀는 불꽃 ──
    private void PaintWrecked(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f).Grow(-3f);
        ci.DrawRect(r, new Color(0.03f, 0.02f, 0.02f, 0.5f));
        var c = r.GetCenter();
        for (int k = 0; k < 3; k++)
        {
            var pts = new Vector2[5];
            float a = Hash(f.Id, k, 7) * Mathf.Tau;
            var d = Vector2.FromAngle(a);
            var n = new Vector2(-d.Y, d.X);
            for (int i = 0; i < 5; i++)
                pts[i] = c + d * (i - 2) * r.Size.Length() * 0.12f + n * (Hash(f.Id, k * 5 + i, 8) - 0.5f) * 8f;
            ci.DrawPolyline(pts, new Color(0.9f, 0.9f, 0.95f, 0.35f), 1.2f, true);
        }
        float spark = Mathf.PosMod(_time * 0.7f + Hash(f.Id, 0, 9), 1f);
        if (spark < 0.12f)
        {
            var p = c + new Vector2(Hash(f.Id, 1, 9) - 0.5f, Hash(f.Id, 2, 9) - 0.5f) * r.Size * 0.6f;
            for (int k = 0; k < 4; k++)
                ci.DrawLine(p, p + Vector2.FromAngle(k * 1.7f + _time * 3f) * 5f * (1f - spark / 0.12f), new Color("#ffd27a"), 1.2f, true);
        }
    }
}
