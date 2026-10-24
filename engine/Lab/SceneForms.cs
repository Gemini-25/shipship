using ShipEngine;
using ShipSim.Art;
using ShipSim.Core;
using ShipView;

namespace Lab;

/// <summary>
/// 관찰 장면 '도는 도트' (docs/PHYSICS.md PH2 · `Lab --scene dotforms`): 꼴마다 왼쪽에 지금 그림 (새 가구 그림 · 옛 그림 · 바닥 물건 그림) ·
/// 오른쪽에 쌓아 그린 꼴 8각 (0 · 45 · 90 … 315°) — 줄마다 선 꼴 32도트 · 누운 꼴 32도트 · 선 꼴 16도트 · 누운 꼴 16도트. 가구는 1배 · 물건은 3배.
/// </summary>
public static partial class Program
{
    private static void DotFormsScene(string dir)
    {
        var t = Tables.Default;
        var bg = Rgba.Hex("#1c2027"); var line = Rgba.Hex("#2c323c");
        var blocks = new List<(Img now, DotForm up, DotForm lie, int zoom)>();
        foreach (var (type, bw, bh) in new[] { ("Table", 2, 2), ("Shelf", 2, 1), ("Bed", 2, 1), ("SuitLocker", 1, 1), ("Console", 1, 1), ("Seat", 1, 1) })
        {
            var now = ArtFurniture.Has(type) ? ArtFurniture.Body(type, bw, bh, Facing.S, new FurnFill { Ext = 1, Seal = 1, Suits = 2, Masks = 6, Owner = 3 }, 0)
                : DotMachines.Body(Enum.Parse<FurnitureType>(type), bw, bh, -1, 1, 0, 0);
            blocks.Add((now, DotForge.Make(t, type, Facing.S, bw, bh), DotForge.Make(t, type, Facing.S, bw, bh, 1), 1));
        }
        foreach (var name in new[] { "소화기", "식량 상자", "우주복", "공구 상자", "오물 통", "컵", "유리병", "접시", "책", "구급 상자" })
            blocks.Add((Painter.LooseIcon(name), DotForge.Make(t, name), DotForge.Make(t, name, lie: 1), 3));
        // 블록마다 칸 크기 (그 꼴의 그림 32장 가운데 가장 큰 것 + 틈) · 4줄 × (지금 그림 + 8각)
        var laid = new List<(int x, int y, int cw, int ch, int i)>();
        int sheetW = 1720, cx0 = 0, cy0 = 0, rowH = 0;
        var pics = new List<DotStack.Drawn[]>();
        for (int i = 0; i < blocks.Count; i++)
        {
            var (now, up, lie, z) = blocks[i];
            var set = new List<DotStack.Drawn>();
            foreach (var (f, sc) in new[] { (up, 1), (lie, 1), (up, 2), (lie, 2) })
                for (int a = 0; a < 8; a++) set.Add(DotStack.Draw(f, a * 8, sc));
            pics.Add(set.ToArray());
            int cw = (Math.Max(set.Max(d => d.Img.W), now.W) + 4) * z, ch = (Math.Max(set.Max(d => d.Img.H), now.H) + 4) * z;
            int bwid = cw * 9 + 8, bhgt = ch * 4 + 8;
            if (cx0 + bwid > sheetW) { cx0 = 0; cy0 += rowH; rowH = 0; }
            laid.Add((cx0, cy0, cw, ch, i));
            cx0 += bwid; rowH = Math.Max(rowH, bhgt);
        }
        var sheet = new Img(sheetW, cy0 + rowH);
        sheet.Fill(0, 0, sheet.W, sheet.H, bg);
        foreach (var (bx, by, cw, ch, i) in laid)
        {
            var (now, _, _, z) = blocks[i];
            var nowZ = z > 1 ? now.Scale(z) : now;
            sheet.Draw(nowZ, bx + 4 + (cw - nowZ.W) / 2, by + 4 + ch - nowZ.H - 2 * z);
            for (int k = 0; k < 4 * ch; k++) sheet.Set(bx + 4 + cw - 1, by + 4 + k, line);
            for (int r = 0; r < 4; r++)
                for (int a = 0; a < 8; a++)
                {
                    var d = pics[i][r * 8 + a];
                    var im = z > 1 ? d.Img.Scale(z) : d.Img;
                    // 칸 가운데 (그림 크기는 각 단마다 같아 무게중심 바닥 점이 제자리에 선다)
                    sheet.Draw(im, bx + 4 + cw * (a + 1) + (cw - im.W) / 2, by + 4 + ch * r + (ch - im.H) / 2);
                }
            for (int k = 0; k < 9 * cw; k++) sheet.Set(bx + 4 + k, by + 4 + 4 * ch, line);
        }
        Png.WriteRgba(Path.Combine(dir, "dotforms.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  dotforms: {dir}/dotforms.png ({sheet.W}×{sheet.H}) — 꼴마다 왼쪽 지금 그림 · 8각 × (선 · 누운) × (32 · 16도트)");
    }
}
