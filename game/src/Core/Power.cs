using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>임시 배선 한 가닥. From 회로의 전기를 To 회로로 끌어온다. 깐 자리(At)에 흔적이 남는다.</summary>
public sealed class Jumper
{
    public int From { get; }
    public int To { get; }
    public long Since { get; }
    public Cell At { get; }
    public float Load { get; internal set; }
    public bool Burnt { get; internal set; }

    /// <summary>배선 솜씨 (서투르면 허술해서 더 적은 전력에 달아오른다).</summary>
    public float Quality { get; }

    /// <summary>지금 실제로 전기를 나르고 있는지.</summary>
    public bool Active { get; internal set; }

    public float Capacity => PowerGrid.JumperCapacityKw * Quality;

    /// <summary>
    /// 설계해서 깐 예비 배선 (v7 전력망 이중화). 임시 배선과 달리 평소엔 쉬고 있다가,
    /// 받는 회로가 끊기면 저절로 넘어가 전기를 댄다. 굵은 전선이라 정격이 크다.
    /// </summary>
    public bool Permanent { get; }

    public Jumper(int from, int to, long since, Cell at, float quality, bool permanent = false)
    {
        From = from;
        To = to;
        Since = since;
        At = at;
        Quality = quality;
        Permanent = permanent;
    }
}

/// <summary>
/// 전력망. 원자로(냉각 펌프가 열을 빼 줘야 출력을 낼 수 있음) → 배터리 → 배전반의 회로 네 개 → 방과 설비.
/// 전기가 모자라면 우선순위가 낮은 것부터 끊는다. 차단기가 내려가면 그 회로의 방만 죽는다.
///
/// 블랙스타트: 냉각을 거의 잃으면 원자로가 긴급 정지(SCRAM)한다. 냉각 펌프도 전기가 있어야 돌기 때문에
/// 배터리가 살아 있으면 펌프를 돌려 재기동할 수 있지만, 배터리까지 바닥나면 보조 발전기를 손으로 돌려
/// A 회로(필수)부터 살려야 한다: 보조 발전기 → 냉각 펌프 → 원자로 재기동 → 주 전력 복원.
/// </summary>
public sealed class PowerGrid
{
    public const int CircuitCount = 4;
    public static float ReactorMaxKw = 48f;
    public const float CoolingPerPumpKw = 30f;
    public const float BatteryKwhEach = 60f;
    public const float BatteryMaxDischargeKw = 20f;
    public const float BatteryMaxChargeKw = 10f;
    public const float RoomSystemsKw = 0.25f;

    /// <summary>냉각이 이 아래로 떨어지면 원자로 긴급 정지.</summary>
    public const float ScramCoolingKw = 6f;

    /// <summary>재기동하려면 이만큼의 냉각이 있어야 한다 (펌프 절반 이상).</summary>
    public const float RestartCoolingKw = 14f;

    /// <summary>재기동 후 최대 출력까지 걸리는 시간.</summary>
    public const float RampHours = 0.5f;

    public static float AuxKw = 9f;
    public const float AuxFuelHours = 24f;

    /// <summary>연료통 하나로 보조 발전기를 돌리는 시간.</summary>
    public const float FuelCanHours = 6f;

    /// <summary>냉각 펌프 없이 자연 순환으로 뺄 수 있는 열 (저출력 수동 기동의 상한).</summary>
    public const float NaturalCoolingKw = 7f;

    /// <summary>임시 배선 한 가닥이 견디는 전력. 넘기면 달아오르다 불이 난다.</summary>
    public const float JumperCapacityKw = 10f;

    /// <summary>임시 배선 한 가닥에 드는 케이블.</summary>
    public const int JumperCables = 2;

    public static string CircuitName(int i) => ((char)('A' + i)).ToString();

    /// <summary>배터리 한 대의 용량: 설계도의 큰 배터리(2×2) 60kWh, 항해 중에 단 모듈(한 칸) 40kWh.</summary>
    public static float BatteryKwh(Furniture f) => (f.Cells.Count >= 4 ? BatteryKwhEach : 40f) * (f.Machine != null ? Tech.Of(f.Machine).Output : 1f);

    public static string CircuitRole(int i) => i switch
    {
        0 => "필수",
        1 => "생활",
        2 => "거주·운항",
        _ => "기관·작업",
    };

    public static int DefaultCircuit(RoomType t) => t switch
    {
        RoomType.Reactor or RoomType.Cooling or RoomType.Power or RoomType.LifeSupport or RoomType.Medbay => 0,
        RoomType.Galley or RoomType.Mess or RoomType.Hydroponics or RoomType.Storage => 1,
        RoomType.Quarters or RoomType.Lounge or RoomType.Bridge or RoomType.Corridor or RoomType.Comms => 2,
        _ => 3,
    };

    private readonly World _world;

    public bool ReactorOnline { get; private set; } = true;
    public float ReactorRamp { get; private set; } = 1f;
    public float ReactorOutput { get; private set; }
    public float ReactorLimit { get; private set; }
    public float ReactorTemperature { get; private set; } = 280f;
    public float CoolingCapacity { get; private set; }
    public float Demand { get; private set; }
    public float Delivered { get; private set; }
    public float BatteryCharge { get; internal set; }
    public float BatteryCapacity { get; private set; }
    public float BatteryFlow { get; private set; }
    public bool AuxRunning { get; private set; }
    public float AuxFuel { get; internal set; } = AuxFuelHours;
    public float AuxOutput { get; private set; }

    /// <summary>배전반 기준으로 그 회로가 살아 있는지 (차단기·단락).</summary>
    public bool[] CircuitLive { get; } = { true, true, true, true };

    /// <summary>실제로 전기가 흐르는지 (살아 있거나 임시 배선으로 받거나, 사람이 내리지 않았거나).</summary>
    public bool[] CircuitFed { get; } = { true, true, true, true };

    /// <summary>승무원이 절전하려고 일부러 내린 회로.</summary>
    public bool[] ManualOff { get; } = new bool[CircuitCount];

    // ── v9.3 저출력 운영: 원자로가 돌아도 수요를 못 대면, 사람들이 설비를 골라 내리고 식량에 전기를 돌린다 ──
    /// <summary>저출력 운영 중 (회의로 정하고 배전반에서 사람이 내린다).</summary>
    public bool Brownout { get; set; }
    public long BrownoutSince { get; set; }
    public int Brownouts { get; set; }

    /// <summary>원자로가 돌아도 수요를 못 댄 지 (없으면 -1).</summary>
    public long DeficitSince { get; private set; } = -1;

    /// <summary>저출력 운영 중에 전부 켜도 남는 지 (없으면 -1).</summary>
    public long SurplusSince { get; private set; } = -1;

    /// <summary>내린 설비까지 다 켰을 때의 수요.</summary>
    public float FullDemand { get; private set; }
    public int ParkedCount { get; private set; }
    private int _parkedPump = -1, _parkedO2 = -1;

    /// <summary>임시 배선. 한번 깔면 흔적으로 남는다.</summary>
    public List<Jumper> Jumpers { get; } = new();

    /// <summary>v10.10: 걷어 낸 임시 배선의 흔적 (흔적 보기에 남는다).</summary>
    public List<(Cell at, int from, int to, long since, long removed)> RemovedJumpers { get; } = new();

    private readonly long[] _liveSince = new long[4];

    /// <summary>그 회로가 제 힘으로(배전반에서) 다시 살아난 틱.</summary>
    public long LiveSince(int circuit) => _liveSince[circuit];

    /// <summary>냉각 펌프 없이 자연 순환만으로 저출력 운전 중.</summary>
    public bool LowPowerMode { get; private set; }

    public int ShedCount { get; private set; }

    public PowerGrid(World world)
    {
        _world = world;
        foreach (var room in world.Ship.Rooms) room.Circuit = DefaultCircuit(room.Type);
        BatteryCapacity = world.Ship.FurnitureOf(FurnitureType.Battery).Sum(BatteryKwh);
        BatteryCharge = BatteryCapacity * 0.8f;
    }

    public Machine? Reactor => _world.Ship.FurnitureOf(FurnitureType.ReactorCore).FirstOrDefault()?.Machine;

    /// <summary>v11.2 원자로 이상: 노심 온도가 갑자기 뛴다 (냉각이 모자라면 과열 정지로 이어진다).</summary>
    internal void Heat(float celsius) => ReactorTemperature += celsius;

    /// <summary>v10.4: 이 배 원자로의 정격 (크기 × 테크 단계).</summary>
    public float ReactorRated => ReactorMaxKw * (Reactor?.Rating ?? 1f);

    /// <summary>v10.4: 보조 발전기 정격.</summary>
    public float AuxRated => AuxKw * (Aux?.Rating ?? 1f);
    private Machine? Panel => _world.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine;
    private Machine? Aux => _world.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault()?.Machine;

    public float BatteryPercent => BatteryCapacity > 0 ? BatteryCharge / BatteryCapacity : 0f;

    /// <summary>v9: 자동 제어봉이 출력을 내리는 속도 (kW/시간).</summary>
    public const float RodDropKwPerHour = 60f;

    /// <summary>v9.2: 자동화가 꺼지면 제어봉은 느린 기계식 안전 장치만으로 내려간다.</summary>
    public const float ManualRodKwPerHour = 22f;

    /// <summary>v9: 노심의 열용량 (kWh/℃). 넘치는 열 10kW면 한 시간에 100℃ 오른다.</summary>
    public const float CoreHeatKwhPerC = 0.1f;

    public const float OverheatWarnC = 350f;
    public const float OverheatScramC = 380f;

    private float _rodLimit = ReactorMaxKw;
    private bool _overheatWarned;

    /// <summary>v9: 실제로 빼낼 수 있는 열 (kW, 저출력 수동 운전이면 자연 순환 포함).</summary>
    public float EffectiveCooling { get; private set; } = 60f;

    /// <summary>v8: 배전반과 끊겼다 (배전실이 떨어져 나갔거나 다시 붙였지만 전력선을 잇지 않았다).</summary>
    public bool PanelCut { get; private set; }

    /// <summary>승무원이 원자로를 다시 켰다.</summary>
    public void RestartReactor()
    {
        ReactorOnline = true;
        LowPowerMode = false;
        ReactorRamp = 0.05f;
        _world.Log.Add(_world.Tick, LogKind.Ship, "원자로 재기동 — 출력을 서서히 올린다");
    }

    /// <summary>
    /// 냉각 펌프가 돌지 않는 상태에서 손으로 제어봉을 조금씩 빼서 자연 순환 냉각만으로 저출력 기동한다.
    /// 원자로 ↔ 냉각 펌프(전기) ↔ 배터리·보조 발전기가 모두 막혔을 때 고리를 끊는 마지막 수단. 과열될 수 있다.
    /// </summary>
    public void ManualLowPowerStart()
    {
        ReactorOnline = true;
        LowPowerMode = true;
        ReactorRamp = 0.3f;
        _world.RaiseAlert("원자로 저출력 수동 기동 — 자연 순환 냉각으로 버틴다", Reactor?.Body.Room, AlertLevel.Warning, shipWide: true);
        _world.Board.RequestScan();
    }

    public void AddFuel(float hours) => AuxFuel = MathF.Min(AuxFuelHours, AuxFuel + hours);

    /// <summary>임시 배선을 깐다 (from 회로의 전기를 to 회로로).</summary>
    public Jumper AddJumper(int from, int to, Cell at, float quality, bool permanent = false)
    {
        var j = new Jumper(from, to, _world.Tick, at, quality, permanent);
        Jumpers.Add(j);
        CircuitFed[to] = !ManualOff[to];
        _world.Board.RequestScan();
        return j;
    }

    /// <summary>
    /// 임시 배선을 끌어올 회로: 살아 있고 사람이 내리지 않은 회로.
    /// 필수 회로(A)를 살릴 때는 B→C→D 순, 다른 회로를 살릴 때는 A부터 (절전으로 내릴 일이 없는 회로).
    /// </summary>
    public int JumperSource(int to)
    {
        int[] order = to == 0 ? new[] { 1, 2, 3 } : new[] { 0, 1, 2, 3 };
        foreach (int i in order)
            if (i != to && CircuitLive[i] && !ManualOff[i]) return i;
        return -1;
    }

    /// <summary>그 회로를 켜면 드는 전력 (지금 멈춘 설비는 빼고).</summary>
    /// <summary>
    /// v9.3 저출력 운영에서 무엇을 내려 둘지: 원자로 출력이 펌프 하나로 식힐 만큼이면 냉각 펌프 하나, 공기가 넉넉하면 산소 발생기 하나,
    /// 누운 사람이 없으면 치료 침대, 엔진, 함교 밖의 콘솔. 펌프·산소 발생기는 한번 내리면 사정이 나빠질 때까지 둔다 (들쭉날쭉하지 않게).
    /// </summary>
    private void UpdateParking(float reactorMax)
    {
        var w = _world;
        var ship = w.Ship;
        foreach (var m in ship.Machines) m.Parked = false;
        ParkedCount = 0;
        if (!Brownout) { _parkedPump = _parkedO2 = -1; return; }

        // 냉각 펌프: 분기 하나로 원자로 출력을 식힐 수 있으면 약한 쪽 분기의 펌프를 내린다
        var branches = w.Piping.Branches.Where(b => b.Pump?.Machine is Machine pm && !b.Pump.Room.Detached && pm.FaultFactor > 0.3f).ToList();
        if (branches.Count >= 2)
        {
            float Strength(PipeSegment b) => b.Flow * (0.35f + 0.65f * b.RadiatorCondition) * b.Pump!.Machine!.FaultFactor * (0.6f + 0.4f * b.Pump.Machine.Condition);
            var weak = branches.OrderBy(Strength).ThenByDescending(b => b.Id).First();
            var strong = branches.Where(b => b != weak).OrderByDescending(Strength).First();
            float oneBranch = CoolingPerPumpKw * Strength(strong) * 0.95f;
            bool wasParked = _parkedPump == weak.Pump!.Id;
            bool keep = wasParked ? reactorMax + 1f <= oneBranch : reactorMax + 5f <= oneBranch;
            _parkedPump = keep && strong.Flow > 0.5f ? weak.Pump.Id : -1;
        }
        else _parkedPump = -1;

        // 산소 발생기: 공기가 넉넉하고 남은 한 대로 숨 쉴 만큼이면 하나를 내린다
        var gens = ship.FurnitureOf(FurnitureType.OxygenGenerator).Where(f => !f.Room.Detached && f.Machine!.FaultFactor > 0.5f).ToList();
        var living = ship.Rooms.Where(r => !r.Abandoned && !r.Detached).ToList();
        float vol = living.Sum(r => r.Volume);
        float avgO2 = vol > 0 ? living.Sum(r => r.Air.O2 * r.Volume) / vol : 0f;
        int breathers = w.Crew.Count(c => !c.Dead);
        if (gens.Count >= 2)
        {
            var spare = gens.OrderBy(f => f.Machine!.FaultFactor * (0.6f + 0.4f * f.Machine.Condition)).ThenByDescending(f => f.Id).First();
            var main = gens.Where(f => f != spare).OrderByDescending(f => f.Machine!.FaultFactor).First();
            bool enough = breathers * Atmosphere.BreathO2 <= 0.8f * Atmosphere.GeneratorCapacity * main.Machine!.FaultFactor * main.Machine.Rating;
            bool wasParked = _parkedO2 == spare.Id;
            bool keep = enough && (wasParked ? avgO2 >= 18.5f : avgO2 >= 19.5f) && !living.Any(r => r.Leaking);
            _parkedO2 = keep ? spare.Id : -1;
        }
        else _parkedO2 = -1;

        bool hurt = w.Crew.Any(c => !c.Dead && (c.Down || c.Vitals.Injury > 0.25f));
        foreach (var m in ship.Machines)
        {
            if (m.Body.Room.Detached) continue;
            bool park = m.Body.Id == _parkedPump || m.Body.Id == _parkedO2
                        || (m.Body.Type == FurnitureType.MedBed && !hurt)
                        || m.Body.Type == FurnitureType.EngineCore
                        || (m.Body.Type == FurnitureType.Console && m.Body.Room.Type != RoomType.Bridge)
                        || m.Body.Type == FurnitureType.SensorArray; // v10.1: 먹을 것이 먼저다 — 대신 운석을 미리 못 본다
            if (!park) continue;
            m.Parked = true;
            ParkedCount++;
        }
    }

    public float CircuitDemand(int circuit)
    {
        float kw = 0f;
        foreach (var room in _world.Ship.Rooms) if (room.Circuit == circuit && !room.Detached && !room.PowerCut) kw += RoomSystemsKw;
        foreach (var m in _world.Ship.Machines)
            if (m.Body.Room.Circuit == circuit && !m.Body.Room.PowerCut && m.Spec.PowerDraw > 0f && !m.Stopped) kw += m.Demand;
        return kw;
    }

    /// <summary>승무원이 배전반에서 회로를 일부러 내리거나 다시 올린다.</summary>
    public void SetManualOff(int circuit, bool off)
    {
        ManualOff[circuit] = off;
        CircuitFed[circuit] = !off && (CircuitLive[circuit] || FeedingJumper(circuit) != null);
        _world.Board.RequestScan();
    }

    /// <summary>그 회로로 전기를 줄 수 있는 살아 있는 임시 배선.</summary>
    public Jumper? FeedingJumper(int circuit) =>
        Jumpers.FirstOrDefault(j => j.To == circuit && !j.Burnt && CircuitLive[j.From] && !ManualOff[j.From]);

    /// <summary>승무원이 보조 발전기를 돌렸다.</summary>
    public void StartAux()
    {
        if (AuxFuel <= 0f) return;
        AuxRunning = true;
        _world.Log.Add(_world.Tick, LogKind.Ship, $"보조 발전기 가동 — A 회로(필수)만 {AuxRated:0}kW, 연료 {AuxFuel:0}시간분");
    }

    public void Update(float dtHours)
    {
        var ship = _world.Ship;

        // 1) 회로: 배전반의 차단기·단락, 그리고 임시 배선과 사람이 내린 회로
        var panel = Panel;
        // v8: 배전반이 방째로 떨어져 나갔거나(없음), 다시 붙였지만 아직 전력선을 잇지 않았으면 어느 회로에도 전기가 가지 않는다
        bool panelCut = panel == null || panel.Body.Room.PowerCut;
        if (panelCut != PanelCut)
        {
            PanelCut = panelCut;
            if (panelCut) _world.RaiseAlert("배전반과 끊겼다 — 모든 회로 정전", panel?.Body.Room, AlertLevel.Critical, shipWide: true);
            else _world.Log.Add(_world.Tick, LogKind.Ship, "배전반을 다시 이었다 — 회로에 전기가 돈다");
            _world.Board.RequestScan();
        }
        for (int i = 0; i < CircuitCount; i++)
        {
            bool live = !panelCut && !panel!.Faults.Any(f => f.Circuit == i);
            if (live && !CircuitLive[i]) _liveSince[i] = _world.Tick; // v10.10: 제 힘으로 다시 돈 때 (임시 배선을 걷을 때를 본다)
            CircuitLive[i] = live;
        }
        for (int i = 0; i < CircuitCount; i++)
            CircuitFed[i] = !ManualOff[i] && (CircuitLive[i] || FeedingJumper(i) != null);

        // 2) 냉각 → 원자로 (냉각을 거의 잃으면 긴급 정지. 저출력 수동 운전 중이면 자연 순환으로 버틴다)
        // v9: 냉각은 배관망을 따라 흐른다 (고온관·귀환관·분기·방열판·냉각수)
        CoolingCapacity = _world.Piping.ComputeCooling();
        var reactor = Reactor;
        if (LowPowerMode && CoolingCapacity >= RestartCoolingKw)
        {
            LowPowerMode = false;
            _world.Log.Add(_world.Tick, LogKind.Ship, "냉각 펌프가 돌기 시작했다 — 원자로 정상 운전으로 올린다");
        }
        if (ReactorOnline && !LowPowerMode && CoolingCapacity < ScramCoolingKw)
        {
            ReactorOnline = false;
            ReactorRamp = 0f;
            _world.RaiseAlert("원자로 긴급 정지(SCRAM) — 냉각 상실", reactor?.Body.Room, AlertLevel.Critical, shipWide: true);
            _world.History.Scrams++;
            _world.History.Add(_world, HistoryKind.Damage, "원자로 긴급 정지 — 냉각 상실", reactor?.Body.Room);
            _world.Board.RequestScan();
        }
        if (ReactorOnline && LowPowerMode && _world.Rng.Chance(0.12f * dtHours))
        {
            // 자연 순환만으로는 오래 못 버틴다
            ReactorOnline = false;
            LowPowerMode = false;
            ReactorRamp = 0f;
            if (reactor != null)
            {
                reactor.Wear = MathF.Min(1f, reactor.Wear + 0.15f);
                if (_world.Rng.Chance(0.3f)) _world.Machines.Break(reactor, FaultKind.ControlFault);
            }
            _world.RaiseAlert("원자로 과열 — 저출력 운전이 버티지 못하고 다시 정지", reactor?.Body.Room, AlertLevel.Critical, shipWide: true);
            _world.History.Scrams++;
            _world.History.Add(_world, HistoryKind.Damage, "원자로 과열 정지 — 저출력 수동 운전이 버티지 못했다", reactor?.Body.Room);
            _world.Board.RequestScan();
        }
        // v9: 노심이 너무 뜨거워지면 긴급 정지 (냉각이 갑자기 줄었는데 제어봉이 따라가지 못했다)
        if (ReactorOnline && ReactorTemperature >= OverheatScramC)
        {
            ReactorOnline = false;
            LowPowerMode = false;
            ReactorRamp = 0f;
            if (reactor != null) reactor.Wear = MathF.Min(1f, reactor.Wear + 0.05f);
            _world.RaiseAlert($"원자로 과열 긴급 정지 — 노심 {ReactorTemperature:0}℃", reactor?.Body.Room, AlertLevel.Critical, shipWide: true);
            _world.History.Scrams++;
            _world.History.Add(_world, HistoryKind.Damage, $"원자로 과열 긴급 정지 (노심 {ReactorTemperature:0}℃) — 냉각이 갑자기 줄었다", reactor?.Body.Room);
            _world.Board.RequestScan();
        }
        if (ReactorOnline) ReactorRamp = MathF.Min(1f, ReactorRamp + dtHours / RampHours);
        float reactorMax = reactor == null || !ReactorOnline ? 0f : ReactorMaxKw * reactor.Rating * reactor.Efficiency;
        float cooling = LowPowerMode ? NaturalCoolingKw + CoolingCapacity : CoolingCapacity * 0.95f;
        float target = MathF.Min(reactorMax, cooling) * ReactorRamp;
        // v9: 자동 제어봉은 한 시간에 60kW만큼만 출력을 내린다 — 냉각이 갑자기 줄면 그동안 넘치는 열이 노심을 데운다
        if (!ReactorOnline) _rodLimit = 0f;
        else if (target < _rodLimit) _rodLimit = MathF.Max(target, _rodLimit - (_world.Automation.Rods ? RodDropKwPerHour : ManualRodKwPerHour) * dtHours);
        else _rodLimit = target;
        ReactorLimit = _rodLimit;
        EffectiveCooling = LowPowerMode ? NaturalCoolingKw + CoolingCapacity : CoolingCapacity;

        // 3) 보조 발전기 (A 회로 전용)
        var aux = Aux;
        if (AuxRunning && (aux == null || aux.Stopped || AuxFuel <= 0f))
        {
            AuxRunning = false;
            _world.Log.Add(_world.Tick, LogKind.Warning, AuxFuel <= 0f ? "보조 발전기 연료가 떨어졌다" : "보조 발전기가 멈췄다");
        }
        if (AuxRunning && ReactorOnline && ReactorRamp >= 1f && ReactorLimit > 20f && BatteryPercent > 0.3f)
        {
            AuxRunning = false;
            _world.Log.Add(_world.Tick, LogKind.Ship, $"주 전력 복원 · 보조 발전기 정지 (연료 {AuxFuel:0.0}시간 남음)");
        }
        float auxAvailable = AuxRunning && aux != null ? AuxKw * aux.Rating * aux.Efficiency : 0f;

        // 4) 배터리
        BatteryCapacity = ship.FurnitureOf(FurnitureType.Battery)
            .Sum(f => BatteryKwh(f) * f.Machine!.FaultFactor * (0.5f + 0.5f * f.Machine.Condition))
            + Modules.Bonus(_world, FurnitureType.CapacitorBank); // v10.6 축전 모듈
        BatteryCharge = MathF.Min(BatteryCharge, BatteryCapacity);
        float batteryAvailable = dtHours > 0 ? MathF.Min(BatteryMaxDischargeKw, BatteryCharge / dtHours) : 0f;

        // 5) 소비자 배분: 우선순위 높은 것부터. A 회로는 보조 발전기 전력을 먼저 쓴다.
        UpdateParking(reactorMax);
        var consumers = new List<(int priority, float kw, int circuit, Action<bool> set)>();
        foreach (var room in ship.Rooms)
        {
            var r = room;
            // 떨어져 나갔거나 사출 준비로 전력을 끊은 방은 전기가 없다
            if (!CircuitFed[r.Circuit] || r.Detached || r.PowerCut) { r.Powered = false; continue; }
            consumers.Add((8, RoomSystemsKw, r.Circuit, on => r.Powered = on));
        }
        float parkedKw = 0f;
        foreach (var m in ship.Machines)
        {
            if (m.Spec.PowerDraw <= 0f) { m.Powered = true; continue; }
            if (m.Parked) { m.Powered = false; parkedKw += m.Demand; continue; } // v9.3 저출력 운영으로 내려 둠
            if (!CircuitFed[m.Body.Room.Circuit] || m.Body.Room.PowerCut) { m.Powered = false; continue; }
            var mm = m;
            // v9.3 저출력 운영: 재배대·냉장고·조리대·배식기(먹을 것)를 정수기·방 환기와 같은 줄로 올린다 (방 환기가 먼저)
            //    정제기는 사람이 붙어 일하는 동안만 같은 줄로 (금속판·필터를 뽑아야 고칠 수 있다)
            int prio = Brownout && (m.Body.Type is FurnitureType.GrowBed or FurnitureType.Fridge or FurnitureType.Stove or FurnitureType.MealDispenser
                                    || (m.Active && m.Body.Type == FurnitureType.Refinery))
                ? 8 : m.Spec.Priority;
            consumers.Add((prio, m.Demand, m.Body.Room.Circuit, on => mm.Powered = on));
        }
        // 같은 우선순위는 설계도 순서대로 (안정 정렬). v9.2: 자동화가 꺼지면 우선순위를 모른다 — 먼저 붙은 것부터 받는다
        if (_world.Automation.Priority) consumers = consumers.OrderByDescending(x => x.priority).ToList();

        float mainSupply = ReactorLimit + batteryAvailable;
        float mainUsed = 0f, auxUsed = 0f;
        Demand = 0f;
        ShedCount = 0;
        // 임시 배선으로 받는 회로는 배선이 버티는 만큼(정격의 1.25배)만 흐른다
        var viaJumper = new Jumper?[CircuitCount];
        var jumperUsed = new float[CircuitCount];
        foreach (var j in Jumpers) { j.Load = 0f; j.Active = false; }
        for (int i = 0; i < CircuitCount; i++)
            if (!CircuitLive[i] && CircuitFed[i]) viaJumper[i] = FeedingJumper(i);
        foreach (var (_, kw, circuit, set) in consumers)
        {
            Demand += kw;
            if (viaJumper[circuit] is Jumper jj && jumperUsed[circuit] + kw > jj.Capacity * 1.25f) { ShedCount++; set(false); continue; }
            bool on = false;
            if (circuit == 0 && auxUsed + kw <= auxAvailable + 0.001f) { auxUsed += kw; on = true; }
            else if (mainUsed + kw <= mainSupply + 0.001f) { mainUsed += kw; on = true; }
            if (on && viaJumper[circuit] != null) jumperUsed[circuit] += kw;
            if (!on) ShedCount++;
            set(on);
        }
        Delivered = mainUsed + auxUsed;
        AuxOutput = auxUsed;

        // v9.3: 원자로가 돌아도 수요를 못 대는지 / 저출력 운영 중에 전부 켜도 남는지
        FullDemand = Demand + parkedKw;
        bool steady = ReactorOnline && !LowPowerMode && ReactorRamp >= 1f;
        bool deficit = steady && ReactorLimit + auxAvailable + 0.5f < Demand && BatteryPercent < 0.35f;
        if (deficit) { if (DeficitSince < 0) DeficitSince = _world.Tick; }
        else DeficitSince = -1;
        bool surplus = Brownout && steady && ReactorLimit >= FullDemand + 3f;
        if (surplus) { if (SurplusSince < 0) SurplusSince = _world.Tick; }
        else SurplusSince = -1;

        // 5-b) 임시 배선 부하: 정격을 넘겨 받치고 있으면 달아오르다 타거나 불을 낸다
        for (int i = 0; i < CircuitCount; i++)
        {
            if (viaJumper[i] is not Jumper j) continue;
            j.Active = true;
            j.Load = jumperUsed[i];
            float over = j.Load / j.Capacity - 1f;
            if (over <= 0f || !_world.Rng.Chance((0.03f + 0.6f * over) * dtHours)) continue;
            if (_world.Rng.Chance(0.5f))
            {
                j.Burnt = true;
                j.Active = false;
                _world.RaiseAlert($"{(j.Permanent ? "예비" : "임시")} 배선({CircuitName(j.From)}→{CircuitName(j.To)})이 과부하로 타 버렸다", ship.RoomAt(j.At), AlertLevel.Warning, shipWide: true);
                _world.History.Add(_world, HistoryKind.Damage, $"{(j.Permanent ? "예비" : "임시")} 배선({CircuitName(j.From)}→{CircuitName(j.To)})이 과부하로 탔다", ship.RoomAt(j.At));
            }
            if (_world.Fire.Ignite(j.At, 0.3f)) _world.Log.Add(_world.Tick, LogKind.Warning, "달아오른 임시 배선에서 불이 붙었다");
            _world.Board.RequestScan();
        }
        if (AuxRunning) AuxFuel = MathF.Max(0f, AuxFuel - dtHours * MathF.Max(0.3f, auxUsed / MathF.Max(0.1f, AuxRated)));

        // 6) 원자로는 필요한 만큼만, 남으면 배터리 충전 (보조 발전기 여유분도 충전에 씀)
        float spare = auxAvailable - auxUsed;
        if (mainUsed <= ReactorLimit)
        {
            float room = dtHours > 0 ? (BatteryCapacity - BatteryCharge) / dtHours : 0f;
            float charge = MathF.Max(0f, MathF.Min(BatteryMaxChargeKw, MathF.Min(ReactorLimit - mainUsed + spare, room)));
            ReactorOutput = mainUsed + MathF.Max(0f, charge - spare);
            BatteryFlow = charge;
        }
        else
        {
            ReactorOutput = ReactorLimit;
            BatteryFlow = -(mainUsed - ReactorLimit);
        }
        BatteryCharge = Math.Clamp(BatteryCharge + BatteryFlow * dtHours, 0f, BatteryCapacity);

        // 7) 원자로 온도 (v9: 넘치는 열이 쌓이고, 냉각이 넉넉하면 식는다)
        if (!ReactorOnline) ReactorTemperature += (60f - ReactorTemperature) * (1f - MathF.Exp(-dtHours * 2f));
        else
        {
            float ratio = EffectiveCooling > 0.1f ? ReactorOutput / EffectiveCooling : 1.5f;
            float equilibrium = 200f + 140f * Math.Clamp(ratio, 0f, 1f);
            float excess = ReactorOutput - EffectiveCooling;
            if (excess > 0.05f) ReactorTemperature += excess / CoreHeatKwhPerC * dtHours;
            else ReactorTemperature += (equilibrium - ReactorTemperature) * (1f - MathF.Exp(-dtHours * 3f));
            if (ReactorTemperature >= OverheatWarnC && !_overheatWarned)
            {
                _overheatWarned = true;
                _world.RaiseAlert($"원자로 과열 — 노심 {ReactorTemperature:0}℃ (냉각이 모자라다)", reactor?.Body.Room, AlertLevel.Critical, shipWide: true);
            }
            else if (ReactorTemperature < OverheatWarnC - 30f) _overheatWarned = false;
        }

        // 8) 문은 양쪽 방 중 하나라도 전기가 있으면 자동
        foreach (var d in ship.Doors)
            d.Powered = !d.MotorBroken && ((d.RoomA?.Powered ?? false) || (d.RoomB?.Powered ?? false)); // v9.4: 구동기가 고장 나면 손으로
    }
}
