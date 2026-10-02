using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.10 증축: 선체 바깥에 방을 새로 붙인다
public static partial class Program
{
    private static int RunAnnexTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"증축 점검 (v16.10) · 시드 {seed}\n");
        {
            var w = DayOne(seed, "Hanbit");
            Player.Scenario(w, "crowded", out _);
            long t0 = w.Tick;
            string last = "";
            while (w.Tick - t0 < SimTime.TicksPerDay * 5)
            {
                w.Step();
                var p = w.Annex.Plans.LastOrDefault();
                string now = p == null ? "-" : $"{p.State}/{p.Stage} F{p.Frame.Count(f => f >= 1f)}/{p.Frame.Length} P{p.Plate.Count(f => f >= 1f)} pr{p.Pressure:0.0} u{p.Utilities:0.0} s{p.Sheet:0.0} fit{p.Fit.Count(f => f >= 1f)} w{w.Annex.Working} halt{w.Annex.EvaHalted} q{w.Annex.QuietHours}";
                if (now != last && w.Tick % 300 == 0) { Console.WriteLine($"  {SimTime.Day(w.Tick)}일 {SimTime.Clock(w.Tick)} {now}"); last = now; }
                if (p?.State == "개통" && w.Tick - p.Opened > SimTime.Hours(12)) break;
            }
            foreach (var p in w.Annex.Plans) Console.WriteLine($"  안건: {p.Title} · {p.State} · {p.Advice}");
        }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
