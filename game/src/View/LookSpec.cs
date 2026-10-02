using System;
using ShipSim.Core;

namespace ShipSim.View;

// v16.5a 재질 그림 표 (Godot 없이 읽히는 순수 표 — 헤드리스 --lookcheck 가 그대로 컴파일해 대조한다).
// 텍스처 이름은 tools/texgen 의 Gen.All() 과 같다. 바닥재 · 벽 · 상태 겹치기 · 흔적 칸 · 입자 칸 · 방 색온도 · 빛 세기 · 확대 단계.
// 화면은 이 표를 읽기만 한다 — 새 바닥재는 enum 끝 + 파일 이름 한 줄 + Floor() 한 줄.
public static class LookSpec
{
    /// <summary>텍스처 한 칸 = 64px, 4×4칸마다 되풀이 (칸 좌표를 4로 나눈 나머지로 영역을 고른다 → 이음매 없음).</summary>
    public const int CellPx = 64, Period = 4;

    public enum FloorLook { Metal, Grate, Tile, Rubber, Carpet, Engine }
    public enum WallLook { Panel, Partition, Hull }
    /// <summary>상태 겹치기 (Core 칸 상태 · 닳음 · 원소 장을 읽는다).</summary>
    public enum Ov { Wear, Wet, Oil, Soot, Rust, Dust, Frost }

    public static readonly string[] FloorFiles = { "look_floor_metal", "look_floor_grate", "look_floor_tile", "look_floor_rubber", "look_floor_carpet", "look_floor_engine" };
    public static readonly string[] WallFiles = { "look_wall_panel", "look_wall_partition", "look_wall_hull" };
    public static readonly string[] OvFiles = { "look_ov_wear", "look_ov_wet", "look_ov_oil", "look_ov_soot", "look_ov_rust", "look_ov_dust", "look_ov_frost" };
    public const string DecalFile = "look_decals", ParticleFile = "look_particles";

    public static string[] AllFiles()
    {
        var all = new string[FloorFiles.Length + WallFiles.Length + OvFiles.Length + 2];
        FloorFiles.CopyTo(all, 0);
        WallFiles.CopyTo(all, FloorFiles.Length);
        OvFiles.CopyTo(all, FloorFiles.Length + WallFiles.Length);
        all[^2] = DecalFile; all[^1] = ParticleFile;
        return all;
    }

    /// <summary>
    /// 겹치기 모양: 진하기(최대 불투명) · 부드러움(드러나는 가장자리 폭) · 테(+ 밝은 테 · − 어두운 테) · 어둡게(카펫은 닳으면 눌려 어둡다).
    /// 셰이더: a = clamp((마스크 − (1 − 양)) / 부드러움, 0, 1) × 진하기.
    /// </summary>
    public static (float maxA, float soft, float rim, float dark) OvStyle(Ov o, Material floor) => o switch
    {
        Ov.Wear => floor == Material.Carpet ? (0.55f, 0.3f, 0f, 0.75f) : floor == Material.Grate ? (0.3f, 0.3f, 0f, 0f) : (0.42f, 0.28f, 0f, 0f),
        Ov.Wet => floor == Material.Carpet ? (0.5f, 0.2f, 0f, 0.4f) : (0.58f, 0.05f, 0.32f, 0f),
        Ov.Oil => (0.88f, 0.05f, -0.12f, 0f),
        Ov.Soot => (0.9f, 0.35f, 0f, 0f),
        Ov.Rust => (0.85f, 0.12f, -0.08f, 0f),
        Ov.Dust => (0.48f, 0.4f, 0f, 0f),
        Ov.Frost => (0.82f, 0.08f, 0.28f, 0f),
        _ => (0.5f, 0.2f, 0f, 0f),
    };

    /// <summary>흔적 아틀라스 칸 (8×4, 칸 64px).</summary>
    public enum Decal
    {
        Coffee, Food, Mineral, OilDrops, Prints, WetPrints, OilPrints, Wheels,
        Scratches, Drag, Gouge, Swirl, TapeX, HazardTape, TapePatch, TapeLoose,
        Sticky, PinNote, Checklist, Chalk, WeldBead, WeldPatch, HeatRing, Spatter,
        Burn, Scorch, Crack, Shatter, Chevron, Spot, RustRun, ScrewHoles,
    }
    public const int DecalCols = 8, DecalRows = 4, DecalPx = 64;

    /// <summary>입자 칸 (8칸, 칸 32px).</summary>
    public enum Part { Steam, Smoke, Drop, Splash, Spark, Ember, Dust, Bead }
    public const int PartPx = 32;

    // ───────────────────────── 바닥재 고르기 ─────────────────────────

    /// <summary>중작업 구역 (기관 구역판: 미끄럼 방지 돌기 · 경고 띠).</summary>
    public static bool Heavy(RoomType t) => t is RoomType.Workshop or RoomType.WeldingShop or RoomType.Cargo or RoomType.DockingBay or RoomType.ShuttleBay
        or RoomType.Airlock or RoomType.EvaPrep or RoomType.DroneBay or RoomType.RobotBay or RoomType.PartsPrep or RoomType.EscapeBay or RoomType.CraneControl;

    /// <summary>기관 구역 (격자 바닥 · 청록 빛).</summary>
    public static bool EngineZone(RoomType t) => t is RoomType.Engine or RoomType.Reactor or RoomType.Cooling or RoomType.PumpRoom or RoomType.HvacRoom
        or RoomType.Crusher or RoomType.PropellantTank or RoomType.HeatStorage or RoomType.GasStorage or RoomType.Recycling or RoomType.Power
        or RoomType.Substation or RoomType.BatteryRoom or RoomType.FuelCell or RoomType.LifeSupport or RoomType.SuppressionRoom;

    /// <summary>생활 구역 (카펫 · 호박색 빛).</summary>
    public static bool Living(RoomType t) => t is RoomType.Quarters or RoomType.PrivateCabins or RoomType.QuietQuarters or RoomType.Lounge or RoomType.Chapel
        or RoomType.Meditation or RoomType.Theater or RoomType.School or RoomType.MeetingRoom or RoomType.Archive or RoomType.WaterWallCabin
        or RoomType.Observatory or RoomType.Mess or RoomType.Galley or RoomType.Gym or RoomType.Garden;

    /// <summary>
    /// Core 바닥재 + 방 종류 → 그림. 재질 성질(물 빠짐 · 미끄러움)은 Core 가 정하고 화면은 그 재질을 그대로 보여 준다.
    /// 기관 구역의 격자 방은 벽을 따라 한 칸 둘레를 기관 구역판으로 (기계 받침) · 중작업 방의 금속판은 기관 구역판으로.
    /// </summary>
    public static FloorLook Floor(Material m, RoomType kind, bool rim)
    {
        if (m == Material.None) m = Materials.FloorFor(kind);
        return m switch
        {
            Material.Grate => rim && EngineZone(kind) ? FloorLook.Engine : FloorLook.Grate,
            Material.Tile => FloorLook.Tile,
            Material.Rubber => FloorLook.Rubber,
            Material.Carpet => FloorLook.Carpet,
            Material.MetalPlate => Heavy(kind) ? FloorLook.Engine : FloorLook.Metal,
            _ => FloorLook.Metal,
        };
    }

    /// <summary>바닥 그림의 평균 색 (멀리서 볼 때 · 미니 그림). 텍스처 평균과 가깝게.</summary>
    public static (float r, float g, float b) FloorAverage(FloorLook f) => f switch
    {
        FloorLook.Metal => (0.36f, 0.4f, 0.45f),
        FloorLook.Grate => (0.2f, 0.23f, 0.27f),
        FloorLook.Tile => (0.72f, 0.76f, 0.78f),
        FloorLook.Rubber => (0.25f, 0.28f, 0.25f),
        FloorLook.Carpet => (0.42f, 0.27f, 0.35f),
        _ => (0.36f, 0.39f, 0.42f),
    };

    // ───────────────────────── 빛 ─────────────────────────

    /// <summary>빛 버퍼: 칸마다 픽셀 수 · 다시 계산 간격(초).</summary>
    public const int LightPx = 4;
    public const float LightRefresh = 0.12f;
    /// <summary>방 바탕 밝기 (켜진 방 · 통로 · 정전 어둠) · 천장 등 웅덩이 세기 · 반지름(칸).</summary>
    public const float AmbientLit = 0.56f, AmbientCorridor = 0.5f, AmbientDark = 0.06f, CeilingPool = 0.5f, CeilingRadius = 2.6f;
    /// <summary>붉은 비상등 (정전 · 조명이 살아 있는 방) · 비상 조명 설비(흰빛).</summary>
    public static readonly (float r, float g, float b) Emergency = (0.95f, 0.14f, 0.09f), EmergencyFixture = (0.95f, 0.9f, 0.75f);
    /// <summary>불빛 (깜빡이는 주황).</summary>
    public static readonly (float r, float g, float b) FireLight = (1f, 0.55f, 0.2f);

    /// <summary>방 성격 색온도: 기관 구역 청록 · 생활 구역 호박 · 의무 흰빛 · 재배 연두 · 함교 푸른빛.</summary>
    public static (float r, float g, float b) Temperature(RoomType t)
    {
        if (EngineZone(t)) return (0.72f, 0.96f, 1f);
        if (Living(t)) return (1f, 0.84f, 0.62f);
        return t switch
        {
            RoomType.Medbay or RoomType.Triage or RoomType.Quarantine or RoomType.QuarantineLock or RoomType.Morgue or RoomType.Decon or RoomType.Hyperbaric or RoomType.Lab => (0.93f, 0.98f, 1f),
            RoomType.Hydroponics or RoomType.AlgaeLab or RoomType.ProteinFarm or RoomType.SeedVault => (0.84f, 1f, 0.76f),
            RoomType.Bridge or RoomType.BackupBridge or RoomType.Comms or RoomType.Navigation or RoomType.ServerRoom or RoomType.Security or RoomType.Calibration or RoomType.ElectronicsLab => (0.74f, 0.84f, 1f),
            RoomType.Freezer => (0.8f, 0.92f, 1f),
            _ => (0.92f, 0.93f, 0.96f),
        };
    }

    /// <summary>설비가 내는 불빛 (화면 · 콘솔 · 노심 · 재배등 …): 색 · 반지름(칸) · 세기 · 깜빡임. null 이면 빛이 없다.</summary>
    public static (float r, float g, float b, float radius, float strength, float flicker)? Glow(FurnitureType t) => t switch
    {
        FurnitureType.Console or FurnitureType.NavComputer or FurnitureType.DiagnosticScanner or FurnitureType.ReactorSimulator => (0.45f, 0.8f, 1f, 1.8f, 0.42f, 0.08f),
        FurnitureType.MainComputer or FurnitureType.SensorArray or FurnitureType.SignalBooster => (0.4f, 0.65f, 1f, 2.2f, 0.4f, 0.05f),
        FurnitureType.ReactorCore => (0.35f, 0.75f, 1f, 3.4f, 0.6f, 0.04f),
        FurnitureType.EngineCore => (1f, 0.62f, 0.3f, 3f, 0.5f, 0.06f),
        FurnitureType.GrowBed or FurnitureType.PlantWall => (0.95f, 0.5f, 1f, 1.8f, 0.4f, 0f),
        FurnitureType.Stove or FurnitureType.Oven => (1f, 0.55f, 0.25f, 1.5f, 0.35f, 0.15f),
        FurnitureType.Aquarium => (0.35f, 0.75f, 1f, 1.6f, 0.38f, 0.1f),
        FurnitureType.Projector => (0.85f, 0.85f, 1f, 2.4f, 0.4f, 0.2f),
        FurnitureType.LedPanel => (0.95f, 0.95f, 1f, 3.2f, 0.5f, 0f),
        FurnitureType.EmergencyLight => (0.95f, 0.9f, 0.75f, 3.6f, 0.5f, 0f),
        FurnitureType.PowerPanel or FurnitureType.Battery or FurnitureType.CapacitorBank => (0.5f, 1f, 0.6f, 1.2f, 0.25f, 0.03f),
        FurnitureType.SolderStation or FurnitureType.Lathe or FurnitureType.Fabricator => (1f, 0.85f, 0.6f, 1.4f, 0.3f, 0.1f),
        FurnitureType.MealDispenser or FurnitureType.CoffeeMachine or FurnitureType.Fridge => (0.85f, 0.95f, 1f, 1.1f, 0.22f, 0.02f),
        FurnitureType.MedBed => (0.7f, 1f, 0.9f, 1.4f, 0.28f, 0.04f),
        _ => null,
    };

    // ───────────────────────── 확대 단계 ─────────────────────────

    /// <summary>멀리(방 색 + 아이콘) · 보통 · 가까이(흔적 · 입자 · 디테일).</summary>
    public enum Lod { Far, Mid, Near }
    public const float ZoomFar = 0.42f, ZoomNear = 1.05f;
    public static Lod LodOf(float zoom) => zoom < ZoomFar ? Lod.Far : zoom < ZoomNear ? Lod.Mid : Lod.Near;

    /// <summary>입자 상한 (화면 밖은 만들지 않는다).</summary>
    public const int MaxParticles = 700;

    /// <summary>칸 해시 0~1 (흔적 자리 · 방향 고르기 — 결정론, 시뮬레이션 난수를 쓰지 않는다).</summary>
    public static float H(int x, int y, int k)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + k * 1274126177);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xffffff) / 16777216f;
    }
}
