using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.22 재배대는 놓인 방에 따라 기르는 것이 다르다 — 그림도 다르다.
/// 조류 배양관(유리관 줄 · 초록 물 · 올라가는 거품 · 백색 조명) · 버섯 배지 선반(매달린 배지 봉지 · 갓 · 분무 안개 · 포자)
/// · 단백질 배양조(둥근 스테인리스 통 · 들여다보는 창 · 도는 교반기 · 온도계) · 정원 허브 화분(나무 상자 · 흙 · 잎 모양 셋 · 꽃).
/// 몸체는 캐시(정적) · 삶(자람 · 거품 · 안개)은 매 장면 · 손질(볼트 · 밸브 · 이름표)은 가까이서.
/// </summary>
public static partial class FixtureArt
{
    private static readonly Color AlgaeDeep = new("#1f6b3a"), AlgaeBright = new("#62d36b"), AlgaeDead = new("#5c5a2e");
    private static readonly Color BagPale = new("#d9cfb7"), CapTan = new("#b9895a"), CapPale = new("#efe2c8"), Gill = new("#7a5a3a");
    private static readonly Color Steel = new("#8e9aa6"), SteelDark = new("#3c4652"), Culture = new("#e2a9a0"), CultureDense = new("#c77d72");
    private static readonly Color Planter = new("#6b4a2e"), PlanterDark = new("#4a321f");

    private static CropKind Kind(in Fix x) => FoodSourceSystem.Crop(x.F);

    /// <summary>채소가 아닌 재배대의 몸체. 그렸으면 true.</summary>
    internal static bool CropBody(in Fix x)
    {
        switch (Kind(x))
        {
            case CropKind.Algae: AlgaeBody(x); return true;
            case CropKind.Mushroom: MushBody(x); return true;
            case CropKind.Protein: VatBody(x); return true;
            case CropKind.Herb: HerbBody(x); return true;
            default: return false;
        }
    }

    internal static bool CropLife(in Fix x)
    {
        switch (Kind(x))
        {
            case CropKind.Algae: AlgaeLife(x); return true;
            case CropKind.Mushroom: MushLife(x); return true;
            case CropKind.Protein: VatLife(x); return true;
            case CropKind.Herb: HerbLife(x); return true;
            default: return false;
        }
    }

    internal static bool CropFine(in Fix x)
    {
        var k = Kind(x);
        if (k == CropKind.Veg) return false;
        var ci = x.Ci;
        Bolts(ci, x.B, 2f, 0.55f);
        switch (k)
        {
            case CropKind.Algae: // 관마다 번호 · 위쪽 밸브 손잡이
                for (int i = 0; i < Tubes(x); i++) Tag(ci, x.P(0.1f + 0.8f * i / Mathf.Max(1, Tubes(x) - 1), 0.9f), (i + 1).ToString(), 4, new Color(1, 1, 1, 0.35f));
                Knob(ci, x.P(0.97f, 0.12f), 1.6f, 0.4f, new Color("#c0392b"));
                break;
            case CropKind.Mushroom: // 봉지마다 접종 날짜 꼬리표
                for (int i = 0; i < Bags(x); i++) ci.Box(new Rect2(x.P(0.08f + 0.84f * i / Mathf.Max(1, Bags(x) - 1), 0.78f) - new Vector2(1.5f, 0f), new Vector2(3f, 2f)), new Color("#f2e6a0").WithAlpha(0.7f));
                break;
            case CropKind.Protein: // 압력계 · 시료 꼭지
                Gauge(ci, x.P(0.5f, 0.15f), 2.2f, 0.55f, new Color("#e05050"));
                Knob(ci, x.P(0.5f, 0.88f), 1.4f, 1.2f, Steel);
                break;
            case CropKind.Herb: // 화분 이름 막대
                for (int i = 0; i < 3; i++) Tag(ci, x.P(0.17f + 0.33f * i, 0.86f), i == 0 ? "바질" : i == 1 ? "민트" : "부추", 4, new Color(1, 1, 1, 0.4f));
                break;
        }
        return true;
    }

    // ─────────────── 조류 배양관 ───────────────

    private static int Tubes(in Fix x) => Mathf.Max(3, x.F.Width * 2);

    private static void AlgaeBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#14201b"), 4, new Color("#2f5a46"));
        Pipe(ci, x.P(0.02f, 0.1f), x.P(0.98f, 0.1f), 2.2f, new Color("#4a6a6a"));  // 위 머리관
        Pipe(ci, x.P(0.02f, 0.9f), x.P(0.98f, 0.9f), 2.2f, new Color("#3a5656"));  // 아래 모음관
        int n = Tubes(x);
        for (int i = 0; i < n; i++)
        {
            float u = 0.1f + 0.8f * i / Mathf.Max(1, n - 1);
            var top = x.P(u, 0.16f);
            var bot = x.P(u, 0.84f);
            var r = new Rect2(new Vector2(Mathf.Min(top.X, bot.X) - 2.2f, Mathf.Min(top.Y, bot.Y) - 1f), new Vector2(Mathf.Abs(bot.X - top.X) + 4.4f, Mathf.Abs(bot.Y - top.Y) + 2f));
            Box(ci, r, new Color("#0c1512"), 2, new Color("#8fb8a8").WithAlpha(0.55f));
        }
        ci.Box(x.Q(0.02f, 0.94f, 0.98f, 0.98f), new Color("#d8e8ff").WithAlpha(0.25f)); // 백색 조명 띠
    }

    private static void AlgaeLife(in Fix x)
    {
        var ci = x.Ci;
        bool alive = x.Eff > 0.01f && !x.Dead;
        if (x.M?.Crop is not CropState crop) return;
        int n = Tubes(x);
        var water = (crop.Growth < 0.05f ? new Color("#7fb7a0") : AlgaeBright.Lerp(AlgaeDeep, crop.Growth)) * (alive ? 1f : 0.7f);
        if (!alive && crop.DryHours > 6f) water = AlgaeDead;
        for (int i = 0; i < n; i++)
        {
            float u = 0.1f + 0.8f * i / Mathf.Max(1, n - 1);
            float fill = 0.35f + 0.45f * Mathf.Min(1f, crop.Growth + 0.15f);
            var top = x.P(u, 0.84f - 0.68f * fill);
            var bot = x.P(u, 0.84f);
            Line(ci, top, bot, water.WithAlpha(0.85f), 3.4f);
            Line(ci, top, bot, new Color(1, 1, 1, 0.12f), 1f); // 유리 반사
            if (crop.Ripe) Dot(ci, top, 1.8f, new Color("#c9f0b0").WithAlpha(0.8f)); // 위에 뜬 거품층
            if (alive && x.Lod > 0) // 올라가는 거품
                for (int b = 0; b < 2; b++)
                {
                    float ph = Mathf.PosMod(x.T * (0.5f + 0.13f * i) + b * 0.5f + Hash(x.Id, i, 7), 1f);
                    var p = bot.Lerp(top, ph);
                    Dot(ci, p + new Vector2(Mathf.Sin(x.T * 3f + i) * 0.5f, 0f), 0.7f, new Color(1, 1, 1, 0.55f * (1f - ph)));
                }
        }
        if (alive) ci.Box(x.Q(0.02f, 0.94f, 0.98f, 0.98f), new Color("#e8f4ff").WithAlpha(0.35f + 0.1f * Mathf.Sin(x.T * 0.8f)) * x.Glow);
        PaintCropMarks(x, crop);
    }

    // ─────────────── 버섯 배지 선반 ───────────────

    private static int Bags(in Fix x) => Mathf.Max(3, x.F.Width * 2);

    private static void MushBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1d1813"), 3, new Color("#4a3b2a"));
        for (float v = 0.22f; v < 0.95f; v += 0.36f) Line(ci, x.P(0.02f, v), x.P(0.98f, v), new Color("#6a5a46"), 1.6f); // 선반 살
        int n = Bags(x);
        for (int i = 0; i < n; i++)
        {
            float u = 0.08f + 0.84f * i / Mathf.Max(1, n - 1);
            var c = x.P(u, 0.5f);
            var bag = new Rect2(c - new Vector2(2.6f, 3.6f), new Vector2(5.2f, 7.2f));
            Box(ci, bag, BagPale.Darkened(0.15f * Hash(x.Id, i, 3)), 2, new Color("#8a7d63"));
            Line(ci, c + new Vector2(-1.5f, -3.4f), c + new Vector2(1.5f, -3.4f), new Color("#5a4a36"), 1.2f); // 묶은 끈
            for (int s = 0; s < 3; s++) Dot(ci, c + new Vector2(Hash(x.Id, i * 3 + s, 5) * 4f - 2f, Hash(x.Id, i * 3 + s, 6) * 5f - 2f), 0.5f, new Color("#a89a7a")); // 톱밥 결
        }
        Pipe(ci, x.P(0.02f, 0.06f), x.P(0.98f, 0.06f), 1.4f, new Color("#4e6a7a"), false); // 분무관
    }

    private static void MushLife(in Fix x)
    {
        var ci = x.Ci;
        bool alive = x.Eff > 0.01f && !x.Dead;
        if (x.M?.Crop is not CropState crop) return;
        int n = Bags(x);
        for (int i = 0; i < n; i++)
        {
            float u = 0.08f + 0.84f * i / Mathf.Max(1, n - 1);
            var c = x.P(u, 0.5f);
            int caps = crop.Growth < 0.15f ? 0 : crop.Growth < 0.5f ? 2 : 3;
            for (int k = 0; k < caps; k++)
            {
                float side = k == 0 ? -1f : k == 1 ? 1f : 0f;
                var stem = c + new Vector2(side * 2.4f, -1f + k * 1.6f);
                float r = (0.8f + 2.4f * crop.Growth) * (0.8f + 0.3f * Hash(x.Id, i * 5 + k, 9));
                var cap = (crop.Care < 0.4f ? CapTan.Darkened(0.3f) : CapTan.Lerp(CapPale, 0.3f + 0.3f * Hash(x.Id, i, k))) * (alive ? 1f : 0.75f);
                Line(ci, stem, stem + new Vector2(side * r * 0.6f, 0f), CapPale.WithAlpha(0.8f), 0.9f);
                var p = stem + new Vector2(side * r * 0.8f, 0f);
                ci.Circle(p, r, cap, true, -1f, true);
                if (x.Lod > 0) Line(ci, p + new Vector2(-r * 0.7f, r * 0.2f), p + new Vector2(r * 0.7f, r * 0.2f), Gill.WithAlpha(0.6f), 0.6f); // 주름
            }
            if (crop.Blight > 0.1f) Dot(ci, c + new Vector2(0f, 2f), 1.4f, new Color("#3a6a3a").WithAlpha(0.7f)); // 푸른곰팡이
        }
        if (alive && x.Lod > 0) // 분무 안개 · 떠도는 포자
        {
            for (int m = 0; m < 4; m++)
            {
                float ph = Mathf.PosMod(x.T * 0.25f + m * 0.25f, 1f);
                Dot(ci, x.P(0.1f + 0.8f * Hash(x.Id, m, 11), 0.08f + ph * 0.5f), 2.2f + 2f * ph, new Color("#dfe8ee").WithAlpha(0.12f * (1f - ph)));
            }
            if (crop.Ripe)
                for (int s = 0; s < 5; s++)
                {
                    float ph = Mathf.PosMod(x.T * 0.15f + s * 0.2f, 1f);
                    Dot(ci, x.P(Hash(x.Id, s, 13), 0.6f - ph * 0.5f), 0.4f, new Color("#f0e0c0").WithAlpha(0.5f * (1f - ph)));
                }
        }
        PaintCropMarks(x, crop);
    }

    // ─────────────── 단백질 배양조 ───────────────

    private static void VatBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#161b20"), 4, SteelDark);
        int n = Mathf.Max(2, x.F.Width / 2);
        float rad = Mathf.Min(x.Lv * 0.36f, x.Lu / n * 0.38f);
        for (int i = 0; i < n; i++)
        {
            var c = x.P((i + 0.5f) / n, 0.5f);
            ci.Circle(c, rad, Steel.Darkened(0.35f), true, -1f, true);
            Ring(ci, c, rad, Steel, 1.6f);
            Ring(ci, c, rad * 0.62f, SteelDark, 1.2f); // 들여다보는 창 테
            ci.Arc(c, rad * 0.85f, 3.6f, 5f, 10, new Color(1, 1, 1, 0.2f), 1.2f, true);
            if (i < n - 1) Pipe(ci, c + new Vector2(rad, 0f), x.P((i + 1.5f) / n, 0.5f) - new Vector2(rad, 0f), 1.4f, Steel, false);
        }
        Pipe(ci, x.P(0.02f, 0.15f), x.P(0.98f, 0.15f), 1.2f, new Color("#a07a4a"), false); // 양분관
    }

    private static void VatLife(in Fix x)
    {
        var ci = x.Ci;
        bool alive = x.Eff > 0.01f && !x.Dead;
        if (x.M?.Crop is not CropState crop) return;
        int n = Mathf.Max(2, x.F.Width / 2);
        float rad = Mathf.Min(x.Lv * 0.36f, x.Lu / n * 0.38f) * 0.6f;
        var cult = Culture.Lerp(CultureDense, crop.Growth) * (alive ? 1f : 0.7f);
        for (int i = 0; i < n; i++)
        {
            var c = x.P((i + 0.5f) / n, 0.5f);
            float level = 0.25f + 0.7f * crop.Growth;
            ci.Circle(c, rad * Mathf.Sqrt(level), cult.WithAlpha(0.85f), true, -1f, true);
            if (alive) // 교반기 날개
            {
                float a = x.T * 2.2f + i;
                for (int b = 0; b < 3; b++)
                {
                    var d = Vector2.FromAngle(a + b * Mathf.Tau / 3f) * rad * 0.8f;
                    Line(ci, c, c + d, Steel.WithAlpha(0.8f), 0.9f);
                }
            }
            Dot(ci, c, 0.9f, SteelDark);
            if (crop.Ripe) Ring(ci, c, rad * 0.9f, new Color("#ffd0c0").WithAlpha(0.6f + 0.2f * Mathf.Sin(x.T * 2f)), 1f); // 다 됐다는 테두리 등
        }
        if (alive) Led(ci, x.P(0.96f, 0.85f), crop.Ripe ? new Color("#7fff8a") : new Color("#ffb347"), 0.6f + 0.4f * Mathf.Sin(x.T * 3f));
        PaintCropMarks(x, crop);
    }

    // ─────────────── 정원 허브 화분 ───────────────

    private static void HerbBody(in Fix x)
    {
        var ci = x.Ci;
        for (int i = 0; i < 3; i++)
        {
            var r = x.Q(0.02f + 0.33f * i, 0.12f, 0.31f + 0.33f * i, 0.88f);
            Box(ci, r, Planter, 2, PlanterDark);
            for (float v = 0.3f; v < 0.9f; v += 0.3f) Line(ci, x.P(0.02f + 0.33f * i, 0.12f + 0.76f * v), x.P(0.31f + 0.33f * i, 0.12f + 0.76f * v), PlanterDark.WithAlpha(0.6f), 0.6f); // 나뭇결
            Box(ci, new Rect2(r.Position + new Vector2(1.5f, 1.5f), r.Size - new Vector2(3f, 3f)), Soil, 1);
        }
    }

    private static void HerbLife(in Fix x)
    {
        var ci = x.Ci;
        bool alive = x.Eff > 0.01f && !x.Dead;
        if (x.M?.Crop is not CropState crop) return;
        float g = crop.Growth;
        for (int i = 0; i < 3; i++)
        {
            var c = x.P(0.165f + 0.33f * i, 0.5f);
            float sway = Mathf.Sin(x.T * 1.1f + i) * 0.5f;
            var leaf = Leaf.Lerp(LeafLight, crop.Care) * (alive ? 1f : 0.65f);
            if (i == 0) // 바질: 넓은 잎 넷
                for (int k = 0; k < 4; k++) ci.Circle(c + Vector2.FromAngle(k * 1.57f + 0.4f) * (1.5f + 2f * g) + new Vector2(sway, 0f), 1f + 1.6f * g, leaf, true, -1f, true);
            else if (i == 1) // 민트: 작은 잎 여럿
                for (int k = 0; k < 7; k++) Dot(ci, c + new Vector2(Hash(x.Id, k, 21) * 6f - 3f, Hash(x.Id, k, 22) * 6f - 3f) * (0.4f + 0.6f * g) + new Vector2(sway, 0f), 0.6f + 0.9f * g, leaf.Lightened(0.15f));
            else // 부추: 가는 줄기
                for (int k = 0; k < 6; k++) Line(ci, c + new Vector2(k - 2.5f, 2.5f), c + new Vector2(k - 2.5f + sway, 2.5f - (1.5f + 5f * g)), leaf.Darkened(0.1f), 0.7f);
            if (crop.Ripe) Dot(ci, c + new Vector2(2f + sway, -2.5f), 1f, i == 2 ? new Color("#e8e0ff") : new Color("#c8a0ff")); // 꽃
        }
        PaintCropMarks(x, crop);
    }

    /// <summary>병충해 · 마름 표시 (모든 재배대 공통 상태).</summary>
    private static void PaintCropMarks(in Fix x, CropState crop)
    {
        if (crop.DryHours > 8f) x.Ci.Box(x.Q(0.02f, 0.02f, 0.98f, 0.98f), new Color("#a08040").WithAlpha(Mathf.Min(0.25f, crop.DryHours / 120f)));
    }
}
