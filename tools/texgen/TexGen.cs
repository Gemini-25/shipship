using System;
using System.Collections.Generic;

namespace ShipSim.TexGen;

// v16.5 절차적 텍스처 생성기 — 바닥재 6 · 벽 3 · 상태 겹치기 7 · 흔적 아틀라스 1 · 입자 1장.
// 팔레트(Pal)는 고정이고 난수는 정수 해시만 쓴다 (System.Random 없음) → 같은 코드 = 같은 그림 = 같은 PNG.
// 바닥 · 벽 · 상태 그림은 256×256 (칸 하나 = 64px, 4×4칸 주기)이고 가장자리가 이어지게 감싸 그린다 (이음매 없음).
// 바닥 · 벽은 높이 + 바탕색 + 광택을 따로 쌓은 뒤 왼쪽 위 빛으로 한 번에 음영을 넣는다 (볼트 · 홈 · 돌기가 입체로).

public readonly record struct Rgb(float R, float G, float B)
{
    public static Rgb Hex(uint h) => new(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f);
    public static Rgb operator *(Rgb a, float k) => new(a.R * k, a.G * k, a.B * k);
    public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    public Rgb Lerp(Rgb b, float t) => new(R + (b.R - R) * t, G + (b.G - G) * t, B + (b.B - B) * t);
}

/// <summary>고정 팔레트 — 색을 바꾸려면 여기만.</summary>
public static class Pal
{
    public static readonly Rgb Metal = Rgb.Hex(0x5f6a78), MetalScrew = Rgb.Hex(0x9aa3ae), MetalScratch = Rgb.Hex(0xb4bcc6), Stencil = Rgb.Hex(0xd2ae48);
    public static readonly Rgb Pit = Rgb.Hex(0x0b0f14), PipeUnder = Rgb.Hex(0x34404c), CableRed = Rgb.Hex(0x6a2c26), CableTeal = Rgb.Hex(0x235a62), CableOchre = Rgb.Hex(0x6a5a24);
    public static readonly Rgb GrateBar = Rgb.Hex(0x707b88), GrateFrame = Rgb.Hex(0x808a97), Rust = Rgb.Hex(0x8a4c26);
    public static readonly Rgb Grout = Rgb.Hex(0x7c858c), TileA = Rgb.Hex(0xc7d1d8), TileB = Rgb.Hex(0xc4d3cc), TileC = Rgb.Hex(0xcdd0dc);
    public static readonly Rgb Rubber = Rgb.Hex(0x3b423b), RubberStud = Rgb.Hex(0x4a534a);
    public static readonly Rgb Carpet = Rgb.Hex(0x6c4359), CarpetMotif = Rgb.Hex(0x93617a), CarpetDot = Rgb.Hex(0x47283a), Lint = Rgb.Hex(0xc4aebb);
    public static readonly Rgb Engine = Rgb.Hex(0x545c66), EngineLug = Rgb.Hex(0x6c757f), Bolt = Rgb.Hex(0x89919b), HazardY = Rgb.Hex(0xe0b43a), HazardK = Rgb.Hex(0x1c1a16);
    public static readonly Rgb Wall = Rgb.Hex(0x303846), WallRib = Rgb.Hex(0x434d5c), WallVent = Rgb.Hex(0x12161c);
    public static readonly Rgb Partition = Rgb.Hex(0x8e8b80), Hull = Rgb.Hex(0x4a5562), HullRivet = Rgb.Hex(0x7c8794), Scorch = Rgb.Hex(0x3a322b);
    public static readonly Rgb Polish = Rgb.Hex(0xe8edf2), Heel = Rgb.Hex(0x151515);
    public static readonly Rgb Water = Rgb.Hex(0x80b4e6), WaterEdge = Rgb.Hex(0x2a4c70), White = Rgb.Hex(0xffffff);
    public static readonly Rgb Oil = Rgb.Hex(0x1d150b), Soot = Rgb.Hex(0x0c0b0a);
    public static readonly Rgb RustA = Rgb.Hex(0x9a4f22), RustB = Rgb.Hex(0xc46c2e), RustPit = Rgb.Hex(0x4a220e), RustHalo = Rgb.Hex(0x7a4a2a);
    public static readonly Rgb Dust = Rgb.Hex(0xbab3a4), DustLight = Rgb.Hex(0xdcd6c8), DustDark = Rgb.Hex(0x5c584f);
    public static readonly Rgb Frost = Rgb.Hex(0xe4f1ff), FrostCrystal = Rgb.Hex(0xf6fbff);
    public static readonly Rgb Coffee = Rgb.Hex(0x5a3a1e), Food = Rgb.Hex(0xa8642a), FoodBit = Rgb.Hex(0x4f7d2c), Mineral = Rgb.Hex(0xdadfda);
    public static readonly Rgb Boot = Rgb.Hex(0x1c1a18), WetPrint = Rgb.Hex(0x8fc0ea), Scratch = Rgb.Hex(0xd8dee6), Gouge = Rgb.Hex(0x0e0f10);
    public static readonly Rgb Duct = Rgb.Hex(0x9ca0a6), Sticky = Rgb.Hex(0xf2d65c), Ink = Rgb.Hex(0x2a3a8a), Paper = Rgb.Hex(0xece8de), Pin = Rgb.Hex(0xd23a3a), PaperLine = Rgb.Hex(0x8a8a90);
    public static readonly Rgb Weld = Rgb.Hex(0xbcc0c4), HeatStraw = Rgb.Hex(0xd8b860), HeatBlue = Rgb.Hex(0x4a64c8), HeatPurple = Rgb.Hex(0x7a4ab0), PatchPlate = Rgb.Hex(0x6c7682);
    public static readonly Rgb Burn = Rgb.Hex(0x120d0a), BurnHalo = Rgb.Hex(0x4a3020), Crack = Rgb.Hex(0x050607);
    public static readonly Rgb Steam = Rgb.Hex(0xf2f6fa), Drop = Rgb.Hex(0x9fd0ff), Spark = Rgb.Hex(0xffd27a), Smoke = Rgb.Hex(0x8a8a8e), Ember = Rgb.Hex(0xff8a30);
}

/// <summary>정수 해시 잡음 (감싸 이어지는 값 잡음 · fbm).</summary>
public static class Noise
{
    public static uint Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 2246822519u + 0x9E3779B9u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            return h ^ (h >> 13);
        }
    }

    public static float H01(int x, int y, int s) => (Hash(x, y, s) >> 8) * (1f / 16777216f);

    private static int Mod(int v, int n) => ((v % n) + n) % n;
    private static float Fade(float t) => t * t * (3f - 2f * t);

    /// <summary>격자 좌표의 값 잡음 (px · py 칸마다 되풀이).</summary>
    public static float Value(float x, float y, int px, int py, int s)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = Fade(x - x0), fy = Fade(y - y0);
        int ax = Mod(x0, px), bx = Mod(x0 + 1, px), ay = Mod(y0, py), by = Mod(y0 + 1, py);
        float a = H01(ax, ay, s), b = H01(bx, ay, s), c = H01(ax, by, s), d = H01(bx, by, s);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>u · v (0~1)에서 감싸 이어지는 fbm (0~1 근처).</summary>
    public static float Fbm(float u, float v, int f0, int oct, int s, float gain = 0.5f)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        int f = f0;
        for (int o = 0; o < oct; o++)
        {
            sum += amp * Value(u * f, v * f, f, f, s + o * 101);
            norm += amp;
            amp *= gain;
            f *= 2;
        }
        return sum / norm;
    }

    /// <summary>한쪽으로 늘인 잡음 (솔질 · 결 · 줄무늬).</summary>
    public static float Stretch(float u, float v, int fx, int fy, int s) => Value(u * fx, v * fy, fx, fy, s);

    public static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float Tri(float x) { float f = x - MathF.Floor(x); return 1f - 2f * MathF.Abs(f - 0.5f); }
}

/// <summary>결과 그림 (RGBA 0~1, 곧은 알파). 좌표는 감싼다.</summary>
public sealed class Pix
{
    public readonly int W, H;
    public readonly float[] R, G, B, A;

    public Pix(int w, int h)
    {
        W = w; H = h;
        R = new float[w * h]; G = new float[w * h]; B = new float[w * h]; A = new float[w * h];
    }

    public int I(int x, int y) => (((y % H) + H) % H) * W + (((x % W) + W) % W);

    public void Over(int i, Rgb c, float a)
    {
        if (a <= 0.0005f) return;
        if (a > 1f) a = 1f;
        float da = A[i], oa = a + da * (1f - a);
        float k1 = a / oa, k2 = da * (1f - a) / oa;
        R[i] = c.R * k1 + R[i] * k2;
        G[i] = c.G * k1 + G[i] * k2;
        B[i] = c.B * k1 + B[i] * k2;
        A[i] = oa;
    }

    /// <summary>모양 그리기: sdf(픽셀 중심) ≤ 0 이 안쪽, 경계는 1px 부드럽게.</summary>
    public void Shape(float x0, float y0, float x1, float y1, Func<float, float, float> sdf, Rgb c, float a)
    {
        for (int y = (int)MathF.Floor(y0) - 1; y <= (int)MathF.Ceiling(y1) + 1; y++)
        for (int x = (int)MathF.Floor(x0) - 1; x <= (int)MathF.Ceiling(x1) + 1; x++)
        {
            float cov = Math.Clamp(0.5f - sdf(x + 0.5f, y + 0.5f), 0f, 1f);
            if (cov > 0f) Over(I(x, y), c, a * cov);
        }
    }

    /// <summary>픽셀마다 색 · 알파를 정하는 붓 (그라디언트 · 잡음).</summary>
    public void Paint(float x0, float y0, float x1, float y1, Func<float, float, (Rgb c, float a)> f)
    {
        for (int y = (int)MathF.Floor(y0); y <= (int)MathF.Ceiling(y1); y++)
        for (int x = (int)MathF.Floor(x0); x <= (int)MathF.Ceiling(x1); x++)
        {
            var (c, a) = f(x + 0.5f, y + 0.5f);
            if (a > 0f) Over(I(x, y), c, a);
        }
    }

    public void Disc(float cx, float cy, float r, Rgb c, float a) => Shape(cx - r, cy - r, cx + r, cy + r, (x, y) => Sdf.Circle(x - cx, y - cy, r), c, a);
    public void Line(float ax, float ay, float bx, float by, float w, Rgb c, float a) =>
        Shape(MathF.Min(ax, bx) - w, MathF.Min(ay, by) - w, MathF.Max(ax, bx) + w, MathF.Max(ay, by) + w, (x, y) => Sdf.Segment(x, y, ax, ay, bx, by) - w * 0.5f, c, a);

    public byte[] Rgba8()
    {
        var o = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            o[i * 4] = Q(R[i]); o[i * 4 + 1] = Q(G[i]); o[i * 4 + 2] = Q(B[i]); o[i * 4 + 3] = Q(A[i]);
        }
        return o;
    }

    private static byte Q(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
}

public static class Sdf
{
    public static float Circle(float x, float y, float r) => MathF.Sqrt(x * x + y * y) - r;

    public static float Box(float x, float y, float hx, float hy, float r)
    {
        float qx = MathF.Abs(x) - hx + r, qy = MathF.Abs(y) - hy + r;
        float ox = MathF.Max(qx, 0f), oy = MathF.Max(qy, 0f);
        return MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(qx, qy), 0f) - r;
    }

    public static float Segment(float px, float py, float ax, float ay, float bx, float by)
    {
        float pax = px - ax, pay = py - ay, bax = bx - ax, bay = by - ay;
        float h = Math.Clamp((pax * bax + pay * bay) / MathF.Max(1e-6f, bax * bax + bay * bay), 0f, 1f);
        float dx = pax - bax * h, dy = pay - bay * h;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static float Hex(float x, float y, float r)
    {
        x = MathF.Abs(x); y = MathF.Abs(y);
        return MathF.Max(x * 0.866025f + y * 0.5f, y) - r;
    }

    /// <summary>각도 a 로 돌린 좌표.</summary>
    public static (float x, float y) Rot(float x, float y, float cs, float sn) => (x * cs + y * sn, -x * sn + y * cs);
}

/// <summary>입체 재질 판: 높이 · 바탕색 · 광택 · 가림을 쌓고 Shade 로 음영을 넣는다.</summary>
public sealed class Surf
{
    public const int S = 256;
    public readonly float[] Hgt = new float[S * S];
    public readonly Rgb[] Alb = new Rgb[S * S];
    public readonly float[] Spec = new float[S * S];
    public readonly float[] Occ = new float[S * S];

    public static int I(int x, int y) => ((y & (S - 1)) * S) + (x & (S - 1));

    public Surf(Rgb c, float h, float spec)
    {
        for (int i = 0; i < S * S; i++) { Alb[i] = c; Hgt[i] = h; Spec[i] = spec; Occ[i] = 1f; }
    }

    /// <summary>모양 안쪽(덮음 정도 cov · 경계 거리 d)에 픽셀마다 apply(i, cov, d, 지역 x, 지역 y).</summary>
    public void Stamp(float cx, float cy, float rx, float ry, Func<float, float, float> sdf, Action<int, float, float, float, float> apply)
    {
        for (int y = (int)MathF.Floor(cy - ry) - 1; y <= (int)MathF.Ceiling(cy + ry) + 1; y++)
        for (int x = (int)MathF.Floor(cx - rx) - 1; x <= (int)MathF.Ceiling(cx + rx) + 1; x++)
        {
            float lx = x + 0.5f - cx, ly = y + 0.5f - cy;
            float d = sdf(lx, ly);
            float cov = Math.Clamp(0.5f - d, 0f, 1f);
            if (cov > 0f) apply(I(x, y), cov, d, lx, ly);
        }
    }

    public void Each(Action<int, int, int> f)
    {
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++) f(I(x, y), x, y);
    }

    public void TintAt(int i, Rgb c, float t) => Alb[i] = Alb[i].Lerp(c, Math.Clamp(t, 0f, 1f));

    /// <summary>왼쪽 위 빛 · 홈 가림(높이 흐림과의 차) · 모서리 반짝임.</summary>
    public Pix Shade(float bump, float dif = 0.85f, float shin = 20f, float cavity = 2.2f)
    {
        var blur = BoxBlur(Hgt, 3);
        var p = new Pix(S, S);
        float lx = -0.45f, ly = -0.55f, lz = 0.70f;
        float ln = MathF.Sqrt(lx * lx + ly * ly + lz * lz); lx /= ln; ly /= ln; lz /= ln;
        float hx = lx, hy = ly, hz = lz + 1f;
        float hn = MathF.Sqrt(hx * hx + hy * hy + hz * hz); hx /= hn; hy /= hn; hz /= hn;
        float flatSpec = MathF.Pow(hz, shin);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            int i = I(x, y);
            float dx = (Hgt[I(x + 1, y)] - Hgt[I(x - 1, y)]) * 0.5f * bump;
            float dy = (Hgt[I(x, y + 1)] - Hgt[I(x, y - 1)]) * 0.5f * bump;
            float nx = -dx, ny = -dy, nz = 1f;
            float nn = MathF.Sqrt(nx * nx + ny * ny + nz * nz); nx /= nn; ny /= nn; nz /= nn;
            float nd = MathF.Max(0f, nx * lx + ny * ly + nz * lz);
            float lit = Math.Clamp(1f + dif * (nd - lz) / lz, 0.22f, 1.9f);
            float sp = Spec[i] * MathF.Max(0f, MathF.Pow(MathF.Max(0f, nx * hx + ny * hy + nz * hz), shin) - flatSpec) * 2.5f;
            float cav = Math.Clamp(1f - (blur[i] - Hgt[i]) * cavity, 0.45f, 1.08f);
            float k = lit * cav * Occ[i];
            var a = Alb[i];
            p.R[i] = a.R * k + sp; p.G[i] = a.G * k + sp; p.B[i] = a.B * k + sp; p.A[i] = 1f;
        }
        return p;
    }

    private static float[] BoxBlur(float[] src, int r)
    {
        var tmp = new float[S * S];
        var dst = new float[S * S];
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float s = 0f;
            for (int k = -r; k <= r; k++) s += src[I(x + k, y)];
            tmp[I(x, y)] = s * inv;
        }
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float s = 0f;
            for (int k = -r; k <= r; k++) s += tmp[I(x, y + k)];
            dst[I(x, y)] = s * inv;
        }
        return dst;
    }
}

/// <summary>모든 그림을 이름 순서대로 만든다. 이름은 View/LookSpec.cs 와 같아야 한다 (--lookcheck 가 대조).</summary>
public static partial class Gen
{
    public const int S = Surf.S, C = 64;

    public static List<(string name, Pix pix)> All() => new()
    {
        ("look_floor_metal", FloorMetal()),
        ("look_floor_grate", FloorGrate()),
        ("look_floor_tile", FloorTile()),
        ("look_floor_rubber", FloorRubber()),
        ("look_floor_carpet", FloorCarpet()),
        ("look_floor_engine", FloorEngine()),
        ("look_wall_panel", WallPanel()),
        ("look_wall_partition", WallPartition()),
        ("look_wall_hull", WallHull()),
        ("look_ov_wear", OvWear()),
        ("look_ov_wet", OvWet()),
        ("look_ov_oil", OvOil()),
        ("look_ov_soot", OvSoot()),
        ("look_ov_rust", OvRust()),
        ("look_ov_dust", OvDust()),
        ("look_ov_frost", OvFrost()),
        ("look_decals", Decals()),
        ("look_particles", Particles()),
    };

    private static float U(int x) => x / (float)S;

    // ───────────────────────── 바닥재 1: 금속판 — 솔질 결(칸마다 엇갈림) · 빗각 · 접시머리 나사 넷 · 스텐실 ─────────────────────────
    public static Pix FloorMetal()
    {
        var s = new Surf(Pal.Metal, 0.3f, 0.45f);
        for (int cy = 0; cy < 4; cy++)
        for (int cx = 0; cx < 4; cx++)
        {
            float ox = cx * C + C * 0.5f, oy = cy * C + C * 0.5f;
            float tone = 0.93f + 0.13f * Noise.H01(cx, cy, 1);
            bool horiz = (cx + cy) % 2 == 0;
            int seed = cx * 7 + cy * 13;
            s.Stamp(ox, oy, 32, 32, (x, y) => Sdf.Box(x, y, 30.6f, 30.6f, 3f), (i, cov, d, x, y) =>
            {
                float bevel = Noise.Smooth(0f, 2.4f, -d);
                s.Hgt[i] = 0.3f + 0.22f * bevel * cov;
                float u = (ox + x) / S, v = (oy + y) / S;
                float g = horiz ? Noise.Stretch(u, v, 4, 192, 30 + seed) : Noise.Stretch(u, v, 192, 4, 30 + seed);
                float g2 = horiz ? Noise.Stretch(u, v, 16, 256, 31) : Noise.Stretch(u, v, 256, 16, 31);
                s.Alb[i] = Pal.Metal * (tone * (0.9f + 0.12f * g + 0.06f * g2));
            });
            // 접시머리 나사 넷: 둥근 머리 · 둘레 홈 · 홈 방향은 나사마다 다르다
            foreach (var (sx, sy) in new[] { (-25f, -25f), (25f, -25f), (-25f, 25f), (25f, 25f) })
            {
                float px = ox + sx, py = oy + sy;
                float ang = Noise.H01((int)px, (int)py, 3) * MathF.PI;
                float cs = MathF.Cos(ang), sn = MathF.Sin(ang);
                s.Stamp(px, py, 4f, 4f, (x, y) => Sdf.Circle(x, y, 3.4f), (i, cov, d, x, y) =>
                {
                    float r = MathF.Sqrt(x * x + y * y);
                    if (r > 2.6f) { s.Hgt[i] -= 0.05f * cov; s.Alb[i] = s.Alb[i] * 0.55f; return; }
                    s.Hgt[i] = 0.52f + 0.07f * (1f - r * r / 6.8f);
                    s.Alb[i] = Pal.MetalScrew;
                    s.Spec[i] = 0.8f;
                    var (rx, ry) = Sdf.Rot(x, y, cs, sn);
                    if (MathF.Abs(ry) < 0.55f && MathF.Abs(rx) < 2.2f) { s.Hgt[i] -= 0.06f; s.Alb[i] = Pal.MetalScrew * 0.35f; }
                });
            }
        }
        // 때 · 잔흠집
        s.Each((i, x, y) => s.Alb[i] = s.Alb[i] * (0.9f + 0.12f * Noise.Fbm(U(x), U(y), 4, 4, 7)));
        for (int k = 0; k < 70; k++)
        {
            float ax = Noise.H01(k, 1, 40) * S, ay = Noise.H01(k, 2, 40) * S;
            float ang = (Noise.H01(k, 3, 40) - 0.5f) * 0.5f + (k % 2 == 0 ? 0f : MathF.PI * 0.5f);
            float len = 4f + 12f * Noise.H01(k, 4, 40);
            float bx = ax + MathF.Cos(ang) * len, by = ay + MathF.Sin(ang) * len;
            s.Stamp((ax + bx) * 0.5f, (ay + by) * 0.5f, len * 0.5f + 2f, len * 0.5f + 2f,
                (x, y) => Sdf.Segment(x, y, (ax - bx) * 0.5f, (ay - by) * 0.5f, (bx - ax) * 0.5f, (by - ay) * 0.5f) - 0.35f,
                (i, cov, d, x, y) => { s.TintAt(i, Pal.MetalScratch, 0.35f * cov); s.Spec[i] = 0.9f; });
        }
        // 스텐실 화살표 (닳아서 군데군데 빠졌다)
        StencilArrow(s, 2 * C + 32, 1 * C + 32, 1f);
        StencilArrow(s, 0 * C + 32, 3 * C + 32, -1f);
        return s.Shade(bump: 7f, dif: 0.9f, shin: 18f);
    }

    private static void StencilArrow(Surf s, float cx, float cy, float dir)
    {
        s.Stamp(cx, cy, 16, 10, (x, y) =>
        {
            float xx = x * dir;
            float shaft = Sdf.Box(xx + 4f, y, 8f, 2.6f, 0.5f);
            float head = MathF.Max(MathF.Abs(y) * 1.1f + (xx - 12f), -(xx - 2f));
            return MathF.Min(shaft, head);
        }, (i, cov, d, x, y) =>
        {
            float worn = Noise.Fbm((cx + x) / S, (cy + y) / S, 16, 3, 55);
            if (worn < 0.38f) return;
            s.TintAt(i, Pal.Stencil, 0.75f * cov * Noise.Smooth(0.38f, 0.5f, worn));
            s.Spec[i] = 0.1f;
        });
    }

    // ───────────────────────── 바닥재 2: 격자 — 테두리 레일 · 받침살 · 가로살 · 구멍 아래로 관 · 케이블 · 녹 ─────────────────────────
    public static Pix FloorGrate()
    {
        var s = new Surf(Pal.Pit, 0.02f, 0f);
        // 바닥 밑: 가로 관 둘 · 세로 케이블 묶음 (구멍 사이로 보인다)
        s.Each((i, x, y) =>
        {
            s.Occ[i] = 0.5f;
            float u = U(x), v = U(y);
            s.Alb[i] = Pal.Pit * (0.8f + 0.5f * Noise.Fbm(u, v, 4, 3, 90));
            foreach (var (py, r) in new[] { (30f, 9f), (158f, 6f) })
            {
                float dy = MathF.Abs(y + 0.5f - py);
                if (dy < r) { float k = 1f - dy / r; s.Alb[i] = Pal.PipeUnder * (0.45f + 0.8f * k * k); s.Occ[i] = 0.62f; }
            }
            int band = (int)((x + 0.5f - 92f) / 6f);
            if (x >= 92 && x < 110)
            {
                float lx = (x + 0.5f - 92f) % 6f - 3f;
                var col = band == 0 ? Pal.CableRed : band == 1 ? Pal.CableTeal : Pal.CableOchre;
                s.Alb[i] = col * (0.55f + 0.6f * (1f - MathF.Abs(lx) / 3f));
                s.Occ[i] = 0.66f;
            }
        });
        for (int cy = 0; cy < 4; cy++)
        for (int cx = 0; cx < 4; cx++)
        {
            float ox = cx * C, oy = cy * C;
            int seed = cx * 5 + cy * 11;
            void Bar(float x0, float y0, float x1, float y1, float h, Rgb col)
            {
                float mx = (x0 + x1) * 0.5f, my = (y0 + y1) * 0.5f, hx = (x1 - x0) * 0.5f, hy = (y1 - y0) * 0.5f;
                s.Stamp(mx, my, hx, hy, (x, y) => Sdf.Box(x, y, hx, hy, 0.6f), (i, cov, d, x, y) =>
                {
                    float prof = MathF.Min(1f, -d / MathF.Max(0.6f, MathF.Min(hx, hy)));
                    float hh = h * (0.8f + 0.2f * prof);
                    if (hh * cov < s.Hgt[i]) return;
                    s.Hgt[i] = hh * cov + s.Hgt[i] * (1f - cov);
                    float n = Noise.Fbm((mx + x) / S, (my + y) / S, 16, 3, 70 + seed);
                    s.Alb[i] = s.Alb[i].Lerp(col * (0.85f + 0.3f * n), cov);
                    s.Occ[i] = s.Occ[i] + (1f - s.Occ[i]) * cov;
                    s.Spec[i] = 0.5f;
                });
            }
            for (int k = 1; k < 8; k++) Bar(ox + k * 8 - 1.2f, oy + 2, ox + k * 8 + 1.2f, oy + C - 2, 0.75f, Pal.GrateBar);
            for (int k = 1; k < 4; k++) Bar(ox + 2, oy + k * 16 - 0.9f, ox + C - 2, oy + k * 16 + 0.9f, 0.68f, Pal.GrateBar * 0.92f);
            Bar(ox, oy, ox + C, oy + 3.2f, 0.85f, Pal.GrateFrame);
            Bar(ox, oy + C - 3.2f, ox + C, oy + C, 0.85f, Pal.GrateFrame);
            Bar(ox, oy, ox + 3.2f, oy + C, 0.85f, Pal.GrateFrame);
            Bar(ox + C - 3.2f, oy, ox + C, oy + C, 0.85f, Pal.GrateFrame);
            // 살이 만나는 곳의 녹
            for (int k = 0; k < 6; k++)
            {
                if (Noise.H01(seed, k, 71) > 0.45f) continue;
                float rx = ox + 8 * (1 + (int)(Noise.H01(seed, k, 72) * 7)), ry = oy + 16 * (1 + (int)(Noise.H01(seed, k, 73) * 3));
                float rr = 1.8f + 1.6f * Noise.H01(seed, k, 74);
                s.Stamp(rx, ry, rr, rr, (x, y) => Sdf.Circle(x, y, rr), (i, cov, d, x, y) => { if (s.Hgt[i] > 0.3f) { s.TintAt(i, Pal.Rust, 0.6f * cov); s.Spec[i] = 0.05f; } });
            }
        }
        return s.Shade(bump: 5f, dif: 0.85f, shin: 16f, cavity: 1.2f);
    }

    // ───────────────────────── 바닥재 3: 타일 — 칸마다 2×2 장 · 줄눈 · 유약 광택 · 잔점 · 실금 · 깨진 귀 ─────────────────────────
    public static Pix FloorTile()
    {
        var s = new Surf(Pal.Grout, 0.2f, 0.05f);
        s.Each((i, x, y) => s.Alb[i] = Pal.Grout * (0.82f + 0.25f * Noise.Fbm(U(x), U(y), 8, 3, 120)));
        for (int ty = 0; ty < 8; ty++)
        for (int tx = 0; tx < 8; tx++)
        {
            float ox = tx * 32 + 16, oy = ty * 32 + 16;
            float pick = Noise.H01(tx, ty, 121);
            var baseC = pick < 0.33f ? Pal.TileA : pick < 0.66f ? Pal.TileB : Pal.TileC;
            float tone = 0.95f + 0.08f * Noise.H01(tx, ty, 122);
            bool chip = Noise.H01(tx, ty, 123) < 0.09f;
            float chipX = Noise.H01(tx, ty, 124) < 0.5f ? -13.5f : 13.5f, chipY = Noise.H01(tx, ty, 125) < 0.5f ? -13.5f : 13.5f;
            s.Stamp(ox, oy, 16, 16, (x, y) => Sdf.Box(x, y, 14.6f, 14.6f, 2.4f), (i, cov, d, x, y) =>
            {
                if (chip && Sdf.Circle(x - chipX, y - chipY, 3.6f) < 0f) return;
                s.Hgt[i] = 0.2f + 0.32f * Noise.Smooth(0f, 1.6f, -d) * cov;
                float glaze = 1f + 0.05f * (-y / 16f) + 0.03f * (-x / 16f);
                var c = baseC * (tone * glaze);
                int px = (int)(ox + x), py = (int)(oy + y);
                if (Noise.H01(px, py, 126) < 0.012f) c = c * 0.78f;
                s.Alb[i] = s.Alb[i].Lerp(c, cov);
                s.Spec[i] = 0.7f;
            });
            if (Noise.H01(tx, ty, 127) < 0.1f)
            {
                // 실금: 귀퉁이에서 들어가는 꺾인 선
                float px = ox - 14f + 28f * Noise.H01(tx, ty, 128), py = oy - 14f;
                for (int k = 0; k < 4; k++)
                {
                    float nx = px + (Noise.H01(tx, ty, 130 + k) - 0.5f) * 10f, ny = py + 4f + 4f * Noise.H01(tx, ty, 140 + k);
                    float ax = px, ay = py;
                    s.Stamp((ax + nx) * 0.5f, (ay + ny) * 0.5f, 8, 8, (x, y) => Sdf.Segment(x, y, (ax - nx) * 0.5f, (ay - ny) * 0.5f, (nx - ax) * 0.5f, (ny - ay) * 0.5f) - 0.35f,
                        (i, cov, d, x, y) => { s.TintAt(i, Pal.Grout * 0.45f, 0.8f * cov); s.Hgt[i] -= 0.03f * cov; });
                    px = nx; py = ny;
                }
            }
        }
        return s.Shade(bump: 7f, dif: 0.8f, shin: 36f);
    }

    // ───────────────────────── 바닥재 4: 고무 — 엇갈린 동전 돌기 · 반질한 돌기 끝 · 깔판 이음 ─────────────────────────
    public static Pix FloorRubber()
    {
        var s = new Surf(Pal.Rubber, 0.4f, 0.06f);
        s.Each((i, x, y) =>
        {
            float n = Noise.Fbm(U(x), U(y), 32, 2, 160);
            s.Alb[i] = Pal.Rubber * (0.9f + 0.18f * n + 0.06f * (Noise.H01(x, y, 161) - 0.5f));
            s.Hgt[i] = 0.4f + 0.012f * n;
        });
        for (int ry = 0; ry < 16; ry++)
        for (int rx = 0; rx < 16; rx++)
        {
            float cx = rx * 16 + 8 + (ry % 2 == 0 ? 0 : 8), cy = ry * 16 + 8;
            s.Stamp(cx, cy, 6, 6, (x, y) => Sdf.Circle(x, y, 5f), (i, cov, d, x, y) =>
            {
                float r = MathF.Sqrt(x * x + y * y);
                float top = Noise.Smooth(5f, 3.6f, r);
                s.Hgt[i] = 0.4f + 0.2f * top;
                s.Alb[i] = s.Alb[i].Lerp(Pal.RubberStud * (1f + 0.07f * top), top);
                s.Spec[i] = 0.12f;
            });
        }
        s.Each((i, x, y) => { if (x % 64 == 0 || y % 64 == 0) { s.Hgt[i] = 0.33f; s.Alb[i] = s.Alb[i] * 0.55f; } });
        return s.Shade(bump: 5f, dif: 0.8f, shin: 8f);
    }

    // ───────────────────────── 바닥재 5: 카펫 — 털 결(깔판마다 90° 돌림) · 마름모 무늬 · 가운데 점 · 보풀 ─────────────────────────
    public static Pix FloorCarpet()
    {
        var s = new Surf(Pal.Carpet, 0.5f, 0f);
        s.Each((i, x, y) =>
        {
            float u = U(x), v = U(y);
            int qx = x / 128, qy = y / 128;
            bool pile = (qx + qy) % 2 == 0;
            float n1 = Noise.H01(x, y, 170) - 0.5f;
            float streak = pile ? Noise.Stretch(u, v, 256, 24, 171) : Noise.Stretch(u, v, 24, 256, 171);
            float shade = pile ? 0.96f : 1.05f;
            var c = Pal.Carpet * (shade * (1f + 0.16f * n1 + 0.14f * (streak - 0.5f)));
            // 마름모 무늬 (32px마다)
            float lx = x % 32 + 0.5f - 16f, ly = y % 32 + 0.5f - 16f;
            float dia = MathF.Abs(MathF.Abs(lx) + MathF.Abs(ly) - 10f);
            if (dia < 1.1f) c = c.Lerp(Pal.CarpetMotif, 0.55f * (1f - dia / 1.1f));
            if (lx * lx + ly * ly < 4.5f) c = c.Lerp(Pal.CarpetDot, 0.75f);
            if (x % 128 == 0 || y % 128 == 0) c = c * 0.78f;
            if (Noise.H01(x, y, 172) < 0.0025f) c = c.Lerp(Pal.Lint, 0.7f);
            s.Alb[i] = c;
            s.Hgt[i] = 0.5f + 0.05f * n1 + 0.06f * streak;
        });
        return s.Shade(bump: 3f, dif: 0.5f, shin: 4f, cavity: 1f);
    }

    // ───────────────────────── 바닥재 6: 기관 구역판 — 미끄럼 방지 마름모 돌기 · 육각 볼트 · 노랑 · 검정 경고 띠 · 기름때 ─────────────────────────
    public static Pix FloorEngine()
    {
        var s = new Surf(Pal.Engine, 0.4f, 0.4f);
        s.Each((i, x, y) => s.Alb[i] = Pal.Engine * (0.86f + 0.22f * Noise.Fbm(U(x), U(y), 4, 4, 180)));
        // 판 128×128: 홈 + 빗각
        for (int py = 0; py < 2; py++)
        for (int px = 0; px < 2; px++)
        {
            float ox = px * 128 + 64, oy = py * 128 + 64;
            s.Stamp(ox, oy, 64, 64, (x, y) => Sdf.Box(x, y, 63f, 63f, 2f), (i, cov, d, x, y) => s.Hgt[i] = 0.3f + 0.1f * Noise.Smooth(0f, 2f, -d) * cov);
        }
        // 마름모 돌기 (16px 격자, 돌기마다 방향이 엇갈린다)
        for (int gy = 0; gy < 16; gy++)
        for (int gx = 0; gx < 16; gx++)
        {
            float cx = gx * 16 + 8, cy = gy * 16 + 8;
            float a = (gx + gy) % 2 == 0 ? 0.785f : -0.785f;
            float cs = MathF.Cos(a), sn = MathF.Sin(a);
            s.Stamp(cx, cy, 8, 8, (x, y) => { var (rx, ry) = Sdf.Rot(x, y, cs, sn); return Sdf.Segment(rx, ry, -5f, 0f, 5f, 0f) - 1.7f; }, (i, cov, d, x, y) =>
            {
                float top = Noise.Smooth(0f, 1.2f, -d);
                s.Hgt[i] += 0.12f * top * cov;
                s.Alb[i] = s.Alb[i].Lerp(Pal.EngineLug, 0.7f * top);
                s.Spec[i] = 0.75f;
            });
        }
        // 육각 볼트 · 와셔
        for (int py = 0; py < 2; py++)
        for (int px = 0; px < 2; px++)
            foreach (var (bx, by) in new[] { (9f, 9f), (119f, 9f), (9f, 119f), (119f, 119f) })
            {
                float cx = px * 128 + bx, cy = py * 128 + by;
                s.Stamp(cx, cy, 6.5f, 6.5f, (x, y) => Sdf.Circle(x, y, 5.6f), (i, cov, d, x, y) =>
                {
                    float hexd = Sdf.Hex(x, y, 3.6f);
                    if (hexd < 0f) { s.Hgt[i] = 0.62f; s.Alb[i] = Pal.Bolt; s.Spec[i] = 0.9f; }
                    else { s.Hgt[i] = 0.48f; s.Alb[i] = Pal.Bolt * 0.8f; s.Spec[i] = 0.5f; }
                });
            }
        // 경고 띠 (첫 판 윗변, 닳아서 군데군데 벗겨졌다)
        s.Each((i, x, y) =>
        {
            if (x >= 128 || y < 4 || y > 12) return;
            float worn = Noise.Fbm(U(x), U(y), 32, 3, 185);
            if (worn < 0.36f) return;
            bool yellow = ((x + y) / 6) % 2 == 0;
            s.TintAt(i, yellow ? Pal.HazardY : Pal.HazardK, 0.85f * Noise.Smooth(0.36f, 0.46f, worn));
            s.Spec[i] = 0.15f;
        });
        // 기름때
        s.Each((i, x, y) => { float f = Noise.Fbm(U(x), U(y), 4, 4, 186); if (f > 0.58f) s.Alb[i] = s.Alb[i] * (1f - 0.9f * (f - 0.58f)); });
        return s.Shade(bump: 6f, dif: 0.9f, shin: 22f);
    }

    // ───────────────────────── 벽 1: 안쪽 패널 — 칸마다 판 하나 (민판 · 환기 슬롯 · 대각 보강 · 관로 덮개) · 리벳 ─────────────────────────
    public static Pix WallPanel()
    {
        var s = new Surf(Pal.Wall, 0.35f, 0.2f);
        s.Each((i, x, y) => s.Alb[i] = Pal.Wall * (0.88f + 0.2f * Noise.Fbm(U(x), U(y), 4, 4, 200)));
        for (int cy = 0; cy < 4; cy++)
        for (int cx = 0; cx < 4; cx++)
        {
            float ox = cx * C + 32, oy = cy * C + 32;
            int kind = (int)(Noise.H01(cx, cy, 201) * 4f);
            s.Stamp(ox, oy, 32, 32, (x, y) => Sdf.Box(x, y, 29f, 29f, 2.5f), (i, cov, d, x, y) =>
            {
                s.Hgt[i] = 0.35f + 0.15f * Noise.Smooth(0f, 2f, -d) * cov;
                s.TintAt(i, Pal.WallRib, 0.35f * cov);
            });
            switch (kind)
            {
                case 0: // 환기 슬롯 다섯
                    for (int k = 0; k < 5; k++)
                    {
                        float sy = oy - 16 + k * 8;
                        s.Stamp(ox, sy, 18, 3, (x, y) => Sdf.Box(x, y, 16f, 1.5f, 1.4f), (i, cov, d, x, y) => { s.Hgt[i] -= 0.15f * cov; s.TintAt(i, Pal.WallVent, 0.9f * cov); });
                    }
                    break;
                case 1: // 대각 보강 (X)
                    foreach (var sg in new[] { 1f, -1f })
                        s.Stamp(ox, oy, 24, 24, (x, y) => Sdf.Segment(x, y, -20f, -20f * sg, 20f, 20f * sg) - 2.2f, (i, cov, d, x, y) => { s.Hgt[i] += 0.06f * cov; s.TintAt(i, Pal.WallRib * 1.15f, 0.5f * cov); });
                    break;
                case 2: // 움푹한 가운데 판
                    s.Stamp(ox, oy, 18, 18, (x, y) => Sdf.Box(x, y, 16f, 16f, 2f), (i, cov, d, x, y) => { s.Hgt[i] -= 0.08f * Noise.Smooth(0f, 2f, -d) * cov; s.TintAt(i, Pal.Wall * 0.8f, 0.5f * cov); });
                    break;
                default: // 관로 덮개 둘
                    foreach (var py in new[] { -9f, 9f })
                        s.Stamp(ox, oy + py, 30, 5, (x, y) => Sdf.Box(x, y, 28f, 3.5f, 3.4f), (i, cov, d, x, y) =>
                        {
                            float k = Noise.Smooth(0f, 3.4f, -d);
                            s.Hgt[i] += 0.12f * k * cov; s.TintAt(i, Pal.WallRib * 1.2f, 0.6f * cov); s.Spec[i] = 0.5f;
                        });
                    break;
            }
            foreach (var (rx, ry) in new[] { (-25f, -25f), (25f, -25f), (-25f, 25f), (25f, 25f), (0f, -25f), (0f, 25f) })
                s.Stamp(ox + rx, oy + ry, 2.5f, 2.5f, (x, y) => Sdf.Circle(x, y, 1.6f), (i, cov, d, x, y) => { s.Hgt[i] += 0.06f * cov; s.TintAt(i, Pal.WallRib * 1.5f, cov); s.Spec[i] = 0.7f; });
        }
        return s.Shade(bump: 6f, dif: 0.9f, shin: 18f);
    }

    // ───────────────────────── 벽 2: 얇은 칸막이 — 골판 · 이음 띠 · 나사 줄 ─────────────────────────
    public static Pix WallPartition()
    {
        var s = new Surf(Pal.Partition, 0.5f, 0.15f);
        s.Each((i, x, y) =>
        {
            float t = Noise.Tri((x + 0.5f) / 8f);
            s.Hgt[i] = 0.45f + 0.09f * t * t;
            s.Alb[i] = Pal.Partition * (0.9f + 0.08f * t + 0.1f * Noise.Fbm(U(x), U(y), 4, 3, 210));
            if (x % 64 < 3) { s.Hgt[i] = 0.4f; s.Alb[i] = Pal.Partition * 0.62f; }
        });
        for (int k = 0; k < 4; k++)
        for (int j = 0; j < 16; j++)
            s.Stamp(k * 64 + 1.5f, j * 16 + 8, 2.5f, 2.5f, (x, y) => Sdf.Circle(x, y, 1.4f), (i, cov, d, x, y) => { s.Hgt[i] += 0.08f * cov; s.TintAt(i, Pal.Partition * 1.25f, cov); s.Spec[i] = 0.6f; });
        return s.Shade(bump: 5f, dif: 0.8f, shin: 14f);
    }

    // ───────────────────────── 벽 3: 외판 — 엇갈린 큰 판 · 가장자리 리벳 줄 · 미세 운석 자국 · 그을린 줄 ─────────────────────────
    public static Pix WallHull()
    {
        var s = new Surf(Pal.Hull, 0.35f, 0.3f);
        s.Each((i, x, y) =>
        {
            float u = U(x), v = U(y);
            s.Alb[i] = Pal.Hull * (0.86f + 0.12f * Noise.Stretch(u, v, 16, 128, 220) + 0.1f * Noise.Fbm(u, v, 4, 3, 221));
            float sc = Noise.Fbm(u, v, 4, 4, 222);
            if (sc > 0.62f) s.TintAt(i, Pal.Scorch, (sc - 0.62f) * 2.2f);
        });
        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 2; col++)
        {
            float ox = col * 128 + (row % 2 == 0 ? 64 : 0), oy = row * 64 + 32;
            float tone = 0.92f + 0.14f * Noise.H01(row, col, 223);
            s.Stamp(ox, oy, 64, 32, (x, y) => Sdf.Box(x, y, 63f, 31f, 1.5f), (i, cov, d, x, y) =>
            {
                s.Hgt[i] = 0.3f + 0.12f * Noise.Smooth(0f, 1.6f, -d) * cov;
                s.Alb[i] = s.Alb[i] * (1f + (tone - 1f) * cov);
            });
            for (int k = 0; k < 15; k++)
                foreach (var ry in new[] { -27f, 27f })
                {
                    float rx = ox - 56 + k * 8;
                    s.Stamp(rx, oy + ry, 2f, 2f, (x, y) => Sdf.Circle(x, y, 1.25f), (i, cov, d, x, y) => { s.Hgt[i] += 0.05f * cov; s.TintAt(i, Pal.HullRivet, cov); s.Spec[i] = 0.8f; });
                }
        }
        for (int k = 0; k < 70; k++)
        {
            float px = Noise.H01(k, 0, 224) * S, py = Noise.H01(k, 1, 224) * S, r = 0.8f + 1.4f * Noise.H01(k, 2, 224);
            s.Stamp(px, py, r + 1.5f, r + 1.5f, (x, y) => Sdf.Circle(x, y, r + 0.9f), (i, cov, d, x, y) =>
            {
                float rr = MathF.Sqrt(x * x + y * y);
                if (rr < r) { s.Hgt[i] -= 0.08f * cov; s.Alb[i] = s.Alb[i] * 0.45f; }
                else { s.Hgt[i] += 0.03f * cov; s.TintAt(i, Pal.HullRivet, 0.4f * cov); }
            });
        }
        return s.Shade(bump: 6f, dif: 0.9f, shin: 18f);
    }
}
