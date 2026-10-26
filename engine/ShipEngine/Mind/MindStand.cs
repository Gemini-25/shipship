namespace ShipEngine;

/// <summary>
/// 머물 자리 (5단 C1 — docs/STAGE5.md): 회의 · 대피 · 대피 중 잠에 설 칸을 고른다 — 문 칸 · 문 곁 · 폭 한 칸 길목을 피하고 · 남이 선 칸 · 남이 가려는 칸을 피한다.
/// 아는 것: 도면 · 남의 칸 · 남의 계획 (감각 — 참).
/// </summary>
public sealed partial class Mind
{
    private byte[]? _stand;   // 칸마다 0 괜찮다 · 1 폭 한 칸 길목 · 2 문 곁 (대각선 한 칸 안) · 3 문 칸 (도면에서 한 번 — 찍지 않는다)
    private int[] _standStamp = Array.Empty<int>();
    private int _standGen;

    /// <summary>그 칸의 머물 자리 표 (0 괜찮다 · 1 길목 · 2 문 곁 · 3 문 칸).</summary>
    public int StandKindAt(int cell)
    {
        if (_stand == null) BuildStand();
        return cell >= 0 && cell < L.N ? _stand![cell] : 3;
    }

    private void BuildStand()
    {
        _stand = new byte[L.N];
        bool Open(int x, int y) => L.In(x, y) && L.Kind[L.Index(x, y)] is CellKind.Floor or CellKind.Door;
        for (int c = 0; c < L.N; c++)
        {
            if (L.Kind[c] == CellKind.Door) { _stand[c] = 3; continue; }
            if (L.Kind[c] != CellKind.Floor) continue;
            int x = L.X(c), y = L.Y(c);
            bool nearDoor = false;
            for (int dy = -1; dy <= 1 && !nearDoor; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && L.In(x + dx, y + dy) && L.Kind[L.Index(x + dx, y + dy)] == CellKind.Door) { nearDoor = true; break; }
            if (nearDoor) { _stand[c] = 2; continue; }
            if (!Open(x - 1, y) && !Open(x + 1, y) || !Open(x, y - 1) && !Open(x, y + 1)) _stand[c] = 1;   // 양옆이 막힌 길목
        }
    }

    /// <summary>남이 선 칸 · 남의 계획 마지막 가기 칸에 도장을 찍는다 (목표와 상관없이) — 이번 도장 번호를 돌려준다.</summary>
    private int StampOthers(Agent a)
    {
        if (_stand == null) BuildStand();
        if (_standStamp.Length != L.N) _standStamp = new int[L.N];
        int gen = ++_standGen;
        if (gen == int.MaxValue) { Array.Clear(_standStamp); _standGen = gen = 1; }
        foreach (var o in Agents)
        {
            if (o == a) continue;
            if ((uint)o.C.Cell < (uint)L.N) _standStamp[o.C.Cell] = gen;
            for (int k = o.Plan.Count - 1; k >= 0; k--)
                if (o.Plan[k].Kind == StepKind.GoTo) { int g = o.Plan[k].Cell; if ((uint)g < (uint)L.N) _standStamp[g] = gen; break; }
        }
        return gen;
    }

    /// <summary>
    /// 피할 곳을 문 바로 앞에서 물린다 (5단 C4): 처음 찾은 칸 c0이 표 0이 아니면 같은 방 6걸음 안 (방 안 바닥으로)에서 같은 거름 (ok)을 지나는 표 0 칸 가운데 가장 가까운 칸 ·
    /// 남이 선 칸 · 남이 가는 칸은 건너뛴다 · 없으면 c0.
    /// </summary>
    private int RefugeSpot(Agent a, int c0, Func<int, bool> ok)
    {
        if (Environment.GetEnvironmentVariable("RS_DEBUG") != null) Console.WriteLine($"RS a{a.Id} c0=({L.X(c0)-1},{L.Y(c0)-1}) k{StandKindAt(c0)} goal {a.C.Goal}");
        if (c0 < 0 || StandKindAt(c0) == 0) return c0;
        int room = L.Room[c0];
        if (room < 0) return c0;
        int gen = StampOthers(a);
        var q = new Queue<(int c, int d)>();
        var seen = new HashSet<int> { c0 };
        q.Enqueue((c0, 0));
        Span<int> nb = stackalloc int[4];
        while (q.Count > 0)
        {
            var (c, d) = q.Dequeue();
            if (c != c0 && _stand![c] == 0 && L.FurnitureAt[c] < 0 && _standStamp[c] != gen && ok(c)) return c;
            if (d >= 6) continue;
            L.Neighbors(c, nb);
            foreach (int j in nb)
            {
                if (j < 0 || L.Kind[j] != CellKind.Floor || L.Room[j] != room || !seen.Add(j)) continue;
                q.Enqueue((j, d + 1));
            }
        }
        return c0;
    }

    /// <summary>
    /// 그 방 안에서 머물 칸: 남이 선 칸 · 남의 계획 마지막 가기 칸을 표시하고 (목표와 상관없이) · 가구 없는 바닥 가운데 1000 × 표 + 가운데까지 거리가 가장 낮은 칸 ·
    /// 내 칸이 그 방 · 표 0 · 표시 없으면 그대로 · 남는 칸이 없으면 가운데 (문 칸은 끝까지 고르지 않는다). O(방 칸 + 사람).
    /// </summary>
    private int StandSpot(Agent a, int room, int center)
    {
        if (room < 0) return center;
        int gen = StampOthers(a);
        int here = a.C.Cell;
        if ((uint)here < (uint)L.N && L.Room[here] == room && _stand![here] == 0 && _standStamp[here] != gen && L.FurnitureAt[here] < 0) return here;
        int best = -1, bs = int.MaxValue;
        foreach (int c in L.Rooms[room].Cells)
        {
            if (L.Kind[c] != CellKind.Floor || L.FurnitureAt[c] >= 0 || _stand![c] == 3 || _standStamp[c] == gen) continue;
            int sc = 1000 * _stand[c] + Manhattan(c, center);
            if (sc < bs) { bs = sc; best = c; }
        }
        return best >= 0 ? best : center;
    }

    /// <summary>
    /// 줄 서는 자리 (5단 C5): 설비와 같은 방의 가구 없는 바닥 — 차례 ① 거리 1 · 표 0~1 ② 거리 2 · 표 0 ③ 거리 1 · 표 2 (문 칸은 고르지 않는다) ·
    /// 같은 차례 안에서 300 × 표 + 100 × (그 방에 있을 때 보이는 남이 서 있다) + 내 칸까지 맨해튼 거리가 가장 낮은 칸 · 없으면 -1 (예전대로 설비 곁 1칸까지).
    /// 아는 것: 도면 · 같은 방일 때 눈에 보이는 남의 칸.
    /// </summary>
    private int QueueSpot(Agent a, int fu)
    {
        if (fu < 0 || fu >= L.Furniture.Count) return -1;
        int cell = L.Furniture[fu].Cells[0], room = L.Room[cell];
        if (room < 0) return -1;
        if (_stand == null) BuildStand();
        int here = a.C.Cell;
        bool inRoom = (uint)here < (uint)L.N && L.Room[here] == room;
        int x0 = L.X(cell), y0 = L.Y(cell);
        for (int tier = 0; tier < 3; tier++)
        {
            int best = -1, bs = int.MaxValue;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = x0 + dx, y = y0 + dy, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (d == 0 || !L.In(x, y)) continue;
                    int j = L.Index(x, y);
                    if (L.Kind[j] != CellKind.Floor || L.Room[j] != room || L.FurnitureAt[j] >= 0) continue;
                    int k = _stand![j];
                    bool fits = tier switch { 0 => d == 1 && k <= 1, 1 => d == 2 && k == 0, _ => d == 1 && k == 2 };
                    if (!fits) continue;
                    int score = 300 * k + Manhattan(here, j);
                    if (inRoom && Visible(a, j, room, 12))
                        foreach (var o in Agents) if (o != a && o.C.Cell == j) { score += 100; break; }
                    if (score < bs) { bs = score; best = j; }
                }
            if (best >= 0) return best;
        }
        return -1;
    }
}
