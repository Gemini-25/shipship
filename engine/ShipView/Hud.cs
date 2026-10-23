using ShipEngine;

namespace ShipView;

/// <summary>화면의 네모 (논리 크기 — 1080 높이 기준 · 배율 U를 곱해 픽셀).</summary>
public readonly record struct RectF(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool Contains(float x, float y) => x >= X && x < X + W && y >= Y && y < Y + H;
    public bool Intersects(RectF o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    public bool Empty => W <= 0 || H <= 0;
}

/// <summary>손잡이가 하는 일 (docs/UI.md 1.1).</summary>
public enum UiAct
{
    Pause, Speed, FixedMenu, Fixed, Sound, Captions, Help, Menu, MenuShip, ShipPick, MenuRestart, MenuStart, Tab, PanelClose, PanelRow, PanelRowAct, PanelBar, ChainLines, Depart,
    CardFollow, CardClose, CardName, Crew, CrewMore, Feed, Vital, Notice, BannerReplay, Broadcast, Story, ReplayStop, Director, Overview, ZoomIn, ZoomOut, OverlayMenu, Overlay, ReplayLast, AskYes, AskNo, RouteTab, Speech, Thoughts, Robot, RobotMore, Talk,
}

/// <summary>누를 자리 하나: 네모 · 하는 일 · 열쇠 (단추 번호 · 판 줄 열쇠 · 사람 번호) · 켜짐 · 누를 수 있나 · 풀이 글 · 못 누를 때의 까닭.</summary>
public sealed record UiHit(RectF R, UiAct Act, long Key = 0, bool On = false, bool Enabled = true, string Tip = "", string Why = "");

public enum RouteKind { World, Blocked, Hit }

/// <summary>한 점이 어디로 가나: 세계 · 가리는 판 (아무 일 없음) · 손잡이.</summary>
public readonly record struct Route(RouteKind Kind, UiHit? Hit = null, string Block = "");

/// <summary>열린 펼친 목록 · 창.</summary>
public enum HudOpen { None, Fixed, Overlay, Menu, Ships }
public enum HudAsk { None, Ship, Restart, Start }
public enum BackStep { None, CloseAsk, CloseOpen, CloseHelp, ClosePanel, Release }

/// <summary>
/// 자리표 (docs/UI.md 1.1 · 2): 화면에 그릴 것과 누를 자리를 모두 순수 C#으로 셈한다 — Godot은 그리기와 '여기 무엇?'만 묻는다 · 실험실이 겹침 · 새는 누름 · 뒤로 차례를 시험한다.
/// 자리는 논리 크기 (창 높이 1080 기준 · <see cref="U"/>로 픽셀). 위에서 아래로: 위 줄 · 둘째 줄(상태 띠 · 판 탭) · 왼쪽 칸(카드 · 사건 띠) · 가운데 칸(다시보기 · ★ · 알림) · 오른쪽 판 ·
/// 아래(방송 · 사람 줄 · 보기 묶음 · 범례 · 아래 줄) · 펼친 목록 · 창.
/// </summary>
public sealed class Hud
{
    public float U = 1, W = 1920, H = 1080;
    /// <summary>누를 자리 (앞의 것이 먼저 — 안쪽 단추를 앞에 둔다).</summary>
    public readonly List<UiHit> Hits = new();
    /// <summary>가리는 판 (그 위의 누름은 세계로 새지 않는다).</summary>
    public readonly List<(string Name, RectF R)> Blocks = new();
    /// <summary>이름 붙은 자리 (그리는 쪽이 읽는다).</summary>
    public readonly Dictionary<string, RectF> Areas = new();
    /// <summary>판의 보이는 줄: 줄 번호 · 네모 · 본문 첫 줄 높이.</summary>
    public readonly List<(int Row, RectF R)> PanelLines = new();
    public int PanelFirst, PanelLast, PanelTotal;
    /// <summary>사람 줄: 사람 번호 · 칸 · 얼굴 배율 · 이름을 쓰나.</summary>
    public readonly List<(int Id, RectF R)> CrewSlots = new();
    public float FaceScale = 2; public bool CrewNames = true; public int CrewMore;
    /// <summary>모달 (확인 창 · 도움말)이 떠 있나 — 그 밖의 누름은 막는다.</summary>
    public bool Modal;

    public void Clear() { Hits.Clear(); Blocks.Clear(); Areas.Clear(); PanelLines.Clear(); CrewSlots.Clear(); CrewMore = 0; Modal = false; }

    public Route At(float x, float y)
    {
        foreach (var h in Hits) if (h.R.Contains(x, y)) return new Route(RouteKind.Hit, h);
        if (Modal) return new Route(RouteKind.Blocked, null, "창");
        foreach (var (n, r) in Blocks) if (r.Contains(x, y)) return new Route(RouteKind.Blocked, null, n);
        return new Route(RouteKind.World);
    }

    /// <summary>서로 겹치는 가리는 판 쌍 (펼친 목록 · 창 · 일부러 위에 그리는 것은 뺀다).</summary>
    public List<string> Overlaps()
    {
        var bad = new List<string>();
        var b = Blocks.Where(x => !x.Name.StartsWith("목록") && !x.Name.StartsWith("창") && !x.Name.StartsWith("자막")).ToList();
        for (int i = 0; i < b.Count; i++)
            for (int j = i + 1; j < b.Count; j++)
                if (b[i].R.Intersects(b[j].R) && !Allowed(b[i].Name, b[j].Name)) bad.Add($"{b[i].Name} × {b[j].Name}");
        return bad;
    }

    private static bool Allowed(string a, string b) => false;

    /// <summary>화면 밖으로 나간 자리.</summary>
    public List<string> Outside()
    {
        var bad = new List<string>();
        foreach (var (n, r) in Blocks) if (r.X < -0.5f || r.Y < -0.5f || r.Right > W + 0.5f || r.Bottom > H + 0.5f) bad.Add(n);
        foreach (var h in Hits) if (h.R.X < -0.5f || h.R.Y < -0.5f || h.R.Right > W + 0.5f || h.R.Bottom > H + 0.5f) bad.Add($"{h.Act}");
        return bad;
    }

    public RectF Area(string name) => Areas.TryGetValue(name, out var r) ? r : default;

    // ── 글 재기: Godot은 글꼴로 · 실험실은 Pretendard 너비 표로 (docs/UI.md 1.1 Metrics) ──
    public delegate float Measure(string text, int px, bool bold);
    public delegate float MeasureLines(string text, int px, float widthPx, int maxLines);

    /// <summary>Pretendard Medium · Bold 너비 어림 (em): 한글 0.864 · 같은 너비 숫자 0.58 · 라틴 0.55 · 빈칸 0.244 · 그 밖 0.8 · 굵게 ×1.03.</summary>
    public static float Approx(string t, int px, bool bold)
    {
        double w = 0;
        foreach (char c in t)
            w += c >= 0xAC00 && c <= 0xD7A3 ? 0.864 : c >= '0' && c <= '9' ? 0.58 : c == ' ' ? 0.244 : c < 128 ? 0.55 : 0.8;
        return (float)(w * px * (bold ? 1.03 : 1));
    }

    public static float ApproxLines(string t, int px, float width, int maxLines)
    {
        int lines = 0;
        foreach (var para in t.Split('\n'))
        {
            float w = Approx(para, px, false);
            lines += Math.Max(1, (int)Math.Ceiling(w / Math.Max(1, width)));
            if (lines >= maxLines) { lines = maxLines; break; }
        }
        return lines * px * 1.32f;
    }

    /// <summary>화면 글자가 모두 Pretendard에 있나 (없는 글자 ✕ ▾ ✔ ≡는 도형으로 그린다).</summary>
    public static readonly string Missing = "✕▾✔≡";
}

/// <summary>메인 스레드만의 화면 상태 (세계 아님): 펼친 목록 · 창 · 판 넘김 · 누른 손잡이 · 올림.</summary>
public sealed class HudState
{
    public HudOpen Open;
    public bool Help;
    public HudAsk Ask;
    public string AskShip = "";
    public int AskFocus;              // 0 그만두기 · 1 하기
    public int PanelFirst;
    public PanelKind PanelSeen;
    public long PickSeen = long.MinValue;
    public int Mute;                  // 0 다 켬 · 1 음악 끔 · 2 다 끔
    public UiHit? Pressed;
    public bool PressWorld, PressBlocked;
    public long HoverKey = long.MinValue;
    public double HoverSince;
    public double HelpBlink = 8;
    public float DragBarY = float.NaN; public int DragBarFirst;
    public double WheelAcc;
    public int HoverAgent = -1;

    /// <summary>누름: 가리는 판 · 손잡이면 끌기를 막는다 · 펼친 목록 밖을 누르면 목록만 닫는다.</summary>
    public void Press(Route r)
    {
        PressWorld = r.Kind == RouteKind.World;
        PressBlocked = r.Kind == RouteKind.Blocked;
        Pressed = r.Hit;
        if (Open != HudOpen.None && (r.Hit == null || !InOpen(r.Hit.Act))) { Open = HudOpen.None; Pressed = null; PressWorld = false; PressBlocked = true; }
    }

    private bool InOpen(UiAct a) => Open switch
    {
        HudOpen.Fixed => a is UiAct.Fixed or UiAct.FixedMenu,
        HudOpen.Overlay => a is UiAct.Overlay or UiAct.OverlayMenu,
        HudOpen.Menu => a is UiAct.MenuShip or UiAct.MenuRestart or UiAct.MenuStart or UiAct.Sound or UiAct.Captions or UiAct.Speech or UiAct.Thoughts or UiAct.Help or UiAct.Menu,
        HudOpen.Ships => a is UiAct.ShipPick or UiAct.MenuShip or UiAct.Menu,
        _ => false,
    };

    /// <summary>놓음: 누른 손잡이와 같은 손잡이일 때만 돌려준다 (가리는 판 · 다른 손잡이 · 세계면 null).</summary>
    public UiHit? Release(Route r)
    {
        var p = Pressed;
        Pressed = null;
        if (p == null || r.Hit == null) return null;
        return r.Hit.Act == p.Act && r.Hit.Key == p.Key ? r.Hit : null;
    }

    /// <summary>뒤로 (오른쪽 누르기 · Esc): 확인 창 → 펼친 목록 → 도움말 → 판 → 고른 이 → 없음. 다시보기는 끝내지 않는다.</summary>
    public BackStep Back(LiveFrame f)
    {
        if (Ask != HudAsk.None) { Ask = HudAsk.None; return BackStep.CloseAsk; }
        if (Open != HudOpen.None) { Open = HudOpen.None; return BackStep.CloseOpen; }
        if (Help) { Help = false; return BackStep.CloseHelp; }
        if (f.Panel != PanelKind.None) return BackStep.ClosePanel;
        if (f.Selected >= 0) return BackStep.Release;
        return BackStep.None;
    }

    /// <summary>판 넘기기: 분수 휠은 모아서 한 줄이 되면 넘긴다 · 끝을 넘지 않는다.</summary>
    public void Scroll(double rows, int total, int visible)
    {
        WheelAcc += rows;
        int step = (int)Math.Truncate(WheelAcc);
        if (step == 0) return;
        WheelAcc -= step;
        PanelFirst = Math.Clamp(PanelFirst + step, 0, Math.Max(0, total - Math.Max(1, visible)));
    }
}

/// <summary>자리표 셈 (docs/UI.md 2 · 3).</summary>
public static class HudLayout
{
    public const float Side = 16, CardW = 430, LeftCol = 462, ViewBtn = 40, ViewGap = 6, BottomBar = 64;
    public static readonly string[] TabNames = { "장면", "항로", "주컴퓨터", "사슬", "사람" };
    public static readonly PanelKind[] TabKinds = { PanelKind.Highlights, PanelKind.Route, PanelKind.Journal, PanelKind.Chain, PanelKind.People };
    public static readonly string[] TabKeys = { "L", "T", "J", "C", "P" };
    public static readonly string[] OverlayWords = { "끔", "공기", "열", "연기", "사슬 선", "선과 관" };

    /// <summary>모든 자리를 셈한다. pxW · pxH: 창 픽셀 · m · ml: 픽셀 글 재기 (돌려준 값은 U로 나눠 논리 크기로).</summary>
    public static void Build(LiveFrame f, HudState s, float pxW, float pxH, Hud.Measure m, Hud.MeasureLines ml, Hud into, string[]? ships = null)
    {
        var h = into;
        h.Clear();
        h.U = Math.Clamp(pxH / 1080f, 0.85f, 2f);
        float U = h.U, W = pxW / U, H = pxH / U;
        h.W = W; h.H = H;
        int Px(int size) => Math.Max(13, (int)Math.Round(size * U));
        float Tw(string t, int size, bool bold = false) => m(t, Px(size), bold) / U * 1.03f;
        float Th(string t, int size, float width, int lines) => ml(t, Px(size), width * U, lines) / U;

        // ── 위 줄 (0..44) ──
        h.Blocks.Add(("위 줄", new RectF(0, 0, W, 44)));
        float ix = W - Side - 36;
        string[] iconTips = { "메뉴 — 배 바꾸기 · 새 항해 · 처음 화면", "도움말 (H)", f.CaptionsOn ? "소리 자막: 켬 (U)" : "소리 자막: 끔 (U)", s.Mute switch { 0 => "소리: 다 켬 (M)", 1 => "소리: 음악 끔 (M)", _ => "소리: 끔 (M)" } };
        UiAct[] icons = { UiAct.Menu, UiAct.Help, UiAct.Captions, UiAct.Sound };
        for (int i = 0; i < 4; i++)
        {
            var r = new RectF(ix, 6, 36, 32);
            bool on = icons[i] switch { UiAct.Menu => s.Open is HudOpen.Menu or HudOpen.Ships, UiAct.Help => s.Help, UiAct.Captions => f.CaptionsOn, _ => s.Mute == 0 };
            h.Hits.Add(new UiHit(r, icons[i], i, on, true, iconTips[i]));
            h.Areas["아이콘" + i] = r;
            ix -= 36 + 6;
        }
        float badgeW = Tw("새 엔진 판", 15, true) + 22;
        var badge = new RectF(ix + 36 - 12 - badgeW, 9, badgeW, 26);
        h.Areas["표"] = badge;
        // 빠르기 줄: [멈춤][하루 24분]…[×1][▾ 고정] — 오른쪽 끝은 표 왼쪽 − 12
        float right = badge.X - 12;
        var widths = LiveShow.SpeedLabels.Select(l => Math.Max(44, Tw(l, 15) + 20)).ToArray();
        float fixedW = 92, total = 40 + 4 + widths.Sum() + 4 * widths.Length + fixedW;
        float x = right - total;
        var pause = new RectF(x, 6, 40, 32);
        h.Hits.Add(new UiHit(pause, UiAct.Pause, 0, f.Paused, true, f.Holding ? "이대로 출항 (스페이스)" : "멈춤 / 계속 (스페이스)"));
        h.Areas["멈춤"] = pause;
        x += 44;
        for (int i = 0; i < widths.Length; i++)
        {
            var r = new RectF(x, 6, widths[i], 32);
            string tip = i < LiveShow.DayMinutesAt.Length ? $"{LiveShow.SpeedLabels[i]}: 조용할 때 하루가 화면 {LiveShow.SpeedLabels[i].Replace("하루 ", "")} — 일이 나면 연출이 늦춘다 ({i + 1})" : $"실시간 ×1 ({i + 1})";
            if (i == 4) tip = "최대: 기계가 되는 만큼 — 일이 나면 연출이 늦춘다 (5)";
            h.Hits.Add(new UiHit(r, UiAct.Speed, i, !f.Paused && f.SpeedPick == i, true, tip));
            h.Areas["빠르기" + i] = r;
            x += widths[i] + 4;
        }
        var fixedR = new RectF(x, 6, fixedW, 32);
        h.Hits.Add(new UiHit(fixedR, UiAct.FixedMenu, 0, f.SpeedPick == -2 || s.Open == HudOpen.Fixed, true, "고정 빠르기: 일이 나도 늦추지 않는다 (Shift+1~5)"));
        h.Areas["고정"] = fixedR;
        h.Areas["상태 글"] = new RectF(38, 6, Math.Max(0, pause.X - 16 - 38), 32);

        // ── 둘째 줄 (50..80): 판 탭 · 상태 띠 ──
        float tx = W - Side;
        var tabRects = new RectF[5];
        for (int i = 4; i >= 0; i--)
        {
            string label = TabNames[i] + (i == 0 && f.Highlights > 0 ? $" {f.Highlights}" : "");
            float w = Math.Max(64, Tw(label, 15) + 26);
            tx -= w;
            tabRects[i] = new RectF(tx, 50, w, 30);
            tx -= 6;
        }
        string[] tabTips = { "볼 만했던 장면 · 골라서 다시보기 (L)", "항로 · 출항 전에만 고른다 (T)", "주컴퓨터 — 지금 사건 · 배 형편 · 일지 (J)", "사슬 — 지금 장면은 어디서 시작됐나 (C)", "사람 — 모두의 몸 · 마음 (P)" };
        for (int i = 0; i < 5; i++) { h.Hits.Add(new UiHit(tabRects[i], UiAct.Tab, i, f.Panel == TabKinds[i], true, tabTips[i])); h.Areas["탭" + i] = tabRects[i]; }
        h.Blocks.Add(("탭", new RectF(tabRects[0].X, 50, W - Side - tabRects[0].X, 30)));
        float vx = 12, vLimit = tabRects[0].X - 16;
        for (int i = 0; i < f.Vitals.Length; i++)
        {
            var g = f.Vitals[i];
            float w = 30 + Tw(g.Name, 14, true) + 8 + Tw(g.Value, 14) + 12;
            if (vx + w > vLimit) { w = vLimit - vx; if (w < 60) break; }
            var r = new RectF(vx, 50, w, 30);
            h.Hits.Add(new UiHit(r, UiAct.Vital, i, false, true, $"{g.Name}: {g.Value} — 누르면 그 자리로"));
            h.Areas["띠" + i] = r;
            vx += w + 6;
        }

        // ── 아래 (늘 같은 높이): 아래 줄 · 사람 줄 · 보기 묶음 · 범례 ──
        var story = new RectF(0, H - BottomBar, W, BottomBar);
        h.Areas["아래 줄"] = story;
        if (f.StoryIsChain) h.Hits.Add(new UiHit(story, UiAct.Story, 0, false, true, "이 일이 어디서 시작됐나 (C)"));
        else h.Blocks.Add(("아래 줄", story));
        float viewW = 6 * ViewBtn + 5 * ViewGap;
        var view = new RectF(W - Side - viewW, H - BottomBar - 8 - ViewBtn, viewW, ViewBtn);
        h.Areas["보기 묶음"] = view;
        UiAct[] va = { UiAct.Director, UiAct.Overview, UiAct.ZoomOut, UiAct.ZoomIn, UiAct.OverlayMenu, UiAct.ReplayLast };
        string[] vt = { f.Follow ? "연출을 따라가는 중 (F)" : $"연출 따라가기 (F){(f.HandLeft > 0 ? $" — {f.HandLeft:0}초 뒤 저절로" : "")}", "배 전체 보기 (Tab)", "축소 (−)", "확대 (+)", "겹쳐 보기 — 공기 · 열 · 연기 · 사슬 선 · 선과 관 (O)", f.Replaying ? "다시보기 끝내고 돌아오기 (B)" : f.CanReplay ? "방금 그 장면 다시보기 (B)" : f.ReplayWhy };
        for (int i = 0; i < 6; i++)
        {
            var r = new RectF(view.X + i * (ViewBtn + ViewGap), view.Y, ViewBtn, ViewBtn);
            bool on = i switch { 0 => f.Follow && !f.Overview && f.Selected < 0, 1 => f.Overview, 4 => f.Show != Overlay.None || s.Open == HudOpen.Overlay, 5 => f.Replaying, _ => false };
            bool en = i != 5 || f.CanReplay || f.Replaying;
            h.Hits.Add(new UiHit(r, va[i], i, on, en, vt[i], i == 5 ? f.ReplayWhy : ""));
            h.Areas["보기" + i] = r;
        }
        h.Blocks.Add(("보기 묶음", view));
        // 사람 줄 (16..W−16−270−16 · 아래 끝 = 보기 묶음 아래 끝)
        float crewRight = view.X - 16, crewBottom = view.Bottom;
        float crewTop = LayoutCrew(f, h, Side, crewRight, crewBottom, Tw);
        crewTop = LayoutRobots(f, h, Side, crewRight, f.Crew.Length > 0 ? crewTop - 6 : crewBottom, Tw);   // 로봇 칩 줄 (사람 줄 위)
        // 범례
        float legendBottom = view.Y - 8;
        float crewRightEdge = Math.Max(h.Areas.TryGetValue("사람 줄", out var ca) ? ca.Right : Side, h.Areas.TryGetValue("로봇 줄", out var ra) ? ra.Right : Side);
        if (crewRightEdge > W - Side - 420) legendBottom = Math.Min(legendBottom, crewTop - 8);   // 사람 줄이 범례 밑까지 오면 그 위로
        if (f.Show != Overlay.None)
        {
            var lg = new RectF(W - Side - 420, legendBottom - 56, 420, 56);
            h.Areas["범례"] = lg;
            h.Hits.Add(new UiHit(lg, UiAct.OverlayMenu, 99, false, true, "겹쳐 보기 바꾸기"));
            legendBottom = lg.Y - 8;
        }
        float lowTop = Math.Min(crewTop, view.Y) - 8;   // 아래 묶음 위 (방송 · 자막의 아래 끝)

        // ── 왼쪽 칸: 카드 · 사건 띠 ──
        float leftY = 96;
        if (f.Card.Length > 0)
        {
            float lh = 0;
            for (int i = 0; i < f.Card.Length; i++) lh += Math.Max(26, Th(f.Card[i], i == 0 ? 19 : 16, CardW - 28, i == 0 ? 1 : 2) + 6);
            var card = new RectF(Side, leftY, CardW, Math.Min(20 + lh + 8, lowTop - leftY - 40));
            h.Areas["카드"] = card;
            h.Hits.Add(new UiHit(new RectF(Side + CardW - 8 - 28, leftY + 6, 28, 28), UiAct.CardClose, 0, false, true, "놓기 (Esc · 오른쪽 누르기)"));
            h.Hits.Add(new UiHit(new RectF(Side + CardW - 8 - 28 - 6 - 80, leftY + 6, 80, 28), UiAct.CardFollow, 0, f.FollowSelected, true, f.FollowSelected ? "따라가는 중" : "다시 따라가기"));
            h.Hits.Add(new UiHit(new RectF(Side + 16, leftY + 4, CardW - 16 - 8 - 28 - 6 - 80 - 8, 30), UiAct.CardName, 0, false, true, "그 자리로"));
            h.Blocks.Add(("카드", card));
            leftY = card.Bottom + 8;
        }
        for (int i = 0; i < f.Feed.Length; i++)
        {
            var r = new RectF(Side, leftY, CardW, 26);
            if (r.Bottom > lowTop - 30) break;
            h.Areas["사건" + i] = r;
            if (f.Feed[i].Cell >= 0) h.Hits.Add(new UiHit(r, UiAct.Feed, i, false, true, f.Feed[i].Text + " — 누르면 그 자리로"));
            else h.Blocks.Add(("사건" + i, r));
            leftY += 28;
        }

        // ── 오른쪽 판 ──
        float panelLeft = W;
        if (f.Panel != PanelKind.None && f.PanelTitle.Length > 0)
        {
            float pw = Math.Clamp(W - 32 - LeftCol - 560, 420, 640);
            panelLeft = W - Side - pw;
            float bottom = Math.Min(f.Show != Overlay.None ? legendBottom : view.Y - 8, crewRightEdge > panelLeft ? crewTop - 8 : float.MaxValue);   // 판 아래 끝: 가로로 겹치는 것 (범례 · 보기 묶음 · 사람 줄) 가운데 가장 위의 것 − 8
            LayoutPanel(f, s, h, new RectF(panelLeft, 96, pw, bottom - 96), Tw, Th);
        }

        // ── 주컴퓨터 말 창 (판이 닫혔을 때 · 탭 밑 오른쪽 · 누르면 주컴퓨터 판) ──
        if (f.Panel == PanelKind.None && f.Talk.Length > 0)
        {
            float tw = Math.Clamp(W - LeftCol - 600 - 2 * Side, 300, 440);
            var r = new RectF(W - Side - tw, 96, tw, 34 + f.Talk.Length * 24 + 6);
            h.Areas["말 창"] = r;
            h.Hits.Add(new UiHit(r, UiAct.Talk, 0, false, true, "주컴퓨터 판에서 보기 (J)"));
            h.Blocks.Add(("말 창", r));
            panelLeft = r.X;
        }

        // ── 가운데 칸: 다시보기 띠 → 출항 준비 띠 → ★ → 알림 ──
        float colL = LeftCol, colR = (f.Panel != PanelKind.None || f.Talk.Length > 0 ? panelLeft : W) - Side, cy = 96;
        float colW = Math.Max(200, colR - colL);
        RectF Center(float w, float hgt) { w = Math.Min(w, colW); float cx = Math.Clamp(W / 2 - w / 2, colL, colR - w); return new RectF(cx, cy, w, hgt); }
        if (f.Replaying)
        {
            var r = Center(Math.Min(colW, Tw("다시보기 · " + f.Replay, 16, true) + 40 + 112 + 12), 40);
            h.Areas["다시보기 띠"] = r;
            h.Hits.Add(new UiHit(new RectF(r.Right - 8 - 112, r.Y + 4, 112, 32), UiAct.ReplayStop, 0, false, true, "다시보기 끝내고 돌아오기 (B)"));
            h.Blocks.Add(("다시보기 띠", r));
            cy = r.Bottom + 8;
        }
        if (f.Holding && f.Panel != PanelKind.Route)
        {
            var r = Center(560, 44);
            h.Areas["출항 띠"] = r;
            h.Hits.Add(new UiHit(new RectF(r.Right - 8 - 150, r.Y + 4, 150, 36), UiAct.Depart, 0, false, true, "이대로 출항 (스페이스)"));
            h.Hits.Add(new UiHit(new RectF(r.Right - 8 - 150 - 6 - 120, r.Y + 4, 120, 36), UiAct.RouteTab, 0, false, true, "항로 고르기 (T)"));
            h.Blocks.Add(("출항 띠", r));
            cy = r.Bottom + 8;
        }
        if (f.Banner.Length > 0 && f.BannerAlpha > 0.05f)
        {
            bool btn = f.CanReplay && !f.Replaying;
            float tw = Math.Min(Tw("★ " + f.Banner, 22, true), colW - 40 - (btn ? 104 : 0));
            float th = Th("★ " + f.Banner, 22, tw, 2);
            var r = Center(tw + 40 + (btn ? 104 : 0), th + 18);
            h.Areas["★ 띠"] = r;
            if (btn) h.Hits.Add(new UiHit(new RectF(r.Right - 8 - 96, r.Y + (r.H - 28) / 2, 96, 28), UiAct.BannerReplay, 0, false, true, "그 장면 다시보기 (B)"));
            h.Blocks.Add(("★ 띠", r));
            cy = r.Bottom + 8;
        }
        if (f.Notice.Length > 0)
        {
            float tw = Math.Min(Tw(f.Notice, 17), colW - 40);
            float th = Th(f.Notice, 17, tw, 2);
            var r = Center(tw + 32 + (f.NoticeLink != PanelKind.None ? 16 : 0), Math.Max(36, th + 14));
            h.Areas["알림"] = r;
            if (f.NoticeLink != PanelKind.None) h.Hits.Add(new UiHit(r, UiAct.Notice, 0, false, true, "눌러서 보기"));
            else h.Blocks.Add(("알림", r));
        }

        // ── 방송 자막 (가운데 아래 · 아래 끝 = 사람 줄 위 − 8) ──
        if (f.Broadcast.Length > 0 && f.BroadcastAlpha > 0.05f)
        {
            float lowR = (f.Panel != PanelKind.None ? panelLeft : view.X) - Side;
            float bw = Math.Min(980, Math.Max(200, lowR - colL));
            float th = Th(f.Broadcast, 17, bw - 40, 3);
            float bx = Math.Clamp(W / 2 - bw / 2, colL, Math.Max(colL, lowR - bw));
            var r = new RectF(bx, lowTop - (th + 22), bw, th + 22);
            h.Areas["방송"] = r;
            h.Hits.Add(new UiHit(r, UiAct.Broadcast, 0, false, true, "주컴퓨터 판에서 보기 (J)"));
        }
        // 소리 자막 (왼쪽 · 누름이 지나간다)
        if (f.Captions.Length > 0) h.Areas["자막"] = new RectF(Side, lowTop - f.Captions.Length * 26, CardW, f.Captions.Length * 26);

        // ── 펼친 목록 · 창 ──
        if (s.Open == HudOpen.Fixed)
        {
            var area = new RectF(fixedR.Right - (5 * 64 + 4 * 4 + 16), 46, 5 * 64 + 4 * 4 + 16, 48);
            h.Blocks.Insert(0, ("목록 고정", area)); h.Areas["목록 고정"] = area;
            for (int i = 0; i < 5; i++) h.Hits.Insert(0, new UiHit(new RectF(area.X + 8 + i * 68, area.Y + 8, 64, 32), UiAct.Fixed, i, f.FixedPick == i, true, $"고정 빠르기 ×{LiveShow.FixedGameSecPerSec[i]:0} (Shift+{i + 1})"));
        }
        if (s.Open == HudOpen.Overlay)
        {
            var area = new RectF(W - Side - 260, view.Y - 8 - 6 * 34 - 16, 260, 6 * 34 + 16);
            h.Blocks.Insert(0, ("목록 겹쳐 보기", area)); h.Areas["목록 겹쳐 보기"] = area;
            for (int i = 0; i < 6; i++) h.Hits.Insert(0, new UiHit(new RectF(area.X + 8, area.Y + 8 + i * 34, 244, 32), UiAct.Overlay, i, (int)f.Show == i, true, LiveShow.OverlayName((Overlay)i)));
        }
        if (s.Open is HudOpen.Menu or HudOpen.Ships)
        {
            string[] items = { "배 바꾸기 ›", "같은 배로 새 항해", "소리", "소리 자막", "말풍선 (K)", "속마음 풍선 (I)", "도움말", "처음 화면으로" };
            UiAct[] acts = { UiAct.MenuShip, UiAct.MenuRestart, UiAct.Sound, UiAct.Captions, UiAct.Speech, UiAct.Thoughts, UiAct.Help, UiAct.MenuStart };
            var area = new RectF(W - Side - 240, 46, 240, items.Length * 38 + 12);
            h.Blocks.Insert(0, ("목록 메뉴", area)); h.Areas["목록 메뉴"] = area;
            for (int i = items.Length - 1; i >= 0; i--) h.Hits.Insert(0, new UiHit(new RectF(area.X + 6, area.Y + 6 + i * 38, 228, 36), acts[i], 100 + i, false, true, items[i]));
            if (s.Open == HudOpen.Ships && ships != null)
            {
                var sa = new RectF(area.X - 8 - 260, 46, 260, ships.Length * 38 + 12);
                h.Blocks.Insert(0, ("목록 배", sa)); h.Areas["목록 배"] = sa;
                for (int i = ships.Length - 1; i >= 0; i--) h.Hits.Insert(0, new UiHit(new RectF(sa.X + 6, sa.Y + 6 + i * 38, 248, 36), UiAct.ShipPick, i, false, true, ships[i]));
            }
        }
        if (s.Help)
        {
            float hw = Math.Min(760, W - 64), hh = Math.Min(H - 160, 560);
            var r = new RectF(W / 2 - hw / 2, H / 2 - hh / 2, hw, hh);
            h.Areas["창 도움말"] = r;
            h.Hits.Insert(0, new UiHit(new RectF(r.Right - 8 - 32, r.Y + 8, 32, 32), UiAct.Help, 1, false, true, "닫기 (H · Esc · 오른쪽 누르기)"));
            h.Hits.Insert(1, new UiHit(new RectF(0, 0, W, H), UiAct.Help, 2, false, true, ""));   // 아무 데나 누르면 닫힌다
            h.Modal = true;
        }
        if (s.Ask != HudAsk.None)
        {
            var r = new RectF(W / 2 - 230, H / 2 - 85, 460, 170);
            h.Areas["창 확인"] = r;
            h.Hits.Clear();
            h.Hits.Add(new UiHit(new RectF(r.X + 70, r.Bottom - 56, 150, 40), UiAct.AskNo, 0, s.AskFocus == 0, true, "그만두기 (Esc)"));
            h.Hits.Add(new UiHit(new RectF(r.Right - 70 - 150, r.Bottom - 56, 150, 40), UiAct.AskYes, 0, s.AskFocus == 1, true, ""));
            h.Modal = true;
        }
    }

    /// <summary>사람 줄: 한 줄 이름까지 (×2) → 두 줄 (×1.5) → 넘치면 앞에서부터 (+n) · 칸은 적어도 30×30. 위 끝을 돌려준다.</summary>
    private static float LayoutCrew(LiveFrame f, Hud h, float left, float right, float bottom, Func<string, int, bool, float> tw)
    {
        int n = f.Crew.Length;
        if (n == 0) return bottom;
        float avail = right - left;
        float scale = 2; bool names = true; int rows = 1;
        float fw0 = Painter.FaceW, fh0 = Painter.FaceH;
        if (n * (fw0 * 2 + 16) > avail) { scale = 1.5f; names = false; rows = n * (fw0 * 1.5f + 6) > avail ? 2 : 1; }
        if (n * (fw0 * 1.5f + 6) > avail * 2) scale = 1.25f;
        float fw = Math.Max(30, fw0 * scale), fh = Math.Max(30 - 4, fh0 * scale);
        float slot = fw + (names ? 16 : 6), boxH = fh + (names ? 20 : 4);
        int perRow = Math.Max(1, (int)((avail - 44) / slot));
        int fit = Math.Min(n, perRow * rows);
        if (rows == 2 && fit <= perRow) rows = 1;
        if (rows == 2) perRow = (fit + 1) / 2;
        float y0 = bottom - boxH * rows - (rows - 1) * 4;
        float x = left, y = y0;
        for (int i = 0; i < fit; i++)
        {
            if (i > 0 && i % perRow == 0) { x = left; y += boxH + 4; }
            var c = f.Crew[i];
            var r = new RectF(x, y, slot - 4, boxH);
            h.CrewSlots.Add((c.Id, r));
            h.Hits.Add(new UiHit(r, UiAct.Crew, c.Id, c.Id == f.Selected, true, $"{c.Name} — {c.State} · 누르면 따라간다"));
            x += slot;
        }
        h.FaceScale = scale; h.CrewNames = names;
        if (fit < n)
        {
            h.CrewMore = n - fit;
            var more = new RectF(x, y + (boxH - 28) / 2, 44, 28);
            h.Hits.Add(new UiHit(more, UiAct.CrewMore, 0, false, true, "모두 보기 (P)"));
            h.Areas["사람 더"] = more;
        }
        var crewArea = new RectF(left, y0, (fit < n ? x + 44 : x) - left, bottom - y0);
        h.Areas["사람 줄"] = crewArea;
        h.Blocks.Add(("사람 줄", crewArea));
        return y0;
    }

    public const float ChipH = 26;

    /// <summary>로봇 칩 줄: 'R1 불' (칩 글 + 하는 일 낱말) · 넘치면 '+N' (사람 판의 로봇 줄로) — 위 끝을 돌려준다.</summary>
    private static float LayoutRobots(LiveFrame f, Hud h, float left, float right, float bottom, Func<string, int, bool, float> tw)
    {
        int n = f.Robots.Length;
        if (n == 0) return bottom;
        float y = bottom - ChipH, x = left;
        int fit = 0;
        foreach (var c in f.Robots)
        {
            float w = Math.Max(52, tw(c.Tag, 13, true) + 6 + tw(c.Word, 13, false) + 22);
            if (x + w > right - (fit + 1 < n ? 48 : 0)) break;
            h.Hits.Add(new UiHit(new RectF(x, y, w, ChipH), UiAct.Robot, c.Id, c.Id == f.Selected, true, c.Tip));
            x += w + 4; fit++;
        }
        if (fit < n)
        {
            var more = new RectF(x, y, 44, ChipH);
            h.Hits.Add(new UiHit(more, UiAct.RobotMore, 0, false, true, $"로봇 {n - fit} 더 — 사람 판에서 보기 (P)"));
            h.Areas["로봇 더"] = more;
            x += 48;
        }
        var area = new RectF(left, y, Math.Max(0, x - 4 - left), ChipH);
        h.Areas["로봇 줄"] = area;
        h.Blocks.Add(("로봇 줄", area));
        return y;
    }

    /// <summary>판: 머리 50 (제목 · [사슬 선] · ×) · 출항 준비 줄 · 보이는 줄 (넘기기 — PanelFirst부터 · 고른 줄이 바뀌면 보이게) · 넘김 막대 · 바닥 34.</summary>
    private static void LayoutPanel(LiveFrame f, HudState s, Hud h, RectF box0, Func<string, int, bool, float> tw, Func<string, int, float, int, float> th)
    {
        if (s.PanelSeen != f.Panel) { s.PanelSeen = f.Panel; s.PanelFirst = 0; s.PickSeen = long.MinValue; s.WheelAcc = 0; }
        float inner = box0.W - 36 - 18;
        float RowH(PanelRow r)
        {
            float face = r.Face >= 0 ? 56 : 0;
            float main = th((r.On ? "● " : "") + r.Main, 16, inner - face - (r.Act.Length > 0 ? 104 : 0), 2);
            float sub = r.Sub.Length > 0 ? th(r.Sub, 15, inner - face, 8) + 4 : 0;
            return Math.Max(Math.Max(28, r.Face >= 0 ? 54 : 0), 10 + main + (r.Bars is { Length: > 0 } ? 16 : 0) + sub + 4);
        }
        float top = box0.Y + 50 + (f.Holding && f.Panel == PanelKind.Route ? 52 : 0);
        float room = box0.Bottom - 34 - top;
        int total = f.PanelRows.Length;
        h.PanelTotal = total;
        // 고른 줄이 키로 바뀌었으면 보이게 첫 줄을 맞춘다 (휠로 넘긴 뒤에는 그 자리 그대로)
        if (f.PanelPickKey != s.PickSeen)
        {
            s.PickSeen = f.PanelPickKey;
            if (f.PanelPick >= 0 && f.PanelPick < total)
            {
                if (f.PanelPick < s.PanelFirst) s.PanelFirst = f.PanelPick;
                else
                {
                    float acc = 0;
                    for (int i = s.PanelFirst; i <= f.PanelPick; i++) acc += RowH(f.PanelRows[i]);
                    while (acc > room && s.PanelFirst < f.PanelPick) { acc -= RowH(f.PanelRows[s.PanelFirst]); s.PanelFirst++; }
                }
            }
        }
        s.PanelFirst = Math.Clamp(s.PanelFirst, 0, Math.Max(0, total - 1));
        float used = 0; int last = s.PanelFirst;
        var heights = new List<float>();
        while (last < total)
        {
            float rh = RowH(f.PanelRows[last]);
            if (used + rh > room && last > s.PanelFirst) break;
            heights.Add(rh); used += rh; last++;
        }
        h.PanelFirst = s.PanelFirst; h.PanelLast = last;
        var box = new RectF(box0.X, box0.Y, box0.W, Math.Min(box0.H, top - box0.Y + used + 34 + 8));
        h.Areas["판"] = box;
        // 머리: × · 사슬 선 (사슬 판)
        h.Hits.Add(new UiHit(new RectF(box.Right - 8 - 32, box.Y + 8, 32, 32), UiAct.PanelClose, 0, false, true, "닫기 (Esc · 오른쪽 누르기)"));
        if (f.Panel == PanelKind.Chain) h.Hits.Add(new UiHit(new RectF(box.Right - 8 - 32 - 6 - 132, box.Y + 10, 132, 28), UiAct.ChainLines, 0, f.Show == Overlay.Chain, true, "사슬 선 보기 (O)"));
        if (f.Holding && f.Panel == PanelKind.Route)
        {
            var hold = new RectF(box.X + 8, box.Y + 50, box.W - 16, 44);
            h.Areas["출항 줄"] = hold;
            h.Hits.Add(new UiHit(new RectF(hold.Right - 4 - 160, hold.Y + 4, 160, 36), UiAct.Depart, 0, false, true, "이대로 출항 (스페이스)"));
        }
        float y = top;
        for (int i = s.PanelFirst; i < last; i++)
        {
            var row = f.PanelRows[i];
            var r = new RectF(box.X + 6, y, box.W - 12 - (total > last - s.PanelFirst ? 18 : 0), heights[i - s.PanelFirst]);
            h.PanelLines.Add((i, r));
            if (row.Act.Length > 0) h.Hits.Add(new UiHit(new RectF(r.Right - 4 - 96, r.Y + 2, 96, 28), UiAct.PanelRowAct, row.Key, false, row.ActOn, row.ActOn ? "그 장면 다시보기 (Enter)" : row.Why, row.Why));
            if (row.Key != 0) h.Hits.Add(new UiHit(r, UiAct.PanelRow, row.Key, i == f.PanelPick, f.Panel != PanelKind.Route || row.ActOn || row.Key < 1, "", row.Why));
            y += r.H;
        }
        if (total > last - s.PanelFirst)
        {
            var bar = new RectF(box.Right - 4 - 18, top, 18, used);
            h.Areas["막대"] = bar;
            h.Hits.Add(new UiHit(bar, UiAct.PanelBar, 0, false, true, ""));
        }
        h.Blocks.Add(("판", box));
    }
}
