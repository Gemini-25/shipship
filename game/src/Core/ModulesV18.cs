using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 압축-마 설비 75 → 105: 새 시스템(조리 · 냄새 · 고양이 · 화분 · 바구미 · 배수 · 쓰레기 · 무중력 · 기동 · 컴퓨터 · 블랙박스 · 소리 · 회의 · 기억 · 습도 · 열)을 쓰는 설비 30.
// 한 줄 = 설비 하나: 이름 · 다는 방 · 처음부터 있는 방(특수 방) · 값 · 재료 · 설명 · 전기 · 솜씨 · 고장 · 언제 달고 싶어지나 · 쓰는 일(사람이 와서 쓴다) · 쉼.
// 효과는 FittingSystem 이 30분마다 그 시스템의 상태를 직접 바꾼다 (배율만이 아니라: 화분을 묶고 · 걸쇠를 걸고 · 찌꺼기를 걷고 · 고양이를 달랜다).
// 기술(TechWebV18)이 설비 단계를 올린다 — 단계마다 효과가 커지고 모양이 바뀐다.

public static class ModulesV18
{
    public sealed record Row(FurnitureType Type, string Name, RoomType Room, RoomType[] Sig, float Value, (ItemKind kind, int count)[] Cost, string Note,
        float Power, Skill Skill, FaultKind[] Faults, Func<World, (float, string)> Need, string Use = "", float UseMin = 0f, float Relax = 0f);

    private static (ItemKind, int)[] C(params (ItemKind, int)[] x) => x;
    private static FaultKind[] F(params FaultKind[] x) => x;
    private static RoomType[] S(params RoomType[] x) => x;
    private static (float, string) No => (0f, "");
    private static (float, string) Research(World w, float at) => w.Research >= at ? (0.1f, $"연구 {w.Research:0}점") : No;
    private static int Haz(World w, HazardKind k) => w.Hazards.Count[(int)k];
    private static (float, string) Seen(World w, HazardKind k, float need) => Haz(w, k) > 0 ? (need, $"{Hazards.Name(k)} {Haz(w, k)}번") : No;
    private static (float, string) Or((float, string) a, (float, string) b) => a.Item1 > 0f ? a : b;

    public static readonly Row[] Rows =
    {
        // 조리
        new(FurnitureType.Fermenter, "발효 항아리", RoomType.Galley, S(RoomType.Freezer), 0.25f, C((ItemKind.Plate, 1), (ItemKind.Gasket, 1)), "남은 채소를 절여 덜 버린다 · 날마다 김을 빼 줘야 한다",
            0f, Skill.Cooking, F(FaultKind.SealWorn, FaultKind.CorrosionPit), w => Or(w.Crew.Sum(c => c.Stats.MealsCooked) >= 25 ? (0.2f, "끼니를 많이 지었다") : No, Research(w, 15f)), "항아리 김 빼기", 10f),
        new(FurnitureType.BreadOven, "빵 화덕", RoomType.Galley, S(), 0.2f, C((ItemKind.Plate, 3), (ItemKind.Thermostat, 1)), "아침마다 빵 굽는 냄새가 배를 채운다",
            1.2f, Skill.Cooking, F(FaultKind.HeatingElement, FaultKind.ThermostatFault), w => Or(w.Smells.Stats.Appetite >= 3 ? (0.2f, "냄새에 끌려 모인 일이 잦다") : No, Research(w, 18f)), "빵 굽기", 40f, 0.05f),
        new(FurnitureType.SpiceRack, "양념 선반", RoomType.Mess, S(), 0.1f, C((ItemKind.Plate, 1)), "끼니에 맛이 돈다 (식당에서 쉬는 맛)",
            0f, Skill.Cooking, F(FaultKind.MountCrack), w => Or(w.Crew.Count(c => !c.Dead) >= 6 && w.Tick > SimTime.TicksPerDay * 2 ? (0.15f, "같은 맛에 질렸다") : No, Research(w, 12f)), "양념 고르기", 5f, 0.1f),
        new(FurnitureType.IceMaker, "제빙기", RoomType.Medbay, S(RoomType.Hyperbaric), 0.3f, C((ItemKind.Pump, 1), (ItemKind.Thermostat, 1)), "데고 다친 곳을 식히고 더운 방을 식힌다",
            0.6f, Skill.Electrical, F(FaultKind.CompressorFail, FaultKind.ScaleBuildup), w => Or(Seen(w, HazardKind.HeatExhaustion, 0.4f), Seen(w, HazardKind.ScaldSpill, 0.3f)), "얼음 찜질", 10f),
        // 생태
        new(FurnitureType.PlantRack, "화분 선반", RoomType.Lounge, S(RoomType.Meditation), 0.2f, C((ItemKind.Plate, 2)), "화분을 묶어 두고 볕을 고르게 받는다 (흔들려도 안 떨어진다)",
            0.1f, Skill.Botany, F(FaultKind.MountCrack, FaultKind.LampBurnout), w => Or(Seen(w, HazardKind.PlantTopple, 0.45f), w.Eco.Stats.Wilted >= 1 ? (0.25f, "화분이 시들었다") : No), "화분 물 주기", 10f, 0.05f),
        new(FurnitureType.CatTower, "고양이 탑", RoomType.Lounge, S(), 0.3f, C((ItemKind.Plate, 1), (ItemKind.Fiber, 1)), "고양이가 높은 데서 쉬며 겁이 빨리 가라앉는다",
            0f, Skill.Mechanics, F(FaultKind.SpringFatigue, FaultKind.GlueFail), w => w.Eco.Cat is { Alive: true } ? Or(Seen(w, HazardKind.CatScratch, 0.45f), w.Eco.Stats.Hid >= 1 ? (0.3f, $"고양이가 {w.Eco.Stats.Hid}번 숨었다") : (0.08f, "고양이가 있다")) : No, "고양이와 놀아 주기", 15f, 0.1f),
        new(FurnitureType.PestTrap, "벌레 덫", RoomType.Storage, S(RoomType.SeedVault), 0.3f, C((ItemKind.Sensor, 1), (ItemKind.Lamp, 1)), "바구미를 꾀어 잡고 일찍 알린다",
            0.1f, Skill.Botany, F(FaultKind.LampBurnout), w => Or(Seen(w, HazardKind.WeevilSwarm, 0.5f), w.Eco.Stats.Infest >= 1 ? (0.35f, $"바구미 {w.Eco.Stats.Infest}번") : No), "덫 갈기", 10f),
        new(FurnitureType.InsectFarm, "귀뚜라미 사육장", RoomType.Hydroponics, S(RoomType.ProteinFarm), 0.2f, C((ItemKind.Plate, 2), (ItemKind.Fan, 1)), "음식물 찌꺼기를 먹여 단백질을 얻는다 (쓰레기통이 덜 찬다)",
            0.2f, Skill.Botany, F(FaultKind.FanFail, FaultKind.NozzleClog), w => Or(w.Drains.Stats.Overflows >= 1 ? (0.3f, "쓰레기통이 넘쳤다") : No, Evolution.StarvedHours(w) > 2f ? (0.3f, "굶주린 적이 있다") : Research(w, 25f)), "사육장 먹이 주기", 15f),
        // 배수 · 재활용
        new(FurnitureType.GreaseTrap, "기름 거름통", RoomType.Galley, S(), 0.6f, C((ItemKind.Plate, 2), (ItemKind.Gasket, 1)), "개수대 배수가 덜 막힌다",
            0f, Skill.Mechanics, F(FaultKind.DrainClog, FaultKind.GasketLeak), w => Or(Seen(w, HazardKind.DrainBackflow, 0.5f), w.Drains.Stats.Backflows + w.Drains.Stats.Slow >= 2 ? (0.35f, "배수구가 자주 막힌다") : No), "기름통 비우기", 15f),
        new(FurnitureType.Compactor, "쓰레기 압축기", RoomType.Storage, S(RoomType.Recycling, RoomType.Crusher), 0.4f, C((ItemKind.Motor, 1), (ItemKind.Plate, 2)), "쓰레기를 눌러 통이 덜 넘치고 고철이 모인다",
            1.5f, Skill.Mechanics, F(FaultKind.GearChip, FaultKind.SolenoidFail, FaultKind.Jam), w => w.Drains.Stats.Overflows >= 1 ? (0.35f, $"쓰레기통이 {w.Drains.Stats.Overflows}번 넘쳤다") : Research(w, 30f), "압축기 돌리기", 10f),
        new(FurnitureType.Composter, "퇴비 통", RoomType.Hydroponics, S(RoomType.Garden, RoomType.MushroomFarm), 0.3f, C((ItemKind.Plate, 2)), "음식물 찌꺼기를 퇴비로 · 작물과 화분이 튼튼해진다",
            0.1f, Skill.Botany, F(FaultKind.GearChip, FaultKind.CorrosionPit), w => Or(w.Drains.Stats.Sorted >= 3 ? (0.25f, "찌꺼기를 따로 모은다") : No, Research(w, 22f)), "퇴비 뒤집기", 20f),
        new(FurnitureType.GreywaterFilter, "회색수 거름기", RoomType.LifeSupport, S(RoomType.WaterPlant, RoomType.Laundry), 0.4f, C((ItemKind.Filter, 2), (ItemKind.Pump, 1)), "개수대 · 세탁 물을 걸러 다시 쓴다 (배수가 덜 막히고 물이 맑다)",
            0.4f, Skill.Mechanics, F(FaultKind.FilterClogged, FaultKind.MembraneTear), w => Or(Seen(w, HazardKind.GreywaterJam, 0.5f), w.Flow.WaterQuality < 0.8f ? (0.3f, "물이 탁했다") : Research(w, 28f)), "거름층 털기", 15f),
        // 무중력 · 기동
        new(FurnitureType.GrabRail, "손잡이 줄", RoomType.Airlock, S(RoomType.Centrifuge, RoomType.EvaPrep), 0.5f, C((ItemKind.Plate, 1), (ItemKind.Clamp, 1)), "무게가 없어도 붙잡고 다닌다 (떠다니는 것이 덜 흩어지고 멀미가 덜하다)",
            0f, Skill.Mechanics, F(FaultKind.ClampLoose), w => w.ZeroG.Stats.Episodes >= 1 ? (0.45f, $"무게가 {w.ZeroG.Stats.Episodes}번 사라졌다") : Research(w, 20f)),
        new(FurnitureType.CargoNet, "화물 그물", RoomType.Storage, S(RoomType.Cargo, RoomType.ShuttleBay), 1f, C((ItemKind.Fiber, 2), (ItemKind.Clamp, 1)), "기동할 때 선반 짐이 쏟아지지 않는다",
            0f, Skill.Mechanics, F(FaultKind.ClampLoose, FaultKind.SpringFatigue), w => w.Maneuver.Stats.Dropped >= 2 ? (0.4f, $"짐이 {w.Maneuver.Stats.Dropped}번 쏟아졌다") : Research(w, 18f), "그물 조이기", 10f),
        new(FurnitureType.CrashSeat, "충격 좌석", RoomType.Bridge, S(RoomType.BackupBridge, RoomType.ShuttleBay), 0.5f, C((ItemKind.Plate, 2), (ItemKind.Fiber, 1)), "흔들려도 묶인 사람은 넘어지지 않는다",
            0f, Skill.Mechanics, F(FaultKind.BeltSlip, FaultKind.SpringFatigue), w => w.Maneuver.Stats.Fell >= 1 ? (0.4f, $"흔들림에 {w.Maneuver.Stats.Fell}명이 넘어졌다") : Research(w, 25f)),
        new(FurnitureType.MagBootRack, "자석 신발 걸이", RoomType.Airlock, S(RoomType.EvaPrep, RoomType.DockingBay), 0.4f, C((ItemKind.Electronics, 1), (ItemKind.Plate, 1)), "무게가 없어도 바닥을 딛는다 (멀미 · 표류가 준다)",
            0.2f, Skill.Electrical, F(FaultKind.ShortCircuit, FaultKind.LooseTerminal), w => w.ZeroG.Stats.Vomits + w.ZeroG.Stats.Hurt >= 1 ? (0.4f, "무중력에 탈이 났다") : Research(w, 30f), "자석 신발 충전", 5f),
        // 컴퓨터 · 기록 · 소리
        new(FurnitureType.ServerRack, "서버 선반", RoomType.Comms, S(RoomType.ServerRoom, RoomType.ComputerRoom), 0.3f, C((ItemKind.Electronics, 3), (ItemKind.Fan, 1)), "주컴퓨터 일을 나눠 맡는다 (다시 켜질 때 덜 엉킨다 · 연구가 조금 붙는다)",
            1.5f, Skill.Electrical, F(FaultKind.FirmwareCrash, FaultKind.FanFail), w => Or(Seen(w, HazardKind.RebootGlitch, 0.45f), Or(Seen(w, HazardKind.ComputerFault, 0.3f), Research(w, 30f)))),
        new(FurnitureType.RecorderVault, "기록 금고", RoomType.Bridge, S(RoomType.Security, RoomType.BackupBridge), 1f, C((ItemKind.Plate, 3), (ItemKind.Electronics, 1)), "블랙박스 사본을 따로 둔다 (지워진 자리가 드러난다)",
            0.2f, Skill.Electrical, F(FaultKind.DisplayFault, FaultKind.FiberBreak), w => Or(Seen(w, HazardKind.BlackboxGap, 0.5f), w.Blackbox.Stats.Wipes + w.Blackbox.Stats.Damaged >= 1 ? (0.4f, "기록이 빈 적이 있다") : No)),
        new(FurnitureType.ListeningPost, "청음기", RoomType.Engine, S(), 0.5f, C((ItemKind.Sensor, 1), (ItemKind.Cable, 1)), "도는 설비의 소리를 들어 베어링을 일찍 손본다",
            0.1f, Skill.Mechanics, F(FaultKind.SensorDrift), w => Or(Seen(w, HazardKind.BearingWhine, 0.45f), Research(w, 20f)), "소리 들어 보기", 10f),
        // 사람
        new(FurnitureType.MeetingBoard, "회의 칠판", RoomType.Mess, S(RoomType.MeetingRoom, RoomType.School), 0.4f, C((ItemKind.Plate, 1)), "할 말을 적어 두면 회의가 덜 날카롭다",
            0f, Skill.Mechanics, F(FaultKind.MountCrack), w => Or(Seen(w, HazardKind.MeetingBrawl, 0.5f), Research(w, 15f)), "칠판에 적기", 10f, 0.05f),
        new(FurnitureType.MemorialWall, "추모 벽", RoomType.Lounge, S(RoomType.Chapel, RoomType.Morgue), 0.3f, C((ItemKind.Plate, 1)), "떠난 사람의 이름 앞에서 마음이 조금 풀린다",
            0f, Skill.Mechanics, F(FaultKind.MountCrack), w => w.Crew.Any(c => c.Dead) ? (0.5f, "떠난 사람이 있다") : No, "추모 벽 앞에 서기", 15f),
        new(FurnitureType.MusicCorner, "악기 자리", RoomType.Lounge, S(RoomType.Theater), 0.15f, C((ItemKind.Electronics, 1), (ItemKind.Plate, 1)), "누가 치면 다들 쉰다 — 밤에는 옆방 잠을 깨운다",
            0.1f, Skill.Electrical, F(FaultKind.InputFault, FaultKind.LooseTerminal), w => Or(w.Crew.Where(c => !c.Dead).DefaultIfEmpty().Average(c => c?.Needs.Stress ?? 0f) > 0.4f ? (0.25f, "다들 지쳤다") : No, Research(w, 20f)), "악기 연주", 30f, 0.15f),
        new(FurnitureType.LabStill, "증류기", RoomType.Medbay, S(RoomType.Lab), 0.3f, C((ItemKind.Plate, 1), (ItemKind.Hose, 1), (ItemKind.Thermostat, 1)), "소독용 알코올을 내린다 (의무실 균이 준다) — 불 곁에 두면 위험하다",
            0.5f, Skill.Electrical, F(FaultKind.HeatingElement, FaultKind.HoseCrack), w => Or(w.Soil.Stats.WoundInfections >= 1 ? (0.35f, "상처가 곪았다") : No, Research(w, 26f)), "증류하기", 30f),
        new(FurnitureType.ClothesRack, "빨래 건조대", RoomType.Quarters, S(RoomType.Laundry), 0.3f, C((ItemKind.Plate, 1)), "옷을 널어 말린다 (정전기가 덜하다) — 방이 눅눅해진다",
            0f, Skill.Mechanics, F(FaultKind.MountCrack), w => Or(Seen(w, HazardKind.StaticZap, 0.4f), w.Soil.Stats.LaundryRuns >= 2 ? (0.2f, "빨래가 쌓인다") : No), "빨래 널기", 10f),
        new(FurnitureType.SewingMachine, "재봉틀", RoomType.Workshop, S(), 0.3f, C((ItemKind.Motor, 1), (ItemKind.Belt, 1)), "해진 옷과 물건을 꿰맨다",
            0.3f, Skill.Mechanics, F(FaultKind.BeltSlip, FaultKind.Jam), w => Or(w.Belongings.All.Any(b => !b.Usable) ? (0.3f, "망가진 물건이 있다") : No, Research(w, 18f)), "바느질", 30f),
        new(FurnitureType.EyeWash, "눈 세척대", RoomType.Workshop, S(RoomType.ElectronicsLab, RoomType.WeldingShop), 0.5f, C((ItemKind.Pump, 1), (ItemKind.Nozzle, 1)), "튄 것을 바로 씻어 다친 곳이 덜 커진다",
            0f, Skill.Mechanics, F(FaultKind.NozzleClog), w => Or(Seen(w, HazardKind.PumpShock, 0.4f), Or(Seen(w, HazardKind.StaticZap, 0.3f), Research(w, 20f)))),
        new(FurnitureType.OxygenMaskBox, "산소 마스크 함", RoomType.Quarters, S(RoomType.Shelter, RoomType.EscapeBay), 0.5f, C((ItemKind.Hose, 1), (ItemKind.Filter, 1)), "연기 · 공기가 나쁠 때 바로 쓴다",
            0f, Skill.Mechanics, F(FaultKind.HoseCrack), w => w.History.Fires >= 1 ? (0.35f, $"불이 {ShipHistory.Times(w.History.Fires)} 났다") : Research(w, 22f)),
        new(FurnitureType.HeatSuitRack, "방열복 걸이", RoomType.Reactor, S(RoomType.HeatStorage), 0.6f, C((ItemKind.Fiber, 2), (ItemKind.Plate, 1)), "더운 방에서 방열복을 입고 일한다 (열탈진이 준다)",
            0f, Skill.Mechanics, F(FaultKind.MountCrack), w => Or(Seen(w, HazardKind.HeatExhaustion, 0.5f), Research(w, 24f))),
        new(FurnitureType.DockClampPanel, "접안 고리 제어반", RoomType.Airlock, S(RoomType.DockingBay), 0.5f, C((ItemKind.Electronics, 2), (ItemKind.Sensor, 1)), "접안 고리 씰을 하나씩 확인한다 (기밀 실패가 덜 크다)",
            0.3f, Skill.Electrical, F(FaultKind.SolenoidFail, FaultKind.DisplayFault), w => Or(Seen(w, HazardKind.DockSealFail, 0.5f), Research(w, 32f))),
        new(FurnitureType.Telescope, "망원경", RoomType.Lounge, S(RoomType.Observatory), 0.1f, C((ItemKind.Sensor, 1), (ItemKind.Plate, 1)), "별을 보며 쉰다 — 먼 하늘의 낌새를 먼저 본다",
            0.05f, Skill.Electrical, F(FaultKind.SensorDrift, FaultKind.GearChip), w => Research(w, 15f), "별 보기", 25f, 0.1f),
    };

    private static readonly Dictionary<FurnitureType, Row> ByType = Rows.ToDictionary(r => r.Type);
    public static Row? Of(FurnitureType t) => ByType.TryGetValue(t, out var r) ? r : null;
    public static bool Is(FurnitureType t) => ByType.ContainsKey(t);
    public static string? Name(FurnitureType t) => Of(t)?.Name;

    public static IEnumerable<Modules.Spec> Specs => Rows.Select(r => new Modules.Spec(r.Type, r.Room, 1, r.Cost, r.Note, r.Value));
    public static IEnumerable<MachineSpec> Machines => Rows.Select(r => new MachineSpec(r.Type, r.Power, 3, 70f, r.Skill, null, 0.4f, false, r.Faults));

    /// <summary>달고 싶어지는 정도 — 겪은 일 · 연구, 그리고 그 설비를 다루는 기술을 익혔으면.</summary>
    public static (float, string) Need(World w, FurnitureType t)
    {
        var r = Of(t);
        if (r == null) return (0f, "");
        var (n, why) = r.Need(w);
        if (TechWebV18.TechFor(w, t) is EraTech tech && n < 0.35f) return (0.35f, $"{tech.Name} 설계를 익혔다");
        return (n, why);
    }

    /// <summary>특수 방에 처음부터 다는 설비.</summary>
    public static FurnitureType[] Signature(RoomType k)
    {
        List<FurnitureType>? l = null;
        foreach (var r in Rows) if (Array.IndexOf(r.Sig, k) >= 0) (l ??= new()).Add(r.Type);
        return l?.ToArray() ?? Array.Empty<FurnitureType>();
    }

    public static bool Works(Furniture f) => !f.Stowed && f.Machine is Machine m && m.Faults.Count == 0 && (m.Powered || m.Spec.PowerDraw <= 0f) && !f.Room.Abandoned;
    /// <summary>단계만큼 커진 값 (I 1 · II 1.3 · III 1.6 · IV 1.9).</summary>
    public static float Power(Furniture f) => (Of(f.Type)?.Value ?? 0f) * (1f + 0.3f * ((f.Machine?.Tier ?? 1) - 1));

    public static float RelaxAdd(Room? room)
    {
        if (room == null) return 0f;
        float s = 0f;
        foreach (var f in room.Furniture) if (ByType.TryGetValue(f.Type, out var r) && r.Relax > 0f && Works(f)) s += r.Relax;
        return s;
    }

    /// <summary>청음기: 그 방 설비의 진동 전조를 더 잘 듣는다 (v15 감시기 배율에 더한다).</summary>
    public static float OmenAdd(Room? room, OmenKind k)
    {
        if (room == null || k != OmenKind.Vibration) return 0f;
        foreach (var f in room.Furniture) if (f.Type == FurnitureType.ListeningPost && Works(f)) return Power(f);
        return 0f;
    }
}

// ═══════════════════════════════ 설비가 하는 일 ═══════════════════════════════

public sealed class FittingStats
{
    public int Burps, Bakes, Uses, Tied, Latched, Calmed, Trapped, Eaten, Scrap, Righted, Gripped, VaultFinds, Soothed, Woken, Masks, Washes, TierUps, Distilled;
    public string Line() => $"김 빼기 {Burps} · 빵 {Bakes} · 씀 {Uses} · 화분 묶음 {Tied} · 걸쇠 {Latched} · 고양이 달램 {Calmed} · 덫 {Trapped} · 찌꺼기 {Eaten} · 고철 {Scrap} · 바로 일어섬 {Righted} · 손잡이 {Gripped} · 금고 {VaultFinds} · 마음 풂 {Soothed} · 깨움 {Woken} · 마스크 {Masks} · 씻음 {Washes} · 단계 {TierUps} · 증류 {Distilled}";
}

public sealed class FittingSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6311 + 4111));
    public FittingSystem(World w) => _w = w;
    public static bool Off;

    public FittingStats Stats { get; } = new();
    /// <summary>발효 항아리 김 (가구 id → 0~1, 날마다 0.7씩 찬다).</summary>
    public SortedDictionary<int, float> Crock { get; } = new();
    /// <summary>마지막으로 쓴 때 (가구 id → 틱).</summary>
    public SortedDictionary<int, long> Used { get; } = new();
    /// <summary>불을 끈 조리대 · 열원에서 뗀 방 (그때까지 그 원인은 쉰다).</summary>
    public SortedDictionary<int, long> Quiet { get; } = new();
    private long _next;
    private int _techSeen = -1;

    // ── 사고 · 다른 시스템이 읽는 배율 ──
    private static bool Any(World w, FurnitureType t) { foreach (var f in w.Ship.FurnitureOf(t)) if (ModulesV18.Works(f)) return true; return false; }
    private static Furniture? Best(World w, FurnitureType t) { Furniture? b = null; foreach (var f in w.Ship.FurnitureOf(t)) if (ModulesV18.Works(f) && (b == null || f.Machine!.Tier > b.Machine!.Tier)) b = f; return b; }
    public static float HeatSuitMul(Room? r) => r != null && r.Furniture.Any(f => f.Type == FurnitureType.HeatSuitRack && ModulesV18.Works(f)) ? 0.4f : 1f;
    public static float DrainMul(World w, Room? r) => r != null && (r.Furniture.Any(f => f.Type is FurnitureType.GreaseTrap or FurnitureType.GreywaterFilter && ModulesV18.Works(f))) ? 0.45f : Any(w, FurnitureType.GreywaterFilter) ? 0.75f : 1f;
    public static float PestMul(Room? r) => r != null && r.Furniture.Any(f => f.Type == FurnitureType.PestTrap && ModulesV18.Works(f)) ? 0.5f : 1f;
    public static float BoardMul(World w) => Any(w, FurnitureType.MeetingBoard) ? 0.6f : 1f;
    public static float StrapMul(World w) => Best(w, FurnitureType.CargoNet) is Furniture f ? MathF.Max(0.4f, 1f - 0.25f * ModulesV18.Power(f)) : 1f;
    public static float ServerMul(World w) => Any(w, FurnitureType.ServerRack) ? 0.6f : 1f;
    public static bool Vaulted(World w) => Any(w, FurnitureType.RecorderVault);
    public static float ClampMul(World w) => Best(w, FurnitureType.DockClampPanel) is Furniture f ? MathF.Max(0.3f, 1f - ModulesV18.Power(f)) : 1f;

    public (float p, Furniture? f) WorstCrock()
    {
        Furniture? worst = null; float hi = 0f;
        foreach (var f in _w.Ship.FurnitureOf(FurnitureType.Fermenter)) { float v = Crock.GetValueOrDefault(f.Id); if (v > hi) { hi = v; worst = f; } }
        return (Math.Clamp((hi - 0.6f) / 0.35f, 0f, 1f), worst);
    }

    public void Burp(int id, CrewMember? by)
    {
        if (id < 0) return;
        Crock[id] = 0f;
        if (by != null) { Stats.Burps++; Used[id] = _w.Tick; }
    }
    public void PotOff(int stoveId) { if (stoveId >= 0) Quiet[stoveId] = _w.Tick + SimTime.Hours(6); }
    public void Cool(Room? r) { if (r != null) Quiet[-1 - r.Id] = _w.Tick + SimTime.Hours(24); }

    /// <summary>쓸 수 있는 설비 (쓰는 일이 있고 · 일하고 · 오늘 아직 덜 썼다).</summary>
    public bool Wants(Furniture f, out ModulesV18.Row row)
    {
        row = ModulesV18.Of(f.Type)!;
        if (row == null || row.Use.Length == 0 || !ModulesV18.Works(f)) return false;
        long last = Used.GetValueOrDefault(f.Id, -SimTime.TicksPerDay);
        if (f.Type == FurnitureType.Fermenter) return Crock.GetValueOrDefault(f.Id) > 0.35f;
        return _w.Tick - last > SimTime.Hours(f.Type is FurnitureType.BreadOven or FurnitureType.Compactor or FurnitureType.InsectFarm ? 8f : 4f);
    }

    /// <summary>사람이 설비를 썼다: 종류마다 그 시스템에 실제로 하는 일.</summary>
    public string UseBy(Furniture f, CrewMember c)
    {
        var w = _w;
        var room = f.Room;
        float pw = ModulesV18.Power(f);
        Used[f.Id] = w.Tick;
        Stats.Uses++;
        if (f.Machine is Machine m) m.Wear = MathF.Min(1f, m.Wear + 0.01f);
        void Ease(CrewMember x, float v) => x.Needs.Stress = MathF.Max(0f, x.Needs.Stress - v);
        switch (f.Type)
        {
            case FurnitureType.Fermenter: Burp(f.Id, c); return "항아리 뚜껑을 열어 김을 뺐다";
            case FurnitureType.BreadOven:
                w.Smells.Emit(room, SmellKind.Bread, 0.5f + 0.2f * pw);
                foreach (var x in w.Crew) if (!x.Dead && x.Room == room) Ease(x, 0.04f);
                Stats.Bakes++;
                return "빵을 구웠다 — 고소한 냄새가 번진다";
            case FurnitureType.SpiceRack: Ease(c, 0.03f); return "양념을 골라 국에 넣었다";
            case FurnitureType.IceMaker:
                foreach (var x in w.Crew) if (!x.Dead && x.Room == room && x.Vitals.Injury > 0f) x.Vitals.Injury = MathF.Max(0f, x.Vitals.Injury - 0.02f * pw * 3f);
                return "얼음을 싸서 덴 곳에 댔다";
            case FurnitureType.PlantRack:
                foreach (var p in w.Eco.Plants) if (p.RoomId == room.Id && !p.Dead) { p.Water = MathF.Min(1f, p.Water + 0.4f); p.Fixed = true; }
                return "선반의 화분에 물을 주었다";
            case FurnitureType.CatTower:
                if (w.Eco.Cat is ShipCat cat && cat.Alive) { cat.Fear = MathF.Max(0f, cat.Fear - 0.3f); cat.Fond[c.Id] = MathF.Min(1f, EcoSystem.Fond(cat, c.Id) + 0.05f); Stats.Calmed++; }
                Ease(c, 0.05f);
                return "고양이 탑 곁에서 고양이와 놀아 주었다";
            case FurnitureType.PestTrap:
                foreach (var x in w.Eco.Pests) if (x.RoomId == room.Id && !x.Treated) { x.Pop *= 0.7f; if (!x.Found && x.Pop > 0.1f) { x.Found = true; x.FoundBy = c.Id; w.Eco.Stats.FoundByCrew++; } }
                Stats.Trapped++;
                return "끈끈이를 갈며 잡힌 벌레를 셌다";
            case FurnitureType.InsectFarm:
                foreach (var b in w.Drains.Bins) if (b.Food > 0f) { b.Food = MathF.Max(0f, b.Food - 0.15f); b.Fill = MathF.Max(0f, b.Fill - 0.08f); Stats.Eaten++; }
                return "음식물 찌꺼기를 사육장에 넣었다";
            case FurnitureType.GreaseTrap:
                foreach (var d in w.Drains.Drains) if (d.RoomId == room.Id) { d.Food *= 0.5f; d.Clog = MathF.Max(0f, d.Clog - 0.1f * pw); }
                return "기름 거름통을 비웠다";
            case FurnitureType.Compactor:
                foreach (var b in w.Drains.Bins) b.Fill = MathF.Max(0f, b.Fill - 0.12f * pw);
                w.Scrap.AddScrap(0.2f); Stats.Scrap++;
                return "쓰레기를 눌러 묶었다";
            case FurnitureType.Composter:
                foreach (var b in w.Drains.Bins) b.Food = MathF.Max(0f, b.Food - 0.1f);
                foreach (var p in w.Eco.Plants) if (!p.Dead) p.Health = MathF.Min(1f, p.Health + 0.03f);
                w.Smells.Emit(room, SmellKind.Foul, 0.05f);
                return "퇴비를 뒤집었다 — 흙냄새가 난다";
            case FurnitureType.GreywaterFilter:
                w.Flow.WaterQuality = MathF.Min(1f, w.Flow.WaterQuality + 0.03f * pw);
                return "거름층을 털고 물길을 텄다";
            case FurnitureType.CargoNet: return "그물 끈을 다시 조였다";
            case FurnitureType.MagBootRack: return "자석 신발을 충전대에 걸었다";
            case FurnitureType.ListeningPost:
            {
                Machine? loud = null;
                foreach (var x in w.Ship.Machines) if (HazardsV18.Rotating(x.Body.Type) && x.Body.Room == room && (loud == null || x.Wear > loud.Wear)) loud = x;
                if (loud != null && loud.Wear > 0.5f) { loud.Wear = MathF.Max(0f, loud.Wear - 0.05f * pw); MarkLog.Add(loud.Marks, w.Tick, $"{c.Name}: 소리를 듣고 기름을 쳤다"); return $"청음기로 {loud.Name} 소리를 듣고 기름을 쳤다"; }
                return "청음기로 설비 소리를 들어 보았다";
            }
            case FurnitureType.MeetingBoard: foreach (var x in w.Crew) if (!x.Dead && x.Room == room) Ease(x, 0.02f); return "칠판에 다음 회의에 할 말을 적었다";
            case FurnitureType.MemorialWall:
                c.Memory.Trauma = MathF.Max(0f, c.Memory.Trauma - 0.03f);
                Ease(c, 0.05f); Stats.Soothed++;
                return "추모 벽 앞에서 한참 서 있었다";
            case FurnitureType.MusicCorner:
            {
                Ease(c, 0.08f);
                foreach (var x in w.Crew) if (!x.Dead && x != c && x.Room == room && x.IsAwake) Ease(x, 0.03f);
                float hour = SimTime.HourOfDay(w.Tick);
                if (hour >= 22f || hour < 6f)
                    foreach (var x in w.Crew) if (!x.Dead && x.Pose == Pose.Sleeping && x.Room != null && x.Room != room && System.Numerics.Vector2.Distance(x.Room.Center, room.Center) < 10f) { x.Needs.Rest = MathF.Max(0f, x.Needs.Rest - 0.05f); x.Needs.Stress = MathF.Min(1f, x.Needs.Stress + 0.03f); Stats.Woken++; }
                return "악기를 연주했다";
            }
            case FurnitureType.LabStill:
            {
                var a = w.Soil.RoomSoil(room);
                a[(int)SoilKind.Bio] = MathF.Max(0f, a[(int)SoilKind.Bio] - 0.2f * pw);
                Stats.Distilled++;
                return "소독용 알코올을 내렸다";
            }
            case FurnitureType.ClothesRack: room.Humidity = MathF.Min(0.9f, room.Humidity + 0.04f); return "빨래를 널었다 — 방이 눅눅해진다";
            case FurnitureType.SewingMachine:
            {
                var b = w.Belongings.All.Where(x => x.Condition < 0.9f).OrderBy(x => x.Condition).ThenBy(x => x.Id).FirstOrDefault();
                if (b != null) { b.Condition = MathF.Min(1f, b.Condition + 0.3f * pw); return $"{b.Name}을(를) 꿰매 고쳤다"; }
                return "해진 옷깃을 꿰맸다";
            }
            case FurnitureType.Telescope: Ease(c, 0.06f); MarkLog.Add(c.Memory.Marks, w.Tick, "망원경으로 별을 보았다"); return "망원경으로 별을 보았다";
        }
        return "썼다";
    }

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        // 화물 그물: 사람들이 쓰면서 푼 걸쇠도 그물이 붙들고 있다 (매번)
        foreach (var n in w.Ship.FurnitureOf(FurnitureType.CargoNet))
            if (ModulesV18.Works(n)) foreach (var s in n.Room.Furniture) if (ManeuverSystem.Shelfish(s.Type) && w.Maneuver.Latched.Add(s.Id)) Stats.Latched++;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(30);
        const float step = 0.5f / 24f;
        // 기술 → 설비 단계 (익힌 기술이 늘면 다시 맞춘다)
        if (w.Eras.Known.Count != _techSeen) { _techSeen = w.Eras.Known.Count; SyncTiers(); }
        foreach (var k in Quiet.Where(kv => kv.Value < w.Tick).Select(kv => kv.Key).ToList()) Quiet.Remove(k);
        bool weightless = w.ZeroG.Weightless;
        foreach (var f in w.Ship.Furniture)
        {
            if (!ModulesV18.Is(f.Type)) continue;
            if (f.Type == FurnitureType.Fermenter && !f.Stowed) Crock[f.Id] = MathF.Min(1f, Crock.GetValueOrDefault(f.Id) + 0.7f * step); // 일하든 안 하든 김은 찬다
            if (!ModulesV18.Works(f)) continue;
            var room = f.Room;
            float pw = ModulesV18.Power(f);
            switch (f.Type)
            {
                case FurnitureType.PlantRack:
                    foreach (var p in w.Eco.Plants) if (p.RoomId == room.Id && !p.Dead && !p.Fixed) { p.Fixed = true; Stats.Tied++; }
                    break;
                case FurnitureType.CatTower:
                    if (w.Eco.Cat is ShipCat cat && cat.Alive && cat.Fear > 0f) { cat.Fear = MathF.Max(0f, cat.Fear - (cat.RoomId == room.Id ? 0.08f : 0.03f) * pw); }
                    break;
                case FurnitureType.PestTrap:
                    foreach (var x in w.Eco.Pests)
                        if (x.RoomId == room.Id && !x.Treated)
                        {
                            x.Pop *= 1f - 0.05f * pw;
                            if (!x.Found && x.Pop > 0.18f) { x.Found = true; x.FoundBy = -1; w.Eco.Stats.FoundByComputer++; Stats.Trapped++; w.Log.Add(w.Tick, LogKind.Ship, $"{room.Name} 벌레 덫에 바구미가 잡혀 있다 — 곡물 자루를 살펴보자"); }
                        }
                    break;
                case FurnitureType.GreaseTrap:
                case FurnitureType.GreywaterFilter:
                    foreach (var d in w.Drains.Drains) if ((d.RoomId == room.Id || f.Type == FurnitureType.GreywaterFilter) && d.Clog > 0.1f) d.Clog = MathF.Max(0.1f, d.Clog - 0.006f * pw);
                    break;
                case FurnitureType.Compactor:
                    foreach (var b in w.Drains.Bins) if (b.RoomId == room.Id && b.Fill > 0.3f) b.Fill -= 0.01f * pw;
                    break;
                case FurnitureType.Composter:
                case FurnitureType.InsectFarm:
                    foreach (var b in w.Drains.Bins) if (b.Food > 0.05f) b.Food = MathF.Max(0f, b.Food - 0.004f * pw);
                    break;
                case FurnitureType.IceMaker:
                    if (room.Air.Temperature > 24f) room.Air.Temperature -= 0.4f * pw;
                    break;
                case FurnitureType.HeatSuitRack:
                    break; // 사고 쪽에서 읽는다 (열탈진 원인 ×0.4)
                case FurnitureType.GrabRail:
                case FurnitureType.MagBootRack:
                    if (!weightless) break;
                    foreach (var c in w.Crew)
                        if (!c.Dead && c.Room == room && w.ZeroG.Queasy.TryGetValue(c.Id, out var q) && q > 0.2f) { w.ZeroG.Queasy[c.Id] = MathF.Max(0f, q - 0.06f * pw); Stats.Gripped++; }
                    foreach (var d in w.ZeroG.Floaters) if (d.RoomId == room.Id) d.Vel *= 0.5f;
                    break;
                case FurnitureType.CrashSeat:
                    foreach (var t in w.Maneuver.Tumbles) if (!t.Up && w.Crew.FirstOrDefault(c => c.Id == t.Crew) is CrewMember tc && tc.Room == room) { t.Up = true; Stats.Righted++; }
                    break;
                case FurnitureType.ServerRack:
                    w.Research += 0.02f * pw;
                    break;
                case FurnitureType.RecorderVault:
                    foreach (var g in w.Blackbox.Wipes) if (g.Noticed < 0) { g.Noticed = w.Tick; Stats.VaultFinds++; w.Log.Add(w.Tick, LogKind.Ship, $"기록 금고 사본과 블랙박스가 어긋난다 — {SimTime.Clock(g.From)}부터 비어 있다"); }
                    break;
                case FurnitureType.MemorialWall:
                    foreach (var c in w.Crew) if (!c.Dead && c.Room == room && c.Memory.Trauma > 0f) c.Memory.Trauma = MathF.Max(0f, c.Memory.Trauma - 0.004f * pw);
                    break;
                case FurnitureType.EyeWash:
                    foreach (var c in w.Crew) if (!c.Dead && c.Room == room && c.Vitals.Injury > 0f && c.Vitals.Injury < 0.15f) { c.Vitals.Injury = MathF.Max(0f, c.Vitals.Injury - 0.004f * pw); Stats.Washes++; }
                    break;
                case FurnitureType.OxygenMaskBox:
                    if (room.Unbreathable || w.Fire.CountIn(room) > 0 || w.Smells.Level(room, SmellKind.Burnt) > 0.3f)
                        foreach (var c in w.Crew) if (!c.Dead && c.Room == room && c.Suit == null && c.Vitals.Oxygen < 0.95f) { c.Vitals.Oxygen = MathF.Min(1f, c.Vitals.Oxygen + 0.1f * pw); Stats.Masks++; }
                    break;
                case FurnitureType.ClothesRack:
                    if (room.Humidity < 0.55f) room.Humidity += 0.004f;
                    break;
                case FurnitureType.LabStill:
                    if (w.Fire.CountIn(room) > 0 && R.Chance(0.3f)) w.Fire.Ignite(f.Cells[0], 0.3f); // 불 곁의 알코올
                    break;
            }
        }
    }

    /// <summary>익힌 기술이 그 설비의 단계를 올린다 (새로 다는 것도 그 단계로).</summary>
    public void SyncTiers()
    {
        var w = _w;
        foreach (var f in w.Ship.Furniture)
        {
            if (f.Machine is not Machine m || !ModulesV18.Is(f.Type)) continue;
            int t = TechWebV18.TierFor(w, f.Type);
            if (t > m.Tier) { m.Tier = t; Stats.TierUps++; MarkLog.Add(m.Marks, w.Tick, $"{Tech.Roman(t)}단계로 손봤다"); }
        }
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Crock.Count); foreach (var (k, v) in Crock) { I(k); F(v); }
        I(Used.Count); foreach (var (k, v) in Used) { I(k); I(v); }
        I(Quiet.Count); I(Stats.Uses); I(Stats.TierUps);
    }
}

/// <summary>설비를 쓰러 간다 (빵 굽기 · 김 빼기 · 연주 · 별 보기 · 덫 갈기 …): 쉬는 시간에 · 일 끝나고.</summary>
public sealed class FixtureUseActivity : Activity
{
    public override string Id => "fixtureuse";
    public override string Label => "설비 쓰기";

    private static (Furniture f, ModulesV18.Row row)? Pick(CrewMember c, World w, DistanceField dist)
    {
        (Furniture, ModulesV18.Row)? best = null;
        float bd = float.MaxValue;
        foreach (var f in w.Ship.Furniture)
        {
            if (!ModulesV18.Is(f.Type) || f.ReservedBy != null && f.ReservedBy != c || !w.Fittings.Wants(f, out var row)) continue;
            if (row.Skill == Skill.Cooking && c.SkillLevel(Skill.Cooking) < 0.2f && f.Type != FurnitureType.Fermenter) continue;
            Cell? spot = null;
            foreach (var s in f.UseSpots) if (dist.Reachable(s) && !w.IsSpotTaken(s, c)) { spot = s; break; }
            if (spot is not Cell sc) continue;
            float d = dist.Get(sc) + (c.Id * 7 + f.Id * 13) % 5; // 사람마다 가까운 것 · 조금씩 다르게
            if (d < bd) { bd = d; best = (f, row); }
        }
        return best;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Down || c.Outside || c.IsChild && c.Age < 10f || FittingSystem.Off || Crisis.Acting(w)) return (0f, "—");
        if (Pick(c, w, dist) is not var (f, row)) return (0f, "쓸 설비가 없다");
        bool urgent = f.Type == FurnitureType.Fermenter && w.Fittings.Crock.GetValueOrDefault(f.Id) > 0.6f;
        var s = c.Schedule;
        float workEnd = s.WorkStart + s.WorkLength;
        bool free = SimTime.InWindow(Hour(w), workEnd, SimTime.HoursFromTo(workEnd, s.SleepStart)); // 일 끝나고 잘 때까지
        float score = (free ? 0.42f : 0.12f) + 0.3f * c.Needs.Stress + (urgent ? 0.3f : 0f);
        if (OnShift(c, w) && !urgent) score -= 0.1f;
        if (Bedtime(c, w)) score -= 0.25f;
        return (MathF.Max(0f, score), $"{row.Use} ({f.Room.Name})");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Pick(c, w, dist) is not var (f, row)) return null;
        var spot = f.UseSpots.Where(s => dist.Reachable(s) && !w.IsSpotTaken(s, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell at) return null;
        var fx = f;
        var toils = new List<Toil>
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(row.UseMin), Pose.Working, fx.Center),
            new DoToil((cm, world) =>
            {
                if (!ModulesV18.Works(fx)) return false;
                string what = world.Fittings.UseBy(fx, cm);
                world.Log.Add(world.Tick, LogKind.Life, $"{fx.Room.Name}: {what}", cm.Id);
                return true;
            }),
        };
        return new Job(this, row.Use, toils) { LogText = $"{row.Name} — {row.Use}", LogKind = LogKind.Life };
    }
}
