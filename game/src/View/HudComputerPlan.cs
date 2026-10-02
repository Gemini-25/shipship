using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.26 ⑨ 지휘 탭 = 지금 계획을 읽는 화면 (읽기만 — ComputerReadout이 만든 판을 그린다).
/// 위 — 현재 목표 · 수순 걸음(걸음 성격마다 다른 그림: 원격 마름모+전파 · 사람 머리와 어깨 · 시험 운전 계기 바늘 · 지켜보기 눈 · 기다림 모래시계 /
///      상태마다 다른 모양: 끝 채움+체크 · 하는 중 도는 고리와 범위 띠 · 기다림 빈 테두리 · 실패 붉은 X · 건너뜀 점선) ·
///      다음 실행 조건 · 예상과 여유(범위 띠) · 대체 계획 · 최근 수정 이유(방금 바뀌었으면 잠깐 빛남) · 견준 수순 막대.
/// 아래 — 확인된 사실(센서: 계기 바늘 · 보고: 말풍선) · 불확실한 것(마름모 물음표 · 몇 분 전 값은 흐리게) · 예측(점선 부채) ·
///      예약 · 끊긴 구역 · 자기 상태 · 돌아봄 · 성격 / 고른 설비: 왜 꺼져 있나 · 언제 켜지나 · 무엇을 기다리나.
/// </summary>
public partial class Hud
{
    private static readonly Color PlanCyan = new("#7cc4ff");
    private static readonly Color PlanAmber = new("#f2c66d");
    private static readonly Color PlanMint = new("#6ee7b7");
    private static readonly Color PlanViolet = new("#b9a3ff");

    private static Color StepColor(FixState s) => s switch
    {
        FixState.Done => Palette.Good,
        FixState.Run => PlanCyan,
        FixState.Failed => Palette.Danger,
        FixState.Skipped => Palette.TextMuted,
        _ => Palette.TextDim,
    };

    /// <summary>걸음 성격 그림 (원격 · 사람 · 시험 · 지켜보기 · 기다림) — 실루엣이 서로 다르다.</summary>
    private void DrawStepKind(Vector2 c, FixKind k, Color col, bool live)
    {
        switch (k)
        {
            case FixKind.Remote: // 마름모 + 전파 호
                DrawColoredPolygon(new[] { c + new Vector2(0, -4), c + new Vector2(4, 0), c + new Vector2(0, 4), c + new Vector2(-4, 0) }, col.WithAlpha(0.85f));
                for (int i = 1; i <= 2; i++) DrawArc(c + new Vector2(2, -2), 2.5f + 2.2f * i, -1.4f, -0.1f, 6, col.WithAlpha(live ? 0.4f + 0.4f * Mathf.Sin(_time * 6f - i) : 0.5f), 1f, true);
                break;
            case FixKind.Hands: // 머리 + 어깨 + 렌치 자루
                DrawCircle(c + new Vector2(0, -2.6f), 2f, col, true, -1f, true);
                DrawArc(c + new Vector2(0, 4.2f), 4f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 8, col, 1.6f, true);
                DrawLine(c + new Vector2(3.2f, 1.5f), c + new Vector2(5.5f, -1.5f), col.WithAlpha(0.8f), 1.2f, true);
                break;
            case FixKind.Test: // 계기 반원 + 흔들리는 바늘
                DrawArc(c + new Vector2(0, 2), 4.5f, Mathf.Pi, Mathf.Tau, 10, col, 1.2f, true);
                float a = Mathf.Pi * 1.5f + (live ? 0.6f * Mathf.Sin(_time * 4f) : 0.3f);
                DrawLine(c + new Vector2(0, 2), c + new Vector2(0, 2) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 4f, col, 1.3f, true);
                break;
            case FixKind.Watch: // 눈
                DrawArc(c + new Vector2(0, 3.5f), 5f, Mathf.Pi * 1.2f, Mathf.Pi * 1.8f, 8, col, 1.2f, true);
                DrawArc(c + new Vector2(0, -3.5f), 5f, Mathf.Pi * 0.2f, Mathf.Pi * 0.8f, 8, col, 1.2f, true);
                DrawCircle(c, live ? 1.3f + 0.4f * Mathf.Sin(_time * 3f) : 1.3f, col, true, -1f, true);
                break;
            default: // 모래시계
                DrawColoredPolygon(new[] { c + new Vector2(-3, -4.5f), c + new Vector2(3, -4.5f), c, }, col.WithAlpha(0.8f));
                DrawColoredPolygon(new[] { c, c + new Vector2(3, 4.5f), c + new Vector2(-3, 4.5f) }, col.WithAlpha(live ? 0.3f + 0.5f * Mathf.PosMod(_time * 0.5f, 1f) : 0.4f));
                break;
        }
    }

    /// <summary>걸음 상태 그림 (테두리 마디): 끝 · 하는 중 · 기다림 · 실패 · 건너뜀.</summary>
    private void DrawStepState(Vector2 c, FixState s, float frac)
    {
        var col = StepColor(s);
        switch (s)
        {
            case FixState.Done:
                DrawCircle(c, 6.5f, col.WithAlpha(0.22f), true, -1f, true);
                DrawPolyline(new[] { c + new Vector2(-3, 0), c + new Vector2(-1, 2.5f), c + new Vector2(3.5f, -2.5f) }, col, 1.6f, true);
                break;
            case FixState.Run:
                DrawArc(c, 6.5f, 0f, Mathf.Tau, 16, col.WithAlpha(0.25f), 1.2f, true);
                DrawArc(c, 6.5f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * Mathf.Clamp(frac, 0.04f, 1f), 16, col, 2f, true);
                DrawCircle(c + new Vector2(Mathf.Cos(_time * 3f), Mathf.Sin(_time * 3f)) * 6.5f, 1.3f, col, true, -1f, true);
                break;
            case FixState.Failed:
                DrawCircle(c, 6.5f, col.WithAlpha(0.18f), true, -1f, true);
                DrawLine(c + new Vector2(-3, -3), c + new Vector2(3, 3), col, 1.6f, true);
                DrawLine(c + new Vector2(3, -3), c + new Vector2(-3, 3), col, 1.6f, true);
                break;
            case FixState.Skipped:
                for (int i = 0; i < 8; i += 2) DrawArc(c, 6.5f, i * Mathf.Tau / 8f, (i + 1) * Mathf.Tau / 8f, 3, col, 1f, true);
                break;
            default:
                DrawArc(c, 6.5f, 0f, Mathf.Tau, 16, col.WithAlpha(0.6f), 1f, true);
                break;
        }
    }

    /// <summary>읽는 값의 모양: 센서(계기 바늘) · 보고(말풍선) · 예측(점선 부채) · 계산(마름모 물음표).</summary>
    private void DrawReadKind(Vector2 c, string kind, Color col)
    {
        switch (kind)
        {
            case "보고":
                Gfx.RoundRect(this, new Rect2(c - new Vector2(5, 4), new Vector2(10, 7)), col.WithAlpha(0.25f), 2, col);
                DrawColoredPolygon(new[] { c + new Vector2(-2, 3), c + new Vector2(1, 3), c + new Vector2(-3, 6) }, col);
                break;
            case "예측":
                for (int i = -2; i <= 2; i++)
                {
                    float a = -Mathf.Pi / 2f + i * 0.32f;
                    for (int j = 0; j < 3; j++) DrawLine(c + new Vector2(-4, 3) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (2 + j * 2.6f), c + new Vector2(-4, 3) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (3.2f + j * 2.6f), col.WithAlpha(0.9f - j * 0.25f), 1f, true);
                }
                break;
            case "계산":
                DrawColoredPolygon(new[] { c + new Vector2(0, -5), c + new Vector2(5, 0), c + new Vector2(0, 5), c + new Vector2(-5, 0) }, col.WithAlpha(0.2f));
                Gfx.TextCentered(this, Fonts.Bold, c + new Vector2(0, 0.5f), "?", 8, col);
                break;
            default: // 센서
                DrawArc(c + new Vector2(0, 2), 4.5f, Mathf.Pi, Mathf.Tau, 10, col, 1.1f, true);
                DrawLine(c + new Vector2(0, 2), c + new Vector2(3, -1.5f), col, 1.3f, true);
                DrawCircle(c + new Vector2(0, 2), 1f, col, true, -1f, true);
                break;
        }
    }

    /// <summary>지금 계획 판 (위).</summary>
    private void DrawPlanPanel(Rect2 r, PlanReadout rd)
    {
        var w = _world;
        Gfx.RoundRect(this, r, Glass, 8, new Color(1, 1, 1, 0.06f));
        float x = r.Position.X + 10, right = r.End.X - 10, y = r.Position.Y + 4;
        var p = rd.Plan;
        long now = w.Tick;
        // 목표
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 14), "목표", 9, Palette.TextMuted);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 30, y + 14), Fit(rd.Goal, right - x - 120, 12, Fonts.Bold), 12, p == null ? Palette.TextDim : Palette.Text);
        if (p != null) Gfx.Pill(this, Fonts.Bold, new Vector2(right - 44, y + 9), p.Name.Length > 12 ? p.Name[..12] + "…" : p.Name, 8, PlanCyan, PlanCyan.WithAlpha(0.12f));
        y += 22;
        if (p == null)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), "고장 · 보고 · 자원 부족 같은 큰 변화가 오면 수순을 견줘 계획을 세운다", 10, Palette.TextMuted);
            y += 18;
            if (rd.Reserve != "") { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), Fit("예약 — " + rd.Reserve, right - x, 10, Fonts.Body), 10, PlanAmber); y += 16; }
            var last = w.Automation.RecoveryOrNull?.Plans.LastOrDefault();
            if (last != null && y + 16 < r.End.Y) Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), Fit($"지난 계획: {last.Goal} — {last.State} ({last.Elapsed(now):0}분)", right - x, 9, Fonts.Body), 9, Palette.TextMuted);
            return;
        }
        bool fresh = now - p.Changed < SimTime.Minutes(2);
        // 걸음들 (세로 사다리)
        float rowH = Mathf.Clamp((r.End.Y - y - 96f) / Mathf.Max(1, p.Steps.Count), 15f, 22f);
        float lineX = x + 8;
        DrawLine(new Vector2(lineX, y + 6), new Vector2(lineX, y + rowH * p.Steps.Count - 4), new Color(1, 1, 1, 0.12f), 1.5f);
        for (int i = 0; i < p.Steps.Count; i++)
        {
            var s = p.Steps[i];
            float cy = y + rowH * i + rowH * 0.5f;
            bool cur = i == p.Cur && p.Open;
            float took = s.Took(now);
            float frac = s.State == FixState.Run ? took / Mathf.Max(1f, s.Max) : s.State == FixState.Done ? 1f : 0f;
            if (cur && fresh) DrawRect(new Rect2(x - 4, cy - rowH * 0.5f + 1, right - x + 8, rowH - 2), PlanCyan.WithAlpha(0.08f + 0.06f * Mathf.Sin(_time * 4f)));
            DrawStepState(new Vector2(lineX, cy), s.State, frac);
            var col = StepColor(s.State);
            DrawStepKind(new Vector2(lineX + 18, cy), s.Act.Kind, col, s.State == FixState.Run);
            string range = s.Max - s.Min < 1.5f ? $"{s.Min:0}분" : $"{s.Min:0}~{s.Max:0}분";
            Gfx.Text(this, cur ? Fonts.Bold : Fonts.Body, new Vector2(lineX + 28, cy + 4), Fit(s.Name, (right - x) * 0.3f, 10, Fonts.Body), 10, s.State == FixState.Wait && !cur ? Palette.TextDim : Palette.Text);
            float bx = lineX + 28 + (right - x) * 0.31f, bw = (right - x) * 0.22f;
            // 범위 띠: 최소~최대 (회색) · 걸린 시간 (막대)
            float scale = bw / Mathf.Max(1f, s.Max * 1.15f);
            DrawRect(new Rect2(bx, cy - 3, bw, 6), new Color(1, 1, 1, 0.04f));
            DrawRect(new Rect2(bx + s.Min * scale, cy - 3, Mathf.Max(1f, (s.Max - s.Min) * scale), 6), col.WithAlpha(0.18f));
            if (took > 0f) DrawRect(new Rect2(bx, cy - 1.5f, Mathf.Min(bw, took * scale), 3), col.WithAlpha(0.85f));
            string tail = s.State == FixState.Wait && s.Waiting != "" ? s.Waiting : s.Note != "" ? s.Note : s.Waiting;
            Gfx.Text(this, Fonts.Body, new Vector2(bx + bw + 6, cy + 4), Fit($"{range}" + (tail != "" ? " · " + tail : ""), right - bx - bw - 6, 9, Fonts.Body), 9, s.State == FixState.Failed ? Palette.Danger : Palette.TextMuted);
        }
        y += rowH * p.Steps.Count + 4;
        // 다음 조건 · 예상과 여유 · 대체 · 수정
        void Row(string label, string text, Color col, bool glow = false)
        {
            if (y + 14 > r.End.Y - 2 || text == "") return;
            if (glow) DrawRect(new Rect2(x - 4, y + 1, right - x + 8, 14), col.WithAlpha(0.1f + 0.08f * Mathf.Sin(_time * 5f)));
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 12), label, 9, Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 62, y + 12), Fit(text, right - x - 62, 10, Fonts.Body), 10, col);
            y += 15;
        }
        Row("다음 조건", rd.Next, Palette.Text);
        Row("예상 · 여유", rd.Expect, PlanCyan);
        Row("대체 계획", rd.Fallback, PlanViolet);
        if (rd.Revised != "") Row("최근 수정", $"{rd.Revised} ({(now - rd.RevisedAt) / (float)SimTime.Minutes(1):0}분 전)", PlanAmber, now - rd.RevisedAt < SimTime.Minutes(3));
        // 견준 수순 (막대)
        if (y + 16 < r.End.Y)
        {
            float max = 0.01f;
            foreach (var o in p.Compared) if (o.Allowed) max = Mathf.Max(max, o.Score);
            foreach (var o in p.Compared)
            {
                if (y + 13 > r.End.Y - 2) break;
                bool pick = o.Key == p.OptionKey;
                var oc = !o.Allowed ? Palette.TextMuted : pick ? PlanCyan : Palette.TextDim;
                if (pick) DrawColoredPolygon(new[] { new Vector2(x, y + 4), new Vector2(x + 6, y + 7.5f), new Vector2(x, y + 11) }, PlanCyan);
                Gfx.Text(this, pick ? Fonts.Bold : Fonts.Body, new Vector2(x + 10, y + 11), Fit(o.Name, (right - x) * 0.32f, 9, Fonts.Body), 9, oc);
                float bx = x + 12 + (right - x) * 0.32f, bw = (right - x) * 0.2f;
                DrawRect(new Rect2(bx, y + 4, bw, 6), new Color(1, 1, 1, 0.04f));
                if (o.Allowed) DrawRect(new Rect2(bx, y + 4, bw * Mathf.Clamp(o.Score / max, 0.03f, 1f), 6), oc.WithAlpha(pick ? 0.8f : 0.4f));
                else for (int j = 0; j < 6; j++) DrawLine(new Vector2(bx + j * bw / 6f, y + 10), new Vector2(bx + j * bw / 6f + 4, y + 4), Palette.TextMuted.WithAlpha(0.5f), 1f);
                string tail = !o.Allowed ? o.Blocked : (o.PeakMax > 0f ? $"노심 최고 {o.PeakMax:0}℃ · " : "") + (o.BatteryKwh > 0.5f ? $"배터리 {o.BatteryKwh:0}kWh · " : "") + $"{o.Min:0}~{o.Max:0}분";
                Gfx.Text(this, Fonts.Body, new Vector2(bx + bw + 6, y + 11), Fit(tail, right - bx - bw - 6, 9, Fonts.Body), 9, oc);
                y += 13;
            }
        }
    }

    /// <summary>읽는 값 판 (아래): 사실 · 불확실 · 예측 · 예약 · 구역 · 자기 상태 · 고른 설비.</summary>
    private void DrawReadings(Rect2 r, PlanReadout rd)
    {
        var w = _world;
        Gfx.RoundRect(this, r, Glass, 8, new Color(1, 1, 1, 0.05f));
        float x = r.Position.X + 10, right = r.End.X - 10, y = r.Position.Y + 2;
        void Line(ReadLine l, Color col)
        {
            if (y + 14 > r.End.Y - 2) return;
            float fade = l.Age < 0f ? 1f : Mathf.Clamp(1.1f - l.Age / 30f, 0.45f, 1f); // 오래된 값은 흐리게
            DrawReadKind(new Vector2(x + 6, y + 8), l.Kind, col.WithAlpha(fade));
            string age = l.Age < 0.5f ? "" : $" ({l.Age:0}분 전)";
            Gfx.Text(this, Fonts.Body, new Vector2(x + 16, y + 12), Fit(l.Text + age, right - x - 16, 10, Fonts.Body), 10, (l.Age > 10f ? Palette.Warning : Palette.Text).WithAlpha(fade));
            y += 15;
        }
        void Head(string t)
        {
            if (y + 14 > r.End.Y - 2) return;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 12), t, 9, Palette.TextMuted);
            y += 14;
        }
        if (rd.Facts.Count > 0) { Head("확인된 사실"); foreach (var l in rd.Facts.Take(5)) Line(l, l.Kind == "보고" ? PlanAmber : PlanMint); }
        if (rd.Unknowns.Count > 0) { Head("불확실한 것"); foreach (var l in rd.Unknowns.Take(4)) Line(l, PlanViolet); }
        if (rd.Forecasts.Count > 0) { Head("예측"); foreach (var l in rd.Forecasts.Take(3)) Line(l, PlanCyan); }
        foreach (var (label, text, col) in new[] { ("예약", rd.Reserve, PlanAmber), ("구역", rd.Zones, PlanMint), ("자기 상태", rd.Self, Palette.Warning), ("돌아봄", rd.Review, Palette.TextDim), ("성격", rd.Manner, Palette.TextDim) })
        {
            if (text == "" || y + 14 > r.End.Y - 2) continue;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 12), label, 9, Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 54, y + 12), Fit(text, right - x - 54, 9, Fonts.Body), 9, col);
            y += 14;
        }
        // 고른 설비: 왜 꺼져 있나 / 언제 켜지나 / 무엇을 기다리나
        if (_main.SelectedFurniture is Furniture f && f.Machine != null && y + 48 <= r.End.Y)
        {
            var (why, when, wait) = ComputerReadout.WhyOff(w, f);
            var box = new Rect2(x - 4, y + 2, right - x + 8, 46);
            Gfx.RoundRect(this, box, new Color(0.1f, 0.14f, 0.2f, 0.85f), 6, PlanCyan.WithAlpha(0.4f));
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 14), Fit(f.Name, 90, 10, Fonts.Bold), 10, PlanCyan);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 96, y + 14), Fit("왜: " + why, right - x - 96, 9, Fonts.Body), 9, Palette.Text);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 96, y + 27), Fit("언제: " + when, right - x - 96, 9, Fonts.Body), 9, Palette.TextDim);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 96, y + 40), Fit("기다림: " + wait, right - x - 96, 9, Fonts.Body), 9, Palette.TextDim);
        }
    }

    /// <summary>믿는 배 지도 위: 끊겨 혼자 버티는 구역(섬 — 제 불빛이 깜빡이는 상자와 끊긴 선) · 계획이 걸린 방(걸음 구슬 고리).</summary>
    private void DrawPlanMarks(Dictionary<int, Vector2> centers)
    {
        var a = _world.Automation;
        foreach (var z in a.ZonesOrNull?.Alone ?? Enumerable.Empty<ZoneState>())
        {
            if (!centers.TryGetValue(z.RoomId, out var c)) continue;
            var col = PlanMint;
            Gfx.RoundRect(this, new Rect2(c + new Vector2(-6, -12), new Vector2(12, 8)), col.WithAlpha(0.18f), 2, col);
            DrawCircle(c + new Vector2(3, -8), 1.3f, col.WithAlpha(0.4f + 0.6f * Mathf.PosMod(_time * 1.5f + z.RoomId * 0.3f, 1f)), true, -1f, true);
            DrawLine(c + new Vector2(-10, -8), c + new Vector2(-7, -8), col.WithAlpha(0.7f), 1f);
            DrawLine(c + new Vector2(-13, -6), c + new Vector2(-11, -10), Palette.Danger.WithAlpha(0.8f), 1.2f);
        }
        if (a.RecoveryOrNull is not FixBook book) return;
        foreach (var p in book.Plans)
        {
            if (!p.Open || !centers.TryGetValue(p.RoomId, out var c)) continue;
            int n = Math.Max(1, p.Steps.Count);
            for (int i = 0; i < n; i++)
            {
                float ang = -Mathf.Pi / 2f + Mathf.Tau * i / n;
                var at = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 9f;
                var s = p.Steps[i];
                DrawCircle(at, i == p.Cur ? 1.9f + 0.5f * Mathf.Sin(_time * 5f) : 1.4f, StepColor(s.State).WithAlpha(s.State == FixState.Wait ? 0.5f : 0.95f), true, -1f, true);
            }
        }
    }
}
