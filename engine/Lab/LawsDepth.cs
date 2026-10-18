using ShipEngine;

namespace Lab;

/// <summary>법칙 시험 — 깊이 바퀴 2의 물리 쪽 (④ 금 · 조각 · ② · ③).</summary>
public static partial class Program
{
    /// <summary>장 + 구조만 붙인 세계 (공기를 채운다).</summary>
    internal static Sim FieldStructure(Layout layout, ulong seed = 1, Action<Sim>? setup = null)
    {
        var s = new Sim(new SimSetup(layout, seed, sim =>
        {
            var f = new Field(sim.Layout, sim.Tables);
            sim.Add(f);
            sim.Add(new Structure(f));
        }));
        s.Get<Field>().FillAir();
        setup?.Invoke(s);
        return s;
    }

    /// <summary>가운데 기둥 줄(x = 5)을 기준으로 좌우가 똑같은 배 — 가운데 줄을 부수면 두 조각.</summary>
    internal const string Twins = @"
###########
#....#....#
#....#....#
#....#....#
###########";

    private static void DepthLaws()
    {
        var t = Tables.Default;
        if (Do("금"))
        {
            Console.WriteLine("\n[④ 금 — 파리스 법칙 · 하중이 없으면 자라지 않는다]");
            var lay = Worlds.Ascii(Worlds.TwoRooms);
            int part = Worlds.At(lay, 10, 1), hull = Worlds.At(lay, 4, 0);
            var s = FieldStructure(lay, 1, sim => { var st = sim.Get<Structure>(); st.SeedCrack(part, 0.05, 0); st.SeedCrack(hull, 0.003, 0); });
            var st = s.Get<Structure>();
            s.Run(Time.Hours(1));
            Check("하중이 오르내리지 않으면 금이 자라지 않는다 — 양쪽이 같은 기압인 칸막이 · 한 기압을 늘 받치는 마른 외판",
                st.Crack[part] == 0.05f && st.Crack[hull] == 0.003f && st.CrackGrowths == 0, $"칸막이 {st.Crack[part] * 1000:0.0}mm · 외판 {st.Crack[hull] * 1000:0.0}mm · 자람 {st.CrackGrowths}");

            // 파리스 적분: 같은 하중 폭으로 5만 주기 — 엔진(반 주기씩)과 닫힌 식의 수치 적분(룽게-쿠타)이 같다
            var s2 = FieldStructure(lay, 1);
            var st2 = s2.Get<Structure>();
            int c = Worlds.At(lay, 4, 0);
            const double a0 = 0.01, dS = 150; const int n = 50_000;
            st2.SeedCrack(c, a0, 0);
            st2.LoadCycle(s2, c, dS, n);
            var m = t.Materials[lay.Material[c]].Def;
            double Rate(double a) { double mb = Math.Min(Structure.MaxBulge, Math.Sqrt(1 + 1.61 * a * a / (Structure.ShellR * 0.010))); double dk = mb * dS * Math.Sqrt(Math.PI * a); return dk < m.DKth ? 0 : m.ParisC * Math.Pow(dk, m.ParisM); }
            double a = a0;
            for (int k = 0; k < n; k++) { double k1 = Rate(a), k2 = Rate(a + k1 / 2), k3 = Rate(a + k2 / 2), k4 = Rate(a + k3); a += (k1 + 2 * k2 + 2 * k3 + k4) / 6; }
            Check("파리스 법칙: 같은 하중 폭 150MPa · 5만 주기 — 엔진의 금 = 닫힌 식의 적분 (1% 안)", Rel(st2.Crack[c], a) < 0.01,
                $"엔진 {st2.Crack[c] * 1000:0.00}mm · 적분 {a * 1000:0.00}mm (처음 {a0 * 1000:0}mm)");
        }

        if (Do("조각"))
        {
            Console.WriteLine("\n[④ 조각 — 대칭 · 보존 · 결정론]");
            var lay = Worlds.Ascii(Twins);
            Sim Split(int threads)
            {
                int keep = Par.Threads; Par.Threads = threads;
                var s = FieldStructure(lay, 3);
                var st = s.Get<Structure>();
                s.Run(5);
                for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, 5, y), 0);
                s.Run(200);
                Par.Threads = keep;
                return s;
            }
            var a = Split(1);
            var sa = a.Get<Structure>(); var fa = a.Get<Field>();
            double m0 = sa.FragmentMass(0), m1 = sa.FragmentMass(1);
            Check("좌우가 똑같은 배를 가운데에서 끊으면 두 조각의 질량이 같다 · 사이 칸은 우주가 된다", sa.Fragments.Count == 1 && Rel(m0, m1) < 1e-9 && fa.Severed[Worlds.At(lay, 5, 2)] != 0,
                $"조각 {sa.Fragments.Count} · 배 {m0 / 1000:0.000}t · 조각 {m1 / 1000:0.000}t · 사이 칸 끊김 {fa.Severed[Worlds.At(lay, 5, 2)]}");
            // 보존: 끊긴 칸 · 틈으로 빠진 기체는 '우주로 간 것'으로 센다
            var b = FieldStructure(lay, 3, sim => sim.Get<Field>().Adiabatic = true);
            var fb = b.Get<Field>();
            double n0 = 0; for (int g = 0; g < fb.G; g++) n0 += fb.Total(g);
            b.Run(5);
            for (int y = 0; y < 5; y++) b.Get<Structure>().Destroy(b, Worlds.At(lay, 5, y), 0);
            b.Run(200);
            double n1 = 0; for (int g = 0; g < fb.G; g++) n1 += fb.Total(g);
            Check("배가 갈라져도 기체 몰 수 = 남은 것 + 우주로 간 것", Rel(n0, n1 + fb.VentedTotal) < 1e-9, $"처음 {n0:0.0} · 남음 {n1:0.0} · 우주로 {fb.VentedTotal:0.0}몰");
            var c1 = Split(Math.Max(4, Environment.ProcessorCount));
            Check("갈라진 배: 1코어 = 여러 코어 (바이트까지)", a.Hash() == c1.Hash());
            var cl = a.Clone();
            long split = -1;
            for (int i = 0; i < 100 && split < 0; i++)
            {
                a.Step(); cl.Step();
                if (a.Hash() == cl.Hash()) continue;
                split = a.Tick;
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
                    foreach (var (e1, e2) in a.Engines.Zip(cl.Engines))
                    {
                        var w1 = new StateWriter(); e1.Save(w1); var w2 = new StateWriter(); e2.Save(w2);
                        var b1 = w1.ToArray(); var b2 = w2.ToArray();
                        int at = -1; for (int k = 0; k < Math.Min(b1.Length, b2.Length); k++) if (b1[k] != b2[k]) { at = k; break; }
                        Console.WriteLine($"    {e1.Name}: {(at < 0 && b1.Length == b2.Length ? "같음" : $"다름 @{at} / {b1.Length}")}");
                    }
            }
            Check("갈라진 배를 되살려 100틱 이어 가도 틱마다 같다", split < 0, split < 0 ? "" : $"틱 {split}에 갈라짐");
        }
    }
}
