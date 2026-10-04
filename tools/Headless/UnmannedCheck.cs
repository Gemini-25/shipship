using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// 사람이 모두 배를 비워도 주컴퓨터 · 로봇 · 드론만으로 배가 굴러가나 (--unmanned [--only=Key,Key] [--days=N]).
///   첫날을 보낸 뒤 승무원 모두 배를 떠난다 → N일(기본 4) 동안 사람 없이:
///   6시간째 불(주방 아닌 방) · 24시간째 외벽 운석 · 48시간째 생명 유지 설비(산소 발생기 · 없으면 이산화탄소 제거기) 고장.
///   보는 것: 주컴퓨터 켜짐 · 전기(배터리 · 꺼진 방) · 고장 난 설비(생명 유지 설비는 따로) · 불 · 구멍 · 로봇/드론이 한 일 · 잃음.
/// 배마다 두 줄 + 문제 목록. 문제가 있으면 1.
/// </summary>
public static partial class Program
{
    private static int RunUnmanned(int seed, string[] args)
    {
        var only = args.FirstOrDefault(a => a.StartsWith("--only="))?[7..].Split(',');
        int days = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--days="))?[7..], out var dd) ? Math.Max(1, dd) : 4;
        var keys = ShipCatalog.All.Select(t => t.Key).Where(k => only == null || only.Contains(k)).ToList();
        Console.WriteLine($"사람 없는 배 — 주컴퓨터 · 로봇 · 드론만으로 {days}일 · 시드 {seed} · {keys.Count}척\n");
        var problems = new List<string>();
        foreach (var key in keys)
        {
            var w = DayOne(seed, key);
            var a = w.Automation;
            string name = w.Ship.Name;
            // 모두 배를 떠난다 (하던 일은 내려놓는다 — 맡은 일감이 사람에게 묶여 남지 않게)
            int crew = Unmanned.Leave(w);
            long t0 = w.Tick;
            int rj0 = w.Robots.JobsDone, rb0 = w.Robots.Breakdowns, ds0 = w.Drones.Sorties, dj0 = w.Drones.JobsDone;
            int machines = w.Ship.Machines.Count();
            var crit = w.Ship.Machines.Where(m => CoopSystem.Critical(m.Body.Type)).ToList();
            int faulty0 = w.Ship.Machines.Count(m => m.Faults.Count > 0);

            int offMin = 0, darkMax = 0, darkLongMin = 0, maxFaulty = faulty0, critDownMax = 0;
            float batMin = 1f;
            var critDown = new Dictionary<Machine, long>();
            int OpenHoles() => w.Ship.Walls.Count(kv => Hull.EffectiveBreach(kv.Value) > 0.01f && Cell.Dirs4.Any(d => w.Ship.RoomAt(kv.Key + d) is Room rr && !rr.Detached));

            // 넣을 사고
            var fireRoom = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Workshop or RoomType.Storage or RoomType.Lounge).OrderBy(r => r.Id).FirstOrDefault()
                           ?? w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && r.Type != RoomType.Reactor && r.Cells.Count(w.Ship.IsOpenFloor) >= 4).OrderBy(r => r.Id).First();
            var outer = new[] { RoomType.Storage, RoomType.Cargo, RoomType.Workshop, RoomType.Hydroponics, RoomType.Quarters, RoomType.Lounge }
                .SelectMany(t => w.Ship.LiveRooms.Where(r => r.Kind == t)).FirstOrDefault(r => w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r));
            var life = w.Ship.Machines.Where(m => m.Body.Type == FurnitureType.OxygenGenerator).OrderBy(m => m.Body.Id).FirstOrDefault()
                       ?? w.Ship.Machines.Where(m => m.Body.Type == FurnitureType.Scrubber).OrderBy(m => m.Body.Id).FirstOrDefault();
            long fireAt = -1, fireOut = -1, meteorAt = -1, holeAt = -1, holeShut = -1, lifeAt = -1, lifeFixed = -1;
            string lifeName = life?.Name ?? "-";

            for (int m = 0; m < days * 24 * 60; m++)
            {
                long since = w.Tick - t0;
                if (fireAt < 0 && since >= SimTime.Hours(6))
                {
                    var spot = fireRoom.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - fireRoom.Center).LengthSquared()).First();
                    if (Player.Fire(w, spot)) fireAt = w.Tick; else fireAt = -2;
                }
                if (meteorAt < 0 && outer != null && since >= SimTime.Hours(24)) { Player.Meteor(w, Scenarios.OuterTarget(w, outer), 1f); meteorAt = w.Tick; }
                if (lifeAt < 0 && life != null && since >= SimTime.Hours(48)) { w.Machines.Break(life); lifeAt = w.Tick; }
                Run(w, SimTime.Minutes(1));
                if (fireAt >= 0 && fireOut < 0 && w.Fire.Count == 0) fireOut = w.Tick;
                if (meteorAt >= 0 && holeShut < 0 && m % 5 == 0) { int open = OpenHoles(); if (holeAt < 0 && open > 0) holeAt = w.Tick; if (holeAt >= 0 && open == 0) holeShut = w.Tick; }
                if (lifeAt >= 0 && lifeFixed < 0 && life!.Faults.Count == 0) lifeFixed = w.Tick;
                if (args.Contains("--udebug") && m % 120 == 0)
                    Console.WriteLine($"     +{(w.Tick - t0) / (float)SimTime.TicksPerHour:0}h 컴 {(a.MainOnline ? "켜짐" : a.Core.BackupCore ? "예비" : "꺼짐")} · 배터리 {w.Power.BatteryPercent * 100:0}% · 꺼진 방 {w.Ship.LiveRooms.Count(r => !r.Powered)} · 고장 {string.Join(", ", w.Ship.Machines.Where(x => x.Faults.Count > 0).Select(x => $"{x.Name}:{string.Join("/", x.Faults.Select(f => f.Kind))}"))} · 펌프 {string.Join("/", w.Ship.FurnitureOf(FurnitureType.CoolantPump).Select(f => $"관{f.Machine!.Line:0.0}선{f.Machine.Feed:0.0}전{(f.Machine.Powered ? "O" : "X")}"))} 냉매 {w.Piping.CoolantFactor:0.00} · 원자로 {(w.Power.ReactorOnline ? "돎" : "멈춤")} 냉각 {w.Power.CoolingCapacity:0}/{PowerGrid.RestartCoolingKw:0} · 보조 {(w.Power.AuxRunning ? "돎" : "멈춤")} 연료 {w.Power.AuxFuel:0}h · 일감 {string.Join(", ", w.Board.Open.Where(o => o.Kind is WorkKind.Repair or WorkKind.ResetBreaker or WorkKind.RestoreCircuit or WorkKind.Reline or WorkKind.Rewire or WorkKind.RestartReactor or WorkKind.StartAux or WorkKind.Refuel).Select(o => $"{o.Title}({o.Robot?.Name ?? o.Assignee?.Name ?? "-"}{(o.BlockedUntil > w.Tick ? "/막힘" : "")})"))}");
                if (m % 10 != 0) continue;
                if (!a.MainOnline) offMin += 10;
                batMin = MathF.Min(batMin, w.Power.BatteryPercent);
                int dark = w.Ship.LiveRooms.Count(r => !r.Powered);
                darkMax = Math.Max(darkMax, dark);
                if (dark * 4 > w.Ship.LiveRooms.Count()) darkLongMin += 10;
                maxFaulty = Math.Max(maxFaulty, w.Ship.Machines.Count(x => x.Faults.Count > 0));
                foreach (var cm in crit)
                {
                    if (cm.Faults.Count > 0) { if (!critDown.ContainsKey(cm)) critDown[cm] = w.Tick; critDownMax = Math.Max(critDownMax, (int)((w.Tick - critDown[cm]) / SimTime.Minutes(1))); }
                    else critDown.Remove(cm);
                }
            }
            if (args.Contains("--udebug"))
                foreach (var e in w.Log.Entries.Where(e => e.Tick >= t0 + SimTime.Hours(27) && e.Tick <= t0 + SimTime.Hours(36)).Where(e => !e.Text.Contains("배치표") && !e.Text.Contains("냉각 펌프 유량") && !e.Text.Contains("시험 운전") && !e.Text.Contains("예비 센서 대조") && (e.Kind is LogKind.Ship or LogKind.Warning || e.Text.Contains("전기") || e.Text.Contains("원자로") || e.Text.Contains("발전") || e.Text.Contains("연료"))).Take(60))
                    Console.WriteLine($"     · +{(e.Tick - t0) / (float)SimTime.TicksPerHour:0.0}h [{e.Kind}] {e.Text}");
            if (args.Contains("--udebug"))
                foreach (var e in w.Log.Entries.Where(e => e.Tick >= t0 && (e.Text.Contains("사람이 없다") || e.Text.Contains("무인") || e.Text.Contains("재기동") || e.Text.Contains("보조 발전기"))).Take(40))
                    Console.WriteLine($"     ~ +{(e.Tick - t0) / (float)SimTime.TicksPerHour:0.0}h {e.Text}");
            if (args.Contains("--whyfix"))
            {
                Console.WriteLine($"     로봇 {string.Join(", ", w.Robots.Robots.Select(r => $"{r.Name}({r.Kind}{(RobotSystem.Fixer(r.Kind) ? "·수리" : "")})"))}");
                foreach (var m in w.Ship.Machines.Where(m => m.Faults.Count > 0))
                    foreach (var f in m.Faults) Console.WriteLine($"     고장 {m.Name}: {f.Kind} · 부품 {string.Join("+", f.Materials.Select(x => $"{ItemKinds.Name(x.kind)}{x.count}(배에 {w.Ship.CountStored(x.kind)})"))} · 일감 {string.Join(" ", w.Board.All.Where(o => !o.Closed && o.Target.Furniture == m.Body).Select(o => $"{o.Kind}/{(o.Robot?.Name ?? "-")}/{(o.BlockedUntil > w.Tick ? "막힘" : "")}{o.Detail}"))}");
            }
            static bool Ending(string t) => t.Contains("말았다") || t.Contains("못했다") || t.Contains("접었다") || t.Contains("끝을 보지"); // 떠나며 접힌 일의 마무리 기록은 괜찮다
            // 떠난 사람이 배 위에 있는 것처럼 나오는 기록 (떠난 뒤 1분부터)
            var ghosts = w.Log.Entries.Where(e => e.Tick > t0 + SimTime.Minutes(1) && e.CrewId >= 0 && e.CrewId < w.Crew.Count && w.Crew[e.CrewId].LeftShip && !Ending(e.Text)).Select(e => e.Text).ToList();
            if (ghosts.Count > 0)
            {
                problems.Add($"{name}: 떠난 사람 기록 {ghosts.Count}줄");
                foreach (var g in ghosts.Distinct().Take(args.Contains("--ghosts") ? 30 : 6)) Console.WriteLine($"     유령 기록: {g}");
            }
            int faultyEnd = w.Ship.Machines.Count(m => m.Faults.Count > 0);
            var critBroken = crit.Where(m => m.Faults.Count > 0).Select(m => m.Name).ToList();
            int rjobs = w.Robots.JobsDone - rj0, rbreak = w.Robots.Breakdowns - rb0, dsort = w.Drones.Sorties - ds0, djobs = w.Drones.JobsDone - dj0;
            int rDown = w.Robots.Robots.Count(r => !r.Operational || r.State is RobotState.Stalled or RobotState.Lost);
            int dDown = w.Drones.Drones.Count(d => !d.OnTrip && (!d.Operational || d.State is DroneState.Adrift or DroneState.Lost));
            string Min(long from, long to) => to > 0 ? $"{(to - from) / (float)SimTime.Minutes(1):0}분" : "못 함";
            string fireRes = fireAt == -2 ? "안 붙음" : fireAt < 0 ? "-" : fireOut > 0 ? $"{Min(fireAt, fireOut)}에 꺼짐" : "안 꺼짐";
            string holeRes = meteorAt < 0 ? "-" : holeAt < 0 ? "방까지 안 뚫림" : holeShut > 0 ? $"{Min(holeAt, holeShut)}에 막음" : "안 막힘";
            string lifeRes = lifeAt < 0 ? "-" : lifeFixed > 0 ? $"{(lifeFixed - lifeAt) / (float)SimTime.TicksPerHour:0.0}시간에 고침" : "못 고침";

            Console.WriteLine($"{name,-6} 떠난 사람 {crew} · 로봇 {w.Robots.Robots.Count} · 드론 {w.Drones.Drones.Count} | 주컴퓨터 꺼짐 {offMin}분 · 배터리 최저 {batMin * 100:0}% · 꺼진 방 최대 {darkMax}/{w.Ship.LiveRooms.Count()} (넷 중 하나 넘게 {darkLongMin}분)");
            Console.WriteLine($"       고장 설비 {faulty0}→최대 {maxFaulty}→끝 {faultyEnd}/{machines} · 생명 유지 설비 가장 오래 멎음 {critDownMax / 60f:0.0}시간{(critBroken.Count > 0 ? $" (끝에도 고장: {string.Join(",", critBroken)})" : "")} | " +
                              $"불 {fireRes} · 운석 {holeRes} · {lifeName} 고장 {lifeRes} | 로봇 일 {rjobs} · 고장 {rbreak} · 멈춤/잃음 {rDown} | 드론 출격 {dsort}/일 {djobs} · 못 씀 {dDown}");
            void P(bool bad, string what) { if (bad) problems.Add($"{name}: {what}"); }
            P(offMin > 6 * 60, $"주컴퓨터가 {offMin / 60f:0.0}시간 꺼졌다");
            P(darkLongMin > 6 * 60, $"방 넷 중 하나 넘게 전기가 {darkLongMin / 60f:0.0}시간 끊겼다");
            P(fireAt >= 0 && fireOut < 0, "사람 없이 불을 못 끈다");
            P(holeAt >= 0 && holeShut < 0, "사람 없이 외벽 구멍을 못 막는다");
            P(lifeAt >= 0 && lifeFixed < 0, $"사람 없이 {lifeName}를 못 고친다");
            P(critBroken.Count > 0, $"끝에 생명 유지 설비가 고장 난 채 ({string.Join(",", critBroken)})");
            P(rjobs == 0 && w.Robots.Robots.Count > 0, "로봇이 아무 일도 안 했다");
        }
        Console.WriteLine(problems.Count == 0 ? "\n✔ 문제 없음" : $"\n✘ 문제 {problems.Count}\n  " + string.Join("\n  ", problems));
        return problems.Count == 0 ? 0 : 1;
    }
}
