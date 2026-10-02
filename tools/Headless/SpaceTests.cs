using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v17.4 공간과 협력 · 줄 서기 · 구경꾼
public static partial class Program
{
    private static int RunSpaceTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"공간과 협력 · 줄 서기 · 구경꾼 점검 (v17.4) · 시드 {seed}\n");
        return _fails == 0 ? 0 : 1;
    }
}
