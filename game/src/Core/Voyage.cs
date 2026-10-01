using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.8 항로 구간: 항해는 구간(순항·소행성대·성운·방사선대·난파선·기항지)을 차례로 지난다.
//   구간마다 사고의 무게가 달라진다 (소행성대 → 운석, 성운 → 전력 서지·센서 흐림, 방사선대 → 태양 폭풍).
//   소행성대는 채집이 좋고, 난파선에서는 건질 것이 있고, 기항지에서는 교역·새 사람·개수 공사를 한다.
//   엔진이 멎으면 표류한다 (나아가는 속도가 3분의 1). 목적지에 닿으면 항해를 정리하고 다음 항해를 잡는다.

public enum LegKind { Cruise, AsteroidBelt, Nebula, RadiationBelt, Derelict, Port }

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
    private int _incidents0, _deaths0, _techs0;
    private readonly Rng _rng;

    public Leg Current => Legs[Math.Clamp(Index, 0, Legs.Count - 1)];
    public float TotalDays => Legs.Sum(l => l.Days);
    public float DoneDays => Legs.Take(Index).Sum(l => l.Days) + Progress;
    public bool Drifting => !_w.Propulsion.Engines.Any(m => m.Efficiency > 0.1f) || _w.Propulsion.Propellant <= 0f;

    private static readonly string[] Places = { "케레스 정거장", "팔라스 조선소", "유로파 기지", "가니메데 항", "타이탄 정유소", "베스타 광산", "히기에아 교역소", "트리톤 관측소", "에리스 등대", "세드나 개척지" };
    private static readonly string[] Wrecks = { "버려진 화물선 '바람꽃'", "옛 탐사선 '새벽별'", "부서진 채굴선 '두더지'", "침묵한 정찰기 '까치'" };
    private static readonly string[] Belts = { "베스타 근처 소행성대", "트로이 소행성군", "카이퍼 띠 안쪽", "히다 소행성 무리" };
    private static readonly string[] Clouds = { "붉은 성운 가장자리", "이온 구름", "먼지 성운" };

    public VoyageSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 31337 + 7));
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
            if (kind == LegKind.Port && Legs.Any(l => l.Kind == LegKind.Port)) kind = LegKind.Cruise;
            string name = kind switch
            {
                LegKind.AsteroidBelt => Belts[_rng.Range(0, Belts.Length)],
                LegKind.Nebula => Clouds[_rng.Range(0, Clouds.Length)],
                LegKind.RadiationBelt => "방사선대",
                LegKind.Derelict => Wrecks[_rng.Range(0, Wrecks.Length)],
                LegKind.Port => Places[_rng.Range(0, Places.Length)],
                _ => "순항",
            };
            float days = kind switch { LegKind.Port => 1f, LegKind.Derelict => 0.5f, LegKind.Cruise => 2f + _rng.Range(0f, 3f), _ => 1.5f + _rng.Range(0f, 2f) };
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
        LegKind.Derelict => "난파선", _ => "기항지",
    };

    /// <summary>이 구간에서 사고 하나의 무게 배율.</summary>
    public float HazardMul(string key) => Current.Kind switch
    {
        LegKind.AsteroidBelt => key is "meteor" or "bigmeteor" or nameof(HazardKind.MicroShower) or nameof(HazardKind.MeteorShower) or nameof(HazardKind.DebrisCloud) or nameof(HazardKind.HullCrack) ? 2.5f : 1f,
        LegKind.Nebula => key is nameof(HazardKind.PowerSurge) or nameof(HazardKind.ComputerFault) or nameof(HazardKind.ComputerMisjudge) or nameof(HazardKind.LightsOut) ? 2.5f : 1f,
        LegKind.RadiationBelt => key is nameof(HazardKind.SolarStorm) ? 4f : key is nameof(HazardKind.RobotMalfunction) ? 1.8f : 1f,
        LegKind.Port => key is "meteor" or "bigmeteor" or nameof(HazardKind.MicroShower) ? 0.3f : key is nameof(HazardKind.Epidemic) or nameof(HazardKind.FoodPoisoning) ? 2f : 1f,
        _ => 1f,
    };

    /// <summary>채집 배율 (소행성대는 돌이 많다).</summary>
    public float MiningMul => Current.Kind == LegKind.AsteroidBelt ? 2f : 1f;

    /// <summary>센서 배율 (성운 속은 흐리다).</summary>
    public float SensorMul => Current.Kind == LegKind.Nebula ? 0.6f : 1f;

    /// <summary>시스템 틱: 나아간다 (엔진이 멎으면 표류).</summary>
    public void Update(float dt)
    {
        var w = _w;
        float speed = Drifting ? (w.Eras.Has("fusiondrive") ? 0.5f : 0.33f) : 1f;
        if (w.Eras.Has("warpbubble")) speed *= 2f; // v12.8 공간 왜곡
        if (Current.Kind == LegKind.Port) speed = 1f; // 정박 중
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
        switch (leg.Kind)
        {
            case LegKind.Port:
                PortsVisited++;
                w.RaiseAlert($"기항지 도착 — {leg.Name} · 교역과 보급, 새 사람", null, AlertLevel.Notice, shipWide: true);
                w.History.Add(w, HistoryKind.Decision, $"{leg.Name}에 닿았다", null, log: true);
                Trade(leg);
                break;
            case LegKind.Derelict:
                w.RaiseAlert($"{leg.Name} — 건질 것이 있는지 본다", null, AlertLevel.Notice, shipWide: true);
                Salvage(leg);
                break;
            case LegKind.AsteroidBelt:
                w.Space.SetMean(PropulsionSystem.ZoneDensity(w.Propulsion.Zone) * MiningMul); // 돌이 많다 — 채집이 좋다
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
        if (leg.Kind == LegKind.AsteroidBelt) _w.Space.SetMean(PropulsionSystem.ZoneDensity(_w.Propulsion.Zone));
        if (leg.Kind is not (LegKind.Cruise)) _w.Log.Add(_w.Tick, LogKind.Ship, $"{leg.Name}을(를) 벗어났다");
    }

    /// <summary>기항지: 남는 원료·희귀 소재를 팔고 모자란 부품·식량을 산다. 사람이 모자라면 새 사람이 탄다. 가장 낡은 설비 둘을 손본다.</summary>
    private void Trade(Leg leg)
    {
        var w = _w;
        var ship = w.Ship;
        var lines = new List<string>();
        // 팔기
        foreach (var (k, keep, price) in new[] { (ItemKind.Rare, 2, 6f), (ItemKind.MetalOre, 10, 0.8f), (ItemKind.Ice, 6, 0.5f) })
        {
            int have = ship.CountStored(k);
            if (have <= keep) continue;
            int sell = have - keep;
            if (!Life.Take(w, k, sell)) continue;
            Credits += sell * price;
            lines.Add($"{ItemKinds.Name(k)} {sell} 팔고");
        }
        // 사기: 모자란 것부터
        foreach (var (k, want, price) in new[] { (ItemKind.Plate, 10, 1.5f), (ItemKind.Electronics, 6, 3f), (ItemKind.Sealant, 4, 2f), (ItemKind.MedKit, 4, 3f), (ItemKind.Filter, 4, 2f), (ItemKind.Cable, 6, 1f) })
        {
            int have = ship.CountStored(k);
            int buy = Math.Max(0, want - have);
            buy = Math.Min(buy, (int)(Credits / price));
            if (buy <= 0) continue;
            int put = 0;
            foreach (var box in ship.Containers.Where(f => f.Storage!.Accepts(k)))
            {
                put += box.Storage!.Add(k, buy - put);
                if (put >= buy) break;
            }
            if (put <= 0) continue;
            Credits -= put * price;
            lines.Add($"{ItemKinds.Name(k)} {put}");
        }
        // 식량
        if (ship.CountStored(ItemKind.Meal) < w.Crew.Count(c => !c.Dead) * 6 && Credits >= 8f)
        {
            int meals = Math.Min(30, (int)(Credits / 0.3f));
            int put = 0;
            foreach (var f in ship.FurnitureOf(FurnitureType.Fridge).Where(f => f.Storage != null))
            {
                put += f.Storage!.Add(ItemKind.Meal, meals - put);
                if (put >= meals) break;
            }
            if (put > 0) { Credits -= put * 0.3f; lines.Add($"끼니 {put}"); }
        }
        Trades++;
        // 개수 공사: 가장 낡은 설비 둘
        foreach (var m in ship.Machines.Where(m => m.Faults.Count == 0).OrderByDescending(m => m.Wear).Take(2))
        {
            if (Credits < 6f || m.Wear < 0.25f) break;
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
            lines.Add($"새 승무원 {nc.Name}");
        }
        w.History.Add(w, HistoryKind.Decision, $"{leg.Name} 교역 — " + (lines.Count > 0 ? string.Join(" · ", lines) : "살 것도 팔 것도 없었다") + $" (남은 돈 {Credits:0})", null, log: true);
    }

    /// <summary>난파선: 드론·선외 작업조가 건질 것을 건진다 (금속판·전자 부품·희귀 소재, 가끔 구조 신호).</summary>
    private void Salvage(Leg leg)
    {
        var w = _w;
        var got = new List<string>();
        foreach (var (k, n) in new[] { (ItemKind.Plate, 2 + _rng.Range(0, 4)), (ItemKind.Electronics, _rng.Range(0, 3)), (ItemKind.Rare, _rng.Range(0, 2)), (ItemKind.Structure, _rng.Range(0, 3)) })
        {
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
        w.History.Add(w, HistoryKind.Decision, $"{leg.Name}에서 건졌다 — " + (got.Count > 0 ? string.Join(" · ", got) : "쓸 것이 없었다"), null, log: true);
        if (_rng.Chance(0.25f)) Hazards.Apply(w, HazardKind.RescueSignal, default, -1); // 난파선 근처의 구조 신호
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
