using System;
using System.IO;
using System.Linq;
using System.Text;
using ShipSim.Core;

/// <summary>
/// 새 엔진(engine/)으로 옛 배 도면을 내보낸다: --export-ships 폴더 → 폴더/{key}.json
/// 칸 종류(' ' 바깥 · '#' 벽 · 'H' 외판 · '.' 바닥 · 'D' 문) · 방 · 문 · 설비 자리. 둘레에 바깥 칸 한 줄을 덧댄다.
/// </summary>
public static partial class Program
{
    private static int RunExportShips(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var t in ShipCatalog.All)
        {
            var w = World.CreateDefault(1, 0, t.Key);
            var ship = w.Ship;
            var g = ship.Grid;
            int W = g.Width + 2, H = g.Height + 2;
            int Idx(int x, int y) => (y + 1) * W + (x + 1);
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"key\": \"{t.Key}\", \"name\": \"{t.Name}\", \"w\": {W}, \"h\": {H},\n");
            sb.Append("  \"rows\": [\n");
            for (int y = -1; y <= g.Height; y++)
            {
                var row = new StringBuilder();
                for (int x = -1; x <= g.Width; x++)
                {
                    var c = new Cell(x, y);
                    char ch = g.Kind(c) switch
                    {
                        TileKind.Wall => ship.WallAt(c)?.IsHull == true ? 'H' : '#',
                        TileKind.Floor => '.',
                        TileKind.Door => 'D',
                        _ => ' ',
                    };
                    row.Append(ch);
                }
                sb.Append($"    \"{row}\"{(y < g.Height ? "," : "")}\n");
            }
            sb.Append("  ],\n  \"rooms\": [\n");
            var rooms = ship.Rooms.Where(r => !r.Detached && r.Cells.Count > 0).ToList();
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                string cells = string.Join(",", r.Cells.Select(c => Idx(c.X, c.Y)).OrderBy(v => v));
                sb.Append($"    {{ \"id\": {i}, \"type\": \"{r.Kind}\", \"name\": \"{r.Name.Replace("\"", "'")}\", \"cells\": [{cells}] }}{(i < rooms.Count - 1 ? "," : "")}\n");
            }
            sb.Append("  ],\n  \"doors\": [\n");
            var doors = ship.Doors.Where(d => !d.Removed).ToList();
            for (int i = 0; i < doors.Count; i++)
            {
                var d = doors[i];
                string kind = d.IsExternal ? "airlock" : d.Bulkhead ? "pressure" : "normal";
                sb.Append($"    {{ \"x\": {d.Cell.X + 1}, \"y\": {d.Cell.Y + 1}, \"vertical\": {(d.ConnectsVertically ? "true" : "false")}, \"external\": {(d.IsExternal ? "true" : "false")}, \"kind\": \"{kind}\" }}{(i < doors.Count - 1 ? "," : "")}\n");
            }
            sb.Append("  ],\n  \"furniture\": [\n");
            var furn = ship.Furniture.Where(f => !f.Stowed && f.Cells.Count > 0 && !f.Room.Detached).ToList();
            for (int i = 0; i < furn.Count; i++)
            {
                var f = furn[i];
                string cells = string.Join(",", f.Cells.Select(c => $"[{c.X + 1},{c.Y + 1}]"));
                sb.Append($"    {{ \"type\": \"{f.Type}\", \"cells\": [{cells}] }}{(i < furn.Count - 1 ? "," : "")}\n");
            }
            sb.Append("  ]\n}\n");
            string path = Path.Combine(dir, t.Key.ToLowerInvariant() + ".json");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"  {t.Key} {t.Name} → {path} ({W}×{H} · 방 {rooms.Count} · 문 {doors.Count} · 설비 {furn.Count})");
        }
        return 0;
    }
}
