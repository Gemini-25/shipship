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
            int col0 = 0, ore0 = 0, ice0 = 0, scr0 = 0, ref0 = 0;
            bool fab = args.Contains("--fab");
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
                if (d % 5 == 0 || d == days)
                    Console.WriteLine($"        재료 관리: 채집 장치 {w.Ship.FurnitureOf(FurnitureType.Collector).Count()}대 · {w.Mats.Brief()} · 급함 {w.Mats.Urgent()?.ToString() ?? "-"} | {w.Mats.Stats.Line()} | 찌꺼기 {w.Scrap.Organic:0.0} · 고철 {w.Scrap.Scrap:0.0} · 정제 {w.Adapt.Refined}");
                if (fab && (d == 1 || d % 5 == 0 || d == days))
                {
                    // 정제기 · 작업대에 올라온 일: 무엇을 · 누가 · 막힌 까닭
                    Console.WriteLine($"        정제 {w.Adapt.Refined - ref0} · 정제기 {string.Join(" ", w.Ship.FurnitureOf(FurnitureType.Refinery).Select(f => $"[{f.Room.Name} 효율{f.Machine!.Efficiency:0.00}{(f.Machine.Faults.Count > 0 ? " 고장" : "")}]"))}");
                    foreach (var o in w.Board.Open.Where(o => o.Kind is WorkKind.Fabricate or WorkKind.MeltIce).OrderByDescending(o => o.Urgency).Take(8))
                        Console.WriteLine($"        · {o.Kind} {o.Product} {o.Urgency:0.00} @{o.Target.Furniture?.Type} {(o.Assignee?.Name ?? o.Robot?.Name ?? "-")} {(o.BlockedUntil > w.Tick ? "막힘:" + o.BlockedReason : "")} {o.Detail} · 올린 지 {(w.Tick - o.Posted) / (float)SimTime.TicksPerHour:0.0}h"
                            + (o.Robot is Robot rb ? $" · 로봇 {rb.State} {rb.Doing} 짐 {rb.Cargo?.Kind}×{rb.Cargo?.Count} 전지 {rb.Battery:0.00} 고장 {rb.Fault} @{rb.Room?.Name} 진척 {o.Progress:0.00}" : ""));
                }
                if (d % 5 == 0) { col0 = col; ore0 = ore; ice0 = ice; scr0 = scr; ref0 = w.Adapt.Refined; }
            }
        }
        return 0;
    }
}
