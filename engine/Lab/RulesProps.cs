using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>물체 규칙 시험 (docs/PHYSICS.md §10 — 묶음 '물체 그림' …): 작은 판 · 몇 장.</summary>
public static partial class Program
{
    private static void PropRules()
    {
        if (Do("물체 그림"))
        {
            Console.WriteLine("\n[물체 그림]");
            StackRules();
        }
    }

    /// <summary>
    /// 쌓아 그린 꼴 (PH2): 탁자 · 소화기 · 식량 상자 · 우주복을 각 단 0~63으로 (32 · 16도트 둘 다) — 불투명 도트 수가 단 0의 ±10% 안 · 단 0과 8 (45°)이 다르다 ·
    /// 캐시에 2만 장을 넣어도 32MB 안이고 가장 오래 안 쓴 것부터 나간다.
    /// </summary>
    private static void StackRules()
    {
        var t = Tables.Default;
        static int Opaque(Img im) { int n = 0; for (int i = 3; i < im.Px.Length; i += 4) if (im.Px[i] > 0) n++; return n; }
        var forms = new[] { DotForge.Make(t, "Table", Facing.S, 2, 2), DotForge.Make(t, "소화기"), DotForge.Make(t, "식량 상자"), DotForge.Make(t, "우주복") };
        // 32도트 (scale 1)와 넓은 보기 16도트 (scale 2) 둘 다 — 넓은 보기에서도 돌 때 크기가 줄었다 늘었다 하지 않는다
        foreach (int sc in new[] { 1, 2 })
        {
            var spread = new List<string>(); bool ok = true, differ = true;
            foreach (var f in forms)
            {
                int n0 = Opaque(DotStack.Draw(f, 0, sc).Img), lo = n0, hi = n0;
                for (int k = 1; k < DotStack.Steps; k++) { int n = Opaque(DotStack.Draw(f, k, sc).Img); lo = Math.Min(lo, n); hi = Math.Max(hi, n); }
                ok &= n0 > 0 && lo >= 0.9 * n0 && hi <= 1.1 * n0;
                spread.Add($"{f.Name} {n0} ({(n0 > 0 ? (double)lo / n0 : 0):0.00}~{(n0 > 0 ? (double)hi / n0 : 0):0.00})");
                var a = DotStack.Draw(f, 0, sc).Img; var b = DotStack.Draw(f, 8, sc).Img;
                differ &= a.W != b.W || a.H != b.H || !a.Px.AsSpan().SequenceEqual(b.Px);
            }
            string at = sc == 1 ? "32도트" : "16도트 (넓은 보기)";
            Check($"쌓아 그린 꼴 {at} — 각 단 0~63의 불투명 도트 수가 단 0의 ±10% 안 (탁자 · 소화기 · 식량 상자 · 우주복)", ok, string.Join(" · ", spread));
            Check($"쌓아 그린 꼴 {at} — 단 0과 단 8 (45°)의 그림이 다르다 (넷 모두)", differ && ok);
            // 겉 색: 소화기는 빨간 몸 속에 검은 기둥 (꼭지 + 속 — 바닥부터 꼭대기까지)이 있다 — 어느 단에서도 속 기둥이 앞면을 칠하지 않는다
            static double Red(Img im) { int n = 0, r = 0; for (int i = 0; i < im.Px.Length; i += 4) if (im.Px[i + 3] > 0) { n++; if (im.Px[i] > 2 * im.Px[i + 1] && im.Px[i] > 60) r++; } return n > 0 ? (double)r / n : 0; }
            var ext = forms[1];
            double redLo = Enumerable.Range(0, DotStack.Steps).Min(k => Red(DotStack.Draw(ext, k, sc).Img));
            Check($"쌓아 그린 꼴 {at} — 소화기는 단마다 빨간 도트가 그림의 75% 넘게 (속의 검은 기둥이 앞면을 칠하지 않는다)", redLo > 0.75, $"가장 적은 단 {redLo:0.00}");
        }
        // 캐시: 64×64 그림 (16KB) 2만 장 = 320MB를 넣는다 — 32MB 안 · 오래 안 쓴 것부터 나간다 (가끔 꺼내 본 것은 남는다)
        var cache = new DotStackCache();
        var form = forms[0];
        DotStackCache.Key K(int i) => new(form, 0, 0, 0, (byte)(i & 63), 1, (uint)i);
        long peak = 0; bool keptTouched = true;
        for (int i = 0; i < 20000; i++)
        {
            cache.Put(K(i), new DotStack.Drawn(new Img(64, 64), 32, 48));
            if (i >= 100 && i % 100 == 0) keptTouched &= cache.TryGet(K(7), out _);   // 7번은 자주 꺼내 본다
            peak = Math.Max(peak, cache.Bytes);
        }
        bool newest = cache.TryGet(K(19999), out _) && cache.TryGet(K(19000), out _), oldest = !cache.TryGet(K(0), out _) && !cache.TryGet(K(8), out _) && !cache.TryGet(K(10000), out _);
        Check("쌓은 그림 캐시 — 2만 장 (320MB)을 넣어도 32MB 안 · 오래 안 쓴 것부터 나가고 자주 꺼낸 것 · 새것은 남는다", peak <= 32L << 20 && cache.Count > 1000 && newest && oldest && keptTouched,
            $"가장 많을 때 {peak / 1048576.0:0.0}MB · {cache.Count}장 · 새것 {(newest ? "있음" : "없음")} · 옛것 {(oldest ? "나감" : "남음")} · 자주 본 것 {(keptTouched ? "남음" : "나감")}");
    }
}
