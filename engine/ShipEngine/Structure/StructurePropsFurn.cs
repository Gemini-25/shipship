namespace ShipEngine;

/// <summary>
/// ④ 볼트 · 뜯긴 가구 · 밀린 가구 · 날아간 문짝 · 설비 덩어리 (docs/PHYSICS.md §3-7 · §5-4 — PH10): 제자리 가구 (FurnPlaced 1)는 그 꼴의 기둥이다 —
/// 받는 힘 (평면 가속 m·a · 바람 (구멍 입 · 돌풍 — 물체와 같은 셈) · 덜컹 m·Δv / 0.1초 · 폭풍 P·A)이 견디는 힘 (볼트: anchorG × m × 9.8 × 단/10 ·
/// 놓인 가구 (anchorG 0): 정지 마찰 μs × m × g')을 넘으면 그 꼴의 물체 (Kind 1 · What = 가구 번호)가 된다:
/// 볼트였으면 부서짐 (FurnBroken — ⑥이 장치를 끈다: 선 · 관이 뜯겼다) · 칸 질량과 몸 질량을 정확히 뗀다 (칸은 도면에서 다시 세운 값 · 몸은 그 차이만큼) ·
/// 위에 놓인 것은 받침을 잃는다 · 사실 '뜯겨 나왔다' · 원인 '… 뜯겨 나왔다'. 놓인 가구는 제자리 0.2m · 10° 안에서 잠들면 다시 앉는다 (몸 · 칸에 도로 붙는다).
/// 물체가 잠들면 그 연료 · 열용량이 그 칸으로 옮겨 간다 (③ <see cref="Field.MoveFurnishing"/> — 합 · 에너지 그대로). 볼트 단은 ⑥이 틱마다 맞춘다 (<see cref="SetAnchor"/>).
/// 날아간 문짝은 문짝 꼴 물체 (Kind 3 · What = 문 번호) · 2톤 아래 덩어리의 설비는 그 꼴의 물체가 된다 (점 알갱이가 아니다 — 우주로 날아가는 것은 PH11).
/// </summary>
public sealed partial class Structure
{
    /// <summary>덜컹을 힘으로 보는 때 (초 — m Δv / 0.1초) · 다시 앉는 거리 (m) · 각 (rad).</summary>
    public const double AnchorJoltSec = 0.1, ReseatM = 0.2, ReseatRad = 10 * Math.PI / 180;
    /// <summary>조각의 부모가 가구 (Flags 8 — What = 가구 번호) · 쓰러진 가구 · 문짝이 차지한 칸의 길 값 (⑧ 길찾기가 더한다 — 곁 줄로 돌아갈 만큼).</summary>
    public const byte FurnPieceFlag = 8, ClutterCost = 4, LitterCost = 2;
    /// <summary>길 값에 드는 가루 (kg — 그 칸 바닥의 깨진 유리 · 쇳가루).</summary>
    public const float LitterGritKg = 0.05f;

    /// <summary>가구의 연료 · 열용량이 든 곳 (−1: 도면의 제자리 칸들 · −2: 배를 떠났다 · 그 밖은 그 칸 하나) — 찍는다.</summary>
    public int[] FurnAt = Array.Empty<int>();
    /// <summary>문마다 문짝이 날아갔다 (1 — 몸 질량에서 문짝 몫을 뺐다 · 열린 채 끼어 닫히지 않는다) — 찍는다.</summary>
    public byte[] DoorLeafOut = Array.Empty<byte>();
    /// <summary>문짝이 날아간 문인가 (빈 문틀 — 바로잡을 수도 닫을 수도 없다 · 문 센서 · 곁에서 보는 이가 안다).</summary>
    public bool LeafGone(int d) => d >= 0 && d < DoorLeafOut.Length && DoorLeafOut[d] != 0;

    /// <summary>시험 설정 (찍지 않는다 — Debris · Breakage처럼): false면 받는 힘으로 가구가 뜯기지 않는다 (PH10 앞처럼 — 다른 것을 재는 시험이 몸을 순간에 돌리거나 밀 때 · 요청 TearLoose는 그대로).</summary>
    public bool Tearing = true;

    private byte[] _clutter = Array.Empty<byte>();
    private bool _clutterDirty = true;
    private readonly Dictionary<int, DotForm> _furnForms2 = new();   // (가구 · 누움)마다 물체 꼴 (찍지 않는다 — 도면 · 표에서 늘 같게)
    private Sim? _sim;   // 틱 사이 요청 (덜컹)이 사실 · 원인을 남길 세계 (찍지 않는다 — Sim에 붙을 때 · 틱마다 잡는다)

    // ───────────────────────── 요청 · 읽기 ─────────────────────────

    /// <summary>요청 (⑥ 틱마다): 볼트 단 (0~10 — 10 다 조였다). 볼트 상태는 ⑥ Loose 한 벌이다 — ④는 단만 안다.</summary>
    public void SetAnchor(int f, int level)
    {
        if (f >= 0 && f < FurnAnchor.Length) { FurnAnchor[f] = (byte)Math.Clamp(level, 0, 10); _boltLow = -1; }
    }

    /// <summary>쓸 수 있는 가구인가 (제자리에 앉아 있다 — 뜯긴 침대 · 밀린 의자는 아니다 · ⑧ · ⑩이 읽는다).</summary>
    public bool Usable(int f) => f < 0 || f >= FurnPlaced.Length || FurnPlaced[f] != 0;

    /// <summary>가구의 제자리 무게중심 (도면 m).</summary>
    public (double x, double y) FurnHome(int f)
    {
        var (fm, ox, oy) = FurnForm(f);
        return fm == null ? (0, 0) : (ox * DotForm.Dot + fm.Cx, oy * DotForm.Dot + fm.Cy);
    }

    /// <summary>요청 (시험 차림 · 부딪힘): 배의 몸이 아직 없으면 세운다 (세상 틀 = 도면 틀).</summary>
    public void EnsureBody() => EnsureShip();

    /// <summary>몸 b의 질량 · 무게중심 (제 틀) · 회전관성 — 칸에서 다시 센 값 (법칙이 몸이 가진 값과 견준다 · 세계를 바꾸지 않는다).</summary>
    public (double m, double lx, double ly, double inertia) BodyMoments(int b)
    {
        Moments(b, out double m, out double sx, out double sy, out double sxx, out _);
        if (m <= 0) return (0, 0, 0, 0);
        double lx = sx / m, ly = sy / m;
        return (m, lx, ly, sxx - m * (lx * lx + ly * ly));
    }

    /// <summary>그 칸의 길 값 (쓰러진 · 밀린 가구 · 문짝이 차지했다 — 0 없음).</summary>
    public byte ClutterAt(int c)
    {
        if (_clutterDirty) BuildClutter();
        return c >= 0 && c < _clutter.Length ? _clutter[c] : (byte)0;
    }

    /// <summary>요청 (시험 차림): 물체 자리를 바로 고쳤다 — 길 값을 다시 센다.</summary>
    public void ClutterChanged() => _clutterDirty = true;

    private void BuildClutter()
    {
        if (_clutter.Length != L.N) _clutter = new byte[L.N];
        Array.Clear(_clutter);
        foreach (var p in Props)
        {
            if (p.Kind is not (1 or 3) || p.State is 5) continue;
            var (hxs, hys, _) = PropForm(p).Shape.Hit;   // 닿음 표본 (무게중심에서 — 껍질 꼭짓점 · 테두리)
            double cs = Math.Cos(p.Th), sn = Math.Sin(p.Th), x0 = p.X, y0 = p.Y, x1 = p.X, y1 = p.Y;
            for (int k = 0; k < hxs.Length; k++) { double x = p.X + cs * hxs[k] - sn * hys[k], y = p.Y + sn * hxs[k] + cs * hys[k]; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            for (int y = Math.Max(0, (int)Math.Floor(y0 + 0.1)); y <= Math.Min(L.H - 1, (int)Math.Floor(y1 - 0.1)); y++)
                for (int x = Math.Max(0, (int)Math.Floor(x0 + 0.1)); x <= Math.Min(L.W - 1, (int)Math.Floor(x1 - 0.1)); x++)
                    _clutter[L.Index(x, y)] = ClutterCost;
        }
        // 깨진 조각 · 판 조각 · 가루가 흩어진 칸은 +2 (밟으면 미끄럽고 다친다 — 같은 길이면 돌아간다 · PH16)
        foreach (var p in Props)
        {
            if (p.Kind is not (2 or 4) || p.State != 0) continue;
            int c = CellOf(p);
            if (c >= 0 && _clutter[c] < LitterCost) _clutter[c] = LitterCost;
        }
        for (int c = 0; c < L.N && c < Grit.Length; c++) if (Grit[c] > LitterGritKg && _clutter[c] < LitterCost) _clutter[c] = LitterCost;
        _clutterDirty = false;
    }

    // ───────────────────────── 꼴 ─────────────────────────

    /// <summary>가구 물체의 꼴 (그 가구의 제자리 꼴 그대로 · 누움 lie) — 날아간 문짝은 문짝 꼴.</summary>
    private DotForm FurnPropForm(in Prop p) => p.Kind == 3 ? DotForge.Make(T, "문짝", lie: p.Pose) : FurnWhole(p.What, p.Pose);

    /// <summary>가구 f의 꼴 (누움 lie — 제자리 꼴과 같은 보는 쪽 · 칸 상자).</summary>
    private DotForm FurnWhole(int f, int lie)
    {
        int key = f * 8 + lie;
        if (_furnForms2.TryGetValue(key, out var fm)) return fm;
        var fu = L.Furniture[f];
        int x0 = fu.Cells.Min(L.X), y0 = fu.Cells.Min(L.Y), bw = fu.Cells.Max(L.X) - x0 + 1, bh = fu.Cells.Max(L.Y) - y0 + 1;
        _furnForms2[key] = fm = DotForge.Make(T, fu.Type, f < L.Facing.Length ? L.Facing[f] : Facing.S, bw, bh, lie);
        return fm;
    }

    /// <summary>문짝 하나의 질량 (kg — 꼴 표 '문짝').</summary>
    public double LeafKg => DotForge.Make(T, "문짝").Mass;

    // ───────────────────────── 칸 질량 ─────────────────────────

    /// <summary>칸 질량을 도면에서 다시 세운다 (판 · 골조 + 제자리에 앉은 설비 몫 — 처음 짓기와 같은 셈이라 같은 값): 뜯고 · 다시 앉히고 · 되살린 세계가 같은 값을 갖는다.</summary>
    private void RecalcCellMass(int c)
    {
        float v = _smass[c];
        int f = L.FurnitureAt[c];
        if (f >= 0 && f < FurnPlaced.Length && FurnPlaced[f] != 0) v += (float)(FurnitureKg(f) / L.Furniture[f].Cells.Length);
        _mass[c] = v;
    }

    /// <summary>되살린 뒤: 뜯긴 가구의 칸 질량을 다시 세운다 (칸 질량은 찍지 않는다 — 도면 + 제자리 표에서).</summary>
    private void RestoreFurnMass()
    {
        _boltLow = -1;   // 되살린 볼트 단 — 버팀의 아래 끝을 다시 센다
        if (_mass.Length != L.N) return;
        for (int f = 0; f < L.Furniture.Count && f < FurnPlaced.Length; f++)
            if (FurnPlaced[f] == 0) foreach (int c in L.Furniture[f].Cells) RecalcCellMass(c);
        _clutterDirty = true;
    }

    /// <summary>가구 f가 앉거나 떠난 뒤 칸 질량을 다시 세우고 · 몸이 있으면 그 차이 (칸 가운데 · 1/6)를 몸에서 떼거나 붙인다 (물체의 세상 빠르기는 부른 쪽이 맞춘다).</summary>
    private void FurnMassChanged(int f)
    {
        double dm = 0, sx = 0, sy = 0, s2 = 0;
        int b = -1;
        foreach (int c in L.Furniture[f].Cells)
        {
            double before = _mass[c];
            RecalcCellMass(c);
            double d = before - _mass[c];   // 뗀 질량 (붙였으면 −)
            if (d == 0) continue;
            double cx = L.X(c) + 0.5, cy = L.Y(c) + 0.5;
            dm += d; sx += d * cx; sy += d * cy; s2 += d * (cx * cx + cy * cy + 1.0 / 6);
            if (b < 0) b = BodyOf(c);
        }
        if (dm == 0 || b < 0 || b >= Bodies.Count || Bodies[b].Mass <= 0) return;
        if (dm > 0) RemoveMass2(b, dm, sx / dm, sy / dm, s2);
        else AddMass2(b, -dm, sx / dm, sy / dm, -s2);
    }

    /// <summary>질량 kg (무게중심 (px, py) · 제 틀 원점에 대한 둘째 모멘트 sAdd)을 몸 b에 붙인다 — 몸과 함께 움직이던 것 (잠든 채 바닥에 실려 가던 것)이라 빠르기는 새 무게중심의 v + ω×r.</summary>
    private void AddMass2(int b, double kg, double px, double py, double sAdd)
    {
        var o = Bodies[b];
        double s = o.Inertia + o.Mass * (o.Lx * o.Lx + o.Ly * o.Ly);
        double m2 = o.Mass + kg, lx = (o.Mass * o.Lx + kg * px) / m2, ly = (o.Mass * o.Ly + kg * py) / m2;
        var (wx, wy) = WorldOf(b, lx, ly);
        double rx = wx - o.X, ry = wy - o.Y;
        o.Vx -= o.W * ry; o.Vy += o.W * rx;
        o.PVx -= o.PW * ry; o.PVy += o.PW * rx;
        o.X = wx; o.Y = wy; o.Lx = lx; o.Ly = ly;
        o.Mass = m2; o.Inertia = Math.Max(1e-9, s + sAdd - m2 * (lx * lx + ly * ly));
        Bodies[b] = o;
    }

    // ───────────────────────── 견디는 힘 · 받는 힘 ─────────────────────────

    /// <summary>견디는 힘 (N): 볼트 anchorG × m × 9.8 × 단/10 (단 0이면 0) · 놓인 가구 (anchorG 0)는 정지 마찰 μs × m × g' (바닥 겉 재질 · 젖으면 0.33배).</summary>
    public double AnchorHold(int f)
    {
        var (fm, _, _) = FurnForm(f);
        if (fm == null) return double.PositiveInfinity;
        var e = T.Equip(L.Furniture[f].Type);
        int c = L.Furniture[f].Cells[0];
        if (e.AnchorG > 0) return e.AnchorG * fm.Mass * 9.8 * FurnAnchor[f] / 10.0;
        double mu = StaticShare * T.Materials[Math.Min(BaseMatOf(fm), T.Materials.Count - 1)].Def.Mu * (F.Stored(T.sWater, c) > WetMol ? WetShare : 1);
        return mu * fm.Mass * Gravity * F.Grav[c];
    }

    /// <summary>
    /// 틱마다 (물체 걸음 앞 · 틀을 센 뒤): 제자리 가구가 받는 평면 힘 — 틀의 힘 (몸이 없는 배는 점화 −m·ShipA · 몸이면 그 틀의 가속 · 원심 · 오일러) + 바람 (그 칸에 바람이 불 때만 — 물체와 같은 셈).
    /// 견디는 힘을 넘으면 뜯는다 (빠르기 0 — 그 힘은 이제 물체 걸음이 준다). 힘이 없는 조용한 틱은 곧장 돌아간다.
    /// </summary>
    private double[]? _furnWindMax;

    /// <summary>
    /// 놓인 가구 f가 버티는 힘의 아래 끝 (kg마다 — 볼트면 단 몫 그대로 · 마찰이면 젖은 바닥 몫으로): 받는 힘의 위 끝이 이 아래면 셈하지 않는다 (속도만 — 결과는 같다).
    /// </summary>
    private double HoldLowPerKg(int f, int c0)
    {
        double h = StaticOf().HoldKg[f];
        return h < 0 ? -h * 9.8 * FurnAnchor[f] / 10.0 : h * Gravity * F.Grav[c0];
    }

    /// <summary>
    /// 놓인 가구 f가 받을 수 있는 바람 힘의 위 끝 (kg마다): 발자국 상자 칸 (바람 표본이 드는 칸 — 도트 가운데라 상자 밖으로 나가지 않는다)의 가장 센 동압 (칸 돌풍 · 구멍 입 가운데 센 것) × Cd × 16갈래 가운데 가장 넓은 앞면 ÷ 질량.
    /// </summary>
    private double WindBoundPerKg(int f, DotForm fm, int ox, int oy)
    {
        _furnWindMax ??= new double[L.Furniture.Count];
        if (_furnWindMax[f] == 0) { double a = 0; foreach (double x in fm.Area16) a = Math.Max(a, x); _furnWindMax[f] = fm.Cd * a / fm.Mass + 1e-300; }
        int cx0 = Math.Max(0, ox >> 5), cy0 = Math.Max(0, oy >> 5), cx1 = Math.Min(L.W - 1, (ox + fm.W - 1) >> 5), cy1 = Math.Min(L.H - 1, (oy + fm.D - 1) >> 5);
        double q = 0;
        for (int y = cy0; y <= cy1; y++)
            for (int x = cx0; x <= cx1; x++)
            {
                int c = L.Index(x, y);
                q = Math.Max(q, Math.Max(F.WindPa[c], MouthAt(c, 0, out _)));   // 구멍 입 곁이면 그 입의 동압 (WindForce가 같은 틱 값을 읽는다)
            }
        return q * _furnWindMax[f];
    }

    private int[] _windyMark = Array.Empty<int>();
    private int _windyStamp;
    private readonly List<int> _windyFurn = new();
    private double _boltLow = -1;
    private FurnStatic? _furnStatic;

    /// <summary>
    /// 도면 · 표에서 늘 같은 가구 값 (도면마다 한 벌 — 상상 복제가 함께 쓴다 · 다 지은 뒤에 내놓아 일꾼끼리 겹쳐 읽어도 된다):
    /// 가구마다 버팀 몫 (볼트면 −볼트 g · 마찰이면 1.25μ × 젖은 바닥 0.33) · 마찰 가구의 첫 칸.
    /// </summary>
    private sealed class FurnStatic { public Tables T = null!; public double[] HoldKg = Array.Empty<double>(); public int[] FrictionC0 = Array.Empty<int>(); }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Layout, FurnStatic> _furnStatics = new();

    private FurnStatic StaticOf()
    {
        if (_furnStatic != null && _furnStatic.HoldKg.Length == L.Furniture.Count) return _furnStatic;
        if (_furnStatics.TryGetValue(L, out var fs) && fs.T == T && fs.HoldKg.Length == L.Furniture.Count) return _furnStatic = fs;
        fs = new FurnStatic { T = T, HoldKg = new double[L.Furniture.Count] };
        var fr = new List<int>();
        for (int f = 0; f < L.Furniture.Count; f++)
        {
            if (L.Furniture[f].Cells.Length == 0) continue;
            double ag = T.Equip(L.Furniture[f].Type).AnchorG;
            if (ag > 0) { fs.HoldKg[f] = -ag; continue; }
            var (fm, _, _) = FurnForm(f);
            if (fm == null) continue;
            fs.HoldKg[f] = StaticShare * T.Materials[Math.Min(BaseMatOf(fm), T.Materials.Count - 1)].Def.Mu * WetShare + 1e-300;
            fr.Add(L.Furniture[f].Cells[0]);
        }
        fs.FrictionC0 = fr.ToArray();
        _furnStatics.AddOrUpdate(L, fs);
        return _furnStatic = fs;
    }

    /// <summary>
    /// 놓인 가구 모두가 버티는 힘의 아래 끝 (kg마다): 볼트 가구 가운데 가장 약한 것 (단이 바뀌면 다시 센다) · 마찰 가구는 가장 미끄러운 재질 · 젖은 바닥 · 그 칸 중력 가운데 가장 낮은 것.
    /// </summary>
    private double HoldLowAll()
    {
        var fs = StaticOf();
        if (_boltLow < 0)
        {
            double bl = double.PositiveInfinity;
            for (int f = 0; f < fs.HoldKg.Length && f < FurnAnchor.Length; f++) if (fs.HoldKg[f] < 0) bl = Math.Min(bl, -fs.HoldKg[f] * 9.8 * FurnAnchor[f] / 10.0);
            _boltLow = bl;
        }
        double m = _boltLow;
        if (fs.FrictionC0.Length > 0)
        {
            var g = F.Grav; double gmin = double.PositiveInfinity;
            foreach (int c in fs.FrictionC0) gmin = Math.Min(gmin, g[c]);
            m = Math.Min(m, StaticShare * MinMu() * WetShare * Gravity * gmin);   // 가장 미끄러운 재질 (마찰 가구마다의 몫보다 작거나 같다)
        }
        return m;
    }

    /// <summary>틀의 가속의 위 끝 (kg마다 — 몸마다 고른 가속 + (ω² + |α|) × 도면 대각선 · 무게중심은 도면 안이다).</summary>
    private double FrameBoundAll(bool world)
    {
        if (!world) return Math.Sqrt(ShipAx * ShipAx + ShipAy * ShipAy);
        double r = Math.Sqrt((double)L.W * L.W + (double)L.H * L.H), a = 0;
        for (int b = 0; b < Bodies.Count && b < _snap.Length; b++)
        {
            if (Bodies[b].Mass <= 0) continue;
            var sf = _snap[b];
            a = Math.Max(a, Math.Sqrt(sf.Ax * sf.Ax + sf.Ay * sf.Ay) + (sf.W * sf.W + Math.Abs(sf.Al)) * r);
        }
        return a;
    }

    private bool FurnLoads(Sim sim, bool world)
    {
        bool frames = !world && (ShipAx != 0 || ShipAy != 0);
        if (world) for (int b = 0; b < Bodies.Count && b < _snap.Length && !frames; b++) frames = Bodies[b].Mass > 0 && (_snap[b].Ax != 0 || _snap[b].Ay != 0 || _snap[b].Al != 0 || _snap[b].W != 0);
        if (!Tearing || !frames && !_windy) return false;
        bool any = false;
        // 틀의 가속만으로는 어느 가구도 버팀을 넘지 못하면 (감압 · 점화의 거의 모든 틱) 바람이 분 칸의 가구만 본다 — 가구 차례 그대로 (속도만 · 결과는 같다)
        if (frames && FrameBoundAll(world) is double fa && fa > 0 && fa > HoldLowAll())
        {
            for (int f = 0; f < L.Furniture.Count; f++) any |= FurnLoad(sim, f, frames, world);
            return any;
        }
        if (!_windy || PropDetail == 0) return false;   // 가벼운 셈 (상상 복제)은 바람에 밀리는 가구를 셈하지 않는다 — 틀 · 덜컹 · 폭풍은 그대로
        if (_windyMark.Length < L.Furniture.Count) _windyMark = new int[L.Furniture.Count];
        if (++_windyStamp == int.MaxValue) { Array.Clear(_windyMark); _windyStamp = 1; }
        _windyFurn.Clear();
        foreach (int c in F.WindCells)
        {
            int fu = L.FurnitureAt[c];
            if (fu >= 0 && fu < _windyMark.Length && _windyMark[fu] != _windyStamp) { _windyMark[fu] = _windyStamp; _windyFurn.Add(fu); }
        }
        _windyFurn.Sort();
        foreach (int f in _windyFurn) any |= FurnLoad(sim, f, frames, world);
        return any;
    }

    /// <summary>가구 f 하나: 받는 힘 (틀 · 바람)이 견디는 힘을 넘으면 뜯는다.</summary>
    private bool FurnLoad(Sim sim, int f, bool frames, bool world)
    {
        if (f >= FurnPlaced.Length || FurnPlaced[f] == 0 || L.Furniture[f].Cells.Length == 0) return false;
        int c0 = L.Furniture[f].Cells[0];
        bool windy = false;
        if (_windy && PropDetail > 0) foreach (int c in L.Furniture[f].Cells) if (F.WindPa[c] > 0) { windy = true; break; }
        if (!frames && !windy) return false;
        var (fm, ox, oy) = FurnForm(f);
        if (fm == null) return false;
        double px = ox * DotForm.Dot + fm.Cx, py = oy * DotForm.Dot + fm.Cy;
        int b = world ? BodyOf(c0) : -1;
        // 틀의 가속 (kg마다 — FrameForce와 같은 셈) + 바람의 위 끝이 버팀의 아래 끝을 넘지 않으면 건너뛴다 (속도만 — 결과는 같다)
        double ab = 0;
        if (frames)
        {
            if (!world) ab = Math.Sqrt(ShipAx * ShipAx + ShipAy * ShipAy);
            else if (b >= 0 && b < Bodies.Count && b < _snap.Length)
            {
                var sf = _snap[b]; var o = Bodies[b];
                double dx = px - o.Lx, dy = py - o.Ly, w2 = sf.W * sf.W, gx = -sf.Ax + w2 * dx + sf.Al * dy, gy = -sf.Ay + w2 * dy - sf.Al * dx;
                ab = Math.Sqrt(gx * gx + gy * gy);
            }
        }
        double low = HoldLowPerKg(f, c0);
        if (ab <= low && (!windy || ab + WindBoundPerKg(f, fm, ox, oy) <= low)) return false;
        var q = new Prop { Kind = 1, What = (short)f, X = px, Y = py, Kg = (float)fm.Mass, Inertia = (float)fm.I };
        double fx = 0, fy = 0;
        if (frames && (!world || b >= 0 && b < Bodies.Count) && FrameForce(q, b, 0, 0, fm.Mass, fm.I, out double ax, out double ay, out _, out _)) { fx += ax; fy += ay; }
        int cause = 0;
        if (windy && WindForce(q, fm, 0, out double wx, out double wy, out _)) { fx += wx; fy += wy; cause = WindCauseAt(c0); }
        double load = Math.Sqrt(fx * fx + fy * fy);
        if (load <= 0 || load <= AnchorHold(f)) return false;
        if (cause == 0) cause = b < 0 ? ShipACause : BodyCause(c0);
        TearLoose(sim, f, 0, 0, 0, cause, windy ? "바람에" : "가속에", load);
        return true;
    }

    /// <summary>덜컹 (몸 b의 바닥이 Δv · Δω만큼 바뀌었다 — 제 틀): 그 몸의 제자리 가구마다 m·|Δv + Δω × r| / 0.1초가 견디는 힘을 넘으면 뜯는다 (세상 빠르기 그대로 — 바닥에 대한 빠르기 −u).</summary>
    private void JoltFurniture(int b, double lx, double ly, double dw, int cause)
    {
        if (!Tearing || _sim == null || b < 0 || b >= Bodies.Count) return;
        // 위 끝 (kg마다 — 고른 몫 + 돎 × 도면 대각선)이 모든 가구의 버팀 아래면 하나씩 보지 않는다 (속도만 — 결과는 같다)
        if ((Math.Sqrt(lx * lx + ly * ly) + Math.Abs(dw) * Math.Sqrt((double)L.W * L.W + (double)L.H * L.H)) / AnchorJoltSec <= HoldLowAll()) return;
        var o = Bodies[b];
        for (int f = 0; f < L.Furniture.Count && f < FurnPlaced.Length; f++)
        {
            if (FurnPlaced[f] == 0 || L.Furniture[f].Cells.Length == 0 || BodyOf(L.Furniture[f].Cells[0]) != b) continue;
            var (fm, ox, oy) = FurnForm(f);
            if (fm == null) continue;
            double x = ox * DotForm.Dot + fm.Cx, y = oy * DotForm.Dot + fm.Cy, dx = x - o.Lx, dy = y - o.Ly;
            double ux = lx - dw * dy, uy = ly + dw * dx, u = Math.Sqrt(ux * ux + uy * uy);
            if (u / AnchorJoltSec <= HoldLowPerKg(f, L.Furniture[f].Cells[0])) continue;
            double load = fm.Mass * u / AnchorJoltSec;
            if (load <= AnchorHold(f)) continue;
            TearLoose(_sim, f, -ux, -uy, -dw, cause != 0 ? cause : BodyCause(L.Furniture[f].Cells[0]), "덜컹임에", load);
        }
    }

    /// <summary>폭풍 (첨두 kPa · 칸 차례): 그 칸에 든 제자리 가구마다 그 쪽 앞면 × 첨두 압력이 견디는 힘을 넘으면 뜯고 · 폭풍의 충격량 (P A × 양압 길이)만큼 바깥쪽으로 민다.</summary>
    private bool BlastFurniture(Sim sim, int cell, List<(int c, double kpa)> hit, int cause)
    {
        if (!Tearing) return false;
        bool any = false;
        double sx = L.X(cell) + 0.5, sy = L.Y(cell) + 0.5;
        for (int f = 0; f < L.Furniture.Count && f < FurnPlaced.Length; f++)
        {
            if (FurnPlaced[f] == 0) continue;
            double kpa = 0;
            foreach (int c in L.Furniture[f].Cells)
            {
                int k = hit.BinarySearch((c, 0), Comparer<(int c, double kpa)>.Create((a, b) => a.c.CompareTo(b.c)));
                if (k >= 0) kpa = Math.Max(kpa, hit[k].kpa);
            }
            if (kpa <= 0) continue;
            var (fm, ox, oy) = FurnForm(f);
            if (fm == null) continue;
            double x = ox * DotForm.Dot + fm.Cx, y = oy * DotForm.Dot + fm.Cy, dx = x - sx, dy = y - sy, dl = Math.Sqrt(dx * dx + dy * dy);
            if (dl < 1e-9) continue;
            int a16 = (int)Math.Round(Math.Atan2(dy, dx) / (Math.PI / 8)) & 15;
            double force = kpa * 1000 * fm.Area16[a16];
            if (force <= AnchorHold(f)) continue;
            double v = force * BlastPositiveSec / fm.Mass;
            TearLoose(sim, f, v * dx / dl, v * dy / dl, 0, cause, "폭풍에", force);
            any = true;
        }
        return any;
    }

    // ───────────────────────── 뜯김 · 다시 앉음 ─────────────────────────

    /// <summary>
    /// 요청 (폭풍 · 바람 · 가속 · 덜컹 · 시험): 가구 f를 뜯는다 — 그 꼴의 물체 (Kind 1 · 제자리 무게중심 · 바닥에 대한 빠르기 (vx, vy) · 각속도 w)가 되고 · 볼트였으면 부서진다 ·
    /// 칸 · 몸 질량을 뗀다 · 위에 놓인 것은 받침을 잃는다 · 사실 '뜯겨 나왔다' · 원인 '{how} {가구} 뜯겨 나왔다 (힘)'. 물체 Id (이미 떠났으면 −1).
    /// </summary>
    public int TearLoose(Sim sim, int f, double vx, double vy, double w, int cause, string how, double load = 0)
    {
        if (f < 0 || f >= FurnPlaced.Length || FurnPlaced[f] == 0 || L.Furniture[f].Cells.Length == 0) return -1;
        var (fm, ox, oy) = FurnForm(f);
        if (fm == null) return -1;
        var fu = L.Furniture[f];
        var e = T.Equip(fu.Type);
        bool bolted = e.AnchorG > 0;
        int c0 = fu.Cells[0];
        string name = e.Name;
        string text = bolted ? $"{how} {Ko.I(name)} 뜯겨 나왔다" : $"{how} {Ko.I(name)} 밀려났다";
        if (load > 0) text += $" ({load / 1000:0.0}kN)";
        int why = sim.Causes.Add(CauseKind.Impact, text, sim.Tick, c0, cause);
        FurnPlaced[f] = 0;
        if (bolted && FurnBroken[f] == 0) { FurnBroken[f] = 1; FurnCause[f] = why; }
        FurnMassChanged(f);
        var p = new Prop { Id = NextPropId++, Kind = 1, What = (short)f, X = ox * DotForm.Dot + fm.Cx, Y = oy * DotForm.Dot + fm.Cy, Vx = vx, Vy = vy, W = w, G = F.Grav[c0], Cause = why, Since = sim.Tick, State = 1 };
        FillBody(ref p);
        p.PushImp = (float)(fm.Mass * Math.Sqrt(vx * vx + vy * vy));
        Props.Add(p);
        FurnProp[f] = p.Id;
        // 위에 놓인 것은 받침을 잃는다 (깨어 그 자리에서 떨어진다)
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);
        for (int i = 0; i < all.Length; i++) if (all[i].On == f + 1 && all[i].State == 0) { Wake(ref all[i], why); all[i].State = 3; all[i].On = 0; }
        TornLoose++;
        _clutterDirty = true;
        if (bolted) sim.Facts.Emit(sim.Tick, FactKind.TornLoose, c0, (float)fm.Mass, why, -1, f);
        sim.Find<Sound>()?.Emit(sim, c0, (float)(70 + 10 * Math.Log10(Math.Max(1, fm.Mass))), bolted ? SoundKind.Bang : SoundKind.Scrape, why);
        return p.Id;
    }

    /// <summary>
    /// 틱 끝 (깨어 있던 것이 있었으면): 잠든 가구 물체마다 — 놓인 가구가 제자리 0.2m · 10° 안이면 다시 앉는다 (물체를 빼고 칸 · 몸에 도로 붙는다) ·
    /// 아니면 그 연료 · 열용량을 잠든 칸으로 옮긴다 (③ MoveFurnishing). 우주로 간 가구의 연료 · 열용량은 배를 떠난다.
    /// </summary>
    private void SettleFurniture(Sim sim)
    {
        bool removed = false;
        for (int i = 0; i < Props.Count; i++)
        {
            var p = Props[i];
            if (p.Kind != 1) continue;
            int f = p.What;
            if (f < 0 || f >= FurnAt.Length) continue;
            if (p.State == 5)
            {
                if (FurnAt[f] != -2) { F.MoveFurnishing(f, FurnAt[f], -2); FurnAt[f] = -2; }
                FurnProp[f] = -1;
                continue;
            }
            if (p.State != 0 || p.Piece > 0) continue;
            int c = CellOf(p);
            var (hx, hy) = FurnHome(f);
            double th = Math.IEEERemainder(p.Th, 2 * Math.PI);
            if (FurnAt[f] != -2 && T.Equip(L.Furniture[f].Type).AnchorG <= 0 && p.Pose == 0 && p.Z <= 1e-3 && Math.Abs(th) <= ReseatRad
                && (p.X - hx) * (p.X - hx) + (p.Y - hy) * (p.Y - hy) <= ReseatM * ReseatM && FurnSpotClear(f, p.Id))
            {
                if (FurnAt[f] != -1) { F.MoveFurnishing(f, FurnAt[f], -1); FurnAt[f] = -1; }
                FurnPlaced[f] = 1; FurnProp[f] = -1;
                FurnMassChanged(f);
                Props.RemoveAt(i); i--; removed = true;
                _clutterDirty = true;
                continue;
            }
            if (c >= 0 && FurnAt[f] != c && FurnAt[f] != -2 && L.IsSpace(c)) { F.MoveFurnishing(f, FurnAt[f], c); FurnAt[f] = c; }   // 우주에 나갔다 돌아와 실린 가구는 열용량 · 연료를 이미 잃었다 (PH11 검토 고침 — 남의 칸에서 빼지 않게)
        }
        if (removed) _clutterDirty = true;
    }

    /// <summary>제자리에 다른 물체 (가구 · 문짝)가 들어앉지 않았나 (다시 앉을 자리가 비었나).</summary>
    private bool FurnSpotClear(int f, int self)
    {
        foreach (var q in Props)
            if (q.Id != self && q.Kind is 1 or 3 && q.State != 5 && Array.IndexOf(L.Furniture[f].Cells, CellOf(q)) >= 0) return false;
        return true;
    }

    // ───────────────────────── 날아간 문짝 · 설비 덩어리 ─────────────────────────

    /// <summary>
    /// 날아간 문의 문짝 (BlowDoor — 한 번만): 문짝 꼴 물체 (Kind 3 · What = 문 번호)가 문 칸 가운데에서 낮은 쪽 (폭풍이면 그 반대쪽)으로 (기압 차 + 폭풍 반사 첨두) × 문 넓이 × 양압 길이의 충격량으로 난다 ·
    /// 몸이 있으면 그 질량을 몸에서 뗀다 (칸 질량은 그대로 — 문틀 · 문짝은 판 몫에 들어 있고 몸은 문짝 몫을 뺀다).
    /// </summary>
    private void BlowLeaf(Sim sim, int d, int c, double dpKPa, int lowX, int lowY, int cause)
    {
        if (d < 0 || d >= DoorLeafOut.Length || DoorLeafOut[d] != 0) return;
        DoorLeafOut[d] = 1;
        var door = L.Doors[d];
        var fm = DotForge.Make(T, "문짝");
        double dx = lowX, dy = lowY, dl = Math.Sqrt(dx * dx + dy * dy);
        if (dl < 1e-9) { dx = 1; dy = 0; dl = 1; }
        double v = Math.Min(40, Math.Max(0, dpKPa) * 1000 * Field.DoorArea * BlastPositiveSec / fm.Mass);
        // 문짝은 경첩에서 뜯겨 넓은 면을 바닥으로 누운 채 난다 (누움 2 — 꼴 틀의 +y로 누워 두께가 위아래로) · 폭은 벽 줄을 따라 · 긴 쪽 (2m)은 나는 쪽으로
        var p = new Prop { Id = NextPropId++, Kind = 3, What = (short)d, X = L.X(c) + 0.5, Y = L.Y(c) + 0.5, Th = door.Vertical ? 0 : Math.PI / 2, Pose = 2,
            Vx = v * dx / dl, Vy = v * dy / dl, G = F.Grav[c], Cause = cause, Since = sim.Tick, State = 1 };
        FillBody(ref p);
        Props.Add(p);
        int b = BodyOf(c);
        if (b >= 0 && b < Bodies.Count && Bodies[b].Mass > 0) { double cx = L.X(c) + 0.5, cy = L.Y(c) + 0.5; RemoveMass2(b, fm.Mass, cx, cy, fm.Mass * (cx * cx + cy * cy + 1.0 / 6)); }
        _clutterDirty = true;
    }

    /// <summary>
    /// 2톤 아래 덩어리 (ShedLump)에 실린 제자리 설비 f: 그 꼴의 물체 (Kind 1)가 그 덩어리의 몸 frame의 세상 자리 · 점 빠르기 · 각속도로 우주로 간다 (그 칸은 곧 우주 — 날아가는 것은 PH11) ·
    /// 몸 질량은 부른 칸 (shed)은 부른 쪽이 · 나머지 칸은 여기서 그 칸의 몸에서 뗀다 · 볼트였으면 부서짐 · 사실 '뜯겨 나왔다'.
    /// </summary>
    private void LumpFurniture(Sim sim, int f, int cause, int frame, int shed)
    {
        var (fm, ox, oy) = FurnForm(f);
        if (fm == null) return;
        var o = Bodies[frame];
        var (wx, wy) = WorldOf(frame, ox * DotForm.Dot + fm.Cx, oy * DotForm.Dot + fm.Cy);
        double vx = o.Vx - o.W * (wy - o.Y), vy = o.Vy + o.W * (wx - o.X);
        var e = T.Equip(L.Furniture[f].Type);
        int c0 = L.Furniture[f].Cells[0];
        int why = sim.Causes.Add(CauseKind.Breach, $"떨어져 나간 뼈대 조각에 실린 {Ko.I(e.Name)} 함께 날아갔다", sim.Tick, c0, cause);
        FurnPlaced[f] = 0;
        if (e.AnchorG > 0 && FurnBroken[f] == 0) { FurnBroken[f] = 1; FurnCause[f] = why; }
        var p = new Prop { Id = NextPropId++, Kind = 1, What = (short)f, X = wx, Y = wy, Th = o.Th, Vx = vx, Vy = vy, W = o.W, Cause = why, Since = sim.Tick, State = 5, Host = -1 };   // 이미 세상 틀 (PH11)
        FillBody(ref p);
        Props.Add(p);
        FurnProp[f] = p.Id;
        TornLoose++;
        sim.Facts.Emit(sim.Tick, FactKind.TornLoose, c0, (float)fm.Mass, why, -1, f);
        // 나머지 칸의 설비 몫도 지금 그 칸의 몸에서 뗀다 (같은 덩어리의 칸은 뒤에 뗄 몫이 0 · 다른 몸에 남은 칸이면 그 몸에서 — 질량이 두 번 세지지 않게)
        foreach (int c in L.Furniture[f].Cells)
        {
            if (c == shed) continue;   // 부른 칸은 부른 쪽이 뗐다
            double share = _mass[c] - _smass[c];
            int b = BodyOf(c);
            if (share > 0 && b >= 0 && b < Bodies.Count && Bodies[b].Mass > 0) RemoveMass(b, share, L.X(c) + 0.5, L.Y(c) + 0.5, 1.0 / 6);
            RecalcCellMass(c);
        }
    }
}
