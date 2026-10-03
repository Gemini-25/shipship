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

    /// <summary>방 종류마다 다른 아이콘 — 70종 모두 저마다 (같은 무리여도 실루엣이 다르다: 함교 ↔ 예비 함교 · 창고 ↔ 화물칸 …).</summary>
    public static string Room(RoomType t) => t switch
    {
        RoomType.Corridor => "room-corridor",
        RoomType.Bridge => "room-bridge",
        RoomType.BackupBridge => "room-backup-bridge",
        RoomType.Engine => "room-engine",
        RoomType.ShuttleBay => "room-shuttle",
        RoomType.Reactor => "room-reactor",
        RoomType.Centrifuge => "room-centrifuge",
        RoomType.Cooling => "room-cooling",
        RoomType.HeatStorage => "room-heat-storage",
        RoomType.Power => "room-power",
        RoomType.FuelCell => "room-fuel-cell",
        RoomType.LifeSupport => "room-lifesupport",
        RoomType.HvacRoom => "room-hvac",
        RoomType.Workshop => "room-workshop",
        RoomType.PartsPrep => "room-parts-prep",
        RoomType.WeldingShop => "room-welding",
        RoomType.Storage => "room-storage",
        RoomType.Cargo => "room-cargo",
        RoomType.Galley => "room-galley",
        RoomType.Mess => "room-mess",
        RoomType.Hydroponics => "room-hydroponics",
        RoomType.ProteinFarm => "room-protein-farm",
        RoomType.Airlock => "room-airlock",
        RoomType.DockingBay => "room-docking",
        RoomType.EvaPrep => "room-eva-prep",
        RoomType.Quarters => "room-quarters",
        RoomType.PrivateCabins => "room-cabins",
        RoomType.WaterWallCabin => "room-waterwall",
        RoomType.QuietQuarters => "sleep",
        RoomType.Medbay => "room-medbay",
        RoomType.Triage => "room-triage",
        RoomType.Hyperbaric => "room-hyperbaric",
        RoomType.Lounge => "room-lounge",
        RoomType.Comms => "room-comms",
        RoomType.WaterPlant => "room-water-plant",
        RoomType.Recycling => "recycler",
        RoomType.Laundry => "washer",
        RoomType.Freezer => "fridge",
        RoomType.BatteryRoom => "battery",
        RoomType.Morgue => "dead",
        RoomType.EscapeBay => "capsule",
        RoomType.ServerRoom => "computer",
        RoomType.Calibration => "target",
        RoomType.Decon => "shower",
        RoomType.Quarantine => "sick",
        RoomType.QuarantineLock => "room-quarantine-lock",
        RoomType.Shelter => "room-shelter",
        RoomType.Security => "room-security",
        RoomType.DroneBay => "drone",
        RoomType.RobotBay => "robot",
        RoomType.Gym => "treadmill",
        RoomType.Observatory => "eye",
        RoomType.Lab => "refinery",
        RoomType.AlgaeLab => "room-algae-lab",
        RoomType.SeedVault => "room-seed-vault",
        RoomType.Garden => "room-garden",
        RoomType.Chapel => "room-chapel",
        RoomType.Meditation => "room-meditation",
        RoomType.PropellantTank => "fuel",
        RoomType.PumpRoom => "pump",
        RoomType.Substation => "power-panel",
        RoomType.GasStorage => "airtank",
        RoomType.SuppressionRoom => "extinguisher",
        RoomType.ElectronicsLab => "electronics",
        RoomType.Crusher => "room-crusher",
        RoomType.CraneControl => "room-crane",
        RoomType.Theater => "projector",
        RoomType.MeetingRoom => "people",
        RoomType.Archive => "bookshelf",
        RoomType.School => "room-school",
        RoomType.Navigation => "route",
        RoomType.ComputerRoom => "room-computer-core", // v16.24
        RoomType.MushroomFarm => "room-mushroom-farm",
        _ => "room",
    };

    /// <summary>설비 종류마다 다른 아이콘 — 70종 모두 저마다 실루엣이 다르다 (같은 틀에 글자만 바꾼 것 없음 · --uitest가 확인).</summary>
    public static string Furniture(FurnitureType t) => t switch
    {
        FurnitureType.Bed => "bed",
        FurnitureType.Cot => "cot",
        FurnitureType.Seat => "seat",
        FurnitureType.Table => "table",
        FurnitureType.MealDispenser => "dispenser",
        FurnitureType.Console => "console",
        FurnitureType.NavComputer => "nav-computer",
        FurnitureType.ReactorSimulator => "reactor-sim",
        FurnitureType.ReactorCore => "reactor-core",
        FurnitureType.EngineCore => "engine-core",
        FurnitureType.OxygenGenerator => "o2-generator",
        FurnitureType.Workbench => "workbench",
        FurnitureType.Shelf => "shelf",
        FurnitureType.ToolWall => "tool-wall",
        FurnitureType.MedBed => "medbed",
        FurnitureType.CoolantPump => "pump",
        FurnitureType.AirlockPump => "airlock-pump",
        FurnitureType.PowerPanel => "power-panel",
        FurnitureType.SurgeProtector => "surge-protector",
        FurnitureType.Battery => "battery",
        FurnitureType.GrowBed => "growbed",
        FurnitureType.NutrientDoser => "nutrient-doser",
        FurnitureType.WaterRecycler => "recycler",
        FurnitureType.Stove => "stove",
        FurnitureType.Fridge => "fridge",
        FurnitureType.SuitLocker => "locker",
        FurnitureType.SuitDryer => "suit-dryer",
        FurnitureType.AuxGenerator => "aux-generator",
        FurnitureType.Collector => "collector",
        FurnitureType.Refinery => "refinery",
        FurnitureType.DroneDock => "drone-dock",
        FurnitureType.MainComputer => "computer",
        FurnitureType.SensorArray => "sensor",
        FurnitureType.SignalBooster => "signal-booster",
        FurnitureType.LedPanel => "led-panel",
        FurnitureType.EmergencyLight => "emergency-light",
        FurnitureType.HeatExchanger => "heat-exchanger",
        FurnitureType.CapacitorBank => "capacitor",
        FurnitureType.Scrubber => "scrubber",
        FurnitureType.AirPurifier => "air-purifier",
        FurnitureType.Dehumidifier => "dehumidifier",
        FurnitureType.Fabricator => "fabricator",
        FurnitureType.RobotDock => "robot-dock",
        FurnitureType.SupplyCache => "cache",
        FurnitureType.PartTestBench => "part-test-bench",
        FurnitureType.CalibrationRig => "calibration-rig",
        FurnitureType.Hoist => "hoist",
        FurnitureType.MaintCart => "maint-cart",
        FurnitureType.VibrationMonitor => "vibration",
        FurnitureType.ThermalCamera => "thermal-camera",
        FurnitureType.LeakDetector => "leak",
        FurnitureType.Oven => "oven",
        FurnitureType.Lathe => "lathe",
        FurnitureType.SolderStation => "solder-station",
        FurnitureType.DiagnosticScanner => "diagnostic",
        FurnitureType.DishWasher => "dishwasher",
        FurnitureType.WashingMachine => "washer",
        FurnitureType.Autoclave => "autoclave",
        FurnitureType.DeconShower => "shower",
        FurnitureType.BlackoutCurtain => "blackout-curtain",
        FurnitureType.NoiseDamper => "noise-damper",
        FurnitureType.WhiteNoise => "white-noise",
        FurnitureType.CoffeeMachine => "coffee",
        FurnitureType.Projector => "projector",
        FurnitureType.GameTable => "game-table",
        FurnitureType.Bookshelf => "bookshelf",
        FurnitureType.Aquarium => "aquarium",
        FurnitureType.Treadmill => "treadmill",
        FurnitureType.PlantWall => "plantwall",
        FurnitureType.FireBlanket => "fire-blanket",
        FurnitureType.Fermenter => "fermenter",
        FurnitureType.BreadOven => "bread-oven",
        FurnitureType.SpiceRack => "spice-rack",
        FurnitureType.IceMaker => "ice-maker",
        FurnitureType.PlantRack => "plant-rack",
        FurnitureType.CatTower => "cat-tower",
        FurnitureType.PestTrap => "pest-trap",
        FurnitureType.InsectFarm => "insect-farm",
        FurnitureType.GreaseTrap => "grease-trap",
        FurnitureType.Compactor => "compactor",
        FurnitureType.Composter => "composter",
        FurnitureType.GreywaterFilter => "greywater-filter",
        FurnitureType.GrabRail => "grab-rail",
        FurnitureType.CargoNet => "cargo-net",
        FurnitureType.CrashSeat => "crash-seat",
        FurnitureType.MagBootRack => "magboot-rack",
        FurnitureType.ServerRack => "server-rack",
        FurnitureType.RecorderVault => "recorder-vault",
        FurnitureType.ListeningPost => "listening-post",
        FurnitureType.MeetingBoard => "meeting-board",
        FurnitureType.MemorialWall => "memorial-wall",
        FurnitureType.MusicCorner => "music-corner",
        FurnitureType.LabStill => "lab-still",
        FurnitureType.ClothesRack => "clothes-rack",
        FurnitureType.SewingMachine => "sewing-machine",
        FurnitureType.EyeWash => "eye-wash",
        FurnitureType.OxygenMaskBox => "oxygen-mask-box",
        FurnitureType.HeatSuitRack => "heat-suit-rack",
        FurnitureType.DockClampPanel => "dock-clamp-panel",
        FurnitureType.Telescope => "telescope",
        // 의료 1차
        FurnitureType.OperatingTable => "operating-table", FurnitureType.SurgicalLamp => "surgical-lamp", FurnitureType.AnesthesiaMachine => "anesthesia-machine",
        FurnitureType.BloodFridge => "blood-fridge", FurnitureType.MedCabinet => "med-cabinet",
        _ => "parts",
    };

    /// <summary>승무원 기술마다 다른 아이콘 (카드 · 상태 탭에서 이름 옆).</summary>
    public static string Skill(ShipSim.Core.Skill s) => s switch
    {
        ShipSim.Core.Skill.Engineering => "skill-engineering",
        ShipSim.Core.Skill.Electrical => "skill-electrical",
        ShipSim.Core.Skill.Mechanics => "skill-mechanics",
        ShipSim.Core.Skill.Medicine => "skill-medicine",
        ShipSim.Core.Skill.Botany => "skill-botany",
        ShipSim.Core.Skill.Cooking => "skill-cooking",
        ShipSim.Core.Skill.Piloting => "skill-piloting",
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
        // 의료 1차
        ItemKind.Painkiller => "painkiller", ItemKind.Antibiotic => "antibiotic", ItemKind.Anesthetic => "anesthetic",
        ItemKind.BloodSubstitute => "blood-substitute", ItemKind.MedHerb => "med-herb",
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
