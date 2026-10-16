using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v14.5 몸짓: 움찔(느낌표 · 소리 난 쪽으로 고개) · 문 앞 계기 · 조용한 걸음(발끝) · 비켜서기(길 내주는 쪽 화살표)
public partial class ShipView
{
    private void PaintGait(CanvasItem ci, CrewMember c, Vector2 body, Vector2 facing, float rr, float s)
    {
        var w = _world;
        var g = c.Gait;
        // v14.7 손·옷의 얼룩: 손 쪽에 작은 얼룩, 옷이 더러우면 몸 둘레에 탁한 테
        var soil = c.Soil;
        if (soil.HandsMax > 0.3f)
        {
            int k = 0; for (int i = 1; i < Soil.Kinds; i++) if (soil.Hands[i] > soil.Hands[k]) k = i;
            var tint = (SoilKind)k switch { SoilKind.Oil => new Color(0.12f, 0.1f, 0.08f), SoilKind.Dust => new Color(0.6f, 0.55f, 0.45f), SoilKind.Soot => new Color(0.05f, 0.05f, 0.05f), _ => new Color(0.45f, 0.7f, 0.3f) };
            var side = new Vector2(-facing.Y, facing.X);
            ci.Circle(body + side * (rr * 0.8f) + facing * (rr * 0.2f), 2.2f * s, tint.WithAlpha(0.85f), true, -1f, true);
        }
        if (soil.ClothesMax > 0.35f) ci.Arc(body, rr + 1f, 0f, Mathf.Tau, 20, new Color(0.2f, 0.18f, 0.15f, 0.25f + 0.4f * soil.ClothesMax), 1.6f, true);
        // 움찔: 머리 위 느낌표가 튀어 올랐다 사라진다
        long since = w.Tick - g.StartleTick;
        if (since >= 0 && since < Gait.Linger)
        {
            float k = 1f - since / (float)Gait.Linger;
            float jump = since < 6 ? (6 - since) * 0.8f : 0f;
            Gfx.Text(ci, Fonts.Bold, body + new Vector2(-3f, -rr - 6f - jump), "!", 14, Palette.Warning.WithAlpha(k));
        }
        // 문 앞 계기: 작은 바늘 계기판
        long chk = w.Tick - g.CheckedAt;
        if (chk >= 0 && chk < Gait.Linger && g.DoorReading != null)
        {
            float k = 1f - chk / (float)Gait.Linger;
            var gp = body + facing * (rr + 6f * s) + new Vector2(0f, -4f * s);
            ci.Circle(gp, 4.5f * s, new Color(0.1f, 0.12f, 0.16f, 0.85f * k), true, -1f, true);
            ci.Arc(gp, 4.5f * s, 0f, Mathf.Tau, 14, Palette.Warning.WithAlpha(k), 1f, true);
            float a = -Mathf.Pi * 0.75f + (_time * 2f % 1f) * 0.3f;
            ci.DrawLine(gp, gp + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.5f * s, new Color(1f, 0.9f, 0.7f, k), 1f, true);
        }
        // 조용한 걸음: 발끝 점 두 개
        if (g.Quiet(w) && c.IsMoving)
        {
            var back = body - facing * (rr + 3f * s);
            var side = new Vector2(-facing.Y, facing.X) * 2.2f * s;
            ci.Circle(back + side, 1.1f * s, new Color(1, 1, 1, 0.35f), true, -1f, true);
            ci.Circle(back - side, 1.1f * s, new Color(1, 1, 1, 0.35f), true, -1f, true);
        }
        // 비켜섰다: 길을 내준 쪽에 짧은 곡선
        if (w.Tick - g.YieldUntil < Gait.Linger / 2 && g.Aside != System.Numerics.Vector2.Zero)
        {
            var dir = new Vector2(g.Aside.X, g.Aside.Y).Normalized();
            ci.Arc(body - dir * (rr + 3f * s), rr * 0.6f, dir.Angle() + Mathf.Pi - 0.7f, dir.Angle() + Mathf.Pi + 0.7f, 8, new Color(1, 1, 1, 0.4f), 1.2f, true);
        }
    }
}
