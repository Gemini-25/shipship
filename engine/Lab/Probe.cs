using ShipEngine;

namespace Lab;

public static partial class Program
{
    /// <summary>결정론 조사: 1코어 · 여러 코어로 같은 세계를 돌려 처음 갈라진 틱과 엔진을 찾는다.</summary>
    internal static void ProbeDeterminism(bool loaded = false)
    {
        bool quiet = Environment.GetEnvironmentVariable("QUIET") == "1";
        Sim Make()
        {
            var s = Environment.GetEnvironmentVariable("CREW") == "1" ? ShipWorld.Crewed("Didim", 9, 6, 3) : ShipWorld.Create("Didim", 9);
            if (Environment.GetEnvironmentVariable("STORY") == "1") { var st = s.Get<Storyteller>(); st.Enabled = true; }
            if (quiet) return s;
            var f = s.Get<Field>();
            var floor = Enumerable.Range(0, f.Cells).Where(i => f.L.Kind[i] == CellKind.Floor && f.L.FurnitureAt[i] >= 0).ToArray();
            s.Get<Chemistry>().Ignite(floor[floor.Length / 2], 3e6, 0);
            int hull = Enumerable.Range(0, f.Cells).First(i => f.L.Hull[i] && f.L.Y(i) == f.L.H / 2);
            s.Get<Structure>().Impact(s, hull, 2e5, 1, 0, 0);
            return s;
        }
        ulong Whole(int th) { Par.Threads = th; var x = Make(); x.Run(400); return x.Hash(); }
        Console.WriteLine($"  따로 돌림: 1코어 {Whole(1):x} · 1코어 {Whole(1):x} · 8코어 {Whole(8):x} · 8코어 {Whole(8):x}");
        var a = Make(); var b = Make();
        if (loaded)
        {
            a.Run(10); b = new Sim(a.Setup); b.Load(a.Save()); Console.WriteLine("  원래 세계 ↔ 틱 10에서 되살린 세계");
            var r3 = a.Layout.Rooms[3].Cells;
            Console.WriteLine($"  되살린 직후 방3 압력 합 {r3.Sum(c => a.Get<Field>().Pressure(c)):0.000000} / {r3.Sum(c => b.Get<Field>().Pressure(c)):0.000000} · 그을음 {r3.Max(c => a.Get<Field>().Ppm(a.Tables.gSoot, c)):0.000000} / {r3.Max(c => b.Get<Field>().Ppm(b.Tables.gSoot, c)):0.000000}");
            foreach (var e in a.Engines) Console.Write($"{e.Name}({e.Phase}) ");
            Console.WriteLine();
        }
        for (int t = 0; t < 400; t++)
        {
            Par.Threads = 1; a.Step();
            Par.Threads = loaded ? 1 : 8; b.Step();
            if (!a.Save().AsSpan().SequenceEqual(b.Save()) && a.Engines.All(e => { var wa = new StateWriter(); e.Save(wa); var wb = new StateWriter(); b.Engines.First(x => x.Name == e.Name).Save(wb); return wa.ToArray().AsSpan().SequenceEqual(wb.ToArray()); }))
            { Console.WriteLine($"  틱 {a.Tick - 1}에 원인 · 사실 기록이 갈라졌다 (원인 {a.Causes.Count}/{b.Causes.Count} · 사실 {a.Facts.Total}/{b.Facts.Total})"); return; }
            foreach (var e in a.Engines)
            {
                var wa = new StateWriter(); e.Save(wa);
                var eb = b.Engines.First(x => x.Name == e.Name);
                var wb = new StateWriter(); eb.Save(wb);
                if (!wa.ToArray().AsSpan().SequenceEqual(wb.ToArray()))
                {
                    Console.WriteLine($"  틱 {a.Tick - 1}에 '{e.Name}'이(가) 갈라졌다 · 함께 갈라진 엔진: {string.Join(", ", a.Engines.Where(x => { var p = new StateWriter(); x.Save(p); var q = new StateWriter(); b.Engines.First(y => y.Name == x.Name).Save(q); return !p.ToArray().AsSpan().SequenceEqual(q.ToArray()); }).Select(x => x.Name))}");
                    if (e is ShipMind ma && eb is ShipMind mb)
                    {
                        Console.WriteLine($"   사건 {ma.Incidents.Count}/{mb.Incidents.Count} · 기록 {ma.Log.Count}/{mb.Log.Count} · 켜짐 {ma.Online}/{mb.Online} · 경보 {ma.Alarms}/{mb.Alarms} · 끔 {ma.Sheds}/{mb.Sheds} · 상상 {ma.Imaginations.Count}/{mb.Imaginations.Count}");
                        foreach (var x in ma.Log.Except(mb.Log)) Console.WriteLine($"   원래만: {x}");
                        foreach (var x in mb.Log.Except(ma.Log)) Console.WriteLine($"   되살린 쪽만: {x}");
                        string D(string n, float[] x, float[] y) { for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return $"{n}[{i}] {x[i]} / {y[i]}"; return ""; }
                        Console.WriteLine("   " + string.Join(" · ", new[] { D("KPa", ma.RoomKPa, mb.RoomKPa), D("O2", ma.RoomO2, mb.RoomO2), D("Soot", ma.RoomSoot, mb.RoomSoot), D("CO2", ma.RoomCO2, mb.RoomCO2), D("T", ma.RoomTemp, mb.RoomTemp), D("Moles", ma.RoomMoles, mb.RoomMoles) }.Where(x => x.Length > 0)));
                        Console.WriteLine($"   방3 읽힘 {ma.RoomSeen[3]}/{mb.RoomSeen[3]} · 이전 {ma.RoomKPaPrev[3]}/{mb.RoomKPaPrev[3]} · 장 압력 지금 {a.Get<Field>().Pressure(a.Layout.Rooms[3].Cells[0]):0.00}/{b.Get<Field>().Pressure(b.Layout.Rooms[3].Cells[0]):0.00} · 방3 '{a.Layout.Rooms[3].Name}' 칸 {a.Layout.Rooms[3].Cells.Length}");
                        for (int i = 0; i < Math.Min(ma.Incidents.Count, mb.Incidents.Count); i++) if (!ma.Incidents[i].Equals(mb.Incidents[i])) Console.WriteLine($"   사건 {i}: {ma.Incidents[i].Kind} 방 {ma.Incidents[i].Room} 끝 {ma.Incidents[i].Ended}/{mb.Incidents[i].Ended} 경보 {ma.Incidents[i].LastAlarm}/{mb.Incidents[i].LastAlarm} 로봇 {ma.Incidents[i].RobotA}/{mb.Incidents[i].RobotA} 맑음 {ma.Incidents[i].Clear}/{mb.Incidents[i].Clear}");
                    }
                    if (e is Field fa && eb is Field fb)
                    {
                        string Diff(string n, double[] x, double[] y) { for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return $"{n}[{i}] {x[i]:R} / {y[i]:R}"; return ""; }
                        Console.WriteLine("   " + string.Join(" · ", new[] { Diff("N", fa.N, fb.N), Diff("U", fa.U, fb.U), Diff("Ts", fa.Ts, fb.Ts), Diff("Tf", fa.Tf, fb.Tf), Diff("Store", fa.Store, fb.Store) }.Where(x => x.Length > 0)));
                    }
                    return;
                }
            }
        }
        Console.WriteLine("  400틱 내내 같다");
    }

    /// <summary>승무원 하루: 2시간마다 사람마다 목표 · 욕구 · 생각 한 줄 (--probe-crew [배] [시간] [사람] [로봇]).</summary>
    internal static void ProbeCrew(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-crew").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        double hours = rest.Length > 1 ? double.Parse(rest[1]) : 24;
        int people = rest.Length > 2 ? int.Parse(rest[2]) : 6, robots = rest.Length > 3 ? int.Parse(rest[3]) : 2;
        var s = ShipWorld.Crewed(ship, 3, people, robots, 0);
        var m = s.Get<Mind>(); var life = s.Get<Life>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double mindMs = 0;
        s.Measure = true;
        for (long t = 0; t <= Time.Hours(hours); t++)
        {
            if (t % Time.Hours(2) == 0)
            {
                Console.WriteLine($"  {Time.Clock(s.Tick)} · 식사 {m.Meals} · 물 {m.Drinks} · 외침 {m.Shouts}");
                foreach (var a in m.Agents)
                {
                    string body = a.IsPerson ? $"배고픔 {a.C.Hunger:0.00} 목마름 {a.C.Thirst:0.00} 졸림 {a.C.Sleepy:0.00} 외로움 {a.C.Lonely:0.00} 심심 {a.C.Bored:0.00} 긴장 {a.C.Stress:0.00} 자는 중 {life[a.C.Body].Asleep}" : $"전지 {a.C.Battery:0.00}";
                    Console.WriteLine($"    {m.NameOf(a.Id),-6} 칸 {a.C.Cell,5} 방 {(m.RoomOf(a.C.Cell) >= 0 ? s.Layout.Rooms[m.RoomOf(a.C.Cell)].Name : "-"),-8} {m.Thought(a.Id),-22} {body} · 믿음 {a.Beliefs.Count} · 실패 {a.C.Failed}");
                }
            }
            s.Step();
        }
        mindMs = s.LastMs.GetValueOrDefault("마음");
        Console.WriteLine($"  {sw.Elapsed.TotalSeconds:0.0}초 · 마음 1틱 평균 {mindMs / Time.Hours(hours):0.000}ms");
        var watch = Environment.GetEnvironmentVariable("WATCH");
        if (watch != null)
        {
            int wc = int.Parse(watch);
            var ff = s.Get<Field>();
            Span<int> nb = stackalloc int[4];
            s.Layout.Neighbors(wc, nb);
            foreach (int j in nb)
                if (j >= 0) Console.WriteLine($"    옆 {j} {s.Layout.Kind[j]} 방 {(s.Layout.Room[j] >= 0 ? s.Layout.Rooms[s.Layout.Room[j]].Name : "-")} {ff.Pressure(j):0.0}kPa {ff.Temp(j):0}K 구역 {(j < ff.RegionOf.Length ? ff.RegionOf[j] : -9)}");
        }
        if (Environment.GetEnvironmentVariable("FACTS") == "1")
            foreach (var f in s.Facts.Since(0))
                if (f.Kind is FactKind.WallCrack or FactKind.WallBreach or FactKind.DoorBlown or FactKind.Patched or FactKind.Ignition or FactKind.MachineFail or FactKind.Noticed)
                    Console.WriteLine($"    {Time.Clock(f.Tick)} {f.Kind} 칸 {f.Cell} 크기 {f.Size:0.####} 누가 {f.Actor} 무엇 {f.Other} · {s.Causes.Story(f.Cause)}");
    }
}

public static partial class Program
{
    /// <summary>이야기꾼을 켠 배 하루: 30분마다 씨앗 · 방 산소 최저 · 고장 · 주컴퓨터 사건 · 큰 사실 (--probe-story [배] [시드] [시간]).</summary>
    internal static void ProbeStory(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-story").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 18;
        double hours = rest.Length > 2 ? double.Parse(rest[2]) : 24;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        var st = s.Get<Storyteller>(); st.Enabled = true;
        var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>(); var sm = s.Get<ShipMind>();
        long cursor = 0;
        for (long t = 0; t <= Time.Hours(hours); t++)
        {
            if (t % Time.Minutes(30) == 0)
            {
                var rooms = s.Layout.Rooms.Where(r => r.Cells.Length > 0).Select(r => (r.Name, o2: r.Cells.Average(c => f.Partial(s.Tables.gO2, c)), co2: r.Cells.Average(c => f.Ppm(s.Tables.gCO2, c)), p: r.Cells.Average(c => f.Pressure(c)))).ToList();
                var low = rooms.OrderBy(r => r.o2).First();
                var broken = net.Devices.Where(d => Enum.GetValues<Part>().Any(p => net.IsBroken(d.Id, p))).Select(d => d.Name).ToList();
                Console.WriteLine($"  {Time.Clock(s.Tick)} · 씨앗 {string.Join(",", st.Seeds.Select(x => Storyteller.SeedName(x.Kind)))} · 산소 최저 {low.Name} {low.o2:0.0}kPa · 평균 산소 {rooms.Average(r => r.o2):0.0} CO2 {rooms.Average(r => r.co2):0}ppm 기압 {rooms.Average(r => r.p):0} · 고장 [{string.Join(",", broken)}] · 전력 {net.Supplied:0}/{net.Demanded:0}kW · 주컴 {(sm.Online ? "켜짐" : "꺼짐")} 사건 {sm.Incidents.Count(x => x.Active != 0)}");
                if (Environment.GetEnvironmentVariable("HOLES") == "1")
                {
                    for (int c = 0; c < f.Cells; c++)
                        if (f.Hole[c] > 0) { int rr = -1; Span<int> nb = stackalloc int[4]; s.Layout.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && s.Layout.Room[j] >= 0) rr = s.Layout.Room[j]; Console.WriteLine($"      구멍 칸 {c} ({s.Layout.X(c)},{s.Layout.Y(c)}) {f.Hole[c] * 1e4:0}cm² 곁방 {(rr >= 0 ? s.Layout.Rooms[rr].Name : "-")} 바깥면 {s.Layout.Hull[c]}"); }
                    string RoomAt(int c) => c < 0 ? "바깥" : s.Layout.Room[c] >= 0 ? s.Layout.Rooms[s.Layout.Room[c]].Name : $"칸{c}";
                    foreach (var k in f.Links.Where(k => Math.Abs(k.Flow) > 0.05).OrderByDescending(k => Math.Abs(k.Flow)).Take(8))
                        Console.WriteLine($"      틈 {k.Kind} 칸 {k.Via} ({s.Layout.X(k.Via)},{s.Layout.Y(k.Via)}) {RoomAt(k.A)} → {RoomAt(k.B)} {k.Area * 1e4:0}cm² 흐름 {k.Flow:0.00}몰/초");
                    foreach (var a in m.Agents.Where(a => !a.IsPerson))
                        Console.WriteLine($"      {m.NameOf(a.Id),-6} {(m.RoomOf(a.C.Cell) >= 0 ? s.Layout.Rooms[m.RoomOf(a.C.Cell)].Name : "-"),-8} {m.Thought(a.Id),-24} 전지 {a.C.Battery:0.00} 맡음 {a.C.AssignedGoal}:{a.C.AssignedTarget} · 믿음 {string.Join(",", a.Beliefs.Where(x => x.Kind is BeliefKind.Leak or BeliefKind.Breach or BeliefKind.Vacuum).Select(x => $"{x.Kind}@{x.Cell}:{x.Conf:0.0}"))} · 실패 {a.C.Failed}");
                }
                foreach (var a in m.Agents.Where(a => a.IsPerson))
                {
                    var b = life.Bodies[a.C.Body];
                    if (b.Alive == 0) continue;
                    Console.WriteLine($"      {m.NameOf(a.Id),-6} {(m.RoomOf(a.C.Cell) >= 0 ? s.Layout.Rooms[m.RoomOf(a.C.Cell)].Name : "-"),-8} {m.Thought(a.Id),-20} 산소포화 {b.SpO2:0.00} 의식 {b.Conscious}");
                }
            }
            s.Step();
            foreach (var x in s.Facts.Since(cursor))
                if (Director.Weight(x.Kind) >= 10 || x.Kind is FactKind.StorySeed or FactKind.MachineFail or FactKind.LoadShed or FactKind.BreakerTrip)
                    Console.WriteLine($"    {Time.Clock(x.Tick)} [{FactNames.Of(x.Kind)}] 칸 {x.Cell} · {s.Causes.Story(x.Cause)}");
            cursor = s.Facts.Total;
        }
        foreach (var (tick, text) in sm.Log.TakeLast(30)) Console.WriteLine($"    주컴 {Time.Clock(tick)} {text}");
    }

    /// <summary>이야기꾼을 켠 배에서 로봇 하나를 틱마다 따라간다 (--probe-robot [배] [시드] [몇 시간 뒤부터] [로봇 번호] [틱 수]).</summary>
    internal static void ProbeRobot(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-robot").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Hwasal";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 18;
        double hours = rest.Length > 2 ? double.Parse(rest[2]) : 8;
        int which = rest.Length > 3 ? int.Parse(rest[3]) : 0;
        int ticks = rest.Length > 4 ? int.Parse(rest[4]) : 200;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        s.Get<Storyteller>().Enabled = true;
        var m = s.Get<Mind>(); var f = s.Get<Field>(); var stx = s.Get<Structure>();
        s.Run((long)Time.Hours(hours));
        var robots = m.Agents.Where(a => !a.IsPerson).ToList();
        var r = robots[Math.Min(which, robots.Count - 1)];
        var sm = s.Get<ShipMind>();
        foreach (var x in sm.Incidents.Where(x => x.Active != 0))
            Console.WriteLine($"  사건 {x.Kind} {(x.Room >= 0 ? s.Layout.Rooms[x.Room].Name : "-")} 수준 {x.Level:0.0} 로봇 {x.RobotA}/{x.RobotB} 보냄 {x.Tries} 봉쇄 {x.Sealed}");
        foreach (var a in robots) Console.WriteLine($"  {m.NameOf(a.Id)}#{a.Id} 맡음 {a.C.AssignedGoal}:{a.C.AssignedTarget} 기한 {(a.C.AssignedUntil - s.Tick) / 25.0:0}분 · 전지 {a.C.Battery:0.00} · {m.Thought(a.Id)}");
        string Where(int c) => c < 0 ? "-" : $"({s.Layout.X(c)},{s.Layout.Y(c)}){(s.Layout.Kind[c] == CellKind.Outside ? "바깥" : m.RoomOf(c) >= 0 ? s.Layout.Rooms[m.RoomOf(c)].Name : "")}";
        for (int k = 0; k < ticks; k++)
        {
            s.Step();
            var c = r.C;
            string plan = string.Join(">", r.Plan.Select((p, i) => (i == c.StepIndex ? "*" : "") + p.Kind + (p.Cell >= 0 ? Where(p.Cell) : "")));
            string door = c.WaitDoor >= 0 ? $" 문{c.WaitDoor}({s.Layout.Doors[c.WaitDoor].Kind}{(s.Layout.Doors[c.WaitDoor].External ? " 바깥" : "")}) 기다림 {c.WaitTicks} 밸브 {f.DoorBleed[c.WaitDoor]} 열림 {f.DoorOpen[c.WaitDoor]:0.0} 목표 {stx.DoorTarget[c.WaitDoor]:0.0} 끼임 {stx.DoorJam[c.WaitDoor]} 전원 {stx.DoorPowered[c.WaitDoor]} 안전밸브 {stx.SafeToBleed(c.WaitDoor)} 여기 {f.Pressure(c.Cell):0}kPa · 방 문 [{string.Join(",", (m.RoomOf(c.Cell) >= 0 ? stx.RoomDoors(m.RoomOf(c.Cell)) : new List<int>()).Select(d => $"{d}:{f.DoorOpen[d]:0.0}/{stx.DoorTarget[d]:0.0}"))}]" : "";
            Console.WriteLine($"  {Time.Clock(s.Tick)} {m.NameOf(r.Id)}#{r.Id} {Where(c.Cell)} {m.Thought(r.Id)} · 맡음 {c.AssignedGoal}:{Where(c.AssignedTarget)} · 길 {r.PathIdx}/{r.Path.Count}{door} · 계획 {plan} · 피함 {string.Join(",", r.Avoid.Select(v => v.Goal + ":" + v.Target))}");
        }
    }

    /// <summary>방 온도 · 냉각 장부를 시간마다 (--probe-heat [배] [시간] [사람 수]).</summary>
    internal static void ProbeHeat(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-heat").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Hwasal";
        double hours = rest.Length > 1 ? double.Parse(rest[1]) : 12;
        int crew = rest.Length > 2 ? int.Parse(rest[2]) : 0;
        var lay = Layout.Ship(ship, Tables.Default);
        int docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = crew > 0 ? ShipWorld.Crewed(ship, 3, crew, docks, 0) : ShipWorld.Create(ship, 3);
        var f = s.Get<Field>(); var net = s.Get<Network>();
        Console.WriteLine($"  장치: " + string.Join(" · ", net.Devices.GroupBy(d => d.Type).Select(g => $"{g.Key}×{g.Count()}")));
        for (int h = 0; h <= hours; h++)
        {
            if (h > 0) s.Run(Time.Hours(1));
            var rooms = s.Layout.Rooms.Where(r => r.Cells.Length > 0).Select(r => (r.Name, t: r.Cells.Average(c => f.Temp(c)) - 273.15, kw: net.Devices.Where(d => d.Room == r.Id && net.Working(d.Id)).Sum(d => d.Def.Kw)));
            Console.WriteLine($"  {h}시간 · 냉각 {net.CoolingKw:0.0}kW 공조 {net.HvacKw:0.0}kW · 전력 {net.Supplied:0}/{net.Demanded:0}kW · " + string.Join(" · ", rooms.OrderByDescending(x => x.t).Take(6).Select(x => $"{x.Name} {x.t:0}℃({x.kw:0.0}kW)")));
            // 가장 더운 칸 (방마다 · 위 다섯) — 그 칸의 설비
            var hot = s.Layout.Rooms.Where(r => r.Cells.Length > 0).Select(r => (r.Name, c: r.Cells.MaxBy(c => f.Temp(c)))).OrderByDescending(x => f.Temp(x.c)).Take(5);
            Console.WriteLine("    더운 칸: " + string.Join(" · ", hot.Select(x => $"{x.Name} {f.Temp(x.c) - 273.15:0}℃ ({(s.Layout.FurnitureAt[x.c] >= 0 ? s.Layout.Furniture[s.Layout.FurnitureAt[x.c]].Type : "바닥")})")));
        }
    }
}

public static partial class Program
{
    /// <summary>
    /// 사람이 위험에 얼마나 가까이 가는지 (--probe-danger [배,배…] [날] [시드 수]).
    /// 사람마다 가장 낮은 산소 포화 · 가장 높은 산소 부족 · 일산화탄소 · 쓰러짐을, 위험한 일(벽이 뚫림 · 불 · 연기 · 독 · 공기가 빠짐)마다 그 방에 있던 사람 수와 30분 안 최저 포화를 센다.
    /// </summary>
    internal static void ProbeDanger(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-danger").Skip(1).ToArray();
        var ships = (rest.Length > 0 ? rest[0] : "Didim,Hwasal,Jebi,Gorae").Split(',');
        double days = rest.Length > 1 ? double.Parse(rest[1]) : 2;
        int seeds = rest.Length > 2 ? int.Parse(rest[2]) : 2;
        var kinds = new[] { FactKind.WallBreach, FactKind.Ignition, FactKind.FireSpread, FactKind.SmokeThick, FactKind.Toxic, FactKind.RegionVented, FactKind.PressureDrop, FactKind.DoorBlown, FactKind.Flood };
        foreach (var ship in ships)
            for (int k = 0; k < seeds; k++)
            {
                ulong seed = (ulong)(18 + k * 7);
                var lay = Layout.Ship(ship, Tables.Default);
                int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
                var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
                var st = s.Get<Storyteller>(); st.Enabled = true;
                var m = s.Get<Mind>(); var life = s.Get<Life>();
                var people = m.Agents.Where(a => a.IsPerson).ToList();
                var minSat = people.ToDictionary(a => a.Id, a => 1f);
                var maxHyp = people.ToDictionary(a => a.Id, a => 0f);
                var maxCo = people.ToDictionary(a => a.Id, a => 0f);
                var watch = new List<(long tick, FactKind kind, int room, int inRoom, HashSet<int> who, float min, string text)>();
                long cursor = 0, end = (long)(days * Time.Hours(24));
                for (long t = 0; t < end; t++)
                {
                    s.Step();
                    foreach (var f in s.Facts.Since(cursor))
                    {
                        if (!kinds.Contains(f.Kind) || f.Cell < 0) continue;
                        int room = m.RoomOf(f.Cell);
                        if (room < 0) { Span<int> nb = stackalloc int[4]; s.Layout.Neighbors(f.Cell, nb); foreach (int j in nb) if (j >= 0 && m.RoomOf(j) >= 0) room = m.RoomOf(j); }
                        if (room < 0 || watch.Any(w => w.room == room && w.kind == f.Kind && f.Tick - w.tick < Time.Minutes(30))) continue;
                        var who = people.Where(a => life.Bodies[a.C.Body].Alive != 0 && m.RoomOf(life.Bodies[a.C.Body].Cell) == room).Select(a => a.Id).ToHashSet();
                        watch.Add((f.Tick, f.Kind, room, who.Count, who, 1f, s.Causes.Story(f.Cause)));
                    }
                    cursor = s.Facts.Total;
                    foreach (var a in people)
                    {
                        var b = life.Bodies[a.C.Body];
                        if (b.Alive == 0) continue;
                        minSat[a.Id] = Math.Min(minSat[a.Id], b.SpO2); maxHyp[a.Id] = Math.Max(maxHyp[a.Id], b.Hypoxia); maxCo[a.Id] = Math.Max(maxCo[a.Id], b.COHb);
                    }
                    for (int i = 0; i < watch.Count; i++)
                    {
                        var w = watch[i];
                        if (t - w.tick > Time.Minutes(30) || w.who.Count == 0) continue;
                        foreach (int id in w.who) { var b = life.Bodies[m.Agents[id].C.Body]; if (b.Alive != 0) w.min = Math.Min(w.min, b.SpO2); }
                        watch[i] = w;
                    }
                }
                var d = s.Get<Life>();
                Console.WriteLine($"■ {ship} 시드 {seed} · 사람 {people.Count} · 씨앗 {string.Join(",", st.Seeds.Select(x => Storyteller.SeedName(x.Kind)))} · 죽음 {d.Deaths}");
                Console.WriteLine($"  사람 최저 포화 {string.Join(" ", people.Select(a => $"{m.NameOf(a.Id)}:{minSat[a.Id]:0.00}"))}");
                Console.WriteLine($"  최고 산소 부족 {people.Max(a => maxHyp[a.Id]):0.00} · 최고 일산화탄소 {people.Max(a => maxCo[a.Id]):0.00} · 포화 0.9 아래로 간 사람 {people.Count(a => minSat[a.Id] < 0.9f)}");
                foreach (var w in watch)
                    Console.WriteLine($"    {Time.Clock(w.tick)} [{FactNames.Of(w.kind)}] {s.Layout.Rooms[w.room].Name} · 있던 사람 {w.inRoom}{(w.inRoom > 0 ? $" · 30분 최저 포화 {w.min:0.00}" : "")} · {w.text}");
            }
    }
}

public static partial class Program
{
    /// <summary>사람을 틱마다 따라간다 — 칸의 독 · 폐 · 포화 · 생각 · 믿음 (--probe-person [배] [시드] [몇 시간 뒤부터] [틱 수] [몇 틱마다]).</summary>
    /// <summary>하루 살피기: 사람들이 시간을 어디에 쓰나 (목표별 몫) · 쉬는 시간에 한 일 · 지루함 · 졸림 (⑧ 일상).</summary>
    internal static void ProbeDaily(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-daily").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 11;
        double hours = rest.Length > 2 ? double.Parse(rest[2]) : 24;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        var m = s.Get<Mind>();
        var share = new Dictionary<GoalKind, long>();
        double bored = 0, sleepy = 0; long n = 0;
        var prev = new Dictionary<int, (GoalKind g, int step, int plan, long since, int cell)>();
        int shown = 0;
        for (long k = 0; k < Time.Hours(hours); k++)
        {
            s.Step();
            foreach (var a in m.Agents.Where(a => a.IsPerson))
            {
                var now = (a.C.Goal, a.C.StepIndex, a.Plan.Count, a.C.GoalSince, a.C.Cell);
                if (prev.TryGetValue(a.Id, out var p) && p.g == GoalKind.Relax && (now.Goal != GoalKind.Relax || now.GoalSince != p.since) && Environment.GetEnvironmentVariable("PROBE_RELAX") == "1" && shown++ < 25)
                    Console.WriteLine($"    {Time.Clock(s.Tick)} {m.NameOf(a.Id)} 쉼({Mind.HobbyName((Hobby)a.C.Hobby)}) {Time.ToSeconds(s.Tick - p.since) / 60:0}분 · 걸음 {p.step}/{p.plan} → {m.Thought(a.Id)}");
                prev[a.Id] = now;
            }
            if (k % 25 != 0) continue;
            foreach (var a in m.Agents.Where(a => a.IsPerson && s.Get<Life>()[a.C.Body].Alive != 0)) // 자는 이(누워 있다)도 센다
            {
                share[a.C.Goal] = share.GetValueOrDefault(a.C.Goal) + 1;
                bored += a.C.Bored; sleepy += a.C.Sleepy; n++;
            }
        }
        long total = share.Values.Sum();
        Console.WriteLine($"{ship} 시드 {seed} · {hours}시간 · 사람 {m.Agents.Count(a => a.IsPerson)}");
        Console.WriteLine("  시간을 쓴 곳: " + string.Join(" · ", share.OrderByDescending(kv => kv.Value).Select(kv => $"{Mind.GoalName(kv.Key)} {kv.Value * 100.0 / total:0}%")));
        Console.WriteLine("  쉬는 시간: " + string.Join(" · ", Enumerable.Range(0, m.HobbyCount.Length).Select(h => $"{Mind.HobbyName((Hobby)h)} {m.HobbyCount[h]}")));
        Console.WriteLine($"  지루함 평균 {bored / Math.Max(1, n):0.00} · 졸림 평균 {sleepy / Math.Max(1, n):0.00} · 밥 짓기 {m.Cooked}");
        var so = s.Get<Society>();
        Console.WriteLine($"  함께: 같이 먹음 {m.AteTogether} · 따라 나감 {m.Followed} · 무리 {so.Bands.Count} ({string.Join(" ", so.Bands.Select(b => $"{so.BandName(b.Id)} {b.Size}명"))}) · 짝 {so.Couples} · 몸싸움 {so.Fights}");
    }

    internal static void ProbePerson(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-person").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Hwasal";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 25;
        double hours = rest.Length > 2 ? double.Parse(rest[2]) : 10;
        int ticks = rest.Length > 3 ? int.Parse(rest[3]) : 600;
        int every = rest.Length > 4 ? int.Parse(rest[4]) : 5;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        s.Get<Storyteller>().Enabled = true;
        var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
        s.Run((long)Time.Hours(hours));
        string Where(int c) => c < 0 ? "-" : $"({s.Layout.X(c)},{s.Layout.Y(c)}){(m.RoomOf(c) >= 0 ? s.Layout.Rooms[m.RoomOf(c)].Name : "")}";
        long cursor = s.Facts.Total;
        for (int k = 0; k < ticks; k++)
        {
            s.Step();
            foreach (var x in s.Facts.Since(cursor))
                if (Director.Weight(x.Kind) >= 3 || x.Kind is FactKind.ComputerAlarm or FactKind.Toxic or FactKind.Fled or FactKind.Warned)
                    Console.WriteLine($"    {Time.Clock(x.Tick)} [{FactNames.Of(x.Kind)}] {Where(x.Cell)} · {s.Causes.Story(x.Cause)}");
            cursor = s.Facts.Total;
            if (k % every != 0) continue;
            var rooms = s.Layout.Rooms.Select((r, i) => (r.Name, ppm: r.Cells.Length > 0 ? r.Cells.Max(c => f.Ppm(s.Tables.gNH3, c)) : 0)).Where(r => r.ppm > 25).Select(r => $"{r.Name} {r.ppm:0}");
            Console.WriteLine($"  {Time.Clock(s.Tick)} 암모니아 최고: {string.Join(" · ", rooms)} · 사건 {string.Join(",", sm.Incidents.Where(x => x.Active != 0).Select(x => $"{x.Kind}@{(x.Room >= 0 ? s.Layout.Rooms[x.Room].Name : "-")}{(x.Sealed != 0 ? "(봉쇄)" : "")}"))}");
            foreach (var a in m.Agents.Where(a => a.IsPerson))
            {
                var b = life.Bodies[a.C.Body];
                if (b.Alive == 0) continue;
                Console.WriteLine($"    {m.NameOf(a.Id),-5} {Where(b.Cell),-16} {m.Thought(a.Id),-22} 암모니아 {f.Ppm(s.Tables.gNH3, b.Cell):0} · 폐 {b.LungDamage:0.00} · 포화 {b.SpO2:0.00} · 의식 {b.Conscious} 잠 {b.Asleep} · 믿음 {string.Join(",", a.Beliefs.Where(x => x.Kind is BeliefKind.BadAir).Select(x => $"{x.Kind}@{Where(x.Cell)}:{x.Conf:0.0}"))}");
            }
        }
    }
}

public static partial class Program
{
    /// <summary>주컴퓨터 일지 · 정화 장부 (--probe-log [배] [시드] [시간]).</summary>
    private static float _probeDoor = -1, _probeTarget = -1;
    internal static void ProbeLog(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-log").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Hwasal";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 25;
        double hours = rest.Length > 2 ? double.Parse(rest[2]) : 12;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        s.Get<Storyteller>().Enabled = true;
        var sm = s.Get<ShipMind>(); var net = s.Get<Network>();
        for (long t = 0; t < Time.Hours(hours); t++)
        {
            s.Step();
            if (rest.Contains("doors") && s.Tick >= Time.Hours(2.55) && s.Tick <= Time.Hours(2.62) && s.Tick % 5 == 0)
            {
                var fld = s.Get<Field>(); var L2 = s.Layout;
                foreach (var nm in new[] { "에어락", "탐사 장비고" })
                {
                    var room = L2.Rooms.First(r => r.Name == nm);
                    var ds = new List<string>();
                    Span<int> nb2 = stackalloc int[4];
                    foreach (var d in L2.Doors)
                    {
                        L2.Neighbors(d.Cell, nb2);
                        var rs = new List<string>(); bool mine = false;
                        foreach (int j in nb2) if (j >= 0 && L2.Room[j] >= 0) { if (L2.Room[j] == room.Id) mine = true; else rs.Add(L2.Rooms[L2.Room[j]].Name); }
                        if (mine) ds.Add($"문{d.Id}→{string.Join("/", rs.Distinct())} 열림 {fld.DoorOpen[d.Id]:0.0} 틈 {fld.DoorGapOpen(d.Id)} {d.Kind}");
                    }
                    Console.WriteLine($"    {Time.Clock(s.Tick)} {nm} {sm.RoomKPa[room.Id]:0.00}kPa 몰 {room.Cells.Sum(c => fld.Moles(c)):0.0} 온도 {sm.RoomAirT[room.Id]:0.0} · {string.Join(" · ", ds)}");
                }
            }
            double fFrom = double.Parse(Environment.GetEnvironmentVariable("FROM") ?? "13.48"), fTo = double.Parse(Environment.GetEnvironmentVariable("TO") ?? "13.7");
            if (rest.Contains("facts") && s.Tick >= Time.Hours(fFrom) && s.Tick <= Time.Hours(fTo))
            {
                foreach (var f in s.Facts.Recent(64).Where(x => x.Tick == s.Tick - 1 && x.Kind is not (FactKind.Talk or FactKind.Noticed or FactKind.Woke)))
                    Console.WriteLine($"    사실 {Time.Clock(f.Tick)} {f.Kind} 칸 {f.Cell}({(f.Cell >= 0 ? s.Layout.X(f.Cell) + "," + s.Layout.Y(f.Cell) : "-")}) 크기 {f.Size:0.###} · {s.Causes.Story(f.Cause)}");
                {
                    var mm = s.Get<Mind>();
                    var who = mm.Agents.FirstOrDefault(x => mm.NameOf(x.Id) == "정지우");
                    if (who != null) Console.WriteLine($"    정지우 {Time.Clock(s.Tick)} 칸 {s.Layout.X(who.C.Cell)},{s.Layout.Y(who.C.Cell)} 목표 {who.C.Goal} 방식 {who.C.Mode} 세게닫기 {who.C.CloseHard} 연문 {who.C.OpenedDoor} · {mm.Thought(who.Id)} · 길 {who.Path.Count - who.PathIdx}");
                }
                if (s.Tick % 5 == 0)
                {
                    var fld = s.Get<Field>();
                    var holes = Enumerable.Range(0, s.Layout.N).Where(c => fld.Hole[c] > 0).Select(c => $"{s.Layout.X(c)},{s.Layout.Y(c)}:{fld.Hole[c]:0.####}");
                    var rooms = new[] { "원자로실", "둘레 통로", "재배실" }.Select(n => s.Layout.Rooms.FirstOrDefault(r => r.Name == n)).Where(r => r != null).Select(r => r!).Select(r => $"{r.Name} {sm.RoomKPa[r.Id]:0.00}kPa 몰 {r.Cells.Sum(c => fld.Moles(c)):0.0}");
                    Console.WriteLine($"    {Time.Clock(s.Tick)} 구멍 [{string.Join(" ", holes)}] · {string.Join(" · ", rooms)} · 열린 문 {Enumerable.Range(0, s.Layout.Doors.Count).Count(d => fld.DoorGapOpen(d))}");
                }
            }
            if (rest.Contains("door11"))
            {
                var fld = s.Get<Field>(); int dId = int.Parse(Environment.GetEnvironmentVariable("DOOR") ?? "11");
                var stt = s.Get<Structure>();
                if (stt.DoorTarget[dId] != _probeTarget) { var mm = s.Get<Mind>(); Console.WriteLine($"  {Time.Clock(s.Tick)} 문{dId} 목표 {_probeTarget:0.0} → {stt.DoorTarget[dId]:0.0} · 기다리는 이 {string.Join(", ", mm.Agents.Where(a => a.C.WaitDoor == dId || a.C.OpenedDoor == dId).Select(a => $"{mm.NameOf(a.Id)}({mm.Thought(a.Id)} · {a.C.Goal} · 칸 {s.Layout.X(a.C.Cell)},{s.Layout.Y(a.C.Cell)})"))}"); _probeTarget = stt.DoorTarget[dId]; }
                float now = fld.DoorOpen[dId];
                if (now != _probeDoor) { var mm = s.Get<Mind>(); int dc = s.Layout.Doors[dId].Cell; var near = mm.Agents.Where(a => mm.Dist(a.C.Cell, dc) <= 3).Select(a => $"{mm.NameOf(a.Id)}({mm.Thought(a.Id)} · {a.C.Goal})"); Console.WriteLine($"  {Time.Clock(s.Tick)} 문{dId} {_probeDoor:0.0} → {now:0.0} · 곁 {string.Join(", ", near)}"); _probeDoor = now; }
            }
            if (rest.Contains("hull") && t % Time.Minutes(30) == 0)
            {
                // 배 공기: 전체 몰 · 구멍 · 틈이 새는 문 · 열린 바깥문 · 예비 공기
                var fld = s.Get<Field>(); var L2 = s.Layout;
                double mol = 0; foreach (var r in L2.Rooms) foreach (int c in r.Cells) mol += fld.Moles(c);
                var holes = Enumerable.Range(0, L2.N).Where(c => fld.Hole[c] > 0).Select(c => $"{L2.X(c)},{L2.Y(c)}:{fld.Hole[c]:0.####}");
                var gaps = L2.Doors.Where(d => fld.DoorGapOpen(d.Id) || fld.DoorBleed[d.Id] != 0 || d.External && fld.DoorOpen[d.Id] > 0).Select(d => $"문{d.Id}{(d.External ? "(바깥)" : "")} 열림 {fld.DoorOpen[d.Id]:0.0} 틈 {fld.DoorGapOpen(d.Id)} 밸브 {fld.DoorBleed[d.Id]}");
                Console.WriteLine($"  {Time.Clock(s.Tick)} 몰 {mol:0} · 예비 {net.AirReserve:0} · 구멍 [{string.Join(" ", holes)}] · 문 [{string.Join(" ", gaps)}]");
                if (rest.Contains("who")) { var mm = s.Get<Mind>(); foreach (var a in mm.Agents) Console.WriteLine($"      {mm.NameOf(a.Id)} {L2.X(a.C.Cell)},{L2.Y(a.C.Cell)} {mm.RoomOf(a.C.Cell)} · {mm.Thought(a.Id)} · 목표 {a.C.Goal} 단계 {a.C.StepIndex}/{a.Plan.Count}"); }
            }
            if (rest.Contains("air") && t % Time.Minutes(10) == 0) Console.WriteLine($"  {Time.Clock(s.Tick)} 정화 {net.TraceScrubbed:0.0}몰 · 정화기 {(net.ScrubberWorking() ? "돈다" : "섰다")} · 팬 {net.HvacKw:0}kW · 배기 {string.Join(",", Enumerable.Range(0, s.Layout.Rooms.Count).Where(r => net.Exhaust[r] != 0).Select(r => s.Layout.Rooms[r].Name))}");
        }
        foreach (var sd in s.Get<Storyteller>().Seeds) Console.WriteLine($"    씨앗 {Time.Clock(sd.Tick)} {Storyteller.SeedName(sd.Kind)} · {s.Causes.Story(sd.Cause)}");
        foreach (var (tick, text) in sm.Log.TakeLast(rest.Contains("all") ? 300 : 40)) Console.WriteLine($"    주컴 {Time.Clock(tick)} {text}");
        if (rest.Contains("inc")) foreach (var x in sm.Incidents) Console.WriteLine($"    사건 {Time.Clock(x.Since)} {sm.RoomName(x.Room)} {ShipMind.KindName(x.Kind)} 칸 {(x.Cell >= 0 ? s.Layout.X(x.Cell) + "," + s.Layout.Y(x.Cell) : "-")} 원인 {x.Cause}/{s.Causes.Root(x.Cause)} \"{s.Causes.Story(x.Cause)}\"");
        foreach (var (tick, text) in sm.Journal) Console.WriteLine($"    일지 {Time.Clock(tick)}\n      {text.Replace("\n", "\n      ")}");
        if (rest.Contains("cook"))
        {
            var mm = s.Get<Mind>();
            Console.WriteLine($"    밥 짓기 {mm.Cooked} · 깜빡 {mm.Forgot} · 끼니 {mm.Meals} · 주컴퓨터가 조리대를 끔 {sm.StovesCut} · 조심 {string.Join(" ", mm.Agents.Where(a => a.IsPerson).Select(a => a.C.Careful.ToString("0.00")))}");
            foreach (var f in s.Facts.Since(0).Where(x => x.Kind is FactKind.Mistake or FactKind.Switched or FactKind.Ignition)) Console.WriteLine($"    {Time.Clock(f.Tick)} {f.Kind} · {s.Causes.Story(f.Cause)}");
        }
        if (rest.Contains("soc"))
        {
            // 사회: 관계 사실 · 무리 · 존경 · 정 분포
            var so = s.Get<Society>(); var mm = s.Get<Mind>();
            foreach (var f in s.Facts.Since(0).Where(x => x.Kind == FactKind.Relationship)) Console.WriteLine($"    관계 {Time.Clock(f.Tick)} ({f.Size:0.##}) {s.Causes.Text(f.Cause)}");
            foreach (var b in so.Bands) Console.WriteLine($"    무리 {so.BandName(b.Id)} {b.Size}명 · 이끄는 이 {mm.NameOf(b.Leader)} · {Time.Clock(b.Formed)}부터");
            var ppl = mm.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
            var bonds = ppl.SelectMany(i => ppl.Where(j => j > i).Select(j => Math.Min(so.BondOf(i, j), so.BondOf(j, i)))).OrderByDescending(x => x).ToList();
            Console.WriteLine($"    서로 정 (작은 쪽) 위 5: {string.Join(" ", bonds.Take(5).Select(x => x.ToString("0.00")))} · 0.4 넘는 쌍 {bonds.Count(x => x >= 0.4f)} / {bonds.Count}");
            Console.WriteLine($"    존경 위 3: {string.Join(" · ", ppl.OrderByDescending(i => so.Respect.Length > i ? so.Respect[i] : 0).Take(3).Select(i => $"{mm.NameOf(i)} {(so.Respect.Length > i ? so.Respect[i] : 0):0.00}"))} · 따르는 이 {(so.Head >= 0 ? mm.NameOf(so.Head) : "없음")}");
        }
    }
}

public static partial class Program
{
    /// <summary>구멍 · 끼인 문 · 로봇 일을 10분마다 (--probe-holes [배] [시드] [시작 시간] [몇 시간]).</summary>
    internal static void ProbeHoles(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-holes").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Saetteul";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 18;
        double from = rest.Length > 2 ? double.Parse(rest[2]) : 7;
        double hours = rest.Length > 3 ? double.Parse(rest[3]) : 3;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        s.Get<Storyteller>().Enabled = true;
        var m = s.Get<Mind>(); var f = s.Get<Field>(); var st = s.Get<Structure>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
        s.Run((long)Time.Hours(from));
        for (long t = 0; t < Time.Hours(hours); t++)
        {
            s.Step();
            if (t % Time.Minutes(10) != 0) continue;
            int holes = 0; double area = 0;
            for (int c = 0; c < f.Cells; c++) if (f.Hole[c] > 0) { holes++; area += f.Hole[c]; }
            var jams = Enumerable.Range(0, s.Layout.Doors.Count).Where(d => st.DoorJam[d] != 0).Select(d => $"{m.RoomOf(s.Layout.Doors[d].Cell)}:{f.DoorOpen[d]:0.0}");
            var goals = m.Agents.Where(a => !a.IsPerson).GroupBy(a => a.C.Goal).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
            var people = m.Agents.Where(a => a.IsPerson && life[a.C.Body].Alive != 0).GroupBy(a => a.C.Goal).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
            double kpa = s.Layout.Rooms.Where(r => r.Cells.Length > 0).Average(r => f.Pressure(r.Cells[0]));
            Console.WriteLine($"  {Time.Clock(s.Tick)} 구멍 {holes}칸 {area * 1e4:0}cm² · 끼인 문 {string.Join(",", jams)} · 배 기압 {kpa:0} · 막음 {s.Facts.CountOf(FactKind.Patched)} · 로봇 [{string.Join(" ", goals)}] · 사람 [{string.Join(" ", people)}] · 사건 {sm.Incidents.Count(x => x.Active != 0)}");
        }
    }
}

public static partial class Program
{
    /// <summary>한 칸의 방 · 온도 · 기압 · 산소를 30분마다 (--probe-cell [배] [시드] [칸] [시간]).</summary>
    internal static void ProbeCell(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-cell").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Saetteul";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 18;
        int cell = rest.Length > 2 ? int.Parse(rest[2]) : 2919;
        double hours = rest.Length > 3 ? double.Parse(rest[3]) : 14;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        s.Get<Storyteller>().Enabled = true;
        var m = s.Get<Mind>(); var f = s.Get<Field>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>(); var life = s.Get<Life>();
        int room = m.RoomOf(cell);
        Console.WriteLine($"  칸 {cell} ({s.Layout.X(cell)},{s.Layout.Y(cell)}) 방 {(room >= 0 ? s.Layout.Rooms[room].Name : "-")} · 설비 {string.Join(",", net.Devices.Where(d => d.Room == room).Select(d => d.Name))}");
        for (long t = 0; t < Time.Hours(hours); t++)
        {
            s.Step();
            if (t % Time.Minutes(30) != 0) continue;
            var cells = room >= 0 ? s.Layout.Rooms[room].Cells : new[] { cell };
            var who = m.Agents.Where(a => a.IsPerson && m.RoomOf(a.C.Cell) == room).Select(a => $"{m.NameOf(a.Id)}({m.Thought(a.Id)} 체온 {life[a.C.Body].CoreK - 273.15:0.0} 의식 {life[a.C.Body].Conscious} 살아 {life[a.C.Body].Alive} 대사 {life[a.C.Body].ActivityW:0}W 우주복 {life[a.C.Body].Suit} 산소 {life[a.C.Body].SuitO2:0.0} 물 {life[a.C.Body].Water:0} 화상 {life[a.C.Body].Burn:0.00})");
            Console.WriteLine($"  {Time.Clock(s.Tick)} 칸 {f.Temp(cell) - 273.15:0.0}℃ · 방 평균 {cells.Average(c => f.Temp(c)) - 273.15:0.0}℃ 최고 {cells.Max(c => f.Temp(c)) - 273.15:0.0}℃ · {f.Pressure(cell):0}kPa · 통풍구 {(room >= 0 ? net.Damper[room] : 9)} 전원 {(room >= 0 && net.RoomPowered(room) ? "있음" : "없음")} · 냉각 {net.CoolingKw:0}kW 공조 {net.HvacKw:0}kW · 사건 {string.Join(",", sm.Incidents.Where(x => x.Active != 0 && x.Room == room).Select(x => x.Kind))} · {string.Join(" ", who)}");
        }
    }
}

public static partial class Program
{
    /// <summary>위험했다 살아나기를 되풀이하는 사람 (--probe-flap [배] [시드] [날]).</summary>
    internal static void ProbeFlap(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-flap").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Saetteul";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 18;
        double days = rest.Length > 2 ? double.Parse(rest[2]) : 2;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        s.Get<Storyteller>().Enabled = true;
        var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
        var was = new Dictionary<int, bool>(); var flips = new Dictionary<int, int>(); var first = new Dictionary<int, long>();
        var last = new Dictionary<int, string>();
        for (long t = 0; t < (long)(days * Time.Hours(24)); t++)
        {
            s.Step();
            foreach (var a in m.Agents.Where(a => a.IsPerson))
            {
                var b = life[a.C.Body];
                if (b.Alive == 0) continue;
                bool now = b.SpO2 < 0.75f || b.Conscious == 0;
                if (now && !first.ContainsKey(a.Id) && !last.ContainsKey(a.Id)) Console.WriteLine($"  처음 위험 {m.NameOf(a.Id)} {Time.Clock(t)} 칸 {b.Cell} {(m.RoomOf(b.Cell) >= 0 ? s.Layout.Rooms[m.RoomOf(b.Cell)].Name : "-")} 체온 {b.CoreK - 273.15:0.0} 공기 {f.Temp(b.Cell) - 273.15:0.0}℃ 대사 {b.ActivityW:0}W 포화 {b.SpO2:0.00} · {m.Thought(a.Id)}");
                if (now) { was[a.Id] = true; last[a.Id] = $"{Time.Clock(t)} {(m.RoomOf(b.Cell) >= 0 ? s.Layout.Rooms[m.RoomOf(b.Cell)].Name : "-")} 포화 {b.SpO2:0.00} 의식 {b.Conscious} 산소부족 {b.Hypoxia:0.00} 폐 {b.LungDamage:0.00} CO {b.COHb:0.00} 체온 {b.CoreK - 273.15:0.0} 피 {b.BloodL:0.0} 아픔 {b.Pain:0.00} 우주복 {b.Suit}/{b.SuitO2:0} 기압 {f.Pressure(b.Cell):0} 산소 {f.Partial(s.Tables.gO2, b.Cell):0.0} 칸 {b.Cell} 공기 {f.Temp(b.Cell) - 273.15:0.0}℃ 대사 {b.ActivityW:0}W · {m.Thought(a.Id)}"; }
                else if (was.GetValueOrDefault(a.Id) && b.SpO2 > 0.9f) { was[a.Id] = false; flips[a.Id] = flips.GetValueOrDefault(a.Id) + 1; if (!first.ContainsKey(a.Id)) first[a.Id] = t; }
            }
        }
        foreach (var (id, n) in flips.OrderByDescending(kv => kv.Value).Take(6))
            Console.WriteLine($"  {m.NameOf(id)} {n}번 · 처음 {Time.Clock(first[id])} · 마지막 위험: {last.GetValueOrDefault(id)}");
    }
}

public static partial class Program
{
    /// <summary>문 하나: 열림 · 목표 · 그 칸과 곁에 있는 몸 · 로봇 (--probe-door [배] [시드] [시간] [문 번호]).</summary>
    internal static void ProbeDoor(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-door").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Saetteul";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 18;
        double hours = rest.Length > 2 ? double.Parse(rest[2]) : 9.36;
        int d = rest.Length > 3 ? int.Parse(rest[3]) : 24;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        s.Get<Storyteller>().Enabled = true;
        var m = s.Get<Mind>(); var f = s.Get<Field>(); var st = s.Get<Structure>(); var life = s.Get<Life>();
        s.Run((long)Time.Hours(hours));
        int dc = s.Layout.Doors[d].Cell;
        Console.WriteLine($"  문 {d} 칸 {dc} ({s.Layout.X(dc)},{s.Layout.Y(dc)}) {s.Layout.Doors[d].Kind} 바깥 {s.Layout.Doors[d].External} · 열림 {f.DoorOpen[d]:0.0} 목표 {st.DoorTarget[d]:0.0} 끼임 {st.DoorJam[d]}");
        foreach (var a in m.Agents.Where(a => m.Dist(a.C.Cell, dc) <= 2))
            Console.WriteLine($"    {m.NameOf(a.Id)} 칸 {a.C.Cell} 거리 {m.Dist(a.C.Cell, dc)} · {m.Thought(a.Id)} · 연 문 {a.C.OpenedDoor}{(a.IsPerson ? $" · 살아 {life[a.C.Body].Alive} 의식 {life[a.C.Body].Conscious}" : "")}");
        for (int b = 0; b < life.Bodies.Count; b++) if (m.Dist(life[b].Cell, dc) <= 1) Console.WriteLine($"    몸 {b} 칸 {life[b].Cell} 살아 {life[b].Alive}");
    }
}

public static partial class Program
{
    /// <summary>큰 불 하나를 붙여 몇 분마다 타는 칸 · 다시 붙음 · 끔 · 사람을 센다 (--probe-fire [배] [시드] [장치 종류] [MJ] [분]).</summary>
    internal static void ProbeFire(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-fire").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Hwasal";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 3;
        string type = rest.Length > 2 ? rest[2] : "Battery";
        double mj = rest.Length > 3 ? double.Parse(rest[3]) : 8;
        double minutes = rest.Length > 4 ? double.Parse(rest[4]) : 120;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        var ch = s.Get<Chemistry>(); var f = s.Get<Field>(); var life = s.Get<Life>(); var m = s.Get<Mind>(); var sm = s.Get<ShipMind>();
        s.Run(Time.Minutes(30));
        int cell = s.Layout.Furniture.First(fu => fu.Type == type).Cells[0];
        int room = m.RoomOf(cell);
        ch.Ignite(cell, mj * 1e6, s.Causes.Add(CauseKind.Story, "시험 불", s.Tick, cell));
        long cursor = s.Facts.Total;
        Console.WriteLine($"  불: {(room >= 0 ? s.Layout.Rooms[room].Name : "-")} 칸 {cell}");
        for (long t = 0; t < Time.Minutes(minutes); t++)
        {
            s.Step();
            if (t % Time.Minutes(5) != 0) continue;
            int ign = 0, ext = 0;
            foreach (var x in s.Facts.Since(cursor)) { if (x.Kind is FactKind.Ignition or FactKind.FireSpread) ign++; if (x.Kind == FactKind.Extinguished) ext++; }
            cursor = s.Facts.Total;
            var cells = room >= 0 ? s.Layout.Rooms[room].Cells : new[] { cell };
            Console.WriteLine($"  +{t / 25}분 타는 칸 {ch.BurningCount} · 붙음/번짐 {ign} · 끔 {ext} · 방 최고 {cells.Max(c => f.Temp(c)) - 273:0}℃ 겉면 {cells.Max(c => f.Tf[c]) - 273:0}℃ · 산소 {cells.Average(c => f.Partial(s.Tables.gO2, c)):0.0}kPa · 연기 {cells.Max(c => f.Ppm(s.Tables.gSoot, c)):0}ppm · 사건 {string.Join(",", sm.Incidents.Where(x => x.Active != 0 && x.Room == room).Select(x => x.Kind + (x.Sealed != 0 ? "(봉쇄)" : "")))} · 로봇 {string.Join(",", m.Agents.Where(a => !a.IsPerson && a.C.Goal == GoalKind.FightFire).Select(a => m.NameOf(a.Id)))} · 다침 {life.Bodies.Count(b => b.Burn > 0.1f)} 죽음 {life.Deaths}");
        }
    }
}
