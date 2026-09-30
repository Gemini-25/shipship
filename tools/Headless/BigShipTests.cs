using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v10.9 긴 회귀 다시 맞추기: 큰 배를 여러 시드·사고와 함께 돌려 무엇이 먼저 무너지는지, 설비 단계가 사고 뒤 회복을 바꾸는지 본다.
public static partial class Program
{
    /// <summary>모자람의 종류 (먼저 모자란 것을 센다).</summary>
    private static readonly string[] ShortageKinds = { "식량", "조리", "물", "산소", "전력", "잠자리" };

    /// <summary>
    /// v10.9: 12·20·30인 배 × 시드 넷 — 사흘마다 무작위 사고 묶음을 겪게 하고 날마다 모자람을 잰다.
    /// 먼저 모자란 것(식량·조리·물·산소·전력·잠자리)과 그날, 쓰러짐·결과를 표로.
    ///   days 시드 --bigships [--every=3] [--ships=Hanbit,Eunha,Cheonma] [--seeds=4]
    /// </summary>
    private static int RunBigShips(int days, int seed, string[] args)
    {
        int every = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--every="))?.Split('=')[1], out var ev) ? ev : 3;
        int seeds = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--seeds="))?.Split('=')[1], out var sn) ? sn : 4;
        var keys = (args.FirstOrDefault(a => a.StartsWith("--ships="))?.Split('=')[1] ?? "Hanbit,Eunha,Cheonma").Split(',');
        bool robots = !args.Contains("--norobots");
        Console.WriteLine($"큰 배 {days}일 · {every}일마다 무작위 사고 · 시드 {seeds}개 (첫 시드 {seed}){(robots ? "" : " · 로봇 없음")}\n");
        var firsts = new Dictionary<string, Dictionary<string, int>>();
        foreach (var key in keys)
        {
            var tpl = ShipCatalog.Find(key);
            if (tpl == null) { Console.WriteLine($"  배 {key} 없음"); continue; }
            firsts[key] = new Dictionary<string, int>();
            Console.WriteLine($"── {tpl.Name} ({tpl.Crew}명) ──");
            for (int i = 0; i < seeds; i++)
            {
                int s = seed + i * 1103;
                var w = World.CreateDefault(s, 0, key);
                if (!robots) DisableRobots(w);
                var rng = new Rng(unchecked((int)((uint)s * 2654435761u ^ 0x0b16b16u)));
                var first = new Dictionary<string, float>();
                var what = new List<string>();
                float starving = 0f, minWater = 1f, minO2 = 99f, minBattery = 1f, sleepless = 0f, cookBacklog = 0f;
                int collapses0 = w.History.Collapses;
                var before = Snapshot.Take(w);
                long end = w.Tick + (long)days * SimTime.TicksPerDay;
                long nextIncident = w.Tick + (long)every * SimTime.TicksPerDay;
                while (w.Tick < end)
                {
                    w.Step();
                    if (w.Tick == nextIncident)
                    {
                        what.Add($"{w.Day}일 " + Scenarios.RandomIncident(w, rng));
                        nextIncident += (long)every * SimTime.TicksPerDay;
                    }
                    if (w.Tick % World.SystemInterval != 0) continue;
                    float day = (w.Tick - SimTime.Hours(7)) / (float)SimTime.TicksPerDay;
                    var alive = w.Crew.Where(c => !c.Dead).ToList();
                    int n = Math.Max(1, alive.Count);
                    int food = w.Ship.CountStored(ItemKind.Meal) + w.Ship.CountStored(ItemKind.Produce) + w.Ship.CountStored(ItemKind.Ration);
                    int meals = w.Ship.CountStored(ItemKind.Meal);
                    int hungry = alive.Count(c => c.Needs.Food < 0.05f);
                    int tired = alive.Count(c => c.Needs.Rest < 0.08f);
                    float dt = World.SystemInterval / (float)SimTime.TicksPerHour;
                    starving += hungry * dt;
                    sleepless += tired * dt;
                    minWater = MathF.Min(minWater, w.Water.Level / MathF.Max(1f, w.Water.Capacity));
                    foreach (var r in w.Ship.LiveRooms.Where(r => !r.Leaking && !r.Abandoned && alive.Any(c => c.Room == r)))
                        minO2 = MathF.Min(minO2, r.Air.O2);
                    minBattery = MathF.Min(minBattery, w.Power.BatteryPercent);
                    if (meals < n / 2 && w.Ship.CountStored(ItemKind.Produce) >= FoodChain.ProducePerBatch) cookBacklog += dt;
                    void Mark(string k, bool on) { if (on && !first.ContainsKey(k)) first[k] = day; }
                    Mark("식량", food < n * 2 || hungry >= Math.Max(1, n / 4));
                    Mark("조리", cookBacklog > 6f);
                    Mark("물", w.Water.Level < w.Water.Capacity * 0.15f);
                    Mark("산소", minO2 < 18f);
                    Mark("전력", w.Power.BatteryPercent < 0.1f && w.Power.ShedCount > 0);
                    Mark("잠자리", tired >= Math.Max(1, n / 4));
                }
                var outcome = Assessment.Assess(w, before);
                string firstKind = first.Count == 0 ? "없음" : first.OrderBy(kv => kv.Value).First().Key;
                firsts[key][firstKind] = firsts[key].GetValueOrDefault(firstKind) + 1;
                string order = first.Count == 0 ? "모자람 없음" : string.Join(" → ", first.OrderBy(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0.0}일"));
                Console.WriteLine($"  시드 {s,-9} {Outcome.Name(outcome.Kind),-5} · 먼저 모자란 것: {order}");
                Console.WriteLine($"     굶주림 {starving:0}사람·시간 · 못 잠 {sleepless:0}사람·시간 · 물 최저 {minWater * 100:0}% · 산소 최저 {minO2:0.0}kPa · 배터리 최저 {minBattery * 100:0}% · 조리 밀림 {cookBacklog:0}시간 · 쓰러짐 {w.History.Collapses - collapses0} · 공기 탱크 {w.Air.Reserve / w.Air.ReserveCapacity * 100:0}%" +
                                  (w.Robots.Robots.Count > 0 ? $" · 로봇 {w.Robots.Summary}" : ""));
                Console.WriteLine($"     사고: {string.Join(" / ", what)}");
            }
            Console.WriteLine($"  먼저 모자란 것: {string.Join(" · ", firsts[key].OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}\n");
        }
        return 0;
    }

    /// <summary>
    /// v10.9: 설비 단계가 사고 뒤 회복을 바꾸나 — 같은 배(한빛호)·같은 사고를 기본 단계와 핵융합로(III)·고압 펌프(II)로 올린 배가 함께 겪는다.
    /// 긴급 정지 횟수, 원자로가 꺼져 있던 시간, 배터리가 80%로 돌아오기까지, 부하 차단 시간을 견준다.
    ///   1 시드 --tiers [--runs=8]
    /// </summary>
    private static int RunTierRecovery(int seed, string[] args)
    {
        int runs = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--runs="))?.Split('=')[1], out var rn) ? rn : 8;
        Console.WriteLine($"설비 단계와 회복 · 한빛호 · {runs}쌍 · 사고 뒤 48시간\n");
        var sum = new Dictionary<string, (float scrams, float off, float recover, float shed, int n)>();
        for (int i = 0; i < runs; i++)
        {
            int s = seed + i * 331;
            foreach (var mode in new[] { "기본", "핵융합" })
            {
                var w = World.CreateDefault(s, 0, "Hanbit");
                if (mode == "핵융합")
                {
                    foreach (var m in w.Ship.Machines)
                    {
                        if (m.Body.Type == FurnitureType.ReactorCore) m.Tier = 3;
                        if (m.Body.Type == FurnitureType.CoolantPump) m.Tier = 2;
                    }
                    w.Power.Update(0.01f);
                }
                for (int t = 0; t < SimTime.TicksPerDay; t++) w.Step();
                int scrams0 = w.History.Scrams;
                // 같은 사고: 냉각 펌프 절반 고착 + 냉각실 큰 운석 (시드마다 어느 펌프인지 다르다)
                var rng = new Rng(s ^ 0x71e7);
                var pumps = w.Ship.FurnitureOf(FurnitureType.CoolantPump).ToList();
                foreach (var p in pumps.OrderBy(_ => rng.Float()).Take(Math.Max(1, pumps.Count / 2))) Player.Break(w, p);
                if (i % 2 == 0) Player.Meteor(w, Scenarios.OuterTarget(w, RoomType.Cooling), 0.9f);
                float off = 0f, shed = 0f, recover = -1f;
                long start = w.Tick;
                for (int t = 0; t < SimTime.Hours(48); t++)
                {
                    w.Step();
                    if (w.Tick % World.SystemInterval != 0) continue;
                    float dt = World.SystemInterval / (float)SimTime.TicksPerHour;
                    if (!w.Power.ReactorOnline) off += dt;
                    if (w.Power.ShedCount > 0) shed += dt;
                    if (recover < 0f && w.Tick - start > SimTime.Hours(1) && w.Power.ReactorOnline && w.Power.BatteryPercent >= 0.8f && w.Power.ShedCount == 0)
                        recover = (w.Tick - start) / (float)SimTime.TicksPerHour;
                }
                int scrams = w.History.Scrams - scrams0;
                if (recover < 0f) recover = 48f;
                var cur = sum.GetValueOrDefault(mode);
                sum[mode] = (cur.scrams + scrams, cur.off + off, cur.recover + recover, cur.shed + shed, cur.n + 1);
                Console.WriteLine($"  시드 {s,-9} {mode,-4} 긴급 정지 {scrams} · 원자로 꺼짐 {off,4:0.0}시간 · 회복 {recover,4:0.0}시간 · 부하 차단 {shed,4:0.0}시간 · 정격 {w.Power.ReactorRated:0}kW");
            }
        }
        Console.WriteLine();
        foreach (var (mode, v) in sum)
            Console.WriteLine($"  {mode,-4} 평균: 긴급 정지 {v.scrams / v.n:0.0} · 원자로 꺼짐 {v.off / v.n:0.0}시간 · 회복 {v.recover / v.n:0.0}시간 · 부하 차단 {v.shed / v.n:0.0}시간");
        return 0;
    }

    /// <summary>로봇을 모두 멈춘 배 (로봇 없는 배와 견주기).</summary>
    private static void DisableRobots(World w)
    {
        foreach (var r in w.Robots.Robots) r.Disabled = true;
    }
}

public static partial class Program
{
    /// <summary>v10.10: 배마다 로봇 충전대가 어디에 놓였나.</summary>
    private static int RunDocks()
    {
        foreach (var t in ShipCatalog.All)
        {
            var w = World.CreateDefault(1, 0, t.Key);
            Console.WriteLine($"{t.Name}: " + string.Join(" · ", w.Robots.Robots.Select(r => $"{r.Name}@{r.Dock.Room.Name}{r.Dock.Cells[0]}")));
        }
        return 0;
    }

    /// <summary>v10.10: 로봇마다 한 일·거든 시간·고장·끌려온 횟수.</summary>
    private static void PrintRobots(World w)
    {
        var rs = w.Robots;
        if (rs.Robots.Count == 0) return;
        Console.WriteLine($"로봇 {rs.Summary} · 고장 {rs.Breakdowns} · 방전 {rs.Stalls} · 불 {rs.FiresFought} · 순찰 {rs.Patrols}");
        foreach (var r in rs.Robots)
            Console.WriteLine($"  {r.Name,-8} {r.JobsDone,4}건 · 일한 {r.ActiveHours,5:0.0}시간 · 거든 {r.AssistHours,4:0.0}시간 · 고장 {r.Breakdowns} · 끌려옴 {r.Fetched} · 배터리 {r.Battery * 100,3:0}% · 상태 {r.Condition * 100,3:0}% · {r.Doing}");
    }

    /// <summary>v10.10: 자원 장부 — 하루 평균 수입·소비와 사고마다 쓴 양·회복 시간.</summary>
    private static void PrintLedger(World w, int days)
    {
        var l = w.Ledger;
        Console.WriteLine($"자원 장부 (최근 {Math.Min(days, 7)}일 하루 평균 · 비축 방침 {Logistics.ModeName(l.Mode)}{(l.ModeWhy.Length > 0 ? " — " + l.ModeWhy : "")})");
        foreach (var (key, name, unit) in ResourceLedger.Kinds)
        {
            var (inp, outp) = l.Average(key, 7);
            Console.WriteLine($"  {name,-6} +{inp,6:0.0} / −{outp,6:0.0} {unit} · 지금 {ResourceLedger.Level(w, key):0.0}");
        }
        foreach (var e in l.Episodes.TakeLast(8))
            Console.WriteLine($"  사고 {SimTime.Day(e.Start)}일 {SimTime.Clock(e.Start)} {e.Cause}: {l.Describe(e, w.Tick)}");
        var a = w.Adapt;
        if (a.JumpersRemoved + a.CotsStowed + a.Recycled > 0)
            Console.WriteLine($"  정리: 임시 배선 걷음 {a.JumpersRemoved} · 간이침대 치움 {a.CotsStowed} · 재활용 {a.Recycled}");
    }
}
