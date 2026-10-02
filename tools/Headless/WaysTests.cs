using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;


public static partial class Program
{
    private static int RunWaysTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"여러 갈래 해법 점검 (v16.25) · 시드 {seed}\n");
        var bad = WaysTable.Audit();
        Check("표: 문제 20종마다 5갈래 이상 · 이름 · 그림", bad.Count == 0, string.Join(", ", bad.Take(6)));
        return _fails;
    }
}
