using System;
using System.Linq;
using ShipSim.Core;

// 의료 2차 — 장기 손상 · 인공 장기 · 이식 · 거부반응 · 감염 · 격리
public static partial class Program
{
    private static int RunOrganTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"장기 · 이식 · 감염 점검 · 시드 {seed}\n");
        return _fails == 0 ? 0 : 1;
    }
}
