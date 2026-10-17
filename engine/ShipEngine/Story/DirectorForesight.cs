namespace ShipEngine;

/// <summary>
/// ⑬ 연출 — 미리 보기 (깊이 바퀴 2 · docs/ENGINE.md ⑬ '조짐 보기'): 일이 벌어질 듯한 동안(다가오는 운석 · 파편 구름 · 불 · 주컴퓨터 사건 · 조짐 · 위험)
/// 1분마다 세계를 복제해 1분 30초 앞을 돌려 본다 — 결정론이라 복제 세계의 미래가 그대로 일어난다 (함장이 손대지 않으면).
/// 큰 일(무게 20 넘음)이 날 칸에 그 일 50초 전부터 카메라만 쓰는 관심을 쌓고 · 결정적인 일이면 시간을 늦추고 · 15초 전에 화면 밖이면 그리로 간다.
/// 복제 세계는 연출만 본다 (원래 세계를 바꾸지 않는다).
/// </summary>
public sealed partial class Director
{
    public bool Foresee = true;
    public const int ForeseeTicks = 38, ForeseeEvery = 25, LeadTicks = 20, CutLeadTicks = 6;   // 1.5분 앞 · 1분마다 · 50초 전부터 · 15초 전에 자른다
    public int Foresights, Predicted, ForeCuts;
    public double ForeseeMs;
    private long _lastForesee = long.MinValue / 2, _foreHold;
    private readonly List<(long tick, int cell, float w, FactKind kind)> _future = new();

    /// <summary>일이 벌어질 듯한가 (미리 볼 만한가): 다가오는 운석 · 파편 구름 · 불 · 주컴퓨터의 사건 · 최근의 조짐 · 위험.</summary>
    private bool Brewing(Sim sim, long t)
    {
        if (t - _lastOmen < Time.Minutes(3) || t - _lastDanger < Time.Minutes(1)) return true;
        if ((sim.Find<Chemistry>()?.BurningCount ?? 0) > 0) return true;
        var o = sim.Find<Outside>();
        if (o != null && (o.Clouds.Count > 0 || o.Rocks.Exists(r => r.Mass > 0 && r.Warned == 1))) return true;
        var cm = sim.Find<ShipMind>();
        return cm != null && cm.Incidents.Exists(x => x.Active != 0);
    }

    private void Foresight(Sim sim, long t)
    {
        _future.RemoveAll(p => p.tick <= t);
        if (!Foresee || t - _lastForesee < ForeseeEvery || !Brewing(sim, t)) return;
        _lastForesee = t;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var w = sim.Clone();
        var mind = w.Find<Mind>();
        long cursor = w.Facts.Total;
        long seenUntil = _future.Count > 0 ? _future.Max(p => p.tick) : t;   // 앞서 본 것과 겹치는 앞날은 다시 적지 않는다
        for (int i = 0; i < ForeseeTicks; i++)
        {
            w.Step();
            foreach (var f in w.Facts.Since(cursor))
            {
                if (f.Tick <= seenUntil) continue;
                float wt = WeightOf(f);
                if (wt < WatchAt) continue;
                int cell = f.Cell;
                if (cell < 0) { int a = AgentOf(mind, f); if (a >= 0) cell = mind!.Agents[a].C.Cell; }
                if (cell < 0 || cell >= Heat.Length) continue;
                _future.Add((f.Tick, cell, wt, f.Kind));
                Predicted++;
            }
            cursor = w.Facts.Total;
        }
        Foresights++;
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
