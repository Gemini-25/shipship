using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using ShipEngine;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipView;

/// <summary>
/// 화면의 빛 (그래픽 결정 3 · 4 · 7 · 8) — 월드의 빛을 읽어 도트마다 색 빛을 곱한다 (세계는 읽기만 한다):
/// 방 조명(⑥ 망이 켜고 · 주컴퓨터가 밤에 낮춘 몫 — ③ 빛의 방 조명) — 방 종류마다 빛깔 (생활 방은 따뜻하게 · 함교는 푸르게 · 재배실은 생장등 · 기관실은 호박빛) ·
/// 비상등만 남은 방은 붉게 · 불꽃(② 화학의 열 방출 — 구역 안에서 거리로 줄고 · 벽에 막힌다 · 일렁인다) · 켜진 설비의 화면 · 불빛 (망가지면 붉게 깜빡인다) ·
/// 연기(그을음)가 조명을 가린다 (③ 빛과 같은 식) · 벽 밑 그림자.
/// 화면 · 불빛 · 불꽃 · 벽 등은 스스로 빛나므로 곱한 뒤에 그린다.
/// </summary>
public sealed partial class Painter
{
    /// <summary>화면 조명 (끄면 예전처럼 어두운 칸만 덮는다 — 시안 비교 · 느린 기계).</summary>
    public bool Lighting = true;

    public const float Ambient = 0.13f;          // 빛이 하나도 없을 때 (별빛 · 눈이 익은 정도)
    public const float EmergencyLevel = 0.45f;    // 비상등만 남은 방의 밝기
    public const float MaxLight = 1.9f;          // 불 곁은 그림보다 밝아진다
    public const int FireReach = 6;              // 불빛이 닿는 칸 (구역 안)
    private static readonly (float r, float g, float b) AmbientTint = (0.78f, 0.88f, 1.18f);
    private static readonly (float r, float g, float b) EmergencyTint = (1.00f, 0.16f, 0.12f);
    private static readonly (float r, float g, float b) FireTint = (1.00f, 0.56f, 0.22f);
    private const FixKit Glowing = FixKit.Screen | FixKit.Leds | FixKit.Burners | FixKit.Coil | FixKit.Window | FixKit.Glow | FixKit.Lamp | FixKit.Cells;

    /// <summary>방 종류 → 조명 빛깔 (따뜻하고 아늑한 생활감 — 일하는 방은 희고 · 함교는 푸르다).</summary>
    public static (float r, float g, float b) TintFor(string type) => type switch
    {
        "Quarters" or "Lounge" or "Chapel" or "School" or "Observatory" or "Mess" or "Galley" => (1.07f, 0.94f, 0.78f),
        "Hydroponics" => (1.03f, 0.96f, 1.04f),
        "Medbay" or "Lab" => (0.97f, 1.00f, 1.05f),
        "Bridge" or "Comms" or "ComputerRoom" => (0.86f, 0.96f, 1.10f),
        "Engine" or "Reactor" or "Cooling" or "Power" or "Foundry" or "Workshop" or "LifeSupport" => (1.04f, 0.95f, 0.82f),
        _ => (0.97f, 0.98f, 1.00f),
    };

    private (float r, float g, float b)[]? _tint;
    // 빛 지도: 그리는 칸 + 둘레 한 칸 (kind 0 바깥 · 1 바닥 · 2 벽 · 3 문)
    private float[] _lr = Array.Empty<float>(), _lg = Array.Empty<float>(), _lb = Array.Empty<float>();
    private float[] _rr = Array.Empty<float>(), _rg = Array.Empty<float>(), _rb = Array.Empty<float>();   // 그 가운데 방 조명 몫 (웅덩이는 이 몫만 — 불 · 장치 빛은 그대로)
    private float[] _crr = Array.Empty<float>(), _crg = Array.Empty<float>(), _crb = Array.Empty<float>();
    private byte[] _lk = Array.Empty<byte>();
    private int _mx0, _my0, _mw, _mh;
    private float[] _cr = Array.Empty<float>(), _cg = Array.Empty<float>(), _cb = Array.Empty<float>();
    private int[] _fireSeen = Array.Empty<int>();
    private int _fireStamp;
    private readonly Queue<(int c, int d)> _fireQ = new();
    /// <summary>곱한 뒤에 그리는 스스로 빛나는 것 (설비의 화면 · 불빛 · 벽 등 · 공황 표시).</summary>
    private readonly List<Action<Img>> _emissive = new();

    private static (float, float, float) Scale((float r, float g, float b) t, float k) => (t.r * k, t.g * k, t.b * k);

    /// <summary>방 하나의 조명 (연기 가림 shade 0~1을 곱하기 전).</summary>
    private (float r, float g, float b) RoomLamp(int room, Network? net, Light? light)
    {
        if (room < 0) return (0, 0, 0);
        _tint ??= L.Rooms.Select(r => TintFor(r.Type)).ToArray();
        float lamp = light != null && room < light.Lamp.Length ? light.Lamp[room] : 300;
        byte state = net?.LampState(room) ?? (byte)1;
        if (state == 2) return Scale(EmergencyTint, EmergencyLevel);
        if (lamp <= 0) return (0, 0, 0);
        float m = MathF.Pow(Math.Min(1.3f, lamp / 300f), 0.7f);   // 화면 곡선 f (텍스처 T5 — 예전 √: 밤 통로 30%가 낮의 0.55 → 0.43 · 세계는 그대로)
        return Scale(_tint![room], m);
    }

    private void BuildLight(Sim s, Field f, Network? net, Light? light, Chemistry? chem, Structure? st, int x0, int y0, int w, int h, int frame)
    {
        _mx0 = x0 - 1; _my0 = y0 - 1; _mw = w + 2; _mh = h + 2;
        int n = _mw * _mh;
        if (_lr.Length < n) { _lr = new float[n]; _lg = new float[n]; _lb = new float[n]; _lk = new byte[n]; _rr = new float[n]; _rg = new float[n]; _rb = new float[n]; }
        PoolK(net, light);
        Span<int> nb = stackalloc int[4];
        // 방 조명 (연기가 가린다 — ③ 빛과 같은 식) · 장치 · 사람이 더한 빛
        for (int my = 0; my < _mh; my++)
            for (int mx = 0; mx < _mw; mx++)
            {
                int i = my * _mw + mx, x = _mx0 + mx, y = _my0 + my;
                _lr[i] = _lg[i] = _lb[i] = 0; _lk[i] = 0; _rr[i] = _rg[i] = _rb[i] = 0;
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                var k = L.Kind[c];
                if (k == CellKind.Wall) { if (!(f.Hole[c] >= 2f && (st == null || st.Sub[c] == 0))) _lk[i] = 2; continue; }
                if (k is not (CellKind.Floor or CellKind.Door)) continue;
                _lk[i] = (byte)(k == CellKind.Door ? 3 : 1);
                (float r, float g, float b) v;
                if (L.Room[c] >= 0) v = RoomLamp(L.Room[c], net, light);
                else
                {
                    // 문: 양쪽 방 빛의 고른 값
                    L.Neighbors(c, nb);
                    float r = 0, g = 0, b = 0; int cnt = 0;
                    foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0) { var q = RoomLamp(L.Room[j], net, light); r += q.r; g += q.g; b += q.b; cnt++; }
                    v = cnt > 0 ? (r / cnt, g / cnt, b / cnt) : (0, 0, 0);
                }
                float shade = light != null ? 0.3f + 0.7f * (float)Math.Min(1, light.Visibility(c) / 10) : 1;
                float extra = light != null ? MathF.Sqrt(Math.Min(1f, light.Extra[c] / 300f)) * 0.6f : 0;
                _lr[i] = v.r * shade + extra; _lg[i] = v.g * shade + extra; _lb[i] = v.b * shade + extra;
                if (L.Room[c] >= 0) { _rr[i] = v.r * shade; _rg[i] = v.g * shade; _rb[i] = v.b * shade; }
            }
        // 켜진 설비의 화면 · 불빛 (제 칸과 곁 칸을 조금 · 망가지면 붉게 깜빡)
        if (net != null)
            for (int fi = 0; fi < L.Furniture.Count; fi++)
            {
                var (bx, by, bw, bh) = _fixBox[fi];
                if (L.Furniture[fi].Cells.Length == 0 || bx + bw < _mx0 - 1 || by + bh < _my0 - 1 || bx > _mx0 + _mw || by > _my0 + _mh) continue;
                int dev = DeviceOf(net, fi);
                if (dev < 0) continue;
                bool broken = net.IsBroken(dev, Part.Electric) || net.IsBroken(dev, Part.Mechanical);
                var ft = _fixType[fi] ?? FurnitureType.Shelf;
                var spec = DotMachines.Spec(ft);
                if (L.Furniture[fi].Type != "GravityGenerator" && (spec.Kit & Glowing) == 0) continue;
                (float r, float g, float b) col; float amt;
                if (broken) { if (((frame >> 2) & 1) != 0) continue; col = EmergencyTint; amt = 0.22f; }
                else if (!net.Working(dev)) continue;
                else if ((spec.Kit & (FixKit.Burners | FixKit.Coil)) != 0) { col = FireTint; amt = 0.2f; }
                else { var a = Ramps.Get(L.Furniture[fi].Type == "GravityGenerator" ? Ramps.Cyan : spec.Accent, 4); col = (a.R / 255f, a.G / 255f, a.B / 255f); amt = 0.14f; }
                foreach (int c in L.Furniture[fi].Cells)
                {
                    AddLight(c, col, amt);
                    L.Neighbors(c, nb);
                    foreach (int j in nb) if (j >= 0) AddLight(j, col, amt * 0.45f);
                }
            }
        // 불꽃: 구역 안에서 거리 제곱으로 (벽에 막힌다) · 그림 장마다 일렁인다
        if (chem != null)
        {
            if (_fireSeen.Length != L.N) _fireSeen = new int[L.N];
            for (int y = _my0 - FireReach; y < _my0 + _mh + FireReach; y++)
                for (int x = _mx0 - FireReach; x < _mx0 + _mw + FireReach; x++)
                {
                    if (!L.In(x, y)) continue;
                    int src = L.Index(x, y);
                    float hrr = chem.Hrr[src];
                    if (hrr < 5) continue;
                    int region = f.RegionOf.Length > src ? f.RegionOf[src] : -1;
                    float flick = 0.84f + 0.16f * DotHash.F(src, frame);
                    _fireStamp++;
                    _fireQ.Clear(); _fireQ.Enqueue((src, 0)); _fireSeen[src] = _fireStamp;
                    while (_fireQ.Count > 0)
                    {
                        var (c, d) = _fireQ.Dequeue();
                        float lux = hrr * 40 / (1 + d * d);
                        AddLight(c, FireTint, MathF.Sqrt(Math.Min(2f, lux / 300f)) * flick);
                        if (d >= FireReach || region < 0) continue;
                        L.Neighbors(c, nb);
                        foreach (int j in nb)
                        {
                            if (j < 0 || _fireSeen[j] == _fireStamp) continue;
                            _fireSeen[j] = _fireStamp;
                            if (f.RegionOf[j] == region) _fireQ.Enqueue((j, d + 1));
                            else if (L.Kind[j] == CellKind.Wall) AddLight(j, FireTint, MathF.Sqrt(Math.Min(2f, hrr * 40 / (1 + (d + 1) * (d + 1)) / 300f)) * flick);   // 벽 안쪽 면도 받는다
                        }
                    }
                }
        }
        FxLight(s, net, frame);   // 합선 아크 · 폭발 섬광
    }

    /// <summary>벽 칸의 제 빛 (빈 칸이 맞닿지 않은 모서리 벽용): 맞닿은 빈 칸 → 대각선 빈 칸 → 이웃 벽의 고른 값.</summary>
    private void WallLight()
    {
        for (int pass = 0; pass < 3; pass++)
            for (int my = 1; my < _mh - 1; my++)
                for (int mx = 1; mx < _mw - 1; mx++)
                {
                    int i = my * _mw + mx;
                    if (_lk[i] != 2 || pass > 0 && _lr[i] + _lg[i] + _lb[i] > 0) continue;
                    float r = 0, g = 0, b = 0; int cnt = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0 || pass == 0 && dx != 0 && dy != 0) continue;
                            int j = i + dy * _mw + dx;
                            bool use = pass < 2 ? Space(j) : _lk[j] == 2 && _lr[j] + _lg[j] + _lb[j] > 0;
                            if (!use) continue;
                            r += _lr[j]; g += _lg[j]; b += _lb[j]; cnt++;
                        }
                    if (cnt > 0) { _lr[i] += r / cnt; _lg[i] += g / cnt; _lb[i] += b / cnt; }
                }
    }

    private void AddLight(int c, (float r, float g, float b) col, float k)
    {
        int mx = L.X(c) - _mx0, my = L.Y(c) - _my0;
        if ((uint)mx >= (uint)_mw || (uint)my >= (uint)_mh) return;
        int i = my * _mw + mx;
        _lr[i] += col.r * k; _lg[i] += col.g * k; _lb[i] += col.b * k;
    }

    // 벽 밑 그림자: 북쪽 벽 앞면 아래 · 서쪽 · 동쪽 벽 곁 (도트 계단)
    private static readonly float[] AoN = MakeAo(T, DotTiles.Face, 8, 0.30f), AoW = MakeAo(T, 0, 5, 0.16f);
    private static readonly int[] AoNi = AoN.Select(v => (int)(v * 256)).ToArray(), AoWi = AoW.Select(v => (int)(v * 256)).ToArray();
    /// <summary>시험용: 빛을 한 점씩 곱한다 (네 점씩과 같은 그림인지 견준다).</summary>
    public static bool NoVector;
    private static readonly int[] AoWiRev = AoWi.Reverse().ToArray();   // 오른쪽 벽 그림자 (AoWi[T-1-xx]) — 네 점씩 읽는다
    private static float[] MakeAo(int n, int from, int len, float k)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++) { int d = i - from; if (d >= 0 && d < len) { float t = 1 - d / (float)len; a[i] = MathF.Round(k * t * t * 8) / 8; } }
        return a;
    }

    private bool Space(int i) => _lk[i] is 1 or 3;

    /// <summary>빛 지도를 그림에 곱한다 (칸 모서리 빛을 이어 부드럽게 · 벽 너머로 새지 않게 모서리는 빈 칸끼리만 고른다).</summary>
    private void ApplyLight(Img img, int w, int h)
    {
        // 모서리 빛 (w+1)×(h+1): 둘레 네 칸 중 빈 칸의 고른 값
        int gw = w + 1, gh = h + 1;
        if (_cr.Length < gw * gh) { _cr = new float[gw * gh]; _cg = new float[gw * gh]; _cb = new float[gw * gh]; _crr = new float[gw * gh]; _crg = new float[gw * gh]; _crb = new float[gw * gh]; }
        var cr = _cr; var cg = _cg; var cb = _cb; var crr = _crr; var crg = _crg; var crb = _crb;
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                float r = 0, g = 0, b = 0, rr = 0, rg = 0, rb = 0; int cnt = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int i = (gy + dy) * _mw + gx + dx;
                        if (!Space(i)) continue;
                        r += _lr[i]; g += _lg[i]; b += _lb[i]; rr += _rr[i]; rg += _rg[i]; rb += _rb[i]; cnt++;
                    }
                int gi = gy * gw + gx;
                if (cnt > 0) { cr[gi] = r / cnt; cg[gi] = g / cnt; cb[gi] = b / cnt; crr[gi] = rr / cnt; crg[gi] = rg / cnt; crb[gi] = rb / cnt; }
                else cr[gi] = cg[gi] = cb[gi] = crr[gi] = crg[gi] = crb[gi] = 0;
            }
        int ax0 = _mx0 + 1, ay0 = _my0 + 1;   // 그리는 칸 (0, 0)의 도면 자리
        float ar = Ambient * AmbientTint.r, ag = Ambient * AmbientTint.g, ab = Ambient * AmbientTint.b, keep = 1 - Ambient;
        // 고정 소수 (16.16 · 1.0 = 256): 도트마다 더하기 · 곱하기 · 밀기만
        const float F = 256f * 65536f;
        int W = img.W;
        Parallel.For(0, h, vy =>
        {
            var d = MemoryMarshal.Cast<byte, uint>(img.Px.AsSpan());
            Span<float> fr = stackalloc float[9], fg = stackalloc float[9], fb = stackalloc float[9], pf = stackalloc float[9];   // 칸마다 3×3 고운 점 빛 · 웅덩이 곱 − 1
            for (int vx = 0; vx < w; vx++)
            {
                int mi = (vy + 1) * _mw + vx + 1;
                byte kind = _lk[mi];
                if (kind == 0) continue;
                int px0 = vx * T, py0 = vy * T;
                if (kind is 1 or 3)
                {
                    int g00 = vy * gw + vx, g10 = g00 + 1, g01 = g00 + gw, g11 = g01 + 1;
                    bool wn = kind == 1 && _lk[mi - _mw] == 2, ww = kind == 1 && _lk[mi - 1] == 2, we = kind == 1 && _lk[mi + 1] == 2;
                    // 3×3 고운 점 (16도트마다): 칸 모서리 빛을 잇고 · 방 조명 몫에 등 웅덩이를 곱한다 (방 평균은 그대로)
                    int fx0 = 2 * (ax0 + vx), fy0 = 2 * (ay0 + vy);
                    bool flat = true;
                    for (int k = 0; k < 9; k++) { pf[k] = PoolAt(fx0 + k % 3, fy0 + k / 3) - 1; if (pf[k] > 0.004f || pf[k] < -0.004f) flat = false; }
                    if (flat)
                    {
                        // 웅덩이가 없는 칸 (등이 꺼졌거나 · 문 칸): 예전처럼 한 줄 32도트
                        for (int yy = 0; yy < T; yy++)
                        {
                            float ty = (yy + 0.5f) / T;
                            float r0 = ar + keep * (cr[g00] + (cr[g01] - cr[g00]) * ty), g0 = ag + keep * (cg[g00] + (cg[g01] - cg[g00]) * ty), b0 = ab + keep * (cb[g00] + (cb[g01] - cb[g00]) * ty);
                            float r1 = ar + keep * (cr[g10] + (cr[g11] - cr[g10]) * ty), g1 = ag + keep * (cg[g10] + (cg[g11] - cg[g10]) * ty), b1 = ab + keep * (cb[g10] + (cb[g11] - cb[g10]) * ty);
                            Row(d, (py0 + yy) * W + px0, r0, g0, b0, r1, g1, b1, wn ? AoNi[yy] : 0, ww, we);
                        }
                        continue;
                    }
                    Fine(fr, cr, crr, g00, g10, g01, g11, pf, ar, keep);
                    Fine(fg, cg, crg, g00, g10, g01, g11, pf, ag, keep);
                    Fine(fb, cb, crb, g00, g10, g01, g11, pf, ab, keep);
                    for (int yy = 0; yy < T; yy++)
                    {
                        int j = yy >> 4; float ty = ((yy & 15) + 0.5f) / 16;
                        int aoRow = wn ? AoNi[yy] : 0;
                        int a0 = j * 3, a1 = a0 + 3;
                        float r0 = fr[a0] + (fr[a1] - fr[a0]) * ty, g0 = fg[a0] + (fg[a1] - fg[a0]) * ty, b0 = fb[a0] + (fb[a1] - fb[a0]) * ty;
                        float r1 = fr[a0 + 1] + (fr[a1 + 1] - fr[a0 + 1]) * ty, g1 = fg[a0 + 1] + (fg[a1 + 1] - fg[a0 + 1]) * ty, b1 = fb[a0 + 1] + (fb[a1 + 1] - fb[a0 + 1]) * ty;
                        float r2 = fr[a0 + 2] + (fr[a1 + 2] - fr[a0 + 2]) * ty, g2 = fg[a0 + 2] + (fg[a1 + 2] - fg[a0 + 2]) * ty, b2 = fb[a0 + 2] + (fb[a1 + 2] - fb[a0 + 2]) * ty;
                        Row(d, (py0 + yy) * W + px0, r0, g0, b0, r1, g1, b1, aoRow, ww, we, 0, 16);
                        Row(d, (py0 + yy) * W + px0 + 16, r1, g1, b1, r2, g2, b2, aoRow, ww, we, 16, 16);
                    }
                }
                else
                {
                    // 벽: 맞닿은 방의 빛 — 위아래가 트였으면 위→아래로 · 아니면 왼→오른쪽으로 잇는다
                    int iN = mi - _mw, iS = mi + _mw, iW = mi - 1, iE = mi + 1;
                    bool n = Space(iN), s = Space(iS), wv = Space(iW), e = Space(iE);
                    int a, b; bool vertical;
                    if (n || s) { a = n ? iN : iS; b = s ? iS : iN; vertical = true; }
                    else if (wv || e) { a = wv ? iW : iE; b = e ? iE : iW; vertical = false; }
                    else { a = mi; b = mi; vertical = true; }   // 모서리 벽: 제 빛 (대각선 · 이웃 벽에서)
                    float ra = ar + keep * _lr[a], ga = ag + keep * _lg[a], ba = ab + keep * _lb[a];
                    float rb = ar + keep * _lr[b], gb = ag + keep * _lg[b], bb = ab + keep * _lb[b];
                    bool face = _lk[iS] == 1;   // 앞면 (뚜껑 아래 14줄)은 마주한 남쪽 방의 빛 — 그 바닥 칸 위 모서리 빛을 잇는다 (벽 너머 방의 빛이 새지 않게)
                    int gS0 = (vy + 1) * gw + vx, gS1 = gS0 + 1;
                    for (int yy = 0; yy < T; yy++)
                    {
                        if (face && yy >= ArtWalls.CapH)
                        {
                            Row(d, (py0 + yy) * W + px0, ar + keep * cr[gS0], ag + keep * cg[gS0], ab + keep * cb[gS0], ar + keep * cr[gS1], ag + keep * cg[gS1], ab + keep * cb[gS1], 0, false, false);
                            continue;
                        }
                        if (vertical)
                        {
                            float t = (yy + 0.5f) / T;
                            float r = ra + (rb - ra) * t, g = ga + (gb - ga) * t, bl = ba + (bb - ba) * t;
                            Row(d, (py0 + yy) * W + px0, r, g, bl, r, g, bl, 0, false, false);
                        }
                        else Row(d, (py0 + yy) * W + px0, ra, ga, ba, rb, gb, bb, 0, false, false);
                    }
                }
            }
        });

        // 3×3 고운 점 하나의 빛깔 갈래: 모서리 넷 · 변 가운데 (두 모서리 평균) · 한가운데 (넷 평균) — 방 조명 몫에 웅덩이 곱
        static void Fine(Span<float> o, float[] tot, float[] room, int g00, int g10, int g01, int g11, Span<float> pf, float amb, float keep)
        {
            float t00 = tot[g00], t10 = tot[g10], t01 = tot[g01], t11 = tot[g11], q00 = room[g00], q10 = room[g10], q01 = room[g01], q11 = room[g11];
            float tm0 = (t00 + t10) * 0.5f, tm2 = (t01 + t11) * 0.5f, t0m = (t00 + t01) * 0.5f, t2m = (t10 + t11) * 0.5f, tmm = (tm0 + tm2) * 0.5f;
            float qm0 = (q00 + q10) * 0.5f, qm2 = (q01 + q11) * 0.5f, q0m = (q00 + q01) * 0.5f, q2m = (q10 + q11) * 0.5f, qmm = (qm0 + qm2) * 0.5f;
            o[0] = amb + keep * (t00 + q00 * pf[0]); o[1] = amb + keep * (tm0 + qm0 * pf[1]); o[2] = amb + keep * (t10 + q10 * pf[2]);
            o[3] = amb + keep * (t0m + q0m * pf[3]); o[4] = amb + keep * (tmm + qmm * pf[4]); o[5] = amb + keep * (t2m + q2m * pf[5]);
            o[6] = amb + keep * (t01 + q01 * pf[6]); o[7] = amb + keep * (tm2 + qm2 * pf[7]); o[8] = amb + keep * (t11 + q11 * pf[8]);
        }

        // 한 칸의 한 줄 토막 (xoff부터 len 도트): 왼쪽 → 오른쪽 빛을 잇고 · 그림자(ao — 1.0 = 256에서 빼는 몫)를 곱한다
        static void Row(Span<uint> d, int at, float r0, float g0, float b0, float r1, float g1, float b1, int aoRow, bool ww, bool we, int xoff = 0, int len = T)
        {
            const int max = (int)(MaxLight * 256);
            int vr = (int)((r0 + (r1 - r0) * (0.5f / len)) * F), vg = (int)((g0 + (g1 - g0) * (0.5f / len)) * F), vb = (int)((b0 + (b1 - b0) * (0.5f / len)) * F);
            int sr = (int)((r1 - r0) / len * F), sg = (int)((g1 - g0) / len * F), sb = (int)((b1 - b0) / len * F);
            bool shade = aoRow != 0 || ww || we;
            int xx = 0;
            if (Vector128.IsHardwareAccelerated && !NoVector)
            {
                // 네 점씩 (정수 셈 그대로 — 아래 한 점씩과 비트까지 같다)
                var lane = Vector128.Create(0, 1, 2, 3);
                Vector128<int> vR = Vector128.Create(vr) + lane * sr, vG = Vector128.Create(vg) + lane * sg, vB = Vector128.Create(vb) + lane * sb;
                Vector128<int> sR = Vector128.Create(sr * 4), sG = Vector128.Create(sg * 4), sB = Vector128.Create(sb * 4);
                Vector128<int> vmax = Vector128.Create(max), zero = Vector128<int>.Zero, ao0 = Vector128.Create(256 - aoRow);
                Vector128<uint> m255 = Vector128.Create(255u), mFF = Vector128.Create(0xFFu), mA = Vector128.Create(0xFF000000u);
                ref uint row = ref d[at];
                for (; xx + 4 <= len; xx += 4, vR += sR, vG += sG, vB += sB)
                {
                    Vector128<int> mr = Vector128.Min(Vector128.Max(Vector128.ShiftRightArithmetic(vR, 16), zero), vmax);
                    Vector128<int> mg = Vector128.Min(Vector128.Max(Vector128.ShiftRightArithmetic(vG, 16), zero), vmax);
                    Vector128<int> mb = Vector128.Min(Vector128.Max(Vector128.ShiftRightArithmetic(vB, 16), zero), vmax);
                    if (shade)
                    {
                        var ao = ao0;
                        if (ww) ao -= Vector128.LoadUnsafe(ref AoWi[xoff + xx]);
                        if (we) ao -= Vector128.LoadUnsafe(ref AoWiRev[xoff + xx]);
                        mr = Vector128.ShiftRightArithmetic(mr * ao, 8); mg = Vector128.ShiftRightArithmetic(mg * ao, 8); mb = Vector128.ShiftRightArithmetic(mb * ao, 8);
                    }
                    var v = Vector128.LoadUnsafe(ref row, (nuint)xx);
                    var R = Vector128.Min(m255, ((v & mFF) * mr.AsUInt32()) >> 8);
                    var G = Vector128.Min(m255, (((v >> 8) & mFF) * mg.AsUInt32()) >> 8);
                    var B = Vector128.Min(m255, (((v >> 16) & mFF) * mb.AsUInt32()) >> 8);
                    ((v & mA) | B << 16 | G << 8 | R).StoreUnsafe(ref row, (nuint)xx);
                }
                vr += xx * sr; vg += xx * sg; vb += xx * sb;
            }
            for (; xx < len; xx++, vr += sr, vg += sg, vb += sb)
            {
                int mr = Math.Clamp(vr >> 16, 0, max), mg = Math.Clamp(vg >> 16, 0, max), mb = Math.Clamp(vb >> 16, 0, max);
                if (shade)
                {
                    int ao = 256 - aoRow - (ww ? AoWi[xoff + xx] : 0) - (we ? AoWi[T - 1 - xoff - xx] : 0);
                    mr = mr * ao >> 8; mg = mg * ao >> 8; mb = mb * ao >> 8;
                }
                ref uint c = ref d[at + xx];
                uint v = c;
                uint R = Math.Min(255u, (v & 0xFF) * (uint)mr >> 8), G = Math.Min(255u, (v >> 8 & 0xFF) * (uint)mg >> 8), B = Math.Min(255u, (v >> 16 & 0xFF) * (uint)mb >> 8);
                c = (v & 0xFF000000u) | B << 16 | G << 8 | R;
            }
        }
    }

    // ── 빛 웅덩이 (텍스처 T5): 등 자리 (방 바닥 상자 둘레에서 2칸 안쪽 · 4칸마다 + 벽 띠등) · 16도트 고운 점마다 모양 s (반지름 2.5칸 · (1 − d²/r²)²) ──
    public const float PoolR = 2.5f;
    /// <summary>시험용: 웅덩이 없이 (방 평균이 그대로인지 견준다).</summary>
    public static bool NoPools;
    private float[]? _poolS;      // 고운 점 (2W+1)×(2H+1)의 웅덩이 모양 0~1
    private int[]? _poolRoom;     // 그 점의 방 (−1 없음)
    private float[]? _poolMean;   // 방마다 모양의 평균 (곱해도 방 평균이 그대로이게)
    private float[] _poolK = Array.Empty<float>();
    /// <summary>시험 · 관찰용: 등 자리 (방 · 칸 자리 x · y).</summary>
    public List<(int room, float x, float y)> PoolSpots { get { BuildPools(); return _spots!; } }
    private List<(int room, float x, float y)>? _spots;

    private void BuildPools()
    {
        if (_poolS != null) return;
        var spots = new List<(int room, float x, float y)>();
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            var cells = L.Rooms[r].Cells.Where(c => L.Kind[c] == CellKind.Floor).ToArray();
            if (cells.Length == 0) continue;
            int x0 = cells.Min(L.X), x1 = cells.Max(L.X), y0 = cells.Min(L.Y), y1 = cells.Max(L.Y);
            IEnumerable<int> Axis(int a0, int a1) => a1 - a0 < 5 ? new[] { (a0 + a1) / 2 } : Enumerable.Range(0, (a1 - a0 - 2) / 4 + 1).Select(k => a0 + 2 + 4 * k).Where(v => v <= a1 - 1);
            foreach (int sy in Axis(y0, y1))
                foreach (int sx in Axis(x0, x1))
                {
                    // 바닥 칸으로 옮긴다 (그 방 · 2칸 안 · 가장 가까운)
                    int best = -1, bd = int.MaxValue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int x = sx + dx, y = sy + dy;
                            if (!L.In(x, y)) continue;
                            int c = L.Index(x, y);
                            if (L.Kind[c] != CellKind.Floor || L.Room[c] != r) continue;
                            int d = dx * dx + dy * dy;
                            if (d < bd) { bd = d; best = c; }
                        }
                    if (best >= 0) spots.Add((r, L.X(best) + 0.5f, L.Y(best) + 0.5f));
                }
        }
        // 벽 띠등도 등 자리 (앞면 바로 아래)
        for (int c = 0; c < L.N; c++)
        {
            int x = L.X(c), y = L.Y(c);
            if (L.Kind[c] != CellKind.Wall || !L.In(x, y + 1) || L.Kind[L.Index(x, y + 1)] != CellKind.Floor) continue;
            int room = L.Room[L.Index(x, y + 1)];
            if (room >= 0 && FaceDeco(x, y, WallOf(c), room) == 1) spots.Add((room, x + 0.5f, y + 1.3f));
        }
        _spots = spots;
        int gw = 2 * L.W + 1, gh = 2 * L.H + 1;
        var s = new float[gw * gh]; var room0 = new int[gw * gh];
        var byRoom = spots.GroupBy(q => q.room).ToDictionary(g => g.Key, g => g.ToArray());
        var sum = new double[L.Rooms.Count]; var cnt = new int[L.Rooms.Count];
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                int gi = gy * gw + gx;
                // 이 점에 닿은 칸 넷 가운데 방이 있는 첫 칸
                int rm = -1;
                for (int k = 0; k < 4 && rm < 0; k++)
                {
                    int cx = (gx - (k & 1)) / 2, cy = (gy - (k >> 1)) / 2;
                    if (gx - (k & 1) < 0 || gy - (k >> 1) < 0 || !L.In(cx, cy)) continue;
                    int c = L.Index(cx, cy);
                    if (L.Kind[c] == CellKind.Floor) rm = L.Room[c];
                }
                room0[gi] = rm;
                if (rm < 0 || !byRoom.TryGetValue(rm, out var sp)) continue;
                float px = gx / 2f, py = gy / 2f, v = 0;
                foreach (var q in sp)
                {
                    float dx = px - q.x, dy = py - q.y, d2 = (dx * dx + dy * dy) / (PoolR * PoolR);
                    if (d2 < 1) v += (1 - d2) * (1 - d2);
                }
                s[gi] = Math.Min(1f, v);
            }
        // 방 평균: 바닥 칸마다 그려지는 대로 (3×3 점을 겹선형 무게로 — 모서리 1/16 · 변 1/8 · 가운데 1/4)
        for (int c = 0; c < L.N; c++)
        {
            if (L.Kind[c] != CellKind.Floor || L.Room[c] < 0) continue;
            int bx = 2 * L.X(c), by = 2 * L.Y(c);
            double m = 0;
            for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++) m += s[(by + j) * gw + bx + i] * (i == 1 ? 2 : 1) * (j == 1 ? 2 : 1) / 16.0;
            sum[L.Room[c]] += m; cnt[L.Room[c]]++;
        }
        _poolMean = Enumerable.Range(0, L.Rooms.Count).Select(r => cnt[r] > 0 ? (float)(sum[r] / cnt[r]) : 0.5f).ToArray();
        _poolRoom = room0; _poolS = s;
    }

    /// <summary>방마다 웅덩이 대비 k (장마다): 가장 어두운 곳 = 평균의 0.7 (낮) · 0.45 (등이 낮은 밤) · 0.25 (비상등) — 등이 꺼졌으면 웅덩이 없음.</summary>
    private void PoolK(Network? net, Light? light)
    {
        BuildPools();
        if (_poolK.Length != L.Rooms.Count) _poolK = new float[L.Rooms.Count];
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            float lamp = light != null && r < light.Lamp.Length ? light.Lamp[r] : 300;
            byte state = net?.LampState(r) ?? (byte)1;
            float contrast = state == 2 ? 0.75f : lamp <= 0 ? 0 : lamp < 200 ? 0.55f : 0.3f;
            _poolK[r] = contrast / Math.Max(0.15f, _poolMean![r]);
        }
    }

    /// <summary>고운 점 (2x, 2y — 16도트마다)의 웅덩이 곱 (방 조명 몫에만): 1 + k·(s − 방 평균) · 0.15~2.</summary>
    private float PoolAt(int fx, int fy)
    {
        int gw = 2 * L.W + 1;
        if (fx < 0 || fy < 0 || fx >= gw || fy >= 2 * L.H + 1) return 1;
        int gi = fy * gw + fx, r = _poolRoom![gi];
        if (r < 0) return 1;
        if (NoPools) return 1;
        return Math.Clamp(1 + _poolK[r] * (_poolS![gi] - _poolMean![r]), 0.15f, 2f);
    }

    /// <summary>벽 앞면 띠 조명 (py = 앞면 위 끝 · 띠등은 16째 줄): 방 조명이 켜졌으면 그 빛깔로 · 비상등이면 붉게 빛난다 (곱한 뒤에 — 스스로 빛나는 것).</summary>
    private void FaceLamp(int px, int py, int room, Network? net, Light? light)
    {
        if (room < 0) return;
        var v = RoomLamp(room, net, light);
        float m = Math.Max(v.r, Math.Max(v.g, v.b));
        if (m < 0.05f) return;
        byte state = net?.LampState(room) ?? 1;
        var core = state == 2 ? Rgba.Hex("#ff4a3a") : new Rgba((byte)Math.Min(255, 235 * v.r / m), (byte)Math.Min(255, 238 * v.g / m), (byte)Math.Min(255, 230 * v.b / m));
        float k = Math.Min(1f, 0.35f + 0.65f * m);
        _emissive.Add(img =>
        {
            img.Fill(px + 8, py + 16, 16, 1, core, k);
            img.Fill(px + 9, py + 15, 14, 1, core, 0.5f * k);
            img.Glow(px + 16, py + 17, 8, core, 0.2f * k);
        });
    }
}
