using ShipEngine;

namespace Lab;

/// <summary>
/// 가구 떼기 법칙 (PH10 — docs/PHYSICS.md §3-7 · §5-4): 뗀 설비 질량 = 물체 질량 · 몸 질량 · 무게중심 · 회전관성 = 칸에서 다시 센 값 ·
/// 연료 · 열용량 · 고체 열 에너지의 칸 합이 같다 (물체가 잠든 칸으로 옮겨 간다) · 놓인 탁자는 제자리에서 다시 앉는다 (몸 · 칸이 그대로 돌아온다) · 찍고 되살려도 같다 ·
/// 2톤 아래 덩어리의 설비는 그 꼴의 물체가 된다 (점 알갱이가 아니다).
/// </summary>
public static partial class Program
{
    private const string FurnRoom = "\n############\n#s.........#\n#s.........#\n#..........#\n#.....tt...#\n#.....tt...#\n############";

    private static void FurnLaws()
    {
        // ── 몸이 있는 배: 선반을 뜯어 2m/s로 민다 · 탁자는 그 자리에서 뜯는다 (놓였다 — 다시 앉는다) ──
        {
            var s = PropWorld(1, 5, FurnRoom, new Dictionary<char, string> { ['s'] = "Shelf", ['t'] = "Table" });
            var st = s.Get<Structure>(); var f = s.Get<Field>(); var L = s.Layout;
            Furnish.Apply(f);
            f.Adiabatic = true;   // 배 밖으로 나가는 열 없이 (칸끼리 전도는 합을 지킨다) — 옮긴 몫만 본다
            int shelf = L.Furniture.FindIndex(x => x.Type == "Shelf"), table = L.Furniture.FindIndex(x => x.Type == "Table");
            foreach (int c in L.Furniture[shelf].Cells) f.Ts[c] = 350;   // 선반 칸은 더웠다 (옮긴 열용량이 그 온도의 에너지를 지고 간다)
            st.EnsureBody();
            s.Step();
            (double cs, double heat, double store) Sums() { double a = 0, b = 0, c2 = 0; for (int c = 0; c < L.N; c++) { a += f.Cs[c]; b += f.Cs[c] * f.Ts[c]; } foreach (double x in f.Store) c2 += x; return (a, b, c2); }
            double Gap(int b) { var (m, lx, ly, i) = st.BodyMoments(b); var o = st.Bodies[b]; return Math.Max(Math.Max(Rel(m, o.Mass), Math.Abs(lx - o.Lx) + Math.Abs(ly - o.Ly)), Rel(i, o.Inertia)); }
            var (cs0, heat0, store0) = Sums();
            double m0 = st.Bodies[0].Mass, gap0 = Gap(0);
            int id = st.TearLoose(s, shelf, 2, 0, 0, s.Causes.Add(CauseKind.Setup, "시험 — 선반을 뜯었다", s.Tick), "볼트가 풀린");
            var p = st.Props[st.IndexOf(id)];
            double m1 = st.Bodies[0].Mass, gap1 = Gap(0);
            double kg = st.FurnitureKg(shelf);
            for (int k = 0; k < 200 && Structure.Awake(st.Props[st.IndexOf(id)]); k++) s.Step();
            s.Step();
            var q = st.Props[st.IndexOf(id)];
            var (cs1, heat1, store1) = Sums();
            int at = st.CellOf(q);
            bool moved = st.FurnAt[shelf] == at && !L.Furniture[shelf].Cells.Contains(at);
            Check("가구 떼기: 뗀 선반 질량 = 물체 질량 = 몸이 잃은 질량 (1e-6 — 물체 질량은 float) · 몸 질량 · 무게중심 · 회전관성 = 칸에서 다시 센 값 (1e-9)",
                p.Kind == 1 && Rel(p.Kg, kg) < 1e-6 && Rel(m0 - m1, kg) < 1e-6 && gap0 < 1e-9 && gap1 < 1e-9,
                $"설비 {kg:0.000}kg · 물체 {p.Kg:0.000} · 몸이 잃은 {m0 - m1:0.000} · 몸 어긋남 {gap0:0.0e0} → {gap1:0.0e0}");
            // ③ 옮김 그 자체: 잠든 칸에서 제자리로 · 다시 잠든 칸으로 — 열용량 · 고체 열 에너지 · 저장 칸 합 그대로 (1e-12) · 받는 칸은 섞인 온도
            var (ca, ha, sa) = Sums();
            f.MoveFurnishing(shelf, at, -1); var (cb, hb, sb) = Sums();
            f.MoveFurnishing(shelf, -1, at); var (cc, hc, sc) = Sums();
            double worst = Math.Max(Math.Max(Rel(cb, ca), Rel(hb, ha)), Math.Max(Math.Max(Rel(sb, sa), Rel(cc, ca)), Math.Max(Rel(hc, ha), Rel(sc, sa))));
            Check("가구 떼기: 선반이 잠든 칸으로 연료 · 열용량이 옮겨 간다 (그 칸 · 몫 그대로) — 옮김마다 열용량 · 고체 열 에너지 · 저장 칸 합이 같다 (1e-12)",
                moved && Rel(cs1, cs0) < 1e-12 && Rel(store1, store0) < 1e-12 && worst < 1e-12,
                $"선반 칸 {at} (옮김 {moved}) · 틱 지나 열용량 {Rel(cs1, cs0):0.0e0} · 저장 {Rel(store1, store0):0.0e0} · 옮김마다 가장 큰 어긋남 {worst:0.0e0}");
            // 탁자: 그 자리에서 뜯으면 (놓였다) 잠들며 다시 앉는다 — 몸 · 칸 · 연료가 처음대로
            double mBefore = st.Bodies[0].Mass;
            var (cs2, heat2, store2) = Sums();
            int tid = st.TearLoose(s, table, 0.05, 0, 0, 0, "밀린");
            for (int k = 0; k < 200 && st.IndexOf(tid) >= 0; k++) s.Step();
            var (cs3, heat3, store3) = Sums();
            Check("가구 떼기: 놓인 탁자는 제자리 0.2m · 10° 안에서 잠들면 다시 앉는다 — 물체가 없어지고 몸 질량 · 무게중심 · 칸 합이 그대로 돌아온다",
                st.IndexOf(tid) < 0 && st.FurnPlaced[table] != 0 && st.FurnProp[table] < 0 && Rel(st.Bodies[0].Mass, mBefore) < 1e-12 && Gap(0) < 1e-9 && Rel(cs3, cs2) < 1e-12 && Rel(store3, store2) < 1e-12,
                $"물체 {st.IndexOf(tid)} · 제자리 {st.FurnPlaced[table]} · 몸 {mBefore:0.000} → {st.Bodies[0].Mass:0.000} · 어긋남 {Gap(0):0.0e0} · 칸 합 {Rel(cs3, cs2):0.0e0} · {Rel(heat3, heat2):0.0e0} · {Rel(store3, store2):0.0e0}");
            // 찍고 되살려 20틱: 뜯긴 선반 (칸 질량 · 연료 자리)이 그대로
            var snap = s.Save();
            var r = new Sim(s.Setup); r.Load(snap);
            r.Get<Field>().Adiabatic = true;   // 시험 설정은 찍지 않는다
            s.Run(20); r.Run(20);
            Check("가구 떼기: 뜯긴 가구가 있는 배를 찍고 되살려 20틱 — 바이트 같음 (칸 질량을 다시 세운다)", s.Save().AsSpan().SequenceEqual(r.Save()), $"{s.Hash():x16} / {r.Hash():x16}");
        }

        // ── 찍고 되살린 직후 (첫 틱 전) 틱 사이 요청: 같은 덜컹 · 같은 폭풍에 두 세계가 같게 뜯는다 — 바이트 같음 (사실 · 원인을 남길 세계를 되살릴 때도 잇는다) ──
        {
            const string Wall = "\n#############\n#.....ss....#\n#...........#\n#...........#\n#.........t.#\n#############";
            var s = PropWorld(1, 9, Wall, new Dictionary<char, string> { ['s'] = "Shelf", ['t'] = "Table" });
            var st = s.Get<Structure>(); var L = s.Layout;
            int shelf = L.Furniture.FindIndex(x => x.Type == "Shelf"), table = L.Furniture.FindIndex(x => x.Type == "Table");
            st.EnsureBody();
            s.Step();
            // 덜컹 2m/s (2g — 놓인 탁자는 밀리고 선반 9g는 그대로)
            var r = new Sim(s.Setup); r.Load(s.Save());
            foreach (var x in new[] { s, r }) { var t = x.Get<Structure>(); var o = t.Bodies[0]; t.Push(0, o.X, o.Y, o.Mass * 2, 0, x.Causes.Add(CauseKind.Setup, "다른 배와 부딪혔다", x.Tick)); }
            int jolt0 = st.FurnPlaced[table], jolt1 = r.Get<Structure>().FurnPlaced[table];
            s.Run(2); r.Run(2);
            bool same0 = s.Save().AsSpan().SequenceEqual(r.Save());
            // 폭풍: 선반에서 세 칸 떨어진 곳에 첨두 35kPa (규칙 '볼트'와 같다 — 선반이 뜯긴다)
            double w3 = 1;
            { double lo = 0.01, hi = 5; for (int k = 0; k < 60; k++) { double mid = (lo + hi) / 2; if (Structure.BlastKPa(3 / mid) < 35) lo = mid; else hi = mid; } w3 = (lo + hi) / 2; }
            int near = L.Furniture[shelf].Cells.Min(), boom = L.Index(L.X(near) - 3, L.Y(near));
            var r2 = new Sim(s.Setup); r2.Load(s.Save());
            foreach (var x in new[] { s, r2 }) x.Get<Structure>().Blast(x, boom, w3 * w3 * w3 * Structure.TntJ, x.Causes.Add(CauseKind.Setup, "배터리가 터졌다", x.Tick));
            int blown0 = st.FurnPlaced[shelf], blown1 = r2.Get<Structure>().FurnPlaced[shelf];
            s.Run(2); r2.Run(2);
            Check("가구 떼기: 찍고 되살린 직후 (첫 틱 전) 같은 덜컹 (2m/s) · 같은 폭풍 (35kPa)에 두 세계가 같게 뜯는다 — 2틱 뒤 바이트 같음 (예전엔 되살린 세계의 탁자 · 선반이 붙어 있었다)",
                jolt0 == 0 && jolt1 == 0 && same0 && blown0 == 0 && blown1 == 0 && s.Save().AsSpan().SequenceEqual(r2.Save()),
                $"덜컹 — 탁자 붙음 {jolt0} / {jolt1} · 같음 {same0} / 폭풍 — 선반 붙음 {blown0} / {blown1} · {s.Hash():x16} / {r2.Hash():x16}");
        }

        // ── 2톤 아래 덩어리의 설비 (PH10 — §5-4): 냉각 펌프 한 칸을 둘레에서 떼어 내면 그 설비는 그 꼴의 물체가 되어 우주로 (질량 = 설비 · 점 알갱이가 아니다) ──
        {
            const string art = "\n###########\n#.........#\n#.........#\n#....p....#\n#.........#\n#.........#\n###########";
            var lay = Worlds.Ascii(art, new Dictionary<char, string> { ['p'] = "CoolantPump" });
            var s = FieldStructure(lay, 6);
            var st = s.Get<Structure>();
            s.Run(3);
            int pc = lay.Furniture[0].Cells[0];
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dy != 0) st.Destroy(s, lay.Index(lay.X(pc) + dx, lay.Y(pc) + dy), 0);
            long spaced0 = st.PropSpaced;
            st.Track = new MotionTrack();
            var lost = new List<Prop>();
            for (int k = 0; k < 3; k++) { s.Step(); lost.AddRange(st.Track.Gone.Where(x => x.Kind == 1)); }
            double kg = st.FurnitureKg(0);
            var gone = s.Facts.Since(0).Where(x => x.Kind == FactKind.TornLoose).ToList();
            bool whole = lost.Count == 1 && Rel(lost[0].Kg, kg) < 1e-6 && lost[0].What == 0 && !st.Shards.Any(x => Math.Abs(x.Kg - kg) < 1e-3);
            Check("설비 덩어리: 떨어져 나간 칸의 냉각 펌프는 그 꼴의 물체 (Kind 1 · 질량 = 설비)가 되어 우주로 — 점 알갱이가 아니다 (예전엔 145kg 알갱이 하나)",
                st.FurnPlaced[0] == 0 && st.PropSpaced > spaced0 && whole && gone.Count == 1,
                $"설비 {kg:0}kg · 제자리 {st.FurnPlaced[0]} · 우주로 간 가구 물체 {lost.Count} ({(lost.Count > 0 ? lost[0].Kg : 0):0}kg) · 뜯김 사실 {gone.Count}");
        }
    }
}
