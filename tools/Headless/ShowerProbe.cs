using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// 운석 피해 살피기 (시험 아님): 운석우 · 미세 운석 · 잔해 구름 · 잔해형 대재난을 일으켜 몇 시간 동안
/// 누가 왜 죽었나 · 구멍이 몇 분 열려 있었나 · 공기가 빠진 방에 사람이 얼마나 있었나 · 드론이 얼마나 나갔나.
///   SHOWER_SHIPS=Mirinae,Hanbit SHOWER_SEEDS=1,2,3 SHOWER_KINDS=MeteorShower,Kessler SHOWER_HOURS=12 Headless --showerprobe
/// </summary>
public static partial class Program
{
    private static int RunShowerProbe(int seed)
    {
        string ships = Environment.GetEnvironmentVariable("SHOWER_SHIPS") ?? "Mirinae,Hanbit,Eunha,Saeteo";
        var seeds = (Environment.GetEnvironmentVariable("SHOWER_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse).ToArray();
        string kinds = Environment.GetEnvironmentVariable("SHOWER_KINDS") ?? "MeteorShower,MicroShower,DebrisCloud,Kessler,ShatteredPlanet";
        int hours = int.TryParse(Environment.GetEnvironmentVariable("SHOWER_HOURS"), out var hh) ? hh : 12;
        bool verbose = Environment.GetEnvironmentVariable("SHOWER_VERBOSE") == "1";
        Storyteller.PersonaValue = float.TryParse(Environment.GetEnvironmentVariable("SHOWER_PERSONA"), out var pv) ? pv : 0f; // 게임 기본: 예전 무작위 사고
        Storyteller.LevelValue = float.TryParse(Environment.GetEnvironmentVariable("SHOWER_LEVEL"), out var lv) ? lv : 3f;
        Console.WriteLine($"운석 피해 살피기 · {hours}시간 · 시드 {string.Join(",", seeds)}\n");
        foreach (var kind in kinds.Split(','))
        {
            int runs = 0, crew0Sum = 0, deadSum = 0, wipe = 0, hitsSum = 0, sealsSum = 0;
            double leakRoomMin = 0, lowCrewMin = 0, openMinSum = 0, openMax = 0, openN = 0, droneOutMin = 0;
            var causes = new Dictionary<string, int>();
            foreach (var ship in ships.Split(','))
            foreach (var sd in seeds)
            {
                var r = ShowerRun(ship, sd, kind, hours, verbose);
                if (r == null) continue;
                runs++;
                crew0Sum += r.Crew0;
                deadSum += r.Dead;
                if (r.Dead >= r.Crew0 && r.Crew0 > 0) wipe++;
                hitsSum += r.Breaches;
                sealsSum += r.Seals;
                leakRoomMin += r.LeakRoomMin;
                lowCrewMin += r.LowCrewMin;
                droneOutMin += r.DroneOutMin;
                foreach (var m in r.OpenMinutes) { openMinSum += m; openMax = Math.Max(openMax, m); openN++; }
                foreach (var c in r.Causes) causes[c] = causes.GetValueOrDefault(c) + 1;
                if (Environment.GetEnvironmentVariable("SHOWER_COSMIC") == "1")
                    Console.WriteLine($"    장면 {kind,-15} {ship,-9} 시드 {sd}: {r.Scene}");
                if (Environment.GetEnvironmentVariable("SHOWER_COSMIC") == "1")
                    Console.WriteLine($"    우주 {kind,-15} {ship,-9} 시드 {sd}: 부상 {r.Injury:0.00} · 피폭 합 {r.DoseSum:0.00} 최대 {r.DoseMax:0.00} · 고장 설비·분 {r.BrokenMin:0} 최대 {r.BrokenMax}대 · 정전 방·분 {r.DarkMin:0} · 쓰러짐·분 {r.DownMin:0} · 비킴 {(r.Avoided ? "예" : "아니오")} 연료 {r.Fuel:0} · 뚫림 {r.Breaches}");
                Console.WriteLine($"  {kind,-15} {ship,-9} 시드 {sd}: 사망 {r.Dead}/{r.Crew0} · 뚫림 {r.Breaches} · 막음 {r.Seals} · 새는 방·분 {r.LeakRoomMin:0} · 구멍 열린 시간 평균 {(r.OpenMinutes.Count > 0 ? r.OpenMinutes.Average() : 0):0}분 최대 {(r.OpenMinutes.Count > 0 ? r.OpenMinutes.Max() : 0):0}분 · 낮은 기압 속 사람·분 {r.LowCrewMin:0} · 드론 나간 분 {r.DroneOutMin:0}{(r.Causes.Count > 0 ? " · 사인 " + string.Join(", ", r.Causes) : "")}");
            }
            if (runs == 0) continue;
            Console.WriteLine($"■ {kind}: {runs}번 · 사망 {deadSum}/{crew0Sum} ({100.0 * deadSum / Math.Max(1, crew0Sum):0.0}%) · 전멸 {wipe}번 · 뚫림 {hitsSum} · 막음 {sealsSum} · 새는 방·분 {leakRoomMin / runs:0}/번 · 구멍 평균 {(openN > 0 ? openMinSum / openN : 0):0}분 최대 {openMax:0}분 · 낮은 기압 속 사람·분 {lowCrewMin / runs:0}/번 · 드론 나간 분 {droneOutMin / runs:0}/번");
            if (causes.Count > 0) Console.WriteLine("   사인: " + string.Join(", ", causes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            Console.WriteLine();
        }
        return 0;
    }

    private sealed class ShowerResult
    {
        public int Crew0, Dead, Breaches, Seals, BrokenMax;
        public double Injury, DoseSum, DoseMax, BrokenMin, DarkMin, DownMin, Fuel;
        public bool Avoided;
        public string Scene = "";
        public double LeakRoomMin, LowCrewMin, DroneOutMin;
        public List<double> OpenMinutes = new();
        public List<string> Causes = new();
    }

    private static ShowerResult? ShowerRun(string ship, int sd, string kind, int hours, bool verbose)
    {
        var w = World.CreateDefault(sd, 0, ship);
        w.CrewCanDie = true;
        for (int i = 0; i < SimTime.Hours(1); i++) w.Step();
        int crew0 = w.Crew.Count(c => !c.Dead && !c.LeftShip);
        int breaches0 = w.Ship.Walls.Sum(kv => kv.Value.Breaches), seals0 = w.Ship.Walls.Sum(kv => kv.Value.Seals);
        long start = w.Tick;
        string? what;
        if (kind == "None") what = "대조"; // 아무 일도 걸지 않는다 (평소 사망 · 구멍)
        else if (Enum.TryParse<HazardKind>(kind, out var hk)) what = Hazards.Apply(w, hk, default, -1);
        else if (Enum.TryParse<CosmicKind>(kind, out var ck))
        {
            // SHOWER_LEAD=natural: 실제 게임처럼 예보 시간을 표의 범위에서 · 끝날 때까지 (도착 + 단계 + 6시간) 지켜본다
            bool natural = Environment.GetEnvironmentVariable("SHOWER_LEAD") == "natural";
            if (float.TryParse(Environment.GetEnvironmentVariable("SHOWER_PROPELLANT"), out var prop)) w.Propulsion.Propellant = prop; // 추진제가 바닥난 배 (비키기 · 자세 제어 못 함)
            var ce = w.Cosmic.Force(ck, natural ? null : 1f);
            if (natural) hours = Math.Max(hours, (int)MathF.Ceiling((ce.Arrive - w.Tick) / (float)SimTime.TicksPerHour + ce.Spec.Span + (float.TryParse(Environment.GetEnvironmentVariable("SHOWER_AFTER"), out var af) ? af : 6f)));
            what = kind;
        }
        else return null;
        if (what == null) return null;
        var r = new ShowerResult { Crew0 = crew0 };
        if (Environment.GetEnvironmentVariable("SHOWER_FLEET") == "1")
            Console.WriteLine($"    {ship}: 드론 {string.Join(",", w.Drones.Drones.GroupBy(d => d.Kind).Select(g => $"{g.Key}×{g.Count()}"))} · 로봇 {string.Join(",", w.Robots.Robots.GroupBy(x => x.Kind).Select(g => $"{g.Key}×{g.Count()}"))} · 실링폼 {w.Ship.CountStored(ItemKind.Sealant)} · 우주복 {w.Ship.FurnitureOf(FurnitureType.SuitLocker).Count()}");
        var leakSince = new Dictionary<int, long>();
        var dose0 = w.Crew.ToDictionary(c => c.Id, c => c.Dose);
        var inj0 = w.Crew.ToDictionary(c => c.Id, c => c.Vitals.Injury);
        var injMax = new Dictionary<int, float>(inj0);
        var lastRoom = new Dictionary<int, string>();
        long end = start + SimTime.Hours(hours), step = SimTime.Minutes(1);
        while (w.Tick < end)
        {
            for (long k = 0; k < step; k++) w.Step();
            foreach (var room in w.Ship.Rooms)
            {
                bool leaking = !room.Detached && room.LeakArea > 0f;
                if (leaking)
                {
                    r.LeakRoomMin++;
                    if (!leakSince.ContainsKey(room.Id)) leakSince[room.Id] = w.Tick;
                }
                else if (leakSince.Remove(room.Id, out long t0)) r.OpenMinutes.Add((w.Tick - t0) / (double)SimTime.Minutes(1));
            }
            foreach (var c in w.Crew)
                if (!c.Dead && !c.LeftShip && !c.Away && c.Suit == null && c.Room is Room cr && cr.Air.Pressure < 60f) r.LowCrewMin++;
            if (Environment.GetEnvironmentVariable("SHOWER_WHO") is string who && w.Crew.FirstOrDefault(x => x.Name == who) is CrewMember wc && !wc.Dead)
                Console.WriteLine($"      +{(w.Tick - start) / SimTime.Minutes(1),3}분 {wc.Name} {wc.Room?.Name}({wc.Room?.Air.Pressure:0}kPa 위험{(wc.Room != null ? Atmosphere.Danger(wc.Room) : 0):0.00}) 칸{wc.Cell.X},{wc.Cell.Y} 일 {wc.Job?.Label}<{wc.Job?.Activity?.GetType().Name}> {wc.Job?.LogText} 급{wc.Job?.Urgent} 향함 {(wc.Destination is Cell wd ? w.Ship.RoomAt(wd)?.Name : "-")} 길{wc.Path?.Count ?? -1} 산소{wc.Vitals.Oxygen:0.00} 목표 {wc.Mind.GoalWhy}");
            if (Environment.GetEnvironmentVariable("SHOWER_WHO") is string who2 && w.Crew.FirstOrDefault(x => x.Name == who2) is CrewMember wc2 && !wc2.Dead && wc2.Job?.Activity is EvacuateActivity)
                Console.WriteLine("          방: " + string.Join(" / ", w.Ship.Rooms.Where(x => x.Name == "침실" || x.Name == "생명유지실").Select(x => $"{x.Name}#{x.Id} 위험{Atmosphere.Danger(x):0.00} 샘{x.Leaking} 불{w.Fire.CountIn(x)} 위협{w.Sensors.Threat(x)?.GetType().Name} 대피{x.EvacuateBy} 보류{x.ResponseHold} 믿음{w.Brain2.Beliefs.SafeEnough(wc2, x)} 잠금{x.Lockdown}")));
            if (Environment.GetEnvironmentVariable("SHOWER_DOSE") == "1" && (w.Tick - start) % SimTime.Minutes(30) == 0)
            {
                var hot = w.Crew.Where(x => !x.Dead && x.Room != null && w.Cosmic.Radiation(x.Room) > 0.3f).Select(x => $"{x.Name}@{x.Room!.Name}({w.Cosmic.Radiation(x.Room):0.0}Sv/h 누적{x.Dose:0.0}) {x.Job?.Label}").ToList();
                if (hot.Count > 0) Console.WriteLine($"      +{(w.Tick - start) / SimTime.Minutes(1),3}분 쬐는 사람 {hot.Count}: " + string.Join(" / ", hot.Take(8)));
                if (hot.Count > 0) Console.WriteLine("        방: " + string.Join(" / ", w.Ship.Rooms.Where(x => !x.Detached && x.Type != RoomType.Corridor).OrderBy(x => w.Cosmic.RelExposure(x)).Select(x => $"{x.Name} 상대{w.Cosmic.RelExposure(x):0.000} 실제{w.Cosmic.Radiation(x):0.0} 노출{w.Ambience.Exposure(x):0.00}")));
            }
            if (Environment.GetEnvironmentVariable("SHOWER_VITALS") == "1")
                foreach (var vc in w.Crew.Where(x => !x.Dead).Take(3))
                    Console.WriteLine($"      +{(w.Tick - start) / SimTime.Minutes(1),3}분 {vc.Name} {vc.Room?.Name} 체력{vc.Vitals.Health:0.00} 부상{vc.Vitals.Injury:0.00}({vc.Vitals.InjuryCause}) 산소{vc.Vitals.Oxygen:0.00} 피폭{vc.Dose:0.00} 방 O2 {vc.Room?.Air.O2:0.0} CO2 {vc.Room?.Air.CO2:0.00} {vc.Room?.Air.Temperature:0}도 연기{vc.Room?.Air.Smoke:0.00} 독{vc.Room?.Air.Toxin:0.00} CO{vc.Room?.Air.CO:0.00} 배고픔{vc.Needs.Hunger:0.00} 잠{vc.Needs.Rest:0.00}");
            if (Environment.GetEnvironmentVariable("SHOWER_ENTER") is string er)
                foreach (var c in w.Crew)
                {
                    if (c.Dead) continue;
                    string now = c.Room?.Name ?? "-";
                    if (lastRoom.TryGetValue(c.Id, out var was) && was != now && now == er)
                        Console.WriteLine($"      +{(w.Tick - start) / SimTime.Minutes(1),3}분 {c.Name} {was}({w.Ship.Rooms.FirstOrDefault(x => x.Name == was)?.Air.Pressure:0}kPa) → {now}({c.Room?.Air.Pressure:0}kPa) 일 {c.Job?.Label}<{c.Job?.Activity?.GetType().Name}> {c.Job?.LogText} 향함 {(c.Destination is Cell dd ? w.Ship.RoomAt(dd)?.Name : "-")} 산소{c.Vitals.Oxygen:0.00}");
                    lastRoom[c.Id] = now;
                }
            int every = int.TryParse(Environment.GetEnvironmentVariable("SHOWER_TRACE_EVERY"), out var ev) ? ev : 1;
            if (Environment.GetEnvironmentVariable("SHOWER_TRACE") is string tr && w.Tick - start <= SimTime.Minutes(int.TryParse(Environment.GetEnvironmentVariable("SHOWER_TRACE_MIN"), out var tm) ? tm : 60) && (w.Tick - start) / SimTime.Minutes(1) % every == 0)
            {
                var leaking = w.Ship.Rooms.Where(x => !x.Detached && (x.Leaking || x.Air.Pressure < 85f)).Select(x => $"{x.Name}{(x.Leaking ? "*" : "")} {x.Air.Pressure:0}kPa{(x.Lockdown ? " 잠금" : "")}{(x.LockPendingUntil >= 0 ? " 대기" : "")} 사람{w.Crew.Count(c => !c.Dead && c.Room == x)}");
                var where = w.Crew.Where(c => !c.Dead).GroupBy(c => c.Room?.Name ?? "-").Select(g => $"{g.Key}{g.Count()}");
                Console.WriteLine($"    +{(w.Tick - start) / SimTime.Minutes(1),3}분 {string.Join(" · ", leaking)} | 사람 {string.Join(",", where)} | 쓰러짐 {w.Crew.Count(c => c.Down && !c.Dead)} 죽음 {w.Crew.Count(c => c.Dead)} | 공기탱크 {w.Air.Reserve:0}");
                if (Environment.GetEnvironmentVariable("SHOWER_AIR") == "1")
                    Console.WriteLine($"        공기: 주컴 {(w.Automation.Present ? "있음" : "없음")} {(w.Automation.MainOnline ? "켜짐" : "꺼짐")} · 산소발생기 " + string.Join(",", w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Select(f => $"{f.Machine!.Efficiency:0.00}{(f.Room.Powered ? "" : " 전기없음")}"))
                        + " · " + string.Join(" / ", w.Ship.Rooms.Where(x => !x.Detached && x.Air.Pressure < 85f && !x.Leaking).Select(x => $"{x.Name} 전기{(x.Powered ? 1 : 0)} 댐퍼{(x.VentOpen ? 1 : 0)} 덕트{(x.DuctLinked ? 1 : 0)} 구역{(w.Automation.InZone(x) ? 1 : 0)}")) + $" · 구역{w.Automation.ZoneActive}");
                if (Environment.GetEnvironmentVariable("SHOWER_AIR") == "1")
                {
                    Console.WriteLine("        로봇: " + string.Join(" / ", w.Robots.Robots.Select(rb => $"{rb.Name}({rb.Kind}) {rb.State}@{rb.Room?.Name} \"{rb.Doing}\" 전지{rb.Battery:0.00} {(rb.Operational ? "" : "고장 ")}{(rb.Order is WorkOrder ro ? $"[{ro.Kind}:{ro.Target.CurrentRoom?.Name}]" : "")}")));
                    Console.WriteLine($"        끊긴 간선 {w.Net.Links.Count(l => l.Cut)}개 ({string.Join(",", w.Net.Links.Where(l => l.Cut).GroupBy(l => l.Room.Name + ":" + l.Cause).Select(g => g.Key + "×" + g.Count()))})"
                        + " · 일감: " + string.Join(" / ", w.Board.All.Where(o => o.Kind == WorkKind.RepairNet && !o.Closed).Select(o => $"{o.Target.CurrentRoom?.Name} 급{o.Urgency:0.00} 사람:{o.Assignee?.Name ?? "-"} 로봇:{w.Robots.Robots.FirstOrDefault(rb => rb.Order == o)?.Name ?? "-"} 막힘:{o.BlockedReason}")));
                }
                if (Environment.GetEnvironmentVariable("SHOWER_DRONES") == "1")
                {
                    Console.WriteLine("        드론: " + string.Join(" / ", w.Drones.Drones.Select(d => $"{d.Name}({d.Kind}) {d.State} 배터리{d.Battery:0.00} {d.Doing}{(d.Order is WorkOrder o ? $" [{o.Kind}:{o.Target.Room?.Name}]" : "")}")));
                    Console.WriteLine("        거치대: " + string.Join(" / ", w.Ship.FurnitureOf(FurnitureType.DroneDock).Select(f => $"실링폼{f.Storage?.Count(ItemKind.Sealant)} 금속판{f.Storage?.Count(ItemKind.Plate)}")) + $" · 배 실링폼 {w.Ship.CountStored(ItemKind.Sealant)} 금속판 {w.Ship.CountStored(ItemKind.Plate)}");
                    Console.WriteLine("        봉합 일감: " + string.Join(" / ", w.Board.All.Where(o => o.Kind == WorkKind.SealBreach && !o.Closed).Select(o => $"{o.Target.Room?.Name} 급{o.Urgency:0.00} 사람:{o.Assignee?.Name ?? "-"} 드론:{o.Drone?.Name ?? "-"}")));
                }
                if (Environment.GetEnvironmentVariable("SHOWER_WALLS") is string wr)
                    foreach (var (cell, wall) in w.Ship.Walls)
                        if (Hull.EffectiveBreach(wall) > 0f && (wr == "*" || wall.IsHull && Cell.Dirs4.Any(d => w.Ship.RoomAt(cell + d)?.Name == wr)))
                            Console.WriteLine($"        벽 {cell.X},{cell.Y} 파공{wall.Breach:0.00} 골조잃음{wall.FrameLost} 봉합{wall.Patched} 강도{wall.Integrity:0.00} 속한방 {Hull.InsideRoom(w.Ship, cell)?.Name}({(Hull.InsideRoom(w.Ship, cell)?.Abandoned == true ? "포기" : "")}) 일감 {string.Join(",", w.Board.All.Where(o => !o.Closed && o.Target.Kind == TargetKind.Wall && o.Target.Cell == cell).Select(o => o.Kind + (o.Drone != null ? ":" + o.Drone.Name : "") + (o.Assignee != null ? ":" + o.Assignee.Name : "")))}");
                if (Environment.GetEnvironmentVariable("SHOWER_WALLS") == "*")
                {
                    Console.WriteLine("        새는 방: " + string.Join(" / ", w.Ship.Rooms.Where(x => x.Air.Leak > 0f || x.BreachArea > 0f || x.Purging || x.Flushing).Select(x => $"{x.Name}{(x.Purging ? "(배기)" : "")}{(x.Flushing ? "(환기)" : "")} 떨어짐{x.Detached} 새는속도{x.Air.Leak:0.00} 파공{x.BreachArea:0.00} 기압{x.Air.Pressure:0} 댐퍼{(x.VentOpen ? 1 : 0)} 덕트{(x.DuctLinked ? 1 : 0)} 문[{string.Join(",", x.Doors.Select(d => $"{(d.RoomA == x ? d.RoomB : d.RoomA)?.Name}:잠{(d.Locked ? 1 : 0)}열{d.Openness:0.0}"))}]")) + $" · 배 공기 총량 {w.Ship.Rooms.Where(x => !x.Detached).Sum(x => x.Air.Pressure * x.Volume):0} · 탱크 {w.Air.Reserve:0}");
                    Console.WriteLine("        떨어진 방 문: " + string.Join(" / ", w.Ship.Doors.Where(d => d.RoomA?.Detached == true || d.RoomB?.Detached == true).Select(d => $"문{d.Id}({d.RoomA?.Name}|{d.RoomB?.Name}) 잠금{(d.Locked ? 1 : 0)} 열림{d.Openness:0.0}")) + " · 휜 문 " + string.Join(",", w.Ship.Doors.Where(d => d.Bent > 0.3f).Select(d => $"{d.RoomA?.Name}|{d.RoomB?.Name} 휨{d.Bent:0.0} 걸림{d.JammedOpen} 끼임{d.Blocked}")) + " · 문 일감 " + string.Join(",", w.Board.All.Where(o => !o.Closed && o.Kind == WorkKind.RepairDoor).Select(o => $"{o.Detail} 급{o.Urgency:0.0} 사람:{o.Assignee?.Name ?? "-"} 막힘:{o.BlockedReason}")) + " · 떨어진 방 댐퍼 " + string.Join(",", w.Ship.Rooms.Where(x => x.Detached).Select(x => $"{x.Name}:{(x.VentOpen ? "열림" : "닫힘")}")));
                }
                if (Environment.GetEnvironmentVariable("SHOWER_DOORS") is string dr)
                    foreach (var c in w.Crew.Where(c => !c.Dead && !c.Down && c.Room?.Name == dr && c.Path != null))
                    {
                        var doors = c.Path!.Skip(c.PathIndex).Select(x => w.Ship.DoorAt(x)).Where(d => d != null).Take(3)
                            .Select(d => $"문{d!.Id}({d.RoomA?.Name}|{d.RoomB?.Name}) 잠금{(d.Locked ? 1 : 0)} 열림{d.Openness:0.0} 차{MathF.Abs((d.RoomA?.Air.Pressure ?? 0) - (d.RoomB?.Air.Pressure ?? 0)):0}");
                        Console.WriteLine($"        {c.Name} 칸{c.Cell.X},{c.Cell.Y} 길{c.Path.Count - c.PathIndex} 앞문: {string.Join(" · ", doors)}");
                    }
                if (Environment.GetEnvironmentVariable("SHOWER_JOBS") is string jr)
                    Console.WriteLine("        일: " + string.Join(" / ", w.Crew.Where(c => !c.Dead && c.Room?.Name == jr).Select(c => $"{c.Name}:{(c.Down ? "쓰러짐 " : "")}{c.Job?.Label ?? "-"}{(c.Job?.Activity is Activity a ? "<" + a.GetType().Name + ">" : "")} 산소{c.Vitals.Oxygen:0.00}{(c.Suit != null ? " 우주복" : "")} 칸{c.Cell.X},{c.Cell.Y}→{(c.Destination is Cell dd ? $"{dd.X},{dd.Y}({w.Ship.RoomAt(dd)?.Name})" : "-")} 길{c.Path?.Count ?? -1} {c.Job?.Current?.GetType().Name}")));
            }
            r.DroneOutMin += w.Drones.Drones.Count(d => d.State is DroneState.Outbound or DroneState.Working or DroneState.Returning);
            int broken = w.Ship.Machines.Count(m => m.Faults.Count > 0);
            r.BrokenMin += broken; r.BrokenMax = Math.Max(r.BrokenMax, broken);
            r.DarkMin += w.Ship.Rooms.Count(x => !x.Detached && !x.Abandoned && !x.Powered);
            foreach (var c in w.Crew)
            {
                if (c.Down && !c.Dead) r.DownMin++;
                if (injMax.TryGetValue(c.Id, out var im) && c.Vitals.Injury > im) injMax[c.Id] = c.Vitals.Injury;
            }
        }
        foreach (var (_, t0) in leakSince) r.OpenMinutes.Add((w.Tick - t0) / (double)SimTime.Minutes(1));
        foreach (var c in w.Crew.Where(c => c.Dead && c.DiedAt >= start))
        {
            r.Dead++;
            r.Causes.Add($"{c.Vitals.InjuryCause ?? "?"}@{c.Room?.Name ?? "-"}+{(c.DiedAt - start) / (double)SimTime.TicksPerHour:0.0}h");
        }
        r.Breaches = w.Ship.Walls.Sum(kv => kv.Value.Breaches) - breaches0;
        r.Injury = injMax.Sum(kv => kv.Value - inj0[kv.Key]);
        var doses = w.Crew.Where(c => dose0.ContainsKey(c.Id)).Select(c => (double)(c.Dose - dose0[c.Id])).ToList();
        r.DoseSum = doses.Sum(); r.DoseMax = doses.DefaultIfEmpty(0).Max();
        r.Avoided = w.Cosmic.Events.Any(e => e.Avoided);
        r.Scene = string.Join(" | ", w.Cosmic.Events.Select(e => $"선택 {e.SceneChoice} · 횟수 {e.SceneCount} · 늘어남 {e.Stretch:0.00} · 어긋남 {e.DriftMin:0}분 · 생존자 {e.SceneSurvivors.Count} · 장면일 {string.Join(",", e.Tasks.Where(t => t.Kind is BraceKind.Barricade or BraceKind.Decon or BraceKind.Logbook).Select(t => $"{CosmicSystem.KindName(t.Kind)}{(t.Done ? "✓" : "")}"))} · 대비 {e.Tasks.Count(t => t.Done)}/{e.Tasks.Count}{(Environment.GetEnvironmentVariable("SHOWER_TASKS") == "1" ? " [" + string.Join(",", e.Tasks.Select(t => t.Label + (t.Done ? "✓" : "✗"))) + "]" : "")}"))
            + " · 기록: " + string.Join(" / ", w.History.Events.Where(h => h.Tick >= start && (h.Text.Contains("해적") || h.Text.Contains("탈출정") || h.Text.Contains("돌렸다") || h.Text.Contains("떼어") || h.Text.Contains("뜯어") || h.Text.Contains("암흑") || h.Text.Contains("블랙홀") || h.Text.Contains("제염") || h.Text.Contains("주 컴퓨터에 적어"))).Select(h => h.Text).Distinct().Take(6));
        r.Fuel = w.Cosmic.Events.Sum(e => e.FuelSpent);
        r.Seals = w.Ship.Walls.Sum(kv => kv.Value.Seals) - seals0;
        if (verbose)
            foreach (var e in w.Log.Entries.Where(e => e.Tick >= start && e.Kind == LogKind.Warning).Take(60))
                Console.WriteLine($"      {SimTime.Clock(e.Tick)} {e.Text}");
        return r;
    }
}
