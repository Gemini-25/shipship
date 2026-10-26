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

    /// <summary>
    /// 위기 길 데우기 (MIND.md 6단): 메서드를 미리 옮겨도 첫 불에서 처음 도는 길 (일반화 메서드 · 처음 채우는 표 · 정적 준비)은 남는다 (새뜰 첫 위기 가장 긴 틱 6.9ms → 미리 옮기기만 3.9ms).
    /// 띄운 세계의 복제 하나에 불 넷을 내고 낮은 우선순위 스레드에서 몇 분 돌려 그 길을 한 번 지나게 한다 — 복제라 세계는 그대로 (결정론과 상관없다 · 복제는 버린다).
    /// </summary>
    public static Thread? CrisisJob { get; private set; }
    public static double CrisisMs;
    public static void Crisis(Sim live, int ticks = 150)
    {
        if (CrisisJob != null) return;
        var c = live.Clone();   // 부르는 스레드에서 찍는다 (세계를 돌리는 스레드)
        CrisisJob = new Thread(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                c.Find<Structure>()?.WarmForms();   // 가구 · 물건 도트 꼴 (정적 표 — 첫 위기에 든 것을 내려놓은 곳 곁 가구 꼴을 처음 짓던 멈칫)
                var chem = c.Find<Chemistry>();
                var room = c.Layout.Rooms.Where(r => r.Cells.Length > 20 && r.Type != "Corridor").OrderByDescending(r => r.Cells.Length).FirstOrDefault();
                if (chem != null && room != null)
                    foreach (int cell in room.Cells.Where((x, i) => i % 9 == 0 && c.Layout.Kind[x] == CellKind.Floor).Take(4)) chem.Ignite(cell, 3e6, c.Causes.Add(CauseKind.Setup, "데우기 불", c.Tick, cell));
                // 물체 길 (PHYSICS.md §4-2 — 감압 · 막음 · 깨짐 · 몸 물체 · 판 조각): 둘째로 큰 방 외판에 작은 구멍 · 곁에 물건 몇 · 큰 파편 하나 — 복제라 세계는 그대로
                var st = c.Find<Structure>(); var f = c.Find<Field>(); var L = c.Layout;
                var other = L.Rooms.Where(r => r.Cells.Length > 12 && r != room).OrderByDescending(r => r.Cells.Length).FirstOrDefault();
                if (st != null && f != null && other != null)
                {
                    int hull = -1, near = -1;
                    var nb = new int[4];
                    foreach (int x in other.Cells)
                    {
                        L.Neighbors(x, nb);
                        foreach (int j in nb) if (j >= 0 && L.Hull[j] && L.Kind[j] == CellKind.Wall) { hull = j; near = x; break; }
                        if (hull >= 0) break;
                    }
                    if (hull >= 0)
                    {
                        int n = c.Tables.People.Items.Count;
                        foreach (int x in other.Cells.Where(x => L.Kind[x] == CellKind.Floor && L.FurnitureAt[x] < 0).Take(3).Append(near)) st.PutItem(x % Math.Max(1, n), 1, x);
                        f.SetHole(hull, 0.08f);
                        st.Impact(c, hull, 4e6, 0, 0, c.Causes.Add(CauseKind.Setup, "데우기 파편", c.Tick, hull));
                    }
                }
                c.Run(ticks);
            }
            catch { }
            CrisisMs = sw.Elapsed.TotalMilliseconds;
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "위기 길 데우기" };
        CrisisJob.Start();
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
