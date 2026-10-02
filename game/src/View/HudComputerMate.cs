using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.27 관제 화면 "하루 · 정비표" 탭 (읽기만 — 완전 관전):
///  왼쪽 — 오늘 아침 방송(갈래마다 그림) · 주간 정비표(7일 칸 · 남은 수명 막대 · 미루면 위험) · 물자 전망 · 우주 날씨(맞힘 · 빗나감).
///  오른쪽 — 돌봄(살피는 범위) · 훈련(집결 시간 · 배치표 고침) · 약속(지킴 · 어김) · 거절(짐작 ↔ 실제) · 개조 · 기억 · 놓친 경보 · 딜레마.
/// </summary>
public partial class Hud
{
    private void DrawMateTab(Rect2 card, float x, float right, float y)
    {
        var w = _world;
        if (w.Automation.MateOrNull is not ShipMate m) { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 14), "아직 기록이 없다", Ui.TextTiny, Palette.TextDim); return; }
        float colW = (right - x) / 2f - 10;
        float lx = x, ly = y, rx = x + colW + 20, ry = y;
        int day = SimTime.Day(w.Tick);

        // ── 왼쪽: 아침 방송 ──
        var b = m.LastBriefing;
        SectionTitle(lx, ly + 10, b != null ? $"아침 방송 — {b.Day}일 {SimTime.Clock(b.Tick)} · 들은 사람 {b.Heard.Count}명" : "아침 방송 — 아직 없다");
        ly += 18;
        if (b != null)
            foreach (var (kind, text) in b.Lines)
            {
                MateGlyph(new Vector2(lx + 6, ly + 8), kind);
                Gfx.Text(this, Fonts.Body, new Vector2(lx + 16, ly + 12), Fit(text, colW - 16, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Text);
                ly += 14;
            }
        // ── 주간 정비표 ──
        ly += 6;
        SectionTitle(lx, ly + 10, $"주간 정비표 — 계획대로 {m.SlotsDone} · 놓침 {m.SlotsMissed} · 일찍 손봄 {m.ServicedEarly}");
        ly += 18;
        float cw = (colW - 4) / 7f;
        for (int d = 0; d < 7; d++)
        {
            var cell = new Rect2(lx + d * cw, ly, cw - 3, 46);
            Gfx.RoundRect(this, cell, d == 0 ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0.03f), 3, new Color(1, 1, 1, 0.08f));
            Gfx.TextCentered(this, Fonts.Bold, new Vector2(cell.GetCenter().X, cell.Position.Y + 7), $"{day + d}일", Ui.TextMicro, d == 0 ? Palette.Text : Palette.TextMuted);
            int k = 0;
            foreach (var s in m.Slots.Where(s => s.Day == day + d).OrderBy(s => s.Hour).ThenBy(s => s.MachineId).Take(3))
            {
                var row = new Rect2(cell.Position.X + 2, cell.Position.Y + 13 + k * 11, cell.Size.X - 4, 9);
                var col = s.Done ? Palette.Good : s.Missed ? Palette.Danger : s.RiskLate > 0.25f ? Palette.Warning : new Color("#f4c430");
                DrawRect(row, col.WithAlpha(0.15f));
                // 남은 수명 막대 (짧을수록 꽉 찬다)
                DrawRect(new Rect2(row.Position, new Vector2(row.Size.X * Mathf.Clamp(1f - s.LifeLo / 7f, 0.05f, 1f), 2)), col);
                Gfx.Text(this, Fonts.Body, new Vector2(row.Position.X + 1, row.End.Y - 1), Fit($"{s.Hour:0}시 {s.Machine}", row.Size.X - 2, Ui.TextMicro, Fonts.Body), Ui.TextMicro, s.Done ? Palette.TextMuted : Palette.Text);
                k++;
            }
        }
        ly += 50;
        foreach (var s in m.Slots.Where(s => !s.Done && !s.Missed).OrderBy(s => s.At).Take(2))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 11), Fit($"{s.Machine} — 남은 수명 {s.LifeLo:0.#}~{s.LifeHi:0.#}일 · 미루면 고장 {s.RiskNow * 100:0}% → {s.RiskLate * 100:0}% · {s.Why}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
            ly += 14;
        }
        // ── 물자 전망 ──
        ly += 6;
        SectionTitle(lx, ly + 10, "물자 전망 (사흘 추세)");
        ly += 16;
        foreach (var s in m.Supplies.AsEnumerable().Reverse().Take(3))
        {
            var col = !s.Open ? Palette.TextMuted : s.DaysLeft < 6f ? Palette.Danger : Palette.Warning;
            Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 11), Fit($"{s.Name} {s.Warned:0}일 앞에 알림 (지금 {s.DaysLeft:0}일) → {s.Choice}{(s.Result != "" ? " · " + s.Result : "")}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, col);
            ly += 14;
        }
        // ── 우주 날씨 ──
        ly += 6;
        var judged = m.Forecasts.Where(f => f.Judged).ToList();
        SectionTitle(lx, ly + 10, $"우주 날씨 — 맞힘 {judged.Count(f => f.Hit)}/{judged.Count} · 오차 {m.SkyScore:0.00}");
        ly += 16;
        if (m.LastSky is SkyForecast sky)
        {
            DrawMateBar(new Vector2(lx, ly + 3), colW * 0.45f, sky.PStorm, "태양 폭풍");
            DrawMateBar(new Vector2(lx + colW * 0.5f, ly + 3), colW * 0.45f, sky.PShower, "운석우");
            ly += 16;
        }
        foreach (var f in judged.AsEnumerable().Reverse().Take(2)) { Gfx.Text(this, Fonts.Body, new Vector2(lx, ly + 11), Fit(f.Verdict, colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, f.Hit ? Palette.TextDim : Palette.Warning); ly += 14; }

        // ── 오른쪽: 돌봄 · 훈련 ──
        SectionTitle(rx, ry + 10, $"돌봄 — 살피는 범위 '{ShipMate.CareName(m.CareLevel)}' · 넌지시 {m.Hints} · 근무 조정 {m.Adjusts} · 바로 쉬라 {m.EarlyRests}");
        ry += 16;
        if (m.SnoopsFound > 0) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit($"몰래 본 게 들킨 일 {m.SnoopsFound}번 · 아는 사람 {m.KnowsSnoop.Count}명", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Danger); ry += 14; }
        ry += 4;
        var dr = m.ActiveDrill ?? m.Drills.LastOrDefault();
        SectionTitle(rx, ry + 10, $"훈련 — {m.Drills.Count}번 · 배치표 고침 {m.BillChanges} · 집결 셈 ×{m.MusterFactor:0.00}");
        ry += 16;
        if (dr != null) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit(dr.Done ? dr.Summary : $"지금 {dr.Kind} 훈련 중 — {dr.Arrive.Count}/{dr.Role.Count}명 도착", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim); ry += 14; }
        if (m.NextDrill is DrillRun nd) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), $"다음: {SimTime.Day(nd.Start)}일 {SimTime.Clock(nd.Start)} {nd.Kind} 훈련 ({nd.Room})", Ui.TextTiny, Palette.TextMuted); ry += 14; }
        // ── 약속 ──
        ry += 4;
        SectionTitle(rx, ry + 10, $"약속 — 지킴 {m.PromisesKept} · 어김 {m.PromisesBroken}");
        ry += 16;
        foreach (var p in m.Promises.AsEnumerable().Reverse().Take(3))
        {
            var col = p.Broken > p.Kept ? Palette.Danger : p.Kept > 0 ? Palette.Good : Palette.TextDim;
            DrawCircle(new Vector2(rx + 4, ry + 8), 3f, col);
            Gfx.Text(this, Fonts.Body, new Vector2(rx + 12, ry + 11), Fit($"\"{p.Text}\" — 들은 사람 {p.Heard.Count} · 지킴 {p.Kept} · 어김 {p.Broken}", colW - 12, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Text);
            ry += 14;
        }
        // ── 거절 ──
        ry += 4;
        SectionTitle(rx, ry + 10, $"받아들여지지 않은 부탁 — 짐작 맞음 {m.GuessesRight} · 틀림 {m.GuessesWrong}");
        ry += 16;
        foreach (var r in m.Refusals.AsEnumerable().Reverse().Take(3))
        {
            string who = w.Crew.FirstOrDefault(c => c.Id == r.CrewId)?.Name ?? "?";
            Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit($"{SimTime.Clock(r.Tick)} {who} · {r.Title} — 짐작 '{r.Guess}'{(r.GuessRight ? "" : $" (실제 '{r.Truth}')")} → {r.Next}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, r.GuessRight ? Palette.TextDim : Palette.Warning);
            ry += 14;
        }
        // ── 개조 · 정찰 ──
        ry += 4;
        SectionTitle(rx, ry + 10, $"스스로 고치기 — 정찰 {m.Scouts.Count} (봄 {m.ScoutsSeen}) · 개조 {m.UpgradesDone} · 코어 랙 +{m.CoreRacks}");
        ry += 16;
        foreach (var u in m.Upgrades.AsEnumerable().Reverse().Take(3))
        {
            string st = u.State switch { GearState.Proposed => "회의 기다림", GearState.Approved => "자재 기다림", GearState.Working => $"공사 중 {u.Done / Mathf.Max(0.1f, u.Need) * 100:0}%", GearState.Done => "달았다", _ => "거절됨" };
            Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit($"{u.Room} {u.Name} — {st} · {u.Cause}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, u.State == GearState.Done ? Palette.Good : Palette.TextDim);
            ry += 14;
        }
        // ── 약점 ──
        ry += 4;
        SectionTitle(rx, ry + 10, $"약점 — 기억 틀어짐 {m.Corruptions} · 바로잡힘 {m.Corrections} · 검사 {m.MemoryChecks} · 놓친 경보 {m.MissedAlarms.Count}");
        ry += 16;
        if (m.LastMemoryNote != "") { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit(m.LastMemoryNote, colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim); ry += 14; }
        foreach (var d in m.Dilemmas.AsEnumerable().Reverse().Take(2))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit($"{d.Room} ({d.Hazard}) — {(d.Decision == "" ? "묻는 중" : d.Decision)} · {d.By}{(d.Outcome != "" ? " · " + d.Outcome : "")}", colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, d.Outcome.Contains("숨") ? Palette.Danger : Palette.TextDim);
            ry += 14;
        }
        foreach (var rv in m.Reviews.AsEnumerable().Reverse().Take(2)) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit(rv.Text, colW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Warning); ry += 14; }
    }

    private void DrawMateBar(Vector2 p, float wdt, float v, string label)
    {
        DrawRect(new Rect2(p, new Vector2(wdt, 6)), new Color(1, 1, 1, 0.08f));
        DrawRect(new Rect2(p, new Vector2(wdt * Mathf.Clamp(v, 0f, 1f), 6)), v >= 0.35f ? Palette.Warning : Palette.Good);
        Gfx.Text(this, Fonts.Body, new Vector2(p.X, p.Y + 16), $"{label} {v * 100:0}%", Ui.TextMicro, Palette.TextDim);
    }

    private void MateGlyph(Vector2 c, string kind)
    {
        var col = kind switch { "정비" => new Color("#c8ced8"), "날씨" => new Color("#ffd166"), "주의" => new Color("#f4c430"), "일정" => new Color("#9fd8ff"), _ => new Color("#c9a26b") };
        switch (kind)
        {
            case "정비": DrawLine(c + new Vector2(-3, 3), c + new Vector2(2, -2), col, 1.4f, true); DrawArc(c + new Vector2(2.5f, -2.5f), 2f, 0.6f, 5.2f, 8, col, 1.2f, true); break;
            case "날씨": DrawCircle(c, 2.4f, col); break;
            case "주의": DrawPolyline(new[] { c + new Vector2(0, -4), c + new Vector2(4, 3), c + new Vector2(-4, 3), c + new Vector2(0, -4) }, col, 1.2f, true); break;
            case "일정": DrawArc(c, 3.5f, 0f, Mathf.Tau, 14, col, 1f, true); DrawLine(c, c + new Vector2(0, -2.5f), col, 1f, true); break;
            default: DrawRect(new Rect2(c + new Vector2(-3.5f, -3), new Vector2(7, 6)), col, false, 1f); break;
        }
    }
}
