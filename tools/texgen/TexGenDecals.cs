using System;

namespace ShipSim.TexGen;

// v16.5 흔적 아틀라스 (512×256 · 칸 64px · 8×4): 얼룩 · 발자국 · 긁힘 · 테이프 · 쪽지 · 용접 자국 · 탄 자국 · 금.
// 칸 번호는 View/LookSpec.cs 의 Decal 표와 같다 (--lookcheck 가 칸마다 그림이 있는지 · 서로 다른지 본다).
// 입자 아틀라스 (256×32 · 칸 32px): 김 · 연기 · 물방울 · 튀는 물 · 불꽃 · 불씨 · 먼지 · 결로 방울.
public static partial class Gen
{
    public const int DecalSlot = 64, DecalCols = 8, DecalRows = 4;

    private static (float x, float y) SlotC(int k) => ((k % DecalCols) * DecalSlot + 32f, (k / DecalCols) * DecalSlot + 32f);

    /// <summary>잡음으로 가장자리가 울퉁불퉁한 덩어리 (반지름 r · 들쭉 j).</summary>
    private static void Blob(Pix p, float cx, float cy, float r, float j, int seed, Func<float, float, float, (Rgb c, float a)> paint)
    {
        p.Paint(cx - r * 1.6f, cy - r * 1.6f, cx + r * 1.6f, cy + r * 1.6f, (x, y) =>
        {
            float dx = x - cx, dy = y - cy;
            float ang = MathF.Atan2(dy, dx);
            float wob = 1f + j * (Noise.Value((ang + MathF.PI) / 6.2832f * 7f, 0.5f, 7, 1, seed) - 0.5f) * 2f;
            float d = MathF.Sqrt(dx * dx + dy * dy) / (r * wob);
            if (d > 1.05f) return (Pal.White, 0f);
            return paint(x, y, d);
        });
    }

    private static void Shoe(Pix p, float cx, float cy, float ang, float s, Rgb c, float a, int seed)
    {
        float cs = MathF.Cos(ang), sn = MathF.Sin(ang);
        p.Paint(cx - 12 * s, cy - 12 * s, cx + 12 * s, cy + 12 * s, (x, y) =>
        {
            var (lx, ly) = Sdf.Rot(x - cx, y - cy, cs, sn);
            lx /= s; ly /= s;
            float sole = Sdf.Box(lx, ly + 2.5f, 3.4f, 5.2f, 3f);
            float heel = Sdf.Box(lx, ly - 7.5f, 3f, 2.4f, 2f);
            float d = MathF.Min(sole, heel);
            if (d > 0.5f) return (c, 0f);
            float tread = MathF.Abs(MathF.Sin(ly * 1.9f)) > 0.55f ? 1f : 0.45f;
            float wear = Noise.H01((int)(x * 2), (int)(y * 2), seed) * 0.4f + 0.6f;
            return (c, a * Math.Clamp(0.5f - d, 0f, 1f) * tread * wear);
        });
    }

    public static Pix Decals()
    {
        var p = new Pix(DecalCols * DecalSlot, DecalRows * DecalSlot);
        float cx, cy;

        // 0 커피 고리: 진한 가장자리 · 옅은 안쪽 · 튄 방울
        (cx, cy) = SlotC(0);
        Blob(p, cx, cy, 15f, 0.06f, 400, (x, y, d) => (Pal.Coffee, d > 0.82f ? 0.75f * (1f - MathF.Abs(d - 0.91f) * 8f) + 0.2f : 0.16f + 0.1f * Noise.H01((int)x, (int)y, 401)));
        p.Disc(cx + 19, cy - 13, 2.2f, Pal.Coffee, 0.55f); p.Disc(cx + 23, cy - 17, 1.2f, Pal.Coffee, 0.5f);

        // 1 음식 얼룩: 주황갈색 번짐 · 건더기
        (cx, cy) = SlotC(1);
        Blob(p, cx, cy, 17f, 0.3f, 402, (x, y, d) => (Pal.Food * (0.8f + 0.3f * Noise.H01((int)x, (int)y, 403)), 0.7f * Math.Clamp((1.05f - d) * 4f, 0f, 1f)));
        for (int k = 0; k < 7; k++)
            p.Disc(cx + (Noise.H01(k, 1, 404) - 0.5f) * 22, cy + (Noise.H01(k, 2, 404) - 0.5f) * 22, 1.3f + Noise.H01(k, 3, 404) * 1.5f, k % 2 == 0 ? Pal.FoodBit : Pal.Paper, 0.95f);

        // 2 물때 고리: 하얗게 마른 광물 자국 (겹친 고리 둘)
        (cx, cy) = SlotC(2);
        foreach (var (ox, oy, r) in new[] { (-4f, -2f, 16f), (6f, 5f, 11f) })
            Blob(p, cx + ox, cy + oy, r, 0.12f, 405 + (int)r, (x, y, d) => (Pal.Mineral, d > 0.86f ? 0.6f * (1f - MathF.Abs(d - 0.93f) * 10f) : 0.06f));

        // 3 기름 방울 자국: 검은 방울 줄 (떨어진 순서대로 작아진다)
        (cx, cy) = SlotC(3);
        for (int k = 0; k < 6; k++)
        {
            float r = 6f - k * 0.8f;
            Blob(p, cx - 18 + k * 7.5f, cy + MathF.Sin(k * 0.9f) * 4, r, 0.25f, 410 + k, (x, y, d) => (Pal.Oil, 0.85f * Math.Clamp((1.05f - d) * 5f, 0f, 1f)));
            p.Disc(cx - 19 + k * 7.5f, cy - 1 + MathF.Sin(k * 0.9f) * 4, r * 0.25f, Pal.White, 0.35f);
        }

        // 4 마른 발자국 한 쌍 · 5 젖은 발자국 · 6 기름 발자국
        (cx, cy) = SlotC(4);
        Shoe(p, cx - 7, cy + 6, 0.12f, 1.25f, Pal.Boot, 0.42f, 420); Shoe(p, cx + 7, cy - 8, -0.05f, 1.25f, Pal.Boot, 0.38f, 421);
        (cx, cy) = SlotC(5);
        Shoe(p, cx - 7, cy + 7, 0.1f, 1.25f, Pal.WaterEdge, 0.5f, 422); Shoe(p, cx + 7, cy - 7, -0.1f, 1.25f, Pal.WaterEdge, 0.42f, 423);
        Shoe(p, cx - 7.6f, cy + 6.4f, 0.1f, 1.1f, Pal.WetPrint, 0.25f, 424);
        (cx, cy) = SlotC(6);
        Shoe(p, cx - 7, cy + 6, 0.05f, 1.25f, Pal.Oil, 0.75f, 425); Shoe(p, cx + 7, cy - 8, 0.15f, 1.25f, Pal.Oil, 0.55f, 426);

        // 7 바퀴 자국: 나란한 두 줄 · 바퀴 무늬
        (cx, cy) = SlotC(7);
        foreach (var off in new[] { -9f, 9f })
            p.Paint(cx - 30, cy + off - 3, cx + 30, cy + off + 3, (x, y) =>
            {
                float d = MathF.Abs(y - (cy + off + 2.5f * MathF.Sin((x - cx) * 0.06f)));
                float tread = ((int)(x / 2.5f) % 2 == 0) ? 1f : 0.55f;
                float fade = 1f - MathF.Abs(x - cx) / 32f;
                return (Pal.Boot, d < 2.2f ? 0.4f * tread * fade : 0f);
            });

        // 8 긁힘 다발: 같은 방향 잔줄 (밝은 금속이 드러난다)
        (cx, cy) = SlotC(8);
        for (int k = 0; k < 14; k++)
        {
            float ox = (Noise.H01(k, 0, 430) - 0.5f) * 30, oy = (Noise.H01(k, 1, 430) - 0.5f) * 30, len = 6 + 14 * Noise.H01(k, 2, 430);
            p.Line(cx + ox - len * 0.5f, cy + oy - len * 0.2f, cx + ox + len * 0.5f, cy + oy + len * 0.2f, 0.7f, Pal.Scratch, 0.55f + 0.3f * Noise.H01(k, 3, 430));
        }

        // 9 끌린 자국: 무거운 것을 끌고 간 넓은 줄 두 개 (가장자리 검은 고무 · 가운데 밝은 줄)
        (cx, cy) = SlotC(9);
        foreach (var off in new[] { -7f, 7f })
        {
            p.Line(cx - 28, cy + off - 4, cx + 28, cy + off + 4, 3.2f, Pal.Gouge, 0.35f);
            p.Line(cx - 28, cy + off - 4, cx + 28, cy + off + 4, 1f, Pal.Scratch, 0.6f);
        }

        // 10 파인 홈: 깊게 파인 꺾인 홈 (아래 그늘 · 위 반짝)
        (cx, cy) = SlotC(10);
        {
            float px = cx - 24, py = cy + 10;
            for (int k = 0; k < 5; k++)
            {
                float nx = px + 10, ny = py - 6 + (Noise.H01(k, 0, 440) - 0.5f) * 8;
                p.Line(px, py + 1, nx, ny + 1, 2.4f, Pal.Gouge, 0.8f);
                p.Line(px, py - 0.8f, nx, ny - 0.8f, 0.8f, Pal.Scratch, 0.8f);
                px = nx; py = ny;
            }
        }

        // 11 원형 긁힘: 의자를 돌린 자리 (호 여러 개)
        (cx, cy) = SlotC(11);
        for (int k = 0; k < 6; k++)
        {
            float r = 10 + k * 3.2f, a0 = Noise.H01(k, 0, 445) * 6.28f, span = 1.2f + Noise.H01(k, 1, 445) * 1.6f;
            for (int s = 0; s < 10; s++)
            {
                float t0 = a0 + span * s / 10f, t1 = a0 + span * (s + 1) / 10f;
                p.Line(cx + MathF.Cos(t0) * r, cy + MathF.Sin(t0) * r, cx + MathF.Cos(t1) * r, cy + MathF.Sin(t1) * r, 0.7f, Pal.Scratch, 0.45f);
            }
        }

        // 12 덕트 테이프 X: 은회색 · 찢긴 끝 · 천 결
        (cx, cy) = SlotC(12);
        foreach (var a in new[] { 0.7f, -0.7f })
            Tape(p, cx, cy, a, 44f, 9f, Pal.Duct, 450 + (int)(a * 10));

        // 13 경고 테이프 띠: 노랑 · 검정 빗금
        (cx, cy) = SlotC(13);
        p.Paint(cx - 30, cy - 7, cx + 30, cy + 7, (x, y) =>
        {
            if (MathF.Abs(y - cy) > 6.5f) return (Pal.HazardK, 0f);
            bool yel = (int)MathF.Floor((x + y) / 7f) % 2 == 0;
            float edge = Noise.H01((int)x, 0, 455) * 1.5f;
            if (MathF.Abs(x - cx) > 29f - edge) return (Pal.HazardK, 0f);
            return (yel ? Pal.HazardY : Pal.HazardK, 0.92f);
        });

        // 14 테이프 덧댐: 금 간 곳 위로 사각 덧댐 두 겹
        (cx, cy) = SlotC(14);
        Tape(p, cx - 2, cy, 0f, 34f, 12f, Pal.Duct, 460);
        Tape(p, cx + 3, cy + 2, 1.57f, 30f, 10f, Pal.Duct * 0.92f, 461);
        p.Line(cx - 20, cy - 14, cx + 18, cy + 15, 0.8f, Pal.Crack, 0.6f);

        // 15 반쯤 떨어진 테이프: 한쪽 끝이 말려 들렸다 (그림자)
        (cx, cy) = SlotC(15);
        Tape(p, cx - 4, cy, 0.2f, 34f, 9f, Pal.Duct, 465);
        p.Disc(cx + 16, cy + 6, 6f, Pal.Heel, 0.25f);
        p.Disc(cx + 14, cy + 3, 5f, Pal.Duct * 1.12f, 0.95f);
        p.Disc(cx + 14, cy + 3, 2.4f, Pal.Duct * 0.6f, 0.9f);

        // 16 노란 메모지: 그림자 · 손글씨 줄 · 접힌 귀
        (cx, cy) = SlotC(16);
        Note(p, cx, cy, 0.1f, Pal.Sticky, false, 470);
        // 17 핀 꽂은 쪽지: 흰 종이 · 빨간 핀
        (cx, cy) = SlotC(17);
        Note(p, cx, cy, -0.12f, Pal.Paper, true, 471);
        // 18 점검표: 줄 칸 + 체크 표시
        (cx, cy) = SlotC(18);
        p.Shape(cx - 15, cy - 19, cx + 17, cy + 21, (x, y) => Sdf.Box(x - cx - 2, y - cy + 1, 15f, 20f, 1f), Pal.Heel, 0.3f);
        p.Shape(cx - 17, cy - 21, cx + 15, cy + 19, (x, y) => Sdf.Box(x - cx, y - cy, 15f, 20f, 1f), Pal.Paper, 1f);
        p.Shape(cx - 7, cy - 23, cx + 7, cy - 17, (x, y) => Sdf.Box(x - cx, y - cy + 20, 6f, 2.4f, 1f), Pal.Bolt, 1f);
        for (int k = 0; k < 6; k++)
        {
            float ly = cy - 12 + k * 6;
            p.Line(cx - 12, ly, cx + 12, ly, 0.5f, Pal.PaperLine, 0.7f);
            p.Shape(cx - 12, ly - 4, cx - 8, ly, (x, y) => Sdf.Box(x - cx + 10, y - ly + 2, 1.6f, 1.6f, 0f) , Pal.PaperLine, 0.8f);
            if (k < 4) { p.Line(cx - 12, ly - 2, cx - 10.5f, ly - 0.5f, 0.6f, Pal.Ink, 0.9f); p.Line(cx - 10.5f, ly - 0.5f, cx - 7.5f, ly - 4.5f, 0.6f, Pal.Ink, 0.9f); }
            p.Line(cx - 5, ly - 2, cx + 4 + 6 * Noise.H01(k, 0, 475), ly - 2, 0.55f, Pal.Ink, 0.75f);
        }
        // 19 분필 화살표 · 동그라미 (손으로 그린)
        (cx, cy) = SlotC(19);
        {
            float px = cx - 22, py = cy + 8;
            for (int k = 0; k < 8; k++)
            {
                float nx = cx - 22 + (k + 1) * 5.5f, ny = cy + 8 - (k + 1) * 1.6f + (Noise.H01(k, 0, 480) - 0.5f) * 1.6f;
                p.Line(px, py, nx, ny, 1.4f, Pal.Paper, 0.75f); px = nx; py = ny;
            }
            p.Line(px, py, px - 7, py - 5, 1.4f, Pal.Paper, 0.75f);
            p.Line(px, py, px - 4, py + 7, 1.4f, Pal.Paper, 0.75f);
            for (int s = 0; s < 18; s++)
            {
                float t0 = s / 18f * 6.6f, t1 = (s + 1) / 18f * 6.6f, r0 = 7f + s * 0.12f;
                p.Line(cx - 12 + MathF.Cos(t0) * r0, cy - 14 + MathF.Sin(t0) * r0 * 0.8f, cx - 12 + MathF.Cos(t1) * r0, cy - 14 + MathF.Sin(t1) * r0 * 0.8f, 1.2f, Pal.Paper, 0.65f);
            }
        }

        // 20 용접 비드 줄: 비늘 무늬 · 양옆 열변색 (짚 → 파랑 → 보라)
        (cx, cy) = SlotC(20);
        p.Paint(cx - 30, cy - 12, cx + 30, cy + 12, (x, y) =>
        {
            float d = MathF.Abs(y - cy);
            if (MathF.Abs(x - cx) > 28f) return (Pal.White, 0f);
            if (d < 3.2f)
            {
                float scale = Noise.Tri((x - cx) / 3.2f + d * 0.25f);
                return (Pal.Weld * (0.7f + 0.45f * scale) * (1f - d * 0.06f), 1f);
            }
            float t = (d - 3.2f) / 8f;
            if (t > 1f) return (Pal.White, 0f);
            var c = t < 0.33f ? Pal.HeatStraw : t < 0.66f ? Pal.HeatBlue : Pal.HeatPurple;
            return (c, 0.55f * (1f - t));
        });
        // 21 덧댄 판: 사각 판 · 둘레 용접 · 귀 볼트
        (cx, cy) = SlotC(21);
        p.Shape(cx - 24, cy - 20, cx + 24, cy + 20, (x, y) => Sdf.Box(x - cx, y - cy, 21f, 17f, 2f) - 2.6f, Pal.HeatStraw, 0.6f);
        p.Shape(cx - 22, cy - 18, cx + 22, cy + 18, (x, y) => Sdf.Box(x - cx, y - cy, 21f, 17f, 2f), Pal.Weld * 0.9f, 1f);
        p.Shape(cx - 21, cy - 17, cx + 21, cy + 17, (x, y) => Sdf.Box(x - cx, y - cy, 19.5f, 15.5f, 1.5f), Pal.PatchPlate, 1f);
        p.Line(cx - 19, cy - 15, cx + 19, cy - 15, 0.8f, Pal.White, 0.3f);
        p.Line(cx - 19, cy + 15.5f, cx + 19, cy + 15.5f, 0.8f, Pal.Heel, 0.4f);
        foreach (var (bx, by) in new[] { (-16f, -12f), (16f, -12f), (-16f, 12f), (16f, 12f) }) { p.Disc(cx + bx, cy + by, 2f, Pal.Bolt, 1f); p.Disc(cx + bx - 0.5f, cy + by - 0.5f, 0.8f, Pal.White, 0.5f); }
        // 22 열변색 고리: 점용접 자리 동심 무지개
        (cx, cy) = SlotC(22);
        p.Paint(cx - 22, cy - 22, cx + 22, cy + 22, (x, y) =>
        {
            float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / 20f;
            if (d > 1f) return (Pal.White, 0f);
            var c = d < 0.18f ? Pal.Weld : d < 0.4f ? Pal.HeatStraw : d < 0.65f ? Pal.HeatBlue : Pal.HeatPurple;
            return (c, d < 0.18f ? 1f : 0.6f * (1f - d));
        });
        // 23 용접 불똥 자국: 튄 점들 (가운데 진하게)
        (cx, cy) = SlotC(23);
        for (int k = 0; k < 40; k++)
        {
            float r = 26f * MathF.Sqrt(Noise.H01(k, 0, 490)), a = Noise.H01(k, 1, 490) * 6.28f;
            p.Disc(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r, 0.6f + 1.2f * Noise.H01(k, 2, 490), k % 3 == 0 ? Pal.HeatBlue : Pal.Burn, 0.7f);
        }

        // 24 불탄 자국: 검은 가운데 · 갈색 번짐 · 갈라짐
        (cx, cy) = SlotC(24);
        Blob(p, cx, cy, 24f, 0.35f, 500, (x, y, d) => (Pal.Burn.Lerp(Pal.BurnHalo, Noise.Smooth(0.35f, 1f, d)), Math.Clamp(1.1f - d, 0f, 1f) * (0.95f - 0.15f * Noise.H01((int)x, (int)y, 501))));
        // 25 작은 그을린 점 셋
        (cx, cy) = SlotC(25);
        foreach (var (ox, oy, r) in new[] { (-10f, -6f, 9f), (9f, 4f, 7f), (-2f, 14f, 5f) })
            Blob(p, cx + ox, cy + oy, r, 0.3f, 505 + (int)r, (x, y, d) => (Pal.Burn.Lerp(Pal.BurnHalo, d), 0.85f * Math.Clamp(1.1f - d, 0f, 1f)));
        // 26 금: 갈래진 균열
        (cx, cy) = SlotC(26);
        Crack(p, cx - 26, cy - 4, 0.15f, 10, 510);
        Crack(p, cx - 4, cy + 1, 0.9f, 5, 511);
        // 27 거미줄 금: 맞은 자리에서 사방으로
        (cx, cy) = SlotC(27);
        for (int k = 0; k < 7; k++) Crack(p, cx, cy, k * 0.9f + Noise.H01(k, 0, 515) * 0.4f, 4 + k % 3, 516 + k);
        for (int k = 0; k < 3; k++)
            for (int s = 0; s < 12; s++)
            {
                float r = 7 + k * 6, t0 = s / 12f * 6.28f, t1 = (s + 1) / 12f * 6.28f;
                if (Noise.H01(k, s, 520) < 0.45f) continue;
                p.Line(cx + MathF.Cos(t0) * r, cy + MathF.Sin(t0) * r, cx + MathF.Cos(t1) * r, cy + MathF.Sin(t1) * r, 0.6f, Pal.Crack, 0.7f);
            }
        // 28 스텐실 갈매기 (바닥 방향 표시)
        (cx, cy) = SlotC(28);
        for (int k = 0; k < 3; k++)
        {
            float ox = cx - 14 + k * 12;
            p.Paint(ox - 8, cy - 14, ox + 8, cy + 14, (x, y) =>
            {
                float d = MathF.Abs(x - ox - (8f - MathF.Abs(y - cy) * 0.75f)) - 2.4f;
                float worn = Noise.Fbm(x / 64f, y / 64f, 8, 3, 525);
                return (Pal.Stencil, d < 0f && MathF.Abs(y - cy) < 12f && worn > 0.35f ? 0.8f : 0f);
            });
        }
        // 29 발판 표시: 노란 동그라미 · 가운데 십자
        (cx, cy) = SlotC(29);
        p.Paint(cx - 22, cy - 22, cx + 22, cy + 22, (x, y) =>
        {
            float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            bool ring = MathF.Abs(d - 18f) < 2f, cross = (MathF.Abs(x - cx) < 1.2f || MathF.Abs(y - cy) < 1.2f) && d < 9f;
            return (Pal.HazardY, (ring || cross) && Noise.Fbm(x / 64f, y / 64f, 8, 3, 530) > 0.3f ? 0.85f : 0f);
        });
        // 30 녹물 흘러내림: 위에서 아래로 가는 주황 줄
        (cx, cy) = SlotC(30);
        for (int k = 0; k < 5; k++)
        {
            float ox = cx - 16 + k * 8 + (Noise.H01(k, 0, 535) - 0.5f) * 4, len = 20 + 30 * Noise.H01(k, 1, 535);
            p.Paint(ox - 3, cy - 30, ox + 3, cy - 30 + len, (x, y) =>
            {
                float t = (y - (cy - 30)) / len;
                float w = 1.8f * (1f - t * 0.6f);
                return (Pal.RustA.Lerp(Pal.RustB, Noise.H01((int)x, (int)y, 536)), MathF.Abs(x - ox) < w ? 0.75f * (1f - t * t) : 0f);
            });
        }
        // 31 덮개 나사 자국 (빠진 나사 구멍 넷 · 둘레 자국)
        (cx, cy) = SlotC(31);
        foreach (var (ox, oy) in new[] { (-18f, -18f), (18f, -18f), (-18f, 18f), (18f, 18f) })
        {
            p.Disc(cx + ox, cy + oy, 3.2f, Pal.Scratch, 0.5f);
            p.Disc(cx + ox, cy + oy, 1.8f, Pal.Gouge, 0.95f);
        }
        p.Shape(cx - 24, cy - 24, cx + 24, cy + 24, (x, y) => MathF.Abs(Sdf.Box(x - cx, y - cy, 22f, 22f, 2f)) - 0.6f, Pal.Gouge, 0.45f);
        return p;
    }

    private static void Tape(Pix p, float cx, float cy, float ang, float len, float wid, Rgb col, int seed)
    {
        float cs = MathF.Cos(ang), sn = MathF.Sin(ang);
        float r = len * 0.5f + wid;
        p.Paint(cx - r, cy - r, cx + r, cy + r, (x, y) =>
        {
            var (lx, ly) = Sdf.Rot(x - cx, y - cy, cs, sn);
            float tear = 1.6f * Noise.H01((int)(ly * 2f) + 50, seed, seed + 1);
            if (MathF.Abs(lx) > len * 0.5f - tear || MathF.Abs(ly) > wid * 0.5f) return (col, 0f);
            float weave = ((int)MathF.Floor(lx * 1.3f) + (int)MathF.Floor(ly * 1.3f)) % 2 == 0 ? 1.03f : 0.95f;
            float shade = 1f - MathF.Abs(ly) / wid * 0.25f;
            return (col * (weave * shade), 0.95f);
        });
    }

    private static void Note(Pix p, float cx, float cy, float ang, Rgb paper, bool pin, int seed)
    {
        float cs = MathF.Cos(ang), sn = MathF.Sin(ang);
        p.Paint(cx - 26, cy - 26, cx + 26, cy + 26, (x, y) =>
        {
            var (lx, ly) = Sdf.Rot(x - cx - 2.5f, y - cy - 3f, cs, sn);
            return (Pal.Heel, Sdf.Box(lx, ly, 17f, 17f, 1f) < 0f ? 0.28f : 0f); // 그림자
        });
        p.Paint(cx - 26, cy - 26, cx + 26, cy + 26, (x, y) =>
        {
            var (lx, ly) = Sdf.Rot(x - cx, y - cy, cs, sn);
            float d = Sdf.Box(lx, ly, 17f, 17f, 0.6f);
            bool fold = lx + ly > 26f; // 접힌 귀
            if (d > 0.5f || lx + ly > 29f) return (paper, 0f);
            var c = paper * (0.97f + 0.05f * Noise.H01((int)x, (int)y, seed));
            if (fold) c = paper * 0.82f;
            int row = (int)MathF.Floor((ly + 12f) / 5.5f);
            float lineY = -12f + row * 5.5f;
            if (row >= 0 && row < 5 && MathF.Abs(ly - lineY) < 0.6f && lx > -13f && lx < 13f - 6f * Noise.H01(row, 0, seed + 2)
                && MathF.Sin(lx * 1.7f + row) > -0.6f) c = Pal.Ink;
            return (c, Math.Clamp(0.5f - d, 0f, 1f));
        });
        if (pin)
        {
            p.Disc(cx + 1, cy - 13, 3f, Pal.Heel, 0.3f);
            p.Disc(cx, cy - 14, 2.6f, Pal.Pin, 1f);
            p.Disc(cx - 0.8f, cy - 14.8f, 0.9f, Pal.White, 0.8f);
        }
    }

    private static void Crack(Pix p, float x, float y, float ang, int steps, int seed)
    {
        for (int k = 0; k < steps; k++)
        {
            ang += (Noise.H01(k, 0, seed) - 0.5f) * 0.9f;
            float len = 4f + 3f * Noise.H01(k, 1, seed);
            float nx = x + MathF.Cos(ang) * len, ny = y + MathF.Sin(ang) * len;
            p.Line(x, y, nx, ny, 0.9f - k * 0.05f, Pal.Crack, 0.85f);
            if (Noise.H01(k, 2, seed) < 0.3f)
            {
                float ba = ang + (Noise.H01(k, 3, seed) < 0.5f ? 0.8f : -0.8f);
                p.Line(nx, ny, nx + MathF.Cos(ba) * 5f, ny + MathF.Sin(ba) * 5f, 0.5f, Pal.Crack, 0.7f);
            }
            x = nx; y = ny;
        }
    }

    // ───────────────────────── 입자 (32px 칸 8개) ─────────────────────────
    public const int PartSlot = 32, PartCount = 8;

    public static Pix Particles()
    {
        var p = new Pix(PartSlot * PartCount, PartSlot);
        float C0(int k) => k * PartSlot + 16f;
        // 0 김: 부드러운 흰 덩어리 (가장자리 흐릿 · 안에 엷은 결)
        p.Paint(0, 0, 31, 31, (x, y) =>
        {
            float dx = x - 16f, dy = y - 16f, d = MathF.Sqrt(dx * dx + dy * dy) / 14f;
            float n = Noise.Fbm(x / 32f, y / 32f, 4, 3, 600);
            float a = Math.Clamp(1f - d, 0f, 1f); a = a * a * (0.6f + 0.6f * n);
            return (Pal.Steam, a);
        });
        // 1 연기: 진하고 울퉁불퉁한 회색 뭉치
        p.Paint(32, 0, 63, 31, (x, y) =>
        {
            float dx = x - C0(1), dy = y - 16f;
            float n = Noise.Fbm(x / 32f, y / 32f, 4, 4, 601);
            float d = MathF.Sqrt(dx * dx + dy * dy) / (11f + 5f * n);
            float a = Math.Clamp(1.1f - d, 0f, 1f);
            return (Pal.Smoke * (0.7f + 0.4f * n), MathF.Min(1f, a * 1.3f) * (0.55f + 0.45f * n));
        });
        // 2 물방울: 아래가 둥근 눈물 모양 · 반짝 점
        p.Shape(64, 0, 95, 31, (x, y) =>
        {
            float lx = x - C0(2), ly = y - 16f;
            float body = Sdf.Circle(lx, ly - 3f, 6f);
            float cone = MathF.Max(MathF.Abs(lx) - 6f * (ly + 10f) / 13f, MathF.Max(-(ly + 10f), ly - 3f));
            return MathF.Min(body, cone);
        }, Pal.Drop, 0.9f);
        p.Disc(C0(2) - 2f, 16f, 1.6f, Pal.White, 0.95f);
        p.Disc(C0(2) + 2f, 21f, 1.2f, Pal.WaterEdge, 0.7f);
        // 3 튀는 물: 고리 + 튀는 알갱이
        p.Paint(96, 0, 127, 31, (x, y) =>
        {
            float dx = x - C0(3), dy = (y - 16f) * 1.6f, d = MathF.Sqrt(dx * dx + dy * dy);
            return (Pal.Drop, MathF.Abs(d - 11f) < 1.4f ? 0.8f : MathF.Abs(d - 6f) < 0.9f ? 0.4f : 0f);
        });
        for (int k = 0; k < 6; k++) p.Disc(C0(3) + MathF.Cos(k * 1.05f) * 13f, 16f + MathF.Sin(k * 1.05f) * 6f - 3f, 1.1f, Pal.Drop, 0.9f);
        // 4 불꽃: 가늘고 긴 빛줄기 (흰 심 · 노란 띠 · 주황 꼬리)
        p.Paint(128, 0, 159, 31, (x, y) =>
        {
            float lx = x - C0(4), ly = y - 16f;
            float along = (lx + 14f) / 28f;
            if (along < 0f || along > 1f) return (Pal.Spark, 0f);
            float w = 0.6f + 1.8f * along;
            float d = MathF.Abs(ly) / w;
            if (d > 1.6f) return (Pal.Spark, 0f);
            var c = d < 0.45f ? Pal.White : Pal.Spark.Lerp(Pal.Ember, 1f - along);
            return (c, Math.Clamp(1.6f - d, 0f, 1f) * (0.3f + 0.7f * along));
        });
        // 5 불씨: 빛나는 알갱이 (주황 테두리 빛)
        p.Paint(160, 0, 191, 31, (x, y) =>
        {
            float dx = x - C0(5), dy = y - 16f, d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < 2.4f) return (Pal.Spark.Lerp(Pal.White, 0.5f), 1f);
            return (Pal.Ember, Math.Clamp(1f - (d - 2.4f) / 9f, 0f, 1f) * 0.6f);
        });
        // 6 먼지: 작은 알갱이 몇 개 (빛 속에서 반짝)
        for (int k = 0; k < 7; k++)
        {
            float px = C0(6) + (Noise.H01(k, 0, 610) - 0.5f) * 22f, py = 16f + (Noise.H01(k, 1, 610) - 0.5f) * 22f;
            p.Disc(px, py, 0.7f + Noise.H01(k, 2, 610) * 1.1f, Pal.DustLight, 0.85f);
        }
        p.Disc(C0(6), 16f, 7f, Pal.DustLight, 0.12f);
        // 7 결로 방울: 둥근 물방울 · 굴절 반짝 · 아래 맺힌 그늘
        p.Disc(C0(7) + 1f, 17.5f, 7f, Pal.WaterEdge, 0.45f);
        p.Disc(C0(7), 16f, 6.5f, Pal.Drop, 0.75f);
        p.Disc(C0(7) + 1.5f, 18f, 4.5f, Pal.Water * 1.1f, 0.6f);
        p.Disc(C0(7) - 2.2f, 13.2f, 1.8f, Pal.White, 1f);
        return p;
    }
}
