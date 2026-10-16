using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 연출: 오늘의 장면. 연대기에 남는 일마다 그날의 장면 후보로 점수를 매긴다 (죽음 · 탄생 · 결혼 · 큰 사고와 대응 · 처음 본 풍경 · 이정표 …).
//   같은 방 30분 안의 후보는 가장 센 것 하나만. 저녁 22시에 그날 장면 1~3개를 골라 보여 주고 사진첩에 남긴다.
//   화면은 후보가 생긴 그 순간 그 자리를 따로 찍어 둔다 (사진 파일 — 시뮬레이션은 모른다).

public sealed class SceneShot
{
    public int Id { get; init; }
    public long Tick { get; init; }
    /// <summary>고른 날 (장면의 하루는 저녁 22시에서 다음 22시까지 — 밤늦은 일은 다음 날 장면이 된다).</summary>
    public int Day { get; set; }
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public int X { get; init; } = -1;
    public int Y { get; init; } = -1;
    public float Score { get; set; }
    /// <summary>배 전체의 일 (출항 · 함장 · 풍경 …) — 배 전체를 한 장에.</summary>
    public bool Wide { get; init; }
    public HistoryKind Kind { get; init; }
}

public sealed class SceneBook
{
    private readonly World _w;
    public SceneBook(World w) => _w = w;

    /// <summary>오늘의 후보 (열둘까지 · 점수 순으로 거른다).</summary>
    public List<SceneShot> Today { get; } = new();
    /// <summary>지금까지 고른 장면.</summary>
    public List<SceneShot> Shots { get; } = new();
    /// <summary>마지막으로 보여 준 날의 장면 (화면이 띄운다).</summary>
    public List<int> LastPick { get; } = new();
    public long LastPickTick { get; private set; } = -1;
    public int LastDay { get; private set; }
    public int NextId { get; private set; } = 1;
    /// <summary>찍을 만한 후보가 마지막으로 생긴 번호 (화면이 이보다 큰 것을 찍는다).</summary>
    public int LastOffered { get; private set; }

    public SceneShot? Get(int id) => Today.FirstOrDefault(s => s.Id == id) ?? Shots.FirstOrDefault(s => s.Id == id);

    private static float Score(HistoryKind k, string text)
    {
        float s = k switch
        {
            HistoryKind.Death => 6f, HistoryKind.Casualty => 3f, HistoryKind.Incident => 2.5f, HistoryKind.Damage => 2f,
            HistoryKind.Response => 2.5f, HistoryKind.Bond => 3f, HistoryKind.Milestone => 3f, HistoryKind.Decision => 2f,
            HistoryKind.Structure => 3f, HistoryKind.Upgrade => 1.5f, HistoryKind.Adaptation => 1.5f, HistoryKind.Recovery => 1.5f,
            HistoryKind.Memory => 1f, HistoryKind.Lesson => 1f, _ => 0.5f,
        };
        if (text.Contains("태어")) s += 4f;
        if (text.Contains("결혼")) s += 3f;
        if (text.Contains("막았다") || text.Contains("껐다") || text.Contains("구했다") || text.Contains("살렸다")) s += 1f;
        if (text.Contains("함장이 되었다") || text.Contains("선거") || text.Contains("불신임")) s += 1.5f;
        if (text.Contains("조건이 풀렸다") || text.Contains("익혔다 (")) s -= 1f;
        if (text.Contains("처음 본 창밖")) s += 0.5f;
        if (text.Contains("저장")) s -= 3f;
        return s;
    }

    /// <summary>연대기 한 줄이 생겼다 (History.Add가 부른다).</summary>
    internal void Offer(HistoryEvent e)
    {
        var w = _w;
        float score = Score(e.Kind, e.Text);
        if (score < 2f) return;
        int x = -1, y = -1;
        if (e.At is Cell at) { x = at.X; y = at.Y; }
        else if (e.RoomId >= 0 && e.RoomId < w.Ship.Rooms.Count) { var c = w.Ship.Rooms[e.RoomId].Center; x = (int)c.X; y = (int)c.Y; }
        else if (e.CrewIds.Length > 0 && w.Crew.FirstOrDefault(c => c.Id == e.CrewIds[0]) is CrewMember cm) { x = cm.Cell.X; y = cm.Cell.Y; }
        bool wide = x < 0;
        if (wide && w.Ship.LiveRooms.Any())
        {
            var cells = w.Ship.LiveRooms.SelectMany(r => r.Cells);
            x = (cells.Min(c => c.X) + cells.Max(c => c.X)) / 2; y = (cells.Min(c => c.Y) + cells.Max(c => c.Y)) / 2;
        }
        string text = e.Text;
        // 사고 대응은 결산 카드의 말로 (지휘 건 번호 대신 어디서 무엇이)
        if (e.Kind == HistoryKind.Response && w.Commander.Summaries.Count > 0 && w.Commander.Summaries[^1] is CaseSummary cs && cs.Closed == e.Tick)
        {
            text = $"{cs.Title} · {cs.What} · {cs.Robots}";
            if (cs.Big) score += 1.5f;
            if (cs.Lost.Count > 0) score += 0.5f;
        }
        // 같은 방 30분 안의 후보는 가장 센 것 하나만
        var near = Today.FirstOrDefault(s => s.RoomId >= 0 && s.RoomId == e.RoomId && e.Tick - s.Tick < SimTime.Minutes(30));
        if (near != null)
        {
            if (near.Score >= score) return;
            Today.Remove(near);
        }
        string title = text.Split(" · ")[0];
        if (title.Length > 30) title = title[..29] + "…";
        var shot = new SceneShot { Id = NextId++, Tick = e.Tick, Day = SimTime.Day(e.Tick), Title = title, Text = text, RoomId = e.RoomId, X = x, Y = y, Score = score, Kind = e.Kind, Wide = wide };
        Today.Add(shot);
        LastOffered = shot.Id;
        if (Today.Count > 12) Today.Remove(Today.OrderBy(s => s.Score).ThenBy(s => s.Id).First());
    }

    /// <summary>저녁 22시: 그날 장면 1~3개를 고른다.</summary>
    public void Update()
    {
        var w = _w;
        int day = SimTime.Day(w.Tick);
        if (SimTime.HourOfDay(w.Tick) < 22f || day == LastDay) return;
        LastDay = day;
        if (Today.Count == 0) return;
        var pick = Today.OrderByDescending(s => s.Score).ThenBy(s => s.Tick).Take(3).Where((s, i) => i == 0 || s.Score >= 2.5f).OrderBy(s => s.Tick).ToList();
        LastPick.Clear();
        foreach (var s in pick) { s.Day = day; Shots.Add(s); LastPick.Add(s.Id); }
        if (Shots.Count > 300) Shots.RemoveRange(0, Shots.Count - 300);
        LastPickTick = w.Tick;
        Today.Clear();
        w.Log.Add(w.Tick, LogKind.Ship, $"오늘의 장면 ({day}일) — {string.Join(" · ", pick.Select(s => s.Title))}");
    }
}
