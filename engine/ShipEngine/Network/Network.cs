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
public sealed partial class Network : IEngine
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
    public int[] SealLeakCause;   // [장치] 봉함이 망가져 냉매가 새기 시작한 원인 마디 (고치면 0)
    public float[] PanK, PanWater; // [장치] 조리대 위 냄비 온도 (K) · 냄비 속 물 (몰) — 켜 두면 졸고 · 눌고 · 불이 붙는다
    public int[] OnCause;         // [장치] 켠 원인 (켜 둔 조리대의 사람 — 그 열로 불이 나면 불의 뿌리가 된다 · 끄면 0)
    public float[] Charge;        // 배터리 kWh · 보조 발전기 연료 kWh
    // ── 상태 (급전선마다) ──
    public byte[] Breaker;        // 1: 붙어 있음
    public float[] OverSec;       // 정격을 넘은 채 지난 초
    public int[] FaultCell;       // 합선 칸 (-1)
    public byte[] BreakerStuck;   // 1: 낡아 떨어지지 않는다
    /// <summary>⑫ 자연 사건: 합선 전류를 끊다 접점이 녹아 상한 차단기 (원인 마디 · 0 멀쩡) — 다시 올리면 접점이 눌어붙어 다음 합선에 떨어지지 않는다.</summary>
    public int[] BreakerWeld = Array.Empty<int>();
    /// <summary>합선 전류를 한 번 끊을 때 접점이 상할 몫 (기능 확인용 첫 값 — 맨 끝에 다듬는다 · 세계마다 · 찍지 않는다).</summary>
    public double WeldChance = 0.05;
    /// <summary>[장치] 배전반 주 차단기(윗단 보호)도 낡아 듣지 않는다 — 그 배전반 회로의 눌어붙은 합선은 손으로 끊어야 한다 (겹겹 보호의 셋째 겹).</summary>
    public byte[] MainStuck = Array.Empty<byte>();
    public float[] Amps;
    // ── 상태 (방마다) ──
    public byte[] Damper;         // 1: 열림
    /// <summary>1: 정화 배기 — 그 방 덕트를 배 덕트에서 떼어 정화기(활성탄 · 촉매)로만 돌린다 (독한 기체가 다른 방으로 가지 않고 그 방에서 걸러진다).</summary>
    public byte[] Exhaust;
    public double TraceScrubbed;  // 정화기가 걸러 낸 독한 기체 (몰 — 암모니아 · 일산화탄소 · 오존)
    // ── 배 전체 ──
    public bool ReactorScram;
    public int ScramCause;
    public double WaterTank;      // 물 (몰)
    public double WasteWater;     // 오수 (몰 — 오줌 · 씻은 물) — 재활용 장치가 마실 물로 되돌린다
    public double Brine;          // 버린 소금물의 물 (몰 — 장부)
    public const double WaterRecovery = 0.94, RecyclerMolPerSec = 0.025; // 장치 하나 1.6L/시 · 새 거르개의 회수율 (낡을수록 0.85까지)
    public double WaterProcessed, WaterRecovered;   // 거른 오수 · 되찾은 물 (몰 — 회수율을 잰다)
    /// <summary>냉각 펌프 하나에 딸린 방 열교환 · 팬 (kW) — 전기가 모두 방 공기의 열이 되니 배 전력만큼은 걷어 내야 한다.</summary>
    public const double HvacShare = 1.0 / 3; // 펌프 냉각 용량의 1/3은 그 고리에 딸린 방 열교환 · 팬 몫 (90kW 펌프 → 30kW)
    /// <summary>예비 공기 (몰 · 질소 79% 산소 21%) — 큰 감압 뒤 배를 다시 채울 압축 공기. 배 전체를 반 번 채울 만큼 싣고 떠난다.</summary>
    public double AirReserve;
    /// <summary>예비 공기를 지금 푸는 빠르기 (몰/초) — 주컴퓨터가 정한다 (생명유지 설비 자리로 나온다).</summary>
    public double AirRelease;
    public double[] RoomAir = Array.Empty<double>();   // ⑩이 요청한 방마다 긴급 공기 (몰/초 — 그 방 통풍구로)
    public double AirReleased;
    /// <summary>⑥ 공기 되돌리기 (판단 엔진 — 임시 에어락): RoomAir가 음수인 방은 통풍구 펌프가 그 방 공기를 빨아 예비 탱크로 되돌린다 (칸마다 이 몰/초 · 20kPa 아래로는 못 뺀다 · 공조가 돌고 그 방에 전기가 있어야).
    /// 질소 · 산소 · 아르곤만 탱크로 — 다른 기체는 거르개가 버린다. ISS 퀘스트 에어락의 감압 펌프처럼 느리다.</summary>
    public const double PumpMolPerCellSec = 0.05, PumpFloorKPa = 20;
    public double AirRecovered;
    public double Coolant;        // 냉각 고리의 암모니아 (몰)
    public double VentedH2;       // 배 밖으로 버린 수소 (몰 · 원소 장부)
    public double VentedCH4, VentedCO2; // 배 밖으로 버린 메탄(사바티에) · 이산화탄소(모은 것이 넘쳐서) (몰 · 원소 장부)
    /// <summary>정화기가 걸러 모은 이산화탄소 (몰) — 사바티에가 수소와 섞어 물로 되찾는다 · 넘치면 배 밖으로 (ISS 이산화탄소 제거 장치 → 사바티에).</summary>
    public double CO2Store;
    public const double CO2StoreMax = 200;
    /// <summary>산소 발생기의 일 몫 (0~1) — 배 공기의 산소 분압에 맞춰 스스로 조절한다 (ISS 산소 발생 장치처럼 사람 수에 맞춘 만큼만 · 물을 아낀다).</summary>
    public float[] Duty;
    /// <summary>배의 평균 산소 분압 (kPa — 가압된 방 칸 · 25틱마다 잰다).</summary>
    public double PpO2;
    public const double CoilK = 283.15;              // 공조 열교환판 온도 (이슬점 — ISS 공조는 4~16℃)
    public const double O2MaxFraction = 0.34;
    public const double O2Target = 21.0, O2Band = 0.5; // 이 분압 아래에서 돌기 시작 · 0.5kPa 아래면 다 돈다 (떠날 때 공기 21.0kPa · ISS 19.5~23.1kPa)
    public double Supplied, Demanded, CoolingKw, ReactorKw, HvacKw;
    /// <summary>지금 돌 수 있는 원자로들의 낼 수 있는 전력 합 (원자로 열을 나눌 때 — 틱마다 다시 센다).</summary>
    public double ReactorMaxKw;
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
        Supply = new float[nd]; Duty = Enumerable.Repeat(1f, nd).ToArray(); Wear = new float[nd * 4]; Broken = new byte[nd * 4]; SealLeakCause = new int[nd]; OnCause = new int[nd]; PanK = Enumerable.Repeat(293.15f, nd).ToArray(); PanWater = new float[nd];
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
        BuildData();
        BuildLines();
        BuildWear();
        BuildCells();
        BuildPipes();
        int nf = Feeders.Count;
        Breaker = Enumerable.Repeat((byte)1, nf).ToArray();
        OverSec = new float[nf]; FaultCell = Enumerable.Repeat(-1, nf).ToArray(); BreakerStuck = new byte[nf]; BreakerWeld = new int[nf]; Amps = new float[nf];
        MainStuck = new byte[Devices.Count];
        Damper = Enumerable.Repeat((byte)1, L.Rooms.Count).ToArray();
        Exhaust = new byte[L.Rooms.Count];
        WaterTank = (L.WaterL > 0 ? L.WaterL : 800) * 55.5;      // 물 (도면에 적힌 L — 없으면 800L)
        int inside = L.Rooms.Sum(r => r.Cells.Length);
        AirReserve = inside * Field.CellVolume * 101_325 / (Field.R * 293.15) * 0.5;
        Coolant = (L.CoolantKg > 0 ? L.CoolantKg : 200) * 1000 / 17.03;   // 암모니아 (도면에 적힌 kg — 없으면 200kg)
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

    public void SetOn(int device, bool on) { On[device] = (byte)(on ? 1 : 0); if (!on) OnCause[device] = 0; }
    public void ResetBreaker(int feeder) { Breaker[feeder] = 1; OverSec[feeder] = 0; if (BreakerWeld[feeder] != 0) BreakerStuck[feeder] = 1; }   // 상한 접점은 올리는 순간 눌어붙는다
    /// <summary>배전반에서 급전선을 손으로 끊는다 (눌어붙은 차단기도) — 합선을 잇는 전기가 멎는다.</summary>
    public void PullFeeder(int feeder) { Breaker[feeder] = 0; BreakerStuck[feeder] = 0; OverSec[feeder] = 0; }
    /// <summary>이 칸에 전기를 대는 살아 있는 급전선 (② (바)): 전기를 받는 장치가 차지한 칸 · 살아 있는 급전선이 지나는 칸 — 없으면 −1.</summary>
    public int LiveFeederAt(int cell)
    {
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            if (dev.Feeder < 0 || dev.Furniture < 0 || On[d] == 0 || Supply[d] <= 0.05f || Breaker[dev.Feeder] == 0) continue;
            if (Array.IndexOf(L.Furniture[dev.Furniture].Cells, cell) >= 0) return dev.Feeder;
        }
        for (int k = 0; k < Feeders.Count; k++)
            if (Breaker[k] != 0 && !Cut(k) && (FaultCell[k] == cell || Array.IndexOf(Feeders[k].Path, cell) >= 0)) return k;   // 합선 아크가 튀는 칸도
        return -1;
    }

    /// <summary>이 칸에 전기가 흐르나 — 살아 있는 급전선 · 전기를 받는 장치 · 급전선 없이 전기를 내는 것(배전반 · 원자로 · 배터리).</summary>
    public bool LiveAt(int cell)
    {
        if (LiveFeederAt(cell) >= 0) return true;
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            if (dev.Feeder >= 0 || dev.Furniture < 0 || dev.Def.Role is not ("panel" or "reactor" or "battery" or "aux")) continue;
            if (On[d] != 0 && Broken[d * 4 + (int)Part.Electric] == 0 && Array.IndexOf(L.Furniture[dev.Furniture].Cells, cell) >= 0) return true;
        }
        return false;
    }
    /// <summary>합선이 났는데 차단기가 눌어붙어 전기가 계속 흐르는 급전선 (없으면 -1).</summary>
    public int StuckFault(int panel = -1)
    {
        for (int f = 0; f < Feeders.Count; f++)
            if (FaultCell[f] >= 0 && Breaker[f] != 0 && BreakerStuck[f] != 0 && (panel < 0 || Feeders[f].Panel == panel)) return f;
        return -1;
    }
    public int FaultCause(int feeder) => _faultCause.GetValueOrDefault(feeder);
    /// <summary>지금 낼 수 있는 전력 (kW): 돌고 있는 원자로 · 보조 발전기.</summary>
    public double Capacity()
    {
        double cap = 0;
        foreach (var d in Devices)
            if (d.Def.Role is "reactor" or "aux" && On[d.Id] != 0 && Broken[d.Id * 4] == 0 && !(d.Def.Role == "reactor" && ReactorScram)) cap += d.Def.Gen;
        return cap;
    }
    public bool IsStuckFault(int f) => f >= 0 && f < Feeders.Count && FaultCell[f] >= 0 && Breaker[f] != 0 && BreakerStuck[f] != 0;
    public void SetDamper(int room, bool open) => Damper[room] = (byte)(open ? 1 : 0);
    public void SetExhaust(int room, bool on) => Exhaust[room] = (byte)(on ? 1 : 0);
    /// <summary>급전선 길에 젖은(물 5몰 — 합선 문턱 20몰보다 넉넉히: 번지는 물이 문턱을 오르내린다) · 뜨거운(430K) 칸이 있나 — 다시 올려도 또 합선된다.</summary>
    public bool PathFaulty(int feeder)
    {
        if (FeederLineCut(feeder)) return true;   // 탄 · 찢긴 전선은 식어도 다시 쓸 수 없다 — 이어야 한다
        foreach (int c in Feeders[feeder].Path) if (F.Stored(T.sWater, c) > 5 || F.Tf[c] > 430) return true;
        return false;
    }
    /// <summary>독한 기체를 거르는 정화기(이산화탄소 정화기 · 공기 재생기의 활성탄 거르개)가 돌고 있나.</summary>
    public bool ScrubberWorking()
    {
        for (int d = 0; d < Devices.Count; d++) if (Devices[d].Def.Role is "scrubber" or "o2gen" && Working(d) && Broken[d * 4 + (int)Part.Filter] == 0) return true;
        return false;
    }
    /// <summary>
    /// 낡은 기계의 소리 (dB — 바퀴 2 소리): 기계 부품의 마모가 숨은 한계의 60%를 넘으면 커진다 — 12 × ((마모 − 0.6)/0.4)² (0.9에서 +6.8 · 끝에서 +12).
    /// 베어링 고장 3단계부터 귀에 들리고 · ISO 10816 영역 A → D 진동 속도가 네 배 (소리 힘 +12dB). 기계 부품이 없거나 멈췄으면 0.
    /// </summary>
    public float WornDb(int d)
    {
        int k = d * 4 + (int)Part.Mechanical;
        if (_limit[k] == float.MaxValue || Broken[k] != 0) return 0;
        double f = Math.Min(1, Wear[k] / _limit[k]);
        return f <= 0.6 ? 0 : (float)(12 * Math.Pow((f - 0.6) / 0.4, 2));
    }
    /// <summary>돌아가는 장치가 1m에서 내는 소리 (dB — 표의 크기 + 낡은 몫 · 멈췄으면 0).</summary>
    public float NoiseDb(int d) => Devices[d].Def.Db > 0 && Working(d) ? (float)Devices[d].Def.Db + WornDb(d) : 0;
    /// <summary>끽끽 · 덜컹을 내기 시작하는 낡은 몫 (dB — 마모 0.8 남짓).</summary>
    public const float GrindFromDb = 3;
    public bool IsBroken(int device, Part p) => Broken[device * 4 + (int)p] != 0;
    public float WearFraction(int device, Part p) => _limit[device * 4 + (int)p] == float.MaxValue ? 0 : Wear[device * 4 + (int)p] / _limit[device * 4 + (int)p];
    public bool Working(int d) => On[d] != 0 && Broken[d * 4 + (int)Part.Electric] == 0 && Broken[d * 4 + (int)Part.Mechanical] == 0 && Supply[d] > 0.05f;
    public bool RoomPowered(int room) { int f = RoomFeed(room); return f >= 0 && Breaker[f] != 0 && !Cut(f) && FeederSupplied(f) > 0; }

    // ── 배가 갈라지면 (④ 조각): 끊긴 칸을 건너는 급전선 · 떨어진 조각의 배전반 · 원천은 배의 전기에서 빠진다 ──
    private Structure? _stc;
    private bool[] _cut = Array.Empty<bool>();
    private int _cutVersion = -1;
    /// <summary>이 급전선이 배가 갈라져 끊겼나 (길이 떨어져 나간 칸을 건너거나 배전반이 떨어진 조각에 있다 — 탄 · 찢긴 전선은 끊김이 아니라 합선: Faults).</summary>
    public bool Cut(int feeder) => feeder >= 0 && feeder < _cut.Length && _cut[feeder];
    /// <summary>이 장치가 배(떨어지지 않은 쪽)에 있나.</summary>
    private bool OnShip(int d) => _stc == null || _stc.Splits == 0 || _stc.InMain(Devices[d].Cell);
    private void UpdateCuts()
    {
        if (_stc == null || _stc.SplitVersion == _cutVersion) return;
        _cutVersion = _stc.SplitVersion;
        if (_cut.Length != Feeders.Count) _cut = new bool[Feeders.Count];
        for (int k = 0; k < Feeders.Count; k++)
            _cut[k] = !_stc.InMain(Devices[Feeders[k].Panel].Cell) || Feeders[k].Path.Any(c => F.Severed[c] != 0 || !_stc.InMain(c));
    }

    /// <summary>부품을 낡게 한다 (시험 · 만들 때 남은 흠 — 원인 마디를 함께 준다) — 숨은 한계의 fraction까지.</summary>
    public void Age(int device, Part p, double fraction, int cause = 0)
    {
        int k = device * 4 + (int)p;
        if (_limit[k] == float.MaxValue) return;
        Wear[k] = (float)Math.Max(Wear[k], _limit[k] * fraction);
        if (cause != 0) _wearCause[k] = cause;
    }
    /// <summary>부품을 숨은 한계까지 hoursLeft시간(보통 쓰임 기준)만 남게 낡게 한다 — 시험의 '낡은 부품'.</summary>
    public void AgeLeft(int device, Part p, double hoursLeft, int cause = 0)
    {
        int k = device * 4 + (int)p;
        if (_limit[k] == float.MaxValue) return;
        Wear[k] = (float)Math.Max(Wear[k], _limit[k] - Math.Max(0.01, hoursLeft));
        if (cause != 0) _wearCause[k] = cause;
    }
    private readonly Dictionary<int, int> _wearCause = new(); // 낡게 한 원인 (부품 → 원인 마디) — 고장이 그 뿌리에 이어진다
    /// <summary>그 부품을 낡게 · 망가지게 한 원인 마디 (없으면 0).</summary>
    public int PartCause(int device, Part p) => _wearCause.GetValueOrDefault(device * 4 + (int)p);

    /// <summary>부품을 고친다 (사람 · 로봇의 수리).</summary>
    public void Repair(int device, Part p) { Broken[device * 4 + (int)p] = 0; Wear[device * 4 + (int)p] = 0; _wearCause.Remove(device * 4 + (int)p); ClearWear(device * 4 + (int)p); if (p == Part.Seal) SealLeakCause[device] = 0; if (device < Stripped.Length) Stripped[device] = 0; }

    // ───────────────────────── 한 틱 ─────────────────────────

    /// <summary>망 안 단계별 시간 (실험실 --profile — 켜 두면 잰다).</summary>
    public static Dictionary<string, double>? Prof;
    private static long Lap(string name, long t0)
    {
        if (Prof == null) return 0;
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        Prof[name] = Prof.GetValueOrDefault(name) + (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return t1;
    }

    public void Step(Sim sim, double dt)
    {
        long t = Prof != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        _stc ??= sim.Find<Structure>();
        UpdateCuts();
        EnsureLines();
        LinkInsulation(sim);
        if (sim.Tick % 25 == 0) { AgeO2Sensor(sim, 25 * dt); MeasureO2(); }
        Faults(sim);
        t = Lap("고장", t);
        Power(sim, dt);
        t = Lap("전력", t);
        Effects(sim, dt);
        GravityStep(sim, dt);   // 인공 중력 — 몸마다 바닥 중력 (③ 장에 요청)
        t = Lap("장치 일", t);
        Cooling(sim, dt);
        t = Lap("냉각 · 공조", t);
        Ducts(sim);
        t = Lap("덕트", t);
        ReleaseAir(dt);
        LeakStep(sim, dt);
        if (sim.Tick % 25 == 0) WearStep(sim, 25 * dt);
        if (sim.Tick % 5 == 0) LineStep(sim);
        ChipStep(sim);          // 판단 이관 (사): 계산 장치의 칩 열 — 느려짐 · 열 보호
        RobotStep(sim, dt);     // 판단 이관 (사): 로봇 몸 — 전지 · 열 · 충격 · 물 · 전기 · 진공
        Lap("마모", t);
    }

    /// <summary>배의 평균 산소 분압 → 산소 발생기 일 몫 (비례 조절). 조절 센서로 잰다 — 늙으면 낮게 읽어 더 만든다 (② (마) <see cref="O2Gain"/>).</summary>
    private void MeasureO2()
    {
        double sum = 0; int n = 0;
        foreach (var room in L.Rooms)
            foreach (int c in room.Cells)
                if (F.Pressure(c) > 50) { sum += F.Partial(T.gO2, c); n++; }
        PpO2 = n > 0 ? sum / n : 0;
        float duty = n > 0 ? (float)Math.Clamp((O2Target - O2Reading) / O2Band, 0, 1) : 1;
        // 산소 비율 상한: 기압이 낮아진 배에서 분압을 맞추느라 산소 비율이 불붙기 쉬운 데까지 오르지 않게 (NASA 탐사 대기 56.5kPa · 34% — 그 위로는 멈춘다)
        double frac = 0; int m = 0;
        foreach (var room in L.Rooms) foreach (int c in room.Cells) { double pk = F.Pressure(c); if (pk > 50) { frac += F.Partial(T.gO2, c) / pk; m++; } }
        if (m > 0 && frac / m * O2Gain > O2MaxFraction) duty = 0;
        for (int d = 0; d < Devices.Count; d++) if (Devices[d].Def.Role == "o2gen") Duty[d] = duty;
    }

    /// <summary>예비 공기를 생명유지 설비(산소 발생기) 자리로 푼다 — 없으면 첫 방 통풍구로.</summary>
    private void ReleaseAir(double dt)
    {
        for (int r = 0; r < RoomAir.Length && r < Vent.Length; r++)
        {
            if (RoomAir[r] < 0) { DrawAir(r, -RoomAir[r] * dt); continue; }
            if (RoomAir[r] <= 0 || AirReserve <= 0 || Vent[r] < 0 || !F.Passable(Vent[r])) continue;
            double m = Math.Min(RoomAir[r] * dt, AirReserve);
            F.AddGas(Vent[r], T.gN2, m * 0.79, 293.15); F.AddGas(Vent[r], T.gO2, m * 0.21, 293.15);
            AirReserve -= m; AirReleased += m;
        }
        if (AirRelease <= 0 || AirReserve <= 0) return;
        double n = Math.Min(AirRelease * dt, AirReserve);
        var at = new List<int>();
        foreach (var dev in Devices) if (dev.Def.Role == "o2gen" && F.Passable(dev.Cell) && OnShip(dev.Id)) at.Add(dev.Cell);
        if (at.Count == 0) for (int r = 0; r < Vent.Length && at.Count == 0; r++) if (Vent[r] >= 0 && F.Passable(Vent[r])) at.Add(Vent[r]);
        if (at.Count == 0) return;
        foreach (int c in at)
        {
            F.AddGas(c, T.gN2, n * 0.79 / at.Count, 293.15);
            F.AddGas(c, T.gO2, n * 0.21 / at.Count, 293.15);
        }
        AirReserve -= n; AirReleased += n;
    }

    /// <summary>그 방 공기를 want몰까지 예비 탱크로 되돌린다 (방 칸마다 고르게 · 20kPa에서 멈춘다).</summary>
    private void DrawAir(int r, double want)
    {
        int v = Vent[r];
        if (v < 0 || HvacKw <= 0 || !RoomPowered(r) || F.RegionOf.Length <= v || F.RegionOf[v] < 0) return;
        var cells = L.Rooms[r].Cells;
        double total = 0;
        foreach (int c in cells) total += F.Moles(c);
        double kpa = F.Pressure(v);
        if (total <= 0 || kpa <= PumpFloorKPa) return;
        want = Math.Min(want, total * (1 - PumpFloorKPa / kpa));
        double f = want / total, got = 0;
        foreach (int c in cells)
            for (int g = 0; g < T.GasCount; g++)
            {
                double n = F.Gas(g, c);
                if (n <= 0) continue;
                double take = F.RemoveGas(c, g, n * f);
                if (g == T.gN2 || g == T.gO2 || g == T.gAr) got += take;
            }
        AirReserve += got; AirRecovered += got;
    }

    /// <summary>합선 찾기: 급전선이 지나는 칸이 젖었거나 겉면이 470K를 넘으면.</summary>
    private void Faults(Sim sim)
    {
        foreach (var fd in Feeders)
        {
            if (FaultCell[fd.Id] >= 0 || Breaker[fd.Id] == 0) continue; // 차단기가 내려간 회로에는 전류가 없다 — 다시 올렸을 때 찾는다
            foreach (int c in fd.Path)
            {
                bool wet = F.Stored(T.sWater, c) > 20;
                bool burnt = F.Tf[c] > 470;
                bool spent = (LineCut[c] & 1) != 0;   // 타 버리거나 찢긴 전선 (⑥ 바퀴 3) — 전기를 넣으면 드러난 구리끼리 닿아 합선
                if (!wet && !burnt && !spent) continue;
                FaultCell[fd.Id] = c;
                Shorts++;
                int parent = burnt ? sim.Causes.Add(CauseKind.Heat, "전선 피복이 뜨거워 녹았다", sim.Tick, c, HeatSource(sim.Find<Chemistry>(), c)) : wet ? sim.Causes.Add(CauseKind.Leak, F.Grav[c] < Field.LowG ? "떠다니던 물방울이 전선에 들러붙었다" : "전선이 지나는 바닥이 젖었다", sim.Tick, c, SpillNear(c))
                    : sim.Causes.Add(CauseKind.Electric, "상한 전선에 전기가 들어갔다", sim.Tick, c, LineCause[c * 3]);
                int cause = sim.Causes.Add(CauseKind.Electric, "합선", sim.Tick, c, parent);
                sim.Facts.Emit(sim.Tick, FactKind.Short, c, 0, cause, other: fd.Id);
                _faultCause[fd.Id] = cause;
                break;
            }
        }
    }

    private readonly Dictionary<int, int> _faultCause = new();

    private double[] _load = Array.Empty<double>(); // 틱 안에서만 (찍지 않는다)
    private void Power(Sim sim, double dt)
    {
        // 수요 (급전선마다)
        if (_load.Length != Feeders.Count) _load = new double[Feeders.Count];
        var load = _load; Array.Clear(load);
        FeedRooms();   // 방마다 지금 전기를 주는 급전선 (연장선이 있으면 옆 방 것)
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            int ef = FeedOf(dev);
            if (ef < 0 || On[d] == 0 || Broken[d * 4] != 0) continue;
            if (dev.Def.Role is "reactor" or "aux" or "battery") continue;
            if (_dropCut[d]) continue;   // 가지선이 끊겼다 — 그 장치로는 전류가 흐르지 않는다
            double kw = dev.Def.Kw * (dev.Def.Role == "o2gen" ? Math.Max(0.1, Duty[d]) : 1); // 산소 발생기는 일 몫만큼 (팬 · 정화는 늘 10%)
            load[ef] += kw;
            if (dev.Room >= 0) _roomLoad[dev.Room] += kw;
        }
        for (int r = 0; r < L.Rooms.Count; r++)
            if (_feed[r] >= 0) { double lk = 0.01 * L.Rooms[r].Cells.Length; load[_feed[r]] += lk; _roomLoad[r] += lk; } // 조명 10W/m²
        // 섬마다 (간선이 끊기면 배전반 · 원천이 갈린다 — 아무것도 안 끊기면 섬 하나)
        int ni = _nIsl;
        var dem = _iDemand; Array.Clear(dem);
        for (int k = 0; k < Feeders.Count; k++) if (Breaker[k] != 0 && !Cut(k)) dem[FeederIsland(k)] += load[k];
        // 공급
        var rMax = _iReactorMax; var aMax = _iAuxMax; var bMax = _iBattMax; var bCap = _iBattCap; var bRate = _iBattRate;
        Array.Clear(rMax); Array.Clear(aMax); Array.Clear(bMax); Array.Clear(bCap); Array.Clear(bRate);
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            int i = _isl[d];
            if (i < 0) continue;
            bool ok = On[d] != 0 && Broken[d * 4] == 0 && OnShip(d);
            if (dev.Def.Role == "reactor" && ok && !ReactorScram) rMax[i] += dev.Def.Gen;
            if (dev.Def.Role == "aux" && ok && Charge[d] > 0) aMax[i] += dev.Def.Gen;
            if (dev.Def.Role == "battery" && ok && Charge[d] > 0) bMax[i] += dev.Def.Kw;
        }
        for (int d = 0; d < Devices.Count; d++)
            if (Devices[d].Def.Role == "battery" && _isl[d] >= 0 && On[d] != 0 && Broken[d * 4] == 0 && OnShip(d))
            {
                bCap[_isl[d]] += Devices[d].Def.Kwh;
                if (Charge[d] < Devices[d].Def.Kwh - 1e-3) bRate[_isl[d]] += Devices[d].Def.Kw; // 다 찬 배터리는 충전하지 않는다
            }
        Supplied = 0; Demanded = 0; ReactorKw = 0; ReactorMaxKw = 0;
        for (int i = 0; i < ni; i++)
        {
            double avail = rMax[i] + aMax[i], demand = dem[i];
            double gen, fromBatt = 0, charging = 0;
            if (demand <= avail)
            {
                gen = demand;
                charging = Math.Min(avail - demand, bRate[i]); // 남는 것으로 충전
            }
            else
            {
                gen = avail;
                fromBatt = Math.Min(bMax[i], demand - avail);
            }
            double supplied = gen + fromBatt;
            _iSupplied[i] = supplied;
            _iFrac[i] = demand > 1e-9 ? Math.Min(1, supplied / demand) : 1;
            // 원자로가 먼저, 모자라면 보조 발전기
            _iReactorKw[i] = Math.Min(rMax[i], gen + charging);
            _iAuxKw[i] = Math.Max(0, gen + charging - _iReactorKw[i]);
            _iBattDelta[i] = fromBatt > 0 ? -fromBatt : charging; // kW (+ 충전)
            Supplied += supplied; Demanded += demand; ReactorKw += _iReactorKw[i]; ReactorMaxKw += rMax[i];
        }
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            int i = _isl[d];
            if (i < 0) continue;
            if (dev.Def.Role == "battery" && bCap[i] > 0 && On[d] != 0 && Broken[d * 4] == 0 && OnShip(d))
            {
                double share = dev.Def.Kwh / bCap[i];
                double before = Charge[d];
                Charge[d] = (float)Math.Clamp(Charge[d] + _iBattDelta[i] * share * dt / 3600, 0, dev.Def.Kwh);
                if (Charge[d] != before) F.AddHeat(dev.Cell, Math.Abs(_iBattDelta[i] * share) * 1000 * 0.05 * dt); // 충방전 손실
            }
            if (dev.Def.Role == "aux" && aMax[i] > 0) Charge[d] = (float)Math.Max(0, Charge[d] - _iAuxKw[i] * dev.Def.Gen / aMax[i] * dt / 3600);
        }
        // 급전선 전류 · 차단기 · 전선 열 · 합선 아크
        for (int k = 0; k < Feeders.Count; k++)
        {
            var fd = Feeders[k];
            if (Breaker[k] == 0 || Cut(k)) { Amps[k] = 0; continue; }
            int isl = FeederIsland(k);
            double frac = _iFrac[isl], supplied = _iSupplied[isl];
            double amps = load[k] * frac * 1000 / Volts;
            int fc = FaultCell[k];
            if (fc >= 0 && supplied > 0)
            {
                // 합선: 차단기가 곧장 떨어진다 — 낡아 붙어 있으면 아크가 계속된다
                double arcKw = Math.Min(200, Math.Max(0, rMax[isl] + aMax[isl] + bMax[isl] - supplied) + 20);
                var chem = (Chemistry?)null;
                chem = sim.Find<Chemistry>();
                int cause = _faultCause.GetValueOrDefault(k);
                if (BreakerStuck[k] == 0)
                {
                    chem?.Ignite(fc, 150_000, cause); // 떨어지기 전 아크 한 번 (불꽃)
                    Trip(sim, k, cause, "합선으로 차단기가 떨어졌다");
                    if (BreakerWeld[k] == 0 && Rng.Unit(_seed, Rng.Key("차단기 접점"), sim.Tick, k) < WeldChance)   // 큰 전류를 끊는 아크가 접점을 녹였다 (보이지 않는다)
                        BreakerWeld[k] = sim.Causes.Add(CauseKind.Electric, "합선 전류를 끊다 차단기 접점이 녹아 상했다", sim.Tick, fd.Path.Length > 0 ? fd.Path[0] : -1, cause);
                    continue;
                }
                chem?.Ignite(fc, arcKw * 1000 * dt, cause);
                sim.Find<Sound>()?.Emit(sim, fc, 95, SoundKind.Machine, cause);
                amps = Volts / (CableOhmPerM * fd.Path.Length + ArcOhm);
                // 윗단 보호 장치 (배전반 주 차단기 · 시간 지연 과전류): 아래 차단기가 눌어붙어도 20초 뒤 끊는다 — 그사이 아크가 그 자리에 불씨를 남긴다
                OverSec[k] += (float)dt;
                if (OverSec[k] > 20 && MainStuck[fd.Panel] == 0)
                {
                    Trip(sim, k, cause, "윗단 보호 장치가 회로를 끊었다 — 아래 차단기는 눌어붙어 있었다");
                    if (Rng.Unit(_seed, Rng.Key("주 차단기 접점"), sim.Tick, fd.Panel) < WeldChance) MainStuck[fd.Panel] = 1;   // 20초 아크를 끊은 접점도 상한다 — 다음엔 듣지 않는다 (보이지 않는다)
                    continue;
                }
            }
            Amps[k] = (float)amps;
            if (fc >= 0 && supplied > 0) { } // 합선은 위에서 셌다
            else if (amps > fd.RatingA * 1.25)
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
        JumperStep(sim, dt);   // 연장선: 전류 · 열 · 젖은 칸 · 합선 · 문틀
        // 장치마다 받은 비율
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            int ef = FeedOf(dev);
            bool fed = ef >= 0 && Breaker[ef] != 0 && !Cut(ef) && !_dropCut[d];
            Supply[d] = (float)(On[d] != 0 && Broken[d * 4] == 0 && fed ? _iFrac[FeederIsland(ef)] : 0);
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
                double frac = _feed[r] >= 0 ? _iFrac[FeederIsland(_feed[r])] : 0;
                bool lit = _feed[r] >= 0 && Breaker[_feed[r]] != 0 && !Cut(_feed[r]) && frac > 0.05;
                float emergency = EmergencyLit(r) ? 30 : 0;
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

    private bool[]? _emergency; // 방마다 비상등이 있나 (장치는 만들 때 정해진다)
    private bool EmergencyLit(int room)
    {
        if (_emergency == null)
        {
            _emergency = new bool[L.Rooms.Count];
            foreach (var x in Devices) if (x.Type == "EmergencyLight" && x.Room >= 0 && x.Room < _emergency.Length) _emergency[x.Room] = true;
        }
        return _emergency[room];
    }

    private void Trip(Sim sim, int feeder, int cause, string text)
    {
        Breaker[feeder] = 0; OverSec[feeder] = 0; Trips++;
        int c = sim.Causes.Add(CauseKind.Electric, text, sim.Tick, Feeders[feeder].Path.Length > 0 ? Feeders[feeder].Path[0] : -1, cause);
        sim.Facts.Emit(sim.Tick, FactKind.BreakerTrip, Feeders[feeder].Path.Length > 0 ? Feeders[feeder].Path[0] : -1, 0, c, other: feeder);
        FaultCell[feeder] = -1; // 차단기를 다시 올렸을 때 아직 젖어 있으면 다시 찾는다
    }

    /// <summary>장치의 일: 열 · 산소 · 이산화탄소 · 물 · 조리대 · 배터리 열폭주 · 샘.</summary>
    private readonly List<(int cell, double kw, int comp)> _jacket = new(); // 이번 틱 냉각수 재킷으로 간 열 (틱 안에서만 — 찍지 않는다)

    /// <summary>장치 열을 그 장치가 차지한 칸에 고루 (긴 선반 · 큰 펌프의 열이 한 칸에 몰리지 않게).</summary>
    private void HeatInto(Device dev, double joules)
    {
        var fc = L.Furniture[dev.Furniture].Cells;
        foreach (int x in fc) F.AddHeat(x, joules / fc.Length);
    }

    private void Effects(Sim sim, double dt)
    {
        _jacket.Clear();
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
                    // 열효율 1/3 — 낸 전력의 두 배가 노심 열. 원자로가 여럿이면 저마다 맡은 몫만큼 (다 합쳐 한 번)
                    if (_isl[d] is int ri && ri >= 0 && _iReactorKw[ri] > 0 && _iReactorMax[ri] > 0 && On[d] != 0 && Broken[d * 4] == 0 && !ReactorScram)
                        F.AddSolidHeat(c, _iReactorKw[ri] * dev.Def.Gen / _iReactorMax[ri] * 2 * 1000 * dt);
                    break;
                case "o2gen":
                {
                    // 물 전기분해: O2 몰/초 = 전력 × 일 몫 × 0.7 / 572kJ — 저장된 화학 에너지만큼은 열이 아니다 (일 몫: 배 산소 분압에 맞춰)
                    double duty = Duty[d];
                    double o2 = kw * duty * 1000 * 0.7 / 572_000 * dt;
                    o2 = Math.Min(o2, WaterTank / 2);
                    if (!WaterAtDevice(d)) o2 = 0;   // 물관이 끊겼다 — 가를 물이 오지 않는다
                    double h2 = 0;
                    if (o2 > 0)
                    {
                        WaterTank -= 2 * o2; sim.Book.Add(Res.Water, Flow.Split, -2 * o2);
                        F.AddGas(c, T.gO2, o2, F.Temp(c)); sim.Book.Add(Res.O2, Flow.Electrolysis, o2);
                        if (sealLeak) { F.AddGas(c, T.gH2, 2 * o2, F.Temp(c)); sim.Find<Chemistry>()?.MarkGas(c, SealLeakCause[d]); } else h2 = 2 * o2;
                    }
                    HeatInto(dev, kw * 1000 * (0.3 * duty + Math.Max(0, 0.1 - duty)) * dt);
                    // 사바티에 (ISS 2010~): CO₂ + 4H₂ → CH₄ + 2H₂O — 걸러 모은 이산화탄소와 갈라 낸 수소(90%)로 물을 되찾는다 · 메탄은 배 밖으로 · 반응열 165kJ/몰
                    if (h2 > 0 && mechOk)
                    {
                        double xi = Math.Min(0.9 * h2 / 4, CO2Store);
                        if (xi > 0)
                        {
                            CO2Store -= xi; h2 -= 4 * xi;
                            WaterTank += 2 * xi; sim.Book.Add(Res.Water, Flow.Sabatier, 2 * xi);
                            VentedCH4 += xi;
                            HeatInto(dev, 165e3 * xi);
                        }
                    }
                    VentedH2 += h2;
                    // 이산화탄소 거르기 (공기 재생기 — 옛 배의 '산소 발생기'는 한 대로 둘 다 했다 · 거르개가 닳으면 1/5)
                    if (dev.Def.Mol > 0 && p > 0) ScrubRoom(dev, T.gCO2, dev.Def.Mol * (filterBad ? 0.2 : 1) * dt);
                    // 독한 기체 거름 (활성탄 거르개)
                    if (p > 0 && !filterBad) TraceScrubbed += ScrubTrace(dev.Room >= 0 && dev.Room < L.Rooms.Count ? L.Rooms[dev.Room].Cells : new[] { dev.Cell }, Math.Min(0.5, 0.01 * dt));
                    break;
                }
                case "scrubber":
                {
                    ScrubRoom(dev, T.gCO2, dev.Def.Mol * (p > 0 && mechOk ? 1 : 0) * (filterBad ? 0.2 : 1) * dt);
                    // 독한 기체 거름 (활성탄 · 촉매 산화): 정화기 방 공기의 독한 기체를 초마다 1%씩 — 덕트가 배 공기를 이리로 돌린다
                    if (p > 0 && mechOk && !filterBad) TraceScrubbed += ScrubTrace(dev.Room >= 0 && dev.Room < L.Rooms.Count ? L.Rooms[dev.Room].Cells : new[] { dev.Cell }, Math.Min(0.5, 0.01 * dt));
                    HeatInto(dev, kw * 1000 * dt);
                    break;
                }
                case "dehumid":
                {
                    double take = dev.Def.Mol * (p > 0 && mechOk ? 1 : 0) * dt;
                    double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(F.Temp(c)));
                    if (rh > 0.4) { double got = F.RemoveGas(c, T.gH2O, Math.Min(take, F.Gas(T.gH2O, c) * 0.3)); WaterTank += got; sim.Book.Add(Res.Water, Flow.Condensed, got); }
                    HeatInto(dev, kw * 1000 * dt);
                    break;
                }
                case "recycler":
                {
                    // 봉함이 망가지면 오수 관이 샌다: 펌프가 돌면 압력으로 분에 0.5L(0.46몰/초) · 멈추면 스미는 만큼 (0.02몰/초)
                    if (sealLeak) { double leak = Math.Min(WaterTank, (p > 0 && mechOk ? 0.46 : 0.02) * dt); WaterTank -= leak; F.AddStore(c, T.sWater, leak); sim.Book.Add(Res.Water, Flow.Leaked, -leak); }
                    // 오수(오줌 · 씻은 물) → 증류 · 걸러 마실 물로 (ISS 물 회수 장치: 오줌 처리 약 85% + 소금물 처리 → 전체 94~98% — 여기는 94%) · 남은 진한 소금물은 버린다
                    if (p > 0 && mechOk && WasteWater > 0)
                    {
                        // 거르개가 낡을수록 막혀 덜 흐르고 · 소금물을 덜 짜내 회수율이 94% → 85% (오줌 처리만 — ISS 첫 물 회수 장치) · 망가지면 30%만 흐른다
                        double wear = Math.Clamp(WearFraction(dev.Id, Part.Filter), 0, 1);
                        double rec = filterBad ? 0.85 : WaterRecovery - 0.09 * wear;
                        double n = Math.Min(WasteWater, RecyclerMolPerSec * (filterBad ? 0.3 : 1 - 0.5 * wear * wear) * dt);
                        WasteWater -= n; WaterTank += n * rec; Brine += n * (1 - rec); sim.Book.Add(Res.Water, Flow.Recycled, n * rec);
                        WaterProcessed += n; WaterRecovered += n * rec;
                    }
                    HeatInto(dev, kw * 1000 * dt);
                    break;
                }
                case "pump":
                case "hvac":
                    if (sealLeak && Coolant > 0)
                    {
                        double leak = Math.Min(Coolant, 0.05 * dt);
                        Coolant -= leak;
                        F.AddGas(c, T.gNH3, leak, F.Temp(c));
                        sim.Find<Chemistry>()?.MarkGas(c, SealLeakCause[d]);   // 암모니아도 불붙는다 (15~28%) — 터지면 그 샘이 뿌리
                    }
                    HeatInto(dev, kw * 1000 * 0.1 * dt); // 모터 겉면 열 10%만 방으로 — 나머지는 펌프가 미는 냉각수가 가져간다
                    break;
                case "computer":
                    // 주컴퓨터 · 항법 컴퓨터: 열의 80%는 냉각판으로 냉각 고리에 (우주선 전자 장비 — 방 공기만으로 식히면 그 칸이 45℃를 넘는다)
                    if (kw > 0) { HeatInto(dev, kw * 0.2 * 1000 * dt); _jacket.Add((c, kw * 0.8, CoolComp(d))); }
                    break;
                case "stove":
                {
                    // 냄비: 화구 열의 60%가 냄비 · 음식으로 — 물이 다 졸 때까지 100℃에 머물고 · 그 뒤 눌어 오르다 600K를 넘으면 그 위 음식에 불이 붙는다
                    // (식는 것: 대류 3W/K + 복사 εσA(T⁴ − T공기⁴), A 0.1m² — 3kW 화구에 혼자 두면 30분 남짓 · 켜 둔 조리는 집안 불의 첫째 원인)
                    double food = F.Stored(T.sFood, c), tAir = F.Temp(c);
                    double cPan = 500 + food * 0.18 * 3000 + PanWater[d] * 0.018 * 4186; // 냄비 1kg + 음식 + 물 (J/K)
                    double pk = PanK[d];
                    double loss = 3 * (pk - tAir) + 0.9 * 5.67e-8 * 0.1 * (Math.Pow(pk, 4) - Math.Pow(tAir, 4));
                    double q = (kw > 0 ? kw * 1000 * 0.6 : 0) - loss;
                    if (q > 0 && pk >= 373.15 && PanWater[d] > 0)
                    {
                        double boil = Math.Min(PanWater[d], q * dt / 40_650);              // 끓어 날아간다 (몰 — 증발열 40.65kJ/몰)
                        PanWater[d] -= (float)boil; F.AddGas(c, T.gH2O, boil, 373.15);
                    }
                    else pk += q * dt / cPan;
                    PanK[d] = (float)Math.Max(tAir, pk);
                    F.AddSurfaceHeat(c, loss * dt * 0.5);                                  // 냄비가 내는 열의 반은 조리대 겉면으로
                    F.AddHeat(c, (kw * 1000 * 0.4 + loss * 0.5) * dt);
                    // 물이 다 졸고 200℃를 넘으면 음식이 눌어 연기가 난다 (당 · 기름의 열분해 — C₆H₁₂O₆ → 6C + 6H₂O) — 불꽃보다 먼저 냄새 · 연기 (감지기가 안다)
                    if (PanK[d] > 470 && food > 0.05 && PanWater[d] <= 0)
                    {
                        double x = F.TakeStore(c, T.sFood, Math.Min(food, 0.0005 * (PanK[d] - 470) / 100 * dt));
                        F.AddGas(c, T.gSoot, 6 * x, PanK[d]); F.AddGas(c, T.gH2O, 6 * x, PanK[d]);
                    }
                    if (PanK[d] > 600 && food > 0.05) F.Tf[c] = Math.Max(F.Tf[c], PanK[d]); // 눌은 음식이 불붙을 만큼 뜨겁다
                    if (kw > 0 && OnCause[d] != 0) { var chem = sim.Find<Chemistry>(); if (chem != null) chem.HeatCause[c] = OnCause[d]; }
                    break;
                }
                    HeatInto(dev, kw * 1000 * 0.4 * dt);
                    break;
                case "battery":
                    if (HasCells(dev)) { Cells(sim, dev, dt); break; }   // ② (라) 전해액이 든 배터리 — 셀 묶음 (축전기는 아래 옛 방식)
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
                            // 셀이 터지는 펑 · 안전 밸브로 뿜는 기체의 쉬익 (어림 — 셀 파열 120~130dB 언저리 · 1m)
                            var snd = sim.Find<Sound>();
                            snd?.Emit(sim, c, 110, SoundKind.Bang, cause);
                            snd?.Emit(sim, c, 85, SoundKind.Hiss, cause);
                        }
                    }
                    break;
                case "industry":
                    // 냉각수 재킷: 열의 80%는 냉각 고리로 (모자라면 냉각에서 방으로 돌려준다)
                    if (kw > 0) { HeatInto(dev, kw * 0.2 * 1000 * dt); _jacket.Add((c, kw * 0.8, CoolComp(d))); }
                    break;
                default:
                    // 전기는 결국 열 — 장치가 차지한 칸에 고루 (긴 재배 선반의 전등 열이 한 칸에 몰리지 않게)
                    if (kw > 0) HeatInto(dev, kw * 1000 * dt);
                    break;
            }
        }
    }

    private double[] _need = Array.Empty<double>(); // 틱 안에서만 (찍지 않는다)
    private int[] _live = Array.Empty<int>();
    private bool[] _air = Array.Empty<bool>();
    private double[] _gc = Array.Empty<double>(); // 칸 기체 열용량 (이 틱 — 방 공조가 열을 넣고 빼도 기체는 그대로)
    private double[] _cCap = Array.Empty<double>(), _cUsed = Array.Empty<double>(), _cJk = Array.Empty<double>(), _cRest = Array.Empty<double>(); // 냉각관 조각마다 (틱 안에서만)

    /// <summary>냉각: 펌프 용량만큼 원자로 칸 · 공조의 열을 우주로. 모자라면 원자로가 뜨거워진다 → 스크램.</summary>
    private void Cooling(Sim sim, double dt)
    {
        // 냉각관 조각마다 이어진 펌프 용량 (끊긴 관 너머의 원자로 · 냉각판은 그 조각의 펌프만큼 — 펌프가 없는 조각은 식지 않는다)
        int nc = Math.Max(1, _coolComps);
        if (_cCap.Length < nc) { _cCap = new double[nc]; _cUsed = new double[nc]; _cJk = new double[nc]; _cRest = new double[nc]; }
        Array.Clear(_cCap); Array.Clear(_cUsed); Array.Clear(_cJk);
        double cap = 0;
        for (int d = 0; d < Devices.Count; d++)
            if (Devices[d].Def.Role == "pump" && Working(d) && Coolant > 1000)
            {
                double v = Devices[d].Def.HeatKw * Supply[d];
                cap += v;
                if (CoolComp(d) is int pc && pc >= 0) _cCap[pc] += v;
            }
        // 공조: 냉각 고리에 딸린 방 열교환 · 팬 (펌프마다 30kW) + 따로 단 열교환기
        double hvacCap = 0;
        for (int d = 0; d < Devices.Count; d++)
        {
            if (Devices[d].Def.Role == "pump" && Working(d) && Coolant > 1000) hvacCap += Devices[d].Def.HeatKw * HvacShare * Supply[d];
            if (Devices[d].Def.Role == "hvac" && Working(d)) hvacCap += Devices[d].Def.HeatKw * Supply[d];
        }
        HvacKw = hvacCap;
        CoolingKw = cap;
        // 원자로 칸: 정상 온도(330K) 위의 열을 냉각 용량까지 뺀다
        double used = 0;
        // 공장 설비 재킷 열 (원자로 다음) — 냉각이 모자라면 남은 만큼 그 방으로
        if (_jacket.Count > 0)
        {
            foreach (var j in _jacket) if (j.comp >= 0) _cJk[j.comp] += j.kw;
            for (int k = 0; k < nc; k++)
            {
                _cRest[k] = 1;
                if (_cJk[k] <= 0) continue;
                double take = Math.Min(_cJk[k], Math.Max(0, _cCap[k] - _cUsed[k]));
                _cUsed[k] += take; used += take;
                _cRest[k] = 1 - take / _cJk[k];
            }
            foreach (var (jc, jk, comp) in _jacket)
            {
                double rest = comp >= 0 ? _cRest[comp] : 1;
                if (rest > 1e-6) F.AddHeat(jc, jk * rest * 1000 * dt);
            }
        }
        foreach (var dev in Devices)
        {
            if (dev.Def.Role != "reactor") continue;
            int c = dev.Cell, rc = CoolComp(dev.Id);
            double excessJ = Math.Max(0, F.Ts[c] - 330) * F.Cs[c];
            double take = rc >= 0 ? Math.Min(excessJ, Math.Max(0, _cCap[rc] - _cUsed[rc]) * 1000 * dt) : 0;
            F.AddSolidHeat(c, -take);
            if (rc >= 0) _cUsed[rc] += take / 1000 / dt;
            used += take / 1000 / dt;
            if (!ReactorScram && F.Ts[c] > 700)
            {
                ReactorScram = true;
                int cut = rc < 0 || _cCap[rc] <= 0 ? CoolCutCause(dev.Id) : 0;
                ScramCause = cut != 0 ? sim.Causes.Add(CauseKind.Heat, "냉각관이 끊겨 원자로가 식지 못해 스스로 멈췄다", sim.Tick, c, cut) : sim.Causes.Add(CauseKind.Heat, "냉각이 모자라 원자로가 스스로 멈췄다", sim.Tick, c);
                sim.Facts.Emit(sim.Tick, FactKind.Overheat, c, (float)F.Ts[c], ScramCause);
                sim.Find<Sound>()?.Emit(sim, c, 100, SoundKind.Alarm, ScramCause);
            }
        }
        // 방 온도를 293K로: 방마다 필요한 만큼 (식힘 +, 덥힘 − · 덥힘은 전기 히터라 냉각 용량과 상관없다) — 식힘이 남은 용량을 넘으면 나눈다
        if (hvacCap <= 0) return;
        double left = Math.Min(Math.Max(0, cap - used), hvacCap);
        if (_need.Length != L.Rooms.Count) { _need = new double[L.Rooms.Count]; _live = new int[L.Rooms.Count]; _air = new bool[L.N]; _gc = new double[L.N]; }
        var need = _need; Array.Clear(need);
        double coolSum = 0;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int v = Vent[r];
            if (v < 0 || !RoomPowered(r) || Damper[r] == 0 || F.RegionOf[v] < 0 || F.Moles(v) < 1) continue;
            // 방 공기 평균 온도로 (통풍구 한 칸만 보면 그 칸만 식어 방은 계속 덥다)
            double sumT = 0; int nT = 0;
            foreach (int c in L.Rooms[r].Cells) if (_air[c] = F.Moles(c, out _gc[c]) > 1) { sumT += F.Temp(c, _gc[c]); nT++; }
            _live[r] = nT; // 열을 넣고 빼도 몰은 그대로 — 아래에서 다시 세지 않는다
            double t = nT > 0 ? sumT / nT : F.Temp(v);
            need[r] = Math.Clamp(0.1 * L.Rooms[r].Cells.Length * (t - SetpointK), -3, 40); // 칸마다 1K에 0.1kW
            if (need[r] > 0) coolSum += need[r];
        }
        double scale = coolSum > left ? left / coolSum : 1;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (need[r] == 0) continue;
            double kw = need[r] > 0 ? need[r] * scale : need[r];
            // 방 칸마다 나눠서 (팬이 방 공기를 돌린다)
            var cells = L.Rooms[r].Cells;
            int live = _live[r];
            if (live == 0) { F.AddHeat(Vent[r], -kw * 1000 * dt); continue; }
            foreach (int c in cells) if (_air[c]) F.AddHeat(c, -kw * 1000 * dt / live, _gc[c]);
        }
    }

    /// <summary>덕트: 팬이 돌면 열린 통풍구마다 공기의 6%를 모아 고르게 돌려준다 (보존).</summary>
    private readonly List<int> _airflowCells = new();   // 지난 틱 공조 바람을 요청한 칸 (틱마다 다시 — 장에 남은 값이 찍힌다)
    private bool _airflowStale;                          // 되살린 뒤 아직 목록을 다시 짓지 않았다
    private void Ducts(Sim sim)
    {
        if (_airflowStale) { _airflowStale = false; _airflowCells.Clear(); for (int c = 0; c < F.Cells; c++) if (F.Airflow[c] > 0) _airflowCells.Add(c); }   // 되살린 세계: 장에 남은 바람이 지난번 요청이다
        foreach (int c in _airflowCells) F.Airflow[c] = 0;
        _airflowCells.Clear();
        if (HvacKw <= 0) return; // 팬이 돌지 않는다 (냉각 고리 · 열교환기가 모두 섰다)
        // 정화 배기: 방 곳곳의 흡입구로 칸 공기의 1.5%씩(1분이면 방 공기의 3할)을 정화기로 보내 독한 기체의 90%를 걸러 그 방으로 돌려준다 (정화기가 서 있으면 못 한다)
        bool scrub = false, checkedScrub = false;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int v = Vent[r];
            if (Exhaust[r] == 0 || v < 0 || !RoomPowered(r) || F.RegionOf[v] < 0) continue;
            if (!checkedScrub) { scrub = ScrubberWorking(); checkedScrub = true; }
            if (!scrub) break;
            TraceScrubbed += ScrubTrace(L.Rooms[r].Cells, VentFraction / 4 * 0.9);
        }
        var vents = new List<int>();
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int v = Vent[r];
            if (v >= 0 && Damper[r] != 0 && Exhaust[r] == 0 && RoomPowered(r) && F.RegionOf[v] >= 0 && F.Moles(v) > 1 && (_stc == null || _stc.InMain(v))) vents.Add(v);
        }
        if (vents.Count < 2) return;
        // 압력 차단판: 기압이 다른 방들보다 15% 넘게 낮은 방의 통풍구는 저절로 닫힌다 (뚫린 방으로 배 전체 공기가 덕트를 타고 빠지지 않게)
        var ps = vents.Select(v => F.Pressure(v)).OrderBy(x => x).ToList();
        double median = ps[ps.Count / 2];
        vents.RemoveAll(v => F.Pressure(v) < 0.85 * median);
        if (vents.Count < 2) return;
        // 통풍구에서 나오는 바람 (③ 장에 요청 — 그 칸 0.5m/s · 곁 칸 0.25m/s): 무중력에서 부력 대신 공기를 섞는다
        Span<int> vnb = stackalloc int[4];
        foreach (int v in vents)
        {
            F.Airflow[v] = 0.5f; _airflowCells.Add(v);
            L.Neighbors(v, vnb);
            foreach (int j in vnb) if (j >= 0 && F.RegionOf[j] >= 0 && F.RegionOf[j] == F.RegionOf[v] && F.Airflow[j] < 0.25f) { F.Airflow[j] = 0.25f; _airflowCells.Add(j); }
        }
        int G = F.G;
        var pool = new double[G];
        double poolU = 0;
        foreach (int v in vents)
        {
            for (int g = 0; g < G; g++) { double t = F.N[v * G + g] * VentFraction; pool[g] += t; F.N[v * G + g] -= t; }
            double du = F.U[v] * VentFraction; poolU += du; F.U[v] -= du;
        }
        // 응결 열교환기 (ISS 공조 장치처럼): 덕트로 모인 공기가 이슬점 10℃의 차가운 판을 지나며 그보다 많은 습기를 물로 맺어 물탱크로 (방 습도 45% 안팎 · 판을 비껴가는 공기 2할)
        double nTot = 0, cTot = 0;
        for (int g = 0; g < G; g++) { nTot += pool[g]; cTot += pool[g] * T.Gases[g].C; }
        double pH2O = nTot > 0 ? median * pool[T.gH2O] / nTot : 0, pCoil = Water.SatKPa(CoilK);
        if (pH2O > pCoil && cTot > 0)
        {
            double tMix = poolU / cTot;
            double w = pool[T.gH2O] * (1 - pCoil / pH2O) * 0.8;
            pool[T.gH2O] -= w; poolU -= w * T.Gases[T.gH2O].C * tMix;
            WaterTank += w; sim.Book.Add(Res.Water, Flow.Condensed, w);
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
        NoteFires(sim);
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            int c = dev.Cell;
            // ④가 부서졌다고 한 설비 (파편 · 폭발 압력): 회로 · 모터 · 봉함이 한꺼번에 (배터리는 찌그러진 셀이 열폭주로 — ② 화학)
            if (_stc != null && _stc.FurnBroken[dev.Furniture] != 0 && Broken[d * 4 + (int)Part.Electric] == 0) Smash(sim, d, _stc.FurnCause[dev.Furniture]);
            bool running = On[d] != 0 && Supply[d] > 0.05f;
            double temp = Math.Max(F.Temp(c), F.Ts[c]);
            double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(Math.Max(250, F.Temp(c))));
            double soot = F.Ppm(T.gSoot, c);
            for (int p = 0; p < 4; p++)
            {
                int k = d * 4 + p;
                if (_limit[k] == float.MaxValue || Broken[k] != 0) continue;
                WearPart(sim, d, p, hours, running, temp, rh, soot);
            }
        }
        CablesAge(sim, hours);
        PipesRust(sim, hours);
        Noise(sim);
    }

    /// <summary>
    /// 장치의 소리 (1분마다 — ③ 소리 장에 요청만): 돌아가는 장치마다 이어지는 소리(표의 크기 + 낡은 몫) ·
    /// 낡은 몫이 3dB를 넘으면 5분마다 끽끽 · 덜컹 (장치 크기 + 낡은 몫 + 6dB — 결함 박자의 충격이 웅웅 위로 솟는다 · 어림).
    /// 끽끽의 원인 마디 '○○의 모터가 낡아 끽끽거린다'는 그 부품을 낡게 한 원인에 잇고, 망가지면 그 마디에 잇는다.
    /// </summary>
    private void Noise(Sim sim)
    {
        var snd = sim.Find<Sound>();
        if (snd == null) return;
        for (int d = 0; d < Devices.Count; d++)
        {
            var dev = Devices[d];
            float db = NoiseDb(d);
            if (db > 0 || snd.DroneDb(d) > 0) snd.SetDrone(d, dev.Cell, db);
            float worn = WornDb(d);
            if (db <= 0 || worn < GrindFromDb || sim.Tick % 125 != 0) continue;
            int k = d * 4 + (int)Part.Mechanical;
            int cause = sim.Causes.Add(CauseKind.Wear, $"{dev.Name}의 모터가 낡아 끽끽거린다", sim.Tick, dev.Cell, _wearCause.GetValueOrDefault(k));
            _wearCause[k] = cause;
            snd.Emit(sim, dev.Cell, db + 6, SoundKind.Machine, cause);
        }
    }

    /// <summary>송풍기로 방 공기를 돌려 기체 하나를 걷는다 — 방 칸마다 가진 만큼 나눠서 (장치 칸만 비우지 않게).</summary>
    /// <summary>칸들의 독한 기체(암모니아 · 일산화탄소 · 오존)를 frac만큼 걸러 낸다. 걸러 낸 몰.</summary>
    private int[]? _trace; // 거르는 독한 기체 (암모니아 · 일산화탄소 · 오존)
    private double ScrubTrace(int[] cells, double frac)
    {
        double took = 0;
        _trace ??= new[] { T.gNH3, T.gCO, T.gO3 };
        foreach (int c in cells)
            foreach (int g in _trace)
            {
                double n = F.Gas(g, c);
                if (n > 1e-9) took += F.RemoveGas(c, g, n * frac);
            }
        return took;
    }

    private void ScrubRoom(Device dev, int gas, double mol)
    {
        if (mol <= 0) return;
        var cells = dev.Room >= 0 && dev.Room < L.Rooms.Count && L.Rooms[dev.Room].Cells.Length > 0 ? L.Rooms[dev.Room].Cells : new[] { dev.Cell };
        double total = 0;
        foreach (int c in cells) total += F.Gas(gas, c);
        if (total <= 1e-12) return;
        double frac = Math.Min(0.5, mol / total), got = 0;
        foreach (int c in cells) { double g = F.Gas(gas, c); if (g > 0) got += F.RemoveGas(c, gas, g * frac); }
        if (gas == T.gCO2)
        {
            CO2Store += got;
            if (CO2Store > CO2StoreMax) { VentedCO2 += CO2Store - CO2StoreMax; CO2Store = CO2StoreMax; }
        }
    }

    /// <summary>설비가 부서졌다 (④ 구조가 알린 것): 있는 부품이 모두 망가진다 · 그 원인은 부순 것에 잇는다.</summary>
    private void Smash(Sim sim, int d, int cause)
    {
        var dev = Devices[d];
        for (int p = 0; p < 4; p++) if (dev.Has((Part)p) || p == 0) { Broken[d * 4 + p] = 1; _brokeCause[d * 4 + p] = cause; }
        Failures++;
        if (dev.Def.Role is "pump" or "hvac" && Coolant > 0 && dev.Has(Part.Seal))
        {
            SealLeakCause[d] = sim.Causes.Add(CauseKind.Leak, $"부서진 {Ko.I(dev.Name)} 틈으로 암모니아가 새어 나온다", sim.Tick, dev.Cell, cause);
            sim.Facts.Emit(sim.Tick, FactKind.Toxic, dev.Cell, 0, SealLeakCause[d], other: d);
        }
        if (dev.Def.Role == "o2gen" && dev.Has(Part.Seal))
            SealLeakCause[d] = sim.Causes.Add(CauseKind.Leak, $"부서진 {dev.Name}의 수소 관으로 갈라 낸 수소가 방으로 샌다", sim.Tick, dev.Cell, cause);
        if (dev.Def.Role == "battery" && HasCells(dev)) CrushCell(dev, cause);   // 찌그러진 셀 — 묶음 하나가 속 합선 (② (라))
        else if (dev.Def.Role == "battery" && Charge[d] > 0.5) sim.Find<Chemistry>()?.Abuse(dev.Cell, cause);
    }

    private void Fail(Sim sim, int d, Part p)
    {
        var dev = Devices[d];
        Broken[d * 4 + (int)p] = 1;
        Failures++;
        var (why, parent) = WhyFailed(dev, p);   // 닳음의 까닭 (⑥ 바퀴 3 (나)) — 열 · 습기 · 그을음 · 기름 마름 · 기록된 까닭
        int cause = sim.Causes.Add(CauseKind.Wear, why, sim.Tick, dev.Cell, parent);
        _brokeCause[d * 4 + (int)p] = cause;
        sim.Facts.Emit(sim.Tick, FactKind.MachineFail, dev.Cell, (int)p, cause, other: d);
        if (p == Part.Electric && dev.Feeder >= 0 && Rng.Unit(_seed, Rng.Key("회로 고장 합선"), sim.Tick, d) < 0.25)
        {
            FaultCell[dev.Feeder] = dev.Cell;
            _faultCause[dev.Feeder] = sim.Causes.Add(CauseKind.Electric, "망가진 회로에서 합선", sim.Tick, dev.Cell, cause);
            Shorts++;
        }
        if (p == Part.Mechanical) sim.Find<Sound>()?.Emit(sim, dev.Cell, 85, SoundKind.Machine, cause);
        // 냉매 고리의 봉함: 암모니아가 새기 시작한다 (쉬익 — 독한 기체)
        if (p == Part.Seal && dev.Def.Role is "pump" or "hvac" && Coolant > 0)
        {
            SealLeakCause[d] = sim.Causes.Add(CauseKind.Leak, $"{Ko.I(dev.Name)} 터진 틈으로 암모니아가 새어 나온다", sim.Tick, dev.Cell, cause);
            sim.Facts.Emit(sim.Tick, FactKind.Toxic, dev.Cell, 0, SealLeakCause[d], other: d);
            sim.Find<Sound>()?.Emit(sim, dev.Cell, 70, SoundKind.Hiss, SealLeakCause[d]);
        }
        // 산소 발생기 봉함: 갈라 낸 수소가 배 밖으로 가지 않고 방으로 샌다 (냄새 · 색 없음 — 주컴퓨터의 불붙는 기체 센서만 안다)
        if (p == Part.Seal && dev.Def.Role == "o2gen")
        {
            SealLeakCause[d] = sim.Causes.Add(CauseKind.Leak, $"{dev.Name}의 수소 관 봉함이 터져 갈라 낸 수소가 방으로 샌다", sim.Tick, dev.Cell, cause);
            sim.Find<Sound>()?.Emit(sim, dev.Cell, 55, SoundKind.Hiss, SealLeakCause[d]);
        }
        // 물 재생기 봉함: 오수 관이 샌다 (바닥에 고인 물이 급전선 길에 닿으면 합선 — 그 원인이 이 마디에 이어진다)
        if (p == Part.Seal && dev.Def.Role == "recycler")
            SealLeakCause[d] = sim.Causes.Add(CauseKind.Leak, $"{dev.Name} 오수 관이 샌다 — 바닥에 물이 고인다", sim.Tick, dev.Cell, cause);
    }

    /// <summary>젖은 칸의 물이 어디서 왔나: 같은 방 6칸 안에서 새는 물 재생기 (원인 마디 · 0 모름).</summary>
    public int SpillNear(int cell)
    {
        int room = L.Room[cell], best = 0, bd = 7;
        foreach (var dev in Devices)
        {
            if (SealLeakCause[dev.Id] == 0 || dev.Def.Role != "recycler" || dev.Room != room) continue;
            int d = Math.Abs(L.X(dev.Cell) - L.X(cell)) + Math.Abs(L.Y(dev.Cell) - L.Y(cell));
            if (d < bd) { bd = d; best = SealLeakCause[dev.Id]; }
        }
        return best;
    }

    // ───────────────────────── 센서 선 (⑥ 바퀴 1) ─────────────────────────
    // 방마다 센서(통풍구 칸)에서 주컴퓨터까지 바닥 · 문 칸을 따라 선을 깐다 (줄기선은 여러 방이 함께 쓴다).
    // 선이 지나는 칸이 불에 250℃를 넘거나 곁 외판이 뚫리면 그 칸의 선이 상한다 — 그 너머 방들의 센서가 주컴퓨터에 닿지 않는다.
    // 주컴퓨터는 가장 가까운 끊긴 자리를 안다 (선 반사 측정) · 로봇 · 사람이 가서 이으면 다시 닿는다.

    public const double CableBurnK = 523; // 전선 피복이 녹고 타는 온도 (250℃)
    public int[][] DataPath = Array.Empty<int[]>(); // [방] 주컴퓨터 쪽부터 센서까지
    public byte[] DataBroken = Array.Empty<byte>(); // [칸] 1: 그 칸의 선이 상했다
    public int[] DataBrokenCause = Array.Empty<int>();
    public int[] DataCutCell = Array.Empty<int>();   // [방] 주컴퓨터에서 가장 가까운 끊긴 칸 (-1: 닿는다)
    private bool[] _onData = Array.Empty<bool>();
    public long DataCuts, DataSplices;

    private void BuildData()
    {
        DataPath = new int[L.Rooms.Count][];
        DataCutCell = Enumerable.Repeat(-1, L.Rooms.Count).ToArray();
        DataBroken = new byte[L.N]; DataBrokenCause = new int[L.N]; _onData = new bool[L.N];
        var computer = Devices.FirstOrDefault(d => d.Type == "MainComputer");
        for (int r = 0; r < L.Rooms.Count; r++) DataPath[r] = Array.Empty<int>();
        if (computer == null) return;
        // 주컴퓨터 칸에서 바닥 · 문을 따라 거리장
        var prev = Enumerable.Repeat(-2, L.N).ToArray();
        var q = new Queue<int>(); prev[computer.Cell] = -1; q.Enqueue(computer.Cell);
        Span<int> nb = stackalloc int[4];
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            L.Neighbors(c, nb);
            foreach (int j in nb) if (j >= 0 && prev[j] == -2 && L.IsSpace(j)) { prev[j] = c; q.Enqueue(j); }
        }
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            int v = Vent[r];
            if (v < 0 || prev[v] == -2) continue;
            var path = new List<int>();
            for (int c = v; c >= 0; c = prev[c]) path.Add(c);
            path.Reverse();
            DataPath[r] = path.ToArray();
            foreach (int c in path) _onData[c] = true;
        }
    }

    /// <summary>그 방의 센서가 주컴퓨터에 닿나 (센서 선이 없는 배는 늘 닿는다).</summary>
    public bool RoomLinked(int room) => room < 0 || room >= DataCutCell.Length || DataCutCell[room] < 0;

    /// <summary>그 칸의 센서 선이 상했나.</summary>
    public bool DataCutAt(int cell) => cell >= 0 && cell < DataBroken.Length && DataBroken[cell] != 0;

    /// <summary>선이 상한다 (불 · 외판 구멍 — 시험 · 다른 피해도 이것으로).</summary>
    public void CutData(Sim sim, int cell, int cause)
    {
        if (cell < 0 || cell >= DataBroken.Length || !_onData[cell] || DataBroken[cell] != 0) return;
        DataBroken[cell] = 1; DataBrokenCause[cell] = cause; DataCuts++;
        sim.Facts.Emit(sim.Tick, FactKind.SensorCut, cell, 0, cause);
        Relink();
    }

    /// <summary>상한 선을 잇는다 (로봇 · 사람이 그 자리에서).</summary>
    public bool SpliceData(int cell)
    {
        if (!DataCutAt(cell)) return false;
        DataBroken[cell] = 0; DataBrokenCause[cell] = 0; DataSplices++;
        Relink();
        return true;
    }

    private void Relink()
    {
        for (int r = 0; r < DataPath.Length; r++)
        {
            DataCutCell[r] = -1;
            foreach (int c in DataPath[r]) if (DataBroken[c] != 0) { DataCutCell[r] = c; break; }
        }
    }

    /// <summary>그 칸을 달군 불: 그 칸 또는 두 칸 안의 불 · 불씨 · 달군 원인 (가까운 것부터 · 없으면 0).</summary>
    private int HeatSource(Chemistry? chem, int c)
    {
        if (chem == null) return 0;
        int x0 = L.X(c), y0 = L.Y(c), best = 0, bd = int.MaxValue;
        for (int y = y0 - 2; y <= y0 + 2; y++)
            for (int x = x0 - 2; x <= x0 + 2; x++)
            {
                if (!L.In(x, y)) continue;
                int j = L.Index(x, y), d = Math.Abs(x - x0) + Math.Abs(y - y0), why = chem.HeatCauseAt(j);
                if (why != 0 && d < bd) { bd = d; best = why; }
            }
        return best;
    }

    public void Save(StateWriter w)
    {
        w.Arr(DataBroken); w.Arr(DataBrokenCause); w.Arr(DataCutCell); w.I64(DataCuts); w.I64(DataSplices);
        w.Arr(On); w.Arr(Supply); w.Arr(Wear); w.Arr(Broken); w.Arr(Charge);
        w.Arr(Breaker); w.Arr(OverSec); w.Arr(FaultCell); w.Arr(BreakerStuck); w.Arr(BreakerWeld); w.Arr(MainStuck); w.Arr(Amps); w.Arr(Damper); w.Arr(Exhaust); w.F64(TraceScrubbed); w.Arr(SealLeakCause); w.Arr(OnCause); w.Arr(PanK); w.Arr(PanWater);
        w.Bool(ReactorScram); w.I32(ScramCause); w.F64(WaterTank); w.F64(WasteWater); w.F64(Brine); w.F64(WaterProcessed); w.F64(WaterRecovered); w.F64(Coolant); w.F64(VentedH2); w.F64(AirReserve); w.F64(AirRelease); w.F64(AirReleased); w.Arr(RoomAir); w.F64(AirRecovered);
        w.F64(VentedCH4); w.F64(VentedCO2); w.F64(CO2Store); w.Arr(Duty); w.F64(PpO2);
        w.F64(Supplied); w.F64(Demanded); w.F64(CoolingKw); w.F64(ReactorKw); w.F64(HvacKw);
        w.I64(Trips); w.I64(Shorts); w.I64(Failures);
        var keys = _faultCause.Keys.OrderBy(k => k).ToList();
        w.I32(keys.Count);
        foreach (var k in keys) { w.I32(k); w.I32(_faultCause[k]); }
        var wk = _wearCause.Keys.OrderBy(k => k).ToList();
        w.I32(wk.Count);
        foreach (var k in wk) { w.I32(k); w.I32(_wearCause[k]); }
        SaveJumpers(w); w.Arr(Stripped); w.I64(PartsSalvaged);
        SaveLines(w); SaveWear(w); SavePipes(w); SaveCells(w); SaveO2(w); SaveGravity(w); SaveChips(w); SaveRobots(w);
    }

    public void Load(StateReader r)
    {
        _airflowStale = true;
        r.ArrInto(DataBroken); r.ArrInto(DataBrokenCause); r.ArrInto(DataCutCell); DataCuts = r.I64(); DataSplices = r.I64();
        r.ArrInto(On); r.ArrInto(Supply); r.ArrInto(Wear); r.ArrInto(Broken); r.ArrInto(Charge);
        r.ArrInto(Breaker); r.ArrInto(OverSec); r.ArrInto(FaultCell); r.ArrInto(BreakerStuck); r.ArrInto(BreakerWeld); r.ArrInto(MainStuck); r.ArrInto(Amps); r.ArrInto(Damper); r.ArrInto(Exhaust); TraceScrubbed = r.F64(); r.ArrInto(SealLeakCause); r.ArrInto(OnCause); r.ArrInto(PanK); r.ArrInto(PanWater);
        ReactorScram = r.Bool(); ScramCause = r.I32(); WaterTank = r.F64(); WasteWater = r.F64(); Brine = r.F64(); WaterProcessed = r.F64(); WaterRecovered = r.F64(); Coolant = r.F64(); VentedH2 = r.F64(); AirReserve = r.F64(); AirRelease = r.F64(); AirReleased = r.F64(); RoomAir = r.Arr<double>(); AirRecovered = r.F64();
        VentedCH4 = r.F64(); VentedCO2 = r.F64(); CO2Store = r.F64(); r.ArrInto(Duty); PpO2 = r.F64();
        Supplied = r.F64(); Demanded = r.F64(); CoolingKw = r.F64(); ReactorKw = r.F64(); HvacKw = r.F64();
        Trips = r.I64(); Shorts = r.I64(); Failures = r.I64();
        _faultCause.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++) { int k = r.I32(); _faultCause[k] = r.I32(); }
        _wearCause.Clear();
        n = r.I32();
        for (int i = 0; i < n; i++) { int k = r.I32(); _wearCause[k] = r.I32(); }
        LoadJumpers(r); Stripped = r.Arr<byte>(); PartsSalvaged = r.I64();
        LoadLines(r); LoadWear(r); LoadPipes(r); LoadCells(r); LoadO2(r); LoadGravity(r); LoadChips(r); LoadRobots(r);
    }
}
