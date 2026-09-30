using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>적응의 기록: 임시 배선, 뜯긴 설비, 바뀐 방, 수동 기동, 절전, 만든 연료.</summary>
public sealed class AdaptationLog
{
    public int Jumpers;
    public int Stripped;
    public int Repurposed;
    public int ManualStarts;
    public int ManualStartFails;
    public int LoadSheds;
    public int FuelMade;
    public int Refuels;
    // v6 재료와 순환
    public int PartsMade;
    public int Refined;
    public int IceMelted;
    public int Substitutes;
    public int Restored;
    public int Rebuilt;
    public int PanelsReplaced;
    public int Workshops;
    // v10.10 자원 회복: 사고 뒤 정리
    public int JumpersRemoved;
    public int CotsStowed;
    public int Recycled;
    // v10.11 대인원 생활
    public int Rationings;

    public override string ToString() =>
        $"임시 배선 {Jumpers} · 뜯은 설비 {Stripped} · 용도 바뀐 방 {Repurposed} · 수동 기동 {ManualStarts}(실패 {ManualStartFails}) · 절전 {LoadSheds} · 바이오 연료 {FuelMade} · 급유 {Refuels}";

    public string Materials =>
        $"정제 {Refined} · 부품 제작 {PartsMade} · 얼음 {IceMelted} · Mk.1 대체 {Substitutes} · 정품 복원 {Restored} · 파손 재건 {Rebuilt} · 패널 교체 {PanelsReplaced} · 임시 정비실 {Workshops}";
}

/// <summary>
/// 시뮬레이션 세계 전체. Godot을 전혀 모르는 순수 C#이라서
/// 화면 없이 수백 일을 돌려 보는 테스트(tools/Headless)가 가능하다.
/// </summary>
public sealed class World
{
    /// <summary>전력·공기·물·마모는 15틱(게임 36초)마다 한 번 계산한다.</summary>
    public const int SystemInterval = 15;

    public long Tick { get; private set; }
    public Ship Ship { get; }
    public List<CrewMember> Crew { get; } = new();
    public Pathfinder Paths { get; }

    /// <summary>v10.1 통신실: 장거리 센서와 다가오는 운석.</summary>
    public SensorSystem Sensors { get; }
    public Chronicle Log { get; } = new();
    public Rng Rng { get; }
    public int Seed { get; }

    public PowerGrid Power { get; }
    public Atmosphere Air { get; }
    public WaterSystem Water { get; } = new();
    public MachineSystem Machines { get; }
    public WorkBoard Board { get; }
    public HullSystem Hull { get; }
    public FireSystem Fire { get; }

    /// <summary>우주선 주변 환경 (잔해 밀도)과 채집.</summary>
    public SpaceEnvironment Space { get; } = new();
    public CollectionSystem Collection { get; }

    /// <summary>구조 연결부·떨어져 나간 조각 (v8).</summary>
    public StructureSystem Structure { get; }

    /// <summary>외부 드론 (v8): 검사·수리·견인·건설.</summary>
    public DroneSystem Drones { get; }

    /// <summary>v10.10 선내 로봇: 운반·정비·재배·방재. 멈추면 그 일은 사람에게 돌아간다.</summary>
    public RobotSystem Robots { get; }

    /// <summary>v11.2 엔진·추진제·항로: 회피 기동, 보통 항로 ↔ 잔해 지대.</summary>
    public PropulsionSystem Propulsion { get; }

    /// <summary>v11.2 사고 종류 (운석우·태양 폭풍·가스·병충해·식중독…)와 무작위 사고.</summary>
    public HazardSystem Hazards { get; }

    /// <summary>v11.3 승무원 성장 (배우기·재활).</summary>
    public GrowthStats Growth { get; } = new();

    /// <summary>v11.2 외부 교신 (조난 신호·보급 캡슐·탈출 캡슐 구조).</summary>
    public CommsSystem Comms { get; }

    /// <summary>v10.11 먹을 것 방침 (배급).</summary>
    public FoodPolicy Food { get; } = new();

    /// <summary>v9 배관망: 냉각 루프(고온관·분기·귀환관·방열판, 냉각수)와 급수관.</summary>
    public PipeNetwork Piping { get; }

    /// <summary>v9.2 주 컴퓨터와 자동화 (격벽·댐퍼·경보·부하·드론·제어봉).</summary>
    public AutomationSystem Automation { get; }
    public FixturesSystem Fixtures { get; }

    /// <summary>에어락을 드나든 횟수 (EVA·드론 발진). 한 번마다 공기 탱크가 조금 준다.</summary>
    public int AirlockCycles { get; set; }
    /// <summary>v8: 공기 탱크로 우주복 산소를 다시 채운 횟수.</summary>
    public int SuitRefills { get; set; }

    /// <summary>처음 출발할 때의 함선 지표 (지금과 비교하는 기준 = 100).</summary>
    public ShipProfile? InitialProfile { get; set; }

    /// <summary>최근 운석 충돌 (화면 연출용).</summary>
    public List<Impact> Impacts { get; } = new();

    public List<Alert> Alerts { get; } = new();

    /// <summary>우주선이 원래 설계와 달라진 횟수들 (적응의 기록).</summary>
    public AdaptationLog Adapt { get; } = new();

    /// <summary>v10.10 자원 장부: 공기·실링폼·냉각수·금속판·물·식량이 하루마다, 사고마다 얼마나 들어오고 나갔나.</summary>
    public ResourceLedger Ledger { get; } = new();

    /// <summary>v11.0 예방: 사고 전조를 몇 번 냈고, 누가 알아챘고, 몇 번 막고 몇 번 놓쳤나.</summary>
    public PreventionStats Precursors { get; } = new();

    /// <summary>v12.0 당직 일지 · 진단 · 감지기 교정.</summary>
    public WatchLog Watch { get; }

    /// <summary>배 전체 유틸리티 망 (전력 간선·급수관·환기 덕트).</summary>
    public UtilityNet Net { get; }
    /// <summary>v12.2 인과 사슬: 사고가 무엇에서 시작해 무엇으로 번졌고 누가 되돌렸나.</summary>
    public CauseLog Causes { get; }

    /// <summary>v12.1 정비 절차 통계.</summary>
    public ProcedureStats Procs { get; } = new();

    /// <summary>v12.1 떼어 온 중고 부품 (검사하지 않고 쓰면 재조립 불량이 잦다).</summary>
    public List<ItemKind> UsedParts { get; } = new();

    /// <summary>v12.2 설비 열·압력·폭발·잔해·역화·일산화탄소·짙은 산소.</summary>
    public VolatileSystem Volatile { get; }

    /// <summary>시험용: 전조를 아무도 못 보는 배 (감지기·당직·순찰이 전조를 보지 않는다).</summary>
    public bool PreventionBlind { get; set; }

    /// <summary>방마다 마지막으로 순찰한 틱 (로봇이든 사람이든).</summary>
    public Dictionary<int, long> RoomsInspected { get; } = new();

    /// <summary>항해를 시작한 틱.</summary>
    public long StartTickOf => History.FoundedTick;

    /// <summary>우주선의 역사 (v7): 연대기, 사고 에피소드, 교훈, 개조.</summary>
    public ShipHistory History { get; } = new();

    /// <summary>관찰자가 한 일 (사고를 일으킨 것들). 저장하면 시드와 이것만 남기고, 불러오면 그대로 다시 돌린다 (결정론).</summary>
    public List<PlayerCommand> Commands { get; } = new();

    /// <summary>
    /// v10.3: 앞으로 다시 일어날 관찰자 기록 (되감기·불러오기 뒤 아직 오지 않은 틱의 사고). 그 틱이 되면 저절로 다시 한다.
    /// 관찰자가 그 사이 새 사고를 일으키면 역사가 갈라져 모두 버린다.
    /// </summary>
    public List<PlayerCommand> Scheduled { get; } = new();

    /// <summary>기록을 다시 하는 중 (새 사고가 아니다).</summary>
    internal bool ApplyingRecord { get; set; }

    /// <summary>경보 순번 (화면이 한 프레임에 여러 경보가 울려도 빠짐없이 본다).</summary>
    public long AlertSerial { get; private set; }

    /// <summary>v10.5 쌓인 연구 (작업대·솜씨 좋은 사람이 날마다 쌓는다 — 문턱을 넘으면 설비의 다음 단계가 풀린다).</summary>
    public float Research { get; set; }

    /// <summary>승무원이 죽을 수 있는지. 꺼져 있으면 체력이 바닥나도 쓰러질 뿐이다.</summary>
    public bool CrewCanDie { get; set; }

    /// <summary>디버그용: 작업이 중간에 끝나면 이유를 흘려보낸다 (헤드리스 도구에서만 씀).</summary>
    public static Action<string>? Trace;

    private readonly Dictionary<string, bool> _flags = new();

    public World(Ship ship, int seed, long startTick)
    {
        Ship = ship;
        Seed = seed;
        Rng = new Rng(seed);
        Paths = new Pathfinder(ship);
        Sensors = new SensorSystem(this);
        Tick = startTick;
        Power = new PowerGrid(this);
        Air = new Atmosphere(this);
        Machines = new MachineSystem(this);
        Board = new WorkBoard(this);
        Hull = new HullSystem(this);
        Fire = new FireSystem(this);
        Collection = new CollectionSystem(this);
        Structure = new StructureSystem(this);
        Drones = new DroneSystem(this);
        Robots = new RobotSystem(this);
        Propulsion = new PropulsionSystem(this, 1f);
        Hazards = new HazardSystem(this, seed);
        Comms = new CommsSystem(this);
        Watch = new WatchLog(this); // v12.0
        Volatile = new VolatileSystem(this); // v12.2
        Net = new UtilityNet(this);
        Causes = new CauseLog(this);
        Piping = new PipeNetwork(this);
        Automation = new AutomationSystem(this);
        Fixtures = new FixturesSystem(this);
    }

    /// <summary>에어락 한 번 순환: 대부분은 다시 빨아들이지만 조금은 우주로 나간다.</summary>
    public const float AirlockCycleCost = 25f;

    /// <summary>드론은 에어락 한쪽의 작은 드론 포트로 드나든다 (사람 순환의 삼분의 일쯤).</summary>
    public const float DronePortCost = 8f;

    public void CycleAirlock(float cost = AirlockCycleCost)
    {
        AirlockCycles++;
        Air.Reserve = MathF.Max(0f, Air.Reserve - cost);
    }

    /// <summary>있는 곳 갱신: 방, 선체 밖인지. 외부 해치로 드나들면 에어락을 한 번 돌린 셈.</summary>
    private void UpdatePlace(CrewMember c)
    {
        c.Room = Ship.RoomAt(c.Cell);
        var cell = c.Cell;
        var k = Ship.Grid.Kind(cell);
        bool hatch = k == TileKind.Door && Ship.DoorAt(cell)?.IsExternal == true;
        bool outside = c.Room == null && (k == TileKind.Void || hatch);
        if (outside != c.Outside && hatch && !c.Dead) CycleAirlock();
        c.Outside = outside;
        if (outside) c.EvaHours += 1f / SimTime.TicksPerHour;
    }

    public int Day => SimTime.Day(Tick);
    public string Clock => SimTime.Clock(Tick);

    /// <summary>한 틱 진행. 순서: 우주선 시스템 → 작업 목록 → 승무원(욕구 → 판단 → 작업) → 문.</summary>
    public void Step()
    {
        Tick++;
        foreach (var c in Crew) c.PreviousPosition = c.Position;

        if (Tick % SystemInterval == 0)
        {
            const float dt = SystemInterval / (float)SimTime.TicksPerHour;
            Net.Update(dt); // 배 전체 망: 어느 방까지 전기·물·공기가 닿나
            Piping.Update(dt);
            Automation.Update(dt);
            Power.Update(dt);
            Hull.Update(dt);
            Fixtures.Update(dt);
            Air.Update(dt);
            Fire.Update(dt);
            Water.Update(this, dt);
            Machines.Update(dt);
            Prevention.Update(this, dt);
            Watch.Update(dt); // v12.0 교대·감지기
            Volatile.Update(dt); // v12.2 열·폭발·잔해·역화·일산화탄소·짙은 산소
            Volatile.Resume();
            Procedures.Update(this); // v12.1 재조립 불량이 돌아온다
            Causes.Update(); // v12.2 인과 사슬: 번진 상태를 원인에 잇고, 풀린 상태에 복구를 붙인다
            Collection.Update(dt);
            Structure.Update(dt);
            Drones.SystemUpdate(dt);
            Robots.SystemUpdate(dt);
            Propulsion.SystemUpdate(dt);
            Hazards.SystemUpdate(dt);
            Food.Update(this, dt);
            Comms.SystemUpdate(dt);
            Ledger.Sample(this, dt);
            CheckShip();
            foreach (var c in Crew) Memory.Update(this, c);
        }
        if (Tick % SimTime.Minutes(5) == 0)
        {
            History.Update(this);
            float before = Research;
            Research += Tech.ResearchPerHour(this) * (SimTime.Minutes(5) / (float)SimTime.TicksPerHour);
            Tech.NoteUnlocks(this, before);
        }
        Hazards.Step();
        Sensors.Step();
        Propulsion.Step();
        Board.Update();
        Drones.Step();
        Robots.Step();

        foreach (var c in Crew)
        {
            // 떨어져 나간 조각에 탄 사람: 조각과 함께 움직인다 (우주복 산소로 버틴다 — 되찾아 오기를 기다린다)
            if (c.Aboard is Fragment fr)
            {
                c.PreviousPosition = c.Position;
                c.Position = StructureSystem.OnFragment(fr, c.AboardAt);
                c.Room = null;
                c.Outside = true;
                if (c.Dead) continue;
                NeedsSystem.Update(c, this);
                CheckVitals(c);
                if (c.CanAct) c.Pose = Pose.Standing;
                continue;
            }
            if (c.Dead) continue;
            NeedsSystem.Update(c, this);
            CheckVitals(c);
            if (!c.CanAct)
            {
                UpdatePlace(c);
                continue;
            }
            SenseHazard(c);

            // v8: 선체 밖에서 우주복 산소가 바닥나 가면 하던 일을 두고 돌아온다 (긴 일의 진척은 작업 목록에 남는다)
            if (c.Outside && c.Suit is { Oxygen: < 0.6f } && c.Job?.Activity is not EvacuateActivity && c.CarryingPerson == null)
            {
                if (c.Job != null && Tick - c.SuitWarnedAt > SimTime.Minutes(20))
                {
                    c.SuitWarnedAt = Tick;
                    Log.Add(Tick, LogKind.Warning, $"우주복 산소 {c.Suit.Oxygen * 60:0}분 — 하던 일을 두고 에어락으로 돌아간다", c.Id);
                }
                c.EndJob(this, ToilStatus.Interrupted);
                c.NextThinkTick = Tick;
            }

            if (c.Job == null || Tick >= c.NextThinkTick)
            {
                Brain.Think(c, this);
                c.NextThinkTick = Tick + Brain.ThinkInterval + Rng.Range(0, SimTime.Minutes(4));
            }

            if (c.Job != null)
            {
                var status = c.Job.Tick(c, this);
                if (status != ToilStatus.Running)
                {
                    c.EndJob(this, status);
                    c.NextThinkTick = Tick + 1;
                    // 실패했으면 1분 숨 고른다 (같은 실패를 틱마다 반복하지 않게). 경보가 울리면 바로 깬다.
                    if (status == ToilStatus.Failed)
                    {
                        c.StartJob(IdleJob.Create(SimTime.Minutes(1)), this, null);
                        c.NextThinkTick = Tick + SimTime.Minutes(1);
                    }
                }
            }

            UpdatePlace(c);
            Ship.DoorAt(c.Cell)?.Request();
        }

        // 업힌 사람은 업은 사람 등에 붙어 간다
        foreach (var c in Crew)
        {
            if (c.CarriedBy is not CrewMember carrier) continue;
            c.Position = carrier.Position - carrier.Facing * 0.3f;
            UpdatePlace(c);
        }

        foreach (var d in Ship.Doors) d.Update();

        // v10.3: 되감은 역사 — 그 틱에 있었던 관찰자의 사고를 다시 (화면에서 한 것과 같은 자리: 틱이 끝난 뒤)
        while (Scheduled.Count > 0 && Scheduled[0].Tick <= Tick)
        {
            var cmd = Scheduled[0];
            Scheduled.RemoveAt(0);
            Player.Apply(this, cmd);
        }
    }

    // ─────────────────────────────── 쓰러짐과 죽음 ───────────────────────────────

    private void CheckVitals(CrewMember c)
    {
        var v = c.Vitals;
        if (CrewCanDie && v.Health <= 0.001f)
        {
            Die(c);
            return;
        }
        // 체력이 바닥나거나 혈중 산소가 25% 밑이면 (의식을 잃고) 쓰러진다
        if (!c.Down && (v.Health <= 0.12f || v.Oxygen < 0.25f)) Collapse(c);
        else if (c.Down && c.CarriedBy == null && v.Health >= 0.35f && v.Oxygen > 0.6f) WakeUp(c);
    }

    private void Collapse(CrewMember c)
    {
        c.EndJob(this, ToilStatus.Interrupted);
        c.Down = true;
        c.LaidSafe = false;
        c.Pose = Pose.Down;
        c.Stats.TimesDown++;
        c.Path = null;
        c.Destination = null;
        RaiseAlert($"{Ko.IGa(c.Name)} 쓰러졌다 — {c.Room?.Name ?? (c.Outside ? "선체 밖" : "?")}", c.Room, AlertLevel.Critical, shipWide: true);
        Board.RequestScan();

        // 역사: 쓰러진 곳은 무서운 곳이 되고, 긴장이 남는다
        string why = c.Vitals.Oxygen < 0.3f ? "숨이 막혀" : c.Vitals.InjuryCause ?? "기력이 다해";
        History.Collapses++;
        if (History.Current != null) History.Current.Collapses++;
        if (c.Room != null)
        {
            c.Room.Collapses++;
            MarkLog.Add(c.Room.Marks, Tick, $"{Ko.IGa(c.Name)} 쓰러졌다");
        }
        MarkLog.Add(c.Memory.Marks, Tick, $"쓰러졌다 ({c.Room?.Name ?? (c.Outside ? "선체 밖" : "?")} · {why})");
        Memory.Frighten(this, c, c.Room, 0.45f, "거기서 쓰러졌다");
        Memory.Shake(this, c, 0.12f, "쓰러졌던 일");
        History.Add(this, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} {c.Room?.Name ?? (c.Outside ? "선체 밖" : "?")}에서 쓰러졌다 ({why})", c.Room, new[] { c });
    }

    private void WakeUp(CrewMember c)
    {
        c.Down = false;
        c.LaidSafe = false;
        c.Pose = c.CareBed != null ? Pose.Sleeping : Pose.Standing;
        if (c.CareBed is Furniture bed && bed.ReservedBy == c) bed.ReservedBy = null;
        c.CareBed = null;
        c.NextThinkTick = Tick + 1;
        Log.Add(Tick, LogKind.Life, "정신을 차렸다", c.Id);
    }

    private void Die(CrewMember c)
    {
        c.EndJob(this, ToilStatus.Interrupted);
        if (c.CarriedBy is CrewMember carrier) { carrier.CarryingPerson = null; c.CarriedBy = null; }
        if (c.CareBed is Furniture bed && bed.ReservedBy == c) bed.ReservedBy = null;
        c.CareBed = null;
        c.Dead = true;
        c.Down = true;
        c.Pose = Pose.Down;
        c.Path = null;
        c.Destination = null;
        RaiseAlert($"{Ko.IGa(c.Name)} 죽었다 — {c.Room?.Name ?? "?"} ({c.Vitals.InjuryCause ?? "사고"})", c.Room, AlertLevel.Critical, shipWide: true);
        // 남은 사람들: 가까웠던 사람일수록 크게 흔들린다. 그 방은 모두에게 무서운 곳이 된다
        foreach (var o in Crew)
        {
            if (o == c || o.Dead) continue;
            float close = MathF.Max(0f, o.AffinityTo(c));
            o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + 0.25f + 0.4f * close);
            Memory.Shake(this, o, 0.06f + 0.12f * close + (Memory.AreComrades(o, c) ? 0.05f : 0f), $"{c.Name}의 죽음");
            Memory.Frighten(this, o, c.Room, o.Room == c.Room ? 0.4f : 0.15f, $"{Ko.IGa(c.Name)} 거기서 죽었다");
        }
        History.Deaths++;
        if (History.Current != null) History.Current.Deaths++;
        if (c.Room != null)
        {
            c.Room.Deaths++;
            MarkLog.Add(c.Room.Marks, Tick, $"{Ko.IGa(c.Name)} 죽었다");
        }
        History.Add(this, HistoryKind.Death, $"{Ko.IGa(c.Name)} {c.Room?.Name ?? "?"}에서 죽었다 ({c.Vitals.InjuryCause ?? "사고"})", c.Room, new[] { c });
        Board.RequestScan();
    }

    // ─────────────────────────────── 경보와 인지 ───────────────────────────────

    /// <summary>
    /// 경보. 같은 방·옆방 사람은 소리를 듣고, 우주선 전체 방송이면 깨어 있는 모두가(위급하면 자는 사람도) 듣는다.
    /// 들은 사람은 다음 생각 주기를 기다리지 않고 즉시 다시 판단한다.
    /// </summary>
    public void RaiseAlert(string text, Room? room, AlertLevel level, bool shipWide)
    {
        Alerts.Add(new Alert(Tick, text, room, level, shipWide) { Serial = ++AlertSerial });
        if (Alerts.Count > 30) Alerts.RemoveAt(0);
        Log.Add(Tick, LogKind.Warning, text);
        if (level == AlertLevel.Critical) History.NoteCritical(this, text);

        foreach (var c in Crew)
        {
            bool nearby = room != null && c.Room != null &&
                          (c.Room == room || room.Doors.Any(d => d.RoomA == c.Room || d.RoomB == c.Room));
            bool broadcast = shipWide && (c.IsAwake || level == AlertLevel.Critical);
            if (!nearby && !broadcast) continue;
            // 곯아떨어진 사람(탈진)은 방송으로는 잘 깨지 않는다 — 누가 가서 흔들어 깨워야 한다
            if (!c.IsAwake && !nearby && c.Needs.Fatigue > 0.85f && Rng.Chance(0.6f)) { c.DeepAsleep = true; continue; }
            c.Interrupt(this);
            // 위급 경보에 가슴이 철렁한다 (침착한 사람은 덜)
            if (level == AlertLevel.Critical && !c.Dead) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f * (1f - c.Traits.Calm));
        }
    }

    /// <summary>자기가 있는 방이 위험해지면 (숨이 막히거나 뜨거우면) 그 순간 알아챈다.</summary>
    private void SenseHazard(CrewMember c)
    {
        if (c.Room == null) return;
        float danger = EvacuateActivity.DangerHere(c, this);
        // 자다가도: 연기 냄새·숨 막힘·추위·열기에 깬다 (일산화탄소는 모른다 — 그래서 위험하다)
        if (c.Pose == Pose.Sleeping && (c.Room.Air.Smoke > 0.15f || c.Room.Air.O2 < 16.5f || c.Room.Air.Temperature < 8f || c.Room.Air.Temperature > 40f || c.Room.Air.Toxin > 0.1f))
            c.Jolt(this);
        if (!c.InHazard && danger > 0.25f)
        {
            c.InHazard = true;
            c.Interrupt(this);
        }
        else if (c.InHazard && danger < 0.12f)
        {
            c.InHazard = false;
        }
    }

    /// <summary>다른 승무원이 이미 서 있거나 가려는 칸인지.</summary>
    public bool IsSpotTaken(Cell cell, CrewMember me)
    {
        foreach (var o in Crew)
        {
            if (o == me || o.CarriedBy != null) continue;
            if (o.Destination == cell) return true;
            if (!o.IsMoving && o.Cell == cell) return true;
        }
        return false;
    }

    /// <summary>우주선 전체 상태 경고 (조건이 처음 성립할 때 한 번만).</summary>
    private void CheckShip()
    {
        int food = Ship.CountStored(ItemKind.Meal) + Ship.CountStored(ItemKind.Produce);
        Flag("food", food < Crew.Count * 3, food >= Crew.Count * 5, "식량(식사+채소)이 얼마 남지 않았다");
        Flag("water", Water.Level < 60f, Water.Level > 90f, $"정수 탱크가 {Water.Level:0}L까지 줄었다");
        Flag("battery", Power.BatteryPercent < 0.2f && Power.BatteryFlow < 0f, Power.BatteryPercent > 0.3f, "배터리 잔량 20% 미만 — 전력이 모자란다");
        Flag("shed", Power.ShedCount > 0, Power.ShedCount == 0, $"전력 부족으로 설비 {Power.ShedCount}개를 차단했다");
        Flag("co2", Ship.Rooms.Any(r => r.Air.CO2 > 1.2f), Ship.Rooms.All(r => r.Air.CO2 < 0.8f), "공기가 탁해지고 있다 (CO2 상승)");
        Flag("o2", Ship.Rooms.Any(r => r.Air.O2 < 17f && !r.Leaking && r.Air.Pressure > 80f),
            Ship.Rooms.All(r => r.Air.O2 > 18.5f || r.Leaking || r.Air.Pressure <= 80f), "산소 농도가 떨어지고 있다");
        Flag("reserve", Air.Reserve < Air.ReserveCapacity * 0.25f, false, $"공기 탱크 잔량 {Air.Reserve / Air.ReserveCapacity * 100:0}% — 재가압 여유가 거의 없다");
        Flag("aux", Power.AuxRunning && Power.AuxFuel < 4f, !Power.AuxRunning, "보조 발전기 연료가 4시간도 안 남았다");
    }

    /// <summary>경고는 조건이 처음 성립할 때 한 번만, 충분히 풀린 뒤에야 다시 울릴 수 있다 (깜빡임 방지).</summary>
    private void Flag(string key, bool on, bool off, string text)
    {
        bool was = _flags.TryGetValue(key, out var v) && v;
        if (!was && on)
        {
            Log.Add(Tick, LogKind.Warning, text);
            _flags[key] = true;
        }
        else if (was && off) _flags[key] = false;
    }

    // ─────────────────────────────── 기본 세계 ───────────────────────────────

    private sealed record CrewSeed(
        string Name, CrewRole Role, float Bedtime,
        float Diligence, float Sociability, float Bravery, float Appetite,
        RoomType[] Stations, float[] Skills, float Calm = 0.5f);

    //                                                           기관  전기  정비  의료  재배  조리  항법
    private static readonly CrewSeed[] DefaultCrew =
    {
        new("한서진", CrewRole.Engineer, 23f, 0.8f, 0.45f, 0.7f, 1.0f,
            new[] { RoomType.Reactor, RoomType.Cooling, RoomType.Engine }, new[] { .85f, .5f, .6f, .1f, .1f, .3f, .3f }, 0.65f),
        new("박도윤", CrewRole.Medic, 22f, 0.6f, 0.8f, 0.45f, 0.9f,
            new[] { RoomType.Medbay }, new[] { .1f, .2f, .25f, .9f, .35f, .45f, .1f }, 0.6f),
        new("강유리", CrewRole.Pilot, 2f, 0.45f, 0.6f, 0.6f, 1.0f,
            new[] { RoomType.Bridge }, new[] { .4f, .35f, .3f, .2f, .2f, .25f, .9f }, 0.55f),
        new("오태민", CrewRole.Technician, 14f, 0.7f, 0.25f, 0.55f, 1.2f,
            new[] { RoomType.Workshop, RoomType.LifeSupport, RoomType.Storage }, new[] { .45f, .4f, .85f, .15f, .2f, .2f, .1f }, 0.4f),
        new("윤채원", CrewRole.Botanist, 21f, 0.65f, 0.7f, 0.35f, 1.0f,
            new[] { RoomType.Hydroponics, RoomType.Galley }, new[] { .1f, .15f, .3f, .3f, .9f, .85f, .1f }, 0.3f),
        new("이도현", CrewRole.Electrician, 1f, 0.5f, 0.5f, 0.8f, 1.05f,
            new[] { RoomType.Power, RoomType.Workshop }, new[] { .5f, .85f, .5f, .1f, .1f, .35f, .3f }, 0.45f),
    };

    /// <summary>기본 승무원 수 (침대 6개).</summary>
    public static int DefaultCrewSize => DefaultCrew.Length;

    /// <summary>v10.1: 늘릴 수 있는 최대 승무원 수 (간이침대를 놓을 자리와 공기·식량이 버티는 선).</summary>
    public const int MaxCrew = 40;

    /// <summary>v10.4: 이 배의 템플릿 (저장·재생이 같은 배를 다시 만들 수 있게).</summary>
    public string ShipKey { get; private set; } = "Mirinae";

    /// <summary>처음 태운 승무원 수 (저장·재생이 같은 배를 다시 만들 수 있게).</summary>
    public int StartCrew { get; private set; }
    /// <summary>v10.7: 항해를 시작할 때 기본값과 달랐던 밸런스 수치 (저장 파일 머리에 `tune`으로 남는다).</summary>
    public List<(string key, float value)> StartTuning { get; private set; } = new();

    /// <param name="crew">0이면 배의 설계 인원.</param>
    /// <param name="shipKey">배 템플릿 (null이면 인원에 맞는 가장 작은 배, 인원도 없으면 미리내호).</param>
    public static World CreateDefault(int seed = 20260929, int crew = 0, string? shipKey = null)
    {
        var template = ShipCatalog.Find(shipKey) ?? (crew > 0 ? ShipCatalog.ForCrew(crew) : ShipCatalog.Default);
        var shipObj = ShipBuilder.FromAscii(template.Name, template.Ascii);
        var world = new World(shipObj, seed, startTick: SimTime.Hours(7));
        world.ShipKey = template.Key;
        var rng = world.Rng;
        int crewSize = crew <= 0 ? template.Crew : Math.Clamp(crew, 1, MaxCrew);
        world.StartCrew = crewSize;
        world.StartTuning = Tuning.NonDefault().ToList();
        var ship = shipObj;

        // v10.4: 큰 배는 공기 탱크·물통도 크고, 처음 싣는 물자도 설계 인원만큼
        float scale = MathF.Max(1f, template.Crew / 6f);
        world.Air.ReserveCapacity *= scale;
        world.Air.Reserve = world.Air.ReserveCapacity;
        world.Water.Capacity *= scale;
        world.Water.Level *= scale;
        world.Propulsion.SetScale(scale);
        StockShip(ship, rng, scale);

        var beds = ship.FurnitureOf(FurnitureType.Bed).OrderBy(b => b.MinX).ToList();
        float hour = SimTime.HourOfDay(world.Tick);

        // v12.0 이름은 시드마다 다르게 (배의 난수와 따로 굴린다)
        var names = NameGen.ForShip(seed, crewSize);
        for (int i = 0; i < crewSize; i++)
        {
            var s = i < DefaultCrew.Length ? DefaultCrew[i] : ExtraSeed(i, rng);
            var c = new CrewMember
            {
                Id = i,
                Name = names[i],
                Role = s.Role,
                Traits = new Personality
                {
                    Diligence = s.Diligence, Sociability = s.Sociability, Bravery = s.Bravery, Appetite = s.Appetite, Calm = s.Calm,
                },
                Memory = new CrewMemory(ship.Rooms.Count),
                Schedule = Schedule.FromBedtime(s.Bedtime),
                Stations = s.Stations,
                SkillLevels = (float[])s.Skills.Clone(),
            };
            if (i < beds.Count)
            {
                c.Bed = beds[i];
                beds[i].Owner = c;
            }
            else if (StartingCot(ship, world) is Furniture cot)
            {
                // 침대가 모자라 처음부터 간이침대에서 잔다 (덜 쉬어진다 — 진화가 고쳐 짤 거리)
                c.Bed = cot;
                cot.Owner = c;
            }

            bool asleep = SimTime.InWindow(hour, c.Schedule.SleepStart, c.Schedule.SleepLength);
            c.Needs.Food = rng.Range(0.45f, 0.8f);
            c.Needs.Rest = asleep ? rng.Range(0.4f, 0.55f) : rng.Range(0.75f, 0.95f);
            c.Needs.Stress = rng.Range(0.05f, 0.25f);
            c.Needs.Social = rng.Range(0.5f, 0.9f);

            Cell start;
            if (asleep && c.Bed != null) start = c.Bed.UseSpots[0];
            else
            {
                var room = ship.RoomsOf(s.Stations[0]).First();
                start = rng.Pick(room.Cells.Where(ship.IsOpenFloor).ToList());
            }
            c.Position = start.Center;
            c.PreviousPosition = c.Position;
            c.Room = ship.RoomAt(start);
            world.Crew.Add(c);
        }

        if (crewSize > beds.Count) world.Paths.Invalidate();

        // 처음 만난 사이가 아니다: 조금씩 다른 호감
        foreach (var a in world.Crew)
        foreach (var b in world.Crew)
            if (a != b) a.Affinity[b.Id] = rng.Range(-0.1f, 0.3f);

        world.Power.Update(0.01f);
        world.Log.Add(world.Tick, LogKind.Ship, $"{ship.Name} 항해 기록 시작 · 승무원 {world.Crew.Count}명");
        world.History.Founded(world);
        world.InitialProfile = ShipProfile.Measure(world);
        return world;
    }

    /// <summary>v10.1: 기본 여섯 명 뒤의 승무원 — 역할을 돌아가며 잇고, 성격·솜씨·잠드는 시각은 조금씩 다르다.</summary>
    /// <summary>v10.11: 큰 배의 조리 전담 — 재배 담당 후배를 한 사람 걸러 조리사로 (12명 배에 하나, 20명 하나, 30명 둘).</summary>
    private static readonly CrewSeed CookSeed = new("", CrewRole.Cook, 22f, 0.7f, 0.65f, 0.4f, 1.1f,
        new[] { RoomType.Galley, RoomType.Mess }, new[] { .1f, .15f, .3f, .3f, .35f, .9f, .1f }, 0.45f);

    private static CrewSeed ExtraSeed(int i, Rng rng)
    {
        var t = DefaultCrew[i % DefaultCrew.Length];
        if (t.Role == CrewRole.Botanist && (i / DefaultCrew.Length) % 2 == 1) t = CookSeed;
        var skills = t.Skills.Select(v => Math.Clamp(v * rng.Range(0.7f, 1.0f) + rng.Range(-0.05f, 0.1f), 0.05f, 0.9f)).ToArray();
        float bed = (t.Bedtime + rng.Range(-3f, 3f) + 24f) % 24f;
        return new CrewSeed("", t.Role, bed,
            Math.Clamp(t.Diligence + rng.Range(-0.2f, 0.2f), 0.2f, 0.95f), rng.Range(0.2f, 0.85f),
            Math.Clamp(t.Bravery + rng.Range(-0.25f, 0.2f), 0.15f, 0.9f), rng.Range(0.85f, 1.2f),
            t.Stations, skills, rng.Range(0.3f, 0.7f));
    }

    /// <summary>
    /// v11.2 교신: 탈출 캡슐에서 건진 생존자를 승무원으로 태운다 (다쳐서 온다). 번호는 목록 끝, 역할·성격·솜씨는 기본 여섯 중 하나를 닮게.
    /// 침대가 없으면 간이침대를 편다.
    /// </summary>
    public CrewMember AddSurvivor(Cell at)
    {
        int id = Crew.Count;
        var t = DefaultCrew[Rng.Range(0, DefaultCrew.Length)];
        int k = Crew.Count(c => c.Rescued);
        string name = NameGen.Newcomer(Seed, k, Crew.Select(c => c.Name)); // v12.0
        var skills = t.Skills.Select(v => Math.Clamp(v * Rng.Range(0.6f, 1.0f) + Rng.Range(-0.05f, 0.1f), 0.05f, 0.9f)).ToArray();
        var c = new CrewMember
        {
            Id = id,
            Name = name,
            Role = t.Role,
            Traits = new Personality
            {
                Diligence = Math.Clamp(t.Diligence + Rng.Range(-0.2f, 0.2f), 0.2f, 0.95f), Sociability = Rng.Range(0.2f, 0.85f),
                Bravery = Math.Clamp(t.Bravery + Rng.Range(-0.25f, 0.2f), 0.15f, 0.9f), Appetite = Rng.Range(0.85f, 1.2f), Calm = Rng.Range(0.2f, 0.6f),
            },
            Memory = new CrewMemory(Ship.Rooms.Count),
            Schedule = Schedule.FromBedtime((t.Bedtime + Rng.Range(-3f, 3f) + 24f) % 24f),
            Stations = t.Stations,
            SkillLevels = skills,
            Rescued = true,
        };
        c.Needs.Food = Rng.Range(0.15f, 0.35f);
        c.Needs.Rest = Rng.Range(0.2f, 0.4f);
        c.Needs.Stress = Rng.Range(0.4f, 0.65f);
        c.Needs.Social = Rng.Range(0.3f, 0.6f);
        c.Vitals.Health = Rng.Range(0.5f, 0.8f);
        NeedsSystem.AddInjury(c.Vitals, Rng.Range(0.2f, 0.45f), "탈출 캡슐");
        c.Memory.Trauma = Rng.Range(0.1f, 0.25f);
        c.Position = at.Center;
        c.PreviousPosition = c.Position;
        c.Room = Ship.RoomAt(at);
        foreach (var o in Crew)
        {
            c.Affinity[o.Id] = Rng.Range(-0.05f, 0.15f);
            o.Affinity[c.Id] = Rng.Range(0f, 0.25f); // 건져 준 사람들은 조금 정이 간다
        }
        var bed = Ship.FurnitureOf(FurnitureType.Bed).FirstOrDefault(b => b.Owner == null && !b.Room.Abandoned && !b.Room.OffLimits);
        if (bed == null && StartingCot(Ship, this) is Furniture cot) bed = cot;
        if (bed != null) { c.Bed = bed; bed.Owner = c; Paths.Invalidate(); }
        Crew.Add(c);
        return c;
    }

    /// <summary>침대가 모자랄 때 처음부터 놓아 둔 간이침대: 침실 → 휴게실 → 위험하지 않은 방 순서로.</summary>
    private static Furniture? StartingCot(Ship ship, World w)
    {
        static int Order(RoomType t) => t switch
        {
            RoomType.Quarters => 0, RoomType.Lounge => 1, RoomType.Medbay => 2, RoomType.Galley => 3,
            RoomType.Storage => 4, RoomType.Hydroponics => 5, RoomType.Workshop => 6,
            _ => 99,
        };
        foreach (var room in ship.Rooms.Where(r => Order(r.Type) < 99).OrderBy(r => Order(r.Type)).ThenBy(r => r.Id))
        {
            var cells = Adaptation.CotCells(w, room);
            if (cells.Count == 0) continue;
            return ship.AddFurniture(FurnitureType.Cot, cells[0]);
        }
        return null;
    }

    /// <summary>처음 실린 물자와 설비 상태.</summary>
    private static void StockShip(Ship ship, Rng rng, float scale = 1f)
    {
        foreach (var m in ship.Machines)
        {
            m.Wear = rng.Range(0f, 0.45f);
            m.Condition = rng.Range(0.85f, 1f);
        }
        float[] growth = { 0.1f, 0.35f, 0.6f, 0.85f };
        int g = 0;
        foreach (var bed in ship.FurnitureOf(FurnitureType.GrowBed))
        {
            bed.Machine!.Crop!.Growth = growth[g++ % growth.Length];
            bed.Machine.Crop.Care = 0.8f;
        }
        foreach (var stove in ship.FurnitureOf(FurnitureType.Stove)) stove.Machine!.Active = false;

        foreach (var fridge in ship.FurnitureOf(FurnitureType.Fridge))
        {
            fridge.Storage!.Add(ItemKind.Produce, 36);
            fridge.Storage.Add(ItemKind.Meal, 18);
        }
        foreach (var d in ship.FurnitureOf(FurnitureType.MealDispenser)) d.Storage!.Add(ItemKind.Meal, 12);

        Inventory? Shelf(RoomType room, int index) =>
            ship.RoomsOf(room).SelectMany(r => r.Furniture).Where(f => f.Type == FurnitureType.Shelf)
                .OrderBy(f => f.MinY).ThenBy(f => f.MinX).ElementAtOrDefault(index)?.Storage;

        // 처음부터 수리 재료를 넉넉히 싣고 출발한다 (등급별: 기본 수리재 → 일반 부품 → 고급 부품)
        var ws = Shelf(RoomType.Workshop, 0);
        ws?.Add(ItemKind.Lubricant, 20); ws?.Add(ItemKind.Filter, 14); ws?.Add(ItemKind.Cable, 8); ws?.Add(ItemKind.Fuse, 6);
        ws?.Add(ItemKind.Electronics, 4);
        var s0 = Shelf(RoomType.Storage, 0);
        s0?.Add(ItemKind.Ration, 40); s0?.Add(ItemKind.Plate, 12); s0?.Add(ItemKind.Structure, 6);
        var s1 = Shelf(RoomType.Storage, 1);
        s1?.Add(ItemKind.Cable, 8); s1?.Add(ItemKind.Fuse, 6); s1?.Add(ItemKind.Sealant, 14); s1?.Add(ItemKind.Electronics, 4);
        var s2 = Shelf(RoomType.Storage, 2);
        s2?.Add(ItemKind.Pump, 3); s2?.Add(ItemKind.Bearing, 4); s2?.Add(ItemKind.Motor, 3); s2?.Add(ItemKind.PowerController, 3);
        s2?.Add(ItemKind.Sensor, 3); s2?.Add(ItemKind.ReactorControl, 2);
        s2?.Add(ItemKind.Filter, 10); s2?.Add(ItemKind.Lubricant, 10);
        var s3 = Shelf(RoomType.Storage, 3);
        s3?.Add(ItemKind.Ration, 30); s3?.Add(ItemKind.MedKit, 6);
        s3?.Add(ItemKind.MetalOre, 10); s3?.Add(ItemKind.Silicate, 4); s3?.Add(ItemKind.Carbon, 4);
        Shelf(RoomType.Medbay, 0)?.Add(ItemKind.MedKit, 8);

        // 사고 대비: 우주복은 보관함마다 가득, 소화기는 곳곳에, 실링폼은 창고와 작업장에
        foreach (var locker in ship.FurnitureOf(FurnitureType.SuitLocker)) locker.Storage!.Add(ItemKind.Suit, locker.Storage.Capacity);
        ws?.Add(ItemKind.Extinguisher, 2); ws?.Add(ItemKind.Sealant, 6);
        s1?.Add(ItemKind.Extinguisher, 2);
        Shelf(RoomType.Medbay, 0)?.Add(ItemKind.Extinguisher, 1);
        Shelf(RoomType.Galley, 0)?.Add(ItemKind.Extinguisher, 1);
        foreach (var r in new[] { RoomType.Reactor, RoomType.Power, RoomType.LifeSupport, RoomType.Quarters })
            Shelf(r, 0)?.Add(ItemKind.Extinguisher, 1);

        // 보조 발전기 예비 연료통 (다 쓰면 채소로 바이오 연료를 만들어야 한다)
        (Shelf(RoomType.Power, 0) ?? s1)?.Add(ItemKind.Fuel, 2);

        // v10.4: 큰 배 — 기본 적재량의 (배율 − 1)만큼 더 싣는다 (창고 선반부터, 모자라면 정비실 선반)
        if (scale > 1.01f)
        {
            (ItemKind kind, int n)[] basis =
            {
                (ItemKind.Lubricant, 30), (ItemKind.Filter, 24), (ItemKind.Cable, 16), (ItemKind.Fuse, 12), (ItemKind.Electronics, 8),
                (ItemKind.Ration, 70), (ItemKind.Plate, 12), (ItemKind.Structure, 6), (ItemKind.Sealant, 20), (ItemKind.Pump, 3),
                (ItemKind.Bearing, 4), (ItemKind.Motor, 3), (ItemKind.PowerController, 3), (ItemKind.Sensor, 3), (ItemKind.ReactorControl, 2),
                (ItemKind.MedKit, 14), (ItemKind.MetalOre, 10), (ItemKind.Silicate, 4), (ItemKind.Carbon, 4), (ItemKind.Extinguisher, 4), (ItemKind.Fuel, 2),
            };
            var shelves = ship.RoomsOf(RoomType.Storage).Concat(ship.RoomsOf(RoomType.Workshop)).SelectMany(r => r.Furniture)
                .Where(f => f.Type == FurnitureType.Shelf).ToList();
            foreach (var (kind, n) in basis)
            {
                int left = (int)MathF.Round(n * (scale - 1f));
                foreach (var sh in shelves)
                {
                    if (left <= 0) break;
                    if (!sh.Storage!.Accepts(kind)) continue;
                    left -= sh.Storage.Add(kind, left);
                }
            }
        }
    }
}
