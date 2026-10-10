using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.18 승무원이 여는 회의의 그림:
// 바닥 — 둘러앉는 방석 · 의장 탁자와 종 · 재판석 난간 · 증언대 · 투표함 · 잔치 깃발과 냄비.
// 사람 위 — 파벌 완장(파벌마다 색과 무늬가 다르다) · 서명 종이 · 손 들기 · 투표용지 · 말풍선(역할마다 테두리) · 불만 구름 · 본 사람의 눈 · 벌 근무 양동이.
public partial class ShipView
{
    private static readonly Color[] FactionHues =
    {
        new(0.98f, 0.72f, 0.25f), new(0.25f, 0.8f, 0.76f), new(0.9f, 0.38f, 0.72f), new(0.6f, 0.85f, 0.3f),
        new(0.4f, 0.65f, 1f), new(1f, 0.45f, 0.38f), new(0.66f, 0.5f, 0.95f), new(0.85f, 0.78f, 0.6f),
    };

    public static Color FactionColor(Faction f) => FactionHues[f.Hue % FactionHues.Length];

    /// <summary>파벌 무늬 (Id 마다 다른 모양): 고리 · 세모 · 마름모 · 네모 · 별 · 초승달 · 십자 · 물결.</summary>
    public static void FactionGlyph(CanvasItem ci, Faction f, Vector2 c, float s, Color col)
    {
        switch (f.Id % 8)
        {
            case 0: ci.Arc(c, s * 0.8f, 0f, Mathf.Tau, 14, col, 1.6f, true); break;
            case 1: ci.Poly(new[] { c + new Vector2(0, -s), c + new Vector2(s * 0.9f, s * 0.7f), c + new Vector2(-s * 0.9f, s * 0.7f) }, col); break;
            case 2: ci.Poly(new[] { c + new Vector2(0, -s), c + new Vector2(s * 0.75f, 0), c + new Vector2(0, s), c + new Vector2(-s * 0.75f, 0) }, col); break;
            case 3: ci.Box(new Rect2(c - new Vector2(s * 0.7f, s * 0.7f), s * 1.4f, s * 1.4f), col); break;
            case 4:
            {
                var pts = new Vector2[10];
                for (int i = 0; i < 10; i++) { float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5; float r = i % 2 == 0 ? s : s * 0.42f; pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
                ci.Poly(pts, col);
                break;
            }
            case 5:
                ci.Circle(c, s * 0.85f, col);
                ci.Circle(c + new Vector2(s * 0.38f, -s * 0.2f), s * 0.7f, new Color(0.08f, 0.09f, 0.12f, 0.95f));
                break;
            case 6:
                ci.Box(new Rect2(c.X - s * 0.25f, c.Y - s, s * 0.5f, s * 2f), col);
                ci.Box(new Rect2(c.X - s, c.Y - s * 0.25f, s * 2f, s * 0.5f), col);
                break;
            default:
                ci.Polyline(new[] { c + new Vector2(-s, 0), c + new Vector2(-s * 0.5f, -s * 0.6f), c + new Vector2(0, 0), c + new Vector2(s * 0.5f, -s * 0.6f), c + new Vector2(s, 0) }, col, 1.6f, true);
                break;
        }
    }

    private static Vector2[] Ellipse(Vector2 c, float rx, float ry, int n = 14)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = i * Mathf.Tau / n; p[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry); }
        return p;
    }

    private Vector2 VenueTop(Room v) => ToPx(v.Center) + new Vector2(0, -T * (v.Cells.Max(c => c.Y) - v.Cells.Min(c => c.Y) + 1) * 0.5f);

    // ───────────────────────────── 바닥 (사람 밑) ─────────────────────────────

    private void PaintCouncilFloor(CanvasItem ci)
    {
        var w = _world;
        var mo = w.Motions;
        PaintFeastDecor(ci);
        if (mo.Now is not Sitting s) return;
        var v = s.Venue;
        bool trial = s.Kind == SittingKind.Trial;
        // 둘러앉는 방석 (의장은 붉은 방석에 금테)
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room != v || c.Job?.Activity is not SittingActivity) continue;
            var p = CrewPx(c);
            float r = CrewRadius;
            bool chair = c.Id == s.Chair || s.Chair < 0 && c.Id == w.Command.CaptainId;
            if (trial && c.Id == s.Motion.Target)
            {
                // 재판석: 세 면을 두른 낮은 난간 (기둥 넷 · 가로대)
                float hw = r * 1.5f, top = p.Y - r * 1.1f, bot = p.Y + r * 1.2f;
                var rail = new Color(0.42f, 0.32f, 0.2f, 0.95f);
                ci.Polyline(new[] { new Vector2(p.X - hw, top), new Vector2(p.X - hw, bot), new Vector2(p.X + hw, bot), new Vector2(p.X + hw, top) }, rail, 3f, true);
                ci.Polyline(new[] { new Vector2(p.X - hw, top + 1.5f), new Vector2(p.X - hw, bot - 1.5f), new Vector2(p.X + hw, bot - 1.5f), new Vector2(p.X + hw, top + 1.5f) }, new Color(0.75f, 0.6f, 0.38f, 0.6f), 1f, true);
                foreach (var q in new[] { new Vector2(p.X - hw, top), new Vector2(p.X - hw, bot), new Vector2(p.X + hw, bot), new Vector2(p.X + hw, top) })
                    ci.Circle(q, 2.4f, new Color(0.3f, 0.22f, 0.13f));
                continue;
            }
            var seatCol = chair ? new Color(0.55f, 0.14f, 0.16f, 0.9f) : new Color(0.22f, 0.27f, 0.34f, 0.85f);
            ci.Poly(Ellipse(p + new Vector2(0, r * 0.45f), r * 1.05f, r * 0.55f), new Color(0, 0, 0, 0.25f));
            ci.Poly(Ellipse(p + new Vector2(0, r * 0.35f), r * 0.95f, r * 0.5f), seatCol);
            ci.Polyline(Ellipse(p + new Vector2(0, r * 0.35f), r * 0.95f, r * 0.5f).Append(p + new Vector2(r * 0.95f, r * 0.35f)).ToArray(),
                chair ? new Color(0.95f, 0.78f, 0.3f, 0.9f) : new Color(0.55f, 0.6f, 0.68f, 0.6f), 1f, true);
            if (chair)
            {
                // 의장 탁자와 종
                var toward = (ToPx(v.Center) - p);
                var d = toward.Length() > 1f ? toward.Normalized() : new Vector2(0, 1);
                var tp = p + d * r * 1.5f;
                ci.Box(new Rect2(tp - new Vector2(r * 0.9f, r * 0.35f), r * 1.8f, r * 0.7f), new Color(0.4f, 0.27f, 0.15f));
                for (int i = 0; i < 3; i++) ci.DrawLine(tp + new Vector2(-r * 0.8f, -r * 0.2f + i * r * 0.2f), tp + new Vector2(r * 0.8f, -r * 0.2f + i * r * 0.2f), new Color(0.55f, 0.38f, 0.22f, 0.5f), 1f);
                ci.Circle(tp + new Vector2(r * 0.45f, -r * 0.05f), r * 0.22f, new Color(0.95f, 0.78f, 0.3f));
                ci.DrawLine(tp + new Vector2(r * 0.45f, -r * 0.27f), tp + new Vector2(r * 0.45f, -r * 0.45f), new Color(0.3f, 0.22f, 0.12f), 1.5f);
            }
        }
        // 투표함 (비밀 투표) — 가운데 낮은 탁자 위
        if (s.Motion.Secret && s.Opened >= 0)
        {
            var b = ToPx(v.Center);
            ci.Box(new Rect2(b - new Vector2(9, 6), 18, 13), new Color(0.18f, 0.2f, 0.24f));
            ci.Box(new Rect2(b - new Vector2(9, 6), 18, 3), new Color(0.32f, 0.35f, 0.4f));
            ci.DrawLine(b + new Vector2(-4, -5), b + new Vector2(4, -5), new Color(0.02f, 0.02f, 0.03f), 1.6f);
            ci.Box(new Rect2(b + new Vector2(-9, 1), 18, 3), new Color(0.85f, 0.85f, 0.8f, 0.8f));
            int dropped = (int)(s.Hands.Count * Mathf.Clamp(mo.Voting * 1.3f, 0f, 1f));
            for (int i = 0; i < Mathf.Min(dropped, 8); i++) ci.Box(new Rect2(b + new Vector2(-6 + i * 1.5f, -9 - i * 0.6f), 5, 2), new Color(0.95f, 0.93f, 0.85f, 0.9f));
        }
    }

    private void PaintFeastDecor(CanvasItem ci)
    {
        var mo = _world.Motions;
        if (!mo.Feasting || mo.FeastRoom is not Room r) return;
        // 깃발 줄: 방 윗벽을 따라 늘어진 세모 깃발 (바람에 살랑)
        var top = VenueTop(r) + new Vector2(0, 6f);
        float half = T * (r.Cells.Max(c => c.X) - r.Cells.Min(c => c.X) + 1) * 0.5f - 6f;
        var flagCols = new[] { new Color(0.95f, 0.4f, 0.35f), new Color(0.98f, 0.8f, 0.3f), new Color(0.35f, 0.75f, 0.95f), new Color(0.55f, 0.85f, 0.45f) };
        int n = Mathf.Max(4, (int)(half * 2f / 14f));
        Vector2 Rope(float t) => new(top.X - half + t * half * 2f, top.Y + Mathf.Sin(t * Mathf.Pi) * 7f);
        var rope = Enumerable.Range(0, 17).Select(i => Rope(i / 16f)).ToArray();
        ci.Polyline(rope, new Color(0.8f, 0.75f, 0.65f, 0.8f), 1f, true);
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            var a = Rope(t);
            float sway = Mathf.Sin(_time * 2.2f + i) * 1.5f;
            ci.Poly(new[] { a + new Vector2(-4, 0), a + new Vector2(4, 0), a + new Vector2(sway, 9) }, flagCols[i % flagCols.Length]);
        }
        // 가운데 큰 냄비와 촛불
        var c0 = ToPx(r.Center);
        ci.Poly(Ellipse(c0, 11, 7), new Color(0.35f, 0.36f, 0.4f));
        ci.Poly(Ellipse(c0 + new Vector2(0, -1), 9, 5), new Color(0.75f, 0.45f, 0.25f));
        for (int i = 0; i < 3; i++)
        {
            float a = _time * 1.3f + i * 2f;
            ci.Circle(c0 + new Vector2(Mathf.Sin(a) * 4f - 3 + i * 3, -8 - (a * 3f % 8f)), 1.5f, new Color(1, 1, 1, 0.25f)); // 김
        }
        foreach (var dx in new[] { -16f, 16f })
        {
            ci.Box(new Rect2(c0 + new Vector2(dx - 1.5f, -4), 3, 8), new Color(0.95f, 0.92f, 0.85f));
            float fl = 0.8f + 0.2f * Mathf.Sin(_time * 9f + dx);
            ci.Circle(c0 + new Vector2(dx, -6), 2f * fl, new Color(1f, 0.75f, 0.3f, 0.9f));
        }
    }

    // ───────────────────────────── 사람 위 ─────────────────────────────

    private void PaintCouncilOver(CanvasItem ci)
    {
        var w = _world;
        var mo = w.Motions;
        bool fine = Zoom > 0.75f;
        // 파벌 완장 (최근 이틀 안에 같이 선 파벌만)
        foreach (var f in mo.Factions)
        {
            if (f.Gone || w.Tick - f.Last > SimTime.TicksPerDay * 2) continue;
            var col = FactionColor(f);
            int slot = 0;
            foreach (int id in f.Members)
            {
                var c = id >= 0 && id < w.Crew.Count ? w.Crew[id] : null;
                if (c == null || c.Dead || c.CarriedBy != null) continue;
                var p = CrewPx(c);
                float r = CrewRadius + 2.5f;
                slot = mo.Factions.Count(o => !o.Gone && o.Id < f.Id && o.Members.Contains(id) && w.Tick - o.Last <= SimTime.TicksPerDay * 2);
                float a0 = Mathf.Pi * (1.05f + 0.22f * slot);
                ci.Arc(p, r, a0, a0 + 0.45f, 6, col.WithAlpha(0.95f), 3f, true);
                if (fine) FactionGlyph(ci, f, p + new Vector2(Mathf.Cos(a0 + 0.22f), Mathf.Sin(a0 + 0.22f)) * (r + 4.5f), 2.6f, col);
            }
        }
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.CarriedBy != null || c.Down) continue;
            var p = CrewPx(c);
            float r = CrewRadius;
            // 서명 종이 (클립보드)
            if (c.Job?.Activity is PetitionActivity)
            {
                var m = mo.All.FirstOrDefault(x => x.Stage == MotionStage.Signing && x.Carriers.Contains(c.Id));
                var b = p + new Vector2(r * 0.85f, r * 0.1f);
                ci.Box(new Rect2(b, 8, 10), new Color(0.93f, 0.91f, 0.84f));
                ci.Box(new Rect2(b + new Vector2(2.5f, -1.5f), 3, 2.5f), new Color(0.25f, 0.25f, 0.28f));
                for (int i = 0; i < 3; i++) ci.DrawLine(b + new Vector2(1.5f, 3 + i * 2.2f), b + new Vector2(6.5f, 3 + i * 2.2f), new Color(0.5f, 0.5f, 0.55f, 0.7f), 0.8f);
                if (m != null)
                {
                    int sg = Math.Min(m.Signers.Count, 6);
                    for (int i = 0; i < sg; i++) ci.DrawLine(b + new Vector2(1 + i * 1.2f, 9.2f), b + new Vector2(2 + i * 1.2f, 8.2f), new Color(0.15f, 0.3f, 0.7f), 0.9f);
                    if (fine) Gfx.Text(ci, Fonts.Bold, b + new Vector2(10, 9), $"{m.Signers.Count}/{m.Need}", 9, new Color(0.95f, 0.93f, 0.85f, 0.9f));
                }
            }
            // 벌 근무: 양동이와 걸레
            if (c.Job?.Activity is PenaltyDutyActivity && fine)
            {
                var b = p + new Vector2(-r * 1.3f, r * 0.6f);
                ci.Poly(new[] { b + new Vector2(-4, -4), b + new Vector2(4, -4), b + new Vector2(3, 4), b + new Vector2(-3, 4) }, new Color(0.45f, 0.5f, 0.55f));
                ci.Poly(Ellipse(b + new Vector2(0, -4), 4, 1.3f, 8), new Color(0.35f, 0.6f, 0.85f, 0.9f));
                float sw = Mathf.Sin(_time * 4f) * 3f;
                ci.DrawLine(p + new Vector2(r * 0.3f, 0), p + new Vector2(r * 0.9f + sw, r * 1.4f), new Color(0.6f, 0.45f, 0.28f), 1.6f);
                ci.DrawLine(p + new Vector2(r * 0.6f + sw, r * 1.45f), p + new Vector2(r * 1.25f + sw, r * 1.35f), new Color(0.85f, 0.85f, 0.78f), 2.4f);
            }
            if (!fine || mo.Now != null && mo.Now.Present.Contains(c.Id)) continue;
            // 불만 구름 (진 쪽 · 원망)
            if (mo.GrudgeOf(c) is Grudge g)
            {
                float drift = Mathf.Sin(_time * 0.8f + c.Id) * 2f;
                var cc = p + new Vector2(-r * 0.9f + drift, -r * 1.9f);
                var gray = new Color(0.45f, 0.47f, 0.52f, 0.8f);
                ci.Circle(cc, 3.2f, gray); ci.Circle(cc + new Vector2(3.5f, -1), 3.8f, gray); ci.Circle(cc + new Vector2(7, 0.5f), 3f, gray);
                ci.Polyline(new[] { cc + new Vector2(1, 0), cc + new Vector2(2.5f, -1.5f), cc + new Vector2(4, 0.5f), cc + new Vector2(5.5f, -1.5f), cc + new Vector2(7, 0) }, new Color(0.15f, 0.15f, 0.18f, 0.85f), 0.9f);
            }
            // 본 사람의 눈 (몰래 꺼내 먹는 걸 봤다)
            if (mo.SawTheftLately(c))
            {
                var e = p + new Vector2(r * 1.1f, -r * 1.5f);
                ci.Poly(new[] { e + new Vector2(-5, 0), e + new Vector2(-2, -2.6f), e + new Vector2(2, -2.6f), e + new Vector2(5, 0), e + new Vector2(2, 2.6f), e + new Vector2(-2, 2.6f) }, new Color(0.95f, 0.95f, 0.92f, 0.92f));
                ci.Circle(e, 1.7f, new Color(0.15f, 0.2f, 0.3f));
            }
        }
        PaintSittingOver(ci);
    }

    private void PaintSittingOver(CanvasItem ci)
    {
        var w = _world;
        var mo = w.Motions;
        if (mo.Now is not Sitting s) return;
        var v = s.Venue;
        // 머리 표지 (회의 종류마다 다른 표시)
        var top = VenueTop(v) - new Vector2(0, 8f);
        string head = s.Opened < 0 ? $"{MotionSystem.SittingName(s.Kind)} — 모이는 중 {s.Present.Count}/{s.Invited.Count}"
            : mo.Voting > 0f ? $"{MotionSystem.SittingName(s.Kind)} — {(s.Motion.Secret ? "투표함에 넣는다" : "손을 든다")}"
            : $"{MotionSystem.SittingName(s.Kind)} — {s.Motion.Title}";
        if (head.Length > 34) head = head[..33] + "…";
        var (fill, edge) = s.Kind switch
        {
            SittingKind.Trial => (new Color(0.16f, 0.1f, 0.08f, 0.9f), new Color(0.85f, 0.6f, 0.35f)),
            SittingKind.Election => (new Color(0.08f, 0.1f, 0.18f, 0.9f), new Color(0.6f, 0.75f, 1f)),
            SittingKind.Emergency => (new Color(0.2f, 0.06f, 0.06f, 0.9f), new Color(1f, 0.4f, 0.35f, 0.6f + 0.4f * Mathf.Sin(_time * 4f))),
            SittingKind.Feast => (new Color(0.18f, 0.12f, 0.05f, 0.9f), new Color(1f, 0.82f, 0.4f)),
            SittingKind.Inquiry => (new Color(0.08f, 0.12f, 0.12f, 0.9f), new Color(0.5f, 0.9f, 0.85f)),
            _ => (new Color(0.2f, 0.16f, 0.06f, 0.85f), new Color(1f, 0.85f, 0.45f)),
        };
        float hw = Gfx.Width(Fonts.Bold, head, 12) + 30;
        var plate = new Rect2(top - new Vector2(hw / 2, 12), hw, 20);
        Gfx.RoundRect(ci, plate, fill, 6, edge, 1);
        var ic = plate.Position + new Vector2(11, 10);
        switch (s.Kind)
        {
            case SittingKind.Trial: // 저울
                ci.DrawLine(ic + new Vector2(0, -5), ic + new Vector2(0, 5), edge, 1.2f);
                ci.DrawLine(ic + new Vector2(-5, -3), ic + new Vector2(5, -3), edge, 1.2f);
                ci.Arc(ic + new Vector2(-4, 0), 2.2f, 0, Mathf.Pi, 6, edge, 1f);
                ci.Arc(ic + new Vector2(4, 0), 2.2f, 0, Mathf.Pi, 6, edge, 1f);
                break;
            case SittingKind.Election: // 투표용지
                ci.Box(new Rect2(ic - new Vector2(4, 5), 8, 10), edge, false, 1f);
                ci.Polyline(new[] { ic + new Vector2(-2, 0), ic + new Vector2(-0.5f, 2), ic + new Vector2(2.5f, -2.5f) }, edge, 1.2f);
                break;
            case SittingKind.Feast: // 잔
                ci.Poly(new[] { ic + new Vector2(-4, -4), ic + new Vector2(4, -4), ic + new Vector2(0, 1) }, edge);
                ci.DrawLine(ic + new Vector2(0, 1), ic + new Vector2(0, 5), edge, 1.2f);
                break;
            case SittingKind.Inquiry: // 돋보기
                ci.Arc(ic + new Vector2(-1, -1), 3.5f, 0, Mathf.Tau, 10, edge, 1.2f);
                ci.DrawLine(ic + new Vector2(1.5f, 1.5f), ic + new Vector2(4.5f, 4.5f), edge, 1.6f);
                break;
            case SittingKind.Emergency: // 느낌표
                ci.DrawLine(ic + new Vector2(0, -5), ic + new Vector2(0, 1.5f), edge, 2f);
                ci.Circle(ic + new Vector2(0, 4), 1.1f, edge);
                break;
            default: ci.Circle(ic, 3.5f, edge); break;
        }
        Gfx.Text(ci, Fonts.Bold, plate.Position + new Vector2(22, 14), head, 12, new Color(0.97f, 0.95f, 0.9f));
        if (s.Opened < 0) return;
        // 손 들기 · 투표용지
        float vote = mo.Voting;
        if (vote > 0f)
        {
            var box = ToPx(v.Center);
            int k = 0;
            foreach (int id in s.Hands)
            {
                if (id < 0 || id >= w.Crew.Count || w.Crew[id] is not CrewMember c || c.Dead) continue;
                var p = CrewPx(c);
                float r = CrewRadius;
                float t = Mathf.Clamp(vote * 1.4f - k * 0.06f, 0f, 1f);
                k++;
                if (s.Motion.Secret)
                {
                    if (t <= 0f || t >= 1f) continue;
                    var at = p.Lerp(box, t) + new Vector2(0, -Mathf.Sin(t * Mathf.Pi) * 10f);
                    ci.Box(new Rect2(at - new Vector2(3, 2), 6, 4), new Color(0.96f, 0.94f, 0.86f));
                    ci.DrawLine(at + new Vector2(-2, -0.5f), at + new Vector2(2, -0.5f), new Color(0.4f, 0.4f, 0.45f), 0.6f);
                }
                else
                {
                    var sh = p + new Vector2(r * 0.55f, -r * 0.25f);
                    var hand = sh + new Vector2(r * 0.25f, -r * (0.4f + 1.25f * t));
                    ci.DrawLine(sh, hand, new Color(0.3f, 0.32f, 0.38f), 2.6f);
                    ci.Circle(hand, 2.6f, new Color(0.96f, 0.8f, 0.66f));
                }
            }
            return;
        }
        // 말풍선 (역할마다 테두리 · 컴퓨터는 벽 스피커에서)
        if (mo.Speaking() is not var (line, _)) return;
        Vector2 at2;
        if (line.Who < 0)
        {
            var spk = VenueTop(v) + new Vector2(-T * 1.2f, 14f);
            ci.Circle(spk, 6f, new Color(0.1f, 0.16f, 0.2f));
            for (int i = 1; i <= 3; i++) ci.Arc(spk, i * 1.8f, 0, Mathf.Tau, 10, new Color(0.4f, 0.85f, 1f, 0.75f), 0.8f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 6f);
            ci.Arc(spk, 8.5f + pulse * 2f, -0.6f, 0.6f, 6, new Color(0.4f, 0.85f, 1f, 0.6f), 1f);
            at2 = spk + new Vector2(0, -8f);
        }
        else
        {
            if (line.Who >= w.Crew.Count) return;
            var who = w.Crew[line.Who];
            at2 = CrewPx(who) + new Vector2(0, -CrewRadius - 16f);
            var p = CrewPx(who);
            float r = CrewRadius;
            if (line.Role == LineRole.Witness)
            {
                // 증언대: 앞에 작은 단상, 오른손을 든다
                ci.Box(new Rect2(p + new Vector2(-r * 0.7f, r * 0.9f), r * 1.4f, r * 0.55f), new Color(0.38f, 0.26f, 0.15f));
                ci.DrawLine(p + new Vector2(r * 0.5f, -r * 0.2f), p + new Vector2(r * 0.8f, -r * 1.5f), new Color(0.3f, 0.32f, 0.38f), 2.4f);
                ci.Circle(p + new Vector2(r * 0.8f, -r * 1.5f), 2.5f, new Color(0.96f, 0.8f, 0.66f));
            }
            if (line.Role == LineRole.Candidate) ci.Arc(p, r + 5f, 0, Mathf.Tau, 20, new Color(0.6f, 0.75f, 1f, 0.5f + 0.3f * Mathf.Sin(_time * 3f)), 1.5f);
        }
        string text = line.Text.Length > 30 ? line.Text[..29] + "…" : line.Text;
        float bw = Gfx.Width(Fonts.Body, text, 12) + 16;
        var rect = new Rect2(at2.X - bw / 2, at2.Y - 22, bw, 22);
        var border = line.Role switch
        {
            LineRole.Computer => new Color(0.4f, 0.85f, 1f, 0.95f),
            LineRole.Witness => new Color(1f, 0.82f, 0.4f, 0.95f),
            LineRole.Accuser => new Color(1f, 0.55f, 0.3f, 0.95f),
            LineRole.Defense => new Color(0.7f, 0.72f, 0.85f, 0.9f),
            LineRole.Candidate => new Color(0.6f, 0.75f, 1f, 0.95f),
            LineRole.Hearsay => new Color(0.6f, 0.6f, 0.6f, 0.7f),
            LineRole.Chair => new Color(0.95f, 0.78f, 0.3f, 0.9f),
            _ => line.Pro ? new Color(0.5f, 1f, 0.6f, 0.9f) : new Color(1f, 0.45f, 0.4f, 0.9f),
        };
        var bg = line.Role == LineRole.Computer ? new Color(0.05f, 0.12f, 0.16f, 0.94f) : new Color(0.08f, 0.09f, 0.12f, 0.92f);
        Gfx.RoundRect(ci, rect, bg, 8, border, line.Role == LineRole.Witness ? 2 : 1);
        ci.Poly(new[] { new Vector2(at2.X - 5, at2.Y), new Vector2(at2.X + 5, at2.Y), new Vector2(at2.X, at2.Y + 7) }, bg);
        Gfx.Text(ci, line.Role == LineRole.Hearsay ? Fonts.Body : Fonts.Body, new Vector2(rect.Position.X + 8, rect.Position.Y + 16), text, 12,
            line.Role == LineRole.Hearsay ? new Color(0.8f, 0.8f, 0.82f) : new Color(0.95f, 0.96f, 1f));
    }
}
