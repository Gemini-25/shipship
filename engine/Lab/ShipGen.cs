using System.Text.Encodings.Web;
using System.Text.Json;
using ShipEngine;

namespace Lab;

/// <summary>
/// 배 도면 생성기 (Lab --shipgen): 방을 칠한다(사각형 · 둥근 띠 · 아무 칸 모음) → 서로 다른 방이 맞닿은 칸은 벽(통로가 이긴다)
/// → 칠한 곳 둘레가 외판 → 이을 방 사이에 문 · 바깥문 → 방마다 설비 자리 → 다리 · 날개 같은 뼈대 → 스스로 검사.
/// 같은 입력 → 같은 도면. 칸 하나 = 1m.
/// </summary>
public sealed partial class ShipPlan
{
    public const int Out = -1, Wall = -2;

    public sealed class Room
    {
        public string Type = "", Name = "";
        public int Prio;
        public (string Type, int N)[] Furn = Array.Empty<(string, int)>();
    }

    public readonly string Key, Name, About;
    public readonly int W, H, Crew;
    public readonly double WaterL, CoolantKg;
    /// <summary>물 재생기 처리량 — 태울 사람 하나에 시간당 0.6L (하루 14L: 마시고 씻고 내리는 오수 하루 9~11L의 1.3~1.6배 — 깊이 고침 DF6).</summary>
    public const double WaterLphPerCrew = 0.6;
    public int Robots, Drones;
    public readonly List<Room> Rooms = new();
    public readonly List<(int X, int Y, bool Vertical, bool External, string Kind)> Doors = new();
    public readonly List<(string Type, List<(int X, int Y)> Cells)> Furniture = new();

    private readonly int[] _g;
    private char[] _ch = Array.Empty<char>();
    private readonly List<(int A, int B, (double X, double Y)? At, string Kind)> _links = new();
    private readonly List<(int Room, char Side, string Kind)> _exits = new();
    private readonly HashSet<(int, int)> _struts = new();
    private readonly Dictionary<(int, int), int> _occ = new();

    public ShipPlan(string key, string name, int w, int h, int crew, double waterL, double coolantKg, string about)
    {
        Key = key; Name = name; W = w; H = h; Crew = crew; WaterL = waterL; CoolantKg = coolantKg; About = about;
        _g = Enumerable.Repeat(Out, w * h).ToArray();
    }

    // ───────────── 설비 크기 · 놓는 법 ─────────────

    /// <summary>설비 크기 (가로 × 세로) — 놓을 때 돌려 본다.</summary>
    public static readonly Dictionary<string, (int W, int H)> Size = new()
    {
        ["Bed"] = (1, 2), ["MedBed"] = (1, 2), ["Seat"] = (1, 1), ["CrashSeat"] = (1, 1), ["Table"] = (2, 2), ["GameTable"] = (2, 1),
        ["Shelf"] = (2, 1), ["Bookshelf"] = (2, 1), ["Console"] = (1, 1), ["NavComputer"] = (1, 1), ["MainComputer"] = (2, 2),
        ["SensorArray"] = (1, 1), ["Battery"] = (1, 2), ["CapacitorBank"] = (1, 1), ["PowerPanel"] = (1, 1), ["SurgeProtector"] = (1, 1),
        ["ReactorCore"] = (3, 3), ["LargeReactorCore"] = (4, 4), ["EngineCore"] = (3, 3), ["AuxGenerator"] = (2, 1),
        ["CoolantPump"] = (1, 2), ["LargeCoolantPump"] = (2, 2), ["HeatExchanger"] = (1, 1), ["OxygenGenerator"] = (2, 1),
        ["Scrubber"] = (1, 1), ["Dehumidifier"] = (1, 1), ["WaterRecycler"] = (2, 1), ["GreywaterFilter"] = (1, 1),
        ["GrowBed"] = (4, 1), ["GrowRack"] = (4, 1), ["NutrientDoser"] = (1, 1), ["Fridge"] = (1, 1), ["Stove"] = (2, 1), ["Oven"] = (1, 1),
        ["MealDispenser"] = (1, 1), ["CoffeeMachine"] = (1, 1), ["Workbench"] = (2, 1), ["SolderStation"] = (1, 1), ["Lathe"] = (2, 1),
        ["Refinery"] = (2, 2), ["Compactor"] = (1, 1), ["Collector"] = (2, 1), ["Autoclave"] = (1, 1), ["DiagnosticScanner"] = (1, 1),
        ["SuitLocker"] = (1, 1), ["OxygenMaskBox"] = (1, 1), ["RobotDock"] = (1, 1), ["DroneDock"] = (1, 1), ["Treadmill"] = (1, 2),
        ["Projector"] = (1, 1), ["MemorialWall"] = (2, 1), ["Aquarium"] = (2, 1), ["WashingMachine"] = (1, 1), ["SuitDryer"] = (1, 1),
        ["FoodLocker"] = (1, 1), ["CargoCrate"] = (2, 2), ["CargoNet"] = (1, 1), ["OreBin"] = (2, 2), ["Rover"] = (3, 2), ["Shuttle"] = (7, 3), ["GravityGenerator"] = (2, 1),
        ["MeetingBoard"] = (2, 1), ["Fermenter"] = (1, 1), ["LedPanel"] = (1, 1), ["EmergencyLight"] = (1, 1),
        // 꾸밈 (ShipDress.cs)
        ["ToolWall"] = (2, 1), ["PartTestBench"] = (2, 1), ["MusicCorner"] = (2, 1), ["ServerRack"] = (1, 1), ["Telescope"] = (1, 1),
        // 화장실 · 샤워실 (함장 요청)
        ["Toilet"] = (1, 1), ["Sink"] = (1, 1), ["Shower"] = (1, 1),
    };

    /// <summary>줄지어 놓는 것 (줄 사이 통로 · 줄 끝 한 칸 비움).</summary>
    private static readonly HashSet<string> Rows = new() { "GrowRack", "GrowBed", "FoodLocker", "CargoCrate", "OreBin" };
    /// <summary>다른 설비 바로 옆에 붙어도 되는 것 (나머지는 한 칸 띄운다).</summary>
    private static readonly HashSet<string> Touch = new()
    {
        "FoodLocker", "Seat", "CrashSeat", "SuitLocker", "RobotDock", "DroneDock", "Console", "Battery", "CargoNet", "Scrubber", "Dehumidifier",
        "HeatExchanger", "SensorArray", "NavComputer", "PowerPanel", "SurgeProtector", "OxygenMaskBox", "CoolantPump", "MealDispenser", "Fridge",
        "Oven", "CoffeeMachine", "SolderStation", "Compactor", "NutrientDoser", "Toilet", "Sink", "Shower",
    };

    public static (string, int) F(string type, int n = 1) => (type, n);
    /// <summary>들어가는 만큼 (많아야 n · 적어도 하나).</summary>
    public static (string, int) Fill(string type, int n) => (type, -n);

    // ───────────── 칠하기 ─────────────

    private int G(int x, int y) => x >= 0 && y >= 0 && x < W && y < H ? _g[y * W + x] : Out;
    private char C(int x, int y) => x >= 0 && y >= 0 && x < W && y < H ? _ch[y * W + x] : ' ';
    private bool IsWall(int x, int y) => C(x, y) is '#' or 'H';

    public int Paint(IEnumerable<(int X, int Y)> cells, string type, string name, int prio = 0, params (string, int)[] furn)
    {
        int id = Rooms.Count;
        Rooms.Add(new Room { Type = type, Name = name, Prio = prio, Furn = furn });
        foreach (var (x, y) in cells)
            if (x >= 1 && y >= 1 && x < W - 1 && y < H - 1) _g[y * W + x] = id;
        return id;
    }

    public int Rect(int x0, int y0, int x1, int y1, string type, string name, Func<int, int, bool>? mask = null, params (string, int)[] furn)
        => Paint(Cells(x0, y0, x1, y1, mask), type, name, 0, furn);

    public int Corridor(int x0, int y0, int x1, int y1, string name = "통로", Func<int, int, bool>? mask = null)
        => Paint(Cells(x0, y0, x1, y1, mask), "Corridor", name, 1);

    /// <summary>이미 있는 방에 칸을 더한다 (통로 가지 · 돛대 같은 것을 한 방으로).</summary>
    public void Extend(int room, IEnumerable<(int X, int Y)> cells)
    {
        foreach (var (x, y) in cells)
            if (x >= 1 && y >= 1 && x < W - 1 && y < H - 1) _g[y * W + x] = room;
    }

    public int Area(IEnumerable<(int X, int Y)> cells, string type, string name, params (string, int)[] furn) => Paint(cells, type, name, 0, furn);

    /// <summary>각도 (도 · 0 = 오른쪽 · 시계 방향 — y가 아래로 자라므로).</summary>
    public static double Ang(double dx, double dy) { double a = Math.Atan2(dy, dx) * 180 / Math.PI; return a < 0 ? a + 360 : a; }

    /// <summary>a가 [a0, a1) 안인가 (도 · 360을 넘어 감아도).</summary>
    public static bool InAng(double a, double a0, double a1) { double rel = ((a - a0) % 360 + 360) % 360; return rel < a1 - a0; }

    public static IEnumerable<(int X, int Y)> Box(int x0, int y0, int x1, int y1, Func<int, int, bool>? mask = null) => Cells(x0, y0, x1, y1, mask);

    private static IEnumerable<(int, int)> Cells(int x0, int y0, int x1, int y1, Func<int, int, bool>? mask)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (mask == null || mask(x, y)) yield return (x, y);
    }

    /// <summary>
    /// 통로(cy0~cy1 줄) 위('N') 또는 아래('S')에 방을 나란히 — (안쪽 너비, 종류, 이름, 설비). 방마다 통로로 문. 높이 h는 안쪽 높이.
    /// 종류가 "Corridor"면 그 통로의 가지(같은 방)로 칠한다 — 너비 그대로 (실험실 꼬투리로 가는 길 같은 것).
    /// </summary>
    public int[] Row(int x, int cy0, int cy1, char side, int h, int cor, params (int W, string Type, string Name, (string, int)[] Furn)[] specs)
    {
        var ids = new int[specs.Length];
        RowX.Clear();
        for (int i = 0; i < specs.Length; i++)
        {
            var (w, type, name, furn) = specs[i];
            int y0 = side == 'N' ? cy0 - 1 - h : cy1 + 1, y1 = side == 'N' ? cy0 - 1 : cy1 + 1 + h;
            if (type == "Corridor")
            {
                Extend(cor, Cells(x, y0, x + w - 1, y1, null));
                RowX.Add((x, x + w - 1));
                ids[i] = cor; x += w;
                continue;
            }
            bool beforeCor = i + 1 < specs.Length && specs[i + 1].Type == "Corridor";
            int x0 = x, x1 = x + w - 1 + (i > 0 ? 1 : 0) + (beforeCor ? 1 : 0); x = x1 + 1;
            ids[i] = Paint(Cells(x0, y0, x1, y1, null), type, name, 0, furn);
            RowX.Add((x0, x1));
            Link(ids[i], cor);
        }
        return ids;
    }

    /// <summary>마지막 Row가 칠한 칸 범위 (가로 — 칸마다).</summary>
    public readonly List<(int X0, int X1)> RowX = new();

    /// <summary>둥근 띠의 한 조각 (중심 cx, cy · 반지름 r0~r1 · 각도 a0~a1 도 — 0도는 오른쪽, 시계 방향).</summary>
    public IEnumerable<(int, int)> Arc(double cx, double cy, double r0, double r1, double a0, double a1)
    {
        for (int y = (int)(cy - r1) - 1; y <= (int)(cy + r1) + 1; y++)
            for (int x = (int)(cx - r1) - 1; x <= (int)(cx + r1) + 1; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy, r = Math.Sqrt(dx * dx + dy * dy);
                if (r < r0 || r >= r1) continue;
                double a = Math.Atan2(dy, dx) * 180 / Math.PI; if (a < 0) a += 360;
                double lo = ((a0 % 360) + 360) % 360, span = a1 - a0;
                double rel = ((a - lo) % 360 + 360) % 360;
                if (rel < span) yield return (x, y);
            }
    }

    public void Link(int a, int b, (double, double)? at = null, string kind = "normal") => _links.Add((a, b, at, kind));
    public void Exit(int room, char side, string kind = "airlock") => _exits.Add((room, side, kind));
    public void SetLinkKind(int room, string kind)
    {
        for (int i = 0; i < _links.Count; i++) if (_links[i].A == room || _links[i].B == room) _links[i] = _links[i] with { Kind = kind };
    }

    public void StrutPoly(params (double X, double Y)[] pts)
    {
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        for (int y = (int)Math.Floor(y0); y <= (int)Math.Ceiling(y1); y++)
            for (int x = (int)Math.Floor(x0); x <= (int)Math.Ceiling(x1); x++)
                if (Inside(pts, x + 0.5, y + 0.5)) _struts.Add((x, y));
    }

    public void StrutLine((double X, double Y) a, (double X, double Y) b, double width = 2)
    {
        int n = (int)(Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) * 2) + 1;
        for (int k = 0; k <= n; k++)
        {
            double t = (double)k / n, cx = a.X + (b.X - a.X) * t, cy = a.Y + (b.Y - a.Y) * t;
            for (int y = (int)Math.Floor(cy - width / 2); y <= (int)Math.Floor(cy + width / 2); y++)
                for (int x = (int)Math.Floor(cx - width / 2); x <= (int)Math.Floor(cx + width / 2); x++)
                    if ((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy) <= width * width / 4 + 0.1) _struts.Add((x, y));
        }
    }

    public void StrutDisc(double cx, double cy, double r)
    {
        for (int y = (int)(cy - r) - 1; y <= (int)(cy + r) + 1; y++)
            for (int x = (int)(cx - r) - 1; x <= (int)(cx + r) + 1; x++)
                if ((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy) <= r * r) _struts.Add((x, y));
    }

    private static bool Inside((double X, double Y)[] p, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            if ((p[i].Y > y) != (p[j].Y > y) && x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) c = !c;
        return c;
    }

    // ───────────── 짓기 ─────────────

    public ShipPlan Build()
    {
        Carve(); DropFragments(); MakeWalls(); PlaceDoors(); PlaceExits(); PlaceFurniture(); if (Dress) DressRooms(); AddStruts(); Check();
        return this;
    }

    private void Fail(string msg) => throw new InvalidOperationException($"{Key}: {msg}");

    /// <summary>서로 다른 방이 맞닿은 칸 → 벽 (통로가 이긴다 · 같으면 나중에 칠한 방이 진다).</summary>
    private void Carve()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int r = _g[y * W + x];
                    if (r < 0) continue;
                    foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= W || ny >= H) continue;
                        int n = _g[ny * W + nx];
                        if (n < 0 || n == r) continue;
                        int pr = Rooms[r].Prio, pn = Rooms[n].Prio;
                        bool loseMe = pr < pn || (pr == pn && r > n);
                        if (loseMe) { _g[y * W + x] = Wall; changed = true; break; }
                        _g[ny * W + nx] = Wall; changed = true;
                    }
                }
        }
    }

    /// <summary>벽을 깎다 떨어져 나간 조각(방의 가장 큰 덩어리가 아닌 칸)은 벽으로.</summary>
    private void DropFragments()
    {
        for (int r = 0; r < Rooms.Count; r++)
        {
            var cells = CellsOf(r).ToHashSet();
            var best = new HashSet<(int, int)>();
            var seen = new HashSet<(int, int)>();
            foreach (var st in cells.OrderBy(c => c.Y).ThenBy(c => c.X))
            {
                if (seen.Contains(st)) continue;
                var comp = new HashSet<(int, int)> { st }; seen.Add(st);
                var q = new Queue<(int, int)>(); q.Enqueue(st);
                while (q.Count > 0) { var (x, y) = q.Dequeue(); foreach (var (dx, dy) in Four) { var nb = (x + dx, y + dy); if (cells.Contains(nb) && seen.Add(nb)) { comp.Add(nb); q.Enqueue(nb); } } }
                if (comp.Count > best.Count) best = comp;
            }
            foreach (var c in cells) if (!best.Contains(c)) _g[c.Y * W + c.X] = Wall;
        }
    }

    private void MakeWalls()
    {
        _ch = new char[W * H];
        for (int i = 0; i < W * H; i++) _ch[i] = _g[i] >= 0 ? '.' : _g[i] == Wall ? '#' : ' ';
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (_ch[y * W + x] != ' ') continue;
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++) for (int dx = -1; dx <= 1; dx++) if (G(x + dx, y + dy) >= 0) { near = true; break; }
                if (near) _ch[y * W + x] = '#';
            }
        var hull = new List<int>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (_ch[y * W + x] != '#') continue;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (C(x + dx, y + dy) == ' ') { hull.Add(y * W + x); dy = 2; break; }
            }
        foreach (int i in hull) _ch[i] = 'H';
    }

    private void PlaceDoors()
    {
        var taken = new List<(int X, int Y)>();
        foreach (var (a, b, at0, kind) in _links)
        {
            var cands = new List<(int X, int Y, bool V)>();
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    if (C(x, y) != '#') continue;
                    int l = G(x - 1, y), r = G(x + 1, y), u = G(x, y - 1), d = G(x, y + 1);
                    if ((l == a && r == b || l == b && r == a) && IsWall(x, y - 1) && IsWall(x, y + 1)) cands.Add((x, y, false));
                    if ((u == a && d == b || u == b && d == a) && IsWall(x - 1, y) && IsWall(x + 1, y)) cands.Add((x, y, true));
                }
            cands.RemoveAll(c => taken.Any(t => Math.Abs(c.X - t.X) + Math.Abs(c.Y - t.Y) <= 1));
            if (cands.Count == 0)
            {
                // 계단처럼 꺾인 벽(둥근 고리의 대각선 쪽): 양옆이 벽이 아니어도 마주 본 두 칸이 그 두 방이면 문 (공기는 네 이웃으로만 흐르니 닫으면 막힌다)
                for (int y = 1; y < H - 1; y++)
                    for (int x = 1; x < W - 1; x++)
                    {
                        if (C(x, y) != '#') continue;
                        int l = G(x - 1, y), r = G(x + 1, y), u = G(x, y - 1), d = G(x, y + 1);
                        if ((l == a && r == b || l == b && r == a) && u != a && d != a && u is not Out && d is not Out) cands.Add((x, y, false));
                        if ((u == a && d == b || u == b && d == a) && l != a && r != a && l is not Out && r is not Out) cands.Add((x, y, true));
                    }
                cands.RemoveAll(c => taken.Any(t => Math.Abs(c.X - t.X) + Math.Abs(c.Y - t.Y) <= 1));
            }
            if (cands.Count == 0) Fail($"{Rooms[a].Name} ↔ {Rooms[b].Name} 사이에 문 자리가 없다");
            var at = at0 ?? (cands.Average(c => c.X), cands.Average(c => c.Y));
            var pick = cands.OrderBy(c => (c.X - at.Item1) * (c.X - at.Item1) + (c.Y - at.Item2) * (c.Y - at.Item2)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
            _ch[pick.Y * W + pick.X] = 'D';
            taken.Add((pick.X, pick.Y));
            Doors.Add((pick.X, pick.Y, pick.V, false, kind));
        }
    }

    private void PlaceExits()
    {
        foreach (var (room, side, kind) in _exits)
        {
            var (dx, dy) = side switch { 'N' => (0, -1), 'S' => (0, 1), 'W' => (-1, 0), _ => (1, 0) };
            var cands = new List<(int X, int Y)>();
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    if (C(x, y) != 'H' || G(x - dx, y - dy) != room) continue;
                    int ox = x + dx, oy = y + dy;
                    if (C(ox, oy) != ' ' || _struts.Contains((ox, oy)) || ox <= 0 || oy <= 0 || ox >= W - 1 || oy >= H - 1) continue;
                    if (dx == 0 && !(IsWall(x - 1, y) && IsWall(x + 1, y))) continue;
                    if (dy == 0 && !(IsWall(x, y - 1) && IsWall(x, y + 1))) continue;
                    cands.Add((x, y));
                }
            if (cands.Count == 0) Fail($"{Rooms[room].Name} {side}쪽에 바깥문 자리가 없다");
            double mx = cands.Average(c => c.X), my = cands.Average(c => c.Y);
            var p = cands.OrderBy(c => (c.X - mx) * (c.X - mx) + (c.Y - my) * (c.Y - my)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
            _ch[p.Y * W + p.X] = 'D';
            Doors.Add((p.X, p.Y, dy != 0, true, kind));
        }
    }

    private static readonly (int, int)[] Four = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private List<(int X, int Y)> CellsOf(int r)
    {
        var l = new List<(int, int)>();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (_g[y * W + x] == r) l.Add((x, y));
        return l;
    }

    /// <summary>사람이 머물거나 일하는 방 — 산소 마스크함을 하나씩 둔다 (ISS: 모듈마다 휴대용 호흡기 · 소화기).</summary>
    private static readonly HashSet<string> MaskRooms = new()
    {
        "Quarters", "Mess", "Galley", "Medbay", "Lounge", "LifeSupport", "Hydroponics", "Workshop", "Reactor", "Power", "Gym",
        "Cooling", "ComputerRoom", "Comms", "School", "Chapel", "Bridge", "Lab", "Storage", "Hangar",
    };
    public const int MasksPerBox = 6;

    /// <summary>방의 칸 · 비워 둘 문 앞 칸 (드나드는 길).</summary>
    private (HashSet<(int, int)> set, HashSet<(int, int)> keep) RoomSpace(int r, HashSet<(int, int)> doorCells)
    {
        var cells = CellsOf(r);
        var set = cells.ToHashSet();
        var keep = new HashSet<(int, int)>();
        foreach (var (x, y) in cells) foreach (var (dx, dy) in Four) if (doorCells.Contains((x + dx, y + dy))) keep.Add((x, y));
        foreach (var (x, y) in keep.ToList()) foreach (var (dx, dy) in Four) if (set.Contains((x + dx, y + dy))) keep.Add((x + dx, y + dy));
        return (set, keep);
    }

    private void PlaceFurniture()
    {
        var doorCells = Doors.Select(d => (d.X, d.Y)).ToHashSet();
        for (int r = 0; r < Rooms.Count; r++)
        {
            var room = Rooms[r];
            if (room.Furn.Length == 0) continue;
            var (set, keep) = RoomSpace(r, doorCells);
            foreach (var (type, n0) in room.Furn)
                for (int k = 0, n = Math.Abs(n0); k < n; k++)
                    if (!(Rows.Contains(type) ? PutRow(type, set, keep) : PutOne(type, set, keep)))
                    {
                        if (n0 < 0 && k > 0) break;
                        Fail($"{room.Name}에 {type} {k + 1}/{n}번째 자리가 없다 (칸 {set.Count} · 문 앞 {keep.Count} · 이미 놓은 {set.Count(c => _occ.ContainsKey(c))} · 빈 칸이 이어져 있나 {FreeOk(set, keep, new())})");
                    }
        }
        // 비상 호흡기: 사람이 머무는 방마다 산소 마스크함 하나 (자리가 없으면 건너뛴다) · 다 합쳐 사람 수보다 마스크가 많게 (넓은 방부터 더 놓는다)
        //   (기준 평가가 찾은 결함 — 살핌호에는 마스크함이 없었고 · 새터호는 40명에 마스크 6개였다: 큰 구멍 뒤 옅은 공기에 쓸 마스크가 없었다)
        int boxes = Furniture.Count(f => f.Type == "OxygenMaskBox");
        for (int r = 0; r < Rooms.Count; r++)
        {
            if (!MaskRooms.Contains(Rooms[r].Type) || Rooms[r].Furn.Any(f => f.Type == "OxygenMaskBox")) continue;
            var (set, keep) = RoomSpace(r, doorCells);
            if (PutOne("OxygenMaskBox", set, keep)) boxes++;
        }
        var big = Enumerable.Range(0, Rooms.Count).Where(r => MaskRooms.Contains(Rooms[r].Type) || Rooms[r].Type == "Corridor").OrderByDescending(r => CellsOf(r).Count).ThenBy(r => r).ToList();
        for (int pass = 0; pass < 4 && boxes * MasksPerBox < Crew + 2; pass++)
            foreach (int r in big)
            {
                if (boxes * MasksPerBox >= Crew + 2) break;
                var (set, keep) = RoomSpace(r, doorCells);
                if (PutOne("OxygenMaskBox", set, keep)) boxes++;
            }
        if (boxes * MasksPerBox < Crew) Fail($"산소 마스크 {boxes * MasksPerBox}개 < 사람 {Crew}");
        // 인공 중력 발생기 (바퀴 3): 배마다 하나 — 엔진실 · 원자로실 · 배전실 차례로 자리가 나는 곳 · 다른 설비를 다 놓은 뒤에 (설비 번호가 밀리지 않게)
        bool grav = false;
        foreach (string type in new[] { "Engine", "Reactor", "Power" })
        {
            for (int r = 0; r < Rooms.Count && !grav; r++)
            {
                if (Rooms[r].Type != type) continue;
                var (set, keep) = RoomSpace(r, doorCells);
                grav = PutOne("GravityGenerator", set, keep);
            }
            if (grav) break;
        }
        if (!grav) Fail("인공 중력 발생기 자리가 없다 (엔진실 · 원자로실 · 배전실)");
    }

    /// <summary>방의 빈 칸(설비 없는)이 한 덩어리이고 문 앞 칸을 다 품나.</summary>
    private bool FreeOk(HashSet<(int, int)> set, HashSet<(int, int)> keep, List<(int, int)> extra)
    {
        var ex = extra.ToHashSet();
        var free = set.Where(c => !_occ.ContainsKey(c) && !ex.Contains(c)).ToHashSet();
        if (free.Count == 0 || keep.Any(k => !free.Contains(k))) return false;
        var first = free.First();
        var seen = new HashSet<(int, int)> { first };
        var q = new Queue<(int, int)>(); q.Enqueue(first);
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var (dx, dy) in Four) { var nb = (x + dx, y + dy); if (free.Contains(nb) && seen.Add(nb)) q.Enqueue(nb); }
        }
        return seen.Count == free.Count;
    }

    private bool TouchesFree(List<(int X, int Y)> piece, HashSet<(int, int)> set)
    {
        var ps = piece.ToHashSet();
        foreach (var (x, y) in piece) foreach (var (dx, dy) in Four) { var nb = (x + dx, y + dy); if (set.Contains(nb) && !_occ.ContainsKey(nb) && !ps.Contains(nb)) return true; }
        return false;
    }

    private bool NextToOther(List<(int X, int Y)> piece)
    {
        foreach (var (x, y) in piece) foreach (var (dx, dy) in Four) if (_occ.ContainsKey((x + dx, y + dy))) return true;
        return false;
    }

    private int WallScore(List<(int X, int Y)> piece, HashSet<(int, int)> set)
    {
        var ps = piece.ToHashSet(); int sc = 0;
        foreach (var (x, y) in piece) foreach (var (dx, dy) in Four) { var nb = (x + dx, y + dy); if (!set.Contains(nb) && !ps.Contains(nb) && IsWall(nb.Item1, nb.Item2)) sc++; }
        return sc;
    }

    private void Put(string type, List<(int X, int Y)> piece)
    {
        foreach (var p in piece) _occ[p] = Furniture.Count;
        Furniture.Add((type, piece));
    }

    private bool PutOne(string type, HashSet<(int, int)> set, HashSet<(int, int)> keep)
    {
        var (w, h) = Size.GetValueOrDefault(type, (1, 1));
        bool touch = Touch.Contains(type);
        var cands = new List<(int Score, int Y, int X, List<(int, int)> Piece)>();
        foreach (var (ww, hh) in new[] { (w, h), (h, w) }.Distinct())
            foreach (var (x, y) in set)
            {
                var piece = new List<(int, int)>();
                for (int j = 0; j < hh; j++) for (int i = 0; i < ww; i++) piece.Add((x + i, y + j));
                if (piece.Any(p => !set.Contains(p) || _occ.ContainsKey(p) || keep.Contains(p))) continue;
                if (!touch && NextToOther(piece)) continue;
                cands.Add((-WallScore(piece, set), y, x, piece));
            }
        foreach (var c in cands.OrderBy(c => c.Score).ThenBy(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Piece.Count > 1 && c.Piece[1].Item2 != c.Y ? 1 : 0))
        {
            if (!TouchesFree(c.Piece, set) || !FreeOk(set, keep, c.Piece)) continue;
            Put(type, c.Piece);
            return true;
        }
        return false;
    }

    /// <summary>줄지어: 방이 넓은 쪽으로 줄을 긋고 한 줄 건너 한 줄 — 줄 끝은 한 칸씩 비운다 (돌아 나가는 길).</summary>
    private bool PutRow(string type, HashSet<(int X, int Y)> set, HashSet<(int, int)> keep)
    {
        var (w, h) = Size[type];
        bool wide = set.Max(c => c.X) - set.Min(c => c.X) >= set.Max(c => c.Y) - set.Min(c => c.Y);
        int len = Math.Max(w, h), th = Math.Min(w, h);
        bool gap = type != "FoodLocker";
        int lo = wide ? set.Min(c => c.Y) : set.Min(c => c.X), hi = wide ? set.Max(c => c.Y) : set.Max(c => c.X);
        for (int line = lo + 1; line + th - 1 <= hi; line += th + 1)
        {
            var along = set.Where(c => (wide ? c.Y : c.X) == line).Select(c => wide ? c.X : c.Y).ToList();
            if (along.Count == 0) continue;
            for (int pos = along.Min() + 1; pos + len - 1 <= along.Max() - 1; pos++)
            {
                var piece = new List<(int, int)>();
                for (int i = 0; i < len; i++) for (int j = 0; j < th; j++) piece.Add(wide ? (pos + i, line + j) : (line + j, pos + i));
                if (piece.Any(p => !set.Contains(p) || _occ.ContainsKey(p) || keep.Contains(p))) continue;
                if (gap && NextToOther(piece)) continue;
                if (!TouchesFree(piece, set) || !FreeOk(set, keep, piece)) continue;
                Put(type, piece);
                return true;
            }
        }
        return PutOne(type, set, keep);
    }

    private void AddStruts()
    {
        foreach (var (x, y) in _struts)
            if (x > 0 && y > 0 && x < W - 1 && y < H - 1 && _ch[y * W + x] == ' ') _ch[y * W + x] = 'H';
    }

    // ───────────── 검사 ─────────────

    private void Check()
    {
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int r = G(x, y);
                if (r < 0) continue;
                if (G(x + 1, y) >= 0 && G(x + 1, y) != r || G(x, y + 1) >= 0 && G(x, y + 1) != r) Fail($"({x},{y}) 방 둘이 벽 없이 맞닿았다");
            }
        for (int r = 0; r < Rooms.Count; r++)
        {
            var cells = CellsOf(r);
            if (cells.Count == 0) Fail($"{Rooms[r].Name} 칸이 없다");
            var cs = cells.ToHashSet(); var seen = new HashSet<(int, int)> { cells[0] }; var q = new Queue<(int, int)>(); q.Enqueue(cells[0]);
            while (q.Count > 0) { var (x, y) = q.Dequeue(); foreach (var (dx, dy) in Four) { var nb = (x + dx, y + dy); if (cs.Contains(nb) && seen.Add(nb)) q.Enqueue(nb); } }
            if (seen.Count != cs.Count) Fail($"{Rooms[r].Name}이(가) 둘로 끊겼다");
        }
        var adj = Enumerable.Range(0, Rooms.Count).Select(_ => new HashSet<int>()).ToArray();
        foreach (var d in Doors)
        {
            if (d.External) continue;
            int a = d.Vertical ? G(d.X, d.Y - 1) : G(d.X - 1, d.Y), b = d.Vertical ? G(d.X, d.Y + 1) : G(d.X + 1, d.Y);
            adj[a].Add(b); adj[b].Add(a);
        }
        var reach = new HashSet<int> { 0 }; var rq = new Queue<int>(); rq.Enqueue(0);
        while (rq.Count > 0) foreach (int n in adj[rq.Dequeue()]) if (reach.Add(n)) rq.Enqueue(n);
        var lost = Enumerable.Range(0, Rooms.Count).Where(r => !reach.Contains(r)).Select(r => Rooms[r].Name).ToList();
        if (lost.Count > 0) Fail($"문으로 닿지 않는 방: {string.Join(", ", lost)}");
        int beds = Furniture.Count(f => f.Type == "Bed");
        if (beds != Crew) Fail($"침대 {beds} ≠ 사람 {Crew}");
        if (!Doors.Any(d => d.External)) Fail("바깥문이 없다");
        if (Furniture.Count(f => f.Type == "RobotDock") < Robots) Fail($"로봇 충전대가 로봇({Robots})보다 적다");
    }

    // ───────────── 내보내기 ─────────────

    public string Json()
    {
        var rooms = Rooms.Select((r, i) => new { id = i, type = r.Type, name = r.Name, cells = CellsOf(i).Select(c => c.Y * W + c.X).OrderBy(c => c).ToArray() });
        var rows = Enumerable.Range(0, H).Select(y => new string(_ch, y * W, W).TrimEnd()).ToArray();
        var o = new
        {
            key = Key, name = Name, about = About, w = W, h = H, waterL = WaterL, waterLph = Math.Round(Crew * WaterLphPerCrew, 1), coolantKg = CoolantKg, crew = Crew, robots = Robots, drones = Drones,
            rows,
            rooms,
            doors = Doors.Select(d => new { x = d.X, y = d.Y, vertical = d.Vertical, external = d.External, kind = d.Kind }),
            furniture = Furniture.Select(f => new { type = f.Type, cells = f.Cells.OrderBy(c => c.Y).ThenBy(c => c.X).Select(c => new[] { c.X, c.Y }) }),
        };
        return JsonSerializer.Serialize(o, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static readonly Dictionary<string, char> Letter = new()
    {
        ["Bed"] = 'b', ["MedBed"] = 'm', ["Seat"] = 's', ["CrashSeat"] = 's', ["Table"] = 't', ["GameTable"] = 'g', ["Shelf"] = 'h', ["Bookshelf"] = 'k',
        ["Console"] = 'c', ["NavComputer"] = 'n', ["MainComputer"] = 'M', ["SensorArray"] = 'a', ["Battery"] = 'B', ["PowerPanel"] = 'P',
        ["SurgeProtector"] = 'p', ["ReactorCore"] = 'R', ["LargeReactorCore"] = 'R', ["EngineCore"] = 'E', ["AuxGenerator"] = 'A',
        ["CoolantPump"] = 'C', ["LargeCoolantPump"] = 'C', ["HeatExchanger"] = 'x', ["OxygenGenerator"] = 'O', ["Scrubber"] = 'o',
        ["Dehumidifier"] = 'd', ["WaterRecycler"] = 'W', ["GrowRack"] = 'G', ["GrowBed"] = 'G', ["Fridge"] = 'F', ["Stove"] = 'S', ["Oven"] = 'v',
        ["MealDispenser"] = 'e', ["Workbench"] = 'w', ["Lathe"] = 'l', ["Refinery"] = 'r', ["Collector"] = 'q', ["SuitLocker"] = 'u',
        ["RobotDock"] = 'z', ["DroneDock"] = 'y', ["Treadmill"] = 'T', ["FoodLocker"] = 'f', ["CargoCrate"] = 'X', ["OreBin"] = 'K',
        ["Rover"] = 'V', ["Shuttle"] = 'Z', ["MemorialWall"] = 'j', ["Projector"] = 'i', ["GravityGenerator"] = 'H',
    };

    /// <summary>글자 그림 (설비는 글자로).</summary>
    public string Show()
    {
        if (_ch.Length == 0) return "(벽을 세우기 전에 멈췄다)";
        var o = (char[])_ch.Clone();
        foreach (var (t, cells) in Furniture) foreach (var (x, y) in cells) o[y * W + x] = Letter.GetValueOrDefault(t, '*');
        return string.Join('\n', Enumerable.Range(0, H).Select(y => new string(o, y * W, W).TrimEnd()));
    }
}
