using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>아래 탭 (F1~F7) 일곱 묶음.</summary>
public enum DockTab { None = -1, Ship, People, Records, Incidents, Tech, Computer, Menu }

// v20 UI 개편: 늘 보이는 것은 위 줄 · 사고/경보 카드 · 고른 것 카드뿐. 나머지 창은 아래 탭 일곱 개(F1~F7)로 묶어
// 오른쪽 한 칸에 하나씩 연다 (배는 남은 자리에 맞춰 가리지 않는다 · 창을 열어도 시간은 흐른다).
// 옛 창 깃발(ChronicleOpen …)은 그대로 두고 탭 · 쪽에 맞춰 켜고 끈다 — 창 안 단추가 깃발을 바꾸면 탭이 따라간다.
public partial class Hud
{
    public DockTab Dock { get; private set; } = DockTab.None;
    private readonly int[] _dockPage = new int[UiLayout.Tabs];
    private Rect2 _dockBody;
    private int _flagsSeen = -1;
    private float _colXOverride = -1f, _colTop = -1f, _colMaxH;

    private static readonly (string name, string icon, string[] pages)[] DockTabs =
    {
        ("배", "layers", new[] { "겹쳐 보기", "지도", "함선 지표" }),
        ("사람", "people", new[] { "승무원", "방침", "회의록", "메신저" }),
        ("기록", "log", new[] { "항해 기록", "연대기", "항해 결산", "인과 사슬" }),
        ("사고", "incident", new[] { "사고 도구", "사고 도감", "이야기꾼" }),
        ("기술", "wrench", new[] { "기술", "기술 지도" }),
        ("컴퓨터", "computer", new[] { "관제", "오늘의 결정" }),
        ("메뉴", "settings", new[] { "설명서", "수집 도감", "설정", "도움말", "저장 · 불러오기" }),
    };

    /// <summary>창 안 내용 자리 (옛 창들이 이 사각형에 그린다).</summary>
    private float WinX => _dockBody.Position.X;
    private float WinY => _dockBody.Position.Y;
    private float WinW => _dockBody.Size.X;
    private float WinBottom => _dockBody.End.Y;

    /// <summary>고른 것 카드 자리: 오른쪽 끝 (창이 열려 있으면 창 왼쪽).</summary>
    private float ColX => _colXOverride >= 0f ? _colXOverride : (_plan.Dock.Empty ? Screen.X - Margin : _plan.Dock.X - UiLayout.Gap) - RightColumnWidth;
    /// <summary>카드가 내려갈 수 있는 끝 (아래 탭 위).</summary>
    private float SideBottom => _plan.TabBar.Empty ? Screen.Y - Margin - 40f : _plan.TabBar.Y - UiLayout.Gap;

    public int DockPage(DockTab t) => t == DockTab.None ? 0 : _dockPage[(int)t];

    /// <summary>F1~F7: 그 탭을 열거나 (열려 있으면) 닫는다.</summary>
    public void ToggleDock(DockTab t)
    {
        Dock = Dock == t ? DockTab.None : t;
        SyncDockFlags();
    }

    public void OpenDock(DockTab t, int page)
    {
        Dock = t;
        if (t != DockTab.None) _dockPage[(int)t] = Math.Clamp(page, 0, DockTabs[(int)t].pages.Length - 1);
        SyncDockFlags();
    }

    public void CloseDock() { Dock = DockTab.None; SyncDockFlags(); }

    // ── 옛 창 깃발 ↔ 탭 · 쪽 ──

    private static readonly (DockTab tab, int page)[] FlagPages =
    {
        (DockTab.People, 1), (DockTab.People, 2), (DockTab.People, 3),
        (DockTab.Records, 1), (DockTab.Records, 2), (DockTab.Records, 3),
        (DockTab.Incidents, 1), (DockTab.Tech, 0), (DockTab.Tech, 1),
        (DockTab.Computer, 0), (DockTab.Computer, 1), (DockTab.Menu, 1), (DockTab.Menu, 3),
    };

    private int FlagMask()
    {
        bool[] f =
        {
            PolicyOpen, CouncilOpen, MessengerOpen,
            ChronicleOpen, VoyageOpen, ChainOpen,
            ScaleCodexOpen, TechOpen && !TechWebOpen, TechOpen && TechWebOpen,
            ControlOpen, DecisionsOpen, CollectionOpen, HelpOpen,
        };
        int m = 0;
        for (int i = 0; i < f.Length; i++) if (f[i]) m |= 1 << i;
        return m;
    }

    /// <summary>탭 · 쪽에 맞춰 깃발을 켜고 끈다 (한 번에 창 하나).</summary>
    private void SyncDockFlags()
    {
        int page = DockPage(Dock);
        bool On(DockTab t, int p) => Dock == t && page == p;
        PolicyOpen = On(DockTab.People, 1);
        CouncilOpen = On(DockTab.People, 2);
        MessengerOpen = On(DockTab.People, 3);
        ChronicleOpen = On(DockTab.Records, 1);
        VoyageOpen = On(DockTab.Records, 2);
        if (On(DockTab.Records, 3)) { if (ChainIncident == null) ChainIncident = _world.Causes.Notable().FirstOrDefault(); }
        else ChainIncident = null;
        ScaleCodexOpen = On(DockTab.Incidents, 1);
        TechOpen = On(DockTab.Tech, 0) || On(DockTab.Tech, 1);
        TechWebOpen = On(DockTab.Tech, 1);
        ControlOpen = On(DockTab.Computer, 0);
        DecisionsOpen = On(DockTab.Computer, 1);
        CollectionOpen = On(DockTab.Menu, 1);
        HelpOpen = On(DockTab.Menu, 3);
        MinimapOpen = false;
        _hazardMenu = false;
        _flagsSeen = FlagMask();
    }

    /// <summary>창 안 단추 · 다른 코드가 깃발을 바꿨으면 탭을 따라 옮긴다 (새로 켜진 창 쪽으로 · 지금 창이 꺼졌으면 닫는다).</summary>
    private void FollowFlags()
    {
        int m = FlagMask();
        if (m == _flagsSeen) return;
        int on = m & ~Math.Max(0, _flagsSeen);
        if (on != 0)
        {
            int bit = 0;
            while ((on & (1 << bit)) == 0) bit++;
            var (t, p) = FlagPages[bit];
            Dock = t;
            _dockPage[(int)t] = p;
        }
        else if (Dock != DockTab.None && (m & PageBit(Dock, DockPage(Dock))) == 0 && PageBit(Dock, DockPage(Dock)) != 0) Dock = DockTab.None;
        SyncDockFlags();
    }

    private static int PageBit(DockTab t, int p)
    {
        for (int i = 0; i < FlagPages.Length; i++) if (FlagPages[i].tab == t && FlagPages[i].page == p) return 1 << i;
        return 0;
    }

    // ─────────────────────────────── 아래 탭 막대 ───────────────────────────────

    /// <summary>탭마다 배지 수 (0 = 없음): 사고 = 진행 중인 큰 사고 · 컴퓨터 = 기다리는 제안 · 사람 = 서명 받는 안건.</summary>
    private int DockBadge(DockTab t) => t switch
    {
        DockTab.Incidents => _world.Causes.Notable().Count(i => i.Open),
        DockTab.Computer => _world.Automation.Present ? _world.Automation.Asks.Open.Count() : 0,
        DockTab.People => _world.Motions.Open.Count(),
        _ => 0,
    };

    private void DrawTabBar(Vector2 mouse)
    {
        var bar = R(_plan.TabBar);
        if (bar.Size.X <= 0f) return;
        Card(bar);
        float bw = (bar.Size.X - 16f) / UiLayout.Tabs;
        for (int i = 0; i < UiLayout.Tabs; i++)
        {
            var t = (DockTab)i;
            var (name, icon, _) = DockTabs[i];
            var r = new Rect2(bar.Position.X + 8f + i * bw, bar.Position.Y + 6f, bw - 4f, bar.Size.Y - 12f);
            bool on = Dock == t, hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, on ? Palette.Accent.WithAlpha(0.18f) : hover ? Ui.Hover : new Color(1, 1, 1, 0f), Ui.RadiusControl, on ? Palette.Accent.WithAlpha(0.5f) : null);
            var col = on ? Palette.Accent : hover ? Palette.Text : Palette.TextDim;
            float lw = Gfx.Width(Fonts.Bold, name, Ui.TextBody);
            float kw = Gfx.Width(Fonts.Body, $"F{i + 1}", Ui.TextTiny);
            float total = 18f + 6f + lw + 6f + kw;
            float x = r.GetCenter().X - total * 0.5f, cy = r.GetCenter().Y;
            Icons.Draw(this, icon, new Vector2(x + 9f, cy), 16f, col);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 24f, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), name, Ui.TextBody, col);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 30f + lw, cy + Gfx.CenterOffset(Fonts.Body, Ui.TextTiny)), $"F{i + 1}", Ui.TextTiny, Palette.TextMuted);
            int badge = DockBadge(t);
            if (badge > 0)
            {
                string bt = badge > 9 ? "9+" : badge.ToString();
                float bwd = Mathf.Max(16f, Gfx.Width(Fonts.Bold, bt, Ui.TextTiny) + 8f);
                var br = new Rect2(r.End.X - bwd - 2f, r.Position.Y - 4f, bwd, 16f);
                var bc = t == DockTab.Incidents ? Palette.Danger : Palette.Warning;
                Gfx.RoundRect(this, br, bc, 8f);
                Gfx.TextCentered(this, Fonts.Bold, br.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, Ui.TextTiny)), bt, Ui.TextTiny, Palette.Space);
            }
            if (hover)
            {
                string tip = string.Join(" · ", DockTabs[i].pages);
                var anchor = new Vector2(r.Position.X, r.Position.Y - 6f);
                _tip = () => UiKit.Tooltip(this, anchor - new Vector2(0, 40f), Screen, $"{name} (F{i + 1})", new List<TipLine> { new(tip, Palette.TextDim, icon) }, icon: icon);
            }
            _buttons.Add((r, () => ToggleDock(t)));
        }
    }

    // ─────────────────────────────── 오른쪽 창 ───────────────────────────────

    private void DrawDock(Vector2 mouse)
    {
        if (Dock == DockTab.None || _plan.Dock.Empty) return;
        var d = R(_plan.Dock);
        int ti = (int)Dock;
        var (name, icon, pages) = DockTabs[ti];
        int page = _dockPage[ti];
        // 머리 띠: 묶음 이름 · 쪽 단추 · 닫기
        var head = new Rect2(d.Position, new Vector2(d.Size.X, 40f));
        Card(head);
        float x = head.Position.X + 12f, cy = head.GetCenter().Y;
        Icons.Draw(this, icon, new Vector2(x + 8f, cy), 16f, Palette.Accent);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 22f, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextLabel)), name, Ui.TextLabel, Palette.Text);
        x += 22f + Gfx.Width(Fonts.Bold, name, Ui.TextLabel) + 14f;
        var close = new Rect2(head.End.X - 34f, head.Position.Y + 7f, 26f, 26f);
        float avail = close.Position.X - 6f - x;
        float gap = 4f;
        var widths = pages.Select(p => Gfx.Width(Fonts.Bold, p, Ui.TextBody) + 20f).ToArray();
        float scale = Mathf.Min(1f, (avail - gap * (pages.Length - 1)) / Mathf.Max(1f, widths.Sum()));
        for (int i = 0; i < pages.Length; i++)
        {
            float w = widths[i] * scale;
            var r = new Rect2(x, head.Position.Y + 7f, w, 26f);
            bool on = i == page, hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, on ? Palette.Accent.WithAlpha(0.16f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.02f), Ui.RadiusControl, on ? Palette.Accent.WithAlpha(0.45f) : null);
            Gfx.TextCentered(this, Fonts.Bold, r.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), UiKit.Fit(pages[i], w - 6f, Ui.TextBody), Ui.TextBody, on ? Palette.Accent : hover ? Palette.Text : Palette.TextDim);
            int pi = i;
            _buttons.Add((r, () => OpenDock(Dock, pi)));
            x += w + gap;
        }
        Gfx.RoundRect(this, close, close.HasPoint(mouse) ? Ui.Hover : new Color(0, 0, 0, 0), 6);
        Icons.Draw(this, "close", close.GetCenter(), 15, Palette.TextDim);
        _buttons.Add((close, CloseDock));

        _dockBody = new Rect2(d.Position.X, head.End.Y + 6f, d.Size.X, d.End.Y - head.End.Y - 6f);
        _cards.Add(_dockBody); // 창 안 빈 곳을 눌러도 배로 새지 않게
        switch (Dock, page)
        {
            case (DockTab.Ship, 0): DrawViewPage(mouse); break;
            case (DockTab.Ship, 1): DrawMinimap(_dockBody); break;
            case (DockTab.Ship, 2): DrawProfile(_dockBody.Position); break;
            case (DockTab.People, 0): DrawCrewPage(mouse); break;
            case (DockTab.People, 1): DrawPolicy(mouse); break;
            case (DockTab.People, 2): DrawCouncil(mouse); break;
            case (DockTab.People, 3): DrawMessenger(new Vector2(WinX, WinBottom), mouse); break;
            case (DockTab.Records, 0): DrawLogPage(mouse); break;
            case (DockTab.Records, 1): DrawChronicle(mouse); break;
            case (DockTab.Records, 2): DrawVoyage(mouse); break;
            case (DockTab.Records, 3): if (ChainOpen) DrawChain(mouse); else DrawNote("아직 번진 사고가 없다", "사고가 나면 원인에서 결과까지의 사슬이 여기에 그려진다."); break;
            case (DockTab.Incidents, 0): DrawToolsPage(mouse); break;
            case (DockTab.Incidents, 1): DrawScaleCodex(mouse); break;
            case (DockTab.Incidents, 2): DrawStoryPage(mouse); break;
            case (DockTab.Tech, _): DrawTech(mouse); break;
            case (DockTab.Computer, 0): if (_world.Automation.Present) DrawControl(mouse); else DrawNote("주 컴퓨터가 없는 배", "이 배는 사람이 모든 일을 직접 한다."); break;
            case (DockTab.Computer, 1): DrawDecisions(new Vector2(WinX, WinBottom), mouse); break;
            case (DockTab.Menu, 0): DrawCodexPage(mouse); break;
            case (DockTab.Menu, 1): DrawCollection(mouse); break;
            case (DockTab.Menu, 2): DrawSettingsPage(mouse); break;
            case (DockTab.Menu, 3): DrawHelp(mouse); break;
            case (DockTab.Menu, 4): DrawSavePage(mouse); break;
        }
    }

    /// <summary>창 안 한 장짜리 안내 (빈 쪽).</summary>
    private void DrawNote(string title, string text)
    {
        var card = new Rect2(_dockBody.Position, new Vector2(_dockBody.Size.X, 96f));
        Card(card);
        Gfx.Text(this, Fonts.Bold, card.Position + new Vector2(18, 32), title, Ui.TextTitle, Palette.Text);
        Gfx.Text(this, Fonts.Body, card.Position + new Vector2(18, 62), UiKit.Fit(text, card.Size.X - 36f, Ui.TextBody), Ui.TextBody, Palette.TextDim);
    }

    /// <summary>창 안 큰 단추 한 줄 (이름 · 한 줄 설명).</summary>
    private float PageButton(float y, string icon, string label, string note, bool on, Vector2 mouse, Action act)
    {
        var r = new Rect2(_dockBody.Position.X + 12f, y, _dockBody.Size.X - 24f, 46f);
        bool hover = r.HasPoint(mouse);
        Gfx.RoundRect(this, r, on ? Palette.Accent.WithAlpha(0.14f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.025f), 8, on ? Palette.Accent.WithAlpha(0.45f) : Ui.PanelEdge);
        Icons.Draw(this, icon, new Vector2(r.Position.X + 20f, r.GetCenter().Y), 18f, on ? Palette.Accent : Palette.TextDim);
        Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X + 40f, r.Position.Y + 19f), label, Ui.TextBody, on ? Palette.Accent : Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(r.Position.X + 40f, r.Position.Y + 36f), UiKit.Fit(note, r.Size.X - 52f, Ui.TextSmall), Ui.TextSmall, Palette.TextMuted);
        _buttons.Add((r, act));
        return r.End.Y + 6f;
    }

    private Rect2 PageCard(float height)
    {
        var card = new Rect2(_dockBody.Position, new Vector2(_dockBody.Size.X, Mathf.Min(height, _dockBody.Size.Y)));
        Card(card);
        return card;
    }

    // ── 배: 겹쳐 보기 ──
    private void DrawViewPage(Vector2 mouse)
    {
        var modes = ViewModes.All;
        int cols = 3, rows = (modes.Length + cols - 1) / cols;
        const float ch = 50f;
        var card = PageCard(56f + rows * ch + 16f + 4 * 52f + 20f);
        float x0 = card.Position.X + 12f, y = card.Position.Y + 12f;
        float cw = (card.Size.X - 24f) / cols;
        Gfx.Text(this, Fonts.Bold, new Vector2(x0 + 4, y + 16), "무엇을 볼까", Ui.TextLabel, Palette.Text);
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 16, y + 16), "V — 다음 보기 · 한 번 더 누르면 겹쳐 보기 끄기", Ui.TextTiny, Palette.TextMuted);
        y += 30f;
        for (int i = 0; i < modes.Length; i++)
        {
            var m = modes[i];
            var r = new Rect2(x0 + (i % cols) * cw, y + (i / cols) * ch, cw - 6, ch - 6);
            bool on = _main.ViewMode == m, second = _main.SecondaryView == m && !on, hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, on ? Palette.Accent.WithAlpha(0.14f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.02f), 8, on ? Palette.Accent.WithAlpha(0.45f) : second ? Palette.Warning.WithAlpha(0.6f) : null);
            ViewGlyph(m, new Vector2(r.Position.X + 18, r.GetCenter().Y), 9f, on ? Palette.Accent : Palette.TextDim);
            Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X + 34, r.Position.Y + 18), ViewModes.Name(m), Ui.TextBody, on ? Palette.Accent : Palette.Text);
            Gfx.Text(this, Fonts.Body, new Vector2(r.Position.X + 34, r.Position.Y + 35), Fit(ViewNote(m), cw - 46, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
            var mode = m;
            _buttons.Add((r, () =>
            {
                // 같은 보기를 한 번 더 누르면 일반으로 · 다른 보기를 누르면 그 보기를 겹친다 (지금 보기가 일반이 아니면)
                if (_main.ViewMode == mode) { _main.ViewMode = ViewMode.Normal; _main.SecondaryView = null; }
                else if (_main.ViewMode != ViewMode.Normal && mode != ViewMode.Normal && _main.SecondaryView != mode) _main.SecondaryView = mode;
                else { _main.ViewMode = mode; if (_main.SecondaryView == mode) _main.SecondaryView = null; }
            }));
        }
        y += rows * ch + 16f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x0 + 4, y + 4), "따라 보기", Ui.TextLabel, Palette.Text);
        y += 14f;
        y = PageButton(y, "clock", "요약 진행", "큰 일이 날 때까지 며칠을 빨리 감고 한 장으로 보여 준다", _main.Summarizing, mouse, () => _main.ToggleSummary());
        y = PageButton(y, "bag", "물건 따라가기", "누군가의 손을 거치는 물건 하나를 따라간다 (누를 때마다 다음 물건)", false, mouse, () => _main.CycleFollowItem(1));
        y = PageButton(y, "crew", "다음 승무원", "승무원을 차례로 골라 본다 (F — 고른 사람 따라가기)", false, mouse, () => _main.CycleCrew(1));
        PageButton(y, "camera", "사진 모드", "위아래 띠 · Enter로 찍어 연대기에 붙인다 · Esc로 나온다", _main.PhotoMode, mouse, () => { CloseDock(); _main.TogglePhotoMode(); });
    }

    // ── 기록: 항해 기록 + 사건 단추 ──
    private void DrawLogPage(Vector2 mouse)
    {
        var row = new Rect2(_dockBody.Position, new Vector2(_dockBody.Size.X, 44f));
        Card(row);
        float bw = (row.Size.X - 24f - 12f) / 3f;
        string[] labels = { "최근 사건 현장으로", "사건 넘기기 ▸", "사건 직전으로 되감기" };
        Action[] acts = { _main.JumpToLatestAlert, () => _main.CycleEpisode(1), _main.RewindToEvent };
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect2(row.Position.X + 12f + i * (bw + 6f), row.Position.Y + 7f, bw, 30f);
            bool hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, hover ? Ui.Hover : new Color(1, 1, 1, 0.025f), 8, Ui.PanelEdge);
            Gfx.TextCentered(this, Fonts.Bold, r.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), UiKit.Fit(labels[i], bw - 8f, Ui.TextSmall), Ui.TextSmall, hover ? Palette.Text : Palette.TextDim);
            _buttons.Add((r, acts[i]));
        }
        DrawLog(mouse, new Rect2(_dockBody.Position + new Vector2(0, 50f), _dockBody.Size - new Vector2(0, 50f)));
    }

    // ── 사람: 승무원 · 작업 ──
    private void DrawCrewPage(Vector2 mouse)
    {
        _colXOverride = _dockBody.Position.X; _colTop = _dockBody.Position.Y; _colMaxH = _dockBody.Size.Y;
        DrawCrewList(mouse);
        _colXOverride = _dockBody.Position.X + RightColumnWidth + UiLayout.Gap;
        if (_colXOverride + RightColumnWidth <= _dockBody.End.X + 1f) DrawWorkBoard(_dockBody.Position.Y, _dockBody.Size.Y, mouse);
        _colXOverride = -1f; _colTop = -1f; _colMaxH = 0f;
    }

    // ── 사고: 도구 ──
    private void DrawToolsPage(Vector2 mouse)
    {
        var tools = IncidentTools.All;
        int cols = 3;
        float cw = (_dockBody.Size.X - 24f) / cols;
        int rows = (tools.Length + cols - 1) / cols;
        var card = PageCard(48f + rows * 40f + 10f);
        float x0 = card.Position.X + 12f, y = card.Position.Y + 12f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x0 + 4, y + 16), "사고 일으키기", Ui.TextLabel, Palette.Danger.WithAlpha(0.85f));
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 16, y + 16), "고른 뒤 배를 누른다 · Esc — 그만", Ui.TextTiny, Palette.TextMuted);
        y += 30f;
        for (int i = 0; i < tools.Length; i++)
        {
            var t = tools[i];
            var r = new Rect2(x0 + (i % cols) * cw, y + (i / cols) * 40f, cw - 6f, 34f);
            bool active = _main.Tool == t, hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, active ? Palette.Danger.WithAlpha(0.18f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.02f), 8, active ? Palette.Danger.WithAlpha(0.55f) : null);
            Gfx.TextCentered(this, Fonts.Bold, r.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), IncidentTools.Name(t), Ui.TextBody, active ? Palette.Danger : hover ? Palette.Text : Palette.TextDim);
            var tool = t;
            _buttons.Add((r, () => _main.ToggleTool(tool)));
        }
        DrawHazardMenu(new Vector2(card.Position.X, card.End.Y + 8f), mouse, card.Size.X);
    }

    // ── 사고: 이야기꾼 ──
    private void DrawStoryPage(Vector2 mouse)
    {
        var card = PageCard(220f);
        float x = card.Position.X + 18f, y = card.Position.Y + 32f, right = card.End.X - 18f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "이야기꾼", Ui.TextTitle, Palette.Text);
        y += 26f;
        string story = StoryLine();
        Gfx.Text(this, Fonts.Body, new Vector2(x, y), UiKit.Fit(story.Length > 0 ? story : "이야기꾼이 쉬고 있다 — 사고는 저절로 일어나지 않는다", right - x, Ui.TextBody), Ui.TextBody, Palette.TextDim);
        y += 30f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "무작위 사고", Ui.TextLabel, Palette.Text);
        y += 12f;
        int mode = RandomModeIndex();
        float bx = x, bw = (right - x - 4f * (RandomModes.Length - 1)) / RandomModes.Length;
        for (int i = 0; i < RandomModes.Length; i++)
        {
            var r = new Rect2(bx, y, bw, 30f);
            bool on = i == mode, hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, on ? Palette.Danger.WithAlpha(0.16f) : hover ? Ui.Hover : new Color(1, 1, 1, 0.02f), 8, on ? Palette.Danger.WithAlpha(0.5f) : Ui.PanelEdge);
            Gfx.TextCentered(this, Fonts.Bold, r.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), UiKit.Fit(RandomModes[i].name, bw - 8f, Ui.TextSmall), Ui.TextSmall, on ? Palette.Danger : Palette.TextDim);
            float days = RandomModes[i].days;
            _buttons.Add((r, () => _main.SetRandomIncidents(days)));
            bx += bw + 4f;
        }
        y += 46f;
        PageButton(y, "settings", "이야기꾼 성격 · 난이도", "설정 창에서 고른다", false, mouse, () => _main.Options.Toggle());
    }

    // ── 메뉴 ──
    private void DrawCodexPage(Vector2 mouse)
    {
        var card = PageCard(180f);
        float x = card.Position.X + 18f, y = card.Position.Y + 32f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "설명서", Ui.TextTitle, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 26f), UiKit.Fit("켜 두고 설비나 방을 누르면 하는 일 · 원리 · 멈추면 생기는 일이 고른 것 카드에 나온다.", card.Size.X - 36f, Ui.TextBody), Ui.TextBody, Palette.TextDim);
        PageButton(card.Position.Y + 110f, "info", CodexMode ? "설명서 켜짐 — 끄기" : "설명서 켜기", "끄면 고른 것 카드가 지금 상태를 보여 준다", CodexMode, mouse, ToggleCodex);
    }

    private void DrawSettingsPage(Vector2 mouse)
    {
        var card = PageCard(150f);
        float x = card.Position.X + 18f, y = card.Position.Y + 32f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "설정", Ui.TextTitle, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 26f), UiKit.Fit("소리 · 화면 · 이야기꾼 · 글자 크기 · 색약 보정", card.Size.X - 36f, Ui.TextBody), Ui.TextBody, Palette.TextDim);
        PageButton(card.Position.Y + 84f, "settings", _main.Options.Visible ? "설정 창 닫기" : "설정 창 열기", "가운데에 설정 창이 뜬다", _main.Options.Visible, mouse, () => _main.Options.Toggle());
    }

    private void DrawSavePage(Vector2 mouse)
    {
        var card = PageCard(230f);
        float x = card.Position.X + 18f, y = card.Position.Y + 32f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "저장 · 불러오기", Ui.TextTitle, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 24f), "자동 저장은 5분마다 한다", Ui.TextSmall, Palette.TextMuted);
        float by = card.Position.Y + 76f;
        by = PageButton(by, "memory", "지금 저장", "이 항해를 그대로 적어 둔다", false, mouse, () => _main.SaveGame());
        by = PageButton(by, "cache", "불러오기", "마지막으로 저장한 항해로 돌아간다", false, mouse, () => _main.LoadGame());
        PageButton(by, "clock", "자동 저장 불러오기", "가장 최근 자동 저장으로", false, mouse, _main.LoadAutoSave);
    }
}
