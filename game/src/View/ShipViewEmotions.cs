using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.15 승무원 머리 위 감정 그림 — 감정마다 실루엣 · 무늬 · 움직임이 다르다.
// 분노: 붉은 핏대 십자 + 피어오르는 김(떨림) · 두려움: 식은땀 방울 + 떨리는 물결선(좌우로 떨린다) ·
// 기쁨: 빙글 도는 네 갈래 반짝이 · 슬픔: 비구름 + 떨어지는 빗방울 · 수치: 볼의 홍조 빗금 + 말줄임 · 자부심: 금빛 왕관 갈매기표 + 쓸고 지나가는 광택.
// 사람마다 다르게: 위상(사람 번호) · 세기(크기 · 투명도) · 침착한 사람은 느리게, 사교적인 사람은 크게. 멀리서는 색 테 실루엣만.
// 읽기만 한다 (Emotions.Dominant는 상태를 바꾸지 않는다).
public static class EmotionGlyphs
{
    public static Color Of(Feeling f) => f switch
    {
        Feeling.Anger => new Color(1f, 0.32f, 0.25f),
        Feeling.Fear => new Color(0.62f, 0.82f, 1f),
        Feeling.Joy => new Color(1f, 0.86f, 0.3f),
        Feeling.Sadness => new Color(0.55f, 0.62f, 0.78f),
        Feeling.Shame => new Color(1f, 0.55f, 0.7f),
        _ => new Color(1f, 0.74f, 0.25f),
    };

    /// <summary>감정 그림 하나 (center 기준, size = 반지름쯤). t = 시간, phase = 사람마다 다른 위상, speed = 움직임 빠르기, detail = 확대 정도.</summary>
    public static void Draw(CanvasItem ci, Feeling f, Vector2 center, float size, float t, float phase, float speed, float alpha, bool detail)
    {
        var col = Of(f).WithAlpha(alpha);
        float a = t * speed + phase;
        switch (f)
        {
            case Feeling.Anger:
            {
                // 핏대: 네 개의 굽은 획이 십자를 이룬다 · 맥박처럼 커졌다 작아진다
                float pulse = 1f + 0.12f * Mathf.Sin(a * 7f);
                float s = size * pulse;
                var jit = new Vector2(Mathf.Sin(a * 31f), Mathf.Cos(a * 27f)) * size * 0.04f;
                for (int k = 0; k < 4; k++)
                {
                    float ang = k * Mathf.Pi / 2f + Mathf.Pi / 4f;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var c0 = center + jit + dir * s * 0.55f;
                    ci.Arc(c0, s * 0.42f, ang + Mathf.Pi * 0.75f, ang + Mathf.Pi * 1.25f, 8, col, Mathf.Max(1.4f, s * 0.2f), true);
                }
                if (detail)
                    for (int k = 0; k < 3; k++)
                    {
                        // 피어오르는 김 (위로 흩어지는 짧은 곡선)
                        float u = Mathf.PosMod(a * 0.9f + k * 0.33f, 1f);
                        var p0 = center + new Vector2((k - 1) * s * 0.7f, -s * (0.9f + u * 1.4f));
                        var p1 = p0 + new Vector2(Mathf.Sin(a * 3f + k) * s * 0.25f, -s * 0.35f);
                        ci.DrawLine(p0, p1, col.WithAlpha(alpha * (1f - u) * 0.8f), Mathf.Max(1f, s * 0.12f), true);
                    }
                break;
            }
            case Feeling.Fear:
            {
                // 식은땀: 위가 뾰족한 물방울 + 떨리는 물결선 셋 (몸이 좌우로 떤다)
                float shake = Mathf.Sin(a * 38f) * size * 0.08f;
                var c0 = center + new Vector2(shake, Mathf.PosMod(a * 0.6f, 1f) * size * 0.5f);
                var drop = new Vector2[10];
                for (int k = 0; k < 10; k++)
                {
                    float th = k / 9f * Mathf.Tau;
                    float r = size * (0.55f - 0.25f * Mathf.Cos(th));
                    drop[k] = c0 + new Vector2(Mathf.Sin(th) * r * 0.75f, -Mathf.Cos(th) * r);
                }
                drop[0] = c0 + new Vector2(0, -size * 0.95f);
                ci.Poly(drop, col.WithAlpha(alpha * 0.85f));
                if (detail) ci.Circle(c0 + new Vector2(-size * 0.15f, size * 0.05f), size * 0.12f, new Color(1, 1, 1, alpha * 0.8f));
                for (int k = 0; k < 3; k++)
                {
                    var pts = new Vector2[6];
                    for (int j = 0; j < 6; j++)
                        pts[j] = center + new Vector2(-size * 1.05f - k * size * 0.28f, -size * 0.6f + j * size * 0.24f)
                                 + new Vector2(Mathf.Sin(j * 2.1f + a * 20f + k) * size * 0.1f, 0f);
                    ci.Polyline(pts, col.WithAlpha(alpha * (0.8f - k * 0.2f)), Mathf.Max(1f, size * 0.1f), true);
                }
                break;
            }
            case Feeling.Joy:
            {
                // 네 갈래 반짝이 셋이 서로 다른 박자로 깜빡이며 머리 둘레를 돈다
                for (int k = 0; k < 3; k++)
                {
                    float orbit = a * 1.3f + k * Mathf.Tau / 3f;
                    var c0 = center + new Vector2(Mathf.Cos(orbit), Mathf.Sin(orbit) * 0.5f) * size * 0.9f;
                    float tw = 0.55f + 0.45f * Mathf.Sin(a * 6f + k * 2.1f);
                    float s = size * 0.55f * tw;
                    var star = new Vector2[8];
                    for (int j = 0; j < 8; j++)
                    {
                        float th = j * Mathf.Pi / 4f + a * 2f;
                        float r = j % 2 == 0 ? s : s * 0.28f;
                        star[j] = c0 + new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * r;
                    }
                    // 중심에서 삼각형으로 직접 (엔진의 다각형 나누기는 작은 별에서 실패했다 — 화재 · 사고 장면마다 오류가 쌓였다)
                    var sc = col.WithAlpha(alpha * (0.6f + 0.4f * tw));
                    if (s >= 0.3f)
                        for (int j = 0; j < 8; j++) ci.DrawPrimitive(new[] { c0, star[j], star[(j + 1) % 8] }, new[] { sc, sc, sc }, null);
                    if (detail) ci.Circle(c0, s * 0.18f, new Color(1, 1, 1, alpha * tw));
                }
                break;
            }
            case Feeling.Sadness:
            {
                // 비구름 (둥근 셋) + 빗방울이 떨어진다
                var cloud = col.Darkened(0.15f);
                ci.Circle(center + new Vector2(-size * 0.45f, 0), size * 0.42f, cloud);
                ci.Circle(center + new Vector2(size * 0.05f, -size * 0.2f), size * 0.55f, cloud);
                ci.Circle(center + new Vector2(size * 0.55f, size * 0.02f), size * 0.38f, cloud);
                ci.Box(new Rect2(center.X - size * 0.8f, center.Y, size * 1.55f, size * 0.38f), cloud);
                int drops = detail ? 4 : 2;
                for (int k = 0; k < drops; k++)
                {
                    float u = Mathf.PosMod(a * 0.8f + k * 0.37f, 1f);
                    var p = center + new Vector2(-size * 0.55f + k * size * 0.38f, size * (0.5f + u * 1.3f));
                    ci.DrawLine(p, p + new Vector2(-size * 0.06f, size * 0.25f), new Color(0.6f, 0.75f, 1f, alpha * (1f - u)), Mathf.Max(1f, size * 0.1f), true);
                }
                break;
            }
            case Feeling.Shame:
            {
                // 홍조: 양 볼의 분홍 타원 + 빗금 + 말줄임 (고개를 살짝 숙인 듯 아래로)
                var dip = new Vector2(0f, size * 0.15f * (0.5f + 0.5f * Mathf.Sin(a * 1.5f)));
                foreach (float side in new[] { -1f, 1f })
                {
                    var c0 = center + dip + new Vector2(side * size * 0.7f, size * 0.6f);
                    var oval = new Vector2[10];
                    for (int j = 0; j < 10; j++) { float th = j / 10f * Mathf.Tau; oval[j] = c0 + new Vector2(Mathf.Cos(th) * size * 0.42f, Mathf.Sin(th) * size * 0.22f); }
                    ci.Poly(oval, col.WithAlpha(alpha * 0.55f));
                    if (detail)
                        for (int j = -1; j <= 1; j++)
                            ci.DrawLine(c0 + new Vector2(j * size * 0.18f - size * 0.08f, size * 0.12f), c0 + new Vector2(j * size * 0.18f + size * 0.08f, -size * 0.12f), col.Darkened(0.2f).WithAlpha(alpha), 1f, true);
                }
                for (int j = 0; j < 3; j++)
                {
                    float on = Mathf.PosMod(a * 1.2f - j * 0.25f, 1f) < 0.7f ? 1f : 0.3f;
                    ci.Circle(center + dip + new Vector2((j - 1) * size * 0.35f, -size * 0.45f), size * 0.1f, col.WithAlpha(alpha * on));
                }
                break;
            }
            default:
            {
                // 자부심: 금빛 왕관 (세 봉우리) + 위로 솟는 갈매기표 + 쓸고 지나가는 광택
                float bob = -Mathf.Abs(Mathf.Sin(a * 2.2f)) * size * 0.2f;
                var c0 = center + new Vector2(0, bob);
                var crown = new[]
                {
                    c0 + new Vector2(-size * 0.8f, size * 0.35f), c0 + new Vector2(-size * 0.8f, -size * 0.2f), c0 + new Vector2(-size * 0.4f, size * 0.05f),
                    c0 + new Vector2(0f, -size * 0.55f), c0 + new Vector2(size * 0.4f, size * 0.05f), c0 + new Vector2(size * 0.8f, -size * 0.2f), c0 + new Vector2(size * 0.8f, size * 0.35f),
                };
                ci.Poly(crown, col.WithAlpha(alpha * 0.9f));
                if (detail)
                {
                    foreach (var tip in new[] { crown[1], crown[3], crown[5] }) ci.Circle(tip, size * 0.1f, new Color(1f, 0.95f, 0.8f, alpha));
                    float sweep = Mathf.PosMod(a * 0.7f, 1.6f) - 0.3f;
                    var g0 = c0 + new Vector2(-size * 0.8f + sweep * size, size * 0.35f);
                    ci.DrawLine(g0, g0 + new Vector2(size * 0.35f, -size * 0.55f), new Color(1, 1, 1, alpha * 0.7f * Mathf.Clamp(1f - Mathf.Abs(sweep - 0.5f), 0f, 1f)), Mathf.Max(1f, size * 0.12f), true);
                }
                var chev = new[] { c0 + new Vector2(-size * 0.4f, -size * 0.75f), c0 + new Vector2(0f, -size * 1.05f), c0 + new Vector2(size * 0.4f, -size * 0.75f) };
                ci.Polyline(chev, col.WithAlpha(alpha * 0.8f), Mathf.Max(1.2f, size * 0.14f), true);
                break;
            }
        }
    }
}

public partial class ShipView
{
    /// <summary>v16.15 머리 위 감정 그림 (가장 큰 감정 하나 — 세기 · 성격대로 다르게).</summary>
    private void PaintEmotions(CanvasItem ci)
    {
        var w = _world;
        var emo = w.Brain2.Emotions;
        bool detail = Zoom >= 0.9f;
        bool crisis = Severity.Crisis(w);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Down || c.CarriedBy != null || c.Away) continue;
            if (HeadBadge.Shown(w, c, Zoom, _main.SelectedCrew == c, _main.HoveredCrew == c, crisis)) continue; // v17.1 머리 위 딱지 안에 함께 그린다
            if (emo.Dominant(c, 0.22f) is not { } d) continue;
            if (d.f == Feeling.Anger && c.Mind.Anger > 0.5f && !detail) continue; // 멀리서는 Mind 배지(#)와 겹치지 않게
            float r = CrewRadius;
            float size = r * (0.42f + 0.38f * Mathf.Clamp(d.v, 0f, 1f)) * (0.85f + 0.3f * c.Traits.Sociability);
            float speed = 1.4f - 0.8f * c.Traits.Calm;
            float phase = c.Id * 1.7f;
            float alpha = Mathf.Clamp(0.45f + 0.6f * d.v, 0f, 1f);
            var at = CrewPx(c) + (d.f == Feeling.Shame ? new Vector2(0f, -r * 0.35f) : new Vector2(r * 0.95f, -r * 1.35f));
            if (!detail)
            {
                // 멀리서: 색 테를 두른 작은 실루엣
                ci.Circle(at, size * 0.9f, new Color(0.05f, 0.06f, 0.09f, 0.55f * alpha));
                EmotionGlyphs.Draw(ci, d.f, at, size * 0.75f, _time, phase, speed, alpha, false);
                continue;
            }
            EmotionGlyphs.Draw(ci, d.f, at, size, _time, phase, speed, alpha, true);
        }
    }
}
