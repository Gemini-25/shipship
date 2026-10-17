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
/// </para>
/// 장(③)에는 구멍 · 문의 열림만 요청한다 (Field.SetHole · SetDoor).
/// </summary>
public sealed class Structure : IEngine
{
    public string Name => "구조";
    public Phase Phase => Phase.Structure;
    public int Every => 1;
    public int Offset => 0;

    public const double HullRatingKPa = 400, WallRatingKPa = 160;
    /// <summary>보통 문: 한 기압은 겨우 버틴다 — 피로가 쌓이면 수십 분 뒤 날아간다 (끼익 소리가 먼저).</summary>
    public const double NormalDoorKPa = 120;
    public const double FatigueK = 1.8e-4;
    public const double BraceFactor = 1.6;   // 덧대 받친 문 · 벽의 정격 배율   // 피로 쌓임 (초당 · 넘친 부하의 세제곱에 곱한다)
    public const double CrackArea = 0.0005;  // 금 (m²)

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
    public long Cracks, Breaches, Impacts, DoorsBlown, Bleeds;
    /// <summary>칸마다 들어온 충격 에너지 (J) — ⑦ 생명이 읽고 비운다 (그 칸 몸의 상처).</summary>
    public float[] ImpactJ;

    private double[] _p = Array.Empty<double>();

    public Structure(Field f)
    {
        F = f; L = f.L; T = f.T;
        Fatigue = new float[L.N]; Corrode = new float[L.N]; Stress = new float[L.N]; DamageCause = new int[L.N];
        DoorTarget = new float[L.Doors.Count]; DoorJam = new byte[L.Doors.Count]; Braced = new byte[L.N];
        DoorPowered = Enumerable.Repeat((byte)1, L.Doors.Count).ToArray();
        ImpactJ = new float[L.N];
    }

    // ───────────────────────── 요청 ─────────────────────────

    /// <summary>문을 이 목표로 (0 닫힘 ~ 1 열림).</summary>
    public void RequestDoor(int door, float target) => DoorTarget[door] = Math.Clamp(target, 0f, 1f);

    /// <summary>구멍을 막는다 (임시 봉합 · 용접) — 남는 넓이로.</summary>
    public void Patch(int cell, float remainingArea)
    {
        if (L.Kind[cell] == CellKind.Door && L.DoorAt[cell] >= 0 && remainingArea <= 0) F.TapeDoor(L.DoorAt[cell]); // 문틈도 막는다
        F.SetHole(cell, Math.Min(F.Hole[cell], Math.Max(0, remainingArea)));
    }

    /// <summary>버티는 문 · 벽에 덧대 받친다 (수리 꾸러미) — 정격 ×1.6 · 피로가 더 쌓이는 것을 늦춘다.</summary>
    public void Brace(int cell) { if (L.Kind[cell] is CellKind.Wall or CellKind.Door) Braced[cell] = 1; }
    /// <summary>끼인 문을 바로잡는다 (다시 여닫힌다).</summary>
    public void FreeDoor(int door) { DoorJam[door] = 0; DoorTarget[door] = F.DoorOpen[door]; }

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
    /// <summary>그 칸이 끼인 문인가.</summary>
    public bool JammedAt(int cell) => L.DoorAt[cell] >= 0 && DoorJam[L.DoorAt[cell]] != 0;

    /// <summary>충돌: 칸 cell에 에너지 joules가 (dx, dy) 방향으로 들어온다.</summary>
    public void Impact(Sim sim, int cell, double joules, int dx, int dy, int cause)
    {
        Impacts++;
        var snd = sim.Find<Sound>();
        int c = cell;
        double e = joules;
        int hops = 0;
        sim.Facts.Emit(sim.Tick, FactKind.Impact, cell, (float)(joules / 1000), cause);
        snd?.Emit(sim, NearestAir(cell), (float)(85 + 10 * Math.Log10(Math.Max(1, joules / 1000))), SoundKind.Bang, cause);
        while (e > 100 && c >= 0 && hops++ < 12)
        {
            var kind = L.Kind[c];
            if (kind == CellKind.Wall || kind == CellKind.Door)
            {
                var m = T.Materials[L.Material[c]].Def;
                double mm = kind == CellKind.Door ? 3 : L.Hull[c] ? 10 : 4;
                double pen = m.Penetrate * 1000 * mm * Math.Max(0.2, HeatFactor(c, m)) * (1 - Corrode[c]);
                if (e < pen)
                {
                    Fatigue[c] = (float)Math.Min(1, Fatigue[c] + 0.3 * e / pen);
                    DamageCause[c] = cause;
                    AddHeatTo(c, e);
                    break;
                }
                double excess = e - pen;
                double area = Math.Min(2.5, 1e-4 * Math.Pow(excess / 1000, 0.8));
                Open(sim, c, Math.Max(F.Hole[c], (float)area), cause, FactKind.WallBreach);
                if (kind == CellKind.Door && L.DoorAt[c] >= 0) { DoorJam[L.DoorAt[c]] = 1; F.SetDoor(L.DoorAt[c], Math.Max(F.DoorOpen[L.DoorAt[c]], 0.3f)); }
                AddHeatTo(c, pen);
                e = excess * 0.6;
            }
            else if (L.IsSpace(c))
            {
                // 공기 칸: 절반은 열 (불덩이 · 파편이 데운다), 절반은 계속
                ImpactJ[c] += (float)e;
                double heat = e * 0.5;
                F.AddSurfaceHeat(c, heat * 0.5);
                F.AddHeat(c, heat * 0.5);
                sim.Find<Chemistry>()?.Ignite(c, 0, cause);
                e -= heat;
            }
            else if (kind == CellKind.Outside && hops > 1) break;
            int nx = L.X(c) + dx, ny = L.Y(c) + dy;
            c = L.In(nx, ny) ? L.Index(nx, ny) : -1;
        }
    }

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

    public void Step(Sim sim, double dt)
    {
        if (_p.Length != L.N) _p = new double[L.N];
        // 닫힌 문 칸은 벽처럼 (장이 기체를 비워 둔 칸 — 기압 0으로 읽으면 옆 벽이 한쪽이 진공이라고 착각한다)
        for (int i = 0; i < L.N; i++)
            _p[i] = (L.Kind[i] == CellKind.Floor || L.Kind[i] == CellKind.Door && F.DoorPassable(L.DoorAt[i]) || F.Hole[i] >= 2) ? F.Pressure(i) : -1;
        Doors(sim, dt);
        Walls(sim, dt);
        if (sim.Tick % 25 == 0) Rust(sim, 25 * dt);
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
        else baseKPa = L.Hull[c] ? HullRatingKPa : WallRatingKPa;
        double strength = Math.Sqrt(Math.Max(0.01, m.Strength / 250.0));
        return baseKPa * strength * HeatFactor(c, m) * (1 - Fatigue[c]) * (1 - Corrode[c]) * (Braced[c] != 0 ? BraceFactor : 1);
    }

    private void Walls(Sim sim, double dt)
    {
        for (int c = 0; c < L.N; c++)
        {
            var kind = L.Kind[c];
            if (kind != CellKind.Wall && !(kind == CellKind.Door && !F.DoorPassable(L.DoorAt[c]))) { Stress[c] = 0; continue; }
            if (F.Hole[c] >= 2) { Stress[c] = 0; continue; }
            double dp = DeltaP(c);
            if (dp < 1) { Stress[c] = 0; continue; }
            double rating = Math.Max(1e-3, Rating(c));
            double load = dp / rating;
            Stress[c] = (float)load;
            var m = T.Materials[L.Material[c]].Def;
            // 피로: 피로 한계를 넘은 부하의 세제곱에 비례
            double nominal = dp / Math.Max(1e-3, rating / Math.Max(1e-3, (1 - Fatigue[c]) * (1 - Corrode[c])));
            if (nominal > m.Fatigue)
            {
                double x = (nominal - m.Fatigue) / (1 - m.Fatigue);
                Fatigue[c] = (float)Math.Min(1, Fatigue[c] + FatigueK * x * x * x * dt);
                if (DamageCause[c] == 0) DamageCause[c] = sim.Causes.Add(CauseKind.Pressure, "압력 차가 벽을 밀었다", sim.Tick, c, LowSideCause(c));
                // 끼익: 버티는 문 · 벽이 소리를 낸다 (사람 · 컴퓨터가 듣고 알아챈다)
                if (load >= 0.6 && (sim.Tick + c) % 10 == 0)
                    sim.Find<Sound>()?.Emit(sim, NearestAir(c), (float)(50 + 30 * Math.Min(1, load)), SoundKind.Creak, DamageCause[c]);
            }
            if (kind == CellKind.Door)
            {
                if (load >= 1) BlowDoor(sim, c, load);
                continue;
            }
            if (load >= 1.6) Open(sim, c, 2.5f, Cause(sim, c, "벽이 압력을 못 이겨 터졌다"), FactKind.WallBreach);
            else if (load >= 1) Open(sim, c, (float)Math.Max(F.Hole[c], Math.Min(2.0, 0.02 + 0.5 * (load - 1))), Cause(sim, c, "벽이 압력을 못 이겨 찢어졌다"), FactKind.WallBreach);
            else if (load >= 0.85 && F.Hole[c] < CrackArea) Open(sim, c, (float)CrackArea, Cause(sim, c, "벽에 금이 갔다"), FactKind.WallCrack);
        }
    }

    private int Cause(Sim sim, int c, string text)
    {
        var m = T.Materials[L.Material[c]].Def;
        int parent = DamageCause[c];
        if (HeatFactor(c, m) < 0.8) parent = sim.Causes.Add(CauseKind.Heat, "뜨거워 약해졌다", sim.Tick, c, parent);
        else if (Corrode[c] > 0.2) parent = sim.Causes.Add(CauseKind.Corrosion, "녹이 슬어 얇아졌다", sim.Tick, c, parent);
        return sim.Causes.Add(CauseKind.Breach, text, sim.Tick, c, parent);
    }

    /// <summary>벽에 구멍 (금 · 구멍 · 통째로) — 장에 알리고 사실 · 소리.</summary>
    private void Open(Sim sim, int c, float area, int cause, FactKind fact)
    {
        if (area <= F.Hole[c]) return;
        bool first = F.Hole[c] <= 0;
        F.SetHole(c, area);
        DamageCause[c] = cause;
        if (fact == FactKind.WallCrack) Cracks++; else if (first) Breaches++;
        if (first || area >= 2) sim.Facts.Emit(sim.Tick, fact, c, area, cause);
        sim.Find<Sound>()?.Emit(sim, NearestAir(c), fact == FactKind.WallCrack ? 85 : 120, fact == FactKind.WallCrack ? SoundKind.Crack : SoundKind.Bang, cause);
    }

    private void BlowDoor(Sim sim, int c, double load)
    {
        int d = L.DoorAt[c];
        DoorsBlown++;
        int cause = sim.Causes.Add(CauseKind.Pressure, "문이 압력을 못 이겨 날아갔다", sim.Tick, c, DamageCause[c]);
        DoorJam[d] = 1;
        DamageCause[c] = cause;
        F.SetDoor(d, load > 1.5 ? 1f : 0.35f);
        sim.Facts.Emit(sim.Tick, FactKind.DoorBlown, c, (float)load, cause);
        sim.Find<Sound>()?.Emit(sim, NearestAir(c), 125, SoundKind.Bang, cause);
    }

    private void Doors(Sim sim, double dt)
    {
        _life ??= sim.Find<Life>();
        for (int d = 0; d < L.Doors.Count; d++)
        {
            float now = F.DoorOpen[d], target = DoorTarget[d];
            bool opening = now <= 0.001f && target > now && DoorJam[d] == 0;
            if (F.DoorBleed[d] != 0 && !opening) F.SetBleed(d, false);
            if (DoorJam[d] != 0 || Math.Abs(now - target) < 1e-4) continue;
            int c = L.Doors[d].Cell;
            // 닫힌 문을 열 때: 양쪽 압력 차를 이겨야 한다
            if (now <= 0.001f && target > now)
            {
                double limit = DoorPowered[d] != 0 ? 30 : 2;
                if (DeltaP(c) > limit)
                {
                    // 전동 문은 먼저 압력 맞춤 밸브로 양쪽 기압을 맞춘다 — 안전할 때만
                    bool ok = DoorPowered[d] != 0 && SafeToBleed(d);
                    if (ok && F.DoorBleed[d] == 0) Bleeds++;
                    F.SetBleed(d, ok);
                    continue;
                }
            }
            float step = (float)(0.5 * dt);
            F.SetDoor(d, target > now ? Math.Min(target, now + step) : Math.Max(target, now - step));
        }
    }

    /// <summary>
    /// 압력 맞춤 밸브를 열어도 되나:
    /// 바깥으로 빼는 문(에어락 바깥문)은 그 방의 다른 문이 모두 닫혀 있고(맞물림) 우주복 없이 살아 있는 사람이 없을 때만 —
    /// 안쪽 문은 낮은 쪽 구역이 우주로 트여 있지 않을 때만 (트였으면 높은 쪽 공기까지 우주로 빠진다 — 구멍을 먼저 막아야 한다).
    /// </summary>
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
            if (room < 0) return false;
            foreach (int e in RoomDoors(room)) if (e != d && (F.DoorOpen[e] > 0 || DoorTarget[e] > 0)) return false;
            if (_life != null)
                foreach (var body in _life.Bodies)
                    if (body.Alive != 0 && body.Suit == 0 && body.Cell >= 0 && L.Room[body.Cell] == room) return false;
            return true;
        }
        int region = lo < F.RegionOf.Length ? F.RegionOf[lo] : -1;
        if (region < 0) return false;
        foreach (var k in F.Links) if (k.RB < 0 && k.RA == region && k.Area > 0) return false;
        return true;
    }

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
        w.I64(Cracks); w.I64(Breaches); w.I64(Impacts); w.I64(DoorsBlown); w.F64(RustedO2); w.Arr(ImpactJ); w.I64(Bleeds);
    }

    public void Load(StateReader r)
    {
        r.ArrInto(Fatigue); r.ArrInto(Corrode); r.ArrInto(Stress); r.ArrInto(DamageCause); r.ArrInto(DoorTarget); r.ArrInto(DoorJam); r.ArrInto(DoorPowered); r.ArrInto(Braced);
        Cracks = r.I64(); Breaches = r.I64(); Impacts = r.I64(); DoorsBlown = r.I64(); RustedO2 = r.F64(); r.ArrInto(ImpactJ); Bleeds = r.I64();
    }
}
