using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 임시 진단 (커밋 전에 지운다)
public static partial class Program
{
    private static int F1sPower(int seed, string key, int minutes)
    {
        var w = World.CreateDefault(seed, 0, key);
        var p = w.Power;
        var pipes = w.Piping;
        Console.WriteLine($"{w.Ship.Name} 펌프 {w.Ship.FurnitureOf(FurnitureType.CoolantPump).Count()} · 분기 {pipes.Branches.Count} · 원자로 {w.Ship.FurnitureOf(FurnitureType.ReactorCore).Count()} 등급 {string.Join(",", w.Ship.FurnitureOf(FurnitureType.ReactorCore).Select(f => f.Machine!.Rating))}");
        foreach (var b in pipes.Branches) Console.WriteLine($"  분기 {b.Name} 펌프 {b.Pump?.Label} 흐름 {b.Flow} 방열판 {b.RadiatorCondition:0.00}");
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) Console.WriteLine($"  펌프 {f.Label} 방 {f.Room.Name} 효율 {f.Machine!.Efficiency:0.00} 등급 {f.Machine.Rating} 마모 {f.Machine.Wear:0.00} 고장 {string.Join(",", f.Machine.Faults.Select(x => x.Name))} 전기 {f.Machine.Powered} 버려짐 {f.Room.Abandoned}");
        Run(w, SimTime.Minutes(5));
        foreach (var r in w.Ship.Rooms) { var sh = w.Flow.Share(NetKind.Water, r); var sp = w.Flow.Share(NetKind.Power, r); Console.WriteLine($"  전기 {r.Name} {sp.Frac:0.00} {sp.Why} 문 {sp.Doors} 제한 {sp.Limit?.Key}"); Console.WriteLine($"  물 {r.Name} {sh.Frac:0.00} {sh.Why} 필요 {UtilityNet.NeedsWater(r)}"); }
        for (int i = 0; i < minutes / 5; i++)
        {
            Run(w, SimTime.Minutes(5));
            Console.WriteLine($"{SimTime.HourOfDay(w.Tick):0.00}시 원자로 {p.ReactorOnline} 온도 {p.ReactorTemperature:0} 출력 {p.ReactorOutput:0} 한도 {p.ReactorLimit:0} 냉각 {p.CoolingCapacity:0} 수요 {p.Demand:0} 배터리 {p.BatteryPercent * 100:0}% 냉각수 {pipes.CoolantFraction:0.00} 흐름 {pipes.FlowingBranches} · 컴퓨터 {w.Automation.MainOnline} 방온도 {w.Automation.Computer?.Body.Room.Air.Temperature:0} · 펌프 " + string.Join(" ", w.Ship.FurnitureOf(FurnitureType.CoolantPump).Select(f => $"{f.Machine!.Efficiency:0.0}{(f.Machine.Powered ? "" : "x")}/L{f.Machine.Line:0.0}/W{f.Room.WaterFlow:0.00}/P{f.Room.PowerFlow:0.00}/H{f.Machine.Heat:0.0}/C{f.Machine.Condition:0.0}/F{f.Machine.Fouled:0.0}")));
        }
        return 0;
    }
}
