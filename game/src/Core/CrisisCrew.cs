using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.21 승무원 위기 행동.
// ① 공황 재조정: 짧게(수십 초 ~ 몇 분) · 훈련 · 겪어 본 규모 · 제 자리(비상 배치)가 있으면 덜 · 한 사고에서 겪을수록 덜 ·
//    침착한 동료가 말로 · 손으로 정신 차리게 한다 · 목숨이 걸린 행동(대피 · 엎드리기 · 마스크 · 우주복)은 공황 중에도 몸이 먼저.
// ② 비상 배치표: 함장 · 회의가 사고 종류별 자리(소화 · 격벽 · 전력 · 의료 · 선외 · 대피 유도)를 정한다 — 경보를 알면 각자 제 자리로,
//    빈 자리는 다음 사람이, 사람이 바뀌면 다음 회의에서 다시 짠다.
// ③ 모두 아는 비상 절차(보조 발전기 · 차단기 · 소화기 · 수동 격벽 · 산소 마스크): 정기 훈련 · 사고 뒤 훈련 · 실제로 해 본 것으로 익힌다 —
//    익힌 사람은 빨리 · 덜 틀린다. 배터리 추세로 정전을 내다보고 미리 발전기 쪽으로 · 컴퓨터가 원격으로 못 켜면 사람이 손으로.
// ④ 한 작업에 여러 명: 큰 일(수리 · 소화 · 파공 · 잔해 …)에 2~4명 — 작업판이 인원 상한(일감 · 공간 · 사고 규모)을 갖고,
//    가장 솜씨 좋은 사람이 이끌고 나머지는 거든다 (사람이 늘수록 덜 붙고, 비좁으면 서로 방해).
// ⑤ 위기 우선순위: 생명 > 산소/압력 > 불 > 전력 > 나머지 — 공황 · 잠 · 끼니 · 여가가 생명 일을 이기지 않게.
// 모르는 위험엔 반응하지 않는다 (아는 사람만 제 자리로 간다).

public enum StationRole : byte { None, Fire, Bulkhead, Power, Medical, Eva, Guide }
public enum CrisisProc : byte { Aux, Breaker, Extinguisher, Bulkhead, Mask }

public sealed class StationBill
{
    public readonly SortedDictionary<int, StationRole> Of = new();
    /// <summary>역할마다 차례 (앞사람이 못 나오면 다음 사람).</summary>
    public readonly Dictionary<StationRole, List<int>> Order = new();
    public long Drawn = -1;
    public int By = -1;
    public int Version;
    public string Why = "";
}

public sealed partial class CrisisCrewSystem
{
    /// <summary>시험용: 예전 승무원 (전 · 후 비교) — 켜면 이 단계의 모든 훅이 예전 값을 돌려준다.</summary>
    public static bool Off { get; set; }

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 211));
    public CrisisCrewSystem(World w) => _w = w;

    public StationBill Bill { get; } = new();
    public int Tanks, Snaps, HandSnaps, BodyFirsts, Masks, Doors, AuxCalls, AuxEarly, Joins, Leads, Redraws, Fills, Drills, Debriefs, Musters, ProcUses;
    public float HelpHours;
    /// <summary>화면용: 최근 정신 차리게 한 손 (누가 → 누구, 손이면 true).</summary>
    public readonly List<(long tick, int from, int to, bool hand)> SnapMarks = new();

    private readonly Dictionary<int, float[]> _proc = new();
    private readonly Dictionary<int, StationRole> _active = new();
    private readonly Dictionary<int, int> _panicsNow = new();
    private readonly Dictionary<int, long> _masked = new();
    private readonly Dictionary<int, long> _bodyLogged = new();
    private readonly HashSet<int> _involved = new();
    private readonly HashSet<int> _drillDue = new();
    private readonly HashSet<StationRole> _need = new();
    private long _crisisSince = -1, _calmSince = -1, _staleSince = -1, _auxCallAt = -1;
    private string _crewSig = "";
    private bool _stale = true;

    // ───────────────────────────── 이름 ─────────────────────────────

    public static string RoleName(StationRole r) => r switch
    {
        StationRole.Fire => "소화", StationRole.Bulkhead => "격벽", StationRole.Power => "전력", StationRole.Medical => "의료",
        StationRole.Eva => "선외", StationRole.Guide => "대피 유도", _ => "-",
    };

    public static string ProcName(CrisisProc p) => p switch
    {
        CrisisProc.Aux => "보조 발전기 켜기", CrisisProc.Breaker => "차단기 점검", CrisisProc.Extinguisher => "소화기",
        CrisisProc.Bulkhead => "격벽 손으로 닫기", _ => "산소 마스크",
    };

    private static CrisisProc RoleProc(StationRole r) => r switch
    {
        StationRole.Fire => CrisisProc.Extinguisher, StationRole.Bulkhead => CrisisProc.Bulkhead, StationRole.Power => CrisisProc.Aux,
        StationRole.Medical => CrisisProc.Mask, StationRole.Eva => CrisisProc.Bulkhead, _ => CrisisProc.Mask,
    };

    /// <summary>이 일이 어느 비상 자리의 몫인가.</summary>
    public static StationRole RoleFor(WorkOrder o) => o.Kind switch
    {
        WorkKind.Extinguish or WorkKind.OperateDamper or WorkKind.IsolateVent or WorkKind.BleedRoom or WorkKind.CoolDown => StationRole.Fire,
        WorkKind.SealBreach when o.External => StationRole.Eva,
        WorkKind.RepairHull or WorkKind.ReplacePanel or WorkKind.InspectHull => StationRole.Eva,
        WorkKind.SealBreach or WorkKind.CrankDoor or WorkKind.WeldBulkhead or WorkKind.SealOffRoom or WorkKind.RepairDoor or WorkKind.ClearRubble => StationRole.Bulkhead,
        WorkKind.StartAux or WorkKind.ResetBreaker or WorkKind.BreakerOn or WorkKind.RestartReactor or WorkKind.ManualStart or WorkKind.Refuel
            or WorkKind.ShedLoad or WorkKind.InstallJumper or WorkKind.RestoreCircuit or WorkKind.IsolatePower or WorkKind.RepairNet => StationRole.Power,
        WorkKind.Repair when o.Target.Furniture is Furniture f && Crisis.PowerChain(f.Type) => StationRole.Power,
        WorkKind.Treat or WorkKind.Rescue => StationRole.Medical,
        WorkKind.WakeCrew => StationRole.Guide,
        _ => StationRole.None,
    };

    // ───────────────────────────── 비상 절차 숙련 ─────────────────────────────

    private float[] Procs(CrewMember c)
    {
        if (_proc.TryGetValue(c.Id, out var p)) return p;
        float el = c.SkillLevel(Skill.Electrical), me = c.SkillLevel(Skill.Mechanics), md = c.SkillLevel(Skill.Medicine);
        var bg = c.Background;
        p = new float[5];
        p[(int)CrisisProc.Aux] = 0.15f + 0.55f * el + (bg is Background.Lineworker or Background.MilitaryTech ? 0.15f : 0f);
        p[(int)CrisisProc.Breaker] = 0.15f + 0.6f * el + (bg is Background.Lineworker or Background.SysAdmin ? 0.15f : 0f);
        p[(int)CrisisProc.Extinguisher] = 0.25f + 0.35f * me + (bg is Background.Firefighter ? 0.35f : bg is Background.Soldier or Background.Police ? 0.15f : 0f);
        p[(int)CrisisProc.Bulkhead] = 0.15f + 0.45f * me + (bg is Background.Welder or Background.Miner ? 0.15f : 0f);
        p[(int)CrisisProc.Mask] = 0.35f + 0.25f * md + (bg is Background.Diver or Background.Paramedic or Background.Nurse ? 0.2f : 0f);
        for (int i = 0; i < 5; i++) p[i] = Math.Clamp(p[i], 0.05f, 0.9f);
        _proc[c.Id] = p;
        return p;
    }

    /// <summary>이 사람이 그 절차를 얼마나 익혔나 0~1.</summary>
    public float Proc(CrewMember c, CrisisProc p) => Procs(c)[(int)p];
    public float ProcAvg(CrewMember c) { var p = Procs(c); return (p[0] + p[1] + p[2] + p[3] + p[4]) / 5f; }

    /// <summary>익힌다 (남은 만큼 조금씩).</summary>
    public void Practice(CrewMember c, CrisisProc p, float amount)
    {
        if (Off || c.IsChild) return;
        var a = Procs(c);
        a[(int)p] = MathF.Min(0.98f, a[(int)p] + amount * (1f - a[(int)p]));
    }

    /// <summary>절차 시간 배율: 익힌 사람은 빠르다 (0.7 ~ 1.25).</summary>
    public float ProcTime(CrewMember c, CrisisProc p) => Off ? 1f : 1.25f - 0.55f * Proc(c, p);
    /// <summary>절차 실수 배율: 익힌 사람은 덜 틀린다 (0.35 ~ 1.4).</summary>
    public float ProcSlip(CrewMember c, CrisisProc p) => Off ? 1f : 1.4f - 1.05f * Proc(c, p);

    /// <summary>실제로 해 본 일 (일을 끝냈다): 그 절차가 손에 붙는다.</summary>
    public void Did(CrewMember c, WorkOrder o)
    {
        if (Off) return;
        CrisisProc? p = o.Kind switch
        {
            WorkKind.StartAux or WorkKind.Refuel => CrisisProc.Aux,
            WorkKind.ResetBreaker or WorkKind.BreakerOn or WorkKind.RestoreCircuit => CrisisProc.Breaker,
            WorkKind.Extinguish => CrisisProc.Extinguisher,
            WorkKind.CrankDoor or WorkKind.WeldBulkhead or WorkKind.SealOffRoom => CrisisProc.Bulkhead,
            _ => null,
        };
        if (p is CrisisProc pp) { Practice(c, pp, 0.12f); ProcUses++; }
        // 제 몫을 했다 — 자부심 (두뇌 2.0)
        if (_active.TryGetValue(c.Id, out var role) && role == RoleFor(o) && role != StationRole.None)
            _w.Brain2.Emotions.Feel(c, Feeling.Pride, 0.12f, $"비상 배치 {RoleName(role)} — 제 몫을 했다");
    }

    /// <summary>비상 훈련을 마쳤다 (정기 · 사고 뒤): 제 자리 절차를 크게, 나머지는 조금.</summary>
    public void Drilled(CrewMember c)
    {
        if (Off) return;
        Drills++;
        var role = Bill.Of.GetValueOrDefault(c.Id);
        for (int i = 0; i < 5; i++) Practice(c, (CrisisProc)i, 0.08f);
        if (role != StationRole.None) Practice(c, RoleProc(role), 0.2f);
        if (_drillDue.Remove(c.Id)) Life.Diary(_w, c, Persona.Say(c, "지난번에 몸이 굳었던 걸 떠올리며 훈련을 되풀이했다"));
    }

    // ───────────────────────────── 비상 배치표 ─────────────────────────────

    public StationRole BillRole(CrewMember c) => Bill.Of.GetValueOrDefault(c.Id);
    /// <summary>지금 이 사람이 서는 비상 자리 (경보를 알고 · 그 사고가 났을 때만 · 빈 자리를 대신 맡았으면 그 자리).</summary>
    public StationRole Active(CrewMember c) => _active.GetValueOrDefault(c.Id);
    public IReadOnlyDictionary<int, StationRole> ActiveRoles => _active;

    private float Fit(CrewMember c, StationRole r)
    {
        var t = c.Traits;
        var bg = c.Background;
        return r switch
        {
            StationRole.Fire => 0.45f * c.SkillLevel(Skill.Mechanics) + 0.3f * t.Bravery + 0.25f * Proc(c, CrisisProc.Extinguisher) + (bg == Background.Firefighter ? 0.35f : 0f),
            StationRole.Bulkhead => 0.45f * c.SkillLevel(Skill.Mechanics) + 0.2f * t.Diligence + 0.25f * Proc(c, CrisisProc.Bulkhead) + (bg is Background.Welder or Background.Carpenter ? 0.2f : 0f),
            StationRole.Power => 0.55f * c.SkillLevel(Skill.Electrical) + 0.2f * c.SkillLevel(Skill.Engineering) + 0.3f * Proc(c, CrisisProc.Aux) + (bg == Background.Lineworker ? 0.2f : 0f),
            StationRole.Medical => 0.85f * c.SkillLevel(Skill.Medicine) + (bg is Background.MedStudent or Background.Nurse or Background.Paramedic ? 0.3f : 0f),
            StationRole.Eva => 0.4f * t.Bravery + 0.3f * c.SkillLevel(Skill.Mechanics) + MathF.Min(0.2f, c.EvaHours * 0.05f) + (bg is Background.Diver or Background.Soldier ? 0.2f : 0f) - 0.3f * c.Memory.Trauma,
            StationRole.Guide => 0.4f * t.Calm + 0.3f * t.Sociability + 0.3f * CommandSystem.Leadership(c) + (c.Id == _w.Command.CaptainId ? 0.3f : 0f),
            _ => 0f,
        };
    }

    private static readonly StationRole[] DrawOrder = { StationRole.Medical, StationRole.Power, StationRole.Fire, StationRole.Bulkhead, StationRole.Eva, StationRole.Guide };

    /// <summary>배치표를 짠다 (함장 혼자 또는 회의). 바뀐 사람만 알린다.</summary>
    private string Draw(CrewMember? by, string why)
    {
        var w = _w;
        var people = w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Away).ToList();
        var old = new Dictionary<int, StationRole>(Bill.Of);
        Bill.Of.Clear();
        Bill.Order.Clear();
        int n = people.Count;
        if (n == 0) return "";
        var slots = new Dictionary<StationRole, int>
        {
            [StationRole.Medical] = Math.Max(1, n / 8), [StationRole.Power] = Math.Max(1, n / 6), [StationRole.Fire] = Math.Max(1, n / 4),
            [StationRole.Bulkhead] = n >= 4 ? Math.Max(1, n / 6) : 0, [StationRole.Eva] = n >= 8 ? Math.Max(1, n / 10) : 0, [StationRole.Guide] = n >= 5 ? 1 : 0,
        };
        var free = people.OrderBy(c => c.Id).ToList();
        foreach (var r in DrawOrder)
            for (int i = 0; i < slots[r] && free.Count > 0; i++)
            {
                var best = free.OrderByDescending(c => Fit(c, r)).ThenBy(c => c.Id).First();
                Bill.Of[best.Id] = r;
                free.Remove(best);
            }
        // 남은 사람은 소화 · 격벽 · 전력 중 가장 맞는 자리 (큰 배는 손이 많아야 한다)
        foreach (var c in free)
        {
            var r = new[] { StationRole.Fire, StationRole.Bulkhead, StationRole.Power }.OrderByDescending(x => Fit(c, x)).First();
            Bill.Of[c.Id] = r;
        }
        // 차례: 자리마다 맡은 사람(솜씨 순) 뒤에 대신할 사람(그 자리에 맞는 순)
        foreach (var r in DrawOrder)
        {
            var own = Bill.Of.Where(kv => kv.Value == r).Select(kv => people.First(p => p.Id == kv.Key)).OrderByDescending(c => Fit(c, r)).ThenBy(c => c.Id);
            var back = people.Where(c => Bill.Of[c.Id] != r).OrderByDescending(c => Fit(c, r)).ThenBy(c => c.Id).Take(3);
            Bill.Order[r] = own.Concat(back).Select(c => c.Id).ToList();
        }
        Bill.Drawn = w.Tick;
        Bill.By = by?.Id ?? -1;
        Bill.Version++;
        Bill.Why = why;
        _crewSig = CrewSig();
        _stale = false;
        _staleSince = -1;
        var changed = Bill.Of.Where(kv => !old.TryGetValue(kv.Key, out var o) || o != kv.Value).Select(kv => $"{people.First(p => p.Id == kv.Key).Name} {RoleName(kv.Value)}").ToList();
        string text = changed.Count == 0 ? "그대로" : string.Join(" · ", changed.Take(6)) + (changed.Count > 6 ? $" 외 {changed.Count - 6}명" : "");
        if (Bill.Version > 1) Redraws++;
        foreach (var c in people)
            if (!old.TryGetValue(c.Id, out var o) || o != Bill.Of[c.Id])
                Life.Diary(w, c, Persona.Say(c, $"비상 배치표에 내 자리는 {RoleName(Bill.Of[c.Id])}"));
        return text;
    }

    private string CrewSig() => string.Join(",", _w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Away && c.Vitals.Injury < 0.6f).Select(c => c.Id));

    /// <summary>회의 안건: 사람이 바뀌었으면 배치표를 다시 짠다 (회의가 정한다).</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> attendees, CrewMember chair)
    {
        if (Off || !_stale || Bill.Drawn < 0 || attendees.Count < 2) return;
        string text = Draw(chair, "회의");
        rec.Items.Add(new AgendaItem { Title = "비상 배치표 다시 짜기", Topic = "stations", Passed = true, Outcome = $"빈 자리를 채웠다 — {text}" });
        _w.Log.Add(_w.Tick, LogKind.Ship, $"회의에서 비상 배치표를 다시 짰다 — {text}");
    }

    // ───────────────────────────── 공황 (Mind.cs 훅) ─────────────────────────────

    /// <summary>공황 확률 배율: 훈련 · 익힌 절차 · 겪어 본 규모 · 제 자리 · 이번 사고에서 이미 겪음 · 곁의 침착한 동료.</summary>
    public float PanicMul(CrewMember c)
    {
        if (Off) return 1f;
        float m = 0.7f;
        if (c.Drilled(_w)) m *= 0.65f;
        m *= 1f - 0.45f * ProcAvg(c);
        m *= 0.45f + 0.55f * _w.Scale.FearMul(c);
        if (_active.ContainsKey(c.Id)) m *= 0.5f; // 할 일이 있는 사람은 덜 무너진다
        m /= 1f + 0.9f * _panicsNow.GetValueOrDefault(c.Id);
        if (CalmBeside(c) != null) m *= 0.75f;
        return m;
    }

    /// <summary>공황이 얼마나 가나 (분): 달아나면 30초 ~ 2분 반, 얼어붙으면 20초 ~ 1분 남짓.</summary>
    public float PanicMinutes(CrewMember c, bool frozen, float old)
    {
        if (Off) return old;
        float k = MathF.Min(1.25f, 0.55f + 0.5f * MindSystem.PanicScale) * (1f - 0.35f * ProcAvg(c));
        return (frozen ? 0.35f + 0.8f * R.Float() : 0.5f + 2f * R.Float()) * k;
    }

    /// <summary>다시 공황에 빠지기까지 (틱): 이번 사고에서 겪을수록 길다.</summary>
    public long PanicCooldown(CrewMember c) => Off ? SimTime.Minutes(20) : SimTime.Minutes(30 + 15 * _panicsNow.GetValueOrDefault(c.Id));

    /// <summary>얼어붙어도 되나: 목숨이 걸린 자리에서는 몸이 먼저 — 얼어붙지 않고 빠져나간다.</summary>
    public bool MayFreeze(CrewMember c) => Off || EvacuateActivity.DangerHere(c, _w) < 0.55f && !EvacuateActivity.Breathless(c, _w);

    public void OnPanic(CrewMember c)
    {
        if (Off) return;
        _panicsNow[c.Id] = _panicsNow.GetValueOrDefault(c.Id) + 1;
        _involved.Add(c.Id);
    }

    private static Activity[]? _life;

    /// <summary>공황 중에도 몸이 먼저: 대피 · 엎드리기 · 우주복 산소 · 선외 생존이 급하면 공황 행동은 물러선다.</summary>
    public bool BodyFirst(CrewMember c, DistanceField dist, out string? what)
    {
        what = null;
        if (Off) return false;
        _life ??= Brain.Activities.Where(a => a is EvacuateActivity or TakeCoverActivity or RefillSuitActivity or EvaSurviveActivity).ToArray();
        foreach (var a in _life)
        {
            var (s, _) = a.Score(c, _w, dist);
            if (s < 0.95f) continue;
            what = a.Label;
            if (_bodyLogged.GetValueOrDefault(c.Id, -1) != c.Mind.PanicUntil)
            {
                _bodyLogged[c.Id] = c.Mind.PanicUntil;
                BodyFirsts++;
                _w.Log.Add(_w.Tick, LogKind.Warning, $"공황 속에서도 몸이 먼저 움직였다 — {a.Label}", c.Id);
            }
            return true;
        }
        return false;
    }

    /// <summary>곁의 침착한 동료 (같은 방 · 깨어 있고 · 공황이 아니다).</summary>
    private CrewMember? CalmBeside(CrewMember c)
    {
        if (c.Room == null) return null;
        CrewMember? best = null;
        float bs = 0f;
        foreach (var h in _w.Crew)
        {
            if (h == c || h.Room != c.Room || !h.CanAct || !h.IsAwake || h.IsChild || h.Mind.Panicking(_w.Tick)) continue;
            float d2 = (h.Position - c.Position).LengthSquared();
            if (d2 > 25f) continue;
            float s = 0.4f + h.Traits.Calm + (h.Drilled(_w) ? 0.2f : 0f) + (_active.ContainsKey(h.Id) ? 0.2f : 0f) + 0.3f * MathF.Max(0f, c.AffinityTo(h)) - 0.4f * h.Needs.Stress - d2 / 60f;
            if (s > bs) { bs = s; best = h; }
        }
        return bs > 0.5f ? best : null;
    }

    // ───────────────────────────── 위기 우선순위 ─────────────────────────────

    /// <summary>생명 0 · 산소/압력 1 · 불 2 · 전력 3 · 나머지 4.</summary>
    public static int Tier(World w, WorkOrder o)
    {
        switch (o.Kind)
        {
            case WorkKind.Rescue or WorkKind.WakeCrew: return 0;
            case WorkKind.Treat: return o.Urgency >= 0.9f ? 0 : 4;
            case WorkKind.SealO2Line or WorkKind.BuildOxygen or WorkKind.IsolateVent: return 1;
            case WorkKind.SealBreach or WorkKind.CrankDoor or WorkKind.WeldBulkhead or WorkKind.SealOffRoom:
                return o.Target.CurrentRoom is Room r && (o.Urgency >= 1.2f || w.Crew.Any(c => !c.Dead && c.Room == r)) ? 1 : 4;
            case WorkKind.Repair when o.Target.Furniture is Furniture f && Crisis.AirChain(f.Type): return 1;
            case WorkKind.Extinguish or WorkKind.OperateDamper or WorkKind.BleedRoom or WorkKind.CoolDown: return 2;
            default: return RoleFor(o) == StationRole.Power ? 3 : 4;
        }
    }

    /// <summary>작업 매력에 더할 몫 (Chores.Appeal): 제 비상 자리의 일 · 위기 우선순위.</summary>
    public float Bias(CrewMember c, WorkOrder o)
    {
        if (Off) return 0f;
        float b = 0f;
        var role = RoleFor(o);
        if (role != StationRole.None)
        {
            if (_active.TryGetValue(c.Id, out var a) && a == role) b += 0.3f;
            else if (Bill.Of.GetValueOrDefault(c.Id) == role && o.Urgency >= 0.85f) b += 0.1f;
        }
        if (Crisis.Acting(_w) && o.Urgency >= 0.6f)
        {
            float k = Crisis.Level(_w) == CrisisLevel.Survival ? 1f : 0.6f;
            b += k * Tier(_w, o) switch { 0 => 0.15f, 1 => 0.12f, 2 => 0.05f, 3 => 0f, _ => -0.05f };
        }
        return b;
    }

    /// <summary>생명 일을 아는 사람은 잠 · 끼니 · 여가를 미룬다 (Brain.Think · 위기 판단 위에 겹친다).</summary>
    public void Damp(CrewMember c, Activity a, ref float score, ref string reason)
    {
        if (Off || score <= 0f || !_active.ContainsKey(c.Id)) return;
        float m = a switch
        {
            SleepActivity when c.Needs.Fatigue < 0.95f => 0.5f,
            EatActivity when c.Needs.Hunger < 0.9f => 0.5f,
            RelaxActivity or ChatActivity or WanderActivity or HobbyActivity or TidyActivity or MendActivity or SceneActivity => 0.4f,
            _ => 1f,
        };
        if (m >= 1f) return;
        score *= m;
        reason += $" · 비상 배치 {RoleName(_active[c.Id])}";
    }

    /// <summary>현장 지휘가 조를 짤 때: 배치표에 그 자리로 적힌 사람을 앞에 (Command.Assign).</summary>
    public float TeamFit(CrewMember c, TeamKind k)
    {
        if (Off) return 0f;
        var r = Bill.Of.GetValueOrDefault(c.Id);
        bool match = k switch
        {
            TeamKind.Fire => r == StationRole.Fire, TeamKind.Breach => r is StationRole.Bulkhead or StationRole.Eva, TeamKind.Power => r == StationRole.Power,
            TeamKind.Medical or TeamKind.Rescue => r == StationRole.Medical, _ => false,
        };
        return match ? 0.35f : 0f;
    }

    // ───────────────────────────── 산소 마스크 ─────────────────────────────

    /// <summary>산소 마스크를 쓰고 있다 (연기 · 묽은 산소에서 숨을 잇는다 — 진공에서는 소용없다).</summary>
    public bool Masked(CrewMember c) => !Off && _masked.TryGetValue(c.Id, out var t) && t > _w.Tick;

    /// <summary>입은 우주복의 산소가 한 시간도 안 남았으면 보관함에서 새 통으로 갈아 끼운다 (보관함의 한 벌과 바꿔 입거나 · 공기 탱크에서 채운다). 못 채우면 false.</summary>
    public bool FreshTank(CrewMember c, Furniture locker)
    {
        if (c.Suit is not SuitState s || s.Oxygen > 1f) return true;
        if (locker.Storage!.Take(ItemKind.Suit, 1) > 0)
        {
            locker.Storage.Add(ItemKind.Suit, 1);
            s.Oxygen = SuitState.TankHours;
            s.Leak = 1f;
        }
        else if (_w.Air.Reserve >= RefillSuitActivity.Cost)
        {
            _w.Air.Reserve -= RefillSuitActivity.Cost;
            s.Oxygen = SuitState.TankHours;
            _w.SuitRefills++;
        }
        else
        {
            _w.Log.Add(_w.Tick, LogKind.Warning, "우주복 산소가 바닥인데 갈아 끼울 통이 없다 — 들어가지 않는다", c.Id);
            return false;
        }
        Tanks++;
        _w.Log.Add(_w.Tick, LogKind.Work, "우주복 산소통을 새것으로 갈아 끼웠다", c.Id);
        return true;
    }

    // ───────────────────────────── 보조 발전기 예측 ─────────────────────────────

    /// <summary>배터리가 바닥나기까지 (분, 추세) — 채우는 중이면 무한.</summary>
    public float BatteryMinutes
    {
        get
        {
            var p = _w.Power;
            return p.BatteryFlow < -0.1f ? p.BatteryCharge / -p.BatteryFlow * 60f : float.PositiveInfinity;
        }
    }

    /// <summary>주 컴퓨터가 보조 발전기를 원격으로 켤 수 있는 형편인가 (통신 · 컴퓨터 · 발전기 고장).</summary>
    public bool ComputerCanStartAux()
    {
        var a = _w.Automation;
        var aux = _w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault(f => !f.Room.Detached);
        return a.Present && (a.MainOnline || a.BackupActive) && !AutomationSystem.Ship20Off && aux != null && aux.Room.DataLinked
               && aux.Machine is { Stopped: false } && a.TriageOrNull is not { AuxNeedsHands: true }; // 주 컴퓨터 트리아지와 같은 조건 (두 번 안 걸리면 손으로)
    }

    /// <summary>정전이 다가온다 (배터리 추세 · 부하 차단): 바닥나기 전에 미리 발전기를 켠다.</summary>
    public bool AuxDue()
    {
        if (Off) return false;
        var p = _w.Power;
        if (p.AuxRunning || p.AuxFuel <= 0.1f) return false;
        bool weak = !p.ReactorOnline || p.ReactorLimit < 12f;
        if (!weak) return false;
        return BatteryMinutes < 120f || p.BatteryPercent < 0.25f || p.Delivered < p.Demand * 0.8f;
    }

    /// <summary>미리 켜는 일의 긴급도: 컴퓨터가 원격으로 못 켜면 사람이 꼭 가야 한다.</summary>
    public float AuxUrgency => ComputerCanStartAux() ? 0.85f : 1.1f;
    public string AuxWhy
    {
        get
        {
            var p = _w.Power;
            float m = BatteryMinutes;
            return $"배터리 {p.BatteryPercent * 100:0}%" + (float.IsInfinity(m) ? "" : $" · 이대로면 {m:0}분 뒤 바닥") + (ComputerCanStartAux() ? "" : " · 원격 기동 안 됨 — 손으로");
        }
    }

    /// <summary>발전기 쪽으로 미리 가 있을 때 (정전이 다가온다).</summary>
    private bool AuxSoon()
    {
        var p = _w.Power;
        if (p.AuxRunning || p.AuxFuel <= 0.1f) return false;
        return (!p.ReactorOnline || p.ReactorLimit < 12f) && (BatteryMinutes < 240f || p.BatteryPercent < 0.35f || p.Delivered < p.Demand * 0.9f);
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        if (w.Crew.Count == 0) return;
        // 첫 출항: 함장이 배치표를 붙인다 · 사람이 바뀌면 다음 회의(하루를 넘기면 함장 혼자)
        if (Bill.Drawn < 0 && w.Tick > SimTime.Minutes(5) && w.Command.Captain is CrewMember cap0)
        {
            string text = Draw(cap0, "첫 출항");
            w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(cap0.Name)} 비상 배치표를 붙였다 — {text}");
        }
        if (w.Tick % SimTime.Minutes(5) == 0 && Bill.Drawn >= 0)
        {
            if (CrewSig() == _crewSig) _stale = false; // 돌아왔다 — 그대로 둔다
            else if (!_stale) { _stale = true; _staleSince = w.Tick; }
            else if (w.Tick - _staleSince > SimTime.TicksPerDay && w.Command.Captain is CrewMember cap1 && !Crisis.Acting(w))
            {
                string text = Draw(cap1, "함장 혼자");
                w.Log.Add(w.Tick, LogKind.Ship, $"회의가 열리지 않아 {Ko.IGa(cap1.Name)} 혼자 비상 배치표를 고쳤다 — {text}");
            }
        }
        Roles();
        Snap(dt);
        Reflexes(dt);
        AuxWatch();
        Aftermath();
        if (SnapMarks.Count > 0 && w.Tick - SnapMarks[0].tick > SimTime.Minutes(3)) SnapMarks.RemoveAt(0);
    }

    /// <summary>지금 필요한 자리와 그 자리에 서는 사람 (빈 자리는 다음 사람이).</summary>
    private void Roles()
    {
        var w = _w;
        _active.Clear();
        _need.Clear();
        if (!Crisis.Acting(w) && !AuxSoon()) return;
        var s = Crisis.Now(w);
        if (s.Fires > 0) _need.Add(StationRole.Fire);
        if (s.Breaches > 0 || w.Ship.LiveRooms.Any(r => r.Leaking && !r.Abandoned)) _need.Add(StationRole.Bulkhead);
        if (s.Power || AuxSoon()) _need.Add(StationRole.Power);
        if (s.Down > 0 || w.Board.Open.Any(o => o.Kind == WorkKind.Treat && o.Urgency >= 0.9f)) _need.Add(StationRole.Medical);
        if (w.Board.Open.Any(o => o.External && o.Urgency >= 0.85f)) _need.Add(StationRole.Eva);
        if (Crisis.Acting(w)) _need.Add(StationRole.Guide);
        foreach (var r in DrawOrder)
        {
            if (!_need.Contains(r) || !Bill.Order.TryGetValue(r, out var order)) continue;
            int want = Math.Max(1, Bill.Of.Count(kv => kv.Value == r));
            int got = 0;
            for (int i = 0; i < order.Count && got < want; i++)
            {
                var c = w.Brain2.Beliefs.CrewById(order[i]);
                if (c == null || !c.CanAct || c.IsChild || c.Outside && r != StationRole.Eva || !Knows(c, r)) continue;
                if (_active.ContainsKey(c.Id)) continue;
                // 뒷줄(대신할 사람): 제 자리가 지금 필요하면 제 자리부터
                var own = Bill.Of.GetValueOrDefault(c.Id);
                if (own != r && _need.Contains(own) && own != StationRole.Guide) continue;
                _active[c.Id] = r;
                got++;
                _involved.Add(c.Id);
                if (own != r && i >= want)
                {
                    Fills++;
                    if (w.Tick - _fillLog.GetValueOrDefault((int)r, -SimTime.Hours(1)) > SimTime.Minutes(30))
                    {
                        _fillLog[(int)r] = w.Tick;
                        w.Log.Add(w.Tick, LogKind.Warning, $"비상 배치 {RoleName(r)} 자리가 비었다 — 다음 차례가 맡는다", c.Id);
                        w.Automation.Reason($"bill-gap:{r}", $"비상 배치 {RoleName(r)} 자리가 비어 {Ko.IGa(c.Name)} 대신 맡는다 (배치표 차례)", SimTime.Minutes(30));
                    }
                }
            }
        }
    }

    private readonly Dictionary<int, long> _fillLog = new();

    /// <summary>이 사람이 그 자리가 필요한 걸 아는가 (모르는 위험엔 반응 못 한다).</summary>
    private bool Knows(CrewMember c, StationRole r)
    {
        var w = _w;
        if (!c.IsAwake && c.DeepAsleep) return false;
        var k = c.Mind.Knows;
        switch (r)
        {
            case StationRole.Fire: foreach (var key in k.Keys) if (key.StartsWith("fire:")) return true; break;
            case StationRole.Bulkhead or StationRole.Eva: foreach (var key in k.Keys) if (key.StartsWith("breach:")) return true; break;
            case StationRole.Medical: foreach (var key in k.Keys) if (key.StartsWith("down:")) return true; break;
            case StationRole.Power:
                if (c.Room is Room rr && (rr.Dark || !rr.Powered)) return true;
                if (w.Minds.AlarmReaches(c.Room) && c.IsAwake) return true; // 정전 경보 · 컴퓨터 방송
                break;
            case StationRole.Guide: return k.Count > 0 || w.Scale.Felt(c) >= IncidentScale.System;
        }
        return w.Scale.Felt(c) >= IncidentScale.Ship;
    }

    /// <summary>침착한 동료가 말로 · 손으로 정신 차리게 한다.</summary>
    private void Snap(float dt)
    {
        var w = _w;
        float minutes = dt * 60f;
        foreach (var c in w.Crew)
        {
            if (!c.Mind.Panicking(w.Tick) || c.Down || c.Dead) continue;
            var h = CalmBeside(c);
            if (h == null) continue;
            float d2 = (h.Position - c.Position).LengthSquared();
            bool hand = d2 <= 2.6f;
            float rate = (hand ? 0.55f : 0.18f) * (0.4f + h.Traits.Calm) * (1f + 0.5f * MathF.Max(0f, c.AffinityTo(h))) * (_active.TryGetValue(h.Id, out var hr) && hr == StationRole.Guide ? 1.5f : 1f);
            if (!R.Chance(1f - MathF.Exp(-rate * minutes))) continue;
            c.Mind.PanicUntil = w.Tick;
            c.Mind.Frozen = false;
            c.NextThinkTick = w.Tick + 1;
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.06f);
            w.Brain2.Emotions.Feel(c, Feeling.Fear, -0.15f, "");
            c.ChangeAffinity(h, 0.04f);
            h.ChangeAffinity(c, 0.02f);
            w.Relations.Remember(c, h, RelationReason.Comforted, hand ? "공황에 빠졌을 때 어깨를 붙잡아 줬다" : "공황에 빠졌을 때 정신 차리라고 불러 줬다");
            Snaps++;
            if (hand) HandSnaps++;
            SnapMarks.Add((w.Tick, h.Id, c.Id, hand));
            if (SnapMarks.Count > 12) SnapMarks.RemoveAt(0);
            h.Say(w, Persona.Say(h, hand ? $"{c.Name}, 나 봐. 숨 쉬어 — 같이 하자" : $"{c.Name}! 정신 차려, 할 일 있어"));
            c.Say(w, Persona.Say(c, "…응. 알았어"));
            w.Log.Add(w.Tick, LogKind.Life, hand ? $"{Ko.IGa(h.Name)} 어깨를 붙잡아 정신이 들었다" : $"{Ko.IGa(h.Name)} 부르는 소리에 정신이 들었다", c.Id);
        }
    }

    /// <summary>몸이 먼저 하는 것: 산소 마스크 · 불난 방 문 닫기.</summary>
    private void Reflexes(float dt)
    {
        var w = _w;
        var mounts = w.Body.Mounts;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Down || c.Room is not Room r || c.Suit is { Oxygen: > 0f } || !c.IsAwake) continue;
            var air = r.Air;
            if (air.Pressure < 40f || air.Smoke <= 0.45f && air.O2 >= 15.5f) continue;
            if (Masked(c)) continue;
            // 이 방 벽의 산소 마스크함 (열려 있든 아니든 — 함 안에 여럿)
            bool box = false;
            foreach (var m in mounts)
                if (m.Kind == MountKind.OxygenMasks && m.Room == r.Id && (m.Spot.Center - c.Position).LengthSquared() < 49f) { box = true; break; }
            if (!box) continue;
            // 익힌 사람은 바로 · 처음인 사람은 끈을 찾느라 잠깐 (공황 중이어도 손이 먼저 간다)
            if (!R.Chance(0.3f + 0.7f * Proc(c, CrisisProc.Mask))) continue;
            _masked[c.Id] = w.Tick + SimTime.Minutes(40);
            Masks++;
            Practice(c, CrisisProc.Mask, 0.1f);
            w.Log.Add(w.Tick, LogKind.Work, $"벽의 함에서 산소 마스크를 꺼내 썼다 ({r.Name})", c.Id);
        }
        // 불난 방에서 빠져나온 사람이 받쳐 둔 문을 닫는다
        foreach (var (room, _, _) in w.Fire.KnownFires())
            foreach (var d in room.Doors)
            {
                if (!d.HoldOpen) continue;
                var other = d.RoomA == room ? d.RoomB : d.RoomA;
                var by = w.Crew.FirstOrDefault(c => !c.Dead && c.CanAct && c.Room == other && (c.Position - d.Cell.Center).LengthSquared() < 4f);
                if (by == null) continue;
                d.HoldOpen = false;
                Doors++;
                w.Log.Add(w.Tick, LogKind.Work, $"{room.Name}에서 나오며 받쳐 둔 문을 닫았다", by.Id);
            }
    }

    /// <summary>정전 예측: 컴퓨터가 원격으로 못 켜면 전력 자리의 사람을 부른다.</summary>
    private void AuxWatch()
    {
        var w = _w;
        if (!AuxDue() || ComputerCanStartAux() || w.Tick - _auxCallAt < SimTime.Minutes(20)) return;
        if (!w.Automation.Present || !w.Automation.MainOnline && !w.Automation.BackupActive) return; // 컴퓨터가 꺼졌으면 부를 수도 없다 (사람이 알아서)
        var who = _active.Where(kv => kv.Value == StationRole.Power).Select(kv => w.Brain2.Beliefs.CrewById(kv.Key)).FirstOrDefault(c => c != null);
        if (who == null) return;
        _auxCallAt = w.Tick;
        AuxCalls++;
        string text = $"{who.Name}, 보조 발전기로 — {AuxWhy}";
        w.Automation.Speak.Announce(text, w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault()?.Room, 2, "aux");
        w.Automation.Reason("aux-hand", $"배터리 추세로 정전이 다가온다 — 발전기를 원격으로 못 켜 {Ko.EulReul(who.Name)} 보냈다", SimTime.Minutes(30));
    }

    /// <summary>사고가 끝나면: 돌아보기(절차가 손에 붙는다) · 공황을 겪은 사람은 훈련 · 컴퓨터가 훈련을 권한다.</summary>
    private void Aftermath()
    {
        var w = _w;
        bool acting = Crisis.Acting(w);
        if (acting) { if (_crisisSince < 0) _crisisSince = w.Tick; _calmSince = -1; return; }
        if (_crisisSince < 0) return;
        if (_calmSince < 0) { _calmSince = w.Tick; return; }
        if (w.Tick - _calmSince < SimTime.Minutes(30)) return;
        int panicked = 0;
        foreach (var id in _involved)
        {
            var c = w.Brain2.Beliefs.CrewById(id);
            if (c == null || c.Dead) continue;
            var role = Bill.Of.GetValueOrDefault(c.Id);
            if (role != StationRole.None) Practice(c, RoleProc(role), 0.05f);
            if (_panicsNow.GetValueOrDefault(id) > 0) { _drillDue.Add(id); panicked++; }
        }
        if (_involved.Count > 0) Debriefs++;
        if (panicked > 0)
            w.Automation.Reason("crisis-drill", $"지난 사고에서 {panicked}명이 얼어붙거나 달아났다 — 그 사람들부터 비상 훈련을 권한다", SimTime.Hours(6));
        _involved.Clear();
        _panicsNow.Clear();
        _crisisSince = -1;
        _calmSince = -1;
    }

    /// <summary>사고 뒤 훈련이 남은 사람.</summary>
    public bool DrillDue(CrewMember c) => !Off && _drillDue.Contains(c.Id);
    internal IEnumerable<int> DrillDueIds => _drillDue.OrderBy(x => x);

    // ───────────────────────────── 지문 ─────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Bill.Version); I(Bill.Of.Count);
        foreach (var (id, r) in Bill.Of) I(id * 8 + (int)r);
        foreach (var id in _proc.Keys.OrderBy(x => x)) { I(id); foreach (var v in _proc[id]) F(v); }
        I(Snaps); I(BodyFirsts); I(Masks); I(Doors); I(AuxCalls); I(Joins); I(Fills); I(Drills); I(Debriefs);
        foreach (var (o, list) in _hands.OrderBy(kv => kv.Key)) { I(o); foreach (var c in list) I(c.Id); }
        F(HelpHours);
    }
}
