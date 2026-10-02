using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5c 그림 표 — 동력 · 냉각 · 감시 (12종).
/// 원자로(팔각 격납 · 제어봉 구동부 · 체렌코프 빛) · 엔진(터빈 날개 · 연료관 · 분사 불꽃) · 보조 발전기(냉각 핀 · 플라이휠 · 배기)
/// · 배전반(차단기 줄 · 구리 모선 · 회로 스위치) · 배터리(원통 셀 격자 · 충전 화살) · 축전기(K자 통기 깡통 · 충전 고리)
/// · 서지 보호기(번개 표 · 바리스터 원판 · 접지선) · 냉각 펌프(나선 케이싱 · 임펠러) · 열교환기(판 묶음 · 뜨겁고 찬 노즐)
/// · 원자로 모의 장치(미믹 판 · 실제 노심을 따라 하는 빛) · 열화상 카메라(삼각대 · 훑는 시야 · 열 미리보기) · 진동 감시기(기록지 드럼 · 펜).
/// </summary>
public static partial class FixtureArt
{
    private static void PowerArt(System.Collections.Generic.Dictionary<FurnitureType, Art> t)
    {
        t[FurnitureType.ReactorCore] = new(ReactorBody, ReactorLife, ReactorFine, Look.Steam, 0.85f, 0.15f);
        t[FurnitureType.EngineCore] = new(EngineBody, EngineLife, EngineFine, Look.Smoke, 0.5f, 0.5f);
        t[FurnitureType.AuxGenerator] = new(AuxGenBody, AuxGenLife, AuxGenFine, Look.Smoke, 0.8f, 0.1f);
        t[FurnitureType.PowerPanel] = new(PanelBody, PanelLife, PanelFine, Look.Sparks, 0.5f, 0.5f);
        t[FurnitureType.Battery] = new(BatteryBody, BatteryLife, BatteryFine, Look.Heat, 0.5f, 0.5f);
        t[FurnitureType.CapacitorBank] = new(CapacitorBody, CapacitorLife, CapacitorFine, Look.Sparks, 0.5f, 0.2f);
        t[FurnitureType.SurgeProtector] = new(SurgeBody, SurgeLife, SurgeFine, Look.Sparks, 0.5f, 0.75f);
        t[FurnitureType.CoolantPump] = new(PumpBody, PumpLife, PumpFine, Look.Grind, 0.5f, 0.5f);
        t[FurnitureType.HeatExchanger] = new(ExchangerBody, ExchangerLife, ExchangerFine, Look.Leak, 0.5f, 0.85f);
        t[FurnitureType.ReactorSimulator] = new(SimulatorBody, SimulatorLife, SimulatorFine, Look.Flicker, 0.5f, 0.4f);
        t[FurnitureType.ThermalCamera] = new(ThermalBody, ThermalLife, ThermalFine, Look.Flicker, 0.7f, 0.5f);
        t[FurnitureType.VibrationMonitor] = new(VibrationBody, VibrationLife, VibrationFine, Look.Flicker, 0.6f, 0.5f);
    }

    private static readonly Color Cherenkov = new("#5fb8ff");
    private static readonly Color CoreHot = new("#ffcf6a");
    private static readonly Color HotPipe = new("#e05a3a");
    private static readonly Color ColdPipe = new("#3a8fd9");
    private static readonly Color Bus = new("#c87a3a");
    private const float Nozzle = ShipView.T * 0.85f;

    private static float ReactorLoad(World w) => Mathf.Clamp(w.Power.ReactorOutput / Mathf.Max(1f, w.Power.ReactorRated), 0.05f, 1f);

    // ─────────────── 원자로 ───────────────

    private static void ReactorBody(in Fix x)
    {
        var ci = x.Ci;
        float rs = Mathf.Min(x.R.Size.X, x.R.Size.Y) / (3f * ShipView.T);
        var c = x.C;
        Box(ci, x.R.Grow(-4f), new Color("#1d1912"), 14, new Color("#4a3f22"), 2);
        foreach (var corner in new[] { x.B.Position, new Vector2(x.B.End.X - 12f * rs, x.B.Position.Y), new Vector2(x.B.Position.X, x.B.End.Y - 12f * rs), x.B.End - new Vector2(12f, 12f) * rs })
        {
            var fl = new Rect2(corner + new Vector2(2f, 2f), new Vector2(10f, 10f) * rs);
            Box(ci, fl, new Color("#2a2418"), 2, new Color("#5a4a2a")); // 모서리 고정 받침
        }
        for (int k = 0; k < 4; k++) // 냉각관
        {
            var d = Vector2.FromAngle(Mathf.Pi * 0.25f + k * Mathf.Pi * 0.5f);
            Pipe(ci, c + d * 30f * rs, c + d * 44f * rs, 4.5f * rs, new Color("#4a3f22"));
        }
        var oct = new Vector2[8];
        for (int k = 0; k < 8; k++) oct[k] = c + Vector2.FromAngle(Mathf.Pi / 8f + k * Mathf.Tau / 8f) * 36f * rs;
        ci.DrawColoredPolygon(oct, new Color("#221e17")); // 팔각 격납 용기
        var ring = new Vector2[9];
        for (int k = 0; k < 9; k++) ring[k] = oct[k % 8];
        ci.DrawPolyline(ring, new Color("#5a4a2a"), 1.6f, true);
        ci.DrawCircle(c, 30f * rs, new Color("#28221a"), true, -1f, true);
        ci.DrawArc(c, 30f * rs, 0f, Mathf.Tau, 48, new Color("#5a4a2a"), 1.5f, true);
        ci.DrawCircle(c, 13f * rs, new Color("#14110c"), true, -1f, true); // 노심 우물
        for (int k = 0; k < 8; k++) // 제어봉 구동부
            Can(ci, c + Vector2.FromAngle(k * Mathf.Tau / 8f) * 21f * rs, 3.2f * rs, new Color("#3a3226"), Brass);
        // 방사능 표시 (모서리)
        var tre = x.P(0.1f, 0.88f);
        Dot(ci, tre, 4.5f * rs, WarnYellow);
        for (int k = 0; k < 3; k++)
        {
            float a = -Mathf.Pi / 2f + k * Mathf.Tau / 3f;
            ci.DrawArc(tre, 2.6f * rs, a - 0.5f, a + 0.5f, 6, WarnBlack, 2.6f * rs, true);
        }
        Dot(ci, tre, 0.8f * rs, WarnBlack);
    }

    private static void ReactorLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        var pw = x.W.Power;
        float rs = Mathf.Min(x.R.Size.X, x.R.Size.Y) / (3f * ShipView.T);
        var c = x.C;
        float load = ReactorLoad(x.W);
        bool online = pw.ReactorOnline;
        bool hot = pw.ReactorTemperature > 380f;
        var col = hot ? Danger : CoreHot;
        float pulse = 0.5f + 0.5f * Mathf.Sin(x.T * (1f + load));
        float on = online ? 1f : 0.15f;
        Dot(ci, c, 26f * rs, Cherenkov.WithAlpha((0.05f + 0.06f * pulse) * load * on)); // 체렌코프 푸른 빛
        Dot(ci, c, 12f * rs, col.WithAlpha((0.25f + 0.15f * pulse) * load * on + 0.04f));
        Dot(ci, c, (5f + 5f * load + 1.2f * pulse) * rs * (online ? 1f : 0.5f), col.WithAlpha(0.9f * on + 0.1f));
        Dot(ci, c, 3.5f * rs, new Color(1f, 0.96f, 0.86f, 0.9f * on));
        // 제어봉: 출력이 낮을수록 깊이 꽂힌다 (구동부에서 노심 쪽으로 뻗은 막대)
        float insert = online ? 1f - load : 1f;
        for (int k = 0; k < 8; k++)
        {
            var d = Vector2.FromAngle(k * Mathf.Tau / 8f);
            var a = c + d * 21f * rs;
            Line(ci, a, a - d * (2f + 6f * insert) * rs, new Color("#c8b27a").WithAlpha(0.85f), 1.6f * rs);
            Led(ci, a + d * 3.6f * rs, online ? Good : Danger, x.Lod > 0 ? 0.6f : 0f, 0.8f * rs);
        }
        if (online && x.Lod > 0)
        {
            float a0 = x.T * 0.7f * load;
            ci.DrawArc(c, 25f * rs, a0, a0 + 1.3f, 20, Cherenkov.WithAlpha(0.55f), 2f * rs, true);
            ci.DrawArc(c, 25f * rs, a0 + Mathf.Pi, a0 + Mathf.Pi + 1.3f, 20, Cherenkov.WithAlpha(0.55f), 2f * rs, true);
        }
        if (x.Tier == 2) ci.DrawArc(c, 33f * rs, 0f, Mathf.Tau, 48, Hud.TierColor(2).WithAlpha(0.35f), 1.5f, true); // 개량형: 보강 링
        if (hot && x.Lod > 0) Shimmer(ci, new Rect2(c - new Vector2(20f, 20f) * rs, new Vector2(40f, 20f) * rs), x.T, 0.8f);
    }

    private static void ReactorFine(in Fix x)
    {
        var ci = x.Ci;
        float rs = Mathf.Min(x.R.Size.X, x.R.Size.Y) / (3f * ShipView.T);
        for (int k = 0; k < 16; k++) Bolt(ci, x.C + Vector2.FromAngle(k * Mathf.Tau / 16f) * 33f * rs, 0.8f * rs);
        Plate(ci, new Rect2(x.P(0.35f, 0.92f), new Vector2(20f, 4f) * rs), new Color("#c8b27a"));
        Tag(ci, x.P(0.5f, 0.05f) + new Vector2(0f, 4f), "REACTOR", 6, WarnYellow.WithAlpha(0.6f));
    }

    // ─────────────── 엔진 ───────────────

    private static void EngineBody(in Fix x)
    {
        var ci = x.Ci;
        var r = x.R;
        var c = x.C;
        Box(ci, r.Grow(-4f), new Color("#1f1716"), 10, new Color("#4b302b"), 2);
        float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.38f;
        ci.DrawCircle(c, rad, new Color("#150f0e"), true, -1f, true);
        ci.DrawArc(c, rad, 0f, Mathf.Tau, 40, new Color("#5a3a33"), 2.5f, true);
        ci.DrawArc(c, rad * 0.7f, 0f, Mathf.Tau, 32, new Color("#3a2724"), 1.5f, true);
        for (int k = 0; k < 10; k++)
        {
            var d = Vector2.FromAngle(k * Mathf.Tau / 10f);
            var d2 = Vector2.FromAngle(k * Mathf.Tau / 10f + 0.35f);
            ci.DrawLine(c + d * rad * 0.28f, c + d2 * rad * 0.92f, new Color("#4b302b"), 2f, true);
        }
        ci.DrawCircle(c, rad * 0.22f, new Color("#3a2724"), true, -1f, true);
        for (int k = 0; k < 8; k++)
            Dot(ci, c + Vector2.FromAngle(k * Mathf.Tau / 8f + 0.2f) * (rad + 5f), 1.5f, new Color("#6b4a3f"));
        Pipe(ci, new Vector2(r.End.X - 6, r.Position.Y + 8), new Vector2(r.End.X - 6, r.End.Y - 8), 3f, new Color("#6b4a3f")); // 연료 공급관
        Pipe(ci, new Vector2(r.End.X - 6, c.Y), new Vector2(c.X + rad, c.Y), 2.5f, new Color("#6b4a3f"), false);
        ci.DrawRect(new Rect2(r.Position.X + 6, r.Position.Y + 8, 10, r.Size.Y - 16), new Color("#1a1210")); // 노즐 쪽 점화실
    }

    private static void EngineLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        var f = x.F;
        var r = x.R;
        var prop = x.W.Propulsion;
        var m = x.M;
        bool alive = x.Eff > 0.01f;
        bool burn = prop.Burning || prop.CourseBurnVisible;
        bool standby = m != null && !m.Stopped && (m.Powered || m.Spec.PowerDraw <= 0f);
        float power = burn && alive ? 1f : standby ? 0.16f : 0f;
        var accent = x.Accent;
        float t = x.T;
        // 터빈이 돈다 (연소 중엔 빠르게)
        float rad = Mathf.Min(r.Size.X, r.Size.Y) * 0.38f;
        float spin = t * (burn ? 9f : power > 0f ? 0.8f : 0f);
        for (int k = 0; k < 3; k++)
        {
            var d = Vector2.FromAngle(spin + k * Mathf.Tau / 3f);
            Line(ci, x.C + d * rad * 0.3f, x.C + d.Rotated(0.35f) * rad * 0.9f, accent.WithAlpha(0.25f + 0.45f * power), 2f);
        }
        float x0 = (f.MinX - 1) * ShipView.T - 8f - Nozzle; // 노즐 끝에서 나온다
        float y0 = f.MinY * ShipView.T + 4f, y1 = (f.MaxY + 1) * ShipView.T - 4f;
        float yc = (y0 + y1) * 0.5f, h = y1 - y0;
        float flicker = 0.88f + 0.08f * Mathf.Sin(t * 11f) + 0.04f * Mathf.Sin(t * 23f);
        if (power > 0f)
        {
            float len = ShipView.T * (burn ? 3.4f : 0.5f) * flicker * (0.4f + 0.6f * Mathf.Max(0.3f, prop.Thrust));
            var plume = new[] { new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x0 - len, yc + h * 0.12f), new Vector2(x0 - len, yc - h * 0.12f) };
            ci.DrawPolygon(plume, new[] { accent.WithAlpha(0.5f), accent.WithAlpha(0.5f), accent.WithAlpha(0f), accent.WithAlpha(0f) });
            float len2 = len * 0.55f;
            var core = new[] { new Vector2(x0, yc - h * 0.25f), new Vector2(x0, yc + h * 0.25f), new Vector2(x0 - len2, yc + 2f), new Vector2(x0 - len2, yc - 2f) };
            var hotc = new Color(1f, 0.86f, 0.72f);
            ci.DrawPolygon(core, new[] { hotc.WithAlpha(0.75f), hotc.WithAlpha(0.75f), hotc.WithAlpha(0f), hotc.WithAlpha(0f) });
            ci.DrawLine(new Vector2(x0, y0 - 2f), new Vector2(x0, y1 + 2f), accent.Lightened(0.3f).WithAlpha((burn ? 0.8f : 0.25f) * flicker), 2f);
        }
        if (burn && x.Lod > 0)
            for (int k = 0; k < 4; k++)
            {
                float ph = (t * 3f + k * 0.25f) % 1f;
                Dot(ci, new Vector2(x0 - ShipView.T * 3.4f * ph, yc + (k % 2 == 0 ? -1f : 1f) * h * 0.2f * ph), 3f + 7f * ph, new Color(1f, 0.8f, 0.6f, 0.25f * (1f - ph)));
            }
        ci.DrawRect(new Rect2(r.Position.X + 8, r.Position.Y + 10, 6, r.Size.Y - 20), accent.WithAlpha(power * (0.35f + 0.25f * flicker))); // 점화실 불빛
        // 연료 흐름: 공급관을 따라 내려가는 점
        if (power > 0f && x.Lod > 0)
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(t * (burn ? 1.5f : 0.3f) + k / 3f, 1f);
                Dot(ci, new Vector2(r.End.X - 6, Mathf.Lerp(r.Position.Y + 8, r.End.Y - 8, ph)), 1.2f, Amber.WithAlpha(0.8f));
            }
    }

    private static void EngineFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.R.Grow(-4f), 4f, 1f);
        float rad = Mathf.Min(x.R.Size.X, x.R.Size.Y) * 0.38f;
        for (int k = 0; k < 20; k++) Dot(ci, x.C + Vector2.FromAngle(k * Mathf.Tau / 20f) * (rad - 1.5f), 0.6f, new Color("#7a5a4f"));
        Tag(ci, new Vector2(x.R.GetCenter().X, x.R.End.Y - 8f), "THRUST", 6, new Color(1, 1, 1, 0.3f));
    }

    // ─────────────── 보조 발전기 ───────────────

    private static void AuxGenBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1f1f17"), 5, new Color("#4d4a2c"), 2);
        var block = x.Q(0.32f, 0.14f, 0.66f, 0.86f);
        Box(ci, block, new Color("#2a291c"), 2, new Color("#5a5634"));
        for (int k = 0; k < 7; k++) Line(ci, x.P(0.34f + k * 0.045f, 0.16f), x.P(0.34f + k * 0.045f, 0.84f), new Color("#1a1a12"), 1.1f); // 냉각 핀
        var wheel = x.P(0.17f, 0.5f);
        float wr = x.Lv * 0.38f;
        Dot(ci, wheel, wr, new Color("#15150f"));
        Ring(ci, wheel, wr, new Color("#5a5634"), 1.5f, 28);
        Ring(ci, wheel, wr * 0.55f, new Color("#3a3824"), 1f, 20);
        var tank = x.Q(0.72f, 0.14f, 0.96f, 0.86f);
        Box(ci, tank, new Color("#2a2418"), 4, new Color("#5a4a2a")); // 연료 탱크
        Dot(ci, x.P(0.84f, 0.2f), 1.8f, new Color("#c0392b")); // 주입구 마개
        Pipe(ci, x.P(0.6f, 0.1f), x.P(0.6f, -0.12f), 2.4f, new Color("#4a4a3a"), false); // 배기관
        Line(ci, x.P(0.12f, 0.92f), x.P(0.28f, 0.92f), WarnYellow.WithAlpha(0.55f), 2f); // 시동 손잡이
    }

    private static void AuxGenLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        var p = x.W.Power;
        bool run = p.AuxRunning;
        var wheel = x.P(0.17f, 0.5f);
        float wr = x.Lv * 0.38f;
        float spin = run ? x.T * 9f : 0.4f;
        var col = run ? new Color("#f5d547") : new Color("#6b6a4a");
        for (int k = 0; k < 4; k++)
        {
            var d = Vector2.FromAngle(spin + k * Mathf.Pi * 0.5f);
            Line(ci, wheel + d * wr * 0.3f, wheel + d * wr * 0.85f, col.WithAlpha(0.8f), 2f);
        }
        var tank = x.Q(0.75f, 0.24f, 0.93f, 0.8f);
        float fuel = Mathf.Clamp(p.AuxFuel / PowerGrid.AuxFuelHours, 0f, 1f);
        var fr = x.Wide ? new Rect2(tank.Position.X, tank.End.Y - tank.Size.Y * fuel, tank.Size.X, tank.Size.Y * fuel)
                        : new Rect2(tank.Position.X, tank.Position.Y, tank.Size.X * fuel, tank.Size.Y);
        ci.DrawRect(fr, (fuel < 0.2f ? Danger : WarnYellow).WithAlpha(0.6f));
        if (run)
        {
            float j = Mathf.Sin(x.T * 31f) * 0.8f; // 떨림
            Box(ci, x.Q(0.32f, 0.14f, 0.66f, 0.86f), new Color(0, 0, 0, 0f), 2, new Color("#7a7650").WithAlpha(0.5f));
            Line(ci, x.P(0.32f, 0.14f) + new Vector2(j, 0f), x.P(0.66f, 0.14f) + new Vector2(j, 0f), new Color(1, 1, 1, 0.12f), 1f);
            for (int k = 0; k < (x.Lod == 0 ? 1 : 3); k++)
            {
                float ph = Mathf.PosMod(x.T * 1.2f + k / 3f, 1f);
                Dot(ci, x.P(0.6f, -0.12f) + new Vector2(Mathf.Sin(ph * 6f) * 3f, -ph * 18f), 2f + 4f * ph, new Color(0.5f, 0.5f, 0.5f, 0.3f * (1f - ph)));
            }
        }
    }

    private static void AuxGenFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2.2f, 0.7f);
        Bolts(ci, x.Q(0.32f, 0.14f, 0.66f, 0.86f), 2f, 0.6f);
        Tag(ci, x.P(0.84f, 0.6f), "F", 6, new Color(1, 1, 1, 0.35f));
    }

    // ─────────────── 배전반 ───────────────

    private static float SwitchU(int k) => 0.12f + k * 0.76f / (PowerGrid.CircuitCount - 1);

    private static void PanelBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1b2029"), 4, new Color("#4a4a36"), 2);
        Bevel(ci, x.B);
        Line(ci, x.P(0.03f, 0.12f), x.P(0.97f, 0.12f), Bus, 2f); // 구리 모선
        Line(ci, x.P(0.03f, 0.86f), x.P(0.97f, 0.86f), Bus.Darkened(0.2f), 2f);
        for (int k = 0; k < PowerGrid.CircuitCount; k++)
        {
            var sw = x.Q(SwitchU(k) - 0.055f, 0.2f, SwitchU(k) + 0.055f, 0.72f);
            Box(ci, sw, new Color("#11151b"), 2, new Color("#2a3240"));
            ci.DrawRect(x.Q(SwitchU(k) - 0.06f, 0.76f, SwitchU(k) + 0.06f, 0.82f), ShipView.CircuitColors[k].WithAlpha(0.65f)); // 회로 색 띠
            Line(ci, x.P(SwitchU(k), 0.12f), x.P(SwitchU(k), 0.2f), Bus, 1f);
        }
        for (int k = 0; k < 6; k++) Line(ci, x.P(0.9f + k * 0.012f, 0.3f), x.P(0.9f + k * 0.012f, 0.6f), new Color("#0a0d12"), 1f); // 통풍 틈
        ci.DrawRect(x.Q(0.02f, 0.9f, 0.98f, 0.96f), WarnYellow.WithAlpha(0.3f));
    }

    private static void PanelLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        var pw = x.W.Power;
        int dead = 0;
        for (int k = 0; k < PowerGrid.CircuitCount; k++)
        {
            bool live = pw.CircuitLive[k], manual = pw.ManualOff[k];
            bool jump = !live && pw.CircuitFed[k];
            bool up = live && !manual || jump;
            if (!live && !manual && !jump) dead++;
            var col = manual ? Palette.TextMuted : jump ? ShipView.JumperColor : live ? Palette.Good : Palette.Danger;
            float blink = live || manual || jump ? 1f : 0.5f + 0.5f * Mathf.Sin(x.T * 8f);
            var lever = x.Q(SwitchU(k) - 0.035f, up ? 0.24f : 0.5f, SwitchU(k) + 0.035f, up ? 0.44f : 0.68f);
            ci.DrawRect(lever, col.WithAlpha(blink));
        }
        // 전압계: 걸린 부하만큼 바늘
        float load = Mathf.Clamp(pw.ReactorOutput / Mathf.Max(1f, pw.ReactorRated), 0f, 1f);
        Gauge(ci, x.P(0.93f, 0.75f), Mathf.Min(4f, x.Lv * 0.16f), load + Mathf.Sin(x.T * 5f) * 0.02f, new Color("#c0392b"));
        if (dead > 0 && x.Lod > 0 && Mathf.PosMod(x.T * 0.9f, 1f) < 0.08f) // 끊긴 회로: 단자에서 파직
        {
            var e = x.P(0.5f, 0.12f);
            for (int i = 0; i < 4; i++) Line(ci, e, e + Vector2.FromAngle(i * 1.6f + x.T * 5f) * 4f, SparkHot, 1f);
        }
    }

    private static void PanelFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2.2f, 0.7f);
        for (int k = 0; k < PowerGrid.CircuitCount; k++)
            Tag(ci, x.P(SwitchU(k), 0.18f) - x.V * 3f, ((char)('A' + k)).ToString(), 5, new Color(1, 1, 1, 0.45f));
        for (int k = 0; k < 8; k++) Bolt(ci, x.P(0.06f + k * 0.125f, 0.12f), 0.5f);
    }

    // ─────────────── 배터리 ───────────────

    private static (int cols, int rows) BatteryGrid(in Fix x) => x.F.Width >= 2 ? (Mathf.Max(2, (int)(x.Lu / 13f)), Mathf.Max(2, (int)(x.Lv / 13f))) : (2, 2);

    private static Vector2 CellAt(in Fix x, int i, int j, int cols, int rows) => x.P(0.12f + 0.76f * (i + 0.5f) / cols, 0.1f + 0.72f * (j + 0.5f) / rows);

    private static void BatteryBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#18211d"), 5, new Color(x.F.Improved ? "#4a7a8a" : "#2f4a3a"), 2);
        var (cols, rows) = BatteryGrid(x);
        float cr = Mathf.Min(x.Lu * 0.76f / cols, x.Lv * 0.72f / rows) * 0.38f;
        for (int j = 0; j < rows; j++)
        {
            Line(ci, CellAt(x, 0, j, cols, rows), CellAt(x, cols - 1, j, cols, rows), Bus.WithAlpha(0.7f), 1.6f); // 셀 잇는 모선
            for (int i = 0; i < cols; i++)
            {
                var p = CellAt(x, i, j, cols, rows);
                Can(ci, p, cr, new Color("#101713"), new Color("#3a5a46"));
                Line(ci, p - new Vector2(cr * 0.4f, 0f), p + new Vector2(cr * 0.4f, 0f), new Color("#4a6a56"), 0.8f); // + 표시
                Line(ci, p - new Vector2(0f, cr * 0.4f), p + new Vector2(0f, cr * 0.4f), new Color("#4a6a56"), 0.8f);
            }
        }
        Box(ci, x.Q(0.04f, 0.86f, 0.4f, 0.96f), new Color("#0d120f"), 1.5f, new Color("#2f4a3a")); // 셀 관리 장치
    }

    private static void BatteryLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        var pw = x.W.Power;
        float charge = Mathf.Clamp(pw.BatteryPercent, 0f, 1f);
        var col = charge < 0.2f ? Danger : charge < 0.5f ? new Color("#f5d547") : Good;
        var (cols, rows) = BatteryGrid(x);
        int lit = Mathf.CeilToInt(charge * cols * rows);
        float cr = Mathf.Min(x.Lu * 0.76f / cols, x.Lv * 0.72f / rows) * 0.38f;
        int n = 0;
        for (int i = 0; i < cols; i++)
            for (int j = rows - 1; j >= 0; j--, n++)
                if (n < lit) Dot(ci, CellAt(x, i, j, cols, rows), cr * 0.55f, col.WithAlpha(0.55f * Mathf.Max(0.3f, x.Glow)));
        float flow = pw.BatteryFlow;
        if (Mathf.Abs(flow) > 0.1f && x.Lod > 0) // 충전은 안으로, 방전은 밖으로 흐르는 화살
        {
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(x.T * 0.8f + k / 3f, 1f);
                float u = flow > 0f ? 1f - ph : ph;
                var p = x.P(0.12f + 0.76f * u, 0.86f);
                var d = x.U * (flow > 0f ? -2f : 2f);
                Line(ci, p - d + x.V * 1.5f, p + d, (flow > 0f ? Good : Amber).WithAlpha(0.8f), 1f);
                Line(ci, p - d - x.V * 1.5f, p + d, (flow > 0f ? Good : Amber).WithAlpha(0.8f), 1f);
            }
        }
        Led(ci, x.P(0.08f, 0.91f), col, flow < -0.1f ? 0.5f + 0.5f * Pulse(x.T, 6f) : 0.8f, 1f);
    }

    private static void BatteryFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 2.2f, 0.7f);
        Tag(ci, x.P(0.7f, 0.91f), x.F.Improved ? "+증설" : "LiFe", 5, new Color(1, 1, 1, 0.35f));
    }

    // ─────────────── 축전기 ───────────────

    private static void CapacitorBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#161a12"), 3, new Color("#5d6b2c"), 2);
        float cr = Mathf.Min(x.Lu / 3.4f, x.Lv * 0.7f) * 0.5f;
        for (int k = 0; k < 3; k++)
        {
            var p = x.P((k + 0.5f) / 3f, 0.55f);
            Can(ci, p, cr, new Color("#2a3a5a"), new Color("#8aa0c8"));
            Line(ci, p - new Vector2(cr * 0.5f, cr * 0.5f), p + new Vector2(cr * 0.5f, cr * 0.5f), new Color("#14203a"), 1f); // K자 통기 홈
            Line(ci, p - new Vector2(cr * 0.5f, -cr * 0.5f), p + new Vector2(cr * 0.5f, -cr * 0.5f), new Color("#14203a"), 1f);
        }
        Line(ci, x.P(0.05f, 0.14f), x.P(0.95f, 0.14f), Bus, 2f); // 모선
        for (int k = 0; k < 3; k++) Line(ci, x.P((k + 0.5f) / 3f, 0.14f), x.P((k + 0.5f) / 3f, 0.55f) - x.V * cr, Bus.Darkened(0.2f), 1f);
        var zz = new Vector2[6]; // 방전 저항 (지그재그)
        for (int i = 0; i < 6; i++) zz[i] = x.P(0.2f + i * 0.12f, i % 2 == 0 ? 0.9f : 0.96f);
        ci.DrawPolyline(zz, new Color("#c8b27a"), 1f, true);
    }

    private static void CapacitorLife(in Fix x)
    {
        if (x.W == null) return;
        var ci = x.Ci;
        float charge = Mathf.Clamp(x.W.Power.BatteryPercent, 0f, 1f);
        float cr = Mathf.Min(x.Lu / 3.4f, x.Lv * 0.7f) * 0.5f;
        var col = x.On ? new Color("#c6e85a") : new Color("#5c6640");
        for (int k = 0; k < 3; k++)
        {
            var p = x.P((k + 0.5f) / 3f, 0.55f);
            ci.DrawArc(p, cr + 1.3f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * charge, 16, col.WithAlpha(0.85f * Mathf.Max(0.35f, x.Glow)), 1.4f, true);
            if (x.On && charge > 0.95f && x.Lod > 0 && Hash(x.Id, (int)(x.T * 6f) + k, 31) > 0.85f) // 가득 차면 가끔 코로나 반짝
                Dot(ci, p + Vector2.FromAngle(Hash(x.Id, k, 32) * Mathf.Tau) * cr, 1.2f, new Color("#e8ffb0"));
        }
    }

    private static void CapacitorFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.55f);
        float cr = Mathf.Min(x.Lu / 3.4f, x.Lv * 0.7f) * 0.5f;
        for (int k = 0; k < 3; k++) Tag(ci, x.P((k + 0.5f) / 3f, 0.55f) + x.V * (cr + 2.5f), "μF", 4, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 서지 보호기 ───────────────

    private static void SurgeBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#22262c"), 3, new Color("#6a7080"));
        Bevel(ci, x.B, 0.12f);
        var bolt = new[] { x.P(0.52f, 0.12f), x.P(0.36f, 0.42f), x.P(0.5f, 0.42f), x.P(0.42f, 0.66f), x.P(0.64f, 0.34f), x.P(0.5f, 0.34f), x.P(0.6f, 0.12f) };
        ci.DrawColoredPolygon(bolt, WarnYellow); // 번개 표
        for (int k = 0; k < 3; k++) // 바리스터 원판
        {
            var p = x.P(0.2f + k * 0.3f, 0.78f);
            Dot(ci, p, 2.6f, new Color("#2a5aa8"));
            Ring(ci, p, 2.6f, new Color("#7aa2e8"), 0.6f, 10);
        }
        Cable(ci, x.P(0.9f, 0.9f), x.R.End - new Vector2(1f, 1f), 1.5f, new Color("#4a8a3a"), 2f); // 접지선 (녹 · 황)
        Cable(ci, x.P(0.9f, 0.9f), x.R.End - new Vector2(1f, 1f), 1.5f, WarnYellow.WithAlpha(0.5f), 0.7f);
        Box(ci, x.Q(0.74f, 0.12f, 0.95f, 0.3f), GlassDark, 1); // 상태 창
    }

    private static void SurgeLife(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.Q(0.75f, 0.14f, 0.94f, 0.28f), (x.On ? Good : x.St == State.Fault ? Amber : Danger).WithAlpha(0.25f + 0.45f * Mathf.Max(x.Glow, 0.3f)), 1);
        if (!x.On) return;
        for (int k = 0; k < 3; k++) // 바리스터 차례로 깜빡 (감시 중)
        {
            bool on = (int)(x.T * 1.5f) % 3 == k;
            Dot(ci, x.P(0.2f + k * 0.3f, 0.78f), 1f, new Color("#9ac8ff").WithAlpha(on ? 0.85f : 0.15f));
        }
    }

    private static void SurgeFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.55f);
        for (int k = 0; k < 3; k++) Bolt(ci, x.P(0.2f + k * 0.3f, 0.92f), 0.5f);
        Tag(ci, x.P(0.84f, 0.4f), "SPD", 4, new Color(1, 1, 1, 0.45f));
    }

    // ─────────────── 냉각 펌프 ───────────────

    private static void PumpBody(in Fix x)
    {
        var ci = x.Ci;
        var c = x.C;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.42f;
        ci.DrawRect(new Rect2(c.X - 5, x.R.Position.Y - 4, 10, x.R.Size.Y * 0.4f), new Color("#223440")); // 벽으로 가는 관
        ci.DrawRect(new Rect2(c.X - 5, x.R.Position.Y - 4, 10, 3), new Color("#35505f"));
        var spiral = new Vector2[22]; // 나선 케이싱
        for (int i = 0; i < 22; i++)
        {
            float a = i / 21f * Mathf.Tau * 1.05f;
            spiral[i] = c + Vector2.FromAngle(a) * rr * (0.72f + 0.28f * i / 21f);
        }
        ci.DrawCircle(c, rr * 0.86f, new Color("#122029"), true, -1f, true);
        ci.DrawPolyline(spiral, new Color("#2d5263"), 2.5f, true);
        Pipe(ci, c + new Vector2(rr * 0.95f, -rr * 0.15f), new Vector2(x.B.End.X + 1f, c.Y - rr * 0.15f), 6f, new Color("#2d5263")); // 토출관
        ci.DrawArc(c, rr * 0.55f, 0f, Mathf.Tau, 32, new Color("#1f3a47"), 1.5f, true);
        var motor = new Rect2(new Vector2(x.B.Position.X, c.Y + rr * 0.5f), new Vector2(rr * 0.9f, rr * 0.5f)); // 모터 하우징 (핀)
        Box(ci, motor, new Color("#1a2a33"), 2, new Color("#35505f"));
        for (int k = 1; k < 5; k++) Line(ci, motor.Position + new Vector2(motor.Size.X * k / 5f, 1f), motor.Position + new Vector2(motor.Size.X * k / 5f, motor.Size.Y - 1f), new Color("#0f1a20"), 1f);
    }

    private static void PumpLife(in Fix x)
    {
        var ci = x.Ci;
        var c = x.C;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.42f;
        bool alive = x.Eff > 0.01f && !x.Dead;
        var col = alive ? x.Accent : Danger;
        float spin = x.Ang(6f) + x.Id;
        for (int k = 0; k < 5; k++) // 임펠러 (휜 날개)
        {
            float a = spin + k * Mathf.Tau / 5f;
            ci.DrawArc(c, rr * 0.35f, a, a + 0.9f, 6, col.WithAlpha(alive ? 0.75f : 0.45f), 2.2f, true);
            Line(ci, c + Vector2.FromAngle(a) * rr * 0.12f, c + Vector2.FromAngle(a + 0.4f) * rr * 0.5f, col.WithAlpha(alive ? 0.8f : 0.45f), 2f);
        }
        Dot(ci, c, 3.5f, col.WithAlpha(0.9f));
        if (alive && x.Lod > 0) // 토출관 속 흐름
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(x.T * 1.6f * x.Spin + k / 3f, 1f);
                var p = new Vector2(Mathf.Lerp(c.X + rr, x.B.End.X, ph), c.Y - rr * 0.15f);
                Line(ci, p + new Vector2(-1.5f, -1.5f), p, Cyan.WithAlpha(0.7f), 1f);
                Line(ci, p + new Vector2(-1.5f, 1.5f), p, Cyan.WithAlpha(0.7f), 1f);
            }
        Gauge(ci, new Vector2(x.B.End.X - 5f, x.B.Position.Y + 5f), 3.6f, alive ? 0.45f + 0.3f * x.Eff + Mathf.Sin(x.T * 3f) * 0.02f : 0.05f, new Color("#c0392b"));
    }

    private static void PumpFine(in Fix x)
    {
        var ci = x.Ci;
        float rr = Mathf.Min(x.B.Size.X, x.B.Size.Y) * 0.42f;
        for (int k = 0; k < 10; k++) Bolt(ci, x.C + Vector2.FromAngle(k * Mathf.Tau / 10f) * rr * 0.86f, 0.6f);
        Tag(ci, x.C + new Vector2(0f, rr * 0.7f), "→", 6, Cyan.WithAlpha(0.5f));
    }

    // ─────────────── 열교환기 ───────────────

    private static void ExchangerBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#10202a"), 3, new Color("#2f6b86"), 2);
        ci.DrawRect(x.Q(0.02f, 0.08f, 0.12f, 0.92f), new Color("#2a4a5a")); // 끝판
        ci.DrawRect(x.Q(0.88f, 0.08f, 0.98f, 0.92f), new Color("#2a4a5a"));
        for (int k = 0; k < 9; k++) // 판 묶음
        {
            float u = 0.16f + k * 0.085f;
            Line(ci, x.P(u, 0.12f), x.P(u, 0.88f), k % 2 == 0 ? new Color("#3f7f9a") : new Color("#23495c"), 1.6f);
        }
        Line(ci, x.P(0.02f, 0.2f), x.P(0.98f, 0.2f), Chrome.WithAlpha(0.5f), 0.8f); // 조임 봉
        Line(ci, x.P(0.02f, 0.8f), x.P(0.98f, 0.8f), Chrome.WithAlpha(0.5f), 0.8f);
        Ring(ci, x.P(0.07f, 0.2f), 2.6f, HotPipe, 1.6f, 12); // 노즐 넷 (뜨거운 쪽 빨강 · 찬 쪽 파랑)
        Ring(ci, x.P(0.07f, 0.8f), 2.6f, HotPipe.Darkened(0.2f), 1.6f, 12);
        Ring(ci, x.P(0.93f, 0.2f), 2.6f, ColdPipe, 1.6f, 12);
        Ring(ci, x.P(0.93f, 0.8f), 2.6f, ColdPipe.Lightened(0.2f), 1.6f, 12);
    }

    private static void ExchangerLife(in Fix x)
    {
        var ci = x.Ci;
        for (int k = 0; k < 5; k++)
        {
            float ph = Mathf.PosMod(x.T * 0.9f * x.Spin + k * 0.23f, 1f);
            float u = 0.16f + k * 0.17f;
            var col = HotPipe.Lerp(ColdPipe, ph);
            Dot(ci, x.P(u, 0.14f + 0.72f * ph), 1.6f, col.WithAlpha(x.On ? 0.85f : 0.25f));
        }
        if (x.On && x.Lod > 0)
        {
            Led(ci, x.P(0.07f, 0.2f), HotPipe, 0.4f + 0.2f * Pulse(x.T, 2f), 1f);
            Led(ci, x.P(0.93f, 0.8f), ColdPipe, 0.4f + 0.2f * Pulse(x.T + 1f, 2f), 1f);
        }
    }

    private static void ExchangerFine(in Fix x)
    {
        var ci = x.Ci;
        foreach (float u in new[] { 0.07f, 0.93f }) foreach (float v in new[] { 0.45f, 0.55f }) Bolt(ci, x.P(u, v), 0.55f);
        Tag(ci, x.P(0.5f, 0.5f), "HX", 5, new Color(1, 1, 1, 0.3f));
    }

    // ─────────────── 원자로 모의 장치 ───────────────

    private static void SimulatorBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1a1d24"), 4, new Color("#7a6a3a"), 2);
        var mimic = x.Q(0.08f, 0.08f, 0.92f, 0.6f);
        Box(ci, mimic, new Color("#0f1418"), 2, new Color("#3a4454")); // 미믹 판
        var core = mimic.GetCenter() - new Vector2(mimic.Size.X * 0.18f, 0f);
        Ring(ci, core, mimic.Size.Y * 0.28f, new Color("#4a5566"), 1f, 16);
        Line(ci, core + new Vector2(mimic.Size.Y * 0.28f, 0f), core + new Vector2(mimic.Size.X * 0.45f, 0f), ColdPipe.WithAlpha(0.6f), 1f); // 냉각 고리
        Line(ci, core + new Vector2(mimic.Size.X * 0.45f, 0f), core + new Vector2(mimic.Size.X * 0.45f, mimic.Size.Y * 0.3f), ColdPipe.WithAlpha(0.6f), 1f);
        var tri = core + new Vector2(mimic.Size.X * 0.45f, mimic.Size.Y * 0.32f);
        ci.DrawColoredPolygon(new[] { tri + new Vector2(-2f, 0f), tri + new Vector2(2f, 0f), tri + new Vector2(0f, 3f) }, ColdPipe.WithAlpha(0.6f)); // 펌프 기호
        for (int k = 0; k < 4; k++) Box(ci, x.Q(0.12f + k * 0.2f, 0.7f, 0.22f + k * 0.2f, 0.92f), new Color("#11151b"), 1.5f, new Color("#2a3240")); // 제어봉 스위치
    }

    private static void SimulatorLife(in Fix x)
    {
        var ci = x.Ci;
        if (!x.Lit) return;
        var mimic = x.Q(0.08f, 0.08f, 0.92f, 0.6f);
        var core = mimic.GetCenter() - new Vector2(mimic.Size.X * 0.18f, 0f);
        float load = x.W != null ? ReactorLoad(x.W) : 0.5f; // 진짜 원자로를 따라 한다
        Dot(ci, core, mimic.Size.Y * 0.22f * (0.4f + 0.6f * load), CoreHot.WithAlpha((0.4f + 0.3f * Pulse(x.T, 2f)) * x.Glow));
        for (int k = 0; k < 4; k++)
        {
            bool up = load > (k + 0.5f) / 4f;
            var r = x.Q(0.13f + k * 0.2f, up ? 0.72f : 0.81f, 0.21f + k * 0.2f, up ? 0.8f : 0.9f);
            ci.DrawRect(r, (up ? Good : Amber).WithAlpha(0.8f * x.Glow));
        }
        if (Mathf.PosMod(x.T, 1.4f) < 0.9f) Tag(ci, mimic.End - new Vector2(6f, 3f), "SIM", 4, Amber.WithAlpha(0.8f * x.Glow));
        float ph = Mathf.PosMod(x.T * 0.4f, 1f); // 흐름 점
        Dot(ci, core + new Vector2(mimic.Size.Y * 0.28f + (mimic.Size.X * 0.45f - mimic.Size.Y * 0.28f) * ph, 0f), 0.9f, ColdPipe.WithAlpha(x.Glow));
    }

    private static void SimulatorFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.55f);
        for (int k = 0; k < 4; k++) Tag(ci, x.P(0.17f + k * 0.2f, 0.66f), (k + 1).ToString(), 4, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 열화상 카메라 ───────────────

    private static void ThermalBody(in Fix x)
    {
        var ci = x.Ci;
        var c = x.C;
        for (int k = 0; k < 3; k++) Line(ci, c, c + Vector2.FromAngle(Mathf.Pi / 2f + k * Mathf.Tau / 3f) * x.B.Size.X * 0.5f, new Color("#2a2e36"), 1.6f); // 삼각대
        var body = new Rect2(c - new Vector2(6f, 4.5f), new Vector2(12f, 9f));
        Box(ci, body, new Color("#3a3f48"), 2, new Color("#6a7080"));
        var lens = c + new Vector2(7f, 0f);
        Dot(ci, lens, 4f, new Color("#1a1d22"));
        Ring(ci, lens, 4f, new Color("#8a7a5a"), 1.2f, 16); // 게르마늄 렌즈 테
        Dot(ci, lens, 2.4f, new Color("#3a2a4a"));
        Box(ci, new Rect2(c - new Vector2(9f, 3.5f), new Vector2(3.5f, 7f)), GlassDark, 1); // 뒤 화면
        Line(ci, c + new Vector2(-4f, -4.5f), c + new Vector2(4f, -4.5f), new Color("#20242a"), 2f); // 손잡이
    }

    private static void ThermalLife(in Fix x)
    {
        var ci = x.Ci;
        var c = x.C;
        var lens = c + new Vector2(7f, 0f);
        // 방 설비 중 가장 뜨거운 것 (미리보기 화면 색)
        float heat = 0f;
        foreach (var o in x.F.Room.Furniture) if (o.Machine is Machine mm && mm.Heat > heat) heat = mm.Heat;
        var hc = heat < 0.4f ? new Color("#3a3aa8") : heat < 0.8f ? new Color("#e0a03a") : Colors.White;
        if (x.Lit)
        {
            float a = Mathf.Sin(x.Ang(0.4f)) * 0.9f; // 천천히 좌우로 훑는다
            var d = Vector2.FromAngle(a);
            var n = new Vector2(-d.Y, d.X);
            ci.DrawColoredPolygon(new[] { lens, lens + d * 40f + n * 14f, lens + d * 40f - n * 14f }, hc.WithAlpha(0.06f * x.Glow));
            var scr = new Rect2(c - new Vector2(8.5f, 3f), new Vector2(2.5f, 6f));
            ci.DrawRect(scr, new Color("#2a1a5a").WithAlpha(x.Glow));
            ci.DrawRect(new Rect2(scr.Position + new Vector2(0f, scr.Size.Y * (0.3f + 0.2f * Mathf.Sin(x.T))), new Vector2(2.5f, 2f)), hc.WithAlpha(0.9f * x.Glow));
            Led(ci, c + new Vector2(4f, -3f), Danger, Mathf.PosMod(x.T, 1.2f) < 0.6f ? 0.9f : 0.2f, 0.8f); // 녹화
        }
    }

    private static void ThermalFine(in Fix x)
    {
        var ci = x.Ci;
        Bolt(ci, x.C, 0.7f);
        Ring(ci, x.C + new Vector2(7f, 0f), 2.9f, new Color(1, 1, 1, 0.15f), 0.5f, 12);
        Tag(ci, x.C + new Vector2(0f, 7.5f), "IR", 5, new Color(1, 1, 1, 0.4f));
    }

    // ─────────────── 진동 감시기 ───────────────

    private static void VibrationBody(in Fix x)
    {
        var ci = x.Ci;
        Box(ci, x.B, new Color("#1e2228"), 3, new Color("#4f7fa8"));
        var drum = x.Q(0.18f, 0.12f, 0.78f, 0.88f);
        Box(ci, drum, new Color("#e8e2d4"), 2, new Color("#8a8070")); // 기록지 드럼
        for (int k = 1; k < 5; k++) Line(ci, new Vector2(drum.Position.X + 1f, drum.Position.Y + drum.Size.Y * k / 5f), new Vector2(drum.End.X - 1f, drum.Position.Y + drum.Size.Y * k / 5f), new Color("#c8a0a0").WithAlpha(0.6f), 0.6f);
        var puck = x.P(0.9f, 0.8f);
        Can(ci, puck, 2.6f, new Color("#3a4454"), Chrome); // 센서 원판
        Cable(ci, puck, x.P(0.86f, 0.2f), 2f, new Color("#20242a"), 1f);
        Line(ci, x.P(0.86f, 0.2f), x.P(0.55f, 0.5f), Chrome, 1f); // 펜 팔
    }

    private static void VibrationLife(in Fix x)
    {
        var ci = x.Ci;
        var drum = x.Q(0.18f, 0.12f, 0.78f, 0.88f);
        float wear = 0f;
        foreach (var o in x.F.Room.Furniture) if (o.Machine is Machine mm && mm.Wear > wear) wear = mm.Wear;
        float amp = Mathf.Clamp(x.F.Room.Vibration * 0.8f + wear * 0.5f, 0.05f, 1f); // 방 진동 + 가장 닳은 설비
        var col = amp < 0.4f ? new Color("#2a6a3a") : amp < 0.7f ? new Color("#a8742a") : new Color("#c0392b");
        bool vert = drum.Size.Y >= drum.Size.X;
        float len = vert ? drum.Size.Y : drum.Size.X, wid = vert ? drum.Size.X : drum.Size.Y;
        var pts = new Vector2[12];
        float scroll = x.T * 2f * Mathf.Max(0.1f, x.Spin);
        for (int i = 0; i < 12; i++)
        {
            float s = i / 11f;
            float w = Mathf.Sin((s * 9f + scroll) * 2.3f) * 0.6f + Mathf.Sin((s * 23f + scroll * 1.7f)) * 0.4f;
            float off = w * amp * wid * 0.4f * (x.On ? 1f : 0.1f);
            pts[i] = vert ? new Vector2(drum.GetCenter().X + off, drum.Position.Y + s * len) : new Vector2(drum.Position.X + s * len, drum.GetCenter().Y + off);
        }
        ci.DrawPolyline(pts, col, 0.9f, true);
        var tip = pts[6];
        Line(ci, x.P(0.86f, 0.2f), tip, Chrome, 1f);
        Led(ci, x.P(0.08f, 0.15f), amp < 0.7f ? Good : Danger, Mathf.Max(0.2f, x.Glow) * (amp >= 0.7f ? Pulse(x.T, 6f) : 1f), 1f);
    }

    private static void VibrationFine(in Fix x)
    {
        var ci = x.Ci;
        Bolts(ci, x.B, 1.8f, 0.55f);
        Tag(ci, x.P(0.08f, 0.6f), "Hz", 4, new Color(1, 1, 1, 0.4f));
    }
}
