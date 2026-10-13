using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v23 기술표 (F5 기술 탭의 첫 쪽): 함장이 "기술이 발전하는지"를 한눈에 본다.
/// · 위: 시대 사다리 일곱 칸 — 지나온 시대 · 지금 시대(여덟 중 몇) · 마지막 칸은 항해의 목표.
/// · 가운데: 줄기 여덟 × 시대 일곱 칸 — 익힘(채움) · 연구 중(진척 막대) · 열림(테) · 앞 시대(흐림) · 잊음(붉은 테) · 함장 지시(별).
/// · 아래: 곁가지 — 익힌 것 · 보이는 것 · "?"(마우스를 대면 실마리).
/// · 오른쪽: 고른 칸의 설명 — 말 · 전후 숫자 · 설치할 설비 · 갈래(함장이 뒤집을 수 있다) · 값과 날수 · 아는 사람 · [우선 연구].
/// 연구는 회의 · 주 컴퓨터가 고르고, 함장은 "이것부터" 하나를 누를 수 있다.
/// </summary>
public partial class Hud
{
    private string? _treeSel;

    public static Color BranchColor(Branch b) => b switch
    {
        Branch.Power => new Color("#f2c94c"), Branch.Life => new Color("#5ec8e6"), Branch.Food => new Color("#8fd65a"), Branch.Medical => new Color("#ff8a95"),
        Branch.Hull => new Color("#b8c2d3"), Branch.Machines => new Color("#7cc4ff"), Branch.Navigation => new Color("#c58cff"), _ => new Color("#e0a15e"),
    };

    /// <summary>하루 연구 (작업대 · 솜씨 좋은 사람 · 연구 노트).</summary>
    private float TreePerDay => Tech.ResearchPerHour(_world) * ErasV15.Mul(_world, "research") * 24f;

    private void DrawTechTree(Vector2 mouse)
    {
        var w = _world;
        var e = w.Eras;
        float x0 = WinX, y0 = WinY, width = WinW, height = WinBottom - y0;
        var card = new Rect2(x0, y0, width, height);
        _chronicleRect = card;
        Card(card);
        float left = x0 + 16f, right = card.End.X - 16f;
        string ident = w.TechWeb.Epithets.Count > 0 ? " · " + string.Join(" · ", w.TechWeb.Epithets.Take(3)) : "";
        UiKit.CardTitle(this, left, right - 190f, y0 + 30, $"{w.Ship.Name} 기술",
            $"{TechTree.Roman(e.Era)} {TechTree.EraName(e.Era)} · 본 기술 {e.MainKnown}/56 · 곁가지 {e.SideKnown}/23{ident}", "star");
        Button(new Rect2(right - 58, y0 + 10, 58, 26), "닫기", false, mouse, ToggleTech, Ui.TextSmall);
        Button(new Rect2(right - 58 - 96, y0 + 10, 90, 26), "설비 단계", false, mouse, () => TechWebOpen = true, Ui.TextSmall);

        float y = y0 + 50f;
        DrawEraLadder(new Rect2(left, y, right - left, 58f), mouse);
        y += 66f;
        DrawNowLine(left, right, y, mouse);
        y += 34f;

        bool side = width >= 760f;
        float panelW = side ? MathF.Min(320f, width * 0.34f) : 0f;
        float gridRight = side ? right - panelW - 14f : right;
        float chipsH = 64f;
        float gridH = MathF.Max(200f, card.End.Y - 12f - y - chipsH - (side ? 0f : 230f));
        var grid = new Rect2(left, y, gridRight - left, gridH);
        DrawTreeGrid(grid, mouse);
        DrawSideChips(new Rect2(left, grid.End.Y + 8f, gridRight - left, chipsH - 8f), mouse);
        var sel = TechTree.Find(_treeSel) ?? e.ProjectTech ?? e.Available.FirstOrDefault() ?? TechTree.Main[0];
        var panel = side ? new Rect2(gridRight + 14f, y, panelW, card.End.Y - 12f - y) : new Rect2(left, grid.End.Y + chipsH + 4f, right - left, card.End.Y - 12f - grid.End.Y - chipsH - 4f);
        DrawTreeDetail(panel, sel, mouse);
    }

    /// <summary>시대 사다리: 계단처럼 오른다 — 지나온 칸은 채움, 지금 칸은 여덟 중 몇, 마지막 칸은 항해의 목표.</summary>
    private void DrawEraLadder(Rect2 r, Vector2 mouse)
    {
        var e = _world.Eras;
        float gap = 6f, stepW = (r.Size.X - gap * 6f) / 7f;
        for (int i = 1; i <= 7; i++)
        {
            float h = 30f + 4f * i;
            var s = new Rect2(r.Position.X + (i - 1) * (stepW + gap), r.End.Y - h, stepW, h);
            int n = e.LearnedIn(i);
            bool done = i < e.Era || i == 7 && e.Complete, now = i == e.Era && !e.Complete;
            var fill = done ? Palette.Good.WithAlpha(0.20f) : now ? Palette.Accent.WithAlpha(0.10f) : new Color(1, 1, 1, 0.03f);
            Gfx.RoundRect(this, s, fill, Ui.RadiusChip, done ? Palette.Good.WithAlpha(0.55f) : now ? Palette.Accent : new Color(1, 1, 1, 0.08f), now ? 2 : 1);
            if (now)
            {
                float f = n / 8f;
                Gfx.RoundRect(this, new Rect2(s.Position.X + 3, s.End.Y - 6, (s.Size.X - 6) * f, 3), Palette.Accent, 1.5f);
            }
            var tc = done ? Palette.Good : now ? Palette.Accent : Palette.TextMuted;
            Gfx.Text(this, Fonts.Bold, new Vector2(s.Position.X + 6, s.Position.Y + 15), TechTree.Roman(i), Ui.TextLabel, tc);
            string name = TechTree.EraName(i);
            var lines = UiKit.Wrap(name, stepW - 10f, Ui.TextTiny, Fonts.Body, 2);
            for (int k = 0; k < lines.Count; k++)
                Gfx.Text(this, Fonts.Body, new Vector2(s.Position.X + 6, s.Position.Y + 28 + k * 11), lines[k], Ui.TextTiny, done || now ? Palette.Text : Palette.TextMuted);
            Gfx.TextRight(this, Fonts.Bold, new Vector2(s.End.X - 5, s.Position.Y + 15), i == 7 ? (e.Complete ? "목표 ✓" : $"목표 {n}/8") : $"{n}/8", Ui.TextTiny, tc);
            if (s.HasPoint(mouse))
            {
                var tl = TechTree.Main.Where(t => t.Era == i).Select(t => new TipLine((e.Known.Contains(t.Id) ? "✓ " : "· ") + t.Name, e.Known.Contains(t.Id) ? Palette.Good : Palette.TextDim)).ToList();
                if (i == 7) tl.Add(new TipLine("마지막 시대의 여덟 기술 = 항해의 목표", Palette.Accent));
                _tip = () => UiKit.Tooltip(this, new Vector2(s.GetCenter().X, s.End.Y), Screen, $"{TechTree.Roman(i)} {name}", tl);
            }
        }
    }

    /// <summary>지금 연구 한 줄: 무엇 · 몇 % · 며칠 · 왜.</summary>
    private void DrawNowLine(float left, float right, float y, Vector2 mouse)
    {
        var e = _world.Eras;
        var pt = e.ProjectTech;
        var bar = new Rect2(left, y + 18f, right - left, 6f);
        Gfx.RoundRect(this, bar, new Color(1, 1, 1, 0.06f), 3);
        if (pt == null) { Gfx.Text(this, Fonts.Body, new Vector2(left, y + 12), e.Complete ? "모든 기술을 익혔다 — 항해의 목표에 닿았다" : "지금 연구할 것이 없다", Ui.TextBody, Palette.TextDim); return; }
        Gfx.RoundRect(this, new Rect2(bar.Position, new Vector2(bar.Size.X * e.Fraction, bar.Size.Y)), BranchColor(pt.Branch), 3);
        float perDay = TreePerDay;
        float left2 = e.CostOf(pt) - e.Progress;
        string eta = perDay > 0.05f ? $" · 약 {MathF.Max(0.1f, left2 / perDay):0.#}일" : "";
        string head = $"지금 연구 — {pt.Name} {e.Fraction * 100f:0}%{eta}";
        Gfx.Text(this, Fonts.Bold, new Vector2(left, y + 12), head, Ui.TextBody, Palette.Text);
        float hx = left + Gfx.Width(Fonts.Bold, head, Ui.TextBody) + 10f;
        string why = (e.Priority == pt.Id ? "★ " : "") + e.ProjectWhy;
        var wl = UiKit.Wrap(why, MathF.Max(60f, right - hx), Ui.TextSmall, Fonts.Body, 1);
        if (wl.Count > 0) Gfx.Text(this, Fonts.Body, new Vector2(hx, y + 12), wl[0], Ui.TextSmall, Palette.TextDim);
        var hit = new Rect2(left, y, right - left, 26f);
        if (hit.HasPoint(mouse)) _tip = () => UiKit.Tooltip(this, mouse, Screen, "왜 이 연구인가", new List<TipLine> { new(why, Palette.Text), new($"하루 연구 {perDay:0.0} · 남은 값 {left2:0}", Palette.TextDim) });
    }

    private void DrawTreeGrid(Rect2 g, Vector2 mouse)
    {
        var e = _world.Eras;
        const float labelW = 88f, headH = 18f;
        float cw = (g.Size.X - labelW) / 7f, ch = MathF.Min(58f, (g.Size.Y - headH) / 8f);
        // 지금 시대 기둥
        var col = new Rect2(g.Position.X + labelW + (e.Era - 1) * cw, g.Position.Y, cw, headH + ch * 8f);
        Gfx.RoundRect(this, col, Palette.Accent.WithAlpha(0.05f), Ui.RadiusChip);
        for (int i = 1; i <= 7; i++)
            Gfx.TextCentered(this, Fonts.Bold, new Vector2(g.Position.X + labelW + (i - 0.5f) * cw, g.Position.Y + 9), TechTree.Roman(i), Ui.TextSmall, i == e.Era ? Palette.Accent : i < e.Era ? Palette.Good : Palette.TextMuted);
        foreach (var b in Enum.GetValues<Branch>())
        {
            float ry = g.Position.Y + headH + (int)b * ch;
            var bc = BranchColor(b);
            Gfx.RoundRect(this, new Rect2(g.Position.X, ry + 4, 3, ch - 8), bc, 1.5f);
            var bl = UiKit.Wrap(TechTree.BranchName(b), labelW - 12f, Ui.TextSmall, Fonts.Bold, 2);
            for (int k = 0; k < bl.Count; k++)
                Gfx.Text(this, Fonts.Bold, new Vector2(g.Position.X + 8, ry + ch * 0.5f - (bl.Count - 1) * 6.5f + k * 13 + 4), bl[k], Ui.TextSmall, bc);
            foreach (var t in TechTree.Main)
            {
                if (t.Branch != b) continue;
                var c = new Rect2(g.Position.X + labelW + (t.Era - 1) * cw + 2f, ry + 2f, cw - 4f, ch - 4f);
                DrawTreeCell(c, t, mouse);
            }
        }
    }

    private void DrawTreeCell(Rect2 c, TreeTech t, Vector2 mouse)
    {
        var e = _world.Eras;
        bool known = e.Known.Contains(t.Id), now = e.Project == t.Id, lost = e.Lost.Contains(t.Id), future = t.Era > e.Era;
        bool open = !known && !future;
        bool sel = (_treeSel ?? e.Project) == t.Id, hover = c.HasPoint(mouse);
        var bc = BranchColor(t.Branch);
        var fill = known ? bc.WithAlpha(0.22f) : now ? bc.WithAlpha(0.10f) : hover ? Ui.Hover : new Color(1, 1, 1, future ? 0.015f : 0.035f);
        Color? border = lost ? Palette.Danger : now ? Palette.Accent : sel ? Palette.Text.WithAlpha(0.6f) : open ? bc.WithAlpha(0.55f) : known ? bc.WithAlpha(0.35f) : null;
        Gfx.RoundRect(this, c, fill, Ui.RadiusChip, border, now || sel || lost ? 2 : 1);
        var tc = known ? Palette.Text : open ? Palette.Text.WithAlpha(0.85f) : Palette.TextMuted;
        var lines = UiKit.Wrap(t.Name, c.Size.X - 10f, Ui.TextTiny, Fonts.Body, c.Size.Y >= 44f ? 3 : 2);
        for (int k = 0; k < lines.Count; k++)
            Gfx.Text(this, Fonts.Body, new Vector2(c.Position.X + 5, c.Position.Y + 12 + k * 11), lines[k], Ui.TextTiny, tc);
        if (known) Gfx.TextRight(this, Fonts.Bold, new Vector2(c.End.X - 4, c.End.Y - 4), "✓", Ui.TextSmall, bc);
        if (now)
        {
            var pb = new Rect2(c.Position.X + 4, c.End.Y - 6, c.Size.X - 8, 3);
            Gfx.RoundRect(this, pb, new Color(1, 1, 1, 0.08f), 1.5f);
            Gfx.RoundRect(this, new Rect2(pb.Position, new Vector2(pb.Size.X * e.Fraction, 3)), Palette.Accent, 1.5f);
        }
        if (e.Priority == t.Id) Gfx.TextRight(this, Fonts.Bold, new Vector2(c.End.X - 4, c.Position.Y + 11), "★", Ui.TextSmall, Palette.Warning);
        if (t.Fork is { } f && (known || e.Picks.ContainsKey(f.fork)))
        {
            int s = e.Picks.TryGetValue(f.fork, out var ps) ? ps : 0;
            string chip = (s == 0 ? f.a.Name : f.b.Name);
            var cl = UiKit.Wrap(chip, c.Size.X - 22f, Ui.TextMicro, Fonts.Body, 1);
            if (cl.Count > 0 && c.Size.Y >= 40f) Gfx.Text(this, Fonts.Body, new Vector2(c.Position.X + 5, c.End.Y - 5), "⑂ " + cl[0], Ui.TextMicro, WebForkColor);
        }
        if (lost) Gfx.TextRight(this, Fonts.Bold, new Vector2(c.End.X - 4, c.End.Y - 4), "잊음", Ui.TextMicro, Palette.Danger);
        _buttons.Add((c, () => _treeSel = t.Id));
        if (hover) _tip = () => UiKit.Tooltip(this, new Vector2(c.GetCenter().X, c.End.Y), Screen, t.Name, new List<TipLine>
        {
            new($"{TechTree.Roman(t.Era)} {TechTree.EraName(t.Era)} · {TechTree.BranchName(t.Branch)}", Palette.TextDim),
            new(e.Status(t), known ? Palette.Good : lost ? Palette.Danger : Palette.Accent),
            new(t.Says, Palette.Text),
        });
    }

    private static readonly Color WebForkColor = new("#c58cff");

    /// <summary>곁가지 줄: 익힌 것(초록) · 보이는 것(하늘) · 숨은 것 "?"(실마리는 마우스로).</summary>
    private void DrawSideChips(Rect2 r, Vector2 mouse)
    {
        var e = _world.Eras;
        Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X, r.Position.Y + 10), $"곁가지 {e.SideKnown}/23 — 숨은 조합 · 겪어야 열리는 기술", Ui.TextSmall, Palette.TextDim);
        float x = r.Position.X, y = r.Position.Y + 16f;
        foreach (var t in TechTree.SideTechs)
        {
            bool known = e.Known.Contains(t.Id), open = !known && e.SideOpen(t), now = e.Project == t.Id;
            string label = known || open ? t.Name : "?";
            float cw = Gfx.Width(Fonts.Body, label, Ui.TextTiny) + 14f;
            if (x + cw > r.End.X) { x = r.Position.X; y += 20f; }
            if (y + 18f > r.End.Y + 2f) break;
            var c = new Rect2(x, y, cw, 17f);
            var bc = BranchColor(t.Branch);
            bool hover = c.HasPoint(mouse);
            Gfx.RoundRect(this, c, known ? bc.WithAlpha(0.22f) : open ? Palette.Accent.WithAlpha(0.10f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.03f), 8f,
                now ? Palette.Accent : known ? bc.WithAlpha(0.5f) : open ? Palette.Accent.WithAlpha(0.5f) : new Color(1, 1, 1, 0.08f));
            Gfx.TextCentered(this, Fonts.Body, c.GetCenter(), label, Ui.TextTiny, known ? Palette.Text : open ? Palette.Accent : Palette.TextMuted);
            if (known || open) _buttons.Add((c, () => _treeSel = t.Id));
            if (hover)
            {
                var tl = new List<TipLine> { new(TechTree.BranchName(t.Branch) + " · " + (known ? "익힘" : open ? "보임 — 연구할 수 있다" : "아직 숨어 있다"), Palette.TextDim) };
                tl.Add(new TipLine("실마리: " + t.Hint, Palette.Text));
                if (known || open) tl.Add(new TipLine(TechWeb.Find(t.Absorbs[0])?.Effect ?? "", Palette.Text));
                _tip = () => UiKit.Tooltip(this, new Vector2(c.GetCenter().X, c.End.Y), Screen, known || open ? t.Name : "숨은 기술", tl);
            }
            x += cw + 5f;
        }
    }

    /// <summary>고른 기술 한 장: 말 · 바뀌는 것(전후 숫자) · 설치 · 갈래 · 값 · 아는 사람 · 우선 연구.</summary>
    private void DrawTreeDetail(Rect2 p, TreeTech t, Vector2 mouse)
    {
        var w = _world;
        var e = w.Eras;
        if (p.Size.Y < 60f) return;
        var bc = BranchColor(t.Branch);
        Gfx.RoundRect(this, p, new Color(1, 1, 1, 0.025f), Ui.RadiusControl, bc.WithAlpha(0.3f));
        float x = p.Position.X + 12f, right = p.End.X - 12f, y = p.Position.Y + 22f;
        float tw = right - x;
        bool known = e.Known.Contains(t.Id);
        int era = t.Side ? TechTree.SideEra(t) : t.Era;
        foreach (var l in UiKit.Wrap(t.Name, tw, Ui.TextTitle, Fonts.Bold, 2)) { Gfx.Text(this, Fonts.Bold, new Vector2(x, y), l, Ui.TextTitle, Palette.Text); y += 18f; }
        Gfx.Text(this, Fonts.Body, new Vector2(x, y), $"{(t.Side ? "곁가지 · " : $"{TechTree.Roman(era)} {TechTree.EraName(era)} · ")}{TechTree.BranchName(t.Branch)}", Ui.TextSmall, bc);
        y += 15f;
        var st = e.Status(t);
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), st, Ui.TextSmall, known ? Palette.Good : e.Lost.Contains(t.Id) ? Palette.Danger : Palette.Accent);
        y += 18f;
        string says = t.Says.Length > 0 ? t.Says : TechWeb.Find(t.Absorbs.FirstOrDefault())?.Effect ?? "";
        foreach (var l in UiKit.Wrap(says, tw, Ui.TextBody, Fonts.Body, 4)) { if (y > p.End.Y - 40) break; Gfx.Text(this, Fonts.Body, new Vector2(x, y), l, Ui.TextBody, Palette.Text); y += 15f; }
        y += 6f;

        // 바뀌는 것: 익혔으면 그때의 카드(전 → 후), 아직이면 담긴 옛 기술의 말 · 설비 단계
        var rows = new List<(string text, Color color)>();
        var card = e.Unlocks.LastOrDefault(u => u.Id == t.Id);
        if (card != null)
            for (int i = 0; i < card.Lines.Length; i++) rows.Add((card.Lines[i], card.Tones[i] == 1 ? Palette.Good : card.Tones[i] == 2 ? Palette.Warning : Palette.TextDim));
        else
        {
            foreach (var (type, tier) in t.Lifts)
            {
                var tiers = Tech.Tiers(type);
                var to = tiers[Math.Clamp(tier - 1, 0, tiers.Count - 1)];
                int now = e.TierFor(type);
                rows.Add(($"{FurnitureTypes.Name(type)} {Tech.Roman(now)} → {Tech.Roman(tier)} {to.Name} ({to.Note})", Palette.Good));
            }
            if (t.Fx != null) foreach (var (k, m) in t.Fx) rows.Add(($"{EraSystem.KeyLabel(k)} {(m >= 1f ? "+" : "−")}{MathF.Abs(m - 1f) * 100f:0}%", Palette.Good));
            foreach (var id in t.Absorbs)
                if (TechWeb.Find(id) is EraTech old) rows.Add((old.Effect, Palette.TextDim));
        }
        if (rows.Count > 0)
        {
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y), known ? "익힌 뒤 바뀐 것 (전 → 후)" : "익히면", Ui.TextSmall, Palette.TextDim);
            y += 15f;
            foreach (var (text, color) in rows)
            {
                foreach (var l in UiKit.Wrap("· " + text, tw, Ui.TextSmall, Fonts.Body, 2)) { if (y > p.End.Y - 70) break; Gfx.Text(this, Fonts.Body, new Vector2(x, y), l, Ui.TextSmall, color); y += 13f; }
                if (y > p.End.Y - 70) break;
            }
            y += 4f;
        }
        if (card != null && card.Installs.Length > 0 && y < p.End.Y - 70)
            foreach (var l in UiKit.Wrap("설치: " + string.Join(" · ", card.Installs), tw, Ui.TextSmall, Fonts.Body, 2)) { Gfx.Text(this, Fonts.Body, new Vector2(x, y), l, Ui.TextSmall, Palette.Accent); y += 13f; }

        // 갈래: 함장이 뒤집을 수 있다 (익히기 전까지)
        if (t.Fork is { } f && y < p.End.Y - 80)
        {
            y += 6f;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "⑂ " + f.question, Ui.TextSmall, WebForkColor);
            y += 6f;
            int side = e.Picks.TryGetValue(f.fork, out var ps) ? ps : e.ForkLean(f.fork).side;
            float bw = (tw - 6f) / 2f;
            for (int s = 0; s < 2; s++)
            {
                var v = s == 0 ? f.a : f.b;
                var br = new Rect2(x + s * (bw + 6f), y, bw, 24f);
                bool on = side == s;
                bool can = !known && !on;
                Button(br, v.Name, on, mouse, can ? () => e.FlipFork(f.fork) : () => { }, Ui.TextSmall);
            }
            y += 38f;
            string why = e.PickWhy.TryGetValue(f.fork, out var pw) ? pw : e.ForkLean(f.fork).why;
            var vv = side == 0 ? f.a : f.b;
            foreach (var l in UiKit.Wrap($"{vv.Says} — {why}", tw, Ui.TextSmall, Fonts.Body, 2)) { Gfx.Text(this, Fonts.Body, new Vector2(x, y), l, Ui.TextSmall, Palette.TextDim); y += 13f; }
        }

        // 아래: 값 · 날수 · 아는 사람 · 단추
        float by = p.End.Y - 34f;
        if (!known)
        {
            float cost = e.CostOf(t), done = e.Project == t.Id ? e.Progress : e.Partial.TryGetValue(t.Id, out var pp) ? pp : 0f;
            float perDay = TreePerDay;
            string eta = perDay > 0.05f ? $" · 하루 연구 {perDay:0.0} → 약 {MathF.Max(0.1f, (cost - done) / perDay):0.#}일" : "";
            Gfx.Text(this, Fonts.Body, new Vector2(x, by - 22f), $"값 {cost:0}" + (done > 0f ? $" (해 둔 것 {done:0})" : "") + eta, Ui.TextSmall, Palette.TextDim);
        }
        else
        {
            CrewMember? best = null;
            foreach (var c in w.Crew) if (!c.Dead && !c.LeftShip && !c.IsChild && (best == null || c.RawSkill(t.Keeper) > best.RawSkill(t.Keeper))) best = c;
            int orphan = e.Orphan.TryGetValue(t.Id, out var od) ? od : 0;
            string keep = best != null ? $"지키는 사람: {best.Name} ({Skills.Name(t.Keeper)} {best.RawSkill(t.Keeper):0.00})" : "지키는 사람 없음";
            Gfx.Text(this, Fonts.Body, new Vector2(x, by - 22f), keep + (orphan > 0 ? $" · 아는 사람 없이 {orphan}일" : ""), Ui.TextSmall, orphan > 0 ? Palette.Warning : Palette.TextDim);
        }
        bool avail = !known && e.Available.Any(a => a.Id == t.Id);
        if (e.Priority == t.Id) Button(new Rect2(x, by, tw, 26f), "★ 함장 지시 중 — 거두기", true, mouse, () => e.SetPriority(null), Ui.TextSmall);
        else if (avail) Button(new Rect2(x, by, tw, 26f), "우선 연구 — 이것부터", false, mouse, () => e.SetPriority(t.Id), Ui.TextSmall);
        else if (!known) Gfx.Text(this, Fonts.Body, new Vector2(x, by + 16f), t.Side ? "실마리: " + t.Hint : $"{TechTree.Roman(t.Era)} 시대가 열려야 연구할 수 있다", Ui.TextSmall, Palette.TextMuted);
    }

    // ─────────────────────────────── 위 줄: 시대 + 다음 기술 ───────────────────────────────

    private const float EraBlockW = 176f;

    private void DrawTopEra(Rect2 r, Vector2 mouse)
    {
        var e = _world.Eras;
        bool hover = r.HasPoint(mouse);
        if (hover) Gfx.RoundRect(this, r, Ui.Hover, Ui.RadiusChip);
        string era = $"{TechTree.Roman(e.Era)} {TechTree.EraName(e.Era)}";
        Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X + 4, r.Position.Y + 12), UiKit.Fit(era, r.Size.X - 40f, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, Palette.Accent);
        Gfx.TextRight(this, Fonts.Body, new Vector2(r.End.X - 4, r.Position.Y + 12), $"{e.LearnedIn(e.Era)}/8", Ui.TextTiny, Palette.TextDim);
        var pt = e.ProjectTech;
        var bar = new Rect2(r.Position.X + 4, r.Position.Y + 17, r.Size.X - 8, 4);
        Gfx.RoundRect(this, bar, new Color(1, 1, 1, 0.08f), 2);
        if (pt != null) Gfx.RoundRect(this, new Rect2(bar.Position, new Vector2(bar.Size.X * e.Fraction, 4)), BranchColor(pt.Branch), 2);
        string next = pt != null ? $"{(e.Priority == pt.Id ? "★ " : "")}{pt.Name} {e.Fraction * 100f:0}%" : e.Complete ? "항해의 목표에 닿았다" : "연구할 것 없음";
        Gfx.Text(this, Fonts.Body, new Vector2(r.Position.X + 4, r.Position.Y + 33), UiKit.Fit(next, r.Size.X - 8, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        _buttons.Add((r, () => { if (!TechOpen || TechWebOpen) ToggleDock(DockTab.Tech); TechWebOpen = false; }));
        if (hover)
        {
            var tl = new List<TipLine> { new($"본 기술 {e.MainKnown}/56 · 곁가지 {e.SideKnown}/23", Palette.TextDim) };
            if (pt != null) { tl.Add(new TipLine($"지금 연구 — {pt.Name} ({TechTree.BranchName(pt.Branch)})", Palette.Text)); tl.Add(new TipLine(e.ProjectWhy, Palette.TextDim)); }
            tl.Add(new TipLine("누르면 기술표", Palette.Accent));
            _tip = () => UiKit.Tooltip(this, new Vector2(r.GetCenter().X, r.End.Y), Screen, era, tl);
        }
    }

    // ─────────────────────────────── 새 기술 카드 ───────────────────────────────

    private int _unlockSeen = -1;
    private float _unlockAt = -1f;

    /// <summary>새 기술 · 새 시대를 익힌 순간 화면 가운데 위에 크게: 말 · 전후 숫자 · 설치할 설비 · 고른 갈래. 9초 뒤 · 누르면 닫힌다.</summary>
    private void DrawUnlockCard(Vector2 mouse)
    {
        var e = _world.Eras;
        if (_unlockSeen < 0 || _unlockSeen > e.Unlocks.Count) { _unlockSeen = e.Unlocks.Count; return; } // 처음(불러온 게임)엔 지난 카드를 다시 띄우지 않는다
        if (_unlockSeen >= e.Unlocks.Count) return;
        var u = e.Unlocks[_unlockSeen];
        if (_unlockAt < 0f) _unlockAt = _time;
        float age = _time - _unlockAt;
        if (age > 9f) { _unlockSeen++; _unlockAt = -1f; return; }
        bool era = u.Id.StartsWith("era:", StringComparison.Ordinal);
        var t = TechTree.Find(u.Id);
        string title = u.Id == "era:goal" ? "항해의 목표" : era ? $"새 시대 — {TechTree.Roman(u.Era)} {TechTree.EraName(u.Era)}" : $"{(u.Relearn ? "되찾은 기술" : "새 기술")} — {t?.Name}";
        string sub = era ? "지난 시대의 여덟 기술을 모두 익혔다" : t == null ? "" : $"{(t.Side ? "곁가지" : $"{TechTree.Roman(t.Era)} {TechTree.EraName(t.Era)}")} · {TechTree.BranchName(t.Branch)}";
        var accent = era ? new Color("#f2c94c") : t != null ? BranchColor(t.Branch) : Palette.Accent;
        float w = MathF.Min(480f, Screen.X - 40f);
        var rows = new List<(string text, Color color, bool bold)>();
        string says = t == null ? "" : t.Says.Length > 0 ? t.Says : TechWeb.Find(t.Absorbs.FirstOrDefault())?.Effect ?? "";
        if (says.Length > 0) foreach (var l in UiKit.Wrap(says, w - 32f, Ui.TextBody, Fonts.Body, 3)) rows.Add((l, Palette.Text, false));
        for (int i = 0; i < u.Lines.Length && i < 7; i++)
            foreach (var l in UiKit.Wrap((u.Tones[i] == 0 ? "· " : u.Tones[i] == 1 ? "▲ " : "▼ ") + u.Lines[i], w - 32f, Ui.TextSmall, Fonts.Body, 2))
                rows.Add((l, u.Tones[i] == 1 ? Palette.Good : u.Tones[i] == 2 ? Palette.Warning : Palette.TextDim, false));
        foreach (var ins in u.Installs) foreach (var l in UiKit.Wrap("설치 — " + ins, w - 32f, Ui.TextSmall, Fonts.Body, 2)) rows.Add((l, Palette.Accent, false));
        if (u.Pick.Length > 0) foreach (var l in UiKit.Wrap("⑂ " + u.Pick, w - 32f, Ui.TextSmall, Fonts.Body, 2)) rows.Add((l, WebForkColor, false));
        float h = 66f + rows.Count * 16f + 40f;
        float slide = MathF.Min(1f, age / 0.35f), fade = age > 8.3f ? MathF.Max(0f, (9f - age) / 0.7f) : 1f;
        var card = new Rect2((Screen.X - w) * 0.5f, Margin + 64f - (1f - slide) * 24f, w, h);
        _cards.Add(card);
        Gfx.RoundRect(this, card, Ui.TooltipFill.WithAlpha(0.97f * fade), Ui.RadiusCard, accent.WithAlpha(0.8f * fade), 2);
        Gfx.RoundRect(this, new Rect2(card.Position, new Vector2(card.Size.X, 4f)), accent.WithAlpha(fade), 2f);
        float x = card.Position.X + 16f, y = card.Position.Y + 30f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), UiKit.Fit(title, w - 32f, Ui.TextHeading, Fonts.Bold), Ui.TextHeading, Palette.Text.WithAlpha(fade));
        y += 18f;
        Gfx.Text(this, Fonts.Body, new Vector2(x, y), sub, Ui.TextSmall, accent.WithAlpha(fade));
        y += 20f;
        foreach (var (text, color, _) in rows) { Gfx.Text(this, Fonts.Body, new Vector2(x, y), text, Ui.TextSmall, color.WithAlpha(fade)); y += 16f; }
        var pb = new Rect2(card.Position.X + 16f, card.End.Y - 8f, (card.Size.X - 32f) * (1f - age / 9f), 2f);
        Gfx.RoundRect(this, pb, accent.WithAlpha(0.5f * fade), 1f);
        var look = new Rect2(card.End.X - 16f - 120f, card.End.Y - 36f, 120f, 24f);
        string id = u.Id;
        Button(look, "기술표에서 보기", false, mouse, () => { _unlockSeen++; _unlockAt = -1f; if (!TechOpen || TechWebOpen) ToggleDock(DockTab.Tech); TechWebOpen = false; if (TechTree.Find(id) != null) _treeSel = id; }, Ui.TextSmall);
        Button(new Rect2(look.Position.X - 70f, look.Position.Y, 62f, 24f), "닫기", false, mouse, () => { _unlockSeen++; _unlockAt = -1f; }, Ui.TextSmall);
        if (e.Unlocks.Count - _unlockSeen > 1) Gfx.Text(this, Fonts.Body, new Vector2(x, card.End.Y - 18f), $"다음 카드 {e.Unlocks.Count - _unlockSeen - 1}장", Ui.TextTiny, Palette.TextMuted);
    }
}
