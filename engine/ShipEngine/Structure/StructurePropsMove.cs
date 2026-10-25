namespace ShipEngine;

/// <summary>
/// ④ 물체 움직임 (docs/PHYSICS.md §3-1 · §3-5 · §4-1 — PH4a 평면 뼈대): 깨어 있는 물체만 잔걸음으로 셈한다 — 바닥 마찰 (쿨롱 · 타원 한계면) · 정확한 걸음 ·
/// 벽 잔칸 · 닫힌 문 · 제자리 가구 기둥과의 닿음 (StructurePropsHit.cs) · 잠. 깨우기는 요청뿐이다 (다리가 칸으로 옮긴 것은 빠르기 0 · 놓임 그대로).
/// 놓인 물체 (State 0 · 6)는 건너뛴다 — 건너뛸지는 찍힌 상태로만 정한다 (되살린 세계와 같다). 깨어 있는 것이 없으면 곧장 돌아간다.
/// </summary>
public sealed partial class Structure
{
    private readonly Dictionary<int, DotForm> _propForms = new();   // (물건 번호 · 누움)마다 도트 꼴 (찍지 않는다 — 표에서 늘 같게)
    private DotForm?[] _propFormAt = Array.Empty<DotForm?>();         // 같은 것을 물건 번호 × 8 + 누움 차례로 (사전을 찾지 않게)
    private string[]? _itemNames;

    /// <summary>물체의 도트 꼴 (물건: 사람 표의 그 물건 · 누움 Pose).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    public DotForm PropForm(in Prop p)
    {
        if (p.Piece > 0) return PieceForm(p);   // 조각 (PH8 — 온전한 꼴의 가면)
        int key = p.What * 8 + p.Pose;
        if ((uint)key < (uint)_propFormAt.Length && _propFormAt[key] is { } at) return at;   // 표 차례로 곧장 (섬 짝마다 잔걸음마다 부른다 — 속도만)
        if (_propForms.TryGetValue(key, out var fm)) return fm;
        _itemNames ??= T.People.Items.Keys.ToArray();
        string name = p.What >= 0 && p.What < _itemNames.Length ? _itemNames[p.What] : "_default";
        _propForms[key] = fm = DotForge.Make(T, name, lie: p.Pose);
        if (key >= 0 && key < 8 * (_itemNames.Length + 1))
        {
            if (_propFormAt.Length == 0) _propFormAt = new DotForm?[8 * (_itemNames.Length + 1)];
            _propFormAt[key] = fm;
        }
        return fm;
    }

    /// <summary>물체가 움직이는 중인가 (미끄러짐 · 넘어지는 중 · 떨어지는 중 · 떠 있음 — 놓임 · 막음 · 우주 · 낌은 셈하지 않는다).</summary>
    public static bool Awake(in Prop p) => p.State is 1 or 2 or 3 or 4;

    /// <summary>
    /// 요청: 물체를 민다 (충격량 jx · jy N·s · 돌림 jw N·m·s · 원인) — 깨어 미끄러진다. 처음 깨면 꼴에서 질량 · 회전관성 · 겉 재질을 채운다. 없으면 거짓.
    /// </summary>
    public bool Shove(int id, double jx, double jy, double jw = 0, int cause = 0)
    {
        int i = IndexOf(id);
        if (i < 0 || Props[i].State is 5 or 6 or 7) return false;
        var p = Props[i];
        if (p.Kg <= 0) FillBody(ref p);
        p.Vx += jx / p.Kg; p.Vy += jy / p.Kg; p.W += jw / p.Inertia;
        if (!Awake(p)) p.State = 1;
        p.Asleep = 0; p.Still = 0;
        if (cause != 0) { p.Cause = cause; p.PushImp = (float)Math.Sqrt(jx * jx + jy * jy); }
        Props[i] = p;
        return true;
    }

    /// <summary>꼴에서 몸 값을 채운다 (꼴 번호 · 질량 · 회전관성 · 겉 재질 — 바닥에 닿은 기둥이 가장 많은 재질).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void FillBody(ref Prop p)
    {
        var fm = PropForm(p);
        p.Form = fm.Id; p.Kg = (float)fm.Mass; p.Inertia = (float)fm.I; p.Mat = BaseMatOf(fm);
        if (fm.Round) p.Flags |= 2;
    }

    /// <summary>한 틱 움직임: 몸 (조각 · 배) → 물체 (깨어 있는 것만).</summary>
    private void Motion(Sim sim, double dt, long t)
    {
        _tickDt = dt;
        Track?.Begin(sim.Tick, dt);
        MoveBodies(sim, dt); t = Prof.Lap("구조:몸", t);
        MoveProps(sim, dt); Prof.Lap("구조:물체", t);
        sim.Facts.SubNow = 0;
        Track?.End();
    }

    /// <summary>틱 안 기록 (보기용 — 찍지 않는다 · null이면 셈하지 않는다: 관전 화면이 제 세계 · 다시보기 세계에 켠다 · 법칙 13 켬 = 끔).</summary>
    public MotionTrack? Track;

    /// <summary>
    /// 물체 길은 처음부터 다 옮긴 기계어로 (물체 예산 고침): 감압 · 점화 · 덜컹 같은 드문 일에만 도는 길이라 단계 번역이 그 일의 첫 몇십 틱을 덜 옮긴 코드로 돌렸다
    /// (감압 첫 틱 '구조:물체' 0.23 ~ 0.46ms · 단계 번역을 끄면 0.13ms). 셈 · 차례는 그대로다 (결과가 같다).
    /// </summary>
    internal const System.Runtime.CompilerServices.MethodImplOptions Hot = System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization;

    public const double Gravity = 9.8, WetShare = 0.33, WetMol = 50, SleepV = 1e-3, SleepW = 0.01, SleepSec = 0.5, MaxPropH = 0.1, MaxPropTurn = 0.1;
    public const int MaxPropSteps = 1024, LightPropSteps = 4;   // 섬마다 한 틱 잔걸음 상한 (온전 · 가벼운 셈 — 상상 복제)

    /// <summary>물체의 질량 · 회전관성 (아직 깨지 않아 몸 값이 비었으면 꼴에서).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private (double kg, double inertia) MassOf(in Prop p)
    {
        if (p.Kg > 0) return (p.Kg, p.Inertia);
        var fm = PropForm(p);
        return (fm.Mass, fm.I);
    }

    /// <summary>
    /// 물체의 세상 자리 · 빠르기 · 각속도 (몸이 있으면 그 칸 몸의 자세 · 움직임으로 옮긴다 — 물체 값은 바닥에 대한 것) · 몸이 없으면 도면 값 그대로.
    /// </summary>
    public (double x, double y, double vx, double vy, double w) PropWorld(in Prop p)
    {
        int b = Bodies.Count > 0 ? BodyOf(CellOf(p)) : -1;
        if (b < 0 || b >= Bodies.Count || Bodies[b].Mass <= 0) return (p.X, p.Y, p.Vx, p.Vy, p.W);
        var o = Bodies[b];
        double c = Math.Cos(o.Th), s = Math.Sin(o.Th), dx = p.X - o.Lx, dy = p.Y - o.Ly, rx = c * dx - s * dy, ry = s * dx + c * dy;
        return (o.X + rx, o.Y + ry, o.Vx - o.W * ry + c * p.Vx - s * p.Vy, o.Vy + o.W * rx + s * p.Vx + c * p.Vy, o.W + p.W);
    }

    /// <summary>물체들의 세상 운동량 · 원점에 대한 각운동량 (우주로 간 것이 가져간 몫 포함 — 법칙 시험의 장부).</summary>
    public (double px, double py, double l) PropMomentum()
    {
        double px = PropPxGone, py = PropPyGone, l = PropLGone;
        foreach (var p in Props)
        {
            if (p.State == 5) continue;
            var (m, I) = MassOf(p);
            var (x, y, vx, vy, w) = PropWorld(p);
            px += m * vx; py += m * vy; l += m * (x * vy - y * vx) + I * w;
        }
        return (px, py, l);
    }

    /// <summary>
    /// 깨어 있는 물체를 섬마다 (가장 작은 Id 차례 — 물체끼리 닿을 만한 것을 한 섬으로) 한 틱을 잔걸음으로 — 깨우기 (막음 풀림 · 바람 · 덜컹 · 옆 가속 · 폭풍) 뒤 ·
    /// 몸이 있으면 바닥 · 벽이 준 몫을 틱 끝에 그 몸이 되받는다.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void MoveProps(Sim sim, double dt)
    {
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);   // 잠든 것은 베끼지 않고 상태만 본다 (조용한 틱의 거의 전부)
        if (_hostOf.Length < all.Length) GrowFrames(all.Length);   // 물체를 더할 때 (조용한 틱) 미리 — 감압 · 점화 첫 틱에 만들지 않는다
        bool any = false, plugged = false, world = Bodies.Count > 0;
        for (int i = 0; i < all.Length; i++) { any |= Awake(all[i]); plugged |= all[i].State == 6; }
        _windy = F.WindCells.Count > 0; _tickDt = dt;
        if (all.Length > 0) any |= GasBlasts(sim);             // 폭풍 (② 이 틱 기체 폭발 — 기체가 민 것은 바깥 몫이다)
        _framed = false;
        if (all.Length > 0 && (world || ShipAx != 0 || ShipAy != 0)) BeginFrames(all, world);
        if (plugged) any |= Unplug(all);                     // 구멍을 덧댔거나 누르는 힘이 빠졌으면 떨어진다
        if (_windy && all.Length > 0) any |= WakeByWind(all);   // 깨우기: 바람 · 구멍 입 (③ 이 틱 바람이 분 칸 곁)
        if (_jolts.Count > 0) any |= WakeByJolts(all);        // 덜컹 (몸 걸음 안의 밂)
        if (_framed) any |= WakeByFrame(all, world);           // 옆 가속 (점화 · 뿜는 틈 · 도는 몸)
        if (_nActors > 0 && PropDetail > 0 && all.Length > 0) any |= WakeByActors(all);   // 걷는 이 (지난 틱 길에 닿을 잠든 것)
        if (all.Length > 0) any |= WakeByGravity(all);         // 중력이 사라진 칸의 놓인 것은 들뜬다 (PH7)
        if (any)
        {
            BuildClearance();
            StepIslands(sim, all, dt, world);   // 섬마다 함께 걷는다 — 잔걸음 동안 목록은 그대로다 (빼는 것은 틱 끝 · 맞음은 장부에만 더한다)
        }
        if (world && all.Length > 0) Reactions(all);
        if (_breakHits.Count > 0) { long tb = Prof.Start(); Breaks(sim, world); Prof.Lap("구조:깨짐", tb); }       // 깨짐 · 이음매 · 찌그러짐 · 쏟음 (PH8 — 몸이 되받은 뒤 · 맞은 그 때의 상태에서)
        if (_noises.Count > 0) { long tn = Prof.Start(); EmitNoises(sim); Prof.Lap("구조:물체 소리", tn); }       // 물체 소리 (방마다 · 갈래마다 한 번)
        if (any)
        {
            plugged = false;
            foreach (var p in all) plugged |= p.State == 6;
            RemoveSpaced(sim, world);
            all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);
        }
        WritePlugs(all, plugged);   // 물체 막음 → 장 (다음 틱 흐름부터)
    }

    /// <summary>틱 안 기록의 끝 열쇠틀 (그 때에 아직 없으면 — 사라짐 · 막음은 그 사건으로).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static void TrackEnd(MotionTrack? tr, in Prop p, double t, ref double lastKey)
    {
        if (tr == null || double.IsPositiveInfinity(lastKey)) return;
        byte ev = p.State == 5 ? MotionTrack.EvGone : p.State == 6 ? MotionTrack.EvPlug : MotionTrack.EvNone;
        if (t > lastKey + 1e-9 || ev != MotionTrack.EvNone) tr.Add(MotionTrack.WhoProp, p.Id, Math.Max(t, lastKey), p.X, p.Y, p.Th, p.Vx, p.Vy, p.W, ev, p.Z, p.Vz);
        lastKey = double.PositiveInfinity;   // 이 틱은 끝 (한 번만)
    }

    /// <summary>물체 몸이 그 원판 (가운데 ax, ay · 반지름 r)에 닿았나 (가운데 · 껍질 꼭짓점 · 테두리 표본 가운데 하나가 원판 안).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static bool TouchesActor(in Prop p, (double[] x, double[] y, double reach) sm, double ax, double ay, double r)
    {
        double r2 = r * r;
        if ((p.X - ax) * (p.X - ax) + (p.Y - ay) * (p.Y - ay) < r2) return true;
        double cs = Math.Cos(p.Th), sn = Math.Sin(p.Th);
        for (int k = 0; k < sm.x.Length; k++)
        {
            double x = p.X + cs * sm.x[k] - sn * sm.y[k] - ax, y = p.Y + sn * sm.x[k] + cs * sm.y[k] - ay;
            if (x * x + y * y < r2) return true;
        }
        return false;
    }

    /// <summary>
    /// 물체가 원판에 새로 닿았다 (서로 빠르기 rvx, rvy) → 맞음 장부 (꼬리표 · 그 칸 · ½m v_rel² · 질량 · 서로 빠르기 · 원인 — 0.5m/s 넘게 부딪으면 '날아든 …에 맞았다' 마디를 민 원인에 잇는다).
    /// 원판이 물체를 되튀기는 몫은 섬의 충격량이 셈한다 (PH6b) — 여기는 맞음만 적는다.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private void ActorHit(Sim sim, in Prop p, in PropActor a, double rvx, double rvy)
    {
        double v2 = rvx * rvx + rvy * rvy, speed = Math.Sqrt(v2);
        int cause = speed > BounceV ? sim.Causes.Add(CauseKind.Impact, $"날아든 {PropName(p)}에 맞았다", sim.Tick, a.Cell, p.Cause) : p.Cause;
        PropHits.Add(new PropHit { Tag = a.Tag, Cell = a.Cell, E = (float)(0.5 * p.Kg * v2), Kg = p.Kg, Speed = (float)speed, Cause = cause, Tick = sim.Tick });
    }

    /// <summary>코리올리 (−2ω × v — 도는 몸의 틀): 바닥에 대한 빠르기를 phi만큼 돌린다 (크기는 그대로 — 에너지를 꾸며내지 않는다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static void Coriolis(ref Prop p, double phi)
    {
        double c = Math.Cos(phi), s = Math.Sin(phi), vx = p.Vx;
        p.Vx = c * vx - s * p.Vy; p.Vy = s * vx + c * p.Vy;
    }

    /// <summary>
    /// 바닥 마찰 (Goyal–Ruina · Howe–Cutkosky 타원 어림): s = √(|v|² + (ρω)²) · a = −μk g' v/s · α = −μk m g' ρ² ω/(s I) — 잔걸음 안에서는 비를 붙들고
    /// 빠르기 · 각속도가 0에 닿으면 그때 멈춘다 (넘어 뒤집히지 않는다). exact면 자리도 (x += v t + ½ a t²).
    /// </summary>
    private static void Friction(ref Prop p, double h, double k, double m, double I, double rho, bool exact) => Friction(ref p, h, k, m, I, rho, exact, out _);

    /// <summary>잔걸음의 바닥 마찰 (그 물체 걸음 값으로 — 누운 둥근 것은 구른다 · 떠 있으면 r.Gp가 0이라 그대로 간다).</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static void FloorFriction(ref Prop p, in PropRun r, double h, bool exact, out double stopAt)
    {
        if (r.Roll && r.Gp > 0) RollFriction(ref p, h, r.Gp * r.MuK, r.Gp, r.RAx, r.RAy, r.M, r.In, r.Rho, r.Radius, exact, out stopAt);
        else Friction(ref p, h, r.Gp * r.MuK, r.M, r.In, r.Rho, exact, out stopAt);
    }

    /// <summary>바닥 마찰 — 미끄러짐이 그친 때 (잔걸음 처음에서 초 · 그치지 않았으면 −1)도 (틱 안 기록의 '섰다').</summary>
    [System.Runtime.CompilerServices.MethodImpl(Hot)]
    private static void Friction(ref Prop p, double h, double k, double m, double I, double rho, bool exact, out double stopAt)
    {
        double rem = h;
        stopAt = -1;
        for (int it = 0; it < 3 && rem > 0; it++)
        {
            double v2 = p.Vx * p.Vx + p.Vy * p.Vy, rw = rho * p.W, s = Math.Sqrt(v2 + rw * rw);
            if (k <= 0 || s == 0) break;
            double ax = -k * p.Vx / s, ay = -k * p.Vy / s, al = -k * m * rho * rho * p.W / (s * I);
            double tv = v2 > 0 ? s / k : double.PositiveInfinity, tw = p.W != 0 ? s * I / (k * m * rho * rho) : double.PositiveInfinity;
            double t = Math.Min(rem, Math.Min(tv, tw));
            if (exact) { p.X += p.Vx * t + 0.5 * ax * t * t; p.Y += p.Vy * t + 0.5 * ay * t * t; p.Th += p.W * t + 0.5 * al * t * t; }
            if (t == tv) { p.Vx = 0; p.Vy = 0; stopAt = h - rem + t; } else { p.Vx += ax * t; p.Vy += ay * t; }
            if (t == tw) p.W = 0; else p.W += al * t;
            rem -= t;
        }
        if (exact && rem > 0 && (p.Vx != 0 || p.Vy != 0 || p.W != 0)) { p.X += p.Vx * rem; p.Y += p.Vy * rem; p.Th += p.W * rem; }   // 힘이 없다 (무중력 · 마찰 0)
    }
}
