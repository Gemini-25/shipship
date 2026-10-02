using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.18 사고 다섯 규모 — 화면 (읽기만 한다).
///  · 규모마다 다른 아이콘(개인 사람 · 방 문 달린 칸 · 계통 이어진 마디 · 배 선체 · 우주급 별 폭발)과 색.
///  · 사고 카드에 규모 배지 + 규모마다 다른 테두리(실선 · 흐르는 점선 · 맥박 겹선 · 별빛 번짐).
///  · 화면 테두리: 지금 가장 큰 뜨거운 사고의 규모 (방 모서리 · 계통 점선 · 배 전체 붉은 겹테 + 띠 · 우주급 보랏빛 별 테 + 띠).
///  · 인과 사슬: 고리마다 규모 고리 · 규모가 오른 고리엔 ▲ · 머리에 단계 (② → ③ → ④). 시간 막대는 규모 색.
///  · 사고 도감 (Shift+K): 규모별 묶음 · 겪은 횟수.
/// </summary>
public partial class Hud
{
    public bool ScaleCodexOpen { get; private set; }

    public void ToggleScaleCodex()
    {
        ScaleCodexOpen = !ScaleCodexOpen;
        if (ScaleCodexOpen) { ChronicleOpen = false; TechOpen = false; ControlOpen = false; PolicyOpen = false; OpenChain(null); }
    }

    public static Color ScaleColor(IncidentScale s) => UiKit.ScaleColor(s); // v16.24 공통 부품

    // ─────────────────────────────── 아이콘 (규모마다 다른 실루엣) ───────────────────────────────

    /// <summary>규모 아이콘: 개인(사람) · 방(문 달린 칸) · 계통(이어진 세 마디) · 배(선체) · 우주급(별 폭발).</summary>
    public static void ScaleIcon(CanvasItem ci, IncidentScale s, Vector2 c, float size, Color col, float t = 0f)
    {
        float h = size * 0.5f;
        float lw = Mathf.Max(1.2f, size / 11f);
        switch (s)
        {
            case IncidentScale.Personal:
            {
                // 머리 + 어깨 (사람 하나)
                ci.DrawCircle(c + new Vector2(0, -h * 0.38f), h * 0.3f, col);
                ci.DrawArc(c + new Vector2(0, h * 0.62f), h * 0.62f, Mathf.Pi * 1.08f, Mathf.Pi * 1.92f, 12, col, lw * 1.4f, true);
                break;
            }
            case IncidentScale.Room:
            {
                // 칸 하나 + 문틈 + 바닥 물방울
                var r = new Rect2(c - new Vector2(h * 0.8f, h * 0.7f), new Vector2(h * 1.6f, h * 1.4f));
                ci.DrawLine(r.Position, r.Position + new Vector2(r.Size.X, 0), col, lw, true);
                ci.DrawLine(r.Position, r.Position + new Vector2(0, r.Size.Y), col, lw, true);
                ci.DrawLine(r.End, r.End - new Vector2(r.Size.X, 0), col, lw, true);
                ci.DrawLine(r.Position + new Vector2(r.Size.X, 0), r.Position + new Vector2(r.Size.X, r.Size.Y * 0.35f), col, lw, true); // 문틈
                ci.DrawLine(r.End - new Vector2(0, r.Size.Y * 0.2f), r.End, col, lw, true);
                ci.DrawCircle(c + new Vector2(-h * 0.1f, h * 0.15f), h * 0.18f, col.WithAlpha(0.85f));
                break;
            }
            case IncidentScale.System:
            {
                // 이어진 세 마디 (망) — 하나가 깜박인다
                var a = c + new Vector2(-h * 0.75f, h * 0.5f);
                var b = c + new Vector2(h * 0.75f, h * 0.5f);
                var d = c + new Vector2(0, -h * 0.65f);
                ci.DrawLine(a, b, col, lw, true);
                ci.DrawLine(a, d, col, lw, true);
                ci.DrawLine(b, d, col, lw, true);
                float blink = 0.55f + 0.45f * Mathf.Sin(t * 6f);
                ci.DrawCircle(a, h * 0.24f, col);
                ci.DrawCircle(b, h * 0.24f, col);
                ci.DrawCircle(d, h * 0.28f, col.WithAlpha(blink));
                break;
            }
            case IncidentScale.Ship:
            {
                // 선체 실루엣 (뾰족한 앞 · 함교 · 엔진 불꽃)
                var pts = new[]
                {
                    c + new Vector2(h * 0.95f, 0), c + new Vector2(h * 0.35f, -h * 0.45f), c + new Vector2(-h * 0.7f, -h * 0.45f),
                    c + new Vector2(-h * 0.85f, -h * 0.2f), c + new Vector2(-h * 0.85f, h * 0.2f), c + new Vector2(-h * 0.7f, h * 0.45f), c + new Vector2(h * 0.35f, h * 0.45f),
                };
                ci.DrawColoredPolygon(pts, col.WithAlpha(0.35f));
                for (int i = 0; i < pts.Length; i++) ci.DrawLine(pts[i], pts[(i + 1) % pts.Length], col, lw, true);
                ci.DrawRect(new Rect2(c + new Vector2(h * 0.05f, -h * 0.2f), new Vector2(h * 0.28f, h * 0.4f)), col);
                float flick = 0.6f + 0.4f * Mathf.Sin(t * 11f);
                ci.DrawLine(c + new Vector2(-h * 0.9f, 0), c + new Vector2(-h * (1.05f + 0.15f * flick), 0), col.WithAlpha(flick), lw * 1.5f, true);
                break;
            }
            default:
            {
                // 별 폭발: 여덟 줄기 + 테 (천천히 돈다)
                float rot = t * 0.6f;
                for (int i = 0; i < 8; i++)
                {
                    float ang = rot + i * Mathf.Tau / 8f;
                    float len = i % 2 == 0 ? h * 0.95f : h * 0.6f;
                    ci.DrawLine(c + Vector2.FromAngle(ang) * h * 0.22f, c + Vector2.FromAngle(ang) * len, col, lw, true);
                }
                ci.DrawCircle(c, h * 0.2f, col);
                ci.DrawArc(c, h * 0.5f, 0, Mathf.Tau, 18, col.WithAlpha(0.5f), lw * 0.8f, true);
                break;
            }
        }
    }

    // ─────────────────────────────── 사고 카드 · 시간 막대 · 사슬 ───────────────────────────────

    /// <summary>사고 카드에 규모 배지 + 규모마다 다른 테두리.</summary>
    private void DrawScaleBadge(Rect2 card, CauseIncident inc)
    {
        if (_world.Scale.CaseOf(inc) is not ScaleCase k) return;
        var s = inc.Open ? k.Now > IncidentScale.Personal ? k.Now : k.Peak : k.Peak;
        var col = ScaleColor(s);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * (s >= IncidentScale.Ship ? 6f : 3f));
        // 테두리
        switch (s)
        {
            case IncidentScale.Room:
                Gfx.RoundRect(this, card, new Color(0, 0, 0, 0), Ui.RadiusControl + 2f, col.WithAlpha(0.55f), 1);
                break;
            case IncidentScale.System:
                DashedRect(card.Grow(1f), col.WithAlpha(0.8f), 1.6f, 7f, 5f, _time * 18f);
                break;
            case IncidentScale.Ship:
                Gfx.RoundRect(this, card, new Color(0, 0, 0, 0), Ui.RadiusControl + 2f, col.WithAlpha(0.6f + 0.4f * pulse), 2);
                Gfx.RoundRect(this, card.Grow(3f), new Color(0, 0, 0, 0), Ui.RadiusControl + 4f, col.WithAlpha(0.25f * pulse), 1);
                break;
            case IncidentScale.Cosmic:
                Gfx.RoundRect(this, card.Grow(2f), col.WithAlpha(0.06f + 0.05f * pulse), Ui.RadiusControl + 3f, col.WithAlpha(0.7f), 1);
                for (int i = 0; i < 6; i++)
                {
                    float u = Mathf.PosMod(_time * 0.12f + i / 6f, 1f);
                    var p = PerimeterPoint(card.Grow(2f), u);
                    DrawCircle(p, 1.6f + 0.8f * Mathf.Sin(_time * 5f + i), Colors.White.WithAlpha(0.8f));
                }
                break;
        }
        // 배지: 카드 위쪽 테두리에 걸친 알약 (아이콘 · 번호 · 이름)
        string label = $"{ScaleTable.Mark(s)} {ScaleTable.Name(s)}";
        float lw = Gfx.Width(Fonts.Bold, label, Ui.TextMicro) + 26f;
        var pill = new Rect2(card.End.X - lw - 70f, card.Position.Y - 8f, lw, 15f);
        Gfx.RoundRect(this, pill, new Color(0.05f, 0.06f, 0.09f, 0.96f), 7f, col.WithAlpha(0.9f));
        ScaleIcon(this, s, pill.Position + new Vector2(9f, 7.5f), 11f, col, _time);
        Gfx.Text(this, Fonts.Bold, pill.Position + new Vector2(18f, 11f), label, Ui.TextMicro, col);
        // 규모가 오른 지 얼마 안 됐으면 위로 화살 (번지는 중)
        if (inc.Open && k.Steps.Count > 1 && _world.Tick - k.Steps[^1].Tick < SimTime.Minutes(20) && k.Steps[^1].To > k.Steps[^1].From)
            DrawColoredPolygon(new[] { pill.End + new Vector2(6, -9), pill.End + new Vector2(2, -3), pill.End + new Vector2(10, -3) }, col.WithAlpha(0.6f + 0.4f * pulse));
        // 컴퓨터가 교훈으로 한 칸 높여 대비했다: 겹친 꺾쇠 + "대비 ③" (배지 왼쪽)
        if (k.Steps.Count > 0 && k.Guess > k.Steps[0].To)
        {
            var gc = ScaleColor(k.Guess);
            var at = new Vector2(pill.Position.X - 4f, pill.Position.Y + 9f);
            for (int i = 0; i < 2; i++)
                DrawPolyline(new[] { at + new Vector2(-10f, 3f - i * 4f), at + new Vector2(-6f, -1f - i * 4f), at + new Vector2(-2f, 3f - i * 4f) }, gc.WithAlpha(0.9f - 0.3f * i), 1.4f, true);
            Gfx.TextRight(this, Fonts.Bold, at + new Vector2(-13f, 3f), $"대비 {ScaleTable.Mark(k.Guess)}", Ui.TextMicro, gc);
        }
    }

    /// <summary>시간 막대의 규모 색 (규모를 모르면 null — 원래 색).</summary>
    private Color? ScaleBarColor(CauseIncident inc)
    {
        if (_world.Scale.CaseOf(inc) is not ScaleCase k || k.Peak == IncidentScale.Personal) return null;
        var col = ScaleColor(k.Peak);
        return inc.Open ? col : col.Darkened(0.35f);
    }

    /// <summary>사슬의 고리 점에 규모 고리 · 규모가 오른 고리엔 ▲.</summary>
    private void DrawNodeScale(CauseNode n, float ix, float cy, float r)
    {
        if (n.Kind == CauseKind.Recovery || _world.Scale.NodeScale(n.Id) is not IncidentScale s) return;
        var col = ScaleColor(s);
        int seg = (int)s + 1; // 규모만큼 토막 난 고리 (개인 1 · 방 2 · 계통 3 · 배 4 · 우주급 5)
        float gap = seg == 1 ? 0f : 0.5f;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.Tau / seg + gap * 0.5f, a1 = (i + 1) * Mathf.Tau / seg - gap * 0.5f;
            DrawArc(new Vector2(ix, cy), r + 3.5f, a0 - Mathf.Pi * 0.5f, a1 - Mathf.Pi * 0.5f, 8, col.WithAlpha(0.85f), 1.3f, true);
        }
        var k = ChainIncident != null ? _world.Scale.CaseOf(ChainIncident) : null;
        if (k != null && k.Steps.Any(st => st.Node == n.Id && st.To > st.From))
            DrawColoredPolygon(new[] { new Vector2(ix + r + 5f, cy - r - 6f), new Vector2(ix + r + 1.5f, cy - r - 0.5f), new Vector2(ix + r + 8.5f, cy - r - 0.5f) }, col);
    }

    /// <summary>사슬 머리: 규모 단계 (② → ③ → ④) · 판정한 쪽 · 부른 사람.</summary>
    private void DrawChainScale(CauseIncident inc, float x, float right, float y0)
    {
        if (_world.Scale.CaseOf(inc) is not ScaleCase k) return;
        var steps = new List<IncidentScale>();
        foreach (var st in k.Steps) if (steps.Count == 0 || steps[^1] != st.To) steps.Add(st.To);
        float cx = right - 4f;
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            var s = steps[i];
            var col = ScaleColor(s);
            DrawCircle(new Vector2(cx - 8f, y0 + 48f), 9f, col.WithAlpha(0.16f));
            DrawArc(new Vector2(cx - 8f, y0 + 48f), 9f, 0, Mathf.Tau, 18, col.WithAlpha(0.8f), 1.2f, true);
            ScaleIcon(this, s, new Vector2(cx - 8f, y0 + 48f), 11f, col, _time);
            cx -= 20f;
            if (i > 0) { Gfx.Text(this, Fonts.Bold, new Vector2(cx - 6f, y0 + 52f), "›", Ui.TextSmall, Palette.TextMuted); cx -= 10f; }
        }
        string who = k.JudgedBy.Length > 0 ? $"판정 {k.JudgedBy} · " : "";
        Gfx.TextRight(this, Fonts.Body, new Vector2(cx - 4f, y0 + 52f), $"{who}부름 {k.Called} · 붙음 {k.Responders}", Ui.TextTiny, ScaleColor(k.Now).Lerp(Palette.TextDim, 0.4f));
    }

    // ─────────────────────────────── 화면 테두리 ───────────────────────────────

    /// <summary>지금 가장 큰 뜨거운 사고의 규모로 화면 가장자리를 두른다 (개인은 없음).</summary>
    private void DrawScaleFrame()
    {
        if (_world.Scale.Top is not ScaleCase k || k.Now == IncidentScale.Personal) return;
        var s = k.Now;
        var sz = Screen;
        var col = ScaleColor(s);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * (s >= IncidentScale.Ship ? 5f : 2.5f));
        float fresh = Mathf.Clamp(1f - (_world.Tick - k.Changed) / (float)SimTime.Minutes(30), 0f, 1f);
        switch (s)
        {
            case IncidentScale.Room:
            {
                // 방: 네 모서리 꺾쇠 (규모가 정해진 직후에만 또렷하게)
                float a = 0.25f + 0.55f * fresh;
                float L = 22f;
                foreach (var (p, dx, dy) in new[] { (new Vector2(5, 5), 1, 1), (new Vector2(sz.X - 5, 5), -1, 1), (new Vector2(5, sz.Y - 5), 1, -1), (new Vector2(sz.X - 5, sz.Y - 5), -1, -1) })
                {
                    DrawLine(p, p + new Vector2(L * dx, 0), col.WithAlpha(a), 2f);
                    DrawLine(p, p + new Vector2(0, L * dy), col.WithAlpha(a), 2f);
                }
                break;
            }
            case IncidentScale.System:
                // 계통: 흐르는 점선 테 (망을 따라 번지는 것처럼)
                DashedRect(new Rect2(4, 4, sz.X - 8, sz.Y - 8), col.WithAlpha(0.45f + 0.35f * fresh), 2.2f, 16f, 10f, _time * 40f);
                break;
            case IncidentScale.Ship:
            {
                // 배 전체: 붉은 겹테가 맥박 · 위 가운데 띠
                for (int i = 0; i < 4; i++)
                {
                    float inset = i * 4f + 2f;
                    DrawRect(new Rect2(inset, inset, sz.X - inset * 2, sz.Y - inset * 2), col.WithAlpha((0.6f - i * 0.13f) * (0.55f + 0.45f * pulse)), false, 3f);
                }
                ScaleBanner(k, col, pulse);
                break;
            }
            default:
            {
                // 우주급: 보랏빛 번짐 + 가장자리를 도는 별
                for (int i = 0; i < 6; i++)
                {
                    float inset = i * 5f + 1f;
                    DrawRect(new Rect2(inset, inset, sz.X - inset * 2, sz.Y - inset * 2), col.WithAlpha(0.32f - i * 0.05f), false, 4f);
                }
                var edge = new Rect2(10, 10, sz.X - 20, sz.Y - 20);
                for (int i = 0; i < 24; i++)
                {
                    float u = Mathf.PosMod(_time * 0.03f + i / 24f, 1f);
                    var p = PerimeterPoint(edge, u);
                    float tw = 0.5f + 0.5f * Mathf.Sin(_time * 4f + i * 1.7f);
                    DrawCircle(p, 1.2f + 1.6f * tw, Colors.White.WithAlpha(0.35f + 0.55f * tw));
                }
                ScaleBanner(k, col, pulse);
                break;
            }
        }
    }

    /// <summary>배 전체 · 우주급 띠: 아이콘 · 규모 · 대응 · 컴퓨터 방송 한 줄.</summary>
    private void ScaleBanner(ScaleCase k, Color col, float pulse)
    {
        var sz = Screen;
        string head = $"{ScaleTable.Label(k.Now)} — {ScaleTable.Response(k.Now)}";
        string sub = k.Broadcast.Length > 0 ? k.Broadcast : k.Name;
        float w = Mathf.Clamp(Mathf.Max(Gfx.Width(Fonts.Bold, head, Ui.TextTitle), Gfx.Width(Fonts.Body, sub, Ui.TextSmall)) + 70f, 300f, sz.X * 0.6f);
        var r = new Rect2(sz.X * 0.5f - w * 0.5f, Ui.TopBarH + 14f, w, 44f);
        Gfx.RoundRect(this, r, new Color(0.06f, 0.03f, 0.05f, 0.93f), 10f, col.WithAlpha(0.6f + 0.4f * pulse), 2);
        // 위험 줄무늬 (배 전체) · 별가루 (우주급)
        if (k.Now == IncidentScale.Ship)
            for (float sx = r.Position.X + 12f; sx < r.End.X - 12f; sx += 14f)
                DrawLine(new Vector2(sx, r.End.Y - 3f), new Vector2(sx + 7f, r.End.Y - 3f), col.WithAlpha(0.5f), 2f);
        ScaleIcon(this, k.Now, r.Position + new Vector2(24f, 22f), 26f, col, _time);
        Gfx.Text(this, Fonts.Bold, r.Position + new Vector2(46f, 19f), Fit(head, w - 60f, Ui.TextTitle, Fonts.Bold), Ui.TextTitle, col.Lerp(Palette.Text, 0.3f));
        Gfx.Text(this, Fonts.Body, r.Position + new Vector2(46f, 36f), Fit(sub, w - 60f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim);
        if (k.MusterRoom >= 0 && k.MusterAt >= 0)
            Gfx.TextRight(this, Fonts.Bold, r.End - new Vector2(12f, 8f), $"점호 {k.Mustered.Count}", Ui.TextTiny, col);
    }

    // ─────────────────────────────── 사고 도감 (규모별) ───────────────────────────────

    private void DrawScaleCodex(Vector2 mouse)
    {
        var sc = _world.Scale;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f;
        float w = Mathf.Min(760f, Screen.X - RightColumnWidth - Margin * 3);
        float height = Screen.Y - y0 - LogFullHeight - Margin - 40f;
        var card = new Rect2(x0, y0, w, height);
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y0 + 30), "사고 도감 — 다섯 규모", Ui.TextLarge, Palette.Text);
        Button(new Rect2(right - 76, y0 + 12, 76, 26), "⇧K 닫기", false, mouse, ToggleScaleCodex, Ui.TextSmall);
        int total = ScaleTable.All.Length, seenKinds = ScaleTable.All.Count(r => sc.SeenOf(r.Key) > 0);
        Gfx.Text(this, Fonts.Body, new Vector2(x, y0 + 50), $"표 {total}종 · 겪어 본 것 {seenKinds}종 · 규모가 오른 일 {sc.Escalations} · 컴퓨터 판정 {sc.Plans} · 교훈 {sc.Lessons.Count} · 이야기꾼이 쉬어 감 {sc.Rests}", Ui.TextBody, Palette.TextDim);
        Divider(x, right, y0 + 62);
        float y = y0 + 70f;
        float rowH = (card.End.Y - y - 10f) / 5f;
        foreach (var s in ScaleTable.Scales)
        {
            var col = ScaleColor(s);
            var row = new Rect2(x, y, right - x, rowH - 6f);
            Gfx.RoundRect(this, row, col.WithAlpha(0.05f), 8f, col.WithAlpha(0.25f));
            // 아이콘 칸
            var ic = new Vector2(x + 26f, y + 28f);
            DrawCircle(ic, 19f, col.WithAlpha(0.14f));
            DrawArc(ic, 19f, 0, Mathf.Tau, 24, col.WithAlpha(0.7f), 1.5f, true);
            ScaleIcon(this, s, ic, 24f, col, _time);
            int seen = sc.Experienced(s);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 54f, y + 20f), $"{ScaleTable.Label(s)} — {ScaleTable.Response(s)}", Ui.TextTitle, col.Lerp(Palette.Text, 0.25f));
            Gfx.TextRight(this, Fonts.Bold, new Vector2(row.End.X - 10f, y + 20f), $"겪음 {seen}", Ui.TextSmall, seen > 0 ? col : Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 54f, y + 36f), $"예: {ScaleTable.Examples(s)}", Ui.TextSmall, Palette.TextDim);
            // 종류: 겪은 것 먼저 (횟수) · 아직인 것은 흐리게
            var rows = ScaleTable.Group(s).OrderByDescending(r => sc.SeenOf(r.Key)).ThenBy(r => Array.IndexOf(ScaleTable.Sources, r.Source)).ToList();
            float tx = x + 54f, ty = y + 54f, maxY = row.End.Y - 6f;
            int shown = 0;
            foreach (var r in rows)
            {
                int n = sc.SeenOf(r.Key);
                string t = n > 0 ? $"{r.Name} ×{n}" : r.Name;
                float tw = Gfx.Width(Fonts.Body, t, Ui.TextTiny) + 12f;
                if (tx + tw > row.End.X - 8f) { tx = x + 54f; ty += 15f; }
                if (ty > maxY) break;
                if (n > 0) Gfx.RoundRect(this, new Rect2(tx - 3f, ty - 10f, tw - 4f, 13f), col.WithAlpha(0.16f), 4f);
                Gfx.Text(this, Fonts.Body, new Vector2(tx, ty), t, Ui.TextTiny, n > 0 ? col.Lerp(Palette.Text, 0.4f) : Palette.TextMuted);
                // 컴퓨터 교훈: 다음엔 한 칸 높여 부르는 종류 — 그 규모 색 꺾쇠 (칩 오른쪽 위)
                if (sc.Lessons.TryGetValue(r.Key, out var ls))
                {
                    var lc = ScaleColor(ls);
                    var lp = new Vector2(tx + tw - 9f, ty - 9f);
                    DrawPolyline(new[] { lp + new Vector2(-3f, 2f), lp, lp + new Vector2(3f, 2f) }, lc, 1.3f, true);
                    DrawPolyline(new[] { lp + new Vector2(-3f, 5f), lp + new Vector2(0f, 3f), lp + new Vector2(3f, 5f) }, lc.WithAlpha(0.6f), 1.2f, true);
                }
                tx += tw;
                shown++;
            }
            if (shown < rows.Count) Gfx.TextRight(this, Fonts.Body, new Vector2(row.End.X - 10f, y + 36f), $"외 {rows.Count - shown}종", Ui.TextTiny, Palette.TextMuted);
            y += rowH;
        }
    }

    // ─────────────────────────────── 도우미 ───────────────────────────────

    /// <summary>흐르는 점선 사각형.</summary>
    private void DashedRect(Rect2 r, Color col, float width, float dash, float gap, float offset)
    {
        float per = 2f * (r.Size.X + r.Size.Y);
        float step = dash + gap;
        for (float u = -Mathf.PosMod(offset, step); u < per; u += step)
        {
            float a = Mathf.Max(0f, u), b = Mathf.Min(per, u + dash);
            if (b <= a) continue;
            // 모서리를 넘는 토막은 둘로
            float mid = a;
            while (mid < b)
            {
                float edgeEnd = EdgeEnd(r, mid);
                float seg = Mathf.Min(b, edgeEnd);
                DrawLine(PerimeterPoint(r, mid / per), PerimeterPoint(r, seg / per), col, width);
                mid = seg + 0.001f;
            }
        }
    }

    private static float EdgeEnd(Rect2 r, float d)
    {
        float w = r.Size.X, h = r.Size.Y;
        if (d < w) return w;
        if (d < w + h) return w + h;
        if (d < 2 * w + h) return 2 * w + h;
        return 2 * (w + h);
    }

    /// <summary>사각형 둘레의 점 (0~1, 왼쪽 위에서 시계 방향).</summary>
    private static Vector2 PerimeterPoint(Rect2 r, float u)
    {
        float w = r.Size.X, h = r.Size.Y, per = 2f * (w + h);
        float d = Mathf.PosMod(u, 1f) * per;
        if (d < w) return r.Position + new Vector2(d, 0);
        d -= w;
        if (d < h) return r.Position + new Vector2(w, d);
        d -= h;
        if (d < w) return r.Position + new Vector2(w - d, h);
        d -= w;
        return r.Position + new Vector2(0, h - d);
    }
}
