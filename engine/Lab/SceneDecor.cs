using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>
/// 관찰 장면 '생활 소품' (docs/PHYSICS.md PH9 · `Lab --scene props-ships` · `--scene mess`): 일곱 척마다 식당 · 조리실 · 침실 · 작업실 한 장씩 (9 × 6칸 · 32도트) —
/// 탁자 위 컵 · 접시 · 선반 위 책 · 물병 · 작업대 위 공구 상자가 세계 물건 (꾸밈 · 제자리)으로 놓였다. 줄마다 배 하나 · 사람 없이 첫 틱.
/// mess: 디딤호 6명 · 로봇 2 하루 — 식당 탁자 둘레에 사람이 가장 많이 모인 때 넷 · 꾸밈은 그 자리 그대로 (걷는 이가 탁자 칸을 지나도 가구가 가린다).
/// </summary>
public static partial class Program
{
    private static void PropsShipsScene(string dir)
    {
        const int cw = 9, ch = 6, gap = 4;
        var ships = Layout.ShipKeys().OrderBy(k => k).ToList();
        (string room, string furn)[] spots = { ("Mess", "Table"), ("Galley", "Stove"), ("Quarters", "Bed"), ("Workshop", "Workbench") };
        int fw = cw * Painter.T, fh = ch * Painter.T;
        var sheet = new Img(spots.Length * (fw + gap) - gap, ships.Count * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        for (int k = 0; k < ships.Count; k++)
        {
            var s = ShipWorld.Create(ships[k], 7);
            s.Step();
            var L = s.Layout; var st = s.Get<Structure>();
            var painter = new Painter(L);
            for (int j = 0; j < spots.Length; j++)
            {
                var (room, furn) = spots[j];
                var fu = L.Furniture.Where(f => f.Type == furn && f.Room >= 0 && L.Rooms[f.Room].Type == room).ToList();
                if (fu.Count == 0) fu = L.Furniture.Where(f => f.Type == furn).ToList();
                if (fu.Count == 0) continue;
                int r0 = fu[0].Room;
                var cells = fu[0].Cells;   // 그 방의 첫 가구 둘레
                int mx = (int)Math.Round(cells.Average(c => (double)L.X(c))), my = (int)Math.Round(cells.Average(c => (double)L.Y(c)));
                int x0 = Math.Clamp(mx - cw / 2, 0, L.W - cw), y0 = Math.Clamp(my - ch / 2, 0, L.H - ch);
                sheet.Draw(painter.Frame(s, x0, y0, cw, ch, 1), j * (fw + gap), k * (fh + gap));
            }
            var decor = st.Props.Where(p => (p.Flags & 1) != 0).ToList();
            var names = s.Tables.People.Items.Keys.ToList();
            string by = string.Join(" · ", decor.GroupBy(p => p.What).OrderBy(g => g.Key).Select(g => $"{names[g.Key]} {g.Count()}"));
            Console.WriteLine($"  props-ships {ships[k]}: 꾸밈 {decor.Count} ({by}) · 깨어 있음 {st.Props.Count(p => Structure.Awake(p))}");
        }
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "props-ships.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  props-ships: {dir}/props-ships.png ({sheet.W}×{sheet.H}) — 줄마다 배 하나: 식당 · 조리실 · 침실 · 작업실");
    }

    private static void MessScene(string dir)
    {
        Sim New() => ShipWorld.Crewed("Didim", 7, 6, 2);
        var s = New();
        var L = s.Layout; var st = s.Get<Structure>(); var mind = s.Get<Mind>();
        var room = L.Rooms.First(r => r.Type == "Mess");
        const int cw = 10, ch = 7;
        // 식당 탁자마다 둘레 (10 × 7칸) — 하루 동안 사람이 가장 많이 든 탁자를 그린다
        var boxes = L.Furniture.Where(f => f.Room == room.Id && f.Type == "Table").Select(f => (
            x: Math.Clamp((int)Math.Round(f.Cells.Average(c => (double)L.X(c))) - cw / 2, 0, L.W - cw),
            y: Math.Clamp((int)Math.Round(f.Cells.Average(c => (double)L.Y(c))) - ch / 2, 0, L.H - ch))).ToList();
        int Near(Sim w, int b) => w.Get<Mind>().Agents.Count(a => a.IsPerson && L.X(a.C.Cell) >= boxes[b].x && L.X(a.C.Cell) < boxes[b].x + cw && L.Y(a.C.Cell) >= boxes[b].y && L.Y(a.C.Cell) < boxes[b].y + ch);
        var before = st.Props.Where(p => (p.Flags & Structure.DecorFlag) != 0 && L.Room[st.CellOf(p)] == room.Id).ToDictionary(p => p.Id, p => (p.X, p.Y));
        // 하루 동안 10분마다 탁자 둘레 사람 수 → 가장 붐빈 탁자의 가장 붐빈 때 넷 (서로 2시간 넘게 떨어진) → 같은 시드로 다시 돌려 그 때마다 그린다
        var counts = new List<(long tick, int b, int n)>();
        for (long t = 0; t < Time.Hours(24); t++)
        {
            s.Step();
            if (s.Tick % Time.Minutes(10) == 0) for (int b = 0; b < boxes.Count; b++) counts.Add((s.Tick, b, Near(s, b)));
        }
        int box = Enumerable.Range(0, boxes.Count).OrderByDescending(b => counts.Where(c => c.b == b).Sum(c => c.n)).First();
        var (x0, y0) = boxes[box];
        var pick = new List<long>();
        foreach (var (tick, _, n) in counts.Where(c => c.b == box).OrderByDescending(c => c.n).ThenBy(c => c.tick))
            if (pick.Count < 4 && pick.All(p => Math.Abs(p - tick) >= Time.Hours(2))) pick.Add(tick);
        pick.Sort();
        var r = New();
        var painter = new Painter(r.Layout);
        const int gap = 6;
        int fw = cw * Painter.T, fh = ch * Painter.T;
        var sheet = new Img(2 * fw + gap, 2 * (fh + gap) - gap);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        var rst = r.Get<Structure>();
        for (int k = 0; k < pick.Count; k++)
        {
            while (r.Tick < pick[k]) r.Step();
            sheet.Draw(painter.Frame(r, x0, y0, cw, ch, k), k % 2 * (fw + gap), k / 2 * (fh + gap));
            Console.WriteLine($"  mess {Time.Clock(r.Tick)}: 탁자 둘레 사람 {Near(r, box)}");
        }
        while (r.Tick < s.Tick) r.Step();
        int moved = before.Count(kv => rst.IndexOf(kv.Key) < 0 || (rst.Props[rst.IndexOf(kv.Key)].X, rst.Props[rst.IndexOf(kv.Key)].Y) != kv.Value);
        var names = r.Tables.People.Items.Keys.ToList();
        string by = string.Join(" · ", before.Keys.Select(id => st.Props[st.IndexOf(id)].What).GroupBy(x => x).Select(g => $"{names[g.Key]} {g.Count()}"));
        Console.WriteLine($"  mess: 디딤호 식당 꾸밈 {before.Count} ({by}) · 하루 뒤 움직인 것 {moved} · 깨짐 {rst.Shattered} · 떨어짐 사실 {r.Facts.Since(0).Count(f => f.Kind == FactKind.ItemFell)}");
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "mess.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  mess: {dir}/mess.png ({sheet.W}×{sheet.H}) — 디딤호 식당 탁자 둘레에 사람이 가장 많이 모인 때 넷 (하루)");
    }
}
