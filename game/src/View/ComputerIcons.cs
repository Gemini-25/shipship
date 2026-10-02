using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.6 컴퓨터 모듈 아이콘 30 — 모듈마다 실루엣이 다르다 (불꽃 · 구역 고리 · 심장 박동 · 책 · 눈 · 렌치 · 물방울 · 필름 …).
/// 상태마다 움직임이 다르다: 켜짐(제 색 · 작은 움직임) · 잠시 끔(회색 · 점선 테 · 일시정지 막대) · 업데이트 버그(붉게 지지직 어긋남) · 없음(옅은 윤곽).
/// </summary>
public static class ComputerIcons
{
    public enum State { On, Suspended, Bug, Off }

    /// <summary>모듈 갈래 색: 재난(주황) · 사람(분홍) · 설비(청록) · 자원(파랑) · 생활(연보라) · 기록(금색).</summary>
    public static Color Tint(ComputerModule m) => m switch
    {
        ComputerModule.FireResponse or ComputerModule.AirZones or ComputerModule.EvacGuide or ComputerModule.Preempt or ComputerModule.DoorPressure or ComputerModule.RouteForecast => new Color("#ff9a6b"),
        ComputerModule.BioMonitor or ComputerModule.FatigueAlert or ComputerModule.Assistant or ComputerModule.Training => new Color("#ff8fb1"),
        ComputerModule.Foresight or ComputerModule.MaintPlan or ComputerModule.AutoCalib or ComputerModule.PipeWatch or ComputerModule.CommsRelay or ComputerModule.Access => new Color("#5fd0c8"),
        ComputerModule.WaterPlan or ComputerModule.PowerShare or ComputerModule.ResourceAlloc or ComputerModule.CargoSort or ComputerModule.Balance or ComputerModule.SoilWatch => new Color("#7cc4ff"),
        ComputerModule.MealPlan or ComputerModule.LightCycle or ComputerModule.QuietNight or ComputerModule.MediaVault or ComputerModule.Roster => new Color("#c9a0ff"),
        _ => new Color("#f2c66d"),
    };

    /// <summary>아이콘 하나 (가운데 c · 반지름 s).</summary>
    public static void Draw(CanvasItem ci, ComputerModule m, Vector2 c, float s, State st, float t)
    {
        var col = Tint(m);
        if (st == State.Bug) { c += new Vector2(Mathf.Sin(t * 47f) > 0.6f ? 1.5f : 0f, Mathf.Sin(t * 31f) > 0.7f ? -1f : 0f); col = Mathf.Sin(t * 23f) > 0f ? Palette.Danger : col; }
        if (st == State.Suspended) col = new Color("#6b7280");
        if (st == State.Off) col = col.WithAlpha(0.25f);
        // 바탕: 둥근 판 (켜짐은 숨쉬는 테)
        ci.DrawCircle(c, s * 1.25f, new Color("#0b1119"), true, -1f, true);
        if (st == State.Suspended)
            for (int k = 0; k < 8; k++) ci.DrawArc(c, s * 1.25f, k * Mathf.Tau / 8f, k * Mathf.Tau / 8f + 0.45f, 4, col.WithAlpha(0.7f), 1f, true);
        else ci.DrawArc(c, s * 1.25f, 0f, Mathf.Tau, 24, col.WithAlpha(st == State.On ? 0.45f + 0.2f * Mathf.Sin(t * 2f + (int)m) : 0.3f), 1f, true);
        float w = 1.3f;
        var a = col.WithAlpha(st == State.Off ? 0.3f : 0.95f);
        Vector2 P(float x, float y) => c + new Vector2(x * s, y * s);
        void L(float x0, float y0, float x1, float y1) => ci.DrawLine(P(x0, y0), P(x1, y1), a, w, true);
        void Poly(params float[] xy) { var pts = new Vector2[xy.Length / 2]; for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[i * 2], xy[i * 2 + 1]); ci.DrawColoredPolygon(pts, a.WithAlpha(a.A * 0.85f)); }
        void Ring(float x, float y, float r, bool fill = false) { if (fill) ci.DrawCircle(P(x, y), r * s, a, true, -1f, true); else ci.DrawArc(P(x, y), r * s, 0f, Mathf.Tau, 16, a, w, true); }
        float anim = st == State.On ? t : 0f;
        switch (m)
        {
            case ComputerModule.FireResponse:
                Poly(0, -0.8f, 0.5f, 0.1f, 0.35f, 0.6f, -0.35f, 0.6f, -0.5f, 0.1f);
                ci.DrawColoredPolygon(new[] { P(0, -0.2f + 0.08f * Mathf.Sin(anim * 9f)), P(0.22f, 0.3f), P(0, 0.55f), P(-0.22f, 0.3f) }, new Color("#ffe08a").WithAlpha(a.A));
                break;
            case ComputerModule.AirZones:
                for (int k = 1; k <= 3; k++) ci.DrawArc(c, s * 0.28f * k, 0f, Mathf.Tau, 18, a.WithAlpha(a.A * (1.1f - 0.25f * k)), w, true);
                Ring(0, 0, 0.12f, true);
                break;
            case ComputerModule.BioMonitor:
                Poly(0, 0.7f, -0.7f, -0.05f, -0.55f, -0.5f, -0.2f, -0.55f, 0, -0.3f, 0.2f, -0.55f, 0.55f, -0.5f, 0.7f, -0.05f);
                ci.DrawPolyline(new[] { P(-0.8f, 0), P(-0.3f, 0), P(-0.15f, -0.35f - 0.1f * Mathf.Sin(anim * 6f)), P(0.05f, 0.3f), P(0.2f, 0), P(0.8f, 0) }, new Color("#0b1119"), 1.4f, true);
                break;
            case ComputerModule.ResourceAlloc:
                foreach (var (x, y) in new[] { (-0.42f, 0.3f), (0.42f, 0.3f), (0f, -0.35f) }) ci.DrawRect(new Rect2(P(x - 0.3f, y - 0.28f), new Vector2(0.6f, 0.56f) * s), a, false, w);
                break;
            case ComputerModule.EvacGuide:
                ci.DrawRect(new Rect2(P(0.25f, -0.7f), new Vector2(0.5f, 1.4f) * s), a, false, w);
                float ex = -0.2f + 0.15f * Mathf.Sin(anim * 4f);
                L(-0.8f, 0, ex + 0.2f, 0); L(ex + 0.2f, 0, ex - 0.1f, -0.3f); L(ex + 0.2f, 0, ex - 0.1f, 0.3f);
                break;
            case ComputerModule.Preempt:
                Ring(0, 0, 0.75f);
                Poly(0.15f, -0.65f, -0.3f, 0.1f, 0, 0.1f, -0.15f, 0.65f, 0.3f, -0.1f, 0, -0.1f);
                break;
            case ComputerModule.Lessons:
                Poly(0, -0.4f, -0.75f, -0.55f, -0.75f, 0.45f, 0, 0.6f);
                ci.DrawColoredPolygon(new[] { P(0, -0.4f), P(0.75f, -0.55f), P(0.75f, 0.45f), P(0, 0.6f) }, a.WithAlpha(a.A * 0.55f));
                L(0, -0.4f, 0, 0.6f);
                break;
            case ComputerModule.Foresight:
                ci.DrawPolyline(new[] { P(-0.8f, 0), P(-0.4f, -0.4f), P(0.4f, -0.4f), P(0.8f, 0), P(0.4f, 0.4f), P(-0.4f, 0.4f), P(-0.8f, 0) }, a, w, true);
                Ring(0, 0, 0.22f, true);
                ci.DrawCircle(P(0.1f * Mathf.Sin(anim * 1.5f), -0.05f), s * 0.08f, new Color("#0b1119"), true, -1f, true);
                break;
            case ComputerModule.MaintPlan:
                L(-0.55f, 0.55f, 0.25f, -0.25f);
                ci.DrawArc(P(0.38f, -0.38f), s * 0.3f, -2.2f, 2.2f + Mathf.Pi * 0.6f, 12, a, w * 1.4f, true);
                for (int k = 0; k < 3; k++) ci.DrawRect(new Rect2(P(-0.75f + k * 0.28f, -0.75f), new Vector2(0.18f, 0.18f) * s), a.WithAlpha(a.A * 0.6f));
                break;
            case ComputerModule.WaterPlan:
                Poly(0, -0.8f, 0.45f, 0.05f, 0.45f, 0.35f, 0, 0.7f, -0.45f, 0.35f, -0.45f, 0.05f);
                L(-0.6f, 0.15f + 0.08f * Mathf.Sin(anim * 3f), 0.6f, 0.15f);
                break;
            case ComputerModule.PowerShare:
                Poly(0.1f, -0.8f, -0.35f, 0, -0.05f, 0, -0.2f, 0.75f, 0.35f, -0.1f, 0.05f, -0.1f);
                L(0.25f, 0.3f, 0.7f, 0.55f); L(0.25f, 0.3f, 0.7f, 0.05f);
                break;
            case ComputerModule.CargoSort:
                ci.DrawRect(new Rect2(P(-0.65f, -0.2f), new Vector2(1.3f, 0.9f) * s), a, false, w);
                for (int k = 0; k < 3; k++) { float ang = k * Mathf.Pi / 3f + anim * 0.5f; L(Mathf.Cos(ang) * 0.35f, -0.45f + Mathf.Sin(ang) * 0.35f, -Mathf.Cos(ang) * 0.35f, -0.45f - Mathf.Sin(ang) * 0.35f); }
                break;
            case ComputerModule.RouteForecast:
                ci.DrawArc(P(0.4f, 0.4f), s * 1.0f, Mathf.Pi, Mathf.Pi * 1.5f, 10, a.WithAlpha(a.A * 0.5f), w, true);
                Ring(-0.45f + 0.05f * Mathf.Sin(anim * 3f), -0.45f, 0.22f, true);
                L(-0.3f, -0.3f, 0.5f, 0.5f);
                break;
            case ComputerModule.FatigueAlert:
                ci.DrawCircle(P(-0.15f, 0.05f), s * 0.6f, a, true, -1f, true);
                ci.DrawCircle(P(0.12f, -0.1f), s * 0.5f, new Color("#0b1119"), true, -1f, true);
                Gfx.TextCentered(ci, Fonts.Bold, P(0.55f, -0.5f - 0.1f * Mathf.Sin(anim * 2f)), "z", Mathf.Max(7, (int)(s * 1.1f)), a);
                break;
            case ComputerModule.AutoCalib:
                ci.DrawArc(c, s * 0.75f, Mathf.Pi, Mathf.Tau, 14, a, w, true);
                float needle = Mathf.Pi * 1.5f + 0.6f * Mathf.Sin(anim * 1.3f);
                ci.DrawLine(c, c + Vector2.FromAngle(needle) * s * 0.7f, a, w, true);
                for (int k = 0; k <= 4; k++) { var d = Vector2.FromAngle(Mathf.Pi + k * Mathf.Pi / 4f); ci.DrawLine(c + d * s * 0.6f, c + d * s * 0.78f, a, 1f, true); }
                break;
            case ComputerModule.CommsRelay:
                L(0, -0.2f, 0, 0.75f); L(-0.35f, 0.75f, 0.35f, 0.75f);
                for (int k = 1; k <= 2; k++) { float r = 0.25f * k + 0.08f * Mathf.PosMod(anim * 2f, 1f); ci.DrawArc(P(0, -0.2f), r * s, -Mathf.Pi * 0.85f, -Mathf.Pi * 0.15f, 8, a, w, true); }
                break;
            case ComputerModule.SoilWatch:
                Ring(-0.35f, 0.2f, 0.25f); Ring(0.3f, -0.3f, 0.2f); Ring(0.35f, 0.4f, 0.14f, true);
                L(-0.75f, -0.6f, -0.45f, -0.3f);
                break;
            case ComputerModule.PipeWatch:
                L(-0.8f, 0.3f, 0.1f, 0.3f); L(0.1f, 0.3f, 0.1f, -0.8f);
                Ring(0.45f, 0.45f, 0.3f);
                ci.DrawLine(P(0.45f, 0.45f), P(0.45f, 0.45f) + Vector2.FromAngle(-0.8f + 0.3f * Mathf.Sin(anim * 2f)) * s * 0.25f, a, 1f, true);
                break;
            case ComputerModule.DoorPressure:
                ci.DrawRect(new Rect2(P(-0.2f, -0.75f), new Vector2(0.4f, 1.5f) * s), a, false, w);
                float dx = 0.08f * Mathf.Sin(anim * 3f);
                L(-0.85f + dx, 0, -0.35f + dx, 0); L(-0.5f + dx, -0.15f, -0.35f + dx, 0); L(-0.5f + dx, 0.15f, -0.35f + dx, 0);
                L(0.85f - dx, 0, 0.35f - dx, 0); L(0.5f - dx, -0.15f, 0.35f - dx, 0); L(0.5f - dx, 0.15f, 0.35f - dx, 0);
                break;
            case ComputerModule.Archive:
                for (int k = 0; k < 3; k++) { float y = 0.45f - k * 0.4f; ci.DrawArc(P(0, y), s * 0.6f, 0f, Mathf.Pi, 10, a, w, true); ci.DrawLine(P(-0.6f, y), P(-0.6f, y - 0.3f), a, w); ci.DrawLine(P(0.6f, y), P(0.6f, y - 0.3f), a, w); }
                ci.DrawArc(P(0, -0.75f), s * 0.6f, 0f, Mathf.Tau, 14, a, w, true);
                break;
            case ComputerModule.Roster:
                ci.DrawRect(new Rect2(P(-0.55f, -0.7f), new Vector2(1.1f, 1.45f) * s), a, false, w);
                ci.DrawRect(new Rect2(P(-0.2f, -0.85f), new Vector2(0.4f, 0.25f) * s), a);
                for (int k = 0; k < 3; k++) { L(-0.35f, -0.25f + k * 0.35f, 0.35f, -0.25f + k * 0.35f); }
                break;
            case ComputerModule.Assistant:
                ci.DrawRect(new Rect2(P(-0.75f, -0.6f), new Vector2(1.5f, 1.0f) * s), a, false, w);
                L(-0.35f, 0.4f, -0.55f, 0.75f);
                for (int k = 0; k < 3; k++) ci.DrawCircle(P(-0.35f + k * 0.35f, -0.1f), s * (0.09f + (st == State.On && (int)(anim * 3f) % 3 == k ? 0.05f : 0f)), a, true, -1f, true);
                break;
            case ComputerModule.MealPlan:
                ci.DrawArc(P(0, 0.05f), s * 0.7f, 0f, Mathf.Pi, 12, a, w * 1.3f, true);
                L(-0.75f, 0.05f, 0.75f, 0.05f);
                for (int k = -1; k <= 1; k++) ci.DrawArc(P(k * 0.28f, -0.35f - 0.06f * Mathf.Sin(anim * 3f + k)), s * 0.12f, Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 6, a.WithAlpha(a.A * 0.7f), 1f, true);
                break;
            case ComputerModule.LightCycle:
                ci.DrawCircle(P(-0.15f, 0), s * 0.38f, a, true, -1f, true);
                for (int k = 0; k < 6; k++) { var d = Vector2.FromAngle(k * Mathf.Tau / 6f + anim * 0.4f); ci.DrawLine(P(-0.15f, 0) + d * s * 0.5f, P(-0.15f, 0) + d * s * 0.72f, a, 1f, true); }
                ci.DrawCircle(P(0.45f, -0.45f), s * 0.25f, new Color("#9fb3ff").WithAlpha(a.A), true, -1f, true);
                ci.DrawCircle(P(0.55f, -0.52f), s * 0.2f, new Color("#0b1119"), true, -1f, true);
                break;
            case ComputerModule.QuietNight:
                Poly(-0.75f, -0.25f, -0.4f, -0.25f, 0, -0.6f, 0, 0.6f, -0.4f, 0.25f, -0.75f, 0.25f);
                L(0.25f, -0.35f, 0.7f, 0.35f); L(0.7f, -0.35f, 0.25f, 0.35f);
                break;
            case ComputerModule.Access:
                Ring(-0.4f, 0, 0.3f);
                L(-0.1f, 0, 0.75f, 0); L(0.5f, 0, 0.5f, 0.3f); L(0.7f, 0, 0.7f, 0.25f);
                break;
            case ComputerModule.Balance:
                float tilt = 0.15f * Mathf.Sin(anim * 1.2f);
                Poly(0, 0.1f, -0.25f, 0.7f, 0.25f, 0.7f);
                L(-0.8f, 0.1f + tilt, 0.8f, 0.1f - tilt);
                ci.DrawRect(new Rect2(P(-0.75f, -0.25f + tilt), new Vector2(0.3f, 0.3f) * s), a);
                break;
            case ComputerModule.MediaVault:
                ci.DrawRect(new Rect2(P(-0.8f, -0.55f), new Vector2(1.6f, 1.1f) * s), a, false, w);
                for (int k = 0; k < 4; k++) { ci.DrawRect(new Rect2(P(-0.7f + k * 0.42f, -0.5f), new Vector2(0.15f, 0.12f) * s), a); ci.DrawRect(new Rect2(P(-0.7f + k * 0.42f, 0.36f), new Vector2(0.15f, 0.12f) * s), a); }
                if (st != State.Suspended) Poly(-0.18f, -0.25f, 0.3f, 0, -0.18f, 0.25f);
                break;
            case ComputerModule.Training:
                Poly(0, -0.6f, 0.85f, -0.2f, 0, 0.2f, -0.85f, -0.2f);
                ci.DrawRect(new Rect2(P(-0.45f, 0), new Vector2(0.9f, 0.45f) * s), a.WithAlpha(a.A * 0.6f));
                L(0.7f, -0.2f, 0.7f + 0.05f * Mathf.Sin(anim * 3f), 0.55f);
                break;
            case ComputerModule.AutoLog:
                ci.DrawRect(new Rect2(P(-0.6f, -0.7f), new Vector2(1.0f, 1.4f) * s), a, false, w);
                for (int k = 0; k < 3; k++) L(-0.45f, -0.4f + k * 0.3f, 0.2f, -0.4f + k * 0.3f);
                L(0.75f, -0.75f, 0.2f + 0.05f * Mathf.Sin(anim * 5f), 0.45f);
                break;
        }
        if (st == State.Suspended)
        {
            ci.DrawRect(new Rect2(c + new Vector2(s * 0.55f, s * 0.5f), new Vector2(s * 0.18f, s * 0.6f)), new Color("#e6eaf2"));
            ci.DrawRect(new Rect2(c + new Vector2(s * 0.85f, s * 0.5f), new Vector2(s * 0.18f, s * 0.6f)), new Color("#e6eaf2"));
        }
        if (st == State.Bug && Mathf.Sin(t * 19f) > 0.3f)
            ci.DrawLine(c + new Vector2(-s * 1.2f, s * 0.2f), c + new Vector2(s * 1.2f, s * 0.2f), Palette.Danger.WithAlpha(0.8f), 1f);
    }

    /// <summary>조치 종류 그림 (가운데 c · 배율 s=1이면 반지름 5px): 댐퍼(날개 원) · 격벽(가운데 줄 문) · 밸브(나비) · 차단기(손잡이) · 소화(불꽃) · 경보(종) ·
    /// 구역(겹 고리) · 제안(물음표 카드) · 방송(스피커) · 재부팅(돌아가는 화살) · 모듈 · 부하 줄임(끊긴 선).</summary>
    public static void Act(CanvasItem ci, ActKind k, Vector2 at, float s, Color col, float t)
    {
        ci.DrawSetTransform(at, 0f, new Vector2(s, s));
        var c = Vector2.Zero;

        switch (k)
        {
            case ActKind.Damper: ci.DrawArc(c, 5f, 0f, Mathf.Tau, 12, col, 1.2f, true); ci.DrawLine(c + new Vector2(-4, -2), c + new Vector2(4, 2), col, 1.4f); break;
            case ActKind.Bulkhead: ci.DrawRect(new Rect2(c - new Vector2(5, 5), new Vector2(10, 10)), col, false, 1.4f); ci.DrawLine(c + new Vector2(0, -5), c + new Vector2(0, 5), col, 1.4f); break;
            case ActKind.Valve: ci.DrawColoredPolygon(new[] { c + new Vector2(-5, -4), c + new Vector2(0, 0), c + new Vector2(-5, 4) }, col); ci.DrawColoredPolygon(new[] { c + new Vector2(5, -4), c + new Vector2(0, 0), c + new Vector2(5, 4) }, col); break;
            case ActKind.Breaker: ci.DrawRect(new Rect2(c - new Vector2(4, 5), new Vector2(8, 10)), col, false, 1.2f); ci.DrawLine(c + new Vector2(-2, 1), c + new Vector2(2, -3), col, 1.6f); break;
            case ActKind.Suppress: ci.DrawColoredPolygon(new[] { c + new Vector2(0, -6), c + new Vector2(4, 1), c + new Vector2(2, 5), c + new Vector2(-2, 5), c + new Vector2(-4, 1) }, col); break;
            case ActKind.Alarm: ci.DrawArc(c + new Vector2(0, 1), 4.5f, Mathf.Pi, Mathf.Tau, 8, col, 1.6f, true); ci.DrawLine(c + new Vector2(-5, 2), c + new Vector2(5, 2), col, 1.4f); ci.DrawCircle(c + new Vector2(0, 4), 1.2f, col); break;
            case ActKind.Zone: for (int r = 2; r <= 6; r += 2) ci.DrawArc(c, r, 0f, Mathf.Tau, 12, col.WithAlpha(1.1f - r * 0.12f), 1f, true); break;
            case ActKind.Proposal: ci.DrawRect(new Rect2(c - new Vector2(5, 4), new Vector2(10, 8)), col, false, 1.2f); Gfx.TextCentered(ci, Fonts.Bold, c, "?", 8, col); break;
            case ActKind.Broadcast: ci.DrawColoredPolygon(new[] { c + new Vector2(-5, -2), c + new Vector2(-2, -2), c + new Vector2(1, -5), c + new Vector2(1, 5), c + new Vector2(-2, 2), c + new Vector2(-5, 2) }, col); ci.DrawArc(c + new Vector2(2, 0), 4f, -0.8f, 0.8f, 6, col, 1f, true); break;
            case ActKind.Reboot: ci.DrawArc(c, 5f, 0.6f, Mathf.Tau - 0.2f, 12, col, 1.4f, true); ci.DrawLine(c + new Vector2(5, -2), c + new Vector2(5, 2), col, 1.4f); break;
            case ActKind.Module: Draw(ci, ComputerModule.Foresight, c, 4.5f, ComputerIcons.State.On, t); break;
            case ActKind.Shed: ci.DrawLine(c + new Vector2(-5, 0), c + new Vector2(-1, 0), col, 1.4f); ci.DrawLine(c + new Vector2(1, 0), c + new Vector2(5, 0), col, 1.4f); ci.DrawLine(c + new Vector2(-1, -3), c + new Vector2(1, 3), col, 1.2f); break;
            case ActKind.Door: // 문틀 + 열쇠 구멍 (원격 열기 · 막음)
                ci.DrawRect(new Rect2(c - new Vector2(4, 5.5f), new Vector2(8, 11)), col, false, 1.3f);
                ci.DrawCircle(c + new Vector2(1.5f, -0.5f), 1.3f, col, true, -1f, true);
                ci.DrawLine(c + new Vector2(1.5f, 0.3f), c + new Vector2(1.5f, 2.8f), col, 1.2f);
                ci.DrawLine(c + new Vector2(-4, 5.5f), c + new Vector2(-6, 3.5f), col.WithAlpha(0.7f), 1f);
                break;
            case ActKind.Forecast: // 레이더 부채꼴이 돈다 (예보 · 앞날 예측)
                ci.DrawArc(c, 5f, 0f, Mathf.Tau, 14, col.WithAlpha(0.6f), 1f, true);
                ci.DrawArc(c, 2.6f, 0f, Mathf.Tau, 10, col.WithAlpha(0.4f), 0.8f, true);
                ci.DrawColoredPolygon(new[] { c, c + new Vector2(Mathf.Cos(t * 3f), Mathf.Sin(t * 3f)) * 5f, c + new Vector2(Mathf.Cos(t * 3f + 0.7f), Mathf.Sin(t * 3f + 0.7f)) * 5f }, col.WithAlpha(0.8f));
                ci.DrawCircle(c + new Vector2(2.5f, -2.5f), 1f, col, true, -1f, true);
                break;
            case ActKind.Plan: // v16.26 수순: 마디 셋을 잇는 계단 길 · 앞으로 가는 빛
                ci.DrawPolyline(new[] { c + new Vector2(-5, 4), c + new Vector2(-2, 4), c + new Vector2(-2, 0), c + new Vector2(2, 0), c + new Vector2(2, -4), c + new Vector2(5, -4) }, col, 1.3f, true);
                for (int i = 0; i < 3; i++) ci.DrawCircle(c + new Vector2(-3.5f + i * 4f, 4f - i * 4f), 1.3f, col.WithAlpha(Mathf.PosMod(t * 1.2f, 3f) >= i ? 1f : 0.35f), true, -1f, true);
                break;
            case ActKind.Check: // v16.26 확인: 돋보기 + 안의 물음표
                ci.DrawArc(c + new Vector2(-1, -1), 3.8f, 0f, Mathf.Tau, 12, col, 1.3f, true);
                ci.DrawLine(c + new Vector2(1.8f, 1.8f), c + new Vector2(5, 5), col, 1.8f, true);
                Gfx.TextCentered(ci, Fonts.Bold, c + new Vector2(-1, -0.5f), "?", 6, col);
                break;
            default: ci.DrawCircle(c, 3.5f, col.WithAlpha(0.8f), true, -1f, true); break;
        }
        ci.DrawSetTransformMatrix(Transform2D.Identity);
    }

    public static State StateOf(World w, ComputerModule m)
    {
        var a = w.Automation;
        if (!a.Has(m)) return State.Off;
        if (a.BugModule == m && a.BugUntil > w.Tick) return State.Bug;
        if (a.Suspended.Contains(m) || !a.MainOnline) return State.Suspended;
        return State.On;
    }
}
