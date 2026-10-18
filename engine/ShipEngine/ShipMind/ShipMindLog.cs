namespace ShipEngine;

/// <summary>점검 뚜껑을 연 기록 하나 — 누가 · 어느 장치를 · 언제.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct Access
{
    public int Device, Agent;
    public long Tick;
}

/// <summary>
/// ⑩ 주컴퓨터 — 정비 기록 (⑧ · ⑨ 바퀴 3 · 거짓말과 속임의 증거).
/// 장치의 점검 뚜껑이 열릴 때마다 출입 표로 누가 열었는지 적는다 (고침 · 떼어 냄 · 손봄 · 몰래 손댐 — 무엇을 했는지는 모르고 열었다는 것만) · 장치마다 최근 사흘.
/// 멈춰 있던 동안은 적지 못한다 (빈 기록). 사람이 물으면(사실 '기록을 물었다') 그 장치의 기록을 그대로 보여 준다 — 도구로서 답할 뿐 누구를 탓하지 않는다.
/// </summary>
public sealed partial class ShipMind
{
    public readonly List<Access> Accesses = new();
    public long LogAnswers;
    private long _logCursor;
    public const int AccessDays = 3;

    /// <summary>사실 흐름에서 점검 뚜껑을 연 일을 적고 · 물음에 답한다 (멈춰 있으면 지나간 사실은 적지 못한다).</summary>
    private void LogStep(Sim sim, bool online)
    {
        if (_net == null) { _logCursor = sim.Facts.Total; return; }
        foreach (var f in sim.Facts.Since(_logCursor))
        {
            if (!online) continue;
            bool device = f.Other >= 0 && f.Other < _net.Devices.Count;
            switch (f.Kind)
            {
                case FactKind.Repaired or FactKind.Salvaged or FactKind.Sabotage when device && f.Actor >= 0:
                case FactKind.Mistake when device && f.Actor >= 0 && f.Size >= 2 && f.Size < 2.5f:   // 손보다 (상하게 한 것은 기록에 없다 — 열었다는 것만)
                    Accesses.Add(new Access { Device = f.Other, Agent = f.Actor, Tick = f.Tick });
                    break;
                case FactKind.LogAsked when device:
                    Answer(sim, f);
                    break;
            }
        }
        _logCursor = sim.Facts.Total;
        if (sim.Tick % Time.TicksPerHour == 0) Accesses.RemoveAll(a => sim.Tick - a.Tick > Time.Hours(24 * AccessDays));
    }

    private void Answer(Sim sim, Fact ask)
    {
        int askDev = ask.Other;
        LogAnswers++;
        long within = Time.Hours(Math.Max(1, ask.Size));
        var dev = _net!.Devices[ask.Other];
        var rows = Accesses.Where(a => a.Device == askDev && sim.Tick - a.Tick <= within).OrderByDescending(a => a.Tick).Take(4).ToList();
        if (rows.Count == 0)
        {
            int c0 = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터 기록: {Ko.Eul(dev.Name)} 연 이가 없다", sim.Tick, dev.Cell, ask.Cause);
            sim.Facts.Emit(sim.Tick, FactKind.LogShown, dev.Cell, 0, c0, -1, ask.Other);
            Say(sim, $"{dev.Name} 기록을 물었다 — 연 이 없음");
            return;
        }
        foreach (var a in rows)
        {
            string who = _mind != null && a.Agent < _mind.Agents.Count ? _mind.NameOf(a.Agent) : "?";
            int c = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터 기록: {Ko.Eul(dev.Name)} {Time.Clock(a.Tick)}에 연 이 — {who}", sim.Tick, dev.Cell, ask.Cause);
            sim.Facts.Emit(sim.Tick, FactKind.LogShown, dev.Cell, (float)((sim.Tick - a.Tick) / (double)Time.TicksPerHour), c, a.Agent, ask.Other);
        }
        Say(sim, $"{dev.Name} 기록을 물었다 — " + string.Join(" · ", rows.Select(a => $"{Time.Clock(a.Tick)} {(_mind != null ? _mind.NameOf(a.Agent) : "?")}")));
    }

    private void SaveLog(StateWriter w) { w.List(Accesses); w.I64(LogAnswers); w.I64(_logCursor); }
    private void LoadLog(StateReader r) { Accesses.Clear(); Accesses.AddRange(r.List<Access>()); LogAnswers = r.I64(); _logCursor = r.I64(); }
}
