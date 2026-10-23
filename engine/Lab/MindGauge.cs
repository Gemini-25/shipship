using ShipEngine;

namespace Lab;

/// <summary>
/// 판단 눈금 (docs/REVIEW.md ③-8 · 읽기만 — 세계 지문은 그대로): 판정이 판단 결함을 이름으로 가리게 한다.
/// 옛 눈금(`Failed >= 5`)은 계획마다 0으로 돌아가 늘 0이었다. 여기서는 틱마다 판단 엔진의 겉을 보고 센다:
/// 흔들림 (3분 안에 A → B → A → B) · 실패 뒤 30분 안에 같은 (목표 · 대상)을 다시 세움 · 맡은 일을 두고 다른 것을 한 로봇 ·
/// 일감 없이 노는 로봇 · 기한을 넘겨 내린 일감 · 같은 사람의 사건이 30분 안에 새로 섬.
/// </summary>
public sealed class MindGauge
{
    public static readonly long FlipWindow = Time.Minutes(3), RepeatWindow = Time.Minutes(30);

    public long FlipTicks;          // 흔들린 사람 · 틱
    public int Flips;               // 흔들림 수
    public int Fails, FailRepeats;  // 실패 · 그 뒤 30분 안에 같은 (목표 · 대상)을 다시 세운 수
    public long RobotOffJob;        // 맡은 일이 있는데 다른 것을 한 로봇 · 틱
    public long RobotIdle, RobotUp; // 일감 없이 놀거나 충전(전지 > 0.6)한 로봇 · 틱 / 켜진 로봇 · 틱
    public int JobsDone, JobsOverdue;
    public int PersonReraised;      // 같은 사람의 사건이 30분 안에 새로 선 수
    public readonly Dictionary<string, int> FlipWho = new();   // 이름 → 흔들림 수 (보고용)

    private readonly Mind _m;
    private readonly ShipMind _sm;
    private long[] _since = Array.Empty<long>();
    private string[] _fail = Array.Empty<string>();
    private GoalKind[] _goal = Array.Empty<GoalKind>();
    private int[] _target = Array.Empty<int>();
    private (GoalKind g, int t, long at)[][] _hist = Array.Empty<(GoalKind, int, long)[]>();
    private int[] _histN = Array.Empty<int>();
    private readonly Dictionary<(int, GoalKind, int), long> _failAt = new();
    private readonly Dictionary<(GoalKind, int, int), long> _jobs = new();   // (종류 · 대상 · 맡은 이) → 기한
    private readonly Dictionary<(IncidentKind, int), (int id, long seen)> _person = new();
    private readonly HashSet<(GoalKind, int, int)> _cur = new();

    public MindGauge(Sim s) { _m = s.Get<Mind>(); _sm = s.Get<ShipMind>(); }

    /// <summary>흔들림: 마지막 네 (목표 · 대상)이 A · B · A · B이고 3분 안이면 (쉼은 빼고).</summary>
    public static bool IsFlip((GoalKind g, int t, long at) a, (GoalKind g, int t, long at) b, (GoalKind g, int t, long at) c, (GoalKind g, int t, long at) d)
        => a.g == c.g && a.t == c.t && b.g == d.g && b.t == d.t && (a.g != b.g || a.t != b.t)
           && a.g != GoalKind.Idle && b.g != GoalKind.Idle && d.at - a.at < FlipWindow;

    private void Grow(int n)
    {
        if (_since.Length >= n) return;
        int o = _since.Length;
        Array.Resize(ref _since, n); Array.Resize(ref _fail, n); Array.Resize(ref _goal, n); Array.Resize(ref _target, n);
        Array.Resize(ref _hist, n); Array.Resize(ref _histN, n);
        for (int i = o; i < n; i++) { _since[i] = -1; _fail[i] = ""; _hist[i] = new (GoalKind, int, long)[4]; }
    }

    public void Observe(long now)
    {
        int n = _m.Agents.Count;
        Grow(n);
        for (int i = 0; i < n; i++)
        {
            var a = _m.Agents[i];
            ref var c = ref a.C;
            if (!ReferenceEquals(a.LastFail, _fail[i]))
            {
                _fail[i] = a.LastFail;
                if (a.LastFail != "") { Fails++; _failAt[(i, _goal[i], _target[i])] = now; }
            }
            if (c.GoalSince != _since[i])
            {
                _since[i] = c.GoalSince;
                var h = _hist[i];
                if (_histN[i] == 4) { h[0] = h[1]; h[1] = h[2]; h[2] = h[3]; _histN[i] = 3; }
                h[_histN[i]++] = (c.Goal, c.GoalTarget, now);
                if (_histN[i] == 4 && IsFlip(h[0], h[1], h[2], h[3]))
                {
                    Flips++; FlipTicks += h[3].at - h[2].at;
                    string who = _m.NameOf(i);
                    FlipWho[who] = FlipWho.GetValueOrDefault(who) + 1;
                }
                if (_failAt.TryGetValue((i, c.Goal, c.GoalTarget), out long ft) && now - ft < RepeatWindow) FailRepeats++;
            }
            _goal[i] = c.Goal; _target[i] = c.GoalTarget;
            if (c.Kind == AgentKind.Robot && c.Up != 0)
            {
                RobotUp++;
                if (c.AssignedUntil > now && c.AssignedGoal != GoalKind.Idle && c.Goal != c.AssignedGoal) RobotOffJob++;
                if (c.Goal is GoalKind.Idle or GoalKind.Charge && c.Battery > 0.6f && c.AssignedUntil <= now) RobotIdle++;
            }
        }
        if (now % 125 != 8) return;   // 일감 · 사건은 주컴퓨터가 몇 분마다 바꾼다 — 125틱마다 보면 된다
        _cur.Clear();
        foreach (var j in _sm.Jobs)
        {
            var k = (j.Kind, j.Target, j.Agent);
            _cur.Add(k);
            _jobs.TryAdd(k, j.Until);
        }
        foreach (var (k, until) in _jobs.ToList())
        {
            if (_cur.Contains(k)) continue;
            if (now >= until) JobsOverdue++; else JobsDone++;
            _jobs.Remove(k);
        }
        foreach (var x in _sm.Incidents)
        {
            if (x.Active == 0 || x.Kind is not (IncidentKind.PersonDown or IncidentKind.Injured or IncidentKind.Arrest or IncidentKind.Surgery or IncidentKind.Counsel)) continue;
            var key = (x.Kind, x.Subject);
            if (_person.TryGetValue(key, out var p) && p.id != x.Id && now - p.seen < RepeatWindow) PersonReraised++;
            _person[key] = (x.Id, now);
        }
    }

    public double FlipMinutes => FlipTicks * Time.SecondsPerTick / 60;
    public double RobotIdleShare => RobotUp == 0 ? 0 : (double)RobotIdle / RobotUp;
    public double RobotOffJobMinutes => RobotOffJob * Time.SecondsPerTick / 60;

    public string Line() =>
        $"판단 눈금 — 흔들림 {Flips}번 · {FlipMinutes:0}분{(FlipWho.Count > 0 ? " (" + string.Join(" ", FlipWho.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value}")) + ")" : "")}"
        + $" · 실패 {Fails} (30분 안에 같은 것 다시 {FailRepeats}) · 맡은 일을 두고 딴 일 한 로봇 {RobotOffJobMinutes:0}분 · 로봇 놂 {RobotIdleShare:P0}"
        + $" · 일감 끝 {JobsDone} · 기한 넘김 {JobsOverdue} · 같은 사람 사건 다시 섬 {PersonReraised}";

    public string Json() => FormattableString.Invariant(
        $"\"flips\":{Flips},\"flipMin\":{FlipMinutes:0},\"fails\":{Fails},\"failRepeats\":{FailRepeats},\"robotOffJobMin\":{RobotOffJobMinutes:0},\"robotIdle\":{RobotIdleShare:0.000},\"jobsDone\":{JobsDone},\"jobsOverdue\":{JobsOverdue},\"personReraised\":{PersonReraised}");
}
