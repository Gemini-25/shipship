using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>통합6 위험 수준 살피기 (시험 아님): 보통 재해가 시작될 때 그 방에 누가 있었고, 그 뒤 몇 시간 동안 얼마나 다쳤나.</summary>
public static partial class Program
{
    private static int RunRiskProbe(string[] args, int seed)
    {
        if (Environment.GetEnvironmentVariable("RISK_SCENE") is string sc) { foreach (var k in sc.Split(',')) RiskScene(seed, k); return 0; }
        Storyteller.PersonaValue = 1f;
        Storyteller.LevelValue = 3f;
        string ships = Environment.GetEnvironmentVariable("RISK_SHIPS") ?? "Hanbit,Eunha,Cheonma";
        string seeds = Environment.GetEnvironmentVariable("RISK_SEEDS") ?? "7,11";
        int days = int.TryParse(Environment.GetEnvironmentVariable("RISK_DAYS"), out var dd) ? dd : 10;
        int withPeople = 0, total = 0;
        foreach (var ship in ships.Split(','))
        foreach (var sd in seeds.Split(',').Select(int.Parse))
        {
            var w = World.CreateDefault(sd, 0, ship);
            w.CrewCanDie = true;
            var watch = new List<(ScaleCase k, List<(CrewMember c, string how)> who, float[] min)>();
            var seen = new HashSet<int>();
            var exp = new SortedDictionary<string, (int min, HashSet<int> who)>(StringComparer.Ordinal);
            var minHp = new Dictionary<int, (float h, string why)>();
            void Bump(SortedDictionary<string, (int min, HashSet<int> who)> d, string k, CrewMember c)
            {
                if (!d.TryGetValue(k, out var v)) v = (0, new HashSet<int>());
                v.who.Add(c.Id); d[k] = (v.min + 1, v.who);
            }
            long end = (long)(days * SimTime.TicksPerDay);
            for (long t = 1; t <= end; t++)
            {
                w.Step();
                if (t % SimTime.Minutes(1) != 0) continue;
                foreach (var c in w.Crew)
                    if (!minHp.TryGetValue(c.Id, out var mh) || c.Vitals.Health < mh.h) minHp[c.Id] = (c.Dead ? 0f : c.Vitals.Health, $"{c.Vitals.InjuryCause ?? "?"} @{(w.Tick - SimTime.Hours(7)) / (float)SimTime.TicksPerHour:0}h {c.Room?.Name}");
                foreach (var k in w.Scale.Cases)
                {
                    if (!seen.Add(k.Id)) continue;
                    if (k.Base is not (IncidentScale.Room or IncidentScale.System)) continue;
                    total++;
                    var rooms = new HashSet<int>(k.Rooms) { k.RoomId };
                    var who = w.Crew.Where(c => !c.Dead && (c.Room != null && rooms.Contains(c.Room.Id) || c.Id == k.CrewId))
                        .Select(c => (c, $"{c.Name}({(c.Pose == Pose.Sleeping ? "잠" : c.Job?.Activity?.Id ?? "-")})")).ToList();
                    if (who.Count > 0) withPeople++;
                    watch.Add((k, who, who.Select(x => x.c.Vitals.Health).ToArray()));
                }
                foreach (var c in w.Crew)
                {
                    if (c.Dead || c.Away || c.Outside || c.Room is not Room rr) continue;
                    var a = rr.Air;
                    bool suit = c.Suit != null;
                    if (!suit && a.Smoke > 0.4f) Bump(exp, "연기>0.4", c);
                    if (!suit && a.Smoke > 0.7f) Bump(exp, "연기>0.7", c);
                    if (!suit && a.Pressure < 50f) Bump(exp, "기압<50", c);
                    if (!suit && a.O2 < 16f) Bump(exp, "산소<16", c);
                    if (a.Temperature > 45f) Bump(exp, "열>45", c);
                    if (w.Fire.CountIn(rr) > 0) Bump(exp, "불난 방", c);
                    if (c.Vitals.Health < 0.6f) Bump(exp, "체력<60", c);
                }
                foreach (var x in watch)
                {
                    if (w.Tick - x.k.Start > SimTime.Hours(4)) continue;
                    for (int i = 0; i < x.who.Count; i++) x.min[i] = MathF.Min(x.min[i], x.who[i].c.Dead ? 0f : x.who[i].c.Vitals.Health);
                }
            }
            foreach (var x in watch)
            {
                if (x.who.Count == 0) continue;
                Console.WriteLine($"  {ship}·{sd} {(x.k.Start - SimTime.Hours(7)) / (float)SimTime.TicksPerHour,6:0.0}h [{x.k.Peak}] {x.k.Name} · {string.Join(" ", x.who.Select((y, i) => $"{y.how}{x.min[i] * 100:0}%"))}");
            }
            Console.WriteLine($"   노출(사람·분): " + string.Join(" · ", exp.Select(e => $"{e.Key} {e.Value.min}분 {e.Value.who.Count}명")));
            Console.WriteLine($"   최저 체력: " + string.Join(" · ", w.Crew.Where(c => minHp.ContainsKey(c.Id) && minHp[c.Id].h < 0.8f).Select(c => $"{c.Name} {minHp[c.Id].h * 100:0}% {minHp[c.Id].why}")));
            Console.WriteLine($"== {ship}·{sd} 출혈 {w.Casualty.Bleeds} 심정지 {w.Casualty.Arrests} 사망 {w.Crew.Count(c => c.Dead)} · 쓰러짐 {w.History.Collapses}");
        }
        Console.WriteLine($"보통 재해 {total} · 시작 때 그 방에 사람 {withPeople}");
        return 0;
    }

    /// <summary>통합6 장면 살피기: 잠든 방 불 · 감압 — 분 단위로 몸 상태를 찍는다.</summary>
    private static void RiskScene(int seed, string kind)
    {
        var w = DayOne(seed, Environment.GetEnvironmentVariable("RISK_SHIP") ?? "Hanbit");
        w.CrewCanDie = true;
        RunUntilHour(w, 2f);
        var v = w.Crew.Where(c => !c.Dead && c.Pose == Pose.Sleeping && c.Room != null && c.Room.Type == RoomType.Quarters).OrderBy(c => c.Id).FirstOrDefault();
        if (v == null) { Console.WriteLine("잠든 사람 없음"); return; }
        if (kind == "bedfire")
        {
            var spot = Cell.Dirs8.Select(d => v.Cell + d).Where(x => w.Ship.IsOpenFloor(x) && w.Ship.RoomAt(x) == v.Room).OrderBy(x => x.X).ThenBy(x => x.Y).First();
            w.Fire.Ignite(spot, 0.5f);
        }
        else if (kind == "breach") Incidents.Meteor(w, v.Cell, 0.6f);
        Console.WriteLine($"  [{kind}] {v.Name} · {v.Room?.Name}");
        for (int m = 0; m < 120 && !v.Dead; m++)
        {
            Run(w, SimTime.Minutes(1));
            var r = v.Room;
            if (m % 3 == 0 || v.Down)
                Console.WriteLine($"   {SimTime.Clock(w.Tick)} {v.Name} 체력 {v.Vitals.Health * 100:0}% 산소 {v.Vitals.Oxygen * 100:0}% 부상 {v.Vitals.Injury * 100:0}% {v.Pose} {(v.Down ? "쓰러짐" : "")} {r?.Name} 연기 {r?.Air.Smoke:0.00} 기압 {r?.Air.Pressure:0} 불 {(r != null ? w.Fire.CountIn(r) : 0)} 일 {v.Job?.Label} · 배 불 {w.Fire.Count} · 곁 {w.Crew.Count(o => o != v && !o.Dead && o.Room == r)}");
        }
        Console.WriteLine($"   끝: {(v.Dead ? "숨짐 " + v.Vitals.InjuryCause : "살았다")} · " + string.Join(" / ", w.History.Events.TakeLast(4).Select(h => h.Text)));
    }
}
