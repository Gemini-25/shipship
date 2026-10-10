using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.8 항로 구간: 항해는 구간(순항·소행성대·성운·방사선대·난파선·기항지)을 차례로 지난다.
//   구간마다 사고의 무게가 달라진다 (소행성대 → 운석, 성운 → 전력 서지·센서 흐림, 방사선대 → 태양 폭풍).
//   소행성대는 채집이 좋고, 난파선에서는 건질 것이 있고, 기항지에서는 교역·새 사람·개수 공사를 한다.
//   엔진이 멎으면 표류한다 (나아가는 속도가 3분의 1). 목적지에 닿으면 항해를 정리하고 다음 항해를 잡는다.
// v15.4 구간 20 · 지명 70 · 기항지 성격 10 (VoyageV15.cs) — 새 구간은 순항 자리에 섞여 든다.

public enum LegKind
{
    Cruise, AsteroidBelt, Nebula, RadiationBelt, Derelict, Port,
    // v15.4 새 구간 14 (VoyageV15.cs)
    SolarWind, CometTrail, GasGiant, IceRing, Pulsar, DeepVoid, TradeLane, MagneticField, Graveyard, PatrolLane, Perihelion, SporeCloud, Lagrange, Narrows,
}

public sealed class Leg
{
    public LegKind Kind { get; init; }
    public string Name { get; init; } = "";
    public float Days { get; init; }
}

public sealed record VoyageSummary(int Number, string From, string To, float Days, int Incidents, int Deaths, int Trades, int Recruits, int Salvage, int Techs, string Highlight);

public sealed class VoyageSystem
{
    private readonly World _w;
    public List<Leg> Legs { get; } = new();
    public int Index { get; private set; }
    /// <summary>지금 구간에서 나아간 날 수.</summary>
    public float Progress { get; private set; }
    public int Number { get; private set; } = 1;
    public string Origin { get; private set; } = "";
    public string Destination { get; private set; } = "";
    public long StartTick { get; private set; }
    public List<VoyageSummary> Past { get; } = new();
    public int Trades, Recruits, Salvaged, PortsVisited;
    public float Credits { get; set; } = 40f;
    /// <summary>아껴 둘 돈: 기항지에서 부품·개수 공사에는 이만큼을 남기고 쓴다 (끼니는 예외 — 캠페인 "돈 모으기" 임무).</summary>
    public float Reserve { get; set; }
    /// <summary>지금까지 기항지에 판 개수 (종류별 — 캠페인 임무가 센다).</summary>
    public Dictionary<ItemKind, int> Sold { get; } = new();
    /// <summary>v15.4 지금까지 기항지에서 팔아 번 돈.</summary>
    public float Earned { get; private set; }
    private int _incidents0, _deaths0, _techs0;
    private readonly Rng _rng;
    private readonly Rng _mix; // v15.4 새 구간 · 지명 (본 항로의 뽑기와 따로)

    public Leg Current => Legs[Math.Clamp(Index, 0, Legs.Count - 1)];
    public float TotalDays => Legs.Sum(l => l.Days);
    public float DoneDays => Legs.Take(Index).Sum(l => l.Days) + Progress;
    public bool Drifting => !_w.Propulsion.Engines.Any(m => m.Efficiency > 0.1f) || _w.Propulsion.Propellant <= 0f;

    private static readonly string[] Places = VoyageV15.PortNames, Wrecks = VoyageV15.Wrecks, Belts = VoyageV15.Belts, Clouds = VoyageV15.Clouds; // v15.4 지명 70

    public VoyageSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 31337 + 7));
        _mix = new Rng(unchecked(w.Seed * 7027 + 13));
        Plan(first: true);
    }

    /// <summary>다음 항해를 짠다: 처음 엿새는 순항(배를 길들이는 기간), 그 뒤로 구간 대여섯, 끝은 기항지.</summary>
    private void Plan(bool first)
    {
        Legs.Clear();
        Index = 0;
        Progress = 0f;
        Origin = first ? "지구 궤도 조선소" : Destination;
        Destination = Places[_rng.Range(0, Places.Length)];
        if (Destination == Origin) Destination = Places[(Array.IndexOf(Places, Destination) + 1) % Places.Length];
        Legs.Add(new Leg { Kind = LegKind.Cruise, Name = "순항", Days = first ? 6f : 2f + _rng.Range(0f, 2f) });
        int n = 4 + _rng.Range(0, 3);
        for (int i = 0; i < n; i++)
        {
            var kind = (_rng.Range(0, 10)) switch { 0 or 1 or 2 => LegKind.Cruise, 3 or 4 => LegKind.AsteroidBelt, 5 => LegKind.Nebula, 6 => LegKind.RadiationBelt, 7 => LegKind.Derelict, _ => LegKind.Port };
            // v13.4 방침(항로 성향): 안전은 소행성대·방사선대를 돌아가고, 탐사는 난파선·성운을 찾아간다
            int route = _w.Policies is PolicySystem pol ? pol["route"] : 0; // (배를 만들 때는 방침보다 항로가 먼저다)
            if (route == 1 && kind is LegKind.AsteroidBelt or LegKind.RadiationBelt && _rng.Chance(0.6f)) kind = LegKind.Cruise;
            if (route == 3 && kind == LegKind.Cruise && _rng.Chance(0.4f)) kind = _rng.Chance(0.5f) ? LegKind.Derelict : LegKind.Nebula;
            if (kind == LegKind.Port && Legs.Any(l => l.Kind == LegKind.Port)) kind = LegKind.Cruise;
            string name = kind switch
            {
                LegKind.AsteroidBelt => Belts[_rng.Range(0, Belts.Length)],
                LegKind.Nebula => Clouds[_rng.Range(0, Clouds.Length)],
                LegKind.RadiationBelt => VoyageV15.Rads[_mix.Range(0, VoyageV15.Rads.Length)],
                LegKind.Derelict => Wrecks[_rng.Range(0, Wrecks.Length)],
                LegKind.Port => Places[_rng.Range(0, Places.Length)],
                _ => "순항",
            };
            float days = kind switch { LegKind.Port => 1f, LegKind.Derelict => 0.5f, LegKind.Cruise => 2f + _rng.Range(0f, 3f), _ => 1.5f + _rng.Range(0f, 2f) };
            if (route == 2 && kind == LegKind.Cruise) days *= 0.65f; // 빠르게
            // v15.4 순항 자리 일부가 새 구간이 된다 (따로 굴린다 — 본 항로의 뽑기는 그대로)
            if (kind == LegKind.Cruise && VoyageV15.Swap(_mix, route) is LegSpec ns) { kind = ns.Kind; name = ns.Places[_mix.Range(0, ns.Places.Length)]; days = ns.DaysMin + _mix.Range(0f, ns.DaysMax - ns.DaysMin); }
            Legs.Add(new Leg { Kind = kind, Name = name, Days = days });
        }
        Legs.Add(new Leg { Kind = LegKind.Port, Name = Destination, Days = 1f });
        StartTick = _w.Tick;
        _incidents0 = _w.Causes.Incidents.Count;
        _deaths0 = _w.History.Deaths;
        _techs0 = _w.Eras.Known.Count;
    }

    public static string KindName(LegKind k) => k switch
    {
        LegKind.Cruise => "순항", LegKind.AsteroidBelt => "소행성대", LegKind.Nebula => "성운", LegKind.RadiationBelt => "방사선대",
        LegKind.Derelict => "난파선", LegKind.Port => "기항지", _ => VoyageV15.Spec(k)?.Name ?? "순항",
    };

    /// <summary>이 구간에서 사고 하나의 무게 배율.</summary>
    public float HazardMul(string key) => _w.Outside.RiskMul(key) * Current.Kind switch // v15.4 바깥 소식(예보 · 옛 등대)이 무게를 바꾼다
    {
        LegKind.AsteroidBelt => key is "meteor" or "bigmeteor" or nameof(HazardKind.MicroShower) or nameof(HazardKind.MeteorShower) or nameof(HazardKind.DebrisCloud) or nameof(HazardKind.HullCrack) ? 2.5f : 1f,
        LegKind.Nebula => key is nameof(HazardKind.PowerSurge) or nameof(HazardKind.ComputerFault) or nameof(HazardKind.ComputerMisjudge) or nameof(HazardKind.LightsOut) ? 2.5f : 1f,
        LegKind.RadiationBelt => key is nameof(HazardKind.SolarStorm) ? 4f : key is nameof(HazardKind.RobotMalfunction) ? 1.8f : 1f,
        LegKind.Port => key is "meteor" or "bigmeteor" or nameof(HazardKind.MicroShower) ? 0.3f : key is nameof(HazardKind.Epidemic) or nameof(HazardKind.FoodPoisoning) ? 2f : 1f,
        _ => VoyageV15.HazardMul(Current.Kind, key), // v15.4 새 구간
    };

    /// <summary>채집 배율 (소행성대는 돌이 많다 · v15.4 얼음 고리).</summary>
    public float MiningMul => Current.Kind == LegKind.AsteroidBelt ? 2f : VoyageV15.MiningMul(Current.Kind);

    /// <summary>센서 배율 (성운 속은 흐리다 · v15.4 깊은 공허는 맑다).</summary>
    public float SensorMul => Current.Kind == LegKind.Nebula ? 0.6f : VoyageV15.SensorMul(Current.Kind);

    /// <summary>시스템 틱: 나아간다 (엔진이 멎으면 표류).</summary>
    public void Update(float dt)
    {
        var w = _w;
        float speed = Drifting ? (w.Eras.Has("fusiondrive") ? 0.5f : 0.33f) : 1f;
        if (w.Eras.Has("warpbubble")) speed *= 2f; // v12.8 공간 왜곡
        speed *= VoyageV15.SpeedMul(Current.Kind); // v15.4 태양풍 물길
        if (Current.Kind == LegKind.Port) speed = 1f; // 정박 중
        if (w.Expedition.Halted) speed = 0f; // v16.12 재료가 바닥나 엔진을 껐다 — 일정이 밀린다
        Progress += dt / 24f * speed;
        if (Progress < Current.Days) return;
        Progress = 0f;
        Leave(Current);
        Index++;
        if (Index >= Legs.Count)
        {
            Finish();
            return;
        }
        Enter(Current);
    }

    private void Enter(Leg leg)
    {
        var w = _w;
        if (VoyageV15.Spec(leg.Kind) is LegSpec spec) { VoyageV15.Enter(w, this, leg, spec, _mix); return; } // v15.4 새 구간의 이점
        switch (leg.Kind)
        {
            case LegKind.Port:
                PortsVisited++;
                w.RaiseAlert($"기항지 도착 — {leg.Name}({VoyageV15.PortOf(leg.Name).Name}) · 교역과 보급, 새 사람", null, AlertLevel.Notice, shipWide: true);
                w.History.Add(w, HistoryKind.Decision, $"{leg.Name}에 닿았다", null, log: true);
                Trade(leg);
                w.Dock.OnPort(leg); // v18.5 거룻배 도킹 · 손님
                break;
            case LegKind.Derelict:
                w.RaiseAlert($"{leg.Name} — 건질 것이 있는지 본다", null, AlertLevel.Notice, shipWide: true);
                Salvage(leg);
                w.Dock.OnDerelict(leg); // v18.5 난파선에 붙어 들어가 본다
                break;
            case LegKind.AsteroidBelt:
                w.Space.SetMean(PropulsionSystem.ZoneDensity(w.Propulsion.Zone) * MiningMul); // 돌이 많다 — 채집이 좋다
                Vein(leg);
                goto default;
            default:
                w.Log.Add(w.Tick, LogKind.Ship, $"{KindName(leg.Kind)}에 들어섰다 — {leg.Name}" + leg.Kind switch
                {
                    LegKind.AsteroidBelt => " (운석이 잦다 · 채집이 좋다)", LegKind.Nebula => " (센서가 흐리고 전기가 튄다)",
                    LegKind.RadiationBelt => " (태양 폭풍이 잦다 — 대피소를 챙긴다)", _ => "",
                });
                break;
        }
    }

    private void Leave(Leg leg)
    {
        if (leg.Kind == LegKind.AsteroidBelt || VoyageV15.MiningMul(leg.Kind) != 1f) _w.Space.SetMean(PropulsionSystem.ZoneDensity(_w.Propulsion.Zone));
        if (leg.Kind is not (LegKind.Cruise)) _w.Log.Add(_w.Tick, LogKind.Ship, $"{Ko.EulReul(leg.Name)} 벗어났다");
    }

    /// <summary>기항지: 남는 원료·희귀 소재를 팔고 모자란 부품·식량을 산다. 사람이 모자라면 새 사람이 탄다. 가장 낡은 설비 둘을 손본다.</summary>
    private void Trade(Leg leg)
    {
        var w = _w;
        var ship = w.Ship;
        var lines = new List<string>();
        var port = VoyageV15.PortOf(leg.Name); // v15.4 기항지 성격 — 값 · 파는 물건 · 태우는 사람 · 들르면 생기는 일
        // 팔기
        foreach (var (k, keep, price) in new[] { (ItemKind.Rare, 2, 6f), (ItemKind.MetalOre, 10, 0.8f), (ItemKind.Ice, 6, 0.5f) })
        {
            int have = ship.CountStored(k);
            if (have <= keep) continue;
            int sell = have - keep;
            if (!Life.Take(w, k, sell)) continue;
            float got = sell * price * port.Sell(k) * (w.Policies["comms"] == 0 ? 1.15f : 1f); // v13.4 방침(교신: 정기 보고) — 기항지가 반긴다
            Credits += got;
            Earned += got;
            Sold[k] = Sold.GetValueOrDefault(k) + sell;
            lines.Add($"{ItemKinds.Name(k)} {sell} 팔고");
        }
        // 상선단과 함께 가면 실어 나른 짐삯을 받는다 (캠페인 4장 교역의 길)
        if (w.Campaign.ContractFee() is float fee and > 0f)
        {
            Credits += fee;
            lines.Add($"짐삯 {fee:0}");
        }
        // 사기: 모자란 것부터 (v13.2 방침(기항지 지출): 아끼면 덜 사고 돈을 남기고, 넉넉하면 더 산다)
        int spend = w.Policies["portspend"];
        float wantScale = spend == 0 ? 0.6f : spend == 2 ? 1.5f : 1f;
        float keepMoney = spend == 0 ? MathF.Max(Reserve, 20f) : spend == 2 ? 0f : Reserve;
        foreach (var (k, want0, price) in VoyageV15.Wares(port, new[] { (ItemKind.Plate, 10, 1.5f), (ItemKind.Electronics, 6, 3f), (ItemKind.Sealant, 4, 2f), (ItemKind.MedKit, 4, 3f), (ItemKind.Filter, 4, 2f), (ItemKind.Cable, 6, 1f),
            (ItemKind.Soap, 4, 0.5f), (ItemKind.Coffee, 6, 0.8f), (ItemKind.Bandage, 6, 0.8f), (ItemKind.Disinfectant, 2, 1f), (ItemKind.Spice, 2, 1f), (ItemKind.Gasket, 2, 1f) }))
        {
            int want = (int)MathF.Round(want0 * wantScale);
            int have = ship.CountStored(k);
            int buy = Math.Max(0, want - have);
            buy = Math.Min(buy, (int)(MathF.Max(0f, Credits - (have == 0 ? 0f : keepMoney)) / price)); // 하나도 없으면 아껴 둔 돈도 쓴다
            if (buy <= 0) continue;
            int put = 0;
            foreach (var box in ship.Containers.Where(f => f.Storage!.Accepts(k)))
            {
                put += box.Storage!.Add(k, buy - put);
                if (put >= buy) break;
            }
            if (put <= 0) continue;
            Credits -= put * price;
            w.Parts.Bought(k, put, leg.Name); // v14.6 기항지 묶음
            lines.Add($"{ItemKinds.Name(k)} {put}");
        }
        // 식량
        float mealPrice = 0.3f * port.Buy(ItemKind.Meal);
        if (ship.CountStored(ItemKind.Meal) < w.Crew.Count(c => !c.Dead) * 6 && Credits >= 8f)
        {
            int meals = Math.Min(30, (int)(Credits / mealPrice));
            int put = 0;
            foreach (var f in ship.FurnitureOf(FurnitureType.Fridge).Where(f => f.Storage != null))
            {
                put += f.Storage!.Add(ItemKind.Meal, meals - put);
                if (put >= meals) break;
            }
            if (put > 0) { Credits -= put * mealPrice; lines.Add($"끼니 {put}"); w.FoodSources.Note(FoodSrc.Trade, put); } // v16.22 교역
        }
        w.Cooking.OnPort(leg.Name, lines); // v16.8 고향 재료
        Trades++;
        // 개수 공사: 가장 낡은 설비 둘
        foreach (var m in ship.Machines.Where(m => m.Faults.Count == 0).OrderByDescending(m => m.Wear).Take(spend == 0 ? 0 : spend == 2 ? 4 : 2))
        {
            if (Credits - keepMoney < 6f || m.Wear < 0.25f) break;
            Credits -= 6f;
            m.Wear = 0.05f;
            m.Condition = MathF.Max(m.Condition, 0.95f);
            lines.Add($"{m.Name} 개수");
        }
        // 새 사람
        int alive = w.Crew.Count(c => !c.Dead);
        if (alive < w.StartCrew && alive < World.MaxCrew && _rng.Chance(0.7f))
        {
            var dock = ship.RoomsOf(RoomType.Airlock).FirstOrDefault()?.Cells.FirstOrDefault(ship.IsOpenFloor) ?? ship.Rooms.SelectMany(r => r.Cells).First(ship.IsOpenFloor);
            var nc = w.AddSurvivor(dock);
            // 기항지에서 탄 사람은 다치지 않았다
            nc.Vitals.Injury = 0f; nc.Vitals.Wounds.Clear(); nc.Vitals.Health = 1f; nc.Vitals.InjuryCause = null;
            nc.Needs.Food = 0.8f; nc.Needs.Rest = 0.8f; nc.Needs.Stress = 0.2f; nc.Memory.Trauma = 0.05f;
            nc.Joined = $"{leg.Name}에서 탔다";
            Recruits++;
            lines.Add($"새 승무원 {nc.Name}" + VoyageV15.Recruit(w, nc, port));
        }
        if (port.OnVisit(w, this) is string visit) lines.Add(visit);
        w.History.Add(w, HistoryKind.Decision, $"{leg.Name}({port.Name}) 교역 — " + (lines.Count > 0 ? string.Join(" · ", lines) : "살 것도 팔 것도 없었다") + $" (남은 돈 {Credits:0})", null, log: true);
    }

    /// <summary>v15.4 바깥 사건: 항로 안내 · 검문 · 우회로 이 구간에서 나아간 날을 더하거나 뺀다.</summary>
    public void Shift(float days) => Progress = MathF.Max(0f, Progress + days);

    /// <summary>시험용: 다음 항해를 미리 짜 본다.</summary>
    public void Replan() => Plan(first: false);

    /// <summary>시험용: 지금 구간을 이 종류로 바꾼다 (enter면 들어선 것처럼 이점 · 교역까지).</summary>
    public void Force(LegKind k, bool enter = false, string? name = null)
    {
        Leave(Current);
        var pool = VoyageV15.Pool(k);
        Legs[Math.Clamp(Index, 0, Legs.Count - 1)] = new Leg { Kind = k, Name = name ?? pool[_mix.Range(0, pool.Length)], Days = 1f };
        Progress = 0f;
        if (enter) Enter(Current);
    }

    /// <summary>난파선: 드론·선외 작업조가 건질 것을 건진다 (금속판·전자 부품·희귀 소재, 가끔 구조 신호).</summary>
    private void Salvage(Leg leg)
    {
        var w = _w;
        var got = new List<string>();
        // v13.4 방침(난파선: 적극 건진다) — 안까지 들어가 더 건지지만 다치기도 한다
        bool bold = w.Policies["wrecks"] == 1;
        foreach (var (k, n0) in new[] { (ItemKind.Plate, 2 + _rng.Range(0, 4)), (ItemKind.Electronics, _rng.Range(0, 3)), (ItemKind.Rare, _rng.Range(0, 2)), (ItemKind.Structure, _rng.Range(0, 3)) })
        {
            int n = bold ? (int)MathF.Ceiling(n0 * 1.6f) : n0;
            if (n <= 0) continue;
            int put = 0;
            foreach (var box in w.Ship.Containers.Where(f => f.Storage!.Accepts(k)))
            {
                put += box.Storage!.Add(k, n - put);
                if (put >= n) break;
            }
            if (put > 0) got.Add($"{ItemKinds.Name(k)} {put}");
        }
        Salvaged++;
        w.History.Add(w, HistoryKind.Decision, $"{leg.Name}에서 건졌다 — " + (got.Count > 0 ? string.Join(" · ", got) : "쓸 것이 없었다") + (bold ? " (안까지 들어갔다)" : ""), null, log: true);
        if (bold && _rng.Chance(0.3f) && w.Crew.Where(c => c.CanAct && !c.IsChild).OrderByDescending(c => c.Traits.Bravery).FirstOrDefault() is CrewMember diver)
        {
            NeedsSystem.AddInjury(diver.Vitals, 0.15f, "난파선 안에서 부상");
            w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(diver.Name)} 난파선 안에서 다쳤다 — 무너진 격벽에 걸렸다", null, new[] { diver }, log: true);
        }
        if (_rng.Chance(0.25f)) Hazards.Apply(w, HazardKind.RescueSignal, default, -1); // 난파선 근처의 구조 신호
    }

    /// <summary>소행성대의 광맥: 채집 팔이 닿는 바위에서 희귀 소재를 캔다 (채집 장치가 돌아야 한다). 희귀 소재는 거의 여기서만 모인다.</summary>
    private void Vein(Leg leg)
    {
        var w = _w;
        float arms = w.Ship.FurnitureOf(FurnitureType.Collector).Sum(f => f.Machine!.Efficiency);
        if (arms <= 0.05f) { w.Log.Add(w.Tick, LogKind.Ship, $"{leg.Name} — 광맥이 보이지만 채집 장치가 멈춰 있다"); return; }
        int n = 1 + _rng.Range(0, 3) + (arms >= 1.5f ? 1 : 0);
        int put = 0;
        foreach (var box in w.Ship.Containers.Where(f => f.Storage!.Accepts(ItemKind.Rare)))
        {
            put += box.Storage!.Add(ItemKind.Rare, n - put);
            if (put >= n) break;
        }
        if (put > 0) w.History.Add(w, HistoryKind.Decision, $"{leg.Name}에서 광맥을 찾았다 — 희귀 소재 {put}", null, log: true);
    }

    /// <summary>목적지: 항해를 정리하고 (연대기 요약) 다음 항해를 잡는다.</summary>
    private void Finish()
    {
        var w = _w;
        float days = (w.Tick - StartTick) / (float)SimTime.TicksPerDay;
        int inc = w.Causes.Incidents.Count - _incidents0, deaths = w.History.Deaths - _deaths0, techs = w.Eras.Known.Count - _techs0;
        var top = w.Causes.Incidents.Skip(_incidents0).OrderByDescending(i => i.Weight(w.Causes)).FirstOrDefault();
        var s = new VoyageSummary(Number, Origin, Destination, days, inc, deaths, Trades, Recruits, Salvaged, techs,
            top != null ? w.Causes.Node(top.Root).Text : "큰 사고 없이");
        Past.Add(s);
        w.History.Add(w, HistoryKind.Decision, $"{Number}번째 항해를 마쳤다 — {Origin} → {Destination} · {days:0.0}일 · 사고 {inc} · 사망 {deaths} · 새 기술 {techs} · 가장 큰 일: {s.Highlight}", null, log: true);
        w.RaiseAlert($"{Number}번째 항해 끝 — {Destination} · 다음 항해를 잡는다", null, AlertLevel.Notice, shipWide: true);
        Number++;
        Trades = Recruits = Salvaged = 0;
        Plan(first: false);
    }
}
