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
