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
    private static readonly Dictionary<string, (long ticks, int calls, long max, long bytes)> Acc = new();
    private static readonly Dictionary<long, long> AllocAt = new();

    public static long Now
    {
        get
        {
            if (!On) return 0;
            long t = Stopwatch.GetTimestamp();
            if (AllocAt.Count > 4096) AllocAt.Clear();
            AllocAt[t] = System.GC.GetAllocatedBytesForCurrentThread();
            return t;
        }
    }

    public static void Add(string key, long start)
    {
        if (!On || start == 0) return;
        long t = Stopwatch.GetTimestamp() - start;
        if (!key.StartsWith("hud.")) _frame += t;
        _thisFrame[key] = _thisFrame.GetValueOrDefault(key) + t;
        long bytes = AllocAt.Remove(start, out var a0) ? System.GC.GetAllocatedBytesForCurrentThread() - a0 : 0;
        var e = Acc.GetValueOrDefault(key);
        Acc[key] = (e.ticks + t, e.calls + 1, System.Math.Max(e.max, t), e.bytes + bytes);
    }

    /// <summary>v19 한 프레임에 우리 코드가 쓴 CPU (층 · HUD · 이름표 · 시뮬레이션 합 — HUD 안쪽 구간은 빼고).</summary>
    private static long _frame;
    public static readonly List<double> FrameMs = new();
    private static readonly Dictionary<string, long> _thisFrame = new();
    /// <summary>12ms를 넘은 프레임의 무거운 구간 (최대 60개).</summary>
    public static readonly List<string> SlowFrames = new();
    public static void EndFrame()
    {
        if (On)
        {
            double ms = _frame * 1000.0 / Stopwatch.Frequency;
            FrameMs.Add(ms);
            if (ms > 12 && SlowFrames.Count < 60)
                SlowFrames.Add($"{ms:0.0}ms: " + string.Join(" · ", _thisFrame.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} {kv.Value * 1000.0 / Stopwatch.Frequency:0.0}")));
        }
        _frame = 0;
        _thisFrame.Clear();
    }

    public static void Reset() { Acc.Clear(); AllocAt.Clear(); FrameMs.Clear(); SlowFrames.Clear(); _thisFrame.Clear(); _frame = 0; }

    /// <summary>(구간, 한 프레임 평균 ms, 부른 횟수, 한 번 가장 길었던 ms) — 무거운 순.</summary>
    public static List<(string key, double msPerFrame, int calls, double maxMs, double kbPerFrame)> Report(int frames) =>
        Acc.Select(kv => (kv.Key, kv.Value.ticks * 1000.0 / Stopwatch.Frequency / System.Math.Max(1, frames), kv.Value.calls, kv.Value.max * 1000.0 / Stopwatch.Frequency, kv.Value.bytes / 1024.0 / System.Math.Max(1, frames)))
            .OrderByDescending(x => x.Item2).ToList();
}
