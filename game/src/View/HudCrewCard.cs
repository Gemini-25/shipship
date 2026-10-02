using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.2 승무원 카드 (승무원을 고르면 먼저 보이는 탭) — 화면의 주인공:
/// 지금 하는 일 · 목표(생존 > 맡은 역할 > 일 > 생활) · 이유 사슬("배고픔 → 식당 → 자리 없음 → 기다림") ·
/// 믿음(아는 것 · 모르는 것 · 무서운 곳 · 짐작 · 컴퓨터 신뢰 — 세계와 다를 수 있다) · 여러 시스템에 걸친 영향
/// ("배관 누수 → 젖은 바닥 → 미끄러짐 → 부상 → 작업 느림") · 몸 넷 · 잘하는 기술 · 최근 기억 3 · 관계 3 · 지닌 물건.
/// 아무도 고르지 않았으면 오른쪽 칸에 "눈여겨볼 한 사람"의 작은 카드가 늘 떠 있다.
/// 이유 · 믿음은 Core.CrewWhy가 읽기만 해서 만든다.
/// </summary>
public partial class Hud
{
    /// <summary>요약 탭 번호 (탭 줄에서는 맨 앞에 보인다).</summary>
    private const int CardTab = 6;

    /// <summary>지난 그림에서 잰 카드 높이 (담을 것이 다 들어가는 높이) · 이번 카드에서 내용이 내려갈 수 있는 끝.</summary>
    private float _crewCardH = 610f, _crewCardBottom = float.MaxValue;
    /// <summary>작은 카드: 사람을 넘긴 횟수 (◀ ▶).</summary>
    private int _spotShift;

    private static string NeedIcon(string motive) =>
        motive.Contains("배고") || motive.Contains("굶") || motive.Contains("식사") ? "eat"
        : motive.Contains("잠") || motive.Contains("졸") || motive.Contains("기력") || motive.Contains("피곤") || motive.Contains("탈진") ? "sleep"
        : motive.Contains("근무") || motive.Contains("당직") ? "duty"
        : motive.Contains("스트레스") || motive.Contains("지침") ? "stress"
        : motive.Contains("외로") || motive.Contains("대화") || motive.Contains("교류") ? "social"
        : motive.Contains("위험") || motive.Contains("긴급") || motive.Contains("경보") || motive.Contains("공기") || motive.Contains("화재") ? "danger"
        : motive.Contains("작업") || motive.Contains("수리") || motive.Contains("정비") ? "work"
        : "why";

    private static (string icon, Tone tone) GoalLook(GoalTier g) => g switch
    {
        GoalTier.Survival => ("danger", Tone.Danger),
        GoalTier.Role => ("duty", Tone.Info),
        GoalTier.Work => ("work", Tone.Normal),
        _ => ("rest", Tone.Good),
    };

    private static string BeliefIcon(BeliefKind k) => k switch
    {
        BeliefKind.Knows => "eye",
        BeliefKind.Unaware => "unaware",
        BeliefKind.Fear => "panic",
        BeliefKind.Guess => "omen",
        _ => "computer",
    };

    private void DrawCrewCard(CrewMember c, float x, float right, float y, Color col)
    {
        var w = _world;
        float cardTop = y - 102f;
        float skipped = 0f; // 자리가 없어 못 그린 칸의 높이 (다음 그림에서 카드를 그만큼 키운다)
        bool Fits(float need)
        {
            if (y + need <= _crewCardBottom) return true;
            skipped += need;
            return false;
        }

        // ── 지금 ──
        string stateIcon = Icons.CrewState(c, w.Tick);
        Icons.Draw(this, stateIcon, new Vector2(x + 9, y + 10), Ui.IconL - 2, col.Lightened(0.2f));
        string activity = c.Dead ? "사망" : c.CarriedBy != null ? "업혀 감" : c.Down ? "쓰러짐" : c.ActivityLabel;
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 26, y + 16), UiKit.Fit(activity, right - x - 120, Ui.TextTitle, Fonts.Bold), Ui.TextTitle, col.Lightened(0.2f));
        float ax = x + 26 + Mathf.Min(Gfx.Width(Fonts.Bold, activity, Ui.TextTitle), right - x - 120) + 8;
        if (c.Room is Room here)
        {
            Icons.Draw(this, Icons.Room(here.Kind), new Vector2(ax + 7, y + 11), 13, Palette.Room(here.Kind));
            Gfx.Text(this, Fonts.Body, new Vector2(ax + 17, y + 16), UiKit.Fit(here.Name, right - ax - 17, Ui.TextBody), Ui.TextBody, Palette.TextDim);
        }
        y += 26;

        // ── 목표: 층 배지 + 까닭 ──
        if (!c.Dead)
        {
            var (tier, tierName, why) = CrewWhy.Goal(c);
            var (gi, gt) = GoalLook(tier);
            var badge = UiKit.Badge(this, new Vector2(x, y + 9), $"목표 · {tierName}", gt, gi);
            if (why.Length > 0)
                Gfx.Text(this, Fonts.Body, new Vector2(badge.End.X + 8, y + 9 + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), UiKit.Fit(why, right - badge.End.X - 8, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
            y += 22;
        }

        // ── 왜: 이유 사슬 ──
        UiKit.Header(this, x, right, y + 10, "왜", null, "why");
        y += 18;
        y = DrawChainChips(c, CrewWhy.Chain(c, w), x, right, y, col, 4);
        // 고를 때 다른 후보와 견준 점수 (판단 탭의 맛보기)
        if (c.LastEvaluations.Count > 1 && c.Job?.Activity is Activity act && Fits(12))
        {
            var next = c.LastEvaluations.Where(e => e.Activity != act).OrderByDescending(e => e.Score).FirstOrDefault();
            var mine = c.LastEvaluations.FirstOrDefault(e => e.Activity == act);
            if (next.Activity != null && mine.Activity != null)
                Gfx.Text(this, Fonts.Body, new Vector2(x, y + 6), UiKit.Fit($"{mine.Score:0.00}점 — 다음 후보 {next.Activity.Label} {next.Score:0.00} ({next.Reason})", right - x, Ui.TextTiny), Ui.TextTiny, Palette.TextMuted);
            y += 12;
        }
        y += 6;

        // ── 믿음: 이 사람이 아는 것 (세계와 다를 수 있다 — 어긋난 것을 먼저) ──
        var beliefs = CrewWhy.Beliefs(c, w, 3);
        if (beliefs.Count > 0 && Fits(20 + beliefs.Count * 16))
        {
            bool off = beliefs.Any(b => b.Wrong);
            UiKit.Header(this, x, right, y + 10, "믿음", off ? "세계와 어긋남" : "아는 것", "eye", off ? Palette.Warning : null);
            y += 16;
            foreach (var b in beliefs)
            {
                var bc = b.Wrong ? Palette.Warning : b.Kind == BeliefKind.Fear ? new Color("#ff9a8a") : Palette.TextDim;
                Icons.Draw(this, BeliefIcon(b.Kind), new Vector2(x + 7, y + 8), 12, bc);
                Gfx.Text(this, Fonts.Body, new Vector2(x + 18, y + 12), UiKit.Fit(b.Text, right - x - 18, Ui.TextSmall), Ui.TextSmall, bc);
                y += 16;
            }
            y += 6;
        }

        y = DrawBrainSection(c, x, right, y, Fits); // v16.15 두뇌: 감정 · 꿈과 목표 · 계획 · 믿음 (HudBrain.cs)

        // ── 영향: 여러 시스템에 걸친 원인 → 결과 ──
        var links = CrewWhy.Influences(c, w);
        if (links.Count > 0 && Fits(Mathf.Min(3, links.Count) * 16 + 4))
        {
            foreach (var link in links.Take(3))
            {
                var lc = link.Hurts ? Palette.Warning : Palette.Good;
                Icons.Draw(this, SourceIcon(link.Source), new Vector2(x + 7, y + 8), 13, lc);
                float lx = x + 18;
                for (int k = 0; k < link.Steps.Length; k++)
                {
                    bool last = k == link.Steps.Length - 1;
                    string t = UiKit.Fit(link.Steps[k], right - lx - (last ? 0 : 14), Ui.TextTiny, last ? Fonts.Bold : Fonts.Body);
                    if (t.Length == 0) break;
                    Gfx.Text(this, last ? Fonts.Bold : Fonts.Body, new Vector2(lx, y + 12), t, Ui.TextTiny, last ? lc : Palette.TextDim);
                    lx += Gfx.Width(last ? Fonts.Bold : Fonts.Body, t, Ui.TextTiny) + 3;
                    if (!last) { Icons.Draw(this, "chevron-right", new Vector2(lx + 4, y + 8), 9, Palette.TextMuted); lx += 11; }
                }
                y += 16;
            }
            if (links.Count > 3) { Gfx.Text(this, Fonts.Body, new Vector2(x + 18, y + 10), $"외 {links.Count - 3}가지", Ui.TextTiny, Palette.TextMuted); y += 14; }
            y += 4;
        }

        // ── 몸: 넷을 두 줄로 ──
        if (Fits(46))
        {
            var n = c.Needs;
            var v = c.Vitals;
            float half = (right - x - 14) * 0.5f;
            MiniGauge(x, y, half, "eat", "포만감", n.Food, Palette.NeedFood, n.Food < 0.2f);
            MiniGauge(x + half + 14, y, half, "sleep", "기력", n.Rest, Palette.NeedRest, n.Rest < 0.2f);
            MiniGauge(x, y + 20, half, "stress", "스트레스", n.Stress, Palette.NeedStress, n.Stress > 0.7f);
            MiniGauge(x + half + 14, y + 20, half, "health", "체력", v.Health, Palette.VitalHealth, v.Health < 0.5f);
            y += 46;
        }

        // ── 잘하는 기술 3 (기술마다 고유 아이콘) ──
        if (!c.Dead && Fits(24))
        {
            float sx = x, colW = (right - x - 12) / 3f;
            foreach (var (skill, level) in CrewWhy.TopSkills(c, 3))
            {
                var sc = level >= 0.7f ? Palette.Accent : Palette.TextDim;
                Icons.Draw(this, Icons.Skill(skill), new Vector2(sx + 8, y + 10), 14, sc);
                string label = $"{Skills.Name(skill)} {Mathf.RoundToInt(level * 100)}";
                Gfx.Text(this, Fonts.Body, new Vector2(sx + 19, y + 10 + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), UiKit.Fit(label, colW - 22, Ui.TextSmall), Ui.TextSmall, sc);
                sx += colW + 6;
            }
            y += 24;
        }

        // ── 최근 기억 3 ──
        var mem = CrewWhy.RecentMemories(c, 3);
        if (Fits(28 + Mathf.Max(1, mem.Count) * 17))
        {
            UiKit.Divider(this, x, right, y);
            UiKit.Header(this, x, right, y + 18, "최근 기억", null, "memory");
            y += 24;
            if (mem.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), "아직 남은 기억이 없다", Ui.TextSmall, Palette.TextMuted); y += 18; }
            foreach (var (tick, text) in mem)
            {
                string when = $"{SimTime.Day(tick)}일 {SimTime.Clock(tick)}";
                Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), when, Ui.TextTiny, Palette.TextMuted);
                float tx = x + 64;
                Gfx.Text(this, Fonts.Body, new Vector2(tx, y + 12), UiKit.Fit(text, right - tx, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
                y += 17;
            }
            y += 4;
        }

        // ── 관계 3 ──
        var rels = CrewWhy.TopRelations(c, w, 3);
        if (rels.Count > 0 && Fits(28 + rels.Count * 20))
        {
            UiKit.Divider(this, x, right, y);
            UiKit.Header(this, x, right, y + 18, "관계", "가장 강한 사이", "relation");
            y += 24;
            foreach (var (who, value, word, why) in rels)
            {
                DrawCircle(new Vector2(x + 5, y + 8), 4f, Palette.Crew(who.Id), true, -1f, true);
                Gfx.Text(this, Fonts.Bold, new Vector2(x + 14, y + 12), who.Name, Ui.TextBody, Palette.Text);
                var tone = value >= 0.25f ? Tone.Good : value < -0.05f ? Tone.Caution : Tone.Disabled;
                var br = UiKit.Badge(this, new Vector2(x + 18 + Gfx.Width(Fonts.Bold, who.Name, Ui.TextBody), y + 8), word, tone);
                if (why != null)
                {
                    float wx = br.End.X + 6;
                    Gfx.Text(this, Fonts.Body, new Vector2(wx, y + 12), UiKit.Fit($"— {why}", right - wx, Ui.TextTiny), Ui.TextTiny, value >= 0 ? new Color("#9fe0b0") : new Color("#ff9a8a"));
                }
                y += 20;
            }
            y += 4;
        }

        // ── 지닌 물건 (아이콘은 물건마다) ──
        var things = new List<(string icon, string text, Color color)>();
        if (c.Carrying is ItemStack held) things.Add((Icons.Item(held.Kind), $"손에 {held}", Palette.Item(held.Kind)));
        foreach (var (_, kind, count) in c.Kit) things.Add((Icons.Item(kind), $"{ItemKinds.Name(kind)} {count}", Palette.Item(kind)));
        if (c.Suit is SuitState suit) things.Add(("suit", $"우주복 {suit.Oxygen:0.0}시간", Palette.Text));
        foreach (var b in w.Belongings.Of(c).OrderBy(b => b.Id))
            things.Add((Icons.Belonging(b.Kind), b.Name, !b.Usable ? Palette.Danger : b.Open ? new Color("#ffd27a") : Palette.TextDim));
        int thingRows = Mathf.Max(1, (Mathf.Min(things.Count, 6) + 1) / 2);
        if (Fits(28 + thingRows * 18))
        {
            UiKit.Divider(this, x, right, y);
            UiKit.Header(this, x, right, y + 18, "지닌 물건", things.Count > 6 ? $"외 {things.Count - 6} · 물건 탭" : null, "bag");
            y += 24;
            if (things.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), "가진 것이 없다", Ui.TextSmall, Palette.TextMuted); y += 18; }
            float colW = (right - x - 10) * 0.5f;
            for (int i = 0; i < Mathf.Min(things.Count, 6); i++)
            {
                var (icon, text, tcol) = things[i];
                float tx = x + (i % 2) * (colW + 10), ty = y + (i / 2) * 18 + 8;
                Icons.Draw(this, icon, new Vector2(tx + 7, ty), 13, tcol);
                Gfx.Text(this, Fonts.Body, new Vector2(tx + 18, ty + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), UiKit.Fit(text, colW - 18, Ui.TextSmall), Ui.TextSmall, tcol);
            }
            y += thingRows * 18 + 4;
        }
        else if (skipped > 0f && y + 14 <= _crewCardBottom + 8)
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 10), "자리가 모자라 줄였다 — 탭에서 더 본다", Ui.TextTiny, Palette.TextMuted);

        // 다음 그림의 카드 높이: 그린 것 + 못 그린 것 + 머리글 · 탭 · 따라가기 단추
        _crewCardH = y + skipped - cardTop + 50f;
    }

    /// <summary>이유 사슬 칩들 (화살표로 잇고, 넘치면 다음 줄 · maxRows 줄까지). 끝 y를 돌려준다.</summary>
    private float DrawChainChips(CrewMember c, List<WhyStep> chain, float x, float right, float y, Color col, int maxRows)
    {
        float cx = x, cy = y + 11;
        int row = 0;
        string stateIcon = Icons.CrewState(c, _world.Tick);
        for (int i = 0; i < chain.Count; i++)
        {
            var step = chain[i];
            string icon;
            Color tc;
            switch (step.Kind)
            {
                case WhyKind.Need: icon = NeedIcon(step.Text); tc = Palette.Accent; break;
                case WhyKind.Place:
                    var room = c.Job?.TargetRoom ?? c.Job?.Target?.Room ?? c.CareBed?.Room;
                    icon = room != null ? Icons.Room(room.Kind) : "pin";
                    tc = room != null ? Palette.Room(room.Kind).Lightened(0.15f) : Palette.TextDim;
                    break;
                case WhyKind.Obstacle: icon = "danger"; tc = Palette.Warning; break;
                default:
                    icon = c.Job?.Target is Furniture f ? Icons.Furniture(f.Type) : stateIcon; // 설비 이름 옆엔 그 설비의 아이콘
                    tc = col.Lightened(0.25f);
                    break;
            }
            string text = UiKit.Fit(step.Text, right - x - 34, Ui.TextSmall, Fonts.Bold);
            float cw = Gfx.Width(Fonts.Bold, text, Ui.TextSmall) + 30;
            if (cx > x && cx + cw > right)
            {
                if (++row >= maxRows) { Gfx.Text(this, Fonts.Body, new Vector2(cx, cy + 4), "…", Ui.TextSmall, Palette.TextMuted); break; }
                cx = x; cy += 25;
            }
            var r = new Rect2(cx, cy - 10, cw, 20);
            Gfx.RoundRect(this, r, tc.WithAlpha(0.12f), Ui.RadiusChip, tc.WithAlpha(0.45f));
            Icons.Draw(this, icon, new Vector2(r.Position.X + 11, cy), 12, tc);
            Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X + 21, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), text, Ui.TextSmall, tc);
            cx = r.End.X;
            if (i < chain.Count - 1)
            {
                if (cx + 18 > right) { if (++row >= maxRows) break; cx = x; cy += 25; }
                else
                {
                    Icons.Draw(this, "chevron-right", new Vector2(cx + 8, cy), 11, Palette.TextMuted);
                    cx += 16;
                }
            }
        }
        return cy + 18;
    }

    // ─────────────────────────── 작은 승무원 카드 (아무도 고르지 않았을 때) ───────────────────────────

    /// <summary>지금 눈여겨볼 사람: 살펴볼 까닭이 있는 사람 → 급한 일을 하는 사람 → 12초마다 돌아가며 (◀ ▶로 넘김).</summary>
    private CrewMember? SpotlightCrew()
    {
        var live = _world.Crew.Where(c => !c.Dead).OrderBy(c => c.Id).ToList();
        if (live.Count == 0) return null;
        var flagged = live.Where(c => Attention(c) is var a && a is { } at && Ui.IsAlarm(at.tone)).ToList();
        var urgent = live.Where(c => c.Job?.Urgent == true && !flagged.Contains(c)).ToList();
        var order = flagged.Concat(urgent).Concat(live.Where(c => !flagged.Contains(c) && !urgent.Contains(c))).ToList();
        int lead = flagged.Count + urgent.Count;
        int idx = lead > 0 ? 0 : (int)(_time / 12f);
        return order[((idx + _spotShift) % order.Count + order.Count) % order.Count];
    }

    /// <summary>오른쪽 칸의 작은 승무원 카드: 이름 · 목표 · 이유 사슬 · 가장 중요한 믿음 한 줄 · 영향 한 줄. 누르면 큰 카드. 끝 y를 돌려준다.</summary>
    private float DrawCrewSpotlight(float y, Vector2 mouse)
    {
        if (ControlOpen || PolicyOpen || ChronicleOpen || TechOpen) return y - 10f;
        if (SpotlightCrew() is not CrewMember c) return y - 10f;
        var w = _world;
        var col = Palette.Crew(c.Id);
        float x0 = Screen.X - Margin - RightColumnWidth;
        var chain = CrewWhy.Chain(c, w);
        var beliefs = CrewWhy.Beliefs(c, w, 1);
        var links = CrewWhy.Influences(c, w);
        float h = 40 + 24 + 50 + (beliefs.Count > 0 ? 18 : 0) + (links.Count > 0 ? 16 : 0) + 8;
        var card = new Rect2(x0, y, RightColumnWidth, h);
        var attention = Attention(c);
        Card(card, attention is { } at && Ui.IsAlarm(at.tone) ? at.tone : Tone.Info);
        float x = x0 + Ui.Pad, right = card.End.X - Ui.Pad;

        // 머리: 이름 · 역할 · ◀ ▶ (누르면 그 사람 큰 카드)
        var head = new Rect2(x0 + 6, y + 6, RightColumnWidth - 12 - 56, 30);
        if (head.HasPoint(mouse)) Gfx.RoundRect(this, head, Ui.HoverSoft, Ui.RadiusControl);
        DrawCircle(new Vector2(x + 6, y + 21), 6f, col, true, -1f, true);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 18, y + 26), c.Name, Ui.TextTitle, Palette.Text);
        float nx = x + 18 + Gfx.Width(Fonts.Bold, c.Name, Ui.TextTitle) + 8;
        Gfx.Text(this, Fonts.Body, new Vector2(nx, y + 26), UiKit.Fit(attention is { } a2 ? a2.text : CrewRoles.Name(c.Role), head.End.X - nx - 4, Ui.TextSmall), Ui.TextSmall,
            attention is { } a3 ? Ui.Of(a3.tone) : Palette.TextMuted);
        var target = c;
        _buttons.Add((head, () => { _main.Select(target); FocusAt(target.Position); _crewTab = CardTab; }));
        var prev = new Rect2(right - 50, y + 9, 24, 24);
        var next = new Rect2(right - 24, y + 9, 24, 24);
        foreach (var (r, icon, d) in new[] { (prev, "chevron-left", -1), (next, "chevron-right", 1) })
        {
            bool hv = r.HasPoint(mouse);
            if (hv) Gfx.RoundRect(this, r, Ui.Hover, 6);
            Icons.Draw(this, icon, r.GetCenter(), 13, hv ? Palette.Text : Palette.TextMuted);
            int dd = d;
            _buttons.Add((r, () => _spotShift += dd));
        }
        float cy = y + 40;

        // 목표
        var (tier, tierName, why) = CrewWhy.Goal(c);
        var (gi, gt) = GoalLook(tier);
        var badge = UiKit.Badge(this, new Vector2(x, cy + 10), tierName, gt, gi);
        Gfx.Text(this, Fonts.Body, new Vector2(badge.End.X + 8, cy + 10 + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), UiKit.Fit(why.Length > 0 ? why : c.ActivityLabel, right - badge.End.X - 8, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
        cy += 24;
        // 이유 사슬 (두 줄까지)
        cy = DrawChainChips(c, chain, x, right, cy, col, 2) - 4;
        cy = Mathf.Max(cy, y + 40 + 24 + 46);
        // 믿음 한 줄 (어긋난 것을 먼저)
        if (beliefs.Count > 0)
        {
            var b = beliefs[0];
            var bc = b.Wrong ? Palette.Warning : Palette.TextDim;
            Icons.Draw(this, BeliefIcon(b.Kind), new Vector2(x + 7, cy + 8), 12, bc);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 18, cy + 12), UiKit.Fit(b.Text, right - x - 18, Ui.TextSmall), Ui.TextSmall, bc);
            cy += 18;
        }
        // 영향 한 줄 (사슬 그대로)
        if (links.Count > 0)
        {
            var link = links[0];
            var lc = link.Hurts ? Palette.Warning : Palette.Good;
            Icons.Draw(this, SourceIcon(link.Source), new Vector2(x + 7, cy + 7), 12, lc);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 18, cy + 11), UiKit.Fit(string.Join(" → ", link.Steps), right - x - 18, Ui.TextTiny), Ui.TextTiny, lc);
        }
        return card.End.Y;
    }

    private static string SourceIcon(WhySource s) => s switch
    {
        WhySource.Body => "injury",
        WhySource.Air => "oxygen",
        WhySource.Light => "lamp",
        WhySource.Rest => "sleep",
        WhySource.Food => "food",
        WhySource.Mind => "stress",
        WhySource.Morale => "people",
        WhySource.Illness => "sick",
        WhySource.Tool => "wrench",
        WhySource.Load => "materials",
        WhySource.Helper => "robot",
        WhySource.Training => "star",
        WhySource.Memory => "memory",
        _ => "shower",
    };

    /// <summary>작은 게이지: 아이콘 · 이름 · 막대.</summary>
    private void MiniGauge(float x, float y, float width, string icon, string label, float value, Color color, bool alarm)
    {
        var c = alarm ? Palette.Warning : color;
        Icons.Draw(this, icon, new Vector2(x + 6, y + 8), 12, c);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 16, y + 12), label, Ui.TextTiny, alarm ? Palette.Warning : Palette.TextDim);
        float bx = x + 16 + 48;
        UiKit.Gauge(this, new Rect2(bx, y + 6, width - (bx - x) - 30, 4), value, c);
        Gfx.TextRight(this, Fonts.Body, new Vector2(x + width, y + 12), $"{Mathf.RoundToInt(value * 100)}", Ui.TextTiny, Palette.Text);
    }
}
