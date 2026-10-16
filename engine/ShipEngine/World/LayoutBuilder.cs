namespace ShipEngine;

/// <summary>
/// 글자 그림으로 도면 만들기 (시험 · 관찰 장면용).
/// <code>
///   ' ' 바깥 · '#' 벽 · '.' 바닥 · 'D' 보통 문 · 'P' 차압 문 · 'A' 바깥 문(에어락)
///   그 밖의 글자는 legend로 설비를 둔다 (그 칸은 바닥 + 설비).
/// </code>
/// 바닥 칸들을 벽 · 문으로 나눠 방을 저절로 찾는다. 둘레에 바깥 칸 한 줄을 덧댄다.
/// </summary>
public static class LayoutBuilder
{
    public static Layout FromAscii(string name, string art, Tables t, Dictionary<char, string>? legend = null,
                                   string wall = "steel", string hull = "steel", string floor = "steel")
    {
        var rows = art.Replace("\r", "").Split('\n').Where(r => r.Trim().Length > 0).ToList();
        int w0 = rows.Max(r => r.Length);
        var L = new Layout { Key = name, Name = name, W = w0 + 2, H = rows.Count + 2 };
        L.Kind = new CellKind[L.N];
        L.Material = new byte[L.N];
        var furnCells = new SortedDictionary<char, List<int>>();
        byte wm = (byte)t.M(wall).Index, hm = (byte)t.M(hull).Index, fm = (byte)t.M(floor).Index;
        for (int y = 0; y < rows.Count; y++)
            for (int x = 0; x < w0; x++)
            {
                char ch = x < rows[y].Length ? rows[y][x] : ' ';
                int i = L.Index(x + 1, y + 1);
                switch (ch)
                {
                    case ' ': L.Kind[i] = CellKind.Outside; break;
                    case '#': L.Kind[i] = CellKind.Wall; L.Material[i] = wm; break;
                    case '.': L.Kind[i] = CellKind.Floor; L.Material[i] = fm; break;
                    case 'D': case 'P': case 'A':
                        L.Kind[i] = CellKind.Door; L.Material[i] = fm;
                        L.Doors.Add(new DoorDef { Id = L.Doors.Count, Cell = i, Kind = ch == 'D' ? "normal" : ch == 'P' ? "pressure" : "airlock", External = ch == 'A' });
                        break;
                    default:
                        if (legend == null || !legend.ContainsKey(ch)) throw new ArgumentException($"도면 글자 '{ch}'를 모른다");
                        L.Kind[i] = CellKind.Floor; L.Material[i] = fm;
                        if (!furnCells.TryGetValue(ch, out var list)) furnCells[ch] = list = new();
                        list.Add(i);
                        break;
                }
            }
        // 문의 방향: 좌우가 벽이면 위아래를 잇는 문
        foreach (var d in L.Doors)
        {
            int x = L.X(d.Cell), y = L.Y(d.Cell);
            bool wallLR = L.Kind[L.Index(x - 1, y)] == CellKind.Wall && L.Kind[L.Index(x + 1, y)] == CellKind.Wall;
            d.Vertical = wallLR;
        }
        // 외판 재질
        for (int i = 0; i < L.N; i++)
        {
            if (L.Kind[i] != CellKind.Wall) continue;
            int x = L.X(i), y = L.Y(i);
            bool outside = false;
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                if (!L.In(x + dx, y + dy) || L.Kind[L.Index(x + dx, y + dy)] == CellKind.Outside) outside = true;
            if (outside) L.Material[i] = hm;
        }
        // 방 찾기 (바닥 칸끼리 — 문 · 벽에서 끊긴다)
        var seen = new bool[L.N];
        var q = new Queue<int>();
        Span<int> nb = stackalloc int[4];
        for (int i = 0; i < L.N; i++)
        {
            if (L.Kind[i] != CellKind.Floor || seen[i]) continue;
            var cells = new List<int>();
            seen[i] = true; q.Enqueue(i);
            while (q.Count > 0)
            {
                int c = q.Dequeue(); cells.Add(c);
                L.Neighbors(c, nb);
                foreach (int j in nb)
                    if (j >= 0 && !seen[j] && L.Kind[j] == CellKind.Floor) { seen[j] = true; q.Enqueue(j); }
            }
            cells.Sort();
            L.Rooms.Add(new RoomDef { Id = L.Rooms.Count, Type = "Room", Name = $"방{L.Rooms.Count + 1}", Cells = cells.ToArray() });
        }
        foreach (var (ch, cells) in furnCells)
            L.Furniture.Add(new FurnitureDef { Id = L.Furniture.Count, Type = legend![ch], Cells = cells.ToArray() });
        L.Finish();
        foreach (var f in L.Furniture) f.Room = L.Room[f.Cells[0]];
        return L;
    }
}
