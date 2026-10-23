using ShipEngine;
using ShipSim.Art;
using ShipSim.Core;
using ShipView;

namespace Lab;

/// <summary>
/// 관찰 장면 '텍스처' (docs/GRAPHICS.md 텍스처 · `Lab --scene art`): 새 가구 견본 (예전 그림 · 새 그림 · 보는 쪽마다 · 세계 값 있음 / 없음) ×3 ·
/// 세 척 (디딤 · 새뜰 · 한누리) 방 종류마다 1080p 크기 잘라 보기 — 눈으로 스스로 검토하는 자리. 세계는 읽기만.
/// </summary>
public static partial class Program
{
    private static void ArtScene(string dir)
    {
        // 1. 가구 견본: 금속판 바닥 위에 (종류 · 크기 · 보는 쪽 · 세계 값)
        var cases = new (string type, int bw, int bh, Facing f, bool full)[]
        {
            ("Shelf", 2, 1, Facing.S, true), ("Shelf", 2, 1, Facing.S, false), ("Shelf", 2, 1, Facing.N, true), ("Shelf", 1, 2, Facing.E, true), ("Shelf", 1, 2, Facing.S, true),
            ("Bookshelf", 2, 1, Facing.S, true), ("Bookshelf", 1, 2, Facing.E, true), ("FoodLocker", 1, 1, Facing.S, true), ("CargoCrate", 2, 2, Facing.S, true),
            ("CargoNet", 1, 1, Facing.S, true), ("SuitLocker", 1, 1, Facing.S, true), ("SuitLocker", 1, 1, Facing.S, false), ("ToolWall", 2, 1, Facing.S, true), ("ToolWall", 1, 2, Facing.E, true),
            ("OxygenMaskBox", 1, 1, Facing.S, true), ("Console", 1, 1, Facing.S, true), ("GrowRack", 4, 1, Facing.S, true), ("GrowRack", 1, 4, Facing.E, true),
            ("ClothesRack", 1, 1, Facing.S, true), ("RobotDock", 1, 1, Facing.S, true), ("OxygenGenerator", 2, 1, Facing.S, true), ("OxygenGenerator", 1, 2, Facing.E, true),
            ("Bed", 2, 1, Facing.S, true), ("Bed", 1, 2, Facing.S, true),
        };
        int cellW = 5 * 32, cellH = 6 * 32, cols = 6, rowsN = (cases.Length + cols - 1) / cols;
        var sheet = new Img(cols * cellW * 2, rowsN * cellH);
        var floor = ArtFloors.Floor(Finish.Metal, 2, 2, 0, false);
        for (int y = 0; y < sheet.H; y += 32) for (int x = 0; x < sheet.W; x += 32) sheet.Draw(floor, x, y);
        for (int i = 0; i < cases.Length; i++)
        {
            var (type, bw, bh, f, full) = cases[i];
            int cx = i % cols * cellW * 2, cy = i / cols * cellH;
            var fill = full ? new FurnFill { Ext = 1, Seal = 1, Suits = 2, Masks = 6, Owner = 3 } : new FurnFill { Owner = -1 };
            // 예전 (DotMachines) · 새 그림 — 밑을 같은 줄에 맞춘다
            var oldType = Enum.TryParse<FurnitureType>(type switch { "FoodLocker" or "CargoCrate" => "SupplyCache", "GrowRack" => "PlantRack", _ => type }, out var ft) ? ft : FurnitureType.Shelf;
            var old = DotMachines.Body(oldType, bw, bh, -1, 1, 0, i);
            int baseY = cy + cellH - 16;
            sheet.Draw(old, cx + 12, baseY - bh * 32 - DotMachines.RiseOf(oldType));
            var body = ArtFurniture.Body(type, bw, bh, f, fill, i);
            sheet.Draw(body, cx + cellW + 12, baseY - body.H);
            var glow = ArtFurniture.Glow(type, bw, bh, f, 1, 4);
            if (glow != null) sheet.Draw(glow, cx + cellW + 12, baseY - body.H);
            for (int k = 0; k < cellH; k++) sheet.Set(cx + 2 * cellW - 1, cy + k, new Rgba(255, 255, 255, 80));
        }
        var big = sheet.Scale(3);
        Png.WriteRgba(Path.Combine(dir, "furniture.png"), big.W, big.H, big.Px);
        Console.WriteLine($"  art: {dir}/furniture.png ({big.W}×{big.H}) — 칸마다 왼쪽 예전 · 오른쪽 새 그림");
        // 2. 세 척 방 종류마다 잘라 보기 (10시 · 1080p 크기 = 60×34칸 · 2배)
        foreach (var ship in new[] { "Didim", "Saetteul", "Hannuri" })
        {
            var lay = Layout.Ship(ship, Tables.Default);
            int beds = lay.Furniture.Count(fu => fu.Type == "Bed"), docks = lay.Furniture.Count(fu => fu.Type == "RobotDock");
            var s = ShipWorld.Crewed(ship, 3, Math.Max(2, beds), docks, 0);
            s.Run(Time.Hours(10));
            var p = new Painter(lay);
            foreach (var type in new[] { "Mess", "Quarters", "Storage", "Engine", "Bridge", "Medbay", "Hydroponics", "Cargo", "LifeSupport" })
            {
                var room = lay.Rooms.Where(r => r.Type == type).OrderByDescending(r => r.Cells.Length).FirstOrDefault();
                if (room == null) continue;
                int mx = (int)room.Cells.Average(lay.X), my = (int)room.Cells.Average(lay.Y);
                int w = 30, h = 17, x0 = Math.Clamp(mx - w / 2, 0, Math.Max(0, lay.W - w)), y0 = Math.Clamp(my - h / 2, 0, Math.Max(0, lay.H - h));
                var img = p.Frame(s, x0, y0, w, h, (int)s.Tick).Scale(2);
                string path = Path.Combine(dir, $"{ship.ToLowerInvariant()}-{type.ToLowerInvariant()}.png");
                Png.WriteRgba(path, img.W, img.H, img.Px);
                Console.WriteLine($"  art: {path} ({x0},{y0} {w}×{h}칸)");
            }
        }
    }
}
