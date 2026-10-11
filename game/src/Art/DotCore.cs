using System;
using System.Collections.Generic;

namespace ShipSim.Art;

// 도트 화가 — 바탕.
//   Godot 없이 도는 순수 C#: 게임은 이 그림을 텍스처로 올리고, 헤드리스는 PNG로 뽑아 눈으로 확인한다 (같은 코드 · 같은 그림).
//   규칙: 정해진 색표(색마다 어두움→밝음 몇 단)만 쓴다 · 빛은 왼쪽 위에서 · 부위마다 위/왼쪽 가장자리는 밝게, 아래/오른쪽은 어둡게 ·
//   앞에 놓인 부위는 뒤 부위에 그림자를 드리운다 · 바깥은 1도트 짙은 외곽선(그 부위 색의 가장 어두운 빛).
//   한 칸 = 32도트 (시뮬레이션의 한 칸 32와 같다 — 도트 하나가 세계 단위 하나).

/// <summary>색 하나 (RGBA 8비트).</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba Hex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[..2], 16), g = Convert.ToByte(hex[2..4], 16), b = Convert.ToByte(hex[4..6], 16);
        byte a = hex.Length >= 8 ? Convert.ToByte(hex[6..8], 16) : (byte)255;
        return new Rgba(r, g, b, a);
    }

    public Rgba WithA(byte a) => new(R, G, B, a);
    public Rgba Scale(float k) => new((byte)Math.Clamp(R * k, 0, 255), (byte)Math.Clamp(G * k, 0, 255), (byte)Math.Clamp(B * k, 0, 255), A);
    public float Luma => 0.299f * R + 0.587f * G + 0.114f * B;

    public static Rgba Lerp(Rgba a, Rgba b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
    }
}

/// <summary>색표: 색마다 어두움 → 밝음 몇 단 (그림자는 푸르게, 밝은 쪽은 따뜻하게 살짝 비튼다).</summary>
public static class Ramps
{
    private static readonly List<Rgba[]> All = new() { new[] { new Rgba(0, 0, 0, 0) } };

    public static int Add(params string[] hex)
    {
        var r = new Rgba[hex.Length];
        for (int i = 0; i < hex.Length; i++) r[i] = Rgba.Hex(hex[i]);
        All.Add(r);
        return All.Count - 1;
    }

    public static int Count(int ramp) => All[ramp].Length;
    public static Rgba Get(int ramp, int shade)
    {
        var r = All[ramp];
        return r[Math.Clamp(shade, 0, r.Length - 1)];
    }

    // ── 피부 6 (5단) ──
    public static readonly int[] Skin =
    {
        Add("#5a3431", "#a15e52", "#dc937c", "#f3bfa2", "#fde0cc"),
        Add("#4c2b26", "#8c4f3e", "#c67b5e", "#e4a987", "#f6cdb0"),
        Add("#41261f", "#7b432f", "#b0694a", "#d3916d", "#ecb994"),
        Add("#371f19", "#6a3826", "#98573b", "#bd7b57", "#d9a17b"),
        Add("#2c1813", "#552e1f", "#7a472e", "#9e6444", "#c08761"),
        Add("#1d110e", "#3b2117", "#593322", "#784a32", "#986649"),
    };

    /// <summary>앓는 사람의 피부 (창백하고 푸르스름).</summary>
    public static readonly int SickSkin = Add("#2f3a35", "#5f716a", "#8fa59b", "#b7cbc0", "#d8e6dd");
    public static readonly int FrostSkin = Add("#1d2a44", "#34507a", "#5a7fae", "#8eb0d6", "#c4dbf0");

    // ── 머리카락 (4단) ──
    public static readonly int HairBlack = Add("#0e0b0f", "#1f181c", "#33282a", "#4a3b39");
    public static readonly int HairDark = Add("#1a100c", "#352015", "#553522", "#775034");
    public static readonly int HairBrown = Add("#2a180e", "#53311a", "#7c4c29", "#a46d41");
    public static readonly int HairAuburn = Add("#2b100b", "#5c2418", "#8d3c27", "#ba5c3a");
    public static readonly int HairBlonde = Add("#5a3f1f", "#987335", "#cca456", "#ead08a");
    public static readonly int HairGrey = Add("#3a383a", "#6a6668", "#9b9794", "#c9c5c0");
    public static readonly int HairWhite = Add("#66656b", "#a3a2a8", "#d2d1d5", "#f3f2f5");
    public static readonly int HairTeal = Add("#0d2b2c", "#1b5755", "#2c8781", "#5bb9ad");
    public static readonly int HairPink = Add("#38142e", "#702e5b", "#b04f89", "#dc80b1");
    public static readonly int[] Hair = { HairBlack, HairDark, HairBrown, HairAuburn, HairBlonde, HairGrey, HairTeal, HairPink, HairWhite };

    // ── 옷 (5단) — 직업복 ──
    public static readonly int ClothOrange = Add("#3b190b", "#773313", "#c1581b", "#ea8832", "#f6b564");
    public static readonly int ClothWhite = Add("#3a4450", "#768390", "#b4c0c8", "#e0e8ec", "#fbfdfd");
    public static readonly int ClothBlue = Add("#0f1838", "#1c306c", "#2d53ab", "#4c82d8", "#88b4ee");
    public static readonly int ClothTeal = Add("#0b282c", "#145156", "#1f7d86", "#36aab1", "#7ad5d2");
    public static readonly int ClothGreen = Add("#0f240f", "#1e4a1c", "#2d762a", "#4ca445", "#8bcd79");
    public static readonly int ClothPurple = Add("#1c1136", "#37246c", "#563da4", "#7c64d2", "#ae9eec");
    public static readonly int ClothBeige = Add("#383024", "#6b5f4d", "#a5977d", "#d3c8ae", "#f0e8d4");
    public static readonly int ClothRed = Add("#2c0a0e", "#5e1620", "#9a2633", "#cf4249", "#ef7e78");
    public static readonly int ClothYellow = Add("#3a2b07", "#77590f", "#c09016", "#ecc02e", "#fbe387");
    public static readonly int ClothNavy = Add("#0b0f1c", "#18203a", "#26335a", "#3a4c80", "#5d72a8");
    public static readonly int ClothBrown = Add("#22150d", "#45291a", "#6c4229", "#93603e", "#b9845d");

    // ── 쇠붙이 · 판 · 고무 · 나무 · 유리 ──
    public static readonly int Steel = Add("#11161e", "#202935", "#364352", "#556474", "#7f8fa0", "#b3c1cd");
    public static readonly int SteelWarm = Add("#17140f", "#2b2720", "#454036", "#665f51", "#8f8775", "#bdb6a2");
    public static readonly int Panel = Add("#0a0d12", "#141921", "#202733", "#2e3744", "#3f4a59");
    public static readonly int Rubber = Add("#0b0b0d", "#18181c", "#26262c", "#36363e");
    public static readonly int Wood = Add("#2a170c", "#4f2f18", "#7a4c28", "#a26d3e", "#c8935e");
    public static readonly int Glass = Add("#0d2430", "#174357", "#2b6d87", "#5aa3bd", "#a8dcea");
    public static readonly int Hazard = Add("#362705", "#735409", "#bd8d12", "#eebf28", "#fde384");
    public static readonly int Ink = Add("#0c0a10", "#1a1620", "#28222e");

    // ── 빛 · 불 · 연기 · 피 · 붕대 · 그을음 ──
    public static readonly int Cyan = Add("#0a3640", "#117684", "#21c0d0", "#7cecf2", "#e2feff");
    public static readonly int Led = Add("#0a2c10", "#156a28", "#2bbd48", "#8af298", "#e4ffe6");
    public static readonly int Alarm = Add("#2a0507", "#5c0e12", "#9e1a1c", "#de3529", "#ff7653", "#ffc6a8");
    public static readonly int Fire = Add("#1a0505", "#5a1308", "#a42b0c", "#e65a10", "#ff981e", "#ffd146", "#fff5c4", "#ffffff");
    public static readonly int Smoke = Add("#111114", "#24242a", "#3b3a41", "#57555d", "#78767e", "#9f9da5", "#c6c4ca");
    public static readonly int Blood = Add("#33050a", "#640b12", "#9e1519", "#d1322b");
    public static readonly int Bandage = Add("#7d7c74", "#b7b6ac", "#e3e2d8", "#fafaf4");
    public static readonly int Char = Add("#120a08", "#2a1711", "#4b2a1d", "#6e3e2a");
    public static readonly int Bruise = Add("#2a1838", "#4a2c5e", "#6e4884", "#9468a6");
    public static readonly int Cloud = Add("#0d1b26", "#1f3a50", "#3d6a8a", "#7aa8c6", "#c5e2f2");
    public static readonly int Soil = Add("#1a120c", "#33241a", "#4f3a2a", "#6e543d");
    public static readonly int Leaf = Add("#0d240f", "#1a4a1e", "#2c7a2c", "#52a846", "#94d474");

    /// <summary>개인 색 (목깃 · 담요 · 무늬) — 승무원 번호로 고른다.</summary>
    public static readonly int[] Accent = { ClothRed, ClothYellow, ClothTeal, ClothPurple, ClothGreen, ClothBlue, ClothOrange, ClothNavy, ClothBrown };
}

/// <summary>
/// 색표 칸으로 그리는 도트 판. 칸마다 (색표 · 단) + 부위 번호(나중에 그린 부위가 앞).
/// 다 그린 뒤 Bevel(빛 · 그림자 · 드리운 그림자) → ToImg(바깥 외곽선) 순서로 마무리한다.
/// </summary>
public sealed class Dot
{
    public readonly int W, H;
    private readonly ushort[] _c;   // (색표 << 4) | 단 — 0 = 빈칸
    private readonly byte[] _part;  // 부위 번호 (클수록 앞)
    private readonly bool[] _flat;  // 눈 · 입 · 글자 같은 세부 — 빛을 받지 않는다
    /// <summary>지금 그리는 부위 번호 — Next()로 앞으로 한 장 넘긴다.</summary>
    public byte Part = 1;

    public Dot(int w, int h)
    {
        W = w; H = h;
        _c = new ushort[w * h];
        _part = new byte[w * h];
        _flat = new bool[w * h];
    }

    public Dot Next() { if (Part < 254) Part++; return this; }
    public bool In(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H;
    public bool Has(int x, int y) => In(x, y) && _c[y * W + x] != 0;
    public int RampAt(int x, int y) => In(x, y) ? _c[y * W + x] >> 4 : 0;
    public int ShadeAt(int x, int y) => In(x, y) ? _c[y * W + x] & 15 : 0;
    public byte PartAt(int x, int y) => In(x, y) ? _part[y * W + x] : (byte)0;

    /// <summary>칸 하나를 이 부위로 칠한다.</summary>
    public void Set(int x, int y, int ramp, int shade)
    {
        if (!In(x, y)) return;
        int i = y * W + x;
        _c[i] = (ushort)((ramp << 4) | Math.Clamp(shade, 0, 15));
        _part[i] = Part;
        _flat[i] = false;
    }

    /// <summary>세부: 밑 부위는 그대로 두고 색만 (빛을 받지 않는다). 빈칸엔 안 칠한다(onEmpty면 칠함).</summary>
    public void Mark(int x, int y, int ramp, int shade, bool onEmpty = false)
    {
        if (!In(x, y)) return;
        int i = y * W + x;
        if (_c[i] == 0 && !onEmpty) return;
        if (_c[i] == 0) _part[i] = Part;
        _c[i] = (ushort)((ramp << 4) | Math.Clamp(shade, 0, 15));
        _flat[i] = true;
    }

    /// <summary>이미 칠한 칸의 단만 바꾼다 (부위 · 색표 유지).</summary>
    public void Tone(int x, int y, int delta)
    {
        if (!Has(x, y)) return;
        int i = y * W + x;
        int r = _c[i] >> 4, s = Math.Clamp((_c[i] & 15) + delta, 0, Ramps.Count(r) - 1);
        _c[i] = (ushort)((r << 4) | s);
    }

    public void Clear(int x, int y)
    {
        if (!In(x, y)) return;
        int i = y * W + x;
        _c[i] = 0; _part[i] = 0; _flat[i] = false;
    }

    public void Rect(int x, int y, int w, int h, int ramp, int shade)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++) Set(i, j, ramp, shade);
    }

    public void MarkRect(int x, int y, int w, int h, int ramp, int shade, bool onEmpty = false)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++) Mark(i, j, ramp, shade, onEmpty);
    }

    /// <summary>가득 찬 타원 (칸 중심 기준 — 짝수 폭도 좌우가 같다).</summary>
    public void Oval(float cx, float cy, float rx, float ry, int ramp, int shade)
    {
        for (int y = (int)MathF.Floor(cy - ry); y <= (int)MathF.Ceiling(cy + ry); y++)
            for (int x = (int)MathF.Floor(cx - rx); x <= (int)MathF.Ceiling(cx + rx); x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                if (dx * dx + dy * dy <= 1.0f) Set(x, y, ramp, shade);
            }
    }

    public void MarkOval(float cx, float cy, float rx, float ry, int ramp, int shade)
    {
        for (int y = (int)MathF.Floor(cy - ry); y <= (int)MathF.Ceiling(cy + ry); y++)
            for (int x = (int)MathF.Floor(cx - rx); x <= (int)MathF.Ceiling(cx + rx); x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                if (dx * dx + dy * dy <= 1.0f) Mark(x, y, ramp, shade);
            }
    }

    public void Line(int x0, int y0, int x1, int y1, int ramp, int shade, bool mark = false)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            if (mark) Mark(x0, y0, ramp, shade, onEmpty: true); else Set(x0, y0, ramp, shade);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>줄 단위 반폭 표로 대칭 모양 (머리 · 몸통): halfW[i]는 i번째 줄의 반폭, 짝수 폭 중심 cx(칸 경계).</summary>
    public void Rows(int cx, int top, int[] halfW, int ramp, int shade, int widen = 0)
    {
        for (int i = 0; i < halfW.Length; i++)
        {
            int hw = halfW[i] + (halfW[i] > 0 ? widen : 0);
            for (int x = cx - hw; x < cx + hw; x++) Set(x, top + i, ramp, shade);
        }
    }

    /// <summary>
    /// 빛: 위·왼쪽이 트인 칸은 한 단 밝게, 아래·오른쪽이 트인 칸은 한 단 어둡게.
    /// 위·왼쪽 이웃이 "앞 부위"면 그 부위가 드리운 그림자로 한 단 어둡게.
    /// </summary>
    public void Bevel(int light = 1, int dark = 1)
    {
        var delta = new int[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (_c[i] == 0 || _flat[i]) continue;
                byte p = _part[i];
                int d = 0;
                byte up = PartAt(x, y - 1), left = PartAt(x - 1, y), down = PartAt(x, y + 1), right = PartAt(x + 1, y);
                bool upOpen = up != p, leftOpen = left != p;
                if (upOpen) d += up > p ? -dark : light;
                else if (leftOpen) d += left > p ? -dark : light;
                bool downOpen = down != p && !(down > p && Has(x, y + 1));
                bool rightOpen = right != p && !(right > p && Has(x + 1, y));
                if (downOpen) d -= dark;
                else if (rightOpen && !upOpen) d -= dark;
                delta[i] = d;
            }
        for (int i = 0; i < W * H; i++)
        {
            if (delta[i] == 0) continue;
            int r = _c[i] >> 4, s = Math.Clamp((_c[i] & 15) + delta[i], 0, Ramps.Count(r) - 1);
            _c[i] = (ushort)((r << 4) | s);
        }
    }

    /// <summary>다른 판을 이 판의 새 부위들로 얹는다 (flipX면 좌우 뒤집기).</summary>
    public void Paste(Dot src, int dx, int dy, bool flipX = false)
    {
        byte basePart = Part;
        byte maxPart = basePart;
        for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
            {
                int si = y * src.W + x;
                if (src._c[si] == 0) continue;
                int tx = dx + (flipX ? src.W - 1 - x : x), ty = dy + y;
                if (!In(tx, ty)) continue;
                int ti = ty * W + tx;
                _c[ti] = src._c[si];
                byte np = (byte)Math.Min(254, basePart + src._part[si]);
                _part[ti] = np;
                _flat[ti] = src._flat[si];
                if (np > maxPart) maxPart = np;
            }
        Part = (byte)Math.Min(254, maxPart + 1);
    }

    /// <summary>좌우 뒤집은 새 판.</summary>
    public Dot Flip()
    {
        var d = new Dot(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int s = y * W + x, t = y * W + (W - 1 - x);
                d._c[t] = _c[s]; d._part[t] = _part[s]; d._flat[t] = _flat[s];
            }
        d.Part = Part;
        return d;
    }

    /// <summary>90도 돌린 새 판 (cw: 시계 방향) — 누운 사람.</summary>
    public Dot Rotate(bool cw)
    {
        var d = new Dot(H, W);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int s = y * W + x;
                int tx = cw ? H - 1 - y : y, ty = cw ? x : W - 1 - x;
                int t = ty * d.W + tx;
                d._c[t] = _c[s]; d._part[t] = _part[s]; d._flat[t] = _flat[s];
            }
        d.Part = Part;
        return d;
    }

    /// <summary>색으로 풀어 RGBA 그림으로 — outline이면 바깥 1도트 외곽선 (이웃 색의 가장 어두운 단보다 더 짙게).</summary>
    public Img ToImg(bool outline = true, float ink = 0.55f)
    {
        var img = new Img(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (_c[i] == 0) continue;
                img.Set(x, y, Ramps.Get(_c[i] >> 4, _c[i] & 15));
            }
        if (!outline) return img;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (_c[y * W + x] != 0) continue;
                int best = -1; float bestL = 1e9f;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 1 ? -1 : k == 2 ? 1 : 0), ny = y + (k == 0 ? -1 : k == 3 ? 1 : 0);
                    if (!Has(nx, ny)) continue;
                    int r = _c[ny * W + nx] >> 4;
                    float l = Ramps.Get(r, 0).Luma;
                    if (l < bestL) { bestL = l; best = r; }
                }
                if (best >= 0) img.Set(x, y, Ramps.Get(best, 0).Scale(ink));
            }
        return img;
    }
}

/// <summary>RGBA 그림 — 장면 짜기 · 반투명(그림자 · 연기 · 빛) · 확대 · PNG.</summary>
public sealed class Img
{
    public readonly int W, H;
    public readonly byte[] Px;

    public Img(int w, int h) { W = w; H = h; Px = new byte[w * h * 4]; }

    public bool In(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H;

    public Rgba Get(int x, int y)
    {
        if (!In(x, y)) return default;
        int i = (y * W + x) * 4;
        return new Rgba(Px[i], Px[i + 1], Px[i + 2], Px[i + 3]);
    }

    public void Set(int x, int y, Rgba c)
    {
        if (!In(x, y)) return;
        int i = (y * W + x) * 4;
        Px[i] = c.R; Px[i + 1] = c.G; Px[i + 2] = c.B; Px[i + 3] = c.A;
    }

    /// <summary>위에 덧칠 (알파 섞기).</summary>
    public void Blend(int x, int y, Rgba c, float alpha = 1f)
    {
        if (!In(x, y)) return;
        float a = c.A / 255f * alpha;
        if (a <= 0f) return;
        int i = (y * W + x) * 4;
        float da = Px[i + 3] / 255f;
        float oa = a + da * (1 - a);
        if (oa <= 0f) return;
        Px[i] = (byte)((c.R * a + Px[i] * da * (1 - a)) / oa);
        Px[i + 1] = (byte)((c.G * a + Px[i + 1] * da * (1 - a)) / oa);
        Px[i + 2] = (byte)((c.B * a + Px[i + 2] * da * (1 - a)) / oa);
        Px[i + 3] = (byte)(oa * 255);
    }

    /// <summary>빛 더하기 (밝아지기만 한다).</summary>
    public void Add(int x, int y, Rgba c, float k)
    {
        if (!In(x, y)) return;
        int i = (y * W + x) * 4;
        Px[i] = (byte)Math.Min(255, Px[i] + c.R * k);
        Px[i + 1] = (byte)Math.Min(255, Px[i + 1] + c.G * k);
        Px[i + 2] = (byte)Math.Min(255, Px[i + 2] + c.B * k);
    }

    public void Fill(int x, int y, int w, int h, Rgba c, float alpha = 1f)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++) Blend(i, j, c, alpha);
    }

    /// <summary>반투명 타원 (그림자 · 연기).</summary>
    public void Oval(float cx, float cy, float rx, float ry, Rgba c, float alpha = 1f, bool soft = false)
    {
        for (int y = (int)MathF.Floor(cy - ry); y <= (int)MathF.Ceiling(cy + ry); y++)
            for (int x = (int)MathF.Floor(cx - rx); x <= (int)MathF.Ceiling(cx + rx); x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry, d = dx * dx + dy * dy;
                if (d > 1f) continue;
                float a = soft ? alpha * (1f - d) : alpha;
                if (soft) a = Bands(a); // 도트답게 계단으로
                Blend(x, y, c, a);
            }
    }

    /// <summary>빛 번짐 (계단진 동심원).</summary>
    public void Glow(float cx, float cy, float r, Rgba c, float k)
    {
        for (int y = (int)(cy - r); y <= (int)(cy + r); y++)
            for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d = MathF.Sqrt(dx * dx + dy * dy) / r;
                if (d >= 1f) continue;
                Add(x, y, c, Bands((1f - d) * (1f - d)) * k);
            }
    }

    private static float Bands(float a) => MathF.Ceiling(a * 4f) / 4f;

    /// <summary>다른 그림을 얹는다 (알파 섞기).</summary>
    public void Draw(Img src, int dx, int dy, bool flipX = false, float alpha = 1f)
    {
        for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
            {
                var c = src.Get(x, y);
                if (c.A == 0) continue;
                Blend(dx + (flipX ? src.W - 1 - x : x), dy + y, c, alpha);
            }
    }

    /// <summary>바닥 그림자: 이 그림의 실루엣을 납작하게 눌러 아래에 깐다.</summary>
    public void DrawShadowOf(Img src, int dx, int dy, float alpha = 0.32f)
    {
        var ink = new Rgba(8, 10, 18);
        for (int x = 0; x < src.W; x++)
        {
            int top = -1, bot = -1;
            for (int y = 0; y < src.H; y++) if (src.Get(x, y).A > 0) { if (top < 0) top = y; bot = y; }
            if (top < 0) continue;
            int len = Math.Max(1, (bot - top + 1) / 4);
            for (int k = 0; k < len; k++) Blend(dx + x + 2, dy + bot - k + 1, ink, alpha);
        }
    }

    /// <summary>정수배 확대 (가장 가까운 칸 — 도트가 또렷하게).</summary>
    public Img Scale(int n)
    {
        var o = new Img(W * n, H * n);
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
            {
                int s = ((y / n) * W + x / n) * 4, t = (y * o.W + x) * 4;
                o.Px[t] = Px[s]; o.Px[t + 1] = Px[s + 1]; o.Px[t + 2] = Px[s + 2]; o.Px[t + 3] = Px[s + 3];
            }
        return o;
    }

    /// <summary>불투명 배경을 깐 사본 (PNG로 볼 때).</summary>
    public Img OnColor(Rgba bg)
    {
        var o = new Img(W, H);
        o.Fill(0, 0, W, H, bg);
        o.Draw(this, 0, 0);
        return o;
    }
}

/// <summary>결정론 난수 (같은 씨앗 → 같은 그림).</summary>
public static class DotHash
{
    public static uint U(int a, int b = 0, int c = 0)
    {
        uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return h;
    }

    public static float F(int a, int b = 0, int c = 0) => (U(a, b, c) & 0xFFFFFF) / 16777216f;
}
