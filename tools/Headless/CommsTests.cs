using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v10.1 통신실 시험: 같은 운석을 센서가 멀쩡할 때 / 주 컴퓨터가 꺼졌을 때 / 센서가 고장 났을 때 던져 본다
public static partial class Program
{
    /// <summary>
    /// 통신실 게이트 (v10.1). 시드마다 하루를 돌린 뒤, 사람이 가장 많이 있는 바깥 방에 큰 운석을 던진다.
    /// 세 갈래: 정상(센서+컴퓨터) · 컴퓨터 꺼짐(한 시간 전부터 — 사람이 통신실 화면을 지키면 수동 판독) · 센서 고장.
    /// 본다: 어떤 경보가 몇 분 전에 났나, 부딪힐 때 그 방에 남은 사람, 파편에 다친 사람, 옆방까지 번진 감압.
    /// 통과: 정상일 때 다친 사람이 센서 고장일 때보다 적고, 세 갈래의 경보 분포가 다르다 (정상은 대부분 센서 경보).
    /// </summary>
    private static int RunCommsGate(int runs, int seed)
    {
        string[] modes = { "정상", "컴퓨터 꺼짐", "센서 고장" };
        var hurtBy = new int[3];
        var leftIn = new int[3];
        var levels = new Dictionary<WarnLevel, int>[3];
        var spread = new int[3];
        var leads = new List<float>[3];
        for (int k = 0; k < 3; k++) { levels[k] = new(); leads[k] = new(); }
        Console.WriteLine($"통신실 게이트 · {runs}회 × 3갈래 · 시드 {seed}부터\n");
        for (int i = 0; i < runs; i++)
        {
            var line = new List<string>();
            string? roomName = null;
            for (int k = 0; k < 3; k++)
            {
                var w = World.CreateDefault(seed + i);
                var rng = new Rng(seed * 17 + i);
                long start = w.Tick + SimTime.TicksPerDay + SimTime.Hours(rng.Range(0f, 14f));
                // 컴퓨터는 한 시간 전에 꺼진다 (사람이 레이더를 지키러 갈 틈) · 센서는 방금 고장 난다
                while (w.Tick < start - SimTime.Hours(1)) w.Step();
                if (k == 1) foreach (var c in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(c.Machine!, FaultKind.StorageFault);
                while (w.Tick < start) w.Step();
                if (k == 2) foreach (var s in w.Ship.FurnitureOf(FurnitureType.SensorArray)) w.Machines.Break(s.Machine!, FaultKind.RadarFault);
                // 사람이 가장 많은 바깥 방 (같은 시드면 세 갈래가 같은 방을 노린다: 고장은 방금 났다)
                var outer = w.Ship.Rooms.Where(r => r.Type is not (RoomType.Corridor or RoomType.Comms) && !r.Detached
                                                    && w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r)).ToList();
                var room = outer.OrderByDescending(r => w.Crew.Count(c => !c.Dead && c.Room == r)).ThenBy(r => r.Id).First();
                roomName ??= room.Name;
                var m = Player.Meteor(w, Scenarios.OuterTarget(w, room.Type), 1.0f)!;
                var health = w.Crew.Select(c => c.Vitals.Health).ToArray();
                while (w.Tick < m.Arrive - 1) w.Step();
                health = w.Crew.Select(c => c.Vitals.Health).ToArray();
                int inside = w.Crew.Count(c => !c.Dead && c.Room == room);
                var neighbors = room.Doors.Select(d => d.RoomA == room ? d.RoomB : d.RoomA).Where(r => r != null && r.Type != RoomType.Corridor).Distinct().ToList();
                w.Step(); w.Step();
                int hurt = w.Crew.Where((c, j) => health[j] - c.Vitals.Health > 0.04f).Count();
                long watch = w.Tick + SimTime.Hours(1);
                float minNeighbor = 101f;
                while (w.Tick < watch)
                {
                    w.Step();
                    foreach (var r in neighbors) minNeighbor = MathF.Min(minNeighbor, r!.Air.Pressure);
                    minNeighbor = MathF.Min(minNeighbor, w.Ship.Rooms.First(r => r.Type == RoomType.Corridor).Air.Pressure);
                }
                hurtBy[k] += hurt;
                leftIn[k] += inside;
                levels[k][m.Warned] = levels[k].GetValueOrDefault(m.Warned) + 1;
                if (m.Warned != WarnLevel.None) leads[k].Add((m.Arrive - m.WarnedAt) / (float)SimTime.Minutes(1));
                if (minNeighbor < 85f) spread[k]++;
                line.Add($"{modes[k]}: {SensorSystem.LevelName(m.Warned)}" + (m.Warned != WarnLevel.None ? $" {(m.Arrive - m.WarnedAt) / (float)SimTime.Minutes(1):0.#}분" : "")
                         + $" · 남은 {inside} · 부상 {hurt}" + (m.Sealed ? " · 미리 닫음" : "") + (minNeighbor < 85f ? $" · 옆방 {minNeighbor:0}kPa" : ""));
            }
            Console.WriteLine($"  #{i + 1,2} {roomName}: " + string.Join(" | ", line));
        }
        Console.WriteLine();
        for (int k = 0; k < 3; k++)
            Console.WriteLine($"  {modes[k],-7} 경보 {string.Join(" · ", levels[k].OrderByDescending(x => x.Key).Select(x => $"{SensorSystem.LevelName(x.Key)} {x.Value}"))}"
                              + (leads[k].Count > 0 ? $" (평균 {leads[k].Average():0.#}분 전)" : "")
                              + $" · 부딪힐 때 그 방에 남은 사람 {leftIn[k]} · 다친 사람 {hurtBy[k]} · 옆방·통로 감압 {spread[k]}회");
        bool fewer = hurtBy[0] < hurtBy[2] || (hurtBy[0] == hurtBy[2] && leftIn[0] < leftIn[2]);
        bool sensorMostly = levels[0].GetValueOrDefault(WarnLevel.Sensor) * 2 > runs;
        bool differ = !levels[0].OrderBy(x => x.Key).SequenceEqual(levels[2].OrderBy(x => x.Key));
        Console.WriteLine($"\n게이트: 정상이 덜 다침 {(fewer ? "✔" : "✘")} · 정상은 대부분 센서 경보 {(sensorMostly ? "✔" : "✘")} · 갈래마다 경보가 다름 {(differ ? "✔" : "✘")}");
        bool pass = fewer && sensorMostly && differ;
        Console.WriteLine(pass ? "✔ 게이트 통과" : "✘ 게이트 미달");
        return pass ? 0 : 1;
    }
}
