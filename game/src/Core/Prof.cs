using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ShipSim.Core;

// v14.2 구간별 시간 재기 (켜 두지 않으면 거의 공짜 — `--profile` 벤치가 켠다).
// 시스템 갱신 · 승무원 판단 · 길찾기 · 일 진행을 따로 재서, 어디가 느린지 숫자로 본다.

public static class Prof
{
    public static bool On;
    private static readonly Dictionary<string, (long ticks, long calls, long bytes)> Totals = new();
    private static readonly Dictionary<long, long> AllocAt = new(); // 시작 시각 → 그때까지 할당한 바이트 (겹친 구간도 제 몫을 잰다)

    /// <summary>지금 시각 (꺼져 있으면 0).</summary>
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

    /// <summary>start부터 지금까지를 key에 더하고, 지금 시각을 돌려준다 (이어서 재기).</summary>
    public static long Lap(string key, long start)
    {
        if (!On) return 0;
        long now = Stopwatch.GetTimestamp();
        long alloc = System.GC.GetAllocatedBytesForCurrentThread();
        long from = AllocAt.Remove(start, out var a0) ? a0 : alloc;
        var e = Totals.GetValueOrDefault(key);
        Totals[key] = (e.ticks + now - start, e.calls + 1, e.bytes + alloc - from);
        if (AllocAt.Count > 4096) AllocAt.Clear();
        AllocAt[now] = alloc;
        return now;
    }

    public static void Reset() { Totals.Clear(); AllocAt.Clear(); }

    /// <summary>(구간, 밀리초, 호출 수, 할당 바이트) — 오래 걸린 순. (겹친 구간은 바깥 구간에도 들어간다)</summary>
    public static List<(string key, double ms, long calls, long bytes)> Report() =>
        Totals.Select(kv => (kv.Key, kv.Value.ticks * 1000.0 / Stopwatch.Frequency, kv.Value.calls, kv.Value.bytes)).OrderByDescending(x => x.Item2).ToList();
}
