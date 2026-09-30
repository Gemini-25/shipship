using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

public enum CrewRole { Engineer, Medic, Pilot, Technician, Botanist, Electrician }

public static class CrewRoles
{
    public static string Name(CrewRole r) => r switch
    {
        CrewRole.Engineer => "기관장",
        CrewRole.Medic => "의무관",
        CrewRole.Pilot => "항법사",
        CrewRole.Technician => "정비사",
        CrewRole.Botanist => "재배·조리",
        CrewRole.Electrician => "전기 기사",
        _ => r.ToString(),
    };

    /// <summary>이 역할이 "내 일"이라고 느끼는 작업인지.</summary>
    public static bool Owns(CrewRole r, WorkOrder o) => r switch
    {
        CrewRole.Engineer => o.Skill == Skill.Engineering || o.Kind == WorkKind.ResetBreaker,
        CrewRole.Technician => o.Skill == Skill.Mechanics || o.Kind is WorkKind.RepairRobot or WorkKind.ServiceRobot or WorkKind.StockCache or WorkKind.SuitCheck or WorkKind.Drill,
        CrewRole.Electrician => o.Skill == Skill.Electrical || o.Kind == WorkKind.ResetBreaker,
        CrewRole.Botanist => o.Kind is WorkKind.Harvest or WorkKind.Tend or WorkKind.Cook or WorkKind.Restock,
        CrewRole.Medic => o.Skill == Skill.Medicine || o.Kind == WorkKind.Rescue,
        CrewRole.Pilot => o.Skill == Skill.Piloting,
        _ => false,
    };
}

/// <summary>화면에 어떤 자세로 그릴지 + 욕구 변화율을 정하는 상태.</summary>
public enum Pose { Standing, Walking, Sitting, Sleeping, Working, Down }

public sealed class Personality
{
    /// <summary>성실성. 높을수록 근무·작업을 우선한다.</summary>
    public float Diligence { get; init; } = 0.5f;

    /// <summary>사교성. 높을수록 사람을 찾고 대화로 기운을 얻는다.</summary>
    public float Sociability { get; init; } = 0.5f;

    /// <summary>용기. 낮을수록 위험한 곳을 크게 돌아가거나 피한다.</summary>
    public float Bravery { get; init; } = 0.5f;

    /// <summary>식욕. 배고픔이 차오르는 속도 배율.</summary>
    public float Appetite { get; init; } = 1f;

    /// <summary>
    /// 침착함 (v7). 높을수록 위기에서 판단 실수가 적다 (소화기를 엉뚱한 데 뿌리기, 급한 수리에서 손이 떨리기, 결정을 오래 끌기).
    /// 타고나는 성격이지만 고정은 아니다: 사고를 넘길 때마다 조금씩 오르고, 큰일을 겪으면 흔들린다.
    /// </summary>
    public float Calm { get; set; } = 0.5f;

    public string Summary()
    {
        var parts = new List<string>();
        if (Diligence >= 0.7f) parts.Add("성실함");
        else if (Diligence <= 0.35f) parts.Add("느긋함");
        if (Sociability >= 0.7f) parts.Add("사교적");
        else if (Sociability <= 0.35f) parts.Add("혼자가 편함");
        if (Bravery >= 0.75f) parts.Add("대담함");
        else if (Bravery <= 0.4f) parts.Add("겁이 많음");
        if (Calm >= 0.7f) parts.Add("침착함");
        else if (Calm <= 0.35f) parts.Add("쉽게 당황함");
        if (Appetite >= 1.15f) parts.Add("대식가");
        return parts.Count > 0 ? string.Join(" · ", parts) : "무난함";
    }
}

public sealed class Schedule
{
    public float SleepStart { get; init; }
    public float SleepLength { get; init; } = 8f;
    public float WorkStart { get; init; }
    public float WorkLength { get; init; } = 9f;

    public float WakeHour => SimTime.Wrap(SleepStart + SleepLength);

    public static Schedule FromBedtime(float sleepStart) => new()
    {
        SleepStart = sleepStart,
        WorkStart = SimTime.Wrap(sleepStart + 8f + 1f),
    };
}

/// <summary>몸 상태. 욕구와 달리 환경에 직접 반응한다.</summary>
public sealed class Vitals
{
    /// <summary>체력 0~1. 저산소·고CO2·굶주림에 깎인다.</summary>
    public float Health { get; set; } = 1f;

    /// <summary>혈중 산소 0~1. 방의 산소 분압을 따라간다.</summary>
    public float Oxygen { get; set; } = 1f;

    /// <summary>마지막으로 치료받은 틱.</summary>
    public long TreatedTick { get; set; } = -1_000_000;

    /// <summary>
    /// 부상 0~1. 체력과 달리 며칠에 걸쳐 천천히 낫는다. 최대 체력을 낮추고, 움직임과 손기술을 둔하게 한다.
    /// 파편·화상·감압이 남기고, 치료와 의무실 침대가 빨리 낫게 한다.
    /// </summary>
    public float Injury { get; set; }

    /// <summary>부상 때문에 체력이 이 이상 오르지 않는다.</summary>
    public float MaxHealth => 1f - 0.6f * Injury;

    /// <summary>부상이 어디서 왔는지 (기록용).</summary>
    public string? InjuryCause { get; set; }
}

/// <summary>입고 있는 우주복.</summary>
public sealed class SuitState
{
    public const float TankHours = 3f;

    /// <summary>남은 산소 (시간).</summary>
    public float Oxygen { get; set; } = TankHours;

    /// <summary>v11.0: 점검을 오래 안 한 보관함의 우주복 — 밸브가 새서 산소가 더 빨리 준다 (1이면 멀쩡).</summary>
    public float Leak { get; set; } = 1f;
}

public sealed class CrewStats
{
    public int Meals;
    public long TicksAsleep;
    public long TicksWorking;
    public long TicksRelaxing;
    public long TicksStarving;
    public int JobsFailed;
    public int Repairs;
    public int Services;
    public int Harvests;
    public int MealsCooked;
    public int Chats;
    public int Emergencies;
    public int Rescues;
    public int TimesDown;
    public int Panics;
}

public sealed class CrewMember
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public CrewRole Role { get; init; }
    public Personality Traits { get; init; } = new();
    public Schedule Schedule { get; init; } = new();

    /// <summary>당직을 서는 방 종류.</summary>
    public IReadOnlyList<RoomType> Stations { get; init; } = new List<RoomType>();

    /// <summary>기술 수준 0~1 (Skill 순서). 쓰면 조금씩 는다.</summary>
    public float[] SkillLevels { get; init; } = new float[Skills.All.Length];

    // ── 위치 ──
    public Vector2 Position { get; set; }
    public Vector2 PreviousPosition { get; set; }
    public Vector2 Facing { get; set; } = new(0, 1);
    public Cell Cell => Cell.FromPosition(Position);
    public Room? Room { get; internal set; }

    // ── 상태 ──
    public Needs Needs { get; } = new();

    /// <summary>겪은 것이 남긴 것 (v7): 방마다의 공포, 긴장(스트레스 기저치), 함께 넘긴 사고, 개인 이력.</summary>
    public CrewMemory Memory { get; init; } = new(0);
    public Vitals Vitals { get; } = new();
    public Pose Pose { get; set; } = Pose.Standing;
    public Furniture? Bed { get; set; }

    /// <summary>원래 침대 (임시 침실로 옮겨 가 있을 때).</summary>
    public Furniture? HomeBed { get; set; }
    public ItemStack? Carrying { get; set; }

    /// <summary>v11.2: 들고 있는 식사 중 균이 든 것 (나르면 따라가고, 먹으면 앓는다).</summary>
    public int CarryTaint { get; set; }

    /// <summary>v11.2: 균이 든 식사를 먹었다 — 이 틱에 탈이 난다 (-1이면 없음).</summary>
    public long PoisonAt { get; set; } = -1;
    public Furniture? PoisonSource { get; set; }

    /// <summary>
    /// 공구 가방: 여러 재료가 드는 일(부품 제작, Mk.1 대체품, 패널 교체)의 두 번째 이후 재료.
    /// 손에 든 것(Carrying)은 주재료 하나뿐이라, 나머지는 여기에 담아 다닌다. 일이 끝나면 남은 건 꺼낸 곳에 되돌린다.
    /// </summary>
    public List<(Furniture from, ItemKind kind, int count)> Kit { get; } = new();

    public int KitCount(ItemKind k) => Kit.Where(x => x.kind == k).Sum(x => x.count);

    /// <summary>가방에서 n개를 쓴다. 모자라면 false (아무것도 안 씀).</summary>
    public bool UseKit(ItemKind k, int n)
    {
        if (KitCount(k) < n) return false;
        for (int i = Kit.Count - 1; i >= 0 && n > 0; i--)
        {
            if (Kit[i].kind != k) continue;
            int take = Math.Min(n, Kit[i].count);
            n -= take;
            if (Kit[i].count == take) Kit.RemoveAt(i);
            else Kit[i] = (Kit[i].from, k, Kit[i].count - take);
        }
        return true;
    }

    /// <summary>입고 있는 우주복 (없으면 null).</summary>
    public SuitState? Suit { get; set; }

    /// <summary>쓰러져서 스스로 움직이지 못한다. 누군가 옮겨 줘야 한다.</summary>
    public bool Down { get; internal set; }

    /// <summary>죽었다 (World.CrewCanDie가 켜져 있을 때만).</summary>
    public bool Dead { get; internal set; }

    /// <summary>나를 업고 가는 사람.</summary>
    public CrewMember? CarriedBy { get; internal set; }

    /// <summary>내가 업고 가는 사람.</summary>
    public CrewMember? CarryingPerson { get; internal set; }

    /// <summary>숨을 참고 진공을 가로질러 우주복을 가지러 가는 중 (길찾기가 진공을 허용한다).</summary>
    public bool Dashing { get; internal set; }

    /// <summary>선체 밖 작업(EVA)에 나선 중: 길찾기가 외부 해치와 선체 밖 칸을 허용한다 (우주복을 입었을 때만).</summary>
    public bool EvaMode { get; internal set; }

    /// <summary>선체 밖(우주)에 있다. World가 매 틱 갱신한다.</summary>
    public bool Outside { get; internal set; }

    /// <summary>선체 밖에서 보낸 시간 (누적, 시간).</summary>
    public float EvaHours { get; internal set; }

    /// <summary>쓰러져서 안전한 방 바닥에 눕혀 두었다 (치료 침대가 모자랄 때).</summary>
    public bool LaidSafe { get; internal set; }

    /// <summary>쓰러진 채 눕혀진 치료 침대.</summary>
    public Furniture? CareBed { get; internal set; }

    /// <summary>스스로 판단하고 움직일 수 있는지.</summary>
    public bool CanAct => !Dead && !Down;

    /// <summary>다른 승무원에 대한 호감 -1~1 (Id로 찾음).</summary>
    public Dictionary<int, float> Affinity { get; } = new();

    /// <summary>지금 이야기 나누는 상대 (화면 표시용).</summary>
    public CrewMember? TalkingTo { get; set; }

    // ── 행동 ──
    public Job? Job { get; private set; }
    public string? JobReason { get; private set; }
    public string? LastActivityId { get; private set; }

    public List<Cell>? Path { get; set; }

    /// <summary>v10.3: 가던 길이 막혀 다시 찾지 못했다 (이동 단계가 실패한다).</summary>
    public bool PathBlocked { get; set; }
    public int PathIndex { get; set; }
    public Cell? Destination { get; set; }

    // ── 판단 기록 ──
    public IReadOnlyList<Evaluation> LastEvaluations { get; internal set; } = new List<Evaluation>();
    public long LastThinkTick { get; internal set; }
    internal long NextThinkTick { get; set; }

    /// <summary>우주복 산소가 모자라 돌아가라고 마지막으로 적은 때 (같은 경고를 틱마다 적지 않게).</summary>
    internal long SuitWarnedAt { get; set; } = -1_000_000;

    /// <summary>마지막으로 경보·위험을 알아챈 틱 (화면에 느낌표).</summary>
    public long AlertedTick { get; internal set; } = -100000;

    /// <summary>지금 있는 방이 위험하다고 느끼는 중인지 (위험에 들어설 때 한 번만 인터럽트).</summary>
    internal bool InHazard { get; set; }

    public CrewStats Stats { get; } = new();

    /// <summary>v10.10: 옆에서 거드는 정비 로봇 (긴 손일이 빨라진다).</summary>
    public Robot? Helper { get; internal set; }

    /// <summary>v11.0: 비상 훈련을 받은 효과가 이때까지 남는다 (우주복을 빨리 입고, 사고 대응이 조금 빠르다).</summary>
    public long DrilledUntil { get; set; } = -1;
    public int Drills { get; set; }
    public bool Drilled(World w) => w.Tick < DrilledUntil;

    public string ActivityLabel => Job?.Label ?? "대기";
    public bool IsMoving => Path != null;
    public bool IsAwake => Pose is not (Pose.Sleeping or Pose.Down) && !Dead;

    /// <summary>지금 발휘할 수 있는 기술 (다치면 손이 둔해진다).</summary>
    public float SkillLevel(Skill s) => SkillLevels[(int)s] * (1f - 0.4f * Vitals.Injury);

    /// <summary>다치기 전의 원래 기술.</summary>
    public float RawSkill(Skill s) => SkillLevels[(int)s];

    public void Practice(Skill s, float amount)
    {
        ref float v = ref SkillLevels[(int)s];
        v = MathF.Min(1f, v + amount * (1.05f - v));
    }

    public float AffinityTo(CrewMember other) => Affinity.TryGetValue(other.Id, out var a) ? a : 0f;

    public void ChangeAffinity(CrewMember other, float delta) =>
        Affinity[other.Id] = Math.Clamp(AffinityTo(other) + delta, -1f, 1f);

    /// <summary>
    /// 길 고르는 성향. 겁 많을수록 위험 비용을 크게 느끼고,
    /// 급한 작업 중인 성실한 사람은 덜 느낀다.
    /// </summary>
    public PathProfile PathProfile
    {
        get
        {
            float scale = 1.8f - 0.8f * Traits.Bravery;
            bool urgent = Job?.Urgent == true;
            if (urgent) scale *= 1f - 0.3f * Traits.Diligence;
            // 격리된 구획 안의 일을 맡았으면 잠긴 격벽을 비상 개방할 수 있다 (작업 계획도 그렇게 짰다)
            bool responder = urgent || Job?.Order?.Target.CurrentRoom?.Lockdown == true;
            // 무서운 방은 돌아서 간다 (v7: 감압·화재·쓰러짐을 겪은 방)
            bool suited = Suit is { Oxygen: > 0.1f };
            return new PathProfile(scale, suited || Dashing, responder, Memory.AnyFear ? Memory.Fear : null, suited && (EvaMode || Outside));
        }
    }

    /// <summary>다음 틱에 바로 다시 생각하게 한다 (긴급 인터럽트).</summary>
    public void Interrupt(World world)
    {
        NextThinkTick = world.Tick + 1;
        AlertedTick = world.Tick;
    }

    internal void StartJob(Job job, World world, Evaluation? why)
    {
        Job = job;
        JobReason = why?.Reason;
        if (job.LogText != null && (job.Activity?.Id != LastActivityId || job.AlwaysLog))
            world.Log.Add(world.Tick, job.LogKind, job.LogText, Id);
    }

    internal void EndJob(World world, ToilStatus status)
    {
        Dashing = false;
        EvaMode = false; // 밖에 있으면 Outside로 돌아올 길은 열려 있다
        // 업고 가던 사람이 있으면 그 자리에 내려놓는다
        if (CarryingPerson is CrewMember patient)
        {
            patient.CarriedBy = null;
            patient.Position = Position;
            patient.PreviousPosition = Position;
            CarryingPerson = null;
        }
        // 가방에 남은 재료는 꺼낸 곳에 되돌린다 (일을 못 끝냈거나 덜 썼을 때)
        foreach (var (from, kind, count) in Kit)
        {
            int back = from.Storage?.Add(kind, count) ?? 0;
            if (back < count)
                foreach (var box in world.Ship.Containers)
                {
                    back += box.Storage!.Add(kind, count - back);
                    if (back >= count) break;
                }
        }
        Kit.Clear();
        Helper = null;
        if (Job == null) return;
        var job = Job;
        job.Release(this, world);
        LastActivityId = job.Activity?.Id;
        Job = null;
        JobReason = null;
        Path = null;
        Destination = null;
        TalkingTo = null;
        if (Pose is Pose.Walking or Pose.Working or Pose.Sitting or Pose.Sleeping) Pose = Pose.Standing;
        if (status == ToilStatus.Failed) Stats.JobsFailed++;
        job.OnFinished?.Invoke(this, world, status);
    }

    public IEnumerable<(CrewMember who, float value)> Relations(World world) =>
        world.Crew.Where(o => o != this).Select(o => (o, AffinityTo(o))).OrderByDescending(x => x.Item2);
}
