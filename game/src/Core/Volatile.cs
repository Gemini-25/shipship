using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>설비가 한계를 넘었을 때 어떻게 터지나.</summary>
public enum BlowKind
{
    None,
    ThermalRunaway, // 배터리·축전기: 열폭주 → 불·유독 연기·작은 폭발, 옆 셀로 번진다
    Hydrogen,       // 산소 발생기: 전해조에서 수소가 새어 차면 불꽃 하나에 터진다
    ArcFlash,       // 배전반: 아크 섬광 → 곁의 사람 화상·눈부심, 모든 회로 차단, 불
    FuelFire,       // 보조 발전기: 연료가 새어 불이 붙는다 (연료가 많으면 폭발)
    Combustion,     // 엔진: 연소실 파열 → 폭발·선체 손상
    Rupture,        // 냉각 펌프·열교환기: 과압 파열 → 증기·냉각수 손실
    Grease,         // 조리대: 기름 불
    Furnace,        // 정제기: 가열로 파열 → 불·파편
    Refrigerant,    // 냉장고: 냉매 누출 (유독 가스)
}

public sealed record BlastEvent(long Tick, Cell At, float Power, string Cause);

public sealed class VolatileStats
{
    public int Overheats;       // 한계를 넘긴 설비
    public int Explosions;
    public int ThermalRunaways;
    public int Hydrogen;
    public int ArcFlashes;
    public int FuelFires;
    public int Ruptures;
    public int Chain;           // 폭발이 옆 설비를 터뜨린 연쇄
    public int Backdrafts;
    public int CoAlarms;
    public int CoPoisoned;
    public int SparkFires;      // 짙은 산소에 불꽃이 튀어 난 불
    public int RubbleCells;
    public int RubbleCleared;
    public int CooledDown;      // 사람이 달아오른 설비를 내려 식혔다
    public int Cleaned;         // 소화 분말·그을음 청소
    public int DeepDischarges;  // 배터리가 바닥까지

    public override string ToString() =>
        $"과열 {Overheats} · 폭발 {Explosions}(열폭주 {ThermalRunaways} · 수소 {Hydrogen} · 아크 {ArcFlashes} · 연료 {FuelFires} · 파열 {Ruptures} · 연쇄 {Chain}) · " +
        $"역화 {Backdrafts} · 일산화탄소 경보 {CoAlarms}(중독 {CoPoisoned}) · 짙은 산소 불꽃 {SparkFires} · 잔해 {RubbleCells}칸(치움 {RubbleCleared}) · " +
        $"식힘 {CooledDown} · 청소 {Cleaned} · 배터리 바닥 {DeepDischarges}";
}

/// <summary>
/// v12.2 설비의 열·압력과 폭발, 잔해, 역화, 일산화탄소, 짙은 산소, 소화제의 대가.
/// 원칙 1(사고는 첫 충격만)을 지킨다: 설비는 제 사정(부하·마모·고장·주변 불·환기·진공)으로 달아오르고,
/// 한계를 넘으면 종류마다 다르게 터진다 — 그다음(불·연기·감압·정전·부상·잔해·연쇄)은 각 시스템이 이어받는다.
/// 사람은 달아오른 설비를 먼저 내려 식히고(전조처럼), 터진 뒤에는 잔해를 치우고 분말을 닦고 불탄 방을 조심해서 연다.
/// </summary>
public sealed class VolatileSystem
{
    private readonly World _w;
    public VolatileStats Stats { get; } = new();
    public List<BlastEvent> Blasts { get; } = new();
    private readonly HashSet<int> _coAlarmed = new();
    private long _deepSince = -1;

    public VolatileSystem(World w) => _w = w;

    /// <summary>시험용: 설비가 달아오르지도 터지지도 않는 배.</summary>
    public bool Inert { get; set; }

    public static BlowKind Mode(FurnitureType t) => t switch
    {
        FurnitureType.Battery or FurnitureType.CapacitorBank => BlowKind.ThermalRunaway,
        FurnitureType.OxygenGenerator => BlowKind.Hydrogen,
        FurnitureType.PowerPanel => BlowKind.ArcFlash,
        FurnitureType.AuxGenerator => BlowKind.FuelFire,
        FurnitureType.EngineCore => BlowKind.Combustion,
        FurnitureType.CoolantPump or FurnitureType.HeatExchanger or FurnitureType.WaterRecycler => BlowKind.Rupture,
        FurnitureType.Stove => BlowKind.Grease,
        FurnitureType.Refinery => BlowKind.Furnace,
        FurnitureType.Fridge => BlowKind.Refrigerant,
        _ => BlowKind.None,
    };

    public static string Name(BlowKind k) => k switch
    {
        BlowKind.ThermalRunaway => "열폭주",
        BlowKind.Hydrogen => "수소 폭발",
        BlowKind.ArcFlash => "아크 섬광",
        BlowKind.FuelFire => "연료 화재",
        BlowKind.Combustion => "연소실 파열",
        BlowKind.Rupture => "과압 파열",
        BlowKind.Grease => "기름 불",
        BlowKind.Furnace => "가열로 파열",
        BlowKind.Refrigerant => "냉매 누출",
        _ => "",
    };

    /// <summary>(달아오르는 빠르기, 식는 빠르기) — 한 시간 기준.</summary>
    private static (float heat, float cool) Profile(FurnitureType t) => t switch
    {
        FurnitureType.Battery or FurnitureType.CapacitorBank => (0.15f, 0.5f),
        FurnitureType.PowerPanel => (0.2f, 0.6f),
        FurnitureType.OxygenGenerator => (0.25f, 0.8f),
        FurnitureType.AuxGenerator => (0.45f, 0.7f),
        FurnitureType.EngineCore => (0.5f, 0.9f),
        FurnitureType.CoolantPump or FurnitureType.HeatExchanger => (0.25f, 0.8f),
        FurnitureType.WaterRecycler => (0.2f, 0.8f),
        FurnitureType.Stove => (0.35f, 0.9f),
        FurnitureType.Refinery => (0.4f, 0.8f),
        FurnitureType.Fridge => (0.2f, 0.8f),
        FurnitureType.MainComputer => (0.3f, 0.9f),
        _ => (0.12f, 0.8f),
    };

    private static bool Heaty(FaultKind k) => k is FaultKind.BearingWear or FaultKind.ShortCircuit or FaultKind.CellDegradation or FaultKind.HeatingElement
        or FaultKind.ElectrolyzerFault or FaultKind.InjectorClog or FaultKind.WiringFault or FaultKind.PumpSeized or FaultKind.CompressorFail or FaultKind.FurnaceFault;

    // ─────────────────────────────── 시스템 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (Inert) return;
        var ship = w.Ship;
        var p = w.Power;
        foreach (var m in ship.Machines.ToList())
        {
            var f = m.Body;
            var room = f.Room;
            var t = f.Type;
            if (t == FurnitureType.ReactorCore) continue; // 원자로는 제 모델(노심 온도·긴급 정지)이 있다
            var (heatRate, coolRate) = Profile(t);
            bool running = m.Powered && m.Active && !m.Stopped && !m.Parked;
            float gain = 0f;
            if (running && m.Spec.PowerDraw > 0f) gain = heatRate * (0.35f + 0.25f * (Tech.Of(m).Power - 1f));
            // 마모·고장·임시품은 더 달군다
            float stress = 1f + 1.2f * m.Wear * m.Wear + (m.Faults.Any(x => Heaty(x.Kind) && x.Circuit < 0) ? 1.3f : 0f) + (m.Grade == MachineGrade.Mk1 ? 0.4f : 0f);
            gain *= stress;
            switch (t)
            {
                case FurnitureType.Battery or FurnitureType.CapacitorBank when !m.Parked:
                    // 빨리 채우고 빨리 빼면 뜨겁다 (정전·블랙스타트 동안), 셀이 상했으면 더
                    float rate = p.BatteryCapacity > 1f ? MathF.Abs(p.BatteryFlow) / p.BatteryCapacity : 0f;
                    gain += heatRate * (0.2f + 2.5f * rate) * stress;
                    break;
                case FurnitureType.PowerPanel:
                    // 흐르는 전기만큼 + 단락 난 회로마다 + 임시 배선(정격을 넘기면 달아오른다)
                    float load = p.ReactorRated > 1f ? (p.Delivered / p.ReactorRated) : 0.3f;
                    gain += heatRate * (0.3f + 0.6f * load + 0.35f * m.Faults.Count(x => x.Kind == FaultKind.ShortCircuit)) + 0.08f * w.Power.Jumpers.Count(j => j.Active);
                    break;
                case FurnitureType.CoolantPump or FurnitureType.HeatExchanger:
                    // 노심이 뜨거울수록 냉각수 압력이 오른다 (과압)
                    if (running) gain += heatRate * MathF.Max(0f, (p.ReactorTemperature - 320f) / 60f);
                    break;
            }
            // 곁의 불이 달군다
            foreach (var d in Cell.Dirs8.Append(new Cell(0, 0)))
                foreach (var c in f.Cells.Take(4))
                {
                    float fire = w.Fire.At(c + d);
                    if (fire > 0f) { gain += 0.25f * fire; break; }
                }
            if (room.Air.Temperature > 40f) gain += (room.Air.Temperature - 40f) / 40f * 0.3f;
            // 식힘: 환기가 돌면 잘, 전기가 없으면 덜, 진공에서는 열이 빠질 데가 없다 (대류가 없다)
            float cool = coolRate * (room.VentOpen && room.Powered ? 1f : 0.4f) * (room.Air.Pressure < 20f ? 0.25f : 1f)
                         * (1f + 0.6f * Modules.Working(w, FurnitureType.HeatExchanger, room));
            m.Heat = MathF.Max(0f, m.Heat + (gain - cool * m.Heat) * dt);
            // 뜨거운 설비는 방을 데운다
            if (m.Heat > 0.5f) room.Air.Temperature = MathF.Min(90f, room.Air.Temperature + (m.Heat - 0.5f) * 30f * dt / MathF.Max(8f, room.Volume) * 20f);

            // 새는 기체: 수소(산소 발생기 전해조), 연료 증기(보조 발전기)
            if (t == FurnitureType.OxygenGenerator)
            {
                bool leaking = running && (m.Has(FaultKind.ElectrolyzerFault) || m.Heat > 0.7f);
                float vent = room.VentOpen && room.Powered ? 1.2f : 0.15f;
                m.Vapor = Math.Clamp(m.Vapor + ((leaking ? 0.35f : 0f) - vent * m.Vapor) * dt, 0f, 1f);
            }
            else if (t == FurnitureType.AuxGenerator)
            {
                bool leaking = m.Has(FaultKind.InjectorClog) && p.AuxRunning;
                float vent = room.VentOpen && room.Powered ? 1f : 0.1f;
                m.Vapor = Math.Clamp(m.Vapor + ((leaking ? 0.3f : 0f) - vent * m.Vapor) * dt, 0f, 1f);
                // 배기: 환기가 약한 곳에서 돌리면 일산화탄소가 찬다
                if (p.AuxRunning && m.Efficiency > 0f)
                    room.Air.CO = MathF.Min(1f, room.Air.CO + (room.VentOpen && room.Powered ? 3f : 16f) * dt / MathF.Max(8f, room.Volume));
            }

            // 소화 분말·그을음: 효율과 전자 장비 (전기 쪽 설비는 가끔 합선)
            if (m.Fouled > 0.5f && m.Spec.Skill == Skill.Electrical && m.Faults.Count == 0 && w.Rng.Chance(0.02f * m.Fouled * dt))
                w.Machines.Break(m, FaultKind.WiringFault);
            // 연기는 감지기를 흐린다
            if (room.Air.Smoke > 0.2f) m.SensorCal = MathF.Max(0.3f, m.SensorCal - 0.04f * room.Air.Smoke * dt);

            // 한계를 넘으면 터진다 (종류마다)
            var mode = Mode(t);
            if (mode == BlowKind.None) continue;
            bool spark = SparkIn(room, m);
            if (m.Heat > 0.9f && w.Rng.Chance(MathF.Min(1f, (m.Heat - 0.9f) * 5f) * dt)) Blow(m, mode, "과열");
            else if (m.Vapor > 0.55f && spark && w.Rng.Chance(2f * dt)) Blow(m, mode, "불꽃");
        }

        // 방마다: 역화 · 일산화탄소 · 짙은 산소
        foreach (var room in ship.LiveRooms)
        {
            UpdateBackdraft(room, dt);
            UpdateCo(room, dt);
            UpdateRichO2(room, dt);
        }

        // 배터리가 바닥까지 떨어지면 셀이 상한다 (용량이 영구히 준다 — 위기의 흔적)
        if (p.BatteryPercent < 0.05f && p.BatteryCapacity > 1f)
        {
            if (_deepSince < 0)
            {
                _deepSince = w.Tick;
                Stats.DeepDischarges++;
                w.Log.Add(w.Tick, LogKind.Warning, "배터리가 바닥까지 떨어졌다 — 셀이 상하기 시작한다");
            }
            foreach (var b in ship.FurnitureOf(FurnitureType.Battery)) b.Machine!.Condition = MathF.Max(0.3f, b.Machine.Condition - 0.02f * dt);
        }
        else if (p.BatteryPercent > 0.3f && _deepSince >= 0)
        {
            float hrs = (w.Tick - _deepSince) / (float)SimTime.TicksPerHour;
            if (hrs > 0.5f) w.History.Add(w, HistoryKind.Damage, $"배터리가 {hrs:0.0}시간 바닥이었다 — 셀이 상해 용량이 줄었다", ship.FurnitureOf(FurnitureType.Battery).FirstOrDefault()?.Room);
            _deepSince = -1;
        }
    }

    /// <summary>불꽃이 튈 거리: 그 방의 불, 합선·배선 고장, 달아오른 전기 설비, 용접 중인 사람.</summary>
    private bool SparkIn(Room room, Machine? except)
    {
        var w = _w;
        if (w.Fire.CountIn(room) > 0) return true;
        foreach (var f in room.Furniture)
        {
            if (f.Machine is not Machine m || m == except) continue;
            if (m.Has(FaultKind.ShortCircuit) || m.Has(FaultKind.WiringFault) || m.Spec.Skill == Skill.Electrical && m.Heat > 0.8f) return true;
        }
        var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine;
        if (panel != null && panel.Faults.Any(x => x.Circuit == room.Circuit && x.Kind == FaultKind.ShortCircuit)) return true;
        return w.Crew.Any(c => c.Room == room && c.Job?.Order?.Kind is WorkKind.RepairHull or WorkKind.WeldBulkhead or WorkKind.ReplacePanel);
    }

    // ─────────────────────────────── 터짐 ───────────────────────────────

    /// <summary>설비가 제 방식으로 터진다.</summary>
    public void Blow(Machine m, BlowKind mode, string why)
    {
        // v12.2 인과 사슬: 설비가 터진 까닭(고장·냉각 끊김·불)에 잇고, 터지며 생긴 피해는 이 폭발의 자식
        var w = _w;
        int parent = w.Causes.Context >= 0 ? w.Causes.Context : w.Causes.ParentFor(m);
        string text = $"{m.Name} {Name(mode)}" + (why.Length > 0 && why != "시험" ? $" ({why})" : "");
        int node = parent >= 0 ? w.Causes.Effect(CauseKind.Explosion, "", text, m.Body.Room, m.Body.Center, parent, lasting: false)
            : w.Causes.Root(CauseKind.Explosion, text, m.Body.Room, m.Body.Center, observer: w.Causes.ConsumeObserver());
        w.Causes.Hit(m.Body.Room, node);
        using (w.Causes.Because(node)) BlowCore(m, mode, why);
    }

    private void BlowCore(Machine m, BlowKind mode, string why)
    {
        var w = _w;
        var f = m.Body;
        var room = f.Room;
        var at = f.UseSpots.FirstOrDefault(s => w.Ship.IsOpenFloor(s));
        if (at == default) at = f.Cells[0];
        Stats.Overheats++;
        string what = Name(mode);
        float power = 0f;
        switch (mode)
        {
            case BlowKind.ThermalRunaway:
                Stats.ThermalRunaways++;
                power = 0.3f;
                w.Fire.Ignite(at, 0.7f);
                room.Air.Toxin = MathF.Min(1f, room.Air.Toxin + 18f / MathF.Max(8f, room.Volume));
                room.Air.Smoke = MathF.Min(1f, room.Air.Smoke + 0.4f);
                m.Faults.Clear();
                w.Machines.Break(m, FaultKind.Wrecked);
                // 옆 셀로 번진다: 같은 방의 다른 배터리·축전기가 달아오른다
                foreach (var o in room.Furniture.Where(x => x != f && x.Machine != null && Mode(x.Type) == BlowKind.ThermalRunaway))
                    o.Machine!.Heat += 0.45f;
                break;
            case BlowKind.Hydrogen:
                Stats.Hydrogen++;
                power = 0.4f + 0.5f * m.Vapor;
                w.Fire.Ignite(at, 0.5f);
                m.Faults.Clear();
                w.Machines.Break(m, FaultKind.Wrecked);
                break;
            case BlowKind.ArcFlash:
                Stats.ArcFlashes++;
                power = 0f; // 섬광과 열 (압력파는 작다)
                // 곁의 사람: 화상과 눈부심 (우주복 헬멧이면 덜)
                foreach (var c in w.Crew.Where(c => !c.Dead && (c.Position - f.Center).LengthSquared() < 2.6f * 2.6f))
                {
                    float dmg = (c.Suit != null ? 0.12f : 0.3f) * w.Rng.Range(0.7f, 1.1f);
                    c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg);
                    NeedsSystem.AddInjury(c.Vitals, dmg * 0.8f, "아크 화상");
                    c.Interrupt(w);
                    MarkLog.Add(c.Memory.Marks, w.Tick, "아크 섬광에 데었다");
                }
                w.Fire.Ignite(at, 0.4f);
                // 모든 회로가 떨어진다
                for (int circuit = 0; circuit < PowerGrid.CircuitCount; circuit++)
                    if (!m.Faults.Any(x => x.Circuit == circuit))
                        m.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = circuit });
                        w.Causes.OnFault(m, m.Faults[^1]);
                m.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = room.Circuit });
                w.Causes.OnFault(m, m.Faults[^1]);
                m.FaultCount++;
                break;
            case BlowKind.FuelFire:
                Stats.FuelFires++;
                power = w.Power.AuxFuel > 4f ? 0.45f : 0.15f;
                foreach (var d in Cell.Dirs4.Append(new Cell(0, 0))) w.Fire.Ignite(at + d, 0.6f);
                w.Power.AuxFuel = MathF.Max(0f, w.Power.AuxFuel * 0.6f);
                w.Machines.Break(m, FaultKind.InjectorClog);
                break;
            case BlowKind.Combustion:
                power = 0.55f;
                w.Fire.Ignite(at, 0.5f);
                w.Machines.Break(m, FaultKind.Wrecked);
                break;
            case BlowKind.Rupture:
                Stats.Ruptures++;
                power = 0.25f;
                // 가장 가까운 관이 터진다 (증기·냉각수)
                var seg = w.Piping.Segments.OrderBy(s => s.Path.Count == 0 ? 999f : s.Path.Min(c => (c.Center - f.Center).LengthSquared())).FirstOrDefault();
                if (seg != null && seg.Path.Count > 0) w.Piping.Damage(seg, 0.8f, seg.Path.OrderBy(c => (c.Center - f.Center).LengthSquared()).First(), "과압 파열");
                w.Machines.Break(m, FaultKind.PumpSeized);
                room.Air.Temperature = MathF.Min(90f, room.Air.Temperature + 12f);
                break;
            case BlowKind.Grease:
                w.Fire.Ignite(at, 0.55f);
                w.Machines.Break(m, FaultKind.HeatingElement);
                break;
            case BlowKind.Furnace:
                power = 0.3f;
                w.Fire.Ignite(at, 0.5f);
                w.Machines.Break(m, FaultKind.FurnaceFault);
                break;
            case BlowKind.Refrigerant:
                room.Air.Toxin = MathF.Min(1f, room.Air.Toxin + 12f / MathF.Max(8f, room.Volume));
                w.Machines.Break(m, FaultKind.GasLeak);
                break;
        }
        m.Heat = MathF.Min(m.Heat, 0.3f);
        m.Vapor = 0f;
        MarkLog.Add(m.Marks, w.Tick, $"{what} ({why})");
        MarkLog.Add(room.Marks, w.Tick, $"{m.Name} {what}");
        w.History.Add(w, HistoryKind.Incident, $"{m.Name} {what} — {why}" + (power > 0f ? " · 폭발" : ""), room);
        w.RaiseAlert($"{m.Name} {what} — {room.Name}", room, power >= 0.3f || mode == BlowKind.ArcFlash ? AlertLevel.Critical : AlertLevel.Warning, shipWide: true);
        if (power > 0f) Blast(at, power, $"{m.Name} {what}", m);
        w.Board.RequestScan();
    }

    /// <summary>
    /// 폭발: 가까울수록 크게 — 벽(선체면 구멍), 문(구동기·잔해가 끼여 안 닫힘), 설비(고장·달아오름 — 연쇄), 관·전선, 사람(파편·화상·넘어짐), 보관함, 불, 잔해.
    /// </summary>
    public void Blast(Cell at, float power, string cause, Machine? source = null)
    {
        var cl = _w.Causes;
        if (cl.Context >= 0) { cl.Hit(_w.Ship.RoomAt(at), cl.Context); BlastCore(at, power, cause, source); return; }
        int node = cl.Root(CauseKind.Explosion, $"폭발 — {_w.Ship.RoomAt(at)?.Name ?? "선체"}" + (cause.Length > 0 && cause != "시험" ? $" ({cause})" : ""), _w.Ship.RoomAt(at), at.Center, observer: cl.ConsumeObserver());
        cl.Hit(_w.Ship.RoomAt(at), node);
        using (cl.Because(node)) BlastCore(at, power, cause, source);
    }

    private void BlastCore(Cell at, float power, string cause, Machine? source)
    {
        var w = _w;
        var ship = w.Ship;
        Stats.Explosions++;
        Blasts.Add(new BlastEvent(w.Tick, at, power, cause));
        if (Blasts.Count > 12) Blasts.RemoveAt(0);
        float r = 1.5f + 3.2f * power;
        var room = ship.RoomAt(at);
        // 벽
        foreach (var (cell, _) in ship.Walls.ToList())
        {
            float d = (cell.Center - at.Center).Length();
            if (d > r) continue;
            Hull.Damage(ship, cell, 0.7f * power * (1f - d / (r + 0.5f)) * w.Rng.Range(0.8f, 1.1f));
        }
        // 문: 구동기가 상하고, 가까우면 잔해가 끼여 안 닫힌다
        foreach (var door in ship.Doors.Where(dd => !dd.Removed))
        {
            float d = (door.Cell.Center - at.Center).Length();
            if (d > r) continue;
            w.Fixtures.OnDebris(door.Cell, power * (1f - d / (r + 0.5f)), room);
        }
        // 설비: 수명이 깎이고 고장 나고, 터질 수 있는 것은 달아오른다 (연쇄)
        var hurtMachines = 0;
        foreach (var f in ship.Furniture.Where(ff => ff.Machine != null && !ff.Room.Detached && !ff.Stowed).ToList())
        {
            var m = f.Machine!;
            if (m == source) continue;
            float d = (f.Center - at.Center).Length();
            if (d > r) continue;
            float k = power * (1f - d / (r + 0.5f));
            m.Condition = MathF.Max(0.02f, m.Condition - 0.45f * k);
            m.Heat += 0.8f * k;
            Procedures.DamageLinks(w, m, 2.6f * k, 2.1f * k, cause); // v12.1 설비 전선·관
            if (w.Rng.Chance(0.8f * k)) { w.Machines.Break(m); hurtMachines++; }
            if (Mode(f.Type) != BlowKind.None && m.Heat > 0.9f && w.Rng.Chance(0.5f * k))
            {
                Stats.Chain++;
                // 다음 틱에 터진다 (한 번에 다 터지지 않게)
                m.Heat = MathF.Max(m.Heat, 1.2f);
            }
        }
        // 관: 가까운 칸을 지나는 관이 상한다
        foreach (var seg in w.Piping.Segments.ToList())
        {
            var near = seg.Path.Where(c => (c.Center - at.Center).Length() <= r * 0.8f).OrderBy(c => (c.Center - at.Center).LengthSquared()).FirstOrDefault();
            if (near == default && !seg.Path.Contains(default)) continue;
            if (seg.Path.Count == 0 || (near.Center - at.Center).Length() > r * 0.8f) continue;
            float k = power * (1f - (near.Center - at.Center).Length() / (r + 0.5f));
            if (w.Rng.Chance(0.9f * k)) w.Piping.Damage(seg, 0.5f * k + 0.1f, near, cause);
        }
        // 배 전체 망: 가까운 간선·급수관·덕트
        w.Net.DamageNear(at, r, 1.3f * power, cause);
        // 전선: 그 방 회로가 끊긴다
        if (room != null && w.Rng.Chance(0.6f * power) && ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine panel
            && !panel.Faults.Any(x => x.Circuit == room.Circuit))
        {
            panel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = room.Circuit });
            w.Causes.OnFault(panel, panel.Faults[^1]);
            panel.FaultCount++;
            w.History.CircuitFaults++;
        }
        // 사람: 가까울수록 크게 (우주복이면 덜), 넘어지고 놀란다
        var hurt = new List<CrewMember>();
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside) continue;
            float d = (c.Position - at.Center).Length();
            if (d > r) continue;
            float k = power * (1f - d / (r + 0.5f));
            float dmg = (0.15f + 0.5f * k) * w.Rng.Range(0.7f, 1.1f) * (c.Suit != null ? 0.55f : 1f);
            c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg);
            NeedsSystem.AddInjury(c.Vitals, dmg * 0.85f, "폭발 파편·화상");
            c.Interrupt(w);
            hurt.Add(c);
            Memory.Frighten(w, c, c.Room, 0.3f + 0.4f * k, "폭발이 났다");
            Memory.Shake(w, c, 0.06f + 0.15f * k, "폭발");
            MarkLog.Add(c.Memory.Marks, w.Tick, $"폭발에 휘말렸다 ({cause})");
        }
        // 폭발음과 진동: 온 배가 깬다 (작은 것은 가까운 방만)
        foreach (var c in w.Crew.Where(c => !c.Dead && (power >= 0.3f || c.Room == room))) c.Jolt(w);
        // 멀리서도 놀란다 (같은 방·옆방)
        foreach (var c in w.Crew.Where(c => !c.Dead && !hurt.Contains(c) && c.Room != null && room != null && (c.Room == room || c.Room.Doors.Any(dd => dd.RoomA == room || dd.RoomB == room))))
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.12f);
        // 보관함: 물건이 부서지고 흩어진다
        foreach (var f in ship.Containers.Where(ff => (ff.Center - at.Center).Length() <= r * 0.7f))
        {
            var inv = f.Storage!;
            if (inv.Total == 0) continue;
            var kind = w.Rng.Pick(inv.Contents.Select(x => x.kind).ToList());
            int lost = inv.Take(kind, w.Rng.Range(1, 4));
            if (lost > 0) w.Log.Add(w.Tick, LogKind.Warning, $"폭발로 {f.Label}의 {ItemKinds.Name(kind)} {lost}개가 부서졌다");
        }
        // 불과 연기, 열
        if (power >= 0.25f)
            foreach (var d in Cell.Dirs8)
                if (w.Rng.Chance(0.35f * power)) w.Fire.Ignite(at + d, 0.3f);
        if (room != null)
        {
            room.Air.Smoke = MathF.Min(1f, room.Air.Smoke + 0.5f * power);
            room.Air.Temperature = MathF.Min(95f, room.Air.Temperature + 25f * power);
            room.Air.CO = MathF.Min(1f, room.Air.CO + 0.15f * power);
        }
        // 잔해: 무너진 선반·패널·천장재가 바닥과 문을 막는다 (치워야 지나간다)
        int rubble = 0;
        for (int dy = -(int)r; dy <= (int)r; dy++)
            for (int dx = -(int)r; dx <= (int)r; dx++)
            {
                var c = new Cell(at.X + dx, at.Y + dy);
                float d = (c.Center - at.Center).Length();
                if (d > r * 0.8f || d < 0.5f) continue;
                var kind = ship.Grid.Kind(c);
                if (kind != TileKind.Floor && kind != TileKind.Door) continue;
                if (kind == TileKind.Floor && ship.FurnitureAt(c) != null) continue;
                float k = power * (1f - d / (r + 0.5f));
                if (!w.Rng.Chance(0.7f * k)) continue;
                float amount = MathF.Min(1f, 0.4f + k);
                ship.Rubble[c] = MathF.Max(ship.Rubble.GetValueOrDefault(c), amount);
                if (ship.DoorAt(c) is Door door && amount >= 0.5f) door.Blocked = true;
                rubble++;
                // 그 칸에 서 있던 사람은 옆으로 밀려난다
                foreach (var cm in w.Crew.Where(cm => cm.Cell == c && !cm.Dead))
                {
                    var free = Cell.Dirs8.Select(dd => c + dd).FirstOrDefault(x => ship.IsWalkable(x) && !ship.Rubble.ContainsKey(x));
                    if (free != default) { cm.Position = free.Center; cm.PreviousPosition = cm.Position; }
                }
            }
        Stats.RubbleCells += rubble;
        if (rubble > 0) w.Paths.Invalidate();
        w.Structure.Touch();
        if (source == null)
            w.History.Add(w, HistoryKind.Incident, $"{room?.Name ?? "?"}에서 폭발 — {cause}", room);
        if (hurt.Count > 0 || rubble > 0 || hurtMachines > 0)
            w.Log.Add(w.Tick, LogKind.Warning, $"폭발({cause}) — 부상 {hurt.Count} · 설비 {hurtMachines}대 · 잔해 {rubble}칸");
        w.Board.RequestScan();
    }

    // ─────────────────────────────── 방: 역화 · 일산화탄소 · 짙은 산소 ───────────────────────────────

    /// <summary>
    /// 역화: 산소가 모자라 꺼진 뜨거운 방에는 타다 만 가스가 고인다. 문을 확 열면 산소가 들이쳐 불길이 터져 나온다.
    /// 식을 때까지 기다리거나(환기가 조금씩 되면 천천히 걷힌다), 문틈으로 조금씩 공기를 넣어 태워 없앤다.
    /// </summary>
    private void UpdateBackdraft(Room room, float dt)
    {
        var w = _w;
        var air = room.Air;
        // 쌓임: 불이 산소를 다 먹어 사그라드는데 방은 아직 뜨겁다
        if (w.Fire.CountIn(room) > 0 && air.O2 < 13f && air.Temperature > 50f && air.Pressure > 40f)
            room.Backdraft = MathF.Min(1f, room.Backdraft + 0.6f * dt);
        if (room.Backdraft <= 0f) return;
        // 걷힘: 식으면, 산소가 천천히 들어오면(환기), 진공이 되면
        if (air.Temperature < 42f) room.Backdraft = MathF.Max(0f, room.Backdraft - 0.25f * dt);
        if (air.Pressure < 30f) room.Backdraft = 0f;
        if (room.VentOpen && room.Powered && air.O2 > 15f) room.Backdraft = MathF.Max(0f, room.Backdraft - 0.6f * dt);
        if (room.Backdraft < 0.3f) return;
        if (!room.BackdraftKnown && (w.Automation.Alarms && room.Powered || w.Crew.Any(c => c.Room != null && c.Room.Doors.Any(d => d.RoomA == room || d.RoomB == room) && c.IsAwake)))
        {
            room.BackdraftKnown = true;
            w.RaiseAlert($"{room.Name} 역화 위험 — 뜨겁고 산소가 없다 (식거나 조금씩 환기할 때까지 문을 열지 마라)", room, AlertLevel.Warning, shipWide: true);
            w.Board.RequestScan();
        }
        // 터짐: 문이 크게 열렸고 건너편에 산소가 있다
        foreach (var door in room.Doors)
        {
            if (door.Removed || door.Openness < 0.4f) continue;
            var other = door.RoomA == room ? door.RoomB : door.RoomA;
            if (other == null || other.Air.O2 < 15f) continue;
            Stats.Backdrafts++;
            float power = 0.2f + 0.3f * room.Backdraft;
            room.Backdraft = 0f;
            room.BackdraftKnown = false;
            var inner = room.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => (c.Center - door.Cell.Center).LengthSquared()).Take(5).ToList();
            foreach (var c in inner) w.Fire.Ignite(c, 0.7f);
            air.O2 += 3f;
            w.History.Add(w, HistoryKind.Incident, $"{room.Name} 역화 — 문이 열리자 불길이 터져 나왔다", room);
            w.RaiseAlert($"{room.Name} 역화!", room, AlertLevel.Critical, shipWide: true);
            Blast(door.Cell, power, $"{room.Name} 역화");
            break;
        }
    }

    /// <summary>
    /// 일산화탄소: 산소가 모자란 불, 환기가 약한 곳의 보조 발전기 배기에서 생긴다. 보이지도 냄새도 없다 —
    /// 경보(전기·주 컴퓨터)가 있어야 알고, 없으면 두통·졸음으로만 온다. 잠든 사람이 가장 위험하다.
    /// </summary>
    private void UpdateCo(Room room, float dt)
    {
        var w = _w;
        var air = room.Air;
        if (air.CO <= 0.001f) { air.CO = 0f; _coAlarmed.Remove(room.Id); return; }
        // 걷힘: 팬이 돌면 세정기 쪽으로 빠진다, 아니면 아주 천천히
        air.CO = MathF.Max(0f, air.CO - (room.VentOpen && room.Powered ? 0.8f * (1f + Modules.Bonus(w, FurnitureType.Scrubber)) : 0.04f) * air.CO * dt);
        if (air.CO > 0.12f && room.Powered && w.Automation.Alarms && _coAlarmed.Add(room.Id))
        {
            Stats.CoAlarms++;
            room.CoKnown = true;
            w.RaiseAlert($"{room.Name} 일산화탄소 경보 ({air.CO * 1000:0}ppm쯤) — 환기하고 나가라", room, AlertLevel.Critical, shipWide: false);
            w.Board.RequestScan();
        }
        if (air.CO < 0.05f) room.CoKnown = false;
    }

    /// <summary>
    /// 짙은 산소: 산소 발생기가 제 방에 갇혀 돌거나(환기가 닫혔다), 산소관이 새면 산소가 짙어진다.
    /// 짙은 산소에서는 합선·용접·달아오른 설비의 불꽃 하나가 불이 되고, 불은 빨리 번진다. 환기를 열어 섞거나 발생기를 내린다.
    /// </summary>
    private void UpdateRichO2(Room room, float dt)
    {
        var w = _w;
        var air = room.Air;
        // 발생기가 제 방에 갇혀 돈다
        if (!room.VentOpen && !room.Leaking)
            foreach (var f in room.Furniture.Where(x => x.Type == FurnitureType.OxygenGenerator && x.Machine is { Efficiency: > 0f }))
                air.O2 = MathF.Min(60f, air.O2 + Atmosphere.GeneratorCapacity * f.Machine!.Efficiency * f.Machine.Rating * 0.5f * dt / MathF.Max(8f, room.Volume));
        // 산소관 누출: 탱크에서 방으로
        if (room.O2Leak > 0f && w.Air.Reserve > 0f)
        {
            float amount = MathF.Min(w.Air.Reserve, room.O2Leak * dt * 40f);
            w.Air.Reserve -= amount;
            air.O2 = MathF.Min(70f, air.O2 + amount / MathF.Max(8f, room.Volume));
        }
        if (air.O2 < 25f || air.Pressure < 50f) { room.RichKnown = false; return; }
        if (!room.RichKnown && room.Powered && w.Automation.Alarms)
        {
            room.RichKnown = true;
            w.RaiseAlert($"{room.Name} 산소 농도 {air.O2 / MathF.Max(1f, air.Pressure) * 100:0}% — 불꽃 조심 (환기·발생기 내리기)", room, AlertLevel.Warning, shipWide: false);
            w.Board.RequestScan();
        }
        // 불꽃이 불이 된다
        if (w.Fire.CountIn(room) == 0 && SparkIn(room, null) && w.Rng.Chance(MathF.Min(1f, (air.O2 - 25f) / 10f) * 1.5f * dt))
        {
            var cell = room.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => c.X * 31 + c.Y).FirstOrDefault();
            if (cell != default && w.Fire.Ignite(cell, 0.45f))
            {
                Stats.SparkFires++;
                w.History.Add(w, HistoryKind.Incident, $"{room.Name} — 짙은 산소에 불꽃이 튀어 불이 붙었다", room);
            }
        }
    }

    /// <summary>사람이 달아오른 설비를 내려 식힌다 (식으면 다시 올린다).</summary>
    public void CoolDown(Machine m, CrewMember c)
    {
        var w = _w;
        m.Parked = true;
        m.CoolingDown = true;
        m.Heat = MathF.Max(0f, m.Heat - 0.15f);
        m.Vapor = MathF.Max(0f, m.Vapor - 0.3f);
        Stats.CooledDown++;
        MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: 달아올라 내려 식힘 ({m.Heat * 100:0}%)");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.EulReul(m.Name)} 내려 식힌다 (열 {m.Heat * 100:0}%)", c.Id);
    }

    /// <summary>시스템 틱 끝: 식은 설비를 다시 올린다.</summary>
    public void Resume()
    {
        foreach (var m in _w.Ship.Machines)
            if (m.CoolingDown && m.Heat < 0.35f && m.Vapor < 0.2f)
            {
                m.CoolingDown = false;
                m.Parked = false;
                _w.Log.Add(_w.Tick, LogKind.Ship, $"{m.Name}이(가) 식어 다시 돈다");
            }
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v12.2 달아오른 설비 식히기 · 잔해 치우기 · 분말·그을음 청소 · 역화 방 조금씩 환기 · 산소관 막기 · 일산화탄소 방 환기.</summary>
    private void ScanVolatile(Poster post)
    {
        var w = _world;
        var ship = w.Ship;
        foreach (var m in ship.Machines)
        {
            if (m.Body.Room.Abandoned || m.Body.Room.OffLimits) continue;
            var mode = VolatileSystem.Mode(m.Body.Type);
            bool known = w.Automation.MainOnline && m.Body.Room.Powered || w.Crew.Any(c => c.Room == m.Body.Room && c.IsAwake);
            if (!m.CoolingDown && known && (m.Heat > 0.72f || m.Vapor > 0.35f) && mode != BlowKind.None)
                post(WorkKind.CoolDown, WorkTarget.Of(m.Body), MathF.Min(1.2f, 0.6f + (m.Heat - 0.72f) * 2f + m.Vapor * 0.5f), m.Spec.Skill,
                    m.Vapor > 0.35f ? $"{(m.Body.Type == FurnitureType.OxygenGenerator ? "수소" : "연료 증기")} {m.Vapor * 100:0}% — 불꽃 하나면 터진다 · 내리고 환기"
                        : $"열 {m.Heat * 100:0}% — {VolatileSystem.Name(mode)} 위험 · 내려 식힌다");
            if (m.Fouled > 0.3f && !m.Body.Room.Leaking && w.Fire.CountIn(m.Body.Room) == 0)
                post(WorkKind.CleanUp, WorkTarget.Of(m.Body), 0.2f + 0.25f * m.Fouled, Skill.Mechanics, $"소화 분말·그을음 {m.Fouled * 100:0}% — 효율이 떨어지고 전자 장비가 합선된다");
        }
        // 잔해: 문과 통로 먼저 (한 번에 여덟 칸까지)
        foreach (var (cell, amount) in ship.Rubble.OrderByDescending(kv => ship.DoorAt(kv.Key) != null ? 2 : ship.RoomAt(kv.Key)?.Type == RoomType.Corridor ? 1 : 0)
                     .ThenBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).Take(8))
        {
            var room = ship.RoomAt(cell);
            if (room?.Leaking == true || room?.Abandoned == true) continue;
            bool door = ship.DoorAt(cell) != null;
            post(WorkKind.ClearRubble, WorkTarget.AtCell(cell, room), door ? 0.8f : room?.Type == RoomType.Corridor ? 0.65f : 0.4f, Skill.Mechanics,
                door ? "문에 잔해가 끼여 닫히지 않는다" : $"잔해 {amount * 100:0}% — 길을 막는다");
        }
        foreach (var room in ship.LiveRooms)
        {
            if (room.Backdraft >= 0.3f && room.BackdraftKnown && w.Fire.CountIn(room) == 0)
                post(WorkKind.BleedRoom, WorkTarget.OfRoom(room), 0.75f, Skill.Mechanics, $"역화 위험 {room.Backdraft * 100:0}% · {room.Air.Temperature:0}℃ — 문틈으로 조금씩 공기를 넣어 태워 없앤다 (우주복)");
            if (room.O2Leak > 0f)
                post(WorkKind.SealO2Line, WorkTarget.OfRoom(room), 0.85f, Skill.Mechanics, $"산소관 누출 · 방 산소 {room.Air.O2:0}kPa — 실링폼 1");
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>달아오른 설비를 내려 식힌다 (수소·연료 증기면 환기도).</summary>
    private static Job? CoolDown(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var m = o.Target.Furniture!.Machine!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.15f, m.Spec.Skill, m.Body.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            world.Volatile.CoolDown(m, cm);
            if (m.Body.Room.VentOpen == false && m.Vapor > 0.2f) m.Body.Room.VentOpen = true; // 손으로 댐퍼를 연다
            return true;
        }));
        return Wrap(a, o, c, w, "식히기", toils, $"{m.Name} 내려 식히기 ({o.Detail})");
    }

    /// <summary>잔해를 치운다 (쓸 만한 금속판이 나오기도).</summary>
    private static Job? ClearRubble(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var cell = o.Target.Cell;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.3f + 0.3f * w.Ship.Rubble.GetValueOrDefault(cell), Skill.Mechanics, cell.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            if (!world.Ship.Rubble.Remove(cell)) return true;
            if (world.Ship.DoorAt(cell) is Door door) door.Blocked = false;
            world.Volatile.Stats.RubbleCleared++;
            world.Paths.Invalidate();
            if (world.Rng.Chance(0.35f) && world.Ship.Containers.FirstOrDefault(f => f.Storage!.Free > 0) is Furniture box)
            {
                box.Storage!.Add(ItemKind.Plate, 1);
                world.Log.Add(world.Tick, LogKind.Work, "잔해를 치웠다 — 쓸 만한 금속판 하나를 건졌다", cm.Id);
            }
            else world.Log.Add(world.Tick, LogKind.Work, "잔해를 치웠다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "잔해 치우기", toils, "잔해를 치운다");
    }

    /// <summary>소화 분말·그을음을 닦는다.</summary>
    private static Job? CleanUp(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var m = o.Target.Furniture!.Machine!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.25f, Skill.Mechanics, m.Body.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            m.Fouled = 0f;
            world.Volatile.Stats.Cleaned++;
            MarkLog.Add(m.Marks, world.Tick, $"{cm.Name}: 분말·그을음을 닦았다");
            return true;
        }));
        return Wrap(a, o, c, w, "청소", toils, $"{m.Name} 분말·그을음 닦기");
    }

    /// <summary>역화 위험이 있는 방: 우주복을 입고 문틈으로 조금씩 공기를 넣어 고인 가스를 태워 없앤다 (작은 불꽃이 인다).</summary>
    private static Job? BleedRoom(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        var door = room.Doors.Where(d => !d.Removed).Select(d => (d, other: d.RoomA == room ? d.RoomB : d.RoomA))
            .Where(x => x.other != null && x.other.Air.O2 > 15f).Select(x => x.d).FirstOrDefault();
        if (door == null) { blocked = "공기를 넣을 문이 없다"; return null; }
        var outside = Cell.Dirs4.Select(d => door.Cell + d).Where(x => w.Ship.RoomAt(x) != room && w.Ship.IsWalkable(x) && dist.Reachable(x)).Cast<Cell?>().FirstOrDefault();
        if (outside is not Cell spot) { blocked = "문 앞에 설 자리가 없다"; return null; }
        var toils = new List<Toil>();
        if (c.Suit is not { Oxygen: > 1f } && !SuitUp(c, w, dist, toils)) { blocked = "우주복 없음"; return null; }
        toils.Add(new GotoToil(spot));
        toils.Add(new WorkToil(0.4f, Skill.Mechanics, door.Cell.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            // 조금씩 넣어 태운다: 문 안쪽에 작은 불꽃 (역화는 걷힌다)
            var inner = room.Cells.Where(x => world.Ship.IsOpenFloor(x)).OrderBy(x => (x.Center - door.Cell.Center).LengthSquared()).FirstOrDefault();
            if (inner != default) world.Fire.Ignite(inner, 0.15f);
            room.Backdraft = 0f;
            room.BackdraftKnown = false;
            room.Air.O2 += 4f;
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name}에 문틈으로 조금씩 공기를 넣어 고인 가스를 태웠다 (역화를 막았다)", cm.Id);
            world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} {room.Name} 문틈으로 공기를 조금씩 넣어 역화를 막았다", room, new[] { cm });
            return true;
        }));
        return Wrap(a, o, c, w, "역화 막기", toils, $"{room.Name} 조금씩 환기 (역화 막기)");
    }

    /// <summary>산소관 누출을 막는다.</summary>
    private static Job? SealO2Line(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        var cost = new[] { (ItemKind.Sealant, 1) };
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = "실링폼 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.4f, Skill.Mechanics, room.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!UseAll(cm, cost)) return false;
            world.Board.Close(o);
            room.O2Leak = 0f;
            MarkLog.Add(room.Marks, world.Tick, $"{cm.Name}: 산소관 누출을 막았다");
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 산소관 누출을 막았다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "산소관 막기", toils, $"{room.Name} 산소관 막기");
    }
}
