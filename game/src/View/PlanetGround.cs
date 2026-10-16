using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 행성: 배 둘레 땅을 타일로 (도트 타일 한 장을 깔고 · 바위 · 수정 · 얼음 조각 · 웅덩이를 흩뿌린다). 한 번 그려 두고 행성이 바뀔 때만 다시 그린다.
//   하강 · 이륙 동안 서서히 나타나고 사라진다. 낮 · 밤 · 폭풍에 따라 밝기가 바뀐다. 캘 곳 · 이름난 곳 · 기지는 PlanetGroundMarks가 그린다.
public partial class PlanetGround : Node2D
{
    private Main _main = null!;
    private int _planetId = -1;
    private ImageTexture? _tile;
    private PlanetGroundMarks _marks = null!;
    public float Alpha { get; private set; }
    private const float T = ShipView.T;
    private const int Reach = 90;

    public void Init(Main main)
    {
        _main = main;
        TextureRepeat = TextureRepeatEnum.Enabled;
        TextureFilter = TextureFilterEnum.Nearest;
        _marks = new PlanetGroundMarks { Name = "Marks" };
        AddChild(_marks);
        _marks.Init(main);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var pl = _main.Sim.Planet;
        var w = _main.Sim;
        float a = pl.Here == null ? 0f : pl.Phase switch
        {
            PlanetPhase.Descent => Math.Clamp(1f - (pl.LiftAt - w.Tick) / (float)SimTime.Minutes(14), 0f, 1f),
            PlanetPhase.Landed => 1f,
            PlanetPhase.Liftoff => pl.LiftAt < 0 || w.Tick < pl.LiftAt ? 1f : Math.Clamp(1f - (w.Tick - pl.LiftAt) / (float)SimTime.Minutes(10), 0f, 1f),
            _ => 0f,
        };
        Alpha = a;
        Visible = a > 0.001f;
        if (!Visible) { _planetId = -1; return; }
        if (pl.Here!.Id != _planetId) { _planetId = pl.Here.Id; _tile = MakeTile(pl.Here); QueueRedraw(); _marks.QueueRedraw(); }
        // 낮 · 밤 · 폭풍 밝기
        float day = pl.DayHours <= 0f ? 0.6f : 0.3f + 0.7f * MathF.Max(0f, MathF.Sin(MathF.Tau * DayPhase(pl, w)));
        float storm = PlanetSystem.Severe(pl.Weather) ? 1f - 0.3f * pl.WxPower : 1f;
        float b = Math.Clamp(0.45f + 0.55f * day * storm, 0.42f, 1f);
        var sky = new Color(pl.Here.Hex2);
        var tint = new Color(b, b, b).Lerp(new Color(sky.R * b, sky.G * b, sky.B * b), pl.Here.Pressure > 1f ? 0.12f : 0f);
        Modulate = new Color(tint.R, tint.G, tint.B, a);
    }

    private static float DayPhase(PlanetSystem pl, World w) => pl.DayHours <= 0f ? 0.25f : (w.Tick - pl.PhaseSince) / (float)SimTime.TicksPerHour / pl.DayHours % 1f;

    /// <summary>도트 땅 타일 64×64 (4×4 덩어리 · 지표마다 무늬).</summary>
    private static ImageTexture MakeTile(Planet p)
    {
        const int S = 64;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        var bas = new Color(p.Hex);
        var rng = new Rng(p.Seed);
        Color Shade(float k) => new(Math.Clamp(bas.R * k, 0f, 1f), Math.Clamp(bas.G * k, 0f, 1f), Math.Clamp(bas.B * k, 0f, 1f));
        var pal = new[] { Shade(0.68f), Shade(0.84f), bas, Shade(1.12f), Shade(1.3f) };
        for (int by = 0; by < S; by += 4)
            for (int bx = 0; bx < S; bx += 4)
            {
                float n = rng.Float();
                var c = pal[n < 0.12f ? 0 : n < 0.35f ? 1 : n < 0.7f ? 2 : n < 0.92f ? 3 : 4];
                img.FillRect(new Rect2I(bx, by, 4, 4), c);
            }
        void Line(int x0, int y0, int x1, int y1, Color c)
        {
            int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (int i = 0; i <= n; i++) { int x = (x0 + (x1 - x0) * i / Math.Max(1, n) + S) % S, y = (y0 + (y1 - y0) * i / Math.Max(1, n) + S) % S; img.SetPixel(x, y, c); }
        }
        switch (p.Surface)
        {
            case PlanetSurface.Lava:
                for (int k = 0; k < 6; k++) { int x = rng.Range(0, S), y = rng.Range(0, S); Line(x, y, x + rng.Range(-14, 14), y + rng.Range(-14, 14), new Color("#ff7a2a")); }
                break;
            case PlanetSurface.IceSheet or PlanetSurface.Rock or PlanetSurface.Ash:
                for (int k = 0; k < 4; k++) { int x = rng.Range(0, S), y = rng.Range(0, S); Line(x, y, x + rng.Range(-12, 12), y + rng.Range(-12, 12), Shade(p.Surface == PlanetSurface.IceSheet ? 0.78f : 0.65f)); }
                break;
            case PlanetSurface.Dunes:
                for (int y = 4; y < S; y += 12) for (int x = 0; x < S; x++) img.SetPixel(x, (y + (int)(3 * MathF.Sin(x * 0.2f)) + S) % S, Shade(0.8f));
                break;
            case PlanetSurface.Shallows:
                for (int y = 6; y < S; y += 14) for (int x = 0; x < S; x += 2) img.SetPixel(x, (y + (int)(2 * MathF.Sin(x * 0.3f)) + S) % S, Shade(1.35f));
                break;
            case PlanetSurface.Crystal:
                for (int k = 0; k < 10; k++) img.SetPixel(rng.Range(0, S), rng.Range(0, S), new Color("#e8f6ff"));
                break;
            case PlanetSurface.Mud:
                for (int k = 0; k < 3; k++) { int x = rng.Range(4, S - 8), y = rng.Range(4, S - 8); img.FillRect(new Rect2I(x, y, 6, 3), Shade(0.6f)); }
                break;
        }
        return ImageTexture.CreateFromImage(img);
    }

    public override void _Draw()
    {
        var pl = _main.Sim.Planet;
        if (pl.Here is not Planet p || _tile == null) return;
        var g = _main.Sim.Ship.Grid;
        var region = new Rect2(-Reach * T, -Reach * T, (g.Width + 2 * Reach) * T, (g.Height + 2 * Reach) * T);
        // 얕은 바다: 배 둘레만 모래섬
        if (p.Surface == PlanetSurface.Shallows)
        {
            DrawTextureRect(_tile, region, true);
            var cells = _main.Sim.Ship.LiveRooms.SelectMany(r => r.Cells).ToList();
            if (cells.Count > 0)
            {
                float x0 = (cells.Min(c => c.X) - 8) * T, y0 = (cells.Min(c => c.Y) - 7) * T, x1 = (cells.Max(c => c.X) + 9) * T, y1 = (cells.Max(c => c.Y) + 8) * T;
                var sand = new Color("#c9b285");
                DrawRect(new Rect2(x0 - 24f, y0 - 24f, x1 - x0 + 48f, y1 - y0 + 48f), new Color("#9fd0dc"));
                DrawRect(new Rect2(x0, y0, x1 - x0, y1 - y0), sand);
            }
        }
        else DrawTextureRect(_tile, region, true);
        // 흩뿌린 것 (지표마다)
        var rng = new Rng(p.Seed * 7 + 11);
        var cellsSet = _main.Sim.Ship.LiveRooms.SelectMany(r => r.Cells).ToHashSet();
        var bas = new Color(p.Hex);
        var near = new Rect2(-30 * T, -30 * T, (g.Width + 60) * T, (g.Height + 60) * T); // 배 가까이 촘촘히 (멀리는 타일만)
        for (int i = 0; i < 2600; i++)
        {
            var at = new Vector2(rng.Range(near.Position.X, near.End.X), rng.Range(near.Position.Y, near.End.Y));
            var cell = new Cell((int)MathF.Floor(at.X / T), (int)MathF.Floor(at.Y / T));
            if (cellsSet.Contains(cell)) continue;
            float s = rng.Range(6f, 20f);
            switch (p.Surface)
            {
                case PlanetSurface.Rock or PlanetSurface.Ash or PlanetSurface.Lava:
                    DrawRect(new Rect2(at, new Vector2(s, s * 0.7f)), Dark(bas, 0.6f));
                    DrawRect(new Rect2(at + new Vector2(1, 1), new Vector2(s - 2, s * 0.7f - 2)), Dark(bas, p.Surface == PlanetSurface.Lava ? 0.5f : 0.85f));
                    if (p.Surface == PlanetSurface.Lava && rng.Chance(0.3f)) DrawRect(new Rect2(at + new Vector2(s * 0.3f, s * 0.2f), new Vector2(2, 2)), new Color("#ff9a3a"));
                    break;
                case PlanetSurface.IceSheet:
                    DrawRect(new Rect2(at, new Vector2(s, s * 0.6f)), new Color(0.9f, 0.96f, 1f, 0.8f));
                    break;
                case PlanetSurface.Crystal:
                    DrawColoredPolygon(new[] { at, at + new Vector2(s * 0.35f, -s * 1.4f), at + new Vector2(s * 0.7f, 0) }, new Color(0.65f, 0.85f, 1f, 0.85f));
                    break;
                case PlanetSurface.Dunes:
                    if (rng.Chance(0.3f)) DrawRect(new Rect2(at, new Vector2(s * 0.6f, s * 0.4f)), Dark(bas, 0.7f));
                    break;
                case PlanetSurface.Mud:
                    DrawRect(new Rect2(at, new Vector2(s * 1.4f, s * 0.6f)), Dark(bas, 0.55f));
                    break;
                case PlanetSurface.Shallows:
                    if (rng.Chance(0.2f)) DrawRect(new Rect2(at, new Vector2(s * 1.6f, 2f)), new Color(1f, 1f, 1f, 0.35f));
                    break;
            }
        }
        // 내려앉은 자리: 그을린 땅
        if (cellsSet.Count > 0)
        {
            float cx = (cellsSet.Min(c => c.X) + cellsSet.Max(c => c.X) + 1) * 0.5f * T, cy = (cellsSet.Min(c => c.Y) + cellsSet.Max(c => c.Y) + 1) * 0.5f * T;
            float rx = (cellsSet.Max(c => c.X) - cellsSet.Min(c => c.X)) * 0.62f * T, ry = (cellsSet.Max(c => c.Y) - cellsSet.Min(c => c.Y)) * 0.75f * T;
            for (int k = 0; k < 3; k++)
            {
                float f = 1f + k * 0.12f;
                var pts = Enumerable.Range(0, 32).Select(i => new Vector2(cx + MathF.Cos(i * MathF.Tau / 32) * rx * f, cy + MathF.Sin(i * MathF.Tau / 32) * ry * f)).ToArray();
                DrawColoredPolygon(pts, new Color(0f, 0f, 0f, 0.07f));
            }
        }
    }

    private static Color Dark(Color c, float k) => new(c.R * k, c.G * k, c.B * k);
}

/// <summary>땅 위의 캘 곳 · 이름난 곳 · 기지 (반 초마다 다시 그린다).</summary>
public partial class PlanetGroundMarks : Node2D
{
    private Main _main = null!;
    private double _t;
    public void Init(Main main) => _main = main;

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t >= 0.5) { _t = 0; QueueRedraw(); }
    }

    public override void _Draw()
    {
        var pl = _main.Sim.Planet;
        if (pl.Here == null) return;
        const float T = ShipView.T;
        float blink = (float)(Time.GetTicksMsec() % 1000) / 1000f;
        foreach (var s in pl.Sites)
        {
            var at = ShipView.ToPx(s.At);
            float k = Math.Clamp(s.Left / 20f, 0.25f, 1f);
            switch (s.Kind)
            {
                case GroundKind.Landmark:
                    DrawArc(at, T * 2.4f, 0, MathF.Tau, 32, new Color(1f, 0.85f, 0.4f, s.Surveyed ? 0.45f : 0.9f), 5f);
                    DrawLine(at, at + new Vector2(0, -T * 2.8f), new Color(0.95f, 0.95f, 0.95f), 6f);
                    DrawColoredPolygon(new[] { at + new Vector2(0, -T * 2.8f), at + new Vector2(T * 1.6f, -T * 2.3f), at + new Vector2(0, -T * 1.8f) }, s.Surveyed ? new Color("#7cf0c8") : new Color("#ffcf5a"));
                    break;
                default:
                {
                    var col = s.Kind switch { GroundKind.Ice => new Color("#cfeeff"), GroundKind.Sand => new Color("#e2c48a"), GroundKind.Crystal => new Color("#a7d8ff"), GroundKind.Carbon => new Color("#3a3430"), _ => new Color("#8a7a6a") };
                    int n = 4 + (int)(7 * k);
                    var r = new Rng(s.Id * 131 + 7);
                    for (int i = 0; i < n; i++)
                    {
                        var o = at + new Vector2(r.Range(-2.2f, 1.6f) * T, r.Range(-1.6f, 1.2f) * T);
                        float sz = r.Range(0.9f, 1.6f) * T * (0.6f + 0.4f * k);
                        DrawRect(new Rect2(o + new Vector2(3, 4), new Vector2(sz, sz * 0.7f)), new Color(0, 0, 0, 0.3f)); // 그림자
                        if (s.Kind == GroundKind.Crystal) DrawColoredPolygon(new[] { o, o + new Vector2(sz * 0.4f, -sz * 1.5f), o + new Vector2(sz * 0.8f, 0) }, col);
                        else { DrawRect(new Rect2(o, new Vector2(sz, sz * 0.7f)), col.Darkened(0.35f)); DrawRect(new Rect2(o + new Vector2(2, 2), new Vector2(sz - 4, sz * 0.7f - 4)), col); }
                        if (s.Kind == GroundKind.Ore && i % 3 == 0) DrawRect(new Rect2(o + new Vector2(sz * 0.3f, sz * 0.2f), new Vector2(3, 3)), new Color("#e8d9a0"));
                    }
                    if (s.Mined > 0) DrawArc(at, T * 2.6f, 0, MathF.Tau, 28, new Color(1, 1, 1, 0.18f), 3f); // 캔 자리 테
                    break;
                }
            }
        }
        foreach (var b in pl.Builds)
        {
            var at = ShipView.ToPx(b.At);
            bool done = b.Done >= 0;
            DrawSetTransform(at, 0f, new Vector2(3.2f, 3.2f)); // 기지는 크게 (배 전체가 보이는 확대에서도 보이게)
            at = Vector2.Zero;
            float hp = b.Health;
            var steel = new Color(0.62f, 0.66f, 0.7f).Darkened(0.5f * (1f - hp));
            if (!done)
            {
                // 짓는 중: 비계 · 진척 막대
                var box = new Rect2(at - new Vector2(T, T * 0.8f), new Vector2(T * 2, T * 1.6f));
                DrawRect(box, new Color(1f, 0.85f, 0.3f, 0.6f), false, 2f);
                for (float x = box.Position.X; x < box.End.X; x += 8f) DrawLine(new Vector2(x, box.Position.Y), new Vector2(x + 8f, box.End.Y), new Color(1f, 0.85f, 0.3f, 0.35f), 1f);
                float f = Math.Clamp(b.Work / 6f, 0f, 1f);
                DrawRect(new Rect2(box.Position.X, box.End.Y + 4f, box.Size.X * f, 4f), new Color("#ffcf5a"));
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                continue;
            }
            switch (b.Kind)
            {
                case BuildKind.Barrier:
                    for (int i = -2; i <= 2; i++)
                    {
                        var seg = new Rect2(at + new Vector2(i * T * 0.9f - T * 0.45f, -T * 0.3f), new Vector2(T * 0.85f, T * 0.6f));
                        if (hp < 0.4f && i % 2 == 0) continue; // 무너진 칸
                        DrawRect(seg, steel);
                        DrawRect(new Rect2(seg.Position, new Vector2(seg.Size.X, 3f)), steel.Lightened(0.3f));
                        DrawLine(seg.Position + new Vector2(4, seg.Size.Y - 4), seg.Position + new Vector2(seg.Size.X - 4, 4), new Color(1f, 0.8f, 0.2f, 0.6f), 2f);
                    }
                    break;
                case BuildKind.Tower:
                    DrawRect(new Rect2(at + new Vector2(-T * 0.35f, -T * 0.15f), new Vector2(T * 0.7f, T * 0.3f)), steel);
                    DrawLine(at, at + new Vector2(0, -T * 2.4f), steel.Lightened(0.2f), 3f);
                    DrawLine(at + new Vector2(-T * 0.3f, 0), at + new Vector2(0, -T * 2f), steel, 1.5f);
                    DrawLine(at + new Vector2(T * 0.3f, 0), at + new Vector2(0, -T * 2f), steel, 1.5f);
                    DrawCircle(at + new Vector2(0, -T * 2.45f), 3.5f, blink < 0.5f && hp > 0.15f ? new Color("#ff4a4a") : new Color(0.3f, 0.05f, 0.05f));
                    break;
                case BuildKind.Greenhouse:
                {
                    var dome = Enumerable.Range(0, 20).Select(i => at + new Vector2(MathF.Cos(MathF.PI + i * MathF.PI / 19) * T * 1.3f, MathF.Sin(MathF.PI + i * MathF.PI / 19) * T * 1.1f)).ToArray();
                    DrawColoredPolygon(dome, new Color(0.55f, 0.9f, 0.7f, 0.45f * hp + 0.1f));
                    for (int i = 1; i < 4; i++) DrawLine(at, dome[i * 5], new Color(0.85f, 1f, 0.9f, 0.6f), 1.5f);
                    DrawLine(at - new Vector2(T * 1.3f, 0), at + new Vector2(T * 1.3f, 0), steel, 2f);
                    for (int i = -2; i <= 2; i++) DrawCircle(at + new Vector2(i * T * 0.4f, -T * 0.25f), 4f, new Color("#4caf6a"));
                    break;
                }
                default: // 창고: 쌓인 상자
                    for (int i = 0; i < 4; i++)
                    {
                        var o = at + new Vector2((i % 2) * T * 0.75f - T * 0.75f, (i / 2) * -T * 0.6f);
                        DrawRect(new Rect2(o, new Vector2(T * 0.7f, T * 0.55f)), new Color("#b58a4a").Darkened(0.4f * (1f - hp)));
                        DrawRect(new Rect2(o, new Vector2(T * 0.7f, T * 0.55f)), new Color("#5a4020"), false, 1.5f);
                    }
                    break;
            }
        }
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
