using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 의료 3차 로봇 그림 (위에서 내려다본 모양 · 가는 쪽이 +x).
/// 들것 로봇: 길고 낮은 몸 · 여섯 바퀴 · 위에 천 들것(띠 둘 · 앞머리 붉은 십자) · 양옆 난간 · 뒤쪽 붉은 · 흰 경광등 —
///   사람을 실으면 난간이 올라오고 경광등이 돈다 · 누를 때 앞에서 누름 판이 나와 맥박처럼 눌린다.
/// 간호 로봇: 둥근 흰 몸 · 둘레 서랍 여섯 · 가운데 푸른 십자 돔 · 앞에 작은 팔 —
///   지혈(거즈 판이 눌린다) · 피(붉은 팩이 매달려 흔들린다) · 키트(초록 상자) · 손 펌프(손잡이가 돈다) · 소독(보랏빛 자외선 둘레).
/// </summary>
public partial class ShipView
{
    private static readonly Color MbCanvas = new("#d9d2bf"), MbStrap = new("#3a3226"), MbCross = new("#e8383d"), MbWhite = new("#f1f4f7"),
        MbTeal = new("#2fb3a6"), MbBlue = new("#5ab8ff"), MbBag = new("#b3121c"), MbKit = new("#2f9a58"), MbUv = new("#b07cff");

    /// <summary>의료 로봇이면 그리고 true (아니면 원형 몸을 그리게 false).</summary>
    private bool PaintMedBot(CanvasItem ci, Robot r, Color body, Color col, bool moving, bool working, float t, bool dead)
    {
        if (r.Kind is not (RobotKind.Stretcher or RobotKind.Nurse)) return false;
        var task = _world.MedBots.TaskOf(r);
        var dim = dead ? new Color(0.55f, 0.55f, 0.58f) : Colors.White;
        if (r.Kind == RobotKind.Stretcher) PaintStretcher(ci, r, body, col, moving, working, t, task, dim);
        else PaintNurse(ci, r, body, col, working, t, task, dim);
        return true;
    }

    private void PaintStretcher(CanvasItem ci, Robot r, Color body, Color col, bool moving, bool working, float t, MedTask task, Color dim)
    {
        bool loaded = _world.MedBots.Carrying(r) != null;
        var wheel = new Color("#16191e");
        foreach (float x in new[] { -9f, -2f, 5f })
            foreach (float y in new[] { -8.2f, 6.4f })
            {
                ci.DrawRect(new Rect2(x, y, 3.6f, 1.8f), wheel);
                if (moving) ci.DrawLine(new Vector2(x + (t * 18f) % 3.6f, y), new Vector2(x + (t * 18f) % 3.6f, y + 1.8f), new Color("#3a3f48"), 0.8f);
            }
        Gfx.RoundRect(ci, new Rect2(-11f, -6.6f, 21f, 13.2f), body * dim, 3, col.Lightened(0.2f) * dim);
        // 들것 (천 · 띠 둘 · 머리 쪽 붉은 십자)
        var bed = new Rect2(-9.5f, -4.6f, 15.5f, 9.2f);
        ci.DrawRect(bed, MbCanvas * dim);
        ci.DrawRect(bed, MbCanvas.Darkened(0.35f) * dim, false, 0.8f);
        foreach (float x in new[] { -5.5f, 1f }) ci.DrawLine(new Vector2(x, -4.6f), new Vector2(x, 4.6f), (loaded ? MbStrap.Lightened(0.15f) : MbStrap) * dim, loaded ? 1.6f : 1f);
        ci.DrawLine(new Vector2(3.4f, -1.8f), new Vector2(3.4f, 1.8f), MbCross * dim, 1.4f);
        ci.DrawLine(new Vector2(1.6f, 0f), new Vector2(5.2f, 0f), MbCross * dim, 1.4f);
        // 난간 (실으면 올라온다 — 밝고 두껍게)
        var rail = (loaded ? new Color("#c8d0da") : new Color("#6b7380")) * dim;
        ci.DrawLine(new Vector2(-10f, -5.6f), new Vector2(6.5f, -5.6f), rail, loaded ? 1.4f : 0.8f);
        ci.DrawLine(new Vector2(-10f, 5.6f), new Vector2(6.5f, 5.6f), rail, loaded ? 1.4f : 0.8f);
        // 뒤쪽 경광등 (실었거나 달릴 때 돈다)
        bool spin = loaded || moving && task != MedTask.None;
        var bp = new Vector2(-9.5f, 0f);
        ci.DrawCircle(bp, 1.8f, (spin && Mathf.Sin(t * 9f) > 0f ? MbCross : Colors.White).WithAlpha(spin ? 0.95f : 0.4f) * dim, true, -1f, true);
        if (spin) ci.DrawLine(bp, bp + new Vector2(Mathf.Cos(t * 8f), Mathf.Sin(t * 8f)) * 6f, MbCross.WithAlpha(0.3f), 2f, true);
        // 누름 판 (앞으로 나와 눌린다)
        if (task == MedTask.Press && working)
        {
            float push = 1.5f + 1.2f * Mathf.Abs(Mathf.Sin(t * 2.2f));
            ci.DrawLine(new Vector2(8f, 0f), new Vector2(9.5f + push, 0f), new Color("#c8d0da") * dim, 1.4f, true);
            ci.DrawRect(new Rect2(9.5f + push, -2.2f, 1.6f, 4.4f), MbWhite * dim);
        }
    }

    private void PaintNurse(CanvasItem ci, Robot r, Color body, Color col, bool working, float t, MedTask task, Color dim)
    {
        // 자외선 소독 (일할 때 보랏빛 둘레)
        if (task == MedTask.Disinfect && working)
        {
            float a = 0.18f + 0.12f * Mathf.Sin(t * 6f);
            ci.DrawCircle(Vector2.Zero, 13f + 1.5f * Mathf.Sin(t * 3f), MbUv.WithAlpha(a), true, -1f, true);
        }
        // 바퀴 셋 (밑에 숨은 공 바퀴)
        foreach (float a in new[] { 0f, 2.1f, 4.2f }) ci.DrawCircle(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 7.6f, 1.4f, new Color("#16191e"), true, -1f, true);
        ci.DrawCircle(Vector2.Zero, 8.5f, MbWhite * dim, true, -1f, true);
        ci.DrawArc(Vector2.Zero, 8.5f, 0f, Mathf.Tau, 28, MbTeal * dim, 1.2f, true);
        // 서랍 여섯 (둘레)
        for (int k = 0; k < 6; k++)
        {
            float a0 = k * Mathf.Tau / 6f + 0.1f;
            ci.DrawArc(Vector2.Zero, 6.6f, a0, a0 + Mathf.Tau / 6f - 0.2f, 5, (k % 2 == 0 ? MbTeal.Darkened(0.2f) : MbTeal.Lightened(0.25f)) * dim, 1.6f, true);
        }
        // 가운데 돔 · 푸른 십자 (일할 때 숨 쉬듯)
        float glow = working ? 0.7f + 0.3f * Mathf.Sin(t * 4f) : 0.45f;
        ci.DrawCircle(Vector2.Zero, 3.6f, new Color("#1c2a36") * dim, true, -1f, true);
        ci.DrawLine(new Vector2(-2.2f, 0f), new Vector2(2.2f, 0f), MbBlue.WithAlpha(glow) * dim, 1.3f, true);
        ci.DrawLine(new Vector2(0f, -2.2f), new Vector2(0f, 2.2f), MbBlue.WithAlpha(glow) * dim, 1.3f, true);
        // 앞 팔 · 손에 든 것
        var sh = new Vector2(6f, 2.5f);
        switch (task)
        {
            case MedTask.Press:
            {
                float push = working ? 1.2f * Mathf.Abs(Mathf.Sin(t * 2.4f)) : 0f;
                var hand = new Vector2(11f + push, 1f);
                ci.DrawLine(sh, hand, new Color("#c8d0da") * dim, 1.4f, true);
                ci.DrawRect(new Rect2(hand.X, hand.Y - 2f, 2.2f, 4f), MbWhite * dim); // 거즈 판
                break;
            }
            case MedTask.Blood when _world.MedBots.Holding(r):
            {
                var hook = new Vector2(9.5f, -3f);
                ci.DrawLine(new Vector2(6f, -2f), hook, new Color("#c8d0da") * dim, 1.2f, true);
                float sw = 0.6f * Mathf.Sin(t * 3f);
                Gfx.RoundRect(ci, new Rect2(hook.X - 1.8f + sw, hook.Y + 0.4f, 3.6f, 4.6f), MbBag * dim, 1, MbBag.Darkened(0.4f) * dim);
                ci.DrawLine(new Vector2(hook.X + sw, hook.Y + 5f), new Vector2(hook.X + 1.5f + sw, hook.Y + 7.5f), MbBag.Darkened(0.2f) * dim, 0.6f, true); // 관
                break;
            }
            case MedTask.Kit when _world.MedBots.Holding(r) || r.Cargo is ItemStack { Kind: ItemKind.MedKit }:
            {
                ci.DrawRect(new Rect2(7f, -2.6f, 5f, 4.4f), MbKit * dim);
                ci.DrawLine(new Vector2(9.5f, -1.8f), new Vector2(9.5f, 1f), Colors.White * dim, 0.9f);
                ci.DrawLine(new Vector2(8.1f, -0.4f), new Vector2(10.9f, -0.4f), Colors.White * dim, 0.9f);
                break;
            }
            case MedTask.Crank:
            {
                var hub = new Vector2(10f, 0f);
                float a = working ? t * 6f : 0f;
                ci.DrawLine(sh, hub, new Color("#c8d0da") * dim, 1.4f, true);
                ci.DrawCircle(hub, 1.2f, new Color("#3d4757") * dim, true, -1f, true);
                ci.DrawLine(hub, hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.2f, new Color("#ffb347") * dim, 1.2f, true);
                break;
            }
            default:
                ci.DrawLine(sh, sh + new Vector2(2.5f, -1.5f), new Color("#9aa3ae") * dim, 1.2f, true); // 접은 팔
                break;
        }
    }
}
