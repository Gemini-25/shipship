using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.27 ① 훈련 설계: 한가한 날 컴퓨터가 불 · 감압 훈련을 짠다 (방침 '비상 훈련'이 없음이면 안 한다).
//  아침 방송에 시각을 알리고 → 그 시각에 "훈련입니다 — ○○ 불 가정, 비상 배치표 자리로" → 들은 사람은 제 자리로 걷는다 (진짜 걸음 · 걸린 시간).
//  사람마다 다르다: 진지한 사람(성실 · 안전 · 겪은 사고)은 바로 뛰고, 귀찮아하는 사람(자유 · 지침 · 컴퓨터를 덜 믿음)은 커피를 마저 마시거나 안 온다.
//  결과로 비상 배치표를 고친다: 제 자리에 늦거나 안 온 사람 대신 그 자리에 더 빨리 닿는 사람을 앞에 세운다.
//  자기 계획도 시험한다: 컴퓨터가 미리 셈한 집결 시간과 실제를 견줘 다음 셈을 고친다 (딜레마 · 대피 안내가 이 값을 쓴다).
//  방송이 안 닿은 방이 드러나면 일지에 남긴다. 귀찮아한 사람에겐 다음엔 이유부터 말한다.

public sealed class DrillRun
{
    public int Id { get; init; }
    public string Kind { get; init; } = "불";
    public int RoomId { get; init; } = -1;
    public string Room { get; init; } = "";
    public long Start { get; set; }
    public long End { get; set; } = -1;
    public bool Begun, Done;
    public SortedDictionary<int, StationRole> Role { get; } = new();
    public SortedDictionary<int, Cell> Spot { get; } = new();
    public SortedDictionary<int, Cell> From { get; } = new();
    public SortedDictionary<int, long> Go { get; } = new();
    public SortedDictionary<int, float> Arrive { get; } = new();
    public List<int> Skipped { get; } = new();
    public List<int> Grumbled { get; } = new();
    public List<int> Serious { get; } = new();
    public List<int> NotHeard { get; } = new();
    public float Expect, Actual;
    public List<string> Changes { get; } = new();
    public string Summary { get; set; } = "";
}

public sealed partial class ShipMate
{
    public List<DrillRun> Drills { get; } = new();
    public DrillRun? NextDrill { get; private set; }
    public DrillRun? ActiveDrill { get; private set; }
    public int BillChanges;
    /// <summary>집결 시간 셈의 보정 (훈련에서 배운다 — 1 = 처음 셈 그대로).</summary>
    public float MusterFactor { get; private set; } = 1f;
    /// <summary>훈련 태도 (+ 진지 · − 귀찮아함) — 다음엔 이유부터 말한다.</summary>
    private readonly SortedDictionary<int, int> _attitude = new();
    private int _drillId = 1;

    private bool Calm(float hours)
    {
        var w = _w;
        return Crisis.Level(w) == CrisisLevel.Calm && w.Fire.Count == 0 && !w.Command.Active && w.Cosmic.Main == null
               && !w.History.Events.Any(e => e.Kind == HistoryKind.Incident && w.Tick - e.Tick < SimTime.Hours(hours));
    }

    private void DrillTick()
    {
        var w = _w;
        if (ActiveDrill is DrillRun d) { if (w.Tick - d.Start > SimTime.Minutes(35) || d.Role.Keys.All(id => d.Arrive.ContainsKey(id) || d.Skipped.Contains(id))) Finish(d); return; }
        if (!Up) return;
        if (NextDrill is DrillRun n)
        {
            if (w.Tick < n.Start) return;
            if (!Calm(4f) || Crisis.Acting(w)) { n.Start = w.Tick + SimTime.Hours(2); if (Hour(n.Start) > 18f) NextDrill = null; return; }
            Begin(n);
            return;
        }
        int pol = w.Policies["drills"];
        float gap = pol == 2 ? 2f : 4f;
        float h = Hour(w.Tick);
        if (pol == 0 || w.Tick < SimTime.TicksPerDay * 1.2f || h < 5f || h >= 9f) return;
        if (Drills.Count > 0 && w.Tick - Drills[^1].Start < SimTime.TicksPerDay * gap) return;
        if (!Calm(12f) || w.Board.Open.Count(o => o.Urgency >= 0.7f) >= 3) return; // 한가한 날만
        PlanDrill(null, -1);
    }

    /// <summary>훈련을 짠다 (시험은 종류 · 시각을 준다).</summary>
    public DrillRun? PlanDrill(string? kind, long at)
    {
        var w = _w;
        kind ??= Drills.Count % 2 == 0 ? "불" : "감압";
        Room? room = kind == "불"
            ? w.Ship.LiveRooms.Where(r => !r.OffLimits && r.Type is RoomType.Galley or RoomType.Engine or RoomType.Workshop or RoomType.Power or RoomType.Reactor).OrderBy(r => r.Id).FirstOrDefault()
            : w.Ship.LiveRooms.Where(r => !r.OffLimits && r.Type is not RoomType.Corridor && r.Doors.Count > 0).OrderByDescending(r => r.Cells.Count).ThenBy(r => r.Id).FirstOrDefault();
        room ??= w.Ship.LiveRooms.Where(r => !r.OffLimits && r.Type != RoomType.Corridor).OrderBy(r => r.Id).FirstOrDefault();
        if (room == null) return null;
        long day0 = w.Tick - w.Tick % SimTime.TicksPerDay;
        long start = at >= 0 ? at : day0 + SimTime.Hours(13 + R.Range(0, 3));
        NextDrill = new DrillRun { Id = _drillId++, Kind = kind, RoomId = room.Id, Room = room.Name, Start = start };
        return NextDrill;
    }

    private Cell? Near(Room r, Vector2Like p)
    {
        Cell? best = null; float bd = float.MaxValue;
        foreach (var c in r.Cells)
        {
            if (!_w.Ship.IsWalkable(c)) continue;
            float d = (c.Center.X - p.X) * (c.Center.X - p.X) + (c.Center.Y - p.Y) * (c.Center.Y - p.Y);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }
    private readonly record struct Vector2Like(float X, float Y);

    /// <summary>역할마다 설 자리.</summary>
    private Cell? StationSpot(StationRole role, Room scene)
    {
        var w = _w;
        var door = scene.Doors.Where(d => !d.Removed).OrderBy(d => d.Id).FirstOrDefault();
        Room? outside = door == null ? null : door.RoomA == scene ? door.RoomB : door.RoomA;
        Room? Of(RoomType t) => w.Ship.LiveRooms.Where(r => r.Type == t && !r.OffLimits).OrderBy(r => r.Id).FirstOrDefault();
        var dc = door != null ? new Vector2Like(door.Cell.Center.X, door.Cell.Center.Y) : new Vector2Like(scene.Center.X, scene.Center.Y);
        switch (role)
        {
            case StationRole.Fire: return (outside != null ? Near(outside, dc) : null) ?? Near(scene, dc);
            case StationRole.Bulkhead: return (outside != null ? Near(outside, dc) : null) ?? Near(scene, dc);
            case StationRole.Power:
                var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault(f => !f.Room.Detached);
                return panel != null ? Near(panel.Room, new Vector2Like(panel.Center.X, panel.Center.Y)) : null;
            case StationRole.Medical: return Of(RoomType.Medbay) is Room mb ? Near(mb, new Vector2Like(mb.Center.X, mb.Center.Y)) : null;
            case StationRole.Eva: return Of(RoomType.Airlock) is Room al ? Near(al, new Vector2Like(al.Center.X, al.Center.Y)) : null;
            default:
                var (sh, _) = Facilities.Best(w.Ship, "shelter", r => !r.Detached && !r.OffLimits && r != scene);
                var muster = sh ?? Of(RoomType.Mess) ?? outside;
                return muster != null ? Near(muster, new Vector2Like(muster.Center.X, muster.Center.Y)) : null;
        }
    }

    private void Begin(DrillRun d)
    {
        var w = _w;
        var a = A;
        NextDrill = null;
        ActiveDrill = d;
        d.Begun = true;
        d.Start = w.Tick;
        var scene = w.Ship.Rooms.FirstOrDefault(r => r.Id == d.RoomId);
        if (scene == null) { ActiveDrill = null; return; }
        string text = $"훈련입니다 — {d.Room} {(d.Kind == "불" ? "불이 났다고" : "공기가 샌다고")} 가정합니다. 비상 배치표 자리로 (실제 상황 아님)";
        var bc = a.Speak.Announce(a.Voice.Style(text), scene, 1);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {text}");
        var heard = bc?.HeardBy ?? new List<int>();
        var dists = new List<float>();
        foreach (var c in w.Crew.OrderBy(c => c.Id))
        {
            if (c.Dead || c.IsChild || !c.CanAct || c.Outside || c.Away || c.Room == null) continue;
            if (!c.IsAwake) continue; // 자는 사람(비번)은 부르지 않는다
            if (!heard.Contains(c.Id)) { d.NotHeard.Add(c.Id); continue; }
            if (c.Job?.Order is WorkOrder jo && jo.Urgency >= 0.8f) continue; // 급한 일 중
            var role = w.CrisisCrew.BillRole(c);
            if (StationSpot(role, scene) is not Cell spot) continue;
            d.Role[c.Id] = role;
            d.Spot[c.Id] = spot;
            d.From[c.Id] = c.Cell;
            float dist = MathF.Sqrt(MathF.Pow(spot.X - c.Cell.X, 2) + MathF.Pow(spot.Y - c.Cell.Y, 2)) * 1.35f;
            dists.Add(0.6f + dist / 55f);
            // 진지함: 성실 · 안전/규칙 · 겪은 사고 · 컴퓨터 믿음 − 자유 · 지침 · 지난번 귀찮아함 (이유를 들으면 조금 낫다)
            int att = _attitude.GetValueOrDefault(c.Id);
            float s = 0.35f * c.Traits.Diligence + (c.Value is CrewValue.Safety or CrewValue.Rules ? 0.2f : 0f) + 0.3f * c.Memory.Trauma + 0.2f * a.Trusts.Of(c)
                      - (c.Value == CrewValue.Freedom ? 0.22f : 0f) - 0.2f * c.Needs.Stress - 0.15f * (1f - c.Needs.Rest) + (att < 0 ? 0.12f : 0f) + R.Range(-0.08f, 0.08f);
            if (att < 0) a.Apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "훈련", "지난번 불 때 제 자리까지 4분이 걸렸다 — 그래서 다시 한다. 2분이면 막을 불이었다"));
            if (s < 0.28f) { d.Skipped.Add(c.Id); c.Say(w, Persona.Say(c, "또 훈련이야? 이것만 끝내고")); continue; }
            long go = w.Tick;
            if (s < 0.45f) { d.Grumbled.Add(c.Id); go += SimTime.Minutes(3 + R.Range(0, 5)); c.Say(w, Persona.Say(c, "훈련이라며. 커피는 마시고 가자")); }
            else d.Serious.Add(c.Id);
            d.Go[c.Id] = go;
            c.Interrupt(w);
        }
        dists.Sort();
        d.Expect = dists.Count == 0 ? 0f : dists[dists.Count / 2] * MusterFactor;
    }

    /// <summary>훈련 행동이 읽는다: 이 사람이 지금 갈 자리.</summary>
    public Cell? DrillSpot(CrewMember c) =>
        ActiveDrill is DrillRun d && d.Go.TryGetValue(c.Id, out var go) && _w.Tick >= go && !d.Arrive.ContainsKey(c.Id) && d.Spot.TryGetValue(c.Id, out var s) ? s : null;

    internal void Arrived(CrewMember c)
    {
        if (ActiveDrill is not DrillRun d || d.Arrive.ContainsKey(c.Id)) return;
        d.Arrive[c.Id] = (_w.Tick - d.Start) / (float)SimTime.TicksPerHour * 60f;
    }

    private void Finish(DrillRun d)
    {
        var w = _w;
        var a = A;
        ActiveDrill = null;
        d.Done = true;
        d.End = w.Tick;
        Drills.Add(d);
        if (Drills.Count > 20) Drills.RemoveAt(0);
        var times = d.Arrive.Values.OrderBy(x => x).ToList();
        d.Actual = times.Count == 0 ? 0f : times[times.Count / 2];
        var bill = w.CrisisCrew.Bill;
        var scene = w.Ship.Rooms.FirstOrDefault(r => r.Id == d.RoomId);
        // 배치표 고치기: 늦거나 안 온 사람의 자리에, 그 자리에 더 빨리 닿을 사람을 (진지하게 온 사람 중)
        int swaps = 0;
        foreach (var (id, role) in d.Role.ToList())
        {
            if (swaps >= 2 || role == StationRole.None || scene == null) break;
            float mine = d.Arrive.TryGetValue(id, out var t0) ? t0 : 99f;
            if (mine < MathF.Max(2.5f, d.Actual * 1.6f)) continue;
            if (StationSpot(role, scene) is not Cell spot) continue;
            int pick = -1; float best = mine * 0.6f;
            foreach (var (oid, orole) in d.Role)
            {
                if (oid == id || orole == role || !d.Arrive.ContainsKey(oid) || d.Changes.Any(x => x.Contains(Crew(oid)?.Name ?? "?"))) continue;
                var from = d.From[oid];
                float est = 0.6f + MathF.Sqrt(MathF.Pow(spot.X - from.X, 2) + MathF.Pow(spot.Y - from.Y, 2)) * 1.35f / 55f;
                if (est < best) { best = est; pick = oid; }
            }
            if (pick < 0 || Crew(id) is not CrewMember slow || Crew(pick) is not CrewMember fast) continue;
            var other = bill.Of.GetValueOrDefault(pick);
            bill.Of[pick] = role;
            bill.Of[id] = other;
            if (bill.Order.TryGetValue(role, out var ord)) { ord.Remove(pick); ord.Insert(0, pick); }
            swaps++;
            d.Changes.Add($"{CrisisCrewSystem.RoleName(role)}: {slow.Name} → {fast.Name}");
            Life.Diary(w, fast, Persona.Say(fast, $"훈련 뒤 비상 배치표에 내 자리가 {CrisisCrewSystem.RoleName(role)}로 바뀌었다"));
            Life.Diary(w, slow, Persona.Say(slow, d.Skipped.Contains(id) ? "훈련에 안 갔더니 내 자리가 바뀌었다" : "훈련에서 늦었다. 자리가 바뀌었다"));
        }
        // 안 온 사람은 자기 자리 차례에서 뒤로 (대신할 사람이 먼저 나서게)
        foreach (var id in d.Skipped)
            if (d.Role.TryGetValue(id, out var r) && bill.Order.TryGetValue(r, out var ord) && ord.Remove(id)) { ord.Add(id); if (swaps == 0) d.Changes.Add($"{CrisisCrewSystem.RoleName(r)} 차례: {Crew(id)?.Name} 뒤로"); }
        if (d.Changes.Count > 0)
        {
            bill.Version++;
            bill.Drawn = w.Tick;
            bill.Why = $"{d.Kind} 훈련 결과";
            BillChanges += d.Changes.Count;
        }
        // 손에 익는다 · 태도를 기억한다
        foreach (var (id, t) in d.Arrive)
            if (Crew(id) is CrewMember c)
            {
                w.CrisisCrew.Drilled(c);
                c.DrilledUntil = Math.Max(c.DrilledUntil, w.Tick + SimTime.TicksPerDay * 5);
                _attitude[id] = Math.Min(3, _attitude.GetValueOrDefault(id) + (d.Serious.Contains(id) ? 1 : 0));
                if (d.Serious.Contains(id)) { a.Trusts.Change(c, 0.01f, "훈련이 쓸모 있었다", quiet: true); Life.Diary(w, c, Persona.Say(c, $"{d.Kind} 훈련 — 내 자리까지 {t:0.0}분. 다음엔 더 빨리")); }
            }
        foreach (var id in d.Skipped) { _attitude[id] = Math.Max(-3, _attitude.GetValueOrDefault(id) - 1); if (Crew(id) is CrewMember c) Life.Diary(w, c, Persona.Say(c, "훈련이라길래 하던 걸 마저 했다")); }
        // 자기 계획 시험: 셈한 집결 시간 ↔ 실제
        string self = "";
        if (times.Count > 0 && d.Expect > 0.1f)
        {
            float ratio = Math.Clamp(d.Actual / d.Expect, 0.5f, 3f);
            float before = MusterFactor;
            MusterFactor = Math.Clamp(MusterFactor * (0.6f + 0.4f * ratio), 0.6f, 3f);
            self = $" · 내 셈은 {d.Expect:0.0}분, 실제 {d.Actual:0.0}분 — 다음 셈은 {(MusterFactor / before):0.0#}배로 고친다";
        }
        string heardNote = d.NotHeard.Count > 0 ? $" · 방송을 못 들은 사람 {d.NotHeard.Count}명" : "";
        d.Summary = $"{d.Kind} 훈련 ({d.Room}): {d.Arrive.Count}명이 자리에 섰다 (가운데 {d.Actual:0.0}분){(d.Skipped.Count > 0 ? $" · 안 온 사람 {d.Skipped.Count}" : "")}"
                    + (d.Changes.Count > 0 ? $" · 배치표 고침: {string.Join(" · ", d.Changes)}" : "") + heardNote;
        Say(d.Summary + self, scene, 0);
        w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터가 짠 {d.Summary}", scene, d.Arrive.Keys.Select(Crew).Where(c => c != null)!, log: false);
    }
}
