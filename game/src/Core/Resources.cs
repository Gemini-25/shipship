using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>만드는 곳: 작업대(부품·소모품) 또는 정제기(원료 → 기본 수리재).</summary>
public enum Station { Workbench, Refinery }

/// <summary>
/// 만드는 법 하나. 첫 재료는 손에 들고, 나머지는 공구 가방에 담아 간다.
/// Target은 평소 쌓아 두려는 개수 (그 아래면 틈날 때 만든다).
/// </summary>
public sealed record Recipe(ItemKind Product, (ItemKind kind, int count)[] Inputs, float Hours, Skill Skill, Station Station,
    int Target, float MinSkill = 0f, int Yield = 1, string? Lesson = null)
{
    /// <summary>
    /// Lesson이 있으면 처음부터 아는 법이 아니다. 그 사고를 여러 번 겪고 교훈을 얻어야 궁리해 낸다 (Doctrine).
    /// </summary>
    public bool FromProduce => Inputs[0].kind == ItemKind.Produce;
}

/// <summary>
/// 리뷰어 안의 재료 흐름: 우주 공간 → 채집 장치 → 원료 → 정제 → 기본 수리재 → 부품 제작.
/// 평소엔 천천히 쌓이고, 큰 사고 한 번이 몇 주 치를 쓴다.
/// </summary>
public static class Recipes
{
    private static (ItemKind, int)[] In(params (ItemKind, int)[] x) => x;

    public static readonly Recipe[] All =
    {
        // 작업대: 남는 채소 → 소모품
        new(ItemKind.Filter, In((ItemKind.Produce, 3)), 1.2f, Skill.Mechanics, Station.Workbench, 10),
        new(ItemKind.Lubricant, In((ItemKind.Produce, 3)), 1.2f, Skill.Mechanics, Station.Workbench, 10),
        new(ItemKind.Fuel, In((ItemKind.Produce, 4)), 2f, Skill.Mechanics, Station.Workbench, 3),

        // 작업대: 기본 수리재 → 일반 부품
        new(ItemKind.Bearing, In((ItemKind.Plate, 1)), 2f, Skill.Mechanics, Station.Workbench, 3),
        new(ItemKind.Pump, In((ItemKind.Plate, 2), (ItemKind.Cable, 1)), 4f, Skill.Mechanics, Station.Workbench, 2),
        new(ItemKind.Motor, In((ItemKind.Cable, 2), (ItemKind.Plate, 1)), 3f, Skill.Electrical, Station.Workbench, 3),
        new(ItemKind.PowerController, In((ItemKind.Electronics, 2), (ItemKind.Cable, 1)), 4f, Skill.Electrical, Station.Workbench, 2),
        new(ItemKind.Sensor, In((ItemKind.Electronics, 1)), 2f, Skill.Electrical, Station.Workbench, 2),

        // 고급 부품: 희귀 소재 + 숙련된 사람의 긴 정밀 작업
        new(ItemKind.ReactorControl, In((ItemKind.Electronics, 3), (ItemKind.Rare, 1), (ItemKind.Plate, 1)), 12f, Skill.Engineering,
            Station.Workbench, 1, MinSkill: 0.6f),

        // 교훈으로 얻는 법 (v7): 불을 여러 번 겪은 뒤에야 빈 소화기를 다시 채울 궁리를 한다
        new(ItemKind.Extinguisher, In((ItemKind.Carbon, 1), (ItemKind.Plate, 1)), 1.5f, Skill.Mechanics, Station.Workbench, 8, Lesson: "fire"),

        // 정제기: 원료 → 기본 수리재
        new(ItemKind.Plate, In((ItemKind.MetalOre, 2)), 1f, Skill.Mechanics, Station.Refinery, 12),
        new(ItemKind.Structure, In((ItemKind.MetalOre, 3)), 1.5f, Skill.Mechanics, Station.Refinery, 6),
        new(ItemKind.Cable, In((ItemKind.MetalOre, 1)), 0.6f, Skill.Electrical, Station.Refinery, 12),
        new(ItemKind.Electronics, In((ItemKind.Silicate, 1), (ItemKind.MetalOre, 1)), 1.5f, Skill.Electrical, Station.Refinery, 6),
        new(ItemKind.Fuse, In((ItemKind.Silicate, 1)), 0.5f, Skill.Electrical, Station.Refinery, 8, Yield: 2),
        new(ItemKind.Sealant, In((ItemKind.Carbon, 2)), 1f, Skill.Mechanics, Station.Refinery, 18),
        // 채소가 모자랄 때의 길: 탄소 원료로 활성탄 필터와 합성 윤활유 (필터 ← 채소 ← 물 ← 정수기 ← 필터의 고리를 끊는다)
        new(ItemKind.Filter, In((ItemKind.Carbon, 1)), 0.8f, Skill.Mechanics, Station.Refinery, 6),
        new(ItemKind.Lubricant, In((ItemKind.Carbon, 1), (ItemKind.Ice, 1)), 0.8f, Skill.Mechanics, Station.Refinery, 6),
    };

    public static Recipe? For(ItemKind k) => All.FirstOrDefault(r => r.Product == k);

    /// <summary>그 물건을 그 장소에서 만드는 법 (같은 물건도 작업대와 정제기에서 만드는 법이 다를 수 있다).</summary>
    public static Recipe? For(ItemKind k, Station s) => All.FirstOrDefault(r => r.Product == k && r.Station == s);

    public static IEnumerable<Recipe> AllFor(ItemKind k) => All.Where(r => r.Product == k);

    public static FurnitureType StationType(Station s) => s == Station.Workbench ? FurnitureType.Workbench : FurnitureType.Refinery;

    /// <summary>얼음 한 덩이를 녹여 공기 탱크와 물에 보태는 양.</summary>
    public const float IceAir = 700f;
    public const float IceWater = 3f;
    public const float IceHoursEach = 0.35f;

    /// <summary>재료 가치 (수리력 지표용): 원료 0.3, 기본 1, 일반 3, 고급 8.</summary>
    public static float Value(ItemKind k) => ItemKinds.Tier(k) switch
    {
        ItemTier.Raw => k == ItemKind.Rare ? 4f : 0.3f,
        ItemTier.Basic => 1f,
        ItemTier.General => 3f,
        ItemTier.Advanced => 8f,
        _ => 0f,
    };
}

/// <summary>
/// 우주선 주변 환경. 잔해·먼지 밀도는 천천히 오르내린다 (항로에 따라 달라지는 것의 가장 단순한 형태).
/// </summary>
public sealed class SpaceEnvironment
{
    public float Density { get; private set; } = 1f;

    /// <summary>v11.2: 지나는 공간의 평균 밀도 (보통 항로 1, 잔해 지대 1.7) — 밀도는 여기로 천천히 끌려간다.</summary>
    public float Mean { get; private set; } = 1f;
    public void SetMean(float mean) => Mean = mean;

    public string DensityName => Density < 0.6f ? "희박" : Density < 1.25f ? "보통" : "잔해 많음";

    /// <summary>채집되는 원료의 비율 (금속성 먼지가 가장 흔하고, 희귀 소재는 아주 드물다).</summary>
    public static readonly (ItemKind kind, float weight)[] Composition =
    {
        (ItemKind.MetalOre, 0.45f), (ItemKind.Silicate, 0.15f), (ItemKind.Carbon, 0.16f), (ItemKind.Ice, 0.23f), (ItemKind.Rare, 0.01f),
    };

    public void Update(World w, float dtHours)
    {
        // 평균 1로 돌아가려는 느린 무작위 걸음 (몇 주 단위로 풍부했다가 희박해진다)
        float step = w.Rng.Range(-0.06f, 0.06f) * MathF.Sqrt(dtHours) + (Mean - Density) * (Mean > 1f ? 0.08f : 0.01f) * dtHours;
        Density = Math.Clamp(Density + step, 0.3f, 2.4f);
    }
}

/// <summary>
/// 채집 장치: 선체 바깥의 채집 팔이 먼지·미세 운석·얼음·잔해를 모아 호퍼에 담는다.
/// 전기와 정비가 필요하고(원료 → 부품 → 채집 장치 수리의 고리), 종류마다 호퍼 칸이 차면 그 종류는 흘려보낸다.
/// </summary>
public sealed class CollectionSystem
{
    /// <summary>효율 1, 밀도 1일 때 한 시간에 모으는 원료 (하루 약 3.6개).</summary>
    public static float RatePerHour = 0.15f;

    /// <summary>호퍼의 종류별 칸 크기.</summary>
    public const int BinSize = 15;

    private readonly World _world;
    private readonly Dictionary<Furniture, float> _progress = new();

    public int Collected { get; private set; }

    public CollectionSystem(World world) => _world = world;

    public void Update(float dtHours)
    {
        var w = _world;
        w.Space.Update(w, dtHours);
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.Collector))
        {
            var m = f.Machine!;
            float eff = m.Efficiency;
            if (eff <= 0f) continue;
            float acc = (_progress.TryGetValue(f, out var a) ? a : 0f) + RatePerHour * eff * m.Rating * w.Space.Density * dtHours;
            while (acc >= 1f)
            {
                acc -= 1f;
                var kind = Pick(w.Rng);
                if (f.Storage!.Count(kind) >= (kind == ItemKind.Rare ? 5 : BinSize)) continue; // 그 칸이 가득: 흘려보낸다
                if (f.Storage.Add(kind, 1) > 0)
                {
                    Collected++;
                    if (kind == ItemKind.Rare) w.Log.Add(w.Tick, LogKind.Ship, "채집 장치가 희귀 소재를 건졌다");
                }
            }
            _progress[f] = acc;
        }
    }

    private static ItemKind Pick(Rng rng)
    {
        float r = rng.Float();
        foreach (var (kind, weight) in SpaceEnvironment.Composition)
        {
            if (r < weight) return kind;
            r -= weight;
        }
        return ItemKind.MetalOre;
    }
}

/// <summary>
/// 함선 지표 (리뷰어 안 17번): 전력·생명유지·수리력·거주성·구조·엔진.
/// 처음 출발할 때를 100으로 두고 지금을 비교한다. 사고와 개조를 거치면 어떤 건 떨어지고 어떤 건 오른다.
/// </summary>
public sealed record ShipProfile(float Power, float LifeSupport, float Repair, float Habitability, float Structure, float Engine)
{
    public static readonly string[] Names = { "전력", "생명유지", "수리력", "거주성", "구조", "엔진" };

    public float[] Values => new[] { Power, LifeSupport, Repair, Habitability, Structure, Engine };

    /// <summary>전기 여부와 상관없이 설비가 낼 수 있는 몫 (고장·등급·수명·마모).</summary>
    public static float Potential(Machine m) =>
        m.FaultFactor * Grades.Output(m.Grade) * (0.6f + 0.4f * m.Condition) * (1f - 0.25f * m.Wear * m.Wear)
        * (m.Body.Room.Abandoned && !m.Spec.Critical ? 0.5f : 1f);

    /// <summary>
    /// v10.3: 생명유지 지표는 산소 농도가 아니다 — 산소 발생기·정수기가 낼 수 있는 몫과 공기 비축을 더한 상대값이다.
    /// 지금 기능과 남은 비축을 따로 보여 준다.
    /// </summary>
    public static string LifeBreakdown(World w)
    {
        var ship = w.Ship;
        var o2 = ship.FurnitureOf(FurnitureType.OxygenGenerator).ToList();
        var rec = ship.FurnitureOf(FurnitureType.WaterRecycler).ToList();
        float o2Now = o2.Count == 0 ? 0f : o2.Average(f => Potential(f.Machine!)) * 100f;
        float recNow = rec.Count == 0 ? 0f : rec.Average(f => Potential(f.Machine!)) * 100f;
        return $"산소 발생기 {o2.Count}대 {o2Now:0}% · 정수기 {rec.Count}대 {recNow:0}% · 공기 탱크 {w.Air.Reserve / w.Air.ReserveCapacity * 100:0}% · 물 {w.Water.Level:0}L";
    }

    public static ShipProfile Measure(World w)
    {
        var ship = w.Ship;
        float Sum(FurnitureType t) => ship.FurnitureOf(t).Sum(f => Potential(f.Machine!));
        float Best(FurnitureType t) => ship.FurnitureOf(t).Select(f => Potential(f.Machine!)).DefaultIfEmpty(0f).Max();

        float reactor = ship.FurnitureOf(FurnitureType.ReactorCore).Sum(f => PowerGrid.ReactorMaxKw * f.Machine!.Rating * Potential(f.Machine!));
        float cooling = ship.FurnitureOf(FurnitureType.CoolantPump).Sum(f => Potential(f.Machine!) * f.Machine!.Rating) * PowerGrid.CoolingPerPumpKw * 0.95f;
        float battery = ship.FurnitureOf(FurnitureType.Battery).Sum(f => PowerGrid.BatteryKwh(f) * f.Machine!.FaultFactor * (0.5f + 0.5f * f.Machine.Condition));
        float power = MathF.Min(reactor, cooling) + battery * 0.1f + ship.FurnitureOf(FurnitureType.AuxGenerator).Sum(f => Potential(f.Machine!) * f.Machine!.Rating) * PowerGrid.AuxKw * 0.5f;
        // v10.1: 예비 배선은 회로 하나가 끊겨도 받쳐 주는 힘이다 (용량이 아니라 버티는 힘 — 지표가 이걸 못 봤다)
        power *= 1f + 0.03f * w.Power.Jumpers.Count(j => j.Permanent && !j.Burnt);

        float life = Sum(FurnitureType.OxygenGenerator) + 0.5f * Sum(FurnitureType.WaterRecycler) + w.Air.Reserve / w.Air.ReserveCapacity;

        float materials = ItemKinds.All.Sum(k => Recipes.Value(k) * ship.CountStored(k));
        float facilities = Best(FurnitureType.Workbench) + Best(FurnitureType.Refinery) + Best(FurnitureType.Collector);
        float repair = facilities * 20f + materials * 0.5f;

        float total = ship.Rooms.Sum(r => r.Volume);
        float living = ship.Rooms.Where(r => !r.Abandoned).Sum(r => r.Volume);
        int crew = Math.Max(1, w.Crew.Count(c => !c.Dead));
        float beds = ship.FurnitureOf(FurnitureType.Bed).Count(f => !f.Room.Abandoned)
                     + ship.FurnitureOf(FurnitureType.Cot).Where(f => !f.Room.Abandoned).Sum(f => f.Improved ? 1f : 0.7f);
        float amenities = (Best(FurnitureType.Stove) + Best(FurnitureType.Fridge) + Best(FurnitureType.MealDispenser)
                           + (ship.RoomsOf(RoomType.Lounge).Any(r => !r.Abandoned) ? 1f : 0f)) / 4f;
        float habit = 50f * living / Math.Max(1f, total) + 30f * MathF.Min(1.5f, beds / crew) + 20f * amenities;

        var hull = ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Value).ToList();
        float walls = hull.Count == 0 ? 1f : hull.Average(x => x.MaxIntegrity * (x.Breach > 0f && !x.Patched ? 0.2f : x.Patched ? 0.6f : MathF.Min(1f, x.Integrity / MathF.Max(0.01f, x.MaxIntegrity))));
        int nonCorridor = Math.Max(1, ship.Rooms.Count(r => r.Type != RoomType.Corridor));
        // v8: 연결부 (설계 연결부 수 대비 버티는 힘 — 증설하면 100을 넘는다), 떨어져 나간 방
        var jointed = ship.Rooms.Where(r => !r.Detached && r.DesignJoints > 0).ToList();
        float joints = jointed.Count == 0 ? 1f : jointed.Average(r => MathF.Min(1.4f, StructureSystem.Capacity(r) / r.DesignJoints));
        int detached = ship.Rooms.Count(r => r.Detached && !r.Merged); // v10.12 합친 빈 칸은 잃은 방이 아니다
        float structure = 100f * (0.6f * walls + 0.4f * joints)
                          * (1f - 0.3f * ship.Rooms.Count(r => r.Abandoned && !r.Detached) / (float)nonCorridor)
                          * (1f - 0.6f * detached / (float)nonCorridor);

        float engine = Sum(FurnitureType.EngineCore);
        return new ShipProfile(power, life, repair, habit, structure, engine);
    }

    /// <summary>기준(처음)을 100으로 둔 값.</summary>
    public ShipProfile RelativeTo(ShipProfile basis)
    {
        static float R(float now, float then) => then > 0.001f ? now / then * 100f : 100f;
        return new ShipProfile(R(Power, basis.Power), R(LifeSupport, basis.LifeSupport), R(Repair, basis.Repair),
            R(Habitability, basis.Habitability), R(Structure, basis.Structure), R(Engine, basis.Engine));
    }

    public override string ToString() => string.Join(" · ", Names.Zip(Values, (n, v) => $"{n} {v:0}"));
}
