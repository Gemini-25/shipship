using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v7 역사: 연대기 화면(J), 승무원의 "기억" 탭, 설비·방의 이력, 함선 지표 카드의 진화 한 줄.
/// </summary>
public partial class Hud
{
    public bool ChronicleOpen { get; set; }
    private int _chronicleScroll;
    private int _chronicleFilter;

    private static readonly string[] Filters = { "전체", "사고", "대응·적응", "결정", "개조·교훈", "사람" };

    public void ToggleChronicle()
    {
        ChronicleOpen = !ChronicleOpen;
        if (ChronicleOpen) { TechOpen = false; PolicyOpen = false; OpenChain(null); }
        _chronicleScroll = 0;
    }

    private static bool Matches(HistoryKind k, int filter) => filter switch
    {
        1 => k is HistoryKind.Incident or HistoryKind.Damage or HistoryKind.Recovery or HistoryKind.Structure,
        2 => k is HistoryKind.Response or HistoryKind.Adaptation,
        3 => k is HistoryKind.Decision,
        4 => k is HistoryKind.Upgrade or HistoryKind.Lesson,
        5 => k is HistoryKind.Casualty or HistoryKind.Death or HistoryKind.Bond or HistoryKind.Memory,
        _ => true,
    };

    public static string KindName(HistoryKind k) => k switch
    {
        HistoryKind.Incident => "사고",
        HistoryKind.Damage => "피해",
        HistoryKind.Casualty => "부상",
        HistoryKind.Death => "사망",
        HistoryKind.Response => "대응",
        HistoryKind.Adaptation => "적응",
        HistoryKind.Decision => "결정",
        HistoryKind.Recovery => "수습",
        HistoryKind.Upgrade => "개조",
        HistoryKind.Lesson => "교훈",
        HistoryKind.Bond => "유대",
        HistoryKind.Memory => "기억",
        HistoryKind.Structure => "구조",
        _ => "기록",
    };

    public static Color KindColor(HistoryKind k) => k switch
    {
        HistoryKind.Incident => Palette.Danger,
        HistoryKind.Damage => new Color("#ff8a6b"),
        HistoryKind.Casualty => new Color("#ff7a85"),
        HistoryKind.Death => new Color("#e6eaf2"),
        HistoryKind.Response => Palette.Accent,
        HistoryKind.Adaptation => new Color("#e0b64a"),
        HistoryKind.Decision => new Color("#f5d547"),
        HistoryKind.Recovery => Palette.Good,
        HistoryKind.Upgrade => new Color("#5fd4e8"),
        HistoryKind.Lesson => new Color("#8fd65a"),
        HistoryKind.Bond => new Color("#6ee7b7"),
        HistoryKind.Memory => new Color("#b07cff"),
        HistoryKind.Structure => new Color("#ff9a5c"),
        _ => Palette.TextDim,
    };

    /// <summary>글자를 폭에 맞춰 여러 줄로 (공백 기준, 너무 긴 낱말은 글자 단위).</summary>
    private static List<string> Wrap(string text, Font font, int size, float width, int maxLines = 4) => UiKit.Wrap(text, width, size, font, maxLines); // v16.2 공통 부품

    // ─────────────────────────── 연대기 화면 ───────────────────────────

    private Rect2 _chronicleRect;

    /// <summary>모든 기록 (연대기의 "기록"탭 — 역사 줄을 하나하나).</summary>
    private void DrawChronicleEvents(Vector2 mouse)
    {
        var h = _world.History;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f + 64f + 10f;
        float w = Mathf.Min(620f, Screen.X - RightColumnWidth - Margin * 3);
        float height = Screen.Y - y0 - LogFullHeight - Margin - 10f;
        var card = new Rect2(x0, y0, w, height);
        _chronicleRect = card;
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;

        Gfx.Text(this, Fonts.Bold, new Vector2(x, y0 + 30), $"{_world.Ship.Name} 연대기", Ui.TextLarge, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + Gfx.Width(Fonts.Bold, $"{_world.Ship.Name} 연대기", Ui.TextLarge) + 10, y0 + 30),
            $"{_world.Day}일째 · 사고 {h.Episodes.Count}건 · 개조 {h.Upgrades} · 결정 {h.DecisionsMade}(부결 {h.DecisionsRejected})"
            + (_world.Voyage.Past.Count > 0 ? $" · 마친 항해 {_world.Voyage.Past.Count}" : "") + (_world.Life.Memorial.Count > 0 ? $" · 추모 {string.Join("·", _world.Life.Memorial.Select(m => m.name))}" : ""), Ui.TextBody, Palette.TextMuted); // v12.8·v12.7
        Button(new Rect2(right - 58, y0 + 12, 58, 26), "J 닫기", false, mouse, ToggleChronicle, Ui.TextSmall);
        ChronModeTabs(right - 58f - Ui.S2, y0 + 12f, mouse); // v16.24 날 · 주 · 기록

        // 교훈 (운영 방침)
        var lessons = h.Doctrine.Summary().ToList();
        string lessonText = lessons.Count > 0 ? "교훈: " + string.Join(" · ", lessons) : "교훈: 아직 없음 (같은 사고를 여러 번 겪으면 생긴다)";
        Gfx.Text(this, Fonts.Body, new Vector2(x, y0 + 50), lessonText, Ui.TextSmall, lessons.Count > 0 ? new Color("#8fd65a") : Palette.TextMuted);

        // ── 시간 띠: 사고(빨강 막대), 개조(청록 마름모), 결정(노랑), 죽음(흰 십자) ──
        var strip = new Rect2(x, y0 + 62, right - x, 34);
        Gfx.RoundRect(this, strip, new Color(1, 1, 1, 0.03f), 6);
        long start = SimTime.Hours(7);
        long span = Math.Max(SimTime.TicksPerDay, _world.Tick - start);
        float X(long t) => strip.Position.X + 6 + (strip.Size.X - 12) * Mathf.Clamp((t - start) / (float)span, 0f, 1f);
        int days = _world.Day;
        int step = days <= 10 ? 1 : days <= 30 ? 5 : 10;
        for (int d = 1; d <= days; d += step)
        {
            float dx = X((long)(d - 1) * SimTime.TicksPerDay + start);
            DrawLine(new Vector2(dx, strip.End.Y - 6), new Vector2(dx, strip.End.Y - 2), Palette.TextMuted, 1f);
            Gfx.Text(this, Fonts.Body, new Vector2(dx + 2, strip.End.Y - 2), $"{d}", Ui.TextMicro, Palette.TextMuted);
        }
        foreach (var ep in h.Episodes)
        {
            float a = X(ep.Start), b = Mathf.Max(a + 3f, X(ep.End < 0 ? _world.Tick : ep.End));
            DrawRect(new Rect2(a, strip.Position.Y + 5, b - a, 8), Palette.Danger.WithAlpha(ep.Deaths > 0 ? 0.9f : 0.55f));
        }
        foreach (var e in h.Events)
        {
            float ex = X(e.Tick);
            switch (e.Kind)
            {
                case HistoryKind.Upgrade:
                    DrawColoredPolygon(new[] { new Vector2(ex, strip.Position.Y + 14), new Vector2(ex + 4, strip.Position.Y + 18), new Vector2(ex, strip.Position.Y + 22), new Vector2(ex - 4, strip.Position.Y + 18) }, KindColor(e.Kind));
                    break;
                case HistoryKind.Decision:
                    DrawCircle(new Vector2(ex, strip.Position.Y + 18), 2.2f, KindColor(e.Kind), true, -1f, true);
                    break;
                case HistoryKind.Lesson:
                case HistoryKind.Bond:
                    DrawRect(new Rect2(ex - 1, strip.Position.Y + 14, 2, 8), KindColor(e.Kind));
                    break;
                case HistoryKind.Death:
                    DrawRect(new Rect2(ex - 0.75f, strip.Position.Y + 3, 1.5f, 11), Colors.White);
                    DrawRect(new Rect2(ex - 3, strip.Position.Y + 6, 6, 1.5f), Colors.White);
                    break;
            }
        }
        DrawLine(new Vector2(X(_world.Tick), strip.Position.Y + 2), new Vector2(X(_world.Tick), strip.End.Y - 2), Palette.Accent.WithAlpha(0.7f), 1f);

        // ── 걸러 보기 ──
        float fx = x;
        for (int i = 0; i < Filters.Length; i++)
        {
            int f = i;
            float bw = Gfx.Width(Fonts.Bold, Filters[i], Ui.TextSmall) + 18;
            Button(new Rect2(fx, y0 + 102, bw, 24), Filters[i], _chronicleFilter == i, mouse, () => { _chronicleFilter = f; _chronicleScroll = 0; }, Ui.TextSmall);
            fx += bw + 4;
        }

        // ── 목록 (최근 것이 위) ──
        var list = h.Events.Where(e => Matches(e.Kind, _chronicleFilter)).Reverse().ToList();
        float ly = y0 + 134;
        float bottom = card.End.Y - 10;
        _chronicleScroll = Math.Clamp(_chronicleScroll, 0, Math.Max(0, list.Count - 1));
        int shown = 0;
        foreach (var e in list.Skip(_chronicleScroll))
        {
            float textX = x + 112;
            var lines = Wrap(e.Text, Fonts.Body, 12, right - textX, 3);
            float rowH = 8 + lines.Count * 16;
            if (ly + rowH > bottom) break;
            var row = new Rect2(x - 8, ly, right - x + 16, rowH);
            bool hover = row.HasPoint(mouse);
            if (hover) Gfx.RoundRect(this, row, new Color(1, 1, 1, 0.05f), 6);
            if (e.Kind is HistoryKind.Recovery or HistoryKind.Death or HistoryKind.Upgrade or HistoryKind.Lesson)
                DrawRect(new Rect2(x - 8, ly + 3, 2, rowH - 6), KindColor(e.Kind));
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 15), $"{SimTime.Day(e.Tick)}일 {SimTime.Clock(e.Tick)}", Ui.TextSmall, Palette.TextMuted);
            var kc = KindColor(e.Kind);
            Gfx.Pill(this, Fonts.Bold, new Vector2(x + 88, ly + 11), KindName(e.Kind), Ui.TextTiny, kc, kc.WithAlpha(0.12f), kc.WithAlpha(0.35f), 6f, 2f);
            for (int i = 0; i < lines.Count; i++)
                Gfx.Text(this, i == 0 && e.Kind is HistoryKind.Recovery or HistoryKind.Death or HistoryKind.Upgrade ? Fonts.Bold : Fonts.Body,
                    new Vector2(textX, ly + 16 + i * 16), lines[i], Ui.TextBody, e.Kind == HistoryKind.Death ? Colors.White : Palette.Text.WithAlpha(0.92f));
            var ev = e;
            _buttons.Add((row, () => FocusEvent(ev)));
            ly += rowH;
            shown++;
        }
        if (list.Count == 0)
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 16), "아직 아무 일도 없었다.", Ui.TextBody, Palette.TextMuted);
        else if (_chronicleScroll > 0 || shown < list.Count - _chronicleScroll)
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, card.End.Y - 8), $"{_chronicleScroll + 1}–{_chronicleScroll + shown} / {list.Count} · 휠로 넘기기", Ui.TextTiny, Palette.TextMuted);
    }

    private void FocusEvent(HistoryEvent e)
    {
        if (e.At is Cell c)
        {
            _main.Camera.Position = ShipView.ToPx(c.Center);
            if (e.RoomId >= 0) _main.SelectRoom(_world.Ship.Rooms[e.RoomId]);
        }
        else if (e.RoomId >= 0)
        {
            var room = _world.Ship.Rooms[e.RoomId];
            _main.SelectRoom(room);
            _main.Camera.Position = ShipView.ToPx(room.Center);
        }
        else if (e.CrewIds.Length > 0) _main.Select(_world.Crew[e.CrewIds[0]]);
    }

    private bool ScrollChronicle(InputEventMouseButton mb)
    {
        if (!ChronicleOpen || !_chronicleRect.HasPoint(mb.Position)) return false;
        if (mb.ButtonIndex == MouseButton.WheelUp) _chronicleScroll = Math.Max(0, _chronicleScroll - 2);
        else if (mb.ButtonIndex == MouseButton.WheelDown) _chronicleScroll += 2;
        else return false;
        return true;
    }

    // ─────────────────────────── 승무원: 기억 ───────────────────────────

    private void DrawCrewMemory(CrewMember c, float x, float right, float y, Color col)
    {
        var m = c.Memory;
        SectionTitle(x, y + 10, "성격의 변화");
        Row(x, right, y + 18, "침착함", c.Traits.Calm, new Color("#5fd4e8"), Pct(c.Traits.Calm));
        Row(x, right, y + 40, "긴장", m.Trauma / Memory.TraumaMax, Palette.NeedStress, Pct(m.Trauma), m.Trauma >= 0.15f);
        float ly = y + 66;
        if (m.TraumaCause != null && m.Trauma >= 0.03f)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 12), $"{m.TraumaCause} 뒤로 스트레스가 {Pct(m.Trauma)} 밑으로 안 내려간다", Ui.TextSmall, Palette.TextMuted);
            ly += 18;
        }

        ly += 8;
        SectionTitle(x, ly + 10, "무서운 곳");
        ly += 16;
        var fears = Enumerable.Range(0, m.Fear.Length).Where(i => m.Fear[i] >= 0.08f).OrderByDescending(i => m.Fear[i]).Take(3).ToList();
        if (fears.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 14), "없음", Ui.TextBody, Palette.TextMuted); ly += 20; }
        foreach (int i in fears)
        {
            var room = _world.Ship.Rooms[i];
            Row(x, right, ly, room.Name, m.Fear[i], ShipView.FearColor, Pct(m.Fear[i]), m.Fear[i] >= 0.35f);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 70, ly + 28), m.FearCause[i] ?? "", Ui.TextTiny, Palette.TextMuted);
            ly += 34;
        }

        ly += 6;
        SectionTitle(x, ly + 10, "함께 넘긴 사고");
        ly += 16;
        var shared = m.Shared.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(3).ToList();
        if (shared.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 14), "아직 없음", Ui.TextBody, Palette.TextMuted); ly += 20; }
        foreach (var (id, n) in shared)
        {
            var o = _world.Crew[id];
            DrawCircle(new Vector2(x + 5, ly + 10), 4f, Palette.Crew(o.Id), true, -1f, true);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 16, ly + 15), o.Name, Ui.TextBody, Palette.Text);
            bool comrade = m.Comrades.Contains(id);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, ly + 15), comrade ? $"전우 · {n}번" : $"{n}번", Ui.TextSmall, comrade ? Palette.Good : Palette.TextDim);
            ly += 20;
        }

        ly += 6;
        SectionTitle(x, ly + 10, "지나온 일");
        ly += 14;
        foreach (var mark in m.Marks.AsEnumerable().Reverse().Take(4))
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 14), $"{SimTime.Day(mark.Tick)}일", Ui.TextSmall, Palette.TextMuted);
            string t = mark.Text;
            while (t.Length > 4 && Gfx.Width(Fonts.Body, t, Ui.TextSmall) > right - x - 34) t = t[..^2] + "…";
            Gfx.Text(this, Fonts.Body, new Vector2(x + 34, ly + 14), t, Ui.TextSmall, Palette.TextDim);
            ly += 18;
        }
    }

    // ─────────────────────────── 설비·방의 이력 ───────────────────────────

    private List<(string text, Color color, bool bold)> MachineHistoryLines(Furniture f, float width)
    {
        var lines = new List<(string, Color, bool)>();
        if (f.Machine is not Machine m) return lines;
        if (ShipHistory.MachineStory(m) is string story)
            foreach (var l in Wrap(story, Fonts.Bold, 12, width, 2)) lines.Add((l, m.Grade == MachineGrade.Mk3 ? ShipView.Mk3Color : Palette.Text, true));
        foreach (var mark in m.Marks.AsEnumerable().Reverse().Take(4))
            lines.Add(($"{SimTime.Day(mark.Tick)}일 {SimTime.Clock(mark.Tick)}  {mark.Text}", Palette.TextDim, false));
        return lines;
    }

    private List<(string text, Color color, bool bold)> RoomHistoryLines(Room room, float width)
    {
        var lines = new List<(string, Color, bool)>();
        if (ShipHistory.RoomStory(room) is string story)
            foreach (var l in Wrap(story, Fonts.Bold, 12, width, 2)) lines.Add((l, Palette.Text, true));
        var ship = _world.Ship;
        var walls = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == room).Select(kv => kv.Value).ToList();
        int breached = walls.Count(w => w.Breaches > 0), sealedN = walls.Sum(w => w.Seals), welds = walls.Sum(w => w.TotalWelds);
        int swaps = walls.Sum(w => w.Replacements), plates = walls.Count(w => w.Reinforced);
        var hull = new List<string>();
        if (breached > 0) hull.Add($"뚫린 칸 {breached}");
        if (sealedN > 0) hull.Add($"봉합 {sealedN}");
        if (welds > 0) hull.Add($"용접 {welds}");
        if (swaps > 0) hull.Add($"교체 {swaps}");
        if (plates > 0) hull.Add($"보강 {plates}칸");
        if (hull.Count > 0) lines.Add(("외벽: " + string.Join(" · ", hull), plates > 0 ? ShipView.SteelColor : Palette.TextDim, false));
        if (room.Suppression) lines.Add(("천장에 자동 소화 장치", new Color("#9ad0ff"), false));
        foreach (var mark in room.Marks.AsEnumerable().Reverse().Take(3))
            lines.Add(($"{SimTime.Day(mark.Tick)}일 {SimTime.Clock(mark.Tick)}  {mark.Text}", Palette.TextDim, false));
        return lines;
    }

    private float DrawHistoryLines(List<(string text, Color color, bool bold)> lines, float x, float right, float y)
    {
        if (lines.Count == 0) return y;
        Divider(x, right, y + 8);
        SectionTitle(x, y + 28, "이력");
        y += 34;
        foreach (var (text, color, bold) in lines)
        {
            string t = text;
            var font = bold ? Fonts.Bold : Fonts.Body;
            int size = bold ? 12 : 11;
            while (t.Length > 4 && Gfx.Width(font, t, size) > right - x) t = t[..^2] + "…";
            Gfx.Text(this, font, new Vector2(x, y + 14), t, size, color);
            y += bold ? 19 : 17;
        }
        return y;
    }

    // ─────────────────────────── 진화 한 줄 (함선 지표 카드) ───────────────────────────

    private string EvolutionSummary()
    {
        var ship = _world.Ship;
        var h = _world.History;
        var parts = new List<string>();
        int plates = ship.Walls.Count(kv => kv.Value.Reinforced);
        if (plates > 0) parts.Add($"보강 {plates}칸");
        if (h.BatteriesAdded > 0) parts.Add($"배터리 +{h.BatteriesAdded}");
        int feeders = _world.Power.Jumpers.Count(j => j.Permanent);
        if (feeders > 0) parts.Add($"예비 배선 {feeders}");
        int mk3 = ship.Machines.Count(m => m.Grade == MachineGrade.Mk3);
        if (mk3 > 0) parts.Add($"Mk.3 {mk3}");
        int sup = ship.Rooms.Count(r => r.Suppression);
        if (sup > 0) parts.Add($"소화 장치 {sup}");
        int settled = ship.Rooms.Count(r => r.Purpose == "제2 침실");
        if (settled > 0) parts.Add("제2 침실");
        var lessons = h.Doctrine.Summary().ToList();
        string evo = parts.Count > 0 ? "개조: " + string.Join(" · ", parts) : "개조: 아직 없음";
        if (lessons.Count > 0) evo += "   교훈: " + string.Join(" · ", lessons);
        return evo;
    }
}
