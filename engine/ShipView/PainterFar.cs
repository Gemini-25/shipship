using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ShipEngine;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipView;

/// <summary>
/// 넓은 보기 화가 (docs/GRAPHICS.md §3 T0 세밀도 단계 · 6단 60프레임): 칸 하나를 D = 16 · 8도트로 그린다 (배 전체 보기 32도트 20ms → 몇 ms).
/// 늘 같은 층 (바닥 · 벽 뚜껑 · 앞면 · 문턱 · 잔칸 · 녹 · 실금 · 외판 구멍 · 그을음 단 · 쏟은 액체 · 설비 몸)은 8×8칸 덩어리로 32도트에 한 번 구워
/// 2×2 알파 평균으로 줄여 둔다 (8도트는 그것을 다시 2×2). 덩어리 도장 (구멍 · 잔칸 · 끊김 · 녹 · 실금 · 그을음 단 · 액체 · 설비 채움 · 꺼짐)이 바뀌면 다시 굽는다.
/// 장마다 그리는 층: 별 · 문짝 · 바닥 물건 · 물 · 서리 · 열 · 사람 · 로봇 (앞 가림까지 — 32도트로 그려 줄인다) · 물방울 · 연기 · 빛 (칸 모서리 빛을 잇는다) · 설비 빛 · 불 · 겹쳐 보기.
/// 넓은 보기에 없는 것: 빛 웅덩이 · 벽 밑 그림자 · 벽 띠등 · 새는 공기 줄기 · 아지랑이 · 합선 불꽃 · 충격파 · 방사선 잡음 (칸이 14~30픽셀이라 거의 안 보인다).
/// 떨어져 나간 조각이 있으면 (드물다) 32도트로 그려 줄인다. 세계는 읽기만.
/// </summary>
public sealed partial class Painter
{
    /// <summary>굽는 덩어리 한 변 (칸).</summary>
    public const int Chunk = 8;
    /// <summary>한 장에 도장이 바뀐 덩어리를 다시 굽는 시간 (ms) — 처음 보는 덩어리는 이 끝과 상관없이 그 장에 굽는다 · 0이면 바뀐 것을 모두 그 장에.</summary>
    public static double FarBakeMs = 1.0;
    /// <summary>한 장에 다시 굽는 덩어리 수 끝 (0이면 끝 없음 — 시험은 시간 대신 이것으로 묶는다: 같은 세계 같은 그림).</summary>
    public static int FarBakeMax;
    /// <summary>재기용: 지금까지 구운 덩어리 수.</summary>
    public int Baked { get; private set; }

    private bool _bake;
    private sealed class FarChunk { public Img? D16, D8; public ulong Stamp; public bool Has; public int[] Furn = Array.Empty<int>(); }
    private FarChunk[]? _chunks;
    private int _cw, _chn, _prebake, _farTurn;
    private Img? _bakeInto, _agentInto;
    private readonly List<int> _stale = new();
    /// <summary>구울 때 모은 솟은 설비 (도면 32도트 자리 — 사람 앞 가림에 쓴다).</summary>
    private readonly Dictionary<long, (Img img, int x, int y, int baseRow)> _farFront = new();
    private readonly ConditionalWeakTable<Img, Img?[]> _down = new();
    private const int AgW = 72, AgH = 84, AgOx = 20, AgOy = 34;   // 사람 · 로봇을 32도트로 그릴 작은 판 (칸 왼쪽 위 자리)

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    private static int Mod(int a, int b) => (a % b + b) % b;

    /// <summary>그을음 단 (√2배마다 — 굽는 덩어리는 이 값으로 그린다 · 불 동안 다시 굽기가 몰리지 않게).</summary>
    private static int SootIdx(double g) => g <= 0.05 ? 0 : 1 + (int)Math.Ceiling(2 * Math.Log2(g / 0.05));
    private static double SootStep(double g) => g <= 0.05 ? g : 0.05 * Math.Pow(2, (SootIdx(g) - 1) / 2.0);

    /// <summary>k×k 알파 가중 평균으로 줄인다 (투명한 곳은 빛깔에 섞지 않는다).</summary>
    public static Img Down(Img src, int k)
    {
        if (k == 1) return src;
        int w = (src.W + k - 1) / k, h = (src.H + k - 1) / k;
        var o = new Img(w, h);
        var s = src.Px; var d = o.Px;
        bool opaque = true;
        for (int y = 0; y < h; y++)
        {
            int yb = Math.Min(src.H, y * k + k);
            for (int x = 0; x < w; x++)
            {
                int a = 0, r = 0, g = 0, b = 0, n = 0, xb = Math.Min(src.W, x * k + k);
                for (int yy = y * k; yy < yb; yy++)
                    for (int xx = x * k; xx < xb; xx++)
                    {
                        int i = (yy * src.W + xx) * 4, pa = s[i + 3];
                        n++;
                        if (pa == 0) continue;
                        a += pa; r += s[i] * pa; g += s[i + 1] * pa; b += s[i + 2] * pa;
                    }
                if (a == 0) { opaque = false; continue; }
                int j = (y * w + x) * 4;
                d[j] = (byte)(r / a); d[j + 1] = (byte)(g / a); d[j + 2] = (byte)(b / a); d[j + 3] = (byte)(a / n);
                if (d[j + 3] != 255) opaque = false;
            }
        }
        o.Opaque = opaque;
        return o;
    }

    /// <summary>캐시한 그림의 줄인 그림 (그림마다 한 번).</summary>
    private Img DownOf(Img src, int k)
    {
        if (k == 1) return src;
        var arr = _down.GetValue(src, static _ => new Img?[2]);
        return arr[k == 2 ? 0 : 1] ??= Down(src, k);
    }

    private void EnsureChunks()
    {
        if (_chunks != null) return;
        _cw = (L.W + Chunk - 1) / Chunk; _chn = (L.H + Chunk - 1) / Chunk;
        var lists = new List<int>[_cw * _chn];
        for (int k = 0; k < lists.Length; k++) lists[k] = new List<int>();
        for (int i = 0; i < L.Furniture.Count; i++)
        {
            if (L.Furniture[i].Cells.Length == 0) continue;
            var (bx, by, bw, bh) = _fixBox[i];
            for (int cy = 0; cy < _chn; cy++)
                for (int cx = 0; cx < _cw; cx++)
                {
                    int x0 = cx * Chunk, y0 = cy * Chunk;   // Draw가 설비를 고르는 셈과 같다 (솟은 설비는 위 덩어리에도 걸친다)
                    if (bx + bw < x0 || by + bh < y0 || bx > x0 + Chunk || by > y0 + Chunk + 1) continue;
                    lists[cy * _cw + cx].Add(i);
                }
        }
        _chunks = new FarChunk[lists.Length];
        for (int k = 0; k < lists.Length; k++) _chunks[k] = new FarChunk { Furn = lists[k].ToArray() };
    }

    /// <summary>설비가 꺼졌다 (장치가 있고 · 고장도 아니고 · 일하지 않는다 — 몸을 어둡게 굽는다).</summary>
    private bool FixOff(Network? net, int i)
    {
        int dev = DeviceOf(net, i);
        return dev >= 0 && !(net!.IsBroken(dev, Part.Electric) || net.IsBroken(dev, Part.Mechanical)) && !net.Working(dev);
    }

    /// <summary>덩어리 도장: 굽는 층이 읽는 세계 값 (둘레 한 칸 · 아래 두 줄까지 — 벽 앞면 · 솟은 설비).</summary>
    private ulong StampOf(Sim s, int ci)
    {
        var f = s.Get<Field>(); var st = s.Find<Structure>(); var mind = s.Find<Mind>(); var net = s.Find<Network>();
        int cx = ci % _cw, cy = ci / _cw;
        int x0 = Math.Max(0, cx * Chunk - 1), y0 = Math.Max(0, cy * Chunk - 1), x1 = Math.Min(L.W, cx * Chunk + Chunk + 1), y1 = Math.Min(L.H, cy * Chunk + Chunk + 2);
        bool sub = st != null && st.Sub.Length == L.N, multi = st != null && st.Bodies.Count > 1;
        int nh = f.T.sNH3l, ec = f.T.sEC;
        ulong h = 14695981039346656037UL;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int c = L.Index(x, y);
                var k = L.Kind[c];
                if (k == CellKind.Outside) continue;
                float hole = f.Hole[c];
                long v = hole <= 0 ? 0 : hole >= 2f ? 255 : 1 + (int)(MathF.Sqrt(hole / 0.01f) * 2);
                v |= (long)f.Severed[c] << 8;
                if (sub) v |= (long)st!.Sub[c] << 16 | (long)(int)(st.Corrode[c] * 40) << 32 | (long)(int)(st.Fatigue[c] * 84) << 40;   // 굽기가 쓰는 단과 같다 (WallTraces)
                v |= (long)SootIdx(f.SootGPerM2(c)) << 48;
                if (k is CellKind.Floor or CellKind.Door)
                {
                    if (nh >= 0) { double m = f.Stored(nh, c); v ^= (long)(m < 0.5 ? 0 : 1 + (int)(m / 40 * 9)) << 54; }
                    if (ec >= 0) { double m = f.Stored(ec, c); v ^= (long)(m < 0.5 ? 0 : 1 + (int)(m / 20 * 9)) << 59; }
                }
                h = (h ^ (ulong)c) * 1099511628211UL;
                h = (h ^ (ulong)v) * 1099511628211UL;
                if (multi) h = (h ^ (ulong)(st!.BodyOf(c) + 2)) * 1099511628211UL;   // 떨어져 나간 조각의 칸은 배 몸 덩어리에서 빠진다
            }
        foreach (int i in _chunks![ci].Furn)
        {
            h = (h ^ (ulong)FillOf(i, mind).Key) * 1099511628211UL;
            h = (h ^ (FixOff(net, i) ? 1UL : 2UL)) * 1099511628211UL;
        }
        return h;
    }

    /// <summary>덩어리 하나를 32도트로 굽고 (Draw의 굽기 층만) 16도트로 줄인다.</summary>
    private void Bake(Sim s, int ci)
    {
        int cx = ci % _cw, cy = ci / _cw;
        _bakeInto ??= new Img(Chunk * T, Chunk * T);
        bool lit = Lighting;
        var st = s.Find<Structure>();
        bool multi = st != null && st.Bodies.Count > 1;
        int only = _only; var stc = _stc;
        _bake = true; Lighting = true; _only = multi ? 0 : -1; _stc = multi ? st : null;   // 빛나는 것은 나중 목록으로 (굽지 않는다 — 장마다 그린다) · 조각이 있으면 배 몸만
        try { Draw(s, cx * Chunk, cy * Chunk, Chunk, Chunk, 0, _bakeInto, false); }
        finally { _bake = false; Lighting = lit; _only = only; _stc = stc; }
        _emissive.Clear();
        foreach (var (fimg, fx, fy, row) in _front)
        {
            int ax = fx + cx * Chunk * T, ay = fy + cy * Chunk * T;
            _farFront[((long)ax << 32) ^ (uint)ay] = (fimg, ax, ay, row);
        }
        _front.Clear();
        var ch = _chunks![ci];
        ch.D16 = Down(_bakeInto, 2); ch.D8 = Down(ch.D16, 2); ch.Has = true;   // 8도트도 구울 때 (처음 8도트로 볼 때 덩어리마다 몰리지 않게)
        ch.Stamp = StampOf(s, ci);
        Baked++;
    }

    private static Img ChunkAt(FarChunk ch, int D) => D == 16 ? ch.D16! : ch.D8!;

    /// <summary>미리 굽기: 관전 화면이 32도트로 그리는 동안 장마다 아직 안 구운 덩어리를 PrebakeMs까지 (넓게 볼 때 첫 장에 굽기가 몰리지 않게 — 한누리호 약 390덩어리도 몇 초 안에).</summary>
    public static double PrebakeMs = 1.5;
    public void Prebake(Sim s)
    {
        EnsureChunks();
        _tick = s.Tick;
        long b0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int n = 0; ; n++)
        {
            while (_prebake < _chunks!.Length && _chunks[_prebake].Has) _prebake++;
            if (_prebake >= _chunks.Length) return;
            if (n > 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - b0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency > PrebakeMs) return;
            Bake(s, _prebake++);
        }
    }

    /// <summary>넓은 보기 한 장: 칸 (x0, y0)부터 w × h 칸을 칸당 D (16 · 8)도트로. into: 다시 쓸 그림판.</summary>
    public Img FrameFar(Sim s, int x0, int y0, int w, int h, int frame, int D, Img? into = null)
    {
        if (D is not (16 or 8)) throw new ArgumentException("넓은 보기는 칸당 16 · 8도트", nameof(D));
        int k = T / D;
        var st = s.Find<Structure>();
        bool multi = st != null && st.Bodies.Count > 1;   // 떨어져 나간 조각: 배 몸은 넓은 보기 길로 · 조각만 32도트로 그려 줄여 얹는다 (맨 끝)
        _tick = s.Tick; _frames++;
        (_skyTh, _skyX, _skyY) = st != null && st.Bodies.Count > 0 ? (double.IsNaN(SkyTh) ? st.Bodies[0].Th : SkyTh, st.Bodies[0].Lx, st.Bodies[0].Ly) : (0, 0, 0);
        _only = multi ? 0 : -1; _stc = multi ? st : null;
        EnsureChunks();
        var img = into != null && into.W >= w * D && into.H >= h * D ? into : new Img(w * D, h * D);
        var f = s.Get<Field>();
        var net = s.Find<Network>();
        var chem = s.Find<Chemistry>();
        var light = s.Find<Light>();
        var mind = s.Find<Mind>();
        var life = s.Find<Life>();
        int Gx(int x) => (x - x0) * T;   // 그림판 32도트 자리
        int Gy(int y) => (y - y0) * T;
        long lt = Prof != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (ProfAlloc != null) _lapAlloc = GC.GetAllocatedBytesForCurrentThread();

        // ── 덩어리: 처음 보는 것은 그 장에 · 도장이 바뀐 것은 장마다 FarBakeMs까지 (차례를 돌린다) ──
        int cx0 = Math.Max(0, FloorDiv(x0, Chunk)), cy0 = Math.Max(0, FloorDiv(y0, Chunk));
        int cx1 = Math.Min(_cw - 1, FloorDiv(x0 + w - 1, Chunk)), cy1 = Math.Min(_chn - 1, FloorDiv(y0 + h - 1, Chunk));
        _stale.Clear();
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int ci = cy * _cw + cx;
                if (!_chunks![ci].Has) Bake(s, ci);
                else if (StampOf(s, ci) != _chunks[ci].Stamp) _stale.Add(ci);
            }
        if (_stale.Count > 0)
        {
            long b0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int start = _farTurn++ % _stale.Count;
            for (int n = 0; n < _stale.Count; n++)
            {
                if (FarBakeMax > 0 && n >= FarBakeMax) break;
                if (FarBakeMs > 0 && n > 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - b0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency > FarBakeMs) break;
                Bake(s, _stale[(start + n) % _stale.Count]);
            }
        }
        _emissive.Clear(); _front.Clear();
        lt = Lap("넓은: 굽기 · 도장", lt);
        GatherFacts(s);
        lt = Lap("넓은: 사실 모으기", lt);
        var space = Rgba.Hex("#05070c");
        Parallel.For(0, h, r => FillOpaque(img, 0, r * D, w * D, D, space));
        lt = Lap("넓은: 우주", lt);
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
                Put(img, ChunkAt(_chunks![cy * _cw + cx], D), (cx * Chunk - x0) * D, (cy * Chunk - y0) * D);
        lt = Lap("넓은: 덩어리", lt);
        FarStars(img, x0, y0, w, h, D, space);
        lt = Lap("넓은: 별", lt);

        // ── 문짝 ──
        _stainInto ??= new Img(T, T);
        foreach (var d in L.Doors)
        {
            int x = L.X(d.Cell), y = L.Y(d.Cell);
            if (x < x0 || y < y0 || x >= x0 + w || y >= y0 + h || !Mine(d.Cell)) continue;
            var tile = DoorTile(d, f, st);
            Put(img, DownOf(tile, k), (x - x0) * D, (y - y0) * D);
            DoorStain(img, f, d.Cell, tile, x, y, x0, y0, D, frame);   // 그을음 · 쏟은 액체는 문짝 위에 (32도트와 같은 차례)
        }
        // ── 바닥에 놓인 · 날아온 물건 ──
        if (mind != null)
        {
            var count = new Dictionary<int, int>();
            foreach (var it in mind.Loose)
            {
                int c = it.Cell;
                if (c < 0 || !Mine(c)) continue;
                int x = L.X(c), y = L.Y(c);
                if (x < x0 || y < y0 || x >= x0 + w || y >= y0 + h) continue;
                int n = count.GetValueOrDefault(c); count[c] = n + 1;
                var icon = Cached(Key(9, it.Item), mind.ItemNames[it.Item], static nm => LooseIcon(nm));
                Put(img, DownOf(icon, k), FloorDiv(Gx(x) + T / 2 - icon.W / 2 + (n % 3 - 1) * 7, k), FloorDiv(Gy(y) + T / 2 - icon.H / 2 + n / 3 * 6, k));
            }
        }
        // ── 칸 덮개: 물 · 진공 · 열 · 어둠 (그을음 · 액체는 구웠다) ──
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (L.Kind[c] is not (CellKind.Floor or CellKind.Door) || !Mine(c)) continue;
                int px = (x - x0) * D, py = (y - y0) * D;
                double water = f.Stored(f.T.sWater, c);
                if (water > 2) Put(img, DownOf(Cached(Key(6, frame & 3, c & 7), (f4: frame & 3, c8: c & 7), static q => DotFx.WaterTile(q.f4, q.c8)), k), px, py, false, (float)Math.Min(0.85, water / 60));
                if (L.Kind[c] == CellKind.Door && L.DoorAt[c] >= 0 && f.DoorOpen[L.DoorAt[c]] < 0.05f) continue;
                double kpa = f.Pressure(c);
                if (kpa < 40) img.Fill(px, py, D, D, Rgba.Hex("#8fd0ff"), (float)(0.42 * (1 - kpa / 40)));
                double temp = f.Temp(c);
                if (temp > 330 && kpa > 5) img.Fill(px, py, D, D, Rgba.Hex("#ff5a1e"), (float)Math.Min(0.35, (temp - 330) / 400));
                if (light != null && !Lighting) { float lux = light.Lux[c]; float dark = lux >= 60 ? 0 : (float)Math.Min(0.72, 0.72 * (1 - lux / 60)); if (dark > 0.02f) img.Fill(px, py, D, D, Rgba.Hex("#02040a"), dark); }
            }
        lt = Lap("넓은: 문 · 물건 · 칸 덮개", lt);

        // ── 사람 · 로봇 (아래쪽이 앞): 32도트 작은 판에 그리고 (앞 가림까지) 줄여 얹는다 ──
        if (mind != null && life != null)
        {
            _agentInto ??= new Img(AgW, AgH);
            var sc = _agentInto;
            _farMarks.Clear();
            {
                foreach (var a in mind.Agents.OrderBy(a => L.Y(a.C.Cell)).ThenBy(a => a.Id))
                {
                    int cell = a.C.Cell, x = L.X(cell), y = L.Y(cell);
                    if (x < x0 - 1 || y < y0 - 1 || x > x0 + w || y > y0 + h + 1 || !Mine(cell)) continue;
                    if (a.IsPerson && a.C.CarriedBy >= 0) continue;
                    var (nx, ny) = Nudge?.Invoke(a) ?? (0, 0);
                    int gx = Gx(x) + nx, gy = Gy(y) + ny;
                    int ox = AgOx + Mod(gx - AgOx, k), oy = AgOy + Mod(gy - AgOy, k);   // 판 (0, 0)이 줄인 도트 칸에 맞게
                    Array.Clear(sc.Px);
                    int e0 = _emissive.Count;
                    if (a.IsPerson) DrawPerson(sc, mind, life, a, ox, oy, frame);
                    else DrawBot(sc, a, ox, oy, frame);
                    if (_emissive.Count > e0)   // 공황 표시처럼 빛을 받지 않는 것 (작은 판 자리) — 빛을 곱한 뒤 그 자리에 다시 그린다
                    {
                        _farMarks.Add((_emissive.GetRange(e0, _emissive.Count - e0), (gx - ox) / k, (gy - oy) / k));
                        _emissive.RemoveRange(e0, _emissive.Count - e0);
                    }
                    int ax = gx - ox + x0 * T, ay = gy - oy + y0 * T;   // 판 (0, 0)의 도면 32도트 자리
                    int sx0 = ox, sy0 = oy + T - 46, sx1 = ox + T, sy1 = oy + T;
                    foreach (var (fimg, fx, fy, row) in _farFront.Values)
                    {
                        int qx = fx - ax, qy = fy - ay;
                        if (row <= y || qx >= sx1 || qy >= sy1 || qx + fimg.W <= sx0 || qy + fimg.H <= sy0) continue;
                        PutClip(sc, fimg, qx, qy, sx0, sy0, sx1, sy1);
                    }
                    PutDown(img, sc, k, (gx - ox) / k, (gy - oy) / k);
                }
            }
        }
        lt = Lap("넓은: 사람 · 로봇", lt);

        // ── 떠 있는 물방울 (무중력) ──
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (!Mine(c)) continue;
                double dr = f.Floating(f.T.sWater, c);
                if (dr < 3) continue;
                int n = (int)Math.Min(5, 1 + dr / 25);
                for (int q = 0; q < n; q++)
                {
                    uint hsh = (uint)(c * 73856093) ^ (uint)(q * 19349663);
                    int r = dr > 60 && q % 2 == 0 ? 7 : dr > 20 ? 5 : 4, rr = Math.Max(1, r / k);
                    int px = Gx(x) + 1 + (int)(hsh % (uint)(T - r - 2)), py = Gy(y) + 1 + (int)(hsh / 7 % (uint)(T - r - 2));
                    img.Fill(px / k, py / k, rr, rr, Rgba.Hex("#3e8fd0"), 0.85f);
                    img.Fill(px / k, py / k, 1, 1, Rgba.Hex("#a8dcf8"), 0.9f);
                }
            }
        // ── 연기: 뭉게 연기 · 천장 아래 엷은 막 ──
        for (int y = y0; y < y0 + h + 1; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (!Mine(c)) continue;
                double soot = PuffPpm(f, c);
                if (soot <= 20) continue;
                float al = (float)Math.Min(0.8, soot / 500);
                var puff = Cached(Key(7, c & 15), c & 15, static q => DotFx.SmokeSprite(T + 12, q));
                int dx = (int)((DotHash.F(c, frame / 4) - 0.5f) * 6);
                Put(img, DownOf(puff, k), FloorDiv(Gx(x) - 6 + dx, k), FloorDiv(Gy(y) - 10, k), false, al);
            }
        if (f.Strat.Length == L.N)
        {
            var veil = Rgba.Hex("#4a4744");
            int bs = Math.Max(1, 4 / k), drift = frame / 3;
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    if (!L.In(x, y)) continue;
                    int c = L.Index(x, y);
                    if (L.Kind[c] is not (CellKind.Floor or CellKind.Door) || f.Strat[c] < 0.1f || !Mine(c)) continue;
                    double extra = f.HeadPpm(f.T.gSoot, c, false) - f.HeadPpm(f.T.gSoot, c, true);
                    if (extra < 20) continue;
                    float a = (float)Math.Min(0.55, extra / 900);
                    for (int by = 0; by < T; by += 4)
                        for (int bx = 0; bx < T; bx += 4)
                        {
                            float kk = 0.75f + 0.5f * DotHash.F(c * 31 + (bx + drift * 4) % T, by, 7);
                            img.Fill((Gx(x) + bx) / k, (Gy(y) + by) / k, bs, bs, veil, Math.Min(0.8f, a * kk));
                        }
                }
        }
        lt = Lap("넓은: 물방울 · 연기", lt);

        // ── 빛: 곱하고 · 설비 빛 · 불꽃 (맨 앞) ──
        if (Lighting)
        {
            BuildLight(s, f, net, light, chem, st, x0, y0, w, h, frame);
            WallLight();
            lt = Lap("넓은: 빛 지도", lt);
            ApplyLightFar(img, w, h, D);
            lt = Lap("넓은: 빛 곱하기", lt);
            FarGlows(img, net, x0, y0, w, h, frame, k);
            foreach (var (marks, mx, my) in _farMarks)
            {
                Array.Clear(_agentInto!.Px);
                foreach (var e in marks) e(_agentInto);
                PutDown(img, _agentInto, k, mx, my);
            }
            lt = Lap("넓은: 설비 빛", lt);
        }
        if (chem != null)
            for (int y = y0; y < y0 + h + 1; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    if (!L.In(x, y)) continue;
                    int c = L.Index(x, y);
                    if (chem.Burning[c] == 0 || !Mine(c)) continue;
                    float power = Math.Clamp(chem.Hrr[c] / 40f, 0.25f, 1f);
                    if (Lighting) img.Glow((Gx(x) + T / 2f) / k, (Gy(y) + T / 2f) / k, (10 + power * 10) / k, Ramps.Get(Ramps.Fire, 5), 0.10f + 0.12f * power);
                    int fy = Gy(y) + T - 40, fo = Mod(fy, k);
                    var tile = DotFx.FireTile(frame + c, power, c);
                    if (fo == 0) PutDown(img, tile, k, Gx(x) / k, fy / k);
                    else Put(img, Down(tile, k), Gx(x) / k, FloorDiv(fy, k));
                }
        lt = Lap("넓은: 불", lt);
        if (Show != Overlay.None) DrawOverlay(img, f, net, x0, y0, w, h, D);
        lt = Lap("넓은: 겹쳐 보기", lt);
        // ── 떨어져 나간 조각: 제 칸만 32도트로 그려 자세대로 돌려 붙인다 (Frame과 같다 — 붙이는 도트만 D) · 부스러기 알갱이 ──
        if (multi)
            for (int b = 1; b < st!.Bodies.Count; b++)
            {
                var o = st.Bodies[b];
                if (o.Mass <= 0) continue;
                int bx0 = o.MinX - 1, by0 = o.MinY - 2, bw = o.MaxX - o.MinX + 3, bh = o.MaxY - o.MinY + 4;
                var (cs, sn, tx, ty) = Poses != null && b < Poses.Length ? Poses[b] : st.ToShipFrame(b);
                double qx0 = double.MaxValue, qy0 = double.MaxValue, qx1 = double.MinValue, qy1 = double.MinValue;
                foreach (var (px, py) in new[] { (bx0, by0), (bx0 + bw, by0), (bx0, by0 + bh), (bx0 + bw, by0 + bh) })
                {
                    double qx = cs * px - sn * py + tx, qy = sn * px + cs * py + ty;
                    qx0 = Math.Min(qx0, qx); qy0 = Math.Min(qy0, qy); qx1 = Math.Max(qx1, qx); qy1 = Math.Max(qy1, qy);
                }
                if (qx1 < x0 || qy1 < y0 || qx0 > x0 + w || qy0 > y0 + h) continue;   // 화면 밖
                _only = b;
                var part = Draw(s, bx0, by0, bw, bh, frame, null, true);
                Place(img, part, bx0, by0, x0, y0, cs, sn, tx, ty, qx0, qy0, qx1, qy1, D);
            }
        _only = -1; _stc = null;
        if (st != null) Shards(img, st, x0, y0, D);
        Lap("넓은: 조각", lt);
        return img;
    }

    private Img? _stainInto;
    private readonly List<(List<Action<Img>> marks, int x, int y)> _farMarks = new();

    /// <summary>문 칸의 그을음 · 쏟은 액체를 문짝 위에 (굽는 덩어리는 문 칸 얼룩을 빼고 굽는다) · 가로 벽 문짝이 아래 칸에 드리운 줄 위에도 그 칸의 얼룩을 (문짝의 불투명한 도트만 — 나머지는 구웠다).</summary>
    private void DoorStain(Img img, Field f, int c, Img tile, int x, int y, int x0, int y0, int D, int frame)
    {
        int k = T / D;
        bool Stained(int cell) => f.SootGPerM2(cell) > 0.05 || L.FurnitureAt[cell] < 0 && (f.T.sNH3l >= 0 && f.Stored(f.T.sNH3l, cell) >= 0.5 || f.T.sEC >= 0 && f.Stored(f.T.sEC, cell) >= 0.5);
        var sc = _stainInto!;
        if (Stained(c))
        {
            Array.Clear(sc.Px);
            double sg = f.SootGPerM2(c);
            if (sg > 0.05) SootStain(sc, 0, 0, T, T, SootStep(sg), c);
            FloorLiquids(sc, f, c, 0, 0, frame);
            PutDown(img, sc, k, (x - x0) * D, (y - y0) * D, T, T);
        }
        int hang = tile.H - T;   // 가로 벽 문짝이 아래 바닥 칸에 드리운 줄 (ArtWalls.FaceLow)
        if (hang <= 0 || !L.In(x, y + 1)) return;
        int below = L.Index(x, y + 1);
        if (L.Kind[below] != CellKind.Floor || !Stained(below) || y + 1 >= y0 + (img.H / D)) return;
        Array.Clear(sc.Px);
        double sb = f.SootGPerM2(below);
        if (sb > 0.05) SootStain(sc, 0, 0, T, T, SootStep(sb), below);
        FloorLiquids(sc, f, below, 0, 0, frame);
        var d = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(sc.Px.AsSpan());
        for (int yy = 0; yy < T; yy++)
            for (int xx = 0; xx < T; xx++)
                if (yy >= hang || tile.Get(xx, T + yy).A == 0) d[yy * sc.W + xx] = 0;   // 드리운 문짝 도트 위만 (그 밖은 구운 얼룩)
        PutDown(img, sc, k, (x - x0) * D, (y + 1 - y0) * D, T, hang + (k - hang % k) % k);
    }

    /// <summary>src의 (0, 0)부터 w × h를 k×k 알파 가중 평균으로 줄여 dst의 (dx, dy)에 바로 얹는다 (줄인 그림을 새로 잡지 않는다 · 투명한 덩이는 건너뛴다). src (0, 0)은 줄인 도트 칸에 맞아야 한다.</summary>
    private static void PutDown(Img dst, Img src, int k, int dx, int dy, int w = -1, int h = -1)
    {
        if (w < 0) w = src.W; if (h < 0) h = src.H;
        int ow = (Math.Min(w, src.W) + k - 1) / k, oh = (Math.Min(h, src.H) + k - 1) / k;
        var s = src.Px;
        var d = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(dst.Px.AsSpan());
        for (int y = 0; y < oh; y++)
        {
            int ty = dy + y;
            if ((uint)ty >= (uint)dst.H) continue;
            int yb = Math.Min(Math.Min(h, src.H), y * k + k);
            for (int x = 0; x < ow; x++)
            {
                int tx = dx + x;
                if ((uint)tx >= (uint)dst.W) continue;
                int a = 0, r = 0, g = 0, b = 0, n = 0, xb = Math.Min(Math.Min(w, src.W), x * k + k);
                for (int yy = y * k; yy < yb; yy++)
                    for (int xx = x * k; xx < xb; xx++)
                    {
                        int i = (yy * src.W + xx) * 4, pa = s[i + 3];
                        n++;
                        if (pa == 0) continue;
                        a += pa; r += s[i] * pa; g += s[i + 1] * pa; b += s[i + 2] * pa;
                    }
                if (a == 0) continue;
                int al = a / n;
                if (al >= 255) d[ty * dst.W + tx] = (uint)(r / a) | (uint)(g / a) << 8 | (uint)(b / a) << 16 | 0xFF000000u;
                else dst.Blend(tx, ty, new Rgba((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)al));
            }
        }
    }

    /// <summary>문짝 그림 (열림 6단 · 종류 · 상태 불) — 32도트 · 넓은 보기가 함께 쓴다.</summary>
    private Img DoorTile(DoorDef d, Field f, Structure? st)
    {
        float open = f.DoorOpen[d.Id];
        int step = Math.Clamp((int)MathF.Round(open * 6f), 0, 6);
        int kind = d.External ? 1 : d.Kind == "normal" ? 0 : 2;
        bool jam = st != null && st.DoorJam[d.Id] != 0;
        int lightKind = st != null && st.DoorPowered[d.Id] == 0 ? 2 : jam ? 3 : open > 0.05f ? 1 : 0;
        return Cached(Key(4, d.Vertical ? 1 : 0, step, kind, lightKind), (d.Vertical, step, kind, lightKind), static q => ArtWalls.Door(q.Vertical, q.step, q.kind, q.lightKind, 0));
    }

    /// <summary>별 (장마다 · 우주 빛깔 그대로인 도트에만 — 배가 돌면 별도 돈다).</summary>
    private void FarStars(Img img, int x0, int y0, int w, int h, int D, Rgba space)
    {
        int k = T / D, W = w * D, H = h * D;
        uint sp = (uint)space.R | (uint)space.G << 8 | (uint)space.B << 16 | 0xFF000000u;
        var d = MemoryMarshal.Cast<byte, uint>(img.Px.AsSpan());
        if (_skyTh == 0)
        {
            for (int y = y0 - 1; y <= y0 + h; y++)
                for (int x = x0 - 1; x <= x0 + w; x++)
                {
                    uint u = DotHash.U(x, y, 7);
                    if (u % 9 != 0) continue;
                    int sx = FloorDiv((x - x0) * T + (int)(u >> 8) % T, k), sy = FloorDiv((y - y0) * T + (int)(u >> 16) % T, k);
                    if ((uint)sx >= (uint)W || (uint)sy >= (uint)H) continue;
                    ref uint px = ref d[sy * img.W + sx];
                    if (px != sp) continue;
                    uint v = 90 + (u >> 24) % 140;
                    px = v | v << 8 | Math.Min(255u, v + 20) << 16 | 0xFF000000u;
                }
            return;
        }
        double cs = Math.Cos(_skyTh), sn = Math.Sin(_skyTh);
        double qx0 = double.MaxValue, qy0 = double.MaxValue, qx1 = double.MinValue, qy1 = double.MinValue;
        foreach (var (px, py) in new[] { (x0 - 1.0, y0 - 1.0), (x0 + w + 1.0, y0 - 1.0), (x0 - 1.0, y0 + h + 1.0), (x0 + w + 1.0, y0 + h + 1.0) })
        {
            double qx = cs * (px - _skyX) - sn * (py - _skyY) + _skyX, qy = sn * (px - _skyX) + cs * (py - _skyY) + _skyY;
            qx0 = Math.Min(qx0, qx); qy0 = Math.Min(qy0, qy); qx1 = Math.Max(qx1, qx); qy1 = Math.Max(qy1, qy);
        }
        for (int y = (int)Math.Floor(qy0); y <= (int)Math.Ceiling(qy1); y++)
            for (int x = (int)Math.Floor(qx0); x <= (int)Math.Ceiling(qx1); x++)
            {
                uint u = DotHash.U(x, y, 7);
                if (u % 9 != 0) continue;
                double kx = x + (double)((u >> 8) % T) / T - _skyX, ky = y + (double)((u >> 16) % T) / T - _skyY;
                double bx = cs * kx + sn * ky + _skyX, by = -sn * kx + cs * ky + _skyY;
                int sx = (int)Math.Floor((bx - x0) * D), sy = (int)Math.Floor((by - y0) * D);
                if ((uint)sx >= (uint)W || (uint)sy >= (uint)H) continue;
                ref uint px = ref d[sy * img.W + sx];
                if (px != sp) continue;
                uint v = 90 + (u >> 24) % 140;
                px = v | v << 8 | Math.Min(255u, v + 20) << 16 | 0xFF000000u;
            }
    }

    /// <summary>빛 지도를 넓은 보기 그림에 곱한다 (칸 모서리 빛을 D도트로 잇는다 — 32도트의 웅덩이 · 벽 밑 그림자는 없다 · 벽 앞면은 마주한 남쪽 방의 빛).</summary>
    private void ApplyLightFar(Img img, int w, int h, int D)
    {
        int gw = w + 1;
        Corners(w, h);
        var cr = _cr; var cg = _cg; var cb = _cb;
        float ar = Ambient * AmbientTint.r, ag = Ambient * AmbientTint.g, ab = Ambient * AmbientTint.b, keep = 1 - Ambient;
        int W = img.W, cap = ArtWalls.CapH * D / T;
        Parallel.For(0, h, vy =>
        {
            var d = MemoryMarshal.Cast<byte, uint>(img.Px.AsSpan());
            for (int vx = 0; vx < w; vx++)
            {
                int mi = (vy + 1) * _mw + vx + 1;
                byte kind = _lk[mi];
                if (kind == 0) continue;
                int px0 = vx * D, py0 = vy * D;
                if (kind is 1 or 3)
                {
                    int g00 = vy * gw + vx, g10 = g00 + 1, g01 = g00 + gw, g11 = g01 + 1;
                    for (int yy = 0; yy < D; yy++)
                    {
                        float ty = (yy + 0.5f) / D;
                        float r0 = ar + keep * (cr[g00] + (cr[g01] - cr[g00]) * ty), g0 = ag + keep * (cg[g00] + (cg[g01] - cg[g00]) * ty), b0 = ab + keep * (cb[g00] + (cb[g01] - cb[g00]) * ty);
                        float r1 = ar + keep * (cr[g10] + (cr[g11] - cr[g10]) * ty), g1 = ag + keep * (cg[g10] + (cg[g11] - cg[g10]) * ty), b1 = ab + keep * (cb[g10] + (cb[g11] - cb[g10]) * ty);
                        Row(d, (py0 + yy) * W + px0, r0, g0, b0, r1, g1, b1, 0, false, false, 0, D);
                    }
                    continue;
                }
                int iN = mi - _mw, iS = mi + _mw, iW = mi - 1, iE = mi + 1;
                bool n = Space(iN), s = Space(iS), wv = Space(iW), e = Space(iE);
                int a, b; bool vertical;
                if (n || s) { a = n ? iN : iS; b = s ? iS : iN; vertical = true; }
                else if (wv || e) { a = wv ? iW : iE; b = e ? iE : iW; vertical = false; }
                else { a = mi; b = mi; vertical = true; }
                float ra = ar + keep * _lr[a], ga = ag + keep * _lg[a], ba = ab + keep * _lb[a];
                float rb = ar + keep * _lr[b], gb = ag + keep * _lg[b], bb = ab + keep * _lb[b];
                bool face = _lk[iS] == 1;
                int gS0 = (vy + 1) * gw + vx, gS1 = gS0 + 1;
                for (int yy = 0; yy < D; yy++)
                {
                    if (face && yy >= cap)
                    {
                        Row(d, (py0 + yy) * W + px0, ar + keep * cr[gS0], ag + keep * cg[gS0], ab + keep * cb[gS0], ar + keep * cr[gS1], ag + keep * cg[gS1], ab + keep * cb[gS1], 0, false, false, 0, D);
                        continue;
                    }
                    if (vertical)
                    {
                        float t = (yy + 0.5f) / D;
                        float r = ra + (rb - ra) * t, g = ga + (gb - ga) * t, bl = ba + (bb - ba) * t;
                        Row(d, (py0 + yy) * W + px0, r, g, bl, r, g, bl, 0, false, false, 0, D);
                    }
                    else Row(d, (py0 + yy) * W + px0, ra, ga, ba, rb, gb, bb, 0, false, false, 0, D);
                }
            }
        });
    }

    /// <summary>켜진 설비의 화면 · 불빛 (곱한 뒤 · 32도트와 같은 그림을 줄여서).</summary>
    private void FarGlows(Img img, Network? net, int x0, int y0, int w, int h, int frame, int k)
    {
        _fixOrder ??= Enumerable.Range(0, L.Furniture.Count).Where(i => L.Furniture[i].Cells.Length > 0)
            .OrderBy(i => _fixBox[i].y + _fixBox[i].h).ThenBy(i => i).ToArray();
        foreach (int i in _fixOrder)
        {
            var (bx, by, bw, bh) = _fixBox[i];
            if (bx + bw < x0 || by + bh < y0 || bx > x0 + w || by > y0 + h + 1) continue;
            int gx = (bx - x0) * T, gy = (by - y0) * T;
            string type = L.Furniture[i].Type;
            if (type == "GravityGenerator")
            {
                int gdev = DeviceOf(net, i);
                var gs = gdev < 0 ? FixState.On : net!.IsBroken(gdev, Part.Electric) || net.IsBroken(gdev, Part.Mechanical) ? FixState.Broken : net.Working(gdev) ? FixState.On : FixState.Off;
                float field = gdev >= 0 && net!.GravField.Length > gdev ? net.GravField[gdev] : 1f;
                int top = gy - GravRise, oy = Mod(top, k);
                var g = new Img(bw * T, bh * T + GravRise + k);
                GravGlow(g, 0, oy, bw, bh, field, gs, frame);
                Put(img, Down(g, k), gx / k, (top - oy) / k);
                continue;
            }
            if (_newArt[i])
            {
                var face = _facing[i];
                int nrise = ArtFurniture.Rise(type, bw, bh, face);
                int stIdx = NewGlowState(net, i);
                if (stIdx == 0) continue;
                var g = NewGlow(type, bw, bh, face, stIdx, frame);
                if (g.W > 1) Put(img, DownOf(g, k), gx / k, FloorDiv(gy - nrise, k));
                continue;
            }
            if (_washKind[i] != 0) continue;
            var ft = _fixType[i] ?? FurnitureType.Shelf;
            int rise = DotMachines.RiseOf(ft);
            int dev = DeviceOf(net, i);
            var state = dev < 0 ? FixState.On : net!.IsBroken(dev, Part.Electric) || net.IsBroken(dev, Part.Mechanical) ? FixState.Broken : net.Working(dev) ? FixState.On : FixState.Off;
            if (state == FixState.Off) continue;
            Put(img, DownOf(OldGlow(ft, bw, bh, state, frame), k), gx / k, FloorDiv(gy - rise, k));
        }
    }

    /// <summary>새 가구의 빛 상태 (0 꺼짐 · 1 켜짐 · 2 고장).</summary>
    private int NewGlowState(Network? net, int i)
    {
        int ndev = DeviceOf(net, i);
        var nst = ndev < 0 ? FixState.On : net!.IsBroken(ndev, Part.Electric) || net.IsBroken(ndev, Part.Mechanical) ? FixState.Broken : net.Working(ndev) ? FixState.On : FixState.Off;
        return nst == FixState.Off ? 0 : nst == FixState.Broken ? 2 : 1;
    }

    private Img NewGlow(string type, int bw, int bh, Facing face, int stIdx, int frame)
    {
        long gkey = (13L << 56) ^ ((long)(type.GetHashCode() & 0xFFFF) << 32) ^ ((long)bw << 24) ^ ((long)bh << 20) ^ ((long)face << 16) ^ ((long)stIdx << 12) ^ (frame & 15);
        return Cached(gkey, (type, bw, bh, face, stIdx, f16: frame & 15), static q => ArtFurniture.Glow(q.type, q.bw, q.bh, q.face, q.stIdx, q.f16) ?? new Img(1, 1));
    }

    private Img OldGlow(FurnitureType ft, int bw, int bh, FixState state, int frame) =>
        Cached(Key(10, (int)ft, bw * 64 + bh, (int)state, frame & 7), (ft, bw, bh, state, f8: frame & 7), static q => DotMachines.Life(q.ft, q.bw, q.bh, q.state, q.f8, 1f));
}
