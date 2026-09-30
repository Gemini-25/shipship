using System;
using System.Linq;
using ShipSim.Core;
public static partial class Program
{
    private static int RunMoistureDebug(int seed)
    {
        var w = DayOne(seed, "Mirinae");
        foreach (var comp in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(comp.Machine!, FaultKind.Wrecked);
        var room = w.Ship.RoomsOf(RoomType.Quarters).First();
        w.Moisture.AddWater(room, room.Cells.Count * 20f * 0.5f);
        for (int i = 0; i < 12; i++)
        {
            Run(w, SimTime.Minutes(5));
            Console.WriteLine($"{SimTime.Clock(w.Tick)} depth {MoistureSystem.Depth(room):0.00} powered {room.Powered} breaker {room.BreakerOff} noticed {w.Moisture.Noticed(room)} online {w.Automation.MainOnline} live {w.Ship.LiveRooms.Contains(room)} · {w.Moisture.Stats}");
        }
        return 0;
    }
}
