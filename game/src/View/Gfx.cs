using Godot;

namespace ShipSim.View;

/// <summary>폰트. 동봉된 Pretendard를 쓰고, 없으면 시스템 한글 폰트로 대체.</summary>
public static class Fonts
{
    public static Font Body { get; private set; } = null!;
    public static Font Bold { get; private set; } = null!;

    public static void Load()
    {
        Body = LoadOrSystem("res://assets/fonts/Pretendard-Medium.otf", 500);
        Bold = LoadOrSystem("res://assets/fonts/Pretendard-Bold.otf", 700);
        ThemeDB.FallbackFont = Body;
    }

    private static Font LoadOrSystem(string path, int weight)
    {
        if (ResourceLoader.Exists(path))
        {
            var font = GD.Load<FontFile>(path);
            if (font != null) return font;
        }
        return new SystemFont
        {
            FontNames = new[] { "Pretendard", "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "sans-serif" },
            FontWeight = weight,
        };
    }
}

/// <summary>둥근 사각형, 글자 정렬 같은 그리기 도우미.</summary>
public static class Gfx
{
    private static StyleBoxFlat? _box;

    public static void RoundRect(CanvasItem ci, Rect2 rect, Color fill, float radius, Color? border = null, int borderWidth = 1) =>
        FastDraw.RoundRect(ci, rect, fill, radius, border, borderWidth); // 60프레임: 상자 그림(15µs) 대신 사각형 + 원 그림 모서리 (1µs)

    /// <summary>예전 상자 그림 (비교용).</summary>
    public static void RoundRectStyle(CanvasItem ci, Rect2 rect, Color fill, float radius, Color? border = null, int borderWidth = 1)
    {
        _box ??= new StyleBoxFlat { AntiAliasing = true, CornerDetail = 6 };
        _box.BgColor = fill;
        _box.DrawCenter = true;
        _box.SetCornerRadiusAll(Mathf.RoundToInt(radius));
        _box.SetBorderWidthAll(border.HasValue ? borderWidth : 0);
        _box.BorderColor = border ?? Colors.Transparent;
        _box.Draw(ci.GetCanvasItem(), rect);
    }

    /// <summary>v17.6 글자 크기 배율 (설정 · 0.85 ~ 1.3). 화면 글은 모두 이 배율을 거친다.</summary>
    public static float TextScale { get; set; } = 1f;
    private static int Sz(int size) => TextScale == 1f ? size : Mathf.Max(6, Mathf.RoundToInt(size * TextScale));

    // v17.7 성능: 글자 폭은 (글꼴 · 글 · 크기)가 같으면 같다 — 매 프레임 다시 재지 않는다 (가득 차면 비운다)
    private static readonly System.Collections.Generic.Dictionary<(Font, string, int), float> WidthCache = new();

    public static float Width(Font font, string text, int size)
    {
        int sz = Sz(size);
        var key = (font, text, sz);
        if (WidthCache.TryGetValue(key, out float w)) return w;
        if (WidthCache.Count > 16384) WidthCache.Clear();
        return WidthCache[key] = font.GetStringSize(text, HorizontalAlignment.Left, -1, sz).X;
    }

    /// <summary>세로 중앙 정렬용 기준선 보정값.</summary>
    public static float CenterOffset(Font font, int size) => (font.GetAscent(Sz(size)) - font.GetDescent(Sz(size))) * 0.5f;

    public static void Text(CanvasItem ci, Font font, Vector2 baseline, string text, int size, Color color) =>
        ci.DrawString(font, baseline, text, HorizontalAlignment.Left, -1, Sz(size), color);

    public static void TextRight(CanvasItem ci, Font font, Vector2 rightBaseline, string text, int size, Color color) =>
        ci.DrawString(font, rightBaseline - new Vector2(Width(font, text, size), 0), text, HorizontalAlignment.Left, -1, Sz(size), color);

    public static void TextCentered(CanvasItem ci, Font font, Vector2 center, string text, int size, Color color) =>
        ci.DrawString(font, center + new Vector2(-Width(font, text, size) * 0.5f, CenterOffset(font, size)),
            text, HorizontalAlignment.Left, -1, Sz(size), color);

    /// <summary>알약 모양 라벨. 그린 사각형을 돌려준다.</summary>
    public static Rect2 Pill(CanvasItem ci, Font font, Vector2 center, string text, int size, Color textColor, Color bg,
        Color? border = null, float padX = 7f, float padY = 3.5f)
    {
        float w = Width(font, text, size) + padX * 2f;
        float h = size + padY * 2f;
        var rect = new Rect2(center.X - w * 0.5f, center.Y - h * 0.5f, w, h);
        RoundRect(ci, rect, bg, h * 0.5f, border);
        TextCentered(ci, font, center, text, size, textColor);
        return rect;
    }

    /// <summary>진행 막대.</summary>
    public static void Bar(CanvasItem ci, Rect2 rect, float value, Color fill, Color? track = null)
    {
        float r = rect.Size.Y * 0.5f;
        RoundRect(ci, rect, track ?? new Color(1, 1, 1, 0.07f), r);
        float w = Mathf.Clamp(value, 0f, 1f) * rect.Size.X;
        if (w >= 1f) RoundRect(ci, new Rect2(rect.Position, new Vector2(Mathf.Max(w, rect.Size.Y), rect.Size.Y)), fill, r);
    }

    public static Vector2 ToGodot(this System.Numerics.Vector2 v) => new(v.X, v.Y);
}
