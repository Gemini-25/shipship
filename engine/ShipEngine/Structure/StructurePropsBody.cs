namespace ShipEngine;

/// <summary>
/// ④ 몸 물체 (docs/PHYSICS.md PH14): 제 힘을 잃은 동안 (바람에 발이 떨어졌다) 사람 · 로봇 몸은 원판 물체 (Kind 5 사람 · 6 로봇 — 지름 0.5m · 70 · 120kg)로 ④가 셈한다 —
/// 바람에 밀리고 · 벽 · 설비에 부딪히면 그 몸이 맞음 장부에 남고 (50J 넘게 — ⑦ · ⑥이 읽는다) · 작은 구멍에 빨려 붙어 막고 (물체 막음) · 큰 구멍이면 우주로 난다 (PH11).
/// 꼬리표 (Spare0)는 열어 보지 않는다 (사람 몸 번호 · −1−로봇 번호 — ⑧이 정한다). 만들고 거두는 것은 ⑧의 요청이다.
/// </summary>
public sealed partial class Structure
{
    /// <summary>몸이 벽 · 설비에 부딪혀 다치는 에너지 (J — 맞음 장부에 남긴다).</summary>
    public const double BodyHitJ = 50;

    /// <summary>선 몸이 받는 바람: 끌림 계수 · 앞면 (서 있으면 0.7m² · 낮으면 0.3m²) · 젖은 바닥 발 마찰 · 로봇 바퀴 · 발 마찰.</summary>
    public const double BodyCd = 1.0, StandArea = 0.7, LowArea = 0.3, WetFootMu = 0.1, RobotFootMu = 0.8;
    /// <summary>셈 (찍지 않는다 — 보기용): 바람에 발이 떨어진 사람 · 밀린 로봇.</summary>
    public long PeopleKnocked, RobotsKnocked;
    private readonly List<(int tag, double n)> _holds = new();   // 이번 틱 붙잡음 요청 (⑧ — 찍지 않는다: 요청은 같은 틱에 쓰고 ④가 틱 끝에 비운다)

    /// <summary>요청 (⑧): 그 몸이 곁의 것을 붙잡고 버틴다 — 손힘 (N)만큼 발 마찰에 더한다 (이번 틱).</summary>
    public void Hold(int tag, double newtons)
    {
        for (int i = 0; i < _holds.Count; i++) if (_holds[i].tag == tag) { _holds[i] = (tag, Math.Max(_holds[i].n, newtons)); return; }
        _holds.Add((tag, newtons));
    }

    /// <summary>
    /// 요청 (⑧ — 걷기 전 · 장이 흐르기 전): 선 몸들 (꼬리표 · 칸 · 무게 · 낮음 — 틱 처음 자리)의 발 디딤을 셈한다 — 틱 처음 기압의 구멍 입 (구멍이 나는 그 순간 ·
    /// 작은 구멍에 빨려 붙으면 이번 틱 흐름부터 막는다) · 지난 틱 돌풍. 같은 몸들은 ④가 장이 흐른 뒤 이 틱 돌풍으로 한 번 더 본다 (<see cref="FootingAfter"/>).
    /// </summary>
    public void Footing(Sim sim, ReadOnlySpan<PropActor> who)
    {
        _feet.Clear();
        foreach (var a in who) _feet.Add(a);
        Footing(sim, false);
    }
    private readonly List<PropActor> _feet = new();   // 이번 틱 선 몸 (⑧ 요청 — 찍지 않는다: 같은 틱에 쓰고 ④가 틱 끝에 비운다)

    /// <summary>④ (장이 흐른 뒤): 같은 선 몸들을 이 틱 돌풍 · 틱 처음으로 되짚은 구멍 입으로 다시 본다 (한 틱 안에 빈 방의 돌풍도 놓치지 않는다) · 요청을 비운다.</summary>
    private void FootingAfter(Sim sim)
    {
        if (_feet.Count > 0) Footing(sim, true);
        _feet.Clear(); _holds.Clear();
    }

    /// <summary>
    /// 발 디딤 (PH14 — 사람 · 로봇이 제 힘을 잃는 것은 월드가 셈한다): 선 몸마다 그 칸의 바람 (돌풍 동압 · 구멍 입으로 모이는 흐름 가운데 센 것)이 미는 힘
    /// ½ρv²·Cd·A가 발 마찰 μ·m·g (+ 붙잡은 손힘)를 넘으면 발이 떨어진다 — 몸 물체가 되어 1m 미끄러지며 얻을 빠르기로 바람 쪽에서 시작한다 (그 뒤는 ④가 민다).
    /// </summary>
    private void Footing(Sim sim, bool afterFlow)
    {
        for (int k = 0; k < _feet.Count; k++)
        {
            var a = _feet[k];
            int c = a.Cell;
            if (c < 0 || c >= L.N || !F.Passable(c) || F.Grav[c] < Field.LowG) continue;   // 무중력에서 떠 있는 몸은 ⑧이 떠다님으로 셈한다 (발 디딤이 없다)
            double wx = F.WindX[c], wy = F.WindY[c], pa = Math.Sqrt(wx * wx + wy * wy) >= 0.5 ? F.WindPa[c] : 0;
            double qm = F.HoleMouthPa(c, out int mouth, afterFlow ? Math.Exp(F.WindK[c] * _tickDt) : 1);
            if (qm > pa && mouth >= 0) { pa = qm; wx = L.X(mouth) - L.X(c); wy = L.Y(mouth) - L.Y(c); }
            if (pa < 1 || RagdollOf(a.Tag) >= 0) continue;
            bool robot = a.Tag < 0;
            double drag = pa * BodyCd * (a.Low != 0 ? LowArea : StandArea);
            double mu = robot ? RobotFootMu : F.Stored(T.sWater, c) > WetMol ? WetFootMu : FootMu;
            double grip = mu * Math.Max(0.05, F.Grav[c]) * a.Kg * Gravity, hold = 0;
            foreach (var h in _holds) if (h.tag == a.Tag) hold = h.n;
            if (drag <= grip + hold) continue;
            int cause = sim.Causes.Add(CauseKind.Pressure, robot ? "빨아들이는 바람에 로봇이 밀려 미끄러졌다" : "빨아들이는 바람에 발이 떨어졌다", sim.Tick, c, WindCauseAt(c));
            double v = Math.Sqrt(2 * (drag - grip - hold) / a.Kg), wl = Math.Sqrt(wx * wx + wy * wy);
            Ragdoll(sim, a.Tag, c, robot, a.Low != 0, cause, wl > 0 ? v * wx / wl : 0, wl > 0 ? v * wy / wl : 0);
            if (robot) RobotsKnocked++; else PeopleKnocked++;
            if (afterFlow) DropActor(a.Tag);
        }
    }

    /// <summary>이번 틱 걷는 원판에서 그 꼬리표를 뺀다 (장이 흐른 뒤 몸 물체가 된 몸 — ⑧이 틱 끝에 알린 원판이 제 몸 물체에 부딪히지 않게 · 다음 틱 ⑧은 몸 물체인 이를 알리지 않는다).</summary>
    private void DropActor(int tag)
    {
        int n = 0;
        for (int i = 0; i < _nActors; i++) if (_actors[i].Tag != tag) _actors[n++] = _actors[i];
        _nActors = n;
    }

    /// <summary>그 꼬리표의 몸 물체 (목록 차례 · 없으면 −1).</summary>
    public int RagdollOf(int tag)
    {
        if (BodyProps() == 0) return -1;   // 몸 물체가 없으면 훑지 않는다 (평시 — 사람마다 틱에 여러 번 묻는다)
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);
        for (int i = 0; i < all.Length; i++) if (all[i].Kind is 5 or 6 && all[i].Spare0 == tag) return i;
        return -1;
    }

    private int _nBodyProps, _bodyPropsAt = -1;   // 몸 물체 수 · 그때 물체 수 (찍지 않는다 — 물체 수가 바뀌면 다시 센다: 날아가 지워짐 · 깨짐 · 되살림)
    private int BodyProps()
    {
        if (_bodyPropsAt != Props.Count)
        {
            _nBodyProps = 0;
            foreach (ref readonly var p in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props)) if (p.Kind is 5 or 6) _nBodyProps++;
            _bodyPropsAt = Props.Count;
        }
        return _nBodyProps;
    }

    /// <summary>요청 (⑧): 그 몸이 제 힘을 잃었다 — 칸 가운데에 몸 물체를 세운다 (이미 있으면 그것) · 누웠으면 누운 꼴 · 원인 (끌려간 까닭) · 발이 떨어진 순간의 빠르기 (끌림이 이긴 만큼). Id를 돌려준다.</summary>
    public int Ragdoll(Sim sim, int tag, int cell, bool robot, bool low, int cause, double vx = 0, double vy = 0)
    {
        int i = RagdollOf(tag);
        if (i >= 0) return Props[i].Id;
        if (cell < 0 || cell >= L.N) return -1;
        var p = new Prop { Id = NextPropId++, Kind = (byte)(robot ? 6 : 5), Spare0 = tag, X = L.X(cell) + 0.5, Y = L.Y(cell) + 0.5, Pose = (byte)(low ? 1 : 0), State = 1, Cause = cause, Since = _now, G = F.Grav[cell], Vx = vx, Vy = vy };
        FillBody(ref p);
        // 장부: 몸은 배와 함께 가던 운동량을 지니고 들어오고 (바깥 몫 — 우주로 간 것의 반대) · 발이 떨어진 순간의 빠르기는 바람이 준 것 (기체가 잃는다)
        var still = p; still.Vx = still.Vy = 0;
        var (m, inertia) = MassOf(p);
        var (x, y, vx0, vy0, w0) = PropWorld(still);
        var (_, _, wx, wy, _) = PropWorld(p);
        PropPxGone -= m * vx0; PropPyGone -= m * vy0; PropLGone -= m * (x * vy0 - y * vx0) + inertia * w0;
        GasPx -= m * (wx - vx0); GasPy -= m * (wy - vy0); GasL -= m * (x * (wy - vy0) - y * (wx - vx0));
        PlugNow(sim, ref p, cell);
        BodyProps();
        Props.Add(p);
        _nBodyProps++; _bodyPropsAt = Props.Count;
        return p.Id;
    }

    /// <summary>
    /// 곁 벽의 작은 구멍이 몸을 무게보다 세게 빨아들이면 (몸 앞면이 구멍 × 1.2보다 크다) 그 자리에서 빨려 붙어 막는다 — 칸 가운데에서 구멍 벽까지 0.25m는
    /// 한 틱 (2.4초)보다 훨씬 짧게 닿으니 (빨아들이는 힘 수 kN · 0.1초 언저리) 이번 틱 흐름부터 막는다 (요청은 ③ 장이 흐르기 전 — 기압은 틱 처음 값).
    /// 큰 구멍 (몸이 지나는)은 ④가 이번 틱에 날려 보낸다. 빨아들이는 구멍이 여럿이면 가장 센 것.
    /// </summary>
    private void PlugNow(Sim sim, ref Prop p, int cell)
    {
        Span<int> nb = stackalloc int[4];
        L.Neighbors(cell, nb);
        var fm = PropForm(p);
        double face = MinFace(fm), best = 0;
        int hole = -1;
        foreach (int j in nb)
        {
            if (j < 0 || L.Kind[j] != CellKind.Wall || F.Hole[j] <= 0.01f || face <= F.Hole[j] * PlugMargin) continue;
            double net = SuckNet(p, j, face);
            if (net > best) { best = net; hole = j; }
        }
        if (hole < 0) return;
        double r = SamplesOf(fm).reach, nx = Math.Sign(L.X(hole) - L.X(cell)), ny = nx == 0 ? Math.Sign(L.Y(hole) - L.Y(cell)) : 0;
        p.X = L.X(cell) + 0.5 + nx * Math.Max(0, 0.5 - r); p.Y = L.Y(cell) + 0.5 + ny * Math.Max(0, 0.5 - r);
        if (!HoleHit(sim, ref p, hole) || p.State != 6) return;
        float area = (float)(Math.Min(F.Hole[hole], face) * PlugSeal);
        if (F.PlugProp[hole] == 0) _plugCells.Add(hole);
        if (area > F.PlugProp[hole]) F.SetPlugProp(hole, area);
    }

    /// <summary>요청 (⑧): 몸이 다시 제 힘으로 선다 (또는 업혀 간다) — 몸 물체를 거둔다 (몸의 자리는 ⑧이 이미 읽었다).</summary>
    public void EndRagdoll(int tag)
    {
        int i = RagdollOf(tag);
        if (i < 0) return;
        // 장부: 몸이 지닌 운동량을 가지고 나간다 (다시 제 힘으로 — 바깥 몫)
        var p = Props[i];
        var (m, inertia) = MassOf(p);
        var (x, y, vx, vy, w) = PropWorld(p);
        PropPxGone += m * vx; PropPyGone += m * vy; PropLGone += m * (x * vy - y * vx) + inertia * w;
        BodyProps();
        Props.RemoveAt(i);
        _nBodyProps--; _bodyPropsAt = Props.Count;
    }

    /// <summary>몸 물체의 꼴 (사람 · 로봇 · 누움).</summary>
    private DotForm BodyForm(in Prop p) => DotForge.Make(T, p.Kind == 5 ? "사람 몸" : "로봇 몸", lie: p.Pose);

    /// <summary>몸 물체가 부딪혔다 (잔걸음의 맞음): 50J 넘게면 그 몸의 맞음 장부 (꼬리표 · 원인 '끌려가다 부딪혔다').</summary>
    private void BodyHit(Sim sim, in Prop p, double e, double speed)
    {
        if (e < BodyHitJ) return;
        int c = Math.Max(0, CellOf(p));
        int cause = sim.Causes.Add(CauseKind.Impact, $"끌려가다 부딪혔다 ({speed:0.0}m/s)", sim.Tick, c, p.Cause);
        PropHits.Add(new PropHit { Tag = p.Spare0, Cell = c, E = (float)e, Kg = p.Kg, Speed = (float)speed, Cause = cause, Tick = sim.Tick });
    }

    /// <summary>나는 몸 물체가 배 둘레에서 있는 칸 (배 틀 — 도면 밖이면 가장 가까운 끝 칸): ⑦ · ⑧이 그 몸의 둘레 (진공)를 읽는다.</summary>
    public int SpaceCellOf(in Prop p)
    {
        double x = p.X, y = p.Y;
        if (Flying(p) && Bodies.Count > 0) (x, y) = LocalOf(0, p.X, p.Y);
        int ix = Math.Clamp((int)Math.Floor(x), 0, L.W - 1), iy = Math.Clamp((int)Math.Floor(y), 0, L.H - 1);
        return L.Index(ix, iy);
    }
}
