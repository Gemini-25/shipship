using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 화면 없이 시뮬레이션만 빠르게 돌려 보는 도구.
//   dotnet run --project tools/Headless -- [일수=14] [시드=20260929]
// 튜닝을 바꾼 뒤 이걸 돌려서 "아무 일 없으면 우주선이 알아서 굴러가는지" 확인한다.

public static partial class Program
{
    /// <summary>v10.1 성능: 승무원 수를 늘려 가며 같은 기간을 돌리고 틱/초와 살림살이를 본다.</summary>
    private static int RunBench(int days, int seed)
    {
        Console.WriteLine($"성능 측정 · {days}일 · 시드 {seed}");
        foreach (int n in new[] { 6, 10, 16 })
        {
            var w = World.CreateDefault(seed, n);
            w.CrewCanDie = true;
            long end = w.Tick + (long)days * SimTime.TicksPerDay;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            float minFood = 1f, minRest = 1f, maxCO2 = 0f;
            while (w.Tick < end)
            {
                w.Step();
                if (w.Tick % World.SystemInterval == 0)
                {
                    foreach (var c in w.Crew.Where(c => !c.Dead)) { minFood = MathF.Min(minFood, c.Needs.Food); minRest = MathF.Min(minRest, c.Needs.Rest); }
                    foreach (var r in w.Ship.Rooms) maxCO2 = MathF.Max(maxCO2, r.Air.CO2);
                }
            }
            watch.Stop();
            double tps = days * SimTime.TicksPerDay / Math.Max(0.001, watch.Elapsed.TotalSeconds);
            var ship = w.Ship;
            Console.WriteLine($"  승무원 {n,2}명: {watch.ElapsedMilliseconds,6}ms · {tps,8:N0} 틱/초 (하루 {SimTime.TicksPerDay / tps:0.00}초) · 사망 {w.Crew.Count(c => c.Dead)} · 간이침대 {ship.FurnitureOf(FurnitureType.Cot).Count()} · 최저 배고픔 {minFood * 100:0}% · 최저 휴식 {minRest * 100:0}% · 최고 CO2 {maxCO2:0.00}% · 식사 {ship.CountStored(ItemKind.Meal)} 채소 {ship.CountStored(ItemKind.Produce)} 비상식량 {ship.CountStored(ItemKind.Ration)} · 물 {w.Water.Level:0}L");
        }
        return 0;
    }

    /// <summary>v10.4: 템플릿마다 설계 인원으로 무사고 N일 — 굶주림·물·산소·전력이 버티나, 틱/초.</summary>
    private static int RunShips(int days, int seed)
    {
        Console.WriteLine($"배 크기 템플릿 · 무사고 {days}일 · 시드 {seed}");
        int bad = 0;
        foreach (var t in ShipCatalog.All)
        {
            var w = World.CreateDefault(seed, 0, t.Key);
            w.CrewCanDie = true;
            long end = w.Tick + (long)days * SimTime.TicksPerDay;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            float minFood = 1f, minO2 = 99f, minBattery = 1f, minWater = 1e9f, maxDemand = 0f;
            long starving = 0;
            while (w.Tick < end)
            {
                w.Step();
                if (w.Tick % World.SystemInterval != 0) continue;
                foreach (var c in w.Crew.Where(c => !c.Dead)) { minFood = MathF.Min(minFood, c.Needs.Food); if (c.Needs.Food <= 0.02f) starving++; }
                foreach (var r in w.Ship.Rooms.Where(r => !r.Detached)) minO2 = MathF.Min(minO2, r.Air.O2);
                minBattery = MathF.Min(minBattery, w.Power.BatteryPercent);
                minWater = MathF.Min(minWater, w.Water.Level);
                maxDemand = MathF.Max(maxDemand, w.Power.Demand);
            }
            watch.Stop();
            var ship = w.Ship;
            int dead = w.Crew.Count(c => c.Dead);
            bool ok = dead == 0 && minO2 > 18f && starving < 20 && minBattery > 0.2f;
            if (!ok) bad++;
            Console.WriteLine($"  {(ok ? "✔" : "✘")} {t.Name}({t.Key}) {w.Crew.Count}명 · 방 {ship.Rooms.Count} · 설비 {ship.Machines.Count()} · 격자 {ship.Grid.Width}×{ship.Grid.Height} · " +
                              $"원자로 {w.Power.ReactorRated:0}kW(최대 수요 {maxDemand:0}) · 냉각 {w.Piping.CoolingKw:0}kW · 최저 배터리 {minBattery * 100:0}% · 최저 산소 {minO2:0.0}kPa · 최저 포만 {minFood * 100:0}% · " +
                              $"굶주림 {starving}회 · 물 {minWater:0}/{w.Water.Capacity:0}L · 사망 {dead} · 연구 {w.Research:0} · {days * SimTime.TicksPerDay / Math.Max(0.001, watch.Elapsed.TotalSeconds):N0}틱/초");
        }
        return bad == 0 ? 0 : 1;
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int days = args.Length > 0 && int.TryParse(args[0], out var d) ? d : 14;
        int seed = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 20260929;
        bool quiet = args.Contains("--quiet");

        bool death = args.Contains("--death");
        int crewArg = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--crew="))?.Split('=')[1], out var cn) ? cn : 0;
        string? shipArg = args.FirstOrDefault(a => a.StartsWith("--ship="))?.Split('=')[1];
        if (args.FirstOrDefault(a => a.StartsWith("--tuning=")) is string tf && System.IO.File.Exists(tf.Split('=', 2)[1]))
            Console.WriteLine($"수치 파일: {Tuning.Load(System.IO.File.ReadAllText(tf.Split('=', 2)[1]))}개 적용");
        if (args.Contains("--bench")) return RunBench(days, seed);
        if (args.Contains("--ships")) return RunShips(days, seed);
        if (args.Contains("--bigships")) return RunBigShips(days, seed, args);
        if (args.Contains("--tiers")) return RunTierRecovery(seed, args);
        if (args.Contains("--docks")) return RunDocks();
        var world = World.CreateDefault(seed, crewArg, shipArg);
        world.CrewCanDie = death;
        world.Log.Capacity = 60000; // 긴 시험의 기록을 끝까지 볼 수 있게
        var ship = world.Ship;
        if (Environment.GetEnvironmentVariable("SHIPSIM_TRACE") is string trace)
            World.Trace = msg => { if (msg.Contains(trace)) Console.WriteLine("  [trace] " + msg); };

        string? scenario = args.FirstOrDefault(a => a.StartsWith("--scenario="))?.Split('=')[1];
        if (scenario is "all" or "extreme" or "adaptive" or "materials" or "structure" or "pipes" or "auto" or "fixtures")
            return RunAllScenarios(seed, death, scenario switch
            {
                "extreme" => Scenarios.Extreme,
                "adaptive" => Scenarios.Adaptive,
                "materials" => Scenarios.Materials,
                "structure" => Scenarios.Structural,
                "pipes" => Scenarios.Piping,
                "auto" => Scenarios.Automation,
                "fixtures" => Scenarios.Fixtures,
                _ => Scenarios.All.Select(x => x.Name).ToArray(),
            });
        if (args.Contains("--map")) { PrintMap(world); return 0; }
        if (args.Contains("--pipemap")) { PrintPipeMap(world); PrintPipes(world); return 0; }
        if (args.Contains("--gate=auto"))
            return RunAutoGate(int.TryParse(args.FirstOrDefault(a => a.StartsWith("--runs="))?.Split('=')[1], out var arn) ? arn : 12, seed);
        if (args.Contains("--gate=pipes"))
            return RunPipeGate(int.TryParse(args.FirstOrDefault(a => a.StartsWith("--runs="))?.Split('=')[1], out var prn) ? prn : 20, seed);
        if (args.Contains("--selftest")) return RunSelfTest(seed);
        if (args.Contains("--robottest")) return RunRobotTest(seed);
        if (args.Contains("--hazardtest")) return RunHazardTest(seed);
        if (args.Contains("--livingtest")) return RunLivingTest(seed);
        if (args.Contains("--remodeltest")) return RunRemodelTest(seed);
        if (args.Contains("--commstest")) return RunOutsideCommsTest(seed);
        if (args.Contains("--growthtest")) return RunGrowthTest(seed);
        if (args.Contains("--watchtest")) return RunWatchTest(seed); // v12.0
        if (args.Contains("--volatiletest")) return RunVolatileTest(seed); // v12.2
        if (args.Contains("--proctest")) return RunProcedureTest(seed); // v12.1
        if (args.Contains("--nettest")) return RunNetTest(seed);
        if (args.Contains("--handtrace")) return RunHandoverTrace(seed);
        if (args.Contains("--chaintest")) return RunCauseTest(seed); // v12.2
        if (args.Contains("--moisturetest")) return RunMoistureTest(seed); // v12.3
        if (args.Contains("--storytest")) return RunStoryTest(seed); // v12.4
        if (args.Contains("--autotest")) return RunAutomationTest(seed); // v12.5
        if (args.FirstOrDefault(a => a.StartsWith("--ambdebug=")) is string ad) return RunAmbienceDebug(seed, ad[11..]);
        if (args.Contains("--jumpdebug")) return RunJumperDebug(seed);
        if (args.Contains("--treatdebug")) return RunTreatDebug(seed);
        if (args.Contains("--watchdebug")) return RunWatchDebug(seed);
        if (args.Contains("--voyagetest")) return RunVoyageTest(seed); // v12.8
        if (args.Contains("--campaigntest")) return RunCampaignTest(seed); // v12.9
        if (args.Contains("--responsetest")) return RunResponseTest(seed); // v13.0
        if (args.Contains("--commandtest")) return RunCommandTest(seed); // v13.1
        if (args.Contains("--meetingtest")) return RunMeetingTest(seed); // v13.2
        if (args.Contains("--deathtrace")) return RunDeathTrace(Math.Max(1, days), seed);
        if (args.Contains("--stressprobe")) return RunStressProbe(Math.Max(1, days), seed);
        if (args.Contains("--campaignrun")) return RunCampaignLong(Math.Max(1, days), seed, shipArg ?? "Mirinae", args.FirstOrDefault(a => a.StartsWith("--from="))?[7..]);
        if (args.Contains("--crewtest")) return RunCrewTest(seed); // v12.7
        if (args.Contains("--designtest")) { args_Print = args.Contains("--print"); return RunDesignTest(seed); } // v12.6
        if (args.Contains("--autodebug")) return RunAutoDebug(seed);
        if (args.Contains("--balance")) return RunBalance(Math.Max(1, days), seed, int.TryParse(args.FirstOrDefault(a => a.StartsWith("--runs="))?.Split('=')[1], out var brn) ? brn : 3);
        if (args.Contains("--moisturedebug")) return RunMoistureDebug(seed);
        if (args.Contains("--netdebug")) return RunNetDebug(seed);
        if (args.FirstOrDefault(a => a.StartsWith("--reopentrace=")) is string rt) return RunReopenTrace(seed, rt.Split('=')[1], Math.Max(1, days));
        if (args.Contains("--crisistrace")) return RunCrisisTrace(seed, args.FirstOrDefault(a => a.StartsWith("--ship="))?[7..] ?? "Mirinae", Math.Max(1, days));
        if (args.Contains("--gate=crisis")) return RunCrisisGate(Math.Max(1, days), seed, args.FirstOrDefault(a => a.StartsWith("--ship="))?[7..] ?? "Mirinae");
        if (args.FirstOrDefault(a => a.StartsWith("--partition=")) is string pc)
            return RunPartitionCampaign(days, seed, pc.Split('=')[1]);
        if (args.Contains("--gate=partition"))
            return RunPartitionGate(int.TryParse(args.FirstOrDefault(a => a.StartsWith("--runs="))?.Split('=')[1], out var prt) ? prt : 12, seed);
        if (args.Contains("--gate=comms"))
            return RunCommsGate(int.TryParse(args.FirstOrDefault(a => a.StartsWith("--runs="))?.Split('=')[1], out var crn) ? crn : 10, seed);
        if (args.Contains("--gate=recovery")) return RunRecoveryGate(days, seed, shipArg);
        if (args.Contains("--gate=distributed"))
            return RunDistributedGate(int.TryParse(args.FirstOrDefault(a => a.StartsWith("--runs="))?.Split('=')[1], out var drn) ? drn : 4, seed);
        if (args.Contains("--gate=structure"))
            return RunStructureGate(int.TryParse(args.FirstOrDefault(a => a.StartsWith("--runs="))?.Split('=')[1], out var rn) ? rn : 20, seed);
        if (args.FirstOrDefault(a => a.StartsWith("--gate")) is string gate)
            return RunGate(days, seed, gate.Contains('=') ? gate.Split('=')[1] : "order", quiet);
        if (args.FirstOrDefault(a => a.StartsWith("--savetest")) is string st)
            return RunSaveTest(days, seed, st.Contains('=') ? st.Split('=')[1] : null);
        if (args.FirstOrDefault(a => a.StartsWith("--load=")) is string ld)
            return RunLoad(ld.Split('=', 2)[1]);
        if (args.FirstOrDefault(a => a.StartsWith("--rhythm")) is string rh)
            return RunRhythm(world, days, rh.Contains('=') ? rh.Split('=')[1] : "chaos", quiet);
        if (args.Contains("--scarcity"))
        {
            int every = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--every="))?.Split('=')[1], out var ev) ? ev : 10;
            return RunScarcity(world, days, every, quiet);
        }
        if (scenario != null) return RunScenario(world, scenario, verbose: true) == null ? 2 : 0;
        string? experiment = args.FirstOrDefault(a => a.StartsWith("--experiment="))?.Split('=')[1];
        int waves = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--waves="))?.Split('=')[1], out var wv) ? wv : 1;
        if (experiment != null) return RunExperiment(int.Parse(experiment), seed, death, waves);
        Console.WriteLine($"{ship.Name} · 방 {ship.Rooms.Count} · 가구/설비 {ship.Furniture.Count} (설비 {ship.Machines.Count()}) · 문 {ship.Doors.Count} · 승무원 {world.Crew.Count} · 시드 {seed}");
        Console.WriteLine($"{days}일 시뮬레이션\n");

        int n = world.Crew.Count;
        var minFood = Enumerable.Repeat(1f, n).ToArray();
        var minRest = Enumerable.Repeat(1f, n).ToArray();
        var minHealth = Enumerable.Repeat(1f, n).ToArray();
        var minOx = Enumerable.Repeat(1f, n).ToArray();
        var maxStress = new float[n];
        var timeline = new List<char>[n];
        for (int i = 0; i < n; i++) timeline[i] = new List<char>();

        float minO2 = 99f, maxCO2 = 0f, minWater = 9999f, minBattery = 1f;
        long shedTicks = 0;
        var daily = new List<string>();

        long start = world.Tick;
        long end = start + (long)days * SimTime.TicksPerDay;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (world.Tick < end)
        {
            world.Step();
            for (int i = 0; i < n; i++)
            {
                var c = world.Crew[i];
                if (c.Needs.Food < 0.02f && minFood[i] >= 0.02f && Environment.GetEnvironmentVariable("SHIPSIM_WATCH") == "1")
                {
                    Console.WriteLine($"[watch] {world.Day}일차 {world.Clock} {c.Name} 굶주림 · {c.ActivityLabel} @{c.Room?.Name} down={c.Down} job={c.Job?.Label} reason={c.JobReason}");
                    foreach (var e in c.LastEvaluations.Take(6)) Console.WriteLine($"   {e.Activity.Label} {e.Score:0.00} {e.Reason}");
                    foreach (var e in world.Log.Entries.Where(e => e.CrewId == c.Id).TakeLast(12))
                        Console.WriteLine($"   {SimTime.Day(e.Tick)}일차 {SimTime.Clock(e.Tick)} {e.Text}");
                }
                minFood[i] = MathF.Min(minFood[i], c.Needs.Food);
                minRest[i] = MathF.Min(minRest[i], c.Needs.Rest);
                minHealth[i] = MathF.Min(minHealth[i], c.Vitals.Health);
                minOx[i] = MathF.Min(minOx[i], c.Vitals.Oxygen);
                maxStress[i] = MathF.Max(maxStress[i], c.Needs.Stress);
            }
            if (world.Tick % World.SystemInterval == 0)
            {
                foreach (var r in ship.Rooms)
                {
                    minO2 = MathF.Min(minO2, r.Air.O2);
                    maxCO2 = MathF.Max(maxCO2, r.Air.CO2);
                }
                minWater = MathF.Min(minWater, world.Water.Level);
                minBattery = MathF.Min(minBattery, world.Power.BatteryPercent);
                if (world.Power.ShedCount > 0) shedTicks += World.SystemInterval;
            }
            long rel = world.Tick - start;
            if (rel > SimTime.TicksPerDay && rel <= 2L * SimTime.TicksPerDay && rel % SimTime.Minutes(30) == 0)
                for (int i = 0; i < n; i++) timeline[i].Add(Symbol(world.Crew[i]));
            if (rel % SimTime.TicksPerDay == 0)
            {
                int faults = ship.Machines.Sum(m => m.Faults.Count);
                float avgWear = ship.Machines.Average(m => m.Wear);
                daily.Add($"  {world.Day - 1,3}일  식사 {ship.CountStored(ItemKind.Meal),3}  채소 {ship.CountStored(ItemKind.Produce),3}  " +
                          $"물 {world.Water.Level,5:0}L  배터리 {world.Power.BatteryPercent * 100,4:0}%  평균마모 {avgWear * 100,3:0}%  " +
                          $"고장 {faults}  작업 {world.Board.OpenCount,2}  수리재 {Tier(ship, ItemTier.Basic),3}  부품 {Tier(ship, ItemTier.General),2}  원료 {Tier(ship, ItemTier.Raw),3}  탱크 {world.Air.Reserve / world.Air.ReserveCapacity * 100,3:0}%  필터 {ship.CountStored(ItemKind.Filter),2}  윤활유 {ship.CountStored(ItemKind.Lubricant),2}");
            }
        }
        watch.Stop();
        Console.WriteLine($"실행 시간 {watch.ElapsedMilliseconds}ms ({(end - start) / Math.Max(0.001, watch.Elapsed.TotalSeconds):N0} 틱/초)\n");

        Console.WriteLine("하루 끝 우주선 상태");
        foreach (var line in daily) Console.WriteLine(line);

        bool ok = true;
        Console.WriteLine("\n이름     식사/일 수면h 일h  휴식h 대화 정비 수리 수확 조리 최저포만 최저기력 최저체력 최저산소 최고스트레스");
        for (int i = 0; i < n; i++)
        {
            var c = world.Crew[i];
            float meals = c.Stats.Meals / (float)days;
            float sleep = c.Stats.TicksAsleep / (float)SimTime.TicksPerHour / days;
            float work = c.Stats.TicksWorking / (float)SimTime.TicksPerHour / days;
            float relax = c.Stats.TicksRelaxing / (float)SimTime.TicksPerHour / days;
            Console.WriteLine($"{c.Name,-6} {meals,6:0.0} {sleep,5:0.0} {work,4:0.0} {relax,5:0.0} {c.Stats.Chats,4} {c.Stats.Services,4} {c.Stats.Repairs,4} {c.Stats.Harvests,4} {c.Stats.MealsCooked,4} " +
                              $"{minFood[i],8:0.00} {minRest[i],8:0.00} {minHealth[i],8:0.00} {minOx[i],8:0.00} {maxStress[i],10:0.00}");

            ok &= Check(meals >= 2f && meals <= 4f * c.Traits.Appetite + 0.6f, $"{c.Name}: 하루 식사 {meals:0.0}회");
            ok &= Check(sleep is >= 5.5f and <= 10f, $"{c.Name}: 하루 수면 {sleep:0.0}시간");
            ok &= Check(minFood[i] > 0.03f, $"{c.Name}: 굶주림 (최저 포만감 {minFood[i]:0.00})");
            ok &= Check(minHealth[i] > 0.6f, $"{c.Name}: 체력 저하 (최저 {minHealth[i]:0.00})");
        }

        Console.WriteLine($"\n공기: 최저 O2 {minO2:0.0}kPa, 최고 CO2 {maxCO2:0.00}kPa · 물 최저 {minWater:0}L · 배터리 최저 {minBattery * 100:0}% · 부하 차단 {shedTicks / (float)SimTime.TicksPerHour:0.0}시간");
        int totalFaults = ship.Machines.Sum(m => m.FaultCount);
        Console.WriteLine($"고장 누적 {totalFaults}건 · 정비 누적 {ship.Machines.Sum(m => m.ServiceCount)}회 · 남은 작업 {world.Board.OpenCount}");
        PrintRobots(world);
        Console.WriteLine($"예방: {world.Precursors}");
        Console.WriteLine($"당직 일지: {world.Watch.Stats}"); // v12.0
        Console.WriteLine($"설비 열·폭발: {world.Volatile.Stats} · 뜨거운 설비 {string.Join(", ", world.Ship.Machines.Where(m => m.Heat > 0.4f).Select(m => $"{m.Name} {m.Heat * 100:0}%"))}"); // v12.2
        Console.WriteLine($"물·습기·전기: {world.Moisture.Stats} · 가장 습한 방 {world.Ship.Rooms.Max(r => r.Humidity) * 100:0}%"); // v12.3
        Console.WriteLine($"승무원 생활: {world.Life.Stats}"); // v12.7
        Console.WriteLine($"항로: {world.Voyage.Number}번째 항해 · {world.Voyage.Index}/{world.Voyage.Legs.Count} 구간({VoyageSystem.KindName(world.Voyage.Current.Kind)}) · 돈 {world.Voyage.Credits:0} · 시대 {EraSystem.EraName(world.Eras.Era)} · 기술 {string.Join(",", world.Eras.Order)}"); // v12.8
        if (world.Hazards.RandomCount > 0 || world.Hazards.Count.Any(n => n > 0))
        {
            // v11.2 무작위 사고 (켜져 있을 때): 무엇이 언제 났고, 사람이 다치거나 쓰러졌나
            var rnd = world.Log.Entries.Where(e => e.Text.StartsWith("무작위 사고:")).Select(e => $"{SimTime.Day(e.Tick)}일 {e.Text["무작위 사고: ".Length..]}").ToList();
            Console.WriteLine($"무작위 사고 {world.Hazards.RandomCount}번 (평균 {HazardSystem.RandomDays:0.#}일): {string.Join(" · ", rnd)}");
            Console.WriteLine($"  식중독 {world.Hazards.Poisoned}명 · 버린 식사 {world.Hazards.FoodDiscarded} · 병충해 잡음 {world.Hazards.BlightCured} · 병충해로 죽은 작물 {world.Hazards.BlightKilled} · 쓰러짐 {world.History.Events.Count(e => e.Kind == HistoryKind.Casualty)} · 사망 {world.Crew.Count(c => c.Dead)}");
        }
        PrintLedger(world, days);
        ok &= Check(minO2 > 17f, $"산소 부족 (최저 {minO2:0.0})");
        ok &= Check(maxCO2 < 1.5f, $"CO2 과다 (최고 {maxCO2:0.00})");
        int foodEnd = ship.CountStored(ItemKind.Meal) + ship.CountStored(ItemKind.Produce);
        ok &= Check(foodEnd >= n * 3, $"식량이 줄어듦 (끝 {foodEnd})");

        Console.WriteLine("\n둘째 날 일과 (30분 단위, 07:00부터)  S수면 E식사 J작업 D당직 R휴식 C대화 w산책 !대피 H치료 ·이동");
        for (int i = 0; i < n; i++)
            Console.WriteLine($"  {world.Crew[i].Name,-6} {new string(timeline[i].ToArray())}");

        if (!quiet)
        {
            Console.WriteLine("\n최근 기록");
            foreach (var e in world.Log.Entries.TakeLast(24))
            {
                string who = e.CrewId >= 0 ? world.Crew[e.CrewId].Name + " " : "";
                Console.WriteLine($"  {SimTime.Day(e.Tick)}일차 {SimTime.Clock(e.Tick)}  [{e.Kind}] {who}{e.Text}");
            }
        }

        if (args.Contains("--chronicle")) PrintChronicle(world, 200);
        if (args.Contains("--evo"))
        {
            Console.WriteLine($"\n진화 후보 (평화 {Evolution.Peaceful(world)} · 마지막 개조 {(world.Tick - world.History.LastUpgradeTick) / (float)SimTime.TicksPerHour:0}시간 전 · 굶주림 {Evolution.StarvedHours(world):0}사람·시간 · 재배대 자리 {(Evolution.GrowSpot(world) is { } gs ? gs.room.Name : "없음")})");
            foreach (var p in Evolution.Candidates(world))
                Console.WriteLine($"  {p.Kind} {p.Score:0.00} 살 수 있나 {Evolution.Affordable(world.Board, p.Cost)} · {p.Why}");
        }
        ok &= Check(Deterministic(seed), "같은 시드인데 결과가 다름 (결정론 깨짐)");
        Console.WriteLine(ok ? "\n✔ 모든 점검 통과" : "\n✘ 점검 실패");
        return ok ? 0 : 1;
    }

    /// <summary>SHIPSIM_DEBUG=1 이면 시나리오 끝에 승무원 판단과 남은 작업의 길찾기를 자세히 보여 준다.</summary>
    private static readonly bool args_debug = Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1";

    /// <summary>시나리오 뒤에 지켜볼 시간 (시간).</summary>
    private const int ScenarioHours = 72;

    /// <summary>
    /// 사고 시험: 하루 돌린 뒤 시나리오를 걸고(Core/Scenarios.cs) 사흘을 지켜본 다음 결과를 네 가지로 나눈다.
    /// 목록은 --scenario=all 또는 Scenarios.All 참고.
    /// </summary>
    private static Outcome? RunScenario(World world, string name, bool verbose)
    {
        var ship = world.Ship;
        for (int i = 0; i < SimTime.TicksPerDay; i++) world.Step();
        long logStart = world.Tick;
        var before = Snapshot.Take(world);
        if (!Scenarios.Apply(world, name, out var focus))
        {
            Console.WriteLine($"알 수 없는 시나리오: {name}");
            return null;
        }
        before.RebaseStock(world);
        if (verbose)
        {
            Console.WriteLine($"── 시나리오 '{name}' ({Scenarios.All.First(x => x.Name == name).Description}) 시작: {world.Day}일차 {world.Clock} ──");
            var walls = ship.Walls.Where(kv => kv.Value.IsHull && kv.Value.StageIndex > 0).ToList();
            if (walls.Count > 0)
                Console.WriteLine($"손상된 외벽 {walls.Count}칸: " + string.Join(", ", walls.GroupBy(kv => kv.Value.Stage).Select(g => $"{g.Key} {g.Count()}")));
            Console.WriteLine("경과    시각   원자로 한계kW 배터리 보조 | 관심방 기압 O2  연기 누출 잠금 | 최저기압 불 탱크%  | 승무원 활동");
        }
        var marks = new List<int> { 5, 10, 15, 20, 30, 45, 60, 90, 120, 180, 240 };
        for (int h = 6; h <= 24; h += 2) marks.Add(h * 60);
        for (int h = 28; h <= ScenarioHours; h += 4) marks.Add(h * 60);
        int elapsed = 0;
        float minHealth = 1f;
        foreach (int m in marks)
        {
            int ticks = SimTime.Minutes(m - elapsed);
            for (int i = 0; i < ticks; i++)
            {
                world.Step();
                foreach (var c in world.Crew) minHealth = MathF.Min(minHealth, c.Vitals.Health);
            }
            elapsed = m;
            if (!verbose) continue;
            var p = world.Power;
            string fr = focus == null ? "" :
                $"{focus.Air.Pressure,5:0} {focus.Air.O2,4:0.0} {focus.Air.Smoke,4:0.00} {(focus.Leaking ? "누출" : "  - "),4} {(focus.Abandoned ? "폐쇄" : focus.Lockdown ? "잠금" : "  - "),4}";
            float minP = ship.Rooms.Where(r => !r.Abandoned).Select(r => r.Air.Pressure).DefaultIfEmpty(0f).Min();
            string acts = string.Join(" ", world.Crew.Select(c => $"{c.Name[1..]}:{CrewState(c)}"));
            Console.WriteLine($"{(m < 60 ? $"{m}분" : $"{m / 60f:0.#}시간"),6} {world.Clock}  {(p.ReactorOnline ? $"{p.ReactorRamp * 100,3:0}%" : "정지"),4} {p.ReactorLimit,6:0.0} {p.BatteryPercent * 100,5:0}% {(p.AuxRunning ? "가동" : "  - "),4} | {focus?.Name,-5} {fr} | {minP,6:0} {world.Fire.Count,3} {world.Air.Reserve / world.Air.ReserveCapacity * 100,5:0}  | {acts}");
            if (Scenarios.Automation.Contains(name))
            {
                var au = world.Automation;
                var br = ship.RoomsOf(RoomType.Bridge).First();
                Console.WriteLine($"        [자동화] {(au.MainOnline ? "켜짐" : au.BackupActive ? "꺼짐(예비)" : "꺼짐")} · 함교 {br.Air.Temperature:0}℃ 댐퍼 {(br.VentOpen ? "열림" : "닫힘")}{(br.DamperStuck ? "(걸림)" : "")} · 컴퓨터 {au.Computer?.StatusText} · 잠긴 문 {ship.Doors.Count(d => d.Locked)} · 정지 {au.OfflineHours:0.0}시간");
            }
            if (Environment.GetEnvironmentVariable("SHIPSIM_PWR") == "1")
                Console.WriteLine($"        [전력] 저출력 {p.LowPowerMode} · 공급 {p.Delivered:0.0}/{p.Demand:0.0}kW · 우선순위 {world.Automation.Priority} · 회로 " + string.Join("", Enumerable.Range(0, 4).Select(i => p.CircuitFed[i] ? "O" : "x")) + " · " +
                                  string.Join(" ", ship.FurnitureOf(FurnitureType.CoolantPump).Select(f => $"{f.Label}:{(f.Machine!.Powered ? "전기" : "무전")}/{(f.Machine.Parked ? "내림" : "")}{f.Machine.Faults.Count}고장/{f.Machine.Efficiency * 100:0}%")));
            if (Scenarios.Piping.Contains(name) || Environment.GetEnvironmentVariable("SHIPSIM_PIPES") == "1")
            {
                var net = world.Piping;
                Console.WriteLine($"        [냉각] 냉각수 {net.CoolantFraction * 100:0}% · 냉각 {net.CoolingKw:0}kW (분기 {net.FlowingBranches}) · 노심 {p.ReactorTemperature:0}℃ · 물 {world.Water.Level:0}L · " +
                                  string.Join(" ", net.Segments.Select(sg => $"{sg.Name.Replace(" ", "")}:{sg.StateText}{(sg.Leaking ? $"({sg.LeakRate:0})" : "")}{(sg.Radiator.Count > 0 && sg.RadiatorCondition < 0.95f ? $"/방열{sg.RadiatorCondition * 100:0}" : "")}")));
            }
            if (Environment.GetEnvironmentVariable("SHIPSIM_JOINTS") is string jr)
                foreach (var r in ship.Rooms.Where(r => r.Name == jr))
                    Console.WriteLine($"        [연결부] {r.Name} 하중 {world.Structure.Stress.GetValueOrDefault(r.Id) * 100:0}% 골조 {world.Structure.FramesLost.GetValueOrDefault(r.Id)} " +
                                      string.Join(" ", r.Joints.Select(j => $"{j.Index}:{j.Strength * 100:0}/{j.Known * 100:0}")) +
                                      " · 드론 " + string.Join(" ", world.Drones.Drones.Select(d => $"{d.Name[..2]}{d.State.ToString()[..3]}{d.Battery * 100:0}")) +
                                      " · 거치대 " + string.Join("/", ship.FurnitureOf(FurnitureType.DroneDock).Select(f => $"S{f.Storage!.Count(ItemKind.Structure)}P{f.Storage.Count(ItemKind.Plate)}")) +
                                      $" · 구조재 {ship.CountStored(ItemKind.Structure)} 금속판 {ship.CountStored(ItemKind.Plate)}");
        }

        var outcome = Assessment.Assess(world, before);
        if (!verbose) return outcome;

        Console.WriteLine("\n기록");
        foreach (var e in world.Log.Entries.Where(e => e.Tick >= logStart && e.Kind != LogKind.Life).Take(90))
        {
            string who = e.CrewId >= 0 ? world.Crew[e.CrewId].Name + " " : "";
            Console.WriteLine($"  {SimTime.Day(e.Tick)}일차 {SimTime.Clock(e.Tick)}  [{e.Kind}] {who}{e.Text}");
        }
        if (Environment.GetEnvironmentVariable("SHIPSIM_GREP") is string grep)
            foreach (var e in world.Log.Entries.Where(e => e.Tick >= logStart && e.Text.Contains(grep)))
                Console.WriteLine($"  [grep] {SimTime.Day(e.Tick)}일차 {SimTime.Clock(e.Tick)} {(e.CrewId >= 0 ? world.Crew[e.CrewId].Name : "")} {e.Text}");
        var hullLeft = ship.Walls.Where(kv => kv.Value.IsHull && kv.Value.StageIndex > 0).GroupBy(kv => kv.Value.Stage).Select(g => $"{g.Key} {g.Count()}");
        Console.WriteLine($"\n최저 체력 {minHealth * 100:0}% · 남은 고장 {ship.Machines.Sum(m => m.Faults.Count)} · 불 {world.Fire.Count} · 외벽 [{string.Join(", ", hullLeft)}] · 사고 대응 {world.Crew.Sum(c => c.Stats.Emergencies)}회 · 구조 {world.Crew.Sum(c => c.Stats.Rescues)}회");
        foreach (var c in world.Crew.Where(c => c.Vitals.Injury > 0.05f || c.Down || c.Dead))
            Console.WriteLine($"  {c.Name}: {(c.Dead ? "사망" : c.Down ? "쓰러짐" : "")} 체력 {c.Vitals.Health * 100:0}% 부상 {c.Vitals.Injury * 100:0}% ({c.Vitals.InjuryCause})");
        foreach (var o in world.Board.Open)
        {
            Console.WriteLine($"  남은 작업: {o.Title} ({o.Detail}) 긴급 {o.Urgency:0.00} 담당 {o.Assignee?.Name ?? "-"}");
            if (args_debug)
                foreach (var c in world.Crew)
                {
                    var f = world.Paths.Flood(c.Cell, c.PathProfile);
                    var spot = Plans.WorkSpot(o.Target, world, f, c);
                    string why = spot is Cell sp ? $"spot {sp} room {world.Ship.RoomAt(sp)?.Name} find {(world.Paths.Find(c.Cell, sp, new PathProfile(c.PathProfile.HazardScale, false, false)) != null)}" : "no spot";
                    Console.WriteLine($"      {c.Name} @{c.Cell} {c.Room?.Name}: {why} suit {c.Suit?.Oxygen}");
                }
        }
        if (args_debug)
            foreach (var c in world.Crew)
                Console.WriteLine($"  {c.Name} [{c.Room?.Name}] {c.ActivityLabel}: " +
                                  string.Join(" / ", c.LastEvaluations.Take(4).Select(e => $"{e.Activity.Label} {e.Score:0.00} ({e.Reason})")));
        Console.WriteLine($"물자: 실링폼 {ship.CountStored(ItemKind.Sealant)} · 금속판 {ship.CountStored(ItemKind.Plate)} · 구조재 {ship.CountStored(ItemKind.Structure)} · 일반 부품 {Tier(ship, ItemTier.General)} · 원자로 제어부 {ship.CountStored(ItemKind.ReactorControl)} · 케이블 {ship.CountStored(ItemKind.Cable)} · 퓨즈 {ship.CountStored(ItemKind.Fuse)} · 연료통 {ship.CountStored(ItemKind.Fuel)} · 소화기 {ship.CountStored(ItemKind.Extinguisher)} · 우주복 {ship.CountStored(ItemKind.Suit)} · 구급키트 {ship.CountStored(ItemKind.MedKit)}");
        Console.WriteLine($"적응: {world.Adapt}");
        Console.WriteLine($"재료: {world.Adapt.Materials}");
        if (args_debug)
            foreach (var f in ship.Containers.Where(f => f.Type != FurnitureType.SuitLocker))
                Console.WriteLine($"  [보관함] {f.Label} ({f.Room.Name}) {f.Storage!.Total}/{f.Storage.Capacity}: {string.Join(", ", f.Storage.Contents.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}"))}");
        if (world.InitialProfile != null) Console.WriteLine($"함선 지표 (처음=100): {ShipProfile.Measure(world).RelativeTo(world.InitialProfile)}");
        PrintStructure(world);
        PrintPipes(world);
        if (Environment.GetEnvironmentVariable("SHIPSIM_CHRONICLE") == "1") PrintChronicle(world, 120);
        Console.WriteLine($"\n결과: {outcome}");
        return outcome;
    }

    /// <summary>
    /// 물자 부족 모드 (v5 끝 기준): 예비 부품·케이블·퓨즈·연료통을 바닥내고, 며칠마다 무작위 사고를 건다.
    /// 우주선이 "다른 모습으로" 살아남는지 — 임시 배선, 뜯긴 설비, 폐쇄 구역, 용도가 바뀐 방 — 를 본다.
    /// </summary>
    private static int RunScarcity(World world, int days, int every, bool quiet)
    {
        var ship = world.Ship;
        Console.WriteLine($"물자 부족 {days}일 · {every}일마다 무작위 사고{(world.CrewCanDie ? " · 사망 켬" : "")}\n");
        for (int t = 0; t < SimTime.TicksPerDay; t++) world.Step();
        Scenarios.Scarcity(world);
        var rng = new Rng(unchecked((int)((uint)world.Rng.Range(0, 1 << 30) * 2654435761u ^ 0x5eed1234u)));
        long logFromS = world.Tick;
        int minAlive = world.Crew.Count;
        int maxDown = 0;
        float minAvgO2 = 99f;
        Console.WriteLine("  일  원자로   배터리 연료 탱크 | 채소 식사 비상 부품 케이블 퓨즈 필터 윤활 연료통 | 뜯김 배선 절전 침실 폐쇄 | 쓰러짐 | 사고");
        for (int day = 1; day <= days; day++)
        {
            string incident = "";
            if (day % every == 0) incident = Scenarios.RandomIncident(world, rng);
            for (int t = 0; t < SimTime.TicksPerDay; t++)
            {
                world.Step();
                if (t % World.SystemInterval != 0) continue;
                maxDown = Math.Max(maxDown, world.Crew.Count(c => c.Down && !c.Dead));
                var living = ship.Rooms.Where(r => !r.Abandoned).ToList();
                float vol = living.Sum(r => r.Volume);
                if (vol > 0) minAvgO2 = MathF.Min(minAvgO2, living.Sum(r => r.Air.O2 * r.Volume) / vol);
            }
            minAlive = Math.Min(minAlive, world.Crew.Count(c => !c.Dead));
            var p = world.Power;
            if (Environment.GetEnvironmentVariable("SHIPSIM_POWERDAYS") is string pds && pds.Split(',').Contains(day.ToString()))
                PrintPowerBreakdown(world);
            string reactor = !p.ReactorOnline ? "정지" : p.LowPowerMode ? "저출력" : $"{p.ReactorLimit:0}kW";
            Console.WriteLine($"  {day,2}  {reactor,-6} {p.BatteryPercent * 100,4:0}% {p.AuxFuel,4:0} {world.Air.Reserve / world.Air.ReserveCapacity * 100,3:0}% | {ship.CountStored(ItemKind.Produce),4} {ship.CountStored(ItemKind.Meal),4} {ship.CountStored(ItemKind.Ration),4} {Tier(ship, ItemTier.General),4} {ship.CountStored(ItemKind.Cable),6} {ship.CountStored(ItemKind.Fuse),4} {ship.CountStored(ItemKind.Filter),4} {ship.CountStored(ItemKind.Lubricant),4} {ship.CountStored(ItemKind.Fuel),6} | " +
                              $"{ship.Machines.Count(m => m.Has(FaultKind.Stripped)),4} {p.Jumpers.Count(j => j.Active),4} {Enumerable.Range(0, 4).Count(i => p.ManualOff[i]),4} {ship.Rooms.Count(r => r.Purpose == "임시 침실"),4} {ship.Rooms.Count(r => r.Abandoned),4} | {world.Crew.Count(c => c.Down && !c.Dead),4}   | {incident}");
        }

        var outcome = Assessment.Assess(world);
        if (int.TryParse(Environment.GetEnvironmentVariable("SHIPSIM_LOGDAY"), out var logDay))
        {
            Console.WriteLine($"\n{logDay}일차부터 사흘 기록");
            foreach (var e in world.Log.Entries.Where(e => e.Kind != LogKind.Life && SimTime.Day(e.Tick) - 1 >= logDay && SimTime.Day(e.Tick) - 1 < logDay + 3))
                Console.WriteLine($"  {SimTime.Day(e.Tick) - 1}일 {SimTime.Clock(e.Tick)} {(e.CrewId >= 0 ? world.Crew[e.CrewId].Name : "")} {e.Text}");
            foreach (var o in world.Board.Open)
                Console.WriteLine($"  남은 작업: {o.Title} ({o.Detail}) 긴급 {o.Urgency:0.00} 담당 {o.Assignee?.Name ?? "-"} 보류 {o.BlockedReason}");
            foreach (var r in ship.Rooms)
                Console.WriteLine($"  {r.Name}: {r.Air.Pressure:0}kPa O2 {r.Air.O2:0.0} {(r.Leaking ? "누출" : "")} {(r.Abandoned ? "폐쇄" : "")} 회로 {PowerGrid.CircuitName(r.Circuit)} 전기 {r.Powered}");
            foreach (var f in ship.Containers.Where(f => f.Storage!.Count(ItemKind.Fuel) > 0))
                Console.WriteLine($"  연료통 {f.Storage!.Count(ItemKind.Fuel)}개: {f.Label} ({f.Room.Name})");
            foreach (var m in ship.Machines.Where(m => m.Spec.Critical || m.Body.Type is FurnitureType.AuxGenerator or FurnitureType.GrowBed or FurnitureType.Stove or FurnitureType.Workbench))
                Console.WriteLine($"  {m.Name} [{m.Body.Room.Name}] {m.StatusText} 효율 {m.Efficiency:0.00}" + (m.Crop is CropState cr ? $" 생장 {cr.Growth:0.00} 돌봄 {cr.Care:0.00}" : ""));
        }
        Console.WriteLine($"\n적응 누적: {world.Adapt}");
        Console.WriteLine($"재료 누적: {world.Adapt.Materials} · 채집 {world.Collection.Collected}");
        if (world.InitialProfile != null) Console.WriteLine($"함선 지표 (처음=100): {ShipProfile.Measure(world).RelativeTo(world.InitialProfile)}");
        Console.WriteLine($"뜯긴 설비: {string.Join(", ", ship.Machines.Where(m => m.Has(FaultKind.Stripped)).Select(m => $"{m.Name}({ItemKinds.Name(m.Faults.First(f => f.Kind == FaultKind.Stripped).Part ?? ItemKind.Motor)})"))}");
        Console.WriteLine($"임시 배선: {string.Join(", ", world.Power.Jumpers.Select(j => $"{PowerGrid.CircuitName(j.From)}→{PowerGrid.CircuitName(j.To)}{(j.Burnt ? "(탐)" : j.Active ? "(사용 중)" : "(흔적)")}"))}");
        Console.WriteLine($"용도가 바뀐 방: {string.Join(", ", ship.Rooms.Where(r => r.Purpose != null).Select(r => $"{r.Name}={r.Purpose}"))} · 간이침대 {ship.FurnitureOf(FurnitureType.Cot).Count()}개");
        Console.WriteLine($"폐쇄 구역: {string.Join(", ", ship.Rooms.Where(r => r.Abandoned).Select(r => r.Name))}");
        Console.WriteLine($"최소 생존 {minAlive}/{world.Crew.Count} · 동시에 쓰러진 최대 {maxDown} · 생활 구역 평균 O2 최저 {minAvgO2:0.0}kPa");
        if (!quiet)
        {
            Console.WriteLine("\n적응 기록");
            foreach (var e in world.Log.Entries.Where(e => e.Tick >= logFromS && IsAdaptLog(e.Text)).Take(80))
                Console.WriteLine($"  {SimTime.Day(e.Tick)}일차 {SimTime.Clock(e.Tick)} {(e.CrewId >= 0 ? world.Crew[e.CrewId].Name : "")} {e.Text}");
        }
        PrintPipes(world);
        if (Environment.GetEnvironmentVariable("SHIPSIM_CHRONICLE") == "1") PrintChronicle(world, 160);
        Console.WriteLine($"\n결과: {outcome}");
        Console.WriteLine($"생명유지 내역: {ShipProfile.LifeBreakdown(world)}");
        // v10.3: 종료 코드 0은 "시험을 끝까지 돌렸다"는 뜻이다. 우주선이 무너진 건 게임 결과 (극한 생존 시험의 기준은 따로 본다)
        Console.WriteLine("(시험 실행: 끝까지 돌렸다 · 위 결과는 게임 결과이며 시험 실패가 아니다)");
        return 0;
    }

    /// <summary>
    /// 회복 리듬 (v6): 하루 뒤 큰 사고(기본 chaos)를 한 번 겪고, 그 뒤 평화로운 날들 동안 재료가 어떻게 다시 쌓이는지 본다.
    /// 리뷰어 안: "손상 → 대량 소비 → 회복 기간 → 다시 비축".
    /// </summary>
    private static int RunRhythm(World world, int days, string scenario, bool quiet)
    {
        var ship = world.Ship;
        for (int t = 0; t < SimTime.TicksPerDay; t++) world.Step();
        var start = ShipProfile.Measure(world);
        if (!Scenarios.Apply(world, scenario, out _)) { Console.WriteLine($"알 수 없는 시나리오: {scenario}"); return 2; }
        Console.WriteLine($"회복 리듬: 1일차 끝에 '{scenario}' → {days}일 평화 · 시드 {world.Seed}\n");
        Console.WriteLine("  일 탱크 실링 판 구조 케이블 전자 부품 원료 채집 | 외벽 최대강도(평균·최저) 용접 교체 | Mk1 | 수리력 구조 생명유지 전력");
        var basis = world.InitialProfile!;
        long logFrom = world.Tick;
        for (int day = 0; day <= days; day++)
        {
            if (day > 0) for (int t = 0; t < SimTime.TicksPerDay; t++) world.Step();
            var hull = ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Value).ToList();
            var prof = ShipProfile.Measure(world).RelativeTo(basis);
            Console.WriteLine($"  {day,2} {world.Air.Reserve / world.Air.ReserveCapacity * 100,3:0}% {ship.CountStored(ItemKind.Sealant),4} {ship.CountStored(ItemKind.Plate),3} {ship.CountStored(ItemKind.Structure),4} " +
                              $"{ship.CountStored(ItemKind.Cable),6} {ship.CountStored(ItemKind.Electronics),4} {Tier(ship, ItemTier.General),4} {Tier(ship, ItemTier.Raw),4} {world.Collection.Collected,4} | " +
                              $"{hull.Average(x => x.MaxIntegrity) * 100,5:0}% {hull.Min(x => x.MaxIntegrity) * 100,4:0}% {hull.Sum(x => x.Welds),4} {hull.Sum(x => x.Replacements),4} | " +
                              $"{ship.Machines.Count(m => m.Grade == MachineGrade.Mk1),3} | {prof.Repair,5:0} {prof.Structure,4:0} {prof.LifeSupport,6:0} {prof.Power,4:0}");
        }
        Console.WriteLine($"\n재료 누적: {world.Adapt.Materials} · 채집 {world.Collection.Collected} · 주변 밀도 {world.Space.Density:0.00} ({world.Space.DensityName})");
        Console.WriteLine($"적응 누적: {world.Adapt}");
        Console.WriteLine($"함선 지표 (처음=100): {ShipProfile.Measure(world).RelativeTo(basis)}");
        foreach (var m in ship.Machines.Where(m => m.Body.Type is FurnitureType.Collector or FurnitureType.Refinery or FurnitureType.Workbench))
            Console.WriteLine($"  {m.Name} [{m.Body.Room.Name}] {m.StatusText} · 효율 {m.Efficiency:0.00} · 전기 {m.Powered} · 등급 {Grades.Name(m.Grade)}");
        if (args_debug)
            foreach (var o in world.Board.Open)
                Console.WriteLine($"  남은 작업: {o.Title} ({o.Detail}) 긴급 {o.Urgency:0.00} 담당 {o.Assignee?.Name ?? "-"} 보류 {o.BlockedReason}");
        if (!quiet)
        {
            Console.WriteLine("\n재료 기록 (정제·제작·교체·복원)");
            foreach (var e in world.Log.Entries.Where(e => e.Tick >= logFrom).Where(e => e.Text.Contains("정제해") || e.Text.Contains("만들었다 (") || e.Text.Contains("패널을")
                         || e.Text.Contains("정품으로") || e.Text.Contains("Mk.1") || e.Text.Contains("녹여") || e.Text.Contains("다시 짜")).Take(60))
                Console.WriteLine($"  {SimTime.Day(e.Tick) - 1}일 {SimTime.Clock(e.Tick)} {(e.CrewId >= 0 ? world.Crew[e.CrewId].Name : "")} {e.Text}");
        }
        Console.WriteLine($"\n결과: {Assessment.Assess(world)}");
        return 0;
    }

    /// <summary>전력 배분 한눈에: 원자로 한계·수요·공급, 설비마다 우선순위·수요·전기 (물자 부족 시험 진단용).</summary>
    private static void PrintPowerBreakdown(World w)
    {
        var p = w.Power;
        Console.WriteLine($"    [전력] 한계 {p.ReactorLimit:0.0}kW · 수요 {p.Demand:0.0} · 공급 {p.Delivered:0.0} · 배터리 {p.BatteryPercent * 100:0}% ({p.BatteryFlow:+0.0;-0.0}kW) · 냉각 {p.CoolingCapacity:0}kW" +
                          $" · 끈 회로 {string.Join("", Enumerable.Range(0, PowerGrid.CircuitCount).Where(i => p.ManualOff[i]).Select(PowerGrid.CircuitName))} · 자동화 {(w.Automation.MainOnline ? "켜짐" : "꺼짐")}");
        foreach (var g in w.Ship.Machines.Where(m => m.Spec.PowerDraw > 0f).GroupBy(m => m.Body.Type).OrderByDescending(g => g.First().Spec.Priority))
            Console.WriteLine($"    [전력]   {FurnitureTypes.Name(g.Key),-8} 우선 {g.First().Spec.Priority,2} · 수요 {g.Sum(m => m.Demand):0.0}kW · 전기 {g.Count(m => m.Powered)}/{g.Count()}" +
                              $" · 고장 {g.Count(m => m.Faults.Count > 0)}");
    }

    private static int Tier(Ship ship, ItemTier tier) => ItemKinds.All.Where(k => ItemKinds.Tier(k) == tier).Sum(ship.CountStored);

    // ═════════════════════════════ v7 역사와 진화 ═════════════════════════════

    private static void PrintChronicle(World w, int max)
    {
        Console.WriteLine($"\n연대기 ({w.History.Events.Count}줄 · 사고 {w.History.Episodes.Count}건)");
        foreach (var e in w.History.Events.TakeLast(max))
            Console.WriteLine($"  {SimTime.Day(e.Tick),2}일 {SimTime.Clock(e.Tick)} [{Kind(e.Kind)}] {e.Text}");
    }

    private static string Kind(HistoryKind k) => k switch
    {
        HistoryKind.Incident => "사고",
        HistoryKind.Damage => "피해",
        HistoryKind.Casualty => "부상",
        HistoryKind.Death => "사망",
        HistoryKind.Response => "대응",
        HistoryKind.Adaptation => "적응",
        HistoryKind.Decision => "결정",
        HistoryKind.Recovery => "수습",
        HistoryKind.Upgrade => "개조",
        HistoryKind.Lesson => "교훈",
        HistoryKind.Bond => "유대",
        HistoryKind.Memory => "기억",
        HistoryKind.Structure => "구조",
        _ => "기록",
    };

    /// <summary>게이트 시험에 쓰는 사고 네 가지 (대상이 정해져 있어 순서만 바꿀 수 있다).</summary>
    private static readonly (string Name, Action<World> Apply)[] GateIncidents =
    {
        ("창고 큰 운석", w => Player.Meteor(w, Scenarios.OuterTarget(w, RoomType.Storage), 1f)),
        ("주방 화재", w => Player.Fire(w, w.Ship.RoomsOf(RoomType.Galley).First().Cells.First(w.Ship.IsOpenFloor))),
        ("냉각 펌프 둘 고착", w => Player.BreakAll(w, "CoolantPump", true, FaultKind.PumpSeized)),
        ("침실 운석", w => Player.Meteor(w, Scenarios.OuterTarget(w, RoomType.Quarters), 0.8f)),
        ("배전실 운석", w => Player.Meteor(w, Scenarios.OuterTarget(w, RoomType.Power), 0.9f)),
        ("식당 화재", w => Player.Fire(w, w.Ship.RoomsOf(RoomType.Mess).First().Cells.First(w.Ship.IsOpenFloor))),
        ("수경재배실 운석", w => Player.Meteor(w, Scenarios.OuterTarget(w, RoomType.Hydroponics), 0.9f)),
    };

    private sealed record GateResult(string Label, World World, ShipProfile Profile, Outcome Outcome);

    /// <summary>
    /// 게이트 (로드맵 v7): 같은 시드에서 사고 순서만 바꿔도 한 달 뒤 우주선과 승무원이 눈에 띄게 달라지고, 어떤 지표는 처음보다 높아진다.
    /// mode=order: 같은 네 사고를 네 가지 순서로 (2·6·10·14일). mode=kind: 겪는 사고의 종류를 바꿔서 (운석 많은 배, 불 많은 배, 정전 많은 배).
    /// </summary>
    private static int RunGate(int days, int seed, string mode, bool quiet)
    {
        if (days < 20) days = 30;
        // 사고 시각: 2일 07시, 6일 13시, 10일 19시, 14일 01시 (누가 깨어 있고 어디 있느냐가 매번 다르다)
        (int day, int hour)[] at = { (2, 7), (6, 13), (10, 19), (14, 1) };
        var plans = mode == "kind"
            ? new (string label, int[] order)[] { ("운석 많은 배", new[] { 0, 3, 6, 4 }), ("불 많은 배", new[] { 1, 5, 1, 5 }), ("정전 많은 배", new[] { 2, 4, 2, 4 }) }
            : new (string label, int[] order)[] { ("가 순서", new[] { 0, 1, 2, 3 }), ("나 순서", new[] { 3, 2, 1, 0 }), ("다 순서", new[] { 2, 0, 3, 1 }), ("라 순서", new[] { 1, 3, 0, 2 }) };
        Console.WriteLine($"게이트 ({(mode == "kind" ? "겪는 사고의 종류" : "사고 순서")}) · 시드 {seed} · {days}일 · 사고 {string.Join("·", at.Select(a => $"{a.day}일 {a.hour:00}시"))}\n");
        var results = new List<GateResult>();
        int onlyPlan = int.TryParse(Environment.GetEnvironmentVariable("SHIPSIM_GATEPLAN"), out var gp) ? gp : 0;
        int planIndex = 0;
        foreach (var (label, order) in plans)
        {
            if (onlyPlan > 0 && ++planIndex != onlyPlan) continue;
            var w = World.CreateDefault(seed);
            w.Log.Capacity = 20000;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            long end = (long)days * SimTime.TicksPerDay + SimTime.Hours(7);
            for (int k = 0; k < at.Length; k++)
            {
                long when = (long)(at[k].day - 1) * SimTime.TicksPerDay + SimTime.Hours(at[k].hour);
                while (w.Tick < when) w.Step();
                if (onlyPlan > 0)
                    foreach (var r in w.Ship.Rooms.Where(r => r.Joints.Any(j => j.Strength < 0.95f)))
                        Console.WriteLine($"   [{SimTime.Day(w.Tick)}일 사고 전] {r.Name} 연결부 " + string.Join(" ", r.Joints.Select(j => $"{j.Strength * 100:0}/{j.Known * 100:0}")));
                GateIncidents[order[k]].Apply(w);
                if (onlyPlan > 0)
                    foreach (var r in w.Ship.Rooms.Where(r => r.Joints.Any(j => j.Strength < 0.95f)))
                        Console.WriteLine($"   [{SimTime.Day(w.Tick)}일 사고 뒤] {r.Name} 연결부 " + string.Join(" ", r.Joints.Select(j => $"{j.Strength * 100:0}/{j.Known * 100:0}")));
            }
            while (w.Tick < end) w.Step();
            var prof = ShipProfile.Measure(w).RelativeTo(w.InitialProfile!);
            var outcome = Assessment.Assess(w);
            results.Add(new GateResult(label, w, prof, outcome));
            if (onlyPlan > 0 && int.TryParse(Environment.GetEnvironmentVariable("SHIPSIM_LOGDAY"), out var gd))
                foreach (var e in w.Log.Entries.Where(e => e.Kind != LogKind.Life && SimTime.Day(e.Tick) >= gd && SimTime.Day(e.Tick) < gd + 3))
                    Console.WriteLine($"  {SimTime.Day(e.Tick)}일 {SimTime.Clock(e.Tick)} {(e.CrewId >= 0 ? w.Crew[e.CrewId].Name : "")} {e.Text}");
            Console.WriteLine($"── {label}: {string.Join(" → ", order.Select(i => GateIncidents[i].Name))} ({watch.ElapsedMilliseconds / 1000.0:0.0}초)");
            Describe(w, prof, outcome);
            if (!quiet)
            {
                Console.WriteLine("   연대기 발췌 (결정·개조·교훈·유대·기억·수습):");
                foreach (var e in w.History.Events.Where(e => e.Kind is HistoryKind.Decision or HistoryKind.Upgrade or HistoryKind.Lesson
                             or HistoryKind.Bond or HistoryKind.Memory or HistoryKind.Recovery or HistoryKind.Death).Take(40))
                    Console.WriteLine($"     {SimTime.Day(e.Tick),2}일 {SimTime.Clock(e.Tick)} [{Kind(e.Kind)}] {e.Text}");
            }
            Console.WriteLine();
        }

        // ── 비교: 무엇이 달라졌나 ──
        Console.WriteLine("비교 (처음 = 100)");
        Console.WriteLine($"  {"",-12} " + string.Join(" ", ShipProfile.Names.Select(n => $"{n,6}")) + "  개조 교훈 결정(부결) 전우쌍 공포(≥35%) 긴장(≥15%) 평균 침착 Mk3 보강칸 배터리");
        foreach (var r in results)
        {
            var w = r.World;
            var h = w.History;
            int pairs = w.Crew.Sum(c => c.Memory.Comrades.Count) / 2;
            int fears = w.Crew.Sum(c => c.Memory.Fear.Count(f => f >= 0.35f));
            int tense = w.Crew.Count(c => c.Memory.Trauma >= 0.15f);
            Console.WriteLine($"  {r.Label,-12} " + string.Join(" ", r.Profile.Values.Select(v => $"{v,6:0}")) +
                              $"  {h.Upgrades,4} {h.Doctrine.Summary().Count(),4} {h.DecisionsMade,4}({h.DecisionsRejected}) {pairs,6} {fears,10} {tense,10} {w.Crew.Average(c => c.Traits.Calm),9:0.00}" +
                              $" {w.Ship.Machines.Count(m => m.Grade == MachineGrade.Mk3),3} {w.Ship.Walls.Count(kv => kv.Value.Reinforced),6} {w.Ship.FurnitureOf(FurnitureType.Battery).Count(),6}");
        }
        int distinctPairs = 0, total = 0;
        for (int i = 0; i < results.Count; i++)
        for (int j = i + 1; j < results.Count; j++)
        {
            total++;
            var diff = Differences(results[i], results[j]);
            if (diff.Count >= 2) distinctPairs++;
            Console.WriteLine($"  {results[i].Label} ↔ {results[j].Label}: 다른 점 {diff.Count}가지 — {string.Join(" / ", diff.Take(6))}");
        }
        bool above = results.All(r => r.Profile.Values.Any(v => v > 100.5f));
        Console.WriteLine($"\n게이트: 눈에 띄게 다른 쌍 {distinctPairs}/{total} · 처음보다 높아진 지표가 있는 배 {results.Count(r => r.Profile.Values.Any(v => v > 100.5f))}/{results.Count}");
        bool pass = distinctPairs == total && above;
        Console.WriteLine(pass ? "✔ 게이트 통과" : "✘ 게이트 미달");
        return pass ? 0 : 1;
    }

    private static void Describe(World w, ShipProfile prof, Outcome outcome)
    {
        var h = w.History;
        Console.WriteLine($"   함선 지표: {prof}");
        Console.WriteLine($"   생명유지 내역: {ShipProfile.LifeBreakdown(w)}");
        Console.WriteLine($"   결과: {outcome}");
        var ups = h.Events.Where(e => e.Kind == HistoryKind.Upgrade).Select(e => $"{SimTime.Day(e.Tick)}일 {e.Text}").ToList();
        Console.WriteLine($"   개조 {h.Upgrades}: " + (ups.Count > 0 ? string.Join(" | ", ups) : "없음"));
        Console.WriteLine($"   교훈: {string.Join(", ", h.Doctrine.Summary().DefaultIfEmpty("없음"))} · 사고 {h.Episodes.Count}건 · 파공 {h.Breaches} · 화재 {h.Fires} · 긴급 정지 {h.Scrams} · 회로 {h.CircuitFaults} · 캄캄 {h.DarkHours:0}시간");
        Console.WriteLine($"   결정 {h.DecisionsMade} (부결 {h.DecisionsRejected}) · 포기 {h.Abandons} · 용도: {string.Join(", ", w.Ship.Rooms.Where(r => r.Purpose != null).Select(r => $"{r.Name}={r.Purpose}").DefaultIfEmpty("-"))}");
        foreach (var c in w.Crew)
        {
            var mem = Memory.Describe(c, w).ToList();
            Console.WriteLine($"   {c.Name,-4} 침착 {c.Traits.Calm:0.00} 긴장 {c.Memory.Trauma:0.00} 스트레스 {c.Needs.Stress:0.00}" + (c.Dead ? " (사망)" : "") +
                              (mem.Count > 0 ? " · " + string.Join(" · ", mem) : ""));
        }
    }

    /// <summary>두 우주선이 무엇이 다른지 (한 달 뒤).</summary>
    private static List<string> Differences(GateResult a, GateResult b)
    {
        var d = new List<string>();
        var wa = a.World;
        var wb = b.World;
        for (int i = 0; i < ShipProfile.Names.Length; i++)
            if (MathF.Abs(a.Profile.Values[i] - b.Profile.Values[i]) >= 4f)
                d.Add($"{ShipProfile.Names[i]} {a.Profile.Values[i]:0}↔{b.Profile.Values[i]:0}");
        string Ups(World w) => string.Join(",", w.History.Events.Where(e => e.Kind == HistoryKind.Upgrade).Select(e => e.Text.Split('—')[0]).OrderBy(x => x));
        if (Ups(wa) != Ups(wb)) d.Add("개조가 다름");
        if (string.Join(",", wa.History.Doctrine.Summary()) != string.Join(",", wb.History.Doctrine.Summary())) d.Add("교훈이 다름");
        string Pairs(World w) => string.Join(",", w.Crew.SelectMany(c => c.Memory.Comrades.Where(o => o > c.Id).Select(o => $"{c.Id}-{o}")));
        if (Pairs(wa) != Pairs(wb)) d.Add("전우가 다름");
        string Fears(World w) => string.Join(",", w.Crew.Select(c => c.Memory.WorstFear() is { } f && f.fear >= 0.2f ? $"{c.Id}:{f.room}" : "-"));
        if (Fears(wa) != Fears(wb)) d.Add("무서워하는 방이 다름");
        string Tense(World w) => string.Join(",", w.Crew.Select(c => c.Memory.Trauma >= 0.12f ? "1" : "0"));
        if (Tense(wa) != Tense(wb)) d.Add("긴장한 사람이 다름");
        string Rooms(World w) => string.Join(",", w.Ship.Rooms.Select(r => $"{r.Abandoned}{r.Purpose}"));
        if (Rooms(wa) != Rooms(wb)) d.Add("방 모습이 다름");
        string Grades(World w) => string.Join(",", w.Ship.Machines.Select(m => (int)m.Grade));
        if (Grades(wa) != Grades(wb)) d.Add("설비 등급이 다름");
        if (wa.Crew.Count(c => c.Dead) != wb.Crew.Count(c => c.Dead)) d.Add("살아남은 사람 수가 다름");
        return d;
    }

    /// <summary>
    /// 저장·불러오기 시험: days일 동안 무작위 사고를 겪게 한 뒤 저장하고, 저장 파일만으로 다시 만든 우주선이 한 틱도 어긋나지 않는지 본다.
    /// path를 주면 그 파일에 저장한다 (다른 프로세스에서 --load=path 로 확인).
    /// </summary>
    private static int RunSaveTest(int days, int seed, string? path)
    {
        var w = World.CreateDefault(seed);
        var rng = new Rng(seed ^ 0x5a5a5a);
        for (int day = 1; day <= days; day++)
        {
            if (day % 3 == 0) Scenarios.RandomIncident(w, rng);
            for (int t = 0; t < SimTime.TicksPerDay; t++) w.Step();
        }
        // 틱 한가운데서도 (하루 경계가 아닌 곳)
        for (int t = 0; t < 12345; t++) w.Step();
        string text = SaveGame.Write(w);
        uint hash = SaveGame.StateHash(w);
        Console.WriteLine($"저장: {w.Day}일차 {w.Clock} · 틱 {w.Tick} · 관찰자 기록 {w.Commands.Count}줄 · 지문 {hash:x8} · 연대기 {w.History.Events.Count}줄 · 파일 {text.Length}바이트");
        if (path != null) System.IO.File.WriteAllText(path, text);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var runner = new ReplayRunner(text);
        while (!runner.Advance(50000)) { }
        bool verified = runner.Verified; // v10.3: 불러온 그 자리에서 (하루 더 돌린 뒤 저장 때 지문과 견주던 판정 오류)
        Console.WriteLine($"불러오기: {watch.ElapsedMilliseconds}ms · 지문 {SaveGame.StateHash(runner.World):x8} · 연대기 {runner.World.History.Events.Count}줄 → {(verified ? "✔ 같은 역사" : "✘ 어긋남")}");
        // 불러온 뒤 계속 돌려도 같은지
        for (int t = 0; t < SimTime.TicksPerDay; t++) { w.Step(); runner.World.Step(); }
        bool same = SaveGame.StateHash(w) == SaveGame.StateHash(runner.World);
        Console.WriteLine($"하루 더: {(same ? "✔ 여전히 같다" : "✘ 갈라졌다")}");
        // v10 되감기: 마지막 관찰자 기록 뒤 어느 틱으로 되감아 다시 흘려도 같은 역사
        long back = Math.Max(w.Commands.Count > 0 ? w.Commands.Max(c => c.Tick) + 1 : 0, w.Tick - SimTime.Hours(30));
        var rewind = new ReplayRunner(SaveGame.WriteAt(w, back));
        while (!rewind.Advance(50000)) { }
        while (rewind.World.Tick < w.Tick) rewind.World.Step();
        bool rewound = rewind.Rewind && SaveGame.StateHash(rewind.World) == SaveGame.StateHash(w);
        Console.WriteLine($"되감기: {SimTime.Day(back)}일 {SimTime.Clock(back)}로 되감아 다시 흘림 → {(rewound ? "✔ 같은 역사" : "✘ 갈라졌다")}");
        // v10.3: 사고 **앞으로** 되감아도 그 사고가 다시 일어나 같은 역사가 된다
        bool before = true;
        if (w.Commands.Count > 0)
        {
            long t0 = Math.Max(0, w.Commands[^1].Tick - SimTime.Minutes(3));
            var again = new ReplayRunner(SaveGame.WriteAt(w, t0));
            while (!again.Advance(50000)) { }
            int upcoming = again.Upcoming;
            while (again.World.Tick < w.Tick) again.World.Step();
            before = upcoming > 0 && SaveGame.StateHash(again.World) == SaveGame.StateHash(w);
            Console.WriteLine($"사고 직전 되감기: {SimTime.Day(t0)}일 {SimTime.Clock(t0)} · 다시 일어날 사고 {upcoming}건 → {(before ? "✔ 같은 역사" : "✘ 갈라졌다")}");
        }
        return verified && same && rewound && before ? 0 : 1;
    }

    private static int RunLoad(string path)
    {
        var text = System.IO.File.ReadAllText(path);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var runner = new ReplayRunner(text);
        while (!runner.Advance(50000)) { }
        var w = runner.World;
        Console.WriteLine($"불러오기 ({path}): {w.Day}일차 {w.Clock} · {watch.ElapsedMilliseconds}ms · 지문 {SaveGame.StateHash(w):x8} / 저장 {runner.ExpectedHash:x8} → {(runner.Verified ? "✔ 같은 역사" : "✘ 어긋남")}");
        return runner.Verified ? 0 : 1;
    }

    private static bool IsAdaptLog(string t) =>
        t.Contains("뜯어") || t.Contains("임시 배선") || t.Contains("일부러 내렸다") || t.Contains("다시 올렸다") || t.Contains("간이침대")
        || t.Contains("수동 기동") || t.Contains("바이오 연료") || t.Contains("연료통") || t.Contains("포기했다") || t.Contains("저출력");

    private static string CrewState(CrewMember c) =>
        c.Dead ? "사망" : c.Down ? (c.CarriedBy != null ? "업혀감" : "쓰러짐") : c.ActivityLabel + (c.Suit != null ? "(복)" : "");

    private static int RunAllScenarios(int seed, bool death, string[] names)
    {
        Console.WriteLine($"시나리오 {names.Length}개 · 각각 {ScenarioHours}시간 · 시드 {seed}{(death ? " · 사망 켬" : "")}\n");
        var tally = new Dictionary<OutcomeKind, int>();
        foreach (var name in names)
        {
            var w = World.CreateDefault(seed);
            w.CrewCanDie = death;
            var outcome = RunScenario(w, name, verbose: false)!;
            tally[outcome.Kind] = tally.GetValueOrDefault(outcome.Kind) + 1;
            Console.WriteLine($"  {name,-10} {Outcome.Name(outcome.Kind),-6} {string.Join(", ", outcome.Notes)}");
        }
        Console.WriteLine("\n" + string.Join(" · ", Enum.GetValues<OutcomeKind>().Select(k => $"{Outcome.Name(k)} {tally.GetValueOrDefault(k)}")));
        return 0;
    }

    /// <summary>
    /// 무작위 사고 실험: 시드마다 하루 뒤 무작위 사고 묶음을 걸고 사흘 뒤 결과를 센다.
    /// waves가 2 이상이면 하루 간격으로 사고 묶음을 여러 번 걸고(되풀이되는 사고 → 물자가 줄어든다) 마지막 뒤 사흘을 본다.
    /// </summary>
    private static int RunExperiment(int runs, int seed, bool death, int waves)
    {
        Console.WriteLine($"무작위 사고 {runs}회 · 사고 묶음 {waves}번(하루 간격) · 마지막 뒤 {ScenarioHours}시간{(death ? " · 사망 켬" : "")}\n");
        var tally = new Dictionary<OutcomeKind, int>();
        int only = int.TryParse(Environment.GetEnvironmentVariable("SHIPSIM_RUN"), out var r) ? r : 0;
        for (int i = 0; i < runs; i++)
        {
            if (only > 0 && i + 1 != only) continue;
            int s = seed + i * 7919;
            var w = World.CreateDefault(s);
            w.CrewCanDie = death;
            for (int t = 0; t < SimTime.TicksPerDay; t++) w.Step();
            var rng = new Rng(unchecked((int)((uint)s * 2654435761u ^ 0x5eed1234u)));
            var before = Snapshot.Take(w);
            var what = new List<string>();
            for (int k = 0; k < waves; k++)
            {
                if (k > 0) for (int t = 0; t < SimTime.TicksPerDay; t++) w.Step();
                what.Add(Scenarios.RandomIncident(w, rng));
            }
            for (int t = 0; t < SimTime.Hours(ScenarioHours); t++) w.Step();
            var outcome = Assessment.Assess(w, before);
            tally[outcome.Kind] = tally.GetValueOrDefault(outcome.Kind) + 1;
            Console.WriteLine($"  {i + 1,2}. {string.Join(" / ", what)}");
            Console.WriteLine($"      → {Outcome.Name(outcome.Kind)}{(outcome.Notes.Count > 0 ? " — " + string.Join(", ", outcome.Notes) : "")}");
            if (Environment.GetEnvironmentVariable("SHIPSIM_RUN") == (i + 1).ToString())
                foreach (var e in w.Log.Entries.Where(e => e.Kind != LogKind.Life && e.Tick >= SimTime.TicksPerDay))
                    Console.WriteLine($"        {SimTime.Day(e.Tick)}일차 {SimTime.Clock(e.Tick)} {(e.CrewId >= 0 ? w.Crew[e.CrewId].Name : "")} {e.Text}");
        }
        Console.WriteLine("\n" + string.Join(" · ", Enum.GetValues<OutcomeKind>().Select(k => $"{Outcome.Name(k)} {tally.GetValueOrDefault(k)} ({tally.GetValueOrDefault(k) * 100 / Math.Max(1, runs)}%)")));
        return 0;
    }

    private static char Symbol(CrewMember c)
    {
        if (c.IsMoving) return '·';
        return c.Job?.Activity?.Id switch
        {
            "sleep" => 'S',
            "eat" => 'E',
            "chores" => 'J',
            "duty" => 'D',
            "relax" => 'R',
            "chat" => 'C',
            "wander" => 'w',
            "evacuate" => '!',
            "recover" => 'H',
            "stowsuit" => 'u',
            _ => '·',
        };
    }

    private static bool Check(bool condition, string message)
    {
        if (!condition) Console.WriteLine($"  ⚠ {message}");
        return condition;
    }

    private static bool Deterministic(int seed)
    {
        string Run()
        {
            var w = World.CreateDefault(seed);
            for (int i = 0; i < SimTime.TicksPerDay / 2; i++) w.Step();
            return string.Join("|", w.Crew.Select(c => $"{c.Position.X:0.000},{c.Position.Y:0.000},{c.Needs.Food:0.0000}"));
        }
        return Run() == Run();
    }
}
