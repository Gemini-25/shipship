using Godot;

namespace ShipSim.View;

/// <summary>v16.2 색의 의미: 평상 · 주의 · 위험 · 정보 · 비활성 · 좋음. 같은 뜻은 어디서나 같은 색.</summary>
public enum Tone { Normal, Caution, Danger, Info, Disabled, Good }

/// <summary>
/// v16.2 디자인 표준 — 간격 · 크기 · 글자 크기 · 색의 의미를 한 곳에.
/// 새 패널은 숫자를 직접 쓰지 말고 여기 값을 쓴다 (모양이 서로 맞게).
/// </summary>
public static class Ui
{
    // ── 간격 (4의 배수) ──
    public const float S1 = 4f, S2 = 8f, S3 = 12f, S4 = 16f, S5 = 24f;
    /// <summary>화면 가장자리 여백.</summary>
    public const float Margin = 16f;
    /// <summary>카드 사이 간격.</summary>
    public const float Gap = 10f;
    /// <summary>카드 안쪽 여백.</summary>
    public const float Pad = 16f;

    // ── 크기 ──
    public const float TopBarH = 52f;
    public const float ButtonH = 28f;
    public const float RowH = 22f;
    public const float ChipH = 20f;
    public const float GaugeH = 6f;
    public const float IconS = 14f, IconM = 18f, IconL = 24f;
    public const float RadiusCard = 12f, RadiusControl = 8f, RadiusChip = 6f;

    // ── 글자 크기 ──
    public const int TextMicro = 9;     // 눈금 · 단축키 글자
    public const int TextTiny = 10;     // 부가 설명 · 시각
    public const int TextSmall = 11;    // 칩 · 머리글 · 목록 둘째 줄
    public const int TextBody = 12;     // 본문
    public const int TextLabel = 13;    // 단추 · 줄 제목
    public const int TextSubtitle = 14; // 카드 부제
    public const int TextTitle = 15;    // 카드 제목
    public const int TextLarge = 17;    // 큰 수치
    public const int TextHeading = 18;  // 사람 이름 · 화면 제목
    public const int TextClock = 22;    // 시계

    // ── 면 ──
    public static Color PanelFill => Palette.Panel;
    public static Color PanelEdge => Palette.PanelBorder;
    public static readonly Color Hover = new(1, 1, 1, 0.06f);
    public static readonly Color HoverSoft = new(1, 1, 1, 0.04f);
    public static readonly Color Track = new(1, 1, 1, 0.07f);
    public static readonly Color TooltipFill = new(0.035f, 0.045f, 0.065f, 0.97f);

    /// <summary>뜻 → 색.</summary>
    public static Color Of(Tone t) => t switch
    {
        Tone.Caution => Palette.Warning,
        Tone.Danger => Palette.Danger,
        Tone.Info => Palette.Accent,
        Tone.Disabled => Palette.TextMuted,
        Tone.Good => Palette.Good,
        _ => Palette.Text,
    };

    /// <summary>예전 코드가 직접 고른 색을 뜻으로 되돌린다 (윗줄 칩처럼 색으로 상태를 넘기던 곳).</summary>
    public static Tone ToneOf(Color c) =>
        c == Palette.Danger ? Tone.Danger : c == Palette.Warning ? Tone.Caution : c == Palette.Good ? Tone.Good
        : c == Palette.Accent ? Tone.Info : c == Palette.TextMuted ? Tone.Disabled : Tone.Normal;

    /// <summary>둘 중 더 급한 뜻.</summary>
    public static Tone Worst(Tone a, Tone b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(Tone t) => t switch { Tone.Danger => 4, Tone.Caution => 3, Tone.Info => 2, Tone.Normal => 1, _ => 0 };

    /// <summary>주의 · 위험만 "이상"이다 (조용한 HUD에서 떠오르는 것).</summary>
    public static bool IsAlarm(Tone t) => t is Tone.Caution or Tone.Danger;
}
