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
    GravityBack, // 곧 중력이 돌아온다 — 바닥 쪽을 잡는다 (방송으로 안다)
    LeftOn,      // 켜 둔 채 곁에 쓰는 이가 없는 조리대 · 오븐 (대상 = 장치 · 보고 안다 — 깊이 고침 DF5)
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
    Improvise,  // ⑧ 판단 엔진 — 제 궁리를 한다 (막혀서 지어낸 몸 걸음 · 대상: 막으려던 구멍)
    Cpr,        // ⑦ 멎은 심장 — 곁에서 가슴을 누르고 · 제세동기를 가져와 친다 (대상: 몸)
    Surgery,    // ⑦ (라) 수술 — 치료 침대에서 마취하고 속 출혈을 멈추거나 맹장을 떼어 낸다 (대상: 몸)
    Attack,     // ⑧ 공황 발작 — 그 자리에 주저앉아 숨을 고른다
    Withdraw,   // ⑧ 우울 — 일하는 시간에 제 침대에 틀어박힌다
    Counsel,    // ⑧ 상담 — 주컴퓨터의 부탁으로 마음이 힘든 이 곁에 앉아 이야기를 들어 준다 (대상: 몸)
    Haul,       // ⑪ 공급 사슬 — 헌 부품을 정제기로 나른다 (맡긴 일: 대상 정제기 설비 · Arg 헌 부품이 있는 설비 × 256 + 물건)
    Tighten,    // ④ 열 · 진동 — 진동에 풀려 덜컹거리는 장치의 볼트를 조인다 (대상: 장치)
    Toilet,     // 화장실 — 변기에 비운다 (대상: 변기 설비 · 없으면 용변 봉투)
    Wash,       // 씻는다 — 샤워 칸 (대상: 샤워 칸 설비 · 없으면 물수건)
    ToiletCare, // 변기 손질 (맡긴 일 — 대상: 변기 설비 · Arg 0 닦기 · 1 뚫기 · 2 오물 통 비우기 · 3 내려놓인 통 주워 비우기 (대상: 칸))
    Stash,      // 손 비우기 · 돌려놓기 — 든 물건을 제자리(그 물건을 두는 보관함)에 넣는다 (대상: 보관함 설비)
    Refill,     // 쓴 소화기 · 우주복 · 마스크 채우기 (맡긴 일 — 대상: 보관함 설비 · Arg 물건)
    Gather,     // 바닥 물건 줍기 → 제자리 (대상: 칸 · Arg 물건)
    SwitchOff,  // 켜 둔 채 쓰는 이가 없는 조리대 · 오븐을 끈다 (대상: 장치 — DF5)
    Patrol,     // 로봇의 순회 — 제 구역의 방을 돌며 켜진 것 · 멈춘 장치 · 고인 물 · 숨진 이를 본다 (대상: 방 — DF5)
    LayOut,     // 숨진 이를 수습한다 — 덮어 쉴 곳에 뉜다 (대상: 몸 — DF5)
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
    Door,      // ⑧ 문을 연다(Arg 1) · 닫는다(Arg 0) (Cell: 문 — 제 궁리의 걸음)
    Put,       // ⑪ 든 물건을 설비에 넣는다 (Target: 설비 — 나르기)
    Rebuild,   // ⑪ 부서진 설비의 틀을 외장 틀로 새로 짠다 (Target: 장치)
    Tighten,   // ④ 풀린 볼트를 조인다 (Target: 장치)
    Sanitize,  // 변기 손질 (Target: 설비 · Arg 0 닦기 · 1 뚫기 · 2 오물 통 들기 · 3 비울 곳에 쏟기 · 4 내려놓인 통 줍기 (Cell))
    Lay,       // 든 물건을 그 자리에 내려놓는다 (급할 때 손 비우기)
    Refill,    // 든 소화기 · 우주복 · 마스크를 채운다 (Target: 채울 곳 설비 · 물탱크 물 · 모은 이산화탄소 · 방 공기의 산소)
    Gather,    // 바닥 물건을 줍는다 (Cell · Arg: 물건)
    Switch,    // 켜 둔 장치를 끈다 (Target: 장치 — DF5)
    Shroud,    // 숨진 이를 덮는다 (Target: 몸 — DF5)
}

/// <summary>쓰기 단계가 하는 일.</summary>
public enum UseKind : byte { None, Eat, Drink, Sleep, Work, Relax, Talk, Charge, Exercise, Cook, Pilfer, Treat, Cpr, Surgery, Counsel, Queue, Toilet, Shower, Hands, Bag, Wipe, Mop }

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
    public byte Spares;        // ⑥ 로봇의 부품 주머니 — 갈래마다 하나 (비트: 전기 · 기계 · 봉함 · 거르개 — ⑪ 공급 사슬 · 고칠 때마다 그 갈래를 쓴다)
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
    /// <summary>바라보는 쪽 (마지막 걸음의 칸 쪽 −1~1 — 서 있으면 그대로 · 0, 0이면 남쪽) · 지난 틱에 걸은 칸 수 (⑧ 걸음 끝에 남긴다: 다음 틱 걸음보다 먼저 쓰러지면 그 빠르기 · 쪽으로 떨군다).</summary>
    public sbyte FaceX, FaceY;
    public byte WalkCells;
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
    // ⑧ 판단 엔진 — 마지막 막힘: 까닭 · 자리(칸) · 대상(문 · 물건) · 때 · 그때의 목표 (주컴퓨터가 맡긴 로봇의 막힘을 읽는다)
    public SnagKind Snag;
    public int SnagCell, SnagSubject, SnagTarget;
    public long SnagTick;
    public GoalKind SnagGoal;
    // ⑧ 판단 엔진 — 분야를 아는 정도 (0~1 · 상식은 모두 1): 기압 · 전기 · 화학 · 몸 · 구조 (맡은 일의 시작값 ± 사람마다 · 겪고 · 배운다)
    public float KnPressure, KnElectric, KnChem, KnBody, KnStruct;
    // ⑧ 판단 엔진 (마) — 마지막 궁리: 때 · 구멍 · 고른 길(1 제 궁리 · 2 주컴퓨터 계획을 따름 · 3 주컴퓨터에 물음 · 4 모르겠다) · 제 궁리가 살아 있는 때까지
    public long PonderTick, OwnUntil;
    public int PonderTarget;
    public byte PonderWay;
    /// <summary>무중력 (바퀴 3): 1이면 지금 떠 있다 (바닥 중력 0.3g 아래) · 떠서 지낸 시간 (시간 — 익숙해질수록 손잡이를 잘 탄다).</summary>
    public byte Floated;
    public float ZeroGH;
    /// <summary>무중력에서 잘 자리 (칸 + 1 · 0 없음): 날숨이 고여 깬 뒤 고른 공조 바람이 닿는 자리 — 통풍구 앞에 침낭을 묶는다.</summary>
    public int ZeroGBed;
    /// <summary>⑦ (다) 진료 기록 (그 사람의 — 돌보는 이가 남긴다): 본 병 · 본 때(0: 본 적 없음) · 본 이 · 원인 마디 · 그때 있던 방 · 그때의 증상 · 그때 깨어 있었나.</summary>
    public Ail DxAil;
    public long DxAt;
    public int DxBy, DxCause, DxRoom;
    public Sym DxSym;
    public byte DxAwake;
    /// <summary>⑦ (라) 집도의: 이번 수술의 마취를 한 때 (목표를 잡은 때보다 앞이면 아직 안 했다).</summary>
    public long OpAnesAt;
    /// <summary>⑧ 기억 · 정신 건강 (바퀴 3 — MindHeart): 충격(갓 겪은 일) · 외상(오래 남는다) · 우울 · 불안 (0~1).</summary>
    public float Shock, Trauma, Depress, Anxiety;
    /// <summary>이번 1분 동안 가장 컸던 겁 (충격은 공포의 세기로 잰다 — 1분마다 0으로).</summary>
    public float FearMax;
    /// <summary>충격의 원인 마디 · 그 방 · 갈래 · 이번 일에서 가장 컸던 충격 · 가장 큰 한 몫 · 그때 · 마지막으로 위험에 맞선 때(불 끄기 · 구하기 · 막기) · 외상이 0.3을 넘었었나(1).</summary>
    public int ShockCause, ShockRoom;
    public ShockKind ShockKind;
    public float ShockPeak, ShockTop;
    public long ShockAt, CopedAt;
    public byte TraumaHigh;
    /// <summary>처음 성격 (겪은 일로 바뀌어도 ±0.2 안).</summary>
    public float Neuro0, Brave0, Agree0;
    /// <summary>악몽 · 공황 발작 · 되살아남 · 그 방에서 떠오름의 마지막 때 · 공황 발작이 끝나는 때.</summary>
    public long NightmareAt, AttackAt, AttackUntil, FlashAt, RoomFlashAt;
    /// <summary>⑥ 로봇 · 드론의 몸 번호 (망의 로봇 몸 — 사람은 쓰지 않는다) · 느낀 몸 (1 다리가 망가졌다 · 2 손을 못 쓴다 · 4 눈이 흐리다).</summary>
    public int Mech;
    public byte MechFelt;
    /// <summary>③ 국소 현상: 빨려 붙은 구멍 칸 + 1 (0: 없음) · 구멍 밖으로 빨려 나가 떠내려간다(1) · 떠내려가는 쪽 · 감압 돌풍에 붙잡고 버틴 때.</summary>
    public int PinAt;
    public byte Adrift;
    public sbyte AdriftDx, AdriftDy;
    public long HeldAt;
    /// <summary>소리 단계 S4: 소리 난 쪽으로 고개를 돌렸다 — 본 칸 · 언제까지 (판단 · 그림이 그 쪽을 보게 그린다).</summary>
    public int LookAt;
    public long LookUntil;
    /// <summary>소리 단계 S5: 쉬며 음악을 어떻게 듣나 (0 이어폰 · 1 작은 스피커로 튼다 · 2 악기를 친다) · 마지막으로 소리 내어 튼 때.</summary>
    public byte Playing;
    public long PlayedAt;
    /// <summary>화장실 · 샤워실 (docs/WASH.md): 마지막으로 변기에 비운 때 · 씻은 때 · 손을 씻은 때.</summary>
    public long ToiletAt, ShoweredAt, HandsAt;
    /// <summary>손 (docs/MIND.md 3단): 지금 든 물건 · 그것을 든 때 (방금 가져온 것은 한동안 돌려놓지 않는다).</summary>
    public byte HeldItem;
    public long HeldSince;
    // 죽은 충전독 (docs/MIND.md 3단 덧붙임): 6분 동안 전지가 오르지 않은 독 (독 번호마다 한 비트 · 2시간 피한다) · 그 때 · 독에서 잰 전지의 바닥 · 그때부터 잰 때
    public ulong DeadDocks;
    public long DeadDocksAt;
    public float ChargeFrom;
    public long ChargeAt;
    /// <summary>기분 (깊이 고침 DF7 — MIND 14단 '기분' 당김 · MindMood): 감정 아래 며칠 가는 층 — 밝은 몫 · 가라앉은 몫 (0~1) · 오래 높은 심심함 (세 시간에 걸친 평균) ·
    /// 지난 틱 끝의 기쁨 · 겁 · 슬픔 · 화 (갑자기 솟은 몫 — 겪은 일 — 을 알아본다).</summary>
    public float MoodGood, MoodBad, BoredLong;
    public float JoyWas, FearWas, SadWas, AngerWas;
    /// <summary>위기 뒤 (DF7): 마지막으로 안 위기 — 이야기 갈래 (불 · 구멍) · 안 때 (0: 없음) · 원인 · 칸 · 제 몸으로 겪었나 (1 본 · 들은 소리 · 냄새 / 0 말 · 방송으로만) · 그 이야기를 나눈 수.</summary>
    public Topic CrisisTopic;
    public long CrisisAt;
    public int CrisisCause, CrisisCell;
    public byte CrisisSaw, CrisisTold;
}

/// <summary>보관함에 돌려놓은 쓰다 남은 물건 (소화기 물 · 이산화탄소 · 우주복 · 마스크 산소가 덜 찼다) — 꺼낼 때 그 양으로 · 채우기 일감의 대상.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SpentItem
{
    public int Furniture;
    public byte Item;
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

/// <summary>샤워 칸 · 화장실을 알렸다 (5단 — 사람이 배 안 통신으로 · 주컴퓨터가 읽는다).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WashReport
{
    public long Tick;
    public int Where;    // 샤워 칸 장치 (갈래 1 · 2) · 방 (갈래 3)
    public int Cause;
    public int Who;
    public byte Kind;    // 1 데일 듯 뜨겁다 · 2 찬물 · 3 역한 냄새
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
    public List<PlanStep> Own = new();   // ⑧ 판단 엔진: 제 궁리로 지어낸 몸 걸음 ('제 궁리' 목표가 고르면 그대로 계획이 된다)
    public int PathIdx;
    public string LastFail = "";   // 관찰용: 마지막으로 그만둔 까닭 (찍지 않는다)
    public string LastThought = "";   // 관찰용: 마지막 궁리 (찍지 않는다)
    public long LastThoughtTick = long.MinValue / 2;   // 관찰용: 그때 (찍지 않는다)
    public Ring<Judgment>? Judged;   // 관찰용: 판단 일지 32칸 (Mind.JudgedKeep) (세계가 일지를 켰을 때 첫 쓰기에 잡는다 · 찍지 않는다 · 판단은 읽지 않는다 — 화면만)
    public bool IsPerson => C.Kind == AgentKind.Person;
}
