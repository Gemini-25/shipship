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

    // ───────────── 바뀐 부분만 짧게 강조 (화면 쪽 기억 — 시뮬레이션은 건드리지 않는다) ─────────────

    private readonly Dictionary<string, (string val, float at)> _planSeen = new();

    /// <summary>이 칸의 값이 방금 바뀌었나: 1(막 바뀜) → 0(3초 뒤). 처음 보는 값은 빛나지 않는다 (탭을 열 때 전부 번쩍이지 않게).</summary>
    private float Fresh(string key, string val)
    {
        if (!_planSeen.TryGetValue(key, out var e)) { if (_planSeen.Count > 400) _planSeen.Clear(); _planSeen[key] = (val, -99f); return 0f; }
        if (e.val != val) { _planSeen[key] = (val, _time); return 1f; }
        return Mathf.Clamp(1f - (_time - e.at) / 3f, 0f, 1f);
    }

    /// <summary>바뀐 줄 뒤에 짧게 번지는 띠 (왼쪽 마디 + 옅은 바탕).</summary>
    private void FreshBand(Rect2 row, float f, Color col)
    {
        if (f <= 0.01f) return;
        DrawRect(row, col.WithAlpha(0.14f * f));
        DrawRect(new Rect2(row.Position, new Vector2(2.5f, row.Size.Y)), col.WithAlpha(0.9f * f));
    }

    private static string ProblemIcon(string problem) => problem switch { "냉각" => "coolant", "문" => "room-airlock", _ => "wrench" };

    /// <summary>지금 계획 판 (위): 목표 카드 머리 · 숫자 칩 · 걸음 사다리 · 다음 조건 · 예상과 여유 · 대체 계획 · 최근 수정(단계 줄) · 견준 수순.</summary>
    private void DrawPlanPanel(Rect2 r, PlanReadout rd)
    {
        var w = _world;
        Gfx.RoundRect(this, r, Glass, Ui.RadiusControl, new Color(1, 1, 1, 0.06f));
        float x = r.Position.X + Ui.S3, right = r.End.X - Ui.S3, y = r.Position.Y + 22f;
        var p = rd.Plan;
        long now = w.Tick;
        // 목표 (카드 머리) — 목표가 바뀌면 짧게 빛난다
        string goal = p == null ? rd.Goal : p.Goal.Split(" — ")[0];
        FreshBand(new Rect2(r.Position.X + 2, r.Position.Y + 4, r.Size.X - 4, 24), Fresh("goal", rd.Goal), PlanCyan);
        string? sub = p == null ? null : (p.Goal.Contains(" — ") ? p.Goal.Split(" — ")[1] + " · " : "") + p.Name;
        UiKit.CardTitle(this, x, right, y, goal, sub, p == null ? "eye" : ProblemIcon(p.Problem), p == null ? Palette.TextDim : Palette.Text);
        y += 10f;
        if (p == null)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), Fit("고장 · 보고 · 자원 부족 같은 큰 변화가 오면 수순을 견줘 계획을 세운다", right - x, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextMuted);
            y += 18;
            if (rd.Reserve != "")
            {
                Icons.Draw(this, "battery", new Vector2(x + 6, y + 8), Ui.IconS, PlanAmber);
                Gfx.Text(this, Fonts.Body, new Vector2(x + 18, y + 12), Fit(rd.Reserve, right - x - 18, Ui.TextSmall, Fonts.Body), Ui.TextSmall, PlanAmber);
                y += 17;
            }
            var last = w.Automation.RecoveryOrNull?.Plans.LastOrDefault();
            if (last != null && y + 18 < r.End.Y)
            {
                var tone = last.State == "성공" ? Tone.Good : last.State == "중단" ? Tone.Danger : Tone.Info;
                var trail = last.Revisions.Select(v => (v.tick, v.why)).ToList();
                trail.Add((now, $"지난 계획: {last.Goal.Split(" — ")[0]} — {last.State} ({last.Elapsed(now):0}분)"));
                UiKit.Steps(this, x, right, y, trail, tone, (int)Mathf.Max(1, (r.End.Y - y) / 16f));
            }
            return;
        }
        // 숫자 칩: 남은 시간(최대) · 사람 손 · 고쳐 짬 · 견준 수순
        var s0 = p.Step;
        float leftMax = p.Steps.Skip(p.Cur).Sum(z => z.Max) - (s0?.Took(now) ?? 0f);
        int hands = p.Steps.Count(z => z.Act.Kind == FixKind.Hands && z.State is FixState.Run or FixState.Wait);
        UiKit.Stats(this, new Vector2(x, y + 8), right, new (string, int, string, Tone)[]
        {
            ("clock", Mathf.Max(1, Mathf.RoundToInt(leftMax)), "분 안", Tone.Info),
            ("crew", hands, "사람 손", Tone.Normal),
            ("why", p.Revisions.Count, "고쳐 짬", p.Revisions.Count > 0 ? Tone.Caution : Tone.Normal),
            ("layers", p.Compared.Count, "견준 수순", Tone.Normal),
        });
        y += 20f;
        // 걸음 사다리 (걸음 성격 · 상태마다 다른 그림) — 상태가 바뀐 걸음만 짧게 빛난다
        float rowH = Mathf.Clamp((r.End.Y - y - 110f) / Mathf.Max(1, p.Steps.Count), 15f, 22f);
        float lineX = x + 8;
        DrawLine(new Vector2(lineX, y + 6), new Vector2(lineX, y + rowH * p.Steps.Count - 4), new Color(1, 1, 1, 0.12f), 1.5f);
        for (int i = 0; i < p.Steps.Count; i++)
        {
            var s = p.Steps[i];
            float cy = y + rowH * i + rowH * 0.5f;
            bool cur = i == p.Cur && p.Open;
            float took = s.Took(now);
            float frac = s.State == FixState.Run ? took / Mathf.Max(1f, s.Max) : s.State == FixState.Done ? 1f : 0f;
            var col = StepColor(s.State);
            var band = new Rect2(x - 4, cy - rowH * 0.5f + 1, right - x + 8, rowH - 2);
            FreshBand(band, Fresh($"step:{p.Id}:{i}", $"{s.State}|{s.Waiting}"), col);
            if (cur) DrawRect(band, PlanCyan.WithAlpha(0.05f));
            DrawStepState(new Vector2(lineX, cy), s.State, frac);
            DrawStepKind(new Vector2(lineX + 18, cy), s.Act.Kind, col, s.State == FixState.Run);
            string range = s.Max - s.Min < 1.5f ? $"{s.Min:0}분" : $"{s.Min:0}~{s.Max:0}분";
            Gfx.Text(this, cur ? Fonts.Bold : Fonts.Body, new Vector2(lineX + 28, cy + 4), Fit(s.Name, (right - x) * 0.3f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, s.State == FixState.Wait && !cur ? Palette.TextDim : Palette.Text);
            float bx = lineX + 28 + (right - x) * 0.31f, bw = (right - x) * 0.22f;
            // 범위 띠: 최소~최대 (옅게) · 걸린 시간 (막대) · 최대를 넘기면 끝에 노란 점
            float scale = bw / Mathf.Max(1f, s.Max * 1.15f);
            DrawRect(new Rect2(bx, cy - 3, bw, 6), Ui.Track);
            DrawRect(new Rect2(bx + s.Min * scale, cy - 3, Mathf.Max(1f, (s.Max - s.Min) * scale), 6), col.WithAlpha(0.2f));
            if (took > 0f) DrawRect(new Rect2(bx, cy - 1.5f, Mathf.Min(bw, took * scale), 3), col.WithAlpha(0.85f));
            if (s.State == FixState.Run && took > s.Max) DrawCircle(new Vector2(bx + bw, cy), 2.2f, Palette.Warning, true, -1f, true);
            string tail = s.State == FixState.Wait && s.Waiting != "" ? s.Waiting : s.Note != "" ? s.Note : s.Waiting;
            Gfx.Text(this, Fonts.Body, new Vector2(bx + bw + 6, cy + 4), Fit(range + (tail != "" ? " · " + tail : ""), right - bx - bw - 6, Ui.TextTiny, Fonts.Body), Ui.TextTiny, s.State == FixState.Failed ? Palette.Danger : Palette.TextMuted);
        }
        y += rowH * p.Steps.Count + 4;
        // 다음 조건 · 예상과 여유 · 대체 계획 (아이콘 줄 — 바뀐 줄만 짧게 빛난다)
        void Row(string icon, string label, string text, string key, string seen, Color col)
        {
            if (y + 15 > r.End.Y - 2 || text == "") return;
            FreshBand(new Rect2(x - 4, y + 1, right - x + 8, 15), Fresh(key, seen), col);
            Icons.Draw(this, icon, new Vector2(x + 6, y + 8), Ui.IconS, col.WithAlpha(0.85f));
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 18, y + 12), label, Ui.TextTiny, Palette.TextMuted);
            float tx = x + 18 + 58;
            Gfx.Text(this, Fonts.Body, new Vector2(tx, y + 12), Fit(text, right - tx, Ui.TextSmall, Fonts.Body), Ui.TextSmall, col);
            y += 16;
        }
        Row("clock", "다음 조건", rd.Next, "next", $"{p.Id}|{p.Cur}|{s0?.State}|{s0?.Waiting}", Palette.Text);
        Row("trend-flat", "예상 · 여유", rd.Expect, "expect", $"{p.Id}|{p.OptionKey}|{p.Steps.Count}", PlanCyan);
        Row("route", "대체 계획", rd.Fallback, "fallback", rd.Fallback, PlanViolet);
        // 최근 수정 이유 (단계 줄 — 마지막이 굵게 · 새 수정이 오면 짧게 빛난다)
        if (p.Revisions.Count > 0 && y + 20 < r.End.Y)
        {
            float f = Fresh($"rev:{p.Id}", p.Revisions.Count.ToString());
            int room = (int)Mathf.Clamp((r.End.Y - y - 4f) / 16f, 1f, 3f);
            FreshBand(new Rect2(x - 4, y + 16f * (Mathf.Min(room, p.Revisions.Count) - 1) + 1, right - x + 8, 15), f, PlanAmber);
            y += UiKit.Steps(this, x, right, y, p.Revisions.Select(v => (v.tick, v.why)).ToList(), Tone.Caution, room) + 2;
        }
        // 견준 수순 (막대 — 고른 것은 화살표 · 막힌 것은 빗금)
        if (y + 14 < r.End.Y)
        {
            float max = 0.01f;
            foreach (var o in p.Compared) if (o.Allowed) max = Mathf.Max(max, o.Score);
            foreach (var o in p.Compared)
            {
                if (y + 13 > r.End.Y - 2) break;
                bool pick = o.Key == p.OptionKey;
                var oc = !o.Allowed ? Palette.TextMuted : pick ? PlanCyan : Palette.TextDim;
                if (pick) DrawColoredPolygon(new[] { new Vector2(x, y + 4), new Vector2(x + 6, y + 7.5f), new Vector2(x, y + 11) }, PlanCyan);
                Gfx.Text(this, pick ? Fonts.Bold : Fonts.Body, new Vector2(x + 10, y + 11), Fit(o.Name, (right - x) * 0.32f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, oc);
                float bx = x + 12 + (right - x) * 0.32f, bw = (right - x) * 0.2f;
                DrawRect(new Rect2(bx, y + 4, bw, 6), Ui.Track);
                if (o.Allowed) DrawRect(new Rect2(bx, y + 4, bw * Mathf.Clamp(o.Score / max, 0.03f, 1f), 6), oc.WithAlpha(pick ? 0.8f : 0.4f));
                else for (int j = 0; j < 6; j++) DrawLine(new Vector2(bx + j * bw / 6f, y + 10), new Vector2(bx + j * bw / 6f + 4, y + 4), Palette.TextMuted.WithAlpha(0.5f), 1f);
                string tail = !o.Allowed ? o.Blocked : (o.PeakMax > 0f ? $"노심 최고 {o.PeakMax:0}℃ · " : "") + (o.BatteryKwh > 0.5f ? $"배터리 {o.BatteryKwh:0}kWh · " : "") + $"{o.Min:0}~{o.Max:0}분";
                Gfx.Text(this, Fonts.Body, new Vector2(bx + bw + 6, y + 11), Fit(tail, right - bx - bw - 6, Ui.TextTiny, Fonts.Body), Ui.TextTiny, oc);
                y += 13;
            }
        }
    }

    private static string StyleName(string style) => style switch
    {
        "수치만" => "숙련자에게 — 수치만", "순서와 이유" => "처음 하는 사람에게 — 순서와 이유", "짧게" => "지친 사람에게 — 짧게", "근거부터" => "의심 많은 사람에게 — 근거부터", _ => "",
    };

    /// <summary>읽는 값 판 (아래): 사실 · 불확실 · 예측(모양으로 구분) · 예약 · 구역 · 자기 상태 · 돌아봄 · 성격 · 마지막 부탁(인용) · 고른 설비(칸).</summary>
    private void DrawReadings(Rect2 r, PlanReadout rd)
    {
        var w = _world;
        var a = w.Automation;
        Gfx.RoundRect(this, r, Glass, Ui.RadiusControl, new Color(1, 1, 1, 0.05f));
        float x = r.Position.X + Ui.S3, right = r.End.X - Ui.S3, y = r.Position.Y + 4;
        // 고른 설비: 왜 꺼져 있나 / 언제 켜지나 / 무엇을 기다리나 — 아래에 칸 하나 (먼저 자리를 잡는다)
        var sel = _main.SelectedFurniture is Furniture sf && sf.Machine != null ? sf : null;
        float bottom = r.End.Y - (sel != null ? 84f : 2f);
        if (sel != null)
        {
            var (why, when, wait) = ComputerReadout.WhyOff(w, sel);
            var tile = new Rect2(r.Position.X + 6, r.End.Y - 82f, r.Size.X - 12, 78f);
            UiKit.Tile(this, tile, "why", sel.Name, new[] { $"왜 — {why}", $"언제 — {when}", $"기다림 — {wait}" }, PlanCyan, true, 3);
        }
        void Line(ReadLine l, Color col)
        {
            if (y + 15 > bottom) return;
            float fade = l.Age < 0f ? 1f : Mathf.Clamp(1.1f - l.Age / 30f, 0.45f, 1f); // 오래된 값은 흐리게
            DrawReadKind(new Vector2(x + 6, y + 8), l.Kind, col.WithAlpha(fade));
            string age = l.Age < 0.5f ? "" : $" ({l.Age:0}분 전)";
            Gfx.Text(this, Fonts.Body, new Vector2(x + 18, y + 12), Fit(l.Text + age, right - x - 18, Ui.TextSmall, Fonts.Body), Ui.TextSmall, (l.Age > 10f ? Palette.Warning : Palette.Text).WithAlpha(fade));
            y += 15;
        }
        void Head(string icon, string t, Color col)
        {
            if (y + 16 > bottom) return;
            UiKit.Header(this, x, right, y + 13, t, null, icon, col.Lerp(Palette.TextMuted, 0.4f));
            y += 16;
        }
        if (rd.Facts.Count > 0) { Head("sensor", "확인된 사실", PlanMint); foreach (var l in rd.Facts.Take(5)) Line(l, l.Kind == "보고" ? PlanAmber : PlanMint); }
        if (rd.Unknowns.Count > 0) { Head("help", "불확실한 것", PlanViolet); foreach (var l in rd.Unknowns.Take(4)) Line(l, PlanViolet); }
        if (rd.Forecasts.Count > 0) { Head("trend-up", "예측", PlanCyan); foreach (var l in rd.Forecasts.Take(3)) Line(l, PlanCyan); }
        // 예약 · 구역 · 자기 상태 · 돌아봄 · 성격 (아이콘 줄 — 바뀐 줄만 짧게 빛난다)
        foreach (var (icon, label, text, col) in new[] { ("battery", "예약", rd.Reserve, PlanAmber), ("signal", "구역", rd.Zones, PlanMint), ("computer", "자기 상태", rd.Self, Palette.Warning), ("log", "돌아봄", rd.Review, Palette.TextDim), ("social", "성격", rd.Manner, Palette.TextDim) })
        {
            if (text == "" || y + 15 > bottom) continue;
            FreshBand(new Rect2(x - 4, y + 1, right - x + 8, 14), Fresh("rd:" + label, text), col);
            Icons.Draw(this, icon, new Vector2(x + 6, y + 8), Ui.IconS, col);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 18, y + 12), label, Ui.TextTiny, Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 72, y + 12), Fit(text, right - x - 72, Ui.TextTiny, Fonts.Body), Ui.TextTiny, col);
            y += 15;
        }
        // 마지막으로 사람에게 건넨 부탁 (사람마다 말이 다르다 — 인용)
        if (a.MannerOrNull is ComputerManner mn && mn.LastBrief.tick >= 0 && w.Tick - mn.LastBrief.tick < SimTime.Hours(2) && y + 36 < bottom
            && w.Crew.FirstOrDefault(c => c.Id == mn.LastBrief.crew) is CrewMember who)
            UiKit.Quote(this, x, right, y + 2, $"{a.Voice.Call} → {who.Name}", StyleName(mn.LastBrief.style), mn.LastBrief.text, CmdHands, (int)Mathf.Clamp((bottom - y - 18f) / 16f, 1f, 2f));
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
