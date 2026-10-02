using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v14.2 성능 측정: 평시(작은 배 ~ 초대형) · 큰 배 재난 직후 · 긴 기록 — 틱당 시간의 느린 구간(p99·최대) · 구간별 시간 · 할당 · GC.
public static partial class Program
{
    private static int RunProfile(int seed, string[] args)
    {
        Console.WriteLine($"성능 측정 (v14.2) · 시드 {seed}\n");
        bool quick = args.Contains("--quick");
        Measure("평시 · 미리내 6명 · 하루", () => World.CreateDefault(seed, 0, "Mirinae"), null, SimTime.TicksPerDay);
        Measure("평시 · 은하 20명 · 하루", () => World.CreateDefault(seed, 0, "Eunha"), null, SimTime.TicksPerDay);
        if (!quick) Measure("평시 · 천마 30명 · 하루", () => World.CreateDefault(seed, 0, "Cheonma"), null, SimTime.TicksPerDay);
        Measure("재난 · 은하 20명 · 운석+화재+정전 직후 3시간", () =>
        {
            var w = World.CreateDefault(seed, 0, "Eunha");
            for (int i = 0; i < SimTime.Hours(3); i++) w.Step();
            return w;
        }, w =>
        {
            Scenarios.Apply(w, "bigmeteor", out _);
            Scenarios.Apply(w, "fire", out _);
            Scenarios.Apply(w, "blackout", out _);
        }, SimTime.Hours(3));
        if (!quick) Measure("긴 기록 · 한빛 12명 · 15일 (첫날 ↔ 마지막 날)", () => World.CreateDefault(seed, 0, "Hanbit"), null, SimTime.TicksPerDay * 15, daily: true);
        return 0;
    }

    private static void Measure(string name, Func<World> make, Action<World>? hit, long ticks, bool daily = false)
    {
        var w = make();
        hit?.Invoke(w);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long alloc0 = GC.GetTotalAllocatedBytes(), g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var pause0 = GC.GetTotalPauseDuration();
        Prof.Reset();
        Prof.On = true;
        var times = new double[ticks];
        var dayMs = new List<double>();
        double freq = Stopwatch.Frequency / 1000.0;
        var total = Stopwatch.StartNew();
        double acc = 0;
        for (long i = 0; i < ticks; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            w.Step();
            double ms = (Stopwatch.GetTimestamp() - t0) / freq;
            times[i] = ms;
            acc += ms;
            if (daily && (i + 1) % SimTime.TicksPerDay == 0) { dayMs.Add(acc); acc = 0; }
        }
        total.Stop();
        Prof.On = false;
        long alloc = GC.GetTotalAllocatedBytes() - alloc0;
        var pause = GC.GetTotalPauseDuration() - pause0;
        var sorted = times.OrderBy(x => x).ToArray();
        double P(double q) => sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        // 1분(게임) 단위로 묶은 느린 구간 — 화면에서 끊김으로 느껴지는 곳
        int perMin = SimTime.Minutes(1);
        var minutes = Enumerable.Range(0, (int)(ticks / perMin)).Select(m => times.Skip(m * perMin).Take(perMin).Sum()).ToArray();
        int worstMin = Array.IndexOf(minutes, minutes.DefaultIfEmpty(0).Max());
        double hours = ticks / (double)SimTime.TicksPerHour;
        Console.WriteLine($"■ {name}");
        Console.WriteLine($"  전체 {total.ElapsedMilliseconds}ms · 게임 한 시간에 {total.ElapsedMilliseconds / hours:0}ms · {ticks / total.Elapsed.TotalSeconds:N0} 틱/초 · 1배속 여유 ×{(SimTime.TicksPerHour / 3600.0 * hours * 1000) / Math.Max(1, total.ElapsedMilliseconds) * 3600:0}");
        Console.WriteLine($"  틱: 중앙 {P(0.5):0.000}ms · p99 {P(0.99):0.000}ms · p99.9 {P(0.999):0.000}ms · 최대 {sorted[^1]:0.00}ms · 가장 무거운 게임 1분 {minutes.DefaultIfEmpty(0).Max():0}ms ({worstMin}분째)");
        Console.WriteLine($"  할당 {alloc / 1024.0 / 1024.0:0.0}MB (틱당 {alloc / (double)ticks / 1024.0:0.0}KB) · GC {GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2} (0/1/2세대) · 멈춤 {pause.TotalMilliseconds:0}ms");
        if (dayMs.Count > 1) Console.WriteLine($"  날마다: {string.Join(" · ", dayMs.Select((d, i) => $"{i + 1}일 {d / 1000:0.0}s"))}");
        double sum = total.Elapsed.TotalMilliseconds;
        foreach (var (key, ms, calls, bytes) in Prof.Report().Take(int.TryParse(Environment.GetEnvironmentVariable("PROF_TOP"), out var top) ? top : 24))
            Console.WriteLine($"    {key,-28} {ms,8:0}ms {100 * ms / sum,5:0.0}% · {calls,8}번 · {1000 * ms / Math.Max(1, calls),7:0.0}µs/번 · 할당 {bytes / 1048576.0,7:0.0}MB");
        Console.WriteLine($"    (할당 많은 순) " + string.Join(" · ", Prof.Report().OrderByDescending(x => x.bytes).Take(8).Select(x => $"{x.key} {x.bytes / 1048576.0:0}MB")));
        Console.WriteLine($"    거리장 캐시: 다시 씀 {w.Paths.FloodHits} · 새로 계산 {w.Paths.FloodMisses}");
        Console.WriteLine();
    }
}
