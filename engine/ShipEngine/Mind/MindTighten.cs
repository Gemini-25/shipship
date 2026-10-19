namespace ShipEngine;

/// <summary>④ 열 · 진동 (docs/ENGINE.md ④ '열 · 진동'): 진동에 풀려 덜컹거리는 장치의 볼트를 조인다 — 사람 10분 · 로봇 5분 · 조이면 풀림이 걷힌다 (⑥에 요청).</summary>
public sealed partial class Mind
{
    public const double TightenPersonMin = 10, TightenRobotMin = 5;

    private void TightenStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int dev = st.Target;
        if (_net == null || dev < 0 || dev >= _net.Loose.Length || _net.Loose[dev] < 1) { JobDone(a, t); return; }   // 와 보니 이미 조였다
        if (Dist(c.Cell, _net.Devices[dev].Cell) > 2) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        Activity(a, "work");
        WorkNoise(sim, a, SoundKind.Tool, 72);   // 볼트 조이기: 렌치
        if (t - c.StepStart < Time.Minutes(a.IsPerson ? TightenPersonMin : TightenRobotMin)) return;
        int parent = _net.LooseCause[dev];
        _net.Tighten(dev);
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 덜컹거리던 {_net.Devices[dev].Name}의 볼트를 조였다", t, _net.Devices[dev].Cell, parent, 0, a.Id);
        sim.Facts.Emit(t, FactKind.Repaired, _net.Devices[dev].Cell, 10, cause, a.Id, dev);
        if (a.IsPerson) c.SkRepair = Math.Min(1, c.SkRepair + 0.002f * Practice(c, "고치기") * UseSkill(a, 0, t));
        JobDone(a, t);
    }
}
