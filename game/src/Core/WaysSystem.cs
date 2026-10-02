using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.25 문제마다 여러 갈래 해법 — 시스템.
// ① 찾기: 불 · 연기 · 파공 · 갇힘 · 정전 · 부상자 · 부품 없음 · 산소 · 식량 · 물 · 과열 · 독가스 · 통신 끊김 · 화물 이탈 · 물 샘 · 추위 · 불꽃 · 쏟은 것 · 조명 · 막힘 (20종).
// ② 주컴퓨터가 갈래를 견준다 (ComputerForesee 같은 저울 · 타임라인 · 채점) → 안을 낸다 · 제 손으로 되는 것(잠금 해제 · 배기 · 회로 끊기)은 직접 한다 · 로봇을 보낸다.
// ③ 사람마다 고른다: 아는 것(믿음) · 손에 든 것 · 근처 물건(재질 · 물리) · 솜씨 · 성격(과감/신중) · 배의 관행 · 컴퓨터 안 · 남은 시간 → 같은 사고도 배마다 · 사람마다 다르다.
//    컴퓨터 안과 다른 길을 고르면 이유가 남는다. 기존 수순(소화기 · 실링폼 · 보조 발전기 · 구조 · 제작)도 한 갈래 — 고른 사람은 그 일감으로 간다.
// ④ 결과는 규칙에서 (WaysRules) · 부작용(감전 · 오래 못 가는 마개 · 멈춘 설비 · 휜 문틀) · 나중에 제대로 고칠 일(따로 일감).
// ⑤ 배움: 잘 통한 갈래는 배의 관행이 되고(다음엔 더 빨리 · 남도 따라 한다) · 망한 갈래는 그 사람의 교훈(기억 · 일기) · 이 배가 예전에 같은 문제를 어떻게 풀었는지.
// ⑥ 보이게: 그 순간의 그림(WayMark · ShipViewWays) · 사람 카드의 "왜" · 연대기 · 사고 카드에 쓴 방법.

public sealed class WayCase
{
    public int Id { get; init; }
    public Snag Snag { get; init; }
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public Cell At { get; set; }
    public int DoorId { get; init; } = -1;
    /// <summary>문제의 주인공 (갇힌 사람 · 다친 사람).</summary>
    public int CrewId { get; init; } = -1;
    public int FurnId { get; init; } = -1;
    public ItemKind? Part { get; init; }
    public long Opened { get; init; }
    public long Closed { get; set; } = -1;
    public long NextChoose { get; set; }
    public int Rounds { get; set; }
    /// <summary>컴퓨터가 견줘 고른 갈래 · 이유 · 타임라인 번호.</summary>
    public string ComputerPick { get; set; } = "";
    public string ComputerWhy { get; set; } = "";
    public int Decision { get; set; } = -1;
    /// <summary>기존 수순이 하는 것을 본 것 (갈래 · 누가).</summary>
    public List<(string way, int who)> Seen { get; } = new();
    public string SolvedBy { get; set; } = "";
    public int SolvedWho { get; set; } = -1;
    public bool Open => Closed < 0;
    public int Welds0 { get; init; }
    public int Seals0 { get; init; }
}

/// <summary>한 사람(로봇 · 컴퓨터)이 고른 한 갈래.</summary>
public sealed class WayTry
{
    public int Id { get; init; }
    public int CaseId { get; init; }
    public string WayId { get; init; } = "";
    public int CrewId { get; init; } = -1;
    public int RobotId { get; init; } = -1;
    public string Why { get; set; } = "";
    public string Suggested { get; init; } = "";
    public bool Defied { get; init; }
    public long Chosen { get; init; }
    public long Started { get; set; } = -1;
    public long Ended { get; set; } = -1;
    /// <summary>0 골랐다 · 1 하는 중 · 2 지켜본다(기존 수순 · 문 닫고 기다림) · 3 끝.</summary>
    public int State { get; set; }
    public bool Ok { get; set; }
    public string Result { get; set; } = "";
    public string Thing { get; set; } = "";
    public int Rounds { get; set; }
    /// <summary>도와주는 사람 (들것 둘째 손).</summary>
    public bool Helper { get; init; }
    public float Score { get; init; }
    public Way Way => WaysTable.Get(WayId)!;
}

/// <summary>배의 관행: 이 배가 그 갈래를 얼마나 써 봤고 잘됐나.</summary>
public sealed class WayPractice
{
    public string WayId { get; init; } = "";
    public int Uses { get; set; }
    public int Ok { get; set; }
    public int Bad { get; set; }
    public float BestMinutes { get; set; } = 9999f;
    public float LastMinutes { get; set; }
    public bool Custom { get; set; }
    public string Origin { get; set; } = "";
    public long Since { get; set; } = -1;
}

/// <summary>남는 그림 (매트리스로 막은 구멍 · 쇠지레로 벌린 문 · 엮은 도구 …).</summary>
public sealed class WayMark
{
    public WayLook Look { get; init; }
    public Cell At { get; set; }
    public int RoomId { get; init; } = -1;
    public int DoorId { get; init; } = -1;
    public long Since { get; init; }
    public long Until { get; set; } = -1;
    public bool Active { get; set; } = true;
    public bool Ok { get; set; } = true;
    public Material Mat { get; init; }
    public float Q { get; set; } = 1f;
    public int Try { get; init; } = -1;
    public int Who { get; init; } = -1;
    /// <summary>바깥 쪽 (벽 · 문에서 방 안을 보는 방향).</summary>
    public Cell Dir { get; init; }
}

/// <summary>나중에 제대로 고칠 일.</summary>
public sealed class WayFollow
{
    public int Id { get; init; }
    public string WayId { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>0 임시 마개 → 실링폼 · 1 문틀 · 2 차단기 · 3 로봇 배터리 · 4 침대 매트리스 · 5 양액.</summary>
    public int Kind { get; init; }
    public Cell At { get; init; }
    public int RoomId { get; init; } = -1;
    public int DoorId { get; init; } = -1;
    public int RobotId { get; init; } = -1;
    public int FurnId { get; init; } = -1;
    public long Since { get; init; }
    public long Done { get; set; } = -1;
    public int By { get; set; } = -1;
    public int Claimed { get; set; } = -1;
}

/// <summary>임시 마개 (보통 봉합보다 빨리 닳는다).</summary>
public sealed class WayPlug
{
    public Cell Wall { get; init; }
    public Material Mat { get; init; }
    public float Decay { get; init; }
    public string Name { get; init; } = "";
    public long Since { get; init; }
    public int Follow { get; init; } = -1;
    public int Robot { get; init; } = -1;
    public bool Gone { get; set; }
}

public readonly record struct WayPast(long Tick, Snag Snag, string WayId, string Room, bool Ok, float Minutes, int Who);

public sealed class WaysStats
{
    public int Cases, Chosen, Defied, Done, Failed, Customs, Follows, FollowsDone, Shocks, PlugsFell, Noticed, ComputerActs, Robots;
    public readonly int[] BySnag = new int[20];
}

public sealed partial class WaysSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6143 + 4231));
    public static bool Off { get; set; }

    public List<WayCase> Cases { get; } = new();
    public List<WayTry> Tries { get; } = new();
    public List<WayPractice> Practices { get; } = new();
    public List<WayMark> Marks { get; } = new();
    public List<WayFollow> Follows { get; } = new();
    public List<WayPlug> Plugs { get; } = new();
    public List<WayPast> Past { get; } = new();
    public WaysStats Stats { get; } = new();

    private int _nextCase = 1, _nextTry = 1, _nextFollow = 1, _beat;
    private readonly Dictionary<string, long> _cool = new();
    private readonly Dictionary<long, (int ok, int bad)> _mine = new();
    private readonly List<(int furn, long since, int by, bool noticed)> _stripped = new();
    private readonly List<(int room, int tryId, long since, long outSince, bool letBurn)> _seals = new();
    private readonly List<(int room, long until, bool set)> _lamps = new();

    public WaysSystem(World w) => _w = w;

    // ───────────── 조회 ─────────────

    public WayCase? Case(int id) { foreach (var k in Cases) if (k.Id == id) return k; return null; }
    public WayTry? TryOf(CrewMember c) { for (int i = Tries.Count - 1; i >= 0; i--) { var t = Tries[i]; if (t.CrewId == c.Id && t.State is 0 or 1) return t; } return null; }
    public WayPractice Practice(string wayId)
    {
        foreach (var p in Practices) if (p.WayId == wayId) return p;
        var np = new WayPractice { WayId = wayId };
        Practices.Add(np);
        return np;
    }
    public WayPractice? PracticeOrNull(string wayId) { foreach (var p in Practices) if (p.WayId == wayId) return p; return null; }
    private static long MineKey(CrewMember c, string wayId) => (long)c.Id * 4096 + WaysTable.Index(wayId);
    public (int ok, int bad) Mine(CrewMember c, string wayId) => _mine.TryGetValue(MineKey(c, wayId), out var v) ? v : (0, 0);

    /// <summary>다음엔 더 빨리: 제 손에 익은 만큼 · 배의 관행이면 더.</summary>
    public float SpeedMul(CrewMember? c, Way way)
    {
        var p = PracticeOrNull(way.Id);
        float ship = p == null ? 0f : p.Custom ? 0.25f : 0.06f * Math.Min(2, p.Ok);
        float mine = c == null ? 0f : 0.12f * Math.Min(3, Mine(c, way.Id).ok);
        return 1f / (1f + ship + mine);
    }

    /// <summary>사람 카드: 왜 그 방법 (자연스러운 한 줄).</summary>
    public string? WhyLine(CrewMember c)
    {
        for (int i = Tries.Count - 1; i >= 0; i--)
        {
            var t = Tries[i];
            if (t.CrewId != c.Id) continue;
            if (_w.Tick - t.Chosen > SimTime.Hours(10)) return null;
            var way = t.Way;
            string head = t.State == 3 ? (t.Ok ? $"{way.Name} — 됐다" : $"{way.Name} — {(t.Result.Length > 0 ? t.Result : "안 됐다")}") : way.Name;
            return t.Why.Length > 0 ? $"{head} · {t.Why}" : head;
        }
        return null;
    }

    /// <summary>사고 카드: 그 사고 동안 (그 방에서) 쓴 방법.</summary>
    public string? MethodIn(long from, long to, Room? room)
    {
        string? best = null;
        foreach (var k in Cases)
        {
            if (k.Opened > to || (k.Closed >= 0 && k.Closed < from)) continue;
            if (room != null && k.RoomId != room.Id) continue;
            string? used = k.SolvedBy.Length > 0 ? k.SolvedBy : null;
            if (used == null) for (int i = Tries.Count - 1; i >= 0; i--) if (Tries[i].CaseId == k.Id && Tries[i].State is 1 or 2) { used = Tries[i].WayId; break; }
            if (used == null && k.Seen.Count > 0) used = k.Seen[^1].way;
            if (used != null && WaysTable.Get(used) is Way w) best = w.Name;
        }
        return best;
    }

    /// <summary>이 배가 예전에 같은 문제를 어떻게 풀었나 (최근 것부터).</summary>
    public List<string> PastLines(Snag s, int n = 3)
    {
        var list = new List<string>();
        for (int i = Past.Count - 1; i >= 0 && list.Count < n; i--)
        {
            var p = Past[i];
            if (p.Snag != s) continue;
            var way = WaysTable.Get(p.WayId);
            string who = _w.Crew.FirstOrDefault(x => x.Id == p.Who)?.Name ?? "";
            list.Add($"{SimTime.Day(p.Tick)}일 {p.Room} — {(who.Length > 0 ? Ko.IGa(who) + " " : "")}{way?.Name ?? p.WayId}{(p.Ok ? $" ({p.Minutes:0}분)" : " (안 됐다)")}");
        }
        return list;
    }

    /// <summary>관행 한 줄씩 (화면용).</summary>
    public IEnumerable<string> CustomLines() => Practices.Where(p => p.Custom).Select(p => $"{WaysTable.Name(WaysTable.Get(p.WayId)!.Snag)}: {WaysTable.Get(p.WayId)!.Name} — {p.Origin}");

    // ───────────── 틱 ─────────────

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        TickPlugs(dt);
        TickSeals();
        TickLamps();
        TickComputerTries();
        if (++_beat % 4 != 0) return;
        Scan();
        Notice();
        Expire();
    }

    private readonly List<Found> _found = new();
    private readonly HashSet<string> _foundKeys = new();
    private int[] _people = Array.Empty<int>();

    private readonly record struct Found(string Key, Snag S, Room? Room, Cell At, string Title, int Door = -1, int Crew = -1, int Furn = -1, ItemKind? Part = null);

    private void Scan()
    {
        var w = _w;
        var ship = w.Ship;
        _found.Clear();
        _foundKeys.Clear();
        if (_people.Length != ship.Rooms.Count) _people = new int[ship.Rooms.Count];
        Array.Clear(_people);
        int alive = 0;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away) continue;
            alive++;
            if (c.Room is Room cr && cr.Id < _people.Length && c.CanAct) _people[cr.Id]++;
        }
        Detect(alive);
        foreach (var f in _found) _foundKeys.Add(f.Key);
        // 풀린 사고는 닫는다
        foreach (var k in Cases)
            if (k.Open && !_foundKeys.Contains(k.Key)) Close(k);
        // 새 사고 · 아직 안 풀린 사고는 다시 고른다
        foreach (var f in _found)
        {
            var k = Cases.FirstOrDefault(x => x.Open && x.Key == f.Key);
            if (k == null)
            {
                if (_cool.TryGetValue(f.Key, out var until) && w.Tick < until) continue;
                if (Cases.Count(x => x.Open) >= 12) continue;
                k = OpenCase(f);
            }
            Observe(k);
            if (w.Tick >= k.NextChoose && k.Rounds < 5 && !Tries.Any(t => t.CaseId == k.Id && t.State is 0 or 1 || t.CaseId == k.Id && t.State == 2 && t.WayId != "" && WaysTable.Get(t.WayId)!.Fx != WayFx.Existing))
                Respond(k);
        }
    }

    private bool Has(Room r) => r.Id < _people.Length && _people[r.Id] > 0;

    private void Detect(int alive)
    {
        var w = _w;
        var ship = w.Ship;
        // 불 (알려진 것만)
        foreach (var (room, hottest, _) in w.Fire.KnownFires())
            Add(new Found($"fire:{room.Id}", Snag.Fire, room, hottest, $"{room.Name} 불"));
        bool anyLeak = false;
        foreach (var r in ship.Rooms)
        {
            if (r.Detached) continue;
            if (r.Leaking) anyLeak = true;
            if (r.Abandoned) continue;
            bool ppl = Has(r);
            var mid = r.Cells.Count > 0 ? r.Cells[r.Cells.Count / 2] : default;
            bool burning = w.Fire.Count > 0 && w.Fire.CountIn(r) > 0;
            if (ppl && !burning && r.Air.Smoke > 0.45f) Add(new Found($"smoke:{r.Id}", Snag.Smoke, r, mid, $"{r.Name} 연기"));
            if (ppl && !burning && r.Air.O2 < 15.5f && r.Air.Pressure > 50f) Add(new Found($"o2:{r.Id}", Snag.Oxygen, r, mid, $"{r.Name} 산소 부족"));
            if (ppl && !burning && r.Air.Temperature > 38f) Add(new Found($"heat:{r.Id}", Snag.Overheat, r, mid, $"{r.Name} 과열"));
            if (ppl && r.Air.Temperature < 12f) Add(new Found($"cold:{r.Id}", Snag.Cold, r, mid, $"{r.Name} 추위"));
            if (ppl && !burning && (r.Air.Toxin > 0.25f || r.Air.CO > 0.15f)) Add(new Found($"tox:{r.Id}", Snag.ToxicGas, r, mid, $"{r.Name} 독가스"));
            if (ppl && !r.DataLinked) Add(new Found($"comms:{r.Id}", Snag.CommsDown, r, mid, $"{r.Name} 통신 끊김"));
            if (r.Flood > 0.15f) Add(new Found($"leak:{r.Id}", Snag.Leak, r, mid, $"{r.Name} 물 샘"));
            if (ppl && r.LightsOut && r.Powered) Add(new Found($"dark:{r.Id}", Snag.Dark, r, mid, $"{r.Name} 조명 꺼짐"));
            if (ppl && !r.Powered && !r.BreakerOff) Add(new Found($"power:{r.Circuit}", Snag.Blackout, r, mid, $"{PowerGrid.CircuitName(r.Circuit)} 회로 정전"));
        }
        // 파공 (막지 않은 구멍)
        if (anyLeak)
            foreach (var (cell, wall) in ship.Walls)
            {
                if (!wall.IsHull || wall.Breach <= 0f || wall.Patched || wall.FrameLost) continue;
                if (Hull.InsideRoom(ship, cell) is not Room br || br.Abandoned || br.Detached) continue;
                Add(new Found($"breach:{cell.X},{cell.Y}", Snag.Breach, br, cell, $"{br.Name} 파공"));
            }
        // 갇힘 · 부상자
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.Outside || c.Room is not Room r) continue;
            if (c.Down)
            {
                if (c.CarriedBy == null && c.CareBed == null && !c.LaidSafe && !w.Fire.AnyWithin(c.Cell, 0.5f)) Add(new Found($"hurt:{c.Id}", Snag.Injured, r, c.Cell, $"{c.Name} 쓰러짐", Crew: c.Id));
            }
            if (Trap(r) is Door d) Add(new Found($"trap:{c.Id}", Snag.Trapped, r, c.Cell, $"{c.Name} {r.Name}에 갇힘", Door: d.Id, Crew: c.Id));
        }
        // 부품 없음 · 막힘 (오래 못 고친 고장만)
        int parts = 0;
        foreach (var m in ship.Machines)
        {
            if (m.Faults.Count == 0 || m.Body.Room.Abandoned) continue;
            foreach (var fault in m.Faults)
            {
                if (fault.Kind is FaultKind.Stripped or FaultKind.BreakerTrip) continue;
                long age = w.Tick - fault.Since;
                if (fault.Kind is FaultKind.FilterClogged or FaultKind.NutrientClog or FaultKind.InjectorClog or FaultKind.NozzleClog or FaultKind.HeatsinkClog or FaultKind.DrainClog && age > SimTime.Hours(2))
                    Add(new Found($"clog:{m.Body.Id}", Snag.Clog, m.Body.Room, m.Body.Cells[0], $"{m.Name} 막힘", Furn: m.Body.Id, Part: fault.Part));
                else if (parts < 3 && fault.Part is ItemKind p && age > SimTime.Hours(1) && ship.CountStored(p) == 0 && (m.Spec.Critical || fault.Spec.OutputFactor <= 0f))
                {
                    parts++;
                    Add(new Found($"part:{m.Body.Id}:{(int)p}", Snag.NoPart, m.Body.Room, m.Body.Cells[0], $"{m.Name} {ItemKinds.Name(p)} 없음", Furn: m.Body.Id, Part: p));
                }
            }
        }
        // 식량 · 물 (배 전체)
        if (alive > 0)
        {
            int food = ship.CountStored(ItemKind.Meal) + ship.CountStored(ItemKind.Ration) + ship.CountStored(ItemKind.Produce);
            var mess = ship.LiveRooms.FirstOrDefault(r => r.Kind is RoomType.Mess or RoomType.Galley) ?? ship.LiveRooms.FirstOrDefault();
            if (food < alive && mess != null) Add(new Found("food", Snag.Food, mess, mess.Cells[mess.Cells.Count / 2], "식량이 바닥"));
            if (w.Water.Level < 25f && mess != null) Add(new Found("water", Snag.Water, mess, mess.Cells[mess.Cells.Count / 2], "물이 바닥"));
        }
        // 화물 이탈 (빠르게 미끄러지는 짐) · 불꽃 튀는 접속부 · 크게 쏟은 것
        foreach (var t in w.Matter.Things)
        {
            if (!t.Loose || t.Vel.LengthSquared() < 1.2f * 1.2f || t.Mass < 3f) continue;
            if (ship.RoomAt(t.At) is Room cr) Add(new Found($"cargo:{cr.Id}", Snag.CargoLoose, cr, t.At, $"{cr.Name} 짐이 미끄러진다"));
        }
        foreach (var j in w.Matter.Junctions)
        {
            if (!j.Live || j.Fixed || w.Tick - j.LastSpark > SimTime.Minutes(20) || j.Room < 0 || j.Room >= ship.Rooms.Count) continue;
            var jr = ship.Rooms[j.Room];
            Add(new Found($"spark:{jr.Id}", Snag.Spark, jr, j.At, $"{jr.Name} 배선에서 불꽃"));
        }
        if (w.Matter.Spills.Count > 0)
            foreach (var (idx, s) in w.Matter.Spills)
            {
                if (s.Liters < 4f) continue;
                var at = new Cell(idx % ship.Grid.Width, idx / ship.Grid.Width);
                if (ship.RoomAt(at) is Room sr && Has(sr)) Add(new Found($"spill:{sr.Id}", Snag.Spill, sr, at, $"{sr.Name} 바닥에 쏟은 것"));
            }
    }

    private void Add(Found f)
    {
        foreach (var x in _found) if (x.Key == f.Key) return;
        _found.Add(f);
    }

    /// <summary>방의 모든 문이 꽉 끼어 못 연다 (용접 · 휜 문틀) — 열린 정비 통로가 있어도 갇힌 건 갇힌 것 (나오는 길을 찾아야 한다).</summary>
    public static Door? Trap(Room r)
    {
        Door? first = null;
        int inner = 0;
        foreach (var d in r.Doors)
        {
            if (d.IsExternal) continue;
            inner++;
            if (!Stuck(d)) return null;
            first ??= d;
        }
        return inner > 0 ? first : null;
    }

    public static bool Stuck(Door d) => d.Welded && !d.Removed && !d.JammedOpen;

    private WayCase OpenCase(Found f)
    {
        var w = _w;
        int welds = 0, seals = 0;
        if (f.S == Snag.Breach && w.Ship.WallAt(f.At) is WallState ws) { welds = ws.Welds; seals = ws.Seals; }
        var k = new WayCase
        {
            Id = _nextCase++, Snag = f.S, Key = f.Key, Title = f.Title, RoomId = f.Room?.Id ?? -1, At = f.At, DoorId = f.Door, CrewId = f.Crew, FurnId = f.Furn, Part = f.Part,
            Opened = w.Tick, NextChoose = w.Tick, Welds0 = welds, Seals0 = seals,
        };
        Cases.Add(k);
        if (Cases.Count > 80) Cases.RemoveAll(x => !x.Open && Cases.IndexOf(x) < Cases.Count - 60);
        Stats.Cases++;
        Stats.BySnag[(int)f.S]++;
        Compare(k);
        return k;
    }

    private void Close(WayCase k)
    {
        var w = _w;
        k.Closed = w.Tick;
        _cool[k.Key] = w.Tick + SimTime.Hours(k.Snag is Snag.Food or Snag.Water ? 12 : 2);
        // 고른 갈래가 아직 진행 중이면 풀어 준다 (불이 먼저 꺼졌다 · 누가 먼저 막았다)
        foreach (var t in Tries)
        {
            if (t.CaseId != k.Id || t.State is 3) continue;
            if (t.State == 2 && t.Way.Fx == WayFx.Existing) { t.State = 3; t.Ended = w.Tick; t.Ok = true; t.Result = "끝났다"; continue; }
            if (t.State == 0) { t.State = 3; t.Ended = w.Tick; t.Result = "다른 손이 먼저 풀었다"; }
        }
        if (k.SolvedBy.Length == 0 && k.Seen.Count > 0)
        {
            var (way, who) = k.Seen[^1];
            k.SolvedBy = way;
            k.SolvedWho = who;
            var p = Practice(way);
            p.Uses++; p.Ok++;
            float min = (w.Tick - k.Opened) / (float)SimTime.Minutes(1);
            p.LastMinutes = min; p.BestMinutes = MathF.Min(p.BestMinutes, min);
            if (_w.Crew.FirstOrDefault(x => x.Id == who) is CrewMember c) Bump(c, way, true);
            AddPast(k, way, true, min, who);
        }
    }

    private void AddPast(WayCase k, string way, bool ok, float min, int who)
    {
        string room = k.RoomId >= 0 && k.RoomId < _w.Ship.Rooms.Count ? _w.Ship.Rooms[k.RoomId].Name : "배";
        Past.Add(new WayPast(_w.Tick, k.Snag, way, room, ok, min, who));
        if (Past.Count > 120) Past.RemoveAt(0);
    }

    private void Bump(CrewMember c, string way, bool ok)
    {
        var key = MineKey(c, way);
        var (vo, vb) = _mine.TryGetValue(key, out var x) ? x : (0, 0);
        _mine[key] = ok ? (vo + 1, vb) : (vo, vb + 1);
    }

    /// <summary>기존 수순이 하는 것을 지켜본다 (소화기 일감 · 소화 장치 · 로봇 · 진공 · 실링폼 · 용접 · 버리기 · 구조 · 제작 …).</summary>
    private void Observe(WayCase k)
    {
        var w = _w;
        var room = k.RoomId >= 0 && k.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[k.RoomId] : null;
        void See(string id, int who) { if (k.Seen.Count == 0 || k.Seen[^1].way != id) k.Seen.Add((id, who)); if (k.Seen.Count > 12) k.Seen.RemoveAt(0); }
        foreach (var way in WaysTable.Of(k.Snag))
        {
            if (way.Order is not WorkKind kind) continue;
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Job?.Order is not WorkOrder o || o.Kind != kind) continue;
                bool here = k.FurnId >= 0 ? o.Target.Furniture?.Id == k.FurnId || kind is WorkKind.Fabricate or WorkKind.Cannibalize && o.Product == k.Part
                    : k.CrewId >= 0 && kind == WorkKind.Rescue ? o.Target.Crew?.Id == k.CrewId
                    : room != null && (o.Target.CurrentRoom == room || o.Target.Room == room);
                if (!here) continue;
                if (way.Id == "breach.weld" && w.Ship.CountStored(ItemKind.Sealant) >= 1) continue; // 실링폼이 있으면 실링폼 갈래
                if (way.Id == "breach.sealant" && w.Ship.CountStored(ItemKind.Sealant) < 1) continue;
                See(way.Id, c.Id);
            }
        }
        if (room == null) return;
        switch (k.Snag)
        {
            case Snag.Fire:
                if (room.Suppression && room.Powered) See("fire.system", -1);
                if (room.Purging) See("fire.vacuum", -1);
                if (room.Inerting) See("fire.inert", -1);
                if (w.Fleet.BotOn(room)) See("fire.robot", -1);
                break;
            case Snag.Breach:
                if (room.Abandoned) See("breach.abandon", -1);
                if (w.Ship.WallAt(k.At) is WallState ws && ws.Patched && !Plugs.Any(p => p.Wall == k.At && !p.Gone))
                    See(ws.Welds > k.Welds0 ? "breach.weld" : ws.Seals > k.Seals0 ? "breach.sealant" : "breach.drone", -1);
                break;
            case Snag.Blackout:
                if (w.Power.AuxRunning) See("power.aux", -1);
                break;
            case Snag.Smoke:
                if (room.Purging) See("smoke.purge", -1);
                break;
        }
    }

    // ───────────── 컴퓨터가 견준다 ─────────────

    private static bool Acute(Snag s) => s is not (Snag.Food or Snag.Water or Snag.Spill or Snag.Dark or Snag.Clog);

    private void Compare(WayCase k)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.CoreOnline || AutomationSystem.Ship20Off || !Acute(k.Snag)) return;
        var room = RoomOf(k);
        if (room != null && !room.DataLinked && k.Snag != Snag.CommsDown) return; // 그 방이 보이지 않는다
        int inside = room == null ? 0 : _w.Crew.Count(c => !c.Dead && c.Room == room && c.Id != k.CrewId);
        var opts = new List<(Way way, ForeseeOption o)>();
        foreach (var way in WaysTable.Of(k.Snag))
        {
            var (ok, note, mins) = ShipCan(way, k, room);
            float risk = way.Risk;
            float occupants = way.Fx is WayFx.Seal or WayFx.LetBurn or WayFx.Blow || way.Id is "fire.vacuum" or "breach.abandon" ? inside * 0.6f : 0f;
            var o = new ForeseeOption
            {
                Name = way.Name, Key = way.Id, Minutes = mins,
                People = occupants + risk * 0.8f + (1f - way.Power) * WaysTable.Urgency(k.Snag) * (inside + 0.5f) * 0.5f,
                Ship = way.ShipCost + (1f - way.Power) * 0.3f + MathF.Min(1f, mins / 60f) * WaysTable.Urgency(k.Snag) * 0.25f,
                Allowed = ok && (!way.Leave || w.Automation.Character.Caution < 0.5f || way.By == WayBy.Computer), Blocked = ok ? (way.Leave ? "허락 없이는 못 한다" : "") : note, Note = note,
            };
            opts.Add((way, o));
        }
        var shortlist = opts.Where(x => x.o.Allowed).OrderBy(x => x.o.People + x.o.Ship).Take(3).Select(x => x.o).ToList();
        if (shortlist.Count < 2) return;
        var blocked = opts.Where(x => !x.o.Allowed && x.way.Book).Select(x => x.o).FirstOrDefault(); // 규정 갈래가 막혔으면 그것도 보여 준다
        if (blocked != null) shortlist.Add(blocked);
        var d = a.Foresee.Fleet(k.Snag is Snag.Fire ? "불" : k.Snag is Snag.Breach ? "파공" : WaysTable.Name(k.Snag), room, k.Title, shortlist);
        k.ComputerPick = d.Pick.Key;
        k.ComputerWhy = d.Reason;
        k.Decision = d.Id;
        var kk = k;
        d.Grader = (world, dd) => kk.Closed >= 0 ? (1, $"풀렸다 — {(WaysTable.Get(kk.SolvedBy)?.Name ?? "저절로")}") : ((int, string)?)null;
        var pick = WaysTable.Get(k.ComputerPick)!;
        // 제 손으로 되는 것은 직접 한다 · 로봇을 보낸다 · 사람 갈래는 권한다
        if (pick.By == WayBy.Computer && pick.Fx != WayFx.Existing) ComputerDo(k, pick);
        else if (pick.By == WayBy.Robot && pick.Fx == WayFx.RobotHold) w.Robots.WayHold(this, k, pick);
        else if (pick.By == WayBy.Crew) w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {k.Title} — {Ko.EulReul(pick.Name)} 권합니다");
    }

    /// <summary>배가 이 갈래를 쓸 수 있나 (누구 손이든) · 대략 몇 분.</summary>
    private (bool ok, string note, float minutes) ShipCan(Way way, WayCase k, Room? room)
    {
        var w = _w;
        var ship = w.Ship;
        if (way.Item is ItemKind it && ship.CountStored(it) < way.ItemCount) return (false, $"{ItemKinds.Name(it)} 없음", way.Minutes);
        if (way.Near.Length > 0 && way.Things.Length == 0 && !way.Near.Any(t => ship.FurnitureOf(t).Any())) return (false, $"{WaysRules.FromFurniture(way.Near[0]).name} 없음", way.Minutes);
        switch (way.Id)
        {
            case "fire.system": if (room == null || !room.Suppression || !room.Powered) return (false, "소화 장치 없음", 5); break;
            case "fire.robot": if (!w.Robots.Robots.Any(r => r.Operational && r.Foam > 0.1f)) return (false, "로봇 없음", 6); break;
            case "fire.vacuum": if (room == null || w.Policies["vacuumfire"] <= 0) return (false, "방침이 막았다", 6); break;
            case "fire.inert": if (room == null || w.Policies["inertfire"] <= 0) return (false, "방침이 막았다", 8); break;
            case "fire.seal" or "fire.letburn": if (room == null || room.Doors.Any(d => d.JammedOpen || d.Removed)) return (false, "문이 안 닫힌다", way.Minutes); break;
            case "breach.drone": if (!ship.FurnitureOf(FurnitureType.DroneDock).Any()) return (false, "드론 없음", 20); break;
            case "breach.robot" or "cargo.robot": if (!w.Robots.Robots.Any(r => r.Operational)) return (false, "로봇 없음", 4); break;
            case "trap.remote":
                if (k.DoorId < 0 || ship.Doors[k.DoorId] is not Door td || Stuck(td) && td.Bent > 0.2f || td.Welded && !td.Locked) return (false, "문틀이 휘어 잠금만 풀어선 안 된다", 1);
                break;
            case "trap.crawl": if (room == null || !CrawlOut(room).HasValue) return (false, "통로가 없다", 6); break;
            case "power.robotbat": if (!w.Robots.Robots.Any(r => r.Operational && r.AtDock)) return (false, "쉬는 로봇이 없다", 10); break;
            case "hurt.push": if (w.Matter.Gravity > 0.3f) return (false, "중력이 있다", 3); break;
        }
        return (true, "", way.Minutes);
    }

    /// <summary>컴퓨터 손으로 바로 하는 갈래 (잠금 해제 · 배기 · 스크러버 · 회로 끊기 · 추진 정지 · 출력).</summary>
    private void ComputerDo(WayCase k, Way way)
    {
        var t = new WayTry { Id = _nextTry++, CaseId = k.Id, WayId = way.Id, Chosen = _w.Tick, Why = k.ComputerWhy, State = 1 };
        t.Started = _w.Tick;
        Tries.Add(t);
        Stats.ComputerActs++;
    }

    private void TickComputerTries()
    {
        var w = _w;
        foreach (var t in Tries)
        {
            if (t.State != 1 || t.CrewId >= 0 || t.RobotId >= 0) continue;
            var way = t.Way;
            if (w.Tick - t.Started < SimTime.Minutes(way.Minutes)) continue;
            var k = Case(t.CaseId);
            var room = k == null ? null : RoomOf(k);
            bool ok = true;
            string res = "";
            switch (way.Fx)
            {
                case WayFx.RemoteOpen:
                    if (k != null && k.DoorId >= 0 && w.Ship.Doors[k.DoorId] is Door d && !(d.Welded && d.Bent > 0.2f)) { d.Welded = false; d.Locked = false; d.Request(); res = "잠금을 풀었다"; }
                    else { ok = false; res = "문이 꿈쩍도 않는다"; }
                    break;
                default:
                    if (room != null) ApplyGen(room, way, null);
                    res = way.Name;
                    break;
            }
            Finish(t, ok, res, null);
            if (room != null) w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: {room.Name} — {way.Name}{(ok ? "" : $" ({res})")}");
        }
    }

    // ───────────── 사람이 고른다 ─────────────

    private void Respond(WayCase k)
    {
        var w = _w;
        k.Rounds++;
        k.NextChoose = w.Tick + SimTime.Minutes(12);
        var room = RoomOf(k);
        int want = k.Snag is Snag.Fire or Snag.Breach or Snag.Trapped or Snag.Injured ? 2 : 1;
        var picked = new List<(CrewMember c, int d)>();
        foreach (var c in w.Crew)
        {
            if (!Free(c)) continue;
            bool subject = c.Id == k.CrewId;
            if (k.Snag == Snag.Injured && subject) continue;
            if (!subject && !Knows(c, k, room)) continue;
            int d = Math.Abs(c.Cell.X - k.At.X) + Math.Abs(c.Cell.Y - k.At.Y);
            if (subject) d = -1;
            picked.Add((c, d));
        }
        picked.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.c.Id.CompareTo(b.c.Id));
        int n = 0;
        foreach (var (c, _) in picked)
        {
            if (n >= want) break;
            var used = Tries.Where(t => t.CaseId == k.Id && t.State != 3 && t.Way.Fx != WayFx.Existing).Select(t => t.WayId).ToHashSet();
            var (way, why, score, defied) = Choose(c, k, room, used);
            if (way == null) continue;
            n++;
            Assign(c, k, way, why, score, defied);
        }
    }

    private bool Free(CrewMember c) =>
        c.CanAct && !c.Outside && !c.EvaMode && !c.IsChild && c.CarryingPerson == null && TryOf(c) == null && c.Job?.Activity is not (PanicActivity or EvacuateActivity);

    private bool Knows(CrewMember c, WayCase k, Room? room)
    {
        var w = _w;
        if (room != null && (c.Room == room || c.Room != null && Adjacent(c.Room, room))) return true;
        var bel = w.Brain2.Beliefs;
        return k.Snag switch
        {
            Snag.Fire => room != null && bel.Believes(c, Topic.Fire, room.Id),
            Snag.Breach => room != null && bel.Believes(c, Topic.Breach, room.Id),
            Snag.Injured => bel.Believes(c, Topic.Down, k.CrewId),
            Snag.Trapped or Snag.NoPart or Snag.Blackout => w.Automation.CoreOnline, // 방송 · 일감판으로 안다
            _ => room != null && Math.Abs(c.Cell.X - k.At.X) + Math.Abs(c.Cell.Y - k.At.Y) < 14,
        };
    }

    private static bool Adjacent(Room a, Room b)
    {
        foreach (var d in a.Doors) if (d.RoomA == b || d.RoomB == b) return true;
        return false;
    }

    public Room? RoomOf(WayCase k) => k.RoomId >= 0 && k.RoomId < _w.Ship.Rooms.Count ? _w.Ship.Rooms[k.RoomId] : null;

    /// <summary>그 사람 눈으로 갈래마다 따져 하나를 고른다 → (갈래 · 이유 · 점수 · 컴퓨터 안과 다른가).</summary>
    public (Way? way, string why, float score, bool defied) Choose(CrewMember c, WayCase k, Room? room, ICollection<string>? taken = null)
    {
        var w = _w;
        float urg = WaysTable.Urgency(k.Snag);
        float riskTol = 0.3f + 0.7f * c.Traits.Bravery;
        var pick = WaysTable.Get(k.ComputerPick);
        float faith = c.ComputerFaith < 0f ? 0.6f : c.ComputerFaith;
        Way? best = null;
        float bestS = float.MinValue;
        Avail bestA = default;
        var seen = new List<(Way way, float s, Avail a)>();
        bool subject = c.Id == k.CrewId;
        foreach (var way in WaysTable.Of(k.Snag))
        {
            if (way.By != WayBy.Crew) continue;
            if (taken != null && taken.Contains(way.Id) && way.Fx != WayFx.Stretcher) continue;
            if (Tries.Any(t => t.CaseId == k.Id && t.CrewId == c.Id && t.WayId == way.Id && t.State == 3 && !t.Ok)) continue; // 해 봤는데 안 됐다
            // 갇힌 본인은 안에서 할 수 있는 것만 · 밖의 사람은 갇힌 본인의 길(통로 · 두드리기)을 못 한다
            if (k.Snag == Snag.Trapped)
            {
                bool selfWay = way.Fx is WayFx.Crawl or WayFx.Signal;
                if (subject && !(selfWay || way.Fx == WayFx.Pry) || !subject && selfWay) continue;
            }
            var a = Check(c, way, k, room);
            if (!a.Ok) { seen.Add((way, float.MinValue, a)); continue; }
            float mins = a.Travel + way.Minutes * SpeedMul(c, way);
            float s = a.Est
                - way.Risk * (1f - riskTol) * 0.9f - a.Danger
                - urg * Math.Clamp(mins / 45f, 0f, 1f) * 0.5f
                - way.ShipCost * (0.25f + 0.4f * c.Traits.Diligence)
                + (way.Book ? 0.06f + 0.08f * c.Traits.Calm : 0f)
                - (way.Leave ? 0.1f + 0.3f * (1f - c.Traits.Bravery) : 0f);
            var p = PracticeOrNull(way.Id);
            if (p != null && p.Custom) s += 0.18f;
            else if (p != null) s += 0.03f * Math.Min(2, p.Ok) - 0.05f * Math.Min(2, p.Bad);
            var mine = Mine(c, way.Id);
            s += 0.06f * Math.Min(3, mine.ok);
            if (c.Lessons.Contains("ways:" + way.Id)) s -= 0.4f;
            if (pick != null && pick.Id == way.Id) s += 0.15f * faith;
            s += R.Range(-0.07f, 0.07f);
            seen.Add((way, s, a));
            if (s > bestS) { bestS = s; best = way; bestA = a; }
        }
        if (best == null) return (null, "", 0f, false);
        // 이유 (자연스러운 말로)
        string why;
        var bookBlocked = seen.FirstOrDefault(x => x.way.Book && x.s == float.MinValue);
        var p2 = PracticeOrNull(best.Id);
        var mine2 = Mine(c, best.Id);
        bool defied = false;
        if (pick != null && pick.Id != best.Id && (pick.By == WayBy.Crew || pick.Id is "fire.vacuum" or "breach.abandon" or "fire.letburn"))
        {
            defied = true;
            var mineEntry = seen.FirstOrDefault(x => x.way.Id == pick.Id);
            string because = mineEntry.way == null ? (k.CrewId >= 0 && k.CrewId != c.Id && pick.Id is "fire.vacuum" or "breach.abandon" ? $"{Ko.IGa(w.Crew.First(x => x.Id == k.CrewId).Name)} 안에 있다" : RoomOf(k) is Room rr && pick.Id is "fire.vacuum" or "breach.abandon" or "fire.letburn" ? $"그러면 {Ko.EulReul(rr.Name)} 잃는다" : "그럴 손이 없다")
                : mineEntry.s == float.MinValue ? mineEntry.a.Note.Length > 0 ? mineEntry.a.Note : "그건 지금 안 된다"
                : c.Lessons.Contains("ways:" + pick.Id) ? "그건 지난번에 안 됐다"
                : faith < 0.4f ? "컴퓨터 말은 못 믿겠다"
                : pick.Risk > best.Risk + 0.2f ? "그건 위험하다"
                : pick.Minutes > best.Minutes * 2f ? "그건 너무 오래 걸린다"
                : bestA.Thing.Length > 0 ? $"{Ko.IGa(bestA.Thing)} 바로 옆에 있다"
                : "이게 더 빠르다";
            why = $"컴퓨터는 {Ko.EulReul(pick.Name)} 권했지만 {because}";
        }
        else if (p2 is { Custom: true }) why = $"이 배에선 {WaysTable.Name(k.Snag)}{(Ko.EunNeun(WaysTable.Name(k.Snag))[^1..])} 이렇게 한다";
        else if (mine2.ok > 0) why = "전에 이렇게 해서 됐다";
        else if (!best.Book && bookBlocked.way != null) why = $"{bookBlocked.a.Note} — {(bestA.Thing.Length > 0 ? $"{Ko.EuRo(bestA.Thing)}" : best.Name)}";
        else if (!best.Book && best.Risk >= 0.35f && c.Traits.Bravery > 0.6f) why = $"급하다 — {(bestA.Thing.Length > 0 ? Ko.EuRo(bestA.Thing) : "손에 잡히는 걸로")}";
        else if (bestA.Why.Length > 0) why = bestA.Why;
        else why = best.Line.Length > 0 ? best.Line : best.Name;
        return (best, why, bestS, defied);
    }

    /// <summary>그 사람이 지금 이 갈래를 쓸 수 있나 (물건 · 재질 · 솜씨 · 장소) · 얼마나 될 것 같나 · 몸이 얼마나 위험한가.</summary>
    public readonly record struct Avail(bool Ok, float Est, float Travel, string Thing, string Note, string Why, float Danger, int ArticleId = -1, int FurnId = -1, Material Mat = Material.None, float Bulk = 0f);

    public Avail Check(CrewMember c, Way way, WayCase k, Room? room)
    {
        var w = _w;
        var ship = w.Ship;
        var at = k.At;
        static Avail No(string why) => new(false, 0f, 0f, "", why, "", 0f);
        if (c.SkillLevel(way.Skill) < way.SkillMin) return No("솜씨가 모자란다");
        if (way.Item is ItemKind it && ship.CountStored(it) < way.ItemCount && !(c.Carrying?.Kind == it))
        {
            if (way.Id == "fire.ext") return No("소화기가 없다");
            return No($"{ItemKinds.Name(it)}{(Ko.IGa(ItemKinds.Name(it))[^1..])} 없다");
        }
        if (way.Id == "fire.ext" && w.Brain2.Beliefs.WhereItem(c, ItemKind.Extinguisher, out _, out bool none) == null && none && c.Carrying?.Kind != ItemKind.Extinguisher) return No("소화기가 없는 줄 안다");
        float est = way.Power, danger = 0f;
        string thing = "", why = "";
        int art = -1, furn = -1;
        Material mat = Material.None;
        float bulk = 0f, travel = Manhattan(c.Cell, at) / 3f;
        // 근처 물건 · 설비
        bool inside = k.Snag == Snag.Trapped && c.Id == k.CrewId; // 갇힌 본인은 방 안의 것만
        if (way.Things.Length > 0 || way.Near.Length > 0)
        {
            Article? best = null; int bd = int.MaxValue;
            if (way.Things.Length > 0)
                foreach (var t in w.Matter.Things)
                {
                    if (!t.Loose || t.Ruined || t.Stage >= BreakStage.Broken || Array.IndexOf(way.Things, t.Kind) < 0 || t.ClaimedBy >= 0 && t.ClaimedBy != c.Id) continue;
                    if (t.Kind == ArticleKind.WaterJug && t.Contents < 1f) continue;
                    if (inside && ship.RoomAt(t.At) != room) continue;
                    int d = Manhattan(c.Cell, t.At) + Manhattan(t.At, at);
                    if (d < bd && d < 40) { bd = d; best = t; }
                }
            Furniture? bf = null; int fd = int.MaxValue;
            if (way.Near.Length > 0)
                foreach (var f in ship.Furniture)
                {
                    if (f.Stowed || f.Room.Detached || f.Room.Abandoned || Array.IndexOf(way.Near, f.Type) < 0 || Taken(f)) continue;
                    if (f.Type is FurnitureType.Bed or FurnitureType.Cot or FurnitureType.MedBed && (f.ReservedBy != null && f.ReservedBy.Down)) continue; // 누가 누운 침대
                    if (inside && f.Room != room) continue;
                    int d = Manhattan(c.Cell, f.Cells[0]) + Manhattan(f.Cells[0], at);
                    if (d < fd && d < 46) { fd = d; bf = f; }
                }
            if (best == null && bf == null) return No(way.Near.Length > 0 ? $"{WaysRules.FromFurniture(way.Near[0]).name}{(Ko.IGa(WaysRules.FromFurniture(way.Near[0]).name)[^1..])} 근처에 없다" : $"{ArticleSpecs.Of(way.Things[0]).Name}{(Ko.IGa(ArticleSpecs.Of(way.Things[0]).Name)[^1..])} 근처에 없다");
            if (best != null && (bf == null || bd <= fd))
            {
                art = best.Id; thing = best.Name; mat = best.Mat; bulk = WaysRules.Bulk(best.Kind); travel = bd / 3f;
                if (best.Kind == ArticleKind.Towel && best.WetFrac > 0.3f) thing = "젖은 수건";
            }
            else
            {
                furn = bf!.Id; var fi = WaysRules.FromFurniture(bf.Type); thing = fi.name; mat = fi.mat; bulk = fi.bulk; travel = fd / 3f;
            }
        }
        var wall = k.Snag == Snag.Breach ? ship.WallAt(at) : null;
        switch (way.Fx)
        {
            case WayFx.Existing:
                if (way.Id == "breach.sealant" && wall != null && ship.CountStored(ItemKind.Sealant) < Hull.SealantFor(wall)) return No("실링폼이 모자란다");
                if (way.Id == "breach.weld" && ship.CountStored(ItemKind.Sealant) >= 1) return No("실링폼이 있으면 그걸로");
                if (way.Id == "fire.ext")
                {
                    // 소화기가 어디 있나 — 멀면 늦다
                    var box = ship.Furniture.Where(f => f.Storage is Inventory inv && inv.Count(ItemKind.Extinguisher) > 0 && !f.Room.Detached).OrderBy(f => Manhattan(c.Cell, f.Cells[0])).FirstOrDefault();
                    if (box == null && c.Carrying?.Kind != ItemKind.Extinguisher) return No("소화기가 없다");
                    if (box != null) travel = (Manhattan(c.Cell, box.Cells[0]) + Manhattan(box.Cells[0], at)) / 3f;
                    if (travel > 14f) why = "멀어도 소화기가 확실하다";
                }
                if (way.Order == WorkKind.Rescue && room != null && room.Unbreathable && c.Suit == null) danger += 0.2f;
                break;
            case WayFx.Seal or WayFx.LetBurn:
            {
                if (room == null || room.Doors.Any(d => d.JammedOpen || d.Removed)) return No("문이 안 닫힌다");
                int believed = w.Crew.Count(o => !o.Dead && o != c && o.Room == room && (o.Down || o.Job?.Order?.Kind != WorkKind.Extinguish));
                if (believed > 0) { danger += 0.45f * believed; why = "안에 사람이 있다"; }
                int cells = w.Fire.CountIn(room);
                est = way.Fx == WayFx.Seal ? 0.72f - 0.02f * cells + (room.Volume < 40f ? 0.08f : 0f) : 0.6f;
                if (!room.VentOpen) est += 0.05f;
                break;
            }
            case WayFx.Douse or WayFx.CutDouse:
            {
                if (room == null) return No("");
                int cells = w.Fire.CountIn(room);
                est = (furn >= 0 ? 0.75f : 0.55f) - 0.05f * MathF.Max(0, cells - 2);
                float live = WaysRules.LiveNear(w, room, at);
                bool knows = c.SkillLevel(Skill.Electrical) >= 0.3f || c.Lessons.Contains("ways:fire.jug") || c.Lessons.Contains("ways:fire.hydro");
                if (way.Fx == WayFx.Douse && live > 0f && knows) { danger += 0.6f; why = "전기 불엔 물이 안 된다"; }
                if (way.Fx == WayFx.CutDouse && live <= 0f) est -= 0.25f; // 전기 불이 아니면 굳이
                break;
            }
            case WayFx.Smother:
            {
                if (room == null) return No("");
                int cells = w.Fire.CountIn(room);
                if (cells > 3) return No("천으로 덮기엔 너무 번졌다");
                float inten = w.Fire.At(at);
                float wet = art >= 0 && w.Matter.Get(art) is Article a ? a.WetFrac : 0f;
                est = WaysRules.SmotherChance(mat == Material.None ? Material.Fabric : mat, wet, inten, cells) + (furn >= 0 && ship.Furniture.First(f => f.Id == furn).Type == FurnitureType.FireBlanket ? 0.25f : 0f);
                break;
            }
            case WayFx.Eject:
            {
                if (room == null || w.Fire.CountIn(room) > 2) return No("한 군데가 아니다");
                Article? burning = null;
                foreach (var d in Cell.Dirs8.Append(new Cell(0, 0))) foreach (var t in w.Matter.At(at + d)) if (t.Loose && t.Mass < 12f && (t.Smolder || t.Flammable)) { burning = t; break; }
                if (burning == null) return No("들고 나갈 게 없다");
                art = burning.Id; thing = burning.Name; mat = burning.Mat;
                est = 0.7f;
                break;
            }
            case WayFx.Plug:
            {
                if (wall == null) return No("");
                if (way.Id == "breach.pot" && wall.Breach >= 0.25f) return No("냄비로는 구멍이 크다");
                est = WaysRules.PlugQuality(mat, bulk, wall.Breach);
                if (est < 0.3f) return No($"{thing}{(Ko.EunNeun(thing)[^1..])} 빨려 나간다");
                break;
            }
            case WayFx.Freeze:
                est = wall != null && wall.Breach >= 0.25f ? 0.45f : 0.62f;
                break;
            case WayFx.Brace:
                if (wall == null || wall.Breach >= 0.3f) return No("몸으로 막기엔 크다");
                danger += c.Suit == null ? 0.15f : 0f;
                est = 0.5f;
                break;
            case WayFx.Crawl:
                if (room == null || CrawlOut(room) is null) return No("통로가 없다");
                if (c.Vitals.Injury > 0.4f || c.Carrying is { Count: > 4 }) return No("다쳐서 못 기어간다");
                est = 0.85f;
                break;
            case WayFx.Pry or WayFx.Cut or WayFx.Bypass or WayFx.Blow:
            {
                if (k.DoorId < 0 || k.DoorId >= ship.Doors.Count) return No("");
                var door = ship.Doors[k.DoorId];
                if (way.Fx == WayFx.Pry) { est = 0.5f + 0.25f * c.Fitness + 0.2f * door.Bent; if (art < 0 && furn < 0) return No("쇠지레가 없다"); }
                if (way.Fx == WayFx.Bypass && door.Powered && !door.MotorBroken) return No("모터는 멀쩡하다 — 문틀이 문제다");
                if (way.Fx == WayFx.Blow)
                {
                    if (c.Traits.Bravery < 0.55f) return No("그럴 배짱이 없다");
                    int within = w.Crew.Count(o => !o.Dead && o.Room == room);
                    danger += 0.2f * within;
                    var kc = w.Crew.FirstOrDefault(x => x.Id == k.CrewId);
                    if (kc != null && room != null && Atmosphere.Danger(room) < 0.3f) danger += 0.4f; // 안이 아직 괜찮으면 그럴 일까진
                }
                break;
            }
            case WayFx.Signal:
                if (k.Snag == Snag.Trapped && c.Id != k.CrewId) return No("");
                break;
            case WayFx.RobotBattery:
                if (!w.Robots.Robots.Any(r => r.Operational && r.AtDock && !r.Disabled)) return No("쉬는 로봇이 없다");
                break;
            case WayFx.Pedal:
                if (c.Needs.Rest < 0.35f) return No("힘이 없다");
                break;
            case WayFx.Stretcher:
            {
                var other = w.Crew.FirstOrDefault(o => o != c && Free(o) && Manhattan(o.Cell, c.Cell) < 16 && o.Id != k.CrewId);
                if (other == null) return No("같이 들 사람이 없다");
                est = 0.85f;
                why = $"{Ko.WaGwa(other.Name)} 둘이 들면 흔들리지 않는다";
                break;
            }
            case WayFx.Push:
                if (w.Matter.Gravity > 0.3f) return No("중력이 있다");
                break;
            case WayFx.TreatHere or WayFx.Guided:
            {
                var pt = w.Crew.FirstOrDefault(x => x.Id == k.CrewId);
                if (pt == null) return No("");
                est = way.Fx == WayFx.TreatHere ? 0.45f + 0.45f * c.SkillLevel(Skill.Medicine) : w.Automation.CoreOnline ? 0.65f : 0f;
                if (est <= 0f) return No("컴퓨터가 말이 없다");
                if (pt.Room != null && (pt.Room.Unbreathable || w.Fire.CountIn(pt.Room) > 0)) { est -= 0.4f; why = ""; } // 그 자리가 위험하면 옮겨야
                else if (way.Fx == WayFx.TreatHere) why = "옮기다 더 다친다 — 여기서";
                break;
            }
            case WayFx.Strip:
            {
                if (k.Part is not ItemKind part || w.Policies["cannibalize"] == 0) return No("뜯지 말라는 방침");
                var m = MachineById(k.FurnId);
                var donor = Adaptation.BestDonor(w, part, m != null ? Adaptation.Worth(m, asDonor: false) : 4f, m != null ? new[] { m } : Array.Empty<Machine>(), anything: w.Policies["cannibalize"] == 2);
                if (donor == null) return No("떼어 올 설비가 없다");
                furn = donor.Body.Id; thing = donor.Name;
                travel = (Manhattan(c.Cell, donor.Body.Cells[0]) + Manhattan(donor.Body.Cells[0], at)) / 3f;
                why = $"{Ko.EunNeun(donor.Name)} 당장 안 써도 된다";
                break;
            }
            case WayFx.Improvise:
                if (k.Snag == Snag.NoPart && (k.Part is not ItemKind ip || WaysRules.Improvised(ip) is not string iname)) return No("그건 손으로 못 만든다");
                if (k.Snag == Snag.NoPart) thing = WaysRules.Improvised(k.Part!.Value)!;
                break;
            case WayFx.Lathe or WayFx.Recycle:
                if (k.Part is ItemKind lp && ItemKinds.Tier(lp) == ItemTier.Advanced) return No("그건 깎아서 안 된다");
                break;
        }
        return new Avail(true, Math.Clamp(est, 0f, 1f), travel, thing, "", why, danger, art, furn, mat, bulk);
    }

    private bool Taken(Furniture f)
    {
        foreach (var t in Tries) if (t.State is 0 or 1 && t.Thing == f.Name && _furnOf.TryGetValue(t.Id, out var id) && id == f.Id) return true;
        return false;
    }

    private readonly Dictionary<int, int> _furnOf = new();
    private readonly Dictionary<int, int> _artOf = new();
    public int FurnOf(WayTry t) => _furnOf.TryGetValue(t.Id, out var v) ? v : -1;
    public int ArticleOf(WayTry t) => _artOf.TryGetValue(t.Id, out var v) ? v : -1;

    public Machine? MachineById(int furnId) { foreach (var f in _w.Ship.Furniture) if (f.Id == furnId) return f.Machine; return null; }
    public Furniture? FurnById(int furnId) { foreach (var f in _w.Ship.Furniture) if (f.Id == furnId) return f; return null; }

    /// <summary>방 안에서 열린 정비 통로로 나가는 칸 (건너편 방의 빈 바닥).</summary>
    public Cell? CrawlOut(Room r)
    {
        var w = _w;
        foreach (var wb in w.Body.WallList)
        {
            if (!wb.Crawl || !wb.CrawlOpen || wb.CrawlA != r.Id && wb.CrawlB != r.Id) continue;
            foreach (var d in Cell.Dirs4)
            {
                var o = wb.Cell + d;
                if (w.Ship.RoomAt(o) is Room or && or != r && w.Ship.IsOpenFloor(o)) return o;
            }
        }
        return null;
    }

    private static int Manhattan(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private void Assign(CrewMember c, WayCase k, Way way, string why, float score, bool defied)
    {
        var w = _w;
        var a = Check(c, way, k, RoomOf(k));
        var t = new WayTry
        {
            Id = _nextTry++, CaseId = k.Id, WayId = way.Id, CrewId = c.Id, Why = why, Suggested = k.ComputerPick, Defied = defied, Chosen = w.Tick, Score = score,
            State = way.Fx == WayFx.Existing ? 2 : 0, Thing = a.Thing,
        };
        if (a.FurnId >= 0) _furnOf[t.Id] = a.FurnId;
        if (a.ArticleId >= 0) _artOf[t.Id] = a.ArticleId;
        Tries.Add(t);
        if (Tries.Count > 240) Tries.RemoveAll(x => x.State == 3 && Tries.IndexOf(x) < 60);
        Stats.Chosen++;
        if (defied)
        {
            Stats.Defied++;
            w.Log.Add(w.Tick, LogKind.Life, $"{why} — {Ko.EulReul(way.Name)} 골랐다", c.Id);
            MarkLog.Add(c.Memory.Marks, w.Tick, $"컴퓨터 안 대신 {way.Name}: {why}");
            if (w.Automation.CoreOnline) w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: {Ko.IGa(c.Name)} 다른 길을 골랐습니다 — {way.Name}. 지켜보겠습니다");
        }
        c.Say(w, way.Line.Length > 0 && !defied ? way.Line : why);
        if (way.Fx != WayFx.Existing) c.Interrupt(w);
        // 들것: 옆 사람을 부른다
        if (way.Fx == WayFx.Stretcher && w.Crew.FirstOrDefault(o => o != c && Free(o) && Manhattan(o.Cell, c.Cell) < 16 && o.Id != k.CrewId) is CrewMember other)
        {
            Tries.Add(new WayTry { Id = _nextTry++, CaseId = k.Id, WayId = way.Id, CrewId = other.Id, Why = $"{Ko.IGa(c.Name)} 같이 들자고 했다", Chosen = w.Tick, Helper = true, State = 0 });
            other.Interrupt(w);
        }
    }

    /// <summary>시험 · 회의용: 그 사람에게 이 갈래를 맡긴다.</summary>
    public WayTry? Force(CrewMember c, WayCase k, string wayId, string why = "")
    {
        if (WaysTable.Get(wayId) is not Way way) return null;
        foreach (var t in Tries) if (t.CrewId == c.Id && t.State is 0 or 1) { t.State = 3; t.Result = "다른 일로"; }
        Assign(c, k, way, why.Length > 0 ? why : way.Line, 1f, false);
        return Tries[^1].CrewId == c.Id ? Tries[^1] : Tries.LastOrDefault(t => t.CrewId == c.Id);
    }

    /// <summary>시험용: 사고를 지금 찾는다 (다음 훑기를 기다리지 않고).</summary>
    public void ScanNow() => Scan();

    public WayCase? CaseFor(Snag s, Room? room = null) => Cases.LastOrDefault(k => k.Snag == s && (room == null || k.RoomId == room.Id));

    // ───────────── 끝 · 배움 ─────────────

    /// <summary>갈래 하나가 끝났다: 결과 · 부작용 기록 · 관행 · 교훈 · 연대기.</summary>
    public void Finish(WayTry t, bool ok, string result, CrewMember? c)
    {
        var w = _w;
        if (t.State == 3) return;
        t.State = 3;
        t.Ended = w.Tick;
        t.Ok = ok;
        t.Result = result;
        var way = t.Way;
        var k = Case(t.CaseId);
        float min = (w.Tick - (t.Started >= 0 ? t.Started : t.Chosen)) / (float)SimTime.Minutes(1);
        if (t.Helper) return;
        var p = Practice(way.Id);
        p.Uses++;
        if (ok) { p.Ok++; Stats.Done++; } else { p.Bad++; Stats.Failed++; }
        p.LastMinutes = min;
        if (ok) p.BestMinutes = MathF.Min(p.BestMinutes, min);
        if (c != null) Bump(c, way.Id, ok);
        if (k != null)
        {
            if (ok && k.SolvedBy.Length == 0) { k.SolvedBy = way.Id; k.SolvedWho = c?.Id ?? -1; }
            AddPast(k, way.Id, ok, min, c?.Id ?? -1);
        }
        var room = k == null ? null : RoomOf(k);
        string where = room?.Name ?? "배";
        foreach (var m in Marks) if (m.Try == t.Id) { m.Active = false; m.Ok = ok; if (m.Until < 0 && !Persistent(m.Look)) m.Until = w.Tick + SimTime.Hours(8); }
        if (c == null) return;
        if (ok)
        {
            c.Stats.Emergencies++;
            w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(c.Name)} {where} {WaysTable.Name(way.Snag)} — {way.Name}{(t.Thing.Length > 0 && !way.Name.Contains(t.Thing) ? $" ({t.Thing})" : "")}{(result.Length > 0 ? $" · {result}" : "")}", room, new[] { c });
            // 관행: 두 번 넘게 잘 통했고 실패보다 훨씬 많으면 이 배의 방식이 된다
            if (!p.Custom && p.Ok >= 2 && p.Ok > p.Bad * 2 && !way.Book)
            {
                p.Custom = true;
                p.Since = w.Tick;
                p.Origin = $"{SimTime.Day(w.Tick)}일 {Ko.IGa(c.Name)} {where}에서";
                Stats.Customs++;
                w.Log.Add(w.Tick, LogKind.Life, $"이 배에선 {WaysTable.Name(way.Snag)}{(Ko.IGa(WaysTable.Name(way.Snag))[^1..])} 나면 {way.Name} — {p.Origin} 처음 통한 뒤로 다들 그렇게 한다", c.Id);
                w.History.Add(w, HistoryKind.Lesson, $"{WaysTable.Name(way.Snag)}{(Ko.IGa(WaysTable.Name(way.Snag))[^1..])} 나면 {Ko.EulReul(way.Name)} 먼저 — 이 배의 방식이 됐다", room, new[] { c });
            }
            // 옆에서 본 사람도 조금 배운다
            foreach (var o in w.Crew) if (o != c && !o.Dead && o.Room == c.Room && o.CanAct && R.Chance(0.5f)) { var key = MineKey(o, way.Id); var v = _mine.TryGetValue(key, out var x) ? x : (0, 0); if (v.Item1 == 0) _mine[key] = (1, v.Item2); }
        }
        else
        {
            // 교훈: 망한 갈래는 그 사람이 다시 고르지 않는다 · 일기 · 기억
            c.Lessons.Add("ways:" + way.Id);
            string lesson = result.Length > 0 ? $"{way.Name} — {result}" : $"{way.Name} — 안 됐다";
            c.Diary.Add((w.Tick, $"{where}에서 {lesson}. 다음엔 안 한다."));
            MarkLog.Add(c.Memory.Marks, w.Tick, lesson);
            w.History.Add(w, HistoryKind.Lesson, $"{Ko.IGa(c.Name)} {where}에서 {lesson}", room, new[] { c });
            Memory.Shake(w, c, 0.04f, lesson);
        }
    }

    private static bool Persistent(WayLook l) => l is WayLook.MattressPlug or WayLook.TablePlug or WayLook.PotPlug or WayLook.CratePlug or WayLook.MatPlug or WayLook.FrostPlug or WayLook.PriedDoor or WayLook.CutDoor or WayLook.BlownDoor or WayLook.WallHole or WayLook.Stripped or WayLook.RobotBattery or WayLook.Jumper or WayLook.TapeHose or WayLook.Strap or WayLook.Wedge or WayLook.Burnt;

    // ───────────── 부작용 · 나중 일 ─────────────

    private void TickPlugs(float dt)
    {
        var w = _w;
        foreach (var p in Plugs)
        {
            if (p.Gone) continue;
            if (w.Ship.WallAt(p.Wall) is not WallState ws || !ws.Patched || ws.Breach <= 0f) { p.Gone = true; continue; }
            var room = Hull.InsideRoom(w.Ship, p.Wall);
            ws.PatchQuality -= p.Decay * dt * (room != null && room.Air.Temperature < 5f && p.Mat == Material.Ice ? 0.1f : 1f);
            foreach (var m in Marks) if (m.At == p.Wall && Persistent(m.Look)) m.Q = Math.Clamp(ws.PatchQuality, 0f, 1f);
            if (ws.PatchQuality < 0.3f)
            {
                // 여기서 떨어뜨린다 (선체 틱이 같은 말을 하기 전에 — 무엇이 떨어졌는지)
                ws.Patched = false;
                p.Gone = true;
                Stats.PlugsFell++;
                w.RaiseAlert($"{room?.Name ?? "선체"} 구멍에 댄 {Ko.IGa(p.Name)} 떨어져 나갔다", room, AlertLevel.Critical, shipWide: true);
                MarkLog.Add(ws.Marks, w.Tick, $"{p.Name} 마개가 떨어져 나갔다");
                w.History.Add(w, HistoryKind.Damage, $"{room?.Name ?? "선체"} 구멍에 댄 {Ko.IGa(p.Name)} 떨어져 나갔다", room, at: p.Wall);
                foreach (var m in Marks) if (m.At == p.Wall && Persistent(m.Look)) { m.Ok = false; m.Until = w.Tick + SimTime.Hours(2); }
                w.Board.RequestScan();
            }
        }
        Plugs.RemoveAll(p => p.Gone && w.Tick - p.Since > SimTime.Hours(24));
    }

    private void TickSeals()
    {
        var w = _w;
        for (int i = _seals.Count - 1; i >= 0; i--)
        {
            var s = _seals[i];
            if (s.room >= w.Ship.Rooms.Count) { _seals.RemoveAt(i); continue; }
            var room = w.Ship.Rooms[s.room];
            var t = Tries.FirstOrDefault(x => x.Id == s.tryId);
            bool burning = w.Fire.Count > 0 && w.Fire.CountIn(room) > 0;
            if (!burning)
            {
                if (s.outSince < 0) { _seals[i] = s with { outSince = w.Tick }; continue; }
                if (w.Tick - s.outSince < SimTime.Minutes(5)) continue;
                Unseal(room);
                _seals.RemoveAt(i);
                var by = t == null ? null : w.Crew.FirstOrDefault(x => x.Id == t.CrewId);
                if (t != null && t.State != 3)
                {
                    bool purged = room.Purging || room.Inerting;
                    Finish(t, !purged, purged ? "컴퓨터가 먼저 공기를 뺐다" : $"숨이 끊긴 불이 꺼졌다 ({(w.Tick - s.since) / (float)SimTime.Minutes(1):0}분)", by);
                }
                if (room.Air.Smoke > 0.3f) AddFollow(new WayFollow { Id = _nextFollow++, WayId = t?.WayId ?? "fire.seal", Text = $"{room.Name} 연기 빼기", Kind = 6, RoomId = room.Id, At = room.Cells[0], Since = w.Tick });
                continue;
            }
            if (s.outSince >= 0) _seals[i] = s with { outSince = -1 };
            if (w.Tick - s.since > SimTime.Hours(4) && t != null && t.State != 3)
            {
                Finish(t, false, "문을 닫아도 꺼지지 않았다 — 어디선가 공기가 든다", w.Crew.FirstOrDefault(x => x.Id == t.CrewId));
            }
        }
    }

    internal void Seal(Room room, WayTry t, bool letBurn)
    {
        var w = _w;
        room.VentOpen = false;
        room.Lockdown = true;
        foreach (var d in room.Doors) if (!d.IsExternal && !d.Removed && !d.JammedOpen) d.Locked = true;
        _seals.RemoveAll(x => x.room == room.Id);
        _seals.Add((room.Id, t.Id, w.Tick, -1, letBurn));
        t.State = 2;
        MarkLog.Add(room.Marks, w.Tick, letBurn ? "타게 두고 문을 잠갔다" : "문을 닫아 숨을 끊었다");
    }

    private void Unseal(Room room)
    {
        if (room.Abandoned) return;
        room.Lockdown = false;
        room.VentOpen = true;
        foreach (var d in room.Doors) if (d.Locked && !d.Welded && !d.IsExternal) d.Locked = false;
        _w.Log.Add(_w.Tick, LogKind.Work, $"{room.Name} 불이 꺼졌다 — 문을 다시 연다");
    }

    public bool Sealed(Room room) { foreach (var s in _seals) if (s.room == room.Id) return true; return false; }

    private void TickLamps()
    {
        var w = _w;
        for (int i = _lamps.Count - 1; i >= 0; i--)
        {
            var (rid, until, set) = _lamps[i];
            if (rid >= w.Ship.Rooms.Count) { _lamps.RemoveAt(i); continue; }
            var r = w.Ship.Rooms[rid];
            if (w.Tick > until || r.Powered && !r.LightsOut)
            {
                if (set && r.PortableLit == 1) r.PortableLit = 0;
                _lamps.RemoveAt(i);
                continue;
            }
            if (r.PortableLit == 0) { r.PortableLit = 1; _lamps[i] = (rid, until, true); }
        }
    }

    internal void Lamp(Room r, float hours)
    {
        _lamps.RemoveAll(x => x.room == r.Id);
        bool set = r.PortableLit == 0;
        if (set) r.PortableLit = 1;
        _lamps.Add((r.Id, _w.Tick + SimTime.Hours(hours), set));
    }

    public bool LampLit(Room r) { foreach (var l in _lamps) if (l.room == r.Id) return true; return false; }

    internal void AddFollow(WayFollow f)
    {
        Follows.Add(f);
        Stats.Follows++;
        if (Follows.Count > 80) Follows.RemoveAll(x => x.Done >= 0 && Follows.IndexOf(x) < 30);
    }

    internal void AddPlug(WayPlug p) => Plugs.Add(p);

    internal void AddMark(WayMark m)
    {
        Marks.Add(m);
        if (Marks.Count > 120) Marks.RemoveAt(0);
    }

    internal void NoteStrip(Machine donor, int by) => _stripped.Add((donor.Body.Id, _w.Tick, by, false));

    /// <summary>떼어 간 설비가 멈춘 것을 누군가 알아챈다 (그 방에 들른 사람 · 컴퓨터).</summary>
    private void Notice()
    {
        var w = _w;
        // 기존 수순이 뜯은 것도 줍는다
        foreach (var m in w.Ship.Machines)
        {
            if (m.Faults.Count == 0 || !m.Has(FaultKind.Stripped)) continue;
            int id = m.Body.Id;
            bool known = false;
            foreach (var s in _stripped) if (s.furn == id) { known = true; break; }
            if (!known) _stripped.Add((id, w.Tick, -1, false));
        }
        for (int i = 0; i < _stripped.Count; i++)
        {
            var s = _stripped[i];
            if (s.noticed) continue;
            var m = MachineById(s.furn);
            if (m == null || !m.Has(FaultKind.Stripped)) { _stripped[i] = s with { noticed = true }; continue; }
            if (w.Tick - s.since < SimTime.Minutes(10)) continue;
            CrewMember? who = null;
            foreach (var c in w.Crew)
                if (c.CanAct && c.Id != s.by && c.Room == m.Body.Room && c.Pose != Pose.Sleeping) { who = c; break; }
            if (who == null) continue;
            _stripped[i] = s with { noticed = true };
            Stats.Noticed++;
            var part = m.Faults.FirstOrDefault(f => f.Kind == FaultKind.Stripped)?.Part;
            var by = w.Crew.FirstOrDefault(x => x.Id == s.by);
            string line = $"어? {Ko.IGa(m.Name)} 꺼져 있네 — 속에 {(part is ItemKind pk ? ItemKinds.Name(pk) : "부품")}{(part is ItemKind pk2 ? (Ko.IGa(ItemKinds.Name(pk2))[^1..]) : "이")} 없다";
            who.Say(w, line);
            w.Log.Add(w.Tick, LogKind.Warning, $"{line}{(by != null ? $" ({Ko.IGa(by.Name)} 떼어 갔다)" : "")}", who.Id);
            MarkLog.Add(m.Marks, w.Tick, $"{who.Name}: 멈춘 걸 알아챔");
            who.Needs.Stress = MathF.Min(1f, who.Needs.Stress + 0.03f);
            if (by != null && who.Stations.Contains(m.Body.Room.Kind)) { who.ChangeAffinity(by, -0.04f); MarkLog.Add(who.Memory.Marks, w.Tick, $"{Ko.IGa(by.Name)} 내 {Ko.EulReul(m.Name)} 뜯어 갔다"); }
            if (w.Automation.CoreOnline) w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: {m.Name} 멈춤 확인 — {(part is ItemKind p3 ? ItemKinds.Name(p3) : "부품")}을 떼어 간 자리입니다. 새 부품이 생기면 돌려 달아야 합니다");
        }
        if (_stripped.Count > 40) _stripped.RemoveAll(x => x.noticed);
    }

    public bool Noticed(int furnId) { foreach (var s in _stripped) if (s.furn == furnId) return s.noticed; return false; }

    private void Expire()
    {
        var w = _w;
        Marks.RemoveAll(m => m.Until >= 0 && w.Tick > m.Until);
        // 너무 오래 걸린 갈래는 놓는다 (못 했다)
        foreach (var t in Tries)
        {
            if (t.State is not (0 or 1) || t.CrewId < 0) continue;
            var way = t.Way;
            if (w.Tick - t.Chosen > SimTime.Minutes(way.Minutes * 3f + 40f))
            {
                var c = w.Crew.FirstOrDefault(x => x.Id == t.CrewId);
                if (t.Helper) { t.State = 3; continue; }
                Finish(t, false, "끝내 손을 못 댔다", c);
            }
            else if (w.Crew.FirstOrDefault(x => x.Id == t.CrewId) is CrewMember c2 && !c2.CanAct) { t.State = 3; t.Result = "쓰러졌다"; }
        }
    }

    // ───────────── 효과 (단순) ─────────────

    internal void ApplyGen(Room room, Way way, CrewMember? c)
    {
        var w = _w;
        var g = way.Gen;
        var air = room.Air;
        if (g.O2 != 0f) air.O2 = Math.Clamp(air.O2 + g.O2 * MathF.Min(2f, 60f / MathF.Max(20f, room.Volume)), 0f, 30f);
        if (g.Smoke != 1f) air.Smoke *= g.Smoke;
        if (g.Toxin != 1f) { air.Toxin *= g.Toxin; air.CO *= g.Toxin; }
        if (g.Temp != 0f) air.Temperature += g.Temp;
        if (g.Water != 0f) w.Water.Level = MathF.Max(0f, w.Water.Level + g.Water);
        if (g.Kwh != 0f) w.Power.BatteryCharge = Math.Clamp(w.Power.BatteryCharge + g.Kwh, 0f, MathF.Max(w.Power.BatteryCharge, w.Power.BatteryCapacity));
        if (g.Flood != 1f) room.Flood *= g.Flood;
        if (g.Close) foreach (var d in room.Doors) if (!d.IsExternal && !d.JammedOpen && !d.Removed) d.Openness = MathF.Min(d.Openness, 0.1f);
        if (g.Vent) room.VentOpen = true;
        if (g.LightHours > 0f) Lamp(room, g.LightHours);
        if (g.Rest != 0f || g.Stress != 0f || g.Food != 0f)
            foreach (var o in w.Crew)
            {
                if (o.Dead || o.Room != room) continue;
                o.Needs.Rest = Math.Clamp(o.Needs.Rest + g.Rest, 0f, 1f);
                o.Needs.Stress = Math.Clamp(o.Needs.Stress + g.Stress, 0f, 1f);
                o.Needs.Food = Math.Clamp(o.Needs.Food + g.Food, 0f, 1f);
            }
        if (g.Injury > 0f && c != null) NeedsSystem.AddInjury(c.Vitals, g.Injury, way.Name);
    }

    // ───────────── 지문 ─────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Cases.Count); I(Tries.Count); I(Stats.Chosen); I(Stats.Done); I(Stats.Failed); I(Stats.Customs); I(Stats.PlugsFell); I(Stats.Noticed);
        foreach (var t in Tries) { I(t.Id); I(t.State); I(t.CrewId); I(WaysTable.Index(t.WayId)); }
        foreach (var p in Practices) { I(p.Uses); I(p.Ok); I(p.Bad); }
        foreach (var p in Plugs) F(p.Decay);
        I(Follows.Count(f => f.Done >= 0));
    }
}
