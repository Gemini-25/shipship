using System;
using System.Linq;

namespace ShipSim.Core;

// v23 크기별 시작 시대 (함장 승인):
//   소형 · 기본형 (8명 이하) Ⅰ 핵분열 출항 · 중형 (9~14명) Ⅱ 폐쇄 순환 · 대형 (15~24명) Ⅲ 핵융합 · 초대형 · 세대선 (25명 넘음) Ⅳ 나노 조립.
//   큰 배일수록 앞선 시대에서 떠난다 — 그 앞 시대의 기술을 이미 알고, 설비도 그 단계로 지어져 있다 (EraSystem.StartAt).
//   높은 단계의 대가(전기 · 닳음 · 고장)는 약하게 따라온다 (Tech.Mild).
public static class ShipTiers
{
    /// <summary>끄면 모든 배가 Ⅰ 시대 · I 단계 설비로 떠난다 (시험 · SHIPSIM_OFF=tiers).</summary>
    public static bool On = true;

    public static int EraOf(int crew) => crew <= 8 ? 1 : crew <= 14 ? 2 : crew <= 24 ? 3 : 4;
    public static int TierOf(int crew) => EraOf(crew);

    public static string Name(int era) => $"{TechTree.Roman(era)} {TechTree.EraName(era)}";

    /// <summary>배를 띄울 때 한 번: 크기만큼 앞선 시대에서 시작하고, 설비를 아는 단계까지 지어 둔다. 올린 설비 수.</summary>
    public static int Apply(World w, int crew)
    {
        if (!On) return 0;
        int era = EraOf(crew), raised = 0;
        if (era <= 1) return 0;
        w.Eras.StartAt(era);
        foreach (var f in w.Ship.Furniture)
        {
            if (f.Machine is not Machine m || !Tech.HasTree(f.Type)) continue;
            int t = Tech.Unlocked(w, f.Type);
            if (t > m.Tier) { m.Tier = t; raised++; }
        }
        // 배터리 용량이 커졌으니 처음 채워 둔 양도 그만큼 (늘 80%로 떠난다 — 그대로 두면 32%로 시작한 셈이었다)
        float cap = w.Ship.FurnitureOf(FurnitureType.Battery).Sum(PowerGrid.BatteryKwh);
        if (cap > 0f) w.Power.BatteryCharge = cap * 0.8f;
        return raised;
    }
}
