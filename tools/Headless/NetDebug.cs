using System;
using System.Linq;
using ShipSim.Core;
public static partial class Program
{
    private static int RunNetDebug(int seed)
    {
        var w = DayOne(seed, "Mirinae");
        var power = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Room;
        var link = w.Net.Links.Where(l => l.Kind == NetKind.Power && (l.Door?.RoomA == power || l.Door?.RoomB == power)).OrderBy(l => l.Id).First();
        w.Net.Hurt(link, 1f, "시험");
        int last = -1;
        for (int i = 0; i < 400; i++)
        {
            var before = w.Ship.Rooms.ToDictionary(r => r.Id, r => r.PowerLinked);
            w.Step();
            var flips = w.Ship.Rooms.Where(r => before[r.Id] != r.PowerLinked).Select(r => $"{r.Name}{(r.PowerLinked ? "+" : "-")}").ToList();
            if (flips.Count > 0 && i < 60) Console.WriteLine($"  tick {i}: {string.Join(" ", flips)}");
            if (w.Net.Stats.Blackouts != last) { last = w.Net.Stats.Blackouts; Console.WriteLine($"{SimTime.Clock(w.Tick)} tick {i} blackouts {last} dark {w.Ship.LiveRooms.Count(r => !r.PowerLinked)} cut {link.Cut} int {link.Integrity:0.00}"); }
        }
        return 0;
    }
}
