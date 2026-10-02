using System;
using System.Collections.Generic;

// v16.23 점검 항해 — 한 번 돌린 항해(배 × 시드)의 날것 기록. 프로세스 사이에 JSON 으로 오간다.
// 벽시계 값(Wall · SecPerDay · Prof)만 빼면 같은 인자 → 같은 숫자 (결정론).

public sealed class AuditRun
{
    public string Ship = "", ShipName = "";
    public int Seed;
    public float Days;
    public int Crew, Alive, RoomsTotal, RoomsLost;
    public uint Hash;
    public double Wall, SecPerDay;
    public string? Error;
    public float ErrorHour = -1f;
    public List<ADeath> Deaths = new();
    public List<ADown> Downs = new();
    public List<int> PanicMin = new();
    public List<ACase> Cases = new();
    public int FaultEvents;
    public List<ARepeat> Repeats = new();
    public List<ARefail> Refails = new();
    public List<AAux> Aux = new();
    public List<ARoom> Rooms = new();
    public Dictionary<string, int> FixPresent = new(), FixTouched = new();
    public List<string> Acts = new(), Daily = new(), Scenes = new(), TechGained = new(), Keys = new();
    public Dictionary<string, float> ResStart = new(), ResMin = new(), ResOut = new();
    public Dictionary<string, int> Harvest = new();
    public float FoodIn, FoodOut;
    public AComp Comp = new();
    public ABots Bots = new();
    public List<AProf> Prof = new();
    public List<AStall> Stalls = new();
    public Dictionary<string, int> TextHits = new();
    public List<string> TextEx = new();
}

/// <summary>죽음 하나: 죽기 전 30분 동안 무엇을 했나.</summary>
public sealed class ADeath
{
    public float Hour;
    public string Name = "", Room = "", Cause = "", Case = "";
    public int Scale = -1, SelfMin, NearMin, DownMin, PanicMin, RoomId = -1, CrewId = -1;
    public long Tick;
    public bool Panic, Sudden;
}

/// <summary>쓰러짐 하나 (죽음 바로 아래 단계 — 사고가 사람을 얼마나 위협했나).</summary>
public sealed class ADown { public float Hour; public long Tick; public string Name = "", Room = ""; public int RoomId = -1, CrewId = -1, Scale = -1; }

public sealed class ACase
{
    public string Key = "", Name = "", Room = "";
    public int Base, Peak, Deaths, Downs, Chain, Spread;
    public float Hour, Hours;
}

public sealed class ARepeat { public string What = "", Room = ""; public int Count; public float First, Last; }
public sealed class ARefail { public string What = "", Room = ""; public float Hour, GapMin; }

/// <summary>정전 · 배터리 바닥 한 번: 보조 발전기를 켰나, 언제, 누가.</summary>
public sealed class AAux { public float Hour, Minutes, AfterMin = -1f; public string By = "", Why = ""; public bool Had; }

public sealed class ARoom { public string Kind = "", Name = ""; public int Dwell, Touched; }

/// <summary>주컴퓨터 — 지금 있는 공개 값만. 못 재면 Measured=false.</summary>
public sealed class AComp
{
    public bool Measured;
    public string Note = "";
    public int Total, Right, Wrong, Held, Remote, Asked, OfflineMin, Reboots, Overheats, GradeDrops, Decisions, Options;
}

public sealed class ABots
{
    public int Robots, RobotMin, RobotActive, RobotDown, RobotFaults, RobotLost;
    public int Drones, DroneMin, DroneActive, DroneDown, DroneFaults, DroneAdrift, DroneLost;
}

public sealed class AProf { public string Key = ""; public double Ms; }
public sealed class AStall { public float Hour, Hours; public string What = "", Who = "", Room = ""; }
