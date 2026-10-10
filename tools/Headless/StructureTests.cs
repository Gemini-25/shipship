using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v8 구조 시험: 연결부 지도, 구조 게이트(떨어져 나감·사출·되찾기의 분포)
public static partial class Program
{
    /// <summary>
    /// 구조 게이트 (v8): 시드마다 한 방에 큰 구조 사고(거대 운석, 연달아 큰 운석)를 걸고 닷새를 지켜본다.
    /// 때로는 드론이 고장 나 있고, 때로는 구조재가 없다. 결과를 다섯 갈래로 나눈다:
    /// 버팀(연결부를 이어 막음) · 사출 · 뜯겨 나감→되찾아 되살림 · 되찾았지만 잔해/도킹 중 · 잃음(떠내려가거나 표류 중).
    /// 통과: 적어도 세 갈래가 나오고, 한 번 이상은 구조 손상의 결과로 방이 떨어져 나갔다.
    /// </summary>
    private static int RunStructureGate(int runs, int seed)
    {
        var counts = new Dictionary<string, int>();
        int torn = 0, deaths = 0, downs = 0;
        Console.WriteLine($"구조 게이트 · {runs}회 · 각 5일 · 시드 {seed}부터\n");
        int only = int.TryParse(Environment.GetEnvironmentVariable("SHIPSIM_GATEONLY"), out var o) ? o : 0;
        for (int i = 0; i < runs; i++)
        {
            if (only > 0 && i + 1 != only) continue;
            var w = World.CreateDefault(seed + i);
            w.Propulsion.EvasionEnabled = false; // 운석이 맞았을 때의 구조 결과를 잰다
            var rng = new Rng(seed * 31 + i);
            for (int t = 0; t < SimTime.TicksPerDay; t++) w.Step();
            var rooms = w.Ship.Rooms.Where(r => r.DesignJoints > 0).ToList();
            var room = rng.Pick(rooms);
            var notes = new List<string>();
            if (rng.Chance(0.3f)) { foreach (var d in w.Drones.Drones) d.Faulty = true; Scenarios.LimitStock(w, ItemKind.Electronics, 0); notes.Add("드론 고장"); }
            if (rng.Chance(0.3f))
            {
                Scenarios.LimitStock(w, ItemKind.Structure, 0);
                Scenarios.LimitStock(w, ItemKind.MetalOre, 0);
                foreach (var dock in w.Ship.FurnitureOf(FurnitureType.DroneDock)) dock.Storage!.Take(ItemKind.Structure, 99);
                notes.Add("구조재 없음");
            }
            int hits = rng.Chance(0.5f) ? 1 : rng.Chance(0.6f) ? 2 : 3;
            var schedule = new List<(long tick, float size)>();
            long at = w.Tick;
            for (int k = 0; k < hits; k++)
            {
                float size = hits == 1 ? rng.Range(1.3f, 1.7f) : rng.Range(0.9f, 1.3f);
                schedule.Add((at, size));
                at += SimTime.Hours(rng.Range(1f, 8f));
            }
            notes.Insert(0, string.Join(" + ", schedule.Select(x => $"{(x.size >= 1.3f ? "거대" : "큰")} 운석")) + $"({room.Name})");
            long end = w.Tick + 5L * SimTime.TicksPerDay;
            int next = 0;
            while (w.Tick < end)
            {
                while (next < schedule.Count && schedule[next].tick <= w.Tick)
                {
                    if (!room.Detached) Player.Meteor(w, Scenarios.OuterTarget(w, room.Type), schedule[next].size);
                    if (Environment.GetEnvironmentVariable("SHIPSIM_GATELOG") == "1")
                        Console.WriteLine($"        [운석 {schedule[next].size:0.00}] {room.Name} 연결부 " + string.Join(" ", room.Joints.Select(j => $"{j.Strength * 100:0}")) +
                                          $" · 하중 {StructureSystem.StressOf(StructureSystem.Capacity(room), room.DesignJoints, StructureSystem.FrameLost(w, room)) * 100:0}% · 끊어짐 한계 {StructureSystem.SnapAt(room):0.00}");
                    next++;
                }
                w.Step();
            }
            var st = w.Structure;
            if (only > 0)
            {
                PrintStructure(w); PrintMap(w);
                foreach (var wo in w.Board.Open)
                    Console.WriteLine($"  남은 작업: {wo.Title} ({wo.Detail}) 긴급 {wo.Urgency:0.00} 담당 {wo.Assignee?.Name ?? wo.Drone?.Name ?? "-"} 보류 {wo.BlockedReason}");
                foreach (var c in w.Crew)
                    Console.WriteLine($"  {c.Name} {(c.Down ? "쓰러짐" : c.ActivityLabel)} @ {c.Room?.Name ?? "밖"} 체력 {c.Vitals.Health * 100:0} 우주복 {(c.Suit != null ? "O" : "-")}");
                Console.WriteLine($"  전력: 원자로 {w.Power.ReactorOutput:0.0}kW 배터리 {w.Power.BatteryPercent * 100:0}% · 산소 발생기 " +
                                  string.Join(" ", w.Ship.Furniture.Where(f => f.Type == FurnitureType.OxygenGenerator).Select(f => $"{f.Machine!.StatusText}/{(f.Machine.Powered ? "전기" : "무전")}")));
                foreach (var e in w.Log.Entries.Where(e => e.Text.Contains("재연결") || e.Text.Contains("이었다") || e.Text.Contains("되살") || e.Text.Contains("연결부")).TakeLast(25))
                    Console.WriteLine($"        {SimTime.Day(e.Tick)}일 {SimTime.Clock(e.Tick)} {e.Text}");
            }
            string outcome;
            var frag = st.Fragments.FirstOrDefault(f => f.Room == room);
            if (room.Jettisons > 0 && frag != null) outcome = frag.State == FragmentState.Lost ? "사출 · 잃음" : "사출";
            else if (room.Jettisons > 0) outcome = room.Docked ? "사출 → 되찾음 (도킹·잔해)" : "사출 → 되찾아 되살림";
            else if (room.Detachments > 0 && frag != null) outcome = frag.State == FragmentState.Lost ? "뜯겨 나감 · 잃음" : $"뜯겨 나감 · {(frag.State == FragmentState.Moored ? "계류" : frag.State == FragmentState.Towed ? "견인 중" : "표류")}";
            else if (room.Detachments > 0) outcome = room.Docked ? "뜯겨 나감 → 되찾음 (도킹·잔해)" : "뜯겨 나감 → 되찾아 되살림";
            else outcome = StructureSystem.StressOf(StructureSystem.Capacity(room), room.DesignJoints, StructureSystem.FrameLost(w, room)) > 1f ? "버팀 (하중 초과)" : "버팀";
            if (room.Detachments > room.Jettisons) torn++;
            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
            int dead = w.Crew.Count(c => c.Dead), down = w.Crew.Sum(c => c.Stats.TimesDown);
            deaths += dead; downs += down;
            var prof = ShipProfile.Measure(w).RelativeTo(w.InitialProfile!);
            if (Environment.GetEnvironmentVariable("SHIPSIM_GATELOG") == "1")
                foreach (var e in w.History.Events.Where(e => e.Kind is not (HistoryKind.Memory or HistoryKind.Bond) && e.Tick > w.Tick - 5L * SimTime.TicksPerDay).Take(40))
                    Console.WriteLine($"        {SimTime.Day(e.Tick)}일 {SimTime.Clock(e.Tick)} [{Kind(e.Kind)}] {e.Text}");
            Console.WriteLine($"  {i + 1,2}. {string.Join(" · ", notes)}\n      → {outcome} · 연결부 끊김 {st.JointBreaks} · 드론 일 {w.Drones.JobsDone} · EVA {w.Crew.Sum(c => c.EvaHours):0.0}h · 쓰러짐 {down} · 구조 {prof.Structure:0}");
        }
        Console.WriteLine("\n분포:");
        foreach (var (k, v) in counts.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  {k,-24} {v,2} ({v * 100 / runs}%)");
        string Group(string k) => k.StartsWith("버팀") ? "버팀" : k.StartsWith("사출") ? "사출" : k.Contains("되살림") ? "되살림" : k.Contains("되찾음") || k.Contains("계류") ? "도킹·잔해" : "잃음·표류";
        var groups = counts.Keys.Select(Group).Distinct().ToList();
        Console.WriteLine($"\n갈래 {groups.Count}가지 ({string.Join("·", groups)}) · 구조 손상으로 뜯겨 나감 {torn}회 · 쓰러짐 {downs} · 사망 {deaths}");
        bool ok = groups.Count >= 3 && torn >= 1;
        Console.WriteLine(ok ? "✔ 게이트 통과" : "✘ 게이트 미달");
        return ok ? 0 : 1;
    }

    /// <summary>구조 요약: 상한 방의 연결부, 조각, 드론, EVA.</summary>
    private static void PrintStructure(World w)
    {
        var st = w.Structure;
        var rooms = w.Ship.Rooms.Where(r => r.Detached || r.Docked || r.Jettison != null || r.Joints.Any(j => j.Broken || j.Strength < 0.8f || j.Truss)).ToList();
        foreach (var r in rooms)
            Console.WriteLine($"  [구조] {r.Name}: " + (r.Detached ? $"떨어짐 ({r.Fragment?.State} {r.Fragment?.Distance:0}칸) " : "") +
                              (r.Docked ? (r.Wreck ? "잔해 " : r.Restoring ? "되살리는 중 " : "임시 도킹 ") : "") +
                              (r.Jettison != null ? $"사출 {JettisonPlan.StageName(r.Jettison.Stage)} " : "") +
                              $"하중 {st.Stress.GetValueOrDefault(r.Id) * 100:0}% · " +
                              string.Join(" ", r.Joints.Select(j => $"{(j.Truss ? "T" : "")}{j.Index}:{(j.Released ? "풀림" : j.Broken ? "X" : $"{j.Strength * 100:0}")}/{j.Known * 100:0}")));
        foreach (var f in st.Fragments.Where(f => f.State == FragmentState.Lost))
            Console.WriteLine($"  [구조] {f.Room.Name} 잃음");
        Console.WriteLine($"  [구조] 끊어진 연결부 누적 {st.JointBreaks} · 떨어져 나감 {st.Detachments} (사출 {st.Jettisons}) · 되찾음 {st.Retrieved} · 잃음 {st.RoomsLost} · 골조 잃은 벽 {w.Ship.Walls.Count(kv => kv.Value.FrameLost)}");
        Console.WriteLine($"  [드론] 출동 {w.Drones.Sorties} · 일 {w.Drones.JobsDone} · 검사 {w.Drones.Inspections} · 견인 {w.Drones.Tows} · 에어락 {w.AirlockCycles}회 · 우주복 보충 {w.SuitRefills}회 · EVA {w.Crew.Sum(c => c.EvaHours):0.0}시간 ({string.Join("·", w.Crew.Where(c => c.EvaHours > 0.05f).Select(c => $"{c.Name} {c.EvaHours:0.0}"))})");
        foreach (var d in w.Drones.Drones.Where(d => d.State != DroneState.Docked || d.Faulty || d.Wrecked))
            Console.WriteLine($"  [드론] {d.Name} {d.State} 배터리 {d.Battery * 100:0}% 상태 {d.Condition * 100:0}%{(d.Faulty ? " 고장" : "")}{(d.Wrecked ? " 부서짐" : "")} — {d.Doing}");
    }

    /// <summary>격자와 연결부(숫자=강도 0~9, X=끊김), 드론 거치대(Q), 드론(d)을 찍는다.</summary>
    private static void PrintMap(World w)
    {
        var ship = w.Ship;
        var g = ship.Grid;
        var joints = w.Structure.Joints.Where(j => !j.Room.Detached).ToDictionary(j => j.Cell, j => j);
        for (int y = 0; y < g.Height; y++)
        {
            var line = new System.Text.StringBuilder();
            for (int x = 0; x < g.Width; x++)
            {
                var c = new Cell(x, y);
                char ch = g.Kind(c) switch { TileKind.Void => w.Paths.IsSpace(c) ? ',' : ' ', TileKind.Wall => '#', TileKind.Door => '+', _ => '.' };
                if (ship.FurnitureAt(c) is Furniture f) ch = f.Type == FurnitureType.DroneDock ? 'Q' : 'o';
                if (joints.TryGetValue(c, out var j)) ch = j.Broken ? 'X' : (char)('0' + Math.Min(9, (int)(j.Strength * 9.99f)));
                if (ship.WallAt(c) is WallState ws && ws.FrameLost) ch = '%';
                line.Append(ch);
            }
            Console.WriteLine(line.ToString());
        }
        foreach (var r in ship.Rooms)
            Console.WriteLine($"{r.Name,-6} 연결부 {r.Joints.Count} (설계 {r.DesignJoints}) 하중 {w.Structure.Stress.GetValueOrDefault(r.Id) * 100:0}% " +
                              string.Join(" ", r.Joints.Select(j => $"{j.Index}@{j.Cell}{(j.Broken ? "X" : $"{j.Strength * 100:0}")}/{j.Known * 100:0}")) +
                              (r.Detached ? $" · 떨어짐 {r.Fragment?.State} {r.Fragment?.Distance:0}칸" : ""));
        foreach (var d in w.Drones.Drones)
            Console.WriteLine($"{d.Name,-8} {d.State,-9} 배터리 {d.Battery * 100:0}% 상태 {d.Condition * 100:0}% @({d.Position.X:0.0},{d.Position.Y:0.0}) {d.Doing}");
    }
}
