using System;
using System.Linq;
using ShipSim.Core;

// v18.15 가치관 · 딜레마 · 늦게 돌아오는 결과
public static partial class Program
{
    private static int RunValueTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"가치관 · 딜레마 · 결정 장부 점검 (v18.15) · 시드 {seed}\n");
        Check("딜레마 표 20종 이상", DilemmaTable.All.Length >= 20, $"{DilemmaTable.All.Length}종");
        return _fails == 0 ? 0 : 1;
    }
}
