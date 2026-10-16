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

    /// <summary>--warmsplit=파일: 프로세스를 나눠 견준다 — 파일이 없으면 첫날을 새로 돌려 찍어 두고, 있으면 되살린다.
    /// 그 뒤 틱마다 지문을 찍는다 (두 출력을 견주면 처음 갈라진 틱). --dumpat=틱 : 그 틱의 상태 바이트를 파일.새로|되살림.raw 로 · --describe=바이트 : 그 자리가 무엇인지.</summary>
    private static int RunWarmSplit(int seed, string[] args)
    {
        string file = args.First(a => a.StartsWith("--warmsplit="))[12..];
        string ship = args.FirstOrDefault(a => a.StartsWith("--ships="))?[8..] ?? "Hanbit";
        int hours = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--hours="))?[8..], out var hh) ? hh : 24;
        long dumpAt = long.TryParse(args.FirstOrDefault(a => a.StartsWith("--dumpat="))?[9..], out var da) ? da : -1;
        long describe = long.TryParse(args.FirstOrDefault(a => a.StartsWith("--describe="))?[11..], out var de) ? de : -1;
        World w;
        string label;
        if (!System.IO.File.Exists(file)) { w = DayOneRaw(seed, ship); System.IO.File.WriteAllBytes(file, StateSave.Write(w)); label = "새로"; }
        else { w = StateSave.Read(System.IO.File.ReadAllBytes(file)); label = "되살림"; }
        switch (args.FirstOrDefault(a => a.StartsWith("--scene="))?[8..])
        {
            case "meteor": w.CrewCanDie = true; Player.Hazard(w, HazardKind.MeteorShower, default); break;
            case "blackout":
                w.CrewCanDie = true;
                foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.3f;
                break;
            case "fire": w.CrewCanDie = true; Scenarios.Apply(w, "combo", out _); break;
            case "lamp": // aftertest 개인 조명 장면과 같게
            {
                var af = w.After;
                var mess = w.Ship.RoomsOf(RoomType.Mess).First();
                var reader = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Bed != null).OrderBy(c => c.Id).First();
                foreach (var c in w.Crew) c.Habits.Remove(Habit.Generous); foreach (var c in w.Crew) { c.Habits.Remove(Habit.Cheerful); c.Habits.Remove(Habit.Optimist); }
                if (!reader.Habits.Contains(Habit.Bookworm)) reader.Habits.Add(Habit.Bookworm);
                if (af.LampOf(reader) == null) w.Props.Place(Props.Get("readlamp"), reader.Bed!.Room, reader, "처음부터", near: reader.Bed.Cells[0]);
                mess.PowerCut = true;
                break;
            }
            case "hungry": foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.3f); w.Ship.RoomsOf(RoomType.Mess).First().PowerCut = true; break;
        }
        long end = w.Tick + SimTime.Hours(hours);
        while (w.Tick < end)
        {
            if (w.Tick == dumpAt)
            {
                StateSave.Write(w, out var raw);
                System.IO.File.WriteAllBytes($"{file}.{label}.raw", raw);
                if (describe >= 0) Console.WriteLine($"# {label} @{describe}: {StateSave.Describe(w, describe)}");
                return 0;
            }
            w.Step();
            Console.WriteLine($"{w.Tick} {SaveGame.StateHash(w):x8}");
        }
        return 0;
    }

    /// <summary>--warmdiff: 첫날 배(새로 돌린 것)와 그 배를 찍어 되살린 것을 나란히 한 틱씩 돌려, 상태가 처음 갈라지는 곳(객체 · 필드)을 찾는다.</summary>
    private static int RunWarmDiff(int seed, string[] args)
    {
        string ship = args.FirstOrDefault(a => a.StartsWith("--ships="))?[8..] ?? "Hanbit";
        int hours = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--hours="))?[8..], out var hh) ? hh : 24;
        if (args.Contains("--floodcheck"))
        {
            // 길찾기 거리장 캐시가 낡은 값을 돌려주나: 꺼낼 때마다 다시 펴서 견준다
            Pathfinder.CheckCache = true;
            var x = World.CreateDefault(seed, 0, ship);
            for (int h = 1; h <= 24 + hours; h++)
            {
                Run(x, SimTime.Hours(1));
                if (Pathfinder.StaleHits > 0) { Console.WriteLine($"✘ {SimTime.Day(x.Tick)}일 {SimTime.Clock(x.Tick)}까지 낡은 캐시 {Pathfinder.StaleHits}번 · 처음: {Pathfinder.FirstStale}"); return 1; }
            }
            Console.WriteLine($"✔ {24 + hours}시간 — 캐시에서 꺼낸 거리장 {x.Paths.FloodHits}번 모두 다시 편 것과 같다");
            return 0;
        }
        var a = DayOneRaw(seed, ship);
        var snap = StateSave.Write(a, out var raw0);
        var b = StateSave.Read(snap);
        StateSave.Write(b, out var raw1);
        int first = FirstDiff(raw0, raw1);
        Console.WriteLine($"되살린 직후: {(first < 0 ? "바이트까지 같음" : $"다름 @{first} · {StateSave.Describe(b, first)}")} ({raw0.Length}B)");
        long end = a.Tick + SimTime.Hours(hours);
        bool bytesSplit = false; // 바이트가 한 번 갈라지면 그 뒤로는 지문(행동)만 본다
        while (a.Tick < end)
        {
            a.Step(); b.Step();
            bool hashDiff = SaveGame.StateHash(a) != SaveGame.StateHash(b);
            if (!hashDiff && (bytesSplit || a.Tick % SimTime.Minutes(1) != 0)) continue;
            StateSave.Write(a, out var ra);
            StateSave.Write(b, out var rb);
            int at = FirstDiff(ra, rb);
            if (at < 0 && !hashDiff) continue;
            Console.WriteLine($"{(hashDiff ? "✘" : "·")} {SimTime.Day(a.Tick)}일 {SimTime.Clock(a.Tick)} (틱 {a.Tick})에 {(hashDiff ? "지문(행동)이" : "바이트가")} 갈라졌다 · 바이트 @{at}");
            if (at >= 0)
            {
                Console.WriteLine($"  새로 돌린 배: {StateSave.Describe(a, at)}");
                Console.WriteLine($"  되살린 배:   {StateSave.Describe(b, at)}");
            }
            if (!hashDiff) { bytesSplit = true; continue; }
            foreach (var line in DetDiff(a, b).Take(12)) Console.WriteLine("    " + line);
            return 1;
        }
        Console.WriteLine($"✔ {hours}시간 내내 같다");
        return 0;
    }
}
