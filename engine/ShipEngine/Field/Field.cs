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
    public const double BleedArea = 0.005;       // m² (문의 압력 맞춤 밸브 50cm² — 에어락을 1~2분에 비우거나 채운다)
    public const double LeakNormalDoor = 0.0005; // m² (닫힌 보통 문의 틈 5cm² — 이웃 방이 진공이면 반 시간쯤에 걸쳐 샌다)
    public const double Sigma = 5.670374e-8;
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
    public byte[] DoorTaped;    // 1: 닫힌 문의 틈을 봉합 꾸러미로 막았다 (문을 열면 떨어진다)
    public byte[] DoorBleed;    // 1: 닫힌 문의 압력 맞춤 밸브가 열렸다 (양쪽 기압을 맞춰야 문이 열린다 — 구조가 연다)
    /// <summary>벽 칸의 구멍 넓이 (m²). 2 이상이면 벽이 뚫려 칸이 트인다.</summary>
    public float[] Hole;
    /// <summary>
    /// 1: 이 칸은 이제 우주다 — 배가 갈라져 떨어져 나간 자리 (④ 구조가 요청한다 · <see cref="Sever"/>). 기체 · 액체가 빠지고 고체 열이 끊기며 이웃 칸에는 우주와의 틈이 된다.
    /// 바닥 칸의 <see cref="Hole"/>은 바닥 · 천장 판의 구멍 (한 층짜리 배 — 그 너머가 우주다).
    /// </summary>
    public byte[] Severed;
    /// <summary>칸의 바닥 쪽 중력 (g — 1이면 지구 · 0이면 무중력). ⑥ 인공 중력 발생기가 몸마다 정해 요청한다 (발생기가 없는 배 · 시험 배는 바탕 1g 그대로).</summary>
    public float[] Grav;
    /// <summary>이 아래(g)면 무중력처럼 — 고인 액체가 퍼지지 않고 방울로 떠오르며 · 사람이 떠서 손잡이를 잡고 간다 (⑧이 같은 문턱을 읽는다).</summary>
    public const float LowG = 0.3f;
    /// <summary>떠 있는 액체 (저장 종류마다 · 칸마다 몰 — [저장 × 칸]): 무중력에서 바닥을 떠난 방울. 공기를 따라 떠돌다 겉면에 들러붙고 · 중력이 서면 그 자리에 떨어진다.</summary>
    public double[] Drops;
    /// <summary>끊겨 우주로 간 응축상 (저장 종류마다 몰 — 장부) · 끊긴 칸에서 빠진 기체는 <see cref="VentedTotal"/>에.</summary>
    public double[] SeveredStore;
    /// <summary>바람 (m/s) — 틈으로 빠지는 흐름에서 계산한다.</summary>
    public float[] WindX, WindY;
    /// <summary>
    /// 이 틱 처음 돌풍의 동압 ½ρv² (Pa — 이 틱 동안만 · 찍지 않는다): 감압은 한 틱 안에 방을 비우니 틱 평균 바람으로는 물건을 미는 힘을 잴 수 없다.
    /// 구역 몰 수가 지수로 줄었다고 보고(틈 흐름이 그 식이다) 처음 흐름 = n₀·ln(n₀/n₁)/Δt · 처음 밀도 = 끝 밀도 × n₀/n₁.
    /// </summary>
    public float[] WindPa;
    /// <summary>틱마다 바깥으로 빠진 몰 수 누계 (시험 · 연출).</summary>
    public double VentedTotal;
    public Exterior Outside = Exterior.Space();
    /// <summary>배의 가속도 (m/s² · 도면 좌표 — 점화 · 피하는 기동): ⑤ 바깥이 틱마다 넣는 요청. 배 안의 것은 반대쪽(−a)으로 쏠린다 (바닥 물 · 떠다니는 것). 찍지 않는다 — 바깥이 다시 넣는다.</summary>
    public double AccelX, AccelY;

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
        DoorOpen = new float[layout.Doors.Count]; DoorTaped = new byte[layout.Doors.Count]; DoorBleed = new byte[layout.Doors.Count]; Hole = new float[Cells]; Severed = new byte[Cells]; Grav = new float[Cells]; Array.Fill(Grav, 1f); SeveredStore = new double[t.StoreCount]; Drops = new double[S * Cells];
        WindX = new float[Cells]; WindY = new float[Cells]; WindPa = new float[Cells];
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

    /// <summary>몰 · 기체 열용량을 한 번에 (Moles · GasC와 같은 값).</summary>
    public double Moles(int i, out double gasC)
    {
        double n = 0, c = 0;
        for (int g = 0; g < G; g++) { double v = N[i * G + g]; n += v; c += v * _cv[g]; }
        gasC = c;
        return n;
    }

    /// <summary>기체 열용량을 이미 셌을 때의 온도 (Temp와 같은 값).</summary>
    public double Temp(int i, double gasC) => gasC > 1e-6 ? U[i] / gasC : Ts[i];

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

    /// <summary>몰 · 기체 열용량 · 압력을 한 번에 (Moles · GasC · Pressure와 같은 값 — 칸의 여러 값을 읽는 센서가 쓴다).</summary>
    public double Read(int i, out double gasC, out double kPa)
    {
        double n = 0, c = 0;
        for (int g = 0; g < G; g++) { double v = N[i * G + g]; n += v; c += v * _cv[g]; }
        gasC = c;
        kPa = c > 1e-9 ? n * R * (U[i] / c) / CellVolume / 1000.0 : 0;
        return n;
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
    public void AddHeat(int i, double joules) => AddHeat(i, joules, GasC(i));

    /// <summary>AddHeat과 같다 — 칸 기체 열용량(GasC)을 이미 셌을 때 (그 사이 기체가 바뀌지 않았어야 한다).</summary>
    public void AddHeat(int i, double joules, double gasC)
    {
        if (gasC > 50)
        {
            // 공기 열의 절반을 넘는 흡열(배터리 전해액이 갈라질 때)은 그 칸의 고체(열용량이 큰 쪽 · 없으면 겉면)가 낸다 — 0에서 잘려 에너지가 생기던 빈틈
            if (joules < -0.5 * U[i] && (Cs[i] > 0 || HasSurface(i)))
            {
                double rest = joules + 0.5 * U[i];
                if (Cs[i] > 0) AddSolidHeat(i, rest); else AddSurfaceHeat(i, rest);
                joules = -0.5 * U[i];
            }
            U[i] = Math.Max(0, U[i] + joules); if (RegionOf.Length > i) MarkDirty(RegionOf[i], joules / Math.Max(1, U[i]));
        }
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
        if (open > 0f) { DoorTaped[door] = 0; DoorBleed[door] = 0; } // 열면 막은 것이 떨어지고 밸브는 닫힌다
        // 지나다닐 수 있는지가 바뀌거나 틈 넓이가 바뀌면 구역을 다시 짠다
        if (before != DoorPassable(door) || linkBefore != (open > 0f) || !before) _dirty = true;
    }

    /// <summary>
    /// 이 칸이 배에서 떨어져 나간다 (④ 구조의 요청 — 배가 갈라진 자리): 기체는 우주로(<see cref="VentedTotal"/>) · 고인 것도 우주로 · 고체 열용량 · 전도가 끊긴다 ·
    /// 곁의 칸에는 우주로 트인 틈이 된다. 되돌릴 수 없다.
    /// </summary>
    public void Sever(int cell)
    {
        if (Severed[cell] != 0 || L.Kind[cell] == CellKind.Outside) return;
        Severed[cell] = 1;
        double n = 0;
        for (int g = 0; g < G; g++) { n += N[cell * G + g]; N[cell * G + g] = 0; }
        U[cell] = 0; VentedTotal += n;
        for (int s = 0; s < S; s++) { SeveredStore[s] += Store[s * Cells + cell] + Drops[s * Cells + cell]; Store[s * Cells + cell] = 0; Drops[s * Cells + cell] = 0; }
        Cs[cell] = 0; Hole[cell] = 0;
        SolidLinks(); // 곁의 벽이 우주를 본다 (복사 · 햇빛) · 전도가 끊긴다
        _dirty = true;
    }

    /// <summary>바깥(우주 · 행성 대기)인 칸: 도면의 바깥 · 떨어져 나간 자리.</summary>
    public bool IsOut(int i) => L.Kind[i] == CellKind.Outside || Severed[i] != 0;

    /// <summary>구멍 넓이가 바뀔 때마다 오른다 (위층 캐시용 — ④ 잔칸이 구멍 모양을 맞춘다 · 찍지 않는다).</summary>
    public int HoleVersion;

    public void SetHole(int cell, float area)
    {
        area = Math.Max(0f, area);
        if (Hole[cell] == area) return;
        Hole[cell] = area;
        _dirty = true;
        HoleVersion++;
    }

    public bool DoorPassable(int door) => DoorOpen[door] >= 0.5f;

    /// <summary>닫힌 보통 문의 틈으로 새고 있나 (막지 않았다).</summary>
    public bool DoorGapOpen(int door) => DoorOpen[door] <= 0f && DoorTaped[door] == 0 && DoorBleed[door] == 0 && L.Doors[door].Kind == "normal";

    /// <summary>닫힌 문의 압력 맞춤 밸브를 열고 닫는다.</summary>
    public void SetBleed(int door, bool on)
    {
        byte v = (byte)(on && DoorOpen[door] <= 0f ? 1 : 0);
        if (DoorBleed[door] == v) return;
        DoorBleed[door] = v; _dirty = true;
    }

    /// <summary>닫힌 문의 틈을 막는다 (봉합 꾸러미).</summary>
    public void TapeDoor(int door)
    {
        if (DoorOpen[door] > 0f || DoorTaped[door] != 0) return;
        DoorTaped[door] = 1; _dirty = true;
    }

    /// <summary>기체가 지나다닐 수 있는 칸: 바닥 · 반 넘게 열린 문 · 뚫린 벽.</summary>
    public bool Passable(int i) => Severed[i] == 0 && L.Kind[i] switch
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
        Array.Clear(WindX); Array.Clear(WindY); Array.Clear(WindPa);
        FlowLinks(dt);
        t = Lap("틈 흐름", t);
        Equalize();
        t = Lap("압력 고르기", t);
        // 달궈지거나 식은 겉면(바닥과 2K 넘게 다름)은 매 틱 열을 주고받는다 — 불이 붙고 번지는 빠르기
        for (int i = 0; i < Cells; i++)
            if (Math.Abs(Tf[i] - Ts[i]) > 2 && HasSurface(i)) { SurfaceExchange(i, dt); _surfTick[i] = sim.Tick; }
        FloatLiquids(dt);   // 무중력 방울 — 틈 흐름의 바람을 이 틱에 따라간다 (돌풍은 몇 틱이면 그친다)
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
        // 칸 고체 열용량(설비를 앉히며 늘었다) · 구역별 '아직 고를 차이' · 겉면을 매 틱 다룬 때 · 구역을 지은 때의 문 · 구멍 — 다음 틱의 계산을 바꾸므로 찍는다
        w.Arr(Cs); w.Arr(_imb); w.Arr(_surfTick); w.Bool(_dirty); w.Arr(_topoDoor); w.Arr(_topoHole); w.Arr(DoorTaped); w.Arr(_topoTaped); w.Arr(DoorBleed); w.Arr(_topoBleed);
        w.Tag("끊김"); w.Arr(Severed); w.Arr(SeveredStore);
        w.Tag("중력"); w.Arr(Grav); w.Arr(Drops);
    }

    public void Load(StateReader r)
    {
        r.ArrInto(N); r.ArrInto(U); r.ArrInto(Ts); r.ArrInto(Tf); r.ArrInto(Store); r.ArrInto(DoorOpen); r.ArrInto(Hole); r.ArrInto(WindX); r.ArrInto(WindY);
        VentedTotal = r.F64();
        Outside.Load(r);
        r.ArrInto(Cs); var imb = r.Arr<double>(); _surfTick = r.Arr<long>(); bool dirty = r.Bool(); _topoDoor = r.Arr<float>(); _topoHole = r.Arr<float>(); r.ArrInto(DoorTaped); _topoTaped = r.Arr<byte>(); r.ArrInto(DoorBleed); _topoBleed = r.Arr<byte>();
        r.Tag("끊김"); r.ArrInto(Severed); r.ArrInto(SeveredStore);
        r.Tag("중력"); r.ArrInto(Grav); r.ArrInto(Drops); _dropsAny = Drops.Any(v => v > 0);
        SolidLinks(); // 끊긴 칸의 전도 · 우주를 보는 면은 도면 + 끊김에서 다시 센다 (찍지 않는다)
        // 구역 · 틈은 찍지 않고 다시 세운다 — 장보다 먼저 도는 엔진(망 · 마음 · 주컴퓨터)이 첫 틱에 구역을 읽는다
        RestoreTopology();
        _dirty = dirty; _imb = imb;
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
