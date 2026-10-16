using System.Runtime.InteropServices;

namespace ShipEngine;

public enum AgentKind : byte { Person, Robot, Drone }

/// <summary>믿음의 종류 — '안다고 생각하는 것'.</summary>
public enum BeliefKind : byte
{
    None,
    Fire,        // 불
    Smoke,       // 연기
    Breach,      // 벽에 구멍 (칸 = 벽 칸)
    Vacuum,      // 공기가 빠졌다 (칸 = 그 방의 한 칸)
    Injured,     // 쓰러진 사람 (대상 = 사람)
    Dead,        // 죽은 사람 (대상 = 사람)
    Broken,      // 멈춘 장치 (대상 = 장치)
    Bang,        // 쾅 소리
    Strain,      // 끼익 — 버티는 문 · 벽
    Leak,        // 쉬익 · 바람 · 귀 먹먹 — 어딘가 샌다
    SmokeSmell,  // 타는 냄새 (어딘지 모름)
    BadAir,      // 공기가 이상하다 (숨참 · 두통 · 매운 냄새)
    Water,       // 물이 고였다
    Spark,       // 불꽃 (합선)
    StuckDoor,   // 안 열리는 문 (대상 = 문)
    Help,        // 비명 · 도와 달라
    Cold,
    Hot,
}

public enum BeliefSource : byte { Saw, Heard, Smelled, Felt, Told, Screen }

/// <summary>믿음 하나.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Belief
{
    public BeliefKind Kind;
    public BeliefSource Src;
    public int Cell;
    public int Subject;   // 사람 · 장치 · 문 (-1)
    public float Conf;    // 확신 0~1
    public long Seen;     // 마지막으로 확인한 틱
    public int Cause;
    public float Size;    // 크기 (구멍 넓이 · 소리 dB …)
}

/// <summary>목표 — 점수로 고른다 (도망 · 공황은 본능).</summary>
public enum GoalKind : byte
{
    Idle, Flee, Panic, Down,
    Sleep, Eat, Drink, Work, Relax, Socialize,
    FightFire, Rescue, Patch, Repair, Investigate, GetSuit, Charge,
    Evacuate,   // 위험한 방을 나와 문을 닫는다 (구멍 · 새는 소리 · 연기 · 불 · 이상한 공기)
    Brace,      // 끼익거리는 문 · 벽을 덧대 받친다
    Craft,      // ⑪ 작업대에서 물건을 만든다
    Maintain,   // ⑪ 닳은 부품을 미리 간다
    Clean,      // ⑪ 곰팡이를 닦아 낸다
}

/// <summary>계획의 한 단계.</summary>
public enum StepKind : byte
{
    None,
    GoTo,      // 칸으로 (Arg: 가까이 몇 칸이면 됨)
    Take,      // 설비에서 물건을 꺼낸다 (Target: 설비 · Arg: 물건)
    Use,       // 몇 틱 동안 무엇을 한다 (Arg: 하는 일 · Value: 틱)
    Spray,     // 불에 물을 뿌린다 (Cell: 불)
    Seal,      // 구멍을 막는다 (Cell: 벽)
    Fix,       // 장치를 고친다 (Target: 장치 · Arg: 부품)
    PickUp,    // 쓰러진 사람을 업는다 (Target: 사람)
    Drop,      // 내려놓는다
    Wear,      // 우주복 · 마스크를 입는다 (Arg: 물건)
    Look,      // 살펴본다 (그 칸 믿음을 확인)
    Brace,     // 문 · 벽을 덧대 받친다 (Cell: 문 · 벽)
    Craft,     // 만든다 (Target: 작업대 설비 · Arg: 물건)
    Maintain,  // 부품을 미리 간다 (Target: 장치 · Arg: 부품)
    Clean,     // 닦는다 (Cell)
}

/// <summary>쓰기 단계가 하는 일.</summary>
public enum UseKind : byte { None, Eat, Drink, Sleep, Work, Relax, Talk, Charge }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlanStep
{
    public StepKind Kind;
    public byte Arg;
    public int Cell;
    public int Target;
    public float Value;
}

/// <summary>사람 · 로봇 · 드론 하나의 바탕 값 (찍기는 그대로).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AgentCore
{
    public AgentKind Kind;
    public byte Role;          // 맡은 일 번호 (표 순서 · 255: 없음)
    public byte Up;            // 1: 움직일 수 있다 (사람: 깨어 있고 의식 있음 · 로봇: 멀쩡)
    public byte Mode;          // 0 보통 · 1 도망 · 2 공황
    public int Name;           // 이름 번호
    public int Body;           // ⑦ 생명의 몸 (-1: 로봇)
    public int Cell;
    public int Bed;            // 자기 침대 칸 (-1)
    public int Station;        // 일하는 설비 (-1)
    public int Dock;           // 로봇 충전 칸 (-1)
    public int ShiftHour;      // 근무 시작 시각 (0~23)

    // 욕구 0~1 (1: 급함)
    public float Hunger, Thirst, Sleepy, Lonely, Bored;
    // 감정 0~1
    public float Fear, Sad, Anger, Joy, Stress;
    // 성격 0~1
    public float Brave, Calm, Kindness, Diligent, Sociable, Curious;
    // 솜씨 0~1
    public float SkRepair, SkMedic, SkFire, SkSeal, SkCook, SkPilot, SkFarm, SkScience;
    // 로봇
    public float Battery, Damage;

    // 지금 목표 · 계획
    public GoalKind Goal;
    public int GoalTarget, GoalCell, GoalCause;
    public float GoalScore;
    public long GoalSince, ModeUntil;
    public int StepIndex;
    public long StepStart;
    public int Failed;         // 막혀서 다시 계획한 횟수 (같은 목표)

    // 손 · 등
    public byte Holding;       // 든 물건 (255: 없음)
    public float HoldCharge;   // 소화기 물 · 마스크 산소 …
    public int Carrying;       // 업은 사람 (-1)
    public int CarriedBy;      // 나를 업은 이 (-1)

    // 걷기
    public float MoveBudget;
    public int WaitDoor, WaitTicks;
    public int OpenedDoor;     // 내가 연 문 (-1) — 지나가면 다시 닫는다
    public byte CloseHard;     // 1: 공기 때문에 도망 중 — 지나자마자 닫는다
    public float Speed;        // 지난 틱 빠르기 (m/s — 화면용)

    // 주컴퓨터가 맡긴 일 (그 목표 점수에 +3)
    public GoalKind AssignedGoal;
    public int AssignedArg;     // 맡긴 일의 덧붙임 (만들 물건)
    public int AssignedTarget;
    public long AssignedUntil;

    // 감각
    public int HeardUpTo;      // 처리한 소리 사건 번호
    public float LastKPa, LastCOHb;
    public long LastShout;
    public long WokeAt, AteAt, DrankAt, SleptAt;
}

/// <summary>바닥에 떨어진 · 놓인 물건.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LooseItem
{
    public byte Item;
    public int Cell;
    public float Charge;
}

/// <summary>한동안 다시 고르지 않을 목표 (막혀서 실패한 것).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct AvoidEntry
{
    public GoalKind Goal;
    public int Target;
    public long Until;
}

/// <summary>직접 가서 없다고 확인한 것 — 한동안 말 · 방송으로 들어도 반신반의한다.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Doubt
{
    public BeliefKind Kind;
    public int Cell;
    public long Until;
}

/// <summary>사람 · 로봇 하나 — 바탕 값 + 믿음 · 계획 · 길.</summary>
public sealed class Agent
{
    public int Id;
    public AgentCore C;
    public List<Belief> Beliefs = new();
    public List<PlanStep> Plan = new();
    public List<int> Path = new();
    public List<AvoidEntry> Avoid = new();
    public List<Doubt> Doubts = new();
    public int PathIdx;
    public bool IsPerson => C.Kind == AgentKind.Person;
}
