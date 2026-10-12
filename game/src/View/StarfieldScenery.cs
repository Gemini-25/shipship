using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 연출: 창밖 풍경 — 항로 구간마다 배경이 다르다. 먼 것(구름 · 띠 · 오로라)은 별 뒤에, 가까운 것(행성 · 혜성 · 정거장 · 잔해 · 다른 배)은 별 앞에.
//   구간이 바뀌면 3초에 걸쳐 섞인다.
public partial class Starfield
{
    /// <summary>지금 구간 (LegKind 번호).</summary>
    public int Leg { get; set; }
    private int _legShown = -1, _legPrev = -1;
    private float _legFade = 1f;

    private void StepScenery(float dt)
    {
        if (_legShown < 0) { _legShown = Leg; _legFade = 1f; }
        if (Leg != _legShown) { _legPrev = _legShown; _legShown = Leg; _legFade = 0f; }
        _legFade = Mathf.Min(1f, _legFade + dt / 3f);
    }

    private void DrawSceneryLayer(Vector2 size, bool near)
    {
        if (_legPrev >= 0 && _legFade < 1f) DrawLeg((LegKind)_legPrev, size, near, 1f - _legFade);
        DrawLeg((LegKind)_legShown, size, near, _legFade);
    }

    private Vector2 Par(float k) => CameraPosition * k + new Vector2(_travel * k * 0.6f, 0f);

    private void DrawLeg(LegKind k, Vector2 size, bool near, float a)
    {
        if (a <= 0.01f) return;
        switch (k)
        {
            case LegKind.Nebula when !near:
                Cloud(size * new Vector2(0.3f, 0.4f) - Par(0.01f), size.X * 0.5f, new Color("#7a3fb0"), 0.06f * a);
                Cloud(size * new Vector2(0.75f, 0.65f) - Par(0.012f), size.X * 0.45f, new Color("#2fa7a0"), 0.05f * a);
                Cloud(size * new Vector2(0.55f, 0.2f) - Par(0.008f), size.X * 0.35f, new Color("#c05a9a"), 0.04f * a);
                break;
            case LegKind.SporeCloud when !near:
                Cloud(size * new Vector2(0.5f, 0.5f) - Par(0.01f), size.X * 0.6f, new Color("#4fa83a"), 0.045f * a);
                Specks(size, 60, new Color(0.6f, 1f, 0.5f), 0.5f * a, 0.002f, 1.4f);
                break;
            case LegKind.RadiationBelt or LegKind.MagneticField when !near:
                Aurora(size, k == LegKind.MagneticField ? new Color("#6cf0c0") : new Color("#9ad84a"), (k == LegKind.MagneticField ? 0.12f : 0.07f) * a);
                break;
            case LegKind.SolarWind when !near:
                for (int i = 0; i < 40; i++)
                {
                    float y = Mathf.PosMod(i * 0.137f, 1f) * size.Y;
                    float x = Mathf.PosMod(i * 0.311f - _time * (0.15f + 0.1f * Mathf.PosMod(i * 0.7f, 1f)), 1f) * size.X;
                    DrawLine(new Vector2(x, y), new Vector2(x + 90f, y), new Color(1f, 0.85f, 0.5f, 0.12f * a), 1.5f);
                }
                break;
            case LegKind.DeepVoid when !near:
                DrawRect(new Rect2(Vector2.Zero, size), new Color(0f, 0f, 0f, 0.5f * a)); // 별이 거의 없다 — 덮어 어둡게
                break;
            case LegKind.Perihelion when !near:
            {
                var sun = new Vector2(size.X * 1.02f, size.Y * 0.15f) - Par(0.004f);
                for (int i = 0; i < 9; i++) this.Circle(sun, size.Y * (0.9f - i * 0.09f), new Color(1f, 0.85f, 0.5f, 0.05f * a), true, -1f, true);
                this.Circle(sun, size.Y * 0.16f, new Color(1f, 0.97f, 0.85f, 0.85f * a), true, -1f, true);
                break;
            }
            case LegKind.Cruise when !near:
                Band(size, new Color("#9fb4ff"), 0.035f * a);
                break;
            case LegKind.AsteroidBelt or LegKind.Narrows when near:
                Rocks(size, k == LegKind.Narrows ? 34 : 22, a, k == LegKind.Narrows);
                break;
            case LegKind.GasGiant when near:
            {
                var c = new Vector2(size.X * 0.86f, size.Y * 1.02f) - Par(0.006f);
                float r = size.Y * 0.62f;
                for (int i = 0; i < 5; i++) this.Circle(c, r * (1.12f - i * 0.025f), new Color(1f, 0.72f, 0.42f, 0.035f * a), true, -1f, true); // 대기 번짐
                this.Circle(c, r, new Color(0.6f, 0.4f, 0.25f, a), true, -1f, true);
                for (int b = 0; b < 9; b++)
                {
                    float y0 = -r + b * r * 2f / 9f, y1 = y0 + r * 2f / 9f * 0.62f;
                    var col = b % 3 == 0 ? new Color(0.86f, 0.64f, 0.42f) : b % 3 == 1 ? new Color(0.48f, 0.3f, 0.2f) : new Color(0.72f, 0.5f, 0.32f);
                    this.Poly(BandPoly(c, r, y0 + Mathf.Sin(_time * 0.05f + b) * 3f, y1), col.WithAlpha(0.75f * a));
                }
                this.Circle(c + new Vector2(-r * 0.3f, -r * 0.42f), r * 0.07f, new Color(0.82f, 0.38f, 0.28f, 0.85f * a), true, -1f, true); // 큰 소용돌이
                // 밤쪽 그늘: 오른쪽 아래로 갈수록 어둡게 (원 안에서만)
                for (int i = 0; i < 6; i++) this.Poly(BandPoly(c, r, -r + i * 0.0f, r, 0.35f + i * 0.11f), new Color(0f, 0f, 0f, 0.12f * a));
                break;
            }
            case LegKind.IceRing when near:
                for (int i = 0; i < 160; i++)
                {
                    float t = Mathf.PosMod(i * 0.0137f + _time * 0.004f, 1f);
                    float x = t * size.X * 1.2f - size.X * 0.1f;
                    float y = size.Y * 0.55f + (x - size.X * 0.5f) * -0.18f + Mathf.Sin(i * 1.7f) * 18f;
                    float tw = 0.5f + 0.5f * Mathf.Sin(_time * 3f + i);
                    this.Box(new Rect2(x, y, 2f, 2f), new Color(0.85f, 0.95f, 1f, (0.25f + 0.5f * tw) * a));
                }
                break;
            case LegKind.CometTrail when near:
            {
                var head = new Vector2(size.X * 0.68f, size.Y * 0.28f) - Par(0.01f) + new Vector2(Mathf.Sin(_time * 0.05f) * 20f, 0f);
                for (int i = 0; i < 26; i++)
                {
                    float t = i / 26f;
                    this.Circle(head + new Vector2(-t * size.X * 0.45f, t * 40f), 18f * (1f - t) + 4f, new Color(0.55f, 0.8f, 1f, 0.07f * (1f - t) * a), true, -1f, true);
                }
                this.Circle(head, 5f, new Color(0.95f, 1f, 1f, 0.95f * a), true, -1f, true);
                break;
            }
            case LegKind.Pulsar when near:
            {
                var p = new Vector2(size.X * 0.22f, size.Y * 0.22f) - Par(0.003f);
                float beat = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(_time * 7f), 6f);
                for (int i = 0; i < 5; i++) this.Circle(p, 6f + i * 10f * (0.5f + beat), new Color(0.75f, 0.85f, 1f, 0.09f * a * (0.4f + beat)), true, -1f, true);
                float ang = _time * 0.6f;
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                DrawLine(p - d * size.X, p + d * size.X, new Color(0.7f, 0.85f, 1f, 0.08f * a), 6f);
                this.Circle(p, 4f, new Color(1f, 1f, 1f, a), true, -1f, true);
                break;
            }
            case LegKind.Port when near:
            {
                var c = new Vector2(size.X * 0.08f, size.Y * 1.2f) - Par(0.004f);
                this.Circle(c, size.Y * 0.55f, new Color(0.25f, 0.45f, 0.65f, a), true, -1f, true); // 행성 가장자리
                this.Circle(c, size.Y * 0.57f, new Color(0.5f, 0.75f, 1f, 0.12f * a), false, 4f, true);
                Station(new Vector2(size.X * 0.8f, size.Y * 0.25f) - Par(0.012f), a);
                break;
            }
            case LegKind.TradeLane or LegKind.PatrolLane when near:
                for (int i = 0; i < (k == LegKind.TradeLane ? 9 : 6); i++)
                {
                    float x = Mathf.PosMod(i * 0.23f + _time * (k == LegKind.TradeLane ? 0.008f : 0.002f), 1f) * size.X;
                    float y = size.Y * (0.12f + 0.08f * (i % 3));
                    bool blink = Mathf.Sin(_time * 3f + i * 1.3f) > 0.4f;
                    var col = k == LegKind.PatrolLane ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.92f, 0.7f);
                    this.Circle(new Vector2(x, y), blink ? 3f : 2f, col.WithAlpha((blink ? 0.95f : 0.4f) * a), true, -1f, true);
                    if (k == LegKind.TradeLane) DrawLine(new Vector2(x - 14f, y), new Vector2(x + 4f, y), new Color(0.6f, 0.65f, 0.75f, 0.5f * a), 3f);
                }
                break;
            case LegKind.Derelict or LegKind.Graveyard when near:
                for (int i = 0; i < (k == LegKind.Graveyard ? 6 : 1); i++)
                {
                    var c = new Vector2(size.X * (0.7f - i * 0.12f), size.Y * (0.25f + 0.11f * (i % 3))) - Par(0.01f + i * 0.002f);
                    Wreck(c, 40f - i * 4f, _time * 0.05f + i, a);
                }
                break;
            case LegKind.Lagrange when near:
                this.Circle(new Vector2(size.X * 0.15f, size.Y * 0.2f) - Par(0.003f), 26f, new Color(0.7f, 0.6f, 0.5f, 0.9f * a), true, -1f, true);
                this.Circle(new Vector2(size.X * 0.88f, size.Y * 0.8f) - Par(0.003f), 16f, new Color(0.5f, 0.6f, 0.75f, 0.9f * a), true, -1f, true);
                break;
        }
    }

    /// <summary>원(중심 c · 반지름 r) 안의 가로 띠 y0~y1 (원 둘레를 따라 잘린 다각형) · from이 0보다 크면 오른쪽 그 비율부터만 (그늘).</summary>
    private static Vector2[] BandPoly(Vector2 c, float r, float y0, float y1, float from = 0f)
    {
        const int n = 10;
        var pts = new Vector2[n * 2];
        for (int i = 0; i < n; i++)
        {
            float y = Mathf.Lerp(y0, y1, i / (n - 1f));
            float half = Mathf.Sqrt(Mathf.Max(0f, r * r - y * y));
            float left = from > 0f ? Mathf.Lerp(-half, half, from) : -half;
            pts[i] = c + new Vector2(left, y);
            pts[n * 2 - 1 - i] = c + new Vector2(half, y);
        }
        return pts;
    }

    private void Cloud(Vector2 c, float r, Color col, float alpha)
    {
        for (int i = 0; i < 7; i++) this.Circle(c + new Vector2(Mathf.Sin(i * 1.9f) * r * 0.2f, Mathf.Cos(i * 2.3f) * r * 0.15f), r * (1f - i * 0.11f), col.WithAlpha(alpha), true, -1f, true);
    }

    private void Band(Vector2 size, Color col, float alpha)
    {
        var shift = Par(0.004f);
        for (int i = 0; i < 6; i++)
        {
            float w = size.Y * (0.32f - i * 0.045f);
            DrawLine(new Vector2(-50f, size.Y * 0.9f - shift.Y), new Vector2(size.X + 50f, size.Y * 0.1f - shift.Y), col.WithAlpha(alpha), w);
        }
    }

    private void Aurora(Vector2 size, Color col, float alpha)
    {
        for (int band = 0; band < 2; band++)
        {
            const int n = 36;
            var pts = new Vector2[n * 2];
            float baseY = size.Y * (0.3f + 0.35f * band);
            for (int i = 0; i < n; i++)
            {
                float x = size.X * i / (n - 1f);
                float y = baseY + Mathf.Sin(x * 0.005f + _time * 0.2f + band) * 40f;
                pts[i] = new Vector2(x, y);
                pts[n * 2 - 1 - i] = new Vector2(x, y + 30f + 20f * Mathf.Sin(x * 0.01f + _time * 0.5f));
            }
            this.Poly(pts, col.WithAlpha(alpha));
        }
    }

    private void Specks(Vector2 size, int n, Color col, float alpha, float speed, float r)
    {
        for (int i = 0; i < n; i++)
        {
            float x = Mathf.PosMod(i * 0.173f - _travel * speed * (0.5f + Mathf.PosMod(i * 0.37f, 1f)), 1f) * size.X;
            float y = Mathf.PosMod(i * 0.291f, 1f) * size.Y;
            this.Circle(new Vector2(x, y), r, col.WithAlpha(alpha * (0.4f + 0.6f * Mathf.PosMod(i * 0.61f, 1f))), true, -1f, true);
        }
    }

    private void Rocks(Vector2 size, int n, float a, bool walls)
    {
        for (int i = 0; i < n; i++)
        {
            float depth = 0.3f + 0.7f * Mathf.PosMod(i * 0.618f, 1f);
            float u = Mathf.PosMod(i * 0.3719f - _travel * (0.0003f + 0.0012f * depth), 1f);
            float v = walls ? (i % 2 == 0 ? 0.06f : 0.94f) + 0.06f * Mathf.Sin(i * 1.3f) : Mathf.PosMod(i * 0.2113f, 1f);
            var c = new Vector2(u * size.X, v * size.Y);
            float r = (walls ? 18f : 6f) + (walls ? 30f : 16f) * depth * Mathf.PosMod(i * 0.77f, 1f);
            float rot = _time * 0.05f * (1 + i % 3) + i;
            var pts = new Vector2[7];
            for (int k = 0; k < 7; k++) pts[k] = c + new Vector2(Mathf.Cos(rot + k * 0.9f), Mathf.Sin(rot + k * 0.9f)) * r * (0.75f + 0.25f * Mathf.PosMod(i * k * 0.37f, 1f));
            this.Poly(pts, new Color(0.24f + 0.1f * depth, 0.22f + 0.08f * depth, 0.2f + 0.06f * depth, (0.45f + 0.5f * depth) * a));
        }
    }

    private void Station(Vector2 c, float a)
    {
        var hull = new Color(0.55f, 0.6f, 0.68f, 0.9f * a);
        DrawRect(new Rect2(c + new Vector2(-40f, -4f), new Vector2(80f, 8f)), hull);
        DrawRect(new Rect2(c + new Vector2(-4f, -30f), new Vector2(8f, 60f)), hull);
        this.Circle(c, 14f, hull, true, -1f, true);
        DrawRect(new Rect2(c + new Vector2(-62f, -14f), new Vector2(20f, 28f)), new Color(0.25f, 0.35f, 0.6f, 0.9f * a));
        DrawRect(new Rect2(c + new Vector2(42f, -14f), new Vector2(20f, 28f)), new Color(0.25f, 0.35f, 0.6f, 0.9f * a));
        for (int i = 0; i < 4; i++)
        {
            bool on = Mathf.Sin(_time * 2.5f + i * 1.6f) > 0.3f;
            var p = c + new Vector2(i % 2 == 0 ? -40f : 40f, i < 2 ? -4f : 4f);
            this.Circle(p, 2.5f, (i % 2 == 0 ? new Color(1f, 0.3f, 0.3f) : new Color(0.4f, 1f, 0.5f)).WithAlpha((on ? 1f : 0.25f) * a), true, -1f, true);
        }
    }

    private void Wreck(Vector2 c, float s, float rot, float a)
    {
        var dir = new Vector2(Mathf.Cos(rot), Mathf.Sin(rot));
        var n = new Vector2(-dir.Y, dir.X);
        var pts = new[] { c + dir * s, c + dir * s * 0.3f + n * s * 0.35f, c - dir * s * 0.6f + n * s * 0.25f, c - dir * s * 0.8f - n * s * 0.1f, c - dir * s * 0.2f - n * s * 0.4f, c + dir * s * 0.6f - n * s * 0.2f };
        this.Poly(pts, new Color(0.3f, 0.29f, 0.3f, 0.85f * a));
        DrawLine(c - dir * s * 0.2f - n * s * 0.4f, c + n * s * 0.1f, new Color(0.1f, 0.1f, 0.12f, a), 2f); // 찢긴 금
    }
}
