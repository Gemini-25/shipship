using System;

namespace ShipSim.Art;

// 도트 바닥 · 벽 타일 (한 칸 32도트).
//   바닥 6재질 × 4가지(칸마다 골라 반복이 덜 보이게): 금속판 · 격자(아래로 관이 비친다) · 타일(줄눈) · 고무(돌기) · 카펫(결) · 기관 바닥(미끄럼 막이 무늬).
//   벽 3종류(내벽 · 칸막이 · 외판) × 이웃 16가지: 벽끼리 이어 붙고, 바닥에 닿은 가장자리만 테가 선다.
//   벽의 남쪽 앞면(아래 칸이 바닥일 때 그 칸 위쪽에 겹쳐 그린다): 판 · 리벳 · 띠 조명 — 양 끝이 트였으면 마감.

public enum DotFloor : byte { Metal, Grate, Tile, Rubber, Carpet, Engine }
public enum DotWall : byte { Panel, Partition, Hull }

public static class DotTiles
{
    public const int T = 32, Face = 10, Variants = 4;

    public static readonly int FloorMetal = Ramps.Add("#262a33", "#323843", "#3e4552", "#4b5361", "#5a6372", "#6e7887");
    public static readonly int FloorTile = Ramps.Add("#5c6268", "#7c848a", "#9aa2a8", "#b6bdc2", "#cdd3d6", "#e2e6e8");
    public static readonly int FloorRubber = Ramps.Add("#161b18", "#1f2622", "#29312c", "#343e38", "#414c45");
    public static readonly int FloorCarpet = Ramps.Add("#2a1720", "#3b2130", "#4d2c3e", "#5f384c", "#73465c");
    public static readonly int FloorDeep = Ramps.Add("#07090c", "#0d1015", "#14181f");
    public static readonly int WallPanel = Ramps.Add("#11151b", "#1a2028", "#252c36", "#323a46", "#424c5a", "#56616f");
    public static readonly int WallPart = Ramps.Add("#1c2129", "#28303a", "#36404c", "#46515e", "#5a6674", "#717d8b");
    public static readonly int WallHull = Ramps.Add("#0a0d12", "#11161d", "#1a2029", "#252d38", "#323c49", "#435060");

    // ───────────────────────────── 바닥 ─────────────────────────────

    /// <summary>바닥 한 칸 (재질 · 몇 번째 모양).</summary>
    public static Img Floor(DotFloor kind, int variant)
    {
        var d = new Dot(T, T);
        int seed = (int)kind * 31 + variant * 7 + 3;
        switch (kind)
        {
            case DotFloor.Grate:
                d.Rect(0, 0, T, T, FloorDeep, 1);
                // 아래로 비치는 관 (모양마다 다르게)
                if (variant is 1 or 3) { d.Rect(0, 10 + variant, T, 5, Ramps.Steel, 1); d.Rect(0, 11 + variant, T, 1, Ramps.Steel, 2); }
                if (variant == 2) { d.Rect(18, 0, 4, T, Ramps.ClothBlue, 0); }
                d.Part = 2;
                for (int y = 0; y < T; y += 4) d.Rect(0, y, T, 1, FloorMetal, 3);
                for (int x = 0; x < T; x += 8) d.Rect(x, 0, 2, T, FloorMetal, 2);
                d.Bevel();
                for (int i = 0; i < T; i++) { d.Mark(i, 0, FloorMetal, 4); d.Mark(0, i, FloorMetal, 4); d.Mark(i, T - 1, FloorMetal, 1); d.Mark(T - 1, i, FloorMetal, 1); }
                break;
            case DotFloor.Tile:
                d.Rect(0, 0, T, T, FloorTile, 3);
                d.Bevel();
                for (int i = 0; i < T; i++) { d.Mark(i, 15, FloorTile, 1); d.Mark(15, i, FloorTile, 1); d.Mark(i, 0, FloorTile, 4); d.Mark(0, i, FloorTile, 4); d.Mark(i, 16, FloorTile, 4); d.Mark(16, i, FloorTile, 4); d.Mark(i, T - 1, FloorTile, 1); d.Mark(T - 1, i, FloorTile, 1); }
                for (int k = 0; k < 6; k++) d.Mark(2 + (int)(DotHash.F(seed, k) * 28), 2 + (int)(DotHash.F(seed, k, 1) * 28), FloorTile, 2);
                if (variant == 3) { d.Mark(5, 5, FloorTile, 5); d.Mark(21, 21, FloorTile, 5); } // 반짝
                break;
            case DotFloor.Rubber:
                d.Rect(0, 0, T, T, FloorRubber, 2);
                d.Bevel();
                for (int y = 2; y < T; y += 6)
                    for (int x = 2 + ((y / 6) & 1) * 3; x < T; x += 6) { d.Mark(x, y, FloorRubber, 4); d.Mark(x + 1, y + 1, FloorRubber, 0); d.Mark(x + 1, y, FloorRubber, 3); }
                for (int i = 0; i < T; i++) { d.Mark(i, T - 1, FloorRubber, 0); d.Mark(T - 1, i, FloorRubber, 0); }
                break;
            case DotFloor.Carpet:
                d.Rect(0, 0, T, T, FloorCarpet, 2);
                // 짠 결 (대각선 무늬) · 드문 보풀
                for (int y = 0; y < T; y++)
                    for (int x = 0; x < T; x++)
                    {
                        if (((x + y) & 3) == 0) d.Mark(x, y, FloorCarpet, 3);
                        else if (((x - y) & 7) == 0) d.Mark(x, y, FloorCarpet, 1);
                        else if (DotHash.F(seed, x, y) < 0.03f) d.Mark(x, y, FloorCarpet, 4);
                    }
                // 가장자리 띠 (카펫 테)
                for (int x = 0; x < T; x++) { d.Mark(x, 1, FloorCarpet, 4); d.Mark(x, T - 2, FloorCarpet, 0); }
                break;
            case DotFloor.Engine:
                d.Rect(0, 0, T, T, FloorMetal, 2);
                d.Bevel();
                // 미끄럼 막이 다이아몬드 무늬
                for (int y = 2; y < T - 1; y += 4)
                    for (int x = 2 + ((y / 4) & 1) * 2; x < T - 1; x += 4) { d.Mark(x, y, FloorMetal, 4); d.Mark(x + 1, y + 1, FloorMetal, 1); }
                for (int i = 0; i < T; i++) { d.Mark(i, 0, FloorMetal, 4); d.Mark(0, i, FloorMetal, 4); d.Mark(i, T - 1, FloorMetal, 0); d.Mark(T - 1, i, FloorMetal, 0); }
                break;
            default:
                d.Rect(0, 0, T, T, FloorMetal, 3);
                d.Bevel();
                for (int i = 0; i < T; i++) { d.Mark(i, 0, FloorMetal, 4); d.Mark(0, i, FloorMetal, 4); d.Mark(i, T - 1, FloorMetal, 1); d.Mark(T - 1, i, FloorMetal, 1); }
                for (int i = 1; i < T - 1; i++) if ((i & 3) != 0) d.Mark(i, T / 2, FloorMetal, 2);
                foreach (var (x, y) in new[] { (3, 3), (T - 4, 3), (3, T - 4), (T - 4, T - 4) }) { d.Mark(x, y, FloorMetal, 5); d.Mark(x + 1, y + 1, FloorMetal, 1); }
                if (variant == 2)
                {
                    // 점검 뚜껑
                    for (int i = 8; i < 24; i++) { d.Mark(i, 7, FloorMetal, 1); d.Mark(i, 13, FloorMetal, 4); }
                    d.Mark(9, 10, FloorMetal, 5); d.Mark(22, 10, FloorMetal, 5);
                }
                break;
        }
        // 긁힘 (모든 재질 · 모양마다 다르게)
        if (kind is DotFloor.Metal or DotFloor.Engine)
            for (int k = 0; k < 4 + variant; k++)
            {
                int x = 2 + (int)(DotHash.F(seed, k, 5) * (T - 8)), y = 2 + (int)(DotHash.F(seed, k, 6) * (T - 6));
                int len = 2 + (int)(DotHash.F(seed, k, 7) * 3);
                for (int j = 0; j < len; j++) d.Mark(x + j, y + (j >> 1), FloorMetal, DotHash.F(seed, k, 8) < 0.5f ? 4 : 2);
            }
        return d.ToImg(outline: false);
    }

    // ───────────────────────────── 벽 ─────────────────────────────

    public static int WallRamp(DotWall k) => k switch { DotWall.Partition => WallPart, DotWall.Hull => WallHull, _ => WallPanel };

    /// <summary>벽 윗면 한 칸 (mask: 1 북 · 2 동 · 4 남 · 8 서 — 그쪽도 벽이면 켠다).</summary>
    public static Img WallTop(DotWall kind, int mask)
    {
        var d = new Dot(T, T);
        int r = WallRamp(kind);
        bool n = (mask & 1) != 0, e = (mask & 2) != 0, s = (mask & 4) != 0, w = (mask & 8) != 0;
        d.Rect(0, 0, T, T, r, 2);
        // 트인 쪽: 바닥에 닿은 테 (위 · 왼쪽 밝게, 아래 · 오른쪽 어둡게 — 두께 2)
        for (int i = 0; i < T; i++)
        {
            if (!n) { d.Mark(i, 0, r, 4); d.Mark(i, 1, r, 3); }
            if (!w) { d.Mark(0, i, r, 4); d.Mark(1, i, r, 3); }
            if (!s) { d.Mark(i, T - 1, r, 0); d.Mark(i, T - 2, r, 1); }
            if (!e) { d.Mark(T - 1, i, r, 0); d.Mark(T - 2, i, r, 1); }
        }
        // 윗면 무늬: 외판은 리벳 두 줄 · 내벽은 가운데 이음 · 칸막이는 가운데 볼록 줄
        bool horiz = (e || w) && !(n || s);
        switch (kind)
        {
            case DotWall.Hull:
                for (int i = 4; i < T - 3; i += 6)
                {
                    if (horiz || !(n || s)) { d.Mark(i, 9, r, 4); d.Mark(i, 22, r, 4); d.Mark(i + 1, 10, r, 0); d.Mark(i + 1, 23, r, 0); }
                    else { d.Mark(9, i, r, 4); d.Mark(22, i, r, 4); d.Mark(10, i + 1, r, 0); d.Mark(23, i + 1, r, 0); }
                }
                for (int i = 0; i < T; i++) if (((i / 3) & 1) == 0) { if (horiz) d.Mark(i, 16, r, 3); else d.Mark(16, i, r, 3); }
                break;
            case DotWall.Partition:
                for (int i = 0; i < T; i++) { if (horiz || !(n || s)) { d.Mark(i, 15, r, 4); d.Mark(i, 16, r, 1); } if (n || s) { d.Mark(15, i, r, 4); d.Mark(16, i, r, 1); } }
                break;
            default:
                for (int i = 2; i < T - 2; i++) { if (horiz) d.Mark(i, 16, r, 1); else if (n || s) d.Mark(16, i, r, 1); }
                d.Mark(6, 6, r, 4); d.Mark(T - 7, T - 7, r, 0);
                break;
        }
        return d.ToImg(outline: false);
    }

    /// <summary>벽 남쪽 앞면 (32×10): 판 · 리벳 · (내벽은) 띠 조명 — 양 끝이 트였으면 마감선.</summary>
    public static Img WallFace(DotWall kind, bool openW, bool openE, bool light)
    {
        var d = new Dot(T, Face);
        int r = WallRamp(kind);
        d.Rect(0, 0, T, Face, r, 3);
        d.Bevel();
        for (int x = 0; x < T; x++) { d.Mark(x, 0, r, 5); d.Mark(x, Face - 1, r, 0); d.Mark(x, Face - 2, r, 1); }
        if (openW) for (int y = 0; y < Face; y++) d.Mark(0, y, r, 5);
        if (openE) for (int y = 0; y < Face; y++) d.Mark(T - 1, y, r, 0);
        switch (kind)
        {
            case DotWall.Hull:
                for (int x = 3; x < T; x += 7) { d.Mark(x, 3, r, 5); d.Mark(x, 6, r, 5); }
                for (int y = 1; y < Face - 2; y++) d.Mark(16, y, r, 1);
                break;
            case DotWall.Partition:
                for (int x = 2; x < T - 2; x++) d.Mark(x, 4, r, 4);
                break;
            default:
                for (int y = 1; y < Face - 2; y++) d.Mark(T / 2, y, r, 1);
                d.Mark(3, 3, r, 5); d.Mark(T - 4, 3, r, 5);
                if (light) for (int x = 9; x < 23; x++) { d.Mark(x, 4, Ramps.Cyan, 3); d.Mark(x, 5, Ramps.Cyan, 2); }
                break;
        }
        return d.ToImg(outline: false);
    }

    // ───────────────────────────── 문 ─────────────────────────────

    /// <summary>
    /// 미닫이 문 한 칸 (32×32 · 가로 벽의 문은 아래에 앞면 10줄이 붙어 32×42).
    /// hWall: 가로로 놓인 벽의 문(위아래 방을 잇는다 — 문짝이 좌우로) · open 0~6 · kind 0 보통 · 1 바깥(노란 줄무늬) · 2 격벽(두꺼운 주황 띠)
    /// light 0 대기(파랑) · 1 열림(초록) · 2 정전(빨강) · 3 잠김(노랑).
    /// </summary>
    public static Img Door(bool hWall, int open, int kind, int light)
    {
        int h = hWall ? T + Face : T;
        var d = new Dot(T, h);
        int frame = kind == 1 ? WallHull : WallPanel;
        int panel = kind == 1 ? Ramps.SteelWarm : Ramps.Steel;
        int half = 16 - (int)MathF.Round(open / 6f * 12f); // 한쪽 문짝 너비 (16 → 4)
        // 문틀 (양 끝 4도트) · 문짝 둘
        void R(int a, int b, int len, int thick, int ramp, int shade)
        {
            // a: 벽 방향 위치 · b: 두께 방향 위치
            if (hWall) d.Rect(a, b, len, thick, ramp, shade); else d.Rect(b, a, thick, len, ramp, shade);
        }
        d.Part = 1; R(0, 0, 4, T, frame, 3); d.Part = 2; R(T - 4, 0, 4, T, frame, 3);
        int th0 = kind == 2 ? 2 : 4, th = T - 2 * th0;
        if (half > 4)
        {
            d.Part = 3; R(4, th0, half - 4, th, panel, 3);
            d.Part = 4; R(T - half, th0, half - 4, th, panel, 3);
        }
        else { d.Part = 3; R(4, th0, 1, th, panel, 2); d.Part = 4; R(T - 5, th0, 1, th, panel, 2); }
        if (hWall && open < 3)
        {
            // 닫힌 문의 앞면 (아래 칸 위쪽에 겹친다)
            d.Part = 5; d.Rect(0, T, T, Face, frame, 3);
        }
        d.Bevel();
        void M(int a, int b, int ramp, int shade) { if (hWall) d.Mark(a, b, ramp, shade); else d.Mark(b, a, ramp, shade); }
        if (half > 4)
        {
            // 가운데 이음 · 손잡이 홈 · 줄무늬
            for (int b = th0; b < th0 + th; b++) { M(half - 1, b, Ramps.Ink, 1); M(T - half, b, Ramps.Ink, 1); }
            for (int b = th0 + 3; b < th0 + th - 3; b += 4)
            {
                if (kind == 1) { M(6, b, Ramps.Hazard, 3); M(7, b + 1, Ramps.Ink, 1); M(T - 7, b, Ramps.Hazard, 3); M(T - 8, b + 1, Ramps.Ink, 1); }
                if (kind == 2) { M(half - 4, b, Ramps.ClothOrange, 3); M(T - half + 3, b, Ramps.ClothOrange, 3); }
            }
            M(half - 3, T / 2, panel, 1); M(T - half + 2, T / 2, panel, 1);
        }
        int lr = light switch { 1 => Ramps.Led, 2 => Ramps.Alarm, 3 => Ramps.Hazard, _ => Ramps.Cyan };
        M(1, 2, lr, 4); M(2, 2, lr, 3); M(T - 3, 2, lr, 4); M(T - 2, 2, lr, 3);
        if (hWall && open < 3) for (int x = 2; x < T - 2; x++) { d.Mark(x, T + 3, lr, 2); }
        return d.ToImg(outline: false);
    }

    /// <summary>바닥 아틀라스: 줄 = 재질(6) · 칸 = 모양(4).</summary>
    public static Img FloorAtlas()
    {
        var img = new Img(Variants * T, 6 * T);
        for (int k = 0; k < 6; k++) for (int v = 0; v < Variants; v++) img.Draw(Floor((DotFloor)k, v), v * T, k * T);
        return img;
    }

    /// <summary>벽 아틀라스: 줄 0~2 = 윗면(종류) × 이웃 16 · 줄 3 = 앞면(종류 × 끝 4 × 조명 2 = 24칸, 높이 10).</summary>
    public static Img WallAtlas()
    {
        var img = new Img(16 * T, 3 * T + 2 * Face);
        for (int k = 0; k < 3; k++) for (int m = 0; m < 16; m++) img.Draw(WallTop((DotWall)k, m), m * T, k * T);
        for (int k = 0; k < 3; k++)
            for (int ends = 0; ends < 4; ends++)
                for (int li = 0; li < 2; li++)
                {
                    int idx = k * 8 + ends * 2 + li; // 0..23
                    int col = idx % 16, row = idx / 16;
                    img.Draw(WallFace((DotWall)k, (ends & 1) != 0, (ends & 2) != 0, li == 1), col * T, 3 * T + row * Face);
                }
        return img;
    }

    /// <summary>벽 아틀라스 안에서 앞면 자리.</summary>
    public static (int x, int y) FaceCell(DotWall kind, bool openW, bool openE, bool light)
    {
        int idx = (int)kind * 8 + ((openW ? 1 : 0) | (openE ? 2 : 0)) * 2 + (light ? 1 : 0);
        return (idx % 16 * T, 3 * T + idx / 16 * Face);
    }
}
