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

    /// <summary>v12.2 운항: 배가 앞으로 가는 빠르기 (화면 px/초, 가장 가까운 별 기준). 별이 뒤로 흐른다.</summary>
    public float Cruise { get; set; }
    /// <summary>v12.2 잔해 지대: 잔해 알갱이가 지나간다.</summary>
    public bool Debris { get; set; }
    private float _travel, _cruise;

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
        _cruise = Mathf.Lerp(_cruise, Cruise, 1f - Mathf.Exp(-3f * (float)delta)); // 배속이 바뀌면 부드럽게
        _travel += _cruise * (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;

        // 옅은 성운 (v10: 생성한 성운 무늬를 아주 느린 시차로)
        if (Textures.Nebula is Texture2D neb)
        {
            var shift = CameraPosition * 0.008f + new Vector2(_travel * 0.01f, 0f);
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
                this.Poly(pts, col.WithAlpha(0.11f * _storm));
            }
        }

        foreach (var s in _stars)
        {
            var shift = CameraPosition * (0.015f + 0.05f * s.Depth) + new Vector2(_travel * (0.15f + 0.85f * s.Depth * s.Depth), 0f);
            float x = Mathf.PosMod(s.Uv.X * size.X - shift.X, size.X);
            float y = Mathf.PosMod(s.Uv.Y * size.Y - shift.Y, size.Y);
            float a = s.Brightness * (0.75f + 0.25f * Mathf.Sin(_time * (0.6f + s.Depth) + s.Twinkle));
            var color = new Color(s.Tint.R, s.Tint.G, s.Tint.B, a);
            float streak = Mathf.Min(26f, _cruise * 0.05f * s.Depth * s.Depth);
            if (streak > 1.5f) DrawLine(new Vector2(x, y), new Vector2(x + streak, y), color.WithAlpha(a * 0.55f), s.Size * 0.8f, true); // 빨리 갈 때 별이 늘어진다
            if (s.Size < 1.1f) this.Box(new Rect2(x, y, 1f, 1f), color);
            else this.Circle(new Vector2(x, y), s.Size * 0.6f, color, true, -1f, true);
        }
        // v12.2 잔해 지대: 크고 작은 잔해가 앞에서 뒤로 지나간다 (돌면서)
        if (Debris)
        {
            for (int i = 0; i < 26; i++)
            {
                float depth = 0.3f + 0.7f * Mathf.PosMod(i * 0.618f, 1f);
                float u = Mathf.PosMod(i * 0.3719f - _travel * (0.0006f + 0.0024f * depth), 1f);
                float v = Mathf.PosMod(i * 0.2113f + 0.05f * Mathf.Sin(_time * 0.2f + i), 1f);
                var c = new Vector2(u * size.X, v * size.Y);
                float r = 1.2f + 3.5f * depth * Mathf.PosMod(i * 0.77f, 1f);
                float rot = _time * (0.3f + i * 0.05f) + i;
                var pts = new Vector2[5];
                for (int k = 0; k < 5; k++) pts[k] = c + new Vector2(Mathf.Cos(rot + k * 1.26f), Mathf.Sin(rot + k * 1.26f)) * r * (0.7f + 0.3f * Mathf.PosMod(i * k * 0.37f, 1f));
                this.Poly(pts, new Color(0.42f, 0.40f, 0.38f, 0.35f + 0.4f * depth));
            }
        }
    }

    private void DrawNebula(Vector2 center, float radius, Color color)
    {
        for (int k = 0; k < 6; k++)
            this.Circle(center, radius * (1f - k * 0.14f), new Color(color.R, color.G, color.B, 0.035f), true, -1f, true);
    }
}
