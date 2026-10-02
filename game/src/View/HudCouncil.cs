using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v18.18 회의록 창 (⇧E · 어두운 유리): 서명을 받는 안건(종이 몇 장이 모였나) · 지금/지난 회의의 발언과 표결(재판 · 선거 · 비밀 투표 뒤 짐작) ·
/// 파벌(색과 무늬 · 누가 · 이기고 진 수) · 진 쪽의 불만과 벌 근무. 보기만 한다 (정하는 건 승무원이다).
/// </summary>
public partial class Hud
{
    public bool CouncilOpen { get; set; }
    private int _sittingIndex = -1;

    public void ToggleCouncil()
    {
        CouncilOpen = !CouncilOpen;
        _sittingIndex = -1;
        if (CouncilOpen) { PolicyOpen = false; ChronicleOpen = false; TechOpen = false; ControlOpen = false; OpenChain(null); }
    }

    private string Who(int id) => id < 0 ? "주 컴퓨터" : id < _world.Crew.Count ? _world.Crew[id].Name : "?";
    private Color WhoColor(int id) => id < 0 ? Palette.Accent : Palette.Crew(id);

    private static string RoleTag(LineRole r) => r switch
    {
        LineRole.Chair => "의장", LineRole.Accuser => "고발", LineRole.Witness => "증언", LineRole.Defense => "변론",
        LineRole.Computer => "기록", LineRole.Candidate => "연설", LineRole.Hearsay => "들은 말", _ => "",
    };

    private void DrawCouncil(Vector2 mouse)
    {
        var w = _world;
        var mo = w.Motions;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f + 64f + 10f;
        float wdt = Mathf.Min(1260f, Screen.X - RightColumnWidth - Margin * 3);
        float height = Screen.Y - y0 - LogFullHeight - Margin - 10f;
        var card = new Rect2(x0, y0, wdt, height);
        _chronicleRect = card;
        UiKit.Panel(this, card);
        float x = x0 + 18, right = card.End.X - 18;
        int open = mo.Open.Count(), live = mo.Factions.Count(f => !f.Gone), grudges = mo.Grudges.Count(g => g.Until > w.Tick);
        UiKit.CardTitle(this, x, right - 40, y0 + 30, "회의록", $"서명 받는 중 {open} · 파벌 {live} · 앙금 {grudges} · 선장 {w.Command.Captain?.Name ?? "-"}");
        Button(new Rect2(right - 24, y0 + 12, 22, 20), "×", false, mouse, () => CouncilOpen = false, 11);
        float top = y0 + 52, bottom = card.End.Y - 14;
        float colA = x, colB = x + (right - x) * 0.31f, colC = x + (right - x) * 0.71f;
        DrawMotionsColumn(colA, colB - 16, top, bottom);
        DrawSittingColumn(colB, colC - 16, top, bottom, mouse);
        DrawFactionColumn(colC, right, top, bottom);
    }

    private void DrawMotionsColumn(float x, float right, float y, float bottom)
    {
        var w = _world;
        var mo = w.Motions;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 4), "서명을 받는 안건", Ui.TextSmall, Palette.TextDim);
        y += 16;
        var open = mo.Open.OrderByDescending(m => m.Born).Take(5).ToList();
        if (open.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 14), "지금 돌고 있는 종이는 없다", Ui.TextBody, Palette.TextMuted); y += 24; }
        foreach (var m in open)
        {
            if (y > bottom - 70) break;
            var r = new Rect2(x, y, right - x, 64);
            Gfx.RoundRect(this, r, UiKit.Well, Ui.RadiusControl, new Color(1, 1, 1, 0.06f));
            UiKit.Chip(this, new Vector2(x + 8, y + 6), MotionSystem.KindName(m.Kind), false, false);
            float cx = x + 14 + UiKit.ChipWidth(MotionSystem.KindName(m.Kind));
            Gfx.Text(this, Fonts.Bold, new Vector2(cx, y + 19), UiKit.Fit(m.Title, right - cx - 8, Ui.TextBody, Fonts.Bold), Ui.TextBody, Palette.Text);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 10, y + 37), UiKit.Fit($"{Who(m.Proposer)}: “{m.Why}”", right - x - 20, Ui.TextSmall), Ui.TextSmall, WhoColor(m.Proposer).Lerp(Palette.Text, 0.4f));
            // 서명 종이: 모인 장은 서명이 적혀 있고, 남은 장은 빈 종이
            int need = Math.Min(m.Need, 12);
            for (int i = 0; i < need; i++)
            {
                var p = new Vector2(x + 10 + i * 13, y + 44);
                bool signed = i < m.Signers.Count;
                DrawRect(new Rect2(p, 10, 13), signed ? new Color(0.93f, 0.91f, 0.84f) : new Color(0.93f, 0.91f, 0.84f, 0.15f));
                if (signed) DrawPolyline(new[] { p + new Vector2(2, 9), p + new Vector2(4, 7), p + new Vector2(6, 10), p + new Vector2(8, 7) }, new Color(0.15f, 0.3f, 0.7f), 1f);
                else DrawRect(new Rect2(p, 10, 13), new Color(0.93f, 0.91f, 0.84f, 0.4f), false, 1f);
            }
            string state = m.Stage == MotionStage.Ready ? (m.Sitting == SittingKind.Regular ? "다음 정기 회의에" : $"{MotionSystem.SittingName(m.Sitting)} 소집") : $"{SimTime.Day(m.Deadline)}일 {SimTime.Clock(m.Deadline)}까지";
            Gfx.TextRight(this, Fonts.Body, new Vector2(right - 8, y + 56), $"{m.Signers.Count}/{m.Need} · {state}", Ui.TextTiny, Palette.TextMuted);
            y += 70;
        }
        y += 6;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 4), "정해진 안건", Ui.TextSmall, Palette.TextDim);
        y += 12;
        foreach (var m in mo.All.Where(m => m.Decided >= 0).OrderByDescending(m => m.Decided).Take(8))
        {
            if (y > bottom - 34) break;
            var col = m.Passed ? Palette.Good : Palette.TextMuted;
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 14), UiKit.Fit($"{SimTime.Day(m.Decided)}일 · {m.Title}", right - x, Ui.TextSmall), Ui.TextSmall, Palette.Text);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 10, y + 28), UiKit.Fit($"→ {m.Outcome}", right - x - 10, Ui.TextTiny), Ui.TextTiny, col);
            if (m.Item is AgendaItem it && it.Yes + it.No > 0)
            {
                float bw = 60, bx = right - bw;
                DrawRect(new Rect2(bx, y + 22, bw, 4), new Color(1, 1, 1, 0.08f));
                DrawRect(new Rect2(bx, y + 22, bw * it.Yes / (it.Yes + it.No), 4), m.Kind == MotionKind.Accusation ? new Color(1f, 0.6f, 0.35f) : Palette.Good);
            }
            y += 34;
        }
    }

    private void DrawSittingColumn(float x, float right, float y, float bottom, Vector2 mouse)
    {
        var w = _world;
        var mo = w.Motions;
        var list = mo.Past.ToList();
        if (mo.Now != null) list.Add(mo.Now);
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 4), "회의 자리", Ui.TextSmall, Palette.TextDim);
        if (list.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 30), "아직 따로 모인 적이 없다 — 안건은 정기 회의에서도 정해진다", Ui.TextBody, Palette.TextMuted); return; }
        int idx = _sittingIndex < 0 || _sittingIndex >= list.Count ? list.Count - 1 : _sittingIndex;
        Button(new Rect2(right - 52, y - 8, 22, 18), "‹", false, mouse, () => _sittingIndex = Math.Max(0, idx - 1), 11);
        Button(new Rect2(right - 26, y - 8, 22, 18), "›", false, mouse, () => _sittingIndex = idx + 1 >= list.Count - 1 ? -1 : idx + 1, 11);
        var s = list[idx];
        var m = s.Motion;
        y += 22;
        string when = s.Opened >= 0 ? $"{SimTime.Day(s.Opened)}일 {SimTime.Clock(s.Opened)}" : "모이는 중";
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 6), UiKit.Fit($"{MotionSystem.SittingName(s.Kind)} — {m.Title}", right - x, Ui.TextLabel, Fonts.Bold), Ui.TextLabel, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 22), UiKit.Fit($"{when} · {s.Venue.Name} · {s.Present.Count}명 · 의장 {(s.Chair >= 0 ? Who(s.Chair) : "-")} · {(m.Secret ? "비밀 투표" : "손 들기")}", right - x, Ui.TextSmall), Ui.TextSmall, Palette.TextMuted);
        y += 32;
        // 지금 말하는 줄까지만 (끝난 회의는 전부)
        int shown = s == mo.Now ? (mo.Speaking() is var (_, k) ? k + 1 : s.Opened >= 0 ? s.Script.Count : 0) : s.Script.Count;
        foreach (var line in s.Script.Take(shown).TakeLast(9))
        {
            if (y > bottom - 90) break;
            string tag = RoleTag(line.Role);
            y += UiKit.Bubble(this, x, right, y, tag != "" ? $"{Who(line.Who)} · {tag}" : Who(line.Who), line.Text, false, WhoColor(line.Who), 2);
        }
        if (s.Opened < 0 || s == mo.Now && mo.Voting <= 0f && s.End > w.Tick && mo.Speaking() != null) return;
        // 표결
        y += 4;
        var it = m.Item;
        if (it != null)
        {
            string result = m.Kind == MotionKind.Accusation ? $"벌: {m.Outcome}" : m.Outcome;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 12), UiKit.Fit(result, right - x, Ui.TextBody, Fonts.Bold), Ui.TextBody, m.Passed ? Palette.Good : Palette.Warning);
            y += 18;
            int n = Math.Max(1, it.Yes + it.No);
            DrawRect(new Rect2(x, y, right - x, 6), new Color(1, 1, 1, 0.08f));
            DrawRect(new Rect2(x, y, (right - x) * it.Yes / n, 6), m.Kind == MotionKind.Accusation ? new Color(1f, 0.6f, 0.35f) : Palette.Good);
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 18), m.Kind == MotionKind.Accusation ? $"엄하게 {it.Yes} · 너그럽게 {it.No}" : $"찬성 {it.Yes} · 반대 {it.No}" + (it.FlippedBy != null ? $" · {it.FlippedBy}의 말에 뒤집혔다" : ""), Ui.TextTiny, Palette.TextMuted);
            y += 26;
        }
        // 비밀 투표 뒤: 누가 누구를 저쪽이라 짐작하나 (틀린 짐작은 흐리게)
        foreach (var g in m.Guesses.Where(g => g.thinksAgainst).Take(4))
        {
            if (y > bottom - 14) break;
            bool wrong = !g.truth;
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), UiKit.Fit($"{Ko.EunNeun(Who(g.who))} {Ko.IGa(Who(g.about))} 저쪽에 넣었다고 생각한다", right - x, Ui.TextTiny), Ui.TextTiny, wrong ? Palette.TextMuted.WithAlpha(0.6f) : Palette.TextDim);
            y += 15;
        }
    }

    private void DrawFactionColumn(float x, float right, float y, float bottom)
    {
        var w = _world;
        var mo = w.Motions;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 4), "파벌", Ui.TextSmall, Palette.TextDim);
        y += 14;
        var fs = mo.Factions.OrderBy(f => f.Gone ? 1 : 0).ThenByDescending(f => f.Last).Take(7).ToList();
        if (fs.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 14), "아직 갈라선 적이 없다", Ui.TextBody, Palette.TextMuted); y += 24; }
        foreach (var f in fs)
        {
            if (y > bottom - 120) break;
            var col = ShipView.FactionColor(f);
            if (f.Gone) col = col.Lerp(Palette.TextMuted, 0.6f);
            DrawRect(new Rect2(x, y + 4, 3, 30), col);
            ShipView.FactionGlyph(this, f, new Vector2(x + 14, y + 12), 5f, col);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 26, y + 16), f.Name, Ui.TextBody, f.Gone ? Palette.TextMuted : Palette.Text);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, y + 16), f.Gone ? "흩어짐" : $"이김 {f.Wins} · 짐 {f.Losses}", Ui.TextTiny, Palette.TextMuted);
            string members = string.Join("·", f.Members.Select(Who));
            Gfx.Text(this, Fonts.Body, new Vector2(x + 10, y + 32), UiKit.Fit(f.Gone ? $"{members} — {f.GoneWhy}" : members, right - x - 10, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
            y += 40;
        }
        y += 6;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 4), "진 쪽의 앙금", Ui.TextSmall, Palette.TextDim);
        y += 10;
        foreach (var g in mo.Grudges.Where(g => g.Until > w.Tick).OrderByDescending(g => g.Since).Take(6))
        {
            if (y > bottom - 50) break;
            float days = (g.Until - w.Tick) / (float)SimTime.TicksPerDay;
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 14), UiKit.Fit($"{Who(g.Who)} — {g.Why}", right - x - 50, Ui.TextSmall), Ui.TextSmall, WhoColor(g.Who).Lerp(Palette.Text, 0.5f));
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, y + 14), $"{days:0.#}일", Ui.TextTiny, Palette.TextMuted);
            y += 18;
        }
        if (mo.Duty.Count > 0 && y < bottom - 30)
        {
            y += 8;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 4), "벌 근무", Ui.TextSmall, Palette.TextDim);
            y += 8;
            foreach (var (id, d) in mo.Duty.OrderBy(kv => kv.Key))
            {
                if (y > bottom - 16) break;
                Gfx.Text(this, Fonts.Body, new Vector2(x, y + 14), $"{Who(id)} — {d.hours:0.#}시간 남음 · {SimTime.Day(d.due)}일 {SimTime.Clock(d.due)}까지", Ui.TextSmall, Palette.Text);
                y += 18;
            }
        }
    }
}
