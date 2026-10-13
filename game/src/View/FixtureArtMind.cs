using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v23 마음 돌봄 — 오락실 그림 (2종).
/// 오락기(보라 몸통 · 화면 속 작은 비행선 · 조이스틱 · 누가 앉으면 화면이 번쩍) · 가상 고향 의자(눕는 의자 · 머리 덮개 · 쓰는 사람이 있으면 고향 하늘빛이 감돈다).
/// </summary>
public static partial class FixtureArt
{
    private static void MindArt(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.ArcadeCabinet] = new(ArcadeBody, ArcadeLife, ArcadeFine, Look.Flicker, 0.5f, 0.3f);
        t[FurnitureType.VrChair] = new(VrBody, VrLife, VrFine, Look.Flicker, 0.5f, 0.2f);
    }

    private static readonly Color ArcadePurple = new("#3a2458");
    private static readonly Color ArcadePink = new("#ff7ad0");
    private static readonly Color VrSeat = new("#2a3a5a");
    private static readonly Color HomeSky = new("#8ad0ff");

    // ─────────────── 오락기 ───────────────

    private static void ArcadeBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, ArcadePurple, 2, ArcadePink.Darkened(0.4f), 1);
        ci.Box(x.Q(0.12f, 0.08f, 0.88f, 0.5f), new Color("#0a0a18")); // 화면
        ci.Box(x.Q(0.08f, 0.56f, 0.92f, 0.78f), ArcadePurple.Lightened(0.15f)); // 조작판
        Dot(ci, x.P(0.3f, 0.66f), 1.6f, new Color("#e0e0e0")); // 조이스틱 머리
        Dot(ci, x.P(0.62f, 0.64f), 1.1f, new Color("#ff5a5a")); // 단추 둘
        Dot(ci, x.P(0.76f, 0.68f), 1.1f, new Color("#ffd04a"));
        ci.Box(x.Q(0.1f, 0.84f, 0.9f, 0.9f), ArcadePink.WithAlpha(0.6f)); // 아래 띠 불빛
    }

    private static void ArcadeLife(in Fix x)
    {
        var ci = x.Ci;
        if (x.Glow <= 0.05f) return;
        int players = SittingIn(x);
        var scr = x.Q(0.12f, 0.08f, 0.88f, 0.5f);
        float tt = x.T * (players > 0 ? 1.4f : 0.5f);
        // 화면 속: 작은 비행선이 좌우로 · 별이 흐른다
        for (int k = 0; k < 5; k++)
        {
            float u = Mathf.PosMod(Hash(x.Id, k, 7) - tt * 0.3f * (1 + k % 2), 1f);
            Dot(ci, scr.Position + new Vector2(scr.Size.X * Hash(x.Id, k, 3), scr.Size.Y * u), 0.5f, Colors.White.WithAlpha(0.7f * x.Glow));
        }
        var ship = scr.Position + new Vector2(scr.Size.X * (0.5f + 0.35f * Mathf.Sin(tt * 2f)), scr.Size.Y * 0.8f);
        ci.Box(new Rect2(ship - new Vector2(1.5f, 1f), new Vector2(3f, 2f)), ArcadePink.Lightened(0.3f) with { A = x.Glow });
        if (players > 0 && Mathf.PosMod(tt, 2f) < 0.15f) ci.Box(scr, Colors.White.WithAlpha(0.25f * x.Glow)); // 번쩍 (맞혔다)
    }

    private static void ArcadeFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 6; k++) Dot(ci, x.P(0.12f + k * 0.15f, 0.87f), 0.5f, (k % 2 == 0 ? ArcadePink : new Color("#7ad0ff")).WithAlpha(0.5f + 0.5f * x.Glow));
        Line(ci, x.P(0.3f, 0.66f), x.P(0.3f, 0.74f), new Color("#888888"), 0.6f); // 조이스틱 막대
    }

    // ─────────────── 가상 고향 의자 ───────────────

    private static void VrBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.1f, 0.3f, 0.9f, 0.95f), VrSeat, 3, VrSeat.Lightened(0.25f), 1); // 눕는 의자
        Box(ci, x.Q(0.22f, 0.02f, 0.78f, 0.28f), new Color("#d8dce8"), 3, new Color("#7a8098"), 1); // 머리 덮개
        ci.Box(x.Q(0.3f, 0.12f, 0.7f, 0.2f), new Color("#1a2030")); // 덮개 창
        Line(ci, x.P(0.85f, 0.25f), x.P(0.95f, 0.9f), new Color("#4a5068"), 0.8f); // 선
    }

    private static void VrLife(in Fix x)
    {
        var ci = x.Ci;
        if (x.Glow <= 0.05f) return;
        bool used = SittingIn(x) > 0;
        float a = (used ? 0.55f : 0.15f) * x.Glow * (0.8f + 0.2f * Mathf.Sin(x.T * 1.5f));
        ci.Box(x.Q(0.3f, 0.12f, 0.7f, 0.2f), HomeSky.WithAlpha(a)); // 덮개 창의 고향 하늘
        if (used) Ring(ci, x.P(0.5f, 0.15f), 4f + Mathf.PosMod(x.T, 2f) * 2f, HomeSky.WithAlpha(0.25f * x.Glow * (1f - Mathf.PosMod(x.T, 2f) / 2f)), 0.6f, 16); // 고향 소리 물결
    }

    private static void VrFine(in Fix x)
    {
        var ci = x.Ci;
        Dot(ci, x.P(0.24f, 0.15f), 0.6f, new Color("#5dff9a").WithAlpha(0.4f + 0.6f * x.Glow));
        Dot(ci, x.P(0.76f, 0.15f), 0.6f, HomeSky.WithAlpha(0.4f + 0.6f * x.Glow));
    }
}
