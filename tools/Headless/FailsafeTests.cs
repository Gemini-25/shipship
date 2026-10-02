using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.19 페일세이프 설계 · 내구 재조정 · 중대형 사고
public static partial class Program
{
    private static int RunFailsafeTest(int seed)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.FirstOrDefault(a => a.StartsWith("--measure")) is string ms)
        {
            float days = ms.Contains('=') && float.TryParse(ms.Split('=')[1], out var dd) ? dd : 2f;
            int seeds = args.FirstOrDefault(a => a.StartsWith("--seeds=")) is string ss && int.TryParse(ss[8..], out var sn) ? sn : 2;
            string[]? only = args.FirstOrDefault(a => a.StartsWith("--ships=")) is string sh ? sh[8..].Split(',') : null;
            Durability.Legacy = args.Contains("--legacy");
            return RunFailsafeMeasure(seed, days, seeds, only);
        }
        _fails = 0;
        Console.WriteLine($"페일세이프 · 내구 · 중대형 사고 점검 (v16.19) · 시드 {seed}\n");
        Console.WriteLine(_fails == 0 ? "\n✔ 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
