using System;

namespace ShipSim.Art;

/// <summary>이동식 장비 · 통 · 충전 받침.</summary>
public enum DotGearKind : byte { Lamp, Heater, Fan, Purifier, Battery, Pump, Cart, Pail, Dock }

/// <summary>
/// 도트 이동식 장비 (24 × 24, 카트 32 × 24): 작업등(삼각대 · 겨눈 쪽 렌즈) · 히터(달아오른 코일) · 선풍기(도는 날 4장면) ·
/// 공기 청정기(푸른 띠) · 배터리(충전 칸) · 펌프(지느러미 · 떨림) · 카트(널판 · 바퀴 · 상자 · 통로 경고 띠) · 회수통(오수 높이) · 충전 받침(표시등).
/// </summary>
public static class DotGear
{
    public const int W = 24, H = 24, CartW = 32;

    /// <summary>
    /// on: 켜짐 · frame 0~3 · level 0~4 (충전 · 오수 높이 · 받침 표시등 0 없음 1 호박 켬 2 호박 끔 3 초록) ·
    /// dir 0 동 1 남 2 서 3 북 (작업등이 겨눈 쪽) · broken: 금 · 그을음 · extra: 카트 상자(1) · 통로 경고 띠(2).
    /// </summary>
    public static Img Gear(DotGearKind kind, bool on, int frame, int level, int dir, bool broken, int extra = 0)
    {
        var d = new Dot(kind == DotGearKind.Cart ? CartW : W, H);
        int cx = d.W / 2;
        switch (kind)
        {
            case DotGearKind.Lamp: Lamp(d, cx, on, level, dir); break;
            case DotGearKind.Heater:
                d.Part = 1; d.Rect(cx - 7, 8, 14, 14, Ramps.ClothRed, 1);
                d.Part = 2; d.Rect(cx - 8, 6, 16, 3, Ramps.ClothRed, 2);
                d.Part = 3; d.Rect(cx - 5, 11, 10, 8, Ramps.Ink, 0);
                for (int y = 12; y < 19; y += 2)
                    for (int x = cx - 4; x < cx + 4; x++)
                        d.Mark(x, y, on ? Ramps.Fire : Ramps.Char, on ? 3 + ((x + y + frame) % 3 == 0 ? 1 : 0) : 2);
                d.Part = 4; d.Rect(cx + 4, 7, 2, 1, Ramps.Steel, 4); d.Rect(cx - 6, 21, 3, 2, Ramps.Rubber, 2); d.Rect(cx + 3, 21, 3, 2, Ramps.Rubber, 2);
                break;
            case DotGearKind.Fan:
            {
                d.Part = 1; d.Rect(cx - 1, 15, 3, 6, Ramps.Steel, 2); d.Rect(cx - 5, 20, 11, 3, Ramps.Steel, 2);
                d.Part = 2; d.Oval(cx, 9, 8.5f, 8.5f, Ramps.Steel, 4);
                d.Part = 3; d.Oval(cx, 9, 7f, 7f, Ramps.Panel, 1);
                float a0 = (on ? frame : 0) * MathF.PI / 6f;
                for (int b = 0; b < 3; b++)
                {
                    float a = a0 + b * MathF.Tau / 3f;
                    for (int r = 2; r < 7; r++)
                    {
                        int x = cx + (int)MathF.Round(MathF.Cos(a) * r), y = 9 + (int)MathF.Round(MathF.Sin(a) * r);
                        d.Set(x, y, Ramps.ClothBlue, 3); d.Set(x + (int)MathF.Round(-MathF.Sin(a)), y + (int)MathF.Round(MathF.Cos(a)), Ramps.ClothBlue, 2);
                    }
                }
                d.Part = 4; d.Oval(cx, 9, 1.5f, 1.5f, Ramps.Steel, 4);
                for (int k = -6; k <= 6; k += 3) d.Mark(cx + k, 9, Ramps.Steel, 4); // 보호망 살
                break;
            }
            case DotGearKind.Purifier:
                d.Part = 1; d.Rect(cx - 6, 3, 12, 19, Ramps.ClothWhite, 3);
                d.Part = 2; d.Rect(cx - 6, 2, 12, 3, Ramps.ClothWhite, 4);
                for (int x = cx - 5; x < cx + 5; x += 2) d.Mark(x, 3, Ramps.Steel, 2);
                d.Part = 3; d.Rect(cx - 4, 8, 8, 9, Ramps.Steel, 3);
                for (int y = 9; y < 17; y += 2) d.Mark(cx - 3, y, Ramps.Steel, 1);
                for (int y = 9; y < 17; y += 2) d.Mark(cx + 2, y, Ramps.Steel, 1);
                if (on) d.MarkRect(cx - 4, 18, 8, 1, Ramps.Cyan, 3 + (frame & 1));
                d.Mark(cx + 4, 19, on ? Ramps.Led : Ramps.Panel, on ? 3 : 2);
                break;
            case DotGearKind.Battery:
                d.Part = 1; d.Rect(cx - 8, 8, 16, 14, Ramps.ClothGreen, 1);
                d.Part = 2; d.Rect(cx - 8, 6, 16, 3, Ramps.ClothGreen, 2);
                d.Part = 3; d.Rect(cx - 5, 4, 3, 2, Ramps.Alarm, 3); d.Rect(cx + 2, 4, 3, 2, Ramps.Steel, 4);
                d.Part = 4; d.Rect(cx - 6, 12, 12, 6, Ramps.Panel, 0);
                for (int k = 0; k < 4; k++) d.MarkRect(cx - 5 + k * 3, 13, 2, 4, k < level ? Ramps.Led : Ramps.Panel, k < level ? 3 : 2);
                d.MarkRect(cx - 8, 20, 16, 1, Ramps.Hazard, 3);
                break;
            case DotGearKind.Pump:
            {
                int j = on ? (frame & 1) : 0; // 떨림
                d.Part = 1; d.Rect(cx - 9, 13, 18, 9, Ramps.ClothBlue, 2);
                d.Part = 2; d.Rect(cx - 6 + j, 6, 11, 8, Ramps.ClothBlue, 3);
                for (int x = cx - 5 + j; x < cx + 5 + j; x += 2) d.Mark(x, 7, Ramps.ClothBlue, 1);
                for (int x = cx - 5 + j; x < cx + 5 + j; x += 2) d.Mark(x, 10, Ramps.ClothBlue, 1);
                d.Part = 3; d.Rect(cx + 6, 15, 4, 3, Ramps.Rubber, 3); d.Rect(cx - 10, 15, 4, 3, Ramps.Rubber, 3);
                d.Part = 4; d.Mark(cx - 6, 18, on ? Ramps.Led : Ramps.Alarm, on ? 3 : 2); d.MarkRect(cx - 9, 21, 18, 1, Ramps.Panel, 1);
                break;
            }
            case DotGearKind.Cart:
            {
                int spin = frame & 1;
                d.Part = 1; d.Rect(4, 8, 25, 11, Ramps.Steel, 2);
                d.Part = 2; for (int x = 5; x < 28; x += 6) d.Rect(x, 9, 5, 9, Ramps.Steel, 3);
                d.Part = 3; d.Rect(1, 6, 2, 13, Ramps.Steel, 4); d.Rect(1, 6, 4, 2, Ramps.Steel, 4); d.Rect(1, 17, 4, 2, Ramps.Steel, 4); d.Rect(0, 9, 2, 6, Ramps.Rubber, 2);
                d.Part = 5;
                foreach (int x in new[] { 6, 24 }) { d.Rect(x, 19, 4, 4, Ramps.Rubber, 1); d.Mark(x + 1 + spin, 20 + spin, Ramps.Steel, 4); }
                if ((extra & 1) != 0)
                {
                    d.Part = 6; d.Rect(10, 3, 12, 11, Ramps.Wood, 3);
                    d.Line(10, 3, 21, 13, Ramps.Wood, 1, mark: true); d.Line(21, 3, 10, 13, Ramps.Wood, 1, mark: true);
                    d.MarkRect(10, 3, 12, 1, Ramps.Wood, 4);
                }
                if ((extra & 2) != 0) for (int x = 4; x < 29; x++) d.Mark(x, 17, ((x / 3) & 1) == 0 ? Ramps.Hazard : Ramps.Ink, ((x / 3) & 1) == 0 ? 3 : 1);
                break;
            }
            case DotGearKind.Pail:
            {
                d.Part = 1;
                for (int y = 8; y < 21; y++) { int hw = 7 - (y - 8) / 4; d.Rect(cx - hw, y, hw * 2, 1, Ramps.ClothTeal, 1); }
                d.Part = 2; d.Rect(cx - 8, 6, 16, 3, Ramps.Steel, 4);
                d.Part = 3; d.Rect(cx - 6, 7, 12, 1, Ramps.Panel, 0);
                if (level > 0) d.MarkRect(cx - 6, 7, 12, 1, Ramps.Soil, Math.Min(3, level));
                d.Line(cx - 7, 6, cx - 4, 2, Ramps.Steel, 4, mark: false); d.Line(cx - 4, 2, cx + 4, 2, Ramps.Steel, 4); d.Line(cx + 4, 2, cx + 7, 6, Ramps.Steel, 4);
                if (level >= 4) d.Mark(cx + 8, 10, Ramps.Glass, 4, true);
                break;
            }
            case DotGearKind.Dock:
                d.Part = 1; d.Rect(1, 3, 22, 20, Ramps.Panel, 2);
                d.Part = 2; d.Rect(3, 5, 18, 16, Ramps.Panel, 1);
                if (level > 0) d.MarkRect(19, 4, 2, 2, level == 3 ? Ramps.Led : Ramps.Hazard, level == 2 ? 1 : 4);
                break;
        }
        if (kind == DotGearKind.Lamp && dir == 2) d = d.Flip(); // 서: 렌즈가 왼쪽
        d.Bevel();
        if (broken) Broken(d);
        return d.ToImg();
    }

    private static void Lamp(Dot d, int cx, bool on, int level, int dir)
    {
        // 삼각대
        d.Part = 1;
        d.Line(cx, 12, cx - 7, 22, Ramps.Steel, 2); d.Line(cx, 12, cx + 7, 22, Ramps.Steel, 2); d.Line(cx, 12, cx + 1, 21, Ramps.Steel, 3);
        d.Rect(cx - 8, 22, 2, 1, Ramps.Rubber, 2); d.Rect(cx + 6, 22, 2, 1, Ramps.Rubber, 2);
        // 머리
        d.Part = 2;
        int lit = on ? Ramps.Fire : Ramps.SteelWarm;
        switch (dir)
        {
            case 0: // 동: 오른쪽이 렌즈
            case 2:
            {
                d.Rect(cx - 6, 4, 11, 8, Ramps.Hazard, 3);
                d.Part = 3; d.Rect(cx + 4, 3, 3, 10, Ramps.Hazard, 2);
                d.MarkRect(cx + 5, 4, 2, 8, lit, on ? 6 : 2);
                d.Mark(cx - 5, 6, level > 2 ? Ramps.Led : level > 0 ? Ramps.Hazard : Ramps.Alarm, on ? 3 : 1);
                break;
            }
            case 1: // 남: 렌즈가 정면
                d.Rect(cx - 6, 3, 12, 10, Ramps.Hazard, 2);
                d.Part = 3; d.Rect(cx - 5, 4, 10, 8, lit, on ? 6 : 2);
                for (int x = cx - 4; x < cx + 5; x += 3) d.Mark(x, 5, Ramps.Ink, 1);
                break;
            default: // 북: 등이 보인다
                d.Rect(cx - 6, 3, 12, 10, Ramps.Hazard, 3);
                d.Part = 3; d.Rect(cx - 3, 5, 6, 5, Ramps.Hazard, 2);
                d.Mark(cx, 7, level > 2 ? Ramps.Led : level > 0 ? Ramps.Hazard : Ramps.Alarm, on ? 3 : 1);
                break;
        }
    }

    /// <summary>고장: 금 · 그을음 자국.</summary>
    private static void Broken(Dot d)
    {
        int cx = d.W / 2;
        d.Line(cx - 4, 7, cx - 1, 11, Ramps.Ink, 0, mark: true);
        d.Line(cx - 1, 11, cx - 3, 13, Ramps.Ink, 0, mark: true);
        d.Line(cx - 3, 13, cx + 3, 17, Ramps.Ink, 0, mark: true);
        for (int k = 0; k < 6; k++) d.Tone(cx + 2 + (k % 3), 8 + k / 2, -2);
    }
}
