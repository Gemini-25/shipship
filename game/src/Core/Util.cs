using System;
using System.Collections.Generic;

namespace ShipSim.Core;

/// <summary>시드 고정 난수. 같은 시드면 항상 같은 역사가 나온다(버그 재현용).</summary>
public sealed class Rng
{
    private readonly Random _random;

    public Rng(int seed) => _random = new Random(seed);

    /// <summary>v10.3: 지금까지 뽑은 횟수 (같은 시드에서 같은 횟수면 같은 상태 — 저장 지문에 넣는다).</summary>
    public long Draws { get; private set; }

    public float Float() { Draws++; return (float)_random.NextDouble(); }
    public float Range(float min, float max) => min + (max - min) * Float();
    public int Range(int minInclusive, int maxExclusive) { Draws++; return _random.Next(minInclusive, maxExclusive); }
    public bool Chance(float probability) => Float() < probability;
    public T Pick<T>(IReadOnlyList<T> items) { Draws++; return items[_random.Next(items.Count)]; }
}

/// <summary>Utility AI 점수 곡선.</summary>
public static class Curve
{
    public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

    /// <summary>lo 이하는 0, hi 이상은 1, 사이는 부드럽게.</summary>
    public static float Smooth(float x, float lo, float hi)
    {
        float t = Clamp01((x - lo) / (hi - lo));
        return t * t * (3f - 2f * t);
    }
}

/// <summary>한국어 조사 자동 선택 (받침 여부).</summary>
public static class Ko
{
    private static (bool batchim, bool rieul) Final(string word)
    {
        if (string.IsNullOrEmpty(word)) return (false, false);
        char last = word[^1];
        // 숫자는 읽는 소리로: 영·일·삼·육·칠·팔은 받침이 있다 (일·칠·팔은 ㄹ)
        if (last >= '0' && last <= '9') return last switch
        {
            '0' or '3' or '6' => (true, false),
            '1' or '7' or '8' => (true, true),
            _ => (false, false),
        };
        if (last < '가' || last > '힣') return (false, false);
        int jong = (last - 0xAC00) % 28;
        return (jong != 0, jong == 8);
    }

    public static string EuRo(string w) { var (b, r) = Final(w); return w + (b && !r ? "으로" : "로"); }
    public static string IGa(string w) => w + (Final(w).batchim ? "이" : "가");
    public static string EunNeun(string w) => w + (Final(w).batchim ? "은" : "는");
    public static string EulReul(string w) => w + (Final(w).batchim ? "을" : "를");
    public static string WaGwa(string w) => w + (Final(w).batchim ? "과" : "와");
}
