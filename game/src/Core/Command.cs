using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v13.1 지휘: 선장 · 현장 지휘자 · 조 편성 · 2인 1조와 안전 감시자 · 교대 · 선장 스타일 · 불신임.
//
// 선장: 처음엔 지휘 순서의 첫 사람(기관장). 신뢰가 무너지면 정기 회의(v13.2)에서 불신임 → 새 선장을 뽑는다 (방침: 선출 방식·불신임 문턱).
// 선장 스타일: 권위형(정해 준 조를 꼭 지킨다 · 빠르다) / 협의형(조를 지키되 의견을 듣는다 · 덜 지친다) / 방임형(각자 판단).
// 현장 지휘자: 위기 때 방침(지휘: 사람 · 컴퓨터 · 상황 따라)대로 — 사람이면 선장(못 하면 다음 사람), 컴퓨터면 V 지휘 컴퓨터.
// 조 편성: 급한 일을 묶어(구조 · 봉합 · 소화 · 전력·냉각 · 치료) 솜씨·거리·피로를 보고 사람을 붙이고, 나머지는 대기조.
//   위험한 방(공기가 나쁘거나 불)에는 2인 1조 — 한 명이 들어가고 한 명은 문 밖에서 지킨다 (방침: 위험 감수). 안에서 쓰러지면 감시자가 바로 끌어낸다.
// 교대: 비상 일을 오래 한 사람·지친 사람은 대기조와 바꿔 재운다 (방침: 위기 교대).

public enum CaptainStyle { Authoritarian, Consultative, Laissez }
public enum TeamKind { Rescue, Breach, Fire, Power, Medical, Reserve }

public sealed class Team
{
    public string Key { get; init; } = "";
    public TeamKind Kind { get; init; }
    public Room? Room { get; init; }
    public int Worker { get; set; } = -1;
    public int Watcher { get; set; } = -1;
    public bool Hazard { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class CommandSystem
{
    private readonly World _w;
    private readonly Rng _rng;
    public CommandSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 4099 + 61));
    }

    // ── 선장 ──

    public int CaptainId { get; private set; } = -1;
    public CrewMember? Captain => _w.Crew.FirstOrDefault(c => c.Id == CaptainId && !c.Dead);
    public CaptainStyle Style { get; private set; } = CaptainStyle.Consultative;
    /// <summary>선장에 대한 배 전체의 신뢰 0~1.</summary>
    public float Trust { get; set; } = 0.65f;
    public int Elections, NoConfidence, CaptainsLost;

    public static string StyleName(CaptainStyle s) => s switch { CaptainStyle.Authoritarian => "권위형", CaptainStyle.Consultative => "협의형", _ => "방임형" };

    public static CaptainStyle StyleOf(CrewMember c)
    {
        var t = c.Traits;
        float auth = (c.Value is CrewValue.Rules or CrewValue.Efficiency ? 0.3f : 0f) + (1f - t.Sociability) * 0.4f + t.Diligence * 0.2f;
        float cons = (c.Value is CrewValue.People or CrewValue.Safety ? 0.3f : 0f) + t.Sociability * 0.4f + t.Calm * 0.2f;
        float lax = (c.Value == CrewValue.Freedom ? 0.4f : 0f) + (1f - t.Diligence) * 0.4f;
        return auth >= cons && auth >= lax ? CaptainStyle.Authoritarian : cons >= lax ? CaptainStyle.Consultative : CaptainStyle.Laissez;
    }

    /// <summary>지휘 솜씨 0~1 (침착·성실·사교 + 공학·전기 솜씨 − 지침·스트레스).</summary>
    public static float Leadership(CrewMember c) =>
        Math.Clamp(0.25f * c.Traits.Calm + 0.2f * c.Traits.Diligence + 0.15f * c.Traits.Sociability
                   + 0.2f * MathF.Max(c.SkillLevel(Skill.Engineering), c.SkillLevel(Skill.Electrical)) + 0.2f * MathF.Min(1f, c.Stats.Repairs / 30f)
                   - 0.25f * c.Needs.Stress - 0.2f * MathF.Max(0f, 0.4f - c.Needs.Rest), 0f, 1f);

    private void Appoint(CrewMember c, string why)
    {
        var w = _w;
        CaptainId = c.Id;
        Style = StyleOf(c);
        w.History.Add(w, HistoryKind.Decision, $"{Ko.IGa(c.Name)} 선장이 되었다 ({StyleName(Style)}) — {why}", null, new[] { c }, log: true);
        Life.Diary(w, c, $"선장을 맡았다 — {why}.");
    }

    // ── 현장 지휘 ──

    /// <summary>지금 지휘하는 사람 (컴퓨터가 지휘하면 null).</summary>
    public CrewMember? Commander { get; private set; }
    public bool ComputerCommands { get; private set; }
    public bool Active { get; private set; }
    public List<Team> Teams { get; } = new();
    private readonly Dictionary<int, Team> _teamOf = new();
    private readonly Dictionary<int, long> _dutySince = new();
    private readonly Dictionary<int, long> _restUntil = new();
    private long _nextAssign = -1, _nudgeAt;
    private string _lastPlan = "";
    public int Rotations, WatchRescues, Assignments;
    /// <summary>컴퓨터 지휘에 대한 신뢰 (v13.3에서 헛경보·오판으로 오르내린다).</summary>
    public float ComputerTrust { get; set; } = 0.6f;

    public string CommanderName => ComputerCommands ? "주 컴퓨터" : Commander?.Name ?? "-";
    public Team? TeamOf(CrewMember c) => _teamOf.TryGetValue(c.Id, out var t) ? t : null;
    public bool Resting(CrewMember c) => _restUntil.TryGetValue(c.Id, out var t) && _w.Tick < t;

    private CrewMember? Find(int id) => id < 0 ? null : _w.Crew.FirstOrDefault(c => c.Id == id);

    /// <summary>시스템 틱.</summary>
    public void Update(float dt)
    {
        var w = _w;
        if (CaptainId < 0 && w.Crew.Any(c => !c.Dead && !c.IsChild))
            Appoint(Council.Decider(w, true) ?? w.Crew.First(c => !c.Dead), "첫 출항");
        if (Captain == null && CaptainId >= 0)
        {
            // 선장이 죽었다 — 다음 저녁에 새로 뽑을 때까지 지휘 순서대로 대신
            CaptainsLost++;
            Trust = 0.5f;
            var acting = Council.Decider(w, true);
            if (acting != null) Appoint(acting, "선장이 숨져 지휘 순서대로 대신 맡았다");
        }

        var level = Crisis.Level(w);
        // 위기: 비상 이상이거나, 경계 중 사고 일(구조·봉합·소화·전력)이 있을 때 — 치료만으로는 조를 짜지 않는다
        bool crisis = level >= CrisisLevel.Emergency || level == CrisisLevel.Alert && w.Board.Open.Any(o => o.Urgency >= 0.9f && Group(o.Kind) is TeamKind g && g != TeamKind.Medical);
        if (!crisis)
        {
            if (Active) Stand("위기가 지나갔다 — 조를 푼다");
            return;
        }
        PickCommander();
        if (!Active)
        {
            Active = true;
            _deaths0 = w.History.Deaths;
            _nextAssign = -1;
            w.Log.Add(w.Tick, LogKind.Ship, $"현장 지휘: {CommanderName}" + (ComputerCommands ? " (V 지휘)" : $" ({StyleName(Style)})"));
        }
        // 지휘자의 솜씨가 낮으면 조를 덜 자주 고친다 (지쳤거나 예민하면 더)
        float skill = ComputerCommands ? (w.Automation.Level >= 5 ? 0.85f : 0.65f) : Commander != null ? Leadership(Commander) : 0.3f;
        // 일감이 바뀌면(새 불·새 구멍·쓰러진 사람) 곧장, 아니면 솜씨에 따라 4~8분마다 다시 짠다
        string needs = NeedSig();
        if (w.Tick >= _nextAssign || needs != _lastNeeds && w.Tick - _lastAssignTick >= SimTime.Minutes(1))
        {
            _lastNeeds = needs;
            _lastAssignTick = w.Tick;
            Assign(skill);
            _nextAssign = w.Tick + SimTime.Minutes(skill > 0.6f ? 4f : 8f);
        }
        // v13.4 목표 계층: 조를 맡았는데 딴일을 하는 사람은 2분마다 다시 본다 (보류됐던 조의 일이 풀리면 곧장 돌아간다)
        if (w.Tick >= _nudgeAt)
        {
            _nudgeAt = w.Tick + SimTime.Minutes(2);
            foreach (var (id, t) in _teamOf)
            {
                if (t.Kind == TeamKind.Reserve || Find(id) is not CrewMember m || !m.CanAct) continue;
                bool offTask = m.Job?.Order is WorkOrder jo && Group(jo.Kind) != t.Kind && jo.Kind != WorkKind.SafetyWatch && t.Watcher != id;
                // v14.1 조원이 당직·산책·쉼처럼 일이 아닌 걸 하고 있어도 다시 본다 (감시 일이 뒤늦게 올라온 짝 등)
                bool idle = m.Job?.Activity is DutyActivity or WanderActivity or RelaxActivity or ChatActivity or PatrolActivity;
                if (offTask || idle) m.NextThinkTick = Math.Min(m.NextThinkTick, w.Tick + 1);
            }
        }
        Watch();
        Rotate();
    }

    private void PickCommander()
    {
        var w = _w;
        int mode = w.Policies["command"];
        bool computerOk = w.Automation.Present && w.Automation.MainOnline && w.Automation.Level >= 4;
        bool computer = mode == 1 ? computerOk : mode == 2 && computerOk && w.Automation.Level >= 5 && ComputerTrust >= Trust;
        CrewMember? human = null;
        if (!computer)
        {
            var cap = Captain;
            human = cap is { CanAct: true } && !cap.IsChild ? cap : Council.Decider(w, true);
        }
        if (computer != ComputerCommands || human != Commander)
        {
            ComputerCommands = computer;
            Commander = human;
            if (Active) w.Log.Add(w.Tick, LogKind.Ship, $"현장 지휘가 바뀌었다 — {CommanderName}");
        }
    }

    private int _deaths0;
    private string _lastNeeds = "";
    private long _lastAssignTick = -1;

    private string NeedSig() => string.Join(",", _w.Board.Open.Where(o => o.Urgency >= 0.85f && Group(o.Kind) is not null)
        .Select(o => $"{Group(o.Kind)}:{(Group(o.Kind) is TeamKind.Rescue or TeamKind.Medical ? o.Target.Crew?.Id : o.Target.CurrentRoom?.Id)}").Distinct().OrderBy(x => x));

    private void Stand(string why)
    {
        OnIncidentClosed(_w.History.Deaths > _deaths0);
        Active = false;
        Teams.Clear();
        _teamOf.Clear();
        _dutySince.Clear();
        _lastPlan = "";
        _w.Log.Add(_w.Tick, LogKind.Ship, $"현장 지휘({CommanderName}): {why}");
    }

    /// <summary>급한 일의 무리 (조 종류).</summary>
    public static TeamKind? Group(WorkKind k) => k switch
    {
        WorkKind.Rescue => TeamKind.Rescue,
        WorkKind.SealBreach or WorkKind.SealOffRoom or WorkKind.WeldBulkhead or WorkKind.CrankDoor => TeamKind.Breach,
        WorkKind.Extinguish => TeamKind.Fire,
        WorkKind.RestartReactor or WorkKind.StartAux or WorkKind.ManualStart or WorkKind.ResetBreaker or WorkKind.ShedLoad or WorkKind.InstallJumper
            or WorkKind.RefillCoolant or WorkKind.CloseValve or WorkKind.PatchPipe or WorkKind.RepairNet or WorkKind.BreakerOn => TeamKind.Power,
        WorkKind.Treat => TeamKind.Medical,
        _ => null,
    };

    private static Skill SkillFor(TeamKind k) => k switch
    {
        TeamKind.Power => Skill.Electrical,
        TeamKind.Medical => Skill.Medicine,
        _ => Skill.Mechanics,
    };

    private void Assign(float skill)
    {
        var w = _w;
        // 1) 할 일 무리: 방(또는 사람)마다 한 조
        var needs = new List<(TeamKind kind, Room? room, string key, float urgency, bool hazard, string detail)>();
        foreach (var o in w.Board.Open.Where(o => o.Urgency >= 0.85f))
        {
            if (Group(o.Kind) is not TeamKind kind) continue;
            if (!w.Minds.CommandKnows(o)) continue; // v13.3 지휘하는 쪽이 아직 모르는 사고
            var room = o.Target.CurrentRoom;
            string key = kind == TeamKind.Rescue || kind == TeamKind.Medical ? $"{kind}:{o.Target.Crew?.Id}" : $"{kind}:{room?.Id}";
            if (needs.Any(n => n.key == key)) continue;
            bool hazard = room != null && (WorkPlanners.Hostile(room) || w.Fire.CountIn(room) > 0);
            needs.Add((kind, room, key, o.Urgency, hazard, o.Title));
        }
        needs = needs.OrderBy(n => n.kind).ThenByDescending(n => n.urgency).ToList();

        // 2) 쓸 수 있는 사람
        var free = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside && !Resting(c) && c.Vitals.Injury < 0.5f && !w.Society.Suspended(c)).ToList();
        if (!ComputerCommands && Commander != null) free.Remove(Commander); // 사람 지휘자는 지휘에 매인다 (작은 배면 같이 뛴다)
        if (free.Count <= 2 && Commander != null && !ComputerCommands) free.Add(Commander);
        int risk = w.Policies["risktaking"]; // 0 신중(위험한 방은 2인 1조) · 1 보통(숨 쉴 수 없는 방만) · 2 과감(혼자)

        var plan = new List<Team>();
        foreach (var n in needs)
        {
            if (free.Count == 0) break;
            var sk = SkillFor(n.kind);
            float Fit(CrewMember c)
            {
                float d = n.room != null ? (c.Position - n.room.Center).Length() : 0f;
                float s = c.SkillLevel(sk) + (CrewRoles.Owns(c.Role, new WorkOrder { Kind = n.kind == TeamKind.Fire ? WorkKind.Extinguish : n.kind == TeamKind.Power ? WorkKind.ResetBreaker : WorkKind.Rescue, Skill = sk }) ? 0.15f : 0f)
                          - d / 80f - 0.4f * MathF.Max(0f, 0.35f - c.Needs.Rest) + 0.1f * c.Traits.Bravery * (n.hazard ? 1f : 0f)
                          - (c.IsAwake ? 0f : 0.6f); // v13.2 깨어 있는 사람부터 (자는 사람은 깨워야 온다)
                // 솜씨 없는 지휘자는 가끔 엉뚱한 사람을 보낸다
                return s + _rng.Range(-0.3f, 0.3f) * (1f - skill);
            }
            // 하던 사람은 그대로 (조가 자꾸 바뀌지 않게)
            var prev = Teams.FirstOrDefault(t => t.Key == n.key);
            // v13.4 수습 중인 새 승무원은 위험한 조에 넣지 않는다
            var pool = n.hazard && free.Any(c => !w.Society.OnProbation(c)) ? free.Where(c => !w.Society.OnProbation(c)).ToList() : free;
            var worker = prev != null && free.FirstOrDefault(c => c.Id == prev.Worker) is CrewMember pw ? pw : pool.OrderByDescending(Fit).First();
            free.Remove(worker);
            var team = new Team { Key = n.key, Kind = n.kind, Room = n.room, Worker = worker.Id, Hazard = n.hazard, Detail = n.detail };
            bool buddy = n.hazard && n.kind != TeamKind.Medical && (risk == 0 || risk == 1 && n.room != null && WorkPlanners.Unsafe(n.room));
            if (buddy && free.Count > 0)
            {
                var watcher = prev != null && free.FirstOrDefault(c => c.Id == prev.Watcher) is CrewMember pv ? pv
                    : free.OrderByDescending(c => -(n.room != null ? (c.Position - n.room.Center).Length() : 0f) / 80f + 0.2f * c.Traits.Calm - (c.IsAwake ? 0f : 0.6f)
                                                  + 0.3f * w.Relations.Trust(worker, c)).First(); // v14.4 믿는 사람과 위험한 일을 함께 한다 (두고 갔던 사람은 피한다)
                free.Remove(watcher);
                team.Watcher = watcher.Id;
            }
            plan.Add(team);
        }
        // 3) 나머지는 대기조
        foreach (var c in free) plan.Add(new Team { Key = $"reserve:{c.Id}", Kind = TeamKind.Reserve, Worker = c.Id });

        // 바뀐 것만 알린다
        string sig = string.Join("|", plan.Where(t => t.Kind != TeamKind.Reserve).Select(t => $"{t.Key}:{t.Worker}:{t.Watcher}"));
        Teams.Clear();
        Teams.AddRange(plan);
        var before = new Dictionary<int, Team>(_teamOf);
        _teamOf.Clear();
        foreach (var t in plan)
        {
            if (t.Worker >= 0) _teamOf[t.Worker] = t;
            if (t.Watcher >= 0) _teamOf[t.Watcher] = t;
        }
        // 맡은 일이 바뀐 사람은 곧장 다시 판단한다
        foreach (var (id, t) in _teamOf)
            if (!before.TryGetValue(id, out var bt) || bt.Key != t.Key || (bt.Watcher == id) != (t.Watcher == id))
                if (Find(id) is CrewMember who && t.Kind != TeamKind.Reserve) who.NextThinkTick = w.Tick;
        foreach (var t in plan.Where(t => t.Kind != TeamKind.Reserve))
            foreach (var id in new[] { t.Worker, t.Watcher })
                if (id >= 0 && !_dutySince.ContainsKey(id)) _dutySince[id] = w.Tick;
        foreach (var id in _dutySince.Keys.ToList())
            if (!_teamOf.TryGetValue(id, out var tt) || tt.Kind == TeamKind.Reserve) _dutySince.Remove(id);
        if (sig == _lastPlan) return;
        _lastPlan = sig;
        Assignments++;
        string Name(int id) => Find(id)?.Name ?? "?";
        var parts = plan.Where(t => t.Kind != TeamKind.Reserve).Select(t => $"{TeamName(t.Kind)} {Name(t.Worker)}" + (t.Watcher >= 0 ? $"(감시 {Name(t.Watcher)})" : "") + (t.Room != null ? $" → {t.Room.Name}" : "")).ToList();
        var reserve = plan.Where(t => t.Kind == TeamKind.Reserve).Select(t => Name(t.Worker)).ToList();
        string text = $"현장 지휘({CommanderName}{(ComputerCommands ? "" : $", {StyleName(Style)}")}): " + (parts.Count > 0 ? string.Join(" · ", parts) : "할 조가 없다") + (reserve.Count > 0 ? $" · 대기 {string.Join("·", reserve)}" : "");
        w.Log.Add(w.Tick, LogKind.Ship, text);
        if (ComputerCommands) w.Automation.Reason($"cmd:{Assignments}", text, 0);
    }

    public static string TeamName(TeamKind k) => k switch
    {
        TeamKind.Rescue => "구조조", TeamKind.Breach => "봉합조", TeamKind.Fire => "소화조", TeamKind.Power => "전력조", TeamKind.Medical => "치료조", _ => "대기조",
    };

    /// <summary>안전 감시: 일하는 사람이 위험한 방에서 쓰러지면 감시자가 바로 끌어낸다.</summary>
    private void Watch()
    {
        var w = _w;
        foreach (var t in Teams.Where(t => t.Watcher >= 0 && t.Room != null))
        {
            var worker = Find(t.Worker);
            var watcher = Find(t.Watcher);
            if (worker == null || watcher == null || !watcher.CanAct) continue;
            if (worker.Down && !worker.Dead && worker.Room == t.Room && worker.CarriedBy == null && watcher.Job?.Order?.Kind != WorkKind.Rescue)
            {
                // 감시자가 곧장 다시 판단하게 — 구조 일감이 그 사람에게 가장 급하다 (Bias)
                watcher.NextThinkTick = w.Tick;
                if (watcher.Job?.Order?.Kind == WorkKind.SafetyWatch) watcher.EndJob(w, ToilStatus.Interrupted);
                w.Board.RequestScan();
            }
        }
    }

    /// <summary>교대: 비상 일을 오래 했거나 지친 사람을 대기조와 바꿔 재운다.</summary>
    private void Rotate()
    {
        var w = _w;
        float hours = w.Policies["rotation"] switch { 0 => 2f, 1 => 4f, _ => float.PositiveInfinity };
        foreach (var (id, since) in _dutySince.ToList())
        {
            var c = Find(id);
            if (c == null || Resting(c)) continue;
            bool long_ = (w.Tick - since) > SimTime.Hours(hours);
            bool spent = c.Needs.Rest < 0.18f;
            if (!long_ && !spent) continue;
            // 바꿔 줄 사람이 있어야 쉰다 (없으면 버틴다 — 끝까지면 바꾸지 않는다)
            bool relief = Teams.Any(t => t.Kind == TeamKind.Reserve && Find(t.Worker) is { Needs.Rest: > 0.45f });
            if (!relief && !(spent && c.Needs.Rest < 0.08f)) continue;
            if (float.IsInfinity(hours) && !spent) continue;
            _restUntil[id] = w.Tick + SimTime.Hours(2.5f);
            _dutySince.Remove(id);
            Rotations++;
            c.EndJob(w, ToilStatus.Interrupted);
            c.NextThinkTick = w.Tick;
            _nextAssign = w.Tick; // 빈자리를 다시 채운다
            w.Log.Add(w.Tick, LogKind.Life, $"교대 — {CommanderName}의 지시로 두 시간 반 눈을 붙인다" + (spent ? " (지쳤다)" : $" ({hours:0}시간 근무)"), c.Id);
        }
    }

    /// <summary>작업 점수 보정: 맡은 조의 일은 앞으로, 남의 조 일은 뒤로 (선장 스타일만큼), 감시자는 제 짝을 구한다.</summary>
    public float Bias(CrewMember c, WorkOrder o)
    {
        if (!Active || !_teamOf.TryGetValue(c.Id, out var t)) return 0f;
        // v13.4 목표 계층(맡은 역할 > 일): 조의 일에 끌리는 힘을 키웠다
        float k = ComputerCommands ? 0.55f : Style switch { CaptainStyle.Authoritarian => 0.7f, CaptainStyle.Consultative => 0.55f, _ => 0.28f };
        k *= _w.Minds.Obedience(c); // v13.3 명령을 따르는 정도
        var group = Group(o.Kind);
        bool mine = group == t.Kind && (t.Room == null || o.Target.CurrentRoom == t.Room || t.Kind is TeamKind.Rescue or TeamKind.Medical && o.Target.Crew?.Id.ToString() == t.Key.Split(':')[1]);
        // 감시자: 제 짝이 쓰러지면 가장 급하다
        if (t.Watcher == c.Id)
        {
            if (o.Kind == WorkKind.Rescue && o.Target.Crew?.Id == t.Worker) return 1.4f;
            if (o.Kind == WorkKind.SafetyWatch && o.Circuit == t.Worker) return 0.9f;
            return o.Urgency >= 0.8f ? -0.6f : -0.3f; // 감시를 두고 다른 일로 가지 않는다 (짝을 혼자 두지 않는다)
        }
        if (t.Kind == TeamKind.Reserve)
        {
            // 대기조: 다른 조가 맡은 위험한 일에는 끼어들지 않는다 (사람이 몰리지 않게)
            if (group != null && o.Target.CurrentRoom is Room r && Teams.Any(x => x.Kind == group && x.Room == r && x.Kind != TeamKind.Reserve)) return -0.3f * k / 0.35f;
            return 0f;
        }
        if (mine) return k;
        if (group != null && o.Urgency >= 0.9f) return -0.6f * k; // 다른 조 몫
        return -0.2f * k; // 조를 맡았으면 딴일은 나중
    }

    // ── 저녁: 신뢰 · 불신임 · 선거 ──

    /// <summary>신뢰를 움직이는 일 (사망·성공한 대응·선장의 실수).</summary>
    public void OnDeath(CrewMember dead)
    {
        if (dead.Id == CaptainId) return;
        Trust = MathF.Max(0f, Trust - (Active ? 0.12f : 0.05f));
    }

    public void OnIncidentClosed(bool deaths)
    {
        _w.Meetings.OnIncidentClosed(deaths); // v13.2 결정의 무게
        if (!deaths) Trust = MathF.Min(1f, Trust + 0.03f);
    }

    /// <summary>v13.2 정기 회의의 안건: 신뢰가 무너진 선장을 불신임한다 (토론 · 표결 · 방침의 문턱).</summary>
    public AgendaItem VoteNoConfidence(List<CrewMember> voters, MeetingSystem m)
    {
        var w = _w;
        var cap = Captain!;
        NoConfidence++;
        var item = new AgendaItem { Title = $"선장 {cap.Name} 불신임", Topic = "captain" };
        (float, string) Opinion(CrewMember c)
        {
            if (c == cap) return (-1f, "끝까지 맡겠다");
            var terms = new List<(float v, string why)>
            {
                (0.45f - Trust, Trust < 0.2f ? "아무도 선장을 믿지 않는다" : "믿음이 무너졌다"),
                (-0.5f * c.AffinityTo(cap), c.AffinityTo(cap) > 0.2f ? "선장 편이다" : "선장과 사이가 나쁘다"),
                (m.Guilt(cap) > 0.2f ? 0.15f : 0f, "선장이 정한 일로 사람이 죽었다"),
                (c.Value == CrewValue.Rules ? -0.1f : 0f, "선장을 함부로 바꾸면 안 된다"),
            };
            float s = terms.Sum(t => t.v);
            return (s, s > 0f ? terms.Where(t => t.v > 0f).OrderByDescending(t => t.v).First().why : terms.Where(t => t.v <= 0f).OrderBy(t => t.v).Select(t => t.why).DefaultIfEmpty("그대로 두자").First());
        }
        var (yes, no) = m.Debate(voters, Opinion, Leadership, item, null);
        int need = w.Policies["noconfidence"] == 0 ? voters.Count / 2 + 1 : (int)MathF.Ceiling(voters.Count * 2f / 3f);
        bool pass = yes.Count >= need;
        item.Passed = pass;
        item.Outcome = pass ? "가결" : "부결";
        w.History.Add(w, HistoryKind.Decision, $"정기 회의: 선장 {cap.Name} 불신임 — 찬성 {yes.Count} · 필요 {need} → {(pass ? "가결" : "부결")} (신뢰 {Trust * 100:0}%)"
            + (item.FlippedBy != null ? $" · {item.FlippedBy}의 설득으로 뒤집혔다" : ""), null, voters, log: true);
        m.Split(yes, no);
        if (!pass) { Trust = MathF.Min(1f, Trust + 0.08f); return item; }
        Elect(voters, cap);
        return item;
    }

    /// <summary>선장 선거 (방침: 직책 순서 · 다수결 · 경력).</summary>
    public void Elect(List<CrewMember> voters, CrewMember? old)
    {
        var w = _w;
        Elections++;
        var candidates = voters.Where(c => c != old).ToList();
        if (candidates.Count == 0) return;
        CrewMember pick;
        int mode = w.Policies["election"];
        string how;
        if (mode == 0) { pick = Council.Decider(w, true) is CrewMember d && d != old ? d : candidates.OrderByDescending(Leadership).First(); how = "지휘 순서대로"; }
        else if (mode == 2) { pick = candidates.OrderByDescending(c => c.Stats.Repairs + c.Stats.Rescues * 3 + (int)(c.Age)).First(); how = "경력대로"; }
        else
        {
            var tally = new Dictionary<CrewMember, int>();
            foreach (var v in voters)
            {
                var choice = candidates.Where(c => c != v || candidates.Count == 1).OrderByDescending(c => Leadership(c) + (c == v ? 0f : v.AffinityTo(c))).First();
                tally[choice] = tally.GetValueOrDefault(choice) + 1;
            }
            pick = tally.OrderByDescending(kv => kv.Value).ThenByDescending(kv => Leadership(kv.Key)).First().Key;
            how = $"다수결 {tally[pick]}표";
        }
        if (old != null)
        {
            old.Needs.Stress = MathF.Min(1f, old.Needs.Stress + 0.15f);
            Life.Diary(w, old, "선장 자리를 내려놓았다.");
        }
        Trust = 0.6f;
        Appoint(pick, $"새 선장 ({how})");
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v13.1 2인 1조: 위험한 방에서 일하는 사람의 짝은 문 밖에서 지킨다.</summary>
    private void ScanSafetyWatch(Poster post)
    {
        var w = _world;
        var cmd = w.Command;
        if (!cmd.Active) return;
        foreach (var t in cmd.Teams.Where(t => t.Watcher >= 0 && t.Room != null))
        {
            var room = t.Room!;
            var worker = w.Crew.FirstOrDefault(c => c.Id == t.Worker);
            if (worker == null || worker.Room != room && worker.Job?.Order?.Target.CurrentRoom != room) continue; // 짝이 그 방에 있거나 가는 중일 때만
            // 문 밖 자리: 그 방 문 옆, 바깥쪽 바닥 (일하는 사람에게 가까운 문)
            Cell? spot = null;
            float best = float.MaxValue;
            foreach (var d in room.Doors)
            {
                if (d.IsExternal || d.Removed) continue;
                var other = d.RoomA == room ? d.RoomB : d.RoomA;
                if (other == null || other.Detached) continue;
                foreach (var dir in Cell.Dirs4)
                {
                    var c = d.Cell + dir;
                    if (w.Ship.RoomAt(c) != other || !w.Ship.IsOpenFloor(c)) continue;
                    float dist = worker != null ? (c.Center - worker.Position).Length() : 0f;
                    if (dist < best) { best = dist; spot = c; }
                }
            }
            if (spot is not Cell s) continue;
            post(WorkKind.SafetyWatch, WorkTarget.AtCell(s, room), 0.92f, Skill.Medicine, $"{worker?.Name ?? "?"}의 짝 — 쓰러지면 바로 끌어낸다", circuit: t.Worker);
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>v13.1 문 밖에서 짝을 지킨다 (짝이 나오거나 쓰러지거나 위기가 끝날 때까지).</summary>
    private static Job? SafetyWatch(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.CurrentRoom;
        int buddy = o.Circuit;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WaitToil(SimTime.Minutes(30), Pose.Standing, room?.Center)
        {
            DoneWhen = (cm, world) =>
            {
                var b = world.Crew.FirstOrDefault(x => x.Id == buddy);
                return !world.Command.Active || world.Command.TeamOf(cm)?.Watcher != cm.Id || b == null || b.Dead || b.Down || b.Room != room;
            },
        });
        toils.Add(new DoToil((cm, world) => { world.Board.Close(o); return true; }));
        var bn = w.Crew.FirstOrDefault(x => x.Id == buddy)?.Name ?? "?";
        return Wrap(a, o, c, w, "안전 감시", toils, $"{room?.Name ?? "?"} 문 밖에서 {Ko.EulReul(bn)} 지킨다 (2인 1조)");
    }
}
