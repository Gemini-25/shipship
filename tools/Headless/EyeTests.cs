using System;
using System.Linq;
using ShipSim.Core;

// v23 눈의 그물 점검 (--eyetest): 로봇이 본 것이 장부에 · 감지기 대조 · 모르는 방 정찰 · 끊긴 구역의 눈은 안 닿음.
public static partial class Program
{
    private static int RunEyeTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"눈의 그물 점검 (v23) · 시드 {seed}\n");
        try
        {
            World Fresh() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.Hours(2)); return w; }
            (Robot bot, Room room)? Parked(World w) =>
                w.Robots.Robots.Where(r => r.Operational && r.State == RobotState.Docked && r.Room != null && r.Room.Type != RoomType.Corridor && w.Relays.Reach(r.Room))
                    .OrderBy(r => r.Id).Select(r => ((Robot, Room)?)(r, r.Room!)).FirstOrDefault();

            // 1) 로봇의 눈: 감지기 값이 멈춘 방도 로봇이 있으면 장부가 새롭다
            {
                var w = Fresh();
                var p = Parked(w);
                if (p is not (Robot bot, Room room)) Check("로봇의 눈 — 감지기가 멎은 방도 로봇이 보면 장부가 새롭다", false, "거치대 로봇 없음");
                else
                {
                    w.Automation.Belief.Break(room, SensorFault.Stuck, "시험 — 값이 멈췄다");
                    Run(w, SimTime.Minutes(3)); // (더 두면 눈의 그물이 멈춘 감지기를 잡아 고친다 — 그건 대조 시험이 본다)
                    var f = w.Automation.Facts.Get(room, FactKind.People);
                    float age = (w.Tick - f.At) / (float)SimTime.Minutes(1);
                    Check("로봇의 눈 — 감지기가 멎은 방도 로봇이 보면 장부가 새롭다", f.From == FactFrom.Robot && age <= 1f && !w.Automation.Facts.Unknown(room, FactKind.People),
                        $"{room.Name} · {w.Automation.Facts.Line(room, FactKind.People)} · {w.Eyes.Stats.Line()}");
                }
            }

            // 2) 대조: 로봇 눈과 감지기가 계속 다르면 감지기를 의심하고 다시 맞춘다
            {
                var w = Fresh();
                var p = Parked(w);
                if (p is not (Robot bot, Room room)) Check("대조 — 로봇 눈과 감지기가 계속 다르면 감지기를 의심하고 다시 맞춘다", false, "거치대 로봇 없음");
                else
                {
                    var b = w.Automation.Belief.Of(room);
                    w.Automation.Belief.Break(room, SensorFault.Stuck, "시험 — 값이 멈췄다");
                    for (int m = 0; m < 6 && b.Fault != SensorFault.None; m++) { room.Air.O2 = MathF.Max(8f, b.O2 - 6f); Run(w, SimTime.Minutes(1)); }
                    Check("대조 — 로봇 눈과 감지기가 계속 다르면 감지기를 의심하고 다시 맞춘다", b.Fault == SensorFault.None && w.Eyes.Stats.Stucks >= 1,
                        $"{room.Name} · 감지기 {BeliefModel.FaultName(b.Fault)} · 믿음 {b.Trust:0.00} · {w.Eyes.Stats.Line()}");
                }
            }

            // 3) 정찰: 주 컴퓨터가 오래 모르는 중요한 방에 로봇을 보낸다
            {
                var w = Fresh();
                var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Quarters or RoomType.Medbay or RoomType.Galley && w.Robots.Robots.All(x => x.Room != r)).OrderBy(r => r.Id).First();
                w.Automation.Belief.Break(room, SensorFault.Stuck, "시험 — 값이 멈췄다");
                bool unknown = false, refreshed = false;
                for (int m = 0; m < 90 && !refreshed; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (w.Automation.Facts.Unknown(room, FactKind.People)) unknown = true;
                    var f = w.Automation.Facts.Get(room, FactKind.People);
                    refreshed = unknown && f.From == FactFrom.Robot && w.Tick - f.At <= SimTime.Minutes(1);
                }
                Check("정찰 — 오래 모르는 중요한 방은 로봇이 가서 본다", refreshed, $"{room.Name} · 모름이 됨 {(unknown ? "예" : "아니오")} · {w.Automation.Facts.Line(room, FactKind.People)} · {w.Eyes.Stats.Line()}");
            }

            // 4) 끊긴 구역의 로봇 눈은 주 컴퓨터에 닿지 않는다
            {
                var w = Fresh();
                var core = w.Automation.ComputerBody?.Room;
                var p = w.Robots.Robots.Where(r => r.Operational && r.State == RobotState.Docked && r.Room != null && r.Room != core && w.Relays.ZoneOf(r.Room) is RelayZone z && (core == null || !z.Rooms.Contains(core.Id)))
                    .OrderBy(r => r.Id).FirstOrDefault();
                if (p == null) Check("끊긴 구역의 로봇 눈은 주 컴퓨터에 닿지 않는다", false, "알맞은 로봇 없음");
                else
                {
                    var room = p.Room!;
                    w.Relays.Break(w.Relays.ZoneOf(room)!, "시험");
                    w.Automation.Belief.Break(room, SensorFault.Stuck, "시험");
                    Run(w, 10);
                    long at0 = w.Automation.Facts.Get(room, FactKind.People).At;
                    Run(w, SimTime.Minutes(3));
                    var z = w.Relays.ZoneOf(room)!;
                    long at1 = w.Automation.Facts.Get(room, FactKind.People).At;
                    Check("끊긴 구역의 로봇 눈은 주 컴퓨터에 닿지 않는다 (드론이 잇기 전)", z.Up || at1 == at0, $"{room.Name} · {z.Name} {(z.Up ? "드론이 이었다" : "끊김")} · 장부 {(at1 == at0 ? "그대로" : "새로움")}");
                }
            }

            // 5) (참고) 평소 하루: 정찰 · 의심이 얼마나 잦나
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(24));
                var rooms = w.Log.Entries.Where(e => e.Text.Contains("정찰 보낸다")).Select(e => e.Text.Split(':').Skip(1).FirstOrDefault()?.Split('—')[0].Trim() ?? "?").GroupBy(x => x).Select(g => $"{g.Key}×{g.Count()}");
                Console.WriteLine($"  · 참고 — 평소 하루: {w.Eyes.Stats.Line()} · 정찰한 방 {string.Join(", ", rooms)}");
                Check("평소 하루 — 정찰은 드물다 (하루 12번 이하)", w.Eyes.Stats.Scouts <= 12, $"{w.Eyes.Stats.Scouts}번");
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
}
