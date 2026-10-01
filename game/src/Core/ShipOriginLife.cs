using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.9 배의 내력을 사람과 주컴퓨터가 읽는다 (같은 규칙이 모든 새 배에 — 예전 배는 아무것도 바뀌지 않는다).
//  · 주컴퓨터: 시작 상태 · 부품 내력(떼어 온 부품 · 돈 시간) · 마모로 고장 위험 순위를 매겨 정비 순서를 바꾼다 (Early → 작업 목록).
//              민간 설계의 단일 고장점 · 막힌 구역의 공기 · 갈라진 배의 쪽마다 없는 것을 읽고 경고 · 제안한다.
//  · 승무원: 닳은 배(고물 · 버려졌던 배 · 전쟁 상흔)를 알아채고 아침마다 한 바퀴 — 걱정되는 설비에 귀를 대고 조인다 (ShipRoundsActivity).
//            그게 쌓이고 고장을 여러 번 겪으면 "소리부터 듣는다"는 정비 문화가 된다 → 전조를 더 잘 듣는다 (Prevention) → 고장이 준다.
//  · 좁은 배: 비켜서기가 잦다 — 자꾸 마주치는 둘은 친하면 웃고, 사이가 나쁘면 짜증이 쌓인다 (관계가 좁음을 겪는 방식을 바꾼다).
//  · 갈라진 배: 걱정되는 사람은 통신기로 건너편을 부른다 (걱정이 풀리고 · 사이가 가까워진다).
//  · 설계사 × 불: 군용 내장재는 덜 타고(격벽 · 내화) 개척민이 손으로 댄 내장재는 잘 탄다 (Fire가 FireMul을 읽는다).
//  · 배 본체: 닳은 배는 바닥이 닳아 있고(미끄럽다) 엔진실 바닥엔 기름, 전쟁 상흔엔 그을음.

public sealed partial class ShipOriginSystem
{
    private long _nextHour = -1;
    private readonly Dictionary<int, long> _lastYield = new();
    private readonly SortedDictionary<long, int> _bumps = new(); // (작은 Id << 16 | 큰 Id) → 이번 10분 동안 비켜선 횟수
    private readonly Dictionary<int, long> _roundsAt = new();
    private readonly Dictionary<int, long> _checkedAt = new(); // 설비 몸체 Id → 마지막으로 한 바퀴 돈 때
    private readonly Dictionary<int, int> _roundsBy = new();
    private readonly Dictionary<int, float> _early = new(); // 컴퓨터가 앞당긴 설비 (몸체 Id → 앞당김)
    private int _faults0 = -1;
    private bool _spofSaid, _sealedSaid;
    private long _lastCall = -1;

    /// <summary>시작 상태가 얼마나 거친가 (0 새 배 · 예전 배 ~ 1).</summary>
    public float Rough => Info == null ? 0f : ShipInfos.Rough(Info.Start);

    /// <summary>주컴퓨터가 매긴 정비 위험 순위 (몸체 Id · 앞당김 — 화면 · 시험용).</summary>
    public IReadOnlyDictionary<int, float> Ranked => _early;

    /// <summary>불 번짐 배율: 군용 내장재는 덜 타고, 개척민이 손으로 댄 내장재는 잘 탄다.</summary>
    public float FireMul => Info == null ? 1f : Info.Designer switch { ShipDesigner.Military => 0.65f, ShipDesigner.Settler => 1.25f, _ => 1f };

    /// <summary>주컴퓨터가 이 설비의 정비를 앞당겼나 (작업 목록이 기준 마모를 낮추고 급함을 올린다). 컴퓨터가 멎으면 0.</summary>
    public float Early(Machine m) => _early.Count > 0 && _w.Automation.MainOnline && _early.TryGetValue(m.Body.Id, out var e) ? e : 0f;

    public bool RoundsToday(CrewMember c) => _roundsAt.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Hours(4);
    public bool RoundsDone(CrewMember c) => _roundsAt.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Hours(18);
    public int RoundsBy(CrewMember c) => _roundsBy.TryGetValue(c.Id, out var n) ? n : 0;

    /// <summary>이 사람이 한 바퀴를 돌 까닭: 닳은 배를 안다 · 정비 문화를 따른다.</summary>
    public string? RoundsWhy(CrewMember c)
    {
        if (Info == null) return null;
        if (_w.Culture.Follows(c, CustomKind.MaintainerWay)) return "소리부터 듣는다 — 아침마다 한 바퀴";
        if (Rough >= 0.55f) return Info.Start switch
        {
            ShipStart.Junk => "고물 배 — 아침마다 한 바퀴 돌며 귀를 댄다",
            ShipStart.Derelict => "오래 버려졌던 배 — 어디가 먼저 갈지 모른다",
            _ => "전쟁을 겪은 배 — 땜질한 곳부터 본다",
        };
        return null;
    }

    // ───────────────────────────── 세계를 만들 때: 배 본체 ─────────────────────────────

    private void ApplyBody(Rng rng)
    {
        var w = _w;
        var body = w.Body;
        var grid = w.Ship.Grid;
        float rough = Rough;
        if (rough <= 0f || body.Wear.Length != grid.CellCount) return;
        for (int i = 0; i < grid.CellCount; i++)
        {
            if (body.Floor[i] == Material.None) continue;
            var c = grid.CellAt(i);
            var room = w.Ship.RoomAt(c);
            if (room == null) continue;
            // 많이 밟힌 통로 · 식당 · 엔진실은 더 닳았다
            float busy = room.Type is RoomType.Corridor or RoomType.Mess or RoomType.Engine or RoomType.Galley ? 1f : 0.55f;
            body.Wear[i] = MathF.Min(0.95f, rough * busy * rng.Range(0.35f, 0.9f));
            if (Info!.Start == ShipStart.Junk && room.Type is RoomType.Engine or RoomType.Cooling or RoomType.Workshop && rng.Chance(0.08f))
                body.SetMark(c, CellMark.Oil, rng.Range(0.2f, 0.5f), "전 주인이 흘린 기름");
            if (Info.Start == ShipStart.WarScarred && rng.Chance(0.03f))
                body.SetMark(c, CellMark.Soot, rng.Range(0.3f, 0.7f), "전쟁 때 그을음");
        }
    }

    // ───────────────────────────── 매 틱: 비켜서기 관찰 ─────────────────────────────

    private void Watch()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            var g = c.Gait;
            if (g.YieldTo < 0 || g.YieldUntil < w.Tick) continue;
            if (_lastYield.TryGetValue(c.Id, out var last) && last == g.YieldUntil) continue;
            _lastYield[c.Id] = g.YieldUntil;
            if (g.YieldTo >= w.Crew.Count || g.YieldTo == c.Id) continue;
            int a = Math.Min(c.Id, g.YieldTo), b = Math.Max(c.Id, g.YieldTo);
            long key = ((long)a << 16) | (uint)b;
            _bumps[key] = _bumps.TryGetValue(key, out var n) ? n + 1 : 1;
            Stats.Squeezes++;
        }
    }

    /// <summary>10분마다: 자꾸 비켜선 두 사람 — 친하면 농담 · 가까워지고, 사이가 나쁘면 짜증이 쌓인다.</summary>
    private void Squeeze()
    {
        var w = _w;
        foreach (var (key, n) in _bumps)
        {
            if (n < 2) continue;
            var x = w.Crew[(int)(key >> 16)];
            var y = w.Crew[(int)(key & 0xffff)];
            if (!Able(x) || !Able(y)) continue;
            float aff = (x.AffinityTo(y) + y.AffinityTo(x)) * 0.5f;
            if (aff >= 0.1f || x.Habits.Contains(Habit.Joker) || y.Habits.Contains(Habit.Joker))
            {
                Stats.SqueezeBonds++;
                x.ChangeAffinity(y, 0.012f); y.ChangeAffinity(x, 0.012f);
                x.Needs.Social = MathF.Min(1f, x.Needs.Social + 0.03f);
                if (R.Chance(0.5f)) x.Say(w, Persona.Say(x, $"{y.Name}, 또 만났네 — 이 배는 너무 좁아"));
            }
            else if (aff < -0.1f || x.Needs.Stress > 0.6f)
            {
                Stats.SqueezeSpats++;
                x.ChangeAffinity(y, -0.012f); y.ChangeAffinity(x, -0.008f);
                x.Needs.Stress = MathF.Min(1f, x.Needs.Stress + 0.025f);
                if (R.Chance(0.5f)) x.Say(w, Persona.Say(x, $"{y.Name}, 또 너야? 좀 비켜"));
                if (R.Chance(0.3f)) Life.Diary(w, x, Persona.Say(x, $"좁은 통로에서 {Ko.WaGwa(y.Name)} 하루에도 몇 번씩 부딪힌다."));
            }
        }
        _bumps.Clear();
    }

    // ───────────────────────────── 한 시간마다: 주컴퓨터 · 정비 문화 ─────────────────────────────

    private void Hourly()
    {
        var w = _w;
        if (_faults0 < 0) _faults0 = w.Ship.Machines.Sum(m => m.FaultCount);
        Rank();
        Advise();
        MaintCulture();
    }

    /// <summary>주컴퓨터: 고장 위험 = 마모 × 부품 나이 × (떼어 온 부품) × 핵심 설비. 위에서 넷을 앞당긴다 (닳은 배일수록 많이).</summary>
    private void Rank()
    {
        var w = _w;
        var au = w.Automation;
        if (!au.Present || !au.MainOnline) return;
        var list = new List<(Machine m, float risk, float age, int salvage)>();
        foreach (var m in w.Ship.Machines)
        {
            if (m.Crop != null || m.Body.Room.Abandoned || m.Body.Room.Detached || m.Body.Stowed) continue;
            float age = w.Parts.AgeFactor(m);
            int salvage = w.Parts.Of(m).Count(p => p.Lot.Origin == PartOrigin.Salvage);
            float risk = MathF.Max(0.05f, m.Wear) * age * (1f + 0.15f * salvage) * (m.Spec.Critical ? 1.6f : 1f) * (m.Faults.Count > 0 ? 0.3f : 1f);
            list.Add((m, risk, age, salvage));
        }
        if (list.Count == 0) return;
        var top = list.OrderByDescending(x => x.risk).ThenBy(x => x.m.Body.Id).Take(Rough >= 0.55f ? 4 : Rough > 0f ? 2 : 1).ToList();
        float scale = 0.08f + 0.22f * Rough; // 새 배는 조금만, 고물 배는 크게 앞당긴다
        var before = _early.Keys.OrderBy(k => k).ToList();
        _early.Clear();
        for (int i = 0; i < top.Count; i++) _early[top[i].m.Body.Id] = scale * (1f - 0.18f * i);
        if (before.SequenceEqual(_early.Keys.OrderBy(k => k))) return;
        Stats.Ranks++;
        float avgWear = list.Average(x => x.m.Wear);
        int salv = list.Sum(x => x.salvage);
        au.Book.Add(ActKind.Advice, top[0].m.Body.Room,
            $"{ShipInfos.Name(Info!.Start)} 배 · {ShipInfos.Year - Info.Built}년 · 평균 마모 {avgWear * 100:0}% · 떼어 온 부품 {salv}",
            "고장 위험 순위 — " + string.Join(" · ", top.Select((x, i) => $"{i + 1}. {x.m.Name} (마모 {x.m.Wear * 100:0}% · 부품 나이 {x.age:0.0}배" + (x.salvage > 0 ? $" · 떼어 온 부품 {x.salvage}" : "") + ")")),
            $"정비 순서를 위험한 것부터 · 기준 마모를 {scale * 100:0}%p 낮춤", "",
            "origin:rank", SimTime.Hours(1), 60f);
    }

    /// <summary>주컴퓨터: 설계 · 시작 상태에서 읽은 경고와 제안 (한 번씩).</summary>
    private void Advise()
    {
        var w = _w;
        var au = w.Automation;
        if (!au.Present || !au.MainOnline) return;
        if (!_spofSaid && Info!.Designer == ShipDesigner.Civilian && w.Net.Rings.Count == 0 && w.Net.SourceRoom(NetKind.Power) is Room ps)
        {
            _spofSaid = true;
            Stats.Advice++;
            au.Book.Add(ActKind.Proposal, ps, "전력 보조 간선 0 · 배전반 하나에 모든 회로", "민간 설계의 단일 고장점 — 배전반이나 간선 하나가 나가면 배 전체가 정전된다",
                "", "여유가 생기면 보조 간선(이중 배선)을 놓자", "origin:spof", SimTime.TicksPerDay, 60f);
        }
        if (!_sealedSaid && SealedRooms.Count > 0 && SealedRooms.Any(id => w.Ship.Rooms[id].Abandoned))
        {
            _sealedSaid = true;
            Stats.Advice++;
            var r = w.Ship.Rooms[SealedRooms[0]];
            au.Book.Add(ActKind.Advice, r, $"{r.Name} — 처음부터 용접돼 있던 구역 · 안 공기 기록 없음", "다시 열면 안 공기가 섞인다 — 먼저 압력 · 산소를 확인해야 한다",
                "", "다시 열 때 공기부터 확인", "origin:sealed", SimTime.TicksPerDay, 60f);
        }
    }

    /// <summary>고장을 여러 번 겪고 한 바퀴가 몸에 밴 닳은 배 → "소리부터 듣는다"가 배의 관행이 된다.</summary>
    private void MaintCulture()
    {
        var w = _w;
        if (Rough < 0.55f || w.Culture.Of(CustomKind.MaintainerWay) != null) return;
        int faults = w.Ship.Machines.Sum(m => m.FaultCount) - _faults0;
        var walkers = w.Crew.Where(c => !c.Dead && RoundsBy(c) >= 2).OrderBy(c => c.Id).ToList();
        if (faults < 2 || walkers.Count < 2) return;
        var hero = walkers.OrderByDescending(c => RoundsBy(c)).ThenByDescending(c => c.RawSkill(Skill.Mechanics)).First();
        var cu = w.Culture.Adopt(CustomKind.MaintainerWay, $"고물 배 — 고장이 {faults}번 나는 동안 {Ko.IGa(hero.Name)} 아침마다 한 바퀴 돌며 소리로 먼저 찾았다", hero.Name);
        cu.Followers.Clear(); cu.Knowers.Clear();
        foreach (var c in walkers) { cu.Followers.Add(c.Id); cu.Knowers.Add(c.Id); }
        Stats.CultureBorn++;
        hero.Say(w, Persona.Say(hero, "이 배는 귀로 고치는 거야"));
    }

    /// <summary>한 바퀴: 닳은 설비에 귀를 대고 조이고 닦는다 (마모 조금 · 소리에 익는다).</summary>
    internal void OnRounds(CrewMember c, Machine m)
    {
        var w = _w;
        _roundsAt[c.Id] = w.Tick;
        _checkedAt[m.Body.Id] = w.Tick;
        _roundsBy[c.Id] = RoundsBy(c) + 1;
        Stats.Rounds++;
        c.Familiarize(m.Body.Type, 0.06f);
        if (m.Wear > 0.3f)
        {
            Stats.RoundFixes++;
            m.Wear = MathF.Max(0f, m.Wear - 0.04f);
            m.Fouled = MathF.Max(0f, m.Fouled - 0.05f);
        }
        MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: 아침 한 바퀴");
        if (R.Chance(0.3f)) c.Say(w, Persona.Say(c, m.Wear > 0.6f ? $"{m.Name} 소리가 어제랑 달라" : $"{m.Name}, 오늘도 버텨 줘"));
    }

    internal void OnRoundsSkipped(CrewMember c) => _roundsAt[c.Id] = _w.Tick - SimTime.Hours(4); // 볼 게 없다 — 오늘은 넘어간다

    public long CheckedAt(Furniture f) => _checkedAt.TryGetValue(f.Id, out var t) ? t : -1;

    // ───────────────────────────── 갈라진 배 ─────────────────────────────

    /// <summary>갈라진 순간: 주컴퓨터가 쪽마다 사람 · 함교 · 의무실 · 공기 · 물 · 조리를 세어 없는 것을 알린다.</summary>
    private void OnSplit(Dictionary<int, int> side)
    {
        var w = _w;
        var au = w.Automation;
        if (!au.Present || !au.MainOnline) return;
        var ship = w.Ship;
        var groups = side.Values.Distinct().OrderBy(g => g).ToList();
        var notes = new List<string>();
        foreach (int g in groups)
        {
            var rooms = ship.Rooms.Where(r => !r.Detached && side.Values.Contains(g) && RoomGroup(r) == g).ToList();
            int people = side.Count(kv => kv.Value == g);
            var miss = new List<string>();
            if (!rooms.Any(r => r.Type == RoomType.Bridge || r.Kind == RoomType.BackupBridge)) miss.Add("조종석");
            if (!rooms.Any(r => r.Type == RoomType.Medbay)) miss.Add("의무실");
            if (!rooms.Any(r => r.Furniture.Any(f => f.Type == FurnitureType.OxygenGenerator))) miss.Add("산소");
            if (!rooms.Any(r => r.Furniture.Any(f => f.Type == FurnitureType.WaterRecycler))) miss.Add("정수");
            if (!rooms.Any(r => r.Furniture.Any(f => f.Type is FurnitureType.MealDispenser or FurnitureType.Stove))) miss.Add("배식");
            string head = rooms.Any(r => r.Type == RoomType.Bridge) ? "함교 쪽" : rooms.Any(r => r.Kind == RoomType.BackupBridge) ? "예비 함교 쪽" : $"{rooms.FirstOrDefault()?.Name ?? "?"} 쪽";
            notes.Add($"{head} {people}명" + (miss.Count > 0 ? $" — {string.Join("·", miss)} 없음" : " — 혼자 버틸 수 있다"));
        }
        Stats.Advice++;
        au.Book.Add(ActKind.Advice, null, $"연결 통로가 끊겨 배가 {groups.Count}쪽으로 갈렸다", string.Join(" / ", notes),
            "쪽마다 따로 돌린다 (공기 · 전기 · 당직)", "연결 통로를 먼저 다시 잇자", "origin:split", SimTime.Hours(2), 60f);
    }

    private int RoomGroup(Room r)
    {
        // 갈라진 순간의 덩어리: 그 방에 이어진 사람의 덩어리 (사람이 없는 방은 문을 따라)
        var ship = _w.Ship;
        int n = ship.Rooms.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        foreach (var d in ship.Doors)
        {
            if (d.IsExternal || d.Removed || d.Welded || d.RoomA == null || d.RoomB == null || d.RoomA.Detached || d.RoomB.Detached) continue;
            parent[Find(d.RoomA.Id)] = Find(d.RoomB.Id);
        }
        return Find(r.Id);
    }

    /// <summary>갈라진 동안: 건너편 친구를 걱정하는 사람이 통신기 곁에 있으면 부른다 → 걱정이 풀리고 서로 가까워진다.</summary>
    private void Call(Dictionary<int, int> side)
    {
        var w = _w;
        if (_lastCall >= 0 && w.Tick - _lastCall < SimTime.Hours(1)) return;
        foreach (var c in w.Crew)
        {
            if (!Able(c) || !side.TryGetValue(c.Id, out int s) || c.Job?.Urgent == true) continue;
            var friend = w.Crew.Where(o => o != c && !o.Dead && side.TryGetValue(o.Id, out int so) && so != s && c.AffinityTo(o) > 0.15f)
                .OrderByDescending(o => c.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
            if (friend == null) continue;
            bool console = c.Room!.Furniture.Any(f => f.Type is FurnitureType.Console or FurnitureType.MainComputer) || c.Room.Type is RoomType.Bridge or RoomType.Comms;
            if (!console) continue;
            _lastCall = w.Tick;
            Stats.Calls++;
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
            friend.Needs.Stress = MathF.Max(0f, friend.Needs.Stress - 0.04f);
            c.ChangeAffinity(friend, 0.03f); friend.ChangeAffinity(c, 0.03f);
            c.Say(w, Persona.Say(c, $"{friend.Name}, 들려? 그쪽은 괜찮아?"));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 통신기로 건너편의 {Ko.EulReul(friend.Name)} 불렀다", c.Id);
            return;
        }
    }
}

/// <summary>v16.9 닳은 배의 아침 한 바퀴: 걱정되는 설비에 귀를 대고 조이고 닦는다. 정비 문화를 따르는 사람은 새 배에서도.</summary>
public sealed class ShipRoundsActivity : Activity
{
    public override string Id => "shiprounds";
    public override string Label => "아침 한 바퀴";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var o = w.Origin;
        if (!o.Active || c.Down || c.Outside || c.IsChild || !c.IsAwake || c.Room == null || Crisis.Acting(w)) return (0f, "—");
        if (o.RoundsDone(c)) return (0f, "오늘은 돌았다");
        float hour = SimTime.HourOfDay(w.Tick);
        if (hour < 6.5f || hour > 11f) return (0f, "아침에");
        if (c.Needs.Hunger > 0.75f || c.Needs.Rest < 0.25f) return (0f, "—");
        var why = o.RoundsWhy(c);
        if (why == null) return (0f, "—");
        return (0.42f + 0.18f * c.Traits.Diligence, why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var o = w.Origin;
        // 사람마다 제 귀로 고른다: 닳은 정도 × 그 설비에 익은 정도, 최근에 누가 본 것은 빼고 (컴퓨터 순위는 모른다)
        Furniture? best = null;
        Cell spot = default;
        float bestScore = 0f;
        foreach (var m in w.Ship.Machines)
        {
            var f = m.Body;
            if (m.Crop != null || f.Stowed || f.Room.Abandoned || f.Room.Detached || f.UseSpots.Count == 0) continue;
            if (o.CheckedAt(f) >= 0 && w.Tick - o.CheckedAt(f) < SimTime.Hours(8)) continue;
            var at = f.UseSpots.Where(s => dist.Reachable(s) && !w.IsSpotTaken(s, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
            if (at is not Cell s0 || dist.Get(s0) > 90) continue;
            float score = m.Wear * (1f + 0.6f * c.FamiliarityWith(f.Type)) + (m.Spec.Critical ? 0.15f : 0f) - 0.002f * dist.Get(s0);
            if (score <= bestScore) continue;
            bestScore = score; best = f; spot = s0;
        }
        if (best?.Machine is not Machine target) { o.OnRoundsSkipped(c); return null; }
        var toils = new List<Toil>
        {
            new GotoToil(spot),
            new WorkToil(0.2f, target.Spec.Skill, best.Center),
            new DoToil((cm, world) => { world.Origin.OnRounds(cm, target); return true; }),
        };
        return new Job(this, $"아침 한 바퀴 — {target.Name}", toils) { Target = best, LogText = $"아침 한 바퀴 — {Ko.EulReul(target.Name)} 귀로 듣는다", LogKind = LogKind.Work };
    }
}
