using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 재료 되살리기 그림 (1종).
/// 탄소 회수기: 생명유지실 벽가의 두툼한 반응 기둥 — 위에서 걸러 낸 숨(이산화탄소)이 관으로 들어오고, 속의 전열 코일이 달아올라
/// 아래 받침판에 검은 탄소 가루가 쌓인다. 돌 때는 코일이 주황으로 달고 관 속으로 숨 방울이 흘러 들어간다 · 판이 차면 검게 쌓인다.
/// </summary>
public static partial class FixtureArt
{
    private static void MatArt(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.CarbonReclaimer] = new(ReclaimBody, ReclaimLife, ReclaimFine, Look.Heat, 0.5f, 0.35f);
    }

    private static readonly Color ReclaimShell = new("#4a5058");
    private static readonly Color ReclaimHot = new("#ff8a3a");
    private static readonly Color CarbonSoot = new("#1a1a1c");

    private static void ReclaimBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.18f, 0.04f, 0.82f, 0.78f), ReclaimShell, 4, ReclaimShell.Lightened(0.25f), 1); // 반응 기둥
        ci.Box(x.Q(0.3f, 0.16f, 0.7f, 0.62f), new Color("#20242a")); // 속 창
        Pipe(ci, x.P(0.0f, 0.22f), x.P(0.18f, 0.22f), 1.4f, Copper, false); // 들어오는 숨 관
        Line(ci, x.P(0.82f, 0.4f), x.P(0.98f, 0.4f), Steel4, 1.2f); // 빠지는 김 관
        ci.Box(x.Q(0.12f, 0.8f, 0.88f, 0.95f), Steel3); // 받침판 (탄소가 쌓이는 곳)
        Dot(ci, x.P(0.75f, 0.1f), 1.3f, new Color("#d8d0b0")); // 압력계
        Line(ci, x.P(0.24f, 0.78f), x.P(0.24f, 0.8f), Steel4, 1f); // 다리
        Line(ci, x.P(0.76f, 0.78f), x.P(0.76f, 0.8f), Steel4, 1f);
        if (x.Tier >= 2) ci.Box(x.Q(0.18f, 0.66f, 0.82f, 0.72f), new Color("#6a4a2a")); // II: 단열 띠
        if (x.Tier >= 3) Pipe(ci, x.P(0.5f, 0.04f), x.P(0.5f, 0.0f), 1.2f, Copper, false); // III: 수소 되돌림 관
    }

    private static void ReclaimLife(in Fix x)
    {
        var ci = x.Ci;
        float hot = x.Glow * (x.On ? 1f : 0.3f);
        // 속의 전열 코일: 돌면 주황으로 단다 (고장이면 깜빡)
        for (int k = 0; k < 5; k++)
        {
            float v = 0.2f + k * 0.09f;
            Line(ci, x.P(0.34f, v), x.P(0.66f, v + 0.04f), ReclaimHot.WithAlpha(0.15f + 0.75f * hot), 0.9f);
        }
        if (x.Spin > 0.05f && x.Lod > 0) // 관으로 흘러 들어가는 숨 방울
            for (int k = 0; k < 3; k++)
            {
                float u = Mathf.PosMod(x.T * 0.5f * x.Spin + k / 3f, 1f);
                Dot(ci, x.P(0.02f + 0.16f * u, 0.22f), 0.7f, new Color("#a8c8e8").WithAlpha(0.6f * (1f - u)));
            }
        // 받침판에 쌓인 탄소 (일할수록 · 효율만큼)
        float pile = 0.15f + 0.6f * Mathf.Clamp(x.Eff, 0f, 1f) * (x.On ? 1f : 0.4f);
        ci.Box(x.Q(0.3f, 0.95f - 0.12f * pile, 0.7f, 0.95f), CarbonSoot.WithAlpha(0.9f));
    }

    private static void ReclaimFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 4; k++) Dot(ci, x.P(0.2f + k * 0.2f, 0.06f), 0.45f, Steel4); // 뚜껑 볼트
        Line(ci, x.P(0.75f, 0.1f), x.P(0.75f + 0.04f * Mathf.Sin(x.T * 0.7f) * x.Glow, 0.07f), new Color("#c03030"), 0.5f); // 압력계 바늘
        Dot(ci, x.P(0.86f, 0.72f), 0.6f, (x.On ? new Color("#5dff9a") : new Color("#ff5a5a")).WithAlpha(0.4f + 0.6f * x.Glow)); // 가동 등
    }
}
