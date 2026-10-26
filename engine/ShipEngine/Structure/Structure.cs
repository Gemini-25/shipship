namespace ShipEngine;

/// <summary>
/// ④ 구조 · 물체 — 벽 · 외판 · 문의 하중과 부서짐 (docs/ENGINE.md §4 ④).
/// <para>
/// <b>하중</b>: 벽 칸 양쪽(좌우 · 위아래)의 압력 차 ΔP를 견디는 힘(정격)과 견준다.
/// 정격 = 종류(외판 400kPa · 칸막이 160kPa · 보통 문 40kPa · 차압 문 300kPa) × √(재질 강도/강철) × 열 약화 × (1−피로) × (1−부식).
/// 부하가 피로 한계(재질)를 넘으면 피로가 쌓인다. 0.85를 넘으면 금(작은 틈), 1을 넘으면 구멍, 1.6을 넘으면 벽이 통째로 나간다.
/// </para>
/// <para>
/// <b>충돌</b>(⑤ 바깥 세계 · 폭발 · 날아온 물건): 에너지가 관통 에너지(재질 kJ/mm × 두께)를 넘으면 구멍을 내고,
/// 남은 에너지는 그 방향 다음 칸으로 — 공기 칸이면 절반은 열(불덩이), 절반은 계속 날아가 다음 벽을 친다.
/// </para>
/// <para>
/// <b>문</b>: 위층(사람 · 장치 · 주컴퓨터)은 목표만 정한다 (<see cref="RequestDoor"/>). 문은 초당 0.5만큼 움직이고,
/// 압력 차를 거슬러 열지 못한다 (손으로 2kPa · 전동 30kPa) · 끼인 문은 움직이지 않는다. 문이 정격을 넘는 압력 차를 받으면 날아가거나 끼인다.
/// 닫히는 문짝 판은 문 칸에 든 물체에서 멈춘다 (목표는 그대로 — 10kg 넘으면 끼고 · 가벼우면 밀어낸다 · <see cref="DoorHeldBy"/>).
/// </para>
/// 장(③)에는 구멍 · 문의 열림만 요청한다 (Field.SetHole · SetDoor).
/// </summary>
/// <summary>문이 열리지 않는 까닭 (④ — 마음 · 주컴퓨터가 막힘으로 읽는다).</summary>
public enum DoorShut : byte { None, Jammed, Unpowered, Held, VacuumBeyond, Pressure, Locked }

/// <summary>파편이 지난 칸 하나 (DF8 — ⑥ 망이 읽는다).</summary>
public struct ShardPass
{
    public int Cell;
    public float J;
    public int Cause;
}

public sealed partial class Structure : IEngine
{
    public string Name => "구조";
    public Phase Phase => Phase.Structure;
    public int Every => 1;
    public int Offset => 0;

    public const double HullRatingKPa = 400, WallRatingKPa = 160;
    /// <summary>보통 문: 한 기압 차의 두 배 남짓을 버틴다 (DF9 — 우주선 해치 · 칸막이 문은 진공 대 한 기압을 무한정 버틴다: 정적 하중으로는 시들지 않는다 · 한 기압이면 부하 0.48이라 끼익거린다).</summary>
    public const double NormalDoorKPa = 210;
    /// <summary>피로 쌓임 (부하가 오르내린 폭마다 · 넘친 부하의 세제곱에 곱한다 — 부하 0.8을 오르내리면 천 번 남짓에 찢어진다 · 정적 하중은 쌓지 않는다 · DF9) · 오르내림으로 셀 가장 작은 폭 (기압이 틱마다 조금씩 흔들리는 것은 빼고).</summary>
    public const double FatigueK = 0.002, FatigueStep = 0.02;
    /// <summary>버티는 벽 · 문이 끼익거리는 부하 (정격의 몫) · 압력 차가 민 원인을 다는 부하.</summary>
    public const double CreakLoad = 0.45, PushedLoad = 0.4;   // 민 원인은 새 판의 부하로 (평시 한 기압을 받치는 외판 0.25는 '밀었다'가 아니다)
    public const double BraceFactor = 1.6;   // 덧대 받친 문 · 벽의 정격 배율   // 피로 쌓임 (초당 · 넘친 부하의 세제곱에 곱한다)
    public const double CrackArea = 0.0005;  // 금 (m²)
    /// <summary>초고속 충돌이 배 안 공기 칸에 남긴 열 가운데 충격파가 되는 몫 (어림 — 파편 구름 · 증기의 팽창).</summary>
    public const double BlastShare = 0.15;
    /// <summary>충격파가 되는 가장 작은 에너지 (J — TNT 50g · 그 아래는 그 방의 열로).</summary>
    public const double BlastMinJ = 0.05 * 4.184e6;

    private readonly Field F;
    private readonly Layout L;
    private readonly Tables T;

    // ── 상태 ──
    public float[] Fatigue;      // 0~1
    public float[] Corrode;      // 0~1 (두께가 준 비율)
    public float[] Stress;       // 지난 틱 부하 (정격에 대한 비율 — 화면 · 연출 · 주컴퓨터 센서)
    /// <summary>녹이 먹은 산소 (몰) — 벽 속으로 들어간 원소 (보존 장부에서 더한다).</summary>
    public double RustedO2;
    public int[] DamageCause;    // 그 벽을 상하게 한 원인
    public float[] DoorTarget;   // 문 목표 0~1
    public byte[] DoorJam;       // 1: 끼임
    public byte[] Braced;        // 1: 버티는 문 · 벽에 덧대 받쳤다 (정격 ×1.6)
    public byte[] DoorPowered;   // 1: 전동 (망이 끊으면 0 — 손으로만)
    public long Cracks, Breaches, Impacts, DoorsBlown, Bleeds, BleedAborts;
    /// <summary>바깥문 비우기 감시: 밸브를 연 때 · 그때 방 기압 · 다시 비우지 않을 때까지 (에어락이 비워지지 않으면 멈춘다).</summary>
    public long[] BleedSince; public float[] BleedFrom; public long[] BleedLock; public byte[] BleedFails; public long[] BleedLast;
    /// <summary>⑩의 요청: 주컴퓨터가 잠근 전동 문 (그때까지 — 닫힌 문은 열리지 않는다 · 열린 문은 닫을 수만 있다). 임시 에어락으로 쓰는 방 · 우주로 트인 방 쪽.</summary>
    public long[] DoorLock;
    /// <summary>칸마다 들어온 충격 에너지 (J) — ⑦ 생명이 읽고 비운다 (그 칸 몸의 상처).</summary>
    public float[] ImpactJ;
    /// <summary>그 가운데 지나는 순간 그 칸에 있던 사람 (⑧이 알린 원판)이 맞음으로 받아 간 몫 (J — DF8 · ⑦이 ImpactJ와 함께 비운다: 남은 몫만 알리지 않은 몸 · 늦게 든 몸이 받는다).</summary>
    public float[] ImpactTakenJ;
    /// <summary>벽 · 문 칸마다 피로를 마지막으로 센 부하 (오르내림의 기준 — DF9).</summary>
    public float[] LoadRef;
    /// <summary>폭풍이 바닥 칸에 준 가장 큰 넘친 압력 (kPa — ⑦ 귀가 읽고 지운다 · 소리 단계 S3).</summary>
    public float[] BlastPeakKPa;
    /// <summary>점화의 관성 하중 (g — 배 가속도 / 9.8): ⑤ 바깥이 틱마다 넣는 요청. 외판 · 뼈대가 받는 하중이 이만큼 는다 (점화 한 번 = 하중 한 주기). 찍지 않는다.</summary>
    public double ThrustG;

    private double[] _p = Array.Empty<double>();

    public Structure(Field f)
    {
        F = f; L = f.L; T = f.T;
        Fatigue = new float[L.N]; Corrode = new float[L.N]; Stress = new float[L.N]; DamageCause = new int[L.N];
        DoorTarget = new float[L.Doors.Count]; DoorJam = new byte[L.Doors.Count]; Braced = new byte[L.N];
        DoorPowered = Enumerable.Repeat((byte)1, L.Doors.Count).ToArray();
        ImpactJ = new float[L.N]; ImpactTakenJ = new float[L.N]; LoadRef = new float[L.N]; BlastPeakKPa = new float[L.N];
        BleedSince = new long[L.Doors.Count]; BleedFrom = new float[L.Doors.Count]; BleedLock = Enumerable.Repeat(long.MinValue / 2, L.Doors.Count).ToArray(); BleedFails = new byte[L.Doors.Count]; BleedLast = Enumerable.Repeat(long.MinValue / 2, L.Doors.Count).ToArray();
        DoorLock = Enumerable.Repeat(long.MinValue / 2, L.Doors.Count).ToArray();
        // 에어락 벽: 바깥문이 있는 방의 벽은 배의 압력 경계다 — 비운 에어락의 안쪽 벽도 한 기압을 버틴다 (외판과 같은 정격 · ISS 퀘스트 에어락)
        _lockWall = new bool[L.N];
        var lockRooms = new HashSet<int>();
        Span<int> nb = stackalloc int[4];
        foreach (var d in L.Doors)
        {
            if (!d.External) continue;
            L.Neighbors(d.Cell, nb);
            foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0) lockRooms.Add(L.Room[j]);
        }
        foreach (int r in lockRooms)
            foreach (int c in L.Rooms[r].Cells)
            {
                L.Neighbors(c, nb);
                foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Wall) _lockWall[j] = true;
            }
        Crack = new float[L.N]; CrackPatch = new float[L.N]; SigTurn = new float[L.N]; SigRun = new float[L.N]; ThTurn = new float[L.N]; ThRun = new float[L.N]; CrackCause = new int[L.N]; SigDir = new sbyte[L.N];
        Frag = new int[L.N]; FurnHit = new float[L.Furniture.Count]; FurnBroken = new byte[L.Furniture.Count]; FurnCause = new int[L.Furniture.Count];
        ThermalJam = new byte[L.Doors.Count]; _pPrev = new float[L.N];
        BuildCrackGeometry();
        BuildFrames();
        BuildSub();
        PropArrays();
    }
    private readonly bool[] _lockWall;

    // ───────────────────────── 요청 ─────────────────────────

    /// <summary>문을 이 목표로 (0 닫힘 ~ 1 열림).</summary>
    public void RequestDoor(int door, float target) => DoorTarget[door] = Math.Clamp(target, 0f, 1f);

    /// <summary>구멍을 막는다 (임시 봉합 · 용접) — 남는 넓이로.</summary>
    public void Patch(int cell, float remainingArea)
    {
        if (L.Kind[cell] == CellKind.Door && L.DoorAt[cell] >= 0 && remainingArea <= 0) F.TapeDoor(L.DoorAt[cell]); // 문틈도 막는다
        SetHole(cell, Math.Min(F.Hole[cell], Math.Max(0, remainingArea)));
        if (Crack[cell] > 0 && remainingArea <= 0) CrackPatch[cell] = Crack[cell]; // 금 위에 덧판 — 그 길이까지는 새지 않는다 (더 자라면 다시 샌다)
    }

    /// <summary>버티는 문 · 벽에 덧대 받친다 (수리 꾸러미) — 정격 ×1.6 · 피로가 더 쌓이는 것을 늦춘다.</summary>
    public void Brace(int cell) { if (L.Kind[cell] is CellKind.Wall or CellKind.Door) Braced[cell] = 1; }
    /// <summary>끼인 문을 바로잡는다 (다시 여닫힌다) — 문짝이 날아간 문은 바로잡을 것이 없다 (열린 채 · PH10).</summary>
    public void FreeDoor(int door) { if (LeafGone(door)) return; DoorJam[door] = 0; DoorTarget[door] = F.DoorOpen[door]; }

    /// <summary>문틀에 무엇이 끼었다 (위층의 요청 — 바람에 날린 상자 등): 그 자리에서 멈춰 닫히지 않는다 · 끼인 문으로 바로잡아야 한다.</summary>
    public void JamDoor(Sim sim, int door, int cause)
    {
        if (DoorJam[door] != 0) return;
        int c = L.Doors[door].Cell;
        DoorJam[door] = 1;
        DamageCause[c] = cause;
        F.SetDoor(door, Math.Max(F.DoorOpen[door], 0.5f));
        sim.Facts.Emit(sim.Tick, FactKind.DoorJammed, c, F.DoorOpen[door], cause);
        sim.Find<Sound>()?.Emit(sim, NearestAir(c), 95, SoundKind.Bang, cause);
    }
    /// <summary>문 양쪽 기압 차 (kPa).</summary>
    public double DoorDeltaP(int door)
    {
        // 마음 · 주컴퓨터가 부른다 — 틱 안 캐시(_p)에 기대지 않고 지금 기압으로
        var dr = L.Doors[door];
        int x = L.X(dr.Cell), y = L.Y(dr.Cell);
        int Side(int xx, int yy) => L.In(xx, yy) ? L.Index(xx, yy) : -1;
        int a = dr.Vertical ? Side(x, y - 1) : Side(x - 1, y), b = dr.Vertical ? Side(x, y + 1) : Side(x + 1, y);
        return Math.Abs(SideKPa(a) - SideKPa(b));
    }
    /// <summary>그 칸이 끼인 문인가 (바로잡을 수 있는 — 문짝이 날아간 문은 끼인 것이 아니라 빈 문틀이다).</summary>
    public bool JammedAt(int cell) => L.DoorAt[cell] >= 0 && DoorJam[L.DoorAt[cell]] != 0 && !LeafGone(L.DoorAt[cell]);

    /// <summary>관통 값(재질 표)을 잰 파편의 크기 (2cm — 운석 · 작은 파편).</summary>
    public const double ImpactSizeM = 0.02;
    /// <summary>뭉툭한 것은 덜 뚫는다 (드 마르: 뚫는 에너지 ∝ 지름^1.5 — 같은 두께) — 200kg 덩어리가 7m/s로 부딪히면 찌그러뜨릴 뿐.</summary>
    public static double Blunt(double sizeM) => Math.Pow(Math.Max(1, sizeM / ImpactSizeM), 1.5);

    /// <summary>파편 구름의 갈래 (DF8 — 첫 벽을 뚫은 뒤 원뿔로 퍼진다: 옆 기울기 · 몫 — 겉 갈래의 반각 약 24° · 초고속 충돌의 파편 구름은 반각 30° 언저리):
    /// 남은 탄체 덩이가 가는 가운데 줄이 가장 무겁고 (0.6) · 안쪽 갈래 (기울기 0.2 — 둘째 칸부터 옆으로) · 겉의 흩뿌림 (0.45).</summary>
    private static readonly (double Slope, double Share)[] ConeRays = { (0, 0.6), (-0.2, 0.13), (0.2, 0.13), (-0.45, 0.07), (0.45, 0.07) };
    /// <summary>파편이 지난 칸 (DF8 — ⑥ 망이 읽고 비운다: 그 칸의 선 · 관이 파편 에너지에 찢긴다 · 칸 · 지난 에너지 J · 원인).</summary>
    public readonly List<ShardPass> ShardPasses = new();

    /// <summary>운석 · 구름 파편의 밀도 (kg/m³ — 돌 · 알루미늄 어림 · 구멍 크기의 운석 지름).</summary>
    public const double RockDensity = 2800;
    private double _rockKg;

    /// <summary>얇은 판 관통의 구멍 넓이 (m² — 운석 kg · 에너지 J · 판 두께 mm): D = d (0.45 V (t/d)^⅔ + 0.9) · d = 같은 무게 공의 지름 (DF9).</summary>
    public static double RockHoleM2(double kg, double joules, double mm)
    {
        double d = Math.Cbrt(6 * kg / (Math.PI * RockDensity)), v = Math.Sqrt(2 * joules / kg) / 1000, t = mm / 1000;
        double dd = d * (0.45 * v * Math.Pow(t / d, 2.0 / 3) + 0.9);
        return Math.PI / 4 * dd * dd;
    }

    /// <summary>
    /// 운석 · 구름 파편이 배에 부딪힌다 (⑤ 바깥이 부른다 — 무게 kg · 에너지 J · 날아온 쪽 (ux, uy) · 칸 축 (dx, dy)): 운석의 운동량 m·v가 맞은 몸에 들어가고 (DF9 — 덜컹 · 원인 그 운석) ·
    /// 처음 뚫는 판의 구멍은 운석 지름으로 (그 뒤는 충돌과 같다).
    /// </summary>
    public void Rock(Sim sim, int cell, double kg, double joules, double ux, double uy, int dx, int dy, int cause)
    {
        if (kg > 0 && joules > 0)
        {
            double p = Math.Sqrt(2 * joules * kg);   // m·v
            EnsureShip();
            int b = BodyOf(cell);
            if (b >= 0)
            {
                var (wx, wy) = WorldOf(b, L.X(cell) + 0.5, L.Y(cell) + 0.5);
                double th = Bodies[b].Th, jx = p * (Math.Cos(th) * ux - Math.Sin(th) * uy), jy = p * (Math.Sin(th) * ux + Math.Cos(th) * uy);   // 도면 쪽 → 세상 틀
                Push(b, wx, wy, jx, jy, cause);
                RockPx += jx; RockPy += jy; RockL += wx * jy - wy * jx;
            }
        }
        _rockKg = kg;
        try { Impact(sim, cell, joules, dx, dy, cause); }
        finally { _rockKg = 0; }
    }
    /// <summary>운석이 배의 몸에 넣은 운동량 (N·s · 세상 틀 — 법칙 장부: 몸들의 운동량 = 점화 + 운석 − 기체).</summary>
    public double RockPx, RockPy, RockL;

    /// <summary>충돌: 칸 cell에 에너지 joules가 (dx, dy) 방향으로 들어온다 (부딪힌 것의 크기 sizeM — 클수록 뭉툭해 덜 뚫는다).
    /// 첫 벽을 뚫기 전에는 한 줄 · 뚫은 뒤에는 원뿔의 갈래 다섯 (같은 걸음에 같은 칸에 닿은 갈래는 합쳐 셈한다 — 갈래마다 몫이 줄어든다).</summary>
    public void Impact(Sim sim, int cell, double joules, int dx, int dy, int cause, double sizeM = ImpactSizeM)
    {
        Impacts++;
        var snd = sim.Find<Sound>();
        sim.Facts.Emit(sim.Tick, FactKind.Impact, cell, (float)(joules / 1000), cause);
        snd?.Emit(sim, NearestAir(cell), (float)(85 + 10 * Math.Log10(Math.Max(1, joules / 1000))), SoundKind.Bang, cause);
        int nr = ConeRays.Length;
        Span<int> rc = stackalloc int[nr]; Span<double> re = stackalloc double[nr];
        Span<int> uc = stackalloc int[nr]; Span<double> ue = stackalloc double[nr], uf = stackalloc double[nr];
        int n = 1; rc[0] = cell; re[0] = joules;
        int ox = 0, oy = 0, k = 0;   // 원뿔이 열린 칸 · 그 뒤 걸음
        bool cone = false;
        int px = -dy, py = dx;       // 옆 방향
        for (int hop = 1; hop <= 12; hop++)
        {
            // 이 걸음에 갈래들이 닿은 칸마다 에너지를 합친다
            int nu = 0;
            for (int i = 0; i < n; i++)
            {
                if (rc[i] < 0 || re[i] <= 100) continue;
                int u = 0;
                while (u < nu && uc[u] != rc[i]) u++;
                if (u == nu) { uc[nu] = rc[i]; ue[nu] = 0; nu++; }
                ue[u] += re[i];
            }
            if (nu == 0) break;
            bool opened = false;
            for (int u = 0; u < nu; u++)
            {
                int c = uc[u];
                double e = ue[u];
                uf[u] = 0;
                var kind = L.Kind[c];
                if (kind == CellKind.Wall || kind == CellKind.Door)
                {
                    var m = T.Materials[L.Material[c]].Def;
                    double mm = kind == CellKind.Door ? 3 : L.Hull[c] ? 10 : 4;
                    double pen = m.Penetrate * 1000 * mm * Math.Max(0.2, HeatFactor(c, m)) * (1 - Corrode[c]) * Blunt(sizeM);
                    if (e < pen)
                    {
                        Fatigue[c] = (float)Math.Min(1, Fatigue[c] + 0.3 * e / pen);
                        DamageCause[c] = cause;
                        // 뚫지 못한 파편은 흠을 낸다 — 금의 씨앗 (2mm + 40mm × (에너지/관통)^1.5)
                        if (kind == CellKind.Wall && e > 0.05 * pen) SeedCrack(sim, c, 0.002 + 0.04 * Math.Pow(e / pen, 1.5), sim.Causes.Add(CauseKind.Impact, "파편이 벽에 흠을 냈다", sim.Tick, c, cause));
                        AddHeatTo(c, e);
                        continue;
                    }
                    double excess = e - pen;
                    // 구멍 넓이: 운석이 처음 뚫는 판은 얇은 판 관통 어림 (구멍 지름 D = 운석 지름 d × (0.45 · V(km/s) · (t/d)^⅔ + 0.9) — DF9 · 5g 20 · 60g 49 · 2kg 230cm² 언저리) ·
                    //  그 뒤 판 (파편 구름) · 무게를 모르는 것은 남은 에너지로 (1e-4 × kJ^0.8) — 한 칸의 구멍은 2.5m²까지
                    double area = !cone && _rockKg > 0 ? RockHoleM2(_rockKg, joules, mm) : 1e-4 * Math.Pow(excess / 1000, 0.8);
                    area = Math.Min(2.5, area);
                    Open(sim, c, Math.Max(F.Hole[c], (float)area), cause, FactKind.WallBreach);
                    if (kind == CellKind.Door && L.DoorAt[c] >= 0) { DoorJam[L.DoorAt[c]] = 1; F.SetDoor(L.DoorAt[c], Math.Max(F.DoorOpen[L.DoorAt[c]], 0.3f)); }
                    AddHeatTo(c, pen);
                    ShardPasses.Add(new ShardPass { Cell = c, J = (float)excess, Cause = cause });
                    uf[u] = excess * 0.6 / e;
                    if (!cone) { opened = true; ox = L.X(c); oy = L.Y(c); }
                }
                else if (L.IsSpace(c))
                {
                    // 공기 칸: 절반은 열 (불덩이 · 파편이 데운다), 절반은 계속
                    ImpactJ[c] += (float)e;
                    HitActors(sim, c, e, cause);
                    ShardPasses.Add(new ShardPass { Cell = c, J = (float)e, Cause = cause });
                    // 그 칸의 설비 · 가구가 맞는다 (100J/kg에 부서진다 — 어림)
                    if (L.FurnitureAt[c] >= 0) HitFurniture(sim, L.FurnitureAt[c], e * 0.3 / Math.Max(10, FurnitureKg(L.FurnitureAt[c]) * 100), cause, "파편에");
                    double heat = e * 0.5;
                    // 초고속 충돌은 배 안에서 터진다: 그 열의 15%가 충격파 (파편 구름 · 증기의 팽창 — 문턱 없이 연속 · DF9: 작은 관통은 TNT 50g 아래라 폭풍이 없고 ·
                    //  kg급 운석부터 방을 넘어서는 충격파가 된다 — 예전 20MJ 문턱의 절벽이 없다)
                    if (heat * BlastShare >= BlastMinJ) { Blast(sim, c, heat * BlastShare, cause); heat *= 1 - BlastShare; }
                    F.AddSurfaceHeat(c, heat * 0.5);
                    F.AddHeat(c, heat * 0.5);
                    sim.Find<Chemistry>()?.Ignite(c, 0, cause);
                    uf[u] = 0.5;
                }
                else if (kind == CellKind.Outside && hop > 1) uf[u] = 0;
                else uf[u] = 1;
            }
            for (int i = 0; i < n; i++)
            {
                if (rc[i] < 0 || re[i] <= 100) continue;
                int u = 0;
                while (uc[u] != rc[i]) u++;
                re[i] *= uf[u];
            }
            if (opened)
            {
                // 첫 벽을 뚫었다: 남은 에너지를 원뿔의 갈래로 나눈다
                double left = re[0];
                cone = true; n = nr; k = 0;
                for (int i = 0; i < nr; i++) re[i] = left * ConeRays[i].Share;
            }
            if (cone)
            {
                k++;
                for (int i = 0; i < n; i++)
                {
                    double s = ConeRays[i].Slope;
                    int side = Math.Sign(s) * (int)Math.Floor(Math.Abs(s) * k + 0.5);
                    int nx = ox + dx * k + px * side, ny = oy + dy * k + py * side;
                    rc[i] = rc[i] >= 0 && L.In(nx, ny) ? L.Index(nx, ny) : -1;
                }
            }
            else
            {
                int nx = L.X(rc[0]) + dx, ny = L.Y(rc[0]) + dy;
                rc[0] = L.In(nx, ny) ? L.Index(nx, ny) : -1;
            }
        }
    }

    /// <summary>파편 · 충격파가 그 칸을 지나는 순간 그 칸에 있던 사람 (⑧이 알린 원판)을 맞음으로 남긴다 (⑦이 다음 생명 틱에 읽는다 — 그 사이 걸어 나가도 맞았다 · DF8).</summary>
    private void HitActors(Sim sim, int c, double e, int cause)
    {
        bool took = false;
        for (int a = 0; a < _nActors; a++)
            if (_actors[a].Tag >= 0 && _actors[a].Cell == c)
            {
                PropHits.Add(new PropHit { Tag = _actors[a].Tag, Cell = c, E = (float)e, Sharp = ShardHitMark, Cause = cause, Tick = sim.Tick });
                took = true;
            }
        if (took) ImpactTakenJ[c] += (float)e;
    }
    /// <summary>맞음 장부에서 파편 · 충격파의 맞음 (물체가 맞힌 것과 다르다 — ⑦은 칸의 충격처럼 셈한다).</summary>
    public const byte ShardHitMark = 2;

    private void AddHeatTo(int c, double j)
    {
        if (F.HasSurface(c)) F.AddSurfaceHeat(c, j); else F.AddSolidHeat(c, j);
    }

    /// <summary>소리가 나는 공기 칸: 바닥 · 열린 문은 그 칸, 벽 · 닫힌 문은 옆 칸 중 기압이 가장 높은 쪽 (진공 쪽에선 아무도 못 듣는다).</summary>
    private int NearestAir(int c)
    {
        if (L.Kind[c] == CellKind.Floor || (L.Kind[c] == CellKind.Door && F.DoorPassable(L.DoorAt[c]))) return c;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(c, nb);
        int best = c; double bp = -1;
        foreach (int j in nb)
            if (j >= 0 && L.IsSpace(j) && F.Pressure(j) > bp) { bp = F.Pressure(j); best = j; }
        return best;
    }

    // ───────────────────────── 한 틱 ─────────────────────────

    public void Bind(Sim sim) { _life ??= sim.Find<Life>(); _chem ??= sim.Find<Chemistry>(); }
    public void Attach(Sim sim) => _sim = sim;   // 틱 사이 덜컹이 사실 · 원인을 남길 세계 — 붙을 때 잡는다 (되살린 세계가 첫 틱 전에도 같게 · PH10 검토)

    public void Step(Sim sim, double dt)
    {
        if (_p.Length != L.N) _p = new double[L.N];
        _now = sim.Tick; _sim = sim;
        ShardPasses.Clear();   // ⑥이 이 틱 앞에서 읽었다 (망이 없는 세계에서도 쌓이지 않게)
        long t = Prof.Start();
        // 닫힌 문 칸은 벽처럼 (장이 기체를 비워 둔 칸 — 기압 0으로 읽으면 옆 벽이 한쪽이 진공이라고 착각한다)
        for (int i = 0; i < L.N; i++)
            _p[i] = (L.Kind[i] == CellKind.Floor || L.Kind[i] == CellKind.Door && F.DoorPassable(L.DoorAt[i]) || F.Hole[i] >= 2) ? F.Pressure(i) : -1;
        t = Prof.Lap("구조:압력 읽기", t);
        Doors(sim, dt); t = Prof.Lap("구조:문", t);
        HeatJam(sim); t = Prof.Lap("구조:열 끼임", t);
        Sections(sim); t = Prof.Lap("구조:구획", t);
        Walls(sim, dt); t = Prof.Lap("구조:벽", t);
        Decks(sim); t = Prof.Lap("구조:바닥", t);
        if (sim.Tick % 25 == 0) { Rust(sim, 25 * dt); StressCorrosion(sim, 25 * dt); }
        t = Prof.Lap("구조:녹 · 응력 부식", t);
        SyncSub(sim);
        Lattice(sim);
        Array.Clear(_blow);   // 이번 구멍들의 기압은 다 썼다 (뒤에 부딪혀 난 구멍은 다음 틱에)
        t = Prof.Lap("구조:격자", t);
        Motion(sim, dt, t);   // 몸 → 물체 (StructurePropsMove.cs)
    }

    /// <summary>압력이 빠진 쪽의 원인: 낮은 쪽 구역에 뚫린 틈(벽 구멍 · 날아간 문)이 있으면 그 원인.</summary>
    public int LowSideCause(int c)
    {
        int x = L.X(c), y = L.Y(c), low = -1; double pl = double.MaxValue;
        foreach (var (xx, yy) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
        {
            if (!L.In(xx, yy)) continue;
            int j = L.Index(xx, yy);
            double pj = F.Passable(j) ? F.Pressure(j) : -1; // 마음 · 주컴퓨터도 부른다 — 구조의 틱 안 캐시(_p)에 기대지 않는다
            if (pj >= 0 && pj < pl) { pl = pj; low = j; }
        }
        if (low < 0) return 0;
        int r = F.RegionOf[low];
        if (r < 0) return DamageCause[low];
        foreach (var k in F.Links)
            if ((k.RA == r || k.RB == r) && k.Via >= 0 && DamageCause[k.Via] != 0 && k.Kind is LinkKind.Breach or LinkKind.Crack or LinkKind.Open or LinkKind.DoorAjar)
                return DamageCause[k.Via];
        return 0;
    }

    /// <summary>양쪽 압력: 좌우 · 위아래 가운데 차이가 큰 쪽 (바깥은 바깥 압력 · 벽 속은 -1이면 건너뜀).</summary>
    private double DeltaP(int c)
    {
        int x = L.X(c), y = L.Y(c);
        double Side(int xx, int yy)
        {
            if (!L.In(xx, yy)) return F.Outside.KPa;
            int j = L.Index(xx, yy);
            if (L.Kind[j] == CellKind.Outside) return F.Outside.KPa;
            return _p[j];
        }
        double best = 0;
        double l = Side(x - 1, y), r = Side(x + 1, y), u = Side(x, y - 1), d = Side(x, y + 1);
        if (l >= 0 && r >= 0) best = Math.Max(best, Math.Abs(l - r));
        if (u >= 0 && d >= 0) best = Math.Max(best, Math.Abs(u - d));
        return best;
    }

    /// <summary>열 약화: 녹는점에 대한 비율 0.3까지는 그대로, 0.7에서 10%.</summary>
    public static double HeatFactor(double tempK, MaterialDef m)
    {
        double r = tempK / Math.Max(300, m.MeltK);
        if (r < 0.3) return 1;
        return Math.Max(0.1, 1 - (r - 0.3) / 0.4 * 0.9);
    }

    private double HeatFactor(int c, MaterialDef m) => HeatFactor(F.Ts[c], m);

    /// <summary>이 벽 · 문 칸의 정격 (kPa).</summary>
    public double Rating(int c)
    {
        var m = T.Materials[L.Material[c]].Def;
        double baseKPa;
        if (L.Kind[c] == CellKind.Door)
        {
            var d = L.Doors[L.DoorAt[c]];
            baseKPa = d.Kind switch { "pressure" => 300, "airlock" => 400, _ => NormalDoorKPa };
        }
        else baseKPa = L.Hull[c] || _lockWall[c] ? HullRatingKPa : WallRatingKPa;
        double strength = Math.Sqrt(Math.Max(0.01, m.Strength / 250.0));
        return baseKPa * strength * HeatFactor(c, m) * (1 - Fatigue[c]) * (1 - Corrode[c]) * (Braced[c] != 0 ? BraceFactor : 1);
    }

    private void Walls(Sim sim, double dt)
    {
        for (int c = 0; c < L.N; c++)
        {
            var kind = L.Kind[c];
            if (kind != CellKind.Wall && !(kind == CellKind.Door && !F.DoorPassable(L.DoorAt[c]))) { Stress[c] = 0; continue; }
            if (F.Hole[c] >= 2 || F.Severed[c] != 0) { Stress[c] = 0; continue; }
            double dp = DeltaP(c);
            // 금: 응력 되돌이(반 주기마다 파리스 법칙) · 금 끝의 K가 파괴 인성을 넘으면 찢어진다
            if (kind == CellKind.Wall && (dp >= 1 || SigRun[c] != 0 || SigTurn[c] != 0 || ThrustG > 0 || L.Hull[c]))
            {
                double th = ThermalSigma(c);
                ThermalTick(sim, c);              // 소리 단계 S6: 늘고 줄며 이음매가 딱
                double sig = Sigma(c, dp) + th;   // ④ 열 · 진동 (가): 막힌 열 응력도 되돌이를 만든다
                Cycle(sim, c, sig, th);
                if (Crack[c] > 0) { Fracture(sim, c, sig); if (F.Hole[c] >= 2) { Stress[c] = 0; continue; } }
            }
            if (dp < 1) { Stress[c] = 0; if (LoadRef[c] >= FatigueStep) Swing(c, 0, T.Materials[L.Material[c]].Def); continue; }
            double rating = Math.Max(1e-3, Rating(c));
            double load = dp / rating;
            Stress[c] = (float)load;
            var m = T.Materials[L.Material[c]].Def;
            // 피로: 부하가 오르내린 폭 (마이너 — 반 주기마다) × 피로 한계를 넘은 부하의 세제곱 · 정적 하중은 쌓지 않는다 (DF9 — 정격을 넘으면 찢어질 뿐)
            double nominal = dp / Math.Max(1e-3, rating / Math.Max(1e-3, (1 - Fatigue[c]) * (1 - Corrode[c])));
            Swing(c, nominal, m);
            if (nominal >= PushedLoad && DamageCause[c] == 0) DamageCause[c] = sim.Causes.Add(CauseKind.Pressure, "압력 차가 벽을 밀었다", sim.Tick, c, LowSideCause(c));
            // 끼익: 버티는 문 · 벽이 소리를 낸다 (사람 · 컴퓨터가 듣고 알아챈다)
            if (load >= CreakLoad && (sim.Tick + c) % 10 == 0)
                sim.Find<Sound>()?.Emit(sim, NearestAir(c), (float)(50 + 30 * Math.Min(1, load)), SoundKind.Creak, DamageCause[c]);
            if (kind == CellKind.Door)
            {
                if (load >= 1) BlowDoor(sim, c, load);
                continue;
            }
            if (load >= 1.6) Open(sim, c, 2.5f, Cause(sim, c, "벽이 압력을 못 이겨 터졌다"), FactKind.WallBreach);
            else if (load >= 1) Open(sim, c, (float)Math.Max(F.Hole[c], Math.Min(2.0, 0.02 + 0.5 * (load - 1))), Cause(sim, c, "벽이 압력을 못 이겨 찢어졌다"), FactKind.WallBreach);
            else if (load >= 0.85 && F.Hole[c] < CrackArea)
            {
                int cc = Cause(sim, c, "벽에 금이 갔다");
                Open(sim, c, (float)CrackArea, cc, FactKind.WallCrack);
                SeedCrack(c, 0.05 + 0.5 * (load - 0.85), cc);   // 과부하로 난 금 — 자랄 수 있다
            }
        }
    }

    /// <summary>부하가 기준에서 FatigueStep 넘게 옮겨 가면 그 폭을 반 주기로 센다 (넘친 부하의 세제곱 × 폭) — 쌓인 피로가 0.85를 넘으면 벽에 금이 된다 (S-N 손상 → 큰 금의 시작).</summary>
    private void Swing(int c, double nominal, MaterialDef m)
    {
        double swing = Math.Abs(nominal - LoadRef[c]);
        if (swing < FatigueStep) return;
        double top = Math.Max(nominal, LoadRef[c]);
        LoadRef[c] = (float)nominal;
        if (top <= m.Fatigue) return;
        double x = (top - m.Fatigue) / (1 - m.Fatigue);
        Fatigue[c] = (float)Math.Min(1, Fatigue[c] + FatigueK * x * x * x * swing);
        if (Fatigue[c] >= 0.85f && Crack[c] <= 0 && L.Kind[c] == CellKind.Wall) SeedCrack(c, 0.005, DamageCause[c]);
    }

    private int Cause(Sim sim, int c, string text)
    {
        var m = T.Materials[L.Material[c]].Def;
        int parent = DamageCause[c];
        if (HeatFactor(c, m) < 0.8) parent = sim.Causes.Add(CauseKind.Heat, "뜨거워 약해졌다", sim.Tick, c, parent);
        else if (Corrode[c] > 0.2) parent = sim.Causes.Add(CauseKind.Corrosion, "녹이 슬어 얇아졌다", sim.Tick, c, parent);
        return sim.Causes.Add(CauseKind.Breach, text, sim.Tick, c, parent);
    }

    /// <summary>바깥 입력 (함장의 손 — docs/UI2.md §2.4): 외판 칸을 그 넓이만큼 찢는다 — 새 법칙이 아니라 벽이 찢길 때의 길 그대로 (사실 '벽이 뚫렸다' · 쾅).</summary>
    public void Tear(Sim sim, int c, float area, int cause) { if (c >= 0 && c < L.N && L.Kind[c] == CellKind.Wall) Open(sim, c, area, cause, FactKind.WallBreach); }

    /// <summary>벽에 구멍 (금 · 구멍 · 통째로) — 장에 알리고 사실 · 소리.</summary>
    private void Open(Sim sim, int c, float area, int cause, FactKind fact, float db = -1)
    {
        if (area <= F.Hole[c]) return;
        bool first = F.Hole[c] <= 0, gone = F.Hole[c] < 2 && area >= 2;
        SetHole(c, area);
        DamageCause[c] = cause;
        if (fact == FactKind.WallCrack) Cracks++; else if (first) Breaches++;
        if (first || gone) sim.Facts.Emit(sim.Tick, fact, c, area, cause);
        if (gone) _latticeDirty = true;   // 벽이 통째로 나갔다 — 골조 그물이 바뀐다
        if (db < 0) db = fact == FactKind.WallCrack ? 85 : 120;
        if (db > 0) sim.Find<Sound>()?.Emit(sim, NearestAir(c), db, fact == FactKind.WallCrack ? SoundKind.Crack : SoundKind.Bang, cause);
    }

    /// <summary>문이 압력 (load = 받은 압력 / 정격)에 날아간다 — 폭풍이면 blastCell · blastKPa (그 문 칸의 첨두): 문짝은 폭풍의 반대쪽으로 반사 압력의 충격량을 받는다.</summary>
    private void BlowDoor(Sim sim, int c, double load, int blastCell = -1, double blastKPa = 0)
    {
        int d = L.DoorAt[c];
        DoorsBlown++;
        int why = DamageCause[c];
        if (why == 0)
        {
            // 압력이 갑자기 오른 쪽(폭발)의 원인 — 없으면 빠진 쪽의 틈
            int x = L.X(c), y = L.Y(c);
            foreach (var (xx, yy) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                if (why == 0 && L.In(xx, yy) && L.IsSpace(L.Index(xx, yy))) why = PressureCause(sim, L.Index(xx, yy));
        }
        int cause = sim.Causes.Add(CauseKind.Pressure, "문이 압력을 못 이겨 날아갔다", sim.Tick, c, why);
        DoorJam[d] = 1;
        DamageCause[c] = cause;
        F.SetDoor(d, load > 1.5 ? 1f : 0.35f);
        sim.Facts.Emit(sim.Tick, FactKind.DoorBlown, c, (float)load, cause);
        sim.Find<Sound>()?.Emit(sim, NearestAir(c), 125, SoundKind.Bang, cause);
        if (load > 1.5)   // 문짝이 통째로 떨어져 낮은 쪽 (폭풍이면 폭풍의 반대쪽)으로 난다 (PH10 — 덜 세면 열린 채 끼인다)
        {
            int x = L.X(c), y = L.Y(c), lx = 0, ly = 0; double hi = 0, lo = double.MaxValue, far = -1;
            foreach (var (ox, oy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                if (!L.In(x + ox, y + oy)) continue;
                int j = L.Index(x + ox, y + oy);
                if (!L.IsSpace(j) && !F.IsOut(j)) continue;
                double pj = F.IsOut(j) ? 0 : F.Pressure(j);
                hi = Math.Max(hi, pj);
                if (blastCell >= 0)
                {
                    double ex = x + ox - L.X(blastCell), ey = y + oy - L.Y(blastCell), r2 = ex * ex + ey * ey;
                    if (r2 > far) { far = r2; lo = pj; lx = ox; ly = oy; }
                }
                else if (pj < lo) { lo = pj; lx = ox; ly = oy; }
            }
            if (lo < double.MaxValue) BlowLeaf(sim, d, c, Math.Max(0, hi - lo) + 2 * blastKPa, lx, ly, cause);
        }
    }

    private void Doors(Sim sim, double dt)
    {
        for (int d = 0; d < L.Doors.Count; d++)
        {
            float now = F.DoorOpen[d], target = DoorTarget[d];
            bool opening = now <= 0.001f && target > now && DoorJam[d] == 0;
            if (F.DoorBleed[d] != 0 && !opening) F.SetBleed(d, false);
            if (DoorJam[d] != 0 || Math.Abs(now - target) < 1e-4) continue;
            int c = L.Doors[d].Cell;
            // 주컴퓨터가 잠근 전동 문: 닫힌 채로 (밸브도 열지 않는다)
            if (now <= 0.001f && target > now && DoorPowered[d] != 0 && DoorLock[d] > sim.Tick) continue;
            // 닫힌 문을 열 때: 양쪽 압력 차를 이겨야 한다
            if (now <= 0.001f && target > now)
            {
                double limit = DoorPowered[d] != 0 ? (OutsideBleedRoomCell(d) >= 0 ? 5 : 30) : 2;   // 바깥문은 에어락이 거의 비어야 연다 (맞물림 — 배와 이어진 채 반쯤 빈 에어락의 바깥문을 열면 배 공기가 빠진다)
                if (DeltaP(c) > limit)
                {
                    // 전동 문은 먼저 압력 맞춤 밸브로 양쪽 기압을 맞춘다 — 안전할 때만
                    bool ok = DoorPowered[d] != 0 && SafeToBleed(d);
                    int hi = OutsideBleedRoomCell(d);
                    if (ok && F.DoorBleed[d] == 0)
                    {
                        Bleeds++;
                        if (sim.Tick - BleedLast[d] > Time.Minutes(1)) { BleedSince[d] = sim.Tick; BleedFrom[d] = hi >= 0 ? (float)F.Pressure(hi) : 0; }   // 1분 안에 다시 켠 밸브는 그 감시를 이어 간다
                    }
                    if (ok) BleedLast[d] = sim.Tick;
                    // 비우기 감시: 바깥으로 빼는 동안 3분마다 잰다 — 그 3분에 방 기압이 10%도 빠지지 않았다 (배와 공기가 이어져 들어오는 만큼 빠진다 — 비워지지 않는다 · 막힌 큰 방은 계속 빠지니 그대로)
                    if (ok && hi >= 0 && F.DoorBleed[d] != 0 && sim.Tick - BleedSince[d] > Time.Minutes(3) && F.Pressure(hi) <= BleedFrom[d] * 0.9f) { BleedSince[d] = sim.Tick; BleedFrom[d] = (float)F.Pressure(hi); }
                    else if (ok && hi >= 0 && F.DoorBleed[d] != 0 && (sim.Tick - BleedSince[d] > Time.Minutes(3) || sim.Tick - BleedSince[d] > Time.Minutes(1) && Unsealed(hi)))
                    {
                        ok = false; BleedAborts++;
                        BleedLock[d] = sim.Tick + Time.Hours(1 << Math.Min(4, (int)BleedFails[d]));   // 거듭 안 되면 더 오래 (1 · 2 · 4 · 8 · 16시간)
                        if (BleedFails[d] < 255) BleedFails[d]++;
                        DoorTarget[d] = 0;
                        int room = L.Room[hi];
                        int cause = sim.Causes.Add(CauseKind.Machine, $"{Ko.I(room >= 0 ? L.Rooms[room].Name : "에어락")} 비워지지 않아 바깥문 밸브를 잠갔다 — 배 공기가 계속 들어온다", sim.Tick, c);
                        sim.Facts.Emit(sim.Tick, FactKind.Decision, c, (float)F.Pressure(hi), cause, -1, d);
                    }
                    F.SetBleed(d, ok);
                    continue;
                }
            }
            float step = (float)(0.5 * dt), next = target > now ? Math.Min(target, now + step) : Math.Max(target, now - step);
            // 닫히는 문짝 판은 물체에서 멈춘다 (PH4b — 목표는 그대로 · 무거우면 끼고 가벼우면 밀어낸다)
            if (next < now && Props.Count > 0)
            {
                float floor = LeafFloor(d, out int held);
                if (next < floor) { F.SetDoor(d, Math.Min(now, floor)); LeafHit(sim, d, held, dt); continue; }
            }
            F.SetDoor(d, next);
        }
    }

    /// <summary>
    /// 압력 맞춤 밸브를 열어도 되나:
    /// 바깥으로 빼는 문(에어락 바깥문)은 그 방의 다른 문이 모두 닫혀 있고(맞물림) 우주복 없이 살아 있는 사람이 없을 때만 —
    /// 안쪽 문은 낮은 쪽 구역이 우주로 트여 있지 않을 때만 (트였으면 높은 쪽 공기까지 우주로 빠진다 — 구멍을 먼저 막아야 한다).
    /// </summary>
    /// <summary>
    /// 그 문이 지금 열리지 않는 까닭 (⑧ · ⑩이 막힘을 알 때 — 열 수 있으면 None):
    /// 끼었다 · 전기가 없다(손으로는 기압 차 2kPa까지) · 주컴퓨터가 밸브를 잠가 두었다 · 낮은 쪽이 우주로 트여 기압을 맞출 수 없다 · 기압을 맞추는 중이거나 맞물림(에어락)을 기다린다.
    /// </summary>
    public DoorShut WhyShut(int d)
    {
        if (d < 0 || d >= L.Doors.Count) return DoorShut.None;
        if (DoorJam[d] != 0) return F.DoorOpen[d] >= 0.5f ? DoorShut.None : DoorShut.Jammed;
        if (DoorPowered[d] != 0 && DoorLock[d] > _now && F.DoorOpen[d] <= 0.001f) return DoorShut.Locked;
        double limit = DoorPowered[d] != 0 ? (OutsideBleedRoomCell(d) >= 0 ? 5 : 30) : 2;
        if (DoorDeltaP(d) <= limit) return DoorShut.None;
        if (DoorPowered[d] == 0) return DoorShut.Unpowered;
        if (BleedLock[d] > _now) return DoorShut.Held;
        if (LowSideOpen(d)) return DoorShut.VacuumBeyond;
        return DoorShut.Pressure;
    }

    /// <summary>문의 낮은 쪽 기체 구역이 우주로 트여 있다 (안쪽 문 — 밸브를 열면 높은 쪽 공기까지 빠진다).</summary>
    private bool LowSideOpen(int d)
    {
        var door = L.Doors[d];
        int c = door.Cell, x = L.X(c), y = L.Y(c);
        int a = door.Vertical ? Idx(x, y - 1) : Idx(x - 1, y), b = door.Vertical ? Idx(x, y + 1) : Idx(x + 1, y);
        int lo = SideKPa(a) < SideKPa(b) ? a : b;
        if (lo < 0 || L.Kind[lo] == CellKind.Outside) return true;
        int region = lo < F.RegionOf.Length ? F.RegionOf[lo] : -1;
        if (region < 0) return false;
        foreach (var k in F.Links) if (k.RB < 0 && k.RA == region && k.Area > 0) return true;
        return false;
    }

    /// <summary>⑩의 요청: 그 문의 압력 맞춤 밸브를 until까지 잠근다 (안 열린다 — 비워진 방에 배 공기를 나누지 않게).</summary>
    public void HoldBleed(int d, long until) { if (d >= 0 && d < BleedLock.Length) BleedLock[d] = Math.Max(BleedLock[d], until); }

    /// <summary>⑩의 요청: 전동 문을 until까지 잠근다 (닫힌 문은 열리지 않는다) · Unlock은 바로 푼다.</summary>
    public void LockDoor(int d, long until) { if (d >= 0 && d < DoorLock.Length) DoorLock[d] = Math.Max(DoorLock[d], until); }
    public void UnlockDoor(int d) { if (d >= 0 && d < DoorLock.Length) DoorLock[d] = long.MinValue / 2; }
    public bool Locked(int d) => d >= 0 && d < DoorLock.Length && DoorLock[d] > _now && DoorPowered[d] != 0;

    public bool SafeToBleed(int d)
    {
        var door = L.Doors[d];
        int c = door.Cell, x = L.X(c), y = L.Y(c);
        int a = door.Vertical ? Idx(x, y - 1) : Idx(x - 1, y), b = door.Vertical ? Idx(x, y + 1) : Idx(x + 1, y);
        double pa = SideKPa(a), pb = SideKPa(b);
        int lo = pa < pb ? a : b, hi = pa < pb ? b : a;
        bool Out(int j) => j < 0 || L.Kind[j] == CellKind.Outside;
        if (Out(hi)) return false;
        if (Out(lo))
        {
            int room = L.Room[hi];
            if (room < 0 || BleedLock[d] > _now) return false;
            // 방이 배와 막혀 있어야 비울 수 있다 — 그 방 기체 구역에 다른 방 칸이 있으면 (문 없이 트였다) 비워지지 않는다
            int reg = hi < F.RegionOf.Length ? F.RegionOf[hi] : -1;
            if (reg >= 0 && reg < F.Regions.Count) foreach (int e in F.Regions[reg]) if (L.Room[e] >= 0 && L.Room[e] != room) return false;
            foreach (int e in RoomDoors(room)) if (e != d && (F.DoorOpen[e] > 0 || DoorTarget[e] > 0)) return false;
            if (_life != null)
                foreach (var body in _life.Bodies)
                    if (body.Alive != 0 && body.Suit == 0 && body.Cell >= 0 && L.Room[body.Cell] == room) return false;
            return true;
        }
        if (BleedLock[d] > _now) return false;   // 주컴퓨터가 잠가 두었다 (비워진 방 — 채울 예비가 모자라다 · ⑩)
        int region = lo < F.RegionOf.Length ? F.RegionOf[lo] : -1;
        if (region < 0) return false;
        foreach (var k in F.Links) if (k.RB < 0 && k.RA == region && k.Area > 0) return false;
        return true;
    }

    /// <summary>비우는 방이 막혀 있지 않다: 그 기체 구역으로 다른 구역에서 들어오는 흐름이 바깥으로 빼는 흐름의 5분의 1을 넘는다 (안쪽 벽 구멍 · 틈 — 빼는 만큼 배에서 들어온다).</summary>
    private bool Unsealed(int cell)
    {
        int reg = cell < F.RegionOf.Length ? F.RegionOf[cell] : -1;
        if (reg < 0) return false;
        double inflow = 0, outflow = 0;
        foreach (var k in F.Links)
        {
            if (k.RA == reg && k.RB < 0) { if (k.Flow > 0) outflow += k.Flow; }
            else if (k.RA == reg && k.RB >= 0) { if (k.Flow < 0) inflow -= k.Flow; }
            else if (k.RB == reg && k.RA >= 0) { if (k.Flow > 0) inflow += k.Flow; }
        }
        return outflow > 0 && inflow > outflow * 0.2;
    }

    /// <summary>바깥으로 빼는 문이면 방 쪽 칸 (아니면 −1).</summary>
    public int OutsideBleedRoomCell(int d)
    {
        var door = L.Doors[d];
        int c = door.Cell, x = L.X(c), y = L.Y(c);
        int a = door.Vertical ? Idx(x, y - 1) : Idx(x - 1, y), b = door.Vertical ? Idx(x, y + 1) : Idx(x + 1, y);
        bool Out(int j) => j < 0 || L.Kind[j] == CellKind.Outside;
        if (Out(a) && !Out(b)) return b;
        if (Out(b) && !Out(a)) return a;
        return -1;
    }

    private long _now;
    private int Idx(int x, int y) => L.In(x, y) ? L.Index(x, y) : -1;
    private double SideKPa(int j) => j < 0 || L.Kind[j] == CellKind.Outside ? F.Outside.KPa : F.Pressure(j);
    private Life? _life;
    private List<int>[]? _roomDoorList;

    /// <summary>방 둘레의 문.</summary>
    public List<int> RoomDoors(int room)
    {
        if (_roomDoorList == null)
        {
            _roomDoorList = new List<int>[L.Rooms.Count];
            for (int r = 0; r < L.Rooms.Count; r++) _roomDoorList[r] = new List<int>();
            Span<int> nb = stackalloc int[4];
            for (int d = 0; d < L.Doors.Count; d++)
            {
                L.Neighbors(L.Doors[d].Cell, nb);
                foreach (int j in nb)
                    if (j >= 0 && L.Room[j] >= 0 && !_roomDoorList[L.Room[j]].Contains(d)) _roomDoorList[L.Room[j]].Add(d);
            }
        }
        return _roomDoorList[room];
    }

    /// <summary>
    /// 부식 (25틱마다): 철 벽 곁 칸이 습하면(상대 습도 70% 넘게) 반응식 '철이 녹슨다'만큼 두께가 준다 — 그 칸 산소를 먹는다.
    /// 아주 느리다 (몇 달 · 몇 해).
    /// </summary>
    private void Rust(Sim sim, double dt)
    {
        var rust = T.R("rust");
        int fe = T.S("Fe").Index;
        Span<int> nb = stackalloc int[4];
        for (int c = 0; c < L.N; c++)
        {
            if (L.Kind[c] != CellKind.Wall) continue;
            var m = T.Materials[L.Material[c]];
            if (!m.Made.Any(p => p.s == fe)) continue;
            L.Neighbors(c, nb);
            foreach (int j in nb)
            {
                if (j < 0 || !L.IsSpace(j) || F.RegionOf.Length <= j || F.RegionOf[j] < 0) continue;
                double rh = F.Partial(T.gH2O, j) / Math.Max(1e-6, Water.SatKPa(F.Ts[c]));
                bool wet = rh > 0.7 || F.Stored(T.sWater, j) > 10;
                if (!wet) continue;
                // 철 몰/초 = 비율 × (벽 칸 철 약 60kg 겉면 몫) — 산소를 먹는다
                double feMol = rust.Def.Rate * 1000 * dt * (rh > 1 ? 2 : 1);
                double o2 = feMol * 3 / 4;
                double took = F.RemoveGas(j, T.gO2, o2);
                RustedO2 += took; sim.Book.Add(Res.O2, Flow.Rusted, -took);
                Corrode[c] = (float)Math.Min(0.9, Corrode[c] + took * 4 / 3 * 0.0558 / 60.0);
            }
        }
    }

    public void Save(StateWriter w)
    {
        w.Arr(Fatigue); w.Arr(Corrode); w.Arr(Stress); w.Arr(DamageCause); w.Arr(DoorTarget); w.Arr(DoorJam); w.Arr(DoorPowered); w.Arr(Braced);
        w.I64(Cracks); w.I64(Breaches); w.I64(Impacts); w.I64(DoorsBlown); w.F64(RustedO2); w.Arr(ImpactJ); w.I64(Bleeds); w.Tag("폭풍 압력"); w.Arr(BlastPeakKPa);
        w.Arr(BleedSince); w.Arr(BleedFrom); w.Arr(BleedLock); w.Arr(BleedFails); w.Arr(BleedLast); w.I64(BleedAborts); w.Arr(DoorLock);
        w.Tag("금"); w.Arr(Crack); w.Arr(CrackPatch); w.Arr(SigTurn); w.Arr(SigRun); w.Arr(CrackCause); w.Arr(SigDir); w.I64(CrackGrowths); w.I64(Tears); w.I64(CrackLeaks);
        w.Tag("조각"); w.Arr(Frag); w.List(Fragments); w.I32(SplitVersion); w.I64(DeckBursts); w.I64(Smashed); w.I64(Splits); w.I64(DebrisLost); w.I64(SectionFails);
        w.Arr(FurnHit); w.Arr(FurnBroken); w.Arr(FurnCause); w.Arr(ThermalJam); w.Arr(_pPrev); w.Bool(_latticeDirty);
        w.Tag("열 · 진동"); w.Arr(ThTurn); w.Arr(ThRun); w.I64(VibGrowths); w.I64(ThermalCracks);
        w.Tag("열 수축"); w.Arr(TsSlip); w.I64(ThermalTicks);
        w.Tag("파편 길"); w.List(ShardPasses); w.Arr(ImpactTakenJ); w.Arr(LoadRef);
        SaveBodies(w);
        SaveProps(w);
    }

    public void Load(StateReader r)
    {
        r.ArrInto(Fatigue); r.ArrInto(Corrode); r.ArrInto(Stress); r.ArrInto(DamageCause); r.ArrInto(DoorTarget); r.ArrInto(DoorJam); r.ArrInto(DoorPowered); r.ArrInto(Braced);
        Cracks = r.I64(); Breaches = r.I64(); Impacts = r.I64(); DoorsBlown = r.I64(); RustedO2 = r.F64(); r.ArrInto(ImpactJ); Bleeds = r.I64(); r.Tag("폭풍 압력"); r.ArrInto(BlastPeakKPa);
        r.ArrInto(BleedSince); r.ArrInto(BleedFrom); r.ArrInto(BleedLock); r.ArrInto(BleedFails); r.ArrInto(BleedLast); BleedAborts = r.I64(); r.ArrInto(DoorLock);
        r.Tag("금"); r.ArrInto(Crack); r.ArrInto(CrackPatch); r.ArrInto(SigTurn); r.ArrInto(SigRun); r.ArrInto(CrackCause); r.ArrInto(SigDir); CrackGrowths = r.I64(); Tears = r.I64(); CrackLeaks = r.I64();
        r.Tag("조각"); r.ArrInto(Frag); Fragments.Clear(); Fragments.AddRange(r.List<Fragment>()); SplitVersion = r.I32(); DeckBursts = r.I64(); Smashed = r.I64(); Splits = r.I64(); DebrisLost = r.I64(); SectionFails = r.I64();
        r.ArrInto(FurnHit); r.ArrInto(FurnBroken); r.ArrInto(FurnCause); r.ArrInto(ThermalJam); r.ArrInto(_pPrev); _latticeDirty = r.Bool();
        r.Tag("열 · 진동"); r.ArrInto(ThTurn); r.ArrInto(ThRun); VibGrowths = r.I64(); ThermalCracks = r.I64();
        r.Tag("열 수축"); TsSlip = r.Arr<float>(); ThermalTicks = r.I64();
        r.Tag("파편 길"); ShardPasses.Clear(); ShardPasses.AddRange(r.List<ShardPass>()); r.ArrInto(ImpactTakenJ); r.ArrInto(LoadRef);
        LoadBodies(r);
        LoadProps(r);
        Label(_comp, out _); _latticeVersion++; _thrustAxis = -1; Array.Clear(_thrustSig);   // 덩어리 · 단면은 다시 센다 (찍지 않는다)
    }
}
