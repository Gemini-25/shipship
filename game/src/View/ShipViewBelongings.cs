using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v14.3 바닥·탁자에 놓인 물건 (펼친 책 · 판 · 걸어 둔 작품 · 두고 온 공구) · 손에 든 취미 물건 · 연주의 음표 · 진행 중인 판
public partial class ShipView
{
    private void PaintBelongings(CanvasItem ci)
    {
        var w = _world;
        var bs = w.Belongings;
        foreach (var b in bs.All)
        {
            if (b.Holder >= 0 || b.At is not Cell at) continue;
            var r = CellRect(at);
            var col = Palette.Crew(b.Maker >= 0 ? b.Maker : b.Owner);
            float a = b.Usable ? 1f : 0.45f;
            var c0 = r.GetCenter() + new Vector2((b.Id % 3 - 1) * 5f, (b.Id / 3 % 3 - 1) * 4f);
            Glyph(ci, b.Kind, c0, col, a, b.Open);
            if (!b.Usable) ci.DrawLine(c0 + new Vector2(-5, -5), c0 + new Vector2(5, 5), new Color(1f, 0.4f, 0.3f, 0.7f), 1.2f, true);
        }
        // 손에 든 취미 물건 (하고 있는 동안)
        foreach (var (id, s) in bs.Sessions)
        {
            var c = w.Crew.FirstOrDefault(x => x.Id == id);
            if (c == null || c.Dead) continue;
            var p = CrewPx(c);
            if (bs.Get(s.Item) is Belonging held && held.Holder == c.Id)
                Glyph(ci, held.Kind, p + new Vector2(CrewRadius * 0.9f, CrewRadius * 0.4f), Palette.Crew(c.Id), 1f, true, 0.8f);
            // 연주·노래: 음표가 떠오른다 (조용히 해 달라고 했으면 멈춘다)
            if (s.Hobby is Hobby.Instrument or Hobby.Singing && !s.Hushed)
            {
                float t = (_time * 0.8f + id * 0.37f) % 1f;
                var q = p + new Vector2(6f + 6f * Mathf.Sin(t * 6f), -CrewRadius - 4f - 14f * t);
                Gfx.Text(ci, Fonts.Bold, q, "♪", 11, new Color(1f, 0.9f, 0.6f, 1f - t));
            }
        }
        // 진행 중인 판: 두 사람 사이에 선
        foreach (var g in bs.Games)
        {
            if (g.Done || g.B < 0) continue;
            var a = w.Crew.FirstOrDefault(x => x.Id == g.A);
            var b2 = w.Crew.FirstOrDefault(x => x.Id == g.B);
            if (a == null || b2 == null || a.Job?.Activity is not HobbyActivity || b2.Job?.Activity is not HobbyActivity) continue;
            ci.DrawDashedLine(CrewPx(a), CrewPx(b2), new Color(0.9f, 0.85f, 0.6f, 0.35f), 1f, 3f);
        }
    }

    /// <summary>물건의 작은 그림 (종류마다).</summary>
    private static void Glyph(CanvasItem ci, BelongingKind k, Vector2 c, Color owner, float alpha, bool open, float scale = 1f)
    {
        float s = 5f * scale;
        var ink = new Color(0.08f, 0.09f, 0.12f, 0.9f * alpha);
        var paper = new Color(0.95f, 0.92f, 0.82f, alpha);
        var tint = owner.WithAlpha(alpha);
        switch (k)
        {
            case BelongingKind.Book or BelongingKind.Journal or BelongingKind.Sketchbook:
                if (open)
                {
                    ci.DrawRect(new Rect2(c.X - s, c.Y - s * 0.6f, s, s * 1.2f), paper);
                    ci.DrawRect(new Rect2(c.X, c.Y - s * 0.6f, s, s * 1.2f), paper.Darkened(0.08f));
                    ci.DrawLine(new Vector2(c.X, c.Y - s * 0.6f), new Vector2(c.X, c.Y + s * 0.6f), tint, 1f);
                }
                else { ci.DrawRect(new Rect2(c.X - s * 0.6f, c.Y - s * 0.8f, s * 1.2f, s * 1.6f), tint); ci.DrawRect(new Rect2(c.X - s * 0.6f, c.Y - s * 0.8f, s * 0.3f, s * 1.6f), ink); }
                break;
            case BelongingKind.Instrument:
                ci.DrawCircle(c + new Vector2(-s * 0.3f, s * 0.3f), s * 0.6f, new Color(0.7f, 0.45f, 0.25f, alpha));
                ci.DrawLine(c + new Vector2(-s * 0.2f, s * 0.1f), c + new Vector2(s * 0.9f, -s * 0.9f), ink, 1.4f);
                break;
            case BelongingKind.ChessSet or BelongingKind.Puzzle:
                for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    ci.DrawRect(new Rect2(c.X - s + i * s, c.Y - s + j * s, s, s), (i + j) % 2 == 0 ? paper : ink);
                break;
            case BelongingKind.Cards:
                ci.DrawRect(new Rect2(c.X - s * 0.8f, c.Y - s * 0.6f, s, s * 1.3f), paper);
                ci.DrawRect(new Rect2(c.X - s * 0.2f, c.Y - s * 0.4f, s, s * 1.3f), new Color(0.85f, 0.2f, 0.25f, alpha));
                break;
            case BelongingKind.Artwork:
                ci.DrawRect(new Rect2(c.X - s * 1.1f, c.Y - s * 0.9f, s * 2.2f, s * 1.8f), new Color(0.75f, 0.6f, 0.3f, alpha));
                ci.DrawRect(new Rect2(c.X - s * 0.8f, c.Y - s * 0.6f, s * 1.6f, s * 1.2f), tint.Lightened(0.3f));
                break;
            case BelongingKind.Toolset or BelongingKind.ModelKit:
                ci.DrawRect(new Rect2(c.X - s, c.Y - s * 0.5f, s * 2f, s * 1.1f), k == BelongingKind.Toolset ? new Color(0.95f, 0.55f, 0.15f, alpha) : tint);
                ci.DrawLine(new Vector2(c.X - s * 0.4f, c.Y - s * 0.5f), new Vector2(c.X - s * 0.4f, c.Y - s), ink, 1.2f);
                ci.DrawLine(new Vector2(c.X + s * 0.4f, c.Y - s * 0.5f), new Vector2(c.X + s * 0.4f, c.Y - s), ink, 1.2f);
                ci.DrawLine(new Vector2(c.X - s * 0.4f, c.Y - s), new Vector2(c.X + s * 0.4f, c.Y - s), ink, 1.2f);
                break;
            case BelongingKind.Mug or BelongingKind.TeaSet:
                ci.DrawRect(new Rect2(c.X - s * 0.5f, c.Y - s * 0.6f, s, s * 1.2f), tint);
                ci.DrawArc(c + new Vector2(s * 0.6f, 0f), s * 0.35f, -Mathf.Pi / 2, Mathf.Pi / 2, 8, tint, 1.2f);
                break;
            default:
                ci.DrawCircle(c, s * 0.6f, tint);
                ci.DrawArc(c, s * 0.6f, 0f, Mathf.Tau, 12, ink, 1f);
                break;
        }
    }

    /// <summary>v14.4 말풍선: 짧게 한 말 (목적 있는 대화 · 인수인계 · 깨우기) — 몇 분 동안.</summary>
    private void PaintTalk(CanvasItem ci)
    {
        var w = _world;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Said == null || c.SaidUntil <= w.Tick || c.CarriedBy != null) continue;
            string t = c.Said.Length > 34 ? c.Said[..32] + "…" : c.Said;
            var p = CrewPx(c) + new Vector2(0f, -CrewRadius - 16f);
            float bw = Gfx.Width(Fonts.Body, t, 10) + 12;
            float fade = Mathf.Clamp((c.SaidUntil - w.Tick) / (float)SimTime.Minutes(1), 0f, 1f);
            var rect = new Rect2(p.X - bw / 2, p.Y - 10, bw, 16);
            Gfx.RoundRect(ci, rect, new Color(0.96f, 0.95f, 0.9f, 0.92f * fade), 6, Palette.Crew(c.Id).WithAlpha(0.9f * fade), 1);
            ci.DrawColoredPolygon(new[] { new Vector2(p.X - 4, p.Y + 6), new Vector2(p.X + 4, p.Y + 6), new Vector2(p.X, p.Y + 11) }, new Color(0.96f, 0.95f, 0.9f, 0.92f * fade));
            Gfx.Text(ci, Fonts.Body, new Vector2(p.X - bw / 2 + 6, p.Y + 2), t, 10, new Color(0.1f, 0.1f, 0.14f, fade));
        }
    }
}
