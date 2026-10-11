using System;

namespace ShipSim.Art;

/// <summary>그릇 갈래.</summary>
public enum DotDish : byte { Pot, Ttuk, Wok, Pan, Steamer, Tray, Cake, Onggi, Glass, Tub, Plate, Bowl }

/// <summary>그릇 상태: 따뜻함 · 끓는 중 · 식음 · 상함 · 탐.</summary>
public enum DishState : byte { Warm, Cooking, Cold, Spoiled, Burnt }

/// <summary>
/// 도트 음식 그릇 (24 × 18, 접시 16 × 12, 공기 12 × 9): 비스듬히 내려다본 테(타원) + 몸통 + 안의 음식.
/// 국 냄비(손잡이 · 건더기 · 면발 · 들썩이는 뚜껑) · 뚝배기(흙 테 · 붉은 거품) · 웍(밥알) · 프라이팬(전) · 찜기(대나무 결 · 만두) ·
/// 오븐 판(칼집 낸 빵) · 케이크(조각 선) · 옹기(유약 빛 · 뚜껑) · 유리병(절임 조각) · 보관 통(파란 뚜껑 · 상하면 부풂) · 접시 · 공기.
/// </summary>
public static class DotFood
{
    public const int W = 24, H = 18;

    /// <summary>fill 0~4: 남은 양 (접시 0 = 다 먹음) · food: 음식 색표 · frame 0~3.</summary>
    public static Img Dish(DotDish k, int food, DishState st, int frame, int fill, int seed)
    {
        bool cook = st == DishState.Cooking, spoiled = st == DishState.Spoiled, burnt = st == DishState.Burnt;
        int fs = st == DishState.Cold ? 1 : 2; // 식으면 탁하다
        if (spoiled) food = Spoil;
        var d = k switch { DotDish.Plate => new Dot(16, 12), DotDish.Bowl => new Dot(12, 9), _ => new Dot(W, H) };
        int cx = d.W / 2;
        switch (k)
        {
            case DotDish.Pot:
                d.Part = 1; d.Rect(cx - 2 - 8, 7, 1 + 3, 2, Ramps.Steel, 3); d.Rect(cx + 7, 7, 4, 2, Ramps.Steel, 3); // 손잡이
                d.Part = 2; d.Rect(cx - 8, 6, 16, 10, Ramps.Steel, 3);
                d.Part = 3; d.Oval(cx, 6, 8, 3, Ramps.Steel, 4);
                d.Part = 4; d.Oval(cx, 6, 6.5f, 2.2f, burnt ? Ramps.Char : food, burnt ? 0 : fs);
                if (!burnt)
                    for (int i = 0; i < 4; i++) d.Mark(cx - 4 + i * 3, 5 + (i & 1) + ((frame + i) & 1) * (cook ? 1 : 0) - (cook ? 1 : 0), food, fs + 1);
                if (cook)
                {
                    d.Part = 5; int up = frame & 1; // 뚜껑이 반쯤 걸쳐 들썩인다
                    d.Oval(cx + 3, 4 - up, 6, 2.5f, Ramps.Steel, 4); d.Rect(cx + 2, 1 - up, 2, 2, Ramps.Rubber, 3);
                }
                break;
            case DotDish.Ttuk:
                d.Part = 1; d.Rect(cx - 9, 13, 18, 3, Ramps.Wood, 1); // 받침
                d.Part = 2; d.Rect(cx - 8, 6, 16, 8, Ramps.Soil, 2);
                d.Part = 3; d.Oval(cx, 6, 8, 3.2f, Ramps.Soil, 3);
                d.Part = 4; d.Oval(cx, 6, 5.5f, 2, burnt ? Ramps.Char : food, burnt ? 0 : fs);
                if (cook) for (int i = 0; i < 3; i++) d.Mark(cx - 3 + i * 3, 5 + ((frame + i) % 3 == 0 ? 0 : 1), food, 3);
                break;
            case DotDish.Wok:
                d.Part = 1; d.Line(cx + 8, 8, cx + 11, 6, Ramps.Rubber, 2); d.Rect(cx + 10, 5, 2, 2, Ramps.Wood, 2); // 자루
                d.Part = 2; d.Oval(cx, 9, 9, 4.5f, Ramps.Rubber, 2);
                d.Part = 3; d.Oval(cx, 8.5f, 7, 3.2f, burnt ? Ramps.Char : food, burnt ? 0 : fs);
                if (!burnt) for (int i = 0; i < 6; i++) d.Mark(cx - 5 + (int)(DotHash.F(seed, i) * 10), 7 + (int)(DotHash.F(seed, i + 9) * 3) - (cook && ((frame + i) & 3) == 0 ? 2 : 0), Ramps.Bandage, 3, cook);
                break;
            case DotDish.Pan:
                d.Part = 1; d.Rect(cx + 7, 8, 5, 2, Ramps.Rubber, 3);
                d.Part = 2; d.Oval(cx - 1, 9, 8, 4.5f, Ramps.Rubber, 1);
                d.Part = 3; d.Oval(cx - 1, 8.5f, 6, 3, burnt ? Ramps.Char : food, burnt ? 0 : fs + 1);
                d.Mark(cx - 3, 8, food, 4); // 노릇한 빛
                if (cook) d.Mark(cx - 6 + frame * 3, 6, Ramps.Bandage, 3, true); // 기름 튐
                break;
            case DotDish.Steamer:
                d.Part = 2; d.Rect(cx - 8, 7, 16, 9, Ramps.Wood, 3);
                for (int y = 8; y < 16; y += 2) d.MarkRect(cx - 8, y, 16, 1, Ramps.Wood, 2);
                d.Part = 3; d.Oval(cx, 7, 8, 3, Ramps.Wood, 4);
                if (cook) { d.Part = 4; d.Oval(cx, 6 - (frame & 1), 7.5f, 2.6f, Ramps.Wood, 3); for (int x = cx - 6; x < cx + 7; x += 3) d.Mark(x, 6 - (frame & 1), Ramps.Wood, 2); }
                else
                {
                    d.Part = 4;
                    for (int i = 0; i < 3; i++) { int x = cx - 5 + i * 5; d.Oval(x, 7, 2, 1.4f, food, 3); d.Mark(x, 6, food, 4); }
                }
                break;
            case DotDish.Tray:
                d.Part = 1; d.Rect(cx - 10, 7, 20, 8, Ramps.Steel, 3);
                d.Part = 2; d.Rect(cx - 9, 8, 18, 6, Ramps.Steel, 2);
                d.Part = 3;
                for (int i = 0; i < 2; i++)
                {
                    int x = cx - 5 + i * 9;
                    d.Oval(x, 10, 3.5f, 2.2f, burnt ? Ramps.Char : food, burnt ? 0 : fs + 1);
                    for (int j = -1; j <= 1; j++) d.Mark(x + j * 2, 9 + (j & 1), food, fs);
                }
                break;
            case DotDish.Cake:
                d.Part = 1; d.Oval(cx, 12, 9, 3.5f, Ramps.Steel, 4);
                d.Part = 2; d.Rect(cx - 7, 6, 14, 6, food, fs);
                d.Part = 3; d.Oval(cx, 6, 7, 2.6f, food, fs + 1);
                d.Line(cx, 4, cx, 8, food, fs - 1, mark: true); d.Line(cx - 5, 5, cx + 5, 7, food, fs - 1, mark: true);
                d.Mark(cx - 3, 5, Ramps.ClothRed, 3); d.Mark(cx + 3, 6, Ramps.ClothRed, 3); // 장식
                break;
            case DotDish.Onggi:
            {
                d.Part = 1;
                int[] half = { 3, 4, 5, 6, 6, 7, 7, 7, 7, 6, 6, 5, 4 };
                d.Rows(cx, 4, half, OnggiRamp, 2);
                d.Part = 2; d.Rect(cx - 4, 2, 8, 3, OnggiRamp, 1); // 뚜껑
                d.Part = 3; d.Rect(cx - 2, 1, 4, 1, OnggiRamp, 3);
                d.MarkRect(cx - 7, 11, 14, 1, OnggiRamp, 1); // 띠
                d.Mark(cx - 4, 7, OnggiRamp, 4); d.Mark(cx - 4, 8, OnggiRamp, 4); d.Mark(cx - 3, 6, OnggiRamp, 4); // 유약 빛
                if (fill > 0) d.MarkRect(cx - 3, 4, 6, 1, food, 2 + Math.Min(1, fill / 3));
                break;
            }
            case DotDish.Glass:
            {
                d.Part = 1; d.Rect(cx - 5, 4, 10, 12, Ramps.Glass, 3);
                d.Part = 2; d.Rect(cx - 5, 2, 10, 3, Ramps.Steel, 4);
                int top = 15 - fill * 2;
                for (int y = Math.Max(5, top); y < 15; y++)
                    for (int x = cx - 4; x < cx + 4; x++)
                        if (((x * 7 + y * 3 + seed) % 5) < 3) d.Mark(x, y, food, 2 + ((x + y) & 1));
                d.MarkRect(cx - 4, 5, 1, 9, Ramps.Glass, 4); // 반사
                break;
            }
            case DotDish.Tub:
            {
                int b = spoiled ? 1 : 0; // 상하면 부푼다
                d.Part = 1; d.Rect(cx - 7 - b, 7 - b, 14 + 2 * b, 9 + b, Ramps.ClothWhite, 3);
                d.MarkRect(cx - 6, 11, 12, 4, food, 2);
                d.Part = 2; d.Rect(cx - 8 - b, 5 - b, 16 + 2 * b, 3, Ramps.ClothBlue, 3);
                d.Part = 3; d.Rect(cx - 7, 7, 2, 2, Ramps.ClothBlue, 2); d.Rect(cx + 5, 7, 2, 2, Ramps.ClothBlue, 2); // 걸쇠
                break;
            }
            case DotDish.Plate:
                d.Part = 1; d.Oval(cx, 6, 7.5f, 4.5f, Ramps.Bandage, 3);
                d.Part = 2; d.Oval(cx, 6, 5.5f, 3.2f, Ramps.Bandage, 2);
                if (fill > 0) { d.Part = 3; d.Oval(cx, 6, 4, 2.2f, food, fs); d.Mark(cx - 1, 5, food, fs + 1); }
                else for (int i = 0; i < 3; i++) d.Mark(cx - 3 + i * 3, 5 + (i & 1) * 2, food, 1); // 부스러기
                break;
            case DotDish.Bowl:
                d.Part = 1; d.Rect(cx - 4, 3, 8, 5, Ramps.Bandage, 3);
                d.Part = 2; d.Oval(cx, 3, 5, 2, Ramps.Bandage, 3);
                d.Part = 3; d.Oval(cx, 3, 3.6f, 1.3f, food, fs);
                break;
        }
        d.Bevel();
        if (spoiled) for (int i = 0; i < 4; i++) d.Mark(cx - 5 + (int)(DotHash.F(seed, i + 40) * 10), 4 + (int)(DotHash.F(seed, i + 50) * 4), Ramps.Leaf, 4); // 곰팡이 점
        if (burnt) for (int i = 0; i < 8; i++) d.Tone(cx - 8 + (int)(DotHash.F(seed, i) * 16), 6 + (int)(DotHash.F(seed, i + 7) * 9), -2);
        return d.ToImg();
    }

    /// <summary>상한 음식 색 (탁한 올리브).</summary>
    public static readonly int Spoil = Ramps.Add("#1e2410", "#3d4a1e", "#6f7d3a", "#97a65a", "#c2cf8a");
    private static readonly int OnggiRamp = Ramps.Add("#1d0f08", "#3e2214", "#5e3620", "#7f4d2f", "#b07a52");
}
