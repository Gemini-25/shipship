using System;
using System.Collections.Generic;
using Godot;
using ShipView;

namespace ShipLive;

/// <summary>그리는 층 하나 — 한 번 그려 두면 그래픽 카드가 기억한다 (바뀔 때만 다시 그린다).</summary>
public partial class VLayer : Node2D
{
    public Action<VLayer>? Paint;
    public override void _Draw() => Paint?.Invoke(this);
}

/// <summary>
/// 새 그림 — 매끈한 반입체 (docs/GRAPHICS.md '화풍 다시 보기' 시안 3). 도트 그림 대신 선과 면으로 그리고, 빛은 칸마다의 빛 지도를 곱한다.
/// 무엇을 어디에 그릴지는 모두 engine/ShipView의 <see cref="VFrame"/>이 세계에서 읽어 온 값이다 (화면만의 가짜는 없다).
/// 층: 바닥 → 줄마다 [벽 · 문] [사람] [설비] (아래 줄이 위 줄을 가린다 — 비스듬한 위 시점) → 빛 곱하기 → 스스로 빛나는 것 → 이름표.
/// 바뀌지 않는 것(바닥 · 벽 · 설비)은 한 번만 그린다 — 장마다 다시 그리는 것은 사람 · 빛 지도 · 빛나는 것 · 이름표뿐.
/// </summary>
public partial class VectorView : Node2D
{
    public const float C = 32;          // 한 칸 (그림 단위)
    private const float WH = 18;        // 벽 높이
    private const int ZRow = 10, ZLight = 3000, ZGlow = 3001, ZLabel = 3002;

    private VStatic? _s;
    private VFrame? _f;
    private Node2D _world = null!;
    private VLayer _floor = null!, _glow = null!, _labels = null!;
    private VLayer[] _wallRows = Array.Empty<VLayer>(), _furnRows = Array.Empty<VLayer>();
    private readonly List<VLayer> _people = new();
    private Sprite2D _lightSprite = null!;
    private Image? _lightImg; private ImageTexture? _lightTex;
    private Texture2D _glowTex = null!;
    private byte[] _furnState = Array.Empty<byte>();
    private bool[] _seatAt = Array.Empty<bool>();
    private float[] _doorDrawn = Array.Empty<float>();
    private List<int>[] _furnInRow = Array.Empty<List<int>>(), _doorInRow = Array.Empty<List<int>>();
    private Font _font = null!, _bold = null!;
    private float _scale = 1; private Vector2 _origin; private Vector2 _vp;
    private int _selected = -1;
    private readonly Dictionary<long, StyleBoxFlat> _boxes = new();

    public void Fonts(Font font, Font bold) { _font = font; _bold = bold; }

    public override void _Ready()
    {
        _world = new Node2D { Name = "세계" };
        AddChild(_world);
        _labels = new VLayer { Name = "이름표", ZIndex = ZLabel, Paint = DrawLabels };
        AddChild(_labels);
        var g = new Gradient { Offsets = new[] { 0f, 0.45f, 1f }, Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.35f), new Color(1, 1, 1, 0) } };
        _glowTex = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 128, Height = 128 };
    }

    // ───────────── 한 장 받기 ─────────────

    public void Show(VFrame f, double camLeft, double camTop, double camW, Vector2 vp, int selected)
    {
        if (!ReferenceEquals(_s, f.S)) Build(f.S);
        _f = f; _vp = vp; _selected = selected;
        _scale = (float)(vp.X / (camW * C));
        _origin = new Vector2((float)(-camLeft * C * _scale), (float)(-camTop * C * _scale));
        _world.Scale = new Vector2(_scale, _scale);
        _world.Position = _origin;

        // 빛 지도 (칸마다 한 점 — 부드럽게 늘려 곱한다)
        var s = f.S;
        if (_lightImg == null || _lightImg.GetWidth() != s.W || _lightImg.GetHeight() != s.H)
        {
            _lightImg = Image.CreateFromData(s.W, s.H, false, Image.Format.Rgb8, f.Light);
            _lightTex = ImageTexture.CreateFromImage(_lightImg);
            _lightSprite.Texture = _lightTex;
        }
        else { _lightImg.SetData(s.W, s.H, false, Image.Format.Rgb8, f.Light); _lightTex!.Update(_lightImg); }

        // 바뀐 설비 · 문이 있는 줄만 다시 그린다
        for (int i = 0; i < f.Furn.Length && i < _furnState.Length; i++)
            if (f.Furn[i] != _furnState[i]) { _furnState[i] = f.Furn[i]; _furnRows[s.Furn[i].Y1 - 1].QueueRedraw(); }
        for (int d = 0; d < f.Door.Length && d < _doorDrawn.Length; d++)
            if (Math.Abs(f.Door[d] - _doorDrawn[d]) > 0.02f) { _doorDrawn[d] = f.Door[d]; _wallRows[s.Doors[d].Y].QueueRedraw(); }

        // 사람 (장마다)
        while (_people.Count < f.People.Length)
        {
            int idx = _people.Count;
            var n = new VLayer { Paint = l => DrawPerson(l, idx) };
            _world.AddChild(n); _people.Add(n);
        }
        for (int i = 0; i < _people.Count; i++)
        {
            var n = _people[i];
            if (i >= f.People.Length) { n.Visible = false; continue; }
            var p = f.People[i];
            n.Visible = !float.IsNaN(p.X);
            if (!n.Visible) continue;
            n.Position = new Vector2(p.X * C, p.Y * C);
            int row = Math.Clamp((int)MathF.Floor(p.Y), 0, s.H - 1);
            int cell = row * s.W + Math.Clamp((int)MathF.Floor(p.X), 0, s.W - 1);
            n.ZIndex = ZRow + row * 4 + (p.Down || _seatAt[cell] ? 3 : 1);   // 누운 이 · 앉은 이는 침대 · 의자 위에
            n.QueueRedraw();
        }
        _glow.QueueRedraw();
        _labels.QueueRedraw();
        QueueRedraw();
    }

    private void Build(VStatic s)
    {
        _s = s;
        foreach (var c in _world.GetChildren()) c.QueueFree();
        _people.Clear();
        _floor = new VLayer { Name = "바닥", ZIndex = 0, Paint = DrawFloor };
        _world.AddChild(_floor);
        _wallRows = new VLayer[s.H]; _furnRows = new VLayer[s.H];
        _furnInRow = new List<int>[s.H]; _doorInRow = new List<int>[s.H];
        for (int y = 0; y < s.H; y++) { _furnInRow[y] = new(); _doorInRow[y] = new(); }
        for (int i = 0; i < s.Furn.Length; i++) _furnInRow[Math.Clamp(s.Furn[i].Y1 - 1, 0, s.H - 1)].Add(i);
        for (int d = 0; d < s.Doors.Length; d++) _doorInRow[Math.Clamp(s.Doors[d].Y, 0, s.H - 1)].Add(d);
        for (int y = 0; y < s.H; y++)
        {
            int row = y;
            _wallRows[y] = new VLayer { ZIndex = ZRow + y * 4, Paint = n => DrawWallRow(n, row) };
            _furnRows[y] = new VLayer { ZIndex = ZRow + y * 4 + 2, Paint = n => DrawFurnRow(n, row) };
            _world.AddChild(_wallRows[y]); _world.AddChild(_furnRows[y]);
        }
        _lightSprite = new Sprite2D
        {
            Name = "빛", Centered = false, Scale = new Vector2(C, C), ZIndex = ZLight, TextureFilter = TextureFilterEnum.Linear,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul },
        };
        _world.AddChild(_lightSprite);
        _lightImg = null;
        _glow = new VLayer { Name = "빛나는 것", ZIndex = ZGlow, Paint = DrawGlow, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _world.AddChild(_glow);
        _furnState = new byte[s.Furn.Length];
        _seatAt = new bool[s.W * s.H];
        foreach (var fu in s.Furn) if (fu.Cat is VCat.Seat or VCat.Bed or VCat.MedBed or VCat.Toilet) for (int yy = fu.Y0; yy < fu.Y1; yy++) for (int xx = fu.X0; xx < fu.X1; xx++) _seatAt[yy * s.W + xx] = true;
        _doorDrawn = new float[s.Doors.Length];
        for (int d = 0; d < _doorDrawn.Length; d++) _doorDrawn[d] = -1;
    }

    // ───────────── 도구 ─────────────

    private static Color Col(uint rgb, float a = 1) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    private static Color Shade(Color c, float k) => k < 0 ? new Color(c.R * (1 + k), c.G * (1 + k), c.B * (1 + k), c.A) : new Color(c.R + (1 - c.R) * k, c.G + (1 - c.G) * k, c.B + (1 - c.B) * k, c.A);
    private static float Rnd(int i) { uint t = (uint)i * 2654435761u; t ^= t >> 15; t *= 2246822519u; t ^= t >> 13; return t / 4294967296f; }

    private StyleBoxFlat Box(Color c, int radius, bool outline = true, int shadow = 0)
    {
        long key = ((long)c.ToRgba32() << 20) ^ ((long)radius << 8) ^ (outline ? 16 : 0) ^ shadow;
        if (_boxes.TryGetValue(key, out var b)) return b;
        b = new StyleBoxFlat { BgColor = c, AntiAliasing = true, AntiAliasingSize = 0.7f, CornerDetail = 6 };
        b.SetCornerRadiusAll(radius);
        if (outline) { b.SetBorderWidthAll(1); b.BorderColor = new Color(0.04f, 0.055f, 0.09f, 0.5f); }
        if (shadow > 0) { b.ShadowColor = new Color(0.015f, 0.02f, 0.045f, 0.42f); b.ShadowSize = shadow; b.ShadowOffset = new Vector2(0, shadow * 0.45f); }
        _boxes[key] = b;
        return b;
    }
    private void RR(VLayer n, float x, float y, float w, float h, int r, Color c, bool outline = true) { if (w > 0.5f && h > 0.5f) n.DrawStyleBox(Box(c, (int)Math.Min(r, Math.Min(w, h) / 2), outline), new Rect2(x, y, w, h)); }

    // ───────────── 바닥 ─────────────

    private Color FloorAt(VStatic s, int x, int y)
    {
        if ((uint)x >= (uint)s.W || (uint)y >= (uint)s.H) return default;
        int r = s.Room[y * s.W + x];
        return r >= 0 && s.Kind[y * s.W + x] == VStatic.Floor ? Col(s.Rooms[r].Floor) : default;
    }

    private void DrawFloor(VLayer n)
    {
        var s = _s!;
        for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                byte k = s.Kind[y * s.W + x];
                if (k == VStatic.Space) continue;
                float px = x * C, py = y * C;
                if (k == VStatic.Floor) { n.DrawRect(new Rect2(px - 0.3f, py - 0.3f, C + 0.6f, C + 0.6f), FloorAt(s, x, y)); continue; }
                // 벽 · 문 칸: 네 귀퉁이마다 그쪽 방의 바닥 (가는 벽 아래로 바닥이 이어진다)
                for (int qy = 0; qy < 2; qy++)
                    for (int qx = 0; qx < 2; qx++)
                    {
                        int dx = qx == 0 ? -1 : 1, dy = qy == 0 ? -1 : 1;
                        var c = FloorAt(s, x + dx, y);
                        if (c.A == 0) c = FloorAt(s, x, y + dy);
                        if (c.A == 0) c = FloorAt(s, x + dx, y + dy);
                        if (c.A == 0) c = FloorAt(s, x - dx, y);
                        if (c.A == 0) c = FloorAt(s, x, y - dy);
                        if (c.A == 0) c = k == VStatic.Hull ? new Color(0.085f, 0.105f, 0.15f) : new Color(0.2f, 0.23f, 0.29f);
                        n.DrawRect(new Rect2(px + qx * C / 2 - 0.3f, py + qy * C / 2 - 0.3f, C / 2 + 0.6f, C / 2 + 0.6f), c);
                    }
            }
        // 넓은 판 이음매 (아주 옅게) · 벽 곁 그늘
        var seam = new Color(0, 0, 0, 0.06f);
        var dark = new Color(0.01f, 0.015f, 0.04f, 0.26f); var none = new Color(0.01f, 0.015f, 0.04f, 0f);
        const float AO = 13;
        for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                byte k = s.Kind[y * s.W + x];
                if (k != VStatic.Floor && k != VStatic.Door) continue;
                float px = x * C, py = y * C;
                if (x % 2 == 0) n.DrawLine(new Vector2(px, py), new Vector2(px, py + C), seam, 1);
                if (y % 2 == 0) n.DrawLine(new Vector2(px, py), new Vector2(px + C, py), seam, 1);
                if (s.Solid(x, y - 1)) Grad(n, px, py, px + C, py, px + C, py + AO + 6, px, py + AO + 6, dark, none);          // 북쪽 벽 아래는 조금 더 깊게
                if (s.Solid(x, y + 1)) Grad(n, px, py + C, px + C, py + C, px + C, py + C - AO * 0.6f, px, py + C - AO * 0.6f, dark, none);
                if (s.Solid(x - 1, y)) Grad(n, px, py, px, py + C, px + AO, py + C, px + AO, py, dark, none);
                if (s.Solid(x + 1, y)) Grad(n, px + C, py, px + C, py + C, px + C - AO, py + C, px + C - AO, py, dark, none);
            }
        // 설비 그림자 (바닥에 부드럽게)
        foreach (var f in s.Furn)
        {
            if (f.Cat == VCat.Small) continue;
            float x = f.X0 * C + 4, y = f.Y0 * C + 6, w = (f.X1 - f.X0) * C - 8, h = (f.Y1 - f.Y0) * C - 8;
            n.DrawStyleBox(Box(new Color(0.015f, 0.02f, 0.045f, 0.30f), 6, false, 7), new Rect2(x, y, w, h));
        }
    }

    private static readonly Vector2[] _q = new Vector2[4];
    private static readonly Color[] _qc = new Color[4];
    private static void Grad(VLayer n, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, Color near, Color far)
    {
        _q[0] = new(x0, y0); _q[1] = new(x1, y1); _q[2] = new(x2, y2); _q[3] = new(x3, y3);
        _qc[0] = near; _qc[1] = near; _qc[2] = far; _qc[3] = far;
        n.DrawPolygon(_q, _qc);
    }

    // ───────────── 벽 · 문 ─────────────

    private static readonly (int dx, int dy)[] WallDirs = { (1, 0), (0, 1), (1, 1), (1, -1) };
    private static readonly (int dx, int dy)[] Four = { (1, 0), (0, 1), (-1, 0), (0, -1) };

    /// <summary>벽 한 칸의 선: 가운데에서 이웃 벽 쪽으로. face: 앞면 (남쪽을 보는 곳만 — 위아래로 뻗은 벽은 윗면만 이어진다).</summary>
    private void WallSeg(VLayer n, VStatic s, int x, int y, Color col, float wid, float oy, bool face)
    {
        var c = new Vector2(x * C + C / 2, y * C + C / 2 + oy);
        bool side = false;
        foreach (var (dx, dy) in WallDirs)
        {
            if (!s.Solid(x + dx, y + dy)) continue;
            if (dx != 0 && dy != 0 && (s.Solid(x + dx, y) || s.Solid(x, y + dy))) continue;   // 대각선은 계단진 곳에서만
            if (face && dx == 0) continue;
            n.DrawLine(c, c + new Vector2(dx * C, dy * C), col, wid, true);
            side = true;
        }
        foreach (var (dx, dy) in Four)
            if (s.At(x + dx, y + dy) == VStatic.Door && !(face && dx == 0)) { n.DrawLine(c, c + new Vector2(dx * C * 0.5f, dy * C * 0.5f), col, wid, true); side = true; }
        if (!face || side || !s.Solid(x, y + 1) || s.Solid(x - 1, y) || s.Solid(x - 1, y + 1) && !s.Solid(x, y + 1)) n.DrawCircle(c, wid / 2, col);
    }

    private void DrawWallRow(VLayer n, int y)
    {
        var s = _s!;
        var hullFace = new Color(0.078f, 0.102f, 0.149f); var hullTop = new Color(0.184f, 0.231f, 0.314f);
        for (int x = 0; x < s.W; x++)
        {
            byte k = s.Kind[y * s.W + x];
            if (k == VStatic.Hull)
            {
                // 선체: 두꺼운 덩어리 — 윗면은 이어지고 앞면은 아래가 빈 곳에만
                float px = x * C, py = y * C;
                if (!s.Solid(x, y + 1)) n.DrawRect(new Rect2(px - 0.3f, py + C - WH, C + 0.6f, WH + 0.3f), hullFace);
                n.DrawRect(new Rect2(px - 0.3f, py - WH - 0.3f, C + 0.6f, C + 0.6f), hullTop);
                if (!s.Solid(x, y - 1)) n.DrawRect(new Rect2(px - 0.3f, py - WH - 0.3f, C + 0.6f, 1.5f), new Color(1, 1, 1, 0.10f));
                continue;
            }
            if (k != VStatic.Wall) continue;
            float wid = C * 0.42f;
            var face = new Color(0.106f, 0.133f, 0.188f);
            var top = new Color(0.239f, 0.290f, 0.388f);
            WallSeg(n, s, x, y, face, wid, 0, true);
            WallSeg(n, s, x, y, face, wid, -WH / 2, true);
            WallSeg(n, s, x, y, Shade(face, 0.1f), wid, -WH + 2, true);
            WallSeg(n, s, x, y, top, wid, -WH, false);
            WallSeg(n, s, x, y, new Color(0.337f, 0.400f, 0.518f), wid * 0.28f, -WH - 1.5f, false);
        }
        foreach (int d in _doorInRow[y])
        {
            var (x, _, _, external) = s.Doors[d];
            float open = _f != null && d < _f.Door.Length ? _f.Door[d] : 0;
            float cx = x * C + C / 2, cy = y * C + C / 2, dh = WH - 3, shut = 1 - Math.Clamp(open, 0, 1);
            var glass = external ? new Color(0.95f, 0.72f, 0.36f) : new Color(0.557f, 0.773f, 0.910f);
            bool inVertWall = s.Solid(x, y - 1) || s.Solid(x, y + 1);
            if (inVertWall)
            {
                // 위아래로 뻗은 벽의 문: 위에서 본 가는 판 둘 (열리면 벽 속으로)
                float len = C * 0.5f * shut;
                RR(n, cx - 3, cy - C / 2 - dh, 6, len + 0.5f, 3, glass, false);
                RR(n, cx - 3, cy + C / 2 - len - dh, 6, len + 0.5f, 3, glass, false);
                if (shut > 0.05f) n.DrawRect(new Rect2(cx - 3, cy + C / 2 - dh, 6, dh), Shade(glass, -0.45f));
            }
            else
            {
                // 좌우로 뻗은 벽의 문: 앞면이 보이는 판 둘 (열리면 양옆으로)
                float w = C * 0.5f * shut;
                n.DrawRect(new Rect2(cx - C / 2, cy - dh - 3, C, 3), new Color(0.17f, 0.2f, 0.27f));
                if (w > 0.5f)
                {
                    n.DrawRect(new Rect2(cx - C / 2, cy - dh, w, dh + 4), new Color(glass, 0.72f));
                    n.DrawRect(new Rect2(cx + C / 2 - w, cy - dh, w, dh + 4), new Color(glass, 0.72f));
                    n.DrawRect(new Rect2(cx - C / 2, cy - dh, w, 1.5f), new Color(1, 1, 1, 0.35f));
                    n.DrawRect(new Rect2(cx + C / 2 - w, cy - dh, w, 1.5f), new Color(1, 1, 1, 0.35f));
                }
            }
        }
    }

    // ───────────── 설비 ─────────────

    private static Color FrontOf(VCat c) => c switch
    {
        VCat.Bed => Col(0x5c493c), VCat.MedBed => Col(0xb9c3ca), VCat.Seat => Col(0x35435c), VCat.Table => Col(0x7d5f41), VCat.Game => Col(0x4d3829), VCat.Bench => Col(0x62543f),
        VCat.Books => Col(0x3f3025), VCat.Store => Col(0x515e73), VCat.Stove => Col(0x262a32), VCat.Steel => Col(0xa2a9b3), VCat.Grow => Col(0x33281e), VCat.Aqua => Col(0x134a52),
        VCat.Reactor => Col(0x262d39), VCat.Engine => Col(0x2c323d), VCat.Screen => Col(0x1b2029), VCat.Battery => Col(0x20252d), VCat.Dock => Col(0x262c36),
        VCat.Toilet => Col(0xaab3bd), VCat.Sink => Col(0x9aa6b2), VCat.Shower => Col(0x3d6f78), _ => Col(0x313845),
    };

    private void DrawFurnRow(VLayer n, int row)
    {
        var s = _s!;
        foreach (int i in _furnInRow[row])
        {
            var f = s.Furn[i];
            byte state = i < _furnState.Length ? _furnState[i] : (byte)0;
            float x = f.X0 * C, y = f.Y0 * C, w = (f.X1 - f.X0) * C, h = (f.Y1 - f.Y0) * C, hh = f.Height;
            if (hh > 0)
            {
                const float pad = 3;
                RR(n, x + pad, y + h - pad - hh, w - 2 * pad, hh + 3, 3, FrontOf(f.Cat), false);
                n.DrawRect(new Rect2(x + pad + 1, y + h - pad - 2, w - 2 * pad - 2, 2), new Color(0, 0, 0, 0.2f));
            }
            DrawTop(n, f, state, x, y - hh, w, h);
            if (state == 3)   // 망가졌다: 붉은 표시
            {
                var at = new Vector2(x + w - 8, y - hh + 8);
                n.DrawCircle(at, 5.5f, new Color(0.85f, 0.2f, 0.18f));
                n.DrawRect(new Rect2(at.X - 0.9f, at.Y - 3.2f, 1.8f, 4), Colors.White);
                n.DrawRect(new Rect2(at.X - 0.9f, at.Y + 1.6f, 1.8f, 1.6f), Colors.White);
            }
        }
    }

    private void DrawTop(VLayer n, VFurn f, byte state, float x, float y, float w, float h)
    {
        float pad = f.Cat == VCat.Small ? 9 : 3;
        x += pad; y += pad; w -= 2 * pad; h -= 2 * pad;
        bool horiz = w >= h, on = state != 2 && state != 3;
        int seed = f.X0 * 31 + f.Y0 * 17;
        void Fill(uint col, int r = 5) => RR(n, x, y, w, h, r, Col(col));
        switch (f.Cat)
        {
            case VCat.Bed:
            {
                Fill(0x7a6252, 6);
                uint[] blankets = { 0x5d7fb8, 0xb86a5d, 0x6aa37d, 0xb89a5d, 0x8a6fb8 };
                var bl = Col(blankets[(f.X0 * 7 + f.Y0 * 3) % 5]);
                if (horiz) { RR(n, x + w * 0.3f, y + 2, w * 0.7f - 2, h - 4, 5, bl, false); RR(n, x + 3, y + 4, w * 0.22f, h - 8, 4, Col(0xefe9df), false); }
                else { RR(n, x + 2, y + h * 0.3f, w - 4, h * 0.7f - 2, 5, bl, false); RR(n, x + 4, y + 3, w - 8, h * 0.22f, 4, Col(0xefe9df), false); }
                break;
            }
            case VCat.MedBed:
                Fill(0xe6edf1, 6); RR(n, x + 3, y + 3, w - 6, h - 6, 4, new Color(0.345f, 0.718f, 0.769f, 0.35f), false);
                n.DrawRect(new Rect2(x + w / 2 - 5, y + h / 2 - 1.5f, 10, 3), Col(0xe25b5b)); n.DrawRect(new Rect2(x + w / 2 - 1.5f, y + h / 2 - 5, 3, 10), Col(0xe25b5b));
                break;
            case VCat.Seat: Fill(0x4b5d7d, 8); RR(n, x + 3, y + 3, w - 6, h - 9, 5, Col(0x6f84a8), false); break;
            case VCat.Table:
            {
                Fill(0xb08a62, 7);
                n.DrawLine(new Vector2(x + 5, y + h / 2), new Vector2(x + w - 5, y + h / 2), new Color(1, 1, 1, 0.12f), 1);
                int k = Math.Max(1, (int)(w / C));
                for (int i = 0; i < k; i++) n.DrawCircle(new Vector2(x + (i + 0.5f) * (w / k), y + h / 2), 5, Col(0xf3efe6));
                break;
            }
            case VCat.Game: Fill(0x6b4f3a, 7); RR(n, x + 4, y + 4, w - 8, h - 8, 4, Col(0x2f7a64), false); break;
            case VCat.Bench: Fill(0x8a7860, 4); n.DrawRect(new Rect2(x + 6, y + 6, 12, 4), Col(0xc9ced6)); n.DrawRect(new Rect2(x + w - 16, y + h - 12, 9, 5), Col(0xe0803a)); break;
            case VCat.Books:
            {
                Fill(0x5a4636, 4);
                uint[] cols = { 0xc9614f, 0xd9a441, 0x4f8fc9, 0x63a877, 0x8d6fd1, 0xe0d6c2 };
                int cnt = (int)((horiz ? w : h) / 5) - 1;
                for (int i = 0; i < cnt; i++)
                {
                    var c = Col(cols[(i * 3 + f.X0 + f.Y0) % 6]);
                    if (horiz) n.DrawRect(new Rect2(x + 4 + i * 5, y + 4, 4, h - 8), c); else n.DrawRect(new Rect2(x + 4, y + 4 + i * 5, w - 8, 4), c);
                }
                break;
            }
            case VCat.Store:
            {
                // 선반마다 내용물이 다르게 (상자 · 통 — 같은 그림이 줄지어 보이지 않게)
                Fill(0x76859c, 4);
                int cnt = Math.Max(1, (int)MathF.Round((horiz ? w : h) / 16));
                uint[] goods = { 0xc9b38a, 0x8fa3bd, 0xb5795a, 0x7fae8a, 0xd0c7b6, 0x9a8cc0 };
                for (int i = 0; i < cnt; i++)
                {
                    float cw = (horiz ? w : h) / cnt;
                    var c = Col(goods[(int)(Rnd(seed + i * 7) * goods.Length) % goods.Length], 0.9f);
                    if (Rnd(seed + i * 13 + 5) < 0.25f) continue;   // 빈 칸
                    if (horiz) RR(n, x + i * cw + 3, y + 4, cw - 6, h - 8, 2, c, false); else RR(n, x + 4, y + i * cw + 3, w - 8, cw - 6, 2, c, false);
                }
                n.DrawRect(new Rect2(x + 4, y + 2.5f, w - 8, 1.5f), new Color(1, 1, 1, 0.22f));
                break;
            }
            case VCat.Small: Fill(f.Type == "OxygenMaskBox" ? 0xe6a23cu : f.Type == "EmergencyLight" ? 0xd9534fu : 0x8b95a5u, 3); break;
            case VCat.Stove:
            {
                Fill(0x3a3f49, 4);
                int k = Math.Max(1, (int)MathF.Round(w / 16));
                for (int i = 0; i < k; i++) n.DrawArc(new Vector2(x + 9 + i * 15, y + h / 2), 4.5f, 0, MathF.Tau, 16, on && state == 1 ? Col(0xff9a57) : Col(0x6b6f78), 1.6f, true);
                break;
            }
            case VCat.Steel: Fill(0xd3d8df, 4); n.DrawRect(new Rect2(x + w - 6, y + 5, 2, h - 10), new Color(0.16f, 0.2f, 0.25f, 0.5f)); break;
            case VCat.Grow:
            {
                Fill(0x4b3b2d, 5);
                int k = Math.Max(2, (int)MathF.Round(w * h / 150));
                uint[] greens = { 0x6fbf73, 0x4fa35e, 0x8fd18a };
                for (int i = 0; i < k; i++)
                    n.DrawCircle(new Vector2(x + 6 + Rnd(seed + i) * (w - 12), y + 6 + Rnd(seed * 3 + i * 3) * (h - 12)), 4 + Rnd(i) * 2.5f, Col(greens[i % 3]));
                break;
            }
            case VCat.Aqua:
                Fill(0x1e6f78, 5); RR(n, x + 3, y + 3, w - 6, h - 6, 4, new Color(0.55f, 1, 0.94f, 0.35f), false);
                RR(n, x + w * 0.6f - 4, y + h * 0.55f - 2.2f, 8, 4.4f, 2, Col(0xffb347), false);
                break;
            case VCat.Reactor:
            {
                Fill(0x3b4352, 10);
                var c = new Vector2(x + w / 2, y + h / 2); float R = Math.Min(w, h) / 2 - 6;
                n.DrawCircle(c, R, Col(0x232a36));
                n.DrawCircle(c, R * 0.62f, on ? Col(0x5fe0ff) : Col(0x2a4a58));
                if (on) n.DrawCircle(c, R * 0.34f, Col(0xf4ffff));
                n.DrawArc(c, R, 0, MathF.Tau, 40, Col(0x8fa3bd), 2, true);
                break;
            }
            case VCat.Engine:
                Fill(0x444b58, 8);
                for (int i = 0; i < 5; i++) n.DrawRect(new Rect2(x + 10 + i * (w - 20) / 5, y + 8, (w - 20) / 5 - 3, h - 16), Col(i % 2 == 1 ? 0x9aa6b8u : 0x7c889bu));
                n.DrawRect(new Rect2(x + w - 9, y + h / 2 - 3, 4, 6), Col(0xffb057));
                break;
            case VCat.Screen:
                Fill(0x2a303c, 4);
                RR(n, x + 4, y + 4, w - 8, Math.Max(5, h * 0.45f), 2, state == 3 ? Col(0x5a2a2a) : on ? Col(0x66c4f2) : Col(0x1d2733), false);
                break;
            case VCat.Battery:
                Fill(0x30363f, 4);
                for (int i = 0; i < 4; i++) n.DrawRect(new Rect2(x + 5 + i * ((w - 10) / 4), y + h - 9, (w - 10) / 4 - 2, 4), Col(i < 3 && on ? 0x7dffb0u : 0x3d4652u));
                break;
            case VCat.Dock:
            {
                Fill(0x39414e, 6);
                var c = Col(0xffd166);
                for (float t = 6; t < w - 8; t += 7) { n.DrawRect(new Rect2(x + t, y + 4, 4, 1.5f), c); n.DrawRect(new Rect2(x + t, y + h - 5.5f, 4, 1.5f), c); }
                for (float t = 6; t < h - 8; t += 7) { n.DrawRect(new Rect2(x + 4, y + t, 1.5f, 4), c); n.DrawRect(new Rect2(x + w - 5.5f, y + t, 1.5f, 4), c); }
                break;
            }
            case VCat.Toilet: Fill(0xdfe4ea, 9); RR(n, x + w * 0.2f, y + h * 0.28f, w * 0.6f, h * 0.56f, 8, Col(0xb8c2cd), false); n.DrawRect(new Rect2(x + 3, y + 3, w - 6, 5), Col(0xc8d0d9)); break;
            case VCat.Sink: Fill(0xd0d7de, 5); RR(n, x + 5, y + 6, w - 10, h - 11, 6, Col(0x9fb6c6), false); n.DrawCircle(new Vector2(x + w / 2, y + 5), 2, Col(0x7a8794)); break;
            case VCat.Shower: Fill(0x9fc3cc, 4); for (int i = 1; i < 4; i++) n.DrawLine(new Vector2(x + i * w / 4, y + 4), new Vector2(x + i * w / 4, y + h - 4), new Color(1, 1, 1, 0.25f), 1); n.DrawCircle(new Vector2(x + w / 2, y + h / 2), 3, Col(0x5b7f88)); break;
            default:
                Fill(0x4a5262, 4);
                n.DrawRect(new Rect2(x + 5, y + 5, 5, 3), on ? Col(0xffc857) : Col(0x5b5f68)); n.DrawRect(new Rect2(x + 12, y + 5, 5, 3), on && state == 1 ? Col(0x7dffb0) : Col(0x5b5f68));
                n.DrawRect(new Rect2(x + 4, y + h - 8, w - 8, 2), new Color(1, 1, 1, 0.12f));
                break;
        }
    }

    // ───────────── 사람 · 로봇 ─────────────

    private void DrawPerson(VLayer n, int idx)
    {
        var f = _f;
        if (f == null || idx >= f.People.Length) return;
        var p = f.People[idx];
        float t = (float)f.Anim;
        if (p.Id == _selected) n.DrawArc(new Vector2(0, 9), 13, 0, MathF.Tau, 28, new Color(1, 0.86f, 0.4f, 0.95f), 2, true);
        if (!p.Person)
        {
            // 로봇: 낮은 몸통 · 눈 불빛
            n.DrawStyleBox(Box(new Color(0.015f, 0.02f, 0.045f, 0.35f), 6, false, 4), new Rect2(-9, 3, 18, 8));
            RR(n, -10, -8, 20, 16, 5, p.Down ? Col(0x6b6f78) : Col(0xd9a441));
            RR(n, -7, -14, 14, 9, 4, Col(0x3a4252));
            n.DrawCircle(new Vector2(-3, -9.5f), 1.6f, p.Down ? Col(0x555b66) : Col(0x7dffb0)); n.DrawCircle(new Vector2(3, -9.5f), 1.6f, p.Down ? Col(0x555b66) : Col(0x7dffb0));
            return;
        }
        var shirt = p.Suit ? Col(0xe8ecf1) : Col(p.Shirt); var skin = Col(p.Skin); var hair = Col(p.Hair);
        if (p.Down)
        {
            // 누운 사람 (자는 이 · 쓰러진 이): 옆으로 눕혀 그린다
            n.DrawStyleBox(Box(new Color(0.015f, 0.02f, 0.045f, 0.3f), 6, false, 4), new Rect2(-14, 0, 28, 9));
            RR(n, -4, -4, 17, 11, 4, Shade(shirt, -0.25f), false);
            RR(n, -11, -5, 15, 13, 6, shirt, false);
            n.DrawCircle(new Vector2(-14, 1), 6.6f, hair); n.DrawCircle(new Vector2(-13.2f, 1.6f), 5.8f, skin);
            n.DrawLine(new Vector2(-15.5f, 0.5f), new Vector2(-13.5f, 0.5f), new Color(0.1f, 0.12f, 0.16f), 1);
            n.DrawLine(new Vector2(-15.5f, 3.2f), new Vector2(-13.5f, 3.2f), new Color(0.1f, 0.12f, 0.16f), 1);
            return;
        }
        float bob = p.Moving ? MathF.Sin(t * 11 + p.Id) * 1.3f : MathF.Sin(t * 1.6f + p.Id) * 0.35f;
        float step = p.Moving ? MathF.Sin(t * 11 + p.Id) * 2.4f : 0;
        n.DrawStyleBox(Box(new Color(0.015f, 0.02f, 0.045f, 0.34f), 5, false, 4), new Rect2(-8, 4, 16, 7));
        var leg = Shade(shirt, -0.38f);
        RR(n, -6, -2 + step * 0.5f, 5, 11 - step * 0.5f, 2, leg, false);
        RR(n, 1, -2 - step * 0.5f, 5, 11 + step * 0.5f, 2, leg, false);
        RR(n, -7.5f, -15 + bob, 15, 14, 6, shirt, false);
        RR(n, -7.5f, -15 + bob, 15, 5, 5, new Color(1, 1, 1, 0.16f), false);
        var head = new Vector2(0, -21 + bob);
        n.DrawCircle(head + new Vector2(0, -1.7f), 7.3f, hair);
        n.DrawCircle(head + new Vector2(0, 0.9f), 6.3f, skin);
        if (p.Suit) { n.DrawArc(head, 8.2f, 0, MathF.Tau, 24, new Color(0.9f, 0.93f, 0.97f), 1.8f, true); n.DrawCircle(head + new Vector2(0, 1), 5.4f, new Color(0.45f, 0.75f, 0.95f, 0.35f)); }
        var eye = new Color(0.1f, 0.12f, 0.16f);
        n.DrawCircle(head + new Vector2(-2.4f, 1), 0.95f, eye); n.DrawCircle(head + new Vector2(2.4f, 1), 0.95f, eye);
        // 표정 (1 웃음 · 2 겁 · 3 아픔 · 4 슬픔 — DotFace 차례)
        var m = head + new Vector2(0, 4);
        switch (p.Face)
        {
            case 1: n.DrawArc(m + new Vector2(0, -1.2f), 2, 0.2f, MathF.PI - 0.2f, 8, eye, 1, true); break;
            case 2: n.DrawCircle(m, 1.2f, eye); break;
            case 3: n.DrawLine(m + new Vector2(-2, 0), m + new Vector2(2, 0.6f), new Color(0.6f, 0.15f, 0.12f), 1.3f, true); break;
            case 4: n.DrawArc(m + new Vector2(0, 1.6f), 2, MathF.PI + 0.3f, MathF.Tau - 0.3f, 8, eye, 1, true); break;
        }
    }

    // ───────────── 스스로 빛나는 것 (빛을 곱한 뒤 더한다) ─────────────

    private void Glow(VLayer n, float x, float y, float r, Color c) => n.DrawTextureRect(_glowTex, new Rect2(x - r, y - r, 2 * r, 2 * r), false, c);

    private void DrawGlow(VLayer n)
    {
        var f = _f; var s = _s;
        if (f == null || s == null) return;
        float t = (float)f.Anim;
        for (int i = 0; i < s.Furn.Length && i < f.Furn.Length; i++)
        {
            var fu = s.Furn[i];
            if (!fu.Glows || f.Furn[i] != 1) continue;
            float cx = (fu.X0 + fu.X1) * C / 2, cy = (fu.Y0 + fu.Y1) * C / 2 - fu.Height;
            switch (fu.Cat)
            {
                case VCat.Reactor: Glow(n, cx, cy, 150 + MathF.Sin(t * 2.2f) * 6, new Color(0.35f, 0.86f, 1f, 0.55f)); break;
                case VCat.Engine: Glow(n, cx, cy, 120, new Color(1f, 0.67f, 0.35f, 0.38f)); break;
                case VCat.Screen: Glow(n, cx, cy, 44, new Color(0.43f, 0.78f, 1f, 0.34f)); break;
                case VCat.Aqua: Glow(n, cx, cy, 50, new Color(0.31f, 0.9f, 0.86f, 0.30f)); break;
                case VCat.Battery: Glow(n, cx, cy, 28, new Color(0.47f, 1f, 0.67f, 0.20f)); break;
                case VCat.Stove: Glow(n, cx, cy, 32, new Color(1f, 0.59f, 0.31f, 0.26f)); break;
            }
        }
        // 불: 세기만큼 크고 장마다 일렁인다
        foreach (var (cell, hrr) in f.Fires)
        {
            int x = cell % s.W, y = cell / s.W;
            float cx = x * C + C / 2, cy = y * C + C / 2, k = Math.Clamp(MathF.Sqrt(hrr / 60f), 0.5f, 2.6f);
            float fl = 0.85f + 0.15f * MathF.Sin(t * 17 + cell);
            Glow(n, cx, cy - 6, 70 * k * fl, new Color(1f, 0.5f, 0.16f, 0.55f));
            for (int j = 0; j < 3; j++)
            {
                float ox = (Rnd(cell * 7 + j) - 0.5f) * 16, hgt = (14 + 10 * Rnd(cell + j * 3)) * k * (0.8f + 0.2f * MathF.Sin(t * 13 + j * 2 + cell));
                _tri[0] = new(cx + ox - 6, cy + 8); _tri[1] = new(cx + ox + 6, cy + 8); _tri[2] = new(cx + ox + MathF.Sin(t * 9 + j) * 2, cy + 8 - hgt);
                n.DrawColoredPolygon(_tri, j == 1 ? new Color(1f, 0.85f, 0.4f, 0.9f) : new Color(1f, 0.45f, 0.12f, 0.85f));
            }
        }
    }
    private static readonly Vector2[] _tri = new Vector2[3];

    // ───────────── 이름표 (화면 크기 그대로 — 어느 확대에서도 읽힌다) ─────────────

    private Vector2 ToScreen(float cx, float cy) => _origin + new Vector2(cx * C * _scale, cy * C * _scale);

    private void Pill(VLayer n, Font font, string text, Vector2 at, int size, Color bg, Color fg)
    {
        var ts = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        float w = ts.X + size * 1.1f, h = size * 1.55f;
        n.DrawStyleBox(Box(bg, (int)(h / 2), false), new Rect2(at.X - w / 2, at.Y - h / 2, w, h));
        n.DrawString(font, new Vector2(at.X - ts.X / 2, at.Y + size * 0.36f), text, HorizontalAlignment.Left, -1, size, fg);
    }

    private void DrawLabels(VLayer n)
    {
        var f = _f; var s = _s;
        if (f == null || s == null || _font == null) return;
        float cell = C * _scale;   // 한 칸의 화면 크기
        if (cell >= 9)
            foreach (var r in s.Rooms)
            {
                if (r.Type == "Corridor" || r.Name.Length == 0) continue;
                var at = ToScreen(r.LabelX, r.LabelY) + new Vector2(0, Math.Max(-6, cell * 0.1f) - WH * _scale * 0.3f);
                if (at.X < -60 || at.X > _vp.X + 60 || at.Y < 44 || at.Y > _vp.Y + 20) continue;
                Pill(n, _bold, r.Name, at, cell >= 22 ? 14 : 12, new Color(0.04f, 0.055f, 0.095f, 0.62f), new Color(0.91f, 0.93f, 0.97f, 0.95f));
            }
        if (cell >= 12)
            foreach (var p in f.People)
            {
                if (!p.Person || float.IsNaN(p.X)) continue;
                var at = ToScreen(p.X, p.Y) + new Vector2(0, (p.Down ? -16 : -34) * _scale - 9);
                if (at.X < -40 || at.X > _vp.X + 40 || at.Y < 44 || at.Y > _vp.Y) continue;
                Pill(n, _bold, p.Name, at, 12, p.Dead ? new Color(0.25f, 0.07f, 0.07f, 0.8f) : new Color(0.047f, 0.063f, 0.1f, 0.74f), new Color(0.93f, 0.95f, 0.97f));
            }
    }

    // ───────────── 우주 (화면 뒤) ─────────────

    public override void _Draw()
    {
        if (_vp.X <= 0) return;
        _q[0] = new(0, 0); _q[1] = new(_vp.X, 0); _q[2] = new(_vp.X, _vp.Y); _q[3] = new(0, _vp.Y);
        _qc[0] = new Color(0.043f, 0.063f, 0.125f); _qc[1] = new Color(0.035f, 0.05f, 0.1f); _qc[2] = new Color(0.02f, 0.027f, 0.055f); _qc[3] = new Color(0.027f, 0.035f, 0.07f);
        DrawPolygon(_q, _qc);
        // 별 (멀리 있다 — 카메라가 움직여도 조금만 따라온다)
        for (int i = 0; i < 220; i++)
        {
            float depth = 0.03f + Rnd(i + 5) * 0.06f;
            float x = Mod(Rnd(i) * _vp.X * 1.3f + _origin.X * depth, _vp.X), y = Mod(Rnd(i + 999) * _vp.Y * 1.3f + _origin.Y * depth, _vp.Y);
            DrawCircle(new Vector2(x, y), Rnd(i + 3) < 0.07f ? 1.5f : 0.8f, new Color(0.8f + Rnd(i + 7) * 0.2f, 0.85f + Rnd(i + 8) * 0.15f, 1, 0.25f + Rnd(i + 77) * 0.6f));
        }
    }
    private static float Mod(float a, float m) => ((a % m) + m) % m;
}
