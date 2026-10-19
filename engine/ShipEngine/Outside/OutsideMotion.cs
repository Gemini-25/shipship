namespace ShipEngine;

/// <summary>점화 종류.</summary>
public enum BurnKind : byte { None, Depart, Correct, Arrive, Capture, Dodge, Match, Brake }

/// <summary>항로에 정해진 점화 하나: 할 때 · 늦어도 될 때 · Δv(m/s). 주컴퓨터(⑩)가 방송하고 · 미루고 · 한다.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct PlannedBurn
{
    public BurnKind Kind;
    public long Due, Deadline;
    public double Dv;
    public int Cause;
    public byte Announced;   // 주컴퓨터가 방송했다
    public int Delays;       // 미룬 번
}

/// <summary>
/// ⑤ 기동 — 배가 실제로 가속한다 (docs/ENGINE.md ⑤ 바퀴 2).
/// 질량 = 칸마다 마른 질량 + 추진제 · 엔진(EngineCore)마다 추력 · 로켓 방정식으로 추진제가 준다.
/// 주엔진은 도면에서 정해진 한쪽으로만 민다 · 자세 분사기는 어느 쪽으로나 약하게.
/// 점화는 위층(주컴퓨터)의 요청 — 바깥은 하라는 대로 가속하고, 배를 가운데 둔 좌표에서 바깥 물체의 상대 속도를 바꾸고, 아래 층(③ 장 · ④ 구조)에 가속도를 넣는다.
/// </summary>
public sealed partial class Outside
{
    public const double G0 = 9.80665;
    public const double SunMu = 1.32712440018e20, Au = 1.495978707e11;   // 노란 별(태양 질량) GM · 1AU (m)

    public double DryKg, PropellantKg, PropellantStart;
    public int Engines;                      // 도면의 엔진 수
    public double AxisX = 1, AxisY;          // 주엔진이 배를 미는 쪽 (단위 벡터)
    // 지금 점화
    public BurnKind Burning;
    public double BurnDirX, BurnDirY, BurnAccel, BurnDvLeft, BurnRampSec;
    public long BurnStart, BurnUntil;
    public int BurnCause, BurnEngines;
    public byte BurnRcs;
    public double Ax, Ay;                    // 지금 배 안에서 느끼는 가속도 (m/s² · 도면 좌표 — 엔진 쪽)
    public double Kx, Ky;                    // 지금 바깥에 대한 가속도 (배를 돌려 미는 점화는 Ax와 쪽이 다르다)
    public double DvUsed;
    public long BurnCount;
    // 끝난 점화 (주컴퓨터가 읽는다)
    public BurnKind LastBurn;
    public double LastBurnDv, LastBurnWanted;
    public long LastBurnEnd;
    // 항로 계획
    public List<PlannedBurn> Plan = new();
    public double PendingTo = double.NaN;    // 출발 점화가 끝나면 갈 거리 (AU)
    public string PendingName = "";
    public string VoyageName = "";
    public double ArriveExtraDv;             // 놓친 고침 때문에 도착에 더 드는 Δv
    public byte VoyagePlanned, ArrivalDone, Missed;
    public long MissedBurns;

    private ShipMotionDef Mo => T.Space.Ship;
    public double MassKg => DryKg + PropellantKg;
    public double EngineN => Mo.EngineKN * 1000;
    /// <summary>엔진 n개로 낼 수 있는 가속 (m/s²).</summary>
    public double MaxAccel(int engines) => engines * EngineN / Math.Max(1, MassKg);
    public double RcsAccel => Mo.RcsG * G0;
    /// <summary>남은 추진제로 낼 수 있는 Δv (로켓 방정식).</summary>
    public double DvLeft => Mo.Isp * G0 * Math.Log(MassKg / Math.Max(1, DryKg));

    private void InitMotion()
    {
        int cells = 0; double sx = 0, sy = 0;
        for (int i = 0; i < L.N; i++)
            if (L.Kind[i] != CellKind.Outside) { cells++; sx += L.X(i); sy += L.Y(i); }
        DryKg = Math.Max(1, cells) * Mo.TonsPerCell * 1000;
        PropellantKg = PropellantStart = DryKg * Mo.PropellantRatio;
        double ex = 0, ey = 0; int en = 0;
        foreach (var fu in L.Furniture)
            if (fu.Type == "EngineCore" && fu.Cells.Length > 0)
            {
                Engines++;
                foreach (int c in fu.Cells) { ex += L.X(c); ey += L.Y(c); en++; }
            }
        double cx = cells > 0 ? sx / cells : L.W / 2.0, cy = cells > 0 ? sy / cells : L.H / 2.0;
        // 엔진 칸들의 무게중심 → 배 무게중심 쪽으로 민다 (가운데에 있으면 긴 쪽)
        double dx = en > 0 ? cx - ex / en : 0, dy = en > 0 ? cy - ey / en : 0;
        if (Math.Sqrt(dx * dx + dy * dy) < 0.15 * Math.Max(L.W, L.H) / 2) { AxisX = L.W >= L.H ? 1 : 0; AxisY = L.W >= L.H ? 0 : 1; }
        else if (Math.Abs(dx) >= Math.Abs(dy)) { AxisX = Math.Sign(dx); AxisY = 0; }
        else { AxisX = 0; AxisY = Math.Sign(dy); }
    }

    // ───────────────────────── 궤도 역학 (실제 식) ─────────────────────────

    /// <summary>호만 전이 Δv (m/s) — 활력 방정식: Δv₁ = √(μ/r₁)(√(2r₂/(r₁+r₂)) − 1) · Δv₂ = √(μ/r₂)(1 − √(2r₁/(r₁+r₂))).</summary>
    public static (double dv1, double dv2) HohmannDv(double r1Au, double r2Au)
    {
        double r1 = r1Au * Au, r2 = r2Au * Au;
        double dv1 = Math.Sqrt(SunMu / r1) * (Math.Sqrt(2 * r2 / (r1 + r2)) - 1);
        double dv2 = Math.Sqrt(SunMu / r2) * (1 - Math.Sqrt(2 * r1 / (r1 + r2)));
        return (Math.Abs(dv1), Math.Abs(dv2));
    }

    /// <summary>
    /// 면을 바꾸는 호만 전이 (⑫ 항로의 기울기): 출발에 황도면에서 inclDeg만큼 기울이고 도착에 되돌린다 — 두 점화에 묶어
    /// Δv = √(v₁² + v₂² − 2v₁v₂·cos Δi) (원 궤도 빠르기 ↔ 전이 궤도 근일점 · 원일점 빠르기). 0°면 <see cref="HohmannDv(double, double)"/>와 같다.
    /// </summary>
    public static (double dv1, double dv2) HohmannDv(double r1Au, double r2Au, double inclDeg)
    {
        var (h1, h2) = HohmannDv(r1Au, r2Au);
        if (inclDeg <= 0) return (h1, h2);
        double r1 = r1Au * Au, r2 = r2Au * Au, ci = Math.Cos(inclDeg * Math.PI / 180);
        double vc1 = Math.Sqrt(SunMu / r1), vc2 = Math.Sqrt(SunMu / r2);
        double vt1 = Math.Sqrt(SunMu * 2 * r2 / (r1 * (r1 + r2))), vt2 = Math.Sqrt(SunMu * 2 * r1 / (r2 * (r1 + r2)));   // 전이 궤도의 출발 · 도착 빠르기
        double Comb(double a, double b) => Math.Sqrt(Math.Max(0, a * a + b * b - 2 * a * b * ci));
        return (Comb(vc1, vt1), Comb(vt2, vc2));
    }

    /// <summary>
    /// 항해를 계획한다: 출발 점화(호만 Δv₁)를 minutes 뒤에 · 늦어도 6시간 안. 출발 점화가 끝나면 전이 궤도에 오르고 —
    /// 중간 고침 둘(몇 m/s) · 도착 점화(Δv₂)가 정해진다. 점화는 주컴퓨터가 한다.
    /// </summary>
    public void PlanVoyage(Sim sim, double toAu, string name, double minutes, int cause)
    {
        var (dv1, _) = HohmannDv(Distance, toAu, Rt?.Incline ?? 0);
        PendingTo = toAu; PendingName = name; VoyagePlanned = 1; ArrivalDone = 0; Missed = 0; ArriveExtraDv = 0;
        Plan.RemoveAll(p => p.Kind is BurnKind.Depart or BurnKind.Correct or BurnKind.Arrive or BurnKind.Capture);
        long due = sim.Tick + Time.Minutes(minutes);
        Plan.Add(new PlannedBurn { Kind = BurnKind.Depart, Due = due, Deadline = due + Time.Hours(6), Dv = dv1, Cause = cause });
    }

    // ───────────────────────── 점화 요청 (⑩ 주컴퓨터) ─────────────────────────

    /// <summary>정해진 점화를 고친다 (방송했다 · 미뤘다) · 더한다 (남은 몫) · 가져간다 (점화했다).</summary>
    public void SetPlan(int k, PlannedBurn p) { if (k >= 0 && k < Plan.Count) Plan[k] = p; }
    public void AddPlan(PlannedBurn p) => Plan.Add(p);
    public void TakePlan(int k) { if (k >= 0 && k < Plan.Count) Plan.RemoveAt(k); }

    /// <summary>
    /// 점화한다: 쪽(dirX, dirY) · 가속(m/s²) · Δv(m/s — 0이면 seconds만큼) · 천천히 올리는 초(0이면 곧장 다) · 자세 분사기인가 · 도는 엔진 수 · 원인.
    /// 주엔진은 미는 쪽으로만 (그 쪽 성분이 없으면 못 한다) · 가속은 낼 수 있는 만큼으로 줄인다. 하던 점화는 끝낸다.
    /// </summary>
    public bool Burn(Sim sim, BurnKind kind, double dirX, double dirY, double accel, double dv, double seconds, double rampSec, bool rcs, int engines, int cause)
    {
        if (Planet != null || PropellantKg <= 0) return false;
        double len = Math.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 1e-9) return false;
        dirX /= len; dirY /= len;
        if (!rcs)
        {
            // 주엔진은 미는 쪽으로만 — 다른 배에 맞추는 점화 · 멈추는 점화는 방송하는 3분 사이에 배를 돌려 그쪽으로 민다 (배 안에서는 늘 엔진 쪽으로 쏠린다)
            if (engines <= 0) return false;
            if (kind is not (BurnKind.Match or BurnKind.Brake))
            {
                if (dirX * AxisX + dirY * AxisY < 0.5) return false;
                dirX = AxisX; dirY = AxisY;
            }
            accel = Math.Min(accel, MaxAccel(engines));
        }
        else accel = Math.Min(accel, RcsAccel);
        if (accel <= 0) return false;
        if (Burning != BurnKind.None) EndBurn(sim);
        LastBurnDv = 0;
        Burning = kind; BurnDirX = dirX; BurnDirY = dirY; BurnAccel = accel; BurnRampSec = rampSec; BurnRcs = (byte)(rcs ? 1 : 0); BurnEngines = engines;
        BurnStart = sim.Tick; BurnCause = cause;
        BurnDvLeft = dv > 0 ? dv : double.PositiveInfinity;
        BurnUntil = seconds > 0 ? sim.Tick + 1 + Math.Max(1, (long)Math.Ceiling(seconds / Time.SecondsPerTick)) : long.MaxValue;
        LastBurnWanted = dv;
        BurnCount++;
        sim.Facts.Emit(sim.Tick, FactKind.Burn, -1, (float)(accel / G0), cause, other: (int)kind);
        return true;
    }

    /// <summary>점화를 멈춘다 (엔진이 멈췄다 · 주컴퓨터가 끊었다).</summary>
    public void EndBurn(Sim sim)
    {
        if (Burning == BurnKind.None) return;
        var kind = Burning;
        LastBurn = kind; LastBurnEnd = sim.Tick;
        Burning = BurnKind.None;   // 이번 틱의 가속(Ax · Kx)은 그대로 둔다 — 이번 틱에 이미 밀었다 (다음 틱 처음에 0)
        if (kind == BurnKind.Depart && !double.IsNaN(PendingTo) && LastBurnDv >= 0.95 * LastBurnWanted)
        {
            // 전이 궤도에 올랐다 — 고침 둘 · 도착이 정해진다
            double to = PendingTo; PendingTo = double.NaN; VoyageName = PendingName;
            var (_, dv2) = HohmannDv(Distance, to, Rt?.Incline ?? 0);
            StartVoyage(sim, to);
            int c = sim.Causes.Add(CauseKind.Route, $"{Ko.Euro(VoyageName)} 가는 전이 궤도에 올랐다 — {VoyageDays(VoyageFrom, to):0}일", sim.Tick, -1, BurnCause);
            foreach (double at in new[] { 0.15, 0.6 })
            {
                long due = VoyageStart + (long)(VoyageTicks * at);
                Plan.Add(new PlannedBurn { Kind = BurnKind.Correct, Due = due, Deadline = due + Time.Hours(6), Dv = _route.Range(0.5, 5.0), Cause = c });
            }
            long arrive = VoyageStart + VoyageTicks;
            Plan.Add(new PlannedBurn { Kind = BurnKind.Arrive, Due = arrive, Deadline = arrive + Time.Minutes(30), Dv = dv2, Cause = c });
        }
        if (kind is BurnKind.Arrive or BurnKind.Capture && LastBurnDv >= 0.95 * LastBurnWanted) { ArrivalDone = 1; Missed = 0; }
    }

    // ───────────────────────── 한 틱 ─────────────────────────

    private void MotionStep(Sim sim, double dt)
    {
        Ax = Ay = Kx = Ky = 0;
        if (Burning != BurnKind.None)
        {
            double el = Math.Max(0, sim.Tick - BurnStart - 1) * dt;
            double a = BurnAccel * (BurnRampSec > 0 ? Math.Min(1, (el + dt) / BurnRampSec) : 1);
            if (BurnRcs == 0) a = Math.Min(a, MaxAccel(BurnEngines));
            if (a * dt > BurnDvLeft) a = BurnDvLeft / dt;
            double isp = BurnRcs != 0 ? Mo.RcsIsp : Mo.Isp;
            double dm = MassKg * a * dt / (isp * G0);                  // 로켓 방정식 (한 틱): dm = m·a·dt / (Isp·g₀)
            if (dm > PropellantKg) { a *= PropellantKg / dm; dm = PropellantKg; }
            PropellantKg -= dm;
            Kx = BurnDirX * a; Ky = BurnDirY * a;                       // 바깥에 대한 가속
            Ax = (BurnRcs != 0 ? BurnDirX : AxisX) * a; Ay = (BurnRcs != 0 ? BurnDirY : AxisY) * a;   // 배 안에서 느끼는 가속
            DvUsed += a * dt; LastBurnDv += a * dt; BurnDvLeft -= a * dt;
            if (PropellantKg <= 1e-6)
            {
                PropellantKg = 0;
                sim.Facts.Emit(sim.Tick, FactKind.Burn, -1, 0, sim.Causes.Add(CauseKind.Route, "추진제가 다 떨어졌다", sim.Tick, -1, BurnCause), other: 0);
                EndBurn(sim);
            }
            else if (BurnDvLeft <= 1e-6 || sim.Tick + 1 >= BurnUntil) EndBurn(sim);
        }
        F.AccelX = Ax; F.AccelY = Ay;
        if (sim.Find<Structure>() is { } st) { st.ThrustG = Math.Sqrt(Ax * Ax + Ay * Ay) / G0; st.ShipAx = Ax; st.ShipAy = Ay; }
        if (Kx == 0 && Ky == 0) return;
        // 배를 가운데 둔 좌표: 배가 a로 가속하면 바깥 물체는 −a로 (붙어 있는 배는 함께 간다)
        for (int k = 0; k < Rocks.Count; k++) { var r = Rocks[k]; r.Vx -= Kx * dt; r.Vy -= Ky * dt; Rocks[k] = r; }
        for (int k = 0; k < Clouds.Count; k++) { var c = Clouds[k]; c.Vx -= Kx * dt; c.Vy -= Ky * dt; Clouds[k] = c; }
        for (int k = 0; k < Contacts.Count; k++) { var c = Contacts[k]; if (c.State == ContactState.Docked) continue; c.Vx -= Kx * dt; c.Vy -= Ky * dt; Contacts[k] = c; }
    }

    /// <summary>정해진 점화가 늦어도 될 때를 넘기면 값을 치른다: 출발 — 하루 뒤로 · 고침 — 도착 Δv가 세 배 늘고 · 도착 — 전이 궤도를 그대로 돌아 한 바퀴 뒤에 다시.</summary>
    private void PlanStep(Sim sim)
    {
        for (int k = Plan.Count - 1; k >= 0; k--)
        {
            var p = Plan[k];
            if (sim.Tick <= p.Deadline) continue;
            Plan.RemoveAt(k); MissedBurns++;
            switch (p.Kind)
            {
                case BurnKind.Depart:
                {
                    int c = sim.Causes.Add(CauseKind.Route, "출발 점화를 하지 못했다 — 하루 뒤로 미룬다", sim.Tick, -1, p.Cause);
                    Plan.Add(new PlannedBurn { Kind = BurnKind.Depart, Due = sim.Tick + Time.Days(1), Deadline = sim.Tick + Time.Days(1) + Time.Hours(6), Dv = p.Dv, Cause = c });
                    break;
                }
                case BurnKind.Correct:
                {
                    ArriveExtraDv += 3 * p.Dv;
                    for (int j = 0; j < Plan.Count; j++) if (Plan[j].Kind is BurnKind.Arrive or BurnKind.Capture) { var a = Plan[j]; a.Dv += 3 * p.Dv; Plan[j] = a; }
                    sim.Causes.Add(CauseKind.Route, $"항로 고침을 놓쳤다 — 도착에 {3 * p.Dv:0.#}m/s 더 든다", sim.Tick, -1, p.Cause);
                    break;
                }
                case BurnKind.Arrive or BurnKind.Capture:
                {
                    Missed = 1;
                    int c = sim.Causes.Add(CauseKind.Route, $"{VoyageName}에 붙잡히지 못하고 지나쳤다 — 한 바퀴 돌아 다시", sim.Tick, -1, p.Cause);
                    sim.Facts.Emit(sim.Tick, FactKind.Burn, -1, 0, c, other: -(int)p.Kind);
                    long again = VoyageStart + 3 * VoyageTicks;   // 전이 타원 한 바퀴(반 바퀴 둘) 뒤 다시 목표 거리
                    Plan.Add(new PlannedBurn { Kind = BurnKind.Capture, Due = again, Deadline = again + Time.Minutes(30), Dv = p.Dv * 1.5, Cause = c });
                    break;
                }
            }
        }
    }

    /// <summary>
    /// ⑫ 자연 사건 — 항로의 구간: 그 거리를 지나는 동안 구름 · 운석이 구간 빈도 × 항로 배율로 온다 (안에 있을 때만 난수를 쓴다) ·
    /// 항해 중 구간 밖은 깊은 우주의 바탕 빈도 × 항로 배율. 항로를 고르지 않은 배(시험 — 가만히 떠 있다)는 구간 그대로 · 바탕 없음 · 그 항로만의 구간 없음.
    /// </summary>
    private void ZoneStep(Sim sim)
    {
        if (Planet != null) return;
        var rt = Rt;
        bool inZone = false;
        foreach (var z in T.Space.Zones)
        {
            if (Distance < z.From || Distance > z.To) continue;
            if (z.Only && (rt == null || !rt.Extra.Contains(z.Name))) continue;
            inZone = true;
            Debris(sim, z.Name, z.Debris, z.CloudsPerDay * (rt?.Zones ?? 1), z.RocksPerDay * (rt?.Zones ?? 1));
        }
        if (!inZone && rt != null && VoyageTicks > 0)
            Debris(sim, "깊은 우주", T.Space.Deep.Debris, T.Space.Deep.CloudsPerDay * rt.Deep, T.Space.Deep.RocksPerDay * rt.Deep);
    }

    /// <summary>파편 구름 · 바위가 하루 평균 그만큼 저절로 (포아송 — 크기는 거듭제곱 분포 · 다가오는 거리는 10~60분 앞).</summary>
    private void Debris(Sim sim, string where, string kind, double cloudsPerDay, double rocksPerDay)
    {
        if (!T.Space.Debris.TryGetValue(kind, out var d)) return;
        if (cloudsPerDay > 0 && _route.Unit() < cloudsPerDay / Time.TicksPerDay)
        {
            double minutes = _route.Range(20, 60), radius = _route.Range(200_000, 600_000), ang = _route.Range(0, 2 * Math.PI);
            double density = kind == "dust" ? 2e-6 : kind == "kessler" ? 6e-8 : 2e-7;
            int c = sim.Causes.Add(CauseKind.Route, $"{where}의 {Ko.I(d.Name)} 항로로 들어온다", sim.Tick);
            AddCloud(kind, d.Speed * minutes * 60 + radius, radius, density, Math.Cos(ang), Math.Sin(ang), c);
        }
        if (rocksPerDay > 0 && _route.Unit() < rocksPerDay / Time.TicksPerDay)
        {
            double mass = 0.05 * Math.Pow(10, _route.Range(0.0, 1.6)), speed = _route.Range(3000, 9000), minutes = _route.Range(10, 40), ang = _route.Range(0, 2 * Math.PI);
            int c = sim.Causes.Add(CauseKind.Route, $"{where}의 {mass:0.##}kg 바위", sim.Tick);
            AddRock(mass, speed, Math.Cos(ang), Math.Sin(ang), _route.Range(0, 120), speed * minutes * 60, c);
        }
    }

    /// <summary>
    /// 고향(떠난 천체)까지 빛이 가는 분 — 배는 전이 궤도 위 (근점 이각 ν: r = a(1−e²)/(1+e·cosν)) · 고향은 제 원 궤도를 돈다 (주기 r^1.5년) ·
    /// 둘 다 출발 때 같은 쪽에서 — 코사인 법칙으로 거리 (AU) × 8.317분. 항해 전이면 가까이 (0).
    /// </summary>
    public double HomeLightMinutes(long tick)
    {
        if (VoyageTicks <= 0 && Distance == VoyageFrom) return 0;
        var (home, r, ang) = HomeGeometry(tick);
        double d = Math.Sqrt(Math.Max(0, r * r + home * home - 2 * r * home * Math.Cos(ang)));
        return d * 8.317;
    }

    /// <summary>고향 · 배의 자리: 고향 궤도 반지름 · 배까지 별에서 거리 (AU) · 별에서 본 둘 사이 각 (라디안).</summary>
    private (double home, double r, double ang) HomeGeometry(long tick)
    {
        double home = VoyageFrom > 0 ? VoyageFrom : 1, r = Distance;
        double a = (VoyageFrom + VoyageTo) / 2, e = Math.Abs(VoyageTo - VoyageFrom) / Math.Max(1e-9, VoyageTo + VoyageFrom);
        double nu = 0;
        if (e > 1e-9) nu = Math.Acos(Math.Clamp((a * (1 - e * e) / r - 1) / e, -1, 1));
        if (VoyageTo < VoyageFrom) nu = Math.PI - nu;   // 안으로 가면 원일점에서
        double years = (tick - VoyageStart) / (double)Time.TicksPerDay / DaysPerYear;
        double th = 2 * Math.PI * years / Math.Pow(home, 1.5);
        return (home, r, nu - th);
    }

    /// <summary>
    /// ⑤ 통신 지연 (나) 태양 합: 고향에서 본 별과 배 사이 각 (SEP · 도) — 2° 안이면 별의 코로나가 신호를 삼킨다 (실제: 화성 합 때 두 주 넘게 명령을 멈춘다).
    /// 항해 전이면 180 (고향 곁).
    /// </summary>
    public double HomeSepDeg(long tick)
    {
        if (VoyageTicks <= 0 && Distance == VoyageFrom) return 180;
        var (home, r, ang) = HomeGeometry(tick);
        // 고향 (home, 0) · 별 (0, 0) · 배 (r cos ang, r sin ang): 고향에서 별 쪽 (−home, 0) 과 배 쪽 사이 각
        double sx = r * Math.Cos(ang) - home, sy = r * Math.Sin(ang);
        double len = Math.Sqrt(sx * sx + sy * sy);
        if (len < 1e-9) return 180;
        double cos = Math.Clamp(-sx / len, -1, 1);
        return Math.Acos(cos) * 180 / Math.PI;
    }

    public const double ConjunctionDeg = 2;
    /// <summary>태양 합 — 고향과 통신이 끊겼다.</summary>
    public bool Conjunction(long tick) => Route > 0 && HomeSepDeg(tick) < ConjunctionDeg;

    /// <summary>지금 지나는 구간 (별계 지도 — 아는 것 · 항로만의 구간은 그 항로일 때) · 없으면 null.</summary>
    public ZoneDef? ZoneAt(double au)
    {
        var rt = Rt;
        foreach (var z in T.Space.Zones)
            if (au >= z.From && au <= z.To && (!z.Only || rt != null && rt.Extra.Contains(z.Name))) return z;
        return null;
    }

    /// <summary>앞으로 hours 시간 안에 드는 구간 (지금 거리에서 전이 궤도를 따라 — 지도로 아는 것).</summary>
    public ZoneDef? ZoneAhead(long now, double hours)
    {
        if (VoyageTicks <= 0) return null;
        double f = (now - VoyageStart + Time.Hours(hours)) / (double)VoyageTicks;
        var z = ZoneAt(TransferDistance(VoyageFrom, VoyageTo, f));
        return z != null && z != ZoneAt(Distance) ? z : null;
    }

    /// <summary>(ux, uy)로 날아오는 것이 off(m)만큼 비껴 지나면 처음 닿을 칸 (-1: 안 닿는다) — 주컴퓨터가 '맞을까'를 셈한다 (세계를 바꾸지 않는다).</summary>
    public int WouldHit(double ux, double uy, double off)
    {
        double cx = L.W / 2.0, cy = L.H / 2.0;
        double px = -uy, py = ux;
        double reach = Math.Sqrt(L.W * L.W + L.H * L.H);
        double sx = cx - ux * reach + px * off, sy = cy - uy * reach + py * off;
        int steps = (int)(reach * 2 * 2);
        for (int s = 0; s < steps; s++)
        {
            int ix = (int)Math.Floor(sx + ux * s * 0.5), iy = (int)Math.Floor(sy + uy * s * 0.5);
            if (!L.In(ix, iy)) continue;
            int c = L.Index(ix, iy);
            if (L.Kind[c] != CellKind.Outside) return c;
        }
        return -1;
    }

    private void SaveMotion(StateWriter w)
    {
        w.F64(DryKg); w.F64(PropellantKg); w.F64(PropellantStart); w.I32(Engines); w.F64(AxisX); w.F64(AxisY);
        w.Byte((byte)Burning); w.F64(BurnDirX); w.F64(BurnDirY); w.F64(BurnAccel); w.F64(BurnDvLeft); w.F64(BurnRampSec);
        w.I64(BurnStart); w.I64(BurnUntil); w.I32(BurnCause); w.I32(BurnEngines); w.Byte(BurnRcs); w.F64(Ax); w.F64(Ay); w.F64(Kx); w.F64(Ky); w.F64(DvUsed); w.I64(BurnCount);
        w.Byte((byte)LastBurn); w.F64(LastBurnDv); w.F64(LastBurnWanted); w.I64(LastBurnEnd);
        w.List(Plan); w.F64(PendingTo); w.Str(PendingName); w.Str(VoyageName); w.F64(ArriveExtraDv); w.Byte(VoyagePlanned); w.Byte(ArrivalDone); w.Byte(Missed); w.I64(MissedBurns);
    }

    private void LoadMotion(StateReader r)
    {
        DryKg = r.F64(); PropellantKg = r.F64(); PropellantStart = r.F64(); Engines = r.I32(); AxisX = r.F64(); AxisY = r.F64();
        Burning = (BurnKind)r.Byte(); BurnDirX = r.F64(); BurnDirY = r.F64(); BurnAccel = r.F64(); BurnDvLeft = r.F64(); BurnRampSec = r.F64();
        BurnStart = r.I64(); BurnUntil = r.I64(); BurnCause = r.I32(); BurnEngines = r.I32(); BurnRcs = r.Byte(); Ax = r.F64(); Ay = r.F64(); Kx = r.F64(); Ky = r.F64(); DvUsed = r.F64(); BurnCount = r.I64();
        LastBurn = (BurnKind)r.Byte(); LastBurnDv = r.F64(); LastBurnWanted = r.F64(); LastBurnEnd = r.I64();
        Plan = r.List<PlannedBurn>(); PendingTo = r.F64(); PendingName = r.Str()!; VoyageName = r.Str()!; ArriveExtraDv = r.F64(); VoyagePlanned = r.Byte(); ArrivalDone = r.Byte(); Missed = r.Byte(); MissedBurns = r.I64();
    }
}
