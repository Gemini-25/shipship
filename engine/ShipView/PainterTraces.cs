using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 재질 흔적 (그래픽 결정 5) — 사고와 세월로 쌓이고 남는다 · 모두 세계의 값: ④ 벽의 부식(녹 — 윗면 얼룩 · 앞면으로 흘러내린 자국) ·
/// ④ 벽의 피로(실금) · ② 바닥에 고인 액체마다 빛깔 (암모니아 냉매는 옅은 청록 · 배터리 전해액은 호박색 — 물 · 그을음은 따로 그린다).
/// 청소 · 수리로 세계의 값이 줄면 흔적도 옅어진다.
/// </summary>
public sealed partial class Painter
{
    public const float RustMin = 0.05f, CrackMin = 0.3f;

    private void WallTraces(Img img, Structure st, int x0, int y0, int w, int h)
    {
        var rust = Rgba.Hex("#8a4a22"); var rustDark = Rgba.Hex("#5c2e14"); var crack = Rgba.Hex("#14171c");
        for (int y = y0 - 1; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (L.Kind[c] != CellKind.Wall || !Mine(c)) continue;
                float cor = st.Corrode[c], fat = st.Fatigue[c];
                int px = (x - x0) * T, py = (y - y0) * T;
                if (cor >= RustMin)
                {
                    // 윗면: 녹 얼룩 (2×2 점 — 많이 먹을수록 짙고 넓게)
                    float a = Math.Min(0.85f, 0.25f + cor);
                    if (y >= y0)
                        for (int k = 0; k < (int)(6 + cor * 40); k++)
                        {
                            uint u = DotHash.U(c, k, 61);
                            img.Fill(px + (int)(u % (T - 2)), py + (int)((u >> 8) % (T - 2)), 2, 2, (u >> 16) % 3 == 0 ? rustDark : rust, a * (0.6f + 0.4f * ((u >> 20) % 4) / 3f));
                        }
                    // 앞면 (아래가 바닥이면): 흘러내린 녹 자국
                    if (L.In(x, y + 1) && L.Kind[L.Index(x, y + 1)] == CellKind.Floor)
                        for (int k = 0; k < (int)(2 + cor * 10); k++)
                        {
                            uint u = DotHash.U(c, k, 67);
                            int sx = px + 2 + (int)(u % (T - 4)), len = 3 + (int)((u >> 8) % (uint)(DotTiles.Face - 2));
                            for (int i = 0; i < len; i++) img.Blend(sx, py + T + i, i < 2 ? rustDark : rust, a * (1 - i / (float)(len + 2)));
                        }
                }
                if (fat >= CrackMin && y >= y0)
                {
                    // 실금: 피로가 클수록 길고 많다 (꺾이며 뻗는다)
                    int n = 1 + (int)((fat - CrackMin) * 6);
                    for (int k = 0; k < n; k++)
                    {
                        uint u = DotHash.U(c, k, 71);
                        float cx = px + 4 + u % (T - 8), cy = py + 4 + (u >> 8) % (T - 8), ang = (u >> 16) % 628 / 100f;
                        int len = 5 + (int)(fat * 14);
                        for (int i = 0; i < len; i++)
                        {
                            if (i % 4 == 3) ang += ((u >> (i % 24)) & 1) == 0 ? 0.6f : -0.6f;
                            cx += MathF.Cos(ang); cy += MathF.Sin(ang);
                            img.Blend((int)cx, (int)cy, crack, 0.75f);
                        }
                    }
                }
            }
    }

    /// <summary>바닥에 고인 액체 (물은 따로 · 설비가 없는 칸만): 빛깔 · 짙기는 몰 수로.</summary>
    private void FloorLiquids(Img img, Field f, int c, int px, int py, int frame)
    {
        if (L.FurnitureAt[c] >= 0) return;   // 설비 칸의 저장은 설비의 몸 (배터리 셀의 전해액) — 쏟아진 것만 그린다
        Liquid(f.T.sNH3l, Rgba.Hex("#9fe3d2"), 40);
        Liquid(f.T.sEC, Rgba.Hex("#d9a441"), 20);
        void Liquid(int sIdx, Rgba col, double full)
        {
            if (sIdx < 0) return;
            double mol = f.Stored(sIdx, c);
            if (mol < 0.5) return;
            float a = (float)Math.Min(0.75, 0.2 + mol / full * 0.55);
            // 웅덩이 (칸마다 다른 모양 · 가장자리 밝게)
            float cx = px + T / 2 + (DotHash.F(c, sIdx, 3) - 0.5f) * 8, cy = py + T / 2 + (DotHash.F(c, sIdx, 4) - 0.5f) * 6;
            float rx = (float)Math.Min(14, 5 + mol / full * 9), ry = rx * 0.6f;
            img.Oval(cx, cy, rx, ry, col, a);
            img.Oval(cx - rx * 0.3f, cy - ry * 0.3f, rx * 0.35f, ry * 0.3f, Rgba.Hex("#ffffff"), a * 0.35f);   // 빛 받은 자리
        }
    }
}
