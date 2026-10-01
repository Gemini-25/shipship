using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.2 아이콘: game/assets/icons/*.svg (24×24 흰 선 — 그릴 때 색을 입힌다).
/// 크기마다 SVG를 그 픽셀로 다시 그려 두어 작게 그려도 선이 또렷하다. 파일이 없으면 글자 한 자로 대신한다.
/// </summary>
public static class Icons
{
    private const string Dir = "res://assets/icons/";
    private static readonly Dictionary<(string, int), Texture2D?> Cache = new();
    private static readonly Dictionary<string, string?> Source = new();

    /// <summary>그 이름 · 픽셀 크기의 아이콘 (없으면 null).</summary>
    public static Texture2D? Get(string name, int px)
    {
        px = Mathf.Clamp(px, 8, 96);
        if (Cache.TryGetValue((name, px), out var tex)) return tex;
        tex = Raster(name, px);
        Cache[(name, px)] = tex;
        return tex;
    }

    public static bool Has(string name) => Get(name, 16) != null;

    private static Texture2D? Raster(string name, int px)
    {
        string path = Dir + name + ".svg";
        if (!Source.TryGetValue(name, out var svg))
        {
            svg = FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : null;
            Source[name] = svg;
        }
        if (svg != null)
        {
            var img = new Image();
            if (img.LoadSvgFromString(svg, px / 24f) == Error.Ok) return ImageTexture.CreateFromImage(img);
        }
        // 내보낸 판에서는 날 SVG가 빠질 수 있다: 가져온(임포트한) 텍스처라도
        if (ResourceLoader.Exists(path)) return GD.Load<Texture2D>(path);
        return null;
    }

    /// <summary>아이콘 한 개 (가운데 기준). 파일이 없으면 대신 글자.</summary>
    public static void Draw(CanvasItem ci, string name, Vector2 center, float size, Color color)
    {
        int px = Mathf.RoundToInt(size);
        if (Get(name, px) is Texture2D tex)
        {
            var rect = new Rect2(center - new Vector2(px, px) * 0.5f, new Vector2(px, px));
            ci.DrawTextureRect(tex, rect, false, color);
            return;
        }
        int fs = Mathf.Max(8, Mathf.RoundToInt(size * 0.72f));
        Gfx.TextCentered(ci, Fonts.Bold, center, Glyph(name), fs, color);
    }

    /// <summary>아이콘이 없을 때 쓸 글자 한 자.</summary>
    public static string Glyph(string name) => name switch
    {
        "power" => "전", "battery" => "배", "oxygen" => "O", "co2" => "C", "water" => "물", "food" => "식", "work" => "작",
        "airtank" => "탱", "coolant" or "room-cooling" => "냉", "sleep" => "z", "duty" => "당", "eat" => "식", "rest" => "휴",
        "injury" => "+", "danger" or "alert" => "!", "fire" => "火", "vacuum" => "진", "broken" => "×", "help" => "?",
        "clock" => "◷", "close" => "×", "chevron-down" => "▾", "chevron-up" => "▴", "chevron-right" => "▸",
        "trend-up" => "↑", "trend-down" => "↓", "trend-flat" => "→", "play" => "▶", "pause" => "II", "pin" => "◎",
        _ => name.Length > 0 ? name[..1].ToUpperInvariant() : "·",
    };

    // ─────────────────────────── 무엇에 어떤 아이콘 ───────────────────────────

    public static string Resource(ResourceKey k) => k switch
    {
        ResourceKey.Power => "battery",
        ResourceKey.Oxygen => "oxygen",
        ResourceKey.AirTank => "airtank",
        ResourceKey.CO2 => "co2",
        ResourceKey.Water => "water",
        _ => "food",
    };

    /// <summary>방 종류마다 다른 아이콘 (v12.6 방 70종은 가장 가까운 모양으로).</summary>
    public static string Room(RoomType t) => t switch
    {
        RoomType.Corridor => "room-corridor",
        RoomType.Bridge or RoomType.BackupBridge => "room-bridge",
        RoomType.Engine or RoomType.ShuttleBay => "room-engine",
        RoomType.Reactor or RoomType.Centrifuge => "room-reactor",
        RoomType.Cooling or RoomType.HeatStorage => "room-cooling",
        RoomType.Power or RoomType.FuelCell => "room-power",
        RoomType.LifeSupport or RoomType.HvacRoom => "room-lifesupport",
        RoomType.Workshop or RoomType.PartsPrep or RoomType.WeldingShop => "room-workshop",
        RoomType.Storage or RoomType.Cargo => "room-storage",
        RoomType.Galley => "room-galley",
        RoomType.Mess => "room-mess",
        RoomType.Hydroponics or RoomType.ProteinFarm => "room-hydroponics",
        RoomType.Airlock or RoomType.DockingBay or RoomType.EvaPrep => "room-airlock",
        RoomType.Quarters or RoomType.PrivateCabins or RoomType.WaterWallCabin => "room-quarters",
        RoomType.QuietQuarters => "sleep",
        RoomType.Medbay or RoomType.Triage or RoomType.Hyperbaric => "room-medbay",
        RoomType.Lounge => "room-lounge",
        RoomType.Comms => "room-comms",
        RoomType.WaterPlant or RoomType.Recycling => "recycler",
        RoomType.Laundry => "washer",
        RoomType.Freezer => "fridge",
        RoomType.BatteryRoom => "battery",
        RoomType.Morgue => "dead",
        RoomType.EscapeBay => "capsule",
        RoomType.ServerRoom => "computer",
        RoomType.Calibration => "target",
        RoomType.Decon => "shower",
        RoomType.Quarantine or RoomType.QuarantineLock => "sick",
        RoomType.Shelter or RoomType.Security => "duty",
        RoomType.DroneBay => "drone",
        RoomType.RobotBay => "robot",
        RoomType.Gym => "treadmill",
        RoomType.Observatory => "eye",
        RoomType.Lab or RoomType.AlgaeLab => "refinery",
        RoomType.SeedVault or RoomType.Garden => "plantwall",
        RoomType.Chapel or RoomType.Meditation => "star",
        RoomType.PropellantTank => "fuel",
        RoomType.PumpRoom => "pump",
        RoomType.Substation => "power-panel",
        RoomType.GasStorage => "airtank",
        RoomType.SuppressionRoom => "extinguisher",
        RoomType.ElectronicsLab => "electronics",
        RoomType.Crusher or RoomType.CraneControl => "collector",
        RoomType.Theater => "projector",
        RoomType.MeetingRoom => "people",
        RoomType.Archive or RoomType.School => "bookshelf",
        RoomType.Navigation => "route",
        _ => "room",
    };

    /// <summary>설비 종류마다 다른 아이콘 (v15 모듈은 하는 일이 닮은 모양으로).</summary>
    public static string Furniture(FurnitureType t) => t switch
    {
        FurnitureType.Bed => "bed",
        FurnitureType.Cot => "cot",
        FurnitureType.Seat => "seat",
        FurnitureType.Table => "table",
        FurnitureType.MealDispenser => "dispenser",
        FurnitureType.Console or FurnitureType.NavComputer or FurnitureType.ReactorSimulator => "console",
        FurnitureType.ReactorCore => "reactor-core",
        FurnitureType.EngineCore => "engine-core",
        FurnitureType.OxygenGenerator => "o2-generator",
        FurnitureType.Workbench => "workbench",
        FurnitureType.Shelf or FurnitureType.ToolWall => "shelf",
        FurnitureType.MedBed => "medbed",
        FurnitureType.CoolantPump or FurnitureType.AirlockPump => "pump",
        FurnitureType.PowerPanel or FurnitureType.SurgeProtector => "power-panel",
        FurnitureType.Battery => "battery",
        FurnitureType.GrowBed or FurnitureType.NutrientDoser => "growbed",
        FurnitureType.WaterRecycler => "recycler",
        FurnitureType.Stove => "stove",
        FurnitureType.Fridge => "fridge",
        FurnitureType.SuitLocker or FurnitureType.SuitDryer => "locker",
        FurnitureType.AuxGenerator => "aux-generator",
        FurnitureType.Collector => "collector",
        FurnitureType.Refinery => "refinery",
        FurnitureType.DroneDock => "drone-dock",
        FurnitureType.MainComputer => "computer",
        FurnitureType.SensorArray or FurnitureType.SignalBooster => "sensor",
        FurnitureType.LedPanel or FurnitureType.EmergencyLight => "lamp",
        FurnitureType.HeatExchanger => "heat-exchanger",
        FurnitureType.CapacitorBank => "capacitor",
        FurnitureType.Scrubber or FurnitureType.AirPurifier or FurnitureType.Dehumidifier => "scrubber",
        FurnitureType.Fabricator => "fabricator",
        FurnitureType.RobotDock => "robot-dock",
        FurnitureType.SupplyCache => "cache",
        FurnitureType.PartTestBench or FurnitureType.CalibrationRig => "target",
        FurnitureType.Hoist => "collector",
        FurnitureType.MaintCart => "toolbox",
        FurnitureType.VibrationMonitor => "vibration",
        FurnitureType.ThermalCamera => "thermal-camera",
        FurnitureType.LeakDetector => "leak",
        FurnitureType.Oven => "oven",
        FurnitureType.Lathe => "lathe",
        FurnitureType.SolderStation => "electronics",
        FurnitureType.DiagnosticScanner => "diagnostic",
        FurnitureType.DishWasher or FurnitureType.WashingMachine => "washer",
        FurnitureType.Autoclave => "medkit",
        FurnitureType.DeconShower => "shower",
        FurnitureType.BlackoutCurtain or FurnitureType.NoiseDamper or FurnitureType.WhiteNoise => "sleep",
        FurnitureType.CoffeeMachine => "coffee",
        FurnitureType.Projector => "projector",
        FurnitureType.GameTable => "game-table",
        FurnitureType.Bookshelf => "bookshelf",
        FurnitureType.Aquarium => "aquarium",
        FurnitureType.Treadmill => "treadmill",
        FurnitureType.PlantWall => "plantwall",
        FurnitureType.FireBlanket => "extinguisher",
        _ => "parts",
    };

    /// <summary>물자: 도구 · 소모품은 저마다, 나머지는 재료 상자.</summary>
    public static string Item(ItemKind k) => k switch
    {
        ItemKind.Meal or ItemKind.Ration => "food",
        ItemKind.Produce => "room-hydroponics",
        ItemKind.Filter => "filter-item",
        ItemKind.Lubricant => "lubricant",
        ItemKind.Plate or ItemKind.Structure => "plate",
        ItemKind.Electronics or ItemKind.PowerController or ItemKind.ReactorControl or ItemKind.Sensor => "electronics",
        ItemKind.Motor or ItemKind.Bearing => "parts",
        ItemKind.Pump => "pump",
        ItemKind.MetalOre or ItemKind.Silicate or ItemKind.Carbon or ItemKind.Rare => "ore",
        ItemKind.Ice => "coolant",
        ItemKind.Cable => "cable",
        ItemKind.Fuse => "fuse",
        ItemKind.Sealant => "sealant",
        ItemKind.MedKit => "medkit",
        ItemKind.Suit => "suit",
        ItemKind.Extinguisher => "extinguisher",
        ItemKind.Fuel => "fuel",
        _ => "materials",
    };

    /// <summary>개인 물건마다 다른 아이콘.</summary>
    public static string Belonging(BelongingKind k) => k switch
    {
        BelongingKind.Book => "bookshelf",
        BelongingKind.Instrument => "instrument",
        BelongingKind.ChessSet => "chess",
        BelongingKind.Cards or BelongingKind.Puzzle => "game-table",
        BelongingKind.Sketchbook or BelongingKind.Journal => "log",
        BelongingKind.ModelKit or BelongingKind.Toolset => "toolbox",
        BelongingKind.Knitting => "yarn",
        BelongingKind.Camera => "camera",
        BelongingKind.GameDevice => "console",
        BelongingKind.TeaSet or BelongingKind.Mug => "rest",
        BelongingKind.CollectionBox => "materials",
        BelongingKind.PlantPot => "room-hydroponics",
        BelongingKind.Dumbbells => "treadmill",
        BelongingKind.YogaMat => "mat",
        BelongingKind.Headphones => "headphones",
        BelongingKind.Photo => "photo",
        BelongingKind.Blanket => "bed",
        BelongingKind.Artwork => "star",
        _ => "bag",
    };

    /// <summary>승무원의 지금 상태 → 아이콘 (목록 · 카드).</summary>
    public static string CrewState(CrewMember c, long tick)
    {
        if (c.Dead) return "dead";
        if (c.Down || c.CarriedBy != null) return "down";
        if (c.Mind.Panicking(tick)) return "panic";
        if (c.Vitals.Injury > 0.3f || c.Vitals.Health < 0.5f) return "injury";
        var a = c.Job?.Activity;
        return a switch
        {
            SleepActivity => "sleep",
            EatActivity or SharedMealActivity => "eat",
            DutyActivity or PatrolActivity => "duty",
            ChatActivity or VisitActivity => "social",
            RelaxActivity or HobbyActivity => "rest",
            EvacuateActivity or ShelterActivity => "danger",
            RecoverActivity or QuarantineActivity => "injury",
            PanicActivity => "panic",
            ChoresActivity or MendActivity or InspectActivity or PartTestActivity => "working",
            WanderActivity => "walk",
            null => c.Job == null ? "clock" : c.Job.Urgent ? "alert" : "working",
            _ => c.Job?.Urgent == true ? "alert" : "working",
        };
    }
}
