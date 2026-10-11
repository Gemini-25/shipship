using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v19 물 순환 (자원 순환 1): 깨끗한 물 → 씀 → 배수구 → 회수관 → 오수 탱크 → 정수기 → 깨끗한 물.
//  정수기는 오수 탱크에 든 만큼만 거른다 (거르며 2%는 찌꺼기 · 소금물로 잃는다). 쓴 물이 다 돌아오지는 않는다 — 땀 · 숨 · 잎에서 날아간 몫.
//  역류 중인 배수구로 갈 몫은 바닥으로 넘친다 (퍼내면 오수 탱크로). 오수 탱크가 넘치면 밖으로 버린다.
//  회수관은 쓸수록 찌꺼기가 쌓여 가늘어진다 (개수대 음식물 찌꺼기면 더 빨리) → 막히면 배수구마다 회색물이 거꾸로 올라오고,
//  정수기는 멀쩡한데 오수가 안 들어와 놀고, 깨끗한 물이 준다.
//  주 컴퓨터는 회수관 유량이 줄면 미리 역세척을 맡기고, 막히면 '정수기는 멀쩡한데 오수가 없다'에서 회수관을 짚는다 —
//  컴퓨터가 없으면 배수구를 뚫어도 또 올라오는 일이 되풀이될 때 손재주 있는 사람이 안쪽을 의심한다.
//  뚫기 전까지는 역류가 잦은 배수구 밑에 임시 회수통을 받쳐 두고, 차면 정수기로 날라 붓는다.

/// <summary>배수구 밑에 받쳐 둔 임시 회수통.</summary>
public sealed class WaterBucket
{
    public const float Cap = 40f;
    public int Id { get; init; }
    public int RoomId { get; internal set; }
    public Cell At { get; internal set; }
    public float Fill { get; internal set; }
    /// <summary>들고 가는 사람 · 로봇 (-1 = 놓여 있다).</summary>
    public int HeldBy { get; internal set; } = -1;
    public int RobotBy { get; internal set; } = -1;
    public bool Placed => HeldBy < 0 && RobotBy < 0;
    public long Since { get; init; }
    public string By { get; init; } = "";
}

public sealed class WaterLoopStats
{
    /// <summary>누적 L: 쓴 물 · 배수구로 흘린 물 · 오수 탱크에 닿은 물 · 바닥으로 넘친 물 · 회수통이 받은 물 · 거른 물 · 넘쳐 버린 오수.</summary>
    public float Used, Drained, Returned, Spilled, Caught, Processed, Overflow;
    /// <summary>정수기가 오수가 없어 논 시간 · 회수관이 막혀 있던 시간.</summary>
    public float StarvedHours, BlockedHours;
    public float Condensed, FromFood;
    public int Flushes, Clears, Backups, Diagnosed, CrewDiagnosed, Buckets, BucketTrips, InVain;
    public string Line() =>
        $"쓴 물 {Used:0}L · 배수구로 {Drained:0}L · 오수 탱크에 {Returned:0}L · 응축수 {Condensed:0}L · 음식에서 {FromFood:0}L · 바닥으로 {Spilled:0}L · 회수통 {Caught:0}L · 거름 {Processed:0}L · 넘쳐 버림 {Overflow:0}L · " +
        $"정수기 놀림 {StarvedHours:0.#}h · 회수관 막힘 {BlockedHours:0.#}h · 역세척 {Flushes} · 뚫음 {Clears} · 역류 {Backups}(헛뚫음 {InVain}) · 짚음 컴퓨터 {Diagnosed}/사람 {CrewDiagnosed} · 회수통 {Buckets}(나름 {BucketTrips})";
}

public sealed partial class WaterSystem
{
    /// <summary>쓴 물이 배수구로 돌아오는 몫: 사람(마시고 씻은 물 — 소변 · 세면 · 설거지) · 화분(물받이로 빠지는 물) · 거르며 남는 몫 · 나머지 김(숨 · 땀 · 잎)을 공조기가 응축수로 모으는 몫.</summary>
    public const float CrewBack = 0.92f, BedBack = 0.85f, Yield = 0.98f, Condense = 0.85f;
    /// <summary>먹은 음식에 든 물과 몸이 음식을 태워 만드는 물 (사람 하나 하루 1.2L) — 소변 · 땀으로 나와 거르며 남는 몫 · 날아간 몫을 메운다.</summary>
    public const float FoodWaterPerHour = 0.05f;
    /// <summary>회수관이 평소 유량으로 꽉 막히기까지 (역세척 없이).</summary>
    public const float DaysToClog = 7f;
    private const int RouteEvery = 75; // 3분

    /// <summary>오수 탱크 (L).</summary>
    public float Grey { get; set; } = 40f;
    public float GreyCapacity => MathF.Max(60f, Capacity * 0.35f);
    /// <summary>회수관 찌꺼기 0~1 (1 = 꽉 막힘).</summary>
    public float Sludge { get; set; } = 0.12f;
    /// <summary>회수관이 지금 넘기는 몫 (유량계가 보는 값).</summary>
    public float Pass => Sludge < 0.6f ? 1f : Sludge < 1f ? 1f - (Sludge - 0.6f) / 0.4f * 0.92f : 0.05f;
    /// <summary>정수기가 오수가 없어 논 몫 (한 시간쯤 평균).</summary>
    public float Starved { get; private set; }
    /// <summary>오수 탱크에 닿는 양 (L/h, 한 시간쯤 평균).</summary>
    public float Inflow { get; private set; }
    /// <summary>회수관이 막혔다고 짚었다 (누가 · 언제).</summary>
    public bool Suspected { get; internal set; }
    public long SuspectedAt { get; internal set; } = -1;
    public string? SuspectedBy { get; internal set; }
    public List<WaterBucket> Buckets { get; } = new();
    public WaterLoopStats Loop { get; } = new();
    /// <summary>시험: 주 컴퓨터 없이 (유량계 · 진단 방송 · 예방 역세척이 없다).</summary>
    public static bool NoComputer;
    /// <summary>시험: 회수관을 뚫지 않는다 (임시 회수통만으로 버티는지).</summary>
    public static bool HoldClear;

    private float _pending, _spill, _winDrained, _winReturned; // 30분 창: 배수구로 간 물 · 오수 탱크에 닿은 물 (유량계)
    private long _routeTick, _checkTick;
    private int _backups, _vain, _nextBucket = 1;
    private long _backupSince = -1;
    private readonly HashSet<int> _backupRooms = new();
    private Rng? _rng;
    private Rng R(World w) => _rng ??= new Rng(unchecked(w.Seed * 7759 + 4111));

    /// <summary>쓴 물이 배수구로 (씻기 · 세탁 · 관 씻기 · 퍼낸 물) — 회수관을 지나 오수 탱크로.</summary>
    public void Drain(float liters)
    {
        if (liters <= 0f) return;
        _pending += liters;
        Loop.Drained += liters;
    }

    /// <summary>정수기 (회수관의 끝) — 없으면 null.</summary>
    public static Furniture? Recycler(World w)
    {
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.WaterRecycler))
            if (!f.Room.Detached) return f;
        return null;
    }

    /// <summary>정수 · 쓰기 · 돌아오기 (매 틱). 깨끗한 물로 들어간 양(L/h)을 돌려준다.</summary>
    private float Cycle(World w, float dt, float crew, float beds, float cap, float consume)
    {
        // ① 정수: 오수 탱크에 든 만큼만 · 깨끗한 물 탱크가 차면 쉰다
        float want = cap * dt;
        float room = MathF.Max(0f, (Capacity - Level) / Yield + consume * dt);
        float can = MathF.Min(want, room);
        float proc = MathF.Min(can, Grey);
        Grey -= proc;
        Loop.Processed += proc;
        float starved = can > 1e-6f ? 1f - proc / can : 0f;
        Starved += (starved - Starved) * MathF.Min(1f, dt);
        if (starved > 0.5f) Loop.StarvedHours += dt;
        if (Pass < 0.3f) Loop.BlockedHours += dt;
        // ② 쓰기: 있는 만큼만 (탱크가 비면 쓰지도 돌아오지도 않는다)
        float wantUse = consume * dt;
        float used = MathF.Min(wantUse, Level + proc * Yield);
        Loop.Used += used;
        float food = Drinkers(w) * FoodWaterPerHour * dt;
        Drain(food);
        Loop.FromFood += food;
        if (wantUse > 0f)
        {
            float share = used / wantUse;
            Drain((crew * CrewBack + beds * BedBack) * dt * share);
            // 숨 · 땀 · 잎에서 나간 김은 공조기 응축수로 오수 탱크에 (회수관을 거치지 않는다) — 음식 · 몸 · 식물에 남는 몫만 빠진다
            float vapor = (crew * (1f - CrewBack) + beds * (1f - BedBack)) * dt * share * Condense;
            Grey = MathF.Min(GreyCapacity, Grey + vapor);
            Loop.Condensed += vapor;
        }
        // ③ 3분마다: 배수구 → 회수관 → 오수 탱크
        if (w.Tick >= _routeTick)
        {
            _routeTick = w.Tick + RouteEvery;
            Route(w, cap);
        }
        return dt > 0f ? proc * Yield / dt : 0f;
    }

    private void Route(World w, float cap)
    {
        float inflow = _pending;
        _pending = 0f;
        _winDrained += inflow;
        var drains = w.Drains.Drains;
        float food = 0f;
        int sinks = 0;
        foreach (var d in drains)
            if (d.Kind == DrainKind.Sink) { food += d.Food; sinks++; }
        // 배수구 하나가 막히면 사람들은 다른 개수대를 쓴다 (그 몫도 회수관으로 간다) — 넘치는 건 회수관이 막혔을 때만
        float pass = Pass;
        float reach = inflow * pass;
        float spill = inflow - reach;
        // 임시 회수통: 막힌 몫 가운데 그 방 배수구로 갈 몫을 받는다
        if (spill > 0f)
            foreach (var b in Buckets)
            {
                if (!b.Placed) continue;
                float take = MathF.Min(spill * 0.45f, WaterBucket.Cap - b.Fill);
                if (take <= 0f) continue;
                b.Fill += take;
                spill -= take;
                Loop.Caught += take;
            }
        if (spill > 0f) { _spill += spill; Loop.Spilled += spill; }
        // 회수관 찌꺼기: 지난 오수만큼 (배 설계 유량에 견줘) · 개수대 음식물 찌꺼기가 많으면 더 빨리
        float design = MathF.Max(6f, cap) * 24f * DaysToClog;
        food = sinks > 0 ? food / sinks : 0f;
        Sludge = MathF.Min(1f, Sludge + reach / design * (0.8f + 0.8f * food));
        Grey += reach;
        Loop.Returned += reach;
        _winReturned += reach;
        if (Grey > GreyCapacity) { Loop.Overflow += Grey - GreyCapacity; Grey = GreyCapacity; } // 넘친 오수는 밖으로 버린다
        float hours = RouteEvery / (float)SimTime.TicksPerHour;
        Inflow += (reach / hours - Inflow) * 0.05f;
        if (_spill >= 0.5f) PourSpill(w);
        if (pass < 0.4f) Backups(w, pass);
        DropStale(w);
        if (w.Tick >= _checkTick)
        {
            _checkTick = w.Tick + SimTime.Minutes(30);
            Check(w);
            _winDrained = _winReturned = 0f;
        }
    }

    /// <summary>넘친 물을 바닥으로: 회수관 탓에 역류한 배수구 방 (없으면 회수관 끝 — 정수기 방).</summary>
    private void PourSpill(World w)
    {
        int n = 0;
        foreach (var d in w.Drains.Drains) if (d.MainBack && !w.Ship.Rooms[d.RoomId].Detached) n++;
        if (n == 0)
        {
            if (Recycler(w) is Furniture rec) w.Moisture.AddWater(rec.Room, _spill);
            _spill = 0f;
            return;
        }
        float each = _spill / n;
        foreach (var d in w.Drains.Drains)
            if (d.MainBack && !w.Ship.Rooms[d.RoomId].Detached) w.Moisture.AddWater(w.Ship.Rooms[d.RoomId], each);
        _spill = 0f;
    }

    /// <summary>회수관이 막혀 가면 쓰는 방 배수구마다 회색물이 거꾸로 올라온다 (배수구는 멀쩡하다 — 뚫어도 금방 다시).</summary>
    private void Backups(World w, float pass)
    {
        var occ = new int[w.Ship.Rooms.Count];
        foreach (var c in w.Crew) if (!c.Dead && c.IsAwake && c.Room is Room r && r.Id < occ.Length) occ[r.Id]++;
        foreach (var d in w.Drains.Drains)
        {
            if (d.Backflow || d.RoomId >= occ.Length || occ[d.RoomId] == 0 || w.Ship.Rooms[d.RoomId].Detached) continue;
            if (!R(w).Chance((0.4f - pass) * 0.35f)) continue;
            w.Drains.MainBackup(d);
            Loop.Backups++;
            if (_backupSince < 0 || w.Tick - _backupSince > SimTime.Hours(12)) { _backupSince = w.Tick; _backups = 0; _vain = 0; _backupRooms.Clear(); }
            _backups++;
            _backupRooms.Add(d.RoomId);
        }
    }

    /// <summary>들고 가다 그만둔 회수통: 사람이면 그 자리에 내려놓고, 로봇이면 제자리로.</summary>
    private void DropStale(World w)
    {
        foreach (var b in Buckets)
        {
            if (b.HeldBy >= 0)
            {
                var c = w.Crew.FirstOrDefault(x => x.Id == b.HeldBy);
                if (c != null && !c.Dead && c.Job?.Order is { Kind: WorkKind.EmptyBucket } o && o.Circuit == b.Id) continue;
                if (c is { Room: Room r } && !c.Dead) { b.At = c.Cell; b.RoomId = r.Id; }
                b.HeldBy = -1;
            }
            if (b.RobotBy >= 0 && !w.Robots.Robots.Any(r => r.Id == b.RobotBy && r.Order is { Kind: WorkKind.EmptyBucket } ro && ro.Circuit == b.Id))
                b.RobotBy = -1;
        }
    }

    /// <summary>30분마다: 회수관을 짚나 (컴퓨터는 유량 · 정수기 놀림으로, 사람은 되풀이되는 역류로) · 다 뚫었나.</summary>
    private void Check(World w)
    {
        if (Suspected)
        {
            if (Pass >= 0.85f) { Suspected = false; SuspectedBy = null; }
            return;
        }
        if (Pass >= 0.5f) return;
        var rec = Recycler(w);
        bool healthy = rec != null && w.Ship.FurnitureOf(FurnitureType.WaterRecycler).Average(f => f.Machine!.Efficiency) >= 0.5f;
        bool computer = !NoComputer && w.Automation.CoreOnline;
        // 컴퓨터: 정수기는 멀쩡한데 오수가 안 들어온다 — 배수구로 간 물은 평소대로인데 탱크에 닿는 물이 반도 안 된다
        bool lost = _winDrained > 1f && _winReturned < _winDrained * 0.5f;
        if (computer && healthy && (lost || Starved > 0.5f && Grey < GreyCapacity * 0.2f)) // 오수 탱크에 남은 오수가 있어도 유량계는 안다
        {
            Suspect(w, "주 컴퓨터");
            Loop.Diagnosed++;
            w.Automation.Speak.Announce(w.Automation.Voice.Style("정수기는 멀쩡한데 오수가 들어오지 않습니다 — 회수관이 막힌 것 같습니다. 뚫을 때까지 배수구 밑에 임시 회수통을 받쳐 주십시오."), rec!.Room, 2);
            w.Automation.Book.Add(ActKind.Advice, rec.Room, $"배수구로 {_winDrained:0.#}L · 오수 탱크에 {_winReturned:0.#}L (30분) · 정수기 놀림 {Starved * 100:0}%",
                "정수기도 배수구도 아니라 회수관 — 오수가 탱크에 닿지 않는다", "회수관 뚫기 · 임시 회수통", "", "watermain", SimTime.Hours(6));
            return;
        }
        // 사람: 배수구가 여기저기서 올라오고 뚫어도 또 올라온다 → 손재주 있는 사람이 안쪽을 의심한다
        if (_backups < 3 || _backupRooms.Count < 2 || _vain < 2) return;
        CrewMember? who = null;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.LeftShip || !c.IsAwake || c.IsChild || c.SkillLevel(Skill.Mechanics) < 0.4f) continue;
            if (who == null || c.SkillLevel(Skill.Mechanics) + (c.Room != null && _backupRooms.Contains(c.Room.Id) ? 0.3f : 0f) > who.SkillLevel(Skill.Mechanics) + (who.Room != null && _backupRooms.Contains(who.Room.Id) ? 0.3f : 0f)) who = c;
        }
        if (who == null) return;
        Suspect(w, who.Name);
        Loop.CrewDiagnosed++;
        who.Say(w, Persona.Say(who, "배수구가 다 같이 올라오잖아 — 하나씩 막힌 게 아니라 안쪽 회수관이야"));
        MarkLog.Add(who.Memory.Marks, w.Tick, "배수구가 다 같이 올라와 회수관을 의심했다");
    }

    /// <summary>회수관 탓에 올라온 배수구를 뚫었는데 안쪽이 그대로다.</summary>
    internal void ClearedInVain()
    {
        Loop.InVain++;
        _vain++;
    }

    private void Suspect(World w, string by)
    {
        Suspected = true;
        SuspectedAt = w.Tick;
        SuspectedBy = by;
        w.Log.Add(w.Tick, LogKind.Warning, $"{by}: 회수관이 막혔다 — 정수기는 멀쩡한데 오수가 들어오지 않는다");
    }

    /// <summary>역세척 (FlushMain): 깨끗한 물을 거꾸로 밀어 찌꺼기를 씻어 낸다 (쓴 물은 오수 탱크로).</summary>
    internal void Flushed(World w, string by)
    {
        Sludge = MathF.Max(0.08f, Sludge - 0.55f);
        Loop.Flushes++;
        float use = MathF.Min(15f, Level);
        Level -= use;
        Drain(use);
        w.Log.Add(w.Tick, LogKind.Work, $"{by}: 회수관을 역세척했다 — 유량 {Pass * 100:0}%");
    }

    /// <summary>뚫었다 (ClearMain): 찌꺼기 덩어리를 빼낸다 · 회수관 탓에 올라오던 배수구는 가라앉는다.</summary>
    internal void Cleared(World w, string by)
    {
        Sludge = 0.06f;
        Loop.Clears++;
        Suspected = false;
        SuspectedBy = null;
        _backups = 0;
        _vain = 0;
        _backupRooms.Clear();
        foreach (var d in w.Drains.Drains) if (d.MainBack) w.Drains.MainCleared(d);
        w.Log.Add(w.Tick, LogKind.Work, $"{by}: 회수관을 뚫었다 — 찌꺼기 덩어리가 나왔다 · 오수가 다시 정수기로 간다");
    }

    /// <summary>임시 회수통을 둘 곳: 회수관 탓에 역류한 배수구 가운데, 통이 없는 방의 첫 배수구.</summary>
    public static (Room room, Cell at)? BucketSpot(World w)
    {
        var water = w.Water;
        Drain? best = null;
        int bestScore = -1;
        foreach (var d in w.Drains.Drains)
        {
            if (!d.Backflow || water.Buckets.Any(b => b.RoomId == d.RoomId)) continue;
            var room = w.Ship.Rooms[d.RoomId];
            if (room.Detached || room.OffLimits) continue;
            int score = (d.Kind is DrainKind.Shower or DrainKind.Sink ? 2 : 1) * 10 + (d.MainBack ? 5 : 0);
            if (score > bestScore) { bestScore = score; best = d; }
        }
        return best == null ? null : (w.Ship.Rooms[best.RoomId], best.At);
    }

    internal WaterBucket PlaceBucket(World w, Room room, Cell at, string by)
    {
        var b = new WaterBucket { Id = _nextBucket++, RoomId = room.Id, At = at, Since = w.Tick, By = by };
        Buckets.Add(b);
        Loop.Buckets++;
        MarkLog.Add(room.Marks, w.Tick, $"{by}: 배수구 밑에 임시 회수통");
        w.Log.Add(w.Tick, LogKind.Work, $"{by}: {room.Name} 배수구 밑에 임시 회수통을 받쳤다 — 회수관을 뚫을 때까지");
        return b;
    }

    /// <summary>회수통을 정수기에 붓는다.</summary>
    internal void PourBucket(WaterBucket b)
    {
        Grey = MathF.Min(GreyCapacity, Grey + b.Fill);
        b.Fill = 0f;
        Loop.BucketTrips++;
    }

    internal void RemoveBucket(WaterBucket b) => Buckets.Remove(b);

    /// <summary>점검 · 결정론 지문.</summary>
    public void HashLoop(Action<long> I, Action<float> F)
    {
        F(Grey); F(Sludge); I(Suspected ? 1 : 0); I(Buckets.Count);
        foreach (var b in Buckets) { I(b.RoomId); F(b.Fill); }
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v19 물 순환: 회수관 역세척 · 뚫기 · 임시 회수통 놓기 · 비우기.</summary>
    private void ScanWaterLoop(Poster post)
    {
        var w = _world;
        var water = w.Water;
        if (WaterSystem.Recycler(w) is Furniture rec)
        {
            bool computer = !WaterSystem.NoComputer && w.Automation.CoreOnline;
            // 예방: 유량계가 회수관이 가늘어진 걸 보면 역세척 (컴퓨터가 있어야 안다)
            if (computer && !water.Suspected && water.Pass < 0.88f && water.Pass >= 0.3f)
                post(WorkKind.FlushMain, WorkTarget.Of(rec), 0.3f, Skill.Mechanics, $"회수관 유량 {water.Pass * 100:0}% — 찌꺼기가 쌓였다 · 역세척");
            if (water.Suspected && water.Pass < 0.85f && !WaterSystem.HoldClear)
                post(WorkKind.ClearMain, WorkTarget.Of(rec), 0.72f, Skill.Mechanics, $"정수기는 멀쩡한데 오수가 안 들어온다 ({water.SuspectedBy}) — 회수관을 뚫는다", minSkill: 0.3f);
        }
        // 임시 회수통: 짚은 뒤 뚫기 전까지 · 역류한 방 배수구 밑에 (둘까지)
        if (water.Suspected && water.Pass < 0.4f && water.Buckets.Count < 2 && WaterSystem.BucketSpot(w) is (Room room, Cell at))
            post(WorkKind.SetBucket, WorkTarget.AtCell(at, room), 0.55f, Skill.Mechanics, $"회수관을 뚫을 때까지 {room.Name} 배수구 밑에 임시 회수통");
        foreach (var b in water.Buckets)
        {
            if (!b.Placed) continue;
            bool done = water.Pass >= 0.85f;
            if (b.Fill >= WaterBucket.Cap * 0.7f || done)
                post(WorkKind.EmptyBucket, WorkTarget.AtCell(b.At, w.Ship.Rooms[b.RoomId]), done ? 0.25f : 0.5f, Skill.Mechanics,
                    done ? "회수관을 뚫었다 — 통을 비우고 치운다" : $"임시 회수통 {b.Fill:0}L — 정수기에 붓는다", circuit: b.Id);
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>로봇이 맡는 물 일 (역세척 · 회수통 나르기): 맡을 로봇이 있으면 사람은 나서지 않는다 — 한 시간 넘게 아무도 안 맡았을 때만 (로봇이 고장 · 방전 · 길이 막힘).</summary>
    internal static bool RobotFirst(World w, WorkOrder o) =>
        o.Kind is WorkKind.FlushMain or WorkKind.EmptyBucket or WorkKind.LookRoom or WorkKind.SealSeep && w.Tick - o.Posted < SimTime.Hours(1) && w.Robots.Robots.Any(r => r.Operational && RobotSystem.CanDo(r.Kind, o.Kind));

    private static Job? FlushMain(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1f, Skill.Mechanics, o.Target.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) => { if (!o.Closed) { world.Water.Flushed(world, cm.Name); world.Board.Close(o); } return true; }));
        return Wrap(a, o, c, w, "회수관 역세척", toils, "회수관을 역세척하러 간다");
    }

    private static Job? ClearMain(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        if (c.SkillLevel(Skill.Mechanics) < 0.3f) { blocked = "손재주가 모자란다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1.5f, Skill.Mechanics, o.Target.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) => { if (!o.Closed) { world.Water.Cleared(world, cm.Name); world.Board.Close(o); } return true; }));
        return Wrap(a, o, c, w, "회수관 뚫기", toils, "막힌 회수관을 뚫으러 간다");
    }

    private static Job? SetBucket(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        if (o.Target.Room is not Room room) { w.Board.Close(o); return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WaitToil(SimTime.Minutes(2), Pose.Working, at.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!o.Closed && world.Water.Buckets.Count < 2 && !world.Water.Buckets.Any(b => b.RoomId == room.Id)) world.Water.PlaceBucket(world, room, at, cm.Name);
            world.Board.Close(o);
            return true;
        }));
        return Wrap(a, o, c, w, "임시 회수통", toils, $"{room.Name} 배수구 밑에 받칠 통을 들고 간다");
    }

    private static Job? EmptyBucket(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var b = w.Water.Buckets.FirstOrDefault(x => x.Id == o.Circuit);
        var rec = WaterSystem.Recycler(w);
        if (b == null || !b.Placed) { if (b == null) w.Board.Close(o); return null; }
        if (rec == null || !dist.Reachable(rec.UseSpots[0])) { blocked = "정수기에 갈 수 없다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(b.At));
        toils.Add(new WaitToil(SimTime.Minutes(1), Pose.Working, b.At.Center));
        toils.Add(new DoToil((cm, world) => { if (!b.Placed) return false; b.HeldBy = cm.Id; return true; }));
        toils.Add(new GotoToil(rec.UseSpots[0]));
        toils.Add(new WaitToil(SimTime.Minutes(1), Pose.Working, rec.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Water.PourBucket(b);
            if (world.Water.Pass >= 0.85f) { world.Water.RemoveBucket(b); world.Board.Close(o); MarkLog.Add(cm.Memory.Marks, world.Tick, "회수통을 비우고 치웠다"); }
            return true;
        }));
        // 아직 막혀 있으면 제자리로 (치웠으면 빈손으로 돌아간다)
        toils.Add(new GotoToil(b.At, cm => cm.Id == b.HeldBy && w.Water.Buckets.Contains(b)));
        toils.Add(new DoToil((cm, world) => { if (b.HeldBy == cm.Id) b.HeldBy = -1; world.Board.Close(o); return true; }));
        return Wrap(a, o, c, w, "회수통 비우기", toils, "임시 회수통을 정수기에 붓는다");
    }
}
