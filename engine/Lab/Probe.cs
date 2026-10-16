using ShipEngine;

namespace Lab;

public static partial class Program
{
    /// <summary>결정론 조사: 1코어 · 여러 코어로 같은 세계를 돌려 처음 갈라진 틱과 엔진을 찾는다.</summary>
    internal static void ProbeDeterminism()
    {
        Sim Make()
        {
            var s = ShipWorld.Create("Kestrel", 9);
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
        for (int t = 0; t < 400; t++)
        {
            Par.Threads = 1; a.Step();
            Par.Threads = 8; b.Step();
            foreach (var e in a.Engines)
            {
                var wa = new StateWriter(); e.Save(wa);
                var eb = b.Engines.First(x => x.Name == e.Name);
                var wb = new StateWriter(); eb.Save(wb);
                if (!wa.ToArray().AsSpan().SequenceEqual(wb.ToArray()))
                {
                    Console.WriteLine($"  틱 {a.Tick - 1}에 '{e.Name}'이(가) 갈라졌다");
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
