using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v19 60프레임: 한 틱이 튀는 순간 — 틱마다 시간을 재어 가장 느린 틱들과 그 틱의 구간별 내역을 보인다.
//   시간 시드 --tickspikes [--ship=Hanbit] [--crew=0] [--cosmic=종류:시간] [--top=15]
public static partial class Program
{
    private static int RunTickSpikes(int hours, int seed, string[] args)
    {
        string ship = args.FirstOrDefault(a => a.StartsWith("--ship="))?.Split('=')[1] ?? "Hanbit";
        int crew = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--crew="))?.Split('=')[1], out var cn) ? cn : 0;
        int top = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--top="))?.Split('=')[1], out var tp) ? tp : 15;
        var w = World.CreateDefault(seed, crew, ship);
        for (int i = 0; i < SimTime.Hours(1); i++) w.Step();
        int warm = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--warm="))?.Split('=')[1], out var wm) ? wm : 0;
        for (long i = 0; i < SimTime.Hours(warm); i++) w.Step(); // 처음 도는 코드의 컴파일(JIT)을 먼저 치른다
        if (args.FirstOrDefault(a => a.StartsWith("--cosmic="))?.Split('=')[1] is string cs)
        {
            var bits = cs.Split(':');
            w.Cosmic.Force(Enum.Parse<CosmicKind>(bits[0]), bits.Length > 1 ? float.Parse(bits[1]) : 0.5f);
        }
        Prof.On = true;
        var spikes = new List<(double ms, long tick, string why)>();
        var all = new List<double>();
        long end = w.Tick + SimTime.Hours(hours);
        while (w.Tick < end)
        {
            Prof.Reset();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            var p0 = GC.GetTotalPauseDuration();
            long t0 = Stopwatch.GetTimestamp();
            w.Step();
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            string gc = GC.CollectionCount(2) > g2 ? "GC2 " : GC.CollectionCount(1) > g1 ? "GC1 " : GC.CollectionCount(0) > g0 ? "GC0 " : "";
            double pause = (GC.GetTotalPauseDuration() - p0).TotalMilliseconds;
            if (gc.Length > 0) gc += $"{pause:0.0}ms · ";
            all.Add(ms);
            if (spikes.Count < top || ms > spikes[^1].ms)
            {
                string why = gc + string.Join(" · ", Prof.Report().Where(r => !r.key.StartsWith("step") || r.key.Contains('.')).Take(6).Select(r => $"{r.key} {r.ms:0.0}"));
                spikes.Add((ms, w.Tick, why));
                spikes.Sort((a, b) => b.ms.CompareTo(a.ms));
                if (spikes.Count > top) spikes.RemoveAt(spikes.Count - 1);
            }
        }
        Prof.On = false;
        all.Sort();
        Console.WriteLine($"GC: 0세대 {GC.CollectionCount(0)} · 1세대 {GC.CollectionCount(1)} · 2세대 {GC.CollectionCount(2)} · 멈춘 시간 합 {GC.GetTotalPauseDuration().TotalMilliseconds:0}ms · 할당 {GC.GetTotalAllocatedBytes() / 1e9:0.0}GB · 힙 {GC.GetTotalMemory(false) / 1e6:0}MB");
        Console.WriteLine($"틱 튐 · {ShipCatalog.Find(ship)?.Name ?? ship} · {w.Crew.Count(c => !c.Dead)}명 · {hours}시간 · 틱 {all.Count} · 평균 {all.Average():0.000}ms · p99 {all[(int)(all.Count * 0.99)]:0.00}ms · p999 {all[(int)(all.Count * 0.999)]:0.00}ms · 최대 {all[^1]:0.0}ms · 5ms 넘은 틱 {all.Count(x => x > 5)}");
        foreach (var (ms, tick, why) in spikes)
            Console.WriteLine($"  {ms,6:0.0}ms  {SimTime.Clock(tick)}  {why}");
        return 0;
    }
}
