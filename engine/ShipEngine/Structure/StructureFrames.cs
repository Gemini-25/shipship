namespace ShipEngine;

/// <summary>떨어져 나간 조각 하나 (배가 갈라진 자리 · 그때 · 질량 · 멀어지는 빠르기).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct Fragment
{
    public int Id;          // 1부터 (0은 배)
    public int Cause;       // '배가 갈라졌다' 원인 마디
    public long Tick;
    public float MassT;     // 톤
    public int Cells;
    public float Speed;     // m/s — 갈라질 때의 충격량 ÷ 질량
    public float Dist;      // 지금 떨어진 거리 (m)
    public int Rooms;       // 조각 안의 방 수
    public int CutCause;    // '조각이 떨어져 나가 공기 · 전력이 끊겼다' (⑦ 생명이 그 안의 숨 · 추위에 잇는다)
}

/// <summary>
/// ④ 구조 — 골조 그물 · 조각 · 바닥 판 · 설비 · 문의 열 끼임 · 점화의 단면 하중 (깊이 바퀴 2 · docs/ENGINE.md ④ '어디든 부서진다 · 배가 갈라진다').
/// <para>
/// 구조가 남은 칸(벽 · 문 · 바닥 — 통째로 나간 벽(구멍 2㎡ 넘게) · 바닥 판이 다 나간 칸 · 떨어져 나간 칸은 빠진다)을 이웃으로 잇는 그물.
/// 그물이 끊기면 가장 무거운 덩어리가 배이고 · 나머지는 떨어져 나간 조각 — 둘 사이의 부서진 칸들은 우주가 된다(③ Sever) · 2톤 아래 덩어리는 날아가는 파편.
/// </para>
/// </summary>
public sealed partial class Structure
{
    /// <summary>바닥 판이 다 나간 넓이 (m² — 바닥 · 천장 둘 다) · 날아가는 파편으로 칠 덩어리 (kg).</summary>
    public const float DeckGone = 1.5f;
    /// <summary>바닥 · 천장 판이 버티는 압력 (kPa — 벽 칸보다 넓은 판이라 외판 400보다 낮게 · 우주선 압력 외판의 설계 파괴 압력 2~3기압 언저리).</summary>
    public const double DeckRatingKPa = 300;
    public const double DebrisKg = 2000;
    /// <summary>문틀과 문 사이의 틈 (m) — 달군 문틀이 이만큼 늘어나면 끼인다.</summary>
    public const double DoorGapM = 0.004;
    /// <summary>폭발 압력이 설비를 상하게 하기 시작하는 넘친 압력 (kPa — 폭풍 피해 표: 35kPa 장비 크게 상함 · 70kPa 무너짐).</summary>
    public const double BlastHarmKPa = 35;

    // ── 상태 (찍는다) ──
    /// <summary>칸이 속한 조각 (0: 배 · 1~: 떨어져 나간 조각 번호).</summary>
    public int[] Frag;
    public readonly List<Fragment> Fragments = new();
    /// <summary>배가 갈라질 때마다 오른다 (위층 캐시용).</summary>
    public int SplitVersion;
    public long DeckBursts, Smashed, Splits, DebrisLost, SectionFails;
    /// <summary>설비마다 상한 정도 (1이면 부서진다) · 부서졌다 · 그 원인.</summary>
    public float[] FurnHit;
    public byte[] FurnBroken;
    public int[] FurnCause;
    /// <summary>문마다: 1이면 열 끼임 (식으면 풀린다).</summary>
    public byte[] ThermalJam;
    /// <summary>칸마다 지난 틱 기압 (kPa — 폭발 압력이 오른 몫을 잰다).</summary>
    private float[] _pPrev;
    private bool _latticeDirty;

    // ── 도면에서 (찍지 않는다) ──
    private float[] _mass = Array.Empty<float>(), _area = Array.Empty<float>();
    private int[] _comp = Array.Empty<int>(), _initComp = Array.Empty<int>();
    private int _mainInit = -1;
    private float[] _thrustSig = Array.Empty<float>();   // 칸마다 점화 1g의 단면 응력 (MPa)
    private int _thrustAxis = -1, _thrustLattice = -1, _latticeVersion;
    private long _thrustAt = long.MinValue;

    private void BuildFrames()
    {
        _mass = new float[L.N]; _area = new float[L.N];
        for (int c = 0; c < L.N; c++)
        {
            var m = T.Materials[L.Material[c]].Def;
            // 장(③)의 고체 열용량과 같은 부피: 벽 7.5cm(판 둘 + 골조 — 1m × 2.5m) · 바닥 2cm(바닥 · 천장) · 문 1.5cm
            (double vol, double area) = L.Kind[c] switch { CellKind.Wall => (0.075, 0.03), CellKind.Floor => (0.02, 0.02), CellKind.Door => (0.015, 0.006), _ => (0, 0) };
            _mass[c] = (float)(m.Density * vol); _area[c] = (float)area;
        }
        foreach (var fu in L.Furniture)
        {
            if (fu.Cells.Length == 0) continue;
            double kg = T.Equip(fu.Type).Parts.Values.Sum();
            foreach (int c in fu.Cells) _mass[c] += (float)(kg / fu.Cells.Length);
        }
        _comp = new int[L.N];
        int n = Label(_comp, out var masses);
        _initComp = (int[])_comp.Clone();
        _mainInit = n > 0 ? Array.IndexOf(masses, masses.Max()) : -1;
        _thrustSig = new float[L.N];
    }

    /// <summary>구조가 남은 칸인가 (그물의 마디).</summary>
    public bool Solid(int c) => L.Kind[c] switch
    {
        CellKind.Wall => F.Hole[c] < 2 && F.Severed[c] == 0,
        CellKind.Floor => F.Hole[c] < DeckGone && F.Severed[c] == 0,
        CellKind.Door => F.Hole[c] < 2 && F.Severed[c] == 0,
        _ => false,
    };

    public bool InMain(int c) => c >= 0 && c < L.N && Frag[c] == 0 && F.Severed[c] == 0;
    public int FragmentOf(int c) => c >= 0 && c < L.N ? Frag[c] : 0;
    public int FragmentCause(int c) { int k = FragmentOf(c); return k > 0 && k <= Fragments.Count ? Fragments[k - 1].Cause : 0; }
    public int FragmentCutCause(int c) { int k = FragmentOf(c); return k > 0 && k <= Fragments.Count ? Fragments[k - 1].CutCause : 0; }

    /// <summary>이 칸의 구조를 부순다 (요청 — 큰 충돌 · 다른 배와 부딪힘 · 시험): 벽 · 문은 통째로 · 바닥은 바닥 · 천장 판이 다 나간다 → 골조 그물을 다시 센다.</summary>
    public void Destroy(Sim sim, int c, int cause)
    {
        if (!Solid(c)) return;
        if (L.Kind[c] == CellKind.Floor) { F.SetHole(c, 2f); DamageCause[c] = cause; _latticeDirty = true; }
        else Open(sim, c, 2.5f, cause, FactKind.WallBreach, 0);
        _latticeDirty = true;
    }

    /// <summary>조각의 질량 (kg — 0이면 배): 구조가 남은 칸들의 판 · 골조 · 설비.</summary>
    public double FragmentMass(int k)
    {
        double m = 0;
        for (int c = 0; c < L.N; c++) if (Frag[c] == k && Solid(c)) m += _mass[c];
        return m;
    }

    /// <summary>덩어리 번호 매기기 (칸 순서로 — 결정론) · 덩어리마다 질량.</summary>
    private int Label(int[] comp, out double[] masses)
    {
        Array.Fill(comp, -1);
        var ms = new List<double>();
        var q = new Queue<int>();
        Span<int> nb = stackalloc int[4];
        for (int s = 0; s < L.N; s++)
        {
            if (comp[s] >= 0 || !Solid(s)) continue;
            int id = ms.Count; double m = 0;
            comp[s] = id; q.Enqueue(s);
            while (q.Count > 0)
            {
                int c = q.Dequeue(); m += _mass[c];
                L.Neighbors(c, nb);
                foreach (int j in nb) if (j >= 0 && comp[j] < 0 && Solid(j)) { comp[j] = id; q.Enqueue(j); }
            }
            ms.Add(m);
        }
        masses = ms.ToArray();
        return ms.Count;
    }

    /// <summary>그물이 바뀌었으면 (벽이 통째로 · 바닥 판이 다 나감 · 끊김) 덩어리를 다시 세어 새로 떨어져 나간 것을 가른다.</summary>
    private void Lattice(Sim sim)
    {
        if (!_latticeDirty) return;
        _latticeDirty = false;
        _latticeVersion++;
        int n = Label(_comp, out var masses);
        if (n <= 1) return;
        // 덩어리마다 주인 몸 (그 칸의 조각 번호 — 배는 처음 배 덩어리였던 칸만 · 처음부터 따로 떨어진 것은 없다)
        var owner = new int[n];
        Array.Fill(owner, -2);
        for (int c = 0; c < L.N; c++)
        {
            int k = _comp[c];
            if (k < 0 || owner[k] != -2) continue;
            int f = Frag[c];
            owner[k] = f < 0 || f == 0 && _initComp[c] != _mainInit ? -1 : f;
        }
        // 몸마다 가장 무거운 덩어리가 그 몸으로 남고 · 나머지는 그 몸에서 떨어져 나간 새 조각 (배도 조각도 — 부딪혀 또 갈라진다)
        var keep = new Dictionary<int, int>();
        for (int k = 0; k < n; k++)
        {
            int o = owner[k];
            if (o < 0) continue;
            if (!keep.TryGetValue(o, out int best) || masses[k] > masses[best]) keep[o] = k;
        }
        for (int k = 0; k < n; k++)
            if (owner[k] >= 0 && keep[owner[k]] != k) Detach(sim, k, masses[k], owner[k]);
    }

    private void Detach(Sim sim, int k, double massKg, int parent)
    {
        var cells = new List<int>();
        for (int c = 0; c < L.N; c++) if (_comp[c] == k) cells.Add(c);
        // 사이의 부서진 칸들 (구조가 없는 칸 — 이 덩어리와 다른 덩어리에 함께 닿은 무더기)을 찾는다 · 이 덩어리에만 닿은 무더기(통째로 뚫린 벽 · 다 나간 바닥)는 이 몸에 든다
        var gap = GapBetween(k, out var own);
        int cutCell = gap.Count > 0 ? gap[0] : cells[0];
        int parentCause = 0; long newest = long.MinValue;
        foreach (int g in gap) { int dc = DamageCause[g]; if (dc != 0 && sim.Causes[dc].Tick >= newest) { newest = sim.Causes[dc].Tick; parentCause = dc; } }
        // 갈라질 때의 충격량: 사이 칸들의 기압 × 면 × 0.05초 (폭발이 밀어낸다 · 어림) → 조각이 멀어지는 빠르기
        double impulse = 0;
        foreach (int g in gap) impulse += Math.Max(0, _p.Length > g ? _p[g] : 0) * 1000 * Field.WallFace * 0.05;
        if (massKg < DebrisKg)
        {
            // 작은 덩어리는 날아가는 파편이 된다 (그 칸들도 우주로)
            DebrisLost++;
            int dcause = sim.Causes.Add(CauseKind.Breach, "부서진 뼈대 조각이 떨어져 날아갔다", sim.Tick, cells[0], parentCause);
            foreach (int c in cells) { DamageCause[c] = dcause; F.Sever(c); Frag[c] = -1; }
            foreach (int c in own) { F.Sever(c); Frag[c] = -1; }
            if (parent < Bodies.Count) Refit(parent);
            foreach (int g in gap) F.Sever(g);
            sim.Facts.Emit(sim.Tick, FactKind.ObjectFlung, cells[0], (float)(massKg / 1000), dcause);
            _latticeDirty = true;
            return;
        }
        int id = Fragments.Count + 1;
        var rooms = new SortedSet<int>();
        foreach (int c in cells) if (L.Room[c] >= 0) rooms.Add(L.Room[c]);
        string names = string.Join(" · ", rooms.Take(2).Select(r => L.Rooms[r].Name)) + (rooms.Count > 2 ? " …" : "");
        string what = rooms.Count > 0 ? $"{names} 쪽이 떨어져 나갔다" : "뼈대 한쪽이 떨어져 나갔다";
        int cause = sim.Causes.Add(CauseKind.Breach, parent > 0 ? $"떨어져 나간 조각이 또 갈라졌다 — {what}" : $"배가 갈라졌다 — {what}", sim.Tick, cutCell, parentCause);
        foreach (int c in cells) Frag[c] = id;
        foreach (int c in own) Frag[c] = id;
        foreach (int g in gap) { DamageCause[g] = cause; F.Sever(g); Frag[g] = -1; }
        float speed = (float)Math.Clamp(impulse / massKg, 0.02, 3);
        int cut = rooms.Count > 0 ? sim.Causes.Add(CauseKind.Electric, "조각이 떨어져 나가 공기 · 전력이 끊겼다 — 산소 · 정화 · 난방이 멈췄다", sim.Tick, cutCell, cause) : 0;
        Fragments.Add(new Fragment { Id = id, Cause = cause, Tick = sim.Tick, MassT = (float)(massKg / 1000), Cells = cells.Count, Speed = speed, Rooms = rooms.Count, CutCause = cut });
        // ④ 칸 강체: 조각이 몸이 된다 — 갈라진 자리에서 배와 조각을 반대로 민다 (운동량 보존 · 사이 칸 기압이 밀어낸 만큼)
        SpawnBody(id, parent);
        if (gap.Count > 0)
        {
            double gx = 0, gy = 0;
            foreach (int g in gap) { gx += L.X(g) + 0.5; gy += L.Y(g) + 0.5; }
            gx /= gap.Count; gy /= gap.Count;
            var (wx, wy) = WorldOf(parent, gx, gy);
            var fb = Bodies[id];
            double dx = fb.X - wx, dy = fb.Y - wy, dl = Math.Sqrt(dx * dx + dy * dy);
            if (dl > 1e-9)
            {
                double j = speed * fb.Mass;
                Push(id, wx, wy, j * dx / dl, j * dy / dl, cause);
                Push(parent, wx, wy, -j * dx / dl, -j * dy / dl, cause);
            }
        }
        Splits++; SplitVersion++;
        sim.Facts.Emit(sim.Tick, FactKind.ShipSplit, cutCell, (float)(massKg / 1000), cause, -1, id);
        sim.Find<Sound>()?.Emit(sim, NearestAir(cutCell), 130, SoundKind.Bang, cause);
        _latticeDirty = true; // 끊긴 칸이 늘었다 — 다음 틱에 다시 센다 (더 갈라졌나)
    }

    /// <summary>이 덩어리와 다른 덩어리 사이의 부서진 칸들 (구조 없는 칸의 무더기 중 둘 이상의 덩어리에 닿은 것 · 칸 순서) · 이 덩어리에만 닿은 무더기(own).</summary>
    private List<int> GapBetween(int k, out List<int> own)
    {
        var result = new List<int>();
        own = new List<int>();
        var seen = new HashSet<int>();
        var q = new Queue<int>();
        Span<int> nb = stackalloc int[4];
        for (int s = 0; s < L.N; s++)
        {
            if (seen.Contains(s) || L.Kind[s] == CellKind.Outside || F.Severed[s] != 0 || Solid(s)) continue;
            // 이 덩어리에 닿은 무더기만
            L.Neighbors(s, nb);
            bool touches = false;
            foreach (int j in nb) if (j >= 0 && _comp[j] == k) touches = true;
            if (!touches) continue;
            var blob = new List<int>(); var comps = new HashSet<int>();
            seen.Add(s); q.Enqueue(s);
            while (q.Count > 0)
            {
                int c = q.Dequeue(); blob.Add(c);
                L.Neighbors(c, nb);
                foreach (int j in nb)
                {
                    if (j < 0) continue;
                    if (_comp[j] >= 0) { comps.Add(_comp[j]); continue; }
                    if (!seen.Contains(j) && L.Kind[j] != CellKind.Outside && F.Severed[j] == 0 && !Solid(j)) { seen.Add(j); q.Enqueue(j); }
                }
            }
            if (comps.Count >= 2) result.AddRange(blob);
            else own.AddRange(blob);
        }
        result.Sort(); own.Sort();
        return result;
    }

    // ───────────── 바닥 · 천장 판 · 설비 · 폭발 압력 ─────────────

    /// <summary>
    /// 바닥 칸 (매 틱 — 기압이 높은 칸만): 한 층짜리 배라 방의 바닥 · 천장도 압력 외판이다 (정격 = 외판).
    /// 폭발 압력이 정격을 넘으면 바닥 · 천장 판이 뚫려 우주로 · 1.6배면 다 나간다. 오른 압력이 35kPa를 넘으면 그 칸 설비가 상한다.
    /// </summary>
    private void Decks(Sim sim)
    {
        double pOut = F.Outside.KPa;
        for (int c = 0; c < L.N; c++)
        {
            if (L.Kind[c] != CellKind.Floor || F.Severed[c] != 0) { if (_pPrev.Length > c) _pPrev[c] = 0; continue; }
            float p = (float)_p[c];
            // 오른 몫: 지난 틱 기압(빈 방이 다시 차는 것은 폭발이 아니다 — 보통 기압보다 낮았으면 보통 기압)에서
            float rise = p - Math.Max(_pPrev[c], 101.3f);
            _pPrev[c] = p;
            if (rise > BlastHarmKPa && L.FurnitureAt[c] >= 0)
            {
                int cause = BlastCause(sim, c);
                HitFurniture(sim, L.FurnitureAt[c], (rise - BlastHarmKPa) / 70, cause, "폭발 압력에");
            }
            if (p < 200) continue;   // 빠른 거르기 (정격 300kPa의 2/3)
            var m = T.Materials[L.Material[c]].Def;
            double rating = DeckRatingKPa * Math.Sqrt(Math.Max(0.01, m.Strength / 250.0)) * HeatFactor(c, m) * (1 - Corrode[c]);
            double load = (p - pOut) / Math.Max(1, rating);
            if (load < 1) continue;
            float area = load >= 1.6 ? 2f : (float)(0.3 + 0.7 * (load - 1) / 0.6);
            if (area <= F.Hole[c]) continue;
            bool first = F.Hole[c] <= 0;
            int bc = BlastCause(sim, c);
            int cause2 = sim.Causes.Add(CauseKind.Breach, area >= DeckGone ? "폭발 압력에 바닥 · 천장 판이 통째로 날아갔다" : "폭발 압력에 바닥 · 천장 판이 뚫렸다", sim.Tick, c, bc);
            F.SetHole(c, area);
            DamageCause[c] = cause2;
            DeckBursts++;
            if (area >= DeckGone) _latticeDirty = true;
            if (first) { sim.Facts.Emit(sim.Tick, FactKind.WallBreach, c, area, cause2); sim.Find<Sound>()?.Emit(sim, c, 125, SoundKind.Bang, cause2); }
        }
    }

    /// <summary>폭발 압력의 원인: 그 칸 · 곁의 불 · 불씨 → 낮은 쪽 틈 → 압력 마디.</summary>
    private int BlastCause(Sim sim, int c)
    {
        int p = PressureCause(sim, c);
        return p != 0 ? p : sim.Causes.Add(CauseKind.Pressure, "기압이 갑자기 치솟았다", sim.Tick, c);
    }

    /// <summary>기압이 치솟은 까닭 (없으면 0): 그 칸 · 그 구역의 불 · 불씨 · 달군 것 → 그 칸을 친 충격 → 그 구역 틈(뚫린 벽 · 날아간 문)의 원인.</summary>
    public int PressureCause(Sim sim, int c)
    {
        _chem ??= sim.Find<Chemistry>();
        int r = F.RegionOf.Length > c ? F.RegionOf[c] : -1;
        if (_chem != null)
        {
            int h = _chem.HeatCauseAt(c);
            if (h != 0) return h;
            if (r >= 0) foreach (int x in F.Regions[r]) { int hx = _chem.HeatCauseAt(x); if (hx != 0 && _chem.Hrr[x] > 0) return hx; }
        }
        if (L.Kind[c] == CellKind.Floor && DamageCause[c] != 0) return DamageCause[c];
        if (r >= 0)
        {
            foreach (int x in F.Regions[r]) if (ImpactJ[x] > 0 && DamageCause[x] != 0) return DamageCause[x];
            foreach (var k in F.Links) if ((k.RA == r || k.RB == r) && k.Via >= 0 && DamageCause[k.Via] != 0) return DamageCause[k.Via];
        }
        return 0;
    }

    private Chemistry? _chem;

    /// <summary>설비 · 가구가 상한다 (파편 · 폭발 압력 · 날아온 물건): 1을 넘으면 부서진다 — 장치면 ⑥ 망이 읽어 부품이 망가진다.</summary>
    public void HitFurniture(Sim sim, int f, double harm, int cause, string how)
    {
        if (f < 0 || f >= FurnHit.Length || harm <= 0 || FurnBroken[f] != 0) return;
        FurnHit[f] = (float)Math.Min(2, FurnHit[f] + harm);
        if (FurnHit[f] < 1) return;
        FurnBroken[f] = 1; Smashed++;
        var fu = L.Furniture[f];
        int c = fu.Cells.Length > 0 ? fu.Cells[0] : -1;
        FurnCause[f] = sim.Causes.Add(CauseKind.Impact, $"{how} {Ko.I(T.Equip(fu.Type).Name)} 부서졌다", sim.Tick, c, cause);
        sim.Facts.Emit(sim.Tick, FactKind.MachineFail, c, FurnHit[f], FurnCause[f], -1, -1 - f);
    }

    // ───────────── 폭풍 (큰 충돌의 충격파) ─────────────

    /// <summary>TNT 1kg의 에너지 (J) · 폭풍이 해를 끼치지 않는 압력 (kPa).</summary>
    public const double TntJ = 4.184e6, BlastFloorKPa = 15;
    public long Blasts;

    /// <summary>
    /// 킨니-그레이엄 폭풍 압력 (kPa): 환산 거리 Z = r / W^(1/3) (m/kg^(1/3) · W = TNT kg) —
    /// P/Pa = 808[1 + (Z/4.5)²] / √{[1 + (Z/0.048)²][1 + (Z/0.32)²][1 + (Z/1.35)²]} (Z 1 → 1MPa · 3 → 82kPa · 5 → 29kPa).
    /// </summary>
    public static double BlastKPa(double z)
    {
        z = Math.Max(0.05, z);
        double num = 808 * (1 + Math.Pow(z / 4.5, 2));
        double den = Math.Sqrt((1 + Math.Pow(z / 0.048, 2)) * (1 + Math.Pow(z / 0.32, 2)) * (1 + Math.Pow(z / 1.35, 2)));
        return 101.325 * num / den;
    }

    /// <summary>
    /// 폭풍: 칸 cell에서 energyJ가 충격파가 된다 (큰 충돌이 배 안에서 터진다 — 초고속 충돌의 열 · 파편 구름). 칸 길을 따라 퍼지며 벽 · 문을 지날 때마다 거리 넷만큼 준다 (반사 · 흡수 — 어림).
    /// 벽 · 문은 반사 압력(2배)이 정격을 넘으면 찢기고 · 1.6배면 통째로 · 0.85면 금 / 바닥 · 천장 판도 같은 잣대 / 설비는 35kPa 넘게 / 사람은 ⑦이 읽는 충격(넘친 압력 × 충격파 길이).
    /// </summary>
    public void Blast(Sim sim, int cell, double energyJ, int cause)
    {
        double w = energyJ / TntJ;
        if (w < 0.05 || cell < 0) return;
        Blasts++;
        double w3 = Math.Cbrt(w);
        int maxD = (int)Math.Ceiling(9 * w3);   // Z 9 언저리에서 15kPa 아래
        var dist = new Dictionary<int, int> { [cell] = 0 };
        var pq = new PriorityQueue<int, int>();
        pq.Enqueue(cell, 0);
        Span<int> nb = stackalloc int[4];
        var hit = new List<(int c, double kpa)>();
        while (pq.TryDequeue(out int c, out int d))
        {
            if (d > dist[c]) continue;
            double r = Math.Max(0.5, d), kpa = BlastKPa(r / w3);
            if (kpa < BlastFloorKPa) continue;
            hit.Add((c, kpa));
            bool solid = L.Kind[c] is CellKind.Wall || L.Kind[c] == CellKind.Door && !F.DoorPassable(L.DoorAt[c]);
            if (solid && F.Hole[c] < 2 && c != cell) continue;  // 버틴 벽 너머는 아래에서 다시 본다 (찢기면 지나간다)
            L.Neighbors(c, nb);
            foreach (int j in nb)
            {
                if (j < 0 || F.IsOut(j)) continue;
                bool js = L.Kind[j] is CellKind.Wall or CellKind.Door;
                int nd = d + (js ? 4 : 1);
                if (nd > maxD || dist.TryGetValue(j, out int old) && old <= nd) continue;
                dist[j] = nd; pq.Enqueue(j, nd);
            }
        }
        hit.Sort((a, b) => a.c.CompareTo(b.c));
        int bc = sim.Causes.Add(CauseKind.Impact, "충격파가 배 안을 휩쓸었다", sim.Tick, cell, cause);
        foreach (var (c, kpa) in hit)
        {
            if (L.Kind[c] is CellKind.Wall or CellKind.Door)
            {
                double load = 2 * kpa / Math.Max(1, Rating(c));   // 벽에 부딪힌 충격파는 두 배로 되튄다
                if (L.Kind[c] == CellKind.Door) { if (load >= 1 && L.DoorAt[c] >= 0 && DoorJam[L.DoorAt[c]] == 0) { DamageCause[c] = bc; BlowDoor(sim, c, load); } continue; }
                if (load >= 1.6) Open(sim, c, 2.5f, bc, FactKind.WallBreach, 0);
                else if (load >= 1) Open(sim, c, (float)Math.Min(2.0, 0.02 + 0.5 * (load - 1)), bc, FactKind.WallBreach, 0);
                else if (load >= 0.85) SeedCrack(sim, c, 0.05 + 0.5 * (load - 0.85), bc);
            }
            else if (L.Kind[c] == CellKind.Floor)
            {
                var m = T.Materials[L.Material[c]].Def;
                double load = kpa / (DeckRatingKPa * Math.Sqrt(Math.Max(0.01, m.Strength / 250.0)) * HeatFactor(c, m) * (1 - Corrode[c]));
                if (load >= 1)
                {
                    float area = load >= 1.6 ? 2f : (float)(0.3 + 0.7 * (load - 1) / 0.6);
                    if (area > F.Hole[c]) { if (F.Hole[c] <= 0) DeckBursts++; F.SetHole(c, area); DamageCause[c] = bc; if (area >= DeckGone) _latticeDirty = true; }
                }
                if (L.FurnitureAt[c] >= 0 && kpa > BlastHarmKPa) HitFurniture(sim, L.FurnitureAt[c], (kpa - BlastHarmKPa) / 70, bc, "충격파에");
                // ⑦ 몸이 읽는 충격: 넘친 압력 × 충격파의 길이 (작은 폭발은 짧아 덜 다친다 — 지속 시간 ∝ W^(1/3) · 브라운 곡선의 경향) — TNT 15kg 넘게면 100kPa에 3.9kJ
                if (kpa > BlastHarmKPa) { ImpactJ[c] += (float)((kpa - BlastHarmKPa) * 60 * Math.Min(1, w3 / 2.5)); if (DamageCause[c] == 0) DamageCause[c] = bc; }
            }
        }
        sim.Facts.Emit(sim.Tick, FactKind.LoudSound, cell, (float)Math.Min(194, 120 + 20 * Math.Log10(Math.Max(1, w))), bc);
        sim.Find<Sound>()?.Emit(sim, NearestAir(cell), (float)Math.Min(160, 130 + 10 * Math.Log10(Math.Max(1, w))), SoundKind.Bang, bc);
    }

    /// <summary>설비 하나의 질량 (kg — 설비 표의 재질 합).</summary>
    public double FurnitureKg(int f) => T.Equip(L.Furniture[f].Type).Parts.Values.Sum();

    // ───────────── 문의 열 끼임 ─────────────

    /// <summary>달군 문틀이 늘어나 문이 끼인다 (열팽창 × 1m가 틈 4mm를 넘으면 — 강철 600K 남짓) · 식으면 풀린다.</summary>
    private void HeatJam(Sim sim)
    {
        for (int d = 0; d < L.Doors.Count; d++)
        {
            int c = L.Doors[d].Cell;
            var m = T.Materials[L.Material[c]].Def;
            double grow = m.Alpha * 1.0 * (F.Ts[c] - 293.15);
            if (ThermalJam[d] != 0)
            {
                if (grow < DoorGapM * 0.5) { ThermalJam[d] = 0; DoorJam[d] = 0; DoorTarget[d] = F.DoorOpen[d]; }
                continue;
            }
            if (grow < DoorGapM || DoorJam[d] != 0) continue;
            _chem ??= sim.Find<Chemistry>();
            int heat = 0;
            Span<int> nb = stackalloc int[4];
            L.Neighbors(c, nb);
            if (_chem != null) { heat = _chem.HeatCauseAt(c); foreach (int j in nb) if (heat == 0 && j >= 0) heat = _chem.HeatCauseAt(j); }
            int cause = sim.Causes.Add(CauseKind.Heat, "달군 문틀이 늘어나 문이 끼었다", sim.Tick, c, heat);
            DoorJam[d] = 1; ThermalJam[d] = 1;
            DamageCause[c] = cause;
            sim.Facts.Emit(sim.Tick, FactKind.DoorJammed, c, F.DoorOpen[d], cause);
            sim.Find<Sound>()?.Emit(sim, NearestAir(c), 70, SoundKind.Creak, cause);
        }
    }

    // ───────────── 점화의 단면 하중 (선체 거더) ─────────────

    /// <summary>
    /// 배가 가속하면(⑤ ThrustG · Field.Accel) 단면마다 그 너머 질량 × 가속도를 받친다 — 힘이 들어오는 곳(엔진 · 없으면 무게 중심)에서 먼 쪽의 질량.
    /// 단면 응력 = 힘 / 단면적 (그 줄의 구조 칸) — 금의 주기에 더한다 · 버틸 힘(강도 × 단면적 × 열 · 부식)을 넘으면 그 단면이 끊긴다.
    /// </summary>
    private void Sections(Sim sim)
    {
        double g = ThrustG;
        if (g <= 0.002) { if (_thrustAxis >= 0) { Array.Clear(_thrustSig); _thrustAxis = -1; } return; }
        int axis = Math.Abs(F.AccelY) > Math.Abs(F.AccelX) ? 1 : 0;
        if (axis == _thrustAxis && _thrustLattice == _latticeVersion && sim.Tick - _thrustAt < 25) return;
        _thrustAxis = axis; _thrustLattice = _latticeVersion; _thrustAt = sim.Tick;
        Array.Clear(_thrustSig);
        int lines = axis == 0 ? L.W : L.H;
        var mass = new double[lines]; var area = new double[lines]; var cap = new double[lines];
        double total = 0, mx = 0, ex = 0; int engines = 0;
        for (int c = 0; c < L.N; c++)
        {
            if (!InMain(c) || !Solid(c)) continue;
            int k = axis == 0 ? L.X(c) : L.Y(c);
            var m = T.Materials[L.Material[c]].Def;
            mass[k] += _mass[c]; area[k] += _area[c];
            cap[k] += _area[c] * m.Strength * 1e6 * HeatFactor(c, m) * (1 - Corrode[c]);
            total += _mass[c]; mx += _mass[c] * k;
            if (L.FurnitureAt[c] >= 0 && L.Furniture[L.FurnitureAt[c]].Type == "EngineCore") { ex += k; engines++; }
        }
        if (total <= 0) return;
        double at = engines > 0 ? ex / engines : mx / total;   // 힘이 들어오는 줄
        double accel = g * 9.81;
        var below = new double[lines + 1];
        for (int k = 0; k < lines; k++) below[k + 1] = below[k] + mass[k];
        int weakest = -1; double worst = 0;
        for (int k = 0; k < lines; k++)
        {
            if (area[k] <= 0) continue;
            double far = k > at ? total - below[k] : below[k + 1];   // 힘이 들어오는 곳에서 먼 쪽
            double force = far * accel;
            double sig = force / area[k] / 1e6;
            for (int c = axis == 0 ? k : k * L.W; c < L.N; c += axis == 0 ? L.W : 1)
            {
                if (axis == 1 && L.Y(c) != k) break;
                if (InMain(c) && Solid(c)) _thrustSig[c] = (float)(sig / g);
            }
            double ratio = force / Math.Max(1, cap[k]);
            if (ratio > worst) { worst = ratio; weakest = k; }
        }
        if (worst < 1 || weakest < 0) return;
        // 단면이 끊긴다: 그 줄의 구조 칸이 모두 찢긴다
        SectionFails++;
        int parent = 0;
        for (int c = axis == 0 ? weakest : weakest * L.W; c < L.N && parent == 0; c += axis == 0 ? L.W : 1)
        {
            if (axis == 1 && L.Y(c) != weakest) break;
            if (InMain(c) && DamageCause[c] != 0) parent = DamageCause[c];
        }
        int cause = sim.Causes.Add(CauseKind.Breach, "점화의 힘을 못 이겨 선체의 약해진 단면이 끊어졌다", sim.Tick, -1, parent);
        for (int c = axis == 0 ? weakest : weakest * L.W; c < L.N; c += axis == 0 ? L.W : 1)
        {
            if (axis == 1 && L.Y(c) != weakest) break;
            if (!InMain(c) || !Solid(c)) continue;
            if (L.Kind[c] == CellKind.Floor) { F.SetHole(c, 2f); DamageCause[c] = cause; }
            else Open(sim, c, 2.5f, cause, FactKind.WallBreach, 0);
        }
        _latticeDirty = true;
    }
}
