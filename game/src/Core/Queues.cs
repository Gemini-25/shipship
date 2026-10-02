using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.4 줄 서기: 배식기 · 샤워 · 화장실(세면대) · 커피 머신 — 한 번에 한 사람.
//  · 줄 자리: 받는 칸에서 설비 반대쪽으로 곧게, 벽 · 가구를 만나면 꺾인다 (번호 · 기다린 시간).
//  · 새치기: 급함(배고픔 · 다침 · 곧 근무 · 아이) × 성격(서두름 · 욱함 · 대담 · 자유) — 친구 뒤에 끼기도 한다.
//    끼어든 바로 뒷사람이 반응한다: 양보(참을성 · 너그러움 · 친함 · 정말 급해 보임) → 고마움 · 곁에 앉기,
//    다툼(욱함 · 투덜 · 규칙 · 배고픔) → 분노 · 서운함 · 말다툼(화해 대화로 이어진다) · 저녁 자리를 떨어져 앉는다. 차분한 새치기꾼은 뒤로 물러난다.
//  · 먼저 하세요: 다친 사람 · 아이 · 급한 일을 맡은 사람에게 너그러운 사람이 자리를 내준다.
//  · 오래 기다리면 투덜 · 씻기 줄은 포기하기도 한다. 주 컴퓨터는 줄 길이 · 기다림을 보고 급하지 않은 사람에게 나중에 오라고 순서를 나눈다(믿는 만큼 따른다).

public enum QueueKind : byte { Meal, Shower, Toilet, Coffee }
public enum QueueEventKind : byte { Cut, Quarrel, Yield, BackOff, Offer, Defer, GiveUp, SeatAway, SeatNear }

public sealed class ServiceQueue
{
    public int Id { get; init; }
    public QueueKind Kind { get; init; }
    public int FurnitureId { get; init; } = -1;
    public int RoomId { get; init; } = -1;
    /// <summary>받는 칸 (맨 앞사람이 여기서 받는다).</summary>
    public Cell Spot { get; init; }
    /// <summary>바라보는 곳 (배식기 · 세면대 벽).</summary>
    public Vector2 Toward { get; init; }
    public string Label { get; init; } = "";
    /// <summary>줄 선 사람 (앞에서부터) · 들어온 때.</summary>
    public List<int> Line { get; } = new();
    public List<long> Joined { get; } = new();
    /// <summary>줄 자리 칸 (0 = 맨 앞 — 받는 칸 바로 뒤).</summary>
    public List<Cell> Slots { get; } = new();
    public int Serving { get; internal set; } = -1;
    public long ServingSince { get; internal set; }
    public long LastUse { get; internal set; }
    public long Advised { get; internal set; } = -1;
    public int Served { get; internal set; }
    public int MaxLen { get; internal set; }
    public Cell SlotCell(int i) => Slots.Count == 0 ? Spot : Slots[Math.Clamp(i, 0, Slots.Count - 1)];
}

public sealed class QueueEvent
{
    public long Tick { get; init; }
    public QueueEventKind Kind { get; init; }
    public int A { get; init; }
    public int B { get; init; } = -1;
    public int QueueId { get; init; } = -1;
}

/// <summary>줄에서 생긴 사이 (자리 고르기에 18시간 남는다): 다툼 = 떨어져 앉기 · 양보 = 곁에 앉기.</summary>
public sealed class Feud
{
    public int A { get; init; }
    public int B { get; init; }
    public long Tick { get; init; }
    public bool Quarrel { get; init; }
}

public sealed class QueueStats
{
    public int Joined, Served, Cuts, Quarrels, Yields, BackOffs, Offers, GiveUps, Grumbles, Advised, Deferred, SeatAway, SeatNear, MaxLen;
    public float WaitMinutes;
    public string Line() => $"줄 섬 {Joined}(받음 {Served} · 가장 긴 줄 {MaxLen}명 · 기다림 {WaitMinutes:0}분) · 새치기 {Cuts}(다툼 {Quarrels} · 양보 {Yields} · 물러남 {BackOffs}) · 먼저 하세요 {Offers} · " +
                            $"투덜 {Grumbles} · 포기 {GiveUps} · 컴퓨터 순서 나눔 {Advised}(나중에 옴 {Deferred}) · 자리: 떨어져 앉음 {SeatAway} · 곁에 앉음 {SeatNear}";
}

public sealed class QueueSystem
{
    private readonly World _w;
    private readonly CoopSystem _co;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7927 + 3313));

    public List<ServiceQueue> All { get; } = new();
    public List<QueueEvent> Events { get; } = new();
    public List<Feud> Feuds { get; } = new();
    public QueueStats Stats { get; } = new();
    /// <summary>시험용: 이 사람은 다음 줄에서 꼭 끼어든다.</summary>
    public int ForceCut { get; set; } = -1;

    private readonly Dictionary<int, (int q, int victim)> _pending = new();
    private readonly Dictionary<int, int> _grumbled = new();
    private readonly HashSet<int> _deferred = new();
    /// <summary>마지막 커피 한 잔 (사람 id → 틱).</summary>
    internal Dictionary<int, long> Coffee { get; } = new();
    internal long LastCoffee(CrewMember c) => Coffee.TryGetValue(c.Id, out var t) ? t : -1_000_000;

    public QueueSystem(World w, CoopSystem co) { _w = w; _co = co; }

    public static string KindName(QueueKind k) => k switch { QueueKind.Meal => "배식", QueueKind.Shower => "샤워", QueueKind.Toilet => "화장실", _ => "커피 머신" };

    private CrewMember? CrewById(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }

    // ───────────────────────────── 줄 찾기 · 만들기 ─────────────────────────────

    public ServiceQueue For(QueueKind kind, Furniture? f, Cell spot)
    {
        var w = _w;
        var room = f?.Room ?? w.Ship.RoomAt(spot);
        if (f == null && kind is QueueKind.Shower or QueueKind.Toilet)
            kind = room?.Type is RoomType.Laundry or RoomType.Decon or RoomType.Gym ? QueueKind.Shower : QueueKind.Toilet;
        foreach (var q in All)
        {
            if (q.Kind != kind) continue;
            if (f != null ? q.FurnitureId == f.Id : q.FurnitureId < 0 && q.RoomId == (room?.Id ?? -1)) return q;
        }
        Cell at = spot;
        Vector2 toward;
        if (f != null) toward = f.Center;
        else
        {
            // 씻는 곳: 방에서 벽을 가장 많이 등진 빈 바닥 (세면대 · 샤워 칸)
            at = WashStation(room, spot, out toward);
        }
        var nq = new ServiceQueue
        {
            Id = _co.NextId(), Kind = kind, FurnitureId = f?.Id ?? -1, RoomId = room?.Id ?? -1, Spot = at, Toward = toward,
            Label = f != null ? f.Label : $"{room?.Name ?? ""} {KindName(kind)}",
        };
        BuildSlots(nq);
        All.Add(nq);
        return nq;
    }

    private Cell WashStation(Room? room, Cell fallback, out Vector2 toward)
    {
        var ship = _w.Ship;
        toward = fallback.Center;
        if (room == null) return fallback;
        Cell best = fallback; int bk = int.MinValue;
        foreach (var c in room.Cells)
        {
            if (!ship.IsOpenFloor(c)) continue;
            bool doorNear = false; int walls = 0; Cell wall = c;
            foreach (var d in Cell.Dirs4)
            {
                if (ship.DoorAt(c + d) != null) doorNear = true;
                if (ship.Grid.Kind(c + d) == TileKind.Wall) { walls++; wall = c + d; }
            }
            if (doorNear) continue;
            int key = walls * 1000 - c.Y * 10 - c.X;
            if (key > bk) { bk = key; best = c; toward = wall.Center; }
        }
        return best;
    }

    /// <summary>줄 자리: 받는 칸에서 바라보는 곳 반대쪽으로 곧게, 막히면 꺾는다.</summary>
    private void BuildSlots(ServiceQueue q)
    {
        var ship = _w.Ship;
        q.Slots.Clear();
        var away = q.Spot.Center - q.Toward;
        Cell dir = MathF.Abs(away.X) >= MathF.Abs(away.Y) ? new Cell(away.X >= 0 ? 1 : -1, 0) : new Cell(0, away.Y >= 0 ? 1 : -1);
        var used = new HashSet<Cell> { q.Spot };
        var cur = q.Spot;
        for (int k = 0; k < 10; k++)
        {
            Cell? next = null;
            var left = new Cell(-dir.Y, dir.X);
            var right = new Cell(dir.Y, -dir.X);
            foreach (var cd in new[] { dir, left, right })
            {
                var n = cur + cd;
                if (used.Contains(n) || !ship.IsWalkable(n) || ship.FurnitureAt(n) != null || ship.DoorAt(n) != null) continue;
                next = n; dir = cd; break;
            }
            if (next is not Cell nc) break;
            q.Slots.Add(nc); used.Add(nc); cur = nc;
        }
    }

    // ───────────────────────────── 줄 서기 ─────────────────────────────

    /// <summary>줄에 선다. true = 바로 받는다 (줄이 없다).</summary>
    internal bool Join(CrewMember c, ServiceQueue q)
    {
        long now = _w.Tick;
        q.LastUse = now;
        if (q.Serving == c.Id) return true;
        if (q.Line.Contains(c.Id)) return false;
        if (q.Serving >= 0 && CrewById(q.Serving) is not { CanAct: true }) q.Serving = -1;
        if (q.Line.Count == 0 && q.Serving < 0) { Serve(q, c); return true; }
        Stats.Joined++;
        int pos = q.Line.Count;
        int victim = -1;
        var cut = DecideCut(c, q);
        if (cut >= 0) { pos = cut; victim = q.Line[cut]; }
        q.Line.Insert(pos, c.Id);
        q.Joined.Insert(pos, now);
        if (victim >= 0)
        {
            Stats.Cuts++;
            _pending[c.Id] = (q.Id, victim);
            AddEvent(QueueEventKind.Cut, c.Id, victim, q.Id);
        }
        else Offer(c, q);
        if (q.Line.Count > q.MaxLen) q.MaxLen = q.Line.Count;
        if (q.Line.Count > Stats.MaxLen) Stats.MaxLen = q.Line.Count;
        return false;
    }

    private static bool Genuine(CrewMember c, World w) =>
        c.Vitals.Injury > 0.3f || c.Fx.Worst > 0.4f || c.IsChild || c.Job?.Urgent == true || ShiftSoon(c, w);

    private static bool ShiftSoon(CrewMember c, World w)
    {
        float h = SimTime.HourOfDay(w.Tick);
        return SimTime.InWindow(h + 0.4f, c.Schedule.WorkStart, c.Schedule.WorkLength) && !SimTime.InWindow(h, c.Schedule.WorkStart, c.Schedule.WorkLength);
    }

    /// <summary>끼어들 자리 (-1 = 맨 뒤에 선다).</summary>
    private int DecideCut(CrewMember c, ServiceQueue q)
    {
        var w = _w;
        bool force = ForceCut == c.Id;
        if (q.Line.Count < 2 && !(force && q.Line.Count >= 1)) return -1;
        float u = 0f;
        if (q.Kind == QueueKind.Meal) u += c.Needs.Hunger > 0.85f ? 0.3f : c.Needs.Hunger > 0.7f ? 0.12f : 0f;
        if (c.Vitals.Injury > 0.3f || c.Fx.Worst > 0.4f) u += 0.2f;
        if (ShiftSoon(c, w)) u += 0.2f;
        if (c.IsChild) u += 0.2f;
        float p = (Life.Has(c, Habit.Hasty) ? 0.25f : 0f) + (Life.Has(c, Habit.ShortTempered) ? 0.1f : 0f) + (Life.Has(c, Habit.Daredevil) ? 0.1f : 0f)
                  + (Life.Has(c, Habit.Prankster) ? 0.1f : 0f) + (c.Value == CrewValue.Freedom ? 0.1f : c.Value == CrewValue.Efficiency ? 0.05f : 0f)
                  - (Life.Has(c, Habit.Patient) ? 0.3f : 0f) - (Life.Has(c, Habit.Methodical) ? 0.15f : 0f) - (c.Value == CrewValue.Rules ? 0.25f : 0f)
                  - 0.2f * (c.Traits.Diligence - 0.5f) - 0.1f * c.Traits.Calm;
        int friend = -1;
        for (int i = 1; i < q.Line.Count - 1; i++)
            if (CrewById(q.Line[i]) is CrewMember o && c.AffinityTo(o) > 0.35f) { friend = i; break; }
        float chance = Math.Clamp(u + p - 0.04f + (friend >= 0 ? 0.15f : 0f), 0f, 0.75f);
        if (!force && (chance <= 0f || !R.Chance(chance))) return -1;
        if (force) ForceCut = -1;
        int pos = friend >= 0 ? friend + 1 : Math.Min(1, q.Line.Count - 1);
        return pos < q.Line.Count ? pos : -1;
    }

    /// <summary>먼저 하세요: 다친 사람 · 아이 · 급한 사람이 뒤에 서면 너그러운 사람이 앞자리를 내준다.</summary>
    private void Offer(CrewMember u, ServiceQueue q)
    {
        var w = _w;
        if (!Genuine(u, w)) return;
        int me = q.Line.IndexOf(u.Id);
        for (int i = 1; i < me; i++)
        {
            if (CrewById(q.Line[i]) is not CrewMember m || !m.CanAct) continue;
            bool kind = Life.Has(m, Habit.Generous) || Life.Has(m, Habit.Patient) || Life.Has(m, Habit.Cheerful) || m.Traits.Sociability > 0.72f;
            if (!kind || m.Needs.Hunger > 0.75f && q.Kind == QueueKind.Meal || m.AffinityTo(u) < -0.2f) continue;
            q.Line.RemoveAt(me); long t = q.Joined[me]; q.Joined.RemoveAt(me);
            q.Line.Insert(i, u.Id); q.Joined.Insert(i, t);
            Stats.Offers++;
            AddEvent(QueueEventKind.Offer, m.Id, u.Id, q.Id);
            m.Say(w, Persona.Say(m, u.IsChild ? "꼬마야, 먼저 받아" : u.Vitals.Injury > 0.3f ? "다쳤잖아 — 먼저 해" : "급하지? 먼저 해"));
            u.Say(w, Persona.Say(u, "고마워!"));
            w.Relations.Remember(u, m, RelationReason.LetMeFirst, $"{KindName(q.Kind)} 줄에서 먼저 하라고 자리를 내줬다");
            u.ChangeAffinity(m, 0.05f);
            Feuds.Add(new Feud { A = u.Id, B = m.Id, Tick = w.Tick, Quarrel = false });
            return;
        }
    }

    /// <summary>끼어든 사람이 자리에 닿았다 — 바로 뒷사람이 반응한다.</summary>
    internal void OnArrive(CrewMember c, ServiceQueue q)
    {
        if (!_pending.TryGetValue(c.Id, out var pend) || pend.q != q.Id) return;
        _pending.Remove(c.Id);
        var w = _w;
        long now = w.Tick;
        int me = q.Line.IndexOf(c.Id);
        if (me < 0 || CrewById(pend.victim) is not CrewMember v || !q.Line.Contains(v.Id) || !v.CanAct) return;
        var emo = w.Brain2.Emotions;
        float yieldS = (Life.Has(v, Habit.Patient) ? 0.3f : 0f) + (Life.Has(v, Habit.Generous) ? 0.25f : 0f) + (Life.Has(v, Habit.Cheerful) ? 0.1f : 0f) + 0.2f * v.Traits.Sociability
                       + 0.5f * MathF.Max(0f, v.AffinityTo(c)) + (Genuine(c, w) ? 0.3f : 0f) + (q.Kind == QueueKind.Meal && v.Needs.Hunger < 0.5f ? 0.15f : 0f) + 0.15f * v.Traits.Calm;
        float angerS = (Life.Has(v, Habit.ShortTempered) ? 0.35f : 0f) + (Life.Has(v, Habit.Grumbler) ? 0.2f : 0f) + (v.Value == CrewValue.Rules ? 0.2f : 0f)
                       + (q.Kind == QueueKind.Meal && v.Needs.Hunger > 0.75f ? 0.2f : 0f) + 0.3f * (1f - v.Traits.Calm) + 0.5f * MathF.Max(0f, -v.AffinityTo(c)) + 0.3f * v.Mind.Anger;
        string what = KindName(q.Kind);
        if (yieldS >= angerS)
        {
            Stats.Yields++;
            AddEvent(QueueEventKind.Yield, v.Id, c.Id, q.Id);
            v.Say(w, Persona.Say(v, "급하면 먼저 해"));
            c.Say(w, Persona.Say(c, "고마워, 다음에 갚을게"));
            w.Relations.Remember(c, v, RelationReason.LetMeFirst, $"{what} 줄에서 끼어든 나를 너그럽게 봐줬다");
            c.ChangeAffinity(v, 0.06f); v.ChangeAffinity(c, 0.02f);
            emo.Feel(c, Feeling.Joy, 0.12f, $"{what} 줄을 양보받았다", v);
            emo.Feel(v, Feeling.Pride, 0.08f, $"{what} 줄을 양보했다", c);
            Feuds.Add(new Feud { A = c.Id, B = v.Id, Tick = now, Quarrel = false });
            w.Log.Add(now, LogKind.Life, $"{Ko.IGa(c.Name)} {what} 줄에 끼어들었다 — {Ko.IGa(v.Name)} 너그럽게 양보했다", c.Id);
            return;
        }
        v.Say(w, Persona.Say(v, Life.Has(v, Habit.ShortTempered) ? $"야, {c.Name}! 줄 서! 다들 기다리잖아" : "저기, 줄 서 있는데요"));
        // 차분한 새치기꾼은 뒤로 물러난다 — 버티면 말다툼
        float back = (Life.Has(c, Habit.Patient) ? 0.3f : 0f) + 0.4f * c.Traits.Calm + (c.Value == CrewValue.Rules ? 0.2f : 0f) + 0.2f * c.Traits.Diligence
                     - (Life.Has(c, Habit.Hasty) ? 0.2f : 0f) - (Life.Has(c, Habit.ShortTempered) ? 0.2f : 0f) - (c.Needs.Hunger > 0.9f ? 0.1f : 0f);
        if (back > 0.45f)
        {
            Stats.BackOffs++;
            AddEvent(QueueEventKind.BackOff, c.Id, v.Id, q.Id);
            q.Line.RemoveAt(me); q.Joined.RemoveAt(me);
            q.Line.Add(c.Id); q.Joined.Add(now);
            c.Say(w, Persona.Say(c, "아, 미안 — 뒤로 갈게"));
            v.ChangeAffinity(c, -0.02f);
            emo.Feel(c, Feeling.Shame, 0.15f, $"{what} 줄에 끼어들다 지적받았다", v);
            return;
        }
        Stats.Quarrels++;
        AddEvent(QueueEventKind.Quarrel, v.Id, c.Id, q.Id);
        c.Say(w, Persona.Say(c, Genuine(c, w) ? "급해서 그래! 좀 봐줘" : "잠깐이면 돼, 뭘 그래"));
        emo.Feel(v, Feeling.Anger, 0.35f, $"{what} 줄 새치기", c);
        if (c.Traits.Diligence > 0.6f) emo.Feel(c, Feeling.Shame, 0.2f, $"{what} 줄에서 다퉜다", v);
        else emo.Feel(c, Feeling.Anger, 0.2f, $"{what} 줄에서 다퉜다", v);
        w.Relations.Remember(v, c, RelationReason.CutInLine, $"{what} 줄에 새치기하고 버텼다");
        v.ChangeAffinity(c, -0.1f); c.ChangeAffinity(v, -0.05f);
        v.Quarrel = c.Quarrel = now; // 말다툼 (감정 · 화해 대화 · 목표가 이어 받는다)
        for (int i = me + 2; i < q.Line.Count; i++) if (CrewById(q.Line[i]) is CrewMember wit) wit.ChangeAffinity(c, -0.02f); // 뒤에 선 사람들도 본다
        Feuds.Add(new Feud { A = v.Id, B = c.Id, Tick = now, Quarrel = true });
        w.Log.Add(now, LogKind.Life, $"{Ko.IGa(c.Name)} {what} 줄에 끼어들어 {Ko.WaGwa(v.Name)} 말다툼을 했다", c.Id);
        if (w.Ship.RoomAt(q.Spot) is Room room) MarkLog.Add(room.Marks, now, $"{what} 줄 새치기 다툼 ({c.Name} · {v.Name})");
        MarkLog.Add(v.Memory.Marks, now, $"{Ko.IGa(c.Name)} {what} 줄에 새치기했다");
    }

    private void Serve(ServiceQueue q, CrewMember c)
    {
        int i = q.Line.IndexOf(c.Id);
        if (i >= 0) { q.Line.RemoveAt(i); q.Joined.RemoveAt(i); }
        q.Serving = c.Id;
        q.ServingSince = _w.Tick;
        q.Served++;
        Stats.Served++;
        _grumbled.Remove(c.Id);
    }

    /// <summary>줄 맨 앞이고 받는 칸이 비었으면 받으러 간다.</summary>
    internal bool TryServe(CrewMember c, ServiceQueue q)
    {
        if (q.Line.Count == 0 || q.Line[0] != c.Id) return false;
        if (q.Serving >= 0 && q.Serving != c.Id) return false;
        // 끼어들어 바로 차례가 왔다 — 받기 전에 뒷사람이 먼저 한마디 한다 (물러나면 차례가 아니다)
        if (_pending.ContainsKey(c.Id)) { OnArrive(c, q); if (q.Line.Count == 0 || q.Line[0] != c.Id) return false; }
        Serve(q, c);
        return true;
    }

    internal void Leave(CrewMember c, ServiceQueue q, bool served)
    {
        _pending.Remove(c.Id);
        int i = q.Line.IndexOf(c.Id);
        if (i >= 0) { q.Line.RemoveAt(i); q.Joined.RemoveAt(i); }
        if (!served && q.Serving == c.Id) q.Serving = -1;
    }

    internal bool Deferred(CrewMember c) => _deferred.Remove(c.Id);

    /// <summary>기다리는 동안: 투덜 · 포기 (true = 포기).</summary>
    internal bool Waiting(CrewMember c, ServiceQueue q, int idx)
    {
        var w = _w;
        long waited = w.Tick - q.Joined[idx];
        float patience = (q.Kind == QueueKind.Meal ? 40f : q.Kind == QueueKind.Coffee ? 10f : 18f)
                         * (Life.Has(c, Habit.Patient) ? 1.6f : 1f) * (Life.Has(c, Habit.Hasty) ? 0.6f : 1f);
        if (q.Kind == QueueKind.Meal && c.Needs.Hunger > 0.8f) patience *= 3f; // 배고프면 버틴다
        int g = _grumbled.GetValueOrDefault(c.Id);
        if (g == 0 && waited > SimTime.Minutes(patience * 0.5f))
        {
            _grumbled[c.Id] = 1;
            Stats.Grumbles++;
            if (c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, Life.Has(c, Habit.Grumbler) ? $"이 줄은 언제 줄어…" : $"{KindName(q.Kind)} 줄 길다"));
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.01f);
        }
        if (waited > SimTime.Minutes(patience) && idx > 0)
        {
            Stats.GiveUps++;
            AddEvent(QueueEventKind.GiveUp, c.Id, -1, q.Id);
            c.Say(w, Persona.Say(c, "나중에 와야겠다"));
            _grumbled.Remove(c.Id);
            return true;
        }
        return false;
    }

    // ───────────────────────────── 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        long now = w.Tick;
        foreach (var q in All)
        {
            // 받는 사람이 받아 떠났나 (받는 칸에서 멀어졌다 · 쓰러졌다 · 너무 오래)
            if (q.Serving >= 0)
            {
                var s = CrewById(q.Serving);
                bool gone = s == null || !s.CanAct || now - q.ServingSince > SimTime.Minutes(8)
                            || now - q.ServingSince > SimTime.Minutes(1) && (s.Position - q.Spot.Center).LengthSquared() > 1.6f * 1.6f && s.Job?.Current is not QueueToil;
                if (gone) q.Serving = -1;
            }
            // 줄에서 일을 놓은 사람 (경보 · 다른 일) 은 빠진다
            for (int i = q.Line.Count - 1; i >= 0; i--)
                if (CrewById(q.Line[i]) is not CrewMember m || !m.CanAct || m.Job?.Current is not QueueToil qt || qt.Queue != q) { q.Line.RemoveAt(i); q.Joined.RemoveAt(i); }
            if (q.Line.Count > 0) Stats.WaitMinutes += q.Line.Count * dt * 60f;
            Advise(q, now);
        }
        if (Events.Count > 60) Events.RemoveRange(0, Events.Count - 60);
        Feuds.RemoveAll(f => now - f.Tick > SimTime.Hours(18));
    }

    /// <summary>주 컴퓨터: 줄이 길면 급하지 않은 사람에게 나중에 오라고 순서를 나눈다 (믿는 만큼 따른다).</summary>
    private void Advise(ServiceQueue q, long now)
    {
        var w = _w;
        if (q.Line.Count < 3) return;
        long longest = now - q.Joined.Min();
        if (q.Line.Count < 4 && longest < SimTime.Minutes(8)) return;
        if (q.Advised >= 0 && now - q.Advised < SimTime.Minutes(30)) return;
        var au = w.Automation;
        if (!au.Present || !au.MainOnline) return;
        var room = w.Ship.RoomAt(q.Spot);
        if (room == null || !room.DataLinked) return;
        int per = q.Kind == QueueKind.Meal ? 3 : q.Kind == QueueKind.Coffee ? 2 : 6;
        int eta = Math.Max(5, q.Line.Count * per);
        var later = new List<CrewMember>();
        for (int i = 2; i < q.Line.Count; i++)
        {
            if (CrewById(q.Line[i]) is not CrewMember m) continue;
            bool notUrgent = q.Kind == QueueKind.Meal ? m.Needs.Hunger < 0.6f : !Genuine(m, w);
            if (notUrgent && au.Trusts.Of(m) >= 0.45f) later.Add(m);
        }
        if (au.Book.Add(ActKind.Advice, room, $"{q.Label} 줄 {q.Line.Count}명 · 맨 앞 {longest / SimTime.Minutes(1)}분째",
                $"예측: 맨 뒤까지 약 {eta}분 · 받는 곳 하나", "순서 나눔", later.Count > 0 ? $"{string.Join("·", later.Select(m => m.Name))} — 급하지 않으면 {eta}분 뒤에" : "급하지 않은 사람은 나중에",
                $"queue:{q.Id}", SimTime.Minutes(30), 20f) == null) return;
        q.Advised = now;
        Stats.Advised++;
        foreach (var m in later)
        {
            Leave(m, q, false);
            _deferred.Add(m.Id);
            m.HoldUntil = now + SimTime.Minutes(eta);
            m.HoldWhy = $"{KindName(q.Kind)} 줄이 길다 — 컴퓨터가 {eta}분 뒤에 오라 했다";
            Stats.Deferred++;
            AddEvent(QueueEventKind.Defer, m.Id, -1, q.Id);
        }
    }

    // ───────────────────────────── 자리 ─────────────────────────────

    /// <summary>EatActivity: 자리 고르기 — 줄에서 다툰 사람 곁은 피하고, 양보해 준 사람 곁은 끌린다 (거리² 단위).</summary>
    public float SeatBias(CrewMember c, Furniture seat)
    {
        if (Feuds.Count == 0) return 0f;
        float b = 0f;
        foreach (var f in Feuds)
        {
            int other = f.A == c.Id ? f.B : f.B == c.Id ? f.A : -1;
            if (other < 0 || CrewById(other) is not CrewMember o || SeatPos(o) is not Vector2 op) continue;
            float d = (seat.Center - op).Length();
            if (f.Quarrel) { if (d < 3.5f) b += (3.5f - d) * 25f; }
            else if (d < 2.5f) b -= 6f;
        }
        return b;
    }

    /// <summary>그 사람이 앉았거나 앉으려고 잡아 둔 의자.</summary>
    private Vector2? SeatPos(CrewMember o)
    {
        if (o.Job is Job j) foreach (var r in j.Reservations) if (r.Type == FurnitureType.Seat) return r.Center;
        if (o.Pose == Pose.Sitting && o.Room?.Type is RoomType.Mess or RoomType.Lounge) return o.Position;
        return null;
    }

    /// <summary>음식을 받아 자리로 갈 때 다시 본다: 방금 다툰 사람 곁이면 떨어진 자리로, 양보해 준 사람 곁에 빈자리가 있으면 그리로.</summary>
    public Cell? SeatFor(CrewMember c, Furniture seat)
    {
        var w = _w;
        if (Feuds.Count == 0 || seat.UseSpots.Count == 0) return seat.UseSpots.Count > 0 ? seat.UseSpots[0] : null;
        long now = w.Tick;
        Feud? quarrel = null, kind = null;
        Vector2 qpos = default, kpos = default;
        foreach (var f in Feuds)
        {
            int other = f.A == c.Id ? f.B : f.B == c.Id ? f.A : -1;
            if (other < 0 || now - f.Tick > SimTime.Hours(6) || CrewById(other) is not CrewMember o || SeatPos(o) is not Vector2 op) continue;
            if (f.Quarrel && (seat.Center - op).Length() < 3.5f) { quarrel = f; qpos = op; }
            else if (!f.Quarrel && f.A == c.Id && (seat.Center - op).Length() >= 1.6f) { kind = f; kpos = op; }
        }
        if (quarrel == null && kind == null) return seat.UseSpots[0];
        Furniture? best = null; float bk = float.MaxValue;
        foreach (var s2 in seat.Room.Furniture)
        {
            if (s2.Type != FurnitureType.Seat || s2 == seat || s2.ReservedBy != null || s2.UseSpots.Count == 0 || w.IsSpotTaken(s2.UseSpots[0], c)) continue;
            float key = quarrel != null ? -(s2.Center - qpos).Length() : (s2.Center - kpos).Length();
            if (quarrel != null && kind != null) key += 0.3f * (s2.Center - kpos).Length();
            if (quarrel == null && (s2.Center - kpos).Length() > 1.6f) continue;
            if (key < bk) { bk = key; best = s2; }
        }
        if (best == null || quarrel != null && (best.Center - qpos).Length() <= (seat.Center - qpos).Length() + 0.5f) return seat.UseSpots[0];
        // 잡아 둔 자리를 바꾼다
        if (c.Job is Job job)
        {
            if (seat.ReservedBy == c) seat.ReservedBy = null;
            job.Reservations.Remove(seat);
            job.Reserve(best, c);
        }
        int otherId = quarrel != null ? (quarrel.A == c.Id ? quarrel.B : quarrel.A) : (kind!.B);
        var other2 = CrewById(otherId);
        if (quarrel != null)
        {
            Stats.SeatAway++;
            AddEvent(QueueEventKind.SeatAway, c.Id, otherId, -1);
            w.Log.Add(now, LogKind.Life, $"{other2?.Name ?? "그 사람"} 곁을 피해 떨어진 자리에 앉는다 (줄에서 다퉜다)", c.Id);
        }
        else
        {
            Stats.SeatNear++;
            AddEvent(QueueEventKind.SeatNear, c.Id, otherId, -1);
            w.Log.Add(now, LogKind.Life, $"줄을 양보해 준 {other2?.Name ?? "사람"} 곁에 앉는다", c.Id);
        }
        return best.UseSpots[0];
    }

    // ───────────────────────────── 길 · 화면 · 지문 ─────────────────────────────

    /// <summary>줄 선 칸은 지나는 사람이 조금 돌아간다 (통로에 선 줄).</summary>
    public void PathCost(int[] cost)
    {
        var w = _w;
        var grid = w.Ship.Grid;
        foreach (var q in All)
            for (int i = 0; i < q.Line.Count && i < q.Slots.Count; i++)
                if (grid.InBounds(q.Slots[i]) && w.Matter.InAisle(q.Slots[i])) { int k = grid.Index(q.Slots[i]); if (k < cost.Length) cost[k] += 3; }
    }

    private void AddEvent(QueueEventKind k, int a, int b, int q) => Events.Add(new QueueEvent { Tick = _w.Tick, Kind = k, A = a, B = b, QueueId = q });

    /// <summary>화면: 이 사람의 줄 번호(1부터)와 기다린 분 — 줄에 없으면 null.</summary>
    public (ServiceQueue q, int number, float minutes)? Place(CrewMember c)
    {
        foreach (var q in All)
        {
            int i = q.Line.IndexOf(c.Id);
            if (i >= 0) return (q, i + 1, (_w.Tick - q.Joined[i]) * 60f / SimTime.TicksPerHour);
            if (q.Serving == c.Id) return (q, 0, 0f);
        }
        return null;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(All.Count);
        foreach (var q in All) { I(q.Serving); I(q.Line.Count); foreach (var id in q.Line) I(id); I(q.Served); }
        I(Feuds.Count);
        var s = Stats; I(s.Joined); I(s.Served); I(s.Cuts); I(s.Quarrels); I(s.Yields); I(s.BackOffs); I(s.Offers); I(s.GiveUps); I(s.Advised); I(s.Deferred); I(s.SeatAway); I(s.SeatNear);
    }
}

/// <summary>줄에 서서 차례를 기다리다, 맨 앞이 되고 받는 칸이 비면 받는 칸으로 간다 (끝나면 받는 칸에 서 있다).</summary>
public sealed class QueueToil : Toil
{
    private readonly QueueKind _kind;
    private readonly Furniture? _f;
    private readonly Cell _spot;
    private ServiceQueue? _q;
    private bool _serving, _arrivedOnce, _ok = true;
    private Cell _slot = new(-9999, -9999);
    internal ServiceQueue? Queue => _q;

    public QueueToil(QueueKind kind, Furniture? f, Cell spot)
    {
        _kind = kind;
        _f = f;
        _spot = spot;
    }

    private Cell Target => _q?.Kind is QueueKind.Shower or QueueKind.Toilet ? _q.Spot : _spot;

    public override void Begin(CrewMember c, World w)
    {
        var qs = w.Coop.Queues;
        _q = qs.For(_kind, _f, _spot);
        if (qs.Join(c, _q)) GoServe(c, w);
    }

    private void GoServe(CrewMember c, World w)
    {
        _serving = true;
        if (c.Cell == Target) { c.Path = null; return; }
        _ok = Locomotion.SetDestination(c, w, Target);
        if (_ok && c.Path is { Count: > 0 }) c.Pose = Pose.Walking;
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        var q = _q!;
        var qs = w.Coop.Queues;
        if (_serving)
        {
            if (!_ok) return ToilStatus.Failed;
            if (c.Path == null || Locomotion.Step(c, w)) { c.Pose = Pose.Standing; Locomotion.Face(c, q.Toward); return ToilStatus.Succeeded; }
            return c.PathBlocked ? ToilStatus.Failed : ToilStatus.Running;
        }
        int idx = q.Line.IndexOf(c.Id);
        if (idx < 0)
        {
            if (qs.Deferred(c)) return ToilStatus.Failed; // 컴퓨터가 나중에 오라 했다
            if (qs.Join(c, q)) { GoServe(c, w); return ToilStatus.Running; }
            idx = q.Line.IndexOf(c.Id);
            if (idx < 0) return ToilStatus.Failed;
        }
        if (qs.TryServe(c, q)) { c.Path = null; GoServe(c, w); return ToilStatus.Running; }
        var slot = q.SlotCell(idx);
        if (slot != _slot)
        {
            _slot = slot;
            if (c.Cell != slot && !Locomotion.SetDestination(c, w, slot)) c.Path = null;
            else if (c.Path is { Count: > 0 }) c.Pose = Pose.Walking;
        }
        if (c.Path != null)
        {
            if (Locomotion.Step(c, w)) c.Path = null;
            else if (c.PathBlocked) { c.PathBlocked = false; c.Path = null; }
            return ToilStatus.Running;
        }
        if (c.Pose == Pose.Walking) c.Pose = Pose.Standing;
        Locomotion.Face(c, q.Spot.Center);
        if (!_arrivedOnce) { _arrivedOnce = true; qs.OnArrive(c, q); }
        else if (w.Tick % 25 == c.Id % 25 && qs.Waiting(c, q, Math.Max(0, q.Line.IndexOf(c.Id)))) return ToilStatus.Failed;
        return ToilStatus.Running;
    }

    public override void End(CrewMember c, World w)
    {
        if (_q != null) w.Coop.Queues.Leave(c, _q, _serving);
        c.Path = null;
        c.Destination = null;
        if (c.Pose == Pose.Walking) c.Pose = Pose.Standing;
    }
}

/// <summary>커피 한 잔: 커피 · 차를 즐기는 사람이 근무 전에 커피 머신 줄에 선다 (머신이 있을 때만).</summary>
public sealed class CoffeeRunActivity : Activity
{
    public override string Id => "coffeerun";
    public override string Label => "커피 한 잔";

    private static Furniture? Machine(World w, DistanceField dist, out Cell spot)
    {
        spot = default;
        Furniture? best = null; int bd = int.MaxValue;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.CoffeeMachine))
            foreach (var s in f.UseSpots)
            {
                int d = dist.Get(s);
                if (d < 0 || d >= bd) continue;
                best = f; bd = d; spot = s;
            }
        return best;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!(Life.Has(c, Habit.CoffeeAddict) || Life.Has(c, Habit.TeaLover)) || c.IsChild || Crisis.Acting(w)) return (0f, "—");
        if (w.Tick - w.Coop.Queues.LastCoffee(c) < SimTime.Hours(6)) return (0f, "—");
        float h = SimTime.HourOfDay(w.Tick);
        bool before = SimTime.InWindow(h + 0.5f, c.Schedule.WorkStart, c.Schedule.WorkLength) && !SimTime.InWindow(h, c.Schedule.WorkStart, c.Schedule.WorkLength);
        if (!before) return (0f, "—");
        if (Machine(w, dist, out _) == null) return (0f, "커피 머신이 없다");
        return (0.45f, Life.Has(c, Habit.TeaLover) ? "근무 전에 차 한 잔" : "근무 전에 커피 한 잔");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var m = Machine(w, dist, out var spot);
        if (m == null) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new QueueToil(QueueKind.Coffee, m, spot));
        toils.Add(new WaitToil(SimTime.Minutes(2), Pose.Working, m.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Coop.Queues.Coffee[cm.Id] = world.Tick;
            cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.03f);
            cm.Needs.Rest = MathF.Min(1f, cm.Needs.Rest + 0.04f);
            return true;
        }));
        return new Job(this, "커피 한 잔", toils) { LogText = Life.Has(c, Habit.TeaLover) ? "근무 전에 차를 내리러 간다" : "근무 전에 커피 한 잔", LogKind = LogKind.Life };
    }
}
