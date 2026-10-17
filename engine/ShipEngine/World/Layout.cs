using System.Text.Json;

namespace ShipEngine;

/// <summary>칸의 종류 (도면 — 바뀌지 않는 것). 문이 열렸는지 · 벽에 구멍이 났는지는 엔진 상태에 있다.</summary>
public enum CellKind : byte { Outside, Floor, Wall, Door }

public sealed class RoomDef
{
    public int Id;
    public string Type = "";
    public string Name = "";
    public int[] Cells = Array.Empty<int>();
}

public sealed class DoorDef
{
    public int Id;
    public int Cell;
    /// <summary>위아래 칸을 잇는 문 (아니면 좌우).</summary>
    public bool Vertical;
    /// <summary>배 바깥으로 나가는 문 (에어락 바깥문).</summary>
    public bool External;
    /// <summary>normal(보통 — 닫혀도 조금 샌다) · pressure(차압 문 — 닫히면 막힌다) · airlock.</summary>
    public string Kind = "normal";
}

public sealed class FurnitureDef
{
    public int Id;
    public string Type = "";
    public int[] Cells = Array.Empty<int>();
    public int Room = -1;
}

/// <summary>
/// 배 도면: 칸 종류 · 방 · 문 · 설비 자리 · 벽과 바닥 재질. 바뀌지 않는다 (복제가 같은 것을 함께 쓴다).
/// 칸 번호 = y × W + x. 도면 둘레에는 바깥 칸이 한 줄 이상 있다 (외판 바깥이 늘 바깥이 되게).
/// </summary>
public sealed class Layout
{
    public string Key = "", Name = "", About = "";
    public int W, H;
    /// <summary>처음 물탱크 (L) · 냉각재 (kg) — 0이면 엔진 기본값.</summary>
    public double WaterL, CoolantKg;
    public CellKind[] Kind = Array.Empty<CellKind>();
    /// <summary>칸 → 방 번호 (-1: 방 아님).</summary>
    public int[] Room = Array.Empty<int>();
    /// <summary>칸 → 문 번호 (-1: 문 아님).</summary>
    public int[] DoorAt = Array.Empty<int>();
    /// <summary>칸 → 설비 번호 (-1: 없음).</summary>
    public int[] FurnitureAt = Array.Empty<int>();
    /// <summary>벽 칸 → 재질 번호 · 바닥 칸 → 바닥 재질 번호.</summary>
    public byte[] Material = Array.Empty<byte>();
    /// <summary>외판: 바깥 칸과 맞닿은 벽 칸.</summary>
    public bool[] Hull = Array.Empty<bool>();
    public List<RoomDef> Rooms = new();
    public List<DoorDef> Doors = new();
    public List<FurnitureDef> Furniture = new();

    public int N => W * H;
    public int Index(int x, int y) => y * W + x;
    public int X(int i) => i % W;
    public int Y(int i) => i / W;
    public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

    /// <summary>바닥 · 열린 문처럼 기체가 들 수 있는 칸 (문은 열렸을 때만 — 그건 엔진이 본다).</summary>
    public bool IsSpace(int i) => Kind[i] is CellKind.Floor or CellKind.Door;

    /// <summary>네 이웃 (도면 밖은 -1).</summary>
    public void Neighbors(int i, Span<int> four)
    {
        int x = i % W, y = i / W;
        four[0] = x > 0 ? i - 1 : -1;
        four[1] = x < W - 1 ? i + 1 : -1;
        four[2] = y > 0 ? i - W : -1;
        four[3] = y < H - 1 ? i + W : -1;
    }

    /// <summary>도면 정리: 외판 표시 · 칸→방/문/설비 표 (불러온 뒤 한 번).</summary>
    public void Finish()
    {
        Room = Enumerable.Repeat(-1, N).ToArray();
        foreach (var r in Rooms) foreach (int c in r.Cells) Room[c] = r.Id;
        DoorAt = Enumerable.Repeat(-1, N).ToArray();
        foreach (var d in Doors) DoorAt[d.Cell] = d.Id;
        FurnitureAt = Enumerable.Repeat(-1, N).ToArray();
        foreach (var f in Furniture) foreach (int c in f.Cells) FurnitureAt[c] = f.Id;
        Hull = new bool[N];
        Span<int> nb = stackalloc int[4];
        for (int i = 0; i < N; i++)
        {
            if (Kind[i] != CellKind.Wall) continue;
            Neighbors(i, nb);
            foreach (int j in nb) if (j < 0 || Kind[j] == CellKind.Outside) { Hull[i] = true; break; }
        }
        if (Material.Length != N) Material = new byte[N];
    }

    // ── 배 도면 파일 (engine/data/ships/*.json — Lab --shipgen 이 만든다) ──

    private sealed class ShipFile
    {
        public string Key = "", Name = "", About = "";
        public int W, H;
        public double WaterL, CoolantKg;
        public string[] Rows = Array.Empty<string>();
        public string WallMaterial = "steel", HullMaterial = "steel", FloorMaterial = "steel";
        public List<RoomDef> Rooms = new();
        public List<ShipDoor> Doors = new();
        public List<ShipFurniture> Furniture = new();
    }
    private sealed class ShipDoor { public int X, Y; public bool Vertical, External; public string Kind = "normal"; }
    private sealed class ShipFurniture { public string Type = ""; public int[][] Cells = Array.Empty<int[]>(); }

    public static IEnumerable<string> ShipKeys() =>
        DataFiles.Names("ships/").Select(n => Path.GetFileNameWithoutExtension(n));

    /// <summary>engine/data/ships/{key}.json 을 읽는다.</summary>
    public static Layout Ship(string key, Tables t) => FromJson(DataFiles.Text($"ships/{key.ToLowerInvariant()}.json"), t);

    /// <summary>도면 글(JSON)을 읽는다 — 생성기가 만든 도면을 내장하기 전에 바로 검사할 때도.</summary>
    public static Layout FromJson(string json, Tables t)
    {
        var f = JsonSerializer.Deserialize<ShipFile>(json, DataFiles.Json) ?? throw new InvalidDataException("배 도면을 읽지 못했다");
        var L = new Layout { Key = f.Key, Name = f.Name, About = f.About, W = f.W, H = f.H, WaterL = f.WaterL, CoolantKg = f.CoolantKg };
        L.Kind = new CellKind[L.N];
        L.Material = new byte[L.N];
        byte wallM = (byte)t.M(f.WallMaterial).Index, hullM = (byte)t.M(f.HullMaterial).Index, floorM = (byte)t.M(f.FloorMaterial).Index;
        for (int y = 0; y < L.H; y++)
            for (int x = 0; x < L.W; x++)
            {
                char ch = x < f.Rows[y].Length ? f.Rows[y][x] : ' ';
                int i = L.Index(x, y);
                L.Kind[i] = ch switch { '#' => CellKind.Wall, 'H' => CellKind.Wall, '.' => CellKind.Floor, 'D' => CellKind.Door, _ => CellKind.Outside };
                L.Material[i] = ch == 'H' ? hullM : ch == '#' ? wallM : floorM;
            }
        L.Rooms = f.Rooms;
        for (int k = 0; k < f.Doors.Count; k++)
        {
            var d = f.Doors[k];
            L.Doors.Add(new DoorDef { Id = k, Cell = L.Index(d.X, d.Y), Vertical = d.Vertical, External = d.External, Kind = d.Kind });
        }
        for (int k = 0; k < f.Furniture.Count; k++)
        {
            var fu = f.Furniture[k];
            L.Furniture.Add(new FurnitureDef { Id = k, Type = fu.Type, Cells = fu.Cells.Select(c => L.Index(c[0], c[1])).ToArray() });
        }
        L.Finish();
        foreach (var fu in L.Furniture) fu.Room = fu.Cells.Length > 0 ? L.Room[fu.Cells[0]] : -1;
        return L;
    }
}
