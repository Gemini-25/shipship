namespace ShipEngine;

/// <summary>
/// ④ 물체끼리 (docs/PHYSICS.md §3-5 · §4-1 · PH6a): 칸 통 · 쓸린 상자 섬 · 껍질 SAT + 기준면 자르기 (닿는 점 ≤ 2 — Box2D Lite 꼴 · 두 쪽이 거의 같으면 두 기준의 가운데: 거울 물체는 거울 그대로) ·
/// 순차 충격량 (Catto 2006 — 섬의 닿음을 한꺼번에: 바닥 · 벽 · 가구와 물체끼리 · 차례 (작은 Id, 큰 Id, 점 번호) · 되풀이 4번 · 물체끼리 닿으면 8번 · 틱 첫 잔걸음 · 부딪는 잔걸음 16번 ·
/// 물체끼리 닿은 것은 바닥 마찰도 같은 고리에서 — 눌린 것끼리 비트는 몫을 정지 마찰이 붙든다) ·
/// 따뜻한 시작 (닿음 캐시 — 짝은 (작은 Id, 큰 Id, 변 · 점) · 더미의 벽 닿음은 (Id, 0, 표본 사분면) 열쇠 · 잔걸음 길이 비로 줄여 쓴다 · 섬마다 · 틱마다 비운다: 찍지 않아도 되살린 세계와 같다) ·
/// split impulse (겹침이 0.5도트 넘는 몫의 20%를 질량비로 자리만 옮긴다 — 빠르기 장부는 그대로 · 바뀐 각운동량은 FixL) · 반발은 다가오는 빠르기 (바닥 마찰 뒤) 0.5m/s 넘을 때만 (e = 둘 가운데 작은 것 · μ = √곱).
/// 섬: 깨어 있는 것의 쓸린 상자 (지금 ∪ v dt 뒤 + ½ a dt² + 덜컹 + 2도트)에 닿는 것 — 잠든 것은 '쉬는 짝'으로 들어와 맞으면 그 잔걸음에 깬다 (바닥 마찰 한도 안의 밂은 바닥이 받는다) ·
/// 맞아 빨라질 몫 (섬의 가장 큰 빠르기의 두 배 × dt)만큼 넓혀 만나는 깨어 있는 섬끼리 한 섬 · 걷다가 살핀 곳을 벗어나면 섬 안 · 아무 섬에도 들지 않은 잠든 것 가운데 새 짝을 찾는다.
/// 같은 몸에 실린 것끼리만 닿는다 (다른 몸의 물체끼리는 PH11). 섬마다 같은 잔걸음을 함께 걷고 · 섬 차례는 가장 작은 Id다. 혼자인 섬은 물체 하나의 걸음과 같다 (PH4 그대로 — 바이트까지).
/// </summary>
public sealed partial class Structure
{
    public const double MaxPileH = 0.05;   // 물체끼리 닿을 만한 섬의 잔걸음 상한 (초 — 2.4초 틱에 48걸음)
    private const int BaseIters = 4, PileIters = 8, FirstIters = 16, MaxHull = 16;

    /// <summary>시험 스위치: 거짓이면 물체끼리 닿지 않는다 (섬은 늘 혼자 — PH4 걸음과 바이트까지 같은지 보는 데만).</summary>
    internal static bool PairsOn = true;

    /// <summary>
    /// 물체 셈의 자세함 (docs/PHYSICS.md §4-2): 1 온전 · 0 가벼움 (주컴퓨터 상상 복제 — 섬마다 잔걸음 4 · 물체끼리 · 원판 닿음 · 넘어짐 끔 · 깨짐은 PH8이 따른다).
    /// 찍지 않는다 — 복제를 만드는 코드가 늘 정한다 (<see cref="ShipMind.Imagined"/> · 벽시계로 정하지 않는다: 1코어 = 4코어 · 다시보기 · 되살린 세계는 온전 그대로).
    /// </summary>
    public int PropDetail = 1;

    /// <summary>물체끼리 닿는가 (시험 스위치 · 가벼운 셈이면 끈다 — 섬은 늘 혼자).</summary>
    private bool Pairs => PairsOn && PropDetail > 0;

    /// <summary>섬을 지을 때 찾은 짝 수 (틱마다 더한다 · 찍지 않는다 — 보기용: 쓸린 상자가 넓을수록 는다).</summary>
    public long IslandPairs;

    // 칸 통 · 섬 (찍지 않는다 — 깨어 있는 것이 있는 틱마다 다시 짓는다)
    private int[] _propHead = Array.Empty<int>(), _propNext = Array.Empty<int>(), _isl = Array.Empty<int>(), _islAt = Array.Empty<int>(), _mem = Array.Empty<int>();
    private byte[] _role = Array.Empty<byte>();          // 0 섬 밖 · 1 틱 처음에 깨어 있었다 · 2 쉬는 짝 (잠든 채 들어왔다)
    private double[] _box = Array.Empty<double>();       // 물체마다 상자 (minX, minY, maxX, maxY — 깨어 있으면 쓸린 상자)
    private double[] _u = Array.Empty<double>(), _uRoot = Array.Empty<double>();   // 깨어 있는 것의 빠르기 잣대 · 섬마다 가장 큰 것
    private readonly List<int> _queue = new();
    private int[] _cand = Array.Empty<int>();             // 상자에 든 물체 (Gather)
    private readonly List<long> _pairs = new();          // 섬 · 작은 차례 · 큰 차례 (섬 차례대로 늘어선다)
    private double _maxReach = -1;                       // 물건 꼴 (누움 다섯)의 가장 먼 꼭짓점 (칸 통을 훑는 넓이)

    private struct PropRun
    {
        public int I, Host, Steps, Jp, S0, Ns, Na;   // 정적 닿음 [S0, S0 + Ns) 뒤에 원판 닿음 Na
        public DotForm Fm;
        public PropShape Sh, Hs;   // Hs · Reach: 섬에 든 때의 껍질 · 닿는 반지름 (쉬는 짝도 — 짝 셈이 잔걸음마다 꼴을 다시 찾지 않게)
        public double Reach;
        public MaterialDef Mat;
        public (double[] x, double[] y, double reach) Sm;
        public double M, In, Rho, Jw, LastKey, NextJolt, H, FloorL, Ffx, Ffy, Ffw, Tvx, Tvy, Tw;   // 바닥 마찰: 이 잔걸음 한도 μk m g' h · 쌓인 충격량 (운동량 둘 · 돎) · 닿음 목표를 정하는 빠르기 (마찰 뒤)
        public bool Active, Done, Velo, Paired, Kicked, Walked;   // Kicked: 이 틱 원판에 차였다 (원인 마디를 한 번만) · Walked: 곁을 걷는 원판이 있다 (이 틱엔 자지 않는다)
        // 잔걸음 셈값
        public double PreX, PreY, PreTh, PreVx, PreVy, PreW, Gp, MuK, Room, Speed, Fx, Fy, Tq, Wfx, Wfy, Wtq, Wb, Ax, Ay, Al, Acc, Px0, Py0, L0, Lw, StopAt;
        public bool Windy, Framed, Blown;
        // 높이 (PH7): 떠 있음 (떨어지는 중 · 무중력) · 이 잔걸음이 닿는 때로 끝난다 · 받침 높이 · 받침 · 재질 · 수직 중력 · 넘어지는 힘 (그 쪽 몫 · 높이) · 구름 (축)
        public bool Air, Roll;
        public int On;
        public byte ZMat;
        public double Gz, Zs, Hz, TipFa, TipHf, RAx, RAy, Radius;
        public int Cell;
        // 맞음 (PH8): 이 잔걸음에 잃은 에너지 (제 몫) · 가장 센 닿음의 충격량 · 다가온 빠르기 · 팔 · 상대 재질 · 어떻게 / 이 틱 가장 빨리 미끄러진 빠르기² · 그 때
        public double HitE, HitJ, HitV, HitRx, HitRy, SlideV2;
        public byte HitMat, HitHow, SlideSub;
    }

    private struct IslandContact
    {
        public int A, B;   // 섬 안 차례 (B < 0: 바닥 · 벽 · 가구)
        public double Rx, Ry, Qx, Qy, Nx, Ny, Sep, Mu, E, Kn, Kt, Target, Jn, Jt;   // R: A 쪽 팔 · Q: B 쪽 팔 · 법선은 B에서 A 쪽
        public double Ux, Uy, Cap;   // B < 0 쪽의 빠르기 (걷는 원판 — 벽 · 가구는 0) · 법선 충격량 한도 (원판: 150N × h — 벽 · 가구는 끝없음)
        public double Vn0;           // 닿기 직전 법선 빠르기 (다가오면 음수 — 잃은 에너지 ½ Jn (−vn₀)(1 − e) · PH8)
        public int Cell, Key;
        public byte Mat;             // B < 0 쪽 재질 (벽 · 가구 기둥 — 깨짐의 단단함 몫)
        public bool Bounce;
    }

    private PropRun[] _runs = new PropRun[8];
    private int[] _runNear = new int[32];
    private bool[] _runTouch = new bool[32];
    private IslandContact[] _ic = new IslandContact[64], _icStage = new IslandContact[64];
    private int _nic;
    // 따뜻한 시작 캐시 (지난 잔걸음의 닿음 열쇠 · 충격량 — 같은 차례로 다시 나오니 그 자리부터 본다 · 찍지 않는다)
    private long[] _wkKey = new long[64], _wkKeyNext = new long[64];
    private double[] _wkJn = new double[64], _wkJt = new double[64], _wkJnNext = new double[64], _wkJtNext = new double[64];
    private int _wkN;
    private double _warmH;

    /// <summary>물체끼리 닿을 수 있나 (물건 · 조각 (PH8) · 놓임 · 미끄러짐 · 넘어짐 · 떨어짐 · 떠 있음 — 우주 · 막음 · 낌은 빼고).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static bool Collides(in Prop p) => p.Kind is 0 or 2 && p.State is 0 or 1 or 2 or 3 or 4;

    /// <summary>물건 꼴 (표의 물건마다 누움 다섯)의 가장 먼 꼭짓점 — 표의 순수 함수 (칸 통을 훑는 넓이 · 표마다 한 번 — 새 세계 · 복제가 다시 세지 않는다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private double MaxReach()
    {
        if (_maxReach >= 0) return _maxReach;
        var box = Volatile.Read(ref _reachOf);
        if (box == null || box.T != T)
        {
            double r = 0;
            foreach (var name in T.People.Items.Keys)
                for (int lie = 0; lie <= 4; lie++) r = Math.Max(r, DotForge.Make(T, name, lie: lie).Shape.Hit.reach);
            box = new ReachOf(T, r);
            Volatile.Write(ref _reachOf, box);
        }
        return _maxReach = box.R;
    }

    private sealed record ReachOf(Tables T, double R);
    private static ReachOf? _reachOf;

    /// <summary>
    /// 깨어 있는 것 (있는 틱에만): 칸 통 → 섬 (합집합 — 뿌리는 가장 작은 차례) → 섬마다 함께 걷는다 (차례는 뿌리) → 섬 놓침 (다른 섬 · 섬 밖 물체와 0.5도트 넘게 겹친 채 끝난 것).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void StepIslands(Sim sim, Span<Prop> all, double dt, bool world)
    {
        int n = all.Length;
        if (_isl.Length < n) { int m = n + 16; _isl = new int[m]; _islAt = new int[m]; _mem = new int[m]; _role = new byte[m]; _box = new double[4 * m]; _propNext = new int[m]; _u = new double[m]; _uRoot = new double[m]; _cand = new int[m]; }
        if (_propHead.Length != L.N) _propHead = new int[L.N];
        BuildIslands(all, dt, world);
        // 섬마다 차례 (작은 차례 먼저) — 뿌리마다 몇 · 어디서
        int roots = 0;
        for (int i = 0; i < n; i++) _islAt[i] = 0;
        for (int i = 0; i < n; i++) if (_role[i] != 0) _islAt[Find(i)]++;
        int at = 0;
        for (int i = 0; i < n; i++) if (_role[i] != 0 && _isl[i] == i) { int c = _islAt[i]; _islAt[i] = at; at += c; roots++; }
        for (int i = 0; i < n; i++) if (_role[i] != 0) _mem[_islAt[Find(i)]++] = i;   // 뒤: _islAt[뿌리] = 그 섬의 끝
        int pp = 0, start = 0;
        for (int r = 0; r < n && roots > 0; r++)
        {
            if (_role[r] == 0 || _isl[r] != r) continue;
            int end = _islAt[r], p0 = pp;
            while (pp < _pairs.Count && (int)(_pairs[pp] >> 42) == r) pp++;
            StepIsland(sim, all, start, end - start, p0, pp - p0, dt, world);
            start = end; roots--;
        }
        if (Pairs) CountMisses(all, world);
    }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int Find(int i)
    {
        while (_isl[i] != i) { _isl[i] = _isl[_isl[i]]; i = _isl[i]; }
        return i;
    }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void Union(int a, int b)
    {
        int ra = Find(a), rb = Find(b);
        if (ra < rb) _isl[rb] = ra; else if (rb < ra) _isl[ra] = rb;
    }

    /// <summary>
    /// 칸 통 (물체 가운데 칸마다) · 섬: 깨어 있는 것마다 쓸린 상자에 닿는 이웃 (같은 몸 — 잠든 것은 쉬는 짝으로) →
    /// 맞아 빨라질 몫 (섬마다 깨어 있는 것의 가장 큰 빠르기 u의 두 배 × dt만큼 깨어 있는 것의 상자를 넓혀 다른 섬의 깨어 있는 것 · 쉬는 짝과 만나면 한 섬 — 두 번까지).
    /// 걷다가 살핀 곳을 벗어나면 섬 안에서 다시 찾는다 (<see cref="Discover"/>). 짝은 (섬 뿌리 · 작은 차례 · 큰 차례)로 늘어놓는다.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void BuildIslands(Span<Prop> all, double dt, bool world)
    {
        int n = all.Length;
        Array.Fill(_propHead, -1);
        for (int i = n - 1; i >= 0; i--)
        {
            _isl[i] = i; _role[i] = 0;
            if (!Collides(all[i])) continue;
            int c = CellOf(all[i]);
            if (c < 0) continue;
            _propNext[i] = _propHead[c]; _propHead[c] = i;
        }
        _queue.Clear(); _pairs.Clear();
        for (int i = 0; i < n; i++)
        {
            if (!Awake(all[i])) continue;
            _role[i] = 1;
            if (Collides(all[i])) { _queue.Add(i); SweptBox(all[i], world ? _hostOf[i] : -1, dt, i); }
        }
        if (!Pairs || _queue.Count == 0) return;
        double pad = MaxReach() + 2 * DotForm.Dot;
        int actives = _queue.Count;
        for (int q = 0; q < actives; q++)
        {
            int i = _queue[q], hi = world ? _hostOf[i] : -1;
            int nc = Gather(all, _box[4 * i] - pad, _box[4 * i + 1] - pad, _box[4 * i + 2] + pad, _box[4 * i + 3] + pad);
            for (int c = 0; c < nc; c++)
            {
                int j = _cand[c];
                if (j == i || (world ? _hostOf[j] : -1) != hi) continue;
                if (_role[j] == 0)
                {
                    StaticBox(all[j], j);
                    if (!BoxesMeet(i, j)) continue;
                    _role[j] = 2;
                }
                else if (!BoxesMeet(i, j)) continue;
                Union(i, j);
                _pairs.Add(((long)Math.Min(i, j) << 21) | (long)Math.Max(i, j));
            }
        }
        // 맞아 빨라질 몫: 섬마다 가장 큰 u의 두 배 × dt (다른 섬과 만나면 한 섬 · 뿌리가 바뀌면 다시 한 번)
        for (int round = 0; round < 2; round++)
        {
            for (int q = 0; q < actives; q++) _uRoot[_queue[q]] = 0;
            for (int q = 0; q < actives; q++) { int i = _queue[q], r = Find(i); _uRoot[r] = Math.Max(_uRoot[r], _u[i]); }
            bool merged = false;
            for (int q = 0; q < actives; q++)
            {
                int i = _queue[q], hi = world ? _hostOf[i] : -1;
                double g = 2 * _uRoot[Find(i)] * dt;
                if (g <= 0) continue;
                double x0 = _box[4 * i] - g, y0 = _box[4 * i + 1] - g, x1 = _box[4 * i + 2] + g, y1 = _box[4 * i + 3] + g;
                int nc = Gather(all, x0 - pad, y0 - pad, x1 + pad, y1 + pad);
                for (int c = 0; c < nc; c++)
                {
                    int j = _cand[c];
                    if (j == i || _role[j] == 0 || (world ? _hostOf[j] : -1) != hi || Find(j) == Find(i)) continue;
                    double gj = _role[j] == 1 ? 2 * _uRoot[Find(j)] * dt : 0;
                    if (x0 > _box[4 * j + 2] + gj || _box[4 * j] - gj > x1 || y0 > _box[4 * j + 3] + gj || _box[4 * j + 1] - gj > y1) continue;
                    Union(i, j); merged = true;
                    _pairs.Add(((long)Math.Min(i, j) << 21) | (long)Math.Max(i, j));
                }
            }
            if (!merged) break;
        }
        var ps = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pairs);
        for (int k = 0; k < ps.Length; k++) ps[k] |= (long)Find((int)(ps[k] >> 21)) << 42;
        ShellSort(ps);
        int w = 0;
        for (int k = 0; k < ps.Length; k++) if (w == 0 || ps[k] != ps[w - 1]) ps[w++] = ps[k];
        _pairs.RemoveRange(w, _pairs.Count - w);
        IslandPairs += w;
    }

    /// <summary>셸 정렬 (Ciura 간격 — 짝은 수십 · 일반화 정렬을 처음 부를 때의 번역 없이).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static void ShellSort(Span<long> a)
    {
        ReadOnlySpan<int> gaps = stackalloc[] { 701, 301, 132, 57, 23, 10, 4, 1 };
        foreach (int g in gaps)
            for (int i = g; i < a.Length; i++)
            {
                long v = a[i];
                int j = i;
                for (; j >= g && a[j - g] > v; j -= g) a[j] = a[j - g];
                a[j] = v;
            }
    }

    /// <summary>
    /// 상자 (m)에 가운데가 든 물체 (닿을 수 있는 것) → _cand: 상자가 덮는 칸이 물체 수보다 많으면 물체를 차례로 · 아니면 칸 통 (어느 길이든 고르는 것은 같다 — 짝은 뒤에 정렬한다).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int Gather(Span<Prop> all, double x0, double y0, double x1, double y1)
    {
        Cells(x0, y0, x1, y1, out int cx0, out int cy0, out int cx1, out int cy1);
        int n = 0;
        if (cx1 < cx0 || cy1 < cy0) return 0;
        if ((long)(cx1 - cx0 + 1) * (cy1 - cy0 + 1) > all.Length)
        {
            for (int j = 0; j < all.Length; j++)
            {
                if (!Collides(all[j])) continue;
                double x = Math.Floor(all[j].X), y = Math.Floor(all[j].Y);
                if (x >= cx0 && x <= cx1 && y >= cy0 && y <= cy1) _cand[n++] = j;
            }
            return n;
        }
        for (int y = cy0; y <= cy1; y++)
            for (int x = cx0; x <= cx1; x++)
                for (int j = _propHead[L.Index(x, y)]; j >= 0; j = _propNext[j]) _cand[n++] = j;
        return n;
    }

    /// <summary>그 상자 (m)를 덮는 칸 (도면 안으로 자른다 — 칸 통을 훑는 넓이).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void Cells(double x0, double y0, double x1, double y1, out int cx0, out int cy0, out int cx1, out int cy1)
    {
        cx0 = Math.Max(0, (int)Math.Floor(x0)); cy0 = Math.Max(0, (int)Math.Floor(y0));
        cx1 = Math.Min(L.W - 1, (int)Math.Floor(x1)); cy1 = Math.Min(L.H - 1, (int)Math.Floor(y1));
    }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool BoxesMeet(int i, int j) =>
        _box[4 * i] <= _box[4 * j + 2] && _box[4 * j] <= _box[4 * i + 2] && _box[4 * i + 1] <= _box[4 * j + 3] && _box[4 * j + 1] <= _box[4 * i + 3];

    /// <summary>잠든 것의 상자: 지금 자리 ± (가장 먼 꼭짓점 + 2도트).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void StaticBox(in Prop p, int i)
    {
        double r = SamplesOf(PropForm(p)).reach + 2 * DotForm.Dot;
        _box[4 * i] = p.X - r; _box[4 * i + 1] = p.Y - r; _box[4 * i + 2] = p.X + r; _box[4 * i + 3] = p.Y + r;
    }

    /// <summary>
    /// 깨어 있는 것의 쓸린 상자: 지금 ∪ v dt 뒤 · 그 둘레 ½ a dt² (틱 처음 바람 · 틀의 힘의 두 배 — 바람은 틱 안에서 줄기만 한다) + 이 틱 그 몸의 덜컹 (Δv × 남은 때) + 가장 먼 꼭짓점 + 2도트.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void SweptBox(in Prop p, int host, double dt, int i)
    {
        var fm = PropForm(p);
        var (m, I) = MassOf(p);
        double reach = SamplesOf(fm).reach, a = 0;
        double wx = 0, wy = 0, wt = 0;
        if (_windy && WindForce(p, fm, 0, out wx, out wy, out wt)) a += Math.Sqrt(wx * wx + wy * wy) / m + Math.Abs(wt) / I * reach;
        // 놓인 채 (빠르기 0) 정지 타원이 바람 · 틀의 힘을 틱 처음 · 끝에 다 붙든다 → 힘으로는 가지 않는다 (바람은 틱 안에서 줄기만 한다 — 잔걸음 처음의 붙듦과 같은 판정 ·
        // 걷는 이에게 깨운 것이 섬 하나를 바람 몫만큼 넓혀 곁 물건을 다 쉬는 짝으로 끌지 않게 · 차이거나 맞아 움직이면 살핀 곳을 벗어날 때 새 짝을 찾는다)
        int c0 = CellOf(p);
        bool rest = p.Vx == 0 && p.Vy == 0 && p.W == 0 && c0 >= 0;
        double muS = rest ? StaticShare * MaterialOf(p).Mu * (F.Stored(T.sWater, c0) > WetMol ? WetShare : 1) : 0, nf = rest ? m * Gravity * F.Grav[c0] : 0;
        if (rest && a > 0 && !Holds(wx, wy, wt, muS, nf, fm.Rho)) rest = false;
        if (_framed)
            foreach (double tau in stackalloc[] { 0, dt })
                if (FrameForce(p, host, tau, 0, m, I, out double fx, out double fy, out double tq, out double fw))
                {
                    a = Math.Max(a, Math.Sqrt(fx * fx + fy * fy) / m + Math.Abs(tq) / I * reach + fw * fw * reach);
                    if (rest && !(Holds(wx + fx, wy + fy, wt + tq, muS, nf, fm.Rho) && Holds(fx, fy, tq, muS, nf, fm.Rho))) rest = false;   // 바람이 줄어 틀 힘만 남아도
                }
        if (rest) a = 0;
        double ext = reach + 2 * DotForm.Dot + a * dt * dt;   // ½ · 2a · dt²
        if (host >= 0)
            foreach (var j in _jolts)
                if (j.B == host)
                {
                    var o = Bodies[host];
                    double dx = p.X - o.Lx, dy = p.Y - o.Ly;
                    ext += (Math.Sqrt(j.Dvx * j.Dvx + j.Dvy * j.Dvy) + Math.Abs(j.Dw) * (Math.Sqrt(dx * dx + dy * dy) + reach)) * Math.Max(0, dt - j.T);
                }
        double ex = p.X + p.Vx * dt, ey = p.Y + p.Vy * dt;
        _u[i] = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy) + Math.Abs(p.W) * reach + (ext - reach - 2 * DotForm.Dot) / dt;   // 맞아 빨라질 몫의 잣대
        _box[4 * i] = Math.Min(p.X, ex) - ext; _box[4 * i + 1] = Math.Min(p.Y, ey) - ext; _box[4 * i + 2] = Math.Max(p.X, ex) + ext; _box[4 * i + 3] = Math.Max(p.Y, ey) + ext;
    }

    /// <summary>섬 놓침 (법칙 14): 이 틱 걸은 것이 다른 섬 · 섬 밖 물체 (같은 몸)와 0.5도트 넘게 겹친 채 끝났다 — 셈만 (다음 틱에 한 섬이 되어 바로잡는다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void CountMisses(Span<Prop> all, bool world)
    {
        double pad = MaxReach();
        for (int i = 0; i < all.Length; i++)
        {
            if (_role[i] == 0 || !Collides(all[i]) || !Awake(all[i])) continue;
            int hi = world ? _hostOf[i] : -1, ri = Find(i);
            double r = SamplesOf(PropForm(all[i])).reach + pad;
            int x0 = Math.Max(0, (int)Math.Floor(all[i].X - r)), y0 = Math.Max(0, (int)Math.Floor(all[i].Y - r));
            int x1 = Math.Min(L.W - 1, (int)Math.Floor(all[i].X + r)), y1 = Math.Min(L.H - 1, (int)Math.Floor(all[i].Y + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    for (int j = _propHead[L.Index(x, y)]; j >= 0; j = _propNext[j])
                    {
                        if (j == i || !Collides(all[j]) || (world ? _hostOf[j] : -1) != hi || _role[j] != 0 && Find(j) == ri) continue;
                        double dx = all[i].X - all[j].X, dy = all[i].Y - all[j].Y, rr = r - pad + SamplesOf(PropForm(all[j])).reach;
                        if (dx * dx + dy * dy < rr * rr && ZMeet(all[i], all[j]) && PairSeparation(all[i], all[j]) < -Slop) IslandMiss++;
                    }
        }
    }

    /// <summary>두 물체 껍질 사이 (m — SAT · 겹치면 −깊이 · 껍질이 없으면 +∞): 시험 · 놓침 셈이 읽는다.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    public double PairSeparation(in Prop a, in Prop b)
    {
        var sa = PropForm(a).Shape; var sb = PropForm(b).Shape;
        if (sa.HullX.Length == 0 || sb.HullX.Length == 0) return double.PositiveInfinity;
        Span<double> ax = stackalloc double[MaxHull], ay = stackalloc double[MaxHull], anx = stackalloc double[MaxHull], any = stackalloc double[MaxHull];
        Span<double> bx = stackalloc double[MaxHull], by = stackalloc double[MaxHull], bnx = stackalloc double[MaxHull], bny = stackalloc double[MaxHull];
        int na = WorldHull(a, sa, ax, ay, anx, any), nb = WorldHull(b, sb, bx, by, bnx, bny);
        return Math.Max(MaxSeparation(ax, ay, anx, any, na, bx, by, nb, double.PositiveInfinity, out _), MaxSeparation(bx, by, bnx, bny, nb, ax, ay, na, double.PositiveInfinity, out _));
    }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static int WorldHull(in Prop p, PropShape sh, Span<double> x, Span<double> y, Span<double> nx, Span<double> ny)
    {
        int n = Math.Min(MaxHull, sh.HullX.Length);
        double c = Math.Cos(p.Th), s = Math.Sin(p.Th);
        for (int k = 0; k < n; k++)
        {
            x[k] = p.X + c * sh.HullX[k] - s * sh.HullY[k]; y[k] = p.Y + s * sh.HullX[k] + c * sh.HullY[k];
            nx[k] = c * sh.HullNx[k] - s * sh.HullNy[k]; ny[k] = s * sh.HullNx[k] + c * sh.HullNy[k];
        }
        return n;
    }

    /// <summary>가장 덜 겹친 변 (SAT): 변마다 맞은편 꼭짓점의 가장 깊은 거리 — 그 가운데 가장 큰 것 (stop 넘으면 그만 본다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static double MaxSeparation(Span<double> ax, Span<double> ay, Span<double> anx, Span<double> any, int na, Span<double> bx, Span<double> by, int nb, double stop, out int edge)
    {
        double best = double.NegativeInfinity;
        edge = 0;
        for (int i = 0; i < na; i++)
        {
            if (anx[i] == 0 && any[i] == 0) continue;
            double s = double.PositiveInfinity;
            for (int j = 0; j < nb; j++) s = Math.Min(s, anx[i] * (bx[j] - ax[i]) + any[i] * (by[j] - ay[i]));
            if (s > best) { best = s; edge = i; if (s > stop) break; }
        }
        return best;
    }

    /// <summary>
    /// 두 물체의 닿는 점 (≤ 2 — b2CollidePolygons 꼴): SAT로 기준면 (덜 겹친 쪽) → 맞은편에서 가장 마주 보는 변을 기준면 양옆으로 자르고 · 분리가 margin 아래인 점만
    /// (기준면까지 반 — 두 몸이 함께 쓰는 한 점). 두 쪽이 거의 같으면 (0.1 × 0.5도트 안) 두 기준의 점을 짝지어 가운데로 · 법선도 둘의 가운데 —
    /// 어느 쪽이 작은 Id인지에 기대지 않는다 (거울 물체 둘이 부딪혀도 거울 그대로). 법선은 b에서 a 쪽. 점마다 열쇠 (기준 · 기준 변 · 맞은 변 · 잘린 끝).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int PairPoints(in Prop a, PropShape sa, in Prop b, PropShape sb, double margin, Span<double> px, Span<double> py, Span<double> sep, Span<int> key, out double nx, out double ny)
    {
        nx = ny = 0;
        if (sa.HullX.Length == 0 || sb.HullX.Length == 0) return 0;
        Span<double> ax = stackalloc double[MaxHull], ay = stackalloc double[MaxHull], anx = stackalloc double[MaxHull], any = stackalloc double[MaxHull];
        Span<double> bx = stackalloc double[MaxHull], by = stackalloc double[MaxHull], bnx = stackalloc double[MaxHull], bny = stackalloc double[MaxHull];
        int na = WorldHull(a, sa, ax, ay, anx, any), nb = WorldHull(b, sb, bx, by, bnx, bny);
        double sepA = MaxSeparation(ax, ay, anx, any, na, bx, by, nb, margin, out int ea);
        if (sepA > margin) return 0;
        double sepB = MaxSeparation(bx, by, bnx, bny, nb, ax, ay, na, margin, out int eb);
        if (sepB > margin) return 0;
        const double tie = 0.1 * Slop;
        if (sepB > sepA + tie)
        {
            int n = Manifold(bx, by, bnx, bny, nb, eb, ax, ay, anx, any, na, margin, px, py, sep, key);
            nx = bnx[eb]; ny = bny[eb];
            for (int k = 0; k < n; k++) key[k] |= 1;
            return n;
        }
        if (sepA > sepB + tie)
        {
            int n = Manifold(ax, ay, anx, any, na, ea, bx, by, bnx, bny, nb, margin, px, py, sep, key);
            nx = -anx[ea]; ny = -any[ea];
            return n;
        }
        // 거의 같다: 두 기준의 점을 짝지어 가운데 (짝이 없으면 많은 쪽 그대로)
        Span<double> qx = stackalloc double[2], qy = stackalloc double[2], qs = stackalloc double[2];
        Span<int> qk = stackalloc int[2];
        int nA = Manifold(ax, ay, anx, any, na, ea, bx, by, bnx, bny, nb, margin, px, py, sep, key);
        int nB = Manifold(bx, by, bnx, bny, nb, eb, ax, ay, anx, any, na, margin, qx, qy, qs, qk);
        double mx = bnx[eb] - anx[ea], my = bny[eb] - any[ea], ml = Math.Sqrt(mx * mx + my * my);
        if (nA == 0 && nB == 0) return 0;
        if (nA != nB || ml <= 1e-9)
        {
            if (nB > nA) { for (int k = 0; k < nB; k++) { px[k] = qx[k]; py[k] = qy[k]; sep[k] = qs[k]; key[k] = qk[k] | 1; } nx = bnx[eb]; ny = bny[eb]; return nB; }
            nx = -anx[ea]; ny = -any[ea];
            return nA;
        }
        nx = mx / ml; ny = my / ml;
        bool used0 = false, used1 = false;
        for (int k = 0; k < nA; k++)
        {
            int best = -1; double bd = double.PositiveInfinity;
            for (int q = 0; q < nB; q++)
            {
                if (q == 0 ? used0 : used1) continue;
                double d = (qx[q] - px[k]) * (qx[q] - px[k]) + (qy[q] - py[k]) * (qy[q] - py[k]);
                if (d < bd) { bd = d; best = q; }
            }
            if (best == 0) used0 = true; else used1 = true;
            px[k] = 0.5 * (px[k] + qx[best]); py[k] = 0.5 * (py[k] + qy[best]); sep[k] = 0.5 * (sep[k] + qs[best]);
            key[k] = 1 << 15 | key[k] | eb << 11;   // 가운데 점: a 기준 점의 열쇠 (변 · 맞은 변 · 잘린 끝) + b 기준 변
        }
        return nA;
    }

    /// <summary>
    /// 기준 (r · 변 er)에 대한 맞은편 (c)의 닿는 점: 기준 법선에 가장 마주 보는 맞은 변을 기준 변 양옆 반평면으로 자르고 · 분리 ≤ margin인 끝마다
    /// 기준면까지 반 간 점 · 분리 · 열쇠 (기준 변 · 맞은 변 · 잘린 끝 — 아래 1비트는 부른 쪽이 쓴다).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static int Manifold(Span<double> rx, Span<double> ry, Span<double> rnx, Span<double> rny, int nr, int er,
        Span<double> cx, Span<double> cy, Span<double> cnx, Span<double> cny, int nc, double margin, Span<double> px, Span<double> py, Span<double> sep, Span<int> key)
    {
        double n0 = rnx[er], n1 = rny[er];
        int ec = 0;
        double low = double.PositiveInfinity;
        for (int i = 0; i < nc; i++) { double d = n0 * cnx[i] + n1 * cny[i]; if (d < low) { low = d; ec = i; } }
        double v1x = rx[er], v1y = ry[er], v2x = rx[(er + 1) % nr], v2y = ry[(er + 1) % nr];
        double tx = v2x - v1x, ty = v2y - v1y, tl = Math.Sqrt(tx * tx + ty * ty);
        if (tl <= 0) return 0;
        tx /= tl; ty /= tl;
        Span<double> qx = stackalloc double[2], qy = stackalloc double[2];
        Span<int> qk = stackalloc int[2];
        qx[0] = cx[ec]; qy[0] = cy[ec]; qx[1] = cx[(ec + 1) % nc]; qy[1] = cy[(ec + 1) % nc]; qk[0] = 0; qk[1] = 1;
        if (Clip(qx, qy, qk, -tx, -ty, -(tx * v1x + ty * v1y), 2) < 2) return 0;
        if (Clip(qx, qy, qk, tx, ty, tx * v2x + ty * v2y, 3) < 2) return 0;
        int n = 0;
        for (int k = 0; k < 2; k++)
        {
            double s = n0 * (qx[k] - v1x) + n1 * (qy[k] - v1y);
            if (s > margin) continue;
            px[n] = qx[k] - 0.5 * s * n0; py[n] = qy[k] - 0.5 * s * n1; sep[n] = s;
            key[n] = er << 1 | ec << 5 | qk[k] << 9;
            n++;
        }
        return n;
    }

    /// <summary>선분을 반평면 (법선 · x ≤ off)으로 자른다 — 잘린 끝은 열쇠 tag (b2ClipSegmentToLine).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static int Clip(Span<double> x, Span<double> y, Span<int> k, double nx, double ny, double off, int tag)
    {
        double d0 = nx * x[0] + ny * y[0] - off, d1 = nx * x[1] + ny * y[1] - off;
        if (d0 <= 0 && d1 <= 0) return 2;
        if (d0 > 0 && d1 > 0) return 0;
        double t = d0 / (d0 - d1), ix = x[0] + t * (x[1] - x[0]), iy = y[0] + t * (y[1] - y[0]);
        if (d0 > 0) { x[0] = ix; y[0] = iy; k[0] = tag; } else { x[1] = ix; y[1] = iy; k[1] = tag; }
        return 2;
    }

    /// <summary>섬 걸음 자리 (물체가 늘면 담은 것을 옮겨 넓힌다 — 걷는 동안 늘 수 있다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void GrowMembers(int count)
    {
        if (_runs.Length >= count) return;
        int m = Math.Max(count, 2 * _runs.Length);
        Array.Resize(ref _runs, m); Array.Resize(ref _imem, m); Array.Resize(ref _iord, m); Array.Resize(ref _nearCount, m); Array.Resize(ref _cb, 4 * m);
        Array.Resize(ref _runNear, 4 * m); Array.Resize(ref _runTouch, 4 * m);
    }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void GrowContacts(int contacts)
    {
        if (_ic.Length < contacts) { _ic = new IslandContact[contacts * 2]; _icStage = new IslandContact[contacts * 2]; }
    }

    /// <summary>섬 걸음에 처음 들어온다 (틱 처음 · 쉬다 맞아 깬 때 tau): 몸 값 · 꼴 · 재질 · 맞힐 만한 원판 · 덜컹 차례 (그 때까지는 실려 갔다) · 기록.</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void JoinRun(ref PropRun r, ref Prop p, int k, double tau, double dt)
    {
        if (p.Kg <= 0) FillBody(ref p);
        r.Fm = PropForm(p); r.Sh = r.Fm.Shape; r.Sm = r.Sh.Hit;
        r.Mat = T.Materials[Math.Min(p.Mat, T.Materials.Count - 1)].Def;
        r.M = p.Kg; r.In = p.Inertia; r.Rho = r.Fm.Rho;
        r.Active = true;
        // 맞힐 수 있는 이 (⑧이 알린 원판 — 이번 틱에 닿을 만한 것 · 많아야 4): 이미 닿아 있던 이는 새로 맞힌 것이 아니다
        r.Ns = 0;
        int nNear = 0;
        if (_nActors > 0 && PropDetail > 0)   // 가벼운 셈 (상상 복제)은 원판과 닿지 않는다
        {
            double far = (Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy) + Math.Abs(p.W) * r.Sm.reach) * dt + r.Sm.reach + ActorR;
            for (int a = 0; a < _nActors && nNear < 4; a++)
            {
                int ac = _actors[a].Cell;
                if (ac < 0 || ac >= L.N) continue;
                if (PathDistance(_actors[a], p.X, p.Y) > far) continue;   // 이번 틱 길 (서 있으면 제 칸 가운데) 가운데 닿을 만한 것
                ActorAt(_actors[a], tau, dt, out double ax, out double ay, out _, out _);
                _runNear[4 * k + nNear] = a; _runTouch[4 * k + nNear] = TouchesActor(p, r.Sm, ax, ay, _actors[a].R); nNear++;
            }
        }
        _nearCount[k] = nNear;
        r.Walked = false;
        for (int q = 0; q < nNear; q++) r.Walked |= _actors[_runNear[4 * k + q]].PathN >= 2 && !DropperOf(p, _actors[_runNear[4 * k + q]]);
        if (tau > 0)
        {
            // 쉬다 깼다: 그 때까지 그 몸의 덜컹은 실려 갔다 (반작용이 맞춘다) — 지난 덜컹의 각속도만 잇는다
            r.Jp = 0; r.Jw = 0;
            for (; r.Jp < _jolts.Count && _jolts[r.Jp].T <= tau + 1e-12; r.Jp++) if (_jolts[r.Jp].B == r.Host) r.Jw += _jolts[r.Jp].Dw;
        }
        var tr = Track;
        if (tr != null)
        {
            if (tau > 0) { tr.Add(MotionTrack.WhoProp, p.Id, tau, p.X, p.Y, p.Th, 0, 0, 0, MotionTrack.EvNone, p.Z, 0); r.LastKey = tau; }
            else { tr.Add(MotionTrack.WhoProp, p.Id, 0, p.X, p.Y, p.Th, p.Vx, p.Vy, p.W, MotionTrack.EvNone, p.Z, p.Vz); r.LastKey = 0; }
        }
    }

    private int[] _nearCount = new int[8];

    // 섬 걸음 (섬 안 차례 — 걷는 동안 늘 수 있다): 물체 차례 · Id 차례로 늘어선 섬 안 차례 · 짝 (앞이 작은 Id) · 이미 짝인가 · 살핀 상자 (짝을 다 찾은 곳)
    private int[] _imem = new int[8], _iord = new int[8];
    private int _icount, _root;
    private readonly List<(int a, int b)> _ipairs = new();
    private double[] _cb = new double[32];

    /// <summary>
    /// 섬 하나의 한 틱 (차례 _mem[first ..] · 짝 _pairs[p0 ..]): 잔걸음마다 깨어 있는 것마다 (물체 하나의 걸음 그대로) 길이 ℓ = max(2도트, 틈새 − 닿는 반지름) · 0.1rad ·
    /// (마찰이 있으면) 0.1초 · 짝마다 max(2도트, 떨어진 거리) / 서로 빠르기 · 붙을 만하면 0.05초를 넘지 않게 — 닿을 것이 없는 것은 마찰의 정확한 걸음 ·
    /// 닿는 것은 마찰 빠르기 → 섬의 충격량을 한꺼번에 (따뜻한 시작) → 자리 → 바로잡음. 쉬는 짝은 충격량을 받으면 그 잔걸음에 깬다 · 잠든 것은 섬에 남아 다시 깰 수 있다 ·
    /// 살핀 곳을 벗어나면 섬 안 물체 · 아직 아무 섬에도 들지 않은 잠든 것 가운데 새 짝을 찾는다 (<see cref="Discover"/>).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void StepIsland(Sim sim, Span<Prop> all, int first, int count, int p0, int np, double dt, bool world)
    {
        GrowMembers(count + 8);
        _wkN = 0; _warmH = 0;   // 닿음 캐시는 섬마다 · 틱마다 새로 (찍지 않는다)
        _icount = count; _ipairs.Clear(); _root = Find(_mem[first]);
        for (int k = 0; k < count; k++)
        {
            int i = _mem[first + k];
            _imem[k] = i; _iord[k] = k;
            ref var r = ref _runs[k];
            r = default;
            r.I = i; r.Host = world ? _hostOf[i] : -1;
            r.Hs = PropForm(all[i]).Shape; r.Reach = r.Hs.Hit.reach;
            _nearCount[k] = 0;
            for (int b = 0; b < 4; b++) _cb[4 * k + b] = _box[4 * i + b];
            if (_role[i] == 1) { if (world) _stepped[i] = true; JoinRun(ref r, ref all[i], k, 0, dt); }
        }
        for (int q = 0; q < np; q++)
        {
            long pr = _pairs[p0 + q];
            int ia = (int)(pr >> 21 & 0x1FFFFF), ib = (int)(pr & 0x1FFFFF);
            _ipairs.Add((Local(first, count, ia), Local(first, count, ib)));
        }
        var tr = Track;
        double rem = dt;
        int steps = 0;
        Span<double> cpx = stackalloc double[2], cpy = stackalloc double[2], csep = stackalloc double[2];
        Span<int> ckey = stackalloc int[2];
        while (rem > 1e-12)
        {
            double tau = dt - rem;
            sim.Facts.SubNow = (byte)Math.Clamp((int)Math.Round(tau / dt * 255), 0, 255);   // 이 잔걸음에 난 사실 · 소리의 때
            // ── 1. 깨어 있는 것마다: 덜컹 · 우주 · 구멍 · 힘 · 제 잔걸음 길이 ──
            double h = rem, nextJolt = double.PositiveInfinity;
            bool anyActive = false;
            for (int k = 0; k < _icount; k++)
            {
                ref var r = ref _runs[k];
                if (!r.Active || r.Done) continue;
                if (!SubstepBegin(sim, ref r, ref all[r.I], k, tau, rem, dt)) continue;
                anyActive = true;
                h = Math.Min(h, r.H); nextJolt = Math.Min(nextJolt, r.NextJolt);
            }
            if (!anyActive) break;
            // 살핀 곳을 벗어날 것은 새 짝을 찾는다 (제 잔걸음 길이로 갈 만큼)
            if (Pairs)
                for (int k = 0; k < _icount; k++)
                {
                    if (!_runs[k].Active || _runs[k].Done) continue;
                    ref var p = ref all[_runs[k].I];
                    double e = _runs[k].Sm.reach + 2 * DotForm.Dot + _runs[k].Speed * _runs[k].H;
                    if (p.X - e >= _cb[4 * k] && p.Y - e >= _cb[4 * k + 1] && p.X + e <= _cb[4 * k + 2] && p.Y + e <= _cb[4 * k + 3]) continue;
                    Discover(all, k, p.X - e, p.Y - e, p.X + e, p.Y + e, world);
                }
            // 짝: 서로 빠르기로 건너뛰지 않게 · 붙을 만하면 짧게 (우주로 갔거나 구멍을 막은 것과의 짝은 내린다 — 차례 그대로)
            bool pile = false;
            int keep = 0;
            for (int q = 0; q < _ipairs.Count; q++) { var pq = _ipairs[q]; if (!_runs[pq.a].Done && !_runs[pq.b].Done) _ipairs[keep++] = pq; }
            if (keep < _ipairs.Count) _ipairs.RemoveRange(keep, _ipairs.Count - keep);
            for (int q = 0; q < _ipairs.Count; q++)
            {
                var (ka, kb) = _ipairs[q];
                ref var ra = ref _runs[ka]; ref var rb = ref _runs[kb];
                if (!ra.Active && !rb.Active) continue;
                ref var a = ref all[ra.I]; ref var b = ref all[rb.I];
                if (!ZMeet(a, b)) continue;   // 높이가 겹치지 않는다 (탁자 위 · 바닥)
                double reachA = ra.Reach, reachB = rb.Reach;
                double dx = a.X - b.X, dy = a.Y - b.Y, gap = Math.Sqrt(dx * dx + dy * dy) - reachA - reachB;
                double rvx = a.Vx - b.Vx, rvy = a.Vy - b.Vy, rel = Math.Sqrt(rvx * rvx + rvy * rvy) + Math.Abs(a.W) * reachA + Math.Abs(b.W) * reachB;
                if (rel > 0) h = Math.Min(h, Math.Max(2 * DotForm.Dot, gap) / rel);
                if (gap <= 2 * DotForm.Dot + rel * h) pile = true;
            }
            if (pile) h = Math.Min(h, MaxPileH);
            if (++steps >= (PropDetail > 0 ? MaxPropSteps : LightPropSteps)) h = Math.Min(rem, Math.Max(1e-9, nextJolt - tau));
            GrowContacts(4 * _icount + 2 * _ipairs.Count + 4);
            // ── 2. 닿음: 바닥 · 벽 · 가구 (깨어 있는 것마다 — 받은 힘만큼 빨라진 길로) · 물체끼리 (껍질) ──
            int nStage = 0;
            for (int k = 0; k < _icount; k++)
            {
                ref var r = ref _runs[k];
                r.Velo = false; r.Paired = false; r.FloorL = 0;
                r.S0 = nStage; r.Ns = 0; r.Na = 0;   // 쉬는 것 · 끝난 것도 비운다 (잠들기 전 잔걸음의 닿음 자리가 차례에 다시 들지 않게)
                if (!r.Active || r.Done) continue;
                ref var p = ref all[r.I];
                r.Px0 = r.M * p.Vx; r.Py0 = r.M * p.Vy; r.L0 = r.M * (p.X * p.Vy - p.Y * p.Vx) + r.In * p.W;
                r.Lw = r.Blown ? (p.X * r.Fy - p.Y * r.Fx + r.Tq) * h : 0;   // 바람이 준 각운동량 (원점 둘레)
                double speedEnd = r.Speed + r.Acc * h;
                r.StopAt = -1;
                if (speedEnd > 0 && r.Room < speedEnd * h + 2 * DotForm.Dot)
                {
                    var pk = p;   // 받은 힘만큼 빨라진 길로 닿을 곳을 찾는다
                    if (r.Blown) { pk.Vx += r.Ax * h; pk.Vy += r.Ay * h; pk.W += r.Al * h; }
                    if (r.Wb != 0) Coriolis(ref pk, -2 * r.Wb * h);
                    HeightWindow(pk, r.Fm);
                    int nc = FindContacts(pk, r.Sm, h, r.Mat.Mu, r.Mat.Rest);
                    FullHeight();
                    if (nc > 0)
                    {
                        p = pk; r.Velo = true; r.Ns = nc;
                        for (int c = 0; c < nc; c++)
                        {
                            ref var hc = ref _propHits[c];
                            _icStage[nStage++] = new IslandContact { A = k, B = -1, Rx = hc.Rx, Ry = hc.Ry, Nx = hc.Nx, Ny = hc.Ny, Sep = hc.Sep, Mu = hc.Mu, E = hc.E, Kn = hc.Kn, Kt = hc.Kt, Cell = hc.Cell, Key = hc.Quad, Cap = double.PositiveInfinity, Mat = hc.Mat };
                        }
                    }
                }
                // 걷는 이 · 선 이 · 누운 이의 원판 (τ 때 자리 · 빠르기 — 다가오면 반발 0.2 · 미는 힘 150N 한도 · 놓은 이와는 닿지 않는다)
                int nNear = _nearCount[k];
                for (int q = 0; q < nNear; q++)
                {
                    int ai = _runNear[4 * k + q];
                    ref var ac = ref _actors[ai];
                    if (DropperOf(p, ac) || p.Z >= ac.H) continue;   // 머리 위 (탁자 · 선반 위 높이)는 지나간다
                    ActorAt(ac, tau, dt, out double ax, out double ay, out double avx, out double avy);
                    double sep = DiscSeparation(p, r.Sh, ax, ay, ac.R, out double nx, out double ny, out double cpX, out double cpY);
                    double rvx = p.Vx - avx, rvy = p.Vy - avy, rel = Math.Sqrt(rvx * rvx + rvy * rvy) + Math.Abs(p.W) * r.Sm.reach;
                    if (sep > rel * h + Slop) continue;
                    if (!r.Velo)
                    {
                        // 원판에만 닿는다: 받은 힘만큼 빨라진 빠르기 (정적 닿음의 pk와 같게)
                        if (r.Blown) { p.Vx += r.Ax * h; p.Vy += r.Ay * h; p.W += r.Al * h; }
                        if (r.Wb != 0) Coriolis(ref p, -2 * r.Wb * h);
                        r.Velo = true;
                    }
                    double rx = cpX - p.X, ry = cpY - p.Y, rn = rx * ny - ry * nx, rt = rx * nx + ry * ny;
                    // 한도: 원판이 미는 몫은 150N × h · 물체가 제 빠르기로 원판에 부딪는 몫은 그 빠르기를 되튀기는 데까지 (몸이 받아 낸다)
                    double own = Math.Max(0, -((p.Vx - p.W * ry) * nx + (p.Vy + p.W * rx) * ny));
                    _icStage[nStage++] = new IslandContact { A = k, B = -1, Rx = rx, Ry = ry, Nx = nx, Ny = ny, Sep = sep, Mu = Math.Sqrt(r.Mat.Mu * FootMu), E = ActorRest, Cell = -2 - ai,
                        Kn = 1 / r.M + rn * rn / r.In, Kt = 1 / r.M + rt * rt / r.In, Ux = avx, Uy = avy, Cap = ActorPushN * h + (1 + ActorRest) * own / (1 / r.M + rn * rn / r.In), Key = 8 + ai };
                    r.Na++;
                }
            }
            // 물체끼리 (섬 짝 — 껍질이 margin 안 · 법선은 큰 Id → 작은 Id 쪽)
            int pStage = nStage, nPair = 0;
            for (int q = 0; q < _ipairs.Count; q++)
            {
                var (ka, kb) = _ipairs[q];
                ref var ra = ref _runs[ka]; ref var rb = ref _runs[kb];
                if (ra.Done || rb.Done || !ra.Active && !rb.Active) continue;
                ref var a = ref all[ra.I]; ref var b = ref all[rb.I];
                if (!ZMeet(a, b)) continue;
                double reachA = ra.Reach, reachB = rb.Reach;
                double rvx = a.Vx - b.Vx, rvy = a.Vy - b.Vy, rel = Math.Sqrt(rvx * rvx + rvy * rvy) + Math.Abs(a.W) * reachA + Math.Abs(b.W) * reachB;
                double margin = rel * h + Slop, dx = a.X - b.X, dy = a.Y - b.Y, far = reachA + reachB + margin;
                if (dx * dx + dy * dy > far * far) continue;
                int npt = PairPoints(a, ra.Hs, b, rb.Hs, margin, cpx, cpy, csep, ckey, out double nx, out double ny);
                if (npt == 0) continue;
                if (!ra.Active) Rest(ref ra, ref a, h); if (!rb.Active) Rest(ref rb, ref b, h);
                var ma = MaterialOf(a); var mb = MaterialOf(b);
                double mu = Math.Sqrt(ma.Mu * mb.Mu), e = Math.Min(ma.Rest, mb.Rest);
                for (int c = 0; c < npt; c++)
                {
                    double rax = cpx[c] - a.X, ray = cpy[c] - a.Y, rbx = cpx[c] - b.X, rby = cpy[c] - b.Y;
                    double rna = rax * ny - ray * nx, rnb = rbx * ny - rby * nx, rta = rax * nx + ray * ny, rtb = rbx * nx + rby * ny;
                    _icStage[nStage++] = new IslandContact { A = ka, B = kb, Rx = rax, Ry = ray, Qx = rbx, Qy = rby, Nx = nx, Ny = ny, Sep = csep[c], Mu = mu, E = e, Cell = -1, Cap = double.PositiveInfinity,
                        Kn = 1 / ra.M + 1 / rb.M + rna * rna / ra.In + rnb * rnb / rb.In, Kt = 1 / ra.M + 1 / rb.M + rta * rta / ra.In + rtb * rtb / rb.In,
                        Key = ckey[c] };
                    nPair++;
                }
                ra.Paired = rb.Paired = true;
            }
            // 차례 (작은 Id, 큰 Id, 점 번호): Id 차례로 물체마다 제 정적 닿음 → 제가 작은 쪽인 짝 (큰 쪽 Id 차례)
            _nic = 0;
            for (int o = 0; o < _icount; o++)
            {
                int k = _iord[o];
                for (int c = 0; c < _runs[k].Ns + _runs[k].Na; c++) _ic[_nic++] = _icStage[_runs[k].S0 + c];
                _runs[k].S0 = _nic - _runs[k].Ns - _runs[k].Na;
                if (nPair == 0 || !_runs[k].Paired) continue;   // 짝 닿음이 없는 것은 제 정적 닿음뿐
                for (int o2 = o + 1; o2 < _icount; o2++)
                {
                    int k2 = _iord[o2];
                    if (!_runs[k2].Paired) continue;
                    for (int c = pStage; c < nStage; c++) if (_icStage[c].A == k && _icStage[c].B == k2) _ic[_nic++] = _icStage[c];
                }
            }
            // ── 3. 빠르기: 닿을 것이 없는 것은 마찰의 정확한 걸음 · 벽 · 가구에만 닿는 것은 마찰 빠르기 (PH4 그대로) ·
            //    물체끼리 닿는 것은 받은 힘만큼 (pk) — 바닥 마찰은 충격량 고리에서 함께 푼다 (눌린 것끼리 비트는 몫을 정지 마찰이 붙든다). 닿음 목표는 마찰 뒤 빠르기로 ──
            bool hit = false;
            for (int k = 0; k < _icount; k++)
            {
                ref var r = ref _runs[k];
                if (!r.Active || r.Done) continue;
                ref var p = ref all[r.I];
                if (!r.Velo && !r.Paired)
                {
                    // 힘 반 → 마찰의 정확한 걸음 → 힘 반 (마찰로 선 채 정지 타원 안이면 그대로)
                    if (r.Blown) { p.Vx += 0.5 * r.Ax * h; p.Vy += 0.5 * r.Ay * h; p.W += 0.5 * r.Al * h; }
                    if (r.Wb != 0) Coriolis(ref p, -r.Wb * h);
                    FloorFriction(ref p, in r, h, true, out r.StopAt);
                    if (r.Wb != 0) Coriolis(ref p, -r.Wb * h);
                    if (r.Blown && !(p.Vx == 0 && p.Vy == 0 && p.W == 0 && Holds(r.Fx, r.Fy, r.Tq, StaticShare * r.MuK, r.M * r.Gp, r.Rho))) { p.Vx += 0.5 * r.Ax * h; p.Vy += 0.5 * r.Ay * h; p.W += 0.5 * r.Al * h; }
                    continue;
                }
                if (!r.Velo)
                {
                    // 물체끼리만 닿는다: 받은 힘만큼 빨라진 빠르기 (정적 닿음의 pk와 같게)
                    if (r.Blown) { p.Vx += r.Ax * h; p.Vy += r.Ay * h; p.W += r.Al * h; }
                    if (r.Wb != 0) Coriolis(ref p, -2 * r.Wb * h);
                    r.Velo = true;
                }
                if (!r.Paired) { FloorFriction(ref p, in r, h, false, out _); r.Tvx = p.Vx; r.Tvy = p.Vy; r.Tw = p.W; continue; }
                var pf = p;
                FloorFriction(ref pf, in r, h, false, out _);
                r.Tvx = pf.Vx; r.Tvy = pf.Vy; r.Tw = pf.W;
                r.FloorL = r.Gp * r.MuK * r.M * h; r.Ffx = r.Ffy = r.Ffw = 0;
            }
            // 닿음 목표 (다가오는 빠르기 · 반발은 닿기 직전 값 — 바닥 마찰 뒤)
            for (int c = 0; c < _nic; c++)
            {
                ref var ct = ref _ic[c];
                ref var a = ref _runs[ct.A];
                double vn0 = (a.Tvx - a.Tw * ct.Ry - ct.Ux) * ct.Nx + (a.Tvy + a.Tw * ct.Rx - ct.Uy) * ct.Ny;
                if (ct.B >= 0) { ref var b = ref _runs[ct.B]; vn0 -= (b.Tvx - b.Tw * ct.Qy) * ct.Nx + (b.Tvy + b.Tw * ct.Qx) * ct.Ny; }
                ct.Bounce = -vn0 > BounceV && (ct.Sep <= 0 || -vn0 * h > ct.Sep);
                ct.Vn0 = vn0;
                ct.Target = ct.Bounce ? -ct.E * vn0 : -Math.Max(ct.Sep, 0) / h;
                hit |= ct.Bounce;
            }
            // ── 4. 충격량 (섬 한꺼번에 · 짝은 따뜻한 시작) ──
            if (nPair == 0) _wkN = 0;
            if (_nic > 0)
            {
                if (nPair > 0) WarmStart(all, h);
                SolveIsland(all, _icount, steps == 1 || hit ? FirstIters : nPair > 0 ? PileIters : BaseIters);   // 틱 첫 잔걸음 · 부딪는 잔걸음은 16번 (따뜻한 시작이 없다)
                if (nPair > 0) KeepWarm(all, h);
                if (hit) IslandHits(sim, all, tau);   // 맞음 (PH8 — 깨짐 · 소리는 틱 끝에)
                for (int k = 0; k < _icount; k++)
                {
                    ref var r = ref _runs[k];
                    for (int c = r.S0; c < r.S0 + r.Ns; c++) if (_ic[c].Bounce && _ic[c].Jn > 0) { Bounces++; break; }
                }
                for (int c = 0; c < _nic; c++)
                {
                    ref var ct = ref _ic[c];
                    if (ct.Cell > -2 || ct.Jn <= 0) continue;
                    ref var kr = ref _runs[ct.A];
                    ref var kp = ref all[kr.I];
                    int ai = -2 - ct.Cell;
                    ref var ac = ref _actors[ai];
                    // 원판에 새로 닿았다 → 맞음 장부 (부딪기 직전 서로 빠르기 — 원판이 되튀겨 몸에 들지 않아도)
                    for (int q = 0; q < _nearCount[ct.A]; q++)
                        if (_runNear[4 * ct.A + q] == ai && !_runTouch[4 * ct.A + q]) { ActorHit(sim, kp, ac, kr.Tvx - ct.Ux, kr.Tvy - ct.Uy); _runTouch[4 * ct.A + q] = true; }
                    if (kr.Kicked) continue;
                    // 원판이 밀었다 (걷던 이 · 선 이에게 부딪혀 튐): 원인은 그 이가 하던 일에 잇는다
                    kp.Cause = sim.Causes.Add(CauseKind.Impact, ac.PathN >= 2 ? $"지나가던 발에 채인 {PropName(kp)}" : $"사람 · 로봇에게 부딪혀 튄 {PropName(kp)}", sim.Tick, ac.Cell, ac.Cause != 0 ? ac.Cause : kp.Cause);
                    kp.PushImp = (float)ct.Jn;
                    kr.Kicked = true;
                }
                int lastA = -1, lastB = -1, hitA = -1, hitB = -1;
                for (int c = 0; c < _nic; c++)
                {
                    ref var ct = ref _ic[c];
                    if (ct.B < 0 || ct.Jn <= 0) continue;
                    if (ct.A != hitA || ct.B != hitB) { PairHits++; hitA = ct.A; hitB = ct.B; }
                    if (!ct.Bounce || ct.A == lastA && ct.B == lastB) continue;
                    Bounces++; lastA = ct.A; lastB = ct.B;
                }
            }
            int icount0 = _icount;
            for (int k = 0; k < icount0; k++)
            {
                if (_runs[k].Done) continue;
                ref var p = ref all[_runs[k].I];
                if (_runs[k].Active) { if (_runs[k].Velo) { p.X += p.Vx * h; p.Y += p.Vy * h; p.Th += p.W * h; } continue; }
                if (!_runs[k].Paired || p.Vx == 0 && p.Vy == 0 && p.W == 0) continue;
                // 쉬던 것이 맞았다 → 깬다 (이 잔걸음부터 걷는다 — 쉬던 동안 장부는 반작용이 틱 처음 세상 운동량부터 맞춘다 · 틱 안 기록은 그 때부터)
                p.State = 1; p.Asleep = 0; p.Still = 0;
                if (world) _stepped[_runs[k].I] = true;
                {
                    ref var r = ref _runs[k];
                    JoinRun(ref r, ref p, k, tau, dt);
                    r.Steps++;
                    r.Px0 = r.Py0 = r.L0 = r.Lw = 0; r.StopAt = -1; r.Velo = r.Paired = true;
                    r.Fx = r.Fy = r.Tq = r.Wfx = r.Wfy = r.Wtq = r.Wb = 0; r.Windy = r.Framed = r.Blown = false;
                    r.PreX = p.X; r.PreY = p.Y; r.PreTh = p.Th; r.PreVx = r.PreVy = r.PreW = 0;
                    r.Cell = CellOf(p); r.Gp = r.Cell >= 0 ? Gravity * F.Grav[r.Cell] : 0;
                    r.NextJolt = double.PositiveInfinity;
                    for (int q = r.Jp; q < _jolts.Count; q++) if (_jolts[q].B == r.Host) { r.NextJolt = _jolts[q].T; break; }
                    r.Speed = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy) + Math.Abs(p.W) * r.Sm.reach; r.H = h;
                }
                double e = _runs[k].Sm.reach + 2 * DotForm.Dot + _runs[k].Speed * h;
                if (Pairs) Discover(all, k, p.X - e, p.Y - e, p.X + e, p.Y + e, world);   // 붙어 있던 것을 다음 잔걸음부터 민다
                ref var pw = ref all[_runs[k].I];
                pw.X += pw.Vx * h; pw.Y += pw.Vy * h; pw.Th += pw.W * h;
            }
            // ── 5. 깨어 있는 것마다: 장부 · 바로잡음 · 원판 · 기록 · 구멍 · 잠 ──
            rem -= h;
            for (int k = 0; k < _icount; k++)
            {
                ref var r = ref _runs[k];
                if (!r.Active || r.Done) continue;
                SubstepEnd(sim, ref r, ref all[r.I], k, tau, rem, h, dt);
            }
        }
        for (int k = 0; k < _icount; k++)
        {
            ref var r = ref _runs[k];
            if (r.Active && !r.Done) TrackEnd(tr, all[r.I], dt - rem, ref r.LastKey);
            if (r.SlideV2 > ScrapeV * ScrapeV) Noise(CellOf(all[r.I]), SoundKind.Scrape, 0.5 * r.M * r.SlideV2, all[r.I], r.SlideSub);   // 드르륵 (1m/s 넘게 바닥을 미끄러졌다)
            PropSteps += r.Steps;
        }
    }

    /// <summary>
    /// 새 짝 찾기 (섬 안 차례 k가 살핀 곳을 벗어날 때 · 쉬다 깰 때 — 상자 (m)): 섬 안 물체 가운데 아직 짝이 아닌 것 (지금 자리 ± 닿는 반지름 + 2도트 + 제 잔걸음에 갈 만큼) ·
    /// 아무 섬에도 들지 않은 잠든 것 (칸 통 — 잠든 것은 틱 처음 자리 그대로) → 쉬는 짝으로 들인다. 다른 섬의 것은 들이지 않는다 (그 섬은 따로 걷는다 — 겹쳐 끝나면 섬 놓침).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void Discover(Span<Prop> all, int k, double x0, double y0, double x1, double y1, bool world)
    {
        int i = _imem[k], host = _runs[k].Host;
        for (int j = 0; j < _icount; j++)
        {
            if (j == k || _runs[j].Done) continue;
            int ij = _imem[j];
            if (PairedIn(k, j)) continue;
            ref var q = ref all[ij];
            double e = _runs[j].Reach + 2 * DotForm.Dot + (_runs[j].Active ? _runs[j].Speed * _runs[j].H : 0);
            if (q.X + e < x0 || q.X - e > x1 || q.Y + e < y0 || q.Y - e > y1) continue;
            _ipairs.Add(i < ij ? (k, j) : (j, k));
        }
        double pad = MaxReach() + 2 * DotForm.Dot;
        int nCand = Gather(all, x0 - pad, y0 - pad, x1 + pad, y1 + pad);
        for (int c = 0; c < nCand; c++)
        {
            int j = _cand[c];
            if (_role[j] != 0 || !Collides(all[j]) || (world ? _hostOf[j] : -1) != host) continue;
            double e = SamplesOf(PropForm(all[j])).reach + 2 * DotForm.Dot;
            if (all[j].X + e < x0 || all[j].X - e > x1 || all[j].Y + e < y0 || all[j].Y - e > y1) continue;
            // 쉬는 짝으로 들인다
            _role[j] = 2; _isl[j] = _root;
            GrowMembers(_icount + 1);
            int kn = _icount++;
            _imem[kn] = j; _nearCount[kn] = 0;
            var hs = PropForm(all[j]).Shape;
            _runs[kn] = new PropRun { I = j, Host = host, Hs = hs, Reach = hs.Hit.reach };
            _cb[4 * kn] = all[j].X - e; _cb[4 * kn + 1] = all[j].Y - e; _cb[4 * kn + 2] = all[j].X + e; _cb[4 * kn + 3] = all[j].Y + e;
            int o = kn;   // Id 차례로 끼운다
            while (o > 0 && _imem[_iord[o - 1]] > j) { _iord[o] = _iord[o - 1]; o--; }
            _iord[o] = kn;
            _ipairs.Add(i < j ? (k, kn) : (kn, k));
        }
        _cb[4 * k] = Math.Min(_cb[4 * k], x0); _cb[4 * k + 1] = Math.Min(_cb[4 * k + 1], y0); _cb[4 * k + 2] = Math.Max(_cb[4 * k + 2], x1); _cb[4 * k + 3] = Math.Max(_cb[4 * k + 3], y1);
    }

    /// <summary>섬 안 차례 a · b가 이미 짝인가 (짝은 수십 — 찾기는 드물다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool PairedIn(int a, int b)
    {
        foreach (var (x, y) in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_ipairs)) if (x == a && y == b || x == b && y == a) return true;
        return false;
    }

    /// <summary>섬 차례 (_mem[first ..]에서 그 목록 차례를 반으로 나눠 찾는다 — 차례대로 늘어섰다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private int Local(int first, int count, int i)
    {
        int lo = 0, hi = count - 1;
        while (lo < hi) { int mid = (lo + hi) >> 1; if (_mem[first + mid] < i) lo = mid + 1; else hi = mid; }
        return lo;
    }

    /// <summary>쉬는 짝이 닿을 때: 몸 값 (질량 · 회전관성 · 마찰 반지름)과 그 잔걸음 바닥 마찰 한도 (지금 칸 — 젖으면 × 0.33)만 채운다 (깨지는 않는다 — 한도 안의 밂은 바닥이 받는다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void Rest(ref PropRun r, ref Prop p, double h)
    {
        if (p.Kg <= 0) FillBody(ref p);
        r.M = p.Kg; r.In = p.Inertia; r.Rho = PropForm(p).Rho;
        int c = CellOf(p);
        double gp = c >= 0 ? Gravity * F.Grav[c] : 0, mu = c >= 0 ? MaterialOf(p).Mu * (F.Stored(T.sWater, c) > WetMol ? WetShare : 1) : 0;
        r.FloorL = gp * mu * r.M * h; r.Ffx = r.Ffy = r.Ffw = 0; r.Tvx = r.Tvy = r.Tw = 0;
    }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private MaterialDef MaterialOf(in Prop p) => T.Materials[Math.Min(MatOf(p), T.Materials.Count - 1)].Def;

    /// <summary>
    /// 잔걸음 처음 (물체 하나의 걸음 그대로): 덜컹 (그 때가 지났으면 바닥에 대한 빠르기) · 우주 · 구멍 · 바람 · 틀의 힘 · 제 잔걸음 길이 (r.H) — 걸음이 끝났으면 거짓.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private bool SubstepBegin(Sim sim, ref PropRun r, ref Prop p, int k, double tau, double rem, double dt)
    {
        var tr = Track;
        r.PreX = p.X; r.PreY = p.Y; r.PreTh = p.Th; r.PreVx = p.Vx; r.PreVy = p.Vy; r.PreW = p.W;   // 잔걸음 처음 (틱 안 기록 — 튀기 앞)
        int host = r.Host;
        for (; r.Jp < _jolts.Count && _jolts[r.Jp].T <= tau + 1e-12; r.Jp++)
        {
            var j = _jolts[r.Jp];
            if (j.B != host) continue;
            var o = Bodies[host];
            double jdx = p.X - o.Lx, jdy = p.Y - o.Ly;
            p.Vx -= j.Dvx - j.Dw * jdy; p.Vy -= j.Dvy + j.Dw * jdx; p.W -= j.Dw; r.Jw += j.Dw; p.Still = 0;
            if (j.Cause != 0) p.Cause = j.Cause;
        }
        r.NextJolt = double.PositiveInfinity;
        for (int q = r.Jp; q < _jolts.Count; q++) if (_jolts[q].B == host) { r.NextJolt = _jolts[q].T; break; }
        int c = CellOf(p);
        r.Cell = c;
        // 무게중심이 도면 밖 · 바깥에 들었다 → 우주로 · 구멍 벽 칸에 들었다 (큰 구멍의 틈) → 빨아들이면 막거나 우주로 (아니면 틈을 지나 그대로 간다)
        if (c < 0 || L.Kind[c] == CellKind.Outside || F.IsOut(c)) { HoleHit(sim, ref p, -1); TrackEnd(tr, p, tau, ref r.LastKey); r.Done = true; return false; }
        if (L.Kind[c] == CellKind.Wall && F.Hole[c] > 0.01f && HoleHit(sim, ref p, c)) { TrackEnd(tr, p, tau, ref r.LastKey); r.Done = true; return false; }
        double m = r.M, I = r.In, rho = r.Rho;
        var sm = r.Sm;
        double gp = Gravity * F.Grav[c];   // 누르는 힘 N = m g' (무중력이면 정말 뜬다)
        double muK = r.Mat.Mu * (F.Stored(T.sWater, c) > WetMol ? WetShare : 1);   // 칸 값으로 늘 다시 읽는다 (젖으면 × 0.33)
        // 높이 (PH7): 받침 · 떨어짐 · 떠 있음 (떠 있으면 바닥이 누르지 않는다 — 마찰 · 붙듦 · 잠 없음 · 닿는 때로 끊는다) · 덜컹에 넘어짐 · 구름
        double hz = rem;
        r.Air = HeightBegin(sim, ref r, ref p, c, tau, ref hz);
        if (r.Air) gp = 0;
        if (PropDetail > 0 && !r.Air && p.State == 1 && p.Pose == 0 && r.Gz > TipG * Gravity && (p.Vx != r.PreVx || p.Vy != r.PreVy) && r.PreVx == 0 && r.PreVy == 0 && r.PreW == 0)
        {
            // 덜컹 문턱 (놓여 있던 것이 바닥이 바뀐 몫 Δv를 받았다): ω₀ = m Δv Hc / Iₚ · ½ Iₚ ω₀² ≥ m g' (R − Hc)면 넘어진다 (받침 모서리가 붙든다 — 평면 빠르기는 0)
            int js = TipSide(p.Th, p.Vx, p.Vy, out double jv);
            var (_, jhc, jR, _, jip) = Pivot(r.Fm, js, m);
            double w0 = m * jv * jhc / jip;
            if (0.5 * jip * w0 * w0 >= m * r.Gz * (jR - jhc)) StartTip(ref p, js, w0);
        }
        r.Roll = !r.Air && p.State != 2 && Roller(p, r.Fm, out r.RAx, out r.RAy);
        r.Radius = r.Roll ? r.Fm.Hc : 0;
        double v = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy), w = Math.Abs(p.W), speed = v + w * sm.reach;
        // 바람 · 구멍 입 · 몸의 틀 (−a · 원심 · 오일러 — 잔걸음 처음 때 · 코리올리는 빠르기를 돌린다) — 놓인 채 정지 타원 안이면 그대로
        double wfx = 0, wfy = 0, wtq = 0, wb = 0, ffx = 0, ffy = 0, ftq = 0;
        bool windy = _windy && WindForce(p, r.Fm, tau, out wfx, out wfy, out wtq);
        bool framed = _framed && FrameForce(p, host, tau, r.Jw, m, I, out ffx, out ffy, out ftq, out wb);
        if (p.State == 4 && AirForce(p, r.Fm, c, out double afx, out double afy)) { wfx += afx; wfy += afy; windy = true; }   // 떠 있으면 공조 바람에 흘러간다
        double fx = wfx + ffx, fy = wfy + ffy, tq = wtq + ftq;
        bool blown = windy || framed;
        if (p.Cause == 0 && (windy || framed)) p.Cause = windy ? WindCauseAt(c) : host >= 0 ? BodyCause(c) : ShipACause;   // 작은 덜컹에 깬 것도 미는 힘의 원인을 잇는다
        // 넘어짐 (§3-4): 놓인 채 선 꼴이 옆 힘에 F h_F > m g' b이고 F < μs N이면 모서리를 축으로 (아니면 미끄러진다 — 다만 μk h_F > b면 미끄러지는 밑이 걸려 넘어진다) · 넘어지는 중이면 평면은 받침이 붙든다
        if (blown && PropDetail > 0 && !r.Air && p.State == 1 && p.Pose == 0 && p.Vx == 0 && p.Vy == 0 && p.W == 0 && r.Gz > TipG * Gravity && Tips(r.Fm, p.Th, m, r.Gz, wfx, wfy, ffx, ffy, out int ts, out double tsh)
            && (fx * fx + fy * fy < StaticShare * muK * m * r.Gz * (StaticShare * muK * m * r.Gz) || muK * tsh > r.Fm.TipB[ts]))   // 미끄러질 만해도 바닥 마찰 μk가 b/h_F를 넘으면 밑이 걸려 넘어진다
            StartTip(ref p, ts, 0);
        if (p.State == 2)
        {
            var (tdx, tdy) = SideDir(p.Th, p.TiltDir);
            Tips(r.Fm, p.Th, m, r.Gz, wfx, wfy, ffx, ffy, out _, out double thF);
            r.TipFa = fx * tdx + fy * tdy; r.TipHf = thF;
            double ht = Math.Min(rem, TipH);
            if (r.NextJolt - tau < ht) ht = Math.Max(1e-9, r.NextJolt - tau);
            r.Steps++;
            r.H = ht; r.Gp = 0; r.MuK = muK; r.Room = ClearAt(c) - sm.reach; r.Speed = 0;
            r.Fx = r.Fy = r.Tq = r.Wfx = r.Wfy = r.Wtq = r.Wb = r.Ax = r.Ay = r.Al = r.Acc = 0;
            r.Windy = r.Framed = r.Blown = false; r.Roll = false;
            return true;
        }
        if (r.Roll && blown)
        {
            // 구르는 것: 구르는 쪽 힘은 구름 저항 (μr N)만 붙든다 · 축 쪽은 바닥 정지 마찰 · 구르는 쪽 가속은 구르는 관성 1.5m으로 나눈다 (아래)
            double fu = fx * r.RAx + fy * r.RAy, fq = -fx * r.RAy + fy * r.RAx;
            if (p.Vx == 0 && p.Vy == 0 && p.W == 0 && Math.Abs(fq) <= RollMu * m * gp && Holds(fu * r.RAx, fu * r.RAy, tq, StaticShare * muK, m * gp, rho)) { blown = windy = false; fx = fy = tq = wfx = wfy = wtq = 0; }
        }
        else if (blown && p.Vx == 0 && p.Vy == 0 && p.W == 0 && Holds(fx, fy, tq, StaticShare * muK, m * gp, rho)) { blown = windy = false; fx = fy = tq = wfx = wfy = wtq = 0; }
        double ax = fx / m, ay = fy / m, al = tq / I;
        if (r.Roll) { double au = ax * r.RAx + ay * r.RAy, aq = (-ax * r.RAy + ay * r.RAx) / RollMass; ax = au * r.RAx - aq * r.RAy; ay = au * r.RAy + aq * r.RAx; }
        double acc = blown ? Math.Sqrt(ax * ax + ay * ay) + Math.Abs(al) * sm.reach : 0;
        double room = ClearAt(c) - sm.reach, gap = room, h = rem;
        int nNear = _nearCount[k];
        double hAct = double.PositiveInfinity;
        for (int q = 0; q < nNear; q++)   // 원판도 건너뛰지 않게 (닿을 만큼만 간다 · 걷는 원판은 서로 다가오는 빠르기로)
        {
            ref var a = ref _actors[_runNear[4 * k + q]];
            if (a.PathN < 2)
            {
                double dx = L.X(a.Cell) + 0.5 - p.X, dy = L.Y(a.Cell) + 0.5 - p.Y;
                gap = Math.Min(gap, Math.Sqrt(dx * dx + dy * dy) - sm.reach - a.R);
                continue;
            }
            ActorAt(a, tau, dt, out double dxA, out double dyA, out double avx, out double avy);
            double ex = dxA - p.X, ey = dyA - p.Y, close = speed + Math.Sqrt(avx * avx + avy * avy);
            if (close > 0) hAct = Math.Min(hAct, Math.Max(2 * DotForm.Dot, Math.Sqrt(ex * ex + ey * ey) - sm.reach - a.R) / close);
        }
        double ell = Math.Max(2 * DotForm.Dot, gap);
        if (hAct < h) h = hAct;
        if (speed > 0) h = Math.Min(h, ell / speed);
        if (w > 0) h = Math.Min(h, MaxPropTurn / w);
        if (acc > 0) h = Math.Min(h, 2 * ell / (speed + Math.Sqrt(speed * speed + 2 * acc * ell)));   // 힘을 받아 빨라져도 ℓ 넘게 가지 않는다 (아주 작은 힘에도 0이 되지 않는 꼴)
        if (blown && al != 0) h = Math.Min(h, 2 * MaxPropTurn / (w + Math.Sqrt(w * w + 2 * Math.Abs(al) * MaxPropTurn)));
        // 놓인 채 붙들린 것 (빠르기 0 · 정지 타원이 지금 바람 · 틀의 힘과 틱 끝 틀의 힘을 다 붙든다 — 바람은 틱 안에서 줄기만 한다)은 0.1초씩 끊지 않는다 (걷는 이 곁에서 깨어 있는 것 —
        // 걷는 원판 · 짝 · 덜컹이 다가오는 때까지만 간다)
        bool held = speed == 0 && !blown && (!framed || !FrameForce(p, host, dt, r.Jw, m, I, out double efx, out double efy, out double etq, out _) ||
            Holds(efx, efy, etq, StaticShare * muK, m * gp, rho) && Holds(wfx + efx, wfy + efy, wtq + etq, StaticShare * muK, m * gp, rho));
        if ((speed > 0 || blown) && gp > 0 && muK > 0 || (_windy || framed) && !held) h = Math.Min(h, MaxPropH);   // 바람 · 틀의 힘은 틱 안에서 준다
        if (r.NextJolt - tau < h) h = Math.Max(1e-9, r.NextJolt - tau);   // 덜컹 때에 맞춰 끊는다
        if (r.Air && r.Hz < h) h = r.Hz;   // 받침 · 바닥 · 천장에 닿는 때 (정확히)
        r.Steps++;
        r.H = h; r.Gp = gp; r.MuK = muK; r.Room = room; r.Speed = speed;
        r.Fx = fx; r.Fy = fy; r.Tq = tq; r.Wfx = wfx; r.Wfy = wfy; r.Wtq = wtq; r.Wb = wb; r.Ax = ax; r.Ay = ay; r.Al = al; r.Acc = acc;
        r.Windy = windy; r.Framed = framed; r.Blown = blown;
        return true;
    }

    /// <summary>잔걸음 끝 (물체 하나의 걸음 그대로): 장부 · 바로잡음 (정적 · 짝) · 원판 맞음 · 틱 안 기록 · 구멍 · 잠 (잠들면 쉬는 짝으로 섬에 남는다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void SubstepEnd(Sim sim, ref PropRun r, ref Prop p, int k, double tau, double rem, double h, double dt)
    {
        var tr = Track;
        double m = r.M, I = r.In;
        int host = r.Host;
        // 높이 (PH7): 넘어지는 중 (평면은 받침이 붙든다) · 떨어지는 · 떠 있는 것의 오르내림 · 닿음
        if (p.State == 2) TipStep(sim, ref r, ref p, h, dt - rem);
        else if (r.Air) HeightEnd(sim, ref r, ref p, dt - rem - h, h);
        if (p.State == 1 && !r.Air && r.Gp > 0 && p.Kind is 0 or 2) { double v2 = p.Vx * p.Vx + p.Vy * p.Vy; if (v2 > r.SlideV2) { r.SlideV2 = v2; r.SlideSub = sim.Facts.SubNow; } }
        if (p.State == 4 && r.Ns > 0)
        {
            // 무중력: 평면 부딪힘 (벽 · 가구)의 0.2|Δv|를 오르내림으로 (쪽은 Id · 틱 · 잔걸음으로 정한다 — 벽이 평면에 꼭 수직이지 않은 흩어짐 어림)
            // 에너지는 늘지 않는다: 오르내림이 얻은 몫만큼 평면 빠르기 · 돎을 같은 비로 줄인다
            double dv = 0;
            for (int c = r.S0; c < r.S0 + r.Ns; c++) if (_ic[c].Jn > 0) dv += _ic[c].Jn / m;
            if (dv > 0)
            {
                double vz1 = p.Vz + ((DotHash(p.Id, (int)(sim.Tick * 64 + r.Steps)) & 1) == 0 ? AirToVz * dv : -AirToVz * dv);
                double ep = 0.5 * m * (p.Vx * p.Vx + p.Vy * p.Vy) + 0.5 * I * p.W * p.W, de = 0.5 * m * (vz1 * vz1 - (double)p.Vz * p.Vz);
                if (de <= 0 || ep > de)
                {
                    double kv = de > 0 ? Math.Sqrt(1 - de / ep) : 1;
                    p.Vx *= kv; p.Vy *= kv; p.W *= kv; p.Vz = (float)vz1;
                }
            }
        }
        // 장부: 바닥 · 벽 · 가구가 받은 몫 — 몸이 없는 배에 준 몫 (바람 · 점화의 틀 힘이 준 몫은 뺀다 · 물체끼리 준 몫은 짝이 서로 지운다) · 몸이 있으면 틱 끝에 그 몸이 되받는다 (바람 몫은 기체가 잃는다)
        if (host < 0) { ToHostPx -= m * p.Vx - r.Px0 - r.Fx * h; ToHostPy -= m * p.Vy - r.Py0 - r.Fy * h; ToHostL -= m * (p.X * p.Vy - p.Y * p.Vx) + I * p.W - r.L0 - r.Lw; }
        else if (r.Windy) WindToWorld(p, host, tau, r.Wfx, r.Wfy, r.Wtq, h);
        if (r.Ns > 0 || r.Paired) FixIsland(ref p, k, m);
        var sm = r.Sm;
        int nNear = _nearCount[k];
        for (int q = 0; q < nNear; q++)
        {
            int a = _runNear[4 * k + q];
            ActorAt(_actors[a], dt - rem, dt, out double ax, out double ay, out double avx, out double avy);
            bool skip = DropperOf(p, _actors[a]);
            bool now = !skip && TouchesActor(p, sm, ax, ay, _actors[a].R);
            if (now && !_runTouch[4 * k + q]) ActorHit(sim, p, _actors[a], p.Vx - avx, p.Vy - avy);
            _runTouch[4 * k + q] = !skip && (now || _runTouch[4 * k + q] && DiscSeparation(p, r.Sh, ax, ay, _actors[a].R, out _, out _, out _, out _) <= 2 * DotForm.Dot);   // 맞은 뒤 붙어 있는 동안은 새로 맞은 것이 아니다
        }
        if (tr != null)
        {
            double t1 = dt - rem;
            bool bounced = false;
            for (int c = 0; c < _nic && !bounced; c++) if ((_ic[c].A == k || _ic[c].B == k) && _ic[c].Bounce && _ic[c].Jn > 0) bounced = true;
            if (bounced)
            {
                if (tau > r.LastKey + 1e-9) tr.Add(MotionTrack.WhoProp, p.Id, tau, r.PreX, r.PreY, r.PreTh, r.PreVx, r.PreVy, r.PreW, MotionTrack.EvNone, p.Z, p.Vz);   // 튀기 앞 (그 잔걸음은 튄 빠르기로 곧게 갔다)
                tr.Add(MotionTrack.WhoProp, p.Id, t1, p.X, p.Y, p.Th, p.Vx, p.Vy, p.W, MotionTrack.EvHit, p.Z, p.Vz); r.LastKey = t1;
            }
            else
            {
                if (r.StopAt >= 0) { tr.Add(MotionTrack.WhoProp, p.Id, tau + r.StopAt, p.X, p.Y, p.Th, 0, 0, p.W, MotionTrack.EvStop, p.Z, p.Vz); r.LastKey = tau + r.StopAt; }
                if (Math.Floor(t1 * 16 / dt + 1e-9) > Math.Floor(tau * 16 / dt + 1e-9) && t1 > r.LastKey + 1e-9) { tr.Add(MotionTrack.WhoProp, p.Id, t1, p.X, p.Y, p.Th, p.Vx, p.Vy, p.W, MotionTrack.EvNone, p.Z, p.Vz); r.LastKey = t1; }
            }
        }
        // 구멍 벽에 닿았다 (밀어 닿았거나 겹침) → 빨아들이는 구멍이면 막거나 우주로 (아니면 보통 벽 — 이미 튀었다)
        bool gone = false;
        for (int c = r.S0; c < r.S0 + r.Ns && !gone; c++)
        {
            ref var ct = ref _ic[c];
            if (ct.Cell >= 0 && L.Kind[ct.Cell] == CellKind.Wall && F.Hole[ct.Cell] > 0.01f && (ct.Jn > 0 || ct.Sep <= Slop)) gone = HoleHit(sim, ref p, ct.Cell);
        }
        if (gone) { TrackEnd(tr, p, dt - rem, ref r.LastKey); r.Done = true; return; }
        bool still = r.Gp > 0 && !r.Air && p.State == 1 && p.Vx * p.Vx + p.Vy * p.Vy < SleepV * SleepV && Math.Abs(p.W) < SleepW && p.Tilt == 0 && double.IsPositiveInfinity(r.NextJolt) && !r.Walked;   // 걷는 원판이 아직 지나가는 틱이면 자지 않는다 (깨운 까닭)
        p.Still = still ? p.Still + (float)h : 0;
        if (p.Still >= SleepSec)
        {
            p.Vx = p.Vy = p.W = 0; p.State = 0; p.Asleep = 1; p.Still = 0; p.Since = _now;
            TrackEnd(tr, p, dt - rem, ref r.LastKey);
            r.Active = false;   // 쉬는 짝으로 섬에 남는다 (다시 맞으면 깬다)
        }
    }

    /// <summary>
    /// 지난 잔걸음 충격량 (길이 비로 줄여)으로 먼저 준다 (따뜻한 시작 — 열쇠가 같은 닿음만: 짝은 (작은 Id, 큰 Id, 변 · 점) · 벽 · 가구는 (Id, 0, 표본 사분면)) —
    /// 물체끼리 닿는 잔걸음에만 (구석에 쌓인 더미를 벽까지 몇 번의 되풀이로 전하지 못한다 · 혼자인 것은 PH4 그대로).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void WarmStart(Span<Prop> all, double h)
    {
        if (_wkN == 0 || _warmH <= 0) return;
        double scale = h / _warmH;
        for (int c = 0; c < _nic; c++)
        {
            ref var ct = ref _ic[c];
            ref var a = ref all[_runs[ct.A].I];
            long key = WarmKey(a.Id, ct.B >= 0 ? all[_runs[ct.B].I].Id : 0, ct.Key);
            int at = c < _wkN && _wkKey[c] == key ? c : -1;
            for (int q = 0; q < _wkN && at < 0; q++) if (_wkKey[q] == key) at = q;
            if (at < 0) continue;
            ct.Jn = _wkJn[at] * scale; ct.Jt = _wkJt[at] * scale;
            double tx = -ct.Ny, ty = ct.Nx, px = ct.Jn * ct.Nx + ct.Jt * tx, py = ct.Jn * ct.Ny + ct.Jt * ty;
            if (ct.B >= 0) ApplyPair(ref a, ref all[_runs[ct.B].I], ref _runs[ct.A], ref _runs[ct.B], in ct, px, py);
            else { ref var r = ref _runs[ct.A]; a.Vx += px / r.M; a.Vy += py / r.M; a.W += (ct.Rx * py - ct.Ry * px) / r.In; }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void KeepWarm(Span<Prop> all, double h)
    {
        if (_wkKeyNext.Length < _nic) { _wkKeyNext = new long[2 * _nic]; _wkJnNext = new double[2 * _nic]; _wkJtNext = new double[2 * _nic]; }
        for (int c = 0; c < _nic; c++)
        {
            ref var ct = ref _ic[c];
            _wkKeyNext[c] = WarmKey(all[_runs[ct.A].I].Id, ct.B >= 0 ? all[_runs[ct.B].I].Id : 0, ct.Key); _wkJnNext[c] = ct.Jn; _wkJtNext[c] = ct.Jt;
        }
        (_wkKey, _wkKeyNext) = (_wkKeyNext, _wkKey); (_wkJn, _wkJnNext) = (_wkJnNext, _wkJn); (_wkJt, _wkJtNext) = (_wkJtNext, _wkJt);
        _wkN = _nic; _warmH = h;
    }

    /// <summary>닿음 열쇠: 작은 Id (24비트 위) · 큰 Id (벽 · 가구면 0 — 물체 Id는 1부터) · 닿음 (16비트 — 짝: 뒤집힘 · 기준 변 · 맞은 변 · 잘린 끝 / 가운데 점: + b 기준 변 / 벽: 표본 사분면).</summary>
    private static long WarmKey(int a, int b, int key) => (long)a << 40 | (long)b << 16 | (uint)key;

    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static void ApplyPair(ref Prop a, ref Prop b, ref PropRun ra, ref PropRun rb, in IslandContact ct, double px, double py)
    {
        a.Vx += px / ra.M; a.Vy += py / ra.M; a.W += (ct.Rx * py - ct.Ry * px) / ra.In;
        b.Vx -= px / rb.M; b.Vy -= py / rb.M; b.W -= (ct.Qx * py - ct.Qy * px) / rb.In;
    }

    /// <summary>
    /// 순차 충격량 (Catto — 되풀이마다 바닥 마찰 (물체마다 · Goyal–Ruina 타원: 쌓인 충격량 (P, Pω) — (|P|/L)² + (Pω/ρL)² ≤ 1 · L = μk m g' h — 세우는 데 드는 만큼 쌓고 타원 밖이면 줄인다) →
    /// 닿음 차례대로 마찰 먼저 (지난 되풀이의 jn으로 한계) → 법선 누적 jn ≥ 0) — 정적 닿음은 그 물체만 · 짝은 둘이 서로 반대로.
    /// 바닥 마찰을 같은 고리에서 풀어 눌린 것끼리 서로 비트는 몫도 정지 마찰이 붙든다 (먼저 빼고 나중에 밀면 잔걸음마다 조금씩 돈다).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void SolveIsland(Span<Prop> all, int count, int iters)
    {
        for (int it = 0; it < iters; it++)
        {
            for (int k = 0; k < count; k++)
            {
                ref var r = ref _runs[k];
                if (r.FloorL <= 0 || r.Done) continue;
                ref var p = ref all[r.I];
                double L = r.FloorL, rl = r.Rho * L;
                double px = r.Ffx - r.M * p.Vx, py = r.Ffy - r.M * p.Vy, pw = r.Ffw - r.In * p.W;
                double q = (px * px + py * py) / (L * L) + (rl > 0 ? pw * pw / (rl * rl) : 0);
                if (rl <= 0) pw = r.Ffw;
                if (q > 1) { double f = 1 / Math.Sqrt(q); px *= f; py *= f; pw *= f; }
                p.Vx += (px - r.Ffx) / r.M; p.Vy += (py - r.Ffy) / r.M; p.W += (pw - r.Ffw) / r.In;
                r.Ffx = px; r.Ffy = py; r.Ffw = pw;
            }
            for (int c = 0; c < _nic; c++)
            {
                ref var ct = ref _ic[c];
                ref var ra = ref _runs[ct.A];
                ref var p = ref all[ra.I];
                double tx = -ct.Ny, ty = ct.Nx;
                if (ct.B < 0)
                {
                    double m = ra.M, I = ra.In;
                    double vx = p.Vx - p.W * ct.Ry - ct.Ux, vy = p.Vy + p.W * ct.Rx - ct.Uy;
                    double vt = vx * tx + vy * ty, lim = ct.Mu * ct.Jn;
                    double jt = Math.Clamp(ct.Jt - vt / ct.Kt, -lim, lim), djt = jt - ct.Jt;
                    ct.Jt = jt;
                    p.Vx += djt * tx / m; p.Vy += djt * ty / m; p.W += djt * (ct.Rx * ty - ct.Ry * tx) / I;
                    vx = p.Vx - p.W * ct.Ry - ct.Ux; vy = p.Vy + p.W * ct.Rx - ct.Uy;
                    double vn = vx * ct.Nx + vy * ct.Ny;
                    double jn = Math.Min(ct.Cap, Math.Max(0, ct.Jn + (ct.Target - vn) / ct.Kn)), dj = jn - ct.Jn;
                    ct.Jn = jn;
                    p.Vx += dj * ct.Nx / m; p.Vy += dj * ct.Ny / m; p.W += dj * (ct.Rx * ct.Ny - ct.Ry * ct.Nx) / I;
                }
                else
                {
                    ref var rb = ref _runs[ct.B];
                    ref var q = ref all[rb.I];
                    double vx = p.Vx - p.W * ct.Ry - (q.Vx - q.W * ct.Qy), vy = p.Vy + p.W * ct.Rx - (q.Vy + q.W * ct.Qx);
                    double vt = vx * tx + vy * ty, lim = ct.Mu * ct.Jn;
                    double jt = Math.Clamp(ct.Jt - vt / ct.Kt, -lim, lim), djt = jt - ct.Jt;
                    ct.Jt = jt;
                    ApplyPair(ref p, ref q, ref ra, ref rb, in ct, djt * tx, djt * ty);
                    vx = p.Vx - p.W * ct.Ry - (q.Vx - q.W * ct.Qy); vy = p.Vy + p.W * ct.Rx - (q.Vy + q.W * ct.Qx);
                    double vn = vx * ct.Nx + vy * ct.Ny;
                    double jn = Math.Max(0, ct.Jn + (ct.Target - vn) / ct.Kn), dj = jn - ct.Jn;
                    ct.Jn = jn;
                    ApplyPair(ref p, ref q, ref ra, ref rb, in ct, dj * ct.Nx, dj * ct.Ny);
                }
            }
        }
    }

    /// <summary>
    /// 자리 바로잡음 (그 물체의 닿음 — 정적 · 짝): 겹침이 0.5도트 넘는 몫의 20%를 법선 쪽으로 (짝은 질량비로 나눠 반대로 — split impulse: 빠르기는 그대로 · 밀어 떼지 않는다) ·
    /// 닿음 무리마다 (벽 · 가구 한 무리 · 짝마다 한 무리 — 한 면의 두 점을 두 번 세지 않게) 축마다 가장 큰 몫 → 무리를 더한다 (양옆에 끼인 것은 양쪽에서 민다) · 바뀐 각운동량은 FixL.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void FixIsland(ref Prop p, int k, double m)
    {
        double tx = 0, ty = 0, sx = 0, sy = 0;
        int group = int.MinValue;
        for (int c = 0; c < _nic; c++)
        {
            ref var ct = ref _ic[c];
            if (ct.A != k && ct.B != k || ct.Sep >= -Slop || ct.Cell <= -2) continue;   // 원판은 자리로 떼지 않는다 (발밑의 것)
            int g = ct.B < 0 ? -1 : ct.A == k ? ct.B : ct.A;
            if (g != group) { tx += sx; ty += sy; sx = sy = 0; group = g; }
            double d = FixShare * (-ct.Sep - Slop);
            if (ct.B >= 0)
            {
                double wa = 1 / _runs[ct.A].M, wb = 1 / _runs[ct.B].M;
                d *= ct.A == k ? wa / (wa + wb) : -wb / (wa + wb);
            }
            if (Math.Abs(ct.Nx * d) > Math.Abs(sx)) sx = ct.Nx * d;
            if (Math.Abs(ct.Ny * d) > Math.Abs(sy)) sy = ct.Ny * d;
        }
        tx += sx; ty += sy;
        if (tx == 0 && ty == 0) return;
        FixL += m * (tx * p.Vy - ty * p.Vx);
        p.X += tx; p.Y += ty;
    }
}
