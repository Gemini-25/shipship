namespace ShipEngine;

/// <summary>소화제 (② (바)).</summary>
public enum Extinguishant : byte { Water, CO2 }

/// <summary>
/// ② 물질 — 칸의 화학 (불 · 기체 폭발 · 느린 반응). 반응식 표를 그대로 칸 위에서 돌린다.
/// <para>
/// <b>불</b>: 칸의 연료(바닥 저장의 고분자 · 섬유 · 양분 · 그을음)가 발화 온도를 넘으면 붙는다. 타는 빠르기는 산소 분압 · 남은 연료로 정해지고,
/// 산소가 모자라면 일산화탄소 · 그을음이 는다. 열의 70%는 그 칸 기체로, 15%는 바닥으로, 15%는 이웃 칸으로 (복사 — 그래서 옆 가구로 번진다).
/// 기체가 500K 아래로 식거나 · 산소 분압이 9kPa 아래거나 · 연료가 떨어지면 꺼진다 (진공으로 빼면 꺼진다 · 물을 부으면 식어서 꺼진다).
/// </para>
/// <para>
/// <b>기체 불(폭발)</b>: 수소 · 메탄 · 일산화탄소 · 암모니아가 연소 범위 안에 있고 불씨(불 붙은 이웃 · 전기 불꽃)가 닿거나 자연 발화 온도를 넘으면
/// 한 틱에 대부분이 탄다 → 온도 · 압력이 튄다 → 이웃 칸이 다음 틱의 불씨가 된다 (불꽃 앞면이 퍼진다).
/// </para>
/// 에너지 장부: 반응열 = Σ반응물 E − Σ생성물 E (E = 생성 엔탈피 + 기체는 현열) — 장의 장부와 맞아 보존된다 (법칙 시험).
/// </summary>
public sealed class Chemistry : IEngine
{
    public string Name => "화학";
    public Phase Phase => Phase.Chemistry;
    public int Every => 1;
    public int Offset => 0;

    /// <summary>불 붙은 칸 하나가 산소가 넉넉할 때 내는 열 (W) — 가구 하나가 활활 타는 정도.</summary>
    public const double CellHrr = 250_000;
    public const double SustainK = 500;
    /// <summary>② (다) 이 기압(kPa) 아래면 불꽃이 서지 못한다 (열을 너무 빨리 잃는다).</summary>
    public const double QuenchKPa = 8;
    /// <summary>빠르기 몫의 잣대 (몰분율): 한계를 이만큼 넘으면 1 — 옛 분압 모형이 불 칸(뜨거워 기압 약 110kPa)에서 보이던 기울기(1kPa에 1/12)와 같게 맞췄다.</summary>
    public const double LoiSpan = 0.109;

    private readonly Field F;
    private readonly Tables T;
    private readonly int Cells;

    // ── 상태 ──
    public byte[] Burning;
    /// <summary>불 붙은 칸의 원인 마디.</summary>
    public int[] FireCause;
    /// <summary>칸마다 불붙는 기체가 새어 나온 원인 (② (마) — ⑥이 요청: 봉함 샘 · 관 샘 · 배터리 뿜음). 그 칸에 불붙는 기체가 없어지면 지운다.</summary>
    public int[] GasCause;
    /// <summary>이번 틱 칸에 뿜은 이산화탄소 소화기 분사 (몰/초 — 다음 틱 불꽃이 들이쉬는 공기에 섞인다 · ② (바)).</summary>
    private double[] _jet = Array.Empty<double>(), _jetNext = Array.Empty<double>();
    /// <summary>칸을 달군 것의 원인 (켜 둔 조리대 · 과열 장치 — 위 층이 적는다) — 뜨거워 불이 붙으면 그 불의 부모가 된다.</summary>
    public int[] HeatCause;
    /// <summary>지난 틱에 낸 열 (kW) — 화면 · 연출.</summary>
    public float[] Hrr;
    /// <summary>꾸밈의 방화 담요가 덮은 칸 (남은 틱): 산소가 겉면에 닿지 않아 타지도 붙지도 않는다 — 덮은 지 약 5분.</summary>
    public byte[] Smother = Array.Empty<byte>();
    /// <summary>담요로 덮어 끌 수 있는 불 (그 칸의 열 방출 kW — 휴지통 · 작은 물건 하나의 불 · 이보다 크면 담요 둘레로 번진다).</summary>
    public const double BlanketMaxKw = 150;
    public const byte SmotherTicks = 125;
    /// <summary>이번 틱의 불씨 (전기 불꽃 · 불 붙은 이웃의 기체 불) — 다음 틱에 쓴다.</summary>
    private byte[] _spark, _sparkNext;
    private int[] _sparkCause, _sparkCauseNext;
    public long Ignitions, GasBursts;
    /// <summary>[칸] 이웃과 이어진 탈 것 (위 1 · 오른쪽 2 · 아래 4 · 왼쪽 8) — 위 층이 요청한다 (전선 묶음): 불꽃 앞머리가 그 탈 것을 따라 기어간다.</summary>
    public byte[] FuelLink;
    /// <summary>[칸] 옆 칸에서 기어 온 불꽃 앞머리 (m — 1이면 그 칸에 닿아 붙는다).</summary>
    public float[] Creep;
    /// <summary>이어진 고분자(전선 피복)를 따라 기는 불꽃 앞머리의 빠르기 (m/s · 산소 33%에서 — 잘 안 타는 피복은 25% 아래에서 스스로 꺼진다 · 이미 달궈진 피복은 보통 공기에서도 반쯤).</summary>
    public const double CreepMps = 0.03;
    public long Creeps;

    // ── 소리 (바퀴 2 소리 — ③ 소리 장에 요청만) ──
    /// <summary>방마다 아직 소리를 내지 않은 기체 불의 열 (J) · 가장 센 칸 · 원인 — 기체 불이 그친 틱에 쾅 하나 (찍는다).</summary>
    private readonly double[] _boomJ;
    private readonly int[] _boomCell, _boomCause;
    private readonly double[] _tickBurst; // 이번 틱 방마다 기체 불의 열 (틱 안에서만)
    private readonly double[] _roarKw;    // 우르릉: 방마다 불의 열 (틱 안에서만)
    private readonly int[] _roarCell, _roarCause;

    /// <summary>탈 것마다: 완전 · 일산화탄소 · 검댕 · 독가스 갈래 (불완전 몫을 나누는 비율) · 저장 · '넉넉한 양' (kg — 이만큼이면 제 빠르기로 탄다).</summary>
    private readonly (Reaction full, Reaction? co, Reaction? soot, Reaction? tox, double coShare, double sootShare, double toxShare, int store, double kgFull)[] _fuels;
    private readonly Reaction _pvcPyro, _burnPvc, _burnPoly, _ventEc, _ventSalt, _pf5Hydro;
    private readonly double _coldK;   // 이보다 차가운 칸은 어떤 탈 것도 붙지 않는다 (가장 낮은 발화 온도 − 산소 몫 150K)
    /// <summary>달궈진 피복의 탈염화수소 활성화 에너지 (J/몰 — 300℃에서 초당 0.1% · 250℃면 7분의 1 · 430℃면 45배).</summary>
    public const double PyroEa = 100_000;
    public double PyroHcl;
    private readonly (Reaction r, int gas, double lfl, double ufl)[] _gasFires;
    private readonly Reaction _ozone;

    public Chemistry(Field f)
    {
        F = f; T = f.T; Cells = f.Cells;
        Smother = new byte[Cells];
        Burning = new byte[Cells]; FireCause = new int[Cells]; HeatCause = new int[Cells]; Hrr = new float[Cells]; GasCause = new int[Cells]; _jet = new double[Cells]; _jetNext = new double[Cells];
        FuelLink = new byte[Cells]; Creep = new float[Cells];
        _spark = new byte[Cells]; _sparkNext = new byte[Cells]; _sparkCause = new int[Cells]; _sparkCauseNext = new int[Cells];
        // 넉넉한 양: 바퀴 1의 몰 잣대(200몰)를 그대로 질량으로 — 고분자 2.8kg · 섬유 32kg · 음식 36kg · 그을음 2.4kg · 피복 · 폼은 고분자와 같은 2.8kg
        _fuels = new[]
        {
            (T.R("burn_poly"), (Reaction?)T.R("burn_poly_co"), (Reaction?)T.R("burn_poly_soot"), (Reaction?)null, 0.6, 0.4, 0.0, T.sPoly, 2.8),
            (T.R("burn_cell"), T.R("burn_cell_co"), T.R("char_cell"), null, 0.6, 0.4, 0.0, T.sCell, 32.4),
            (T.R("burn_food"), null, null, null, 0, 0, 0, T.sFood, 36.0),
            (T.R("burn_soot"), null, null, null, 0, 0, 0, T.sSoot, 2.4),
            // ② 사고 화학: 피복(PVC)은 어느 갈래든 염소가 염화수소로 · 폼(폴리우레탄)은 산소가 모자라면 질소가 시안화수소로 (불완전 몫의 15% — 폼 불의 HCN 수율 1~2%)
            (T.R("burn_pvc"), T.R("burn_pvc_co"), T.R("burn_pvc_soot"), null, 0.6, 0.4, 0.0, T.sPvc, 2.8),
            (T.R("burn_pu"), T.R("burn_pu_co"), T.R("burn_pu_soot"), T.R("burn_pu_hcn"), 0.5, 0.35, 0.15, T.sPu, 2.8),
        };
        _coldK = _fuels.Min(fu => fu.full.Def.MinK) - 150;   // IgniteK의 아래 끝 (100% 산소)
        _pvcPyro = T.R("pvc_pyro"); _burnPvc = T.R("burn_pvc"); _burnPoly = T.R("burn_poly");
        _ventEc = T.R("vent_ec"); _ventSalt = T.R("vent_lipf6"); _pf5Hydro = T.R("pf5_hydro");
        // 연소 범위 (공기 중 부피 %): 수소 4~75 · 메탄 5~15 · 일산화탄소 12.5~74 · 암모니아 15~28
        _gasFires = new[]
        {
            (T.R("burn_h2"), T.gH2, 0.04, 0.75),
            (T.R("burn_ch4"), T.gCH4, 0.05, 0.15),
            (T.R("burn_co"), T.gCO, 0.125, 0.74),
            (T.R("burn_nh3"), T.gNH3, 0.15, 0.28),
        };
        _ozone = T.R("ozone_decay");
        int nr = f.L.Rooms.Count + 1; // 마지막 칸: 방 밖 (문 칸 · 통로가 아닌 곳)
        _boomJ = new double[nr]; _boomCell = new int[nr]; _boomCause = new int[nr]; _tickBurst = new double[nr];
        _roarKw = new double[nr]; _roarCell = new int[nr]; _roarCause = new int[nr];
    }

    private int RoomSlot(int cell) => F.L.Room[cell] >= 0 ? F.L.Room[cell] : _boomJ.Length - 1;

    /// <summary>
    /// 소화기를 이 칸에 뿜는다 (② (바) — ⑧이 요청): 물은 바닥 저장으로 (겉면을 식혀 끈다 — <see cref="Quench"/>) ·
    /// 이산화탄소는 −78℃(195K) 기체로 (산소 몰분율을 한계 산소 지수 아래로 밀어내 끈다 — 식히지는 못한다 · 남는 것이 없다).
    /// </summary>
    public void Douse(int cell, Extinguishant kind, double mol)
    {
        if (cell < 0 || cell >= Cells || mol <= 0) return;
        if (kind == Extinguishant.CO2) { F.AddGas(cell, T.gCO2, mol, 195); _jetNext[cell] += mol / Time.SecondsPerTick; }
        else F.AddStore(cell, T.sWater, mol);
    }

    /// <summary>불붙는 기체가 이 칸에서 새어 나왔다 (⑥이 요청) — 그 방 칸 모두에 원인을 단다 (기체가 방에 고루 퍼진다).</summary>
    public void MarkGas(int cell, int cause)
    {
        if (cause == 0 || cell < 0 || cell >= Cells) return;
        if (F.L.Room[cell] is int r && r >= 0) { foreach (int c in F.L.Rooms[r].Cells) GasCause[c] = cause; }
        else GasCause[cell] = cause;
    }

    /// <summary>불씨 (전기 불꽃 · 성냥 · 과열): 그 칸 겉면에 열을 넣고, 다음 틱에 연소 범위의 기체를 태운다.</summary>
    public void Ignite(int cell, double joules, int cause)
    {
        F.AddSurfaceHeat(cell, joules);
        _spark[cell] = 1;
        _sparkCause[cell] = cause;
    }

    public bool IsBurning(int cell) => Burning[cell] != 0;

    /// <summary>⑧의 요청: 방화 담요로 그 칸의 불을 덮는다 — 작은 불(열 방출 150kW 아래)만 꺼진다 (큰 불은 담요 둘레로 번진다 — false).</summary>
    public bool Blanket(Sim sim, int cell, int cause)
    {
        if (cell < 0 || cell >= Cells || Burning[cell] == 0 || Hrr[cell] > BlanketMaxKw) return false;
        Smother[cell] = SmotherTicks;
        if (cause != 0) FireCause[cell] = sim.Causes.Add(CauseKind.Person, "담요에 덮여 불이 숨을 못 쉰다", sim.Tick, cell, cause);
        Out(sim, cell);
        return true;
    }

    /// <summary>이 칸의 탈 것이 그 쪽 이웃의 탈 것과 이어져 있다 (전선 묶음 — ⑥이 요청).</summary>
    public void LinkFuel(int cell, byte dirs) { if (cell >= 0 && cell < Cells) FuelLink[cell] |= dirs; }

    /// <summary>배터리 셀이 찌그러졌다 (④ 파편 · 폭발이 배터리를 부쉈다 — 속 합선): 그 칸 셀이 달아오른다 → 열폭주.</summary>
    public void Abuse(int cell, int cause)
    {
        HeatCause[cell] = cause;
        F.Tf[cell] = Math.Max(F.Tf[cell], 440);
    }

    /// <summary>그 칸이 뜨거운 까닭: 타는 불 · 아직 안 붙은 불씨 · 달군 원인 (없으면 0).</summary>
    public int HeatCauseAt(int cell) => FireCause[cell] != 0 ? FireCause[cell] : _spark[cell] != 0 && _sparkCause[cell] != 0 ? _sparkCause[cell] : HeatCause[cell];
    public int BurningCount { get; private set; }

    public void Step(Sim sim, double dt)
    {
        F.RebuildNow(); // 장보다 먼저 돈다 — 구역이 바뀌었으면 지금 짠다
        Array.Clear(Hrr);
        Array.Clear(_sparkNext);
        _stamp++;
        int burning = 0;
        Span<int> nb = stackalloc int[4];
        for (int i = 0; i < Cells; i++)
        {
            if (F.RegionOf.Length <= i || F.RegionOf[i] < 0) { if (Burning[i] != 0) Out(sim, i); continue; }
            GasFire(sim, i, dt, nb);
            Quench(i);
            Pyro(sim, i, dt);
            Hydrolyze(sim, i, dt);
            if (Smother[i] != 0) { Smother[i]--; if (Burning[i] != 0) Out(sim, i); continue; }   // 방화 담요가 덮고 있다 — 타지도 붙지도 않는다 (겉면은 그대로 식는다)
            if (Burning[i] == 0 && F.Tf[i] < _coldK) continue;   // 가장 낮은 발화 온도보다 차가운 칸은 붙을 것이 없다 (산소 · 기압을 셀 까닭이 없다 — 대부분의 칸)
            if (!HasFuel(i)) { if (Burning[i] != 0) { HandOff(sim, i, nb); Out(sim, i); } continue; }
            double tf = F.Tf[i], x = O2Frac(i), pf = PressureFactor(i);
            if (Burning[i] == 0)
            {
                // 발화: 겉면이 그 연료의 발화 온도(산소가 짙을수록 낮다)를 넘었고 · 산소가 불꽃을 먹일 공기의 한계 산소 지수보다 짙다 (꺼질 불이면 붙지도 않는다)
                double feedK = 0, feedX = 0, jet = 0; bool fed = false;
                foreach (var fu in _fuels)
                {
                    if (F.Stored(fu.store, i) < 1e-3 || tf < IgniteK(fu.full.Def.MinK, x)) continue;
                    if (!fed) { (feedK, feedX) = FeedAir(i); jet = JetAt(i); fed = true; }
                    if (CanFlame(fu.full.Def.Loi, x, tf, pf, feedK, feedX, jet, Draw(i)))
                    {
                        Light(sim, i, nb);
                        break;
                    }
                }
                if (Burning[i] == 0) continue;
            }
            if ((tf < SustainK || !Flames(i, x, tf, pf)) && !FrontAlive(i)) { Out(sim, i); continue; }
            Burn(sim, i, dt, x, pf, nb);
            if (Burning[i] != 0) burning++;
        }
        BurningCount = burning;
        Sounds(sim);
        (_spark, _sparkNext) = (_sparkNext, _spark);
        (_jet, _jetNext) = (_jetNext, _jet); Array.Clear(_jetNext);
        (_sparkCause, _sparkCauseNext) = (_sparkCauseNext, _sparkCause);
        if (sim.Tick % 25 == 0) Slow(25 * dt);
    }

    private bool HasFuel(int i)
    {
        foreach (var fu in _fuels) if (F.Stored(fu.store, i) > 1e-3) return true;
        return false;
    }

    // ── ② 사고 화학 (다): 산소와 불 — 몰분율 · 한계 산소 지수(LOI) · 기압 (docs/ENGINE.md) ──

    /// <summary>그 칸 공기의 산소 몰분율.</summary>
    public double O2Frac(int i) { double p = F.Pressure(i); return p > 1 ? F.Partial(T.gO2, i) / p : 0; }

    /// <summary>기압 몫: 8kPa 아래 0 (불꽃이 못 선다) · 60kPa 넘으면 1 — 같은 몰분율이면 낮은 기압에서도 거의 같이 탄다 (NASA 시험).</summary>
    public double PressureFactor(int i) => Math.Sqrt(Math.Clamp((F.Pressure(i) - QuenchKPa) / 52, 0, 1));

    /// <summary>온도 t(K)에서의 한계 산소 지수 — 1K에 0.018%p씩 낮아진다 (Fenimore · 예열된 공기에선 낮은 산소에서도 탄다) · 많아야 절반까지 (뜨거운 방의 불꽃 한계 8~12%). 꺼짐은 둘레 공기 · 빠르기는 겉면 온도로 부른다.</summary>
    public static double LoiAt(double loi, double t) => Math.Max(loi * 0.5, loi - 0.00018 * (t - 298));

    /// <summary>탈 것 하나의 빠르기 몫 (1기압 공기의 뜨거운 고분자 ≈ 1.1 · 많아야 2.5) — 산소가 그 한계를 넘는 만큼.</summary>
    public static double Vigor(double loi, double x, double t, double pf) => pf * Math.Clamp((x - LoiAt(loi, t)) / LoiSpan, 0, 2.5);

    /// <summary>산소가 짙을수록 낮아지는 발화 온도 (100% 산소면 150K 낮다 — 아폴로 1호).</summary>
    public static double IgniteK(double minK, double x) => minK - 150 * Math.Clamp((x - 0.21) / 0.79, 0, 1);

    /// <summary>
    /// 그 칸에 아직 불꽃이 설 탈 것이 있나 — 불꽃을 먹이는 공기(<see cref="FeedAir"/>)의 산소가 그 공기 온도의 한계 산소 지수보다 짙어야 한다 (모두 아래면 스스로 꺼진다).
    /// 꺼지는 한계는 둘레 공기의 예열로 낮아진다 (차가운 방 13~15% · 뜨거운 방 10% 언저리 — 구획 화재 실험 · 이산화탄소 설계 농도 34%) · 타는 빠르기는 겉면의 되먹임으로 (<see cref="Vigor"/>).
    /// </summary>
    private bool Flames(int i, double x, double tf, double pf)
    {
        var (feedK, feedX) = FeedAir(i);
        double jet = JetAt(i), draw = Draw(i);
        foreach (var fu in _fuels) if (F.Stored(fu.store, i) > 1e-3 && CanFlame(fu.full.Def.Loi, x, tf, pf, feedK, feedX, jet, draw)) return true;
        return false;
    }

    /// <summary>
    /// 탈 것 하나에 불꽃이 설 수 있나 (붙을 때 · 버틸 때 같은 판정): 타는 빠르기가 있고 · 불꽃이 들이쉬는 공기의 산소가 그 꺼짐 한계보다 짙다.
    /// 소화기 분사(jet 몰/초)는 들이쉬는 공기에 섞인다 — 들이쉬는 공기는 불이 셀수록 많다 (세기 1에 초당 25몰 ≈ 130kW): 작은 불은 꺼지고 큰 불은 못 끈다.
    /// </summary>
    private static bool CanFlame(double loi, double x, double tf, double pf, double feedK, double feedX, double jet, double draw = 1)
    {
        double v = Vigor(loi, x, tf, pf);
        if (v <= 0) return false;
        double entrain = 25 * Math.Max(0.2, v) * (draw >= 1 ? 1 : Math.Max(0.15, draw));   // 무중력 불꽃은 들이쉬는 공기가 적다 — 소화기 분사가 더 듣는다
        return feedX * entrain / (entrain + jet) > FlameLimit(loi, feedK, tf);
    }

    /// <summary>그 칸과 이웃에 지난 틱 뿜은 소화기 분사 (몰/초).</summary>
    private double JetAt(int i)
    {
        double j = _jet[i];
        Span<int> nb = stackalloc int[4];
        F.L.Neighbors(i, nb);
        foreach (int k in nb) if (k >= 0) j += _jet[k];
        return j;
    }

    /// <summary>
    /// 불꽃이 꺼지는 산소 한계: 불꽃을 먹이는 공기 온도의 한계 산소 지수 − 큰 불의 되먹임 몫 (겉면 500K → 800K에 0 → 3.5%p — 뜨거운 겉면이 복사로 열분해를 돕는다:
    /// 작은 시편의 LOI보다 방만 한 불은 옅은 산소에서도 버틴다 · 구획 화재의 불꽃 소멸 12~15%) · 많아야 LOI의 절반까지.
    /// </summary>
    public static double FlameLimit(double loi, double feedK, double tf) => Math.Max(loi * 0.5, LoiAt(loi, feedK) - 0.035 * Math.Clamp((tf - 500) / 300, 0, 1));

    /// <summary>
    /// 불꽃을 먹이는 공기 — 그 방의 불이 아닌 칸 공기의 몰 가중 평균 온도(K) · 산소 몰분율 (불기둥은 방 공기를 끌어들인다 — 구획 화재의 고루 섞인 아래층:
    /// 닫힌 방이면 방 전체가 옅어져 꺼지고 · 뜨거운 방이면 한계가 낮아진다). 장에 대류가 없어 불 곁 칸만 옅어지는 것을 불꽃의 공기로 읽지 않게.
    /// 방 밖 칸(문 · 통로)은 불이 아닌 이웃 · 그것도 없으면 그 칸.
    /// </summary>
    public (double k, double x) FeedAir(int i)
    {
        float g = F.Grav[i];
        if (g >= 1) return RoomFeed(i);
        // 무중력: 부력이 방 공기를 끌어들이지 않는다 — 이웃 칸 공기(확산)를 먹고 · 공조 바람이 닿는 만큼 방 공기를 (우주 정거장 촛불 실험)
        var local = LocalFeed(i);
        int r = F.L.Room[i];
        if (r < 0) return local;
        var room = RoomFeed(i);
        double w = Draw(i);
        return (w * room.k + (1 - w) * local.k, w * room.x + (1 - w) * local.x);
    }

    /// <summary>불꽃이 방 공기를 끌어들이는 몫 (1g 1 · 0g면 공조 바람만큼): √g (부력 흐름 빠르기) 와 공조 바람 가운데 큰 쪽.</summary>
    private double Draw(int i) { float g = F.Grav[i]; return g >= 1 ? 1 : Math.Max(Math.Sqrt(Math.Max(0, g)), F.Fan(i)); }

    private (double k, double x) RoomFeed(int i)
    {
        int r = F.L.Room[i];
        if (r >= 0)
        {
            // 한 틱에 방마다 한 번 (처음 물을 때의 공기 — 불이 여럿이어도 방 칸을 다시 돌지 않는다)
            if (_feedStamp.Length != F.L.Rooms.Count) { _feedStamp = new int[F.L.Rooms.Count]; _feedRoom = new (double, double)[F.L.Rooms.Count]; }
            if (_feedStamp[r] == _stamp) return _feedRoom[r];
            _feedStamp[r] = _stamp;
            double tr = 0, xr = 0, wr = 0;
            foreach (int j in F.L.Rooms[r].Cells)
            {
                if (Burning[j] != 0 || F.RegionOf.Length <= j || F.RegionOf[j] < 0) continue;
                double m = F.Moles(j);
                if (m < 1) continue;
                tr += F.Temp(j) * m; xr += O2Frac(j) * m; wr += m;
            }
            if (wr > 0) return _feedRoom[r] = (tr / wr, xr / wr);
            _feedStamp[r] = 0;
        }
        return LocalFeed(i);
    }

    private (double k, double x) LocalFeed(int i)
    {
        double t = 0, x = 0, w = 0;
        {
            Span<int> nb = stackalloc int[4];
            F.L.Neighbors(i, nb);
            foreach (int j in nb)
            {
                if (j < 0 || Burning[j] != 0 || F.RegionOf.Length <= j || F.RegionOf[j] < 0 || F.Moles(j) < 1) continue;
                t += F.Temp(j); x += O2Frac(j); w += 1;
            }
        }
        return w > 0 ? (t / w, x / w) : (F.Temp(i), O2Frac(i));
    }

    private int _stamp = 1;
    private int[] _feedStamp = Array.Empty<int>();
    private (double k, double x)[] _feedRoom = Array.Empty<(double, double)>();

    /// <summary>불이 붙는다 — 원인: 불씨 > 불 붙은 이웃 > 뜨거운 공기.</summary>
    private void Light(Sim sim, int i, Span<int> nb)
    {
        Burning[i] = 1;
        Ignitions++;
        int cause = _spark[i] != 0 ? _sparkCause[i] : 0;
        bool spread = false;
        if (cause == 0)
        {
            F.L.Neighbors(i, nb);
            foreach (int j in nb)
                if (j >= 0 && Burning[j] != 0) { cause = sim.Causes.Add(CauseKind.Fire, "옆 칸으로 번졌다", sim.Tick, i, FireCause[j]); spread = true; break; }
        }
        if (cause == 0 && HeatCause[i] == 0)
        {
            // 달군 원인이 따로 없으면 남은 불의 열이다 — 꺼졌던 그 칸이 다시 · 꺼진 옆 칸의 열로 (불꽃이 잠깐 서지 못한 사이 — 원인 사슬이 끊기지 않게)
            if (FireCause[i] != 0) cause = sim.Causes.Add(CauseKind.Fire, "남은 열에 다시 불이 붙었다", sim.Tick, i, FireCause[i]);
            else
            {
                F.L.Neighbors(i, nb);
                foreach (int j in nb)
                    if (j >= 0 && FireCause[j] != 0) { cause = sim.Causes.Add(CauseKind.Fire, "옆 불의 열로 번졌다", sim.Tick, i, FireCause[j]); spread = true; break; }
            }
        }
        if (cause == 0) cause = sim.Causes.Add(CauseKind.Heat, "뜨거워져 불이 붙었다", sim.Tick, i, HeatCause[i]);
        FireCause[i] = cause;
        sim.Facts.Emit(sim.Tick, spread ? FactKind.FireSpread : FactKind.Ignition, i, (float)F.Temp(i), cause);
    }

    private void Out(Sim sim, int i)
    {
        Burning[i] = 0;
        sim.Facts.Emit(sim.Tick, FactKind.FireOut, i, 0, FireCause[i]);
    }

    /// <summary>한 칸이 dt초 동안 탄다.</summary>
    private void Burn(Sim sim, int i, double dt, double x, double pf, Span<int> nb)
    {
        double gMax = 0;
        // 산소가 모자랄수록 그을며 탄다 (불완전 연소) — 늘 조금은 그을음 (몰분율 15.5%부터 · 8.2%면 다 — 옛 분압 모형이 불 칸(약 110kPa)에서 보이던 것과 같게)
        double incomplete = Math.Clamp(1 - (x - 0.082) / 0.073, 0, 1) * 0.7 + 0.05;
        double heat = 0;
        double o2Budget = F.Gas(T.gO2, i) * 0.5; // 한 틱에 그 칸 산소의 절반까지
        double draw = Draw(i), flow = draw >= 1 ? 1 : 0.3 + 0.7 * draw;   // 무중력 불꽃은 산소가 확산으로만 와 느리게 탄다 (공조 바람이 닿으면 덜)
        foreach (var fu in _fuels)
        {
            double fuel = F.Stored(fu.store, i);
            if (fuel < 1e-6 || F.Tf[i] < fu.full.Def.MinK * 0.8) continue; // 겉면이 식은 연료는 타지 않는다
            double kg = fuel * T.Substances[T.Stores[fu.store].Index].MolarMass;
            double ff = Math.Min(1, kg / fu.kgFull) * 0.8 + 0.2 * Math.Min(1, kg * 10 / fu.kgFull);
            double g = Vigor(fu.full.Def.Loi, x, F.Tf[i], pf);   // 탈 것마다 제 한계 산소 지수로 (공기 중 피복은 고분자의 0.7배)
            if (g <= 0) continue;
            gMax = Math.Max(gMax, g);
            double dhFull = -fu.full.DH298; // J/반응몰
            double ext = CellHrr * fu.full.Def.Rate * g * ff / dhFull * dt * flow; // 반응 몰
            if (ext <= 0) continue;
            // 갈래: 완전 · 일산화탄소 · 그을음
            double share = 1 - incomplete;
            heat += React(sim, i, fu.full, ext * share, ref o2Budget);
            if (fu.co != null) heat += React(sim, i, fu.co, ext * incomplete * fu.coShare * (fu.full.In[0].Nu / fu.co.In[0].Nu), ref o2Budget);
            if (fu.soot != null) heat += React(sim, i, fu.soot, ext * incomplete * fu.sootShare * (fu.full.In[0].Nu / fu.soot.In[0].Nu), ref o2Budget);
            if (fu.tox != null) heat += React(sim, i, fu.tox, ext * incomplete * fu.toxShare * (fu.full.In[0].Nu / fu.tox.In[0].Nu), ref o2Budget);
        }
        if (FuelLink[i] != 0) heat += CreepFront(sim, i, dt, ref o2Budget, nb);
        if (heat <= 0) return;
        Hrr[i] = (float)(heat / dt / 1000);
        // 열 나누기 (보존):
        //   ① 불꽃이 제 겉면을 불꽃 온도 가까이 달군다 (산소가 많을수록 뜨겁다 · 많아야 40%)
        //   ② 이웃 겉면 · 벽으로 불꽃 복사 εσF(T불꽃⁴−Tn⁴) (보는 각 F=0.3 — 1m 곁 가구가 받는 15~20kW/m² · 많아야 35% · 벽은 고체로 — 불 곁의 벽이 달아오른다)
        //   ③ 나머지: 기체 80% · 바닥 20%
        double left = heat;
        double tFlame = 750 + 550 * Math.Min(1, gMax);
        double own = Math.Clamp((tFlame - F.Tf[i]) * Field.SurfaceC, 0, heat * 0.4);
        F.AddSurfaceHeat(i, own); left -= own;
        F.L.Neighbors(i, nb);
        double tf = Math.Max(F.Tf[i], tFlame * Math.Min(1, Hrr[i] / 150.0)), tf4 = tf * tf * tf * tf, radTotal = 0;
        Span<double> rad = stackalloc double[4];
        for (int k = 0; k < 4; k++)
        {
            int j = nb[k];
            rad[k] = 0;
            if (j < 0) continue;
            bool wall = F.L.Kind[j] == CellKind.Wall;
            if (!wall && !F.HasSurface(j)) continue;
            double tn = wall ? F.Ts[j] : F.Tf[j];
            rad[k] = Math.Max(0, 0.9 * Field.Sigma * 0.3 * (tf4 - tn * tn * tn * tn)) * dt;
            radTotal += rad[k];
        }
        double scale = radTotal > heat * 0.35 ? heat * 0.35 / radTotal : 1;
        for (int k = 0; k < 4; k++)
            if (rad[k] > 0)
            {
                if (F.L.Kind[nb[k]] == CellKind.Wall) F.AddSolidHeat(nb[k], rad[k] * scale); else F.AddSurfaceHeat(nb[k], rad[k] * scale);
                left -= rad[k] * scale;
            }
        F.AddHeat(i, left * 0.8);
        F.AddSolidHeat(i, left * 0.2);
        // 상승 기류: 불꽃이 둘레 공기를 빨아들이고 뜨거운 연기를 내보낸다 (불이 셀수록 세게)
        double pull = 0.06 + 0.12 * Math.Min(1, Hrr[i] / 250.0);
        foreach (int j in nb)
            if (j >= 0 && F.RegionOf[j] == F.RegionOf[i]) F.Exchange(i, j, pull);
    }

    /// <summary>
    /// 이어진 탈 것을 따라 기는 불꽃 앞머리: 앞머리가 지나는 만큼 그 칸 고분자가 탄다 (빠르기 × 피복 양 — 앞머리의 불이 칸 전체의 느린 불보다 세다) ·
    /// 이웃으로 1m를 기면 그 칸 피복 일부가 앞머리에서 타 그 열로 겉면을 달군다 → 불이 붙는다 (보존 — 열은 그 피복이 낸 만큼).
    /// </summary>
    private double CreepFront(Sim sim, int i, double dt, ref double o2Budget, Span<int> nb)
    {
        if (Insul(i) < 1e-3) { HandOff(sim, i, nb); return 0; }
        double v0 = FrontSpeed(i);
        // 앞머리가 칸 길이(1m)를 지나는 동안 피복을 고르게 먹는다 (남은 것이 적어도 가닥 하나 몫의 빠르기 — 꼬리가 길게 끌리지 않게 ·
        // 다 타기 전에 앞머리가 옆 칸에 닿는다: 불꽃 길이만큼 — 0.8) · 피복은 PVC와 가소제(고분자) — 둘 다 같은 앞머리에서
        double heat = 0;
        if (v0 > 0)
            foreach (var (store, r, least) in new[] { (T.sPvc, _burnPvc, 0.07), (T.sPoly, _burnPoly, 0.17) })
            {
                double amt = F.Stored(store, i);
                if (amt > 1e-6) heat += React(sim, i, r, Math.Min(amt, Math.Max(amt, least) * v0 * dt * 0.8) / r.In[0].Nu, ref o2Budget);
            }
        for (int d = 0; d < 4; d++)
        {
            if ((FuelLink[i] & (1 << d)) == 0) continue;
            int j = d switch { 0 => i - F.L.W, 1 => i + 1, 2 => i + F.L.W, _ => i - 1 };
            if (j < 0 || j >= Cells || Burning[j] != 0 || Insul(j) < 1e-3 || F.RegionOf.Length <= j || F.RegionOf[j] < 0) continue;
            double v = v0 + (F.Tf[j] > 420 ? CreepMps * 0.5 : 0);
            if (v <= 0) continue;
            Creep[j] += (float)(v * dt);
            if (Creep[j] < 1) continue;
            Creep[j] = 0;
            if (PressureFactor(j) <= 0 || O2Frac(j) < LoiAt(_burnPvc.Def.Loi, F.Tf[j])) continue;
            Creeps++;
            Light(sim, j, nb);   // 앞머리가 닿았다 — 피복 위의 불꽃 (그 칸 바닥까지 달굴 것은 없다 · 산소가 옅으면 다음 틱에 꺼진다)
        }
        return heat;
    }

    /// <summary>
    /// ② (라) 배터리 셀의 안전 밸브가 열렸다 (⑥ 망이 요청 — 셀 묶음이 150℃를 넘었다): 그 칸 전해액 · 염의 frac를 끓여 갈라 뿜는다 —
    /// 4 탄산에틸렌 → 5.5CO₂ + 4H₂ + 2CH₄ + CO + 3.5숯 (뿜는 기체: 이산화탄소 44% · 수소 32% · 메탄 16% · 일산화탄소 8% — 실측 범위) · LiPF₆ → LiF + PF₅ (공기의 물과 만나 플루오린화수소).
    /// 갈라지며 드는 열(흡열 — 실제는 음극의 리튬이 낸다)은 <paramref name="heatJ"/>로 돌려주고 부른 쪽이 맡는다 (배터리: 저장 에너지 · 셀 묶음 열에서). 뿜은 기체 몰을 돌려준다.
    /// </summary>
    public double Vent(Sim sim, int cell, double frac, out double heatJ)
    {
        heatJ = 0;
        if (cell < 0 || cell >= Cells || frac <= 0) return 0;
        double budget = double.MaxValue;
        double ec = F.Stored(T.sEC, cell) * Math.Min(1, frac) / _ventEc.In[0].Nu, salt = F.Stored(T.sLiPF6, cell) * Math.Min(1, frac) / _ventSalt.In[0].Nu;
        heatJ = React(sim, cell, _ventEc, ec, ref budget) + React(sim, cell, _ventSalt, salt, ref budget);
        return ec * GasOut(_ventEc) + salt * GasOut(_ventSalt);
    }

    private double GasOut(Reaction r) { double n = 0; foreach (var x in r.Out) if (T.Substances[x.S].Gas >= 0) n += x.Nu; return n; }

    /// <summary>PF₅는 공기의 물과 곧 만난다 → POF₃ + 2HF (초당 5% — 습한 배 공기에서 몇십 초).</summary>
    private void Hydrolyze(Sim sim, int i, double dt)
    {
        double pf5 = F.Gas(T.gPF5, i);
        if (pf5 < 1e-9) return;
        double budget = double.MaxValue;
        F.AddHeat(i, React(sim, i, _pf5Hydro, pf5 * (1 - Math.Exp(-_pf5Hydro.Def.Rate * dt)), ref budget));
    }

    /// <summary>
    /// 칸의 피복이 다 탔다 — 1m가 다 탔으면 앞머리는 이미 그 칸 끝에 닿아 있다: 그쪽으로 기던 이웃(쌓인 거리가 있는 칸)에 불이 붙는다 ·
    /// 그 이웃이 불꽃을 세울 수 있을 때만 (닫힌 문 · 옅은 산소는 막는다). 짙은 산소에서 칸이 앞머리보다 빨리 타 버려 기던 거리가 사라지던 빈틈.
    /// </summary>
    private void HandOff(Sim sim, int i, Span<int> nb)
    {
        if (FuelLink[i] == 0) return;
        for (int d = 0; d < 4; d++)
        {
            if ((FuelLink[i] & (1 << d)) == 0) continue;
            int j = d switch { 0 => i - F.L.W, 1 => i + 1, 2 => i + F.L.W, _ => i - 1 };
            if (j < 0 || j >= Cells || Burning[j] != 0 || Creep[j] <= 0 || Insul(j) < 1e-3 || F.RegionOf.Length <= j || F.RegionOf[j] < 0) continue;
            Creep[j] = 0;
            if (PressureFactor(j) <= 0 || O2Frac(j) < LoiAt(_burnPvc.Def.Loi, F.Tf[j])) continue;
            Creeps++;
            Light(sim, j, nb);
        }
    }

    /// <summary>이어진 피복 위 앞머리의 빠르기 (m/s): 산소 몰분율이 피복의 한계 산소 지수(25%) 아래 0 · 33%에서 CreepMps · 많아야 1.5배 (기압 몫을 곱한다).</summary>
    private double FrontSpeed(int i) => CreepMps * PressureFactor(i) * Math.Clamp((O2Frac(i) - _burnPvc.Def.Loi) / 0.08, 0, 1.5);

    /// <summary>이어진 피복 위 불꽃 앞머리가 살아 있나 — 그 칸 바닥이 식어도 피복 위 불꽃은 탄다 (산소가 짙을 때).</summary>
    private bool FrontAlive(int i) => FuelLink[i] != 0 && Insul(i) > 1e-3 && FrontSpeed(i) > 0;

    /// <summary>칸의 피복 (PVC + 가소제 몫 고분자 — 몰).</summary>
    private double Insul(int i) => F.Stored(T.sPvc, i) + F.Stored(T.sPoly, i);

    /// <summary>
    /// 달궈진 피복에서 염화수소가 빠진다 (탈염화수소 — 불꽃 없이 250℃부터: C₂H₃Cl → HCl + 2C + H₂ · 아레니우스) — 타기 전에 매캐하다 ·
    /// 겉면이 500K 넘는 칸만 (뜨거운 장치 곁 · 불 곁 · 합선 아크).
    /// </summary>
    private void Pyro(Sim sim, int i, double dt)
    {
        double tf = F.Tf[i];
        if (tf < _pvcPyro.Def.MinK) return;
        double pvc = F.Stored(T.sPvc, i);
        if (pvc < 1e-6) return;
        double k = _pvcPyro.Def.Rate * Math.Exp(-PyroEa / 8.314 * (1 / tf - 1 / 573.0));
        double ext = pvc * (1 - Math.Exp(-k * dt));
        double budget = double.MaxValue;
        double q = React(sim, i, _pvcPyro, ext, ref budget);
        F.AddSurfaceHeat(i, q);
        PyroHcl += ext;
    }

    /// <summary>물이 고인 칸: 뜨거운 겉면이 물을 끓여 식는다 (증발열) — 불을 끄는 길.</summary>
    private void Quench(int i)
    {
        if (F.Tf[i] <= 380 || !F.HasSurface(i)) return;
        double water = F.Stored(T.sWater, i);
        if (water < 1) return;
        var vap = T.Gases[T.gH2O];
        var liq = T.Substances[T.Stores[T.sWater].Index];
        double perMol = (vap.Hf - liq.Hf) * 1000 + vap.C * (373.15 - 298.15);
        double n = Math.Min(water, (F.Tf[i] - 373.15) * Field.SurfaceC / perMol);
        if (n <= 0) return;
        F.TakeStore(i, T.sWater, n);
        F.AddGas(i, T.gH2O, n, 373.15);
        F.Tf[i] -= n * perMol / Field.SurfaceC;
    }

    /// <summary>
    /// 반응을 칸에서 일으킨다: 반응물(기체 · 바닥 저장)을 빼고 생성물을 넣는다 — 반응열을 돌려준다 (장에 넣는 것은 부르는 쪽).
    /// 기체 반응물은 그 칸 온도의 현열과 함께 빠지고, 기체 생성물은 그 온도의 현열과 함께 들어간다 (장부를 맞추는 반응열은 Reaction.Heat).
    /// </summary>
    private double React(Sim sim, int i, Reaction r, double extent, ref double o2Budget)
    {
        if (extent <= 0) return 0;
        foreach (var x in r.In)
        {
            var s = T.Substances[x.S];
            double have = s.Gas >= 0 ? F.Gas(s.Gas, i) : s.Store >= 0 ? F.Stored(s.Store, i) : 0;
            if (s.Gas == T.gO2) have = Math.Min(have, o2Budget);
            extent = Math.Min(extent, have / x.Nu);
        }
        if (extent <= 1e-12) return 0;
        double tg = F.Temp(i);
        double q = r.Heat(T, tg) * extent;
        foreach (var x in r.In)
        {
            var s = T.Substances[x.S];
            double n = x.Nu * extent;
            if (s.Gas >= 0) { F.RemoveGas(i, s.Gas, n); if (s.Gas == T.gO2) { o2Budget -= n; sim.Book.Add(Res.O2, Flow.Burned, -n); } }
            else { F.TakeStore(i, s.Store, n); if (s.Store == T.sFood) sim.Book.Add(Res.Food, Flow.Burned, -n); }
        }
        foreach (var x in r.Out)
        {
            var s = T.Substances[x.S];
            double n = x.Nu * extent;
            if (s.Gas >= 0) F.AddGas(i, s.Gas, n, tg);
            else if (s.Store >= 0) F.AddStore(i, s.Store, n);
        }
        return q;
    }

    /// <summary>기체 불: 연소 범위 안 + (불씨 · 불 붙은 이웃 · 자연 발화 온도) → 한 틱에 대부분.</summary>
    private void GasFire(Sim sim, int i, double dt, Span<int> nb)
    {
        double n = 0;
        foreach (var gf in _gasFires) n += F.Gas(gf.gas, i);
        if (n < 1e-6) { GasCause[i] = 0; return; }
        double total = F.Moles(i);
        if (total < 1e-6) return;
        double o2 = F.Gas(T.gO2, i) / total;
        if (o2 < 0.05) return;
        double tg = F.Temp(i);
        bool spark = _spark[i] != 0 || Burning[i] != 0;
        foreach (var gf in _gasFires)
        {
            double x = F.Gas(gf.gas, i) / total;
            if (x < gf.lfl || x > gf.ufl) continue;
            if (!spark && tg < gf.r.Def.MinK) continue;
            double budget = F.Gas(T.gO2, i) * 0.9;
            double fuelNu = gf.r.In.First(t => T.Substances[t.S].Gas == gf.gas).Nu;
            double heat = React(sim, i, gf.r, F.Gas(gf.gas, i) * 0.8 / fuelNu * gf.r.Def.Rate, ref budget);
            if (heat <= 0) continue;
            F.AddHeat(i, heat);
            Hrr[i] += (float)(heat / dt / 1000);
            GasBursts++;
            int cause = _spark[i] != 0 ? _sparkCause[i] : FireCause[i];
            int gc = GasCause[i];
            if (gc != 0 && gc != cause && (cause == 0 || sim.Causes[cause].Parent != gc && sim.Causes[cause].Parent2 != gc))
                // 새어 나온 기체 — 첫 부모는 샘 (사슬의 뿌리 · 씨앗의 열매) · 둘째 부모는 불씨 (② (마)). 불꽃 앞면이 옆 칸으로 옮겨도 이 마디를 그대로 잇는다
                cause = sim.Causes.Add(CauseKind.Reaction, $"새어 나온 {Ko.I(T.Substances[T.Gases[gf.gas].Index].Name)} 한꺼번에 탔다", sim.Tick, i, gc, cause);
            else if (cause == 0) cause = sim.Causes.Add(CauseKind.Reaction, $"{Ko.I(T.Substances[T.Gases[gf.gas].Index].Name)} 한꺼번에 탔다", sim.Tick, i);
            sim.Facts.Emit(sim.Tick, FactKind.Ignition, i, (float)(heat / 1000), cause, other: gf.gas);
            int slot = RoomSlot(i);
            if (_boomJ[slot] <= 0 && _tickBurst[slot] <= 0) { _boomCell[slot] = i; _boomCause[slot] = cause; } // 쾅의 자리 · 원인: 그 불이 처음 터진 칸
            _tickBurst[slot] += heat;
            // 불꽃 앞면: 이웃이 다음 틱의 불씨
            F.L.Neighbors(i, nb);
            foreach (int j in nb) if (j >= 0) { _sparkNext[j] = 1; _sparkCauseNext[j] = cause; }
        }
    }

    /// <summary>
    /// 불과 폭발의 소리 (③ 소리 장에 요청): 타는 방마다 5틱에 한 번 우르릉 — 60 + 10·log(그 방 불의 kW) (의자 하나 250kW 84dB · 어림) ·
    /// 기체 불은 그 방에서 그친 틱에 쾅 하나 — 85 + 10·log(탄 열 kJ · 많아야 140dB — 충돌과 같은 잣대 · 어림).
    /// </summary>
    private void Sounds(Sim sim)
    {
        var snd = sim.Find<Sound>();
        int nr = _boomJ.Length;
        if (snd != null)
            for (int r = 0; r < nr; r++)
                if (_tickBurst[r] <= 0 && _boomJ[r] > 0)
                {
                    float db = (float)Math.Min(140, 85 + 10 * Math.Log10(Math.Max(1, _boomJ[r] / 1000)));
                    snd.Emit(sim, _boomCell[r], db, SoundKind.Bang, _boomCause[r]);
                    _boomJ[r] = 0;
                }
        for (int r = 0; r < nr; r++) { _boomJ[r] += _tickBurst[r]; _tickBurst[r] = 0; }
        if (snd == null || BurningCount == 0 || sim.Tick % 5 != 0) return;
        Array.Clear(_roarKw);
        for (int i = 0; i < Cells; i++)
        {
            if (Burning[i] == 0 || Hrr[i] <= 0) continue;
            int r = RoomSlot(i);
            if (_roarKw[r] <= 0 || Hrr[i] > Hrr[_roarCell[r]]) { _roarCell[r] = i; _roarCause[r] = FireCause[i]; }
            _roarKw[r] += Hrr[i];
        }
        for (int r = 0; r < nr; r++)
            if (_roarKw[r] > 0) snd.Emit(sim, _roarCell[r], (float)(60 + 10 * Math.Log10(Math.Max(1, _roarKw[r]))), SoundKind.Roar, _roarCause[r]);
    }

    /// <summary>느린 반응 (25틱마다): 오존이 산소로 돌아간다.</summary>
    private void Slow(double dt)
    {
        int g = T.gO3;
        double k = 1 - Math.Exp(-_ozone.Def.Rate * dt);
        double budget = double.MaxValue;
        for (int i = 0; i < Cells; i++)
        {
            double o3 = F.Gas(g, i);
            if (o3 < 1e-9) continue;
            double tg = F.Temp(i);
            double ext = o3 * k * (tg > 350 ? 10 : 1) / 2;
            double q = _ozone.Heat(T, tg) * ext;
            F.RemoveGas(i, g, 2 * ext);
            F.AddGas(i, T.gO2, 3 * ext, tg);
            F.AddHeat(i, q);
            _ = budget;
        }
    }

    public void Save(StateWriter w)
    {
        w.Arr(Burning); w.Arr(FireCause); w.Arr(HeatCause); w.Arr(Hrr); w.Arr(_spark); w.Arr(_sparkCause);
        w.I64(Ignitions); w.I64(GasBursts);
        w.Arr(FuelLink); w.Arr(Creep); w.I64(Creeps); w.F64(PyroHcl);
        w.Tag("폭발 소리"); w.Arr(_boomJ); w.Arr(_boomCell); w.Arr(_boomCause);
        w.Arr(GasCause); w.Arr(_jet); w.Arr(_jetNext);
        w.Tag("담요"); w.Arr(Smother);
    }

    public void Load(StateReader r)
    {
        r.ArrInto(Burning); r.ArrInto(FireCause); r.ArrInto(HeatCause); r.ArrInto(Hrr); r.ArrInto(_spark); r.ArrInto(_sparkCause);
        Ignitions = r.I64(); GasBursts = r.I64();
        r.ArrInto(FuelLink); r.ArrInto(Creep); Creeps = r.I64(); PyroHcl = r.F64();
        r.Tag("폭발 소리"); r.ArrInto(_boomJ); r.ArrInto(_boomCell); r.ArrInto(_boomCause);
        r.ArrInto(GasCause); r.ArrInto(_jet); r.ArrInto(_jetNext);
        r.Tag("담요"); r.ArrInto(Smother);
    }
}
