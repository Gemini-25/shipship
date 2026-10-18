namespace ShipEngine;

/// <summary>
/// ④ 칸 강체 (라) 잔칸 4×4 (docs/ENGINE.md ④ '바퀴 3 설계 — 칸 강체' · 함장과 정함: 필요한 것만 잘게).
/// <para>
/// 벽 · 바닥 · 문 칸마다 25cm 잔칸 16개 (비트 — 처음엔 다 찼다). 기체가 새는 넓이는 ④의 구멍 넓이 하나로만 셈하고 (두 장부를 두지 않는다) · 잔칸은 그 구멍의 모양이다:
/// 구멍(넓이 0.04m² 넘게)은 바깥 쪽 면에서 들쭉날쭉 파이고 (넓이만큼 — 벽 한 면 2.5m²가 16개) · 통째로 나간 벽은 두셋만 남은 테두리 · 바닥 판은 가운데에서 ·
/// 부딪힌 칸은 맞닿은 쪽에서 으스러지고 · 갈라진 사이 칸은 가운데를 들쭉날쭉 찢어 남은 잔칸이 양쪽 몸의 찢긴 끝이 된다 (그림 · 부스러기).
/// 부딪힘은 잔칸으로 본다 (빈 잔칸에는 닿지 않는다 — 찢긴 끝의 모양 그대로).
/// </para>
/// </summary>
public sealed partial class Structure
{
    public const int SubN = 4;
    public const ushort SubFull = 0xFFFF;
    public const float SubMinArea = 0.04f;
    /// <summary>칸마다 잔칸 (비트 sy*4 + sx — 1이면 남아 있다).</summary>
    public ushort[] Sub = Array.Empty<ushort>();
    private int _holeSeen = -1;
    public long SubCarved;
    /// <summary>구멍이 커진 순간 그 칸 양쪽의 기압 차 (kPa — 장이 다음 틱에 비우기 전): 파인 잔칸 · 찢긴 조각을 미는 기압. 격자를 본 뒤 비운다.</summary>
    private float[] _blow = Array.Empty<float>();

    /// <summary>구멍 넓이를 바꾼다 (④가 내는 구멍은 모두 이리로): 커지면 그 순간의 기압 차를 적어 둔다.</summary>
    private void SetHole(int c, float area)
    {
        if (area > F.Hole[c] && _blow.Length == L.N) _blow[c] = Math.Max(_blow[c], (float)PressureAcross(c));
        F.SetHole(c, area);
    }

    private void BuildSub()
    {
        Sub = new ushort[L.N];
        _blow = new float[L.N];
        for (int c = 0; c < L.N; c++) Sub[c] = L.Kind[c] is CellKind.Wall or CellKind.Floor or CellKind.Door ? SubFull : (ushort)0;
    }

    public static int SubCount(ushort m) => System.Numerics.BitOperations.PopCount(m);
    public bool SubAt(int c, int sx, int sy) => c >= 0 && c < Sub.Length && (Sub[c] >> (sy * SubN + sx) & 1) != 0;

    /// <summary>구멍 넓이가 뜻하는 빈 잔칸 수 (벽 · 문: 한 면 2.5m²가 16개 · 통째로면 테두리 두셋만 · 바닥: 판 2m²가 16개).</summary>
    private int SubHoles(int c)
    {
        float a = F.Hole[c];
        if (a < SubMinArea) return 0;
        bool floor = L.Kind[c] == CellKind.Floor;
        if (!floor && a >= 2 || floor && a >= DeckGone) return 16 - (2 + (int)(DotHash(c, 7) % 3));
        return Math.Clamp((int)Math.Ceiling(16 * a / (floor ? 2.0 : Field.WallFace)), 1, 15);
    }

    /// <summary>구멍이 바뀌었으면 (③의 구멍 판이 바뀔 때만 — 칸 순서) 잔칸을 구멍 넓이만큼 판다. 메운(덧댄) 구멍은 잔칸을 되돌리지 않는다 (그림이 덧댄 판으로 그린다).</summary>
    private void SyncSub(Sim sim)
    {
        if (_holeSeen == F.HoleVersion) return;
        _holeSeen = F.HoleVersion;
        for (int c = 0; c < L.N; c++)
        {
            if (Sub[c] == 0 || F.Severed[c] != 0 || F.Hole[c] < SubMinArea) continue;
            int want = SubHoles(c), have = 16 - SubCount(Sub[c]);
            if (want <= have) continue;
            var (sx, sy) = L.Kind[c] == CellKind.Floor ? (1 + (int)(DotHash(c, 3) & 1), 1 + (int)(DotHash(c, 4) & 1)) : OuterSeed(c);
            ushort before = Sub[c];
            Carve(c, want - have, sx, sy);
            // 파인 잔칸은 부스러기가 되어 구멍 바깥쪽으로 튄다 — 빠지는 공기가 민다 (기압 차만큼 · 없으면 흩어질 뿐)
            double blow = BlowKick(Math.Max(_blow[c], PressureAcross(c)), _smass.Length > c ? _smass[c] / 16 : 0);
            Shed(sim, c, (ushort)(before & ~Sub[c]), DamageCause[c], sx - 1.5, sy - 1.5, Math.Max(SpallKick, blow), byGas: blow > SpallKick);
        }
    }

    /// <summary>칸 c 양쪽의 기압 차 (kPa — 벽 · 문: 네 이웃 중 공기 칸의 가장 높은 것과 가장 낮은 것 · 우주는 0 · 바닥 판: 그 칸의 기압).</summary>
    private double PressureAcross(int c)
    {
        if (L.Kind[c] == CellKind.Floor) return F.IsOut(c) ? 0 : F.Pressure(c);
        int x = L.X(c), y = L.Y(c);
        double hi = 0, lo = double.MaxValue;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int j = L.In(x + dx, y + dy) ? L.Index(x + dx, y + dy) : -1;
            double p = j < 0 || F.IsOut(j) ? 0 : L.Kind[j] is CellKind.Floor or CellKind.Door ? F.Pressure(j) : double.NaN;
            if (double.IsNaN(p)) continue;
            hi = Math.Max(hi, p); lo = Math.Min(lo, p);
        }
        return lo == double.MaxValue ? 0 : hi - lo;
    }

    /// <summary>벽 구멍이 파이기 시작하는 잔칸: 우주 · 기압이 낮은 이웃 쪽 면의 가운데 (바깥에서 뚫렸다).</summary>
    private (int sx, int sy) OuterSeed(int c)
    {
        int x = L.X(c), y = L.Y(c), best = -1; double bp = double.MaxValue;
        var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        for (int k = 0; k < 4; k++)
        {
            var (dx, dy) = dirs[k];
            int j = L.In(x + dx, y + dy) ? L.Index(x + dx, y + dy) : -1;
            double p = j < 0 || F.IsOut(j) ? -1 : L.Kind[j] is CellKind.Floor or CellKind.Door ? F.Pressure(j) : double.MaxValue;
            if (p < bp) { bp = p; best = k; }
        }
        int mid = 1 + (int)(DotHash(c, 5) & 1);
        return best switch { 0 => (3, mid), 1 => (0, mid), 2 => (mid, 3), 3 => (mid, 0), _ => (mid, mid) };
    }

    /// <summary>잔칸 n개를 (sx, sy)에서부터 들쭉날쭉 판다 (빈 잔칸에 맞닿은 남은 잔칸 중 칸 · 순서마다 정해진 해시가 가장 작은 것 — 결정론).</summary>
    public int Carve(int c, int n, int sx, int sy)
    {
        if (c < 0 || c >= Sub.Length || n <= 0 || Sub[c] == 0) return 0;
        EnsureShip();   // 몸의 질량은 파기 전의 잔칸으로 센다 (판 잔칸은 부스러기로 몸에서 뗀다 — 두 번 빼지 않게)
        int done = 0;
        ushort m = Sub[c];
        sx = Math.Clamp(sx, 0, SubN - 1); sy = Math.Clamp(sy, 0, SubN - 1);
        while (done < n && m != 0)
        {
            int pick = -1; uint ph = uint.MaxValue;
            bool anyHole = m != SubFull;
            for (int i = 0; i < 16; i++)
            {
                if ((m >> i & 1) == 0) continue;
                int ix = i % SubN, iy = i / SubN;
                bool touch = !anyHole ? ix == sx && iy == sy
                    : ix > 0 && (m >> (i - 1) & 1) == 0 || ix < SubN - 1 && (m >> (i + 1) & 1) == 0 || iy > 0 && (m >> (i - SubN) & 1) == 0 || iy < SubN - 1 && (m >> (i + SubN) & 1) == 0;
                if (!touch) continue;
                // 해시 + 시작 잔칸에서 먼 것은 조금 덜 (구멍이 한쪽 면에서 자란다)
                uint h = DotHash(c * 16 + i, 11 + done) % 1000 + (uint)(150 * (Math.Abs(ix - sx) + Math.Abs(iy - sy)));
                if (h < ph) { ph = h; pick = i; }
            }
            if (pick < 0) break;
            m = (ushort)(m & ~(1 << pick));
            done++;
        }
        Sub[c] = m;
        SubCarved += done;
        return done;
    }

    /// <summary>갈라진 사이 칸을 찢는다: 양쪽 몸 사이를 가로지르는 들쭉날쭉한 줄을 비운다 — 남은 잔칸은 가까운 쪽 몸의 찢긴 끝 (그림).</summary>
    private void TearGap(Sim sim, int c, int cause)
    {
        if (Sub[c] == 0) return;
        EnsureShip();
        ushort before = Sub[c];
        int x = L.X(c), y = L.Y(c);
        bool Body(int xx, int yy) => L.In(xx, yy) && Frag[L.Index(xx, yy)] >= 0 && F.Severed[L.Index(xx, yy)] == 0 && L.Kind[L.Index(xx, yy)] != CellKind.Outside;
        bool vertical = Body(x - 1, y) && Body(x + 1, y) || !(Body(x, y - 1) && Body(x, y + 1));   // 좌우가 몸이면 세로로 찢는다
        ushort m = Sub[c];
        int line = 1 + (int)(DotHash(c, 13) & 1);
        for (int k = 0; k < SubN; k++)
        {
            line = Math.Clamp(line + (int)(DotHash(c * 4 + k, 17) % 3) - 1, 1, 2);
            int i = vertical ? k * SubN + line : line * SubN + k;
            m = (ushort)(m & ~(1 << i));
            if (DotHash(c * 4 + k, 19) % 3 == 0) { int j = vertical ? k * SubN + line + 1 : (line + 1) * SubN + k; m = (ushort)(m & ~(1 << j)); }
        }
        SubCarved += SubCount(Sub[c]) - SubCount(m);
        Sub[c] = m;
        // 찢긴 조각: 사이 칸이나 그 곁에 공기가 있었으면 (갈라지기 전 기압) 그 기압이 밀어내고 (기체가 준 운동량) · 없으면 흩어질 뿐
        double gp = 0;
        if (_p.Length == L.N)
        {
            gp = Math.Max(Math.Max(0, _p[c]), _blow[c]);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                if (L.In(x + dx, y + dy)) { int j = L.Index(x + dx, y + dy); gp = Math.Max(gp, Math.Max(_p[j], _blow[j])); }
        }
        double blow = BlowKick(gp, _smass.Length > c ? _smass[c] / 16 : 0);
        Shed(sim, c, (ushort)(before & ~m), cause, 0, 0, Math.Max(SpallKick, blow), byGas: blow > SpallKick);
    }

    /// <summary>찢긴 사이 칸(우주가 된 칸)의 잔칸 (sx, sy)가 붙은 몸 (가까운 면의 이웃 칸의 몸 · 없으면 -1) — 그림이 그 몸과 함께 그린다.</summary>
    public int TornOwner(int c, int sx, int sy)
    {
        int x = L.X(c), y = L.Y(c);
        int Of(int xx, int yy) => L.In(xx, yy) ? BodyOf(L.Index(xx, yy)) : -1;
        int hx = sx < SubN / 2 ? Of(x - 1, y) : Of(x + 1, y), hy = sy < SubN / 2 ? Of(x, y - 1) : Of(x, y + 1);
        double dx = sx < SubN / 2 ? sx + 0.5 : SubN - sx - 0.5, dy = sy < SubN / 2 ? sy + 0.5 : SubN - sy - 0.5;   // 그 면까지 잔칸 거리
        if (hx >= 0 && (hy < 0 || dx <= dy)) return hx;
        return hy;
    }

    /// <summary>칸 · 잔칸 번호의 해시 (결정론 — 모양이 시드 · 칸마다 정해진다).</summary>
    private static uint DotHash(int a, int b)
    {
        uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return h;
    }
}
