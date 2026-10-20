using System.Reflection;
using System.Runtime.CompilerServices;
using ShipEngine;

namespace ShipView;

/// <summary>
/// 코드 미리 옮기기: 큰 메서드는 처음 부를 때 기계어로 옮기는 데 20~40ms가 걸린다 (소리 울림 · 마음의 계획 · 주컴퓨터 알아채기 · 그리기 —
/// 관전 화면 첫 몇 분 동안 그림이 한 장씩 멈칫하던 것). 시작할 때 낮은 우선순위 스레드에서 엔진 · 화면의 메서드를 미리 옮겨 둔다.
/// 세계는 건드리지 않는다 (코드만 준비 — 결정론과 상관없다).
/// </summary>
public static class Warmup
{
    private static int _started;
    /// <summary>옮긴 메서드 수 · 걸린 시간 (확인용).</summary>
    public static int Methods;
    public static double Ms;
    public static Thread? Job { get; private set; }

    /// <summary>한 번만 시작한다 (거듭 불러도 된다).</summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        Job = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "코드 미리 옮기기" };
        Job.Start();
    }

    private static void Run()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // 화면 쪽부터 (그리기 · 소리 · 연출이 첫 장면에서 먼저 쓰인다) · 그다음 엔진
        foreach (var asm in new[] { typeof(Painter).Assembly, typeof(Sim).Assembly }.Distinct())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
            foreach (var t in types)
            {
                if (t.ContainsGenericParameters || t.Namespace is not { } ns || !(ns.StartsWith("ShipEngine") || ns.StartsWith("ShipView") || ns == "ShipSim.Art")) continue;
                foreach (var m in t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters || (m.MethodImplementationFlags & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) != 0) continue;
                    try { RuntimeHelpers.PrepareMethod(m.MethodHandle); Methods++; } catch { }
                }
                foreach (var c in t.GetConstructors(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    try { RuntimeHelpers.PrepareMethod(c.MethodHandle); Methods++; } catch { }
            }
        }
        Ms = sw.Elapsed.TotalMilliseconds;
    }
}
