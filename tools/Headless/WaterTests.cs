using System;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// v19 물 순환 점검 (--watertest): 깨끗한 물 → 씀 → 배수구 → 회수관 → 오수 탱크 → 정수기 → 깨끗한 물.
/// 평소 균형 · 회수관이 막히면 (정수기는 멀쩡한데 물이 준다) 컴퓨터가 짚고 · 임시 회수통 · 뚫기 · 컴퓨터 없이 사람이 의심 · 예방 역세척.
/// </summary>
public static partial class Program
{
    private static int RunWaterTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"물 · 산소 순환 점검 (v19) · 시드 {seed}\n");
        WaterSystem.NoComputer = false;
        WaterSystem.HoldClear = false;

        // 1) 평소: 쓴 물은 거의 다 오수 탱크로 돌아오고 정수기가 다시 거른다 (바닥으로 새지 않는다)
        foreach (var ship in new[] { "Hanbit", "Saeteo" })
        {
            var w = World.CreateDefault(seed, 0, ship);
            Run(w, SimTime.Hours(2));
            var l0 = Snap(w.Water.Loop);
            float level0 = w.Water.Level;
            Run(w, SimTime.Hours(24));
            var l = w.Water.Loop;
            float used = l.Used - l0.used, drained = l.Drained - l0.drained, back = l.Returned - l0.returned, proc = l.Processed - l0.processed, spill = l.Spilled - l0.spilled;
            bool ok = back >= drained * 0.9f && spill <= drained * 0.05f && proc >= back * 0.85f && w.Water.Grey < w.Water.GreyCapacity * 0.5f && w.Water.Pass >= 0.85f;
            Check($"평소 균형 ({w.Ship.Name}) — 쓴 물이 배수구 · 회수관을 지나 오수 탱크로 돌아오고 정수기가 다시 거른다", ok,
                $"하루 쓴 물 {used:0}L · 배수구로 {drained:0}L · 오수 탱크에 {back:0}L · 거름 {proc:0}L · 바닥으로 {spill:0.0}L · 오수 {w.Water.Grey:0}/{w.Water.GreyCapacity:0}L · 물 {level0:0} → {w.Water.Level:0}L · 회수관 {w.Water.Pass * 100:0}%");
        }

        // 2) 회수관이 막힌다 (컴퓨터 있음): 정수기는 멀쩡한데 놀고 · 배수구가 올라오고 · 컴퓨터가 짚고 · 회수통 · 뚫는다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(3));
            var water = w.Water;
            water.Sludge = 0.97f;
            water.Grey = 0f; // 오수 탱크가 빈 때 막혔다 (남은 오수가 있으면 정수기는 그만큼 늦게 논다)
            long t0 = w.Tick, diag = -1, bucket = -1, clear = -1, starvedAt = -1;
            string? by = null;
            float effAtStarve = 0f, level0 = water.Level;
            for (int i = 0; i < 24 * 6 && clear < 0; i++)
            {
                Run(w, SimTime.Minutes(10));
                if (starvedAt < 0 && water.Starved > 0.5f) { starvedAt = w.Tick; effAtStarve = Recyclers(w); }
                if (diag < 0 && water.Suspected) { diag = w.Tick; by = water.SuspectedBy; }
                if (bucket < 0 && water.Loop.Buckets > 0) bucket = w.Tick;
                if (clear < 0 && water.Loop.Clears > 0) clear = w.Tick;
            }
            float levelAtClear = water.Level;
            Run(w, SimTime.Hours(2));
            bool recovered = water.Pass >= 0.85f && water.Inflow > 0.5f;
            Check("회수관 막힘 — 정수기는 멀쩡한데 오수가 안 들어와 놀고 · 배수구마다 회색물이 올라온다", starvedAt >= 0 && effAtStarve >= 0.7f && water.Loop.Backups >= 2,
                $"정수기 놀기 시작 {H(starvedAt, t0)} (효율 {effAtStarve:0.00}) · 역류 {water.Loop.Backups} (헛뚫음 {water.Loop.InVain}) · 바닥으로 {water.Loop.Spilled:0}L");
            Check("회수관 막힘 — 주 컴퓨터가 '정수기는 멀쩡한데 오수가 없다'에서 회수관을 짚는다", diag >= 0 && water.Loop.Diagnosed >= 1 && diag - t0 <= SimTime.Hours(6),
                $"짚음 {H(diag, t0)} ({by ?? "-"}) · 컴퓨터 {water.Loop.Diagnosed} · 사람 {water.Loop.CrewDiagnosed}");
            Check("회수관 막힘 — 뚫기 전까지 배수구 밑에 임시 회수통 · 뚫으면 오수가 다시 정수기로", clear >= 0 && clear - t0 <= SimTime.Hours(16) && recovered,
                $"회수통 {H(bucket, t0)} ({water.Loop.Buckets}개 · 받은 물 {water.Loop.Caught:0}L) · 뚫음 {H(clear, t0)} · 물 {level0:0} → {levelAtClear:0} → {water.Level:0}L · 회수관 {water.Pass * 100:0}% · 오수 유입 {water.Inflow:0.0}L/h · 남은 통 {water.Buckets.Count}");
        }

        // 3) 컴퓨터 없이: 배수구를 뚫어도 또 올라온다 → 손재주 있는 사람이 안쪽 회수관을 의심한다
        {
            WaterSystem.NoComputer = true;
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(3));
            var water = w.Water;
            water.Sludge = 0.97f;
            long t0 = w.Tick, diag = -1, clear = -1;
            string? by = null;
            for (int i = 0; i < 36 * 6 && clear < 0; i++)
            {
                Run(w, SimTime.Minutes(10));
                if (diag < 0 && water.Suspected) { diag = w.Tick; by = water.SuspectedBy; }
                if (clear < 0 && water.Loop.Clears > 0) clear = w.Tick;
            }
            Check("컴퓨터 없이 — 배수구가 여기저기서 되풀이해 올라오면 사람이 회수관을 의심하고 뚫는다", diag >= 0 && water.Loop.CrewDiagnosed >= 1 && clear >= 0,
                $"짚음 {H(diag, t0)} ({by ?? "-"}) · 뚫음 {H(clear, t0)} · 역류 {water.Loop.Backups} (헛뚫음 {water.Loop.InVain}) · 바닥으로 {water.Loop.Spilled:0}L");
            WaterSystem.NoComputer = false;
        }

        // 4) 예방: 유량이 줄면 막히기 전에 역세척
        {
            var w = World.CreateDefault(seed, 0, "Mirinae");
            Run(w, SimTime.Hours(2));
            var water = w.Water;
            water.Sludge = 0.75f;
            long t0 = w.Tick, flushed = -1;
            for (int i = 0; i < 12 * 6 && flushed < 0; i++)
            {
                Run(w, SimTime.Minutes(10));
                if (water.Loop.Flushes > 0) flushed = w.Tick;
            }
            Check("예방 — 회수관 유량이 줄면 주 컴퓨터가 막히기 전에 역세척을 맡긴다", flushed >= 0 && water.Pass >= 0.85f && water.Loop.BlockedHours == 0f && water.Loop.Backups == 0,
                $"역세척 {H(flushed, t0)} · 회수관 {water.Pass * 100:0}% (찌꺼기 {water.Sludge:0.00}) · 막힘 {water.Loop.BlockedHours:0.#}h");
        }

        // 5) 임시 회수통만으로 버티기: 뚫지 못하는 동안 통이 막힌 물을 받고 · 차면 정수기로 날라 붓는다
        {
            WaterSystem.HoldClear = true;
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(3));
            var water = w.Water;
            water.Sludge = 1f;
            Run(w, SimTime.Hours(30));
            Check("임시 회수통 — 뚫지 못하는 동안 통이 막힌 물을 받고, 차면 정수기로 날라 붓는다", water.Loop.Caught > 10f && water.Loop.BucketTrips >= 1,
                $"회수통 {water.Loop.Buckets}개 · 받은 물 {water.Loop.Caught:0}L · 나름 {water.Loop.BucketTrips} · 바닥으로 {water.Loop.Spilled:0}L · 놓인 통 {string.Join(", ", water.Buckets.Select(b => $"{w.Ship.Rooms[b.RoomId].Name} {b.Fill:0}L"))}");
            if (water.Loop.BucketTrips == 0)
            {
                foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.EmptyBucket))
                    Console.WriteLine($"      주문 {o.Urgency:0.00} · 막힘 {o.BlockedReason ?? "-"} · 사람 {o.Assignee?.Name ?? "-"} · 로봇 {o.Robot?.Name ?? "-"}");
                foreach (var r in w.Robots.Robots)
                    Console.WriteLine($"      로봇 {r.Name} {r.Kind} {r.State} · 일 {r.Order?.Kind.ToString() ?? "-"} · 할 수 있나 {RobotSystem.CanDo(r.Kind, WorkKind.EmptyBucket)} · 작동 {r.Operational}");
            }
            WaterSystem.HoldClear = false;
        }

        // 6) 산소: 정원보다 많이 타면 발생기 몫이 숨 쉬는 양을 못 따라간다 → 하나 더 짠다 (정원대로면 짜지 않는다)
        {
            var w = World.CreateDefault(seed, 60, "Saeteo");
            float breathe0 = 0f, rated0 = 0f;
            Run(w, SimTime.Hours(1));
            breathe0 = w.Air.Breathing; rated0 = w.Air.RatedCapacity;
            long t0 = w.Tick, built = -1;
            for (int i = 0; i < 30 && built < 0; i++)
            {
                Run(w, SimTime.Hours(1));
                if (w.Adapt.ExtraOxygen > 0) built = w.Tick;
            }
            Run(w, SimTime.Hours(6));
            float low = w.Ship.LiveRooms.Where(r => w.Crew.Any(c => !c.Dead && c.Room == r)).Select(r => r.Air.O2).DefaultIfEmpty(21f).Min();
            Check("산소 — 60명이 탄 새터호: 발생기 몫이 숨 쉬는 양을 못 따라가면 발생기를 하나 더 짠다", built >= 0 && w.Air.RatedCapacity >= w.Air.Breathing && low >= 19f,
                $"숨 {breathe0:0} · 발생기 몫 {rated0:0} → {w.Air.RatedCapacity:0} · 하나 더 {H(built, t0)} ({w.Adapt.ExtraOxygen}대) · 사람 있는 방 가장 낮은 산소 {low:0.0}%"
                + (built < 0 ? $" · 전력 제어기 {w.Ship.CountStored(ItemKind.PowerController)} 케이블 {w.Ship.CountStored(ItemKind.Cable)} 금속판 {w.Ship.CountStored(ItemKind.Plate)} · 일 {string.Join(", ", w.Board.Open.Where(o => o.Kind == WorkKind.BuildOxygen).Select(o => $"{o.Urgency:0.00}/{o.Assignee?.Name ?? "-"}/{o.BlockedReason ?? "-"}"))}" : ""));
            var v = World.CreateDefault(seed, 0, "Saeteo");
            Run(v, SimTime.Hours(24));
            Check("산소 — 정원(40명)이면 짓지 않는다", v.Adapt.ExtraOxygen == 0 && v.Air.ShortSince < 0, $"숨 {v.Air.Breathing:0} · 발생기 몫 {v.Air.RatedCapacity:0} · 하나 더 {v.Adapt.ExtraOxygen}대");
            // 모든 배가 정원대로면 발생기 몫이 넉넉하다 (설계가 숨 쉬는 양을 못 따라가는 배가 없다)
            var tight = new System.Collections.Generic.List<string>();
            foreach (var t in ShipCatalog.All)
            {
                var x = World.CreateDefault(seed, 0, t.Key);
                Run(x, SimTime.Minutes(30));
                if (x.Air.RatedCapacity < x.Air.Breathing * 1.03f) tight.Add($"{x.Ship.Name} {x.Air.RatedCapacity:0}/{x.Air.Breathing:0}");
            }
            Check("산소 — 어느 배든 정원대로 타면 발생기 몫이 숨 쉬는 양보다 넉넉하다", tight.Count == 0, tight.Count == 0 ? $"{ShipCatalog.All.Count()}척 모두" : string.Join(", ", tight));
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 물 · 산소 순환" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    private static int RunWaterProbe(int seed, string ship, int hours)
    {
        var w = World.CreateDefault(seed, 0, ship);
        for (int h = 0; h <= hours; h++)
        {
            var p = w.Automation.Planner.PlanOf("water");
            var fc = w.Automation.Outlook.Get("water");
            Console.WriteLine($"{h,3}h 물 {w.Water.Level:0}/{w.Water.Capacity:0} · 오수 {w.Water.Grey:0} · 정수 {w.Water.Produced:0.0}/h · 씀 {w.Water.Consumed:0.0}/h · 계획 {p?.Mode ?? "-"} · 예측 {fc?.Line ?? "-"} · 방침 {w.Policies["water"]}");
            Run(w, SimTime.Hours(1));
        }
        return 0;
    }

    /// <summary>v19 열: 배마다 몇 시간 돌려 방 온도 · 공조 열 · 냉방 전기 · 전력 수요를 본다.</summary>
    private static int RunHeatProbe(int seed, int hours, string? only)
    {
        foreach (var t in ShipCatalog.All.Where(t => only == null || t.Key == only))
        {
            var w = World.CreateDefault(seed, 0, t.Key);
            Run(w, SimTime.Hours(hours));
            var live = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).ToList();
            var hot = live.OrderByDescending(r => r.Air.Temperature).First();
            var cold = live.OrderBy(r => r.Air.Temperature).First();
            var comp = w.Automation.ComputerBody?.Room;
            if (Environment.GetEnvironmentVariable("HEAT_ROOMS") == "1")
                foreach (var r in w.Ship.LiveRooms.OrderBy(r => r.Air.Temperature))
                    Console.WriteLine($"    {r.Name,-10} {r.Type,-12} {r.Air.Temperature,5:0.0}℃ · 부피 {r.Volume,3} · 드는 열 {r.HeatIn:0.00} · 공조 {r.HvacHeat:0.00} · 환기 {Atmosphere.Vented(r)} · 전기 {r.Powered} · 외벽 {w.Air.HullConductance(r):0.000} · 사람 {w.Crew.Count(c => !c.Dead && c.Room == r)}");
            Console.WriteLine($"{w.Ship.Name,-6} 방 {live.Count} · 평균 {live.Average(r => r.Air.Temperature):0.0}℃ · 가장 더운 {hot.Name} {hot.Air.Temperature:0.0} · 가장 찬 {cold.Name} {cold.Air.Temperature:0.0}"
                + $" · 컴퓨터실 {comp?.Air.Temperature:0.0} · 냉방 {w.Air.CoolingKw:0.0}kW(전기 {w.Air.HvacElectricKw:0.0} · 버림 {w.Air.RejectKw:0.0}) · 난방 {w.Air.HeatingKw:0.0} · 전력 수요 {w.Power.Demand:0.0}/원자로 {w.Power.ReactorLimit:0.0} · 끈 것 {w.Power.ShedCount}");
        }
        return 0;
    }

    private static (float used, float drained, float returned, float processed, float spilled) Snap(WaterLoopStats l) => (l.Used, l.Drained, l.Returned, l.Processed, l.Spilled);

    private static float Recyclers(World w) => w.Ship.FurnitureOf(FurnitureType.WaterRecycler).Select(f => f.Machine!.Efficiency).DefaultIfEmpty(0f).Average();

    private static string H(long t, long t0) => t < 0 ? "-" : $"{(t - t0) / (float)SimTime.TicksPerHour:0.0}시간 뒤";
}
