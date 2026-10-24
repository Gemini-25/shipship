using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>가구 그림에 넘기는 세계 값 (없으면 0): 선반 · 로커 · 함의 물건 수 (Mind.Stock) · 담요 주인 색 · 낡음 단 (망 장치만).</summary>
public struct FurnFill
{
    public byte Ext, Seal, Suits, Masks, Wear;
    public int Owner;   // 담요 주인 (사람 번호 · −1 없음)
    public long Key => Ext | (long)Seal << 3 | (long)Suits << 6 | (long)Masks << 9 | (long)Wear << 13 | (long)(Owner + 1 & 0x3FF) << 16;
}

/// <summary>
/// 새 가구 그림 (docs/GRAPHICS.md 텍스처 T6 — 상위 15종): 3/4 시점 상자 (윗면 + 앞면) · 선택 외곽 (아래 · 오른쪽은 진하게 · 위 · 왼쪽은 한 단 밝게) ·
/// 면 단 (윗면 밝게 · 앞면 가운데 · 앞 모서리 1도트 따뜻한 빛) · 재질 결 · 칸에 읽히는 부품 2~4.
/// 세계 값: 선반의 소화기 · 봉합 꾸러미 (빈 칸이면 비었다) · 우주복 보관함의 우주복 · 산소마스크함의 마스크 수 · 침대 담요 = 주인의 색.
/// 화물 · 책 · 공구 · 옷은 세계가 세지 않아 고정 꾸밈 (해시). 그림 크기 = 칸 × 32 · 높이 = 칸 × 32 + 솟음 (밑이 칸 바닥에 닿는다).
/// </summary>
public static class ArtFurniture
{
    public const int T = 32;
    private static Rgba H(string s) => Rgba.Hex(s);
    private static readonly Rgba Clear = new(0, 0, 0, 0);

    // 재질 단 (0 외곽 · 1 그늘 · 2 앞면 · 3 앞면 밝게 · 4 윗면 · 5 빛)
    private static readonly Rgba[] Steel = { H("#14171d"), H("#2a3039"), H("#3b434e"), H("#4c5562"), H("#5d6775"), H("#7d8796") };
    private static readonly Rgba[] Dark = { H("#0f1115"), H("#1c1f26"), H("#272b34"), H("#323843"), H("#3d4450"), H("#566070") };
    private static readonly Rgba[] Wood = { H("#1f140d"), H("#3a2618"), H("#523622"), H("#68462c"), H("#7d5637"), H("#9a6d47") };
    private static readonly Rgba[] White = { H("#3d4248"), H("#6e757d"), H("#8b939b"), H("#a2aab1"), H("#b8bfc5"), H("#d3d8dc") };
    private static readonly Rgba[] Olive = { H("#1a1c12"), H("#363a24"), H("#4a4f31"), H("#5c623d"), H("#6c7348"), H("#868e5b") };
    private static readonly Rgba[] Navy = { H("#11151e"), H("#1f2738"), H("#2b3650"), H("#364565"), H("#425478"), H("#5a6e96") };
    private static readonly Rgba[] Rust = { H("#1e130e"), H("#4a2a1c"), H("#663a26"), H("#7e4a31"), H("#93583b"), H("#ad6e4e") };
    private static readonly Rgba[] Leaf = { H("#0f1f0d"), H("#1c3818"), H("#2a5223"), H("#3b6e2f"), H("#4f8a3c"), H("#6ea852") };
    private static readonly Rgba Warm = H("#c8b48a"), Glass = H("#1d3340"), GlassHi = H("#6fa3b8");
    private static readonly Rgba[] Bins = { H("#6b5a3e"), H("#4a5a6e"), H("#5e6b4a"), H("#7a4a3a"), H("#7a7a72"), H("#56506a") };
    private static readonly Rgba[] Books = { H("#7a3030"), H("#305a7a"), H("#6a6a2e"), H("#2e6a52"), H("#7a5a2e"), H("#5a3a6a"), H("#8a7a5a"), H("#3a3a3a") };
    private static readonly Rgba[] Cloth = { H("#7a4040"), H("#3d5a7a"), H("#5a7a4a"), H("#7a6a3a"), H("#5a4a7a"), H("#3a6a6a"), H("#8a5a3a"), H("#6a6a6a") };

    /// <summary>새 그림이 있는 가구 종류.</summary>
    public static bool Has(string type) => type is "Shelf" or "Bookshelf" or "FoodLocker" or "CargoCrate" or "CargoNet" or "SuitLocker" or "ToolWall" or "OxygenMaskBox" or "Console" or "GrowRack" or "PlantRack" or "ClothesRack" or "RobotDock" or "OxygenGenerator" or "Bed" or "Table";

    /// <summary>앞면 높이 · 윗면 깊이 (도트) — 솟음 = 앞면 + 윗면 − 칸 깊이. 도트 꼴 법칙이 앞면 눈금을 견준다 (ShipView는 Lab에 속을 열지 않아 public).</summary>
    public static (int front, int top) Size(string type, int bw, int bh, Facing f)
    {
        int deep = (f is Facing.E or Facing.W ? Math.Max(bw, bh) : bh) * T;   // 위에서 보이는 깊이 (옆으로 보면 긴 쪽)
        int front = type switch
        {
            "Shelf" => 26, "Bookshelf" => 30, "FoodLocker" => 22, "CargoCrate" => 22, "CargoNet" => 18, "SuitLocker" => 34, "ToolWall" => 28,
            "OxygenMaskBox" => 14, "Console" => 14, "GrowRack" or "PlantRack" => 12, "Table" => 12, "ClothesRack" => 26, "RobotDock" => 4, "OxygenGenerator" => 28, "Bed" => 8,
            _ => 16,
        };
        int top = type switch
        {
            "OxygenMaskBox" => 6, "ToolWall" => 4, "ClothesRack" => 8, "RobotDock" => deep - 4,
            _ => f is Facing.E or Facing.W || bh > 1 ? deep - 4 : type == "Bookshelf" ? 8 : type is "Console" or "Bed" or "GrowRack" or "PlantRack" or "Table" ? deep - 6 : 10,
        };
        return (front, top);
    }

    /// <summary>칸 위로 솟는 높이.</summary>
    public static int Rise(string type, int bw, int bh, Facing f)
    {
        var (front, top) = Size(type, bw, bh, f);
        int deep = (f is Facing.E or Facing.W ? Math.Max(bw, bh) : bh) * T;
        return Math.Max(0, front + top - deep);
    }

    /// <summary>몸체 그림 (bw × bh 칸 · 위로 솟음만큼 더 · 밑이 칸 바닥).</summary>
    public static Img Body(string type, int bw, int bh, Facing f, FurnFill fill, int seed)
    {
        int rise = Rise(type, bw, bh, f);
        var o = new Img(bw * T, bh * T + rise);
        var (front, top) = Size(type, bw, bh, f);
        int W = o.W, Hh = o.H;
        int fy = Hh - front;   // 앞면 위 끝
        int ty = fy - top;     // 윗면 위 끝
        switch (type)
        {
            case "Shelf": Shelf(o, 0, ty, W, top, front, f, fill, seed, Steel); break;
            case "Bookshelf": Bookshelf(o, 0, ty, W, top, front, f, seed); break;
            case "FoodLocker": FoodLocker(o, 2, ty, W - 4, top, front, seed); break;
            case "CargoCrate": Crate(o, 3, ty + 2, W - 6, top - 2, front, seed); break;
            case "CargoNet": CargoNet(o, 1, ty, W - 2, top, front, seed); break;
            case "SuitLocker": SuitLocker(o, 3, ty, W - 6, top, front, fill); break;
            case "ToolWall": ToolWall(o, 0, ty, W, top, front, f, seed); break;
            case "OxygenMaskBox": MaskBox(o, 5, ty, W - 10, top, front, fill); break;
            case "Console": Console_(o, 2, ty, W - 4, top, front, f); break;
            case "GrowRack" or "PlantRack": GrowRack(o, 1, ty, W - 2, top, front, f, seed); break;
            case "ClothesRack": ClothesRack(o, 2, ty, W - 4, top, front, seed); break;
            case "RobotDock": RobotDock(o, 2, ty, W - 4, top, front, f); break;
            case "OxygenGenerator": OxyGen(o, 1, ty, W - 2, top, front, f); break;
            case "Bed": Bed(o, 1, ty, W - 2, top, front, f, fill); break;
            case "Table": Table(o, 1, ty, W - 2, top, front, seed); break;
        }
        return o;
    }

    // ───────────────────────── 도구 ─────────────────────────

    /// <summary>3/4 상자: 윗면 (x, y, w, top) · 앞면 (x, y + top, w, front) — 선택 외곽 · 앞 모서리 빛.</summary>
    private static void Box(Img o, int x, int y, int w, int top, int front, Rgba[] p, bool warmEdge = true)
    {
        o.Fill(x, y, w, top, p[4]);
        o.Fill(x, y + top, w, front, p[2]);
        o.Fill(x, y + top, w, 1, warmEdge ? Rgba.Lerp(p[5], Warm, 0.35f) : p[5]);   // 앞 모서리 빛
        o.Fill(x, y + top + 1, w, 1, p[3]);
        Outline(o, x, y, w, top + front, p);
    }

    /// <summary>선택 외곽: 위 · 왼쪽은 그늘 단 · 아래 · 오른쪽은 외곽 단.</summary>
    private static void Outline(Img o, int x, int y, int w, int h, Rgba[] p)
    {
        for (int i = 0; i < w; i++) { o.Set(x + i, y, p[1]); o.Set(x + i, y + h - 1, p[0]); }
        for (int j = 0; j < h; j++) { o.Set(x, y + j, p[1]); o.Set(x + w - 1, y + j, p[0]); }
    }

    private static void Rect(Img o, int x, int y, int w, int h, Rgba c) => o.Fill(x, y, w, h, c);

    /// <summary>작은 물건 (고정 꾸밈 통 · 상자): 윗면 1줄 밝게 · 오른쪽 그늘.</summary>
    private static void Bin(Img o, int x, int y, int w, int h, Rgba c)
    {
        o.Fill(x, y, w, h, c);
        o.Fill(x, y, w, 1, c.Scale(1.25f));
        o.Fill(x + w - 1, y, 1, h, c.Scale(0.7f));
        o.Fill(x, y + h - 1, w, 1, c.Scale(0.6f));
    }

    /// <summary>소화기 (빨간 통 · 검은 꼭지 · 하얀 띠).</summary>
    private static void Extinguisher(Img o, int x, int y)
    {
        o.Fill(x + 1, y, 2, 2, H("#1a1a1a"));
        o.Fill(x, y + 2, 5, 9, H("#b8322a"));
        o.Fill(x, y + 2, 1, 9, H("#de5a48"));
        o.Fill(x + 4, y + 2, 1, 9, H("#7a1c18"));
        o.Fill(x, y + 6, 5, 1, H("#d8d8d0"));
    }

    /// <summary>봉합 꾸러미 (노란 통 · 검은 띠).</summary>
    private static void SealKit(Img o, int x, int y)
    {
        o.Fill(x, y, 8, 6, H("#c9a227"));
        o.Fill(x, y, 8, 1, H("#e8c858"));
        o.Fill(x, y + 3, 8, 1, H("#2a2416"));
        o.Fill(x + 7, y, 1, 6, H("#8a6e18"));
    }

    // ───────────────────────── 종류마다 ─────────────────────────

    /// <summary>
    /// 금속 선반 (3단): 깊으면 (윗면 20도트 넘게) 위에서 본 칸막이 사이 물건 윗면 · 얕으면 윗판.
    /// 앞면은 보는 쪽대로 — S 기둥 · 판 · 단마다 물건 / N 뒷판 X자 버팀 / E · W 옆판 (판이 가로줄로).
    /// 세계 값 칸 (가운데 단 · 첫 칸 소화기 · 둘째 봉합 꾸러미 — 빈 칸이면 없다): 깊으면 윗면에 · 얕으면 앞면에.
    /// </summary>
    private static void Shelf(Img o, int x, int y, int w, int top, int front, Facing f, FurnFill fill, int seed, Rgba[] p)
    {
        int fy = y + top;
        bool deep = top > 20;
        if (deep) Compartments(o, x, y, w, top, p, seed, fill, false);
        else { o.Fill(x, y, w, top, p[4]); o.Fill(x, y, w, 1, p[5]); o.Fill(x, y + top - 1, w, 1, p[3]); Outline(o, x, y, w, top, p); }
        if (f == Facing.S)
        {
            o.Fill(x, fy, w, front, Dark[1]);
            int[] boards = { fy + front / 3, fy + 2 * front / 3, fy + front - 3 };
            int prev = fy;
            for (int b = 0; b < 3; b++)
            {
                int gap = boards[b] - prev, bx = x + 4;
                while (bx < x + w - 6)
                {
                    uint u = DotHash.U(seed, bx, b * 7 + 3);
                    int iw = 4 + (int)(u % 5), ih = Math.Min(gap - 2, 3 + (int)((u >> 8) % 4));
                    if (!deep && b == 1 && bx == x + 4)
                    {
                        if (fill.Ext > 0) Extinguisher(o, bx, boards[b] - 11);
                        bx += 7;
                        if (fill.Seal > 0) SealKit(o, bx, boards[b] - 6);
                        bx += 10;
                        continue;
                    }
                    if ((u >> 16) % 5 != 0 && bx + iw < x + w - 3) Bin(o, bx, boards[b] - ih, iw, ih, Bins[(u >> 20) % (uint)Bins.Length]);
                    bx += iw + 1 + (int)((u >> 24) % 3);
                }
                o.Fill(x, boards[b], w, 2, p[3]); o.Fill(x, boards[b], w, 1, p[5]);
                prev = boards[b] + 2;
            }
            o.Fill(x, fy, 3, front, p[2]); o.Fill(x + w - 3, fy, 3, front, p[1]);
            o.Fill(x, fy, 1, front, p[4]); o.Fill(x + w - 1, fy, 1, front, p[0]);
            o.Fill(x, fy + front - 1, w, 1, p[0]);
        }
        else if (f == Facing.N)
        {
            o.Fill(x, fy, w, front, p[2]);
            o.Fill(x, fy, w, 1, Rgba.Lerp(p[5], Warm, 0.3f));
            for (int i = 0; i < w; i++)   // 뒷판 X자 버팀
            {
                int a2 = fy + 2 + i * (front - 4) / Math.Max(1, w), b2 = fy + front - 3 - i * (front - 4) / Math.Max(1, w);
                o.Set(x + i, a2, p[3]); o.Set(x + i, b2, p[1]);
            }
            o.Fill(x, fy, 3, front, p[3]); o.Fill(x + w - 3, fy, 3, front, p[1]);
            Outline(o, x, fy, w, front, p);
            if (!deep)   // 얕은 선반 뒤: 윗판 너머로 물건 윗면이 조금
                for (int bx = x + 3; bx < x + w - 6; bx += 7) { uint u = DotHash.U(seed, bx, 11); if ((u & 3) != 0) o.Fill(bx, y + 2, 5, Math.Max(1, top - 5), Bins[(u >> 4) % (uint)Bins.Length].Scale(1.15f)); }
        }
        else
        {
            o.Fill(x, fy, w, front, p[2]);
            o.Fill(x, fy, w, 1, Rgba.Lerp(p[5], Warm, 0.3f));
            for (int k = 1; k <= 3; k++) { o.Fill(x + 2, fy + k * front / 4, w - 4, 1, p[1]); o.Fill(x + 2, fy + k * front / 4 + 1, w - 4, 1, p[3]); }
            Outline(o, x, fy, w, front, p);
        }
    }

    /// <summary>위에서 본 칸막이 (선반 · 책장 윗면): 칸마다 물건 (통 · 상자 · 책 윗면) — 세계 값 칸은 둘째 칸 왼쪽 (선반만).</summary>
    private static void Compartments(Img o, int x, int y, int w, int top, Rgba[] p, int seed, FurnFill fill, bool books)
    {
        o.Fill(x, y, w, top, books ? p[0] : Dark[1]);
        int n = Math.Max(2, top / 14);
        for (int k = 0; k < n; k++)
        {
            int cy = y + 2 + k * (top - 4) / n, ch = (top - 4) / n - 2;
            if (books)
            {
                for (int bx = x + 3; bx < x + w - 3; )
                {
                    uint u = DotHash.U(seed, k, bx);
                    int bw = 2 + (int)(u % 2);
                    var c = Books[(u >> 4) % (uint)Books.Length];
                    o.Fill(bx, cy + 1, bw, ch - 1, c.Scale(1.1f)); o.Fill(bx, cy + 1, bw, 1, H("#e8e0c8").Scale(0.8f));   // 책 윗면 (종이 끝)
                    bx += bw + ((u >> 8) % 7 == 0 ? 2 : 0);
                }
            }
            else
                for (int bx = x + 4; bx < x + w - 5; bx += 8)
                {
                    uint u = DotHash.U(seed, k, bx);
                    if (k == 1 && bx == x + 4) { if (fill.Ext > 0) { o.Fill(bx, cy + 2, 5, 5, H("#b8322a")); o.Fill(bx + 1, cy + 3, 2, 2, H("#1a1a1a")); o.Set(bx, cy + 2, H("#de5a48")); } continue; }
                    if (k == 1 && bx == x + 12) { if (fill.Seal > 0) { o.Fill(bx, cy + 2, 6, 4, H("#c9a227")); o.Fill(bx, cy + 3, 6, 1, H("#2a2416")); } continue; }
                    if ((u & 3) != 0) Bin(o, bx, cy + 1, 6, Math.Max(2, ch - 1), Bins[(u >> 4) % (uint)Bins.Length].Scale(1.1f));
                }
            o.Fill(x, cy + ch, w, 2, p[4]); o.Fill(x, cy + ch, w, 1, p[5]);
        }
        o.Fill(x, y, 3, top, p[4]); o.Fill(x + w - 3, y, 3, top, p[3]);
        o.Fill(x, y, w, 2, p[4]); o.Fill(x, y, w, 1, p[5]);
        Outline(o, x, y, w, top, p);
    }

    /// <summary>책장 (나무): 깊으면 위에서 본 책 윗면 · 앞이면 책등 (색 · 높이 해시) · 가끔 화분 / 뒤 · 옆이면 나무 판.</summary>
    private static void Bookshelf(Img o, int x, int y, int w, int top, int front, Facing f, int seed)
    {
        var p = Wood;
        int fy = y + top;
        if (top > 20) Compartments(o, x, y, w, top, p, seed, default, true);
        else { o.Fill(x, y, w, top, p[4]); o.Fill(x, y, w, 1, p[5]); Outline(o, x, y, w, top, p); }
        if (f != Facing.S)
        {
            o.Fill(x, fy, w, front, p[2]);
            o.Fill(x, fy, w, 1, Rgba.Lerp(p[5], Warm, 0.4f));
            for (int k = 1; k < 4; k++) o.Fill(x + 2, fy + k * front / 4, w - 4, 1, p[1]);
            for (int j = fy + 2; j < fy + front - 2; j += 3) o.Fill(x + 3, j, w - 6, 1, p[3].Scale(0.95f));   // 나뭇결
            Outline(o, x, fy, w, front, p);
            return;
        }
        o.Fill(x, fy, w, front, p[0]);
        int rows = 3, rh = (front - 2) / rows;
        for (int r = 0; r < rows; r++)
        {
            int by = fy + 2 + r * rh, bottom = by + rh - 2;
            int bx = x + 3;
            while (bx < x + w - 3)
            {
                uint u = DotHash.U(seed, bx, r + 5);
                if ((u & 15) == 0 && bx + 7 < x + w - 3)   // 화분
                {
                    o.Fill(bx + 1, bottom - 3, 4, 3, H("#8a5a3a"));
                    o.Fill(bx, bottom - 7, 6, 4, Leaf[3]); o.Set(bx + 2, bottom - 8, Leaf[4]); o.Set(bx + 1, bottom - 6, Leaf[5]);
                    bx += 7; continue;
                }
                int bw = 2 + (int)((u >> 4) % 2), bh = rh - 4 - (int)((u >> 8) % 4);
                var c = Books[(u >> 12) % (uint)Books.Length];
                o.Fill(bx, bottom - bh, bw, bh, c);
                o.Fill(bx, bottom - bh, bw, 1, c.Scale(1.3f));
                if (bh > 6) o.Fill(bx, bottom - bh + 3, bw, 1, c.Scale(0.6f));
                bx += bw + ((u >> 16) % 6 == 0 ? 2 : 0);
            }
            o.Fill(x, bottom, w, 2, p[3]); o.Fill(x, bottom, w, 1, p[5]);
        }
        o.Fill(x, fy, 2, front, p[3]); o.Fill(x + w - 2, fy, 2, front, p[1]);
        o.Fill(x, fy, w, 1, Rgba.Lerp(p[5], Warm, 0.4f));
        Outline(o, x, fy, w, front, p);
    }

    /// <summary>식량 보관함 (단열 · 흰 합성수지 · 걸쇠 · 온도 표시등).</summary>
    private static void FoodLocker(Img o, int x, int y, int w, int top, int front, int seed)
    {
        var p = White;
        Box(o, x, y, w, top, front, p);
        int fy = y + top;
        o.Fill(x + 2, y + 2, w - 4, top - 4, p[3]);   // 뚜껑 홈
        o.Fill(x + 2, y + 2, w - 4, 1, p[5]);
        o.Fill(x + 2, fy + front / 2 - 1, w - 4, 1, p[1]);   // 문 틈
        o.Fill(x + w / 2 - 3, fy + 3, 6, 3, H("#5a6068")); o.Fill(x + w / 2 - 2, fy + 4, 4, 1, H("#2a2e33"));   // 걸쇠
        o.Fill(x + 3, fy + 3, 2, 2, H("#3fbf6a"));   // 온도 등 (초록)
        o.Fill(x + 3, fy + front - 6, w - 6, 3, H("#3d6d8a"));   // 단열 띠
        Outline(o, x, y, w, top + front, p);
    }

    /// <summary>화물 상자 (2×2 · 늑골 · 모서리 쇠 · 색은 해시 · 글 없는 표지).</summary>
    private static void Crate(Img o, int x, int y, int w, int top, int front, int seed)
    {
        uint u = DotHash.U(seed, 3, 17);
        var p = (u % 4) switch { 0 => Olive, 1 => Navy, 2 => Rust, _ => Steel };
        Box(o, x, y, w, top, front, p);
        int fy = y + top;
        for (int i = 6; i < w - 4; i += 10) { o.Fill(x + i, fy + 2, 2, front - 3, p[3]); o.Fill(x + i + 2, fy + 2, 1, front - 3, p[1]); }   // 늑골
        for (int i = 8; i < top - 2; i += 12) o.Fill(x + 2, y + i, w - 4, 1, p[3]);   // 뚜껑 판 이음
        foreach (var (cx, cy) in new[] { (x, y), (x + w - 4, y), (x, fy + front - 4), (x + w - 4, fy + front - 4) }) o.Fill(cx, cy, 4, 4, H("#8a8f96"));   // 모서리 쇠
        o.Fill(x + w / 2 - 6, fy + front / 2 - 2, 12, 4, H("#c8b878").Scale(0.8f));   // 표지 판
        o.Fill(x + w / 2 - 4, fy + front / 2 - 1, 8, 1, p[0]);
        o.Fill(x + w / 2 - 5, y + top / 2 - 3, 10, 6, H("#c8b878").Scale(0.7f));
        Outline(o, x, y, w, top + front, p);
    }

    /// <summary>화물망: 쌓인 상자 둘~셋 위로 그물 끈 (사선 · 고리).</summary>
    private static void CargoNet(Img o, int x, int y, int w, int top, int front, int seed)
    {
        // 바닥 받침 (낮은 판)
        o.Fill(x, y + top + front - 3, w, 3, Steel[1]);
        // 상자 셋 (해시)
        int bx = x + 1;
        for (int k = 0; k < 3 && bx < x + w - 6; k++)
        {
            uint u = DotHash.U(seed, k, 23);
            int bw = 8 + (int)(u % 5), bh = 10 + (int)((u >> 4) % 8);
            bw = Math.Min(bw, x + w - 1 - bx);
            var c = Bins[(u >> 8) % (uint)Bins.Length];
            int by = y + top + front - 3 - bh;
            o.Fill(bx, by - 4, bw, 4, c.Scale(1.25f)); Bin(o, bx, by, bw, bh, c);
            bx += bw;
        }
        // 그물 (어두운 끈 사선 · 위 고리)
        var cord = H("#2a2620");
        for (int i = 0; i < w; i += 5)
            for (int j = 0; j < top + front - 3; j++)
            {
                int px = x + i + j / 2;
                if (px < x + w) { var c0 = o.Get(px, y + j); if (c0.A > 0) o.Set(px, y + j, cord); }
                int qx = x + i - j / 2 + 4;
                if (qx >= x && qx < x + w) { var c1 = o.Get(qx, y + j); if (c1.A > 0) o.Set(qx, y + j, cord); }
            }
        o.Fill(x + 1, y, 2, 2, H("#8a8f96")); o.Fill(x + w - 3, y, 2, 2, H("#8a8f96"));
    }

    /// <summary>우주복 보관함: 유리문 너머 우주복 (세계 값 — 0~2벌) · 아래 소화기 (세계 값).</summary>
    private static void SuitLocker(Img o, int x, int y, int w, int top, int front, FurnFill fill)
    {
        var p = Dark;
        Box(o, x, y, w, top, front, p);
        int fy = y + top;
        o.Fill(x + 2, fy + 3, w - 4, front - 8, Glass);
        for (int k = 0; k < Math.Min(2, (int)fill.Suits); k++)
        {
            int sx = x + 3 + k * (w - 6) / 2, sw = Math.Max(5, (w - 8) / 2);
            o.Fill(sx + 1, fy + 5, sw - 2, 4, H("#d8dce0"));            // 투구
            o.Fill(sx + 2, fy + 6, sw - 4, 2, H("#3a5a78"));            // 얼굴 가리개
            o.Fill(sx, fy + 9, sw, front - 16, H("#c8ccd2"));          // 몸
            o.Fill(sx + sw / 2 - 1, fy + 11, 2, 3, H("#d06a2a"));        // 가슴 표시
            o.Fill(sx + sw - 1, fy + 9, 1, front - 16, H("#8a9098"));
        }
        o.Fill(x + 2, fy + 3, 1, front - 8, GlassHi);   // 유리 빛
        if (fill.Ext > 0) Extinguisher(o, x + w - 8, fy + front - 13);
        o.Fill(x + 2, fy + front - 5, w - 4, 3, p[1]);
        o.Fill(x + w / 2 - 1, fy + front / 2, 2, 4, H("#d06a2a"));   // 손잡이
        Outline(o, x, y, w, top + front, p);
    }

    /// <summary>공구 벽: 구멍판 · 공구 윤곽 (렌치 · 망치 · 드라이버 · 집게 — 해시로 자리).</summary>
    private static void ToolWall(Img o, int x, int y, int w, int top, int front, Facing f, int seed)
    {
        var board = H("#5a5246"); var hole = H("#3a342c");
        int fy = y + top;
        o.Fill(x, y, w, top, Steel[4]);
        o.Fill(x, fy, w, front, board);
        for (int j = fy + 2; j < fy + front - 2; j += 3) for (int i = x + 2; i < x + w - 2; i += 3) o.Set(i, j, hole);
        var tool = H("#9aa2ac"); var tdark = H("#4a4f56"); var grip = H("#b0422e");
        for (int i = x + 3; i < x + w - 6; i += 8)
        {
            uint u = DotHash.U(seed, i, 29);
            int ty = fy + 3 + (int)(u % 4);
            switch (u >> 4 & 3)
            {
                case 0: o.Fill(i + 2, ty + 2, 2, 14, tool); o.Fill(i, ty, 6, 3, tool); o.Fill(i + 2, ty + 1, 2, 1, tdark); break;   // 렌치
                case 1: o.Fill(i, ty, 6, 4, tdark); o.Fill(i + 2, ty + 4, 2, 12, grip); break;                                     // 망치
                case 2: o.Fill(i + 2, ty, 2, 9, tool); o.Fill(i + 1, ty + 9, 4, 7, grip); break;                                    // 드라이버
                default: o.Fill(i + 1, ty, 2, 10, tool); o.Fill(i + 3, ty, 2, 10, tool); o.Fill(i, ty + 10, 2, 6, grip); o.Fill(i + 4, ty + 10, 2, 6, grip); break;   // 집게
            }
        }
        o.Fill(x, fy, w, 1, Rgba.Lerp(Steel[5], Warm, 0.3f));
        Outline(o, x, y, w, top + front, new[] { H("#1e1b17"), H("#2e2a24"), board, board, board, board });
    }

    /// <summary>산소마스크함 (벽에 건 작은 함 · 노랑 테 · 투명 창 너머 마스크 — 세계 값 개수 · 셋까지 그림).</summary>
    private static void MaskBox(Img o, int x, int y, int w, int top, int front, FurnFill fill)
    {
        var rim = new[] { H("#2a2414"), H("#5f4b1e"), H("#86692a"), H("#a88536"), H("#c4a047"), H("#dcbb62") };
        Box(o, x, y, w, top, front, rim);
        int fy = y + top;
        o.Fill(x + 2, fy + 2, w - 4, front - 4, Glass);
        int n = Math.Min(3, (fill.Masks + 1) / 2);
        for (int k = 0; k < n; k++) { int mx = x + 3 + k * (w - 6) / 3; o.Fill(mx, fy + 4, 4, 4, H("#e8ecef")); o.Fill(mx + 1, fy + 8, 2, 2, H("#7aa8c0")); }
        o.Fill(x + 2, fy + 2, 1, front - 4, GlassHi);
    }

    /// <summary>조작대: 기운 화면 판 (꺼지면 어둡다 — 켜진 화면은 빛 그림에서) · 자판 · 받침.</summary>
    private static void Console_(Img o, int x, int y, int w, int top, int front, Facing f)
    {
        var p = Dark;
        Box(o, x, y, w, top, front, p);
        // 윗면: 기운 화면 (뒤쪽) · 자판 (앞쪽)
        int sh = Math.Max(6, top * 5 / 9);
        o.Fill(x + 3, y + 1, w - 6, sh, H("#0c1418"));
        o.Fill(x + 3, y + 1, w - 6, 1, p[5]);
        o.Fill(x + 4, y + sh + 2, w - 8, Math.Max(2, top - sh - 4), p[3]);
        for (int i = x + 5; i < x + w - 5; i += 2) o.Set(i, y + sh + 3, p[1]);
        // 앞면: 통풍 틈 · 표시등 자리
        for (int i = x + 4; i < x + w - 4; i += 3) o.Fill(i, y + top + 5, 2, front - 8, p[1]);
    }

    /// <summary>재배 선반: 흙 통 · 촘촘한 포기 (잎 무리 셋 단 · 줄기 · 가끔 열매 — 자람 값이 없어 다 자란 포기) · 앞 물 관 · 생장등 막대 (켜면 빛 그림에서).</summary>
    private static void GrowRack(Img o, int x, int y, int w, int top, int front, Facing f, int seed)
    {
        var p = Steel;
        Box(o, x, y, w, top, front, p);
        bool vertical = top > w;   // 세로로 긴 선반 (위에서 보는 쪽이 길다)
        o.Fill(x + 2, y + 2, w - 4, top - 4, H("#2e241a"));
        for (int k = 0; k < (vertical ? top : w) - 4; k += 3) o.Set(vertical ? x + 3 + k % 5 * 5 : x + 2 + k, vertical ? y + 2 + k : y + 3 + k % 3 * 2, H("#4a3826"));   // 흙 결
        int len = vertical ? top - 6 : w - 6, across = vertical ? w - 4 : top - 4;
        for (int k = 3; k < len; k += 5)
        {
            uint u = DotHash.U(seed, k, 31);
            int lane = across >= 18 ? ((k / 5) & 1) * 2 - 1 : 0;   // 넓으면 두 줄로 엇갈려 심는다
            int off = lane * across / 4 + (int)(u % 3) - 1;
            int cx = vertical ? x + w / 2 + off : x + 3 + k, cy = vertical ? y + 3 + k : y + 1 + top / 2 + off;
            int r = 3 + (int)((u >> 2) % 2);
            for (int j = -r - 1; j <= r; j++)
                for (int i = -r; i <= r; i++)
                {
                    int d = i * i + j * j;
                    if (d > r * r + 1) continue;
                    int shade = j < -1 ? 4 : j < 1 ? 3 : 2;
                    if (((i + j * 3 + (int)u) & 3) == 0) shade = Math.Max(1, shade - 1);   // 잎 사이 그늘
                    o.Set(cx + i, cy + j, Leaf[shade]);
                }
            o.Set(cx - 1, cy - r - 1, Leaf[5]); o.Set(cx + 1, cy - r, Leaf[5]);
            if ((u >> 8) % 3 == 0) { o.Set(cx + 1, cy, H("#c84a3a")); o.Set(cx + 2, cy + 1, H("#e86a50")); }      // 토마토
            else if ((u >> 8) % 5 == 1) { o.Set(cx - 1, cy + 1, H("#e8c23a")); }                                    // 꽃
        }
        // 물 관 (앞면) · 받침
        o.Fill(x + 2, y + top + 2, w - 4, 2, H("#3a6a8a"));
        o.Fill(x + 2, y + top + 2, w - 4, 1, H("#6a9ab8"));
        for (int i = x + 6; i < x + w - 4; i += 16) o.Fill(i, y + top + 4, 2, front - 5, p[1]);
    }

    /// <summary>탁자 (식당 · 쉼터 · 회의): 나무 윗판 · 가장자리 빛 · 앞 다리 · 위에 쟁반 · 컵 · 접시 (고정 꾸밈).</summary>
    private static void Table(Img o, int x, int y, int w, int top, int front, int seed)
    {
        var p = Wood;
        o.Fill(x, y, w, top, p[4]);
        for (int j = y + 3; j < y + top - 2; j += 4) o.Fill(x + 2, j, w - 4, 1, p[3]);   // 나뭇결
        o.Fill(x, y, w, 1, p[5]); o.Fill(x, y + top - 1, w, 1, p[3]);
        o.Fill(x, y + top, w, 3, p[2]); o.Fill(x, y + top, w, 1, Rgba.Lerp(p[5], Warm, 0.4f));
        Outline(o, x, y, w, top + 3, p);
        o.Fill(x + 2, y + top + 3, 3, front - 3, p[1]); o.Fill(x + w - 5, y + top + 3, 3, front - 3, p[0]);   // 다리
        for (int i = x + 6; i < x + w - 8; i += 11)
        {
            uint u = DotHash.U(seed, i, 41);
            int py = y + 3 + (int)(u % (uint)Math.Max(1, top - 10));
            switch (u >> 8 & 3)
            {
                case 0: o.Oval(i + 3, py + 3, 3.5f, 2.5f, H("#d8d4ca")); o.Oval(i + 3, py + 3, 2, 1.2f, H("#b8a888")); break;   // 접시
                case 1: o.Fill(i + 1, py, 4, 5, H("#e8e4dc")); o.Fill(i + 1, py, 4, 1, H("#5a3a22")); o.Set(i + 5, py + 2, H("#c8c4bc")); break;   // 컵
                case 2: o.Fill(i, py, 8, 5, H("#4a5462")); o.Fill(i, py, 8, 1, H("#6a7482")); break;   // 쟁반
            }
        }
    }

    /// <summary>옷걸이: 막대 · 옷 (색은 해시 — 방 사람들의 색은 나중에) · 받침 다리.</summary>
    private static void ClothesRack(Img o, int x, int y, int w, int top, int front, int seed)
    {
        var bar = Steel;
        int ry = y + 3;
        o.Fill(x, ry, w, 2, bar[4]); o.Fill(x, ry + 1, w, 1, bar[1]);
        o.Fill(x, ry, 2, top + front - 3, bar[3]); o.Fill(x + w - 2, ry, 2, top + front - 3, bar[2]);
        o.Fill(x - 1, y + top + front - 2, 5, 2, bar[1]); o.Fill(x + w - 4, y + top + front - 2, 5, 2, bar[1]);
        for (int i = x + 3; i < x + w - 5; i += 5)
        {
            uint u = DotHash.U(seed, i, 37);
            var c = Cloth[u % (uint)Cloth.Length];
            int len = front + top - 12 - (int)((u >> 4) % 6);
            o.Set(i + 2, ry + 2, bar[5]);
            o.Fill(i, ry + 3, 5, len, c);
            o.Fill(i, ry + 3, 1, len, c.Scale(1.25f));
            o.Fill(i + 4, ry + 3, 1, len, c.Scale(0.65f));
            o.Fill(i + 1, ry + 3, 3, 1, c.Scale(1.15f));
        }
    }

    /// <summary>로봇 충전대: 바닥 판 (사선 띠) · 뒤쪽 충전 기둥 · 팔 (표시등은 빛 그림에서).</summary>
    private static void RobotDock(Img o, int x, int y, int w, int top, int front, Facing f)
    {
        var p = Steel;
        int by = y + top + front - 6;
        o.Fill(x, y + 4, w, top + front - 4, Dark[2]);
        for (int i = 0; i < w; i++) { if ((i / 3 & 1) == 0) { o.Set(x + i, y + 4, H("#8a7230")); o.Set(x + i, by + 5, H("#8a7230")); } }
        Outline(o, x, y + 4, w, top + front - 4, Dark);
        // 충전 기둥 (뒤쪽 벽 쪽)
        int px = f == Facing.E ? x + 1 : f == Facing.W ? x + w - 7 : x + w / 2 - 3, py = f == Facing.N ? by - 6 : y;
        o.Fill(px, py, 6, 12, p[3]); o.Fill(px, py, 6, 1, p[5]); o.Fill(px + 5, py, 1, 12, p[1]);
        o.Fill(px + 1, py + 3, 4, 2, H("#1a3a44"));
        o.Fill(px + 2, py + 12, 2, 5, p[2]);   // 팔
    }

    /// <summary>산소 발생기: 흰 원통 둘 · 관 · 압력계 · 기포 창 (켜면 기포는 빛 그림에서).</summary>
    private static void OxyGen(Img o, int x, int y, int w, int top, int front, Facing f)
    {
        var p = White;
        int n = Math.Max(1, w / 30) + (w >= 60 ? 0 : 1);
        int cw = (w - 2) / n - 2;
        for (int k = 0; k < n; k++)
        {
            int cx = x + 1 + k * (cw + 2);
            // 원통: 윗면 타원 · 몸 (왼쪽 밝게 · 오른쪽 그늘)
            o.Fill(cx, y + top / 2, cw, top / 2 + front, p[2]);
            for (int i = 0; i < cw; i++)
            {
                float t = i / (float)Math.Max(1, cw - 1);
                var col = t < 0.2f ? p[4] : t < 0.45f ? p[3] : t > 0.85f ? p[1] : p[2];
                for (int j = y + top / 2; j < y + top + front - 1; j++) o.Set(cx + i, j, col);
            }
            o.Oval(cx + cw / 2f - 0.5f, y + top / 2f, cw / 2f, top / 2f, p[4]);
            o.Oval(cx + cw / 2f - 0.5f, y + top / 2f, cw / 2f - 2, top / 2f - 2, p[5]);
            o.Fill(cx + 3, y + top + 4, cw - 6, 7, Glass);   // 기포 창
            o.Fill(cx + 3, y + top + 4, 1, 7, GlassHi);
            o.Fill(cx, y + top + front - 2, cw, 2, p[0]);
        }
        // 관 · 압력계
        o.Fill(x + 1, y + top + front - 9, w - 2, 2, H("#6a7480"));
        o.Fill(x + w / 2 - 3, y + top + front - 15, 6, 6, H("#d8dce0")); o.Set(x + w / 2, y + top + front - 13, H("#c83a2a"));
    }

    /// <summary>침대: 틀 · 매트 · 베개 · 담요 (주인의 색 — 세계 값 · 없으면 회청) · 앞 서랍.</summary>
    private static void Bed(Img o, int x, int y, int w, int top, int front, Facing f, FurnFill fill)
    {
        var frame = Steel;
        var blanket = fill.Owner >= 0 ? Cloth[fill.Owner % Cloth.Length] : H("#4d5a6a");
        bool vertical = top > w;   // 세로로 긴 침대 (머리가 위 — 위에서 보이는 쪽이 길다)
        Box(o, x, y, w, top, front, frame);
        o.Fill(x + 2, y + 2, w - 4, top - 3, H("#c9c4b8"));   // 매트
        if (vertical)
        {
            o.Fill(x + 4, y + 3, w - 8, 9, H("#e6e3dc")); o.Fill(x + 4, y + 11, w - 8, 1, H("#a8a49a"));   // 베개
            o.Fill(x + 2, y + 15, w - 4, top - 16, blanket);
            o.Fill(x + 2, y + 15, w - 4, 2, blanket.Scale(1.3f));   // 접은 끝
            o.Fill(x + w - 3, y + 15, 1, top - 16, blanket.Scale(0.7f));
        }
        else
        {
            bool headLeft = f != Facing.E;
            int px = headLeft ? x + 3 : x + w - 12;
            o.Fill(px, y + 3, 9, top - 6, H("#e6e3dc"));
            int bx = headLeft ? x + 14 : x + 2, bw = w - 16;
            o.Fill(bx, y + 2, bw, top - 3, blanket);
            o.Fill(headLeft ? bx : bx + bw - 2, y + 2, 2, top - 3, blanket.Scale(1.3f));
            o.Fill(bx, y + top - 2, bw, 1, blanket.Scale(0.7f));
        }
        o.Fill(x + 3, y + top + 2, w - 6, front - 4, frame[1]);   // 서랍
        o.Fill(x + w / 2 - 2, y + top + front / 2 - 1, 4, 1, frame[4]);
    }

    /// <summary>스스로 빛나는 부분 (켜진 장치 — 곱한 뒤에 얹는다): 조작대 화면 · 재배 생장등 · 충전대 표시등 · 산소 발생기 기포 · 식량 보관함 온도등. 망가지면 붉게 깜빡.</summary>
    public static Img? Glow(string type, int bw, int bh, Facing f, int state, int frame)
    {
        if (state == 0) return null;   // 꺼짐
        int rise = Rise(type, bw, bh, f);
        var (front, top) = Size(type, bw, bh, f);
        var o = new Img(bw * T, bh * T + rise);
        int fy = o.H - front, ty = fy - top, W = o.W;
        bool broken = state == 2;
        if (broken && (frame >> 2 & 1) == 0) return o;
        var red = H("#ff4a3a");
        switch (type)
        {
            case "Console":
            {
                int sh = Math.Max(6, top * 5 / 9);
                var scr = broken ? red : H("#3fb8d8");
                o.Fill(5, ty + 2, W - 10, sh - 2, scr.WithA(150));
                for (int j = ty + 3; j < ty + sh - 1; j += 2) o.Fill(6, j, (W - 12) * (1 + (frame + j) % 3) / 3, 1, broken ? red : H("#bff4ff"));
                break;
            }
            case "GrowRack" or "PlantRack":
            {
                var lamp = broken ? red : H("#f08ae0");
                bool vertical = top > W - 2;   // 생장등 막대: 선반 테 위 (가로면 위 끝 · 세로면 왼쪽 끝) · 잎 위로 옅은 분홍 빛
                if (vertical) { o.Fill(2, ty + 2, 2, top - 4, lamp); o.Fill(4, ty + 2, W - 8, top - 4, lamp.WithA(28)); }
                else { o.Fill(3, ty + 1, W - 6, 2, lamp); o.Fill(3, ty + 3, W - 6, top - 6, lamp.WithA(28)); }
                break;
            }
            case "RobotDock":
            {
                int px = f == Facing.E ? 3 : f == Facing.W ? W - 5 : W / 2 - 1, py = f == Facing.N ? fy - 4 : ty;
                o.Fill(px, py + 1, 2, 2, broken ? red : (frame >> 3 & 1) == 0 ? H("#57e07a") : H("#2a8a44"));
                break;
            }
            case "OxygenGenerator":
            {
                int n = Math.Max(1, (W - 2) / 30) + (W - 2 >= 60 ? 0 : 1), cw = (W - 4) / n - 2;
                for (int k = 0; k < n; k++)
                {
                    int cx = 2 + k * (cw + 2);
                    for (int b = 0; b < 3; b++) { int by = fy + 10 - (frame / 2 + b * 3 + k) % 7; o.Set(cx + 4 + b * 2 % Math.Max(1, cw - 7), by, broken ? red : H("#bfefff")); }
                }
                break;
            }
            case "FoodLocker":
                o.Fill(5, fy + 3, 2, 2, broken ? red : H("#7cff9a"));
                break;
            default: return null;
        }
        return o;
    }
}

/// <summary>
/// 색 맞추기 (텍스처 T2): 옛 도트 설비 그림 (새로 그리지 않은 70여 종)을 새 색표 쪽으로 — 채도를 낮추고 (큰 면은 낮은 채도 · 채도는 사람 · 화면 빛 · 사고 신호에만) ·
/// 그늘 쪽은 푸른 보라 (#16142a)로 · 밝은 쪽은 조금 따뜻하게 비튼다. 구울 때 한 번 (화면 · 표시등은 따로 그려 그대로).
/// </summary>
public static class ArtPalette
{
    public static readonly Rgba Shade = Rgba.Hex("#16142a"), WarmLight = Rgba.Hex("#fff0d4");

    public static Img Unify(Img src)
    {
        var o = new Img(src.W, src.H);
        for (int i = 0; i < src.Px.Length; i += 4)
        {
            byte a = src.Px[i + 3];
            if (a == 0) continue;
            float r = src.Px[i], g = src.Px[i + 1], b = src.Px[i + 2];
            float l = 0.299f * r + 0.587f * g + 0.114f * b;
            const float keep = 0.72f;   // 채도 72%
            r = l + (r - l) * keep; g = l + (g - l) * keep; b = l + (b - l) * keep;
            if (l < 70) { float t = 0.22f * (1 - l / 70f); r += (Shade.R - r) * t; g += (Shade.G - g) * t; b += (Shade.B - b) * t; }
            else if (l > 170) { float t = 0.08f * (l - 170) / 85f; r += (WarmLight.R - r) * t; g += (WarmLight.G - g) * t; b += (WarmLight.B - b) * t; }
            o.Px[i] = (byte)Math.Clamp(r, 0, 255); o.Px[i + 1] = (byte)Math.Clamp(g, 0, 255); o.Px[i + 2] = (byte)Math.Clamp(b, 0, 255); o.Px[i + 3] = a;
        }
        return o;
    }
}
