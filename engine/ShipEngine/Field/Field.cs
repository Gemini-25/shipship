namespace ShipEngine;

/// <summary>
/// ③ 장 물리 — 기체 · 열 · 액체 (docs/ENGINE.md §4 ③).
/// <para>
/// 칸 = 1m × 1m × 2.5m. 칸마다 성분별 몰 수(<see cref="N"/> — 성분 우선 배열) · 기체 현열(<see cref="U"/>, J · 0K 기준) ·
/// 바닥/벽 고체 온도(<see cref="Ts"/>) · 바닥에 고인 것(<see cref="Store"/> — 물 · 연료 · 그을음).
/// </para>
/// <para>
/// 매 틱: 틈 흐름(문틈 · 금 · 구멍 · 바깥) → 구역 안 압력 고르기 → 바람.
/// 5틱마다: 확산(성분 · 열) → 기체↔바닥 열 → 고체 전도 · 외판 복사 · 햇빛 → 응결 · 증발 · 물 퍼짐 · 그을음 가라앉음.
/// </para>
/// 위층(구조 · 장치 · 마음)은 <see cref="SetDoor"/> · <see cref="SetHole"/> · <see cref="AddGas"/> · <see cref="AddHeat"/> 같은 요청으로만 바꾼다.
/// </summary>
public sealed partial class Field : IEngine
{
    public const double R = 8.314462618;
    public const double CellVolume = 2.5;       // m³
    public const double CellFloor = 1.0;        // m²
    public const double WallFace = 2.5;         // m² (벽 한 칸의 한쪽 면)
    public const double DoorArea = 2.0;         // m² (문 1m × 2m)
    public const double LeakNormalDoor = 0.0005; // m² (닫힌 보통 문의 틈 5cm² — 이웃 방이 진공이면 반 시간쯤에 걸쳐 샌다)
    public const double Sigma = 5.670374e-8;
    public const double HullEmissivity = 0.03;  // 외판 단열재(다층 단열) 덕분에 낮다 — 구멍 난 곳은 재질 그대로
    public const int SlowEvery = 5;

    public string Name => "장";
    public Phase Phase => Phase.Field;
    public int Every => 1;
    public int Offset => 0;

    public readonly Layout L;
    public readonly Tables T;
    public readonly int Cells, G, S;

    // ── 상태 (찍기에 들어간다) ──
    /// <summary>성분별 몰 수: N[i * G + g].</summary>
    public double[] N;
    /// <summary>기체 현열 (J · 0K 기준) = Σ n cv T.</summary>
    public double[] U;
    /// <summary>고체(바닥 · 벽) 온도 K.</summary>
    public double[] Ts;
    /// <summary>
    /// 겉면 온도 K (바닥 · 문 칸): 물건 · 연료의 얇은 겉면 — 열용량이 작아 불꽃 · 복사에 금방 달궈진다 (불이 붙고 번지는 곳).
    /// 기체 · 고체와 열을 주고받는다.
    /// </summary>
    public double[] Tf;
    public const double SurfaceC = 4000; // J/K (겉면 약 2kg)
    /// <summary>겉면을 매 틱 다룬 마지막 틱 (5틱마다의 열 단계에서 겹쳐 세지 않게).</summary>
    private long[] _surfTick = Array.Empty<long>();
    private long _tickNow;
    /// <summary>바닥에 고이거나 쌓인 응축상: Store[s * Cells + i] (몰).</summary>
    public double[] Store;
    /// <summary>문마다 열린 정도 0~1.</summary>
    public float[] DoorOpen;
    /// <summary>벽 칸의 구멍 넓이 (m²). 2 이상이면 벽이 뚫려 칸이 트인다.</summary>
    public float[] Hole;
    /// <summary>바람 (m/s) — 틈으로 빠지는 흐름에서 계산한다.</summary>
    public float[] WindX, WindY;
    /// <summary>틱마다 바깥으로 빠진 몰 수 누계 (시험 · 연출).</summary>
    public double VentedTotal;
    public Exterior Outside = Exterior.Space();

    // ── 도면에서 정해지는 것 ──
    /// <summary>고체 열용량 J/K (바닥판 + 설비 / 벽).</summary>
    public readonly double[] Cs;
    /// <summary>고체끼리 이웃 열전도 (오른쪽 · 아래쪽, W/K).</summary>
    private readonly double[] _kRight, _kDown;
    /// <summary>바깥에 드러난 외판 면의 수 (복사 · 햇빛).</summary>
    private readonly byte[] _exposed;
    private readonly double[] _cv;      // 성분별 cv
    private readonly double[] _molar;   // 성분별 kg/몰

    public Field(Layout layout, Tables t)
    {
        L = layout; T = t; Cells = layout.N; G = t.GasCount; S = t.StoreCount;
        N = new double[G * Cells]; U = new double[Cells]; Ts = new double[Cells]; Tf = new double[Cells]; Store = new double[S * Cells];
        DoorOpen = new float[layout.Doors.Count]; Hole = new float[Cells];
        WindX = new float[Cells]; WindY = new float[Cells];
        _cv = t.Gases.Select(s => s.C).ToArray();
        _molar = t.Gases.Select(s => s.MolarMass).ToArray();
        Cs = new double[Cells]; _kRight = new double[Cells]; _kDown = new double[Cells]; _exposed = new byte[Cells];
        BuildSolids();
        Array.Fill(Ts, 293.15);
        Array.Fill(Tf, 293.15);
        _dirty = true;
    }

    // ───────────────────────── 읽기 ─────────────────────────

    public double Moles(int i)
    {
        double n = 0;
        for (int g = 0; g < G; g++) n += N[i * G + g];
        return n;
    }

    /// <summary>기체 열용량 Σ n cv (J/K).</summary>
    public double GasC(int i)
    {
        double c = 0;
        for (int g = 0; g < G; g++) c += N[i * G + g] * _cv[g];
        return c;
    }

    /// <summary>기체 온도 (K). 기체가 거의 없으면 바닥 온도.</summary>
    public double Temp(int i)
    {
        double c = GasC(i);
        return c > 1e-6 ? U[i] / c : Ts[i];
    }

    /// <summary>압력 (kPa).</summary>
    public double Pressure(int i)
    {
        double n = 0, c = 0;
        for (int g = 0; g < G; g++) { double v = N[i * G + g]; n += v; c += v * _cv[g]; }
        return c > 1e-9 ? n * R * (U[i] / c) / CellVolume / 1000.0 : 0;
    }

    /// <summary>분압 (kPa).</summary>
    public double Partial(int g, int i)
    {
        double n = Moles(i);
        return n > 1e-12 ? Pressure(i) * N[i * G + g] / n : 0;
    }

    /// <summary>몰 분율 → ppm.</summary>
    public double Ppm(int g, int i)
    {
        double n = Moles(i);
        return n > 1e-9 ? N[i * G + g] / n * 1e6 : 0;
    }

    public double Gas(int g, int i) => N[i * G + g];
    public double Stored(int s, int i) => Store[s * Cells + i];

    /// <summary>배 안 성분 g의 총량 (몰).</summary>
    public double Total(int g)
    {
        double s = 0;
        for (int i = 0; i < Cells; i++) s += N[i * G + g];
        return s;
    }

    // ───────────────────────── 요청 ─────────────────────────

    /// <summary>칸에 기체를 넣는다 (온도 temp K의 기체 — 현열도 함께).</summary>
    public void AddGas(int i, int g, double moles, double temp)
    {
        if (moles <= 0) return;
        N[i * G + g] += moles;
        U[i] += moles * _cv[g] * temp;
        if (RegionOf.Length > i) MarkDirty(RegionOf[i], moles / Math.Max(1, Moles(i)));
    }

    /// <summary>칸에서 기체를 뺀다 (그 칸 온도의 현열도 함께). 실제로 뺀 몰 수.</summary>
    public double RemoveGas(int i, int g, double moles)
    {
        int k = i * G + g;
        double have = N[k];
        if (moles <= 0 || have <= 0) return 0;
        double take = Math.Min(moles, have);
        double c = GasC(i);
        double temp = c > 1e-9 ? U[i] / c : 0;
        N[k] = have - take;
        U[i] = Math.Max(0, U[i] - take * _cv[g] * temp);
        if (RegionOf.Length > i) MarkDirty(RegionOf[i], take / Math.Max(1, Moles(i) + take));
        return take;
    }

    /// <summary>칸에 열 (J). 기체가 거의 없으면 바닥으로.</summary>
    public void AddHeat(int i, double joules)
    {
        if (GasC(i) > 50) { U[i] = Math.Max(0, U[i] + joules); if (RegionOf.Length > i) MarkDirty(RegionOf[i], joules / Math.Max(1, U[i])); }
        else AddSolidHeat(i, joules);
    }

    /// <summary>겉면에 열 (J) — 바닥 · 문 칸만 (아니면 고체로).</summary>
    public void AddSurfaceHeat(int i, double joules)
    {
        if (HasSurface(i)) Tf[i] = Math.Max(2.7, Tf[i] + joules / SurfaceC);
        else AddSolidHeat(i, joules);
    }

    public bool HasSurface(int i) => L.Kind[i] is CellKind.Floor or CellKind.Door;

    public void AddSolidHeat(int i, double joules)
    {
        if (Cs[i] > 0) Ts[i] = Math.Max(2.7, Ts[i] + joules / Cs[i]);
    }

    public void AddStore(int i, int s, double moles) { if (moles > 0) Store[s * Cells + i] += moles; }

    public double TakeStore(int i, int s, double moles)
    {
        int k = s * Cells + i;
        double take = Math.Clamp(moles, 0, Store[k]);
        Store[k] -= take;
        return take;
    }

    /// <summary>
    /// 두 칸이 같은 몰 수 m = frac × min(nᵢ, nⱼ)을 주고받는다 (성분 · 열이 섞이고 압력은 그대로 — 보존).
    /// 불꽃의 상승 기류가 둘레 공기를 빨아들이는 것 · 선풍기 · 사람의 움직임.
    /// </summary>
    public void Exchange(int i, int j, double frac)
    {
        double ni = Moles(i), nj = Moles(j);
        if (ni <= 1e-12 || nj <= 1e-12 || frac <= 0) return;
        double m = Math.Min(0.45, frac) * Math.Min(ni, nj);
        double a = m / nj, b = m / ni;
        int bi = i * G, bj = j * G;
        for (int g = 0; g < G; g++)
        {
            double d = a * N[bj + g] - b * N[bi + g];
            N[bi + g] += d; N[bj + g] -= d;
        }
        double du = a * U[j] - b * U[i];
        U[i] += du; U[j] -= du;
        if (RegionOf.Length > i) MarkDirty(RegionOf[i], m / ni * 0.01);
    }

    public void SetDoor(int door, float open)
    {
        open = Math.Clamp(open, 0f, 1f);
        if (DoorOpen[door] == open) return;
        bool before = DoorPassable(door), linkBefore = DoorOpen[door] > 0f;
        DoorOpen[door] = open;
        // 지나다닐 수 있는지가 바뀌거나 틈 넓이가 바뀌면 구역을 다시 짠다
        if (before != DoorPassable(door) || linkBefore != (open > 0f) || !before) _dirty = true;
    }

    public void SetHole(int cell, float area)
    {
        area = Math.Max(0f, area);
        if (Hole[cell] == area) return;
        Hole[cell] = area;
        _dirty = true;
    }

    public bool DoorPassable(int door) => DoorOpen[door] >= 0.5f;

    /// <summary>기체가 지나다닐 수 있는 칸: 바닥 · 반 넘게 열린 문 · 뚫린 벽.</summary>
    public bool Passable(int i) => L.Kind[i] switch
    {
        CellKind.Floor => true,
        CellKind.Door => DoorPassable(L.DoorAt[i]),
        CellKind.Wall => Hole[i] >= 2f,
        _ => false,
    };

    /// <summary>처음 공기를 채운다 (1기압 · 습도 50%).</summary>
    public void FillAir(double kPa = 101.325, double temp = 293.15, double humidity = 0.5)
    {
        double pH2O = Math.Min(kPa * 0.05, humidity * Water.SatKPa(temp));
        double rest = kPa - pH2O;
        var frac = new double[G];
        frac[T.gN2] = 0.7808 * rest / kPa; frac[T.gO2] = 0.2095 * rest / kPa; frac[T.gAr] = 0.0093 * rest / kPa;
        frac[T.gCO2] = 0.0004 * rest / kPa; frac[T.gH2O] = pH2O / kPa;
        double n = kPa * 1000 * CellVolume / (R * temp);
        for (int i = 0; i < Cells; i++)
        {
            if (!Passable(i)) continue;
            for (int g = 0; g < G; g++) N[i * G + g] = n * frac[g];
            U[i] = GasC(i) * temp;
            Ts[i] = temp; Tf[i] = temp;
        }
        for (int i = 0; i < Cells; i++) if (L.Kind[i] == CellKind.Wall || L.Kind[i] == CellKind.Door) Ts[i] = temp;
        _dirty = true;
        RebuildNow();
    }

    // ───────────────────────── 한 틱 ─────────────────────────

    /// <summary>부분별 누적 시간 (ms) — 성능 측정 때만.</summary>
    public static Dictionary<string, double>? Prof;

    private long Lap(string name, long t0)
    {
        if (Prof == null) return 0;
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        Prof[name] = Prof.GetValueOrDefault(name) + (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return t1;
    }

    public void Step(Sim sim, double dt)
    {
        _tickNow = sim.Tick;
        if (_surfTick.Length != Cells) { _surfTick = new long[Cells]; Array.Fill(_surfTick, long.MinValue / 2); }
        long t = Prof != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (_dirty) Rebuild();
        t = Lap("구역", t);
        Array.Clear(WindX); Array.Clear(WindY);
        FlowLinks(dt);
        t = Lap("틈 흐름", t);
        Equalize();
        t = Lap("압력 고르기", t);
        // 달궈지거나 식은 겉면(바닥과 2K 넘게 다름)은 매 틱 열을 주고받는다 — 불이 붙고 번지는 빠르기
        for (int i = 0; i < Cells; i++)
            if (Math.Abs(Tf[i] - Ts[i]) > 2 && HasSurface(i)) { SurfaceExchange(i, dt); _surfTick[i] = sim.Tick; }
        if (sim.Tick % SlowEvery == 0)
        {
            double slow = dt * SlowEvery;
            Diffuse(slow);
            t = Lap("확산", t);
            Heat(slow);
            t = Lap("열", t);
            PhaseChange(slow);
            Lap("상 변화", t);
            MarkAllDirty(); // 온도가 바뀌어 압력이 달라졌다
        }
    }

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.Arr(N); w.Arr(U); w.Arr(Ts); w.Arr(Tf); w.Arr(Store); w.Arr(DoorOpen); w.Arr(Hole); w.Arr(WindX); w.Arr(WindY);
        w.F64(VentedTotal);
        Outside.Save(w);
    }

    public void Load(StateReader r)
    {
        r.ArrInto(N); r.ArrInto(U); r.ArrInto(Ts); r.ArrInto(Tf); r.ArrInto(Store); r.ArrInto(DoorOpen); r.ArrInto(Hole); r.ArrInto(WindX); r.ArrInto(WindY);
        VentedTotal = r.F64();
        Outside.Load(r);
        _dirty = true;
        _imb = Array.Empty<double>();
    }
}

/// <summary>배 바깥 — 진공(우주) 또는 행성 대기. ⑤ 바깥 세계가 바꾼다.</summary>
public sealed class Exterior : IState
{
    /// <summary>압력 kPa (0: 진공).</summary>
    public double KPa;
    /// <summary>기체 온도 K (진공에서는 복사 배경 온도).</summary>
    public double Temp = 2.7;
    /// <summary>성분 몰 분율 (기체 칸 순서).</summary>
    public double[] Frac = Array.Empty<double>();
    /// <summary>햇빛 세기 (W/m²) · 방향 (도면 좌표 — 빛이 오는 쪽).</summary>
    public double Sun = 1361, SunX = 1, SunY = 0;

    public bool Vacuum => KPa < 1e-6;

    public static Exterior Space() => new();

    public void Save(StateWriter w) { w.F64(KPa); w.F64(Temp); w.Arr(Frac); w.F64(Sun); w.F64(SunX); w.F64(SunY); }
    public void Load(StateReader r) { KPa = r.F64(); Temp = r.F64(); Frac = r.Arr<double>(); Sun = r.F64(); SunX = r.F64(); SunY = r.F64(); }
}

/// <summary>물의 포화 증기압 등.</summary>
public static class Water
{
    /// <summary>포화 수증기압 (kPa) — 클라우지우스-클라페이롱 (증발열 44kJ/몰 · 100℃에서 1기압).</summary>
    public static double SatKPa(double tempK) => tempK < 150 ? 0 : 101.325 * Math.Exp(-44000.0 / Field.R * (1.0 / tempK - 1.0 / 373.15));

    /// <summary>끓는점 boilK · 증발열 lJ인 물질의 포화 증기압 (kPa).</summary>
    public static double SatKPa(double tempK, double boilK, double latentJ) =>
        tempK < 30 ? 0 : 101.325 * Math.Exp(-latentJ / Field.R * (1.0 / tempK - 1.0 / boilK));
}
