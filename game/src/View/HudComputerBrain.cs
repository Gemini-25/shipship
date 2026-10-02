using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.16 주컴퓨터 두뇌 2.0 화면 (읽기만): 상시 카드에 네 줄 — 지금 계획(클립보드) · 가장 급한 예측(곡선 · 확신 부채꼴) · 권한(방패 네 기둥) · 최근 배운 것(갈래 그림).
/// 관제 화면 "두뇌" 탭: 자원 계획 · 고친 까닭 · 일정 · 위기 계획 / 권한 사다리 · 협상(안건 · 근거 갈래) · 윤리 갈등 · 실수와 사과(신뢰 곡선) · 승무원 모형 · 배운 것.
/// </summary>
public partial class Hud
{
    private const float BrainBlockH = 74f;

    private static string Short(AuthLevel l) => l switch { AuthLevel.Advise => "조언", AuthLevel.Propose => "제안", _ => "자동" };

    private AuthLevel[] BrainLevels(AutomationSystem a) => ComputerAuthority.Domains.Select(d => a.Authority.Level(d)).ToArray();

    /// <summary>상시 카드 안 두뇌 네 줄.</summary>
    private void DrawBrainBlock(float x, float y, float width)
    {
        var w = _world;
        var a = w.Automation;
        var pl = a.Planner;
        float row = 18f;
        // 계획
        var top = pl.Plans.Where(p => p.Mode != "유지").OrderByDescending(p => p.Mode == "위기" ? 3 : p.Mode == "대책" ? 2 : 1).FirstOrDefault();
        bool revised = pl.Revisions.Count > 0 && w.Tick - pl.Revisions[^1].Tick < SimTime.Hours(1);
        BrainIcons.PlanBoard(this, new Vector2(x + 7, y + 8), 6f, top?.Mode ?? "유지", revised, _time);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 20, y + 12), "계획", 10, BrainIcons.Plan);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 48, y + 12), Fit(pl.NowLine, width - 76, 10, Fonts.Body), 10, top != null ? BrainIcons.ModeColor(top.Mode) : Palette.TextDim);
        y += row;
        // 예측: 가장 급한 것
        var fc = a.Outlook.All.OrderBy(f => f.DaysToShort).FirstOrDefault();
        BrainIcons.Forecast(this, new Vector2(x + 7, y + 8), 6f, fc?.DaysToShort ?? 99f, fc?.Confidence ?? 0.5f, _time);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 20, y + 12), "예측", 10, BrainIcons.Fore);
        string fl = fc == null ? "재는 중" : fc.DaysToShort >= 30f ? "모든 자원 넉넉 — " + string.Join(" · ", a.Outlook.All.Take(3).Select(f => $"{f.Name} {f.Estimate:0}{f.Unit}")) : fc.Line;
        Gfx.Text(this, Fonts.Body, new Vector2(x + 48, y + 12), Fit(fl, width - 76, 10, Fonts.Body), 10, fc != null && fc.DaysToShort <= 4f ? Palette.Warning : Palette.TextDim);
        y += row;
        // 권한
        var lv = BrainLevels(a);
        BrainIcons.Authority(this, new Vector2(x + 7, y + 8), 7f, lv, _time);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 20, y + 12), "권한", 10, BrainIcons.Auth);
        string al = string.Join(" · ", ComputerAuthority.Domains.Select((d, i) => $"{ComputerAuthority.DomainName(d)} {Short(lv[i])}"));
        Gfx.Text(this, Fonts.Body, new Vector2(x + 48, y + 12), Fit(al + (a.Authority.Reviews.Count > 0 ? " · 회의 안건 있음" : ""), width - 76, 10, Fonts.Body), 10, Palette.TextDim);
        y += row;
        // 배움
        var ln = a.Authority.LearnedList.LastOrDefault();
        BrainIcons.Learned(this, new Vector2(x + 7, y + 8), 6f, ln?.Kind ?? "", ln != null && w.Tick - ln.Tick < SimTime.Hours(2), _time);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 20, y + 12), "배움", 10, BrainIcons.Learn);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 48, y + 12), Fit(ln?.Text ?? a.Authority.Persona, width - 76, 10, Fonts.Body), 10, Palette.TextDim);
    }

    /// <summary>관제 화면 "두뇌" 탭.</summary>
    private void DrawBrainTab(Rect2 card, float x, float right, float y)
    {
        var w = _world;
        var a = w.Automation;
        var pl = a.Planner;
        float colW = (right - x) / 2f - 10;
        float lx = x, ly = y, rx = x + colW + 20, ry = y;
        // ── 왼쪽: 자원 계획 ──
        SectionTitle(lx, ly + 10, $"자원 계획 — 다시 세움 {pl.Replans}번 · 고침 {pl.Revisions.Count} · 말투: {a.Authority.Persona}");
        ly += 18;
        foreach (var m in ShipForecast.Models)
        {
            var f = a.Outlook.Get(m.Key);
            var p = pl.PlanOf(m.Key);
            var L = a.Outlook.Ledger(m.Key);
            string mode = p?.Mode ?? "유지";
            var mc = BrainIcons.ModeColor(mode);
            Gfx.RoundRect(this, new Rect2(lx, ly + 2, 40, 14), mc.WithAlpha(0.18f), 4, mc);
            Gfx.TextCentered(this, Fonts.Bold, new Vector2(lx + 20, ly + 9), mode, 9, mc);
            Gfx.Text(this, Fonts.Bold, new Vector2(lx + 46, ly + 13), Fit(m.Name, 44, 10, Fonts.Bold), 10, Palette.Text);
            if (f != null)
            {
                BrainIcons.Forecast(this, new Vector2(lx + 102, ly + 9), 6f, f.DaysToShort, f.Confidence, _time);
                Gfx.Text(this, Fonts.Body, new Vector2(lx + 114, ly + 13), Fit(f.Line, colW - 220, 10, Fonts.Body), 10, f.DaysToShort <= 4f ? Palette.Warning : Palette.TextDim);
                // 계기 믿음 다이얼
                var dc = new Vector2(lx + colW - 96, ly + 10);
                DrawArc(dc, 6f, Mathf.Pi, Mathf.Tau, 12, new Color(1, 1, 1, 0.1f), 2f, true);
                DrawArc(dc, 6f, Mathf.Pi, Mathf.Pi + Mathf.Pi * L.Reliability, 12, L.Suspect ? Palette.Danger : Palette.Good, 2f, true);
                Gfx.Text(this, Fonts.Body, new Vector2(dc.X + 9, ly + 13), Fit(f.DeadReckon ? "흐름 셈" : f.Stale ? "낡은 값" : $"계기 {L.Reliability * 100:0}%", 80, 9, Fonts.Body), 9, L.Suspect ? Palette.Danger : Palette.TextMuted);
            }
            ly += 16;
            if (p != null && mode != "유지") { Gfx.Text(this, Fonts.Body, new Vector2(lx + 46, ly + 10), Fit($"→ {p.Action}", colW - 46, 10, Fonts.Body), 10, Palette.Text); ly += 14; }
        }
        ly += 6;
        SectionTitle(lx, ly + 10, "계획 고침 — 바뀐 까닭");
        ly += 16;
        foreach (var r in pl.Revisions.AsEnumerable().Reverse().Take(4))
        {
            BrainIcons.PlanBoard(this, new Vector2(lx + 6, ly + 7), 4.5f, r.To.Contains("소화조") ? "주의" : r.To, w.Tick - r.Tick < SimTime.Hours(1), _time);
            Gfx.Text(this, Fonts.Body, new Vector2(lx + 16, ly + 11), Fit($"{SimTime.Clock(r.Tick)} {(r.Key == "crisis" ? "위기 계획" : ShipForecast.Models.FirstOrDefault(m => m.Key == r.Key)?.Name)} {r.From} → {r.To} · {r.Why}", colW - 16, 10, Fonts.Body), 10, Palette.TextDim);
            ly += 14;
        }
        ly += 6;
        SectionTitle(lx, ly + 10, $"일정 · 위기 계획 (소화조 {string.Join("·", pl.Emergency.FireTeam.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "?"))} · 대피 {pl.Emergency.Shelter})");
        ly += 16;
        foreach (var s in pl.Schedule.Take(6))
        {
            if (ly > card.End.Y - 20) break;
            var col = s.Kind switch { "정비" => new Color("#e8b84a"), "쉼" => BrainIcons.Rest, "당번" => Palette.Accent, "교정" => Palette.Danger, "회의" => BrainIcons.Auth, _ => Palette.TextDim };
            DrawRect(new Rect2(lx, ly + 3, 4, 10), col);
            string who = s.CrewId >= 0 ? w.Crew.FirstOrDefault(c => c.Id == s.CrewId)?.Name ?? "" : "";
            Gfx.Text(this, Fonts.Body, new Vector2(lx + 8, ly + 12), Fit($"[{s.Kind}] {s.What}" + (who != "" ? $" — {who}" : ""), colW - 8, 10, Fonts.Body), 10, Palette.Text);
            ly += 14;
        }
        // ── 오른쪽: 권한 · 협상 · 윤리 · 실수 · 승무원 모형 · 배움 ──
        SectionTitle(rx, ry + 10, "권한 — 승무원 회의가 주고 거둔다");
        ry += 18;
        var lv = BrainLevels(a);
        for (int i = 0; i < ComputerAuthority.Domains.Length; i++)
        {
            var d = ComputerAuthority.Domains[i];
            Gfx.Text(this, Fonts.Bold, new Vector2(rx, ry + 12), ComputerAuthority.DomainName(d), 10, Palette.Text);
            for (int k = 0; k < 3; k++)
            {
                var seg = new Rect2(rx + 34 + k * 46, ry + 3, 42, 12);
                bool on = (int)lv[i] == k;
                Gfx.RoundRect(this, seg, on ? BrainIcons.Auth.WithAlpha(0.35f) : new Color(1, 1, 1, 0.04f), 3, on ? BrainIcons.Auth : new Color(1, 1, 1, 0.08f));
                Gfx.TextCentered(this, Fonts.Body, seg.GetCenter(), Short((AuthLevel)k), 9, on ? Palette.Text : Palette.TextMuted);
            }
            if (a.Authority.Granted0(d) != lv[i]) Gfx.Text(this, Fonts.Body, new Vector2(rx + 176, ry + 12), "(컴퓨터가 상해 조언만)", 9, Palette.Danger);
            ry += 16;
        }
        foreach (var ch in a.Authority.Changes.AsEnumerable().Reverse().Take(2))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit($"{SimTime.Day(ch.Tick)}일 {ComputerAuthority.DomainName(ch.Domain)} {Short(ch.From)} → {Short(ch.To)} (찬 {ch.Yes} · 반 {ch.No}) · {ch.Why}", colW, 10, Fonts.Body), 10, Palette.TextDim);
            ry += 13;
        }
        ry += 6;
        SectionTitle(rx, ry + 10, "협상 — 근거를 바꿔 다시 (세 번까지)");
        ry += 16;
        foreach (var p in pl.Pitches.AsEnumerable().Reverse().Take(4))
        {
            var col = p.State is "통과" or "받음" or "실행" ? Palette.Good : p.State is "부결" or "거절" ? Palette.Danger : p.Open ? Palette.Warning : Palette.TextDim;
            for (int k = 0; k < ShipPlanner.MaxAttempts; k++) DrawCircle(new Vector2(rx + 4 + k * 7, ry + 8), 2.4f, k < p.Attempt ? col : new Color(1, 1, 1, 0.1f), true, -1f, true);
            Gfx.Text(this, Fonts.Body, new Vector2(rx + 26, ry + 11), Fit($"{p.Option} [{p.Arg} · {p.Via}] {p.State}" + (p.Yes + p.No > 0 ? $" (찬 {p.Yes} · 반 {p.No})" : "") + $" — {p.Basis}", colW - 26, 10, Fonts.Body), 10, col);
            ry += 14;
        }
        ry += 6;
        SectionTitle(rx, ry + 10, $"윤리 갈등 · 실수와 사과 (사과 {a.Authority.Apologies})");
        ry += 16;
        foreach (var e in a.Authority.Ethics.AsEnumerable().Reverse().Take(2))
        {
            BrainIcons.Learned(this, new Vector2(rx + 6, ry + 7), 5f, "윤리", false, _time);
            Gfx.Text(this, Fonts.Body, new Vector2(rx + 16, ry + 11), Fit($"{e.Kind} — {e.Situation} · {e.Choice}", colW - 16, 10, Fonts.Body), 10, Palette.TextDim);
            ry += 14;
        }
        foreach (var mk in a.Authority.Mistakes.AsEnumerable().Reverse().Take(2))
        {
            BrainIcons.Learned(this, new Vector2(rx + 6, ry + 7), 5f, "실수", false, _time);
            // 신뢰 곡선: 전 → 틀림 → 사과 뒤
            var p0 = new Vector2(rx + 16, ry + 12 - mk.TrustBefore * 10);
            var p1 = new Vector2(rx + 26, ry + 12 - mk.TrustLow * 10);
            var p2 = new Vector2(rx + 36, ry + 12 - mk.TrustAfter * 10);
            DrawPolyline(new[] { p0, p1, p2 }, Palette.Warning, 1.2f, true);
            Gfx.Text(this, Fonts.Body, new Vector2(rx + 42, ry + 11), Fit($"{mk.What} · {mk.TrustBefore * 100:0}→{mk.TrustLow * 100:0}→{mk.TrustAfter * 100:0}%", colW - 42, 10, Fonts.Body), 10, Palette.Warning);
            ry += 14;
        }
        ry += 6;
        SectionTitle(rx, ry + 10, "승무원 모형 — 관찰로 배운 짐작 (틀릴 수 있다)");
        ry += 16;
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => a.CrewModel.RestNow(c)).Take(5))
        {
            if (ry > card.End.Y - 60) break;
            Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 11), Fit(c.Name, 52, 10, Fonts.Body), 10, Palette.Crew(c.Id));
            Gfx.Bar(this, new Rect2(rx + 54, ry + 4, 40, 6), a.CrewModel.RestNow(c), a.CrewModel.RestNow(c) < 0.3f ? Palette.Danger : Palette.NeedRest);
            if (a.CrewModel.RestAsked(c)) BrainIcons.RestChip(this, new Vector2(rx + 102, ry + 7), 5f, _time);
            else if (a.CrewModel.Asks.ContainsKey(c.Id)) BrainIcons.AskChip(this, new Vector2(rx + 102, ry + 7), 5f, _time);
            Gfx.Text(this, Fonts.Body, new Vector2(rx + 112, ry + 11), Fit(a.CrewModel.Summary(c), colW - 112, 10, Fonts.Body), 10, Palette.TextDim);
            ry += 13;
        }
        ry += 6;
        SectionTitle(rx, ry + 10, "최근 배운 것");
        ry += 16;
        foreach (var l in a.Authority.LearnedList.AsEnumerable().Reverse())
        {
            if (ry > card.End.Y - 16) break;
            BrainIcons.Learned(this, new Vector2(rx + 6, ry + 7), 5f, l.Kind, w.Tick - l.Tick < SimTime.Hours(2), _time);
            Gfx.Text(this, Fonts.Body, new Vector2(rx + 16, ry + 11), Fit($"{SimTime.Clock(l.Tick)} {l.Text}", colW - 16, 10, Fonts.Body), 10, Palette.TextDim);
            ry += 14;
        }
    }
}
