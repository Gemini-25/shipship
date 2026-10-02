using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.24 연대기 화면 (J) — 위: 규모 색으로 칠한 사건 타임라인 / 거르기(사람 · 방 · 물건 · 기술 · 규모) /
/// 아래: 날 · 주마다 한 장 — 머리기사 한 줄 + 부제 · 펼치면 주 컴퓨터 항해 일지 · 승무원 일기(같은 일을 서로 다르게) · 그날의 현장 · 숫자.
/// 점이나 장을 누르면 그 장면(자리)으로 · "그때로"는 그 시각 조금 전으로 되감는다. 읽기만 한다.
/// </summary>
public partial class Hud
{
    /// <summary>0 날 · 1 주 · 2 모든 기록.</summary>
    private int _chronMode;
    private ChronFilter _chronFilter = ChronFilter.All;
    /// <summary>펼친 고르개 (All이면 닫힘).</summary>
    private ChronFilterKind _chronPick = ChronFilterKind.All;
    /// <summary>펼친 장의 첫날 (-1: 가장 최근 장).</summary>
    private int _chronOpenDay = -1;
    private List<ChronChapter> _chapters = new();
    private List<ChronMark> _chronMarks = new();
    private List<(ChronFilter Filter, string Label, int Count)> _chronChoices = new();
    private (int, ChronFilter, ChronFilterKind) _chronKey = (-1, ChronFilter.All, ChronFilterKind.All);
    private float _chronAt = -99f;

    private static readonly string[] ChronModes = { "날", "주", "기록" };
    private static readonly (ChronFilterKind Kind, string Label, string Icon)[] ChronPicks =
    {
        (ChronFilterKind.Person, "사람", "people"), (ChronFilterKind.Room, "방", "room"), (ChronFilterKind.Thing, "물건", "parts"),
        (ChronFilterKind.Tech, "기술", "star"), (ChronFilterKind.Scale, "규모", "incident"),
    };

    /// <summary>장 · 점 · 고를 거리를 다시 만든다 (기록이 바뀌었거나 1초가 지났을 때만 — 화면은 읽기만).</summary>
    private void RefreshChronicle()
    {
        // 고르개를 바꾸면 바로 · 기록이 바뀌는 것은 3초에 한 번만 다시 만든다
        var key = (_chronMode, _chronFilter, _chronPick);
        if (key == _chronKey && _time - _chronAt < 3f) return;
        _chronKey = key;
        _chronAt = _time;
        _chapters = ChronicleBook.Chapters(_world, _chronMode == 1, _chronFilter);
        _chronMarks = ChronicleBook.Timeline(_world, 0, long.MaxValue, _chronFilter);
        _chronChoices = _chronPick == ChronFilterKind.All ? new() : ChronicleBook.Choices(_world, _chronPick, 10);
    }

    private void DrawChronicle(Vector2 mouse)
    {
        if (_chronMode == 2) { DrawChronicleEvents(mouse); return; }
        RefreshChronicle();
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f + 64f + 10f;
        float w = Mathf.Min(720f, Screen.X - RightColumnWidth - Margin * 3);
        float height = Screen.Y - y0 - LogFullHeight - Margin - 10f;
        var card = new Rect2(x0, y0, w, height);
        _chronicleRect = card;
        Card(card);
        float x = x0 + Ui.Pad, right = card.End.X - Ui.Pad;

        // ── 머리 ──
        string sub = $"{_world.Day}일째" + (_world.Life.Memorial.Count > 0 ? $" · 기리는 이름 {string.Join("·", _world.Life.Memorial.Select(m => m.name))}" : "");
        float tabsX = ChronModeTabs(right - 58f - Ui.S2, y0 + 12f, mouse);
        UiKit.CardTitle(this, x, tabsX - Ui.S3, y0 + 32f, $"{_world.Ship.Name} 연대기", sub, "log");
        Button(new Rect2(right - 58, y0 + 12, 58, 26), "J 닫기", false, mouse, ToggleChronicle, Ui.TextSmall);

        // ── 타임라인 ──
        var strip = new Rect2(x, y0 + 46f, right - x, 46f);
        DrawChronTimeline(strip, mouse);

        // ── 거르기 ──
        float fy = DrawChronFilters(x, right, strip.End.Y + Ui.S2, mouse);

        // ── 장 (최근 것이 위) ──
        var list = Enumerable.Reverse(_chapters).ToList();
        float ly = fy + Ui.S2, bottom = card.End.Y - Ui.S3;
        if (list.Count == 0)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 18), _chronFilter.IsAll ? "아직 적을 일이 없다." : "고른 것과 이어진 일이 없다.", Ui.TextBody, Palette.TextMuted);
            return;
        }
        int openDay = _chronOpenDay < 0 ? list[0].FirstDay : _chronOpenDay;
        _chronicleScroll = Math.Clamp(_chronicleScroll, 0, Math.Max(0, list.Count - 1));
        int shown = 0;
        foreach (var ch in list.Skip(_chronicleScroll))
        {
            bool open = ch.FirstDay == openDay;
            float h = open ? Mathf.Min(bottom - ly, ChapterOpenHeight(ch, right - x)) : 50f;
            if (h < 50f || ly + Mathf.Min(h, 50f) > bottom) break;
            var r = new Rect2(x - 6f, ly, right - x + 12f, h);
            if (open) DrawChapterOpen(ch, r, mouse);
            else DrawChapterRow(ch, r, mouse);
            ly += h + Ui.S1;
            shown++;
        }
        if (_chronicleScroll > 0 || shown < list.Count - _chronicleScroll)
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, card.End.Y - 4), $"{_chronicleScroll + 1}–{_chronicleScroll + shown} / {list.Count}장 · 휠로 넘기기", Ui.TextTiny, Palette.TextMuted);
    }

    /// <summary>날 · 주 · 기록 탭 (오른쪽 끝 x에서 왼쪽으로). 왼쪽 끝을 돌려준다.</summary>
    private float ChronModeTabs(float rightX, float y, Vector2 mouse)
    {
        float bx = rightX;
        for (int i = ChronModes.Length - 1; i >= 0; i--)
        {
            float bw = Gfx.Width(Fonts.Bold, ChronModes[i], Ui.TextSmall) + 18f;
            bx -= bw;
            int mode = i;
            Button(new Rect2(bx, y, bw, 26), ChronModes[i], _chronMode == i, mouse, () => { _chronMode = mode; _chronicleScroll = 0; _chronOpenDay = -1; _chronAt = -99f; }, Ui.TextSmall);
            bx -= 4f;
        }
        return bx;
    }

    // ─────────────────────────── 타임라인 ───────────────────────────

    private void DrawChronTimeline(Rect2 strip, Vector2 mouse)
    {
        Gfx.RoundRect(this, strip, UiKit.Well, Ui.RadiusControl, Ui.PanelEdge);
        long start = Math.Max(0, (long)(SimTime.Day(_world.History.FoundedTick) - 1) * SimTime.TicksPerDay);
        long span = Math.Max(SimTime.TicksPerDay, _world.Tick - start);
        float X(long t) => strip.Position.X + 8f + (strip.Size.X - 16f) * Mathf.Clamp((t - start) / (float)span, 0f, 1f);
        float baseY = strip.End.Y - 12f;
        // 날 눈금 · 펼친 장의 구간
        int days = _world.Day - SimTime.Day(start) + 1;
        int step = days <= 12 ? 1 : days <= 40 ? 5 : 10;
        for (int d = SimTime.Day(start); d <= _world.Day; d += step)
        {
            float dx = X((long)(d - 1) * SimTime.TicksPerDay);
            DrawLine(new Vector2(dx, strip.Position.Y + 4f), new Vector2(dx, strip.End.Y - 4f), Palette.TextMuted.WithAlpha(0.18f), 1f);
            Gfx.Text(this, Fonts.Body, new Vector2(dx + 3f, strip.End.Y - 2f), $"{d}일", Ui.TextMicro, Palette.TextMuted);
        }
        var openCh = _chapters.FirstOrDefault(c => c.FirstDay == (_chronOpenDay < 0 ? _chapters.LastOrDefault()?.FirstDay ?? -1 : _chronOpenDay));
        if (openCh != null)
            DrawRect(new Rect2(X(openCh.Start), strip.Position.Y + 2f, Mathf.Max(2f, X(openCh.End) - X(openCh.Start)), strip.Size.Y - 4f), Palette.Accent.WithAlpha(0.07f));
        DrawLine(new Vector2(strip.Position.X + 8f, baseY), new Vector2(strip.End.X - 8f, baseY), Ui.PanelEdge, 1f);

        ChronMark? hover = null;
        foreach (var m in _chronMarks)
        {
            float mx = X(m.Tick);
            Rect2 hit;
            if (m.Scale is IncidentScale s)
            {
                // 사건: 규모가 클수록 높고 굵은 막대 · 길이는 걸린 시간
                float bh = 6f + (int)s * 5f;
                float ex = Mathf.Max(mx + 3f, X(m.End >= 0 ? m.End : _world.Tick));
                var col = UiKit.ScaleColor(s);
                var bar = new Rect2(mx, baseY - bh, ex - mx, bh);
                DrawRect(bar, col.WithAlpha(m.End < 0 ? 0.95f : 0.75f));
                if (s >= IncidentScale.Ship) DrawRect(bar.Grow(1.5f), col.WithAlpha(0.5f), false, 1f);
                hit = bar.Grow(3f);
            }
            else
            {
                string icon = ChronIcon(m.Kind);
                var col = ChronColor(m.Kind);
                var c = new Vector2(mx, strip.Position.Y + 11f);
                Icons.Draw(this, icon, c, 11f, col);
                hit = new Rect2(c - new Vector2(6f, 6f), new Vector2(12f, 12f));
            }
            if (hit.HasPoint(mouse)) hover = m;
            var cm = m;
            _buttons.Add((hit, () => FocusMark(cm)));
        }
        DrawLine(new Vector2(X(_world.Tick), strip.Position.Y + 2f), new Vector2(X(_world.Tick), strip.End.Y - 2f), Palette.Accent.WithAlpha(0.7f), 1.5f);
        if (hover != null)
        {
            var hm = hover;
            var anchor = new Vector2(X(hm.Tick), strip.End.Y);
            var lines = new List<TipLine> { new($"{SimTime.Day(hm.Tick)}일 {SimTime.Clock(hm.Tick)}" + (hm.Scale is IncidentScale hs ? $" · {ScaleTable.Label(hs)}" : ""), Palette.TextDim, "clock") };
            if (hm.RoomId >= 0 && hm.RoomId < _world.Ship.Rooms.Count) lines.Add(new(_world.Ship.Rooms[hm.RoomId].Name, Palette.TextDim, "pin"));
            if (hm.Crew.Length > 0) lines.Add(new(string.Join(" · ", hm.Crew.Take(4).Select(id => _world.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "?")), Palette.TextDim, "people"));
            lines.Add(new("누르면 그 자리로", Palette.TextMuted));
            _tip = () => UiKit.Tooltip(this, anchor, Screen, ChronicleBook.Short(hm.Title, 40), lines, icon: hm.Scale != null ? "incident" : ChronIcon(hm.Kind),
                graphColor: hm.Scale is IncidentScale gs ? UiKit.ScaleColor(gs) : ChronColor(hm.Kind));
        }
    }

    private static string ChronIcon(string kind) => kind switch
    {
        "death" => "dead", "tech" => "star", "milestone" => "pin", "decision" => "people", "bond" => "relation", "upgrade" => "wrench", "lesson" => "memory", _ => "incident",
    };

    private static Color ChronColor(string kind) => kind switch
    {
        "death" => new Color("#e6eaf2"), "tech" => new Color("#5fd4e8"), "milestone" => Palette.Accent, "decision" => new Color("#f5d547"),
        "bond" => new Color("#6ee7b7"), "upgrade" => new Color("#5fd4e8"), "lesson" => new Color("#8fd65a"), _ => Palette.TextDim,
    };

    // ─────────────────────────── 거르기 ───────────────────────────

    /// <summary>거르기 칩 줄 (펼친 고르개가 있으면 그 아래 고를 거리 한 줄). 다음 y를 돌려준다.</summary>
    private float DrawChronFilters(float x, float right, float y, Vector2 mouse)
    {
        float cx = x;
        bool hovAll = new Rect2(cx, y, UiKit.ChipWidth("전체"), Ui.ChipH + 4f).HasPoint(mouse);
        var all = UiKit.Chip(this, new Vector2(cx, y), "전체", _chronFilter.IsAll, hovAll);
        _buttons.Add((all, () => { _chronFilter = ChronFilter.All; _chronPick = ChronFilterKind.All; _chronicleScroll = 0; _chronOpenDay = -1; }));
        cx = all.End.X + Ui.S1;
        foreach (var (kind, label, icon) in ChronPicks)
        {
            bool on = _chronFilter.Kind == kind;
            string text = on ? $"{label}: {ChronFilterLabel()}" : label;
            float cw = UiKit.ChipWidth(text, icon);
            if (cx + cw > right) break;
            bool hov = new Rect2(cx, y, cw, Ui.ChipH + 4f).HasPoint(mouse);
            var r = UiKit.Chip(this, new Vector2(cx, y), text, on || _chronPick == kind, hov, icon);
            var k = kind;
            _buttons.Add((r, () => { _chronPick = _chronPick == k ? ChronFilterKind.All : k; _chronAt = -99f; }));
            cx = r.End.X + Ui.S1;
        }
        y += Ui.ChipH + 4f;
        if (_chronPick == ChronFilterKind.All) return y;
        // 고를 거리 (사건에 많이 나온 순)
        y += Ui.S1;
        cx = x + Ui.S3;
        DrawLine(new Vector2(x + 4f, y), new Vector2(x + 4f, y + Ui.ChipH + 4f), Palette.Accent.WithAlpha(0.5f), 2f);
        if (_chronChoices.Count == 0)
            Gfx.Text(this, Fonts.Body, new Vector2(cx, y + 16f), "아직 고를 것이 없다", Ui.TextSmall, Palette.TextMuted);
        foreach (var (f, label, n) in _chronChoices)
        {
            string text = $"{label} {n}";
            float cw = UiKit.ChipWidth(text);
            if (cx + cw > right) break;
            bool hov = new Rect2(cx, y, cw, Ui.ChipH + 4f).HasPoint(mouse);
            var r = UiKit.Chip(this, new Vector2(cx, y), text, _chronFilter == f, hov);
            var cf = f;
            _buttons.Add((r, () => { _chronFilter = _chronFilter == cf ? ChronFilter.All : cf; _chronPick = ChronFilterKind.All; _chronicleScroll = 0; _chronOpenDay = -1; }));
            cx = r.End.X + Ui.S1;
        }
        return y + Ui.ChipH + 4f;
    }

    private string ChronFilterLabel()
    {
        var f = _chronFilter;
        return f.Kind switch
        {
            ChronFilterKind.Person => _world.Crew.FirstOrDefault(c => c.Id == f.Id)?.Name ?? "?",
            ChronFilterKind.Room => f.Id >= 0 && f.Id < _world.Ship.Rooms.Count ? _world.Ship.Rooms[f.Id].Name : "?",
            ChronFilterKind.Thing => _world.Ship.Furniture.FirstOrDefault(x => x.Id == f.Id)?.Name ?? "?",
            ChronFilterKind.Tech => EraSystem.Find(f.Key)?.Name ?? f.Key,
            ChronFilterKind.Scale => $"{ScaleTable.Mark(f.MinScale)} 이상",
            _ => "",
        };
    }

    // ─────────────────────────── 장 ───────────────────────────

    /// <summary>접힌 장: 날 · 규모 점 · 머리기사 한 줄 · 부제. 누르면 펼친다.</summary>
    private void DrawChapterRow(ChronChapter ch, Rect2 r, Vector2 mouse)
    {
        bool hover = r.HasPoint(mouse);
        if (hover) Gfx.RoundRect(this, r, Ui.HoverSoft, Ui.RadiusControl);
        var peak = ch.Peak;
        if (peak is IncidentScale ps) DrawRect(new Rect2(r.Position.X + 2f, r.Position.Y + 6f, 3f, r.Size.Y - 12f), UiKit.ScaleColor(ps));
        float x = r.Position.X + 14f, right = r.End.X - 8f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, r.Position.Y + 19f), ch.Week ? $"{(ch.FirstDay - 1) / 7 + 1}주" : $"{ch.FirstDay}일", Ui.TextSmall, Palette.TextMuted);
        float dotsW = UiKit.ScaleDots(this, new Vector2(x, r.Position.Y + 34f), ch.ByScale);
        float hx = x + 52f;
        Gfx.Text(this, Fonts.Bold, new Vector2(hx, r.Position.Y + 20f), UiKit.Fit(ch.Headline, right - hx, Ui.TextLabel, Fonts.Bold), Ui.TextLabel, Palette.Text);
        if (ch.Deck != "") Gfx.Text(this, Fonts.Body, new Vector2(hx, r.Position.Y + 38f), UiKit.Fit(ch.Deck, right - hx, Ui.TextSmall), Ui.TextSmall, Palette.TextMuted);
        _ = dotsW;
        int day = ch.FirstDay;
        _buttons.Add((r, () => { _chronOpenDay = day; }));
    }

    private float ChapterOpenHeight(ChronChapter ch, float width)
    {
        float h = 16f + 44f + 18f; // 머리 두 줄 · 부제
        h += 22f + Mathf.Max(1, ch.ShipLog.Count) * 16f; // 항해 일지
        h += 22f + Mathf.Max(1, ch.Diaries.Count) * 50f; // 일기
        h += 34f; // 숫자 줄
        return Mathf.Max(h, 260f);
    }

    /// <summary>펼친 장: 머리기사 · 부제 · 현장 그림 · 항해 일지 · 일기 · 숫자 · 단추(현장 보기 · 사슬 · 그때로).</summary>
    private void DrawChapterOpen(ChronChapter ch, Rect2 r, Vector2 mouse)
    {
        var peak = ch.Peak;
        var accent = peak is IncidentScale ps ? UiKit.ScaleColor(ps) : Palette.Accent;
        Gfx.RoundRect(this, r, UiKit.Well, Ui.RadiusControl, accent.WithAlpha(0.3f));
        DrawRect(new Rect2(r.Position.X + 2f, r.Position.Y + 8f, 3f, r.Size.Y - 16f), accent.WithAlpha(0.9f));
        float x = r.Position.X + 16f, right = r.End.X - 12f, y = r.Position.Y;
        const float photoW = 132f, photoH = 100f;
        float textRight = right - photoW - Ui.S3;

        // 머리: 날 · 규모 점 · 머리기사 (두 줄까지) · 부제
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 18f), ch.Title, Ui.TextSmall, Palette.TextMuted);
        UiKit.ScaleDots(this, new Vector2(x + Gfx.Width(Fonts.Bold, ch.Title, Ui.TextSmall) + 10f, y + 14f), ch.ByScale);
        var head = UiKit.Wrap(ch.Headline, textRight - x, Ui.TextHeading, Fonts.Bold, 2);
        for (int i = 0; i < head.Count; i++) Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 42f + i * 22f), head[i], Ui.TextHeading, Palette.Text);
        y += 42f + (head.Count - 1) * 22f;
        if (ch.Deck != "") Gfx.Text(this, Fonts.Body, new Vector2(x, y + 18f), UiKit.Fit(ch.Deck, textRight - x, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
        y += 26f;

        // 현장 그림 · 단추 (오른쪽 위)
        var photo = new Rect2(right - photoW, r.Position.Y + 12f, photoW, photoH);
        if (ch.Photo is { } ph)
        {
            string cap = $"{SimTime.Day(ph.Tick)}일 {SimTime.Clock(ph.Tick)}" + (ph.RoomId >= 0 && ph.RoomId < _world.Ship.Rooms.Count ? $" · {_world.Ship.Rooms[ph.RoomId].Name}" : "");
            UiKit.SceneFrame(this, photo, _world, ph.RoomId, ph.At, accent, cap);
        }
        else UiKit.SceneFrame(this, photo, _world, -1, null, accent, ch.Week ? "이번 주" : "이날");
        float by = photo.End.Y + Ui.S2;
        if (ch.Lead is ChronMark lead)
        {
            Button(new Rect2(photo.Position.X, by, photoW, 24f), "현장 보기", false, mouse, () => FocusMark(lead), Ui.TextSmall);
            by += 28f;
            if (lead.IsCase && lead.CauseRoot >= 0 && _world.Causes.IncidentOf(lead.CauseRoot) is CauseIncident inc)
            {
                Button(new Rect2(photo.Position.X, by, photoW, 24f), "사고 카드", false, mouse, () => { OpenChain(inc); FocusMark(lead); }, Ui.TextSmall);
                by += 28f;
            }
            long when = Math.Max(0, lead.Tick - SimTime.Minutes(3));
            Button(new Rect2(photo.Position.X, by, photoW, 24f), "↶ 그때로", false, mouse, () => _main.RewindTo(when), Ui.TextSmall);
        }

        // 항해 일지
        UiKit.Header(this, x, textRight, y + 14f, "주 컴퓨터 항해 일지", null, "computer");
        y += 22f;
        if (ch.ShipLog.Count == 0)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x + 4f, y + 12f), _world.Automation.Present ? "이날은 적은 것이 없다" : "이 배엔 주 컴퓨터가 없다", Ui.TextSmall, Palette.TextMuted);
            y += 16f;
        }
        foreach (var (tick, text) in ch.ShipLog)
        {
            if (y > r.End.Y - 60f) break;
            string clock = ch.Week ? $"{SimTime.Day(tick)}일 {SimTime.Clock(tick)}" : SimTime.Clock(tick);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 4f, y + 12f), clock, Ui.TextTiny, Palette.TextMuted);
            float tx = x + 4f + Gfx.Width(Fonts.Body, ch.Week ? "00일 00:00" : "00:00", Ui.TextTiny) + 8f;
            Gfx.Text(this, Fonts.Body, new Vector2(tx, y + 12f), UiKit.Fit(text, (y < photo.End.Y + 90f ? textRight : right) - tx, Ui.TextSmall), Ui.TextSmall, new Color("#a9d8ff").WithAlpha(0.9f));
            y += 16f;
        }

        // 일기 (같은 일을 서로 다르게)
        y += 6f;
        UiKit.Header(this, x, textRight, y + 14f, ch.Diaries.Any(d => d.About) ? "그 일을 적은 일기" : "일기에서", ch.DiaryCount > ch.Diaries.Count ? $"{ch.DiaryCount}편 중" : null, "memory");
        y += 22f;
        if (ch.Diaries.Count == 0)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x + 4f, y + 12f), "아무도 일기를 쓰지 않았다", Ui.TextSmall, Palette.TextMuted);
            y += 18f;
        }
        foreach (var d in ch.Diaries)
        {
            if (y > r.End.Y - 70f) break;
            float qr = y < by + 30f ? textRight : right;
            var who = _world.Crew.FirstOrDefault(c => c.Id == d.CrewId);
            var col = who != null ? Palette.Crew(who.Id).Lightened(0.25f) : Palette.TextDim;
            string role = (d.Role != "" ? d.Role + " · " : "") + (ch.Week ? $"{SimTime.Day(d.Tick)}일 " : "") + SimTime.Clock(d.Tick);
            float qh = UiKit.Quote(this, x, qr, y, d.Name + (who?.Dead == true ? " (고인)" : ""), role, d.Text, col, 2);
            var row = new Rect2(x, y, qr - x, qh);
            if (who != null) { var cw = who; _buttons.Add((row, () => _main.Select(cw))); }
            y += qh;
        }

        // 숫자
        float sy = r.End.Y - 18f;
        Divider(x, right, sy - 14f);
        UiKit.Stats(this, new Vector2(x, sy), right, new (string, int, string, Tone)[]
        {
            ("incident", ch.ByScale.Sum(), "사고", ch.ByScale.Sum() > 0 ? Tone.Caution : Tone.Normal),
            ("injury", ch.Hurt, "다침", Tone.Caution),
            ("dead", ch.Deaths, "숨짐", Tone.Danger),
            ("computer", ch.Acts, "컴퓨터 조치", Tone.Normal),
            ("people", ch.Decisions, "회의 결정", Tone.Normal),
            ("star", ch.Learned, "익힌 기술", Tone.Good),
            ("memory", ch.DiaryCount, "일기", Tone.Normal),
        });
    }

    /// <summary>점 · 장의 장면으로: 그 자리를 화면 가운데(연대기 오른쪽 빈 곳)에 · 방이나 사람을 고른다.</summary>
    private void FocusMark(ChronMark m)
    {
        if (m.At is System.Numerics.Vector2 at)
        {
            float panelRight = ChronicleOpen ? _chronicleRect.End.X : 0f;
            float regionCenter = (panelRight + Screen.X - RightColumnWidth - Margin) * 0.5f;
            float delta = regionCenter - Screen.X * 0.5f;
            _main.Camera.Position = ShipView.ToPx(at) - new Vector2(delta, 0f) / _main.Camera.Zoom.X;
        }
        if (m.RoomId >= 0 && m.RoomId < _world.Ship.Rooms.Count) _main.SelectRoom(_world.Ship.Rooms[m.RoomId]);
        else if (m.Crew.Length > 0 && _world.Crew.FirstOrDefault(c => c.Id == m.Crew[0]) is CrewMember c0) _main.Select(c0);
    }
}
