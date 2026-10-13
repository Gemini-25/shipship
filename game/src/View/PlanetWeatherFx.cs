using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 행성 날씨 연출 (화면 위 얇은 층 · 글 아래): 비 · 산성비 · 눈보라 · 모래 폭풍 · 강풍 · 폭염 아지랑이 · 번개 번쩍임 · 밤의 오로라.
//   알갱이 몇백 개를 매 프레임 옮겨 그린다 (60프레임에 가볍게). 게임이 멈추면 같이 멈춘다.
public partial class PlanetWeatherFx : Control
{
    private Main _main = null!;
    private readonly Vector2[] _p = new Vector2[320];
    private readonly float[] _v = new float[320];
    private int _strikes;
    private bool _seen;
    private float _flash, _time;
    private Vector2[] _bolt = Array.Empty<Vector2>();
    private readonly Random _r = new(17);

    public void Init(Main main)
    {
        _main = main;
        MouseFilter = MouseFilterEnum.Ignore;
        for (int i = 0; i < _p.Length; i++) { _p[i] = new Vector2(_r.NextSingle(), _r.NextSingle()); _v[i] = 0.6f + 0.8f * _r.NextSingle(); }
    }

    public override void _Process(double delta)
    {
        var pl = _main.Sim.Planet;
        float a = _main.Ground?.Alpha ?? 0f;
        Visible = a > 0.01f && pl.Here != null;
        if (!Visible) { _strikes = pl.Strikes; _seen = false; return; }
        if (!_seen) { _seen = true; _strikes = pl.Strikes; } // 막 보이기 시작했을 때는 지난 번개로 번쩍이지 않는다
        var s = GetViewportRect().Size;
        if (Size != s) Size = s;
        float dt = _main.Paused ? 0f : (float)delta;
        _time += dt;
        float wind = pl.Wind;
        var wx = pl.Weather;
        float fall = wx switch { PlanetWeather.Blizzard => 0.12f, PlanetWeather.DustStorm => 0.02f, _ => 0.9f };
        float side = wx switch { PlanetWeather.DustStorm => 0.9f + wind, PlanetWeather.Blizzard => 0.25f + wind * 0.6f, _ => 0.1f + wind * 0.6f };
        for (int i = 0; i < _p.Length; i++)
        {
            _p[i] += new Vector2(side, fall) * _v[i] * dt;
            if (_p[i].Y > 1.05f) _p[i].Y -= 1.1f;
            if (_p[i].X > 1.05f) _p[i].X -= 1.1f;
        }
        if (pl.Strikes != _strikes)
        {
            _strikes = pl.Strikes;
            _flash = 1f;
            float x = 0.2f + 0.6f * _r.NextSingle();
            var pts = new System.Collections.Generic.List<Vector2> { new(x * s.X, 0) };
            float y = 0f, cx = x * s.X;
            while (y < s.Y * (0.45f + 0.3f * _r.NextSingle())) { y += 18f + 30f * _r.NextSingle(); cx += (_r.NextSingle() - 0.5f) * 50f; pts.Add(new Vector2(cx, y)); }
            _bolt = pts.ToArray();
        }
        _flash = MathF.Max(0f, _flash - dt * 3.2f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var pl = _main.Sim.Planet;
        if (pl.Here is not Planet p) return;
        var s = Size;
        float a = _main.Ground?.Alpha ?? 1f;
        float pw = pl.WxPower;
        var wx = pl.Weather;
        // 대기 빛깔 (짙을수록)
        if (p.Pressure > 1f) DrawRect(new Rect2(Vector2.Zero, s), new Color(p.Hex2).WithAlpha(MathF.Min(0.1f, p.Pressure / 3000f + 0.03f) * a));
        int n(float k) => (int)(_p.Length * Math.Clamp(k, 0f, 1f));
        switch (wx)
        {
            case PlanetWeather.Rain or PlanetWeather.AcidRain or PlanetWeather.Thunder or PlanetWeather.Typhoon:
            {
                var col = wx == PlanetWeather.AcidRain ? new Color(0.8f, 0.95f, 0.45f, 0.45f * a) : new Color(0.75f, 0.85f, 1f, 0.4f * a);
                float len = 14f + 10f * pl.Wind;
                var dir = new Vector2(0.1f + pl.Wind * 0.6f, 0.9f).Normalized();
                for (int i = 0; i < n(0.35f + 0.65f * pw); i++) { var at = _p[i] * s; DrawLine(at, at + dir * len, col, 1.2f); }
                if (wx == PlanetWeather.Typhoon) DrawRect(new Rect2(Vector2.Zero, s), new Color(0.2f, 0.25f, 0.3f, 0.18f * pw * a));
                break;
            }
            case PlanetWeather.Blizzard:
                DrawRect(new Rect2(Vector2.Zero, s), new Color(0.9f, 0.95f, 1f, 0.12f * pw * a));
                for (int i = 0; i < n(0.4f + 0.6f * pw); i++) DrawRect(new Rect2(_p[i] * s, new Vector2(2.5f, 2.5f)), new Color(1f, 1f, 1f, 0.8f * a));
                break;
            case PlanetWeather.DustStorm:
            {
                var sand = new Color(p.Hex).Lerp(new Color("#c9a46a"), 0.6f);
                DrawRect(new Rect2(Vector2.Zero, s), sand.WithAlpha((0.18f + 0.3f * pw) * a));
                for (int i = 0; i < n(0.3f + 0.6f * pw); i++) { var at = _p[i] * s; DrawLine(at, at + new Vector2(6f + 10f * pw, 1f), sand.Lightened(0.2f).WithAlpha(0.55f * a), 1.5f); }
                break;
            }
            case PlanetWeather.Wind:
                for (int i = 0; i < n(0.12f * pw + 0.05f); i++) { var at = _p[i] * s; DrawLine(at, at + new Vector2(30f, 2f), new Color(1f, 1f, 1f, 0.12f * a), 1f); }
                break;
            case PlanetWeather.HeatWave:
                DrawRect(new Rect2(Vector2.Zero, s), new Color(1f, 0.55f, 0.2f, 0.08f * pw * a));
                for (int i = 0; i < 8; i++)
                {
                    float y = s.Y * (i + 0.5f) / 8f;
                    var pts = new Vector2[24];
                    for (int k = 0; k < 24; k++) pts[k] = new Vector2(s.X * k / 23f, y + MathF.Sin(_time * 3f + k * 0.7f + i) * 3f);
                    DrawPolyline(pts, new Color(1f, 0.8f, 0.6f, 0.05f * a), 2f);
                }
                break;
        }
        // 밤 · 강한 자기장: 오로라
        if (p.Has(PlanetFeature.Aurora) && _main.Ground!.Modulate.R < 0.6f)
            for (int b = 0; b < 3; b++)
            {
                var pts = new Vector2[30];
                for (int k = 0; k < 30; k++) pts[k] = new Vector2(s.X * k / 29f, 40f + b * 22f + MathF.Sin(_time * 0.6f + k * 0.35f + b) * 14f);
                DrawPolyline(pts, new Color(0.4f, 1f, 0.6f, 0.16f * a), 14f - b * 3f);
            }
        // 번개
        if (_flash > 0f)
        {
            DrawRect(new Rect2(Vector2.Zero, s), new Color(1f, 1f, 1f, 0.22f * _flash * _flash * a));
            if (_bolt.Length > 1) { DrawPolyline(_bolt, new Color(0.85f, 0.9f, 1f, _flash * a), 5f); DrawPolyline(_bolt, new Color(1f, 1f, 1f, _flash * a), 2f); }
        }
    }
}
