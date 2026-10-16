namespace ShipEngine;

/// <summary>파편 구름: 배에 대한 상대 위치 · 속도 (m · m/s) · 반지름 · 입자 밀도(개/m³).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct Cloud
{
    public double X, Y, Vx, Vy, Radius, Density;
    public int Kind;     // 파편 표 순서
    public int Cause;
    public long Hits;
    public byte Warned;  // 10분 앞 '다가온다'를 알렸다
}

/// <summary>떠다니는 물체 하나 (운석 · 큰 파편 · 떨어져 나간 조각).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct Rock
{
    public double X, Y, Vx, Vy, Mass;
    public int Cause;
    public byte Warned;
}

/// <summary>
/// ⑤ 바깥 세계 — 별 · 행성 · 파편 · 운석 (docs/ENGINE.md §4 ⑤).
/// <para>
/// 배를 가운데 둔 평면 좌표(칸 1개 = 1m)에서 물체가 실제로 움직이고, 배의 외곽과 만나면 그 칸 · 방향 · 운동 에너지가 ④ 구조로 간다.
/// 파편 구름 안에서는 칸이 쓸고 지나는 부피만큼 입자를 만나고(포아송), 입자 질량은 거듭제곱 분포(작은 것이 대부분).
/// </para>
/// <para>
/// 별: 햇빛 세기 · 방향(③ 열의 햇빛 쪽 외판) · 폭발(조짐 X선 → 양성자 폭풍 몇 시간). 바깥 방사선은 벽 · 설비의 면밀도로 줄어
/// 칸마다 선량률이 된다 (⑦ 생명이 읽는다 — 대피실이 '저절로' 의미를 가진다).
/// 행성에 내리면 ③ 장의 바깥 경계가 그 행성 대기(압력 · 성분 · 온도 · 낮밤 · 바람)가 된다.
/// </para>
/// 이야기꾼은 원인만 심는다: <see cref="AddCloud"/> · <see cref="AddRock"/> · <see cref="ScheduleFlare"/>. 어디에 맞을지는 엔진이 정한다.
/// </summary>
public sealed class Outside : IEngine
{
    public string Name => "바깥";
    public Phase Phase => Phase.Outside;
    public int Every => 1;
    public int Offset => 0;

    private readonly Field F;
    private readonly Layout L;
    private readonly Tables T;
    private readonly string[] _debrisKeys;

    // ── 상태 ──
    public string Star = "yellow";
    public double Distance = 1;          // 별까지 (궤도 단위)
    public double SunAngle;              // 햇빛이 오는 방향 (라디안 · 도면 좌표)
    public double SunDrift = 2 * Math.PI / Time.TicksPerDay; // 틱마다 (하루에 한 바퀴 — 배가 천천히 돈다)
    public string? Planet;               // 내려앉은 행성 (null: 우주)
    public long PlanetSince;
    public bool Storm;
    public long StormUntil;
    public long NextFlare = -1, FlareWarnAt = -1, FlareStart = -1, FlareEnd = -1;
    public int FlareCause;
    public double DoseOut;               // 바깥 방사선 (mGy/시)
    public List<Cloud> Clouds = new();
    public List<Rock> Rocks = new();
    public long DebrisHits, RockHits, NearMisses, Flares;
    private RngStream _rng;

    /// <summary>칸마다 방사선 선량률 (mGy/시) — 바깥 × 가림.</summary>
    public float[] Dose;
    private float[] _shield;
    private double _shieldAngle = double.NaN;

    public Outside(Field f, ulong seed)
    {
        F = f; L = f.L; T = f.T;
        _debrisKeys = T.Space.Debris.Keys.ToArray();
        _rng = Rng.Stream(seed, "바깥");
        Dose = new float[L.N]; _shield = new float[L.N];
    }

    public StarDef StarDef => T.Space.Stars[Star];

    // ───────────────────────── 이야기꾼 · 시험이 심는 원인 ─────────────────────────

    /// <summary>파편 구름을 놓는다: 방향 (dirX, dirY)에서 다가온다 · 앞 거리(m) · 반지름 · 밀도(개/m³).</summary>
    public void AddCloud(string kind, double ahead, double radius, double density, double dirX, double dirY, int cause)
    {
        var d = T.Space.Debris[kind];
        double len = Math.Sqrt(dirX * dirX + dirY * dirY);
        dirX /= len; dirY /= len;
        Clouds.Add(new Cloud { X = -dirX * ahead, Y = -dirY * ahead, Vx = dirX * d.Speed, Vy = dirY * d.Speed, Radius = radius, Density = density, Kind = Array.IndexOf(_debrisKeys, kind), Cause = cause });
    }

    /// <summary>운석 · 큰 파편 하나: 질량(kg) · 속도(m/s) · 오는 방향 · 배 가운데에서 비낀 거리(m) · 지금 거리(m).</summary>
    public void AddRock(double mass, double speed, double dirX, double dirY, double offset, double distance, int cause)
    {
        double len = Math.Sqrt(dirX * dirX + dirY * dirY);
        dirX /= len; dirY /= len;
        double px = -dirY, py = dirX; // 비낀 방향
        Rocks.Add(new Rock { X = -dirX * distance + px * offset, Y = -dirY * distance + py * offset, Vx = dirX * speed, Vy = dirY * speed, Mass = mass, Cause = cause });
    }

    /// <summary>별 폭발을 앞당긴다 (지금부터 minutes 뒤 조짐).</summary>
    public void ScheduleFlare(double minutes, int cause) { FlareWarnAt = -1; FlareStart = -1; ScheduleAt(Time.Minutes(minutes + StarDef.WarnMin), cause); }

    private long _nowTick;
    private void ScheduleAt(long ticksFromNow, int cause) { NextFlare = _nowTick + ticksFromNow; FlareCause = cause; }

    public void Land(string planet, long tick) { Planet = planet; PlanetSince = tick; }
    public void Launch() { Planet = null; }

    // ───────────────────────── 한 틱 ─────────────────────────

    public void Step(Sim sim, double dt)
    {
        _nowTick = sim.Tick;
        SunAngle += SunDrift;
        Boundary(sim);
        FlareStep(sim, dt);
        CloudsStep(sim, dt);
        RocksStep(sim, dt);
        if (sim.Tick % 25 == 0 || double.IsNaN(_shieldAngle)) Radiation();
    }

    /// <summary>③ 장의 바깥 경계: 우주(진공 · 햇빛) 또는 행성 대기.</summary>
    private void Boundary(Sim sim)
    {
        var o = F.Outside;
        var star = StarDef;
        double sun = star.LightAt1 / (Distance * Distance);
        if (Planet == null)
        {
            o.KPa = 0; o.Temp = 2.7; o.Frac = Array.Empty<double>();
            o.Sun = sun; o.SunX = Math.Cos(SunAngle); o.SunY = Math.Sin(SunAngle);
            return;
        }
        var p = T.Space.Planets[Planet];
        double hours = (sim.Tick - PlanetSince) / (double)Time.TicksPerHour;
        double phase = (hours % p.DayHours) / p.DayHours;          // 0~1 (0.25 정오)
        double daylight = Math.Max(0, Math.Sin(2 * Math.PI * phase));
        if (Storm && sim.Tick >= StormUntil) Storm = false;
        if (!Storm && p.StormsPerDay > 0 && _rng.Unit() < p.StormsPerDay / Time.TicksPerDay)
        {
            Storm = true; StormUntil = sim.Tick + Time.Hours(6 + 18 * _rng.Unit());
            sim.Facts.Emit(sim.Tick, FactKind.Flare, -1, 0, sim.Causes.Add(CauseKind.Weather, $"{p.Name}에 모래 폭풍", sim.Tick));
        }
        double atmo = p.KPa / 101.3;
        o.KPa = p.KPa;
        o.Temp = p.NightK + (p.DayK - p.NightK) * daylight;
        o.Frac = new double[F.G];
        foreach (var (g, frac) in p.Air) o.Frac[T.S(g).Gas] = frac;
        o.Sun = sun * daylight * Math.Exp(-0.1 * atmo) * (Storm ? 0.2 : 1);
        o.SunX = Math.Cos(SunAngle); o.SunY = Math.Sin(SunAngle);
    }

    private void FlareStep(Sim sim, double dt)
    {
        var star = StarDef;
        if (NextFlare < 0)
        {
            // 다음 폭발까지 지수 분포
            double days = -Math.Log(Math.Max(1e-12, _rng.Unit())) / Math.Max(1e-6, star.FlaresPerDay);
            NextFlare = sim.Tick + Time.Days(days);
        }
        long warnTicks = Time.Minutes(star.WarnMin);
        if (FlareWarnAt < 0 && sim.Tick >= NextFlare - warnTicks && sim.Tick < NextFlare)
        {
            FlareWarnAt = sim.Tick;
            if (FlareCause == 0) FlareCause = sim.Causes.Add(CauseKind.Flare, $"{star.Name}의 X선이 갑자기 밝아졌다 (폭발 조짐)", sim.Tick);
            sim.Facts.Emit(sim.Tick, FactKind.Flare, -1, 0, FlareCause, other: 0);
        }
        if (FlareStart < 0 && sim.Tick >= NextFlare)
        {
            Flares++;
            FlareStart = sim.Tick;
            FlareEnd = sim.Tick + Time.Hours(star.FlareHours * (0.5 + _rng.Unit()));
            int c = sim.Causes.Add(CauseKind.Flare, $"{star.Name} 폭발 — 양성자 폭풍", sim.Tick, -1, FlareCause);
            sim.Facts.Emit(sim.Tick, FactKind.Flare, -1, (float)star.FlareDose, c, other: 1);
        }
        double dose = star.BaseDose;
        if (FlareStart >= 0)
        {
            if (sim.Tick < FlareEnd)
            {
                double x = (sim.Tick - FlareStart) / (double)Math.Max(1, FlareEnd - FlareStart);
                dose += star.FlareDose * Math.Min(1, x * 6) * Math.Exp(-2 * x);
            }
            else { FlareStart = -1; FlareWarnAt = -1; NextFlare = -1; FlareCause = 0; }
        }
        if (Planet != null)
        {
            var p = T.Space.Planets[Planet];
            double column = p.KPa * 1000 / (9.81 * Math.Max(0.05, p.G)); // 대기 기둥 kg/m²
            dose *= Math.Exp(-column / 1000);
        }
        DoseOut = dose;
    }

    private void CloudsStep(Sim sim, double dt)
    {
        double halfW = L.W / 2.0, halfH = L.H / 2.0;
        for (int k = 0; k < Clouds.Count; k++)
        {
            var c = Clouds[k];
            c.X += c.Vx * dt; c.Y += c.Vy * dt;
            double dist = Math.Sqrt(c.X * c.X + c.Y * c.Y);
            double csp = Math.Sqrt(c.Vx * c.Vx + c.Vy * c.Vy);
            if (c.Warned == 0 && c.X * c.Vx + c.Y * c.Vy < 0 && (dist - c.Radius - Math.Max(halfW, halfH)) < csp * Time.SecondsPerTick * 250)
            {
                c.Warned = 1;
                sim.Facts.Emit(sim.Tick, FactKind.DebrisNear, -1, (float)c.Density, c.Cause, other: 2);
            }
            if (dist < c.Radius + Math.Max(halfW, halfH))
            {
                // 배가 구름 속: 칸이 쓸고 지나는 부피 = 단면(구름이 오는 쪽에서 본 배 폭 × 높이 2.5m) × 속도 × dt
                double sp = Math.Sqrt(c.Vx * c.Vx + c.Vy * c.Vy);
                double ux = c.Vx / sp, uy = c.Vy / sp;
                double width = Math.Abs(uy) * L.W + Math.Abs(ux) * L.H;
                double overlap = Math.Clamp((c.Radius + Math.Max(halfW, halfH) - dist) / Math.Max(1, 2 * Math.Max(halfW, halfH)), 0, 1);
                double lambda = c.Density * width * 2.5 * sp * dt * overlap;
                int hits = Poisson(lambda);
                var d = T.Space.Debris[_debrisKeys[c.Kind]];
                for (int h = 0; h < hits; h++)
                {
                    double m = PowerLaw(d.MinKg, d.MaxKg, d.Alpha);
                    double e = 0.5 * m * sp * sp;
                    double off = (_rng.Unit() - 0.5) * width;
                    if (HitShip(sim, ux, uy, off, e, c.Cause, $"{d.Name}의 파편 ({m * 1000:0.##}g)")) { c.Hits++; DebrisHits++; }
                }
            }
            Clouds[k] = c;
        }
        Clouds.RemoveAll(c => c.X * c.Vx + c.Y * c.Vy > 0 && Math.Sqrt(c.X * c.X + c.Y * c.Y) > c.Radius * 3 + 1e5); // 지나가 멀어진 것만
    }

    private void RocksStep(Sim sim, double dt)
    {
        double reach = Math.Sqrt(L.W * L.W + L.H * L.H) / 2;
        for (int k = 0; k < Rocks.Count; k++)
        {
            var r = Rocks[k];
            double x0 = r.X, y0 = r.Y;
            r.X += r.Vx * dt; r.Y += r.Vy * dt;
            double sp = Math.Sqrt(r.Vx * r.Vx + r.Vy * r.Vy);
            double ux = r.Vx / sp, uy = r.Vy / sp;
            // 가장 가까이 오는 거리 (배 가운데에서)
            double t = -(x0 * ux + y0 * uy);
            double cx = x0 + ux * t, cy = y0 + uy * t;
            double miss = Math.Sqrt(cx * cx + cy * cy);
            if (r.Warned == 0 && t > 0 && t < sp * Time.SecondsPerTick * 250 && miss < reach + 200)
            {
                r.Warned = 1;
                sim.Facts.Emit(sim.Tick, FactKind.DebrisNear, -1, (float)miss, r.Cause);
            }
            // 이번 틱에 배 곁을 지나가나 (가장 가까운 점을 지난다)
            bool crossed = t >= 0 && t <= sp * dt + reach;
            if (crossed && miss <= reach + 1)
            {
                double off = cx * -uy + cy * ux; // 비낀 거리 (진행 방향의 왼쪽 +)
                double e = 0.5 * r.Mass * sp * sp;
                if (HitShip(sim, ux, uy, -off, e, r.Cause, $"{r.Mass:0.#}kg 운석")) { RockHits++; r.Mass = 0; }
            }
            if (r.Mass > 0 && r.Warned < 2 && t >= 0 && t <= sp * dt && miss <= reach + 1000)
            {
                // 아슬아슬하게 비껴갔다
                NearMisses++; r.Warned = 2;
                sim.Facts.Emit(sim.Tick, FactKind.DebrisNear, -1, (float)miss, r.Cause, other: 1);
            }
            Rocks[k] = r;
        }
        Rocks.RemoveAll(r => r.Mass <= 0 || r.X * r.Vx + r.Y * r.Vy > 0 && r.X * r.X + r.Y * r.Y > 1e12); // 지나가 멀어진 것만 (멀리서 다가오는 것은 남긴다)
    }

    /// <summary>
    /// (ux, uy) 방향으로 날아오는 것이, 진행 방향에 수직으로 off(m)만큼 비껴 배를 지난다 — 처음 닿는 칸을 찾아 ④ 구조에 충돌로.
    /// </summary>
    private bool HitShip(Sim sim, double ux, double uy, double off, double energy, int parentCause, string what)
    {
        double cx = L.W / 2.0, cy = L.H / 2.0;
        double px = -uy, py = ux;
        double reach = Math.Sqrt(L.W * L.W + L.H * L.H);
        double sx = cx - ux * reach + px * off, sy = cy - uy * reach + py * off;
        int steps = (int)(reach * 2 * 2);
        for (int s = 0; s < steps; s++)
        {
            double x = sx + ux * s * 0.5, y = sy + uy * s * 0.5;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            if (!L.In(ix, iy)) continue;
            int c = L.Index(ix, iy);
            if (L.Kind[c] == CellKind.Outside) continue;
            int cause = sim.Causes.Add(CauseKind.Impact, $"{Ko.I(what)} 부딪혔다 ({energy / 1000:0.#}kJ)", sim.Tick, c, parentCause);
            int dx = Math.Abs(ux) >= Math.Abs(uy) ? Math.Sign(ux) : 0, dy = dx == 0 ? Math.Sign(uy) : 0;
            sim.Find<Structure>()?.Impact(sim, c, energy, dx, dy, cause);
            return true;
        }
        return false;
    }

    /// <summary>칸마다 가림: 햇빛 쪽(절반) + 네 방향 평균(절반)으로 바깥까지 지나는 벽 · 설비의 면밀도 → e^(−면밀도/300kg/m²).</summary>
    private void Radiation()
    {
        if (Math.Abs(_shieldAngle - SunAngle) > 0.15 || double.IsNaN(_shieldAngle))
        {
            _shieldAngle = SunAngle;
            double sx = Math.Cos(SunAngle), sy = Math.Sin(SunAngle);
            for (int i = 0; i < L.N; i++)
            {
                if (!L.IsSpace(i)) { _shield[i] = 0; continue; }
                double sunSide = Areal(i, sx, sy);
                double avg = (Areal(i, 1, 0) + Areal(i, -1, 0) + Areal(i, 0, 1) + Areal(i, 0, -1)) / 4;
                _shield[i] = (float)(0.5 * Math.Exp(-sunSide / 300) + 0.5 * Math.Exp(-avg / 300));
            }
        }
        for (int i = 0; i < L.N; i++) Dose[i] = (float)(DoseOut * _shield[i]);
    }

    /// <summary>칸 i에서 (dx, dy) 쪽으로 바깥까지 지나는 면밀도 (kg/m²).</summary>
    private double Areal(int i, double dx, double dy)
    {
        double x = L.X(i) + 0.5, y = L.Y(i) + 0.5, sum = 0;
        int last = i;
        for (int s = 0; s < 400; s++)
        {
            x += dx * 0.5; y += dy * 0.5;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            if (!L.In(ix, iy)) break;
            int c = L.Index(ix, iy);
            if (c == last) continue;
            last = c;
            var kind = L.Kind[c];
            if (kind == CellKind.Outside) break;
            var m = T.Materials[L.Material[c]].Def;
            if (kind == CellKind.Wall) sum += m.Density * (L.Hull[c] ? 0.010 : 0.004) * (F.Hole[c] >= 2 ? 0 : 1);
            if (L.FurnitureAt[c] >= 0) sum += F.Cs[c] / 500.0; // 설비 질량 (열용량 ÷ 강철 비열 ≈ kg) 이 1m²에 있다고
            sum += F.Stored(T.sWater, c) * 0.018; // 고인 물
        }
        return sum;
    }

    private int Poisson(double lambda)
    {
        if (lambda <= 0) return 0;
        if (lambda > 30) return Math.Max(0, (int)Math.Round(_rng.Normal(lambda, Math.Sqrt(lambda))));
        double l = Math.Exp(-lambda), p = 1; int k = 0;
        do { k++; p *= _rng.Unit(); } while (p > l);
        return k - 1;
    }

    /// <summary>거듭제곱 분포 (지수 alpha) 질량 — 작은 것이 대부분.</summary>
    private double PowerLaw(double lo, double hi, double alpha)
    {
        double a = 1 - alpha, u = _rng.Unit();
        return Math.Pow(Math.Pow(lo, a) + u * (Math.Pow(hi, a) - Math.Pow(lo, a)), 1 / a);
    }

    public void Save(StateWriter w)
    {
        w.Str(Star); w.F64(Distance); w.F64(SunAngle); w.F64(SunDrift); w.Str(Planet); w.I64(PlanetSince); w.Bool(Storm); w.I64(StormUntil);
        w.I64(NextFlare); w.I64(FlareWarnAt); w.I64(FlareStart); w.I64(FlareEnd); w.I32(FlareCause); w.F64(DoseOut);
        w.List(Clouds); w.List(Rocks);
        w.I64(DebrisHits); w.I64(RockHits); w.I64(NearMisses); w.I64(Flares);
        _rng.Save(w);
        w.Arr(Dose); w.Arr(_shield); w.F64(_shieldAngle);
    }

    public void Load(StateReader r)
    {
        Star = r.Str()!; Distance = r.F64(); SunAngle = r.F64(); SunDrift = r.F64(); Planet = r.Str(); PlanetSince = r.I64(); Storm = r.Bool(); StormUntil = r.I64();
        NextFlare = r.I64(); FlareWarnAt = r.I64(); FlareStart = r.I64(); FlareEnd = r.I64(); FlareCause = r.I32(); DoseOut = r.F64();
        Clouds = r.List<Cloud>(); Rocks = r.List<Rock>();
        DebrisHits = r.I64(); RockHits = r.I64(); NearMisses = r.I64(); Flares = r.I64();
        _rng.Load(r);
        r.ArrInto(Dose); r.ArrInto(_shield); _shieldAngle = r.F64();
    }
}
