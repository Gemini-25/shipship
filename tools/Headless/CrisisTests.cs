using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 승무원 AI: 배가 크게 망가졌을 때 무엇부터 하나 (위기 추적 · 위기 게이트)
public static partial class Program
{
    /// <summary>크게 망가진 배: 한밤중에 배전실·생명유지실·창고에 운석, 식당에 불, 냉각 펌프 하나 고착, 배터리 15%.</summary>
    private static World WreckedShip(int seed, string ship = "Mirinae", float hour = 23f)
    {
        var w = DayOne(seed, ship);
        // 밤 11시까지 (잠자는 사람이 있다)
        while (Math.Abs(SimTime.HourOfDay(w.Tick) - hour) > 0.05f) w.Step();
        Scenarios.Apply(w, "powerhit", out _);
        Scenarios.Apply(w, "chaos", out _);
        var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First().Machine!;
        w.Machines.Break(pump, FaultKind.PumpSeized);
        w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.15f;
        return w;
    }

    /// <summary>한 사람이 지금 하는 일을 짧게.</summary>
    private static string Doing(CrewMember c, World w)
    {
        if (c.Dead) return "죽음";
        if (c.Down) return "쓰러짐";
        string what = c.Job?.Order is WorkOrder o ? $"{WorkKinds.Name(o.Kind)}:{o.Target.Label}({o.Urgency:0.00})" : c.ActivityLabel;
        return what;
    }

    /// <summary>위기 추적: 15분마다 배 상태와 모두가 하는 일.</summary>
    private static int RunCrisisTrace(int seed, string ship, int hours)
    {
        var w = WreckedShip(seed, ship);
        Console.WriteLine($"위기 추적 · {w.Ship.Name} · 시드 {seed} · {SimTime.Clock(w.Tick)}부터 {hours}시간\n");
        for (int q = 0; q <= hours * 4; q++)
        {
            var p = w.Power;
            int leaking = w.Ship.LiveRooms.Count(r => r.Leaking);
            Console.WriteLine($"{SimTime.Clock(w.Tick)} 원자로 {(p.ReactorOnline ? "켜짐" : "꺼짐")} · 공급 {p.Delivered:0}/{p.Demand:0}kW · 배터리 {p.BatteryPercent * 100:0}% · 보조 {(p.AuxRunning ? "돎" : "-")} · " +
                              $"새는 방 {leaking} · 불 {w.Fire.Count} · 산소 몫 {w.Air.O2Capacity:0} · 위기 {Crisis.Name(Crisis.Level(w))}");
            foreach (var c in w.Crew)
                Console.WriteLine($"    {c.Name,-8} {(c.Pose == Pose.Sleeping ? "[잠]" : "     ")} 기력 {c.Needs.Rest * 100,3:0}% {c.Room?.Name ?? "밖",-8} {Doing(c, w)}");
            if (q % 4 == 0)
                foreach (var o in w.Board.Open.OrderByDescending(o => o.Urgency).Take(14))
                    Console.WriteLine($"      · {o.Urgency:0.00} {o.Title} — {o.Detail}" + (o.Assignee != null ? $" [{o.Assignee.Name}]" : "") +
                                      (o.BlockedUntil > w.Tick ? $" (보류: {o.BlockedReason})" : "") + $" 관련 {Crisis.Relevance(w, o):0.0}");
            Run(w, SimTime.Minutes(15));
        }
        return 0;
    }

    /// <summary>
    /// 위기 게이트: 크게 망가진 배(여러 시드)에서 — 원자로·전기가 돌아오기까지 몇 시간, 그동안 잠든 사람·시간, 급하지 않은 일(외벽 점검·정비·개조 등)에 쓴 사람·시간.
    /// </summary>
    private static int RunCrisisGate(int runs, int seed, string ship)
    {
        if (Environment.GetEnvironmentVariable("SHIPSIM_OLDAI") == "1") { Crisis.Disabled = true; Console.WriteLine("(위기 판단을 끈 예전 AI)"); }
        Console.WriteLine($"위기 게이트 · {ship} · {runs}판 · 시드 {seed}부터 (밤 11시 · 배전실·생명유지실·창고 운석 · 불 · 냉각 펌프 고착 · 배터리 15%)\n");
        float sumPower = 0, sumSleep = 0, sumIdle = 0, sumLow = 0, sumCrit = 0;
        int restored = 0, deaths = 0, downs = 0;
        for (int i = 0; i < runs; i++)
        {
            var w = WreckedShip(seed + i * 37, ship);
            long start = w.Tick, back = -1;
            float sleep = 0, low = 0, idle = 0, crit = 0;
            var lowKinds = new Dictionary<string, float>();
            for (int t = 0; t < SimTime.Hours(12); t++)
            {
                w.Step();
                if (t % SimTime.Minutes(5) != 0) continue;
                bool powerOk = w.Power.ReactorOnline && w.Power.Delivered >= w.Power.Demand * 0.95f;
                if (back < 0 && powerOk) back = w.Tick;
                var level = Crisis.Level(w);
                if (level < CrisisLevel.Emergency) continue;
                crit += 5f / 60f;
                foreach (var c in w.Crew.Where(c => c.CanAct))
                {
                    if (c.Pose == Pose.Sleeping) { sleep += 5f / 60f; continue; }
                    if (c.Job?.Order is WorkOrder o)
                    {
                        if (Crisis.Relevance(w, o) <= 0.3f)
                        {
                            low += 5f / 60f;
                            string k = WorkKinds.Name(o.Kind);
                            lowKinds[k] = lowKinds.GetValueOrDefault(k) + 5f / 60f;
                        }
                    }
                    else if (c.Job?.Activity is RelaxActivity or WanderActivity or ChatActivity || c.Job == null) idle += 5f / 60f;
                }
            }
            float hrs = back < 0 ? 12f : (back - start) / (float)SimTime.TicksPerHour;
            if (back >= 0) restored++;
            sumPower += hrs; sumSleep += sleep; sumLow += low; sumIdle += idle; sumCrit += crit;
            deaths += w.Crew.Count(c => c.Dead);
            downs += w.Crew.Sum(c => c.Stats.TimesDown);
            Console.WriteLine($"  #{i + 1} 시드 {seed + i * 37}: 전기 복구 {(back < 0 ? "12시간 넘게" : $"{hrs:0.0}시간")} · 위기 {crit:0.0}시간 동안 잠 {sleep:0.0}·딴일 {low:0.0}·빈둥 {idle:0.0} 사람·시간" +
                              (lowKinds.Count > 0 ? $" ({string.Join(", ", lowKinds.OrderByDescending(x => x.Value).Take(4).Select(x => $"{x.Key} {x.Value:0.0}"))})" : "") +
                              $" · 쓰러짐 {w.Crew.Sum(c => c.Stats.TimesDown)} · 사망 {w.Crew.Count(c => c.Dead)}");
        }
        Console.WriteLine($"\n평균: 전기 복구 {sumPower / runs:0.0}시간 ({restored}/{runs}판) · 위기 {sumCrit / runs:0.0}시간 동안 잠 {sumSleep / runs:0.0} · 딴일 {sumLow / runs:0.0} · 빈둥 {sumIdle / runs:0.0} 사람·시간 · 쓰러짐 {downs} · 사망 {deaths}");
        return 0;
    }
}

public static partial class Program
{
    /// <summary>봉쇄 구역 추적: 시나리오를 걸고 포기한 방이 다시 열리는지 두 시간마다.</summary>
    private static int RunReopenTrace(int seed, string scenario, int days)
    {
        var w = DayOne(seed, "Mirinae");
        Scenarios.Apply(w, scenario, out var focus);
        Console.WriteLine($"봉쇄 구역 추적 · {scenario} · {days}일");
        for (int h = 0; h <= days * 24; h += 2)
        {
            foreach (var r in w.Ship.Rooms.Where(r => r.Abandoned || r == focus))
            {
                var orders = w.Board.Open.Where(o => o.Target.CurrentRoom == r || o.Target.Room == r).Select(o => $"{WorkKinds.Name(o.Kind)}({o.Urgency:0.00}{(o.BlockedUntil > w.Tick ? $" 보류:{o.BlockedReason}" : "")}{(o.Assignee != null ? " " + o.Assignee.Name : "")})");
                Console.WriteLine($"{h,3}h {r.Name}: 포기 {(r.Abandoned ? "예" : "아니오")} · 샘 {(r.Leaking ? "예" : "아니오")} · 기압 {r.Air.Pressure:0} · 탱크 {w.Air.Reserve:0}/필요 {r.Volume * 70f:0} · 잠금 {r.Lockdown} · {string.Join(", ", orders)}");
            }
            foreach (var e in w.History.Events.Where(e => e.Tick > w.Tick - SimTime.Hours(2) && e.Kind is HistoryKind.Decision or HistoryKind.Adaptation))
                Console.WriteLine($"      [{e.Kind}] {e.Text}");
            Run(w, SimTime.Hours(2));
        }
        return 0;
    }
}
