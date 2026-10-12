using System;

namespace ShipSim.Art;

/// <summary>사진 장면 (Core PhotoScene 과 같은 순서는 아니다 — 그림 갈래만).</summary>
public enum DotPhotoScene : byte { Plain, Window, Birthday, Meal, Work }

/// <summary>
/// 도트 생활 물건 (v17.3 정보 차이): 사람마다 다른 컵 · 깨진 조각과 쏟은 음료 · 벽에 건 사진 · 만든 모형/선물 ·
/// 머리맡 독서등 · 개수대 그릇 더미 · 설거지 당번표 · 손목 단말.
/// </summary>
public static class DotInfo
{
    /// <summary>컵 12 × 12: shape 0 키 큰 머그 · 1 받침 찻잔 · 2 법랑 캠핑 컵 · 3 뚜껑 텀블러 / pattern 0 줄 · 1 점 · 2 띠 · 3 별 / 이어 붙였으면 금빛 금.</summary>
    public static Img Cup(int shape, int pattern, bool left, int ramp, bool mended, int seed)
    {
        var d = new Dot(12, 12);
        int body = shape == 2 ? 4 : 3;
        int x0 = 3, x1 = 8, y0 = 2, y1 = 10;
        switch (shape)
        {
            case 0: d.Part = 1; d.Rect(3, 2, 5, 9, ramp, body); d.Part = 2; d.Rect(8, 4, 2, 1, ramp, 2); d.Rect(9, 4, 1, 4, ramp, 2); d.Rect(8, 7, 2, 1, ramp, 2); break;
            case 1:
                d.Part = 1; d.Rect(1, 9, 10, 2, ramp, 2);
                d.Part = 2; d.Rect(2, 4, 7, 3, ramp, body); d.Rect(3, 7, 5, 2, ramp, body);
                d.Part = 3; d.Rect(9, 5, 1, 2, ramp, 2);
                x0 = 2; x1 = 8; y0 = 4; y1 = 8; break;
            case 2:
                d.Part = 1; d.Rect(2, 3, 7, 7, ramp, body);
                d.Part = 2; d.Rect(2, 3, 7, 1, Ramps.Ink, 1); d.Mark(4, 3, Ramps.Ink, 0);
                d.Part = 3; d.Rect(9, 5, 2, 1, Ramps.Ink, 2); d.Rect(10, 5, 1, 3, Ramps.Ink, 2);
                x0 = 2; x1 = 8; y0 = 4; y1 = 9; break;
            default:
                d.Part = 1; d.Rect(3, 2, 6, 9, ramp, body); d.Rect(4, 10, 4, 1, ramp, body);
                d.Part = 2; d.Rect(2, 1, 8, 2, ramp, 1);
                x0 = 3; x1 = 8; y0 = 3; y1 = 10; break;
        }
        d.Bevel();
        int pr = ramp, ps = shape == 2 ? 2 : 4;
        switch (pattern)
        {
            case 0: for (int x = x0; x <= x1; x++) { d.Mark(x, y0 + (y1 - y0) / 3, pr, ps); d.Mark(x, y0 + 2 * (y1 - y0) / 3, pr, ps); } break;
            case 1: d.Mark(x0 + 1, y0 + 2, pr, ps); d.Mark(x1 - 1, y0 + 2, pr, ps); d.Mark(x0 + 2, y1 - 2, pr, ps); d.Mark(x1 - 2, y1 - 2, pr, ps); break;
            case 2: for (int x = x0; x <= x1; x++) { d.Mark(x, (y0 + y1) / 2, pr, ps); d.Mark(x, (y0 + y1) / 2 + 1, pr, ps); } break;
            default: { int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2; d.Mark(cx, cy, pr, ps); d.Mark(cx - 1, cy, pr, ps); d.Mark(cx + 1, cy, pr, ps); d.Mark(cx, cy - 1, pr, ps); d.Mark(cx, cy + 1, pr, ps); break; }
        }
        d.Mark(x0, y0 + 1, Ramps.Bandage, 3); // 유약 빛
        if (mended) // 금빛 이음매 (컵마다 다른 갈래)
        {
            int x = x0 + 1 + (seed % 3), y = y0;
            for (int i = 0; i < 6 && y <= y1; i++) { d.Mark(x, y, Ramps.Hazard, 4); y++; if ((seed + i) % 3 == 0) x++; else if ((seed + i) % 3 == 1) x--; x = Math.Clamp(x, x0, x1); }
        }
        var img = d.ToImg();
        if (left) img = Mirror(img);
        return img;
    }

    /// <summary>깨진 컵 24 × 20: 쏟은 음료(0 커피 · 1 차 · 2 물) 웅덩이 + 흩어진 조각 4~7 (하나는 손잡이 달림).</summary>
    public static Img Shards(int seed, int ramp, int spill)
    {
        var img = new Img(24, 20);
        var sc = spill switch { 0 => Rgba.Hex("#5c3820"), 1 => Rgba.Hex("#8c6630"), _ => Rgba.Hex("#80b2e6") };
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 24; x++)
            {
                float dx = (x - 13.5f) / 9f, dy = (y - 12f) / 6f, r = dx * dx + dy * dy + 0.15f * MathF.Sin(x * 0.9f + seed) * MathF.Cos(y * 0.7f);
                if (r < 1f) img.Blend(x, y, sc, 0.45f);
            }
        int n = 4 + seed % 4;
        for (int i = 0; i < n; i++)
        {
            float a = MathF.Tau * (i + DotHash.F(seed, i)) / n, rad = 2f + 7f * DotHash.F(seed, i + 3);
            int px = (int)(12 + MathF.Cos(a) * rad), py = (int)(10 + MathF.Sin(a) * rad * 0.8f);
            int sz = 1 + (int)(DotHash.F(seed, i + 7) * 2.5f);
            for (int yy = 0; yy < sz; yy++) for (int xx = 0; xx < sz + (i & 1); xx++) img.Set(px + xx, py + yy, Ramps.Get(ramp, i % 3 == 0 ? 4 : 3));
            img.Set(px, py, Ramps.Get(Ramps.Bandage, 3));
            if (i == 0) { img.Set(px + sz + 1, py, Ramps.Get(ramp, 2)); img.Set(px + sz + 2, py + 1, Ramps.Get(ramp, 2)); img.Set(px + sz + 1, py + 2, Ramps.Get(ramp, 2)); }
        }
        return img;
    }

    /// <summary>
    /// 벽에 건 사진 (w × h): 액자(찍은 사람마다 0 나무 · 1 검정 · 2 흰색 · 3 금색) · 배경(방 색) 또는 창밖 별 ·
    /// 그날의 사람들(그 사람 색 옷 · 머리) · 장면 물건(케이크 · 식탁 · 안전모 · 용접 불꽃) · 떠난 사람이 있으면 검은 리본.
    /// </summary>
    public static Img Photo(int w, int h, int frame, DotPhotoScene scene, int bgRamp, int[] people, int[] hair, bool mourning, int seed)
    {
        var d = new Dot(w, h);
        int fr = frame switch { 0 => Ramps.Wood, 1 => Ramps.Ink, 2 => Ramps.Bandage, _ => Ramps.Hazard };
        d.Part = 1; d.Rect(0, 0, w, h, fr, frame == 2 ? 3 : 2);
        d.Part = 2;
        int ix = 1, iy = 1, iw = w - 2, ih = h - 2;
        if (scene == DotPhotoScene.Window)
        {
            d.Rect(ix, iy, iw, ih, Ramps.Ink, 0);
            d.Bevel();
            int neb = Ramps.Accent[seed % Ramps.Accent.Length];
            for (int y = iy; y < iy + ih; y++)
                for (int x = ix; x < ix + iw; x++)
                {
                    float dx = (x - (ix + iw * (0.35f + 0.3f * DotHash.F(seed, 1)))) / (iw * 0.35f), dy = (y - (iy + ih * 0.5f)) / (ih * 0.35f);
                    if (dx * dx + dy * dy < 1f && ((x + y) & 1) == 0) d.Mark(x, y, neb, 1);
                }
            for (int i = 0; i < 6; i++) d.Mark(ix + (int)(DotHash.F(seed, i + 10) * iw), iy + (int)(DotHash.F(seed, i + 30) * ih), Ramps.Bandage, 3);
        }
        else
        {
            d.Rect(ix, iy, iw, ih, bgRamp, 3);
            int floorY = iy + ih * 72 / 100;
            d.Rect(ix, floorY, iw, iy + ih - floorY, bgRamp, 2);
            d.Bevel();
            int n = Math.Max(1, people.Length);
            for (int i = 0; i < people.Length; i++)
            {
                int x = ix + iw * (2 * i + 1) / (2 * n);
                int y = floorY - 3 - ((i + seed) & 1);
                d.Mark(x - 1, y + 2, people[i], 3); d.Mark(x, y + 2, people[i], 3); d.Mark(x + 1, y + 2, people[i], 3); d.Mark(x, y + 3, people[i], 2);
                d.Mark(x, y + 1, Ramps.Skin[2], 3);
                d.Mark(x, y, scene == DotPhotoScene.Work ? Ramps.Hazard : hair[i], scene == DotPhotoScene.Work ? 4 : 2);
            }
            switch (scene)
            {
                case DotPhotoScene.Birthday:
                    d.MarkRect(ix + iw / 2 - 2, floorY - 2, 5, 2, Ramps.Bandage, 3); d.MarkRect(ix + iw / 2 - 2, floorY - 1, 5, 1, Ramps.ClothRed, 3);
                    d.Mark(ix + iw / 2, floorY - 3, Ramps.Fire, 5);
                    break;
                case DotPhotoScene.Meal: d.MarkRect(ix, floorY - 1, iw, 1, Ramps.Wood, 2); break;
                case DotPhotoScene.Work: d.Mark(ix + iw - 2, iy + 2, Ramps.Fire, 6); break;
                default: d.MarkRect(ix + 1, iy + 1, iw - 2, 1, bgRamp, 4); break;
            }
        }
        d.Mark(ix + iw - 2, iy, Ramps.Bandage, 4); // 반사
        if (mourning) { d.Mark(0, 0, Ramps.Ink, 0); d.Mark(1, 0, Ramps.Ink, 0); d.Mark(0, 1, Ramps.Ink, 0); d.Mark(2, 0, Ramps.Ink, 0); d.Mark(0, 2, Ramps.Ink, 0); }
        return d.ToImg();
    }

    /// <summary>만든 모형 · 선물 갈래 (이름에 따라).</summary>
    public static int MadeKind(string name) =>
        name.Contains("탐사선") ? 0 : name.Contains("화물선") ? 1 : name.Contains("등대") ? 2 : name.Contains("정거장") ? 3 : name.Contains("범선") ? 4 :
        name.Contains("장갑") ? 5 : name.Contains("나무 새") ? 6 : name.Contains("악보") ? 7 : name.Contains("책갈피") ? 8 : name.Contains("오르골") ? 9 : 10;

    /// <summary>만든 것 14 × 14 (step 0~4: 만든 만큼 · 덜 된 부분은 비침 없이 빠진다).</summary>
    public static Img Made(int kind, int ramp, int step)
    {
        var d = new Dot(14, 14);
        bool P(int idx, int total) => step >= 4 || idx < (step * total + 3) / 4;
        switch (kind)
        {
            case 0: // 탐사선
                if (P(0, 4)) { d.Part = 1; d.Rect(5, 2, 4, 9, ramp, 3); d.Rect(6, 1, 2, 1, ramp, 3); }
                if (P(1, 4)) { d.Part = 2; d.Rect(2, 8, 3, 3, ramp, 2); }
                if (P(2, 4)) { d.Part = 3; d.Rect(9, 8, 3, 3, ramp, 2); }
                if (P(3, 4)) d.Mark(6, 4, Ramps.Glass, 4, true);
                break;
            case 1: // 화물선
                if (P(0, 3)) { d.Part = 1; d.Rect(1, 6, 12, 4, ramp, 3); }
                if (P(1, 3)) { d.Part = 2; d.Rect(3, 3, 3, 3, Ramps.Wood, 3); d.Rect(7, 3, 3, 3, Ramps.ClothOrange, 3); }
                if (P(2, 3)) { d.Part = 3; d.Rect(11, 4, 2, 2, ramp, 4); }
                break;
            case 2: // 등대
                if (P(0, 3)) { d.Part = 1; d.Rect(5, 4, 4, 9, Ramps.Bandage, 3); d.MarkRect(5, 7, 4, 1, Ramps.ClothRed, 3); d.MarkRect(5, 10, 4, 1, Ramps.ClothRed, 3); }
                if (P(1, 3)) { d.Part = 2; d.Rect(4, 2, 6, 2, Ramps.Steel, 3); }
                if (P(2, 3)) d.Mark(7, 1, Ramps.Fire, 6, true);
                break;
            case 3: // 정거장
                if (P(0, 3)) { d.Part = 1; d.Oval(7, 7, 5, 5, ramp, 3); d.Oval(7, 7, 3, 3, Ramps.Ink, 0); }
                if (P(1, 3)) { d.Part = 2; d.Rect(6, 6, 2, 2, Ramps.Steel, 4); }
                if (P(2, 3)) { d.Part = 3; d.Rect(0, 6, 2, 2, Ramps.Glass, 3); d.Rect(12, 6, 2, 2, Ramps.Glass, 3); }
                break;
            case 4: // 범선
                if (P(0, 3)) { d.Part = 1; d.Rect(2, 10, 10, 2, Ramps.Wood, 2); d.Rect(3, 12, 8, 1, Ramps.Wood, 1); }
                if (P(1, 3)) { d.Part = 2; d.Rect(7, 2, 1, 8, Ramps.Wood, 3); }
                if (P(2, 3)) { d.Part = 3; for (int y = 3; y < 9; y++) d.Rect(8, y, y - 2, 1, Ramps.Bandage, 3); }
                break;
            case 5: // 장갑 (뜨개)
                if (P(0, 2)) { d.Part = 1; d.Rect(3, 4, 7, 8, ramp, 3); d.Rect(3, 2, 2, 3, ramp, 3); d.Rect(6, 1, 2, 3, ramp, 3); }
                if (P(1, 2)) { d.Part = 2; d.Rect(10, 6, 2, 3, ramp, 3); d.MarkRect(3, 10, 7, 1, ramp, 1); }
                break;
            case 6: // 나무 새
                if (P(0, 2)) { d.Part = 1; d.Oval(6, 8, 4, 3, Ramps.Wood, 3); d.Rect(9, 5, 3, 3, Ramps.Wood, 3); }
                if (P(1, 2)) { d.Mark(11, 6, Ramps.Ink, 0, true); d.Mark(12, 6, Ramps.Hazard, 3, true); d.Part = 2; d.Rect(1, 7, 2, 2, Ramps.Wood, 2); }
                break;
            case 7: // 악보
            case 8: // 책갈피
                d.Part = 1; d.Rect(kind == 7 ? 2 : 5, 1, kind == 7 ? 10 : 4, 12, Ramps.Bandage, 3);
                if (kind == 7) for (int y = 3; y < 12; y += 3) d.MarkRect(3, y, 8, 1, Ramps.Ink, 2);
                else { d.MarkRect(5, 1, 4, 2, ramp, 3); d.Mark(7, 12, ramp, 3); }
                break;
            case 9: // 오르골
                if (P(0, 2)) { d.Part = 1; d.Rect(2, 5, 10, 7, Ramps.Wood, 3); d.MarkRect(2, 7, 10, 1, Ramps.Hazard, 3); }
                if (P(1, 2)) { d.Part = 2; d.Rect(12, 6, 2, 1, Ramps.Hazard, 4); d.Mark(7, 4, Ramps.Hazard, 4, true); }
                break;
            default: // 기관실 축소 모형
                if (P(0, 3)) { d.Part = 1; d.Rect(1, 5, 12, 6, ramp, 3); }
                if (P(1, 3)) { d.Part = 2; d.Rect(3, 2, 1, 3, Ramps.Steel, 4); d.Rect(6, 1, 1, 4, Ramps.Steel, 4); }
                if (P(2, 3)) d.Mark(9, 7, Ramps.Led, 4, true);
                break;
        }
        d.Bevel();
        return d.ToImg();
    }

    /// <summary>머리맡 독서등 12 × 12: 고장(테이프 감은 줄 · 깜빡) · 고침(따뜻한 빛).</summary>
    public static Img Lamp(bool fixedLamp, bool lit, int tapes)
    {
        var d = new Dot(12, 12);
        d.Part = 1; d.Rect(0, 9, 4, 2, Ramps.Steel, 2);
        d.Part = 2; d.Line(2, 9, 3, 4, Ramps.Steel, 3); d.Line(3, 4, 7, 3, Ramps.Steel, 3);
        d.Part = 3; d.Rect(6, 2, 5, 3, fixedLamp ? Ramps.ClothTeal : Ramps.Steel, fixedLamp ? 2 : 3);
        d.Bevel();
        if (!fixedLamp) for (int i = 0; i < Math.Min(3, tapes); i++) d.Mark(2 + i, 7 - i * 1, Ramps.Bandage, 3);
        if (lit) { d.Mark(7, 5, Ramps.Fire, 6, true); d.Mark(8, 5, Ramps.Fire, 6, true); d.Mark(9, 5, Ramps.Fire, 5, true); }
        return d.ToImg();
    }

    /// <summary>개수대 그릇 더미 (n 0~14): 탑 1~3 · 쌓인 만큼 높다 · 꼭대기에 컵.</summary>
    public static Img DishPile(int n)
    {
        var d = new Dot(20, 24);
        int stacks = n > 8 ? 3 : n > 3 ? 2 : 1, k = 0;
        for (int s = 0; s < stacks; s++)
        {
            int here = (n + stacks - 1 - s) / stacks, bx = 2 + s * 6;
            for (int i = 0; i < here; i++, k++)
            {
                d.Part = (byte)(1 + k);
                d.Rect(bx + (k % 3 == 1 ? 1 : 0), 21 - i * 2, 6, 2, k % 4 == 3 ? Ramps.Cloud : Ramps.Bandage, 3);
            }
        }
        if (n >= 6) { d.Part = 30; d.Rect(14, 21 - (n / stacks) * 2 - 3, 3, 3, Ramps.Wood, 3); }
        d.Bevel();
        return d.ToImg();
    }

    /// <summary>설거지 당번표 (줄 수만큼 · 사람 색 칸 · 오늘 줄은 노랗게 · 핀).</summary>
    public static Img Roster(int[] ramps, int today)
    {
        int rows = Math.Min(7, ramps.Length);
        var d = new Dot(12, 5 + rows * 3);
        d.Part = 1; d.Rect(0, 0, 12, d.H, Ramps.Bandage, 3);
        d.Bevel();
        d.Mark(6, 1, Ramps.Alarm, 3);
        for (int i = 0; i < rows; i++)
        {
            int y = 3 + i * 3;
            if (i == today) d.MarkRect(1, y - 1, 10, 3, Ramps.Hazard, 4);
            d.MarkRect(1, y, 2, 1, ramps[i], 3);
            d.MarkRect(4, y, 6, 1, Ramps.Ink, 2);
        }
        return d.ToImg();
    }

    /// <summary>손목 단말 7 × 6 (화면 빛 · 사람 색 띠).</summary>
    public static Img Tablet(int ramp)
    {
        var d = new Dot(7, 6);
        d.Part = 1; d.Rect(0, 0, 7, 5, Ramps.Ink, 1);
        d.Part = 2; d.Rect(1, 1, 5, 3, Ramps.Cyan, 3);
        d.MarkRect(2, 2, 3, 1, Ramps.Bandage, 3);
        d.Part = 3; d.Rect(0, 5, 7, 1, ramp, 3);
        return d.ToImg();
    }

    private static Img Mirror(Img src)
    {
        var o = new Img(src.W, src.H);
        for (int y = 0; y < src.H; y++) for (int x = 0; x < src.W; x++) o.Set(src.W - 1 - x, y, src.Get(x, y));
        return o;
    }
}
