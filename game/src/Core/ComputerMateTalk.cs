using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.27 ① 거절당했을 때 · 약속.
//  거절: 부탁한 일을 안 하고 기한이 지나면 까닭을 짐작한다(본 것으로만 — 못 들었나 · 지쳤나 · 다른 일 중인가 · 나를 못 믿나 · 솜씨 · 싫어하나).
//    짐작대로 다른 사람에게 · 다른 설명(근거부터)으로 다시 · 같은 일이 두 번 막히거나 급하면 함장에게. 짐작이 맞았는지는 세계가 안다 (기록으로 배운다).
//  약속: 틀렸을 때 "다음엔 이렇게"를 남긴다 (날씨 · 기억 · 경보 · 사생활). 같은 상황이 오면 지켰나를 들은 사람들이 기억한다 —
//    지키면 믿음이 조금 오르고, 어기면 크게 깎인다. 어긴 걸 기억하는 사람은 다음 약속을 반쯤만 믿는다.

public sealed class RefusalCase
{
    public long Tick { get; init; }
    public int CrewId { get; init; }
    public int OrderId { get; init; }
    public string Title { get; init; } = "";
    public string Guess { get; init; } = "";
    public string Truth { get; init; } = "";
    public bool GuessRight => Guess == Truth;
    public string Next { get; set; } = "";
    public int Alt { get; set; } = -1;
}

public sealed class Promise
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public string Rule { get; init; } = "";
    public string Text { get; set; } = "";
    public List<int> Heard { get; } = new();
    public int Tests, Kept, Broken;
    public long LastTest = -1;
    public bool LastKept;
    public string LastWhat = "";
}

public sealed partial class ShipMate
{
    public List<RefusalCase> Refusals { get; } = new();
    public List<Promise> Promises { get; } = new();
    public int PromisesKept, PromisesBroken, GuessesRight, GuessesWrong;
    private readonly SortedDictionary<int, WorkAsk> _askSeen = new();
    private readonly SortedSet<long> _handled = new();
    private static long Key(WorkAsk a) => (long)a.CrewId * 10_000_000L + a.OrderId;
    /// <summary>사람마다 기억하는 컴퓨터 약속 (지킨 것 · 어긴 것).</summary>
    private readonly SortedDictionary<int, (int kept, int broken)> _promiseMemory = new();
    private int _promiseId = 1;

    public (int kept, int broken) Remembers(CrewMember c) => _promiseMemory.GetValueOrDefault(c.Id);

    private void TalkTick()
    {
        var w = _w;
        var cm = A.CrewModel;
        foreach (var (id, ask) in _askSeen.ToList())
        {
            if (cm.Asks.TryGetValue(id, out var now) && now.OrderId == ask.OrderId) continue;
            _askSeen.Remove(id);
            if (ask.Crisis || w.Tick <= ask.Until || !_handled.Add(Key(ask)) || Crew(id) is not CrewMember c || c.Dead) continue; // 따랐거나 거둔 부탁
            if (c.Job?.Order?.Id == ask.OrderId) continue;
            Refused(c, ask);
        }
        // 기한이 한참 지났는데 그대로인 부탁 (컴퓨터가 못 보는 곳에서 버틴 사람)
        var stale = new List<WorkAsk>();
        foreach (var ask in cm.Asks.Values) if (!ask.Crisis && w.Tick > ask.Until + SimTime.Minutes(10) && !_handled.Contains(Key(ask))) stale.Add(ask);
        foreach (var ask in stale.OrderBy(x => x.CrewId))
        {
            _handled.Add(Key(ask));
            if (Crew(ask.CrewId) is CrewMember c && !c.Dead && c.Job?.Order?.Id != ask.OrderId) Refused(c, ask);
        }
        if (_handled.Count > 400) _handled.Clear();
        foreach (var (id, ask) in cm.Asks) _askSeen[id] = ask;
        if (Refusals.Count > 60) Refusals.RemoveRange(0, Refusals.Count - 60);
    }


    /// <summary>부탁이 거절됐다 — 까닭을 짐작하고 다음 수를 고른다.</summary>
    internal RefusalCase? Refused(CrewMember c, WorkAsk ask)
    {
        var w = _w;
        var a = A;
        var cm = a.CrewModel;
        var order = w.Board.Open.FirstOrDefault(o => o.Id == ask.OrderId);
        if (order == null || order.Closed) return null;
        if (order.Assignee != null && order.Assignee != c) return null; // 다른 사람이 이미 했다
        bool heard = c.Room != null && a.Speak.SpeakerWorks(c.Room) && c.IsAwake;
        float rest = cm.RestNow(c), trust = a.Trusts.Of(c);
        bool busy = c.Job?.Order != null && c.Job.Order.Id != ask.OrderId;
        bool weak = cm.Knows(c) && cm.Of(c).Skill[(int)order.Skill] < 0.35f;
        string guess = !heard ? "못 들었다" : rest < 0.35f ? "지쳤다" : busy ? "다른 일 중이다" : trust < 0.4f ? "나를 못 믿는다" : weak ? "솜씨가 모자라 망설인다" : "그 일을 싫어한다";
        // 세계가 아는 진짜 까닭
        var mem = Remembers(c);
        string truth = !c.IsAwake ? "못 들었다" : c.Needs.Rest < 0.3f ? "지쳤다" : busy ? "다른 일 중이다"
            : trust < 0.4f || mem.broken > mem.kept ? "나를 못 믿는다" : c.SkillLevel(order.Skill) < 0.3f ? "솜씨가 모자라 망설인다" : "그 일을 싫어한다";
        var rc = new RefusalCase { Tick = w.Tick, CrewId = c.Id, OrderId = order.Id, Title = order.Title, Guess = guess, Truth = truth };
        if (rc.GuessRight) GuessesRight++; else GuessesWrong++;
        if (!rc.GuessRight && GuessesWrong % 3 == 0) a.Authority.Learned("사람", $"거절 까닭을 또 잘못 짚었다 — {c.Name}: 나는 '{guess}', 실제로는 '{truth}'");
        int before = Refusals.Count(r => r.OrderId == order.Id);
        Refusals.Add(rc);
        var refusers = Refusals.Where(r => r.OrderId == order.Id).Select(r => r.CrewId).ToHashSet();
        var pool = w.Crew.Where(x => !x.Dead && !x.IsChild && x.CanAct && !x.Away && x.IsAwake && !refusers.Contains(x.Id)).ToList();
        var cap = w.Command.Captain;
        if ((before >= 1 || order.Urgency >= 0.75f) && cap != null && cap.CanAct && !cap.Away && !refusers.Contains(cap.Id))
        {
            // 함장에게: 함장이 사람을 정해 직접 말한다 (함장 말은 더 잘 듣는다)
            var alt = cm.Best(order.Skill, pool.Where(x => x != cap)) ?? (pool.Contains(cap) ? cap : null);
            rc.Next = alt != null ? $"함장에게 — {cap.Name}이 {alt.Name}에게 맡겼다" : "함장에게 — 맡길 사람이 없다";
            if (alt != null)
            {
                rc.Alt = alt.Id;
                cm.Ask(alt, order, $"함장 지시 — {order.Title}", 6f);
                cap.Say(w, Persona.Say(cap, $"{alt.Name}, {order.Title} 좀 맡아 줘"));
            }
            a.Authority.Learned("사람", $"{order.Title} — 부탁이 {before + 1}번 막혀 함장에게 넘겼다");
        }
        else if (before >= 1 && cap != null && refusers.Contains(cap.Id))
        {
            rc.Next = "함장도 거절 — 미뤄 두고 다음 정비표에 다시 올린다";
            w.Board.Block(order, null, 4f);
        }
        else if (guess == "나를 못 믿는다" && before == 0)
        {
            rc.Next = "다른 설명 — 근거부터";
            a.Manner.Of(c).LastEvidence = w.Tick;
            cm.Ask(c, order, $"근거: {order.Detail} — 미루면 고장 날 확률이 오른다 (불확실성도 함께 적었다)", 4f);
        }
        else if (cm.Best(order.Skill, pool) is CrewMember alt2)
        {
            rc.Next = $"다른 사람 — {alt2.Name}";
            rc.Alt = alt2.Id;
            cm.Ask(alt2, order, $"{order.Title} ({c.Name} 대신 — {guess})", 4f);
        }
        else rc.Next = "기다린다 — 맡을 사람이 없다";
        Say($"{Ko.IGa(c.Name)} {order.Title} 부탁을 안 따랐다 — {guess}고 본다 → {rc.Next}", null, -1, c.Id);
        return rc;
    }

    // ───────────── 약속 ─────────────

    public Promise? Promised(string rule) { for (int i = Promises.Count - 1; i >= 0; i--) if (Promises[i].Rule == rule) return Promises[i]; return null; }

    /// <summary>"다음엔 이렇게" — 들은 사람이 기억한다.</summary>
    public Promise MakePromise(string rule, string text, IEnumerable<CrewMember?> heard)
    {
        var w = _w;
        var p = Promised(rule);
        if (p == null) { p = new Promise { Id = _promiseId++, Tick = w.Tick, Rule = rule, Text = text }; Promises.Add(p); }
        else p.Text = text;
        foreach (var c in heard)
        {
            if (c == null || c.Dead || p.Heard.Contains(c.Id)) continue;
            p.Heard.Add(c.Id);
            var m = Remembers(c);
            if (m.broken > m.kept && R.Chance(0.5f)) Life.Diary(w, c, Persona.Say(c, "컴퓨터가 또 약속을 했다. 지난번에도 했었지"));
        }
        Say($"약속한다 — {text}");
        return p;
    }

    /// <summary>약속이 시험대에 올랐다: 지켰나 — 들은 사람이 기억한다.</summary>
    public void TestPromise(Promise p, bool kept, string what)
    {
        var w = _w;
        var a = A;
        p.Tests++;
        p.LastTest = w.Tick;
        p.LastKept = kept;
        p.LastWhat = what;
        if (kept) { p.Kept++; PromisesKept++; } else { p.Broken++; PromisesBroken++; }
        foreach (var id in p.Heard)
        {
            if (Crew(id) is not CrewMember c || c.Dead) continue;
            var m = Remembers(c);
            _promiseMemory[id] = kept ? (m.kept + 1, m.broken) : (m.kept, m.broken + 1);
            if (kept) a.Trusts.Change(c, m.broken > m.kept ? 0.015f : 0.03f, $"약속을 지켰다 — {p.Text}", quiet: true);
            else a.Trusts.Change(c, -0.06f, $"약속을 어겼다 — {p.Text}", quiet: true);
            if (R.Chance(0.4f)) Life.Diary(w, c, Persona.Say(c, kept ? $"컴퓨터가 지난번 약속을 지켰다 — {what}" : $"약속했잖아. {what}"));
        }
        Say(kept ? $"지난번 약속대로 했다 — {what}" : $"약속을 못 지켰다 — {what}");
    }
}
