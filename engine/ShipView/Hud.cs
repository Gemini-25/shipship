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
    DetailTab, DetailBack, DetailFollow, DetailOpen, HandSize, HandCancel, HandNow, LookHand, Inset,
    // 항해 번호 · 이어 보기 (docs/OBSERVER.md §3.3 · 3.4 — V2): 번호 창 (숫자판 0~9 · 10 지움 · 11 새로 뽑기 · 배 ‹ ›) · 출항 준비 [다른 사람들로] · '지난번까지' [계속] · 처음 화면 카드
    VoyageKey, VoyageShip, Recrew, ResumeGo, StartResume, StartOther, StartNew, StartPick, StartRenew, StartDelete, StartChoice, StartQuit,
    // 기록 (docs/OBSERVER.md §5 — V3): 장 카드 (누르면 기록 그 장) · 날 띠의 칸 · 메뉴 [항해 끝내기] · 결산 카드 [같은 배로 새 항해] [그대로 지켜보기]
    ChapterCard, DayCell, MenuEnd, EndNew, EndWatch,
    // 장면 금고 (docs/OBSERVER.md §4.3 — V4): '감는 중' 띠의 [그만]
    RewindStop,
}

/// <summary>누를 자리 하나: 네모 · 하는 일 · 열쇠 (단추 번호 · 판 줄 열쇠 · 사람 번호) · 켜짐 · 누를 수 있나 · 풀이 글 · 못 누를 때의 까닭.</summary>
public sealed record UiHit(RectF R, UiAct Act, long Key = 0, bool On = false, bool Enabled = true, string Tip = "", string Why = "");

public enum RouteKind { World, Blocked, Hit }

/// <summary>한 점이 어디로 가나: 세계 · 가리는 판 (아무 일 없음) · 손잡이.</summary>
public readonly record struct Route(RouteKind Kind, UiHit? Hit = null, string Block = "");

/// <summary>열린 펼친 목록 · 창.</summary>
public enum HudOpen { None, Fixed, Overlay, Menu, Ships }
public enum HudAsk { None, Ship, Restart, Start, NewVoyage, End }
public enum BackStep { None, CloseAsk, CloseOpen, CloseHelp, CloseCard, Disarm, CloseDetail, ClosePanel, Release }

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
    /// <summary>상태 띠가 탭 왼쪽을 넘어 칸마다 짧은 값을 쓴다 (docs/UI.md §2 · UI2.md §7 결함 5) — 그리는 쪽이 같은 값을 쓴다.</summary>
    public bool VitalsShort;
    /// <summary>지나온 날 띠의 칸 (기록 판 — 넘치면 이레 칸): 네모 · 분위기 · 잃은 이 · 오늘 (그리는 쪽이 읽는다) · 결산 카드에 보이는 줄 수.</summary>
    public readonly List<(RectF R, int Mood, bool Lost, bool Today)> DayCells = new();
    public int EndShown;

    public void Clear() { Hits.Clear(); Blocks.Clear(); Areas.Clear(); PanelLines.Clear(); CrewSlots.Clear(); DayCells.Clear(); CrewMore = 0; EndShown = 0; Modal = false; VitalsShort = false; }

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

    /// <summary>도움말 창의 줄 (UI.md §6.3 · docs/UI2.md U9 — 메인이 그리고 시험이 글자를 본다).</summary>
    public static string[] HelpLines(string route) => new[]
    {
        "빠르기 단추   멈춤 · 하루 24분 · 10분 · 3분 · 1분 · 최대 · 실시간 — 일이 나면 연출이 늦춘다 (스페이스 · 1~6)",
        "고정 빠르기 단추   ×1 · ×10 · ×60 · ×600 · ×3600 — 늦추지 않는다 (Shift + 1~5)",
        "끌기 · 가운데 단추 끌기   카메라 옮기기 (W A S D · 화살표)",
        "휠 · 두 번 누르기   커서 쪽으로 확대 / 축소 · 판 위에서는 넘기기 (+ −)",
        "사람 · 로봇 누르기 · 사람 줄 · 로봇 칩 누르기   생각 · 몸 보기 · 따라가기 — 로봇은 하는 일 · 맡은 까닭 · 최근 판단 (카드의 × 로 놓기 · Esc)",
        "주컴퓨터 말 창 (오른쪽 위)   주컴퓨터가 방금 한 말 세 줄 — 누르면 주컴퓨터 판 (J)",
        "오른쪽 누르기   뒤로 — 펼친 목록 → 판 → 고른 이 (Esc)",
        "판 탭   장면 (L) · 항로 (T) · 주컴퓨터 (J) · 사슬 (C) · 사람 (P) · 로봇 (G) · 사고 (X) · 기록 (Y) — 줄을 눌러 고른다",
        "사고 탭   줄을 누르고 배 위를 누르면 일어난다 — 되돌릴 수 없다 (다시보기로 볼 수는 있다) · 오른쪽 누르기로 그만두기 (X)",
        "물건 누르기   기체 · 배선 말고 무엇이든 — 왼쪽 카드에 이름 · 풀이 · 상태 (살펴보기) · 카드의 [사고 ›]로 그 자리에 겨눈다",
        "사람 · 로봇 · 주컴퓨터 판   자세히 — 몸 · 마음 · 하는 일 · 아는 것 · 일지 · 주컴퓨터는 예측과 맞음 몫까지 (P · G · J)",
        "구석 작은 창   따라가는 동안 다른 곳에 위험이 나면 그 자리를 작게 보인다 — 누르면 그리로",
        "오른쪽 아래   연출 따라가기 (F) · 배 전체 (Tab) · 축소 · 확대 · 겹쳐 보기 (O) · 방금 장면 다시보기 (B)",
        "겹쳐 보기   공기 · 열 · 연기 · 사슬 선 · 선과 관",
        "위 오른쪽   소리 (M) · 소리 자막 (U) · 도움말 (H) · 메뉴 — 말풍선 (K) · 속마음 풍선: 끔 · 고른 이 · 화면 안 셋 (I)",
        "메뉴   배 바꾸기 (N 두 번) · 새 항해 — 번호를 넣거나 새로 뽑는다 (R 두 번) · 항해 끝내기 (결산) · 처음 화면 (Esc 두 번) · 지금 항해는 저장해 둔다",
        "띠 · 알림 누르기   그 자리 · 그 판으로",
        $"항로   출항 전에만 여덟 항로 가운데 고른다 · 지금 항로: {route}",
    };
}

/// <summary>메인 스레드만의 화면 상태 (세계 아님): 펼친 목록 · 창 · 판 넘김 · 누른 손잡이 · 올림.</summary>
public sealed class HudState
{
    public HudOpen Open;
    public bool Help;
    public HudAsk Ask;
    public string AskShip = "";
    public int AskFocus;              // 0 그만두기 · 1 하기
    /// <summary>번호 창 (HudAsk.NewVoyage): 넣은 번호 (숫자 글 · 다섯 자리까지) · 고른 배 (배 목록 차례) · 띄우면 지울 가장 오래된 항해 (빈 글: 없음).</summary>
    public string AskNumber = ""; public int AskShipAt; public string AskPrune = "";

    /// <summary>번호 창 숫자판: 0~9 붙이기 (다섯 자리까지 · 앞 0은 뗀다) · 10 지움 · 11 새로 뽑기.</summary>
    public void VoyageKey(int k)
    {
        if (k is >= 0 and <= 9) { if (AskNumber.Length >= 5 || AskNumber.Length == 0 && k == 0) return; AskNumber += (char)('0' + k); }
        else if (k == 10) { if (AskNumber.Length > 0) AskNumber = AskNumber[..^1]; }
        else if (k == 11) AskNumber = VoyageNumber.Draw().ToString();
    }

    /// <summary>넣은 번호가 띄울 수 있는 번호인가 (1000 ~ 99999).</summary>
    public bool AskNumberOk => VoyageNumber.Parse(AskNumber) != null;

    /// <summary>번호 창을 연다 (지금 배 · 지금 번호로 — 넣을 수 없는 번호 (확인용 18)면 새로 뽑아 둔다).</summary>
    public void OpenVoyage(int shipAt, ulong number)
    {
        Ask = HudAsk.NewVoyage; AskShipAt = shipAt; AskFocus = 1; Open = HudOpen.None;
        AskNumber = VoyageNumber.Parse(number.ToString()) != null ? number.ToString() : VoyageNumber.Draw().ToString();
    }
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
    public int DetailSeen = -1;
    /// <summary>밀어 던지기 끌기 (docs/UI2.md §6.3): 누른 물체 Id (0 없음) · 시작 자리 (칸 · 소수).</summary>
    public int HandDrag; public double HandDragX, HandDragY;
    /// <summary>마지막으로 작업 스레드에 보낸 올린 자리 (칸이 0.02 넘게 바뀔 때만 다시 보낸다).</summary>
    public double HoverSentX = double.NaN, HoverSentY = double.NaN;

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
        HudOpen.Menu => a is UiAct.MenuShip or UiAct.MenuRestart or UiAct.MenuEnd or UiAct.MenuStart or UiAct.Sound or UiAct.Captions or UiAct.Speech or UiAct.Thoughts or UiAct.Help or UiAct.Menu,
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

    /// <summary>뒤로 (오른쪽 누르기 · Esc): 확인 창 → 펼친 목록 → 도움말 → 결산 카드 (그대로 지켜보기) → 겨눔 (그만두기) → 자세히 (목록으로 — 사람 · 로봇) → 판 → 고른 이 → 없음. 다시보기는 끝내지 않는다 (docs/UI2.md §6.5 · OBSERVER.md §15.7).</summary>
    public BackStep Back(LiveFrame f)
    {
        if (Ask != HudAsk.None) { Ask = HudAsk.None; return BackStep.CloseAsk; }
        if (Open != HudOpen.None) { Open = HudOpen.None; return BackStep.CloseOpen; }
        if (Help) { Help = false; return BackStep.CloseHelp; }
        if (f.EndCard.Length > 0) return BackStep.CloseCard;
        if (f.Armed != HandKind.None) return BackStep.Disarm;
        if (f.Panel != PanelKind.None && f.Detail is DetailKind.Person or DetailKind.Robot) return BackStep.CloseDetail;   // 주컴퓨터 판은 목록이 없다 — 판을 닫는다
        if (f.Panel != PanelKind.None) return BackStep.ClosePanel;
        if (f.Selected >= 0 || f.CardIs == CardKind.Thing) return BackStep.Release;   // 고른 이 · 고른 물건
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
    /// <summary>판 탭 여덟 (docs/UI2.md §5.1 — 0~4는 그대로라 키 번호가 바뀌지 않는다 · 로봇 5 · 사고 6 · 기록 7 — docs/OBSERVER.md §15.1).</summary>
    public static readonly string[] TabNames = { "장면", "항로", "주컴퓨터", "사슬", "사람", "로봇", "사고", "기록" };
    public static readonly PanelKind[] TabKinds = { PanelKind.Highlights, PanelKind.Route, PanelKind.Journal, PanelKind.Chain, PanelKind.People, PanelKind.Robots, PanelKind.Hand, PanelKind.Record };
    public static readonly string[] TabKeys = { "L", "T", "J", "C", "P", "G", "X", "Y" };
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
        var tabRects = new RectF[TabNames.Length];
        for (int i = TabNames.Length - 1; i >= 0; i--)
        {
            string label = TabNames[i] + (i == 0 && f.Highlights > 0 ? $" {f.Highlights}" : "");
            float w = Math.Max(64, Tw(label, 15) + 26);
            tx -= w;
            tabRects[i] = new RectF(tx, 50, w, 30);
            tx -= 6;
        }
        string[] tabTips = { "볼 만했던 장면 · 골라서 다시보기 (L)", "항로 · 출항 전에만 고른다 (T)", "주컴퓨터 — 지금 · 예측 · 아는 것 · 일감 · 말 기록 (J)", "사슬 — 지금 장면은 어디서 시작됐나 (C)", "사람 — 모두의 몸 · 마음 · 누르면 자세히 (P)",
            "로봇 · 드론 — 전지 · 몸 · 하는 일 (G)", "사고 일으키기 — 불 · 구멍 · 고장 … (X)", "기록 — 지나온 날 · 잃은 이 · 사진첩 · 항해 (Y)" };
        for (int i = 0; i < TabNames.Length; i++) { h.Hits.Add(new UiHit(tabRects[i], UiAct.Tab, i, f.Panel == TabKinds[i], true, tabTips[i])); h.Areas["탭" + i] = tabRects[i]; }
        h.Blocks.Add(("탭", new RectF(tabRects[0].X, 50, W - Side - tabRects[0].X, 30)));
        float vx = 12, vLimit = tabRects[0].X - 16;
        // 띠가 탭 왼쪽을 넘으면 칸마다 짧은 값 (전체 값은 풀이 글 — docs/UI.md §2 · UI2.md §7 결함 5)
        float full = 12;
        foreach (var g in f.Vitals) full += 30 + Tw(g.Name, 14, true) + 8 + Tw(g.Value, 14) + 12 + 6;
        h.VitalsShort = full - 6 > vLimit && f.Vitals.Any(g => g.Short.Length > 0);
        for (int i = 0; i < f.Vitals.Length; i++)
        {
            var g = f.Vitals[i];
            float w = 30 + Tw(g.Name, 14, true) + 8 + Tw(h.VitalsShort && g.Short.Length > 0 ? g.Short : g.Value, 14) + 12;
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
            bool thing = f.CardIs == CardKind.Thing, more = !thing && f.Selected >= 0;   // 사람 · 로봇 카드: 맨 아래 [자세히 보기] · 물건 카드: [사고 ›] (docs/UI2.md §5.4)
            var card = new RectF(Side, leftY, CardW, Math.Max(0, Math.Min(20 + lh + 8 + (more ? 36 : 0), lowTop - leftY - 40)));   // 아주 낮은 창에서는 0 (음수 높이를 그리지 않는다)
            if (more) h.Hits.Add(new UiHit(new RectF(Side + 8, card.Bottom - 36, CardW - 16, 28), UiAct.DetailOpen, 0, false, true, "자세히 보기 — 몸 · 마음 · 하는 일 · 아는 것 · 일지"));
            h.Areas["카드"] = card;
            h.Hits.Add(new UiHit(new RectF(Side + CardW - 8 - 28, leftY + 6, 28, 28), UiAct.CardClose, 0, false, true, "놓기 (Esc · 오른쪽 누르기)"));
            if (thing) h.Hits.Add(new UiHit(new RectF(Side + CardW - 8 - 28 - 6 - 80, leftY + 6, 80, 28), UiAct.LookHand, 0, false, f.LookHandOk, "이것에 사고를 일으킨다 — 사고 판이 열리고 겨눈다", f.LookHandWhy));
            else h.Hits.Add(new UiHit(new RectF(Side + CardW - 8 - 28 - 6 - 80, leftY + 6, 80, 28), UiAct.CardFollow, 0, f.FollowSelected, true, f.FollowSelected ? "따라가는 중" : "다시 따라가기"));
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
            float pw = f.Detail != DetailKind.None ? Math.Clamp(W - 32 - LeftCol - 300, 560, 920) : Math.Clamp(W - 32 - LeftCol - 560, 420, 640);   // 자세한 판은 넓게 (docs/UI2.md §5.2)
            panelLeft = W - Side - pw;
            float bottom = Math.Min(f.Show != Overlay.None ? legendBottom : view.Y - 8, crewRightEdge > panelLeft ? crewTop - 8 : float.MaxValue);   // 판 아래 끝: 가로로 겹치는 것 (범례 · 보기 묶음 · 사람 줄) 가운데 가장 위의 것 − 8
            LayoutPanel(f, s, h, new RectF(panelLeft, 96, pw, Math.Max(0, bottom - 96)), Tw, Th);
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
        // 칸이 200보다 좁으면 (작은 창 · 넓은 자세한 판 · 화면 없이 띄운 창 64×64) 오른쪽 끝이 왼쪽 끝 밑으로 내려간다 — 칸 왼쪽에 붙인다 (예전엔 Math.Clamp가 장마다 예외를 던져 자리표가 비었다)
        RectF Center(float w, float hgt) { w = Math.Min(w, colW); float cx = Math.Clamp(W / 2 - w / 2, colL, Math.Max(colL, colR - w)); return new RectF(cx, cy, w, hgt); }
        if (f.Replaying)
        {
            var r = Center(Math.Min(colW, Tw("다시보기 · " + f.Replay, 16, true) + 40 + 112 + 12), 40);
            h.Areas["다시보기 띠"] = r;
            h.Hits.Add(new UiHit(new RectF(r.Right - 8 - 112, r.Y + 4, 112, 32), UiAct.ReplayStop, 0, false, true, "다시보기 끝내고 돌아오기 (B)"));
            h.Blocks.Add(("다시보기 띠", r));
            cy = r.Bottom + 8;
        }
        if (f.Rewind.Length > 0)
        {
            // 감는 중 띠 (V4 — 금고에서 놓친 장면을 0시 세계에서 감는다 · 산 세계는 멈춘다): 글 · [그만]
            var r = Center(Math.Min(colW, Tw(f.Rewind, 16, true) + 40 + 112 + 12), 40);
            h.Areas["감기 띠"] = r;
            h.Hits.Add(new UiHit(new RectF(r.Right - 8 - 112, r.Y + 4, 112, 32), UiAct.RewindStop, 0, false, true, "감기를 그만두고 돌아오기 (B)"));
            h.Blocks.Add(("감기 띠", r));
            cy = r.Bottom + 8;
        }
        if (f.EndCard.Length > 0)
        {
            // 결산 카드 (docs/OBSERVER.md §5.6 — 멈춘다): 제목 · 사람마다 한 줄 · 큰 장면 · [같은 배로 새 항해 — 새 사람들] [그대로 지켜보기] — 칸이 좁으면 단추를 두 줄로 · 낮으면 줄을 덜 보인다
            const float Pad = 14, HeadH = 34, LineH = 22, BtnH = 40;
            float wide = f.EndCard.Select((l, i) => Tw(l, i == 0 ? 20 : 15, i == 0)).Max() + 48;
            float w = Math.Min(colW, Math.Clamp(wide, 600, 820));   // 긴 줄은 자른다 (카드가 가운데 칸을 다 덮지 않게)
            float w1 = Math.Min(w - 32, Math.Max(150, Tw(EndButton(true), 15) + 28)), w2 = Math.Min(w - 32, Math.Max(150, Tw(EndButton(false), 15, true) + 28));
            bool stack = w1 + 8 + w2 > w - 32;
            float btns = stack ? 2 * BtnH + 8 : BtnH;
            float keep = (f.Notice.Length > 0 ? 52 : 0) + (f.ChapterTitle.Length > 0 ? 120 : 0);   // 아래에 올 알림 · 장 카드 자리 (★ 띠는 결산 카드가 있는 동안 띄우지 않는다 — 큰 장면은 카드에)
            int fit = (int)Math.Floor((lowTop - cy - 8 - keep - 2 * Pad - HeadH - 12 - btns) / LineH);
            int shown = Math.Clamp(f.EndCard.Length - 1, 0, Math.Max(0, fit));
            h.EndShown = 1 + shown;
            var r = Center(w, Pad + HeadH + shown * LineH + 12 + btns + Pad);
            h.Areas["결산 카드"] = r;
            float by = r.Bottom - Pad - BtnH;
            var watch = new RectF(r.Right - 16 - w2, by, w2, BtnH);
            var fresh = stack ? new RectF(r.Right - 16 - w1, by - 8 - BtnH, w1, BtnH) : new RectF(watch.X - 8 - w1, by, w1, BtnH);
            h.Hits.Add(new UiHit(fresh, UiAct.EndNew, 0, false, true, "같은 배 · 새 번호 · 새 사람들로 띄운다 (이 항해는 저장해 두었다)"));
            h.Hits.Add(new UiHit(watch, UiAct.EndWatch, 0, true, true, "카드를 닫고 그대로 지켜본다 (스페이스 · 오른쪽 누르기) — 결산은 기록 판 '항해'에"));
            h.Blocks.Add(("결산 카드", r));
            cy = r.Bottom + 8;
        }
        if (f.Holding && f.Panel != PanelKind.Route)
        {
            var r = Center(720, 44);
            h.Areas["출항 띠"] = r;
            h.Hits.Add(new UiHit(new RectF(r.Right - 8 - 150, r.Y + 4, 150, 36), UiAct.Depart, 0, false, true, "이대로 출항 (스페이스)"));
            h.Hits.Add(new UiHit(new RectF(r.Right - 8 - 150 - 6 - 120, r.Y + 4, 120, 36), UiAct.RouteTab, 0, false, true, "항로 고르기 (T)"));
            h.Hits.Add(new UiHit(new RectF(r.Right - 8 - 150 - 6 - 120 - 6 - 136, r.Y + 4, 136, 36), UiAct.Recrew, 0, false, true, "새 번호로 다른 사람들을 태운다 (출항 전에만)"));
            h.Blocks.Add(("출항 띠", r));
            cy = r.Bottom + 8;
        }
        if (f.ResumeCard.Length > 0)
        {
            float wide = f.ResumeCard.Select((l, i) => Tw(l, i == 0 ? 18 : 16, i == 0)).DefaultIfEmpty(0).Max();
            var r = Center(Math.Max(520, wide + 48), 14 + 28 + (f.ResumeCard.Length - 1) * 24 + 12 + 40 + 12);
            h.Areas["지난번까지"] = r;
            h.Hits.Add(new UiHit(new RectF(r.Right - 16 - 140, r.Bottom - 12 - 40, 140, 40), UiAct.ResumeGo, 0, true, true, "계속 (스페이스)"));
            h.Blocks.Add(("지난번까지", r));
            cy = r.Bottom + 8;
        }
        if (f.ChapterTitle.Length > 0 && f.ChapterAlpha > 0.02f)
        {
            // 장 카드 (docs/OBSERVER.md §5.1 — 4초 · 한 주 6초): 제목 · 하루의 결 · 주인공 얼굴 · '누르면 기록 ›' — 카드 전체가 누를 자리
            bool faces = f.ChapterFaces.Length > 0;
            float wide = Math.Max(Tw(f.ChapterTitle, 18, true), Tw(f.ChapterLine, 15)) + 40;
            float hint = Tw(ChapterHint, 14) + 24 + (faces ? f.ChapterFaces.Length * (Painter.FaceW * 2 + 8) : 0);
            var r = Center(Math.Max(Math.Max(380, wide), hint + 24), 12 + 26 + 22 + (faces ? Painter.FaceH * 2 + 10 : 22) + 10);   // 얼굴은 두 배 (자세히 머리와 같은 정수 배율)
            h.Areas["장 카드"] = r;
            h.Hits.Add(new UiHit(r, UiAct.ChapterCard, 0, false, true, "기록 판에서 그 장 보기 (Y)"));
            h.Blocks.Add(("장 카드", r));
            cy = r.Bottom + 8;
        }
        if (f.Banner.Length > 0 && f.BannerAlpha > 0.05f && f.EndCard.Length == 0)
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

        // ── 두 곳 나눠 보기 (U9 · docs/OBSERVER.md §8-12): 가운데 칸 왼쪽 아래 (아래 묶음 위) — 자리가 모자라면 띄우지 않는다 ──
        float castL = colL;
        if (f.InsetTitle.Length > 0)
        {
            float iw = Math.Min(320, colR - colL), ih = iw * 0.6f + 26;
            var r = new RectF(colL, lowTop - ih, iw, ih);
            if (iw >= 200 && r.Y >= cy + 8)
            {
                h.Areas["구석 창"] = r; h.Blocks.Add(("구석 창", r));
                h.Hits.Add(new UiHit(r, UiAct.Inset, 0, false, true, "그리로 — 연출을 따라간다"));
                castL = r.Right + 8;   // 방송 자막은 그 오른쪽에
            }
        }

        // ── 방송 자막 (가운데 아래 · 아래 끝 = 사람 줄 위 − 8) ──
        if (f.Broadcast.Length > 0 && f.BroadcastAlpha > 0.05f)
        {
            float lowR = (f.Panel != PanelKind.None ? panelLeft : view.X) - Side;
            float bw = Math.Min(980, Math.Max(200, lowR - castL));
            float th = Th(f.Broadcast, 17, bw - 40, 3);
            float bx = Math.Clamp(W / 2 - bw / 2, castL, Math.Max(castL, lowR - bw));
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
            string[] items = { "배 바꾸기 ›", "새 항해 ›", "항해 끝내기", "소리", "소리 자막", "말풍선 (K)", "속마음 풍선 (I)", "도움말", "처음 화면으로" };
            UiAct[] acts = { UiAct.MenuShip, UiAct.MenuRestart, UiAct.MenuEnd, UiAct.Sound, UiAct.Captions, UiAct.Speech, UiAct.Thoughts, UiAct.Help, UiAct.MenuStart };
            var area = new RectF(W - Side - 240, 46, 240, items.Length * 38 + 12);
            h.Blocks.Insert(0, ("목록 메뉴", area)); h.Areas["목록 메뉴"] = area;
            for (int i = items.Length - 1; i >= 0; i--)
            {
                bool en = acts[i] != UiAct.MenuEnd || !f.Ended;   // 끝난 항해는 다시 끝내지 않는다
                h.Hits.Insert(0, new UiHit(new RectF(area.X + 6, area.Y + 6 + i * 38, 228, 36), acts[i], 100 + i, false, en, acts[i] == UiAct.MenuEnd ? "결산을 보이고 저장한다 — 배는 그대로 지켜볼 수 있다" : items[i], en ? "" : "이미 끝난 항해다 — 결산은 기록 판 '항해'에"));
            }
            if (s.Open == HudOpen.Ships && ships != null)
            {
                var sa = new RectF(area.X - 8 - 260, 46, 260, ships.Length * 38 + 12);
                h.Blocks.Insert(0, ("목록 배", sa)); h.Areas["목록 배"] = sa;
                for (int i = ships.Length - 1; i >= 0; i--) h.Hits.Insert(0, new UiHit(new RectF(sa.X + 6, sa.Y + 6 + i * 38, 248, 36), UiAct.ShipPick, i, false, true, ships[i]));
            }
        }
        if (s.Help)
        {
            float hw = Math.Min(860, W - 64), hh = Math.Min(H - 96, 760);   // 줄 열여덟 (U9 — 사고 · 살펴보기 · 자세히 · 구석 작은 창)
            var r = new RectF(W / 2 - hw / 2, H / 2 - hh / 2, hw, hh);
            h.Areas["창 도움말"] = r;
            h.Hits.Insert(0, new UiHit(new RectF(r.Right - 8 - 32, r.Y + 8, 32, 32), UiAct.Help, 1, false, true, "닫기 (H · Esc · 오른쪽 누르기)"));
            h.Hits.Insert(1, new UiHit(new RectF(0, 0, W, H), UiAct.Help, 2, false, true, ""));   // 아무 데나 누르면 닫힌다
            h.Modal = true;
        }
        if (s.Ask == HudAsk.NewVoyage) VoyageWindow(h, s, ships, (t, size, b) => Tw(t, size, b));
        else if (s.Ask != HudAsk.None)
        {
            var r = new RectF(W / 2 - 230, H / 2 - 85, 460, 170);
            h.Areas["창 확인"] = r;
            h.Hits.Clear();
            h.Hits.Add(new UiHit(new RectF(r.X + 70, r.Bottom - 56, 150, 40), UiAct.AskNo, 0, s.AskFocus == 0, true, "그만두기 (Esc)"));
            h.Hits.Add(new UiHit(new RectF(r.Right - 70 - 150, r.Bottom - 56, 150, 40), UiAct.AskYes, 0, s.AskFocus == 1, true, ""));
            h.Modal = true;
        }
    }

    /// <summary>번호 창 (docs/OBSERVER.md §3.4 — 게임 메뉴 [새 항해 ›] · 처음 화면 [새 항해 ›]가 같은 창): 배 ‹ › · 번호 · [새로 뽑기] · 숫자판 · [그만두기] [띄우기]. 다른 누를 자리는 지운다 (모달).</summary>
    public static void VoyageWindow(Hud h, HudState s, string[]? ships, Func<string, int, bool, float> tw)
    {
        float ww = 520, wh = 540;
        var r = new RectF(h.W / 2 - ww / 2, Math.Max(8, h.H / 2 - wh / 2), ww, wh);
        h.Areas["창 새 항해"] = r;
        h.Hits.Clear();
        int n = ships?.Length ?? 0;
        // 배 ‹ › (이름은 그 사이에)
        h.Hits.Add(new UiHit(new RectF(r.X + 24, r.Y + 60, 44, 44), UiAct.VoyageShip, -1, false, n > 1, "앞 배"));
        h.Hits.Add(new UiHit(new RectF(r.Right - 24 - 44, r.Y + 60, 44, 44), UiAct.VoyageShip, 1, false, n > 1, "다음 배"));
        h.Areas["새 항해 배"] = new RectF(r.X + 76, r.Y + 60, ww - 152, 44);
        // 번호 · [새로 뽑기]
        h.Areas["새 항해 번호"] = new RectF(r.X + 24, r.Y + 120, 220, 56);
        h.Hits.Add(new UiHit(new RectF(r.X + 24 + 220 + 12, r.Y + 120, ww - 48 - 232, 56), UiAct.VoyageKey, 11, false, true, "번호를 새로 뽑는다"));
        // 숫자판: 1 2 3 / 4 5 6 / 7 8 9 / 지움 0
        float kw = 96, kh = 52, gap = 8, kx = r.X + ww / 2 - (3 * kw + 2 * gap) / 2, ky = r.Y + 192;
        int[] grid = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0 };
        for (int i = 0; i < grid.Length; i++)
        {
            int row = i / 3, col = i % 3;
            h.Hits.Add(new UiHit(new RectF(kx + col * (kw + gap), ky + row * (kh + gap), kw, kh), UiAct.VoyageKey, grid[i], false, true, grid[i] == 10 ? "한 자리 지운다 (Backspace)" : ""));
        }
        h.Areas["새 항해 풀이"] = new RectF(r.X + 24, ky + 4 * (kh + gap) + 4, ww - 48, 44);
        bool ok = s.AskNumberOk;
        h.Hits.Add(new UiHit(new RectF(r.X + 70, r.Bottom - 56, 150, 40), UiAct.AskNo, 0, s.AskFocus == 0, true, "그만두기 (Esc)"));
        h.Hits.Add(new UiHit(new RectF(r.Right - 70 - 150, r.Bottom - 56, 150, 40), UiAct.AskYes, 0, s.AskFocus == 1 && ok, ok, "이 배 · 이 번호로 띄운다 (Enter)", ok ? "" : $"번호는 {VoyageNumber.Min}부터 {VoyageNumber.Max}까지 — 숫자를 넣거나 [새로 뽑기]"));
        h.Modal = true;
    }

    /// <summary>결산 카드 단추 글 (V22 전에는 '같은 배로 새 항해 — 새 사람들').</summary>
    public static string EndButton(bool fresh) => fresh ? "같은 배로 새 항해 — 새 사람들" : "그대로 지켜보기";
    /// <summary>장 카드 오른쪽 아래 풀이.</summary>
    public const string ChapterHint = "누르면 기록 ›";
    /// <summary>확인 창의 물음 (그리는 쪽이 쓴다 · 시험이 글자를 본다).</summary>
    public static string AskText(HudAsk a) => a switch
    {
        HudAsk.Restart => "같은 배로 새 항해를 띄울까요?\n지금 항해는 저장해 둔다 (처음 화면 이어 보기)",
        HudAsk.Start => "처음 화면으로 갈까요?\n지금 항해는 저장해 둔다 (처음 화면 이어 보기)",
        HudAsk.End => "항해를 끝낼까요?\n결산을 보이고 저장한다 — 배는 그대로 지켜볼 수 있다",
        _ => "",
    };

    /// <summary>숫자판 글: 0~9 · 10 지움 · 11 새로 뽑기.</summary>
    public static string VoyageKeyLabel(int k) => k switch { 10 => "지움", 11 => "새로 뽑기", _ => k.ToString() };

    /// <summary>번호 창 풀이 (가장 오래된 항해를 지울 때는 그 줄도).</summary>
    public static string VoyageNote(HudState s) => "같은 배 · 같은 번호 · 같은 항로 · 손대지 않으면 같은 항해" + (s.AskPrune.Length > 0 ? $"\n항해는 다섯까지 — 띄우면 가장 오래된 '{s.AskPrune}'를 지운다" : "");

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

    /// <summary>
    /// 지나온 날 띠 (docs/OBSERVER.md §5.3): 날마다 한 칸 — 폭 = clamp(띠 폭 ÷ 날 수, 4, 16) · 이레마다 틈 2 · 넘치면 이레 칸 (그 주의 가장 높은 분위기 · 잃은 이 · 오늘) ·
    /// 칸마다 누를 자리 (그 장 — 이레 칸은 그 주의 첫 날).
    /// </summary>
    private static void LayoutDays(LiveFrame f, Hud h, RectF strip)
    {
        h.Areas["날 띠"] = strip;
        int n = f.Days.Length;
        const float Gap = 2, MinW = 4, MaxW = 16;
        float cw = (strip.W - Gap * ((n - 1) / 7)) / n;
        bool weeks = cw < MinW;
        var cells = new List<(int from, int to)>();
        if (weeks) for (int i = 0; i < n; i += 7) cells.Add((i, Math.Min(n, i + 7)));
        else for (int i = 0; i < n; i++) cells.Add((i, i + 1));
        if (weeks) cw = (strip.W - (cells.Count - 1)) / cells.Count;
        cw = Math.Min(cw, MaxW);
        float x = strip.X;
        for (int k = 0; k < cells.Count; k++)
        {
            var (a, b) = cells[k];
            int mood = 0; bool lost = false, today = false;
            for (int i = a; i < b; i++) { mood = Math.Max(mood, f.Days[i].Mood); lost |= f.Days[i].Lost; today |= f.Days[i].Today; }
            var r = new RectF(x, strip.Y + 2, cw, 28);
            h.DayCells.Add((r, mood, lost, today));
            var d = f.Days[a];
            string tip = weeks ? $"{d.Day / 7 + 1}주째 ({d.Day + 1}~{f.Days[b - 1].Day + 1}일째)" : today ? $"{d.Day + 1}일째 — 오늘" : $"{d.Day + 1}일째";
            h.Hits.Add(new UiHit(r, UiAct.DayCell, d.Key, today, true, tip + " — 누르면 그날"));
            x += cw + (weeks ? 1 : 0);
            if (!weeks && (k + 1) % 7 == 0) x += Gap;
        }
    }

    /// <summary>판 줄 머리의 장면 표 (● ◐ ○ — 도형) 자리 폭.</summary>
    public const float MarkW = 22;

    /// <summary>사고 판의 크기 칩이 붙는 줄 (크기가 있는 갈래).</summary>
    private static bool HandChips(LiveFrame f, PanelRow r) => f.Panel == PanelKind.Hand && r.Key >> 40 == 5 && LiveShow.HandSized((HandKind)(r.Key - LiveShow.KeyHandKind(HandKind.None)));

    /// <summary>판: 머리 50 (제목 · [사슬 선] · ×) · 출항 준비 줄 · 보이는 줄 (넘기기 — PanelFirst부터 · 고른 줄이 바뀌면 보이게) · 넘김 막대 · 바닥 34.
    /// 자세한 판 (docs/UI2.md §5.2): 머리 56 (얼굴 · 이름 · 둘째 줄 · [따라가기][목록][×]) · 작은 탭 30 (+56) · 줄은 +92부터.</summary>
    private static void LayoutPanel(LiveFrame f, HudState s, Hud h, RectF box0, Func<string, int, bool, float> tw, Func<string, int, float, int, float> th)
    {
        int seen = (int)f.Panel | (int)f.Detail << 8 | f.DetailSub << 12 | (f.DetailId & 0xFFFF) << 16;   // 판 · 자세히 · 작은 탭이 바뀌면 첫 줄로
        if (s.PanelSeen != f.Panel || s.DetailSeen != seen) { s.PanelSeen = f.Panel; s.DetailSeen = seen; s.PanelFirst = 0; s.PickSeen = long.MinValue; s.WheelAcc = 0; }
        bool detail = f.Detail != DetailKind.None;
        float inner = box0.W - 36 - 18;
        const float BoardH = 132;   // 판 줄의 작은 그림 (상상 그림 — 제목 18 + 도면 114)
        float RowH(PanelRow r)
        {
            float face = r.Face >= 0 ? 56 : 0;
            float main = th((r.On ? "● " : "") + r.Main, 16, inner - face - (r.Mark != 0 ? MarkW : 0) - (r.Act.Length > 0 ? 104 : HandChips(f, r) ? 184 : 0), 2);
            float sub = r.Sub.Length > 0 ? th(r.Sub, 15, inner - face, 8) + 4 : 0;
            return Math.Max(Math.Max(28, r.Face >= 0 ? 54 : 0), 10 + main + (r.Bars is { Length: > 0 } ? 16 : 0) + (r.Boards is { Length: > 0 } ? BoardH + 6 : 0) + sub + 4);
        }
        bool aimRow = f.Panel == PanelKind.Hand && (f.Armed != HandKind.None || f.HandWaiting > 0);   // 겨눔 줄 (겨누는 중 · 기다리는 손)
        bool strip = f.Detail == DetailKind.Record && f.DetailSub == 0 && f.Days.Length > 0;   // 지나온 날 띠 (기록 '날마다' — docs/OBSERVER.md §5.3)
        float top = box0.Y + (detail ? 92 : 50) + (f.Holding && f.Panel == PanelKind.Route || aimRow ? 52 : 0) + (strip ? 40 : 0);
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
        if (detail)
        {
            h.Areas["자세히 머리"] = new RectF(box.X, box.Y, box.W, 56);
            if (f.Detail is DetailKind.Person or DetailKind.Robot)
            {
                var back = new RectF(box.Right - 40 - 6 - 72, box.Y + 10, 72, 28);
                h.Hits.Add(new UiHit(back, UiAct.DetailBack, 0, false, true, "목록으로 (오른쪽 누르기 · ←)"));
                h.Hits.Add(new UiHit(new RectF(back.X - 6 - 96, box.Y + 10, 96, 28), UiAct.DetailFollow, 0, f.DetailFollow, true, f.DetailFollow ? "따라가는 중" : "따라가기"));
            }
            float sx = box.X + 12;
            for (int i = 0; i < f.DetailTabs.Length; i++)
            {
                float w = Math.Max(64, tw(f.DetailTabs[i], 15, false) + 24);
                var r = new RectF(sx, box.Y + 56, w, 30);
                h.Hits.Add(new UiHit(r, UiAct.DetailTab, i, i == f.DetailSub, true, $"{f.DetailTabs[i]} (← →)"));
                sx += w + 6;
            }
            h.Areas["작은 탭"] = new RectF(box.X + 12, box.Y + 56, Math.Max(0, sx - 6 - box.X - 12), 30);
        }
        if (aimRow)
        {
            var aim = new RectF(box.X + 8, box.Y + 50, box.W - 16, 44);
            h.Areas["겨눔 줄"] = aim;
            if (f.Armed != HandKind.None)
            {
                var stop = new RectF(aim.Right - 4 - 120, aim.Y + 4, 120, 36);
                h.Hits.Add(new UiHit(stop, UiAct.HandCancel, 0, false, true, "그만두기 (오른쪽 누르기 · Esc)"));
                if (LiveShow.HandAimless(f.Armed)) h.Hits.Add(new UiHit(new RectF(stop.X - 6 - 120, aim.Y + 4, 120, 36), UiAct.HandNow, 0, false, f.CanHand && f.Aim.Ok, "누르면 곧 일어난다 — 되돌릴 수 없다 (다시보기로 볼 수는 있다)", f.CanHand ? f.Aim.Why : f.HandWhy));
            }
        }
        if (strip) LayoutDays(f, h, new RectF(box.X + 12, box.Y + 92, box.W - 24, 32));
        if (f.Holding && f.Panel == PanelKind.Route)
        {
            var hold = new RectF(box.X + 8, box.Y + 50, box.W - 16, 44);
            h.Areas["출항 줄"] = hold;
            h.Hits.Add(new UiHit(new RectF(hold.Right - 4 - 132, hold.Y + 4, 132, 36), UiAct.Depart, 0, false, true, "이대로 출항 (스페이스)"));
            h.Hits.Add(new UiHit(new RectF(hold.Right - 4 - 132 - 6 - 132, hold.Y + 4, 132, 36), UiAct.Recrew, 0, false, true, "새 번호로 다른 사람들을 태운다 (출항 전에만)"));
        }
        float y = top;
        for (int i = s.PanelFirst; i < last; i++)
        {
            var row = f.PanelRows[i];
            var r = new RectF(box.X + 6, y, box.W - 12 - (total > last - s.PanelFirst ? 18 : 0), heights[i - s.PanelFirst]);
            h.PanelLines.Add((i, r));
            if (row.Act.Length > 0) h.Hits.Add(new UiHit(new RectF(r.Right - 4 - 96, r.Y + 2, 96, 28), UiAct.PanelRowAct, row.Key, false, row.ActOn, row.ActOn ? "그 장면 다시보기 (Enter)" : row.Why, row.Why));
            if (HandChips(f, row))
            {
                // 크기 칩 셋 (작게 · 보통 · 크게 — 줄 오른쪽 위 · docs/UI2.md §5.3)
                var k = (HandKind)(row.Key - LiveShow.KeyHandKind(HandKind.None));
                for (int z = 0; z < 3; z++)
                {
                    bool on = (int)k < f.HandSizes.Length && f.HandSizes[(int)k] == z;
                    h.Hits.Add(new UiHit(new RectF(r.Right - 4 - 176 + z * 60, r.Y + 2, 56, 26), UiAct.HandSize, LiveShow.KeyHandSize(k, z), on && f.Armed == k, row.ActOn,
                        row.ActOn ? $"{LiveShow.HandName(k)} · {LiveShow.HandSizeNames[z]}로 겨눈다" : row.Why, row.Why));
                }
            }
            bool en = f.Panel == PanelKind.Route ? row.ActOn || row.Key < 1 : f.Panel != PanelKind.Hand || row.ActOn || row.Key < 1;
            if (row.Key != 0) h.Hits.Add(new UiHit(r, UiAct.PanelRow, row.Key, i == f.PanelPick, en, f.Panel == PanelKind.Hand && row.Key >> 40 == 5 ? (f.Armed == (HandKind)(row.Key - LiveShow.KeyHandKind(HandKind.None)) ? "그만두기" : "겨눈다 — 배 위를 누르면 일어난다") : "", row.Why));
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

/// <summary>처음 화면에 보일 것 (docs/OBSERVER.md §3.3): 이어 볼 항해 카드 (없으면 null) · 판이 달라 이어 볼 수 없는 까닭 · 다른 항해 목록 · 판 고르기.</summary>
public sealed class StartView
{
    public VoyageMeta? Latest;
    public string Why = "";
    public VoyageMeta[] Voyages = Array.Empty<VoyageMeta>();
    public bool ListOpen;
    public int Pick = 1;
}

/// <summary>처음 화면 자리표 (순수 C# — StartMenu가 그리고 실험실이 시험): 제목 · 이어 보기 카드 · 판 고르기 카드 둘 · 나가기 · 다른 항해 목록 · 번호 창.</summary>
public static class StartLayout
{
    public const float CardW = 560, CardH = 250, Gap = 36, ResumeH = 132;

    /// <summary>
    /// 자리를 셈한다 (논리 크기 — 창 높이 1080 기준): 제목 → 이어 보기 카드 (있으면 · 배 · 번호 · 날 · 어제 · 끈 때 + [다른 항해 ›] [새 항해 ›] [이어 보기] — 판이 다르면 [같은 번호로 새로] [지우기]) →
    /// 판 고르기 카드 둘 (카드가 없으면 새 엔진 판 카드 안에 [새 항해 ›]) → 풀이 → [나가기] · 다른 항해 목록 (펼침) · 번호 창 (모달).
    /// </summary>
    public static void Build(StartView v, HudState s, float pxW, float pxH, Hud.Measure m, Hud into, string[]? ships = null)
    {
        var h = into;
        h.Clear();
        h.U = Math.Clamp(pxH / 1080f, 0.85f, 2f);
        float U = h.U, W = pxW / U, H = pxH / U;
        h.W = W; h.H = H;
        float Tw(string t, int size, bool bold = false) => m(t, Math.Max(13, (int)Math.Round(size * U)), bold) / U * 1.03f;
        float cx = W / 2;
        h.Areas["제목"] = new RectF(cx - 300, H * 0.10f, 600, 90);
        float total = Math.Min(2 * CardW + Gap, W - 64), cw = (total - Gap) / 2;
        float top = H * 0.30f;
        if (v.Latest != null)
        {
            var card = new RectF(cx - total / 2, H * 0.21f, total, ResumeH);
            h.Areas["이어 보기 카드"] = card; h.Blocks.Add(("이어 보기 카드", card));
            float bx = card.Right - 16, by = card.Bottom - 12 - 40;
            RectF Btn(float w) { bx -= w; var r = new RectF(bx, by, w, 40); bx -= 8; return r; }
            if (v.Why.Length == 0)
            {
                bool ended = v.Latest.End.Length > 0;
                h.Hits.Add(new UiHit(Btn(Math.Max(150, Tw(ended ? "그대로 지켜보기" : "이어 보기", 16, true) + 28)), UiAct.StartResume, 0, true, true, ended ? "끝난 항해를 그대로 지켜본다" : "지난번 그 자리에서 이어 본다 (Enter)"));
            }
            else
            {
                h.Hits.Add(new UiHit(Btn(100), UiAct.StartDelete, 0, false, true, "이 항해의 저장을 지운다"));
                h.Hits.Add(new UiHit(Btn(Math.Max(170, Tw("같은 번호로 새로", 16) + 28)), UiAct.StartRenew, 0, true, true, "같은 배 · 같은 번호로 새로 띄운다"));
            }
            h.Hits.Add(new UiHit(Btn(Math.Max(130, Tw("새 항해 ›", 16) + 28)), UiAct.StartNew, 0, false, true, "번호를 넣거나 새로 뽑아 띄운다"));
            if (v.Voyages.Length > 1)
            {
                var other = Btn(Math.Max(140, Tw("다른 항해 ›", 16) + 28));
                h.Hits.Add(new UiHit(other, UiAct.StartOther, 0, v.ListOpen, true, "남은 항해 가운데 고른다"));
                h.Areas["다른 항해"] = other;
            }
            h.Areas["이어 보기 글"] = new RectF(card.X + 24, card.Y + 12, Math.Max(0, bx - card.X - 24 - 8), card.H - 24);
            top = card.Bottom + 28;
        }
        for (int i = 0; i < 2; i++)
        {
            var r = new RectF(cx - total / 2 + i * (cw + Gap), top, cw, CardH);
            h.Areas["판 " + i] = r; h.Blocks.Add(("판 " + i, r));
            if (i == 1 && v.Latest == null) h.Hits.Add(new UiHit(new RectF(r.Right - 16 - 130, r.Bottom - 16 - 40, 130, 40), UiAct.StartNew, 0, false, true, "번호를 넣거나 새로 뽑아 띄운다"));
        }
        for (int i = 0; i < 2; i++) h.Hits.Add(new UiHit(h.Areas["판 " + i], UiAct.StartChoice, i, v.Pick == i, true, i == 0 ? "기존 판 (1)" : "새 엔진 판 (2)"));
        h.Areas["풀이"] = new RectF(cx - total / 2, top + CardH + 24, total, 56);
        var quit = new RectF(cx - 70, top + CardH + 92, 140, 40);
        h.Areas["나가기"] = quit; h.Hits.Add(new UiHit(quit, UiAct.StartQuit, 0, false, true, "나가기 (Esc)"));
        if (v.ListOpen && v.Voyages.Length > 0 && h.Areas.TryGetValue("다른 항해", out var ob))
        {
            float lw = Math.Min(560, W - 32), rowH = 52;
            var area = new RectF(Math.Clamp(ob.Right - lw, 16, Math.Max(16, W - 16 - lw)), ob.Bottom + 6, lw, v.Voyages.Length * rowH + 12);
            h.Blocks.Insert(0, ("목록 항해", area)); h.Areas["목록 항해"] = area;
            for (int i = v.Voyages.Length - 1; i >= 0; i--) h.Hits.Insert(0, new UiHit(new RectF(area.X + 6, area.Y + 6 + i * rowH, lw - 12, rowH - 4), UiAct.StartPick, i, false, VoyageStore.Why(v.Voyages[i]).Length == 0, VoyageCard.Title(v.Voyages[i]), VoyageStore.Why(v.Voyages[i])));
        }
        if (s.Ask == HudAsk.NewVoyage) HudLayout.VoyageWindow(h, s, ships, (t, size, b) => Tw(t, size, b));
    }
}
