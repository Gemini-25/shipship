using Godot;

namespace ShipSim.View;

/// <summary>배경 별. 화면 좌표로 그리고, 카메라가 움직이면 아주 조금 따라 움직인다(시차).</summary>
public partial class Starfield : Node2D
{
    private struct Star
    {
        public Vector2 Uv;
        public float Size;
        public float Brightness;
        public float Twinkle;
        public float Depth;
        public Color Tint;
    }

    private Star[] _stars = System.Array.Empty<Star>();
    private float _time;

    /// <summary>카메라 월드 위치 (시차용).</summary>
    public Vector2 CameraPosition { get; set; }

    /// <summary>v11.2 태양 폭풍 세기 0~1 (오로라처럼 하늘이 일렁인다).</summary>
    public float Storm { get; set; }
    private float _storm;

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator { Seed = 1977 };
        Color[] tints = { new("#ffffff"), new("#cfe3ff"), new("#ffe9cf"), new("#d9d2ff") };
        _stars = new Star[360];
        for (int i = 0; i < _stars.Length; i++)
        {
            float depth = rng.Randf();
            _stars[i] = new Star
            {
                Uv = new Vector2(rng.Randf(), rng.Randf()),
                Depth = depth,
                Size = Mathf.Lerp(0.6f, 1.7f, depth * depth),
                Brightness = Mathf.Lerp(0.18f, 0.85f, depth),
                Twinkle = rng.RandfRange(0f, Mathf.Tau),
                Tint = tints[rng.RandiRange(0, tints.Length - 1)],
            };
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;

        // 옅은 성운 (v10: 생성한 성운 무늬를 아주 느린 시차로)
        if (Textures.Nebula is Texture2D neb)
        {
            var shift = CameraPosition * 0.008f;
            var r = new Rect2(-size * 0.15f - shift, size * 1.3f);
            DrawTextureRect(neb, r, false, new Color(1f, 1f, 1f, 0.9f));
        }
        else
        {
            DrawNebula(new Vector2(size.X * 0.18f, size.Y * 0.85f), size.X * 0.42f, new Color("#1a2b52"));
            DrawNebula(new Vector2(size.X * 0.82f, size.Y * 0.12f), size.X * 0.32f, new Color("#2a1f4a"));
        }

        // v11.2 태양 폭풍: 보라·초록 띠가 화면을 가로질러 일렁인다
        _storm = Mathf.MoveToward(_storm, Storm, 0.02f);
        if (_storm > 0.01f)
        {
            for (int band = 0; band < 3; band++)
            {
                var col = band == 1 ? new Color("#6cf0a0") : new Color("#b27cff");
                float baseY = size.Y * (0.2f + 0.28f * band);
                const int n = 40;
                var pts = new Vector2[n * 2];
                for (int i = 0; i < n; i++)
                {
                    float x = size.X * i / (n - 1f);
                    float y = baseY + Mathf.Sin(x * 0.004f + _time * (0.25f + 0.1f * band) + band * 2f) * 60f + Mathf.Sin(x * 0.011f - _time * 0.4f) * 18f;
                    float h = 50f + 30f * Mathf.Sin(x * 0.007f + _time * 0.6f + band);
                    pts[i] = new Vector2(x, y);
                    pts[n * 2 - 1 - i] = new Vector2(x, y + h);
                }
                DrawColoredPolygon(pts, col.WithAlpha(0.11f * _storm));
            }
        }

        foreach (var s in _stars)
        {
            var shift = CameraPosition * (0.015f + 0.05f * s.Depth);
            float x = Mathf.PosMod(s.Uv.X * size.X - shift.X, size.X);
            float y = Mathf.PosMod(s.Uv.Y * size.Y - shift.Y, size.Y);
            float a = s.Brightness * (0.75f + 0.25f * Mathf.Sin(_time * (0.6f + s.Depth) + s.Twinkle));
            var color = new Color(s.Tint.R, s.Tint.G, s.Tint.B, a);
            if (s.Size < 1.1f) DrawRect(new Rect2(x, y, 1f, 1f), color);
            else DrawCircle(new Vector2(x, y), s.Size * 0.6f, color, true, -1f, true);
        }
    }

    private void DrawNebula(Vector2 center, float radius, Color color)
    {
        for (int k = 0; k < 6; k++)
            DrawCircle(center, radius * (1f - k * 0.14f), new Color(color.R, color.G, color.B, 0.035f), true, -1f, true);
    }
}
