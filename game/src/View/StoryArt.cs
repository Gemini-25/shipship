using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v18.16 · v18.17 이야기의 그림: 이야기 틀마다 다른 상징(편지 · 반지 · 아이 그림 · 찢긴 사진 · 칩 · 장부 · 안전모 · 맥박 · 성냥 · 초 ·
/// 얼음 · 약병 · 안경 · 사원증 · 흉터 · 재 · 별 · 책 · 음표 · 씨앗 · 국자 · 자격증 · 가면 · 묵주 · 눌린 꽃 · 금 간 컵 …)
/// 과 대화 카드의 초상(머리 · 머리색 · 피부 · 옷 색 · 표정: 눈썹 · 눈 · 입 · 땀 · 눈물 · 볼 붉어짐).
/// 읽기만 한다.
/// </summary>
public static class StoryArt
{
    private static readonly Color[] Skins = { new("#f3cfb0"), new("#e2b08a"), new("#c99068"), new("#a8714a"), new("#7f5236"), new("#5b3a26") };
    private static readonly Color[] Hairs = { new("#1d1817"), new("#3b2a20"), new("#6b4b2f"), new("#8e4a26"), new("#cfa862"), new("#9a8a74"), new("#2f9a92"), new("#c95a95"), new("#bdb9b2") };

    private static void Poly(CanvasItem ci, Color c, params Vector2[] p) => ci.Poly(p, c);
    private static void Line(CanvasItem ci, Vector2 a, Vector2 b, Color c, float w = 1f) => ci.DrawLine(a, b, c, w, true);

    /// <summary>이야기 상징 하나 (가운데 p · 크기 s).</summary>
    public static void Emblem(CanvasItem ci, string key, Vector2 p, float s, Color ink, float t = 0f)
    {
        float h = s * 0.5f;
        var paper = new Color("#efe6cf"); var dark = new Color(0.08f, 0.08f, 0.1f, 0.9f);
        var gold = new Color("#e8c25a"); var red = new Color("#e2595b"); var blue = new Color("#6fb6ff"); var green = new Color("#5fcf7f");
        switch (key)
        {
            case "letter": // 접힌 편지 + 손글씨 줄
                ci.Box(new Rect2(p - new Vector2(h, h * 0.7f), new Vector2(s, s * 0.7f)), paper);
                Line(ci, p + new Vector2(-h, -h * 0.7f), p + new Vector2(0, 0), dark, 1f); Line(ci, p + new Vector2(h, -h * 0.7f), p, dark, 1f);
                for (int i = 0; i < 2; i++) Line(ci, p + new Vector2(-h * 0.6f, h * 0.15f + i * h * 0.22f), p + new Vector2(h * 0.5f, h * 0.15f + i * h * 0.22f), ink, 0.8f);
                break;
            case "ring":
                ci.Arc(p + new Vector2(0, h * 0.15f), h * 0.6f, 0, Mathf.Tau, 20, gold, s * 0.12f, true);
                Poly(ci, new Color("#bfe9ff"), p + new Vector2(0, -h * 0.85f), p + new Vector2(h * 0.25f, -h * 0.5f), p + new Vector2(0, -h * 0.3f), p + new Vector2(-h * 0.25f, -h * 0.5f));
                break;
            case "drawing": // 아이가 그린 배: 크레용 선 · 창문 점
                ci.Box(new Rect2(p - new Vector2(h, h * 0.75f), new Vector2(s, s * 0.75f)), paper);
                ci.Polyline(new[] { p + new Vector2(-h * 0.7f, h * 0.1f), p + new Vector2(h * 0.6f, h * 0.1f), p + new Vector2(h * 0.75f, -h * 0.15f), p + new Vector2(-h * 0.5f, -h * 0.3f), p + new Vector2(-h * 0.7f, h * 0.1f) }, blue, 1.2f, true);
                for (int i = 0; i < 4; i++) ci.Circle(p + new Vector2(-h * 0.4f + i * h * 0.28f, -h * 0.08f), s * 0.035f, red);
                break;
            case "torn_photo":
                Poly(ci, paper, p + new Vector2(-h, -h * 0.7f), p + new Vector2(h * 0.05f, -h * 0.7f), p + new Vector2(-h * 0.1f, -h * 0.2f), p + new Vector2(h * 0.1f, h * 0.2f), p + new Vector2(-h * 0.05f, h * 0.7f), p + new Vector2(-h, h * 0.7f));
                Poly(ci, paper.Darkened(0.15f), p + new Vector2(h * 0.25f, -h * 0.75f), p + new Vector2(h, -h * 0.75f), p + new Vector2(h, h * 0.65f), p + new Vector2(h * 0.2f, h * 0.65f), p + new Vector2(h * 0.4f, h * 0.15f), p + new Vector2(h * 0.15f, -h * 0.25f));
                ci.Circle(p + new Vector2(-h * 0.55f, -h * 0.1f), s * 0.12f, ink.Darkened(0.3f));
                break;
            case "envelope":
                ci.Box(new Rect2(p - new Vector2(h, h * 0.6f), new Vector2(s, s * 0.6f)), paper);
                Poly(ci, paper.Darkened(0.12f), p + new Vector2(-h, -h * 0.6f), p + new Vector2(h, -h * 0.6f), p + new Vector2(0, h * 0.05f));
                ci.Circle(p + new Vector2(0, h * 0.05f), s * 0.08f, red);
                break;
            case "chips": // 쌓인 판돈 칩
                for (int i = 0; i < 3; i++) { var q = p + new Vector2(-h * 0.2f + i * h * 0.2f, h * 0.4f - i * h * 0.35f); ci.Circle(q, h * 0.45f, i == 1 ? red : i == 2 ? blue : gold); ci.Arc(q, h * 0.3f, 0, Mathf.Tau, 12, paper, 0.8f, true); }
                break;
            case "ledger": // 장부: 줄 · 붉은 숫자
                ci.Box(new Rect2(p - new Vector2(h * 0.8f, h), new Vector2(s * 0.8f, s)), new Color("#3c5a46"));
                ci.Box(new Rect2(p - new Vector2(h * 0.65f, h * 0.85f), new Vector2(s * 0.65f, s * 0.85f)), paper);
                for (int i = 0; i < 4; i++) Line(ci, p + new Vector2(-h * 0.5f, -h * 0.55f + i * h * 0.35f), p + new Vector2(h * 0.4f, -h * 0.55f + i * h * 0.35f), i == 3 ? red : dark, 0.8f);
                break;
            case "stamp": // 독촉 도장
                ci.Box(new Rect2(p + new Vector2(-h * 0.2f, -h), new Vector2(s * 0.2f, s * 0.6f)), new Color("#7a5a3a"));
                ci.Box(new Rect2(p + new Vector2(-h * 0.6f, -h * 0.4f), new Vector2(s * 0.6f, s * 0.25f)), dark);
                ci.Arc(p + new Vector2(0, h * 0.55f), h * 0.45f, 0, Mathf.Tau, 16, red, 1.4f, true);
                break;
            case "helmet": // 광부 안전모 + 등
                ci.Circle(p + new Vector2(0, h * 0.15f), h * 0.75f, gold);
                ci.Box(new Rect2(p + new Vector2(-h, h * 0.1f), new Vector2(s, s * 0.45f)), new Color(0.08f, 0.08f, 0.1f, 1f));
                ci.Circle(p + new Vector2(0, -h * 0.25f), h * 0.22f, new Color(1f, 1f, 0.75f, 0.6f + 0.4f * Mathf.Sin(t * 3f) * 0.5f + 0.2f));
                break;
            case "pulse": // 끊긴 맥박선
                ci.Polyline(new[] { p + new Vector2(-h, 0), p + new Vector2(-h * 0.4f, 0), p + new Vector2(-h * 0.2f, -h * 0.7f), p + new Vector2(0, h * 0.6f), p + new Vector2(h * 0.2f, 0), p + new Vector2(h, 0) }, green, 1.4f, true);
                ci.Circle(p + new Vector2(h, 0), s * 0.06f, red);
                break;
            case "match":
                Line(ci, p + new Vector2(-h * 0.6f, h * 0.8f), p + new Vector2(h * 0.3f, -h * 0.3f), new Color("#d9b27a"), s * 0.1f);
                ci.Circle(p + new Vector2(h * 0.35f, -h * 0.35f), s * 0.11f, red);
                Poly(ci, new Color(1f, 0.6f, 0.2f, 0.85f), p + new Vector2(h * 0.2f, -h * 0.45f), p + new Vector2(h * 0.5f, -h * (1f + 0.1f * Mathf.Sin(t * 9f))), p + new Vector2(h * 0.6f, -h * 0.35f));
                break;
            case "candle":
                ci.Box(new Rect2(p + new Vector2(-h * 0.25f, -h * 0.2f), new Vector2(s * 0.25f, s * 0.6f)), paper);
                Poly(ci, new Color(1f, 0.75f, 0.3f, 0.9f), p + new Vector2(-h * 0.12f, -h * 0.25f), p + new Vector2(0, -h * (0.85f + 0.08f * Mathf.Sin(t * 7f))), p + new Vector2(h * 0.12f, -h * 0.25f));
                ci.Circle(p + new Vector2(0, -h * 0.4f), h * 0.45f, new Color(1f, 0.8f, 0.4f, 0.12f));
                break;
            case "ice":
                Poly(ci, new Color("#bfe9ff"), p + new Vector2(0, -h), p + new Vector2(h * 0.8f, -h * 0.2f), p + new Vector2(h * 0.4f, h * 0.9f), p + new Vector2(-h * 0.5f, h * 0.8f), p + new Vector2(-h * 0.85f, -h * 0.1f));
                Line(ci, p + new Vector2(-h * 0.2f, -h * 0.5f), p + new Vector2(h * 0.15f, h * 0.4f), new Color(1, 1, 1, 0.8f), 0.8f);
                break;
            case "pills": // 약병 + 알약
                ci.Box(new Rect2(p + new Vector2(-h * 0.45f, -h * 0.5f), new Vector2(s * 0.45f, s * 0.7f)), new Color("#e9a23b"));
                ci.Box(new Rect2(p + new Vector2(-h * 0.5f, -h * 0.75f), new Vector2(s * 0.5f, s * 0.15f)), paper);
                ci.Box(new Rect2(p + new Vector2(-h * 0.4f, -h * 0.2f), new Vector2(s * 0.4f, s * 0.2f)), paper.Darkened(0.05f));
                ci.Circle(p + new Vector2(h * 0.55f, h * 0.6f), s * 0.09f, paper); ci.Circle(p + new Vector2(h * 0.85f, h * 0.35f), s * 0.07f, paper);
                break;
            case "glasses":
                ci.Arc(p + new Vector2(-h * 0.45f, 0), h * 0.35f, 0, Mathf.Tau, 14, ink, 1.3f, true);
                ci.Arc(p + new Vector2(h * 0.45f, 0), h * 0.35f, 0, Mathf.Tau, 14, ink, 1.3f, true);
                Line(ci, p + new Vector2(-h * 0.1f, 0), p + new Vector2(h * 0.1f, 0), ink, 1.2f);
                Line(ci, p + new Vector2(h * 0.25f, -h * 0.2f), p + new Vector2(h * 0.65f, h * 0.2f), new Color(1, 1, 1, 0.6f), 0.7f);
                break;
            case "badge": // 회사 사원증
                ci.Box(new Rect2(p - new Vector2(h * 0.7f, h * 0.9f), new Vector2(s * 0.7f, s * 0.9f)), new Color("#d8dde6"));
                ci.Box(new Rect2(p + new Vector2(-h * 0.55f, -h * 0.7f), new Vector2(s * 0.3f, s * 0.3f)), ink.Darkened(0.4f));
                ci.Box(new Rect2(p + new Vector2(-h * 0.7f, h * 0.5f), new Vector2(s * 0.7f, s * 0.15f)), red);
                Line(ci, p + new Vector2(0, -h * 0.9f), p + new Vector2(0, -h * 1.1f), dark, 1f);
                break;
            case "scar": // 꿰맨 흉터
                ci.Polyline(new[] { p + new Vector2(-h, h * 0.3f), p + new Vector2(-h * 0.2f, -h * 0.1f), p + new Vector2(h, -h * 0.4f) }, red, 1.6f, true);
                for (int i = 0; i < 4; i++) { var q = p + new Vector2(-h * 0.75f + i * h * 0.5f, h * 0.15f - i * h * 0.17f); Line(ci, q + new Vector2(-h * 0.1f, -h * 0.2f), q + new Vector2(h * 0.1f, h * 0.2f), paper, 0.9f); }
                break;
            case "ash":
                for (int i = 0; i < 6; i++) ci.Circle(p + new Vector2(-h * 0.6f + (i * 37 % 10) * h * 0.13f, h * 0.5f - (i * 53 % 7) * h * 0.1f - t * 0f), s * (0.05f + i % 3 * 0.02f), new Color(0.5f, 0.5f, 0.52f, 0.9f));
                ci.Arc(p + new Vector2(0, h * 0.7f), h * 0.7f, Mathf.Pi, Mathf.Tau, 12, new Color(0.4f, 0.4f, 0.42f), 1.2f, true);
                break;
            case "star":
                {
                    var pts = new Vector2[10];
                    for (int i = 0; i < 10; i++) { float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5; float r = i % 2 == 0 ? h : h * 0.42f; pts[i] = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
                    ci.Poly(pts, gold);
                    ci.Circle(p, h * 0.9f, new Color(1f, 0.9f, 0.5f, 0.08f + 0.05f * Mathf.Sin(t * 2f)));
                    break;
                }
            case "book":
                Poly(ci, new Color("#7a4bd0"), p + new Vector2(-h, -h * 0.6f), p + new Vector2(0, -h * 0.4f), p + new Vector2(0, h * 0.8f), p + new Vector2(-h, h * 0.6f));
                Poly(ci, paper, p + new Vector2(0, -h * 0.4f), p + new Vector2(h, -h * 0.6f), p + new Vector2(h, h * 0.6f), p + new Vector2(0, h * 0.8f));
                for (int i = 0; i < 3; i++) Line(ci, p + new Vector2(h * 0.15f, -h * 0.15f + i * h * 0.25f), p + new Vector2(h * 0.8f, -h * 0.25f + i * h * 0.25f), dark, 0.7f);
                break;
            case "note": // 음표 + 오선
                for (int i = 0; i < 3; i++) Line(ci, p + new Vector2(-h, -h * 0.4f + i * h * 0.35f), p + new Vector2(h, -h * 0.4f + i * h * 0.35f), ink.WithAlpha(0.5f), 0.6f);
                ci.Circle(p + new Vector2(-h * 0.2f, h * 0.45f), h * 0.25f, ink);
                Line(ci, p + new Vector2(h * 0.03f, h * 0.45f), p + new Vector2(h * 0.03f, -h * 0.8f), ink, 1.2f);
                Line(ci, p + new Vector2(h * 0.03f, -h * 0.8f), p + new Vector2(h * 0.45f, -h * 0.5f), ink, 1.4f);
                break;
            case "seed": // 싹 튼 씨앗
                ci.Circle(p + new Vector2(0, h * 0.5f), h * 0.3f, new Color("#8a6236"));
                Line(ci, p + new Vector2(0, h * 0.3f), p + new Vector2(0, -h * 0.3f), green, 1.3f);
                Poly(ci, green, p + new Vector2(0, -h * 0.25f), p + new Vector2(-h * 0.6f, -h * 0.6f), p + new Vector2(-h * 0.1f, -h * 0.05f));
                Poly(ci, green.Lightened(0.2f), p + new Vector2(0, -h * 0.35f), p + new Vector2(h * 0.55f, -h * 0.75f), p + new Vector2(h * 0.1f, -h * 0.1f));
                break;
            case "ladle":
                Line(ci, p + new Vector2(h * 0.8f, -h * 0.9f), p + new Vector2(-h * 0.05f, h * 0.15f), new Color("#c9ced8"), s * 0.09f);
                ci.Circle(p + new Vector2(-h * 0.3f, h * 0.4f), h * 0.42f, new Color("#c9ced8"));
                ci.Circle(p + new Vector2(-h * 0.3f, h * 0.35f), h * 0.28f, new Color("#e8b04a"));
                break;
            case "card": // 자격증: 사진 · 도장 · 물음표 같은 흐린 줄
                ci.Box(new Rect2(p - new Vector2(h, h * 0.65f), new Vector2(s, s * 0.65f)), new Color("#e3e8f0"));
                ci.Box(new Rect2(p + new Vector2(-h * 0.85f, -h * 0.5f), new Vector2(s * 0.3f, s * 0.4f)), ink.Darkened(0.3f));
                for (int i = 0; i < 3; i++) Line(ci, p + new Vector2(-h * 0.1f, -h * 0.35f + i * h * 0.28f), p + new Vector2(h * 0.8f, -h * 0.35f + i * h * 0.28f), dark.WithAlpha(0.5f), 0.7f);
                ci.Arc(p + new Vector2(h * 0.6f, h * 0.35f), h * 0.2f, 0, Mathf.Tau, 10, red.WithAlpha(0.7f), 1f, true);
                break;
            case "mask":
                Poly(ci, paper, p + new Vector2(-h, -h * 0.3f), p + new Vector2(h, -h * 0.3f), p + new Vector2(h * 0.7f, h * 0.4f), p + new Vector2(0, h * 0.2f), p + new Vector2(-h * 0.7f, h * 0.4f));
                ci.Circle(p + new Vector2(-h * 0.4f, -h * 0.05f), h * 0.15f, dark); ci.Circle(p + new Vector2(h * 0.4f, -h * 0.05f), h * 0.15f, dark);
                break;
            case "beads": // 묵주
                for (int i = 0; i < 12; i++) { float a = i / 12f * Mathf.Tau; ci.Circle(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * h * 0.7f, s * 0.06f, new Color("#a8774a")); }
                Line(ci, p + new Vector2(0, h * 0.55f), p + new Vector2(0, h), new Color("#a8774a"), 1.2f);
                Line(ci, p + new Vector2(-h * 0.15f, h * 0.8f), p + new Vector2(h * 0.15f, h * 0.8f), new Color("#a8774a"), 1.2f);
                break;
            case "pressed_flower":
                ci.Box(new Rect2(p - new Vector2(h * 0.8f, h), new Vector2(s * 0.8f, s)), paper);
                for (int i = 0; i < 5; i++) { float a = i / 5f * Mathf.Tau; ci.Circle(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h * 0.28f + new Vector2(0, -h * 0.2f), h * 0.18f, new Color("#d98ab0")); }
                Line(ci, p + new Vector2(0, -h * 0.05f), p + new Vector2(h * 0.05f, h * 0.8f), green.Darkened(0.2f), 1f);
                break;
            case "broken_cup":
                Poly(ci, new Color("#dfe6ee"), p + new Vector2(-h * 0.7f, -h * 0.5f), p + new Vector2(-h * 0.05f, -h * 0.5f), p + new Vector2(-h * 0.2f, 0), p + new Vector2(0, h * 0.7f), p + new Vector2(-h * 0.55f, h * 0.7f));
                Poly(ci, new Color("#cfd6de"), p + new Vector2(h * 0.15f, -h * 0.5f), p + new Vector2(h * 0.7f, -h * 0.5f), p + new Vector2(h * 0.55f, h * 0.7f), p + new Vector2(h * 0.2f, h * 0.7f), p + new Vector2(h * 0.05f, 0));
                ci.Arc(p + new Vector2(h * 0.75f, 0), h * 0.25f, -Mathf.Pi / 2, Mathf.Pi / 2, 8, new Color("#cfd6de"), 1.4f, true);
                break;
            default:
                ci.Circle(p, h * 0.6f, ink);
                break;
        }
    }

    /// <summary>대화 카드의 초상: 머리 · 머리카락 · 옷깃 · 표정.</summary>
    public static void Portrait(CanvasItem ci, Vector2 c, float r, CrewMember who, BodyLook? look, FaceLook face, Color cloth, float t)
    {
        var skin = Skins[(look?.Skin ?? (byte)(who.Id % 6)) % Skins.Length];
        var hair = Hairs[Mathf.Clamp(look?.HairColor ?? (who.Id * 7 % 5), 0, Hairs.Length - 1)];
        ci.Circle(c, r * 1.12f, new Color(0, 0, 0, 0.35f));
        // 옷깃 (직업 옷 색)
        Poly(ci, cloth, c + new Vector2(-r * 0.95f, r * 1.05f), c + new Vector2(-r * 0.5f, r * 0.55f), c + new Vector2(r * 0.5f, r * 0.55f), c + new Vector2(r * 0.95f, r * 1.05f));
        ci.Circle(c, r * 0.72f, skin);
        // 머리카락: 대머리면 윤기만
        bool bald = look?.Style == HairStyle.Bald;
        if (!bald)
        {
            ci.Arc(c + new Vector2(0, -r * 0.05f), r * 0.66f, Mathf.Pi * 1.02f, Mathf.Pi * 1.98f, 18, hair, r * 0.32f, true);
            if (who.Id % 3 == 0) ci.Circle(c + new Vector2(r * 0.55f, -r * 0.15f), r * 0.2f, hair); // 옆머리
        }
        else ci.Arc(c, r * 0.55f, Mathf.Pi * 1.2f, Mathf.Pi * 1.5f, 8, new Color(1, 1, 1, 0.3f), 1.2f, true);
        var ink = new Color(0.1f, 0.07f, 0.06f);
        float ex = r * 0.26f, ey = -r * 0.05f;
        bool blink = Mathf.PosMod(t * 0.3f + who.Id * 0.37f, 1f) < 0.04f;
        // 눈썹: 화 = 안으로 내려감 · 걱정/두려움 = 안쪽이 올라감 · 슬픔 = 처짐
        float inner = face switch { FaceLook.Angry => r * 0.1f, FaceLook.Worry or FaceLook.Fear or FaceLook.Sad => -r * 0.1f, _ => 0f };
        float outer = face == FaceLook.Sad ? r * 0.06f : 0f;
        for (int s = -1; s <= 1; s += 2)
        {
            var e = c + new Vector2(s * ex, ey);
            Line(ci, e + new Vector2(s * r * 0.16f, -r * 0.18f + outer), e + new Vector2(-s * r * 0.08f, -r * 0.18f + inner), hair.Darkened(0.2f), r * 0.07f);
            if (blink || face is FaceLook.Asleep) Line(ci, e - new Vector2(r * 0.09f, 0), e + new Vector2(r * 0.09f, 0), ink, r * 0.05f);
            else if (face == FaceLook.Smile) ci.Arc(e + new Vector2(0, r * 0.04f), r * 0.08f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 6, ink, r * 0.05f, true);
            else ci.Circle(e, face == FaceLook.Fear ? r * 0.09f : r * 0.065f, ink);
            if (face == FaceLook.Fear) ci.Circle(e, r * 0.035f, new Color(1, 1, 1));
        }
        // 입
        var m = c + new Vector2(0, r * 0.32f);
        switch (face)
        {
            case FaceLook.Smile: ci.Arc(m - new Vector2(0, r * 0.1f), r * 0.2f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 10, ink, r * 0.06f, true); ci.Circle(c + new Vector2(-r * 0.42f, r * 0.15f), r * 0.1f, new Color(1f, 0.45f, 0.45f, 0.35f)); ci.Circle(c + new Vector2(r * 0.42f, r * 0.15f), r * 0.1f, new Color(1f, 0.45f, 0.45f, 0.35f)); break;
            case FaceLook.Sad or FaceLook.Pain: ci.Arc(m + new Vector2(0, r * 0.12f), r * 0.18f, Mathf.Pi * 1.2f, Mathf.Pi * 1.8f, 10, ink, r * 0.06f, true); break;
            case FaceLook.Fear: ci.Circle(m + new Vector2(0, r * 0.02f), r * 0.09f, ink); break;
            case FaceLook.Angry: Line(ci, m + new Vector2(-r * 0.16f, r * 0.03f), m + new Vector2(r * 0.16f, -r * 0.02f), ink, r * 0.07f); break;
            case FaceLook.Worry: ci.Polyline(new[] { m + new Vector2(-r * 0.16f, 0), m + new Vector2(-r * 0.05f, -r * 0.04f), m + new Vector2(r * 0.05f, r * 0.02f), m + new Vector2(r * 0.16f, -r * 0.02f) }, ink, r * 0.05f, true); break;
            default: Line(ci, m + new Vector2(-r * 0.12f, 0), m + new Vector2(r * 0.12f, 0), ink, r * 0.05f); break;
        }
        // 눈물 · 땀
        if (face is FaceLook.Sad) ci.Circle(c + new Vector2(-ex, ey + r * 0.18f + Mathf.PosMod(t * 0.6f, 1f) * r * 0.25f), r * 0.05f, new Color("#9fd4ff"));
        if (face is FaceLook.Fear or FaceLook.Worry) ci.Circle(c + new Vector2(r * 0.6f, -r * 0.35f), r * 0.07f, new Color("#bfe6ff"));
        if (face is FaceLook.Angry) for (int i = 0; i < 3; i++) Line(ci, c + new Vector2(r * 0.6f + i * r * 0.1f, -r * 0.75f), c + new Vector2(r * 0.68f + i * r * 0.1f, -r * 0.55f), new Color(1f, 0.4f, 0.35f), 1.2f);
    }
}
