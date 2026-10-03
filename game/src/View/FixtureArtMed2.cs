using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 의료 2차 그림 — 투석기(바퀴 달린 함 · 연동 펌프 바퀴 둘 · 줄무늬 필터 통 · 걸린 투석액 주머니 · 붉은/푸른 관) ·
/// 인공 폐(카트 · 둥근 산소 통 — 검붉은 피가 들어가 선홍으로 나온다 · 원심 펌프 · 초록 산소 병 · 옆구리 손 펌프 손잡이) ·
/// 인공 심장 충전대(벽 거치대 · 전지 칸 둘 · 감긴 구동선 · 맥박 등) · 장기 보관함(흰 단열 상자 · 서리 낀 유리 뚜껑 · 안의 통 · 온도 숫자) ·
/// 바이오 프린터(유리 상자 · 가로대와 머리 · 층층이 자라는 장기 · 잉크 통) · 음압기(벽 상자 · 큰 팬 · 주름 필터 · 음압 바늘 · 붉은/푸른 등 · 빨려 드는 공기).
/// 상태: 돎(바퀴 · 피 · 팬) · 대기 · 꺼짐 · 내장 전지(호박색 깜빡임) · 손 펌프(손잡이가 돈다) · 연결된 사람까지 관.
/// </summary>
public static partial class FixtureArt
{
    private static void MedArt2(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.Dialyzer] = new(DialBody, DialLife, DialFine, Look.Leak, 0.3f, 0.7f);
        t[FurnitureType.Ecmo] = new(EcmoBody, EcmoLife, EcmoFine, Look.Leak, 0.4f, 0.5f);
        t[FurnitureType.HeartPump] = new(PumpDockBody, PumpDockLife, PumpDockFine, Look.Sparks, 0.5f, 0.3f);
        t[FurnitureType.OrganCooler] = new(CoolerBody, CoolerLife, CoolerFine, Look.Gas, 0.85f, 0.5f);
        t[FurnitureType.BioPrinter] = new(PrinterBody, PrinterLife, PrinterFine, Look.Jam, 0.5f, 0.15f);
        t[FurnitureType.NegPressure] = new(NegBody, NegLife, NegFine, Look.Grind, 0.5f, 0.5f);
    }

    private static readonly Color MBody = new("#e9eef2"), MTrim = new("#9fb3c4"), MBlood = new("#c0262e"), MVein = new("#6e1018"), MDialysate = new("#e8d78a");
    private static readonly Color MO2 = new("#2f9a58"), MScreen = new("#0c1a20"), MWave = new("#6ef0b0"), MFrost = new("#d8f0ff"), MInkA = new("#e86a9a"), MInkB = new("#f2c94c");
    private static readonly Color MTissue = new("#c8576a"), MHepa = new("#d9d2c0"), MNegRed = new("#ff4d5a"), MNegBlue = new("#5fb0ff");

    private static bool On2(in Fix x) => x.M != null && x.M.Powered && x.M.Faults.Count == 0;
    private static CrewMember? Hooked(in Fix x) => x.W?.Organs.PatientOn(x.F);
    private static bool OnCell(in Fix x) => x.W != null && x.M != null && !x.M.Powered && x.M.Faults.Count == 0 && Hooked(x) != null && x.W.Organs.Cell(x.F) > 0f;

    /// <summary>연결된 사람까지 관 두 가닥 (나가는 피 · 들어오는 피).</summary>
    private static void Tubes(in Fix x, Vector2 from, Color outC, Color inC, float flow)
    {
        if (Hooked(x) is not CrewMember c) return;
        var ci = x.Ci;
        var to = ShipView.ToPx(c.Position);
        var n = (to - from).Normalized().Orthogonal() * x.Px(1.6f);
        Cable(ci, from + n, to + n, x.Px(6f), outC, x.Px(1.1f));
        Cable(ci, from - n, to - n, x.Px(7f), inC, x.Px(1.1f));
        if (flow <= 0f || x.Lod <= 0) return;
        for (int k = 0; k < 4; k++) // 흐르는 점
        {
            float u = Mathf.PosMod(x.T * 0.35f * flow + k * 0.25f, 1f);
            Dot(ci, from + n + (to - from) * u, x.Px(0.7f), outC.Lightened(0.35f));
        }
    }

    // ─────────────── 투석기 ───────────────

    private static void DialBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.08f, 0.1f, 0.92f, 0.95f), MBody, 3f, MTrim, 1);
        Bevel(ci, x.Q(0.08f, 0.1f, 0.92f, 0.95f), 0.12f);
        Box(ci, x.Q(0.16f, 0.16f, 0.84f, 0.36f), MScreen, 1.5f, Steel3); // 화면
        foreach (float u in new[] { 0.32f, 0.68f }) { Ring(ci, x.P(u, 0.55f), x.Px(4.2f), Steel3, x.Px(1.2f)); Dot(ci, x.P(u, 0.55f), x.Px(1.2f), Steel4); } // 연동 펌프 바퀴
        var cart = x.Q(0.86f, 0.38f, 0.97f, 0.88f); // 필터 통 (줄무늬)
        Box(ci, cart, new Color("#f4f6f8"), 2f, MTrim);
        for (int k = 1; k < 6; k++) Line(ci, cart.Position + new Vector2(0f, cart.Size.Y * k / 6f), cart.Position + new Vector2(cart.Size.X, cart.Size.Y * k / 6f), MTrim, x.Px(0.5f));
        Line(ci, x.P(0.2f, 0.04f), x.P(0.8f, 0.04f), Chrome, x.Px(1f)); // 주머니 거는 대
        Box(ci, x.Q(0.24f, 0.0f, 0.4f, 0.12f), MDialysate.WithAlpha(0.8f), 1.5f, Chrome); // 투석액 주머니
        if (x.Tier >= 2) Box(ci, x.Q(0.6f, 0.0f, 0.76f, 0.12f), MDialysate.WithAlpha(0.6f), 1.5f, Chrome); // II: 둘째 주머니
        Pipe(ci, x.P(0.32f, 0.62f), x.P(0.86f, 0.7f), x.Px(1.2f), MBlood, false);
        Pipe(ci, x.P(0.68f, 0.62f), x.P(0.86f, 0.48f), x.Px(1.2f), MVein, false);
        foreach (float u in new[] { 0.16f, 0.84f }) Dot(ci, x.P(u, 0.97f), x.Px(1.4f), Rubber); // 바퀴
        if (x.Tier >= 3) Box(ci, x.Q(0.12f, 0.78f, 0.4f, 0.9f), new Color("#2a5a8a"), 1f); // III: 물 정화 칸
    }

    private static void DialLife(in Fix x)
    {
        var ci = x.Ci;
        var pt = Hooked(x);
        bool run = pt != null && (On2(x) || OnCell(x));
        float a = run ? x.Ang(3.2f) : 0f;
        foreach (float u in new[] { 0.32f, 0.68f })
            for (int k = 0; k < 3; k++) // 롤러
            {
                float ang = a + k * Mathf.Tau / 3f + (u > 0.5f ? 0.5f : 0f);
                Dot(ci, x.P(u, 0.55f) + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * x.Px(3f), x.Px(1f), run ? Chrome : Steel4);
            }
        var scr = x.Q(0.18f, 0.18f, 0.82f, 0.34f);
        if (x.M != null && x.M.Powered || OnCell(x))
        {
            float s0 = scr.Position.X, w = scr.Size.X, y0 = scr.GetCenter().Y;
            Vector2? prev = null;
            for (int i = 0; i <= 14; i++) // 압력 선
            {
                float u = i / 14f;
                float y = y0 + (run ? Mathf.Sin(u * 9f + x.T * 3f) * scr.Size.Y * 0.28f : 0f);
                var p = new Vector2(s0 + u * w, y);
                if (prev is Vector2 pp) Line(ci, pp, p, (run ? MWave : MWave.WithAlpha(0.3f)) * new Color(1, 1, 1, x.Glow), x.Px(0.6f));
                prev = p;
            }
        }
        if (OnCell(x)) Led(ci, x.P(0.84f, 0.2f), Amber, 0.5f + 0.5f * Pulse(x.T, 6f), x.Px(1.4f)); // 내장 전지
        else Led(ci, x.P(0.84f, 0.2f), run ? Good : Steel4, run ? 0.9f * x.Glow : 0.2f, x.Px(1.2f));
        if (run && x.W != null && pt != null) // 걸러지는 양
        {
            float u = Mathf.Clamp(1f - (x.W.Organs.Peek(pt)?.Uremia ?? 0f), 0f, 1f);
            ci.DrawRect(new Rect2(x.P(0.18f, 0.39f), new Vector2(x.Lu * 0.64f * u, x.Px(1.2f))), MWave.WithAlpha(0.7f));
        }
        Tubes(x, x.P(0.86f, 0.6f), MBlood, MVein, run ? 1f : 0f);
    }

    private static void DialFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.08f, 0.1f, 0.92f, 0.95f), 1.2f, 0.35f);
        Tag(ci, x.P(0.5f, 0.88f), "투석", 3, Steel3);
        foreach (float u in new[] { 0.2f, 0.27f, 0.34f }) Knob(ci, x.P(u, 0.75f), x.Px(0.9f), u * 6f, Steel4);
        Line(ci, x.P(0.32f, 0.12f), x.P(0.32f, 0.3f), MDialysate.WithAlpha(0.5f), x.Px(0.4f)); // 주머니 줄
    }

    // ─────────────── 인공 폐 ───────────────

    private static void EcmoBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.05f, 0.2f, 0.95f, 0.9f), Steel2, 3f, Steel4, 1); // 카트
        Box(ci, x.Q(0.08f, 0.24f, 0.4f, 0.48f), MScreen, 1.5f, Steel4); // 유량 화면
        Dot(ci, x.P(0.62f, 0.5f), x.Px(6.5f), new Color("#3a0c10")); // 산소 통 (막)
        Ring(ci, x.P(0.62f, 0.5f), x.Px(6.5f), Chrome, x.Px(1f));
        Ring(ci, x.P(0.62f, 0.5f), x.Px(4.5f), Steel4, x.Px(0.5f));
        Dot(ci, x.P(0.3f, 0.72f), x.Px(3.6f), Steel1); // 원심 펌프 머리
        Ring(ci, x.P(0.3f, 0.72f), x.Px(3.6f), Chrome, x.Px(0.8f));
        Can(ci, x.P(0.9f, 0.15f), x.Px(2.6f), MO2, Chrome); // 산소 병
        Pipe(ci, x.P(0.9f, 0.18f), x.P(0.68f, 0.4f), x.Px(0.9f), MO2.Lightened(0.3f), false);
        Line(ci, x.P(0.97f, 0.62f), x.P(0.97f, 0.82f), Chrome, x.Px(1.4f)); // 손 펌프 축
        foreach (float u in new[] { 0.1f, 0.9f }) Dot(ci, x.P(u, 0.95f), x.Px(1.5f), Rubber);
        if (x.Tier >= 2) Box(ci, x.Q(0.42f, 0.78f, 0.6f, 0.88f), new Color("#a83a2a"), 1f); // II: 피 데우는 칸
        if (x.Tier >= 3) Ring(ci, x.P(0.62f, 0.5f), x.Px(8f), Cyan.WithAlpha(0.5f), x.Px(0.6f)); // III: 둘째 막
    }

    private static void EcmoLife(in Fix x)
    {
        var ci = x.Ci;
        var pt = Hooked(x);
        bool crank = x.W != null && x.W.Organs.Cranked(x.F);
        bool run = pt != null && (On2(x) || OnCell(x) || crank);
        // 산소 통: 들어오는 검붉은 피가 선홍으로 바뀐다
        var c0 = x.P(0.62f, 0.5f);
        if (run)
        {
            for (int k = 0; k < 6; k++)
            {
                float r = x.Px(1f + k * 0.9f);
                Ring(ci, c0, r, MVein.Lerp(MBlood.Lightened(0.15f), k / 5f).WithAlpha(0.7f), x.Px(0.8f));
            }
            float sweep = Mathf.PosMod(x.T * 1.5f, Mathf.Tau);
            ci.DrawArc(c0, x.Px(5.5f), sweep, sweep + 1.2f, 10, MBlood.Lightened(0.3f).WithAlpha(0.6f), x.Px(1f), true);
        }
        Fan(ci, x.P(0.3f, 0.72f), x.Px(2.8f), 3, run ? x.T * (crank && !On2(x) ? 4f : 14f) : 0.3f, run ? Chrome : Steel4, x.Px(0.8f)); // 원심 펌프
        // 손 펌프 손잡이
        float ha = crank ? x.T * 5f : 0.6f;
        var hub = x.P(0.97f, 0.72f);
        var tip = hub + new Vector2(Mathf.Cos(ha), Mathf.Sin(ha)) * x.Px(4f);
        Line(ci, hub, tip, crank ? WarnYellow : Steel4, x.Px(1.2f));
        Dot(ci, tip, x.Px(1.1f), crank ? WarnYellow : Steel3);
        // 화면: 흐름 숫자 막대
        var scr = x.Q(0.1f, 0.27f, 0.38f, 0.45f);
        if (run || x.M?.Powered == true)
        {
            float f = run ? (On2(x) ? 0.85f : crank ? 0.5f : 0.7f) : 0.05f;
            ci.DrawRect(new Rect2(scr.Position + new Vector2(0f, scr.Size.Y * 0.6f), new Vector2(scr.Size.X * f, scr.Size.Y * 0.25f)), (crank ? WarnYellow : MWave).WithAlpha(0.8f));
            Line(ci, scr.Position + new Vector2(0f, scr.Size.Y * 0.35f), scr.Position + new Vector2(scr.Size.X * (0.5f + 0.4f * Pulse(x.T, 2f)), scr.Size.Y * 0.35f), MWave.WithAlpha(0.6f * x.Glow), x.Px(0.6f));
        }
        if (OnCell(x)) Led(ci, x.P(0.1f, 0.55f), Amber, 0.5f + 0.5f * Pulse(x.T, 6f), x.Px(1.4f));
        else if (pt != null && !run) Led(ci, x.P(0.1f, 0.55f), Danger, Pulse(x.T, 9f) > 0.5f ? 1f : 0.1f, x.Px(1.6f)); // 멎었다
        Tubes(x, c0, MBlood.Lightened(0.15f), MVein, run ? (crank && !On2(x) ? 0.5f : 1f) : 0f);
    }

    private static void EcmoFine(in Fix x)
    {
        var ci = x.Ci;
        Tag(ci, x.P(0.9f, 0.32f), "O₂", 3, MO2.Lightened(0.4f));
        Bolts(ci, x.Q(0.05f, 0.2f, 0.95f, 0.9f), 1.2f, 0.35f);
        for (int k = 0; k < 3; k++) Line(ci, x.P(0.55f + k * 0.07f, 0.66f), x.P(0.55f + k * 0.07f, 0.7f), Chrome.WithAlpha(0.6f), x.Px(0.4f)); // 집게
    }

    // ─────────────── 인공 심장 충전대 ───────────────

    private static void PumpDockBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.1f, 0.05f, 0.9f, 0.6f), new Color("#2b2f3a"), 3f, Steel4, 1); // 벽 거치대
        for (int k = 0; k < 2; k++) Box(ci, x.Q(0.18f + k * 0.36f, 0.14f, 0.46f + k * 0.36f, 0.5f), Steel1, 1.5f, Steel3); // 전지 칸
        Cable(ci, x.P(0.5f, 0.6f), x.P(0.3f, 0.9f), x.Px(4f), new Color("#d8dde4"), x.Px(1.2f)); // 감긴 구동선
        Cable(ci, x.P(0.3f, 0.9f), x.P(0.62f, 0.86f), x.Px(-3f), new Color("#d8dde4"), x.Px(1.2f));
        Box(ci, x.Q(0.6f, 0.78f, 0.82f, 0.95f), new Color("#3a3f4c"), 2f, Chrome); // 조절기 가방
        if (x.Tier >= 2) Box(ci, x.Q(0.4f, 0.62f, 0.6f, 0.7f), new Color("#1a4a6a"), 1f); // II: 예비 전지
        if (x.Tier >= 3) Ring(ci, x.P(0.5f, 0.32f), x.Px(3f), Cyan.WithAlpha(0.6f), x.Px(0.6f)); // III: 무선 충전 고리
    }

    private static void PumpDockLife(in Fix x)
    {
        var ci = x.Ci;
        var u = x.User;
        var b = u != null && x.W != null ? x.W.Organs.Peek(u) : null;
        bool charging = b is { Pump: true } && On2(x);
        for (int k = 0; k < 2; k++)
        {
            var r = x.Q(0.2f + k * 0.36f, 0.17f, 0.44f + k * 0.36f, 0.47f);
            float fill = charging ? Mathf.Clamp(b!.PumpCharge + (k == 1 ? -0.2f : 0f), 0.05f, 1f) : x.M?.Powered == true ? 1f : 0.1f;
            float h = r.Size.Y * fill;
            ci.DrawRect(new Rect2(r.Position.X, r.End.Y - h, r.Size.X, h), (fill < 0.3f ? Amber : Good).WithAlpha(charging ? 0.55f + 0.3f * Pulse(x.T + k, 3f) : 0.35f * x.Glow));
        }
        // 맥박 등: 펌프가 도는 사람이 곁에 있으면 그 박자로
        float beat = Mathf.PosMod(x.T * 1.6f, 1f);
        Led(ci, x.P(0.5f, 0.08f), MBlood.Lightened(0.3f), x.On ? (beat < 0.12f || beat > 0.3f && beat < 0.4f ? 1f : 0.2f) * x.Glow : 0.05f, x.Px(1.3f));
        if (charging && u != null) Cable(ci, x.P(0.71f, 0.86f), ShipView.ToPx(u.Position), x.Px(5f), new Color("#d8dde4"), x.Px(0.9f));
    }

    private static void PumpDockFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.1f, 0.05f, 0.9f, 0.6f), 1.2f, 0.35f);
        for (int k = 0; k < 2; k++) Line(ci, x.P(0.28f + k * 0.36f, 0.12f), x.P(0.36f + k * 0.36f, 0.12f), Chrome, x.Px(0.6f)); // 전지 단자
        Tag(ci, x.P(0.71f, 0.9f), "12h", 2, Cream.WithAlpha(0.6f));
    }

    // ─────────────── 장기 보관함 ───────────────

    private static void CoolerBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.06f, 0.1f, 0.94f, 0.92f), MBody, 3f, MTrim, 2); // 단열 상자
        Glass(ci, x.Q(0.14f, 0.18f, 0.72f, 0.82f), MFrost.WithAlpha(0.35f), 2f); // 서리 낀 뚜껑
        Box(ci, x.Q(0.76f, 0.18f, 0.9f, 0.4f), MScreen, 1f, Steel3); // 온도
        Vents(ci, x.Q(0.76f, 0.5f, 0.9f, 0.86f), 4, false, MTrim, x.Px(0.8f)); // 압축기 숨구멍
        Line(ci, x.P(0.14f, 0.5f), x.P(0.72f, 0.5f), MTrim, x.Px(0.6f)); // 칸막이
        if (x.Tier >= 2) Box(ci, x.Q(0.08f, 0.02f, 0.3f, 0.1f), new Color("#3a7ab8"), 1f); // II: 보조 냉각기
        if (x.Tier >= 3) Line(ci, x.P(0.06f, 0.96f), x.P(0.94f, 0.96f), Cyan.WithAlpha(0.7f), x.Px(1f)); // III: 기록 띠
    }

    private static void CoolerLife(in Fix x)
    {
        var ci = x.Ci;
        bool cold = On2(x);
        int slot = 0;
        if (x.W != null)
            foreach (var g in x.W.Transplant.Grafts)
            {
                if (g.Gone || g.Cooler != x.F.Id || slot >= 6) continue;
                var p = x.P(0.22f + (slot % 3) * 0.2f, slot < 3 ? 0.34f : 0.66f);
                var col = g.Organ switch { Organ.Heart => new Color("#b0202a"), Organ.Lungs => new Color("#e08a9a"), Organ.Liver => new Color("#6a2a1a"), _ => new Color("#9a3a3a") };
                Box(ci, new Rect2(p - new Vector2(x.Px(3f), x.Px(3f)), new Vector2(x.Px(6f), x.Px(6f))), new Color("#e8f4ff").WithAlpha(0.8f), 1.5f, MTrim); // 통
                if (g.Organ == Organ.Kidney) { Dot(ci, p + new Vector2(-x.Px(0.6f), 0f), x.Px(1.8f), col); Dot(ci, p + new Vector2(x.Px(0.8f), -x.Px(0.6f)), x.Px(1.2f), col); }
                else if (g.Organ == Organ.Lungs) { Dot(ci, p + new Vector2(-x.Px(1.1f), 0f), x.Px(1.4f), col); Dot(ci, p + new Vector2(x.Px(1.1f), 0f), x.Px(1.4f), col); }
                else Dot(ci, p, x.Px(g.Organ == Organ.Liver ? 2.2f : 1.8f), col);
                float q = x.W.Transplant.Quality(g);
                ci.DrawRect(new Rect2(p + new Vector2(-x.Px(3f), x.Px(3.4f)), new Vector2(x.Px(6f) * q, x.Px(0.8f))), (q > 0.4f ? Good : Danger).WithAlpha(0.8f));
                slot++;
            }
        // 서리: 차가우면 반짝 · 꺼지면 녹아 물방울
        if (cold && x.Lod > 0) for (int k = 0; k < 5; k++) { float h = Hash(x.Id, k + (int)(x.T * 0.7f), 77); Dot(ci, x.P(0.16f + 0.54f * h, 0.2f + 0.6f * Hash(x.Id, k, 91)), x.Px(0.5f), MFrost.WithAlpha(0.5f * Pulse(x.T + k, 2f))); }
        var scr = x.Q(0.77f, 0.2f, 0.89f, 0.38f);
        bool warm = slot > 0 && !cold;
        Tag(ci, scr.GetCenter() + new Vector2(0f, x.Px(1f)), cold ? "4°" : warm ? "↑" : "", 3, (warm ? Danger : Cyan) * new Color(1, 1, 1, warm ? (Pulse(x.T, 8f) > 0.5f ? 1f : 0.3f) : x.Glow));
    }

    private static void CoolerFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.06f, 0.1f, 0.94f, 0.92f), 1.4f, 0.35f);
        Line(ci, x.P(0.3f, 0.1f), x.P(0.56f, 0.1f), Steel4, x.Px(1.4f)); // 손잡이
        Line(ci, x.P(0.14f, 0.86f), x.P(0.72f, 0.86f), new Color("#c03030").WithAlpha(0.7f), x.Px(0.6f)); // 붉은 띠
    }

    // ─────────────── 바이오 프린터 ───────────────

    private static void PrinterBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.06f, 0.06f, 0.94f, 0.94f), Steel1, 2f, Chrome, 1); // 틀
        Glass(ci, x.Q(0.12f, 0.12f, 0.88f, 0.8f), new Color("#a8e0ff").WithAlpha(0.18f), 1.5f); // 유리 상자
        Line(ci, x.P(0.14f, 0.24f), x.P(0.86f, 0.24f), Chrome, x.Px(1.2f)); // 가로대
        Box(ci, x.Q(0.38f, 0.62f, 0.62f, 0.72f), new Color("#ccd6dc"), 1f, Steel4); // 받침 접시
        Can(ci, x.P(0.2f, 0.88f), x.Px(2f), MInkA, Chrome); // 잉크 통
        Can(ci, x.P(0.32f, 0.88f), x.Px(2f), MInkB, Chrome);
        Box(ci, x.Q(0.6f, 0.84f, 0.88f, 0.92f), MScreen, 1f);
        if (x.Tier >= 2) Can(ci, x.P(0.44f, 0.88f), x.Px(2f), new Color("#7ad0ff"), Chrome); // II: 셋째 잉크
        if (x.Tier >= 3) Line(ci, x.P(0.12f, 0.12f), x.P(0.12f, 0.8f), new Color("#b07aff").WithAlpha(0.6f), x.Px(1f)); // III: 굳히는 빛 줄
    }

    private static void PrinterLife(in Fix x)
    {
        var ci = x.Ci;
        PrintJob? job = null;
        if (x.W != null) foreach (var j in x.W.Transplant.Prints) if (j.Printer == x.F.Id && j.Started && !j.Failed) { job = j; break; }
        bool done = false;
        if (x.W != null) foreach (var g in x.W.Transplant.Grafts) if (!g.Gone && g.Printer == x.F.Id) { done = true; break; }
        bool run = job != null && On2(x);
        float prog = done ? 1f : job?.Progress ?? 0f;
        // 층층이 자라는 장기 (신장꼴)
        var bed = x.P(0.5f, 0.62f);
        int layers = Mathf.Clamp((int)(prog * 8f), 0, 8);
        for (int k = 0; k < layers; k++)
        {
            float wv = x.Px(5.5f) * (0.6f + 0.4f * Mathf.Sin((k + 1) / 9f * Mathf.Pi));
            var col = MTissue.Lerp(new Color("#e89aa8"), k / 8f);
            Box(ci, new Rect2(bed - new Vector2(wv, x.Px(1.1f) * (k + 1)), new Vector2(wv * 2f, x.Px(1f))), col.WithAlpha(0.9f), 0.5f);
        }
        if (done) Ring(ci, bed - new Vector2(0f, x.Px(5f)), x.Px(6.5f), Good.WithAlpha(0.4f + 0.3f * Pulse(x.T, 2f)), x.Px(0.6f));
        // 머리: 찍는 동안 앞뒤로 · 층마다 내려온다
        float hx = run ? 0.3f + 0.4f * (0.5f + 0.5f * Mathf.Sin(x.T * 3.1f)) : 0.5f;
        var head = x.P(hx, 0.24f);
        Box(ci, new Rect2(head - new Vector2(x.Px(2.2f), x.Px(1.6f)), new Vector2(x.Px(4.4f), x.Px(3.2f))), Steel3, 1f, Chrome);
        var nozzle = head + new Vector2(0f, x.Px(1.6f));
        float drop = (bed.Y - x.Px(1.1f) * (layers + 1)) - nozzle.Y;
        if (run) { Line(ci, nozzle, nozzle + new Vector2(0f, drop), MInkA.WithAlpha(0.5f), x.Px(0.4f)); Dot(ci, nozzle + new Vector2(0f, drop), x.Px(0.6f), MInkA); }
        var scr = x.Q(0.62f, 0.85f, 0.86f, 0.91f);
        if (x.M?.Powered == true) ci.DrawRect(new Rect2(scr.Position, new Vector2(scr.Size.X * prog, scr.Size.Y)), (run ? MWave : Steel4).WithAlpha(0.8f * x.Glow));
        if (job != null && !On2(x)) Led(ci, x.P(0.9f, 0.12f), Danger, Pulse(x.T, 7f) > 0.5f ? 1f : 0.15f, x.Px(1.4f)); // 세포가 식는다
    }

    private static void PrinterFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.06f, 0.06f, 0.94f, 0.94f), 1f, 0.3f);
        for (int k = 0; k < 6; k++) Line(ci, x.P(0.14f + k * 0.14f, 0.22f), x.P(0.14f + k * 0.14f, 0.26f), Steel4, x.Px(0.3f)); // 눈금
        Tag(ci, x.P(0.74f, 0.8f), "37°", 2, Cream.WithAlpha(0.5f));
    }

    // ─────────────── 음압기 ───────────────

    private static void NegBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.04f, 0.08f, 0.96f, 0.92f), new Color("#d4dade"), 3f, Steel4, 1);
        Dot(ci, x.P(0.36f, 0.5f), x.Px(7f), Steel1); // 팬 구멍
        Ring(ci, x.P(0.36f, 0.5f), x.Px(7f), Steel4, x.Px(1f));
        Grille(ci, new Rect2(x.P(0.36f, 0.5f) - new Vector2(x.Px(6f), x.Px(6f)), new Vector2(x.Px(12f), x.Px(12f))), x.Px(2f), Steel3.WithAlpha(0.6f), x.Px(0.5f));
        var hepa = x.Q(0.66f, 0.16f, 0.9f, 0.62f); // 주름 필터
        Box(ci, hepa, MHepa, 1f, Steel3);
        for (int k = 1; k < 7; k++) { float u = hepa.Position.X + hepa.Size.X * k / 7f; Line(ci, new Vector2(u, hepa.Position.Y), new Vector2(u, hepa.End.Y), Steel3.WithAlpha(0.6f), x.Px(0.5f)); }
        Pipe(ci, x.P(0.78f, 0.08f), x.P(0.78f, -0.05f), x.Px(2.5f), Steel3, true); // 벽으로 빼는 관
        if (x.Tier >= 2) Box(ci, x.Q(0.66f, 0.66f, 0.9f, 0.74f), new Color("#6a3ab8").WithAlpha(0.7f), 1f); // II: 살균 빛
        if (x.Tier >= 3) Ring(ci, x.P(0.36f, 0.5f), x.Px(8.6f), Cyan.WithAlpha(0.5f), x.Px(0.6f)); // III: 차압 감지 고리
    }

    private static void NegLife(in Fix x)
    {
        var ci = x.Ci;
        bool on = On2(x);
        bool active = on && x.M!.Active;
        bool sick = false;
        if (x.W != null) foreach (var c in x.W.Crew) if (c.Room == x.F.Room && x.W.Infection.Contagious(c) != null) { sick = true; break; }
        Fan(ci, x.P(0.36f, 0.5f), x.Px(5.5f), 5, x.Ang(active ? 12f : on ? 2f : 0f), Chrome, x.Px(1.3f));
        Gauge(ci, x.P(0.78f, 0.82f), x.Px(3.2f), active ? 0.18f : on ? 0.45f : 0.5f, active ? MNegBlue : Steel4); // 음압 바늘
        Led(ci, x.P(0.12f, 0.16f), sick ? MNegRed : MNegBlue, on ? (sick ? 0.6f + 0.4f * Pulse(x.T, 3f) : 0.6f) * x.Glow : 0.05f, x.Px(1.6f));
        if (!active || x.Lod <= 0) return;
        // 빨려 드는 공기: 방 쪽에서 팬으로
        var c0 = x.P(0.36f, 0.5f);
        for (int k = 0; k < 8; k++)
        {
            float ang = k * Mathf.Tau / 8f + 0.3f;
            float u = 1f - Mathf.PosMod(x.T * 0.6f + k * 0.13f, 1f);
            var p = c0 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * x.Px(9f + 14f * u);
            Dot(ci, p, x.Px(0.6f), NoiseWhite.WithAlpha(0.35f * (1f - u) + 0.1f));
        }
    }

    private static void NegFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.Q(0.04f, 0.08f, 0.96f, 0.92f), 1.2f, 0.35f);
        Tag(ci, x.P(0.78f, 0.7f), "−Pa", 2, Steel3);
        foreach (float u in new[] { 0.08f, 0.64f }) Bolt(ci, x.P(u, 0.5f), 0.4f);
    }
}
