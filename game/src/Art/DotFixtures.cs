using System;

namespace ShipSim.Art;

// 도트 설비 · 바닥 · 벽 · 문 — 위에서 비스듬히 본 시점: 윗면은 밝게, 남쪽 앞면이 보인다 (키 큰 것은 칸 위로 솟는다).
//   상태: 켜짐 · 꺼짐 · 고장 · 달아오름 · 불 (설비마다 다르게).

public enum DotMachineState : byte { On, Off, Broken, Hot, Burning }

public static class DotFixtures
{
    public const int T = 32;

    public static readonly int Floor = Ramps.Add("#22262e", "#2e333d", "#3a404b", "#474e5a", "#565e6b", "#6b7482");
    public static readonly int WallTop = Ramps.Add("#0c0f14", "#151a21", "#1f252e", "#2b323d", "#3a4350");
    public static readonly int WallFace = Ramps.Add("#1a1f27", "#262c36", "#343c48", "#454f5d", "#5a6576", "#7a8696");

    // ───────────────────────────── 바닥 · 벽 · 문 ─────────────────────────────

    /// <summary>금속 바닥판 한 칸 (이음매 · 리벳 · 긁힘 — 칸마다 조금씩 다르게).</summary>
    public static Img FloorTile(int cx, int cy, int kind = 0)
    {
        var d = new Dot(T, T);
        int seed = cx * 73 + cy * 151;
        if (kind == 1)
        {
            // 격자 (기관실 · 통로)
            d.Rect(0, 0, T, T, Floor, 1);
            for (int y = 1; y < T; y += 4) for (int x = 1; x < T; x += 4) { d.Rect(x, y, 3, 3, Floor, 3); }
            d.Bevel();
            for (int y = 1; y < T; y += 4) for (int x = 1; x < T; x += 4) d.Mark(x + 2, y + 2, Floor, 1);
            return d.ToImg(outline: false);
        }
        d.Rect(0, 0, T, T, Floor, 3);
        d.Bevel();
        // 판 이음매: 위 · 왼쪽 밝게, 아래 · 오른쪽 어둡게 (판 하나 = 한 칸, 가운데 반 이음)
        for (int i = 0; i < T; i++) { d.Mark(i, 0, Floor, 4); d.Mark(0, i, Floor, 4); d.Mark(i, T - 1, Floor, 1); d.Mark(T - 1, i, Floor, 1); }
        for (int i = 1; i < T - 1; i++) if ((i & 3) != 0) d.Mark(i, T / 2, Floor, 2);
        foreach (var (x, y) in new[] { (3, 3), (T - 4, 3), (3, T - 4), (T - 4, T - 4) }) { d.Mark(x, y, Floor, 5); d.Mark(x + 1, y + 1, Floor, 1); }
        // 긁힘 · 얼룩
        for (int i = 0; i < 6; i++)
        {
            int x = 2 + (int)(DotHash.F(seed, i) * (T - 6)), y = 2 + (int)(DotHash.F(seed, i, 1) * (T - 6));
            int len = 2 + (int)(DotHash.F(seed, i, 2) * 3);
            for (int k = 0; k < len; k++) d.Mark(x + k, y + (k >> 1), Floor, DotHash.F(seed, i, 3) < 0.5f ? 4 : 2);
        }
        return d.ToImg(outline: false);
    }

    /// <summary>벽 한 칸의 윗면 (어두운 두께 + 밝은 테).</summary>
    public static Img WallTopTile(bool n, bool e, bool s, bool w)
    {
        var d = new Dot(T, T);
        d.Rect(0, 0, T, T, WallTop, 2);
        for (int i = 0; i < T; i++)
        {
            if (!n) { d.Mark(i, 0, WallTop, 4); d.Mark(i, 1, WallTop, 3); }
            if (!w) { d.Mark(0, i, WallTop, 4); d.Mark(1, i, WallTop, 3); }
            if (!s) d.Mark(i, T - 1, WallTop, 0);
            if (!e) d.Mark(T - 1, i, WallTop, 0);
        }
        // 위에서 본 보강 무늬
        for (int i = 6; i < T - 6; i += 6) { d.Mark(i, T / 2, WallTop, 1); d.Mark(i + 1, T / 2, WallTop, 3); }
        return d.ToImg(outline: false);
    }

    /// <summary>벽의 남쪽 앞면 (아래 칸이 바닥일 때 그 칸 위쪽에 겹쳐 그린다) — 판 · 리벳 · 띠 조명.</summary>
    public static Img WallFaceTile(int cx, bool lightStrip)
    {
        var d = new Dot(T, 12);
        d.Rect(0, 0, T, 12, WallFace, 3);
        d.Bevel();
        for (int x = 0; x < T; x++) { d.Mark(x, 0, WallFace, 5); d.Mark(x, 11, WallFace, 0); d.Mark(x, 10, WallFace, 1); }
        d.Mark(0, 5, WallFace, 1); for (int y = 1; y < 10; y++) d.Mark(T / 2, y, WallFace, 1);
        d.Mark(3, 3, WallFace, 5); d.Mark(T - 4, 3, WallFace, 5); d.Mark(3, 8, WallFace, 5); d.Mark(T - 4, 8, WallFace, 5);
        if (lightStrip) for (int x = 8; x < 24; x++) { d.Mark(x, 4, Ramps.Cyan, 3); d.Mark(x, 5, Ramps.Cyan, 2); }
        var img = d.ToImg(outline: false);
        if (lightStrip) img.Glow(16f, 5f, 10f, Ramps.Get(Ramps.Cyan, 3), 0.25f);
        return img;
    }

    /// <summary>미닫이 문 (벽 줄에 낀 한 칸) — open 0~1.</summary>
    public static Img Door(float open, bool locked = false)
    {
        var d = new Dot(T, T + 12);
        d.Part = 1; d.Rect(0, 0, T, T + 12, Ramps.Steel, 2);
        int gap = (int)(open * 12);
        d.Part = 2; d.Rect(3, 3, 13 - gap, T + 6, Ramps.Steel, 3);
        d.Part = 3; d.Rect(16 + gap, 3, 13 - gap, T + 6, Ramps.Steel, 3);
        if (gap > 0) { d.Part = 4; d.Rect(16 - gap, 3, gap * 2, T + 6, Floor, 2); }
        d.Bevel();
        for (int y = 6; y < T + 6; y += 4)
        {
            if (13 - gap > 2) { d.Mark(4, y, Ramps.Hazard, 3); d.Mark(5, y + 1, Ramps.Ink, 1); }
            if (13 - gap > 2) { d.Mark(27, y, Ramps.Hazard, 3); d.Mark(26, y + 1, Ramps.Ink, 1); }
        }
        d.Mark(15, 1, locked ? Ramps.Alarm : Ramps.Led, 3); d.Mark(16, 1, locked ? Ramps.Alarm : Ramps.Led, 4);
        return d.ToImg();
    }

    // ───────────────────────────── 설비 ─────────────────────────────

    /// <summary>침대 (한 칸 × 두 칸, 머리가 위) — 담요는 개인 색. sleeper가 있으면 베개 위에 머리.</summary>
    public static Img Bed(int blanket, Dot? sleeperHead = null)
    {
        var d = new Dot(T, 2 * T);
        d.Part = 1; d.Rect(1, 2, T - 2, 2 * T - 6, Ramps.Steel, 3);         // 틀
        d.Part = 2; d.Rect(3, 4, T - 6, 2 * T - 12, Ramps.Bandage, 2);      // 매트리스
        d.Part = 3; d.Rect(5, 6, T - 10, 8, Ramps.Bandage, 3);             // 베개
        d.Part = 5; d.Rect(3, 22, T - 6, 2 * T - 30, blanket, 2);           // 담요
        d.Part = 6; d.Rect(3, 22, T - 6, 3, blanket, 3);                    // 접힌 끝
        d.Part = 7; d.Rect(1, 2 * T - 4, T - 2, 4, Ramps.Steel, 2);         // 발치 앞면
        d.Bevel();
        for (int x = 4; x < T - 4; x += 3) d.Mark(x, 36, blanket, 1);
        d.Mark(6, 2 * T - 3, Ramps.Steel, 4); d.Mark(T - 7, 2 * T - 3, Ramps.Steel, 4);
        if (sleeperHead != null)
        {
            // 누운 사람: 베개 위에 머리, 담요 아래로 몸 윤곽
            d.Part = 4;
            d.Paste(sleeperHead, (T - sleeperHead.W) / 2, 2);
            for (int x = 9; x < T - 9; x++) d.Mark(x, 28, blanket, 3);
            for (int y = 25; y < 50; y++) { d.Mark(9, y, blanket, 1); d.Mark(T - 10, y, blanket, 1); }
        }
        return d.ToImg();
    }

    /// <summary>배터리 뭉치 (두 칸 너비, 칸 위로 8도트 솟음) — 셀 넷 · 충전 막대 · 경고 표지.</summary>
    public static Img Battery(DotMachineState st, float charge = 0.8f, int frame = 0)
    {
        var d = new Dot(2 * T, T + 8);
        d.Part = 1; d.Rect(1, 0, 2 * T - 2, 8, Ramps.Steel, 4);            // 윗면
        d.Part = 2; d.Rect(1, 8, 2 * T - 2, T - 2, Ramps.Panel, 3);         // 앞면
        for (int i = 0; i < 4; i++)
        {
            d.Part = (byte)(3 + i);
            int x = 4 + i * 15;
            bool swell = st is DotMachineState.Hot or DotMachineState.Burning && i == 2;
            if (swell) d.Oval(x + 5.5f, 21f, 7f, 11f, Ramps.ClothGreen, 2);
            else d.Rect(x, 11, 11, 21, Ramps.ClothGreen, 1);
        }
        d.Part = 10; d.Rect(1, T + 6, 2 * T - 2, 2, Ramps.Steel, 2);
        d.Bevel();
        // 윗면 통풍구 · 케이블
        for (int x = 6; x < 2 * T - 6; x += 3) { d.Mark(x, 3, Ramps.Steel, 1); d.Mark(x, 4, Ramps.Steel, 2); }
        d.Mark(10, 1, Ramps.Alarm, 3); d.Mark(11, 1, Ramps.Alarm, 3); d.Mark(52, 1, Ramps.Ink, 2); d.Mark(53, 1, Ramps.Ink, 2);
        for (int i = 0; i < 4; i++)
        {
            int x = 4 + i * 15;
            bool dead = st is DotMachineState.Off or DotMachineState.Broken;
            int bars = dead ? 0 : (int)MathF.Round(charge * 5);
            for (int b = 0; b < 5; b++) d.Mark(x + 4, 28 - b * 3, b < bars ? (st == DotMachineState.Hot && i == 2 ? Ramps.Alarm : Ramps.Led) : Ramps.Panel, b < bars ? 3 : 1);
            for (int b = 0; b < 5; b++) d.Mark(x + 5, 28 - b * 3, b < bars ? Ramps.Led : Ramps.Panel, b < bars ? 2 : 1);
            d.Mark(x + 1, 12, Ramps.ClothGreen, 3); d.Mark(x + 2, 12, Ramps.ClothGreen, 3);
        }
        // 경고 표지 (노란 세모 · 번개)
        int lx = 2 * T - 9, ly = 10;
        for (int r = 0; r < 5; r++) for (int x = lx - r; x <= lx + r; x++) d.Mark(x, ly + r, Ramps.Hazard, 3);
        d.Mark(lx, ly + 1, Ramps.Ink, 1); d.Mark(lx, ly + 2, Ramps.Ink, 1); d.Mark(lx, ly + 4, Ramps.Ink, 1);
        if (st == DotMachineState.Burning) for (int i = 0; i < 30; i++) { int x = 2 + (int)(DotHash.F(9, i) * 60), y = 2 + (int)(DotHash.F(9, i, 1) * 34); d.Mark(x, y, Ramps.Char, 1); }
        var img = d.ToImg();
        if (st == DotMachineState.Hot) img.Glow(39.5f, 21f, 14f, Ramps.Get(Ramps.Alarm, 4), 0.45f + 0.15f * (frame & 1));
        return img;
    }

    /// <summary>산소 발생기 (두 칸) — 원통 탱크 둘 · 전해조 창(물 · 거품) · 관 · 계기.</summary>
    public static Img Oxygen(DotMachineState st, int frame = 0)
    {
        var d = new Dot(2 * T, T + 12);
        // 탱크 둘 (칸 위로 솟는 원통)
        for (int i = 0; i < 2; i++)
        {
            d.Part = (byte)(1 + i);
            int x = 3 + i * 11;
            d.Rect(x, 4, 9, T + 4, Ramps.ClothWhite, 2);
            d.Part = (byte)(3 + i);
            d.Oval(x + 4.5f, 4.5f, 4.5f, 2.5f, Ramps.ClothWhite, 3);
        }
        // 전해조 장
        d.Part = 5; d.Rect(26, 10, 36, 6, Ramps.Steel, 4);
        d.Part = 6; d.Rect(26, 16, 36, T - 4, Ramps.Steel, 2);
        d.Part = 7; d.Rect(30, 19, 18, 16, Ramps.Glass, 2);
        // 관
        d.Part = 8; d.Rect(21, 14, 6, 2, Ramps.Steel, 4); d.Rect(21, 30, 6, 2, Ramps.Steel, 4);
        d.Part = 9; d.Oval(55, 24, 4, 4, Ramps.Bandage, 3);
        d.Bevel();
        for (int i = 0; i < 2; i++) { int x = 3 + i * 11; for (int xx = x; xx < x + 9; xx++) { d.Mark(xx, 20, Ramps.Cyan, 2); d.Mark(xx, 21, Ramps.Cyan, 3); } d.Mark(x + 2, 8, Ramps.ClothWhite, 4); d.Mark(x + 2, 9, Ramps.ClothWhite, 4); }
        // 창 속 물과 거품 (장면마다 위로)
        for (int y = 21; y < 35; y++) for (int x = 31; x < 47; x++) d.Mark(x, y, Ramps.Glass, y < 23 ? 1 : 2);
        if (st != DotMachineState.Off)
            for (int i = 0; i < 9; i++)
            {
                int bx = 32 + (int)(DotHash.F(3, i) * 14), by = 34 - ((int)(DotHash.F(3, i, 1) * 12) + frame * 2) % 12;
                d.Mark(bx, by, Ramps.Cyan, 4);
            }
        d.Mark(30, 19, Ramps.Glass, 4); d.Mark(31, 19, Ramps.Glass, 4);
        // 계기 바늘 · 상태 화면
        d.Mark(55, 24, Ramps.Ink, 1); d.Mark(56, 23, Ramps.Ink, 1); d.Mark(57, 22, Ramps.Ink, 1);
        d.MarkRect(51, 31, 8, 4, Ramps.Panel, 1);
        int led = st == DotMachineState.Broken ? Ramps.Alarm : Ramps.Led;
        for (int x = 52; x < 58; x += 2) d.Mark(x, 32, led, st == DotMachineState.Off ? 0 : 3);
        for (int x = 52; x < 56; x += 1) d.Mark(x, 33, led, st == DotMachineState.Off ? 0 : 2);
        var img = d.ToImg();
        if (st == DotMachineState.Hot) img.Glow(40, 27, 12, Ramps.Get(Ramps.Alarm, 4), 0.4f);
        return img;
    }

    /// <summary>배전반 (벽에 붙은 한 칸) — 차단기 2×4 · 표시등 · 위험 띠 · 위로 가는 배관. 아크면 번개.</summary>
    public static Img PowerPanel(DotMachineState st, int frame = 0)
    {
        var d = new Dot(T, T + 8);
        d.Part = 1; d.Rect(10, 0, 4, 6, Ramps.Steel, 2); d.Rect(18, 0, 4, 6, Ramps.Steel, 2);   // 배관
        d.Part = 2; d.Rect(3, 5, T - 6, T, Ramps.Steel, 3);                                       // 함
        d.Part = 3; d.Rect(5, 8, T - 10, T - 9, Ramps.Panel, 2);                                   // 안판
        d.Bevel();
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 4; c++)
            {
                int x = 7 + c * 5, y = 11 + r * 9;
                bool tripped = st == DotMachineState.Broken && (r + c) % 3 == 0;
                d.MarkRect(x, y, 3, 6, Ramps.Ink, 1);
                d.Mark(x + 1, tripped ? y + 4 : y + 1, Ramps.Bandage, 3); d.Mark(x + 1, tripped ? y + 3 : y + 2, Ramps.Bandage, 2);
                d.Mark(x + 1, y - 1, tripped ? Ramps.Alarm : Ramps.Led, st == DotMachineState.Off ? 0 : 3);
            }
        for (int x = 4; x < T - 4; x++) d.Mark(x, T + 2, ((x / 2) & 1) == 0 ? Ramps.Hazard : Ramps.Ink, ((x / 2) & 1) == 0 ? 3 : 1);
        var img = d.ToImg();
        if (st is DotMachineState.Hot or DotMachineState.Burning)
        {
            // 아크: 청백색 번개가 함에서 바닥으로
            var bolt = Ramps.Get(Ramps.Cyan, 4);
            int x = 16, y = 20;
            for (int i = 0; i < 14; i++)
            {
                x += (int)(DotHash.F(frame, i) * 5) - 2; y += 1;
                img.Set(x, y, bolt); img.Set(x + 1, y, Ramps.Get(Ramps.Cyan, 3));
            }
            img.Glow(16, 24, 16, Ramps.Get(Ramps.Cyan, 4), 0.7f);
        }
        return img;
    }

    /// <summary>조리대 (두 칸) — 화구 둘(빨갛게 단 코일) · 냄비 · 손잡이 · 오븐 창.</summary>
    public static Img Stove(DotMachineState st, int frame = 0)
    {
        var d = new Dot(2 * T, T + 4);
        d.Part = 1; d.Rect(1, 0, 2 * T - 2, 14, Ramps.Steel, 4);   // 윗면
        d.Part = 2; d.Rect(1, 14, 2 * T - 2, T - 10, Ramps.Steel, 2);  // 앞면
        d.Part = 3; d.Oval(15, 7, 7, 5, Ramps.Ink, 2);                // 화구 1
        d.Part = 4; d.Oval(41, 7, 7, 5, Ramps.Ink, 2);                // 화구 2
        d.Part = 5; d.Rect(35, 0, 13, 9, Ramps.Steel, 3);             // 냄비
        d.Part = 6; d.Rect(33, 3, 2, 2, Ramps.Rubber, 3); d.Rect(48, 3, 2, 2, Ramps.Rubber, 3);
        d.Part = 7; d.Rect(20, 19, 24, 11, Ramps.Panel, 2);           // 오븐 창
        d.Bevel();
        bool on = st != DotMachineState.Off;
        for (int i = 0; i < 3; i++) { d.MarkOval(15, 7, 5 - i * 1.6f, 3.5f - i * 1.1f, on ? Ramps.Fire : Ramps.Ink, on ? 3 + i : 1 + (i & 1)); }
        d.MarkRect(36, 1, 11, 2, Ramps.Steel, 5);
        for (int x = 6; x < 60; x += 9) { d.Mark(x, 17, Ramps.Ink, 1); d.Mark(x + 1, 17, Ramps.Ink, 0); d.Mark(x, 16, Ramps.Steel, 5); }
        d.MarkRect(22, 21, 20, 7, on ? Ramps.Fire : Ramps.Panel, on ? 2 : 1); d.MarkRect(22, 21, 20, 1, Ramps.Glass, 4);
        var img = d.ToImg();
        if (on)
        {
            img.Glow(15, 7, 9, Ramps.Get(Ramps.Fire, 5), 0.5f);
            // 김
            var steam = Ramps.Get(Ramps.Smoke, 6);
            for (int i = 0; i < 3; i++)
            {
                float y = -2 - ((frame * 2 + i * 4) % 10), x = 39 + i * 3 + MathF.Sin((frame + i) * 1.3f) * 1.5f;
                img.Oval(x, y + 4, 2.2f, 1.6f, steam, 0.35f);
            }
        }
        if (st == DotMachineState.Burning) { img.Glow(15, 4, 14, Ramps.Get(Ramps.Fire, 5), 0.9f); }
        return img;
    }

    /// <summary>냉각 펌프 (한 칸 반) — 파란 전동기 · 펌프 몸통 · 관 · 볼트 · 팬 · 계기.</summary>
    public static Img Pump(DotMachineState st, int frame = 0)
    {
        var d = new Dot(48, T + 6);
        d.Part = 1; d.Rect(0, 20, 48, 6, Ramps.ClothBlue, 3);          // 관 (뒤로 지나감)
        d.Part = 2; d.Rect(4, 8, 20, 22, Ramps.ClothBlue, 2);          // 전동기
        d.Part = 3; d.Rect(4, 4, 20, 4, Ramps.ClothBlue, 3);
        d.Part = 4; d.Oval(33, 20, 9, 10, Ramps.Steel, 3);             // 펌프 몸통
        d.Part = 5; d.Rect(2, 30, 44, 6, Ramps.Steel, 2);              // 받침
        d.Bevel();
        // 냉각 핀 · 볼트 · 팬 날개(돈다) · 계기
        for (int y = 10; y < 28; y += 2) for (int x = 6; x < 22; x++) d.Mark(x, y, Ramps.ClothBlue, 1);
        for (int k = 0; k < 8; k++) { float a = k * MathF.PI / 4; d.Mark(33 + (int)MathF.Round(MathF.Cos(a) * 7), 20 + (int)MathF.Round(MathF.Sin(a) * 8), Ramps.Steel, 5); }
        float spin = st == DotMachineState.On ? frame * 0.7f : 0f;
        for (int k = 0; k < 3; k++) { float a = spin + k * 2.09f; for (int r = 1; r < 6; r++) d.Mark(33 + (int)MathF.Round(MathF.Cos(a) * r), 20 + (int)MathF.Round(MathF.Sin(a) * r), Ramps.Steel, 1); }
        d.Mark(33, 20, Ramps.Steel, 5);
        d.MarkOval(14, 5, 3, 2, Ramps.Bandage, 3); d.Mark(14, 5, Ramps.Ink, 1); d.Mark(15, 4, Ramps.Ink, 1);
        d.Mark(6, 32, Ramps.Led, st == DotMachineState.Off ? 0 : st == DotMachineState.Broken ? 1 : 3);
        if (st == DotMachineState.Broken) d.Mark(8, 32, Ramps.Alarm, 4);
        var img = d.ToImg();
        if (st is DotMachineState.Hot or DotMachineState.Broken)
        {
            // 새는 냉각수 방울 · 김
            img.Set(42, 33, Ramps.Get(Ramps.Cyan, 3)); img.Set(43, 35, Ramps.Get(Ramps.Cyan, 2));
            img.Oval(40, 12, 3, 2, Ramps.Get(Ramps.Smoke, 6), 0.4f);
        }
        return img;
    }

    /// <summary>탁자 (두 칸) · 의자 · 컵 — 생활 장면.</summary>
    public static Img Table()
    {
        var d = new Dot(2 * T, 22);
        d.Part = 1; d.Rect(1, 0, 2 * T - 2, 14, Ramps.Wood, 3);
        d.Part = 2; d.Rect(1, 14, 2 * T - 2, 3, Ramps.Wood, 1);
        d.Part = 3; d.Rect(4, 17, 3, 5, Ramps.Wood, 1); d.Rect(2 * T - 7, 17, 3, 5, Ramps.Wood, 1);
        d.Bevel();
        for (int x = 2; x < 2 * T - 2; x += 7) for (int y = 2; y < 13; y++) if (((y + x) % 5) == 0) d.Mark(x, y, Ramps.Wood, 2);
        return d.ToImg();
    }

    public static Img Chair(bool facingUp)
    {
        var d = new Dot(16, 20);
        if (facingUp) { d.Part = 1; d.Rect(2, 8, 12, 8, Ramps.ClothNavy, 3); d.Part = 2; d.Rect(2, 14, 12, 6, Ramps.ClothNavy, 1); }
        else { d.Part = 1; d.Rect(2, 0, 12, 8, Ramps.ClothNavy, 2); d.Part = 2; d.Rect(2, 8, 12, 6, Ramps.ClothNavy, 3); d.Part = 3; d.Rect(3, 14, 2, 5, Ramps.Steel, 2); d.Rect(11, 14, 2, 5, Ramps.Steel, 2); }
        d.Bevel();
        return d.ToImg();
    }

    public static Img Cup(bool full, bool broken = false)
    {
        var d = new Dot(7, 7);
        if (broken)
        {
            d.Rect(0, 4, 3, 2, Ramps.Bandage, 3); d.Rect(4, 3, 2, 2, Ramps.Bandage, 2); d.Set(3, 6, Ramps.Bandage, 3);
            d.Bevel();
            return d.ToImg();
        }
        d.Part = 1; d.Rect(1, 1, 4, 5, Ramps.Bandage, 3);
        d.Part = 2; d.Set(5, 2, Ramps.Bandage, 2); d.Set(5, 3, Ramps.Bandage, 2);
        d.Bevel();
        if (full) { d.Mark(2, 1, Ramps.Wood, 1); d.Mark(3, 1, Ramps.Wood, 2); }
        return d.ToImg();
    }

    public static Img Plant(int seed)
    {
        var d = new Dot(16, 24);
        d.Part = 1; d.Rect(4, 16, 8, 7, Ramps.ClothRed, 2);
        d.Part = 2; d.Rect(3, 15, 10, 2, Ramps.ClothRed, 3);
        d.Part = 3;
        for (int i = 0; i < 7; i++)
        {
            float a = -MathF.PI / 2 + (DotHash.F(seed, i) - 0.5f) * 2.4f;
            float len = 6 + DotHash.F(seed, i, 1) * 6;
            for (int r = 0; r < len; r++) { int x = 8 + (int)(MathF.Cos(a) * r), y = 15 + (int)(MathF.Sin(a) * r); d.Set(x, y, Ramps.Leaf, 2 + (r > len / 2 ? 1 : 0)); if (r > 2) d.Set(x + 1, y, Ramps.Leaf, 2); }
        }
        d.Bevel();
        d.MarkRect(5, 15, 6, 1, Ramps.Soil, 1);
        return d.ToImg();
    }
}
