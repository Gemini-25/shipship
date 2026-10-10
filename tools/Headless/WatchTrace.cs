using System;
using System.Linq;
using ShipSim.Core;

public static partial class Program
{
    private static int RunHandoverTrace(int seed)
    {
        var w = DayOne(seed, "Mirinae");
        Tuning.AnomalyPerDay = 0f;
        w.Watch.NoSensors = true;
        CrewMember? a = null;
        for (int t = 0; t < 24 * 12 && a == null; t++)
        {
            float hr = SimTime.HourOfDay(w.Tick);
            a = w.Crew.FirstOrDefault(c => WatchLog.OnShift(c, w) && !SimTime.InWindow(SimTime.Wrap(hr + 0.5f), c.Schedule.WorkStart, c.Schedule.WorkLength));
            if (a == null) Run(w, SimTime.Minutes(5));
        }
        Console.WriteLine($"{SimTime.Clock(w.Tick)} {a!.Name} 근무 {a.Schedule.WorkStart}+{a.Schedule.WorkLength}");
        var targets = w.Ship.Machines.Where(m => m.Omen == null && m.Faults.Count == 0 && m.Spec.PowerDraw > 0f
                                                 && m.Spec.FaultKinds.Any(k => k != FaultKind.BreakerTrip && Prevention.KindOf(k) != null)).OrderBy(m => m.Body.Id).Take(4).ToList();
        foreach (var m in targets)
        {
            var fault = m.Spec.FaultKinds.First(k => k != FaultKind.BreakerTrip && Prevention.KindOf(k) != null);
            var kind = Prevention.KindOf(fault)!.Value;
            var o = new Omen { Kind = kind, Fault = fault, Cause = Causes.For(m.Body.Type, kind).First(), Since = w.Tick, Due = w.Tick + SimTime.Hours(14) };
            m.Omen = o; w.Precursors.Omens++;
            Prevention.Detect(w, m, o, "당직", a);
        }
        for (int h = 0; h < 16; h++)
        {
            Run(w, SimTime.Hours(1));
            var orders = w.Board.Open.Where(o => o.Kind == WorkKind.PreventiveCheck).Select(o => $"{o.Target} [{o.Detail}] {o.Assignee?.Name ?? "-"}{(o.BlockedReason != null && o.BlockedUntil > w.Tick ? " 막힘:" + o.BlockedReason : "")}");
            Console.WriteLine($"{SimTime.Clock(w.Tick)} 일감: {string.Join(" | ", orders)}");
        }
        foreach (var n in w.Watch.Notes) { Console.WriteLine($"#{n.Id} {n.Machine.Name} {n.Stage}"); foreach (var t in n.Trail) Console.WriteLine("   " + t); }
        Console.WriteLine($"{w.Watch.Stats} · 막음 {w.Precursors.Prevented} · 놓침 {w.Precursors.Missed}");
        foreach (var c in w.Crew) Console.WriteLine($"{c.Name} {c.Schedule.WorkStart}+{c.Schedule.WorkLength} 기계 {c.RawSkill(Skill.Mechanics):0.00} 전기 {c.RawSkill(Skill.Electrical):0.00}");
        return 0;
    }
}
