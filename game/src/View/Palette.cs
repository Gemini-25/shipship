using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>화면 색. 어둡고 차분한 관측실 느낌 + 방마다 채도 낮은 강조색.</summary>
public static class Palette
{
    public static readonly Color Space = new("#06080c");
    public static readonly Color HullRim = new("#252d3a");
    public static readonly Color Wall = new("#171c25");
    public static readonly Color WallEdge = new("#343d4d");
    public static readonly Color Floor = new("#10141c");
    public static readonly Color GridLine = new(1f, 1f, 1f, 0.03f);

    public static readonly Color Text = new("#e6eaf2");
    public static readonly Color TextDim = new("#9aa3b5");
    public static readonly Color TextMuted = new("#5f6879");
    public static readonly Color Panel = new(0.047f, 0.063f, 0.09f, 0.95f);
    public static readonly Color PanelBorder = new(1f, 1f, 1f, 0.07f);
    public static readonly Color Warning = new("#ff9a6b");
    public static readonly Color Danger = new("#ff5c6c");
    public static readonly Color Good = new("#6ee7b7");
    public static readonly Color Accent = new("#7cc4ff");

    public static readonly Color NeedFood = new("#f2b134");
    public static readonly Color NeedRest = new("#7cc4ff");
    public static readonly Color NeedStress = new("#ff7a85");
    public static readonly Color NeedSocial = new("#c79be0");
    public static readonly Color VitalHealth = new("#6ee7b7");
    public static readonly Color VitalOxygen = new("#5ec8e6");

    public static Color Room(RoomType t) => t switch
    {
        RoomType.Corridor => new Color("#6b7486"),
        RoomType.Bridge => new Color("#5aa9e6"),
        RoomType.Engine => new Color("#e07a5f"),
        RoomType.Reactor => new Color("#f2b134"),
        RoomType.Cooling => new Color("#7fdfff"),
        RoomType.Power => new Color("#f5d547"),
        RoomType.LifeSupport => new Color("#4fd1c5"),
        RoomType.Workshop => new Color("#c9a66b"),
        RoomType.Storage => new Color("#8d93a6"),
        RoomType.Galley => new Color("#ef8f5a"),
        RoomType.Mess => new Color("#e9b56b"),
        RoomType.Hydroponics => new Color("#a3d95b"),
        RoomType.Airlock => new Color("#aab3c5"),
        RoomType.Quarters => new Color("#8e9be3"),
        RoomType.Medbay => new Color("#ef8fa6"),
        RoomType.Lounge => new Color("#c79be0"),
        RoomType.Comms => new Color("#6ee7b7"),
        _ => RoomCatalog.Of(t) is RoomSpec spec ? new Color(spec.Color) : new Color("#6b7486"),
    };

    public static Color RoomFloor(RoomType t) => Floor.Lerp(Room(t), t == RoomType.Corridor ? 0.06f : 0.1f);

    private static readonly Color[] CrewColors =
    {
        new("#ffb454"), new("#6ee7b7"), new("#7cc4ff"), new("#f0a6f7"),
        new("#ffd166"), new("#b8e986"), new("#ff8fa3"), new("#a0c4ff"),
        // v10.1: 여덟 명 넘게 태운 배
        new("#ffa07a"), new("#5eead4"), new("#c4b5fd"), new("#fde68a"),
        new("#86efac"), new("#f9a8d4"), new("#93c5fd"), new("#fdba74"),
    };

    /// <summary>v10.7: 16명이 넘으면 같은 색을 밝기만 바꿔 돌려 쓴다 (30명 배에서도 겹치지 않게).</summary>
    public static Color Crew(int id)
    {
        var c = CrewColors[id % CrewColors.Length];
        int round = id / CrewColors.Length;
        return round == 0 ? c : round % 2 == 1 ? c.Darkened(0.28f) : c.Lightened(0.3f);
    }

    public static Color Item(ItemKind k) => k switch
    {
        ItemKind.Produce => new Color("#8fd65a"),
        ItemKind.Meal => new Color("#f2b134"),
        ItemKind.Ration => new Color("#c9a66b"),
        ItemKind.Filter => new Color("#dfe6ee"),
        ItemKind.Lubricant => new Color("#e0b64a"),
        ItemKind.Plate => new Color("#9aa6b5"),
        ItemKind.Structure => new Color("#7f8a99"),
        ItemKind.Electronics => new Color("#6fd3b0"),
        ItemKind.Motor => new Color("#c0a0ff"),
        ItemKind.Pump => new Color("#6cb8ff"),
        ItemKind.Bearing => new Color("#b8c2cf"),
        ItemKind.PowerController => new Color("#ffd27a"),
        ItemKind.Sensor => new Color("#8ee6ff"),
        ItemKind.ReactorControl => new Color("#ff9ad0"),
        ItemKind.MetalOre => new Color("#8a7d6b"),
        ItemKind.Silicate => new Color("#b9a98f"),
        ItemKind.Carbon => new Color("#5a5550"),
        ItemKind.Ice => new Color("#cfeaff"),
        ItemKind.Rare => new Color("#e8b8ff"),
        ItemKind.Cable => new Color("#ff9a6b"),
        ItemKind.Fuse => new Color("#f5d547"),
        ItemKind.Sealant => new Color("#7cc4ff"),
        ItemKind.MedKit => new Color("#ef8fa6"),
        ItemKind.Suit => new Color("#dfe6ee"),
        ItemKind.Extinguisher => new Color("#ff5c4c"),
        ItemKind.Fuel => new Color("#c8783a"),
        _ => new Color("#9aa6b5"),
    };

    /// <summary>0 = 좋음(초록) ~ 1 = 나쁨(빨강).</summary>
    public static Color Severity(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t < 0.5f ? Good.Lerp(new Color("#f5d547"), t * 2f) : new Color("#f5d547").Lerp(Danger, (t - 0.5f) * 2f);
    }

    public static Color WithAlpha(this Color c, float a) => new(c.R, c.G, c.B, a);
}
