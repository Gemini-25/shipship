using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v13.2 방침·회의 화면 (E): 왼쪽은 방침 26개(분야별 · 지금 값 · 정한 날과 찬반), 오른쪽은 최근 회의록(안건 · 발언 · 설득 · 표결 · 컴퓨터 권고)과
/// 이 배의 문화 · 파벌과 골 · 신용과 죄책감. 플레이어는 보기만 한다 (회의가 정한다).
/// </summary>
public partial class Hud
{
    public bool PolicyOpen { get; set; }
    private int _minutesIndex = -1; // -1 = 가장 최근

    public void TogglePolicy()
    {
        PolicyOpen = !PolicyOpen;
        _minutesIndex = -1;
        if (PolicyOpen) { ChronicleOpen = false; TechOpen = false; ControlOpen = false; OpenChain(null); }
    }

    private void CycleMinutes(int dir)
    {
        int n = _world.Meetings.Minutes.Count;
        if (n == 0) return;
        int cur = _minutesIndex < 0 ? n - 1 : _minutesIndex;
        _minutesIndex = Math.Clamp(cur + dir, 0, n - 1);
        if (_minutesIndex == n - 1) _minutesIndex = -1;
    }

    private static Color AreaColor(string area) => area switch
    {
        "재난" => new Color("#ff8a65"),
        "지휘" => new Color("#ffd54f"),
        "자원" => new Color("#81c784"),
        _ => new Color("#90a4ae"),
    };

    private void DrawPolicy(Vector2 mouse)
    {
        var w = _world;
        var mt = w.Meetings;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f + 64f + 10f;
        float wdt = Mathf.Min(1260f, Screen.X - RightColumnWidth - Margin * 3);
        float height = Screen.Y - y0 - LogFullHeight - Margin - 10f;
        var card = new Rect2(x0, y0, wdt, height);
        _chronicleRect = card;
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y0 + 30), "방침과 회의", Ui.TextLarge, Palette.Text);
        string culture = mt.Culture != "" ? $"이 배의 문화: {mt.Culture}" : "첫 출항 회의 전";
        var cap = w.Command.Captain;
        Gfx.Text(this, Fonts.Body, new Vector2(x + 110, y0 + 30), Fit($"{culture} · 함장 {cap?.Name ?? "-"} (신뢰 {w.Command.Trust * 100:0}%) · 정기 회의 {mt.Held}번 · 걸른 회의 {mt.Postponed} · 뒤집힌 표결 {mt.Flips} · 바뀐 방침 {w.Policies.Changes.Count}", right - x - 110 - 250, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextMuted);
        // v13.4 사기: 지금 값과 최근 일주일 (두 시간마다)
        {
            var soc = w.Society;
            float gx = right - 240, gw = 170, gy = y0 + 16;
            var mc = soc.Morale >= 0.6f ? Palette.Good : soc.Morale >= 0.4f ? Palette.Warning : Palette.Danger;
            Gfx.Text(this, Fonts.Bold, new Vector2(gx - 74, y0 + 30), $"사기 {soc.Morale * 100:0}%", Ui.TextBody, mc);
            Gfx.RoundRect(this, new Rect2(gx, gy, gw, 20), new Color(1, 1, 1, 0.04f), 4);
            var hist = soc.MoraleHistory;
            for (int i = 1; i < hist.Count; i++)
            {
                float x1 = gx + gw * (i - 1) / 83f, x2 = gx + gw * i / 83f;
                DrawLine(new Vector2(x1, gy + 20 - 20 * hist[i - 1]), new Vector2(x2, gy + 20 - 20 * hist[i]), mc.WithAlpha(0.8f), 1.5f, true);
            }
            Gfx.Text(this, Fonts.Body, new Vector2(gx, gy + 32), Fit($"{SocietySystem.MoraleName(soc.Morale)} — {soc.MoraleWhy}", gw + 70, Ui.TextMicro, Fonts.Body), Ui.TextMicro, Palette.TextMuted);
        }
        Button(new Rect2(right - 58, y0 + 12, 58, 26), "E 닫기", false, mouse, TogglePolicy, Ui.TextSmall);

        // ── 왼쪽 두 칸: 방침 (재난·지휘·자원 / 생활·사회·세대선·항해) ──
        float colW = Mathf.Min(370f, (right - x) * 0.31f);
        float colL = x;
        var columns = new[] { new[] { "재난", "지휘", "자원" }, new[] { "생활", "사회", "세대선", "항해" } };
        int maxRows = columns.Max(cs => PolicySystem.All.Count(p => cs.Contains(p.Area)));
        float rowH = Mathf.Clamp((height - 78f - 4 * 20f) / maxRows, 13f, 21f);
        for (int col = 0; col < columns.Length; col++)
        {
            float cx0 = x + col * (colW + 14);
            float ly = y0 + 52;
            foreach (var g in PolicySystem.All.Where(p => columns[col].Contains(p.Area)).GroupBy(p => p.Area))
            {
                var ac = AreaColor(g.Key);
                Gfx.Text(this, Fonts.Bold, new Vector2(cx0, ly + 14), g.Key, Ui.TextBody, ac);
                DrawLine(new Vector2(cx0 + 44, ly + 9), new Vector2(cx0 + colW, ly + 9), ac.WithAlpha(0.25f), 1f);
                ly += 18;
                foreach (var p in g)
                {
                    int cur = w.Policies[p.Id];
                    var last = w.Policies.Changes.LastOrDefault(c => c.Id == p.Id);
                    bool recent = last != null && w.Tick - last.Tick < SimTime.TicksPerDay;
                    var row = new Rect2(cx0, ly, colW, rowH);
                    bool hover = row.HasPoint(mouse);
                    if (hover) Gfx.RoundRect(this, row, new Color(1, 1, 1, 0.04f), 4);
                    Gfx.Text(this, Fonts.Body, new Vector2(cx0 + 4, ly + rowH - 4), p.Name, Ui.TextTiny, cur != p.Default ? Palette.Text : Palette.TextDim);
                    // 선택지 칩
                    float chx = cx0 + 84;
                    for (int i = 0; i < p.Options.Length; i++)
                    {
                        string label = p.Options[i];
                        float cw = Gfx.Width(Fonts.Body, label, Ui.TextMicro) + 8;
                        if (chx + cw > cx0 + colW) break;
                        bool on = i == cur;
                        var r = new Rect2(chx, ly + 1.5f, cw, rowH - 3);
                        Gfx.RoundRect(this, r, on ? ac.WithAlpha(recent ? 0.38f : 0.22f) : new Color(1, 1, 1, 0.025f), 4, on ? ac.WithAlpha(0.8f) : null, 1);
                        Gfx.Text(this, Fonts.Body, new Vector2(chx + 4, ly + rowH - 4.5f), label, Ui.TextMicro, on ? Palette.Text : Palette.TextMuted.WithAlpha(i == p.Default ? 0.9f : 0.6f));
                        chx += cw + 3;
                    }
                    if (hover)
                    {
                        // 설명 · 정한 날과 찬반 (마우스를 올리면 아래쪽에)
                        string hist = last != null ? $"{SimTime.Day(last.Tick)}일 {p.Options[last.From]} → {p.Options[last.To]}" + (last.Yes + last.No > 0 ? $" (찬성 {last.Yes} · 반대 {last.No})" : "") + $" — {last.Why}" : "처음 값 그대로";
                        _policyHover = $"{p.Name}: {p.Note}\n{hist}";
                    }
                    ly += rowH;
                }
                ly += 2;
            }
        }
        float colLW = colW * 2 + 14;
        if (_policyHover != null)
        {
            var lines = _policyHover.Split('\n');
            float hy = card.End.Y - 14 - 15 * lines.Length;
            Gfx.RoundRect(this, new Rect2(colL - 4, hy - 14, colLW + 8, 15 * lines.Length + 8), Ui.TooltipFill, Ui.RadiusChip, Ui.PanelEdge, 1); // v16.2 툴팁 면
            foreach (var l in lines)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(colL, hy), Fit(l, colLW, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Accent);
                hy += 15;
            }
            _policyHover = null;
        }

        // ── 오른쪽: 회의록 ──
        float rx = colL + colLW + 22, rw = right - rx;
        DrawLine(new Vector2(rx - 11, y0 + 50), new Vector2(rx - 11, card.End.Y - 14), Palette.PanelBorder, 1f);
        float ry = y0 + 52;
        int n = mt.Minutes.Count;
        int idx = _minutesIndex < 0 ? n - 1 : _minutesIndex;
        SectionTitle(rx, ry + 12, n == 0 ? "회의록 — 아직 없음" : $"회의록 {idx + 1}/{n}");
        Button(new Rect2(right - 76, ry - 2, 36, 20), "◀", false, mouse, () => CycleMinutes(-1), Ui.TextTiny);
        Button(new Rect2(right - 36, ry - 2, 36, 20), "▶", false, mouse, () => CycleMinutes(1), Ui.TextTiny);
        ry += 22;
        float minutesBottom = card.End.Y - 120;
        if (n > 0)
        {
            var rec = mt.Minutes[idx];
            var chair = w.Crew.FirstOrDefault(c => c.Id == rec.Chair);
            var kc = rec.Kind switch { MeetingKind.Review => Palette.Warning, MeetingKind.Field or MeetingKind.Emergency => Palette.Danger, MeetingKind.Maiden => Palette.Accent, _ => Palette.Good };
            Gfx.Text(this, Fonts.Bold, new Vector2(rx, ry + 14), MeetingSystem.KindName(rec.Kind), Ui.TextSubtitle, kc);
            Gfx.Text(this, Fonts.Body, new Vector2(rx + Gfx.Width(Fonts.Bold, MeetingSystem.KindName(rec.Kind), Ui.TextSubtitle) + 8, ry + 14),
                Fit($"{SimTime.Day(rec.Tick)}일 {SimTime.Clock(rec.Tick)} · {rec.Venue} · {rec.Attendees.Count}명 · 의장 {chair?.Name ?? "-"}", rw - 90, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextMuted);
            ry += 20;
            string who = string.Join("·", rec.Attendees.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "?"));
            Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 12), Fit("참석: " + who, rw, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
            ry += 18;
            bool open = w.Policies["minutes"] == 0;
            if (rec.Items.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 12), "안건 없음 — 하루를 돌아보고 흩어졌다", Ui.TextSmall, Palette.TextMuted); ry += 18; }
            foreach (var item in rec.Items)
            {
                if (ry > minutesBottom) { Gfx.Text(this, Fonts.Body, new Vector2(rx, ry + 10), "…", Ui.TextSmall, Palette.TextMuted); break; }
                var oc = item.Passed ? Palette.Good : Palette.Danger;
                Gfx.RoundRect(this, new Rect2(rx, ry + 2, rw, 18), oc.WithAlpha(0.08f), 4);
                Gfx.Text(this, Fonts.Bold, new Vector2(rx + 6, ry + 15), Fit(item.Title, rw - 150, Ui.TextBody, Fonts.Bold), Ui.TextBody, Palette.Text);
                Gfx.TextRight(this, Fonts.Bold, new Vector2(rx + rw - 6, ry + 15), $"찬성 {item.Yes} · 반대 {item.No} → {item.Outcome}", Ui.TextSmall, oc);
                ry += 22;
                if (item.Evidence != null) { Gfx.Text(this, Fonts.Body, new Vector2(rx + 8, ry + 11), Fit($"근거: {item.Evidence}", rw - 10, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Warning); ry += 14; }
                if (item.Computer != null) { Gfx.Text(this, Fonts.Body, new Vector2(rx + 8, ry + 11), Fit(item.Computer, rw - 10, Ui.TextTiny, Fonts.Body), Ui.TextTiny, new Color("#80deea")); ry += 14; }
                foreach (var sp in item.Speeches.Take(5))
                {
                    if (ry > minutesBottom) break;
                    var c = w.Crew.FirstOrDefault(x => x.Id == sp.Who);
                    string tag = sp.For ? "찬" : "반";
                    var tc = sp.For ? Palette.Good : Palette.Danger;
                    Gfx.Text(this, Fonts.Bold, new Vector2(rx + 8, ry + 11), tag, Ui.TextTiny, tc);
                    Gfx.Text(this, Fonts.Body, new Vector2(rx + 24, ry + 11),
                        Fit($"{c?.Name ?? "?"}{(c != null ? $" ({MeetingSystem.ValueName(c.Value)})" : "")}: “{sp.Text}”" + (sp.Moved > 0 ? $" → {sp.Moved}명이 마음을 바꿨다" : ""), rw - 26, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
                    ry += 14;
                }
                if (item.FlippedBy != null) { Gfx.Text(this, Fonts.Bold, new Vector2(rx + 8, ry + 11), $"↺ {item.FlippedBy}의 설득으로 뒤집혔다", Ui.TextTiny, Palette.Accent); ry += 14; }
                if (open && item.Votes.Count > 0)
                {
                    string ys = string.Join("·", item.Votes.Where(v => v.yes).Select(v => w.Crew.FirstOrDefault(c => c.Id == v.who)?.Name ?? "?"));
                    string ns = string.Join("·", item.Votes.Where(v => !v.yes).Select(v => w.Crew.FirstOrDefault(c => c.Id == v.who)?.Name ?? "?"));
                    Gfx.Text(this, Fonts.Body, new Vector2(rx + 8, ry + 11), Fit($"찬성 {(ys == "" ? "-" : ys)} / 반대 {(ns == "" ? "-" : ns)}", rw - 10, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
                    ry += 14;
                }
                else if (!open) { Gfx.Text(this, Fonts.Body, new Vector2(rx + 8, ry + 11), "누가 어디에 표를 던졌는지는 함장만 안다 (방침: 정보 공개)", Ui.TextTiny, Palette.TextMuted); ry += 14; }
                ry += 4;
            }
        }

        // ── 오른쪽 아래: 파벌 · 골 · 신용 · 죄책감 ──
        float fy = card.End.Y - 112;
        Divider(rx, right, fy);
        SectionTitle(rx, fy + 16, "파벌 (같은 가치관)");
        float fx = rx + 110;
        foreach (var (value, members) in mt.Factions().Take(4))
        {
            string label = $"{MeetingSystem.ValueName(value)} {string.Join("·", members.Select(m => m.Name))}";
            float lw = Mathf.Min(Gfx.Width(Fonts.Body, label, Ui.TextTiny) + 12, right - fx);
            if (lw < 40) break;
            Gfx.RoundRect(this, new Rect2(fx, fy + 4, lw, 17), new Color(1, 1, 1, 0.04f), 4, Palette.PanelBorder, 1);
            Gfx.Text(this, Fonts.Body, new Vector2(fx + 6, fy + 16), Fit(label, lw - 10, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
            fx += lw + 5;
        }
        var rifts = mt.Rifts().Take(3).Select(r => $"{MeetingSystem.ValueName(r.pair.a)}↔{MeetingSystem.ValueName(r.pair.b)} {r.tension * 100:0}%").ToList();
        Gfx.Text(this, Fonts.Body, new Vector2(rx, fy + 38), rifts.Count > 0 ? "골: " + string.Join(" · ", rifts) + " (깊을수록 서로 설득이 안 되고 말다툼이 잦다)" : "골: 아직 없다", Ui.TextTiny, rifts.Count > 0 ? Palette.Warning : Palette.TextMuted);
        var standing = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(c => mt.Credibility(c)).ToList();
        string cred = string.Join(" · ", standing.Where(c => MathF.Abs(mt.Credibility(c)) >= 0.02f).Take(5).Select(c => $"{c.Name} {(mt.Credibility(c) >= 0 ? "+" : "")}{mt.Credibility(c) * 100:0}"));
        Gfx.Text(this, Fonts.Body, new Vector2(rx, fy + 56), Fit("신용: " + (cred == "" ? "고르다" : cred), right - rx, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        string guilt = string.Join(" · ", standing.Where(c => mt.Guilt(c) > 0.05f).OrderByDescending(mt.Guilt).Take(4).Select(c => $"{c.Name} {mt.Guilt(c) * 100:0}%"));
        Gfx.Text(this, Fonts.Body, new Vector2(rx, fy + 74), Fit("죄책감: " + (guilt == "" ? "없다" : guilt) + $" · 비난 {mt.Blames}번 · 옳았던 결정 {mt.Praises}번", right - rx, Ui.TextTiny, Fonts.Body), Ui.TextTiny, guilt == "" ? Palette.TextMuted : Palette.Danger);
        string next = mt.Reviews.Count > 0 ? "다음 정기 회의 사후 검토: " + string.Join(" · ", mt.Reviews.Select(r => $"{PolicySystem.Spec(r.id).Name} → {PolicySystem.Spec(r.id).Options[r.to]}")) : $"다음 정기 회의: {mt.Hour:0}시 (일과가 가장 많이 겹치는 때)";
        Gfx.Text(this, Fonts.Body, new Vector2(rx, fy + 92), Fit(next, right - rx, Ui.TextTiny, Fonts.Body), Ui.TextTiny, mt.Reviews.Count > 0 ? Palette.Warning : Palette.TextMuted);
    }

    private string? _policyHover;
}
