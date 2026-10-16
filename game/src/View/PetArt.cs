using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>v23 반려동물 · 외계 생물 그림: 개(꼬리 흔들기) · 새(날갯짓 · 노래 음표) · 물고기(수조 곁 물방울) · 벌(작은 점 무리) · 외계 애완동물(말랑한 몸 · 불빛 점) · 숨어든 외계 생물(여섯 다리 · 붉은 눈).</summary>
internal static class PetArt
{
    private static Vector2 R(Vector2 v, float a) => v.Rotated(a);

    public static void Draw(CanvasItem ci, Pet p, Vector2 at, float t, bool close)
    {
        var face = new Vector2(p.Facing.X, p.Facing.Y);
        if (face == Vector2.Zero) face = Vector2.Right;
        float ang = face.Angle();
        bool moving = p.Path != null && p.PathIndex < p.Path.Count;
        switch (p.Kind)
        {
            case PetKind.Dog:
            {
                var fur = (p.Name.Length % 3) switch { 0 => new Color("#c8955a"), 1 => new Color("#f0e6d6"), _ => new Color("#4a3a2e") };
                float step = moving ? Mathf.Sin(t * 9f) : 0f;
                for (int i = 0; i < 4; i++)
                {
                    var hip = new Vector2(i < 2 ? 5f : -5f, i % 2 == 0 ? -3f : 3f);
                    ci.DrawLine(at + R(hip, ang), at + R(hip + new Vector2(step * 1.8f * (i % 2 == 0 ? 1 : -1), i % 2 == 0 ? -1.5f : 1.5f), ang), fur.Darkened(0.2f), 2f);
                }
                ci.DrawCircle(at, 5.5f, fur); // 몸
                ci.DrawCircle(at + R(new Vector2(-3f, 0), ang), 5f, fur);
                var head = at + R(new Vector2(7f, 0), ang);
                ci.DrawCircle(head, 3.8f, fur);
                ci.DrawCircle(head + R(new Vector2(1.5f, -2.8f), ang), 1.6f, fur.Darkened(0.3f)); // 귀
                ci.DrawCircle(head + R(new Vector2(1.5f, 2.8f), ang), 1.6f, fur.Darkened(0.3f));
                ci.DrawCircle(head + R(new Vector2(3.4f, 0), ang), 0.9f, new Color("#1a1a1a")); // 코
                float wag = Mathf.Sin(t * (p.PettedAt > 0 ? 14f : 5f)) * 0.7f;
                ci.DrawLine(at + R(new Vector2(-8f, 0), ang), at + R(new Vector2(-12f, 0), ang + wag), fur.Darkened(0.1f), 2f); // 꼬리
                break;
            }
            case PetKind.Bird:
            {
                var body = new Color("#f2d24a");
                float hop = moving ? Mathf.Abs(Mathf.Sin(t * 10f)) * 3f : 0f;
                var b = at + new Vector2(0, -hop);
                float flap = moving ? Mathf.Sin(t * 22f) * 3f : 0.5f;
                ci.DrawLine(b + new Vector2(-1f, 0), b + new Vector2(-5f, -flap), body.Darkened(0.2f), 1.5f); // 날개
                ci.DrawLine(b + new Vector2(1f, 0), b + new Vector2(5f, -flap), body.Darkened(0.2f), 1.5f);
                ci.DrawCircle(b, 3f, body);
                ci.DrawCircle(b + R(new Vector2(3f, 0), ang), 2f, body.Lightened(0.1f));
                ci.DrawLine(b + R(new Vector2(4.5f, 0), ang), b + R(new Vector2(6f, 0), ang), new Color("#e07a2a"), 1f); // 부리
                if (!moving && close && Mathf.PosMod(t, 6f) < 1.4f) ci.DrawString(ThemeDB.FallbackFont, b + new Vector2(4f, -6f), "♪", HorizontalAlignment.Left, -1, 10, new Color(1f, 1f, 1f, 0.8f));
                break;
            }
            case PetKind.Fish:
                for (int i = 0; i < 3; i++)
                {
                    float fx = Mathf.Sin(t * 0.8f + i * 2.1f) * 6f, fy = Mathf.Cos(t * 0.6f + i) * 3f;
                    var f = at + new Vector2(fx, fy - 4f);
                    ci.DrawCircle(f, 1.6f, i == 0 ? new Color("#ff8a3c") : i == 1 ? new Color("#5fd0ff") : new Color("#ffd04a"));
                    if (close) ci.DrawCircle(f + new Vector2(0, -3f - Mathf.PosMod(t * 3f + i, 6f)), 0.6f, new Color(1, 1, 1, 0.5f));
                }
                break;
            case PetKind.Bees:
                for (int i = 0; i < 6; i++)
                {
                    var bp = at + new Vector2(Mathf.Sin(t * 3f + i * 1.7f) * 9f, Mathf.Cos(t * 2.3f + i * 2.3f) * 6f);
                    ci.DrawCircle(bp, 0.9f, new Color("#f5c518"));
                    if (close) ci.DrawCircle(bp + new Vector2(0.6f, 0), 0.5f, new Color("#2a2a2a"));
                }
                break;
            case PetKind.Alien:
            {
                float br = 1f + 0.08f * Mathf.Sin(t * 2.4f);
                var skin = new Color("#7ad0b8");
                ci.DrawCircle(at, 5.5f * br, skin);
                ci.DrawCircle(at + new Vector2(0, 2f), 4.5f * br, skin.Darkened(0.15f));
                for (int i = 0; i < 4; i++) ci.DrawCircle(at + new Vector2(Mathf.Cos(i * 1.6f) * 3f, Mathf.Sin(i * 1.6f) * 3f), 0.9f, new Color("#e8ff9a", 0.5f + 0.5f * Mathf.Sin(t * 3f + i)));
                ci.DrawCircle(at + R(new Vector2(3f, -1.5f), ang), 1f, new Color("#1a2a28"));
                break;
            }
            default: // 숨어든 외계 생물
            {
                var shell = new Color("#6a3a5a");
                float step = moving ? Mathf.Sin(t * 14f) : 0f;
                for (int i = 0; i < 6; i++)
                {
                    float y = (i % 3 - 1) * 3f, side = i < 3 ? -1f : 1f;
                    ci.DrawLine(at + R(new Vector2(y, side * 2f), ang), at + R(new Vector2(y + step * 1.5f * side, side * 6f), ang), shell.Darkened(0.3f), 1.2f);
                }
                ci.DrawCircle(at, 4.5f, shell);
                ci.DrawCircle(at + R(new Vector2(3f, -1.2f), ang), 0.8f, new Color("#ff3a3a"));
                ci.DrawCircle(at + R(new Vector2(3f, 1.2f), ang), 0.8f, new Color("#ff3a3a"));
                break;
            }
        }
    }
}
