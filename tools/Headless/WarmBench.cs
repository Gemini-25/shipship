using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using ShipSim.Core;

// 시험 시간 재기 (--warmbench): 첫날 배 만들기 · 하루 돌리기 · 상태 찍기 · 되살리기가 각각 얼마나 걸리고,
// 되살린 배가 원래 배와 똑같이 이어 가는지 본다.
public static partial class Program
{
    private static int RunWarmBench(int seed, string[] args)
    {
        var ships = args.FirstOrDefault(a => a.StartsWith("--ships="))?[8..].Split(',') ?? new[] { "Hanbit", "Mirinae" };
        Console.WriteLine($"시험 시간 재기 · 시드 {seed}");
        foreach (var ship in ships)
        {
            var sw = Stopwatch.StartNew();
            var w = World.CreateDefault(seed, 0, ship);
            double create = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            Run(w, SimTime.TicksPerDay);
            double day = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var snap = StateSave.Write(w);
            double write = sw.Elapsed.TotalMilliseconds;
            var reads = new List<double>();
            World? copy = null;
            for (int i = 0; i < 3; i++)
            {
                sw.Restart();
                copy = StateSave.Read(snap);
                reads.Add(sw.Elapsed.TotalMilliseconds);
            }
            sw.Restart();
            Run(w, SimTime.Hours(2));
            double two = sw.Elapsed.TotalMilliseconds;
            Run(copy!, SimTime.Hours(2));
            uint h1 = SaveGame.StateHash(w), h2 = SaveGame.StateHash(copy!);
            Console.WriteLine($"  {ship}: 만들기 {create:0}ms · 하루 {day:0}ms · 두 시간 {two:0}ms · 찍기 {write:0}ms ({snap.Length / 1024}KB) · 되살리기 {string.Join("/", reads.Select(r => r.ToString("0")))}ms · 이어 간 지문 {(h1 == h2 ? "같음" : $"다름 {h1:x8}/{h2:x8}")}");
        }
        foreach (var d in WarmCache.Drift(() => { var x = World.CreateDefault(seed, 0, "Mirinae"); Run(x, SimTime.TicksPerDay); })) Console.WriteLine($"  하루 사이 바뀐 전역 칸: {d}");
        // 세계 밖 전역 스위치 (시험이 켜고 끄는 것): 첫날 배를 나눠 쓸 때 열쇠에 넣어야 한다
        var asm = typeof(World).Assembly;
        var fields = asm.GetTypes().SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsInitOnly && !f.IsLiteral && !f.Name.Contains('<')).ToList();
        var props = asm.GetTypes().SelectMany(t => t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(p => p.CanWrite && p.GetMethod != null).ToList();
        Console.WriteLine($"  전역 칸 {fields.Count}개 (bool {fields.Count(f => f.FieldType == typeof(bool))} · 수 {fields.Count(f => f.FieldType.IsPrimitive && f.FieldType != typeof(bool))} · 그 밖 {fields.Count(f => !f.FieldType.IsPrimitive)}) · 쓸 수 있는 전역 속성 {props.Count}");
        if (args.Contains("--list"))
            foreach (var f in fields.OrderBy(f => f.DeclaringType!.Name)) Console.WriteLine($"    {f.DeclaringType!.Name}.{f.Name} : {f.FieldType.Name}");
        return 0;
    }
}
