using System.Collections.Concurrent;

namespace ShipEngine;

/// <summary>
/// 도트 꼴 (docs/PHYSICS.md §2-1 — 월드 · 찍지 않는다 · 정적 캐시 · 복제와 함께 쓴다): 보이는 도트가 곧 몸이다.
/// 발자국 W × D 도트 (1도트 = 1/32m) 기둥마다 아래 · 위 층 [Bot, Top) (1층 = 1/16m — 2.5m 칸이 40층) · 재질 · 부위 · 질량.
/// 빈 기둥은 Top &lt; Bot (Bot 1 · Top 0). 꼴 틀은 세상 틀과 같은 쪽 (x 동 · y 남) — 가구는 보는 쪽대로 이미 돌려 둔다.
/// </summary>
public sealed class DotForm
{
    public const double Dot = 1.0 / 32, Layer = 1.0 / 16;

    /// <summary>종류 번호 (표 차례 × 5 + 누움 — 표에 없으면 ushort.MaxValue) · 이름 · 보는 쪽 · 누움 (0 섰다 · 1~4 +x · +y · −x · −y 쪽으로 누웠다).</summary>
    public ushort Id = ushort.MaxValue;
    public string Name = "";
    public Facing Face;
    public byte Lie;
    /// <summary>발자국 (도트) · 층 수 · 칸 상자 안 발자국 모서리 (도트 — 가구만 · 물건은 0) · 칸 상자 (칸 — 가구만 · 물건은 0).</summary>
    public int W, D, H, Ox, Oy, Bw, Bh;
    public byte[] Bot = Array.Empty<byte>(), Top = Array.Empty<byte>(), Mat = Array.Empty<byte>(), Part = Array.Empty<byte>();
    /// <summary>기둥 꼭대기 층의 재질 (그 위에 놓인 것이 닿는 겉 — 침대는 천 · 탁자는 알루미늄: 받침의 반발 · 마찰 · 깨짐의 단단함 몫 — PH8).</summary>
    public byte[] TopMat = Array.Empty<byte>();
    /// <summary>기둥 질량 (kg — 재질 부피 몫 × 밀도를 표의 kg에 맞춰 늘리고 줄였다 · 합 = Mass).</summary>
    public double[] Kg = Array.Empty<double>();
    /// <summary>질량 · 무게중심 (꼴 틀 m — 발자국 모서리에서) · 회전관성 Σm(r² + d²/6) · 무게중심 높이 (m) · 바닥 마찰 반지름 (m).</summary>
    public double Mass, Cx, Cy, I, Hc, Rho;
    /// <summary>바람 16갈래 (k × 22.5° 쪽으로 부는)의 앞면 넓이 (m²) · 압력 중심의 옆 어긋남 (m — 바람의 왼쪽이 +). 처음 읽을 때 셈한다 (놓인 가구는 거의 읽지 않는다 — 늘 같은 값).</summary>
    public double[] Area16 { get { if (Volatile.Read(ref _area16) == null) DotForge.Wind(this); return _area16!; } }
    public double[] Cop16 { get { if (Volatile.Read(ref _area16) == null) DotForge.Wind(this); return _cop16!; } }
    private double[]? _area16, _cop16;
    /// <summary>가장 넓은 앞면 · 가장 작은 앞면 (m² — 16갈래 가운데 · 0보다 큰 것: 바람 깨우기를 넉넉히 거를 때 · 구멍을 막을지 지날지). 바람 갈래와 함께 센다.</summary>
    public double AreaMax { get { if (Volatile.Read(ref _area16) == null) DotForge.Wind(this); return _areaMax; } }
    public double AreaMin { get { if (Volatile.Read(ref _area16) == null) DotForge.Wind(this); return _areaMin; } }
    private double _areaMax, _areaMin;
    internal void SetWind(double[] area, double[] cop)
    {
        double a = double.MaxValue;
        foreach (double x in area) if (x > 0) a = Math.Min(a, x);
        _areaMax = area.Max(); _areaMin = a == double.MaxValue ? 0 : a;
        _cop16 = cop; Volatile.Write(ref _area16, area);
    }
    /// <summary>물체 셈의 꼴 표본 (<see cref="PropShape"/>): 처음 읽을 때 센다 — 꼴은 온 세계가 나눠 쓰니 새 세계 · 복제가 다시 세지 않는다 (물체 예산 고침).</summary>
    public PropShape Shape { get { var sh = Volatile.Read(ref _shape); if (sh == null) Volatile.Write(ref _shape, sh = new PropShape(this)); return sh; } }
    private PropShape? _shape;
    /// <summary>볼록 껍질 (꼴 틀 m · 많아야 8 꼭짓점) · 테두리 표본 (두 도트마다 · 많아야 64).</summary>
    public (double x, double y)[] Hull = Array.Empty<(double, double)>();
    public (byte x, byte y)[] Edge = Array.Empty<(byte, byte)>();
    /// <summary>넘어짐 여유 b (받침 끝까지 m — +x · +y · −x · −y).</summary>
    public double[] TipB = new double[4];
    /// <summary>끌림 계수 (상자 1.05 · 원기둥 0.8 — Hoerner) · 둥긂 (구른다) · 이음매 문턱 (J/kg).</summary>
    public double Cd = 1.05, JointEps = 20;
    public bool Round;
    /// <summary>
    /// 부위 (PH8 — 이음매): 꼴의 부위 수 · 부위가 둘인 기둥 (위아래로 붙은 부위 — 소화기 노즐 · 병뚜껑)의 아래 부위 · 위 부위가 시작하는 층 · 아래 부위 질량 · 재질
    /// (그런 기둥이 없는 꼴은 null — <see cref="Part"/>는 꼭대기 부위).
    /// </summary>
    public int Parts = 1;
    public byte[]? PartLo, PartZ, MatLo;
    public double[]? KgLo;
    /// <summary>조각 (PH8): 온전한 꼴에서 내린 층 수 (조각 밑면 = 온전한 꼴 밑면 + ZShift 층) · 깨진 면 (이웃 · 위아래가 떨어져 나간 기둥 — 그림이 반짝인다 · 온전한 꼴은 null).</summary>
    public int ZShift;
    public byte[]? Cut;
    /// <summary>조각 꼴의 누움 표시 (넘어지지 않는다 · 구르지 않는다 — 조각은 그 자세 그대로 미끄러지고 튄다).</summary>
    public const byte PieceLie = 5;

    public int Columns => W * D;
    public bool Solid(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)D && Top[y * W + x] > Bot[y * W + x];
}

/// <summary>
/// 물체 셈이 꼴마다 쓰는 표본 (<see cref="DotForm.Shape"/> — 꼴의 순수 함수 · 한 번 세면 바꾸지 않는다: 여러 스레드가 함께 읽는다):
/// 닿음 표본 (무게중심에서 — 껍질 꼭짓점 · 테두리 기둥 가운데) · 가장 먼 꼭짓점까지 (m) ·
/// 바람 발자국 표본 (무게중심에서 — 기둥 가운데 · 꼴 가운데에서 대칭으로 2도트 걸러: 대칭 꼴은 표본도 대칭이다) · 겉 재질 (바닥에 닿은 기둥이 가장 많은 재질) ·
/// 껍질 (무게중심에서 · 넓이가 +인 차례 — 변 i는 꼭짓점 i → i+1) · 변마다 바깥 법선 (물체끼리 닿음 — 껍질이 셋 아래면 비었다).
/// </summary>
public sealed class PropShape
{
    public readonly (double[] x, double[] y, double reach) Hit;
    public readonly double[] FootX, FootY;
    public readonly byte BaseMat;
    public readonly double[] HullX, HullY, HullNx, HullNy;

    internal PropShape(DotForm fm)
    {
        int n = fm.Hull.Length + fm.Edge.Length;
        var xs = new double[n]; var ys = new double[n];
        double reach = 0;
        for (int i = 0; i < fm.Hull.Length; i++)
        {
            xs[i] = fm.Hull[i].x - fm.Cx; ys[i] = fm.Hull[i].y - fm.Cy;
            reach = Math.Max(reach, Math.Sqrt(xs[i] * xs[i] + ys[i] * ys[i]));
        }
        for (int i = 0; i < fm.Edge.Length; i++)
        {
            int k = fm.Hull.Length + i;
            xs[k] = (fm.Edge[i].x + 0.5) * DotForm.Dot - fm.Cx; ys[k] = (fm.Edge[i].y + 0.5) * DotForm.Dot - fm.Cy;
            reach = Math.Max(reach, Math.Sqrt(xs[k] * xs[k] + ys[k] * ys[k]));
        }
        Hit = (xs, ys, reach);

        static bool Take(int i, int n) => Math.Abs(2 * i - (n - 1)) % 4 == ((n & 1) == 1 ? 0 : 1);
        var fx = new List<double>(); var fy = new List<double>();
        for (int y = 0; y < fm.D; y++)
            for (int x = 0; x < fm.W; x++)
                if (fm.Solid(x, y) && Take(x, fm.W) && Take(y, fm.D)) { fx.Add((x + 0.5) * DotForm.Dot - fm.Cx); fy.Add((y + 0.5) * DotForm.Dot - fm.Cy); }
        if (fx.Count == 0) { fx.Add(0); fy.Add(0); }
        FootX = fx.ToArray(); FootY = fy.ToArray();

        Span<int> cnt = stackalloc int[256];
        int low = int.MaxValue, best = -1;
        for (int i = 0; i < fm.Columns; i++) if (fm.Top[i] > fm.Bot[i]) low = Math.Min(low, fm.Bot[i]);
        for (int i = 0; i < fm.Columns; i++)
        {
            if (fm.Top[i] <= fm.Bot[i] || fm.Bot[i] != low) continue;
            if (++cnt[fm.Mat[i]] > (best < 0 ? 0 : cnt[best])) best = fm.Mat[i];
        }
        BaseMat = (byte)Math.Max(0, best);

        int nh = fm.Hull.Length >= 3 ? fm.Hull.Length : 0;
        HullX = new double[nh]; HullY = new double[nh]; HullNx = new double[nh]; HullNy = new double[nh];
        double area = 0;
        for (int i = 0; i < nh; i++) { var (x0, y0) = fm.Hull[i]; var (x1, y1) = fm.Hull[(i + 1) % nh]; area += x0 * y1 - x1 * y0; }
        for (int i = 0; i < nh; i++)
        {
            int k = area >= 0 ? i : nh - 1 - i;
            HullX[i] = fm.Hull[k].x - fm.Cx; HullY[i] = fm.Hull[k].y - fm.Cy;
        }
        for (int i = 0; i < nh; i++)
        {
            double dx = HullX[(i + 1) % nh] - HullX[i], dy = HullY[(i + 1) % nh] - HullY[i], l = Math.Sqrt(dx * dx + dy * dy);
            if (l > 0) { HullNx[i] = dy / l; HullNy[i] = -dx / l; }
        }
    }
}

/// <summary>도트 꼴을 표에서 짓는다 (늘 같게 — 같은 열쇠면 같은 꼴 하나를 함께 쓴다).</summary>
public static class DotForge
{
    private static readonly ConcurrentDictionary<(Tables, string, Facing, int, int, int), DotForm> _cache = new();

    /// <summary>
    /// 꼴 하나: 물건 · 가구 이름 · 보는 쪽 (가구 — `Layout.Facing`) · 칸 상자 (가구 — 가로 bw · 세로 bh 칸) · 누움 (0~4).
    /// </summary>
    public static DotForm Make(Tables t, string name, Facing face = Facing.S, int bw = 1, int bh = 1, int lie = 0) =>
        _cache.GetOrAdd((t, name, face, bw, bh, lie), k => Build(k.Item1, k.Item2, k.Item3, k.Item4, k.Item5, k.Item6));

    private const int Cell = 32;   // 칸 한 변 (도트)
    private static readonly string[] Metals = { "steel", "aluminum", "titanium", "copper" };
    private static readonly ConcurrentDictionary<Tables, Dictionary<string, int>> _kinds = new();

    /// <summary>
    /// 짓기: 남쪽을 보는 틀에서 도트 덩어리 (가로 · 깊이 · 층)를 칠한다 → 보는 쪽대로 돌린다 → 누움이면 수평축으로 90° 눕힌다 → 기둥으로 접고 질량을 표의 kg에 맞춘다.
    /// 가구 (설비 표에 있는 이름): 칸 상자 안 · 짠 꼴이 있으면 그 기본형 · 없으면 칸 상자에서 테두리 2도트를 뺀 상자를 재질 몫대로 (겉 = 가장 무거운 금속부터).
    /// 물건: 짠 꼴 (forms.json — 물건 표 `form` · 없으면 같은 이름) · 아니면 물건 표 크기 · 재질 · 둥긂 · 그것도 없으면 500kg/m³ 정육면체 (옛 날림 어림).
    /// </summary>
    private static DotForm Build(Tables t, string name, Facing face, int bw, int bh, int lie)
    {
        var fm = new DotForm { Name = name, Face = face, Lie = (byte)lie, Id = IdOf(t, name, lie) };
        Vox v; double kg; FormDef? def;
        if (name != "_default" && t.Equipment.TryGetValue(name, out var e))
        {
            def = t.Forms.GetValueOrDefault(name);
            kg = e.Parts.Values.Sum();
            fm.Bw = Math.Max(1, bw); fm.Bh = Math.Max(1, bh);
            v = Furniture(t, e, def, face, fm.Bw, fm.Bh, out fm.Ox, out fm.Oy);
        }
        else
        {
            var it = t.People.Items.GetValueOrDefault(name);
            def = t.Forms.GetValueOrDefault(it?.Form ?? name);
            kg = it?.Kg ?? (def is { Kg: > 0 } ? def.Kg : 2);
            fm.Round = def?.Round == true || it?.Round == true;
            v = Item(t, it, def, kg, fm.Round);
        }
        if (lie > 0) v = Lay(v, lie);
        Fold(t, fm, v, kg);
        fm.Cd = fm.Round ? 0.8 : 1.05;
        fm.JointEps = def?.Joint ?? 20;
        return fm;
    }

    /// <summary>종류 번호: 설비 표 · 물건 표 · 꼴 표 차례 (표가 같으면 늘 같다) × 5 + 누움.</summary>
    private static ushort IdOf(Tables t, string name, int lie)
    {
        var kinds = _kinds.GetOrAdd(t, tt =>
        {
            var k = new Dictionary<string, int>();
            foreach (var n in tt.Equipment.Keys) if (n != "_default") k.TryAdd(n, k.Count);
            foreach (var n in tt.People.Items.Keys) k.TryAdd(n, k.Count);
            foreach (var n in tt.Forms.Keys) k.TryAdd(n, k.Count);
            return k;
        });
        return kinds.TryGetValue(name, out int i) && i * 5 + lie < ushort.MaxValue ? (ushort)(i * 5 + lie) : ushort.MaxValue;
    }

    // ── 도트 덩어리 (짓는 동안만 — 칸 = (mat + 1) | 부위 << 8 · 0 빈칸 · 기둥마다 층이 이어져 있다) ──
    private sealed class Vox
    {
        public readonly int W, D, H;
        public readonly ushort[] C;
        public Vox(int w, int d, int h) { W = w; D = d; H = h; C = new ushort[w * d * h]; }
        public ref ushort this[int x, int y, int z] => ref C[(y * W + x) * H + z];
        public Span<ushort> Col(int x, int y) => C.AsSpan((y * W + x) * H, H);
    }

    /// <summary>
    /// 가구: 남쪽을 보는 틀 (뒤 = 북쪽 벽 · E · W는 칸 상자를 돌려 본다)에서 발자국 가로 = 칸 × 32 − 옆 테두리 × 2 · 깊이 = 꼴 표 (그림 윗면과 같게) ·
    /// 얕으면 (칸 깊이보다 8도트 넘게 얕다) 뒤 벽에 등을 대고 (1도트 틈) · 아니면 가운데. 층 = 표 h × 16 (없으면 16).
    /// </summary>
    private static Vox Furniture(Tables t, EquipmentDef e, FormDef? def, Facing face, int bw, int bh, out int ox, out int oy)
    {
        bool side = face is Facing.E or Facing.W;
        int along = (side ? bh : bw) * Cell, deep = (side ? bw : bh) * Cell;
        int h = Math.Clamp((int)Math.Round(e.H > 0 ? e.H * 16 : 16), 1, 40);
        int ws, ds;
        if (e.Foot is { Length: 2 }) { ws = (int)Math.Round(e.Foot[0] * Cell); ds = (int)Math.Round(e.Foot[1] * Cell); }
        else
        {
            ws = along - 2 * (def?.Mx ?? 2);
            int dr = (def?.Depth ?? new[] { -4, -4 })[(side ? bw : bh) > 1 ? 1 : 0];
            ds = dr > 0 ? dr : deep + dr;
        }
        ws = Math.Clamp(ws, 1, along); ds = Math.Clamp(ds, 1, deep);
        int xs0 = (along - ws) / 2, ys0 = ds <= deep - 8 ? 1 : (deep - ds) / 2;
        var v = new Vox(ws, ds, h);
        if (def is { Parts.Count: > 0 }) Paint(t, v, def.Parts);
        else Shares(t, v, e.Parts);
        // 남쪽 틀 → 세상 틀 (N 반 바퀴 · E 앞이 동쪽 · W 앞이 서쪽) · 칸 상자 안 모서리도 같이 돌린다
        switch (face)
        {
            case Facing.N: ox = along - xs0 - ws; oy = deep - ys0 - ds; return Rot(v, 2);
            case Facing.E: ox = ys0; oy = along - xs0 - ws; return Rot(v, 3);
            case Facing.W: ox = deep - ys0 - ds; oy = xs0; return Rot(v, 1);
            default: ox = xs0; oy = ys0; return v;
        }
    }

    private static Vox Item(Tables t, ItemDef? it, FormDef? def, double kg, bool round)
    {
        if (def is { Parts.Count: > 0 })
        {
            var v = new Vox(Math.Max(1, def.W), Math.Max(1, def.D), Math.Clamp(def.Parts.Max(p => p.Z[1]), 1, 40));
            Paint(t, v, def.Parts);
            return v;
        }
        int w, d, h; string mat;
        if (it?.Size is { Length: 3 } sz) { w = (int)Math.Round(sz[0] * Cell); d = (int)Math.Round(sz[1] * Cell); h = (int)Math.Round(sz[2] * 16); mat = it.Mat ?? "polymer"; }
        else { double a = Math.Cbrt(kg / 500); w = d = (int)Math.Round(a * Cell); h = (int)Math.Round(a * 16); mat = "polymer"; }
        var b = new Vox(Math.Max(1, w), Math.Max(1, d), Math.Clamp(h, 1, 40));
        Paint(t, b, new List<FormPart> { round ? new FormPart { Disc = Math.Max(b.W, b.D), Mat = mat } : new FormPart { Rect = new[] { 0, 0, 0, 0 }, Mat = mat } });
        return b;
    }

    /// <summary>기본형을 차례로 칠한다 (뒤가 앞을 덮는다). 음수 x · y는 끝에서 · 0 이하 w · d는 끝까지에서 뺀 값 · 0 이하 위 층은 꼭대기에서 뺀 값.</summary>
    private static void Paint(Tables t, Vox v, List<FormPart> parts)
    {
        foreach (var p in parts)
        {
            ushort c = (ushort)(t.M(p.Mat).Index + 1 | p.Part << 8);
            int z0 = p.Z[0] < 0 ? v.H + p.Z[0] : p.Z[0], z1 = p.Z[1] <= 0 ? v.H + p.Z[1] : p.Z[1];
            z0 = Math.Clamp(z0, 0, v.H); z1 = Math.Clamp(z1, 0, v.H);
            if (z1 <= z0) continue;
            int x0 = 0, y0 = 0, x1 = v.W, y1 = v.D;
            if (p.Rect is { } r)
            {
                x0 = r[0] < 0 ? v.W + r[0] : r[0]; y0 = r[1] < 0 ? v.D + r[1] : r[1];
                x1 = r[2] <= 0 ? v.W + r[2] : x0 + r[2]; y1 = r[3] <= 0 ? v.D + r[3] : y0 + r[3];
                x0 = Math.Max(x0, 0); y0 = Math.Max(y0, 0); x1 = Math.Min(x1, v.W); y1 = Math.Min(y1, v.D);
            }
            double rr = p.Disc / 2;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    if (p.Rect == null) { double dx = x + 0.5 - v.W / 2.0, dy = y + 0.5 - v.D / 2.0; if (dx * dx + dy * dy > rr * rr + 1e-9) continue; }
                    v.Col(x, y)[z0..z1].Fill(c);
                }
        }
    }

    /// <summary>
    /// 짠 꼴이 없는 가구: 발자국 기둥을 바깥 둘레부터 (테 · 그다음 테 …) 재질 부피 몫대로 나눠 꽉 채운다 — 겉은 가장 무거운 금속 · 다음 금속 · 그 밖은 무거운 것부터.
    /// 부위 = 그 재질의 차례 (재질이 바뀌는 곳이 이음매).
    /// </summary>
    private static void Shares(Tables t, Vox v, Dictionary<string, double> parts)
    {
        var mats = parts.Where(kv => kv.Value > 0).Select((kv, i) => (id: kv.Key, kg: kv.Value, i))
            .OrderBy(m => Array.IndexOf(Metals, m.id) < 0 ? 1 : 0).ThenByDescending(m => m.kg).ThenBy(m => m.i).ToList();
        if (mats.Count == 0) mats.Add(("steel", 1, 0));
        int n = v.W * v.D;
        var vol = mats.Select(m => m.kg / t.M(m.id).Def.Density).ToArray();
        double sum = vol.Sum();
        // 기둥 수: 부피 몫 (가장 큰 나머지) · 재질마다 적어도 하나 (기둥이 모자라지 않으면)
        var cnt = vol.Select(x => (int)Math.Floor(x / sum * n)).ToArray();
        if (n >= mats.Count) for (int k = 0; k < cnt.Length; k++) if (cnt[k] == 0) cnt[k] = 1;
        while (cnt.Sum() > n) { int k = Array.IndexOf(cnt, cnt.Max()); cnt[k]--; }
        var order = Enumerable.Range(0, cnt.Length).OrderByDescending(k => vol[k] / sum * n - Math.Floor(vol[k] / sum * n)).ThenBy(k => k).ToArray();
        for (int j = 0; cnt.Sum() < n; j++) cnt[order[j % order.Length]]++;
        var cols = new int[n]; var ring = new long[n];
        for (int i = 0; i < n; i++) { int x = i % v.W, y = i / v.W; cols[i] = i; ring[i] = (long)Math.Min(Math.Min(x, y), Math.Min(v.W - 1 - x, v.D - 1 - y)) * n + i; }
        Array.Sort(ring, cols);
        int at = 0;
        for (int k = 0; k < mats.Count; k++)
        {
            ushort c = (ushort)(t.M(mats[k].id).Index + 1 | Math.Min(k, 15) << 8);
            for (int j = 0; j < cnt[k]; j++, at++) v.Col(cols[at] % v.W, cols[at] / v.W).Fill(c);
        }
    }

    /// <summary>제자리 돌기 q × 90° (+x → +y 쪽으로 — 화면에서 시계 방향).</summary>
    private static Vox Rot(Vox v, int q)
    {
        for (; (q & 3) != 0; q--)
        {
            var o = new Vox(v.D, v.W, v.H);
            for (int y = 0; y < v.D; y++) for (int x = 0; x < v.W; x++) v.Col(x, y).CopyTo(o.Col(v.D - 1 - y, x));
            v = o;
        }
        return v;
    }

    /// <summary>
    /// 눕기 (1 +x · 2 +y · 3 −x · 4 −y 쪽으로): 그 쪽 면이 바닥에 닿게 수평축으로 90° 돌려 다시 기둥으로 —
    /// 층 하나 (1/16m)가 도트 둘이고 도트 둘이 층 하나라 부피가 같다 (질량은 다시 표에 맞춘다).
    /// </summary>
    private static Vox Lay(Vox v, int lie)
    {
        int pre = lie switch { 2 => 3, 3 => 2, 4 => 1, _ => 0 };
        v = Rot(v, pre);
        var o = new Vox(2 * v.H, v.D, (v.W + 1) / 2);
        for (int z2 = 0; z2 < o.H; z2++)
            for (int y = 0; y < v.D; y++)
                for (int x2 = 0; x2 < o.W; x2++)
                {
                    int z = x2 >> 1, xa = v.W - 1 - 2 * z2, xb = xa - 1;
                    ushort c = v[xa, y, z];
                    if (c == 0 && xb >= 0) c = v[xb, y, z];
                    o[x2, y, z2] = c;
                }
        return Rot(o, (4 - pre) & 3);
    }

    /// <summary>덩어리 → 기둥 (아래 · 위 층 · 가장 무거운 재질 · 꼭대기 부위 · 질량) · 무게중심 · 회전관성 · 마찰 반지름 · 넘어짐 여유 · 껍질 · 테두리 (바람 넓이는 처음 읽을 때).</summary>
    private static void Fold(Tables t, DotForm fm, Vox v, double kg)
    {
        const double d = DotForm.Dot, dz = DotForm.Layer;
        int W = v.W, D = v.D, n = W * D;
        fm.W = W; fm.D = D; fm.H = v.H;
        fm.Bot = new byte[n]; fm.Top = new byte[n]; fm.Mat = new byte[n]; fm.Part = new byte[n]; fm.Kg = new double[n]; fm.TopMat = new byte[n];
        var dens = new double[t.Materials.Count];
        for (int k = 0; k < dens.Length; k++) dens[k] = t.Materials[k].Def.Density;
        double raw = 0;
        foreach (var c in v.C) if (c != 0) raw += dens[(c & 0xFF) - 1];
        double s = raw > 0 ? kg / raw : 0;   // 도트 칸 하나의 질량 = 밀도 × s (속 빈 정도를 흡수)
        double m = 0, mx = 0, my = 0, mz = 0;
        Span<double> byMat = stackalloc double[t.Materials.Count], byLo = stackalloc double[t.Materials.Count];
        var partLo = new byte[n]; var partZ = new byte[n]; var matLo = new byte[n]; var kgLo = new double[n];
        bool split = false;
        uint partSet = 0;
        for (int y = 0; y < D; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x, bot = -1, top = -1, lo = -1, pz = -1;
                byMat.Clear(); byLo.Clear();
                double ck = 0, cl = 0;
                var col = v.Col(x, y);
                for (int z = 0; z < v.H; z++)
                {
                    ushort c = col[z];
                    if (c == 0) continue;
                    if (bot < 0) bot = z;
                    top = z;
                    double k = dens[(c & 0xFF) - 1] * s;
                    byMat[(c & 0xFF) - 1] += k; ck += k; mz += k * (z + 0.5) * dz;
                    fm.Part[i] = (byte)(c >> 8); fm.TopMat[i] = (byte)((c & 0xFF) - 1);
                    int pt = c >> 8;
                    if (lo < 0) lo = pt; else if (pz < 0 && pt != lo) pz = z;   // 아래 부위 · 위 부위가 시작하는 층 (이음매)
                    if (pz < 0) { cl += k; byLo[(c & 0xFF) - 1] += k; }
                }
                if (bot < 0) { fm.Bot[i] = 1; fm.Top[i] = 0; continue; }
                fm.Bot[i] = (byte)bot; fm.Top[i] = (byte)(top + 1);
                int best = 0; for (int k = 1; k < byMat.Length; k++) if (byMat[k] > byMat[best]) best = k;
                fm.Mat[i] = (byte)best; fm.Kg[i] = ck;
                m += ck; mx += ck * (x + 0.5) * d; my += ck * (y + 0.5) * d;
                partSet |= 1u << (lo & 31) | 1u << (fm.Part[i] & 31);
                partLo[i] = (byte)lo; partZ[i] = (byte)(pz >= 0 ? pz : bot); kgLo[i] = pz >= 0 ? cl : ck;
                int bl = 0; for (int k = 1; k < byLo.Length; k++) if (byLo[k] > byLo[bl]) bl = k;
                matLo[i] = (byte)(pz >= 0 ? bl : best);
                split |= pz >= 0;
            }
        fm.Parts = System.Numerics.BitOperations.PopCount(partSet);
        if (split) { fm.PartLo = partLo; fm.PartZ = partZ; fm.MatLo = matLo; fm.KgLo = kgLo; }
        fm.Mass = m;
        if (m <= 0) return;
        fm.Cx = mx / m; fm.Cy = my / m; fm.Hc = mz / m;
        double I = 0, rho = 0; int nr = 0;
        int sx0 = int.MaxValue, sy0 = int.MaxValue, sx1 = -1, sy1 = -1;
        bool grounded = false;
        for (int i = 0; i < n; i++) if (fm.Top[i] > fm.Bot[i] && fm.Bot[i] == 0) { grounded = true; break; }
        for (int y = 0; y < D; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (fm.Top[i] <= fm.Bot[i]) continue;
                double rx = (x + 0.5) * d - fm.Cx, ry = (y + 0.5) * d - fm.Cy;
                I += fm.Kg[i] * (rx * rx + ry * ry + d * d / 6);
                if (grounded && fm.Bot[i] != 0) continue;   // 받침: 바닥에 닿은 기둥 (떠 있는 꼴이면 모두)
                rho += Math.Sqrt(rx * rx + ry * ry); nr++;
                sx0 = Math.Min(sx0, x); sy0 = Math.Min(sy0, y); sx1 = Math.Max(sx1, x); sy1 = Math.Max(sy1, y);
            }
        fm.I = I; fm.Rho = rho / nr;
        fm.TipB[0] = (sx1 + 1) * d - fm.Cx; fm.TipB[1] = (sy1 + 1) * d - fm.Cy; fm.TipB[2] = fm.Cx - sx0 * d; fm.TipB[3] = fm.Cy - sy0 * d;
        Outline(fm);
    }

    private static readonly ConcurrentDictionary<(DotForm, byte, int, string), DotForm> _pieces = new();

    /// <summary>
    /// 조각 꼴 (PH8 — §5-2): 온전한 꼴 orig의 기둥 가운데 가면 (줄 y0부터 줄마다 ulong — x 비트)에 든 것 · 부위 part (255 기둥 통째 · 아니면 그 부위의 층만).
    /// 질량 · 무게중심 · 회전관성 · 마찰 반지름 · 넘어짐 여유 · 껍질을 그 기둥으로 다시 센다 (기둥 질량 합 — 조각끼리 더하면 온전한 꼴) · 밑면을 바닥으로 내린다 (ZShift) ·
    /// 깨진 면 (온전한 꼴에서 이웃 · 위아래가 떨어져 나간 기둥). 같은 가면이면 같은 꼴 하나를 함께 쓴다 (그림 캐시 · 여러 세계).
    /// </summary>
    public static DotForm Piece(DotForm orig, ReadOnlySpan<ulong> rows, int y0, byte part)
    {
        var key = (orig, part, y0, Convert.ToHexString(System.Runtime.InteropServices.MemoryMarshal.AsBytes(rows)));
        if (_pieces.TryGetValue(key, out var hit)) return hit;
        var copy = rows.ToArray();
        return _pieces.GetOrAdd(key, _ => BuildPiece(orig, copy, y0, part));
    }

    private static DotForm BuildPiece(DotForm orig, ulong[] rows, int y0, byte part)
    {
        int W = orig.W, D = orig.D, n = W * D;
        var fm = new DotForm { Name = orig.Name, Face = orig.Face, Lie = DotForm.PieceLie, Id = orig.Id, W = W, D = D, Cd = 1.05, JointEps = orig.JointEps };
        fm.Bot = new byte[n]; fm.Top = new byte[n]; fm.Mat = new byte[n]; fm.Part = new byte[n]; fm.Kg = new double[n]; fm.Cut = new byte[n]; fm.TopMat = new byte[n];
        Array.Fill(fm.Bot, (byte)1);
        bool In(int x, int y) => (uint)x < (uint)W && y >= y0 && y < y0 + rows.Length && (rows[y - y0] >> x & 1) != 0 && orig.Solid(x, y);
        int zmin = int.MaxValue;
        for (int y = y0; y < y0 + rows.Length && y < D; y++)
            for (int x = 0; x < W; x++)
            {
                if (!In(x, y)) continue;
                int i = y * W + x;
                bool two = orig.PartLo != null && orig.PartLo[i] != orig.Part[i];
                if (part == 255 || !two)
                {
                    if (part != 255 && orig.Part[i] != part) continue;
                    fm.Bot[i] = orig.Bot[i]; fm.Top[i] = orig.Top[i]; fm.Mat[i] = orig.Mat[i]; fm.Part[i] = orig.Part[i]; fm.Kg[i] = orig.Kg[i]; fm.TopMat[i] = orig.TopMat[i];
                }
                else if (orig.PartLo![i] == part) { fm.Bot[i] = orig.Bot[i]; fm.Top[i] = orig.PartZ![i]; fm.Mat[i] = orig.MatLo![i]; fm.Part[i] = part; fm.Kg[i] = orig.KgLo![i]; fm.Cut[i] = 1; fm.TopMat[i] = orig.MatLo[i]; }
                else if (orig.Part[i] == part) { fm.Bot[i] = orig.PartZ![i]; fm.Top[i] = orig.Top[i]; fm.Mat[i] = orig.Mat[i]; fm.Part[i] = part; fm.Kg[i] = orig.Kg[i] - orig.KgLo![i]; fm.Cut[i] = 1; fm.TopMat[i] = orig.TopMat[i]; }
                else continue;
                zmin = Math.Min(zmin, fm.Bot[i]);
            }
        if (zmin == int.MaxValue) { fm.Bot = new byte[n]; Array.Fill(fm.Bot, (byte)1); return fm; }
        fm.ZShift = orig.ZShift + zmin;
        int h = 0;
        for (int i = 0; i < n; i++)
        {
            if (fm.Top[i] <= fm.Bot[i]) continue;
            fm.Bot[i] -= (byte)zmin; fm.Top[i] -= (byte)zmin; h = Math.Max(h, fm.Top[i]);
        }
        fm.H = h;
        // 깨진 면: 온전한 꼴에서는 찼던 4이웃이 이 조각에 없다 (위아래로 갈린 기둥은 이미 표시했다)
        for (int y = 0; y < D; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (fm.Top[i] <= fm.Bot[i]) continue;
                if (orig.Solid(x - 1, y) && !fm.Solid(x - 1, y) || orig.Solid(x + 1, y) && !fm.Solid(x + 1, y) || orig.Solid(x, y - 1) && !fm.Solid(x, y - 1) || orig.Solid(x, y + 1) && !fm.Solid(x, y + 1)) fm.Cut[i] = 1;
            }
        FoldColumns(fm);
        return fm;
    }

    /// <summary>기둥 (아래 · 위 층 · 질량)에서 질량 · 무게중심 · 무게중심 높이 (기둥 가운데 층 — 조각의 어림) · 회전관성 · 마찰 반지름 · 넘어짐 여유 · 껍질 (조각 꼴).</summary>
    private static void FoldColumns(DotForm fm)
    {
        const double d = DotForm.Dot, dz = DotForm.Layer;
        int n = fm.W * fm.D;
        double m = 0, mx = 0, my = 0, mz = 0;
        for (int y = 0; y < fm.D; y++)
            for (int x = 0; x < fm.W; x++)
            {
                int i = y * fm.W + x;
                if (fm.Top[i] <= fm.Bot[i]) continue;
                double k = fm.Kg[i];
                m += k; mx += k * (x + 0.5) * d; my += k * (y + 0.5) * d; mz += k * (fm.Bot[i] + fm.Top[i]) * 0.5 * dz;
            }
        fm.Mass = m;
        if (m <= 0) return;
        fm.Cx = mx / m; fm.Cy = my / m; fm.Hc = mz / m;
        double I = 0, rho = 0; int nr = 0;
        int sx0 = int.MaxValue, sy0 = int.MaxValue, sx1 = -1, sy1 = -1;
        for (int y = 0; y < fm.D; y++)
            for (int x = 0; x < fm.W; x++)
            {
                int i = y * fm.W + x;
                if (fm.Top[i] <= fm.Bot[i]) continue;
                double rx = (x + 0.5) * d - fm.Cx, ry = (y + 0.5) * d - fm.Cy;
                I += fm.Kg[i] * (rx * rx + ry * ry + d * d / 6);
                if (fm.Bot[i] != 0) continue;
                rho += Math.Sqrt(rx * rx + ry * ry); nr++;
                sx0 = Math.Min(sx0, x); sy0 = Math.Min(sy0, y); sx1 = Math.Max(sx1, x); sy1 = Math.Max(sy1, y);
            }
        fm.I = I; fm.Rho = nr > 0 ? rho / nr : 0;
        fm.TipB[0] = (sx1 + 1) * d - fm.Cx; fm.TipB[1] = (sy1 + 1) * d - fm.Cy; fm.TipB[2] = fm.Cx - sx0 * d; fm.TipB[3] = fm.Cy - sy0 * d;
        Outline(fm);
    }

    /// <summary>
    /// 바람 16갈래: 부는 쪽에 수직인 축 u로 기둥을 비춘 그림자 (u 반 도트 × 층 칸)의 넓이 · 그 가운데가 무게중심에서 어긋난 만큼.
    /// 칸 눈금은 무게중심에서 잰다 (칸 가운데 = 홀수 J × 반의반 도트) — 대칭 꼴이면 그림자도 꼭 대칭이라 어긋남이 0이다 (고른 바람에 돌지 않는다).
    /// </summary>
    internal static void Wind(DotForm fm)
    {
        const double d = DotForm.Dot, b = d / 2, eps = 1e-9;
        int W = fm.W, words = (fm.H + 63) / 64;
        double[] area = new double[16], cop = new double[16];
        // 기둥마다 층 비트 (64층씩) — 갈래마다 다시 셈하지 않는다
        var cols = new List<int>();
        for (int i = 0; i < W * fm.D; i++) if (fm.Top[i] > fm.Bot[i]) cols.Add(i);
        var bits = new ulong[cols.Count * words];
        for (int k = 0; k < cols.Count; k++)
            for (int z = fm.Bot[cols[k]]; z < fm.Top[cols[k]]; z++) bits[k * words + (z >> 6)] |= 1UL << (z & 63);
        var lo = new int[cols.Count]; var hi = new int[cols.Count];
        for (int a16 = 0; a16 < 16; a16++)
        {
            double a = a16 * Math.PI / 8, ux = -Math.Sin(a), uy = Math.Cos(a), hw = d * (Math.Abs(ux) + Math.Abs(uy)) / 2;
            int jmin = int.MaxValue, jmax = int.MinValue;
            for (int k = 0; k < cols.Count; k++)
            {
                int c = cols[k];
                double rel = ux * ((c % W + 0.5) * d - fm.Cx) + uy * ((c / W + 0.5) * d - fm.Cy);
                int j0 = (int)Math.Ceiling((rel - hw - eps) * 2 / b), j1 = (int)Math.Floor((rel + hw + eps) * 2 / b);
                if ((j0 & 1) == 0) j0++;
                if ((j1 & 1) == 0) j1--;
                lo[k] = j0; hi[k] = j1;
                if (j0 <= j1) { jmin = Math.Min(jmin, j0); jmax = Math.Max(jmax, j1); }
            }
            if (jmin > jmax) continue;
            int nb = (jmax - jmin) / 2 + 1;
            var mask = new ulong[nb * words];
            for (int k = 0; k < cols.Count; k++)
                for (int jj = lo[k]; jj <= hi[k]; jj += 2) for (int w = 0; w < words; w++) mask[(jj - jmin) / 2 * words + w] |= bits[k * words + w];
            long cells = 0, sj = 0;
            for (int q = 0; q < nb; q++)
            {
                int pc = 0;
                for (int w = 0; w < words; w++) pc += System.Numerics.BitOperations.PopCount(mask[q * words + w]);
                cells += pc; sj += (long)pc * (jmin + 2 * q);
            }
            area[a16] = cells * b * DotForm.Layer;
            cop[a16] = cells > 0 ? sj * (b / 2) / cells : 0;
        }
        fm.SetWind(area, cop);
    }

    /// <summary>볼록 껍질 (기둥 모서리 — 단조 사슬 · 8 꼭짓점이 넘으면 가장 작은 세모부터 뺀다) · 테두리 표본 (빈 이웃이 있는 기둥 · 둘째마다 · 64까지).</summary>
    private static void Outline(DotForm fm)
    {
        const double d = DotForm.Dot;
        var pts = new List<(double x, double y)>();
        var edge = new List<(byte, byte)>();
        for (int y = 0; y < fm.D; y++)
        {
            int first = -1, last = -1;
            for (int x = 0; x < fm.W; x++)
            {
                if (!fm.Solid(x, y)) continue;
                if (first < 0) first = x;
                last = x;
                if (!(fm.Solid(x - 1, y) && fm.Solid(x + 1, y) && fm.Solid(x, y - 1) && fm.Solid(x, y + 1))) edge.Add(((byte)x, (byte)y));
            }
            if (first < 0) continue;   // 줄 양 끝 기둥의 네 모서리만 껍질 후보다
            pts.Add((first * d, y * d)); pts.Add((first * d, (y + 1) * d)); pts.Add(((last + 1) * d, y * d)); pts.Add(((last + 1) * d, (y + 1) * d));
        }
        pts = pts.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        static double Cross((double x, double y) o, (double x, double y) a, (double x, double y) b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var hull = new List<(double x, double y)>();
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            foreach (var p in pass == 0 ? pts : Enumerable.Reverse(pts))
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        while (hull.Count > 8)
        {
            int best = 0; double area = double.MaxValue;
            for (int i = 0; i < hull.Count; i++)
            {
                double a = Math.Abs(Cross(hull[(i + hull.Count - 1) % hull.Count], hull[i], hull[(i + 1) % hull.Count]));
                if (a < area) { area = a; best = i; }
            }
            hull.RemoveAt(best);
        }
        fm.Hull = hull.ToArray();
        int stride = Math.Max(2, (edge.Count + 63) / 64);
        fm.Edge = edge.Where((_, i) => i % stride == 0).Take(64).ToArray();
    }
}
