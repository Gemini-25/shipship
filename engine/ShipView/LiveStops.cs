using ShipEngine;

namespace ShipView;

/// <summary>
/// 놓치지 않게 (docs/OBSERVER.md §6.3 ~ 6.6 — V7 · 화면만 · 세계는 읽기만):
/// 큰 일 멈춤 (켠 갈래의 큰 일이 산 세계에 난 틱에서 멈추고 카드 하나 — '멈춤 — 김하늘이 쓰러졌다 (식당 · 07:04)' [보기] [계속] · 같은 뿌리는 10분에 한 번 · 다시보기 · 감기에서는 멈추지 않는다) ·
/// 창이 뒤로 가면 (그대로 · 느리게 · 멈춤 — 돌아오면 되돌린다) · '그사이' 카드 (손 없이 2분 + 게임 1시간이나 하이라이트 → 다음 손이 닿을 때) · 처음 한 줄 길잡이 (함장마다 한 번 — 설정에 남는다).
/// 사실은 산 세계의 것만 읽는다 (미리 보기 · 다시보기 세계는 보지 않는다 — 원칙 선).
/// </summary>
public sealed partial class LiveShow
{
    /// <summary>함장의 보기 설정 (profile.json — 게임이 항해마다 넘긴다 · 넘기지 않으면 사람 없는 띄우기: 큰 일에 멈추지 않는다).</summary>
    public ViewPrefs Prefs { get => _prefs; set { _prefs = value; _stopKinds = (bool[])value.Stops.Clone(); } }
    private ViewPrefs _prefs = ViewPrefs.Unattended();
    private bool[] _stopKinds = new bool[ViewPrefs.StopDefaults.Length];   // 그림에 넘기는 갈래 켬 (바뀔 때만 새 배열)

    public static readonly (string key, string text)[] HintTexts =
    {
        ("depart", "출항했다 — 일이 나면 연출이 늦추고 · 큰 일이 나면 그 자리에서 멈춘다 (메뉴 [알림 멈춤 ›])"),
        ("star", "★ 볼 만한 장면이 생겼다 — [장면] 탭 (L)이나 띠의 [다시보기]로 처음부터 다시 본다"),
        ("fire", "불이 났다 — 사건 띠 줄을 누르면 그 자리로 · 겹쳐 보기 [연기]로 번지는 길을 본다"),
        ("calm", "조용한 날이다 — [사고] 탭에서 직접 일으켜 볼 수 있다 (X)"),
        ("chapter", "하루가 지나면 장이 남는다 — [기록] 탭 (Y)에서 날마다 다시 본다"),
        ("decision", "주컴퓨터가 묻는다 — 고르지 않으면 주컴퓨터 안대로 간다"),
    };
    public const double HintSec = 5, AwayIdleSec = 120, AwaySnapSec = 1;
    /// <summary>같은 뿌리의 큰 일은 이만큼 안에 다시 멈추지 않는다 (불 하나에 쓰러짐 셋 — 첫 번만) · 그사이 카드를 띄울 게임 시간.</summary>
    public static readonly long StopAgain = Time.Minutes(10), AwayGame = Time.Hours(1);

    // ── 큰 일 멈춤 (§6.3) ──
    private long _stopFacts;
    private readonly Dictionary<int, long> _stopRoots = new();
    private string _stopLine = "";
    private int _stopAgent = -1, _stopCell = -1;
    /// <summary>멈춤 카드 글 (없으면 빈 글).</summary>
    public string StopLine => _stopLine;

    /// <summary>
    /// 산 세계가 한 틱 간 뒤 (<see cref="Advance"/> 틱 고리 — 다시보기 · 감기 · 감는 동안은 부르지 않는다): 켠 갈래의 큰 일이 났으면 그 틱에서 멈추고 카드 하나 (같은 틱의 다른 큰 일은 그 뿌리만 적어 둔다) ·
    /// 같은 뿌리는 10분에 한 번 · 첫 불 길잡이도 여기서 (사실 커서 하나로 함께 훑는다 — 새 사실이 없는 틱은 견줌 하나).
    /// </summary>
    private bool CheckStops(Sim w)
    {
        long total = w.Facts.Total;
        if (total == _stopFacts) return false;
        bool stop = false;
        Mind? mind = null;
        foreach (var f in w.Facts.Since(_stopFacts))
        {
            if (f.Kind == FactKind.Ignition) ShowHint("fire");
            var k = StopOf(w, ref mind, f, out int who);
            if (k == null) continue;
            int root = f.Cause != 0 ? w.Causes.Root(f.Cause) : 0;
            if (root != 0 && _stopRoots.TryGetValue(root, out long last) && f.Tick - last < StopAgain) continue;
            if (root != 0) _stopRoots[root] = f.Tick;
            if (stop) continue;
            stop = true;
            _stopLine = StopText(w, mind, f, k.Value, who); _stopAgent = who; _stopCell = f.Cell;
        }
        _stopFacts = total;
        if (stop) Paused = true;
        return stop;
    }

    /// <summary>사실 하나가 켠 갈래의 큰 일이면 그 갈래 · 그 사람 (마음 번호 — 없으면 −1): 사람의 숨짐 · 쓰러짐 · 외판이 뚫림 · 불이 붙음 · 별 단 이의 일 (V6 알림과 같은 갈래).</summary>
    private StopKind? StopOf(Sim w, ref Mind? mind, in Fact f, out int who)
    {
        who = -1;
        StopKind? k = null;
        switch (f.Kind)
        {
            case FactKind.Died or FactKind.Collapsed:
                mind ??= w.Find<Mind>();
                int a = f.Actor >= 0 && mind != null ? mind.AgentOfBody(f.Actor) : -1;
                if (a >= 0 && mind!.Agents[a].IsPerson) { who = a; k = f.Kind == FactKind.Died ? StopKind.Died : StopKind.Collapsed; }
                break;
            case FactKind.WallBreach when f.Cell >= 0 && f.Cell < w.Layout.N && w.Layout.Hull[f.Cell]: k = StopKind.Breach; break;
            case FactKind.Ignition: k = StopKind.Fire; break;
        }
        if (k != null && _prefs.Stop(k.Value)) return k;
        if (Stars.Count == 0 || !_prefs.Stop(StopKind.Star)) return null;
        mind ??= w.Find<Mind>();
        if (mind == null) return null;
        var (s, _) = StarLine(mind, f);
        if (s < 0) return null;
        who = s;
        return StopKind.Star;
    }

    /// <summary>카드 글: '멈춤 — 김하늘이 쓰러졌다 (식당 · 07:04)' · '멈춤 — 외판에 구멍이 났다 (침실 둘 · 03:12)' · '멈춤 — ☆ 이도윤이 다쳤다 (…)'.</summary>
    private string StopText(Sim w, Mind? mind, in Fact f, StopKind k, int who)
    {
        string name = who >= 0 && mind != null ? mind.NameOf(who) : "누군가";
        string what = k switch
        {
            StopKind.Died => $"{Ko.I(name)} 숨졌다",
            StopKind.Collapsed => $"{Ko.I(name)} 쓰러졌다",
            StopKind.Breach => "외판에 구멍이 났다",
            StopKind.Fire => "불이 붙었다",
            _ => "☆ " + (mind != null ? StarLine(mind, f).text : name),
        };
        int cell = f.Cell >= 0 ? f.Cell : who >= 0 && mind != null ? mind.Agents[who].C.Cell : -1;
        string room = PlaceOf(w.Layout, cell);
        return $"멈춤 — {what} ({(room.Length > 0 ? room + " · " : "")}{Hm(f.Tick)})";
    }

    /// <summary>칸이 든 방 이름 (벽 · 문 칸이면 곁의 방 — 외판이면 안쪽 방).</summary>
    private static string PlaceOf(Layout L, int cell)
    {
        if (cell < 0 || cell >= L.N) return "";
        if (L.Room[cell] >= 0) return L.Rooms[L.Room[cell]].Name;
        int x = L.X(cell), y = L.Y(cell);
        foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            if (L.In(x + dx, y + dy) && L.Room[L.Index(x + dx, y + dy)] is int r && r >= 0) return L.Rooms[r].Name;
        return "";
    }

    /// <summary>[보기]: 그 자리로 (사람이면 따라간다) · 다시 간다.</summary>
    public void StopSee()
    {
        int a = _stopAgent, c = _stopCell;
        StopGo();
        if (a >= 0) SelectAgent(a);
        else if (c >= 0) LookAt(c);
    }

    /// <summary>[계속] · 스페이스: 카드를 닫고 다시 간다.</summary>
    public void StopGo()
    {
        if (_stopLine.Length == 0) return;
        _stopLine = ""; _stopAgent = _stopCell = -1;
        Paused = false;
    }

    /// <summary>메뉴 [알림 멈춤 ›]: 갈래 하나를 켜고 끈다 (설정에 남는다).</summary>
    public void ToggleStop(StopKind k)
    {
        bool on = !_prefs.Stop(k);
        _prefs.SetStop(k, on);
        _stopKinds = (bool[])_prefs.Stops.Clone();
        Notify($"큰 일 멈춤 — {ViewPrefs.StopNames[(int)k]}: {(on ? "멈춘다" : "멈추지 않는다")}");
    }

    // ── 창이 뒤로 가면 (§6.4) ──
    /// <summary>창이 뒤로 갔다 (포커스를 잃었다).</summary>
    public bool Away { get; private set; }
    private bool _bgPaused, _bgSlow;
    private (double day, double over, int pick, int fixedPick) _bgSpeed;

    /// <summary>메뉴 [창이 뒤로 가면 ›]: 그대로 · 느리게 · 멈춤 (설정에 남는다).</summary>
    public void SetAwayMode(AwayMode m)
    {
        _prefs.SetAway(m);
        Notify($"창이 뒤로 가면 — {ViewPrefs.AwayNames[(int)m]}");
    }

    /// <summary>창이 뒤로 가거나 (away) 앞으로 왔다: 멈춤이면 멈추고 · 느리게면 하루 24분으로 — 앞으로 오면 되돌린다 (그사이 함장이 멈춰 둔 것 · 큰 일 멈춤은 그대로) · 돌아올 때도 '그사이' 카드.</summary>
    public void Background(bool away)
    {
        if (away == Away) return;
        Away = away;
        if (away)
        {
            var mode = _prefs.AwayIs;
            if (mode == AwayMode.Pause && !Paused) { Paused = true; _bgPaused = true; }
            else if (mode == AwayMode.Slow && !Paused)
            {
                _bgSpeed = (DayMinutes, SpeedOverride, SpeedPick, FixedPick); _bgSlow = true;
                DayMinutes = 24; SpeedOverride = 0;   // 하루 24분 (연출이 정한 빠르기 — 단추는 그대로 · 돌아오면 되돌린다)
            }
            return;
        }
        if (_bgPaused) { _bgPaused = false; if (Paused && _stopLine.Length == 0 && EndCard.Length == 0 && ResumeCard.Length == 0) Paused = false; }
        if (_bgSlow) { _bgSlow = false; (DayMinutes, SpeedOverride, SpeedPick, FixedPick) = _bgSpeed; }
        Touched();
    }

    // ── '그사이' 카드 (§6.5) ──
    /// <summary>손이 끊긴 때의 표 (틱 · 장 수 · 하이라이트 수 · 산 이 · 탄 이 · 식량 · 물 날 · 외판 구멍) — 화면만.</summary>
    private sealed record AwaySnap(long Tick, int Chapters, int Highlights, int[] Alive, double Food, double Water, int Holes);
    private AwaySnap? _snap;
    private double _touchAt;
    /// <summary>'그사이' 카드 줄 (닫을 때까지 — 빈 배열이면 없다).</summary>
    public string[] AwayCard = Array.Empty<string>();

    /// <summary>손이 닿았다 (마우스 · 키 — 게임은 0.5초에 한 번 보낸다): 실제 2분 넘게 손이 없었고 그사이 게임 1시간이 넘게 갔거나 하이라이트가 났으면 '그사이' 카드.</summary>
    public void Touched()
    {
        var s = _snap; _snap = null;
        double idle = _anim - _touchAt; _touchAt = _anim;
        if (s == null || idle < AwayIdleSec) return;
        if (Sim.Tick - s.Tick < AwayGame && Dir.Highlights.Count <= s.Highlights) return;
        AwayCard = AwayLines(s);
    }

    /// <summary>확인용 (화면 사진 --ui=away): 지금 손이 끊긴 것으로 적는다 — 실제 2분을 기다리지 않고 다음 <see cref="Touched"/>에 그사이 카드.</summary>
    public void MarkAway() { _snap = Snap(); _touchAt = _anim - AwayIdleSec; }

    /// <summary>나아갈 때마다 (<see cref="Advance"/>): 손이 끊긴 지 1초면 그사이 표를 한 번 찍는다.</summary>
    private void WatchTouch() { if (_snap == null && _anim - _touchAt >= AwaySnapSec) _snap = Snap(); }

    private AwaySnap Snap()
    {
        var mind = Sim.Find<Mind>(); var life = Sim.Find<Life>(); var f = Sim.Find<Field>(); var L = Sim.Layout;
        var alive = new List<int>();
        if (mind != null && life != null) foreach (var a in mind.Agents) if (a.IsPerson && a.C.Body >= 0 && life.Bodies[a.C.Body].Alive != 0) alive.Add(a.Id);
        int holes = 0;
        if (f != null) for (int c = 0; c < L.N; c++) if (L.Hull[c] && f.Hole[c] > 0) holes++;
        return new AwaySnap(Sim.Tick, Dir.Chapters.Count, Dir.Highlights.Count, alive.ToArray(), Sim.Book.DaysLeft((int)Res.Food, Sim.Tick), Sim.Book.DaysLeft((int)Res.Water, Sim.Tick), holes);
    }

    /// <summary>
    /// '그사이' 카드 줄: '그사이 — 9시간이 지났다 (3일째 22:10 → 4일째 07:30)' · '장 · 3일째 — 박서연을 잃은 날' · '장면 ▶ 07:04 침대 불이 번졌다 · …' ·
    /// '숨진 이 · 박서연 (07:04 · 연기가 찼다)' · '바뀐 것 · 식량 41 → 38일 · 사람 6 → 5 · 외판 구멍 0 → 1' (바뀐 것만).
    /// </summary>
    private string[] AwayLines(AwaySnap s)
    {
        var now = Snap();
        var l = new List<string> { $"그사이 — {Ko.I(Span(now.Tick - s.Tick))} 지났다 ({DayClock(s.Tick)} → {DayClock(now.Tick)})" };
        var chs = Dir.Chapters.Skip(s.Chapters).Where(c => !c.Week).Select(c => c.Title).ToList();
        if (chs.Count > 0) l.Add("장 · " + string.Join(" · ", chs.TakeLast(2)) + (chs.Count > 2 ? $" 외 {chs.Count - 2}" : ""));
        var hs = Dir.Highlights.Skip(s.Highlights).ToList();
        if (hs.Count > 0) l.Add("장면 " + string.Join(" · ", hs.TakeLast(3).Select(h => $"▶ {Hm(h.Tick)} {h.Title}")) + (hs.Count > 3 ? $" 외 {hs.Count - 3}" : ""));
        var lost = Lost().Where(x => Array.IndexOf(s.Alive, x.agent) >= 0).ToList();
        if (lost.Count > 0) l.Add("숨진 이 · " + string.Join(" · ", lost.Select(x => $"{AgentName(x.agent)} ({(x.at / Time.TicksPerDay == now.Tick / Time.TicksPerDay ? Hm(x.at) : DayClock(x.at))}{DeathWord(x.cause)})")));
        var ch = new List<string>();
        static string D(double d) => double.IsInfinity(d) || d > 999 ? "넉넉" : $"{d:0}";
        if (D(s.Food) != D(now.Food)) ch.Add($"식량 {D(s.Food)} → {D(now.Food)}{(D(now.Food) == "넉넉" ? "" : "일")}");
        if (D(s.Water) != D(now.Water)) ch.Add($"물 {D(s.Water)} → {D(now.Water)}{(D(now.Water) == "넉넉" ? "" : "일")}");
        if (now.Alive.Length != s.Alive.Length) ch.Add($"사람 {s.Alive.Length} → {now.Alive.Length}");
        if (now.Holes != s.Holes) ch.Add($"외판 구멍 {s.Holes} → {now.Holes}");
        if (ch.Count > 0) l.Add("바뀐 것 · " + string.Join(" · ", ch));
        return l.ToArray();
    }

    /// <summary>숨진 까닭 한 마디 (원인 사슬의 마지막 앞 마디 — '숨이 멎었다' 앞의 '연기가 찼다').</summary>
    private string DeathWord(int cause)
    {
        if (cause == 0) return "";
        var c = Sim.Causes.Chain(cause);
        if (c.Count == 0) return "";
        string t = Sim.Causes.Text(c[c.Count >= 2 ? ^2 : ^1]);
        return " · " + (t.Length > 18 ? t[..17] + "…" : t);
    }

    private static string Span(long ticks)
    {
        long min = (long)Math.Round(ticks * Time.SecondsPerTick / 60);
        if (min < 60) return $"{Math.Max(1, min)}분";
        long h = min / 60, m = min % 60;
        if (h < 24) return h < 3 && m >= 10 ? $"{h}시간 {m}분" : $"{h}시간";
        return h % 24 == 0 ? $"{h / 24}일" : $"{h / 24}일 {h % 24}시간";
    }

    /// <summary>[닫기] · 뒤로.</summary>
    public void AwayClose() => AwayCard = Array.Empty<string>();

    // ── 처음 한 줄 길잡이 (§6.6) ──
    private readonly Queue<string> _hints = new();
    private string _hint = "";
    private double _hintLeft;
    /// <summary>지금 길잡이 (없으면 빈 글).</summary>
    public string Hint => _hintLeft > 0 ? _hint : "";
    private float HintAlpha => _hintLeft > 0 ? (float)Math.Clamp(Math.Min(_hintLeft, HintSec - _hintLeft + 0.1) / 0.4, 0, 1) : 0;

    /// <summary>길잡이 하나를 (함장이 처음 보면) 5초 — 띄운 것이 있으면 그 뒤에.</summary>
    private void ShowHint(string key)
    {
        int i = Array.FindIndex(HintTexts, h => h.key == key);
        if (i < 0 || !_prefs.FirstHint(key)) return;
        if (_hintLeft > 0) _hints.Enqueue(HintTexts[i].text);
        else { _hint = HintTexts[i].text; _hintLeft = HintSec; }
    }

    private void TickHint(double realSec)
    {
        if (_hintLeft > 0) { _hintLeft = Math.Max(0, _hintLeft - realSec); return; }
        if (_hints.Count > 0) { _hint = _hints.Dequeue(); _hintLeft = HintSec; }
    }
}
