using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v17.7 항해 결산: 지금까지의 항해를 한 장으로 — 큰 사고 · 사람 · 평화로운 순간 · 물건이 거친 손.
/// 읽기만 한다 (화면이 열 때 만든다).
/// </summary>
public sealed class VoyageReview
{
    public string Ship { get; init; } = "";
    public int Days { get; init; }
    public int Crew { get; init; }
    public int Alive { get; init; }
    public int Incidents { get; init; }
    public int Moments { get; init; }
    public int[] ByScale { get; } = new int[5];
    /// <summary>큰 사고 (규모 큰 것부터 · 날짜순으로 다시).</summary>
    public List<(long Tick, string Text, IncidentScale Scale, bool Open)> Big { get; } = new();
    /// <summary>사람: 떠난 사람 · 가장 많이 나선 사람 · 가장 많이 함께한 사람.</summary>
    public List<(int Id, string Name, string Why, bool Gone)> People { get; } = new();
    /// <summary>평화로운 순간 (선물 · 화해 · 완성 · 고백 · 추모).</summary>
    public List<(long Tick, WatchKind Kind, string Text)> Peace { get; } = new();
    /// <summary>물건 내력: 이름 · 거친 손(이름들) · 지금 어디 · 자국 수.</summary>
    public List<(int Id, string Name, List<string> Hands, string Now, int Marks)> Items { get; } = new();

    public static VoyageReview Build(World w)
    {
        var h = w.History;
        string N(int id) => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "?";
        var cases = w.Scale.Cases.Concat(w.Scale.OpenCases).Distinct().ToList();
        var peaceEvents = h.Events.Where(e => WatchScenes.Classify(e.Text) != null).ToList();
        var r = new VoyageReview
        {
            Ship = w.Ship.Name,
            Days = SimTime.Day(w.Tick),
            Crew = w.Crew.Count,
            Alive = w.Crew.Count(c => !c.Dead),
            Incidents = Math.Max(cases.Count, h.Episodes.Count),
            Moments = peaceEvents.Count,
        };
        foreach (var k in cases) r.ByScale[Math.Clamp((int)k.Peak, 0, 4)]++;
        foreach (var k in cases.OrderByDescending(k => k.Peak).ThenBy(k => k.Start).Take(4).OrderBy(k => k.Start))
            r.Big.Add((k.Start, k.Name, k.Peak, k.End < 0));
        if (r.Big.Count == 0)
            foreach (var ep in h.Episodes.OrderBy(e => e.Start).TakeLast(4)) r.Big.Add((ep.Start, ep.Cause, IncidentScale.Room, ep.End < 0));

        // 사람: 떠난 사람부터 (추모) → 사고 때 가장 많이 나선 사람 → 평화로운 순간에 가장 많이 있던 사람
        foreach (var c in w.Crew.Where(c => c.Dead).Take(3))
        {
            var m = w.Life.Memorial.FirstOrDefault(x => x.name == c.Name);
            r.People.Add((c.Id, c.Name, m.name != null ? $"{SimTime.Day(m.tick)}일 · {m.cause}" : "떠났다", true));
        }
        var responders = h.Events.Where(e => e.Kind == HistoryKind.Response).SelectMany(e => e.CrewIds).GroupBy(id => id)
            .Select(g => (id: g.Key, n: g.Count())).OrderByDescending(x => x.n).ThenBy(x => x.id).ToList();
        foreach (var (id, n) in responders.Where(x => r.People.All(p => p.Id != x.id)).Take(2))
            r.People.Add((id, N(id), $"사고 때 {n}번 앞에 섰다", w.Crew.FirstOrDefault(c => c.Id == id)?.Dead == true));
        var warm = peaceEvents.SelectMany(e => e.CrewIds).GroupBy(id => id).Select(g => (id: g.Key, n: g.Count()))
            .OrderByDescending(x => x.n).ThenBy(x => x.id).FirstOrDefault(x => r.People.All(p => p.Id != x.id));
        if (warm.n > 0) r.People.Add((warm.id, N(warm.id), warm.n > 1 ? $"좋은 순간마다 있었다 ({warm.n}번)" : "좋은 순간에 함께 있었다", w.Crew.FirstOrDefault(c => c.Id == warm.id)?.Dead == true));

        // 그래도 자리가 남으면: 연대기에 이름이 가장 많이 오른 사람
        if (r.People.Count < 3)
            foreach (var (id, n) in h.Events.SelectMany(e => e.CrewIds).GroupBy(id => id).Select(g => (id: g.Key, n: g.Count()))
                         .Where(x => x.id >= 0 && r.People.All(p => p.Id != x.id)).OrderByDescending(x => x.n).ThenBy(x => x.id).Take(3 - r.People.Count).ToList())
                r.People.Add((id, N(id), $"연대기에 {n}번 이름이 올랐다", w.Crew.FirstOrDefault(c => c.Id == id)?.Dead == true));

        // 좋은 순간: 종류마다 가장 최근 것 하나씩 (화해만 넷이 되지 않게) → 남으면 최근 것으로 채운다
        var picks = peaceEvents.GroupBy(e => WatchScenes.Classify(e.Text)!.Value).Select(g => g.Last()).ToList();
        foreach (var e in peaceEvents.AsEnumerable().Reverse()) { if (picks.Count >= 4) break; if (!picks.Contains(e)) picks.Add(e); }
        foreach (var e in picks.OrderByDescending(e => e.Tick).Take(4).OrderBy(e => e.Tick)) r.Peace.Add((e.Tick, WatchScenes.Classify(e.Text)!.Value, e.Text));
        foreach (var b in WatchScenes.Notable(w).Take(3))
            r.Items.Add((b.Id, b.Name, WatchScenes.Hands(b).Select(N).ToList(), w.Belongings.Where(b), b.Marks.Count));
        return r;
    }
}
