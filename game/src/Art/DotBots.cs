using System;

namespace ShipSim.Art;

// 도트 로봇 · 드론 — 로봇은 로봇답게(궤도 · 상자 몸 · 외눈 · 팔), 드론은 드론답게(날개 넷 · 잔상 · 항법등).
//   로봇: 앞(남) · 옆(동, 서는 뒤집기) · 뒤(북) 세 모습 · 궤도/바퀴 4장면.
//   모양: 정비(궤도 · 집게 팔 · 공구함) · 운반(바퀴 넷 · 짐칸) · 재배(초록 · 모종 판 · 물뿌리개) · 방재(빨강 · 거품 통 · 경광등) ·
//         간호(흰 캡슐 · 웃는 화면 · 부드러운 팔) · 들것(긴 침상 · 붉은 십자).
//   상태: 대기 · 일함(용접 불꽃 · 물안개 · 거품) · 고장(찌그러짐 · 떨어진 덮개 · 드러난 전선 · 빨간 눈 · 불꽃) · 꺼짐(눈이 꺼짐).
//   특기 표식(v15 로봇 11): 등 · 앞판의 작은 그림 (편지 · 물방울 · 상자 · 번개 · 손 · 톱니 · 잎 · 물뿌리개 · 눈 · 불꽃 · 더하기).

public enum DotBotKind : byte { Maintainer, Hauler, Gardener, Safety, Nurse, Stretcher }
public enum DotBotState : byte { Ok, Working, Broken, Off }
public enum DotDroneKind : byte { Inspect, Repair, Tow, Build }

/// <summary>로봇 한 대의 겉모습 (시뮬레이션의 종류 · 색 · 상태 · 짐에서 만든다).</summary>
public sealed class BotLook
{
    public DotBotKind Shape;
    public int Paint = Ramps.Hazard;
    public DotBotState State;
    /// <summary>짐 색표 (-1: 빈 짐칸).</summary>
    public int Cargo = -1;
    /// <summary>특기 표식 0 없음 · 1 편지 · 2 물방울 · 3 상자 · 4 번개 · 5 손 · 6 톱니 · 7 잎 · 8 물뿌리개 · 9 눈 · 10 불꽃 · 11 더하기.</summary>
    public int Badge;
    public int Seed;
}

public static class DotBots
{
    public const int RW = 32, RH = 32, Foot = 30, Cx = 16;

    /// <summary>로봇 한 장면 (발 = 30줄, 가운데 = 16).</summary>
    public static Img Robot(BotLook l, DotFacing f, int frame = 0)
    {
        if (f == DotFacing.W) return Flip(Robot(l, DotFacing.E, frame));
        var d = new Dot(RW, RH);
        switch (l.Shape)
        {
            case DotBotKind.Hauler: Hauler(d, l, f, frame); break;
            case DotBotKind.Nurse: Nurse(d, l, f, frame); break;
            case DotBotKind.Stretcher: Stretcher(d, l, f, frame); break;
            default: Tracked(d, l, f, frame); break;
        }
        d.Bevel();
        Details(d, l, f, frame);
        var img = d.ToImg();
        Effects(img, l, f, frame);
        return img;
    }

    /// <summary>예전 시험 장면용 (모양 · 상태만).</summary>
    public static Img Robot(DotBotKind kind, DotBotState state, int frame = 0, int seed = 0) =>
        Robot(new BotLook { Shape = kind, State = state, Seed = seed, Paint = kind switch { DotBotKind.Gardener => Ramps.ClothGreen, DotBotKind.Safety => Ramps.ClothRed, DotBotKind.Nurse => Ramps.ClothWhite, _ => Ramps.Hazard } }, DotFacing.S, frame);

    private static Img Flip(Img src)
    {
        var o = new Img(src.W, src.H);
        for (int y = 0; y < src.H; y++) for (int x = 0; x < src.W; x++) o.Set(src.W - 1 - x, y, src.Get(x, y));
        return o;
    }

    // ───────────────────────────── 궤도 로봇 (정비 · 재배 · 방재) ─────────────────────────────

    private static void Tracked(Dot d, BotLook l, DotFacing f, int frame)
    {
        int paint = l.Paint;
        bool work = l.State == DotBotState.Working;
        if (f == DotFacing.E)
        {
            // 옆: 긴 궤도 하나 · 바퀴 셋 · 상자 몸 · 돔 머리(눈이 오른쪽) · 앞으로 뻗은 팔 · 등의 공구함
            d.Part = 1; d.Rect(3, 22, 24, 8, Ramps.Rubber, 2);
            d.Part = 2; d.Rect(6, 13, 18, 3, paint, 3);
            d.Part = 3; d.Rect(6, 16, 18, 7, paint, 2);
            d.Part = 4; d.Oval(17, 9.5f, 5.5f, 4f, Ramps.Steel, 3);
            d.Part = 5; d.Line(13, 6, 11, 1, Ramps.Steel, 4);
            d.Part = 6;
            if (work) { d.Line(24, 15, 28, 11, Ramps.Steel, 3); d.Line(24, 16, 28, 12, Ramps.Steel, 3); d.Set(29, 10, Ramps.Steel, 4); d.Set(29, 12, Ramps.Steel, 4); }
            else { d.Line(24, 17, 28, 19, Ramps.Steel, 3); d.Line(24, 18, 28, 20, Ramps.Steel, 3); d.Set(29, 19, Ramps.Steel, 4); d.Set(29, 21, Ramps.Steel, 4); }
            d.Part = 7; BackPack(d, l, 1, 12, f);
            return;
        }
        bool back = f == DotFacing.N;
        // 궤도 (왼 · 오)
        d.Part = 1; d.Rect(5, 20, 6, 10, Ramps.Rubber, 2);
        d.Part = 2; d.Rect(21, 20, 6, 10, Ramps.Rubber, 2);
        d.Part = 3; d.Rect(10, 22, 12, 6, Ramps.Steel, 2);
        // 몸통 상자: 윗면(밝게) · 앞면
        d.Part = 4; d.Rect(7, 11, 18, 4, paint, 3);
        d.Part = 5; d.Rect(7, 15, 18, 8, paint, 2);
        // 머리 (센서 돔)
        d.Part = 6; d.Oval(16, 8.5f, 5.5f, 4f, Ramps.Steel, 3);
        d.Part = 7; d.Line(20, 5, 22, 1, Ramps.Steel, 4);
        // 팔 (앞모습은 화면 오른쪽, 뒷모습은 왼쪽)
        int ax = back ? 4 : 27, dir = back ? -1 : 1;
        d.Part = 8; d.Oval(ax - dir * 0.5f, 14.5f, 1.8f, 1.8f, Ramps.Steel, 2);
        d.Part = 9;
        if (work && !back) { d.Line(27, 13, 29, 9, Ramps.Steel, 3); d.Line(28, 13, 30, 9, Ramps.Steel, 3); d.Line(29, 9, 29, 4, Ramps.Steel, 3); }
        else { d.Line(ax, 15, ax + dir, 20, Ramps.Steel, 3); d.Line(ax + dir, 15, ax + 2 * dir, 20, Ramps.Steel, 3); }
        d.Part = 10;
        if (work && !back) { d.Set(28, 3, Ramps.Steel, 4); d.Set(30, 3, Ramps.Steel, 4); d.Set(29, 2, Ramps.Hazard, 4); }
        else { d.Set(ax, 21, Ramps.Steel, 4); d.Set(ax + 2 * dir, 21, Ramps.Steel, 4); d.Set(ax, 22, Ramps.Steel, 3); d.Set(ax + 2 * dir, 22, Ramps.Steel, 3); }
        d.Part = 11; BackPack(d, l, back ? 26 : 1, 13, f);
    }

    /// <summary>등짐: 정비 공구함 · 재배 모종 판 · 방재 거품 통.</summary>
    private static void BackPack(Dot d, BotLook l, int x, int y, DotFacing f)
    {
        switch (l.Shape)
        {
            case DotBotKind.Gardener:
                d.Rect(x, y, 5, 6, Ramps.Wood, 3);
                d.Part++; d.Rect(x, y - 1, 5, 2, Ramps.Soil, 2);
                d.Set(x + 1, y - 2, Ramps.Leaf, 3); d.Set(x + 3, y - 3, Ramps.Leaf, 4); d.Set(x + 2, y - 2, Ramps.Leaf, 2);
                break;
            case DotBotKind.Safety:
                d.Rect(x, y - 2, 5, 9, Ramps.ClothWhite, 3);
                d.Part++; d.Rect(x + 1, y - 3, 3, 1, Ramps.Rubber, 2);
                break;
            default:
                d.Rect(x, y + 2, 5, 5, Ramps.Alarm, 3);
                d.Part++; d.Set(x + 1, y + 1, Ramps.Steel, 3); d.Set(x + 2, y + 1, Ramps.Steel, 3); d.Set(x + 3, y + 1, Ramps.Steel, 3);
                break;
        }
    }

    // ───────────────────────────── 운반 로봇 ─────────────────────────────

    private static void Hauler(Dot d, BotLook l, DotFacing f, int frame)
    {
        int paint = l.Paint;
        if (f == DotFacing.E)
        {
            // 옆: 바퀴 둘 · 긴 차체 · 뒤쪽 센서 탑 · 짐칸
            d.Part = 1; d.Oval(8, 27, 3.2f, 3.2f, Ramps.Rubber, 2); d.Oval(24, 27, 3.2f, 3.2f, Ramps.Rubber, 2);
            d.Part = 2; d.Rect(2, 18, 28, 3, Ramps.Steel, 4);
            d.Part = 3; d.Rect(2, 21, 28, 5, paint, 2);
            d.Part = 4; d.Rect(3, 7, 7, 11, Ramps.Panel, 3);
            d.Part = 5; d.Rect(4, 5, 5, 2, Ramps.Panel, 4);
            if (l.Cargo >= 0) { d.Part = 6; d.Rect(12, 9, 15, 3, l.Cargo, 3); d.Part = 7; d.Rect(12, 12, 15, 6, l.Cargo, 2); }
            return;
        }
        bool back = f == DotFacing.N;
        d.Part = 1; d.Oval(7, 27, 3, 3, Ramps.Rubber, 2); d.Oval(25, 27, 3, 3, Ramps.Rubber, 2);
        d.Part = 2; d.Rect(3, 17, 26, 4, Ramps.Steel, 4);
        d.Part = 3; d.Rect(3, 21, 26, 7, paint, 2);
        d.Part = 4; d.Oval(7, 29, 2.5f, 2.2f, Ramps.Rubber, 3); d.Oval(25, 29, 2.5f, 2.2f, Ramps.Rubber, 3);
        int tx = back ? 21 : 4;
        d.Part = 5; d.Rect(tx, 7, 7, 10, Ramps.Panel, 3);
        d.Part = 6; d.Rect(tx + 1, 5, 5, 2, Ramps.Panel, 4);
        if (l.Cargo >= 0)
        {
            int cx = back ? 5 : 12;
            d.Part = 7; d.Rect(cx, 9, 15, 3, l.Cargo, 3);
            d.Part = 8; d.Rect(cx, 12, 15, 6, l.Cargo, 2);
        }
    }

    // ───────────────────────────── 간호 · 들것 로봇 ─────────────────────────────

    private static void Nurse(Dot d, BotLook l, DotFacing f, int frame)
    {
        bool side = f == DotFacing.E, back = f == DotFacing.N;
        d.Part = 1; d.Oval(16, 27, 9, 3.5f, Ramps.SuitWhite, 1);       // 치마 (바퀴를 덮는다)
        d.Part = 2; d.Oval(16, 18, side ? 7 : 8.5f, 10, Ramps.SuitWhite, 3); // 캡슐 몸
        d.Part = 3; d.Oval(side ? 18 : 16, 8, side ? 5 : 6, 5, Ramps.SuitWhite, 3); // 머리
        if (!back)
        {
            d.Part = 4; d.Rect(side ? 18 : 12, 6, side ? 4 : 8, 4, Ramps.Panel, 1); // 화면 얼굴
        }
        // 부드러운 팔
        d.Part = 5;
        if (side) { d.Line(18, 16, 24, 19, Ramps.SuitWhite, 2); d.Set(25, 19, Ramps.SuitWhite, 3); }
        else { d.Line(8, 15, 5, 21, Ramps.SuitWhite, 2); d.Line(24, 15, 27, 21, Ramps.SuitWhite, 2); }
    }

    private static void Stretcher(Dot d, BotLook l, DotFacing f, int frame)
    {
        bool side = f == DotFacing.E;
        if (side)
        {
            d.Part = 1; d.Oval(6, 28, 2.5f, 2.5f, Ramps.Rubber, 2); d.Oval(26, 28, 2.5f, 2.5f, Ramps.Rubber, 2);
            d.Part = 2; d.Rect(3, 22, 26, 3, Ramps.Steel, 3);
            d.Part = 3; d.Rect(2, 18, 28, 4, Ramps.Bandage, 3);
            d.Part = 4; d.Rect(2, 16, 6, 2, Ramps.Bandage, 4);
            return;
        }
        d.Part = 1; d.Oval(8, 28, 2.5f, 2.5f, Ramps.Rubber, 2); d.Oval(24, 28, 2.5f, 2.5f, Ramps.Rubber, 2);
        d.Part = 2; d.Rect(6, 22, 20, 4, Ramps.Steel, 3);
        d.Part = 3; d.Rect(7, 8, 18, 15, Ramps.Bandage, 3);
        d.Part = 4; d.Rect(9, 9, 14, 3, Ramps.Bandage, 4);
    }

    // ───────────────────────────── 세부 · 효과 ─────────────────────────────

    private static void Details(Dot d, BotLook l, DotFacing f, int frame)
    {
        bool broken = l.State == DotBotState.Broken, off = l.State == DotBotState.Off;
        int eye = broken ? Ramps.Alarm : off ? Ramps.Panel : Ramps.Cyan;
        bool side = f == DotFacing.E, back = f == DotFacing.N;
        switch (l.Shape)
        {
            case DotBotKind.Hauler:
                if (side)
                {
                    for (int x = 3; x < 29; x++) d.Mark(x, 23, Ramps.ClothOrange, 3);
                    d.Mark(29, 22, Ramps.Hazard, 4); d.Mark(2, 22, Ramps.Alarm, 3);
                    d.Mark(8, 27, Ramps.Steel, 4); d.Mark(24, 27, Ramps.Steel, 4);
                    d.MarkRect(4, 9, 5, 3, eye, off ? 1 : 2);
                }
                else
                {
                    for (int x = 4; x < 28; x++) d.Mark(x, 23, Ramps.ClothOrange, 3);
                    if (back) { d.Mark(4, 22, Ramps.Alarm, 4); d.Mark(27, 22, Ramps.Alarm, 4); }
                    else { d.Mark(4, 22, Ramps.Hazard, 4); d.Mark(27, 22, Ramps.Hazard, 4); }
                    d.Mark(7, 27, Ramps.Steel, 4); d.Mark(25, 27, Ramps.Steel, 4);
                    int tx = back ? 21 : 4;
                    if (!back) { d.MarkRect(tx + 1, 9, 5, 3, eye, off ? 1 : 2); d.Mark(tx + 2, 9, eye, off ? 2 : 4); }
                    for (int x = tx + 1; x < tx + 6; x++) d.Mark(x, 13, Ramps.Led, (x + frame) % 3 == 0 && !off ? 3 : 1);
                }
                if (l.Cargo >= 0)
                {
                    int cx = side ? 12 : back ? 5 : 12;
                    for (int x = cx; x < cx + 15; x++) d.Mark(x, 14, l.Cargo, 1);
                    d.Mark(cx + 3, 15, l.Cargo, 1); d.Mark(cx + 10, 16, l.Cargo, 1);
                }
                Badge(d, l.Badge, side ? 18 : 15, side ? 24 : 24);
                return;
            case DotBotKind.Nurse:
                if (!back)
                {
                    // 화면 얼굴: 웃는 눈 · 입 (고장이면 빨간 X)
                    int fx = side ? 19 : 13;
                    if (broken) { d.Mark(fx + 1, 7, Ramps.Alarm, 4); d.Mark(fx + 2, 8, Ramps.Alarm, 4); d.Mark(fx + 3, 7, Ramps.Alarm, 4); d.Mark(fx + 1, 9, Ramps.Alarm, 4); d.Mark(fx + 3, 9, Ramps.Alarm, 4); }
                    else if (!off)
                    {
                        d.Mark(fx + 1, 7, Ramps.Cyan, 4); if (!side) d.Mark(fx + 5, 7, Ramps.Cyan, 4);
                        d.Mark(fx + (side ? 1 : 2), 9, Ramps.Cyan, 3); d.Mark(fx + (side ? 2 : 3), 9, Ramps.Cyan, 3); if (!side) d.Mark(fx + 4, 9, Ramps.Cyan, 3);
                    }
                    // 붉은 십자
                    int cxp = side ? 15 : 16;
                    d.Mark(cxp, 16, Ramps.Alarm, 3); d.Mark(cxp - 1, 17, Ramps.Alarm, 3); d.Mark(cxp, 17, Ramps.Alarm, 4); d.Mark(cxp + 1, 17, Ramps.Alarm, 3); d.Mark(cxp, 18, Ramps.Alarm, 3);
                }
                d.Mark(16, 2, broken ? Ramps.Alarm : Ramps.Led, off ? 0 : (frame & 1) == 0 ? 3 : 1, onEmpty: false);
                return;
            case DotBotKind.Stretcher:
                if (side) { d.Mark(15, 19, Ramps.Alarm, 3); d.Mark(16, 19, Ramps.Alarm, 3); d.Mark(17, 19, Ramps.Alarm, 3); }
                else { d.Mark(16, 14, Ramps.Alarm, 3); d.Mark(15, 15, Ramps.Alarm, 3); d.Mark(16, 15, Ramps.Alarm, 4); d.Mark(17, 15, Ramps.Alarm, 3); d.Mark(16, 16, Ramps.Alarm, 3); }
                return;
        }
        // 궤도 로봇
        if (side)
        {
            d.MarkRect(18, 8, 4, 3, Ramps.Panel, 1);
            d.Mark(20, 9, eye, 3); d.Mark(21, 9, eye, 2); d.Mark(20, 8, eye, off ? 1 : 4);
            for (int x = 7; x < 23; x++) d.Mark(x, 19, l.Paint, 1);
            for (int x = 3; x < 27; x++) { int k = (x + frame) & 1; d.Mark(x, 22, Ramps.Rubber, k == 0 ? 0 : 3); d.Mark(x, 29, Ramps.Rubber, k == 0 ? 3 : 0); }
            foreach (int wx in new[] { 6, 14, 22 }) { d.Mark(wx, 25, Ramps.Steel, 4); d.Mark(wx + 1, 25, Ramps.Steel, 3); d.Mark(wx, 26, Ramps.Steel, 2); d.Mark(wx + 1, 26, Ramps.Steel, 1); }
            d.Mark(11, 1, Ramps.Alarm, (frame & 1) == 0 && !off ? 4 : 1, onEmpty: true);
            Badge(d, l.Badge, 9, 17);
        }
        else
        {
            if (!back)
            {
                d.MarkRect(12, 7, 9, 3, Ramps.Panel, 1);
                d.Mark(15, 8, eye, 2); d.Mark(16, 8, eye, 3); d.Mark(17, 8, eye, 2); d.Mark(16, 7, eye, off ? 1 : 4);
                for (int x = 7; x < 25; x++) d.Mark(x, 21, ((x / 2) & 1) == 0 ? Ramps.Ink : Ramps.Hazard, ((x / 2) & 1) == 0 ? 2 : 3);
                d.Mark(9, 13, Ramps.Led, off ? 0 : 3); d.Mark(11, 13, broken ? Ramps.Alarm : Ramps.Led, off ? 0 : (frame & 1) == 0 ? 3 : 1);
            }
            else
            {
                // 뒤: 전지 칸 · 통풍구
                d.MarkRect(11, 15, 10, 6, Ramps.Panel, 2);
                for (int x = 12; x < 20; x += 2) d.Mark(x, 17, Ramps.Led, off ? 0 : 2);
                for (int x = 12; x < 21; x++) d.Mark(x, 9, Ramps.Steel, 1);
            }
            for (int x = 8; x < 24; x++) d.Mark(x, 18, l.Paint, 1);
            for (int y = 20; y < 30; y++) { int k = (y + frame) & 1; d.Mark(5, y, Ramps.Rubber, k == 0 ? 0 : 3); d.Mark(26, y, Ramps.Rubber, k == 0 ? 0 : 3); }
            foreach (var (wx, wy) in new[] { (8, 23), (8, 27), (23, 23), (23, 27) }) { d.Mark(wx, wy, Ramps.Steel, 4); d.Mark(wx + 1, wy, Ramps.Steel, 3); d.Mark(wx, wy + 1, Ramps.Steel, 2); d.Mark(wx + 1, wy + 1, Ramps.Steel, 1); }
            d.Mark(22, 1, Ramps.Alarm, (frame & 1) == 0 && !off ? 4 : 1, onEmpty: true);
            Badge(d, l.Badge, back ? 13 : 19, 16);
        }
        if (broken)
        {
            // 찌그러짐 · 떨어진 덮개 (속 전선) · 그을음
            int px = side ? 12 : 15;
            d.Mark(9, 12, l.Paint, 0); d.Mark(10, 13, l.Paint, 1); d.Mark(21, 19, l.Paint, 0);
            d.MarkRect(px, 15, 5, 4, Ramps.Panel, 0);
            d.Mark(px + 1, 16, Ramps.Alarm, 3); d.Mark(px + 2, 17, Ramps.Led, 3); d.Mark(px + 3, 16, Ramps.Hazard, 4); d.Mark(px + 1, 17, Ramps.Cyan, 2);
            for (int i = 0; i < 9; i++) { int x = 7 + (int)(DotHash.F(l.Seed, i) * 18), y = 11 + (int)(DotHash.F(l.Seed, i, 1) * 12); d.Mark(x, y, Ramps.Char, 1); }
        }
    }

    /// <summary>특기 표식 (3×3 도트 그림).</summary>
    private static void Badge(Dot d, int badge, int x, int y)
    {
        if (badge <= 0) return;
        int bg = Ramps.Ink;
        for (int j = -1; j < 4; j++) for (int i = -1; i < 4; i++) d.Mark(x + i, y + j, bg, 1);
        (int ramp, string pat) = badge switch
        {
            1 => (Ramps.Bandage, "###" + "#.#" + "###"),   // 편지
            2 => (Ramps.Cyan, ".#." + "###" + ".#."),      // 물방울
            3 => (Ramps.Wood, "###" + "###" + "###"),      // 상자
            4 => (Ramps.Hazard, ".##" + ".#." + "##."),    // 번개
            5 => (Ramps.Skin[1], "#.#" + "###" + ".#."),   // 손
            6 => (Ramps.Steel, "#.#" + ".#." + "#.#"),     // 톱니
            7 => (Ramps.Leaf, ".##" + "##." + "#.."),      // 잎
            8 => (Ramps.Cyan, "##." + ".##" + "..#"),      // 물뿌리개
            9 => (Ramps.Alarm, "###" + "#.#" + "###"),     // 눈
            10 => (Ramps.Fire, ".#." + "###" + "###"),     // 불꽃
            _ => (Ramps.Led, ".#." + "###" + ".#."),       // 더하기
        };
        for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++) if (pat[j * 3 + i] == '#') d.Mark(x + i, y + j, ramp, ramp == Ramps.Fire ? 5 : 3);
    }

    private static void Effects(Img img, BotLook l, DotFacing f, int frame)
    {
        if (l.State == DotBotState.Off) return;
        bool side = f == DotFacing.E, back = f == DotFacing.N;
        if (l.Shape is DotBotKind.Maintainer or DotBotKind.Gardener or DotBotKind.Safety && !back)
            img.Glow(side ? 20.5f : 16.5f, 8.5f, 4f, Ramps.Get(l.State == DotBotState.Broken ? Ramps.Alarm : Ramps.Cyan, 3), 0.35f);
        if (l.Shape == DotBotKind.Safety && l.State != DotBotState.Broken)
        {
            // 경광등: 장면마다 도는 빛
            float a = frame * 1.57f;
            float bx = side ? 17 : 16, by = 4;
            img.Set((int)bx, (int)by, Ramps.Get(Ramps.Hazard, 4));
            for (int i = 2; i < 7; i++) img.Blend((int)(bx + MathF.Cos(a) * i), (int)(by + MathF.Sin(a) * i * 0.5f), Ramps.Get(Ramps.Hazard, 4), 0.6f - i * 0.07f);
        }
        if (l.State == DotBotState.Working && !back)
        {
            float tx = side ? 29.5f : 29.5f, ty = side ? 10.5f : 2.5f;
            switch (l.Shape)
            {
                case DotBotKind.Maintainer:
                    img.Glow(tx, ty, 5f, Ramps.Get(Ramps.Fire, 6), 0.8f);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = DotHash.F(l.Seed + frame, i) * 6.28f, r = 1.5f + DotHash.F(l.Seed + frame, i, 2) * 3.5f;
                        img.Set((int)(tx + MathF.Cos(a) * r), (int)(ty + MathF.Sin(a) * r), Ramps.Get(Ramps.Fire, 5 + (i & 1)));
                    }
                    break;
                case DotBotKind.Gardener:
                    for (int i = 0; i < 7; i++) img.Blend((int)(tx - 1 + DotHash.F(frame, i) * 4), (int)(ty + 1 + DotHash.F(frame, i, 1) * 6), Ramps.Get(Ramps.Cyan, 4), 0.7f);
                    break;
                case DotBotKind.Safety:
                    for (int i = 0; i < 10; i++) img.Blend((int)(tx - 2 + DotHash.F(frame, i) * 5), (int)(ty + 1 + DotHash.F(frame, i, 1) * 8), Ramps.Get(Ramps.Bandage, 3), 0.85f);
                    break;
            }
        }
        if (l.State == DotBotState.Broken)
        {
            var spark = Ramps.Get(Ramps.Fire, 6);
            int sx = side ? 14 : 18;
            img.Set(sx, 14, spark); img.Set(sx + 2, 13, spark); img.Set(sx + 1, 12 - (frame & 1), Ramps.Get(Ramps.Fire, 5));
            if ((frame & 1) == 0) img.Glow(sx + 0.5f, 16.5f, 3.5f, spark, 0.5f);
        }
    }

    // ───────────────────────────── 드론 ─────────────────────────────

    /// <summary>드론 (날개 넷 · 종류마다 다른 몸과 도구). broken: 꺾인 날개 하나 · rotorOff: 거치대에서 멈춤.</summary>
    public static Img Drone(DotDroneKind kind, DotBotState state, int frame, int bodyRamp = -1, bool brokenRotor = false, int badge = 0)
    {
        int body = bodyRamp < 0 ? Ramps.ClothWhite : bodyRamp;
        bool wreck = state == DotBotState.Broken, off = state == DotBotState.Off;
        var d = new Dot(26, 24);
        // 팔 넷 (대각선)
        d.Part = 1;
        d.Line(6, 5, 11, 9, Ramps.Steel, 3); d.Line(19, 5, 14, 9, Ramps.Steel, 3);
        d.Line(6, 16, 11, 12, Ramps.Steel, 2); d.Line(19, 16, 14, 12, Ramps.Steel, 2);
        d.Part = 2;
        foreach (var (x, y) in new[] { (5, 4), (20, 4), (5, 16), (20, 16) }) d.Rect(x - 1, y, 2, 2, Ramps.Steel, 2);
        // 몸
        d.Part = 3;
        switch (kind)
        {
            case DotDroneKind.Inspect: d.Oval(12.5f, 10.5f, 4.5f, 4f, body, 3); break;
            case DotDroneKind.Repair: d.Rect(8, 7, 9, 8, body, 3); break;
            case DotDroneKind.Tow: d.Oval(12.5f, 10.5f, 5.5f, 3.5f, body, 3); break;
            default: d.Rect(8, 8, 9, 6, body, 3); break;
        }
        d.Part = 4; d.Rect(10, 14, 6, 2, Ramps.Panel, 3);
        // 도구
        d.Part = 5;
        switch (kind)
        {
            case DotDroneKind.Inspect: d.Oval(12.5f, 15.5f, 2f, 1.6f, Ramps.Panel, 2); break;                // 아래 큰 렌즈
            case DotDroneKind.Repair: d.Line(10, 16, 9, 19, Ramps.Steel, 3); d.Line(15, 16, 16, 19, Ramps.Steel, 3); d.Set(10, 20, Ramps.Steel, 4); d.Set(15, 20, Ramps.Steel, 4); break;
            case DotDroneKind.Tow: d.Line(12, 16, 12, 20, Ramps.Rubber, 3); d.Set(11, 21, Ramps.Steel, 4); d.Set(13, 21, Ramps.Steel, 4); d.Set(12, 22, Ramps.Steel, 4); break;
            default: d.Rect(11, 16, 3, 3, Ramps.Hazard, 2); d.Set(12, 19, Ramps.Steel, 4); break;
        }
        d.Bevel();
        for (int x = 8; x < 17; x++) d.Mark(x, 9, wreck ? Ramps.Char : Ramps.ClothOrange, wreck ? 1 : 3);
        d.Mark(12, 12, Ramps.Ink, 1); d.Mark(13, 12, wreck ? Ramps.Panel : state == DotBotState.Broken ? Ramps.Alarm : Ramps.Cyan, off ? 1 : 3);
        bool blink = (frame & 1) == 0 && !off && !wreck;
        d.Mark(8, 11, Ramps.Alarm, blink ? 4 : 1); d.Mark(17, 11, Ramps.Led, blink ? 3 : 1);
        if (badge > 0) Badge(d, badge, 18, 18);
        if (wreck) for (int i = 0; i < 8; i++) d.Mark(8 + (int)(DotHash.F(badge, i) * 9), 7 + (int)(DotHash.F(badge, i, 1) * 7), Ramps.Char, 0);
        var img = d.ToImg();
        if (!off && !wreck)
        {
            // 날개 잔상 (반투명 원판 + 한 줄 반짝) — 꺾인 날개는 멈춘 날 하나
            var blur = Ramps.Get(Ramps.Steel, 5);
            int k = 0;
            foreach (var (x, y) in new[] { (5f, 4.5f), (20f, 4.5f), (5f, 16.5f), (20f, 16.5f) })
            {
                if (brokenRotor && k++ == 1) { img.Set((int)x - 2, (int)y, Ramps.Get(Ramps.Steel, 2)); img.Set((int)x + 2, (int)y + 1, Ramps.Get(Ramps.Steel, 2)); continue; }
                img.Oval(x, y, 4.6f, 2.2f, blur, 0.28f);
                int ph = (frame + (int)x) % 3;
                img.Blend((int)x - 3 + ph, (int)y, blur, 0.7f);
                img.Blend((int)x + 2 - ph, (int)y, blur, 0.5f);
            }
            if (blink) { img.Glow(8.5f, 11.5f, 3f, Ramps.Get(Ramps.Alarm, 4), 0.5f); img.Glow(17.5f, 11.5f, 3f, Ramps.Get(Ramps.Led, 3), 0.5f); }
            if (state == DotBotState.Working && kind is DotDroneKind.Repair or DotDroneKind.Build)
            {
                float tx = kind == DotDroneKind.Build ? 12.5f : 12.5f, ty = kind == DotDroneKind.Build ? 20.5f : 21.5f;
                img.Glow(tx, ty, 4f, Ramps.Get(Ramps.Fire, 6), 0.8f);
                for (int i = 0; i < 5; i++) img.Set((int)(tx + (DotHash.F(frame, i) - 0.5f) * 6), (int)(ty + DotHash.F(frame, i, 1) * 3), Ramps.Get(Ramps.Fire, 5 + (i & 1)));
            }
        }
        else
        {
            // 멈춘 날개: 날 하나씩
            foreach (var (x, y) in new[] { (5, 4), (20, 4), (5, 16), (20, 16) })
            {
                img.Set(x - 3, y, Ramps.Get(Ramps.Steel, 3)); img.Set(x - 2, y, Ramps.Get(Ramps.Steel, 4)); img.Set(x + 1, y + 1, Ramps.Get(Ramps.Steel, 4)); img.Set(x + 2, y + 1, Ramps.Get(Ramps.Steel, 3));
            }
        }
        return img;
    }

    /// <summary>예전 시험 장면용.</summary>
    public static Img Drone(DotBotState state, int frame = 0, int bodyRamp = -1) => Drone(DotDroneKind.Repair, state, frame, bodyRamp);
}
