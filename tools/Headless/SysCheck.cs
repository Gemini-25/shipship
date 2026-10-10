using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// 모든 배에서 주컴퓨터 · 로봇 · 드론이 실제로 일하나 (--syscheck [--only=Key,Key] [--days=N]).
///   ① 평상시 N일 (기본 1): 로봇이 한 일 · 멈춰 선 로봇 (일하러 나가서 세 시간 넘게 제자리) · 드론 출격 · 잃은 드론 ·
///      주컴퓨터가 켜져 있나 · 사람이 있는 방 공기(산소 · 이산화탄소 · 온도) · 주컴퓨터가 아는 배터리 용량과 실제
///   ② 주방 불: 주컴퓨터가 알아채고 알리나 · 방재 로봇 출동 · 꺼지기까지
///   ③ 외벽 운석: 방까지 구멍이 나면 막히기까지 · 드론 출격 · 주컴퓨터가 알리나 (구멍이 안 나면 다른 방에 다시 — 세 번까지)
///   ④ 주컴퓨터 고장(저장장치): 사람이 고쳐 다시 켜기까지 · 꺼진 동안 난 불도 꺼지나 · 꺼진 동안 로봇이 일하나
/// 배마다 한 줄 + 문제 목록. 문제가 있으면 1.
/// </summary>
public static partial class Program
{
    private static int _airShown;
    private static int RunSysCheck(int seed, string[] args)
    {
        var only = args.FirstOrDefault(a => a.StartsWith("--only="))?[7..].Split(',');
        int days = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--days="))?[7..], out var dd) ? Math.Max(1, dd) : 1;
        var keys = ShipCatalog.All.Select(t => t.Key).Where(k => only == null || only.Contains(k)).ToList();
        Console.WriteLine($"주컴퓨터 · 로봇 · 드론 — 모든 배 실제 점검 · 시드 {seed} · 평상시 {days}일 · {keys.Count}척\n");
        var problems = new List<string>();
        foreach (var key in keys)
        {
            var w = DayOne(seed, key);
            string name = w.Ship.Name;
            var a = w.Automation;
            int robots = w.Robots.Robots.Count, drones = w.Drones.Drones.Count;
            bool online0 = a.Present && a.MainOnline;
            int Said(long t0) => w.Log.Entries.Where(e => e.Tick >= t0).Count(e => e.Text.Contains("주 컴퓨터") || e.Text.Contains(a.Voice.Call) || e.Text.Contains("[방송]"));

            // ① 평상시 N일
            int rj0 = w.Robots.JobsDone, ds0 = w.Drones.Sorties, dj0 = w.Drones.JobsDone;
            int lost0 = w.Drones.Drones.Count(d => d.State is DroneState.Lost && !d.OnTrip), rlost0 = w.Robots.Robots.Count(r => r.State == RobotState.Lost);
            var last = w.Robots.Robots.ToDictionary(r => r.Id, r => (pos: r.Position, since: w.Tick, doing: r.Doing));
            var stuck = new Dictionary<int, string>();
            int offlineMin = 0, badAirMin = 0;
            float worstO2 = 99f, worstCO2 = 0f, batLow = 1f; int shortMin = 0, shed0 = w.Failsafe.ShedEvents;
            string worstAir = "";
            for (int m = 0; m < days * 24 * 6; m++)
            {
                Run(w, SimTime.Minutes(10));
                if (!a.MainOnline) offlineMin += 10;
                foreach (var r in w.Robots.Robots)
                {
                    var l = last[r.Id];
                    bool moved = (r.Position - l.pos).Length() > 0.6f;
                    if (moved || r.State != RobotState.Active || r.Helping != null) { last[r.Id] = (r.Position, w.Tick, r.Doing); continue; }
                    if (w.Tick - l.since > SimTime.Hours(3) && !stuck.ContainsKey(r.Id))
                        stuck[r.Id] = $"{r.Name}({r.KindName}) {r.Room?.Name ?? "?"} · {r.Doing}";
                }
                bool bad = false;
                foreach (var room in w.Ship.LiveRooms)
                {
                    if (!w.Crew.Any(c => !c.Dead && !c.Outside && c.Room == room)) continue; // 사람이 있는 방만 (비운 방 · 닫은 방은 빼고)
                    var air = room.Air;
                    if (air.O2 < worstO2) { worstO2 = air.O2; worstAir = $"{room.Name} {SimTime.Clock(w.Tick)}"; }
                    worstCO2 = MathF.Max(worstCO2, air.CO2);
                    bad |= air.O2 < 17f || air.CO2 > 1.5f || air.Temperature < 10f || air.Temperature > 35f;
                    if (args.Contains("--airdebug") && (air.CO2 > 1.5f || air.O2 < 17f) && _airShown++ < 8)
                        Console.WriteLine($"     공기: {room.Name} {SimTime.Clock(w.Tick)} CO2 {air.CO2:0.00} O2 {air.O2:0.0} · 사람 {w.Crew.Count(c => !c.Dead && c.Room == room)} · 환기 {Atmosphere.Vented(room)}(댐퍼 {room.VentOpen} 덕트 {room.DuctLinked} 원함 {Hull.WantVentOpen(w, room)} 감염 {w.Infection.Shut(room)} 소화 {w.Automation.KeepDamperShut(room)} 봉 {room.VentSealed} 가스 {w.Hazards.GasSource(room) != null} 걸림 {room.DamperJammed}/{room.DamperStuck} 자동 {w.Automation.DampersIn(room)}) · 전기 {room.Powered} · 공기망 {room.AirFlow:0.00} · 부피 {room.Volume:0} · 정화 {w.Air.CO2Scrubbed:0.0}/능력 {w.Air.O2Capacity:0.0}");
                }
                if (bad) badAirMin += 10;
                batLow = MathF.Min(batLow, w.Power.BatteryPercent);
                // 고장 처리 중(배전반 고장 · 임시 배선 · 죽은 회로)은 빼고 — 평소 전기가 모자란 배만 잡는다
                if (w.Power.ShedCount > 0 && !w.Power.Jumpers.Any(j => j.Active) && w.Power.CircuitLive.All(x => x) && !w.Ship.FurnitureOf(FurnitureType.PowerPanel).Any(f => f.Machine!.Faults.Count > 0)) { shortMin += 10; if (args.Contains("--powerdebug") && shortMin <= 120) Console.WriteLine($"     전기: {SimTime.Day(w.Tick)}일 {SimTime.Clock(w.Tick)} 못 받음 {w.Power.ShedCount} · 수요 {w.Power.Demand:0}kW · 원자로 {(w.Power.ReactorOnline ? "돎" : "멈춤")} 한도 {w.Power.ReactorLimit:0}kW · 배터리 {w.Power.BatteryPercent * 100:0}% 흐름 {w.Power.BatteryFlow:0} · 계전기 {w.Failsafe.ShedLevel} · 꺼진 방 {w.Ship.LiveRooms.Count(r => !r.Powered)} · 끊긴 회로 {string.Join("", Enumerable.Range(0, PowerGrid.CircuitCount).Where(i => !w.Power.CircuitFed[i]).Select(PowerGrid.CircuitName))} · 죽은 회로 {string.Join("", Enumerable.Range(0, PowerGrid.CircuitCount).Where(i => !w.Power.CircuitLive[i]).Select(PowerGrid.CircuitName))} · 임시 배선 {string.Join(",", w.Power.Jumpers.Where(j => j.Active).Select(j => $"{PowerGrid.CircuitName(j.From)}→{PowerGrid.CircuitName(j.To)} {j.Load:0}/{j.Capacity:0}kW"))} · 배터리 용량 {w.Power.BatteryCapacity:0}kWh · 고장 {string.Join(",", w.Ship.Machines.Where(m => m.Faults.Count > 0).Select(m => $"{m.Name}:{string.Join("/", m.Faults.Select(f => $"{f.Kind}[{string.Join("+", f.Materials.Select(x => $"{ItemKinds.Name(x.kind)}{x.count}(배에 {w.Ship.CountStored(x.kind)})"))}]"))}"))}{(w.Board.Open.FirstOrDefault(o => o.Target.Furniture?.Type == FurnitureType.PowerPanel && o.Assignee != null)?.Assignee is CrewMember pa ? $" · 맡은 사람 {pa.Name} {pa.Room?.Name} {pa.Job?.Label} {pa.Job?.Current?.GetType().Name} 진행 {w.Board.Open.First(o => o.Assignee == pa).Progress:0.00} 칸 {pa.Cell} 길 {pa.PathIndex}/{pa.Path?.Count} 목적 {pa.Destination} 걸음 {pa.Gait.Line(pa, w) ?? "-"} 막힘 {pa.Gait.Blocked} 손 {pa.Carrying?.Kind} 깸 {pa.IsAwake} 기력 {pa.Needs.Rest:0.00} 방전기 {pa.Room?.Powered}" : "")} · 일감 {string.Join(" | ", w.Board.Open.Where(o => o.Target.Furniture?.Type == FurnitureType.PowerPanel || o.Kind is WorkKind.ResetBreaker or WorkKind.RestoreCircuit).Select(o => $"{o.Title}/{o.Assignee?.Name ?? o.Robot?.Name ?? "-"}{(o.BlockedUntil > w.Tick ? "/막힘 " + o.BlockedReason : "")}"))}"); }
            }
            int shedN = w.Failsafe.ShedEvents - shed0, rjobs = w.Robots.JobsDone - rj0, dsort = w.Drones.Sorties - ds0, djobs = w.Drones.JobsDone - dj0;
            int faulty = w.Robots.Robots.Count(r => !r.Operational), dfaulty = w.Drones.Drones.Count(d => !d.Operational);
            int dlost = w.Drones.Drones.Count(d => d.State is DroneState.Lost && !d.OnTrip) - lost0, rlost = w.Robots.Robots.Count(r => r.State == RobotState.Lost) - rlost0;
            if (args.Contains("--dronedebug") && (dlost > 0 || dfaulty > 0))
            {
                foreach (var d in w.Drones.Drones) Console.WriteLine($"     드론 {d.Name} {d.KindName} {d.State} · {d.Doing} · 배터리 {d.Battery:0.00}");
                var dn = w.Drones.Drones.Select(d => d.Name).ToList();
                foreach (var e in w.Log.Entries.Where(e => dn.Any(n => e.Text.Contains(n)) || e.Text.Contains("떠내려") || e.Text.Contains("드론")).TakeLast(40)) Console.WriteLine($"     {SimTime.Clock(e.Tick)} d{e.Tick / SimTime.TicksPerDay} {e.Text}");
            }
            var (bKwh, bCap) = a.Review.BatteryBelief();
            float realCap = w.Power.BatteryCapacity;
            float batErr = realCap > 1f ? MathF.Abs(bCap - realCap) / realCap : 0f;

            // ② 주방 불
            var galley = w.Ship.RoomsOf(RoomType.Galley).FirstOrDefault(r => !r.Detached);
            string fireRes = "주방 없음";
            int compFire = 0, ff0 = w.Robots.FiresFought;
            bool fireOut = true;
            if (galley != null)
            {
                var spot = galley.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - galley.Center).LengthSquared()).First();
                Player.Fire(w, spot);
                long t0 = w.Tick, outAt = -1;
                for (int t = 0; t < SimTime.Hours(4) && outAt < 0; t += SimTime.Minutes(1))
                {
                    Run(w, SimTime.Minutes(1));
                    if (w.Fire.Count == 0) outAt = w.Tick;
                }
                compFire = Said(t0);
                if (args.Contains("--firedebug")) { Console.WriteLine($"     컴퓨터 이름 '{a.Voice.Call}'"); foreach (var e in w.Log.Entries.Where(e => e.Tick >= t0).Take(40)) Console.WriteLine($"     {SimTime.Clock(e.Tick)} [{e.Kind}] {e.Text}"); }
                fireOut = outAt > 0;
                fireRes = fireOut ? $"{(outAt - t0) / (float)SimTime.Minutes(1):0}분" : "4시간 넘게 탐";
                Run(w, SimTime.Hours(1));
            }
            int fought = w.Robots.FiresFought - ff0;

            // ③ 외벽 운석 — 방까지 구멍이 안 나면 다른 바깥 방에 다시 (세 번까지)
            var outers = new[] { RoomType.Storage, RoomType.Cargo, RoomType.Workshop, RoomType.Hydroponics, RoomType.Quarters, RoomType.Lounge }
                .SelectMany(t => w.Ship.LiveRooms.Where(r => r.Kind == t)).Where(r => w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r)).Distinct().Take(3).ToList();
            string holeRes = "대상 없음";
            int dsort2 = 0, compHole = 0, throws = 0, dodged = 0;
            bool sealedOk = true;
            if (outers.Count > 0)
            {
                int dsB = w.Drones.Sorties;
                long tAll = w.Tick, hitAt = -1, shutAt = -1;
                int OpenHoles() => w.Ship.Walls.Count(kv => Hull.EffectiveBreach(kv.Value) > 0.01f && Cell.Dirs4.Any(d => w.Ship.RoomAt(kv.Key + d) is Room rr && !rr.Detached)); // 방으로 새는 구멍만 (방에 안 닿은 모서리 긁힘은 빼고) · 미세 누출(4%)도 구멍이다
                foreach (var outer in outers)
                {
                    throws++;
                    Player.Meteor(w, Scenarios.OuterTarget(w, outer), 1f);
                    // 1분마다 본다 (컴퓨터 · 사람이 몇 분 안에 막으면 5분 간격으로는 못 봤다)
                    for (int t = 0; t < SimTime.Minutes(50) && hitAt < 0; t += SimTime.Minutes(1)) { Run(w, SimTime.Minutes(1)); if (OpenHoles() > 0) hitAt = w.Tick; }
                    if (Environment.GetEnvironmentVariable("HOLEDBG") == "1")
                    {
                        var tg = Scenarios.OuterTarget(w, outer);
                        foreach (var kv in w.Ship.Walls.Where(kv => kv.Value.IsHull && (kv.Key.Center - tg.Center).Length() < 4f))
                            Console.WriteLine($"     {outer.Name} 벽 {kv.Key} 구멍 {kv.Value.Breach:0.00} 실효 {Hull.EffectiveBreach(kv.Value):0.00} 땜 {kv.Value.Patched} 강도 {kv.Value.Integrity:0.00}");
                        foreach (var e in w.Log.Entries.Where(e => e.Tick >= tAll).Where(e => e.Text.Contains("운석") || e.Text.Contains("파공") || e.Text.Contains("구멍")).Take(12)) Console.WriteLine($"     {SimTime.Clock(e.Tick)} {e.Text}");
                    }
                    if (hitAt >= 0) break;
                }
                for (int t = 0; t < SimTime.Hours(14) && hitAt >= 0 && shutAt < 0; t += SimTime.Minutes(1))
                {
                    Run(w, SimTime.Minutes(1));
                    if (OpenHoles() == 0) shutAt = w.Tick;
                }
                dsort2 = w.Drones.Sorties - dsB;
                if (args.Contains("--holedebug") && hitAt >= 0 && shutAt < 0)
                {
                    foreach (var kv in w.Ship.Walls.Where(kv => Hull.EffectiveBreach(kv.Value) > 0.01f))
                        Console.WriteLine($"     열린 벽 {kv.Key} · 구멍 {kv.Value.Breach:0.00} · 뼈대 잃음 {kv.Value.FrameLost} · 땜 {kv.Value.Patched} · 방 {string.Join("/", Cell.Dirs4.Select(d => w.Ship.RoomAt(kv.Key + d)?.Name).Where(n => n != null).Distinct())}");
                    foreach (var d in w.Drones.Drones) Console.WriteLine($"     드론 {d.Name} {d.KindName} {d.State} · {d.Doing} · 배터리 {d.Battery:0.00}");
                    foreach (var kv in w.Ship.Walls.Where(kv => Hull.EffectiveBreach(kv.Value) > 0.01f))
                        if (Hull.InsideRoom(w.Ship, kv.Key) is Room ir)
                            Console.WriteLine($"     안쪽 방 {ir.Name}: 포기 {ir.Abandoned} · 되살림 {ir.Restoring} · 금지 {ir.OffLimits} · 기압 {ir.Air.Pressure:0} · 실링폼 {w.Ship.CountStored(ItemKind.Sealant)} 금속판 {w.Ship.CountStored(ItemKind.Plate)} · 일감 {string.Join(" | ", w.Board.All.Where(o => !o.Closed && o.Target.CurrentRoom == ir).Select(o => $"{o.Kind}/{o.Urgency:0.00}/{o.Assignee?.Name ?? o.Robot?.Name ?? o.Drone?.Name ?? "-"}"))}");
                }
                compHole = Said(tAll);
                dodged = w.Log.Entries.Count(e => e.Tick >= tAll && e.Text.StartsWith("회피 기동 성공 — ") && e.Text.Contains("로 오던 운석이 비껴갔다"));
                sealedOk = hitAt < 0 || shutAt > 0;
                holeRes = hitAt < 0 ? $"{throws}번 던져도 방까지 안 뚫림" : shutAt > 0 ? $"{(shutAt - hitAt) / (float)SimTime.Minutes(1):0}분에 막음" : "14시간 넘게 열림";
                if (throws > 1 && hitAt >= 0) holeRes += $"({throws}번째)";
                Run(w, SimTime.Hours(1));
            }

            // ④ 주컴퓨터 고장 — 저장장치 오류 (부품: 전자 부품 · 사람 손으로 고친다)
            string downRes = "주컴퓨터 없음";
            bool backOk = true, downFireOk = true;
            int downJobs = 0, compDown = 0;
            string downFire = "-";
            if (a.Computer is Machine core)
            {
                long tB = w.Tick, backAt = -1;
                int rjB = w.Robots.JobsDone;
                w.Machines.Break(core, FaultKind.StorageFault);
                bool wentDown = false, fireLit = false;
                long fireAt = -1, fireOutAt = -1;
                var fireRoom = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Workshop or RoomType.Storage or RoomType.Lounge && r != galley).OrderBy(r => r.Id).FirstOrDefault()
                               ?? w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && r.Type != RoomType.Reactor && r != galley && r.Cells.Count(w.Ship.IsOpenFloor) >= 4).OrderBy(r => r.Id).FirstOrDefault();
                for (int t = 0; t < SimTime.Hours(24) && backAt < 0; t += SimTime.Minutes(1))
                {
                    Run(w, SimTime.Minutes(1));
                    wentDown |= !a.MainOnline;
                    // 꺼진 지 20분 뒤 다른 방에 불 (자동 화재 대응 없이 사람 · 로봇이 끄나)
                    if (!fireLit && wentDown && w.Tick - tB >= SimTime.Minutes(20) && fireRoom != null)
                    {
                        var fs = fireRoom.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - fireRoom.Center).LengthSquared()).FirstOrDefault();
                        fireLit = Player.Fire(w, fs);
                        if (fireLit) fireAt = w.Tick;
                    }
                    if (fireAt >= 0 && fireOutAt < 0 && w.Fire.Count == 0) fireOutAt = w.Tick;
                    if (wentDown && a.MainOnline && core.Faults.All(f => f.Kind != FaultKind.StorageFault)) backAt = w.Tick;
                    if (args.Contains("--compdebug") && t % SimTime.Hours(2) == 0)
                    {
                        var os = w.Board.Open.Where(o => o.Target.Furniture == core.Body).Select(o => $"{o.Kind}/{o.Title}/{o.Assignee?.Name ?? "-"}/막힘 {(o.BlockedUntil > w.Tick ? "예" : "아니오")}");
                        Console.WriteLine($"     +{(w.Tick - tB) / (float)SimTime.TicksPerHour:0}h 켜짐 {a.MainOnline} · 전기 {core.Powered} 효율 {core.Efficiency:0.00}(고장 {core.FaultFactor:0.00} 등급 {Grades.Output(core.Grade):0.00} 마모 {core.Wear:0.00} 열 {core.Heat:0.00} 분말 {core.Fouled:0.00} 전압 {core.Body.Room.PowerFlow:0.00} 막는것 {w.Flow.Share(NetKind.Power, core.Body.Room).Why} {w.Flow.Share(NetKind.Power, core.Body.Room).Limit?.Key} 간선일감 {w.Board.Open.Count(o => o.Kind == WorkKind.RepairNet)}/{w.Board.Open.Where(o => o.Kind == WorkKind.RepairNet).Select(o => o.Assignee?.Name ?? "-").FirstOrDefault()}) 멈춤 {core.Stopped} 내림 {core.Parked} 상태 {core.Condition:0.00} 온도 {core.Body.Room.Air.Temperature:0} 재부팅 {a.Rebooting} 회로 {core.Body.Room.Powered} · 예비 {a.Core.BackupCore} · 같은 기계 {a.Computer == core} · 고장 {string.Join(",", core.Faults.Select(f => $"{f.Kind}단계{f.Stage}"))} · 전자 부품 {w.Ship.CountStored(ItemKind.Electronics)} · 일감 {string.Join(" | ", os)}");
                    }
                }
                // 고쳐진 뒤에도 불이 남았으면 마저 지켜본다
                for (int t = 0; t < SimTime.Hours(4) && fireAt >= 0 && fireOutAt < 0; t += SimTime.Minutes(1)) { Run(w, SimTime.Minutes(1)); if (w.Fire.Count == 0) fireOutAt = w.Tick; }
                downJobs = w.Robots.JobsDone - rjB;
                compDown = Said(tB);
                backOk = !wentDown || backAt > 0;
                downFireOk = fireAt < 0 || fireOutAt > 0;
                downFire = fireAt < 0 ? "안 붙음" : fireOutAt > 0 ? $"{(fireOutAt - fireAt) / (float)SimTime.Minutes(1):0}분" : "안 꺼짐";
                downRes = !wentDown ? "안 꺼짐(예비가 붙잡음)" : backAt > 0 ? $"{(backAt - tB) / (float)SimTime.TicksPerHour:0.0}시간 만에 고침" : "24시간 넘게 꺼짐";
            }
            int dead = w.Crew.Count(c => c.Dead);

            Console.WriteLine($"{name,-6} 로봇 {robots,2} · 드론 {drones,2} · 주컴퓨터 {(online0 ? "켜짐" : "없음/꺼짐")}{(offlineMin > 0 ? $"(꺼진 {offlineMin}분)" : "")}");
            Console.WriteLine($"       평상시 {days}일: 로봇 일 {rjobs} · 멈춤 {stuck.Count} · 고장 {faulty} · 잃음 {rlost} | 드론 출격 {dsort}/일 {djobs} · 고장 {dfaulty} · 잃음 {dlost} | " +
                              $"공기 나쁨 {badAirMin}분 (가장 낮은 산소 {worstO2:0.0}kPa {worstAir} · 이산화탄소 최고 {worstCO2:0.00}) | 배터리 컴퓨터 {bCap:0}kWh / 실제 {realCap:0}kWh · 평상시 배터리 최저 {batLow * 100:0}% · 전기 못 받은 설비 있던 시간 {shortMin}분 · 부하 차단 계전기 {shedN}번");
            Console.WriteLine($"       불 {fireRes} (컴퓨터 {compFire} · 로봇 {fought}) | 운석 {holeRes} (피함 {dodged} · 드론 {dsort2} · 컴퓨터 {compHole}) | " +
                              $"주컴퓨터 고장 {downRes} · 그동안 불 {downFire} · 로봇 일 {downJobs} · 알림 {compDown} | 사망 {dead}");
            foreach (var s in stuck.Values) Console.WriteLine($"     멈춤: {s}");
            // 순찰: 설비가 있는 방인데 이틀 넘게 아무도 안 본 방 (순찰 로봇이 없는 배)
            var unseen = w.Ship.LiveRooms.Where(r => !r.Abandoned && !r.OffLimits && r.Type != RoomType.Corridor && r.Furniture.Any(f => f.Machine != null))
                .Select(r => (r, h: (w.Tick - w.RoomsInspected.GetValueOrDefault(r.Id, w.StartTickOf)) / (float)SimTime.TicksPerHour)).Where(x => x.h > 48f).ToList();
            if (unseen.Count > 0 && !w.Robots.Robots.Any(r => RobotsV15.Patrols(r.Kind) && r.Operational)) Console.WriteLine($"     순찰 못 받은 방: {string.Join(", ", unseen.Select(x => $"{x.r.Name} {x.h:0}시간"))}");
            void P(bool bad, string what) { if (bad) problems.Add($"{name}: {what}"); }
            P(!online0, "주컴퓨터 없음/꺼짐");
            P(offlineMin > 60, $"평상시에 주컴퓨터가 {offlineMin}분 꺼졌다");
            P(robots == 0, "로봇 없음");
            P(drones == 0, "드론 없음");
            P(robots > 0 && rjobs == 0, "로봇이 평상시에 한 일이 없다");
            P(stuck.Count > 0, $"멈춰 선 로봇 {stuck.Count}");
            P(rlost > 0 || dlost > 0, $"평상시에 잃은 로봇 {rlost} · 드론 {dlost}");
            P(badAirMin > 60, $"사람이 있는 방 공기가 {badAirMin}분 나빴다");
            P(shedN > days * 12, $"부하 차단 계전기가 {shedN}번 내렸다 올렸다 (되풀이)");
            P(batLow < 0.2f, $"평상시에 배터리가 {batLow * 100:0}%까지 떨어졌다");
            P(shortMin > 60, $"평상시에 전기를 못 받은 설비가 {shortMin}분 있었다");
            P(batErr > 0.35f, $"주컴퓨터가 아는 배터리 용량이 실제와 {batErr * 100:0}% 다르다");
            P(!fireOut, "불이 안 꺼진다");
            P(galley != null && compFire == 0, "불에 주컴퓨터가 아무 말이 없다");
            P(!sealedOk, "외벽 구멍이 안 막힌다");
            P(outers.Count > 0 && compHole == 0, "운석에 주컴퓨터가 아무 말이 없다");
            P(!backOk, "고장 난 주컴퓨터를 하루 안에 못 고친다");
            P(!downFireOk, "주컴퓨터가 꺼진 동안 난 불이 안 꺼진다");
            P(dead > 0, $"사망 {dead}");
        }
        Console.WriteLine(problems.Count == 0 ? "\n✔ 문제 없음" : $"\n✘ 문제 {problems.Count}\n  " + string.Join("\n  ", problems));
        return problems.Count == 0 ? 0 : 1;
    }
}
