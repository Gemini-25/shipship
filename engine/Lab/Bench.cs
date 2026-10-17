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

    /// <summary>배 하나 · 엔진마다 한 틱 시간 (--profile 배 사람 로봇 [틱]) — 시험이 많이 쓰는 배가 어디에 시간을 쓰나.</summary>
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

    private static void ProfileShip(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--profile").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        int people = rest.Length > 1 ? int.Parse(rest[1]) : 6, robots = rest.Length > 2 ? int.Parse(rest[2]) : 2, ticks = rest.Length > 3 ? int.Parse(rest[3]) : 3000;
        if (args.Contains("--one")) Par.Threads = 1;
        var make = Stopwatch.StartNew();
        var s = ShipWorld.Crewed(ship, 3, people, robots);
        Console.WriteLine($"  만들기 {make.Elapsed.TotalMilliseconds:0}ms");
        s.Run(100);
        s.Measure = true; s.LastMs.Clear();
        Field.Prof = new(); Network.Prof = new();
        var sw = Stopwatch.StartNew();
        s.Run(ticks);
        double ms = sw.Elapsed.TotalMilliseconds / ticks;
        Console.WriteLine("  장 안: " + string.Join(" · ", Field.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        Console.WriteLine("  망 안: " + string.Join(" · ", Network.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        Field.Prof = null; Network.Prof = null;
        Console.WriteLine($"{ship} 사람 {people} · 로봇 {robots} · 코어 {Par.Threads}: 틱마다 {ms:0.000}ms (하루 = {ms * Time.TicksPerDay / 1000:0.0}초)");
        Console.WriteLine("  " + string.Join(" · ", s.LastMs.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
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
