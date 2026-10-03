using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ShipSim.View;

/// <summary>
/// v17.7 프레임 시간 재기 (`--perf=프레임 수`): 그리기 층 · HUD · 이름표 · 시뮬레이션을 따로 잰다.
/// 꺼져 있으면 거의 공짜 (Now 가 0 을 돌려주고 Add 는 곧장 돌아간다).
/// </summary>
public static class FrameProbe
{
    public static bool On;
    private static readonly Dictionary<string, (long ticks, int calls)> Acc = new();

    public static long Now => On ? Stopwatch.GetTimestamp() : 0;

    public static void Add(string key, long start)
    {
        if (!On || start == 0) return;
        long t = Stopwatch.GetTimestamp() - start;
        var e = Acc.GetValueOrDefault(key);
        Acc[key] = (e.ticks + t, e.calls + 1);
    }

    public static void Reset() => Acc.Clear();

    /// <summary>(구간, 한 프레임 평균 ms, 부른 횟수) — 무거운 순.</summary>
    public static List<(string key, double msPerFrame, int calls)> Report(int frames) =>
        Acc.Select(kv => (kv.Key, kv.Value.ticks * 1000.0 / Stopwatch.Frequency / System.Math.Max(1, frames), kv.Value.calls))
            .OrderByDescending(x => x.Item2).ToList();
}
