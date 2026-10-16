using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15.4 항해 확장: 구간 6 → 20 · 지명 70 · 기항지 성격 10.
//   새 구간 14는 순항 자리 일부에 섞여 든다 (따로 굴린다 — 본 항로의 뽑기는 그대로).
//   구간마다 사고의 무게(HazardMul)가 다르고, 이점 하나씩: 빨리 간다 · 얼음 · 추진제 · 채집 · 연구 · 센서 · 돈 · 배터리 · 부품 · 안심 · 산소 · 양분 · 정비 · 조타 솜씨.
//   기항지는 성격마다 값(가격 배율)이 다르고, 파는 물건 · 태우는 사람의 경력 · 들르면 생기는 일이 다르다.

/// <summary>새 구간의 이점 (구간 하나에 하나).</summary>
public enum LegPerk { Speed, Ice, Propellant, Mining, Research, Sensor, Credits, Battery, Salvage, Calm, Air, Nutrient, Repair, Training }

public sealed record LegSpec(LegKind Kind, string Name, string Hex, float Weight, float DaysMin, float DaysMax, bool Danger, bool Explore,
    (string key, float mul)[] Risks, LegPerk Perk, string Note, string[] Places);

/// <summary>기항지 성격: 값 · 파는 물건 · 태우는 사람 · 들르면 생기는 일.</summary>
public sealed record PortTrait(string Id, string Name, float BuyMul, float SellMul, (ItemKind k, float mul)[] Values,
    (ItemKind k, int want, float price)[] Wares, Background Recruit, Func<World, VoyageSystem, string?> OnVisit)
{
    /// <summary>이 기항지에서 그 물건의 값 (원래 값에 곱한다).</summary>
    public float Value(ItemKind k)
    {
        foreach (var (kk, m) in Values) if (kk == k) return m;
        return 1f;
    }
    public float Buy(ItemKind k) => BuyMul * Value(k);
    public float Sell(ItemKind k) => SellMul * Value(k);
}

public static class VoyageV15
{
    private const string Meteor = "meteor", BigMeteor = "bigmeteor", Fire = "fire", Break = "break";

    // ═══════════════════════════════ 구간 14 ═══════════════════════════════

    public static readonly LegSpec[] Legs =
    {
        new(LegKind.SolarWind, "태양풍 물길", "#ffb86b", 1.1f, 1.5f, 3f, false, false,
            new[] { (nameof(HazardKind.StaticDischarge), 2.5f), (nameof(HazardKind.PowerSurge), 1.6f), (nameof(HazardKind.SolarStorm), 1.5f) },
            LegPerk.Speed, "태양풍이 등을 민다 — 빨리 간다 · 정전기가 튄다",
            new[] { "헬리오스 물길", "태양풍 동쪽 갈래", "코로나 바람길" }),
        new(LegKind.CometTrail, "혜성 꼬리", "#9be7ff", 1f, 1f, 2f, true, true,
            new[] { (nameof(HazardKind.CometTail), 3f), (nameof(HazardKind.MicroShower), 2f), (Meteor, 1.5f) },
            LegPerk.Ice, "얼음 부스러기가 많다 — 채집 팔로 얼음을 건진다 · 작은 돌이 잦다",
            new[] { "핼리 혜성 꼬리", "백조 혜성 먼지길", "C/2061 혜성 꼬리" }),
        new(LegKind.GasGiant, "가스 거성 궤도", "#e0a96d", 1f, 1.5f, 3f, false, true,
            new[] { (nameof(HazardKind.IonStorm), 2.5f), (nameof(HazardKind.RadiationBurst), 1.5f), (nameof(HazardKind.StaticDischarge), 1.5f) },
            LegPerk.Propellant, "대기를 스치며 가스를 퍼 담는다 — 추진제 · 이온 폭풍이 잦다",
            new[] { "목성 궤도", "토성 궤도", "해왕성 궤도" }),
        new(LegKind.IceRing, "얼음 고리", "#cfe8ff", 1f, 1f, 2.5f, true, false,
            new[] { (Meteor, 2f), (nameof(HazardKind.MicroShower), 2.5f), (nameof(HazardKind.HullCrack), 1.4f), (nameof(HazardKind.WindowCrack), 1.5f) },
            LegPerk.Mining, "얼음과 돌이 빽빽하다 — 채집이 좋다 · 운석이 잦다",
            new[] { "토성 B 고리", "천왕성 엡실론 고리", "해왕성 아담스 고리" }),
        new(LegKind.Pulsar, "펄서 곁", "#7fd1ff", 0.7f, 1f, 2f, true, true,
            new[] { (nameof(HazardKind.RadiationBurst), 3f), (nameof(HazardKind.ComputerFault), 2f), (nameof(HazardKind.ComputerMisjudge), 1.5f) },
            LegPerk.Research, "규칙적인 전파 — 관측하면 연구가 쌓인다 · 방사선 · 컴퓨터가 흔들린다",
            new[] { "PSR 0437 곁", "까마귀 펄서 곁" }),
        new(LegKind.DeepVoid, "깊은 공허", "#2b3550", 1f, 2f, 4f, false, true,
            new[] { (Meteor, 0.3f), (nameof(HazardKind.MicroShower), 0.3f), (nameof(HazardKind.PanicAttack), 2.5f), (nameof(HazardKind.PipeFreeze), 2f) },
            LegPerk.Sensor, "잡음이 없다 — 센서가 맑다 · 운석은 드물지만 외로움과 냉기",
            new[] { "빈 하늘 공동", "고요의 바다 너머", "별 없는 골짜기" }),
        new(LegKind.TradeLane, "상선 항로", "#8fe388", 1.2f, 1.5f, 3f, false, false,
            new[] { (Meteor, 0.6f), (nameof(HazardKind.DebrisAlert), 2f), (nameof(HazardKind.Epidemic), 1.4f) },
            LegPerk.Credits, "상선이 잦다 — 남는 원료를 판다 · 잔해 경보 · 병이 옮는다",
            new[] { "목성–토성 상선 항로", "소행성대 정기 항로", "외행성 화물 항로" }),
        new(LegKind.MagneticField, "자기장 띠", "#c58cff", 0.8f, 1f, 2f, true, false,
            new[] { (nameof(HazardKind.PowerSurge), 2.5f), (nameof(HazardKind.ArcFault), 2f), (nameof(HazardKind.BreakerCascade), 2f), (nameof(HazardKind.ComputerMisjudge), 1.5f) },
            LegPerk.Battery, "자기장 유도 — 배터리가 찬다 · 전력 서지 · 아크",
            new[] { "목성 자기권", "가니메데 자기 꼬리" }),
        new(LegKind.Graveyard, "폐선 무덤", "#a0a4b3", 0.8f, 0.5f, 1f, true, true,
            new[] { (nameof(HazardKind.DebrisAlert), 2.5f), (nameof(HazardKind.DebrisCloud), 2f), (Meteor, 1.5f), (nameof(HazardKind.HullCrack), 1.5f) },
            LegPerk.Salvage, "부서진 배들 — 부품을 건진다 · 잔해 · 선체 균열",
            new[] { "옛 함대의 무덤", "트로이 폐선장", "침묵한 선단" }),
        new(LegKind.PatrolLane, "순찰 항로", "#5fb3ff", 1f, 1.5f, 3f, false, false,
            new[] { (Meteor, 0.4f), (BigMeteor, 0.4f), (nameof(HazardKind.MeteorShower), 0.4f), (nameof(HazardKind.DebrisCloud), 0.3f) },
            LegPerk.Calm, "순찰대가 지킨다 — 운석·잔해가 치워져 있다 · 마음이 놓인다",
            new[] { "연합 순찰 구역", "등대 감시 항로" }),
        new(LegKind.Perihelion, "근일점", "#ff8a5c", 0.7f, 1f, 2f, true, false,
            new[] { (nameof(HazardKind.Overheat), 2.5f), (Fire, 1.5f), (nameof(HazardKind.ThermalStress), 2f), (nameof(HazardKind.CoolantLoss), 2f), (nameof(HazardKind.SolarStorm), 1.5f) },
            LegPerk.Air, "햇빛이 세다 — 조류 판이 산소를 낸다 · 과열 · 불",
            new[] { "수성 안쪽 근일점", "금성 그늘 바깥" }),
        new(LegKind.SporeCloud, "유기물 구름", "#9ad36a", 0.7f, 1f, 2f, false, true,
            new[] { (nameof(HazardKind.MoldOutbreak), 2.5f), (nameof(HazardKind.SkinFungus), 2f), (nameof(HazardKind.CropBlight), 1.8f), (nameof(HazardKind.Epidemic), 1.3f) },
            LegPerk.Nutrient, "유기물을 걸러 양분 · 씨앗 · 곰팡이와 병충해",
            new[] { "녹색 유기물 구름", "톨린 안개" }),
        new(LegKind.Lagrange, "라그랑주 정박점", "#7de0c4", 0.9f, 0.5f, 1f, false, false,
            new[] { (Meteor, 0.5f), (Break, 0.7f), (nameof(HazardKind.DebrisAlert), 1.5f) },
            LegPerk.Repair, "고요한 정박점 — 멈춰 서서 설비를 손본다 · 버려진 잔해",
            new[] { "목성 L4 정박점", "지구–달 L5 정박점", "토성 L2 정박점" }),
        new(LegKind.Narrows, "좁은 협로", "#d9b36b", 0.9f, 0.5f, 1.5f, true, false,
            new[] { (Meteor, 1.8f), (BigMeteor, 1.5f), (nameof(HazardKind.HullCrack), 1.5f), (nameof(HazardKind.FrameCreak), 1.5f), (nameof(HazardKind.WorkAccident), 1.3f) },
            LegPerk.Training, "바위틈을 빠져나간다 — 조타 솜씨가 는다 · 운석 · 선체 피로",
            new[] { "바위틈 협로", "히다 협로" }),
    };

    // ═══════════════════════════════ 지명 (원래 구간) ═══════════════════════════════

    public static readonly string[] Wrecks = { "버려진 화물선 '바람꽃'", "옛 탐사선 '새벽별'", "부서진 채굴선 '두더지'", "침묵한 정찰기 '까치'", "녹슨 예인선 '황소'", "실종된 연구선 '해오름'" };
    public static readonly string[] Belts = { "베스타 근처 소행성대", "트로이 소행성군", "카이퍼 띠 안쪽", "히다 소행성 무리", "테미스 소행성족" };
    public static readonly string[] Clouds = { "붉은 성운 가장자리", "이온 구름", "먼지 성운", "푸른 반사 성운" };
    public static readonly string[] Rads = { "목성 방사선대", "토성 방사선대", "이오 플라스마 고리" };

    /// <summary>기항지 16곳과 그 성격.</summary>
    public static readonly (string place, string trait)[] Ports =
    {
        ("케레스 정거장", "trade"), ("팔라스 조선소", "shipyard"), ("유로파 기지", "science"), ("가니메데 항", "military"),
        ("타이탄 정유소", "refinery"), ("베스타 광산", "mine"), ("히기에아 교역소", "trade"), ("트리톤 관측소", "science"),
        ("에리스 등대", "haven"), ("세드나 개척지", "frontier"), ("칼리스토 병원선", "medical"), ("이오 제련소", "mine"),
        ("엔켈라두스 온실", "farm"), ("미란다 수도원", "haven"), ("포보스 보급기지", "military"), ("마케마케 조선소", "shipyard"),
    };

    public static readonly string[] PortNames = Ports.Select(p => p.place).ToArray();

    /// <summary>지명 전부 (구간 종류별 풀 — 순항은 이름 없음).</summary>
    public static int PlaceCount => Ports.Length + Wrecks.Length + Belts.Length + Clouds.Length + Rads.Length + Legs.Sum(l => l.Places.Length);

    public static string[] Pool(LegKind k) => k switch
    {
        LegKind.Port => PortNames, LegKind.Derelict => Wrecks, LegKind.AsteroidBelt => Belts, LegKind.Nebula => Clouds, LegKind.RadiationBelt => Rads,
        LegKind.Cruise => new[] { "순항" },
        _ => Spec(k)!.Places,
    };

    public static LegSpec? Spec(LegKind k) => k >= LegKind.SolarWind && (int)k - (int)LegKind.SolarWind < Legs.Length ? Legs[(int)k - (int)LegKind.SolarWind] : null;

    public static float HazardMul(LegKind k, string key)
    {
        if (Spec(k) is not LegSpec s) return 1f;
        foreach (var (rk, m) in s.Risks) if (rk == key) return m;
        return 1f;
    }

    public static float SpeedMul(LegKind k) => Spec(k)?.Perk == LegPerk.Speed ? 1.5f : 1f;
    public static float MiningMul(LegKind k) => Spec(k)?.Perk == LegPerk.Mining ? 1.8f : 1f;
    public static float SensorMul(LegKind k) => Spec(k)?.Perk == LegPerk.Sensor ? 1.35f : 1f;

    /// <summary>순항 자리 하나를 새 구간으로 바꿀지 (방침: 안전은 위험한 곳을 덜 · 빠르게는 바람길을 더 · 탐사는 낯선 곳을 더).</summary>
    public static LegSpec? Swap(Rng r, int route)
    {
        float p = route == 2 ? 0.7f : route == 3 ? 0.65f : 0.5f;
        if (!r.Chance(p)) return null;
        float W(LegSpec s) => s.Weight * (route == 1 && s.Danger ? 0.3f : 1f) * (route == 3 && s.Explore ? 2f : 1f) * (route == 2 && s.Perk == LegPerk.Speed ? 2.5f : 1f);
        float pick = r.Float() * Legs.Sum(W);
        foreach (var s in Legs) { pick -= W(s); if (pick <= 0f) return s; }
        return Legs[^1];
    }

    /// <summary>창고에 넣는다 (넣은 개수).</summary>
    public static int Put(World w, ItemKind k, int n)
    {
        int put = 0;
        if (n <= 0) return 0;
        foreach (var box in w.Ship.Containers.Where(f => f.Storage!.Accepts(k)))
        {
            put += box.Storage!.Add(k, n - put);
            if (put >= n) break;
        }
        return put;
    }

    private static float Arms(World w) => w.Ship.FurnitureOf(FurnitureType.Collector).Sum(f => f.Machine!.Efficiency);

    /// <summary>새 구간에 들어선다: 알림 한 줄 + 이점.</summary>
    public static void Enter(World w, VoyageSystem v, Leg leg, LegSpec s, Rng r)
    {
        w.Log.Add(w.Tick, LogKind.Ship, $"{s.Name}에 들어섰다 — {leg.Name} ({s.Note})");
        string? got = s.Perk switch
        {
            LegPerk.Mining => Mine(w, v),
            LegPerk.Ice => Ice(w, r),
            LegPerk.Propellant => Scoop(w, v),
            LegPerk.Research => Observe(w, r),
            LegPerk.Credits => Peddle(w, v),
            LegPerk.Battery => Induct(w),
            LegPerk.Salvage => Strip(w, v, r),
            LegPerk.Calm => Ease(w),
            LegPerk.Air => Algae(w),
            LegPerk.Nutrient => Filter(w, r),
            LegPerk.Repair => Tune(w),
            LegPerk.Training => Steer(w),
            _ => null, // 빨리 간다 · 센서가 맑다: 지나는 동안
        };
        if (got != null) w.History.Add(w, HistoryKind.Decision, $"{leg.Name} — {got}", null, log: true);
    }

    private static string? Mine(World w, VoyageSystem v)
    {
        w.Space.SetMean(PropulsionSystem.ZoneDensity(w.Propulsion.Zone) * v.MiningMul);
        return null;
    }

    private static string Ice(World w, Rng r)
    {
        float arms = Arms(w);
        if (arms <= 0.05f) return "얼음 부스러기가 지나가는데 채집 장치가 멈춰 있다";
        int put = Put(w, ItemKind.Ice, 3 + r.Range(0, 4) + (arms >= 1.5f ? 2 : 0));
        float water = MathF.Min(20f, MathF.Max(0f, w.Water.Capacity - w.Water.Level));
        w.Water.Level += water;
        return $"혜성 얼음을 건졌다 — 얼음 {put}" + (water >= 1f ? $" · 녹여 물 {water:0}L" : "");
    }

    private static string Scoop(World w, VoyageSystem v)
    {
        var p = w.Propulsion;
        if (v.Drifting) return "엔진이 멎어 가스를 퍼 담지 못했다";
        float add = MathF.Min(p.Capacity * 0.3f, MathF.Max(0f, p.Capacity - p.Propellant));
        p.Propellant += add;
        return add >= 1f ? $"대기를 스치며 가스를 퍼 담았다 — 추진제 +{add:0}kg" : "탱크가 가득 차 있어 그냥 지나갔다";
    }

    private static string Observe(World w, Rng r)
    {
        bool lab = w.Ship.RoomsOf(RoomType.Observatory).Any(rm => !rm.Abandoned && !rm.Detached);
        float add = 4f + r.Range(0f, 2f) + (lab ? 3f : 0f);
        w.Research += add;
        return $"펄서의 박동을 관측했다 — 연구 +{add:0}" + (lab ? " (관측실)" : "");
    }

    /// <summary>오가는 상선에 남는 원료를 판다 (기항지보다 싸게).</summary>
    private static string Peddle(World w, VoyageSystem v)
    {
        var sold = new List<string>();
        float got = 0f;
        foreach (var (k, keep, price) in new[] { (ItemKind.MetalOre, 14, 0.5f), (ItemKind.Silicate, 8, 0.35f), (ItemKind.Carbon, 8, 0.35f), (ItemKind.Ice, 10, 0.3f) })
        {
            int n = w.Ship.CountStored(k) - keep;
            if (n <= 0 || !Life.Take(w, k, n)) continue;
            got += n * price;
            sold.Add($"{ItemKinds.Name(k)} {n}");
        }
        v.Credits += got;
        return sold.Count > 0 ? $"지나가는 상선에 팔았다 — {string.Join(" · ", sold)} (돈 +{got:0})" : "상선과 인사만 나눴다 — 팔 것이 없었다";
    }

    private static string Induct(World w)
    {
        var pw = w.Power;
        if (pw.BatteryCapacity <= 0f) return "자기장이 세지만 담을 배터리가 없다";
        float add = MathF.Min(pw.BatteryCapacity * 0.5f, pw.BatteryCapacity - pw.BatteryCharge);
        pw.BatteryCharge += MathF.Max(0f, add);
        return $"자기장 유도 충전 — 배터리 {pw.BatteryPercent * 100:0}%";
    }

    private static string Strip(World w, VoyageSystem v, Rng r)
    {
        var got = new List<string>();
        foreach (var (k, n) in new[] { (ItemKind.Plate, 1 + r.Range(0, 3)), (ItemKind.Valve, r.Range(1, 3)), (ItemKind.Gasket, r.Range(1, 4)), (ItemKind.Fuse, r.Range(1, 3)), (ItemKind.Cable, r.Range(1, 4)), (ItemKind.Motor, r.Range(0, 2)) })
        {
            int put = Put(w, k, n);
            if (put > 0) got.Add($"{ItemKinds.Name(k)} {put}");
        }
        v.Salvaged++;
        return got.Count > 0 ? $"폐선에서 부품을 떼어 왔다 — {string.Join(" · ", got)}" : "폐선을 뒤졌지만 실을 데가 없었다";
    }

    private static string Ease(World w)
    {
        int n = 0;
        foreach (var c in w.Crew.Where(c => !c.Dead)) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.08f); n++; }
        return $"순찰선 불빛이 보인다 — 다들 마음이 놓였다 ({n}명)";
    }

    private static string Algae(World w)
    {
        float add = MathF.Min(w.Air.ReserveCapacity * 0.1f, MathF.Max(0f, w.Air.ReserveCapacity - w.Air.Reserve));
        w.Air.Reserve += add;
        return $"센 햇빛에 조류 판이 산소를 냈다 — 공기 탱크 {w.Air.Reserve / MathF.Max(1f, w.Air.ReserveCapacity) * 100:0}%";
    }

    private static string Filter(World w, Rng r)
    {
        if (Arms(w) <= 0.05f) return "유기물 구름인데 채집 장치가 멈춰 있다";
        int a = Put(w, ItemKind.Nutrient, 3 + r.Range(0, 3)), b = Put(w, ItemKind.Seed, 1 + r.Range(0, 2));
        return $"유기물을 걸러 냈다 — 양분 {a} · 씨앗 {b}";
    }

    private static string Tune(World w)
    {
        var fixedOnes = new List<string>();
        foreach (var m in w.Ship.Machines.Where(m => m.Faults.Count == 0 && !m.Body.Room.Detached && m.Wear > 0.1f).OrderByDescending(m => m.Wear).ThenBy(m => m.Body.Id).Take(2))
        {
            m.Wear = MathF.Max(0.05f, m.Wear - 0.2f);
            fixedOnes.Add(m.Name);
        }
        return fixedOnes.Count > 0 ? $"멈춰 선 김에 정비했다 — {string.Join(" · ", fixedOnes)}" : "멈춰 섰지만 손볼 설비가 없었다";
    }

    private static string Steer(World w)
    {
        var pilots = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderByDescending(c => c.RawSkill(Skill.Piloting)).ThenBy(c => c.Id).Take(2).ToList();
        if (pilots.Count == 0) return "협로를 자동 조타로 빠져나갔다";
        for (int i = 0; i < pilots.Count; i++) pilots[i].Practice(Skill.Piloting, i == 0 ? 0.03f : 0.015f);
        return $"{Ko.IGa(string.Join("·", pilots.Select(c => c.Name)))} 번갈아 조타를 잡고 빠져나갔다 — 조종 솜씨가 늘었다";
    }

    // ═══════════════════════════════ 기항지 성격 10 ═══════════════════════════════

    public static readonly PortTrait[] Traits =
    {
        new("trade", "교역소", 0.9f, 1.2f, Array.Empty<(ItemKind, float)>(),
            new[] { (ItemKind.TeaLeaf, 4, 0.6f), (ItemKind.Thread, 3, 0.4f), (ItemKind.Paint, 2, 0.8f) }, Background.TruckDriver,
            (w, v) => { foreach (var c in w.Crew.Where(c => !c.Dead)) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f); return "장터를 한 바퀴 돌았다 — 기분이 풀렸다"; }),
        new("shipyard", "조선소", 1f, 1f, new[] { (ItemKind.Plate, 0.6f), (ItemKind.Structure, 0.6f), (ItemKind.MetalOre, 1.3f) },
            new[] { (ItemKind.Structure, 6, 2f), (ItemKind.Bearing, 2, 4f), (ItemKind.Motor, 1, 6f) }, Background.Welder,
            (w, v) =>
            {
                var m = w.Ship.Machines.Where(m => m.Faults.Count == 0 && m.Wear >= 0.15f).OrderByDescending(m => m.Wear).ThenBy(m => m.Body.Id).FirstOrDefault();
                if (m == null) return null;
                m.Wear = 0.05f;
                return $"도크에서 {Ko.EulReul(m.Name)} 덤으로 손봐 줬다";
            }),
        new("mine", "광산", 1f, 0.9f, new[] { (ItemKind.MetalOre, 0.5f), (ItemKind.Plate, 0.7f), (ItemKind.Rare, 1.3f), (ItemKind.Ice, 1.4f) },
            new[] { (ItemKind.Gear, 2, 2f), (ItemKind.Clamp, 2, 0.8f) }, Background.Miner,
            (w, v) => { foreach (var c in w.Crew.Where(c => !c.Dead)) c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.1f); return "광부들과 한잔했다 — 이야기가 늘었다"; }),
        new("refinery", "정유소", 1.05f, 1f, new[] { (ItemKind.Ice, 2.2f), (ItemKind.Lubricant, 0.6f), (ItemKind.Fuel, 0.6f) },
            new[] { (ItemKind.Fuel, 4, 1.2f), (ItemKind.Lubricant, 4, 1f), (ItemKind.Solvent, 2, 1f) }, Background.Chemist,
            (w, v) =>
            {
                var p = w.Propulsion;
                if (p.Propellant >= p.Capacity * 0.95f || v.Credits < 4f) return null;
                v.Credits -= 4f;
                p.Propellant = p.Capacity;
                return "추진제를 싸게 가득 채웠다";
            }),
        new("science", "관측 기지", 1.15f, 1f, new[] { (ItemKind.Rare, 1.8f), (ItemKind.Electronics, 0.8f), (ItemKind.Sensor, 0.7f) },
            new[] { (ItemKind.Sensor, 1, 5f), (ItemKind.Fiber, 3, 1.5f), (ItemKind.Thermocouple, 2, 1.5f) }, Background.Astronomer,
            (w, v) => { w.Research += 6f; return "관측 자료를 나눠 받았다 — 연구 +6"; }),
        new("frontier", "개척지", 1.35f, 1.4f, new[] { (ItemKind.Meal, 1.4f) },
            new[] { (ItemKind.Seed, 4, 0.6f), (ItemKind.Nutrient, 4, 0.6f) }, Background.Carpenter,
            (w, v) => { v.Credits += 5f; return "개척민의 짐을 맡았다 — 짐삯 선금 5"; }),
        new("medical", "병원선", 1f, 1f, new[] { (ItemKind.MedKit, 0.6f), (ItemKind.Bandage, 0.6f), (ItemKind.Disinfectant, 0.6f) },
            new[] { (ItemKind.Vitamin, 4, 0.8f), (ItemKind.Gloves, 4, 0.3f) }, Background.Nurse,
            (w, v) =>
            {
                var hurt = w.Crew.Where(c => !c.Dead && c.Vitals.Injury > 0.05f).ToList();
                foreach (var c in hurt) c.Vitals.Injury = MathF.Max(0f, c.Vitals.Injury - 0.2f);
                return hurt.Count > 0 ? $"의사가 다친 사람 {hurt.Count}명을 봐 줬다" : "의사가 다들 건강하다고 했다";
            }),
        new("farm", "온실 정거장", 0.95f, 1f, new[] { (ItemKind.Meal, 0.5f), (ItemKind.Coffee, 0.7f), (ItemKind.Spice, 0.7f) },
            new[] { (ItemKind.Seed, 4, 0.4f), (ItemKind.Nutrient, 4, 0.4f), (ItemKind.TeaLeaf, 3, 0.5f) }, Background.Gardener,
            (w, v) =>
            {
                foreach (var c in w.Crew.Where(c => !c.Dead)) { c.Needs.Food = MathF.Min(1f, c.Needs.Food + 0.2f); c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f); }
                return "갓 딴 채소로 한 상 얻어먹었다";
            }),
        new("military", "군 보급기지", 1.1f, 0.9f, new[] { (ItemKind.Extinguisher, 0.7f), (ItemKind.Suit, 0.8f), (ItemKind.Sealant, 0.7f) },
            new[] { (ItemKind.Extinguisher, 2, 3f), (ItemKind.Suit, 1, 8f), (ItemKind.CellPack, 2, 2f) }, Background.Firefighter,
            (w, v) =>
            {
                int a = Put(w, ItemKind.Sealant, 2), b = Put(w, ItemKind.Extinguisher, 1);
                return a + b > 0 ? $"보급 할당을 받았다 — 실링폼 {a} · 소화기 {b}" : null;
            }),
        new("haven", "쉼터", 1f, 0.95f, new[] { (ItemKind.TeaLeaf, 0.6f), (ItemKind.Soap, 0.7f) },
            new[] { (ItemKind.TeaLeaf, 3, 0.5f), (ItemKind.Desiccant, 2, 0.5f) }, Background.Monk,
            (w, v) =>
            {
                foreach (var c in w.Crew.Where(c => !c.Dead)) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.1f); c.Memory.Trauma = MathF.Max(0f, c.Memory.Trauma - 0.03f); }
                return "조용한 정원에서 하루 쉬었다";
            }),
    };

    public static PortTrait Trait(string id) => Traits.First(t => t.Id == id);

    /// <summary>이 기항지의 성격 (표에 없는 이름은 글자로 정한다 — 늘 같다).</summary>
    public static PortTrait PortOf(string place)
    {
        foreach (var (p, t) in Ports) if (p == place) return Trait(t);
        int h = 0;
        foreach (char ch in place) h = unchecked(h * 31 + ch);
        return Traits[(int)((uint)h % (uint)Traits.Length)];
    }

    /// <summary>기항지 값을 매긴 살 목록 (기본 목록 + 이 기항지가 파는 물건).</summary>
    public static IEnumerable<(ItemKind k, int want, float price)> Wares(PortTrait port, IEnumerable<(ItemKind k, int want, float price)> basic) =>
        basic.Concat(port.Wares).Select(x => (x.k, x.want, x.price * port.Buy(x.k)));

    /// <summary>기항지에서 탄 사람: 그 기항지다운 경력 (솜씨 · 자격이 따라온다).</summary>
    public static string Recruit(World w, CrewMember c, PortTrait port)
    {
        Life.Assign(w); // 습관 · 가치관은 지금 정한다 (따로 굴리는 난수라 늦게 정해도 같다)
        c.Background = port.Recruit;
        var (skill, bonus, quals) = Life.Gift(port.Recruit);
        c.SkillLevels[(int)skill] = MathF.Min(1f, c.SkillLevels[(int)skill] + bonus);
        foreach (var q in quals) c.Quals.Add(q);
        return $" ({Life.Name(port.Recruit)})";
    }
}
