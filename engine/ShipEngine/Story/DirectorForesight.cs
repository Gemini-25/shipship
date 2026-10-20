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
        if (!Foresee) { _ahead = null; return; }
        if (t % 5 == 0 && Brewing(sim, t)) _lastBrew = t;
        if (t - _lastBrew > Time.Minutes(5)) { _ahead = null; return; }   // 5분 조용하면 놓는다
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // 어긋남: 앞선 세계가 이 틱을 마쳤을 때의 사실 수와 진짜 세계의 사실 수가 다르면 다시 복제
        while (_aheadTotals.Count > 0 && _aheadTotals.Peek().tick < t) _aheadTotals.Dequeue();
        if (_ahead != null && (_ahead.Tick < t || _aheadTotals.Count > 0 && _aheadTotals.Peek().tick == t && _aheadTotals.Peek().facts != sim.Facts.Total))
        { _ahead = null; Resyncs++; }
        if (_ahead == null)
        {
            _ahead = sim.Clone(); _aheadCursor = _ahead.Facts.Total; _aheadTotals.Clear();
            _future.Clear(); Foresights++;
        }
        var mind = _ahead.Find<Mind>();
        int budget = AheadPerTick;
        while (_ahead.Tick < t + ForeseeTicks && budget-- > 0)
        {
            _ahead.Step(); AheadSteps++;
            foreach (var f in _ahead.Facts.Since(_aheadCursor))
            {
                float wt = WeightOf(f);
                if (wt < WatchAt) continue;
                int cell = f.Cell;
                if (cell < 0) { int a = AgentOf(mind, f); if (a >= 0) cell = mind!.Agents[a].C.Cell; }
                if (cell < 0 || cell >= Heat.Length) continue;
                _future.Add((f.Tick, cell, wt, f.Kind));
                Predicted++;
            }
            _aheadCursor = _ahead.Facts.Total;
            _aheadTotals.Enqueue((_ahead.Tick, _ahead.Facts.Total));
        }
        ForeseeMs += clock.Elapsed.TotalMilliseconds;
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
