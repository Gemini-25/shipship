using System;

namespace ShipSim.TexGen;

// v16.5 상태 겹치기 7장: RGB = 다 덮였을 때의 색 · A = 드러나는 순서 (1 이 가장 먼저).
// 화면 셰이더가 양(0~1)만큼 A 가 높은 곳부터 드러낸다 → 물웅덩이가 번지고, 녹이 이음매에서 퍼지고, 먼지가 가장자리부터 쌓인다.
// A 는 순위로 고르게 펴서(같은 값은 칸 번호 순) 양 = 덮인 넓이가 된다. 모두 감싸 이어진다 (칸 경계를 넘어 번져도 이음매가 없다).
public static partial class Gen
{
    /// <summary>원시 마스크를 순위로 0~1 에 고르게 편다 (결정론: 같은 값은 칸 번호 순).</summary>
    private static void Equalize(Pix p, float[] raw)
    {
        int n = raw.Length;
        var idx = new int[n];
        for (int i = 0; i < n; i++) idx[i] = i;
        Array.Sort(idx, (a, b) => { int c = raw[a].CompareTo(raw[b]); return c != 0 ? c : a.CompareTo(b); });
        for (int r = 0; r < n; r++) p.A[idx[r]] = (r + 0.5f) / n;
    }

    private static Pix Overlay(Func<int, int, (Rgb c, float mask)> f)
    {
        var p = new Pix(S, S);
        var raw = new float[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            int i = p.I(x, y);
            var (c, m) = f(x, y);
            p.R[i] = Math.Clamp(c.R, 0f, 1f); p.G[i] = Math.Clamp(c.G, 0f, 1f); p.B[i] = Math.Clamp(c.B, 0f, 1f);
            raw[i] = m;
        }
        Equalize(p, raw);
        return p;
    }

    /// <summary>칸(64px) 가운데에서의 거리 0(가운데)~1(가장자리) — 상자 거리.</summary>
    private static float CellEdge(int x, int y)
    {
        float lx = MathF.Abs((x & 63) + 0.5f - 32f) / 32f, ly = MathF.Abs((y & 63) + 0.5f - 32f) / 32f;
        return MathF.Max(lx, ly);
    }

    // ───────────────────────── 닳음: 반들반들한 맨 금속 · 결 방향 잔긁힘 (가운데 길부터) ─────────────────────────
    public static Pix OvWear() => Overlay((x, y) =>
    {
        float u = U(x), v = U(y);
        float streakH = Noise.Stretch(u, v, 6, 160, 300), streakV = Noise.Stretch(u, v, 160, 6, 301);
        float fine = Noise.H01(x, y, 302);
        float g = 0.78f + 0.14f * MathF.Max(streakH, streakV) + 0.06f * fine;
        var c = new Rgb(g, g * 1.01f, g * 1.04f);
        // 신발 끝에 쓸린 짧은 검은 줄
        if (Noise.H01(x / 3, y, 303) < 0.012f) c = c * 0.35f;
        float mask = 0.22f * (1f - CellEdge(x, y)) + 0.62f * Noise.Fbm(u, v, 4, 4, 304) + 0.12f * MathF.Max(streakH, streakV) + 0.04f * fine;
        return (c, mask);
    });

    // ───────────────────────── 물기: 바닥을 어둡게 적시는 막 · 빛 반사 줄 · 물방울 (낮은 곳 웅덩이부터) ─────────────────────────
    public static Pix OvWet() => Overlay((x, y) =>
    {
        float u = U(x), v = U(y);
        float sheen = Noise.Stretch(u + v * 0.35f, v, 8, 48, 310);
        var c = (Pal.WaterEdge * 0.55f).Lerp(Pal.WaterEdge, 0.6f * Noise.Fbm(u, v, 8, 2, 311));
        if (sheen > 0.7f) c = c.Lerp(Pal.Water, MathF.Min(1f, (sheen - 0.7f) * 4f)); if (sheen > 0.8f) c = c.Lerp(Pal.White, (sheen - 0.8f) * 4f);
        // 맺힌 물방울: 동그란 밝은 점 + 아래 그늘
        int gx = x / 9, gy = y / 9;
        float jx = gx * 9 + 2 + 5 * Noise.H01(gx, gy, 312), jy = gy * 9 + 2 + 5 * Noise.H01(gx, gy, 313);
        float dd = (x + 0.5f - jx) * (x + 0.5f - jx) + (y + 0.5f - jy) * (y + 0.5f - jy);
        if (Noise.H01(gx, gy, 314) < 0.25f && dd < 2.4f) c = dd < 0.7f ? Pal.White : Pal.WaterEdge * 0.7f;
        float mask = Noise.Fbm(u, v, 3, 4, 315) + 0.1f * (1f - CellEdge(x, y));
        return (c, mask);
    });

    // ───────────────────────── 기름: 검갈색 막 · 얇은 막 무지개 (번진 얼룩 · 흘러간 줄) ─────────────────────────
    public static Pix OvOil() => Overlay((x, y) =>
    {
        float u = U(x), v = U(y);
        float film = Noise.Fbm(u, v, 6, 3, 320);
        var c = Pal.Oil * (0.8f + 0.5f * film);
        // 얇은 막 무지개: 잡음 등고선마다 색이 돈다
        float band = Noise.Tri(film * 7f);
        if (band > 0.82f)
        {
            float h = film * 5f;
            var rainbow = new Rgb(0.5f + 0.5f * MathF.Cos(h * 6.283f), 0.5f + 0.5f * MathF.Cos(h * 6.283f - 2.1f), 0.5f + 0.5f * MathF.Cos(h * 6.283f - 4.2f));
            c = c.Lerp(rainbow * 0.55f, (band - 0.82f) * 3.2f);
        }
        if (Noise.H01(x, y, 321) < 0.004f) c = c.Lerp(Pal.White, 0.6f); // 반짝
        float splat = Noise.Fbm(u, v, 4, 3, 322);
        float drip = MathF.Max(0f, 1f - MathF.Abs(((x + (int)(18f * Noise.Fbm(u, v, 4, 2, 323))) & 63) - 20f) / 5f) * Noise.Stretch(u, v, 16, 2, 324);
        return (c, splat + 0.45f * drip);
    });

    // ───────────────────────── 그을음: 검댕이 번진 결 · 가장자리 갈색 (진하기가 고르지 않다) ─────────────────────────
    public static Pix OvSoot() => Overlay((x, y) =>
    {
        float u = U(x), v = U(y);
        float n = Noise.Fbm(u, v, 4, 5, 330);
        float smear = Noise.Stretch(u * 0.7f + v * 0.3f, v, 24, 4, 331);
        var c = Pal.Soot.Lerp(Pal.BurnHalo, 0.35f * (1f - n));
        if (Noise.H01(x, y, 332) < 0.05f) c = c * 0.4f; // 고운 알갱이
        return (c, n + 0.3f * smear);
    });

    // ───────────────────────── 녹: 이음매 · 볼트 자리에서 번지는 주황 · 갈색 · 파인 점 ─────────────────────────
    public static Pix OvRust() => Overlay((x, y) =>
    {
        float u = U(x), v = U(y);
        float n = Noise.Fbm(u, v, 8, 4, 340);
        float t = Noise.Fbm(u, v, 16, 3, 341);
        var c = Pal.RustA.Lerp(Pal.RustB, t).Lerp(Pal.RustHalo, 0.4f * (1f - n));
        if (Noise.H01(x, y, 342) < 0.03f) c = Pal.RustPit; // 파인 점
        if (Noise.Tri(t * 9f) > 0.93f) c = c * 0.7f; // 겹겹이 앉은 층 경계
        // 이음매(칸 가장자리) · 귀퉁이 볼트 자리에서 먼저
        float lx = (x & 63) + 0.5f - 32f, ly = (y & 63) + 0.5f - 32f;
        float bolt = 0f;
        foreach (var (bx, by) in new[] { (-25f, -25f), (25f, -25f), (-25f, 25f), (25f, 25f) })
            bolt = MathF.Max(bolt, 1f - MathF.Sqrt((lx - bx) * (lx - bx) + (ly - by) * (ly - by)) / 14f);
        float mask = 0.6f * n + 0.25f * Noise.Fbm(u, v, 32, 2, 343) + 0.18f * MathF.Max(0f, bolt) + 0.08f * CellEdge(x, y);
        return (c, mask);
    });

    // ───────────────────────── 먼지: 밝은 회갈색 가루 · 보풀 실 · 어두운 알갱이 (발길 없는 가장자리부터) ─────────────────────────
    public static Pix OvDust() => Overlay((x, y) =>
    {
        float u = U(x), v = U(y);
        float n = Noise.Fbm(u, v, 16, 3, 350);
        var c = Pal.Dust.Lerp(Pal.DustLight, n);
        if (Noise.H01(x, y, 351) < 0.035f) c = Pal.DustDark;
        // 보풀 실: 짧게 굽은 밝은 선
        float fib = Noise.Tri(Noise.Fbm(u, v, 32, 2, 352) * 11f);
        if (fib > 0.96f && Noise.Fbm(u, v, 8, 2, 353) > 0.55f) c = Pal.DustLight * 1.05f;
        float mask = 0.15f * CellEdge(x, y) + 0.65f * Noise.Fbm(u, v, 6, 3, 354) + 0.2f * n + 0.05f * Noise.H01(x, y, 355);
        return (c, mask);
    });

    // ───────────────────────── 서리: 흰 막 · 깃털 결정 (가장자리에서 안으로 기어든다) ─────────────────────────
    public static Pix OvFrost() => Overlay((x, y) =>
    {
        float u = U(x), v = U(y);
        float n = Noise.Fbm(u, v, 8, 4, 360);
        var c = Pal.Frost * (0.88f + 0.12f * n);
        // 깃털 결정: 세 방향 줄무늬 잡음의 능선
        float f1 = Noise.Tri(Noise.Fbm(u, v, 16, 2, 361) * 9f + (x + y) * 0.08f);
        float f2 = Noise.Tri(Noise.Fbm(u, v, 16, 2, 362) * 9f + (x - y) * 0.08f);
        float ridge = MathF.Max(f1, f2);
        if (ridge > 0.9f) c = c.Lerp(Pal.FrostCrystal, 1f) * 1.02f;
        else if (ridge < 0.15f) c = c * 0.86f;
        if (Noise.H01(x, y, 363) < 0.006f) c = Pal.White;
        float mask = 0.1f * CellEdge(x, y) + 0.7f * Noise.Fbm(u, v, 4, 4, 364) + 0.15f * n + 0.12f * ridge;
        return (c, mask);
    });
}
