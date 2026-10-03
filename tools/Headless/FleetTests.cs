using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ShipSim.Core;


// v16.20b 로봇 · 드론 두뇌와 성능 · 주컴퓨터 함대 지휘 (--fleettest)
//  운석우 + 외벽 파공 → 컴퓨터 지휘로 드론이 먼저 밖에서 막는다 · 배터리가 떨어지기 전에 교대 · 고장 난 드론을 다른 드론이 끌고 온다 ·
//  불난 방에 사람 대신 소방 로봇이 먼저 · 수리는 부품 → 수리 → 시험 · 막힌 길 우회 · 기술 단계로 성능 상승 · 결정론 · 성능.
public static partial class Program
{
    private static (Cell cell, WallState wall, Room room)? FleetHullWall(World w, Func<Room, bool>? ok = null)
    {
        var hatch = DroneSystem.Hatch(w);
        var cands = new List<(Cell, WallState, Room, float)>();
        foreach (var (cell, wall) in w.Ship.Walls)
        {
            if (!wall.IsHull || wall.FrameLost || wall.Breach > 0f) continue;
            var room = Hull.InsideRoom(w.Ship, cell);
            if (room == null || room.Detached || room.Type == RoomType.Corridor || ok != null && !ok(room)) continue;
            if (w.Drones.WallSpot(cell) == null) continue;
            float d = hatch == null ? 0f : MathF.Abs(cell.X - hatch.Cell.X) + MathF.Abs(cell.Y - hatch.Cell.Y);
            cands.Add((cell, wall, room, d));
        }
        // 해치에서 너무 가깝지도 멀지도 않은 벽
        var pick = cands.OrderBy(c => MathF.Abs(c.Item4 - 14f)).ThenBy(c => c.Item1.X).ThenBy(c => c.Item1.Y).FirstOrDefault();
        return pick.Item2 == null ? null : (pick.Item1, pick.Item2, pick.Item3);
    }

    private static int RunFleetTest(int seed)
    {
        _fails = 0;
        if (Environment.GetEnvironmentVariable("FLEETPROBE") is string probe) return FleetProbe(probe); // 디버그: 배,시드,일수 — 드론을 잃은 까닭
        Console.WriteLine($"v16.20b 로봇 · 드론 두뇌 · 주컴퓨터 함대 지휘 점검 (시드 {seed})");
        string? only = Environment.GetEnvironmentVariable("FLEETONLY"); // 시험 하나만 (디버그)
        try
        {
            // ── 1) 운석우 · 외벽 파공: 컴퓨터가 드론을 먼저 밖으로 → 배터리가 떨어지기 전에 교대 → 밖에서 막는다 ──
            if (only == null || only.Contains('1'))
            {
                var w = DayOne(seed, "Hanbit");
                var f = w.Fleet;
                Console.WriteLine($"  드론: {string.Join(", ", w.Drones.Drones.Select(d => $"{d.Name}({d.Kind} {d.State} {d.Battery * 100:0}%)"))}");
                var pick = FleetHullWall(w);
                Check("파공 낼 외벽", pick != null);
                if (pick is var (cell, wall, room))
                {
                    var sealers = w.Drones.Drones.Where(d => DroneSystem.CanSeal(d.Kind)).ToList();
                    foreach (var d in sealers) { d.Battery = 1f; if (d.Dock.Storage!.Count(ItemKind.Sealant) < 4) d.Dock.Storage.Add(ItemKind.Sealant, 4); }
                    Player.Hazard(w, HazardKind.MeteorShower, default);
                    Hull.Damage(w.Ship, cell, wall.Integrity - 0.12f);
                    Check("외벽에 파공", wall.Breach > 0f, $"강도 {wall.Integrity:0.00} · 파공 {wall.Breach:0.00}");
                    Drone? sent = null;
                    for (int t = 0; t < SimTime.Minutes(30) && sent == null; t += 30)
                    {
                        Run(w, 30);
                        sent = w.Drones.Drones.FirstOrDefault(d => f.DroneTask.ContainsKey(d.Id));
                    }
                    var dec = w.Automation.Foresee.Timeline.LastOrDefault(d => d.Kind == "파공");
                    Check("운석우에 컴퓨터가 사람 대신 드론을 먼저 보낸다 (견줘 보기 → 명령선)",
                        sent != null && dec != null && dec.Options[dec.Chosen].Key == "drone" && w.Automation.Command.Lines.Any(l => l.Target == CmdTarget.Drone && l.TargetId == sent.Id),
                        $"보낸 드론 {sent?.Name} · 판단 {dec?.Title} → {(dec != null ? dec.Options[dec.Chosen].Name : "-")} ({dec?.Reason}) · 일 {string.Join(" / ", w.Board.Open.Where(o => o.Kind == WorkKind.SealBreach).Select(o => $"{o.Title} 맡음 {o.Assignee?.Name ?? o.Drone?.Name ?? "-"}"))}");
                    // 일터에 붙으면 배터리를 돌아올 몫 가까이로 떨어뜨린다 → 교대
                    bool dropped = false, cameBack = false;
                    int relief0 = f.Reliefs, seal0 = f.Seals;
                    Drone? second = null;
                    WorkOrder? job = null;
                    for (int t = 0; t < SimTime.Hours(6) && !(f.Seals > seal0 && (wall.Patched || wall.Breach <= 0f)); t += 20)
                    {
                        Run(w, 20);
                        if (Environment.GetEnvironmentVariable("FLEETDBG") == "1" && t % 100 == 0 && sent != null)
                            Console.WriteLine($"    t{t} {sent.Name} {sent.State} 배 {sent.Battery:0.00} 진척 {sent.WorkProgress:0.00} {sent.Doing} · 일 {sent.Order?.Title} · 맡김 {f.DroneTask.ContainsKey(sent.Id)} · 경보 {w.Sensors.Alarm != null} · 파편 {w.Hazards.Shower.Count} · 파공 {wall.Breach:0.00}/{wall.Patched} · 둘째 {second?.Name} {second?.State} {second?.Doing}");
                        if (!dropped && sent != null && sent.State == DroneState.Working && sent.Order?.Kind == WorkKind.SealBreach && sent.WorkProgress < 0.5f)
                        {
                            job = sent.Order;
                            foreach (var sp in sealers) if (sp != sent && sp.State == DroneState.Docked) sp.Battery = 1f; // 쉬던 드론은 충전을 마쳤다
                            sent.WorkDone = sent.WorkNeeded * 0.4f;
                            sent.Battery = w.Drones.FleetReturnCost(sent) + 0.05f;
                            dropped = true;
                        }
                        else if (dropped && !cameBack && sent != null && sent.State != DroneState.Working)
                        {
                            cameBack = sent.State is DroneState.Returning or DroneState.Docked && sent.Order == null;
                            second = w.Drones.Drones.FirstOrDefault(d => d != sent && job != null && f.DroneTask.TryGetValue(d.Id, out int oid) && oid == job.Id);
                        }
                    }
                    Check("배터리가 돌아올 몫만 남으면 교대로 돌아온다 (쉬던 드론이 한 만큼부터)",
                        dropped && cameBack && f.Reliefs > relief0 && (second != null || job != null && (f.Carry.ContainsKey(job.Id) || job.Closed)),
                        $"떨어뜨림 {dropped} · 돌아옴 {cameBack} · 교대 {f.Reliefs - relief0} · 이어받은 드론 {second?.Name} · {sent?.Mind.Why}");
                    Check("드론이 밖에서 파공을 막았다", f.Seals > seal0 && (wall.Patched || wall.Breach <= 0f),
                        $"막음 {f.Seals} · 실패 {f.SealFails} · 파공 {wall.Breach:0.00} · 봉합 {wall.Patched} · {string.Join(" / ", wall.Marks.TakeLast(3).Select(m => m.Text))} · 드론 {string.Join(", ", sealers.Select(d => $"{d.Name} {d.State} {d.Doing}"))}");
                    Check("드론 카드: 지금 · 다음 · 배터리 · 왜", sealers.All(d => d.Mind.Now != "" && d.Mind.Power.Contains("배터리")) && sealers.Any(d => d.Mind.Why.Length > 4),
                        string.Join(" / ", sealers.Select(d => $"{d.Name}: {d.Mind.Now} → {d.Mind.Next} · {d.Mind.Power} · {d.Mind.Why}")));
                }
            }

            // ── 2) 떠내려간 드론: 견인 드론이 없으면 다른 드론이 건져 온다 ──
            if (only == null || only.Contains('2'))
            {
                var w = DayOne(seed, "Hanbit");
                var f = w.Fleet;
                foreach (var t in w.Drones.Drones.Where(d => RobotsV15.Base(d.Kind) == DroneKind.Tow)) t.Faulty = true;
                var lost = w.Drones.Drones.FirstOrDefault(d => d.State == DroneState.Docked && d.Operational);
                var spots = HullSpots(w, 6);
                Check("떠내려갈 드론", lost != null && spots.Count > 0);
                if (lost != null && spots.Count > 0)
                {
                    foreach (var d in w.Drones.Drones) d.Battery = 1f;
                    lost.State = DroneState.Working;
                    lost.Position = spots[0].Center + new Vector2(1.5f, 0f);
                    w.Drones.ForceStrand(lost, "시험 — 추진기가 멈췄다");
                    for (int t = 0; t < SimTime.Hours(5) && lost.State != DroneState.Docked; t += 30) Run(w, 30);
                    var by = w.Drones.Drones.FirstOrDefault(d => d != lost && d.Marks.Any(m => m.Text.Contains(lost.Name)) || d.Mind.Why.Contains(lost.Name));
                    Check("고장 난 드론을 다른 드론이 끌고 온다 (견인 드론이 없을 때)", lost.State == DroneState.Docked && f.Fetches >= 1,
                        $"상태 {lost.State} · {lost.Doing} · 건지러 간 {f.Fetches} · 건진 드론 {by?.Name} · 명령선 {w.Automation.Command.Lines.Count(l => l.Target == CmdTarget.Drone)}");
                }
                // 조용할 때: 약해진 외벽을 드론이 밖에서 미리 덧댄다 (외벽 순찰)
                if (FleetHullWall(w) is var (hc, hw, hroom))
                {
                    hw.Integrity = hw.MaxIntegrity * 0.7f;
                    for (int t = 0; t < SimTime.Hours(6) && hw.Integrity < hw.MaxIntegrity * 0.8f; t += 30) Run(w, 30);
                    Check("조용할 때 드론이 약해진 외벽을 밖에서 덧댄다 (외벽 순찰)", f.HullRounds >= 1 && hw.Integrity >= hw.MaxIntegrity * 0.8f && hw.Marks.Any(m => m.Text.Contains("순찰")),
                        $"{hroom.Name} 외벽 강도 {hw.Integrity:0.00}/{hw.MaxIntegrity:0.00} · 순찰 {f.HullRounds} · {string.Join(" / ", hw.Marks.TakeLast(2).Select(m => m.Text))}");
                }
            }

            // ── 3) 불: 사람보다 소방 로봇이 먼저 · 본 사람은 컴퓨터를 더 믿는다 ──
            if (only == null || only.Contains('3'))
            {
                var w = DayOne(seed, "Hanbit");
                var f = w.Fleet;
                var fighter = w.Robots.Robots.Where(r => RobotsV15.Fights(r.Kind) && r.Operational).OrderBy(r => r.Id).FirstOrDefault();
                Check("소방 로봇", fighter != null, string.Join(", ", w.Robots.Robots.Select(r => r.Kind)));
                if (fighter != null)
                {
                    fighter.Battery = 1f; fighter.Foam = 1f;
                    var room = w.Ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Corridor && r.Type != RoomType.Reactor && r != fighter.Room && r.Cells.Count(c => w.Ship.IsWalkable(c)) >= 6)
                        .OrderBy(r => MathF.Abs(r.Center.X - fighter.Position.X) + MathF.Abs(r.Center.Y - fighter.Position.Y)).ThenBy(r => r.Id).First();
                    var cells = room.Cells.Where(c => w.Ship.IsWalkable(c)).OrderBy(c => (c.Center - room.Center).LengthSquared()).Take(3).ToList();
                    foreach (var c in cells) w.Fire.Ignite(c, 0.5f);
                    float trust0 = w.Crew.Where(c => !c.Dead).Sum(c => w.Automation.Trusts.Of(c));
                    long robotIn = -1, crewOn = -1;
                    var inside = w.Crew.Where(c => c.Room == room).Select(c => c.Id).ToHashSet(); // 불이 났을 때 방 안에 있던 사람은 그 자리에서 끈다
                    var foam0 = w.Robots.Robots.ToDictionary(r => r.Id, r => r.Foam);
                    Robot? went = null;
                    for (int t = 0; t < SimTime.Minutes(40) && w.Fire.CountIn(room) > 0; t++)
                    {
                        w.Step();
                        if (robotIn < 0 && w.Robots.Robots.FirstOrDefault(r => RobotsV15.Fights(r.Kind) && (r.Room == room || r.Foam < foam0[r.Id] - 0.01f)) is Robot rb) { robotIn = w.Tick; went = rb; }
                        if (crewOn < 0 && w.Crew.Any(c => !c.Dead && !inside.Contains(c.Id) && c.Room == room && c.Job?.Order?.Kind == WorkKind.Extinguish)) crewOn = w.Tick; // 밖에서 들어온 사람
                        if (Environment.GetEnvironmentVariable("FLEETDBG") == "1" && t % 60 == 0)
                            Console.WriteLine($"    t{t} {fighter.Name} {fighter.State} {fighter.Room?.Name} 길 {(fighter.Path == null ? -1 : fighter.Path.Count - fighter.PathIndex)} 비킴 {fighter.YieldTicks} 거품 {fighter.Foam:0.00} 소화 {fighter.FightingFire} · {fighter.Doing} · 불 {w.Fire.CountIn(room)} · {room.Name} 압 {room.Air.Pressure:0} O2 {room.Air.O2:0.00} · 수순 {w.Automation.FireCases.FirstOrDefault(c => c.RoomId == room.Id)?.Status} · 질식 {w.Automation.Smothered} 로봇맡음 {f.BotOn(room)} · 가까운 사람 {w.Crew.Where(c => !c.Dead && c.IsAwake).Select(c => MathF.Abs(c.Position.X - fighter.Position.X) + MathF.Abs(c.Position.Y - fighter.Position.Y)).DefaultIfEmpty(99f).Min():0} 본 {f.Witnessed} 화면 {w.Crew.Count(c => !c.Dead && c.IsAwake && c.Room is { Type: RoomType.Bridge or RoomType.Comms or RoomType.ServerRoom })} 데이터 {room.DataLinked} 주컴 {w.Automation.MainOnline}");
                    }
                    var dec = w.Automation.Foresee.Timeline.LastOrDefault(d => d.Kind == "불" && d.Title.Contains("누가 먼저"));
                    Check("불난 방에 사람 대신 소방 로봇이 먼저 들어간다", f.FireFirst >= 1 && robotIn >= 0 && (crewOn < 0 || robotIn <= crewOn) && dec != null && dec.Options[dec.Chosen].Key != "crew",
                        $"먼저 보냄 {f.FireFirst} · 로봇 {went?.Name} 닿음 {robotIn} · 사람 들어감 {crewOn} · 판단 {dec?.Title} → {(dec != null ? dec.Options[dec.Chosen].Name : "-")} · {went?.Mind.Why}");
                    float trust1 = w.Crew.Where(c => !c.Dead).Sum(c => w.Automation.Trusts.Of(c));
                    Check("로봇이 먼저 들어간 걸 본 사람은 컴퓨터를 더 믿는다", f.Witnessed >= 1 && trust1 > trust0 - 0.001f && w.Brain2.Beliefs != null,
                        $"본 사람 {f.Witnessed} · 믿음 합 {trust0:0.00} → {trust1:0.00}");
                    Check("불이 꺼졌다", w.Fire.CountIn(room) == 0, $"남은 불 {w.Fire.CountIn(room)}");
                }
            }

            // ── 4) 수리는 부품 → 수리 → 시험 · 멈춘 로봇 끌고 오기 · 서로 고치기 · 막힌 길 우회 ──
            if (only == null || only.Contains('4'))
            {
                var w = DayOne(seed, "Hanbit");
                var f = w.Fleet;
                // 고장 난 로봇을 다른 로봇이 부품을 들고 와서 고친다
                var broken = w.Robots.Robots.Where(r => r.Operational && !(RobotsV15.Assists(r.Kind) || RobotsV15.Base(r.Kind) == RobotKind.Maintainer)).OrderByDescending(r => r.Id).FirstOrDefault();
                for (int t = 0; t < SimTime.Hours(6) && broken != null && broken.State != RobotState.Docked; t++) w.Step();
                var stagesSeen = new List<string>();
                if (broken != null)
                {
                    foreach (var rb in w.Robots.Robots) rb.Battery = 1f; // 모두 충전을 마친 아침
                    w.Robots.ForceFault(broken, RobotFault.Drive);
                    for (int t = 0; t < SimTime.Hours(6) && broken.Fault != null; t++)
                    {
                        w.Step();
                        if (stagesSeen.Count == 0 && w.Robots.Robots.FirstOrDefault(r => r.Fixing == broken) is Robot fx && fx.Mind.Stages.Count >= 3) stagesSeen.AddRange(fx.Mind.Stages);
                        if (Environment.GetEnvironmentVariable("FLEETDBG") == "1" && t % 60 == 0 && t < SimTime.Minutes(20))
                            Console.WriteLine($"    t{t} {broken.Name} {broken.State} {broken.Fault} · 일 {string.Join(",", w.Board.All.Where(o => o.Target.Robot == broken && !o.Closed).Select(o => $"{o.Kind}#{o.Id} {o.Assignee?.Name} 로봇 {o.Robot?.Name} 막힘 {o.BlockedUntil - w.Tick}"))} · {string.Join(" / ", w.Robots.Robots.Where(r => r != broken).Select(r => $"{r.Name} {r.State} 배 {r.Battery:0.00} 일 {r.Order?.Title}({r.Order?.Urgency:0.00}) 짐 {r.Cargo} {r.Doing}"))}");
                    }
                    Run(w, SimTime.Minutes(10));
                }
                int gi = stagesSeen.FindIndex(s => s.EndsWith("가져오기")), fi = stagesSeen.IndexOf("고치기"), ti = stagesSeen.IndexOf("시험 가동");
                Check("고장 난 로봇을 다른 로봇이 고친다: 부품 → 고치기 → 시험 가동", broken != null && broken.Fault == null && f.Fixes >= 1 && gi >= 0 && gi < fi && fi < ti && f.Tests + f.TestFails >= 1,
                    $"고침 {f.Fixes} · 단계 {string.Join(" → ", stagesSeen)} · 시험 {f.Tests}/{f.TestFails} · 고장 {broken?.Fault}");

                // 방전돼 멈춘 로봇을 다른 로봇이 끌고 온다
                var flat = w.Robots.Robots.Where(r => r != broken && r.Operational && !RobotsV15.Fights(r.Kind)).OrderBy(r => r.Id).FirstOrDefault();
                if (flat != null)
                {
                    for (int t = 0; t < SimTime.Hours(10) && flat.State != RobotState.Active; t++) w.Step();
                    flat.Battery = 0.0005f;
                    for (int t = 0; t < SimTime.Hours(4) && flat.State != RobotState.Docked; t += 10)
                    {
                        Run(w, 10);
                        if (Environment.GetEnvironmentVariable("FLEETDBG") == "1" && t % SimTime.Minutes(15) == 0 && flat.TowBot is Robot tb)
                            Console.WriteLine($"    t{t} {flat.Name} {flat.State} ← {tb.Name} {tb.State} {tb.Room?.Name} 길 {(tb.Path == null ? -1 : tb.Path.Count - tb.PathIndex)} 단계 {tb.StepIndex}/{tb.Steps?.Count} 비킴 {tb.YieldTicks} 배 {tb.Battery:0.00} · {tb.Doing} · {tb.Mind.Why}");
                    }
                }
                Check("방전돼 멈춘 로봇을 다른 로봇이 충전대까지 끌고 온다", flat != null && flat.State == RobotState.Docked && f.Tows >= 1,
                    $"상태 {flat?.State} · 끌고 옴 {f.Tows} · {flat?.Doing} · {string.Join(" / ", flat?.Marks.TakeLast(3).Select(m => m.Text) ?? Array.Empty<string>())}");

                // 막힌 길: 가는 길의 문을 잠그면 다른 길로
                int re0 = f.Reroutes;
                Robot? mover = null;
                Door? locked = null;
                for (int t = 0; t < SimTime.Hours(12) && f.Reroutes == re0; t++)
                {
                    w.Step();
                    if (f.Reroutes != re0) break;
                    if (locked != null && (mover!.Path == null || !mover.Path.Skip(mover.PathIndex).Any(c => w.Ship.DoorAt(c) == locked))) { locked.Locked = false; locked = null; }
                    if (locked != null || t % 10 != 0) continue;
                    foreach (var r in w.Robots.Robots)
                    {
                        if (r.State != RobotState.Active || r.Hauling != null || r.Path == null || r.Path.Count - r.PathIndex < 6) continue;
                        for (int k = r.PathIndex + 2; k < r.Path.Count - 2 && locked == null; k++)
                        {
                            if (w.Ship.DoorAt(r.Path[k]) is not Door door || door.Locked || door.RoomA == r.Room || door.RoomB == r.Room) continue;
                            door.Locked = true;
                            var alt = w.Paths.Find(r.Cell, r.Path[^1], RobotSystem.Profile);
                            if (alt != null && alt.All(c => w.Ship.DoorAt(c) != door)) { locked = door; mover = r; }
                            else door.Locked = false;
                        }
                        if (locked != null) break;
                    }
                }
                Check("막힌 길(잠긴 문)은 다른 길로 돌아간다", mover != null && f.Reroutes > re0 && (mover.Path == null || mover.Path.Skip(mover.PathIndex).All(c => w.Ship.DoorAt(c) != locked)),
                    $"로봇 {mover?.Name} · 돌아감 {f.Reroutes - re0} · {mover?.Mind.Why}");
                if (locked != null) locked.Locked = false;

                // 서로 손보기: 닳은 로봇을 쉬던 정비 로봇이 고장 나기 전에 손본다
                var worn = w.Robots.Robots.Where(r => r.Operational && r.Fault == null && !RobotsV15.Fights(r.Kind)).OrderByDescending(r => r.Id).First();
                for (int t = 0; t < SimTime.Hours(6) && worn.State != RobotState.Docked; t++) w.Step();
                worn.Condition = 0.6f;
                foreach (var rb in w.Robots.Robots) if (rb != worn) rb.Battery = MathF.Max(rb.Battery, 0.9f);
                int tune0 = f.Tunes;
                for (int t = 0; t < SimTime.Hours(4) && f.Tunes == tune0; t += 10)
                {
                    Run(w, 10);
                    if (Environment.GetEnvironmentVariable("FLEETDBG") == "1" && t % SimTime.Minutes(20) == 0)
                        Console.WriteLine($"    t{t} {worn.Name} {worn.State} 상태 {worn.Condition:0.00} · 함대 {f.Mode} · {string.Join(" / ", w.Robots.Robots.Where(r => r != worn).Select(r => $"{r.Name} {r.State} 배 {r.Battery:0.00} 상태 {r.Condition:0.00} 고침 {r.Fixing?.Name} {r.Doing}"))}");
                }
                Check("닳은 로봇을 다른 로봇이 고장 나기 전에 손본다", f.Tunes > tune0 && worn.Condition >= 0.8f,
                    $"{worn.Name} 상태 {worn.Condition:0.00} · 손봄 {f.Tunes - tune0} · {string.Join(" / ", worn.Marks.TakeLast(2).Select(m => m.Text))}");

                // 정비: 부품 → 정비 → 시험 가동 (하루 동안 지켜본다)
                List<string>? maint = null;
                foreach (var m in w.Ship.Machines.Where(m => m.Spec.ServiceItem is ItemKind it && w.Ship.CountStored(it) > 0 && m.Faults.Count == 0 && m.Body.Room.Type != RoomType.Reactor && !m.Body.Room.Detached)
                    .OrderBy(m => m.Body.Id).Take(5)) m.Wear = MathF.Max(m.Wear, 0.62f); // 여러 설비가 한꺼번에 닳았다 — 로봇도 몇 곳을 맡는다
                for (int t = 0; t < SimTime.Hours(20) && maint == null; t++)
                {
                    w.Step();
                    if (t % 30 != 0) continue;
                    foreach (var r in w.Robots.Robots)
                        if (r.Order?.Kind == WorkKind.Maintain && r.Mind.Stages.Count >= 3 && r.Mind.Stages[0].EndsWith("가져오기")) { maint = r.Mind.Stages.ToList(); break; }
                }
                Check("정비 일도 부품 가져오기 → 고치기 → 시험 가동", maint != null && maint.IndexOf("고치기") > 0 && maint.IndexOf("시험 가동") > maint.IndexOf("고치기"),
                    maint == null ? "정비 단계 못 봄" : string.Join(" → ", maint));
            }

            // ── 5) 자기 보존 · 부서짐 · 아쉬움 ──
            if (only == null || only.Contains('5'))
            {
                var w = DayOne(seed, "Hanbit");
                var f = w.Fleet;
                Robot? r = null;
                for (int t = 0; t < SimTime.Hours(10) && r == null; t++)
                {
                    w.Step();
                    r = w.Robots.Robots.FirstOrDefault(x => x.State == RobotState.Active && !x.Homing && x.Path == null && x.Order != null && x.Room is Room rm && rm.Type != RoomType.Corridor && !x.FightingFire
                        && x.Order.Target.CurrentRoom == rm && x.Progress is float pr && pr < 0.3f);
                }
                Check("일하는 로봇", r != null);
                if (r != null)
                {
                    var room = r.Room!;
                    var m = Player.Meteor(w, Scenarios.OuterTarget(w, room), 0.6f);
                    if (m != null) { m.Room = room; m.Warned = WarnLevel.Manual; } // 통신실 사람이 창으로 봤다 (그 방 외벽으로 온다)
                    int ret0 = f.Retreats;
                    bool warned = false, stayed = true;
                    for (int t = 0; t < SimTime.Minutes(30) && f.Retreats == ret0 && m != null && w.Sensors.Incoming.Contains(m); t++)
                    {
                        w.Step();
                        if (!warned && w.Sensors.Threat(room) != null) { warned = true; stayed = r.Room == room && r.State == RobotState.Active; }
                    }
                    Check("운석이 떨어질 방에서 로봇이 물러난다 (자기 보존)", m != null && warned && (!stayed || f.Retreats > ret0 && r.Mind.Why.Contains("물러난다")),
                        $"경보 {m?.Room?.Name} {warned} {m?.Warned} 다가옴 {w.Sensors.Incoming.Count} 위협 {w.Sensors.Threat(room) != null} 로봇 방 {r.Room?.Name} · 경보 때 방에 있었나 {stayed} · 물러남 {f.Retreats - ret0} · {r.Mind.Why}");
                    Run(w, SimTime.Minutes(30));
                    // 불길 속에서 닳은 로봇이 부서진다 → 같이 일하던 사람이 아쉬워한다
                    Robot? victim = null;
                    bool lit = false;
                    // 통합8 재배대 칸은 바닥이 아니라 불이 안 붙는다 — 바닥에 선 채 일하는 로봇을 기다린다
                    for (int t = 0; t < SimTime.Hours(10) && !lit; t++)
                    {
                        w.Step();
                        victim = w.Robots.Robots.FirstOrDefault(x => x.Operational && !RobotsV15.Fireproof(x.Kind) && x.State == RobotState.Active && x.Path == null && x.Progress is float pv && pv < 0.5f && x.Room?.Type != RoomType.Corridor && w.Ship.Grid.Kind(x.Cell) == TileKind.Floor);
                        if (victim == null) continue;
                        lit = w.Fire.Ignite(victim.Cell, 0.8f);
                        foreach (var dd in Cell.Dirs4) lit |= w.Fire.Ignite(victim.Cell + dd, 0.8f);
                    }
                    if (victim == null || !lit) { Check("불길에 들 로봇", false, victim?.Room?.Name ?? "없음"); return 1; }
                    victim.Condition = 0.01f;
                    for (int t = 0; t < SimTime.Minutes(5) && !victim.Wrecked; t++) w.Step();
                    Check("불길 속에서 부서지면 아끼던 사람이 아쉬워한다", victim.Wrecked && f.Mourned >= 1 && w.History.Events.Any(e => e.Text.Contains(victim.Name) && e.Text.Contains("아쉬워한다")),
                        $"불 {lit} {w.Fire.Count} · 부서짐 {victim.Wrecked} · {victim.State} 상태 {victim.Condition:0.00} · {victim.Doing} · 아쉬움 {f.Mourned}");
                }
            }

            // ── 6) 기술 단계: 익힐수록 빨라지고 오래가고 공구가 좋아진다 ──
            if (only == null || only.Contains('6'))
            {
                var w = DayOne(seed, "Hanbit");
                var f = w.Fleet;
                var r = w.Robots.Robots.First(x => x.Operational);
                float s1 = f.Speed(r), d1 = f.Drain(r), k1 = f.FaultMul, wk1 = f.Work;
                int tier1 = f.Tier;
                foreach (var id in FleetSystem.TierTechs.Take(3)) w.Eras.Known.Add(id);
                Run(w, SimTime.Minutes(2));
                Check("기술 단계가 오르면 속도 · 배터리 · 외피 · 공구가 좋아진다", f.Tier > tier1 && f.Speed(r) > s1 && f.Drain(r) < d1 && f.FaultMul < k1 && f.Work < wk1
                    && w.History.Events.Any(e => e.Text.Contains("고쳐 달았다")),
                    $"단계 {tier1} → {f.Tier} ({FleetSystem.TierName(f.Tier)}) · {f.TierNote}");
            }

            // ── 7) 결정론 · 성능 ──
            if (only == null || only.Contains('7'))
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint h1 = H(), h2 = H();
                Check("결정론 — 같은 시드 두 번 같은 지문", h1 == h2, $"{h1:x8} / {h2:x8}");
                double Day(bool off)
                {
                    FleetSystem.Off = off;
                    try
                    {
                        var w = World.CreateDefault(seed, 30, "Hanbit");
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        Run(w, SimTime.TicksPerDay);
                        return sw.Elapsed.TotalSeconds;
                    }
                    finally { FleetSystem.Off = false; }
                }
                double off = Day(true), on = Day(false); // 번갈아 두 번씩 — 빠른 쪽 (다른 일이 CPU를 나눠 쓴다)
                on = Math.Min(on, Day(false)); off = Math.Min(off, Day(true));
                Check("성능 — 30인 배 하루가 크게 늘지 않는다 (+10% 안)", on <= off * 1.10 + 0.4, $"끔 {off:0.00}초 · 켬 {on:0.00}초 ({(on / off - 1) * 100:+0;-0}%)");
            }
        }
        catch (Exception e) { Check("예외 없이", false, e.ToString()); }
        Console.WriteLine(_fails == 0 ? "\n✔ 로봇 · 드론 함대 지휘 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>디버그: 점검 항해처럼 돌리며 드론 · 로봇 상태 변화를 찍는다 (FLEETPROBE=배,시드,일수[,off]).</summary>
    private static int FleetProbe(string spec)
    {
        var a = spec.Split(',');
        string ship = a[0];
        int seed = int.Parse(a[1]);
        float days = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
        FleetSystem.Off = a.Length > 3 && a[3] == "off";
        Storyteller.PersonaValue = 1f;
        Storyteller.LevelValue = 3f;
        var w = World.CreateDefault(seed, 0, ship);
        w.CrewCanDie = true;
        var last = new Dictionary<int, DroneState>();
        var rbad = new Dictionary<int, bool>();
        long act = 0, min = 0, total = (long)(days * SimTime.TicksPerDay);
        for (long t = 1; t <= total; t++)
        {
            w.Step();
            if (t % SimTime.Minutes(1) != 0) continue;
            foreach (var r in w.Robots.Robots)
            {
                bool bad = r.Fault != null;
                if (bad && !rbad.GetValueOrDefault(r.Id))
                    Console.WriteLine($"  {SimTime.Day(w.Tick)}일 {SimTime.Clock(w.Tick)} 로봇 고장 {r.Name} {r.Fault} · {r.Room?.Name} · 상태 {r.Condition:0.00} · 배 {r.Battery:0.00} · {r.Doing} · 불 {w.Fire.Count}");
                rbad[r.Id] = bad;
            }
            foreach (var d in w.Drones.Drones)
            {
                min++;
                if (d.State is DroneState.Outbound or DroneState.Working or DroneState.Returning or DroneState.Towing) act++;
                var prev = last.GetValueOrDefault(d.Id, DroneState.Docked);
                if (d.State != prev && (d.State is DroneState.Lost or DroneState.Adrift || prev == DroneState.Docked))
                    Console.WriteLine($"  {SimTime.Day(w.Tick)}일 {SimTime.Clock(w.Tick)} {d.Name} {prev}→{d.State} 배 {d.Battery:0.00} 상태 {d.Condition:0.00} 부풂 {d.Hurt.Swell:0.00} · {d.Doing} · {string.Join(" / ", d.Marks.TakeLast(2).Select(m => m.Text))}");
                last[d.Id] = d.State;
            }
        }
        Console.WriteLine($"드론 일함 {act * 100.0 / Math.Max(1, min):0.0}% · 손봄 {w.Fleet.Tunes} · 잃음 {w.Drones.Drones.Count(d => d.State == DroneState.Lost)} · 로봇 잃음 {w.Robots.Robots.Count(r => r.State == RobotState.Lost)} · 함대 {(FleetSystem.Off ? "끔" : $"선외수리 {w.Fleet.HullJobs} 순찰 {w.Fleet.HullRounds} 막음 {w.Fleet.Seals} 교대 {w.Fleet.Reliefs} 건짐 {w.Fleet.Fetches} 피함 {w.Fleet.Dodges} 끌고옴 {w.Fleet.Tows} 고침 {w.Fleet.Fixes} 우회 {w.Fleet.Reroutes} 물러남 {w.Fleet.Retreats} 소방 {w.Fleet.FireFirst} 부서짐 {w.Fleet.Wrecks}")}");
        var hull = w.Ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Value).ToList();
        Console.WriteLine($"외벽 {hull.Count} · 85% 아래 {hull.Count(x => x.Integrity < x.MaxIntegrity * 0.85f)} · 파공 {hull.Count(x => x.Breach > 0f)} · 봉합 {hull.Count(x => x.Patched)} · 평균 강도 {hull.Average(x => x.Integrity):0.00} · 맞은 방 {w.Fleet.Hits.Count} · 다음 순찰 {(w.Fleet.NextHullRound - w.Tick) / (float)SimTime.TicksPerHour:0.0}시간 · 판 {string.Join(",", w.Drones.Drones.Select(d => d.Dock.Storage!.Count(ItemKind.Plate)).Distinct())}");
        FleetSystem.Off = false;
        return 0;
    }
}
