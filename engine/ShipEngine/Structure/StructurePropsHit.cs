namespace ShipEngine;

/// <summary>
/// ④ 물체 닿음 — 정적 틀 (docs/PHYSICS.md §3-5 · PH4a): 벽 · 문 칸은 남은 25cm 잔칸 상자 (구멍은 실제 틈) · 닫힌 문은 벽 · 열린 문 칸은 양옆 12.5cm 기둥 (PH4b: 닫혀 오는 문짝 판도) ·
/// 제자리 가구는 그 꼴의 기둥 (높이 겹침 없이 기둥만 — 높이는 PH7). 모두 도트 (1/32m) 격자 위라 한 도트가 막혔나로 묻는다.
/// 닿음은 물체 껍질 꼭짓점 · 테두리 표본이 이번 잔걸음에 갈 길을 도트마다 따라가 본다 (투기적 닿음 — 빠른 것도 잔칸을 건너뛰지 않는다) ·
/// 법선은 잔칸 면 · 가구 기둥은 8이웃의 빈 쪽 기울기 · 사분면마다 가장 가까운 점 (많아야 4). 풀이는 순차 충격량 (법선 jn ≥ 0 · 쿨롱 |jt| ≤ μ jn ·
/// 다가오는 빠르기 0.5m/s 넘으면 반발 e = 둘 가운데 작은 것) · 겹침이 0.5도트 넘는 몫의 20%만 자리를 옮긴다 (빠르기는 그대로).
/// 틈새 지도 (칸마다 막힌 칸까지의 거리)는 깨어 있는 물체가 지나는 칸만 물을 때 센다 — 먼 것은 큰 잔걸음으로 · 가까우면 2도트씩.
/// </summary>
public sealed partial class Structure
{
    private const int MaxContacts = 4;
    private const double BounceV = 0.5, Slop = 0.5 * DotForm.Dot, FixShare = 0.2;

    private struct PropContact
    {
        public double Rx, Ry, Nx, Ny, Sep, Mu, E, Kn, Kt, Target, Jn, Jt;
        public int Cell, Quad;   // 닿은 도트의 칸 (구멍 벽인가 — PH4c) · 표본 사분면 (따뜻한 시작 열쇠 — PH6a)
        public bool Bounce;
    }

    private readonly PropContact[] _propHits = new PropContact[MaxContacts];
    private const int ClearSlots = 256;
    private readonly int[] _clearKey = NewKeys(ClearSlots), _clear = new int[ClearSlots];   // 칸 번호 아래 8비트 자리마다 그 칸 · 막힌 칸까지 체비셰프 거리 (칸 — 막힌 칸 0 · 빈 자리 −1 · 겹치면 다시 센다)
    private static int[] NewKeys(int n) { var a = new int[n]; Array.Fill(a, -1); return a; }
    private byte[] _clearFurn = Array.Empty<byte>();  // 그 지도를 셀 때의 가구 제자리
    private (DotForm? form, int ox, int oy)[]? _furnForms;   // 가구마다 꼴 · 꼴 모서리의 도트 자리 (찍지 않는다 — 도면에서 늘 같게)
    private sbyte[]? _doorAxis;                       // 문 칸마다 지나는 쪽 (1: 좌우가 벽 — 남북으로 지난다 · 2: 위아래가 벽 · 0 모름)
    private int _steel = -1;

    /// <summary>꼴의 닿음 표본 (무게중심에서 — 껍질 꼭짓점 · 테두리 기둥 가운데) · 가장 먼 꼭짓점까지 (m) — 꼴에 한 번 세어 둔다 (<see cref="PropShape"/>).</summary>
    private static (double[] x, double[] y, double reach) SamplesOf(DotForm fm) => fm.Shape.Hit;

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private (DotForm? form, int ox, int oy) FurnForm(int fu)
    {
        if (_furnForms == null)
        {
            _furnForms = new (DotForm?, int, int)[L.Furniture.Count];
            for (int i = 0; i < L.Furniture.Count; i++)
            {
                var cells = L.Furniture[i].Cells;
                if (cells.Length == 0) continue;
                int x0 = cells.Min(L.X), y0 = cells.Min(L.Y), bw = cells.Max(L.X) - x0 + 1, bh = cells.Max(L.Y) - y0 + 1;
                var fm = DotForge.Make(T, L.Furniture[i].Type, i < L.Facing.Length ? L.Facing[i] : Facing.S, bw, bh);
                _furnForms[i] = (fm, x0 * 32 + fm.Ox, y0 * 32 + fm.Oy);
            }
        }
        return _furnForms[fu];
    }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int DoorAxis(int c)
    {
        if (_doorAxis == null)
        {
            _doorAxis = new sbyte[L.N];
            for (int i = 0; i < L.N; i++)
            {
                if (L.Kind[i] != CellKind.Door) continue;
                int x = L.X(i), y = L.Y(i);
                bool Wall(int xx, int yy) => !L.In(xx, yy) || L.Kind[L.Index(xx, yy)] == CellKind.Wall;
                _doorAxis[i] = (sbyte)(Wall(x - 1, y) && Wall(x + 1, y) ? 1 : Wall(x, y - 1) && Wall(x, y + 1) ? 2 : 0);
            }
        }
        return _doorAxis[c];
    }

    /// <summary>그 도트가 막혔나 (도면 밖 · 벽 잔칸 · 닫힌 문 잔칸 · 열린 문 기둥 · 제자리 가구 기둥) — 재질 · 가구 기둥인가.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool SolidDot(int dx, int dy, out byte mat, out bool furn)
    {
        if (_steel < 0) _steel = Math.Max(0, T.Materials.FindIndex(m => m.Id == "steel"));
        mat = (byte)_steel; furn = false;
        int cx = dx >> 5, cy = dy >> 5;
        if (!L.In(cx, cy)) return true;
        int c = L.Index(cx, cy), sx = dx & 31, sy = dy & 31;
        switch (L.Kind[c])
        {
            case CellKind.Wall:
                return (Sub[c] >> ((sy >> 3) * SubN + (sx >> 3)) & 1) != 0;
            case CellKind.Door:
            {
                float open = F.DoorOpen[L.DoorAt[c]];
                int axis = DoorAxis(c);
                if (open <= 0 || axis == 0 && !F.DoorPassable(L.DoorAt[c])) return (Sub[c] >> ((sy >> 3) * SubN + (sx >> 3)) & 1) != 0;   // 닫힌 문은 벽
                if (axis == 0) return false;
                int along = axis == 1 ? sx : sy;
                return along < 4 || along >= 28 || along < 4 + (1 - open) * 24;   // 열린 문: 양옆 기둥 12.5cm · 닫혀 오는 문짝 판 (낮은 쪽 기둥에서 — 끝 = 4 + (1 − 열림) × 24도트)
            }
            case CellKind.Floor:
            {
                int fu = L.FurnitureAt[c];
                if (fu < 0 || fu >= FurnPlaced.Length || FurnPlaced[fu] == 0) return false;
                var (fm, ox, oy) = FurnForm(fu);
                if (fm == null) return false;
                int x = dx - ox, y = dy - oy;
                if (!fm.Solid(x, y)) return false;
                mat = fm.Mat[y * fm.W + x]; furn = true;
                return true;
            }
            default:
                return false;
        }
    }

    private bool Solid(int dx, int dy) => SolidDot(dx, dy, out _, out _);

    /// <summary>
    /// 틈새 지도: 칸마다 막힌 칸 (벽 · 문 · 제자리 가구 · 도면 끝 밖)까지 체비셰프 거리 — 그 칸 안 어느 점이든 막힌 것에서 (거리 − 1)m 넘게 떨어져 있다.
    /// 물을 때 그 칸만 고리를 넓혀 가며 센다 (깨어 있는 물체가 지나는 칸만 · 온 배를 훑지 않는다 · 작은 자리표 — 온 배 크기 배열을 첫 깨움에 만들지 않는다). 제자리 가구가 바뀌면 다 잊는다 (도면 · 문 칸은 그대로다).
    /// 찍지 않는다 — 찍힌 상태의 순수 함수라 되살린 세계와 같다.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void BuildClearance()
    {
        if (_clearFurn.Length != FurnPlaced.Length || !_clearFurn.AsSpan().SequenceEqual(FurnPlaced))
        {
            Array.Fill(_clearKey, -1);
            if (_clearFurn.Length != FurnPlaced.Length) _clearFurn = new byte[FurnPlaced.Length];
            FurnPlaced.AsSpan().CopyTo(_clearFurn);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool Blocked(int x, int y)
    {
        if (!L.In(x, y)) return true;
        int c = L.Index(x, y), fu = L.FurnitureAt[c];
        var k = L.Kind[c];
        return k is CellKind.Wall or CellKind.Door || k == CellKind.Floor && fu >= 0 && fu < FurnPlaced.Length && FurnPlaced[fu] != 0;
    }

    /// <summary>그 칸 안 어느 점이든 막힌 것까지 적어도 이만큼 (m).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private double ClearAt(int c)
    {
        if (c < 0) return 0;
        int k = c & (ClearSlots - 1);
        if (_clearKey[k] != c)
        {
            int x = L.X(c), y = L.Y(c), d = 0;
            if (!Blocked(x, y))
                for (d = 1; ; d++)
                {
                    bool hit = false;
                    for (int i = -d; i <= d && !hit; i++) hit = Blocked(x + i, y - d) || Blocked(x + i, y + d) || Blocked(x - d, y + i) || Blocked(x + d, y + i);
                    if (hit) break;
                }
            _clearKey[k] = c; _clear[k] = d;
        }
        return Math.Max(0, _clear[k] - 1);
    }

    /// <summary>
    /// 한 점이 이번 잔걸음에 (d만큼) 갈 길에서 처음 막힌 도트: 떨어진 거리 (법선 쪽 m — 이미 들어가 있으면 −깊이) · 법선 (막힌 것에서 밖으로) · 재질.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool Sweep(double px, double py, double ddx, double ddy, out double sep, out double nx, out double ny, out byte mat, out int cell)
    {
        const double S = 32;
        double Px = px * S, Py = py * S, Dx = ddx * S, Dy = ddy * S;
        int ix = (int)Math.Floor(Px), iy = (int)Math.Floor(Py);
        sep = 0; nx = 0; ny = -1; cell = -1;
        if (SolidDot(ix, iy, out mat, out _))
        {
            cell = L.In(ix >> 5, iy >> 5) ? L.Index(ix >> 5, iy >> 5) : -1;
            // 이미 들어가 있다: 법선은 빈 이웃 쪽 기울기 (없으면 온 쪽) · 깊이는 그 쪽으로 빠져나갈 때까지
            (nx, ny) = EmptySide(ix, iy);
            if (nx == 0 && ny == 0) { double l = Math.Sqrt(Dx * Dx + Dy * Dy); if (l > 0) { nx = -Dx / l; ny = -Dy / l; } else ny = -1; }
            bool alongX = Math.Abs(nx) >= Math.Abs(ny);
            int st = alongX ? Math.Sign(nx) : Math.Sign(ny), k = 1;
            while (k < 8 && (alongX ? Solid(ix + k * st, iy) : Solid(ix, iy + k * st))) k++;
            double face = st > 0 ? (alongX ? ix : iy) + k : (alongX ? ix : iy) - k + 1, at = alongX ? Px : Py;
            sep = -Math.Abs(face - at) / S;
            return true;
        }
        int sx = Math.Sign(Dx), sy = Math.Sign(Dy);
        double tMaxX = Dx > 0 ? (ix + 1 - Px) / Dx : Dx < 0 ? (Px - ix) / -Dx : double.PositiveInfinity;
        double tMaxY = Dy > 0 ? (iy + 1 - Py) / Dy : Dy < 0 ? (Py - iy) / -Dy : double.PositiveInfinity;
        double tdx = Dx != 0 ? 1 / Math.Abs(Dx) : double.PositiveInfinity, tdy = Dy != 0 ? 1 / Math.Abs(Dy) : double.PositiveInfinity;
        for (int it = 0; it < 256; it++)
        {
            double tt; bool xAxis;
            if (tMaxX < tMaxY) { if (tMaxX > 1) break; ix += sx; tt = tMaxX; tMaxX += tdx; xAxis = true; }
            else { if (tMaxY > 1) break; iy += sy; tt = tMaxY; tMaxY += tdy; xAxis = false; }
            if (!SolidDot(ix, iy, out mat, out bool furn)) continue;
            cell = L.In(ix >> 5, iy >> 5) ? L.Index(ix >> 5, iy >> 5) : -1;
            double fx = xAxis ? -sx : 0, fy = xAxis ? 0 : -sy;   // 들어간 면
            nx = fx; ny = fy;
            if (furn)
            {
                var (gx, gy) = EmptySide(ix, iy);   // 가구 기둥: 8이웃의 빈 쪽 기울기 (둥근 면이 계단으로 보이지 않게)
                if (gx * fx + gy * fy > 0.3) { nx = gx; ny = gy; }
            }
            sep = Math.Max(0, -tt * (Dx * nx + Dy * ny)) / S;
            return true;
        }
        mat = 0;
        return false;
    }

    /// <summary>막힌 도트의 빈 이웃 쪽 (8이웃 합 — 단위 벡터 · 다 막혔으면 0).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private (double x, double y) EmptySide(int ix, int iy)
    {
        double gx = 0, gy = 0;
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
                if ((ox != 0 || oy != 0) && !Solid(ix + ox, iy + oy)) { gx += ox; gy += oy; }
        double l = Math.Sqrt(gx * gx + gy * gy);
        return l > 0 ? (gx / l, gy / l) : (0, 0);
    }

    /// <summary>이번 잔걸음 h에 닿을 곳 (사분면마다 가장 가까운 표본 — 많아야 4 · 지금 빠르기로 갈 길을 따라간다): 개수.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int FindContacts(in Prop p, (double[] x, double[] y, double reach) sm, double h, double muObj, double restObj)
    {
        double c = Math.Cos(p.Th), s = Math.Sin(p.Th);
        Span<int> best = stackalloc int[MaxContacts];
        Span<double> bestSep = stackalloc double[MaxContacts];
        Span<double> bnx = stackalloc double[MaxContacts], bny = stackalloc double[MaxContacts];
        Span<byte> bmat = stackalloc byte[MaxContacts];
        Span<int> bcell = stackalloc int[MaxContacts];
        best.Fill(-1);
        for (int i = 0; i < sm.x.Length; i++)
        {
            double rx = c * sm.x[i] - s * sm.y[i], ry = s * sm.x[i] + c * sm.y[i];
            double vx = p.Vx - p.W * ry, vy = p.Vy + p.W * rx;
            if (!Sweep(p.X + rx, p.Y + ry, vx * h, vy * h, out double sep, out double nx, out double ny, out byte mat, out int cell)) continue;
            int q = (sm.x[i] >= 0 ? 1 : 0) | (sm.y[i] >= 0 ? 2 : 0);
            if (best[q] >= 0 && sep >= bestSep[q]) continue;
            best[q] = i; bestSep[q] = sep; bnx[q] = nx; bny[q] = ny; bmat[q] = mat; bcell[q] = cell;
        }
        int n = 0;
        double m = p.Kg, I = p.Inertia;
        for (int q = 0; q < MaxContacts; q++)
        {
            if (best[q] < 0) continue;
            int i = best[q];
            double rx = c * sm.x[i] - s * sm.y[i], ry = s * sm.x[i] + c * sm.y[i], nx = bnx[q], ny = bny[q];
            var wall = T.Materials[bmat[q]].Def;
            double rn = rx * ny - ry * nx, rt = rx * nx + ry * ny;   // r × n · r × t (t = (−ny, nx))
            _propHits[n++] = new PropContact { Rx = rx, Ry = ry, Nx = nx, Ny = ny, Sep = bestSep[q], Mu = Math.Sqrt(muObj * wall.Mu), E = Math.Min(restObj, wall.Rest), Cell = bcell[q], Quad = q,
                Kn = 1 / m + rn * rn / I, Kt = 1 / m + rt * rt / I };   // 목표 (반발 · 다가옴)는 마찰 뒤 빠르기로 부르는 이가 정한다
        }
        return n;
    }

    /// <summary>순차 충격량 (Catto — 되풀이마다 마찰 먼저 (지난 되풀이의 jn으로 한계) → 법선 누적 jn ≥ 0) — 되풀이 iters번.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void SolveContacts(ref Prop p, int n, int iters)
    {
        double m = p.Kg, I = p.Inertia;
        for (int it = 0; it < iters; it++)
            for (int k = 0; k < n; k++)
            {
                ref var ct = ref _propHits[k];
                double tx = -ct.Ny, ty = ct.Nx;
                double vx = p.Vx - p.W * ct.Ry, vy = p.Vy + p.W * ct.Rx;
                double vt = vx * tx + vy * ty, lim = ct.Mu * ct.Jn;
                double jt = Math.Clamp(ct.Jt - vt / ct.Kt, -lim, lim), djt = jt - ct.Jt;
                ct.Jt = jt;
                p.Vx += djt * tx / m; p.Vy += djt * ty / m; p.W += djt * (ct.Rx * ty - ct.Ry * tx) / I;
                vx = p.Vx - p.W * ct.Ry; vy = p.Vy + p.W * ct.Rx;
                double vn = vx * ct.Nx + vy * ct.Ny;
                double jn = Math.Max(0, ct.Jn + (ct.Target - vn) / ct.Kn), dj = jn - ct.Jn;
                ct.Jn = jn;
                p.Vx += dj * ct.Nx / m; p.Vy += dj * ct.Ny / m; p.W += dj * (ct.Rx * ct.Ny - ct.Ry * ct.Nx) / I;
            }
    }

    /// <summary>자리 바로잡음: 겹침이 0.5도트 넘는 몫의 20%를 법선 쪽으로 (빠르기는 그대로 — 밀어 떼지 않는다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void FixOverlap(ref Prop p, int n)
    {
        double sx = 0, sy = 0;
        for (int k = 0; k < n; k++)
        {
            ref var ct = ref _propHits[k];
            if (ct.Sep >= -Slop) continue;
            double d = FixShare * (-ct.Sep - Slop);
            if (Math.Abs(ct.Nx * d) > Math.Abs(sx)) sx = ct.Nx * d;
            if (Math.Abs(ct.Ny * d) > Math.Abs(sy)) sy = ct.Ny * d;
        }
        if (sx == 0 && sy == 0) return;
        FixL += p.Kg * (sx * p.Vy - sy * p.Vx);
        p.X += sx; p.Y += sy;
    }
}
