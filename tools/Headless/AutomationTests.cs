using System;
using System.Linq;
using ShipSim.Core;

// v12.5 중앙 컴퓨터 등급 · 수동 조종 · 판단 근거 · 예측 · 격벽 카운트다운 · 방침
public static partial class Program
{
    private static int RunAutomationTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"자동화 점검 (v12.5) · 시드 {seed}\n");

        // 1) 등급 (v13.0): 기본 V — 데이터망이 끊기면 내려가고(고장 사다리), 주 컴퓨터가 서면 II·I
        {
            var w = DayOne(seed, "Mirinae");
            int basic = w.Automation.Level;
            foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Data).ToList()) w.Net.Hurt(l, 1f, "시험");
            w.Net.Update(0f);
            Run(w, SimTime.Minutes(1));
            int cut = w.Automation.Level;
            string why = w.Automation.LevelWhy;
            var w2 = DayOne(seed, "Mirinae");
            foreach (var c in w2.Ship.FurnitureOf(FurnitureType.MainComputer)) w2.Machines.Break(c.Machine!, FaultKind.Wrecked);
            Run(w2, SimTime.Minutes(2));
            int down = w2.Automation.Level;
            Check("등급 — 기본 V, 데이터망이 끊기면 내려가고, 주 컴퓨터가 서면 II 이하", basic == 5 && cut <= 4 && down <= 2,
                $"기본 {AutomationSystem.LevelName(basic)} · 데이터망 끊김 {AutomationSystem.LevelName(cut)} ({why}) · 컴퓨터 정지 {AutomationSystem.LevelName(down)}");
        }
        // 2) 격벽: 사람 우선이면 안에 사람이 있을 때 2분 기다린다 · 배 우선이면 바로
        {
            bool[] waited = new bool[2]; bool[] lockedLater = new bool[2];
            for (int k = 0; k < 2; k++)
            {
                var w = DayOne(seed, "Mirinae");
                w.Automation.ShipFirst = k == 1;
                var room = w.Ship.RoomsOf(RoomType.Workshop).First();
                var c = w.Crew.First(x => x.CanAct);
                var spot = room.Cells.First(w.Ship.IsOpenFloor); c.Position = spot.Center; c.PreviousPosition = c.Position; c.Path = null; // 옮겨 놓았으니 가던 길은 버린다 (안 버리면 옛 경로를 따라 벽을 뚫고 방을 나간다)
                var wall = w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == room).Select(kv => kv.Key).First();
                Hull.Damage(w.Ship, wall, 1.2f);
                for (int dbg = 0; dbg < SimTime.Minutes(1); dbg += World.SystemInterval) { Run(w, World.SystemInterval); if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "9") Console.WriteLine($"   {k} t{dbg} 샘 {room.Leaking} 잠금 {room.Lockdown} 대기 {room.LockPendingUntil} 기압 {room.Air.Pressure:0} 사람 {c.Name} {c.Cell}→{spot} 방 {c.Room?.Name}|{w.Ship.RoomAt(c.Cell)?.Name} 밖 {c.Outside} 원정 {c.Away} {c.Job?.Label} 문잠김 {room.Doors.Count(d => d.Locked)} 자동 {w.Automation.AutoDoorsIn(room)}"); }
                waited[k] = room.LockPendingUntil >= 0 && !room.Doors.Any(d => d.Locked);
                Run(w, SimTime.Minutes(4));
                lockedLater[k] = room.Doors.Where(d => !d.IsExternal && d.Powered).All(d => d.Locked) || room.LockPendingUntil < 0;
            }
            Check("격벽 — 사람 우선은 기다리고, 배 우선은 바로 닫는다", waited[0] && lockedLater[0] && !waited[1], $"사람 우선: 기다림 {waited[0]} → 닫힘 {lockedLater[0]} · 배 우선: 기다림 {waited[1]}");
        }
        // 3) 관제석: 컴퓨터 혼자면 일찍 끊고 늦게 되돌린다 ↔ 사람이 조종하면 늦게 끊고 빨리 되돌린다 (정전된 총시간)
        {
            float[] darkMin = new float[2]; string op = "-";
            for (int k = 0; k < 2; k++)
            {
                var w = DayOne(seed, "Mirinae");
                for (int t = 0; t < 48 && !w.Crew.Any(c => c.CanAct && c.IsAwake && c.SkillLevel(Skill.Electrical) >= 0.5f && WatchLog.OnShift(c, w)); t++) Run(w, SimTime.Minutes(30));
                var room = w.Ship.RoomsOf(RoomType.Galley).First();
                if (k == 1)
                {
                    var lounge = w.Ship.RoomsOf(RoomType.Lounge).First();
                    lounge.BreakerOff = true;
                    for (int t = 0; t < 30 && w.Automation.Operator == null; t++) Run(w, SimTime.Minutes(2));
                }
                w.Moisture.AddWater(room, room.Cells.Count * 20f * 0.18f);
                for (int i = 0; i < 180; i++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (k == 0) foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.ManualControl).ToList()) w.Board.Close(o);
                    if (!room.Powered) darkMin[k]++;
                    if (w.Automation.Operator != null) op = w.Automation.Operator.Name;
                }
            }
            Check("관제석 — 사람이 조종하면 물 찬 방의 정전이 짧다", op != "-" && darkMin[1] < darkMin[0],
                $"컴퓨터 혼자: 정전 {darkMin[0]:0}분 · 관제석({op}): 정전 {darkMin[1]:0}분");
        }
        // 4) IV 추론: 판단 근거를 말한다 (III은 말하지 않는다)
        {
            int[] reasons = new int[2];
            for (int k = 0; k < 2; k++)
            {
                var w = DayOne(seed, "Mirinae");
                w.Automation.LevelCap = 3 + k;
                w.Policies.Set("controlseat", 1, "시험"); // 관제석은 컴퓨터에 (사람이 앉으면 사람이 말한다 — 컴퓨터의 말만 센다)
                foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.ManualControl).ToList()) w.Board.Close(o);
                foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                // 관제석에 사람이 앉으면 사람이 말한다 — 컴퓨터가 혼자 한 말만 센다 (틱마다 보고, 사람이 앉아 있던 틱의 말은 뺀다)
                for (int t = 0; t < SimTime.Minutes(60); t++)
                {
                    int before = w.Automation.Reasoning.Count;
                    w.Step();
                    if (w.Automation.Operator == null) reasons[k] += Math.Max(0, w.Automation.Reasoning.Count - before);
                    if (t % 15 != 0) continue;
                    foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.ManualControl).ToList()) w.Board.Close(o);
                    if (w.Automation.Operator is CrewMember seated) { seated.EndJob(w, ToilStatus.Interrupted); seated.NextThinkTick = w.Tick + 1; }
                }
                if (k == 1 || Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2")
                    Console.WriteLine($"    [{(k == 0 ? "III" : "IV")} · 단계 {w.Automation.Level} · 관제석 {w.Automation.Operator?.Name ?? "-"}]\n    " + string.Join("\n    ", w.Automation.Reasoning.Take(3).Select(r => $"{SimTime.Clock(r.tick)} {r.text}")));
            }
            Check("IV 추론 — 원인을 짚어 말한다 (III은 조용)", reasons[0] == 0 && reasons[1] > 0, $"III {reasons[0]}줄 · IV {reasons[1]}줄");
        }
        // 5) V 지휘: 연쇄 예측
        {
            var w = DayOne(seed, "Mirinae");
            foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
            Run(w, SimTime.Hours(1));
            var pred = w.Automation.Reasoning.FirstOrDefault(r => r.text.StartsWith("예측"));
            Check("V 지휘 — 배터리가 언제 바닥나는지 예측한다", pred.text != null, pred.text ?? "(없음)");
        }
        // 6) III 조정: 새는 설비의 방 급수 밸브를 원격으로 잠근다
        {
            var w = DayOne(seed, "Mirinae");
            var rec = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).First().Machine!;
            rec.Line = 0.1f;
            Run(w, SimTime.Minutes(3));
            Check("III 조정 — 새는 방 급수 밸브를 원격으로 잠근다", rec.Body.Room.ValveShut, $"{rec.Body.Room.Name} 밸브 {(rec.Body.Room.ValveShut ? "잠김" : "열림")} · 샌 물 {w.Moisture.Stats.Leaked:0.0}L");
        }
        // 7) 방침 회의: 늦게 닫아 옆방까지 잃은 일이 쌓이면
        {
            var w = DayOne(seed, "Mirinae");
            w.Automation.LateSeals = 3;
            Run(w, SimTime.TicksPerDay + SimTime.Hours(1));
            var dec = w.History.Events.LastOrDefault(e => e.Text.Contains("배 우선"));
            Check("방침 회의 — 늦게 닫아 잃은 일이 쌓이면 배 우선을 두고 표결한다", dec != null, dec?.Text ?? "(회의 없음)");
        }
        // 8) 결정론
        {
            uint H() { var w = DayOne(seed, "Mirinae"); w.Moisture.AddWater(w.Ship.RoomsOf(RoomType.Galley).First(), 250f); Run(w, SimTime.Hours(8)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("자동화가 든 배의 결정론", a == b, $"지문 {a:x8} / {b:x8}");
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 자동화 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
