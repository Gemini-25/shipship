using System;
using ShipSim.Core;

namespace ShipSim.Art;

/// <summary>
/// 도트 개인 물건 22가지 (12 × 12): 책(펼침 · 덮음) · 악기 · 체스판 · 카드 · 스케치북 · 모형 · 뜨개 · 일기장 · 사진기 · 퍼즐 · 게임기 ·
/// 다기 · 수집 상자 · 화분 · 아령 · 요가 매트 · 헤드폰 · 머그 · 공구함 · 사진 · 담요 · 그림. owner: 주인 색표 (표지 · 손잡이 · 무늬).
/// </summary>
public static class DotItems
{
    public const int S = 12;

    public static Img Belonging(BelongingKind k, int owner, bool open, bool broken)
    {
        var d = new Dot(S, S);
        switch (k)
        {
            case BelongingKind.Book:
            case BelongingKind.Journal:
            case BelongingKind.Sketchbook:
                if (open)
                {
                    d.Part = 1; d.Rect(1, 3, 5, 7, Ramps.Bandage, 3); d.Part = 2; d.Rect(6, 3, 5, 7, Ramps.Bandage, 2);
                    for (int y = 5; y < 9; y += 2) { d.MarkRect(2, y, 3, 1, Ramps.Ink, 2); d.MarkRect(7, y, 3, 1, Ramps.Ink, 2); }
                    if (k == BelongingKind.Sketchbook) { d.Mark(8, 5, owner, 3); d.Mark(9, 6, owner, 3); d.Mark(7, 7, owner, 3); }
                    d.MarkRect(5, 3, 2, 7, owner, 2);
                }
                else
                {
                    d.Part = 1; d.Rect(3, 2, 6, 8, owner, 2);
                    d.MarkRect(3, 2, 1, 8, owner, 1);
                    if (k == BelongingKind.Journal) d.MarkRect(7, 2, 1, 8, Ramps.Ink, 1);
                    if (k == BelongingKind.Sketchbook) for (int y = 3; y < 10; y += 2) d.Mark(3, y, Ramps.Steel, 4);
                    d.Part = 2; d.Rect(9, 3, 1, 7, Ramps.Bandage, 3);
                }
                break;
            case BelongingKind.Instrument:
                d.Part = 1; d.Oval(4, 8, 3.2f, 3, Ramps.Wood, 3); d.Oval(6, 6, 2.2f, 2, Ramps.Wood, 3);
                d.Mark(5, 7, Ramps.Ink, 0);
                d.Part = 2; d.Line(6, 6, 10, 1, Ramps.Wood, 1); d.Rect(10, 0, 2, 2, Ramps.Wood, 1);
                break;
            case BelongingKind.ChessSet:
            case BelongingKind.Puzzle:
                d.Part = 1; d.Rect(1, 3, 10, 7, Ramps.Wood, 2);
                for (int y = 4; y < 9; y++) for (int x = 2; x < 10; x++) if (((x >> 1) + (y >> 1) & 1) == 0) d.Mark(x, y, k == BelongingKind.Puzzle ? owner : Ramps.Bandage, k == BelongingKind.Puzzle ? 3 : 2);
                if (k == BelongingKind.ChessSet) { d.Part = 2; d.Rect(3, 1, 2, 4, Ramps.Bandage, 3); d.Rect(7, 2, 2, 3, Ramps.Ink, 2); }
                else { d.Part = 2; d.Rect(8, 0, 3, 3, owner, 3); }
                break;
            case BelongingKind.Cards:
                d.Part = 1; d.Rect(1, 3, 6, 8, Ramps.Bandage, 3); d.Mark(2, 4, Ramps.Alarm, 3);
                d.Part = 2; d.Rect(5, 1, 6, 8, Ramps.ClothRed, 3); d.MarkRect(6, 2, 4, 6, Ramps.ClothRed, 2); d.Mark(8, 4, Ramps.Bandage, 3);
                break;
            case BelongingKind.ModelKit:
                d.Part = 1; d.Rect(5, 1, 3, 9, Ramps.ClothWhite, 3); d.Rect(6, 0, 1, 1, Ramps.ClothWhite, 3);
                d.Part = 2; d.Rect(3, 7, 2, 3, owner, 3); d.Rect(8, 7, 2, 3, owner, 3); d.Mark(6, 4, Ramps.Glass, 4);
                d.Part = 3; d.Rect(5, 10, 3, 1, Ramps.Fire, 4);
                break;
            case BelongingKind.Knitting:
                d.Part = 1; d.Oval(5, 7, 4, 3.5f, owner, 3);
                d.Line(2, 6, 8, 8, owner, 2, mark: true); d.Line(3, 9, 8, 5, owner, 2, mark: true);
                d.Part = 2; d.Line(7, 1, 11, 9, Ramps.Wood, 4); d.Line(9, 1, 10, 9, Ramps.Wood, 3);
                break;
            case BelongingKind.Camera:
                d.Part = 1; d.Rect(1, 4, 10, 6, Ramps.Rubber, 2); d.Rect(2, 3, 3, 1, Ramps.Rubber, 3);
                d.Part = 2; d.Oval(6, 7, 2.6f, 2.6f, Ramps.Steel, 4); d.Mark(6, 7, Ramps.Glass, 3); d.Mark(9, 5, Ramps.Alarm, 3);
                break;
            case BelongingKind.GameDevice:
                d.Part = 1; d.Rect(1, 3, 10, 6, owner, 2);
                d.Part = 2; d.Rect(4, 4, 4, 4, Ramps.Glass, 2); d.Mark(5, 5, Ramps.Led, 4);
                d.Mark(2, 6, Ramps.Ink, 1); d.Mark(9, 5, Ramps.Alarm, 3); d.Mark(9, 7, Ramps.Hazard, 3);
                break;
            case BelongingKind.TeaSet:
                d.Part = 1; d.Oval(4, 7, 3.5f, 3, Ramps.ClothWhite, 3); d.Rect(7, 5, 2, 1, Ramps.ClothWhite, 3); d.Rect(3, 3, 3, 1, Ramps.ClothWhite, 4);
                d.MarkRect(2, 7, 5, 1, owner, 3);
                d.Part = 2; d.Rect(8, 8, 3, 3, Ramps.ClothWhite, 3); d.Mark(9, 8, Ramps.Wood, 2);
                break;
            case BelongingKind.CollectionBox:
                d.Part = 1; d.Rect(1, 4, 10, 7, Ramps.Wood, 2);
                d.Part = 2; d.Rect(1, 2, 10, 3, Ramps.Wood, 3);
                d.MarkRect(2, 6, 2, 2, owner, 3); d.MarkRect(5, 6, 2, 2, Ramps.Glass, 4); d.MarkRect(8, 6, 2, 2, Ramps.Hazard, 3);
                break;
            case BelongingKind.PlantPot:
                d.Part = 1; d.Rect(3, 7, 6, 4, Ramps.Wood, 2);
                d.Part = 2; d.Oval(6, 4, 3.5f, 3, Ramps.Leaf, 3); d.Mark(4, 3, Ramps.Leaf, 4); d.Mark(8, 5, Ramps.Leaf, 2);
                break;
            case BelongingKind.Dumbbells:
                for (int i = 0; i < 2; i++)
                {
                    int y = 3 + i * 5;
                    d.Part = (byte)(1 + i); d.Rect(3, y + 1, 6, 1, Ramps.Steel, 4); d.Rect(1, y, 2, 3, Ramps.Rubber, 3); d.Rect(9, y, 2, 3, Ramps.Rubber, 3);
                }
                break;
            case BelongingKind.YogaMat:
                d.Part = 1; d.Rect(1, 4, 10, 5, owner, 3);
                d.Part = 2; d.Oval(10, 6.5f, 1.5f, 2.5f, owner, 2); d.Mark(10, 6, owner, 1);
                break;
            case BelongingKind.Headphones:
                d.Part = 1; d.Line(2, 7, 2, 4, Ramps.Rubber, 3); d.Line(2, 4, 4, 2, Ramps.Rubber, 3); d.Line(4, 2, 8, 2, Ramps.Rubber, 3); d.Line(8, 2, 10, 4, Ramps.Rubber, 3); d.Line(10, 4, 10, 7, Ramps.Rubber, 3);
                d.Part = 2; d.Rect(1, 6, 3, 4, owner, 2); d.Rect(9, 6, 3, 4, owner, 2);
                break;
            case BelongingKind.Mug:
                d.Part = 1; d.Rect(3, 3, 6, 7, owner, 3);
                d.Part = 2; d.Rect(9, 5, 2, 3, owner, 2);
                d.Part = 3; d.Oval(6, 3, 3, 1.2f, Ramps.Wood, 1);
                break;
            case BelongingKind.Toolset:
                d.Part = 1; d.Rect(1, 5, 10, 6, Ramps.ClothOrange, 3);
                d.Part = 2; d.Rect(3, 2, 6, 1, Ramps.Rubber, 3); d.Rect(3, 3, 1, 2, Ramps.Rubber, 3); d.Rect(8, 3, 1, 2, Ramps.Rubber, 3);
                d.MarkRect(1, 7, 10, 1, Ramps.ClothOrange, 1); d.Mark(6, 8, Ramps.Steel, 4);
                break;
            case BelongingKind.Photo:
            case BelongingKind.Artwork:
                d.Part = 1; d.Rect(1, 1, 10, 9, k == BelongingKind.Artwork ? Ramps.Hazard : Ramps.Wood, k == BelongingKind.Artwork ? 2 : 3);
                d.Part = 2; d.Rect(2, 2, 8, 7, k == BelongingKind.Artwork ? owner : Ramps.Cloud, 3);
                if (k == BelongingKind.Photo) { d.Mark(4, 5, Ramps.Skin[2], 3); d.Mark(4, 4, Ramps.HairDark, 2); d.Mark(7, 5, Ramps.Skin[1], 3); d.Mark(7, 4, Ramps.HairBlack, 2); d.MarkRect(2, 7, 8, 2, Ramps.Leaf, 2); }
                else { d.Mark(4, 4, Ramps.Hazard, 4); d.MarkRect(2, 6, 8, 3, owner, 1); }
                break;
            case BelongingKind.Blanket:
                d.Part = 1; d.Rect(1, 4, 10, 6, owner, 2);
                d.Part = 2; d.Rect(1, 3, 10, 2, owner, 3);
                for (int x = 2; x < 11; x += 3) d.Mark(x, 7, owner, 3);
                break;
            default:
                d.Part = 1; d.Oval(6, 6, 3.5f, 3.5f, owner, 3);
                break;
        }
        d.Bevel();
        if (broken) { d.Line(2, 2, 9, 9, Ramps.Ink, 0, mark: true); d.Line(3, 2, 10, 9, Ramps.Alarm, 2, mark: true); }
        return d.ToImg();
    }
}
