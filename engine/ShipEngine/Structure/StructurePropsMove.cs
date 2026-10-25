namespace ShipEngine;

/// <summary>
/// ④ 물체 움직임 (docs/PHYSICS.md §3-1 · §3-5 · §4-1 — PH4a 평면 뼈대): 깨어 있는 물체만 잔걸음으로 셈한다 — 바닥 마찰 (쿨롱 · 타원 한계면) · 정확한 걸음 ·
/// 벽 잔칸 · 닫힌 문 · 제자리 가구 기둥과의 닿음 (StructurePropsHit.cs) · 잠. 깨우기는 요청뿐이다 (다리가 칸으로 옮긴 것은 빠르기 0 · 놓임 그대로).
/// 놓인 물체 (State 0 · 6)는 건너뛴다 — 건너뛸지는 찍힌 상태로만 정한다 (되살린 세계와 같다). 깨어 있는 것이 없으면 곧장 돌아간다.
/// </summary>
public sealed partial class Structure
{
    private readonly Dictionary<int, DotForm> _propForms = new();   // (물건 번호 · 누움)마다 도트 꼴 (찍지 않는다 — 표에서 늘 같게)
    private string[]? _itemNames;

    /// <summary>물체의 도트 꼴 (물건: 사람 표의 그 물건 · 누움 Pose).</summary>
    public DotForm PropForm(in Prop p)
    {
        int key = p.What * 8 + p.Pose;
        if (_propForms.TryGetValue(key, out var fm)) return fm;
        _itemNames ??= T.People.Items.Keys.ToArray();
        string name = p.What >= 0 && p.What < _itemNames.Length ? _itemNames[p.What] : "_default";
        _propForms[key] = fm = DotForge.Make(T, name, lie: p.Pose);
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
    private void FillBody(ref Prop p)
    {
        var fm = PropForm(p);
        p.Form = fm.Id; p.Kg = (float)fm.Mass; p.Inertia = (float)fm.I; p.Mat = BaseMat(fm);
        if (fm.Round) p.Flags |= 2;
    }

    private static byte BaseMat(DotForm fm)
    {
        Span<int> n = stackalloc int[256];
        int low = int.MaxValue, best = -1;
        for (int i = 0; i < fm.Columns; i++) if (fm.Top[i] > fm.Bot[i]) low = Math.Min(low, fm.Bot[i]);
        for (int i = 0; i < fm.Columns; i++)
        {
            if (fm.Top[i] <= fm.Bot[i] || fm.Bot[i] != low) continue;
            if (++n[fm.Mat[i]] > (best < 0 ? 0 : n[best])) best = fm.Mat[i];
        }
        return (byte)Math.Max(0, best);
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

    public const double Gravity = 9.8, WetShare = 0.33, WetMol = 50, SleepV = 1e-3, SleepW = 0.01, SleepSec = 0.5, MaxPropH = 0.1, MaxPropTurn = 0.1;
    public const int MaxPropSteps = 1024;

    /// <summary>물체의 질량 · 회전관성 (아직 깨지 않아 몸 값이 비었으면 꼴에서).</summary>
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
    /// 깨어 있는 물체마다 (Id 차례 — 지금은 물체끼리 닿지 않아 하나가 한 섬) 한 틱을 잔걸음으로 — 깨우기 (막음 풀림 · 바람 · 덜컹 · 옆 가속 · 폭풍) 뒤 ·
    /// 몸이 있으면 바닥 · 벽이 준 몫을 틱 끝에 그 몸이 되받는다.
    /// </summary>
    private void MoveProps(Sim sim, double dt)
    {
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);   // 잠든 것은 베끼지 않고 상태만 본다 (조용한 틱의 거의 전부)
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
        if (any)
        {
            BuildClearance();
            for (int i = 0; i < all.Length; i++)
            {
                if (!Awake(all[i])) continue;
                if (world) _stepped[i] = true;
                StepProp(sim, ref all[i], dt, world ? _hostOf[i] : -1);   // 잔걸음 동안 목록은 그대로다 (빼는 것은 틱 끝 · 맞음은 장부에만 더한다)
            }
        }
        if (world && all.Length > 0) Reactions(all);
        if (any)
        {
            plugged = false;
            foreach (var p in all) plugged |= p.State == 6;
            RemoveSpaced(sim, world);
            all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);
        }
        WritePlugs(all, plugged);   // 물체 막음 → 장 (다음 틱 흐름부터)
    }

    /// <summary>
    /// 물체 하나의 한 틱: 잔걸음마다 길이 ℓ = max(2도트, 틈새 − 닿는 반지름) · 0.1rad · (마찰이 있으면) 0.1초를 넘지 않게 —
    /// 닿을 것이 없으면 마찰의 정확한 걸음 (x += v h + ½ a h² · 서는 걸음은 v²/2a에서 멈춘다) · 있으면 마찰 빠르기 → 충격량 → 자리 → 바로잡음.
    /// 빠르기 1mm/s · 각속도 0.01rad/s 아래가 0.5초 이어지면 잔다 (빠르기 0 · 놓임).
    /// </summary>
    private void StepProp(Sim sim, ref Prop p, double dt, int host)
    {
        if (p.Kg <= 0) FillBody(ref p);
        var fm = PropForm(p);
        var sm = SamplesOf(fm);
        var mat = T.Materials[Math.Min(p.Mat, T.Materials.Count - 1)].Def;
        double m = p.Kg, I = p.Inertia, rho = fm.Rho, rem = dt;
        int steps = 0;
        // 맞힐 수 있는 이 (⑧이 알린 원판 — 이번 틱에 닿을 만한 것 · 많아야 4): 틱 처음에 이미 닿아 있던 이는 새로 맞힌 것이 아니다
        Span<int> near = stackalloc int[4];
        Span<bool> touching = stackalloc bool[4];
        int nNear = 0;
        if (_nActors > 0)
        {
            double far = (Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy) + Math.Abs(p.W) * sm.reach) * dt + sm.reach + ActorR;
            for (int a = 0; a < _nActors && nNear < near.Length; a++)
            {
                int ac = _actors[a].Cell;
                if (ac < 0 || ac >= L.N) continue;
                double ax = L.X(ac) + 0.5 - p.X, ay = L.Y(ac) + 0.5 - p.Y;
                if (ax * ax + ay * ay > far * far) continue;
                near[nNear] = a; touching[nNear] = TouchesActor(p, sm, _actors[a]); nNear++;
            }
        }
        int jp = 0;          // 몸 걸음 안의 덜컹 (때 차례) — 그 때가 지나면 바닥에 대한 빠르기를 고친다
        double jw = 0;       // 지난 덜컹이 바꾼 몸의 각속도
        var tr = Track;      // 틱 안 기록 (보기용 — 켰을 때만)
        double lastKey = 0;
        tr?.Add(MotionTrack.WhoProp, p.Id, 0, p.X, p.Y, p.Th, p.Vx, p.Vy, p.W);
        while (rem > 1e-12)
        {
            double tau = dt - rem;
            sim.Facts.SubNow = (byte)Math.Clamp((int)Math.Round(tau / dt * 255), 0, 255);   // 이 잔걸음에 난 사실 · 소리의 때
            double preX = p.X, preY = p.Y, preTh = p.Th, preVx = p.Vx, preVy = p.Vy, preW = p.W;   // 잔걸음 처음 (틱 안 기록 — 튀기 앞)
            for (; jp < _jolts.Count && _jolts[jp].T <= tau + 1e-12; jp++)
            {
                var j = _jolts[jp];
                if (j.B != host) continue;
                var o = Bodies[host];
                double jdx = p.X - o.Lx, jdy = p.Y - o.Ly;
                p.Vx -= j.Dvx - j.Dw * jdy; p.Vy -= j.Dvy + j.Dw * jdx; p.W -= j.Dw; jw += j.Dw; p.Still = 0;
                if (j.Cause != 0) p.Cause = j.Cause;
            }
            double nextJolt = double.PositiveInfinity;
            for (int k = jp; k < _jolts.Count; k++) if (_jolts[k].B == host) { nextJolt = _jolts[k].T; break; }
            int c = CellOf(p);
            // 무게중심이 도면 밖 · 바깥에 들었다 → 우주로 · 구멍 벽 칸에 들었다 (큰 구멍의 틈) → 빨아들이면 막거나 우주로 (아니면 틈을 지나 그대로 간다)
            if (c < 0 || L.Kind[c] == CellKind.Outside || F.IsOut(c)) { HoleHit(sim, ref p, -1); TrackEnd(tr, p, tau, ref lastKey); break; }
            if (L.Kind[c] == CellKind.Wall && F.Hole[c] > 0.01f && HoleHit(sim, ref p, c)) { TrackEnd(tr, p, tau, ref lastKey); break; }
            double gp = Gravity * F.Grav[c];   // 누르는 힘 N = m g' (무중력이면 정말 뜬다)
            double muK = mat.Mu * (F.Stored(T.sWater, c) > WetMol ? WetShare : 1);   // 칸 값으로 늘 다시 읽는다 (젖으면 × 0.33)
            double v = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy), w = Math.Abs(p.W), speed = v + w * sm.reach;
            // 바람 · 구멍 입 · 몸의 틀 (−a · 원심 · 오일러 — 잔걸음 처음 때 · 코리올리는 빠르기를 돌린다) — 놓인 채 정지 타원 안이면 그대로
            double wfx = 0, wfy = 0, wtq = 0, wb = 0, ffx = 0, ffy = 0, ftq = 0;
            bool windy = _windy && WindForce(p, fm, tau, out wfx, out wfy, out wtq);
            bool framed = _framed && FrameForce(p, host, tau, jw, m, I, out ffx, out ffy, out ftq, out wb);
            double fx = wfx + ffx, fy = wfy + ffy, tq = wtq + ftq;
            bool blown = windy || framed;
            if (p.Cause == 0 && (windy || framed)) p.Cause = windy ? WindCauseAt(c) : host >= 0 ? BodyCause(c) : ShipACause;   // 작은 덜컹에 깬 것도 미는 힘의 원인을 잇는다
            if (blown && p.Vx == 0 && p.Vy == 0 && p.W == 0 && Holds(fx, fy, tq, StaticShare * muK, m * gp, rho)) { blown = windy = false; fx = fy = tq = wfx = wfy = wtq = 0; }
            double ax = fx / m, ay = fy / m, al = tq / I, acc = blown ? Math.Sqrt(ax * ax + ay * ay) + Math.Abs(al) * sm.reach : 0;
            double room = ClearAt(c) - sm.reach, gap = room, h = rem;
            for (int k = 0; k < nNear; k++)   // 원판도 건너뛰지 않게 (닿을 만큼만 간다)
            {
                int ac = _actors[near[k]].Cell;
                double dx = L.X(ac) + 0.5 - p.X, dy = L.Y(ac) + 0.5 - p.Y;
                gap = Math.Min(gap, Math.Sqrt(dx * dx + dy * dy) - sm.reach - _actors[near[k]].R);
            }
            double ell = Math.Max(2 * DotForm.Dot, gap);
            if (speed > 0) h = Math.Min(h, ell / speed);
            if (w > 0) h = Math.Min(h, MaxPropTurn / w);
            if (acc > 0) h = Math.Min(h, 2 * ell / (speed + Math.Sqrt(speed * speed + 2 * acc * ell)));   // 힘을 받아 빨라져도 ℓ 넘게 가지 않는다 (아주 작은 힘에도 0이 되지 않는 꼴)
            if (blown && al != 0) h = Math.Min(h, 2 * MaxPropTurn / (w + Math.Sqrt(w * w + 2 * Math.Abs(al) * MaxPropTurn)));
            if ((speed > 0 || blown) && gp > 0 && muK > 0 || _windy || framed) h = Math.Min(h, MaxPropH);   // 바람 · 틀의 힘은 틱 안에서 준다
            if (nextJolt - tau < h) h = Math.Max(1e-9, nextJolt - tau);   // 덜컹 때에 맞춰 끊는다
            if (++steps >= MaxPropSteps) h = Math.Min(rem, Math.Max(1e-9, nextJolt - tau));
            double px0 = m * p.Vx, py0 = m * p.Vy, l0 = m * (p.X * p.Vy - p.Y * p.Vx) + I * p.W;
            double lw = blown ? (p.X * fy - p.Y * fx + tq) * h : 0;   // 바람이 준 각운동량 (원점 둘레)
            double speedEnd = speed + acc * h, stopAt = -1;
            int nc = 0;
            if (speedEnd > 0 && room < speedEnd * h + 2 * DotForm.Dot)
            {
                var pk = p;   // 받은 힘만큼 빨라진 길로 닿을 곳을 찾는다
                if (blown) { pk.Vx += ax * h; pk.Vy += ay * h; pk.W += al * h; }
                if (wb != 0) Coriolis(ref pk, -2 * wb * h);
                nc = FindContacts(pk, sm, h, mat.Mu, mat.Rest);
                if (nc > 0) p = pk;
            }
            if (nc == 0)
            {
                // 힘 반 → 마찰의 정확한 걸음 → 힘 반 (마찰로 선 채 정지 타원 안이면 그대로)
                if (blown) { p.Vx += 0.5 * ax * h; p.Vy += 0.5 * ay * h; p.W += 0.5 * al * h; }
                if (wb != 0) Coriolis(ref p, -wb * h);
                Friction(ref p, h, gp * muK, m, I, rho, true, out stopAt);
                if (wb != 0) Coriolis(ref p, -wb * h);
                if (blown && !(p.Vx == 0 && p.Vy == 0 && p.W == 0 && Holds(fx, fy, tq, StaticShare * muK, m * gp, rho))) { p.Vx += 0.5 * ax * h; p.Vy += 0.5 * ay * h; p.W += 0.5 * al * h; }
            }
            else
            {
                Friction(ref p, h, gp * muK, m, I, rho, false);
                // 마찰이 바꾼 빠르기로 닿음 목표를 다시 (다가오는 빠르기 · 반발은 닿기 직전 값)
                bool hit = false;
                for (int k = 0; k < nc; k++)
                {
                    ref var ct = ref _propHits[k];
                    double vn0 = (p.Vx - p.W * ct.Ry) * ct.Nx + (p.Vy + p.W * ct.Rx) * ct.Ny;
                    ct.Bounce = -vn0 > BounceV && (ct.Sep <= 0 || -vn0 * h > ct.Sep);
                    ct.Target = ct.Bounce ? -ct.E * vn0 : -Math.Max(ct.Sep, 0) / h;
                    hit |= ct.Bounce;
                }
                SolveContacts(ref p, nc, steps == 1 || hit ? 16 : 4);   // 틱 첫 잔걸음 · 부딪는 잔걸음은 16번 (따뜻한 시작이 없다)
                for (int k = 0; k < nc; k++) if (_propHits[k].Bounce && _propHits[k].Jn > 0) { Bounces++; break; }
                p.X += p.Vx * h; p.Y += p.Vy * h; p.Th += p.W * h;
            }
            // 장부: 바닥 · 벽 · 가구가 받은 몫 — 몸이 없는 배에 준 몫 (바람 · 점화의 틀 힘이 준 몫은 뺀다) · 몸이 있으면 틱 끝에 그 몸이 되받는다 (바람 몫은 기체가 잃는다)
            if (host < 0) { ToHostPx -= m * p.Vx - px0 - fx * h; ToHostPy -= m * p.Vy - py0 - fy * h; ToHostL -= m * (p.X * p.Vy - p.Y * p.Vx) + I * p.W - l0 - lw; }
            else if (windy) WindToWorld(p, host, tau, wfx, wfy, wtq, h);
            if (nc > 0) FixOverlap(ref p, nc);
            for (int k = 0; k < nNear; k++)
            {
                bool now = TouchesActor(p, sm, _actors[near[k]]);
                if (now && !touching[k]) ActorHit(sim, p, _actors[near[k]]);
                touching[k] = now;
            }
            rem -= h;
            if (tr != null)
            {
                double t1 = dt - rem;
                bool bounced = false;
                for (int k = 0; k < nc; k++) if (_propHits[k].Bounce && _propHits[k].Jn > 0) { bounced = true; break; }
                if (bounced)
                {
                    if (tau > lastKey + 1e-9) tr.Add(MotionTrack.WhoProp, p.Id, tau, preX, preY, preTh, preVx, preVy, preW);   // 튀기 앞 (그 잔걸음은 튄 빠르기로 곧게 갔다)
                    tr.Add(MotionTrack.WhoProp, p.Id, t1, p.X, p.Y, p.Th, p.Vx, p.Vy, p.W, MotionTrack.EvHit); lastKey = t1;
                }
                else
                {
                    if (stopAt >= 0) { tr.Add(MotionTrack.WhoProp, p.Id, tau + stopAt, p.X, p.Y, p.Th, 0, 0, p.W, MotionTrack.EvStop); lastKey = tau + stopAt; }
                    if (Math.Floor(t1 * 16 / dt + 1e-9) > Math.Floor(tau * 16 / dt + 1e-9) && t1 > lastKey + 1e-9) { tr.Add(MotionTrack.WhoProp, p.Id, t1, p.X, p.Y, p.Th, p.Vx, p.Vy, p.W); lastKey = t1; }
                }
            }
            // 구멍 벽에 닿았다 (밀어 닿았거나 겹침) → 빨아들이는 구멍이면 막거나 우주로 (아니면 보통 벽 — 이미 튀었다)
            bool gone = false;
            for (int k = 0; k < nc && !gone; k++)
            {
                ref var ct = ref _propHits[k];
                if (ct.Cell >= 0 && L.Kind[ct.Cell] == CellKind.Wall && F.Hole[ct.Cell] > 0.01f && (ct.Jn > 0 || ct.Sep <= Slop)) gone = HoleHit(sim, ref p, ct.Cell);
            }
            if (gone) { TrackEnd(tr, p, dt - rem, ref lastKey); break; }
            bool still = gp > 0 && p.Vx * p.Vx + p.Vy * p.Vy < SleepV * SleepV && Math.Abs(p.W) < SleepW && p.Z == 0 && p.Tilt == 0 && double.IsPositiveInfinity(nextJolt);
            p.Still = still ? p.Still + (float)h : 0;
            if (p.Still >= SleepSec) { p.Vx = p.Vy = p.W = 0; p.State = 0; p.Asleep = 1; p.Still = 0; p.Since = _now; break; }
        }
        TrackEnd(tr, p, dt - rem, ref lastKey);
        PropSteps += steps;
    }

    /// <summary>틱 안 기록의 끝 열쇠틀 (그 때에 아직 없으면 — 사라짐 · 막음은 그 사건으로).</summary>
    private static void TrackEnd(MotionTrack? tr, in Prop p, double t, ref double lastKey)
    {
        if (tr == null || double.IsPositiveInfinity(lastKey)) return;
        byte ev = p.State == 5 ? MotionTrack.EvGone : p.State == 6 ? MotionTrack.EvPlug : MotionTrack.EvNone;
        if (t > lastKey + 1e-9 || ev != MotionTrack.EvNone) tr.Add(MotionTrack.WhoProp, p.Id, Math.Max(t, lastKey), p.X, p.Y, p.Th, p.Vx, p.Vy, p.W, ev);
        lastKey = double.PositiveInfinity;   // 이 틱은 끝 (한 번만)
    }

    /// <summary>물체 몸이 그 원판에 닿았나 (가운데 · 껍질 꼭짓점 · 테두리 표본 가운데 하나가 원판 안).</summary>
    private bool TouchesActor(in Prop p, (double[] x, double[] y, double reach) sm, in PropActor a)
    {
        double ax = L.X(a.Cell) + 0.5, ay = L.Y(a.Cell) + 0.5, r2 = a.R * a.R;
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
    /// 물체가 원판에 새로 닿았다 → 맞음 장부 (꼬리표 · 그 칸 · ½mv² · 질량 · 빠르기 · 원인 — 0.5m/s 넘게 부딪으면 '날아든 …에 맞았다' 마디를 민 원인에 잇는다).
    /// 원판은 아직 물체를 밀어내지 않는다 (원판 충격량은 PH6b) — 맞음만 적는다.
    /// </summary>
    private void ActorHit(Sim sim, in Prop p, in PropActor a)
    {
        double v2 = p.Vx * p.Vx + p.Vy * p.Vy, speed = Math.Sqrt(v2);
        int cause = speed > BounceV ? sim.Causes.Add(CauseKind.Impact, $"날아든 {PropName(p)}에 맞았다", sim.Tick, a.Cell, p.Cause) : p.Cause;
        PropHits.Add(new PropHit { Tag = a.Tag, Cell = a.Cell, E = (float)(0.5 * p.Kg * v2), Kg = p.Kg, Speed = (float)speed, Cause = cause, Tick = sim.Tick });
    }

    /// <summary>코리올리 (−2ω × v — 도는 몸의 틀): 바닥에 대한 빠르기를 phi만큼 돌린다 (크기는 그대로 — 에너지를 꾸며내지 않는다).</summary>
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

    /// <summary>바닥 마찰 — 미끄러짐이 그친 때 (잔걸음 처음에서 초 · 그치지 않았으면 −1)도 (틱 안 기록의 '섰다').</summary>
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
