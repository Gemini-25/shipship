using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15 고장 30 → 70 · 물자 26 → 70.
// 새 고장은 새 부품을 요구한다(개스킷 · 밸브 · 팬 · 벨트 · 릴레이 · 축전기 …) — 없으면 정비실에서 그때 만든다(재고 목표 0).
// 새 소모품은 이미 있는 행동이 쓴다: 비누(손 씻기) · 세제(빨래) · 소독약(관 씻기) · 붕대(키트 없는 응급 처치) · 절연 테이프(임시 이음) · 커피·찻잎·향신료(일상).

public static class FaultsV15
{
    // (고장, 이름, 출력, 부품, 수리 시간, 전조, 걸리는 설비)
    private static readonly (FaultKind k, string name, float output, ItemKind? part, float hours, OmenKind omen, FurnitureType[] on)[] Rows =
    {
        (FaultKind.GasketLeak, "개스킷 누설", 0.7f, ItemKind.Gasket, 0.75f, OmenKind.Pressure, T(FurnitureType.CoolantPump, FurnitureType.WaterRecycler, FurnitureType.HeatExchanger, FurnitureType.Fridge)),
        (FaultKind.SealWorn, "축 씰 마모", 0.6f, ItemKind.Seal, 1f, OmenKind.Vibration, T(FurnitureType.CoolantPump, FurnitureType.AuxGenerator, FurnitureType.EngineCore)),
        (FaultKind.ValveStuck, "밸브 고착", 0.3f, ItemKind.Valve, 1f, OmenKind.Pressure, T(FurnitureType.WaterRecycler, FurnitureType.CoolantPump, FurnitureType.OxygenGenerator, FurnitureType.Scrubber)),
        (FaultKind.FanFail, "냉각 팬 고장", 0.5f, ItemKind.Fan, 0.75f, OmenKind.Heat, T(FurnitureType.MainComputer, FurnitureType.PowerPanel, FurnitureType.Battery, FurnitureType.Console, FurnitureType.Fabricator)),
        (FaultKind.BeltSlip, "벨트 미끄러짐", 0.6f, ItemKind.Belt, 0.5f, OmenKind.Vibration, T(FurnitureType.Workbench, FurnitureType.Refinery, FurnitureType.Collector, FurnitureType.MealDispenser)),
        (FaultKind.RelayStuck, "릴레이 붙음", 0.5f, ItemKind.Relay, 0.5f, OmenKind.Heat, T(FurnitureType.PowerPanel, FurnitureType.Console, FurnitureType.DroneDock, FurnitureType.RobotDock)),
        (FaultKind.CapacitorBulge, "축전기 부풂", 0.4f, ItemKind.Capacitor, 1f, OmenKind.Heat, T(FurnitureType.PowerPanel, FurnitureType.CapacitorBank, FurnitureType.MainComputer, FurnitureType.SensorArray)),
        (FaultKind.InsulatorCrack, "애자 균열", 0.6f, ItemKind.Insulator, 1f, OmenKind.Heat, T(FurnitureType.PowerPanel, FurnitureType.ReactorCore, FurnitureType.Battery)),
        (FaultKind.CouplingWear, "커플링 마모", 0.6f, ItemKind.Coupling, 1.2f, OmenKind.Vibration, T(FurnitureType.CoolantPump, FurnitureType.EngineCore, FurnitureType.AuxGenerator)),
        (FaultKind.ImpellerErosion, "임펠러 침식", 0.5f, ItemKind.Impeller, 1.5f, OmenKind.Vibration, T(FurnitureType.CoolantPump, FurnitureType.WaterRecycler)),
        (FaultKind.BrushWear, "모터 브러시 마모", 0.6f, ItemKind.Brush, 0.75f, OmenKind.Vibration, T(FurnitureType.Fridge, FurnitureType.Workbench, FurnitureType.Collector, FurnitureType.MealDispenser)),
        (FaultKind.NozzleClog, "노즐 막힘", 0.4f, ItemKind.Nozzle, 0.5f, OmenKind.Pressure, T(FurnitureType.Stove, FurnitureType.AuxGenerator, FurnitureType.GrowBed)),
        (FaultKind.MembraneTear, "막 찢어짐", 0.2f, ItemKind.Membrane, 1.5f, OmenKind.Pressure, T(FurnitureType.WaterRecycler, FurnitureType.OxygenGenerator, FurnitureType.Scrubber)),
        (FaultKind.ThermostatFault, "온도 조절기 고장", 0.6f, ItemKind.Thermostat, 0.5f, OmenKind.Drift, T(FurnitureType.Fridge, FurnitureType.Stove, FurnitureType.HeatExchanger, FurnitureType.GrowBed)),
        (FaultKind.DiodeFail, "다이오드 소손", 0.5f, ItemKind.Diode, 0.75f, OmenKind.Heat, T(FurnitureType.Battery, FurnitureType.PowerPanel, FurnitureType.DroneDock)),
        (FaultKind.HeatsinkClog, "방열판 먼지 막힘", 0.6f, null, 0.5f, OmenKind.Heat, T(FurnitureType.MainComputer, FurnitureType.PowerPanel, FurnitureType.Console, FurnitureType.Fabricator)),
        (FaultKind.SpringFatigue, "스프링 피로", 0.7f, ItemKind.Spring, 0.5f, OmenKind.Vibration, T(FurnitureType.Workbench, FurnitureType.SuitLocker, FurnitureType.MedBed)),
        (FaultKind.HoseCrack, "호스 균열", 0.6f, ItemKind.Hose, 0.75f, OmenKind.Pressure, T(FurnitureType.GrowBed, FurnitureType.WaterRecycler, FurnitureType.Stove)),
        (FaultKind.ClampLoose, "클램프 풀림", 0.8f, ItemKind.Clamp, 0.3f, OmenKind.Vibration, T(FurnitureType.CoolantPump, FurnitureType.HeatExchanger, FurnitureType.Scrubber)),
        (FaultKind.GearChip, "기어 이 빠짐", 0.4f, ItemKind.Gear, 1.5f, OmenKind.Vibration, T(FurnitureType.Collector, FurnitureType.Refinery, FurnitureType.EngineCore)),
        (FaultKind.BushingWear, "부싱 마모", 0.7f, ItemKind.Bushing, 1f, OmenKind.Vibration, T(FurnitureType.Workbench, FurnitureType.Collector, FurnitureType.Fridge)),
        (FaultKind.FiberBreak, "광섬유 끊김", 0.3f, ItemKind.Fiber, 1f, OmenKind.Drift, T(FurnitureType.MainComputer, FurnitureType.SensorArray, FurnitureType.Console)),
        (FaultKind.LampBurnout, "램프 소손", 0.5f, ItemKind.Lamp, 0.3f, OmenKind.Drift, T(FurnitureType.GrowBed, FurnitureType.MedBed, FurnitureType.Console)),
        (FaultKind.ThermocoupleDrift, "열전대 오차", 0.8f, ItemKind.Thermocouple, 0.5f, OmenKind.Drift, T(FurnitureType.ReactorCore, FurnitureType.Refinery, FurnitureType.AuxGenerator, FurnitureType.Stove)),
        (FaultKind.SolenoidFail, "솔레노이드 고장", 0.3f, ItemKind.Solenoid, 0.75f, OmenKind.Heat, T(FurnitureType.WaterRecycler, FurnitureType.OxygenGenerator, FurnitureType.SuitLocker)),
        (FaultKind.FirmwareCrash, "펌웨어 멈춤", 0f, null, 0.4f, OmenKind.Drift, T(FurnitureType.MainComputer, FurnitureType.Console, FurnitureType.DroneDock, FurnitureType.RobotDock, FurnitureType.Fabricator, FurnitureType.SensorArray)),
        (FaultKind.CalibrationLoss, "보정값 소실", 0.6f, null, 0.5f, OmenKind.Drift, T(FurnitureType.SensorArray, FurnitureType.MedBed, FurnitureType.Refinery)),
        (FaultKind.CorrosionPit, "부식 구멍", 0.7f, ItemKind.Plate, 1f, OmenKind.Pressure, T(FurnitureType.CoolantPump, FurnitureType.HeatExchanger, FurnitureType.WaterRecycler)),
        (FaultKind.ScaleBuildup, "스케일 침착", 0.6f, ItemKind.Solvent, 0.75f, OmenKind.Pressure, T(FurnitureType.HeatExchanger, FurnitureType.WaterRecycler, FurnitureType.CoolantPump, FurnitureType.Stove)),
        (FaultKind.DrainClog, "응축수 배수 막힘", 0.7f, null, 0.4f, OmenKind.Pressure, T(FurnitureType.Fridge, FurnitureType.Scrubber, FurnitureType.HeatExchanger)),
        (FaultKind.LooseTerminal, "단자 풀림", 0.6f, ItemKind.Tape, 0.3f, OmenKind.Heat, T(FurnitureType.PowerPanel, FurnitureType.Battery, FurnitureType.Console)),
        (FaultKind.ArcTracking, "아크 트래킹", 0.5f, ItemKind.Insulator, 1f, OmenKind.Heat, T(FurnitureType.PowerPanel, FurnitureType.Battery)),
        (FaultKind.GlueFail, "접착부 떨어짐", 0.8f, ItemKind.Glue, 0.3f, OmenKind.Vibration, T(FurnitureType.Console, FurnitureType.MedBed, FurnitureType.GrowBed)),
        (FaultKind.OilStarve, "윤활 부족", 0.5f, ItemKind.Lubricant, 0.5f, OmenKind.Vibration, T(FurnitureType.EngineCore, FurnitureType.CoolantPump, FurnitureType.AuxGenerator, FurnitureType.Workbench)),
        (FaultKind.AirBound, "배관 공기 막힘", 0.4f, null, 0.5f, OmenKind.Pressure, T(FurnitureType.CoolantPump, FurnitureType.WaterRecycler, FurnitureType.HeatExchanger)),
        (FaultKind.NutrientImbalance, "양액 불균형", 0.6f, ItemKind.Nutrient, 0.4f, OmenKind.Drift, T(FurnitureType.GrowBed)),
        (FaultKind.SeedTrayRot, "육묘판 부패", 0.7f, ItemKind.Seed, 0.5f, OmenKind.Pressure, T(FurnitureType.GrowBed)),
        (FaultKind.GaugeStuck, "게이지 바늘 걸림", 0.9f, null, 0.25f, OmenKind.Drift, T(FurnitureType.CoolantPump, FurnitureType.OxygenGenerator, FurnitureType.ReactorCore, FurnitureType.Battery)),
        (FaultKind.InputFault, "입력부 고장", 0.7f, ItemKind.Electronics, 0.4f, OmenKind.Drift, T(FurnitureType.Console, FurnitureType.MainComputer, FurnitureType.Fabricator)),
        (FaultKind.MountCrack, "방진 마운트 균열", 0.7f, ItemKind.Bushing, 0.75f, OmenKind.Vibration, T(FurnitureType.EngineCore, FurnitureType.AuxGenerator, FurnitureType.CoolantPump, FurnitureType.Refinery)),
    };

    private static FurnitureType[] T(params FurnitureType[] t) => t;

    public static IEnumerable<FaultSpec> Specs => Rows.Select(r => new FaultSpec(r.k, r.name, r.output, r.part, r.hours));
    public static int Count => Rows.Length;

    /// <summary>설비 사양에 새 고장을 덧붙인다.</summary>
    public static MachineSpec Extend(MachineSpec s)
    {
        var extra = Rows.Where(r => r.on.Contains(s.Type)).Select(r => r.k).ToArray();
        return extra.Length == 0 ? s : s with { FaultKinds = s.FaultKinds.Concat(extra).ToArray() };
    }

    public static OmenKind? Omen(FaultKind k) => Rows.Where(r => r.k == k).Select(r => (OmenKind?)r.omen).FirstOrDefault();
}

public static class ItemsV15
{
    // (물자, 이름, 등급)
    private static readonly (ItemKind k, string name, ItemTier tier)[] Rows =
    {
        // 부품 24
        (ItemKind.Gasket, "개스킷", ItemTier.General), (ItemKind.Seal, "축 씰", ItemTier.General), (ItemKind.Valve, "밸브", ItemTier.General),
        (ItemKind.Fan, "냉각 팬", ItemTier.General), (ItemKind.Belt, "구동 벨트", ItemTier.General), (ItemKind.Relay, "릴레이", ItemTier.General),
        (ItemKind.Capacitor, "축전기", ItemTier.General), (ItemKind.Insulator, "애자", ItemTier.General), (ItemKind.Coupling, "커플링", ItemTier.General),
        (ItemKind.Impeller, "임펠러", ItemTier.General), (ItemKind.Brush, "모터 브러시", ItemTier.General), (ItemKind.Nozzle, "노즐", ItemTier.General),
        (ItemKind.Membrane, "여과막", ItemTier.General), (ItemKind.Thermostat, "온도 조절기", ItemTier.General), (ItemKind.Diode, "다이오드", ItemTier.General),
        (ItemKind.Spring, "스프링", ItemTier.General), (ItemKind.Hose, "호스", ItemTier.General), (ItemKind.Clamp, "클램프", ItemTier.General),
        (ItemKind.Gear, "기어", ItemTier.General), (ItemKind.Bushing, "부싱", ItemTier.General), (ItemKind.Fiber, "광섬유", ItemTier.General),
        (ItemKind.Lamp, "램프", ItemTier.General), (ItemKind.Thermocouple, "열전대", ItemTier.General), (ItemKind.Solenoid, "솔레노이드", ItemTier.General),
        // 소모품 20
        (ItemKind.Solvent, "세척제", ItemTier.Supply), (ItemKind.Tape, "절연 테이프", ItemTier.Supply), (ItemKind.Glue, "접착제", ItemTier.Supply),
        (ItemKind.Nutrient, "양액", ItemTier.Supply), (ItemKind.Seed, "종자", ItemTier.Supply), (ItemKind.Soap, "비누", ItemTier.Supply),
        (ItemKind.Detergent, "세제", ItemTier.Supply), (ItemKind.Disinfectant, "소독약", ItemTier.Supply), (ItemKind.Bandage, "붕대", ItemTier.Supply),
        (ItemKind.Coffee, "커피", ItemTier.Supply), (ItemKind.TeaLeaf, "찻잎", ItemTier.Supply), (ItemKind.Spice, "향신료", ItemTier.Supply),
        (ItemKind.Vitamin, "비타민", ItemTier.Supply), (ItemKind.Gloves, "작업 장갑", ItemTier.Supply), (ItemKind.Rag, "걸레", ItemTier.Supply),
        (ItemKind.CellPack, "예비 셀", ItemTier.Supply), (ItemKind.Thread, "실", ItemTier.Supply), (ItemKind.Paint, "페인트", ItemTier.Supply),
        (ItemKind.Desiccant, "제습제", ItemTier.Supply), (ItemKind.Mesh, "철망", ItemTier.Supply),
        // 의료 1차
        (ItemKind.Painkiller, "진통제", ItemTier.Supply), (ItemKind.Antibiotic, "항생제", ItemTier.Supply), (ItemKind.Anesthetic, "마취제", ItemTier.Supply),
        (ItemKind.BloodSubstitute, "혈액 대용제", ItemTier.Supply), (ItemKind.MedHerb, "약초", ItemTier.Supply),
    };

    public static string? Name(ItemKind k) => Rows.Where(r => r.k == k).Select(r => r.name).FirstOrDefault();
    public static ItemTier Tier(ItemKind k) => Rows.Where(r => r.k == k).Select(r => r.tier).DefaultIfEmpty(ItemTier.Supply).First();
    public static int Count => Rows.Length;
    public static IEnumerable<ItemKind> Kinds => Rows.Select(r => r.k);

    private static (ItemKind, int)[] In(params (ItemKind, int)[] x) => x;

    /// <summary>만드는 법: 부품은 목표 0 (수리에 필요할 때만 만든다) · 자주 쓰는 소모품만 조금 쌓아 둔다.</summary>
    public static readonly Recipe[] Recipes =
    {
        new(ItemKind.Gasket, In((ItemKind.Carbon, 1)), 0.8f, Skill.Mechanics, Station.Workbench, 0),
        new(ItemKind.Seal, In((ItemKind.Carbon, 1), (ItemKind.Plate, 1)), 1f, Skill.Mechanics, Station.Workbench, 0),
        new(ItemKind.Valve, In((ItemKind.Plate, 1)), 1.5f, Skill.Mechanics, Station.Workbench, 0),
        new(ItemKind.Fan, In((ItemKind.Plate, 1), (ItemKind.Cable, 1)), 1.2f, Skill.Electrical, Station.Workbench, 0),
        new(ItemKind.Belt, In((ItemKind.Carbon, 1)), 0.6f, Skill.Mechanics, Station.Workbench, 0),
        new(ItemKind.Relay, In((ItemKind.Electronics, 1)), 0.8f, Skill.Electrical, Station.Workbench, 0),
        new(ItemKind.Capacitor, In((ItemKind.Electronics, 1)), 0.8f, Skill.Electrical, Station.Workbench, 0),
        new(ItemKind.Insulator, In((ItemKind.Silicate, 1)), 0.8f, Skill.Electrical, Station.Refinery, 0),
        new(ItemKind.Coupling, In((ItemKind.Plate, 1)), 1.2f, Skill.Mechanics, Station.Workbench, 0),
        new(ItemKind.Impeller, In((ItemKind.Plate, 2)), 2f, Skill.Mechanics, Station.Workbench, 0),
        new(ItemKind.Brush, In((ItemKind.Carbon, 1)), 0.5f, Skill.Electrical, Station.Workbench, 0),
        new(ItemKind.Nozzle, In((ItemKind.Plate, 1)), 0.8f, Skill.Mechanics, Station.Workbench, 0),
        new(ItemKind.Membrane, In((ItemKind.Carbon, 1), (ItemKind.Silicate, 1)), 1.5f, Skill.Mechanics, Station.Refinery, 0),
        new(ItemKind.Thermostat, In((ItemKind.Electronics, 1)), 1f, Skill.Electrical, Station.Workbench, 0),
        new(ItemKind.Diode, In((ItemKind.Silicate, 1)), 0.5f, Skill.Electrical, Station.Refinery, 0, Yield: 2),
        new(ItemKind.Spring, In((ItemKind.MetalOre, 1)), 0.5f, Skill.Mechanics, Station.Refinery, 0, Yield: 2),
        new(ItemKind.Hose, In((ItemKind.Carbon, 1)), 0.6f, Skill.Mechanics, Station.Workbench, 0),
        new(ItemKind.Clamp, In((ItemKind.MetalOre, 1)), 0.4f, Skill.Mechanics, Station.Refinery, 0, Yield: 2),
        new(ItemKind.Gear, In((ItemKind.Plate, 1)), 1.5f, Skill.Mechanics, Station.Workbench, 0),
        new(ItemKind.Bushing, In((ItemKind.MetalOre, 1)), 0.6f, Skill.Mechanics, Station.Refinery, 0),
        new(ItemKind.Fiber, In((ItemKind.Silicate, 1)), 1f, Skill.Electrical, Station.Refinery, 0),
        new(ItemKind.Lamp, In((ItemKind.Electronics, 1)), 0.6f, Skill.Electrical, Station.Workbench, 0),
        new(ItemKind.Thermocouple, In((ItemKind.Cable, 1)), 0.6f, Skill.Electrical, Station.Workbench, 0),
        new(ItemKind.Solenoid, In((ItemKind.Cable, 1), (ItemKind.Plate, 1)), 1f, Skill.Electrical, Station.Workbench, 0),
        new(ItemKind.Solvent, In((ItemKind.Carbon, 1)), 0.5f, Skill.Mechanics, Station.Refinery, 0),
        new(ItemKind.Tape, In((ItemKind.Carbon, 1)), 0.4f, Skill.Electrical, Station.Refinery, 2, Yield: 2),
        new(ItemKind.Glue, In((ItemKind.Carbon, 1)), 0.4f, Skill.Mechanics, Station.Refinery, 0),
        new(ItemKind.Nutrient, In((ItemKind.Produce, 2)), 0.6f, Skill.Botany, Station.Workbench, 0),
        new(ItemKind.Seed, In((ItemKind.Produce, 2)), 0.6f, Skill.Botany, Station.Workbench, 0),
        new(ItemKind.Soap, In((ItemKind.Carbon, 1)), 0.5f, Skill.Mechanics, Station.Refinery, 2, Yield: 2),
        new(ItemKind.Detergent, In((ItemKind.Carbon, 1)), 0.5f, Skill.Mechanics, Station.Refinery, 1),
        new(ItemKind.Disinfectant, In((ItemKind.Carbon, 1), (ItemKind.Ice, 1)), 0.6f, Skill.Mechanics, Station.Refinery, 1),
        new(ItemKind.Bandage, In((ItemKind.Produce, 2)), 0.5f, Skill.Medicine, Station.Workbench, 0, Yield: 2),
        new(ItemKind.Rag, In((ItemKind.Produce, 1)), 0.3f, Skill.Mechanics, Station.Workbench, 0, Yield: 2),
        new(ItemKind.Mesh, In((ItemKind.MetalOre, 1)), 0.5f, Skill.Mechanics, Station.Refinery, 0),
        // 의료 1차 — 재배실 채소에서 약초를 고르고 · 약초로 진통제 · 항생제 · 원료로 마취제 · 혈액 대용제 (목표는 PharmacySystem.Want가 올린다)
        new(ItemKind.MedHerb, In((ItemKind.Produce, 2)), 0.5f, Skill.Botany, Station.Workbench, 0, Yield: 2),
        new(ItemKind.Painkiller, In((ItemKind.MedHerb, 1)), 0.6f, Skill.Medicine, Station.Workbench, 0, MinSkill: 0.2f, Yield: 2),
        new(ItemKind.Antibiotic, In((ItemKind.MedHerb, 2), (ItemKind.Carbon, 1)), 1.2f, Skill.Medicine, Station.Workbench, 0, MinSkill: 0.35f),
        new(ItemKind.Anesthetic, In((ItemKind.Carbon, 1), (ItemKind.Ice, 1)), 1f, Skill.Medicine, Station.Refinery, 0, MinSkill: 0.3f),
        new(ItemKind.BloodSubstitute, In((ItemKind.Ice, 2), (ItemKind.Silicate, 1)), 1f, Skill.Medicine, Station.Refinery, 0, MinSkill: 0.25f),
    };

    /// <summary>처음 싣는 양 (빈 선반 자리만큼 — 모자라면 그때 만든다).</summary>
    public static void Stock(Ship ship)
    {
        var shelves = ship.RoomsOf(RoomType.Storage).Concat(ship.RoomsOf(RoomType.Workshop)).SelectMany(r => r.Furniture)
            .Where(f => f.Type == FurnitureType.Shelf && f.Storage != null).ToList();
        (ItemKind k, int n)[] start =
        {
            (ItemKind.Soap, 6), (ItemKind.Detergent, 3), (ItemKind.Disinfectant, 3), (ItemKind.Bandage, 6), (ItemKind.Tape, 4),
            (ItemKind.Coffee, 6), (ItemKind.TeaLeaf, 4), (ItemKind.Spice, 3), (ItemKind.Gasket, 2), (ItemKind.Seal, 1), (ItemKind.Valve, 1),
            (ItemKind.Fan, 1), (ItemKind.Relay, 1), (ItemKind.Lamp, 2), (ItemKind.Hose, 1), (ItemKind.Clamp, 2), (ItemKind.Nutrient, 2),
            (ItemKind.Seed, 3), (ItemKind.Vitamin, 4), (ItemKind.Gloves, 4), (ItemKind.Rag, 4),
        };
        foreach (var (k, n) in start)
        {
            int left = n;
            foreach (var sh in shelves)
            {
                if (left <= 0) break;
                if (!sh.Storage!.Accepts(k)) continue;
                left -= sh.Storage.Add(k, left);
            }
        }
    }

    /// <summary>배 어디서든 하나 꺼내 쓴다 (없으면 false).</summary>
    public static bool Use(World w, ItemKind k)
    {
        foreach (var f in w.Ship.Furniture)
            if (f.Storage != null && !f.Room.Detached && !f.Stowed && f.Storage.Count(k) > 0) { f.Storage.Take(k, 1); return true; }
        return false;
    }
}
