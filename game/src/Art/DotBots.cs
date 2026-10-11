using System;

namespace ShipSim.Art;

// 도트 로봇 · 드론 — 로봇은 로봇답게(궤도 · 상자 몸 · 외눈 · 팔), 드론은 드론답게(날개 넷 · 잔상 · 그림자 · 항법등).
//   상태: 멀쩡 · 일함(용접 불꽃) · 고장(찌그러짐 · 떨어진 덮개 · 드러난 전선 · 빨간 눈 · 불꽃 · 연기) · 꺼짐.

public enum DotBotKind : byte { Maintainer, Hauler, Gardener, Safety }
public enum DotBotState : byte { Ok, Working, Broken, Off }

public static class DotBots
{
    public const int RW = 30, RH = 32;

    /// <summary>로봇 한 장면 (앞에서 약간 위로 본 모습).</summary>
    public static Img Robot(DotBotKind kind, DotBotState state, int frame = 0, int seed = 0)
    {
        var d = new Dot(RW, RH);
        switch (kind)
        {
            case DotBotKind.Hauler: Hauler(d, state, frame); break;
            default: Maintainer(d, state, frame, kind); break;
        }
        d.Bevel();
        Details(d, kind, state, frame, seed);
        var img = d.ToImg();
        Effects(img, kind, state, frame, seed);
        return img;
    }

    private static int Paint(DotBotKind k) => k switch
    {
        DotBotKind.Gardener => Ramps.ClothGreen,
        DotBotKind.Safety => Ramps.ClothRed,
        _ => Ramps.Hazard,
    };

    private static void Maintainer(Dot d, DotBotState state, int frame, DotBotKind kind)
    {
        int paint = Paint(kind);
        // 궤도 (왼 · 오)
        d.Part = 1; d.Rect(4, 20, 6, 10, Ramps.Rubber, 2);
        d.Part = 2; d.Rect(20, 20, 6, 10, Ramps.Rubber, 2);
        // 차대
        d.Part = 3; d.Rect(9, 22, 12, 6, Ramps.Steel, 2);
        // 몸통 상자: 윗면(밝게) · 앞면
        d.Part = 4; d.Rect(6, 11, 18, 4, paint, 3);
        d.Part = 5; d.Rect(6, 15, 18, 8, paint, 2);
        // 머리 (센서 돔)
        d.Part = 6; d.Oval(15, 8.5f, 5.5f, 4f, Ramps.Steel, 3);
        // 안테나
        d.Part = 7; d.Line(19, 5, 21, 1, Ramps.Steel, 4);
        // 오른팔: 어깨 → 위팔 → 아래팔 → 집게
        bool work = state == DotBotState.Working;
        d.Part = 8; d.Oval(25.5f, 14.5f, 1.8f, 1.8f, Ramps.Steel, 2);
        d.Part = 9;
        if (work) { d.Line(26, 13, 28, 9, Ramps.Steel, 3); d.Line(27, 13, 29, 9, Ramps.Steel, 3); d.Line(28, 9, 28, 4, Ramps.Steel, 3); }
        else { d.Line(26, 15, 27, 20, Ramps.Steel, 3); d.Line(27, 15, 28, 20, Ramps.Steel, 3); }
        d.Part = 10;
        if (work) { d.Set(27, 3, Ramps.Steel, 4); d.Set(29, 3, Ramps.Steel, 4); d.Set(28, 2, Ramps.Hazard, 4); }
        else { d.Set(26, 21, Ramps.Steel, 4); d.Set(28, 21, Ramps.Steel, 4); d.Set(26, 22, Ramps.Steel, 3); d.Set(28, 22, Ramps.Steel, 3); }
        // 왼쪽 공구함
        d.Part = 11; d.Rect(1, 15, 5, 5, Ramps.Alarm, 3); d.Set(2, 14, Ramps.Steel, 3); d.Set(3, 14, Ramps.Steel, 3); d.Set(4, 14, Ramps.Steel, 3);
        if (kind == DotBotKind.Gardener) { d.Part = 11; d.Rect(1, 13, 5, 7, Ramps.Wood, 3); }
        if (kind == DotBotKind.Safety) { d.Part = 11; d.Rect(1, 12, 4, 9, Ramps.Alarm, 3); d.Set(2, 11, Ramps.Rubber, 2); }
    }

    private static void Hauler(Dot d, DotBotState state, int frame)
    {
        // 바퀴 넷
        d.Part = 1; d.Oval(6, 27, 3, 3, Ramps.Rubber, 2); d.Oval(24, 27, 3, 3, Ramps.Rubber, 2);
        // 납작한 차체: 윗면 · 앞면
        d.Part = 2; d.Rect(2, 17, 26, 4, Ramps.Steel, 4);
        d.Part = 3; d.Rect(2, 21, 26, 7, Ramps.Steel, 2);
        d.Part = 4; d.Oval(6, 29, 2.5f, 2.2f, Ramps.Rubber, 3); d.Oval(24, 29, 2.5f, 2.2f, Ramps.Rubber, 3);
        // 뒤쪽 센서 탑
        d.Part = 5; d.Rect(3, 7, 7, 10, Ramps.Panel, 3);
        d.Part = 6; d.Rect(4, 5, 5, 2, Ramps.Panel, 4);
        // 짐 (나무 상자)
        d.Part = 7; d.Rect(11, 9, 15, 3, Ramps.Wood, 4);
        d.Part = 8; d.Rect(11, 12, 15, 6, Ramps.Wood, 3);
    }

    private static void Details(Dot d, DotBotKind kind, DotBotState state, int frame, int seed)
    {
        bool broken = state == DotBotState.Broken, off = state == DotBotState.Off;
        int eye = broken ? Ramps.Alarm : off ? Ramps.Panel : Ramps.Cyan;
        if (kind == DotBotKind.Hauler)
        {
            // 앞면 주황 띠 · 전조등 · 바퀴 축 · 상자 판자 · 화면
            for (int x = 3; x < 27; x++) d.Mark(x, 23, Ramps.ClothOrange, 3);
            d.Mark(3, 22, Ramps.Hazard, 4); d.Mark(26, 22, Ramps.Hazard, 4);
            d.Mark(6, 27, Ramps.Steel, 4); d.Mark(24, 27, Ramps.Steel, 4);
            for (int x = 11; x < 26; x++) { d.Mark(x, 14, Ramps.Wood, 1); }
            d.Mark(14, 15, Ramps.Wood, 1); d.Mark(21, 16, Ramps.Wood, 1);
            d.Mark(12, 13, Ramps.Hazard, 3); d.Mark(13, 13, Ramps.Hazard, 3);
            d.MarkRect(4, 9, 5, 3, eye, off ? 1 : 2); d.Mark(5, 9, eye, off ? 2 : 4);
            for (int x = 4; x < 9; x++) d.Mark(x, 13, Ramps.Led, (x + frame) % 3 == 0 ? 3 : 1);
            return;
        }
        // 정비 로봇: 바이저 · 외눈 · 판 이음매 · 통풍구 · 위험 줄무늬 · 궤도 무늬 · 바퀴
        d.MarkRect(11, 7, 9, 3, Ramps.Panel, 1);
        d.Mark(14, 8, eye, 2); d.Mark(15, 8, eye, 3); d.Mark(16, 8, eye, 2); d.Mark(15, 7, eye, off ? 1 : 4);
        for (int x = 7; x < 23; x++) d.Mark(x, 18, Ramps.Hazard, 1);
        for (int x = 9; x < 21; x += 2) { d.Mark(x, 16, Ramps.Panel, 1); d.Mark(x, 17, Ramps.Panel, 2); }
        for (int x = 6; x < 24; x++) d.Mark(x, 21, ((x / 2) & 1) == 0 ? Ramps.Ink : Ramps.Hazard, ((x / 2) & 1) == 0 ? 2 : 3);
        for (int y = 20; y < 30; y++)
        {
            int k = (y + frame) & 1;
            d.Mark(4, y, Ramps.Rubber, k == 0 ? 0 : 3); d.Mark(25, y, Ramps.Rubber, k == 0 ? 0 : 3);
        }
        foreach (var (wx, wy) in new[] { (7, 23), (7, 27), (22, 23), (22, 27) }) { d.Mark(wx, wy, Ramps.Steel, 4); d.Mark(wx + 1, wy, Ramps.Steel, 3); d.Mark(wx, wy + 1, Ramps.Steel, 2); d.Mark(wx + 1, wy + 1, Ramps.Steel, 1); }
        d.Mark(8, 13, Ramps.Led, off ? 0 : 3); d.Mark(10, 13, broken ? Ramps.Alarm : Ramps.Led, off ? 0 : (frame & 1) == 0 ? 3 : 1);
        d.Mark(21, 1, Ramps.Alarm, (frame & 1) == 0 && !off ? 4 : 1, onEmpty: true);
        if (broken)
        {
            // 찌그러짐 · 떨어진 덮개 (속 전선) · 그을음
            d.Mark(9, 12, Ramps.Hazard, 0); d.Mark(10, 13, Ramps.Hazard, 1); d.Mark(20, 19, Ramps.Hazard, 0);
            d.MarkRect(15, 15, 5, 4, Ramps.Panel, 0);
            d.Mark(16, 16, Ramps.Alarm, 3); d.Mark(17, 17, Ramps.Led, 3); d.Mark(18, 16, Ramps.Hazard, 4); d.Mark(16, 17, Ramps.Cyan, 2);
            for (int i = 0; i < 9; i++) { int x = 6 + (int)(DotHash.F(seed, i) * 18), y = 11 + (int)(DotHash.F(seed, i, 1) * 12); d.Mark(x, y, Ramps.Char, 1); }
        }
    }

    private static void Effects(Img img, DotBotKind kind, DotBotState state, int frame, int seed)
    {
        if (state == DotBotState.Off) return;
        if (kind != DotBotKind.Hauler)
        {
            img.Glow(15.5f, 8.5f, 4f, Ramps.Get(state == DotBotState.Broken ? Ramps.Alarm : Ramps.Cyan, 3), 0.35f);
        }
        if (state == DotBotState.Working && kind != DotBotKind.Hauler)
        {
            // 용접 불꽃: 끝에서 흩어지는 불티
            img.Glow(28.5f, 2.5f, 5f, Ramps.Get(Ramps.Fire, 6), 0.8f);
            for (int i = 0; i < 6; i++)
            {
                float a = DotHash.F(seed + frame, i) * 6.28f, r = 1.5f + DotHash.F(seed + frame, i, 2) * 3.5f;
                int x = (int)(28.5f + MathF.Cos(a) * r), y = (int)(2.5f + MathF.Sin(a) * r);
                img.Set(x, y, Ramps.Get(Ramps.Fire, 5 + (i & 1)));
            }
        }
        if (state == DotBotState.Broken)
        {
            // 튀는 불꽃 · 연기 한 줄기
            var spark = Ramps.Get(Ramps.Fire, 6);
            img.Set(17, 14, spark); img.Set(19, 13, spark); img.Set(18, 12, Ramps.Get(Ramps.Fire, 5));
            img.Glow(17.5f, 16.5f, 3.5f, spark, 0.5f);
        }
    }

    /// <summary>드론 (선체 안팎에서 쓰는 소형 수리 드론 — 날개 넷).</summary>
    public static Img Drone(DotBotState state, int frame = 0, int bodyRamp = -1)
    {
        int body = bodyRamp < 0 ? Ramps.ClothWhite : bodyRamp;
        var d = new Dot(26, 22);
        // 팔 넷 (대각선)
        d.Part = 1;
        d.Line(6, 5, 11, 9, Ramps.Steel, 3); d.Line(19, 5, 14, 9, Ramps.Steel, 3);
        d.Line(6, 16, 11, 12, Ramps.Steel, 2); d.Line(19, 16, 14, 12, Ramps.Steel, 2);
        // 날개 축
        d.Part = 2;
        foreach (var (x, y) in new[] { (5, 4), (20, 4), (5, 16), (20, 16) }) { d.Rect(x - 1, y, 2, 2, Ramps.Steel, 2); }
        // 몸통 · 띠
        d.Part = 3; d.Oval(12.5f, 10.5f, 5f, 4f, body, 3);
        d.Part = 4; d.Rect(10, 14, 6, 2, Ramps.Panel, 3);
        // 아래 집게
        d.Part = 5; d.Set(11, 16, Ramps.Steel, 3); d.Set(14, 16, Ramps.Steel, 3); d.Set(11, 17, Ramps.Steel, 4); d.Set(14, 17, Ramps.Steel, 4);
        d.Bevel();
        for (int x = 8; x < 17; x++) d.Mark(x, 9, Ramps.ClothOrange, 3);
        bool broken = state == DotBotState.Broken;
        d.Mark(12, 12, Ramps.Ink, 1); d.Mark(13, 12, broken ? Ramps.Alarm : Ramps.Cyan, 3); d.Mark(13, 11, Ramps.Ink, 1);
        bool blink = (frame & 1) == 0 && state != DotBotState.Off;
        d.Mark(8, 11, Ramps.Alarm, blink ? 4 : 1); d.Mark(17, 11, Ramps.Led, blink ? 3 : 1);
        var img = d.ToImg();
        if (state != DotBotState.Off)
        {
            // 날개 잔상 (반투명 원판 + 한 줄 반짝)
            var blur = Ramps.Get(Ramps.Steel, 5);
            foreach (var (x, y) in new[] { (5f, 4.5f), (20f, 4.5f), (5f, 16.5f), (20f, 16.5f) })
            {
                img.Oval(x, y, 4.6f, 2.2f, blur, 0.28f);
                int k = (frame + (int)x) % 3;
                img.Blend((int)x - 3 + k, (int)y, blur, 0.7f);
                img.Blend((int)x + 2 - k, (int)y, blur, 0.5f);
            }
            if (blink) { img.Glow(8.5f, 11.5f, 3f, Ramps.Get(Ramps.Alarm, 4), 0.5f); img.Glow(17.5f, 11.5f, 3f, Ramps.Get(Ramps.Led, 3), 0.5f); }
        }
        return img;
    }
}
