using System.Diagnostics;
using System.Text;
using ShipEngine;

namespace Lab;

public static partial class Program
{
    /// <summary>큰 시험 배: 방 (cols × rows)개, 방마다 8×6 · 사이 문 · 가운데 통로.</summary>
    internal static string BigShip(int cols, int rows)
    {
        int rw = 9, rh = 7;
        int W = cols * rw + 1, H = rows * rh + 1;
        var g = new char[H, W];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) g[y, x] = '.';
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            if (x % rw == 0 || y % rh == 0) g[y, x] = '#';
        for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                int x0 = cx * rw, y0 = cy * rh;
                if (cx + 1 < cols) g[y0 + rh / 2, x0 + rw] = 'D';
                if (cy + 1 < rows) g[y0 + rh, x0 + rw / 2] = (cx + cy) % 3 == 0 ? 'P' : 'D';
            }
        var sb = new StringBuilder();
        for (int y = 0; y < H; y++) { for (int x = 0; x < W; x++) sb.Append(g[y, x]); sb.Append('\n'); }
        return sb.ToString();
    }

    /// <summary>--fingerprint: 배 몇 척을 몇 분 돌린 세계 지문 — 속도만 고친 뒤 결과가 한 비트도 안 바뀌었는지 (고치기 전과 견준다).</summary>
    private static void Fingerprints()
    {
        foreach (var (ship, people, robots, ticks) in new[] { ("Didim", 6, 2, 3000), ("Salpim", 12, 4, 1500), ("Saetteul", 40, 10, 600), ("Hannuri", 100, 30, 300) })
        {
            var s = ShipWorld.Crewed(ship, 7, people, robots);
            s.Run(ticks);
            Console.WriteLine($"{ship} {people}/{robots} {ticks}틱 {s.Hash():x16}");
        }
    }

    /// <summary>
    /// --budget: 엔진별 시간 예산 (docs/DEPTH.md ① — CI가 따로 잰다). 새터호 40명 · 로봇 10은 한 틱 4ms 안 · 엔진마다 제 몫 안 · 한누리호 100명 · 로봇 30은 9ms 안.
    /// 세 번 재어 가장 빠른 것을 쓴다 (같은 기계의 다른 일에 흔들리지 않게). 넘으면 1로 끝난다 (CI 빨강).
    /// </summary>
    private static void Budget()
    {
        var share = new Dictionary<string, double>
        {
            ["장"] = 1.8, ["마음"] = 1.0, ["망"] = 0.6, ["구조"] = 0.4, ["화학"] = 0.4, ["주컴퓨터"] = 0.4,
        };
        const double Other = 0.15; // 나머지 엔진은 저마다
        (double total, Dictionary<string, double> per) Measure(string ship, int people, int robots, int ticks)
        {
            var s = ShipWorld.Crewed(ship, 7, people, robots);
            s.Run(300); // 데우기 (처음 길찾기 · 계획)
            (double total, Dictionary<string, double> per) best = (double.MaxValue, new());
            for (int k = 0; k < 3; k++)
            {
                s.Measure = true; s.LastMs.Clear();
                var sw = Stopwatch.StartNew();
                s.Run(ticks);
                double ms = sw.Elapsed.TotalMilliseconds / ticks;
                if (ms < best.total) best = (ms, s.LastMs.ToDictionary(kv => kv.Key, kv => kv.Value / ticks));
            }
            s.Measure = false;
            return best;
        }
        Console.WriteLine($"\n[① 시간 예산 — 코어 {Par.Threads}]");
        var (t, per) = Measure("Saetteul", 40, 10, 300);
        Console.WriteLine("  " + string.Join(" · ", per.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0.000}/{share.GetValueOrDefault(kv.Key, Other):0.00}")));
        var over = per.Where(kv => kv.Value > share.GetValueOrDefault(kv.Key, Other)).Select(kv => $"{kv.Key} {kv.Value:0.00}ms > {share.GetValueOrDefault(kv.Key, Other):0.00}").ToList();
        Check("시간 예산: 새터호 40명 · 로봇 10 — 한 틱 4ms 안 (60프레임에 넉넉히)", t <= 4.0, $"{t:0.00}ms/틱 (하루 = {t * Time.TicksPerDay / 1000:0}초)");
        Check("시간 예산: 엔진마다 제 몫 안 (장 1.8 · 마음 1.0 · 망 0.6 · 구조 · 화학 · 주컴퓨터 0.4 · 나머지 0.15ms)", over.Count == 0, over.Count == 0 ? "모두 안" : string.Join(" · ", over));
        var (th, _) = Measure("Hannuri", 100, 30, 150);
        Check("시간 예산: 한누리호 100명 · 로봇 30 — 한 틱 9ms 안", th <= 9.0, $"{th:0.00}ms/틱");
    }

    /// <summary>배 하나 · 엔진마다 한 틱 시간 (--profile 배 사람 로봇 [틱]) — 시험이 많이 쓰는 배가 어디에 시간을 쓰나.</summary>
    private static void ProfileShip(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--profile").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        int people = rest.Length > 1 ? int.Parse(rest[1]) : 6, robots = rest.Length > 2 ? int.Parse(rest[2]) : 2, ticks = rest.Length > 3 ? int.Parse(rest[3]) : 3000;
        if (args.Contains("--one")) Par.Threads = 1;
        var make = Stopwatch.StartNew();
        var s = ShipWorld.Crewed(ship, 3, people, robots);
        Console.WriteLine($"  만들기 {make.Elapsed.TotalMilliseconds:0}ms (처음 — 표 읽기 · JIT 함께)");
        Console.WriteLine($"  도면 {s.Layout.W}×{s.Layout.H} = {s.Layout.N}칸 · 바깥 아닌 칸 {Enumerable.Range(0, s.Layout.N).Count(c => s.Layout.Kind[c] != CellKind.Outside)} · 구역 {s.Get<Field>().Regions.Count}");
        if (args.Contains("--make"))   // 배 만들기 단계별 (두 번째부터 — 시험이 배를 만들 때 드는 값)
        {
            for (int k = 0; k < 3; k++)
            {
                var w = Stopwatch.StartNew();
                var lay = Layout.Ship(ship, Tables.Default); double a = w.Elapsed.TotalMilliseconds;
                var sim = new Sim(ShipWorld.Setup(lay, 3)); double b = w.Elapsed.TotalMilliseconds;
                var f = sim.Get<Field>(); f.FillAir(); double c = w.Elapsed.TotalMilliseconds;
                Furnish.Apply(f); double d = w.Elapsed.TotalMilliseconds;
                sim.Get<Life>().Plant(sim.Get<Network>()); double e = w.Elapsed.TotalMilliseconds;
                sim.Get<Mind>().Crew(sim, people, robots, 0); double g = w.Elapsed.TotalMilliseconds;
                sim.Run(1); double h = w.Elapsed.TotalMilliseconds;
                Console.WriteLine($"  만들기 {k + 2}번째 {h:0}ms — 도면 {a:0} · 엔진 짜기 {b - a:0} · 공기 {c - b:0} · 꾸미기 {d - c:0} · 심기 {e - d:0} · 사람 {g - e:0} · 첫 틱 {h - g:0}");
            }
        }
        s.Run(100);
        s.Measure = !args.Contains("--nolap"); s.LastMs.Clear();
        if (args.Contains("--alloc")) { s.Measure = true; s.LastAlloc = new(); Mind.ProfAlloc = new(); }   // 엔진마다 · 마음 안 단계마다 새로 잡는 것 (재기 자체가 느리다)
        bool laps = !args.Contains("--nolap");   // --nolap: 단계별 재기를 끈다 (재기 자체가 25%쯤 먹는다 — tools/hot.sh 표본 수집은 끄고 잰다)
        if (laps) { Field.Prof = new(); Network.Prof = new(); Mind.Prof = new(); Prof.D = new(); }
        long alloc0 = GC.GetTotalAllocatedBytes(true); int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        var pause0 = GC.GetTotalPauseDuration();
        var sw = Stopwatch.StartNew();
        s.Run(ticks);
        double ms = sw.Elapsed.TotalMilliseconds / ticks;
        Console.WriteLine($"  할당 틱마다 {(GC.GetTotalAllocatedBytes(true) - alloc0) / (double)ticks / 1024:0.0}KB · 수거 {GC.CollectionCount(0) - gc0}/{GC.CollectionCount(1) - gc1}/{GC.CollectionCount(2) - gc2} · 멈춤 {(GC.GetTotalPauseDuration() - pause0).TotalMilliseconds / ticks:0.000}ms/틱");
        if (laps) {
        Console.WriteLine("  장 안: " + string.Join(" · ", Field.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        Console.WriteLine("  망 안: " + string.Join(" · ", Network.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        Console.WriteLine("  마음 안: " + string.Join(" · ", Mind.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        foreach (var g in Prof.D.GroupBy(kv => kv.Key.Split(':')[0]))
            Console.WriteLine($"  {g.Key} 안: " + string.Join(" · ", g.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key.Split(':')[1]} {kv.Value / ticks:0.000}")));
        }
        Field.Prof = null; Network.Prof = null; Mind.Prof = null; Prof.D = null;
        Console.WriteLine($"{ship} 사람 {people} · 로봇 {robots} · 코어 {Par.Threads}: 틱마다 {ms:0.000}ms (하루 = {ms * Time.TicksPerDay / 1000:0.0}초)");
        if (args.Contains("--clone"))   // 복제 한 번에 드는 것 (연출 미리 보기 · 주컴퓨터 상상)
        {
            for (int k = 0; k < 3; k++)
            {
                long m0 = GC.GetAllocatedBytesForCurrentThread(); var w = Stopwatch.StartNew();
                var blank = new Sim(s.Setup); long m1 = GC.GetAllocatedBytesForCurrentThread(); double t1 = w.Elapsed.TotalMilliseconds;
                var bytes = s.Save(); long m2 = GC.GetAllocatedBytesForCurrentThread(); double t2 = w.Elapsed.TotalMilliseconds;
                blank.Load(bytes); long m3 = GC.GetAllocatedBytesForCurrentThread(); double t3 = w.Elapsed.TotalMilliseconds;
                {
                    var parts = new List<string>();
                    long a0 = GC.GetAllocatedBytesForCurrentThread(); var pw = Stopwatch.StartNew();
                    void P(string nm) { long a1 = GC.GetAllocatedBytesForCurrentThread(); parts.Add($"{nm} {(a1 - a0) / 1048576.0:0.0}MB {pw.Elapsed.TotalMilliseconds:0.0}ms"); a0 = a1; pw.Restart(); }
                    var e = new Sim(new SimSetup(s.Layout, 3, _ => { })); P("빈 세계");
                    var ff = new Field(e.Layout, e.Tables); P("장"); var ch = new Chemistry(ff); P("화학"); var so = new Sound(ff); P("소리"); var li = new Light(ff); P("빛");
                    var stt = new Structure(ff); P("구조"); var ou = new Outside(ff, 3); P("바깥"); var ne = new Network(ff, 3); P("망"); var se = new Sensors(e.Tables); P("감지");
                    var lf = new Life(ff); P("생명"); var mi = new Mind(ff); P("마음"); var soc = new Society(ff.L); P("사회"); var sm = new ShipMind(ff); P("주컴퓨터"); var wk = new Work(ff.L, ff.T); P("일");
                    Console.WriteLine("  새 세계 엔진마다: " + string.Join(" · ", parts));
                }
                long c0 = GC.GetAllocatedBytesForCurrentThread(); var cw = Stopwatch.StartNew();
                s.Clone();
                Console.WriteLine($"  복제 {k + 1}: Clone() {(GC.GetAllocatedBytesForCurrentThread() - c0) / 1048576.0:0.0}MB {cw.Elapsed.TotalMilliseconds:0}ms");
                Console.WriteLine($"  복제 {k + 1}: 새 세계 {(m1 - m0) / 1048576.0:0.0}MB {t1:0}ms · 찍기 {(m2 - m1) / 1048576.0:0.0}MB {t2 - t1:0}ms (크기 {bytes.Length / 1048576.0:0.0}MB) · 읽기 {(m3 - m2) / 1048576.0:0.0}MB {t3 - t2:0}ms");
            }
        }
        var pm = s.Get<Mind>();
        Console.WriteLine($"  길찾기: 부름 {pm.PathCalls} · 못 찾음 {pm.PathFails} · 펼친 칸 {pm.PathExpanded} (부름마다 {pm.PathExpanded / Math.Max(1, pm.PathCalls)})");
        Console.WriteLine("  " + string.Join(" · ", s.LastMs.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        if (Mind.ProfAlloc != null) { Console.WriteLine("  마음 안 할당 KB/틱: " + string.Join(" · ", Mind.ProfAlloc.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value / 1024.0 / ticks:0.0}"))); Mind.ProfAlloc = null; }
        if (s.LastAlloc != null) Console.WriteLine("  할당 엔진마다 KB/틱: " + string.Join(" · ", s.LastAlloc.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / 1024.0 / ticks:0.0}")));
    }

    private static void Bench()
    {
        Console.WriteLine("성능\n");
        var l = Worlds.Ascii(BigShip(18, 12)); // 약 163×85 = 1.4만 칸 · 방 216
        foreach (int threads in new[] { 1, Math.Max(2, Environment.ProcessorCount) })
        {
            Par.Threads = threads;
            var s = Worlds.FieldOnly(l, 1, f =>
            {
                Stir(f);
                for (int d = 0; d < f.L.Doors.Count; d += 3) f.SetDoor(d, 1f);
                f.SetHole(Worlds.At(f.L, 0, 3), 0.05f);
            });
            s.Run(20);
            Field.Prof = new();
            var sw = Stopwatch.StartNew();
            const int Ticks = 500;
            s.Run(Ticks);
            double ms = sw.Elapsed.TotalMilliseconds / Ticks;
            var f = s.Get<Field>();
            Console.WriteLine("    " + string.Join(" · ", Field.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / Ticks:0.000}")));
            Field.Prof = null;
            Console.WriteLine($"  장(기체 · 열 · 물) {f.Cells}칸 · 구역 {f.Regions.Count} · 틈 {f.Links.Count} · 코어 {threads}: 틱마다 {ms:0.000}ms (게임 1시간 = {ms * Time.TicksPerHour / 1000:0.00}초)");
        }
        // 평시: 구멍 없음 · 문 몇 개 열림 · 100명이 숨 쉰다 (틱마다 산소를 빼고 이산화탄소 · 수증기를 넣는다)
        foreach (int threads in new[] { 1, Math.Max(2, Environment.ProcessorCount) })
        {
            Par.Threads = threads;
            var s = Worlds.FieldOnly(l, 1, f => { for (int d = 0; d < f.L.Doors.Count; d += 3) f.SetDoor(d, 1f); });
            var f = s.Get<Field>();
            var floor = Enumerable.Range(0, f.Cells).Where(i => f.L.Kind[i] == CellKind.Floor).ToArray();
            var rng = Rng.Stream(1, "bench");
            var people = Enumerable.Range(0, 100).Select(_ => floor[rng.Range(0, floor.Length)]).ToArray();
            void Breathe()
            {
                foreach (int c in people)
                {
                    f.RemoveGas(c, f.T.gO2, 0.0006);          // 약 0.25 L/분 × 2.4초
                    f.AddGas(c, f.T.gCO2, 0.0005, 307);
                    f.AddGas(c, f.T.gH2O, 0.0004, 307);
                    f.AddHeat(c, 100 * Time.SecondsPerTick); // 사람 100W
                }
            }
            for (int i = 0; i < 20; i++) { Breathe(); s.Step(); }
            Field.Prof = new();
            var sw = Stopwatch.StartNew();
            const int Ticks = 1000;
            for (int i = 0; i < Ticks; i++) { Breathe(); s.Step(); }
            double ms = sw.Elapsed.TotalMilliseconds / Ticks;
            Console.WriteLine("    " + string.Join(" · ", Field.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / Ticks:0.000}")));
            Field.Prof = null;
            Console.WriteLine($"  평시 100명 숨 · {f.Cells}칸 · 코어 {threads}: 틱마다 {ms:0.000}ms (게임 1시간 = {ms * Time.TicksPerHour / 1000:0.00}초)");
        }
        // 100명 · 로봇 30이 탄 큰 배(한누리호): 마음 엔진 · 전체
        {
            Par.Threads = Math.Max(2, Environment.ProcessorCount);
            var s = ShipWorld.Crewed("hannuri", 1, 100, 30);
            s.Run(50);
            s.Measure = true;
            s.LastMs.Clear();
            var sw = Stopwatch.StartNew();
            const int Ticks = 1500;
            s.Run(Ticks);
            double ms = sw.Elapsed.TotalMilliseconds / Ticks;
            var m = s.Get<Mind>();
            Console.WriteLine("    " + string.Join(" · ", s.LastMs.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / Ticks:0.000}")));
            var sm = s.Get<ShipMind>();
            if (Environment.GetEnvironmentVariable("BENCH_LOG") == "1") foreach (var (t, txt) in sm.Log) Console.WriteLine($"    {Time.Clock(t)} {txt}");
            Console.WriteLine($"  한누리호 주컴퓨터: 사건 {sm.Incidents.Count(x => x.Active != 0)} · 상상 {sm.Imaginations.Count / 2}번 · 경보 {sm.Alarms} · 맡김 {sm.Assignments}");
            Console.WriteLine($"  한누리호 사람 {m.Agents.Count(a => a.IsPerson)} · 로봇 {m.Agents.Count(a => !a.IsPerson)} · 코어 {Par.Threads}: 틱마다 {ms:0.000}ms (마음 {s.LastMs.GetValueOrDefault("마음") / Ticks:0.000}ms) · 게임 1시간 = {ms * Time.TicksPerHour / 1000:0.00}초");
        }
        Par.Threads = Math.Max(1, Environment.ProcessorCount);
    }

    private static void Ships()
    {
        foreach (var key in Layout.ShipKeys())
        {
            var l = Layout.Ship(key, Tables.Default);
            Console.WriteLine($"  {l.Key} {l.Name} — {l.W}×{l.H} · 방 {l.Rooms.Count} · 문 {l.Doors.Count} · 설비 {l.Furniture.Count}");
        }
    }
}
