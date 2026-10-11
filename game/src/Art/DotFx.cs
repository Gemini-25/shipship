using System;

namespace ShipSim.Art;

// 도트 효과 — 폭발 불덩이 12장면 · 충격파 고리 · 파편 · 불티 · 연기 덩이 · 칸 불꽃 · 그을음 자국 · 잔해 · 피 웅덩이.
//   폭발은 종류마다 색이 다르다 (연료 주황 · 수소 창백한 파랑 · 배터리 흰 불꽃+회녹 연기 · 아크 청백 · 증기 흰 · 분진 누런).
//   색 단계는 점묘(4×4 바이어)로 이어 붙여 도트답게 계단진다.

public enum DotBlast : byte { Fuel, Hydrogen, Battery, Arc, Steam, Dust }

public static class DotFx
{
    private static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
    private static float Dither(int x, int y) => (Bayer[y & 3, x & 3] + 0.5f) / 16f;

    public static readonly int SmokeGreen = Ramps.Add("#141a14", "#26302a", "#3c4a40", "#58685a", "#7a8a7a", "#a2b0a0");
    public static readonly int FireBlue = Ramps.Add("#0a1430", "#173070", "#2c5cc0", "#5c9cf0", "#a8d4ff", "#e8f6ff", "#ffffff", "#ffffff");
    public static readonly int DustRamp = Ramps.Add("#2a1e0c", "#5a4018", "#8c6828", "#c09a44", "#e8c870", "#fff0b0", "#ffffff", "#ffffff");

    private static int FireOf(DotBlast k) => k switch
    {
        DotBlast.Hydrogen or DotBlast.Arc => FireBlue,
        DotBlast.Dust => DustRamp,
        _ => Ramps.Fire,
    };

    private static int SmokeOf(DotBlast k) => k switch
    {
        DotBlast.Battery => SmokeGreen,
        DotBlast.Steam => Ramps.Bandage,
        _ => Ramps.Smoke,
    };

    /// <summary>폭발 한 장면 (frame 0~11, 크기 size×size). 0~1 섬광 · 2~5 불덩이 · 6~11 연기로 흩어짐.</summary>
    public static Img Blast(DotBlast kind, int frame, int size = 64, int seed = 1)
    {
        var img = new Img(size, size);
        float c = size / 2f;
        float t = frame / 11f;
        int fire = FireOf(kind), smoke = SmokeOf(kind);
        int fireN = Ramps.Count(fire), smokeN = Ramps.Count(smoke);
        // 반지름: 빨리 커졌다가 천천히
        float R = c * (0.25f + 0.75f * (1f - MathF.Pow(1f - MathF.Min(1f, t * 1.6f), 2.2f)));
        if (kind == DotBlast.Hydrogen) R *= 0.9f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float a = MathF.Atan2(dy, dx);
                // 울퉁불퉁한 가장자리 (각도 · 장면마다 다른 혹)
                float bump = 0.18f * MathF.Sin(a * 5 + seed) + 0.12f * MathF.Sin(a * 9 + seed * 2.1f + frame * 0.4f) + 0.08f * MathF.Sin(a * 13 + seed * 0.7f);
                float r = MathF.Sqrt(dx * dx + dy * dy) / (R * (1f + bump));
                if (r > 1f) continue;
                // 안쪽 혹 (연기 덩이 · 불꽃 덩이)
                float lump = 0.5f + 0.5f * MathF.Sin(x * 0.45f + seed) * MathF.Cos(y * 0.5f - seed * 0.3f + frame * 0.3f);
                float heat = (1f - r) * (1.25f - t * 1.35f) + 0.25f * lump - 0.05f;
                if (frame <= 1) heat = 1.2f - r * 0.6f;
                float smokeAmt = MathF.Max(0f, t * 1.4f - 0.15f + r * 0.6f - 0.5f + 0.3f * (1 - lump));
                float dz = Dither(x, y);
                if (kind == DotBlast.Hydrogen) smokeAmt *= 0.25f;
                if (kind == DotBlast.Steam) { heat *= 0.35f; smokeAmt = MathF.Max(smokeAmt, 0.6f + 0.3f * t); }
                if (smokeAmt > heat + 0.05f)
                {
                    // 연기 — 늦은 장면일수록 옅어지고(점묘로 구멍) 가장자리가 어둡다
                    float fade = MathF.Max(0f, (t - 0.55f) * 1.9f) + MathF.Max(0f, r - 0.8f) * 1.5f;
                    if (fade > dz + 0.15f) continue;
                    float tone = (1f - r) * 0.55f + 0.35f * lump - t * 0.25f;
                    int s = Math.Clamp((int)MathF.Floor(tone * (smokeN - 1) + dz * 0.9f), 0, smokeN - 1);
                    img.Set(x, y, Ramps.Get(smoke, s));
                }
                else
                {
                    int s = Math.Clamp((int)MathF.Floor(heat * (fireN - 1) + dz * 0.9f), 1, fireN - 1);
                    img.Set(x, y, Ramps.Get(fire, s));
                }
            }
        if (frame <= 2)
        {
            // 섬광 줄기
            var flash = Ramps.Get(fire, fireN - 1);
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4 + seed;
                float len = c * (frame == 0 ? 0.9f : 0.7f);
                for (int i = 0; i < len; i += 1) img.Blend((int)(c + MathF.Cos(a) * i), (int)(c + MathF.Sin(a) * i), flash, 1f - i / len);
            }
        }
        return img;
    }

    /// <summary>충격파 고리 (반지름 r, 두께 2, 반투명).</summary>
    public static void Ring(Img img, float cx, float cy, float r, float alpha)
    {
        var c = Ramps.Get(Ramps.Bandage, 3);
        for (int y = (int)(cy - r - 2); y <= (int)(cy + r + 2); y++)
            for (int x = (int)(cx - r - 2); x <= (int)(cx + r + 2); x++)
            {
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float e = MathF.Abs(d - r);
                if (e < 1.2f) img.Blend(x, y, c, alpha * (e < 0.6f ? 1f : 0.5f));
            }
    }

    /// <summary>날아가는 파편 · 불티 (중심에서 바깥으로, 꼬리가 남는다).</summary>
    public static void Debris(Img img, float cx, float cy, float reach, int count, int seed, DotBlast kind)
    {
        int fire = FireOf(kind);
        for (int i = 0; i < count; i++)
        {
            float a = DotHash.F(seed, i) * MathF.PI * 2, r = reach * (0.45f + 0.55f * DotHash.F(seed, i, 1));
            float x = cx + MathF.Cos(a) * r, y = cy + MathF.Sin(a) * r;
            bool chunk = (i % 3) == 0;
            if (chunk)
            {
                var col = Ramps.Get(Ramps.Steel, 2 + (i % 3));
                img.Set((int)x, (int)y, col); img.Set((int)x + 1, (int)y, Ramps.Get(Ramps.Steel, 1)); img.Set((int)x, (int)y + 1, Ramps.Get(Ramps.Steel, 1));
                for (int k = 1; k < 4; k++) img.Blend((int)(x - MathF.Cos(a) * k * 1.5f), (int)(y - MathF.Sin(a) * k * 1.5f), Ramps.Get(Ramps.Smoke, 3), 0.5f - k * 0.12f);
            }
            else
            {
                img.Set((int)x, (int)y, Ramps.Get(fire, 6));
                for (int k = 1; k < 4; k++) img.Blend((int)(x - MathF.Cos(a) * k), (int)(y - MathF.Sin(a) * k), Ramps.Get(fire, 5 - k), 0.9f - k * 0.2f);
            }
        }
    }

    /// <summary>칸 하나의 불꽃 (frame으로 날름거림) — 32×40, 아래가 칸 바닥.</summary>
    public static Img FireTile(int frame, float strength, int seed)
    {
        var img = new Img(32, 40);
        int fire = Ramps.Fire, n = Ramps.Count(fire);
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 32; x++)
            {
                float u = (x + 0.5f) / 32f, v = 1f - (y + 0.5f) / 40f; // v: 바닥 0 → 위 1
                float tongues = 0.55f + 0.25f * MathF.Sin(u * 18f + seed + frame * 0.9f) + 0.2f * MathF.Sin(u * 31f - frame * 1.7f + seed * 2);
                float height = strength * tongues;
                float edge = MathF.Min(u, 1f - u) * 3.2f;
                float h = v / MathF.Max(0.05f, height * MathF.Min(1f, edge));
                if (h > 1f) continue;
                float heat = 1.05f - h + 0.15f * MathF.Sin(x * 0.9f + y * 0.7f + frame);
                int s = Math.Clamp((int)(heat * (n - 2) + Dither(x, y) * 1.2f), 2, n - 2);
                img.Set(x, y, Ramps.Get(fire, s));
            }
        return img;
    }

    /// <summary>연기 덩이 (반투명 · 점묘 가장자리).</summary>
    public static void Puff(Img img, float cx, float cy, float r, int shade, float alpha, int ramp = -1)
    {
        int sr = ramp < 0 ? Ramps.Smoke : ramp;
        for (int y = (int)(cy - r); y <= (int)(cy + r); y++)
            for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
            {
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / r;
                if (d > 1f || d > 0.75f && Dither(x, y) < (d - 0.75f) * 4f) continue;
                int s = Math.Clamp(shade + (d < 0.4f && (x - cx) < 0 && (y - cy) < 0 ? 1 : 0) - (d > 0.7f ? 1 : 0), 0, Ramps.Count(sr) - 1);
                img.Blend(x, y, Ramps.Get(sr, s), alpha);
            }
    }

    /// <summary>바닥 그을음 (방사형 줄기) — 며칠 남는 자국.</summary>
    public static void Scorch(Img img, float cx, float cy, float r, int seed)
    {
        var ink = Ramps.Get(Ramps.Char, 0);
        for (int y = (int)(cy - r); y <= (int)(cy + r); y++)
            for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = MathF.Sqrt(dx * dx + dy * dy) / r;
                if (d > 1f) continue;
                float a = MathF.Atan2(dy, dx);
                float streak = 0.5f + 0.5f * MathF.Sin(a * 11 + seed) * MathF.Sin(a * 7 - seed);
                float k = (1f - d) * (0.55f + 0.45f * streak);
                if (k > Dither(x, y) * 0.9f + 0.1f) img.Blend(x, y, ink, MathF.Min(0.85f, k + 0.2f));
            }
    }

    /// <summary>잔해 조각 몇 개 (치울 때까지 남는다).</summary>
    public static void Rubble(Img img, float cx, float cy, float r, int count, int seed)
    {
        for (int i = 0; i < count; i++)
        {
            float a = DotHash.F(seed, i) * MathF.PI * 2, rr = r * DotHash.F(seed, i, 1);
            int x = (int)(cx + MathF.Cos(a) * rr), y = (int)(cy + MathF.Sin(a) * rr);
            int w = 2 + (int)(DotHash.F(seed, i, 2) * 3), h = 1 + (int)(DotHash.F(seed, i, 3) * 2);
            img.Fill(x + 1, y + 1, w, h, Ramps.Get(Ramps.Ink, 0), 0.5f);
            img.Fill(x, y, w, h, Ramps.Get(Ramps.Steel, 2 + (i % 3)));
            img.Set(x, y, Ramps.Get(Ramps.Steel, 5));
        }
    }

    /// <summary>피 웅덩이 · 핏자국 (끌린 자국은 dx, dy 방향으로).</summary>
    public static void Blood(Img img, float cx, float cy, float r, int seed, float trailX = 0, float trailY = 0)
    {
        var dark = Ramps.Get(Ramps.Blood, 1); var mid = Ramps.Get(Ramps.Blood, 2);
        for (int y = (int)(cy - r); y <= (int)(cy + r); y++)
            for (int x = (int)(cx - r * 1.4f); x <= (int)(cx + r * 1.4f); x++)
            {
                float dx = (x + 0.5f - cx) / 1.4f, dy = y + 0.5f - cy, d = MathF.Sqrt(dx * dx + dy * dy) / r;
                d += 0.25f * MathF.Sin(MathF.Atan2(dy, dx) * 5 + seed);
                if (d > 1f) continue;
                img.Blend(x, y, d < 0.6f ? mid : dark, 0.92f);
            }
        img.Set((int)(cx - r * 0.3f), (int)(cy - r * 0.3f), Ramps.Get(Ramps.Blood, 3));
        if (trailX != 0 || trailY != 0)
            for (int i = 0; i < 18; i++)
            {
                float k = i / 18f;
                if (DotHash.F(seed, i) < 0.35f) continue;
                img.Blend((int)(cx + trailX * k), (int)(cy + trailY * k + MathF.Sin(i) * 1.2f), dark, 0.85f - k * 0.5f);
            }
    }
}
