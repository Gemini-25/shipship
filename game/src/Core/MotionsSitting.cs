using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.18 따로 여는 회의(긴급 · 재판 · 선거 · 사고 조사 · 잔치 의논): 모인다 → 연다 → 발언 → 표결(손 · 투표함) → 실행.
// 감시: 벌 근무를 빼먹으면 다시 안건 · 창고 수량이 어긋나면 주 컴퓨터가 적는다. 불만은 며칠 간다. 잔치.
public sealed partial class MotionSystem
{
    /// <summary>발언 한 줄에 드는 시간 (말풍선이 차례로 뜬다).</summary>
    public static readonly int LineTicks = SimTime.Minutes(1.5f);
    public static readonly int VoteTicks = SimTime.Minutes(5);
    private long _sittingRetry;

    private Room? PickVenue()
    {
        var w = _w;
        bool Ok(Room r) => !r.Abandoned && !r.Detached && !r.OffLimits && !r.Leaking && Atmosphere.Danger(r) < 0.1f && w.Fire.CountIn(r) == 0;
        return w.Ship.RoomsOf(RoomType.MeetingRoom).FirstOrDefault(Ok)
               ?? w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault(Ok)
               ?? w.Ship.RoomsOf(RoomType.Lounge).FirstOrDefault(Ok);
    }

    private List<CrewMember> Eligible() =>
        _w.Crew.Where(c => Adult(c) && c.CanAct && c.IsAwake && !c.Outside && c.Room != null && c.Vitals.Health > 0.35f).ToList();

    private void Sittings()
    {
        var w = _w;
        if (Now != null) { Step(Now); return; }
        if (Crisis.Acting(w) || w.Meetings.Gathering || w.Meetings.Session != null || w.Tick < _sittingRetry) return;
        var m = All.FirstOrDefault(x => x.Stage == MotionStage.Ready && x.Sitting != SittingKind.Regular);
        if (m == null) return;
        float hour = SimTime.HourOfDay(w.Tick);
        bool urgent = m.Sitting == SittingKind.Emergency;
        if (!urgent && (hour < 8f || hour > 22f)) return;
        if (!urgent && MathF.Abs(hour - w.Meetings.Hour) < 1f) return; // 곧 정기 회의다 — 겹치지 않게
        Convene(m);
    }

    /// <summary>서명이 모인 안건으로 회의를 소집한다 (깨어 있는 사람을 부른다).</summary>
    public Sitting? Convene(Motion m)
    {
        var w = _w;
        var venue = PickVenue();
        var eligible = Eligible();
        if (venue == null || eligible.Count < 3) { _sittingRetry = w.Tick + SimTime.Hours(1); return null; }
        var s = new Sitting { Motion = m, Kind = m.Sitting, Venue = venue, Called = w.Tick };
        foreach (var c in eligible) { s.Invited.Add(c.Id); c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1 + c.Id % 4); }
        if (m.Target >= 0 && P(m.Target) is CrewMember t && !t.Dead && t.CanAct) s.Invited.Add(t.Id);
        m.Stage = MotionStage.Sitting;
        Now = s;
        Stats.Sittings++;
        if (m.Sitting == SittingKind.Feast) Stats.Feasts++;
        if (m.Sitting == SittingKind.Inquiry) Stats.Inquiries++;
        w.Log.Add(w.Tick, LogKind.Ship, $"{SittingName(m.Sitting)} — {venue.Name}에 모인다: {m.Title} (서명 {m.Signers.Count}장 · {s.Invited.Count}명)");
        return s;
    }

    private void Postpone(Sitting s, string why)
    {
        var w = _w;
        s.Motion.Stage = MotionStage.Ready;
        s.Motion.Deadline = Math.Max(s.Motion.Deadline, w.Tick + SimTime.TicksPerDay);
        Now = null;
        _sittingRetry = w.Tick + SimTime.Hours(3);
        w.Log.Add(w.Tick, LogKind.Ship, $"{SittingName(s.Kind)} — {why}");
    }

    private void Step(Sitting s)
    {
        var w = _w;
        if (s.Opened < 0)
        {
            if (Crisis.Acting(w) || s.Venue.Leaking || w.Fire.CountIn(s.Venue) > 0) { Postpone(s, "비상이라 흩어졌다"); return; }
            foreach (var id in s.Invited.ToList())
            {
                var c = P(id);
                if (c == null || c.Dead || c.Down || c.Pose == Pose.Sleeping && c.Room != s.Venue) { s.Invited.Remove(id); s.Present.Remove(id); continue; }
                if (c.Room == s.Venue) s.Present.Add(id);
            }
            long waited = w.Tick - s.Called;
            int need = Math.Max(3, (int)MathF.Ceiling(s.Invited.Count * 0.6f));
            bool targetHere = s.Motion.Target < 0 || s.Motion.Kind == MotionKind.Confidence || s.Present.Contains(s.Motion.Target) || waited > SimTime.Minutes(30);
            if (targetHere && (s.Present.Count >= s.Invited.Count && s.Present.Count >= 3 || s.Present.Count >= need && waited >= SimTime.Minutes(8) || waited >= SimTime.Minutes(25) && s.Present.Count >= 3))
            {
                OpenSitting(s);
                return;
            }
            if (waited >= SimTime.Minutes(50)) Postpone(s, "사람이 모이지 않아 미뤘다");
            return;
        }
        if (w.Tick < s.End) return;
        s.Apply?.Invoke();
        s.Apply = null;
        Past.Add(s);
        if (Past.Count > 12) Past.RemoveAt(0);
        Now = null;
        foreach (var id in s.Present) if (P(id) is CrewMember c) c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1 + c.Id % 3);
    }

    /// <summary>모였다 — 의장이 열고, 발언 · 근거 · 표결을 정한다 (말풍선은 차례로 뜨고 결과는 표결 뒤에 실행된다).</summary>
    public void OpenSitting(Sitting s)
    {
        var w = _w;
        var m = s.Motion;
        var attendees = s.Present.Select(P).Where(c => c != null && !c.Dead).Select(c => c!).OrderBy(c => c.Id).ToList();
        var cap = w.Command.Captain;
        var pool = attendees.Where(c => c.Id != m.Target).ToList();
        if (pool.Count == 0) { Postpone(s, "의장을 맡을 사람이 없다"); return; }
        var chair = cap != null && pool.Contains(cap) && m.Kind != MotionKind.Confidence ? cap : pool.OrderByDescending(CommandSystem.Leadership).ThenBy(c => c.Id).First();
        var voters = pool.Where(c => !NoVote(c) && !w.Society.OnProbation(c) && (m.Kind != MotionKind.Confidence || c != cap)).ToList();
        if (voters.Count < 2) { Postpone(s, "표를 던질 사람이 모자라다"); return; }
        s.Opened = w.Tick;
        s.Chair = chair.Id;
        var item = Resolve(m, voters, attendees, chair, s.Script, out var apply);
        s.Apply = apply;
        s.Voters.AddRange(voters.Select(c => c.Id));
        // 손 들기: 찬성(재판은 엄하게) 쪽 · 비밀이면 모두 투표함에 넣는다
        foreach (var c in voters) if (m.Secret || m.Final.GetValueOrDefault(c.Id) > 0f) s.Hands.Add(c.Id);
        var rec = new MeetingRecord { Id = w.Meetings.Minutes.Count + 1, Kind = MeetingKind.AdHoc, Tick = w.Tick, Chair = chair.Id, Venue = s.Venue.Name, Attendees = attendees.Select(c => c.Id).ToList() };
        rec.Items.Add(item);
        s.Record = rec;
        w.Meetings.Minutes.Add(rec);
        if (w.Meetings.Minutes.Count > 60) w.Meetings.Minutes.RemoveAt(0);
        s.VoteAt = w.Tick + LineTicks * Math.Max(1, s.Script.Count) + SimTime.Minutes(1);
        s.End = s.VoteAt + VoteTicks;
        foreach (var c in attendees) c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.08f);
    }

    /// <summary>지금 말하는 사람과 말 (화면의 말풍선) — 표결 전까지 차례로.</summary>
    public (SittingLine line, int index)? Speaking()
    {
        var s = Now;
        if (s == null || s.Opened < 0 || _w.Tick >= s.VoteAt) return null;
        int k = (int)((_w.Tick - s.Opened) / LineTicks);
        return k < s.Script.Count ? (s.Script[k], k) : null;
    }

    /// <summary>표결 중 (0~1: 손이 올라가는 정도).</summary>
    public float Voting => Now is { Opened: >= 0 } s && _w.Tick >= s.VoteAt && _w.Tick < s.End ? Math.Clamp((_w.Tick - s.VoteAt) / (float)SimTime.Minutes(1.5f), 0f, 1f) : 0f;

    // ───────────────────────────── 감시 · 기분 ─────────────────────────────

    private void Watch()
    {
        var w = _w;
        // 벌 근무: 다 했나 · 기한을 넘겼나 (어기면 다시 안건)
        foreach (var id in Duty.Keys.OrderBy(i => i).ToList())
        {
            var (hours, due, mid) = Duty[id];
            var c = P(id);
            if (c == null || c.Dead) { Duty.Remove(id); continue; }
            if (hours <= 0.01f)
            {
                Duty.Remove(id);
                Stats.DutyDone++;
                w.Log.Add(w.Tick, LogKind.Life, "벌 근무를 마쳤다", c.Id);
                Life.Diary(w, c, "벌 근무를 다 했다. 이제 끝이다.");
                foreach (var o in w.Crew) if (!o.Dead && o != c && o.Room == c.Room && o.IsAwake) o.ChangeAffinity(c, 0.01f);
                continue;
            }
            if (w.Tick < due) continue;
            Duty.Remove(id);
            Stats.DutySkipped++;
            Stats.Breaches++;
            var orig = Get(mid);
            var accuser = (orig != null ? P(orig.Proposer) : null) is CrewMember pr && !pr.Dead && pr.CanAct && pr != c ? pr
                : w.Crew.Where(o => Adult(o) && o.CanAct && o != c).OrderByDescending(o => o.Value == CrewValue.Rules ? 1 : 0).ThenBy(o => o.Id).FirstOrDefault();
            if (w.Automation.Present && w.Automation.CoreOnline)
                w.Automation.Reason("motion.duty", $"벌 근무 기록이 비어 있습니다 — {c.Name} · 회의가 정한 두 시간 중 {2f - hours:0.#}시간", SimTime.Hours(6));
            if (accuser != null && !Open.Any(x => x.Kind == MotionKind.Punishment && x.Target == c.Id))
                Propose(accuser, MotionKind.Punishment, SittingKind.Regular, $"{Ko.IGa(c.Name)} 벌 근무를 하지 않았다", "회의에서 정한 벌을 안 지키면 정한 게 무슨 소용이냐", target: c.Id);
        }
        // 급할 때 정한 물 규칙: 물탱크가 다시 차면 정한 대로 푼다
        if (_waterRevert.motion >= 0 && w.Water.Capacity > 0 && w.Water.Level >= w.Water.Capacity * 0.5f)
        {
            if (w.Policies["water"] == 2) w.Policies.Set("water", _waterRevert.to, "물탱크가 다시 찼다 — 회의에서 정한 대로 푼다");
            _waterRevert = (-1, 0);
        }
        // 주 컴퓨터: 창고 수량이 배식 기록과 어긋난다 (누가 꺼냈는지는 모른다)
        foreach (var t in Thefts)
        {
            if (t.Noticed || w.Tick - t.Tick < SimTime.Hours(1)) continue;
            t.Noticed = true;
            if (!w.Automation.Present || !w.Automation.CoreOnline) continue;
            Stats.ComputerLines++;
            w.Automation.Reason("motion.stock", $"식량 창고 수량이 배식 기록보다 하나 적습니다 — {SimTime.Clock(t.Tick)} 무렵 {t.Room}", SimTime.Hours(2));
        }
        if (Thefts.Count > 40) Thefts.RemoveAt(0);
    }

    private void Mood()
    {
        var w = _w;
        long day = w.Tick / SimTime.TicksPerDay;
        foreach (var g in Grudges)
        {
            if (g.Until <= w.Tick || P(g.Who) is not CrewMember c || c.Dead) continue;
            if (c.Needs.Stress < 0.75f) c.Needs.Stress += 0.0006f; // 마음에 걸린 일이 조금씩 누른다
            if (g.LastSaid >= 0 && g.LastSaid / SimTime.TicksPerDay == day || !c.IsAwake || SimTime.HourOfDay(w.Tick) < 20f) continue;
            g.LastSaid = w.Tick;
            var m = Get(g.Motion);
            string line = c.Value switch
            {
                CrewValue.Rules => $"아직도 '{m?.Title ?? "그 일"}'{Ko.IGa(m?.Title ?? "그 일")[(m?.Title ?? "그 일").Length..]} 마음에 걸린다. 정해진 건 따르지만.",
                CrewValue.Freedom => $"'{m?.Title ?? "그 일"}' — 저쪽 사람들 얼굴을 보면 아직 속이 끓는다.",
                CrewValue.People => $"'{m?.Title ?? "그 일"}' 뒤로 식당 공기가 다르다.",
                _ => $"'{m?.Title ?? "그 일"}' — 그때 더 말했어야 했다.",
            };
            Life.Diary(w, c, line);
            if (P(g.Against) is CrewMember a && !a.Dead) c.ChangeAffinity(a, -0.01f);
        }
        foreach (var f in Factions)
        {
            if (f.Gone) continue;
            f.Members.RemoveAll(id => P(id) is not { Dead: false });
            if (f.Members.Count < 2) Dissolve(f, "남은 사람이 없다");
            else if (w.Tick - f.Last > SimTime.TicksPerDay * 4) Dissolve(f, "한동안 같이 설 일이 없었다");
        }
    }

    private void Feast()
    {
        var w = _w;
        if (_feastAt < 0 || _feastDone || w.Tick < _feastEnd) return;
        _feastDone = true;
        var room = FeastRoom;
        var came = w.Crew.Where(c => !c.Dead && c.Room == room && room != null).ToList();
        int meals = Math.Max(1, came.Count / 2);
        for (int i = 0; i < meals; i++) if (!Life.Take(w, ItemKind.Meal, 1)) Life.Take(w, ItemKind.Ration, 1);
        foreach (var c in came)
        {
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.1f);
            c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.2f);
            foreach (var o in came) if (o != c) c.ChangeAffinity(o, 0.01f);
        }
        foreach (var g in Grudges) if (came.Any(c => c.Id == g.Who) && g.Until > w.Tick) g.Until -= SimTime.Hours(12); // 같이 웃고 나면 앙금이 조금 풀린다
        if (came.Count > 0) w.History.Add(w, HistoryKind.Memory, $"잔치 — {room!.Name}에 {came.Count}명이 모여 {meals}끼를 나눠 먹었다", room, came, log: true);
    }

    // ───────────────────────────── 배급 빼돌리기 ─────────────────────────────

    /// <summary>배고픈데 배급이 줄었다 — 몰래 꺼내 먹고 싶어진다 (가치관 · 성실 · 화).</summary>
    private void Temptation()
    {
        var w = _w;
        bool lean = w.Food.Rationing || w.Policies["rations"] == 3;
        foreach (var c in w.Crew)
        {
            if (!Adult(c) || !c.CanAct || c.Needs.Hunger < 0.45f || _tempted.ContainsKey(c.Id) && _tempted[c.Id] > w.Tick) continue;
            if (!lean && MealShare(c) >= 1f) continue;
            if (_stoleAt.TryGetValue(c.Id, out var last) && w.Tick - last < SimTime.TicksPerDay) continue;
            float p = 0.18f * (c.Needs.Hunger - 0.35f) * (c.Value switch { CrewValue.Rules => 0.15f, CrewValue.Safety => 0.5f, CrewValue.Freedom => 1.4f, _ => 1f })
                      * (1.3f - c.Traits.Diligence) * (1f + c.Mind.Anger) * (MealShare(c) < 1f ? 1.5f : 1f);
            if (R.Chance(p)) _tempted[c.Id] = w.Tick + SimTime.Hours(5);
        }
    }

    public bool Tempted(CrewMember c) => _tempted.TryGetValue(c.Id, out var t) && t > _w.Tick;
    /// <summary>시험용: 이 사람을 배고프게 만들어 창고로 가게 한다.</summary>
    public void Tempt(CrewMember c, float hours = 5f) => _tempted[c.Id] = _w.Tick + SimTime.Hours(hours);

    /// <summary>몰래 꺼내 먹는다 — 그 방에 깨어 있던 사람만 본다.</summary>
    public Theft? Steal(CrewMember thief, Furniture box)
    {
        var w = _w;
        if (box.Storage == null) return null;
        var kind = box.Storage.Count(ItemKind.Ration) > 0 ? ItemKind.Ration : box.Storage.Count(ItemKind.Meal) > 0 ? ItemKind.Meal : (ItemKind?)null;
        if (kind is not ItemKind k || box.Storage.Take(k, 1) < 1) return null;
        thief.Needs.Food = MathF.Min(1f, thief.Needs.Food + 0.35f);
        _tempted.Remove(thief.Id);
        _stoleAt[thief.Id] = w.Tick;
        var t = new Theft
        {
            Id = _nextTheft++, Thief = thief.Id, Tick = w.Tick, RoomId = box.Room.Id, Room = box.Room.Name, Item = k == ItemKind.Ration ? "비상식량" : "식사",
            Breach = RationsMotion >= 0 && w.Policies["rations"] == 3 ? RationsMotion : -1,
        };
        Thefts.Add(t);
        Stats.Thefts++;
        if (t.Breach >= 0) Stats.Breaches++;
        foreach (var o in w.Crew)
            if (o != thief && !o.Dead && o.IsAwake && o.Room == box.Room && !o.Outside) See(o, t);
        t.SeenAtOnce = t.Witnesses.Count > 0;
        Life.Diary(w, thief, t.SeenAtOnce ? $"{t.Room}에서 {Ko.EulReul(t.Item)} 하나 꺼내 먹었다. 누가 본 것 같다." : $"{t.Room}에서 {Ko.EulReul(t.Item)} 하나 꺼내 먹었다. 아무도 못 봤다. 그래도 속이 편하지 않다.");
        if (t.SeenAtOnce) thief.Needs.Stress = MathF.Min(1f, thief.Needs.Stress + 0.08f);
        return t;
    }

    /// <summary>그 자리를 본 사람 (몰래 먹는 중에 들어와도 본다).</summary>
    public void See(CrewMember o, Theft t)
    {
        var w = _w;
        if (t.Witnesses.Contains(o.Id) || P(t.Thief) is not CrewMember thief) return;
        t.Witnesses.Add(o.Id);
        Stats.Witnessed++;
        _sawTheft[o.Id] = w.Tick;
        w.Relations.Remember(o, thief, RelationReason.TookMyThing, $"{t.Room}에서 몰래 {Ko.EulReul(t.Item)} 꺼내 먹는 걸 봤다");
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(thief.Name)} {t.Room}에서 몰래 {Ko.EulReul(t.Item)} 꺼내 먹는 걸 봤다", o.Id);
        Life.Diary(w, o, $"{Ko.IGa(thief.Name)} {t.Room}에서 몰래 {Ko.EulReul(t.Item)} 먹는 걸 봤다. 다들 줄여 먹는데.");
    }

    /// <summary>벌 근무 한 시간을 했다.</summary>
    public void DidDuty(CrewMember c, float hours)
    {
        if (!Duty.TryGetValue(c.Id, out var d)) return;
        Duty[c.Id] = (d.hours - hours, d.due, d.motion);
        _w.Info.Note(c, LedgerKind.Dishes, null, "벌 근무 — 식당 청소 · 설거지");
    }

    // ───────────────────────────── 지문 ─────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(All.Count); I(Stats.Proposed); I(Stats.Signed); I(Stats.Refused); I(Stats.Sittings); I(Stats.Thefts); I(Stats.Witnessed); I(Stats.Grudges);
        I(Factions.Count(f => !f.Gone)); I(Duty.Count); I(RationsMotion);
        foreach (var m in All) { I(m.Id); I((int)m.Stage); I(m.Signers.Count); I(m.Passed ? 1 : 0); I((int)m.Verdict); }
        foreach (var g in Grudges) { I(g.Who); I(g.Until); }
    }
}
