using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.7 부위 부상: 어디를 어떻게 다쳤는지. 팔을 다치면 손이 느리고, 다리를 다치면 걸음이 느리고, 폐를 다치면 숨이 차고,
// 머리를 다치면 판단이 흐리다. 아주 크게 상한 팔·다리는 잃는다 — 정비실에서 의수·의족을 만들어 달면 대부분 되찾는다.
// 상처의 크기는 Vitals.Injury(전체 부상)를 나눠 가진다: 부상이 나으면 상처들도 같은 비율로 아문다 (잃은 팔다리는 그대로).

public enum BodyPart { Head, Chest, Lungs, LeftArm, RightArm, LeftLeg, RightLeg }
public enum WoundKind { Cut, Burn, Fracture, Crush, Barotrauma, Toxic, Radiation }

public sealed class Wound
{
    public BodyPart Part { get; init; }
    public WoundKind Kind { get; init; }
    public float Weight { get; set; }        // 전체 부상 중 이 상처의 몫 (나눠 가진다)
    public string Cause { get; init; } = "";
    public bool Lost { get; set; }           // 팔·다리를 잃었다
    public bool Prosthetic { get; set; }     // 의수·의족을 달았다
}

public static class Wounds
{
    public static string PartName(BodyPart p) => p switch
    {
        BodyPart.Head => "머리", BodyPart.Chest => "가슴", BodyPart.Lungs => "폐",
        BodyPart.LeftArm => "왼팔", BodyPart.RightArm => "오른팔", BodyPart.LeftLeg => "왼다리", _ => "오른다리",
    };

    public static string KindName(WoundKind k) => k switch
    {
        WoundKind.Cut => "찢김", WoundKind.Burn => "화상", WoundKind.Fracture => "골절", WoundKind.Crush => "으스러짐",
        WoundKind.Barotrauma => "감압 손상", WoundKind.Toxic => "가스 흡입", _ => "방사선",
    };

    public static bool IsArm(BodyPart p) => p is BodyPart.LeftArm or BodyPart.RightArm;
    public static bool IsLeg(BodyPart p) => p is BodyPart.LeftLeg or BodyPart.RightLeg;

    private static int Stable(string s) { int h = 17; foreach (char ch in s) h = unchecked(h * 31 + ch); return h & 0x7fffffff; }

    /// <summary>원인 글에서 부위와 종류를 고른다 (같은 원인·같은 순간이면 같은 결과 — 결정론).</summary>
    public static (BodyPart part, WoundKind kind)? Classify(string cause, float amount, int salt)
    {
        int h = Math.Abs((Stable(cause) * 31 + salt * 7919 + (int)(amount * 1000)) % 1000); // string.GetHashCode는 실행마다 달라 결정론을 깬다
        BodyPart Limb() => (h % 4) switch { 0 => BodyPart.LeftArm, 1 => BodyPart.RightArm, 2 => BodyPart.LeftLeg, _ => BodyPart.RightLeg };
        bool Has(params string[] keys) => keys.Any(cause.Contains);
        if (Has("열병", "병", "식중독", "배탈")) return null; // 온몸 — 부위가 없다
        if (Has("감압")) return (BodyPart.Lungs, WoundKind.Barotrauma);
        if (Has("유독", "연기", "가스", "질식")) return (BodyPart.Lungs, WoundKind.Toxic);
        if (Has("방사선")) return (BodyPart.Chest, WoundKind.Radiation);
        if (Has("감전", "누전")) return (h % 2 == 0 ? BodyPart.RightArm : BodyPart.LeftArm, WoundKind.Burn);
        if (Has("화상", "불", "화재", "증기", "열")) return (h % 3 == 0 ? BodyPart.Chest : h % 3 == 1 ? BodyPart.RightArm : BodyPart.LeftArm, WoundKind.Burn);
        if (Has("폭발", "파편", "운석", "충돌")) return h % 7 == 0 ? (BodyPart.Head, WoundKind.Cut) : (Limb(), amount > 0.3f ? WoundKind.Fracture : WoundKind.Cut);
        if (Has("끼임", "짓눌", "무너", "구조물")) return (Limb(), WoundKind.Crush);
        if (Has("작업", "넘어", "떨어")) return (h % 2 == 0 ? Limb() : BodyPart.RightArm, amount > 0.2f ? WoundKind.Fracture : WoundKind.Cut);
        return (Limb(), WoundKind.Cut);
    }

    /// <summary>다쳤다: 부위별 상처를 더하고, 크게 상한 팔·다리는 잃는다.</summary>
    public static Wound? Add(Vitals v, float amount, string cause)
    {
        if (Classify(cause, amount, v.Wounds.Count) is not (BodyPart part, WoundKind kind)) return null;
        var w = v.Wounds.FirstOrDefault(x => x.Part == part && x.Kind == kind && !x.Lost);
        if (w == null) v.Wounds.Add(w = new Wound { Part = part, Kind = kind, Cause = cause });
        w.Weight += amount;
        // 한 번에 크게 (팔·다리를 으스러뜨리거나 크게 태운다) → 잃는다
        float lose = kind switch { WoundKind.Crush => 0.45f, WoundKind.Burn => 0.55f, WoundKind.Fracture => 0.6f, _ => 9f };
        if ((IsArm(part) || IsLeg(part)) && amount >= lose && !v.Wounds.Any(x => x.Part == part && x.Lost))
            w.Lost = true;
        return w;
    }

    /// <summary>지금 이 상처의 크기 0~1 (전체 부상을 몫대로 나눈다). 잃은 팔다리는 따로.</summary>
    public static float Severity(Vitals v, Wound w)
    {
        if (w.Lost) return w.Prosthetic ? 0.15f : 0.7f;
        float total = v.Wounds.Where(x => !x.Lost).Sum(x => x.Weight);
        if (total <= 0f || v.Injury <= 0f) return 0f;
        return MathF.Min(1f, v.Injury * w.Weight / total * MathF.Max(1f, v.Wounds.Count(x => !x.Lost)));
    }

    /// <summary>부상이 다 나으면 상처는 지운다 (잃은 팔다리는 남는다).</summary>
    public static void Tidy(Vitals v)
    {
        if (v.Injury <= 0.001f) v.Wounds.RemoveAll(x => !x.Lost);
    }

    private static float Worst(Vitals v, Func<BodyPart, bool> which) =>
        v.Wounds.Where(x => which(x.Part)).Select(x => Severity(v, x)).DefaultIfEmpty(0f).Max();

    /// <summary>손 (일 속도): 팔을 다치면 느리다. 오른팔이 조금 더.</summary>
    public static float HandFactor(Vitals v)
    {
        float r = Worst(v, p => p == BodyPart.RightArm), l = Worst(v, p => p == BodyPart.LeftArm);
        return Math.Clamp(1f - 0.45f * r - 0.3f * l, 0.35f, 1f);
    }

    /// <summary>걸음: 다리를 다치면 느리다.</summary>
    public static float LegFactor(Vitals v) => Math.Clamp(1f - 0.5f * Worst(v, IsLeg), 0.4f, 1f);

    /// <summary>숨: 폐를 다치면 쉽게 지친다 (기력이 더 빨리 빠진다).</summary>
    public static float LungLoad(Vitals v) => 1f + 0.8f * Worst(v, p => p == BodyPart.Lungs);

    /// <summary>머리: 판단이 흐려진다 (실수 확률에 곱한다).</summary>
    public static float HeadLoad(Vitals v) => 1f + 2f * Worst(v, p => p == BodyPart.Head);

    public static IEnumerable<Wound> Missing(Vitals v) => v.Wounds.Where(x => x.Lost && !x.Prosthetic);

    public static string Summary(Vitals v)
    {
        var parts = v.Wounds.Where(x => x.Lost || Severity(v, x) > 0.03f)
            .Select(x => x.Lost ? $"{PartName(x.Part)} {(x.Prosthetic ? (IsArm(x.Part) ? "의수" : "의족") : "잃음")}" : $"{PartName(x.Part)} {KindName(x.Kind)} {Severity(v, x) * 100:0}%");
        return string.Join(" · ", parts);
    }
}
