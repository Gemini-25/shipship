using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 연출: 사고 결산 카드. 지휘 건이 닫히면 한 장으로 — 무엇이 · 누가 · 잃은 것 · 지킨 것 · 로봇 활약 · 주컴퓨터 판단.
//   큰 건(위중한 부상 · 3칸 넘게 번졌거나 · 10분 넘게 갔거나 · 잃은 것이 있으면)은 화면에 크게 띄웠다가 접는다. 연대기에도 남는다.

public sealed class CaseSummary
{
    public int CaseId { get; init; }
    public CaseKind Kind { get; init; }
    public int RoomId { get; init; }
    public long Opened { get; init; }
    public long Closed { get; init; }
    public string Outcome { get; init; } = "";
    /// <summary>크게 띄울 만한 건.</summary>
    public bool Big { get; init; }
    public string Title { get; init; } = "";
    public string What { get; init; } = "";
    public List<string> Who { get; } = new();
    public List<string> Lost { get; } = new();
    public List<string> Kept { get; } = new();
    public string Robots { get; init; } = "";
    public string Computer { get; init; } = "";
    public float Minutes => (Closed - Opened) / (float)SimTime.Minutes(1);
}

public sealed partial class IncidentCommander
{
    /// <summary>최근 결산 (스물까지).</summary>
    public List<CaseSummary> Summaries { get; } = new();

    /// <summary>지휘 건을 열 때의 모습을 적어 둔다 (결산에서 잃은 것을 셈한다).</summary>
    private void Snapshot(CommandCase k, Room room)
    {
        var w = _w;
        k.FaultsAt0 = room.Furniture.Sum(f => f.Machine?.FaultCount ?? 0);
        k.BurnedAt0 = w.Fire.ItemsBurned;
        k.BotStagesAt0 = w.Robots.Body.Stages;
        k.PeopleIn0 = w.Crew.Count(c => !c.Dead && c.Room == room);
        foreach (var c in w.Crew) if (!c.Dead) k.HealthAt0[c.Id] = c.Vitals.Health;
    }

    private void Summarize(CommandCase k, string outcome)
    {
        var w = _w;
        if (outcome.Contains("넘겨받는다")) return;
        var room = RoomOf(k);
        string where = room?.Name ?? "?";
        var who = k.CrewId >= 0 ? w.Crew.FirstOrDefault(c => c.Id == k.CrewId) : null;
        float mins = (w.Tick - k.Opened) / (float)SimTime.Minutes(1);
        var s = new CaseSummaryBuilder();

        // 잃은 것
        int faults = room == null ? 0 : room.Furniture.Sum(f => f.Machine?.FaultCount ?? 0) - k.FaultsAt0;
        if (faults > 0) s.Lost.Add($"설비 고장 {faults}");
        int burned = w.Fire.ItemsBurned - k.BurnedAt0;
        if (burned > 0 && k.Kind == CaseKind.Fire) s.Lost.Add($"탄 물건 {burned}개");
        var hurt = w.Crew.Where(c => !c.Dead && k.HealthAt0.TryGetValue(c.Id, out var h0) && h0 - c.Vitals.Health >= 0.05f && (c.Room == room || c.Id == k.CrewId)).Select(c => c.Name).ToList();
        if (hurt.Count > 0) s.Lost.Add($"다친 사람: {string.Join(" · ", hurt.Take(4))}");
        var dead = w.Crew.Where(c => c.Dead && c.DiedAt >= k.Opened).Select(c => c.Name).ToList();
        if (dead.Count > 0) s.Lost.Add($"숨진 사람: {string.Join(" · ", dead)}");
        int botHurt = k.Served.Sum(id => BotOf(id) is Robot rb ? rb.Scares - (k.ScaresAt0.TryGetValue(id, out var s0) ? s0 : rb.Scares) : 0);
        var wrecked = k.Served.Select(id => BotOf(id)).Where(r => r != null && r.Wrecked).Select(r => r!.Name).ToList();
        if (wrecked.Count > 0) s.Lost.Add($"부서진 로봇: {string.Join(" · ", wrecked)}");
        else if (botHurt > 0) s.Lost.Add($"로봇 상처 {botHurt}곳");
        if (room != null && room.Detached) s.Lost.Add($"{where} — 떨어져 나갔다");

        // 지킨 것
        if (k.PeopleIn0 > 0 && dead.Count == 0) s.Kept.Add($"방 안에 있던 {k.PeopleIn0}명 무사");
        if (k.Kind == CaseKind.Fire && outcome == "불을 껐다") s.Kept.Add(k.Peak <= 2 ? $"번지기 전에 껐다 ({k.Peak:0}칸)" : $"옆 방으로 번지지 않았다 (가장 컸을 때 {k.Peak:0}칸)");
        if (k.Kind == CaseKind.Breach && outcome == "파공을 막았다") s.Kept.Add("배의 공기 — 그 방에서 멈췄다");
        if (k.Kind == CaseKind.Casualty && who != null && !who.Dead) s.Kept.Add($"{who.Name} — 고비를 넘겼다");
        if (faults == 0 && k.Kind == CaseKind.Fire && outcome == "불을 껐다") s.Kept.Add("설비 고장 없음");

        // 누가 · 로봇 활약
        var first = BotOf(k.FirstBot);
        var served = k.Served.Select(BotOf).Where(r => r != null).Cast<Robot>().ToList();
        foreach (var r in served.Take(5)) s.Who.Add(r == first ? $"{r.Name} (첫 손)" : r.Name);
        var people = w.Log.Entries.Where(e => e.Tick >= k.Opened && e.CrewId >= 0 && (e.Text.Contains(where) || who != null && e.Text.Contains(who.Name)) && (e.Text.Contains("껐") || e.Text.Contains("막았") || e.Text.Contains("치료") || e.Text.Contains("눌렀")))
            .Select(e => w.Crew.FirstOrDefault(c => c.Id == e.CrewId)?.Name).Where(n => n != null).Distinct().Take(3).ToList();
        foreach (var n in people) s.Who.Add(n!);
        string robots = served.Count == 0 ? (w.Nozzles.SprayedWithin(room ?? w.Ship.Rooms[0], SimTime.Minutes(30)) && k.Kind == CaseKind.Fire ? "로봇이 닿기 전에 천장 소화 설비가 껐다" : "로봇 손이 닿지 않았다")
            : first != null ? $"{Ko.IGa(first.Name)} {(k.OnScene - k.Opened) / (float)SimTime.Minutes(1):0}분 만에 닿았다 · 모두 {served.Count}대" + (k.Reinforced > 0 ? $" · 증원 {k.Reinforced}번" : "")
            : $"로봇 {served.Count}대가 붙었다";

        // 주컴퓨터 판단
        var comp = new List<string> { $"{Ko.EuRo(k.Source)} 알았다" };
        if (k.NozzleSaid != "") comp.Add(k.NozzleSaid == "뿌리는 중" ? "천장 소화를 열었다" : $"천장 소화 {k.NozzleSaid}");
        if (k.WindWait) comp.Add("바람이 세서 로봇을 문 앞에 세웠다");
        if (k.Notes.Any(n => n.Text.Contains("다시 붙었다") || n.Text.Contains("다시 샌다"))) comp.Add("한 번 끝났다고 봤는데 다시 붙었다 (오판)");
        var review = Lessons.Recent.LastOrDefault(l => l.StartsWith($"복기 #{k.Id} "));
        if (review != null) comp.Add(review.Split(" — ", 2).Last());

        string kind = KindName(k.Kind);
        bool big = k.Kind == CaseKind.Casualty && k.Life || k.Peak >= 3 || mins >= 10f || s.Lost.Count > 0; // 사람이 잠깐 지나간 작은 불은 크게 띄우지 않는다
        var sum = new CaseSummary
        {
            CaseId = k.Id, Kind = k.Kind, RoomId = k.RoomId, Opened = k.Opened, Closed = w.Tick, Outcome = outcome, Big = big,
            Title = k.Kind == CaseKind.Casualty && who != null ? $"{who.Name} 부상 — {outcome}" : $"{where} {kind} — {outcome}",
            What = k.Kind switch
            {
                CaseKind.Fire => $"불 {k.Peak:0}칸까지 · {mins:0}분",
                CaseKind.Breach => $"외벽 파공 · {mins:0}분",
                _ => $"{who?.Name ?? "?"} 다침 · {mins:0}분",
            },
            Robots = robots,
            Computer = string.Join(" · ", comp),
        };
        sum.Who.AddRange(s.Who);
        sum.Lost.AddRange(s.Lost);
        sum.Kept.AddRange(s.Kept);
        Summaries.Add(sum);
        if (Summaries.Count > 20) Summaries.RemoveAt(0);
    }

    private sealed class CaseSummaryBuilder
    {
        public List<string> Who { get; } = new();
        public List<string> Lost { get; } = new();
        public List<string> Kept { get; } = new();
    }
}
