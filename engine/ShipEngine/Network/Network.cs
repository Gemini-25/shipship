namespace ShipEngine;

/// <summary>장치의 부품 (마모가 쌓여 숨은 한계를 넘으면 고장).</summary>
public enum Part : byte { Electric, Mechanical, Seal, Filter }

/// <summary>장치 하나 (도면의 설비 + 장치 표의 역할).</summary>
public sealed class Device
{
    public int Id;            // 장치 번호
    public int Furniture;     // 설비 번호
    public int Cell;          // 대표 칸
    public int Room;
    public string Type = "";
    public string Name = "";
    public DeviceDef Def = null!;
    public int Feeder = -1;   // 전기를 받는 급전선
    public bool Has(Part p) => Def.Parts.ContainsKey(PartName(p));
    public double Life(Part p) => Def.Parts.TryGetValue(PartName(p), out var h) ? h : 0;
    public static string PartName(Part p) => p switch { Part.Electric => "전기", Part.Mechanical => "기계", Part.Seal => "봉함", _ => "거르개" };
}

/// <summary>급전선: 배전반 → 방. 전선은 실제 칸들을 지나간다 (젖거나 타면 합선 · 과부하면 그 칸들이 뜨거워진다).</summary>
public sealed class Feeder
{
    public int Id, Panel, Room;
    public int[] Path = Array.Empty<int>();
    public double RatingA = 60;
}

/// <summary>
/// ⑥ 망 · 장치 — 전기 회로 · 냉각 · 공조와 덕트 · 물 · 장치의 일과 고장 (docs/ENGINE.md §4 ⑥).
/// <para>
/// <b>전기</b>: 원자로 · 보조 발전기 · 배터리 → 주 모선 → 배전반 → 방마다 급전선(400V) → 장치 · 조명.
/// 급전선은 배전반에서 그 방까지 실제 칸을 지나간다. 전류가 정격을 넘으면 5초 뒤 차단기가 떨어지고(과부하),
/// 전선이 지나는 칸이 젖거나(물 20몰) 겉면이 470K를 넘으면(피복이 녹음) 합선 — 차단기가 곧장 떨어지며 그 칸에 불꽃.
/// 차단기가 낡아 붙어 버렸으면 아크가 계속되어 불이 난다. 공급이 모자라면 모두 조금씩 덜 받는다 (브라운아웃).
/// </para>
/// <para>
/// <b>원자로 · 냉각</b>: 원자로는 낸 전력의 두 배를 열로 낸다. 냉각 펌프(봉함 · 기계)가 그 열과 공조의 열을 우주로 버린다.
/// 냉각이 모자라 원자로 칸이 700K를 넘으면 스스로 멈춘다(스크램) → 배터리 → 정전. 펌프 봉함이 새면 암모니아가 그 칸으로.
/// </para>
/// <para>
/// <b>공조 · 덕트</b>: 방마다 통풍구 칸이 있고, 팬이 돌면 통풍구 공기의 6%를 매 틱 모아 고르게 돌려준다 — 그래서 연기 · 이산화탄소가 덕트로 배 전체에 퍼진다
/// (댐퍼를 닫으면 그 방은 빠진다). 방 온도는 293K로 맞추되 냉각 용량만큼.
/// </para>
/// <para>
/// <b>장치의 일</b>: 산소 발생기(물 전기분해 — 수소는 배 밖으로, 봉함이 새면 그 칸으로) · 이산화탄소 제거기(거르개) · 제습기(수증기 → 물탱크) ·
/// 조리대(켜 두면 겉면이 달궈진다) · 배터리(뜨거우면 열폭주 → 불).
/// </para>
/// <para>
/// <b>고장은 상태에서</b>: 부품마다 숨은 한계(와이블 · 시드에서 정해짐)가 있고, 마모 = 가동 시간 × 열(전기는 10K마다 두 배) × 습기(봉함) × 부하.
/// 기계 부품은 한계의 85%를 넘으면 끽끽 소리가 난다 (사람 · 진동 감시기가 알아챌 수 있다).
/// </para>
/// </summary>
public sealed class Network : IEngine
{
    public string Name => "망";
    public Phase Phase => Phase.Network;
    public int Every => 1;
    public int Offset => 0;

    public const double Volts = 400;
    public const double CableOhmPerM = 1.1e-3;  // 16mm² 구리
    public const double ArcOhm = 0.05;
    public const double SetpointK = 293.15;
    public const double VentFraction = 0.06;    // 통풍구 공기를 매 틱 이만큼 (방 공기가 한 시간에 여러 번 바뀐다)

    private readonly Field F;
    private readonly Layout L;
    private readonly Tables T;
    private readonly ulong _seed;
    public readonly List<Device> Devices = new();
    public readonly List<Feeder> Feeders = new();
    /// <summary>방 → 급전선 번호 (-1: 없음).</summary>
    public readonly int[] RoomFeeder;
    /// <summary>방 → 통풍구 칸.</summary>
    public readonly int[] Vent;

    // ── 상태 (장치마다) ──
    public byte[] On;
    public float[] Supply;        // 받은 비율 0~1
    public float[] Wear;          // [장치 × 4 + 부품] 시간
    public byte[] Broken;         // [장치 × 4 + 부품]
    public float[] Charge;        // 배터리 kWh · 보조 발전기 연료 kWh
    // ── 상태 (급전선마다) ──
    public byte[] Breaker;        // 1: 붙어 있음
    public float[] OverSec;       // 정격을 넘은 채 지난 초
    public int[] FaultCell;       // 합선 칸 (-1)
    public byte[] BreakerStuck;   // 1: 낡아 떨어지지 않는다
    public float[] Amps;
    // ── 상태 (방마다) ──
    public byte[] Damper;         // 1: 열림
    // ── 배 전체 ──
    public bool ReactorScram;
    public int ScramCause;
    public double WaterTank;      // 물 (몰)
    public double Coolant;        // 냉각 고리의 암모니아 (몰)
    public double VentedH2;       // 배 밖으로 버린 수소 (몰 · 원소 장부)
    public double Supplied, Demanded, CoolingKw, ReactorKw, HvacKw;
    public long Trips, Shorts, Failures;

    private readonly float[] _limit; // 부품마다 숨은 한계 (시드에서 — 찍지 않는다)

    public Network(Field f, ulong seed)
    {
        F = f; L = f.L; T = f.T; _seed = seed;
        foreach (var fu in L.Furniture)
        {
            var def = T.Dev(fu.Type);
            if (def == null || fu.Cells.Length == 0) continue;
            Devices.Add(new Device { Id = Devices.Count, Furniture = fu.Id, Cell = fu.Cells[0], Room = fu.Room, Type = fu.Type, Name = T.Equip(fu.Type).Name, Def = def });
        }
        int nd = Devices.Count;
        On = Devices.Select(d => (byte)(d.Def.On ? 1 : 0)).ToArray();
        Supply = new float[nd]; Wear = new float[nd * 4]; Broken = new byte[nd * 4];
        Charge = Devices.Select(d => (float)d.Def.Kwh).ToArray();
        _limit = new float[nd * 4];
        for (int d = 0; d < nd; d++)
            for (int p = 0; p < 4; p++)
            {
                double life = Devices[d].Life((Part)p);
                if (life <= 0) { _limit[d * 4 + p] = float.MaxValue; continue; }
                // 와이블(모양 2): 수명 × √(−ln u)
                double u = Math.Max(1e-9, Rng.Unit(seed, Rng.Key("부품 한계"), d, p));
                _limit[d * 4 + p] = (float)(life * Math.Sqrt(-Math.Log(u)) * 1.13);
            }
        RoomFeeder = Enumerable.Repeat(-1, L.Rooms.Count).ToArray();
        Vent = new int[L.Rooms.Count];
        BuildFeeders();
        int nf = Feeders.Count;
        Breaker = Enumerable.Repeat((byte)1, nf).ToArray();
        OverSec = new float[nf]; FaultCell = Enumerable.Repeat(-1, nf).ToArray(); BreakerStuck = new byte[nf]; Amps = new float[nf];
        Damper = Enumerable.Repeat((byte)1, L.Rooms.Count).ToArray();
        WaterTank = 800 * 55.5;      // 물 800L
        Coolant = 200_000 / 17.03;   // 암모니아 200kg
    }

    /// <summary>방마다 가까운 배전반 → 급전선 (전선이 지나는 칸).</summary>
    private void BuildFeeders()
    {
        var panels = Devices.Where(d => d.Def.Role == "panel").ToList();
        if (panels.Count == 0) panels = Devices.Where(d => d.Def.Role == "reactor").ToList();
        // 방마다 통풍구: 방 칸들의 가운데에 가장 가까운 칸
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            var cells = L.Rooms[r].Cells;
            if (cells.Length == 0) { Vent[r] = -1; continue; }
            double cx = cells.Average(c => (double)L.X(c)), cy = cells.Average(c => (double)L.Y(c));
            Vent[r] = cells.OrderBy(c => (L.X(c) - cx) * (L.X(c) - cx) + (L.Y(c) - cy) * (L.Y(c) - cy)).ThenBy(c => c).First();
        }
        if (panels.Count == 0) return;
        // 배전반마다 거리장 (바닥 · 문 칸을 따라)
        var dist = new List<int[]>(); var prev = new List<int[]>();
        foreach (var p in panels)
        {
            var d = Enumerable.Repeat(-1, L.N).ToArray(); var pv = Enumerable.Repeat(-1, L.N).ToArray();
            var q = new Queue<int>(); d[p.Cell] = 0; q.Enqueue(p.Cell);
            Span<int> nb = stackalloc int[4];
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                L.Neighbors(c, nb);
                foreach (int j in nb)
                    if (j >= 0 && d[j] < 0 && L.IsSpace(j)) { d[j] = d[c] + 1; pv[j] = c; q.Enqueue(j); }
            }
            dist.Add(d); prev.Add(pv);
        }
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int v = Vent[r];
            if (v < 0) continue;
            int best = -1;
            for (int k = 0; k < panels.Count; k++)
                if (dist[k][v] >= 0 && (best < 0 || dist[k][v] < dist[best][v])) best = k;
            if (best < 0) continue;
            var path = new List<int>();
            for (int c = v; c >= 0; c = prev[best][c]) path.Add(c);
            path.Reverse();
            var fd = new Feeder { Id = Feeders.Count, Panel = panels[best].Id, Room = r, Path = path.ToArray() };
            Feeders.Add(fd);
            RoomFeeder[r] = fd.Id;
        }
        foreach (var d in Devices) d.Feeder = d.Room >= 0 ? RoomFeeder[d.Room] : -1;
        // 전선 굵기는 그 방에 달린 장치를 다 켰을 때의 1.5배로 (배 설계자가 하듯) — 많아야 60A보다 작지는 않다
        foreach (var fd in Feeders)
        {
            double kw = Devices.Where(d => d.Feeder == fd.Id).Sum(d => d.Def.Kw);
            fd.RatingA = Math.Max(60, Math.Ceiling(kw * 1.5 * 1000 / Volts / 10) * 10);
        }
    }

    // ───────────────────────── 요청 ─────────────────────────

    public void SetOn(int device, bool on) => On[device] = (byte)(on ? 1 : 0);
    public void ResetBreaker(int feeder) { Breaker[feeder] = 1; OverSec[feeder] = 0; }
    /// <summary>배전반에서 급전선을 손으로 끊는다 (눌어붙은 차단기도) — 합선을 잇는 전기가 멎는다.</summary>
    public void PullFeeder(int feeder) { Breaker[feeder] = 0; BreakerStuck[feeder] = 0; OverSec[feeder] = 0; }
    /// <summary>합선이 났는데 차단기가 눌어붙어 전기가 계속 흐르는 급전선 (없으면 -1).</summary>
    public int StuckFault(int panel = -1)
    {
        for (int f = 0; f < Feeders.Count; f++)
            if (FaultCell[f] >= 0 && Breaker[f] != 0 && BreakerStuck[f] != 0 && (panel < 0 || Feeders[f].Panel == panel)) return f;
        return -1;
    }
    public int FaultCause(int feeder) => _faultCause.GetValueOrDefault(feeder);
    public bool IsStuckFault(int f) => f >= 0 && f < Feeders.Count && FaultCell[f] >= 0 && Breaker[f] != 0 && BreakerStuck[f] != 0;
    public void SetDamper(int room, bool open) => Damper[room] = (byte)(open ? 1 : 0);
    public bool IsBroken(int device, Part p) => Broken[device * 4 + (int)p] != 0;
    public float WearFraction(int device, Part p) => _limit[device * 4 + (int)p] == float.MaxValue ? 0 : Wear[device * 4 + (int)p] / _limit[device * 4 + (int)p];
    public bool Working(int d) => On[d] != 0 && Broken[d * 4 + (int)Part.Electric] == 0 && Broken[d * 4 + (int)Part.Mechanical] == 0 && Supply[d] > 0.05f;
    public bool RoomPowered(int room) => room >= 0 && RoomFeeder[room] >= 0 && Breaker[RoomFeeder[room]] != 0 && Supplied > 0;

    /// <summary>부품을 낡게 한다 (이야기꾼이 '낡은 부품'이라는 원인을 심는다 · 시험) — 숨은 한계의 fraction까지.</summary>
    public void Age(int device, Part p, double fraction, int cause = 0)
    {
        int k = device * 4 + (int)p;
        if (_limit[k] == float.MaxValue) return;
        Wear[k] = (float)Math.Max(Wear[k], _limit[k] * fraction);
        if (cause != 0) _wearCause[k] = cause;
    }
    /// <summary>부품을 숨은 한계까지 hoursLeft시간(보통 쓰임 기준)만 남게 낡게 한다 — 이야기꾼의 '낡은 부품'.</summary>
    public void AgeLeft(int device, Part p, double hoursLeft, int cause = 0)
    {
        int k = device * 4 + (int)p;
        if (_limit[k] == float.MaxValue) return;
        Wear[k] = (float)Math.Max(Wear[k], _limit[k] - Math.Max(0.01, hoursLeft));
        if (cause != 0) _wearCause[k] = cause;
    }
    private readonly Dictionary<int, int> _wearCause = new(); // 낡게 한 원인 (부품 → 원인 마디) — 고장이 그 뿌리에 이어진다

    /// <summary>부품을 고친다 (사람 · 로봇의 수리).</summary>
    public void Repair(int device, Part p) { Broken[device * 4 + (int)p] = 0; Wear[device * 4 + (int)p] = 0; _wearCause.Remove(device * 4 + (int)p); }

    // ───────────────────────── 한 틱 ─────────────────────────

    public void Step(Sim sim, double dt)
    {
        Faults(sim);
        Power(sim, dt);
        Effects(sim, dt);
        Cooling(sim, dt);
        Ducts(sim);
        if (sim.Tick % 25 == 0) WearStep(sim, 25 * dt);
    }

    /// <summary>합선 찾기: 급전선이 지나는 칸이 젖었거나 겉면이 470K를 넘으면.</summary>
    private void Faults(Sim sim)
    {
        foreach (var fd in Feeders)
        {
            if (FaultCell[fd.Id] >= 0) continue;
            foreach (int c in fd.Path)
            {
                bool wet = F.Stored(T.sWater, c) > 20;
                bool burnt = F.Tf[c] > 470;
                if (!wet && !burnt) continue;
                FaultCell[fd.Id] = c;
                Shorts++;
                int parent = burnt ? sim.Causes.Add(CauseKind.Heat, "전선 피복이 뜨거워 녹았다", sim.Tick, c) : sim.Causes.Add(CauseKind.Leak, "전선이 지나는 바닥이 젖었다", sim.Tick, c);
                int cause = sim.Causes.Add(CauseKind.Electric, "합선", sim.Tick, c, parent);
                sim.Facts.Emit(sim.Tick, FactKind.Short, c, 0, cause, other: fd.Id);
                _faultCause[fd.Id] = cause;
                break;
            }
        }
    }

    private readonly Dictionary<int, int> _faultCause = new();

    private void Power(Sim sim, double dt)
    {
        // 수요 (급전선마다)
        var load = new double[Feeders.Count];
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            if (dev.Feeder < 0 || On[d] == 0 || Broken[d * 4] != 0) continue;
            if (dev.Def.Role is "reactor" or "aux" or "battery") continue;
            load[dev.Feeder] += dev.Def.Kw;
        }
        for (int r = 0; r < L.Rooms.Count; r++)
            if (RoomFeeder[r] >= 0) load[RoomFeeder[r]] += 0.01 * L.Rooms[r].Cells.Length; // 조명 10W/m²
        double demand = 0;
        for (int k = 0; k < Feeders.Count; k++) if (Breaker[k] != 0) demand += load[k];
        // 공급
        double reactorMax = 0, auxMax = 0, battMax = 0;
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            bool ok = On[d] != 0 && Broken[d * 4] == 0;
            if (dev.Def.Role == "reactor" && ok && !ReactorScram) reactorMax += dev.Def.Gen;
            if (dev.Def.Role == "aux" && ok && Charge[d] > 0) auxMax += dev.Def.Gen;
            if (dev.Def.Role == "battery" && ok && Charge[d] > 0) battMax += dev.Def.Kw;
        }
        double avail = reactorMax + auxMax;
        double battCap = 0, battRate = 0;
        for (int d = 0; d < Devices.Count; d++)
            if (Devices[d].Def.Role == "battery" && On[d] != 0 && Broken[d * 4] == 0)
            {
                battCap += Devices[d].Def.Kwh;
                if (Charge[d] < Devices[d].Def.Kwh - 1e-3) battRate += Devices[d].Def.Kw; // 다 찬 배터리는 충전하지 않는다
            }
        double gen, fromBatt = 0, charging = 0;
        if (demand <= avail)
        {
            gen = demand;
            charging = Math.Min(avail - demand, battRate); // 남는 것으로 충전
        }
        else
        {
            gen = avail;
            fromBatt = Math.Min(battMax, demand - avail);
        }
        double supplied = gen + fromBatt;
        double frac = demand > 1e-9 ? Math.Min(1, supplied / demand) : 1;
        Supplied = supplied; Demanded = demand;
        // 원자로가 먼저, 모자라면 보조 발전기
        ReactorKw = Math.Min(reactorMax, gen + charging);
        double auxKw = Math.Max(0, gen + charging - ReactorKw);
        double battDelta = fromBatt > 0 ? -fromBatt : charging; // kW (+ 충전)
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            if (dev.Def.Role == "battery" && battCap > 0 && On[d] != 0 && Broken[d * 4] == 0)
            {
                double share = dev.Def.Kwh / battCap;
                double before = Charge[d];
                Charge[d] = (float)Math.Clamp(Charge[d] + battDelta * share * dt / 3600, 0, dev.Def.Kwh);
                if (Charge[d] != before) F.AddHeat(dev.Cell, Math.Abs(battDelta * share) * 1000 * 0.05 * dt); // 충방전 손실
            }
            if (dev.Def.Role == "aux" && auxMax > 0) Charge[d] = (float)Math.Max(0, Charge[d] - auxKw * dev.Def.Gen / auxMax * dt / 3600);
        }
        // 급전선 전류 · 차단기 · 전선 열 · 합선 아크
        for (int k = 0; k < Feeders.Count; k++)
        {
            var fd = Feeders[k];
            if (Breaker[k] == 0) { Amps[k] = 0; continue; }
            double amps = load[k] * frac * 1000 / Volts;
            int fc = FaultCell[k];
            if (fc >= 0 && supplied > 0)
            {
                // 합선: 차단기가 곧장 떨어진다 — 낡아 붙어 있으면 아크가 계속된다
                double arcKw = Math.Min(200, Math.Max(0, reactorMax + auxMax + battMax - supplied) + 20);
                var chem = (Chemistry?)null;
                chem = sim.Find<Chemistry>();
                int cause = _faultCause.GetValueOrDefault(k);
                if (BreakerStuck[k] == 0)
                {
                    chem?.Ignite(fc, 150_000, cause); // 떨어지기 전 아크 한 번 (불꽃)
                    Trip(sim, k, cause, "합선으로 차단기가 떨어졌다");
                    continue;
                }
                chem?.Ignite(fc, arcKw * 1000 * dt, cause);
                sim.Find<Sound>()?.Emit(sim, fc, 95, SoundKind.Machine, cause);
                amps = Volts / (CableOhmPerM * fd.Path.Length + ArcOhm);
            }
            Amps[k] = (float)amps;
            if (amps > fd.RatingA * 1.25)
            {
                OverSec[k] += (float)dt;
                if (OverSec[k] > 5 && BreakerStuck[k] == 0)
                {
                    int cause = sim.Causes.Add(CauseKind.Electric, "과부하", sim.Tick, fd.Path.Length > 0 ? fd.Path[0] : -1);
                    Trip(sim, k, cause, "과부하로 차단기가 떨어졌다");
                    continue;
                }
            }
            else OverSec[k] = 0;
            // 전선 열: I²R (칸마다 1m)
            double w = amps * amps * CableOhmPerM;
            if (w > 1) foreach (int c in fd.Path) F.AddSurfaceHeat(c, w * dt);
        }
        // 장치마다 받은 비율
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            bool fed = dev.Feeder >= 0 && Breaker[dev.Feeder] != 0;
            Supply[d] = (float)(On[d] != 0 && Broken[d * 4] == 0 && fed ? frac : 0);
            if (dev.Def.Role is "reactor" or "aux" or "battery") Supply[d] = On[d] != 0 ? 1 : 0;
            else if (dev.Def.Kwh > 0 && On[d] != 0 && Broken[d * 4] == 0)
            {
                // 자체 축전지(무정전): 전기가 끊기면 제 전지로 버티고, 들어오면 다시 채운다
                if (Supply[d] < 0.5f && Charge[d] > 0) { Supply[d] = 1; Charge[d] = (float)Math.Max(0, Charge[d] - dev.Def.Kw * dt / 3600); }
                else if (Supply[d] >= 0.5f && Charge[d] < dev.Def.Kwh) Charge[d] = (float)Math.Min(dev.Def.Kwh, Charge[d] + dev.Def.Kw * 0.2 * dt / 3600);
            }
        }
        // 조명 · 문 동력
        var light = sim.Find<Light>();
        if (light != null)
            for (int r = 0; r < L.Rooms.Count; r++)
            {
                bool lit = RoomFeeder[r] >= 0 && Breaker[RoomFeeder[r]] != 0 && frac > 0.05;
                float emergency = Devices.Any(x => x.Room == r && x.Type == "EmergencyLight") ? 30 : 0;
                light.SetLamp(r, lit ? (float)(300 * Math.Min(1, frac)) : emergency);
            }
        var st = sim.Find<Structure>();
        if (st != null)
            for (int dr = 0; dr < L.Doors.Count; dr++)
            {
                int c = L.Doors[dr].Cell;
                bool powered = false;
                Span<int> nb = stackalloc int[4];
                L.Neighbors(c, nb);
                foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0 && RoomPowered(L.Room[j])) powered = true;
                st.DoorPowered[dr] = (byte)(powered ? 1 : 0);
            }
    }

    private void Trip(Sim sim, int feeder, int cause, string text)
    {
        Breaker[feeder] = 0; OverSec[feeder] = 0; Trips++;
        int c = sim.Causes.Add(CauseKind.Electric, text, sim.Tick, Feeders[feeder].Path.Length > 0 ? Feeders[feeder].Path[0] : -1, cause);
        sim.Facts.Emit(sim.Tick, FactKind.BreakerTrip, Feeders[feeder].Path.Length > 0 ? Feeders[feeder].Path[0] : -1, 0, c, other: feeder);
        FaultCell[feeder] = -1; // 차단기를 다시 올렸을 때 아직 젖어 있으면 다시 찾는다
    }

    /// <summary>장치의 일: 열 · 산소 · 이산화탄소 · 물 · 조리대 · 배터리 열폭주 · 샘.</summary>
    private void Effects(Sim sim, double dt)
    {
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            double p = Supply[d];
            int c = dev.Cell;
            bool sealLeak = Broken[d * 4 + (int)Part.Seal] != 0;
            bool filterBad = Broken[d * 4 + (int)Part.Filter] != 0;
            bool mechOk = Broken[d * 4 + (int)Part.Mechanical] == 0;
            double kw = dev.Def.Kw * p * (mechOk ? 1 : 0.1);
            switch (dev.Def.Role)
            {
                case "reactor":
                    if (ReactorKw > 0) F.AddSolidHeat(c, ReactorKw * 2 * 1000 * dt); // 열효율 1/3
                    break;
                case "o2gen":
                {
                    // 물 전기분해: O2 몰/초 = 전력 × 0.7 / 572kJ — 저장된 화학 에너지만큼은 열이 아니다
                    double o2 = kw * 1000 * 0.7 / 572_000 * dt;
                    o2 = Math.Min(o2, WaterTank / 2);
                    if (o2 > 0)
                    {
                        WaterTank -= 2 * o2;
                        F.AddGas(c, T.gO2, o2, F.Temp(c));
                        if (sealLeak) F.AddGas(c, T.gH2, 2 * o2, F.Temp(c)); else VentedH2 += 2 * o2;
                        F.AddHeat(c, kw * 1000 * 0.3 * dt);
                    }
                    // 이산화탄소 거르기 (공기 재생기 — 옛 배의 '산소 발생기'는 한 대로 둘 다 했다 · 거르개가 닳으면 1/5)
                    if (dev.Def.Mol > 0 && p > 0) ScrubRoom(dev, T.gCO2, dev.Def.Mol * (filterBad ? 0.2 : 1) * dt);
                    break;
                }
                case "scrubber":
                {
                    ScrubRoom(dev, T.gCO2, dev.Def.Mol * (p > 0 && mechOk ? 1 : 0) * (filterBad ? 0.2 : 1) * dt);
                    F.AddHeat(c, kw * 1000 * dt);
                    break;
                }
                case "dehumid":
                {
                    double take = dev.Def.Mol * (p > 0 && mechOk ? 1 : 0) * dt;
                    double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(F.Temp(c)));
                    if (rh > 0.4) WaterTank += F.RemoveGas(c, T.gH2O, Math.Min(take, F.Gas(T.gH2O, c) * 0.3));
                    F.AddHeat(c, kw * 1000 * dt);
                    break;
                }
                case "recycler":
                    if (sealLeak) { double leak = Math.Min(WaterTank, 0.05 * dt); WaterTank -= leak; F.AddStore(c, T.sWater, leak); }
                    F.AddHeat(c, kw * 1000 * dt);
                    break;
                case "pump":
                case "hvac":
                    if (sealLeak && Coolant > 0)
                    {
                        double leak = Math.Min(Coolant, 0.05 * dt);
                        Coolant -= leak;
                        F.AddGas(c, T.gNH3, leak, F.Temp(c));
                    }
                    F.AddHeat(c, kw * 1000 * 0.3 * dt);
                    break;
                case "stove":
                    if (kw > 0) F.AddSurfaceHeat(c, kw * 1000 * 0.6 * dt); // 겉면을 달군다 (그 위 음식 · 기름)
                    F.AddHeat(c, kw * 1000 * 0.4 * dt);
                    break;
                case "battery":
                    if (F.Tf[c] > 430 || F.Ts[c] > 430)
                    {
                        // 열폭주: 저장된 에너지가 한꺼번에 열로
                        if (Charge[d] > 0.5)
                        {
                            double e = Charge[d] * 3.6e6;
                            Charge[d] = 0;
                            Broken[d * 4] = 1;
                            int cause = sim.Causes.Add(CauseKind.Heat, "배터리가 뜨거워져 열폭주", sim.Tick, c);
                            sim.Find<Chemistry>()?.Ignite(c, Math.Min(e, 8e6), cause);
                            sim.Facts.Emit(sim.Tick, FactKind.MachineFail, c, (float)(e / 1e6), cause, other: d);
                        }
                    }
                    break;
                default:
                    if (kw > 0) F.AddHeat(c, kw * 1000 * dt);
                    break;
            }
        }
    }

    /// <summary>냉각: 펌프 용량만큼 원자로 칸 · 공조의 열을 우주로. 모자라면 원자로가 뜨거워진다 → 스크램.</summary>
    private void Cooling(Sim sim, double dt)
    {
        double cap = 0;
        for (int d = 0; d < Devices.Count; d++)
            if (Devices[d].Def.Role == "pump" && Working(d) && Coolant > 1000) cap += Devices[d].Def.HeatKw * Supply[d];
        // 공조: 냉각 고리에 딸린 방 열교환 · 팬 (펌프마다 6kW) + 따로 단 열교환기
        double hvacCap = 0;
        for (int d = 0; d < Devices.Count; d++)
        {
            if (Devices[d].Def.Role == "pump" && Working(d) && Coolant > 1000) hvacCap += 6 * Supply[d];
            if (Devices[d].Def.Role == "hvac" && Working(d)) hvacCap += Devices[d].Def.HeatKw * Supply[d];
        }
        HvacKw = hvacCap;
        CoolingKw = cap;
        // 원자로 칸: 정상 온도(330K) 위의 열을 냉각 용량까지 뺀다
        double used = 0;
        foreach (var dev in Devices)
        {
            if (dev.Def.Role != "reactor") continue;
            int c = dev.Cell;
            double excessJ = Math.Max(0, F.Ts[c] - 330) * F.Cs[c];
            double take = Math.Min(excessJ, Math.Max(0, cap - used) * 1000 * dt);
            F.AddSolidHeat(c, -take);
            used += take / 1000 / dt;
            if (!ReactorScram && F.Ts[c] > 700)
            {
                ReactorScram = true;
                ScramCause = sim.Causes.Add(CauseKind.Heat, "냉각이 모자라 원자로가 스스로 멈췄다", sim.Tick, c);
                sim.Facts.Emit(sim.Tick, FactKind.Overheat, c, (float)F.Ts[c], ScramCause);
                sim.Find<Sound>()?.Emit(sim, c, 100, SoundKind.Alarm, ScramCause);
            }
        }
        // 방 온도를 293K로: 방마다 필요한 만큼 (식힘 +, 덥힘 − · 덥힘은 전기 히터라 냉각 용량과 상관없다) — 식힘이 남은 용량을 넘으면 나눈다
        if (hvacCap <= 0) return;
        double left = Math.Min(Math.Max(0, cap - used), hvacCap);
        var need = new double[L.Rooms.Count];
        double coolSum = 0;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int v = Vent[r];
            if (v < 0 || !RoomPowered(r) || Damper[r] == 0 || F.RegionOf[v] < 0 || F.Moles(v) < 1) continue;
            double t = F.Temp(v);
            need[r] = Math.Clamp(0.3 * L.Rooms[r].Cells.Length * (t - SetpointK) / 10, -3, 15);
            if (need[r] > 0) coolSum += need[r];
        }
        double scale = coolSum > left ? left / coolSum : 1;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (need[r] == 0) continue;
            double kw = need[r] > 0 ? need[r] * scale : need[r];
            F.AddHeat(Vent[r], -kw * 1000 * dt);
        }
    }

    /// <summary>덕트: 팬이 돌면 열린 통풍구마다 공기의 6%를 모아 고르게 돌려준다 (보존).</summary>
    private void Ducts(Sim sim)
    {
        if (HvacKw <= 0) return; // 팬이 돌지 않는다 (냉각 고리 · 열교환기가 모두 섰다)
        var vents = new List<int>();
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int v = Vent[r];
            if (v >= 0 && Damper[r] != 0 && RoomPowered(r) && F.RegionOf[v] >= 0 && F.Moles(v) > 1) vents.Add(v);
        }
        if (vents.Count < 2) return;
        // 압력 차단판: 기압이 다른 방들보다 15% 넘게 낮은 방의 통풍구는 저절로 닫힌다 (뚫린 방으로 배 전체 공기가 덕트를 타고 빠지지 않게)
        var ps = vents.Select(v => F.Pressure(v)).OrderBy(x => x).ToList();
        double median = ps[ps.Count / 2];
        vents.RemoveAll(v => F.Pressure(v) < 0.85 * median);
        if (vents.Count < 2) return;
        int G = F.G;
        var pool = new double[G];
        double poolU = 0;
        foreach (int v in vents)
        {
            for (int g = 0; g < G; g++) { double t = F.N[v * G + g] * VentFraction; pool[g] += t; F.N[v * G + g] -= t; }
            double du = F.U[v] * VentFraction; poolU += du; F.U[v] -= du;
        }
        foreach (int v in vents)
        {
            for (int g = 0; g < G; g++) F.N[v * G + g] += pool[g] / vents.Count;
            F.U[v] += poolU / vents.Count;
        }
        F.MarkDirtyAll();
    }

    /// <summary>마모 (25틱마다): 가동 시간 × 열 × 습기 × 부하 → 숨은 한계를 넘으면 고장.</summary>
    private void WearStep(Sim sim, double dt)
    {
        double hours = dt / 3600;
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            int c = dev.Cell;
            bool running = On[d] != 0 && Supply[d] > 0.05f;
            double temp = Math.Max(F.Temp(c), F.Ts[c]);
            double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(Math.Max(250, F.Temp(c))));
            for (int p = 0; p < 4; p++)
            {
                int k = d * 4 + p;
                if (_limit[k] == float.MaxValue || Broken[k] != 0) continue;
                double stress = running ? 1 : 0.15;
                stress *= (Part)p switch
                {
                    Part.Electric => Math.Pow(2, Math.Clamp((temp - 298) / 10, -2, 8)),
                    Part.Mechanical => 1 + Math.Max(0, temp - 298) / 50,
                    Part.Seal => (1 + Math.Max(0, rh)) * (1 + Math.Max(0, temp - 298) / 80),
                    _ => 1,
                };
                Wear[k] += (float)(hours * stress);
                double frac = Wear[k] / _limit[k];
                if ((Part)p == Part.Mechanical && frac > 0.85 && running && sim.Tick % 125 == 0)
                    sim.Find<Sound>()?.Emit(sim, c, 68, SoundKind.Machine, 0); // 끽끽 — 곧 망가진다
                if (frac >= 1) Fail(sim, d, (Part)p);
            }
        }
    }

    /// <summary>송풍기로 방 공기를 돌려 기체 하나를 걷는다 — 방 칸마다 가진 만큼 나눠서 (장치 칸만 비우지 않게).</summary>
    private void ScrubRoom(Device dev, int gas, double mol)
    {
        if (mol <= 0) return;
        var cells = dev.Room >= 0 && dev.Room < L.Rooms.Count && L.Rooms[dev.Room].Cells.Length > 0 ? L.Rooms[dev.Room].Cells : new[] { dev.Cell };
        double total = 0;
        foreach (int c in cells) total += F.Gas(gas, c);
        if (total <= 1e-12) return;
        double frac = Math.Min(0.5, mol / total);
        foreach (int c in cells) { double g = F.Gas(gas, c); if (g > 0) F.RemoveGas(c, gas, g * frac); }
    }

    private void Fail(Sim sim, int d, Part p)
    {
        var dev = Devices[d];
        Broken[d * 4 + (int)p] = 1;
        Failures++;
        string what = p switch { Part.Electric => "회로", Part.Mechanical => "모터", Part.Seal => "봉함", _ => "거르개" };
        int cause = sim.Causes.Add(CauseKind.Wear, $"{dev.Name}의 {Ko.I(what)} 낡아 망가졌다", sim.Tick, dev.Cell, _wearCause.GetValueOrDefault(d * 4 + (int)p));
        sim.Facts.Emit(sim.Tick, FactKind.MachineFail, dev.Cell, (int)p, cause, other: d);
        if (p == Part.Electric && dev.Feeder >= 0 && Rng.Unit(_seed, Rng.Key("회로 고장 합선"), sim.Tick, d) < 0.25)
        {
            FaultCell[dev.Feeder] = dev.Cell;
            _faultCause[dev.Feeder] = sim.Causes.Add(CauseKind.Electric, "망가진 회로에서 합선", sim.Tick, dev.Cell, cause);
            Shorts++;
        }
        if (p == Part.Mechanical) sim.Find<Sound>()?.Emit(sim, dev.Cell, 85, SoundKind.Machine, cause);
    }

    public void Save(StateWriter w)
    {
        w.Arr(On); w.Arr(Supply); w.Arr(Wear); w.Arr(Broken); w.Arr(Charge);
        w.Arr(Breaker); w.Arr(OverSec); w.Arr(FaultCell); w.Arr(BreakerStuck); w.Arr(Amps); w.Arr(Damper);
        w.Bool(ReactorScram); w.I32(ScramCause); w.F64(WaterTank); w.F64(Coolant); w.F64(VentedH2);
        w.F64(Supplied); w.F64(Demanded); w.F64(CoolingKw); w.F64(ReactorKw); w.F64(HvacKw);
        w.I64(Trips); w.I64(Shorts); w.I64(Failures);
        var keys = _faultCause.Keys.OrderBy(k => k).ToList();
        w.I32(keys.Count);
        foreach (var k in keys) { w.I32(k); w.I32(_faultCause[k]); }
        var wk = _wearCause.Keys.OrderBy(k => k).ToList();
        w.I32(wk.Count);
        foreach (var k in wk) { w.I32(k); w.I32(_wearCause[k]); }
    }

    public void Load(StateReader r)
    {
        r.ArrInto(On); r.ArrInto(Supply); r.ArrInto(Wear); r.ArrInto(Broken); r.ArrInto(Charge);
        r.ArrInto(Breaker); r.ArrInto(OverSec); r.ArrInto(FaultCell); r.ArrInto(BreakerStuck); r.ArrInto(Amps); r.ArrInto(Damper);
        ReactorScram = r.Bool(); ScramCause = r.I32(); WaterTank = r.F64(); Coolant = r.F64(); VentedH2 = r.F64();
        Supplied = r.F64(); Demanded = r.F64(); CoolingKw = r.F64(); ReactorKw = r.F64(); HvacKw = r.F64();
        Trips = r.I64(); Shorts = r.I64(); Failures = r.I64();
        _faultCause.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++) { int k = r.I32(); _faultCause[k] = r.I32(); }
        _wearCause.Clear();
        n = r.I32();
        for (int i = 0; i < n; i++) { int k = r.I32(); _wearCause[k] = r.I32(); }
    }
}
