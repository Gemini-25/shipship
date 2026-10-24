using ShipEngine;

namespace Lab;

/// <summary>물체 법칙 (docs/PHYSICS.md §10): 보는 쪽 한 벌 · (PH1b부터) 도트 꼴.</summary>
public static partial class Program
{
    private static void PropLaws()
    {
        if (!Do("물체")) return;
        Console.WriteLine("\n[물체 — 보는 쪽 · 도트 꼴]");
        FacingLaw();
    }

    /// <summary>
    /// 보는 쪽 한 벌 (PH1a): 가구가 보는 쪽은 도면에서 한 번 (`Layout.Facing`) — 그림이 셈하던 때 (Painter `FacingOfBox`)와 일곱 척 가구마다 같다.
    /// 값은 옮기기 전 코드로 잰 S/N/E/W 문자열의 FNV-1a 해시.
    /// </summary>
    private static void FacingLaw()
    {
        var was = new Dictionary<string, ulong>
        {
            ["didim"] = 0xeedd48741bc2bb93UL, ["hannuri"] = 0x7eaa18f4db702dffUL, ["hwasal"] = 0x7957fe64ce798e38UL, ["nareum"] = 0x29289543d328937bUL,
            ["saetteul"] = 0x13d64cffb51b55c6UL, ["salpim"] = 0x7f4072b4d2ba2241UL, ["soedol"] = 0x7647af24efa023eeUL,
        };
        var bad = new List<string>(); var got = new List<string>();
        foreach (var key in Layout.ShipKeys().OrderBy(k => k, StringComparer.Ordinal))
        {
            var L = Layout.Ship(key, Tables.Default);
            ulong h = 1469598103934665603UL;
            foreach (var f in L.Facing) { h ^= "SNEW"[(int)f]; h *= 1099511628211UL; }
            got.Add(key);
            if (L.Facing.Length != L.Furniture.Count || !was.TryGetValue(key, out var w) || w != h) bad.Add($"{key} {h:x16}");
        }
        Check("보는 쪽 한 벌 — 도면에서 한 번 셈한 가구의 보는 쪽이 그림이 셈하던 것과 일곱 척 모두 같다", bad.Count == 0 && got.Count == 7,
            bad.Count == 0 ? $"{got.Count}척 같음" : "다른 배: " + string.Join(" · ", bad));
    }
}
