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
                    // 산소 생산이 모자란 것만 (막 뚫렸다 재가압하는 방은 빼고 — 기압이 정상인데 산소가 낮은 방)
                    foreach (var r in w.Ship.LiveRooms.Where(r => !r.Leaking && !r.Abandoned && r.Air.Pressure > 85f && alive.Any(c => c.Room == r)))
                        minO2 = MathF.Min(minO2, r.Air.O2);
                    minBattery = MathF.Min(minBattery, w.Power.BatteryPercent);
                    if (meals < n / 2 && w.Ship.CountStored(ItemKind.Produce) >= FoodChain.ProducePerBatch) cookBacklog += dt;
                    void Mark(string k, bool on) { if (on && !first.ContainsKey(k)) first[k] = day; }
                    Mark("식량", food < n * 2 || hungry >= Math.Max(1, n / 4));
                    Mark("조리", cookBacklog > 6f);
                    Mark("물", w.Water.Level < w.Water.Capacity * 0.15f);
                    Mark("산소", minO2 < 18.5f);
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
                // v10.11: 배급과 조리 분담
                int cooked = w.Crew.Sum(c => c.Stats.MealsCooked), byCooks = w.Crew.Where(c => c.Role == CrewRole.Cook).Sum(c => c.Stats.MealsCooked);
                Console.WriteLine($"     배급 {w.Food.Rationings}번 · {w.Food.RationHours:0}사람·시간 · 조리 {cooked}인분 (조리사 {w.Crew.Count(c => c.Role == CrewRole.Cook)}명이 {(cooked > 0 ? byCooks * 100 / cooked : 0)}%) · 먹을 것 끝 {FoodPolicy.FoodDays(w):0.0}일치");
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
        Console.WriteLine($"설비 단계와 회복 · 한빛호 · {runs}쌍 · 냉각 펌프 모두 고착 뒤 48시간, 그 뒤 열흘\n");
        var sum = new Dictionary<string, (float scrams, float off, float recover, float shed, int n)>();
        var longFaults = new Dictionary<string, int>();
        var longScrams = new Dictionary<string, int>();
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
                // 같은 사고: 냉각 펌프가 모두 고착 (원자로가 선다) — 짝수 시드는 냉각실 큰 운석까지
                foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump).ToList()) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
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
                // 뒤이은 열흘: 평소 운전 중 원자로 고장·긴급 정지 (핵융합로는 더 자주 선다?)
                int faults0 = w.Ship.FurnitureOf(FurnitureType.ReactorCore).Sum(f => f.Machine!.FaultCount);
                int scramsMid = w.History.Scrams;
                for (int t = 0; t < 10 * SimTime.TicksPerDay; t++) w.Step();
                int laterFaults = w.Ship.FurnitureOf(FurnitureType.ReactorCore).Sum(f => f.Machine!.FaultCount) - faults0;
                int laterScrams = w.History.Scrams - scramsMid;
                longFaults[mode] = longFaults.GetValueOrDefault(mode) + laterFaults;
                longScrams[mode] = longScrams.GetValueOrDefault(mode) + laterScrams;
                var cur = sum.GetValueOrDefault(mode);
                sum[mode] = (cur.scrams + scrams, cur.off + off, cur.recover + recover, cur.shed + shed, cur.n + 1);
                Console.WriteLine($"  시드 {s,-9} {mode,-4} 긴급 정지 {scrams} · 원자로 꺼짐 {off,4:0.0}시간 · 회복 {recover,4:0.0}시간 · 부하 차단 {shed,4:0.0}시간 · 정격 {w.Power.ReactorRated:0}kW · 뒤이은 열흘 원자로 고장 {laterFaults} · 긴급 정지 {laterScrams}");
            }
        }
        Console.WriteLine();
        foreach (var (mode, v) in sum)
            Console.WriteLine($"  {mode,-4} 평균: 긴급 정지 {v.scrams / v.n:0.0} · 원자로 꺼짐 {v.off / v.n:0.0}시간 · 회복 {v.recover / v.n:0.0}시간 · 부하 차단 {v.shed / v.n:0.0}시간 · 열흘 원자로 고장 {longFaults.GetValueOrDefault(mode) / (float)v.n:0.0} · 긴급 정지 {longScrams.GetValueOrDefault(mode) / (float)v.n:0.0}");
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

public static partial class Program
{
    /// <summary>
    /// v10.10 자원 회복 게이트: 같은 사고를 간격을 바꿔 되풀이하고, 자원마다 "사고 한 번에 쓰는 양 ÷ 간격"과 "하루 수입"을 견준다.
    /// 쓰는 속도가 들어오는 속도를 넘으면 비축이 무너지고, 아니면 사고 사이에 되돌아온다 — 그걸 기록으로 보인다.
    ///   32 시드 --gate=recovery [--ship=Mirinae]
    /// </summary>
    private static int RunRecoveryGate(int days, int seed, string? shipKey)
    {
        days = Math.Max(days, 20);
        string key = shipKey ?? "Mirinae";
        Console.WriteLine($"자원 회복 게이트 · {ShipCatalog.Find(key)?.Name ?? key} · {days}일 · 시드 {seed}\n");
        string[] kinds = { "meteor", "fire", "pipe" };
        int[] gaps = { 2, 4, 8 };
        string[] watch = { "air", "sealant", "plate", "coolant", "water" };
        int explained = 0, recovered = 0, collapsed = 0, total = 0;
        foreach (var kind in kinds)
        {
            Console.WriteLine($"── {kind switch { "meteor" => "운석 배", "fire" => "불 배", _ => "배관 배" }} ──");
            foreach (int gap in gaps)
            {
                var w = World.CreateDefault(seed, 0, key);
                var rng = new Rng(seed ^ (kind.GetHashCode() & 0xffff) ^ gap * 7919);
                var start = watch.ToDictionary(k => k, k => ResourceLedger.Level(w, k));
                int incidents = 0;
                for (int d = 1; d <= days; d++)
                {
                    if (d >= 2 && (d - 2) % gap == 0)
                    {
                        incidents++;
                        switch (kind)
                        {
                            case "meteor":
                            {
                                var rooms = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && !r.Abandoned && w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r)).ToList();
                                Player.Meteor(w, Scenarios.OuterTarget(w, rng.Pick(rooms)), rng.Range(0.7f, 0.95f));
                                break;
                            }
                            case "fire":
                            {
                                var rooms = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && !r.Abandoned && r.Cells.Any(w.Ship.IsOpenFloor)).ToList();
                                var room = rng.Pick(rooms);
                                Player.Fire(w, rng.Pick(room.Cells.Where(w.Ship.IsOpenFloor).ToList()));
                                break;
                            }
                            default:
                            {
                                var segs = w.Piping.Segments.Where(s => s.IsCoolant).ToList();
                                if (segs.Count == 0) break;
                                var seg = rng.Pick(segs);
                                Player.PipeBurst(w, rng.Pick(seg.Path), rng.Range(0.6f, 1f));
                                break;
                            }
                        }
                    }
                    Run(w, SimTime.TicksPerDay);
                }
                var l = w.Ledger;
                var eps = l.Episodes.Where(e => !e.Open).ToList();
                Console.WriteLine($"  {gap}일마다 {incidents}번 · 결과 {Outcome.Name(Assessment.Assess(w, Snapshot.Take(w)).Kind)} · 비축 방침 {Logistics.ModeName(l.Mode)}");
                foreach (var k in watch)
                {
                    float used = eps.Count == 0 ? 0f : eps.Sum(e => e.Used.GetValueOrDefault(k)) / Math.Max(1, incidents);
                    var (inPerDay, outPerDay) = l.Average(k, days);
                    float end = ResourceLedger.Level(w, k);
                    float s0 = MathF.Max(0.01f, start[k]);
                    var rec = eps.Where(e => e.Before.GetValueOrDefault(k) - e.Low.GetValueOrDefault(k, e.Before.GetValueOrDefault(k)) > 0.05f * s0).ToList();
                    var times = rec.Where(e => e.Recovered.ContainsKey(k)).Select(e => (e.Recovered[k] - e.End) / (float)SimTime.TicksPerDay).OrderBy(x => x).ToList();
                    string back = rec.Count == 0 ? "흔들리지 않음" : $"회복 {times.Count}/{rec.Count}" + (times.Count > 0 ? $" (중앙 {times[times.Count / 2]:0.0}일)" : "");
                    // 사고가 먹는 속도 (하루치) · 평소 쓰임(개조·제작·정비) ↔ 들어오는 속도
                    float burn = used / gap;
                    float incidentTotal = eps.Sum(e => e.Used.GetValueOrDefault(k));
                    float ordinary = MathF.Max(0f, outPerDay - incidentTotal / days);
                    float margin = inPerDay - ordinary - burn;
                    string verdict = end >= 0.9f * s0 ? "회복" : end >= 0.5f * s0 ? "버팀" : "무너짐";
                    bool predicted = margin < -0.01f * s0 / days ? verdict != "회복" : verdict != "무너짐";
                    total++;
                    if (predicted) explained++;
                    if (verdict == "회복") recovered++;
                    if (verdict == "무너짐") collapsed++;
                    if (used < 0.01f && inPerDay < 0.01f) continue;
                    string unit = ResourceLedger.Unit(k);
                    Console.WriteLine($"    {ResourceLedger.Name(k),-6} 사고당 −{used,5:0.#}{unit} ÷ {gap}일 = 하루 −{burn,5:0.##} · 평소 −{ordinary,5:0.##} ↔ 수입 +{inPerDay,5:0.##} (남는 {margin,6:+0.##;-0.##}) · {back,-18} · {s0:0.#} → {end:0.#} {verdict}" + (predicted ? "" : " (설명 밖)"));
                }
            }
            Console.WriteLine();
        }
        Console.WriteLine($"판정: 회복 {recovered} · 무너짐 {collapsed} · 설명됨 {explained}/{total} (사고당 소비÷간격 + 평소 쓰임이 하루 수입을 넘으면 비축이 되돌아오지 않는다)");
        bool ok = recovered > 0 && collapsed > 0 && explained >= total * 0.75f;
        Console.WriteLine(ok ? "✔ 게이트 통과 — 회복하는 조건과 무너지는 조건이 기록으로 갈린다" : "✘ 게이트 미달");
        return ok ? 0 : 1;
    }
}
