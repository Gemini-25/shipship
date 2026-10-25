using ShipEngine;

namespace Lab;

/// <summary>
/// 깨짐 법칙 (PH8 — docs/PHYSICS.md §5 · §10 법칙 10 · 8): 조각 + 가루 질량 = 부모 · 운동량 장부 그대로 · 같은 시드면 같은 금 · 깨진 몫 = 1 − exp(−(ε/ε₀)³) ·
/// 침대 위에서는 깨지지 않는다 · 여러 부위 꼴은 이음매가 먼저 갈라진다 · 깨진 뒤 운동 에너지가 늘지 않는다.
/// </summary>
public static partial class Program
{
    private const string BedRoom = "\n############\n#..........#\n#..........#\n#....bb....#\n#..........#\n#..........#\n#..........#\n############";

    private static void BreakLaws()
    {
        var t = Tables.Default;
        int Item(string name) => t.People.Items.Keys.ToList().IndexOf(name);
        var cup = DotForge.Make(t, "컵");
        var glass = t.Materials.First(m => m.Id == "glass").Def; var steel = t.Materials.First(m => m.Id == "steel").Def;
        // 법칙 10 · 8: 무중력 · 공기 없는 방 — 8m/s 컵이 벽에 부딪혀 깨진다 (조각 · 가루 질량 · 운동량 장부 · 에너지 · 같은 시드)
        {
            (ulong hash, double mass, double worstP, double rise, double e0, double e1, int pieces, long shattered, double grit) Run(ulong seed)
            {
                var s = PropWorld(0, seed);
                var st = s.Get<Structure>();
                int id = Slide(s, "컵", 9.2, 4.5, 8, 0.6, 3);
                (double px, double py) Book() { var (px, py, _) = st.PropMomentum(); return (px + st.ToHostPx, py + st.ToHostPy); }
                var b0 = Book();
                double e0 = Kinetic(st), prev = e0, rise = 0, worst = 0, scale = cup.Mass * 8;
                for (int k = 0; k < 4; k++)
                {
                    s.Step();
                    double e = Kinetic(st);
                    rise = Math.Max(rise, e - prev); prev = e;
                    var b = Book();
                    worst = Math.Max(worst, Math.Sqrt((b.px - b0.px) * (b.px - b0.px) + (b.py - b0.py) * (b.py - b0.py)) / scale);
                }
                double mass = st.GritKg;
                int n = 0;
                foreach (var p in st.Props) { if (p.Kind != 2) continue; mass += st.PropForm(p).Mass; n++; }
                return (s.Hash(), mass, worst, rise, e0, prev, n, st.Shattered, st.GritKg);
            }
            var a = Run(3); var a2 = Run(3);
            Check("깨짐 — 질량 · 운동량 · 에너지 (법칙 10 · 8): 무중력 · 공기 없는 방에서 8m/s 유리컵이 벽에 부딪혀 깨진다 — 조각 + 가루 질량 = 컵 (1e-9) · 물체 + 벽 · 바닥이 받은 몫 = 처음 (운동량 1e-9) · 운동 에너지가 틱마다 줄기만 한다",
                a.shattered >= 1 && a.pieces + (a.grit > 0 ? 1 : 0) >= 2 && Math.Abs(a.mass - cup.Mass) <= 1e-9 && a.worstP <= 1e-9 && a.rise <= 1e-12 * a.e0 && a.e1 < a.e0,
                $"깨짐 {a.shattered} · 조각 {a.pieces} · 가루 {a.grit * 1000:0.0}g · 질량 {a.mass:0.000000000}/{cup.Mass:0.000000000}kg · 남는 운동량 {a.worstP:0.0e0} · 가장 큰 늚 {a.rise:0.0e0}J · 운동 에너지 {a.e0:0.00} → {a.e1:0.00}J");
            Check("깨짐 — 같은 시드면 같은 금 (법칙 10): 같은 컵 · 같은 시드를 두 번 깨면 찍은 바이트가 같다", a.hash == a2.hash && a.shattered >= 1, $"{a.hash:x16} / {a2.hash:x16}");
        }
        // 법칙 12: 깨짐의 결정론 — 컵 · 접시 · 유리병 여섯이 서로 · 벽에 부딪혀 깨지는 0.1g 방: 1코어 = 4코어 · 조각이 흩어지는 중에 찍고 되살려 30틱 바이트 같음
        {
            (ulong hash, bool same, long shattered) Run(int threads)
            {
                int keep = Par.Threads; Par.Threads = threads;
                var s = PropWorld(0.1f, 9);
                var st = s.Get<Structure>();
                string[] names = { "컵", "접시", "유리병" };
                for (int k = 0; k < 6; k++) Slide(s, names[k % 3], 3 + k * 1.3, 3.5 + k % 2 * 3, k % 2 == 0 ? 7 : -6, k % 2 == 0 ? 4 : -5, 2 - k);
                s.Step();
                var r = new Sim(s.Setup); r.Load(s.Save());
                for (int k = 0; k < 30; k++) { s.Step(); r.Step(); }
                bool same = s.Save().AsSpan().SequenceEqual(r.Save());
                Par.Threads = keep;
                return (s.Hash(), same, st.Shattered);
            }
            var one = Run(1); var many = Run(Math.Max(4, Environment.ProcessorCount));
            Check("깨짐 — 결정론 (법칙 12): 유리 · 세라믹 여섯이 서로 · 벽에 부딪혀 깨지는 0.1g 방 — 1코어 = 4코어 · 조각이 흩어지는 중에 찍고 되살려 30틱 바이트 같음",
                one.hash == many.hash && one.same && many.same && one.shattered >= 2, $"{one.hash:x16} / {many.hash:x16} · 되살림 같음 {one.same}/{many.same} · 깨짐 {one.shattered}");
        }
        // 법칙 10: 깨진 몫 = 1 − exp(−(ε/ε₀)³) — 1g 강철 바닥에 떨어진 유리컵 (0.3m · 0.75m 각 시드 200)
        {
            double e = Math.Min(glass.Rest, steel.Rest), sShare = steel.E / (steel.E + 0.1 * glass.E);
            double eps0 = 130 * (glass.Kc * glass.Kc / glass.E * 1000) / (glass.Density * Math.Max(cup.W, cup.D) * DotForm.Dot);
            var lines = new List<string>();
            bool ok = true;
            foreach (double h in new[] { 0.3, 0.75 })
            {
                double eps = 9.8 * h * (1 - e * e) * sShare, want = 1 - Math.Exp(-Math.Pow(eps / eps0, 3));
                int broke = 0;
                for (ulong seed = 1; seed <= 200; seed++)
                {
                    var s = PropWorld(1, seed);
                    var st = s.Get<Structure>();
                    st.PutItem(Item("컵"), 0, s.Layout.Index(5, 4), (float)h);
                    s.Step(); s.Step();
                    if (st.Shattered > 0) broke++;
                }
                double got = broke / 200.0;
                ok &= Math.Abs(got - want) <= 0.07;
                lines.Add($"{h}m: ε {eps:0.00}/ε₀ {eps0:0.00} → 식 {want:0.00} · 깨진 몫 {got:0.00}");
            }
            Check("깨짐 — 깨진 몫이 식과 맞는다 (법칙 10): 강철 바닥에 떨어진 유리컵 — 몫 = 1 − exp(−(ε/ε₀)³) ± 0.07 (ε = g h (1 − e²) s · ε₀ = 130 G_c / ρ L)", ok, string.Join(" · ", lines));
        }
        // 법칙 10: 침대 위에서는 깨지지 않는다 (폼 · 천 위 단단함 몫이 거의 0)
        {
            int broke = 0, onBed = 0;
            var legend = new Dictionary<char, string> { ['b'] = "Bed" };
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var s = PropWorld(1, seed, BedRoom, legend);
                var st = s.Get<Structure>();
                int id = st.PutItem(Item("컵"), 0, Worlds.At(s.Layout, 5, 3), 1.0f);
                s.Step(); s.Step();
                if (st.Shattered > 0) broke++;
                int i = st.IndexOf(id);
                if (i >= 0 && st.Props[i].On > 0) onBed++;
            }
            Check("깨짐 — 침대 위 0 (법칙 10): 1m에서 침대 위로 떨어진 유리컵 20개 — 깨지지 않고 침대 위에 선다", broke == 0 && onBed == 20, $"깨짐 {broke} · 침대 위 {onBed}/20");
        }
        // 법칙 10: 이음매가 먼저 — 무중력 12m/s 소화기가 벽에 부딪히면 노즐 이음매만 갈라진다 (강철 몸은 그대로 · 가루 없음)
        {
            var s = PropWorld(0, 2);
            var st = s.Get<Structure>();
            var ext = DotForge.Make(t, "소화기");
            Slide(s, "소화기", 9, 4.5, 12, 0);
            for (int k = 0; k < 3; k++) s.Step();
            var pieces = st.Props.Where(p => p.Kind == 2).ToList();
            var parts = pieces.Select(p => st.Masks[p.Piece - 1].Part).Distinct().Count();
            double mass = pieces.Sum(p => st.PropForm(p).Mass);
            double heavy = pieces.Count > 0 ? pieces.Max(p => st.PropForm(p).Mass) : 0;
            Check("깨짐 — 이음매가 먼저 (법칙 10): 무중력 12m/s 소화기가 벽에 부딪힌다 — 노즐 이음매에서만 두 조각 (부위가 다르다 · 질량 합 = 소화기 · 강철 몸이 거의 다) · 가루 없음",
                st.Shattered == 1 && pieces.Count == 2 && parts == 2 && Math.Abs(mass - ext.Mass) <= 1e-9 && heavy > 0.9 * ext.Mass && st.GritKg == 0,
                $"깨짐 {st.Shattered} · 조각 {pieces.Count} · 부위 {parts} · 질량 {mass:0.000}/{ext.Mass:0.000}kg · 큰 조각 {heavy:0.000}kg · 가루 {st.GritKg:0.000}kg");
        }
    }
}
