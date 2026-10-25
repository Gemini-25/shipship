namespace ShipEngine;

/// <summary>
/// ④ 걷는 이 · 놓기 · 밟기 (docs/PHYSICS.md §7 · PH6b): Actors는 열어 보지 않는 꼬리표의 원판 (R 0.25m) — ⑧이 지난 틱 끝에 알린 길 (그 틱에 지나간 칸 ≤ 8)을
/// 같은 걸음 시간표 (칸마다 같은 때 — 화면의 걸음 메우기와 같다 · 한 틱 늦다)로 따라간다. 물체만 충격량을 받는다 (반발 e 0.2 · 미는 힘 한도 150N · 발 마찰 √(μ × 0.6)) —
/// 걷던 이가 바닥 상자를 차서 밀고 · 날아든 것은 선 이 · 누운 이에게도 튄다. 원판은 밀려나지 않고 · 겹친 것을 자리로 떼지 않는다 (발밑의 것은 밟고 선다 — 다가올 때만 민다).
/// 놓은 이 (Dropper)와는 놓은 틱부터 두 틱 동안 닿지 않는다 (놓고 걸어 나가는 걸음을 ④는 한 틱 늦게 따라간다) (놓자마자 차지 않게) · 집으려는 이는 그것을 차지 않는다 (차며 도는 고리가 없게). 놓는 자리는 원판 바깥이다 (0.25m + 닿는 반지름 + 1도트 · 걷던 쪽 앞 → 오른쪽 → 왼쪽 → 뒤 · 막혔으면 다음 쪽).
/// </summary>
public sealed partial class Structure
{
    public const double ActorRest = 0.2, ActorPushN = 150, FootMu = 0.6;

    private int[] _actorPath = new int[64];   // 원판마다 길 (Path[PathAt .. PathAt + PathN) — 찍는다: ⑧이 틱 끝에 쓴 것을 ④가 다음 틱에 읽는다)
    private int _nActorPath;

    /// <summary>원판의 τ 때 자리 · 빠르기 (도면 m · m/s — 길의 칸을 같은 때로 · 길이 없으면 제 칸 가운데에 선다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void ActorAt(in PropActor a, double tau, double dt, out double x, out double y, out double vx, out double vy)
    {
        int n = a.PathN;
        if (n < 2) { x = L.X(a.Cell) + 0.5; y = L.Y(a.Cell) + 0.5; vx = vy = 0; return; }
        double s = Math.Clamp(tau / dt, 0, 1) * (n - 1);
        int i = Math.Min((int)s, n - 2);
        double f = s - i;
        int c0 = _actorPath[a.PathAt + i], c1 = _actorPath[a.PathAt + i + 1];
        double x0 = L.X(c0) + 0.5, y0 = L.Y(c0) + 0.5, x1 = L.X(c1) + 0.5, y1 = L.Y(c1) + 0.5;
        x = x0 + (x1 - x0) * f; y = y0 + (y1 - y0) * f;
        vx = (x1 - x0) * (n - 1) / dt; vy = (y1 - y0) * (n - 1) / dt;
    }

    /// <summary>그 원판과 닿지 않는가: 놓은 이 (놓은 틱부터 두 틱 — 놓고 걸어 나간 걸음을 ④가 다음 틱에 따라간다 · Dropper는 꼬리표 + 1 · 로봇은 음수 그대로 · 0 없음) · 집으려는 이 (곁에서 손을 뻗는다 — 차지 않는다).</summary>
    private bool DropperOf(in Prop p, in PropActor a) => a.Target != 0 && a.Target == p.Id || p.Dropper != 0 && p.Dropper == (a.Tag >= 0 ? a.Tag + 1 : a.Tag) && _now - p.Since <= 2;

    /// <summary>원판의 길 (지금 · 지나간 칸 가운데)에서 그 점까지 가장 가까운 거리 (m).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private double PathDistance(in PropActor a, double x, double y)
    {
        if (a.PathN < 2) { double dx = L.X(a.Cell) + 0.5 - x, dy = L.Y(a.Cell) + 0.5 - y; return Math.Sqrt(dx * dx + dy * dy); }
        double best = double.PositiveInfinity;
        for (int i = 0; i + 1 < a.PathN; i++)
        {
            int c0 = _actorPath[a.PathAt + i], c1 = _actorPath[a.PathAt + i + 1];
            double x0 = L.X(c0) + 0.5, y0 = L.Y(c0) + 0.5, sx = L.X(c1) + 0.5 - x0, sy = L.Y(c1) + 0.5 - y0, l2 = sx * sx + sy * sy;
            double t = l2 > 0 ? Math.Clamp(((x - x0) * sx + (y - y0) * sy) / l2, 0, 1) : 0, dx = x0 + t * sx - x, dy = y0 + t * sy - y;
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
        }
        return best;
    }

    /// <summary>
    /// 원판 (가운데 c · 반지름 r)과 물체 껍질 사이: 떨어진 거리 (겹치면 −깊이) · 법선 (원판에서 물체 쪽) · 맞은 점 (껍질 위 점과 원판 겉의 가운데).
    /// 가운데가 껍질 밖이면 가장 가까운 변의 점 · 안이면 가장 덜 겹친 변 쪽으로.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static double DiscSeparation(in Prop p, PropShape sh, double cx, double cy, double r, out double nx, out double ny, out double px, out double py)
    {
        nx = 0; ny = -1; px = cx; py = cy;
        int n = Math.Min(MaxHull, sh.HullX.Length);
        if (n == 0) return double.PositiveInfinity;
        Span<double> hx = stackalloc double[MaxHull], hy = stackalloc double[MaxHull], hnx = stackalloc double[MaxHull], hny = stackalloc double[MaxHull];
        WorldHull(p, sh, hx, hy, hnx, hny);
        double maxD = double.NegativeInfinity;
        int e = 0;
        for (int i = 0; i < n; i++)
        {
            if (hnx[i] == 0 && hny[i] == 0) continue;
            double d = hnx[i] * (cx - hx[i]) + hny[i] * (cy - hy[i]);
            if (d > maxD) { maxD = d; e = i; }
        }
        if (maxD <= 0)
        {
            // 원판 가운데가 껍질 안: 가장 덜 겹친 변으로 나간다 — 물체는 그 반대로
            double qx = cx - hnx[e] * maxD, qy = cy - hny[e] * maxD;
            nx = -hnx[e]; ny = -hny[e];
            px = 0.5 * (qx + cx + nx * r); py = 0.5 * (qy + cy + ny * r);
            return maxD - r;
        }
        double best = double.PositiveInfinity, bx = cx, by = cy;
        for (int i = 0; i < n; i++)
        {
            double x0 = hx[i], y0 = hy[i], sx = hx[(i + 1) % n] - x0, sy = hy[(i + 1) % n] - y0, l2 = sx * sx + sy * sy;
            double t = l2 > 0 ? Math.Clamp(((cx - x0) * sx + (cy - y0) * sy) / l2, 0, 1) : 0, qx = x0 + t * sx, qy = y0 + t * sy;
            double d2 = (qx - cx) * (qx - cx) + (qy - cy) * (qy - cy);
            if (d2 < best) { best = d2; bx = qx; by = qy; }
        }
        double dist = Math.Sqrt(best);
        if (dist > 0) { nx = (bx - cx) / dist; ny = (by - cy) / dist; }
        px = 0.5 * (bx + cx + nx * r); py = 0.5 * (by + cy + ny * r);
        return dist - r;
    }

    /// <summary>
    /// 깨우기 — 걷는 이 (§4-1): 이번 틱 길을 걷는 원판마다 그 길 (칸 가운데를 이은 줄 ± 반지름)에 닿을 만한 잠든 것 (가운데에서 닿는 반지름 + 2도트 안)을 깨운다 —
    /// 선 이 · 누운 이는 깨우지 않는다 (발밑의 것은 밟고 선다). 놓은 이는 놓은 것을 깨우지 않는다.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool WakeByActors(Span<Prop> all)
    {
        bool moving = false;
        for (int a = 0; a < _nActors && !moving; a++) moving = _actors[a].PathN >= 2;
        if (!moving) return false;
        if (_propHead.Length != L.N) _propHead = new int[L.N];
        if (_propNext.Length < all.Length) _propNext = new int[all.Length + 16];
        Array.Fill(_propHead, -1);
        for (int i = all.Length - 1; i >= 0; i--)
        {
            if (all[i].State != 0 || !Collides(all[i])) continue;
            int c = CellOf(all[i]);
            if (c < 0) continue;
            _propNext[i] = _propHead[c]; _propHead[c] = i;
        }
        bool woke = false;
        for (int a = 0; a < _nActors; a++)
        {
            ref var ac = ref _actors[a];
            if (ac.PathN < 2) continue;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            for (int q = 0; q < ac.PathN; q++) { int c = _actorPath[ac.PathAt + q]; x0 = Math.Min(x0, L.X(c)); y0 = Math.Min(y0, L.Y(c)); x1 = Math.Max(x1, L.X(c)); y1 = Math.Max(y1, L.Y(c)); }
            x0 = Math.Max(0, x0 - 1); y0 = Math.Max(0, y0 - 1); x1 = Math.Min(L.W - 1, x1 + 1); y1 = Math.Min(L.H - 1, y1 + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    for (int i = _propHead[L.Index(x, y)]; i >= 0; i = _propNext[i])
                    {
                        ref var p = ref all[i];
                        if (p.State != 0 || DropperOf(p, ac)) continue;
                        if (PathDistance(ac, p.X, p.Y) > ac.R + SamplesOf(PropForm(p)).reach + 2 * DotForm.Dot) continue;
                        if (Wake(ref p, p.Cause)) { woke = true; PropWoken++; }
                    }
        }
        return woke;
    }

    /// <summary>
    /// 요청 (⑧ — 내려놓기 · 쓰러지며 떨굼 · 벗어 든 것): 물건을 원판 바깥에 놓는다 — 놓은 이의 자리 (x, y) · 걷던 빠르기 (vx, vy — 그대로 미끄러져 간다) ·
    /// 앞 쪽 (걷던 쪽 · 서 있었으면 face) → 오른쪽 → 왼쪽 → 뒤 가운데 벽 · 가구 · 닫힌 문에 걸리지 않는 첫 자리 (0.25m + 닿는 반지름 + 1도트 · 모두 막혔으면 제자리) ·
    /// 놓은 이 (꼬리표)와는 놓은 틱부터 두 틱 동안 닿지 않는다. 높이 z: 0이면 내려놓기 (그 자리 받침 — 탁자 위면 윗면) · 아니면 그 높이에서 놓친다 (손 1m — 걷던 빠르기로 날아 떨어진다 · PH7). 물체 Id (못 놓으면 −1).
    /// </summary>
    public int DropItem(int item, float charge, double x, double y, float z, double vx, double vy, int cause, int tag, double faceX = 0, double faceY = 1)
    {
        int cell = x >= 0 && y >= 0 && x < L.W && y < L.H ? L.Index((int)x, (int)y) : -1;
        int id = PutItem(item, charge, cell);
        if (id < 0) return -1;
        int i = IndexOf(id);
        var p = Props[i];
        var fm = PropForm(p);
        double v = Math.Sqrt(vx * vx + vy * vy), fx = v > 1e-9 ? vx / v : faceX, fy = v > 1e-9 ? vy / v : faceY, fl = Math.Sqrt(fx * fx + fy * fy);
        if (fl > 0) { fx /= fl; fy /= fl; } else { fx = 0; fy = 1; }
        double d = ActorR + fm.Shape.Hit.reach + DotForm.Dot, hm = HeightOf(fm);
        Span<double> dirs = stackalloc[] { fx, fy, -fy, fx, fy, -fx, -fx, -fy };   // 앞 · 오른쪽 · 왼쪽 · 뒤 (도면 y는 남쪽 — 앞을 시계로 돌린 쪽이 오른쪽)
        double px = x, py = y;
        for (int k = 0; k < 4; k++)
        {
            double qx = x + dirs[2 * k] * d, qy = y + dirs[2 * k + 1] * d;
            double zq = z > 0 ? z : RestHeight(qx, qy);   // 내려놓기는 그 아래 받침 (탁자 위면 윗면) · 떨굼은 손 높이
            _zLo = zq; _zHi = zq + hm;
            bool fits = FitsAt(fm, qx, qy);
            FullHeight();
            if (!fits) continue;
            px = qx; py = qy;
            break;
        }
        double zs = SupportAt(px, py, Math.Max(z, HandZ), out int on, out _);
        p.X = px; p.Y = py; p.Dropper = tag >= 0 ? tag + 1 : tag; p.Since = _now; p.Cause = cause;
        p.Z = (float)(z > 0 ? Math.Max(z, zs) : zs); p.On = on; p.Vz = 0;
        if (v > 1e-9 || p.Z > zs + ZEps)
        {
            if (p.Kg <= 0) FillBody(ref p);
            p.Vx = vx; p.Vy = vy; p.State = (byte)(p.Z > zs + ZEps ? 3 : 1); p.Asleep = 0; p.Still = 0;
        }
        Props[i] = p;
        return id;
    }

    /// <summary>그 꼴이 그 자리 (선 자세)에 놓일 수 있나: 껍질 꼭짓점 · 테두리 표본 · 가운데가 막힌 도트 (벽 · 닫힌 문 · 제자리 가구 · 도면 밖)에 들지 않는다.</summary>
    private bool FitsAt(DotForm fm, double x, double y)
    {
        var sm = fm.Shape.Hit;
        if (SolidDot((int)Math.Floor(x * 32), (int)Math.Floor(y * 32), out _, out _)) return false;
        for (int k = 0; k < sm.x.Length; k++)
            if (SolidDot((int)Math.Floor((x + sm.x[k]) * 32), (int)Math.Floor((y + sm.y[k]) * 32), out _, out _)) return false;
        return true;
    }

    private void SaveActors(StateWriter w)
    {
        w.Arr(_actors.AsSpan(0, _nActors).ToArray()); w.Arr(_actorPath.AsSpan(0, _nActorPath).ToArray());
    }

    private void LoadActors(StateReader r)
    {
        var a = r.Arr<PropActor>(); var path = r.Arr<int>();
        if (_actors.Length < a.Length) _actors = new PropActor[a.Length * 2];
        a.CopyTo(_actors, 0); _nActors = a.Length;
        if (_actorPath.Length < path.Length) _actorPath = new int[path.Length * 2];
        path.CopyTo(_actorPath, 0); _nActorPath = path.Length;
    }
}
