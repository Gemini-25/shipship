namespace ShipEngine;

/// <summary>칸마다 깔린 선 · 관의 갈래 (비트 1 &lt;&lt; 갈래).</summary>
public enum LineKind : byte { Power, Coolant, Water, Data }

/// <summary>간선 한 가닥: 원천 → 배전반 (또는 배전반끼리 이음선). A · B는 장치 번호.</summary>
public sealed class Bus
{
    public int A, B;
    public int[] Path = Array.Empty<int>();
}

/// <summary>
/// ⑥ 바퀴 3 — 배선 · 배관을 칸마다 (docs/ENGINE.md ⑥ '바퀴 3 설계' (가)). 모든 전선 · 관 · 센서 선이 바닥 · 문 칸을 따라 깔리고 끊긴 칸 뒤로는 안 간다.
/// <para>
/// <b>전기</b>는 가닥마다 따로: 간선(원천 → 배전반 · 배전반끼리) · 급전선(배전반 → 방의 분전함 — 통풍구 칸) · 가지선(분전함 → 장치 — 방 안 칸으로).
/// 간선이 끊기면 배전반 · 원천이 섬으로 갈려 섬마다 따로 셈한다 (아무것도 안 끊기면 섬 하나 — 예전 셈 그대로).
/// </para>
/// <para>
/// <b>관</b>은 그물: 소비처마다 가장 가까운 원천(냉각 펌프 · 물 재생기)까지 깔고 원천끼리도 잇는다. 끊긴 칸을 빼고 이어진 조각마다 흐른다 (갈래가 둘이면 돌아서 간다).
/// 관 칸마다 새는 틈(0~1) — 냉각관은 암모니아가 · 물관은 물탱크의 물이 그 칸으로 샌다.
/// </para>
/// <para>
/// <b>피해</b>: 조각이 떨어져 나가거나 곁 외판이 뚫리면 그 칸의 선이 다 끊긴다 (관은 찢겨 틈 1) · 250℃를 넘으면 전선 피복이 타 끊긴다 · 330℃를 넘으면 관 이음매가 풀려 샌다 (틈 0.3).
/// 탄 전선은 식어도 다시 쓸 수 없다 — 로봇 · 사람이 그 칸에서 잇는다 (⑧이 요청).
/// </para>
/// </summary>
public sealed partial class Network
{
    public const double PipeJointK = 603;                 // 관 이음매(납땜 · 패킹)가 풀리는 온도 (330℃)
    /// <summary>전선 가닥마다 칸에 깔린 피복 (고분자 몰 — 0.5몰 · 7g/m: 우주선 전선은 가늘고 잘 안 타는 피복이지만 달궈지면 탄다 · (다) 불이 피복을 타고 번진다).</summary>
    public const double InsulMolPerStrand = 0.5;
    public const double CoolLeakMolPerSec = 0.05, WaterLeakMolPerSec = 0.46;   // 틈 1에서 펌프가 돌 때 (서 있으면 냉각 10분의 1 · 물 20분의 1)
    public static byte Bit(LineKind k) => (byte)(1 << (int)k);

    // ── 깐 것 (도면에서 · 찍지 않는다) ──
    /// <summary>[칸] 지나는 선 (갈래 비트).</summary>
    public byte[] LineAt = Array.Empty<byte>();
    /// <summary>[칸 × 4 + 갈래] 이어진 쪽 (위 1 · 오른쪽 2 · 아래 4 · 왼쪽 8).</summary>
    public byte[] LineDir = Array.Empty<byte>();
    /// <summary>[칸] 지나는 전선 가닥 수.</summary>
    public byte[] Strands = Array.Empty<byte>();
    public readonly List<Bus> Buses = new();
    /// <summary>[장치] 가지선 — 분전함(그 방 통풍구 칸) → 장치 칸 (가지선이 없는 장치는 빈 배열).</summary>
    public int[][] DropPath = Array.Empty<int[]>();
    private int[] _lineCells = Array.Empty<int>();   // 선이 지나는 칸 (작은 번호부터)
    private int[] _lineRoom = Array.Empty<int>();    // [칸] 그 칸의 방 (문 칸은 곁 방)
    private int[] _coolCell = Array.Empty<int>();    // [장치] 냉각관이 닿는 칸 (-1: 냉각관 없음)
    private int[] _waterCell = Array.Empty<int>();   // [설비] 물관이 닿는 칸 (-1: 물관 없음)
    private bool _hasWaterNet;
    private bool[] _hotBody = Array.Empty<bool>();   // [칸] 원자로 · 엔진 몸체 칸 — 몸체에 닿는 선 끝은 높은 온도용이라 그 칸의 열로는 타지 않는다 (둘레 칸부터)

    // ── 상태 (찍는다) ──
    /// <summary>[칸] 끊긴 선 (전기 · 냉각관 · 물관 비트 — 센서 선은 DataBroken).</summary>
    public byte[] LineCut = Array.Empty<byte>();
    /// <summary>[칸 × 2] 냉각관 · 물관 새는 틈 (0~1).</summary>
    public float[] PipeLeak = Array.Empty<float>();
    /// <summary>[칸 × 3] 끊기거나 새게 한 원인 마디 (전기 · 냉각관 · 물관).</summary>
    public int[] LineCause = Array.Empty<int>();
    /// <summary>[칸 × 2] 주컴퓨터가 그 샘의 자리를 아나 (불 · 구멍 자리의 샘은 안다 — 모르는 샘은 (라)에서 찾는다).</summary>
    public byte[] LeakKnown = Array.Empty<byte>();
    public long CablesCut, PipesBroken, LinesFixed;
    public double VentedNH3, WaterLeaked;

    // ── 셈 (끊김이 바뀌면 다시 · 찍지 않는다) ──
    private bool _linesDirty = true;
    private int[] _pipeComp = Array.Empty<int>();    // [칸 × 2] 관망 조각 번호 (-1: 관 없음 · 끊김)
    private int _coolComps, _waterComps;
    private bool[] _waterSrc = Array.Empty<bool>();  // [물 조각] 물 재생기(물탱크)와 이어졌나
    private bool[] _dropCut = Array.Empty<bool>();   // [장치]
    private bool[] _feederLineCut = Array.Empty<bool>(); // [급전선]
    private int[] _isl = Array.Empty<int>();         // [장치] 전기 섬 (원천 · 배전반만 · 나머지 -1)
    private int _nIsl = 1;
    private readonly List<int> _leakCells = new();

    // ── 섬마다 (전력 셈 — 그 틱 · 방이 밝은지는 다음 틱까지 읽으니 찍는다) ──
    private double[] _iSupplied = new double[1], _iFrac = new double[1];
    private double[] _iDemand = new double[1], _iReactorMax = new double[1], _iAuxMax = new double[1], _iBattMax = new double[1], _iBattCap = new double[1], _iBattRate = new double[1];
    private double[] _iReactorKw = new double[1], _iAuxKw = new double[1], _iBattDelta = new double[1];

    private static int DirOf(Layout L, int a, int b)
    {
        int dx = L.X(b) - L.X(a), dy = L.Y(b) - L.Y(a);
        return dy < 0 ? 0 : dx > 0 ? 1 : dy > 0 ? 2 : 3;
    }

    private int Toward(int c, int dir) => dir switch { 0 => c - L.W, 1 => c + 1, 2 => c + L.W, _ => c - 1 };

    private void Lay(LineKind k, int[] path)
    {
        byte bit = Bit(k);
        for (int i = 0; i < path.Length; i++)
        {
            int c = path[i];
            LineAt[c] |= bit;
            if (k == LineKind.Power && Strands[c] < 255) Strands[c]++;
            if (i == 0) continue;
            int p = path[i - 1], d = DirOf(L, p, c);
            LineDir[p * 4 + (int)k] |= (byte)(1 << d);
            LineDir[c * 4 + (int)k] |= (byte)(1 << ((d + 2) % 4));
        }
    }

    /// <summary>from에서 바닥 · 문 칸을 따라 가장 가까운 target 칸까지 (from이 target이면 한 칸 · 없으면 null).</summary>
    private int[]? PathTo(int from, Func<int, bool> target, Func<int, bool>? pass = null)
    {
        if (from < 0) return null;
        if (target(from)) return new[] { from };
        var prev = new Dictionary<int, int> { [from] = -1 };
        var q = new Queue<int>(); q.Enqueue(from);
        Span<int> nb = stackalloc int[4];
        int end = -1;
        while (q.Count > 0 && end < 0)
        {
            int c = q.Dequeue();
            L.Neighbors(c, nb);
            foreach (int j in nb)
            {
                if (j < 0 || prev.ContainsKey(j) || !L.IsSpace(j) || pass != null && !pass(j)) continue;
                prev[j] = c;
                if (target(j)) { end = j; break; }
                q.Enqueue(j);
            }
        }
        if (end < 0) return null;
        var path = new List<int>();
        for (int c = end; c >= 0; c = prev[c]) path.Add(c);
        path.Reverse();
        return path.ToArray();
    }

    /// <summary>원천 · 소비처 묶음을 관망으로: 원천끼리 잇고 · 소비처마다 가장 가까운 관(원천 · 이미 깐 관)까지 — 줄기를 함께 쓴다.</summary>
    private void LayNet(LineKind k, List<int> sources, List<int> users)
    {
        if (sources.Count == 0) return;
        byte bit = Bit(k);
        bool OnNet(int c) => (LineAt[c] & bit) != 0;
        LineAt[sources[0]] |= bit;
        foreach (int s in sources.Skip(1)) if (!OnNet(s) && PathTo(s, OnNet) is int[] tie) Lay(k, tie);
        foreach (int u in users) if (!OnNet(u) && PathTo(u, OnNet) is int[] p) Lay(k, p);
    }

    private void BuildLines()
    {
        int n = L.N;
        LineAt = new byte[n]; LineDir = new byte[n * 4]; Strands = new byte[n];
        LineCut = new byte[n]; PipeLeak = new float[n * 2]; LineCause = new int[n * 3]; LeakKnown = new byte[n * 2];
        _pipeComp = new int[n * 2];
        // 전기 — 급전선 · 간선 · 가지선
        foreach (var fd in Feeders) Lay(LineKind.Power, fd.Path);
        var panels = Devices.Where(d => d.Def.Role == "panel").ToList();
        if (panels.Count == 0) panels = Devices.Where(d => d.Def.Role == "reactor").ToList();
        var panelCells = new HashSet<int>();
        for (int k = 0; k < panels.Count; k++)
        {
            var pc = panels[k];
            if (k > 0 && PathTo(pc.Cell, c => panelCells.Contains(c)) is int[] tie)
            {
                int other = panels.Take(k).First(x => x.Cell == tie[^1]).Id;
                Buses.Add(new Bus { A = pc.Id, B = other, Path = tie }); Lay(LineKind.Power, tie);
            }
            panelCells.Add(pc.Cell);
        }
        foreach (var d in Devices)
        {
            if (d.Def.Role is not ("reactor" or "aux" or "battery") || panels.Contains(d)) continue;
            if (PathTo(d.Cell, c => panelCells.Contains(c)) is int[] bp)
            {
                int to = panels.First(x => x.Cell == bp[^1]).Id;
                Buses.Add(new Bus { A = d.Id, B = to, Path = bp }); Lay(LineKind.Power, bp);
            }
        }
        DropPath = new int[Devices.Count][];
        foreach (var d in Devices)
        {
            DropPath[d.Id] = Array.Empty<int>();
            if (d.Def.Role is "reactor" or "aux" or "battery" or "panel" || d.Room < 0 || d.Room >= Vent.Length || Vent[d.Room] < 0 || RoomFeeder[d.Room] < 0) continue;
            int room = d.Room, v = Vent[room];
            var p = PathTo(d.Cell, c => c == v, c => L.Room[c] == room) ?? PathTo(d.Cell, c => c == v);
            if (p == null) continue;
            Array.Reverse(p);   // 분전함 → 장치
            DropPath[d.Id] = p; Lay(LineKind.Power, p);
        }
        // 냉각관: 냉각 펌프 ↔ 원자로 · 냉각판 장치 (주컴퓨터 · 공장 기계)
        _coolCell = Enumerable.Repeat(-1, Devices.Count).ToArray();
        var pumps = Devices.Where(d => d.Def.Role == "pump").ToList();
        var cooled = Devices.Where(d => d.Def.Role is "reactor" or "computer" or "industry").ToList();
        if (pumps.Count > 0)
        {
            LayNet(LineKind.Coolant, pumps.Select(d => d.Cell).ToList(), cooled.Select(d => d.Cell).ToList());
            foreach (var d in pumps.Concat(cooled)) if ((LineAt[d.Cell] & Bit(LineKind.Coolant)) != 0) _coolCell[d.Id] = d.Cell;
        }
        // 물관: 물 재생기(물탱크 자리) ↔ 산소 발생기 · 재배 선반 · 마실 물 자리
        _waterCell = Enumerable.Repeat(-1, L.Furniture.Count).ToArray();
        var wsrc = L.Furniture.Where(fu => fu.Type == "WaterRecycler" && fu.Cells.Length > 0).ToList();
        _hasWaterNet = wsrc.Count > 0;
        if (_hasWaterNet)
        {
            var users = new List<FurnitureDef>();
            foreach (var fu in L.Furniture)
            {
                if (fu.Cells.Length == 0 || fu.Type == "WaterRecycler") continue;
                var def = T.Dev(fu.Type);
                if (def != null && def.Role is "o2gen" or "grow" || fu.Type is "MealDispenser" or "CoffeeMachine" or "IceMaker" or "GreywaterFilter" or "DeconShower") users.Add(fu);
            }
            LayNet(LineKind.Water, wsrc.Select(fu => fu.Cells[0]).ToList(), users.Select(fu => fu.Cells[0]).ToList());
            foreach (var fu in wsrc.Concat(users)) if ((LineAt[fu.Cells[0]] & Bit(LineKind.Water)) != 0) _waterCell[fu.Id] = fu.Cells[0];
        }
        // 센서 선 (바퀴 1 — 겹쳐 보기 · 피해를 함께 훑는다)
        foreach (var dp in DataPath) Lay(LineKind.Data, dp);
        _lineCells = Enumerable.Range(0, n).Where(c => LineAt[c] != 0).ToArray();
        // 전선 피복 = 칸 바닥의 탈 것 (③ 장에 요청 — 불이 전선 길을 따라 번진다)
        foreach (int c in _lineCells) if (Strands[c] > 0) F.AddStore(c, T.sPoly, Strands[c] * InsulMolPerStrand);
        _hotBody = new bool[n];
        foreach (var d in Devices) if (d.Def.Role is "reactor" or "engine") foreach (int c in L.Furniture[d.Furniture].Cells) _hotBody[c] = true;
        _lineRoom = new int[n];
        Span<int> nb = stackalloc int[4];
        for (int c = 0; c < n; c++)
        {
            int r = L.Room[c];
            if (r < 0) { L.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0) { r = L.Room[j]; break; } }
            _lineRoom[c] = r;
        }
        _linesDirty = true;
    }

    // ───────────────────────── 이어짐 ─────────────────────────

    private bool PowerCutOn(int[] path)
    {
        byte b = Bit(LineKind.Power);
        foreach (int c in path) if ((LineCut[c] & b) != 0) return true;
        return false;
    }

    /// <summary>끊김이 바뀌었으면 다시 센다: 급전선 · 가지선 · 간선 섬 · 관망 조각.</summary>
    private void EnsureLines()
    {
        if (!_linesDirty) return;
        _linesDirty = false;
        if (_feederLineCut.Length != Feeders.Count) _feederLineCut = new bool[Feeders.Count];
        for (int k = 0; k < Feeders.Count; k++) _feederLineCut[k] = PowerCutOn(Feeders[k].Path);
        if (_dropCut.Length != Devices.Count) _dropCut = new bool[Devices.Count];
        for (int d = 0; d < Devices.Count; d++) _dropCut[d] = d < DropPath.Length && PowerCutOn(DropPath[d]);
        // 섬: 원천 · 배전반을 끊기지 않은 간선으로 묶는다 (가장 작은 장치 번호의 섬이 0)
        var parent = new int[Devices.Count];
        for (int d = 0; d < parent.Length; d++) parent[d] = d;
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        foreach (var b in Buses)
        {
            if (PowerCutOn(b.Path)) continue;
            int ra = Root(b.A), rb = Root(b.B);
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
        // 간선을 깔 길이 없던 원천 · 배전반은 첫 배전반에 붙인다 (예전처럼)
        var node = new bool[Devices.Count];
        foreach (var d in Devices) node[d.Id] = d.Def.Role is "panel" or "reactor" or "aux" or "battery";
        int first = -1;
        foreach (var d in Devices) if (d.Def.Role == "panel") { first = d.Id; break; }
        if (first < 0) foreach (var d in Devices) if (d.Def.Role == "reactor") { first = d.Id; break; }
        if (first >= 0)
            foreach (var d in Devices)
                if (node[d.Id] && d.Id != first && !Buses.Any(b => b.A == d.Id || b.B == d.Id)) { int ra = Root(d.Id), rf = Root(first); if (ra != rf) parent[Math.Max(ra, rf)] = Math.Min(ra, rf); }
        if (_isl.Length != Devices.Count) _isl = new int[Devices.Count];
        var label = new Dictionary<int, int>();
        for (int d = 0; d < Devices.Count; d++)
        {
            if (!node[d]) { _isl[d] = -1; continue; }
            int r = Root(d);
            if (!label.TryGetValue(r, out int l)) label[r] = l = label.Count;
            _isl[d] = l;
        }
        int ni = Math.Max(1, label.Count);
        if (ni != _nIsl || _iSupplied.Length != ni)
        {
            // 섬 수가 바뀌면 지난 틱 값은 섬 0에 모은다 (다음 전력 셈까지만 쓰인다)
            double sup = _iSupplied.Sum(), fr = _iFrac.Length > 0 ? _iFrac[0] : 1;
            _nIsl = ni;
            _iSupplied = new double[ni]; _iFrac = new double[ni]; _iDemand = new double[ni]; _iReactorMax = new double[ni]; _iAuxMax = new double[ni]; _iBattMax = new double[ni];
            _iBattCap = new double[ni]; _iBattRate = new double[ni]; _iReactorKw = new double[ni]; _iAuxKw = new double[ni]; _iBattDelta = new double[ni];
            _iSupplied[0] = sup; for (int i = 0; i < ni; i++) _iFrac[i] = fr;
        }
        // 관망 조각
        Array.Fill(_pipeComp, -1);
        _coolComps = Label(LineKind.Coolant, 0);
        _waterComps = Label(LineKind.Water, 1);
        if (_waterSrc.Length < _waterComps) _waterSrc = new bool[Math.Max(4, _waterComps)];
        Array.Clear(_waterSrc);
        foreach (var fu in L.Furniture)
            if (fu.Type == "WaterRecycler" && _waterCell[fu.Id] >= 0 && _pipeComp[_waterCell[fu.Id] * 2 + 1] is int wc && wc >= 0) _waterSrc[wc] = true;
        _leakCells.Clear();
        foreach (int c in _lineCells) if (PipeLeak[c * 2] > 0 || PipeLeak[c * 2 + 1] > 0) _leakCells.Add(c);
    }

    private int Label(LineKind k, int slot)
    {
        byte bit = Bit(k);
        int comps = 0;
        var q = new Queue<int>();
        foreach (int s in _lineCells)
        {
            if ((LineAt[s] & bit) == 0 || (LineCut[s] & bit) != 0 || _pipeComp[s * 2 + slot] >= 0 || ValveShut(s, slot)) continue;
            _pipeComp[s * 2 + slot] = comps;
            q.Enqueue(s);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                int dirs = LineDir[c * 4 + (int)k];
                for (int d = 0; d < 4; d++)
                {
                    if ((dirs & (1 << d)) == 0) continue;
                    int j = Toward(c, d);
                    if (j < 0 || j >= L.N || (LineCut[j] & bit) != 0 || _pipeComp[j * 2 + slot] >= 0 || ValveShut(j, slot)) continue;
                    _pipeComp[j * 2 + slot] = comps;
                    q.Enqueue(j);
                }
            }
            comps++;
        }
        return comps;
    }

    /// <summary>급전선이 칸마다 깔린 길 어딘가에서 탔거나 찢겼나 — 전기를 넣으면 합선이니 차단기를 올리지 못한다 (이어야 한다).</summary>
    public bool FeederLineCut(int feeder) { EnsureLines(); return feeder >= 0 && feeder < _feederLineCut.Length && _feederLineCut[feeder]; }
    /// <summary>장치의 가지선이 끊겼나 (방이 밝아도 그 장치는 전기를 못 받는다).</summary>
    public bool DropCut(int device) { EnsureLines(); return device >= 0 && device < _dropCut.Length && _dropCut[device]; }
    /// <summary>장치(원천 · 배전반)의 전기 섬 (-1: 섬이 없는 장치).</summary>
    public int IslandOf(int device) { EnsureLines(); return device >= 0 && device < _isl.Length ? _isl[device] : -1; }
    public int Islands { get { EnsureLines(); return _nIsl; } }
    private int FeederIsland(int feeder) => feeder >= 0 && feeder < Feeders.Count && Feeders[feeder].Panel < _isl.Length && _isl[Feeders[feeder].Panel] >= 0 ? _isl[Feeders[feeder].Panel] : 0;
    /// <summary>그 급전선이 받는 비율 (그 섬의 공급 ÷ 수요).</summary>
    public double FeederFrac(int feeder) { EnsureLines(); int i = FeederIsland(feeder); return i < _iFrac.Length ? _iFrac[i] : 0; }
    private double FeederSupplied(int feeder) { EnsureLines(); int i = FeederIsland(feeder); return i < _iSupplied.Length ? _iSupplied[i] : 0; }

    /// <summary>냉각관 조각 (그 장치 칸 · -1: 냉각관이 닿지 않는다).</summary>
    public int CoolComp(int device) { EnsureLines(); return device >= 0 && device < _coolCell.Length && _coolCell[device] >= 0 ? _pipeComp[_coolCell[device] * 2] : -1; }
    /// <summary>그 설비에 물관으로 물이 오나 (물관이 없는 설비 · 물 재생기가 없는 배는 늘 온다).</summary>
    public bool WaterAt(int furniture)
    {
        if (!_hasWaterNet || furniture < 0 || furniture >= _waterCell.Length || _waterCell[furniture] < 0) return true;
        EnsureLines();
        int wc = _pipeComp[_waterCell[furniture] * 2 + 1];
        return wc >= 0 && wc < _waterSrc.Length && _waterSrc[wc];
    }
    public bool WaterAtDevice(int device) => device < 0 || device >= Devices.Count || WaterAt(Devices[device].Furniture);
    public bool LineCutAt(int cell, LineKind k) => cell >= 0 && cell < LineCut.Length && (LineCut[cell] & Bit(k)) != 0;
    public float LeakAt(int cell, LineKind k) => k is LineKind.Coolant or LineKind.Water && cell >= 0 && cell < LineCut.Length ? PipeLeak[cell * 2 + (k == LineKind.Water ? 1 : 0)] : 0;

    // ───────────────────────── 피해 ─────────────────────────

    /// <summary>그 칸의 선을 끊는다 (불 · 구멍 · 시험): 전기 · 관 갈래 비트 · 관은 틈도 연다.</summary>
    public void CutLines(Sim sim, int cell, byte kinds, int cause, float leak = 1)
    {
        if (cell < 0 || cell >= LineAt.Length) return;
        kinds &= (byte)(LineAt[cell] & 0b111);
        byte fresh = (byte)(kinds & ~LineCut[cell]);
        for (int k = 0; k < 3; k++)
        {
            if ((kinds & (1 << k)) == 0) continue;
            if (k > 0) { int s = cell * 2 + k - 1; if (PipeLeak[s] < leak) { PipeLeak[s] = leak; LeakKnown[s] = 1; LineCause[cell * 3 + k] = cause; } }
            if ((fresh & (1 << k)) != 0) LineCause[cell * 3 + k] = cause;
        }
        if (fresh == 0 && kinds == 0) return;
        LineCut[cell] |= kinds;
        if ((fresh & 1) != 0) CablesCut++;
        if ((fresh & 0b110) != 0) PipesBroken++;
        if (fresh != 0) sim.Facts.Emit(sim.Tick, FactKind.CableCut, cell, 0, cause, other: fresh);
        if ((fresh & 0b110) != 0) sim.Facts.Emit(sim.Tick, FactKind.PipeLeak, cell, leak, cause, other: fresh >> 1);
        _linesDirty = true;
    }

    /// <summary>관 이음매가 풀려 샌다 (끊기지는 않는다 — 흐름은 그대로 · 틈만).</summary>
    public void LeakPipe(Sim sim, int cell, LineKind k, float leak, int cause, bool known = true)
    {
        if (cell < 0 || cell >= LineAt.Length || (LineAt[cell] & Bit(k)) == 0 || k is not (LineKind.Coolant or LineKind.Water)) return;
        int s = cell * 2 + (k == LineKind.Water ? 1 : 0);
        if (PipeLeak[s] >= leak) return;
        bool fresh = PipeLeak[s] <= 0;
        PipeLeak[s] = leak; LeakKnown[s] = (byte)(known ? 1 : LeakKnown[s]);
        LineCause[cell * 3 + (int)k] = cause;
        if (fresh) { PipesBroken++; sim.Facts.Emit(sim.Tick, FactKind.PipeLeak, cell, leak, cause, other: (int)k); }
        _linesDirty = true;
    }

    /// <summary>그 칸에 고칠 선이 있나 (끊긴 전선 · 관 · 센서 선 · 아는 샘).</summary>
    public bool NeedsFix(int cell)
    {
        if (cell < 0 || cell >= LineAt.Length) return false;
        return LineCut[cell] != 0 || DataCutAt(cell) || PipeLeak[cell * 2] > 0 && LeakKnown[cell * 2] != 0 || PipeLeak[cell * 2 + 1] > 0 && LeakKnown[cell * 2 + 1] != 0;
    }

    /// <summary>그 칸을 고치는 데 걸리는 분 (로봇 — 사람은 두 배): 센서 선 2 · 전선 3 · 관 4 — 가장 긴 것.</summary>
    public double FixMinutes(int cell)
    {
        double m = 0;
        if (DataCutAt(cell)) m = 2;
        if (LineCutAt(cell, LineKind.Power)) m = Math.Max(m, 3);
        if ((LineCut[cell] & 0b110) != 0 || PipeLeak[cell * 2] > 0 || PipeLeak[cell * 2 + 1] > 0) m = Math.Max(m, 4);
        return m;
    }

    /// <summary>그 칸의 끊긴 선을 다 잇고 새는 관을 죈다 — 한 일 ('끊긴 센서 선을 이었다' · '끊긴 전선을 이었다 · 새는 냉각관을 죄었다' …).</summary>
    public string FixLines(int cell)
    {
        var wires = new List<string>();
        if (LineCutAt(cell, LineKind.Power))
        {
            wires.Add("전선"); _insul[cell] = 0; _insulCause[cell] = 0;   // 새 전선으로 이었다 — 새 피복
            double want = Strands[cell] * InsulMolPerStrand - F.Stored(T.sPoly, cell);
            if (want > 0) F.AddStore(cell, T.sPoly, want);
        }
        bool cool = (LineCut[cell] & Bit(LineKind.Coolant)) != 0 || PipeLeak[cell * 2] > 0, water = (LineCut[cell] & Bit(LineKind.Water)) != 0 || PipeLeak[cell * 2 + 1] > 0;
        if (SpliceData(cell)) wires.Add("센서 선");
        if (LineCut[cell] != 0 || cool || water)
        {
            LineCut[cell] = 0; PipeLeak[cell * 2] = 0; PipeLeak[cell * 2 + 1] = 0; LeakKnown[cell * 2] = 0; LeakKnown[cell * 2 + 1] = 0;
            if (cool) { _rust[cell * 2] = 0; _rustCause[cell * 2] = 0; }
            if (water) { _rust[cell * 2 + 1] = 0; _rustCause[cell * 2 + 1] = 0; }   // 새 관 토막
            for (int k = 0; k < 3; k++) LineCause[cell * 3 + k] = 0;
            _linesDirty = true;
        }
        if (wires.Count == 0 && !cool && !water) return "";
        LinesFixed++;
        var parts = new List<string>();
        if (wires.Count > 0) parts.Add($"끊긴 {string.Join(" · ", wires)}을 이었다");
        if (cool || water) parts.Add($"새는 {(cool && water ? "냉각관 · 물관" : cool ? "냉각관" : "물관")}을 죄었다");
        return string.Join(" · ", parts);
    }

    /// <summary>5틱마다 선이 지나는 칸: 떨어져 나감 · 곁 외판 구멍 · 불의 열 → 선이 상한다 (센서 선은 바퀴 1 그대로).</summary>
    private void LineStep(Sim sim)
    {
        if (_lineCells.Length == 0) return;
        Chemistry? chem = null; Structure? st = null;
        Span<int> nb = stackalloc int[4];
        byte pw = Bit(LineKind.Power), pipes = (byte)(Bit(LineKind.Coolant) | Bit(LineKind.Water));
        foreach (int c in _lineCells)
        {
            bool data = c < _onData.Length && _onData[c] && DataBroken[c] == 0;
            byte at = LineAt[c];
            bool power = (at & pw) != 0 && (LineCut[c] & pw) == 0;
            bool pipe = (at & pipes) != 0;
            if (!data && !power && !pipe) continue;
            int cause = 0; bool torn = false, burnt = false, joint = false;
            if (F.Severed[c] != 0)
            {
                st ??= sim.Find<Structure>();
                torn = true;
                cause = st?.FragmentCause(c) is int fc and not 0 ? fc : st != null && st.DamageCause[c] != 0 ? st.DamageCause[c] : sim.Causes.Add(CauseKind.Breach, "선이 지나던 칸이 떨어져 나갔다", sim.Tick, c);
            }
            else
            {
                double hot = _hotBody[c] ? 0 : Math.Max(F.Tf[c], F.Ts[c]);
                burnt = (data || power) && hot > CableBurnK;
                joint = pipe && hot > PipeJointK;
                if (burnt || joint)
                {
                    chem ??= sim.Find<Chemistry>();
                    cause = HeatSource(chem, c);
                    if (cause == 0) cause = sim.Causes.Add(CauseKind.Heat, "선이 지나는 바닥이 달아올랐다", sim.Tick, c);
                }
                else
                {
                    L.Neighbors(c, nb);
                    foreach (int j in nb)
                        if (j >= 0 && L.Kind[j] == CellKind.Wall && F.Hole[j] >= 0.05f)
                        {
                            st ??= sim.Find<Structure>();
                            torn = true;
                            cause = st != null && st.DamageCause[j] != 0 ? st.DamageCause[j] : sim.Causes.Add(CauseKind.Pressure, "곁 외판이 뚫리며 선이 찢겼다", sim.Tick, c);
                            break;
                        }
                    if (!torn) continue;
                }
            }
            if (data && (torn || burnt)) CutData(sim, c, sim.Causes.Add(CauseKind.Electric, "센서 선이 끊겼다", sim.Tick, c, cause));
            if (power && (torn || burnt)) CutLines(sim, c, pw, sim.Causes.Add(CauseKind.Electric, torn ? "전선이 찢겨 끊겼다" : "전선 피복이 타 끊겼다", sim.Tick, c, cause));
            if (pipe && torn)
            {
                byte fresh = (byte)(at & pipes & ~LineCut[c]);
                if (fresh != 0) CutLines(sim, c, fresh, sim.Causes.Add(CauseKind.Leak, (fresh & Bit(LineKind.Coolant)) != 0 ? "냉각관이 찢겨 암모니아가 뿜어 나온다" : "물관이 찢겨 물이 뿜어 나온다", sim.Tick, c, cause));
            }
            else if (pipe && joint)
            {
                if ((at & Bit(LineKind.Coolant)) != 0 && PipeLeak[c * 2] < 0.3f) LeakPipe(sim, c, LineKind.Coolant, 0.3f, sim.Causes.Add(CauseKind.Leak, "냉각관 이음매가 달아 풀렸다 — 암모니아가 샌다", sim.Tick, c, cause));
                if ((at & Bit(LineKind.Water)) != 0 && PipeLeak[c * 2 + 1] < 0.3f) LeakPipe(sim, c, LineKind.Water, 0.3f, sim.Causes.Add(CauseKind.Leak, "물관 이음매가 달아 풀렸다 — 물이 샌다", sim.Tick, c, cause));
            }
        }
    }

    private bool _fuelLinked;
    /// <summary>피복을 타고 기는 불 (다): 전선 칸의 피복을 전선 방향대로 이어진 탈 것으로 ② 화학에 알린다 (처음 한 번 — 되살린 뒤에도 같다).</summary>
    private void LinkInsulation(Sim sim)
    {
        if (_fuelLinked) return;
        _fuelLinked = true;
        var chem = sim.Find<Chemistry>();
        if (chem == null) return;
        foreach (int c in _lineCells) if (Strands[c] > 0) chem.LinkFuel(c, LineDir[c * 4 + (int)LineKind.Power]);
    }

    /// <summary>틱마다 새는 관: 냉각관은 냉매가 그 칸 기체로 (진공이면 우주로) · 물관은 물탱크의 물이 그 칸 바닥으로.</summary>
    private void LeakStep(Sim sim, double dt)
    {
        if (_leakCells.Count == 0) return;
        foreach (int c in _leakCells)
        {
            float lc = PipeLeak[c * 2], lw = PipeLeak[c * 2 + 1];
            if (lc > 0 && Coolant > 0)
            {
                double n = Math.Min(Coolant, LeakFlow(c, LineKind.Coolant) * dt);
                Coolant -= n;
                if (F.Severed[c] != 0 || F.RegionOf.Length <= c || F.RegionOf[c] < 0) VentedNH3 += n; else F.AddGas(c, T.gNH3, n, F.Temp(c));
            }
            if (lw > 0 && WaterTank > 0)
            {
                double n = Math.Min(WaterTank, LeakFlow(c, LineKind.Water) * dt);
                WaterTank -= n; WaterLeaked += n; sim.Book.Add(Res.Water, Flow.Leaked, -n);
                if (F.Severed[c] == 0) F.AddStore(c, T.sWater, n);
            }
        }
    }

    /// <summary>그 관 칸에 압력이 걸려 있나 — 이어진 조각(끊긴 칸이면 이웃 조각)에 일하는 펌프(냉각) · 물 재생기(물)가 있다.</summary>
    private bool Pumping(int c, LineKind k)
    {
        int slot = k == LineKind.Water ? 1 : 0;
        if (ValveShut(c, slot)) return false;   // 밸브를 잠근 방 — 압력이 빠졌다
        string role = k == LineKind.Water ? "recycler" : "pump";
        Span<int> comps = stackalloc int[5];
        int nc = 0;
        if (_pipeComp[c * 2 + slot] >= 0) comps[nc++] = _pipeComp[c * 2 + slot];
        else
        {
            int dirs = LineDir[c * 4 + (int)k];
            for (int d = 0; d < 4; d++) if ((dirs & (1 << d)) != 0 && Toward(c, d) is int j && j >= 0 && j < L.N && _pipeComp[j * 2 + slot] >= 0) comps[nc++] = _pipeComp[j * 2 + slot];
        }
        if (nc == 0) return false;
        foreach (var dev in Devices)
        {
            if (dev.Def.Role != role || !Working(dev.Id)) continue;
            int cell = k == LineKind.Water ? (dev.Furniture < _waterCell.Length ? _waterCell[dev.Furniture] : -1) : _coolCell[dev.Id];
            if (cell < 0) continue;
            int pc = _pipeComp[cell * 2 + slot];
            for (int i = 0; i < nc; i++) if (comps[i] == pc) return true;
        }
        return false;
    }

    /// <summary>방마다 고칠 선이 있는 칸 (주컴퓨터에서 가장 가까운 것 — 방에 없으면 -1).</summary>
    public int LineFixCell(int room, int fromCell)
    {
        int best = -1, bd = int.MaxValue;
        foreach (int c in _lineCells)
        {
            if (_lineRoom[c] != room || !(LineCut[c] != 0 || PipeLeak[c * 2] > 0 && LeakKnown[c * 2] != 0 || PipeLeak[c * 2 + 1] > 0 && LeakKnown[c * 2 + 1] != 0)) continue;
            int d = fromCell < 0 ? 0 : Math.Abs(L.X(c) - L.X(fromCell)) + Math.Abs(L.Y(c) - L.Y(fromCell));
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    /// <summary>그 장치를 펌프에서 떼어 놓은 냉각관 끊김의 원인 (그 장치 조각의 가장자리에서 끊긴 칸 · 장치 칸이 끊겼으면 그 칸 · 없으면 0).</summary>
    public int CoolCutCause(int device)
    {
        EnsureLines();
        if (device < 0 || device >= _coolCell.Length || _coolCell[device] < 0) return 0;
        int home = _coolCell[device], rc = _pipeComp[home * 2];
        byte bit = Bit(LineKind.Coolant);
        if (rc < 0) return LineCause[home * 3 + 1];
        foreach (int c in _lineCells)
        {
            if ((LineCut[c] & bit) == 0) continue;
            int dirs = LineDir[c * 4 + (int)LineKind.Coolant];
            for (int d = 0; d < 4; d++)
                if ((dirs & (1 << d)) != 0 && Toward(c, d) is int j && j >= 0 && j < L.N && _pipeComp[j * 2] == rc) return LineCause[c * 3 + 1];
        }
        return 0;
    }

    /// <summary>그 칸을 끊거나 새게 한 원인 마디 (전기 → 냉각관 → 물관 차례로 · 없으면 0).</summary>
    public int LineCauseAt(int cell)
    {
        if (cell < 0 || cell >= LineAt.Length) return 0;
        for (int k = 0; k < 3; k++) if (LineCause[cell * 3 + k] != 0) return LineCause[cell * 3 + k];
        return 0;
    }

    private void SaveLines(StateWriter w)
    {
        w.Arr(LineCut); w.Arr(PipeLeak); w.Arr(LineCause); w.Arr(LeakKnown);
        w.I64(CablesCut); w.I64(PipesBroken); w.I64(LinesFixed); w.F64(VentedNH3); w.F64(WaterLeaked);
        EnsureLines();
        w.Arr(_iSupplied); w.Arr(_iFrac);
    }

    private void LoadLines(StateReader r)
    {
        r.ArrInto(LineCut); r.ArrInto(PipeLeak); r.ArrInto(LineCause); r.ArrInto(LeakKnown);
        CablesCut = r.I64(); PipesBroken = r.I64(); LinesFixed = r.I64(); VentedNH3 = r.F64(); WaterLeaked = r.F64();
        _linesDirty = true;
        EnsureLines();
        _iSupplied = r.Arr<double>(); _iFrac = r.Arr<double>();
    }
}
