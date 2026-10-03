using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v17.7 성능 시험: 가장 큰 배(새터) 60명 · 30배속 · 복합 재난(운석 둘 + 화재 + 펌프 정지 · 배터리 바닥)에서
// 하루 시뮬 시간 · 30배속 프레임당 시뮬 시간 · 지문이 그대로인지(관찰 카메라 켬/끔).
public static partial class Program
{
    private static World PerfShip(int seed, bool disaster)
    {
        var w = World.CreateDefault(seed, 60, "Saeteo");
        for (int i = 0; i < SimTime.Hours(2); i++) w.Step();
        if (disaster) { Scenarios.Apply(w, "chaos", out _); Scenarios.Apply(w, "blackout", out _); }
        return w;
    }

    /// <summary>한 장면을 ticks 만큼 돌리며 틱별 시간을 잰다 → (전체 ms, 30배속 60fps 한 프레임(15틱) 평균 · 최악 ms, 지문).</summary>
    private static double LastAllocMb; private static int LastGc;
    private static (double total, double frameAvg, double frameWorst, uint hash, double cpu) PerfRun(World w, long ticks, Action<World>? each = null)
    {
        GC.Collect();
        var times = new double[ticks];
        double freq = Stopwatch.Frequency / 1000.0;
        long alloc0 = GC.GetTotalAllocatedBytes(); int gc0 = GC.CollectionCount(0);
        var cpu0 = Process.GetCurrentProcess().TotalProcessorTime; // 다른 일이 CPU를 나눠 쓸 때는 CPU 시간이 덜 흔들린다
        var sw = Stopwatch.StartNew();
        for (long i = 0; i < ticks; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            w.Step();
            each?.Invoke(w);
            times[i] = (Stopwatch.GetTimestamp() - t0) / freq;
        }
        sw.Stop();
        LastAllocMb = (GC.GetTotalAllocatedBytes() - alloc0) / 1048576.0; LastGc = GC.CollectionCount(0) - gc0;
        double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds;
        const int perFrame = 30 * SimTime.TicksPerSecond / 60; // 30배속 · 60fps → 한 프레임에 15틱
        double worst = 0;
        for (long i = 0; i + perFrame <= ticks; i += perFrame) { double s = 0; for (int k = 0; k < perFrame; k++) s += times[i + k]; worst = Math.Max(worst, s); }
        LastWorstTick = Array.IndexOf(times, times.Max());
        return (sw.Elapsed.TotalMilliseconds, times.Sum() / ticks * perFrame, worst, SaveGame.StateHash(w), cpu);
    }

    private static long LastWorstTick;

    /// <summary>가장 무거운 틱을 다시 만들어(결정론) 그 한 틱만 구간별로 잰다.</summary>
    private static void PerfSpike(int seed, bool disaster, long at)
    {
        var w = PerfShip(seed, disaster);
        for (long i = 0; i < at; i++) w.Step();
        Prof.Reset(); Prof.On = true;
        var sw = Stopwatch.StartNew();
        w.Step();
        Prof.On = false;
        Console.WriteLine($"    가장 무거운 틱 {at} ({SimTime.Day(w.Tick)}일 {SimTime.Clock(w.Tick)}) · {sw.Elapsed.TotalMilliseconds:0}ms");
        foreach (var (key, ms, calls, _) in Prof.Report().Take(12)) Console.WriteLine($"      {key,-30} {ms,8:0.0}ms · {calls,6}번");
    }

    private static int RunPerfTest(int seed)
    {
        _fails = 0;
        bool prof = Environment.GetEnvironmentVariable("PERF_PROF") == "1";
        Console.WriteLine($"성능 시험 (v17.7) · 시드 {seed} · 새터 60명");
        foreach (var (name, disaster) in new[] { ("평시", false), ("복합 재난", true) })
        {
            if (Environment.GetEnvironmentVariable("PERF_ONLY") is string only && only.Length > 0 && name != only) continue;
            var w = PerfShip(seed, disaster);
            if (prof) { Prof.Reset(); Prof.On = true; }
            var r = PerfRun(w, SimTime.TicksPerDay);
            Prof.On = false;
            Console.WriteLine($"  {name} · 하루 {r.total / 1000:0.00}초 · CPU {r.cpu / 1000:0.00}초 (30배속 실시간 하루 = {SimTime.TicksPerDay / (30.0 * SimTime.TicksPerSecond):0}초) · 30배속 프레임당 시뮬 평균 {r.frameAvg:0.00}ms · 최악 {r.frameWorst:0.0}ms · 지문 {r.hash:x8} · 생존 {w.Crew.Count(c => !c.Dead)}/{w.Crew.Count} · 할당 {LastAllocMb:0}MB · GC {LastGc}번 · 격자 {w.Ship.Grid.Width}×{w.Ship.Grid.Height} · 거리장 다시 씀 {w.Paths.FloodHits} / 새로 {w.Paths.FloodMisses}");
            if (prof) foreach (var (key, ms, calls, _) in Prof.Report().Take(40)) Console.WriteLine($"      {key,-30} {ms,8:0}ms · {calls,8}번");
            if (Environment.GetEnvironmentVariable("PERF_SPIKE") == "1") PerfSpike(seed, disaster, LastWorstTick);
            Check($"{name} · 30배속을 따라간다 (하루 시뮬 < 30배속 실시간 하루의 절반)", Math.Min(r.total, r.cpu) < SimTime.TicksPerDay / (30.0 * SimTime.TicksPerSecond) * 1000 * 0.5, $"{r.total:0}ms · CPU {r.cpu:0}ms");
        }
        if (Environment.GetEnvironmentVariable("PERF_ONLY") is { Length: > 0 }) return _fails == 0 ? 0 : 1;
        // 결정론: 같은 시드 · 같은 재난 → 같은 지문
        uint H() { var w = PerfShip(seed, true); for (int i = 0; i < SimTime.Hours(6); i++) w.Step(); return SaveGame.StateHash(w); }
        uint a = H(), b = H();
        Check("결정론 · 60명 복합 재난 6시간 지문 두 번 같다", a == b, $"{a:x8} / {b:x8}");
        Console.WriteLine($"  지문(60명 · 복합 재난 · 6시간) {a:x8}");
        return _fails == 0 ? 0 : 1;
    }
}
