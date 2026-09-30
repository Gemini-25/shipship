using System;
using System.Collections.Generic;

namespace ShipSim.Core;

/// <summary>
/// 시드 고정 난수. 같은 시드면 항상 같은 역사가 나온다(버그 재현용).
/// v11.3: System.Random(seed)는 선형 생성기라 가까운 시드(시드 + 37·k)끼리 같은 순번의 값이 서로 닮아,
/// 여러 시드로 평균 내는 시험이 사실상 같은 주사위를 굴렸다 (회피 기동 16번 중 비킴 0). xoshiro128**로 바꾸고 SplitMix64로 씨앗을 펼친다.
/// 플랫폼·.NET 판에 상관없이 같은 수열이다.
/// </summary>
public sealed class Rng
{
    private uint _s0, _s1, _s2, _s3;

    public Rng(int seed)
    {
        ulong z = unchecked((ulong)(long)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);
        ulong Next()
        {
            z = unchecked(z + 0x9E3779B97F4A7C15UL);
            ulong x = z;
            x = unchecked((x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL);
            x = unchecked((x ^ (x >> 27)) * 0x94D049BB133111EBUL);
            return x ^ (x >> 31);
        }
        ulong a = Next(), b = Next();
        _s0 = (uint)a; _s1 = (uint)(a >> 32); _s2 = (uint)b; _s3 = (uint)(b >> 32);
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
    }

    private uint NextUInt()
    {
        uint result = unchecked(RotL(_s1 * 5u, 7) * 9u);
        uint t = _s1 << 9;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotL(_s3, 11);
        return result;
    }

    private static uint RotL(uint x, int k) => (x << k) | (x >> (32 - k));

    /// <summary>v10.3: 지금까지 뽑은 횟수 (같은 시드에서 같은 횟수면 같은 상태 — 저장 지문에 넣는다).</summary>
    public long Draws { get; private set; }

    /// <summary>[0, 1) — 24비트.</summary>
    public float Float() { Draws++; return (NextUInt() >> 8) * (1f / 16777216f); }
    public float Range(float min, float max) => min + (max - min) * Float();
    public int Range(int minInclusive, int maxExclusive)
    {
        Draws++;
        if (maxExclusive <= minInclusive) return minInclusive;
        return minInclusive + (int)((ulong)NextUInt() * (ulong)(uint)(maxExclusive - minInclusive) >> 32);
    }
    public bool Chance(float probability) => Float() < probability;
    public T Pick<T>(IReadOnlyList<T> items) { Draws++; return items[(int)((ulong)NextUInt() * (ulong)(uint)items.Count >> 32)]; }
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
