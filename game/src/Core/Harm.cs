using System;
using System.Linq;

namespace ShipSim.Core;

// v12.4 피해 체계 일반화: "어디에 · 얼마나 · 무엇으로". 운석·폭발·불·전자기 교란이 모두 이 말로 표현된다.
// 지금은 사고들이 쓰고, 나중에 포격(원거리 교전)이 들어오면 새 피해 방식 하나만 더하면 뒤의 연쇄·복구는 그대로 쓴다.

public enum HarmKind
{
    Impact,   // 부딪힘 (운석·파편) — 선체 밖에서 안으로
    Pierce,   // 관통 (고속 파편·포탄) — 벽을 뚫고 깊이 들어온다
    Blast,    // 폭발 — 둘레로 퍼진다
    Heat,     // 열 — 불이 붙고 전선이 탄다
    Pulse,    // 전자기 교란 — 감지기·데이터선·차단기·컴퓨터
}

public static class Harm
{
    /// <summary>한 점에 피해를 준다 (cause는 인과 사슬·기록에 남는 말). 걸었으면 true.</summary>
    public static bool At(World w, Cell at, HarmKind kind, float power, string cause)
    {
        power = Math.Clamp(power, 0f, 1.5f);
        var room = w.Ship.RoomAt(at);
        switch (kind)
        {
            case HarmKind.Impact:
                return Incidents.Meteor(w, at, power) != null;
            case HarmKind.Pierce:
            {
                // 관통: 선체 쪽에서 들어오는 좁고 깊은 운석과 같게, 깊이는 1.5배
                return Incidents.Meteor(w, at, MathF.Min(1.2f, power * 1.3f)) != null;
            }
            case HarmKind.Blast:
                w.Volatile.Blast(at, power, cause);
                return true;
            case HarmKind.Heat:
            {
                int node = w.Causes.Context >= 0 ? w.Causes.Context : w.Causes.Root(CauseKind.Hazard, $"{cause} — {room?.Name ?? "?"}", room, at.Center);
                using (w.Causes.Because(node))
                {
                    bool lit = w.Fire.Ignite(at, 0.2f + 0.5f * power);
                    w.Net.DamageNear(at, 1.2f, 0.6f * power, cause, fire: true);
                    return lit;
                }
            }
            case HarmKind.Pulse:
            {
                int node = w.Causes.Context >= 0 ? w.Causes.Context : w.Causes.Root(CauseKind.Hazard, $"{cause} — 전자기 교란", room, at.Center);
                using (w.Causes.Because(node))
                {
                    float r = 3f + 6f * power;
                    foreach (var m in w.Ship.Machines.Where(m => (m.Body.Center - at.Center).Length() < r))
                    {
                        m.SensorCal = MathF.Max(0.3f, m.SensorCal - 0.3f * power);
                        if (m.Body.Type is FurnitureType.MainComputer or FurnitureType.Console && w.Rng.Chance(0.4f * power)) w.Machines.Break(m);
                    }
                    foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Data && l.Cells.Any(c => (c.Center - at.Center).Length() < r))) w.Net.Hurt(l, 0.5f * power, cause);
                    if (w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine panel && w.Rng.Chance(0.6f * power))
                    {
                        int c = w.Rng.Range(0, PowerGrid.CircuitCount);
                        if (!panel.Faults.Any(f => f.Circuit == c)) { panel.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = c }); panel.FaultCount++; w.Causes.OnFault(panel, panel.Faults[^1]); }
                    }
                    w.RaiseAlert($"{cause} — 전자기 교란: 감지기가 틀어지고 데이터선·차단기가 흔들린다", room, AlertLevel.Warning, shipWide: true);
                }
                return true;
            }
        }
        return false;
    }
}
