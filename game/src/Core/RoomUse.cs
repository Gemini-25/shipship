using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.17 방은 승무원이 정한다 — ① 용도는 쓰임에서 생긴다 · ⑤ 바뀐 배치가 습관과 부딪친다.
//
// 쓰임: 십 분마다 누가 어느 방에서 무엇을 하나(잠 · 식사 · 운동 · 손일 · 쉼)를 적고, 보관함을 드나든 일은 일을 시작할 때 적는다.
//   이틀 반감으로 옅어지는 "사람·시간"과 지금 놓인 가구 · 설비가 함께 방의 지금 용도를 정한다 (한 시간마다).
//   설계도 용도와 다르면 Room.UsedAs — 침실에 선반을 들이고 물건을 드나들면 창고, 창고 절반에 운동 기구를 놓고 땀을 흘리면 운동실.
//   시스템들이 쓰는 용도 판정(Facilities · 소품 자리)은 이걸 먼저 본다. 주 컴퓨터는 바뀐 쓰임을 읽고 감시 기준을 바꾼다고 알린다.
// 습관: 설비 · 선반을 옮기면 옛 자리를 기억하는 사람(옮기는 걸 못 본 사람)은 한동안 옛 자리로 먼저 간다 — 헷갈려 서 있다가 알아챈다.
//   옮기는 걸 본 사람 · 나른 사람은 바로 안다. 헷갈림은 이틀쯤 지나면 반으로 줄고, 한 번 헷갈리면 배운다.

/// <summary>옮긴 설비 · 가구 하나의 옛 자리 (사람들의 습관이 남아 있다).</summary>
public sealed class MovedThing
{
    public int Furniture { get; init; }
    public string Name { get; init; } = "";
    public Cell OldCell { get; init; }
    public int OldRoom { get; init; }
    public int NewRoom { get; set; }
    public long Moved { get; init; }
    /// <summary>새 자리를 아는 사람 (나른 사람 · 본 사람 · 헷갈려 보고 배운 사람).</summary>
    public HashSet<int> Knows { get; } = new();
    /// <summary>옛 자리에 익숙했던 사람.</summary>
    public HashSet<int> Habit { get; } = new();
    public int Confusions;
}

public sealed class RoomUseStats { public int Samples, Changes, Confusions, Learned, Visits, Notices; }

public sealed class RoomUseSystem
{
    /// <summary>쓰임으로 따지는 용도들 (기관 · 통로 · 함교 같은 방은 설비가 정한다 — 따지지 않는다).</summary>
    public static readonly RoomType[] Uses =
    {
        RoomType.Quarters, RoomType.Storage, RoomType.Gym, RoomType.Workshop, RoomType.Hydroponics,
        RoomType.Medbay, RoomType.Galley, RoomType.Mess, RoomType.Lounge,
    };
    public const int N = 9;
    public const float HalfLifeHours = 48f;

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7393 + 211));
    /// <summary>방 → 용도별 사람·시간 (이틀 반감).</summary>
    private readonly Dictionary<int, float[]> _dwell = new();
    /// <summary>(방 · 사람) → 그 방에서 보낸 사람·시간 (이름 짓기: "서지안의 작업장").</summary>
    private readonly Dictionary<long, float> _who = new();
    private readonly Dictionary<int, long> _since = new();
    /// <summary>헷갈려 서 있는 사람 → 그때 (화면의 물음표).</summary>
    public Dictionary<int, long> Puzzled { get; } = new();
    public List<MovedThing> Moved { get; } = new();
    public RoomUseStats Stats { get; } = new();
    private long _nextSample, _nextJudge;

    public RoomUseSystem(World w) => _w = w;

    public static int IndexOf(RoomType t) => Array.IndexOf(Uses, t);

    /// <summary>설계도가 정한 용도 (세부 종류는 본래 쪽으로: 조용한 침실 → 침실, 원심 운동실 → 운동실). 따지지 않는 방은 null.</summary>
    public static RoomType? Family(RoomType t)
    {
        if (IndexOf(t) >= 0) return t;
        if (t is RoomType.Centrifuge) return RoomType.Gym;
        if (t is RoomType.QuietQuarters or RoomType.PrivateCabins or RoomType.WaterWallCabin) return RoomType.Quarters;
        if (t is RoomType.Cargo or RoomType.Shelter) return RoomType.Storage;
        var b = RoomCatalog.BaseOf(t);
        return IndexOf(b) >= 0 && b != RoomType.Lounge ? b : t is RoomType.Garden or RoomType.Theater or RoomType.Meditation ? RoomType.Lounge : null;
    }

    public static RoomType? Design(Room r) => Family(r.Kind);
    public static RoomType? Actual(Room r) => r.UsedAs ?? Design(r);

    public static string UseName(RoomType t) => t switch
    {
        RoomType.Gym => "운동실",
        _ => RoomTypes.Name(t),
    };

    // ─────────────────────────────── 가구가 말하는 용도 ───────────────────────────────

    private static void FurnitureScore(Furniture f, float[] s)
    {
        switch (f.Type)
        {
            case FurnitureType.Bed: s[0] += 2.5f; break;
            case FurnitureType.Cot: s[0] += 1.8f; break;
            case FurnitureType.Shelf: s[1] += 2.5f; break;
            case FurnitureType.SupplyCache: s[1] += 1f; break;
            case FurnitureType.Fridge: s[1] += 0.8f; s[6] += 1f; break;
            case FurnitureType.Treadmill: s[2] += 4.5f; break;
            case FurnitureType.Workbench: s[3] += 3f; break;
            case FurnitureType.Lathe or FurnitureType.SolderStation or FurnitureType.Fabricator or FurnitureType.PartTestBench or FurnitureType.ToolWall
                or FurnitureType.Hoist or FurnitureType.MaintCart: s[3] += 1.5f; break;
            case FurnitureType.GrowBed: s[4] += 3f; break;
            case FurnitureType.PlantWall: s[4] += 0.8f; s[8] += 0.8f; break;
            case FurnitureType.MedBed: s[5] += 3f; break;
            case FurnitureType.Autoclave or FurnitureType.DiagnosticScanner: s[5] += 1f; break;
            case FurnitureType.Stove or FurnitureType.Oven: s[6] += 3f; break;
            case FurnitureType.DishWasher or FurnitureType.CoffeeMachine: s[6] += 0.5f; s[8] += 0.5f; break;
            case FurnitureType.Table: s[7] += 1.5f; s[8] += 0.5f; break;
            case FurnitureType.MealDispenser: s[7] += 2f; break;
            case FurnitureType.Seat: s[7] += 0.4f; s[8] += 0.6f; break;
            case FurnitureType.GameTable or FurnitureType.Projector or FurnitureType.Bookshelf or FurnitureType.Aquarium: s[8] += 2f; break;
        }
    }

    /// <summary>그 용도를 받치는 가구가 있나 (없으면 그 용도의 쓰임은 삼분의 일만 친다 — 침대를 다 뺀 방은 잠잔 기억만으로 침실이 아니다).</summary>
    private static bool Anchored(Room r, int use)
    {
        foreach (var f in r.Furniture)
        {
            var s = new float[N];
            FurnitureScore(f, s);
            if (s[use] >= 1f) return true;
        }
        return use == 2; // 운동은 맨몸으로도 한다
    }

    public float[] Dwell(Room r) => _dwell.TryGetValue(r.Id, out var d) ? d : new float[N];

    /// <summary>방의 용도별 점수: 가구 + 쓰임 (사람·시간의 로그).</summary>
    public float[] Scores(Room r)
    {
        var s = new float[N];
        foreach (var f in r.Furniture) if (!f.Stowed) FurnitureScore(f, s);
        if (_dwell.TryGetValue(r.Id, out var d))
            for (int i = 0; i < N; i++)
                if (d[i] > 0f) s[i] += 2f * MathF.Log(1f + d[i]) * (Anchored(r, i) ? 1f : 0.33f);
        if (_w.RoomPlans.Intent(r) is (RoomType iu, float bonus) && IndexOf(iu) is int ii and >= 0) s[ii] += bonus; // 회의가 막 정한 용도
        return s;
    }

    /// <summary>쓰임을 적는다 (사람·시간).</summary>
    public void Add(Room r, RoomType use, float hours)
    {
        int i = IndexOf(use);
        if (i < 0 || r.Detached) return;
        if (!_dwell.TryGetValue(r.Id, out var d)) _dwell[r.Id] = d = new float[N];
        d[i] += hours;
    }

    /// <summary>그 사람이 그 방에서 보낸 사람·시간 (이틀 반감).</summary>
    public float Who(Room r, CrewMember c) => _who.TryGetValue(r.Id * 4096L + c.Id, out var v) ? v : 0f;

    /// <summary>그 방을 가장 많이 쓰는 사람과 몫 (없으면 null).</summary>
    public (CrewMember who, float hours, float share)? Owner(Room r)
    {
        float total = 0f, best = 0f;
        CrewMember? top = null;
        foreach (var c in _w.Crew)
        {
            float v = Who(r, c);
            total += v;
            if (v > best && !c.Dead) { best = v; top = c; }
        }
        return top == null || total <= 0f ? null : (top, best, best / total);
    }

    // ─────────────────────────────── 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick >= _nextSample) { _nextSample = w.Tick + SimTime.Minutes(10); Sample(); }
        if (w.Tick >= _nextJudge) { _nextJudge = w.Tick + SimTime.Hours(1); Judge(); }
        if (Puzzled.Count > 0 && w.Tick % 300 == 0)
            foreach (var id in Puzzled.Keys.Where(k => w.Tick - Puzzled[k] > SimTime.Minutes(6)).ToList()) Puzzled.Remove(id);
    }

    /// <summary>지금 이 사람이 하는 일이 어느 용도의 쓰임인가 (-1: 셈하지 않는다).</summary>
    private int UseOf(CrewMember c)
    {
        var w = _w;
        if (c.Pose == Pose.Sleeping) return 0;
        var a = c.Job?.Activity;
        if (HobbyActivity.Exercising(c, w) || a is RoomWorkActivity && w.RoomPlans.WorkingOut(c)) return 2;
        if (a is EatActivity or SharedMealActivity) return 7;
        if (c.Pose == Pose.Working && c.Room is Room r)
        {
            if (c.Job?.Target is Furniture t && t.Type is FurnitureType.Stove or FurnitureType.Oven) return 6;
            if (c.Job?.Target is Furniture g && g.Type == FurnitureType.GrowBed) return 4;
            foreach (var f in r.Furniture) if (f.Type is FurnitureType.Workbench or FurnitureType.Lathe or FurnitureType.SolderStation) return 3;
        }
        if (a is RelaxActivity or ChatActivity or HobbyActivity) return 8;
        return -1;
    }

    private void Sample()
    {
        var w = _w;
        Stats.Samples++;
        float k = MathF.Exp(-MathF.Log(2f) * (10f / 60f) / HalfLifeHours);
        foreach (var d in _dwell.Values) for (int i = 0; i < N; i++) d[i] *= k;
        if (_who.Count > 0) foreach (var key in _who.Keys.ToList()) { float v = _who[key] * k; if (v < 0.05f) _who.Remove(key); else _who[key] = v; }
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.Outside || c.Room is not Room r || r.Detached) continue;
            int use = UseOf(c);
            if (use >= 0) Add(r, Uses[use], 1f / 6f);
            if (use is >= 2 and <= 6 || use == 8) _who[r.Id * 4096L + c.Id] = Who(r, c) + 1f / 6f; // 손일 · 운동 · 재배 · 조리 · 쉼 (잠 · 식사는 이름을 정하지 않는다)
        }
    }

    /// <summary>한 시간마다: 방마다 지금 용도를 정한다 (설계와 같으면 null). 바뀌면 기록 · 컴퓨터가 감시 기준을 바꾼다.</summary>
    private void Judge()
    {
        var w = _w;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached || r.OffLimits || Design(r) is not RoomType design) { if (r.UsedAs != null && r.Detached) r.UsedAs = null; continue; }
            var s = Scores(r);
            int di = IndexOf(design), best = di;
            for (int i = 0; i < N; i++) if (s[i] > s[best] + 0.001f) best = i;
            RoomType? want = r.UsedAs;
            if (r.UsedAs is RoomType cur)
            {
                int ci = IndexOf(cur);
                if (best == di || s[ci] < s[di]) want = null;
                else if (best != ci && s[best] > s[ci] * 1.2f + 0.5f) want = Uses[best];
            }
            else if (best != di && s[best] >= 6f && s[best] > s[di] * 1.25f + 1f) want = Uses[best];
            if (want == r.UsedAs) continue;
            Changed(r, design, want, s);
        }
    }

    private void Changed(Room r, RoomType design, RoomType? to, float[] s)
    {
        var w = _w;
        var before = Actual(r) ?? design;
        string beforeName = r.Name;
        r.UsedAs = to;
        _since[r.Id] = w.Tick;
        Stats.Changes++;
        var now = Actual(r) ?? design;
        if (to != null && r.CustomName == null) r.FormerPurposes.Add(UseName(before));
        string why = Why(r, now);
        string text = to != null
            ? $"{Ko.IGa(beforeName)} 이제 {Ko.EuRo(UseName(now))} 쓰인다 — 설계는 {UseName(design)} ({why})"
            : $"{Ko.IGa(beforeName)} 다시 {Ko.EuRo(UseName(design))} 쓰인다 ({why})";
        w.History.Add(w, HistoryKind.Adaptation, text, r, log: true);
        MarkLog.Add(r.Marks, w.Tick, $"쓰임: {UseName(before)} → {UseName(now)}");
        // 주 컴퓨터가 바뀐 쓰임을 읽는다 (감시 기준 · 위험)
        string risk = (design, now) switch
        {
            (RoomType.Quarters, RoomType.Storage) => "잠자리가 줄고 불에 탈 것이 쌓인다 — 침대 수를 다시 센다 · 화재 감시를 창고 기준으로",
            (RoomType.Storage, RoomType.Quarters) => "창고에서 자는 사람 — 선반이 넘어질 수 있다 · 대피 경로를 다시 짠다",
            (_, RoomType.Gym) => "운동하는 방 — 이산화탄소 · 열이 오른다 · 환기를 조금 올린다",
            (_, RoomType.Storage) => "물건이 쌓이는 방 — 화재 하중 · 통로 막힘을 본다",
            (_, RoomType.Quarters) => "사람이 자는 방 — 밤에는 소음 · 공기를 더 본다",
            _ => $"{UseName(now)} 기준으로 감시한다",
        };
        if (w.Automation.Book.Add(ActKind.Advice, r, $"{r.Name}: 설계 {UseName(design)} · 실제 {UseName(now)} ({why})", "쓰임이 설계와 다르다", risk, "", "use:" + r.Id, SimTime.Hours(6)) != null)
            Stats.Notices++;
    }

    /// <summary>왜 그 용도로 보나 (가구 · 쓰임 한 줄).</summary>
    public string Why(Room r, RoomType use)
    {
        var parts = new List<string>();
        int shelves = r.Furniture.Count(f => f.Storage != null && f.Type != FurnitureType.DroneDock);
        int beds = r.Furniture.Count(f => FurnitureTypes.Sleepable(f.Type));
        int fit = r.Furniture.Count(f => f.Type == FurnitureType.Treadmill);
        if (use == RoomType.Storage && shelves > 0) parts.Add($"보관함 {shelves}");
        if (use == RoomType.Quarters && beds > 0) parts.Add($"침대 {beds}");
        if (use == RoomType.Gym && fit > 0) parts.Add($"운동 기구 {fit}");
        var d = Dwell(r);
        int i = IndexOf(use);
        if (i >= 0 && d[i] >= 0.5f) parts.Add(use switch
        {
            RoomType.Storage => $"드나듦 {d[i] * 4:0}번",
            RoomType.Quarters => $"잠 {d[i]:0}시간",
            RoomType.Gym => $"운동 {d[i]:0.#}시간",
            _ => $"쓰임 {d[i]:0}시간",
        });
        if (beds == 0 && Design(r) == RoomType.Quarters && use != RoomType.Quarters) parts.Add("침대가 없다");
        if (_w.RoomPlans.Intent(r) is (RoomType iu, _) && iu == use) parts.Add("회의가 정했다");
        return parts.Count > 0 ? string.Join(" · ", parts) : "쓰임";
    }

    // ─────────────────────────────── 용도 판정 훅 (Facilities) ───────────────────────────────

    /// <summary>쓰임으로 그 일의 전용 방이 된 방 (번호 순).</summary>
    public static Room? BestUsed(Ship ship, ShipFunction fn, Func<Room, bool>? ok)
    {
        foreach (var r in ship.Rooms)
            if (r.UsedAs is RoomType u && fn.Best.Contains(u) && !r.Detached && !r.Abandoned && (ok == null || ok(r))) return r;
        return null;
    }

    /// <summary>이 방이 그 일을 얼마나 잘 하나 — 다른 용도로 쓰이는 방은 설계 용도로는 반만, 쓰임의 용도로는 그 값.</summary>
    public static float Factor(ShipFunction fn, Room room, float blueprint)
    {
        if (room.UsedAs is not RoomType u) return blueprint;
        float used = fn.Best.Contains(u) ? 1f : 0f;
        foreach (var (k, f) in fn.Fallback) if (k == u) used = MathF.Max(used, f);
        return MathF.Max(used, blueprint * 0.5f);
    }

    // ─────────────────────────────── 습관: 옛 자리 ───────────────────────────────

    /// <summary>설비를 옮겼다: 옛 자리를 남긴다. 나른 사람 · 그 방들에 있던 사람은 새 자리를 안다.</summary>
    public MovedThing NoteMove(Furniture f, Cell oldCell, Room oldRoom, IEnumerable<CrewMember> carriers)
    {
        var w = _w;
        Moved.RemoveAll(x => x.Furniture == f.Id);
        var m = new MovedThing { Furniture = f.Id, Name = f.Label, OldCell = oldCell, OldRoom = oldRoom.Id, NewRoom = f.Room.Id, Moved = w.Tick };
        foreach (var c in carriers) m.Knows.Add(c.Id);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            if (c.Room == oldRoom || c.Room == f.Room) m.Knows.Add(c.Id);
            else m.Habit.Add(c.Id);
        }
        Moved.Add(m);
        if (Moved.Count > 24) Moved.RemoveAt(0);
        return m;
    }

    public MovedThing? MovedOf(Furniture f)
    {
        foreach (var m in Moved) if (m.Furniture == f.Id) return m;
        return null;
    }

    /// <summary>
    /// 일을 시작할 때: 손댈 보관함 · 설비가 옮겨졌는데 이 사람은 옛 자리에 익숙하다 → 옛 자리로 먼저 가서 두리번거린다 (이틀쯤이면 반쯤 잊는다).
    /// 보관함을 드나드는 일은 그 방의 창고 쓰임으로 적는다.
    /// </summary>
    public void OnJobStarted(CrewMember c, Job job)
    {
        var w = _w;
        if (job.Activity is RoomWorkActivity) return;
        bool visited = false;
        Furniture? hit = null;
        MovedThing? habit = null;
        void Look(Furniture? f)
        {
            if (f == null || f.Stowed) return;
            if (!visited && f.Storage != null && f.Type != FurnitureType.DroneDock && !f.Room.Detached) { Add(f.Room, RoomType.Storage, 0.25f); Stats.Visits++; visited = true; }
            if (habit != null || Moved.Count == 0) return;
            if (MovedOf(f) is MovedThing m && m.Habit.Contains(c.Id) && !m.Knows.Contains(c.Id)) { habit = m; hit = f; }
        }
        Look(job.Target);
        foreach (var f in job.Reservations) Look(f);
        foreach (var t in job.Toils)
            switch (t)
            {
                case TakeToil tt: Look(tt.From); break;
                case TakeKitToil tk: Look(tk.From); break;
                case PutToil pt: Look(pt.Into); break;
            }
        if (habit is not MovedThing mv || hit == null || c.IsChild || job.Urgent) return;
        float days = (w.Tick - mv.Moved) / (float)SimTime.TicksPerDay;
        float chance = 0.9f * MathF.Exp(-days / 2.9f);
        mv.Knows.Add(c.Id); // 이번에 가 보면 안다 (못 가고 끊겨도 다음엔 새 자리로)
        if (days > 6f || !R.Chance(chance)) { Stats.Learned++; return; }
        var ship = w.Ship;
        Cell? spot = ship.IsWalkable(mv.OldCell) ? mv.OldCell : null;
        if (spot == null && ship.RoomAt(mv.OldCell) is Room or)
        {
            int best = int.MaxValue;
            foreach (var x in or.Cells)
            {
                if (!ship.IsWalkable(x)) continue;
                int d = Math.Abs(x.X - mv.OldCell.X) + Math.Abs(x.Y - mv.OldCell.Y);
                if (d < best) { best = d; spot = x; }
            }
        }
        if (spot is not Cell at || at == c.Cell) return;
        var thing = hit;
        job.Prepend(new Toil[]
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(2), Pose.Standing, mv.OldCell.Center) { EveryTick = (cm, world) => { if (!world.RoomUse.Puzzled.ContainsKey(cm.Id)) world.RoomUse.Puzzled[cm.Id] = world.Tick; } },
            new DoToil((cm, world) => { world.RoomUse.Confused(cm, mv, thing); return true; }),
        });
    }

    /// <summary>옛 자리에 와서야 알았다.</summary>
    internal void Confused(CrewMember c, MovedThing m, Furniture f)
    {
        var w = _w;
        Stats.Confusions++;
        m.Confusions++;
        Puzzled[c.Id] = w.Tick;
        var oldRoom = w.Ship.Rooms[m.OldRoom];
        string here = w.Ship.RoomAt(m.OldCell)?.Name ?? oldRoom.Name;
        string there = f.Room.Name;
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.01f);
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {here}에 {Ko.EulReul(m.Name)} 찾으러 갔다가 헷갈렸다 — 아, {Ko.EuRo(there)} 옮겼지", c.Id);
        c.Say(w, Persona.Say(c, $"어? {Ko.IGa(m.Name)} 여기 있었는데… 아, {Ko.EuRo(there)} 옮겼지"));
        if (m.Confusions == 1) Life.Diary(w, c, Persona.Say(c, $"버릇처럼 {here}에 {Ko.EulReul(m.Name)} 찾으러 갔다. 손이 먼저 기억한다."));
        // 옮기자고 한 사람에게 투덜 (반대했던 사람은 더)
        if (w.RoomPlans.MovedBy(f) is RoomPlan p && p.Proposer >= 0 && p.Proposer != c.Id && p.Proposer < w.Crew.Count)
        {
            var who = w.Crew[p.Proposer];
            if (p.Against.Contains(c.Id)) { c.ChangeAffinity(who, -0.015f); MindSystem.Anger(c, 0.02f); }
        }
    }

    // ─────────────────────────────── 지문 ───────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Stats.Samples); I(Stats.Changes); I(Stats.Confusions); I(Stats.Learned); I(Stats.Visits); I(Moved.Count);
        foreach (var r in _w.Ship.Rooms) I(r.UsedAs is RoomType u ? (int)u + 1 : 0);
        foreach (var (id, d) in _dwell) { I(id); float sum = 0f; foreach (var v in d) sum += v; F(sum); }
    }
}
