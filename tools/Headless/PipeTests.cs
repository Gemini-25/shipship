using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v9 배관·냉각 루프 시험: 배관 요약, 배관 지도, 배관 게이트 (결과 분포)
public static partial class Program
{
    /// <summary>배관 요약: 냉각수·냉각·노심, 정상이 아닌 구간, 누적.</summary>
    private static void PrintPipes(World w)
    {
        var net = w.Piping;
        if (net.Segments.Count == 0) return;
        Console.WriteLine($"  [배관] 냉각수 {net.CoolantFraction * 100:0}% · 냉각 {net.CoolingKw:0}kW (분기 {net.FlowingBranches}/{net.Branches.Count}) · 노심 {w.Power.ReactorTemperature:0}℃ · 원자로 {(w.Power.ReactorOnline ? "가동" : "정지")}" +
                          $" · 새어 나간 냉각수 {net.CoolantLost:0}L · 보충 {net.CoolantAdded:0}L · 새어 나간 물 {net.WaterLost:0}L · 터짐 {net.Bursts} · 우회 {net.BypassesLaid}");
        foreach (var s in net.Segments.Where(s => !s.Sound || s.Closed || s.Bypass > 0f || s.Patched || s.RadiatorCondition < 0.95f || s.Breaks > 0))
            Console.WriteLine($"  [배관] {s.Name}: {s.StateText} · 상태 {s.Integrity * 100:0}%/{s.MaxIntegrity * 100:0}%" +
                              (s.Leaking ? $" · 샘 {s.LeakRate:0}L/시간" : "") + (s.Bypass > 0f ? $" · 우회 {s.Bypass * 100:0}%" : "") +
                              (s.Radiator.Count > 0 ? $" · 방열판 {s.RadiatorCondition * 100:0}%" : "") +
                              $" · 터짐 {s.Breaks} 교체 {s.Repairs} 밀봉 {s.Patches} 우회 {s.Bypasses}");
    }

    /// <summary>격자에 배관을 그린다: H 고온관, 1·2 분기, C 귀환관, W 급수 본관, F 보충관, R 방열판, v 밸브, P 보충구.</summary>
    private static void PrintPipeMap(World w)
    {
        var ship = w.Ship;
        var g = ship.Grid;
        var net = w.Piping;
        var marks = new Dictionary<Cell, char>();
        foreach (var s in net.Segments)
        {
            char ch = s.Role switch
            {
                PipeRole.HotLeg => 'H', PipeRole.ColdLeg => 'C', PipeRole.WaterMain => 'W', PipeRole.WaterFeed => 'F',
                _ => (char)('1' + s.Branch),
            };
            foreach (var c in s.Path) marks[c] = ch;
            foreach (var c in s.Radiator) marks[c] = 'R';
        }
        foreach (var s in net.Segments) marks[s.ValveCell] = 'v';
        marks[net.FillPort] = 'P';
        for (int y = 0; y < g.Height; y++)
        {
            var line = new System.Text.StringBuilder();
            for (int x = 0; x < g.Width; x++)
            {
                var c = new Cell(x, y);
                char ch = g.Kind(c) switch { TileKind.Void => ' ', TileKind.Wall => '#', TileKind.Door => '+', _ => '.' };
                if (ship.FurnitureAt(c) is Furniture f) ch = 'o';
                if (marks.TryGetValue(c, out var m)) ch = m;
                line.Append(ch);
            }
            Console.WriteLine(line.ToString().TrimEnd());
        }
        foreach (var s in net.Segments)
            Console.WriteLine($"{s.Name,-10} 칸 {s.Path.Count} · 밸브 {s.ValveCell} ({s.ValveRoom?.Name}) · 지나는 방 {string.Join("·", s.Path.Select(c => ship.RoomAt(c)?.Name).Where(n => n != null).Distinct())}" +
                              (s.Radiator.Count > 0 ? $" · 방열판 {s.Radiator.Count}칸" : ""));
        Console.WriteLine($"냉각 {net.ComputeCooling():0}kW · 보충구 {net.FillPort} ({ship.RoomAt(net.FillPort)?.Name})");
    }

    /// <summary>
    /// 배관 게이트: 하루 돌린 뒤 무작위 배관 사고 (관 파손·운석, 재료 부족을 섞어서) → 사흘 뒤 결과 분포.
    /// 기준: 갈래가 셋 이상 (완전 복구 / 땜질로 버팀 / 냉각 절반·장기 / 원자로 정지·실패).
    /// </summary>
    private static int RunPipeGate(int runs, int seed)
    {
        var counts = new Dictionary<string, int>();
        int scrams = 0, bypasses = 0, patches = 0, burns = 0, downs = 0, deaths = 0, isolateYes = 0, isolateNo = 0;
        int only = int.TryParse(Environment.GetEnvironmentVariable("SHIPSIM_GATEONLY"), out var o) ? o : 0;
        bool log = Environment.GetEnvironmentVariable("SHIPSIM_GATELOG") == "1";
        Console.WriteLine($"배관 게이트 · {runs}회 · 각 3일 · 시드 {seed}부터\n");
        for (int i = 0; i < runs; i++)
        {
            if (only > 0 && i + 1 != only) continue;
            var w = World.CreateDefault(seed + i);
            var rng = new Rng(seed * 17 + i);
            for (int t = 0; t < SimTime.TicksPerDay; t++) w.Step();
            var net = w.Piping;
            var notes = new List<string>();
            if (rng.Chance(0.3f))
            {
                Scenarios.LimitStock(w, ItemKind.Plate, rng.Chance(0.5f) ? 0 : 1);
                Scenarios.LimitStock(w, ItemKind.MetalOre, 0);
                foreach (var dock in w.Ship.FurnitureOf(FurnitureType.DroneDock)) dock.Storage!.Take(ItemKind.Plate, 99);
                notes.Add("금속판 부족");
            }
            if (rng.Chance(0.3f))
            {
                Scenarios.LimitStock(w, ItemKind.Sealant, 0);
                foreach (var dock in w.Ship.FurnitureOf(FurnitureType.DroneDock)) dock.Storage!.Take(ItemKind.Sealant, 99);
                notes.Add("실링폼 없음");
            }
            if (rng.Chance(0.2f)) { w.Water.Level = MathF.Min(w.Water.Level, 90f); notes.Add("물 90L"); }
            int kind = rng.Range(0, 7);
            int scramBefore = w.History.Scrams;
            switch (kind)
            {
                case 0: case 1:
                {
                    var s = rng.Chance(0.5f) ? net.HotLeg! : net.ColdLeg!;
                    float sev = rng.Range(0.3f, 1f);
                    net.Damage(s, sev, s.Path[rng.Range(0, s.Path.Count)], "배관 파손");
                    notes.Insert(0, $"{s.Name} {(s.Severed ? "끊어짐" : $"누수 {s.LeakRate:0}L/시간")}");
                    break;
                }
                case 2:
                {
                    var s = net.Branches[rng.Range(0, net.Branches.Count)];
                    net.Damage(s, rng.Range(0.4f, 1f), s.Path[rng.Range(0, s.Path.Count)], "배관 파손");
                    notes.Insert(0, $"{s.Name} {(s.Severed ? "끊어짐" : $"누수 {s.LeakRate:0}L/시간")}");
                    break;
                }
                case 3:
                {
                    var s = rng.Chance(0.6f) ? net.WaterMain! : net.WaterFeed!;
                    net.Damage(s, rng.Range(0.4f, 1f), s.Path[rng.Range(0, s.Path.Count)], "배관 파손");
                    notes.Insert(0, $"{s.Name} {(s.Severed ? "끊어짐" : $"누수 {s.LeakRate:0}L/시간")}");
                    break;
                }
                case 4: case 5:
                {
                    var room = rng.Chance(0.6f) ? RoomType.Cooling : RoomType.Reactor;
                    float size = rng.Range(0.8f, 1.3f);
                    Player.Meteor(w, Scenarios.OuterTarget(w, room), size);
                    notes.Insert(0, $"{(size >= 1.1f ? "큰" : "중간")} 운석({RoomTypes.Name(room)})");
                    break;
                }
                default:
                {
                    // 두 군데: 분기 하나 + 고온관 작은 누수
                    var b = net.Branches[rng.Range(0, net.Branches.Count)];
                    net.Damage(b, 0.9f, b.Path[b.Path.Count / 2], "배관 파손");
                    net.Damage(net.HotLeg!, 0.4f, net.HotLeg!.Path[1], "배관 파손");
                    notes.Insert(0, $"{b.Name} 끊어짐 + 고온관 누수");
                    break;
                }
            }
            w.Board.RequestScan();
            long end = w.Tick + 3L * SimTime.TicksPerDay;
            float minCoolant = 1f, maxTemp = 0f;
            while (w.Tick < end)
            {
                w.Step();
                if (w.Tick % SimTime.Minutes(5) == 0)
                {
                    minCoolant = MathF.Min(minCoolant, net.CoolantFraction);
                    maxTemp = MathF.Max(maxTemp, w.Power.ReactorTemperature);
                }
            }
            int sc = w.History.Scrams - scramBefore;
            scrams += sc;
            bypasses += net.BypassesLaid;
            patches += net.Segments.Sum(s => s.Patches);
            int burnt = w.Crew.Count(c => c.Vitals.InjuryCause == "증기 화상");
            burns += burnt;
            int down = w.Crew.Sum(c => c.Stats.TimesDown);
            downs += down;
            deaths += w.Crew.Count(c => c.Dead);
            var decisions = w.History.Events.Where(e => e.Kind == HistoryKind.Decision && e.Text.Contains("잠그기 (원자로 정지)")).ToList();
            isolateYes += decisions.Count(e => !e.Text.Contains("부결") && !e.Text.Contains("미뤘다"));
            isolateNo += decisions.Count(e => e.Text.Contains("부결") || e.Text.Contains("미뤘다"));

            bool coolantOk = net.CoolantFraction >= 0.75f;
            bool allSound = net.Segments.All(s => s.Integrity >= 0.75f && !s.Patched && !s.Closed && s.Bypass <= 0f && s.RadiatorCondition >= 0.7f);
            bool makeshift = net.Segments.Any(s => s.Bypass > 0f && s.Closed || s.Patched);
            bool reactor = w.Power.ReactorOnline;
            string outcome =
                !reactor ? (w.Power.LowPowerMode ? "원자로 저출력 수동" : "원자로 정지") :
                allSound && coolantOk ? (sc > 0 ? "완전 복구 (정지를 겪고)" : "완전 복구") :
                makeshift && net.FlowingBranches >= net.Branches.Count && net.MainFlow ? "땜질로 버팀 (우회·밀봉)" :
                net.FlowingBranches < net.Branches.Count || !coolantOk ? "냉각 부족 (장기)" : "부분 복구";
            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
            if (log || only > 0)
            {
                foreach (var e in w.History.Events.Where(e => e.Kind is not (HistoryKind.Memory or HistoryKind.Bond) && e.Tick > end - 4L * SimTime.TicksPerDay).Take(40))
                    Console.WriteLine($"        {SimTime.Day(e.Tick)}일 {SimTime.Clock(e.Tick)} [{Kind(e.Kind)}] {e.Text}");
                PrintPipes(w);
                foreach (var ord in w.Board.Open.Where(x => x.Kind is WorkKind.CloseValve or WorkKind.OpenValve or WorkKind.PatchPipe or WorkKind.ReplacePipe
                             or WorkKind.LayBypass or WorkKind.RefillCoolant or WorkKind.RepairRadiator or WorkKind.IsolateMain or WorkKind.RestartReactor or WorkKind.ManualStart))
                    Console.WriteLine($"  남은 작업: {ord.Title} ({ord.Detail}) 긴급 {ord.Urgency:0.00} 보류 {ord.BlockedReason}");
            }
            Console.WriteLine($"  {i + 1,2}. {string.Join(" · ", notes)}\n      → {outcome} · 긴급 정지 {sc} · 최저 냉각수 {minCoolant * 100:0}% · 최고 노심 {maxTemp:0}℃ · 우회 {net.BypassesLaid} · 밀봉 {net.Segments.Sum(s => s.Patches)} · 증기 화상 {burnt} · 쓰러짐 {down} · 작물 손실 {w.Machines.CropsLost}");
        }
        Console.WriteLine("\n분포:");
        foreach (var (k, v) in counts.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  {k,-22} {v,2} ({v * 100 / Math.Max(1, runs)}%)");
        string Group(string k) => k.StartsWith("완전") ? "완전" : k.StartsWith("땜질") ? "땜질" : k.StartsWith("원자로") ? "정지" : "장기";
        var groups = counts.Keys.Select(Group).Distinct().ToList();
        Console.WriteLine($"\n갈래 {groups.Count}가지 ({string.Join("·", groups)}) · 긴급 정지 {scrams} · 우회 배관 {bypasses} · 임시 밀봉 {patches} · 본관 잠그기 결정 찬성 {isolateYes} 부결·미룸 {isolateNo} · 증기 화상 {burns} · 쓰러짐 {downs} · 사망 {deaths}");
        bool ok = groups.Count >= 3;
        Console.WriteLine(ok ? "✔ 게이트 통과" : "✘ 게이트 미달");
        return ok ? 0 : 1;
    }
}

// v9.2 자동화 게이트: 같은 사고를 자동화가 켜진 배와 꺼진 배가 겪으면 무엇이 달라지나
public static partial class Program
{
    private static int RunAutoGate(int runs, int seed)
    {
        Console.WriteLine($"자동화 게이트 · 사고 {runs}가지 × (자동화 켜짐 / 꺼짐) · 각 2일 · 시드 {seed}부터\n");
        var sums = new Dictionary<bool, (int downs, float tank, int full, int partial, int longTerm, int fail, float manual)>();
        var outcomes = new Dictionary<bool, Dictionary<string, int>> { [true] = new(), [false] = new() };
        for (int i = 0; i < runs; i++)
        {
            var rng = new Rng(seed * 13 + i);
            int kind = rng.Range(0, 4);
            var room = new[] { RoomType.Storage, RoomType.Quarters, RoomType.Mess, RoomType.Hydroponics, RoomType.Workshop }[rng.Range(0, 5)];
            float size = rng.Range(0.9f, 1.2f);
            int hour = rng.Range(0, 24);
            string what = "";
            var line = new List<string>();
            foreach (bool offline in new[] { false, true })
            {
                var w = World.CreateDefault(seed + i);
                long start = SimTime.TicksPerDay + SimTime.Hours(hour) - SimTime.Hours(7);
                while (w.Tick < start) w.Step();
                var before = Snapshot.Take(w);
                if (offline && w.Automation.Computer is Machine comp) w.Machines.Break(comp, FaultKind.StorageFault);
                switch (kind)
                {
                    case 0: Player.Meteor(w, Scenarios.OuterTarget(w, room), size); what = $"큰 운석({RoomTypes.Name(room)})"; break;
                    case 1:
                    {
                        var r = w.Ship.RoomsOf(room).First();
                        foreach (var c in r.Cells.Where(w.Ship.IsOpenFloor).Take(2)) Incidents.Fire(w, c);
                        what = $"화재({RoomTypes.Name(room)})";
                        break;
                    }
                    case 2:
                        Player.Meteor(w, Scenarios.OuterTarget(w, room), size);
                        Player.Meteor(w, Scenarios.OuterTarget(w, RoomType.Cooling), 0.8f);
                        what = $"큰 운석({RoomTypes.Name(room)}) + 운석(냉각실)";
                        break;
                    default:
                    {
                        var r = w.Ship.RoomsOf(room).First();
                        Player.Meteor(w, Scenarios.OuterTarget(w, room), size);
                        var f = w.Ship.RoomsOf(RoomType.Galley).First();
                        foreach (var c in f.Cells.Where(w.Ship.IsOpenFloor).Take(2)) Incidents.Fire(w, c);
                        what = $"큰 운석({RoomTypes.Name(room)}) + 화재(주방)";
                        break;
                    }
                }
                before.RebaseStock(w);
                long end = w.Tick + 2L * SimTime.TicksPerDay;
                while (w.Tick < end) w.Step();
                var outcome = Assessment.Assess(w, before);
                int downs = w.Crew.Sum(c => c.Stats.TimesDown);
                float tank = (before.Reserve - w.Air.Reserve) / w.Air.ReserveCapacity * 100f;
                float manual = w.Log.Entries.Count(e => e.Text.Contains("손으로"));
                var s0 = sums.GetValueOrDefault(offline);
                string grade = outcome.ToString().Split(" — ")[0];
                sums[offline] = (s0.downs + downs, s0.tank + tank,
                    s0.full + (grade.StartsWith("완전") ? 1 : 0), s0.partial + (grade.StartsWith("부분") ? 1 : 0),
                    s0.longTerm + (grade.StartsWith("장기") ? 1 : 0), s0.fail + (grade.StartsWith("실패") ? 1 : 0), s0.manual + manual);
                line.Add($"{(offline ? "꺼짐" : "켜짐")}: {grade} · 쓰러짐 {downs} · 공기 탱크 −{tank:0}% · 손 조작 {manual:0}");
            }
            Console.WriteLine($"  {i + 1,2}. {SimTime.Clock(SimTime.Hours(hour))} {what}\n      {line[0]}\n      {line[1]}");
        }
        Console.WriteLine();
        foreach (bool offline in new[] { false, true })
        {
            var s = sums[offline];
            Console.WriteLine($"  자동화 {(offline ? "꺼짐" : "켜짐")}: 완전 {s.full} · 부분 {s.partial} · 장기 {s.longTerm} · 실패 {s.fail} · 쓰러짐 {s.downs} · 공기 탱크 평균 −{s.tank / runs:0}% · 손 조작 평균 {s.manual / runs:0.0}");
        }
        var on = sums[false]; var off = sums[true];
        bool worse = off.downs + off.fail * 3 + off.longTerm > on.downs + on.fail * 3 + on.longTerm || off.tank > on.tank * 1.1f;
        Console.WriteLine(worse ? "\n✔ 게이트 통과 — 자동화가 꺼진 배가 같은 사고를 더 비싸게 치른다" : "\n✘ 게이트 미달 — 자동화가 꺼져도 차이가 없다");
        return worse ? 0 : 1;
    }
}
