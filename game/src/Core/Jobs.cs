using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

public enum ToilStatus { Running, Succeeded, Failed, Interrupted }

/// <summary>
/// 작업(Job)을 이루는 한 단계. "선반으로 간다 → 부품을 꺼낸다 → 펌프로 간다 → 고친다"처럼
/// 작은 단계를 이어 붙여 행동 하나를 만든다.
/// </summary>
public abstract class Toil
{
    public virtual void Begin(CrewMember c, World w) { }
    public abstract ToilStatus Tick(CrewMember c, World w);
    public virtual void End(CrewMember c, World w) { }

    /// <summary>화면에 보여줄 진행률 (없으면 null).</summary>
    public virtual float? Progress => null;

    /// <summary>v12.9.1 이 단계에서 우주복을 입는다 (그 일은 가는 길에 공기가 나빠져도 다시 계획하지 않는다).</summary>
    public bool DonsSuit { get; init; }
}

/// <summary>목표 칸으로 걸어간다.</summary>
public sealed class GotoToil : Toil
{
    private readonly Cell _target;
    private readonly Func<CrewMember, bool>? _when;
    private bool _ok;

    /// <param name="when">false면 이 단계를 건너뛴다 (예: 손에 남은 게 있을 때만 되돌려 놓으러 감).</param>
    public GotoToil(Cell target, Func<CrewMember, bool>? when = null)
    {
        _target = target;
        _when = when;
    }

    public override void Begin(CrewMember c, World w)
    {
        if (_when != null && !_when(c))
        {
            _ok = true;
            c.Path = null;
            return;
        }
        _ok = Locomotion.SetDestination(c, w, _target);
        if (_ok && c.Path != null && c.Path.Count > 0) c.Pose = Pose.Walking;
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        if (!_ok) return ToilStatus.Failed;
        if (Locomotion.Step(c, w)) return ToilStatus.Succeeded;
        return c.PathBlocked ? ToilStatus.Failed : ToilStatus.Running;
    }

    public override void End(CrewMember c, World w)
    {
        c.Path = null;
        c.Destination = null;
        if (c.Pose == Pose.Walking) c.Pose = Pose.Standing;
    }
}

/// <summary>갈 곳을 출발할 때 정한다 (앞 단계가 끝나 봐야 알 수 있을 때). null이면 건너뛴다.</summary>
public sealed class GotoToilLate : Toil
{
    private readonly Func<CrewMember, Cell?> _pick;
    private bool _ok;

    public GotoToilLate(Func<CrewMember, Cell?> pick) => _pick = pick;

    public override void Begin(CrewMember c, World w)
    {
        if (_pick(c) is not Cell target)
        {
            _ok = true;
            c.Path = null;
            return;
        }
        _ok = Locomotion.SetDestination(c, w, target);
        if (_ok && c.Path != null && c.Path.Count > 0) c.Pose = Pose.Walking;
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        if (!_ok) return ToilStatus.Failed;
        if (Locomotion.Step(c, w)) return ToilStatus.Succeeded;
        return c.PathBlocked ? ToilStatus.Failed : ToilStatus.Running;
    }

    public override void End(CrewMember c, World w)
    {
        c.Path = null;
        c.Destination = null;
        if (c.Pose == Pose.Walking) c.Pose = Pose.Standing;
    }
}

/// <summary>자리에서 시간을 보낸다 (식사, 수면, 휴식).</summary>
public sealed class WaitToil : Toil
{
    private readonly int _maxTicks;
    private readonly int _minTicks;
    private readonly Pose _pose;
    private readonly Vector2? _faceToward;
    private int _elapsed;

    public Action<CrewMember, World>? EveryTick { get; init; }
    public Func<CrewMember, World, bool>? DoneWhen { get; init; }

    public WaitToil(int maxTicks, Pose pose, Vector2? faceToward = null, int minTicks = 0)
    {
        _maxTicks = maxTicks;
        _minTicks = minTicks;
        _pose = pose;
        _faceToward = faceToward;
    }

    public override void Begin(CrewMember c, World w)
    {
        c.Pose = _pose;
        Locomotion.Face(c, _faceToward);
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        _elapsed++;
        EveryTick?.Invoke(c, w);
        if (_elapsed >= _maxTicks) return ToilStatus.Succeeded;
        if (_elapsed >= _minTicks && DoneWhen != null && DoneWhen(c, w)) return ToilStatus.Succeeded;
        return ToilStatus.Running;
    }
}

/// <summary>
/// 기술을 쓰는 일 (정비·수리·조리·수확). 기술이 높을수록 빨리 끝나고,
/// 피곤하거나 스트레스가 높으면 느려진다. 끝나면 결과를 적용한다.
/// </summary>
public sealed class WorkToil : Toil
{
    private readonly float _baseHours;
    private readonly Skill _skill;
    private readonly Vector2? _face;
    private float _done;
    private float _needed;

    public Func<CrewMember, World, bool>? CanContinue { get; init; }

    /// <summary>
    /// 긴 작업(부품 제작, 패널 교체)은 끼니·잠·교대로 끊겨도 진척이 작업 목록에 남아, 다음 사람이 이어서 한다.
    /// </summary>
    public WorkOrder? Resume { get; init; }
    public Action<CrewMember, World>? OnBegin { get; init; }
    public Action<CrewMember, World>? OnEnd { get; init; }

    public WorkToil(float hours, Skill skill, Vector2? face)
    {
        _baseHours = hours;
        _skill = skill;
        _face = face;
    }

    public override float? Progress => _needed > 0 ? _done / _needed : 0f;

    public override void Begin(CrewMember c, World w)
    {
        c.Pose = Pose.Working;
        Locomotion.Face(c, _face);
        float skill = c.SkillLevel(_skill);
        _needed = SimTime.Hours(_baseHours) * (1.6f - 0.8f * skill);
        if (Resume != null) _done = Resume.Progress * _needed;
        OnBegin?.Invoke(c, w);
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        if (CanContinue != null && !CanContinue(c, w)) return ToilStatus.Failed;
        float speed = (1f - 0.25f * c.Vitals.Injury) * Wounds.HandFactor(c.Vitals) * (1f - 0.3f * c.Vitals.Scar); // v11.3 후유증 · v12.7 팔을 다치면 더
        if (c.Room is Room here && here.Dark && c.Suit == null) speed *= 0.8f; // v9.4 캄캄한 방 (우주복 헬멧 등이면 괜찮다)
        speed *= w.Portable.LampWorkMul(c); // v16.7 이동식 등에 기대 일한다 (멀거나 몸에 가리면 느리다)
        speed *= w.Blast.WorkMul(c); // v16.13 섬광 · 이명
        if (c.Needs.Rest < 0.2f) speed *= 0.75f;
        if (c.Needs.Stress > 0.7f) speed *= 0.8f;
        if (c.Vitals.Oxygen < 0.85f) speed *= 0.8f;
        if (w.Food.Rationing && c.Needs.Food < 0.5f) speed *= 0.94f; // v10.11 배급: 배고픈 손은 조금 느리다
        speed *= w.Society.WorkFactor; // v13.4 사기
        if (c.Habits.Count > 0) speed *= Persona.Mul(c, h => h.Speed); // v14.0 습관 (서두름·완벽주의)
        speed *= c.Fx.WorkMul; // v14.1 앓는 것
        speed *= ModulesV15.SpeedMul(c.Room, _skill); // v15 오븐 · 선반 · 납땜대 · 진단 스캐너 …
        speed *= ErasV15.Mul(w, ErasV15.Work(c.Job?.Order?.Kind)); // v15.5 시대 기술: 수리 · 제작 · 조리 · 치료
        if (_skill is Skill.Mechanics or Skill.Electrical or Skill.Engineering) speed *= c.ToolFactor; // v14.3 손에 익은 제 공구
        if (c.Job?.Urgent == true && w.Society.IsVeteran(c)) speed *= 1.1f; // v13.4 베테랑
        // v11.0: 비상 훈련을 받은 사람은 사고 대응 일이 조금 빠르다
        if (c.Job?.Urgent == true && c.Drilled(w)) speed *= 1.12f;
        speed *= w.CrisisCrew.HandsMul(c); // v16.21 곁에서 거드는 사람 (한 작업에 여러 명)
        // v10.10: 정비 로봇이 옆에서 거들면 (부품을 잡아 주고 공구를 건넨다) 빨라진다
        float co = w.Coop.WorkMul(c, Resume, _face); if (co < 0f) return ToilStatus.Failed; speed *= co; // v17.4 예약 · 옆 설비 · 펼치기 · 짝 기다림 · 이어 함 · 헷갈림 · 혼자
        if (c.Helper is Robot helper && helper.Helping == c && (helper.Position - c.Position).LengthSquared() < 2.7f * 2.7f) speed *= 1f + RobotsV15.AssistBonus(helper.Kind); // v15.7 조수 로봇은 더 거든다
        if (Resume != null && _needed > 0)
        {
            // 로봇과 같은 일을 하면 진척을 함께 쓴다 (누가 먼저 채우든 한 번만 끝난다)
            if (Resume.Closed) return ToilStatus.Succeeded;
            Resume.Progress = MathF.Min(1f, Resume.Progress + speed / _needed);
            _done = Resume.Progress * _needed;
            return Resume.Progress >= 1f ? ToilStatus.Succeeded : ToilStatus.Running;
        }
        _done += speed;
        return _done >= _needed ? ToilStatus.Succeeded : ToilStatus.Running;
    }

    public override void End(CrewMember c, World w) { OnEnd?.Invoke(c, w); w.Coop.WorkEnded(c, Progress >= 0.999f); } // v17.4 다 했나 · 끊겼나 (작업장이 남는다)
}

/// <summary>보관함에서 물건을 꺼내 손에 든다.</summary>
public sealed class TakeToil : Toil
{
    private readonly Furniture _from;
    private readonly ItemKind _kind;
    private readonly int _count;
    private readonly bool _partialOk;

    internal Furniture From => _from; // v16.17 옮긴 선반의 옛 자리 (헷갈림)
    public TakeToil(Furniture from, ItemKind kind, int count, bool partialOk = false)
    {
        _from = from;
        _kind = kind;
        _count = count;
        _partialOk = partialOk;
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        var inv = _from.Storage;
        if (inv == null) return ToilStatus.Failed;
        if (w.Coop.Dig(c, _from, _kind)) return ToilStatus.Running; // v17.4 큰 부품은 앞 상자부터 치운다
        if (c.Carrying is ItemStack held && held.Kind != _kind) return ToilStatus.Failed;
        int want = _count;
        if (!_partialOk && inv.Count(_kind) < want) return ToilStatus.Failed;
        int got = inv.Take(_kind, want);
        if (got <= 0) return ToilStatus.Failed;
        c.CarryTaint += inv.LastTainted; // v11.2 균이 든 식사는 손에 따라온다
        int already = c.Carrying?.Count ?? 0;
        c.Carrying = new ItemStack(_kind, already + got);
        return ToilStatus.Succeeded;
    }
}

/// <summary>보관함에서 공구 가방으로 꺼낸다 (여러 재료가 드는 일의 부재료).</summary>
public sealed class TakeKitToil : Toil
{
    private readonly Furniture _from;
    private readonly ItemKind _kind;
    private readonly int _count;

    internal Furniture From => _from; // v16.17
    public TakeKitToil(Furniture from, ItemKind kind, int count)
    {
        _from = from;
        _kind = kind;
        _count = count;
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        var inv = _from.Storage;
        if (inv == null || inv.Count(_kind) < _count) return ToilStatus.Failed;
        inv.Take(_kind, _count);
        c.Kit.Add((_from, _kind, _count));
        return ToilStatus.Succeeded;
    }
}

/// <summary>손에 든 물건을 보관함에 넣는다. 다 못 넣으면 남은 건 계속 든다.</summary>
public sealed class PutToil : Toil
{
    private readonly Furniture _into;
    public PutToil(Furniture into) => _into = into;
    internal Furniture Into => _into; // v16.17

    public override ToilStatus Tick(CrewMember c, World w)
    {
        if (c.Carrying is not ItemStack held || _into.Storage == null) return ToilStatus.Succeeded;
        int put = _into.Storage.Add(held.Kind, held.Count);
        if (held.Kind == ItemKind.Meal && c.CarryTaint > 0)
        {
            int t = Math.Min(put, c.CarryTaint);
            _into.Storage.Taint(t);
            c.CarryTaint -= t;
        }
        int left = held.Count - put;
        c.Carrying = left > 0 ? new ItemStack(held.Kind, left) : null;
        return ToilStatus.Succeeded;
    }
}

/// <summary>한 번에 끝나는 동작. false를 돌려주면 작업 실패.</summary>
public sealed class DoToil : Toil
{
    private readonly Func<CrewMember, World, bool> _action;
    public DoToil(Func<CrewMember, World, bool> action) => _action = action;
    public override ToilStatus Tick(CrewMember c, World w) => _action(c, w) ? ToilStatus.Succeeded : ToilStatus.Failed;
}

/// <summary>승무원이 지금 하고 있는 일. 여러 Toil을 순서대로 실행한다.</summary>
public sealed class Job
{
    private readonly List<Toil> _toils;
    private int _index = -1;

    public Activity? Activity { get; }
    public string Label { get; }

    /// <summary>시작할 때 사건 기록에 남길 문장 (이름은 화면에서 붙인다).</summary>
    public string? LogText { get; init; }
    public LogKind LogKind { get; init; } = LogKind.Life;

    /// <summary>같은 활동이 이어져도 매번 기록한다 (정비·수리처럼 대상이 다를 때).</summary>
    public bool AlwaysLog { get; init; }

    public Room? TargetRoom { get; init; }
    public Furniture? Target { get; init; }

    /// <summary>작업 목록에서 맡은 일.</summary>
    public WorkOrder? Order { get; init; }

    /// <summary>긴급한 일 (위험 비용을 덜 느낌).</summary>
    public bool Urgent { get; init; }

    /// <summary>v12.9.1 남은 단계 중에 우주복을 입는 단계가 있다.</summary>
    public bool WillDonSuit
    {
        get
        {
            for (int i = System.Math.Max(0, _index); i < _toils.Count; i++)
                if (_toils[i].DonsSuit) return true;
            return false;
        }
    }

    /// <summary>다른 행동이 이만큼 더 급해야 이 일을 중단한다.</summary>
    public float InterruptMargin { get; init; } = 0.15f;

    public Action<CrewMember, World, ToilStatus>? OnFinished { get; init; }

    public List<Furniture> Reservations { get; } = new();

    public Job(Activity? activity, string label, IEnumerable<Toil> toils)
    {
        Activity = activity;
        Label = label;
        _toils = new List<Toil>(toils);
    }

    public Toil? Current => _index >= 0 && _index < _toils.Count ? _toils[_index] : null;
    internal IReadOnlyList<Toil> Toils => _toils; // v16.17 이 일이 손댈 보관함 (옛 자리 습관)

    /// <summary>뒤에 단계를 덧붙인다 (예: EVA를 마치고 에어락으로 돌아온다).</summary>
    internal void Append(IEnumerable<Toil> toils)
    {
        if (_index >= 0) return;
        _toils.AddRange(toils);
    }

    /// <summary>시작 전에 앞에 단계를 끼워 넣는다 (예: 우주복부터 입고 간다).</summary>
    internal void Prepend(IEnumerable<Toil> toils)
    {
        if (_index >= 0) return;
        _toils.InsertRange(0, toils);
    }

    public Job Reserve(Furniture f, CrewMember c)
    {
        f.ReservedBy = c;
        Reservations.Add(f);
        return this;
    }

    internal ToilStatus Tick(CrewMember c, World w)
    {
        if (_index < 0)
        {
            if (_toils.Count == 0) return ToilStatus.Succeeded;
            _index = 0;
            _toils[0].Begin(c, w);
        }

        // 한 틱에 즉시 끝나는 단계(꺼내기·넣기)는 이어서 처리
        for (int guard = 0; guard < 8; guard++)
        {
            var status = _toils[_index].Tick(c, w);
            if (_index >= _toils.Count) return status == ToilStatus.Running ? ToilStatus.Interrupted : status; // v16.15 단계 안에서 일이 이미 끝났다 (예: 잠결 장면이 스스로 깨워 EndJob) — 풀린 단계를 다시 만지지 않는다
            // 단계가 제 일을 안에서 끝냈으면 (Release 가 이미 정리했다) 더 읽지 않는다 — 새 일을 잡았으면 그 일은 건드리지 않게
            if (_index >= _toils.Count) return c.Job != null && c.Job != this ? ToilStatus.Running : status == ToilStatus.Running ? ToilStatus.Interrupted : status;
            if (status == ToilStatus.Running) return ToilStatus.Running;

            _toils[_index].End(c, w);
            if (status != ToilStatus.Succeeded) { _index = _toils.Count; return status; }

            _index++;
            if (_index >= _toils.Count) return ToilStatus.Succeeded;
            _toils[_index].Begin(c, w);
            if (_toils[_index] is GotoToil or GotoToilLate or WaitToil or WorkToil) return ToilStatus.Running;
        }
        return ToilStatus.Running;
    }

    internal void Release(CrewMember c, World w)
    {
        if (_index >= 0 && _index < _toils.Count)
        {
            _toils[_index].End(c, w);
            _index = _toils.Count;
        }
        foreach (var f in Reservations)
            if (f.ReservedBy == c) f.ReservedBy = null;
        Reservations.Clear();
    }
}

/// <summary>경로를 따라 실제로 걷는 부분.</summary>
public static class Locomotion
{
    /// <summary>1틱에 움직이는 칸 수 (1배속 기준 초당 3칸).</summary>
    public const float BaseSpeed = 0.1f;

    public static bool SetDestination(CrewMember c, World w, Cell goal)
    {
        c.PathBlocked = false;
        var path = w.Paths.Find(c.Cell, goal, c.PathProfile);
        if (path == null) return false;
        c.Path = path;
        c.PathIndex = 0;
        c.Destination = goal;
        return true;
    }

    /// <summary>
    /// v10.3: 이 칸이 이제 지나갈 수 없다 (칸막이·새 가구·떨어져 나간 방이 길을 막았다). 벽은 늘, 가구는 올라설 수 없는 것만.
    /// 문·빈 바닥·우주(선체 밖 길)는 길찾기가 따로 따진다.
    /// </summary>
    public static bool Blocked(Ship ship, Cell cell) =>
        ship.Grid.Kind(cell) == TileKind.Wall
        || (ship.FurnitureAt(cell) is Furniture f && !FurnitureTypes.Walkable(f.Type));

    public static void Face(CrewMember c, Vector2? target)
    {
        if (target is not Vector2 t) return;
        var d = t - c.Position;
        if (d.LengthSquared() > 0.0001f) c.Facing = Vector2.Normalize(d);
    }

    public static float Speed(CrewMember c)
    {
        float s = BaseSpeed;
        if (c.Needs.Rest < 0.15f) s *= 0.7f;
        if (c.Needs.Food < 0.1f) s *= 0.8f;
        if (c.Vitals.Oxygen < 0.85f) s *= 0.8f;
        if (c.Vitals.Health < 0.4f) s *= 0.75f;
        if (c.Carrying != null) s *= 0.9f;
        if (c.CarryingPerson != null) s *= 0.6f * CarryGotoToil.Mul(c); // 사람을 업고 간다 (v16.25 들것 · 수레 · 무중력)
        s *= (1f - 0.2f * c.Vitals.Injury) * Wounds.LegFactor(c.Vitals); // v12.7 다리를 다치면 더
        if (c.Job?.Urgent == true) s *= 1.35f; // 급하면 뛴다
        if (c.Outside) s *= 0.8f; // 선체 밖: 추진 팩으로 조심조심 (안전줄을 옮겨 걸며)
        s *= c.Fx.WalkMul; // v14.1 앓는 것 (감압병·관절염·근육 위축…)
        return s;
    }

    /// <summary>한 틱 이동. 도착하면 true.</summary>
    public static bool Step(CrewMember c, World w)
    {
        var path = c.Path;
        if (path == null) return true;

        float budget = Speed(c) * w.Movement.Manners(c, path); // v14.5 비켜서기 · 막힘 · 문 앞 확인 · 조용히 · 움찔
        budget *= w.Portable.SqueezeMul(c, path); // v16.7 통로에 세워 둔 카트를 비켜 간다
        budget *= w.Coop.SqueezeMul(c, path); // v17.4 펼친 부품 · 앞 상자 · 구경꾼 사이 · 좁은 문에서 카트 옮겨 싣기
        if (budget <= 0f) return false;
        path = c.Path ?? path; // 돌아가는 길로 바꿨을 수 있다
        bool repathed = false;
        while (budget > 0f && c.PathIndex < path.Count)
        {
            for (int k = c.PathIndex; k < Math.Min(path.Count, c.PathIndex + 2); k++)
            {
                var ahead = w.Ship.DoorAt(path[k]);
                if (ahead == null) continue;
                if (ahead.Locked) ahead.RequestOverride();
                else ahead.Request();
            }

            var next = path[c.PathIndex];
            // v10.3: 가던 길이 막혔다 — 목적지까지 다시 찾는다 (못 찾으면 그 걸음은 실패)
            if (Blocked(w.Ship, next) && !w.Paths.Crawl[w.Ship.Grid.Index(next)]) // v16.3 열린 정비 통로는 벽이어도 기어서 지난다
            {
                var goal = c.Destination ?? path[^1];
                if (repathed || Blocked(w.Ship, goal) || !SetDestination(c, w, goal))
                {
                    c.Path = null;
                    c.PathBlocked = true;
                    return false;
                }
                path = c.Path!;
                repathed = true;
                continue;
            }
            var door = w.Ship.DoorAt(next);
            if (door != null && door.Openness < 0.8f) break;

            var delta = next.Center - c.Position;
            float dist = delta.Length();
            if (dist > 0.0001f) c.Facing = delta / dist;

            if (dist <= budget)
            {
                c.Position = next.Center;
                budget -= dist;
                c.PathIndex++;
            }
            else
            {
                c.Position += delta / dist * budget;
                budget = 0f;
            }
        }

        if (c.PathIndex >= path.Count)
        {
            c.Path = null;
            return true;
        }
        return false;
    }
}

/// <summary>여러 작업이 같이 쓰는 계획 조각.</summary>
public static class Plans
{
    /// <summary>손에 든 물건이 있으면 먼저 알맞은 보관함에 넣고 오는 단계들.</summary>
    public static List<Toil> DropOff(CrewMember c, World w, DistanceField dist)
    {
        var toils = new List<Toil>();
        if (c.Carrying is not ItemStack held) return toils;
        // 한 번에 다 들어가는 곳을 먼저, 없으면 조금이라도 들어가는 곳
        var (box, spot) = NearestContainer(w, dist, c, f => f.Storage!.Accepts(held.Kind) && f.Storage.Free >= held.Count);
        if (box == null) (box, spot) = NearestContainer(w, dist, c, f => f.Storage!.Accepts(held.Kind) && f.Storage.Free > 0);
        // v14.5 비상 경보 중: 보관함이 멀면 먹을 것·원료는 그 자리에 내려놓고 간다 (공구·수리재·부품은 그 일에 쓸 수 있어 들고 간다) — 조용해지면 챙긴다
        if (w.Movement.Hurry(w) && MovementSystem.CanSetDown(held.Kind) && (box == null || dist.Get(spot) > 60) && c.Room != null && !c.Outside)
        {
            toils.Add(new DoToil((cm, world) => { world.Movement.SetDown(cm, "경보"); return true; }));
            return toils;
        }
        if (box == null)
            World.Trace?.Invoke($"DROPOFF-FAIL {c.Name} {held} @{c.Cell} room {c.Room?.Name} reach " +
                string.Join(",", w.Ship.Containers.Where(f => f.Storage!.Accepts(held.Kind)).Select(f => $"{f.Label}:{f.Storage!.Free}:{f.UseSpots.Select(dist.Get).DefaultIfEmpty(-9).Max()}")));
        if (box != null)
        {
            toils.Add(new GotoToil(spot));
            toils.Add(new PutToil(box));
        }
        // 둘 곳이 없으면 손을 비운다 (채소·음식은 퇴비로, 나머지는 버림). 손이 묶여 아무것도 못 하는 일을 막는다.
        toils.Add(new DoToil((cm, world) =>
        {
            // 가려던 선반이 그새 찼으면 다른 곳을 다시 찾는다 (정말 둘 곳이 하나도 없을 때만 버린다)
            if (cm.Carrying is ItemStack still && world.Ship.Containers.Any(f => f.Storage!.Accepts(still.Kind) && f.Storage.Free > 0))
            {
                if (world.Ship.Containers.Any(f => f.Storage!.Accepts(still.Kind) && f.Storage.Free > 0 && world.Body.MayEnter(cm, f.Room))) return false;
                // 둘 선반은 잠긴 구역에만 있다 — 버리지 않고 이 방 바닥에 내려놓는다 (들어갈 수 있는 사람이 챙긴다)
                if (world.Movement.SetDown(cm, "둘 선반이 잠긴 방에 있다", any: true)) return true;
                return false;
            }
            if (cm.Carrying is ItemStack left)
            {
                world.Log.Add(world.Tick, ItemKinds.IsFood(left.Kind) ? LogKind.Life : LogKind.Warning,
                    ItemKinds.IsFood(left.Kind) ? $"둘 곳이 없어 {left}개를 퇴비로 돌렸다" : $"둘 곳이 없어 {left}개를 버렸다", cm.Id);
                cm.Carrying = null;
            }
            return true;
        }));
        return toils;
    }

    /// <summary>조건에 맞는 보관함 중 가장 가까운 것과 그 앞 칸.</summary>
    public static (Furniture? box, Cell spot) NearestContainer(World w, DistanceField dist, CrewMember c,
        Func<Furniture, bool> match)
    {
        Furniture? best = null;
        Cell bestSpot = default;
        int bestCost = int.MaxValue;
        foreach (var f in w.Ship.Containers)
        {
            if (!match(f) || !w.Body.MayEnter(c, f.Room)) continue; // 잠긴 구역의 보관함은 고르지 않는다
            foreach (var s in f.UseSpots)
            {
                int d = dist.Get(s);
                if (d < 0 || d >= bestCost) continue;
                best = f; bestSpot = s; bestCost = d;
            }
        }
        return (best, bestSpot);
    }

    /// <summary>설비 옆에서 일할 칸 (다른 사람이 안 쓰는 곳 우선).</summary>
    public static Cell? WorkSpot(Furniture f, World w, DistanceField dist, CrewMember c) =>
        WorkSpot(WorkTarget.Of(f), w, dist, c);

    /// <summary>작업 대상 옆에서 일할 칸 (다른 사람이 안 쓰는 곳 우선).</summary>
    public static Cell? WorkSpot(WorkTarget t, World w, DistanceField dist, CrewMember c)
    {
        Cell? best = null;
        int bestCost = int.MaxValue;
        foreach (var s in t.Spots(w.Ship))
        {
            int d = dist.Get(s);
            if (d < 0) continue;
            if (w.IsSpotTaken(s, c)) d += 500;
            if (d < bestCost) { best = s; bestCost = d; }
        }
        return best;
    }

    /// <summary>우주선 어딘가(보관함)에 그 물건이 몇 개 있는지.</summary>
    public static int Available(World w, ItemKind k) => w.Ship.CountStored(k);
}
