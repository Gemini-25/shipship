namespace ShipEngine;

/// <summary>사람 일 갈래 — 무게 순 (큰 일 없는 날의 장 제목은 앞의 것부터): 짝 · 고백 · 화해 · 꿈 · 별명 · 관습 · 부고.</summary>
public enum PeopleKind : byte { Couple, Confess, Reconcile, Dream, Nickname, Custom, Obituary }

/// <summary>사람 일 하나: 때 · 갈래 · 두 사람 (마음 번호 — 없으면 −1) · 화면 글 (그때 짓는다 — 원인 글 · 이름).</summary>
public struct PeopleNote { public long Tick; public PeopleKind Kind; public int A, B; public string Text; }

/// <summary>지나온 길의 갈래 (V6): 구함 · 다침 · 쓰러짐 · 마음 (겁 · 단단해짐 · 외상 뒤 성장) · 짝 · 꿈 · 숨짐.</summary>
public enum PathKind : byte { Rescue, Hurt, Collapsed, Heart, Couple, Dream, Died }

/// <summary>지나온 길 한 걸음: 때 · 갈래 · 마음 갈래 (Heart일 때) · 화면 글 (그때 짓는다 — 이름).</summary>
public struct PathStep { public long Tick; public PathKind Kind; public HeartEvent Heart; public string Text; }

/// <summary>한 사람 (마음 번호)의 장부: 한 일 (구함 · 끔 · 덧댐 · 돌봄 · 고침) · 겪은 일 (다침 · 쓰러짐 · 마음 — 겁 · 단단해짐 · 외상 뒤 성장) · 숨진 때 · 추모에서 나온 말 (셋까지) · 지나온 길 (V6 — 때 차례 · 예순넷까지).</summary>
public sealed class PersonLog
{
    public int Rescued, Fires, Patches, Treated, Repairs;
    public int Hurt, Collapsed, Hearts;
    public long DiedAt = -1;
    public int DiedCause, DiedCell = -1;
    public readonly List<string> Words = new();
    public readonly List<PathStep> Path = new();

    /// <summary>길에 한 걸음 (넘치면 가장 이른 다침 · 쓰러짐부터 놓는다 — 없으면 가장 이른 것).</summary>
    public void Step(PathStep p)
    {
        Path.Add(p);
        if (Path.Count <= PeopleBook.MaxPath) return;
        int drop = Path.FindIndex(x => x.Kind is PathKind.Hurt or PathKind.Collapsed);
        Path.RemoveAt(drop >= 0 ? drop : 0);
    }
}

/// <summary>
/// ⑬ 사람 일 장부 (docs/OBSERVER.md §5.4 · 5.5 — V3): 연출이 사실 고리에서 적기만 한다 (관심 점수 · 하이라이트 문턱에는 넣지 않는다 — 세계 밖 · 읽기만).
/// 묘비 (함께한 날 · 한 일 · 추모 말) · 결산 (사람마다 한 줄) · 큰 일 없는 날의 장 제목 · 한 주 장의 '관계 장면'이 읽는다.
/// 추모 모임이 열린 동안 사회의 말 기록 (128마디 고리)에서 그 모임의 말을 그때 옮겨 적는다 — 고리가 돌아도 묘비에 남는다.
/// </summary>
public sealed class PeopleBook
{
    public const int MaxWords = 3, MaxNotes = 4096, MaxPath = 64;
    /// <summary>다침은 한 사람에 이만큼에 한 걸음만 (틱마다 날 수 있다 — 게임 시간).</summary>
    public static readonly long HurtStepEvery = Time.Hours(1);
    /// <summary>사람마다 장부 (열쇠 차례 — 같은 상태면 같은 바이트).</summary>
    public readonly SortedDictionary<int, PersonLog> Logs = new();
    public readonly List<PeopleNote> Notes = new();
    /// <summary>열린 추모 모임: 기리는 이 (−1 없음) · 연 때 · 마지막으로 읽은 말의 때.</summary>
    public int MournFor = -1;
    public long MournSince, MournSeen;

    public PersonLog Of(int agent) { if (!Logs.TryGetValue(agent, out var l)) Logs[agent] = l = new PersonLog(); return l; }
    public PersonLog? Find(int agent) => Logs.TryGetValue(agent, out var l) ? l : null;

    /// <summary>한 일 한 줄: '구한 사람 2 · 끈 불 3 · 덧댄 구멍 1 · 돌본 이 1 · 고친 것 2' (없으면 빈 글).</summary>
    public string Deeds(int agent)
    {
        var l = Find(agent);
        if (l == null) return "";
        var parts = new List<string>(5);
        if (l.Rescued > 0) parts.Add($"구한 사람 {l.Rescued}");
        if (l.Fires > 0) parts.Add($"끈 불 {l.Fires}");
        if (l.Patches > 0) parts.Add($"덧댄 구멍 {l.Patches}");
        if (l.Treated > 0) parts.Add($"돌본 이 {l.Treated}");
        if (l.Repairs > 0) parts.Add($"고친 것 {l.Repairs}");
        return string.Join(" · ", parts);
    }

    /// <summary>그 사이의 사람 일 (무게 순 — 같으면 이른 것).</summary>
    public IEnumerable<PeopleNote> Between(long from, long to) => Notes.Where(n => n.Tick >= from && n.Tick < to).OrderBy(n => n.Kind).ThenBy(n => n.Tick);

    /// <summary>그 사이 같은 두 사람 사이의 일 수 (두 사람 · 수 — 많은 차례 · 같으면 번호 차례).</summary>
    public List<(int a, int b, int n)> Pairs(long from, long to) => Notes.Where(n => n.Tick >= from && n.Tick < to && n.A >= 0 && n.B >= 0 && n.A != n.B)
        .GroupBy(n => (Math.Min(n.A, n.B), Math.Max(n.A, n.B))).Select(g => (g.Key.Item1, g.Key.Item2, g.Count())).OrderByDescending(p => p.Item3).ThenBy(p => p.Item1).ThenBy(p => p.Item2).ToList();

    public void Add(PeopleNote n) { Notes.Add(n); if (Notes.Count > MaxNotes) Notes.RemoveRange(0, Notes.Count - MaxNotes); }

    public void Save(StateWriter w)
    {
        w.Tag("사람 일 장부");
        w.I32(Logs.Count);
        foreach (var (k, l) in Logs)
        {
            w.I32(k); w.I32(l.Rescued); w.I32(l.Fires); w.I32(l.Patches); w.I32(l.Treated); w.I32(l.Repairs); w.I32(l.Hurt); w.I32(l.Collapsed); w.I32(l.Hearts);
            w.I64(l.DiedAt); w.I32(l.DiedCause); w.I32(l.DiedCell);
            w.I32(l.Words.Count); foreach (var s in l.Words) w.Str(s);
        }
        w.I32(Notes.Count);
        foreach (var n in Notes) { w.I64(n.Tick); w.Byte((byte)n.Kind); w.I32(n.A); w.I32(n.B); w.Str(n.Text); }
        w.I32(MournFor); w.I64(MournSince); w.I64(MournSeen);
        // 지나온 길 (연출 기록 판 3 — V6)
        w.I32(Logs.Count);
        foreach (var (k, l) in Logs)
        {
            w.I32(k); w.I32(l.Path.Count);
            foreach (var p in l.Path) { w.I64(p.Tick); w.Byte((byte)p.Kind); w.Byte((byte)p.Heart); w.Str(p.Text); }
        }
    }

    /// <summary>읽는다 (path: 연출 기록 판 3부터 — 지나온 길).</summary>
    public void Load(StateReader r, bool path = true)
    {
        r.Tag("사람 일 장부");
        Logs.Clear();
        for (int i = 0, n = r.I32(); i < n; i++)
        {
            int k = r.I32();
            var l = new PersonLog { Rescued = r.I32(), Fires = r.I32(), Patches = r.I32(), Treated = r.I32(), Repairs = r.I32(), Hurt = r.I32(), Collapsed = r.I32(), Hearts = r.I32(), DiedAt = r.I64(), DiedCause = r.I32(), DiedCell = r.I32() };
            for (int j = 0, m = r.I32(); j < m; j++) l.Words.Add(r.Str() ?? "");
            Logs[k] = l;
        }
        Notes.Clear();
        for (int i = 0, n = r.I32(); i < n; i++) Notes.Add(new PeopleNote { Tick = r.I64(), Kind = (PeopleKind)r.Byte(), A = r.I32(), B = r.I32(), Text = r.Str() ?? "" });
        MournFor = r.I32(); MournSince = r.I64(); MournSeen = r.I64();
        if (!path) return;
        for (int i = 0, n = r.I32(); i < n; i++)
        {
            var l = Of(r.I32());
            for (int j = 0, m = r.I32(); j < m; j++) l.Path.Add(new PathStep { Tick = r.I64(), Kind = (PathKind)r.Byte(), Heart = (HeartEvent)r.Byte(), Text = r.Str() ?? "" });
        }
    }

    public void Clear() { Logs.Clear(); Notes.Clear(); MournFor = -1; MournSince = MournSeen = 0; }
}

public sealed partial class Director
{
    /// <summary>사람 일 장부 (V3 — 적기만 · 관심 점수에 넣지 않는다).</summary>
    public readonly PeopleBook People = new();
    /// <summary>추모 모임을 이만큼 넘게 열린 채로 두지 않는다 (닫는 사실을 놓쳐도 — 게임 시간).</summary>
    public static readonly long MournMax = Time.Hours(6);

    /// <summary>사실 하나를 사람 일 장부에 (Observe의 사실 고리 — DayNote 곁에서 · 한 사실 ≤ 0.5µs).</summary>
    private void PeopleNote(Sim sim, in Fact f, int agent, Mind? mind)
    {
        string Name(int a) => mind != null && a >= 0 && a < mind.Agents.Count ? mind.NameOf(a) : "누군가";
        static string Head(string s) { int i = s.IndexOf(" — ", StringComparison.Ordinal); return i > 0 ? s[..i] : s; }
        switch (f.Kind)
        {
            case FactKind.Rescue when agent >= 0:
                People.Of(agent).Rescued++;
                People.Of(agent).Step(new PathStep { Tick = f.Tick, Kind = PathKind.Rescue, Text = f.Other >= 0 && f.Other != agent ? $"{Ko.Eul(Name(f.Other))} 구했다" : "사람을 구했다" });
                break;
            case FactKind.Extinguished when agent >= 0: People.Of(agent).Fires++; break;
            case FactKind.Patched when agent >= 0: People.Of(agent).Patches++; break;
            case FactKind.Treated when agent >= 0: People.Of(agent).Treated++; break;
            case FactKind.Repaired when agent >= 0: People.Of(agent).Repairs++; break;
            case FactKind.Hurt when agent >= 0:
            {
                var l = People.Of(agent); l.Hurt++;
                int last = l.Path.FindLastIndex(p => p.Kind == PathKind.Hurt);
                if (last < 0 || f.Tick - l.Path[last].Tick >= PeopleBook.HurtStepEvery) l.Step(new PathStep { Tick = f.Tick, Kind = PathKind.Hurt, Text = "다쳤다" });
                break;
            }
            case FactKind.Collapsed when agent >= 0: People.Of(agent).Collapsed++; People.Of(agent).Step(new PathStep { Tick = f.Tick, Kind = PathKind.Collapsed, Text = "쓰러졌다" }); break;
            case FactKind.Heart when agent >= 0 && (HeartEvent)(int)f.Size is HeartEvent.Fearful or HeartEvent.Hardened or HeartEvent.Grew:
            {
                var he = (HeartEvent)(int)f.Size;
                People.Of(agent).Hearts++;
                People.Of(agent).Step(new PathStep { Tick = f.Tick, Kind = PathKind.Heart, Heart = he, Text = he switch { HeartEvent.Fearful => "겁에 질렸다 — 겁이 많아졌다", HeartEvent.Hardened => "맞서며 단단해졌다", _ => "아픔을 지나 자랐다" } });
                break;
            }
            case FactKind.Died when agent >= 0:
            {
                var l = People.Of(agent);
                if (l.DiedAt < 0) { l.DiedAt = f.Tick; l.DiedCause = f.Cause; l.DiedCell = f.Cell; l.Step(new PathStep { Tick = f.Tick, Kind = PathKind.Died, Text = "숨을 거두었다" }); }
                break;
            }
            case FactKind.Relationship when (int)f.Size is 1 or 4 && f.Actor >= 0:
                People.Add(new PeopleNote { Tick = f.Tick, Kind = (int)f.Size == 1 ? PeopleKind.Couple : PeopleKind.Reconcile, A = f.Actor, B = f.Other,
                    Text = f.Cause != 0 ? Head(sim.Causes.Text(f.Cause)) : $"{Ko.Gwa(Name(f.Actor))} {Ko.I(Name(f.Other))} 가까워졌다" });
                if ((int)f.Size == 1)
                {
                    People.Of(f.Actor).Step(new PathStep { Tick = f.Tick, Kind = PathKind.Couple, Text = $"{Ko.Gwa(Name(f.Other))} 짝이 되었다" });
                    if (f.Other >= 0 && f.Other != f.Actor) People.Of(f.Other).Step(new PathStep { Tick = f.Tick, Kind = PathKind.Couple, Text = $"{Ko.Gwa(Name(f.Actor))} 짝이 되었다" });
                }
                break;
            case FactKind.Talk when f.Actor >= 0 && f.Other >= 0 && (TalkEnd)(int)f.Size is TalkEnd.Accept or TalkEnd.Reject:
                People.Add(new PeopleNote { Tick = f.Tick, Kind = PeopleKind.Confess, A = f.Actor, B = f.Other, Text = $"{Ko.I(Name(f.Actor))} {Name(f.Other)}에게 마음을 털어놓았다" });
                break;
            case FactKind.Talk when f.Actor >= 0 && f.Other >= 0 && (TalkEnd)(int)f.Size == TalkEnd.Apology:
                People.Add(new PeopleNote { Tick = f.Tick, Kind = PeopleKind.Reconcile, A = f.Actor, B = f.Other, Text = $"{Ko.I(Name(f.Actor))} {Name(f.Other)}에게 사과했다" });
                break;
            case FactKind.Aim when f.Actor >= 0 && f.Size >= 1:
                People.Add(new PeopleNote { Tick = f.Tick, Kind = PeopleKind.Dream, A = f.Actor, B = -1, Text = $"{Ko.I(Name(f.Actor))} 꿈을 이루었다" });
                People.Of(f.Actor).Step(new PathStep { Tick = f.Tick, Kind = PathKind.Dream, Text = Mind.AimName((Aim)f.Other) is { Length: > 0 } aim ? $"꿈을 이루었다 — {aim}" : "꿈을 이루었다" });
                break;
            case FactKind.Culture when (CultureEvent)(int)f.Size == CultureEvent.Nickname && f.Actor >= 0:
            {
                int by = f.Cause != 0 ? sim.Causes[f.Cause].Actor : -1;
                People.Add(new PeopleNote { Tick = f.Tick, Kind = PeopleKind.Nickname, A = f.Actor, B = by >= 0 && by != f.Actor ? by : -1, Text = f.Cause != 0 ? Head(sim.Causes.Text(f.Cause)) : $"{Name(f.Actor)}에게 별명이 생겼다" });
                break;
            }
            case FactKind.Culture when (CultureEvent)(int)f.Size == CultureEvent.Custom && f.Cause != 0:
                People.Add(new PeopleNote { Tick = f.Tick, Kind = PeopleKind.Custom, A = -1, B = -1, Text = Head(sim.Causes.Text(f.Cause)) });
                break;
            case FactKind.Culture when (CultureEvent)(int)f.Size == CultureEvent.Memorial && f.Other == People.MournFor:
                MournRead(sim, mind); People.MournFor = -1;   // 추모가 끝났다 — 마지막 말까지 옮기고 닫는다
                break;
            case FactKind.Letter when f.Actor >= 0 && f.Size <= -2:
                People.Add(new PeopleNote { Tick = f.Tick, Kind = PeopleKind.Obituary, A = f.Actor, B = -1, Text = $"{Ko.I(Name(f.Actor))} 고향에서 부고를 받았다" });
                break;
            case FactKind.Meeting when (int)f.Size == (int)CouncilKind.Memorial && f.Other >= 0:
                People.MournFor = f.Other; People.MournSince = People.MournSeen = f.Tick - 1;
                break;
            case FactKind.Meeting when f.Size < 0 && f.Other == People.MournFor:
                People.MournFor = -1;   // 모임이 흩어졌다
                break;
        }
    }

    /// <summary>열린 추모 모임의 새 말 (기리는 말 — 죽음 · 위로)을 그 이의 장부에 옮긴다 (셋까지). 열린 동안만 · 틱마다 새 말만 본다.</summary>
    private void MournRead(Sim sim, Mind? mind)
    {
        int dead = People.MournFor;
        if (dead < 0 || sim.Find<Society>() is not { } so) return;
        if (sim.Tick - People.MournSince > MournMax) { People.MournFor = -1; return; }
        var log = People.Of(dead);
        long seen = People.MournSeen;
        int i = so.Sayings.Count;
        while (i > 0 && so.Sayings[i - 1].Tick > People.MournSeen) i--;
        for (; i < so.Sayings.Count; i++)
        {
            var s = so.Sayings[i];
            seen = Math.Max(seen, s.Tick);
            if (s.Topic != Topic.Death || s.End != TalkEnd.Comfort || s.Speaker == dead || log.Words.Count >= PeopleBook.MaxWords) continue;
            string who = mind != null && s.Speaker >= 0 && s.Speaker < mind.Agents.Count ? mind.NameOf(s.Speaker) : "누군가";
            string line = s.Line != 0 ? sim.Causes.Text(s.Line) : "";
            if (line.Length > 0) log.Words.Add($"{who}: \"{line}\"");
        }
        People.MournSeen = seen;
    }
}
