using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 재료 흐름 진단 (--matprobe [--ships=Mirinae,Hanbit,Bodeum] 일수):
// 날마다 원료 · 기본 수리재 재고와 들어온 길(채집 장치 · 드론 채굴 · 고철 · 원정)을 적고,
// 고장이 부품을 기다린 날수 · 만들자고 올라온 부품 수를 함께 본다.
public static partial class Program
{
    private static int RunMatProbe(int days, int seed, string[] args)
    {
        var ships = args.FirstOrDefault(a => a.StartsWith("--ships="))?[8..].Split(',') ?? new[] { "Mirinae", "Hanbit", "Bodeum" };
        var raw = new[] { ItemKind.MetalOre, ItemKind.Carbon, ItemKind.Silicate, ItemKind.Ice, ItemKind.Rare };
        var basic = new[] { ItemKind.Plate, ItemKind.Cable, ItemKind.Structure, ItemKind.Electronics, ItemKind.Sealant, ItemKind.Fuse };
        string N(ItemKind k) => ItemKinds.Name(k) ?? k.ToString();
        foreach (var ship in ships)
        {
            var w = World.CreateDefault(seed, 0, ship);
            Console.WriteLine($"■ {ship} · 승무원 {w.Crew.Count(c => !c.Dead)} · 설비 {w.Ship.Furniture.Count(f => f.Machine != null)} · 채집 장치 {w.Ship.FurnitureOf(FurnitureType.Collector).Count()} · 정제기 {w.Ship.FurnitureOf(FurnitureType.Refinery).Count()} · 작업대 {w.Ship.FurnitureOf(FurnitureType.Workbench).Count()} · 채굴 드론 {w.Drones.Drones.Count(d => RobotsV15.Base(d.Kind) == DroneKind.Miner || d.Kind == DroneKind.Miner)}");
            Console.WriteLine($"  처음 재고  원료: {string.Join(" · ", raw.Select(k => $"{N(k)} {w.Ship.CountStored(k)}"))}");
            Console.WriteLine($"             수리재: {string.Join(" · ", basic.Select(k => $"{N(k)} {w.Ship.CountStored(k)}"))}");
            int col0 = 0, ore0 = 0, ice0 = 0, scr0 = 0;
            for (int d = 1; d <= days; d++)
            {
                Run(w, SimTime.TicksPerDay);
                int col = w.Collection.Collected, ore = w.Drones.V21.Ore, ice = w.Drones.V21.Ice, scr = w.Scrap.Recovered;
                var faults = w.Ship.Machines.Where(m => m.Faults.Count > 0).ToList();
                int waiting = w.Board.Open.Count(o => o.Kind == WorkKind.Repair && o.BlockedUntil > w.Tick);
                if (d == 1 || d % 5 == 0 || d == days)
                    Console.WriteLine($"  {d,3}일 · 들어옴: 채집 {col - col0} · 드론 금속 {ore - ore0} 얼음 {ice - ice0} · 고철 {scr - scr0}"
                        + $" | 원료 {string.Join(" ", raw.Select(k => $"{N(k)}{w.Ship.CountStored(k)}"))}"
                        + $" | 수리재 {string.Join(" ", basic.Select(k => $"{N(k)}{w.Ship.CountStored(k)}"))}"
                        + $" | 고장 {faults.Count} (막힘 {waiting})");
                if (d % 5 == 0) { col0 = col; ore0 = ore; ice0 = ice; scr0 = scr; }
            }
        }
        return 0;
    }
}
