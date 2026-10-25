using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 쌓아 그리기 (docs/PHYSICS.md §6-2 — 회전 도트 그림): 도트 꼴 (월드 `DotForm`)을 무게중심을 축으로 각 단 (64 — 5.6°)만큼 돌려 층마다 겹쳐 그린다.
/// 층 z마다 그 층을 지나는 기둥을 거꾸로 돌려 찾고 (그림 도트마다 2×2 잔표본 다수결 — 흐리지 않고 계단을 줄인다) z도트 위에 놓는다 (1층 = 1도트 — 3/4 시점).
/// 넓은 보기 (16도트)는 반반이면 들고 · 그림 도트 격자를 단마다 골라 (발자국이 바닥 넓이 / 4에 가장 가깝게) 돌 때 크기가 같다 · 색은 그 층에서 겉에 드러난 기둥.
/// 높은 층이 낮은 층을 덮어 남쪽에서 내려다본 겉만 남는다. 면 단은 새 가구 그림 (ArtFurniture)과 같다: 윗면 4 · 앞면 2 · 윗면 바로 아래 줄 3 ·
/// 서쪽을 보는 옆은 한 단 밝게 · 동쪽은 한 단 어둡게 · 위 · 왼쪽 외곽 1 · 아래 · 오른쪽 외곽 0. 새 그림 가구는 그 그림의 윗면 무늬를 윗면에 그대로 덮는다.
/// 세계를 읽기만 한다. 같은 꼴 · 같은 단이면 늘 같은 그림 (캐시는 `DotStackCache`).
/// </summary>
public static class DotStack
{
    public const int Steps = 64;
    /// <summary>그림 · 무게중심 바닥 점의 그림 자리 (도트 — 그리는 쪽이 물체의 화면 자리에 이 점을 맞춘다).</summary>
    public readonly record struct Drawn(Img Img, double Ax, double Ay);

    private static Rgba H(string s) => Rgba.Hex(s);
    // 면 단 (0 외곽 · 1 그늘 · 2 앞면 · 3 앞면 밝게 · 4 윗면 · 5 빛) — 윗면 색에 곱한다 (ArtFurniture 재질 단의 비율)
    private static readonly float[] Ramp = { 0.30f, 0.50f, 0.68f, 0.82f, 1f, 1.22f };
    // 재질 → 윗면 색 (materials.json 차례가 아니라 이름으로 — 표가 늘어도 그대로)
    private static readonly Dictionary<string, Rgba> MatTop = new()
    {
        ["steel"] = H("#5d6775"), ["aluminum"] = H("#a2aab1"), ["titanium"] = H("#7d8796"), ["glass"] = H("#6fa3b8"), ["polymer"] = H("#425478"),
        ["composite"] = H("#3d4450"), ["copper"] = H("#93583b"), ["ceramic"] = H("#c8ccd0"), ["foam"] = H("#6c7348"), ["electrolyte"] = H("#3d4450"),
        ["fabric"] = H("#7a4a4a"), ["food"] = H("#6fae4b"),
    };
    // 물건 이름 → 부위마다 윗면 색 (바닥 물건 그림 `Painter.LooseIcon`과 같은 색 — 없으면 재질 색)
    private static readonly Dictionary<string, Rgba[]> NameTop = new()
    {
        ["소화기"] = new[] { H("#d13b2f"), H("#1a1a1a") }, ["이산화탄소 소화기"] = new[] { H("#3a3f47"), H("#d8d8d0") },
        ["식량 상자"] = new[] { H("#9a7346") }, ["원료"] = new[] { H("#7d838c") }, ["우주복"] = new[] { H("#e6e8ec"), H("#6fa3b8") },
        ["수리 꾸러미"] = new[] { H("#5b6f8f") }, ["봉합 꾸러미"] = new[] { H("#e0b33a") }, ["구급 상자"] = new[] { H("#f2f2f2") },
        ["산소 마스크"] = new[] { H("#9ad0e8") }, ["수확물"] = new[] { H("#6fae4b") },
        ["컵"] = new[] { H("#9fc6d6") }, ["물병"] = new[] { H("#4a7ab0") }, ["유리병"] = new[] { H("#4f8a5a"), H("#2a2a2a") }, ["접시"] = new[] { H("#e0e0dc") },
        ["공구 상자"] = new[] { H("#b0422e"), H("#4a4f56") }, ["책"] = new[] { H("#7a3030") }, ["오물 통"] = new[] { H("#56506a") },
    };

    // 새 그림 가구의 남쪽 그림 (윗면 무늬를 읽는다 — 종류 · 칸 상자 · 세계 값 · 씨앗마다 한 번)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int, int, long, int), Img> _art = new();
    // 재질 번호 (꼴의 Mat — 표 차례) → 윗면 색
    private static readonly Rgba[] MatColor = ShipEngine.Tables.Default.Materials.Select(m => MatTop.GetValueOrDefault(m.Id, MatTop["steel"])).ToArray();

    /// <summary>
    /// 꼴 하나를 각 단 step (0~63 — 2π × step / 64만큼 +x에서 +y 쪽으로 · 화면에서 시계 방향)으로 그린다.
    /// scale 1 = 칸당 32도트 (도트 하나가 그림 도트 하나) · 2 = 칸당 16도트 (넓은 보기 — 그림 도트 하나에 도트 2×2 · 층 둘).
    /// fill · seed: 새 그림 가구의 윗면 무늬에 넘기는 세계 값 (없으면 빈 것). dents: 찌그러짐 둘 (월드 Prop.Dent0 | Dent1 &lt;&lt; 32 — 그 둘레 기둥을 한 단 어둡게 · PH8).
    /// 조각 꼴 (DotForm.Cut)의 깨진 면은 유리 · 세라믹이면 빛 단 5로 반짝인다.
    /// </summary>
    public static Drawn Draw(DotForm f, int step, int scale = 1, FurnFill fill = default, int seed = 0, ulong dents = 0)
    {
        if (f.Mass <= 0 || f.W == 0) return new(new Img(1, 1), 0, 0);
        int k = Math.Clamp(scale, 1, 2), words = (f.H + 63) >> 6;
        double th = (step & (Steps - 1)) * (2 * Math.PI / Steps), cs = Math.Cos(th), sn = Math.Sin(th);
        double cx = f.Cx / DotForm.Dot, cy = f.Cy / DotForm.Dot;   // 무게중심 (꼴 도트)
        double r = 0, qx0 = double.MaxValue, qx1 = double.MinValue, qy0 = double.MaxValue, qy1 = double.MinValue;
        foreach (var (x, y) in new[] { (0, 0), (f.W, 0), (0, f.D), (f.W, f.D) })
        {
            double dx = x - cx, dy = y - cy, rx = cs * dx - sn * dy, ry = sn * dx + cs * dy;
            r = Math.Max(r, Math.Sqrt(dx * dx + dy * dy));
            qx0 = Math.Min(qx0, rx); qx1 = Math.Max(qx1, rx); qy0 = Math.Min(qy0, ry); qy1 = Math.Max(qy1, ry);
        }
        int rp = (int)Math.Ceiling(r / k) + 1, side = 2 * rp, hp = (f.H + k - 1) / k, n = side * side, ih = side + hp;
        double rd = rp * k, fx0 = cx - Math.Floor(cx), fy0 = cy - Math.Floor(cy);   // 단 0이면 잔표본이 기둥 가운데에 든다
        // 돌린 발자국이 드는 바닥면 도트만 본다
        var smp = Buf(ref _smp, n * 4); var ra = Buf(ref _ra, side); var rb = Buf(ref _rb, side);
        var (px0, px1, py0, py1) = Box(qx0, qx1, qy0, qy1, rd, fx0, fy0, k, side);
        if (k == 1) Sample(f, k, cs, sn, cx, cy, rd, fx0, fy0, side, px0, px1, py0, py1, smp, ra, rb);
        else
        {
            // 넓은 보기 (그림 도트 하나 = 기둥 2×2): 그림 도트 격자를 돌린 발자국 상자의 왼쪽 위 모서리에 맞추고 (90°마다 잔표본이 기둥 가운데 · 짝수 너비는 꼭 맞는다)
            // 반 도트 (기둥 하나)씩 옮긴 넷 가운데 발자국 (바닥에서 그림 도트로 드는 것)이 꼴의 바닥 넓이 / 4에 가장 가까운 것을 쓴다 (같으면 앞의 것 · `Parity`).
            // 무게중심 자리로만 맞추면 홀수 너비 (13기둥 = 6.5도트)의 반 도트가 단마다 올림 · 내림으로 갈려 돌 때 크기가 줄었다 늘었다 했다 (우주복 단 0이 72 · 둘레는 80 넘게)
            double bx = Wrap(-qx0, k), by = Wrap(-qy0, k);
            int c = Parity(f, cs, sn, cx, cy, rd, bx, by, side, _area.GetValue(f, Area).Value / 4.0);
            fx0 = Wrap(bx + (c & 1), k); fy0 = Wrap(by + (c >> 1), k);
            (px0, px1, py0, py1) = Box(qx0, qx1, qy0, qy1, rd, fx0, fy0, k, side);
            Sample(f, k, cs, sn, cx, cy, rd, fx0, fy0, side, px0, px1, py0, py1, smp, ra, rb);
        }
        var mask = Buf(ref _mask, n * words); Array.Clear(mask, 0, n * words);
        var wz = Buf(ref _wz, side * ih); Array.Fill(wz, -1, 0, side * ih);
        var wp = Buf(ref _wp, side * ih); var wc = Buf(ref _wc, side * ih); var top = Buf(ref _top, side * ih);
        Vote(f, k, words, side, hp, py0, py1, smp, ra, rb, mask, wz, wp, wc, top);
        var shade = Buf(ref _shade, side * ih); Array.Fill(shade, (sbyte)-1, 0, side * ih);
        Shade(side, ih, words, wz, wp, top, mask, shade);
        // 새 그림 가구의 윗면 무늬 (선 꼴만 — 남쪽 그림을 보는 쪽대로 되돌려 읽는다)
        Img? art = null; int artTy = 0, artTop = 1, sw = f.W, sd = f.D, sx0 = 0;
        if (f.Lie == 0 && f.Bw > 0 && ArtFurniture.Has(f.Name))
        {
            bool sideFace = f.Face is Facing.E or Facing.W;
            int along = sideFace ? f.Bh : f.Bw, deep = sideFace ? f.Bw : f.Bh;
            if (sideFace) (sw, sd) = (f.D, f.W);
            art = _art.GetOrAdd((f.Name, along, deep, fill.Key, seed), key => ArtFurniture.Body(key.Item1, key.Item2, key.Item3, Facing.S, fill, key.Item5));
            var (front, tp) = ArtFurniture.Size(f.Name, along, deep, Facing.S);
            artTy = art.H - front - tp; artTop = Math.Max(1, tp); sx0 = (along * 32 - sw) / 2;
        }
        // 윗면 무늬 자리: 바닥면 도트 가운데의 남쪽 틀 자리는 (px, py)에 곧다 — 세 점에서 셈해 둔다
        (double x, double y) Src(double px, double py)
        {
            double qx = (px + 0.5) * k - rd - fx0, qy = (py + 0.5) * k - rd - fy0, ux = cx + cs * qx + sn * qy, uy = cy - sn * qx + cs * qy;
            return (f.Face switch { Facing.N => f.W - ux, Facing.E => f.D - uy, Facing.W => uy, _ => ux } + sx0,
                    (f.Face switch { Facing.N => f.D - uy, Facing.E => ux, Facing.W => f.W - ux, _ => uy }) * artTop / sd + artTy);
        }
        var (o0x, o0y) = Src(0, 0); var (o1x, o1y) = Src(1, 0); var (o2x, o2y) = Src(0, 1);
        var map = (o0x, o0y, o1x - o0x, o1y - o0y, o2x - o0x, o2y - o0y);
        var named = NameTop.GetValueOrDefault(f.Name);
        var ramps = named != null ? named.Select(c => Ramp.Select(m => c.Scale(m)).ToArray()).ToArray() : null;
        var img = new Img(side, ih);
        Paint(f, img, side, ih, hp, k == 2 ? 1 : 0, shade, wc, wz, top, ramps, art, map, dents);
        return new(img, (rd + fx0) / k, (rd + fy0) / k + hp);
    }

    // 돌린 발자국 상자 (q) → 드는 바닥면 도트 (격자 fx0 · fy0)
    private static (int x0, int x1, int y0, int y1) Box(double qx0, double qx1, double qy0, double qy1, double rd, double fx0, double fy0, int k, int side) =>
        (Math.Max(0, (int)Math.Floor((qx0 + rd + fx0) / k) - 1), Math.Min(side - 1, (int)Math.Ceiling((qx1 + rd + fx0) / k) + 1),
         Math.Max(0, (int)Math.Floor((qy0 + rd + fy0) / k) - 1), Math.Min(side - 1, (int)Math.Ceiling((qy1 + rd + fy0) / k) + 1));

    // v를 [0, k)로 (k에 아주 가까우면 0 — 90°의 cos 찌꺼기로 한 도트 밀리지 않게)
    private static double Wrap(double v, int k) { double m = v - k * Math.Floor(v / k); return m > k - 1e-9 ? 0 : m; }

    /// <summary>바닥면 도트마다 잔표본 넷의 기둥을 찾는다 (줄마다 곧게 · 줄마다 드는 토막 ra..rb).</summary>
    private static void Sample(DotForm f, int k, double cs, double sn, double cx, double cy, double rd, double fx0, double fy0, int side,
        int px0, int px1, int py0, int py1, int[] smp, int[] ra, int[] rb)
    {
        var solid = _solid.GetValue(f, Solid);
        int fw = f.W, fd = f.D;
        Span<double> offx = stackalloc double[4], offy = stackalloc double[4];
        for (int q = 0; q < 4; q++) { double ox = ((q & 1) - 0.5) * 0.5 * k, oy = ((q >> 1) - 0.5) * 0.5 * k; offx[q] = cs * ox + sn * oy; offy[q] = -sn * ox + cs * oy; }
        double m = 0.25 * k * (Math.Abs(cs) + Math.Abs(sn)) + 1e-9;   // 잔표본이 가운데에서 벗어나는 가장 큰 한 축 길이
        var flags = _edge.GetValue(f, Edges);
        // 그림 바닥면 도트마다 잔표본 넷의 기둥 (없으면 −1) — 줄마다 곧게 나아간다 · 줄마다 돌린 발자국이 드는 토막만 (0 ≤ ux < W · 0 ≤ uy < D가 qx에 곧다)
        for (int py = py0; py <= py1; py++)
        {
            int a = int.MaxValue, b = int.MinValue;
            for (int sy = 0; sy < 2; sy++)
            {
                double qy = py * k + (0.25 + 0.5 * sy) * k - rd - fy0;
                var (l1, h1) = Span1(cs, -cx - sn * qy, fw - cx - sn * qy);
                var (l2, h2) = Span1(-sn, -cy - cs * qy, fd - cy - cs * qy);
                double lo = Math.Max(l1, l2), hi = Math.Min(h1, h2);
                if (lo > hi) continue;
                a = Math.Min(a, (int)Math.Max(-1e6, Math.Floor((lo + rd + fx0) / k - 0.75)) - 1);
                b = Math.Max(b, (int)Math.Min(1e6, Math.Ceiling((hi + rd + fx0) / k - 0.25)) + 1);
            }
            a = Math.Max(a, px0); b = Math.Min(b, px1);
            ra[py] = a; rb[py] = b;
            if (a > b) continue;
            // 바닥면 도트 가운데에서 잔표본 넷은 늘 같은 어긋남 (±¼ 도트를 돌린 것)
            double cqy = (py + 0.5) * k - rd - fy0, cqx = (a + 0.5) * k - rd - fx0;
            double ux = cx + cs * cqx + sn * cqy, uy = cy - sn * cqx + cs * cqy, dux = cs * k, duy = -sn * k;
            for (int px = a, o = (py * side + a) * 4; px <= b; px++, o += 4, ux += dux, uy += duy)
            {
                // 잔표본이 닿을 수 있는 이웃 기둥 (가운데에서 m 안)이 가운데 기둥과 모두 같으면 넷 다 가운데 기둥이다 — 넷을 찾지 않는다
                double vx = ux + 4096, vy = uy + 4096;
                int ix = (int)vx - 4096, iy = (int)vy - 4096;
                if ((uint)ix < (uint)fw && (uint)iy < (uint)fd)
                {
                    int ci = iy * fw + ix, fl = flags[ci];
                    if (fl != 0)
                    {
                        double rx = vx - (int)vx, ry = vy - (int)vy;
                        bool w = rx < m, e = rx + m >= 1, n = ry < m, s = ry + m >= 1;
                        int need = (w ? 1 : 0) | (e ? 2 : 0) | (n ? 4 : 0) | (s ? 8 : 0) | (w && n ? 16 : 0) | (e && n ? 32 : 0) | (w && s ? 64 : 0) | (e && s ? 128 : 0);
                        fl &= need;
                    }
                    if (fl == 0) { int c = solid[ci]; smp[o] = c; smp[o + 1] = c; smp[o + 2] = c; smp[o + 3] = c; continue; }
                }
                else if (ix < -1 || iy < -1 || ix > fw || iy > fd) { smp[o] = smp[o + 1] = smp[o + 2] = smp[o + 3] = -1; continue; }
                for (int q = 0; q < 4; q++)
                {
                    double sx = ux + offx[q] + 4096, sy = uy + offy[q] + 4096;
                    int jx = (int)sx - 4096, jy = (int)sy - 4096;
                    smp[o + q] = (uint)jx < (uint)fw && (uint)jy < (uint)fd ? solid[jy * fw + jx] : -1;
                }
            }
        }
    }

    /// <summary>
    /// 넓은 보기의 격자 고르기: 반 도트씩 옮긴 네 격자의 잔표본은 모두 한 격자 (기둥 하나 간격) 위에 있다 — 한 번 찍고 2×2 덩이마다 (그림 도트 하나)
    /// 그림 도트로 드는 것 (둘 넘게 찬 것 — `Vote`의 넓은 보기와 같다)을 덩이 첫 자리의 홀짝 (x · y)대로 센다 → 발자국이 want에 가장 가까운 격자 (0~3 — x 반 도트 · y 반 도트 옮김 · 같으면 앞의 것).
    /// </summary>
    private static int Parity(DotForm f, double cs, double sn, double cx, double cy, double rd, double bx, double by, int side, double want)
    {
        var solid = _solid.GetValue(f, Solid);
        int fw = f.W, fd = f.D, L = 2 * side + 2;   // 격자 j → 화면 x = j + ½ − rd − bx (돌린 발자국 |q| ≤ r < rd − 1이 다 든다)
        var g = Buf(ref _lat, L * L); Array.Clear(g, 0, L * L);
        int i0 = L, i1 = -1, jl = L, jh = -1;   // 찍은 격자 상자 (세기는 그 안만)
        for (int i = 0; i < L; i++)
        {
            double y = i + 0.5 - rd - by;
            var (l1, h1) = Span1(cs, -cx - sn * y, fw - cx - sn * y);
            var (l2, h2) = Span1(-sn, -cy - cs * y, fd - cy - cs * y);
            double lo = Math.Max(l1, l2), hi = Math.Min(h1, h2);
            if (lo > hi) continue;
            int j0 = (int)Math.Max(0, Math.Floor(lo + rd + bx - 0.5) - 1), j1 = (int)Math.Min(L - 1, Math.Ceiling(hi + rd + bx - 0.5) + 1);
            i0 = Math.Min(i0, i); i1 = i; jl = Math.Min(jl, j0); jh = Math.Max(jh, j1);
            for (int j = j0; j <= j1; j++)
            {
                double x = j + 0.5 - rd - bx, ux = cx + cs * x + sn * y + 4096, uy = cy - sn * x + cs * y + 4096;
                int ix = (int)ux - 4096, iy = (int)uy - 4096;
                if ((uint)ix < (uint)fw && (uint)iy < (uint)fd && solid[iy * fw + ix] >= 0) g[i * L + j] = 1;
            }
        }
        Span<int> cnt = stackalloc int[4];
        for (int i = Math.Max(0, i0 - 1); i <= i1 && i + 1 < L; i++)
            for (int j = Math.Max(0, jl - 1), o = i * L + j; j <= jh && j + 1 < L; j++, o++)
                if (g[o] + g[o + 1] + g[o + L] + g[o + L + 1] >= 2) cnt[(j & 1) | (i & 1) << 1]++;
        int best = 0;
        for (int c = 1; c < 4; c++) if (Math.Abs(cnt[c] - want) < Math.Abs(cnt[best] - want)) best = c;
        return best;
    }

    // 곧은 꼴 a·t ∈ [lo, hi]인 t 토막 (a = 0이면 온통 · 빈 것)
    private static (double lo, double hi) Span1(double a, double lo, double hi) =>
        Math.Abs(a) < 1e-12 ? (lo <= 0 && 0 <= hi ? (double.MinValue, double.MaxValue) : (1, 0)) : a > 0 ? (lo / a, hi / a) : (hi / a, lo / a);

    /// <summary>
    /// 층마다 다수결 (넷 가운데 셋 넘게 · 둘이면 왼쪽 위 잔표본 · 넓은 보기는 둘이면 든다) → 화면 (z/k 도트 위) — 높은 층이 이긴다.
    /// </summary>
    private static void Vote(DotForm f, int k, int words, int side, int hp, int py0, int py1, int[] smp, int[] ra, int[] rb,
        ulong[] mask, int[] wz, int[] wp, int[] wc, bool[] top)
    {
        int ks = k == 2 ? 1 : 0;   // k는 1 아니면 2 (층 → 화면 도트는 자리 옮김)
        var bot = f.Bot; var tops = f.Top; var hid = ks == 1 ? _hid.GetValue(f, Hidden) : null;
        for (int py = py0; py <= py1; py++)
            for (int px = ra[py]; px <= rb[py]; px++)
            {
                int p = py * side + px, s0 = smp[p * 4], s1 = smp[p * 4 + 1], s2 = smp[p * 4 + 2], s3 = smp[p * 4 + 3];
                if ((s0 & s1 & s2 & s3) < 0 && s0 < 0 && s1 < 0 && s2 < 0 && s3 < 0) continue;
                int last = -1, col0 = py + hp, pw = p * words;
                if (s0 >= 0 && s0 == s1 && s0 == s2 && s0 == s3)
                    for (int z = bot[s0], z1 = tops[s0]; z < z1; z++)   // 기둥 하나 안 — 그 기둥 그대로
                    {
                        mask[pw + (z >> 6)] |= 1UL << (z & 63); last = z;
                        int si = (col0 - (z >> ks)) * side + px;
                        if (z > wz[si]) { wz[si] = z; wp[si] = p; wc[si] = s0; top[si] = false; }
                    }
                else
                {
                    int z0 = int.MaxValue, z1 = 0;
                    for (int q = 0; q < 4; q++) { int c = smp[p * 4 + q]; if (c >= 0) { z0 = Math.Min(z0, bot[c]); z1 = Math.Max(z1, tops[c]); } }
                    for (int z = z0; z < z1; z++)
                    {
                        int cnt = 0, col = -1, shown = -1; bool first = false;
                        for (int q = 0; q < 4; q++)
                        {
                            int c = smp[p * 4 + q];
                            if (c < 0 || bot[c] > z || z >= tops[c]) continue;
                            cnt++; if (col < 0) col = c; first |= q == 0;
                            if (hid != null && shown < 0 && !(hid[2 * c] <= z && z < hid[2 * c + 1])) shown = c;
                        }
                        // 반반이면 왼쪽 위 잔표본을 따른다 (한쪽 테가 늘면 맞은쪽 테가 줄어 넓이가 그대로 · 곧은 테가 빗살이 되지 않는다).
                        // 넓은 보기는 반반이면 든다 — 그림 도트 하나가 기둥 2×2라 반 덮인 테가 진짜로 반 (홀수 너비가 단마다 늘 올림 · 비스듬한 단도 같은 쪽으로)
                        if (cnt < 3 && !(cnt == 2 && (first || ks == 1))) continue;
                        if (shown >= 0) col = shown;   // 넓은 보기: 그 층에서 겉에 드러난 기둥 색 (안쪽 기둥이 앞면을 칠하지 않게 — 소화기 속 검은 기둥이 45°에서 몸의 73%를 덮었다)
                        mask[pw + (z >> 6)] |= 1UL << (z & 63); last = z;
                        int si = (col0 - (z >> ks)) * side + px;
                        if (z > wz[si]) { wz[si] = z; wp[si] = p; wc[si] = col; top[si] = false; }
                    }
                }
                if (last >= 0) { int si = (py + hp - (last >> ks)) * side + px; if (wz[si] == last && wp[si] == p) top[si] = true; }
            }
    }

    /// <summary>면 단: 윗면 4 · 앞면 2 · 윗면 바로 아래 줄 3 · 서쪽을 보는 옆 한 단 밝게 · 동쪽 어둡게.</summary>
    private static void Shade(int side, int ih, int words, int[] wz, int[] wp, bool[] top, ulong[] mask, sbyte[] shade)
    {
        for (int si = 0; si < side * ih; si++)
        {
            if (wz[si] < 0) continue;
            if (top[si]) { shade[si] = 4; continue; }
            int p = wp[si], z = wz[si], px = p % side;
            int sh = si >= side && wz[si - side] >= 0 && top[si - side] ? 3 : 2;   // 윗면 바로 아래 줄은 밝게
            bool west = px > 0 && (mask[(p - 1) * words + (z >> 6)] >> (z & 63) & 1) != 0, east = px < side - 1 && (mask[(p + 1) * words + (z >> 6)] >> (z & 63) & 1) != 0;
            if (!west) sh = Math.Min(3, sh + 1); else if (!east) sh = 1;   // 서쪽을 보는 옆 밝게 · 동쪽 어둡게
            shade[si] = (sbyte)sh;
        }
    }

    // 재질 (꼴의 Mat) × 부위 (0 · 둘째 부위 — 한 단 어둡게) → 면 단 여섯
    private static readonly Rgba[][] MatRamp = MatColor.SelectMany(c => new[] { c, c.Scale(0.88f) }).Select(c => Ramp.Select(m => c.Scale(m)).ToArray()).ToArray();

    /// <summary>면 단 → 색 (외곽 · 재질 · 부위) · 새 그림 가구의 윗면 무늬 (map: 바닥면 도트 → 그림 자리 곧은 꼴).</summary>
    private static void Paint(DotForm f, Img img, int side, int ih, int hp, int ks, sbyte[] shade, int[] wc, int[] wz, bool[] top, Rgba[][]? ramps, Img? art,
        (double x0, double y0, double xpx, double ypx, double xpy, double ypy) map, ulong dents = 0)
    {
        var px = img.Px; var mat = f.Mat; var part = f.Part; var cut = f.Cut;
        var apx = art?.Px; int aw = art?.W ?? 0, ah = art?.H ?? 0;
        for (int sy = 0; sy < ih; sy++)
            for (int sx = 0, si = sy * side; sx < side; sx++, si++)
            {
                int sh = shade[si];
                if (sh < 0) continue;
                // 외곽: 아래 · 오른쪽이 비면 0 · 위 · 왼쪽이 비면 1
                if (sy == ih - 1 || shade[si + side] < 0 || sx == side - 1 || shade[si + 1] < 0) sh = 0;
                else if (sy == 0 || shade[si - side] < 0 || sx == 0 || shade[si - 1] < 0) sh = Math.Min(sh, 1);
                int c = wc[si];
                if (cut != null && cut[c] != 0 && sh >= 2 && Glassy[Math.Min(mat[c], Glassy.Length - 1)]) sh = 5;   // 깨진 면: 유리 · 세라믹이 반짝인다 (PH8)
                else if (dents != 0 && sh > 0 && Dented(dents, c % f.W, c / f.W)) sh--;                              // 찌그러진 자리는 한 단 어둡게
                var col = ramps != null ? ramps[Math.Min(part[c], ramps.Length - 1)][sh] : MatRamp[Math.Min(mat[c] * 2 + (part[c] == 1 ? 1 : 0), MatRamp.Length - 1)][sh];
                if (apx != null && sh >= 1 && top[si])
                {
                    int ppy = sy - hp + (wz[si] >> ks);   // 윗면 도트의 바닥면 줄 (그림 칸 = 바닥면 칸 · 층만큼 올렸다)
                    int ax = (int)Math.Floor(map.x0 + map.xpx * sx + map.xpy * ppy), ay = (int)Math.Floor(map.y0 + map.ypx * sx + map.ypy * ppy);
                    if ((uint)ax < (uint)aw && (uint)ay < (uint)ah) { int ai = (ay * aw + ax) * 4; if (apx[ai + 3] > 0) col = new Rgba(apx[ai], apx[ai + 1], apx[ai + 2], apx[ai + 3]); }
                }
                int o = si * 4;
                px[o] = col.R; px[o + 1] = col.G; px[o + 2] = col.B; px[o + 3] = col.A;
            }
    }

    // 재질 번호 → 깨진 면이 반짝이나 (유리 · 세라믹)
    private static readonly bool[] Glassy = ShipEngine.Tables.Default.Materials.Select(m => m.Id is "glass" or "ceramic").ToArray();

    /// <summary>기둥 (x, y)가 찌그러짐 둘 (u · v · 반지름 도트 8비트씩 — 0이면 없음) 안인가.</summary>
    private static bool Dented(ulong dents, int x, int y)
    {
        for (int k = 0; k < 2; k++)
        {
            uint d = (uint)(dents >> (32 * k));
            if (d == 0) continue;
            int u = (int)(d & 0xFF), v = (int)(d >> 8 & 0xFF), r = (int)(d >> 16 & 0xFF);
            if ((x - u) * (x - u) + (y - v) * (y - v) <= r * r) return true;
        }
        return false;
    }

    // 꼴마다 기둥 → 여덟 이웃 가운데 이 기둥과 다른 것 (비었나 · 아래 · 위 층 · 재질 · 부위 — 꼴 밖은 빈 기둥) 비트: 서 · 동 · 북 · 남 · 북서 · 북동 · 남서 · 남동
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DotForm, byte[]> _edge = new();
    private static byte[] Edges(DotForm f)
    {
        var fl = new byte[f.W * f.D];
        ReadOnlySpan<int> dx = new[] { -1, 1, 0, 0, -1, 1, -1, 1 }, dy = new[] { 0, 0, -1, 1, -1, -1, 1, 1 };
        bool Same(int a, int x, int y)
        {
            bool sa = f.Top[a] > f.Bot[a];
            if ((uint)x >= (uint)f.W || (uint)y >= (uint)f.D) return !sa;
            int b = y * f.W + x; bool sb = f.Top[b] > f.Bot[b];
            return sa == sb && (!sa || f.Bot[a] == f.Bot[b] && f.Top[a] == f.Top[b] && f.Mat[a] == f.Mat[b] && f.Part[a] == f.Part[b]);
        }
        for (int y = 0; y < f.D; y++)
            for (int x = 0; x < f.W; x++)
                for (int d = 0; d < 8; d++) if (!Same(y * f.W + x, x + dx[d], y + dy[d])) fl[y * f.W + x] |= (byte)(1 << d);
        return fl;
    }

    // 꼴마다 기둥 → 찬 기둥 번호 (빈 기둥 −1 — 잔표본 찾기를 한 번 읽기로)
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DotForm, int[]> _solid = new();
    private static int[] Solid(DotForm f) => Enumerable.Range(0, f.W * f.D).Select(i => f.Top[i] > f.Bot[i] ? i : -1).ToArray();
    // 꼴마다 기둥 → 옆이 막힌 층 토막 [lo, hi) (네 이웃이 모두 그 층을 품는다 · 맨 윗층은 빼고 — 그 층에서 안 보이는 속) — 넓은 보기의 색 고르기
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DotForm, int[]> _hid = new();
    private static int[] Hidden(DotForm f)
    {
        var h = new int[f.W * f.D * 2];
        ReadOnlySpan<int> dx = new[] { -1, 1, 0, 0 }, dy = new[] { 0, 0, -1, 1 };
        for (int y = 0; y < f.D; y++)
            for (int x = 0; x < f.W; x++)
            {
                int i = y * f.W + x, lo = f.Bot[i], hi = f.Top[i] - 1;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d], n = ny * f.W + nx;
                    if ((uint)nx >= (uint)f.W || (uint)ny >= (uint)f.D || f.Top[n] <= f.Bot[n]) { hi = lo; break; }
                    lo = Math.Max(lo, f.Bot[n]); hi = Math.Min(hi, f.Top[n]);
                }
                h[2 * i] = lo; h[2 * i + 1] = hi;
            }
        return h;
    }

    // 꼴마다 찬 기둥 수 (바닥 넓이 — 넓은 보기의 격자 고르기)
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DotForm, System.Runtime.CompilerServices.StrongBox<int>> _area = new();
    private static System.Runtime.CompilerServices.StrongBox<int> Area(DotForm f) { int n = 0; for (int i = 0; i < f.W * f.D; i++) if (f.Top[i] > f.Bot[i]) n++; return new(n); }

    // 짓는 동안만 쓰는 판 (스레드마다 — 장마다 새로 잡지 않는다)
    [ThreadStatic] private static int[]? _smp, _wz, _wp, _wc, _ra, _rb;
    [ThreadStatic] private static byte[]? _lat;
    [ThreadStatic] private static ulong[]? _mask;
    [ThreadStatic] private static bool[]? _top;
    [ThreadStatic] private static sbyte[]? _shade;
    private static T[] Buf<T>(ref T[]? b, int n) => b != null && b.Length >= n ? b : b = new T[Math.Max(n, (b?.Length ?? 0) * 2)];
}

/// <summary>
/// 쌓은 그림 캐시 (docs/PHYSICS.md §6-2 · 부검 7): Painter의 그림 캐시와 따로 — 열쇠 (꼴 · 가면 해시 · 자세 · 기울기 단 · 각 단 · 크기 · 찌그러짐 해시) ·
/// 가장 오래 안 쓴 것부터 나간다 (바이트 상한 — 기본 32MB). 장마다 새로 짓기는 1ms까지 — 넘치면 이미 지은 가까운 각 단을 쓴다.
/// 그리는 스레드만 쓴다 (미리 굽기는 일꾼이 지어 넘기고 넣기는 그리는 스레드가).
/// </summary>
public sealed class DotStackCache
{
    public readonly record struct Key(DotForm Form, ulong Mask, byte Pose, byte Tilt, byte Step, byte Scale, uint Dent, ulong Dents = 0);
    public const double FrameBuildMs = 1;
    private const int Overhead = 96;   // 한 장의 덧 바이트 (열쇠 · 마디 · 그림 머리 어림)

    public readonly long Cap;
    public long Bytes { get; private set; }
    public int Count => _map.Count;
    /// <summary>이 장에 새로 짓는 데 쓴 시간 (ms — BeginFrame이 비운다) · 내보낸 장 수.</summary>
    public double BuiltMs { get; private set; }
    public long Evicted { get; private set; }

    private readonly Dictionary<Key, LinkedListNode<(Key key, DotStack.Drawn d)>> _map = new();
    private readonly LinkedList<(Key key, DotStack.Drawn d)> _lru = new();

    public DotStackCache(long capBytes = 32L << 20) { Cap = capBytes; }

    public void BeginFrame() => BuiltMs = 0;

    public bool TryGet(in Key k, out DotStack.Drawn d)
    {
        if (!_map.TryGetValue(k, out var node)) { d = default; return false; }
        _lru.Remove(node); _lru.AddFirst(node);
        d = node.Value.d;
        return true;
    }

    public void Put(in Key k, DotStack.Drawn d)
    {
        if (_map.TryGetValue(k, out var old)) { Bytes -= Size(old.Value.d); _lru.Remove(old); _map.Remove(k); }
        var node = _lru.AddFirst((k, d));
        _map[k] = node;
        Bytes += Size(d);
        while (Bytes > Cap && _lru.Last is { } last && last != node)
        {
            _lru.RemoveLast(); _map.Remove(last.Value.key); Bytes -= Size(last.Value.d); Evicted++;
        }
    }

    /// <summary>선 꼴 · 누운 꼴 그림: 있으면 꺼내고 · 없으면 짓는다 — 이 장에 이미 1ms를 지었으면 지어 둔 가까운 각 단 (±8단 안)을 쓴다.</summary>
    public DotStack.Drawn Get(DotForm f, int step, int scale = 1, FurnFill fill = default, int seed = 0, ulong dents = 0)
    {
        var key = new Key(f, (ulong)fill.Key, f.Lie, 0, (byte)(step & 63), (byte)scale, (uint)seed, dents);
        if (TryGet(key, out var d)) return d;
        if (BuiltMs >= FrameBuildMs)
            for (int dk = 1; dk <= 8; dk++)
                foreach (int sgn in new[] { -1, 1 })
                    if (TryGet(key with { Step = (byte)((step + sgn * dk) & 63) }, out d)) return d;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        d = DotStack.Draw(f, step, scale, fill, seed, dents);
        BuiltMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Put(key, d);
        return d;
    }

    private static long Size(DotStack.Drawn d) => d.Img.Px.Length + Overhead;
}
