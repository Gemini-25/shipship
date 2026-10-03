using System;
using Godot;

namespace ShipSim.View;

/// <summary>
/// v17.7 성능: 드물게 바뀌는 층(외판 · 벽 · 바닥 이음 · 기술 모습)을 화면 확대에 맞춘 그림 한 장으로 구워 둔다.
/// 큰 배는 이 층들만으로 그리기 호출이 수만 번 — 구워 두면 한 번이다. 층이 다시 그려지면(구조 · 기술이 바뀜) 다시 굽는다.
/// 확대를 바꾸면 잠깐 늘린 그림을 쓰다가 멈추면 그 배율로 다시 굽고, 너무 가까이 들어가 그림이 너무 커지면 예전처럼 그대로 그린다.
/// </summary>
public partial class BakedLayer : Node2D
{
    private const int MaxTex = 8192;

    private readonly DrawLayer _layer;
    private readonly Func<Rect2> _bounds;
    private readonly SubViewport _vp;
    private readonly Node2D _root;
    private Rect2 _rect, _seenBounds;
    private float _scale = -1f, _want = -1f;
    private double _settle;
    private int _seenDraws = -1;
    private bool _live;

    /// <summary>끄면 예전처럼 그린다 (비교 · 시험용).</summary>
    public static bool Enabled = true;

    public BakedLayer(DrawLayer layer, Func<Rect2> bounds, TextureFilterEnum filter)
    {
        _layer = layer;
        _bounds = bounds;
        Name = layer.Name + "Baked";
        _vp = new SubViewport
        {
            Name = "Bake", TransparentBg = true, Disable3D = true, Size = new Vector2I(4, 4),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, RenderTargetClearMode = SubViewport.ClearMode.Always,
        };
        _root = new Node2D { Name = "Root", TextureFilter = filter };
        _vp.AddChild(_root);
        AddChild(_vp);
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha }; // 투명 바탕에 구운 색은 이미 알파가 곱해져 있다
        AddChild(_layer); // 처음에는 그대로 그린다 (배율이 정해지면 옮긴다)
        _live = true;
    }

    public override void _Process(double delta)
    {
        var b = _bounds();
        if (b.Size.X < 1f || b.Size.Y < 1f) return;
        float zoom = GetViewport().GetCanvasTransform().X.Length();
        float q = MathF.Max(0.02f, zoom);
        float cap = MaxTex / MathF.Max(b.Size.X, b.Size.Y);
        bool wantLive = !Enabled || q > cap;
        if (wantLive != _live) { SetLive(wantLive); _scale = -1f; }
        if (_live) return;

        // 확대가 멈추면 (0.3초) 그 배율로 다시 굽는다 — 그림 한 칸이 화면 한 칸이 되게 (흐려지지 않게)
        if (MathF.Abs(q / MathF.Max(0.0001f, _want) - 1f) > 0.001f) { _want = q; _settle = 0; }
        else _settle += delta;
        bool rescale = _scale < 0f || (MathF.Abs(_want / _scale - 1f) > 0.003f && _settle > 0.3) || b != _seenBounds;
        var filter = MathF.Abs(q / MathF.Max(0.0001f, _scale) - 1f) < 0.005f ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
        if (TextureFilter != filter) TextureFilter = filter;
        if (rescale)
        {
            _scale = _want;
            _seenBounds = b;
            var size = new Vector2I(Math.Max(4, (int)MathF.Ceiling(b.Size.X * _scale)), Math.Max(4, (int)MathF.Ceiling(b.Size.Y * _scale)));
            _rect = new Rect2(b.Position, new Vector2(size.X, size.Y) / _scale); // 그림 한 칸 = 화면 한 칸
            if (_vp.Size != size) _vp.Size = size;
            _root.Scale = new Vector2(_scale, _scale);
            _root.Position = -b.Position * _scale;
            Bake();
        }
        else if (_layer.Draws != _seenDraws) Bake(); // 층을 다시 그렸다 → 다시 굽는다
    }

    private void Bake()
    {
        _seenDraws = _layer.Draws;
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        QueueRedraw();
    }

    private void SetLive(bool live)
    {
        _live = live;
        _layer.GetParent()?.RemoveChild(_layer);
        if (live) AddChild(_layer);
        else _root.AddChild(_layer);
        _layer.QueueRedraw();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_live || _scale <= 0f) return;
        DrawTextureRect(_vp.GetTexture(), _rect, false);
    }
}
