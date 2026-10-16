using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v19 2묶음: 생활 측정 — 같은 배 · 시드 · 인원 · 사고로 굶주림 · 탈진의 원인과 고정 비교 지표를 잰다.
//   days 시드 --lifeprobe [--ship=Saeteo] [--crew=60] [--incidents=fixed|natural|none|storm] [--daily]
//     fixed: 이틀마다 같은 시각에 같은 무작위 사고 (시드에서 나온 차례 그대로) · natural: 게임처럼 평균 3일에 한 번 · none: 사고 없음
//     storm: 사흘째 새벽 같은 시각에 센 태양 폭풍 (세기 3.3 · 아홉 시간으로 고정)
//     --daily: 하루마다 한 줄 · 환경 변수 LP_TRACE=1: 폭풍 · 굶주림 동안 30분마다 식량 · 잠 · 대피소 상태 (폭풍엔 쬐는 사람도)
//     LP_COORD=1: 복구 계획이 도는 동안 두 시간마다 계획 단계 · 급한 일 · 조율 결정 (계획이 셋 넘게 몰리면 그 전 기록)
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
        // --daily: 하루마다 한 줄 (긴 항해에서 끼니 · 잠이 서서히 무너지는지)
        bool daily = args.Contains("--daily"), trace = Environment.GetEnvironmentVariable("LP_TRACE") == "1";
        long dayStart = w.Tick;
        float h0 = 0f, t0 = 0f; int mb0 = 0, sb0 = 0, ex0 = 0;
        Dictionary<string, float> sbw0 = new(), tw0 = new(), crisisWhy = new();
        int crisisMin = 0, acuteMin = 0;
        bool coordTrace = Environment.GetEnvironmentVariable("LP_COORD") == "1"; // 원자로가 멎은 동안 두 시간마다: 계획 단계 · 급한 일 · 조율 결정
        int seenDecisions = 0;
        bool dumped = false;
        while (w.Tick < end)
        {
            w.Step();
            if (coordTrace && !dumped && w.Automation.Recovery.Open.Count() >= 3) // 계획이 셋 넘게 한꺼번에 — 그 전 기록
            {
                dumped = true;
                Console.WriteLine($"   [기록] 계획 {w.Automation.Recovery.Open.Count()}개가 동시에 — 앞선 기록:");
                foreach (var e in w.Log.Entries.Where(e => w.Tick - e.Tick < SimTime.Hours(6) && e.Kind is LogKind.Ship or LogKind.Warning).TakeLast(40))
                    Console.WriteLine($"     {SimTime.Clock(e.Tick)} {e.Text}");
            }
            if (coordTrace && w.Tick % SimTime.Hours(2) == 0 && (!w.Power.ReactorOnline || w.Automation.Recovery.Open.Any()))
            {
                var co = w.Automation.Coord;
                Console.WriteLine($"   [조율] {(w.Tick - dayStart) / SimTime.TicksPerDay + 1}일 {SimTime.Clock(w.Tick)} 원자로 {(w.Power.ReactorOnline ? "돎" : "멎음")} {w.Power.ReactorTemperature:0}℃ · 배터리 {w.Power.BatteryCharge:0}/{w.Power.BatteryCapacity:0} · {co.Line}");
                foreach (var pl in w.Automation.Recovery.Open)
                    Console.WriteLine($"     계획 {pl.Problem} {pl.Name}: " + string.Join(" → ", pl.Steps.Select((st, i) => $"{(i == pl.Cur ? "▶" : "")}{st.Name}[{st.State}{(st.Waiting != "" ? " " + st.Waiting : "")}]")));
                foreach (var o in w.Board.Open.Where(o => !o.Closed && o.Urgency >= 0.85f).OrderByDescending(o => o.Urgency).Take(6))
                    Console.WriteLine($"     일 {o.Title} {o.Urgency:0.##} · {(o.Assignee?.Name ?? o.Robot?.Name ?? (o.Drone != null ? "드론" : "—"))} · {o.Progress * 100:0}% · {o.BlockedReason}");
                foreach (var d in co.Decisions.Skip(Math.Min(seenDecisions, co.Decisions.Count))) Console.WriteLine($"     결정 {SimTime.Clock(d.tick)} {d.text}");
                seenDecisions = co.Decisions.Count;
            }
            if (daily && (w.Tick - dayStart) % SimTime.TicksPerDay == 0)
            {
                var lw0 = w.LifeWatch;
                var live = w.Crew.Where(c => !c.Dead && !c.LeftShip).ToList();
                Console.WriteLine($"  {(w.Tick - dayStart) / SimTime.TicksPerDay}일째: 굶주림 {lw0.HungryHours - h0:0.#} · 탈진 {lw0.TiredHours - t0:0.#}사람·시간 · 식사 중단 {lw0.MealBreaks - mb0} · 잠 중단 {lw0.SleepBreaks - sb0} · 탈진번 {exhaustedEp - ex0} · 포만 평균 {live.Average(c => c.Needs.Food):0.00} · 기력 평균 {live.Average(c => c.Needs.Rest):0.00} · 산 사람 {live.Count} · 식사 {w.Board.Have(ItemKind.Meal)}인분");
                // 그날 잠을 끊은 것 · 지친 까닭 · 겪은 사고 (앞날과의 차이)
                string Delta(Dictionary<string, float> now, Dictionary<string, float> was) => string.Join(" · ", now.Select(kv => (kv.Key, d: kv.Value - was.GetValueOrDefault(kv.Key))).Where(x => x.d > 0.05f).OrderByDescending(x => x.d).Take(4).Select(x => $"{x.Key} {x.d:0.#}"));
                var sbw = lw0.SleepBreakWhy.ToDictionary(kv => kv.Key, kv => (float)kv.Value);
                var evs = w.History.Events.Where(h => h.Kind == HistoryKind.Incident && h.Tick > w.Tick - SimTime.TicksPerDay).Select(h => h.Text).Take(4);
                var lowRoom = w.Crew.Where(c => !c.Dead && c.Room != null && c.Suit == null).Select(c => c.Room!).Distinct().OrderBy(r => r.Air.O2).FirstOrDefault();
                Console.WriteLine($"     비상 {crisisMin / 60f:0.#}시간 (오래 끈 {acuteMin / 60f:0.#}) · {string.Join(" · ", crisisWhy.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value:0.#}"))} · 산소 발생 {w.Air.O2Capacity:0.00} · 탱크 {w.Air.Reserve:0}/{w.Air.ReserveCapacity:0} · 가장 낮은 방 {lowRoom?.Name} O2 {lowRoom?.Air.O2:0.0} CO2 {lowRoom?.Air.CO2:0.00} ({w.Crew.Count(c => !c.Dead && c.Room == lowRoom)}명 · 환기 {(lowRoom == null ? "-" : $"{(lowRoom.Powered ? "전기" : "정전")}/{(lowRoom.VentOpen ? "댐퍼 열림" : "댐퍼 닫힘")}/{(lowRoom.DuctLinked ? "관 연결" : "관 없음")}")}) · 환기 안 되는 방 {string.Join(",", w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && !Atmosphere.Vented(r)).Select(r => r.Name).Take(6))}");
                crisisMin = 0; acuteMin = 0; crisisWhy.Clear();
                var recs = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).ToList();
                Console.WriteLine($"     물 {w.Water.Level:0}/{w.Water.Capacity:0}L · 오수 {w.Water.Grey:0}/{w.Water.GreyCapacity:0}L · 정수 {w.Water.Produced:0.0}L/h · 씀 {w.Water.Consumed:0.0}L/h · 오수 유입 {w.Water.Inflow:0.0}L/h · 회수관 {w.Water.Pass * 100:0}% (찌꺼기 {w.Water.Sludge:0.00}) · 정수기 {recs.Count}대 효율 {(recs.Count > 0 ? recs.Average(f => f.Machine!.Efficiency) : 0f):0.00} 놀림 {w.Water.Starved * 100:0}%");
                Console.WriteLine($"     물 순환 누적: {w.Water.Loop.Line()}");
                Console.WriteLine($"     잠 끊김 {Delta(sbw, sbw0)} | 지침 {Delta(lw0.TiredWhy, tw0)} | 사고 {string.Join(" / ", evs)}");
                sbw0 = sbw; tw0 = new Dictionary<string, float>(lw0.TiredWhy);
                h0 = lw0.HungryHours; t0 = lw0.TiredHours; mb0 = lw0.MealBreaks; sb0 = lw0.SleepBreaks; ex0 = exhaustedEp;
            }
            if (mode == "fixed" && w.Tick == next)
            {
                what.Add($"{w.Day}일 {SimTime.Clock(w.Tick)} " + Scenarios.RandomIncident(w, rng));
                next += 2L * SimTime.TicksPerDay;
            }
            // storm: 둘째 날 아침 끼니 무렵 태양 폭풍 (같은 시각 · 같은 세기 — 폭풍 속 식사 · 잠 비교)
            if (mode == "storm" && w.Tick == next - SimTime.TicksPerDay + SimTime.Hours(23))
            {
                what.Add($"{w.Day}일 {SimTime.Clock(w.Tick)} " + Hazards.Apply(w, HazardKind.SolarStorm, default, -1));
                // 세기 · 길이를 고정한다 (난수에서 뽑으면 행동이 바뀔 때마다 다른 폭풍과 비교하게 된다) — 센 양성자 폭풍 아홉 시간
                w.Hazards.StormPeak = 3.3f;
                w.Hazards.StormUntil = w.Tick + SimTime.Hours(9);
            }
            if (w.Tick % SimTime.Minutes(5) != 0) continue;
            if (daily && Crisis.Acting(w))
            {
                crisisMin += 5; if (Crisis.Chronic(w)) acuteMin += 5;
                foreach (var why in Crisis.Now(w).Reasons) crisisWhy[why.Split(' ')[0]] = crisisWhy.GetValueOrDefault(why.Split(' ')[0]) + 5f / 60f;
            }
            if (trace && w.Tick % SimTime.Minutes(30) == 0 && (w.Ambience.StormPower > 0.05f || w.Crew.Count(c => !c.Dead && c.Needs.Food < 0.15f) > 3))
            {
                var ship2 = w.Ship;
                int fr = ship2.FurnitureOf(FurnitureType.Fridge).Sum(f => f.Storage!.Count(ItemKind.Meal));
                int dsp = ship2.FurnitureOf(FurnitureType.MealDispenser).Sum(f => f.Storage!.Count(ItemKind.Meal));
                int rat = ship2.CountStored(ItemKind.Ration), prod = ship2.CountStored(ItemKind.Produce);
                var cooks = w.Board.OpenUnsorted.Where(o => o.Kind == WorkKind.Cook).Select(o => $"{o.Target.Furniture?.Room.Name}{(o.Target.Furniture?.Room.Radiation >= 0.2f ? "☢" : "")}:{o.Urgency:0.00}{(o.Assignee != null ? "+" : o.BlockedUntil > w.Tick ? "보류" : "")}");
                var hot = string.Join(",", ship2.Rooms.Where(r => r.Radiation >= 0.05f && r.Type is RoomType.Mess or RoomType.Galley or RoomType.Storage or RoomType.Freezer or RoomType.Shelter).Select(r => $"{r.Name}{r.Radiation:0.00}"));
                var jobs = w.Crew.Where(c => !c.Dead).GroupBy(c => c.Job?.Label ?? "-").OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key} {g.Count()}");
                Console.WriteLine($"  [{w.Day}일 {SimTime.Clock(w.Tick)}] 폭풍 {w.Ambience.StormPower:0.00} · 냉장 {fr} · 배식 {dsp} · 비상 {rat} · 채소 {prod} · 굶주림 {w.Crew.Count(c => !c.Dead && c.Needs.Food < 0.15f)} · 기력 {w.Crew.Where(c => !c.Dead).Average(c => c.Needs.Rest):0.00} 지침 {w.Crew.Count(c => !c.Dead && c.Needs.Rest < 0.15f)} 잠 {w.Crew.Count(c => !c.Dead && c.Pose == Pose.Sleeping)} 대피소잠 {w.Crew.Count(c => !c.Dead && c.Pose == Pose.Sleeping && c.Room?.Kind == RoomType.Shelter)} 대피소 {string.Join(",", w.Ship.Rooms.Where(r => r.Kind == RoomType.Shelter).Select(r => $"{w.Crew.Count(c => !c.Dead && c.Room == r)}명 잠질{AmbienceSystem.SleepFactor(r):0.00} 소음{r.Noise:0.0} O2 {r.Air.O2:0.0} CO2 {r.Air.CO2:0.0} 기력{w.Crew.Where(c => !c.Dead && c.Room == r).Select(c => c.Needs.Rest).DefaultIfEmpty(0f).Average():0.00}"))} 숨참{w.Crew.Count(c => !c.Dead && c.Vitals.Oxygen < 0.85f)} · 조리일 [{string.Join(" ", cooks)}] · 쬐는 방 [{hot}] · 하는 일 {string.Join(" · ", jobs)}");
                if (w.Ambience.StormPower > 0.05f)
                    Console.WriteLine("     쬐는 사람 " + string.Join(" · ", w.Crew.Where(c => !c.Dead && (c.Outside || c.Room?.Radiation >= 0.2f)).GroupBy(c => $"{(c.Outside ? "밖" : c.Room!.Name)}:{c.Job?.Label ?? "-"}{(c.Pose == Pose.Sleeping ? "(잠)" : c.Down ? "(쓰러짐)" : "")}").OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key} {g.Count()}")));
            }
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
        { var alive2 = w.Crew.Where(c => !c.Dead).ToList(); Console.WriteLine($"피폭: 1Sv 넘음 {alive2.Count(c => c.Dose > 1f)}명 · 0.5Sv 넘음 {alive2.Count(c => c.Dose > 0.5f)}명 · 평균 {alive2.Average(c => c.Dose):0.00}Sv · 최대 {alive2.Max(c => c.Dose):0.00}Sv · 방사선 병 {alive2.Count(c => c.Ailments.Any(a => a.Id == "radiation"))}명"); }
        if (what.Count > 0) Console.WriteLine("사고: " + string.Join(" / ", what));
        if (mode == "natural") Console.WriteLine("겪은 사고: " + string.Join(" / ", w.History.Events.Where(h => h.Kind == HistoryKind.Incident).Select(h => $"{SimTime.Clock(h.Tick)} {h.Text}").Take(12)));
        return 0;
    }
}
