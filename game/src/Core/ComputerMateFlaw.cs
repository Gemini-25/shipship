using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.27 ④ 약점과 사건.
//  기억 손상: 컴퓨터는 정비 기록을 제 기억에 둔다. 우주 방사선(태양 폭풍)이 기억 칸 하나를 틀면 "어제 정비했다"고 믿고 정비표에서 뺀다 —
//    아침 방송에 그대로 나간다. 그 설비를 아는 사람(정비 솜씨 · 그 방 사람)이 듣고 "아무도 안 손봤는데?" → 컴퓨터가 고치고 자기 기억 전체를 검사한다
//    → 다른 틀어진 칸도 찾는다 · 약속("작업 일지와 맞춰 보고 말하겠다"). 아무도 못 알아채면 그 설비가 정비 없이 멎는다 (사고 뒤 검토).
//  과부하: 연산이 몰리면 덜 급한 경보를 놓친다 — 숨이 트이면 다시 읽고 "몇 분 늦게 봤다"고 인정한다 (몰릴 때마다 원인 분석 → 코어 랙 증설안).
//  성격 탓 실수: 과감하면 남은 수명을 넉넉히 보고(낙관) · 신중하면 헛걱정으로 일을 미룬다(늦음) — 사고 뒤 검토에 성격이 원인으로 적힌다.
//  권한 밖 딜레마: "그 방을 닫으면 둘이 갇히지만 옆방 여럿이 산다" (방침이 '사람 우선'이면 컴퓨터 권한 밖) → 함장에게 묻는다.
//    함장과 연락이 안 되면(원정 · 쓰러짐 · 밖 · 스피커 고장) 컴퓨터가 정한다 — 사람을 먼저 챙기는 컴퓨터는 기다리고, 배를 먼저 지키는 컴퓨터는 닫는다.

public enum MemKind { Service }

public sealed class MemEntry
{
    public string Key { get; init; } = "";
    public MemKind Kind { get; init; }
    public int RefId { get; init; }
    public string Label { get; init; } = "";
    public long Value { get; set; }
    public bool Corrupt { get; set; }
    public long CorruptAt { get; set; } = -1;
    public long Claimed { get; set; } = -1;
    public long Fixed { get; set; } = -1;
}

public sealed class MissedAlarm
{
    public long Serial { get; init; }
    public long Tick { get; init; }
    public string Text { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public long Admitted = -1;
}

public sealed class MateReview
{
    public long Tick { get; init; }
    public string Temper { get; init; } = "";
    public string Text { get; init; } = "";
}

public sealed class ShipDilemma
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public int RoomId { get; init; }
    public string Room { get; init; } = "";
    public string Hazard { get; init; } = "";
    public List<int> Trapped { get; } = new();
    public int Saved { get; init; }
    public bool AskedCaptain, Reachable;
    public int CaptainId = -1;
    public string Unreachable = "";
    public long DecideAt;
    public string Decision = "";
    public string By = "";
    public long Decided = -1, Closed = -1, Released = -1;
    public string Outcome = "";
    public List<int> Locked { get; } = new();
}

public sealed partial class ShipMate
{
    public SortedDictionary<string, MemEntry> Memory { get; } = new(StringComparer.Ordinal);
    public int Corruptions, Corrections, MemoryChecks;
    public long LastMemoryCheck = -1;
    public string LastMemoryNote = "";
    public List<MissedAlarm> MissedAlarms { get; } = new();
    public int Admitted;
    public List<MateReview> Reviews { get; } = new();
    public List<ShipDilemma> Dilemmas { get; } = new();
    /// <summary>시험용: 연산이 몰렸다고 친다.</summary>
    public bool ForceOverload;
    private long _alertSeen = -1, _overSince = -1, _dilNext;
    private int _dilId = 1;

    // ───────────── 기억 ─────────────

    internal void Remember(Machine m)
    {
        string key = "svc:" + m.Body.Id;
        if (Memory.TryGetValue(key, out var e)) { if (!e.Corrupt) e.Value = m.LastServiced; return; }
        Memory[key] = new MemEntry { Key = key, Kind = MemKind.Service, RefId = m.Body.Id, Label = m.Name, Value = m.LastServiced };
    }

    /// <summary>기억으로는 이틀 안에 정비했다 (틀어졌으면 틀린 믿음).</summary>
    private bool BelievedFresh(Machine m) => Memory.TryGetValue("svc:" + m.Body.Id, out var e) && e.Value > 0 && _w.Tick - e.Value < SimTime.TicksPerDay * 2 && (e.Corrupt || m.Wear < 0.3f);

    /// <summary>방사선이 기억 칸 하나를 튼다 (시험도 부른다).</summary>
    public MemEntry? CorruptMemory(Machine? target = null)
    {
        var w = _w;
        MemEntry? e;
        if (target != null) { Remember(target); e = Memory["svc:" + target.Body.Id]; }
        else
        {
            var pool = Memory.Values.Where(x => !x.Corrupt && w.Ship.Machines.FirstOrDefault(m => m.Body.Id == x.RefId) is Machine mm && mm.Wear > 0.3f).ToList();
            if (pool.Count == 0) return null;
            e = pool[R.Range(0, pool.Count)];
        }
        e.Corrupt = true;
        e.CorruptAt = w.Tick;
        e.Claimed = -1;
        e.Value = w.Tick - SimTime.Hours(20); // "어제 정비했다"
        Corruptions++;
        Slots.RemoveAll(s => s.MachineId == e.RefId && !s.Done && !s.Missed && !s.Asked); // 틀린 믿음으로 정비표에서 빠진다
        return e;
    }

    private void FlawHour()
    {
        var w = _w;
        // 정비 기록을 기억에 옮긴다 (작업 기록으로 안다)
        foreach (var m in w.Ship.Machines) if (m.LastServiced > 0 && Trends.ContainsKey(m.Body.Id)) Remember(m);
        // 방사선 — 기억이 틀어진다
        bool rad = w.Hazards.StormActive && w.Hazards.StormPeak > 0.4f;
        if (rad && A.ComputerBody?.Room is Room cr && R.Chance(Hardened(cr) ? 0.03f : 0.08f)) CorruptMemory();
        // 아무도 못 알아채 그 설비가 멎었다 — 검토
        foreach (var e in Memory.Values)
        {
            if (!e.Corrupt || w.Ship.Machines.FirstOrDefault(m => m.Body.Id == e.RefId) is not Machine m || m.Faults.Count == 0) continue;
            e.Corrupt = false; e.Fixed = w.Tick; e.Value = m.LastServiced;
            Review("기억", $"기억이 틀어져 {e.Label}을 어제 정비했다고 믿고 정비표에서 뺐다 — 정비 없이 멎었다");
            MemoryCheck(null);
        }
        // 미뤄 둔 딜레마 · 인정
        if (MissedAlarms.Any(m => m.Admitted < 0) && !Overloaded_()) AdmitMissed();
    }

    /// <summary>아침 방송을 들은 사람이 틀린 말을 알아챈다.</summary>
    private void HeardBriefing(Briefing b)
    {
        var w = _w;
        foreach (var e in Memory.Values.Where(x => x.Corrupt && x.Claimed >= b.Tick && x.Fixed < 0).ToList())
        {
            var m = w.Ship.Machines.FirstOrDefault(x => x.Body.Id == e.RefId);
            if (m == null) continue;
            foreach (var id in b.Heard)
            {
                if (Crew(id) is not CrewMember c || c.IsChild || !c.CanAct) continue;
                float p = 0.15f + 0.5f * c.SkillLevel(m.Spec.Skill) + (c.Room == m.Body.Room ? 0.2f : 0f) + 0.15f * c.Traits.Diligence + (c.Role == CrewRole.Technician ? 0.15f : 0f);
                if (!R.Chance(p)) continue;
                Corrected(e, m, c);
                break;
            }
        }
    }

    /// <summary>승무원이 바로잡았다 → 고치고 · 기억 전체를 검사한다.</summary>
    internal void Corrected(MemEntry e, Machine m, CrewMember c)
    {
        var w = _w;
        var a = A;
        c.Say(w, Persona.Say(c, $"{m.Name} 어제 아무도 안 손봤는데? {TrendWord(m)}도 그대로야"));
        e.Corrupt = false; e.Fixed = w.Tick; e.Value = m.LastServiced;
        Corrections++;
        a.Manner.Of(c).Corrections.Add((w.Tick, $"{m.Name} 정비 기록"));
        a.Manner.CorrectionsHeard++;
        a.Trusts.Change(c, 0.02f, "컴퓨터가 내 말을 듣고 고쳤다", quiet: true);
        Life.Diary(w, c, Persona.Say(c, $"아침 방송이 이상했다. {m.Name}은 어제 아무도 안 만졌는데. 말했더니 컴퓨터가 고맙다고 했다"));
        Say($"{c.Name} 말이 맞다 — {m.Name} 정비 기록이 틀어져 있었다 (방사선으로 기억 칸이 바뀐 듯하다). 기억 전체를 검사한다", m.Body.Room, 1, c.Id);
        MemoryCheck(c);
        if (Promised("memory") == null) MakePromise("memory", "정비 기록은 손으로 쓴 작업 일지와 맞춰 보고 말하겠다", w.Crew.Where(x => !x.Dead && !x.IsChild));
        ForcePlan = true;
    }

    /// <summary>자기 기억 검사: 모든 칸을 실제 작업 기록과 맞춘다.</summary>
    public int MemoryCheck(CrewMember? by)
    {
        var w = _w;
        int bad = 0;
        foreach (var e in Memory.Values)
        {
            var m = w.Ship.Machines.FirstOrDefault(x => x.Body.Id == e.RefId);
            if (m == null) continue;
            if (e.Corrupt || e.Value != m.LastServiced && e.Fixed < 0 && MathF.Abs(e.Value - m.LastServiced) > SimTime.Hours(1)) { bad++; e.Corrupt = false; e.Fixed = w.Tick; }
            e.Value = m.LastServiced;
        }
        MemoryChecks++;
        LastMemoryCheck = w.Tick;
        LastMemoryNote = $"기억 검사 — {Memory.Count}칸 중 {bad}칸이 더 틀어져 있어 고쳤다";
        Say(LastMemoryNote + (by != null ? $" ({by.Name}이 알려 줬다)" : ""));
        return bad;
    }

    /// <summary>약속('작업 일지와 맞춰 보고')이 있으면 방송 전에 맞춰 본다.</summary>
    private void CrossCheck()
    {
        if (Promised("memory") is not Promise p) return;
        var bad = Memory.Values.Where(x => x.Corrupt && x.Claimed < 0).ToList();
        if (bad.Count == 0) return;
        bool kept = A.Load < 0.95f && !ForceOverload;
        if (kept) { foreach (var e in bad) { e.Corrupt = false; e.Fixed = _w.Tick; e.Value = _w.Ship.Machines.FirstOrDefault(m => m.Body.Id == e.RefId)?.LastServiced ?? e.Value; } ForcePlan = true; }
        TestPromise(p, kept, kept ? $"작업 일지와 맞춰 보니 {bad[0].Label} 기록이 틀어져 있었다 — 말하기 전에 고쳤다" : "일이 몰려 맞춰 보지 못하고 방송했다");
    }

    // ───────────── 과부하 · 놓친 경보 ─────────────

    private bool Overloaded_() => ForceOverload || A.Load > 0.95f;

    private void FlawTick()
    {
        var w = _w;
        if (_alertSeen < 0) _alertSeen = w.AlertSerial;
        bool over = Overloaded_();
        if (over && _overSince < 0) { _overSince = w.Tick; Overloaded(); }
        else if (!over) _overSince = -1;
        if (w.AlertSerial != _alertSeen)
        {
            for (int i = w.Alerts.Count - 1; i >= 0; i--)
            {
                var al = w.Alerts[i];
                if (al.Serial <= _alertSeen) break;
                if (!over || al.Level == AlertLevel.Critical || !Up) continue;
                if (R.Chance(0.6f)) MissedAlarms.Add(new MissedAlarm { Serial = al.Serial, Tick = al.Tick, Text = al.Text, RoomId = al.Room?.Id ?? -1 });
            }
            _alertSeen = w.AlertSerial;
            if (MissedAlarms.Count > 40) MissedAlarms.RemoveRange(0, MissedAlarms.Count - 40);
        }
        if (!over && Up && (w.Tick & 63) == 0 && MissedAlarms.Any(m => m.Admitted < 0)) AdmitMissed();
        if (w.Tick >= _dilNext) { _dilNext = w.Tick + 30; Dilemma(); }
    }

    private void AdmitMissed()
    {
        var w = _w;
        var a = A;
        var list = MissedAlarms.Where(m => m.Admitted < 0).ToList();
        if (list.Count == 0) return;
        foreach (var m in list) m.Admitted = w.Tick;
        Admitted++;
        var first = list[0];
        float late = (w.Tick - first.Tick) / (float)SimTime.TicksPerHour * 60f;
        Say($"일이 몰린 사이 덜 급한 경보 {list.Count}건을 늦게 봤다 — 처음 것 \"{ChronicleBook.Short(first.Text, 40)}\"은 {late:0}분 늦었다. 지금 다시 읽는다", null, 0);
        a.Authority.Learned("부하", $"연산이 몰려 경보 {list.Count}건을 놓쳤다 — 덜 급한 경보도 모아 두었다가 다시 읽는다");
        if (Promised("alarm") is Promise p) TestPromise(p, late < 30f, late < 30f ? $"숨이 트이자 곧바로 다시 읽었다 ({late:0}분)" : $"다시 읽기까지 {late:0}분이나 걸렸다");
        else MakePromise("alarm", "몰릴 땐 덜 급한 경보를 모아 두었다가 숨이 트이면 바로 다시 읽겠다", w.Crew.Where(c => !c.Dead && !c.IsChild && c.IsAwake));
    }

    // ───────────── 성격 탓 실수 (사고 뒤 검토) ─────────────

    internal void Review(string kind, string text)
    {
        var w = _w;
        var a = A;
        string temper = a.Character.Temper;
        var r = new MateReview { Tick = w.Tick, Temper = temper, Text = $"검토 — {text}" };
        Reviews.Add(r);
        if (Reviews.Count > 30) Reviews.RemoveAt(0);
        var ar = new AfterReview { Id = 9000 + Reviews.Count + Corruptions * 50, Tick = w.Tick, PlanId = -1, Title = $"{kind}: {ChronicleBook.Short(text, 40)}", Miss = true };
        ar.Lines.Add(text);
        if (temper != "중립") ar.Lines.Add($"내 성격({temper}) 탓이 컸다");
        a.Review.Reviews.Add(ar);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call} 사고 뒤 검토: {text}");
    }

    private void OnMissed(UpkeepSlot s, Machine m)
    {
        var ch = A.Character;
        if (ch.Caution < -0.25f && s.At - s.Planned > SimTime.Hours(s.LifeLo * 24f * 0.75f))
        {
            Review("정비", $"남은 수명을 {s.LifeLo:0.#}~{s.LifeHi:0.#}일로 보고 넉넉하게 미뤘다 (과감하게 낙관했다) — {s.Machine}이 정비 전에 멎었다");
            ch.Nudge(0.08f, 0f, $"{s.Machine} 정비를 낙관해 미뤘다");
        }
        else Review("정비", $"{s.Machine} — 정비표에 올렸지만 {(s.Asked ? "손이 닿기 전에" : "맡을 사람을 찾기 전에")} 멎었다");
    }

    // ───────────── 권한 밖 딜레마 ─────────────

    private void Dilemma()
    {
        var w = _w;
        var a = A;
        // 진행 중인 딜레마
        foreach (var d in Dilemmas)
        {
            if (d.Decided < 0 && w.Tick >= d.DecideAt) Decide(d);
            else if (d.Closed >= 0 && d.Released < 0) Release(d);
        }
        if (!Up || w.Policies["decompress"] != 0) return; // '배 우선'이면 권한 안 (원래 수순이 닫는다)
        foreach (var r in w.Ship.LiveRooms)
        {
            bool fire = w.Fire.IsKnown(r) && w.Fire.CountIn(r) >= 2, leak = r.Leaking && r.Air.Pressure < 85f;
            if (!fire && !leak || r.Type == RoomType.Corridor) continue;
            if (Dilemmas.Any(d => d.RoomId == r.Id && w.Tick - d.Tick < SimTime.Hours(2))) continue;
            var inside = w.Crew.Where(c => !c.Dead && c.Room == r).ToList();
            if (inside.Count == 0 || inside.Count > 3) continue;
            int near = 0;
            foreach (var dr in r.Doors) { var o = dr.RoomA == r ? dr.RoomB : dr.RoomA; if (o != null) foreach (var c in w.Crew) if (!c.Dead && c.Room == o) near++; }
            if (near < inside.Count * 2 || r.Doors.All(d => d.Locked || d.Removed)) continue;
            StartDilemma(r, inside, near, fire ? "불" : "공기");
            return;
        }
    }

    /// <summary>딜레마를 연다 (시험도 부른다).</summary>
    public ShipDilemma StartDilemma(Room r, List<CrewMember> inside, int near, string hazard)
    {
        var w = _w;
        var a = A;
        var d = new ShipDilemma { Id = _dilId++, Tick = w.Tick, RoomId = r.Id, Room = r.Name, Hazard = hazard, Saved = near };
        d.Trapped.AddRange(inside.Select(c => c.Id));
        Dilemmas.Add(d);
        if (Dilemmas.Count > 20) Dilemmas.RemoveAt(0);
        string q = $"{r.Name}을 닫으면 {string.Join("·", inside.Select(c => c.Name))} {inside.Count}명이 갇히지만 {(hazard == "불" ? "불길 · 연기" : "새는 공기")}가 옆 {near}명에게 안 간다";
        var cap = w.Command.Captain;
        d.CaptainId = cap?.Id ?? -1;
        d.Unreachable = cap == null ? "함장이 없다" : cap.Away ? "함장이 원정 중" : !cap.CanAct ? "함장이 쓰러졌다" : cap.Outside ? "함장이 밖에 있다"
            : cap.Room == null || !a.Speak.SpeakerWorks(cap.Room) ? "함장 쪽 스피커가 안 된다" : cap.Room == r ? "함장이 그 방 안에 있다" : "";
        d.Reachable = d.Unreachable == "";
        d.AskedCaptain = d.Reachable;
        if (d.Reachable)
        {
            if (!cap!.IsAwake) cap.Jolt(w);
            d.DecideAt = w.Tick + SimTime.Minutes(cap.IsAwake ? 1f : 3f);
            Say($"함장님 — 권한 밖입니다. {q}. 닫을까요?", cap.Room, 2, cap.Id);
        }
        else
        {
            var ch = a.Character;
            float wait = ch.PeopleTilt > 0.25f ? 4f : ch.PeopleTilt < -0.25f ? 0.5f : ch.Caution > 0.25f ? 3f : 1.5f;
            d.DecideAt = w.Tick + SimTime.Minutes(wait);
            Say($"{q}. {d.Unreachable} — 연락이 안 된다. 내가 정해야 한다", r, 2);
        }
        a.Authority.Dilemma("사람 ↔ 배", q, d.Reachable ? "함장에게 물었다" : "함장과 연락이 안 돼 컴퓨터가 정한다", "방침 '사람 우선' — 사람이 있는 방을 닫는 건 컴퓨터 권한 밖", inside);
        return d;
    }

    private void Decide(ShipDilemma d)
    {
        var w = _w;
        var a = A;
        var r = w.Ship.Rooms.FirstOrDefault(x => x.Id == d.RoomId);
        d.Decided = w.Tick;
        if (r == null) { d.Decision = "없던 일"; return; }
        var still = d.Trapped.Select(Crew).Where(c => c != null && !c.Dead && c.Room == r).ToList();
        bool close;
        if (d.Reachable && Crew(d.CaptainId) is CrewMember cap && cap.CanAct)
        {
            float aff = still.Count == 0 ? 0f : still.Max(c => cap.AffinityTo(c!));
            float s = 0.45f * cap.Traits.Bravery + (cap.Value == CrewValue.Efficiency ? 0.2f : 0f) - (cap.Value == CrewValue.People ? 0.3f : 0f) - 0.4f * MathF.Max(0f, aff) + 0.06f * d.Saved / MathF.Max(1, still.Count) - 0.1f;
            close = still.Count == 0 || s > 0.2f;
            d.By = $"함장 {cap.Name}";
            cap.Say(w, Persona.Say(cap, close ? $"닫아. 옆방 {d.Saved}명을 살려야 해" : "기다려. 저 사람들이 나올 때까지"));
            Life.Diary(w, cap, Persona.Say(cap, close ? $"{d.Room}을 닫으라고 했다. 안에 사람이 있었다" : $"{d.Room}을 열어 두라고 했다. 옳았는지 모르겠다"));
        }
        else
        {
            var ch = a.Character;
            close = still.Count == 0 || ch.PeopleTilt < 0.25f;
            d.By = $"주 컴퓨터 — {(ch.PeopleTilt > 0.25f ? "사람을 먼저 챙기는 성격" : ch.PeopleTilt < -0.25f ? "배를 먼저 지키는 성격" : "기다릴 만큼 기다린 뒤")}";
            if (ch.Caution > 0.25f && close && still.Count > 0) Review("딜레마", $"{d.Room} — 신중하게 확인을 기다리느라 닫기가 늦었다 (그사이 번졌다)");
        }
        d.Decision = close ? "닫는다" : "기다린다";
        if (close)
        {
            d.Closed = w.Tick;
            foreach (var dr in r.Doors) if (!dr.Removed && !dr.IsExternal && !dr.Locked) { dr.Locked = true; d.Locked.Add(dr.Id); }
            Say($"{d.Room}을 닫았다 ({d.By}) — 안의 사람은 반대쪽 문 · 마스크로. 곧 가겠다", r, 2);
        }
        else Say($"{d.Room}을 열어 둔다 ({d.By}) — 안의 사람은 지금 나와라", r, 2);
        w.History.Add(w, HistoryKind.Decision, $"권한 밖 딜레마: {d.Room} ({d.Hazard}) — {d.Decision} · {d.By}{(d.Reachable ? "" : $" ({d.Unreachable})")}", r, still!, log: false);
    }

    private void Release(ShipDilemma d)
    {
        var w = _w;
        var r = w.Ship.Rooms.FirstOrDefault(x => x.Id == d.RoomId);
        bool over = r == null || (w.Fire.CountIn(r) == 0 && !r.Leaking) || w.Tick - d.Closed > SimTime.Hours(1);
        if (!over) return;
        d.Released = w.Tick;
        if (r != null) foreach (var dr in r.Doors) if (d.Locked.Contains(dr.Id)) dr.Locked = false;
        var people = d.Trapped.Select(Crew).Where(c => c != null).ToList();
        int dead = people.Count(c => c!.Dead), down = people.Count(c => !c!.Dead && c.Down);
        d.Outcome = dead > 0 ? $"갇힌 사람 중 {dead}명이 숨졌다" : down > 0 ? $"{down}명이 쓰러졌지만 살았다" : "갇혔던 사람 모두 나왔다";
        if (dead > 0)
            foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild && people.Any(p => p!.AffinityTo(c) > 0.3f || c.AffinityTo(p!) > 0.3f)).OrderBy(c => c.Id))
                A.Trusts.Change(c, d.By.StartsWith("주 컴퓨터", StringComparison.Ordinal) ? -0.1f : -0.03f, $"{d.Room}을 닫아 사람이 갇혔다", quiet: true);
        Say($"{d.Room} 다시 열었다 — {d.Outcome}", r);
    }
}
