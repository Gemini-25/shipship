using System;
using System.Linq;
using ShipSim.Core;

// v23 설계도 손보기 진단 (--hungerprobe 일 배): 식량이 쌓였는데 굶는 사람 — 누가 · 어디서 · 무엇을 하다가.
public static partial class Program
{
    private static int RunHungerProbe(int days, int seed, string ship)
    {
        var w = World.CreateDefault(seed, 0, ship);
        w.CrewCanDie = true;
        long end = (long)days * SimTime.TicksPerDay;
        var seen = new System.Collections.Generic.Dictionary<int, int>();
        while (w.Tick < end)
        {
            Run(w, SimTime.Minutes(30));
            foreach (var c in w.Crew.Where(c => !c.Dead && c.Needs.Food <= 0.04f))
            {
                int n = seen.TryGetValue(c.Id, out var k) ? k : 0;
                seen[c.Id] = n + 1;
                if (n >= 6) continue;
                var meal = w.Ship.CountStored(ItemKind.Meal);
                Console.WriteLine($"{SimTime.Day(w.Tick)}일 {SimTime.Clock(w.Tick)} {c.Name}({c.Role}) 포만 {c.Needs.Food * 100:0}% · 하는 일 {c.Job?.Label ?? "없음"} · 방 {c.Room?.Name ?? "-"} · 길 {(c.Path?.Count ?? 0)}칸 · 깨어 {c.IsAwake} · 바깥 {c.Outside}/{c.Away} · 쓰러짐 {c.Down} · 잠 {c.Needs.Rest * 100:0}% · 병 {string.Join(",", c.Ailments.Select(a => a.Id))} · 배의 식사 {meal}");
            }
        }
        Console.WriteLine("굶은 횟수 (30분마다): " + string.Join(" · ", seen.OrderByDescending(kv => kv.Value).Select(kv => $"{w.Crew[kv.Key].Name} {kv.Value}")));
        return 0;
    }
}
