using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// 결정론 점검: 같은 빌드 · 같은 시드 · 같은 입력이면 같은 틱에 같은 상태여야 한다.
/// --detcheck : 한 프로세스에서 두 세계를 나란히 돌려 1분마다 지문을 견준다 — 갈라지면 처음 갈라진 틱과 달라진 값을 남긴다.
/// --dethash  : 1시간마다 지문을 찍는다 (프로세스 둘을 따로 돌려 출력끼리 견준다 · 하나는 바쁜 컴퓨터에서).
/// </summary>
public static partial class Program
{
    private static readonly string[] DetShips = { "Mirinae", "Hanbit", "Busitdol", "Saeteo" };

    private static int RunDetCheck(int seed, int hours, string? only)
    {
        Console.WriteLine($"결정론 점검 · 시드 {seed} · {hours}시간 · 1분마다 지문 비교\n");
        int bad = 0;
        foreach (var ship in only != null ? new[] { only } : DetShips)
        {
            var a = World.CreateDefault(seed, 0, ship);
            var b = World.CreateDefault(seed, 0, ship);
            long end = SimTime.Hours(hours), step = SimTime.Minutes(1);
            long diverged = -1;
            for (long t = 0; t < end && diverged < 0; t += step)
            {
                for (long k = 0; k < step; k++) { a.Step(); b.Step(); }
                if (SaveGame.StateHash(a) != SaveGame.StateHash(b)) diverged = a.Tick;
            }
            if (diverged < 0) { Console.WriteLine($"  ✔ {ship}: {hours}시간 내내 같다 ({SaveGame.StateHash(a):x8})"); continue; }
            bad++;
            // 처음 갈라진 틱을 한 틱 단위로 다시 찾는다
            a = World.CreateDefault(seed, 0, ship);
            b = World.CreateDefault(seed, 0, ship);
            while (a.Tick < diverged - SimTime.Minutes(1)) { a.Step(); b.Step(); }
            while (SaveGame.StateHash(a) == SaveGame.StateHash(b)) { a.Step(); b.Step(); }
            Console.WriteLine($"  ✘ {ship}: {SimTime.Clock(a.Tick)} (틱 {a.Tick})에 처음 갈라졌다");
            foreach (var line in DetDiff(a, b).Take(12)) Console.WriteLine("      " + line);
        }
        Console.WriteLine(bad == 0 ? "\n✔ 결정론 — 같은 시드는 같은 역사" : $"\n✘ {bad}척에서 갈라짐");
        return bad == 0 ? 0 : 1;
    }

    private static int RunDetHash(int seed, int hours, string? only)
    {
        foreach (var ship in only != null ? new[] { only } : DetShips)
        {
            var w = World.CreateDefault(seed, 0, ship);
            for (int h = 1; h <= hours; h++)
            {
                Run(w, SimTime.Hours(1));
                Console.WriteLine($"{ship} {h}h {SaveGame.StateHash(w):x8}");
            }
        }
        return 0;
    }

    /// <summary>두 세계에서 달라진 값 (사람 · 자원 · 작업 · 기록).</summary>
    private static IEnumerable<string> DetDiff(World a, World b)
    {
        static string Crew(CrewMember c) =>
            $"{c.Name} 칸{c.Cell} 위치({c.Position.X:R},{c.Position.Y:R}) 배{c.Needs.Food:R} 휴{c.Needs.Rest:R} 스{c.Needs.Stress:R} 일[{c.Job?.Label}/{c.Job?.Current?.GetType().Name}] {c.Pose}";
        for (int i = 0; i < Math.Min(a.Crew.Count, b.Crew.Count); i++)
        {
            string x = Crew(a.Crew[i]), y = Crew(b.Crew[i]);
            if (x != y) yield return $"사람 {i}: {x}\n        ↔ {y}";
        }
        if (a.Water.Level != b.Water.Level) yield return $"물 {a.Water.Level:R} ↔ {b.Water.Level:R}";
        if (a.Power.BatteryCharge != b.Power.BatteryCharge) yield return $"배터리 {a.Power.BatteryCharge:R} ↔ {b.Power.BatteryCharge:R}";
        if (a.Log.Entries.Count != b.Log.Entries.Count) yield return $"기록 수 {a.Log.Entries.Count} ↔ {b.Log.Entries.Count}";
        var la = a.Log.Entries.Select(e => e.Text).ToList();
        var lb = b.Log.Entries.Select(e => e.Text).ToList();
        for (int i = 0; i < Math.Min(la.Count, lb.Count); i++)
            if (la[i] != lb[i]) { yield return $"기록 {i}: \"{la[i]}\" ↔ \"{lb[i]}\""; break; }
        var oa = a.Board.Open.Select(o => o.Title).OrderBy(s => s).ToList();
        var ob = b.Board.Open.Select(o => o.Title).OrderBy(s => s).ToList();
        if (!oa.SequenceEqual(ob)) yield return $"작업 {oa.Count} ↔ {ob.Count}: {string.Join(",", oa.Except(ob).Take(3))} | {string.Join(",", ob.Except(oa).Take(3))}";
    }
}
