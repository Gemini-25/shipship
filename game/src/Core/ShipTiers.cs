using System;
using System.Linq;

namespace ShipSim.Core;

// v23 크기별 기술 등급 (도안 — 함장 승인 전에는 꺼 둔다):
//   소형 · 기본형 (8명 이하) I · 중형 (9~14명) II · 대형 (15~24명) III · 초대형 · 세대선 (25명 넘음) IV.
//   큰 배일수록 더 좋은 기술을 싣고 떠난다 — 원자로 · 재배대 · 정수기 · 산소 발생기 · 배터리 · 냉각 펌프 · 작업대 · 정련기 · 수집기 …
//   각 설비는 그 설비가 가진 단계까지만 (재배대는 III이 끝). 높은 단계의 값(전기 더 먹음 · 더 닳음 · 더 자주 고장)도 그대로 따라온다.
public static class ShipTiers
{
    /// <summary>켜면 배를 만들 때 크기 등급만큼 설비 단계를 올린다 (기본 꺼짐 — 승인 뒤 켠다).</summary>
    public static bool On;

    public static int TierOf(int crew) => crew <= 8 ? 1 : crew <= 14 ? 2 : crew <= 24 ? 3 : 4;

    public static string Name(int tier) => tier switch { 1 => "I 기본 기술", 2 => "II 개량 기술", 3 => "III 고급 기술", _ => "IV 최신 기술" };

    /// <summary>배를 띄울 때 한 번: 기술 단계가 있는 설비를 크기 등급까지 올린다.</summary>
    public static int Apply(World w, int crew)
    {
        if (!On) return 0;
        int tier = TierOf(crew), raised = 0;
        if (tier <= 1) return 0;
        foreach (var f in w.Ship.Furniture)
        {
            if (f.Machine is not Machine m || !Tech.HasTree(f.Type)) continue;
            int t = Math.Min(tier, Tech.Tiers(f.Type).Count);
            if (t > m.Tier) { m.Tier = t; raised++; }
        }
        return raised;
    }
}
