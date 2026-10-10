using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.19 계통 · 배 전체 사고 24의 그림 — 사고마다 실루엣이 다르다 (규모 색 테두리는 v16.18 계열 그대로: 계통 주황 · 배 전체 빨강).
///   전조: 설비 위의 작은 기척(종류별 문양이 희미하게 맥박) · 발생: 사고 자리의 큰 문양이 움직이고 칸마다 효과(균열이 달리고 · 간선이 타고 · 휜 문 사이로
///   공기가 빨려 가고 · 셀이 차례로 달아오르고) · 수습 직후: 회색으로 가라앉는다 · 흔적: 오래 남는 작은 자국(얼룩 · 용접선 · 그을음 · 경고 딱지).
/// 그리기는 시뮬레이션 상태를 읽기만 한다.
/// </summary>
public partial class ShipView
{
    private static readonly Color MajSystem = new("#ff922b");
    private static readonly Color MajShip = new("#ff4d5e");
    private static readonly Color MajSteam = new("#eef4fa");
    private static readonly Color Coolant = new("#5cc8ff");
    private static readonly Color Flame = new("#ff8a1f");
    private static readonly Color FlameCore = new("#ffe066");
    private static readonly Color Soot = new("#1a1614");
    private static readonly Color Steel = new("#8b97a8");
    private static readonly Color DarkSteel = new("#3a4252");

    private void PaintMajors(CanvasItem ci)
    {
        var w = _world;
        var sys = w.Major;
        // 흔적 (72시간): 작은 자국
        foreach (var t in sys.Traces)
        {
            float age = (w.Tick - t.Tick) / (float)SimTime.TicksPerHour;
            if (age > 72f) continue;
            float a = 0.55f * (1f - age / 72f) + 0.12f;
            PaintMajorTrace(ci, t.Kind, CellRect(t.At).GetCenter(), a);
        }
        foreach (var k in sys.Cases)
        {
            if (k.Phase == MajorPhase.Averted) continue;
            var scaleCol = k.Spec.Scale >= IncidentScale.Ship ? MajShip : MajSystem;
            if (k.Phase == MajorPhase.Omen)
            {
                // 기척은 누가 알아챈 뒤에만 보인다 (세계 ≠ 아는 것)
                var f = w.Ship.Furniture.FirstOrDefault(x => x.Id == k.Machine);
                if (f?.Machine?.Omen is not Omen o || !o.Known) continue;
                var fr = FurnitureRect(f);
                var p = new Vector2(fr.End.X - 8f, fr.Position.Y + 8f);
                float pulse = 0.35f + 0.25f * Mathf.Sin(_time * 3f + k.Id);
                ci.Arc(p, 7f + 2f * Mathf.Sin(_time * 3f), 0f, Mathf.Tau, 18, scaleCol.WithAlpha(pulse), 1.2f, true);
                MajorGlyph(ci, k.Kind, p, 6f, 0.4f, pulse + 0.2f, false);
                continue;
            }
            bool live = k.Phase == MajorPhase.Active;
            if (!live && w.Tick - k.End > SimTime.Hours(1)) continue;
            var at = MajorAnchor(k);
            float ph = Mathf.PosMod(_time * 0.8f + k.Id * 0.21f, 1f);
            PaintMajorCells(ci, k, live);
            // 규모 색 고리 + 사고 문양
            float ring = live ? 0.55f + 0.35f * Mathf.Sin(_time * 4f) : 0.25f;
            ci.Circle(at, T * 0.62f, new Color("#14161c").WithAlpha(live ? 0.72f : 0.45f), true, -1f, true);
            ci.Arc(at, T * 0.62f, 0f, Mathf.Tau, 28, (live ? scaleCol : new Color(0.6f, 0.62f, 0.66f)).WithAlpha(ring), 2f, true);
            if (live && k.Spec.Scale >= IncidentScale.Ship) ci.Arc(at, T * (0.62f + 0.35f * ph), 0f, Mathf.Tau, 28, scaleCol.WithAlpha(0.5f * (1f - ph)), 1.5f, true);
            MajorGlyph(ci, k.Kind, at, T * 0.48f, ph, live ? 1f : 0.45f, !live);
        }
    }

    private Vector2 MajorAnchor(MajorCase k)
    {
        var w = _world;
        if (k.Furn.Count > 0 && w.Ship.Furniture.FirstOrDefault(f => f.Id == k.Furn[0]) is Furniture f) return FurnitureRect(f).GetCenter() + new Vector2(0, -T * 0.6f);
        if (k.Cells.Count > 0) return CellRect(k.Cells[0]).GetCenter() + new Vector2(0, -T * 0.4f);
        if (w.Ship.Rooms.FirstOrDefault(r => r.Id == k.Room) is Room room) return ToPx(room.Center);
        return Vector2.Zero;
    }

    /// <summary>사고마다 칸 위의 효과 (번진 자리).</summary>
    private void PaintMajorCells(CanvasItem ci, MajorCase k, bool live)
    {
        var w = _world;
        float a = live ? 1f : 0.4f;
        switch (k.Kind)
        {
            case MajorKind.HullCrackRun:
            {
                // 균열이 외판을 따라 달린다: 지그재그 금 + 끝의 불꽃
                Vector2? prev = null;
                for (int i = 0; i < k.Cells.Count; i++)
                {
                    var c = CellRect(k.Cells[i]).GetCenter();
                    if (prev is Vector2 p0)
                    {
                        var mid = (p0 + c) * 0.5f + new Vector2(Hash(i, k.Id, 3) - 0.5f, Hash(i, k.Id, 4) - 0.5f) * 8f;
                        ci.Polyline(new[] { p0, mid, c }, Soot.WithAlpha(0.9f * a), 2.4f, true);
                        ci.Polyline(new[] { p0, mid, c }, new Color("#c9d4e4").WithAlpha(0.5f * a), 0.8f, true);
                    }
                    prev = c;
                }
                if (live && prev is Vector2 tip && Mathf.PosMod(_time * 2f, 1f) < 0.5f)
                    for (int s = 0; s < 4; s++) { float ang = s * 1.6f + _time * 5f; ci.DrawLine(tip, tip + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 5f, FlameCore, 1f, true); }
                break;
            }
            case MajorKind.TrunkFire:
                foreach (var l in w.Net.Links.Where(l => k.Links.Contains(l.Id)))
                    for (int i = 0; i + 1 < l.Cells.Count; i++)
                    {
                        var p0 = CellRect(l.Cells[i]).GetCenter(); var p1 = CellRect(l.Cells[i + 1]).GetCenter();
                        ci.DrawLine(p0, p1, Soot.WithAlpha(0.7f * a), 4f, true);
                        if (live && (i + (int)(_time * 4f)) % 3 == 0) ci.Circle((p0 + p1) * 0.5f, 2.5f, Flame.WithAlpha(0.8f), true, -1f, true);
                    }
                break;
            case MajorKind.CascadeDecomp:
                foreach (var d in w.Ship.Doors.Where(d => k.Doors.Contains(d.Id)))
                {
                    var c = CellRect(d.Cell).GetCenter();
                    float ph = Mathf.PosMod(_time * 2f, 1f);
                    for (int s = 0; s < 3; s++) ci.Arc(c, 4f + 6f * Mathf.PosMod(ph + s / 3f, 1f), 0f, Mathf.Tau, 12, MajSteam.WithAlpha(0.45f * a * (1f - Mathf.PosMod(ph + s / 3f, 1f))), 1f, true);
                    ci.DrawLine(c + new Vector2(-6, -6), c + new Vector2(6, 6), Palette.Warning.WithAlpha(0.7f * a), 1.4f, true); // 휜 문틀
                }
                break;
            case MajorKind.BatteryChain:
                foreach (var f in w.Ship.Furniture.Where(f => k.Furn.Contains(f.Id)))
                {
                    float heat = f.Machine?.Heat ?? 0f;
                    var r = FurnitureRect(f);
                    ci.Box(r.Grow(2f), new Color(1f, 0.3f + 0.4f * (1f - heat), 0.1f, 0.18f + 0.3f * heat * a), false, 2f);
                    for (int s = 0; s < 3; s++) { float x = r.Position.X + r.Size.X * (0.25f + 0.25f * s); float y = r.Position.Y - 3f - 4f * Mathf.PosMod(_time + s * 0.3f, 1f); ci.DrawLine(new Vector2(x, y), new Vector2(x + 2f * Mathf.Sin(_time * 6f + s), y - 4f), Flame.WithAlpha(0.5f * heat * a), 1f, true); }
                }
                break;
            case MajorKind.WaterMainBurst:
            case MajorKind.CoolantHeader:
                if (k.Cells.Count > 0 || k.Kind == MajorKind.WaterMainBurst)
                {
                    var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == k.Room);
                    if (room == null) break;
                    var c = k.Cells.Count > 0 ? CellRect(k.Cells[0]).GetCenter() : ToPx(room.Center);
                    var water = k.Kind == MajorKind.CoolantHeader ? Coolant : NetWater;
                    ci.DrawSetTransform(c + new Vector2(0, 8f), 0f, new Vector2(1f, 0.45f));
                    ci.Circle(Vector2.Zero, T * (0.9f + 0.1f * Mathf.Sin(_time)), water.WithAlpha(0.22f * a), true, -1f, true);
                    ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
                break;
        }
    }

    /// <summary>사고 문양 (r = 반지름 · ph = 움직임 0~1 · grey = 수습 뒤).</summary>
    private void MajorGlyph(CanvasItem ci, MajorKind kind, Vector2 c, float r, float ph, float a, bool grey)
    {
        Color G(Color col) => grey ? new Color(0.62f, 0.64f, 0.68f, a) : col.WithAlpha(a * col.A);
        float s = r / 16f; // 기준 크기 16
        Vector2 P(float x, float y) => c + new Vector2(x * s, y * s);
        void L(float x0, float y0, float x1, float y1, Color col, float wd = 1.6f) => ci.DrawLine(P(x0, y0), P(x1, y1), G(col), wd * s, true);
        void Box(float x, float y, float wdt, float h, Color col, bool fill = true) => ci.Box(new Rect2(P(x, y), new Vector2(wdt * s, h * s)), G(col), fill, fill ? -1f : 1.2f * s);
        void Dot(float x, float y, float rr, Color col) => ci.Circle(P(x, y), rr * s, G(col), true, -1f, true);
        void FlameAt(float x, float y, float h)
        {
            float f = 0.8f + 0.2f * Mathf.Sin(_time * 11f + x);
            ci.Poly(new[] { P(x - 3, y), P(x + 3, y), P(x + 0.6f * Mathf.Sin(_time * 7f), y - h * f) }, G(Flame));
            ci.Poly(new[] { P(x - 1.5f, y), P(x + 1.5f, y), P(x, y - h * 0.55f * f) }, G(FlameCore));
        }
        switch (kind)
        {
            case MajorKind.CoolantHeader:
                Box(-13, -2, 10, 6, Steel); Box(3, -2, 10, 6, Steel);
                ci.Polyline(new[] { P(-3, -2), P(-1, 1), P(-3, 4) }, G(Soot), 1.2f * s, true);
                ci.Polyline(new[] { P(3, -2), P(1, 1), P(3, 4) }, G(Soot), 1.2f * s, true);
                for (int i = 0; i < 3; i++) { float q = Mathf.PosMod(ph + i / 3f, 1f); Dot(-1 + 4 * Mathf.Sin(i * 2f), -4 - 10 * q, 2f + 2f * q, MajSteam.WithAlpha(1f - q)); }
                Dot(0, 7 + 5 * ph, 1.2f, Coolant);
                break;
            case MajorKind.TrunkFire:
                Box(-13, 2, 26, 4, DarkSteel); for (int i = -10; i <= 10; i += 5) L(i, 2, i, 6, Steel, 0.8f);
                L(-13, 0, 13, 0, new Color("#e8b84a"), 1.6f); L(-13, -2, 13, -2, new Color("#c65a2e"), 1.6f);
                FlameAt(-6, -3, 9); FlameAt(2, -3, 12); FlameAt(9, -3, 7);
                break;
            case MajorKind.CascadeDecomp:
                for (int i = 0; i < 3; i++) Box(-14 + i * 10, -4, 8, 8, i == 2 ? Soot : DarkSteel, true);
                Box(6, -4, 8, 8, Steel, false);
                for (int i = 0; i < 2; i++) { float x = -6 + i * 10 + 3 * ph; L(x, 0, x + 3, 0, MajSteam); L(x + 3, 0, x + 1.5f, -1.5f, MajSteam); L(x + 3, 0, x + 1.5f, 1.5f, MajSteam); }
                Dot(10, 0, 1.6f, Palette.Danger);
                break;
            case MajorKind.O2StoreLeak:
                ci.Box(new Rect2(P(-6, -12), new Vector2(12 * s, 22 * s)), G(new Color("#cfe6ff")), true);
                Box(-6, -4, 12, 3, new Color("#4f9fdc"));
                Box(-2, -15, 4, 3, Steel);
                for (int i = 0; i < 4; i++) { float q = Mathf.PosMod(ph * 1.5f + i / 4f, 1f); L(6, -10, 6 + 10 * q, -12 - 2 * q, MajSteam.WithAlpha(1f - q), 1.2f); }
                break;
            case MajorKind.DataCoreFire:
                Box(-7, -12, 14, 22, DarkSteel);
                for (int i = 0; i < 4; i++) { Box(-5, -10 + i * 5, 10, 3, new Color("#1b1f27")); Dot(3.5f, -8.5f + i * 5, 0.8f, (i + (int)(_time * 3)) % 2 == 0 ? Palette.Good : Palette.Danger); }
                FlameAt(-3, -12, 8); FlameAt(3, -12, 10);
                Dot(0, -20 - 4 * ph, 3f + 2 * ph, Soot.WithAlpha(0.8f * (1f - ph)));
                break;
            case MajorKind.PropellantLeak:
                ci.Circle(c, 9 * s, G(new Color("#8a6d3b")), true, -1f, true);
                ci.Arc(c, 9 * s, 0f, Mathf.Tau, 18, G(Steel), 1.2f * s, true);
                Dot(0, 9 + 6 * ph, 1.5f, new Color("#e0a530"));
                for (int i = 0; i < 3; i++) { float x = -8 + i * 8; ci.Polyline(new[] { P(x, -10), P(x + 2, -13 - 2 * ph), P(x, -16), P(x + 2, -19 - 2 * ph) }, G(new Color("#c9e05a").WithAlpha(0.8f)), 1.1f * s, true); }
                break;
            case MajorKind.WaterContam:
                ci.Poly(new[] { P(0, -13), P(8, 2), P(5, 9), P(-5, 9), P(-8, 2) }, G(new Color("#4f9fdc")));
                for (int i = 0; i < 5; i++) Dot(-4 + 2 * i + Mathf.Sin(_time * 2 + i), 1 + 3 * Mathf.Cos(i * 1.7f + _time), 1.2f, new Color("#6b4f2a"));
                L(-10, 10, 10, -10, Palette.Danger, 1.8f);
                break;
            case MajorKind.SwitchboardFire:
                Box(-11, -11, 22, 22, DarkSteel);
                for (int i = 0; i < 4; i++) { Box(-8 + i * 4.5f, -7, 3, 6, new Color("#1b1f27")); L(-6.5f + i * 4.5f, -6, -6.5f + i * 4.5f, i % 2 == 0 ? -2 : -5, Steel, 1f); }
                ci.Polyline(new[] { P(-6, 3), P(-2, 6), P(1, 2), P(5, 7) }, G(new Color("#9fd0ff")), 1.6f * s, true);
                FlameAt(6, 10, 7);
                break;
            case MajorKind.AirPlantFail:
                ci.Arc(c, 11 * s, 0f, Mathf.Tau, 22, G(Steel), 1.4f * s, true);
                for (int i = 0; i < 4; i++) { float ang = i * Mathf.Pi / 2f + 0.3f; ci.Poly(new[] { c, c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 9 * s, c + new Vector2(Mathf.Cos(ang + 0.5f), Mathf.Sin(ang + 0.5f)) * 7 * s }, G(DarkSteel.Lightened(0.2f))); }
                for (int i = 0; i < 3; i++) { float q = Mathf.PosMod(ph + i / 3f, 1f); Dot(8 + 3 * i, -6 - 8 * q, 1.8f, new Color("#9aa0a8").WithAlpha(1f - q)); }
                break;
            case MajorKind.BatteryChain:
                for (int i = 0; i < 3; i++)
                {
                    var col = i == 0 ? new Color("#ff4d2e") : i == 1 ? new Color("#ff9a3a") : new Color("#f5d547");
                    Box(-13 + i * 9, -8, 7, 15, col); Box(-11 + i * 9, -10, 3, 2, Steel);
                    ci.Polyline(new[] { P(-12 + i * 9, -12 - 3 * ph), P(-10 + i * 9, -14), P(-12 + i * 9, -16 - 3 * ph) }, G(col.WithAlpha(0.7f)), 1f * s, true);
                }
                break;
            case MajorKind.SmokeSpread:
                Box(-9, -5, 18, 10, DarkSteel);
                for (int i = 0; i < 4; i++) L(-7, -3 + i * 2, 7, -3 + i * 2, Steel, 0.8f);
                for (int i = 0; i < 4; i++) { float q = Mathf.PosMod(ph + i / 4f, 1f); float ang = -Mathf.Pi / 2f + (i - 1.5f) * 0.6f; Dot(Mathf.Cos(ang) * (6 + 10 * q), -4 + Mathf.Sin(ang) * (6 + 10 * q), 2.5f + 2f * q, new Color("#6f6a66").WithAlpha(0.9f - 0.8f * q)); }
                break;
            case MajorKind.WaterMainBurst:
                Box(-13, -2, 26, 5, new Color("#4f7fb0")); Box(-2, -2, 5, 12, new Color("#4f7fb0"));
                for (int i = 0; i < 6; i++) { float ang = -Mathf.Pi * (0.15f + 0.7f * i / 5f); float q = Mathf.PosMod(ph + i * 0.13f, 1f); Dot(Mathf.Cos(ang) * 10 * q, -2 + Mathf.Sin(ang) * 10 * q, 1.2f, NetWater); }
                break;
            case MajorKind.CropCollapse:
                ci.Polyline(new[] { P(0, 10), P(-1, 2), P(2, -4), P(6, -6) }, G(new Color("#8a7a2a")), 1.6f * s, true);
                ci.Poly(new[] { P(2, -4), P(9, -2), P(7, 2) }, G(new Color("#a68a3a")));
                ci.Poly(new[] { P(-1, 2), P(-9, 4), P(-6, 7) }, G(new Color("#7a5c2e")));
                Box(-8, 9, 16, 3, new Color("#4a3a2a"));
                Dot(5, -1 + 4 * ph, 0.9f, new Color("#2e2410"));
                break;
            case MajorKind.ColdChain:
                for (int i = 0; i < 3; i++) { float ang = i * Mathf.Pi / 3f; L(-Mathf.Cos(ang) * 10, -Mathf.Sin(ang) * 10, Mathf.Cos(ang) * 10, Mathf.Sin(ang) * 10, new Color("#bfe0ff"), 1.4f); }
                ci.Polyline(new[] { P(-6, -8), P(-1, -1), P(-4, 3), P(2, 9) }, G(Palette.Danger), 1.4f * s, true);
                Dot(9, 10 + 4 * ph, 1.2f, new Color("#bfe0ff"));
                break;
            case MajorKind.ReactorCoolingLoss:
            {
                var hex = Enumerable.Range(0, 6).Select(i => P(Mathf.Cos(i * Mathf.Pi / 3f) * 9, Mathf.Sin(i * Mathf.Pi / 3f) * 9)).ToArray();
                ci.Poly(hex, G(new Color("#3a4a3a")));
                Dot(0, 0, 4f + 1f * Mathf.Sin(_time * 6f), new Color("#ff6a3a"));
                Box(11, -10, 3, 18, new Color("#1b1f27")); Box(11, 8 - 18 * (0.5f + 0.5f * ph), 3, 18 * (0.5f + 0.5f * ph), Palette.Danger);
                break;
            }
            case MajorKind.HullCrackRun:
                Box(-12, -10, 24, 20, DarkSteel);
                ci.Polyline(new[] { P(-12, -2), P(-6, 1), P(-2, -4), P(3, 2), P(8 * (0.6f + 0.4f * ph), -1) }, G(Soot), 2f * s, true);
                Dot(8 * (0.6f + 0.4f * ph), -1, 1.3f, FlameCore);
                break;
            case MajorKind.CargoBreakaway:
                ci.DrawSetTransform(c, 0.35f + 0.05f * Mathf.Sin(_time * 3f), new Vector2(s, s));
                ci.Box(new Rect2(-10, -2, 9, 9), G(new Color("#8a6d3b")), true);
                ci.Box(new Rect2(0, -4, 9, 11), G(new Color("#a07a44")), true);
                ci.Box(new Rect2(-5, -12, 9, 9), G(new Color("#7a5c2e")), true);
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                for (int i = 0; i < 3; i++) L(-14, -8 + i * 6, -10, -8 + i * 6, Steel, 1f);
                L(8, -12, 12, -8, HazardYellow, 1.4f); L(12, -12, 10, -10, HazardYellow, 1.4f);
                break;
            case MajorKind.MainBusFail:
                for (int i = 0; i < 3; i++) { Box(-9 + i * 7, -12, 3, 9, Copper); Box(-9 + i * 7, 3, 3, 9, Copper); }
                ci.Polyline(new[] { P(-2, -4), P(2, -1), P(-1, 1), P(3, 4) }, G(FlameCore), 1.6f * s, true);
                if (ph < 0.5f) Dot(0, 0, 3f, new Color("#9fd0ff").WithAlpha(0.6f));
                break;
            case MajorKind.PlateTear:
                Box(-12, -10, 12, 20, DarkSteel);
                ci.Poly(new[] { P(0, -10), P(12, -12 - 3 * ph), P(13, 8), P(0, 10) }, G(Steel));
                for (int i = 0; i < 3; i++) { float q = Mathf.PosMod(ph + i / 3f, 1f); Dot(4 + 12 * q, -6 + 6 * i, 1.1f, ArmorEdge.WithAlpha(1f - q)); }
                break;
            case MajorKind.LifeSupportCascade:
                Dot(-8, -4, 4, new Color("#7cc4ff")); Dot(-3, -4, 4, new Color("#7cc4ff"));
                for (int i = 0; i < 3; i++) { ci.DrawSetTransform(P(-6 + i * 7, 7), 0.3f * i * (0.5f + 0.5f * ph), new Vector2(s, s)); ci.Box(new Rect2(-1.5f, -5, 3, 10), G(i == 0 ? Palette.Danger : Steel), true); ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One); }
                break;
            case MajorKind.MultiZoneFire:
                Box(-2, -12, 4, 24, DarkSteel);
                L(-2, -12, 2, 12, Palette.Warning, 1.2f);
                FlameAt(-8, 8, 12); FlameAt(8, 8, 10 + 3 * ph);
                break;
            case MajorKind.AmmoniaRelease:
                Box(-13, 4, 26, 4, Steel);
                for (int i = 0; i < 5; i++) { float q = Mathf.PosMod(ph + i / 5f, 1f); Dot(-8 + i * 4, 2 - 10 * q, 3 + 2 * q, new Color("#b5e34d").WithAlpha(0.8f - 0.6f * q)); }
                Dot(0, -8, 4, new Color("#d9e86a")); Dot(-1.5f, -9, 0.9f, Soot); Dot(1.5f, -9, 0.9f, Soot);
                break;
            case MajorKind.EngineOverpressure:
                ci.Poly(new[] { P(-12, -6), P(2, -3), P(2, 3), P(-12, 6) }, G(DarkSteel));
                for (int i = 0; i < 3; i++) { float q = Mathf.PosMod(ph + i / 3f, 1f); ci.Arc(P(2, 0), (3 + 10 * q) * s, -0.9f, 0.9f, 10, G(Flame.WithAlpha(1f - q)), 1.3f * s, true); }
                ci.Arc(P(-5, -10), 4 * s, Mathf.Pi, Mathf.Tau, 10, G(Steel), 1f * s, true);
                L(-5, -10, -5 + 3.5f * Mathf.Cos(-0.4f - ph), -10 + 3.5f * Mathf.Sin(-0.4f - ph), Palette.Danger, 1f);
                break;
            case MajorKind.FrameResonance:
                Box(-12, -9, 24, 3, Steel); Box(-12, 6, 24, 3, Steel); Box(-1.5f, -6, 3, 12, Steel);
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < 2; i++) { float q = Mathf.PosMod(ph + i * 0.5f, 1f); ci.Arc(P(side * 14, 0), (3 + 4 * q) * s, side > 0 ? -1f : Mathf.Pi - 1f, side > 0 ? 1f : Mathf.Pi + 1f, 8, G(new Color("#c9d4e4").WithAlpha(1f - q)), 1f * s, true); }
                break;
        }
    }

    /// <summary>흔적: 사고마다 오래 남는 작은 자국.</summary>
    private void PaintMajorTrace(CanvasItem ci, MajorKind kind, Vector2 c, float a)
    {
        switch (kind)
        {
            case MajorKind.CoolantHeader:
            case MajorKind.WaterMainBurst:
                ci.DrawSetTransform(c, 0f, new Vector2(1f, 0.5f));
                ci.Arc(Vector2.Zero, T * 0.7f, 0f, Mathf.Tau, 20, (kind == MajorKind.CoolantHeader ? Coolant : NetWater).WithAlpha(0.35f * a), 2f, true); // 물때 고리
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                break;
            case MajorKind.TrunkFire:
            case MajorKind.DataCoreFire:
            case MajorKind.SwitchboardFire:
            case MajorKind.MultiZoneFire:
            case MajorKind.SmokeSpread:
            case MajorKind.EngineOverpressure:
                for (int i = 0; i < 5; i++) { float ang = i * 1.25f; ci.DrawLine(c, c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * T * 0.6f, Soot.WithAlpha(0.45f * a), 3f, true); } // 그을음 줄기
                break;
            case MajorKind.HullCrackRun:
            case MajorKind.PlateTear:
            case MajorKind.CascadeDecomp:
            case MajorKind.FrameResonance:
                for (int i = -2; i <= 2; i++) ci.DrawLine(c + new Vector2(i * 5f, -3f), c + new Vector2(i * 5f + 3f, 3f), new Color("#c9d4e4").WithAlpha(0.45f * a), 1.4f, true); // 용접선 비늘
                break;
            case MajorKind.O2StoreLeak:
            case MajorKind.AmmoniaRelease:
            case MajorKind.PropellantLeak:
                ci.Poly(new[] { c + new Vector2(0, -6), c + new Vector2(6, 5), c + new Vector2(-6, 5) }, HazardYellow.WithAlpha(0.6f * a)); // 경고 딱지
                ci.DrawLine(c + new Vector2(0, -2), c + new Vector2(0, 2), new Color("#1a1a10").WithAlpha(a), 1.3f);
                break;
            case MajorKind.BatteryChain:
            case MajorKind.MainBusFail:
            case MajorKind.ReactorCoolingLoss:
            case MajorKind.LifeSupportCascade:
                ci.Box(new Rect2(c - new Vector2(5, 5), new Vector2(10, 10)), Copper.WithAlpha(0.45f * a), false, 1.4f); // 새 부품 표시 (반짝이는 새 판)
                ci.DrawLine(c + new Vector2(-3, 0), c + new Vector2(-1, 2), Palette.Good.WithAlpha(0.6f * a), 1.4f, true);
                ci.DrawLine(c + new Vector2(-1, 2), c + new Vector2(3, -2), Palette.Good.WithAlpha(0.6f * a), 1.4f, true);
                break;
            default:
                ci.Circle(c, 4f, new Color("#6b4f2a").WithAlpha(0.35f * a), true, -1f, true); // 얼룩
                break;
        }
    }
}
