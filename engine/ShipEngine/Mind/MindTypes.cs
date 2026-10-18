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
    Grind,       // 기계 소리가 이상하다 — 끽끽 · 덜컹 (대상 = 장치 · 들어서 안다)
    Burn,        // 곧 점화한다 — 붙잡아야 한다 (대상 = 점화 틱 · 방송으로 안다)
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
    Harvest,    // ⑪ 다 자란 작물을 거둬 식량 창고(냉장고 · 배식기)에 넣는다
    Exercise,   // ⑧ 운동 (달리기틀 — 없으면 침대 곁 맨몸 운동)
    Brawl,      // ⑨ 몸싸움 (그 자리에서 — 사회가 주먹을 셈한다)
    Restock,    // ⑪ 식량 창고(포장 식량)에서 주방(냉장고 · 배식기)으로 식량 상자를 나른다
    Cook,       // ⑧ 조리대에서 밥을 짓고(켜고 · 끄고) 먹는다
    Pilfer,     // ⑪ 배급 중 몫을 다 먹고 — 재지 않는 식량 보관함에서 몰래 한 끼
    Treat,      // ⑦ 다친 이를 돌본다 (구급 상자 — 없으면 맨손)
    Rewire,     // ⑥ 급전선이 죽은 방에 옆 방에서 연장선을 끌어온다 (대상: 방)
    Shelter,    // ⑩ 대피 방송 — 주컴퓨터가 고른 방으로 모인다 (대상: 방)
    Meeting,    // ⑨ 회의 — 식당으로 모인다 (대상: 방)
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
    Harvest,   // 작물을 거둔다 (Target: 작물 번호) — 손에 수확물을 든다
    Stow,      // 든 수확물 · 식량 상자를 식량 창고에 넣는다 (Target: 설비)
    TakeFood,  // 식량 보관함에서 식량 상자를 꺼낸다 (Target: 설비)
    Salvage,   // ⑥ 덜 중요한 장치에서 부품을 떼어 낸다 (Target: 떼어 올 장치 · Arg: 부품 · Cell: 고칠 장치)
    Rewire,    // ⑥ 연장선: Arg 0 옆 방 통풍구에서 꽂는다 · 1 그 방까지 깔고 잇는다 (Target: 방 · Cell: 옆 방)
}

/// <summary>쓰기 단계가 하는 일.</summary>
public enum UseKind : byte { None, Eat, Drink, Sleep, Work, Relax, Talk, Charge, Exercise, Cook, Pilfer, Treat }

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
    public byte Mode;          // 0 보통 · 1 도망 · 2 공황 · 3 숨을 아낀다 (갈 곳 없는 나쁜 공기)
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
    /// <summary>⑧ 성격 다섯 축 (Big Five · 0~1 — 사람 무리에서 평균 0.5 · 표준 편차 0.17의 정규 분포): 열림 · 성실 · 외향 · 친화 · 신경성. 위 여섯 버릇은 여기서 나온다.</summary>
    public float Open, Consc, Extra, Agree, Neuro;
    public float Unfit;        // ⑧ 운동하고 싶은 몸 (깨어 있는 하루면 1)
    public long ExercisedAt;
    // 솜씨 0~1
    public float SkRepair, SkMedic, SkFire, SkSeal, SkCook, SkPilot, SkFarm, SkScience;
    public byte Spares;        // ⑥ 로봇의 부품 주머니 (새 부품 · 고칠 때마다 하나)
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
    // ⑧ 요리 · 배우기
    public int CookOn;         // 지금 쓰는 조리대 (장치 번호 + 1 · 0: 없음)
    public float Careful;      // 실수에서 배운 조심 (0~1 · 이레에 반쯤 흐려진다)
    // ⑧ 하루의 리듬 · 쉼
    public float Chrono;       // 몸의 시계 (시간 — 음수 아침형 · 양수 저녁형)
    public byte Hobby;         // 쉬는 시간에 하는 일 (Hobby)
    public byte Aim, AimMet;   // ⑧ 인생 목표 (Aim) · 이뤘나 (1)
    public float LessonFire, LessonBreach, LessonAir;   // ⑧ 사고의 교훈 (0~1 · 반감기 3주)
    // ⑨ 함께
    public long FollowedAt;    // 아는 이를 따라 방을 나선 때 (0: 없음) — 10분 동안은 다시 따라 나서지 않는다
    public float SleepNoise;   // ⑦ 자는 동안 들은 가장 큰 소리 (dB — 10분에 걸쳐 흐려진다)
    public float SleepLoss;    // ⑦ 잠이 얕은 정도 (0 깊다 · 1 거의 못 잔다 — 1 − 잠의 질)
    public float Weary;        // ⑧ 고립의 피로 (0~1 — 닫힌 배의 같은 얼굴 · 같은 일과에 몇 주에 걸쳐 쌓인다)
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

/// <summary>이상한 기계 소리를 알렸다 (사람이 배 안 통신으로 — 일감판(⑪)이 읽어 예방 정비 일감을 올린다).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrindReport
{
    public long Tick;
    public int Device;
    public int Cause;    // '○○이 ○○ 소리가 이상하다고 알렸다' (부모: 끽끽거린다)
    public int Who;
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
    public string LastFail = "";   // 관찰용: 마지막으로 그만둔 까닭 (찍지 않는다)
    public bool IsPerson => C.Kind == AgentKind.Person;
}
