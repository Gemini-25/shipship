namespace ShipEngine;

/// <summary>틈: 구역 둘(또는 구역과 바깥)을 잇는 좁은 곳.</summary>
public sealed class Link
{
    public int A, B;            // 양쪽 칸 (B = -1: 바깥)
    public int RA, RB;          // 양쪽 구역 (RB = -1: 바깥)
    public int Via;             // 틈이 있는 칸 (문 · 벽 칸) — 바람 방향
    public double Area;         // m² (구멍 틈은 막은 만큼 줄어든다 — ③ 국소 현상 · 틱마다 Area0에서 다시 센다)
    public double Area0;        // 틈을 지을 때의 넓이
    public LinkKind Kind;
    public int[] NearA = Array.Empty<int>(), NearB = Array.Empty<int>();
    public double[] WA = Array.Empty<double>(), WB = Array.Empty<double>();
    /// <summary>지난 틱 흐름 (몰/초, A→B가 +).</summary>
    public double Flow;
    /// <summary>바람 계산용 구역 안 거리 (구역 칸 순서) — 처음 쓸 때 잰다.</summary>
    public int[]? DistA, DistB;
    /// <summary>이번 틱에 잘게 나눠 흘릴 틈 (지난 틱에 크게 흘렀다).</summary>
    public bool Fast;
    /// <summary>우주로 트인 틈: 바깥 쪽 방향 (도면 좌표 단위 벡터 — 바닥 · 천장 구멍은 판 밖이라 0) · 이번 틱에 빠진 기체가 가져간 운동량 (N·s — ④ 몸이 반대로 밀린다).</summary>
    public float Dx, Dy;
    public double Impulse;
}

public enum LinkKind : byte { DoorGap, DoorAjar, Crack, Breach, Open }

public sealed partial class Field
{
    private bool _dirty = true;
    /// <summary>칸 → 구역 번호 (-1: 기체가 못 드는 칸).</summary>
    public int[] RegionOf = Array.Empty<int>();
    public List<int[]> Regions { get; } = new();
    public List<Link> Links { get; } = new();
    /// <summary>바뀔 때마다 오른다 (위층 캐시용).</summary>
    public int TopologyVersion { get; private set; }

    private void BuildSolids()
    {
        var mats = T.Materials;
        double KOf(int i) => mats[L.Material[i]].Def.K;
        for (int i = 0; i < Cells; i++)
        {
            var m = mats[L.Material[i]].Def;
            Cs[i] = L.Kind[i] switch
            {
                CellKind.Floor => m.Density * m.C * 0.02,     // 바닥판 2cm
                CellKind.Door => m.Density * m.C * 0.015,     // 문짝
                CellKind.Wall => m.Density * m.C * 0.075,     // 벽: 두 겹 판 + 골조 (유효 두께 7.5cm)
                _ => 0,
            };
        }
        SolidLinks();
    }

    /// <summary>고체끼리 이웃 열전도 · 우주를 보는 외판 면 — 도면과 끊긴 칸(<see cref="Severed"/>)에서 (처음 · 끊길 때 · 되살릴 때).</summary>
    private void SolidLinks()
    {
        var mats = T.Materials;
        double KOf(int i) => mats[L.Material[i]].Def.K;
        Span<int> nb = stackalloc int[4];
        for (int i = 0; i < Cells; i++)
        {
            _kRight[i] = 0; _kDown[i] = 0; _exposed[i] = 0;
            if (IsOut(i)) continue;
            int x = L.X(i), y = L.Y(i);
            if (x + 1 < L.W && !IsOut(i + 1)) _kRight[i] = SolidK(i, i + 1, KOf);
            if (y + 1 < L.H && !IsOut(i + L.W)) _kDown[i] = SolidK(i, i + L.W, KOf);
            if (L.Kind[i] == CellKind.Wall)
            {
                L.Neighbors(i, nb);
                byte e = 0;
                foreach (int j in nb) if (j < 0 || IsOut(j)) e++;
                _exposed[i] = e;
            }
        }
    }

    private double SolidK(int a, int b, Func<int, double> k)
    {
        var ka = L.Kind[a]; var kb = L.Kind[b];
        if (ka == CellKind.Outside || kb == CellKind.Outside) return 0;
        bool fa = ka != CellKind.Wall, fb = kb != CellKind.Wall;
        double h = 2 * k(a) * k(b) / Math.Max(1e-9, k(a) + k(b)); // 두 재질의 조화 평균
        if (fa && fb) return h * 0.02;      // 바닥판끼리 (두께 2cm × 폭 1m / 1m)
        if (!fa && !fb) return h * 0.125;   // 벽끼리 (5cm × 2.5m / 1m)
        return 5.0 * WallFace;              // 바닥(방 공기) ↔ 벽: 공기 대류 h=5 W/m²K × 2.5m²
    }

    /// <summary>구역 · 틈을 다시 짠다 (문이 열고 닫히거나 구멍이 바뀌면).</summary>
    private void Rebuild()
    {
        _dirty = false;
        TopologyVersion++;
        PushOutTrapped();
        BuildTopology();
        MarkAllDirty();
    }

    /// <summary>되살릴 때: 마지막으로 구역을 지은 때의 문 · 구멍으로 같은 구역 · 틈을 다시 세운다 (기체는 건드리지 않는다 — 원래 세계와 바이트까지 같게).</summary>
    private void RestoreTopology()
    {
        if (_topoDoor.Length != DoorOpen.Length || _topoHole.Length != Hole.Length || _topoTaped.Length != DoorTaped.Length || _topoBleed.Length != DoorBleed.Length) { _dirty = true; Rebuild(); return; }
        var door = DoorOpen; var hole = Hole; var taped = DoorTaped; var bleed = DoorBleed;
        DoorOpen = (float[])_topoDoor.Clone(); Hole = (float[])_topoHole.Clone(); DoorTaped = (byte[])_topoTaped.Clone(); DoorBleed = (byte[])_topoBleed.Clone();
        BuildTopology();
        DoorOpen = door; Hole = hole; DoorTaped = taped; DoorBleed = bleed;
    }

    private float[] _topoDoor = Array.Empty<float>(), _topoHole = Array.Empty<float>(); // 마지막으로 구역을 지은 때의 문 · 구멍 · 막은 문틈 (찍는다)
    private byte[] _topoTaped = Array.Empty<byte>();
    private byte[] _topoBleed = Array.Empty<byte>();

    /// <summary>1. 기체가 못 드는 칸(닫힌 문 등)에 남은 기체는 이웃으로 밀어낸다.</summary>
    private void PushOutTrapped()
    {
        Span<int> nb = stackalloc int[4];

        // 1. 기체가 못 드는 칸(닫힌 문 등)에 남은 기체는 이웃으로 밀어낸다
        for (int i = 0; i < Cells; i++)
        {
            if (Passable(i) || L.Kind[i] == CellKind.Outside) continue;
            if (Moles(i) <= 0 && U[i] <= 0) continue;
            L.Neighbors(i, nb);
            int cnt = 0;
            foreach (int j in nb) if (j >= 0 && Passable(j)) cnt++;
            if (cnt == 0) continue;
            foreach (int j in nb)
            {
                if (j < 0 || !Passable(j)) continue;
                for (int g = 0; g < G; g++) N[j * G + g] += N[i * G + g] / cnt;
                U[j] += U[i] / cnt;
            }
            for (int g = 0; g < G; g++) N[i * G + g] = 0;
            U[i] = 0;
        }

    }

    /// <summary>2 · 3. 구역 (칸 번호 순서로 — 결정론) · 틈.</summary>
    private void BuildTopology()
    {
        Span<int> nb = stackalloc int[4];
        if (_topoDoor.Length != DoorOpen.Length) _topoDoor = new float[DoorOpen.Length];
        if (_topoHole.Length != Hole.Length) _topoHole = new float[Hole.Length];
        if (_topoTaped.Length != DoorTaped.Length) _topoTaped = new byte[DoorTaped.Length];
        if (_topoBleed.Length != DoorBleed.Length) _topoBleed = new byte[DoorBleed.Length];
        Array.Copy(DoorOpen, _topoDoor, DoorOpen.Length); Array.Copy(Hole, _topoHole, Hole.Length); Array.Copy(DoorTaped, _topoTaped, DoorTaped.Length); Array.Copy(DoorBleed, _topoBleed, DoorBleed.Length);
        if (RegionOf.Length != Cells) RegionOf = new int[Cells];
        Array.Fill(RegionOf, -1);
        Regions.Clear();
        var q = new Queue<int>();
        var list = new List<int>();
        for (int i = 0; i < Cells; i++)
        {
            if (RegionOf[i] >= 0 || !Passable(i)) continue;
            int id = Regions.Count;
            list.Clear();
            RegionOf[i] = id; q.Enqueue(i);
            while (q.Count > 0)
            {
                int c = q.Dequeue(); list.Add(c);
                L.Neighbors(c, nb);
                foreach (int j in nb)
                    if (j >= 0 && RegionOf[j] < 0 && Passable(j)) { RegionOf[j] = id; q.Enqueue(j); }
            }
            list.Sort();
            Regions.Add(list.ToArray());
        }

        // 3. 틈
        Links.Clear();
        for (int d = 0; d < L.Doors.Count; d++)
        {
            if (DoorPassable(d)) continue;
            var door = L.Doors[d];
            double area = DoorOpen[d] > 0 ? DoorOpen[d] * DoorArea : DoorBleed[d] != 0 ? BleedArea : door.Kind == "normal" && DoorTaped[d] == 0 ? LeakNormalDoor : 0;
            if (area <= 0) continue;
            int c = door.Cell, x = L.X(c), y = L.Y(c);
            var (a, b) = door.Vertical ? (Cell(x, y - 1), Cell(x, y + 1)) : (Cell(x - 1, y), Cell(x + 1, y));
            AddLink(a, b, c, area, DoorOpen[d] > 0 ? LinkKind.DoorAjar : LinkKind.DoorGap);
        }
        for (int i = 0; i < Cells; i++)
        {
            if (L.Kind[i] == CellKind.Wall && Hole[i] > 0 && Hole[i] < 2f)
            {
                int x = L.X(i), y = L.Y(i);
                int l = Cell(x - 1, y), r = Cell(x + 1, y), u = Cell(x, y - 1), dn = Cell(x, y + 1);
                if (Side(l) && Side(r) && (Gasy(l) || Gasy(r))) AddLink(l, r, i, Hole[i], Hole[i] >= 0.05f ? LinkKind.Breach : LinkKind.Crack);
                else if (Side(u) && Side(dn) && (Gasy(u) || Gasy(dn))) AddLink(u, dn, i, Hole[i], Hole[i] >= 0.05f ? LinkKind.Breach : LinkKind.Crack);
            }
            // 트인 칸이 바깥과 바로 맞닿음 (뚫린 외판 · 열린 바깥 문 · 떨어져 나간 자리)
            if (Passable(i))
            {
                L.Neighbors(i, nb);
                foreach (int j in nb)
                    if (j < 0 || IsOut(j)) { AddLink(i, -1, i, L.Kind[i] == CellKind.Door ? DoorArea : WallFace, LinkKind.Open); break; }
                // 바닥 · 천장 판의 구멍 (한 층짜리 배 — 그 너머가 우주다 · 폭발 압력에 뚫린다)
                if (L.Kind[i] == CellKind.Floor && Hole[i] > 0) AddLink(i, -1, i, Hole[i], Hole[i] >= 0.05f ? LinkKind.Breach : LinkKind.Crack);
            }
        }
        foreach (var k in Links)
        {
            (k.NearA, k.WA) = Near(k.A, k.RA);
            if (k.B >= 0) (k.NearB, k.WB) = Near(k.B, k.RB);
        }
    }

    private int Cell(int x, int y) => L.In(x, y) ? L.Index(x, y) : -1;
    /// <summary>틈의 한쪽이 될 수 있나: 트인 칸이거나 바깥.</summary>
    private bool Side(int c) => c < 0 || IsOut(c) || Passable(c);
    private bool Gasy(int c) => c >= 0 && Passable(c);

    private void AddLink(int a, int b, int via, double area, LinkKind kind)
    {
        // 바깥 쪽은 B로
        if (a < 0 || IsOut(a)) (a, b) = (b, a);
        if (a < 0 || !Passable(a)) return;
        int ra = RegionOf[a];
        int rb = b >= 0 && !IsOut(b) && Passable(b) ? RegionOf[b] : -1;
        if (rb < 0) b = -1;
        if (ra == rb) return; // 같은 구역 안의 금 — 압력이 같다
        var k = new Link { A = a, B = b, RA = ra, RB = rb, Via = via, Area = area, Area0 = area, Kind = kind };
        if (rb < 0) (k.Dx, k.Dy) = OutDir(a, via, kind);
        Links.Add(k);
    }

    /// <summary>바깥과의 틈이 트인 쪽 (도면 좌표 단위 벡터): 벽 구멍은 안 칸 → 구멍 칸 · 바깥과 맞닿은 칸은 바깥 이웃 쪽의 합 · 바닥 · 천장 구멍은 0 (위아래가 마주 본다).</summary>
    private (float, float) OutDir(int a, int via, LinkKind kind)
    {
        double dx = 0, dy = 0;
        if (via != a) { dx = L.X(via) - L.X(a); dy = L.Y(via) - L.Y(a); }
        else if (kind == LinkKind.Open)
        {
            int x = L.X(a), y = L.Y(a);
            foreach (var (ox, oy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int j = Cell(x + ox, y + oy);
                if (j < 0 || IsOut(j)) { dx += ox; dy += oy; }
            }
        }
        double n = Math.Sqrt(dx * dx + dy * dy);
        return n > 1e-9 ? ((float)(dx / n), (float)(dy / n)) : (0f, 0f);
    }

    /// <summary>틈 곁 칸들과 무게 (거리 d에서 e^(−d/2) · 반지름 6).</summary>
    private (int[], double[]) Near(int start, int region)
    {
        const int Radius = 6;
        var cells = new List<int>(); var ws = new List<double>();
        var dist = new Dictionary<int, int> { [start] = 0 };
        var q = new Queue<int>(); q.Enqueue(start);
        Span<int> nb = stackalloc int[4];
        while (q.Count > 0)
        {
            int c = q.Dequeue(); int d = dist[c];
            cells.Add(c); ws.Add(Math.Exp(-d / 2.0));
            if (d >= Radius) continue;
            L.Neighbors(c, nb);
            foreach (int j in nb)
                if (j >= 0 && RegionOf[j] == region && !dist.ContainsKey(j)) { dist[j] = d + 1; q.Enqueue(j); }
        }
        return (cells.ToArray(), ws.ToArray());
    }

    /// <summary>구역 안 각 칸의 '틈까지 거리' (바람 계산).</summary>
    private int[] DistanceFrom(int start, int region, int[] buffer)
    {
        foreach (int c in Regions[region]) buffer[c] = -1;
        buffer[start] = 0;
        var q = new Queue<int>(); q.Enqueue(start);
        Span<int> nb = stackalloc int[4];
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            L.Neighbors(c, nb);
            foreach (int j in nb)
                if (j >= 0 && RegionOf[j] == region && buffer[j] < 0) { buffer[j] = buffer[c] + 1; q.Enqueue(j); }
        }
        return buffer;
    }

    /// <summary>위층이 구역을 바로 알아야 할 때 (문을 연 직후 등).</summary>
    public void RebuildNow() { if (_dirty) Rebuild(); }
}
