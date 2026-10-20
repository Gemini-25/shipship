namespace Lab;

/// <summary>
/// 꾸밈 초안 (함장 요청 — 배 일곱 척에 시설 · 장치를 알맞은 밀도로 · 길은 막지 않게): 방마다 맡은 일에 맞는 것을 더 놓아 방이 이만큼 차게 한다.
/// 놓는 법은 설비와 같다 (문 앞 칸은 비우고 · 빈 칸은 한 덩어리로 이어진다) — 더해서 이미 놓인 것마다 곁에 빈 칸이 하나는 남는다 (쓰러 갈 자리).
/// 벽에 붙이는 것끼리는 맞닿아도 되고 (선반 · 사물함 줄) · 한쪽에 몰리지 않게 다른 것에서 먼 벽부터.
/// 통로는 세 칸 넘게 넓은 곳의 벽에만 드문드문 (일곱 칸에 하나 · 문 곁은 비운다).
/// 함장 승인 (docs/FURNISH.md 전부) — 배 도면에 들어간다. Lab --shipgen --draft 는 지금 도면과 견주는 그림만.
/// </summary>
public sealed partial class ShipPlan
{
    public bool Dress = true;
    /// <summary>꾸밈으로 더 놓은 것 (방 · 종류 — 초안 보고용).</summary>
    public readonly List<(int Room, string Type)> Dressed = new();

    /// <summary>방 종류마다 채울 몫(설비가 덮는 칸 / 방의 칸)과 더 놓을 것 (차례로 돌아가며 — 자리가 없거나 그 방에 '종류:몇 개'만큼 놓았으면 그 종류는 그만).</summary>
    public static readonly Dictionary<string, (double Fill, string[] Items)> Dressing = new()
    {
        ["Quarters"] = (0.32, new[] { "Shelf", "ClothesRack", "Bookshelf:2", "Seat:3" }),
        ["Mess"] = (0.32, new[] { "CoffeeMachine:1", "Shelf", "Aquarium:1", "MeetingBoard:1", "PlantRack:3", "Seat" }),
        ["Galley"] = (0.32, new[] { "Shelf", "SpiceRack:2", "DishWasher:1", "Oven:2", "IceMaker:1", "Fermenter:2" }),
        ["Bridge"] = (0.20, new[] { "Console:4", "ServerRack:2", "Shelf:4", "MeetingBoard:1", "CrashSeat:4" }),
        ["Lab"] = (0.30, new[] { "Workbench:3", "Shelf", "Bookshelf:3", "LabStill:1", "EyeWash:1", "CalibrationRig:2", "DiagnosticScanner:2" }),
        ["Comms"] = (0.28, new[] { "Console:3", "ServerRack:3", "SignalBooster:1", "ListeningPost:1", "Shelf", "Seat:2" }),
        ["Airlock"] = (0.20, new[] { "SuitLocker:4", "SuitDryer:2", "MagBootRack:2", "HeatSuitRack:2", "DeconShower:1" }),
        ["Observatory"] = (0.25, new[] { "Telescope:2", "Seat:4", "Bookshelf:2", "Console:2" }),
        ["Medbay"] = (0.30, new[] { "MedCabinet:2", "Shelf", "BloodFridge:1", "EyeWash:1", "Seat:3", "Autoclave:1" }),
        ["Workshop"] = (0.32, new[] { "ToolWall:4", "Shelf", "PartTestBench:1", "CalibrationRig:1", "Hoist:1", "MaintCart:2", "CargoNet" }),
        ["Storage"] = (0.38, new[] { "Shelf", "CargoNet", "SupplyCache:2", "Compactor:1" }),
        ["LifeSupport"] = (0.28, new[] { "Shelf", "ToolWall:2", "LeakDetector:2", "VibrationMonitor:2", "SupplyCache:1" }),
        ["Power"] = (0.28, new[] { "Shelf", "ToolWall:2", "ThermalCamera:1", "FireBlanket:1", "SupplyCache:1" }),
        ["Engine"] = (0.24, new[] { "Console:2", "ToolWall:2", "ThermalCamera:1", "VibrationMonitor:2", "Hoist:1", "FireBlanket:1", "Shelf" }),
        ["Reactor"] = (0.24, new[] { "Console:2", "ToolWall:2", "ThermalCamera:1", "VibrationMonitor:1", "FireBlanket:1", "Shelf" }),
        ["Cooling"] = (0.24, new[] { "ToolWall:2", "VibrationMonitor:2", "LeakDetector:2", "Shelf", "Hoist:1" }),
        ["Lounge"] = (0.32, new[] { "Bookshelf:3", "Seat", "MusicCorner:1", "ArcadeCabinet:2", "VrChair:2", "PlantRack:3", "Aquarium:1" }),
        ["Gym"] = (0.30, new[] { "Treadmill:3", "Shelf", "WashingMachine:1", "ClothesRack:2", "Seat:3" }),
        ["Hydroponics"] = (0.36, new[] { "PestTrap:2", "Composter:1", "PlantRack:4", "Shelf" }),
        ["Cargo"] = (0.36, new[] { "CargoNet", "Hoist:2", "Shelf" }),
        ["ComputerRoom"] = (0.30, new[] { "ServerRack:4", "Console:2", "ThermalCamera:1", "Shelf" }),
        ["Foundry"] = (0.25, new[] { "Compactor:1", "ToolWall:3", "Hoist:2", "Shelf", "OreBin:2" }),
        ["Hangar"] = (0.25, new[] { "Shelf", "CargoNet", "Hoist:2", "MaintCart:2", "SuitLocker:2" }),
        ["School"] = (0.30, new[] { "Bookshelf:4", "MeetingBoard:1", "Seat" }),
        ["Chapel"] = (0.25, new[] { "Seat", "Bookshelf:2", "PlantRack:4" }),
        ["DroneBay"] = (0.25, new[] { "Shelf", "MaintCart:1" }),
        ["Washroom"] = (0.36, new[] { "ClothesRack:2", "Shelf:1", "Dehumidifier:1" }),   // 세면대는 도면이 정한 만큼 (변기 곁)
    };

    /// <summary>방 가운데에 놓아도 되는 것 (곁의 설비에 붙여 — 탁자 곁 의자처럼) · 나머지는 벽에 붙인다.</summary>
    private static readonly HashSet<string> Middle = new() { "Seat", "CrashSeat", "VrChair", "Treadmill", "Hoist", "MaintCart", "Telescope", "Console" };
    /// <summary>통로 벽에 드문드문 (차례로 돌아가며).</summary>
    private static readonly string[] CorridorItems = { "EmergencyLight", "FireBlanket", "SupplyCache", "GrabRail", "LeakDetector" };

    private void DressRooms()
    {
        var doorCells = Doors.Select(d => (d.X, d.Y)).ToHashSet();
        var corridorPut = new List<(int X, int Y)>();
        for (int r = 0; r < Rooms.Count; r++)
        {
            var (set, keep) = RoomSpace(r, doorCells);
            if (Rooms[r].Type == "Corridor") { DressCorridor(r, set, keep, doorCells, corridorPut); continue; }
            if (!Dressing.TryGetValue(Rooms[r].Type, out var plan)) continue;
            var items = plan.Items.Select(t => t.Split(':')).Select(p => (Type: p[0], Max: p.Length > 1 ? int.Parse(p[1]) : int.MaxValue)).ToList();
            var put = new Dictionary<string, int>();
            int k = 0;
            while (items.Count > 0 && Filled(set) < plan.Fill)
            {
                int i = k % items.Count;
                var (type, max) = items[i];
                if (put.GetValueOrDefault(type) < max && DressOne(type, set, keep)) { Dressed.Add((r, type)); put[type] = put.GetValueOrDefault(type) + 1; k++; }
                else items.RemoveAt(i);
            }
        }
    }

    private double Filled(HashSet<(int, int)> set) => set.Count == 0 ? 1 : (double)set.Count(c => _occ.ContainsKey(c)) / set.Count;

    private bool DressOne(string type, HashSet<(int X, int Y)> set, HashSet<(int, int)> keep)
    {
        var (w, h) = Size.GetValueOrDefault(type, (1, 1));
        bool middle = Middle.Contains(type);
        var cands = new List<(int A, int B, int C, int Y, int X, List<(int, int)> Piece)>();
        foreach (var (ww, hh) in new[] { (w, h), (h, w) }.Distinct())
            foreach (var (x, y) in set)
            {
                var piece = new List<(int, int)>();
                for (int j = 0; j < hh; j++) for (int i = 0; i < ww; i++) piece.Add((x + i, y + j));
                if (piece.Any(p => !set.Contains(p) || _occ.ContainsKey(p) || keep.Contains(p))) continue;
                int wall = WallScore(piece, set);
                if (!middle && wall == 0) continue;                       // 벽에 붙인다
                int near = Nearest(piece, 4);
                // 벽에 붙이는 것: 다른 것에서 먼 벽부터 (한쪽에 몰리지 않게) · 가운데 것: 다른 설비 곁에 (탁자 곁 의자)
                cands.Add(middle ? (near == 1 ? 0 : 1, near, -wall, y, x, piece) : (-near, -wall, 0, y, x, piece));
            }
        foreach (var c in cands.OrderBy(c => c.A).ThenBy(c => c.B).ThenBy(c => c.C).ThenBy(c => c.Y).ThenBy(c => c.X))
        {
            if (!TouchesFree(c.Piece, set) || !FreeOk(set, keep, c.Piece) || !UsableAfter(set, c.Piece)) continue;
            Put(type, c.Piece);
            return true;
        }
        return false;
    }

    /// <summary>둘레 r칸 안에서 가장 가까운 설비까지 (체비셰프 · 없으면 r+1).</summary>
    private int Nearest(List<(int X, int Y)> piece, int r)
    {
        int best = r + 1;
        foreach (var (x, y) in piece)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (d < best && d > 0 && _occ.ContainsKey((x + dx, y + dy))) best = d;
                }
        return best;
    }

    /// <summary>이 조각을 놓아도 방의 설비마다 곁에 빈 칸이 하나는 남나 (쓰러 갈 자리).</summary>
    private bool UsableAfter(HashSet<(int, int)> set, List<(int, int)> piece)
    {
        var ps = piece.ToHashSet();
        var ids = new HashSet<int>();
        foreach (var c in set) if (_occ.TryGetValue(c, out int id)) ids.Add(id);
        foreach (int id in ids)
        {
            bool ok = false;
            foreach (var (x, y) in Furniture[id].Cells)
            {
                foreach (var (dx, dy) in Four)
                {
                    var nb = (x + dx, y + dy);
                    if (set.Contains(nb) && !_occ.ContainsKey(nb) && !ps.Contains(nb)) { ok = true; break; }
                }
                if (ok) break;
            }
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>통로: 벽 곁 칸 중 그 벽에서 두 칸 더 빈 통로가 이어지는 곳(세 칸 넘게 넓은 곳)에만 · 문 둘레 두 칸은 비우고 · 다른 꾸밈에서 일곱 칸 넘게.</summary>
    private void DressCorridor(int r, HashSet<(int X, int Y)> set, HashSet<(int, int)> keep, HashSet<(int, int)> doorCells, List<(int X, int Y)> put)
    {
        int k = put.Count;
        foreach (var (x, y) in set.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            if (_occ.ContainsKey((x, y)) || keep.Contains((x, y))) continue;
            if (doorCells.Any(d => Math.Abs(d.Item1 - x) <= 2 && Math.Abs(d.Item2 - y) <= 2)) continue;
            if (put.Any(p => Math.Abs(p.X - x) + Math.Abs(p.Y - y) < 8)) continue;
            bool wide = false;
            foreach (var (dx, dy) in Four)
            {
                if (!IsWall(x + dx, y + dy)) continue;
                var a = (x - dx, y - dy); var b = (x - 2 * dx, y - 2 * dy);
                if (set.Contains(a) && set.Contains(b) && !_occ.ContainsKey(a) && !_occ.ContainsKey(b)) { wide = true; break; }
            }
            if (!wide) continue;
            var piece = new List<(int, int)> { (x, y) };
            if (!FreeOk(set, keep, piece) || !UsableAfter(set, piece)) continue;
            string type = CorridorItems[k % CorridorItems.Length];
            Put(type, piece);
            Dressed.Add((r, type));
            put.Add((x, y));
            k++;
        }
    }
}
