using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 주컴퓨터 대개편 4단계: 평소 수리 배치 · 배움(복기).
//   · 평소 수리 배치 (10분마다 · 주 컴퓨터 · 보조 두뇌만 · 목숨이 걸린 지휘 건이 없을 때):
//     30분 넘게 아무도 안 맡은 고장 · 파손 일을 급한 순서로 골라, 할 줄 아는 한가한 로봇을 직접 붙인다 (특기 먼저 · 가까운 손).
//   · 복기 (지휘 건이 닫힐 때): 증원이 필요했으면 다음엔 처음부터 더 보내고, 남아돌았으면 줄인다.
//     첫 손이 늦게 닿은 방은 거점을 둘 때 더 챙긴다. 오판도 기록에 남긴다. 주 컴퓨터가 멎은 동안 로봇 대장이 지휘한 건도 같은 장부로 복기한다.

public sealed class CommandLessons
{
    /// <summary>사건 종류마다 처음 보낼 손을 더하거나 뺀다 (−1 ~ +2).</summary>
    public List<float> Extra { get; } = new() { 0f, 0f, 0f };
    /// <summary>첫 손이 늦게 닿았던 방 → 거점 가중치.</summary>
    public SortedDictionary<int, float> SlowRooms { get; } = new();
    public int Reviews, More, Less, Slow, Assigned;
    public List<string> Recent { get; } = new();
    public string Line() => $"복기 {Reviews} (더 보냄 {More} · 줄임 {Less} · 늦은 방 {Slow}) · 평소 수리 배치 {Assigned} · 처음 손 더하기 불 {Extra[0]:+0.#;-0.#;0} · 파공 {Extra[1]:+0.#;-0.#;0} · 부상 {Extra[2]:+0.#;-0.#;0}";
}

public sealed partial class IncidentCommander
{
    public CommandLessons Lessons { get; } = new();
    private long _backlogNext;

    /// <summary>복기에서 배운 만큼 처음 보낼 손 (반올림).</summary>
    private int Learned(CaseKind k) => (int)MathF.Round(Lessons.Extra[(int)k]);

    /// <summary>고장 · 파손 일 (평소 수리 배치가 챙긴다).</summary>
    private static bool Fixit(WorkKind k) => k is WorkKind.Repair or WorkKind.RepairHull or WorkKind.RepairDoor or WorkKind.FixLights or WorkKind.ServiceNozzle
        or WorkKind.RepairRelay or WorkKind.Maintain or WorkKind.Rewire or WorkKind.Reline or WorkKind.RepairNet or WorkKind.PatchPipe or WorkKind.RepairRadiator
        or WorkKind.ReplacePanel or WorkKind.RestoreCircuit or WorkKind.ResetBreaker or WorkKind.RepairRobot or WorkKind.SealSeep or WorkKind.RepairJoint;

    /// <summary>평소 수리 배치: 30분 넘게 아무도 안 맡은 고장 · 파손 일에 한가한 로봇을 붙인다 (한 번에 넷까지).</summary>
    private void Backlog()
    {
        var w = _w;
        if (Tier > CommandTier.Backup || Cases.Any(c => c.Open && c.Life)) return;
        var stale = w.Board.OpenUnsorted.Where(o => !o.Closed && o.Robot == null && o.Assignee == null && Fixit(o.Kind) && w.Tick - o.Posted > SimTime.Minutes(30))
            .OrderByDescending(o => o.Urgency).ThenBy(o => o.Posted).ThenBy(o => o.Id).Take(4).ToList();
        foreach (var o in stale)
        {
            var at = o.Target.Cell;
            var bot = w.Robots.Robots.Where(r => RobotSystem.Callable(w, r) && r.CaseId < 0 && r.Order == null && r.Post == null && r.Battery > 0.5f
                    && (r.Steps == null || RobotSystem.Idle(r)) && RobotSystem.CanDo(r.Kind, o.Kind) && w.Relays.Reach(r.Room) && !RobotsV21.Medical(r.Kind))
                .OrderBy(r => RobotSystem.Special(r.Kind, o.Kind) ? 0 : 1).ThenBy(r => (r.Position - at.Center).LengthSquared()).ThenBy(r => r.Id).FirstOrDefault();
            if (bot == null) continue;
            if (bot.State == RobotState.Docked && bot.Battery < 0.8f && !RobotsV21.Reactor) continue; // 충전 중 — 다 차면 제가 고른다
            if (!w.Robots.WorkOn(bot, o, $"{o.Title} — 밀린 일 (주 컴퓨터 배정)")) continue;
            Lessons.Assigned++;
            float hours = (w.Tick - o.Posted) / (float)SimTime.TicksPerHour;
            w.Log.Add(w.Tick, LogKind.Ship, $"{Voice}: 밀린 일 — {o.Title} ({hours:0.#}시간째 아무도 안 맡았다) · {Ko.EulReul(bot.Name)} 붙인다");
            bot.Mind.Say(RobotBrain.Calm(bot, $"주 컴퓨터 배정 — {o.Title}", w.Tick), w.Tick);
        }
    }

    /// <summary>복기: 지휘 건이 닫힐 때 — 처음 손이 모자랐나 · 남았나 · 늦게 닿았나.</summary>
    private void Review(CommandCase k, string outcome)
    {
        var w = _w;
        if (k.Kind is not (CaseKind.Fire or CaseKind.Breach or CaseKind.Casualty) || outcome.Contains("넘겨받는다")) return;
        Lessons.Reviews++;
        int i = (int)k.Kind;
        var bits = new List<string>();
        if (k.Reinforced > 0)
        {
            Lessons.Extra[i] = MathF.Min(2f, Lessons.Extra[i] + 0.5f * k.Reinforced);
            Lessons.More++;
            bits.Add($"증원 {k.Reinforced}번 — 다음엔 처음부터 손을 더 보낸다 (+{Lessons.Extra[i]:0.#})");
        }
        else if (k.Kind == CaseKind.Fire && k.Sent >= 3 && k.Peak <= 2 && k.OnScene >= 0 && k.Resolved >= 0 && k.Resolved - k.OnScene < SimTime.Minutes(3))
        {
            Lessons.Extra[i] = MathF.Max(-1f, Lessons.Extra[i] - 0.25f);
            Lessons.Less++;
            bits.Add($"작은 불에 {k.Sent}대 — 많았다 · 다음엔 줄인다 ({Lessons.Extra[i]:+0.##;-0.##;0})");
        }
        if (k.OnScene >= 0 && k.OnScene - k.Opened > SimTime.Minutes(5) && k.RoomId >= 0)
        {
            Lessons.SlowRooms[k.RoomId] = MathF.Min(3f, (Lessons.SlowRooms.TryGetValue(k.RoomId, out var v) ? v : 0f) + 1f);
            Lessons.Slow++;
            bits.Add($"첫 손이 {(k.OnScene - k.Opened) / (float)SimTime.Minutes(1):0}분 만에 닿았다 — 그 방 곁에 거점을 더 챙긴다");
        }
        if (k.OnScene < 0 && outcome is "불을 껐다" or "파공을 막았다") bits.Add("로봇 손이 닿기 전에 끝났다 (천장 소화 · 사람)");
        if (outcome is "숨졌다") bits.Add("못 살렸다 — 다음엔 더 일찍 · 더 많이");
        if (bits.Count == 0) return;
        string line = $"복기 #{k.Id} {KindName(k.Kind)}{(RoomOf(k) is Room r ? $" ({r.Name})" : "")} — {string.Join(" · ", bits)}";
        Lessons.Recent.Add(line);
        if (Lessons.Recent.Count > 12) Lessons.Recent.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"{Voice}: {line}");
    }

    /// <summary>복기에서 늦게 닿았던 방은 거점 위험에 얹는다.</summary>
    private float SlowBias(Room room) => Lessons.SlowRooms.TryGetValue(room.Id, out var v) ? v : 0f;
}
