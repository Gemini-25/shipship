using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.21 승무원 위기 행동: 공황 재조정 · 비상 배치표 · 비상 절차 훈련 · 한 작업에 여러 명 · 위기 우선순위
public static partial class Program
{
    /// <summary>한 판의 위기 측정값.</summary>
    private sealed class CrisisRun
    {
        public int PanicTicks, Panics, Deaths, PanicDeaths, Silent, AuxStarts, Samples, HandSum, HandJobs, HandMax, Snaps;
        public float AuxMinutes = -1f, AuxBattery = -1f;
    }

    /// <summary>위기 장면 하나를 돌리며 잰다: 공황 · 공황 중 사망 · 대응 없이 죽은 사람 · 보조 발전기 · 위급 작업에 붙은 인원.</summary>
    private static CrisisRun MeasureScene(int seed, string scene, float hours)
    {
        var w = DayOne(seed, "Hanbit");
        w.CrewCanDie = true;
        int panics0 = w.Minds.Panics;
        var r = new CrisisRun();
        switch (scene)
        {
            case "meteor":
                Player.Hazard(w, HazardKind.MeteorShower, default);
                break;
            case "blackout":
                foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.3f;
                break;
            case "fire":
                Scenarios.Apply(w, "combo", out _);
                break;
            case "chaos":
                Scenarios.Apply(w, "chaos", out _);
                break;
            case "night":
                while (Math.Abs(SimTime.HourOfDay(w.Tick) - 23f) > 0.05f) w.Step();
                Player.Hazard(w, HazardKind.MeteorShower, default);
                break;
        }
        long t0 = w.Tick;
        var responded = new HashSet<int>();
        var lastPanic = new Dictionary<int, long>();
        var dead0 = w.Crew.Where(c => c.Dead).Select(c => c.Id).ToHashSet();
        bool aux = w.Power.AuxRunning;
        long end = w.Tick + SimTime.Hours(hours);
        var hands = new Dictionary<int, int>();
        bool trace = Environment.GetEnvironmentVariable("CR_TRACE") == $"{scene}:{seed}";
        int logAt = w.Log.Entries.Count;
        var lastDoing = new Dictionary<int, string>();
        while (w.Tick < end)
        {
            Run(w, 15);
            r.Samples++;
            if (trace)
            {
                var es = w.Log.Entries;
                int i0 = es.Count;
                while (i0 > 0 && es[i0 - 1].Tick > w.Tick - 15) i0--;
                for (int i = i0; i < es.Count; i++)
                {
                    var e = es[i];
                    if (e.Text.Contains("공황") || e.Text.Contains("숨졌") || e.Text.Contains("발전기") || e.Text.Contains("쓰러") || e.Text.Contains("정신"))
                        Console.WriteLine($"      {SimTime.Clock(e.Tick)} {w.Crew.FirstOrDefault(c => c.Id == e.CrewId)?.Name ?? "-"}: {e.Text}");
                }
                logAt = es.Count;
                if ((w.Tick - t0) % SimTime.Minutes(30) < 15)
                    Console.WriteLine($"    {SimTime.Clock(w.Tick)} 배터리 {w.Power.BatteryPercent * 100:0}% 흐름 {w.Power.BatteryFlow:0.0} 원자로 {(w.Power.ReactorOnline ? "켜짐" : "꺼짐")} 한도 {w.Power.ReactorLimit:0} 수요 {w.Power.Demand:0}/{w.Power.Delivered:0} 보조 {w.Power.AuxRunning} 위기 {Crisis.Name(Crisis.Level(w))} · 공황 {w.Crew.Count(c => c.Mind.Panicking(w.Tick))}");
                foreach (var c in w.Crew)
                {
                    if (c.Dead && !dead0.Contains(c.Id)) Console.WriteLine($"      ✝ {c.Name} {c.Vitals.InjuryCause} · 마지막: {lastDoing.GetValueOrDefault(c.Id)}");
                    if (!c.Dead) lastDoing[c.Id] = $"{SimTime.Clock(w.Tick)} {c.Room?.Name} {Doing(c, w)} 체력 {c.Vitals.Health:0.00} 산소 {c.Vitals.Oxygen:0.00} 공황 {c.Mind.Panicking(w.Tick)}";
                }
            }
            hands.Clear();
            foreach (var c in w.Crew)
            {
                if (c.Dead)
                {
                    if (dead0.Add(c.Id))
                    {
                        r.Deaths++;
                        if (lastPanic.TryGetValue(c.Id, out long pt) && w.Tick - pt < SimTime.Minutes(4)) r.PanicDeaths++;
                        if (!responded.Contains(c.Id)) r.Silent++;
                    }
                    continue;
                }
                if (c.Mind.Panicking(w.Tick)) { lastPanic[c.Id] = w.Tick; r.PanicTicks += 15; }
                var job = c.Job;
                if (job != null && (job.Urgent || job.Activity is EvacuateActivity or TakeCoverActivity or RefillSuitActivity or ShelterActivity or MusterActivity
                                    || job.Order is { Urgency: >= 0.85f })) responded.Add(c.Id);
                if (CrisisHelpOrder(w, c) is int hid) { responded.Add(c.Id); if (job?.Current is AssistToil) hands[hid] = hands.GetValueOrDefault(hid) + 1; }
                else if (job?.Order is WorkOrder o && o.Urgency >= 0.85f && !o.Closed) hands[o.Id] = hands.GetValueOrDefault(o.Id) + 1;
            }
            foreach (var (_, n) in hands) { r.HandSum += n; r.HandJobs++; r.HandMax = Math.Max(r.HandMax, n); }
            if (w.Power.AuxRunning && !aux)
            {
                r.AuxStarts++;
                if (r.AuxMinutes < 0f) { r.AuxMinutes = (w.Tick - t0) * 60f / SimTime.TicksPerHour; r.AuxBattery = w.Power.BatteryPercent; }
            }
            aux = w.Power.AuxRunning;
        }
        r.Panics = w.Minds.Panics - panics0;
        r.Snaps = CrisisSnaps(w);
        return r;
    }

    /// <summary>이 사람이 거드는 일 (없으면 null) — 예전 AI에는 없다.</summary>
    private static int? CrisisHelpOrder(World w, CrewMember c) => w.CrisisCrew.HelpingOrder(c) is int id && id >= 0 ? id : null;
    private static int CrisisSnaps(World w) => w.CrisisCrew.Snaps;

    /// <summary>장면 묶음을 여러 시드로 돌려 표 한 줄씩.</summary>
    private static List<(string scene, CrisisRun sum, int runs)> MeasureBundle(int[] seeds, string[] scenes, float hours)
    {
        var rows = new List<(string, CrisisRun, int)>();
        foreach (var s in scenes)
        {
            var sum = new CrisisRun();
            float auxMin = 0f; int auxN = 0; float auxBat = 0f;
            foreach (int seed in seeds)
            {
                var r = MeasureScene(seed, s, hours);
                sum.Panics += r.Panics; sum.PanicTicks += r.PanicTicks; sum.Deaths += r.Deaths; sum.PanicDeaths += r.PanicDeaths; sum.Silent += r.Silent; sum.AuxStarts += r.AuxStarts;
                sum.HandSum += r.HandSum; sum.HandJobs += r.HandJobs; sum.HandMax = Math.Max(sum.HandMax, r.HandMax); sum.Snaps += r.Snaps;
                if (r.AuxMinutes >= 0f) { auxMin += r.AuxMinutes; auxBat += r.AuxBattery; auxN++; }
                Console.WriteLine($"    {s,-8} 시드 {seed,3}: 공황 {r.Panics,2} ({r.PanicTicks * 60f / SimTime.TicksPerHour:0}분) · 사망 {r.Deaths} (공황 중 {r.PanicDeaths} · 대응 없이 {r.Silent}) · 발전기 {r.AuxStarts}회"
                                  + (r.AuxMinutes >= 0f ? $" {r.AuxMinutes:0}분 배터리 {r.AuxBattery * 100:0}%" : "") + $" · 위급 작업 평균 {(r.HandJobs > 0 ? r.HandSum / (float)r.HandJobs : 0f):0.00}명 최대 {r.HandMax} · 깨움 {r.Snaps}");
            }
            sum.AuxMinutes = auxN > 0 ? auxMin / auxN : -1f;
            sum.AuxBattery = auxN > 0 ? auxBat / auxN : -1f;
            sum.Samples = auxN;
            rows.Add((s, sum, seeds.Length));
        }
        return rows;
    }

    private static void PrintBundle(string title, List<(string scene, CrisisRun sum, int runs)> rows)
    {
        Console.WriteLine($"  [{title}]  장면 · 판당 공황 · 사망(공황 중 · 대응 없이) · 발전기 켠 판/걸린 분/배터리 · 위급 작업 평균 인원(최대)");
        foreach (var (s, x, n) in rows)
            Console.WriteLine($"    {s,-8} 공황 {x.Panics / (float)n:0.0}번 {x.PanicTicks * 60f / SimTime.TicksPerHour / n:0}분 · 사망 {x.Deaths}/{n}판 (공황 중 {x.PanicDeaths} · 대응 없이 {x.Silent}) · 발전기 {x.Samples}/{n}판"
                              + (x.AuxMinutes >= 0f ? $" {x.AuxMinutes:0}분 {x.AuxBattery * 100:0}%" : "") + $" · 인원 {(x.HandJobs > 0 ? x.HandSum / (float)x.HandJobs : 0f):0.00} (최대 {x.HandMax}) · 깨움 {x.Snaps}");
    }

    private static int RunCrisisCrewTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"승무원 위기 행동 점검 (v16.21) · 시드 {seed}\n");
        var seeds = Environment.GetEnvironmentVariable("CR_SEEDS") is string ss ? ss.Split(",").Select(int.Parse).ToArray() : new[] { seed, seed + 4, seed + 13, seed + 22 };
        var scenes0 = Environment.GetEnvironmentVariable("CR_SCENES")?.Split(",") ?? new[] { "meteor", "blackout", "fire" };
        bool off0 = CrisisCrewSystem.Off;
        CrisisCrewSystem.Off = true;
        var before = Environment.GetEnvironmentVariable("CR_AFTER") == "1" ? new() : MeasureBundle(seeds, scenes0, 6f);
        CrisisCrewSystem.Off = off0;
        var after = MeasureBundle(seeds, scenes0, 6f);
        PrintBundle("전 · 예전 승무원", before);
        PrintBundle("후", after);
        return _fails;
    }
}
