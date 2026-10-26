namespace ShipEngine;

/// <summary>
/// ④ 치우기 · 세우기의 요청 (docs/PHYSICS.md §7 판단 · PH13 — ⑧이 부른다 · 세계는 셈만): 칸들에 놓인 깨진 조각을 쓸어 담고 가루를 쓴다 (장부 '쓸어 담았다') ·
/// 누운 물건 · 넘어진 가구를 든다 (드는 이의 힘 — 그보다 무거우면 남긴다).
/// </summary>
public sealed partial class Structure
{
    /// <summary>쓸어 담아 배의 바닥에서 뺀 깨진 조각 · 가루 (kg — 장부).</summary>
    public double SweptKg;
    public long SweptPieces;

    /// <summary>그 칸들에 쓸어 담을 것 (놓인 깨진 조각 · 가루)이 있나.</summary>
    public bool HasLitter(ReadOnlySpan<int> cells)
    {
        foreach (int c in cells) if (c >= 0 && c < Grit.Length && Grit[c] > 0) return true;
        foreach (var p in Props) if (p.Kind == 2 && p.State == 0 && cells.Contains(CellOf(p))) return true;
        return false;
    }

    /// <summary>그 칸들에 세울 것 (누운 물건 · 넘어진 가구 — 놓인 것)이 있나 (무게 한도 아래만 · 무거운 것 수도).</summary>
    public (int light, int heavy) Fallen(ReadOnlySpan<int> cells, double maxKg)
    {
        int light = 0, heavy = 0;
        foreach (var p in Props)
        {
            if (p.Kind is not (0 or 1) || p.State != 0 || p.Pose == 0 || (p.Flags & DecorFlag) != 0 || !cells.Contains(CellOf(p))) continue;
            if (MassOf(p).kg <= maxKg) light++; else heavy++;
        }
        return (light, heavy);
    }

    /// <summary>배 안 깨진 조각 상한 · 가루로 부서질 무게 (kg) — 그 아래 조각만.</summary>
    public const int PieceCap = 400;
    public const double DustKg = 0.05;
    /// <summary>셈 (찍지 않는다): 상한을 넘어 가루로 부서진 조각.</summary>
    public long Crumbled;

    /// <summary>
    /// 깨진 조각 (Kind 2 · 배 안)이 400을 넘으면 50g 아래 것을 가장 가벼운 것부터 (같으면 Id 차례) 가루로 — 그 칸 바닥에 앉고 (가루 장부) · 지닌 운동량은 그 칸의 몸이 되받는다 (몸이 없으면 바깥 몫).
    /// 끓는 행성의 92기압 돌풍처럼 한 틱에 깨짐이 깨짐을 불러 조각이 수천이 되면 섬 하나가 짝 수만을 1024 잔걸음 세어 틱이 몇 분 걸렸다 — 아주 작은 조각은 거친 흐름에 가루가 된다는 어림으로 셈을 붙든다.
    /// </summary>
    private void Crumble(bool world)
    {
        int pieces = 0;
        foreach (ref readonly var p in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props)) if (p.Kind == 2 && p.State is not (5 or 6 or 7)) pieces++;
        if (pieces <= PieceCap) return;
        var dust = new List<(float kg, int id)>();
        foreach (ref readonly var p in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props)) if (p.Kind == 2 && p.State is not (5 or 6 or 7) && p.Kg < DustKg) dust.Add((p.Kg, p.Id));
        dust.Sort();
        int n = Math.Min(dust.Count, pieces - PieceCap);
        if (n <= 0) return;
        var gone = new HashSet<int>();
        for (int k = 0; k < n; k++) gone.Add(dust[k].id);
        int nb = Bodies.Count;
        Span<double> back = nb <= 16 ? stackalloc double[3 * nb] : new double[3 * nb];
        for (int i = Props.Count - 1; i >= 0; i--)
        {
            var p = Props[i];
            if (!gone.Contains(p.Id)) continue;
            var (m, inertia) = MassOf(p);
            var (x, y, vx, vy, w) = PropWorld(p);
            int c = CellOf(p), b = world && c >= 0 ? BodyOf(c) : -1;
            if (b >= 0 && b < nb && Bodies[b].Mass > 0) { back[3 * b] += m * vx; back[3 * b + 1] += m * vy; back[3 * b + 2] += m * (x * vy - y * vx) + inertia * w; }
            else { ToHostPx += m * vx; ToHostPy += m * vy; ToHostL += m * (x * vy - y * vx) + inertia * w; }
            if (c >= 0) { Grit[c] += (float)m; GritMat[c] = p.Mat; }
            GritKg += m;
            Props.RemoveAt(i);
            Crumbled++;
            _clutterDirty = true;
        }
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);
        for (int b = 0; b < nb; b++)
            if (back[3 * b] != 0 || back[3 * b + 1] != 0 || back[3 * b + 2] != 0) TakeBack(all, b, -back[3 * b], -back[3 * b + 1], -back[3 * b + 2], false);   // 가루가 잃은 운동량을 그 몸이 받는다
    }

    /// <summary>요청 (⑧ 치우기): 그 칸들에 놓인 깨진 조각 (Kind 2 · 놓임)을 쓸어 담고 가루를 쓴다 → 담은 질량 (kg).</summary>
    public double Sweep(ReadOnlySpan<int> cells)
    {
        double kg = 0;
        foreach (int c in cells)
            if (c >= 0 && c < Grit.Length && Grit[c] > 0) { kg += Grit[c]; GritKg -= Grit[c]; Grit[c] = 0; GritMat[c] = 0; }
        for (int i = Props.Count - 1; i >= 0; i--)
        {
            var p = Props[i];
            if (p.Kind != 2 || p.State != 0 || !cells.Contains(CellOf(p))) continue;
            var (m, inertia) = MassOf(p);
            var (x, y, vx, vy, w) = PropWorld(p);
            PropPxGone += m * vx; PropPyGone += m * vy; PropLGone += m * (x * vy - y * vx) + inertia * w;   // 쓸어 담은 것이 가져간 운동량 (몸에 실려 가던 것 — 장부가 맞게 · PH13 검토 고침)
            kg += m;
            Props.RemoveAt(i);
            SweptPieces++;
        }
        SweptKg += kg;
        if (kg > 0) _clutterDirty = true;   // 치운 칸의 길 값
        return kg;
    }

    /// <summary>요청 (⑧ 세우기): 그 칸들에 누운 물건 · 넘어진 가구를 세운다 (드는 이의 힘 maxKg 아래만 — 무거운 것은 남긴다) → 세운 수.</summary>
    public int RightUp(ReadOnlySpan<int> cells, double maxKg)
    {
        int n = 0;
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);
        for (int i = 0; i < all.Length; i++)
        {
            ref var p = ref all[i];
            if (p.Kind is not (0 or 1) || p.State != 0 || p.Pose == 0 || (p.Flags & DecorFlag) != 0 || !cells.Contains(CellOf(p))) continue;
            if (MassOf(p).kg > maxKg) continue;
            p.Pose = 0; p.Th = 0; p.Vx = p.Vy = p.W = 0;
            FillBody(ref p);   // 선 꼴 (질량은 그대로)
            if (p.Kind == 1) _clutterDirty = true;   // 세운 가구 — 길 값은 다음에 물을 때 다시 (되살린 세계와 같게 · PH13 검토 고침)
            Righted++;
            n++;
        }
        return n;
    }
    public long Righted;
}
