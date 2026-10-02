using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.22 배 크기 등급 — 소형 · 기본형 · 중형 · 대형 · 초대형.
//   작을수록 꼭 필요한 방만 (좁고 알뜰), 클수록 방 종류가 많고 넓고 설비가 좋다.
//   ① 등급 이름 · 한 줄 소개 (배 고르기 화면) ② 시작 물자: 크기에 맞게 금속판 · 금속 원료 · 저장 식량 (작은 배도 며칠 만에 판이 바닥나지 않게)
//   ③ 특수 방의 대표 설비 (교정실 교정 장비 · 항법실 항법 컴퓨터 · 버섯 재배실 제습기 …) — 기본 배 다섯 척은 처음부터 달고 떠난다.
//   ④ 재활용실 · 파쇄실: 수리 · 공사로 쓴 판에서 나온 고철을 몇 시간마다 금속 원료로 되살린다 (정제기가 돌아야).

public enum ShipSize { Small, Basic, Medium, Large, Huge }

public static class ShipClasses
{
    public static ShipSize Of(int crew) => crew <= 4 ? ShipSize.Small : crew <= 8 ? ShipSize.Basic : crew <= 14 ? ShipSize.Medium : crew <= 24 ? ShipSize.Large : ShipSize.Huge;
    public static ShipSize Of(ShipTemplate t) => Of(t.Crew);

    public static string Name(ShipSize s) => s switch
    {
        ShipSize.Small => "소형", ShipSize.Basic => "기본형", ShipSize.Medium => "중형", ShipSize.Large => "대형", _ => "초대형",
    };

    /// <summary>배 고르기 화면의 한 줄 (그 배에 탄 사람이 하는 말투로).</summary>
    public static string Blurb(ShipSize s) => s switch
    {
        ShipSize.Small => "꼭 필요한 방만 있다. 통로는 한 사람 폭, 냉동 창고의 저장 식량이 재배실을 받친다.",
        ShipSize.Basic => "살림이 갖춰졌다. 휴게실 · 체력단련실 · 방사선 대피소 · 조류 배양실까지.",
        ShipSize.Medium => "통로가 차압 문으로 두 구획. 수경 · 조류 · 버섯, 연료전지와 펌프실로 한 번 더 버틴다.",
        ShipSize.Large => "연구실 · 서버실 · 보안실 · 정원 · 관측실 · 개인 선실. 손이 많이 가는 만큼 할 수 있는 것도 많다.",
        _ => "극장 · 학교 · 원심 거주구 · 셔틀 격납고. 배라기보다 작은 도시다.",
    };

    /// <summary>기본 배 다섯 척 (v16.22 새 설계).</summary>
    public static bool Base(string? key) => key is "Kestrel" or "Mirinae" or "Hanbit" or "Eunha" or "Cheonma";

    /// <summary>설계도에서 방 수 · 방 종류 수 (통로 빼고).</summary>
    public static (int rooms, int kinds) Count(Ship ship) =>
        (ship.Rooms.Count(r => r.Type != RoomType.Corridor), ship.Rooms.Where(r => r.Type != RoomType.Corridor).Select(r => r.Kind).Distinct().Count());

    // ─────────────────────────────── ② 시작 물자 ───────────────────────────────

    /// <summary>크기 등급마다 더 싣는 것: 금속판 · 금속 원료 · 구조재 · 저장 식량 (기본 적재 위에). 설비가 많은 큰 배일수록 수리재가 더 든다.</summary>
    public static (int plate, int ore, int structure, int ration) Extra(ShipSize s) => s switch
    {
        ShipSize.Small => (6, 14, 2, 20),
        ShipSize.Basic => (8, 18, 3, 24),
        ShipSize.Medium => (14, 30, 4, 30),
        ShipSize.Large => (22, 44, 6, 40),
        _ => (30, 60, 8, 50),
    };

    /// <summary>시작 물자를 크기에 맞춘다 (기본 배 다섯 척 · 배 크기 등급이 있는 배). 저장 식량은 냉동 창고 · 창고 선반부터.</summary>
    public static void Stock(Ship ship, ShipSize s)
    {
        var (plate, ore, structure, ration) = Extra(s);
        var shelves = ship.Rooms.Where(r => !r.Detached && (r.Kind is RoomType.Freezer or RoomType.Storage or RoomType.Cargo or RoomType.Workshop or RoomType.Recycling))
            .OrderBy(r => r.Kind == RoomType.Freezer ? 0 : r.Kind == RoomType.Storage ? 1 : r.Kind == RoomType.Cargo ? 2 : 3).ThenBy(r => r.Id)
            .SelectMany(r => r.Furniture).Where(f => f.Type == FurnitureType.Shelf && f.Storage != null).ToList();
        void Put(ItemKind k, int n, bool food)
        {
            foreach (var sh in food ? shelves : shelves.AsEnumerable().Reverse())
            {
                if (n <= 0) return;
                if (sh.Storage!.Accepts(k)) n -= sh.Storage.Add(k, n);
            }
        }
        Put(ItemKind.Ration, ration, true);
        Put(ItemKind.Plate, plate, false);
        Put(ItemKind.MetalOre, ore, false);
        Put(ItemKind.Structure, structure, false);
    }

    // ─────────────────────────────── ③ 특수 방의 대표 설비 ───────────────────────────────

    /// <summary>방 종류마다 처음부터 다는 대표 설비 (그 방이 무엇인지 한눈에 보이게).</summary>
    public static FurnitureType[] Signature(RoomType k) => k switch
    {
        RoomType.Calibration => new[] { FurnitureType.CalibrationRig },
        RoomType.Navigation => new[] { FurnitureType.NavComputer },
        RoomType.Security => new[] { FurnitureType.ThermalCamera, FurnitureType.SignalBooster },
        RoomType.HeatStorage => new[] { FurnitureType.HeatExchanger },
        RoomType.Substation => new[] { FurnitureType.SurgeProtector },
        RoomType.BatteryRoom => new[] { FurnitureType.CapacitorBank },
        RoomType.ServerRoom => new[] { FurnitureType.SurgeProtector, FurnitureType.EmergencyLight },
        RoomType.ComputerRoom => new[] { FurnitureType.SurgeProtector },
        RoomType.HvacRoom => new[] { FurnitureType.AirPurifier, FurnitureType.Scrubber },
        RoomType.MushroomFarm => new[] { FurnitureType.Dehumidifier },
        RoomType.AlgaeLab or RoomType.ProteinFarm => new[] { FurnitureType.NutrientDoser },
        RoomType.Gym => new[] { FurnitureType.Treadmill },
        RoomType.Theater => new[] { FurnitureType.Projector },
        RoomType.Archive or RoomType.School => new[] { FurnitureType.Bookshelf },
        RoomType.Laundry => new[] { FurnitureType.WashingMachine },
        RoomType.EvaPrep => new[] { FurnitureType.SuitDryer },
        RoomType.Decon => new[] { FurnitureType.DeconShower },
        RoomType.Lab => new[] { FurnitureType.DiagnosticScanner },
        RoomType.ElectronicsLab => new[] { FurnitureType.SolderStation },
        RoomType.WeldingShop => new[] { FurnitureType.Lathe },
        RoomType.Triage => new[] { FurnitureType.Autoclave },
        RoomType.QuietQuarters => new[] { FurnitureType.WhiteNoise },
        RoomType.Garden => new[] { FurnitureType.PlantWall },
        RoomType.PartsPrep => new[] { FurnitureType.PartTestBench },
        RoomType.Cargo => new[] { FurnitureType.Hoist },
        RoomType.Meditation => new[] { FurnitureType.Aquarium },
        RoomType.PumpRoom => new[] { FurnitureType.LeakDetector },
        RoomType.FuelCell => new[] { FurnitureType.VibrationMonitor },
        RoomType.Observatory => new[] { FurnitureType.Projector },
        RoomType.WaterWallCabin => new[] { FurnitureType.BlackoutCurtain },
        _ => Array.Empty<FurnitureType>(),
    };

    /// <summary>대형 · 초대형은 본래 방에도 좋은 설비를 단다 (오븐 · 커피 머신 · 게임 탁자 · 생장등).</summary>
    public static FurnitureType[] Luxury(RoomType k, ShipSize s) => s < ShipSize.Large ? Array.Empty<FurnitureType>() : k switch
    {
        RoomType.Galley => new[] { FurnitureType.Oven },
        RoomType.Mess => new[] { FurnitureType.CoffeeMachine },
        RoomType.Lounge => new[] { FurnitureType.GameTable },
        RoomType.Hydroponics => new[] { FurnitureType.LedPanel },
        _ => Array.Empty<FurnitureType>(),
    };

    /// <summary>처음 실린 대표 설비를 단다 (길을 막지 않는 벽 쪽 칸에). 단 개수.</summary>
    public static int FitOut(World w, ShipSize s)
    {
        int n = 0;
        foreach (var room in w.Ship.Rooms.OrderBy(r => r.Id).ToList())
        {
            if (room.Detached || room.Type == RoomType.Corridor) continue;
            foreach (var t in Signature(room.Kind).Concat(room.Kind == room.Type ? Luxury(room.Type, s) : Array.Empty<FurnitureType>()))
            {
                if (Modules.Spot(w, room) is not Cell at) break;
                var f = w.Ship.AddFurniture(t, at);
                if (f.Machine != null) { f.Machine.Wear = 0.05f; f.Machine.Condition = 0.95f; }
                n++;
            }
        }
        if (n > 0) w.Paths.Invalidate();
        return n;
    }
}

/// <summary>v16.22 고철 되살리기 — 수리 · 공사로 금속판이 줄면 그만큼 고철이 생기고, 재활용실 · 파쇄실 정제기가 몇 시간마다 금속 원료로 녹인다.</summary>
public sealed class ScrapSystem
{
    private readonly World _w;
    public ScrapSystem(World w) => _w = w;
    /// <summary>모아 둔 고철 (판 개수로).</summary>
    public float Scrap { get; private set; }
    public int Recovered, Runs;
    private int _plates = -1;
    private long _next;

    public Furniture? Smelter => _w.Ship.FurnitureOf(FurnitureType.Refinery)
        .Where(f => !f.Room.Detached && f.Room.Kind is RoomType.Recycling or RoomType.Crusher && f.Machine!.Efficiency > 0f).OrderBy(f => f.Id).FirstOrDefault();

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Hours(1);
        int plates = w.Ship.CountStored(ItemKind.Plate) + w.Ship.CountStored(ItemKind.Structure);
        if (_plates >= 0 && plates < _plates) Scrap += (_plates - plates) * 0.6f; // 떼어 낸 헌 판 · 깎아 낸 자투리
        _plates = plates;
        if (w.Tick % SimTime.Hours(4) >= SimTime.Hours(1) || Scrap < 2f || Smelter is not Furniture sm) return;
        int ore = (int)(Scrap / 2f);
        var shelf = sm.Room.Furniture.Where(f => f.Type == FurnitureType.Shelf && f.Storage != null && f.Storage.Accepts(ItemKind.MetalOre)).OrderBy(f => f.Id).FirstOrDefault()
                    ?? w.Ship.Containers.Where(f => f.Type == FurnitureType.Shelf && f.Storage!.Accepts(ItemKind.MetalOre)).OrderBy(f => f.Id).FirstOrDefault();
        if (shelf == null) return;
        int put = shelf.Storage!.Add(ItemKind.MetalOre, ore);
        if (put <= 0) return;
        Scrap -= put * 2f;
        Recovered += put;
        Runs++;
        sm.Machine!.Wear = MathF.Min(1f, sm.Machine.Wear + 0.01f * put);
        MarkLog.Add(sm.Room.Marks, w.Tick, $"고철 녹임 · 금속 원료 {put}");
        w.Log.Add(w.Tick, LogKind.Work, $"{sm.Room.Name}: 수리 · 공사에서 나온 고철을 녹여 금속 원료 {put}개를 되살렸다");
    }

    public void Hash(Action<long> I, Action<float> F) { F(Scrap); I(Recovered); I(Runs); I(_plates); }
}
