using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.27 ① 아침 방송 · 저녁 항해 일지.
//  아침(7시쯤): 오늘 정비(주간 정비표에서) · 우주 날씨 예보 · 주의할 곳(젖은 바닥 · 감지기가 안 닿는 방 · 닳은 설비 · 공사 중) · 일정(훈련 · 원정 · 당직) · 물자 전망.
//    식당 화면에 같은 줄이 뜨고 방송으로 나간다 — 들은 사람만 안다. 맡은 정비를 들은 사람은 그 일을 먼저 집는다.
//    컴퓨터 기억이 틀어졌으면 틀린 말도 그대로 나간다 (승무원이 듣고 바로잡는다 — ComputerMateFlaw).
//  저녁(21시쯤): 그날의 일지 — 성격 따라 말투가 다르다 (신중: 확신 없는 것부터 · 과감: 짧게 · 사람 우선: 사람부터 · 배 우선: 수치부터).
//    연대기 "장"의 컴퓨터 일지 칸에 이어진다 (UiChronicle.ShipLog).

public sealed class Briefing
{
    public int Day { get; init; }
    public long Tick { get; init; }
    /// <summary>(갈래, 한 줄): 갈래 = 정비 · 날씨 · 주의 · 일정 · 물자.</summary>
    public List<(string Kind, string Text)> Lines { get; } = new();
    public List<int> Heard { get; } = new();
    public string Text => string.Join(" · ", Lines.Select(l => l.Text));
}

public sealed partial class ShipMate
{
    public List<Briefing> Briefings { get; } = new();
    public List<(long Tick, string Text)> Evenings { get; } = new();
    private int _briefDay = -1, _eveDay = -1;
    public Briefing? LastBriefing => Briefings.Count > 0 ? Briefings[^1] : null;

    private void DayTick()
    {
        var w = _w;
        int day = SimTime.Day(w.Tick);
        float h = Hour(w.Tick);
        if (day != _briefDay && h >= 7f && h < 11f && !Crisis.Acting(w)) { _briefDay = day; Brief(day); }
        if (day != _eveDay && h >= 21f) { _eveDay = day; Evening(day); }
    }

    /// <summary>아침 방송 (시험 · 관찰자도 부른다).</summary>
    public Briefing Brief(int day)
    {
        var w = _w;
        var a = A;
        var b = new Briefing { Day = day, Tick = w.Tick };
        CrossCheck(); // 약속: 작업 일지와 맞춰 보고 말한다
        if (ForcePlan) { ForcePlan = false; PlanWeek(); }
        // 정비 (주간 정비표의 오늘 칸 — 기억이 틀어졌으면 "어제 끝냈다"고 빼먹은 것도 말한다)
        var today = Slots.Where(s => s.Day == day && !s.Done && !s.Missed).OrderBy(s => s.Hour).ThenBy(s => s.MachineId).Take(3).ToList();
        if (today.Count > 0)
            b.Lines.Add(("정비", "오늘 정비: " + string.Join(" · ", today.Select(s => $"{s.Machine} {s.Hour:0}시"))
                + $" (가장 급한 것 남은 수명 {LifeText(today[0])})"));
        foreach (var m in Memory.Values.Where(x => x.Kind == MemKind.Service && x.Corrupt && x.Claimed < 0).OrderBy(x => x.Key).Take(1))
        {
            m.Claimed = w.Tick;
            b.Lines.Add(("정비", $"{m.Label}은 어제 손봤다 — 이번 주 정비에서 뺐다"));
        }
        // 날씨
        if (LastSky is SkyForecast f) b.Lines.Add(("날씨", SkyLine(f)));
        // 주의할 곳
        var cautions = Cautions(2);
        if (cautions.Count > 0) b.Lines.Add(("주의", "주의할 곳: " + string.Join(" · ", cautions.Select(c => c.text))));
        // 일정
        var plan = new List<string>();
        if (NextDrill is DrillRun nd && SimTime.Day(nd.Start) == day) plan.Add($"{Hour(nd.Start):0}시 {nd.Kind} 훈련");
        if (w.Expedition.Current is Trip tr && tr.Phase is TripPhase.Gathering) plan.Add($"원정대 출발 ({tr.Site.Name})");
        if (a.Apps.Roster.FirstOrDefault(r => r.Day == day && r.Duty == "야간 당직") is DutySlot ds && Crew(ds.CrewId) is CrewMember dc) plan.Add($"야간 당직 {dc.Name}");
        if (plan.Count > 0) b.Lines.Add(("일정", "오늘 일정: " + string.Join(" · ", plan)));
        // 물자
        if (Supplies.Where(s => s.Open).OrderBy(s => s.DaysLeft).FirstOrDefault() is SupplyPlan sp)
            b.Lines.Add(("물자", $"{sp.DaysLeft:0}일 뒤 {Ko.IGa(sp.Name)} 바닥난다 — {sp.Choice}"));
        if (b.Lines.Count == 0) b.Lines.Add(("일정", "특별한 일정 없음 — 배는 조용하다"));
        Briefings.Add(b);
        if (Briefings.Count > 40) Briefings.RemoveAt(0);
        string text = $"좋은 아침입니다. {b.Text}";
        var bc = a.Speak.Announce(a.Voice.Style(a.Manner.Speak(text)), null, 0);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call} 아침 방송: {b.Text}");
        if (bc != null) b.Heard.AddRange(bc.HeardBy);
        // 들은 사람: 오늘 맡은 정비를 먼저 집는다 (부탁) · 귀찮아하는 사람은 투덜댄다
        foreach (var id in b.Heard)
        {
            if (Crew(id) is not CrewMember c || c.IsChild) continue;
            if (c.Value == CrewValue.Freedom && c.Traits.Sociability < 0.4f && R.Chance(0.25f))
                Life.Diary(w, c, Persona.Say(c, "아침마다 방송이 길다. 귀를 막고 커피를 마셨다"));
        }
        HeardBriefing(b);
        return b;
    }

    /// <summary>주의할 곳 (방 · 한 줄) — 컴퓨터가 아는 것만.</summary>
    internal List<(Room room, string text)> Cautions(int max)
    {
        var w = _w;
        var a = A;
        var list = new List<(int rank, Room room, string text)>();
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Type == RoomType.Corridor || r.OffLimits) continue;
            if (!r.DataLinked || a.Belief.Of(r).Fault != SensorFault.None) list.Add((1, r, $"{r.Name} (감지기가 안 닿는다)"));
            else if (MoistureSystem.DepthCm(r) > 0.6f) list.Add((2, r, $"{r.Name} 바닥이 젖었다"));
            else if (r.Radiation > 0.3f) list.Add((2, r, $"{r.Name} 방사선이 높다"));
        }
        foreach (var t in Trends.Values)
            if (t.Now > 0.6f && w.Ship.Machines.FirstOrDefault(m => m.Body.Id == t.MachineId) is Machine m)
                list.Add((0, m.Body.Room, $"{m.Name} {TrendWord(m)}이 커졌다"));
        foreach (var u in Upgrades.Where(u => u.State == GearState.Working))
            if (w.Ship.Rooms.FirstOrDefault(r => r.Id == u.RoomId) is Room ur) list.Add((3, ur, $"{ur.Name} 공사 중"));
        var seen = new HashSet<int>();
        var outp = new List<(Room, string)>();
        foreach (var x in list.OrderBy(x => x.rank).ThenBy(x => x.room.Id))
        {
            if (!seen.Add(x.room.Id)) continue;
            outp.Add((x.room, x.text));
            if (outp.Count >= max) break;
        }
        return outp;
    }

    // ───────────── 저녁 항해 일지 ─────────────

    /// <summary>저녁 일지 한 편 (시험도 부른다).</summary>
    public string Evening(int day)
    {
        var w = _w;
        var a = A;
        var ch = a.Character;
        long start = (long)(day - 1) * SimTime.TicksPerDay;
        var people = new List<string>();
        var numbers = new List<string>();
        var doubts = new List<string>();
        // 오늘 사고
        int inc = 0; string? first = null;
        for (int i = w.History.Events.Count - 1; i >= 0; i--)
        {
            var e = w.History.Events[i];
            if (e.Tick < start) break;
            if (e.Kind != HistoryKind.Incident) continue;
            inc++; first = e.Text;
        }
        if (inc > 0) numbers.Add($"사고 {inc}건{(first != null ? $" (처음은 {ChronicleBook.Short(first, 34)})" : "")}");
        else numbers.Add("사고 없는 하루");
        // 정비
        int done = Slots.Count(s => s.Day == day && s.Done), missed = Slots.Count(s => s.Day == day && s.Missed);
        if (done + missed > 0) numbers.Add($"계획 정비 {done}건{(missed > 0 ? $" · 늦어 놓친 것 {missed}건" : "")}");
        // 날씨
        if (Forecasts.LastOrDefault(f => f.Judged && f.JudgedAt >= start) is SkyForecast jf) (jf.Hit ? numbers : doubts).Add(jf.Verdict);
        // 사람
        int hints = CareLog.Count(x => x.tick >= start);
        if (hints > 0) people.Add($"걱정되는 사람 {hints}명에게 마음을 썼다{(CareLevel == 0 ? " (몸 신호만 봤다)" : "")}");
        if (Drills.LastOrDefault(d => d.Done && d.End >= start) is DrillRun dr) people.Add(dr.Summary);
        var refused = Refusals.Count(r => r.Tick >= start);
        if (refused > 0) people.Add($"부탁이 {refused}번 받아들여지지 않았다 — {Refusals.Last(r => r.Tick >= start).Next}");
        foreach (var p in Promises.Where(p => p.LastTest >= start)) (p.LastKept ? people : doubts).Add($"약속 \"{p.Text}\" — {(p.LastKept ? "지켰다" : "못 지켰다")}");
        if (MemoryChecks > 0 && LastMemoryCheck >= start) doubts.Add(LastMemoryNote);
        foreach (var rv in Reviews.Where(r => r.Tick >= start)) doubts.Add(rv.Text);
        if (a.Book.WrongToday > 0) doubts.Add($"틀린 판단 {a.Book.WrongToday}건");
        // 말투: 성격
        var order = ch.PeopleTilt > 0.25f ? people.Concat(numbers) : numbers.Concat(people);
        var body = new List<string>();
        if (ch.Caution > 0.25f && doubts.Count > 0) body.AddRange(doubts.Take(2)); // 신중: 확신 없는 것부터
        body.AddRange(order);
        if (!(ch.Caution > 0.25f)) body.AddRange(doubts.Take(ch.Caution < -0.25f ? 1 : 2));
        if (ch.Caution < -0.25f) body = body.Take(3).ToList(); // 과감: 짧게
        string end = ch.Caution > 0.25f ? "내일은 조금 더 일찍 살피겠다." : ch.Caution < -0.25f ? "내일도 이대로 간다." : "내일 아침에 다시 알린다.";
        string joke = inc == 0 && w.Crew.Any(c => !c.Dead) ? a.Manner.Joke(w.Crew.Where(c => !c.Dead).OrderByDescending(c => c.Stats.Services).ThenBy(c => c.Id).First().Name) : "";
        string text = $"{day}일 — " + string.Join(". ", body) + ". " + end + (joke != "" ? " " + joke : "");
        Evenings.Add((w.Tick, text));
        if (Evenings.Count > 60) Evenings.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call} 항해 일지: {text}");
        return text;
    }

    private static string LifeText(UpkeepSlot s) => s.LifeHi >= 30f ? "넉넉" : $"{s.LifeLo:0.#}~{s.LifeHi:0.#}일";
}
