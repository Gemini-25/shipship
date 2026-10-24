using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 재질 흔적 (그래픽 결정 5) — 사고와 세월로 쌓이고 남는다 · 모두 세계의 값: ④ 벽의 부식(녹 — 윗면 얼룩 · 앞면으로 흘러내린 자국) ·
/// ④ 벽의 피로(실금) · ② 바닥에 고인 액체마다 빛깔 (암모니아 냉매는 옅은 청록 · 배터리 전해액은 호박색 — 물 · 그을음은 따로 그린다) ·
/// 곰팡이 (5단 A3 — ⑦ Life.Mold: 습한 칸에 저절로 핀 검푸른 점 무리 · 화장실 설비 칸은 줄눈 · 가장자리에만).
/// 청소 · 수리로 세계의 값이 줄면 흔적도 옅어진다 (닦으면 곰팡이 0 → 다음 장에 사라진다).
/// </summary>
public sealed partial class Painter
{
    public const float RustMin = 0.05f, CrackMin = 0.3f, MoldMin = 0.05f;

    private void WallTraces(Img img, Structure st, int x0, int y0, int w, int h)
    {
        var rust = Rgba.Hex("#8a4a22"); var rustDark = Rgba.Hex("#5c2e14"); var crack = Rgba.Hex("#14171c");
        int hb = _bake ? 1 : 0;   // 굽는 덩어리는 둘레 한 칸까지 (이웃 칸의 실금이 덩어리 경계를 넘어온다)
        for (int y = y0 - 1; y < y0 + h + hb; y++)
            for (int x = x0 - hb; x < x0 + w + hb; x++)
            {
                if (!L.In(x, y)) continue;
                int c = L.Index(x, y);
                if (L.Kind[c] != CellKind.Wall || !Mine(c)) continue;
                float cor = st.Corrode[c], fat = st.Fatigue[c];
                if (_bake) { cor = (int)(cor * 40) / 40f; fat = (int)(fat * 84) / 84f; }   // 굽는 덩어리는 도장과 같은 단으로 (그림이 도장만으로 정해진다)
                int px = (x - x0) * T, py = (y - y0) * T;
                if (cor >= RustMin)
                {
                    // 윗면: 녹 얼룩 (2×2 점 — 많이 먹을수록 짙고 넓게)
                    float a = Math.Min(0.85f, 0.25f + cor);
                    bool faceBelow = L.In(x, y + 1) && L.Kind[L.Index(x, y + 1)] == CellKind.Floor;
                    int capRows = faceBelow ? ArtWalls.CapH - 2 : T - 2;   // 얼룩은 뚜껑에 (앞면엔 흘러내린 자국)
                    if (y >= y0)
                        for (int k = 0; k < (int)(6 + cor * 40); k++)
                        {
                            uint u = DotHash.U(c, k, 61);
                            img.Fill(px + (int)(u % (T - 2)), py + (int)((u >> 8) % (uint)capRows), 2, 2, (u >> 16) % 3 == 0 ? rustDark : rust, a * (0.6f + 0.4f * ((u >> 20) % 4) / 3f));
                        }
                    // 앞면 (아래가 바닥이면): 흘러내린 녹 자국
                    if (L.In(x, y + 1) && L.Kind[L.Index(x, y + 1)] == CellKind.Floor)
                        for (int k = 0; k < (int)(2 + cor * 10); k++)
                        {
                            uint u = DotHash.U(c, k, 67);
                            int sx = px + 2 + (int)(u % (T - 4)), len = 5 + (int)((u >> 8) % (uint)(ArtWalls.FaceH - 8));
                            for (int i = 0; i < len; i++) img.Blend(sx, py + ArtWalls.CapH + 2 + i, i < 2 ? rustDark : rust, a * (1 - i / (float)(len + 2)));
                            img.Blend(sx - 1, py + ArtWalls.CapH + ArtWalls.FaceH - 3, rust, a * 0.5f); img.Blend(sx + 1, py + ArtWalls.CapH + ArtWalls.FaceH - 3, rust, a * 0.5f);   // 굽도리에 고인다
                        }
                }
                if (fat >= CrackMin && y >= y0 - hb)
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
            Puddle(img, cx, cy, rx, ry, col, a);
        }
    }

    /// <summary>곰팡이 단 여섯 (굽는 덩어리는 이 값으로 그린다 — 도장에 단 번호 · 천천히 자라는 값이 장마다 다시 굽게 하지 않는다).</summary>
    private static readonly float[] MoldSteps = { 0.05f, 0.1f, 0.2f, 0.4f, 0.7f, 1f };
    private static int MoldIdx(float m) { int i = 0; while (i < MoldSteps.Length && m >= MoldSteps[i]) i++; return i; }
    private static float MoldStep(float m) { int i = MoldIdx(m); return i == 0 ? m : MoldSteps[i - 1]; }
    private static readonly Rgba[] MoldCol = { Rgba.Hex("#1f2a1a"), Rgba.Hex("#2e3b22"), Rgba.Hex("#3f4a2a") };

    /// <summary>그 칸에 그릴 곰팡이 (없거나 다른 가구 칸이면 0 — 화장실 설비 칸은 그린다).</summary>
    private float MoldAt(Life? life, int c) =>
        life == null || c >= life.Mold.Length || life.Mold[c] < MoldMin || L.FurnitureAt[c] >= 0 && _washKind[L.FurnitureAt[c]] == 0 ? 0 : life.Mold[c];

    /// <summary>칸 c (자리 px, py)의 곰팡이 얼룩 — 32도트 칸 덮개 · 굽는 덩어리 (step: 단 값으로) · 넓은 보기 문 칸이 함께 쓴다.</summary>
    private void MoldOn(Img img, Life? life, int c, int px, int py, bool step)
    {
        float m = MoldAt(life, c);
        if (m <= 0) return;
        int fu = L.FurnitureAt[c];
        MoldStain(img, px, py, step ? MoldStep(m) : m, c, fu >= 0 ? WashBodyImg(life, fu) : null, fu >= 0 ? WashRise(_washKind[fu]) : 0);
    }

    /// <summary>곰팡이 얼룩: 녹 얼룩 꼴 (2×2 점 · DotHash.U — 늘 같은 자리) 검푸른 점이 칸마다 세 무리로 핀다 — 점 4 + 60 × 곰팡이 · 짙기 0.35 + 0.5 × 곰팡이 · 빛깔 셋.
    /// fixture (화장실 설비 몸 그림 · 위로 rise 솟음)가 있으면 칸 가장자리 4도트와 16도트 타일의 가운데 줄눈 (설비 몸이 덮지 않은 곳)에만.</summary>
    private static void MoldStain(Img img, int px, int py, float mold, int c, Img? fixture, int rise)
    {
        mold = Math.Min(1, mold);
        float a = 0.35f + 0.5f * mold;
        int n = (int)(4 + 60 * mold), r = 3 + (int)(mold * 8);
        for (int k = 0; k < n; k++)
        {
            uint u = DotHash.U(c, k, 73);
            int sx, sy;
            if (fixture == null)
            {
                uint g = DotHash.U(c, (int)(u % 3), 79);   // 무리 셋 (칸마다 같은 자리 · 곰팡이가 많을수록 넓게)
                sx = Math.Clamp(4 + (int)(g % (T - 10)) + (int)((u >> 4) % (uint)(2 * r + 1)) - r, 0, T - 2);
                sy = Math.Clamp(4 + (int)((g >> 8) % (T - 10)) + (int)((u >> 10) % (uint)(2 * r + 1)) - r, 0, T - 2);
            }
            else
            {
                int along = (int)((u >> 4) % (T - 1)), side = (int)((u >> 12) % 3);
                switch ((u >> 16) % 6)   // 위 · 아래 · 왼 · 오른 가장자리 (4도트 안) · 가운데 줄눈 (가로 · 세로 — 설비 몸이 덮은 곳은 건너뛴다)
                {
                    case 0: sx = along; sy = side; break;
                    case 1: sx = along; sy = T - 2 - side; break;
                    case 2: sx = side; sy = along; break;
                    case 3: sx = T - 2 - side; sy = along; break;
                    case 4: sx = along; sy = 15; break;
                    default: sx = 15; sy = along; break;
                }
                if ((u >> 16) % 6 >= 4 && (fixture.Get(sx, sy + rise).A > 0 || fixture.Get(sx + 1, sy + 1 + rise).A > 0)) continue;
            }
            img.Fill(px + sx, py + sy, 2, 2, MoldCol[(u >> 20) % 3], a * (0.85f + 0.05f * ((u >> 24) % 4)));
        }
    }

    /// <summary>웅덩이 한 장 (타원 · 빛 받은 자리) — 쏟은 액체 · 화장실 설비 칸 발치의 물이 함께 쓴다.</summary>
    private static void Puddle(Img img, float cx, float cy, float rx, float ry, Rgba col, float a)
    {
        img.Oval(cx, cy, rx, ry, col, a);
        img.Oval(cx - rx * 0.3f, cy - ry * 0.3f, rx * 0.35f, ry * 0.3f, Rgba.Hex("#ffffff"), a * 0.35f);   // 빛 받은 자리
    }
}
