using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace ShipEngine;

/// <summary>
/// 칸의 기체 성분 G개를 네 개씩 한꺼번에 (Vector256 — 없으면 하나씩).
/// 성분마다 하나씩 셀 때와 같은 셈을 같은 차례로 한다 → 비트까지 같다 (지문 같음 — 1코어 · 4코어 · 넷씩 셈이 없는 기계도 같은 값).
/// 성분끼리 더하는 합(n += v)은 넷씩 묶으면 차례가 바뀌므로 하나씩 차례대로 더한다 (<see cref="SumProd"/>도 곱만 넷씩).
/// 곱하고 더하기를 한 번에(FMA) 하지 않는다 — 반올림이 한 번 줄어 값이 달라진다.
/// </summary>
internal static class Lanes
{
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Short() => throw new ArgumentException("성분 수가 모자란다");

    /// <summary>f[g] += a·x[g] − b·y[g] (확산의 이웃 몫).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddDiff(Span<double> f, ReadOnlySpan<double> x, ReadOnlySpan<double> y, double a, double b)
    {
        int n = f.Length, g = 0;
        if (x.Length < n || y.Length < n) Short();
        ref double rf = ref MemoryMarshal.GetReference(f);
        ref double rx = ref MemoryMarshal.GetReference(x);
        ref double ry = ref MemoryMarshal.GetReference(y);
        if (Vector256.IsHardwareAccelerated)
        {
            var va = Vector256.Create(a); var vb = Vector256.Create(b);
            for (; g <= n - 4; g += 4)
            {
                var v = Vector256.LoadUnsafe(ref rf, (nuint)g) + (va * Vector256.LoadUnsafe(ref rx, (nuint)g) - vb * Vector256.LoadUnsafe(ref ry, (nuint)g));
                v.StoreUnsafe(ref rf, (nuint)g);
            }
        }
        for (; g < n; g++) Unsafe.Add(ref rf, g) += a * Unsafe.Add(ref rx, g) - b * Unsafe.Add(ref ry, g);
    }

    /// <summary>d[g] = Math.Max(0, x[g] + y[g]) (0 아래는 +0 · NaN은 그대로 — Math.Max와 같다).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SumFloor0(Span<double> d, ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        int n = d.Length, g = 0;
        if (x.Length < n || y.Length < n) Short();
        ref double rd = ref MemoryMarshal.GetReference(d);
        ref double rx = ref MemoryMarshal.GetReference(x);
        ref double ry = ref MemoryMarshal.GetReference(y);
        if (Vector256.IsHardwareAccelerated)
        {
            for (; g <= n - 4; g += 4)
            {
                var s = Vector256.LoadUnsafe(ref rx, (nuint)g) + Vector256.LoadUnsafe(ref ry, (nuint)g);
                Vector256.ConditionalSelect(Vector256.LessThanOrEqual(s, Vector256<double>.Zero), Vector256<double>.Zero, s).StoreUnsafe(ref rd, (nuint)g);
            }
        }
        for (; g < n; g++) Unsafe.Add(ref rd, g) = Math.Max(0, Unsafe.Add(ref rx, g) + Unsafe.Add(ref ry, g));
    }

    /// <summary>d[g] += x[g] (구역 평균 조성을 칸마다 모은다).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(Span<double> d, ReadOnlySpan<double> x)
    {
        int n = d.Length, g = 0;
        if (x.Length < n) Short();
        ref double rd = ref MemoryMarshal.GetReference(d);
        ref double rx = ref MemoryMarshal.GetReference(x);
        if (Vector256.IsHardwareAccelerated)
            for (; g <= n - 4; g += 4)
                (Vector256.LoadUnsafe(ref rd, (nuint)g) + Vector256.LoadUnsafe(ref rx, (nuint)g)).StoreUnsafe(ref rd, (nuint)g);
        for (; g < n; g++) Unsafe.Add(ref rd, g) += Unsafe.Add(ref rx, g);
    }

    /// <summary>d[g] += x[g] · s (넘친 기체를 모자란 칸에 나눠 준다).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddScaled(Span<double> d, ReadOnlySpan<double> x, double s)
    {
        int n = d.Length, g = 0;
        if (x.Length < n) Short();
        ref double rd = ref MemoryMarshal.GetReference(d);
        ref double rx = ref MemoryMarshal.GetReference(x);
        if (Vector256.IsHardwareAccelerated)
        {
            var vs = Vector256.Create(s);
            for (; g <= n - 4; g += 4)
                (Vector256.LoadUnsafe(ref rd, (nuint)g) + Vector256.LoadUnsafe(ref rx, (nuint)g) * vs).StoreUnsafe(ref rd, (nuint)g);
        }
        for (; g < n; g++) Unsafe.Add(ref rd, g) += Unsafe.Add(ref rx, g) * s;
    }

    /// <summary>d[g] = x[g] · s.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Scale(Span<double> d, ReadOnlySpan<double> x, double s)
    {
        int n = d.Length, g = 0;
        if (x.Length < n) Short();
        ref double rd = ref MemoryMarshal.GetReference(d);
        ref double rx = ref MemoryMarshal.GetReference(x);
        if (Vector256.IsHardwareAccelerated)
        {
            var vs = Vector256.Create(s);
            for (; g <= n - 4; g += 4)
                (Vector256.LoadUnsafe(ref rx, (nuint)g) * vs).StoreUnsafe(ref rd, (nuint)g);
        }
        for (; g < n; g++) Unsafe.Add(ref rd, g) = Unsafe.Add(ref rx, g) * s;
    }

    /// <summary>d[g] = s · x[g] — 그 가운데 하나라도 lim·y[g]를 넘으면 false (고르기: 평균 조성으로 내놓을 수 있나).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ScaleWithin(Span<double> d, ReadOnlySpan<double> x, double s, ReadOnlySpan<double> y, double lim)
    {
        int n = d.Length, g = 0;
        if (x.Length < n || y.Length < n) Short();
        ref double rd = ref MemoryMarshal.GetReference(d);
        ref double rx = ref MemoryMarshal.GetReference(x);
        ref double ry = ref MemoryMarshal.GetReference(y);
        bool ok = true;
        if (Vector256.IsHardwareAccelerated)
        {
            var vs = Vector256.Create(s); var vl = Vector256.Create(lim); var over = Vector256<double>.Zero;
            for (; g <= n - 4; g += 4)
            {
                var v = vs * Vector256.LoadUnsafe(ref rx, (nuint)g);
                v.StoreUnsafe(ref rd, (nuint)g);
                over |= Vector256.GreaterThan(v, vl * Vector256.LoadUnsafe(ref ry, (nuint)g));
            }
            ok = over == Vector256<double>.Zero;
        }
        for (; g < n; g++) { double v = s * Unsafe.Add(ref rx, g); Unsafe.Add(ref rd, g) = v; if (v > lim * Unsafe.Add(ref ry, g)) ok = false; }
        return ok;
    }

    /// <summary>d[g] −= x[g] · e[g] += x[g] (칸에서 덜어 모음에 담는다).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Move(Span<double> d, Span<double> e, ReadOnlySpan<double> x)
    {
        int n = d.Length, g = 0;
        if (e.Length < n || x.Length < n) Short();
        ref double rd = ref MemoryMarshal.GetReference(d);
        ref double re = ref MemoryMarshal.GetReference(e);
        ref double rx = ref MemoryMarshal.GetReference(x);
        if (Vector256.IsHardwareAccelerated)
            for (; g <= n - 4; g += 4)
            {
                var v = Vector256.LoadUnsafe(ref rx, (nuint)g);
                (Vector256.LoadUnsafe(ref rd, (nuint)g) - v).StoreUnsafe(ref rd, (nuint)g);
                (Vector256.LoadUnsafe(ref re, (nuint)g) + v).StoreUnsafe(ref re, (nuint)g);
            }
        for (; g < n; g++) { double v = Unsafe.Add(ref rx, g); Unsafe.Add(ref rd, g) -= v; Unsafe.Add(ref re, g) += v; }
    }
    /// <summary>Σ (x[g] · y[g]) · s — 곱은 네 개씩 t에 (같은 셈) · 더하기는 성분 차례대로 하나씩 (0에서 시작 — 하나씩 더할 때와 같은 값).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SumProd(ReadOnlySpan<double> x, ReadOnlySpan<double> y, double s, Span<double> t)
    {
        int n = x.Length, g = 0;
        if (y.Length < n || t.Length < n) Short();
        ref double rx = ref MemoryMarshal.GetReference(x);
        ref double ry = ref MemoryMarshal.GetReference(y);
        ref double rt = ref MemoryMarshal.GetReference(t);
        if (Vector256.IsHardwareAccelerated)
        {
            var vs = Vector256.Create(s);
            for (; g <= n - 4; g += 4)
                (Vector256.LoadUnsafe(ref rx, (nuint)g) * Vector256.LoadUnsafe(ref ry, (nuint)g) * vs).StoreUnsafe(ref rt, (nuint)g);
        }
        for (; g < n; g++) Unsafe.Add(ref rt, g) = Unsafe.Add(ref rx, g) * Unsafe.Add(ref ry, g) * s;
        double sum = 0;
        for (g = 0; g < n; g++) sum += Unsafe.Add(ref rt, g);
        return sum;
    }
}
