using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.16 주컴퓨터 두뇌 2.0 그림: 계획(클립보드 · 체크 칸 · 고치면 연필) · 예측(내려가는 곡선 · 문턱 점선 · 확신 부채꼴) ·
/// 권한(방패 안 네 기둥 — 갈래마다 조언 · 제안 · 자동 세 칸) · 배움(갈래마다 다른 그림 — 계기 다이얼 · 사람 · 말풍선 둘 · 반창고 · 저울 · 열쇠 · 전구) ·
/// 부탁(톱니 쪽지) · 쉼(초승달 · z). 모두 읽기만 한다 (시뮬레이션을 바꾸지 않는다).
/// </summary>
public static class BrainIcons
{
    public static readonly Color Plan = new("#8fd6ff");
    public static readonly Color Fore = new("#9fd8ff");
    public static readonly Color Auth = new("#c9a0ff");
    public static readonly Color Learn = new("#ffe08a");
    public static readonly Color Ask = new("#5fd8ff");
    public static readonly Color Rest = new("#b9c6ff");

    public static Color ModeColor(string mode) => mode switch { "위기" => Palette.Danger, "대책" => Palette.Warning, "주의" => new Color("#f2c66d"), _ => Palette.Good };

    /// <summary>클립보드: 집게 · 체크 칸 셋(첫 칸이 가장 급한 계획 색) · 방금 고쳤으면 연필이 움직인다.</summary>
    public static void PlanBoard(CanvasItem ci, Vector2 c, float s, string mode, bool revised, float t)
    {
        var board = new Rect2(c - new Vector2(s * 0.75f, s * 0.9f), new Vector2(s * 1.5f, s * 1.9f));
        ci.Box(board, new Color("#1a2533"));
        ci.Box(board, Plan.WithAlpha(0.8f), false, 1f);
        var clip = new Rect2(c + new Vector2(-s * 0.35f, -s * 1.05f), new Vector2(s * 0.7f, s * 0.35f));
        ci.Box(clip, new Color("#5f6879"));
        for (int k = 0; k < 3; k++)
        {
            float y = c.Y - s * 0.45f + k * s * 0.5f;
            var box = new Rect2(new Vector2(c.X - s * 0.55f, y - s * 0.15f), new Vector2(s * 0.3f, s * 0.3f));
            var col = k == 0 ? ModeColor(mode) : Plan.WithAlpha(0.6f);
            ci.Box(box, col, k == 0 && mode != "유지", 1f);
            if (k > 0 || mode == "유지") ci.DrawLine(box.Position + new Vector2(1f, s * 0.15f), box.Position + new Vector2(s * 0.12f, s * 0.27f), col, 1f);
            ci.DrawLine(new Vector2(c.X - s * 0.15f, y), new Vector2(c.X + s * 0.6f, y), Plan.WithAlpha(0.5f), 1f);
        }
        if (revised)
        {
            float q = Mathf.Sin(t * 5f) * s * 0.15f;
            var tip = c + new Vector2(s * 0.5f + q, s * 0.55f);
            ci.DrawLine(tip, tip + new Vector2(s * 0.6f, -s * 0.6f), new Color("#f2c66d"), 2f, true);
            ci.Circle(tip, 1.2f, new Color("#e6eaf2"), true, -1f, true);
        }
    }

    /// <summary>예측: 지금에서 문턱(점선)으로 내려가는 곡선 · 확신이 낮을수록 넓은 부채꼴 · 예측 점이 맥박친다.</summary>
    public static void Forecast(CanvasItem ci, Vector2 c, float s, float days, float conf, float t)
    {
        var r = new Rect2(c - new Vector2(s, s * 0.8f), new Vector2(s * 2f, s * 1.6f));
        ci.Box(r, new Color("#0d1622"));
        float thrY = r.End.Y - s * 0.35f;
        for (float x = r.Position.X + 1f; x < r.End.X - 1f; x += 3f) ci.DrawLine(new Vector2(x, thrY), new Vector2(MathF.Min(x + 1.5f, r.End.X - 1f), thrY), Palette.Danger.WithAlpha(0.7f), 1f);
        var p0 = new Vector2(r.Position.X + 2f, r.Position.Y + s * 0.35f);
        bool flat = days >= 30f;
        float reach = flat ? 0f : Mathf.Clamp(1f - days / 8f, 0.1f, 1f);
        var p1 = new Vector2(r.End.X - 2f, Mathf.Lerp(p0.Y, thrY + s * 0.15f, reach));
        float spread = (1f - Mathf.Clamp(conf, 0f, 1f)) * s * 0.8f;
        if (!flat) ci.Poly(new[] { p0, p1 + new Vector2(0, -spread), p1 + new Vector2(0, spread) }, Fore.WithAlpha(0.16f));
        var mid = (p0 + p1) / 2f + new Vector2(0, flat ? 0f : -s * 0.15f);
        ci.Polyline(new[] { p0, mid, p1 }, flat ? Palette.Good : days <= 1.5f ? Palette.Danger : days <= 4f ? Palette.Warning : Fore, 1.4f, true);
        float pulse = 1.5f + 0.8f * (0.5f + 0.5f * Mathf.Sin(t * 4f));
        ci.Circle(p1, pulse, flat ? Palette.Good : Palette.Text, true, -1f, true);
    }

    /// <summary>권한: 방패 안 네 기둥 (자원 · 일정 · 정비 · 위기) — 조언 1칸 · 제안 2칸 · 자동 3칸. 자동이 있으면 테두리가 돈다.</summary>
    public static void Authority(CanvasItem ci, Vector2 c, float s, AuthLevel[] levels, float t)
    {
        var shield = new[] { c + new Vector2(0, -s), c + new Vector2(s * 0.9f, -s * 0.65f), c + new Vector2(s * 0.8f, s * 0.3f), c + new Vector2(0, s * 1.05f), c + new Vector2(-s * 0.8f, s * 0.3f), c + new Vector2(-s * 0.9f, -s * 0.65f) };
        ci.Poly(shield, new Color("#1a1428"));
        bool any = false;
        foreach (var l in levels) if (l == AuthLevel.Auto) any = true;
        ci.Polyline(new[] { shield[0], shield[1], shield[2], shield[3], shield[4], shield[5], shield[0] }, Auth.WithAlpha(any ? 0.65f + 0.35f * Mathf.Sin(t * 2.5f) : 0.6f), 1.2f, true);
        for (int i = 0; i < levels.Length; i++)
        {
            float x = c.X - s * 0.45f + i * s * 0.3f;
            for (int k = 0; k < 3; k++)
            {
                var seg = new Rect2(new Vector2(x - s * 0.1f, c.Y + s * 0.35f - k * s * 0.3f), new Vector2(s * 0.2f, s * 0.22f));
                bool on = (int)levels[i] >= k;
                ci.Box(seg, on ? (k == 2 ? Auth : k == 1 ? new Color("#f2c66d") : Palette.TextDim) : new Color(1, 1, 1, 0.07f));
            }
        }
    }

    /// <summary>배움 — 갈래마다 다른 그림. 방금 배웠으면 빛살이 깜빡인다.</summary>
    public static void Learned(CanvasItem ci, Vector2 c, float s, string kind, bool fresh, float t)
    {
        var col = Learn;
        switch (kind)
        {
            case "계기": // 다이얼 · 떨리는 바늘
                ci.Arc(c, s * 0.8f, Mathf.Pi, Mathf.Tau, 14, col, 1.4f, true);
                float ang = Mathf.Pi * 1.5f + 0.5f * Mathf.Sin(t * 6f);
                ci.DrawLine(c, c + Vector2.FromAngle(ang) * s * 0.7f, Palette.Danger, 1.3f, true);
                ci.Circle(c, 1.5f, col, true, -1f, true);
                break;
            case "사람": // 머리 · 어깨
                ci.Circle(c + new Vector2(0, -s * 0.35f), s * 0.32f, col, true, -1f, true);
                ci.Arc(c + new Vector2(0, s * 0.6f), s * 0.6f, Mathf.Pi, Mathf.Tau, 12, col, 1.6f, true);
                break;
            case "협상": // 말풍선 둘
                Gfx.RoundRect(ci, new Rect2(c + new Vector2(-s, -s * 0.8f), new Vector2(s * 1.2f, s * 0.8f)), col.WithAlpha(0.25f), 3, col);
                Gfx.RoundRect(ci, new Rect2(c + new Vector2(-s * 0.2f, -s * 0.1f), new Vector2(s * 1.2f, s * 0.8f)), Auth.WithAlpha(0.25f), 3, Auth);
                break;
            case "실수": // 반창고
            {
                var dir = Vector2.FromAngle(-0.6f);
                var nrm = new Vector2(-dir.Y, dir.X);
                var pts = new[] { c - dir * s - nrm * s * 0.3f, c + dir * s - nrm * s * 0.3f, c + dir * s + nrm * s * 0.3f, c - dir * s + nrm * s * 0.3f };
                ci.Poly(pts, new Color("#e8c9a0"));
                ci.Box(new Rect2(c - new Vector2(s * 0.25f, s * 0.25f), new Vector2(s * 0.5f, s * 0.5f)), new Color("#c9a37a"));
                for (int k = -1; k <= 1; k += 2) ci.Circle(c + dir * s * 0.65f * k, 0.8f, new Color("#8a6a4a"), true, -1f, true);
                break;
            }
            case "윤리": // 저울
            {
                float tilt = 0.15f * Mathf.Sin(t * 1.5f);
                ci.DrawLine(c + new Vector2(0, -s * 0.8f), c + new Vector2(0, s * 0.8f), col, 1.3f);
                var l = c + new Vector2(-s * 0.8f, -s * 0.5f + tilt * s);
                var r = c + new Vector2(s * 0.8f, -s * 0.5f - tilt * s);
                ci.DrawLine(l, r, col, 1.3f, true);
                ci.Arc(l + new Vector2(0, s * 0.35f), s * 0.3f, 0f, Mathf.Pi, 8, col, 1.2f, true);
                ci.Arc(r + new Vector2(0, s * 0.35f), s * 0.3f, 0f, Mathf.Pi, 8, col, 1.2f, true);
                break;
            }
            case "권한": // 열쇠
                ci.Arc(c + new Vector2(-s * 0.4f, 0), s * 0.35f, 0f, Mathf.Tau, 12, Auth, 1.5f, true);
                ci.DrawLine(c + new Vector2(-s * 0.05f, 0), c + new Vector2(s * 0.9f, 0), Auth, 1.5f);
                ci.DrawLine(c + new Vector2(s * 0.6f, 0), c + new Vector2(s * 0.6f, s * 0.35f), Auth, 1.5f);
                ci.DrawLine(c + new Vector2(s * 0.85f, 0), c + new Vector2(s * 0.85f, s * 0.3f), Auth, 1.5f);
                break;
            default: // 전구
                ci.Circle(c + new Vector2(0, -s * 0.2f), s * 0.55f, col.WithAlpha(0.3f), true, -1f, true);
                ci.Arc(c + new Vector2(0, -s * 0.2f), s * 0.55f, 0f, Mathf.Tau, 14, col, 1.2f, true);
                ci.Box(new Rect2(c + new Vector2(-s * 0.25f, s * 0.35f), new Vector2(s * 0.5f, s * 0.35f)), Palette.TextDim);
                break;
        }
        if (fresh)
            for (int k = 0; k < 5; k++)
            {
                float a = -Mathf.Pi / 2f + (k - 2) * 0.45f;
                float on = 0.5f + 0.5f * Mathf.Sin(t * 6f + k);
                ci.DrawLine(c + Vector2.FromAngle(a) * s * 1.05f, c + Vector2.FromAngle(a) * s * 1.4f, col.WithAlpha(on), 1f, true);
            }
    }

    /// <summary>작업 부탁: 톱니 달린 쪽지 (사람 머리 위).</summary>
    public static void AskChip(CanvasItem ci, Vector2 c, float s, float t)
    {
        Gfx.RoundRect(ci, new Rect2(c - new Vector2(s, s * 0.7f), new Vector2(s * 2f, s * 1.4f)), new Color(0.04f, 0.1f, 0.14f, 0.9f), 3, Ask);
        float rot = t * 1.5f;
        for (int k = 0; k < 6; k++)
        {
            var d = Vector2.FromAngle(rot + k * Mathf.Tau / 6f);
            ci.DrawLine(c + d * s * 0.3f, c + d * s * 0.55f, Ask, 1.6f);
        }
        ci.Arc(c, s * 0.3f, 0f, Mathf.Tau, 10, Ask, 1.2f, true);
    }

    /// <summary>쉬라는 부탁: 초승달 · 떠오르는 z.</summary>
    public static void RestChip(CanvasItem ci, Vector2 c, float s, float t)
    {
        ci.Circle(c, s * 0.7f, Rest, true, -1f, true);
        ci.Circle(c + new Vector2(s * 0.3f, -s * 0.2f), s * 0.6f, new Color("#0b1119"), true, -1f, true);
        float q = Mathf.PosMod(t * 0.7f, 1f);
        Gfx.Text(ci, Fonts.Bold, c + new Vector2(s * 0.6f, -s * 0.4f - q * s), "z", 8, Rest.WithAlpha(1f - q));
    }
}
