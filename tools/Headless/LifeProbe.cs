using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v19 2묶음: 생활 측정 — 같은 배 · 시드 · 인원 · 사고로 굶주림 · 탈진의 원인과 고정 비교 지표를 잰다.
//   days 시드 --lifeprobe [--ship=Saeteo] [--crew=60] [--incidents=fixed|natural|none]
//     fixed: 이틀마다 같은 시각에 같은 무작위 사고 (시드에서 나온 차례 그대로) · natural: 게임처럼 평균 3일에 한 번 · none: 사고 없음
//     storm: 둘째 날 아침 같은 시각에 태양 폭풍
public static partial class Program
{
    private static int RunLifeProbe(int days, int seed, string[] args)
    {
        string ship = args.FirstOrDefault(a => a.StartsWith("--ship="))?.Split('=')[1] ?? "Saeteo";
        int crew = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--crew="))?.Split('=')[1], out var cn) ? cn : 60;
        string mode = args.FirstOrDefault(a => a.StartsWith("--incidents="))?.Split('=')[1] ?? "fixed";
        if (mode == "natural") Tuning.Apply("incident.days", 3f);
        var w = World.CreateDefault(seed, crew, ship);
        Console.WriteLine($"생활 측정 · {ShipCatalog.Find(ship)?.Name ?? ship} · {w.Crew.Count(c => !c.Dead)}명 · 시드 {seed} · {days}일 · 사고 {mode}");
        var rng = new Rng(unchecked((int)((uint)seed * 2654435761u ^ 0x11fe5eedu)));
        var what = new List<string>();
        long end = w.Tick + (long)days * SimTime.TicksPerDay;
        long next = w.Tick + 2L * SimTime.TicksPerDay;
        int collapses0 = w.History.Collapses;
        int exhaustedEp = 0;
        var wasTired = new HashSet<int>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (w.Tick < end)
        {
            w.Step();
            if (mode == "fixed" && w.Tick == next)
            {
                what.Add($"{w.Day}일 {SimTime.Clock(w.Tick)} " + Scenarios.RandomIncident(w, rng));
                next += 2L * SimTime.TicksPerDay;
            }
            // storm: 둘째 날 아침 끼니 무렵 태양 폭풍 (같은 시각 · 같은 세기 — 폭풍 속 식사 · 잠 비교)
            if (mode == "storm" && w.Tick == next - SimTime.TicksPerDay + SimTime.Hours(23))
                what.Add($"{w.Day}일 {SimTime.Clock(w.Tick)} " + Hazards.Apply(w, HazardKind.SolarStorm, default, -1));
            if (w.Tick % SimTime.Minutes(5) != 0) continue;
            foreach (var c in w.Crew)
            {
                if (c.Dead) continue;
                bool t = c.Needs.Rest < 0.08f;
                if (t && wasTired.Add(c.Id)) exhaustedEp++;
                else if (!t && c.Needs.Rest > 0.3f) wasTired.Remove(c.Id);
            }
        }
        var lw = w.LifeWatch;
        Console.WriteLine("배식기: " + string.Join(" · ", w.Ship.FurnitureOf(FurnitureType.MealDispenser).GroupBy(f => f.Room.Name).Select(g => $"{g.Key} {g.Count()}대")) + $" · 식당 자리 {w.Ship.RoomsOf(RoomType.Mess).Sum(r => r.Furniture.Count(f => f.Type == FurnitureType.Seat))} · 로봇 {w.Robots.Summary}");
        Console.WriteLine(lw.Report());
        Console.WriteLine($"쓰러짐 {w.History.Collapses - collapses0} · 탈진(기력 8% 아래로) {exhaustedEp}번 · 사망 {w.Crew.Count(c => c.Dead)} · 굶주림(포만 2% 아래) {w.Crew.Sum(c => c.Stats.TicksStarving) / (float)SimTime.TicksPerHour:0.#}사람·시간 · {watch.Elapsed.TotalSeconds:0}초");
        if (what.Count > 0) Console.WriteLine("사고: " + string.Join(" / ", what));
        if (mode == "natural") Console.WriteLine("겪은 사고: " + string.Join(" / ", w.History.Events.Where(h => h.Kind == HistoryKind.Incident).Select(h => $"{SimTime.Clock(h.Tick)} {h.Text}").Take(12)));
        return 0;
    }
}
