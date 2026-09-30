using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.2 인과 사슬을 배 도면 위에: 고른 사고의 고리마다 점, 원인 → 결과로 흐르는 화살표.
public partial class ShipView
{
    private void PaintCauseChain(CanvasItem ci)
    {
        if (_main.Hud.ChainIncident is not CauseIncident inc) return;
        var log = _world.Causes;
        // 1) 화살표: 부모 → 자식 (같은 자리면 건너뛴다), 점선이 흐른다
        foreach (int id in inc.Nodes)
        {
            var n = log.Node(id);
            if (n.Parent < 0 || n.Kind == CauseKind.Recovery || n.At is not System.Numerics.Vector2 b) continue;
            var p = log.Node(n.Parent);
            if (p.At is not System.Numerics.Vector2 a) continue;
            var from = ToPx(a);
            var to = ToPx(b);
            var d = to - from;
            float len = d.Length();
            if (len < T * 0.6f) continue;
            var dir = d / len;
            var col = Hud.KindColor(n.Kind);
            float alpha = n.Open ? 0.85f : 0.35f;
            // 굽은 화살 (곧은 선보다 겹침이 덜하다)
            var normal = new Vector2(-dir.Y, dir.X);
            var mid = (from + to) * 0.5f + normal * Mathf.Min(40f, len * 0.18f);
            const int seg = 18;
            Vector2 Q(float t) => (1 - t) * (1 - t) * from + 2 * (1 - t) * t * mid + t * t * to;
            float phase = Mathf.PosMod(_time * 0.8f + id * 0.07f, 1f);
            for (int i = 0; i < seg; i++)
            {
                float t0 = i / (float)seg, t1 = (i + 1) / (float)seg;
                // 흐르는 점선: 켜진 칸만 그린다
                bool on = Mathf.PosMod(t0 * 6f - phase, 1f) < 0.62f;
                if (!on && !n.Open) continue;
                ci.DrawLine(Q(t0), Q(t1), col.WithAlpha(on ? alpha : alpha * 0.25f), n.Open ? 2.4f : 1.6f, true);
            }
            // 화살촉
            var tip = Q(0.97f);
            var back = (Q(0.97f) - Q(0.9f)).Normalized();
            var side = new Vector2(-back.Y, back.X);
            ci.DrawColoredPolygon(new[] { tip + back * 2f, tip - back * 7f + side * 4f, tip - back * 7f - side * 4f }, col.WithAlpha(alpha));
        }
        // 2) 고리 점: 열린 것은 맥박, 풀린 것은 고리, 복구는 초록 테
        foreach (int id in inc.Nodes)
        {
            var n = log.Node(id);
            if (n.At is not System.Numerics.Vector2 at || n.Kind == CauseKind.Recovery) continue;
            var c = ToPx(at);
            var col = Hud.KindColor(n.Kind);
            float r = n.Depth == 0 ? 9f : 6f;
            if (n.Open)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 5f + id);
                ci.DrawCircle(c, r + 6f + 4f * pulse, col.WithAlpha(0.12f + 0.1f * pulse));
                ci.DrawCircle(c, r, col.WithAlpha(0.95f));
                ci.DrawArc(c, r + 1.5f, 0, Mathf.Tau, 20, new Color(0, 0, 0, 0.6f), 1.2f, true);
            }
            else
            {
                ci.DrawCircle(c, r, new Color(0.04f, 0.06f, 0.08f, 0.85f));
                ci.DrawArc(c, r, 0, Mathf.Tau, 20, col.WithAlpha(0.7f), 1.6f, true);
                if (n.Lasting) // 되돌렸다: 작은 체크
                {
                    ci.DrawLine(c + new Vector2(-3, 0), c + new Vector2(-1, 2.5f), Palette.Good, 1.6f, true);
                    ci.DrawLine(c + new Vector2(-1, 2.5f), c + new Vector2(3.5f, -2.5f), Palette.Good, 1.6f, true);
                }
            }
            if (n.Depth == 0)
            {
                // 뿌리: 이름표
                string label = n.Text;
                float w = Gfx.Width(Fonts.Bold, label, 12) + 14f;
                var box = new Rect2(c + new Vector2(-w * 0.5f, -r - 26f), new Vector2(w, 18f));
                Gfx.RoundRect(ci, box, new Color(0.05f, 0.06f, 0.09f, 0.9f), 6, col.WithAlpha(0.7f));
                Gfx.TextCentered(ci, Fonts.Bold, box.GetCenter(), label, 12, col);
            }
        }
    }
}
