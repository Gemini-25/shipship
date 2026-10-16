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
}
