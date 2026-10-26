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

    /// <summary>곰팡이 단 여섯 (32도트 · 굽는 덩어리 모두 이 값으로 그린다 — 도장에 단 번호 · 천천히 자라는 값이 장마다 다시 굽게 하지 않는다 · 단마다 구운 점 목록).</summary>
    private static readonly float[] MoldSteps = { 0.05f, 0.1f, 0.2f, 0.4f, 0.7f, 1f };
    private static int MoldIdx(float m) { int i = 0; while (i < MoldSteps.Length && m >= MoldSteps[i]) i++; return i; }
    private static readonly Rgba[] MoldCol = { Rgba.Hex("#1f2a1a"), Rgba.Hex("#2e3b22"), Rgba.Hex("#3f4a2a") };

    /// <summary>그 칸에 그릴 곰팡이 (없거나 다른 가구 칸이면 0 — 화장실 설비 칸은 그린다).</summary>
    private float MoldAt(Life? life, int c) =>
        life == null || c >= life.Mold.Length || life.Mold[c] < MoldMin || L.FurnitureAt[c] >= 0 && _washKind[L.FurnitureAt[c]] == 0 ? 0 : life.Mold[c];

    /// <summary>곰팡이 점 하나 (칸 안 자리 · 한 번 섞은 빛깔 · 알파).</summary>
    private readonly record struct MoldDot(byte X, byte Y, byte R, byte G, byte B, byte A);
    /// <summary>구운 곰팡이 점 목록: 단 여섯 × 칸 무늬 64 (칸 자리 8칸 주기 — 바닥 타일과 같다) × 화장실 설비 칸 (A3 검토 — 칸마다 점 수백 개를 소수로 섞던 것: 곰팡이 1인 배 32도트 칸 덮개 0.08 → 2.2~3.9ms).</summary>
    private static readonly MoldDot[]?[] MoldMarks = new MoldDot[]?[6 * 64 * 2];

    /// <summary>칸 c의 곰팡이 점 목록 (없으면 null) — 32도트 Draw는 칸을 고르는 고리 (한 스레드)에서 고르고 바닥 타일과 함께 칸 줄마다 여러 코어에서 찍는다.</summary>
    private MoldDot[]? MoldMarksAt(Life? life, int c)
    {
        int i = MoldSlot(life, c);
        if (i < 0) return null;
        return MoldMarks[i] ??= MoldBake(MoldSteps[i >> 7], i >> 1 & 63, (i & 1) != 0);   // 다른 스레드가 함께 구워도 같은 목록 (참조 쓰기는 한 번에)
    }

    /// <summary>칸 c의 곰팡이 칸 (단 × 칸 무늬 64 × 화장실 설비 칸 — (단 · 64 + 무늬) · 2 + 설비) · 곰팡이가 없으면 −1.</summary>
    private int MoldSlot(Life? life, int c)
    {
        float m = MoldAt(life, c);
        if (m <= 0) return -1;
        int idx = MoldIdx(m) - 1, seed = (L.X(c) & 7) | (L.Y(c) & 7) << 3, fix = L.FurnitureAt[c] >= 0 ? 1 : 0;
        return (idx * 64 + seed) * 2 + fix;
    }

    /// <summary>구운 곰팡이 판 (투명한 칸 판에 점 목록을 섞은 것 — 곰팡이 칸마다 한 번): 넓은 보기 문 칸이 줄여 얹는다 (DownOf — 장마다 32도트 판을 지우고 섞어 줄이던 것과 같은 그림 ·
    /// 예산 고침: 곰팡이 1 · 안개인 새뜰호 넓은 보기 '문 · 물건 · 칸 덮개' 단계 16 · 8도트 0.55 · 0.65~0.77 → 0.32~0.38 · 0.26~0.44ms).</summary>
    private static readonly Img?[] MoldTiles = new Img?[6 * 64 * 2];
    /// <summary>시험용: 넓은 보기 문 칸 곰팡이를 예전처럼 장마다 32도트 판에 섞어 줄인다 (구운 판과 같은 그림인지 견준다).</summary>
    public static bool NoMoldTile;
    private Img? MoldTileAt(Life? life, int c)
    {
        int i = MoldSlot(life, c);
        if (i < 0) return null;
        if (MoldTiles[i] is { } done) return done;
        var t = new Img(T, T);
        MoldPut(t, MoldMarksAt(life, c)!, 0, 0);
        return MoldTiles[i] = t;
    }

    /// <summary>칸 c (자리 px, py)의 곰팡이 — 문 칸 (32도트 칸 덮개 · 넓은 보기 DoorStain)이 쓴다 (바닥 칸은 Draw가 바닥 타일과 함께).</summary>
    private void MoldOn(Img img, Life? life, int c, int px, int py)
    {
        if (MoldMarksAt(life, c) is { } dots) MoldPut(img, dots, px, py);
    }

    /// <summary>점 목록을 칸 (px, py)에 섞는다 — 불투명한 바탕은 정수로 반올림해 섞고 (Blend의 버림과 한 단 안) · 투명한 바탕 (문짝 위 얼룩 판)은 Blend 그대로.
    /// 처음부터 다 옮긴 기계어로 (그리기 예산 고침 — 물체 길 Structure.Hot과 같은 까닭): 곰팡이는 며칠 뒤에야 피어 이 고리가 처음 도는 때가 늦다 — 단계 번역이 그 뒤 몇 초를
    /// 덜 옮긴 코드 (점마다 X · Y · R · G · B · A 읽기가 따로 부르기)로 돌려 곰팡이 1인 배 32도트 곰팡이 단계가 코어 하나에서 +2.6ms (단계 번역을 끄면 +0.5ms) · CI에서 +0.8 ~ +4.1ms였다. 그림은 그대로다.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static void MoldPut(Img img, MoldDot[] dots, int px, int py)
    {
        var d = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(img.Px.AsSpan());
        foreach (var q in dots)
        {
            int x = px + q.X, y = py + q.Y;
            if ((uint)x >= (uint)img.W || (uint)y >= (uint)img.H) continue;
            ref uint v = ref d[y * img.W + x];
            if (v >> 24 != 255) { img.Blend(x, y, new Rgba(q.R, q.G, q.B, q.A)); continue; }
            uint a = q.A, ia = 255 - a;   // (t · 0x8081) >> 23 = t / 255 (t < 65536 — 몫이 꼭 같다)
            uint r = ((q.R * a + (v & 255) * ia + 127) * 0x8081u) >> 23, g = ((q.G * a + (v >> 8 & 255) * ia + 127) * 0x8081u) >> 23, b = ((q.B * a + (v >> 16 & 255) * ia + 127) * 0x8081u) >> 23;
            v = r | g << 8 | b << 16 | 0xFF000000u;
        }
    }

    /// <summary>곰팡이 점 목록 굽기: 투명한 칸 판에 MoldStain을 섞어 (겹친 점은 한 빛깔로) 보이는 점만 모은다.</summary>
    private static MoldDot[] MoldBake(float mold, int seed, bool fixture)
    {
        var t = new Img(T, T);
        MoldStain(t, mold, seed, fixture);
        var dots = new List<MoldDot>();
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                var q = t.Get(x, y);
                if (q.A > 0) dots.Add(new MoldDot((byte)x, (byte)y, q.R, q.G, q.B, q.A));
            }
        return dots.ToArray();
    }

    /// <summary>곰팡이 얼룩: 녹 얼룩 꼴 (2×2 점 · DotHash.U — 칸 무늬마다 늘 같은 자리) 검푸른 점이 칸마다 세 무리로 핀다 — 점 4 + 60 × 곰팡이 · 짙기 0.35 + 0.5 × 곰팡이 · 빛깔 셋.
    /// 화장실 설비 칸 (fixture)은 칸 가장자리 4도트와 16도트 타일의 가운데 줄눈에만 — 바닥에 피므로 설비 몸 · 아래 줄 설비가 솟은 몸 · 벽 앞면 · 문짝이 앞에 서서 가린다 (A3 검토 — 가린 쪽보다 나중에 그려 몸 위에 앉았다).</summary>
    private static void MoldStain(Img img, float mold, int seed, bool fixture)
    {
        mold = Math.Min(1, mold);
        float a = 0.35f + 0.5f * mold;
        int n = (int)(4 + 60 * mold), r = 3 + (int)(mold * 8);
        for (int k = 0; k < n; k++)
        {
            uint u = DotHash.U(seed, k, 73);
            int sx, sy;
            if (!fixture)
            {
                uint g = DotHash.U(seed, (int)(u % 3), 79);   // 무리 셋 (칸 무늬마다 같은 자리 · 곰팡이가 많을수록 넓게)
                sx = Math.Clamp(4 + (int)(g % (T - 10)) + (int)((u >> 4) % (uint)(2 * r + 1)) - r, 0, T - 2);
                sy = Math.Clamp(4 + (int)((g >> 8) % (T - 10)) + (int)((u >> 10) % (uint)(2 * r + 1)) - r, 0, T - 2);
            }
            else
            {
                int along = (int)((u >> 4) % (T - 1)), side = (int)((u >> 12) % 3);
                switch ((u >> 16) % 6)   // 위 · 아래 · 왼 · 오른 가장자리 (4도트 안) · 가운데 줄눈 (가로 · 세로)
                {
                    case 0: sx = along; sy = side; break;
                    case 1: sx = along; sy = T - 2 - side; break;
                    case 2: sx = side; sy = along; break;
                    case 3: sx = T - 2 - side; sy = along; break;
                    case 4: sx = along; sy = 15; break;
                    default: sx = 15; sy = along; break;
                }
            }
            img.Fill(sx, sy, 2, 2, MoldCol[(u >> 20) % 3], a * (0.85f + 0.05f * ((u >> 24) % 4)));
        }
    }

    /// <summary>웅덩이 한 장 (타원 · 빛 받은 자리) — 쏟은 액체 · 화장실 설비 칸 발치의 물이 함께 쓴다.</summary>
    private static void Puddle(Img img, float cx, float cy, float rx, float ry, Rgba col, float a)
    {
        img.Oval(cx, cy, rx, ry, col, a);
        img.Oval(cx - rx * 0.3f, cy - ry * 0.3f, rx * 0.35f, ry * 0.3f, Rgba.Hex("#ffffff"), a * 0.35f);   // 빛 받은 자리
    }
}
