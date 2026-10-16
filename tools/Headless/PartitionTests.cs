using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v10.2 방 크기 개조 시험: 같은 운석을 칸막이가 있는 방과 없는 방에 던져 본다
public static partial class Program
{
    /// <summary>
    /// 칸막이 게이트 (v10.2). 시드마다 하루를 돌린 뒤 큰 방(식당·침실·수경재배실·휴게실 중 하나)에 큰 운석을 던진다.
    /// 두 갈래: 그대로 · 운석 전에 그 방에 칸막이를 세워 둔 배. 열두 시간 동안 본다: 공기 탱크를 얼마나 썼나,
    /// 가장 나빴을 때 감압된(50kPa 아래) 칸 수, 쓰러진 사람, 그 방(들)이 다시 90kPa로 돌아오기까지.
    /// 통과: 칸막이 쪽이 감압된 칸이 평균적으로 적고 공기 탱크도 덜 쓴다.
    /// </summary>
    /// <summary>
    /// 칸막이가 교훈에서 나오는지 (v10.2): 둘째 날 식당(또는 침실)에 큰 운석을 맞히고 days일 동안 두고 본다.
    /// 개조·결정 기록과, 끝에 남은 개조 후보를 보여 준다.
    /// </summary>
    private static int RunPartitionCampaign(int days, int seed, string roomName)
    {
        var type = Enum.TryParse<RoomType>(roomName, out var t) ? t : RoomType.Mess;
        var w = World.CreateDefault(seed);
        long start = w.Tick + SimTime.TicksPerDay;
        while (w.Tick < start) w.Step();
        Player.Meteor(w, Scenarios.OuterTarget(w, type), 1.0f);
        long end = start + (long)days * SimTime.TicksPerDay;
        while (w.Tick < end) w.Step();
        Console.WriteLine($"칸막이 교훈 · {RoomTypes.Name(type)} 큰 운석 · {days}일 · 시드 {seed}");
        foreach (var e in w.History.Events.Where(e => e.Kind is HistoryKind.Decision or HistoryKind.Upgrade or HistoryKind.Incident))
            Console.WriteLine($"  {SimTime.Day(e.Tick),2}일 {SimTime.Clock(e.Tick)} {e.Text}");
        Console.WriteLine($"방 {w.Ship.Rooms.Count}개 · 칸막이 {w.History.Partitions} · 금속판 {w.Ship.CountStored(ItemKind.Plate)} 구조재 {w.Ship.CountStored(ItemKind.Structure)} 케이블 {w.Ship.CountStored(ItemKind.Cable)}");
        foreach (var p in Evolution.Candidates(w))
            Console.WriteLine($"  후보 {p.Kind} {p.Score:0.00} 살 수 있나 {Evolution.Affordable(w.Board, p.Cost)} · {p.Why}");
        return 0;
    }

    private static int RunPartitionGate(int runs, int seed)
    {
        string[] modes = { "그대로", "칸막이" };
        // v10.3: 같은 조건의 쌍끼리만 견준다 (칸막이를 세울 수 있었던 판만). 나눌 수 없던 판은 따로 센다
        var air = new List<float>[2] { new(), new() };
        var cellsLost = new List<int>[2] { new(), new() };
        var downs = new int[2];
        var recover = new List<float>[2] { new(), new() };
        var unsplit = new Dictionary<string, int>();
        RoomType[] big = { RoomType.Mess, RoomType.Quarters, RoomType.Hydroponics, RoomType.Lounge };
        Console.WriteLine($"칸막이 게이트 · {runs}회 × 2갈래 · 시드 {seed}부터\n");
        for (int i = 0; i < runs; i++)
        {
            var line = new List<string>();
            var res = new (float used, int worst, int down, float? back)?[2];
            var rng = new Rng(seed * 13 + i);
            var type = big[i % big.Length];
            float size = rng.Range(0.9f, 1.3f);
            float offsetH = rng.Range(0f, 12f);
            for (int k = 0; k < 2; k++)
            {
                var w = World.CreateDefault(seed + i);
                w.Propulsion.EvasionEnabled = false; // 운석이 맞았을 때 칸막이의 값을 잰다
                long start = w.Tick + SimTime.TicksPerDay + SimTime.Hours(offsetH);
                while (w.Tick < start) w.Step();
                var room = w.Ship.RoomsOf(type).First();
                var target = Scenarios.OuterTarget(w, type);
                string shape = $"{room.Name} {room.Cells.Count}칸";
                if (k == 1)
                {
                    if (Remodel.FindSplit(w, room) is not Remodel.SplitPlan sp || Remodel.Apply(w, sp) is not Room inner)
                    {
                        line.Add($"{modes[k]}: 나눌 줄 없음 (쌍에서 뺀다)");
                        unsplit[RoomTypes.Name(type)] = unsplit.GetValueOrDefault(RoomTypes.Name(type)) + 1;
                        continue;
                    }
                    shape = $"{room.Name} {room.Cells.Count}칸 + {inner.Name} {inner.Cells.Count}칸";
                }
                float reserve0 = w.Air.Reserve;
                Player.Meteor(w, target, size);
                long end = w.Tick + SimTime.Hours(12);
                int worst = 0;
                long? back = null;
                long hit = -1;
                var watch = w.Ship.Rooms.Where(r => r.Type == type).ToList();
                while (w.Tick < end)
                {
                    w.Step();
                    if (w.Tick % World.SystemInterval != 0) continue;
                    if (hit < 0 && w.Impacts.Count > 0) hit = w.Tick;
                    int lost = w.Ship.Rooms.Where(r => !r.Detached && r.Air.Pressure < 50f).Sum(r => r.Cells.Count);
                    worst = Math.Max(worst, lost);
                    if (hit >= 0 && back == null && worst > 0 && watch.All(r => r.Air.Pressure >= 90f)) back = w.Tick;
                }
                float used = (reserve0 - w.Air.Reserve) / w.Air.ReserveCapacity * 100f;
                int down = w.Crew.Count(c => c.Down || c.Dead);
                res[k] = (used, worst, down, back is long b && hit >= 0 ? (b - hit) / (float)SimTime.TicksPerHour : null);
                line.Add($"{modes[k]} ({shape}): 탱크 −{used:0}% · 감압 {worst}칸" + (down > 0 ? $" · 쓰러짐 {down}" : "") + (back is long bb && hit >= 0 ? $" · 재가압 {(bb - hit) / (float)SimTime.TicksPerHour:0.#}시간" : worst > 0 ? " · 재가압 못 함" : ""));
            }
            Console.WriteLine($"  #{i + 1,2} {(size >= 1.3f ? "거대" : "큰")} 운석 {RoomTypes.Name(type)}: " + string.Join(" | ", line));
            if (res[0] is { } r0 && res[1] is { } r1)
                for (int k = 0; k < 2; k++)
                {
                    var r = k == 0 ? r0 : r1;
                    air[k].Add(r.used);
                    cellsLost[k].Add(r.worst);
                    downs[k] += r.down;
                    if (r.back is float hb) recover[k].Add(hb);
                }
        }
        Console.WriteLine($"\n  같은 조건의 쌍 {air[0].Count}개" + (unsplit.Count > 0 ? $" · 나눌 수 없던 판 {string.Join(" · ", unsplit.Select(kv => $"{kv.Key} {kv.Value}"))}" : ""));
        for (int k = 0; k < 2; k++)
            Console.WriteLine($"  {modes[k],-4} 공기 탱크 평균 −{(air[k].Count > 0 ? air[k].Average() : 0):0}% · 감압된 칸 평균 {(cellsLost[k].Count > 0 ? cellsLost[k].Average() : 0):0.#} · 쓰러짐 {downs[k]}"
                              + (recover[k].Count > 0 ? $" · 재가압 평균 {recover[k].Average():0.#}시간" : ""));
        bool fewerCells = cellsLost[1].Count > 0 && cellsLost[1].Average() < cellsLost[0].Average();
        bool lessAir = air[1].Count > 0 && air[1].Average() <= air[0].Average();
        Console.WriteLine($"\n게이트: 칸막이 쪽 감압된 칸이 적다 {(fewerCells ? "✔" : "✘")} · 공기 탱크를 덜 쓴다 {(lessAir ? "✔" : "✘")}");
        bool pass = fewerCells && lessAir;
        Console.WriteLine(pass ? "✔ 게이트 통과" : "✘ 게이트 미달");
        return pass ? 0 : 1;
    }
}
