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

    /// <summary>v13.0 방침 (회의가 정하고 컴퓨터·사람이 그 안에서 움직인다).</summary>
    public PolicySystem Policies { get; }

    /// <summary>v13.1 지휘: 선장 · 현장 지휘 · 조 편성 · 2인 1조 · 교대.</summary>
    public CommandSystem Command { get; }
    /// <summary>v13.2 회의: 첫 출항 회의 · 정기 회의 · 사후 검토 · 결정의 무게 · 파벌.</summary>
    public MeetingSystem Meetings { get; }
    /// <summary>v13.3 판단·인지·감정: 누가 무엇을 아나 · 공황 · 분노 · 영웅심 · 명령 반응 · 컴퓨터 신뢰.</summary>
    public MindSystem Minds { get; }
    /// <summary>v13.4 사회: 사기 · 일과표 · 베테랑 · 실수 숨기기 · 야간 당직 · 규칙 위반 · 수습.</summary>
    public SocietySystem Society { get; }
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
    /// <summary>v12.3 물·습기·전기.</summary>
    public MoistureSystem Moisture { get; }
    /// <summary>v12.4 전염병 · 이야기꾼.</summary>
    public DiseaseSystem Disease { get; }
    public AilmentSystem Ailments { get; } // v14.1 질병 30
    public BelongingSystem Belongings { get; } // v14.3 개인 물건 · 취미
    public RelationSystem Relations { get; } // v14.3~ 관계의 이유
    public MovementSystem Movement { get; } // v14.5 움직임
    public PartsSystem Parts { get; } // v14.6 부품마다의 수명과 내력
    public SoilSystem Soil { get; } // v14.7 오염 · 위생
    public FlowSystem Flow { get; } // v14.8 배관 · 배선 전달량
    public CultureSystem Culture { get; } // v14.9 배의 문화
    public DailySystem Daily { get; } // v15 일상 사건 70
    public MotionSystem Motions { get; } // v18.18 승무원이 여는 회의 · 파벌 · 재판 · 선거
    public SchemeSystem Schemes { get; } // v18.14 승무원이 스스로 꾸미는 일 100+
    public ReactSystem React { get; } // v17.8 모든 변화에 누군가 반응한다
    public FleetSystem Fleet { get; } // v16.20b 로봇 · 드론 두뇌와 성능 · 주컴퓨터 함대 지휘
    public FoodSourceSystem FoodSources { get; } // v16.22 식량원 (수경 · 조류 · 단백질 · 버섯 · 정원 · 저장 · 교역 · 원정 · 발효)
    public ScrapSystem Scrap { get; } // v16.22 고철 되살리기 (재활용실 · 파쇄실)
    public FailsafeSystem Failsafe { get; } // v16.19 차압 문 · 예비 회로 · 보조 간선 · 장갑 벽 · 비상 칸막이 · 부하 차단
    public MajorIncidentSystem Major { get; } // v16.19 계통 · 배 전체 사고 (전조 → 발생 → 번짐 → 수습 → 흔적)
    public AnnexSystem Annex { get; } // v16.10 증축 (선체 바깥에 방을 새로 붙인다)
    public Body2System Body2 { get; } // v17.1 몸의 변화 (머리카락 · 수염 · 체중 · 우주복 치수 · 이발)
    public CoopSystem Coop { get; } // v17.4 공간과 협력 · 줄 서기 · 구경꾼
    public AftermathSystem After { get; } // v17.5 사고 뒤 며칠 · 꿈 · 장소의 기억
    public InfoSystem Info { get; } // v17.3 개인 자리 · 못 끝낸 일 · 정보 차이 · 선내 메신저 · 사진 · 공동 장부
    public BrainSystem Brain2 { get; } // v16.15 승무원 두뇌 2.0 (믿음 · 목표 층 · 계획 · 감정 · 사회적 추론 · 배우기)
    public MatterSystem Matter { get; } // v16.4 재질 × 원소 · 칸 장 · 물건 물리
    public CosmicSystem Cosmic { get; } // v18.13 우주 규모 대재난 30
    public ScaleSystem Scale { get; } // v16.18 사고 · 재난 다섯 규모 (판정 · 대응 · 완급 · 연쇄 · 도감)
    public CasualtySystem Casualty { get; } // v16.24 큰 상처 뒤: 출혈 · 화상 쇼크 · 심정지 · 불붙는 순간
    public PerilSystem Perils { get; } // v16.26 위험이 사람에게 닿는 길: 열사병 · 큰 피폭 · 늦게 깨는 잠
    public RadCareSystem RadCare { get; } // 통합5 방사선 병 간호 (수액 · 골수 주사 · 수혈 · 격리)
    public WaysSystem Ways { get; } // v16.25 문제마다 여러 갈래 해법
    public CrisisCrewSystem CrisisCrew { get; } // v16.21 승무원 위기 행동 (공황 · 비상 배치표 · 비상 절차 · 여러 손 · 우선순위)
    public ExpeditionSystem Expedition { get; } // v16.12 재료 탐사 원정
    public BodySystem Body { get; } // v16.3 배 본체 (칸 3층 · 칸 상태 · 벽 층 · 문)
    public DailySceneSystem Scenes { get; } // v16.1 일상 → 행동 (장면 · 인수인계 · 쪽지)
    public CookingSystem Cooking { get; } public SmellSystem Smells { get; } // v16.8 실제 음식 · 냄새
    public PortableSystem Portable { get; } // v16.7 이동식 장비
    public BlastSystem Blast { get; } // v16.13
    public RoomUseSystem RoomUse { get; } public RoomPlanSystem RoomPlans { get; } // v16.17 쓰임이 정하는 용도 · 승무원이 정하는 방 폭발 공통 물리 · 폭발성 물건 · 의도적 폭파
    public ShipOriginSystem Origin { get; } // v16.9 배의 내력 (설계사 · 시작 상태 · 숨은 이야기 · 갈라짐 · 침대 교대)
    public OutsideSystem Outside { get; } // v15.4 외부 사건 40
    public EvaRiskSystem EvaRisk { get; } // v16.11 선외 작업의 위험 (우주복 · 생명줄 · 표류 · 무전)
    public PropSystem Props { get; } // v15.8 소품·장식 70
    public TitleSystem Titles { get; } // v15.9 칭호·업적 50
    public AmbienceSystem Ambience { get; }
    public ExteriorSystem Exterior { get; }
    public LifeSystem Life { get; }
    public EraSystem Eras { get; }
    public TechWebSystem TechWeb { get; } // v16.14 기술 그물 (선행 · 갈림길 · 조건 · 조합 · 부작용 · 실험)
    public VoyageSystem Voyage { get; }
    public GenerationSystem Generation { get; }
    public CampaignSystem Campaign { get; }
    public Storyteller Story { get; }

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
        Moisture = new MoistureSystem(this);
        Disease = new DiseaseSystem(this);
        Ailments = new AilmentSystem(this);
        Belongings = new BelongingSystem(this);
        Relations = new RelationSystem(this);
        Movement = new MovementSystem(this);
        Parts = new PartsSystem(this);
        Soil = new SoilSystem(this);
        Flow = new FlowSystem(this);
        Culture = new CultureSystem(this);
        Daily = new DailySystem(this);
        Motions = new MotionSystem(this); // v18.18
        Schemes = new SchemeSystem(this); // v18.14
        React = new ReactSystem(this); // v17.8
        Fleet = new FleetSystem(this); // v16.20b
        FoodSources = new FoodSourceSystem(this); Scrap = new ScrapSystem(this); // v16.22
        Failsafe = new FailsafeSystem(this); Major = new MajorIncidentSystem(this); // v16.19
        Annex = new AnnexSystem(this); // v16.10
        Body2 = new Body2System(this); // v17.1
        Coop = new CoopSystem(this); // v17.4
        After = new AftermathSystem(this); // v17.5
        Info = new InfoSystem(this); // v17.3
        Brain2 = new BrainSystem(this); // v16.15
        Matter = new MatterSystem(this); // v16.4
        Cosmic = new CosmicSystem(this); // v18.13
        Scale = new ScaleSystem(this); // v16.18
        Casualty = new CasualtySystem(this); // v16.24
        Perils = new PerilSystem(this); // v16.26
        RadCare = new RadCareSystem(this); // 통합5
        Ways = new WaysSystem(this); // v16.25
        CrisisCrew = new CrisisCrewSystem(this); // v16.21
        Expedition = new ExpeditionSystem(this); // v16.12
        Body = new BodySystem(this); // v16.3
        Scenes = new DailySceneSystem(this);
        Cooking = new CookingSystem(this); Smells = new SmellSystem(this); // v16.8
        Portable = new PortableSystem(this); // v16.7
        Blast = new BlastSystem(this); // v16.13
        RoomUse = new RoomUseSystem(this); RoomPlans = new RoomPlanSystem(this); // v16.17
        Origin = new ShipOriginSystem(this);
        Outside = new OutsideSystem(this);
        EvaRisk = new EvaRiskSystem(this); // v16.11
        Props = new PropSystem(this);
        Titles = new TitleSystem(this);
        Ambience = new AmbienceSystem(this);
        Exterior = new ExteriorSystem(this);
        Life = new LifeSystem(this);
        Eras = new EraSystem(this);
        TechWeb = new TechWebSystem(this); // v16.14
        Voyage = new VoyageSystem(this);
        Generation = new GenerationSystem(this);
        Campaign = new CampaignSystem(this);
        Story = new Storyteller(this, seed);
        Piping = new PipeNetwork(this);
        Policies = new PolicySystem(this);
        Command = new CommandSystem(this);
        Meetings = new MeetingSystem(this);
        Minds = new MindSystem(this);
        Society = new SocietySystem(this);
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
        if (ModulesV15.AirlockPumped(this)) cost *= 0.6f; // v15 에어락 회수 펌프
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
            long pf = Prof.Now; // v14.2 구간별 시간
            Net.Update(dt); // 배 전체 망: 어느 방까지 전기·물·공기가 닿나
            pf = Prof.Lap("sys.Net", pf);
            Flow.Update(dt); // v14.8 얼마나 오나 (전압 · 수압 · 역류 · 접촉 저항)
            pf = Prof.Lap("sys.Flow", pf);
            Piping.Update(dt);
            pf = Prof.Lap("sys.Piping", pf);
            Automation.Update(dt);
            pf = Prof.Lap("sys.Automation", pf);
            Power.Update(dt);
            pf = Prof.Lap("sys.Power", pf);
            Hull.Update(dt);
            pf = Prof.Lap("sys.Hull", pf);
            Fixtures.Update(dt);
            pf = Prof.Lap("sys.Fixtures", pf);
            Air.Update(dt);
            pf = Prof.Lap("sys.Air", pf);
            Fire.Update(dt);
            pf = Prof.Lap("sys.Fire", pf);
            Water.Update(this, dt);
            pf = Prof.Lap("sys.Water", pf);
            Machines.Update(dt);
            pf = Prof.Lap("sys.Machines", pf);
            Parts.Update(dt); // v14.6 부품이 따로 닳는다
            pf = Prof.Lap("sys.Parts", pf);
            Soil.Update(dt); // v14.7 손 · 옷 · 방으로 옮겨 다니는 오염
            pf = Prof.Lap("sys.Soil", pf);
            Culture.Update(dt); // v14.9 겪은 일이 관행이 되어 전해진다
            pf = Prof.Lap("sys.Culture", pf);
            Daily.Update(dt); // v15 사고가 아닌 날의 일상 사건
            React.Update(dt); // v17.8 더위 · 추위 · 어둠 · 바닥 · 소리 · 냄새 · 남의 몸짓 → 말 · 몸짓 · 짧은 행동
            Schemes.Update(dt); // v18.14 장난 · 몰래 하는 일 · 규칙 어기기 · 모임 · 판 · 목소리 내기 · 혼자 하는 일
            Motions.Update(dt); // v18.18 안건 · 서명 · 회의 · 파벌 · 재판 · 선거
            Info.Update(dt); // v17.3 자리 · 못 끝낸 일 · 깨진 컵 · 메신저 · 사진 · 장부
            Fleet.Update(dt); // v16.20b 함대 지휘 · 로봇 · 드론 두뇌
            Failsafe.Update(dt); Major.Update(dt); // v16.19 차압 문 · 예비 회로 · 칸막이 · 큰 사고
            Annex.Update(dt); // v16.10 증축: 제안 → 회의 → 골조 · 외판 · 가압 · 배선 · 내장 · 개통
            TechWeb.Update(dt); // v16.14 기술 그물: 조건 · 조합 · 갈림길 · 부작용 · 실험 차례
            RoomUse.Update(dt); RoomPlans.Update(dt); // v16.17 쓰임 → 용도 · 승무원 안건 → 회의 → 공사
            Cosmic.Update(dt); // v18.13 우주 대재난: 예보 · 대비 · 본 사건 · 후유증
            pf = Prof.Lap("sys.Daily", pf);
            Ways.Update(dt); // v16.25 문제마다 여러 갈래 해법
            pf = Prof.Lap("sys.Ways", pf);
            After.Update(dt); // v17.5 사고 뒤 며칠 (그을음 냄새 · 젖은 침구 · 냉장고 · 개인 조명 · 빈자리) · 꿈 · 장소의 기억
            pf = Prof.Lap("sys.After", pf);
            Casualty.Update(dt); // v16.24 큰 상처 뒤 — 누르고 · 가슴을 누르고 · 컴퓨터가 부른다
            Perils.Update(dt); // v16.26 열사병 · 큰 피폭
            RadCare.Update(dt); // 통합5 방사선 병 간호
            pf = Prof.Lap("sys.Casualty", pf);
            FoodSources.Update(dt); Scrap.Update(dt); // v16.22 식량원 · 수경이 멎으면 다른 재배실로 · 고철 되살리기
            pf = Prof.Lap("sys.FoodSources", pf);
            CrisisCrew.Update(dt); // v16.21 비상 배치 · 동료가 깨우는 공황 · 마스크 · 정전 예측 · 사고 뒤 훈련
            pf = Prof.Lap("sys.CrisisCrew", pf);
            Coop.Update(dt); // v17.4 작업장 · 짝 · 예약 · 옆 설비 · 줄 · 구경꾼 · 소문
            pf = Prof.Lap("sys.Coop", pf);
            Body2.Update(dt); // v17.1 머리카락 · 체중 · 우주복 치수 · 알아채기 · 컴퓨터 측정
            pf = Prof.Lap("sys.Body2", pf);
            Matter.Update(dt); // v16.4 재질 × 원소: 물건 · 쏟은 물 · 칸 온도 · 전기 · 바람 · 그을음 연기 · 컴퓨터 경고
            pf = Prof.Lap("sys.Matter", pf);
            Brain2.Update(dt); // v16.15 승무원 두뇌 2.0: 믿음 · 감정 · 목표 · 계획 · 배우기
            pf = Prof.Lap("sys.Brain2", pf);
            Blast.Update(dt); // v16.13 폭발성 물건 · 연쇄 · 이명 · 흔적 · 위험 배치 읽기
            pf = Prof.Lap("sys.Blast", pf);
            Expedition.Update(dt); // v16.12 재료 바닥 → 정지 · 원정 (배에 없는 사람 · 일지 · 무전 · 귀환)
            pf = Prof.Lap("sys.Expedition", pf);
            EvaRisk.Update(dt); // v16.11 우주복 누출 · 표류 · 무전 · 주 컴퓨터 원격 측정 · 선외 공포 · 드론 부위 · 배터리
            pf = Prof.Lap("sys.EvaRisk", pf);
            Origin.Update(dt); // v16.9 숨은 이야기 발견 · 전하기 · 갈라짐과 다시 이음 · 침대 인계
            pf = Prof.Lap("sys.Origin", pf);
            Portable.Update(dt); // v16.7 이동식 장비 (전원 · 회로 부하 · 효과 · 회수)
            pf = Prof.Lap("sys.Portable", pf);
            Cooking.Update(dt); Smells.Update(dt); // v16.8 냄비 · 접시 · 냄새가 공기를 타고
            pf = Prof.Lap("sys.Cooking", pf);
            Scenes.Update(dt); // v16.1 장면 진행 · 중단/재개 · 교대 인수인계 · 쪽지 읽기
            pf = Prof.Lap("sys.Scenes", pf);
            Body.Update(dt); // v16.3 배 본체: 뚜껑 · 칸 상태 · 벽 층 · 문 · 엿듣기
            pf = Prof.Lap("sys.Body", pf);
            Outside.Update(dt); // v15.4 배 바깥의 사건 (조난 신호 · 상선 · 표류 화물 · 우주 기상 · 해적)
            pf = Prof.Lap("sys.Outside", pf);
            Props.Update(dt); // v15.8 소품을 만들고 · 사고 · 겪은 일에서 걸고 · 곁의 사람을 달랜다
            pf = Prof.Lap("sys.Props", pf);
            Titles.Update(dt); // v15.9 칭호·업적 (한 시간마다)
            pf = Prof.Lap("sys.Titles", pf);
            Prevention.Update(this, dt);
            pf = Prof.Lap("sys.Prevention", pf);
            Watch.Update(dt); // v12.0 교대·감지기
            pf = Prof.Lap("sys.Watch", pf);
            Volatile.Update(dt); // v12.2 열·폭발·잔해·역화·일산화탄소·짙은 산소
            pf = Prof.Lap("sys.Volatile", pf);
            Moisture.Update(dt); // v12.3 물·습기·전기 (누전·감전·결로·기동 전류)
            pf = Prof.Lap("sys.Moisture", pf);
            Disease.Update(dt); // v12.4 전염병
            pf = Prof.Lap("sys.Disease", pf);
            Ailments.Update(dt); // v14.1 질병 30
            pf = Prof.Lap("sys.Ailments", pf);
            Belongings.Update(dt); // v14.3 개인 물건 · 취미
            pf = Prof.Lap("sys.Belongings", pf);
            Relations.Update(dt); // v14.4 엇갈린 기억 · 화해한 사이
            pf = Prof.Lap("sys.Relations", pf);
            Ambience.Update(dt); // v12.6 인접성: 소음·진동·냄새·방사선
            pf = Prof.Lap("sys.Ambience", pf);
            Exterior.Update(dt); // v12.6 외부 설비: 안테나·태양 날개 (드론이 고친다)
            pf = Prof.Lap("sys.Exterior", pf);
            Life.Update(dt); // v12.7 실수·말다툼·추모·자격
            pf = Prof.Lap("sys.Life", pf);
            Command.Update(dt); // v13.1 선장·현장 지휘·조 편성
            pf = Prof.Lap("sys.Command", pf);
            Meetings.Update(dt); // v13.2 첫 출항 회의 · 정기 회의 · 사후 검토
            pf = Prof.Lap("sys.Meetings", pf);
            Minds.Update(dt); // v13.3 아는 것 · 감정 · 목표 · 명령 반응
            pf = Prof.Lap("sys.Minds", pf);
            Society.Update(dt); // v13.4 사기 · 일과표 · 베테랑
            pf = Prof.Lap("sys.Society", pf);
            Eras.Update(); // v12.8 시대 기술 (회의가 고른 연구)
            pf = Prof.Lap("sys.Eras", pf);
            Voyage.Update(dt); // v12.8 항로 구간 · 기항지 · 난파선
            pf = Prof.Lap("sys.Voyage", pf);
            Generation.Update(dt); // v12.9 나이 · 짝 · 출생 · 성장
            pf = Prof.Lap("sys.Generation", pf);
            Campaign.Update(); // v12.9 임무
            pf = Prof.Lap("sys.Campaign", pf);
            Volatile.Resume();
            pf = Prof.Lap("sys.Volatile", pf);
            Procedures.Update(this); // v12.1 재조립 불량이 돌아온다
            pf = Prof.Lap("sys.Procedures", pf);
            Causes.Update(); // v12.2 인과 사슬: 번진 상태를 원인에 잇고, 풀린 상태에 복구를 붙인다
            Scale.Update(dt); // v16.18 사슬의 피해로 규모를 재고 · 컴퓨터 판정 · 소집 · 승무원이 느낀다
            pf = Prof.Lap("sys.Causes", pf);
            Collection.Update(dt);
            pf = Prof.Lap("sys.Collection", pf);
            Structure.Update(dt);
            pf = Prof.Lap("sys.Structure", pf);
            Drones.SystemUpdate(dt);
            pf = Prof.Lap("sys.Drones", pf);
            Robots.SystemUpdate(dt);
            pf = Prof.Lap("sys.Robots", pf);
            Propulsion.SystemUpdate(dt);
            pf = Prof.Lap("sys.Propulsion", pf);
            Hazards.SystemUpdate(dt);
            pf = Prof.Lap("sys.Hazards", pf);
            Food.Update(this, dt);
            pf = Prof.Lap("sys.Food", pf);
            Comms.SystemUpdate(dt);
            pf = Prof.Lap("sys.Comms", pf);
            Ledger.Sample(this, dt);
            pf = Prof.Lap("sys.Ledger", pf);
            CheckShip();
            pf = Prof.Lap("sys.CheckShip", pf);
            foreach (var c in Crew) Memory.Update(this, c);
            Prof.Lap("sys.Memory", pf);
        }
        if (Tick % SimTime.Minutes(5) == 0)
        {
            History.Update(this);
            float before = Research;
            Research += Tech.ResearchPerHour(this) * ErasV15.Mul(this, "research") * (SimTime.Minutes(5) / (float)SimTime.TicksPerHour); // v15.5 연구 노트·신경망
            Tech.NoteUnlocks(this, before);
        }
        long ps = Prof.Now;
        Hazards.Step();
        Sensors.Step();
        Propulsion.Step();
        ps = Prof.Lap("step.hazards·sensors", ps);
        Board.Update();
        ps = Prof.Lap("step.Board", ps);
        Drones.Step();
        Robots.Step();
        EvaRisk.Step(); // v16.11 표류하는 몸 · 떠다니는 공구와 잔해
        Prof.Lap("step.drones·robots", ps);

        Movement.BeginTick(); // v14.5 누가 어느 칸에 · 어느 방에 자는 사람이
        foreach (var c in Crew)
        {
            if (c.Away) continue; // v16.12 원정 중 — 배에 없다
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
            long pc = Prof.Now;
            NeedsSystem.Update(c, this);
            CheckVitals(c);
            pc = Prof.Lap("crew.needs", pc);
            if (!c.CanAct)
            {
                UpdatePlace(c);
                continue;
            }
            SenseHazard(c);

            // v8: 선체 밖에서 우주복 산소가 바닥나 가면 하던 일을 두고 돌아온다 (긴 일의 진척은 작업 목록에 남는다)
            if (c.Outside && c.Suit is { Oxygen: < 0.6f } && c.Job?.Activity is not EvacuateActivity and not EvaSurviveActivity && c.CarryingPerson == null)
            {
                if (c.Job != null && Tick - c.SuitWarnedAt > SimTime.Minutes(20))
                {
                    c.SuitWarnedAt = Tick;
                    Log.Add(Tick, LogKind.Warning, $"우주복 산소 {c.Suit.Oxygen * 60:0}분 — 하던 일을 두고 에어락으로 돌아간다", c.Id);
                }
                c.EndJob(this, ToilStatus.Interrupted);
                c.NextThinkTick = Tick;
            }

            // v12.9.1 가는 사이 일할 방의 공기가 나빠졌으면 맨몸으로 들어가지 않는다 — 다시 계획하면 우주복부터 입는다 (비상 대응·치료는 따로 판단)
            if (c.Job is { Order: WorkOrder wo } sj && !sj.WillDonSuit && c.Suit is not { Oxygen: > 1f } && !WorkKinds.IsEmergency(wo.Kind) && wo.Kind != WorkKind.Treat
                && (Tick + c.Id) % 15 == 0 && wo.Target.CurrentRoom is Room tr && c.Room != tr && !tr.Detached && WorkPlanners.Hostile(tr))
            {
                Log.Add(Tick, LogKind.Warning, $"{tr.Name} 공기가 나빠졌다 — 우주복부터 입고 간다", c.Id);
                c.EndJob(this, ToilStatus.Interrupted);
                c.NextThinkTick = Tick;
            }

            // v12.9.1 숨이 찰 만큼 산소가 묽어지면 10분을 기다리지 않고 곧장 다시 판단한다 (대피)
            if ((Tick + c.Id) % 15 == 0 && c.Job?.Activity is not EvacuateActivity && (EvacuateActivity.Breathless(c, this) || c.Room is { EvacuateBy: >= 0 })) c.NextThinkTick = Tick;

            pc = Prof.Lap("crew.checks", pc);
            if (c.Job == null || Tick >= c.NextThinkTick)
            {
                Brain.Think(c, this);
                c.NextThinkTick = Tick + Brain.ThinkInterval + Rng.Range(0, SimTime.Minutes(4));
                pc = Prof.Lap("crew.think", pc);
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

            pc = Prof.Lap("crew.job", pc);
            UpdatePlace(c);
            Ship.DoorAt(c.Cell)?.Request();
            Prof.Lap("crew.place", pc);
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

    internal void KillAway(CrewMember c) => Die(c); // v16.12 원정에서 돌아오지 못한 사람

    private void Die(CrewMember c)
    {
        var lastJob = c.Job?.Order?.Kind; // v13.2 사후 검토: 무엇을 하다 죽었나
        Belongings.OnDeath(c); // v14.3 공구는 제자에게, 사진은 가족에게
        var lastTeam = Command.TeamOf(c);
        c.EndJob(this, ToilStatus.Interrupted);
        if (c.CarriedBy is CrewMember carrier) { carrier.CarryingPerson = null; c.CarriedBy = null; }
        if (c.CareBed is Furniture bed && bed.ReservedBy == c) bed.ReservedBy = null;
        c.CareBed = null;
        c.Dead = true;
        c.DiedAt = Tick;
        c.Down = true;
        c.Pose = Pose.Down;
        c.Path = null;
        c.Destination = null;
        // 사인: 가장 큰 상처를 낸 것 (처음 다친 작은 상처가 아니라) — 숨이 막혀 쓰러졌으면 그대로
        if (c.Vitals.Wounds.Where(x => !x.Lost).OrderByDescending(x => x.Weight).FirstOrDefault() is Wound big && big.Weight >= 0.15f && big.Cause != c.Vitals.InjuryCause
            && c.Vitals.Wounds.Where(x => x.Cause == c.Vitals.InjuryCause).Sum(x => x.Weight) < big.Weight)
            c.Vitals.InjuryCause = big.Cause;
        // 상처가 작은데 죽었다 — 숨이 막혔거나 기력이 다했다 (처음 난 작은 상처가 사인으로 남지 않게)
        float ownWound = c.Vitals.Wounds.Where(x => x.Cause == c.Vitals.InjuryCause).Sum(x => x.Weight);
        if (c.Vitals.Injury < 0.4f && ownWound < 0.15f || c.Vitals.InjuryCause == "작업 중 실수" && ownWound < 0.4f)
            c.Vitals.InjuryCause = c.Vitals.Oxygen < 0.5f ? "질식" : c.Vitals.InjuryCause is null or "작업 중 실수" ? "기력이 다해" : c.Vitals.InjuryCause;
        c.Vitals.InjuryCause = Casualty.DeathCause(c) ?? Perils.DeathCause(c) ?? c.Vitals.InjuryCause; // v16.24 상처 뒤 출혈 · 심정지 · v16.26 열사병 · 방사선 병
        Perils.OnDeath(c); // v16.26
        RaiseAlert($"{Ko.IGa(c.Name)} 죽었다 — {c.Room?.Name ?? "떨어져 나간 구획"} ({c.Vitals.InjuryCause ?? "사고"})", c.Room, AlertLevel.Critical, shipWide: true);
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
        Life.OnDeath(c); // v12.7 추모 · 슬픔
        Command.OnDeath(c); // v13.1 선장에 대한 신뢰
        Meetings.OnDeath(c, lastJob, lastTeam, c.Room); // v13.2 결정의 무게 · 사후 검토
        if (History.Current != null) History.Current.Deaths++;
        if (c.Room != null)
        {
            c.Room.Deaths++;
            MarkLog.Add(c.Room.Marks, Tick, $"{Ko.IGa(c.Name)} 죽었다");
        }
        History.Add(this, HistoryKind.Death, $"{Ko.IGa(c.Name)} {c.Room?.Name ?? (Ship.Grid.InBounds(c.Cell) ? "떨어져 나간 구획" : "배 밖")}에서 죽었다 ({c.Vitals.InjuryCause ?? "사고"})", c.Room, new[] { c });
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
            if (Blast.HearLate(c, nearby, text)) continue; // v16.13 이명 — 경보를 늦게 듣는다
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
        if (c.Pose == Pose.Sleeping && (c.Room.Air.Smoke > 0.15f || c.Room.Air.O2 < 16.5f || c.Room.Air.Temperature < 8f || c.Room.Air.Temperature > 40f || c.Room.Air.Toxin > 0.1f)
            && Perils.WakesFromSleep(c)) // v16.26 냄새만으로는 잘 안 깬다 (몸이 느끼는 것은 깨운다)
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
    public const int MaxCrew = 60; // v16.9 40~60인 배

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
        world.Net.SeedRings(template.Crew); // v12.3 중형 이상 배는 처음부터 보조 간선
        var rng = world.Rng;
        int crewSize = crew <= 0 ? template.Crew : Math.Clamp(crew, 1, MaxCrew);
        world.StartCrew = crewSize;
        world.StartTuning = Tuning.NonDefault().ToList();
        if (CampaignSystem.ModeValue >= 2f) world.Generation.Enable(); // v12.9 처음부터 세대선
        else if (CampaignSystem.ModeValue >= 1f) world.Campaign.Start(); // v12.9 이어지는 임무
        var ship = shipObj;

        // v10.4: 큰 배는 공기 탱크·물통도 크고, 처음 싣는 물자도 설계 인원만큼
        float scale = MathF.Max(1f, template.Crew / 6f);
        world.Air.ReserveCapacity *= scale;
        world.Air.Reserve = world.Air.ReserveCapacity;
        world.Water.Capacity *= scale;
        world.Water.Level *= scale;
        world.Propulsion.SetScale(scale);
        StockShip(ship, rng, scale * Storyteller.StockScale); // v12.4 난이도: 시작 물자
        if (ShipClasses.Base(template.Key) || template.Key.StartsWith("gen:") && !template.Legacy) { ShipClasses.Stock(ship, ShipClasses.Of(template)); if (ShipClasses.Base(template.Key)) ShipClasses.FitOut(world, ShipClasses.Of(template)); } // v16.22 크기 등급: 시작 물자 · 특수 방 대표 설비
        if (Storyteller.StockScale < 0.99f) TrimStock(ship, Storyteller.StockScale);

        var beds = ship.FurnitureOf(FurnitureType.Bed).OrderBy(b => b.MinX).ToList();
        float hour = SimTime.HourOfDay(world.Tick);

        // v12.0 이름은 시드마다 다르게 (배의 난수와 따로 굴린다)
        var names = NameGen.ForShip(seed, crewSize);
        for (int i = 0; i < crewSize; i++)
        {
            var s = crewSize == 2 && i == 1 ? DefaultCrew[2] : i < DefaultCrew.Length ? DefaultCrew[i] : ExtraSeed(i, rng); // v16.9 2인 배는 기관사 + 조종사
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

        world.Origin.Apply(template); // v16.9 설계사 · 시작 상태 · 시작 화물 · 숨은 이야기 · 침대 교대 (예전 배는 그대로)
        world.Power.Update(0.01f);
        world.Log.Add(world.Tick, LogKind.Ship, $"{ship.Name} 항해 기록 시작 · 승무원 {world.Crew.Count}명");
        world.History.Founded(world);
        world.InitialProfile = ShipProfile.Measure(world);
        Core.Life.Assign(world); // v12.7 경력·가치관·습관·자격 (따로 뽑아 본 난수를 건드리지 않는다)
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

    /// <summary>v12.9 세대선: 배에서 태어난 아이 — 부모를 닮은 역할·성격, 솜씨는 거의 없다. 부모 곁에서 자란다.</summary>
    public CrewMember AddChild(CrewMember a, CrewMember b, Rng rng)
    {
        int id = Crew.Count;
        int k = Crew.Count(c => c.Rescued || c.BornAboard);
        string name = NameGen.Newcomer(Seed, 50 + k, Crew.Select(c => c.Name));
        var role = rng.Chance(0.5f) ? a.Role : b.Role;
        float Mix(float x, float y, float lo, float hi) => Math.Clamp((x + y) / 2f + rng.Range(-0.15f, 0.15f), lo, hi);
        var c = new CrewMember
        {
            Id = id,
            Name = name,
            Role = role,
            Traits = new Personality
            {
                Diligence = Mix(a.Traits.Diligence, b.Traits.Diligence, 0.2f, 0.95f), Sociability = Mix(a.Traits.Sociability, b.Traits.Sociability, 0.2f, 0.9f),
                Bravery = Mix(a.Traits.Bravery, b.Traits.Bravery, 0.15f, 0.9f), Appetite = rng.Range(0.85f, 1.15f), Calm = Mix(a.Traits.Calm, b.Traits.Calm, 0.2f, 0.8f),
            },
            Memory = new CrewMemory(Ship.Rooms.Count),
            Schedule = Schedule.FromBedtime(a.Schedule.SleepStart),
            Stations = a.Role == role ? a.Stations : b.Stations,
            SkillLevels = Enumerable.Repeat(0.03f, Skills.All.Length).ToArray(),
            BornAboard = true,
        };
        c.Age = 0f;
        c.Profiled = true;
        c.Parents.Add(a.Id); c.Parents.Add(b.Id); // v14.3
        c.Background = Background.Teacher;
        c.Value = rng.Chance(0.5f) ? a.Value : b.Value;
        c.Joined = "배에서 태어났다";
        c.Position = a.Position;
        c.PreviousPosition = c.Position;
        c.Room = a.Room;
        foreach (var o in Crew)
        {
            c.Affinity[o.Id] = o == a || o == b ? 0.8f : 0.3f;
            o.Affinity[c.Id] = o == a || o == b ? 0.9f : 0.3f;
        }
        Crew.Add(c);
        Paths.Invalidate();
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
    /// <summary>v12.4 어려운 난이도: 수리재·예비 부품·구급 키트를 덜 싣고 떠난다 (회복 여력이 적다).</summary>
    private static void TrimStock(Ship ship, float scale)
    {
        foreach (var kind in new[] { ItemKind.Sealant, ItemKind.Plate, ItemKind.Structure, ItemKind.Cable, ItemKind.Fuse, ItemKind.Filter, ItemKind.Pump,
                     ItemKind.Bearing, ItemKind.Motor, ItemKind.PowerController, ItemKind.Sensor, ItemKind.Electronics, ItemKind.MedKit, ItemKind.Extinguisher })
        {
            int drop = (int)MathF.Round(ship.CountStored(kind) * (1f - scale));
            foreach (var f in ship.Containers)
            {
                if (drop <= 0) break;
                drop -= f.Storage!.Take(kind, drop);
            }
        }
    }

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

        ItemsV15.Stock(ship); // v15 비누·세제·소독약·붕대·테이프·커피 · 흔한 예비 부품 몇

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
