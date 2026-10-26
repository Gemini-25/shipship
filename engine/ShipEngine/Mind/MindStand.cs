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

    /// <summary>
    /// 그 방 안에서 머물 칸: 남이 선 칸 · 남의 계획 마지막 가기 칸을 표시하고 (목표와 상관없이) · 가구 없는 바닥 가운데 1000 × 표 + 가운데까지 거리가 가장 낮은 칸 ·
    /// 내 칸이 그 방 · 표 0 · 표시 없으면 그대로 · 남는 칸이 없으면 가운데 (문 칸은 끝까지 고르지 않는다). O(방 칸 + 사람).
    /// </summary>
    private int StandSpot(Agent a, int room, int center)
    {
        if (room < 0) return center;
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
}
