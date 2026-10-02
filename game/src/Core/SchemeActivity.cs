using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.14 꾸미는 일의 몸짓: 몰래 손을 놀린다(남이 오면 멈춘다) · 귓속말로 끌어들인다 · 냄새 · 소리를 따라 확인하러 간다 ·
// 밤 모임(방송 · 판 · 시음 · 만남) · 행사 · 동호회 · 관행에 간다 · 일손을 놓고 식당에 앉는다.

public enum SchemeTaskKind : byte { None, Work, Recruit, Attend, Check, Sit, Session, Practice }

public readonly record struct SchemeTask(SchemeTaskKind Kind, int Scheme, int Practice, int RoomId, Cell At, int Other, float Score, string Label);

public sealed partial class SchemeSystem
{
    private readonly Dictionary<int, (long at, SchemeTask t)> _tasks = new();
    private static readonly SchemeTask Idle = new(SchemeTaskKind.None, -1, -1, -1, default, -1, 0f, "");

    /// <summary>이 사람이 지금 꾸미는 일로 할 것 (1분쯤 기억한다).</summary>
    public SchemeTask Task(CrewMember c)
    {
        if (_tasks.TryGetValue(c.Id, out var e) && _w.Tick - e.at < 60) return e.t;
        var t = FindTask(c);
        _tasks[c.Id] = (_w.Tick, t);
        return t;
    }

    private bool Fancies(CrewMember c, Scheme s)
    {
        var lead = P(s.Lead);
        return Interest(c, s.Spec) + 0.3f * c.Traits.Sociability + (lead != null ? 0.2f * c.AffinityTo(lead) : 0f) > 0.38f;
    }

    private SchemeTask FindTask(CrewMember c)
    {
        var w = _w;
        long now = w.Tick;
        if (Off || !Adult(c) || c.Outside || All.Count == 0 && Practices.Count == 0) return Idle;
        // 1) 일손을 놓는 날
        foreach (var s in All)
            if (s.Active && s.Spec.Key is "strike" or "slowdown" && s.InSession(now) && s.Crew.Contains(c.Id))
                return new(SchemeTaskKind.Sit, s.Id, -1, s.RoomId, s.Spot, -1, 0.85f, s.Spec.Key == "strike" ? "일손을 놓고 앉아 있다" : "일을 아주 천천히");
        // 2) 행사 · 모임 (겹치면 더 마음 가는 쪽)
        SchemeTask? pick = null; float pv = float.MinValue;
        foreach (var s in All)
        {
            if (!s.Active || !s.InSession(now) || s.Spec.Key is "strike" or "slowdown") continue;
            if (s.Spec.Fate == Fate.Event)
            {
                bool read = s.Invite >= 0 && w.Info.Chat.HasRead(c, s.Invite);
                if (s.Crew.Contains(c.Id) || c.Id == s.Target || (s.Knew(c.Id) || read) && Fancies(c, s))
                {
                    float v = (c.Id == s.Target ? 3f : s.Crew.Contains(c.Id) ? 2f : 1f) + Interest(c, s.Spec);
                    if (v > pv) { pv = v; pick = new(SchemeTaskKind.Attend, s.Id, -1, s.RoomId, s.Spot, -1, c.Id == s.Target ? 0.75f : 0.62f, s.Spec.Name); }
                }
                continue;
            }
            bool member = s.Crew.Contains(c.Id);
            bool club = s.Spec.Fate == Fate.Club && (s.Knew(c.Id) || s.Invite >= 0 && w.Info.Chat.HasRead(c, s.Invite)) && Interest(c, s.Spec) > 0.3f;
            bool guest = s.Spec.Key is "moonshine" or "fruit_wine" && s.Knows.TryGetValue(c.Id, out var how) && how == KnowHow.Told && Approve(c, s).v > 0f;
            if (member || club || guest)
            {
                float v = (member ? 2f : 0.5f) + Interest(c, s.Spec) + (s.Lead == c.Id ? 1f : 0f);
                if (v > pv) { pv = v; pick = new(SchemeTaskKind.Session, s.Id, -1, s.RoomId, s.Spot, -1, member ? 0.56f : 0.48f, s.Spec.Name); }
            }
        }
        if (pick is SchemeTask chosen) return chosen;
        // 3) 관행
        for (int i = 0; i < Practices.Count; i++)
        {
            var p = Practices[i];
            if (!p.Now(now)) continue;
            bool f = p.Followers.Contains(c.Id);
            var spec = SchemeTable.Get(p.Key);
            if (f || spec != null && Interest(c, spec) > 0.35f)
                return new(SchemeTaskKind.Practice, -1, i, p.RoomId, default, -1, 0.48f + 0.1f * c.Traits.Sociability + (f ? 0.05f : 0f), p.Name);
        }
        // 4) 냄새 · 소리를 따라 확인
        if (_suspect.TryGetValue(c.Id, out var sid))
        {
            if (Get(sid) is { Hiding: true } sus && !sus.KnowsWho(c.Id))
                return new(SchemeTaskKind.Check, sus.Id, -1, sus.RoomId, sus.Spot, -1, 0.5f, Smelly(sus.Spec) ? "냄새를 따라" : "소리 나는 쪽으로");
            _suspect.Remove(c.Id);
        }
        // 5) 같이 할 사람을 귓속말로
        foreach (var s in All)
        {
            if (s.Stage != SchemeStage.Plan || s.Lead != c.Id || s.Spec.Fate == Fate.Club) continue;
            if (NextRecruit(s, c) is CrewMember who) return new(SchemeTaskKind.Recruit, s.Id, -1, who.Room?.Id ?? -1, who.Cell, who.Id, 0.46f, $"{who.Name}에게 귓속말");
        }
        // 6) 몰래 준비
        float hour = SimTime.HourOfDay(now);
        bool late = hour >= 20f || hour < 2f;
        foreach (var s in All)
        {
            if (s.Stage != SchemeStage.Prep || s.Progress >= 1f || !s.Crew.Contains(c.Id)) continue;
            if (s.Spec.Fate == Fate.Laugh && s.Lead != c.Id) continue;
            bool secret = s.Spec.Secrecy >= 0.45f;
            return new(SchemeTaskKind.Work, s.Id, -1, s.RoomId, s.Spot, -1, 0.4f + 0.25f * Bored(c) + (late && secret ? 0.08f : 0f), secret ? $"{s.Spec.Name} — 몰래" : $"{s.Spec.Name} 준비");
        }
        return Idle;
    }

    private CrewMember? NextRecruit(Scheme s, CrewMember lead)
    {
        CrewMember? best = null;
        float bv = 0.12f;
        bool craft = (s.Spec.Drives & Drive.Craft) != 0;
        foreach (var o in _w.Crew)
        {
            if (!Adult(o) || o == lead || s.Knew(o.Id) || o.Id == s.Target || !o.CanAct || o.Outside || o.Room == null) continue;
            float v = Interest(o, s.Spec) + 0.35f * lead.AffinityTo(o) + (craft && o.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician && lead.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician ? 0.2f : 0f);
            if (v > bv || v == bv && best != null && o.Id < best.Id) { bv = v; best = o; }
        }
        return best;
    }

    /// <summary>귓속말: 받아들이면 같이 하고, 거절해도 이제 안다 (싫어하면 알릴 수도 있다).</summary>
    public void Recruit(CrewMember lead, int sid, int whoId)
    {
        var w = _w;
        if (Get(sid) is not { Stage: SchemeStage.Plan } s || P(whoId) is not { Dead: false } who || s.Knew(who.Id)) return;
        if (lead.Room != who.Room && (lead.Position - who.Position).LengthSquared() > 9f) return;
        var spec = s.Spec;
        float v = Interest(who, spec) + 0.35f * who.AffinityTo(lead) + 0.15f * w.Relations.Trust(who, lead)
                  - (spec.Cat is SchemeCat.Rule or SchemeCat.Secret or SchemeCat.Trade && who.Value == CrewValue.Rules ? 0.3f : 0f)
                  + ((spec.Drives & Drive.Craft) != 0 && who.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician ? 0.1f : 0f);
        bool yes = v > 0.36f;
        lead.Say(w, Persona.Say(lead, spec.Secrecy >= 0.45f ? "잠깐, 너만 알고 있어…" : $"{spec.Name} 같이 할래?"));
        if (yes)
        {
            s.Crew.Add(who.Id);
            s.Knows[who.Id] = KnowHow.Part;
            who.Say(w, Persona.Say(who, "좋아, 끼워 줘"));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(lead.Name)} {who.Name}에게 귓속말 — {spec.Name} · 같이 하기로 했다", lead.Id);
            Life.Diary(w, who, $"{Ko.IGa(lead.Name)} {Ko.EulReul(spec.Name)} 같이 하자고 했다. 하기로 했다.");
            if (s.Crew.Count >= Needed(spec)) Begin(s);
            return;
        }
        s.Knows[who.Id] = KnowHow.Asked;
        who.Say(w, Persona.Say(who, "난 빠질게"));
        Life.Diary(w, who, $"{Ko.IGa(lead.Name)} {Ko.EulReul(spec.Name)} 같이 하자고 했다. 거절했다.");
        if (spec.Fate is Fate.Vote or Fate.Trial or Fate.Grievance && Approve(who, s).v < -0.3f && R.Chance(0.35f))
        {
            s.Knows.Remove(who.Id);
            Discover(s, who, "귓속말을 듣고");
        }
    }

    /// <summary>준비하는 손 (한 틱).</summary>
    public void WorkTick(CrewMember c, int sid)
    {
        if (Get(sid) is not Scheme s || s.Stage != SchemeStage.Prep) return;
        float hours = MathF.Max(0.2f, s.Spec.Hours);
        float mul = 0.8f + 0.4f * DriveOf(c, Drive.Craft);
        s.Progress = MathF.Min(1f, s.Progress + mul / (hours * SimTime.TicksPerHour));
        s.Working = true;
        s.Stopped = _w.Tick;
        if (_w.Tick % 50 == 0) SetBored(c, Bored(c) - 0.0015f);
    }

    /// <summary>계속 손을 놀려도 되나 (남이 들어오면 멈춘다).</summary>
    public bool KeepWorking(CrewMember c, int sid)
    {
        if (Get(sid) is not Scheme s || s.Stage != SchemeStage.Prep || s.Progress >= 1f) return false;
        if (_w.Tick % 25 == 0 && Watched(s, c)) { Stats.Hid++; s.Working = false; return false; }
        return true;
    }

    public void Arrive(CrewMember c, SchemeTask t)
    {
        if (t.Practice >= 0 && t.Practice < Practices.Count) { Practices[t.Practice].Came.Add(c.Id); return; }
        if (Get(t.Scheme) is not Scheme s) return;
        s.Came.Add(c.Id);
        if (!s.Knew(c.Id)) s.Knows[c.Id] = KnowHow.Chat;
    }

    public void Inspect(CrewMember c, int sid)
    {
        _suspect.Remove(c.Id);
        if (Get(sid) is not { Hiding: true } s || s.KnowsWho(c.Id) || s.Crew.Contains(c.Id)) return;
        if (Smelly(s.Spec)) Stats.Smelled++;
        Discover(s, c, Smelly(s.Spec) ? "냄새를 따라가 보니" : "소리를 따라가 보니");
    }

    public bool Running(SchemeTask t) =>
        t.Practice >= 0 ? t.Practice < Practices.Count && Practices[t.Practice].Now(_w.Tick) : Get(t.Scheme) is Scheme s && s.Active && s.InSession(_w.Tick);

    /// <summary>시험용: 이 사람이 확인하러 갈 일을 정한다.</summary>
    public void Suspect(CrewMember c, Scheme s) => _suspect[c.Id] = s.Id;
}

public sealed class SchemeActivity : Activity
{
    public override string Id => "scheme";
    public override string Label => "꾸미는 일";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (SchemeSystem.Off || c.IsChild || c.Down || !c.CanAct || c.Outside || Crisis.Acting(w)) return (0f, "—");
        var t = w.Schemes.Task(c);
        if (t.Kind == SchemeTaskKind.None) return (0f, "—");
        float s = t.Score;
        bool night = t.Kind == SchemeTaskKind.Session && w.Schemes.Get(t.Scheme)?.Spec.Key is "pirate_radio" or "gambling_den" or "secret_romance" or "black_market";
        if (t.Kind != SchemeTaskKind.Sit && OnShift(c, w)) s -= 0.35f;
        if (Bedtime(c, w)) s -= night ? 0.08f : 0.3f;
        if (c.Needs.Hunger > 0.7f || c.Needs.Fatigue > 0.85f) s *= 0.4f;
        if (c.Job?.Activity is SchemeActivity) s += 0.08f;
        return (MathF.Max(0f, s), t.Label);
    }

    private static Cell? Near(World w, CrewMember c, DistanceField dist, Room? room, Cell at, bool center)
    {
        if (room == null) return null;
        var c0 = center ? room.Center : at.Center;
        return room.Cells.Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c))
            .OrderBy(x => (x.Center - c0).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).Cast<Cell?>().FirstOrDefault();
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var sy = w.Schemes;
        var t = sy.Task(c);
        var room = t.RoomId >= 0 && t.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[t.RoomId] : null;
        var toils = new List<Toil>();
        switch (t.Kind)
        {
            case SchemeTaskKind.Work:
            {
                if (Near(w, c, dist, room, t.At, false) is not Cell spot) return null;
                int sid = t.Scheme;
                toils.AddRange(Plans.DropOff(c, w, dist));
                toils.Add(new GotoToil(spot));
                toils.Add(new WaitToil(SimTime.Minutes(45), Pose.Working, t.At.Center)
                {
                    EveryTick = (cm, world) => world.Schemes.WorkTick(cm, sid),
                    DoneWhen = (cm, world) => !world.Schemes.KeepWorking(cm, sid),
                });
                return new Job(this, t.Label, toils) { TargetRoom = room, InterruptMargin = 0.2f };
            }
            case SchemeTaskKind.Recruit:
            {
                int sid = t.Scheme, who = t.Other;
                toils.Add(new GotoToilLate(cm =>
                {
                    var o = cm.Room == null ? null : w.Crew.FirstOrDefault(x => x.Id == who);
                    if (o == null || o.Dead) return null;
                    var oc = o.Cell;
                    foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
                    {
                        var n = oc + d;
                        if (w.Ship.IsWalkable(n) && !w.IsSpotTaken(n, cm)) return n;
                    }
                    return oc;
                }));
                toils.Add(new DoToil((cm, world) => { world.Schemes.Recruit(cm, sid, who); return true; }));
                toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Standing));
                return new Job(this, t.Label, toils) { InterruptMargin = 0.2f };
            }
            case SchemeTaskKind.Check:
            {
                if (Near(w, c, dist, room, t.At, false) is not Cell spot) return null;
                int sid = t.Scheme;
                toils.Add(new GotoToil(spot));
                toils.Add(new DoToil((cm, world) => { world.Schemes.Inspect(cm, sid); return true; }));
                return new Job(this, t.Label, toils) { TargetRoom = room, LogText = t.Label, LogKind = LogKind.Life };
            }
            case SchemeTaskKind.Attend or SchemeTaskKind.Session or SchemeTaskKind.Practice or SchemeTaskKind.Sit:
            {
                bool secret = t.Kind == SchemeTaskKind.Session;
                if (Near(w, c, dist, room, t.At, !secret) is not Cell spot) return null;
                var task = t;
                toils.AddRange(Plans.DropOff(c, w, dist));
                toils.Add(new GotoToil(spot));
                toils.Add(new DoToil((cm, world) => { world.Schemes.Arrive(cm, task); return true; }));
                toils.Add(new WaitToil(SimTime.Hours(3), t.Kind == SchemeTaskKind.Sit && w.Schemes.Get(t.Scheme)?.Spec.Key == "slowdown" ? Pose.Working : Pose.Sitting, room?.Center)
                {
                    DoneWhen = (cm, world) => !world.Schemes.Running(task),
                    EveryTick = (cm, _) => cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.0003f),
                });
                return new Job(this, t.Label, toils) { TargetRoom = room, LogText = t.Kind == SchemeTaskKind.Session ? null : t.Label, LogKind = LogKind.Life, InterruptMargin = 0.25f };
            }
        }
        return null;
    }
}
