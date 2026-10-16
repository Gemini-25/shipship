using System.Runtime.CompilerServices;

namespace ShipEngine;

/// <summary>
/// 난수 갈래. 두 가지를 쓴다.
/// <list type="bullet">
/// <item><b>셈 난수</b> (<see cref="At"/>): (시드 · 갈래 · 틱 · 번호)에서 바로 나오는 값 — 상태가 없어 병렬에서도 순서와 상관없이 같고, 저장할 것도 없다. 칸 · 사람마다 쓴다.</item>
/// <item><b>흐름 난수</b> (<see cref="Stream"/>): 상태가 있는 xoshiro256** — 한 곳에서 여러 번 뽑을 때. 상태는 찍기에 들어간다.</item>
/// </list>
/// 갈래는 이름으로 나눈다: 한 엔진이 난수를 하나 더 써도 다른 엔진이 흔들리지 않는다.
/// </summary>
public static class Rng
{
    /// <summary>이름 → 64비트 갈래 번호 (FNV-1a — 실행마다 같다. string.GetHashCode는 실행마다 달라 쓰지 않는다).</summary>
    public static ulong Key(string name)
    {
        ulong h = 14695981039346656037UL;
        foreach (char ch in name) { h ^= ch; h *= 1099511628211UL; }
        return h;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>셈 난수: 64비트 정수.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong At(ulong seed, ulong stream, long tick, long index)
        => Mix(Mix(Mix(seed ^ stream) ^ (ulong)tick) ^ (ulong)index);

    /// <summary>셈 난수: [0,1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Unit(ulong seed, ulong stream, long tick, long index)
        => (At(seed, stream, tick, index) >> 11) * (1.0 / (1UL << 53));

    public static RngStream Stream(ulong seed, string name, long index = 0) => new(Mix(seed ^ Key(name)) ^ Mix((ulong)index));
}

/// <summary>상태가 있는 난수 (xoshiro256**). 찍기에 상태 네 개가 들어간다.</summary>
public sealed class RngStream : IState
{
    private ulong _a, _b, _c, _d;

    public RngStream(ulong seed)
    {
        _a = Rng.Mix(seed); _b = Rng.Mix(_a); _c = Rng.Mix(_b); _d = Rng.Mix(_c);
        if ((_a | _b | _c | _d) == 0) _a = 1;
    }

    public ulong Next()
    {
        ulong r = BitOps.Rotl(_b * 5, 7) * 9, t = _b << 17;
        _c ^= _a; _d ^= _b; _b ^= _c; _a ^= _d; _c ^= t; _d = BitOps.Rotl(_d, 45);
        return r;
    }

    public double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));
    public float UnitF() => (float)Unit();
    public bool Chance(double p) => Unit() < p;
    public int Range(int lo, int hiExclusive) => hiExclusive <= lo ? lo : lo + (int)(Unit() * (hiExclusive - lo));
    public double Range(double lo, double hi) => lo + Unit() * (hi - lo);

    /// <summary>정규 분포 (상자-뮐러).</summary>
    public double Normal(double mean = 0, double sd = 1)
    {
        double u1 = Math.Max(1e-300, Unit()), u2 = Unit();
        return mean + sd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    public void Save(StateWriter w) { w.U64(_a); w.U64(_b); w.U64(_c); w.U64(_d); }
    public void Load(StateReader r) { _a = r.U64(); _b = r.U64(); _c = r.U64(); _d = r.U64(); }
}

internal static class BitOps
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));
}
