using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.2 항해 기록: 같은 줄은 묶고("…×3") · 사람 / 방 / 종류로 거르고 · 줄을 누르면 그 장소로 카메라.
/// 조용한 HUD에서는 접어 두고, 최근 경고만 떠오른다.
/// </summary>
public partial class Hud
{
    private bool _logOpen;
    private int _logKind;       // 0 전체 · 1 경고 · 2 배 · 3 작업 · 4 일상
    private bool _logByCrew, _logByRoom;
    private int _logScroll;     // 맨 아래에서 몇 묶음 위로
    private Rect2 _logRect;

    private static readonly (string name, LogKind? kind)[] LogKinds =
        { ("전체", null), ("경고", LogKind.Warning), ("배", LogKind.Ship), ("작업", LogKind.Work), ("일상", LogKind.Life) };

    private static Color LogColor(LogKind k) => k switch
    {
        LogKind.Warning => Palette.Warning,
        LogKind.Ship => Palette.Accent,
        LogKind.Work => Palette.Text,
        _ => Palette.TextDim,
    };

    private Func<LogEntry, bool> LogFilter()
    {
        int crewId = _logByCrew && _main.SelectedCrew is CrewMember c ? c.Id : -1;
        string? room = _logByRoom && _main.SelectedRoom is Room r ? r.Name : null;
        var kind = LogKinds[_logKind].kind;
        return e => Readout.Matches(e, crewId, room, kind);
    }

    /// <summary>줄을 누르면: 사람의 줄은 그 사람에게, 아니면 글 속의 방으로.</summary>
    private void FocusLog(LogEntry e)
    {
        if (e.CrewId >= 0 && e.CrewId < _world.Crew.Count)
        {
            var who = _world.Crew[e.CrewId];
            _main.Select(who);
            FocusAt(who.Position);
            return;
        }
        if (Readout.RoomIn(e.Text, _world.Ship.Rooms) is Room room) _main.FocusRoom(room);
    }

    private bool ScrollLog(InputEventMouseButton mb)
    {
        if (!_logRect.HasPoint(mb.Position)) return false;
        if (mb.ButtonIndex == MouseButton.WheelUp) _logScroll++;
        else if (mb.ButtonIndex == MouseButton.WheelDown) _logScroll = Math.Max(0, _logScroll - 1);
        else return false;
        return true;
    }

    private void DrawLog(Vector2 mouse)
    {
        if (Quiet && !_logOpen) { DrawLogFolded(mouse); return; }
        var card = new Rect2(Margin, Screen.Y - Margin - LogHeight, 470f, LogHeight);
        _logRect = card;
        Card(card);
        float x = card.Position.X + 18, right = card.End.X - 14;
        float hy = card.Position.Y + 22;
        var head = new Rect2(card.Position.X + 6, card.Position.Y + 6, 120, 30);
        if (Quiet && head.HasPoint(mouse)) Gfx.RoundRect(this, head, Ui.HoverSoft, Ui.RadiusControl);
        UiKit.Header(this, x, x + 110, hy, "항해 기록", null, "log");
        if (Quiet)
        {
            Icons.Draw(this, "chevron-up", new Vector2(x + 96, hy - 4), 12, Palette.TextMuted);
            _buttons.Add((head, () => { _logOpen = false; _logScroll = 0; }));
        }

        // 거르기: 종류(눌러서 돌림) · 고른 사람 · 고른 방
        float fx = right;
        fx = LogChip(fx, hy - 4, _main.SelectedRoom is Room sr ? sr.Name : "방", "pin", _logByRoom && _main.SelectedRoom != null, _main.SelectedRoom != null, mouse, () => { _logByRoom = !_logByRoom; _logScroll = 0; });
        fx = LogChip(fx, hy - 4, _main.SelectedCrew is CrewMember sc ? sc.Name : "사람", "crew", _logByCrew && _main.SelectedCrew != null, _main.SelectedCrew != null, mouse, () => { _logByCrew = !_logByCrew; _logScroll = 0; });
        fx = LogChip(fx, hy - 4, LogKinds[_logKind].name, "filter", _logKind != 0, true, mouse, () => { _logKind = (_logKind + 1) % LogKinds.Length; _logScroll = 0; });

        var groups = Readout.Group(_world.Log.Entries, LogRows + _logScroll + 1, LogFilter());
        _logScroll = Math.Min(_logScroll, Math.Max(0, groups.Count - LogRows));
        int end = groups.Count - _logScroll;
        int count = Math.Min(LogRows, end);
        float y = card.Position.Y + 38;
        if (count <= 0)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 18), _logKind != 0 || _logByCrew || _logByRoom ? "거른 조건에 맞는 기록이 없다" : "아직 기록이 없다", Ui.TextBody, Palette.TextMuted);
            return;
        }
        for (int i = 0; i < count; i++)
        {
            var g = groups[end - count + i];
            var e = g.Last;
            float fade = _logScroll > 0 ? 0.85f : 0.45f + 0.55f * (i + 1) / count;
            var row = new Rect2(card.Position.X + 8, y + i * 21, card.Size.X - 16, 21);
            bool hover = row.HasPoint(mouse);
            if (hover) { Gfx.RoundRect(this, row, Ui.HoverSoft, 5); fade = 1f; }
            float baseline = row.Position.Y + 15;
            Gfx.Text(this, Fonts.Body, new Vector2(x, baseline), SimTime.Clock(e.Tick), Ui.TextBody, Palette.TextMuted.WithAlpha(fade));
            float tx = x + 46;
            if (e.Kind == LogKind.Warning) { Icons.Draw(this, "danger", new Vector2(tx + 6, baseline - 4), 12, Palette.Warning.WithAlpha(fade)); tx += 16; }
            if (e.CrewId >= 0 && e.CrewId < _world.Crew.Count)
            {
                var who = _world.Crew[e.CrewId];
                Gfx.Text(this, Fonts.Bold, new Vector2(tx, baseline), who.Name, Ui.TextLabel, Palette.Crew(who.Id).WithAlpha(fade));
                tx += Gfx.Width(Fonts.Bold, who.Name, Ui.TextLabel) + 6;
            }
            float badgeW = g.Count > 1 ? Gfx.Width(Fonts.Bold, $"×{g.Count}", Ui.TextSmall) + 12 : 0f;
            float textRight = row.End.X - 8 - badgeW - (hover ? 18 : 0);
            Gfx.Text(this, Fonts.Body, new Vector2(tx, baseline), UiKit.Fit(e.Text, textRight - tx, Ui.TextLabel), Ui.TextLabel, LogColor(e.Kind).WithAlpha(fade));
            if (g.Count > 1)
            {
                var br = new Rect2(row.End.X - 6 - badgeW - (hover ? 18 : 0), row.Position.Y + 3, badgeW, 15);
                Gfx.RoundRect(this, br, LogColor(e.Kind).WithAlpha(0.14f), 4);
                Gfx.TextCentered(this, Fonts.Bold, br.GetCenter(), $"×{g.Count}", Ui.TextSmall, LogColor(e.Kind).WithAlpha(fade));
            }
            if (hover)
            {
                Icons.Draw(this, "target", new Vector2(row.End.X - 12, row.GetCenter().Y), 13, Palette.Accent);
                if (g.Count > 1)
                {
                    var gg = g;
                    var anchor = new Vector2(Mathf.Min(mouse.X, row.End.X - 100), row.End.Y);
                    _tip = () => UiKit.Tooltip(this, anchor, Screen, $"같은 줄 {gg.Count}번", new List<TipLine>
                    {
                        new($"처음 {SimTime.Day(gg.First.Tick)}일 {SimTime.Clock(gg.First.Tick)} · 마지막 {SimTime.Day(gg.Last.Tick)}일 {SimTime.Clock(gg.Last.Tick)}", Palette.TextDim, "clock"),
                        new("누르면 그 장소로", Palette.TextMuted, "target"),
                    }, icon: "log");
                }
            }
            var entry = e;
            _buttons.Add((row, () => FocusLog(entry)));
        }
        if (_logScroll > 0)
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, card.End.Y - 8), $"↓ 최근 {_logScroll}묶음 더 있음 (휠)", Ui.TextTiny, Palette.TextMuted);
    }

    /// <summary>거르기 칩 (오른쪽 끝 기준, 왼쪽으로 쌓는다). 다음 칩의 오른쪽 끝을 돌려준다.</summary>
    private float LogChip(float right, float cy, string text, string icon, bool on, bool enabled, Vector2 mouse, Action toggle)
    {
        text = UiKit.Fit(text, 70, Ui.TextTiny);
        float w = Gfx.Width(Fonts.Bold, text, Ui.TextTiny) + 28;
        var r = new Rect2(right - w, cy - 10, w, 20);
        bool hover = enabled && r.HasPoint(mouse);
        var col = !enabled ? Palette.TextMuted.WithAlpha(0.6f) : on ? Palette.Accent : hover ? Palette.Text : Palette.TextDim;
        Gfx.RoundRect(this, r, on ? Palette.Accent.WithAlpha(0.15f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.025f), Ui.RadiusChip, on ? Palette.Accent.WithAlpha(0.45f) : Ui.PanelEdge);
        Icons.Draw(this, icon, new Vector2(r.Position.X + 11, r.GetCenter().Y), 11, col);
        Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X + 20, r.GetCenter().Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextTiny)), text, Ui.TextTiny, col);
        if (enabled) _buttons.Add((r, toggle));
        return r.Position.X - 5;
    }

    /// <summary>조용한 HUD: 머리글 한 줄 + 최근 30분의 경고만 (묶어서 셋까지). 누르면 펼친다.</summary>
    private void DrawLogFolded(Vector2 mouse)
    {
        long since = _world.Tick - SimTime.Minutes(30);
        var warn = Readout.Group(_world.Log.Entries, 3, e => e.Kind == LogKind.Warning && e.Tick >= since, 3, 120);
        float h = 36 + warn.Count * 21 + (warn.Count > 0 ? 6 : 0);
        var card = new Rect2(Margin, Screen.Y - Margin - h, 470f, h);
        _logRect = card;
        Card(card, warn.Count > 0 ? Tone.Caution : null);
        float x = card.Position.X + 18, right = card.End.X - 14;
        var head = new Rect2(card.Position.X + 6, card.Position.Y + 4, card.Size.X - 12, 28);
        bool hover = head.HasPoint(mouse);
        if (hover) Gfx.RoundRect(this, head, Ui.HoverSoft, Ui.RadiusControl);
        float hy = head.GetCenter().Y;
        UiKit.IconText(this, "log", new Vector2(x, hy), "항해 기록", Ui.TextSmall, Palette.TextMuted, Fonts.Bold);
        var entries = _world.Log.Entries;
        if (entries.Count > 0 && warn.Count == 0)
        {
            var last = entries[^1];
            float tx = x + 84;
            Gfx.Text(this, Fonts.Body, new Vector2(tx, hy + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)),
                UiKit.Fit($"{SimTime.Clock(last.Tick)}  {(last.CrewId >= 0 && last.CrewId < _world.Crew.Count ? _world.Crew[last.CrewId].Name + " " : "")}{last.Text}", right - 24 - tx, Ui.TextSmall), Ui.TextSmall, Palette.TextMuted.WithAlpha(0.8f));
        }
        else if (warn.Count > 0)
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 84, hy + Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), $"최근 경고 {warn.Sum(g => g.Count)}", Ui.TextSmall, Palette.Warning);
        Icons.Draw(this, "chevron-up", new Vector2(right - 6, hy), 13, hover ? Palette.Text : Palette.TextMuted);
        _buttons.Add((head, () => _logOpen = true));
        float y = card.Position.Y + 34;
        foreach (var g in warn)
        {
            var e = g.Last;
            var row = new Rect2(card.Position.X + 8, y, card.Size.X - 16, 21);
            if (row.HasPoint(mouse)) Gfx.RoundRect(this, row, Ui.HoverSoft, 5);
            float baseline = y + 15;
            Gfx.Text(this, Fonts.Body, new Vector2(x, baseline), SimTime.Clock(e.Tick), Ui.TextBody, Palette.TextMuted);
            Icons.Draw(this, "danger", new Vector2(x + 52, baseline - 4), 12, Palette.Warning);
            string t = (e.CrewId >= 0 && e.CrewId < _world.Crew.Count ? _world.Crew[e.CrewId].Name + " " : "") + e.Text + (g.Count > 1 ? $" ×{g.Count}" : "");
            Gfx.Text(this, Fonts.Body, new Vector2(x + 62, baseline), UiKit.Fit(t, right - x - 62, Ui.TextLabel), Ui.TextLabel, Palette.Warning);
            var entry = e;
            _buttons.Add((row, () => FocusLog(entry)));
            y += 21;
        }
    }
}
