using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShipSim.Core;

// ─────────────────────────────── 방 ───────────────────────────────

public enum RoomType
{
    Corridor, Bridge, Engine, Reactor, Cooling, Power, LifeSupport,
    Workshop, Storage, Galley, Mess, Hydroponics, Airlock, Quarters, Medbay, Lounge,
    Comms, // v10.1 통신실 (장거리 센서·통신 콘솔)
}

public static class RoomTypes
{
    public static string Name(RoomType t) => t switch
    {
        RoomType.Corridor => "중앙 통로",
        RoomType.Bridge => "함교",
        RoomType.Engine => "엔진실",
        RoomType.Reactor => "원자로실",
        RoomType.Cooling => "냉각실",
        RoomType.Power => "배전실",
        RoomType.LifeSupport => "생명유지실",
        RoomType.Workshop => "정비실",
        RoomType.Storage => "창고",
        RoomType.Galley => "주방",
        RoomType.Mess => "식당",
        RoomType.Hydroponics => "수경재배실",
        RoomType.Airlock => "에어락",
        RoomType.Quarters => "침실",
        RoomType.Medbay => "의무실",
        RoomType.Lounge => "휴게실",
        RoomType.Comms => "통신실",
        _ => t.ToString(),
    };

    /// <summary>설계도의 소문자 라벨 → 방 종류.</summary>
    public static RoomType? FromLabel(char c) => c switch
    {
        'c' => RoomType.Corridor,
        'b' => RoomType.Bridge,
        'e' => RoomType.Engine,
        'r' => RoomType.Reactor,
        'k' => RoomType.Cooling,
        'p' => RoomType.Power,
        'l' => RoomType.LifeSupport,
        'w' => RoomType.Workshop,
        's' => RoomType.Storage,
        'j' => RoomType.Galley,
        'm' => RoomType.Mess,
        'f' => RoomType.Hydroponics,
        'a' => RoomType.Airlock,
        'q' => RoomType.Quarters,
        'h' => RoomType.Medbay,
        'g' => RoomType.Lounge,
        'o' => RoomType.Comms,
        _ => null,
    };
}

/// <summary>방 하나의 공기. 기체는 방 단위로만 계산한다 (재난 위치는 칸 단위).</summary>
public sealed class RoomAir
{
    /// <summary>산소 분압 (kPa). 정상 21.</summary>
    public float O2 { get; set; } = 21f;

    /// <summary>질소 분압 (kPa). 정상 79.</summary>
    public float N2 { get; set; } = 79f;

    /// <summary>이산화탄소 분압 (kPa). 정상 0.04, 1 넘으면 답답, 3 넘으면 위험.</summary>
    public float CO2 { get; set; } = 0.04f;

    /// <summary>섭씨 온도.</summary>
    public float Temperature { get; set; } = 21f;

    /// <summary>연기 0~1 (화재가 들어오면 쓰임).</summary>
    public float Smoke { get; set; }

    /// <summary>선체 파공으로 우주로 새는 속도 (칸/시간). 기압 × 이 값 / 부피 만큼 빠진다. 0이면 밀폐.</summary>
    public float Leak { get; set; }

    public float Pressure => O2 + N2 + CO2;
}

public sealed class Room
{
    public int Id { get; init; }
    public RoomType Type { get; init; }
    public string Name => NameOverride ?? RoomTypes.Name(Type);

    /// <summary>v10.2: 칸막이로 나눈 방의 이름 ("식당 안쪽 칸").</summary>
    public string? NameOverride { get; init; }

    /// <summary>v10.2: 칸막이로 나눈 방 (다시 나누지 않는다).</summary>
    public bool Partitioned { get; set; }
    public List<Cell> Cells { get; } = new();
    public List<Furniture> Furniture { get; } = new();
    public List<Door> Doors { get; } = new();

    public RoomAir Air { get; } = new();

    /// <summary>배전반의 어느 회로에 물려 있는지 (0=A ...).</summary>
    public int Circuit { get; set; }

    /// <summary>조명·환기팬에 전기가 들어오는지.</summary>
    public bool Powered { get; set; } = true;

    /// <summary>길찾기에서 이 방을 지나는 추가 비용 (공기·온도·어둠). 공기 시스템이 갱신.</summary>
    public int HazardCost { get; set; }

    /// <summary>v9.4 조명 고장 (전기가 있어도 캄캄하다): 일이 느리고, 길을 꺼리고, 오래 있으면 불안하다.</summary>
    public bool LightsOut { get; set; }
    public long LightsOutSince { get; set; }

    /// <summary>캄캄한 방 (정전이거나 조명이 나갔다).</summary>
    public bool Dark => !Powered || LightsOut;

    /// <summary>진공에 가까워 우주복 없이는 못 들어감.</summary>
    public bool Unbreathable { get; set; }

    /// <summary>환기 댐퍼가 열려 있는지. 닫으면 환기망에서 이 방이 떨어져 나간다 (구획 격리).</summary>
    public bool VentOpen { get; set; } = true;

    /// <summary>댐퍼 조작 위치 (보통 방 밖, 문 옆 통로 천장).</summary>
    public Cell DamperSpot { get; set; }

    /// <summary>감압으로 격벽이 잠긴 상태.</summary>
    public bool Lockdown { get; set; }

    /// <summary>
    /// 포기한 구획. 문을 용접해 막고 환기 댐퍼를 닫았다. 우주복을 입은 사람만 드나들고, 재가압하지 않는다.
    /// 조건이 돌아오면(봉합할 수 있고 공기가 있으면) 다시 연다.
    /// </summary>
    public bool Abandoned { get; set; }

    /// <summary>포기한 이유 (기록·화면).</summary>
    public string? AbandonReason { get; set; }

    /// <summary>새기 시작한 틱 (새지 않으면 의미 없음).</summary>
    public long LeakingSince { get; set; }

    /// <summary>포기한 틱.</summary>
    public long AbandonedSince { get; set; }

    /// <summary>원래 용도와 다르게 쓰이면 그 용도 ("임시 침실"). 흔적으로 남는다.</summary>
    public string? Purpose { get; set; }

    /// <summary>선체 파공으로 새고 있는 정도 (틈 넓이 합).</summary>
    public float BreachArea { get; set; }

    // ── 역사 (v7): 이 방이 겪은 것 ──
    public int Breaches { get; set; }
    public int Fires { get; set; }
    public int TimesAbandoned { get; set; }
    public int Collapses { get; set; }
    public int Deaths { get; set; }

    /// <summary>자동 소화 장치 (v7: 불을 여러 번 겪은 방에 단다). 전기가 있으면 불이 자라지 못하게 뿌린다.</summary>
    public bool Suppression { get; set; }

    /// <summary>예전에 쓰이던 용도 (임시 침실이었다가 되돌아간 방 등).</summary>
    public List<string> FormerPurposes { get; } = new();
    public List<Mark> Marks { get; } = new();

    // ── 구조 (v8): 외판을 용골(중앙 통로)에 잇는 연결부 ──
    /// <summary>구조 연결부. 끊긴 만큼 남은 연결부에 하중이 몰리고, 다 끊어지면 방이 떨어져 나간다.</summary>
    public List<Joint> Joints { get; } = new();

    /// <summary>설계 때의 연결부 수 (하중 기준).</summary>
    public int DesignJoints { get; set; }

    /// <summary>선체에서 떨어져 나갔다 (격자에서 빠지고 조각으로 떠다닌다).</summary>
    public bool Detached { get; set; }

    /// <summary>떨어져 나간 조각 (떨어져 있거나 끌려오는 동안).</summary>
    public Fragment? Fragment { get; set; }

    /// <summary>진행 중인 사출 절차.</summary>
    public JettisonPlan? Jettison { get; set; }

    /// <summary>전력을 끊었다 (사출 준비 · 떨어졌다 붙어 아직 재연결 전).</summary>
    public bool PowerCut { get; set; }

    /// <summary>배관을 잠갔다.</summary>
    public bool PipesCut { get; set; }

    /// <summary>환기관을 막았다 (댐퍼를 닫고 덕트를 봉했다).</summary>
    public bool VentSealed { get; set; }

    /// <summary>임시 도킹으로 다시 붙었지만 아직 전력·배관·환기가 이어지지 않았다.</summary>
    public bool Docked { get; set; }

    /// <summary>되살리지 않기로 한 잔해 (부품과 물자만 뜯어 쓴다).</summary>
    public bool Wreck { get; set; }

    /// <summary>환기 댐퍼가 열에 걸려 움직이지 않는다 (불이 꺼지면 풀린다). 불난 방이 우주선 공기를 빨아들인다.</summary>
    public bool DamperJammed { get; set; }

    /// <summary>v9.2: 댐퍼 구동기가 걸렸다 (열과 상관없이, 손으로 풀어야 한다).</summary>
    public bool DamperStuck { get; set; }

    /// <summary>되살리기로 했다 (재연결 → 재가압 중).</summary>
    public bool Restoring { get; set; }

    /// <summary>사람이 쓰지 않는 방: 떨어져 나갔거나, 사출하기로 했거나, 잔해로 두었다.</summary>
    public bool OffLimits => Detached || Jettison != null || Wreck;

    /// <summary>물이 드나드는 방 (떨어질 때 배관을 안 잠갔으면 물이 샌다).</summary>
    public bool HasPipes => Furniture.Exists(f => f.Type is FurnitureType.GrowBed or FurnitureType.WaterRecycler or FurnitureType.Stove);

    public int Detachments { get; set; }
    public int Jettisons { get; set; }
    public int Retrievals { get; set; }

    public int Volume => Cells.Count;

    public bool Leaking => BreachArea > 0.0001f;

    public int MinX { get; set; } = int.MaxValue;
    public int MinY { get; set; } = int.MaxValue;
    public int MaxX { get; set; } = int.MinValue;
    public int MaxY { get; set; } = int.MinValue;

    public Vector2 Center => new((MinX + MaxX + 1) * 0.5f, (MinY + MaxY + 1) * 0.5f);

    internal void Include(Cell c)
    {
        Cells.Add(c);
        if (c.X < MinX) MinX = c.X;
        if (c.Y < MinY) MinY = c.Y;
        if (c.X > MaxX) MaxX = c.X;
        if (c.Y > MaxY) MaxY = c.Y;
    }

    /// <summary>v10.2: 칸을 떼어 낸 뒤 테두리를 다시 잰다.</summary>
    internal void RecomputeBounds()
    {
        MinX = MinY = int.MaxValue;
        MaxX = MaxY = int.MinValue;
        foreach (var c in Cells)
        {
            if (c.X < MinX) MinX = c.X;
            if (c.Y < MinY) MinY = c.Y;
            if (c.X > MaxX) MaxX = c.X;
            if (c.Y > MaxY) MaxY = c.Y;
        }
    }
}

// ─────────────────────────────── 가구/설비 ───────────────────────────────

public enum FurnitureType
{
    Bed, Seat, Table, MealDispenser, Console,
    ReactorCore, EngineCore, OxygenGenerator, Workbench, Shelf, MedBed,
    CoolantPump, PowerPanel, Battery, GrowBed, WaterRecycler, Stove, Fridge, SuitLocker,
    AuxGenerator,
    Cot, // 간이침대 (방 용도를 바꿀 때 깐다. 설계도에는 없다)
    Collector, Refinery, // 채집 장치(선체 바깥 채집 팔 + 호퍼), 정제기
    DroneDock, // 드론 거치대 (충전·정비·자재 적재) v8
    MainComputer, // 주 컴퓨터 (함교, 자동화의 두뇌) v9
    SensorArray, // 장거리 센서 (통신실, 운석 조기 경보) v10.1
    // v10.6 방 모듈 (개조로 단다)
    LedPanel, HeatExchanger, CapacitorBank, Scrubber, Fabricator,
    RobotDock, // v10.10 선내 로봇 충전대 (칸마다 로봇 한 대)
    SupplyCache, // v10.10 비상 물자함 (실링폼·구급 키트·소화기를 창고 밖에 나눠 둔다 — 개조로 단다)
}

public static class FurnitureTypes
{
    /// <summary>설계도의 대문자 → 가구 종류.</summary>
    public static FurnitureType? FromChar(char c) => c switch
    {
        'B' => FurnitureType.Bed,
        'S' => FurnitureType.Seat,
        'T' => FurnitureType.Table,
        'D' => FurnitureType.MealDispenser,
        'C' => FurnitureType.Console,
        'R' => FurnitureType.ReactorCore,
        'E' => FurnitureType.EngineCore,
        'O' => FurnitureType.OxygenGenerator,
        'W' => FurnitureType.Workbench,
        'K' => FurnitureType.Shelf,
        'M' => FurnitureType.MedBed,
        'P' => FurnitureType.CoolantPump,
        'X' => FurnitureType.PowerPanel,
        'Y' => FurnitureType.Battery,
        'G' => FurnitureType.GrowBed,
        'U' => FurnitureType.WaterRecycler,
        'V' => FurnitureType.Stove,
        'F' => FurnitureType.Fridge,
        'L' => FurnitureType.SuitLocker,
        'Z' => FurnitureType.AuxGenerator,
        'H' => FurnitureType.Collector,
        'N' => FurnitureType.Refinery,
        'Q' => FurnitureType.DroneDock,
        'I' => FurnitureType.MainComputer,
        'A' => FurnitureType.SensorArray,
        'J' => FurnitureType.RobotDock,
        _ => null,
    };

    public static string Name(FurnitureType t) => t switch
    {
        FurnitureType.Bed => "침대",
        FurnitureType.Seat => "의자",
        FurnitureType.Table => "테이블",
        FurnitureType.MealDispenser => "배식기",
        FurnitureType.Console => "콘솔",
        FurnitureType.ReactorCore => "원자로",
        FurnitureType.EngineCore => "엔진",
        FurnitureType.OxygenGenerator => "산소 발생기",
        FurnitureType.Workbench => "작업대",
        FurnitureType.Shelf => "선반",
        FurnitureType.MedBed => "치료 침대",
        FurnitureType.CoolantPump => "냉각 펌프",
        FurnitureType.PowerPanel => "배전반",
        FurnitureType.Battery => "배터리",
        FurnitureType.GrowBed => "재배대",
        FurnitureType.WaterRecycler => "정수기",
        FurnitureType.Stove => "조리대",
        FurnitureType.Fridge => "냉장고",
        FurnitureType.SuitLocker => "우주복 보관함",
        FurnitureType.AuxGenerator => "보조 발전기",
        FurnitureType.Cot => "간이침대",
        FurnitureType.Collector => "채집 장치",
        FurnitureType.Refinery => "정제기",
        FurnitureType.DroneDock => "드론 거치대",
        FurnitureType.MainComputer => "주 컴퓨터",
        FurnitureType.SensorArray => "장거리 센서",
        FurnitureType.LedPanel => "LED 생장 모듈",
        FurnitureType.HeatExchanger => "열교환 모듈",
        FurnitureType.CapacitorBank => "축전 모듈",
        FurnitureType.Scrubber => "CO₂ 세정 모듈",
        FurnitureType.Fabricator => "정밀 가공 모듈",
        FurnitureType.RobotDock => "로봇 충전대",
        FurnitureType.SupplyCache => "비상 물자함",
        _ => t.ToString(),
    };

    /// <summary>올라서거나 누울 수 있는 가구.</summary>
    public static bool Walkable(FurnitureType t) =>
        t is FurnitureType.Bed or FurnitureType.Seat or FurnitureType.MedBed or FurnitureType.Cot;

    /// <summary>누워서 제대로 잘 수 있는 가구.</summary>
    public static bool Sleepable(FurnitureType t) => t is FurnitureType.Bed or FurnitureType.MedBed or FurnitureType.Cot;

    /// <summary>같은 글자가 붙어 있으면 하나로 묶는다.</summary>
    public static bool Groups(FurnitureType t) =>
        t is not (FurnitureType.Seat or FurnitureType.Console or FurnitureType.MealDispenser);

    /// <summary>당직 근무 중에 붙어 있는 자리 (감시·기록).</summary>
    public static bool IsDutyStation(FurnitureType t) =>
        t is FurnitureType.Console or FurnitureType.Workbench or FurnitureType.MedBed;
}

public sealed class Furniture
{
    public int Id { get; init; }
    public FurnitureType Type { get; init; }
    public Room Room { get; set; } = null!;
    public List<Cell> Cells { get; } = new();

    /// <summary>이 가구를 쓰려면 서 있어야(앉아야) 하는 칸들.</summary>
    public List<Cell> UseSpots { get; } = new();

    /// <summary>지금 이 가구를 쓰기로 예약한 승무원.</summary>
    public CrewMember? ReservedBy { get; set; }

    /// <summary>개인 소유 (침대 등).</summary>
    public CrewMember? Owner { get; set; }

    /// <summary>전기를 먹고, 닳고, 고장 나는 설비라면 그 상태.</summary>
    public Machine? Machine { get; set; }

    /// <summary>물건을 넣어 두는 곳이라면 그 보관함.</summary>
    public Inventory? Storage { get; set; }

    public int MinX { get; set; } = int.MaxValue;
    public int MinY { get; set; } = int.MaxValue;
    public int MaxX { get; set; } = int.MinValue;
    public int MaxY { get; set; } = int.MinValue;
    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;
    public Vector2 Center => new((MinX + MaxX + 1) * 0.5f, (MinY + MaxY + 1) * 0.5f);

    public string Name => FurnitureTypes.Name(Type);

    /// <summary>같은 종류끼리 구분하는 이름. "냉각 펌프 #2".</summary>
    public string Label { get; set; } = "";

    /// <summary>항해 중에 손본 가구 (제대로 정비한 간이침대, 증설한 배터리).</summary>
    public bool Improved { get; set; }

    /// <summary>방째로 떨어져 나가 우주선 밖에 있다.</summary>
    public bool Detached => Room.Detached;

    /// <summary>v10.10: 치웠다 (접은 간이침대, 해체해 재활용한 설비). 목록에는 남지만(번호가 바뀌지 않게) 배에는 없다.</summary>
    public bool Stowed { get; internal set; }

    /// <summary>v10.10: 주인 없이 빈 채로 있기 시작한 틱 (간이침대를 치울 때를 본다 — 작업 목록이 훑으며 적는다, 0이면 모름).</summary>
    public long EmptySince { get; set; }

    /// <summary>v11.0: 마지막으로 점검한 틱 (우주복 보관함: 오래 안 보면 우주복 밸브가 샌다).</summary>
    public long Checked { get; set; }

    public bool IsFreeFor(CrewMember c) => ReservedBy == null || ReservedBy == c;

    internal void Include(Cell c)
    {
        Cells.Add(c);
        if (c.X < MinX) MinX = c.X;
        if (c.Y < MinY) MinY = c.Y;
        if (c.X > MaxX) MaxX = c.X;
        if (c.Y > MaxY) MaxY = c.Y;
    }
}

// ─────────────────────────────── 문 ───────────────────────────────

public sealed class Door
{
    public int Id { get; init; }
    public Cell Cell { get; init; }

    /// <summary>true면 위아래를 잇는 문(가로 벽에 뚫림), false면 좌우를 잇는 문.</summary>
    public bool ConnectsVertically { get; init; }

    /// <summary>한쪽이 우주인 외부 해치.</summary>
    public bool IsExternal { get; init; }

    public Room? RoomA { get; set; }
    public Room? RoomB { get; set; }

    /// <summary>0 = 닫힘, 1 = 활짝 열림.</summary>
    public float Openness { get; set; }

    /// <summary>전기가 없으면 손으로 천천히 연다.</summary>
    public bool Powered { get; set; } = true;

    /// <summary>잠김 (격벽 폐쇄 등). 지금은 외부 해치만 잠겨 있다.</summary>
    public bool Locked { get; set; }

    /// <summary>용접해 막은 격벽 (사출 준비). 떨어져 나가도 이쪽은 새지 않는다.</summary>
    public bool Welded { get; set; }

    /// <summary>한쪽 방이 떨어져 나가 문이 벽으로 바뀌었다 (다시 붙으면 되살아난다).</summary>
    public bool Removed { get; set; }

    /// <summary>열에 휘어 열린 채로 걸렸다 (불이 옆방 공기를 끌어들인다). 지렛대로 억지로 닫아야 한다.</summary>
    public bool JammedOpen { get; set; }

    /// <summary>v9.4 문 구동기(모터) 고장: 전기가 있어도 손으로 천천히 열고, 격벽이 저절로 잠기지 않는다.</summary>
    public bool MotorBroken { get; set; }

    /// <summary>모터 대신 케이블·금속판으로 엮은 임시 구동기 (느리다).</summary>
    public bool MotorMk1 { get; set; }

    /// <summary>열린 횟수 (닳음). 시스템 틱마다 새로 열린 만큼 고장을 굴린다.</summary>
    public int Cycles { get; set; }
    internal int PendingCycles { get; set; }
    public int MotorBreaks { get; set; }

    private bool _requested;
    private bool _override;

    public void Request() => _requested = true;

    /// <summary>잠긴 격벽을 손으로 비상 개방 (느리고, 열리는 동안 공기가 섞인다).</summary>
    public void RequestOverride()
    {
        _requested = true;
        _override = true;
    }

    internal void Update()
    {
        if (JammedOpen && !Removed)
        {
            Openness = MathF.Min(1f, Openness + 0.05f);
            _requested = false;
            _override = false;
            return;
        }
        bool open = _requested && (!Locked || _override);
        float speed = Locked ? 0.03f : Powered ? (MotorMk1 ? 0.06f : 0.1f) : 0.025f;
        float before = Openness;
        Openness += open ? speed : Locked ? -0.08f : -0.04f;
        if (Openness < 0f) Openness = 0f;
        if (Openness > 1f) Openness = 1f;
        if (before < 0.05f && Openness >= 0.05f) { Cycles++; PendingCycles++; }
        _requested = false;
        _override = false;
    }
}

// ─────────────────────────────── 벽 ───────────────────────────────

/// <summary>
/// 벽 한 칸의 상태. 운석이 맞으면 찌그러짐 → 균열 → 미세 누출 → 파공으로 나빠진다.
/// 지금은 전부 멀쩡하지만, 사고를 넣기 전에 자리를 만들어 둔다.
/// </summary>
public sealed class WallState
{
    /// <summary>1 = 멀쩡, 0 = 완전히 뚫림.</summary>
    public float Integrity { get; set; } = 1f;

    /// <summary>뚫린 구멍 크기 0~1. 0보다 크면 공기가 샌다.</summary>
    public float Breach { get; set; }

    public bool Patched { get; set; }
    public float PatchQuality { get; set; }

    /// <summary>한쪽이 우주에 닿은 외벽인지 (옆 방이 떨어져 나가면 안쪽 벽도 외벽이 된다).</summary>
    public bool IsHull { get; set; }

    /// <summary>
    /// 골조 0~1 (v8). 외판이 다 뚫린 뒤에도 충격이 남으면 골조가 상한다.
    /// 0.35 아래는 "구조 연결 상실": 외판이 골조에서 뜯겨 나간 큰 구멍이라 실링폼으로는 못 막고,
    /// 밖에서(드론·EVA) 골조부터 다시 세워야 한다.
    /// </summary>
    public float Frame { get; set; } = 1f;

    public bool FrameLost => Frame < 0.35f;

    /// <summary>용접으로 수리한 횟수 (흔적으로 남는다).</summary>
    public int Welds { get; set; }

    /// <summary>구조재로 패널을 통째로 갈아 끼운 횟수. 갈면 피로가 풀린다 (땜질 ≠ 새것, 교체 ≈ 새것).</summary>
    public int Replacements { get; set; }

    /// <summary>
    /// 구조 피로: 이 벽이 회복할 수 있는 최대 강도. 용접할 때마다 내려간다.
    /// 피로한 벽은 같은 충격에 더 크게 상하고, 결국 용접으로는 막을 수 없게 된다.
    /// </summary>
    public float MaxIntegrity { get; set; } = 1f;

    /// <summary>불에 그을린 정도 0~1 (흔적).</summary>
    public float Scorch { get; set; }

    // ── 역사 (v7) ──
    /// <summary>뚫린 횟수 (새지 않던 벽이 새기 시작한 횟수).</summary>
    public int Breaches { get; set; }

    /// <summary>실링폼으로 막은 횟수.</summary>
    public int Seals { get; set; }

    /// <summary>용접한 횟수 전부 (패널을 갈아도 줄지 않는다).</summary>
    public int TotalWelds { get; set; }

    /// <summary>보강판을 덧댔다 (운석을 겪은 뒤의 개조). 같은 충격에 덜 상한다.</summary>
    public bool Reinforced { get; set; }

    public List<Mark> Marks { get; } = new();

    /// <summary>손상 단계 이름: 정상 → 찌그러짐 → 균열 → 미세 누출 → 파공 → 구조 연결 상실 (봉합하면 임시 봉합).</summary>
    public string Stage => FrameLost ? "구조 연결 상실"
        : Patched && Breach > 0f ? "임시 봉합"
        : Integrity < 0.1f ? "파공"
        : Integrity < 0.35f ? "미세 누출"
        : Integrity < 0.6f ? "균열"
        : Integrity < 0.85f ? "찌그러짐"
        : "정상";

    /// <summary>손상 단계 0(정상)~4(파공), 5(구조 연결 상실).</summary>
    public int StageIndex => FrameLost ? 5 : Integrity < 0.1f ? 4 : Integrity < 0.35f ? 3 : Integrity < 0.6f ? 2 : Integrity < 0.85f ? 1 : 0;
}
