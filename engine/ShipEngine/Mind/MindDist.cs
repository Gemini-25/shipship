namespace ShipEngine;

/// <summary>
/// 길 거리 (판단 결함 — docs/REVIEW.md ③-1 흔들림): 고르기가 벽을 모르는 직선 거리(맨해튼)로 대상을 고르면, 벽 너머 가까운 것과 문 둘 너머 것 사이를 오간다.
/// 도면에서 문과 문 사이 걸음 수를 한 번 셈해 두고 (찍지 않는다 — 도면에서 다시 나온다), 두 칸 사이는 '제 방의 문 → 문 사이 → 저쪽 방의 문'으로 어림한다.
/// 사람 · 로봇이 함께 쓴다 (변기 · 보관함 · 침대 · 일감 고르기).
/// </summary>
public sealed partial class Mind
{
    private int[]? _doorDist;                 // [문 × 문] 걸음 수 (못 닿으면 큰 값)
    private int[][]? _roomDoorsDist;              // [방] 그 방에 닿은 문들
    private const int FarAway = 1 << 20;

    private void BuildDoorDist()
    {
        int nd = L.Doors.Count;
        _doorDist = new int[nd * nd];
        Array.Fill(_doorDist, FarAway);
        var rooms = new List<int>[L.Rooms.Count];
        for (int r = 0; r < rooms.Length; r++) rooms[r] = new List<int>();
        Span<int> nb = stackalloc int[4];
        for (int d = 0; d < nd; d++)
        {
            L.Neighbors(L.Doors[d].Cell, nb);
            foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0 && !rooms[L.Room[j]].Contains(d)) rooms[L.Room[j]].Add(d);
        }
        _roomDoorsDist = rooms.Select(x => x.ToArray()).ToArray();
        var dist = new int[L.N];
        var q = new Queue<int>();
        for (int d = 0; d < nd; d++)
        {
            Array.Fill(dist, -1);
            int s = L.Doors[d].Cell;
            dist[s] = 0; q.Enqueue(s);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                L.Neighbors(c, nb);
                foreach (int j in nb)
                {
                    if (j < 0 || dist[j] >= 0 || !L.IsSpace(j)) continue;
                    dist[j] = dist[c] + 1; q.Enqueue(j);
                }
            }
            for (int e = 0; e < nd; e++) if (dist[L.Doors[e].Cell] >= 0) _doorDist[d * nd + e] = dist[L.Doors[e].Cell];
        }
    }

    /// <summary>두 칸 사이 걸음 수 어림 — 같은 방이면 직선 · 아니면 가장 짧은 '문 → 문' 길 (문의 열림 · 막힘은 보지 않는다: 길은 걸으며 찾는다).</summary>
    public int PathDist(int a, int b)
    {
        if (a < 0 || b < 0) return FarAway;
        int ra = L.Room[a], rb = L.Room[b];
        if (ra == rb || ra < 0 || rb < 0) return Manhattan(a, b);
        if (_doorDist == null) BuildDoorDist();
        int nd = L.Doors.Count, best = FarAway;
        foreach (int da in _roomDoorsDist![ra])
            foreach (int db in _roomDoorsDist[rb])
            {
                int dd = _doorDist![da * nd + db];
                if (dd >= FarAway) continue;
                best = Math.Min(best, Manhattan(a, L.Doors[da].Cell) + dd + Manhattan(L.Doors[db].Cell, b));
            }
        return best < FarAway ? best : Manhattan(a, b) * 3;   // 문으로 이어지지 않았다 (바깥으로 돌아야) — 멀게 친다
    }
}
