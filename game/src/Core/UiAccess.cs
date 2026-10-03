using System;
using System.Collections.Generic;

namespace ShipSim.Core;

// v17.6 접근성 — 색약 팔레트: 색만으로 구분하지 않는다. 사람은 표식 모양 + 채움, 방은 바닥 무늬가 함께 다르다.
// 화면(View)과 시험(--uitest)이 같은 표를 쓴다. 색 흉내(Machado 2009, 심도 1.0)로 색이 비슷해도 모양 · 무늬로 갈리는지 시험한다.

/// <summary>사람 표식 모양.</summary>
public enum CrewMark : byte { Circle, Square, Triangle, Diamond, Pentagon, Star, Hexagon, Cross }
/// <summary>표식 채움 (같은 모양끼리 갈리게).</summary>
public enum MarkFill : byte { Solid, Hollow, Half, Dot }
/// <summary>방 바닥 무늬.</summary>
public enum Weave : byte { Plain, Hatch, BackHatch, CrossHatch, Dots, Vertical, Horizontal, Checker, Wave, Zigzag, Rings, Bricks }
public enum Cvd : byte { Protan, Deutan, Tritan }

public static class UiAccess
{
    /// <summary>사람 색 (어두운 바탕에서 읽히는 색약 안전 8색 — 밝기를 고루 벌렸다).</summary>
    public static readonly string[] SafeCrew = { "#e69f00", "#56b4e9", "#009e73", "#f0e442", "#3d8fd1", "#d55e00", "#cc79a7", "#eeeeee" };
    /// <summary>방 색 6가지 (밝기가 서로 다르다).</summary>
    public static readonly string[] SafeRoom = { "#e69f00", "#56b4e9", "#f0e442", "#cc79a7", "#009e73", "#8a8f99" };
    /// <summary>뜻 색: 좋음 · 주의 · 위험 · 강조 (빨강-초록 대신 파랑-주황).</summary>
    public const string SafeGood = "#56b4e9", SafeWarn = "#e69f00", SafeDanger = "#d55e00", SafeAccent = "#cc79a7";

    public static CrewMark Mark(int id) => (CrewMark)(((id % 8) + id / 8) % 8);
    public static MarkFill Fill(int id) => (MarkFill)(id / 8 % 4);
    public static string CrewHex(int id) => SafeCrew[id % SafeCrew.Length];

    public static Weave WeaveOf(RoomType t) => (Weave)((int)t % 12);
    public static string RoomHex(RoomType t) => SafeRoom[((int)t / 12 + (int)t) % SafeRoom.Length];

    public static string MarkName(CrewMark m) => m switch
    {
        CrewMark.Circle => "동그라미", CrewMark.Square => "네모", CrewMark.Triangle => "세모", CrewMark.Diamond => "마름모",
        CrewMark.Pentagon => "오각", CrewMark.Star => "별", CrewMark.Hexagon => "육각", _ => "열십자",
    };

    // ── 색 흉내 · 색 차이 ──

    private static readonly float[][] Mats =
    {
        new[] { 0.152286f, 1.052583f, -0.204868f, 0.114503f, 0.786281f, 0.099216f, -0.003882f, -0.048116f, 1.051998f },
        new[] { 0.367322f, 0.860646f, -0.227968f, 0.280085f, 0.672501f, 0.047413f, -0.011820f, 0.042940f, 0.968881f },
        new[] { 1.255528f, -0.076749f, -0.178779f, -0.078411f, 0.930809f, 0.147602f, 0.004733f, 0.691367f, 0.303900f },
    };

    public static (float r, float g, float b) Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return (Convert.ToInt32(hex[..2], 16) / 255f, Convert.ToInt32(hex.Substring(2, 2), 16) / 255f, Convert.ToInt32(hex.Substring(4, 2), 16) / 255f);
    }

    private static float Lin(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    /// <summary>색약 흉내 (선형 RGB에서 행렬).</summary>
    public static (float r, float g, float b) Simulate((float r, float g, float b) c, Cvd k)
    {
        var m = Mats[(int)k];
        float r = Lin(c.r), g = Lin(c.g), b = Lin(c.b);
        float Clamp(float v) => Math.Clamp(v, 0f, 1f);
        var lr = Clamp(m[0] * r + m[1] * g + m[2] * b);
        var lg = Clamp(m[3] * r + m[4] * g + m[5] * b);
        var lb = Clamp(m[6] * r + m[7] * g + m[8] * b);
        float Gam(float v) => v <= 0.0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;
        return (Gam(lr), Gam(lg), Gam(lb));
    }

    private static (float L, float a, float b) Lab((float r, float g, float b) c)
    {
        float r = Lin(c.r), g = Lin(c.g), b = Lin(c.b);
        float x = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.95047f;
        float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        float z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.08883f;
        static float F(float t) => t > 0.008856f ? MathF.Cbrt(t) : 7.787f * t + 16f / 116f;
        return (116f * F(y) - 16f, 500f * (F(x) - F(y)), 200f * (F(y) - F(z)));
    }

    /// <summary>두 색의 차이 (CIE76 ΔE).</summary>
    public static float DeltaE((float r, float g, float b) a, (float r, float g, float b) b)
    {
        var p = Lab(a); var q = Lab(b);
        return MathF.Sqrt((p.L - q.L) * (p.L - q.L) + (p.a - q.a) * (p.a - q.a) + (p.b - q.b) * (p.b - q.b));
    }

    /// <summary>세 가지 색약 중 가장 헷갈리는 경우의 색 차이.</summary>
    public static float WorstDelta(string hexA, string hexB)
    {
        var a = Hex(hexA); var b = Hex(hexB);
        float worst = DeltaE(a, b);
        foreach (Cvd k in Enum.GetValues<Cvd>()) worst = MathF.Min(worst, DeltaE(Simulate(a, k), Simulate(b, k)));
        return worst;
    }

    /// <summary>두 사람이 색약 팔레트에서 갈리나: 표식(모양 · 채움)이 다르거나, 색이 어떤 색약에서도 뚜렷이 다르다.</summary>
    public static bool CrewApart(int a, int b) => Mark(a) != Mark(b) || Fill(a) != Fill(b) || WorstDelta(CrewHex(a), CrewHex(b)) >= 12f;

    /// <summary>두 방 종류가 갈리나: 바닥 무늬가 다르거나, 색이 어떤 색약에서도 뚜렷이 다르다.</summary>
    public static bool RoomsApart(RoomType a, RoomType b) => a == b || WeaveOf(a) != WeaveOf(b) || WorstDelta(RoomHex(a), RoomHex(b)) >= 12f;
}
