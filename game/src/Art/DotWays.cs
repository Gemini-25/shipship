using System;
using ShipSim.Core;

namespace ShipSim.Art;

/// <summary>
/// 도트 "이게 돼?" 해법 물건 (v16.25 · 32 × 32 · 칸 가운데): 기준 방향은 막을 곳(벽 · 문 · 구멍)이 아래쪽.
/// 다른 쪽은 그릴 때 돌린다 (Oriented). weak: 닳음 · 실패 (처진 매트리스 · 녹는 얼음 · 불씨), frame 0~1: 깜빡이는 것.
/// </summary>
public static class DotWays
{
    public const int S = 32;

    /// <summary>방향을 따라 돌리는 것 (막는 것 · 문 · 구멍 쪽).</summary>
    public static bool Oriented(WayLook k) => k is WayLook.Seal or WayLook.Burnt or WayLook.Eject or WayLook.MattressPlug or WayLook.TablePlug or WayLook.PotPlug
        or WayLook.CratePlug or WayLook.MatPlug or WayLook.FrostPlug or WayLook.BackPlug or WayLook.RobotBrace or WayLook.CrawlHatch or WayLook.PriedDoor
        or WayLook.CutDoor or WayLook.WallHole or WayLook.BlownDoor or WayLook.Jumper or WayLook.Strap or WayLook.Wedge or WayLook.TapeHose or WayLook.Towel
        or WayLook.WetCloth or WayLook.FanDoor or WayLook.HydroHose or WayLook.GluePatch;

    /// <summary>dir 0 남(기본) · 1 동 · 2 북 · 3 서.</summary>
    public static Img Way(WayLook k, bool weak, int frame, int dir)
    {
        if (k == WayLook.None) return new Img(1, 1);
        var d = Draw(k, weak, frame);
        if (Oriented(k))
            for (int i = 0; i < (dir switch { 1 => 3, 2 => 2, 3 => 1, _ => 0 }); i++) d = d.Rotate(true);
        return d.ToImg(outline: k is not (WayLook.Douse or WayLook.HydroHose or WayLook.Bucket or WayLook.Burnt or WayLook.Shavings or WayLook.Sawdust or WayLook.Condense));
    }

    /// <summary>매트리스를 떼어 간 침대: 맨 나무 틀과 살.</summary>
    public static Img BareBed(int w, int h)
    {
        w = Math.Clamp(w, 8, 96); h = Math.Clamp(h, 8, 96);
        var d = new Dot(w, h);
        bool wide = w > h;
        d.Part = 1; d.Rect(0, 0, w, h, Ramps.Ink, 0);
        d.Part = 2;
        int n = (wide ? w : h) / 6;
        for (int i = 1; i < n; i++)
        {
            int t = (wide ? w : h) * i / n;
            if (wide) d.Rect(t - 1, 1, 2, h - 2, Ramps.Wood, 3); else d.Rect(1, t - 1, w - 2, 2, Ramps.Wood, 3);
        }
        d.Part = 3;
        d.Rect(0, 0, w, 2, Ramps.Wood, 2); d.Rect(0, h - 2, w, 2, Ramps.Wood, 1);
        d.Rect(0, 0, 2, h, Ramps.Wood, 2); d.Rect(w - 2, 0, 2, h, Ramps.Wood, 1);
        d.Bevel();
        return d.ToImg();
    }

    private static Dot Draw(WayLook k, bool weak, int frame)
    {
        var d = new Dot(S, S);
        const int c = 16;
        void Puddle(int ramp, int shade)
        {
            for (int y = 8; y < 26; y++) for (int x = 3; x < 29; x++)
            {
                float dx = (x - 16f) / 13f, dy = (y - 17f) / 8f;
                if (dx * dx + dy * dy < 1f) d.Mark(x, y, ramp, ((x + y + frame) % 9 == 0) ? shade + 2 : shade, true);
            }
        }
        switch (k)
        {
            case WayLook.Seal: // 문틈에 쑤셔 넣은 젖은 수건 + 분필 ×
                d.Part = 1; for (int i = -2; i <= 2; i++) d.Oval(c + i * 5, 26, 3, 3, Ramps.ClothTeal, 2);
                d.Part = 2; d.Rect(3, 23, 26, 1, Ramps.ClothTeal, 4);
                d.Line(10, 8, 20, 18, Ramps.Bandage, 3, mark: true); d.Line(20, 8, 10, 18, Ramps.Bandage, 3, mark: true);
                d.Mark(9 + frame * 7, 30, Ramps.Glass, 4, true);
                break;
            case WayLook.Burnt: // 문틈 그을음 부채꼴 + 자물쇠
                for (int y = 4; y < 28; y++) { int hw = (28 - y) / 2 + 2; for (int x = c - hw; x <= c + hw; x++) if (((x + y) % 3) != 0) d.Mark(x, y, Ramps.Char, (x + y) % 2, true); }
                d.Part = 1; d.Rect(12, 24, 8, 6, Ramps.Hazard, 2); d.Line(13, 24, 13, 21, Ramps.Hazard, 3); d.Line(13, 21, 18, 21, Ramps.Hazard, 3); d.Line(18, 21, 18, 24, Ramps.Hazard, 3);
                break;
            case WayLook.Douse: Puddle(Ramps.Glass, 2); break;
            case WayLook.HydroHose:
                Puddle(Ramps.Leaf, 2);
                d.Part = 1; d.Line(16, 16, 20, 24, Ramps.ClothGreen, 2); d.Line(20, 24, 14, 30, Ramps.ClothGreen, 2); d.Line(17, 16, 21, 24, Ramps.ClothGreen, 3);
                d.Part = 2; d.Rect(14, 14, 4, 4, Ramps.Steel, 4);
                break;
            case WayLook.Bucket:
                Puddle(Ramps.Glass, 2);
                d.Part = 1; d.Rect(20, 18, 9, 7, Ramps.ClothRed, 3); d.Part = 2; d.Oval(20, 21.5f, 2, 3.5f, Ramps.ClothRed, 1);
                break;
            case WayLook.Smother: // 그을린 담요 더미 (실패면 불씨)
                d.Part = 1; d.Oval(c, 17, 12, 9, Ramps.ClothBrown, 2);
                d.Part = 2; d.Oval(c - 3, 15, 6, 4, Ramps.ClothBrown, 3);
                for (int i = 0; i < 3; i++) d.Line(8 + i * 7, 12, 11 + i * 7, 22, Ramps.Char, 1, mark: true);
                if (weak) for (int i = 0; i < 5; i++) d.Mark(8 + i * 4, 13 + (i * 3) % 8, Ramps.Fire, 4 + frame, true);
                break;
            case WayLook.PotLid:
                d.Part = 1; d.Oval(c, 16, 10, 8, Ramps.Steel, 3);
                d.Part = 2; d.Oval(c, 16, 2.5f, 2, Ramps.Rubber, 2);
                if (frame == 1) for (int x = 6; x < 27; x += 3) d.Mark(x, 25, Ramps.Fire, 4, true);
                break;
            case WayLook.Eject: // 끌린 그을음 자국 + 탄 덩어리
                for (int i = 0; i < 4; i++) d.MarkRect(15, 18 + i * 3, 3, 2, Ramps.Char, 1, true);
                d.Part = 1; d.Oval(c, 12, 7, 5, Ramps.Char, 2);
                for (int i = 0; i < 3; i++) d.Mark(13 + i * 3, 11 + (i & 1), Ramps.Fire, 4 + frame, true);
                break;
            case WayLook.MattressPlug: // 구멍에 빨려 든 줄무늬 매트리스 (닳으면 처진다)
            {
                int sag = weak ? 3 : 0;
                d.Part = 1; d.Rect(3, 18 + sag, 26, 11 - sag, Ramps.Bandage, 3);
                for (int x = 5; x < 28; x += 5) d.MarkRect(x, 18 + sag, 2, 11 - sag, Ramps.ClothBlue, 3);
                d.MarkRect(8, 18 + sag, 1, 11 - sag, Ramps.Ink, 1); d.MarkRect(23, 18 + sag, 1, 11 - sag, Ramps.Ink, 1);
                if (weak) for (int i = 0; i < 3; i++) d.Mark(8 + i * 8, 15 - frame, Ramps.Cloud, 4, true);
                break;
            }
            case WayLook.TablePlug: // 세운 식탁 상판 + 다리 + 쐐기
                d.Part = 1; d.Rect(3, 22, 26, 7, Ramps.Wood, 3);
                d.Part = 2; d.Rect(6, 14, 3, 8, Ramps.Wood, 2); d.Rect(23, 14, 3, 8, Ramps.Wood, 2);
                d.Part = 3; d.Rect(14, 18, 4, 4, Ramps.Rubber, 2);
                break;
            case WayLook.PotPlug: // 냄비로 막고 서리 고리
                d.Part = 1; d.Oval(c, 24, 9, 6, Ramps.Steel, 3);
                d.Part = 2; d.Rect(24, 22, 6, 2, Ramps.Rubber, 2);
                for (int x = 6; x < 27; x += 2) d.Mark(x, 30, Ramps.Cloud, 4, true);
                break;
            case WayLook.CratePlug:
                d.Part = 1; d.Rect(5, 19, 22, 11, Ramps.Steel, 3);
                for (int i = 0; i < 3; i++) d.MarkRect(8 + i * 6, 23, 4, 2, Ramps.Panel, 1);
                break;
            case WayLook.MatPlug:
                d.Part = 1; d.Rect(4, 20, 24, 9, Ramps.Rubber, 2);
                for (int x = 6; x < 27; x += 3) d.MarkRect(x, 21, 1, 7, Ramps.Rubber, 3);
                break;
            case WayLook.GluePatch:
                d.Part = 1; d.Oval(c, 25, 9, 5, Ramps.Steel, 4);
                d.Part = 2; d.Oval(c, 25, 6, 3, Ramps.SteelWarm, 3);
                d.Mark(12, 23, Ramps.Bandage, 3); d.Mark(20, 27, Ramps.Hazard, 4);
                break;
            case WayLook.FrostPlug: // 뾰족뾰족 얼음 마개 (녹으면 작아진다)
            {
                int sz = weak ? 6 : 10;
                d.Part = 1; d.Oval(c, 25, sz, sz * 0.6f, Ramps.Cloud, 4);
                for (int i = -2; i <= 2; i++) d.Line(c + i * 3, 25, c + i * 4, 25 - sz / 2 - (i & 1) * 2, Ramps.Cloud, 4);
                if (weak) d.Mark(c, 30, Ramps.Glass, 4 - frame, true);
                break;
            }
            case WayLook.BackPlug: // 등으로 막은 자리: 넓은 몸 자국 + 어깨 서리
                d.Part = 1; d.Oval(c, 24, 11, 6, Ramps.Smoke, 2);
                d.Mark(7, 22, Ramps.Cloud, 4, true); d.Mark(25, 22, Ramps.Cloud, 4, true); d.Mark(8, 21, Ramps.Cloud, 4, true);
                break;
            case WayLook.RobotBrace: // 몸으로 막은 로봇
                d.Part = 1; d.Rect(8, 16, 16, 12, Ramps.Hazard, 3);
                d.Part = 2; d.Rect(7, 27, 4, 3, Ramps.Rubber, 1); d.Rect(21, 27, 4, 3, Ramps.Rubber, 1);
                d.Part = 3; d.Rect(11, 18, 10, 4, Ramps.Panel, 1); d.Mark(13, 19, Ramps.Cyan, 4, true); d.Mark(18, 19, Ramps.Cyan, 4, true);
                break;
            case WayLook.CrawlHatch: // 떼어 낸 격자 덮개가 기대 서 있다 + 벽 속 어둠
                d.Part = 1; d.Rect(6, 22, 20, 8, Ramps.Ink, 0);
                d.Part = 2; d.Rect(18, 8, 10, 12, Ramps.Steel, 3); for (int y = 10; y < 19; y += 2) d.MarkRect(19, y, 8, 1, Ramps.Panel, 1);
                d.Mark(19, 9, Ramps.Steel, 5); d.Mark(26, 18, Ramps.Steel, 5);
                break;
            case WayLook.PriedDoor: // 비틀려 벌어진 문짝 + 쇠지레
                d.Part = 1; d.Rect(3, 22, 11, 8, Ramps.Steel, 3); d.Rect(18, 23, 11, 7, Ramps.Steel, 3);
                d.Part = 2; d.Line(16, 6, 15, 30, Ramps.ClothRed, 3); d.Line(17, 6, 16, 30, Ramps.ClothRed, 2);
                for (int i = 0; i < 3; i++) d.Mark(12 + i, 20 - i * 2, Ramps.Bandage, 3, true);
                break;
            case WayLook.CutDoor:
            case WayLook.WallHole: // 붉게 달아오른 절단선
                d.Part = 1; d.Rect(8, 18, 16, 12, Ramps.Ink, 0);
                for (int x = 7; x <= 24; x++) { d.Mark(x, 17, Ramps.Fire, 3 + ((x + frame) & 1), true); d.Mark(x, 30, Ramps.Fire, 3, true); }
                for (int y = 17; y <= 30; y++) { d.Mark(7, y, Ramps.Fire, 3, true); d.Mark(24, y, Ramps.Fire, 3 + ((y + frame) & 1), true); }
                d.Mark(12, 31, Ramps.Fire, 4, true);
                break;
            case WayLook.BlownDoor:
                d.Part = 1; d.Rect(4, 24, 24, 6, Ramps.Char, 1);
                d.Part = 2; d.Line(6, 14, 22, 20, Ramps.Steel, 3); d.Line(6, 15, 22, 21, Ramps.Steel, 2);
                break;
            case WayLook.Jumper: // 문 모터에 물린 노란 임시선 + 악어 집게
                d.Part = 1; d.Rect(12, 24, 8, 6, Ramps.Panel, 3);
                d.Part = 2; d.Line(4, 6, 12, 14, Ramps.Hazard, 4); d.Line(12, 14, 14, 24, Ramps.Hazard, 4);
                d.Part = 3; d.Rect(13, 22, 3, 3, Ramps.Alarm, 3);
                if (frame == 1) d.Mark(16, 21, Ramps.Fire, 6, true);
                break;
            case WayLook.Knock: // 두드리는 렌치 + 소리 고리
                d.Part = 1; d.Line(10, 22, 22, 10, Ramps.Steel, 4); d.Rect(20, 8, 4, 4, Ramps.Steel, 4);
                for (int a = 0; a < 12; a++) { float t = a / 12f * MathF.PI; d.Mark(16 + (int)(MathF.Cos(t) * (8 + frame * 4)), 16 - (int)(MathF.Sin(t) * (8 + frame * 4)), Ramps.Bandage, 3, true); }
                break;
            case WayLook.RobotBattery: // 빈 배터리 칸 + 뺀 배터리
                d.Part = 1; d.Rect(6, 10, 14, 12, Ramps.Hazard, 3);
                d.Part = 2; d.Rect(9, 12, 8, 5, Ramps.Ink, 0);
                d.Part = 3; d.Rect(21, 18, 9, 6, Ramps.ClothGreen, 2); d.MarkRect(22, 19, 3, 2, Ramps.Led, 3);
                break;
            case WayLook.Pedal: // 페달 발전기
                d.Part = 1; d.Oval(c, 18, 8, 8, Ramps.Steel, 3); d.Oval(c, 18, 2, 2, Ramps.Rubber, 2);
                d.Part = 2; d.Line(c, 18, c + (frame == 0 ? 6 : -6), 12, Ramps.Rubber, 3);
                d.Part = 3; d.Rect(8, 27, 16, 3, Ramps.Steel, 2);
                break;
            case WayLook.Lamp: // 작업등 (빛 웅덩이는 그릴 때)
                d.Part = 1; d.Line(c, 14, 9, 26, Ramps.Steel, 2); d.Line(c, 14, 23, 26, Ramps.Steel, 2); d.Line(c, 14, c, 27, Ramps.Steel, 3);
                d.Part = 2; d.Rect(11, 7, 10, 7, Ramps.Hazard, 3); d.MarkRect(12, 8, 8, 5, Ramps.Fire, 5 + frame, true);
                break;
            case WayLook.Stretcher:
            case WayLook.Cart:
                d.Part = 1; d.Rect(4, 10, 24, 12, k == WayLook.Cart ? Ramps.Steel : Ramps.Bandage, 3);
                d.Part = 2; d.Rect(2, 12, 2, 8, Ramps.Steel, 4); d.Rect(28, 12, 2, 8, Ramps.Steel, 4);
                if (k == WayLook.Cart) { d.Part = 3; d.Rect(6, 22, 3, 3, Ramps.Rubber, 1); d.Rect(23, 22, 3, 3, Ramps.Rubber, 1); }
                else d.MarkRect(12, 13, 8, 2, Ramps.ClothRed, 3);
                break;
            case WayLook.Bandage:
                d.Part = 1; d.Rect(8, 12, 12, 9, Ramps.Bandage, 3); d.MarkRect(12, 14, 4, 5, Ramps.Alarm, 3); d.MarkRect(10, 16, 8, 1, Ramps.Alarm, 3);
                d.Part = 2; d.Line(20, 18, 28, 16, Ramps.Bandage, 3);
                break;
            case WayLook.Terminal:
                d.Part = 1; d.Rect(8, 10, 16, 12, Ramps.Panel, 2); d.MarkRect(10, 12, 12, 7, Ramps.Glass, 2);
                d.MarkRect(11, 14, 6 + frame * 3, 1, Ramps.Led, 3);
                d.Part = 2; d.Line(22, 20, 28, 26, Ramps.Rubber, 2);
                break;
            case WayLook.Stripped: // 속이 빈 설비: 열린 점검창 + 늘어진 선
                d.Part = 1; d.Rect(6, 8, 20, 16, Ramps.Steel, 2); d.MarkRect(9, 11, 14, 10, Ramps.Ink, 0);
                d.Line(11, 14, 14, 26, Ramps.Alarm, 3, mark: false); d.Line(17, 14, 20, 27, Ramps.Hazard, 3, mark: false);
                for (int x = 9; x < 23; x += 3) d.Mark(x, 21, Ramps.Bandage, 2);
                break;
            case WayLook.Improvised: // 테이프로 감은 임시 부품
                d.Part = 1; d.Rect(9, 12, 14, 8, Ramps.Steel, 3);
                for (int x = 11; x < 22; x += 3) d.MarkRect(x, 12, 2, 8, Ramps.Bandage, 2);
                d.Part = 2; d.Line(8, 24, 20, 18, Ramps.Steel, 4);
                break;
            case WayLook.Shavings:
            case WayLook.Sawdust:
                for (int i = 0; i < 40; i++) d.Mark(6 + (i * 7) % 20, 12 + (i * 11) % 12, k == WayLook.Sawdust ? Ramps.Wood : Ramps.Steel, 3 + (i & 1), true);
                break;
            case WayLook.Candle:
                d.Part = 1; d.Rect(14, 14, 4, 10, Ramps.Bandage, 3); d.Part = 2; d.Oval(c, 25, 5, 2, Ramps.Steel, 3);
                d.Mark(15, 12, Ramps.Fire, 5 + frame, true); d.Mark(15, 11 - frame, Ramps.Fire, 6, true);
                break;
            case WayLook.Splitter:
                d.Part = 1; d.Rect(8, 14, 16, 6, Ramps.Panel, 3);
                for (int i = 0; i < 3; i++) { d.Mark(11 + i * 4, 16, Ramps.Ink, 0); d.Line(11 + i * 4, 20, 8 + i * 6, 28, Ramps.Rubber, 2, mark: false); }
                d.Mark(22, 15, Ramps.Led, 3 + frame);
                break;
            case WayLook.Huddle:
            case WayLook.Blanket: // 둘러 덮은 담요 더미
                d.Part = 1; d.Oval(c, 18, 12, 8, Ramps.ClothRed, 2);
                d.Part = 2; d.Oval(c - 4, 15, 5, 4, Ramps.ClothRed, 3); d.Oval(c + 5, 16, 4, 3, Ramps.ClothRed, 3);
                break;
            case WayLook.RationBox:
            case WayLook.ColdStash:
                d.Part = 1; d.Rect(8, 12, 16, 12, k == WayLook.ColdStash ? Ramps.ClothWhite : Ramps.Wood, 3);
                d.Part = 2; d.Rect(7, 10, 18, 3, k == WayLook.ColdStash ? Ramps.ClothBlue : Ramps.Wood, 2);
                if (k == WayLook.ColdStash) d.Mark(22, 9, Ramps.Cloud, 4, true); else d.MarkRect(12, 16, 8, 2, Ramps.Hazard, 3);
                break;
            case WayLook.Porridge:
            case WayLook.PotWarmer:
                d.Part = 1; d.Rect(9, 14, 14, 10, Ramps.Steel, 3); d.Part = 2; d.Oval(c, 14, 7, 2.5f, Ramps.Bandage, 2);
                if (k == WayLook.PotWarmer) { d.Part = 3; d.Rect(8, 24, 16, 3, Ramps.ClothRed, 1); d.MarkRect(10, 25, 12, 1, Ramps.Fire, 3 + frame, true); }
                break;
            case WayLook.SignalLamp:
            case WayLook.Radio:
                d.Part = 1; d.Rect(10, 14, 12, 10, k == WayLook.Radio ? Ramps.Panel : Ramps.Hazard, 3);
                if (k == WayLook.Radio) { d.Part = 2; d.Line(20, 14, 24, 4, Ramps.Steel, 4); d.MarkRect(12, 17, 4, 4, Ramps.Steel, 2); d.Mark(19, 18, Ramps.Led, 3 + frame, true); }
                else { d.MarkRect(12, 16, 8, 6, Ramps.Fire, frame == 1 ? 6 : 2, true); }
                break;
            case WayLook.Runner: // 뛰어서 전하는 쪽지
                d.Part = 1; d.Rect(11, 11, 10, 8, Ramps.Bandage, 3); d.MarkRect(13, 13, 6, 1, Ramps.Ink, 2); d.MarkRect(13, 15, 4, 1, Ramps.Ink, 2);
                for (int i = 0; i < 3; i++) d.MarkRect(4 + i * 2, 22 + i * 2, 3, 1, Ramps.Bandage, 2, true);
                break;
            case WayLook.Strap:
            case WayLook.Wedge:
                if (k == WayLook.Strap) { d.Part = 1; d.Rect(4, 22, 24, 3, Ramps.ClothOrange, 3); d.Rect(14, 20, 4, 7, Ramps.Steel, 4); }
                else { d.Part = 1; for (int y = 20; y < 30; y++) d.Rect(10, y, 12 - (y - 20), 1, Ramps.Wood, 3); d.Part = 2; d.Line(8, 16, 18, 10, Ramps.Steel, 4); }
                break;
            case WayLook.TapeHose: // 관을 감은 테이프 + 늘어진 호스
                d.Part = 1; d.Rect(2, 14, 28, 5, Ramps.Steel, 3);
                for (int i = -1; i <= 1; i++) d.MarkRect(c + i * 5 - 1, 14, 3, 5, Ramps.Bandage, 3);
                d.Part = 2; d.Line(c, 19, c + 5, 24, Ramps.ClothGreen, 2); d.Line(c + 5, 24, c - 2, 30, Ramps.ClothGreen, 2);
                break;
            case WayLook.Towel:
            case WayLook.WetCloth:
                d.Part = 1; d.Rect(5, 23, 22, 6, k == WayLook.WetCloth ? Ramps.ClothTeal : Ramps.ClothWhite, 2);
                d.MarkRect(5, 23, 22, 1, k == WayLook.WetCloth ? Ramps.ClothTeal : Ramps.ClothWhite, 4);
                d.Mark(10 + frame * 8, 30, Ramps.Glass, 4, true);
                break;
            case WayLook.FanDoor: // 문 앞 선풍기
                d.Part = 1; d.Oval(c, 18, 8, 8, Ramps.Steel, 4); d.Part = 2; d.Oval(c, 18, 6, 6, Ramps.Panel, 1);
                d.Line(c - 5 + frame * 2, 18 - 4, c + 5 - frame * 2, 18 + 4, Ramps.ClothBlue, 3, mark: true);
                d.Part = 3; d.Rect(c - 1, 26, 3, 4, Ramps.Steel, 2);
                break;
            case WayLook.Plunger:
                d.Part = 1; d.Oval(c, 24, 6, 3, Ramps.ClothRed, 2); d.Part = 2; d.Rect(15, 8, 3, 16, Ramps.Wood, 3);
                break;
            case WayLook.OffTag: // 꺼 둔 표시 꼬리표
                d.Part = 1; d.Rect(10, 10, 12, 14, Ramps.Hazard, 3); d.MarkRect(12, 14, 8, 2, Ramps.Ink, 1); d.MarkRect(12, 18, 6, 2, Ramps.Ink, 1);
                d.Part = 2; d.Line(16, 10, 16, 4, Ramps.Rubber, 3);
                break;
            case WayLook.Condense: // 맺힌 물을 받는 통 + 물방울
                d.Part = 1; d.Rect(10, 18, 12, 8, Ramps.ClothTeal, 2); d.MarkRect(11, 19, 10, 2, Ramps.Glass, 3);
                d.Mark(16, 8 + frame * 5, Ramps.Glass, 4, true); d.Mark(14, 12, Ramps.Glass, 3, true);
                break;
            case WayLook.SuitShare: // 우주복 하나를 번갈아
                d.Part = 1; d.Rect(10, 10, 12, 14, Ramps.SuitWhite, 3); d.Part = 2; d.Oval(c, 8, 5, 4, Ramps.SuitWhite, 4); d.Oval(c, 8, 3, 2, Ramps.Glass, 2);
                d.Part = 3; d.Line(22, 16, 28, 22, Ramps.Rubber, 2);
                break;
            default:
                d.Part = 1; d.Rect(12, 12, 8, 8, Ramps.Steel, 3);
                break;
        }
        d.Bevel();
        return d;
    }
}
