using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v23 행성 기지: 땅에 있는 동안 배 둘레에서 하는 일.
//   채굴 — 드론이 배 둘레 땅의 광맥 · 얼음 · 모래 자리로 날아가 캐 온다 (채굴 드론이 가장 많이).
//   정제 — 배 안에서: 얼음 → 물 · 추진제, 금속 원료 → 금속판(제작기), 탄소 → 필터.
//   기지 — 주컴퓨터가 날씨와 필요를 보고 짓는다: 방벽(바람막이) · 기상탑(피뢰침 · 예보) · 온실(채소) · 창고(채굴이 빨라짐). 날씨에 상한다.
//   탐사 — 정찰 · 측량 드론이 땅의 이름난 곳을 살피고, 원정대(원정 체계 그대로)가 먼 능선까지 걸어간다.
//   생존 — 불시착이면 모자란 것(추진제 · 판)부터 캔다. 떠날 때 기지는 남기고 쓸 만한 자재 절반을 걷어 간다.

public enum GroundKind : byte { Ore, Ice, Sand, Crystal, Carbon, Landmark }
public enum BuildKind : byte { Barrier, Tower, Greenhouse, Depot }

public sealed class GroundSite
{
    public int Id { get; init; }
    public GroundKind Kind { get; init; }
    public ItemKind Item { get; init; }
    public string Name { get; init; } = "";
    public Vector2 At { get; init; }
    public int Left { get; set; }
    public int Mined { get; set; }
    /// <summary>이름난 곳 (살펴보기 전엔 무엇이 있는지 모른다).</summary>
    public bool Surveyed { get; set; }
    public string Found { get; set; } = "";
}

public sealed class GroundBuild
{
    public int Id { get; init; }
    public BuildKind Kind { get; init; }
    public Vector2 At { get; init; }
    public float Work { get; set; }
    public float Health { get; set; } = 1f;
    public long Done { get; set; } = -1;
    public string Why { get; init; } = "";
    public bool Standing => Done >= 0 && Health > 0.15f;
}

public sealed partial class PlanetSystem
{
    public List<GroundSite> Sites { get; } = new();
    public List<GroundBuild> Builds { get; } = new();
    public Dictionary<ItemKind, int> Gathered { get; } = new();
    public int Refined { get; private set; }
    public int Surveys { get; private set; }
    private long _nextPlan = -1, _nextRefine = -1, _askedTrip = -1;
    private int _nextSite = 1, _nextBuild = 1;

    public static string BuildName(BuildKind k) => k switch { BuildKind.Barrier => "방벽", BuildKind.Tower => "기상탑", BuildKind.Greenhouse => "온실", _ => "창고" };
    private static float BuildHours(BuildKind k) => k switch { BuildKind.Barrier => 6f, BuildKind.Tower => 4.5f, BuildKind.Greenhouse => 7f, _ => 5f };
    private static (ItemKind, int)[] BuildCost(BuildKind k) => k switch
    {
        BuildKind.Barrier => new[] { (ItemKind.Structure, 2), (ItemKind.Plate, 2) },
        BuildKind.Tower => new[] { (ItemKind.Structure, 1), (ItemKind.Cable, 3) },
        BuildKind.Greenhouse => new[] { (ItemKind.Plate, 2), (ItemKind.Cable, 1), (ItemKind.Seed, 1) },
        _ => new[] { (ItemKind.Structure, 1), (ItemKind.Plate, 1) },
    };

    private GroundBuild? Built(BuildKind k) => Builds.FirstOrDefault(b => b.Kind == k && b.Standing);

    // ── 2단계 날씨가 묻는 것 ──
    private float BarrierShield() => Built(BuildKind.Barrier) is GroundBuild b ? 0.5f * b.Health : 0f;
    private bool TowerCatches() => Built(BuildKind.Tower) is GroundBuild t && _wx.Chance(0.65f * t.Health);
    private float TowerLead() => Built(BuildKind.Tower) is GroundBuild t ? 2f * t.Health : 0f;

    /// <summary>배 둘레 땅의 빈자리 (격자 안 · 선체에서 두 칸 넘게).</summary>
    private List<Vector2> GroundSpots()
    {
        var ship = _w.Ship;
        var grid = ship.Grid;
        var hull = ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Key).ToList();
        var spots = new List<Vector2>();
        if (hull.Count == 0) return spots;
        for (int y = 1; y < grid.Height - 1; y++)
            for (int x = 1; x < grid.Width - 1; x++)
            {
                var c = new Cell(x, y);
                if (grid.Kind(c) != TileKind.Void) continue;
                int d = hull.Min(h => Math.Abs(h.X - x) + Math.Abs(h.Y - y));
                if (d >= 2 && d <= 9) spots.Add(c.Center);
            }
        return spots;
    }

    private void StartBase(Planet p)
    {
        var w = _w;
        Sites.Clear(); Builds.Clear(); Gathered.Clear();
        Refined = 0; Surveys = 0;
        var spots = GroundSpots().OrderBy(_ => _wx.Float()).ToList();
        Vector2 Spot() { if (spots.Count == 0) return Vector2.Zero; var s = spots[0]; spots.RemoveAll(o => (o - s).LengthSquared() < 16f); return s; }
        foreach (var (k, v) in p.Rich.OrderByDescending(kv => kv.Value))
        {
            if (v < 0.15f || spots.Count == 0) continue;
            var kind = k switch { ItemKind.Ice or ItemKind.Fuel => GroundKind.Ice, ItemKind.Silicate => GroundKind.Sand, ItemKind.Rare => GroundKind.Crystal, ItemKind.Carbon => GroundKind.Carbon, _ => GroundKind.Ore };
            int n = 1 + (v > 0.6f ? 1 : 0);
            for (int i = 0; i < n && spots.Count > 0; i++)
                Sites.Add(new GroundSite { Id = _nextSite++, Kind = kind, Item = k == ItemKind.Fuel ? ItemKind.Ice : k, Name = SiteName(kind), At = Spot(), Left = (int)(6 + 24 * v * _wx.Range(0.7f, 1.3f)) });
        }
        int marks = 1 + _wx.Range(0, 2) + (p.Special ? 1 : 0);
        for (int i = 0; i < marks && spots.Count > 0; i++)
            Sites.Add(new GroundSite { Id = _nextSite++, Kind = GroundKind.Landmark, Item = ItemKind.Rare, Name = LandmarkName(p), At = Spot(), Left = 0 });
        _nextPlan = w.Tick + SimTime.Hours(1);
        _nextRefine = w.Tick + SimTime.Hours(1);
        _askedTrip = -1;
        // 원정대가 걸어갈 먼 곳 (원정 체계의 목적지로 · 떠나면 지운다)
        int far = 1 + (p.Special ? 1 : 0);
        for (int i = 0; i < far; i++)
            w.Expedition.AddPlanetSite(LandmarkName(p), _wx.Range(0.15f, 0.45f), Math.Clamp(p.Danger * 0.6f + 0.05f, 0.05f, 0.7f), 8f + 10f * p.Reward, Math.Clamp(0.75f + 0.2f * p.Certainty, 0.6f, 0.95f)); // 내려앉아 가까이서 본다
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {p.Name} 땅을 살핀다 — 캘 곳 {Sites.Count(s => s.Kind != GroundKind.Landmark)} · 이름난 곳 {Sites.Count(s => s.Kind == GroundKind.Landmark)}");
    }

    private string SiteName(GroundKind k) => k switch
    {
        GroundKind.Ore => new[] { "쇳돌 둔덕", "검은 광맥", "녹슨 바위 무더기", "철빛 절벽" }[_wx.Range(0, 4)],
        GroundKind.Ice => new[] { "얼음 웅덩이", "서리 굴", "언 샘", "푸른 얼음 벽" }[_wx.Range(0, 4)],
        GroundKind.Sand => new[] { "고운 모래톱", "유리 모래밭", "모래 둔덕" }[_wx.Range(0, 3)],
        GroundKind.Crystal => new[] { "수정 무더기", "빛나는 결정 틈", "수정 기둥 밑" }[_wx.Range(0, 3)],
        _ => new[] { "검은 진흙 웅덩이", "재 쌓인 골", "타르 늪가" }[_wx.Range(0, 3)],
    };

    private string LandmarkName(Planet p) => p.Surface switch
    {
        PlanetSurface.Crystal => new[] { "빛 기둥 골짜기", "노래하는 수정 숲", "깨진 수정 언덕" }[_wx.Range(0, 3)],
        PlanetSurface.IceSheet => new[] { "얼음 협곡", "갈라진 빙판 틈", "언 폭포" }[_wx.Range(0, 3)],
        PlanetSurface.Lava or PlanetSurface.Ash => new[] { "식은 용암굴", "분기공 언덕", "재 덮인 분화구" }[_wx.Range(0, 3)],
        PlanetSurface.Dunes => new[] { "바람 깎인 바위탑", "모래에 묻힌 능선", "유리 사막" }[_wx.Range(0, 3)],
        PlanetSurface.Shallows => new[] { "물에 잠긴 섬 끝", "소금 기둥 갯벌", "밀물 동굴" }[_wx.Range(0, 3)],
        _ => new[] { "운석 구덩이", "먼 능선 너머 골짜기", "바람 골짜기", "검은 바위 탑" }[_wx.Range(0, 4)],
    };

    /// <summary>기지 계획: 날씨 · 필요를 보고 무엇을 지을지 (한 번에 하나).</summary>
    private void PlanBuilds()
    {
        var w = _w;
        var p = Here!;
        if (Builds.Any(b => b.Done < 0)) return;
        float stayLeft = (StayUntil - w.Tick) / (float)SimTime.TicksPerDay;
        if (stayLeft < 0.4f) return;
        var want = new List<(BuildKind k, float score, string why)>();
        int thunder = p.Weathers.Count(x => x == PlanetWeather.Thunder), windy = p.Weathers.Count(x => Windy(x));
        if (Built(BuildKind.Tower) == null && !Builds.Any(b => b.Kind == BuildKind.Tower)) want.Add((BuildKind.Tower, 1f + 1.5f * thunder + (p.Has(PlanetFeature.Static) ? 0.5f : 0f), thunder > 0 ? "번개가 잦다 — 피뢰침 · 예보" : "날씨를 더 일찍 본다"));
        if (Built(BuildKind.Barrier) == null && !Builds.Any(b => b.Kind == BuildKind.Barrier)) want.Add((BuildKind.Barrier, 0.5f + 1.2f * windy, windy > 0 ? "바람이 세다 — 바람막이" : "바람막이"));
        if (Built(BuildKind.Depot) == null && !Builds.Any(b => b.Kind == BuildKind.Depot)) want.Add((BuildKind.Depot, 0.4f + 2f * p.Reward, "캘 것이 많다 — 땅에 쌓아 두고 캔다"));
        float foodDays = w.Expedition.DaysLeft(MatCat.Food);
        if (Built(BuildKind.Greenhouse) == null && !Builds.Any(b => b.Kind == BuildKind.Greenhouse) && p.Climate != PlanetClimate.Scorched && stayLeft > 1f)
            want.Add((BuildKind.Greenhouse, 0.3f + (foodDays < 5f ? 2f : 0f), foodDays < 5f ? "먹을 것이 모자란다 — 온실" : "신선한 채소"));
        var pick = want.Where(x => x.score >= 0.9f && BuildCost(x.k).All(c => w.Ship.CountStored(c.Item1) >= c.Item2)).OrderByDescending(x => x.score).FirstOrDefault();
        if (pick.why == null) return;
        foreach (var (k, n) in BuildCost(pick.k)) Life.Take(w, k, n);
        var spots = GroundSpots().Where(s => Sites.All(x => (x.At - s).LengthSquared() > 9f) && Builds.All(b => (b.At - s).LengthSquared() > 9f)).ToList();
        if (spots.Count == 0) return;
        var at = spots[_wx.Range(0, spots.Count)];
        Builds.Add(new GroundBuild { Id = _nextBuild++, Kind = pick.k, At = at, Why = pick.why });
        Note($"{BuildName(pick.k)}을 짓는다 — {pick.why}");
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {p.Name} 땅에 {BuildName(pick.k)}을 짓는다 — {pick.why} (드론이 짓는다)");
    }

    /// <summary>드론 한 대에게 땅 일감 (짓기 → 살피기 → 캐기). 없으면 null.</summary>
    internal (DroneErrand e, Vector2 at, int target, string label)? DroneJob(Drone d)
    {
        if (!Landed || Sheltering || Here == null) return null;
        var kind = RobotsV15.Base(d.Kind);
        bool builder = d.Kind is DroneKind.Build or DroneKind.Rigger or DroneKind.Welder || kind == DroneKind.Repair;
        bool scout = d.Kind is DroneKind.Scout or DroneKind.Surveyor || kind == DroneKind.Inspect;
        var build = Builds.Where(b => b.Done < 0).OrderBy(b => b.Id).FirstOrDefault();
        if (build != null && (builder || _w.Drones.Drones.Count(x => x.Errand == DroneErrand.Raise) == 0))
            return (DroneErrand.Raise, build.At, build.Id, $"{Here.Name} 땅에 {BuildName(build.Kind)} 짓기");
        var mark = Sites.FirstOrDefault(s => s.Kind == GroundKind.Landmark && !s.Surveyed && _w.Drones.Drones.All(x => x.Errand != DroneErrand.Survey || x.GuardFor != s.Id));
        if (mark != null && scout) return (DroneErrand.Survey, mark.At, mark.Id, $"{mark.Name} 살피기");
        var site = Sites.Where(s => s.Kind != GroundKind.Landmark && s.Left > 0).OrderByDescending(s => SiteNeed(s)).ThenBy(s => s.Id).FirstOrDefault();
        if (site != null && (d.Kind == DroneKind.Miner || !builder && !scout || _w.Drones.Drones.Count(x => x.Errand == DroneErrand.Dig) < 2))
            return (DroneErrand.Dig, site.At, site.Id, $"{site.Name}에서 {ItemKinds.Name(site.Item)} 캐기");
        if (mark != null) return (DroneErrand.Survey, mark.At, mark.Id, $"{mark.Name} 살피기");
        return null;
    }

    /// <summary>모자란 것부터 캔다 (불시착이면 추진제 · 판).</summary>
    private float SiteNeed(GroundSite s)
    {
        var w = _w;
        float need = 0.2f;
        if (s.Item == ItemKind.Ice) { need += w.Expedition.DaysLeft(MatCat.Water) < 6f ? 1f : 0.2f; if (w.Propulsion.Propellant < w.Propulsion.Capacity * 0.5f) need += Crashed ? 2f : 0.8f; }
        if (s.Item == ItemKind.MetalOre) need += w.Expedition.DaysLeft(MatCat.Repair) < 6f ? 1f : 0.3f + (Crashed ? 0.8f : 0f);
        if (s.Item == ItemKind.Rare) need += 0.6f;
        if (s.Item == ItemKind.Carbon) need += 0.25f;
        return need;
    }

    /// <summary>드론이 땅 일을 마쳤다.</summary>
    internal void DroneDone(Drone d, DroneErrand e, int target)
    {
        var w = _w;
        switch (e)
        {
            case DroneErrand.Dig when Sites.FirstOrDefault(s => s.Id == target) is GroundSite s && s.Left > 0:
            {
                int n = Math.Min(s.Left, (d.Kind == DroneKind.Miner ? 3 : 1 + _wx.Range(0, 2)) + (Built(BuildKind.Depot) != null ? 1 : 0));
                n = Math.Min(s.Left, (int)MathF.Round(n * w.Eras.TreeMul("mining"))); // v23 궤도 채굴 드론망
                int put = VoyageV15.Put(w, s.Item, n);
                s.Left -= n; s.Mined += put;
                Gathered[s.Item] = Gathered.GetValueOrDefault(s.Item) + put;
                MarkLog.Add(d.Marks, w.Tick, $"{Here?.Name} {s.Name}에서 {ItemKinds.Name(s.Item)} {put}");
                w.Log.Add(w.Tick, LogKind.Work, $"{d.Name}: {s.Name}에서 {ItemKinds.Name(s.Item)} {put}을 캐 왔다" + (s.Left <= 0 ? " — 다 캤다" : ""));
                break;
            }
            case DroneErrand.Raise when Builds.FirstOrDefault(b => b.Id == target) is GroundBuild b && b.Done < 0:
            {
                b.Work += DronesV21.ErrandHours(d) * (d.Kind is DroneKind.Build or DroneKind.Rigger ? 1.5f : 1f);
                if (b.Work >= BuildHours(b.Kind))
                {
                    b.Done = w.Tick;
                    Note($"{BuildName(b.Kind)} 완공");
                    w.History.Add(w, HistoryKind.Structure, $"{Here?.Name} 땅에 {BuildName(b.Kind)}을 세웠다 — {b.Why}", null, log: true);
                }
                break;
            }
            case DroneErrand.Survey when Sites.FirstOrDefault(s => s.Id == target) is GroundSite m && !m.Surveyed:
            {
                m.Surveyed = true;
                Surveys++;
                float roll = _wx.Float();
                if (roll < 0.35f) { int n = 1 + _wx.Range(0, 2); VoyageV15.Put(w, ItemKind.Rare, n); Gathered[ItemKind.Rare] = Gathered.GetValueOrDefault(ItemKind.Rare) + n; m.Found = $"희귀 소재 {n}"; }
                else if (roll < 0.65f && Here != null)
                {
                    var best = Here.Rich.OrderByDescending(kv => kv.Value).First().Key;
                    var item = best == ItemKind.Fuel ? ItemKind.Ice : best;
                    Sites.Add(new GroundSite { Id = _nextSite++, Kind = item == ItemKind.Ice ? GroundKind.Ice : item == ItemKind.Rare ? GroundKind.Crystal : GroundKind.Ore, Item = item, Name = $"{m.Name} 아래 {ItemKinds.Name(item)} 줄기", At = m.At, Left = 10 + _wx.Range(0, 10) });
                    m.Found = $"새 {ItemKinds.Name(item)} 줄기";
                }
                else { w.Research += 8f; m.Found = "땅의 기록 (연구)"; }
                w.Log.Add(w.Tick, LogKind.Work, $"{d.Name}: {m.Name}을 살폈다 — {m.Found}");
                w.Purpose?.Data(1, $"{m.Name}을 살폈다");
                if (Surveys == 1) w.History.Add(w, HistoryKind.Milestone, $"{Here?.Name}의 {m.Name}을 처음 살폈다 — {m.Found}", null, log: true);
                break;
            }
        }
    }

    /// <summary>땅에 있는 동안 한 시간마다: 기지 계획 · 정제 · 온실 · 원정 청하기.</summary>
    private void UpdateBase(float dt)
    {
        var w = _w;
        var p = Here!;
        if (w.Tick >= _nextPlan) { _nextPlan = w.Tick + SimTime.Hours(1); PlanBuilds(); }
        if (w.Tick >= _nextRefine) { _nextRefine = w.Tick + SimTime.Hours(1); Refine(); Greenhouse(); }
        // 원정대: 내린 지 여섯 시간이 지나면 한 번 청한다 (모자란 것 · 아니면 행성에 많은 것)
        if (_askedTrip < 0 && w.Tick - PhaseSince >= SimTime.Hours(6) && !Sheltering && w.Expedition.Current == null)
        {
            _askedTrip = w.Tick;
            var cats = new[] { MatCat.Water, MatCat.Repair }; // 행성 땅이 채우는 갈래
            var cat = cats.OrderBy(c => w.Expedition.DaysLeft(c)).First();
            if (w.Expedition.ComputerRequest(cat, approved: false)) w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {p.Name}의 먼 곳까지 원정대를 보내 보자 — {ExpeditionSites.CatName(cat)}");
        }
    }

    /// <summary>정제 (배 안 · 한 시간마다).</summary>
    private void Refine()
    {
        var w = _w;
        var water = w.Water;
        // 얼음 → 물 (탱크가 차지 않았으면)
        if (water.Level < water.Capacity * 0.9f && w.Ship.CountStored(ItemKind.Ice) > 0 && Life.Take(w, ItemKind.Ice, 1)) { water.Level = MathF.Min(water.Capacity, water.Level + 15f); Refined++; }
        // 얼음 → 추진제 (전기 분해)
        if (w.Propulsion.Capacity > 0f && w.Propulsion.Propellant < w.Propulsion.Capacity * 0.9f && w.Ship.CountStored(ItemKind.Ice) > 0 && w.Power.ReactorOnline && Life.Take(w, ItemKind.Ice, 1))
        { w.Propulsion.Propellant = MathF.Min(w.Propulsion.Capacity, w.Propulsion.Propellant + 8f); Refined++; Gathered[ItemKind.Fuel] = Gathered.GetValueOrDefault(ItemKind.Fuel) + 1; }
        bool fab = w.Ship.FurnitureOf(FurnitureType.Fabricator).Any(f => f.Machine is Machine m && m.Efficiency > 0.3f);
        if (!fab) return;
        // 금속 원료 3 → 금속판 1 · 탄소 2 → 필터 1
        if (w.Ship.CountStored(ItemKind.MetalOre) >= 3 && w.Ship.CountStored(ItemKind.Plate) < 30 && Life.Take(w, ItemKind.MetalOre, 3)) { VoyageV15.Put(w, ItemKind.Plate, 1); Refined++; }
        if (w.Ship.CountStored(ItemKind.Carbon) >= 2 && w.Ship.CountStored(ItemKind.Filter) < 8 && Life.Take(w, ItemKind.Carbon, 2)) { VoyageV15.Put(w, ItemKind.Filter, 1); Refined++; }
    }

    private long _greenAt = -1;
    private void Greenhouse()
    {
        var w = _w;
        if (Built(BuildKind.Greenhouse) is not GroundBuild g) return;
        if (_greenAt < 0) _greenAt = w.Tick;
        if (w.Tick - _greenAt < SimTime.Hours(6)) return;
        _greenAt = w.Tick;
        int n = (int)MathF.Round(2f * g.Health) + (Weather == PlanetWeather.Calm ? 1 : 0);
        int put = VoyageV15.Put(w, ItemKind.Produce, n);
        if (put > 0) { Gathered[ItemKind.Produce] = Gathered.GetValueOrDefault(ItemKind.Produce) + put; w.FoodSources.Note(FoodSrc.Garden, put); }
    }

    /// <summary>날씨 · 지진이 기지를 상하게 한다 (2단계가 부른다).</summary>
    private void HarmBuilds(float dt)
    {
        foreach (var b in Builds.Where(b => b.Done >= 0 && b.Health > 0f))
        {
            float hit = Weather switch
            {
                PlanetWeather.Typhoon => 0.12f * WxPower, PlanetWeather.Wind or PlanetWeather.Blizzard or PlanetWeather.DustStorm => 0.04f * WxPower,
                PlanetWeather.AcidRain => 0.03f * WxPower, PlanetWeather.Thunder when b.Kind == BuildKind.Tower => 0.02f * WxPower, _ => 0f,
            } * dt;
            if (b.Kind == BuildKind.Barrier) hit *= 0.5f; // 방벽은 버티라고 지은 것
            if (hit <= 0f) continue;
            float before = b.Health;
            b.Health = MathF.Max(0f, b.Health - hit);
            if (before > 0.15f && b.Health <= 0.15f) { Count($"무너진 {BuildName(b.Kind)}"); _w.Log.Add(_w.Tick, LogKind.Ship, $"{Here?.Name} — {BuildName(b.Kind)}이 {PlanetGen.WeatherName(Weather)}에 무너졌다"); }
        }
    }

    private void OnQuake(float g)
    {
        foreach (var b in Builds.Where(b => b.Done >= 0)) b.Health = MathF.Max(0f, b.Health - 0.25f * g);
    }

    /// <summary>떠날 때: 기지는 남기고 쓸 만한 자재 절반을 걷어 간다.</summary>
    private void PackUp(Planet p)
    {
        var w = _w;
        var standing = Builds.Where(b => b.Done >= 0).ToList();
        foreach (var b in standing)
            foreach (var (k, n) in BuildCost(b.Kind)) if (k != ItemKind.Seed) VoyageV15.Put(w, k, (int)(n * 0.5f * b.Health));
        if (standing.Count > 0) w.History.Add(w, HistoryKind.Structure, $"{p.Name}에 {string.Join(" · ", standing.Select(b => BuildName(b.Kind)))}을 남기고 떠났다", null, log: true);
        w.Expedition.DropPlanetSites();
    }

    private string BaseLine()
    {
        var bits = new List<string>();
        if (Gathered.Count > 0) bits.Add("캔 것 " + string.Join(" · ", Gathered.Where(kv => kv.Value > 0).Select(kv => $"{(kv.Key == ItemKind.Fuel ? "추진제(얼음)" : ItemKinds.Name(kv.Key))} {kv.Value}")));
        if (Builds.Any(b => b.Done >= 0)) bits.Add("지은 것 " + string.Join(" · ", Builds.Where(b => b.Done >= 0).Select(b => BuildName(b.Kind))));
        if (Surveys > 0) bits.Add($"살핀 곳 {Surveys}");
        return string.Join(" · ", bits);
    }
}
