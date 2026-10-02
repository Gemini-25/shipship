using System;

namespace ShipSim.Core;

// v16.24 확대 3단계 — 멀리(방 색 · 방 아이콘 · 승무원 점) / 중간(설비 실루엣 · 움직임 · 승무원 인형 · 상태 아이콘) /
// 가까이(표정 · 손에 든 물건 · 콘솔 계기 숫자 · 낡은 자국 · 이름표). 화면(그림)과 헤드리스 시험이 같은 판정을 쓴다 — 읽기만.

public enum ZoomTier : byte { Far, Mid, Near }

/// <summary>한 단계에서 그리는 요소.</summary>
[Flags]
public enum Detail : uint
{
    None = 0,
    RoomTint = 1 << 0,      // 방 바탕색
    RoomIcon = 1 << 1,      // 방 가운데 큰 아이콘 (멀리서 방 종류를 읽는다)
    CrewDot = 1 << 2,       // 승무원 점 + 색
    RoomLabel = 1 << 3,     // 방 이름 글
    FixtureShape = 1 << 4,  // 설비 실루엣 (종류마다 다른 몸체)
    FixtureMotion = 1 << 5, // 설비 움직임 (팬 · 불빛 · 김)
    Puppet = 1 << 6,        // 승무원 인형 (자세 · 옷)
    StatusIcon = 1 << 7,    // 머리 위 · 설비 위 상태 아이콘
    Face = 1 << 8,          // 표정 (눈 · 입 · 눈썹)
    Held = 1 << 9,          // 손에 든 물건 (자세히)
    GaugeDigits = 1 << 10,  // 콘솔 계기 숫자
    Wear = 1 << 11,         // 낡은 자국 · 긁힘 · 녹
    NameTag = 1 << 12,      // 이름표
}

/// <summary>얼굴: 감정 · 몸에서 읽는다.</summary>
public enum Expression : byte { Calm, Smile, Worry, Fear, Pain, Tired, Sad, Angry, Asleep }

public static class ZoomDetail
{
    /// <summary>이 확대보다 작으면 멀리.</summary>
    public const float FarBelow = 0.5f;
    /// <summary>이 확대부터 가까이.</summary>
    public const float NearFrom = 1.1f;

    public static ZoomTier Of(float zoom) => zoom < FarBelow ? ZoomTier.Far : zoom < NearFrom ? ZoomTier.Mid : ZoomTier.Near;

    /// <summary>예전 그림 코드의 0 · 1 · 2 단계 (인형 · 설비 그림).</summary>
    public static int Lod(float zoom) => (int)Of(zoom);

    private const Detail FarSet = Detail.RoomTint | Detail.RoomIcon | Detail.CrewDot;
    private const Detail MidSet = Detail.RoomTint | Detail.RoomLabel | Detail.FixtureShape | Detail.FixtureMotion | Detail.Puppet | Detail.StatusIcon;
    private const Detail NearSet = MidSet | Detail.Face | Detail.Held | Detail.GaugeDigits | Detail.Wear | Detail.NameTag;

    public static Detail Draws(ZoomTier t) => t switch { ZoomTier.Far => FarSet, ZoomTier.Mid => MidSet, _ => NearSet };
    public static bool Shows(float zoom, Detail d) => (Draws(Of(zoom)) & d) == d;

    public static string Name(ZoomTier t) => t switch { ZoomTier.Far => "멀리", ZoomTier.Mid => "중간", _ => "가까이" };

    // ── 대비: 어두운 머리 · 피부 · 옷은 어두운 바닥에 묻힌다 ──

    /// <summary>밝기 (sRGB 근사).</summary>
    public static float Luma(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

    /// <summary>이보다 어두우면 밝은 테두리를 두른다.</summary>
    public const float RimBelow = 0.30f;

    public static bool NeedsRim(float luma) => luma < RimBelow;

    /// <summary>얼마나 밝힐까 (0 ~ 0.3): 아주 어두운 색일수록 조금 더 — 색감은 그대로 두고 바닥과만 갈리게.</summary>
    public static float Lift(float luma) => luma >= 0.22f ? 0f : MathF.Min(0.3f, (0.22f - luma) / 0.22f * 0.3f);

    // ── 표정 ──

    /// <summary>지금 얼굴 (가까이에서만 그린다): 잠 · 아픔 · 두려움 · 화 · 슬픔 · 피곤 · 걱정 · 웃음 · 평온.</summary>
    public static Expression Face(World w, CrewMember c)
    {
        if (c.Dead) return Expression.Asleep;
        if (c.Pose == Pose.Sleeping || c.Down) return Expression.Asleep;
        if (c.Vitals.Injury >= 0.45f) return Expression.Pain;
        var em = w.Brain2.Emotions;
        float fear = em.Get(c, Feeling.Fear), anger = em.Get(c, Feeling.Anger), sad = em.Get(c, Feeling.Sadness), joy = em.Get(c, Feeling.Joy);
        if (fear >= 0.5f) return Expression.Fear;
        if (anger >= 0.5f) return Expression.Angry;
        if (sad >= 0.5f) return Expression.Sad;
        if (c.Needs.Rest < 0.2f) return Expression.Tired;
        if (c.Needs.Stress >= 0.6f || fear >= 0.25f) return Expression.Worry;
        if (joy >= 0.35f || c.Needs.Social >= 0.85f && c.Needs.Stress < 0.25f) return Expression.Smile;
        return Expression.Calm;
    }

    /// <summary>표정 까닭 한 줄 (누르면 카드에).</summary>
    public static string FaceWhy(World w, CrewMember c) => Face(w, c) switch
    {
        Expression.Asleep => c.Down ? "쓰러져 있다" : "자는 중",
        Expression.Pain => $"다쳐서 아프다 (부상 {c.Vitals.Injury * 100:0}%)",
        Expression.Fear => "겁에 질렸다",
        Expression.Angry => "화가 났다",
        Expression.Sad => "슬프다",
        Expression.Tired => "몹시 지쳤다",
        Expression.Worry => "걱정이 많다",
        Expression.Smile => "기분이 좋다",
        _ => "덤덤하다",
    };
}
