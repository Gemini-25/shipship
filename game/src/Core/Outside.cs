using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15.4 외부 사건 40: 배 바깥의 세상 — 조난 신호 · 지나가는 배 · 표류 화물 · 옛 등대 · 우주 기상 · 해적 · 만남.
// 사건마다 조건(구간 · 통신실)이 있고, 효과는 이미 있는 것을 쓴다 (사고 · 물자 · 돈 · 추진제 · 연구 · 마음 · 솜씨 · 항로 진행).
// 사고를 부르는 사건(사고 꺼짐이면 일어나지 않는다)은 이야기꾼과 같은 길(FireStory)로 건다.
// 하루 한두 번꼴. 위기 중이거나 기항지에 묶여 있을 때는 쉰다.

public sealed record OutsideSpec(string Id, string Name, string Group, float Weight, LegKind[]? Legs, bool Comms, bool Risky, Func<OutsideCtx, bool> Run);

/// <summary>바깥 사건 하나를 꾸릴 때 쓰는 손잡이.</summary>
public sealed class OutsideCtx
{
    public World W { get; }
    public Rng R { get; }
    public Leg Leg { get; }
    public string? Text { get; private set; }

    public OutsideCtx(World w, Rng r) { W = w; R = r; Leg = w.Voyage.Current; }

    public List<CrewMember> Awake() => W.Crew.Where(c => !c.Dead && c.CanAct && c.IsAwake && !c.IsChild).OrderBy(c => c.Id).ToList();
    public List<CrewMember> Alive() => W.Crew.Where(c => !c.Dead).OrderBy(c => c.Id).ToList();

    /// <summary>깨어 있는 사람 하나 (조건에 맞는).</summary>
    public CrewMember? One(Func<CrewMember, bool>? f = null)
    {
        var list = Awake().Where(c => f == null || f(c)).ToList();
        return list.Count == 0 ? null : list[R.Range(0, list.Count)];
    }

    /// <summary>그 솜씨가 가장 좋은 사람.</summary>
    public CrewMember? Best(Skill s) => Awake().OrderByDescending(c => c.RawSkill(s)).FirstOrDefault();

    public bool Has(CrewMember c, Habit h) => Life.Has(c, h);
    public bool Was(CrewMember c, params Background[] b) => b.Contains(c.Background);
    public void Stress(CrewMember c, float d) => c.Needs.Stress = Math.Clamp(c.Needs.Stress + d, 0f, 1f);
    public void Social(CrewMember c, float d) => c.Needs.Social = Math.Clamp(c.Needs.Social + d, 0f, 1f);
    public void Say(CrewMember c, string text) => c.Say(W, Persona.Say(c, text));
    public void Diary(CrewMember c, string text) => Life.Diary(W, c, Persona.Say(c, text));

    /// <summary>모두의 마음 (f에 맞는 사람만).</summary>
    public int StressAll(float d, Func<CrewMember, bool>? f = null)
    {
        int n = 0;
        foreach (var c in Alive().Where(c => f == null || f(c))) { Stress(c, d); n++; }
        return n;
    }

    public int Have(ItemKind k) => W.Ship.CountStored(k);
    public int Put(ItemKind k, int n) => VoyageV15.Put(W, k, n);
    public bool Take(ItemKind k, int n) => Life.Take(W, k, n);
    public float Arms => W.Ship.FurnitureOf(FurnitureType.Collector).Sum(f => f.Machine!.Efficiency);

    public float Credits { get => W.Voyage.Credits; set => W.Voyage.Credits = value; }

    /// <summary>남는 것을 판다 (판 개수 — 돈은 바로 들어온다).</summary>
    public int Sell(ItemKind k, int keep, float price)
    {
        int n = Have(k) - keep;
        if (n <= 0 || !Take(k, n)) return 0;
        Credits += n * price;
        return n;
    }

    /// <summary>모자라면 산다 (돈이 되는 만큼).</summary>
    public int Buy(ItemKind k, int want, float price)
    {
        int n = Math.Min(want - Have(k), (int)(Credits / price));
        if (n <= 0) return 0;
        int put = Put(k, n);
        Credits -= put * price;
        return put;
    }

    public float Propellant(float frac)
    {
        var p = W.Propulsion;
        float before = p.Propellant;
        p.Propellant = Math.Clamp(p.Propellant + p.Capacity * frac, 0f, p.Capacity);
        return p.Propellant - before;
    }

    /// <summary>이야기꾼과 같은 길로 사고를 건다.</summary>
    public string? Hazard(string key) => W.Hazards.FireStory(key, null);

    /// <summary>잠시 사고의 무게를 바꾼다 (예보 · 옛 등대의 지도).</summary>
    public void Shield(float mul, float hours, params string[] keys) => W.Outside.Shield(mul, hours, keys);

    /// <summary>배 기록에 남긴다.</summary>
    public bool Done(string text, params CrewMember[] who)
    {
        Text = text;
        W.History.Add(W, HistoryKind.Memory, text, null, who, log: true);
        return true;
    }

    /// <summary>모두가 알아야 할 일이면 방송까지.</summary>
    public bool Warn(string text)
    {
        Text = text;
        W.RaiseAlert(text, null, AlertLevel.Warning, shipWide: true);
        W.History.Add(W, HistoryKind.Incident, text, null, null, log: true);
        return true;
    }
}

public sealed class OutsideSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6007 + 271));
    private long _next = -1;
    private readonly List<(string key, float mul, long until)> _shield = new();
    public int Fired { get; private set; }
    public HashSet<string> Seen { get; } = new();
    public List<(long tick, string id, string text)> Recent { get; } = new();

    public OutsideSystem(World w) => _w = w;

    /// <summary>사고가 켜져 있나 (무작위 사고나 이야기꾼) — 꺼져 있으면 사고를 부르는 바깥 사건은 일어나지 않는다.</summary>
    public static bool HazardsOn => HazardSystem.RandomDays > 0f || Storyteller.Persona != StoryPersona.Off;

    public void Update(float dt)
    {
        var w = _w;
        if (_shield.Count > 0) _shield.RemoveAll(s => s.until <= w.Tick);
        if (_next < 0) _next = w.Tick + SimTime.Hours(6f + 10f * R.Float());
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Hours(10f + 10f * R.Float()); // 하루 한두 번꼴
        if (Crisis.Acting(w) || !w.Crew.Any(c => !c.Dead)) return;
        Fire(null);
    }

    public float RiskMul(string key)
    {
        float m = 1f;
        foreach (var s in _shield) if (s.key == key && s.until > _w.Tick) m *= s.mul;
        return m;
    }

    public void Shield(float mul, float hours, params string[] keys)
    {
        foreach (var k in keys) _shield.Add((k, mul, _w.Tick + SimTime.Hours(hours)));
    }

    /// <summary>지금 이 사건의 조건이 맞나 (구간 · 통신실 · 사고 켜짐).</summary>
    public bool Fits(OutsideSpec s)
    {
        var kind = _w.Voyage.Current.Kind;
        if (s.Legs == null ? kind == LegKind.Port : !s.Legs.Contains(kind)) return false;
        if (s.Comms && _w.Comms.Console == null) return false;
        if (s.Risky && !HazardsOn) return false;
        return true;
    }

    private float Weigh(OutsideSpec s)
    {
        long now = _w.Tick;
        bool recent = Recent.Any(r => r.id == s.Id && now - r.tick < SimTime.TicksPerDay * 3);
        return s.Weight * (recent ? 0.2f : 1f) * _w.Expedition.OutsideMul(s.Group); // v16.12 멈춘 배는 해적 눈에 띈다
    }

    /// <summary>사건 하나를 일으킨다 (id를 주면 그것만 — 조건은 그대로 따진다).</summary>
    public string? Fire(string? id)
    {
        var w = _w;
        var pool = Catalog.Where(s => (id == null || s.Id == id) && Fits(s)).ToList();
        for (int tries = 0; tries < 8 && pool.Count > 0; tries++)
        {
            float pick = R.Float() * pool.Sum(Weigh);
            var spec = pool[^1];
            foreach (var s in pool) { pick -= Weigh(s); if (pick <= 0f) { spec = s; break; } }
            var ctx = new OutsideCtx(w, R);
            if (!spec.Run(ctx)) { pool.Remove(spec); continue; }
            Fired++;
            Seen.Add(spec.Id);
            Recent.Add((w.Tick, spec.Id, ctx.Text ?? spec.Name));
            if (Recent.Count > 40) Recent.RemoveAt(0);
            return spec.Id;
        }
        return null;
    }

    // ═══════════════════════════════ 40 ═══════════════════════════════

    private const LegKind Cruise = LegKind.Cruise;
    private static readonly LegKind[] Busy = { Cruise, LegKind.TradeLane, LegKind.PatrolLane, LegKind.Lagrange }; // 배가 오가는 곳
    private static readonly LegKind[] Open = { Cruise, LegKind.TradeLane, LegKind.PatrolLane, LegKind.SolarWind, LegKind.DeepVoid, LegKind.Lagrange }; // 트인 항로
    private static readonly LegKind[] Rocky = { LegKind.AsteroidBelt, LegKind.IceRing, LegKind.CometTrail, LegKind.Graveyard, LegKind.Narrows }; // 돌 · 얼음 · 잔해
    private static readonly LegKind[] Lonely = { LegKind.DeepVoid, LegKind.Nebula, LegKind.Graveyard, LegKind.Derelict, LegKind.Pulsar, Cruise }; // 외진 곳
    private static readonly LegKind[] Lawless = { LegKind.TradeLane, LegKind.Graveyard, LegKind.DeepVoid, LegKind.Narrows, Cruise }; // 해적이 노리는 곳
    private static readonly LegKind[] Charged = { LegKind.GasGiant, LegKind.MagneticField, LegKind.Nebula, LegKind.RadiationBelt, LegKind.Pulsar }; // 전하 · 자기장
    private static LegKind[] L(params LegKind[] k) => k;

    public static readonly OutsideSpec[] Catalog =
    {
        // ── 신호 ──
        new("mayday", "조난 신호", "신호", 0.8f, null, true, true, x =>
        {
            if (x.W.Comms.SignalOpen || x.W.Comms.PodEta >= 0) return false;
            if (Hazards.Apply(x.W, HazardKind.RescueSignal, default, -1) == null) return false;
            return x.Done($"{x.Leg.Name}에서 조난 신호를 잡았다 — 탈출 캡슐 하나가 떠 있다");
        }),
        new("oldsos", "오래된 구조 신호", "신호", 0.7f, Lonely, true, false, x =>
        {
            // 수십 년째 되풀이되는 녹음 — 살아 있는 사람은 없지만 녹음에 이 근처 바위 지도가 들어 있다
            x.W.Voyage.Shift(0.2f);
            int n = x.StressAll(0.04f, c => x.Has(c, Habit.Worrier) || c.Fears.Contains(Fear.Death) || c.Fears.Contains(Fear.Isolation));
            return x.Done($"수십 년째 되풀이되는 구조 신호 — 녹음 속 바위 지도로 반나절 질러 갔다" + (n > 0 ? $" · {n}명은 한동안 말이 없었다" : ""));
        }),
        new("numbers", "숫자 방송", "신호", 0.6f, null, true, false, x =>
        {
            var c = x.One(c => x.Was(c, Background.Programmer, Background.SysAdmin, Background.Physicist, Background.Accountant) || c.RawSkill(Skill.Electrical) > 0.6f);
            if (c != null) { x.W.Research += 3f; x.Say(c, "규칙이 있어. 옛 측량 자료야"); return x.Done($"정체 모를 숫자 방송 — {Ko.IGa(c.Name)} 풀어 보니 옛 측량 자료였다 (연구 +3)", c); }
            int n = x.StressAll(0.05f, c => x.Has(c, Habit.Superstitious) || x.Has(c, Habit.Worrier));
            return x.Done("정체 모를 숫자 방송이 밤새 흘러나왔다" + (n > 0 ? $" — {n}명이 잠을 설쳤다" : ""));
        }),
        new("homecall", "고향 방송", "신호", 1f, Open, true, false, x =>
        {
            var all = x.Alive();
            foreach (var c in all) { x.Social(c, 0.06f); x.Stress(c, -0.03f); }
            return x.Done($"고향 방송 묶음이 닿았다 — 뉴스 · 경기 · 노래, {all.Count}명이 휴게실에 모였다");
        }),
        new("forecast", "우주 기상 예보", "신호", 0.9f, L(Cruise, LegKind.SolarWind, LegKind.Perihelion, LegKind.RadiationBelt, LegKind.GasGiant, LegKind.MagneticField), true, false, x =>
        {
            x.Shield(0.4f, 18f, nameof(HazardKind.SolarStorm), nameof(HazardKind.RadiationBurst), nameof(HazardKind.IonStorm));
            int n = x.StressAll(-0.04f, c => x.Has(c, Habit.Worrier) || c.Fears.Contains(Fear.Radiation));
            return x.Done("우주 기상 예보 — 폭풍이 온다는 쪽을 피해 차폐를 미리 챙겼다 (하루 가까이 폭풍이 덜 닥친다)" + (n > 0 ? $" · {n}명이 한숨 놓았다" : ""));
        }),

        // ── 지나가는 배 ──
        new("merchant", "지나가는 상선", "배", 1.2f, Busy, true, false, x =>
        {
            var deal = new List<string>();
            foreach (var (k, keep, price) in new[] { (ItemKind.MetalOre, 20, 0.6f), (ItemKind.Silicate, 10, 0.4f), (ItemKind.Carbon, 10, 0.4f) })
                if (x.Sell(k, keep, price) is int n and > 0) deal.Add($"{ItemKinds.Name(k)} {n} 팔고");
            foreach (var (k, want, price) in new[] { (ItemKind.Filter, 3, 2.5f), (ItemKind.Electronics, 3, 3.5f), (ItemKind.Sealant, 3, 2.5f) })
                if (x.Buy(k, want, price) is int n and > 0) { deal.Add($"{ItemKinds.Name(k)} {n} 사고"); break; }
            if (deal.Count == 0) { foreach (var c in x.Alive()) x.Social(c, 0.03f); return x.Done("지나가는 상선과 교신 — 살 것도 팔 것도 없어 소식만 나눴다"); }
            return x.Done($"지나가는 상선과 거래 — {string.Join(" · ", deal)} (돈 {x.Credits:0})");
        }),
        new("tanker", "연료 운반선", "배", 0.8f, L(Cruise, LegKind.TradeLane, LegKind.PatrolLane, LegKind.GasGiant, LegKind.Lagrange), true, false, x =>
        {
            var p = x.W.Propulsion;
            if (p.Propellant < p.Capacity * 0.85f && x.Credits >= 5f)
            {
                x.Credits -= 5f;
                float add = x.Propellant(0.25f);
                return x.Done($"연료 운반선에서 추진제를 샀다 — +{add:0}kg (돈 5)");
            }
            float water = MathF.Min(30f, MathF.Max(0f, x.W.Water.Capacity - x.W.Water.Level));
            x.W.Water.Level += water;
            return x.Done($"연료 운반선이 남는 물을 나눠 줬다 — 물 {water:0}L");
        }),
        new("hospital", "병원선", "배", 0.7f, Busy, true, false, x =>
        {
            var hurt = x.Alive().Where(c => c.Vitals.Injury > 0.05f).OrderByDescending(c => c.Vitals.Injury).Take(2).ToList();
            foreach (var c in hurt) c.Vitals.Injury = MathF.Max(0f, c.Vitals.Injury - 0.15f);
            if (hurt.Count > 0) return x.Done($"병원선이 곁에 붙어 {Ko.EulReul(string.Join("·", hurt.Select(c => c.Name)))} 봐 줬다", hurt.ToArray());
            int a = x.Put(ItemKind.MedKit, 1), b = x.Put(ItemKind.Bandage, 2);
            return x.Done($"병원선이 지나가며 구급품을 나눠 줬다 — 구급 키트 {a} · 붕대 {b}");
        }),
        new("pilgrims", "순례선 행렬", "배", 0.6f, null, false, false, x =>
        {
            var seen = x.Awake();
            if (seen.Count == 0) return false;
            foreach (var c in seen) x.Stress(c, x.Has(c, Habit.Superstitious) ? -0.07f : -0.04f);
            x.Diary(seen[0], "등불을 단 순례선 수십 척이 줄지어 지나갔다. 어디로 가는지는 모른다");
            return x.Done($"등불을 단 순례선 행렬이 지나갔다 — {Ko.IGa(string.Join("·", seen.Take(4).Select(c => c.Name)))} 창가에 섰다", seen.ToArray());
        }),
        new("colonists", "이주선", "배", 0.7f, Open, true, false, x =>
        {
            int crew = x.Alive().Count;
            if (x.Have(ItemKind.Meal) >= crew * 4 + 4 && x.Take(ItemKind.Meal, 4))
            {
                x.StressAll(-0.04f);
                x.Credits += 2f;
                return x.Done("먹을 것이 떨어진 이주선에 끼니 4를 나눴다 — 아이들이 손을 흔들었다 (사례 2)");
            }
            int s = x.Put(ItemKind.Seed, 3);
            return x.Done($"이주선이 고향 씨앗을 나눠 줬다 — 씨앗 {s}");
        }),
        new("science", "연구선", "배", 0.6f, L(LegKind.Pulsar, LegKind.Nebula, LegKind.DeepVoid, LegKind.GasGiant, LegKind.MagneticField, Cruise), true, false, x =>
        {
            x.W.Research += 4f;
            int rare = x.Have(ItemKind.Rare) > 2 && x.Take(ItemKind.Rare, 1) ? 1 : 0;
            if (rare > 0) x.Credits += 9f;
            return x.Done("연구선과 관측 자료를 바꿨다 — 연구 +4" + (rare > 0 ? " · 희귀 소재 하나를 9에 넘겼다" : ""));
        }),
        new("racers", "경주선 무리", "배", 0.5f, L(Cruise, LegKind.SolarWind, LegKind.TradeLane), false, false, x =>
        {
            var fans = x.Awake();
            if (fans.Count == 0) return false;
            foreach (var c in fans) x.Social(c, 0.05f);
            var pilot = x.Best(Skill.Piloting);
            pilot?.Practice(Skill.Piloting, 0.01f);
            var fan = fans.FirstOrDefault(c => x.Was(c, Background.DroneRacer, Background.CargoPilot));
            if (fan != null) x.Say(fan, "저 선회 봤어? 저게 진짜야");
            return x.Done("태양풍 경주선 무리가 곁을 스쳐 갔다 — 다들 창가에서 응원했다" + (pilot != null ? $" · {Ko.IGa(pilot.Name)} 선회를 눈여겨봤다" : ""), fans.ToArray());
        }),
        new("hauler", "광부 바지선", "배", 0.7f, L(LegKind.AsteroidBelt, LegKind.IceRing, LegKind.Narrows), true, false, x =>
        {
            if (x.Credits >= 2f && x.Put(ItemKind.MetalOre, 6) is int n and > 0) { x.Credits -= 2f; return x.Done($"광부 바지선에서 금속 원료 {n}을 2에 넘겨받았다"); }
            int ice = x.Put(ItemKind.Ice, 3);
            return x.Done($"광부 바지선이 깨다 남은 얼음을 나눠 줬다 — 얼음 {ice}");
        }),

        // ── 표류물 ──
        new("cargopod", "표류 화물", "표류물", 1f, null, false, false, x =>
        {
            if (x.Arms <= 0.05f && x.W.Comms.Airlock == null) return false;
            var got = new List<string>();
            foreach (var (k, n) in new[] { (ItemKind.Plate, 2), (ItemKind.Electronics, 1 + x.R.Range(0, 2)), (x.R.Pick(new[] { ItemKind.Coffee, ItemKind.Spice, ItemKind.Soap, ItemKind.TeaLeaf }), 2) })
                if (x.Put(k, n) is int p and > 0) got.Add($"{ItemKinds.Name(k)} {p}");
            return got.Count > 0 && x.Done($"떠도는 화물 컨테이너를 건졌다 — {string.Join(" · ", got)}");
        }),
        new("icechunk", "떠도는 얼음덩이", "표류물", 0.9f, L(Cruise, LegKind.DeepVoid, LegKind.AsteroidBelt, LegKind.IceRing, LegKind.CometTrail), false, false, x =>
        {
            if (x.Arms <= 0.05f) return false;
            int n = x.Put(ItemKind.Ice, 3 + x.R.Range(0, 4));
            return n > 0 && x.Done($"떠도는 얼음덩이를 채집 팔로 붙잡았다 — 얼음 {n}");
        }),
        new("probe", "길 잃은 탐사선", "표류물", 0.6f, Lonely, false, false, x =>
        {
            int a = x.Put(ItemKind.Sensor, 1), b = x.Put(ItemKind.Electronics, 2);
            x.W.Research += 2f;
            return x.Done($"길 잃은 무인 탐사선을 건졌다 — 센서 {a} · 전자재 {b} · 기록 장치에서 연구 +2");
        }),
        new("emptypod", "빈 탈출 캡슐", "표류물", 0.6f, L(LegKind.Graveyard, LegKind.Derelict, LegKind.DeepVoid, LegKind.Narrows, Cruise), false, false, x =>
        {
            int a = x.Put(ItemKind.Ration, 3), b = x.Put(ItemKind.MedKit, 1);
            int n = x.StressAll(0.04f, c => c.Fears.Contains(Fear.Death) || x.Has(c, Habit.Worrier));
            return x.Done($"빈 탈출 캡슐 — 탔던 사람은 없다 · 비상식량 {a} · 구급 키트 {b}" + (n > 0 ? $" · {n}명은 마음이 무거웠다" : ""));
        }),
        new("fueldrum", "떠도는 추진제 탱크", "표류물", 0.6f, L(Cruise, LegKind.TradeLane, LegKind.AsteroidBelt, LegKind.Graveyard, LegKind.Narrows), false, false, x =>
        {
            float add = x.Propellant(0.12f);
            if (add >= 1f) return x.Done($"떠도는 추진제 탱크를 붙잡아 옮겨 담았다 — +{add:0}kg");
            int f = x.Put(ItemKind.Fuel, 2);
            return f > 0 && x.Done($"떠도는 연료통을 건졌다 — 연료통 {f}");
        }),
        new("seedcrate", "떠도는 종자 상자", "표류물", 0.5f, L(LegKind.SporeCloud, LegKind.TradeLane, LegKind.Lagrange, Cruise), false, false, x =>
        {
            int a = x.Put(ItemKind.Seed, 4), b = x.Put(ItemKind.Nutrient, 2);
            if (a + b == 0) return false;
            if (x.One(c => x.Was(c, Background.Gardener, Background.FarmResearcher) || c.RawSkill(Skill.Botany) > 0.5f) is CrewMember g) x.Say(g, "아직 살아 있는 씨앗이야");
            return x.Done($"떠도는 종자 상자를 건졌다 — 씨앗 {a} · 양분 {b}");
        }),
        new("junk", "잔해 구름", "표류물", 0.8f, L(LegKind.Graveyard, LegKind.AsteroidBelt, LegKind.IceRing, LegKind.Narrows), false, true, x =>
        {
            string? hit = x.Hazard(nameof(HazardKind.DebrisAlert));
            int p = x.Put(ItemKind.Plate, 2);
            return x.Warn($"잔해 구름 속으로 들어섰다 — " + (hit ?? "가까스로 비켜 갔다") + (p > 0 ? $" · 금속판 {p}을 건졌다" : ""));
        }),

        // ── 옛 등대 · 표지 ──
        new("lighthouse", "옛 등대", "등대", 0.8f, L(Cruise, LegKind.DeepVoid, LegKind.AsteroidBelt, LegKind.IceRing, LegKind.Narrows, LegKind.Graveyard), true, false, x =>
        {
            x.W.Voyage.Shift(0.3f);
            x.Shield(0.5f, 12f, "meteor", "bigmeteor", nameof(HazardKind.MicroShower), nameof(HazardKind.MeteorShower), nameof(HazardKind.DebrisAlert));
            return x.Done("옛 등대가 아직 깜박인다 — 바위 지도를 받아 지름길로 들어섰다 (반나절 동안 돌이 덜 날아든다)");
        }),
        new("buoy", "고장 난 항로 부표", "등대", 0.7f, L(Cruise, LegKind.TradeLane, LegKind.PatrolLane, LegKind.Lagrange), false, false, x =>
        {
            var e = x.Best(Skill.Electrical);
            if (e != null && x.Take(ItemKind.Cable, 1))
            {
                e.Practice(Skill.Electrical, 0.01f);
                x.Credits += 6f;
                return x.Done($"{Ko.IGa(e.Name)} 꺼진 항로 부표를 고쳤다 — 항로청 사례금 6", e);
            }
            x.Credits += 2f;
            return x.Done("꺼진 항로 부표를 항로청에 알렸다 — 신고 사례 2");
        }),
        new("capsule", "옛 시대의 타임캡슐", "등대", 0.5f, null, false, false, x =>
        {
            x.W.Research += 3f;
            x.StressAll(-0.03f);
            var c = x.One(c => x.Was(c, Background.Teacher, Background.Writer, Background.Reporter) || x.Has(c, Habit.Bookworm));
            if (c != null) x.Diary(c, "백 년 전 사람들이 우리에게 편지를 남겼다. 우리도 하나 남겨야겠다");
            return x.Done("떠도는 타임캡슐을 열었다 — 옛 시대의 편지와 기록 (연구 +3)" + (c != null ? $" · {Ko.IGa(c.Name)} 오래 읽었다" : ""));
        }),
        new("memorial", "추모 부표", "등대", 0.5f, L(LegKind.Graveyard, LegKind.Derelict, LegKind.DeepVoid, Cruise), false, false, x =>
        {
            int eased = 0;
            foreach (var c in x.Alive())
            {
                x.Stress(c, 0.02f);
                if (c.Memory.Trauma > 0.1f) { c.Memory.Trauma = MathF.Max(0f, c.Memory.Trauma - 0.03f); eased++; }
            }
            return x.Done("이름이 빼곡한 추모 부표를 지났다 — 다 같이 잠시 멈췄다" + (eased > 0 ? $" · {eased}명은 마음의 짐을 조금 내려놓았다" : ""));
        }),
        new("misbeacon", "틀어진 항로 표지", "등대", 0.6f, L(LegKind.Narrows, LegKind.AsteroidBelt, LegKind.IceRing, LegKind.Nebula, Cruise), false, false, x =>
        {
            x.W.Voyage.Shift(-0.3f);
            float burn = -x.Propellant(-0.05f);
            var p = x.Best(Skill.Piloting);
            p?.Practice(Skill.Piloting, 0.015f);
            return x.Done($"항로 표지가 틀어져 있었다 — 돌아 나오느라 반나절 · 추진제 {burn:0}kg" + (p != null ? $" · {Ko.IGa(p.Name)} 손으로 길을 다시 잡았다" : ""));
        }),

        // ── 우주 기상 ──
        new("flare", "태양 플레어", "기상", 0.8f, L(Cruise, LegKind.SolarWind, LegKind.Perihelion, LegKind.RadiationBelt), false, true, x =>
            x.Warn("태양 플레어 — " + (x.Hazard(nameof(HazardKind.SolarStorm)) ?? "센서가 잠시 하얗게 질렸다"))),
        new("ionfront", "이온 전선", "기상", 0.8f, Charged, false, true, x =>
            x.Warn("이온 전선이 덮쳤다 — " + (x.Hazard(nameof(HazardKind.IonStorm)) ?? "무전에 잡음이 끓었다"))),
        new("aurora", "오로라 커튼", "기상", 0.7f, L(LegKind.MagneticField, LegKind.GasGiant, LegKind.Nebula, LegKind.RadiationBelt), false, false, x =>
        {
            var seen = x.Awake();
            if (seen.Count == 0) return false;
            foreach (var c in seen) { x.Stress(c, -0.05f); x.Social(c, 0.03f); }
            if (seen.FirstOrDefault(c => x.Has(c, Habit.Gazer) || x.Was(c, Background.Astronomer, Background.Artist)) is CrewMember g) x.Diary(g, "창밖에 초록 커튼이 걸렸다. 이건 그려 둬야 한다");
            return x.Done($"배를 감싼 오로라 커튼 — {seen.Count}명이 불을 끄고 봤다", seen.ToArray());
        }),
        new("cometdust", "혜성 부스러기", "기상", 0.8f, L(LegKind.CometTrail, LegKind.IceRing), false, true, x =>
        {
            string? hit = x.Hazard(nameof(HazardKind.MicroShower));
            int ice = x.Arms > 0.05f ? x.Put(ItemKind.Ice, 2) : 0;
            return x.Warn("혜성 부스러기가 쏟아진다 — " + (hit ?? "외판을 긁고 지나갔다") + (ice > 0 ? $" · 얼음 {ice}덩이를 건졌다" : ""));
        }),
        new("tidal", "조석력 흔들림", "기상", 0.6f, L(LegKind.GasGiant, LegKind.Narrows, LegKind.Pulsar), false, true, x =>
            x.Warn("조석력에 배가 비틀렸다 — " + (x.Hazard(nameof(HazardKind.FrameCreak)) ?? "골조가 한참 삐걱댔다"))),
        new("static", "정전기 폭풍", "기상", 0.7f, L(LegKind.SolarWind, LegKind.Nebula, LegKind.SporeCloud, LegKind.MagneticField), false, true, x =>
            x.Warn("정전기 폭풍 — " + (x.Hazard(nameof(HazardKind.StaticDischarge)) ?? "손잡이마다 불꽃이 튀었다"))),
        new("coldsnap", "깊은 냉기", "기상", 0.6f, L(LegKind.DeepVoid, LegKind.IceRing, LegKind.CometTrail), false, true, x =>
            x.Warn("깊은 냉기가 외벽을 파고든다 — " + (x.Hazard(nameof(HazardKind.PipeFreeze)) ?? "창에 성에가 꼈다"))),
        new("heatwave", "열파", "기상", 0.6f, L(LegKind.Perihelion, LegKind.SolarWind), false, true, x =>
            x.Warn("열파가 덮쳤다 — " + (x.Hazard(nameof(HazardKind.Overheat)) ?? "냉각기가 한참 울었다"))),
        new("stillness", "고요한 우주", "기상", 0.7f, L(LegKind.DeepVoid, LegKind.PatrolLane, LegKind.Lagrange, Cruise), false, false, x =>
        {
            var all = x.Alive();
            foreach (var c in all) { c.Needs.Rest = MathF.Min(1f, c.Needs.Rest + 0.05f); x.Stress(c, -0.03f); }
            return x.Done($"아무 일도 없는 고요한 하루 — 엔진 소리마저 잔잔했다 ({all.Count}명이 푹 쉬었다)");
        }),

        // ── 해적 ──
        new("piratehail", "해적 신호", "해적", 0.6f, Lawless, true, false, x =>
        {
            if (x.Credits >= 8f)
            {
                float toll = MathF.Max(5f, x.Credits * 0.3f);
                x.Credits -= toll;
                x.StressAll(0.04f);
                return x.Warn($"해적 신호 — 통행료를 요구했다 · 돈 {toll:0}을 주고 지나갔다");
            }
            if (OutsideSystem.HazardsOn && x.Hazard("meteor") is string shot)
            {
                x.StressAll(0.08f);
                return x.Warn($"해적 신호 — 낼 돈이 없자 경고 사격을 했다 · {shot}");
            }
            int p = Math.Min(3, x.Have(ItemKind.Plate));
            if (p > 0) x.Take(ItemKind.Plate, p);
            x.StressAll(0.06f);
            return x.Warn("해적 신호 — 낼 돈이 없어 짐을 털렸다" + (p > 0 ? $" · 금속판 {p}" : " · 가져갈 것도 없었다"));
        }),
        new("decoy", "가짜 조난 신호", "해적", 0.6f, Lawless, true, false, x =>
        {
            var sharp = x.One(c => x.Was(c, Background.SysAdmin, Background.Programmer, Background.Police, Background.Soldier) || c.RawSkill(Skill.Electrical) > 0.65f);
            if (sharp != null)
            {
                foreach (var o in x.Alive().Where(o => o != sharp)) o.ChangeAffinity(sharp, 0.02f);
                x.Say(sharp, "이건 녹음이야. 같은 숨소리가 되풀이돼");
                return x.Done($"조난 신호가 이상했다 — {Ko.IGa(sharp.Name)} 해적의 미끼라는 걸 알아챘다", sharp);
            }
            x.W.Voyage.Shift(-0.35f);
            x.StressAll(0.03f);
            return x.Done("조난 신호를 따라갔더니 빈 부표였다 — 해적의 미끼 · 반나절을 버렸다");
        }),
        new("raided", "털린 배", "해적", 0.5f, L(LegKind.Graveyard, LegKind.TradeLane, LegKind.DeepVoid, Cruise), false, false, x =>
        {
            int a = x.Put(ItemKind.Fuse, 2), b = x.Put(ItemKind.Cable, 2);
            int n = x.StressAll(0.05f);
            return x.Done($"해적에게 털린 배를 지났다 — 남은 퓨즈 {a} · 케이블 {b}를 거뒀다 · {n}명이 문단속을 다시 했다");
        }),
        new("shadow", "해적선 그림자", "해적", 0.6f, Lawless, false, false, x =>
        {
            if (x.W.Sensors.Quality > 0.2f && !x.W.Voyage.Drifting && x.W.Propulsion.Propellant > x.W.Propulsion.Capacity * 0.1f)
            {
                float burn = -x.Propellant(-0.08f);
                x.StressAll(0.03f);
                return x.Warn($"센서에 해적선 그림자 — 일찍 보고 연소해 따돌렸다 (추진제 {burn:0}kg)");
            }
            x.StressAll(0.08f);
            return x.Warn("해적선이 바짝 붙어 한참 따라왔다 — 늦게 봤다 · 다들 숨을 죽였다");
        }),

        // ── 만남 ──
        new("patrol", "순찰선", "만남", 0.7f, L(LegKind.PatrolLane, LegKind.TradeLane, Cruise), true, false, x =>
        {
            if (x.R.Chance(0.7f))
            {
                int a = x.Put(ItemKind.Sealant, 2), b = x.Put(ItemKind.Extinguisher, 1);
                return x.Done($"순찰선이 다가와 안부를 물었다 — 보급을 나눠 줬다 · 실링폼 {a} · 소화기 {b}");
            }
            x.W.Voyage.Shift(-0.25f);
            return x.Done("순찰선 검문 — 서류를 맞추느라 반나절 묶였다");
        }),
        new("whalesong", "고래 노래", "만남", 0.5f, L(LegKind.DeepVoid, LegKind.Nebula, LegKind.Pulsar, LegKind.GasGiant), true, false, x =>
        {
            x.W.Research += 2f;
            int calm = x.StressAll(-0.06f, c => x.Was(c, Background.Musician, Background.Writer, Background.Artist) || x.Has(c, Habit.Gazer));
            int scared = x.StressAll(0.05f, c => x.Has(c, Habit.Superstitious));
            return x.Done("통신기에 고래 노래 같은 낮은 전파가 걸렸다 — 녹음해 두었다 (연구 +2)" + (calm > 0 ? $" · {calm}명은 밤새 들었다" : "") + (scared > 0 ? $" · {scared}명은 무섭다고 했다" : ""));
        }),
    };
}
