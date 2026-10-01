using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>보기 모드. 우주선 위에 시스템 상태를 덧씌운다.</summary>
public enum ViewMode { Normal, Power, Air, Temperature, Condition, Trace, Structure, Pipes, Sensors, Ambience }

public static class ViewModes
{
    public static readonly ViewMode[] All = (ViewMode[])Enum.GetValues(typeof(ViewMode));

    public static string Name(ViewMode m) => m switch
    {
        ViewMode.Normal => "일반",
        ViewMode.Power => "전력",
        ViewMode.Air => "공기",
        ViewMode.Temperature => "온도",
        ViewMode.Condition => "상태",
        ViewMode.Trace => "흔적",
        ViewMode.Structure => "구조",
        ViewMode.Pipes => "배관",
        ViewMode.Sensors => "감지기",
        ViewMode.Ambience => "환경",
        _ => m.ToString(),
    };
}

/// <summary>바닥·벽 텍스처. 투명 배경에 무늬만 있어서 방 색 위에 덮어 쓴다.</summary>
public static class Textures
{
    public static Texture2D? Plate { get; private set; }
    public static Texture2D? Grate { get; private set; }
    public static Texture2D? Tile { get; private set; }
    public static Texture2D? Soft { get; private set; }
    public static Texture2D? Hydro { get; private set; }
    public static Texture2D? Wall { get; private set; }
    // v10 그래픽 업그레이드
    public static Texture2D? Diamond { get; private set; }
    public static Texture2D? Bridge { get; private set; }
    public static Texture2D? Hazard { get; private set; }
    public static Texture2D? Light { get; private set; }
    public static Texture2D? Nebula { get; private set; }

    public static void Load()
    {
        Diamond = Try("res://assets/textures/floor_diamond.png");
        Bridge = Try("res://assets/textures/floor_bridge.png");
        Hazard = Try("res://assets/textures/floor_hazard.png");
        Light = Try("res://assets/textures/light.png");
        Nebula = Try("res://assets/textures/nebula.png");
        Plate = Try("res://assets/textures/floor_plate.png");
        Grate = Try("res://assets/textures/floor_grate.png");
        Tile = Try("res://assets/textures/floor_tile.png");
        Soft = Try("res://assets/textures/floor_soft.png");
        Hydro = Try("res://assets/textures/floor_hydro.png");
        Wall = Try("res://assets/textures/wall_plate.png");
    }

    private static Texture2D? Try(string path) => ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

    /// <summary>방 종류별 바닥 무늬와 진하기.</summary>
    public static (Texture2D? tex, float alpha) Floor(RoomType t) => t switch
    {
        RoomType.Engine or RoomType.Reactor or RoomType.Cooling or RoomType.Power or RoomType.LifeSupport => (Grate, 0.9f),
        RoomType.Galley or RoomType.Mess or RoomType.Medbay => (Tile, 1f),
        RoomType.Quarters or RoomType.Lounge => (Soft, 1f),
        RoomType.Hydroponics => (Hydro, 1f),
        RoomType.Storage or RoomType.Workshop => (Diamond ?? Plate, 1f),
        RoomType.Bridge or RoomType.Comms => (Bridge ?? Plate, 1f),
        RoomType.Airlock => (Hazard ?? Plate, 1f),
        _ => (Plate, 1f),
    };
}
