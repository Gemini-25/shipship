using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5a 생성기 그림(look_*.png)을 읽어 둔다. 에디터가 아직 가져오지 않은 저장소에서도 PNG 를 바로 읽어 쓴다.
/// 상태 겹치기는 셰이더 하나로: 꼭짓점 색 a = 양 · r = 진하기 · g = 부드러움 · b = 테(0~0.5) / 어둡게(0.5~1).
/// </summary>
public static class LookTextures
{
    public static Texture2D?[] Floors { get; } = new Texture2D?[LookSpec.FloorFiles.Length];
    public static Texture2D?[] Walls { get; } = new Texture2D?[LookSpec.WallFiles.Length];
    public static Texture2D?[] Overlays { get; } = new Texture2D?[LookSpec.OvFiles.Length];
    public static Texture2D? Decals { get; private set; }
    public static Texture2D? Particles { get; private set; }
    public static ShaderMaterial? OverlayMaterial { get; private set; }
    private static bool _loaded;

    /// <summary>바닥 · 벽 그림이 모두 있으면 새 재질 그림으로 그린다 (없으면 예전 그림).</summary>
    public static bool Ready { get; private set; }

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        bool ok = true;
        for (int i = 0; i < Floors.Length; i++) ok &= (Floors[i] = Try(LookSpec.FloorFiles[i])) != null;
        for (int i = 0; i < Walls.Length; i++) ok &= (Walls[i] = Try(LookSpec.WallFiles[i])) != null;
        for (int i = 0; i < Overlays.Length; i++) Overlays[i] = Try(LookSpec.OvFiles[i]);
        Decals = Try(LookSpec.DecalFile);
        Particles = Try(LookSpec.ParticleFile);
        Ready = ok;
        var sh = new Shader { Code = OverlayShader };
        OverlayMaterial = new ShaderMaterial { Shader = sh };
    }

    private static Texture2D? Try(string name)
    {
        string path = $"res://assets/textures/{name}.png";
        if (ResourceLoader.Exists(path)) return GD.Load<Texture2D>(path);
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
        if (img == null || img.IsEmpty()) return null;
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static int Mod4(int v) => ((v % LookSpec.Period) + LookSpec.Period) % LookSpec.Period;

    /// <summary>칸 좌표 → 텍스처 영역 (4×4칸 주기 — 이웃 칸끼리 무늬가 이어진다).</summary>
    public static Rect2 Region(Cell c) => new(Mod4(c.X) * LookSpec.CellPx, Mod4(c.Y) * LookSpec.CellPx, LookSpec.CellPx, LookSpec.CellPx);

    /// <summary>같은 영역의 UV (0~1).</summary>
    public static (Vector2 a, Vector2 b) Uv(Cell c)
    {
        float u = Mod4(c.X) / (float)LookSpec.Period, v = Mod4(c.Y) / (float)LookSpec.Period, s = 1f / LookSpec.Period;
        return (new Vector2(u, v), new Vector2(u + s, v + s));
    }

    public static Rect2 DecalRegion(LookSpec.Decal d) =>
        new((int)d % LookSpec.DecalCols * LookSpec.DecalPx, (int)d / LookSpec.DecalCols * LookSpec.DecalPx, LookSpec.DecalPx, LookSpec.DecalPx);

    public static Rect2 PartRegion(LookSpec.Part p) => new((int)p * LookSpec.PartPx, 0, LookSpec.PartPx, LookSpec.PartPx);

    /// <summary>겹치기 꼭짓점 색: 양 · 진하기 · 부드러움 · 테 / 어둡게.</summary>
    public static Color OvColor(float amount, (float maxA, float soft, float rim, float dark) st) =>
        new(st.maxA, st.soft, st.dark > 0f ? 0.5f + 0.5f * st.dark : 0.25f + 0.25f * Mathf.Clamp(st.rim, -1f, 1f), Mathf.Clamp(amount, 0f, 1f));

    private const string OverlayShader = @"
shader_type canvas_item;
// v16.5a 상태 겹치기: 텍스처 A = 드러나는 순서 · RGB = 덮인 색. 꼭짓점 색이 양 · 모양을 넘긴다 (칸 경계에서 양이 이어진다).
varying vec4 v_mod;
void vertex() { v_mod = COLOR; }
void fragment() {
    vec4 t = texture(TEXTURE, UV);
    float amt = v_mod.a;
    float soft = max(v_mod.g, 0.02);
    float x = t.a - (1.0 - amt);
    float a = clamp(x / soft, 0.0, 1.0) * v_mod.r;
    float rim = v_mod.b < 0.5 ? (v_mod.b - 0.25) * 4.0 : 0.0;
    float dark = v_mod.b >= 0.5 ? (v_mod.b - 0.5) * 2.0 : 0.0;
    float edge = 1.0 - clamp(x / (soft * 3.0), 0.0, 1.0);
    vec3 c = t.rgb * (1.0 - dark) + vec3(rim * edge);
    COLOR = vec4(c, a * step(0.001, amt));
}";
}
