using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.3 정보 차이: 세계에서 일어난 일과 각자가 아는 것은 다르다.
//  · 깨진 컵: 식탁에 두고 간 컵이 배가 덜컹할 때 떨어져 깨진다 → 같은 방에서 본 사람(원인을 안다) · 옆방에서 소리만 들은 사람(확인하러 간다) ·
//    처음 와서 본 사람(메신저에 알린다) · 주인(본 적이 없으니 근처에 있던 사람을 의심한다) → 몰아붙임 · 억울한 사람의 하소연 →
//    본 사람이 해명 → 주인이 사과 (관계 · 다툼 · 자리 · 말한 사람의 신용이 함께 움직인다). 본 사람이 없으면 주 컴퓨터가 그 시각 기록을 댄다.
//  · 물건: 주인은 마지막으로 본 곳만 안다 — 남이 치우면 모른다. 찾을 때는 마지막에 둔 곳부터 → 방 안 → 사물함 → 메신저에 묻는다 → 본 사람이 답한다.
//  · 신용: 틀린 말을 한 사람의 말은 다음에 덜 믿는다 (몰아붙였다가 틀린 주인 · 부풀린 소문).

public sealed class CupCase
{
    public int Id { get; init; }
    public int Cup { get; init; }
    public int Owner { get; init; }
    public long Tick { get; init; }
    public int RoomId { get; init; }
    public Cell At { get; init; }
    public string Cause { get; init; } = "";
    public List<int> Saw { get; } = new();
    public List<int> Heard { get; } = new();
    public List<int> Knows { get; } = new();
    public int Finder { get; set; } = -1;
    public int Suspect { get; set; } = -1;
    public long Accused { get; set; } = -1;
    public int ExplainedBy { get; set; } = -1;
    public long Explained { get; set; } = -1;
    public long Apologized { get; set; } = -1;
    public bool Swept { get; set; }
    public bool OwnerKnows { get; set; }
    public bool OwnerTruth { get; set; }
    public bool Asked { get; set; }
    public long ComputerSaid { get; set; } = -1;
}

public sealed class TableCup
{
    public int Cup { get; init; }
    public Cell Table { get; init; }
    public Cell Spot { get; init; }
    public int User { get; init; }
    public long Since { get; init; }
    public bool Left { get; set; }
}

public sealed partial class InfoSystem
{
    public List<CupCase> Cases { get; } = new();
    public SortedDictionary<int, TableCup> OnTable { get; } = new();
    public List<(long tick, int room, float g, string why)> Jolts { get; } = new();
    private readonly SortedDictionary<int, (Cell at, long t)> _seen = new();
    private readonly SortedDictionary<int, (int who, Cell at, long t)> _spot = new();
    private readonly SortedDictionary<int, float> _cred = new();
    private int _nextCase = 1;
    private float _shakeWas;

    public CupCase? Case(int id) => Cases.FirstOrDefault(k => k.Id == id);

    // ───────────────────────────── 신용 ─────────────────────────────

    /// <summary>이 사람 말을 얼마나 믿나 (0~1): 원칙파 · 침착한 사람은 높게 시작하고, 틀린 말을 하면 깎인다.</summary>
    public float Cred(CrewMember c)
    {
        if (_cred.TryGetValue(c.Id, out var v)) return v;
        float b = 0.55f + (c.Value == CrewValue.Rules ? 0.1f : 0f) + 0.1f * (c.Traits.Calm - 0.5f) - (Life.Has(c, Habit.Prankster) || Life.Has(c, Habit.Joker) ? 0.08f : 0f);
        return Math.Clamp(b, 0.2f, 0.9f);
    }

    private void CredAdd(CrewMember c, float d) { _cred[c.Id] = Math.Clamp(Cred(c) + d, 0.05f, 0.95f); if (d < 0f) Stats.CredDrops++; }

    // ───────────────────────────── 식탁의 컵 ─────────────────────────────

    private Belonging? MugOf(CrewMember c) =>
        _w.Belongings.All.FirstOrDefault(b => b.Owner == c.Id && b.Kind == BelongingKind.Mug && b.Usable && b.Holder < 0 && b.BorrowedBy < 0);

    /// <summary>식당에서 먹는 사람은 제 컵을 식탁에 둔다 · 일어날 때 챙기거나 두고 간다 (덜렁이는 두고 간다).</summary>
    private void ObserveCupsInUse()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Pose != Pose.Sitting || c.Job?.Activity is not EatActivity || c.Room is not { Type: RoomType.Mess } room) continue;
            if (MugOf(c) is not Belonging cup || OnTable.ContainsKey(cup.Id)) continue;
            var table = room.Furniture.Where(f => f.Type == FurnitureType.Table && f.Cells.Count > 0).OrderBy(f => (f.Center - c.Position).LengthSquared()).ThenBy(f => f.Id).FirstOrDefault();
            if (table == null) continue;
            var tcell = table.Cells.OrderBy(x => (x.Center - c.Position).LengthSquared()).First();
            PutOnTable(c, cup, tcell, c.Cell);
        }
        foreach (var tc in OnTable.Values.ToList())
        {
            var cup = w.Belongings.Get(tc.Cup);
            if (cup == null || cup.Holder >= 0 || cup.At != tc.Spot || !cup.Usable) { OnTable.Remove(tc.Cup); continue; }
            if (tc.Left) continue;
            var u = CrewOf(tc.User);
            if (u != null && !u.Dead && u.Pose == Pose.Sitting && u.Cell == tc.Spot && u.Job?.Activity is EatActivity) continue;
            float leave = 0.22f + (u != null && Life.Has(u, Habit.Messy) ? 0.5f : 0f) + (u != null && Life.Has(u, Habit.Forgetful) ? 0.25f : 0f)
                          - (u != null && Life.Has(u, Habit.NeatFreak) ? 0.2f : 0f) + (u != null && Life.Has(u, Habit.CoffeeAddict) ? 0.1f : 0f);
            if (R.Chance(leave)) { tc.Left = true; if (u != null) MarkLog.Add(cup.Marks, w.Tick, $"{Ko.IGa(u.Name)} 식탁에 두고 갔다"); }
            else { OnTable.Remove(tc.Cup); cup.At = null; }
        }
    }

    /// <summary>컵을 식탁에 놓는다 (시험에서도 쓴다).</summary>
    public TableCup PutOnTable(CrewMember c, Belonging cup, Cell table, Cell spot, bool left = false)
    {
        var tc = new TableCup { Cup = cup.Id, Table = table, Spot = spot, User = c.Id, Since = _w.Tick, Left = left };
        OnTable[cup.Id] = tc;
        cup.At = spot;
        if (cup.Owner == c.Id) _seen[cup.Id] = (spot, _w.Tick);
        return tc;
    }

    /// <summary>화면용: 이 물건은 InfoSystem 쪽 그림이 그린다 (벽 사진 · 식탁 컵 · 바닥의 깨진 컵).</summary>
    public bool Drawn(Belonging b) => b.Kind switch
    {
        BelongingKind.Photo => Hung(b),
        BelongingKind.Mug => true, // 컵은 저마다 모양 · 무늬가 달라 따로 그린다 (식탁 · 바닥 · 조각)
        BelongingKind.Artwork => Made(b) != null,
        _ => false,
    };

    /// <summary>못 끝낸 일로 만든 것 (완성한 모형 · 늦게 건넨 선물) — 화면이 따로 그린다.</summary>
    public Todo? Made(Belonging b)
    {
        foreach (var t in Todos) if (t.Done && t.Item == b.Id && t.Kind is TodoKind.Model or TodoKind.Gift) return t;
        return null;
    }

    /// <summary>금빛으로 이어 붙인 컵.</summary>
    public bool Mended(Belonging b) => b.Kind == BelongingKind.Mug && Todos.Any(t => t.Done && t.Kind == TodoKind.MendCup && t.Item == b.Id);

    /// <summary>화면용: 식탁 위에 놓였나 (어디에).</summary>
    public Cell? TableOf(Belonging b) => OnTable.TryGetValue(b.Id, out var tc) && b.At == tc.Spot ? tc.Table : null;

    // ───────────────────────────── 덜컹 ─────────────────────────────

    private void RollJolt()
    {
        if (!R.Chance(0.05f)) return;
        Jolt(null, R.Range(0.35f, 0.8f), R.Chance(0.6f) ? "자세 제어 분사" : "작은 파편이 선체를 때렸다");
    }

    /// <summary>우주 대재난의 흔들림도 식탁 위 컵을 떨어뜨린다.</summary>
    private void WatchCups()
    {
        float shake = _w.Cosmic.ShakeNow;
        if (shake > 0.25f && _shakeWas <= 0.25f) Jolt(null, MathF.Min(1f, shake), "배가 크게 흔들렸다");
        _shakeWas = shake;
    }

    /// <summary>배가 덜컹한다: 식탁 끝 컵이 떨어진다 (가속 · 마찰 — 덜렁이가 둔 컵은 가장자리에 있다).</summary>
    public int Jolt(Room? room, float g, string why, bool force = false)
    {
        var w = _w;
        Stats.Jolts++;
        Jolts.Add((w.Tick, room?.Id ?? -1, g, why));
        if (Jolts.Count > 30) Jolts.RemoveAt(0);
        int n = 0;
        foreach (var tc in OnTable.Values.ToList())
        {
            var r = w.Ship.RoomAt(tc.Table);
            if (r == null || room != null && r != room) continue;
            var owner = w.Belongings.Get(tc.Cup) is Belonging b ? CrewOf(b.Owner) : null;
            float p = g * (0.4f + (owner != null && Life.Has(owner, Habit.Messy) ? 0.25f : 0f));
            if (!force && !R.Chance(p)) continue;
            if (Fall(tc, why, force)) n++;
        }
        // 덜컹을 느낀 사람이 메신저에 한마디 (농담꾼)
        if (!force && R.Chance(0.5f))
        {
            var j = w.Crew.Where(c => Adult(c) && c.IsAwake && !c.Outside && (Life.Has(c, Habit.Joker) || Life.Has(c, Habit.Talker)) && Chat.CannotRead(c) == null).OrderBy(c => c.Id).FirstOrDefault();
            if (j != null) { Chat.Post(j, ChatKind.Joke, ShipChat.Voice(j, R.Chance(0.5f) ? "방금 덜컹한 거 나만 느낌?" : "누가 배 운전 이렇게 해", "방금 배가 덜컹했죠?")); Stats.Jokes++; }
        }
        return n;
    }

    private bool Fall(TableCup tc, string why, bool force)
    {
        var w = _w;
        if (w.Belongings.Get(tc.Cup) is not Belonging cup) return false;
        OnTable.Remove(tc.Cup);
        var at = tc.Spot;
        if (!force && !R.Chance(0.85f))
        {
            cup.At = at;
            MarkLog.Add(cup.Marks, w.Tick, $"{why} — 식탁에서 떨어졌지만 멀쩡했다");
            return false;
        }
        Break(cup, at, $"배가 덜컹할 때({why}) 식탁에서 떨어져 깨졌다");
        return true;
    }

    /// <summary>컵이 깨진다: 조각이 바닥에 (밟으면 베인다 · 손보기가 쓸어 낸다) · 본 사람 · 소리만 들은 사람.</summary>
    public CupCase Break(Belonging cup, Cell at, string cause)
    {
        var w = _w;
        cup.Condition = 0.05f;
        cup.At = at;
        MarkLog.Add(cup.Marks, w.Tick, cause);
        w.Body.RaiseMark(at, CellMark.Glass, 0.6f, $"깨진 {cup.Name} 조각");
        var room = w.Ship.RoomAt(at);
        var k = new CupCase { Id = _nextCase++, Cup = cup.Id, Owner = cup.Owner, Tick = w.Tick, RoomId = room?.Id ?? -1, At = at, Cause = cause };
        Cases.Add(k);
        if (Cases.Count > 30) Cases.RemoveAt(0);
        Stats.Cups++;
        if (room == null) return k;
        bool dark = room.Dark;
        bool said = false;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Down || c.Outside || !c.IsAwake || c.Room != room) continue;
            if (dark) { k.Heard.Add(c.Id); continue; }
            k.Saw.Add(c.Id);
            k.Knows.Add(c.Id);
            Stats.Saw++;
            w.Brain2.Beliefs.Learn(c, Topic.Thing, cup.Id, 3, BeliefSource.Seen, 1f);
            if (!said) { c.Say(w, Persona.Say(c, "어어 — 컵이!")); said = true; }
            if (c.Id == cup.Owner) { k.OwnerKnows = true; k.OwnerTruth = true; }
        }
        // 옆방에서는 소리만 들린다 → 확인하러 간다
        foreach (var d in w.Ship.Doors)
        {
            Room? other = d.RoomA == room ? d.RoomB : d.RoomB == room ? d.RoomA : null;
            if (other == null || other == room) continue;
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Down || c.Outside || !c.IsAwake || c.Room != other || k.Heard.Contains(c.Id) || c.IsChild && c.Age < 6f) continue;
                k.Heard.Add(c.Id);
            }
        }
        foreach (var id in k.Heard)
        {
            if (CrewOf(id) is not CrewMember c) continue;
            Stats.Heard++;
            float s = 0.5f + 0.12f * c.Traits.Bravery + (Life.Has(c, Habit.Worrier) ? 0.08f : 0f) + (Life.Has(c, Habit.NeatFreak) ? 0.05f : 0f) - (Life.Has(c, Habit.Loner) ? 0.08f : 0f);
            if (c.Job?.Urgent == true || c.Job?.Order != null) s -= 0.2f;
            if (s < 0.2f) continue;
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{room.Name} 쪽에서 쨍그랑 소리를 들었다");
            AddIntent(InfoDo.CheckSound, c, -1, k.Id, at, s, $"{room.Name} 쪽에서 쨍그랑 소리", 1f);
        }
        w.Log.Add(w.Tick, LogKind.Life, $"{room.Name} 식탁에서 {Ko.IGa(cup.Name)} 떨어져 깨졌다 ({cause.Replace("식탁에서 떨어져 깨졌다", "").Trim()}) — 본 사람 {k.Saw.Count} · 소리만 들은 사람 {k.Heard.Count}");
        return k;
    }

    /// <summary>방에 들어와 조각을 본 사람: 처음 본 사람은 알리고 · 주인은 누군가를 의심한다 · 쓸어 냈으면 조각은 없다.</summary>
    private void ObserveCases()
    {
        var w = _w;
        long now = w.Tick;
        foreach (var k in Cases)
        {
            if (now - k.Tick > SimTime.TicksPerDay * 2) continue;
            if (!k.Swept && w.Body.Mark(k.At, CellMark.Glass) < 0.05f)
            {
                k.Swept = true;
                if (w.Belongings.Get(k.Cup) is Belonging cup && cup.At == k.At) cup.At = null; // 쓸어 낸 사람이 조각을 봉투에 담아 주인 사물함에
            }
            var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == k.RoomId);
            if (room != null && !k.Swept)
                foreach (var c in w.Crew)
                {
                    if (c.Dead || !c.IsAwake || c.Room != room || k.Knows.Contains(c.Id)) continue;
                    if (room.Dark) continue;
                    k.Knows.Add(c.Id);
                    Intents.RemoveAll(i => i.Crew == c.Id && i.Do == InfoDo.CheckSound && i.Thing == k.Id);
                    if (c.Id == k.Owner) { OwnerLearns(k, BeliefSource.Seen, -1); continue; }
                    w.Brain2.Beliefs.Learn(c, Topic.Thing, k.Cup, 2, BeliefSource.Seen, 0.9f);
                    if (k.Finder < 0 && !k.Saw.Contains(c.Id)) Find(k, c);
                }
            // 아무도 해명하지 않으면 주 컴퓨터가 그 시각 기록을 댄다
            if (k.Accused >= 0 && k.Explained < 0 && k.ComputerSaid < 0 && now - k.Accused > SimTime.Minutes(90) && w.Automation.Present && w.Automation.CoreOnline)
            {
                var j = Jolts.LastOrDefault(x => Math.Abs(x.tick - k.Tick) < SimTime.Minutes(2));
                if (j.why != null)
                {
                    k.ComputerSaid = now;
                    Chat.Post(null, ChatKind.Computer, $"{SimTime.Clock(k.Tick)} 배가 덜컹했습니다({j.why} · {j.g:0.0}G). {room?.Name ?? "식당"} 식탁 위 물건이 떨어졌을 수 있습니다", @case: k.Id);
                }
            }
        }
    }

    private void Find(CupCase k, CrewMember c)
    {
        var w = _w;
        k.Finder = c.Id;
        Stats.Found++;
        var cup = w.Belongings.Get(k.Cup);
        string whose = cup != null && CrewOf(cup.Owner) is CrewMember o ? $"{o.Name}의 " : "";
        c.Say(w, Persona.Say(c, $"어, {whose}컵이 깨져 있네"));
        w.Log.Add(w.Tick, LogKind.Life, $"바닥에 깨진 {whose}컵 조각을 처음 발견했다", c.Id);
        if (c.Traits.Sociability >= 0.25f && !Life.Has(c, Habit.Loner) && Chat.CannotRead(c) == null)
        {
            var room = w.Ship.RoomAt(k.At);
            Chat.Post(c, ChatKind.Notice, ShipChat.Voice(c, $"{room?.Name ?? "식당"} 바닥에 {whose}컵 깨져 있음. 조각 조심", $"{room?.Name ?? "식당"} 바닥에 {whose}컵이 깨져 있습니다. 조각 조심하세요"), @case: k.Id, about: k.Owner);
        }
    }

    /// <summary>주인이 컵이 깨진 걸 안다: 본 적이 없으면 근처에 있던 사람을 의심한다 (성미 · 사이 · 그 사람의 신용).</summary>
    internal void OwnerLearns(CupCase k, BeliefSource src, int from)
    {
        var w = _w;
        if (k.OwnerKnows) return;
        k.OwnerKnows = true;
        var owner = CrewOf(k.Owner);
        var cup = w.Belongings.Get(k.Cup);
        if (owner == null || owner.Dead || cup == null) return;
        w.Brain2.Emotions.Feel(owner, Feeling.Sadness, 0.12f, $"{cup.Name}이(가) 깨졌다");
        if (!Todos.Any(t => t.Kind == TodoKind.MendCup && t.Item == cup.Id && !t.Done)) AddTodo(owner, TodoKind.MendCup, cup.Name, 0f, item: cup.Id);
        if (k.OwnerTruth) return;
        // 의심할 사람: 알려 준 발견자 · 지금 그 방에 있는 사람 (주인이 아는 범위에서)
        var cands = new List<CrewMember>();
        if (k.Finder >= 0 && CrewOf(k.Finder) is CrewMember f && (from == f.Id || f.Room == owner.Room)) cands.Add(f);
        if (src == BeliefSource.Seen)
            foreach (var o in w.Crew) if (o != owner && !o.Dead && o.IsAwake && o.Room == owner.Room && !cands.Contains(o) && !o.IsChild) cands.Add(o);
        float temper = (Life.Has(owner, Habit.ShortTempered) || Life.Has(owner, Habit.Grumbler) || Life.Has(owner, Habit.Pessimist) ? 0.2f : 0f)
                       - (Life.Has(owner, Habit.Patient) || Life.Has(owner, Habit.Optimist) || Life.Has(owner, Habit.Generous) ? 0.2f : 0f);
        CrewMember? sus = null;
        float best = float.MaxValue;
        foreach (var o in cands)
        {
            float key = owner.AffinityTo(o) + 0.3f * Cred(o) - (o.Id == k.Finder ? 0.25f : 0f);
            if (key < best) { best = key; sus = o; }
        }
        float s = sus == null ? 0f : 0.45f + temper - 0.6f * owner.AffinityTo(sus) + (sus.Id == k.Finder ? 0.1f : 0f);
        if (sus != null && s > 0.5f)
        {
            k.Suspect = sus.Id;
            w.Brain2.Beliefs.Learn(owner, Topic.Thing, cup.Id, 2, src == BeliefSource.Seen ? BeliefSource.Guess : src, 0.6f, from, sus.Id + 1);
            w.Brain2.Emotions.Feel(owner, Feeling.Anger, 0.15f, $"{cup.Name}이(가) 깨졌다", sus);
            AddIntent(InfoDo.Confront, owner, sus.Id, k.Id, default, 0.5f + 0.1f * temper, $"{Ko.IGa(sus.Name)} 내 컵을 깬 것 같다", 12f);
            w.Log.Add(w.Tick, LogKind.Life, $"깨진 {cup.Name}을(를) 보고 {Ko.EulReul(sus.Name)} 의심한다 (본 적은 없다)", owner.Id);
        }
        else
        {
            w.Brain2.Beliefs.Learn(owner, Topic.Thing, cup.Id, 2, src, 0.8f, from);
            if (Chat.CannotRead(owner) == null)
            {
                k.Asked = true;
                Stats.Asked++;
                Chat.Post(owner, ChatKind.Ask, ShipChat.Voice(owner, "혹시 내 컵 어쩌다 깨졌는지 본 사람?", "혹시 제 컵이 어쩌다 깨졌는지 보신 분 계세요?"), @case: k.Id);
            }
        }
    }

    /// <summary>몰아붙인다 (InfoActivity가 그 사람 곁에 가서): 억울한 사람은 부인하고 서운해하고, 곁에서 본 사람이 있으면 바로 해명한다.</summary>
    internal void Confront(CrewMember owner, CrewMember sus, CupCase k)
    {
        var w = _w;
        if (k.Explained >= 0 || k.Accused >= 0) return;
        k.Accused = w.Tick;
        Stats.Accused++;
        Stats.Denied++;
        var cup = w.Belongings.Get(k.Cup);
        string cn = cup?.Name ?? "컵";
        owner.Say(w, Persona.Say(owner, $"내 컵 깬 거 너지? 말이라도 하지"));
        sus.Say(w, Persona.Say(sus, sus.Id == k.Finder ? "아니야 — 소리 듣고 가 본 것뿐이야" : "난 아니야. 그때 거기 없었어"));
        var mem = w.Relations.Remember(sus, owner, RelationReason.BlamedMe, $"{cn}을(를) 내가 깼다고 몰아붙였다");
        mem.Truth = k.Cause;
        sus.ChangeAffinity(owner, -0.1f);
        owner.ChangeAffinity(sus, -0.05f);
        w.Brain2.Emotions.Feel(sus, Feeling.Anger, 0.18f, "억울하게 의심받았다", owner);
        OnQuarrel(owner, sus, "컵 일");
        Life.Diary(w, owner, Persona.Say(owner, $"{Ko.IGa(sus.Name)} 내 컵을 깬 것 같다. 아니라는데"));
        Life.Diary(w, sus, Persona.Say(sus, $"{Ko.IGa(owner.Name)} 컵 깬 사람으로 나를 몰았다. 억울하다"));
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.EulReul(sus.Name)} 찾아가 {cn} 일로 몰아붙였다 — \"아니야\"", owner.Id);
        // 곁에 있던 사람: 본 사람은 바로 말한다 · 아닌 사람은 들은 대로 (주인의 신용만큼)
        foreach (var o in w.Crew)
        {
            if (o == owner || o == sus || o.Dead || !o.IsAwake || o.Room != owner.Room) continue;
            if (k.Saw.Contains(o.Id) && k.Explained < 0) { Explain(o, owner, k, true); continue; }
            w.Brain2.Beliefs.Learn(o, Topic.Thing, k.Cup, 2, BeliefSource.Overheard, Cred(owner) * 0.8f, owner.Id, sus.Id + 1);
        }
        // 억울한 사람의 하소연 (메신저) — 본 사람이 읽으면 나선다
        if (k.Explained < 0 && !Life.Has(sus, Habit.Loner) && Chat.CannotRead(sus) == null)
        {
            Stats.Vented++;
            Chat.Post(sus, ChatKind.Gripe, ShipChat.Voice(sus, $"컵 안 깼는데 의심받음. 누가 봤으면 말 좀 해 줘", $"제가 컵을 깼다는 오해를 받고 있습니다. 보신 분 계시면 말씀해 주세요"), @case: k.Id, about: owner.Id);
        }
    }

    /// <summary>본 사람이 해명한다 (곁에서 · 찾아가서 · 메신저로).</summary>
    internal void Explain(CrewMember witness, CrewMember owner, CupCase k, bool inPerson)
    {
        var w = _w;
        if (k.Explained >= 0) return;
        k.Explained = w.Tick;
        k.ExplainedBy = witness.Id;
        Stats.Explained++;
        var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == k.RoomId);
        if (inPerson)
        {
            witness.Say(w, Persona.Say(witness, "그 컵, 배가 덜컹할 때 식탁에서 떨어진 거야. 내가 봤어"));
            Accept(owner, k, witness, BeliefSource.Told);
        }
        else Chat.Post(witness, ChatKind.Explain, ShipChat.Voice(witness, $"그 컵 아까 배 덜컹할 때 떨어진 거야. 내가 {room?.Name ?? "거기"}에 있었어", $"그 컵은 아까 배가 덜컹할 때 떨어졌습니다. 제가 {room?.Name ?? "거기"}에 있었어요"), @case: k.Id, about: owner.Id);
        CredAdd(witness, 0.04f);
        if (k.Suspect >= 0 && CrewOf(k.Suspect) is CrewMember s && s != witness)
        {
            w.Relations.Remember(s, witness, RelationReason.ClearedMyName, "컵 일로 오해받을 때 본 대로 말해 줬다");
            s.ChangeAffinity(witness, 0.05f);
        }
        w.Log.Add(w.Tick, LogKind.Life, $"{owner.Name}에게 컵이 어떻게 깨졌는지 본 대로 말했다{(inPerson ? "" : " (메신저)")}", witness.Id);
    }

    /// <summary>주인이 원인을 받아들인다 (말한 사람의 신용 · 관계) → 의심했던 사람에게 사과하러 간다.</summary>
    internal void Accept(CrewMember owner, CupCase k, CrewMember? by, BeliefSource src)
    {
        var w = _w;
        if (k.OwnerTruth) return;
        float conf = by != null ? 0.5f + 0.4f * Cred(by) + 0.2f * owner.AffinityTo(by) : w.Automation.Trusts.Of(owner);
        w.Brain2.Beliefs.Learn(owner, Topic.Thing, k.Cup, 3, src, conf, by?.Id ?? -1);
        if (conf < 0.35f) return;
        k.OwnerTruth = true;
        if (by == null) { k.ExplainedBy = -2; if (k.Explained < 0) k.Explained = w.Tick; Stats.ComputerExplained++; }
        owner.Say(w, Persona.Say(owner, "…그랬구나"));
        if (k.Accused >= 0 && CrewOf(k.Suspect) is CrewMember s && !s.Dead)
        {
            w.Brain2.Emotions.Feel(owner, Feeling.Shame, 0.2f, $"{Ko.EulReul(s.Name)} 괜히 의심했다", s);
            float urge = 0.55f + (owner.Value == CrewValue.People ? 0.1f : 0f) + 0.1f * owner.Traits.Calm - (Life.Has(owner, Habit.ShortTempered) ? 0.08f : 0f);
            AddIntent(InfoDo.Apologize, owner, s.Id, k.Id, default, urge, $"{Ko.EulReul(s.Name)} 괜히 의심했다", 24f);
        }
        else if (k.Suspect >= 0) k.Suspect = -1; // 마음속으로만 의심했다 — 말하기 전에 풀렸다
        if (by != null) owner.ChangeAffinity(by, 0.03f);
    }

    /// <summary>사과 (InfoActivity가 그 사람 곁에 가서): 오해가 풀리고 다툼이 끝난다 · 몰아붙인 사람의 신용은 깎인다.</summary>
    internal void Apologize(CrewMember owner, CrewMember s, CupCase k)
    {
        var w = _w;
        if (k.Apologized >= 0) return;
        k.Apologized = w.Tick;
        Stats.Apologized++;
        bool kind = Life.Has(s, Habit.Patient) || Life.Has(s, Habit.Generous) || Life.Has(s, Habit.Optimist) || s.Traits.Calm > 0.6f;
        owner.Say(w, Persona.Say(owner, "아까는 미안. 배가 흔들려서 떨어진 거래"));
        s.Say(w, Persona.Say(s, kind ? "괜찮아, 그럴 수도 있지" : "…알았어. 다음엔 먼저 물어봐"));
        w.Relations.Remember(s, owner, RelationReason.Apologized, "컵 일로 몰아붙인 걸 먼저 사과했다");
        foreach (var m in w.Relations.Of(s, owner).Where(m => m.Reason == RelationReason.BlamedMe && !m.Revealed)) { m.Revealed = true; m.Weight *= 0.5f; }
        s.ChangeAffinity(owner, kind ? 0.12f : 0.07f);
        owner.ChangeAffinity(s, 0.05f);
        w.Brain2.Emotions.Feel(s, Feeling.Anger, -0.15f, "");
        foreach (var sp in Spats) if ((sp.A == owner.Id && sp.B == s.Id || sp.A == s.Id && sp.B == owner.Id) && sp.Why == "컵 일") sp.Made = true;
        CredAdd(owner, -0.12f);
        Life.Diary(w, owner, Persona.Say(owner, $"괜히 {Ko.EulReul(s.Name)} 의심했다. 사과했다"));
        Life.Diary(w, s, Persona.Say(s, $"{Ko.IGa(owner.Name)} 컵 일로 사과했다"));
        w.Log.Add(w.Tick, LogKind.Life, $"{s.Name}에게 컵 일로 의심한 걸 사과했다", owner.Id);
        if (Chat.All.Any(m => m.Case == k.Id && m.Kind == ChatKind.Gripe) && Chat.CannotRead(owner) == null)
            Chat.Post(owner, ChatKind.Sorry, ShipChat.Voice(owner, $"컵 일은 내 오해였어. {s.Name} 미안", $"컵 일은 제 오해였습니다. {s.Name} 님께 사과드립니다"), @case: k.Id, about: s.Id);
    }

    // ───────────────────────────── 물건: 마지막에 본 곳 ─────────────────────────────

    /// <summary>주인이 마지막으로 본 곳 (모르면 사물함).</summary>
    public Cell Believed(Belonging b) => _seen.TryGetValue(b.Id, out var v) ? v.at : _w.Belongings.Home(b);
    public (int who, Cell at, long t)? Spotted(Belonging b) => _spot.TryGetValue(b.Id, out var v) ? v : null;

    /// <summary>5분마다: 같은 방에서 눈에 띈 물건 — 주인은 "거기 있다"고 알고, 남은 "봤다"고 안다.</summary>
    private void ObserveThings()
    {
        var w = _w;
        long now = w.Tick;
        foreach (var b in w.Belongings.All)
        {
            if (b.Owner < 0 || CrewOf(b.Owner) is not CrewMember owner || owner.Dead) continue;
            if (b.Holder >= 0)
            {
                if (b.Holder == owner.Id) _seen[b.Id] = (owner.Cell, now);
                else if (CrewOf(b.Holder) is CrewMember h) _spot[b.Id] = (h.Id, h.Cell, now);
                continue;
            }
            if (b.At is Cell at)
            {
                var room = w.Ship.RoomAt(at);
                if (room == null || room.Dark) continue;
                if (owner.IsAwake && owner.Room == room) _seen[b.Id] = (at, now);
                foreach (var o in w.Crew)
                    if (o != owner && !o.Dead && o.IsAwake && o.Room == room) { _spot[b.Id] = (o.Id, at, now); break; }
            }
            else if (owner.IsAwake && owner.Bed is Furniture bed && owner.Room == bed.Room)
                _seen[b.Id] = (w.Belongings.Home(b), now);
        }
    }

    private static bool Near(Cell a, Cell b) => Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;

    /// <summary>한 시간마다: 쉬는 사람이 문득 제 물건을 찾는다 — 마지막에 둔 곳에 없으면 찾으러 나선다.</summary>
    private void Wants()
    {
        var w = _w;
        float hour = SimTime.HourOfDay(w.Tick);
        foreach (var c in w.Crew)
        {
            if (!Adult(c) || !c.CanAct || !c.IsAwake || c.Outside || c.Job?.Urgent == true || !R.Chance(0.1f)) continue;
            if (SimTime.InWindow(hour, c.Schedule.SleepStart, c.Schedule.SleepLength) || SimTime.InWindow(hour, c.Schedule.WorkStart, c.Schedule.WorkLength)) continue;
            var mine = w.Belongings.All.Where(b => b.Owner == c.Id && b.Usable && b.Kind is BelongingKind.Book or BelongingKind.Journal or BelongingKind.Sketchbook or BelongingKind.Instrument
                or BelongingKind.Cards or BelongingKind.Camera or BelongingKind.Toolset or BelongingKind.Knitting or BelongingKind.Puzzle or BelongingKind.Headphones or BelongingKind.ChessSet).ToList();
            if (mine.Count == 0) continue;
            var b = mine[R.Range(0, mine.Count)];
            Want(c, b);
        }
    }

    /// <summary>그 물건이 필요하다: 믿는 곳과 실제가 다르면 찾기 시작한다 (시험에서도 쓴다).</summary>
    public bool Want(CrewMember c, Belonging b)
    {
        var w = _w;
        Stats.Wants++;
        if (b.Holder == c.Id) return false;
        var believed = Believed(b);
        var real = w.Belongings.CellOf(b);
        if (Near(believed, real) && b.BorrowedBy < 0) return false;
        if (Intents.Any(i => i.Do == InfoDo.Search && i.Crew == c.Id && i.Thing == b.Id)) return false;
        AddIntent(InfoDo.Search, c, -1, b.Id, believed, 0.4f, $"{Ko.EulReul(b.Name)} 찾는다", 6f);
        return true;
    }

    /// <summary>찾았다 (그 자리 · 근처 · 사물함 · 들은 곳).</summary>
    internal void Found(CrewMember c, Belonging b, string how, bool there)
    {
        var w = _w;
        _seen[b.Id] = (w.Belongings.CellOf(b), w.Tick);
        if (there) Stats.FoundThere++; else Stats.FoundElsewhere++;
        w.Brain2.Beliefs.Learn(c, Topic.Thing, b.Id, 1, BeliefSource.Seen, 1f);
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.EulReul(b.Name)} 찾았다 — {how}", c.Id);
        Intents.RemoveAll(i => i.Crew == c.Id && i.Do == InfoDo.Search && i.Thing == b.Id);
    }

    /// <summary>사물함에서 찾았다: 누가 치워 줬나 묻는다 (치운 사람이 읽으면 답한다).</summary>
    internal void FoundTidied(CrewMember c, Belonging b)
    {
        var w = _w;
        Found(c, b, "사물함에 들어 있었다. 누가 치워 줬나 보다", false);
        Stats.Tidied++;
        if (Chat.CannotRead(c) == null)
        {
            bool messy = Life.Has(c, Habit.Messy) || Life.Has(c, Habit.ShortTempered);
            Chat.Post(c, ChatKind.Ask, messy ? ShipChat.Voice(c, $"내 {b.Name} 누가 치웠어? 하던 거였는데", $"제 {b.Name} 누가 치우셨나요? 쓰던 중이었습니다")
                : ShipChat.Voice(c, $"내 {b.Name} 사물함에 넣어 준 사람? 고마워", $"제 {b.Name} 사물함에 넣어 주신 분 고맙습니다"), thing: b.Id);
        }
    }

    /// <summary>못 찾았다: 메신저에 묻는다 — 본 사람이 읽으면 답한다.</summary>
    internal void AskWhere(CrewMember c, Belonging b)
    {
        var w = _w;
        w.Brain2.Beliefs.Learn(c, Topic.Thing, b.Id, 4, BeliefSource.Seen, 0.9f);
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.EulReul(b.Name)} 못 찾았다 — 메신저에 물어본다", c.Id);
        Stats.AskedWhere++;
        if (Chat.CannotRead(c) == null)
            Chat.Post(c, ChatKind.Ask, ShipChat.Voice(c, $"내 {b.Name} 본 사람?", $"혹시 제 {b.Name} 보신 분 계세요?"), thing: b.Id);
    }

    // ───────────────────────────── 메신저를 읽었다 ─────────────────────────────

    /// <summary>읽은 글이 그 사람을 움직인다 (안 읽은 사람은 모른다).</summary>
    internal void OnRead(CrewMember c, ChatMsg m)
    {
        var w = _w;
        var author = m.Author >= 0 ? CrewOf(m.Author) : null;
        var k = m.Case >= 0 ? Case(m.Case) : null;
        switch (m.Kind)
        {
            case ChatKind.Joke:
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.01f);
                if (author != null && !Life.Has(c, Habit.Serious)) c.ChangeAffinity(author, 0.005f);
                break;
            case ChatKind.Photo:
                if (Photo(m.Photo) is PhotoInfo p)
                {
                    if (p.People.Contains(c.Id)) w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.05f, p.Caption);
                    if (author != null) c.ChangeAffinity(author, 0.008f);
                }
                break;
            case ChatKind.Thanks:
                if (m.About == c.Id) { w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.06f, "고맙다는 말"); if (author != null) c.ChangeAffinity(author, 0.02f); }
                break;
            case ChatKind.Talk when m.About >= 0 && author != null && m.About == author.Id:
                c.ChangeAffinity(author, 0.015f); // 물자를 내놓은 사람
                break;
            case ChatKind.Gripe when m.Thing == -2:
                // 설거지 불만: 장부에 이름이 없는 사람은 찔린다
                if (Ate(c) >= 4 && Share(c, LedgerKind.Dishes) == 0)
                {
                    _guilt[c.Id] = w.Tick + SimTime.TicksPerDay;
                    Stats.Guilty++;
                    w.Brain2.Emotions.Feel(c, Feeling.Shame, 0.1f, "설거지 장부에 내 이름이 없다");
                    c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1);
                }
                break;
            case ChatKind.Notice when k != null:
                if (!k.Knows.Contains(c.Id)) k.Knows.Add(c.Id);
                if (c.Id == k.Owner) OwnerLearns(k, BeliefSource.Told, m.Author);
                else w.Brain2.Beliefs.Learn(c, Topic.Thing, k.Cup, 2, BeliefSource.Told, author != null ? Cred(author) : 0.6f, m.Author);
                break;
            case ChatKind.Ask when k != null:
            case ChatKind.Gripe when k != null:
            case ChatKind.Accuse when k != null:
                // 본 사람은 나선다: 주인이 묻거나 · 억울한 사람이 하소연하면
                if (k.Saw.Contains(c.Id) && k.Explained < 0 && CrewOf(k.Owner) is CrewMember owner && owner != c)
                {
                    if (Life.Has(c, Habit.Loner) || m.Kind == ChatKind.Ask) Explain(c, owner, k, false);
                    else AddIntent(InfoDo.Explain, c, owner.Id, k.Id, default, 0.55f + 0.1f * c.Traits.Diligence, $"{owner.Name}의 컵 — 내가 봤다", 12f);
                }
                break;
            case ChatKind.Explain when k != null:
            case ChatKind.Computer when k != null:
                if (c.Id == k.Owner) Accept(c, k, author, author != null ? BeliefSource.Told : BeliefSource.Computer);
                else if (w.Brain2.Beliefs.Get(c, Topic.Thing, k.Cup) != null) w.Brain2.Beliefs.Learn(c, Topic.Thing, k.Cup, 3, author != null ? BeliefSource.Told : BeliefSource.Computer, 0.7f, m.Author);
                break;
            case ChatKind.Sorry when k != null:
                if (w.Brain2.Beliefs.Get(c, Topic.Thing, k.Cup) != null) w.Brain2.Beliefs.Learn(c, Topic.Thing, k.Cup, 3, BeliefSource.Told, 0.7f, m.Author);
                break;
            case ChatKind.Ask when m.Thing >= 0:
            {
                // 물건을 찾는 글: 본 사람 · 치운 사람 · 빌려 간 사람이 답한다
                var b = w.Belongings.Get(m.Thing);
                if (b == null || author == null || c == author) break;
                bool knows = b.Holder == c.Id || b.BorrowedBy == c.Id || Spotted(b) is { } sp && sp.who == c.Id && w.Tick - sp.t < SimTime.TicksPerDay;
                if (!knows || Life.Has(c, Habit.Loner) && c.AffinityTo(author) < 0.2f) break;
                var at = w.Belongings.CellOf(b);
                string where = b.Holder == c.Id ? "나한테 있어" : b.At == null ? "사물함에 넣어 뒀어" : $"{w.Ship.RoomAt(at)?.Name ?? "거기"}에 있던데";
                Chat.Post(c, ChatKind.Answer, ShipChat.Voice(c, where, where.Replace("있어", "있어요").Replace("뒀어", "뒀어요").Replace("있던데", "있었어요")), thing: b.Id, reply: m.Id, about: author.Id);
                Stats.Answered++;
                break;
            }
            case ChatKind.Answer when m.Thing >= 0 && m.About == c.Id:
            {
                var b = w.Belongings.Get(m.Thing);
                if (b == null) break;
                var at = w.Belongings.CellOf(b);
                w.Brain2.Beliefs.Learn(c, Topic.Thing, b.Id, 1, BeliefSource.Told, author != null ? Cred(author) : 0.6f, m.Author);
                _seen[b.Id] = (at, w.Tick);
                if (author != null && b.Holder != author.Id)
                {
                    bool annoyed = Life.Has(c, Habit.Messy) && author.Habits.Contains(Habit.NeatFreak);
                    c.ChangeAffinity(author, annoyed ? -0.02f : 0.02f);
                }
                if (b.Holder < 0 && !Near(at, c.Cell)) AddIntent(InfoDo.Search, c, -1, b.Id, at, 0.42f, $"{author?.Name ?? "누가"} 알려 준 곳으로", 4f);
                break;
            }
        }
    }

    /// <summary>믿음 장부 설명 (Topic.Thing).</summary>
    public string DescribeThing(Belief b)
    {
        var w = _w;
        var t = w.Belongings.Get(b.Id);
        string n = t?.Name ?? "물건";
        string P(int id) => CrewOf(id)?.Name ?? "누군가";
        return b.Value switch
        {
            1 => $"{n} — 어디 있는지 안다",
            2 => b.Aux > 0 ? $"{n} 깨짐 — {Ko.IGa(P(b.Aux - 1))} 깬 것 같다" : $"{n} 깨짐 — 누가 그랬는지 모름",
            3 => $"{n} 깨짐 — 배가 덜컹해 떨어졌다",
            _ => $"{n} — 어디 갔는지 모름",
        };
    }
}
