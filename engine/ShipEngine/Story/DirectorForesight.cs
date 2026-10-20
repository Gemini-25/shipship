namespace ShipEngine;

/// <summary>
/// ⑬ 연출 — 미리 보기 (깊이 바퀴 2 · docs/ENGINE.md ⑬ '조짐 보기'): 일이 벌어질 듯한 동안(다가오는 운석 · 파편 구름 · 불 · 주컴퓨터 사건 · 조짐 · 위험)
/// 세계를 한 번 복제해 1분 30초 앞에 두고, 진짜 세계가 한 틱 갈 때 앞선 세계도 한 틱 간다 — 결정론이라 앞선 세계의 일이 그대로 일어난다 (함장이 손대지 않으면).
/// 사실 수가 어긋나면(함장의 손 · 시험이 넣은 사실) 다시 복제한다 · 5분 조용하면 놓는다.
/// 큰 일(무게 20 넘음)이 날 칸에 그 일 50초 전부터 카메라만 쓰는 관심을 쌓고 · 결정적인 일이면 시간을 늦추고 · 15초 전에 화면 밖이면 그리로 간다.
/// 앞선 세계는 연출만 본다 (원래 세계를 바꾸지 않는다).
/// </summary>
public sealed partial class Director
{
    public bool Foresee = true;
    public const int ForeseeTicks = 38, LeadTicks = 20, CutLeadTicks = 6;   // 1.5분 앞 · 50초 전부터 · 15초 전에 자른다
    /// <summary>한 틱에 앞선 세계를 많아야 이만큼 돌린다 — 복제 직후 1.5분 앞까지 한꺼번에 돌리면 화면이 멈칫한다 (새터호 약 0.2초) · 틱마다 둘씩 더 가며 19틱 안에 따라잡는다.</summary>
    public const int AheadPerTick = 3;
    public int Foresights, Predicted, ForeCuts, Resyncs;
    public long AheadSteps;
    public double ForeseeMs;
    public readonly Dictionary<string, int> BrewWhy = new();
    private Sim? _ahead;
    private long _aheadCursor, _lastBrew = long.MinValue / 2, _foreHold;
    private readonly Queue<(long tick, long facts)> _aheadTotals = new();   // 앞선 세계가 그 틱을 마쳤을 때 사실 수 (어긋남 찾기)
    private readonly List<(long tick, int cell, float w, FactKind kind)> _future = new();

    /// <summary>
    /// 앞선 세계의 다음 틱 몫을 다른 코어에서 미리 돌린다 (관전 화면 — 진짜 세계가 한 틱 가는 동안 · 다음 Observe가 기다려 받는다).
    /// 받는 차례 · 몫이 차례로 돌린 것과 같아 카메라도 같다 (규칙 '빠른 길'). 앞선 세계는 진짜 세계와 아무것도 함께 쓰지 않는다 (배 도면 · 표는 읽기만).
    /// </summary>
    public bool AheadInBackground;
    private System.Threading.Tasks.Task? _aheadTask;
    private readonly List<(long tick, int cell, float w, FactKind kind)> _pendFuture = new();
    private readonly List<(long tick, long facts)> _pendTotals = new();
    private int _pendSteps, _pendPredicted;
    private long _pendCursor;

    /// <summary>앞선 세계를 target 틱 앞까지 · 많아야 budget틱 돌리며 본 일 · 틱마다 사실 수를 모은다 (진짜 세계 · 연출의 다른 상태는 건드리지 않는다).</summary>
    private int StepAhead(Sim ahead, long target, int budget, ref long cursor, List<(long tick, int cell, float w, FactKind kind)> future, ICollection<(long tick, long facts)> totals, ref int predicted)
    {
        var mind = ahead.Find<Mind>();
        int steps = 0;
        while (ahead.Tick < target && budget-- > 0)
        {
            ahead.Step(); steps++;
            foreach (var f in ahead.Facts.Since(cursor))
            {
                float wt = WeightOf(f);
                if (wt < WatchAt) continue;
                int cell = f.Cell;
                if (cell < 0) { int a = AgentOf(mind, f); if (a >= 0) cell = mind!.Agents[a].C.Cell; }
                if (cell < 0 || cell >= Heat.Length) continue;
                future.Add((f.Tick, cell, wt, f.Kind));
                predicted++;
            }
            cursor = ahead.Facts.Total;
            totals.Add((ahead.Tick, ahead.Facts.Total));
        }
        return steps;
    }

    /// <summary>미리 돌린 몫을 기다린다 (없으면 그냥).</summary>
    private void JoinAhead() { if (_aheadTask != null) { _aheadTask.Wait(); _aheadTask = null; } }

    private void DropPending() { _pendFuture.Clear(); _pendTotals.Clear(); _pendSteps = 0; _pendPredicted = 0; }

    /// <summary>일이 벌어질 듯한가: 최근의 조짐 · 위험 · 불 · 파편 구름 · 다가오는 운석 · 주컴퓨터 사건.</summary>
    private bool Brewing(Sim sim, long t)
    {
        string? why = null;
        if (t - _lastOmen < Time.Minutes(3)) why = "조짐";
        else if (t - _lastDanger < Time.Minutes(1)) why = "위험";
        else if ((sim.Find<Chemistry>()?.BurningCount ?? 0) > 0) why = "불";
        else if (sim.Find<Outside>() is { } o && o.Clouds.Count > 0) why = "구름";
        else if (sim.Find<Outside>() is { } o2 && o2.Rocks.Exists(r => r.Mass > 0 && r.Warned == 1)) why = "운석";
        else if (sim.Find<ShipMind>() is { } cm && cm.Incidents.Exists(x => x.Active != 0)) why = "사건";
        else if (sim.Find<Outside>() is { } o3 && (o3.Burning != BurnKind.None || o3.Plan.Exists(p => p.Due - t < Time.Minutes(4)))) why = "점화";
        else if (sim.Find<ShipMind>() is { } cm2 && cm2.GoingTo >= 0) why = "다른 배";
        if (why != null) BrewWhy[why] = BrewWhy.GetValueOrDefault(why) + 1;
        return why != null;
    }

    private void Foresight(Sim sim, long t)
    {
        _future.RemoveAll(p => p.tick <= t);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        JoinAhead();
        bool pending = _pendSteps > 0;
        if (!Foresee) { _ahead = null; DropPending(); return; }
        if (t % 5 == 0 && Brewing(sim, t)) _lastBrew = t;
        if (t - _lastBrew > Time.Minutes(5)) { _ahead = null; DropPending(); return; }   // 5분 조용하면 놓는다
        // 어긋남: 앞선 세계가 이 틱을 마쳤을 때의 사실 수와 진짜 세계의 사실 수가 다르면 다시 복제
        while (_aheadTotals.Count > 0 && _aheadTotals.Peek().tick < t) _aheadTotals.Dequeue();
        if (_ahead != null && (_ahead.Tick < t || _aheadTotals.Count > 0 && _aheadTotals.Peek().tick == t && _aheadTotals.Peek().facts != sim.Facts.Total))
        { _ahead = null; Resyncs++; }
        int budget = AheadPerTick;
        if (_ahead == null)
        {
            _ahead = sim.Clone(); _aheadCursor = _ahead.Facts.Total; _aheadTotals.Clear();
            _future.Clear(); Foresights++;
            DropPending();   // 미리 돌린 몫은 버린 앞선 세계의 것
        }
        else if (pending)
        {
            // 이 틱 몫을 다른 코어에서 미리 돌려 두었다 — 차례로 돌린 것과 같은 순서로 받는다
            _future.AddRange(_pendFuture);
            foreach (var x in _pendTotals) _aheadTotals.Enqueue(x);
            _aheadCursor = _pendCursor; AheadSteps += _pendSteps; Predicted += _pendPredicted; budget -= _pendSteps;
            DropPending();
        }
        int predicted = 0;
        AheadSteps += StepAhead(_ahead, t + ForeseeTicks, budget, ref _aheadCursor, _future, new QueueAdder(_aheadTotals), ref predicted);
        Predicted += predicted;
        if (AheadInBackground)
        {
            // 다음 틱 몫 (진짜 세계가 한 틱 가는 동안)
            var ahead = _ahead; long target = t + 1 + ForeseeTicks; _pendCursor = _aheadCursor;
            _aheadTask = System.Threading.Tasks.Task.Run(() => { _pendSteps = StepAhead(ahead, target, AheadPerTick, ref _pendCursor, _pendFuture, _pendTotals, ref _pendPredicted); });
        }
        ForeseeMs += clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>큐에 차례로 넣는 모음 (StepAhead가 큐 · 목록에 똑같이 쓴다).</summary>
    private sealed class QueueAdder : ICollection<(long tick, long facts)>
    {
        private readonly Queue<(long tick, long facts)> _q;
        public QueueAdder(Queue<(long tick, long facts)> q) => _q = q;
        public void Add((long tick, long facts) x) => _q.Enqueue(x);
        public int Count => _q.Count;
        public bool IsReadOnly => false;
        public void Clear() => _q.Clear();
        public bool Contains((long tick, long facts) x) => _q.Contains(x);
        public void CopyTo((long tick, long facts)[] a, int i) => _q.CopyTo(a, i);
        public bool Remove((long tick, long facts) x) => throw new NotSupportedException();
        public IEnumerator<(long tick, long facts)> GetEnumerator() => _q.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _q.GetEnumerator();
    }

    /// <summary>미리 본 앞날을 카메라에: 50초 전부터 그 칸에 관심 · 결정적인 일이면 조짐(느리게) · 15초 전에 화면 밖이면 자른다.</summary>
    private void Anticipate(Sim sim, long t)
    {
        int cutTo = -1; float best = 0; long when = 0;
        foreach (var p in _future)
        {
            long ahead = p.tick - t;
            if (ahead <= 0 || ahead > LeadTicks) continue;
            Aux[p.cell] += p.w * 0.08f;
            bool crit = Critical(p.kind);
            if (crit) _lastOmen = t;
            if (ahead <= CutLeadTicks && (crit || p.w >= 30) && p.w > best && !InView(p.cell)) { best = p.w; cutTo = p.cell; when = p.tick; }
        }
        if (cutTo >= 0 && _criticalLeft <= 0 && (_resting || _sinceSwitch >= PullHoldSec))
        {
            CutTo(sim, cutTo, t); ForeCuts++;
            _foreHold = when + 2;   // 그 일이 날 때까지는 다른 곳으로 되돌아 자르지 않는다
        }
    }
}
