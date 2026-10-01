using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>식량 순환의 숫자들. 재배대 4개 = 6명이 하루 네 끼 먹고 조금 남는 양.</summary>
public static class FoodChain
{
    /// <summary>작업대에서 채소로 만들 수 있는 소모품 (식물 섬유 필터, 식물성 윤활유, 바이오 연료).</summary>
    public static readonly ItemKind[] Fabricable = { ItemKind.Filter, ItemKind.Lubricant, ItemKind.Fuel };

    /// <summary>만드는 데 드는 채소, 시간, 쌓아 둘 목표 개수.</summary>
    public static (int produce, float hours, int target) FabricateSpec(ItemKind k) => k switch
    {
        ItemKind.Fuel => (4, 2f, 3),
        _ => (3, 1.2f, 10),
    };

    public static float GrowHours = 60f;

    /// <summary>v10.1: 재배대 크기 (처음 실린 네 칸짜리 = 1, 항해 중에 짜 넣은 두 칸짜리 = 0.5).</summary>
    public static float BedSize(Furniture bed) => bed.Cells.Count >= 4 ? 1f : bed.Cells.Count / 4f;
    public static int HarvestYield = 14;
    public const int ProducePerBatch = 4;
    public const int MealsPerBatch = 6;
    public const float CookHours = 0.6f;
    public const float CareDecayHours = 22f;
}

/// <summary>물. 정수기가 되살리고, 재배대와 사람이 쓴다. 배관 안에 있으니 우주선 단위로 둔다.</summary>
public sealed class WaterSystem
{
    public static float RecyclerLitersPerHour = 3.4f;
    public static float BedLitersPerHour = 0.45f;
    public static float CrewLitersPerHour = 0.12f;

    public float Level { get; set; } = 300f;
    public float Capacity { get; set; } = 400f;
    public float Produced { get; private set; }
    public float Consumed { get; private set; }

    public void Update(World w, float dt)
    {
        float produce = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).Sum(f => f.Machine!.Efficiency * f.Machine.Rating) * RecyclerLitersPerHour;
        float consume = w.Crew.Count(c => !c.Dead) * CrewLitersPerHour;
        foreach (var bed in w.Ship.FurnitureOf(FurnitureType.GrowBed))
            if (bed.Machine!.Efficiency > 0f && bed.Machine.Crop is { Ripe: false } && w.Piping.WaterTo(bed.Room)) consume += BedLitersPerHour * FoodChain.BedSize(bed);
        Produced = produce;
        Consumed = consume;
        Level = Math.Clamp(Level + (produce - consume) * dt, 0f, Capacity);
    }
}

/// <summary>
/// 설비의 시간 흐름: 마모, 고장, 작물 성장, 냉장고 부패.
/// 고장은 정해진 이벤트가 아니라 마모가 쌓인 결과로 확률적으로 생긴다.
/// </summary>
public sealed class MachineSystem
{
    private readonly World _world;
    private readonly Dictionary<Furniture, float> _spoil = new();

    /// <summary>진공·추위·불로 잃은 작물 수 (누적).</summary>
    public int CropsLost { get; set; }

    public MachineSystem(World world) => _world = world;

    public void Update(float dt)
    {
        var w = _world;
        foreach (var m in w.Ship.Machines)
        {
            // 정제기는 누가 앞에서 일할 때만 제대로 돈다 (평소엔 대기 전력)
            if (m.Body.Type == FurnitureType.Refinery)
                m.Active = w.Crew.Any(c => c.Pose == Pose.Working && c.Job?.Target == m.Body);
            bool running = (m.Powered || m.Spec.PowerDraw <= 0f) && m.Active && !m.Stopped;
            float rate = 1f / (m.Spec.WearDays * 24f) * (running ? 1f : 0.25f) * Grades.Wear(m.Grade) * Tech.Of(m).Wear;
            bool makeshift = false;
            foreach (var f in m.Faults) if (f.Stage > 0) makeshift = true;
            if (makeshift) rate *= 2f; // 임시로 살린 설비는 무리해서 돈다
            m.Wear = MathF.Min(1f, m.Wear + rate * dt);

            // 임시 우회는 풀릴 수 있다
            if (makeshift)
                foreach (var f in m.Faults)
                {
                    if (f.Stage == 0 || !running) continue;
                    if (!w.Rng.Chance((f.Stage == 1 ? 0.03f : 0.01f) * dt)) continue;
                    f.Stage--;
                    w.RaiseAlert($"{m.Name} 임시 수리가 풀렸다 ({(f.Stage == 0 ? "다시 멈춤" : f.StageName)})", m.Body.Room,
                        m.Spec.Critical ? AlertLevel.Critical : AlertLevel.Warning, shipWide: m.Spec.Critical);
                    w.Board.RequestScan();
                    break;
                }

            // v11.0: 닳아서 날 고장의 일부는 먼저 기척(전조)을 낸다 — 누가 알아채면 싸게 막는다
            if (w.Rng.Float() < m.FaultChancePerHour * dt && !Prevention.Foreshadow(w, m)) Break(m);

            // 파손: 운석 파편이나 불에 수명이 바닥난 설비는 고칠 수 없고 통째로 갈아야 한다
            if (m.Condition < 0.18f && !m.Has(FaultKind.Wrecked))
            {
                m.Faults.Add(new Fault { Kind = FaultKind.Wrecked, Since = w.Tick, PartOverride = Faults.KeyPart(m.Body.Type) });
                w.Causes.OnFault(m, m.Faults[^1]);
                MarkLog.Add(m.Marks, w.Tick, "파손");
                w.History.Add(w, HistoryKind.Damage, $"{m.Name} 파손 — 통째로 갈아야 한다", m.Body.Room);
                w.History.Lost($"{m.Name} 파손");
                w.RaiseAlert($"{m.Name} 파손 — 통째로 갈아야 한다 ({ItemKinds.Name(Faults.KeyPart(m.Body.Type))} 필요)", m.Body.Room,
                    m.Spec.Critical ? AlertLevel.Critical : AlertLevel.Warning, shipWide: m.Spec.Critical);
                w.Board.RequestScan();
            }

            if (m.Crop is CropState crop) GrowCrop(m, crop, dt);
        }

        // 냉장고가 멈추면 음식이 상한다
        foreach (var fridge in w.Ship.FurnitureOf(FurnitureType.Fridge))
        {
            if (fridge.Machine!.Efficiency > 0.2f) continue;
            float acc = (_spoil.TryGetValue(fridge, out var a) ? a : 0f) + fridge.Storage!.Total * 0.03f * dt;
            int lost = 0;
            while (acc >= 1f)
            {
                acc -= 1f;
                var kind = fridge.Storage.Count(ItemKind.Meal) > 0 ? ItemKind.Meal : ItemKind.Produce;
                lost += fridge.Storage.Take(kind, 1);
            }
            _spoil[fridge] = acc;
            if (lost > 0 && w.Rng.Chance(0.2f))
                w.Log.Add(w.Tick, LogKind.Warning, $"{fridge.Label}의 음식이 상하고 있다");
        }
    }

    private void GrowCrop(Machine m, CropState crop, float dt)
    {
        // 진공이나 영하에서는 작물이 얼어 죽는다 (구획을 포기하면 수경재배도 잃는다)
        var air = m.Body.Room.Air;
        if (air.Pressure < 40f || air.Temperature < 2f)
        {
            bool alive = crop.Growth > 0.02f;
            crop.Care = MathF.Max(0f, crop.Care - 0.5f * dt);
            if (alive && crop.Care <= 0f)
            {
                crop.Growth = 0f;
                CropsLost++;
                _world.Log.Add(_world.Tick, LogKind.Warning, $"{m.Name}의 작물이 {(air.Pressure < 40f ? "진공에서" : "추위에")} 죽었다");
            }
            return;
        }
        crop.Care = MathF.Max(0f, crop.Care - dt / FoodChain.CareDecayHours);
        if (crop.Ripe) return;
        bool water = _world.Water.Level > 1f && _world.Piping.WaterTo(m.Body.Room); // v9: 급수 본관이 끊기면 물이 안 간다
        // v10.10: 본관이 끊겨도 물통으로 부어 준 물이 남아 있으면 (물은 부을 때 탱크에서 뺐다)
        if (!water && crop.HandWateredHours > 0f) { water = true; crop.HandWateredHours = MathF.Max(0f, crop.HandWateredHours - dt); }
        crop.DryHours = water ? MathF.Max(0f, crop.DryHours - dt * 2f) : crop.DryHours + dt;
        if (!water && crop.DryHours > 36f && crop.Growth > 0.02f)
        {
            crop.Growth = 0f;
            crop.DryHours = 0f;
            CropsLost++;
            _world.Log.Add(_world.Tick, LogKind.Warning, $"{m.Name}의 작물이 말라 죽었다");
            MarkLog.Add(m.Marks, _world.Tick, "물을 못 받아 작물이 말라 죽었다");
            return;
        }
        float rate = dt / FoodChain.GrowHours * m.Efficiency * m.Rating * (1f + Modules.Bonus(_world, FurnitureType.LedPanel, m.Body.Room))
                     * (0.45f + 0.55f * crop.Care) * (water ? 1f : 0f) * AmbienceSystem.CropFactor(m.Body.Room); // v12.6 진동·방사선
        crop.Growth = MathF.Min(1f, crop.Growth + rate);
        if (crop.Ripe) _world.Board.RequestScan();
    }

    private static bool IsCircuitFault(FaultKind k) => k is FaultKind.BreakerTrip or FaultKind.ShortCircuit;

    /// <summary>설비 하나를 고장 낸다. 마모에서 오지만, 나중엔 사고가 직접 부를 수도 있다.</summary>
    public Fault? Break(Machine m, FaultKind? forced = null)
    {
        var w = _world;
        // 회로 고장은 회로마다 따로 날 수 있고, 나머지는 같은 고장이 겹치지 않는다
        var choices = m.Spec.FaultKinds.Where(k => IsCircuitFault(k) || !m.Has(k)).ToList();
        if (forced != null) choices = new List<FaultKind> { forced.Value };
        if (choices.Count == 0) return null;
        var kind = w.Rng.Pick(choices);

        int circuit = -1;
        if (IsCircuitFault(kind))
        {
            var free = Enumerable.Range(0, PowerGrid.CircuitCount).Where(i => !m.Faults.Any(f => f.Circuit == i)).ToList();
            if (free.Count == 0) return null;
            circuit = w.Rng.Pick(free);
        }

        // 파손은 핵심 부품으로 다시 짠다 (v10.10: 파손을 직접 걸어도 부품이 비지 않게)
        var fault = new Fault { Kind = kind, Since = w.Tick, Circuit = circuit, PartOverride = kind == FaultKind.Wrecked ? Faults.KeyPart(m.Body.Type) : null };
        m.Faults.Add(fault);
        m.FaultCount++;
        w.Causes.OnFault(m, fault); // v12.2 인과 사슬
        m.Condition = MathF.Max(0.2f, m.Condition - 0.03f);

        // 역사: 설비의 이력, 회로 단락, 핵심 설비 고장
        MarkLog.Add(m.Marks, w.Tick, circuit >= 0 ? $"{PowerGrid.CircuitName(circuit)} 회로 {fault.Spec.Name}" : $"고장: {fault.Spec.Name}");
        if (circuit >= 0) w.History.CircuitFaults++;
        if (m.Spec.Critical && fault.Spec.OutputFactor < 0.6f)
        {
            w.History.CriticalBreakdowns++;
            w.History.Add(w, HistoryKind.Damage, $"{m.Name} {fault.Spec.Name}" + (circuit >= 0 ? $" ({PowerGrid.CircuitName(circuit)} 회로)" : ""), m.Body.Room);
        }

        bool critical = m.Spec.Critical || circuit == 0;
        var level = critical ? AlertLevel.Critical : fault.Spec.OutputFactor <= 0f ? AlertLevel.Warning : AlertLevel.Notice;
        string where = m.Body.Room.Name;
        string text = circuit >= 0
            ? $"{PowerGrid.CircuitName(circuit)} 회로({PowerGrid.CircuitRole(circuit)}) {fault.Spec.Name} — {where}"
            : $"{m.Name} {fault.Spec.Name} — {where}";
        w.RaiseAlert(text, m.Body.Room, level, shipWide: critical || circuit >= 0);
        w.Board.RequestScan();
        return fault;
    }
}
