using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Art;

namespace ShipSim.View;

/// <summary>
/// 도트 그림 → 텍스처 캐시 (사람 · 로봇 · 드론 · 설비가 함께 쓴다).
/// 쓰는 그림만 그때그때 만들고, 한 프레임에 새로 만드는 수(예산)를 정해 튀지 않게 한다. 오래 안 쓴 그림은 버린다.
/// 그림 하나에 두 텍스처: 줄여 볼 때 부드러운 것(밉맵) · 1배 이상에서 도트가 또렷한 것(가장 가까운 칸).
/// </summary>
public sealed class DotTex
{
    public sealed class Entry
    {
        public ImageTexture Tex = null!;
        public CanvasTexture Crisp = null!;
        public int W, H;
        public long Used;
    }

    private readonly Dictionary<ulong, Entry> _map = new();
    private readonly List<ulong> _drop = new();
    private long _frame;
    public int Budget;
    /// <summary>지금까지 새로 만든 그림 수 (성능 측정).</summary>
    public int Built { get; private set; }
    public int Count => _map.Count;

    /// <summary>프레임 시작: 예산을 채우고, 가끔 오래 안 쓴 그림을 버린다.</summary>
    public void Tick(int budget)
    {
        long f = (long)Engine.GetProcessFrames();
        if (f == _frame) return;
        _frame = f;
        Budget = budget;
        if ((f & 511) == 0 && _map.Count > 600)
        {
            _drop.Clear();
            foreach (var (k, v) in _map) if (f - v.Used > 900) _drop.Add(k);
            foreach (var k in _drop) _map.Remove(k);
        }
    }

    public bool Has(ulong key) => _map.ContainsKey(key);

    /// <summary>이미 있는 그림만 (만들지 않는다).</summary>
    public Entry? Peek(ulong key)
    {
        if (!_map.TryGetValue(key, out var e)) return null;
        e.Used = _frame;
        return e;
    }

    /// <summary>그림 하나 (없으면 예산 안에서 만든다 · 예산이 다 되면 null · 빈 그림이면 W = 0).</summary>
    public Entry? Get(ulong key, Func<Img> make, bool force = false)
    {
        if (_map.TryGetValue(key, out var e)) { e.Used = _frame; return e; }
        if (Budget <= 0 && !force) return null;
        Budget--;
        Built++;
        var img = make();
        if (img.Blank) { e = new Entry { W = 0, H = 0, Used = _frame }; _map[key] = e; return e; } // 빈 그림: 텍스처 없이 (W = 0)
        var gimg = Image.CreateFromData(img.W, img.H, false, Image.Format.Rgba8, img.Px);
        gimg.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(gimg);
        e = new Entry { Tex = tex, Crisp = new CanvasTexture { DiffuseTexture = tex, TextureFilter = CanvasItem.TextureFilterEnum.Nearest }, W = img.W, H = img.H, Used = _frame };
        _map[key] = e;
        return e;
    }

    /// <summary>열쇠 만들기 (FNV — 같은 값이면 같은 열쇠).</summary>
    public static ulong Key(params long[] parts)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            foreach (var p in parts) { h ^= (ulong)p; h *= 1099511628211UL; }
            return h;
        }
    }
}
