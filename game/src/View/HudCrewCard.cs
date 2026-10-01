using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.2 승무원 카드 1차 (승무원을 고르면 먼저 보이는 탭): 지금 하는 일과 이유 사슬
/// ("배고픔 → 식당 → 자리 없음 → 기다림") · 몸 상태 넷 · 최근 기억 3 · 관계 3 · 지닌 물건.
/// 이유는 Core.CrewWhy가 읽기만 해서 만든다.
/// </summary>
public partial class Hud
{
    /// <summary>요약 탭 번호 (탭 줄에서는 맨 앞에 보인다).</summary>
    private const int CardTab = 6;

    private static string NeedIcon(string motive) =>
        motive.Contains("배고") || motive.Contains("굶") || motive.Contains("식사") ? "eat"
        : motive.Contains("잠") || motive.Contains("졸") || motive.Contains("기력") || motive.Contains("피곤") || motive.Contains("탈진") ? "sleep"
        : motive.Contains("근무") || motive.Contains("당직") ? "duty"
        : motive.Contains("스트레스") || motive.Contains("지침") ? "stress"
        : motive.Contains("외로") || motive.Contains("대화") || motive.Contains("교류") ? "social"
        : motive.Contains("위험") || motive.Contains("긴급") || motive.Contains("경보") || motive.Contains("공기") || motive.Contains("화재") ? "danger"
        : motive.Contains("작업") || motive.Contains("수리") || motive.Contains("정비") ? "work"
        : "why";

    private void DrawCrewCard(CrewMember c, float x, float right, float y, Color col)
    {
        var w = _world;
        // ── 지금 ──
        string stateIcon = Icons.CrewState(c, w.Tick);
        Icons.Draw(this, stateIcon, new Vector2(x + 9, y + 10), Ui.IconL - 2, col.Lightened(0.2f));
        string activity = c.Dead ? "사망" : c.CarriedBy != null ? "업혀 감" : c.Down ? "쓰러짐" : c.ActivityLabel;
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 26, y + 16), activity, Ui.TextTitle, col.Lightened(0.2f));
        float ax = x + 26 + Gfx.Width(Fonts.Bold, activity, Ui.TextTitle) + 8;
        if (c.Room is Room here)
        {
            Icons.Draw(this, Icons.Room(here.Kind), new Vector2(ax + 7, y + 11), 13, Palette.Room(here.Kind));
            Gfx.Text(this, Fonts.Body, new Vector2(ax + 17, y + 16), UiKit.Fit(here.Name, right - ax - 17, Ui.TextBody), Ui.TextBody, Palette.TextDim);
        }
        y += 28;

        // ── 왜: 이유 사슬 (칩을 화살표로 잇고, 넘치면 다음 줄) ──
        UiKit.Header(this, x, right, y + 10, "왜", null, "why");
        y += 18;
        var chain = CrewWhy.Chain(c, w);
        float cx = x, cy = y + 11;
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
                    icon = c.Job?.Target is Furniture f ? Icons.Furniture(f.Type) : stateIcon;
                    tc = col.Lightened(0.25f);
                    break;
            }
            string text = UiKit.Fit(step.Text, right - x - 34, Ui.TextSmall);
            float cw = Gfx.Width(Fonts.Bold, text, Ui.TextSmall) + 30;
            if (cx > x && cx + cw > right) { cx = x; cy += 25; }
            var r = new Rect2(cx, cy - 10, cw, 20);
            Gfx.RoundRect(this, r, tc.WithAlpha(0.12f), Ui.RadiusChip, tc.WithAlpha(0.45f));
            Icons.Draw(this, icon, new Vector2(r.Position.X + 11, cy), 12, tc);
            Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X + 21, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), text, Ui.TextSmall, tc);
            cx = r.End.X;
            if (i < chain.Count - 1)
            {
                if (cx + 18 > right) { cx = x; cy += 25; }
                else
                {
                    Icons.Draw(this, "chevron-right", new Vector2(cx + 8, cy), 11, Palette.TextMuted);
                    cx += 16;
                }
            }
        }
        y = cy + 18;
        // 고를 때 다른 후보와 견준 점수 (판단 탭의 맛보기)
        if (c.LastEvaluations.Count > 1 && c.Job?.Activity is Activity act)
        {
            var next = c.LastEvaluations.Where(e => e.Activity != act).OrderByDescending(e => e.Score).FirstOrDefault();
            var mine = c.LastEvaluations.FirstOrDefault(e => e.Activity == act);
            if (next.Activity != null && mine.Activity != null)
                Gfx.Text(this, Fonts.Body, new Vector2(x, y + 6), UiKit.Fit($"{mine.Score:0.00}점 — 다음 후보 {next.Activity.Label} {next.Score:0.00} ({next.Reason})", right - x, Ui.TextTiny), Ui.TextTiny, Palette.TextMuted);
            y += 12;
        }
        y += 6;

        // ── 영향: 여러 시스템에 걸친 원인 → 결과 (부상 → 작업 느림 · 정전 → 캄캄함 → 손 · 무서운 방 → 돌아감) ──
        var links = CrewWhy.Influences(c, w);
        if (links.Count > 0)
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
        var n = c.Needs;
        var v = c.Vitals;
        float half = (right - x - 14) * 0.5f;
        MiniGauge(x, y, half, "eat", "포만감", n.Food, Palette.NeedFood, n.Food < 0.2f);
        MiniGauge(x + half + 14, y, half, "sleep", "기력", n.Rest, Palette.NeedRest, n.Rest < 0.2f);
        MiniGauge(x, y + 20, half, "stress", "스트레스", n.Stress, Palette.NeedStress, n.Stress > 0.7f);
        MiniGauge(x + half + 14, y + 20, half, "health", "체력", v.Health, Palette.VitalHealth, v.Health < 0.5f);
        y += 46;

        // ── 최근 기억 3 ──
        UiKit.Divider(this, x, right, y);
        UiKit.Header(this, x, right, y + 18, "최근 기억", null, "memory");
        y += 24;
        var mem = CrewWhy.RecentMemories(c, 3);
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

        // ── 관계 3 ──
        UiKit.Divider(this, x, right, y);
        UiKit.Header(this, x, right, y + 18, "관계", "가장 강한 사이", "relation");
        y += 24;
        foreach (var (who, value, word, why) in CrewWhy.TopRelations(c, w, 3))
        {
            DrawCircle(new Vector2(x + 5, y + 8), 4f, Palette.Crew(who.Id), true, -1f, true);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 14, y + 12), who.Name, Ui.TextBody, Palette.Text);
            var tone = value >= 0.25f ? Tone.Good : value < -0.05f ? Tone.Caution : Tone.Disabled;
            UiKit.Badge(this, new Vector2(x + 18 + Gfx.Width(Fonts.Bold, who.Name, Ui.TextBody), y + 8), word, tone);
            if (why != null)
            {
                float wx = x + 22 + Gfx.Width(Fonts.Bold, who.Name, Ui.TextBody) + Gfx.Width(Fonts.Body, word, Ui.TextTiny) + 22;
                Gfx.Text(this, Fonts.Body, new Vector2(wx, y + 12), UiKit.Fit($"— {why}", right - wx, Ui.TextTiny), Ui.TextTiny, value >= 0 ? new Color("#9fe0b0") : new Color("#ff9a8a"));
            }
            y += 20;
        }
        y += 4;

        // ── 지닌 물건 (아이콘은 물건마다) ──
        UiKit.Divider(this, x, right, y);
        UiKit.Header(this, x, right, y + 18, "지닌 물건", null, "bag");
        y += 24;
        var things = new List<(string icon, string text, Color color)>();
        if (c.Carrying is ItemStack held) things.Add((Icons.Item(held.Kind), $"손에 {held}", Palette.Item(held.Kind)));
        foreach (var (_, kind, count) in c.Kit) things.Add((Icons.Item(kind), $"{ItemKinds.Name(kind)} {count}", Palette.Item(kind)));
        if (c.Suit is SuitState suit) things.Add(("suit", $"우주복 {suit.Oxygen:0.0}시간", Palette.Text));
        foreach (var b in w.Belongings.Of(c).OrderBy(b => b.Id))
            things.Add((Icons.Belonging(b.Kind), b.Name, !b.Usable ? Palette.Danger : b.Open ? new Color("#ffd27a") : Palette.TextDim));
        if (things.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, y + 12), "가진 것이 없다", Ui.TextSmall, Palette.TextMuted); return; }
        float colW = (right - x - 10) * 0.5f;
        for (int i = 0; i < Mathf.Min(things.Count, 6); i++)
        {
            var (icon, text, tcol) = things[i];
            float tx = x + (i % 2) * (colW + 10), ty = y + (i / 2) * 18 + 8;
            Icons.Draw(this, icon, new Vector2(tx + 7, ty), 13, tcol);
            Gfx.Text(this, Fonts.Body, new Vector2(tx + 18, ty + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), UiKit.Fit(text, colW - 18, Ui.TextSmall), Ui.TextSmall, tcol);
        }
        if (things.Count > 6) Gfx.TextRight(this, Fonts.Body, new Vector2(right, y - 6), $"외 {things.Count - 6} · 물건 탭", Ui.TextTiny, Palette.TextMuted);
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
