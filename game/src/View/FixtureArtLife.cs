using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5c 그림 표 — 생명 유지 · 재배 · 위생 (14종).
/// 산소 발생기(전해조 판 · O₂/H₂ 탱크 · 거품) · 정수기(여과 탱크 · 필터 통 · UV 관 · 수위창) · CO₂ 세정기(흡착 통 · 팬 · 색 변하는 알갱이)
/// · 공기 청정기(둥근 탑 · 동심 틈 · 퍼지는 바람) · 제습기(코일 · 물통 수위) · 재배대(흙 · 점적관 · 잎 · 생장등) · LED 생장판(방열 핀)
/// · 양액 조절기(병 셋 · 연동 펌프 롤러) · 식물 벽(펠트 주머니 · 잎 모양 셋) · 누설 감지기(감지 줄 · 경광등) · 제염 샤워(배수구 · 물줄기)
/// · 세탁기(둥근 창 · 도는 빨래) · 우주복 건조기(장화 기둥 · 더운 바람) · 에어락 회수 펌프(쌍 실린더 · 피스톤).
/// </summary>
public static partial class FixtureArt
{
    private static void LifeArt(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.OxygenGenerator] = new(OxygenBody, OxygenLife, OxygenFine, Look.Gas, 0.8f, 0.5f);
        t[FurnitureType.WaterRecycler] = new(RecyclerBody, RecyclerLife, RecyclerFine, Look.Leak, 0.5f, 0.9f);
        t[FurnitureType.Scrubber] = new(ScrubberBody, ScrubberLife, ScrubberFine, Look.Grind, 0.5f, 0.5f);
        t[FurnitureType.AirPurifier] = new(PurifierBody, PurifierLife, PurifierFine, Look.Smoke, 0.5f, 0.3f);
        t[FurnitureType.Dehumidifier] = new(DehumidBody, DehumidLife, DehumidFine, Look.Leak, 0.5f, 0.9f);
        t[FurnitureType.GrowBed] = new(GrowBody, GrowLife, GrowFine, Look.Leak, 0.1f, 0.15f);
        t[FurnitureType.LedPanel] = new(LedPanelBody, LedPanelLife, LedPanelFine, Look.Flicker, 0.5f, 0.5f);
        t[FurnitureType.NutrientDoser] = new(DoserBody, DoserLife, DoserFine, Look.Leak, 0.5f, 0.85f);
        t[FurnitureType.PlantWall] = new(PlantWallBody, PlantWallLife, PlantWallFine, Look.Jam, 0.5f, 0.5f);
        t[FurnitureType.LeakDetector] = new(LeakBody, LeakLife, LeakFine, Look.Flicker, 0.5f, 0.5f);
        t[FurnitureType.DeconShower] = new(DeconBody, DeconLife, DeconFine, Look.Leak, 0.5f, 0.15f);
        t[FurnitureType.WashingMachine] = new(WasherBody, WasherLife, WasherFine, Look.Grind, 0.5f, 0.5f);
        t[FurnitureType.SuitDryer] = new(DryerBody, DryerLife, DryerFine, Look.Heat, 0.5f, 0.75f);
        t[FurnitureType.AirlockPump] = new(AirlockPumpBody, AirlockPumpLife, AirlockPumpFine, Look.Steam, 0.3f, 0.3f);
    }

    private static readonly Color O2Blue = new("#5ec8e6");
    private static readonly Color H2White = new("#e8ecf2");
    private static readonly Color UvViolet = new("#b07aff");
    private static readonly Color GrowPink = new("#d77cff");
    private static readonly Color Felt = new("#2a2a1e");

    // ─────────────── 산소 발생기 ───────────────

    private static void OxygenBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#10201f"), 8, new Color("#1f4a4a"), 2);
        float tr = Mathf.Min(x.Lv * 0.36f, x.Lu * 0.17f);
        var o2 = x.P(0.2f, 0.5f);
        var h2 = x.P(0.82f, 0.5f);
        Pipe(ci, x.P(0.38f, 0.35f), o2, 2.2f, new Color("#2a6a6a"), false); // 기체 관
        Pipe(ci, x.P(0.62f, 0.65f), h2, 1.8f, new Color("#4a5566"), false);
        Can(ci, o2, tr, new Color("#163130"), O2Blue.WithAlpha(0.7f)); // 산소 탱크
        Ring(ci, o2, tr * 0.7f, O2Blue.WithAlpha(0.45f), 1.6f);
        Can(ci, h2, tr * 0.8f, new Color("#1c2026"), H2White.WithAlpha(0.6f)); // 수소 탱크 (작다)
        ci.Arc(h2, tr * 0.55f, 0f, Mathf.Pi, 10, new Color("#c0392b").WithAlpha(0.6f), 1.6f, true);
        var stack = x.Q(0.38f, 0.14f, 0.62f, 0.86f);
        Box(ci, stack, new Color("#0c1a1a"), 2, new Color("#2a5a5a")); // 전해조
        for (int k = 1; k < 9; k++) Line(ci, x.P(0.38f + k * 0.0267f, 0.16f), x.P(0.38f + k * 0.0267f, 0.84f), new Color("#3a7a7a").WithAlpha(0.7f), 1f); // 전극 판
        Pipe(ci, x.P(0.5f, 1.0f), x.P(0.5f, 0.86f), 2f, ColdPipe, false); // 물 들어오는 관
        Dot(ci, h2 + x.V * (tr * 0.85f), 1.6f, Brass); // 안전 밸브
    }

    private static void OxygenLife(in Fix x)
    {
        var ci = x.Ci;
        float tr = Mathf.Min(x.Lv * 0.36f, x.Lu * 0.17f);
        var o2 = x.P(0.2f, 0.5f);
        float pulse = Pulse(x.T, 1.8f);
        Dot(ci, o2, tr * (0.35f + 0.15f * pulse * x.Eff), O2Blue.WithAlpha((0.2f + 0.25f * pulse) * x.Glow + 0.04f));
        if (x.Lit) ci.Box(x.Q(0.39f, 0.16f, 0.61f, 0.84f), UvViolet.WithAlpha(0.06f * x.Glow)); // 전극 사이 희미한 빛
        if (!x.On && x.St != State.Fault) return;
        int n = x.Lod == 0 ? 3 : 7;
        for (int b = 0; b < n; b++) // 전해조 속 거품: 산소는 왼쪽 탱크로, 수소는 오른쪽으로
        {
            float ph = Mathf.PosMod(x.T * 0.5f * x.Spin + b / (float)n, 1f);
            bool toO2 = b % 3 != 0;
            float v = 0.2f + 0.6f * Hash(x.Id, b, 41);
            var from = x.P(0.5f, v);
            var to = toO2 ? x.P(0.4f, 0.35f) : x.P(0.6f, 0.65f);
            Dot(ci, from.Lerp(to, ph), toO2 ? 1.4f : 1f, (toO2 ? O2Blue : H2White).WithAlpha(0.7f * (1f - ph * 0.5f)));
        }
        if (x.Lod > 0 && Mathf.PosMod(x.T * 0.11f, 1f) < 0.1f) // 수소 안전 밸브가 가끔 뺀다
            Dot(ci, x.P(0.82f, 0.5f) + x.V * (tr * 0.85f) - new Vector2(0f, Mathf.PosMod(x.T * 0.11f, 1f) * 60f), 2.5f, H2White.WithAlpha(0.18f));
    }

    private static void OxygenFine(in Fix x)
    {
        var ci = x.Ci;
        float tr = Mathf.Min(x.Lv * 0.36f, x.Lu * 0.17f);
        Tag(ci, x.P(0.2f, 0.5f), "O₂", 7, O2Blue.WithAlpha(0.8f));
        Tag(ci, x.P(0.82f, 0.5f), "H₂", 6, H2White.WithAlpha(0.6f));
        for (int k = 0; k < 8; k++) Bolt(ci, x.P(0.2f, 0.5f) + Vector2.FromAngle(k * Mathf.Tau / 8f) * tr * 0.9f, 0.55f);
        Bolts(ci, x.B, 3f, 0.8f);
    }

    // ─────────────── 정수기 ───────────────

    private static void RecyclerBody(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R;
        var center = x.C;
        Gfx.RoundRect(ci, r.Grow(-4f), new Color("#122029"), 10, new Color("#2b4c63"), 2);
        float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.3f;
        Can(ci, center, rad, new Color("#0c151c"), new Color("#3f6f88")); // 여과 탱크
        ci.Arc(center, rad * 0.62f, 0f, Mathf.Tau, 24, new Color("#1f3a47"), 1.5f, true); // 막 고리
        Pipe(ci, new Vector2(r.Position.X + 3, center.Y), new Vector2(center.X - rad, center.Y), 4f, new Color("#35505f")); // 들어오는 관
        Pipe(ci, new Vector2(center.X + rad, center.Y), new Vector2(r.End.X - 3, center.Y), 4f, new Color("#35505f")); // 나가는 관
        foreach (var p in new[] { new Vector2(r.Position.X + 9f, r.Position.Y + 9f), new Vector2(r.Position.X + 9f, r.End.Y - 9f) }) // 필터 통 둘
        {
            Can(ci, p, 4f, new Color("#e8ecf2").WithAlpha(0.35f), new Color("#9fb4cc"));
            Ring(ci, p, 2f, new Color("#4a5566"), 0.8f, 10);
        }
        Line(ci, new Vector2(r.End.X - 9f, r.End.Y - 15f), new Vector2(r.End.X - 9f, r.End.Y - 6f), new Color("#3a2a5a"), 3f); // UV 관
        var sight = new Rect2(r.End.X - 7f, r.Position.Y + 16f, 3f, r.Size.Y * 0.35f);
        Box(ci, sight, GlassDark, 1, new Color("#3f6f88")); // 수위창
        var gp = new Vector2(r.End.X - 10, r.Position.Y + 9);
        Gauge(ci, gp, 3.6f, 0.55f, new Color("#c0392b"));
    }

    private static void RecyclerLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        var r = x.R;
        var c = x.C;
        float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.3f;
        float level = Mathf.Clamp(x.W.Water.Level / Mathf.Max(1f, x.W.Water.Capacity), 0f, 1f);
        Dot(ci, c, rad * 0.88f, Water.WithAlpha(0.15f + 0.35f * level));
        var sight = new Rect2(r.End.X - 6.5f, r.Position.Y + 16.5f, 2f, r.Size.Y * 0.35f - 1f);
        float h = sight.Size.Y * level;
        ci.Box(new Rect2(sight.Position.X, sight.End.Y - h, sight.Size.X, h), Water.WithAlpha(0.8f));
        if (x.Lit)
        {
            for (int k = 0; k < 2; k++) // 탱크 속 소용돌이
            {
                float a = x.Ang(1.6f) + k * Mathf.Pi;
                ci.Arc(c, rad * 0.5f, a, a + 1.2f, 10, WaterLight.WithAlpha(0.6f * x.Glow), 1.4f, true);
            }
            Line(ci, new Vector2(r.End.X - 9f, r.End.Y - 15f), new Vector2(r.End.X - 9f, r.End.Y - 6f), UvViolet.WithAlpha(0.8f * x.Glow), 2f); // UV 빛
            Dot(ci, new Vector2(r.End.X - 9f, r.End.Y - 10.5f), 5f, UvViolet.WithAlpha(0.12f * x.Glow));
        }
        if (x.On && x.Lod > 0) // 관 속 흐름
            for (int k = 0; k < 4; k++)
            {
                float ph = Mathf.PosMod(x.T * 0.9f * x.Spin + k / 4f, 1f);
                bool inlet = k % 2 == 0;
                var a = inlet ? new Vector2(r.Position.X + 3, c.Y) : new Vector2(c.X + rad, c.Y);
                var b = inlet ? new Vector2(c.X - rad, c.Y) : new Vector2(r.End.X - 3, c.Y);
                Dot(ci, a.Lerp(b, ph), 1.2f, (inlet ? new Color("#8a9a6a") : WaterLight).WithAlpha(0.85f)); // 흐린 물이 들어가 맑은 물이 나온다
            }
    }

    private static void RecyclerFine(in Fix x)
    {
        var ci = x.Ci;
        float rad = Mathf.Min(x.R.Size.X, x.R.Size.Y) * 0.3f;
        for (int k = 0; k < 10; k++) Bolt(ci, x.C + Vector2.FromAngle(k * Mathf.Tau / 10f) * rad * 0.92f, 0.6f);
        Tag(ci, x.C + new Vector2(0f, rad + 6f), "H₂O", 6, WaterLight.WithAlpha(0.6f));
    }

    // ─────────────── CO₂ 세정기 ───────────────

    private static void ScrubberBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#141c1f"), 5, new Color("#3f6f78"), 2);
        foreach (float u in new[] { 0.06f, 0.76f }) // 흡착 통 둘 (알갱이)
        {
            var cart = x.Q(u, 0.12f, u + 0.18f, 0.88f);
            Box(ci, cart, new Color("#e0dccc").WithAlpha(0.3f), 2, new Color("#6a7a7a"));
            for (int i = 0; i < 8; i++) Dot(ci, cart.Position + new Vector2(Hash(x.Id, i, 51), Hash(x.Id, i, 52)) * cart.Size, 0.7f, new Color("#c8c4b0").WithAlpha(0.5f));
        }
        float fr = Mathf.Min(x.Lu * 0.24f, x.Lv * 0.38f);
        Dot(ci, x.C, fr, new Color("#0b1215"));
        Ring(ci, x.C, fr, new Color("#2c4d55"), 1.5f);
        for (int k = 1; k < 4; k++) Ring(ci, x.C, fr * k / 4f, new Color("#1c2d33"), 0.8f, 16); // 팬 덮개 살
    }

    private static void ScrubberLife(in Fix x)
    {
        var ci = x.Ci;
        float fr = Mathf.Min(x.Lu * 0.24f, x.Lv * 0.38f);
        Fan(ci, x.C, fr * 0.9f, 4, x.Ang(6f) + 0.3f, new Color("#7fd0c0").WithAlpha(x.On ? 0.8f : 0.3f), 2.2f, 0.3f);
        float co2 = Mathf.Clamp(x.F.Room.Air.CO2 / 2f, 0f, 1f); // 방 CO₂가 높을수록 알갱이가 보라로 물든다
        var sat = new Color("#e0dccc").Lerp(new Color("#8a5ac8"), Mathf.Clamp(co2 + (x.M?.Wear ?? 0f) * 0.4f, 0f, 1f));
        foreach (float u in new[] { 0.06f, 0.76f }) ci.Box(x.Q(u + 0.02f, 0.6f, u + 0.16f, 0.86f), sat.WithAlpha(0.45f));
        if (x.On && x.Lod > 0)
            for (int k = 0; k < 4; k++) // 빨려 드는 CO₂ 알갱이
            {
                float ph = Mathf.PosMod(x.T * 0.7f + k / 4f, 1f);
                var from = x.C + Vector2.FromAngle(Hash(x.Id, k, 53) * Mathf.Tau) * x.B.Size.X * 0.7f;
                Dot(ci, from.Lerp(x.C, ph), 0.9f, new Color("#4a4a52").WithAlpha(0.8f * (1f - ph)));
            }
    }

    private static void ScrubberFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.55f);
        Tag(ci, x.P(0.15f, 0.06f) + x.V * 2f, "CO₂", 4, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 공기 청정기 ───────────────

    private static void PurifierBody(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.5f;
        Box(ci, x.B.Grow(-1f), new Color("#2a2e36"), 4, new Color("#4a525e")); // 받침
        Dot(ci, x.C + new Vector2(1.5f, 2f), rr, Shadow);
        Dot(ci, x.C, rr, new Color("#d8dce2"));
        ci.Arc(x.C, rr * 0.92f, -2.4f, -0.7f, 10, new Color("#5a626e"), 1.6f, true); // 손잡이 홈
        Ring(ci, x.C, rr, new Color("#8a929e"), 1.2f, 28);
        for (int k = 1; k < 5; k++) // 동심 틈 (끊긴 고리)
            for (int s = 0; s < 4; s++)
            {
                float a = s * Mathf.Pi / 2f + 0.15f;
                ci.Arc(x.C, rr * (0.25f + k * 0.16f), a, a + Mathf.Pi / 2f - 0.3f, 6, new Color("#6a727e"), 0.9f, true);
            }
        Dot(ci, x.C, rr * 0.2f, new Color("#3a4250"));
    }

    private static void PurifierLife(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.5f;
        bool clogged = x.M != null && x.M.Has(FaultKind.FilterClogged);
        Led(ci, x.C, clogged ? Danger : x.On ? new Color("#7fb8ff") : Palette.TextMuted, Mathf.Max(0.25f, x.Glow), 1.6f);
        if (!x.On) return;
        for (int k = 0; k < (x.Lod == 0 ? 1 : 2); k++) // 퍼져 나가는 맑은 바람
        {
            float ph = Mathf.PosMod(x.T * 0.4f * x.Spin + k * 0.5f, 1f);
            Ring(ci, x.C, rr * (1f + ph * 0.9f), new Color("#bfe0ff").WithAlpha(0.25f * (1f - ph)), 1f, 24);
        }
        if (x.Lod > 0)
            for (int k = 0; k < 3; k++) // 빨려 드는 먼지
            {
                float ph = Mathf.PosMod(x.T * 0.5f + k / 3f, 1f);
                float a = Hash(x.Id, k, 54) * Mathf.Tau + ph * 2f;
                Dot(ci, x.C + Vector2.FromAngle(a) * rr * (1.6f - ph * 1.4f), 0.8f, Powder.WithAlpha(0.6f * (1f - ph)));
            }
    }

    private static void PurifierFine(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.5f;
        for (int k = 0; k < 4; k++) Dot(ci, x.C + Vector2.FromAngle(k * Mathf.Pi / 2f + 0.8f) * rr * 0.32f, 0.6f, new Color("#8a929e"));
        Tag(ci, x.C + new Vector2(0f, rr - 3f), "HEPA", 4, new Color(0, 0, 0, 0.45f));
    }

    // ─────────────── 제습기 ───────────────

    private static void DehumidBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#e0e4ea").Darkened(0.55f), 4, new Color("#8a929e"));
        Bevel(ci, x.B, 0.14f);
        var grill = x.Q(0.1f, 0.1f, 0.9f, 0.55f);
        ci.Box(grill, new Color("#1a1e24"));
        var zz = new Vector2[9]; // 냉각 코일
        for (int i = 0; i < 9; i++) zz[i] = new Vector2(grill.Position.X + grill.Size.X * i / 8f, grill.Position.Y + (i % 2 == 0 ? 2f : grill.Size.Y - 2f));
        ci.Polyline(zz, Copper.WithAlpha(0.8f), 1.2f, true);
        Vents(ci, grill, 5, false, new Color("#4a525e"), 1f); // 앞 살
        var tank = x.Q(0.12f, 0.64f, 0.88f, 0.92f);
        Glass(ci, tank, new Color("#0e1a24"), 2f); // 물통 창
        Line(ci, x.P(0.35f, 0.6f), x.P(0.65f, 0.6f), new Color("#3a4250"), 1.6f); // 손잡이
    }

    private static void DehumidLife(in Fix x)
    {
        var ci = x.Ci;
        var tank = x.Q(0.14f, 0.66f, 0.86f, 0.9f);
        float hum = Mathf.Clamp(x.F.Room.Humidity, 0f, 1f);
        float level = Mathf.Clamp(0.15f + hum * 0.7f + Mathf.Sin(x.T * 0.05f) * 0.05f, 0f, 1f); // 습한 방일수록 물통이 빨리 찬다
        bool vert = x.Wide;
        var fill = vert ? new Rect2(tank.Position.X, tank.End.Y - tank.Size.Y * level, tank.Size.X, tank.Size.Y * level)
                        : new Rect2(tank.Position.X, tank.Position.Y, tank.Size.X * level, tank.Size.Y);
        ci.Box(fill, Water.WithAlpha(0.55f));
        if (x.On && x.Lod > 0)
        {
            float ph = Mathf.PosMod(x.T * 1.4f, 1f); // 코일에서 물통으로 떨어지는 방울
            Dot(ci, x.P(0.5f, 0.55f).Lerp(x.P(0.5f, 0.7f), ph), 0.9f, WaterLight.WithAlpha(1f - ph));
            for (int k = 0; k < 3; k++) // 빨려 드는 습기
            {
                float q = Mathf.PosMod(x.T * 0.6f + k / 3f, 1f);
                Dot(ci, x.P(0.2f + k * 0.3f, -0.3f + q * 0.5f), 1.4f, new Color("#cfe8ff").WithAlpha(0.25f * (1f - q) * hum));
            }
        }
        Led(ci, x.P(0.92f, 0.06f) + x.V * 1.5f, hum > 0.7f ? Amber : Good, Mathf.Max(0.2f, x.Glow), 0.9f);
    }

    private static void DehumidFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.55f);
        Tag(ci, x.P(0.5f, 0.78f), "%RH", 4, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 재배대 ───────────────

    private static void GrowBody(in Fix x)
    {
        if (CropBody(x)) return; // v16.22 조류 · 버섯 · 단백질 · 허브는 FixtureArtFood
        // 채소 재배대 (v16.26 그림 함수 하나에 — 표가 가리키는 함수가 직접 그린다)
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1c2619"), 5, new Color("#3b5a33"));
        var soil = x.Q(0.03f, 0.2f, 0.97f, 0.8f);
        Box(ci, soil, Soil, 3);
        for (int i = 0; i < 26; i++) // 흙 결
            Dot(ci, soil.Position + new Vector2(Hash(x.Id, i, 81), Hash(x.Id, i, 82)) * soil.Size, 0.6f + Hash(x.Id, i, 83), new Color("#3a2c1e"));
        Line(ci, x.P(0.02f, 0.11f), x.P(0.98f, 0.11f), new Color("#2a3a4a"), 2f); // 점적관
        int n = Mathf.Max(2, x.F.Width * 2);
        for (int k = 0; k < n; k++) Dot(ci, x.P(0.06f + 0.88f * k / (n - 1), 0.11f), 1.1f, new Color("#4a6a8a")); // 점적구
        ci.Box(x.Q(0.03f, 0.88f, 0.97f, 0.95f), new Color("#2b2033")); // 생장등 갓
        for (int k = 0; k < n; k += 2) ci.Box(new Rect2(x.P(0.06f + 0.88f * k / (n - 1), 0.82f) - new Vector2(0.5f, 0f), new Vector2(1f, 3f)), new Color("#e8e2d4").WithAlpha(0.6f)); // 이름 막대
    }

    private static void GrowLife(in Fix x)
    {
        if (CropLife(x)) return; // v16.22
        // 채소 재배대 (v16.26 그림 함수 하나에 — 표가 가리키는 함수가 직접 그린다)
        var ci = x.Ci;
        bool alive = x.Eff > 0.01f && !x.Dead;
        if (alive) ci.Box(x.Q(0.04f, 0.89f, 0.96f, 0.94f), GrowPink.WithAlpha((0.35f + 0.1f * Mathf.Sin(x.T)) * x.Glow)); // 생장등
        if (alive && x.Lod > 0) ci.Box(x.Q(0.03f, 0.2f, 0.97f, 0.8f), GrowPink.WithAlpha(0.04f * x.Glow));
        if (x.M?.Crop is not CropState crop) return;
        int n = Mathf.Max(2, x.F.Width * 2);
        for (int k = 0; k < n; k++)
        {
            var p = x.P(0.06f + 0.88f * k / (n - 1), (k & 1) == 0 ? 0.45f : 0.55f);
            float size = 1.5f + 5.5f * crop.Growth;
            float sway = Mathf.Sin(x.T * 1.3f + k) * 0.6f;
            var leaf = Leaf.Lerp(LeafLight, crop.Care) * (alive ? 1f : 0.6f);
            int leaves = crop.Growth < 0.3f ? 2 : 5;
            for (int l = 0; l < leaves; l++) // 잎 로제트
            {
                var d = Vector2.FromAngle(l * Mathf.Tau / leaves + k * 0.7f + sway * 0.2f);
                Dot(ci, p + d * size * 0.55f + new Vector2(sway, 0f), size * 0.5f, leaf.WithAlpha(0.9f));
            }
            Dot(ci, p + new Vector2(sway, 0f), size * 0.3f, leaf.Lightened(0.3f));
            if (crop.Ripe && k % 2 == 0) Dot(ci, p + new Vector2(sway + 2, 1), 2.2f, new Color("#ff8a5c"));
        }
        if (alive && x.Lod > 0) // 점적구에서 떨어지는 물
        {
            int k = (int)(x.T * 0.7f) % n;
            float ph = Mathf.PosMod(x.T * 0.7f, 1f);
            Dot(ci, x.P(0.06f + 0.88f * k / (n - 1), 0.11f + ph * 0.12f), 0.8f, WaterLight.WithAlpha(1f - ph));
        }
    }

    private static void GrowFine(in Fix x)
    {
        if (CropFine(x)) return; // v16.22
        // 채소 재배대 (v16.26 그림 함수 하나에 — 표가 가리키는 함수가 직접 그린다)
        var ci = x.Ci;
        Bolts(ci, x.B, 2f, 0.6f);
        int n = Mathf.Max(2, x.F.Width * 2);
        for (int k = 0; k < n; k += 2) Tag(ci, x.P(0.06f + 0.88f * k / (n - 1), 0.84f), (k / 2 + 1).ToString(), 4, new Color(1, 1, 1, 0.35f));
    }

    // ─────────────── LED 생장판 ───────────────

    private static void LedPanelBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1b1622"), 3, new Color("#5a3f6e"), 2);
        for (int k = 0; k < 4; k++) ci.Box(x.Q(0.12f + k * 0.2f, 0.18f, 0.29f + k * 0.2f, 0.82f), new Color("#2b2033"));
        for (int k = 0; k < 6; k++) Line(ci, x.P(0.08f + k * 0.17f, 0.02f), x.P(0.08f + k * 0.17f, 0.12f), new Color("#3a3046"), 1.2f); // 방열 핀
        Box(ci, x.Q(0.35f, 0.88f, 0.65f, 0.98f), new Color("#0f0c14"), 1); // 전원부
        Cable(ci, x.P(0.5f, 0.98f), x.R.End - new Vector2(3f, 1f), 1.5f, new Color("#1a1a1a"), 1.2f); // 전원선
        foreach (float u in new[] { 0.02f, 0.98f }) ci.Box(new Rect2(x.P(u, 0.45f) - new Vector2(1.5f, 1.5f), new Vector2(3f, 3f)), new Color("#6a5a7a")); // 거는 쇠
    }

    private static void LedPanelLife(in Fix x)
    {
        var ci = x.Ci;
        if (!x.Lit) return;
        var pink = new Color("#ff7fd0");
        for (int k = 0; k < 4; k++)
            ci.Box(x.Q(0.12f + k * 0.2f, 0.18f, 0.29f + k * 0.2f, 0.82f), (k % 2 == 0 ? pink : new Color("#8f7fff")).WithAlpha((0.75f + 0.1f * Mathf.Sin(x.T * 2f + k)) * x.Glow));
        if (x.Lod > 0) Dot(ci, x.C, ShipView.T * 1.3f, pink.WithAlpha(0.05f * x.Glow));
    }

    private static void LedPanelFine(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 4; k++) for (int j = 0; j < 4; j++) Dot(ci, x.P(0.2f + k * 0.2f, 0.25f + j * 0.17f), 0.5f, new Color(1, 1, 1, 0.3f)); // LED 알
        Bolts(ci, x.B, 1.8f, 0.5f);
    }

    // ─────────────── 양액 조절기 ───────────────

    private static readonly Color[] DoseColors = { new("#6ac84a"), new("#e0a03a"), new("#e05a5a") };

    private static void DoserBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1a2218"), 3, new Color("#4a6a3a"));
        for (int k = 0; k < 3; k++)
        {
            var bottle = x.P(0.18f + k * 0.32f, 0.24f);
            Can(ci, bottle, 3.6f, new Color("#e8ecf2").WithAlpha(0.3f), new Color("#9aa3b5"));
            Dot(ci, bottle, 1.8f, DoseColors[k]); // 병뚜껑
            var pump = x.P(0.18f + k * 0.32f, 0.6f);
            Dot(ci, pump, 3.2f, new Color("#2a3328"));
            Ring(ci, pump, 3.2f, new Color("#6a7a5a"), 0.8f, 12); // 연동 펌프 머리
            Line(ci, bottle, pump, DoseColors[k].WithAlpha(0.5f), 0.9f); // 관
            Line(ci, pump, x.P(0.5f, 0.88f), DoseColors[k].WithAlpha(0.5f), 0.9f);
        }
        ci.Box(x.Q(0.08f, 0.85f, 0.92f, 0.93f), new Color("#3a4a3a")); // 섞는 관
    }

    private static void DoserLife(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 3; k++)
        {
            var pump = x.P(0.18f + k * 0.32f, 0.6f);
            float a = x.Ang(3f + k);
            for (int r = 0; r < 3; r++) Dot(ci, pump + Vector2.FromAngle(a + r * Mathf.Tau / 3f) * 2f, 0.9f, new Color("#c8d0b0").WithAlpha(0.9f)); // 롤러
            if (x.On && x.Lod > 0)
            {
                float ph = Mathf.PosMod(x.T * 0.6f * x.Spin + k * 0.3f, 1f);
                Dot(ci, pump.Lerp(x.P(0.5f, 0.88f), ph), 0.9f, DoseColors[k]);
            }
        }
        if (x.Lit) Led(ci, x.P(0.92f, 0.08f) + x.V * 1.5f, x.M != null && x.M.Has(FaultKind.NutrientImbalance) ? Danger : Good, x.Glow, 0.9f);
    }

    private static void DoserFine(in Fix x)
    {
        var ci = x.Ci;
        string[] names = { "A", "B", "pH" };
        for (int k = 0; k < 3; k++) Tag(ci, x.P(0.18f + k * 0.32f, 0.24f) + x.V * 5f, names[k], 4, new Color(1, 1, 1, 0.45f));
        Bolts(ci, x.B, 1.8f, 0.5f);
    }

    // ─────────────── 식물 벽 ───────────────

    private static void PlantWallBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1a1e16"), 3, new Color("#5a4a32"), 2);
        Line(ci, x.P(0.02f, 0.06f), x.P(0.98f, 0.06f), new Color("#3a4a5a"), 1.4f); // 위 점적줄
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var pocket = x.Q(0.06f + i * 0.31f, 0.14f + j * 0.28f, 0.33f + i * 0.31f, 0.38f + j * 0.28f);
                Box(ci, pocket, Felt, 2); // 펠트 주머니
                var p = pocket.GetCenter();
                int kind = (i + j * 3 + x.Id) % 3;
                if (kind == 0) for (int l = 0; l < 4; l++) Line(ci, p, p + Vector2.FromAngle(-Mathf.Pi / 2f + (l - 1.5f) * 0.5f) * 4f, Leaf, 1f); // 고사리
                else if (kind == 1) { Dot(ci, p + new Vector2(-1.5f, 0f), 2f, LeafLight.Darkened(0.2f)); Dot(ci, p + new Vector2(1.5f, -1f), 1.8f, Leaf); } // 둥근 잎
                else for (int l = 0; l < 5; l++) Line(ci, p, p + Vector2.FromAngle(l * Mathf.Tau / 5f) * 3f, new Color("#7ab08a"), 1.4f); // 다육
            }
    }

    private static void PlantWallLife(in Fix x)
    {
        var ci = x.Ci;
        float bloom = Pulse(x.T * 0.08f, 1f);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var p = x.Q(0.06f + i * 0.31f, 0.14f + j * 0.28f, 0.33f + i * 0.31f, 0.38f + j * 0.28f).GetCenter();
                float sway = Mathf.Sin(x.T * 1.1f + i + j * 2f) * 0.8f * (x.F.Room.VentOpen ? 1f : 0.2f);
                Dot(ci, p + new Vector2(sway, -2f), 1.2f, LeafLight.WithAlpha(0.7f * (x.On ? 1f : 0.4f)));
                if ((i + j) % 2 == 0 && x.On && bloom > 0.4f) Dot(ci, p + new Vector2(sway + 2f, -3f), 0.9f + bloom, new Color("#ff9ad0").WithAlpha(0.85f)); // 꽃
            }
        if (x.On && x.Lod > 0)
        {
            float ph = Mathf.PosMod(x.T * 0.8f, 1f);
            Dot(ci, x.P(0.1f + 0.8f * Hash(x.Id, (int)(x.T * 0.8f), 57), 0.06f + ph * 0.1f), 0.7f, WaterLight.WithAlpha(1f - ph));
        }
    }

    private static void PlantWallFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.5f);
        for (int i = 0; i < 4; i++) Dot(ci, x.P(0.06f + i * 0.31f, 0.06f), 0.6f, new Color("#6a8aaa"));
    }

    // ─────────────── 누설 감지기 ───────────────

    private static Vector2[] LeakRope(in Fix x)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = i / 9f * Mathf.Tau;
            pts[i] = x.C + new Vector2(Mathf.Cos(a) * x.B.Size.X * 0.44f, Mathf.Sin(a) * x.B.Size.Y * 0.44f) + new Vector2(Hash(x.Id, i, 58) - 0.5f, Hash(x.Id, i, 59) - 0.5f) * 3f;
        }
        pts[9] = pts[0];
        return pts;
    }

    private static void LeakBody(in Fix x)
    {
        var ci = x.Ci;
        var rope = LeakRope(x);
        ci.Polyline(rope, new Color("#1a1a1a"), 2.4f, true); // 감지 줄
        ci.Polyline(rope, new Color("#3a6aa0"), 1f, true);
        for (int i = 1; i < 9; i += 2) ci.Box(new Rect2(rope[i] - new Vector2(1.2f, 1.2f), new Vector2(2.4f, 2.4f)), new Color("#6a7080")); // 줄 고정 집게
        var box = new Rect2(x.C - new Vector2(6f, 5f), new Vector2(12f, 10f));
        Box(ci, box, new Color("#2a2e36"), 2, new Color("#6a7080"));
        ci.Box(new Rect2(box.Position.X + 1.5f, box.End.Y - 3f, box.Size.X - 3f, 2f), new Color("#1a1d22")); // 단자대
        Dot(ci, x.C, 3.6f, new Color("#5a1a1a")); // 경광등 덮개
        Ring(ci, x.C, 3.6f, new Color("#a04040"), 0.8f, 14);
    }

    private static void LeakLife(in Fix x)
    {
        var ci = x.Ci;
        var room = x.F.Room;
        bool wet = room.Humidity > 0.75f || room.Air.Leak > 0.01f;
        var rope = LeakRope(x);
        if (x.On && x.Lod > 0) // 줄을 따라 도는 점검 신호
        {
            float ph = Mathf.PosMod(x.T * 0.4f, 1f) * 9f;
            int i = Mathf.Clamp((int)ph, 0, 8);
            Dot(ci, rope[i].Lerp(rope[i + 1], ph - i), 1.2f, (wet ? LeakBlue : Good).WithAlpha(0.9f));
        }
        if (wet && x.Lit) // 젖었다: 경광등이 돈다
        {
            float a = x.T * 6f;
            Dot(ci, x.C, 3f, Danger.WithAlpha(0.8f));
            var d = Vector2.FromAngle(a);
            var n = new Vector2(-d.Y, d.X);
            ci.Poly(new[] { x.C, x.C + d * 18f + n * 6f, x.C + d * 18f - n * 6f }, Danger.WithAlpha(0.18f));
        }
        else Led(ci, x.C, Good, x.Lit ? 0.35f + 0.2f * Pulse(x.T, 1f) : 0f, 1.2f);
    }

    private static void LeakFine(in Fix x)
    {
        var ci = x.Ci;
        var box = new Rect2(x.C - new Vector2(6f, 5f), new Vector2(12f, 10f));
        Bolts(ci, box, 1.5f, 0.45f);
        Tag(ci, x.C + new Vector2(0f, 7.5f), "LEAK", 4, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 제염 샤워 ───────────────

    private static void DeconBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1a2226"), 2, new Color("#4fa892"));
        Grille(ci, x.B.Grow(-2f), 3.2f, new Color("#0e1416"), 1f); // 바닥 격자
        Dot(ci, x.C, 3.2f, new Color("#0a0d10")); // 배수구
        Ring(ci, x.C, 3.2f, Chrome.WithAlpha(0.6f), 0.8f, 12);
        var head = x.P(0.5f, 0.12f);
        Dot(ci, head, 4f, Chrome.Darkened(0.2f)); // 샤워 머리
        Ring(ci, head, 4f, Chrome, 0.8f, 14);
        Line(ci, x.P(0.02f, 0.02f), x.P(0.02f, 0.98f), new Color(0.7f, 0.9f, 1f, 0.35f), 1.4f); // 유리 칸막이
        Line(ci, x.P(0.98f, 0.02f), x.P(0.98f, 0.98f), new Color(0.7f, 0.9f, 1f, 0.35f), 1.4f);
        Knob(ci, x.P(0.85f, 0.12f), 1.8f, 0.8f, new Color("#4fa892")); // 밸브
    }

    private static void DeconLife(in Fix x)
    {
        var ci = x.Ci;
        bool using_ = x.User != null || x.Occupant != null;
        var head = x.P(0.5f, 0.12f);
        if (using_ && x.On)
        {
            int n = x.Lod == 0 ? 4 : 10;
            for (int k = 0; k < n; k++) // 머리에서 바닥으로 쏟아지는 물줄기
            {
                float ph = Mathf.PosMod(x.T * 2.2f + k / (float)n, 1f);
                var target = x.C + Vector2.FromAngle(Hash(x.Id, k, 60) * Mathf.Tau) * x.B.Size.X * 0.35f;
                Dot(ci, head.Lerp(target, ph), 0.9f, WaterLight.WithAlpha(0.75f * (1f - ph * 0.6f)));
            }
            ci.Arc(x.C, 4.5f, x.T * 5f, x.T * 5f + 3f, 8, WaterLight.WithAlpha(0.6f), 1f, true); // 배수구 소용돌이
            if (x.Lod > 0) Dot(ci, x.C, x.B.Size.X * 0.45f, new Color(0.8f, 0.9f, 1f, 0.06f)); // 김
        }
        else if (x.Lod > 0 && Mathf.PosMod(x.T * 0.3f, 1f) < 0.15f) // 쉬는 중: 가끔 한 방울
            Dot(ci, head + new Vector2(0f, Mathf.PosMod(x.T * 0.3f, 1f) * 40f), 0.8f, WaterLight.WithAlpha(0.7f));
        Led(ci, x.P(0.85f, 0.25f), using_ ? Cyan : Good, Mathf.Max(0.2f, x.Glow) * 0.8f, 0.8f);
    }

    private static void DeconFine(in Fix x)
    {
        var ci = x.Ci;
        var head = x.P(0.5f, 0.12f);
        for (int k = 0; k < 6; k++) Dot(ci, head + Vector2.FromAngle(k * Mathf.Tau / 6f) * 2.2f, 0.45f, new Color("#1a2226"));
        Tag(ci, x.P(0.5f, 0.85f), "DECON", 4, new Color("#4fa892").WithAlpha(0.6f));
    }

    // ─────────────── 세탁기 ───────────────

    private static readonly Color[] Laundry = { new("#c84a4a"), new("#4a7ac8"), new("#e0c060"), new("#5aa86a") };

    private static void WasherBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#d8dce2").Darkened(0.45f), 4, new Color("#9aa3b0"));
        Bevel(ci, x.B, 0.14f);
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.34f;
        var c = x.C + new Vector2(0f, 2f);
        Dot(ci, c, rr + 2f, Chrome); // 크롬 테
        Dot(ci, c, rr, new Color("#141a22")); // 둥근 창
        Ring(ci, c, rr - 1.5f, new Color("#3a4250"), 0.8f, 20);
        ci.Box(new Rect2(c.X - rr - 3f, c.Y - 2f, 2f, 4f), new Color("#6a727e")); // 경첩
        var strip = new Rect2(x.B.Position + new Vector2(2f, 1.5f), new Vector2(x.B.Size.X - 4f, 4f));
        ci.Box(strip, new Color("#2a2e36")); // 조작 띠
        Knob(ci, new Vector2(strip.End.X - 3f, strip.GetCenter().Y), 1.8f, 1.2f, new Color("#9aa3b0"));
        ci.Box(new Rect2(strip.Position + new Vector2(1f, 0.8f), new Vector2(6f, 2.4f)), new Color("#c8ccd4")); // 세제 서랍
    }

    private static void WasherLife(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.34f;
        bool spinCycle = Mathf.PosMod(x.T * 0.05f, 1f) > 0.8f; // 가끔 탈수
        float j = x.On && spinCycle ? Mathf.Sin(x.T * 45f) * 0.7f : 0f; // 탈수 떨림
        var c = x.C + new Vector2(j, 2f);
        float a = x.Ang(spinCycle ? 12f : 2.5f);
        for (int k = 0; k < 4; k++) // 도는 빨래
        {
            float ang = a + k * Mathf.Tau / 4f + Mathf.Sin(a * 0.5f + k) * 0.4f;
            float dist = spinCycle ? rr * 0.68f : rr * (0.25f + 0.35f * Pulse(a + k, 1f));
            Dot(ci, c + Vector2.FromAngle(ang) * dist, rr * 0.28f, Laundry[k].WithAlpha(0.8f));
        }
        if (x.On && !spinCycle && x.Lod > 0) // 거품
            for (int k = 0; k < 4; k++) Dot(ci, c + Vector2.FromAngle(a * 0.7f + k * 1.6f) * rr * 0.75f, 1.2f, Colors.White.WithAlpha(0.5f));
        ci.Arc(c, rr * 0.8f, 3.6f, 4.6f, 6, new Color(1, 1, 1, 0.2f), 1.2f, true); // 유리 반사
        var strip = new Rect2(x.B.Position + new Vector2(2f, 1.5f), new Vector2(x.B.Size.X - 4f, 4f));
        for (int k = 0; k < 3; k++)
            Led(ci, new Vector2(strip.Position.X + 10f + k * 3.5f, strip.GetCenter().Y), Cyan, x.Lit && (int)(x.T * 0.4f) % 3 >= k ? x.Glow : 0f, 0.7f);
    }

    private static void WasherFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.5f);
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.34f;
        for (int k = 0; k < 12; k++) Dot(ci, x.C + new Vector2(0f, 2f) + Vector2.FromAngle(k * Mathf.Tau / 12f) * (rr + 1f), 0.4f, new Color("#5a6270"));
    }

    // ─────────────── 우주복 건조기 ───────────────

    private static void DryerBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1c201c"), 3, new Color("#4fa892"));
        var base_ = x.Q(0.08f, 0.62f, 0.92f, 0.92f);
        Box(ci, base_, new Color("#2a302a"), 2, new Color("#3a4a3a")); // 송풍 받침
        Vents(ci, base_.Grow(-1.5f), 7, true, new Color("#121612"), 1f);
        for (int k = 0; k < 4; k++) // 장화 · 장갑 거는 기둥
        {
            var p = x.P(0.17f + k * 0.22f, 0.35f);
            Can(ci, p, 2.4f, new Color("#4a524a"), new Color("#8a9a8a"));
        }
        var glove = x.P(0.39f, 0.3f); // 걸린 장갑 하나
        Box(ci, new Rect2(glove - new Vector2(3f, 4f), new Vector2(6f, 7f)), new Color("#d8dee8").WithAlpha(0.7f), 2.5f);
        Cable(ci, x.P(0.92f, 0.78f), x.R.End - new Vector2(2f, 6f), 2f, new Color("#3a4a3a"), 2f); // 호스
    }

    private static void DryerLife(in Fix x)
    {
        var ci = x.Ci;
        if (x.Lit) ci.Box(x.Q(0.12f, 0.88f, 0.88f, 0.9f), Ember.WithAlpha(0.6f * x.Glow)); // 히터 선
        if (!x.On || x.Lod == 0) return;
        for (int k = 0; k < 4; k++) // 기둥마다 올라오는 더운 바람
        {
            float ph = Mathf.PosMod(x.T * 0.9f + k * 0.27f, 1f);
            var p = x.P(0.17f + k * 0.22f, 0.35f) + new Vector2(Mathf.Sin(ph * 8f + k) * 1.5f, -ph * 10f);
            Dot(ci, p, 1f + 1.5f * ph, new Color("#ffd0a0").WithAlpha(0.3f * (1f - ph)));
        }
    }

    private static void DryerFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.5f);
        Tag(ci, x.P(0.5f, 0.12f), "DRY", 4, new Color("#4fa892").WithAlpha(0.6f));
    }

    // ─────────────── 에어락 회수 펌프 ───────────────

    private static void AirlockPumpBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1a1e22"), 3, new Color("#4fa892"), 2);
        float cr = Mathf.Min(x.Lu * 0.2f, x.Lv * 0.24f);
        var a = x.P(0.26f, 0.3f);
        var b = x.P(0.26f, 0.72f);
        ci.Box(x.Q(0.14f, 0.38f, 0.38f, 0.64f), new Color("#2a2e34")); // 크랭크 상자
        foreach (var p in new[] { a, b }) Can(ci, p, cr, new Color("#3a4250"), new Color("#7a8494")); // 실린더 둘
        var tank = x.Q(0.55f, 0.12f, 0.95f, 0.88f);
        Box(ci, tank, new Color("#2a4a44"), 5, new Color("#4fa892")); // 회수 탱크
        Pipe(ci, x.P(0.38f, 0.5f), x.P(0.55f, 0.5f), 2.2f, new Color("#4a5566"), false);
        Gauge(ci, x.P(0.75f, 0.5f), Mathf.Min(4.5f, cr), 0.5f, new Color("#c0392b"));
    }

    private static void AirlockPumpLife(in Fix x)
    {
        var ci = x.Ci;
        float cr = Mathf.Min(x.Lu * 0.2f, x.Lv * 0.24f);
        float a = x.Ang(4f);
        var p0 = x.P(0.26f, 0.3f);
        var p1 = x.P(0.26f, 0.72f);
        Dot(ci, p0, cr * (0.35f + 0.25f * (0.5f + 0.5f * Mathf.Sin(a))), new Color("#9aa6b5")); // 피스톤 머리 (번갈아)
        Dot(ci, p1, cr * (0.35f + 0.25f * (0.5f - 0.5f * Mathf.Sin(a))), new Color("#9aa6b5"));
        float press = x.On ? 0.35f + 0.3f * Pulse(x.T, 0.5f) : 0.05f;
        Gauge(ci, x.P(0.75f, 0.5f), Mathf.Min(4.5f, cr), press, new Color("#c0392b"), false);
        if (x.On && x.Lod > 0 && Mathf.Sin(a) > 0.95f) // 압축 끝: 쉭
            for (int k = 0; k < 3; k++) Line(ci, x.P(0.4f, 0.5f), x.P(0.4f, 0.5f) + Vector2.FromAngle(-Mathf.Pi / 2f + (k - 1) * 0.5f) * 5f, SteamWhite.WithAlpha(0.5f), 0.8f);
    }

    private static void AirlockPumpFine(in Fix x)
    {
        var ci = x.Ci;
        float cr = Mathf.Min(x.Lu * 0.2f, x.Lv * 0.24f);
        foreach (var p in new[] { x.P(0.26f, 0.3f), x.P(0.26f, 0.72f) })
            for (int k = 0; k < 6; k++) Bolt(ci, p + Vector2.FromAngle(k * Mathf.Tau / 6f) * cr * 0.85f, 0.45f);
        Tag(ci, x.P(0.75f, 0.2f), "kPa", 4, new Color(1, 1, 1, 0.45f));
    }
}
