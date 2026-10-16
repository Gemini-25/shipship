using System;
using System.Linq;
using ShipSim.Core;

// v23 구역 중계기 점검 (--relaytest): 구역 나누기 · 꺼지면 그 구역 무전이 끊김 · 끊긴 구역 로봇은 못 부름 · 스스로 판단 · 드론 임시 중계 · 수리 · 정전 전지 · 저장.
public static partial class Program
{
    private static int RunRelayTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"구역 중계기 점검 (v23) · 시드 {seed}\n");
        try
        {
            World Fresh() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.Hours(2)); return w; }
            RelayZone Far(World w)
            {
                var core = w.Automation.ComputerBody?.Room;
                return w.Relays.Zones.Where(z => core == null || !z.Rooms.Contains(core.Id)).OrderByDescending(z => w.Robots.Robots.Count(r => r.Room != null && z.Rooms.Contains(r.Room.Id))).ThenBy(z => z.Id).First();
            }

            // 1) 구역 나누기
            {
                var w = Fresh();
                var live = w.Ship.LiveRooms.ToList();
                var zs = w.Relays.Zones;
                Check("배를 구역으로 나누고 구역마다 중계기 — 모든 방이 한 구역에", zs.Count >= 2 && live.All(r => w.Relays.ZoneOf(r) != null) && zs.All(z => z.Rooms.Contains(z.RoomId)) && zs.All(z => z.Up),
                    $"구역 {zs.Count} · {string.Join(" / ", zs.Select(z => $"{z.Name} 방 {z.Rooms.Count} · 중계기 {w.Ship.Rooms[z.RoomId].Name}"))}");
            }

            // 2) 꺼지면 끊김 · 끊긴 구역 로봇은 부르지 못한다 · 드론이 임시로 잇는다 · 로봇이 고친다
            {
                var w = Fresh();
                var z = Far(w);
                w.Relays.Break(z, "시험 — 부쉈다");
                Run(w, 10);
                var inZone = w.Ship.Rooms.Where(r => z.Rooms.Contains(r.Id)).ToList();
                Check("중계기가 꺼지면 그 구역 무전이 끊긴다", !z.Up && inZone.All(r => !w.Relays.Reach(r)), $"{z.Name} {(z.Up ? "닿음" : "끊김")} · {w.Relays.Summary()}");
                var cutBots = w.Robots.Robots.Where(r => r.Room != null && z.Rooms.Contains(r.Room.Id) && r.CaseId < 0).Select(r => r.Id).ToList();
                var other = w.Ship.LiveRooms.First(r => !z.Rooms.Contains(r.Id) && w.Crew.All(c => c.Room != r) && r.Cells.Count(w.Ship.IsOpenFloor) >= 6 && r.Id != (w.Automation.ComputerBody?.Room.Id ?? -1));
                foreach (var c in other.Cells.Where(w.Ship.IsOpenFloor).Take(3)) w.Fire.Ignite(c, 0.9f);
                Run(w, SimTime.Minutes(3));
                var k = w.Commander.Cases.FirstOrDefault(c => c.Kind == CaseKind.Fire && c.RoomId == other.Id);
                bool calledCut = cutBots.Any(id => w.Robots.Robots.First(r => r.Id == id).CaseId == k?.Id && k != null);
                Check("끊긴 구역의 로봇은 다른 구역 불에 부르지 못한다", k != null && !calledCut && k.Sent >= 1, k == null ? "지휘 건 없음" : $"끊긴 구역 로봇 {cutBots.Count} · 출동 {k.Sent} · {string.Join(" / ", k.Notes.Select(n => n.Text))}");
                bool droneUp = false;
                for (int m = 0; m < 25 && !droneUp; m++) { Run(w, SimTime.Minutes(1)); droneUp = z.Broken && z.Up && z.DroneId >= 0; }
                Check("드론이 선체 밖 그 구역 곁에 떠서 무전을 임시로 잇는다", droneUp || w.Relays.Stats.DroneRelays >= 1 && !z.Broken,
                    $"{(z.DroneId >= 0 ? w.Drones.Drones.First(d => d.Id == z.DroneId).Name : "드론 없음")} · {w.Relays.Stats.Line()}");
                bool fixedIt = false;
                for (int m = 0; m < 240 && !fixedIt; m++) { Run(w, SimTime.Minutes(1)); fixedIt = !z.Broken; }
                Run(w, 10);
                var e = w.Log.Entries.Where(x => x.Text.Contains(z.Name) && x.Text.Contains("중계기를 고쳤다")).Select(x => x.Text).LastOrDefault();
                Check("로봇(못 하면 사람)이 케이블로 중계기를 고치면 다시 닿고 드론은 돌아간다", fixedIt && z.Up && z.DroneId < 0, $"{e ?? "기록 없음"} · {w.Relays.Stats.Line()}");
            }

            // 3) 끊긴 구역의 로봇은 스스로 판단한다 — 눈앞의 불
            {
                var w = Fresh();
                var core = w.Automation.ComputerBody?.Room;
                var bot = w.Robots.Robots.Where(r => RobotSystemCallable(w, r) && r.CaseId < 0 && r.Room != null && r.Room != core && w.Relays.ZoneOf(r.Room) is RelayZone rz && (core == null || !rz.Rooms.Contains(core.Id))
                        && r.Room.Cells.Count(w.Ship.IsOpenFloor) >= 6 && w.Crew.All(c => c.Room != r.Room) && r.Foam > 0.3f).OrderBy(r => r.Id).FirstOrDefault();
                if (bot == null) Check("끊긴 구역의 로봇이 스스로 눈앞의 불을 끈다", false, "알맞은 로봇 없음");
                else
                {
                    var z = w.Relays.ZoneOf(bot.Room)!;
                    w.Relays.Break(z, "시험 — 부쉈다");
                    Run(w, 10);
                    var room = bot.Room!;
                    foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - bot.Position).LengthSquared()).ThenBy(c => c.X).Skip(1).Take(2)) w.Fire.Ignite(c, 0.8f);
                    for (int t = 0; t < 6 && w.Relays.Stats.SelfActs == 0; t++) Run(w, 10);
                    Check("끊긴 구역의 로봇이 스스로 눈앞의 불을 끈다", w.Relays.Stats.SelfActs >= 1 && bot.FightingFire, $"{bot.Name} — {bot.Doing} · {w.Relays.Stats.Line()}");
                }
            }

            // 4) 정전: 전지로 30분 버티다 꺼진다
            {
                var w = Fresh();
                var z = Far(w);
                var host = w.Ship.Rooms[z.RoomId];
                bool upAt20 = false;
                for (int m = 0; m < 45; m++)
                {
                    host.BreakerOff = true;
                    Run(w, SimTime.Minutes(1));
                    if (m == 20) upAt20 = z.Up || z.DroneId >= 0;
                    if (!z.Up) break;
                }
                Check("중계기 방이 정전되면 전지로 30분쯤 버티다 꺼진다", upAt20 && (!z.Up || z.DroneId >= 0) && w.Relays.Stats.Drained >= 1, $"20분 {(upAt20 ? "닿음" : "끊김")} · 전지 {z.Battery * 100:0}% · {w.Relays.Stats.Line()}");
            }

            // 5) 저장 · 되살림
            {
                var w = Fresh();
                var z = Far(w);
                w.Relays.Break(z, "시험");
                Run(w, 10);
                var w2 = StateSave.Read(StateSave.Write(w));
                var z2 = w2.Relays.Zones[z.Id];
                Check("저장 · 되살림 — 구역 · 고장이 남는다", w2.Relays.Zones.Count == w.Relays.Zones.Count && z2.Broken && !z2.Up && z2.Rooms.SequenceEqual(z.Rooms), $"{z2.Name} {(z2.Broken ? "고장" : "멀쩡")} · {(z2.Up ? "닿음" : "끊김")}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }

    private static bool RobotSystemCallable(World w, Robot r) =>
        r.Operational && !r.Wrecked && !r.Runaway && r.Battery >= 0.3f && r.State is RobotState.Docked or RobotState.Active;
}
