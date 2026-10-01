using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15 설비 36 → 70: 개조로 다는 방 모듈 34.
// 겪은 일이 있어야 단다(그 일이 없으면 연구가 쌓였을 때) — 효과는 몇 군데 공통 지점으로 모인다:
//   전조 감지(진동·열·압력·오차) · 일 속도(기술별) · 방 오염 감소 · 잠 · 쉼 · 어두움 · 서지 · 불 · 에어락 · 우주복 · 습기 · 빨래.

public enum ModuleRole { Omen, Speed, Clean, Sleep, Relax, Light, Surge, FireDamp, Airlock, SuitDry, Dry, Laundry }

public static class ModulesV15
{
    public sealed record Row(FurnitureType Type, string Name, RoomType Room, ModuleRole Role, float Value, (ItemKind kind, int count)[] Cost, string Note,
        float Power, Skill Skill, FaultKind[] Faults, Func<World, (float, string)> Need, OmenKind? Omen = null, Skill? Boost = null);

    private static (ItemKind, int)[] C(params (ItemKind, int)[] x) => x;
    private static FaultKind[] F(params FaultKind[] x) => x;
    private static (float, string) No => (0f, "");
    private static (float, string) Research(World w, float at) => w.Research >= at ? (0.12f, $"연구 {w.Research:0}점") : No;
    private static int Faults(World w) => w.Ship.Machines.Sum(m => m.FaultCount);

    public static readonly Row[] Rows =
    {
        // 전조 감지
        new(FurnitureType.VibrationMonitor, "진동 감시기", RoomType.Engine, ModuleRole.Omen, 0.5f, C((ItemKind.Sensor, 1), (ItemKind.Cable, 1)), "그 방 설비의 진동 전조를 1.5배 잘 잡는다",
            0.2f, Skill.Electrical, F(FaultKind.SensorDrift, FaultKind.FiberBreak), w => Faults(w) >= 6 ? (0.3f, $"고장 {Faults(w)}번") : Research(w, 20f), OmenKind.Vibration),
        new(FurnitureType.ThermalCamera, "열화상 카메라", RoomType.Power, ModuleRole.Omen, 0.5f, C((ItemKind.Sensor, 1), (ItemKind.Electronics, 1)), "그 방 설비의 열 전조를 1.5배 잘 잡는다",
            0.2f, Skill.Electrical, F(FaultKind.SensorDrift, FaultKind.FanFail), w => w.Volatile.Stats.Overheats >= 1 ? (0.35f, $"과열 {w.Volatile.Stats.Overheats}번") : Research(w, 20f), OmenKind.Heat),
        new(FurnitureType.LeakDetector, "누설 감지기", RoomType.Cooling, ModuleRole.Omen, 0.5f, C((ItemKind.Sensor, 1), (ItemKind.Hose, 1)), "그 방 설비의 압력 전조를 1.5배 잘 잡는다",
            0.1f, Skill.Mechanics, F(FaultKind.SensorDrift), w => w.Moisture.Stats.Floods >= 1 ? (0.3f, $"물이 {w.Moisture.Stats.Floods}번 찼다") : Research(w, 20f), OmenKind.Pressure),
        new(FurnitureType.CalibrationRig, "교정 장비", RoomType.Workshop, ModuleRole.Omen, 0.5f, C((ItemKind.Sensor, 2), (ItemKind.Electronics, 1)), "정비실 설비의 오차 전조를 1.5배 잘 잡는다",
            0.3f, Skill.Electrical, F(FaultKind.CalibrationLoss), w => w.Watch.Stats.Phantoms >= 2 ? (0.3f, $"계기 오류 {w.Watch.Stats.Phantoms}번") : Research(w, 25f), OmenKind.Drift),
        // 일 속도
        new(FurnitureType.Oven, "오븐", RoomType.Galley, ModuleRole.Speed, 0.2f, C((ItemKind.Plate, 2), (ItemKind.Thermostat, 1)), "조리가 20% 빠르다",
            1.5f, Skill.Electrical, F(FaultKind.HeatingElement, FaultKind.ThermostatFault), w => w.Crew.Sum(c => c.Stats.MealsCooked) >= 20 ? (0.2f, "조리가 많다") : Research(w, 15f), Boost: Skill.Cooking),
        new(FurnitureType.Lathe, "선반", RoomType.Workshop, ModuleRole.Speed, 0.15f, C((ItemKind.Motor, 1), (ItemKind.Plate, 2), (ItemKind.Belt, 1)), "정비실의 기계 일이 15% 빠르다",
            1f, Skill.Mechanics, F(FaultKind.BeltSlip, FaultKind.BearingWear), w => w.Parts.Stats.ByOrigin[(int)PartOrigin.Handmade] >= 3 ? (0.3f, "부품을 손으로 자주 만든다") : Research(w, 20f), Boost: Skill.Mechanics),
        new(FurnitureType.SolderStation, "납땜대", RoomType.Workshop, ModuleRole.Speed, 0.15f, C((ItemKind.Electronics, 1), (ItemKind.Cable, 2)), "정비실의 전기 일이 15% 빠르다",
            0.3f, Skill.Electrical, F(FaultKind.HeatingElement), w => w.Net.Stats.Repairs >= 3 ? (0.2f, $"배선을 {w.Net.Stats.Repairs}번 다시 이었다") : Research(w, 20f), Boost: Skill.Electrical),
        new(FurnitureType.DiagnosticScanner, "진단 스캐너", RoomType.Medbay, ModuleRole.Speed, 0.15f, C((ItemKind.Sensor, 2), (ItemKind.Electronics, 2)), "의무실 치료가 15% 빠르다",
            0.5f, Skill.Electrical, F(FaultKind.DisplayFault, FaultKind.CalibrationLoss), w => w.Crew.Sum(c => c.Ailments.Count) >= 2 ? (0.3f, "앓는 사람이 여럿") : Research(w, 20f), Boost: Skill.Medicine),
        new(FurnitureType.NutrientDoser, "양액 조절기", RoomType.Hydroponics, ModuleRole.Speed, 0.15f, C((ItemKind.Pump, 1), (ItemKind.Sensor, 1)), "재배 일이 15% 빠르다",
            0.3f, Skill.Mechanics, F(FaultKind.NutrientImbalance, FaultKind.ValveStuck), w => Evolution.StarvedHours(w) > 4f ? (0.3f, "굶주린 적이 있다") : Research(w, 20f), Boost: Skill.Botany),
        new(FurnitureType.ToolWall, "공구 벽", RoomType.Workshop, ModuleRole.Speed, 0.05f, C((ItemKind.Plate, 2)), "정비실의 모든 일이 5% 빠르다 (공구를 찾지 않는다)",
            0f, Skill.Mechanics, F(FaultKind.Jam), w => w.Daily.Stats.Seen.Contains("misplaced") ? (0.25f, "공구를 찾느라 한 시간을 썼다") : Research(w, 10f)),
        new(FurnitureType.ReactorSimulator, "원자로 모의 장치", RoomType.Reactor, ModuleRole.Speed, 0.1f, C((ItemKind.Electronics, 2), (ItemKind.Sensor, 1)), "원자로실의 기관 일이 10% 빠르다",
            0.4f, Skill.Electrical, F(FaultKind.FirmwareCrash), w => w.History.Scrams >= 1 ? (0.25f, $"원자로가 {ShipHistory.Times(w.History.Scrams)} 섰다") : Research(w, 30f), Boost: Skill.Engineering),
        new(FurnitureType.NavComputer, "항법 컴퓨터", RoomType.Bridge, ModuleRole.Speed, 0.15f, C((ItemKind.Electronics, 2), (ItemKind.Fiber, 1)), "함교의 조종 일이 15% 빠르다",
            0.4f, Skill.Electrical, F(FaultKind.FirmwareCrash, FaultKind.InputFault), w => Research(w, 25f), Boost: Skill.Piloting),
        // 위생
        new(FurnitureType.DishWasher, "식기 세척기", RoomType.Galley, ModuleRole.Clean, 2f, C((ItemKind.Pump, 1), (ItemKind.Plate, 1)), "주방 바닥·손에 묻는 균이 세 배 빨리 준다",
            0.8f, Skill.Mechanics, F(FaultKind.NozzleClog, FaultKind.DrainClog), w => w.Soil.Stats.TaintedMeals >= 1 ? (0.4f, $"균 든 식사 {w.Soil.Stats.TaintedMeals}끼") : Research(w, 25f)),
        new(FurnitureType.AirPurifier, "공기 청정기", RoomType.Quarters, ModuleRole.Clean, 1.5f, C((ItemKind.Filter, 2), (ItemKind.Fan, 1)), "침실의 먼지·균이 두 배 반 빨리 준다",
            0.3f, Skill.Mechanics, F(FaultKind.FilterClogged, FaultKind.FanFail), w => w.Soil.Stats.ContactCatches >= 1 || w.Crew.Count(c => c.Ailments.Any(a => a.Id is "cold" or "moldlung")) >= 2 ? (0.3f, "병이 손과 공기로 옮았다") : Research(w, 25f)),
        new(FurnitureType.Autoclave, "멸균기", RoomType.Medbay, ModuleRole.Clean, 3f, C((ItemKind.Plate, 1), (ItemKind.Thermostat, 1), (ItemKind.Gasket, 1)), "의무실의 균이 네 배 빨리 준다 (상처가 덜 곪는다)",
            0.8f, Skill.Electrical, F(FaultKind.HeatingElement, FaultKind.GasketLeak), w => w.Soil.Stats.WoundInfections >= 1 ? (0.45f, $"상처가 {w.Soil.Stats.WoundInfections}번 곪았다") : Research(w, 30f)),
        new(FurnitureType.DeconShower, "제염 샤워", RoomType.Airlock, ModuleRole.SuitDry, 1f, C((ItemKind.Pump, 1), (ItemKind.Nozzle, 1)), "에어락에서 우주복 분진과 옷의 분진을 씻어 낸다",
            0.4f, Skill.Mechanics, F(FaultKind.NozzleClog), w => w.Soil.Stats.SkippedDecons >= 2 ? (0.35f, $"털지 않고 들어온 일 {w.Soil.Stats.SkippedDecons}번") : No),
        new(FurnitureType.WashingMachine, "세탁기", RoomType.Laundry, ModuleRole.Laundry, 0.5f, C((ItemKind.Motor, 1), (ItemKind.Pump, 1)), "빨래 물이 절반",
            1f, Skill.Mechanics, F(FaultKind.BeltSlip, FaultKind.DrainClog), w => w.Soil.Stats.LaundryDeferred >= 1 ? (0.35f, "물이 모자라 빨래를 미뤘다") : w.Soil.Stats.LaundryRuns >= 4 ? (0.15f, "빨래가 잦다") : No),
        // 잠
        new(FurnitureType.BlackoutCurtain, "암막 커튼", RoomType.Quarters, ModuleRole.Sleep, 0.08f, C((ItemKind.Thread, 1), (ItemKind.Rag, 2)), "침실에서 8% 더 깊이 잔다",
            0f, Skill.Mechanics, F(FaultKind.Jam), w => w.Crew.Any(c => c.Ailments.Any(a => a.Id == "insomnia")) ? (0.35f, "잠 못 드는 사람이 있다") : Research(w, 10f)),
        new(FurnitureType.NoiseDamper, "방음재", RoomType.Quarters, ModuleRole.Sleep, 0.1f, C((ItemKind.Plate, 1), (ItemKind.Glue, 1)), "침실에서 10% 더 깊이 잔다 · 코골이가 덜 들린다",
            0f, Skill.Mechanics, F(FaultKind.GlueFail), w => w.Daily.Stats.Seen.Contains("snore") ? (0.3f, "코골이에 잠을 설쳤다") : w.Movement.Stats.Woke >= 2 ? (0.25f, $"발소리에 {w.Movement.Stats.Woke}번 깼다") : No),
        new(FurnitureType.WhiteNoise, "백색 소음기", RoomType.Quarters, ModuleRole.Sleep, 0.06f, C((ItemKind.Electronics, 1)), "침실에서 6% 더 깊이 잔다",
            0.05f, Skill.Electrical, F(FaultKind.DisplayFault), w => w.Movement.Stats.Woke >= 1 ? (0.2f, "자다 깬 일이 있다") : Research(w, 15f)),
        // 쉼
        new(FurnitureType.CoffeeMachine, "커피 머신", RoomType.Mess, ModuleRole.Relax, 0.15f, C((ItemKind.Pump, 1), (ItemKind.Thermostat, 1)), "식당에서 15% 더 풀린다",
            0.6f, Skill.Mechanics, F(FaultKind.ScaleBuildup, FaultKind.HeatingElement), w => w.Society.Morale < 0.55f ? (0.3f, $"사기 {SocietySystem.MoraleName(w.Society.Morale)}") : Research(w, 15f)),
        new(FurnitureType.Projector, "영사기", RoomType.Lounge, ModuleRole.Relax, 0.2f, C((ItemKind.Lamp, 1), (ItemKind.Electronics, 1)), "휴게실에서 20% 더 풀린다",
            0.3f, Skill.Electrical, F(FaultKind.LampBurnout, FaultKind.FanFail), w => w.Society.Morale < 0.5f ? (0.35f, $"사기 {SocietySystem.MoraleName(w.Society.Morale)}") : Research(w, 20f)),
        new(FurnitureType.GameTable, "게임 탁자", RoomType.Lounge, ModuleRole.Relax, 0.12f, C((ItemKind.Plate, 1), (ItemKind.Paint, 1)), "휴게실에서 12% 더 풀린다",
            0f, Skill.Mechanics, F(FaultKind.Jam), w => w.Society.Morale < 0.6f ? (0.2f, "다들 지쳤다") : Research(w, 10f)),
        new(FurnitureType.Bookshelf, "책장", RoomType.Lounge, ModuleRole.Relax, 0.1f, C((ItemKind.Plate, 1)), "휴게실에서 10% 더 풀린다",
            0f, Skill.Mechanics, F(FaultKind.Jam), w => w.Crew.Count(c => c.Hobbies.Contains(Hobby.Reading)) >= 2 ? (0.2f, "책 읽는 사람이 여럿") : Research(w, 10f)),
        new(FurnitureType.Aquarium, "수조", RoomType.Mess, ModuleRole.Relax, 0.12f, C((ItemKind.Plate, 1), (ItemKind.Pump, 1)), "식당에서 12% 더 풀린다",
            0.1f, Skill.Botany, F(FaultKind.FilterClogged), w => w.Life.Memorial.Count >= 1 ? (0.25f, "사람을 잃은 배") : Research(w, 30f)),
        new(FurnitureType.Treadmill, "러닝머신", RoomType.Lounge, ModuleRole.Relax, 0.1f, C((ItemKind.Motor, 1), (ItemKind.Belt, 1)), "휴게실에서 10% 더 풀린다 (운동)",
            0.5f, Skill.Mechanics, F(FaultKind.BeltSlip, FaultKind.BrushWear), w => w.Crew.Any(c => c.Ailments.Any(a => a.Id is "boneloss" or "atrophy")) ? (0.4f, "뼈와 근육이 약해진 사람") : Research(w, 20f)),
        new(FurnitureType.PlantWall, "식물 벽", RoomType.Mess, ModuleRole.Relax, 0.1f, C((ItemKind.Seed, 1), (ItemKind.Nutrient, 1), (ItemKind.Mesh, 1)), "식당에서 10% 더 풀린다",
            0.1f, Skill.Botany, F(FaultKind.NutrientImbalance), w => w.Daily.Stats.Seen.Contains("bloom") ? (0.25f, "꽃 핀 걸 다들 보러 갔다") : Research(w, 15f)),
        // 안전
        new(FurnitureType.EmergencyLight, "비상등", RoomType.Corridor, ModuleRole.Light, 1f, C((ItemKind.Lamp, 1), (ItemKind.CellPack, 1)), "정전·조명 고장에도 그 방이 어둡지 않다",
            0.05f, Skill.Electrical, F(FaultKind.LampBurnout, FaultKind.DiodeFail), w => w.Fixtures.LightFailures >= 1 || w.History.DarkHours > 0.5f ? (0.4f, "캄캄한 통로를 겪었다") : No),
        new(FurnitureType.SurgeProtector, "서지 보호기", RoomType.Power, ModuleRole.Surge, 1f, C((ItemKind.Capacitor, 1), (ItemKind.Relay, 1)), "전력 서지에 차단기 하나만 떨어지고 임시 배선이 덜 탄다",
            0.05f, Skill.Electrical, F(FaultKind.CapacitorBulge, FaultKind.RelayStuck), w => w.Hazards.Count[(int)HazardKind.PowerSurge] >= 1 ? (0.45f, "전력 서지를 겪었다") : Research(w, 30f)),
        new(FurnitureType.FireBlanket, "방화포 함", RoomType.Galley, ModuleRole.FireDamp, 0.5f, C((ItemKind.Thread, 1), (ItemKind.Plate, 1)), "그 방에서 붙는 불이 절반 세기로 시작한다",
            0f, Skill.Mechanics, F(FaultKind.Jam), w => w.History.Fires >= 1 ? (0.35f, $"불이 {ShipHistory.Times(w.History.Fires)} 났다") : No),
        new(FurnitureType.Dehumidifier, "제습기", RoomType.Hydroponics, ModuleRole.Dry, 0.3f, C((ItemKind.Motor, 1), (ItemKind.Desiccant, 1)), "습도·결로·곰팡이 사고가 그 방에서 30%로 준다",
            0.4f, Skill.Mechanics, F(FaultKind.DrainClog, FaultKind.CompressorFail), w => w.Hazards.Count[(int)HazardKind.MoldOutbreak] + w.Hazards.Count[(int)HazardKind.HumiditySpike] + w.Hazards.Count[(int)HazardKind.CondensateFlood] >= 1 ? (0.4f, "습기·곰팡이를 겪었다") : No),
        new(FurnitureType.AirlockPump, "에어락 회수 펌프", RoomType.Airlock, ModuleRole.Airlock, 0.6f, C((ItemKind.Pump, 1), (ItemKind.Valve, 1)), "감압이 빠르고 순환마다 버리는 공기가 40% 준다",
            0.8f, Skill.Mechanics, F(FaultKind.ValveStuck, FaultKind.SealWorn), w => w.AirlockCycles >= 8 ? (0.3f, $"에어락을 {w.AirlockCycles}번 돌렸다") : No),
        new(FurnitureType.SuitDryer, "우주복 건조기", RoomType.Airlock, ModuleRole.SuitDry, 1f, C((ItemKind.Fan, 1), (ItemKind.Thermostat, 1)), "에어락에 들어서면 우주복 분진이 털린다",
            0.3f, Skill.Mechanics, F(FaultKind.FanFail), w => w.Soil.Stats.SkippedDecons >= 1 ? (0.3f, "분진을 묻혀 들어왔다") : No),
        new(FurnitureType.SignalBooster, "신호 증폭기", RoomType.Comms, ModuleRole.Relax, 0.15f, C((ItemKind.Electronics, 1), (ItemKind.Fiber, 1)), "통신실에서 15% 더 풀린다 (집에서 온 소식이 또렷하다)",
            0.2f, Skill.Electrical, F(FaultKind.FiberBreak), w => w.Daily.Stats.Seen.Contains("letter") ? (0.2f, "집에서 온 소식이 끊겼다") : Research(w, 25f)),
    };

    private static readonly Dictionary<FurnitureType, Row> ByType = Rows.ToDictionary(r => r.Type);
    public static Row? Of(FurnitureType t) => ByType.TryGetValue(t, out var r) ? r : null;
    public static bool Is(FurnitureType t) => ByType.ContainsKey(t);
    public static string? Name(FurnitureType t) => Of(t)?.Name;

    public static IEnumerable<Modules.Spec> Specs => Rows.Select(r => new Modules.Spec(r.Type, r.Room, 1, r.Cost, r.Note, r.Value));
    public static IEnumerable<MachineSpec> Machines => Rows.Select(r => new MachineSpec(r.Type, r.Power, 3, 60f, r.Skill, null, 0.4f, false, r.Faults));
    public static (float, string) Need(World w, FurnitureType t) => Of(t)?.Need(w) ?? (0f, "");

    // ── 효과 (방에 일하는 모듈이 있으면) ──

    private static bool Works(Furniture f) => f.Machine is Machine m && m.Faults.Count == 0 && (m.Powered || m.Spec.PowerDraw <= 0f || ByType[f.Type].Role == ModuleRole.Light);

    private static float Sum(Room? room, Func<Row, bool> pick)
    {
        if (room == null) return 0f;
        float s = 0f;
        foreach (var f in room.Furniture)
            if (ByType.TryGetValue(f.Type, out var r) && pick(r) && Works(f)) s += r.Value;
        return s;
    }

    public static bool Has(Room? room, FurnitureType t) => room != null && room.Furniture.Any(f => f.Type == t && Works(f));

    /// <summary>전조 감지 배율 (그 방에 그 전조를 보는 감시기가 있으면).</summary>
    public static float OmenMul(Room? room, OmenKind k) => 1f + Sum(room, r => r.Role == ModuleRole.Omen && r.Omen == k);

    /// <summary>일 속도 배율 (그 방에 그 기술을 돕는 모듈 · 공구 벽은 모두).</summary>
    public static float SpeedMul(Room? room, Skill s) => 1f + Sum(room, r => r.Role == ModuleRole.Speed && (r.Boost == s || r.Boost == null));

    /// <summary>방 오염이 줄어드는 배율.</summary>
    public static float CleanMul(Room? room) => 1f + Sum(room, r => r.Role == ModuleRole.Clean);
    public static float SleepAdd(Room? room) => Sum(room, r => r.Role == ModuleRole.Sleep);
    public static float RelaxMul(Room? room) => 1f + Sum(room, r => r.Role == ModuleRole.Relax);
    public static bool Lit(Room room) => room.PortableLit > 0 || FixedLit(room); // v16.7 이동식 작업등도 비춘다
    public static bool FixedLit(Room room) => room.Furniture.Any(f => f.Type == FurnitureType.EmergencyLight && f.Machine is Machine m && m.Faults.Count == 0);
    public static bool SurgeGuard(World w) => w.Ship.FurnitureOf(FurnitureType.SurgeProtector).Any(Works);
    public static float FireMul(Room? room) => Has(room, FurnitureType.FireBlanket) ? 0.5f : 1f;
    public static float DryMul(Room? room) => Has(room, FurnitureType.Dehumidifier) ? 0.3f : 1f;
    public static bool AirlockPumped(World w) => w.Ship.FurnitureOf(FurnitureType.AirlockPump).Any(Works);
    public static bool SuitDried(Room? room) => Has(room, FurnitureType.SuitDryer) || Has(room, FurnitureType.DeconShower);
    public static float LaundryWaterMul(World w) => w.Ship.FurnitureOf(FurnitureType.WashingMachine).Any(Works) ? 0.5f : 1f;
}
