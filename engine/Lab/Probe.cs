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
            var s = Environment.GetEnvironmentVariable("CREW") == "1" ? ShipWorld.Crewed("Kestrel", 9, 6, 3) : ShipWorld.Create("Kestrel", 9);
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
        string ship = rest.Length > 0 ? rest[0] : "Kestrel";
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
