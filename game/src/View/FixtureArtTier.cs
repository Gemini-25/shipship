using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5b 설비 단계 · 등급 · 기술 수준이 몸에 보인다 (FixtureArt 의 tier · grade 자리):
///   기술 수준 테두리(모든 설비) — 초기: 리벳 박은 꺾쇠 받침 / 중간: 이음매 받침띠 · 나사 / 고급: 둥근 얇은 테 · 바닥에 뜬 빛줄.
///   단계 II~IV(설비마다 다른 부품 조합 · 자리, TechLookTable.Kits) — II 도장 띠 + 부품, III 티타늄 광택 + 부품, IV 검은 광택 · 에너지 줄 + 덮개 · 빛.
///     부품 30종: 방열 핀 · 보조 탱크 · 코일 · 센서 돔 · 케이블 다발 · 빛 띠 · 화면 · 안테나 · 보강판 · 덮개 · 회전 팬 · 계기 · 관 고리 · 빛 심 ·
///     홀로그램 · 장 고리 · 배기 그릴 · 레일 · 쿠션 · 작업등 · 서랍 · 필터 · 접시 · 유리 · 제어 칩 · 누빔 · 셀 · 차양 · 관절 팔 · 밸브.
///     기존 단계 모양(ShipViewTiers · 핵융합)은 그대로 — 이 부품들은 그 둘레에 붙는다.
///   Mk.1 임시품(설비 성격마다 다른 손질 · 고장 나던 자리에): 색이 다른 판(삐뚤게) · 굵은 볼트 + 은색 테이프 / 호스 조임쇠 / 점퍼선 / 케이블 타이 /
///     밧줄 / 괸 쐐기 / 테이프 붙인 화면 / 받친 깡통 / 볼트로 단 선풍기 / 거친 용접 덧살 / 판지 — 고장 나면 테이프가 펄럭이고 점퍼선이 튄다.
///   Mk.3 개량형(마감 6종): 빛나는 테두리 · 갈매기 문장 · 유리 앞판 · 양극 산화 띠 · 가는 장식 줄 · 빛 고리 + 매끈한 모서리 광택.
/// 정적 몸은 PaintBody 가(Weathering 뒤), 움직임은 PaintState 가 부른다. Core 상태는 읽기만.
/// </summary>
public static partial class FixtureArt
{
    private static readonly Color Cu2 = new("#b87333");

    // ═══════════════════════════════ 정적 (PaintBody) ═══════════════════════════════

    private static void TierGrade(in Fix x, Art a)
    {
        LookFrame(in x, TechLook.Now);
        if (TechLookTable.Kit(x.F.Type) is not TierKit kit) return;
        if (x.Tier >= 2)
        {
            TierMaterial(in x, kit, x.Tier);
            for (int t = 2; t <= Mathf.Min(4, x.Tier); t++)
                foreach (var pa in kit.At(t)) TierPartStatic(in x, pa, t, kit);
        }
        if (x.M == null) return;
        if (x.Grade == MachineGrade.Mk1) Mk1Static(in x, a, kit);
        else if (x.Grade == MachineGrade.Mk3) Mk3Static(in x, kit);
    }

    /// <summary>기술 수준 테두리 (모든 설비 · 가구).</summary>
    private static void LookFrame(in Fix x, LookSet s)
    {
        var ci = x.Ci;
        var r = x.R.Grow(-1.5f);
        var edge = TechLook.C(s.WallEdge);
        switch (s.Frame)
        {
            case FrameStyle.Riveted:
            {
                // 리벳 박은 꺾쇠 받침 넷
                float L = x.Px(5f);
                var dark = new Color("#2a2e34");
                for (int k = 0; k < 4; k++)
                {
                    float sx = (k & 1) == 0 ? 1f : -1f, sy = (k & 2) == 0 ? 1f : -1f;
                    var c = new Vector2(sx > 0 ? r.Position.X : r.End.X, sy > 0 ? r.Position.Y : r.End.Y);
                    ci.DrawLine(c, c + new Vector2(sx * L, 0f), dark, 2.2f);
                    ci.DrawLine(c, c + new Vector2(0f, sy * L), dark, 2.2f);
                    ci.DrawLine(c + new Vector2(sx * 0.6f, sy * 0.6f), c + new Vector2(sx * L, sy * 0.6f), edge.WithAlpha(0.35f), 0.7f);
                    ci.DrawCircle(c + new Vector2(sx * L * 0.65f, sy * 0.9f), 0.8f, edge.Lightened(0.2f), true, -1f, true);
                    ci.DrawCircle(c + new Vector2(sx * 0.9f, sy * L * 0.65f), 0.8f, edge.Lightened(0.2f), true, -1f, true);
                }
                break;
            }
            case FrameStyle.Seamed:
            {
                // 이음매 받침띠 (쓰는 쪽) · 나사 둘
                var f = x.Front;
                Vector2 a0, a1;
                if (f.Y > 0.5f) { a0 = new Vector2(r.Position.X + 2f, r.End.Y); a1 = new Vector2(r.End.X - 2f, r.End.Y); }
                else if (f.Y < -0.5f) { a0 = new Vector2(r.Position.X + 2f, r.Position.Y); a1 = new Vector2(r.End.X - 2f, r.Position.Y); }
                else if (f.X > 0.5f) { a0 = new Vector2(r.End.X, r.Position.Y + 2f); a1 = new Vector2(r.End.X, r.End.Y - 2f); }
                else { a0 = new Vector2(r.Position.X, r.Position.Y + 2f); a1 = new Vector2(r.Position.X, r.End.Y - 2f); }
                ci.DrawLine(a0, a1, new Color("#1a1f26"), 2.4f);
                ci.DrawLine(a0, a1, edge.WithAlpha(0.45f), 0.8f);
                Dot(ci, a0.Lerp(a1, 0.15f), 0.8f, edge);
                Dot(ci, a0.Lerp(a1, 0.85f), 0.8f, edge);
                break;
            }
            default:
            {
                // 둥근 얇은 테 (이음매 없음) — 빛줄은 움직임 층
                Gfx.RoundRect(ci, r, new Color(0, 0, 0, 0f), Mathf.Min(s.Corner, Mathf.Min(r.Size.X, r.Size.Y) * 0.3f), TechLook.C(s.Trim).WithAlpha(0.32f), 1);
                break;
            }
        }
    }

    /// <summary>단계 소재: II 도장 띠 · III 티타늄 광택 · IV 검은 광택 + 에너지 줄.</summary>
    private static void TierMaterial(in Fix x, TierKit kit, int tier)
    {
        var ci = x.Ci;
        var glow = TechLook.C(kit.Glow);
        switch (tier)
        {
            case 2:
                Gfx.RoundRect(ci, x.R.Grow(-1.2f), new Color(0, 0, 0, 0f), 4, Hud.TierColor(2).WithAlpha(0.35f), 1); // 받침 테
                ci.DrawColoredPolygon(new[] { x.P(0.04f, 0.02f), x.P(0.96f, 0.02f), x.P(0.96f, 0.07f), x.P(0.04f, 0.07f) }, Hud.TierColor(2).WithAlpha(0.45f));
                break;
            case 3:
            {
                // 커진 받침: 몸체 둘레 여백까지 티타늄 받침대가 차지한다
                Gfx.RoundRect(ci, x.R.Grow(-0.8f), new Color(0, 0, 0, 0f), 5, new Color("#8a96a6").WithAlpha(0.7f), 2);
                var b = x.B;
                ci.DrawPolygon(new[] { b.Position, new Vector2(b.End.X, b.Position.Y), new Vector2(b.Position.X, b.End.Y) },
                    new[] { new Color(1, 1, 1, 0.09f), new Color(1, 1, 1, 0.02f), new Color(1, 1, 1, 0.02f) });
                ci.DrawLine(x.P(0.03f, 0.03f), x.P(0.97f, 0.03f), new Color("#dfe6ee").WithAlpha(0.45f), 1f);
                ci.DrawLine(x.P(0.03f, 0.03f), x.P(0.03f, 0.97f), new Color("#dfe6ee").WithAlpha(0.3f), 1f);
                break;
            }
            default:
            {
                // 가장 큰 받침: 검은 받침대 + 모서리 빛 마디
                Gfx.RoundRect(ci, x.R.Grow(-0.5f), new Color(0, 0, 0, 0f), 6, new Color("#14181f").WithAlpha(0.9f), 3);
                foreach (var cpt in new[] { x.R.Position + new Vector2(2f, 2f), new Vector2(x.R.End.X - 2f, x.R.Position.Y + 2f), new Vector2(x.R.Position.X + 2f, x.R.End.Y - 2f), x.R.End - new Vector2(2f, 2f) })
                    Dot(ci, cpt, 1f, glow.WithAlpha(0.8f));
                Gfx.RoundRect(ci, x.B, new Color(0.02f, 0.03f, 0.05f, 0.22f), 5);
                ci.DrawLine(x.P(0.06f, 0.05f), x.P(0.94f, 0.05f), glow.WithAlpha(0.55f), 1f);
                ci.DrawLine(x.P(0.06f, 0.95f), x.P(0.94f, 0.95f), glow.WithAlpha(0.55f), 1f);
                break;
            }
        }
    }

    /// <summary>단계 부품 하나 (정적 몸).</summary>
    private static void TierPartStatic(in Fix x, PartAt pa, int tier, TierKit kit)
    {
        var ci = x.Ci;
        var p = x.P(pa.U, pa.V);
        float s = x.S;
        var glow = TechLook.C(kit.Glow);
        bool edgeV = pa.V < 0.12f || pa.V > 0.88f; // 긴 쪽 가장자리면 긴 쪽을 따라 늘인다
        var along = edgeV ? x.U : x.V;
        var across = edgeV ? x.V : x.U;
        switch (pa.Part)
        {
            case TierPart.Fins:
                for (int k = -2; k <= 2; k++)
                {
                    var o = p + along * (k * 2.6f * s);
                    Line(ci, o - across * 2.6f * s, o + across * 2.6f * s, Steel4, 1.2f);
                    Line(ci, o - across * 2.6f * s, o + across * 2.6f * s, new Color(1, 1, 1, 0.15f), 0.4f);
                }
                break;
            case TierPart.Tank:
                Can(ci, p, 3.2f * s, Steel2, Chrome);
                Line(ci, p - across * 2f * s, p + across * 2f * s, new Color(0, 0, 0, 0.35f), 0.6f);
                break;
            case TierPart.Coil:
                Box(ci, new Rect2(p - (along * 5f + across * 2.4f).Abs() * s, (along * 10f + across * 4.8f).Abs() * s), new Color("#3a2416"), 1f);
                for (int k = 0; k < 6; k++)
                {
                    var o = p + along * ((k - 2.5f) * 1.6f * s);
                    Line(ci, o - across * 2.2f * s, o + across * 2.2f * s, Cu2, 1f);
                }
                break;
            case TierPart.Pod:
                Dot(ci, p, 2.8f * s, Steel1);
                ci.DrawArc(p, 2.8f * s, 0f, Mathf.Tau, 14, Chrome, 0.8f, true);
                Dot(ci, p + new Vector2(-0.7f, -0.7f) * s, 0.9f * s, glow.WithAlpha(0.6f));
                break;
            case TierPart.Cables:
                for (int k = -1; k <= 1; k++)
                {
                    var a0 = p + along * (k * 2f * s);
                    var a1 = a0 + across * (pa.V > 0.5f || pa.U > 0.5f ? 1f : -1f) * 7f * s + along * k * 2f;
                    Cable(ci, a0, a1, 0.6f, k == 0 ? new Color("#2a2a2a") : k < 0 ? new Color("#7a2a22") : new Color("#22447a"), 1.3f);
                }
                break;
            case TierPart.Strip:
            {
                var a0 = p - along * (x.Lu * 0.3f) * (edgeV ? 1f : 0.4f);
                var a1 = p + along * (x.Lu * 0.3f) * (edgeV ? 1f : 0.4f);
                Line(ci, a0, a1, new Color("#0a0c10"), 2.6f);
                Line(ci, a0, a1, glow.WithAlpha(0.35f), 1f);
                break;
            }
            case TierPart.Screen:
            {
                var r = new Rect2(p - new Vector2(4f, 2.6f) * s, new Vector2(8f, 5.2f) * s);
                Box(ci, r.Grow(0.8f), Steel3, 1.5f);
                Box(ci, r, GlassDark, 1f);
                for (int k = 0; k < 3; k++) Line(ci, r.Position + new Vector2(1f, 1.2f + k * 1.3f) * s, r.Position + new Vector2(3f + 2f * k, 1.2f + k * 1.3f) * s, glow.WithAlpha(0.45f), 0.6f);
                break;
            }
            case TierPart.Antenna:
            {
                var tip = p + across * (pa.V < 0.5f ? -1f : 1f) * 7f * s + along * 2f * s;
                Line(ci, p, tip, Chrome, 1f);
                Dot(ci, p, 1.4f * s, Steel3);
                Dot(ci, tip, 1.1f * s, glow.Darkened(0.3f));
                break;
            }
            case TierPart.Plating:
            {
                var r = new Rect2(p - new Vector2(4.5f, 3f) * s, new Vector2(9f, 6f) * s);
                Box(ci, r, Steel3, 0.8f, Steel4);
                Bolts(ci, r, 1.4f * s, 0.6f);
                break;
            }
            case TierPart.Shroud:
            {
                var c0 = x.P(0.18f, pa.V < 0.5f ? 0.02f : 0.55f);
                var c1 = x.P(0.82f, pa.V < 0.5f ? 0.45f : 0.98f);
                var r = new Rect2(new Vector2(Mathf.Min(c0.X, c1.X), Mathf.Min(c0.Y, c1.Y)), (c1 - c0).Abs());
                Box(ci, r, Steel2.Lightened(0.12f).WithAlpha(0.85f), Mathf.Min(r.Size.X, r.Size.Y) * 0.45f, glow.WithAlpha(0.4f));
                ci.DrawLine(r.Position + new Vector2(r.Size.X * 0.2f, 1.5f), new Vector2(r.End.X - r.Size.X * 0.2f, r.Position.Y + 1.5f), new Color(1, 1, 1, 0.22f), 1f);
                break;
            }
            case TierPart.Rotor:
                Dot(ci, p, 3.8f * s, Steel0);
                Ring(ci, p, 3.8f * s, Steel4, 1f, 16);
                break;
            case TierPart.Gauges:
                Gauge(ci, p - along * 2.6f * s, 2.2f * s, 0.55f, Danger);
                Gauge(ci, p + along * 2.6f * s, 2.2f * s, 0.3f, Danger);
                break;
            case TierPart.PipeLoop:
            {
                var a0 = p - along * 3f * s;
                var a1 = p + along * 3f * s;
                var out1 = across * (pa.V > 0.5f || pa.U > 0.5f ? 1f : -1f) * 5f * s;
                Pipe(ci, a0, a0 + out1, 1.8f * s, new Color("#6a8aa0"), false);
                Pipe(ci, a0 + out1, a1 + out1, 1.8f * s, new Color("#6a8aa0"), false);
                Pipe(ci, a1 + out1, a1, 1.8f * s, new Color("#6a8aa0"), false);
                break;
            }
            case TierPart.Core:
            {
                var hex = new Vector2[7];
                for (int k = 0; k < 7; k++) hex[k] = p + Vector2.FromAngle(k * Mathf.Tau / 6f) * 3.4f * s;
                ci.DrawColoredPolygon(hex[..6], new Color("#0a0c12"));
                ci.DrawPolyline(hex, glow.WithAlpha(0.6f), 0.9f, true);
                Dot(ci, p, 1.2f * s, glow.Darkened(0.4f));
                break;
            }
            case TierPart.Holo:
                Dot(ci, p, 2.4f * s, Steel1);
                ci.DrawArc(p, 2.4f * s, 0f, Mathf.Tau, 12, glow.WithAlpha(0.6f), 0.7f, true);
                ci.DrawArc(p, 1.2f * s, 0f, Mathf.Tau, 8, glow.WithAlpha(0.4f), 0.5f, true);
                break;
            case TierPart.Field:
                foreach (var cpt in new[] { x.P(0.04f, 0.06f), x.P(0.96f, 0.06f), x.P(0.04f, 0.94f), x.P(0.96f, 0.94f) })
                {
                    Dot(ci, cpt, 1.5f * s, Steel3);
                    Dot(ci, cpt, 0.7f * s, glow.WithAlpha(0.7f));
                }
                break;
            case TierPart.Vents:
            {
                var r = new Rect2(p - (along * 4f + across * 2.4f).Abs() * s, (along * 8f + across * 4.8f).Abs() * s);
                Box(ci, r, Steel0, 1f);
                Vents(ci, r.Grow(-0.8f), 4, edgeV ? x.Wide : !x.Wide, new Color(1, 1, 1, 0.2f), 0.6f);
                break;
            }
            case TierPart.Rail:
            {
                var a0 = p - along * (x.Lu * 0.4f);
                var a1 = p + along * (x.Lu * 0.4f);
                Line(ci, a0, a1, Steel4, 1.6f);
                Line(ci, a0 + across * 1.2f, a1 + across * 1.2f, new Color(0, 0, 0, 0.4f), 0.6f);
                break;
            }
            case TierPart.Cushion:
            {
                var r = new Rect2(p - new Vector2(5f, 3.5f) * s, new Vector2(10f, 7f) * s);
                Box(ci, r, x.Accent.Darkened(0.35f).WithAlpha(0.85f), 3f * s);
                ci.DrawRect(r.Grow(-1.2f * s), new Color(1, 1, 1, 0.15f), false, 0.5f);
                break;
            }
            case TierPart.Lamp:
            {
                var head = p + across * (pa.V < 0.5f ? 1f : -1f) * 3f * s + along * 3f * s;
                Line(ci, p, head, Steel4, 1f);
                Dot(ci, p, 1.2f * s, Steel3);
                ci.DrawColoredPolygon(new[] { head - along * 1.6f * s, head + along * 1.6f * s, head + along * 2.4f * s + across * 1.6f * s, head - along * 2.4f * s + across * 1.6f * s }, new Color("#c8a050"));
                break;
            }
            case TierPart.Drawers:
                for (int k = -1; k <= 1; k++)
                {
                    var o = p + along * (k * 3.6f * s);
                    var r = new Rect2(o - new Vector2(1.6f, 1.6f) * s, new Vector2(3.2f, 3.2f) * s);
                    Box(ci, r, Steel2, 0.6f, Steel4);
                    Dot(ci, o, 0.5f * s, Chrome);
                }
                break;
            case TierPart.Filter:
            {
                var r = new Rect2(p - (along * 2.4f + across * 4f).Abs() * s, (along * 4.8f + across * 8f).Abs() * s);
                Box(ci, r, new Color("#d8d2c0"), 1.5f, Steel4);
                Vents(ci, r.Grow(-0.8f), 5, !(edgeV ? x.Wide : !x.Wide), new Color(0, 0, 0, 0.25f), 0.5f);
                break;
            }
            case TierPart.Dish:
            {
                var dir = across * (pa.V < 0.5f ? -1f : 1f);
                float ang = dir.Angle();
                ci.DrawArc(p, 3.6f * s, ang - 1.1f, ang + 1.1f, 10, Chrome, 1.4f, true);
                Line(ci, p, p + dir * 3f * s, Steel4, 0.8f);
                Dot(ci, p + dir * 3f * s, 0.7f * s, glow);
                break;
            }
            case TierPart.Glass:
            {
                var r = new Rect2(x.P(0.2f, 0.18f), Vector2.Zero).Expand(x.P(0.8f, 0.82f));
                Box(ci, r, new Color(0.6f, 0.8f, 0.95f, 0.08f), 4f, new Color(1, 1, 1, 0.28f));
                ci.DrawLine(r.Position + new Vector2(2f, r.Size.Y * 0.5f), r.Position + new Vector2(r.Size.X * 0.4f, 2f), new Color(1, 1, 1, 0.18f), 1.2f, true);
                break;
            }
            case TierPart.Chip:
            {
                var r = new Rect2(p - new Vector2(3f, 2.2f) * s, new Vector2(6f, 4.4f) * s);
                Box(ci, r, new Color("#1f5a2a"), 0.6f);
                for (int k = 0; k < 4; k++) Dot(ci, r.Position + new Vector2((1f + k * 1.3f) * s, 0f), 0.4f * s, Brass);
                Box(ci, new Rect2(r.GetCenter() - new Vector2(1.2f, 1f) * s, new Vector2(2.4f, 2f) * s), Steel0, 0.3f);
                break;
            }
            case TierPart.Quilt:
            {
                var r = new Rect2(x.P(0.35f, 0.15f), Vector2.Zero).Expand(x.P(0.9f, 0.85f));
                ci.DrawRect(r, x.Accent.Darkened(0.2f).WithAlpha(0.45f));
                for (float d = 0f; d < r.Size.X + r.Size.Y; d += 4f * s)
                {
                    ci.DrawLine(new Vector2(r.Position.X + d, r.Position.Y), new Vector2(r.Position.X, r.Position.Y + d), new Color(1, 1, 1, 0.12f), 0.5f);
                }
                break;
            }
            case TierPart.Cells:
                for (int k = 0; k < 4; k++)
                {
                    var o = p + along * ((k - 1.5f) * 3f * s);
                    Box(ci, new Rect2(o - new Vector2(1.2f, 2.2f) * s, new Vector2(2.4f, 4.4f) * s), new Color("#2a3a2a"), 0.5f, new Color("#6ee07a").WithAlpha(0.5f));
                }
                break;
            case TierPart.Hood:
            {
                var r = new Rect2(x.P(0.05f, 0.05f), Vector2.Zero).Expand(x.P(0.5f, 0.95f));
                Box(ci, r, new Color(0.8f, 0.85f, 0.95f, 0.16f), Mathf.Min(r.Size.X, r.Size.Y) * 0.4f, new Color(1, 1, 1, 0.35f));
                break;
            }
            case TierPart.Arm:
            {
                var j1 = p + across * 4f * s + along * 2f * s;
                var j2 = j1 + along * 4f * s - across * 1f * s;
                Line(ci, p, j1, Chrome, 1.6f);
                Line(ci, j1, j2, Chrome, 1.3f);
                Dot(ci, p, 1.6f * s, Steel3);
                Dot(ci, j1, 1f * s, Steel0);
                break;
            }
            case TierPart.Valve:
                Ring(ci, p, 2.8f * s, new Color("#c0392b"), 1.2f, 14);
                for (int k = 0; k < 3; k++) Line(ci, p, p + Vector2.FromAngle(k * Mathf.Tau / 3f + 0.4f) * 2.8f * s, new Color("#c0392b"), 0.7f);
                Dot(ci, p, 0.8f * s, Steel3);
                break;
        }
    }

    // ── Mk.1 임시품: 색이 다른 판(삐뚤게) · 굵은 볼트 + 설비 성격마다 다른 손질 · 딱지 ──
    private static void Mk1Static(in Fix x, Art a, TierKit kit)
    {
        var ci = x.Ci;
        var e = x.P(Mathf.Clamp(a.Eu, 0.25f, 0.75f), Mathf.Clamp(a.Ev, 0.3f, 0.7f));
        float s = x.S;
        // 색이 다른 판 (밑칠 붉은색 · 국방색 · 녹슨 청색 중 하나) — 삐뚤게 붙였다
        var odd = (x.Id % 3) switch { 0 => new Color("#7a4a32"), 1 => new Color("#5a6040"), _ => new Color("#3a5468") };
        float tilt = (Hash(x.Id, 1, 480) - 0.5f) * 0.35f;
        ci.DrawSetTransform(e, tilt, Vector2.One);
        var pr = new Rect2(new Vector2(-5.5f, -3.8f) * s, new Vector2(11f, 7.6f) * s);
        Box(ci, pr, odd.WithAlpha(0.92f), 0.5f, odd.Lightened(0.3f));
        foreach (var bp in new[] { pr.Position + new Vector2(1.4f, 1.4f) * s, new Vector2(pr.End.X - 1.4f * s, pr.Position.Y + 1.4f * s), pr.End - new Vector2(1.4f, 1.4f) * s, new Vector2(pr.Position.X + 1.4f * s, pr.End.Y - 1.4f * s) })
        {
            var hex = new Vector2[6];
            for (int k = 0; k < 6; k++) hex[k] = bp + Vector2.FromAngle(k * Mathf.Tau / 6f) * 1.3f * s;
            ci.DrawColoredPolygon(hex, new Color("#8a929e"));
            Dot(ci, bp, 0.5f * s, new Color("#30343a"));
        }
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        // 손질 (성격마다)
        switch (kit.Mk1)
        {
            case Improv.DuctTape:
                for (int k = 0; k < 2; k++)
                {
                    ci.DrawSetTransform(e + new Vector2(k * 2f - 1f, 0f) * s, (k == 0 ? 0.6f : -0.55f) + tilt, Vector2.One);
                    ci.DrawRect(new Rect2(new Vector2(-7f, -1.5f) * s, new Vector2(14f, 3f) * s), new Color("#b8bcc4").WithAlpha(0.9f));
                    for (int w = 0; w < 4; w++) ci.DrawLine(new Vector2(-5f + w * 3.4f, -1.5f) * s, new Vector2(-4.4f + w * 3.4f, 1.5f) * s, new Color(0, 0, 0, 0.15f), 0.5f);
                }
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                break;
            case Improv.HoseClamp:
            {
                var h0 = e + x.U * (-8f * s);
                var h1 = e + x.U * (8f * s);
                Line(ci, h0, h1, new Color("#1a1c20"), 3.4f * s);
                foreach (float f in new[] { 0.25f, 0.75f })
                {
                    var o = h0.Lerp(h1, f);
                    Line(ci, o - x.V * 2.4f * s, o + x.V * 2.4f * s, Chrome, 1.3f);
                    Dot(ci, o + x.V * 2.6f * s, 0.8f * s, Steel4);
                }
                Dot(ci, e + x.V * 5f * s, 2.4f * s, new Color(0.3f, 0.45f, 0.6f, 0.3f));
                break;
            }
            case Improv.JumperWire:
            {
                var a0 = x.P(0.1f, 0.15f);
                var a1 = x.P(0.85f, 0.8f);
                Cable(ci, a0, a1, 3f, new Color("#c0302a"), 1.1f);
                Cable(ci, a0 + x.V * 2f, a1 + x.V * 2f, 2.4f, new Color("#1a1a1a"), 1.1f);
                foreach (var cp in new[] { a0, a1 }) { Box(ci, new Rect2(cp - new Vector2(1.6f, 1f), new Vector2(3.2f, 2f)), new Color("#c8a050"), 0.3f); }
                break;
            }
            case Improv.ZipTies:
            {
                var a0 = x.P(0.12f, 0.88f);
                var a1 = x.P(0.88f, 0.88f);
                for (int k = 0; k < 3; k++) Cable(ci, a0 + x.V * (k - 1) * 1.2f, a1 + x.V * (k - 1) * 1.2f, 1f, new Color("#2a2a30"), 0.9f);
                for (int k = 1; k < 4; k++)
                {
                    var o = a0.Lerp(a1, k / 4f);
                    Line(ci, o - x.V * 2.4f, o + x.V * 2.4f, new Color("#f0f0f0"), 1f);
                    Line(ci, o + x.V * 2.4f, o + x.V * 2.4f + x.U * 2f, new Color("#f0f0f0"), 0.6f);
                }
                break;
            }
            case Improv.Rope:
            {
                var rope = new Color("#a8844f");
                Line(ci, x.P(0.3f, -0.05f), x.P(0.42f, 1.05f), rope, 1.6f);
                Line(ci, x.P(0.62f, -0.05f), x.P(0.74f, 1.05f), rope, 1.6f);
                for (int k = 0; k < 6; k++)
                {
                    var o = x.P(0.3f + 0.12f * k / 5f, -0.05f + 1.1f * k / 5f);
                    Line(ci, o - x.U * 0.8f, o + x.U * 0.8f + x.V * 0.8f, rope.Darkened(0.35f), 0.5f);
                }
                Dot(ci, x.P(0.42f, 1.02f), 1.8f, rope.Darkened(0.15f));
                break;
            }
            case Improv.Shim:
            {
                var c = new Vector2(x.R.End.X - 3f, x.R.End.Y - 1f);
                ci.DrawColoredPolygon(new[] { c, c + new Vector2(-7f, 0f), c + new Vector2(0f, -3f) }, new Color("#b8905a"));
                ci.DrawLine(c + new Vector2(-7f, 0f), c + new Vector2(0f, -3f), new Color("#6a4a2a"), 0.6f);
                ci.DrawLine(x.P(0.02f, 0.98f), x.P(0.98f, 0.93f), new Color(0, 0, 0, 0.3f), 0.8f);
                break;
            }
            case Improv.TapedScreen:
            {
                var o = x.P(0.5f, 0.35f);
                for (int k = 0; k < 4; k++) Line(ci, o, o + Vector2.FromAngle(k * 1.7f + 0.3f) * (4f + 3f * Hash(x.Id, k, 481)) * s, new Color(1, 1, 1, 0.55f), 0.5f);
                ci.DrawSetTransform(o, 0.15f, Vector2.One);
                ci.DrawRect(new Rect2(new Vector2(-6f, -1.2f) * s, new Vector2(12f, 2.4f) * s), new Color("#e8e0b0").WithAlpha(0.7f));
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                break;
            }
            case Improv.DripCan:
            {
                var can = new Vector2(x.R.End.X - 5f, x.R.End.Y - 5f);
                Can(ci, can, 3f, new Color("#7a828e"), new Color("#c8ccd2"));
                Dot(ci, can, 1.8f, new Color("#3a6a98").WithAlpha(0.8f));
                Line(ci, e, can + new Vector2(0f, -3f), new Color(0.3f, 0.5f, 0.7f, 0.4f), 0.8f);
                break;
            }
            case Improv.BoltedFan:
            {
                var o = x.P(0.98f, 0.2f) + x.U * 3f;
                Line(ci, o, o - x.U * 5f, Steel4, 1.4f);
                Dot(ci, o, 3.6f, Steel1);
                Ring(ci, o, 3.6f, Chrome, 0.8f, 14);
                Grille(ci, new Rect2(o - new Vector2(2.6f, 2.6f), new Vector2(5.2f, 5.2f)), 1.3f, new Color(1, 1, 1, 0.25f), 0.4f);
                break;
            }
            case Improv.WeldBead:
            {
                var a0 = x.P(0.15f, 0.5f);
                var a1 = x.P(0.85f, 0.55f);
                ci.DrawLine(a0, a1, TemperGold.WithAlpha(0.25f), 4f);
                for (int k = 0; k <= 10; k++)
                    Dot(ci, a0.Lerp(a1, k / 10f) + x.V * (Hash(x.Id, k, 482) - 0.5f) * 1.6f, 1f + 0.8f * Hash(x.Id, k, 483), new Color("#8a8f99"));
                break;
            }
            default: // 판지
            {
                ci.DrawSetTransform(e + x.V * 2f, -tilt, Vector2.One);
                var cb = new Rect2(new Vector2(-4.5f, -3f) * s, new Vector2(9f, 6f) * s);
                ci.DrawRect(cb, new Color("#a8844f"));
                for (float yy = cb.Position.Y + 1f; yy < cb.End.Y; yy += 1.4f) ci.DrawLine(new Vector2(cb.Position.X, yy), new Vector2(cb.End.X, yy), new Color(0, 0, 0, 0.12f), 0.4f);
                ci.DrawPolyline(new[] { new Vector2(-3f, 0f) * s, new Vector2(-1f, -1.4f) * s, new Vector2(1f, 0.8f) * s, new Vector2(3f, -1f) * s }, new Color("#1a1a1a").WithAlpha(0.7f), 0.6f, true);
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                break;
            }
        }
        // 딱지
        var tag = new Rect2(x.R.Position + new Vector2(2f, 2f), new Vector2(22f, 11f));
        Gfx.RoundRect(ci, tag, WarnYellow.WithAlpha(0.92f), 3);
        Gfx.TextCentered(ci, Fonts.Bold, tag.GetCenter(), "Mk.1", 8, new Color("#1a1710"));
    }

    // ── Mk.3 개량형: 마감마다 다른 표 + 매끈한 모서리 광택 ──
    private static void Mk3Static(in Fix x, TierKit kit)
    {
        var ci = x.Ci;
        var glow = TechLook.C(kit.Glow);
        var b = x.B;
        Gfx.RoundRect(ci, b.Grow(0.5f), new Color(0, 0, 0, 0f), 6, new Color(1, 1, 1, 0.16f), 1);
        switch (kit.Mk3)
        {
            case Trim.EdgeGlow:
                Gfx.RoundRect(ci, b.Grow(-0.5f), new Color(0, 0, 0, 0f), 5, glow.WithAlpha(0.45f), 1);
                break;
            case Trim.Chevron:
            {
                var o = new Vector2(b.End.X - 7f, b.End.Y - 9f); // 오른쪽 아래 (위는 단계 딱지 자리)
                for (int k = 0; k < 2; k++)
                    ci.DrawPolyline(new[] { o + new Vector2(-3f, 1.5f + k * 2.6f), o + new Vector2(0f, -1f + k * 2.6f), o + new Vector2(3f, 1.5f + k * 2.6f) }, glow, 1.2f, true);
                break;
            }
            case Trim.GlassFace:
            {
                var r = new Rect2(x.P(0.08f, 0.62f), Vector2.Zero).Expand(x.P(0.92f, 0.92f));
                Glass(ci, r, new Color(0.5f, 0.75f, 0.9f, 0.18f), 2f);
                break;
            }
            case Trim.Anodized:
                ci.DrawColoredPolygon(new[] { x.P(0.02f, 0.88f), x.P(0.98f, 0.88f), x.P(0.98f, 0.98f), x.P(0.02f, 0.98f) }, glow.Darkened(0.2f).WithAlpha(0.55f));
                ci.DrawLine(x.P(0.02f, 0.88f), x.P(0.98f, 0.88f), Colors.White.WithAlpha(0.25f), 0.6f);
                break;
            case Trim.Pinstripe:
                Line(ci, x.P(0.05f, 0.08f), x.P(0.95f, 0.08f), glow.WithAlpha(0.7f), 0.6f);
                Line(ci, x.P(0.05f, 0.12f), x.P(0.95f, 0.12f), glow.WithAlpha(0.4f), 0.6f);
                break;
            default: // 빛 고리 문장 (오른쪽 아래)
                Ring(ci, x.P(0.88f, 0.8f), 3.2f, glow, 1.2f, 16);
                Dot(ci, x.P(0.88f, 0.8f), 1f, glow);
                break;
        }
    }

    // ═══════════════════════════════ 움직임 (PaintState) ═══════════════════════════════

    private static void TierGradeLife(in Fix x, Art a, Machine m)
    {
        var ci = x.Ci;
        var look = TechLook.Now;
        // 고급 테: 바닥에 뜬 빛줄 (켜져 있을 때)
        if (look.Frame == FrameStyle.Seamless && x.Lit && x.Lod > 0)
        {
            var f = x.Front;
            var r = x.R.Grow(-1f);
            Vector2 a0, a1;
            if (Mathf.Abs(f.Y) > 0.5f) { float y = f.Y > 0 ? r.End.Y : r.Position.Y; a0 = new Vector2(r.Position.X + 4f, y); a1 = new Vector2(r.End.X - 4f, y); }
            else { float xx = f.X > 0 ? r.End.X : r.Position.X; a0 = new Vector2(xx, r.Position.Y + 4f); a1 = new Vector2(xx, r.End.Y - 4f); }
            Line(ci, a0, a1, TechLook.Light.WithAlpha(0.35f * x.Glow), 1f);
        }
        if (TechLookTable.Kit(x.F.Type) is not TierKit kit) return;
        var glow = TechLook.C(kit.Glow);
        if (x.Tier >= 2 && x.Lod > 0)
            for (int t = 2; t <= Mathf.Min(4, x.Tier); t++)
                foreach (var pa in kit.At(t)) TierPartLife(in x, pa, glow);
        if (x.Grade == MachineGrade.Mk3 && x.Lit)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(x.T * 1.6f);
            if (kit.Mk3 == Trim.EdgeGlow) Gfx.RoundRect(ci, x.B.Grow(-0.5f), new Color(0, 0, 0, 0f), 5, glow.WithAlpha((0.25f + 0.35f * pulse) * x.Glow), 1);
            else Line(ci, x.P(0.3f, 0.97f), x.P(0.7f, 0.97f), glow.WithAlpha((0.4f + 0.4f * pulse) * x.Glow), 1.2f);
        }
        else if (x.Grade == MachineGrade.Mk1 && x.Lod > 0)
            Mk1Life(in x, a, kit);
    }

    private static void TierPartLife(in Fix x, PartAt pa, Color glow)
    {
        var ci = x.Ci;
        var p = x.P(pa.U, pa.V);
        float s = x.S;
        bool edgeV = pa.V < 0.12f || pa.V > 0.88f;
        var along = edgeV ? x.U : x.V;
        var across = edgeV ? x.V : x.U;
        switch (pa.Part)
        {
            case TierPart.Strip:
            {
                if (!x.Lit) break;
                var a0 = p - along * (x.Lu * 0.3f) * (edgeV ? 1f : 0.4f);
                var a1 = p + along * (x.Lu * 0.3f) * (edgeV ? 1f : 0.4f);
                Line(ci, a0, a1, glow.WithAlpha(0.85f * x.Glow), 1.2f);
                Line(ci, a0, a1, glow.WithAlpha(0.15f * x.Glow), 4f);
                break;
            }
            case TierPart.Screen:
                if (!x.Lit) break;
                ci.DrawRect(new Rect2(p - new Vector2(4f, 2.6f) * s, new Vector2(8f, 5.2f) * s), glow.WithAlpha(0.12f + 0.08f * Pulse(x.T, 3f)));
                Line(ci, p + new Vector2(-3.4f, Mathf.Sin(x.T * 2f) * 1.6f) * s, p + new Vector2(3.4f, Mathf.Sin(x.T * 2f + 1.5f) * 1.6f) * s, glow.WithAlpha(0.8f), 0.6f);
                break;
            case TierPart.Rotor:
                Fan(ci, p, 3.4f * s, 4, x.Ang(9f), new Color("#9fb4cc"), 1f);
                break;
            case TierPart.Core:
                if (x.Lit) Dot(ci, p, (1.6f + 0.6f * Pulse(x.T, 2.4f)) * s, glow.WithAlpha(0.85f * x.Glow));
                break;
            case TierPart.Holo:
            {
                if (!x.Lit) break;
                float sx = Mathf.Cos(x.T * 0.8f);
                var top = p - new Vector2(0f, 6f * s);
                var cube = new[] { new Vector2(-3f * sx, -2f), new Vector2(3f * sx, -2f), new Vector2(3f * sx, 2f), new Vector2(-3f * sx, 2f), new Vector2(-3f * sx, -2f) };
                for (int k = 0; k < cube.Length; k++) cube[k] = top + cube[k] * s;
                ci.DrawPolyline(cube, glow.WithAlpha(0.7f * x.Glow), 0.7f, true);
                ci.DrawColoredPolygon(new[] { p, top + new Vector2(-3f, 2f) * s, top + new Vector2(3f, 2f) * s }, glow.WithAlpha(0.08f * x.Glow));
                break;
            }
            case TierPart.Field:
            {
                if (!x.Lit) break;
                float rad = Mathf.Max(x.B.Size.X, x.B.Size.Y) * 0.55f;
                float a0 = x.T * 0.9f;
                for (int k = 0; k < 3; k++) ci.DrawArc(x.C, rad, a0 + k * Mathf.Tau / 3f, a0 + k * Mathf.Tau / 3f + 1.1f, 10, glow.WithAlpha(0.45f * x.Glow), 1f, true);
                break;
            }
            case TierPart.Rail:
            {
                float q = 0.5f + 0.5f * Mathf.Sin(x.T * 0.9f * (x.On ? 1f : 0f));
                var o = p - along * (x.Lu * 0.4f) + along * (x.Lu * 0.8f * q);
                Box(ci, new Rect2(o - new Vector2(2f, 2f) * s, new Vector2(4f, 4f) * s), Steel3, 0.8f, Chrome);
                break;
            }
            case TierPart.Lamp:
            {
                if (!x.Lit) break;
                var head = p + across * (pa.V < 0.5f ? 1f : -1f) * 3f * s + along * 3f * s;
                ci.DrawColoredPolygon(new[] { head - along * 2f * s, head + along * 2f * s, head + along * 5f * s + across * 7f * s, head - along * 5f * s + across * 7f * s }, new Color(1f, 0.95f, 0.8f, 0.1f * x.Glow));
                break;
            }
            case TierPart.Antenna:
            {
                var tip = p + across * (pa.V < 0.5f ? -1f : 1f) * 7f * s + along * 2f * s;
                Led(ci, tip, glow, x.Lit && Mathf.PosMod(x.T * 0.8f, 1f) < 0.2f ? 1f : 0.1f, 0.9f);
                break;
            }
            case TierPart.Pod:
                Led(ci, p + new Vector2(-0.7f, -0.7f) * s, glow, x.Lit ? 0.4f + 0.4f * Pulse(x.T, 1.7f) : 0f, 0.8f);
                break;
        }
    }

    /// <summary>Mk.1 손질이 상태에 따라 움직인다: 고장이면 테이프 끝이 펄럭 · 점퍼선 불꽃 · 깡통에 물방울 · 선풍기가 돈다.</summary>
    private static void Mk1Life(in Fix x, Art a, TierKit kit)
    {
        var ci = x.Ci;
        var e = x.P(Mathf.Clamp(a.Eu, 0.25f, 0.75f), Mathf.Clamp(a.Ev, 0.3f, 0.7f));
        bool shaky = x.St is State.Fault or State.Running && x.Spin > 0.5f;
        switch (kit.Mk1)
        {
            case Improv.DuctTape:
            {
                if (!shaky) break;
                var tip = e + Vector2.FromAngle(0.6f) * 7f * x.S;
                float flap = Mathf.Sin(x.T * (x.St == State.Fault ? 14f : 5f)) * 2f;
                ci.DrawColoredPolygon(new[] { tip, tip + new Vector2(3f, flap), tip + new Vector2(1f, 2.5f + flap * 0.5f) }, new Color("#c8ccd2").WithAlpha(0.9f));
                break;
            }
            case Improv.JumperWire:
                if (x.St == State.Fault && Mathf.PosMod(x.T * 1.3f, 1f) < 0.1f)
                    for (int k = 0; k < 4; k++) Line(ci, x.P(0.85f, 0.8f), x.P(0.85f, 0.8f) + Vector2.FromAngle(k * 1.6f + x.T * 5f) * 4f, SparkHot, 0.8f);
                break;
            case Improv.DripCan:
            {
                var can = new Vector2(x.R.End.X - 5f, x.R.End.Y - 5f);
                float q = Mathf.PosMod(x.T * (x.St == State.Fault ? 1.6f : 0.4f), 1f);
                Dot(ci, e.Lerp(can, q), 0.8f, LeakBlue.WithAlpha(0.85f * (1f - q * 0.5f)));
                break;
            }
            case Improv.BoltedFan:
            {
                var o = x.P(0.98f, 0.2f) + x.U * 3f;
                Fan(ci, o, 3f, 3, x.Ang(12f), new Color("#c8ccd2"), 0.9f);
                break;
            }
            case Improv.HoseClamp:
                if (x.St == State.Fault) Dot(ci, e + x.U * (2f * x.S) + new Vector2(0f, Mathf.PosMod(x.T * 1.4f, 1f) * 6f), 0.8f, LeakBlue);
                break;
            case Improv.Rope:
                if (shaky) Line(ci, x.P(0.42f, 1.02f), x.P(0.42f, 1.02f) + new Vector2(Mathf.Sin(x.T * 3f) * 2f, 4f), new Color("#a8844f"), 1f);
                break;
            case Improv.TapedScreen:
                if (x.Lit && Hash(x.Id, (int)(x.T * 8f), 484) > 0.85f) ci.DrawRect(new Rect2(x.P(0.5f, 0.35f) - new Vector2(5f, 3f), new Vector2(10f, 6f)), NoiseWhite.WithAlpha(0.2f));
                break;
            case Improv.WeldBead:
                if (x.St == State.Fault) Dot(ci, x.P(0.5f, 0.52f), 3f, Ember.WithAlpha(0.2f + 0.15f * Pulse(x.T, 3f)));
                break;
            case Improv.ZipTies:
            case Improv.Shim:
                if (shaky) ci.DrawLine(x.P(0.02f, 1.02f), x.P(0.2f, 1.02f), new Color(0, 0, 0, 0.3f * Mathf.Abs(Mathf.Sin(x.T * 9f))), 1f);
                break;
            default:
                if (x.St == State.Fault) Line(ci, e + new Vector2(4f, -2f), e + new Vector2(6f + Mathf.Sin(x.T * 7f), -4f), new Color("#a8844f"), 0.8f);
                break;
        }
    }
}
