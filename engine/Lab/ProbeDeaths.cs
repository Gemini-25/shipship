using ShipEngine;

namespace Lab;

/// <summary>관찰: 재미 측정과 같은 세계로 돌려 죽음마다 원인 사슬 · 그 앞 15분 주컴퓨터 기록 · 그 사람의 생각. 쓰기: Lab --probe-deaths [배] [시드] [날수]</summary>
public static partial class Program
{
    internal static void ProbeDeaths(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-deaths").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 11;
        double days = rest.Length > 2 ? double.Parse(rest[2]) : 3;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        if (Environment.GetEnvironmentVariable("FUN_STAY") != "1") ShipWorld.Embark(s);
        var st = s.Get<Storyteller>(); st.Enabled = true;
        var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var life = s.Get<Life>();
        string Where(int c) => c < 0 ? "-" : $"({s.Layout.X(c)},{s.Layout.Y(c)}){(m.RoomOf(c) >= 0 ? s.Layout.Rooms[m.RoomOf(c)].Name : "")}";
        long cursor = 0, end = (long)(days * Time.Hours(24));
        double logFrom = double.Parse(Environment.GetEnvironmentVariable("LOG_FROM") ?? "9999"), logTo = double.Parse(Environment.GetEnvironmentVariable("LOG_TO") ?? "9999");
        long lastLogTick = -1;
        for (long t = 0; t < end; t++)
        {
            s.Step();
            foreach (var f in s.Facts.Since(cursor))
            {
                if (f.Kind == FactKind.StorySeed) Console.WriteLine($"  {Time.Clock(f.Tick)} 씨앗 · {s.Causes.Story(f.Cause)}");
                if (f.Kind != FactKind.Died) continue;
                Console.WriteLine($"\n✝ {Time.Clock(f.Tick)} {Where(f.Cell)} · {s.Causes.Story(f.Cause)}");
                foreach (var x in sm.Log.Where(x => x.tick >= f.Tick - Time.Minutes(15))) Console.WriteLine($"    {Time.Clock(x.tick)} {x.text}");
            }
            cursor = s.Facts.Total;
            if (s.Tick >= Time.Hours(logFrom) && s.Tick <= Time.Hours(logTo))
            {
                foreach (var x in sm.Log) if (x.tick > lastLogTick) Console.WriteLine($"    ㄴ {Time.Clock(x.tick)} {x.text}");
                if (sm.Log.Count > 0) lastLogTick = sm.Log[^1].tick;
            }
            if (Environment.GetEnvironmentVariable("TRACE") is string tr && s.Tick >= Time.Hours(double.Parse(Environment.GetEnvironmentVariable("TRACE_FROM") ?? "0")) && s.Tick <= Time.Hours(double.Parse(Environment.GetEnvironmentVariable("TRACE_TO") ?? "0")) && t % int.Parse(Environment.GetEnvironmentVariable("TRACE_EVERY") ?? "10") == 0)
            {
                var a = m.Agents.First(x => m.NameOf(x.Id) == tr);
                var br = a.Beliefs.Where(b => b.Kind is BeliefKind.Breach or BeliefKind.Vacuum && b.Conf > 0.2f).Select(b => $"{b.Kind}@{Where(b.Cell)}:{b.Conf:0.0}");
                Console.WriteLine($"    ▶ {Time.Clock(s.Tick)} {Where(a.C.Cell)} 목표 {a.C.Goal}→{Where(a.C.GoalCell)} 단계 {(a.C.StepIndex < a.Plan.Count ? a.Plan[a.C.StepIndex].Kind.ToString() : "-")}/{a.Plan.Count} 길 {a.Path.Count - a.PathIdx} 문대기 {a.C.WaitDoor} 실패 {a.C.Failed} 피함 {string.Join(",", a.Avoid.Where(v => v.Until > s.Tick).Select(v => v.Goal))} · 믿음 {string.Join(" ", br)} · 그만둠 '{a.LastFail}' · {m.Thought(a.Id)}");
                foreach (int dd in (Environment.GetEnvironmentVariable("TRACE_DOORS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).DefaultIfEmpty(a.C.WaitDoor).Where(x => x >= 0))
                {
                    var stc = s.Get<Structure>(); var fl = s.Get<Field>();
                    Span<int> nb = stackalloc int[4]; s.Layout.Neighbors(s.Layout.Doors[dd].Cell, nb);
                    var sides = new List<string>(); foreach (int j in nb) if (j >= 0 && s.Layout.Kind[j] != CellKind.Wall) sides.Add($"{Where(j)} {fl.Pressure(j):0.0}kPa");
                    Console.WriteLine($"      문{dd} 바깥문 {s.Layout.Doors[dd].External} 목표 {stc.DoorTarget[dd]:0.0} 열림 {fl.DoorOpen[dd]:0.00} 밸브 {fl.DoorBleed[dd]} 전기 {stc.DoorPowered[dd]} 끼임 {stc.DoorJam[dd]} · {string.Join(" / ", sides)} · 봉쇄 {string.Join(",", sm.Incidents.Where(x => x.Active != 0 && x.Sealed != 0).Select(x => s.Layout.Rooms[x.Room].Name))} · 문 칸 사람 {m.Agents.Where(o => o.C.Cell == s.Layout.Doors[dd].Cell).Select(o => m.NameOf(o.Id)).DefaultIfEmpty("-").First()} · 기다림 {string.Join(",", m.Agents.Where(o => o.C.WaitDoor == dd).Select(o => m.NameOf(o.Id)))} · 물건 {m.Loose.Count(x => x.Cell == s.Layout.Doors[dd].Cell)}");
                }
            }
            if (Environment.GetEnvironmentVariable("WATCH_BODY") is string wb && t % Time.Minutes(30) == 0 && m.Agents.FirstOrDefault(x => x.IsPerson && m.NameOf(x.Id) == wb) is { } wa)
            {
                var bb = life.Bodies[wa.C.Body];
                Console.WriteLine($"    ◆ {Time.Clock(s.Tick)} {wb} {Where(wa.C.Cell)} 목표 {wa.C.Goal} · 몸의 물 {bb.Water / (42 * 55.5):P0} · 목마름 {wa.C.Thirst:0.00} · 화상 {bb.Burn:0.00} · 아픔 {bb.Pain:0.00} · 의식 {bb.Conscious} · 잠 {bb.Asleep} · 일어섬 {wa.C.Up} · 돌봄 {bb.Dressed}");
            }
            if (t % Time.Minutes(30) == 0 && s.Tick >= Time.Hours(double.Parse(Environment.GetEnvironmentVariable("WATCH_FROM") ?? "9999")))
            {
                var f = s.Get<Field>(); var net = s.Get<Network>();
                var rooms = s.Layout.Rooms.Where(r => m.Agents.Any(a => a.IsPerson && life.Bodies[a.C.Body].Alive != 0 && m.RoomOf(a.C.Cell) == r.Id))
                    .Select(r => $"{r.Name} O₂ {r.Cells.Average(c => f.Partial(s.Tables.gO2, c)):0.0}kPa CO₂ {r.Cells.Average(c => f.Ppm(s.Tables.gCO2, c)):0}ppm {r.Cells.Average(c => f.Pressure(c)):0}kPa");
                var o2 = net.Devices.Where(d => d.Def.Role is "o2gen" or "scrubber").Select(d => $"{d.Name}{(net.Working(d.Id) ? "○" : "×")}{(net.On[d.Id] == 0 ? "꺼짐" : "")}{(Enumerable.Range(0, 4).Any(p => net.Broken[d.Id * 4 + p] != 0) ? "고장" : "")}");
                Console.WriteLine($"  · {Time.Clock(s.Tick)} 전력 {net.Supplied:0.0}/{net.Demanded:0.0}kW · 예비 공기 {net.AirReserve:0} · {string.Join(" ", o2)} · 물 {net.WaterTank * 0.018:0}L · 배 산소 {net.PpO2:0.0}kPa · 일 몫 {string.Join("/", net.Devices.Where(d => d.Def.Role == "o2gen").Select(d => net.Duty[d.Id].ToString("0.00")))} · 공급 {string.Join("/", net.Devices.Where(d => d.Def.Role == "o2gen").Select(d => net.Supply[d.Id].ToString("0.00")))} · 구멍 {Enumerable.Range(0, s.Layout.N).Count(c => f.Hole[c] > 0)}곳 {Enumerable.Range(0, s.Layout.N).Sum(c => f.Hole[c]) * 1e4:0.0}cm² · 배 기압 {s.Layout.Rooms.Where(r => r.Cells.Length > 0).Average(r => r.Cells.Average(c => f.Pressure(c))):0}kPa · 주컴퓨터 {(sm.Online ? "켜짐" : "멈춤")} · 사건 {string.Join(",", sm.Incidents.Where(x => x.Active != 0).Select(x => x.Kind))}\n      {string.Join(" | ", rooms)}");
                if (Environment.GetEnvironmentVariable("ROBOTS") == "1")
                    foreach (var a in m.Agents.Where(a => !a.IsPerson))
                        Console.WriteLine($"      {m.NameOf(a.Id)} {Where(a.C.Cell)} 목표 {a.C.Goal} 맡음 {a.C.AssignedGoal}{(a.C.AssignedUntil > s.Tick ? "" : "(끝)")} 단계 {(a.C.StepIndex < a.Plan.Count ? a.Plan[a.C.StepIndex].Kind.ToString() : "-")} 든 것 {(a.C.Holding == 255 ? "-" : m.ItemNames[a.C.Holding])} 전지 {a.C.Battery:0.00} 부품 {a.C.Spares} · {m.Thought(a.Id)}");
            }
        }
        Console.WriteLine($"\n사람 {m.Agents.Count(a => a.IsPerson)} · 산 사람 {m.Agents.Count(a => a.IsPerson && life.Bodies[a.C.Body].Alive != 0)} · 죽음 {life.Deaths}");
    }
}
