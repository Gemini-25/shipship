using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v10.10 자원 장부. 채집 속도 하나를 보는 대신 <b>하루 수입 · 사고마다 쓴 양 · 다시 비축하는 데 걸린 시간</b>을 함께 적는다.
/// 공기 탱크·실링폼·냉각수·금속판·물·식량·얼음의 양을 시스템 틱마다 재어, 늘면 수입, 줄면 소비로 센다 (같은 틱 안의 들고 남은 합쳐진다).
/// 사고(에피소드)가 열려 있는 동안의 소비는 그 사고 몫이고, 사고가 끝난 뒤 사고 전 수준(95%)으로 돌아오기까지 걸린 시간을 적는다.
/// </summary>
public sealed class ResourceLedger
{
    public static readonly (string Key, string Name, string Unit)[] Kinds =
    {
        ("air", "공기 탱크", "%"), ("sealant", "실링폼", "개"), ("coolant", "냉각수", "L"), ("plate", "금속판", "개"),
        ("water", "물", "L"), ("food", "식량", "끼"), ("ice", "얼음", "개"),
    };

    public sealed class Flow
    {
        public float In;
        public float Out;
    }

    public sealed class DayRecord
    {
        public int Day { get; init; }
        public Dictionary<string, Flow> Flows { get; } = Kinds.ToDictionary(k => k.Key, _ => new Flow());
        public Dictionary<string, float> End { get; } = new();
    }

    /// <summary>사고 하나의 장부: 사고 전 수준, 사고 중 쓴 양, 바닥, 회복한 때.</summary>
    public sealed class EpisodeLedger
    {
        public int EpisodeId { get; init; }
        public string Cause { get; set; } = "";
        public long Start { get; init; }
        public long End { get; set; } = -1;
        public Dictionary<string, float> Before { get; } = new();
        public Dictionary<string, float> Used { get; } = new();
        public Dictionary<string, float> Gained { get; } = new();
        public Dictionary<string, float> Low { get; } = new();
        /// <summary>사고 전 수준의 95%로 돌아온 틱 (돌아오지 않았으면 없다). 떨어지지 않았으면 끝난 틱.</summary>
        public Dictionary<string, long> Recovered { get; } = new();
        public bool Open => End < 0;
    }

    public List<DayRecord> Days { get; } = new();
    public List<EpisodeLedger> Episodes { get; } = new();
    public DayRecord? Today { get; private set; }
    private readonly Dictionary<string, float> _last = new();

    /// <summary>지금 비축 방침 (평시 · 사고 직후 · 극한).</summary>
    public StockMode Mode { get; private set; } = StockMode.Normal;
    public string ModeWhy { get; private set; } = "";
    public long ModeSince { get; private set; }

    public static float Level(World w, string key) => key switch
    {
        "air" => w.Air.ReserveCapacity > 0f ? w.Air.Reserve / w.Air.ReserveCapacity * 100f : 0f,
        "sealant" => w.Board.Have(ItemKind.Sealant),
        "coolant" => w.Piping.Coolant,
        "plate" => w.Board.Have(ItemKind.Plate),
        "water" => w.Water.Level,
        // 식량은 끼니로: 식사 1 · 비상식량 1 · 채소 1.5 (채소 넷 → 식사 여섯)
        "food" => w.Board.Have(ItemKind.Meal) + w.Board.Have(ItemKind.Ration) + 1.5f * w.Board.Have(ItemKind.Produce),
        "ice" => w.Board.Have(ItemKind.Ice),
        _ => 0f,
    };

    public static string Name(string key) => Kinds.FirstOrDefault(k => k.Key == key).Name ?? key;
    public static string Unit(string key) => Kinds.FirstOrDefault(k => k.Key == key).Unit ?? "";

    public void Sample(World w, float dt)
    {
        int day = w.Day;
        if (Today == null || Today.Day != day)
        {
            if (Today != null)
            {
                foreach (var (key, _, _) in Kinds) Today.End[key] = _last.GetValueOrDefault(key);
                Days.Add(Today);
                if (Days.Count > 400) Days.RemoveAt(0);
            }
            Today = new DayRecord { Day = day };
        }

        // 사고 장부를 연다 (에피소드가 열리면 바로 전 표본을 사고 전 수준으로)
        var ep = w.History.Current;
        EpisodeLedger? open = Episodes.Count > 0 && Episodes[^1].Open ? Episodes[^1] : null;
        if (ep != null && (open == null || open.EpisodeId != ep.Id))
        {
            if (open != null) open.End = w.Tick;
            open = new EpisodeLedger { EpisodeId = ep.Id, Start = ep.Start, Cause = ep.Cause };
            foreach (var (key, _, _) in Kinds)
            {
                float before = _last.TryGetValue(key, out var v) ? v : Level(w, key);
                open.Before[key] = before;
                open.Low[key] = before;
            }
            Episodes.Add(open);
            if (Episodes.Count > 200) Episodes.RemoveAt(0);
        }
        if (open != null && ep != null) open.Cause = ep.Cause;
        if (open != null && ep == null) open.End = w.Tick;

        foreach (var (key, _, _) in Kinds)
        {
            float v = Level(w, key);
            if (_last.TryGetValue(key, out var prev))
            {
                float d = v - prev;
                var f = Today.Flows[key];
                if (d > 0f) f.In += d; else f.Out -= d;
                if (open != null && open.Open)
                {
                    if (d < 0f) open.Used[key] = open.Used.GetValueOrDefault(key) - d;
                    else open.Gained[key] = open.Gained.GetValueOrDefault(key) + d;
                }
            }
            _last[key] = v;
            // 최근 사고들의 바닥과 회복 (30일까지 지켜본다)
            foreach (var e in Episodes)
            {
                if (e.Recovered.ContainsKey(key) || w.Tick - e.Start > 30L * SimTime.TicksPerDay) continue;
                if (v < e.Low.GetValueOrDefault(key, v)) e.Low[key] = v;
                if (e.Open) continue;
                float before = e.Before.GetValueOrDefault(key);
                bool dipped = e.Low.GetValueOrDefault(key, before) < before * 0.95f - 0.5f;
                if (!dipped) e.Recovered[key] = e.End;
                else if (v >= before * 0.95f - 0.01f) e.Recovered[key] = w.Tick;
            }
        }
        UpdateMode(w);
    }

    /// <summary>
    /// 상황별 비축 방침. 평시엔 교훈의 목표대로, 사고 직후엔 쓴 것부터 서둘러 다시 채우고(목표 +30%),
    /// 극한(공기 탱크 25% 아래·실링폼 없이 새는 방·물 15% 아래·먹을 것 이틀치 아래)엔 살 길(실링폼·금속판·얼음)만 먼저.
    /// </summary>
    private void UpdateMode(World w)
    {
        int crew = Math.Max(1, w.Crew.Count(c => !c.Dead));
        float air = Level(w, "air");
        string? extreme = air < 25f ? $"공기 탱크 {air:0}%"
            : w.Board.Have(ItemKind.Sealant) == 0 && w.Ship.Rooms.Any(r => r.Leaking && !r.Abandoned) ? "실링폼 없이 새는 방"
            : w.Water.Level < w.Water.Capacity * 0.15f ? $"물 {w.Water.Level:0}L"
            : Level(w, "food") < crew * 2f * 3f ? "먹을 것 이틀치 아래"
            : null;
        string? post = null;
        if (extreme == null)
            foreach (var e in Episodes.Where(e => !e.Open && w.Tick - e.End < SimTime.Hours(72)).Reverse())
            {
                var short1 = Kinds.Select(k => k.Key).Where(k => k is "air" or "sealant" or "plate" or "coolant")
                    .FirstOrDefault(k => !e.Recovered.ContainsKey(k) && Level(w, k) < e.Before.GetValueOrDefault(k) * 0.8f);
                if (short1 != null) { post = $"{e.Cause}로 쓴 {Name(short1)}"; break; }
            }
        var mode = extreme != null ? StockMode.Extreme : post != null ? StockMode.Recovery : StockMode.Normal;
        if (mode != Mode)
        {
            Mode = mode;
            ModeSince = w.Tick;
            w.Log.Add(w.Tick, LogKind.Ship, mode switch
            {
                StockMode.Extreme => $"비축 방침: 극한 생존 — {extreme} (살 길부터: 실링폼·금속판·얼음)",
                StockMode.Recovery => $"비축 방침: 사고 직후 — {post}부터 다시 채운다",
                _ => "비축 방침: 평시로 돌아왔다",
            });
        }
        ModeWhy = extreme ?? post ?? "";
        w.History.Doctrine.Mode = Mode;
    }

    /// <summary>최근 n일 동안의 하루 평균 수입·소비.</summary>
    public (float inPerDay, float outPerDay) Average(string key, int days)
    {
        var list = Days.TakeLast(days).ToList();
        if (list.Count == 0) return (0f, 0f);
        return (list.Average(d => d.Flows[key].In), list.Average(d => d.Flows[key].Out));
    }

    /// <summary>사고 장부 한 줄: 쓴 양과 회복까지 걸린 날 ("운석: 공기 −31%p(7.2일) · 실링폼 −6(2.1일) · 금속판 −4(아직)").</summary>
    public string Describe(EpisodeLedger e, long now)
    {
        var parts = new List<string>();
        foreach (var (key, name, unit) in Kinds)
        {
            float before = e.Before.GetValueOrDefault(key);
            float low = e.Low.GetValueOrDefault(key, before);
            if (low >= before * 0.95f - 0.5f) continue;
            string drop = unit == "%" ? $"−{before - low:0}%p" : $"−{before - low:0}{unit}";
            string back = e.Recovered.TryGetValue(key, out var t) ? $"{(t - e.End) / (float)SimTime.TicksPerDay:0.0}일" : e.Open ? "진행 중" : "아직";
            parts.Add($"{name} {drop}({back})");
        }
        return parts.Count == 0 ? "비축이 흔들리지 않았다" : string.Join(" · ", parts);
    }
}

public enum StockMode { Normal, Recovery, Extreme }

/// <summary>
/// v10.10 군수: 창고의 비축 목표, 비상 물자함(나눠 둔 실링폼·구급 키트·소화기), 물통 급수, 사고 뒤 정리(임시 배선 걷기·간이침대 치우기·뜯긴 설비 재활용).
/// 군수 담당은 따로 두지 않고 정비사·운반 로봇의 겸직이다 (일은 작업 목록에 오르고, 운반 로봇이 먼저 가져간다).
/// </summary>
public static class Logistics
{
    public static string ModeName(StockMode m) => m switch
    {
        StockMode.Recovery => "사고 직후",
        StockMode.Extreme => "극한 생존",
        _ => "평시",
    };

    /// <summary>비상 물자함 하나에 두는 것 (실링폼 3 · 구급 키트 1 · 소화기 1). 극한이면 실링폼을 창고에 모은다.</summary>
    public static (ItemKind kind, int count)[] CacheTarget(World w) =>
        w.Ledger.Mode == StockMode.Extreme
            ? new[] { (ItemKind.Sealant, 1), (ItemKind.MedKit, 1), (ItemKind.Extinguisher, 1) }
            : new[] { (ItemKind.Sealant, 3), (ItemKind.MedKit, 1), (ItemKind.Extinguisher, 1) };

    /// <summary>창고에 남겨 둘 최소량 (비상 물자함으로 다 빼 가지 않게).</summary>
    public static int Keep(ItemKind k) => k switch { ItemKind.Sealant => 4, ItemKind.MedKit => 2, ItemKind.Extinguisher => 1, _ => 0 };

    /// <summary>비상 물자함 수 (창고 밖에 나눠 둔 것).</summary>
    public static int Caches(World w) => w.Ship.FurnitureOf(FurnitureType.SupplyCache).Count();

    /// <summary>재배대에 물통으로 물을 나를 수 있는 곳 (정수기의 물꼭지). 없으면 null.</summary>
    public static Furniture? WaterTap(World w) =>
        w.Ship.FurnitureOf(FurnitureType.WaterRecycler).Where(f => !f.Room.Abandoned && !f.Room.OffLimits && f.UseSpots.Count > 0)
            .OrderBy(f => f.Room.Leaking ? 1 : 0).ThenBy(f => f.Id).FirstOrDefault();

    /// <summary>물통 한 번에 나르는 물 (재배대 하나를 열두 시간 적신다).</summary>
    public static float JugLiters(Furniture bed) => WaterSystem.BedLitersPerHour * FoodChain.BedSize(bed) * HandWaterHours;
    public const float HandWaterHours = 12f;

    // ─────────────────────────────── 로봇이 하는 군수 ───────────────────────────────

    internal static List<RobotStep>? RobotCarryWater(RobotSystem sys, Robot r, WorkOrder o, Cell at, DistanceField dist, out string? blocked)
    {
        blocked = null;
        var w = sysWorld(sys);
        var bed = o.Target.Furniture!;
        var tap = WaterTap(w);
        if (tap == null) { blocked = "물꼭지 없음"; return null; }
        var tapSpot = tap.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (tapSpot is not Cell ts) { blocked = "물꼭지에 갈 수 없음"; return null; }
        float liters = JugLiters(bed);
        bool filled = false;
        return new List<RobotStep>
        {
            new RGoto(ts),
            new RWork(0.05f, null, tap.Center),
            new RDo((rb, world) =>
            {
                if (world.Water.Level < liters + 5f) return false;
                world.Water.Level -= liters;
                filled = true;
                return true;
            }),
            new RGoto(at),
            new RWork(0.12f, null, bed.Center),
            new RDo((rb, world) =>
            {
                if (!filled || bed.Machine?.Crop is not CropState crop) return false;
                crop.HandWateredHours = HandWaterHours;
                world.Board.Close(o);
                world.Robots.Done(rb, $"{bed.Label}에 물통으로 물 {liters:0.0}L를 부었다 (급수 본관이 끊겼다)");
                return true;
            }),
        };
    }

    internal static List<RobotStep>? RobotStockCache(RobotSystem sys, Robot r, WorkOrder o, Cell at, DistanceField dist, out string? blocked)
    {
        blocked = null;
        var w = sysWorld(sys);
        var cache = o.Target.Furniture!;
        var kind = o.Product ?? ItemKind.Sealant;
        int want = WantIn(w, cache, kind);
        var (shelf, shelfSpot) = RobotSystem.NearestFor(w, dist, f => f.Type == FurnitureType.Shelf && f.Storage!.Count(kind) > Keep(kind));
        if (shelf == null || want <= 0) { blocked = $"{ItemKinds.Name(kind)} 여유 없음"; return null; }
        int take = Math.Min(want, shelf.Storage!.Count(kind) - Keep(kind));
        return new List<RobotStep>
        {
            new RGoto(shelfSpot),
            new RTake(shelf, kind, take),
            new RGoto(at),
            new RPut(cache),
            new RDo((rb, world) => { world.Board.Close(o); world.Robots.Done(rb, $"{cache.Label}에 {ItemKinds.Name(kind)} {take}개를 채웠다"); return true; }),
        };
    }

    private static World sysWorld(RobotSystem sys) => sys.World;

    /// <summary>비상 물자함에 더 넣을 개수.</summary>
    public static int WantIn(World w, Furniture cache, ItemKind kind)
    {
        int target = CacheTarget(w).FirstOrDefault(x => x.kind == kind).count;
        return Math.Max(0, target - cache.Storage!.Count(kind));
    }

    /// <summary>창고(선반)에 비상 물자함으로 뺄 여유가 있는지.</summary>
    public static int Spare(World w, ItemKind kind) =>
        w.Ship.FurnitureOf(FurnitureType.Shelf).Sum(f => Math.Max(0, f.Storage!.Count(kind))) - Keep(kind);

    /// <summary>비상 물자함을 둘 방: 파공·불을 겪었거나 창고에서 먼 방 (한 방에 하나).</summary>
    public static IEnumerable<(Room room, float score, string why)> CacheRooms(World w)
    {
        var h = w.History;
        var storage = w.Ship.RoomsOf(RoomType.Storage).FirstOrDefault();
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.Abandoned || room.Leaking || room.OffLimits || room.Type is RoomType.Storage or RoomType.Corridor or RoomType.Airlock) continue;
            if (room.Furniture.Any(f => f.Type == FurnitureType.SupplyCache)) continue;
            int breaches = h.BreachesByRoom.GetValueOrDefault(room.Id);
            int fires = h.FiresByRoom.GetValueOrDefault(room.Id);
            float far = storage == null ? 1f : MathF.Min(1f, (room.Center - storage.Center).Length() / 40f);
            bool vital = room.Type is RoomType.Reactor or RoomType.Cooling or RoomType.Power or RoomType.LifeSupport or RoomType.Bridge or RoomType.Quarters;
            if (breaches + fires == 0 && !(vital && far > 0.5f)) continue;
            float score = 0.25f + 0.2f * breaches + 0.15f * fires + 0.25f * far + (vital ? 0.1f : 0f);
            string why = breaches + fires > 0
                ? $"{room.Name}에서 파공 {breaches}번 · 불 {fires}번"
                : $"{room.Name}은 창고에서 멀다";
            yield return (room, score, why);
        }
    }

    public static void InstallCache(World w, Room room, Cell at, CrewMember cm)
    {
        var f = w.Ship.AddFurniture(FurnitureType.SupplyCache, at);
        f.Storage = new Inventory(10, ItemKind.Sealant, ItemKind.MedKit, ItemKind.Extinguisher);
        f.Improved = true;
        MarkLog.Add(room.Marks, w.Tick, $"{cm.Name}: 비상 물자함");
        w.Paths.Invalidate();
        w.Structure.Touch();
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v10.10 자원 회복: 물통 급수, 비상 물자함 채우기, 사고 뒤 정리.</summary>
    private void ScanRecovery(Poster post)
    {
        var w = _world;
        var ship = w.Ship;

        // ── 물통 급수: 급수 본관이 끊겨 재배대에 물이 안 가면 정수기 꼭지에서 물통으로 나른다 ──
        if (Logistics.WaterTap(w) != null && w.Water.Level > 25f)
            foreach (var bed in ship.FurnitureOf(FurnitureType.GrowBed))
            {
                if (bed.Room.Abandoned || bed.Room.OffLimits || bed.Machine?.Crop is not CropState crop) continue;
                if (crop.Ripe || crop.Growth < 0.02f || w.Piping.WaterTo(bed.Room) || crop.HandWateredHours > 3f) continue;
                if (bed.Room.Air.Pressure < 40f) continue; // 진공이면 어차피 얼어 죽는다
                post(WorkKind.CarryWater, WorkTarget.Of(bed), 0.42f + MathF.Min(0.5f, crop.DryHours / 40f), Skill.Botany,
                    $"급수 본관이 끊겼다 · 마른 지 {crop.DryHours:0}시간 · 물통 {Logistics.JugLiters(bed):0.0}L");
            }

        // ── 비상 물자함 채우기 (창고에 여유가 있을 때만, 사고 직후엔 서두른다) ──
        foreach (var cache in ship.FurnitureOf(FurnitureType.SupplyCache))
        {
            if (cache.Room.Abandoned || cache.Room.OffLimits || cache.Room.Leaking) continue;
            foreach (var (kind, _) in Logistics.CacheTarget(w))
            {
                int want = Logistics.WantIn(w, cache, kind);
                if (want <= 0 || Logistics.Spare(w, kind) <= 0) continue;
                float u = 0.16f + 0.06f * want + (w.Ledger.Mode == StockMode.Recovery ? 0.2f : 0f);
                post(WorkKind.StockCache, WorkTarget.Of(cache), u, Skill.Mechanics,
                    $"{ItemKinds.Name(kind)} {cache.Storage!.Count(kind)}/{want + cache.Storage.Count(kind)} · 비축 방침 {Logistics.ModeName(w.Ledger.Mode)}", product: kind);
            }
        }

        // ── 사고 뒤 정리: 제 회로가 살아난 지 두 시간 넘은 임시 배선은 걷어 케이블을 되찾는다 (흔적은 남는다) ──
        var p = w.Power;
        if (ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault() is Furniture panel && w.History.Current == null)
            foreach (var j in p.Jumpers.Where(j => !j.Permanent))
            {
                if (!p.CircuitLive[j.To] && !j.Burnt) continue;
                if (w.Tick - p.LiveSince(j.To) < SimTime.Hours(2) && !j.Burnt) continue;
                float idleH = (w.Tick - p.LiveSince(j.To)) / (float)SimTime.TicksPerHour; // v12.6 미룬 정리는 점점 급해진다 (당직에 밀려 며칠씩 남던 것)
                post(WorkKind.RemoveJumper, WorkTarget.Of(panel), j.Burnt ? 0.3f : MathF.Min(0.42f, 0.22f + 0.01f * MathF.Max(0f, idleH - 2f)), Skill.Electrical,
                    j.Burnt ? "타 버린 임시 배선 — 걷어 낸다" : $"{PowerGrid.CircuitName(j.To)} 회로가 제 힘으로 돈다 → 케이블 {PowerGrid.JumperCables}개를 되찾는다",
                    circuit: j.To);
            }

        // ── 간이침대 치우기: 주인이 원래 침대로 돌아가 빈 채로 하루 넘은 간이침대 ──
        foreach (var cot in ship.FurnitureOf(FurnitureType.Cot))
        {
            if (cot.Owner != null) { cot.EmptySince = 0; continue; }
            if (cot.EmptySince == 0) cot.EmptySince = w.Tick;
            if (cot.Improved || cot.Room.Abandoned || cot.Room.OffLimits) continue;
            if (w.Tick - cot.EmptySince < SimTime.Hours(24)) continue;
            post(WorkKind.StowCot, WorkTarget.Of(cot), 0.28f, Skill.Mechanics, $"빈 지 {(w.Tick - cot.EmptySince) / (float)SimTime.TicksPerDay:0}일 · 접어 창고로");
        }

        // ── 뜯긴 설비 재활용: 되돌릴 값이 없는 적출 설비는 금속판·케이블로 (회의) ──
        if (Have(ItemKind.Plate) < 8)
            foreach (var m in ship.Machines)
            {
                if (!m.Has(FaultKind.Stripped) || m.Body.Room.Abandoned || m.Body.Room.OffLimits) continue;
                if (Adaptation.Worth(m, asDonor: false) >= 10f || m.Spec.Critical) continue;
                var since = m.Faults.First(f => f.Kind == FaultKind.Stripped).Since;
                if (w.Tick - since < SimTime.Hours(48)) continue;
                post(WorkKind.Recycle, WorkTarget.Of(m.Body), 0.2f + (8 - Have(ItemKind.Plate)) * 0.02f, Skill.Mechanics,
                    $"뜯긴 지 {(w.Tick - since) / (float)SimTime.TicksPerDay:0}일 · 금속판 {Have(ItemKind.Plate)}개 → 금속판 {Recycle.Yield(m.Body.Type).Sum(x => x.count)}개어치",
                    product: ItemKind.Plate);
            }
    }
}

/// <summary>v10.10 재활용: 뜯긴 설비를 해체해 기본 수리재를 되찾는다 (설비는 사라지고 자리만 남는다).</summary>
public static class Recycle
{
    public static (ItemKind kind, int count)[] Yield(FurnitureType t) => t switch
    {
        FurnitureType.Console => new[] { (ItemKind.Plate, 1), (ItemKind.Cable, 1) },
        FurnitureType.EngineCore => new[] { (ItemKind.Plate, 3), (ItemKind.Cable, 1) },
        FurnitureType.MedBed or FurnitureType.Workbench or FurnitureType.Stove => new[] { (ItemKind.Plate, 2) },
        _ => new[] { (ItemKind.Plate, 1) },
    };
}
