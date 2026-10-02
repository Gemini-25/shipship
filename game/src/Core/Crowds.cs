using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.4 구경꾼: 사고 현장(방 · 계통 규모의 뜨거운 사건 — 불 · 구멍 · 쓰러진 사람)에 비번 · 한가한 사람이 몰려든다.
//  · 호기심 = 사교성 · 버릇(수다 · 구경 · 장난) − 두려움(불 · 구멍) − 근무 중 성실함 · 다친 사람과 친하면 더 · 먼 곳은 덜.
//  · 현장 문 앞(통로 칸) · 쓰러진 사람 둘레에 선다 → 길찾기 비용 · 비집기 감속 (펼친 부품 · 짐과 같은 규칙) → 대응자가 늦는다.
//  · 책임자(지휘자 · 선장 · 당직 · 대응자)가 "비켜!" — 성실함 · 권위 · 관계만큼 물러나고, 버티는 사람도 있다 (서운함 · 수치).
//  · 주 컴퓨터는 문 앞 사람 수 · 대응 지연을 보고 방송한다 (믿는 사람이 물러난다).
//  · 본 사람은 소문을 퍼뜨린다: 곁의 사람에게 믿음(소문)으로 — 말이 많은 사람은 부풀린다 (보면 바로잡힌다 — Beliefs).

public sealed class CrowdScene
{
    public int Id { get; init; }
    public int CaseId { get; init; } = -1;
    public int RoomId { get; init; } = -1;
    public int CrewId { get; init; } = -1;
    public string What { get; init; } = "";
    public Vector2 Center { get; init; }
    public bool HasBelief { get; init; }
    public Topic Topic { get; init; }
    public int TopicId { get; init; }
    public long Since { get; init; }
    public long LastSeen { get; internal set; }
    public bool Ended { get; internal set; }
    /// <summary>구경하는 자리 (문 앞 · 둘레) · 자리마다 선 사람 (-1 = 비었다).</summary>
    public List<Cell> Spots { get; } = new();
    public List<int> Taken { get; } = new();
    /// <summary>자리에 닿아 보고 있는 사람.</summary>
    public List<int> Watchers { get; } = new();
    public int Shouts { get; internal set; }
    public long ShoutAt { get; internal set; } = -1;
    public int Shouter { get; internal set; } = -1;
    public string ShoutLine { get; internal set; } = "";
    public long Squeezed { get; internal set; } = -1;
    public int SqueezedBy { get; internal set; } = -1;
    public long Advised { get; internal set; } = -1;
    public int Peak { get; internal set; }
}

public sealed class Rumor
{
    public int Teller { get; init; }
    public int SceneId { get; init; }
    public bool HasBelief { get; init; }
    public Topic Topic { get; init; }
    public int Id { get; init; }
    public string What { get; init; } = "";
    public bool Exaggerate { get; init; }
    public int Left { get; internal set; }
    public long Until { get; init; }
    public List<int> Told { get; } = new();
}

public sealed class CrowdStats
{
    public int Scenes, Watchers, Squeezes, Shouts, Obeyed, Ignored, ComputerCalls, ComputerShoos, Rumors, Exaggerated, Peak;
    public string Line() => $"구경 현장 {Scenes}(가장 많을 때 {Peak}명) · 구경 {Watchers}번 · 비집고 지남 {Squeezes} · \"비켜!\" {Shouts}(물러남 {Obeyed} · 버팀 {Ignored}) · 컴퓨터 방송 {ComputerCalls}(물러남 {ComputerShoos}) · 소문 {Rumors}(부풀림 {Exaggerated})";
}

public sealed class CrowdSystem
{
    private readonly World _w;
    private readonly CoopSystem _co;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7933 + 5501));

    public List<CrowdScene> Scenes { get; } = new();
    public List<Rumor> Rumors { get; } = new();
    public CrowdStats Stats { get; } = new();
    private readonly Dictionary<int, (int scene, long until)> _shooed = new();
    private readonly Dictionary<int, int> _watching = new();
    private readonly Dictionary<int, (int scene, long tick)> _squeeze = new();
    /// <summary>소문이 건너간 길 (말한 사람 → 들은 사람) — 화면 · 시험.</summary>
    public List<(int teller, int listener, long tick, bool big)> Heard { get; } = new();
    private long _next;

    public CrowdSystem(World w, CoopSystem co) { _w = w; _co = co; }

    public int Active { get; private set; }
    private CrewMember? CrewById(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }
    public CrowdScene? SceneById(int id) { foreach (var s in Scenes) if (s.Id == id) return s; return null; }
    public bool IsWatcher(CrewMember c) => _watching.ContainsKey(c.Id);
    public bool Shooed(CrewMember c, CrowdScene s) => _shooed.TryGetValue(c.Id, out var x) && x.scene == s.Id && x.until > _w.Tick;

    // ───────────────────────────── 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        long now = w.Tick;
        if (now < _next) return;
        _next = now + SimTime.Minutes(1);
        Scan(now);
        foreach (var s in Scenes)
        {
            if (s.Ended) continue;
            // 자리를 잡았다 떠난 사람은 비운다
            for (int i = 0; i < s.Taken.Count; i++)
                if (s.Taken[i] >= 0 && (CrewById(s.Taken[i]) is not CrewMember m || m.Job?.Current is not WatchToil wt || wt.Scene != s)) s.Taken[i] = -1;
            s.Watchers.RemoveAll(id => !_watching.TryGetValue(id, out var sid) || sid != s.Id);
            if (s.Watchers.Count > s.Peak) s.Peak = s.Watchers.Count;
            if (s.Watchers.Count > Stats.Peak) Stats.Peak = s.Watchers.Count;
            if (s.Watchers.Count == 0) continue;
            ShoutCheck(s, now);
            ComputerCheck(s, now);
        }
        Scenes.RemoveAll(s => s.Ended && now - s.LastSeen > SimTime.Hours(6));
        Active = Scenes.Count(s => !s.Ended);
        Spread(now);
        foreach (var k in _shooed.Where(kv => kv.Value.until <= now).Select(kv => kv.Key).ToList()) _shooed.Remove(k);
    }

    private void Scan(long now)
    {
        var w = _w;
        foreach (var k in w.Scale.OpenCases)
        {
            if (!k.Hot || k.Now > IncidentScale.System) continue;
            var ex = Scenes.FirstOrDefault(s => s.CaseId == k.Id && !s.Ended);
            if (ex != null) { ex.LastSeen = now; continue; }
            var scene = MakeScene(k, now);
            if (scene == null) continue;
            Scenes.Add(scene);
            Stats.Scenes++;
        }
        foreach (var s in Scenes)
            if (!s.Ended && s.LastSeen < now) { s.Ended = true; s.LastSeen = now; }
    }

    private CrowdScene? MakeScene(ScaleCase k, long now)
    {
        var w = _w;
        var ship = w.Ship;
        var victim = k.CrewId >= 0 ? CrewById(k.CrewId) : null;
        if (victim != null && victim.Down && !victim.Dead && victim.CarriedBy == null && victim.Room is Room vr && !vr.Detached)
        {
            var sc = new CrowdScene
            {
                Id = _co.NextId(), CaseId = k.Id, RoomId = vr.Id, CrewId = victim.Id, What = $"{Ko.IGa(victim.Name)} 쓰러졌다", Center = victim.Position,
                HasBelief = true, Topic = Topic.Down, TopicId = victim.Id, Since = now, LastSeen = now,
            };
            var vc = victim.Cell;
            foreach (var c in vr.Cells.OrderBy(c => Math.Abs(Math.Max(Math.Abs(c.X - vc.X), Math.Abs(c.Y - vc.Y)) - 2)).ThenBy(c => c.Y).ThenBy(c => c.X))
            {
                int r = Math.Max(Math.Abs(c.X - vc.X), Math.Abs(c.Y - vc.Y));
                if (r < 2 || r > 3 || !ship.IsOpenFloor(c) || ship.DoorAt(c) != null) continue;
                sc.Spots.Add(c); sc.Taken.Add(-1);
                if (sc.Spots.Count >= 6) break;
            }
            return sc.Spots.Count > 0 ? sc : null;
        }
        if (k.CrewId >= 0) return null; // 쓰러지지 않은 다친 사람은 구경거리가 아니다
        int roomId = k.RoomId >= 0 ? k.RoomId : k.Rooms.Count > 0 ? k.Rooms[0] : -1;
        if (roomId < 0 || roomId >= ship.Rooms.Count) return null;
        var room = ship.Rooms[roomId];
        if (room.Detached || room.Type == RoomType.Corridor && k.CrewId < 0) return null;
        bool fire = k.Key.Contains("Fire", StringComparison.Ordinal) || k.KindsSeen.Contains(CauseKind.Fire);
        bool breach = !fire && (k.Key.Contains("Breach", StringComparison.Ordinal) || k.Key.Contains("Decomp", StringComparison.Ordinal));
        var scene = new CrowdScene
        {
            Id = _co.NextId(), CaseId = k.Id, RoomId = room.Id, What = k.Name.Contains(room.Name, StringComparison.Ordinal) ? k.Name : $"{room.Name} {k.Name}", Center = room.Center,
            HasBelief = fire || breach, Topic = fire ? Topic.Fire : Topic.Breach, TopicId = room.Id, Since = now, LastSeen = now,
        };
        // 문 앞 (현장 밖 통로 칸) 둘레
        foreach (var d in room.Doors)
        {
            if (d.Removed || d.Welded || d.IsExternal) continue;
            Cell? outside = null;
            foreach (var dir in Cell.Dirs4)
            {
                var o = d.Cell + dir;
                if (ship.RoomAt(o) is Room orr && orr != room && !orr.Detached && ship.IsWalkable(o) && ship.DoorAt(o) == null && ship.FurnitureAt(o) == null) { outside = o; break; }
            }
            if (outside is not Cell oc) continue;
            var oroom = ship.RoomAt(oc)!;
            var seen = new HashSet<Cell> { oc };
            var q = new Queue<Cell>();
            q.Enqueue(oc);
            int added = 0;
            while (q.Count > 0 && added < 4)
            {
                var c = q.Dequeue();
                if (!scene.Spots.Contains(c)) { scene.Spots.Add(c); scene.Taken.Add(-1); added++; }
                foreach (var dir in Cell.Dirs4)
                {
                    var n = c + dir;
                    if (seen.Contains(n) || Math.Max(Math.Abs(n.X - oc.X), Math.Abs(n.Y - oc.Y)) > 2) continue;
                    seen.Add(n);
                    if (ship.RoomAt(n) != oroom || !ship.IsWalkable(n) || ship.DoorAt(n) != null || ship.FurnitureAt(n) != null) continue;
                    q.Enqueue(n);
                }
            }
            if (scene.Spots.Count >= 8) break;
        }
        return scene.Spots.Count > 0 ? scene : null;
    }

    // ───────────────────────────── 고르기 (SpectateActivity) ─────────────────────────────

    internal (CrowdScene? s, int spot, int dist) Pick(CrewMember c, DistanceField dist)
    {
        CrowdScene? best = null; int bspot = -1, bd = int.MaxValue;
        foreach (var s in Scenes)
        {
            if (s.Ended || s.CrewId == c.Id || Shooed(c, s)) continue;
            for (int i = 0; i < s.Spots.Count; i++)
            {
                if (s.Taken[i] >= 0 && s.Taken[i] != c.Id) continue;
                int d = dist.Get(s.Spots[i]);
                if (d < 0 || d >= bd) continue;
                if (_w.Ship.RoomAt(s.Spots[i]) is Room r && (r.Unbreathable || _w.Fire.AnyWithin(s.Spots[i], 2.2f))) continue;
                best = s; bspot = i; bd = d;
            }
        }
        return (best, bspot, bd);
    }

    internal void Claim(CrowdScene s, int spot, CrewMember c)
    {
        for (int i = 0; i < s.Taken.Count; i++) if (s.Taken[i] == c.Id) s.Taken[i] = -1;
        s.Taken[spot] = c.Id;
    }

    internal void Arrive(CrewMember c, CrowdScene s)
    {
        var w = _w;
        _watching[c.Id] = s.Id;
        if (!s.Watchers.Contains(c.Id)) s.Watchers.Add(c.Id);
        Stats.Watchers++;
        if (s.HasBelief) w.Brain2.Beliefs.Learn(c, s.Topic, s.TopicId, 1, BeliefSource.Seen, 0.9f);
        if (c.SaidUntil < w.Tick)
            c.Say(w, Persona.Say(c, s.Topic == Topic.Down && s.HasBelief ? "괜찮아? 무슨 일이야?" : s.Topic == Topic.Fire && s.HasBelief ? "불이다… 저거 봐" : "무슨 일이래?"));
        if (s.Topic == Topic.Fire && s.HasBelief) w.Brain2.Emotions.Feel(c, Feeling.Fear, 0.08f, $"{s.What} 구경");
    }

    internal void Depart(CrewMember c, CrowdScene s, long watched)
    {
        _watching.Remove(c.Id);
        s.Watchers.Remove(c.Id);
        if (watched < SimTime.Minutes(0.5f)) return; // 잠깐이라도 봤으면 말한다
        // 본 것을 퍼뜨린다 — 말이 많은 사람은 부풀린다
        bool talker = Life.Has(c, Habit.Talker) || Life.Has(c, Habit.Joker) || Life.Has(c, Habit.Prankster);
        Rumors.Add(new Rumor
        {
            Teller = c.Id, SceneId = s.Id, HasBelief = s.HasBelief, Topic = s.Topic, Id = s.TopicId, What = s.What,
            Exaggerate = talker || c.Traits.Calm < 0.3f, Left = 2 + (talker ? 2 : 0) + (c.Traits.Sociability > 0.7f ? 1 : 0), Until = _w.Tick + SimTime.Hours(4),
        });
    }

    internal void Shoo(CrewMember c, CrowdScene s, long hours = 1)
    {
        _shooed[c.Id] = (s.Id, _w.Tick + SimTime.Hours(hours));
    }

    public bool ShooedNow(CrewMember c, CrowdScene s) => Shooed(c, s);

    // ───────────────────────────── 비켜! ─────────────────────────────

    /// <summary>SqueezeMul: 구경꾼 칸을 비집고 지난다 (그 현장에 "막혔다" 표시).</summary>
    internal void NoteSqueeze(CrewMember c, Cell cell)
    {
        foreach (var s in Scenes)
        {
            if (s.Ended) continue;
            int i = s.Spots.IndexOf(cell);
            if (i < 0 || s.Taken[i] < 0) continue;
            if (!_squeeze.TryGetValue(c.Id, out var last) || last.scene != s.Id || _w.Tick - last.tick > SimTime.Minutes(3)) Stats.Squeezes++;
            _squeeze[c.Id] = (s.Id, _w.Tick);
            s.Squeezed = _w.Tick; s.SqueezedBy = c.Id;
            return;
        }
    }

    private bool Responder(CrewMember c, CrowdScene s) =>
        c.Job is Job j && (j.Urgent || j.Order != null) && (j.TargetRoom?.Id == s.RoomId || j.Order?.Target.Crew?.Id == s.CrewId && s.CrewId >= 0);

    private void ShoutCheck(CrowdScene s, long now)
    {
        var w = _w;
        bool blocked = s.Squeezed >= 0 && now - s.Squeezed < SimTime.Minutes(3);
        var door = s.Spots[0].Center;
        int responders = 0;
        foreach (var c in w.Crew) if (c.CanAct && Responder(c, s) && (c.Position - door).LengthSquared() < 36f) responders++;
        if (!blocked && (s.Watchers.Count < 2 || responders == 0)) return;
        if (s.ShoutAt >= 0 && now - s.ShoutAt < SimTime.Minutes(3)) return;
        // 책임자: 지휘자 → 선장 → 당직 → 대응자(앞장서는 사람) → 비집던 사람
        CrewMember? chief = null; int rank = 9;
        foreach (var c in w.Crew)
        {
            if (!c.CanAct || c.Outside || IsWatcher(c) || (c.Position - door).LengthSquared() > 100f) continue;
            int r = w.Command.Active && w.Command.Commander == c ? 0 : w.Command.CaptainId == c.Id ? 1 : w.Society.OnNightWatch(c) ? 2
                : Responder(c, s) ? (Life.Has(c, Habit.Leader) || c.Traits.Diligence > 0.65f ? 3 : 4) : c.Id == s.SqueezedBy ? 5 : 9;
            if (r < rank) { rank = r; chief = c; }
        }
        if (chief == null || rank >= 9) return;
        string title = rank switch { 0 => "지휘자 ", 1 => "선장 ", 2 => "당직 ", _ => "" };
        s.ShoutLine = rank <= 2 ? "비켜! 현장 비워!" : Life.Has(chief, Habit.ShortTempered) ? "비켜! 길 막지 마!" : "비켜 줘! 지나가야 해!";
        chief.Say(w, Persona.Say(chief, s.ShoutLine));
        s.ShoutAt = now; s.Shouter = chief.Id; s.Shouts++;
        Stats.Shouts++;
        int obeyed = 0, total = 0;
        foreach (var id in s.Watchers.ToList())
        {
            if (CrewById(id) is not CrewMember v) continue;
            total++;
            float obey = 0.55f + (rank <= 2 ? 0.3f : 0.1f) + 0.15f * v.Traits.Diligence - (Life.Has(v, Habit.Daredevil) || Life.Has(v, Habit.Prankster) ? 0.2f : 0f)
                         + 0.2f * MathF.Max(0f, v.AffinityTo(chief)) - (v.AffinityTo(chief) < -0.3f ? 0.2f : 0f);
            if (R.Chance(obey))
            {
                obeyed++;
                Stats.Obeyed++;
                Shoo(v, s);
                w.Brain2.Emotions.Feel(v, Feeling.Shame, 0.1f, "현장에서 비키라는 말을 들었다", chief);
                if (Life.Has(chief, Habit.ShortTempered)) v.ChangeAffinity(chief, -0.02f);
            }
            else
            {
                Stats.Ignored++;
                if (v.SaidUntil < now) v.Say(w, Persona.Say(v, "보기만 하는 건데…"));
                chief.ChangeAffinity(v, -0.02f);
            }
        }
        string where = s.RoomId >= 0 && s.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[s.RoomId].Name : "현장";
        w.Log.Add(now, LogKind.Life, $"{title}{chief.Name}: \"{s.ShoutLine}\" — {where} 앞 구경꾼 {total}명 중 {obeyed}명이 물러났다", chief.Id);
        if (s.RoomId >= 0 && s.RoomId < w.Ship.Rooms.Count) MarkLog.Add(w.Ship.Rooms[s.RoomId].Marks, now, $"{Ko.IGa(chief.Name)} 구경꾼을 물렸다 ({obeyed}/{total})");
    }

    private void ComputerCheck(CrowdScene s, long now)
    {
        var w = _w;
        if (s.Watchers.Count < 3 || s.Advised >= 0 && now - s.Advised < SimTime.Minutes(20)) return;
        var au = w.Automation;
        if (!au.Present || !au.MainOnline) return;
        var room = s.RoomId >= 0 && s.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[s.RoomId] : null;
        if (room == null || !room.DataLinked) return;
        int responders = w.Crew.Count(c => c.CanAct && Responder(c, s));
        if (au.Book.Add(ActKind.Broadcast, room, $"{room.Name} 앞 사람 {s.Watchers.Count}명 · 대응 {responders}명", "판단: 문 앞 통로가 막혀 대응이 늦어진다", "선내 방송",
                "현장 앞을 비워 달라 — 대응자 먼저", $"crowd:{s.Id}", SimTime.Minutes(20), 15f) == null) return;
        s.Advised = now;
        Stats.ComputerCalls++;
        foreach (var id in s.Watchers.ToList())
            if (CrewById(id) is CrewMember v && au.Trusts.Of(v) >= 0.55f) { Shoo(v, s); Stats.ComputerShoos++; }
    }

    // ───────────────────────────── 소문 ─────────────────────────────

    private void Spread(long now)
    {
        var w = _w;
        if (Rumors.Count == 0) return;
        var bel = w.Brain2.Beliefs;
        foreach (var r in Rumors)
        {
            if (r.Left <= 0 || r.Until <= now) continue;
            if (CrewById(r.Teller) is not CrewMember t || !t.CanAct || t.Pose == Pose.Sleeping || t.Job?.Urgent == true || IsWatcher(t) || t.Room == null) continue;
            foreach (var o in w.Crew)
            {
                if (o == t || !o.CanAct || o.Pose == Pose.Sleeping || o.Room != t.Room || (o.Position - t.Position).LengthSquared() > 9f || IsWatcher(o) || r.Told.Contains(o.Id)) continue;
                if (r.HasBelief && bel.Get(o, r.Topic, r.Id) is Belief b && b.Value == 1 && BeliefSystem.Eff(b, now) > 0.5f) continue; // 이미 안다
                r.Told.Add(o.Id);
                r.Left--;
                Heard.Add((t.Id, o.Id, now, r.Exaggerate));
                if (Heard.Count > 40) Heard.RemoveAt(0);
                Stats.Rumors++;
                if (r.HasBelief) bel.Learn(o, r.Topic, r.Id, 1, BeliefSource.Rumor, r.Exaggerate ? 0.7f : 0.55f, t.Id);
                if (r.Exaggerate) Stats.Exaggerated++;
                string line = r.HasBelief && r.Topic == Topic.Fire
                    ? (r.Exaggerate ? $"{r.What} — 엄청 커, 곧 번진대" : $"{r.What} — 내가 봤어")
                    : r.HasBelief && r.Topic == Topic.Down ? (r.Exaggerate ? $"{r.What} — 피가 많이 났대" : $"{r.What} — 내가 봤어")
                    : (r.Exaggerate ? $"{r.What} — 큰일 났대" : $"{r.What} — 봤어?");
                t.Say(w, Persona.Say(t, line));
                if (r.Exaggerate) w.Brain2.Emotions.Feel(o, Feeling.Fear, 0.06f, $"소문: {r.What}");
                break;
            }
        }
        Rumors.RemoveAll(r => r.Left <= 0 || r.Until <= now);
    }

    // ───────────────────────────── 길 · 화면 · 지문 ─────────────────────────────

    /// <summary>자리에 서 있는 구경꾼 칸.</summary>
    public IEnumerable<Cell> CrowdCells()
    {
        foreach (var s in Scenes)
        {
            if (s.Ended) continue;
            for (int i = 0; i < s.Spots.Count; i++)
                if (s.Taken[i] >= 0 && s.Watchers.Contains(s.Taken[i])) yield return s.Spots[i];
        }
    }

    public void PathCost(int[] cost)
    {
        var w = _w;
        var grid = w.Ship.Grid;
        foreach (var cell in CrowdCells())
            if (grid.InBounds(cell)) { int i = grid.Index(cell); if (i < cost.Length) cost[i] += w.Matter.InAisle(cell) ? 12 : 6; }
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Scenes.Count);
        foreach (var s in Scenes) { I(s.CaseId); I(s.Watchers.Count); I(s.Shouts); I(s.Ended ? 1 : 0); }
        I(Rumors.Count);
        var st = Stats; I(st.Scenes); I(st.Watchers); I(st.Squeezes); I(st.Shouts); I(st.Obeyed); I(st.Ignored); I(st.ComputerCalls); I(st.Rumors);
    }
}

/// <summary>구경: 사고 현장에 가서 문 앞 · 둘레에 서서 본다 ("비켜!"를 들으면 물러난다 · 본 것을 퍼뜨린다).</summary>
public sealed class SpectateActivity : Activity
{
    public override string Id => "spectate";
    public override string Label => "구경";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var cs = w.Coop.Crowds;
        if (cs.Active == 0 || !c.CanAct || c.Outside || c.CarryingPerson != null || c.Job?.Urgent == true || c.Suit != null) return (0f, "—");
        if (c.Job?.Order != null && c.Job.Activity is ChoresActivity) return (0f, "—"); // 일하던 사람은 손을 놓지 않는다
        var (s, _, d) = cs.Pick(c, dist);
        if (s == null) return (0f, "—");
        if (c.Job?.TargetRoom?.Id == s.RoomId && c.Job.Activity is not SpectateActivity) return (0f, "—");
        float k = 0.3f + 0.25f * c.Traits.Sociability + (Life.Has(c, Habit.Talker) ? 0.1f : 0f) + (Life.Has(c, Habit.Gazer) ? 0.12f : 0f)
                  + (Life.Has(c, Habit.Joker) || Life.Has(c, Habit.Prankster) ? 0.06f : 0f) + (Life.Has(c, Habit.Worrier) ? 0.05f : 0f)
                  - (Life.Has(c, Habit.Loner) ? 0.15f : 0f) - (Life.Has(c, Habit.Serious) ? 0.05f : 0f) - d / 3000f;
        if (s.HasBelief && s.Topic is Topic.Fire or Topic.Breach) k -= 0.25f * (1f - c.Traits.Bravery);
        if (s.CrewId >= 0 && w.Crew.FirstOrDefault(x => x.Id == s.CrewId) is CrewMember v) k += 0.2f * MathF.Max(0f, c.AffinityTo(v));
        if (OnShift(c, w)) k -= 0.1f + 0.15f * c.Traits.Diligence;
        if (Bedtime(c, w)) k -= 0.3f;
        if (c.Pose == Pose.Sleeping) k -= 0.5f;
        if (c.IsChild) k += 0.1f;
        return (Math.Clamp(k, 0f, 0.7f), $"{s.What} — 무슨 일인가 보러");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var cs = w.Coop.Crowds;
        var (s, spot, _) = cs.Pick(c, dist);
        if (s == null) return null;
        cs.Claim(s, spot, c);
        float k = 0.5f + 0.5f * c.Traits.Sociability;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new WatchToil(s, s.Spots[spot], SimTime.Minutes(12 + 25 * k)));
        return new Job(this, "구경", toils) { LogText = $"{s.What} — 구경하러 간다", LogKind = LogKind.Life, InterruptMargin = 0.1f };
    }
}

/// <summary>현장 앞 자리로 가서 본다 · 물러나라면 몇 걸음 떨어진 곳으로 비킨다.</summary>
public sealed class WatchToil : Toil
{
    private readonly CrowdScene _s;
    private readonly Cell _spot;
    private readonly int _max;
    private int _elapsed;
    private long _arrived = -1;
    private bool _ok, _leaving;
    internal CrowdScene Scene => _s;

    public WatchToil(CrowdScene s, Cell spot, int maxTicks) { _s = s; _spot = spot; _max = maxTicks; }

    public override void Begin(CrewMember c, World w)
    {
        _ok = c.Cell == _spot || Locomotion.SetDestination(c, w, _spot);
        if (_ok && c.Path is { Count: > 0 }) c.Pose = Pose.Walking;
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        var cs = w.Coop.Crowds;
        if (!_ok) return ToilStatus.Failed;
        if (_leaving)
        {
            if (c.Path == null || Locomotion.Step(c, w)) return ToilStatus.Succeeded;
            return c.PathBlocked ? ToilStatus.Succeeded : ToilStatus.Running;
        }
        if (_s.Ended || cs.ShooedNow(c, _s))
        {
            if (_arrived < 0) return ToilStatus.Succeeded;
            cs.Depart(c, _s, w.Tick - _arrived);
            _arrived = -1;
            if (_s.Ended) return ToilStatus.Succeeded;
            // 몇 걸음 물러난다
            _leaving = true;
            if (Away(c, w) is Cell back && Locomotion.SetDestination(c, w, back)) { c.Pose = Pose.Walking; return ToilStatus.Running; }
            return ToilStatus.Succeeded;
        }
        if (c.Path != null)
        {
            if (Locomotion.Step(c, w)) c.Path = null;
            else if (c.PathBlocked) return ToilStatus.Failed;
            return ToilStatus.Running;
        }
        if (_arrived < 0) { _arrived = w.Tick; c.Pose = Pose.Standing; cs.Arrive(c, _s); }
        Locomotion.Face(c, _s.Center);
        if (EvacuateActivity.DangerHere(c, w) > 0.35f) { cs.Depart(c, _s, w.Tick - _arrived); _arrived = -1; return ToilStatus.Failed; }
        _elapsed++;
        if (_elapsed >= _max) { cs.Depart(c, _s, w.Tick - _arrived); _arrived = -1; return ToilStatus.Succeeded; }
        return ToilStatus.Running;
    }

    /// <summary>현장에서 4칸 넘게 떨어진 같은 방의 빈 바닥 (가까운 것부터).</summary>
    private Cell? Away(CrewMember c, World w)
    {
        var ship = w.Ship;
        var room = ship.RoomAt(c.Cell);
        if (room == null) return null;
        Cell? best = null; int bd = int.MaxValue;
        var door = _s.Spots.Count > 0 ? _s.Spots[0] : c.Cell;
        foreach (var x in room.Cells)
        {
            int r = Math.Abs(x.X - door.X) + Math.Abs(x.Y - door.Y);
            if (r < 5 || !ship.IsOpenFloor(x) || _s.Spots.Contains(x)) continue;
            int d = Math.Abs(x.X - c.Cell.X) + Math.Abs(x.Y - c.Cell.Y);
            if (d < bd) { bd = d; best = x; }
        }
        return best;
    }

    public override void End(CrewMember c, World w)
    {
        if (_arrived >= 0) { w.Coop.Crowds.Depart(c, _s, w.Tick - _arrived); _arrived = -1; }
        c.Path = null;
        c.Destination = null;
        if (c.Pose == Pose.Walking) c.Pose = Pose.Standing;
    }
}
