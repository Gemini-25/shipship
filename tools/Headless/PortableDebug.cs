using System;
using System.Linq;
using ShipSim.Core;

public static partial class Program
{
    private static int RunPortableDebug(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess && PortableSystem.OutletOk(r)).OrderBy(r => r.Id).First();
        var heater = w.Portable.Devices.First(d => d.Kind == PortableKind.Heater);
        w.Portable.PlaceNow(heater, room.Cells.First(c => w.Ship.IsOpenFloor(c) && w.Ship.FurnitureAt(c) == null), null, "hold", outlet: room);
        long t0 = w.Tick;
        for (int i = 0; i < 30; i++)
        {
            Run(w, SimTime.Minutes(1));
            var near = string.Join(" ", w.Ship.LiveRooms.Where(r => w.Smells.Level(r, SmellKind.Burnt) > 0.01f).Select(r => $"{r.Name}{w.Smells.Level(r, SmellKind.Burnt):0.00}"));
            var sm = string.Join(" ", w.Crew.Where(c => w.Smells.Smelled(c, SmellKind.Burnt, SimTime.Minutes(20)) != null).Select(c => $"{c.Name}@{c.Room?.Name} {(c.IsAwake ? "" : "(z)")} job={c.Job?.Label} urg={c.Job?.Urgent} ev={string.Join("/", c.LastEvaluations.Take(3).Select(e => $"{e.Activity.Id}:{e.Score:0.00}"))} likely={w.Smells.Likely(c, SmellKind.Burnt)?.Name}"));
            Console.WriteLine($"{i + 1}m dust {heater.Dust:0.00} burnt[{near}] sniffers[{sm}]");
        }
        foreach (var e in w.Log.Entries.Where(e => e.Tick >= t0 && (e.Text.Contains("탄") || e.Text.Contains("히터"))).TakeLast(30)) Console.WriteLine($"  LOG {(e.Tick - t0) / 25f:0.0}m {e.CrewId} {e.Text}");
        return 0;
    }
}
