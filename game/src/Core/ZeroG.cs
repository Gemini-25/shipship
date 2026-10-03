using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.4 무중력: 회전 고리가 멈추거나 중력 판 제어기가 나가면 —
//  고정 안 된 것만 뜬다 (v17.0 걸쇠 · 끈 · 묶은 것 · 침대 끈과 v16.4 물건 물리 Fixed를 그대로 읽는다): 공구 벽의 렌치 · 선반 통조림 · 책 ·
//   식탁 그릇 · 베개 · 작은 화분 · 바닥에 고인 물은 물방울이 되어 떠다닌다. 물방울이 전기 설비 곁에 닿으면 합선(불꽃 · 회로 단락).
//  사람은 손잡이를 잡고 벽을 따라 천천히 움직이고(벽 밀기는 빠르지만 부딪힌다) · 처음 겪는 사람은 멀미를 한다(겪을수록 덜하다).
//  주컴퓨터가 방송한다: 손잡이 · 떠다니는 것 붙잡기 · 물 열지 말기 — 어느 방에 단단한 게 많이 떠 있는지 짚는다 · 복구 1분 전 머리 조심.
//  복구 순간 떠 있던 것은 그 자리에 떨어진다 — 아래 있던 사람이 맞아 다친다(예고를 들은 사람은 비켜선다) · 깨질 것은 깨지고(v17.0 조각 · 빗자루) ·
//   물방울은 바닥 물이 되고 · 떠 있던 사람은 주저앉는다. 다치거나 많이 깨지면 배에 관행이 생긴다 (공구는 끈에 · 쓰면 바로 넣는다).
//  전기가 끊겨 멈추면 전기가 돌아오는 순간(예고 없이) 중력이 돌아온다 — 고장이면 정비사가 고치고, 컴퓨터가 1분 전에 알린다.

public enum FloatKind : byte { Wrench, Driver, Can, Book, Bottle, Plate, Bowl, PillJar, Pillow, Pot, Droplet, Sick, Article }

/// <summary>떠다니는 것 하나 (칸 좌표 · 칸/틱 속도).</summary>
public sealed class Floater
{
    public int Id { get; init; }
    public FloatKind Kind { get; init; }
    public Vector2 Pos { get; internal set; }
    public Vector2 Vel { get; internal set; }
    public float Angle { get; internal set; }
    public float Spin { get; internal set; }
    public int RoomId { get; internal set; } = -1;
    /// <summary>나온 곳 (가구 id · 물건 id · 화분 id).</summary>
    public int From { get; init; } = -1;
    public int Article { get; init; } = -1;
    public int Plant { get; init; } = -1;
    public float Mass { get; init; }
    public float Liters { get; internal set; }
    public long Since { get; init; }
    public int ClaimedBy { get; internal set; } = -1;
    public bool Hard => Kind is FloatKind.Wrench or FloatKind.Driver or FloatKind.Can or FloatKind.Bottle or FloatKind.PillJar or FloatKind.Pot or FloatKind.Plate or FloatKind.Bowl
        || Kind == FloatKind.Article && Mass >= 1f;
    public bool Wet => Kind is FloatKind.Droplet or FloatKind.Sick;
    public string Name => ZeroGSystem.Name(Kind);
}

/// <summary>복구 순간 떨어진 자리 (그림: 부딪힌 자국).</summary>
public sealed class Thud
{
    public Vector2 At { get; init; }
    public FloatKind Kind { get; init; }
    public long Tick { get; init; }
    public int Hit { get; init; } = -1;
    public bool Broke { get; init; }
}

/// <summary>합선 자리 (그림: 불꽃 · 그을음).</summary>
public sealed class ShortMark
{
    public Vector2 At { get; init; }
    public int Furniture { get; init; }
    public long Tick { get; init; }
}

public sealed class ZeroGStats
{
    public int Episodes, Floated, HeldFast, ArticlesFloated, Droplets, Shorts, Grips, PushOffs, Bumps, Queasy, Sick, Vomits, SleepDrift;
    public int Broadcasts, Heard, Orders, Warned, Caught, Stowed, Repairs, Fell, Hurt, Dodged, Broke, Puddles, PeopleDown, Customs, Tethered, CatFloat;
    public string Line() =>
        $"무중력 {Episodes}번 · 뜬 것 {Floated}(물건 {ArticlesFloated} · 물방울 {Droplets}) · 고정돼 남음 {HeldFast} · 합선 {Shorts} · 손잡이 {Grips} · 벽 밀기 {PushOffs}(부딪힘 {Bumps}) · " +
        $"멀미 {Queasy}(앓음 {Sick} · 토함 {Vomits}) · 방송 {Broadcasts}(들음 {Heard} · 붙잡기 지시 {Orders} · 복구 예고 {Warned}) · 붙잡음 {Caught} · 고침 {Repairs} · " +
        $"복구 때 떨어짐 {Fell}(맞아 다침 {Hurt} · 비킴 {Dodged} · 깨짐 {Broke} · 물 {Puddles}) · 주저앉음 {PeopleDown} · 관행 {Customs}(끈 맨 공구 {Tethered})";
}

public sealed partial class ZeroGSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6007 + 1301));
    public ZeroGStats Stats { get; } = new();
    public static bool Off;
    public static long UpdateTicks;

    public bool Weightless { get; private set; }
    public float G { get; private set; } = 1f;
    public string Cause { get; private set; } = "";
    public bool Ring => RingRoom != null;
    public long Since { get; private set; } = -1;
    public long RestoreAt { get; private set; } = -1;
    public bool RestoreWarned { get; private set; }
    /// <summary>고쳐야 돌아온다 (정비사 일) — 전기로 멈춘 건 전기가 돌아오면 저절로.</summary>
    public bool NeedsRepair { get; private set; }
    public bool PowerCut { get; private set; }
    public int RepairBy { get; internal set; } = -1;
    public float RepairDone { get; internal set; }
    public List<Floater> Floaters { get; } = new();
    public List<Thud> Thuds { get; } = new();
    public List<ShortMark> Shorts { get; } = new();
    /// <summary>사람마다: 멀미 · 겪은 횟수 · 붙잡은 손잡이 쪽(그림).</summary>
    public SortedDictionary<int, float> Queasy { get; } = new();
    public SortedDictionary<int, int> Exposures { get; } = new();
    public SortedDictionary<int, Vector2> Grips { get; } = new();
    public SortedSet<int> Knew { get; } = new();
    public SortedSet<int> Drifting { get; } = new();
    public int Losses { get; private set; }
    public int Episode { get; private set; }
    /// <summary>이번 무중력에서 붙잡기 지시를 받은 방.</summary>
    public int OrderRoom { get; private set; } = -1;
    private int _next = 1, _hurtThis, _brokeThis;
    private long _nextCheck, _nextOrder, _lastVomit;
    private readonly SortedDictionary<int, long> _sparkAt = new();
    private bool[]? _elec;
    private int _elecVer = -1;

    public ZeroGSystem(World w) => _w = w;

    public Room? RingRoom => _w.Ship.RoomsOf(RoomType.Centrifuge).FirstOrDefault();

    public static string Name(FloatKind k) => k switch
    {
        FloatKind.Wrench => "렌치", FloatKind.Driver => "드라이버", FloatKind.Can => "통조림", FloatKind.Book => "책", FloatKind.Bottle => "유리병",
        FloatKind.Plate => "접시", FloatKind.Bowl => "사발", FloatKind.PillJar => "약통", FloatKind.Pillow => "베개", FloatKind.Pot => "화분",
        FloatKind.Droplet => "물방울", FloatKind.Sick => "토사물", _ => "물건",
    };

    private static float MassOf(FloatKind k) => k switch
    {
        FloatKind.Wrench => 0.6f, FloatKind.Driver => 0.2f, FloatKind.Can => 0.45f, FloatKind.Book => 0.5f, FloatKind.Bottle => 0.6f, FloatKind.Plate => 0.4f,
        FloatKind.Bowl => 0.3f, FloatKind.PillJar => 0.15f, FloatKind.Pillow => 0.4f, FloatKind.Pot => 1.5f, _ => 0.1f,
    };

    private CrewMember? CrewOf(int id) => id >= 0 && id < _w.Crew.Count && _w.Crew[id].Id == id ? _w.Crew[id] : _w.Crew.FirstOrDefault(c => c.Id == id);

    /// <summary>Jobs.Step 훅: 무중력에서는 손잡이를 잡고 벽을 따라 느리게 · 겪을수록 빨라진다.</summary>
    public float MoveMul(CrewMember c)
    {
        if (!Weightless || c.Outside) return 1f;
        float exp = MathF.Min(0.25f, 0.06f * Exposures.GetValueOrDefault(c.Id));
        return (NearWall(c.Cell) ? 0.5f : 0.62f) + exp;
    }

    public bool NearWall(Cell c)
    {
        var g = _w.Ship.Grid;
        foreach (var d in Cell.Dirs4) if (g.Kind(c + d) is TileKind.Wall or TileKind.Void) return true;
        return false;
    }

    private Vector2 WallDir(Cell c)
    {
        var g = _w.Ship.Grid;
        foreach (var d in Cell.Dirs4) if (g.Kind(c + d) is TileKind.Wall or TileKind.Void) return new Vector2(d.X, d.Y);
        return Vector2.Zero;
    }

    // ───────────────────────────── 시작 ─────────────────────────────

    /// <summary>중력이 끊긴다: 고정 안 된 것이 뜨고 · 물이 방울이 되고 · 컴퓨터가 방송한다.</summary>
    public void Begin(string cause, bool repair, bool power = false)
    {
        var w = _w;
        if (Weightless) return;
        Weightless = true; G = 0f; Cause = cause; Since = w.Tick; RestoreAt = -1; RestoreWarned = false;
        NeedsRepair = repair; PowerCut = power; RepairBy = -1; RepairDone = 0f;
        _hurtThis = _brokeThis = 0;
        Episode++; Stats.Episodes++;
        Knew.Clear(); OrderRoom = -1;
        ObjectPhysics.SetGravity(w, 0f);
        foreach (var c in w.Crew) if (!c.Dead && !c.Outside) Exposures[c.Id] = Exposures.GetValueOrDefault(c.Id) + 1;
        bool tether = w.Culture.Of(CustomKind.TetherTools) != null;
        // 선반 · 공구 벽 · 책장 · 비상 물자함: 걸쇠를 안 건 것만
        foreach (var f in w.Ship.Furniture)
        {
            if (f.Room.Detached || f.Stowed) continue;
            var kinds = Loose(f.Type);
            if (kinds.Length == 0) continue;
            if (ManeuverSystem.Shelfish(f.Type) && w.Maneuver.Latched.Contains(f.Id)) { Stats.HeldFast++; continue; }
            if (f.Type == FurnitureType.Fridge) continue; // 냉장고 문은 닫혀 있다
            int n = f.Type switch { FurnitureType.ToolWall => 3, FurnitureType.Workbench => 2, FurnitureType.Table => R.Chance(0.4f) ? 2 : 0, _ => R.Range(1, 3) };
            for (int i = 0; i < n; i++)
            {
                var k = kinds[R.Range(0, kinds.Length)];
                if (tether && k is FloatKind.Wrench or FloatKind.Driver && R.Chance(0.8f)) { Stats.Tethered++; continue; } // 끈에 매어 둔 공구
                Spawn(k, f.Center + new Vector2(R.Range(-0.4f, 0.4f), R.Range(-0.4f, 0.4f)), f.Room, from: f.Id);
            }
        }
        // 침대: 끈을 안 맨 베개
        foreach (var f in w.Ship.Furniture)
            if (ManeuverSystem.Bedish(f.Type) && !f.Room.Detached && !f.Stowed && !w.Maneuver.Bunks.Contains(f.Id) && R.Chance(0.35f))
                Spawn(FloatKind.Pillow, f.Center, f.Room, from: f.Id);
        // 바닥 물건 (v16.4): 고정 · 보관 · 깔린 것은 그대로
        foreach (var t in w.Matter.Things)
        {
            if (!t.Loose || t.Stowed || t.Spec.Flat) { if (t.Fixed) Stats.HeldFast++; continue; }
            if (w.Ship.RoomAt(t.At) is not Room r) continue;
            Spawn(FloatKind.Article, t.At.Center + t.Off, r, article: t.Id, mass: t.Mass);
            Stats.ArticlesFloated++;
        }
        // 바닥에 고인 물 → 물방울
        foreach (var (i, s) in w.Matter.Spills.ToList())
        {
            if (s.Liters < 0.2f || s.Kind != Material.Liquid) continue;
            var c = w.Ship.Grid.CellAt(i);
            if (w.Ship.RoomAt(c) is not Room r) continue;
            int n = Math.Min(3, 1 + (int)(s.Liters / 2f));
            for (int k = 0; k < n; k++) { var d = Spawn(FloatKind.Droplet, c.Center, r); d.Liters = MathF.Min(1.5f, s.Liters / n); }
            s.Liters = 0f;
            Stats.Droplets += n;
        }
        // 작은 화분 (v18.2)
        foreach (var p in w.Eco.Plants)
            if (!p.Dead && !p.Fixed && w.Ship.Rooms.ElementAtOrDefault(p.RoomId) is Room pr) { Spawn(FloatKind.Pot, p.At.Center, pr, plant: p.Id); p.Floating = true; }
        while (Floaters.Count > 90) Floaters.RemoveAt(Floaters.Count - 1);
        // 자는 사람: 침대 끈이 없으면 떠오른다
        foreach (var c in w.Crew)
            if (!c.Dead && c.Pose == Pose.Sleeping && c.Bed is Furniture bed && !w.Maneuver.Bunks.Contains(bed.Id)) { Drifting.Add(c.Id); Stats.SleepDrift++; }
        w.Eco.OnZeroG(true);
        w.Log.Add(w.Tick, LogKind.Warning, $"중력이 끊겼다 — {cause}. 떠오른 것 {Floaters.Count}개");
        w.History.Add(w, HistoryKind.Incident, $"중력이 끊겼다 — {cause}");
        Announce();
        // 겪은 사람이 소리친다 (방송을 못 들은 사람에게도 몸이 먼저 안다)
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.Outside) continue;
            int ex = Exposures.GetValueOrDefault(c.Id);
            c.Say(w, Persona.Say(c, ex >= 3 ? "또 떴네. 손잡이 잡고, 떠다니는 거 하나씩 붙잡자" : ex == 2 ? "중력 나갔어 — 손잡이!" : R.Chance(0.5f) ? "어, 어 — 몸이 떠!" : "뭐야, 바닥이 없어졌어!"));
            c.Jolt(w);
        }
    }

    private static FloatKind[] Loose(FurnitureType t) => t switch
    {
        FurnitureType.ToolWall => new[] { FloatKind.Wrench, FloatKind.Driver, FloatKind.Wrench },
        FurnitureType.Workbench => new[] { FloatKind.Wrench, FloatKind.Driver },
        FurnitureType.Bookshelf => new[] { FloatKind.Book },
        FurnitureType.Shelf => new[] { FloatKind.Can, FloatKind.Bottle, FloatKind.Can, FloatKind.Book },
        FurnitureType.SupplyCache => new[] { FloatKind.PillJar, FloatKind.Can },
        FurnitureType.Table => new[] { FloatKind.Plate, FloatKind.Bowl },
        FurnitureType.MedBed => new[] { FloatKind.PillJar },
        _ => Array.Empty<FloatKind>(),
    };

    internal Floater Spawn(FloatKind k, Vector2 at, Room r, int from = -1, int article = -1, int plant = -1, float mass = -1f)
    {
        float a = R.Range(0f, MathF.Tau);
        var f = new Floater
        {
            Id = _next++, Kind = k, Pos = at, Vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * R.Range(0.002f, 0.008f), Angle = R.Range(-3f, 3f), Spin = R.Range(-0.04f, 0.04f),
            RoomId = r.Id, From = from, Article = article, Plant = plant, Mass = mass > 0f ? mass : MassOf(k), Since = _w.Tick, Liters = k == FloatKind.Droplet ? 0.3f : 0f,
        };
        Floaters.Add(f);
        Stats.Floated++;
        return f;
    }

    private void Announce()
    {
        var w = _w;
        if (!w.Automation.CoreOnline) return;
        // 단단한 것이 가장 많이 뜬 방 (사람이 있는 방 먼저)
        var hard = Floaters.Where(f => f.Hard).GroupBy(f => f.RoomId).Select(g => (room: g.Key, n: g.Count(), ppl: w.Crew.Count(c => !c.Dead && c.Room?.Id == g.Key)))
            .OrderByDescending(x => x.ppl > 0 ? 1 : 0).ThenByDescending(x => x.n).ThenBy(x => x.room).FirstOrDefault();
        string where = hard.n > 0 ? $" {w.Ship.Rooms[hard.room].Name}에 공구 · 통조림 같은 단단한 것이 {hard.n}개 떠 있습니다 — 가까운 분이 붙잡아 넣어 주십시오." : "";
        bool wet = Floaters.Any(f => f.Wet);
        string text = $"중력이 끊겼습니다 ({Cause}). 가까운 손잡이를 잡고 벽을 따라 천천히 움직이십시오.{where}" + (wet ? " 떠다니는 물방울은 수건으로 감싸고, 물이 든 것은 열지 마십시오." : " 물이 든 것은 열지 마십시오.")
            + (PowerCut ? " 전기가 돌아오면 중력도 곧바로 돌아옵니다 — 떠 있는 것 아래에 서 있지 마십시오." : "");
        var b = w.Automation.Speak.Announce(w.Automation.Voice.Style(text), hard.n > 0 ? w.Ship.Rooms[hard.room] : null, 2);
        if (b == null) return;
        Stats.Broadcasts++;
        if (hard.n > 0) { OrderRoom = hard.room; Stats.Orders++; }
        foreach (var id in b.HeardBy) Knew.Add(id);
        Stats.Heard += b.HeardBy.Count;
        w.Automation.Book.Add(ActKind.Broadcast, hard.n > 0 ? w.Ship.Rooms[hard.room] : null, $"중력 0 · 떠오른 것 {Floaters.Count}개 (단단한 것 {Floaters.Count(f => f.Hard)})",
            "중력이 돌아오는 순간 떠 있던 것은 그대로 떨어진다 — 붙잡아 넣어 둘수록 다치는 사람이 준다", "손잡이 · 붙잡기 지시 방송", "", "zerog:begin", SimTime.Minutes(5));
    }

    // ───────────────────────────── 복구 ─────────────────────────────

    /// <summary>고친 사람 · 전기: 컴퓨터가 알 수 있으면 1분 전에 알린다.</summary>
    public void ScheduleRestore(bool warn, string why)
    {
        var w = _w;
        if (!Weightless || RestoreAt >= 0) return;
        if (warn && w.Automation.CoreOnline)
        {
            var b = w.Automation.Speak.Announce(w.Automation.Voice.Style($"1분 뒤 중력이 돌아옵니다 ({why}). 떠 있는 물건 아래에서 비켜서고, 머리를 감싸고 손잡이를 잡아 주십시오."), null, 2);
            if (b != null)
            {
                RestoreWarned = true; Stats.Warned++;
                foreach (var id in b.HeardBy) Knew.Add(id);
                RestoreAt = w.Tick + SimTime.Minutes(1);
                return;
            }
        }
        RestoreAt = w.Tick;
    }

    /// <summary>중력이 돌아온다: 떠 있던 것은 그 자리에 떨어진다.</summary>
    public void Restore()
    {
        var w = _w;
        if (!Weightless) return;
        Weightless = false; G = 1f; RestoreAt = -1;
        ObjectPhysics.SetGravity(w, 1f);
        int fell = 0;
        foreach (var f in Floaters.ToList()) { Land(f); fell++; }
        Floaters.Clear();
        // 떠 있던 사람: 손잡이 없이 방 한가운데 있던 사람은 주저앉는다
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside || c.Room == null) continue;
            bool drift = Drifting.Contains(c.Id);
            bool held = NearWall(c.Cell) && Grips.ContainsKey(c.Id) || c.Pose == Pose.Sitting;
            bool ready = RestoreWarned && Knew.Contains(c.Id) && c.IsAwake;
            if (!drift && (held || ready || !c.IsAwake)) continue;
            if (!drift && !R.Chance(0.35f)) continue;
            bool hurt = R.Chance(drift ? 0.15f : 0.1f);
            if (hurt) { NeedsSystem.AddInjury(c.Vitals, R.Range(0.02f, 0.05f), "중력이 돌아올 때 바닥에 떨어졌다"); Stats.Hurt++; }
            w.Maneuver.Tumbles.Add(new Tumble { Crew = c.Id, Tick = w.Tick, At = c.Position, Dir = new Vector2(0f, 0.3f), Hurt = hurt, FromBed = drift });
            Stats.PeopleDown++;
            c.Say(w, Persona.Say(c, drift ? "으악 — 자다가 떨어졌어" : "어이쿠 — 갑자기 무거워!"));
        }
        Drifting.Clear(); Grips.Clear();
        foreach (var p in w.Eco.Plants) p.Floating = false;
        w.Eco.OnZeroG(false);
        long mins = (w.Tick - Since) / SimTime.Minutes(1);
        w.Log.Add(w.Tick, LogKind.Ship, $"중력이 돌아왔다 ({mins}분 만) — 떨어진 것 {fell}개" + (_hurtThis > 0 ? $", 맞아 다친 사람 {_hurtThis}명" : ""));
        // 컴퓨터: 무엇이 어디 떨어졌나 · 조각 조심
        if (w.Automation.CoreOnline && fell > 0)
            w.Automation.Book.Add(ActKind.Advice, null, $"중력 복구 · 떨어진 것 {fell}개 · 깨짐 {_brokeThis} · 다친 사람 {_hurtThis}",
                _hurtThis > 0 ? "떠 있던 단단한 것이 사람 위로 떨어졌다 — 다음엔 공구를 끈에 매어 두는 편이 낫다" : "깨진 조각과 바닥 물을 먼저 치운다", "줍기 · 쓸기 요청", "", "zerog:after", SimTime.Minutes(5));
        if (_hurtThis > 0 || _brokeThis >= 3) Losses++;
        if (Losses >= 1 && _hurtThis > 0 && w.Culture.Of(CustomKind.TetherTools) == null)
        {
            var hurtOne = w.Crew.Where(c => !c.Dead && c.IsAwake && !c.IsChild).OrderByDescending(c => c.Vitals.Injury).ThenBy(c => c.Id).FirstOrDefault();
            w.Culture.Adopt(CustomKind.TetherTools, $"중력이 돌아오는 순간 떠 있던 공구에 사람이 맞았다", hurtOne?.Name);
            Stats.Customs++;
            hurtOne?.Say(w, Persona.Say(hurtOne, "이제 공구는 다 끈에 매어 두자. 쓰면 바로 넣고. 머리 위로 렌치 떨어지는 건 한 번이면 충분해"));
        }
        foreach (var c in w.Crew) if (Queasy.TryGetValue(c.Id, out var q) && q > 0.3f) Queasy[c.Id] = q * 0.6f;
    }

    private void Land(Floater f)
    {
        var w = _w;
        var room = w.Ship.Rooms.ElementAtOrDefault(f.RoomId);
        var cell = Cell.FromPosition(f.Pos);
        if (room == null || !w.Ship.IsOpenFloor(cell) && w.Ship.Grid.Kind(cell) != TileKind.Floor)
            cell = room?.Cells.OrderBy(x => (x.Center - f.Pos).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).FirstOrDefault() ?? cell;
        room ??= w.Ship.RoomAt(cell);
        Stats.Fell++;
        // 아래 있던 사람
        CrewMember? hit = null;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside || c.Room?.Id != f.RoomId) continue;
            if ((c.Position - f.Pos).LengthSquared() > 0.36f) continue;
            hit = c; break;
        }
        bool broke = false;
        if (hit != null && f.Hard)
        {
            bool ready = RestoreWarned && Knew.Contains(hit.Id) && hit.IsAwake;
            if (R.Chance(ready ? 0.2f : 0.75f))
            {
                float dmg = MathF.Min(0.16f, 0.04f + 0.05f * f.Mass);
                NeedsSystem.AddInjury(hit.Vitals, dmg, $"중력이 돌아오는 순간 떠 있던 {Ko.IGa(f.Name)} 떨어져 맞았다");
                Memory.Shake(w, hit, 0.05f, $"중력이 돌아오는 순간 {Ko.IGa(f.Name)} 머리 위로 떨어졌다");
                MarkLog.Add(hit.Memory.Marks, w.Tick, $"중력이 돌아오는 순간 떠 있던 {Ko.IGa(f.Name)} 떨어져 맞았다");
                hit.Say(w, Persona.Say(hit, f.Kind == FloatKind.Wrench ? "아악 — 렌치가!" : "아야! 뭐가 떨어졌어"));
                hit.Jolt(w);
                w.Brain2.Emotions.Feel(hit, Feeling.Fear, 0.2f, "떨어진 것에 맞았다");
                _hurtThis++; Stats.Hurt++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{hit.Name}: 중력이 돌아오는 순간 떠 있던 {Ko.IGa(f.Name)} 떨어져 다쳤다", hit.Id);
                w.History.Add(w, HistoryKind.Casualty, $"중력이 돌아오는 순간 떠 있던 {Ko.IGa(f.Name)} {Ko.EulReul(hit.Name)} 맞혔다", room, new[] { hit });
            }
            else { Stats.Dodged++; hit.Say(w, Persona.Say(hit, "휴 — 비켜서길 잘했다")); }
        }
        // 무엇이 되었나
        switch (f.Kind)
        {
            case FloatKind.Droplet or FloatKind.Sick:
                w.Matter.Pour(cell, Material.Liquid, MathF.Max(0.2f, f.Liters), f.Kind == FloatKind.Sick ? "떠다니던 토사물이 떨어졌다" : "떠다니던 물방울이 떨어졌다");
                if (f.Kind == FloatKind.Sick && room != null) w.Smells.Emit(room, SmellKind.Foul, 0.3f);
                Stats.Puddles++;
                break;
            case FloatKind.Article:
                if (w.Matter.Get(f.Article) is Article a && a.CarriedBy < 0)
                {
                    w.Matter.Place(a, cell);
                    if (a.Spec.Tough < 10f) { w.Matter.Impact(a, a.Spec.Tough * 1.5f, "중력이 돌아와 떨어졌다"); broke = a.Stage >= BreakStage.Broken; }
                }
                break;
            case FloatKind.Pillow:
                break; // 푹신하다 — 그 자리에 둔다 (침대로 되돌리는 건 사람이)
            case FloatKind.Pot:
                if (w.Eco.Plants.FirstOrDefault(p => p.Id == f.Plant) is Plant pl)
                {
                    broke = R.Chance(0.4f);
                    pl.At = cell; pl.RoomId = room?.Id ?? pl.RoomId;
                    if (broke) { pl.Health = MathF.Max(0f, pl.Health - 0.35f); pl.Spilled = true; }
                }
                break;
            default:
            {
                var fk = f.Kind switch
                {
                    FloatKind.Wrench or FloatKind.Driver => FallenKind.Tool, FloatKind.Can => FallenKind.Can, FloatKind.Book => FallenKind.Book, FloatKind.Bottle => FallenKind.Bottle,
                    FloatKind.Plate => FallenKind.Plate, FloatKind.Bowl => FallenKind.Bowl, _ => FallenKind.PillJar,
                };
                bool fragile = fk is FallenKind.Bowl or FallenKind.Bottle or FallenKind.Plate;
                broke = fragile && R.Chance(0.55f);
                var src = w.Ship.Furniture.FirstOrDefault(x => x.Id == f.From);
                w.Maneuver.Fallen.Add(new FallenThing
                {
                    Id = 900000 + f.Id + Episode * 1000, Kind = fk, At = cell, From = src?.Id ?? -1, RoomId = room?.Id ?? -1, Tick = w.Tick, Broken = broke,
                    Off = new Vector2(R.Range(-0.3f, 0.3f), R.Range(-0.3f, 0.3f)), Angle = f.Angle,
                });
                if (w.Maneuver.Fallen.Count > 160) w.Maneuver.Fallen.RemoveAt(0);
                if (broke && room != null)
                {
                    w.Body.RaiseMark(cell, CellMark.Glass, 0.55f, $"깨진 {f.Name} 조각");
                    w.Maneuver.Shards.Add(new ShardSpot { At = cell, RoomId = room.Id, Tick = w.Tick, What = f.Name });
                }
                break;
            }
        }
        if (broke) { Stats.Broke++; _brokeThis++; }
        Thuds.Add(new Thud { At = f.Pos, Kind = f.Kind, Tick = w.Tick, Hit = hit?.Id ?? -1, Broke = broke });
        if (Thuds.Count > 80) Thuds.RemoveAt(0);
    }

    /// <summary>사람이 붙잡아 넣었다 (공구함 · 선반 · 수건).</summary>
    internal void Catch(Floater f, CrewMember c)
    {
        var w = _w;
        Floaters.Remove(f);
        Stats.Caught++;
        if (f.Kind == FloatKind.Article && w.Matter.Get(f.Article) is Article a) { a.Fixed = true; w.Maneuver.Tied.Add(a.Id); w.Matter.Place(a, Cell.FromPosition(c.Position)); }
        if (f.Kind == FloatKind.Pot && w.Eco.Plants.FirstOrDefault(p => p.Id == f.Plant) is Plant pl) { pl.Floating = false; pl.Fixed = true; pl.At = Cell.FromPosition(f.Pos); }
        if (f.Kind is FloatKind.Wrench or FloatKind.Driver) Stats.Stowed++;
        MarkLog.Add(c.Memory.Marks, w.Tick, $"무중력에서 떠다니던 {Ko.EulReul(f.Name)} 붙잡아 넣었다");
    }

    // ───────────────────────────── 매 틱 ─────────────────────────────

    /// <summary>매 틱: 떠다니는 것 움직이기 · 벽에 튕기기 · 물방울 합선 · 손잡이 · 복구 시각.</summary>
    public void Step()
    {
        if (Off || !Weightless) return;
        var w = _w;
        if (RestoreAt >= 0 && w.Tick >= RestoreAt) { Restore(); return; }
        var grid = w.Ship.Grid;
        EnsureElec();
        for (int i = Floaters.Count - 1; i >= 0; i--)
        {
            var f = Floaters[i];
            var np = f.Pos + f.Vel;
            var nc = Cell.FromPosition(np);
            var k = grid.Kind(nc);
            if (k is not (TileKind.Floor or TileKind.Door))
            {
                var oc = Cell.FromPosition(f.Pos);
                var v = f.Vel;
                if (nc.X != oc.X) v.X = -v.X * 0.7f;
                if (nc.Y != oc.Y) v.Y = -v.Y * 0.7f;
                f.Vel = v;
                f.Spin = -f.Spin * 0.8f + v.X * 2f;
                continue;
            }
            f.Pos = np;
            f.Angle += f.Spin;
            int rid = grid.RoomId(nc);
            if (rid >= 0) f.RoomId = rid;
            // 물방울 × 전기 설비 = 합선
            if (f.Wet && _elec != null && _elec[grid.Index(nc)] && w.Tick % 5 == f.Id % 5) Short(f, nc);
        }
        // 손잡이 · 벽 밀기 (움직이는 사람만 · 4틱마다)
        if (w.Tick % 4 == 0)
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Outside || c.Room == null) continue;
                bool moving = c.Path != null && (c.Position - c.PreviousPosition).LengthSquared() > 1e-6f;
                if (!moving) { if (c.Pose == Pose.Sleeping) continue; if (!Grips.ContainsKey(c.Id) && NearWall(c.Cell)) Grips[c.Id] = WallDir(c.Cell); continue; }
                if (NearWall(c.Cell)) { if (!Grips.ContainsKey(c.Id)) Stats.Grips++; Grips[c.Id] = WallDir(c.Cell); }
                else if (Grips.Remove(c.Id))
                {
                    Stats.PushOffs++;
                    if (R.Chance(0.06f * (1f - MathF.Min(0.8f, 0.2f * Exposures.GetValueOrDefault(c.Id)))))
                    {
                        Stats.Bumps++;
                        c.Say(w, Persona.Say(c, "윽 — 너무 세게 밀었다"));
                    }
                }
                // 떠다니는 것과 부딪힘 (손이 비면 붙잡는다)
                foreach (var f in Floaters)
                {
                    if (f.RoomId != c.Room.Id || f.ClaimedBy >= 0 || (f.Pos - c.Position).LengthSquared() > 0.25f) continue;
                    if (c.Carrying == null && c.CarryingPerson == null && R.Chance(0.5f)) { Catch(f, c); break; }
                    Stats.Bumps++;
                    f.Vel = -f.Vel;
                    break;
                }
            }
    }

    private void EnsureElec()
    {
        var w = _w;
        int ver = w.Ship.Furniture.Count * 31 + w.Ship.Grid.Version;
        if (_elec != null && _elecVer == ver) return;
        _elecVer = ver;
        var grid = w.Ship.Grid;
        _elec = new bool[grid.CellCount];
        foreach (var f in w.Ship.Furniture)
        {
            if (!Electric(f.Type) || f.Stowed || f.Room.Detached) continue;
            foreach (var c in f.Cells)
                foreach (var d in Cell.Dirs8.Append(new Cell(0, 0)))
                {
                    var n = c + d;
                    if (grid.InBounds(n)) _elec[grid.Index(n)] = true;
                }
        }
    }

    public static bool Electric(FurnitureType t) => t is FurnitureType.Console or FurnitureType.PowerPanel or FurnitureType.Battery or FurnitureType.MainComputer or FurnitureType.SensorArray
        or FurnitureType.CapacitorBank or FurnitureType.NavComputer or FurnitureType.SolderStation or FurnitureType.DiagnosticScanner or FurnitureType.SurgeProtector
        or FurnitureType.AuxGenerator or FurnitureType.Fabricator or FurnitureType.ReactorSimulator or FurnitureType.SignalBooster or FurnitureType.PartTestBench;

    private void Short(Floater f, Cell at)
    {
        var w = _w;
        var room = w.Ship.RoomAt(at);
        if (room == null) return;
        Furniture? near = null;
        foreach (var x in room.Furniture) if (Electric(x.Type) && x.Cells.Any(c => Math.Abs(c.X - at.X) <= 1 && Math.Abs(c.Y - at.Y) <= 1)) { near = x; break; }
        if (near == null) return;
        if (_sparkAt.TryGetValue(near.Id, out var last) && w.Tick - last < SimTime.Minutes(3)) return;
        _sparkAt[near.Id] = w.Tick;
        Floaters.Remove(f);
        Stats.Shorts++;
        Shorts.Add(new ShortMark { At = f.Pos, Furniture = near.Id, Tick = w.Tick });
        if (Shorts.Count > 30) Shorts.RemoveAt(0);
        bool live = room.Powered && !room.BreakerOff;
        if (live)
        {
            w.Matter.Spark(at, room, $"{near.Label} 곁에 닿은 물방울");
            if (w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine panel && !panel.Faults.Any(x => x.Circuit == room.Circuit))
            {
                var fault = new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = room.Circuit };
                panel.Faults.Add(fault);
                panel.FaultCount++;
                w.Causes.OnFault(panel, fault);
            }
        }
        w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name}: 떠다니던 물방울이 {near.Label}에 닿아 합선 — 불꽃이 튀었다");
        MarkLog.Add(room.Marks, w.Tick, $"무중력 때 떠다니던 물방울이 {near.Label}에 닿아 불꽃이 튀었다");
        foreach (var c in w.Crew)
            if (!c.Dead && c.Room == room && c.IsAwake) { c.Say(w, Persona.Say(c, "물방울이 콘솔에 — 지지직 했어!")); c.Jolt(w); break; }
        if (w.Automation.CoreOnline)
            w.Automation.Book.Add(ActKind.Advice, room, $"{room.Name} {near.Label} 회로 이상 · 떠다니는 물방울 {Floaters.Count(x => x.Wet)}개",
                "무중력에서 물은 바닥에 고이지 않고 떠다니다 전기 설비에 붙는다 — 수건으로 감싸 모아야 한다", "물방울 수거 요청", "", "zerog:short", SimTime.Minutes(5));
    }

    // ───────────────────────────── 느린 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var w = _w;
        // 중력의 원인: 고리 방(또는 배 전체)의 전기
        if (w.Tick >= _nextCheck)
        {
            _nextCheck = w.Tick + SimTime.Minutes(1);
            bool cut = PowerDown();
            if (!Weightless && cut && w.Day >= 2) Begin(Ring ? "고리 모터에 전기가 끊겨 회전 고리가 멈췄다" : "중력 판에 전기가 끊겼다", repair: false, power: true);
            else if (Weightless && PowerCut && !cut && RestoreAt < 0) ScheduleRestore(false, "전기가 돌아왔다");
            // 드문 고장 (사흘째부터)
            if (!Weightless && w.Day >= 3 && R.Chance(1f / (45f * 24f * 60f)))
                Begin(Ring ? "회전 고리 베어링이 걸려 고리가 멈췄다" : "중력 판 제어기가 타 버렸다", repair: true);
        }
        if (Weightless) Sick(dt);
        else if (Queasy.Count > 0)
            foreach (var id in Queasy.Keys.ToList()) { float q = Queasy[id] - 0.5f * dt; if (q <= 0f) Queasy.Remove(id); else Queasy[id] = q; }
        if (Weightless && w.Tick >= _nextOrder && w.Automation.CoreOnline)
        {
            _nextOrder = w.Tick + SimTime.Minutes(12);
            // 컴퓨터가 떠다니는 물방울을 살핀다: 전기 설비 곁
            EnsureElec();
            var near = Floaters.FirstOrDefault(f => f.Wet && _elec != null && Cell.Dirs8.Any(d => _elec[w.Ship.Grid.Index(Cell.FromPosition(f.Pos) + d)]));
            if (near != null && w.Ship.Rooms.ElementAtOrDefault(near.RoomId) is Room nr)
            {
                var b = w.Automation.Speak.Announce(w.Automation.Voice.Style($"{nr.Name} 전기 설비 곁에 물방울이 떠 있습니다. 수건으로 감싸 주십시오."), nr, 1);
                if (b != null) { Stats.Broadcasts++; OrderRoom = nr.Id; foreach (var id in b.HeardBy) Knew.Add(id); }
            }
        }
        // 시간이 지나면 떠 있는 것도 공기 흐름에 조금씩 움직인다 — 오래 그대로면 조금 느려진다
        if (Weightless) foreach (var f in Floaters) f.Vel *= 0.995f;
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    private bool PowerDown()
    {
        var w = _w;
        if (RingRoom is Room ring) return !ring.Powered || ring.BreakerOff;
        int n = 0, off = 0;
        foreach (var r in w.Ship.Rooms) { if (r.Detached || r.Kind == RoomType.Corridor) continue; n++; if (!r.Powered) off++; }
        return n > 0 && off > n * 0.6f;
    }

    private void Sick(float dt)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside) continue;
            int ex = Exposures.GetValueOrDefault(c.Id);
            float rate = 0.55f * MathF.Max(0.15f, 1f - 0.25f * (ex - 1)) * (c.Fitness < 0.4f ? 1.25f : 1f) * (c.Pose == Pose.Sleeping ? 0.3f : 1f);
            float was = Queasy.GetValueOrDefault(c.Id);
            float q = MathF.Min(1f, was + rate * dt);
            Queasy[c.Id] = q;
            if (was < 0.5f && q >= 0.5f)
            {
                Stats.Queasy++;
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
                if (c.IsAwake) c.Say(w, Persona.Say(c, ex <= 1 ? "속이 울렁거려… 위아래가 없어" : "또 멀미다"));
            }
            if (q >= 0.65f && !w.Ailments.Has(c, "spacesick") && w.Ailments.Catch(c, "spacesick", null, "무중력 멀미") != null) Stats.Sick++;
            if (q >= 0.9f && c.IsAwake && w.Tick - _lastVomit > SimTime.Minutes(20) && R.Chance(0.15f) && c.Room is Room r)
            {
                _lastVomit = w.Tick;
                Stats.Vomits++;
                var f = Spawn(FloatKind.Sick, c.Position + c.Facing * 0.4f, r);
                f.Liters = 0.2f;
                w.Smells.Emit(r, SmellKind.Foul, 0.15f);
                c.Say(w, Persona.Say(c, "우웁 —"));
                foreach (var o in w.Crew) if (o != c && !o.Dead && o.Room == r && o.IsAwake) { o.Say(w, Persona.Say(o, "봉투, 봉투! 떠다니게 두면 안 돼")); break; }
            }
        }
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Weightless ? 1 : 0); I(Episode); I(Losses); I(RestoreAt); I(Floaters.Count);
        foreach (var f in Floaters) { I(f.Id); F(f.Pos.X); F(f.Pos.Y); }
        I(Stats.Hurt); I(Stats.Caught); I(Stats.Shorts); I(Stats.Fell);
        foreach (var (k, v) in Queasy) { I(k); F(v); }
    }
}
