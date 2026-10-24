using System.Collections.Concurrent;
using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>관전 화면 한 장: 그림 + 그 안에서 화면에 보일 곳(픽셀 — 카메라가 칸 사이에 있어도 매끄럽게) + 글.</summary>
public sealed class LiveFrame
{
    public Img Img = null!;
    public float SrcX, SrcY, SrcW, SrcH;
    /// <summary>그림의 칸 하나 도트 (32 · 넓은 보기 16 · 8 — docs/GRAPHICS.md §3 세밀도 단계).</summary>
    public int Dot = Painter.T;
    public string Ship = "", Status = "", Story = "", Banner = "", Mood = "", Notice = "", Route = "";
    /// <summary>이 장에서 카메라가 끊어 넘겼다 (화면이 짧게 어두워졌다 밝아지게 할 수 있다).</summary>
    public bool Cut;
    public float BannerAlpha;
    public long Tick;
    public string Replay = "";            // 다시보기 중이면 그 하이라이트 제목
    public double CamLeft, CamTop, CamW, CamH;   // 화면이 보는 곳 (칸 · 소수) — 누른 자리를 칸으로 바꿀 때
    public string[] Card = Array.Empty<string>(); // 고른 사람 · 로봇 카드 (첫 줄: 이름)
    public double SelX = double.NaN, SelY;        // 고른 이가 그려진 자리 (칸 · 가운데)
    public string Broadcast = "";                 // 주컴퓨터 방송 자막 (아침 방송 · 항해 달력 · 몇 초)
    public float BroadcastAlpha;
    /// <summary>소리 자막 (소리 단계 S6 — 설정으로 켰을 때만): 무엇이 · 어느 쪽에서.</summary>
    public ShipView.Audio.SoundCaption[] Captions = Array.Empty<ShipView.Audio.SoundCaption>();
    /// <summary>열린 판 (하이라이트 목록 · 항로 · 일지) — 제목이 비면 닫혔다.</summary>
    public string PanelTitle = "", PanelFoot = "";
    public PanelRow[] PanelRows = Array.Empty<PanelRow>();
    public int PanelPick = -1;
    /// <summary>배 상태 띠 (UI U1): 공기 · 전력 · 선체 · 물자 · 사람 · 불.</summary>
    public Gauge[] Vitals = Array.Empty<Gauge>();
    /// <summary>사람 줄 (UI U2): 사람마다 얼굴 · 이름 · 하는 일 · 단계 — 얼굴은 Faces (칸 Painter.FaceW × FaceH · 한 줄에 FacesPerRow) · 바뀌면 FacesVersion이 오른다.</summary>
    public CrewFace[] Crew = Array.Empty<CrewFace>();
    public RobotChip[] Robots = Array.Empty<RobotChip>();
    public TalkLine[] Talk = Array.Empty<TalkLine>();
    public Img? Faces;
    public int FacesVersion, FacesPerRow = LiveShow.FacesPerRow;
    public int Selected = -1;
    /// <summary>사건 띠 (UI U4): 주컴퓨터가 아는 지금의 사건 · 막 끝난 사건 (누르면 그 자리로).</summary>
    public FeedItem[] Feed = Array.Empty<FeedItem>();
    /// <summary>멈춤 (빠르기 단추 — UI U5).</summary>
    public bool Paused;
    // ── 마우스 화면 (docs/UI.md 1.2): 손잡이를 켜진 모양으로 그리고 누를 자리를 셈하는 데 드는 화면 값 (세계 아님) ──
    public PanelKind Panel;
    public long PanelPickKey;
    public Overlay Show;
    public string Legend = "";
    public bool Follow, Overview, FollowSelected, Replaying;
    public double HandLeft;
    public int SpeedPick = 2, FixedPick = -1;
    public bool CaptionsOn;
    public bool CanReplay;
    public string ReplayWhy = "";
    public bool Holding;
    public double DepartIn = double.NaN;
    public int Highlights;
    public bool JournalNew;
    public PanelKind NoticeLink;
    public bool StoryIsChain;
    public AgentSpot[] Spots = Array.Empty<AgentSpot>();
    public int HoverId = -1;
    public string HoverName = "", HoverDoing = "";
    /// <summary>머리 위 풍선 (입말 · 속말 — docs/MIND.md 4단).</summary>
    public Bubble[] Bubbles = Array.Empty<Bubble>();
    public bool SpeechOn = true; public int ThoughtsOn;
}

/// <summary>사람 · 로봇이 그려진 자리 (칸 · 가운데) — 메인 스레드가 누른 · 올린 사람을 찾는다 (<see cref="LiveShow.Nearest"/>).</summary>
public readonly record struct AgentSpot(int Id, double X, double Y, bool Robot);

/// <summary>사건 띠의 한 줄: 때 · 글 · 단계 (0 알림 · 1 조심 · 2 위험) · 자리 칸 (-1 없음) · 아직인가.</summary>
public readonly record struct FeedItem(string Time, string Text, int Level, int Cell, bool Active);

/// <summary>배 상태 띠의 한 칸 (늘 보인다 — 세계의 값을 읽기만 한다): 그림 열쇠 · 이름 · 값 · 단계 (0 괜찮다 · 1 살펴볼 것 · 2 위험).</summary>
public readonly record struct Gauge(string Icon, string Name, string Value, int Level);

/// <summary>사람 줄의 한 사람 (UI U2): 얼굴은 Faces 그림의 Slot번째 칸 · 단계 (0 괜찮다 · 1 다침 · 지침 · 2 쓰러짐 · 위험 · 3 숨졌다).</summary>
public readonly record struct CrewFace(int Id, string Name, string State, int Level, int Slot);

/// <summary>판의 한 줄: 굵은 첫 줄 · 옅은 둘째 줄(여러 줄일 수 있다) · 켜짐(지금 고른 성격 · 다시보기 됨 등) — 사람 판은 얼굴(사람 줄 얼굴 그림의 칸)과 상태 막대도.</summary>
public readonly record struct PanelRow(string Main, string Sub, bool On, int Face = -1, PanelBar[]? Bars = null, long Key = 0, string Act = "", bool ActOn = true, string Why = "");
/// <summary>판 줄의 막대 하나 (UI U3): 이름 · 0~1 · 단계 (0 괜찮다 · 1 조심 · 2 위험).</summary>
public readonly record struct PanelBar(string Name, float Value, int Level);

/// <summary>관전 화면의 판.</summary>
public enum PanelKind { None, Highlights, Route, Journal, Chain, People }

/// <summary>
/// 관전 화면의 속 (docs/ENGINE.md §새 화면 2단계) — 세계를 돌리고, 연출이 정한 빠르기 · 볼 곳을 따르고, 그린다.
/// Godot 장면은 이것을 작업 스레드에서 부르고 나온 그림을 텍스처로 올리기만 한다 (메인 스레드는 세계를 만지지 않는다 — 60프레임).
/// 실험실(--live)도 같은 것을 돌려 PNG로 확인한다. 세계는 읽기만 한다 (연출 · 그리기 — 결정론 그대로).
/// </summary>
public sealed partial class LiveShow
{
    public readonly Sim Sim;              // 산 세계
    public readonly Director Dir;         // 산 세계의 연출 (다시보기 고리: 2분마다 찍기 1시간치)
    public readonly Painter Painter;
    public readonly string ShipName;
    /// <summary>소리 엔진의 귀 — 화면이 한 장 나아갈 때마다 들은 것을 섞개로 보낸다 (Audio/Acoustics.cs).</summary>
    public readonly ShipView.Audio.Acoustics Ears = new();

    // ── 다시보기: 하이라이트의 첫 조짐 직전 찍기에서 되살린 세계를 따로 돌려 보여 준다 (그동안 산 세계는 멈춘다) ──
    private Sim? _replay;
    private Director? _replayDir;
    private Highlight? _replayOf;
    private long _replayEnd;
    private List<Shot> _shots = new();      // ⑬ 이야기 편집: 다시보기는 연출의 컷 목록을 따른다 (조짐 → 순간 · 정점 → 사람마다의 시점 → 뒤 · 컷 사이는 건너뛴다)
    public IReadOnlyList<Shot> Shots => _shots;
    /// <summary>다시보기에서 지금 걸린 컷 (컷 사이 · 다시보기가 아니면 null).</summary>
    public Shot? ShotNow => _replay == null ? null : _shots.LastOrDefault(c => c.From <= _replay.Tick && _replay.Tick < c.To);
    public const double JumpSpeed = MaxTicksPerCall * 60;   // 컷 사이를 건너뛰는 빠르기 (한 장에 돌릴 수 있는 만큼)
    public bool Replaying => _replay != null;
    public Highlight? ReplayOf => _replayOf;
    /// <summary>지금 화면에 보이는 세계 · 연출 (다시보기 중이면 되살린 세계).</summary>
    public Sim World => _replay ?? Sim;
    public Director View => _replayDir ?? Dir;
    public const double ReplayAfterMin = 2;   // 하이라이트의 마지막 일 뒤 2분까지 보여 준다

    // ── 함장의 손 (화면 쪽에서 바꾼다 — Godot은 명령 줄로 작업 스레드에 넘긴다) ──
    public bool Paused;
    public double SpeedOverride;          // 0: 연출이 정한 빠르기 · 그 밖: 브레이크 없는 고정 빠르기 (화면 1초에 틱 — Shift+숫자)
    /// <summary>하루 길이 목표 (화면 분 · 0이면 연출의 예전 고정 빠르기) — 단추는 이것을 고른다: 조용할 때의 상한일 뿐 · 조짐 · 위험 · 결정적 순간엔 연출이 늘 늦춘다.</summary>
    public double DayMinutes { get => Dir.DayMinutes; set => Dir.DayMinutes = value; }
    public const double DefaultDayMinutes = 3;   // 함장 결정 (docs/REVIEW.md ① 질문 2 — 기본 하루 3분 · 게임과 --live가 띄울 때 고른다 · 0이면 예전 고정 빠르기)
    /// <summary>따라 보기 (docs/MIND.md 4단 · 함장 결정 '추천대로'): 사람을 골라 따라가면 조용할 때의 빠르기 상한을 '하루 이만큼 분'으로 낮춘다 (24분 = 화면 1초에 게임 1분 — 세세한 동작이 보인다) · 놓으면 고른 하루 목표로 · 0이면 끔. 화면만 — 세계는 그대로.</summary>
    public double FollowDayMinutes = 24;
    public bool FollowSlow => SpeedOverride <= 0 && _replay == null && Selected >= 0 && FollowSelected && FollowDayMinutes > 0;
    /// <summary>한 장에 세계를 돌리는 시간 (ms) — 넘으면 남은 틱은 버린다 (그림이 끊기지 않게 · 실제 빠르기는 위 줄에 정직하게).</summary>
    public const double FrameBudgetMs = 10;
    /// <summary>이 화면의 한 장 예산 (ms — 기본 FrameBudgetMs · 시험이 빠르기만 잴 때 넉넉히 둔다: 벽시계에 기대면 바쁜 기계에서 틱 수가 흔들린다).</summary>
    public double FrameBudget = FrameBudgetMs;
    /// <summary>실제로 나아간 빠르기 (화면 1초에 틱 · 0.5초마다 고침).</summary>
    public double Rate { get; private set; }
    private double _rateTicks, _rateSec, _tickMs = 1;
    /// <summary>손으로 카메라를 옮긴 뒤 이만큼 손이 쉬면 연출의 카메라로 돌아간다 (함장 결정 — '손대면 잠깐 양보하고 다시 맡는다').</summary>
    public const double HandHoldSec = 8;
    private double _handAt;
    public bool HandCam => !Follow && !Overview && !FollowSelected;
    public double HandLeft => HandCam ? Math.Max(0, HandHoldSec - (_anim - _handAt)) : 0;
    public bool Follow = true;            // 연출의 카메라를 따라간다 (끄면 손으로)
    public bool Overview;                 // 배 전체
    public double CamX, CamY;             // 화면 가운데 (칸 · 소수)
    public double ViewW = 32;             // 화면 가로 칸 수 (부드럽게 바뀐다)
    public double Aspect = 9.0 / 16.0;    // 화면 세로/가로
    public const int MaxTicksPerCall = 50; // 한 번에 이보다 많이 돌리지 않는다 (느린 기계에서 밀린 것은 버린다 — 화면이 끊기지 않게)
    public const double MinView = 12, MaxView = 96, FollowMin = 20, FollowMax = 48;
    /// <summary>결정적 순간의 가까이 보기 (칸) · 이만큼(화면 너비의 몫)보다 먼 곳으로 옮길 땐 컷 · 이번 장이 컷인가 · 컷 수.</summary>
    public const double CloseUp = 18, CutAt = 0.75;
    public bool Cut; public long Cuts;
    public const double AnimFps = 8;      // 걷기 · 불꽃 그림이 바뀌는 빠르기 (도트 — 화면 프레임과 따로)

    private double _carry, _anim;
    private readonly (int x0, int y0, int x1, int y1) _bbox;
    private int _lastHighlights;
    private string _lastTitle = "";
    public string Banner = "";            // 새 하이라이트가 생기면 몇 초 띄운다
    public double BannerLeft;
    public const double BannerSec = 6;
    private readonly ConcurrentBag<Img> _spare = new();
    // 걸음 사이 메우기: 마지막 틱에 사람 · 로봇이 지나간 칸들 (한 틱 2.4초에 두세 칸을 걷는다)
    public const int TrailMax = 10;
    private int[][] _trail = Array.Empty<int[]>();
    private int[] _trailLen = Array.Empty<int>();

    public LiveShow(Sim sim, string shipName)
    {
        Sim = sim; ShipName = shipName;
        if (sim.Find<Mind>() is { } jm) jm.Journal = true;   // 판단 일지 · 주컴퓨터 일지를 켠다 (쓰기만 — 법칙 '판단 일지는 세계를 바꾸지 않는다') — 로봇 카드 · 말 창이 읽는다
        if (sim.Find<ShipMind>() is { } jc) jc.NoteOn = true;
        Dir = new Director(sim.Layout) { AheadInBackground = true };   // 미리 보기의 앞선 세계는 다른 코어에서 (결과는 같다) · 하루 목표는 띄우는 쪽이 고른다 (게임 · --live는 3분)
        Painter = new Painter(sim.Layout) { Nudge = Nudge };
        var L = sim.Layout;
        int x0 = L.W, y0 = L.H, x1 = 0, y1 = 0;
        for (int i = 0; i < L.N; i++)
            if (L.Kind[i] != CellKind.Outside) { x0 = Math.Min(x0, L.X(i)); y0 = Math.Min(y0, L.Y(i)); x1 = Math.Max(x1, L.X(i)); y1 = Math.Max(y1, L.Y(i)); }
        _bbox = (x0 - 2, y0 - 2, x1 + 2, y1 + 2);
        CamX = (_bbox.x0 + _bbox.x1 + 1) / 2.0; CamY = (_bbox.y0 + _bbox.y1 + 1) / 2.0;
    }

    /// <summary>배 하나를 사람 · 로봇과 함께 띄운다 (침대마다 한 사람 · 충전독마다 로봇) · 항로 route(1~8 — ⑫ 위험도는 항로만)로 항해에 나선다.</summary>
    public static LiveShow Crewed(string ship, ulong seed, int route = ShipWorld.DefaultRoute)
    {
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        ShipWorld.Embark(s, route);   // 항해에 나선다 — 20분 뒤 출발 점화 (그 전엔 항로를 바꿀 수 있다) · 다른 배가 지나간다
        return new LiveShow(s, lay.Name);
    }

    /// <summary>화면 1초에 틱: 함장이 고른 빠르기 · 아니면 연출의 빠르기 (다시보기에선 컷의 빠르기 · 컷 사이는 건너뛴다 — 이미 본 장면이다).</summary>
    public double Speed
    {
        get
        {
            if (SpeedOverride > 0) return SpeedOverride;
            if (_replay != null && _shots.Count > 0) return ShotNow?.Speed ?? JumpSpeed;
            return FollowSlow ? Math.Min(View.Speed, Time.TicksPerDay / (FollowDayMinutes * 60)) : View.Speed;   // 따라 보기: 조짐 · 위험은 연출이 더 늦춘다 (작은 쪽)
        }
    }
    public (int x0, int y0, int x1, int y1) Bounds => _bbox;

    /// <summary>화면 없이 앞으로 감기 (연출은 함께 본다 — 하이라이트 · 다시보기 고리가 쌓인다). 확인 · 화면 사진용.</summary>
    public void FastForward(long ticks)
    {
        for (long i = 0; i < ticks; i++) { Sim.Step(); Dir.Observe(Sim); }
        WatchComputer(0);
        _lastHighlights = Dir.Highlights.Count; _lastTitle = _lastHighlights > 0 ? Dir.Highlights[^1].Title : "";   // 감은 동안의 하이라이트를 첫 장에 띄우지 않는다
    }

    /// <summary>화면 시간 realSec만큼 나아간다 — 돌린 틱 수.</summary>
    /// <summary>나아가기 단계마다 새로 잡은 바이트 (재기용 — null이면 재지 않는다).</summary>
    public static Dictionary<string, long>? AdvAlloc;
    private long _al;
    private void AL(string k) { if (AdvAlloc == null) return; long b = GC.GetAllocatedBytesForCurrentThread(); AdvAlloc[k] = AdvAlloc.GetValueOrDefault(k) + b - _al; _al = b; }

    public int Advance(double realSec)
    {
        int n = 0;
        if (AdvAlloc != null) _al = GC.GetAllocatedBytesForCurrentThread();
        if (!Paused)
        {
            _carry += Speed * realSec;
            n = (int)Math.Min(_carry, MaxTicksPerCall);
            _carry -= n;
            // 다시보기에서 컷 사이를 건너뛸 때는 다음 컷의 첫 틱에서 멈춘다 (건너뛰다 정점을 지나치지 않게)
            if (_replay != null && ShotNow == null && _shots.FirstOrDefault(c => c.From > _replay.Tick) is { } next)
                n = (int)Math.Min(n, next.From - _replay.Tick);
            if (_carry >= 1) _carry -= Math.Floor(_carry);   // 밀린 것은 버린다 (다음 장에 몰아 돌리지 않는다)
            var w = World; var d = View;
            d.Foresee = SpeedOverride <= 0 || SpeedOverride <= Director.CalmSpeed;   // 고정 빠르기로 빨리 감는 동안은 미리 보지 않는다 (복제가 1초에 수십 번 — 볼 틈도 없다)
            if (_replay == null) n = Math.Min(n, Math.Max(1, (int)(FrameBudget / Math.Max(0.01, _tickMs))));   // 이 장의 시간 예산 안에서만 (지난 틱들의 걸린 시간으로)
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            double v0 = Speed;
            int ran = 0;
            var thinker = w.Find<ShipMind>();
            for (int i = 0; i < n; i++)
            {
                if (thinker != null && thinker.Waiting(w.Tick)) break;   // 6단 계산 일꾼이 늦었다: 세계만 멈추고 그림은 계속 그린다 (받을 틱 · 결과는 그대로 — docs/ENGINE.md §3-10)
                if (i == n - 1) Remember();
                AL("자세 · 걸음");
                w.Step(); AL("세계");
                d.Observe(w); AL("연출");
                ran++;
                if (_replay != null && w.Tick >= _replayEnd) break;
                if (SpeedOverride <= 0 && _replay == null && d.Speed < v0 * 0.5) break;   // 묶음 가운데 분위기가 무거워졌다 — 그 틱에서 멈춘다 (빨리 지나치지 않게)
            }
            if (ran > 0) _tickMs = 0.8 * _tickMs + 0.2 * (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency / ran;
            if (ran < n) _carry = 0;   // 남은 틱은 버린다 (다음 장에 몰지 않는다)
            n = ran;
            if (n > 0) Trace();
            AL("자세 · 걸음");
            if (_replay != null && _replay.Tick >= _replayEnd) StopReplay();
        }
        _anim += realSec;
        if (!Paused) { _rateTicks += n; _rateSec += realSec; if (_rateSec >= 0.5) { Rate = _rateTicks / _rateSec; _rateTicks = 0; _rateSec = 0; } }
        if (HandCam && _anim - _handAt > HandHoldSec) Follow = true;   // 손이 쉬었다 — 연출이 다시 맡는다
        // 새 하이라이트 · 같은 사슬에서 커진 하이라이트(죽음이 더해져 제목이 바뀌었다)도 띠를 띄운다
        if (Dir.Highlights.Count > _lastHighlights || Dir.Highlights.Count > 0 && Dir.Highlights[^1].Title != _lastTitle)
        {
            _lastHighlights = Dir.Highlights.Count; _lastTitle = Dir.Highlights[^1].Title;
            Banner = _lastTitle; BannerLeft = BannerSec;
        }
        BannerLeft = Math.Max(0, BannerLeft - realSec);
        TickNotice(realSec);
        WatchComputer(realSec);
        WatchSpeech(realSec);
        WatchTalk(realSec);
        AL("알림 · 주컴퓨터 지켜보기");
        // 카메라: 연출이 고른 곳으로 부드럽게 (전체 보기면 배 가운데)
        var L = Sim.Layout;
        var vd = View;
        double tx = CamX, ty = CamY, tw = ViewW;
        if (Overview) { var rb = Reach(); tx = (rb.x0 + rb.x1 + 1) / 2.0; ty = (rb.y0 + rb.y1 + 1) / 2.0; tw = Math.Max(rb.x1 - rb.x0 + 1, (rb.y1 - rb.y0 + 1) / Aspect); }
        else if (Selected >= 0 && FollowSelected) { var (sx, sy) = DrawnAt(Selected); if (!double.IsNaN(sx)) { tx = sx; ty = sy; tw = Math.Clamp(ViewW, FollowMin, FollowMax); } }
        else if (Follow && ShotNow is { } shot)
        {
            // 다시보기의 컷: 시점 · 순간의 사람을 따라 · 아니면 그 컷의 자리
            var (ax, ay) = shot.Actor >= 0 && World.Find<Mind>() is { } wm && shot.Actor < wm.Agents.Count ? DrawnAt(shot.Actor) : (double.NaN, 0.0);
            (tx, ty) = !double.IsNaN(ax) ? (ax, ay) : shot.Cell >= 0 ? CellDrawn(shot.Cell) : (tx, ty);
            tw = Math.Clamp(vd.Zoom, FollowMin, FollowMax);
        }
        else if (Follow && vd.FocusCell >= 0) { (tx, ty) = CellDrawn(vd.FocusCell); tw = Math.Clamp(vd.Zoom, FollowMin, FollowMax); }
        // 카메라 연출 (그래픽 결정 13 — 위아래 검은 띠는 없다): 결정적 순간엔 가까이 · 먼 곳으로 옮길 땐 미끄러지지 않고 끊어 넘긴다 (컷)
        bool auto = Follow && !Overview && !(Selected >= 0 && FollowSelected);
        if (auto && (vd.Mood == "결정적 순간" || ShotNow is { Kind: "정점" })) tw = Math.Min(tw, CloseUp);
        Cut = false;
        if (auto && Math.Sqrt((tx - CamX) * (tx - CamX) + (ty - CamY) * (ty - CamY)) > ViewW * CutAt)
        {
            CamX = tx; CamY = ty; ViewW = tw; Cut = true; Cuts++;
        }
        double k = 1 - Math.Exp(-realSec * 2.5);
        CamX += (tx - CamX) * k; CamY += (ty - CamY) * k; ViewW += (tw - ViewW) * k;
        AL("카메라");
        Ears.Update(this, realSec);
        AL("소리 듣기");
        return n;
    }

    /// <summary>마지막 틱 앞: 지금 칸과 앞으로 걸을 길(몇 칸)을 적어 둔다.</summary>
    // ④ 칸 강체: 지난 틱 끝의 몸마다 배 틀 무게중심 자리 · 자세 (틱 사이를 메운다 — 걸음처럼 한 틱 늦게 · 각은 감지 않은 값: 한 틱에 반 바퀴 넘게 도는 몸도 도는 쪽으로) · 배의 자세
    private (double qx, double qy, double th)[] _prevPose = Array.Empty<(double, double, double)>();
    // 지난 틱 끝 배(몸 0)의 세상 자세 · 제 틀 무게중심 (몸이 아직 없었으면 0 — 처음 서는 몸 0은 세상 = 도면 틀에서 선다)
    private (double x, double y, double th, double lx, double ly) _prevShip;
    private long _prevTick = long.MinValue;

    private void Remember()
    {
        var st = World.Find<Structure>();
        if (st != null)
        {
            _prevPose = new (double, double, double)[st.Bodies.Count];
            for (int b = 0; b < _prevPose.Length; b++) _prevPose[b] = PoseNow(st, b);
            var b0 = st.Bodies.Count > 0 ? st.Bodies[0] : default;
            _prevShip = (b0.X, b0.Y, b0.Th, b0.Lx, b0.Ly);
            _prevTick = World.Tick;
        }
        var mind = World.Find<Mind>();
        if (mind == null) return;
        if (_trail.Length < mind.Agents.Count)
        {
            int old = _trail.Length;
            Array.Resize(ref _trail, mind.Agents.Count); Array.Resize(ref _trailLen, mind.Agents.Count);
            for (int i = old; i < _trail.Length; i++) _trail[i] = new int[TrailMax];
        }
        foreach (var a in mind.Agents)
        {
            var t = _trail[a.Id];
            t[0] = a.C.Cell;
            int k = 1;
            for (int i = a.PathIdx; i < a.Path.Count && k < TrailMax; i++) t[k++] = a.Path[i];
            _trailLen[a.Id] = -k;   // 음수: 아직 걸을 길 (틱 뒤에 지나간 만큼으로 줄인다)
        }
    }

    /// <summary>마지막 틱 뒤: 적어 둔 길에서 지금 칸까지를 지나간 길로 남긴다 (길 밖으로 옮겨졌으면 옆 칸만 잇고 · 멀면 그대로).</summary>
    private void Trace()
    {
        var mind = World.Find<Mind>();
        if (mind == null) return;
        var L = Sim.Layout;
        foreach (var a in mind.Agents)
        {
            if ((uint)a.Id >= (uint)_trail.Length) continue;
            var t = _trail[a.Id];
            int planned = -_trailLen[a.Id], cell = a.C.Cell, len = 1;
            if (planned <= 0) { _trailLen[a.Id] = 0; continue; }
            if (cell != t[0])
            {
                len = 0;
                for (int i = 1; i < planned; i++) if (t[i] == cell) { len = i + 1; break; }
                if (len == 0)
                {
                    bool near = Math.Abs(L.X(cell) - L.X(t[0])) <= 1 && Math.Abs(L.Y(cell) - L.Y(t[0])) <= 1;
                    if (near) { t[1] = cell; len = 2; } else len = 1;
                }
            }
            _trailLen[a.Id] = len;
        }
        // 업힌 사람은 업은 이의 길을 따른다
        foreach (var a in mind.Agents)
            if (a.C.CarriedBy >= 0 && a.C.CarriedBy < _trail.Length && a.Id < _trail.Length)
            {
                int b = a.C.CarriedBy;
                Array.Copy(_trail[b], _trail[a.Id], TrailMax); _trailLen[a.Id] = _trailLen[b];
            }
    }

    /// <summary>걸음 사이 메우기: 마지막 틱에 지나간 길을 다음 틱까지 남은 시간에 맞춰 따라가며 그린다 (틱이 드문 순간에도 미끄러지듯 걷는다 — 한 틱 늦게 보인다).</summary>
    public (int dx, int dy) Nudge(Agent a)
    {
        if ((uint)a.Id >= (uint)_trailLen.Length) return (0, 0);
        int len = _trailLen[a.Id];
        if (len <= 1) return (0, 0);
        var t = _trail[a.Id];
        int cell = a.C.Cell;
        if (t[len - 1] != cell) return (0, 0);                       // 그 뒤에 또 옮겨졌다
        var L = Sim.Layout;
        double s = Math.Clamp(_carry, 0, 1) * (len - 1);
        int i = Math.Min((int)s, len - 2);
        double f = s - i;
        double px = L.X(t[i]) + (L.X(t[i + 1]) - L.X(t[i])) * f, py = L.Y(t[i]) + (L.Y(t[i + 1]) - L.Y(t[i])) * f;
        return ((int)Math.Round((px - L.X(cell)) * Painter.T), (int)Math.Round((py - L.Y(cell)) * Painter.T));
    }

    // ───────────────────────── 사람 눌러 보기 ─────────────────────────

    /// <summary>고른 사람 · 로봇 (-1: 없음). 고르면 카메라가 그를 따라간다 (손으로 옮기거나 F를 누르면 놓는다).</summary>
    public int Selected = -1;
    public bool FollowSelected;

    /// <summary>그 사람 · 로봇이 그려진 자리 (칸 · 가운데 — 걸음 사이 메우기 포함 · 떨어져 나간 조각 안이면 그 조각의 그려진 자세로).</summary>
    public (double x, double y) DrawnAt(int agent)
    {
        var mind = World.Find<Mind>();
        if (mind == null || (uint)agent >= (uint)mind.Agents.Count) return (double.NaN, 0);
        var a = mind.Agents[agent];
        var L = Sim.Layout;
        var (nx, ny) = Nudge(a);
        double px = L.X(a.C.Cell) + 0.5 + nx / (double)Painter.T, py = L.Y(a.C.Cell) + 0.5 + ny / (double)Painter.T;
        var st = World.Find<Structure>();
        int b = st != null && st.Bodies.Count > 1 ? st.BodyOf(a.C.Cell) : 0;
        if (b <= 0) return (px, py);
        var (cs, sn, tx, ty) = PoseDrawn(st!, b);
        return (cs * px - sn * py + tx, sn * px + cs * py + ty);
    }

    /// <summary>칸 가운데가 그려진 자리 (떨어져 나간 조각의 칸이면 그 조각의 그려진 자세로 — 연출이 고른 곳이 조각 안이어도 카메라가 그리로 간다).</summary>
    public (double x, double y) CellDrawn(int cell)
    {
        var L = Sim.Layout;
        double px = L.X(cell) + 0.5, py = L.Y(cell) + 0.5;
        var st = World.Find<Structure>();
        int b = st != null && st.Bodies.Count > 1 ? st.BodyOf(cell) : 0;
        if (b <= 0) return (px, py);
        var (cs, sn, tx, ty) = PoseDrawn(st!, b);
        return (cs * px - sn * py + tx, sn * px + cs * py + ty);
    }

    /// <summary>몸 b의 무게중심이 그려진 자리 (배 틀 칸 · 틱 사이 메움 포함).</summary>
    public (double x, double y) DrawnBody(int b)
    {
        var st = World.Find<Structure>();
        if (st == null || b <= 0 || b >= st.Bodies.Count) return (double.NaN, 0);
        var o = st.Bodies[b];
        var (cs, sn, tx, ty) = PoseDrawn(st, b);
        return (cs * o.Lx - sn * o.Ly + tx, sn * o.Lx + cs * o.Ly + ty);
    }

    /// <summary>몸 b의 지금 배 틀 무게중심 자리 · 배에 대한 자세 (몸 각 − 배 각 그대로 — 감지 않는다).</summary>
    private static (double qx, double qy, double th) PoseNow(Structure st, int b)
    {
        if (b <= 0) return (0, 0, 0);
        var o = st.Bodies[b];
        var (cs, sn, tx, ty) = st.ToShipFrame(b);
        return (cs * o.Lx - sn * o.Ly + tx, sn * o.Lx + cs * o.Ly + ty, o.Th - st.Bodies[0].Th);
    }

    /// <summary>몸 b를 그릴 배 틀 자세: 지난 틱 끝 → 지금을 틱 사이 비율만큼 (무게중심 자리 · 자세를 따로 — 도는 조각이 흔들리지 않게). 이번 틱에 떨어져 나간 조각은 붙어 있던 자리에서.</summary>
    private (double cs, double sn, double tx, double ty) PoseDrawn(Structure st, int b)
    {
        if (b <= 0 || b >= st.Bodies.Count) return (1, 0, 0, 0);
        var o = st.Bodies[b];
        var (qx, qy, th) = PoseAt(st, b, Math.Clamp(_carry, 0, 1));
        double cs = Math.Cos(th), sn = Math.Sin(th);
        return (cs, sn, qx - (cs * o.Lx - sn * o.Ly), qy - (sn * o.Lx + cs * o.Ly));
    }

    /// <summary>시험용: 몸 b를 틱 사이 비율 f (0 지난 틱 끝 ~ 1 지금)에 그리는 배 틀 각 (rad).</summary>
    public double DrawnTh(int b, double f)
    {
        var st = World.Find<Structure>();
        return st == null || b <= 0 || b >= st.Bodies.Count ? 0 : PoseAt(st, b, f).th;
    }

    /// <summary>몸 b의 틱 사이 f의 배 틀 무게중심 자리 · 자세 — 각은 지난 틱 끝 각에서 지금 각까지 곧게 (감지 않는다: 한 틱에 4.8rad 도는 몸을 −1.48rad로 거꾸로 돌리지 않는다).</summary>
    private (double qx, double qy, double th) PoseAt(Structure st, int b, double f)
    {
        var o = st.Bodies[b];
        var now = PoseNow(st, b);
        bool knew = World.Tick == _prevTick + 1 && _prevPose.Length > 0;   // 바로 지난 틱을 안다 (앞으로 감기 · 다시보기로 건너뛰었으면 지금 그대로)
        (double qx, double qy, double th) was = !knew ? now : b < _prevPose.Length ? _prevPose[b]
            : (o.Lx, o.Ly, now.th - Math.IEEERemainder(now.th, 2 * Math.PI));   // 이번 틱에 났다: 붙어 있던 자리 · 지금 각에 가장 가까운 온 바퀴에서 (돌던 조각에서 또 갈라진 몸이 한 장에 몇 바퀴를 돌지 않게)
        return (was.qx + (now.qx - was.qx) * f, was.qy + (now.qy - was.qy) * f, was.th + (now.th - was.th) * f);
    }

    /// <summary>카메라가 갈 수 있는 곳: 배 둘레 + 떨어져 나간 조각들의 그려진 자리 (200칸 안 — 멀리 간 것은 따라가지 않는다).</summary>
    private (int x0, int y0, int x1, int y1) Reach()
    {
        var st = World.Find<Structure>();
        if (st == null || st.Bodies.Count < 2) return _bbox;
        var (x0, y0, x1, y1) = _bbox;
        for (int b = 1; b < st.Bodies.Count; b++)
        {
            var o = st.Bodies[b];
            if (o.Mass <= 0) continue;
            var (cs, sn, tx, ty) = PoseDrawn(st, b);
            foreach (var (px, py) in new[] { (o.MinX, o.MinY), (o.MaxX + 1, o.MinY), (o.MinX, o.MaxY + 1), (o.MaxX + 1, o.MaxY + 1) })
            {
                double qx = cs * px - sn * py + tx, qy = sn * px + cs * py + ty;
                if (qx < _bbox.x0 - 200 || qx > _bbox.x1 + 200 || qy < _bbox.y0 - 200 || qy > _bbox.y1 + 200) continue;
                x0 = Math.Min(x0, (int)Math.Floor(qx) - 2); y0 = Math.Min(y0, (int)Math.Floor(qy) - 2); x1 = Math.Max(x1, (int)Math.Ceiling(qx) + 2); y1 = Math.Max(y1, (int)Math.Ceiling(qy) + 2);
            }
        }
        return (x0, y0, x1, y1);
    }

    /// <summary>화면에서 누른 자리(칸 · 소수)에 가장 가까운 사람 · 로봇을 고른다 (한 칸 안) — 없으면 고른 것을 놓는다.</summary>
    public bool SelectAt(double x, double y)
    {
        int best = Nearest(SpotsNow(), x, y);   // 메인 스레드가 올림에 쓰는 것과 같은 셈 (올린 사람과 누른 사람이 늘 같다)
        Selected = best; FollowSelected = best >= 0;
        return best >= 0;
    }

    /// <summary>고른 이의 카드: 이름 · 맡은 일 / 지금 생각 / 몸 / 마음 · 바람 (로봇: 전지 · 손상) / 죽었으면 그 까닭.</summary>
    public string[] Card()
    {
        if (Selected < 0) return Array.Empty<string>();
        var w = World;
        var mind = w.Find<Mind>(); var life = w.Find<Life>();
        if (mind == null || Selected >= mind.Agents.Count) return Array.Empty<string>();
        var a = mind.Agents[Selected];
        var lines = new List<string>();
        string role = a.IsPerson && a.C.Role < mind.RoleNames.Length ? " · " + mind.RoleNames[a.C.Role] : "";
        string nick = w.Find<Society>()?.NickOf(a.Id) is string nk ? $" '{nk}'" : "";   // ⑨ 배에서 부르는 별명
        lines.Add(mind.NameOf(a.Id) + nick + role);
        if (a.IsPerson && life != null && a.C.Body >= 0)
        {
            var b = life.Bodies[a.C.Body];
            if (b.Alive == 0)
            {
                lines.Add("죽었다 — " + Time.Clock(b.DiedAt));
                if (b.DeathCause != 0) lines.Add(w.Causes.Story(b.DeathCause));
                return lines.ToArray();
            }
            lines.Add("사람됨: " + Persona(mind, w.Find<Society>(), a));   // 4단: 누가 어떤 사람인지 (성격 낱말 · 꿈 · 즐겨 하는 것 · 짝 — 숫자는 내지 않는다)
            string inner = b.Conscious == 0 ? "(의식이 없다)" : b.Asleep != 0 ? "(자는 중)" : Inner.Of(w, mind, a);   // 4단 속말: 그 사람 말투의 머릿속 말
            lines.Add("생각: " + inner);
            string pondered = mind.RecentThought(a.Id, w.Tick, Time.Hours(1));   // 판단 엔진: 막혀서 한 궁리 (한 시간 안)
            if (pondered.StartsWith("생각: ")) pondered = pondered[4..];
            if (pondered.Length > 0 && !inner.Contains(pondered)) lines.Add("궁리: " + TextLint.Clean(pondered));
            string state = b.Conscious == 0 ? "쓰러짐" : b.Asleep != 0 ? "잠" : "깨어 있음";
            lines.Add($"몸: {state} · 핏속 산소 {b.SpO2 * 100:0}% · 체온 {b.CoreK - 273.15:0.0}℃{(b.Suit != 0 ? " · 우주복" : "")}");
            var hurt = new List<string>();
            if (b.Burn > 0.05f) hurt.Add($"화상 {b.Burn * 100:0}");
            if (b.Bleed > 0.01f) hurt.Add("피가 난다");
            if (b.Blunt > 0.05f) hurt.Add($"타박 {b.Blunt * 100:0}");
            if (b.LungDamage > 0.05f) hurt.Add($"폐 {b.LungDamage * 100:0}");
            if (b.COHb > 0.05f) hurt.Add($"일산화탄소 {b.COHb * 100:0}%");
            if (b.Hypoxia > 0.1f) hurt.Add("숨이 모자랐다");
            if (hurt.Count > 0) lines.Add("다침: " + string.Join(" · ", hurt));
            var c = a.C;
            var feel = new List<(string, float)> { ("두려움", c.Fear), ("슬픔", c.Sad), ("화", c.Anger), ("기쁨", c.Joy), ("긴장", c.Stress) }
                .Where(x => x.Item2 > 0.3f).OrderByDescending(x => x.Item2).Select(x => x.Item1).Take(2).ToList();
            var want = new List<(string, float)> { ("배고픔", c.Hunger), ("목마름", c.Thirst), ("졸림", c.Sleepy), ("외로움", c.Lonely), ("심심함", c.Bored) }
                .Where(x => x.Item2 > 0.6f).OrderByDescending(x => x.Item2).Select(x => x.Item1).Take(2).ToList();
            if (feel.Count + want.Count > 0) lines.Add("마음: " + string.Join(" · ", feel.Concat(want)));
        }
        else
        {
            RobotCard(w, mind, a, lines);   // 4단: 하는 일 ▸ 방법 · 맡은 까닭 · 최근 판단
            var net = w.Find<Network>();
            if (net != null && a.C.Mech >= 0 && a.C.Mech < net.Robots.Count)
            {
                var rb = net.Robots[a.C.Mech];
                var hurt = new List<string>();
                if (rb.Legs > 0.05f) hurt.Add($"다리 {rb.Legs * 100:0}{(rb.Legs >= 1 ? " (못 걷는다)" : "")}");
                if (rb.Arms > 0.05f) hurt.Add($"팔 {rb.Arms * 100:0}{(!Network.RobotHands(rb) ? " (손을 못 쓴다)" : "")}");
                if (rb.Eyes > 0.05f) hurt.Add($"센서 {rb.Eyes * 100:0}");
                if (rb.Core > 0.05f) hurt.Add($"속 {rb.Core * 100:0}{(rb.Core >= 1 ? " (멈췄다)" : "")}");
                lines.Add($"전지 {rb.Battery * 100:0}%" + (hurt.Count > 0 ? " · 상함 " + string.Join(" · ", hurt) : " · 멀쩡하다"));
                int why = rb.Core >= 1 ? rb.CoreCause : rb.Legs >= rb.Arms && rb.Legs > 0.05f ? rb.LegsCause : rb.Arms > 0.05f ? rb.ArmsCause : rb.Eyes > 0.05f ? rb.EyesCause : 0;
                if (why != 0) lines.Add("왜: " + w.Causes.Story(why));
            }
            else lines.Add($"전지 {a.C.Battery * 100:0}% · 손상 {a.C.Damage * 100:0}%");
        }
        return lines.ToArray();
    }

    /// <summary>
    /// 사람됨 한 줄 (docs/MIND.md 4단 — 화면만 · 세계의 값을 읽기만): 가장 두드러진 성격 둘~셋을 낱말로 ('꼼꼼하고 겁이 많다') ·
    /// 꿈 (인생 목표 — 이뤘으면 '이뤘다') · 즐겨 하는 것 (쉬는 시간의 취미) · 짝. 숫자는 내지 않는다.
    /// </summary>
    public static string Persona(Mind mind, Society? soc, Agent a)
    {
        var c = a.C;
        // (축 값 · 높을 때 [이어 말 · 끝말] · 낮을 때) — 0.5에서 멀수록 두드러진다
        var traits = new (float v, string hiGo, string hiEnd, string loGo, string loEnd)[]
        {
            (c.Consc, "꼼꼼하고", "꼼꼼하다", "덤벙대고", "덤벙댄다"),
            (c.Extra, "말이 많고", "말이 많다", "조용하고", "조용하다"),
            (c.Agree, "다정하고", "다정하다", "무뚝뚝하고", "무뚝뚝하다"),
            (c.Neuro, "걱정이 많고", "걱정이 많다", "느긋하고", "느긋하다"),
            (c.Open, "새것을 좋아하고", "새것을 좋아한다", "늘 하던 것을 좋아하고", "늘 하던 것을 좋아한다"),
            (c.Brave, "용감하고", "용감하다", "겁이 많고", "겁이 많다"),
        };
        var picked = traits.Select((t, i) => (t, i, d: Math.Abs(t.v - 0.5f))).Where(x => x.d >= 0.15f).OrderByDescending(x => x.d).ThenBy(x => x.i).Take(3).ToList();
        string words;
        if (picked.Count == 0) words = "무난하다";
        else
        {
            var parts = picked.Select((x, k) => k < picked.Count - 1 ? (x.t.v >= 0.5f ? x.t.hiGo : x.t.loGo) : (x.t.v >= 0.5f ? x.t.hiEnd : x.t.loEnd));
            words = string.Join(" ", parts);
        }
        var bits = new List<string> { words };
        string aim = Mind.AimName((Aim)c.Aim);
        if (aim.Length > 0) bits.Add($"꿈: {aim}{(c.AimMet != 0 ? " (이뤘다)" : "")}");
        bits.Add("즐겨 하는 것: " + (Hobby)c.Hobby switch
        {
            Hobby.Read => "책 읽기", Hobby.Game => "놀이판", Hobby.Film => "영상 보기", Hobby.Music => "음악 듣기",
            Hobby.Study => "자료 들여다보기", Hobby.Tinker => "손으로 만들기", _ => "가만히 쉬기",
        });
        int mate = soc?.PartnerOf(a.Id) ?? -1;
        if (mate >= 0 && mate < mind.Agents.Count) bits.Add($"짝: {mind.NameOf(mate)}");
        return string.Join(" · ", bits);
    }

    /// <summary>손으로 카메라 옮기기 (따라가기를 끈다).</summary>
    public void Pan(double dx, double dy) { _handAt = _anim; Follow = false; Overview = false; FollowSelected = false; var c = Camera(); CamX = c.left + c.w / 2 + dx; CamY = c.top + c.h / 2 + dy; }
    public void ZoomBy(double f) { _handAt = _anim; if (!FollowSelected) Follow = false; Overview = false; ViewW = Math.Clamp(ViewW * f, MinView, MaxView); }

    /// <summary>지금 화면이 보는 곳 (칸 · 소수): 배 둘레 안으로 맞춘다 (배보다 넓으면 가운데).</summary>
    public (double left, double top, double w, double h) Camera()
    {
        double w = ViewW, h = ViewW * Aspect;
        var bb = Reach();
        double bw = bb.x1 - bb.x0 + 1, bh = bb.y1 - bb.y0 + 1;
        double left = CamX - w / 2, top = CamY - h / 2;
        left = w >= bw ? bb.x0 - (w - bw) / 2 : Math.Clamp(left, bb.x0, bb.x1 + 1 - w);
        top = h >= bh ? bb.y0 - (h - bh) / 2 : Math.Clamp(top, bb.y0, bb.y1 + 1 - h);
        return (left, top, w, h);
    }

    /// <summary>지금 화면을 그린다 (32px 칸 · 보이는 곳을 덮는 칸들 + 그 안의 보일 곳).</summary>
    /// <summary>새로 잡은 그림판 수 (재기용).</summary>
    public static int NewCanvas;
    private readonly List<Img> _keep = new();
    /// <summary>지난 장을 그린 세계 · 틱 (같으면 틱이 지나지 않은 장 — 화가가 안개 칸을 다시 훑지 않는다 · <see cref="Painter.Still"/>).</summary>
    private Sim? _drawnWorld;
    private long _drawnTick;
    /// <summary>세밀도 단계 (docs/GRAPHICS.md §3): 보는 칸이 이보다 많으면 칸당 16 · 8도트 (1920 화면에서 한 픽셀에 도트가 하나 넘게 오지 않게) · 경계 ±FarGap칸은 지금 도트 그대로.</summary>
    public const double Far16 = 60, Far8 = 120, FarGap = 3;
    private int _dot = Painter.T;
    /// <summary>보는 칸 수 w에 맞는 칸당 도트 (머뭇거림: 경계를 FarGap칸 넘어야 바꾼다).</summary>
    public int DotFor(double w)
    {
        static int At(double w, double gap) => w > Far8 + gap ? 8 : w > Far16 + gap ? 16 : 32;
        int fine = At(w, FarGap), coarse = At(w, -FarGap);   // 넓어질 때는 +gap을 넘어야 · 좁아질 때는 −gap 밑이어야
        return _dot = Math.Clamp(_dot, coarse, fine);
    }

    public LiveFrame Render()
    {
        var (left, top, w, h) = Camera();
        int x0 = (int)Math.Floor(left), y0 = (int)Math.Floor(top);
        int wc = (int)Math.Ceiling(w) + 1, hc = (int)Math.Ceiling(h) + 1; // 줌이 바뀔 때만 크기가 바뀐다 (텍스처를 덜 새로 만든다)
        int D = DotFor(w);
        // 그림판은 넉넉히 (그리는 칸은 그대로) — 확대가 부드럽게 바뀌는 동안 장마다 새 그림판(몇 MB) · 새 텍스처를 만들지 않게:
        // 남은 그림판이 담을 수 있고 넓이가 1.5배 안이면 그대로 쓰고 (가장 작은 것) · 새로 잡을 때는 가로세로 1/8과 한 칸씩 더
        int needW = wc * D, needH = hc * D;
        long needA = (long)needW * needH;
        Img? reuse = null;
        _keep.Clear();
        while (_spare.TryTake(out var sp))
        {
            long a = (long)sp.W * sp.H;
            if (sp.W >= needW && sp.H >= needH && a * 2 <= needA * 3 && (reuse == null || a < (long)reuse.W * reuse.H)) { if (reuse != null) _keep.Add(reuse); reuse = sp; }
            else _keep.Add(sp);
        }
        _keep.Sort((a, b) => ((long)b.W * b.H).CompareTo((long)a.W * a.H));
        for (int k = 0; k < _keep.Count && k < 2; k++) _spare.Add(_keep[k]);   // 큰 것을 남긴다 (다시 넓게 볼 때 쓴다)
        if (reuse == null) { reuse = new Img((wc + wc / 8 + 1) * D, (hc + hc / 8 + 1) * D); NewCanvas++; } // 넓이 1.35배쯤 — 다음 장에 그대로 쓰인다
        Painter.Show = Show; Painter.ChainCells = Show == Overlay.Chain ? ChainCellsNow() : null;
        // ④ 칸 강체: 틱 사이를 메운 몸의 자세 (떨어져 나간 조각이 있을 때) · 배의 자세(별 — 갈라지지 않은 배도: 구멍만 나면 몸 0 하나가 서서 뿜어 돈다)
        var stb = World.Find<Structure>();
        if (stb != null && stb.Bodies.Count > 0)
        {
            if (stb.Bodies.Count > 1)
            {
                var poses = new (double, double, double, double)[stb.Bodies.Count];
                for (int b = 0; b < poses.Length; b++) poses[b] = PoseDrawn(stb, b);
                Painter.Poses = poses;
            }
            else Painter.Poses = null;
            // 배의 세상 자세도 지난 틱 끝 → 지금을 틱 사이 비율만큼 곧게 (반-차기 셈이라 틱 안 무게중심 길 · 각이 곧다 — 부스러기를 그 때 자리에 · 각은 감지 않는다: 빨리 도는 배의 별도 도는 쪽으로)
            var o0 = stb.Bodies[0];
            if (World.Tick == _prevTick + 1)
            {
                double fr = Math.Clamp(_carry, 0, 1), pc = Math.Cos(_prevShip.th), ps = Math.Sin(_prevShip.th);
                double dx = o0.Lx - _prevShip.lx, dy = o0.Ly - _prevShip.ly;   // 지금 무게중심 (제 틀)이 지난 틱 끝에 있던 세상 자리 (그 사이 떨어져 나간 질량만큼 무게중심이 옮았어도)
                double wx = _prevShip.x + pc * dx - ps * dy, wy = _prevShip.y + ps * dx + pc * dy;
                Painter.SkyTh = _prevShip.th + (o0.Th - _prevShip.th) * fr;
                Painter.ShipX = wx + (o0.X - wx) * fr; Painter.ShipY = wy + (o0.Y - wy) * fr;
                Painter.TickFrac = fr;
            }
            else { Painter.SkyTh = o0.Th; Painter.ShipX = Painter.ShipY = double.NaN; Painter.TickFrac = 1; }   // 되살린 첫 틱 · 건너뛴 틱은 지금 자세로
        }
        else { Painter.Poses = null; Painter.SkyTh = double.NaN; Painter.ShipX = Painter.ShipY = double.NaN; Painter.TickFrac = 1; }
        Painter.Sub = Math.Clamp(_carry, 0, 1);
        Painter.Still = ReferenceEquals(World, _drawnWorld) && World.Tick == _drawnTick;   // 세계는 틱에만 바뀐다 (멈춤 · 느린 연출 · 따라 보기 · 계산 일꾼을 기다리는 장 — 다시보기로 바꾼 세계는 아니다)
        _drawnWorld = World; _drawnTick = World.Tick;
        var img = D == Painter.T ? Painter.Frame(World, x0, y0, wc, hc, (int)(_anim * AnimFps), reuse) : Painter.FrameFar(World, x0, y0, wc, hc, (int)(_anim * AnimFps), D, reuse);
        if (D == Painter.T) Painter.Prebake(World);   // 넓게 볼 때 첫 장에 굽기가 몰리지 않게 (장마다 덩어리 하나)
        (double x, double y) sel = Selected >= 0 ? DrawnAt(Selected) : (double.NaN, 0.0);
        var (shx, shy) = Shake();
        var prows = Panel == PanelKind.None ? Array.Empty<PanelRow>() : PanelRows();
        SyncPick(prows);
        var latest = Dir.Highlights.Count > 0 ? Dir.Highlights[^1] : null;
        float sk = D / (float)Painter.T;   // 흔들림은 32도트 자리
        float srcX = Math.Clamp((float)((left - x0) * D) + shx * sk, 0, Math.Max(0, needW - (float)(w * D)));   // 그린 칸 안에서만 (그림판의 나머지는 지난 장)
        float srcY = Math.Clamp((float)((top - y0) * D) + shy * sk, 0, Math.Max(0, needH - (float)(h * D)));
        return new LiveFrame
        {
            Img = img,
            SrcX = srcX, SrcY = srcY,
            SrcW = (float)(w * D), SrcH = (float)(h * D), Dot = D,
            Ship = ShipName, Status = Status(), Story = Story(), Mood = View.Mood, Cut = Cut,
            Banner = BannerLeft > 0 ? Banner : "", BannerAlpha = (float)Math.Clamp(Math.Min(BannerLeft, BannerSec - BannerLeft) / 0.5, 0, 1),
            Tick = World.Tick, Replay = _replayOf == null ? "" : ShotNow is { } sn ? $"{_replayOf.Title} · 컷 {_shots.IndexOf(sn) + 1}/{_shots.Count} {sn.Kind}" : _replayOf.Title,
            CamLeft = left, CamTop = top, CamW = w, CamH = h,
            Card = Card(), SelX = sel.x, SelY = sel.y,
            Notice = Notice, Route = RouteLine(),
            Broadcast = _castLeft > 0 ? _cast : "", BroadcastAlpha = (float)Math.Clamp(Math.Min(_castLeft, CastSec - _castLeft) / 0.5, 0, 1),
            Captions = Ears.Captions ? Ears.CaptionsNow() : Array.Empty<ShipView.Audio.SoundCaption>(),
            PanelTitle = Panel == PanelKind.None ? "" : PanelTitleOf(Panel), PanelRows = prows, PanelPick = Panel == PanelKind.None ? -1 : PanelPick,
            PanelFoot = Panel == PanelKind.None ? "" : PanelFootOf(),
            Vitals = Vitals(),
            Crew = CrewBar(), Robots = RobotBar(), Talk = Talk(), Faces = _facesShown, FacesVersion = _facesVersion, Selected = Selected,
            Feed = Feed(), Paused = Paused,
            // 마우스 화면의 손잡이 상태 (docs/UI.md 1.2)
            Panel = Panel, PanelPickKey = PanelPickKey, Show = Show, Legend = Show == Overlay.None ? "" : OverlayName(Show),
            Follow = Follow, Overview = Overview, FollowSelected = FollowSelected, Replaying = _replay != null, HandLeft = HandLeft,
            SpeedPick = SpeedPick, FixedPick = FixedPick, CaptionsOn = Ears.Captions,
            CanReplay = latest != null && CanReplay(latest), ReplayWhy = latest == null ? "아직 볼 만한 장면이 없다" : CanReplay(latest) ? "" : "한 시간 넘게 지나 다시 볼 수 없다",
            Holding = Holding, DepartIn = DepartInMin(), Highlights = Dir.Highlights.Count, JournalNew = JournalNew, NoticeLink = Notice.Length > 0 ? _noticeLink : PanelKind.None,
            StoryIsChain = ChainNow().Count > 0, Spots = SpotsNow(),
            Bubbles = Bubbles(), SpeechOn = SpeechBubbles, ThoughtsOn = ThoughtBubbles,
            HoverId = HoverOk() ? HoverId : -1, HoverName = HoverOk() ? World.Find<Mind>()!.NameOf(HoverId) : "", HoverDoing = HoverOk() ? World.Find<Mind>()!.Thought(HoverId) : "",
        };
    }

    /// <summary>화면 흔들림 (그래픽 결정 11 · 적당히): ④ 폭발의 큰 소리 · 충격 · 벽이 터진 사실이 2틱 안이면 잘라 낼 곳을 흔든다 — 클수록 세게 · 잦아든다 (도트).</summary>
    public (float dx, float dy) Shake()
    {
        double amp = 0, sub = Math.Clamp(_carry, 0, 1);
        foreach (var f in World.Facts.Recent(256))
        {
            double age = World.Tick - f.Tick + sub;
            if (age > 2) continue;
            double a = Painter.IsBlast(f) ? Math.Min(10, 3 + (f.Size - 120) / 4)
                : f.Kind == FactKind.Impact && f.Size > 20 ? Math.Min(6, 2 * Math.Log10(f.Size))
                : f.Kind == FactKind.WallBreach ? 3 : 0;
            amp = Math.Max(amp, a * Math.Max(0, 1 - age / 2));
        }
        if (amp < 0.5) return (0, 0);
        int k = (int)(_anim * 30);
        return ((float)((DotHash.F(k, 1) - 0.5) * 2 * amp), (float)((DotHash.F(k, 2) - 0.5) * 2 * amp));
    }

    /// <summary>다 쓴 그림을 돌려준다 (화면이 텍스처로 옮긴 뒤 — 다음 장에 다시 쓴다).</summary>
    public void Recycle(Img img) { if (_spare.Count < 3) _spare.Add(img); }

    // ── 주컴퓨터 방송 · 일지 (⑩) — 산 세계에서만 ──
    private string _cast = ""; private double _castLeft; private int _journals = -1; private long _castTick = long.MinValue, _journalTick = long.MinValue;
    public const double CastSec = 12;

    /// <summary>주컴퓨터가 방송하면 (아침 방송 · 항해 달력 — 소리 단계 S6) 자막으로 몇 초 · 일지를 쓰면 알림 (J로 보기).</summary>
    private void WatchComputer(double realSec)
    {
        _castLeft = Math.Max(0, _castLeft - realSec);
        var sm = Sim.Find<ShipMind>();
        if (sm == null) return;
        long lastJournal = sm.Journal.Count > 0 ? sm.Journal[^1].tick : long.MinValue;
        if (_journals < 0) { _journals = 0; _journalTick = lastJournal; _castTick = sm.Broadcasts.Count > 0 ? sm.Broadcasts[^1].tick : long.MinValue; }
        if (sm.Broadcasts.Count > 0 && sm.Broadcasts[^1].tick != _castTick) { _castTick = sm.Broadcasts[^1].tick; _cast = "주컴퓨터 방송 — " + sm.Broadcasts[^1].text; _castLeft = CastSec; }
        // 일지: 마지막 일지의 때로 견준다 (예전엔 줄 수로 — 60줄을 넘으면 앞에서 지워 수가 늘 60이라 알림이 멈췄다)
        if (lastJournal != _journalTick) { _journalTick = lastJournal; if (lastJournal != long.MinValue) { JournalNew = true; Notify("주컴퓨터가 일지를 썼다 — 눌러서 보기", 5, PanelKind.Journal); } }
    }

    // ── 판: 하이라이트 목록(L) · 항로(T) · 일지(J) ──
    public PanelKind Panel;
    public int PanelPick;

    /// <summary>판을 열거나 (같은 판이면) 닫는다. 열 때 맨 위(가장 새것 · 지금 성격)를 고른다.</summary>
    public void TogglePanel(PanelKind k)
    {
        if (Panel == k) { Panel = PanelKind.None; return; }
        Panel = k;
        if (k == PanelKind.Journal) JournalNew = false;
        var o = Sim.Find<Outside>();
        PanelPick = k == PanelKind.Route && o != null ? Math.Max(0, o.Route - 1) : 0;
        var rows = PanelRows();
        PanelPickKey = PanelPick < rows.Length ? rows[PanelPick].Key : 0;
    }

    /// <summary>↑ ↓: 판에서 고르기.</summary>
    public void PanelMove(int d)
    {
        var rows = PanelRows();
        SyncPick(rows);
        int n = Panel switch { PanelKind.Highlights => Dir.Highlights.Count, PanelKind.Route => Sim.Tables.Space.Routes.Count, PanelKind.None => 0, _ => rows.Length };
        if (n == 0) { PanelPick = 0; return; }
        PanelPick = Math.Clamp(PanelPick + d, 0, Math.Min(n, rows.Length) - 1);
        PanelPickKey = rows[PanelPick].Key;
    }

    /// <summary>← →: (판마다 — 지금은 쓰는 판이 없다).</summary>
    public void PanelSide(int d) { }

    /// <summary>Enter: 하이라이트는 다시보기 · 항로는 출항 전이면 고른 항로로. 한 일이 있으면 true.</summary>
    public bool PanelEnter()
    {
        SyncPick(PanelRows());
        switch (Panel)
        {
            case PanelKind.Highlights:
            {
                var list = HighlightsNewest();
                if (PanelPick < 0 || PanelPick >= list.Count) return false;
                if (!StartReplay(list[PanelPick])) { Notify("그 장면은 너무 오래돼 다시 볼 수 없다 (한 시간 안의 것만)"); return false; }
                Panel = PanelKind.None;
                return true;
            }
            case PanelKind.Route:
                return ChooseRoute(PanelPick + 1);
            case PanelKind.Chain:
            {
                // 고른 마디의 자리로 카메라를 옮긴다 (손으로 — F로 다시 연출을 따른다)
                var chain = ChainNow();
                int i = PanelPick - 1;   // 첫 줄은 사슬 한 줄 요약
                if (i < 0 || i >= chain.Count) return false;
                int cell = World.Causes[chain[i]].Cell;
                if (cell < 0) { Notify("그 일은 배 한 곳의 일이 아니다"); return false; }
                LookAt(cell);
                return true;
            }
            case PanelKind.People:
            {
                var people = PeopleOrder();
                if (PanelPick < 0 || PanelPick >= people.Count) return false;
                Selected = people[PanelPick]; FollowSelected = true; Overview = false;
                Panel = PanelKind.None;
                return true;
            }
        }
        return false;
    }

    /// <summary>카메라를 그 칸으로 (손으로 옮긴 것과 같다).</summary>
    public void LookAt(int cell)
    {
        _handAt = _anim; Follow = false; Overview = false; FollowSelected = false;
        (CamX, CamY) = CellDrawn(cell);   // 떨어져 나간 조각의 칸이면 그 조각이 그려진 곳
    }

    // ── 겹쳐 보기 (O) ──
    public Overlay Show;
    public static string OverlayName(Overlay o) => o switch
    {
        Overlay.Air => "공기 (붉을수록 산소가 옅다 · 파랑은 진공 · 보라는 일산화탄소)", Overlay.Heat => "열 (주황 · 빨강은 뜨겁고 하늘색은 차다)",
        Overlay.Smoke => "연기", Overlay.Chain => "사슬 선 (지금 장면의 원인 사슬이 지나간 자리)",
        Overlay.Lines => "선과 관 (전선 노랑 · 냉각관 하늘 · 물관 파랑 · 센서 선 초록 · 끊긴 곳 빨간 X · 새는 곳 보라)", _ => "겹쳐 보기 끔",
    };
    public void CycleOverlay() { Show = (Overlay)(((int)Show + 1) % 6); Notify(OverlayName(Show)); }

    /// <summary>지금 장면의 원인 사슬 (뿌리 → 끝) — 연출이 보는 사슬 (한 줄로 가장 많이 건넌 끝) · 없으면 가장 최근 하이라이트 (30분 안).</summary>
    public List<int> ChainNow()
    {
        var w = World; var d = View;
        int cause = 0;
        if (d.SceneRoot != 0 && d.Chains.TryGetValue(d.SceneRoot, out var rec)) cause = rec.LineCause != 0 ? rec.LineCause : rec.Cause;
        if (cause == 0 && d.Highlights.Count > 0 && w.Tick - d.Highlights[^1].Last < Time.Minutes(30)) cause = d.Highlights[^1].Cause;
        return cause != 0 ? w.Causes.Chain(cause) : new List<int>();
    }

    /// <summary>사슬 선의 자리들 (같은 칸이 잇따르면 한 번).</summary>
    public List<int> ChainCellsNow()
    {
        var cells = new List<int>();
        foreach (int c in ChainNow()) { int cell = World.Causes[c].Cell; if (cell >= 0 && (cells.Count == 0 || cells[^1] != cell)) cells.Add(cell); }
        return cells;
    }

    /// <summary>사람 판의 차례: 위험한 사람부터 (쓰러짐 · 산소 · 다침 · 공황 · 긴장) — 같으면 이름.</summary>
    public List<int> PeopleOrder()
    {
        var w = World; var mind = w.Find<Mind>(); var life = w.Find<Life>();
        if (mind == null || life == null) return new List<int>();
        float Concern(Agent a)
        {
            var b = life.Bodies[a.C.Body];
            if (b.Alive == 0) return -1;
            return (b.Conscious == 0 ? 100 : 0) + (1 - b.SpO2) * 200 + b.Hypoxia * 50 + Math.Max(b.Burn, b.Blunt) * 40 + b.Infect * 20 + (a.C.Mode != 0 ? 30 : 0) + a.C.Stress * 10;
        }
        return mind.Agents.Where(a => a.IsPerson).OrderByDescending(Concern).ThenBy(a => mind.NameOf(a.Id)).Select(a => a.Id).ToList();
    }

    private List<Highlight> HighlightsNewest() { var l = Dir.Highlights.ToList(); l.Reverse(); return l; }

    /// <summary>그 하이라이트를 다시 볼 수 있나 (다시보기 고리에 그 앞 찍기가 있다).</summary>
    public bool CanReplay(Highlight h) => Dir.Ring.Count > 0 && Dir.Ring[0].tick <= h.Tick;

    private static string HourMin(long t) { long m = t % Time.TicksPerDay * 60 / Time.TicksPerHour; return $"{t / Time.TicksPerDay + 1}일 {m / 60:00}:{m % 60:00}"; }

    private string PanelTitleOf(PanelKind k) => k switch
    {
        PanelKind.Highlights => $"볼 만했던 장면 ({Dir.Highlights.Count})",
        PanelKind.Route => "항로 — 위험도는 항로만",
        PanelKind.Journal => "주컴퓨터",
        PanelKind.Chain => "사슬 — 지금 장면은 어디서 시작됐나",
        PanelKind.People => "사람",
        _ => "",
    };

    private string PanelFootOf() => Panel switch
    {
        PanelKind.Highlights => "줄을 눌러 고르기 · ▶ 다시보기 · 휠로 넘기기 (↑↓ · Enter · L)",
        PanelKind.Route => double.IsNaN(DepartInMin()) ? "항로는 출항 때만 고른다 · 휠로 넘기기 (T)" : "항로를 누르면 그 길로 · 출항 전에만 (↑↓ · Enter · T)",
        PanelKind.Journal => "줄을 누르면 펼친다 · 휠로 넘기기 (↑↓ · J)",
        PanelKind.Chain => "마디를 누르면 그 자리로 · 위 단추로 사슬 선 (↑↓ · Enter · O · C)",
        PanelKind.People => "사람을 누르면 따라간다 · 휠로 넘기기 (↑↓ · Enter · P)",
        _ => "",
    };

    /// <summary>열린 판의 줄들.</summary>
    public PanelRow[] PanelRows()
    {
        var rows = new List<PanelRow>();
        switch (Panel)
        {
            case PanelKind.Highlights:
                for (int hi = Dir.Highlights.Count - 1; hi >= 0; hi--)
                {
                    var h = Dir.Highlights[hi];
                    string story = h.Cause != 0 ? Sim.Causes.Story(h.Cause) : "";
                    string said = h.Line.Length > 0 ? h.Line : story.Length > 0 && story != h.Title ? story : "";
                    bool can = CanReplay(h);
                    string when = can ? "" : "한 시간 넘게 지나 다시 볼 수 없다";
                    rows.Add(new PanelRow($"{HourMin(h.Tick)}  {(h.People ? "사람 이야기 · " : "")}{h.Title}", said.Length > 0 && when.Length > 0 ? said + "\n" + when : said + when, can,
                        Key: KeyHighlight(hi), Act: "▶ 다시보기", ActOn: can, Why: "한 시간 넘게 지나 다시 볼 수 없다 (한 시간 안의 것만)"));
                }
                if (rows.Count == 0) rows.Add(new PanelRow("아직 볼 만한 장면이 없다", "조용한 항해 — 일이 나면 여기에 쌓인다", false, Key: -9));
                break;
            case PanelKind.Route:
            {
                var o = Sim.Find<Outside>();
                if (o == null) break;
                var routes = Sim.Tables.Space.Routes;
                var (flat, _) = Outside.HohmannDv(o.VoyageFrom > 0 ? o.VoyageFrom : o.Distance, double.IsNaN(o.PendingTo) ? o.VoyageTo : o.PendingTo);
                for (int i = 0; i < routes.Count; i++)
                {
                    var r = routes[i];
                    double dv = Outside.HohmannDv(o.VoyageFrom > 0 ? o.VoyageFrom : o.Distance, double.IsNaN(o.PendingTo) ? o.VoyageTo : o.PendingTo, r.Incline).dv1 - flat;
                    bool open = !double.IsNaN(DepartInMin());
                    rows.Add(new PanelRow($"{i + 1}  {r.Name}", $"{r.Line}\n파편 ×{r.Zones:0.##} · 깊은 우주 ×{r.Deep:0.##} · 지나는 배 ×{r.Traffic:0.##}{(r.Incline > 0 ? $" · 면 {r.Incline:0.#}° (출발 +{dv:0}m/s)" : "")}", o.Route == i + 1,
                        Key: i + 1, ActOn: open, Why: "항로는 출항 때만 고른다"));
                }
                var zone = o.ZoneAt(o.Distance); var ahead = o.ZoneAhead(Sim.Tick, 24);
                var sense = Sim.Find<Sensors>();
                rows.Add(new PanelRow(o.VoyageTicks > 0 ? $"항해 {(Sim.Tick - o.VoyageStart) / (double)Time.TicksPerDay:0}일째 / {o.VoyageTicks / (double)Time.TicksPerDay:0}일 · {o.Distance:0.000}AU" : "출항 전 — 항로를 고를 수 있다",
                    $"{(zone != null ? $"지금 {zone.Name}" : "깊은 우주")}{(ahead != null ? $" · 하루 안에 {ahead.Name}" : "")} · 고향까지 빛 {o.HomeLightMinutes(Sim.Tick):0}분 · 센서에 잡힌 것 {sense?.Tracks.Count ?? 0}", false, Key: -1));
                break;
            }
            case PanelKind.Journal:
            {
                var sm = Sim.Find<ShipMind>();
                if (sm == null) break;
                // 지금: 이어지는 사건 · 배 형편 (주컴퓨터가 읽은 것) — 그 아래 일지
                var now = Sim.Tick;
                foreach (var x in sm.Incidents.Where(x => x.Active != 0).OrderBy(x => x.Since))
                {
                    var who = new List<string>();
                    var mind = Sim.Find<Mind>();
                    if (x.Sealed != 0) who.Add("봉쇄");
                    if (mind != null) foreach (int r in new[] { x.RobotA, x.RobotB }) if (r >= 0 && r < mind.Agents.Count) who.Add($"{mind.NameOf(r)}에게 맡김");
                    if (x.Person >= 0 && mind != null && x.Person < mind.Agents.Count) who.Add($"{mind.NameOf(x.Person)}이(가) 받아 줌");
                    string plan = sm.PlanLine(x.Id), chain = plan.Length > 0 ? sm.ChainLine(x) : "";   // 판단 엔진: 지어낸 계획 — 생각 사슬 · 걸음 (✔ 마침 · ▶ 하는 중)
                    rows.Add(new PanelRow($"지금 · {sm.RoomName(x.Room)}: {ShipMind.KindName(x.Kind)} ({Math.Max(0, now - x.Since) / (double)Time.Minutes(1):0}분째)",
                        (who.Count > 0 ? string.Join(" · ", who) : "아직 손을 쓰지 못했다") + (chain.Length > 0 ? "\n궁리: " + chain : "") + (plan.Length > 0 ? "\n계획: " + plan : ""), true, Key: x.Id));
                }
                string blind = sm.BlindLine();
                rows.Add(new PanelRow("배 형편", sm.ShipLine(Sim, true) + (blind.Length > 0 ? "\n" + blind : "") + (sm.HelmLine.Length > 0 ? "\n항로: " + sm.HelmLine : ""), false, Key: -1));
                var manual = Sim.Find<Society>()?.Manual.Where(e => e.Removed == 0).ToList();   // 판단 엔진 (바): 회의가 넣은 매뉴얼
                if (manual != null && manual.Count > 0)
                    rows.Add(new PanelRow($"매뉴얼 ({manual.Count})", string.Join("\n", manual.Select(e => $"{e.Name} — 찬성 {e.Yes}/{e.Of} · 통한 번 {e.Wins}{(e.Fails > 0 ? $" · 틀어진 번 {e.Fails}" : "")}")), false, Key: -2));
                int head = rows.Count;
                for (int i = sm.Journal.Count - 1; i >= 0; i--)
                {
                    var (t, text) = sm.Journal[i];
                    var lines = text.Split('\n');
                    long key = KeyJournal(t);
                    rows.Add(new PanelRow($"{HourMin(t)}  {lines[0]}", key == PanelPickKey ? string.Join("\n", lines.Skip(1)) : "", false, Key: key));   // 고른 일지만 펼친다 (열쇠로 — 차례 번호는 새 사건이 위에 끼면 밀린다)
                }
                if (rows.Count == head) rows.Add(new PanelRow("아직 일지가 없다", $"주컴퓨터는 {sm.JournalHour}시에 하루를 적는다", false, Key: -4));
                if (sm.Morning.Length > 0) rows.Add(new PanelRow("아침 방송", sm.Morning, false, Key: -3));
                break;
            }
            case PanelKind.Chain:
            {
                var chain = ChainNow();
                if (chain.Count == 0) { rows.Add(new PanelRow("지금 이어지는 사슬이 없다", "일이 나면 그 일이 어디서 시작됐는지 여기에 차례로 적힌다", false, Key: -1)); break; }
                var w = World; var d = View;
                var root = d.Chains.GetValueOrDefault(chain[0]);
                string spread = d.SpreadText(chain[0]);
                rows.Add(new PanelRow(spread.Length > 0 ? $"건넌 갈래: {spread}" : "한 갈래 안의 일",
                    root != null ? $"이 뿌리에서 난 큰 일 {root.Facts} · 가지 {root.Leaves.Count} · {HourMin(root.First)}부터" : "", false, Key: -1));
                var cm = w.Find<ShipMind>();
                foreach (int c in chain)
                {
                    var n = w.Causes[c];
                    string kind = Director.PlainName[Director.EngineOf(n.Kind)];
                    string where = n.Cell >= 0 && w.Layout.Room[n.Cell] >= 0 ? (cm?.RoomName(w.Layout.Room[n.Cell]) ?? w.Layout.Rooms[w.Layout.Room[n.Cell]].Name) : n.Cell >= 0 ? "통로 · 벽" : "배 바깥 · 배 전체";
                    rows.Add(new PanelRow((kind.Length > 0 ? $"[{kind}] " : "") + w.Causes.Text(c), $"{HourMin(n.Tick)} · {where}", n.Cell >= 0, Key: c, ActOn: n.Cell >= 0, Why: "그 일은 배 한 곳의 일이 아니다"));
                }
                break;
            }
            case PanelKind.People:
            {
                var w = World; var mind = w.Find<Mind>(); var life = w.Find<Life>(); var so = w.Find<Society>();
                if (mind == null || life == null) break;
                if (so != null && so.CultureLine(w) is string culture) rows.Add(new PanelRow("이 배의 관습", culture, false, Key: -1));   // ⑨ 함께 먹는 때 · 금기 · 배의 규칙
                var slots = new Dictionary<int, int>();   // 사람 줄 얼굴 그림의 칸 (사람 차례)
                foreach (var pa in mind.Agents) if (pa.IsPerson) slots[pa.Id] = slots.Count;
                static PanelBar Bar(string name, float v) => new(name, Math.Clamp(v, 0, 1), v < 0.3f ? 2 : v < 0.6f ? 1 : 0);
                foreach (int id in PeopleOrder())
                {
                    var a = mind.Agents[id]; var b = life.Bodies[a.C.Body];
                    var bars = b.Alive == 0 ? null : new[]
                    {
                        Bar("숨", (b.SpO2 - 0.7f) / 0.28f),
                        Bar("배", 1 - a.C.Hunger),
                        Bar("잠", 1 - a.C.Sleepy),
                        Bar("마음", 1 - Math.Max(a.C.Stress, Math.Max(a.C.Sad, a.C.Fear))),
                    };
                    bool danger = b.Alive != 0 && (b.Conscious == 0 || b.SpO2 < 0.85f || b.Hypoxia > 0.2f || a.C.Mode != 0);
                    string state = b.Alive == 0 ? "숨을 거두었다" : b.Conscious == 0 ? "쓰러져 있다" : Mind.GoalName(a.C.Goal);
                    var body = new List<string> { $"산소 {b.SpO2 * 100:0}%" };
                    if (b.Burn > 0.05f) body.Add($"화상 {b.Burn:0.0}"); if (b.Blunt > 0.05f) body.Add($"타박 {b.Blunt:0.0}");
                    if (b.Infect > 0.05f) body.Add("상처가 곪는다"); if (b.Ill == 2) body.Add("앓는다"); if (b.Hypoxia > 0.05f) body.Add("숨이 가쁘다");
                    var heart = new List<string>();
                    if (a.C.Stress > 0.3f) heart.Add($"긴장 {a.C.Stress:0.0}"); if (a.C.Sad > 0.3f) heart.Add("슬프다"); if (a.C.Fear > 0.3f) heart.Add("두렵다"); if (a.C.Anger > 0.3f) heart.Add("화가 났다");
                    var near = new List<string>();
                    if (so != null)
                    {
                        int pt = so.PartnerOf(id); if (pt >= 0) near.Add($"짝 {mind.NameOf(pt)}");
                        int band = so.BandIdOf(id); if (band >= 0) near.Add(so.BandName(band));
                        int trust = -1, grudge = -1; float bt = 0.6f, bg = Society.GrudgeAt;
                        foreach (var o in mind.Agents)
                        {
                            if (!o.IsPerson || o.Id == id) continue;
                            if (so.TrustIn(id, o.Id) > bt) { bt = so.TrustIn(id, o.Id); trust = o.Id; }
                            if (so.GrudgeOf(id, o.Id) > bg) { bg = so.GrudgeOf(id, o.Id); grudge = o.Id; }
                        }
                        if (trust >= 0) near.Add($"가장 믿는 이 {mind.NameOf(trust)}"); if (grudge >= 0) near.Add($"앙금 {mind.NameOf(grudge)}");
                    }
                    if (so?.PastLine(id) is { Length: > 0 } past) near.Add(past);   // ⑫ 출항 과거사
                    string pondered = b.Alive != 0 ? mind.RecentThought(id, w.Tick, Time.Hours(1)) : "";
                    string thinks = b.Alive == 0 ? "" : b.Conscious == 0 ? "(의식이 없다)" : b.Asleep != 0 ? "(자는 중)" : Inner.Of(w, mind, a);   // 4단 속말 (카드와 같은 글)
                    rows.Add(new PanelRow($"{mind.NameOf(id)}{(so?.NickOf(id) is string nk ? $" '{nk}'" : "")} — {state}",
                        $"{Persona(mind, so, a)}{(thinks.Length > 0 ? "\n생각: " + thinks : "")}\n몸: {string.Join(" · ", body)}{(heart.Count > 0 ? " / 마음: " + string.Join(" · ", heart) : "")}{(near.Count > 0 ? "\n" + string.Join(" · ", near) : "")}{(pondered.Length > 0 ? "\n궁리: " + (pondered.StartsWith("생각: ") ? pondered[4..] : pondered) : "")}", danger,
                        slots.GetValueOrDefault(id, -1), bars, Key: KeyPerson(id)));
                }
                // 로봇 · 드론 (사람 다음 — 칩 줄의 '+N'이 여기로)
                foreach (var chip in RobotBar())
                {
                    var ra = mind.Agents[chip.Id];
                    var card = new List<string>();
                    RobotCard(w, mind, ra, card);
                    rows.Add(new PanelRow($"{mind.NameOf(chip.Id)} — {(chip.Level == 3 ? "멈췄다" : Mind.GoalName(ra.C.Goal))}", string.Join("\n", card.Skip(0)) + $"\n전지 {ra.C.Battery * 100:0}%", chip.Level >= 2, Key: KeyPerson(chip.Id)));
                }
                break;
            }
        }
        return rows.ToArray();
    }

    // ── 함장이 고르는 항로 (⑫ — 위험도는 항로만) ──
    private string _notice = ""; private double _noticeLeft;
    /// <summary>잠깐 띄우는 알림 (항로를 골랐다 등) — 남은 초가 0이면 빈 글.</summary>
    public string Notice => _noticeLeft > 0 ? _notice : "";
    public void Notify(string text, double seconds = 3, PanelKind link = PanelKind.None) { _notice = text; _noticeLeft = seconds; _noticeLink = link; }
    public void TickNotice(double realSec) => _noticeLeft = Math.Max(0, _noticeLeft - realSec);

    /// <summary>항로 한 줄: 번호 · 이름.</summary>
    public string RouteLine()
    {
        var o = Sim.Find<Outside>();
        return o?.Rt == null ? "" : $"항로 {o.Route} '{o.Rt.Name}'";
    }

    /// <summary>
    /// 항로를 고른다 — 출항 점화 전에만 (전이 궤도에 오른 뒤엔 바꿀 수 없다): 출발 점화를 새 항로의 Δv로 다시 계획한다 (주컴퓨터가 하는 것은 그대로).
    /// </summary>
    public bool ChooseRoute(int route)
    {
        var o = Sim.Find<Outside>();
        if (o == null || route < 1 || route > Sim.Tables.Space.Routes.Count) return false;
        if (double.IsNaN(DepartInMin())) { Notify("이미 떠났다 — 항로는 출항 때만 고른다"); return false; }   // 출발 점화가 시작됐으면 (계획에 남은 출발 점화가 없다) 더는 못 바꾼다
        o.Route = route;
        var due = o.Plan.FirstOrDefault(p => p.Kind == BurnKind.Depart);
        double minutes = due.Kind == BurnKind.Depart ? Math.Max(1, (due.Due - Sim.Tick) / (double)Time.TicksPerHour * 60) : 20;
        int c = Sim.Causes.Add(CauseKind.Route, $"함장이 항로를 골랐다 — {route} '{o.Rt!.Name}'", Sim.Tick);
        o.PlanVoyage(Sim, o.PendingTo, o.PendingName, minutes, c);
        Notify(RouteLine() + " — " + o.Rt.Line);
        return true;
    }

    /// <summary>이 빠르기(화면 1초에 틱)면 하루가 화면 얼마인가 — '3분' · '1.5시간' · '24시간'.</summary>
    public static string DayLength(double ticksPerSec)
    {
        double min = Time.TicksPerDay / Math.Max(1e-6, ticksPerSec) / 60;
        return min < 1 ? $"{min * 60:0}초" : min < 90 ? $"{min:0.#}분" : $"{min / 60:0.#}시간";
    }

    /// <summary>위 줄: 배 · 시각 · 분위기 · 빠르기 · 사람.</summary>
    public string Status()
    {
        var w = World;
        var life = w.Find<Life>(); var mind = w.Find<Mind>();
        int alive = 0, all = 0;
        if (mind != null && life != null)
            foreach (var a in mind.Agents) if (a.IsPerson) { all++; if (life.Bodies[a.C.Body].Alive != 0) alive++; }
        // 실제로 나아간 빠르기를 적는다 (고른 빠르기가 아니라 — 기계가 못 따라가면 그만큼) · 그 빠르기면 하루가 화면 몇 분인가
        double rate = Rate > 0 ? Rate : Speed;
        string speed = Paused ? "멈춤" : $"{(SpeedOverride > 0 ? "고정 " : "")}×{rate * Time.SecondsPerTick:0} · 하루 {DayLength(rate)}";
        string hand = FollowSlow ? " · 따라 보기 (천천히)" : "";   // 손 카메라 남은 초는 화면의 연출 단추 고리가 보인다 (docs/UI.md)
        if (Holding) speed = "출항 준비 · 멈춤";
        return $"{(_replay != null ? "다시보기 · " : "")}{ShipName} · {Time.Clock(w.Tick)} · {View.Mood} · {speed} · 사람 {alive}/{all}{hand}";
    }

    /// <summary>
    /// 배 상태 띠 (UI U1 — 늘 보인다 · 세계의 값을 읽기만 한다): 공기(사람이 있는 칸의 가장 낮은 산소 · 가장 높은 이산화탄소) · 전력(내는 것 / 쓰려는 것) ·
    /// 선체(구멍 난 칸 · 공기가 빠진 방) · 물자(식량 · 물이 남은 날) · 사람(산 이 · 쓰러진 이 · 다친 이) · 불(타는 칸).
    /// 단계 문턱: 산소 18.5 / 16kPa · 이산화탄소 1 / 3kPa · 전력 모자람 / 원자로 멈춤 · 구멍 / 빈 방 · 남은 날 20 / 7 · 다침 / 쓰러짐 · 불.
    /// </summary>
    public Gauge[] Vitals()
    {
        var w = World; var L = w.Layout;
        var f = w.Find<Field>(); var life = w.Find<Life>(); var mind = w.Find<Mind>(); var net = w.Find<Network>(); var chem = w.Find<Chemistry>();
        var o = new List<Gauge>(6);
        if (f != null && life != null && mind != null)
        {
            double minO2 = double.MaxValue, maxCO2 = 0;
            int all = 0, alive = 0, down = 0, hurt = 0;
            foreach (var a in mind.Agents)
            {
                if (!a.IsPerson) continue;
                all++;
                var b = life.Bodies[a.C.Body];
                if (b.Alive == 0) continue;
                alive++;
                if (b.Conscious == 0) down++;
                else if (b.Bleed > 0 || b.Burn > 0.2f || b.Pain > 0.5f || b.Infect > 0.3f) hurt++;
                if (b.Suit != 0 || (uint)b.Cell >= (uint)L.N) continue;
                minO2 = Math.Min(minO2, f.Partial(f.T.gO2, b.Cell));
                maxCO2 = Math.Max(maxCO2, f.Partial(f.T.gCO2, b.Cell));
            }
            if (minO2 == double.MaxValue) minO2 = 21;
            o.Add(new Gauge("air", "공기", $"산소 {minO2:0.0}kPa" + (maxCO2 >= 0.5 ? $" · 이산화탄소 {maxCO2:0.0}" : ""),
                minO2 < 16 || maxCO2 > 3 ? 2 : minO2 < 18.5 || maxCO2 > 1 ? 1 : 0));
            o.Add(new Gauge("crew", "사람", $"{alive}/{all}" + (down > 0 ? $" · 쓰러짐 {down}" : "") + (hurt > 0 ? $" · 다침 {hurt}" : ""),
                down > 0 ? 2 : hurt > 0 ? 1 : 0));
        }
        if (net != null)
            o.Add(new Gauge("power", "전력", $"{net.Supplied:0}/{net.Demanded:0}kW", net.ReactorScram ? 2 : net.Supplied < net.Demanded * 0.97 ? 1 : 0));
        if (f != null)
        {
            int holes = 0, vac = 0;
            for (int c = 0; c < L.N; c++) if (f.Hole[c] > 0) holes++;
            foreach (var r in L.Rooms) if (r.Cells.Length > 0 && f.Pressure(r.Cells[0]) < 30) vac++;
            o.Add(new Gauge("hull", "선체", holes == 0 && vac == 0 ? "멀쩡하다" : $"구멍 {holes}" + (vac > 0 ? $" · 빈 방 {vac}" : ""), vac > 0 ? 2 : holes > 0 ? 1 : 0));
        }
        double food = w.Book.DaysLeft((int)Res.Food, w.Tick), water = w.Book.DaysLeft((int)Res.Water, w.Tick);
        static string Days(double d) => double.IsInfinity(d) || d > 999 ? "넉넉" : $"{d:0}일";
        double least = Math.Min(food, water);
        o.Add(new Gauge("supply", "물자", $"식량 {Days(food)} · 물 {Days(water)}", least < 7 ? 2 : least < 20 ? 1 : 0));
        int burning = chem?.BurningCount ?? 0;
        // 세계의 진실과 주컴퓨터의 앎이 다르면 그 차이를 적는다 (함장 결정 — 둘 다 두고 차이를 적는다 · '아는 것으로만 판단한다'가 화면에서 보인다)
        bool smFire = w.Find<ShipMind>()?.Incidents.Any(x => x.Active != 0 && x.Kind == IncidentKind.Fire) ?? false;
        o.Add(new Gauge("fire", "불", burning > 0 ? $"{burning}칸이 탄다{(smFire ? "" : " · 주컴퓨터는 아직 모른다")}" : smFire ? "꺼졌다 · 주컴퓨터는 아직 불로 안다" : "없다", burning > 0 ? 2 : smFire ? 1 : 0));
        return o.ToArray();
    }

    /// <summary>
    /// 사건 띠 (UI U4): 주컴퓨터의 사건 목록에서 방마다 한 줄 — 아직인 방(최근 것부터) · 그다음 30분 안에 모두 끝난 방(끝난 차례로) · 다섯 줄까지.
    /// 한 줄에 그 방의 일들('불 · 독한 기체 · 일산화탄소')과 주컴퓨터가 한 일(봉쇄 · 맡긴 로봇 · 부탁한 사람)을 함께 (예전엔 불 하나가 네 줄 · 끝난 줄이 목록 번호 차례로 뒤섞였다).
    /// 주컴퓨터가 아는 것만 (못 보는 방의 일은 띠에도 없다 — 세계의 진실이 아니라 배가 아는 것).
    /// </summary>
    public FeedItem[] Feed()
    {
        var sm = World.Find<ShipMind>(); var mind = World.Find<Mind>();
        if (sm == null) return Array.Empty<FeedItem>();
        long now = World.Tick;
        static int LevelOf(IncidentKind k) => k switch
        {
            IncidentKind.Fire or IncidentKind.Vacuum or IncidentKind.PersonDown or IncidentKind.Arrest or IncidentKind.Toxic or IncidentKind.CO or IncidentKind.ReactorScram or IncidentKind.Flammable => 2,
            IncidentKind.Leak or IncidentKind.Hot or IncidentKind.BadAir or IncidentKind.ElectricFault or IncidentKind.PowerShort or IncidentKind.PowerLost or IncidentKind.Injured
                or IncidentKind.Strain or IncidentKind.DoorJam or IncidentKind.O2Rich or IncidentKind.Surgery => 1,
            _ => 0,
        };
        static string Hm(long t) { long m = t % Time.TicksPerDay * 60 / Time.TicksPerHour; return $"{m / 60:00}:{m % 60:00}"; }
        string Who(int agent) => mind != null && agent >= 0 && agent < mind.Agents.Count ? mind.NameOf(agent) : "";
        FeedItem Line(List<Incident> xs, bool active, List<Incident>? over = null)
        {
            var top = xs.OrderByDescending(x => LevelOf(x.Kind)).ThenBy(x => x.Since).First();
            var kinds = xs.OrderByDescending(x => LevelOf(x.Kind)).Select(x => ShipMind.KindName(x.Kind)).Distinct();
            var did = new List<string>();
            if (xs.Any(x => x.Sealed != 0)) did.Add("봉쇄");
            var robots = xs.SelectMany(x => new[] { x.RobotA, x.RobotB }).Where(r => r >= 0).Distinct().Select(Who).Where(n => n.Length > 0).ToList();
            if (robots.Count > 0) did.Add(string.Join("·", robots) + " 보냄");
            var asked = xs.Where(x => x.Person >= 0).Select(x => Who(x.Person)).Where(n => n.Length > 0).Distinct().ToList();
            if (asked.Count > 0) did.Add(string.Join("·", asked) + "에게 부탁");
            string room = top.Room >= 0 ? sm.RoomName(top.Room) : "배 전체";
            string ended = over is { Count: > 0 } ? " · 끝: " + string.Join(" · ", over.Select(x => ShipMind.KindName(x.Kind)).Distinct()) : "";   // 같은 방에서 막 끝난 일
            string text = $"{room} · {string.Join(" · ", kinds)}{ended}{(active ? "" : " — 끝")}{(active && did.Count > 0 ? " — 주컴퓨터: " + string.Join(" · ", did) : "")}";
            return new FeedItem(Hm(active ? xs.Min(x => x.Since) : xs.Max(x => x.Ended)), text, xs.Max(x => LevelOf(x.Kind)), top.Cell, active);
        }
        var o = new List<FeedItem>(5);
        var live = sm.Incidents.Where(x => x.Active != 0).GroupBy(x => x.Room).OrderByDescending(g => g.Max(x => x.Since));
        foreach (var g in live) { if (o.Count >= 5) break; o.Add(Line(g.ToList(), true, sm.Incidents.Where(x => x.Active == 0 && x.Room == g.Key && now - x.Ended < Time.Minutes(30)).ToList())); }
        var liveRooms = live.Select(g => g.Key).ToHashSet();
        var done = sm.Incidents.Where(x => x.Active == 0 && now - x.Ended < Time.Minutes(30) && !liveRooms.Contains(x.Room)).GroupBy(x => x.Room).OrderByDescending(g => g.Max(x => x.Ended));
        foreach (var g in done) { if (o.Count >= 5) break; o.Add(Line(g.ToList(), false)); }
        return o.ToArray();
    }

    public const int FacesPerRow = 16;
    private Img? _faces;
    private int _facesVersion;
    private long[] _faceKeys = Array.Empty<long>();

    /// <summary>
    /// 사람 줄 (UI U2): 사람마다 얼굴(지금 표정 · 다친 자국)과 하는 일 · 단계 — 쓰러진 이 · 위험한 이가 앞에 (그다음 다친 이 · 나머지는 차례대로).
    /// 얼굴 그림은 바뀐 사람만 다시 찍고 판 번호를 올린다 (화면이 바뀔 때만 텍스처를 다시 올린다).
    /// </summary>
    public CrewFace[] CrewBar()
    {
        var w = World; var mind = w.Find<Mind>(); var life = w.Find<Life>();
        if (mind == null || life == null) return Array.Empty<CrewFace>();
        var people = mind.Agents.Where(a => a.IsPerson).ToList();
        int rows = Math.Max(1, (people.Count + FacesPerRow - 1) / FacesPerRow);
        if (_faces == null || _faces.H != rows * Painter.FaceH) { _faces = new Img(FacesPerRow * Painter.FaceW, rows * Painter.FaceH); _faceKeys = Enumerable.Repeat(-1L, people.Count).ToArray(); }
        if (_faceKeys.Length != people.Count) _faceKeys = Enumerable.Repeat(-1L, people.Count).ToArray();
        var o = new List<CrewFace>(people.Count);
        bool changed = false;
        for (int i = 0; i < people.Count; i++)
        {
            var a = people[i];
            var b = life.Bodies[a.C.Body];
            int slot = i, sx = slot % FacesPerRow * Painter.FaceW, sy = slot / FacesPerRow * Painter.FaceH;
            // 얼굴: 상태 열쇠가 바뀐 사람만 다시 찍는다 (먼저 칸을 비운다)
            var tmp = _scratchFace ??= new Img(Painter.FaceW, Painter.FaceH);
            Array.Clear(tmp.Px);
            long key = Painter.Portrait(w, a.Id, tmp, 0, 0);
            if (key != _faceKeys[i])
            {
                for (int yy = 0; yy < Painter.FaceH; yy++) Array.Copy(tmp.Px, yy * Painter.FaceW * 4, _faces.Px, ((sy + yy) * _faces.W + sx) * 4, Painter.FaceW * 4);
                _faceKeys[i] = key; changed = true;
            }
            int level = b.Alive == 0 ? 3 : b.Conscious == 0 || b.SpO2 < 0.85f || a.C.Mode is 1 or 2 ? 2
                : b.Bleed > 0 || b.Burn > 0.2f || b.Pain > 0.5f || b.Infect > 0.3f || b.Ill == 2 ? 1 : 0;
            string state = b.Alive == 0 ? "숨졌다" : b.Conscious == 0 ? "쓰러졌다" : b.Asleep != 0 ? "잔다" : Mind.GoalName(a.C.Goal);
            o.Add(new CrewFace(a.Id, mind.NameOf(a.Id), state, level, slot));
        }
        if (changed) { _facesVersion++; _facesShown = new Img(_faces.W, _faces.H); Array.Copy(_faces.Px, _facesShown.Px, _faces.Px.Length); }   // 화면 스레드에는 사본 (바뀔 때만)
        return o.OrderByDescending(f => f.Level == 3 ? -1 : f.Level).ThenBy(f => f.Id).ToArray();
    }
    private Img? _scratchFace, _facesShown;

    /// <summary>사람 줄에서 누른 사람을 고른다 (그를 따라간다).</summary>
    public void SelectAgent(int id) { Selected = id; FollowSelected = id >= 0; Overview = false; }

    /// <summary>아래 줄: 지금 장면의 원인 사슬 (가장 최근 하이라이트가 아직 이어지면) — 아니면 연출 자막.</summary>
    /// <summary>
    /// 아래 줄 (docs/REVIEW.md ② — 함장 결정): 지금 보는 장면의 사슬을 마지막 마디부터 (10분 안의 일) · 아니면 막 끝난 하이라이트 (10분 안) ·
    /// 조용할 때는 카메라가 보는 방의 일상 ('식당 — 김하늘: 밥 짓기 · 이도윤: 놀이판'). 예전엔 몇 시간 지난 하이라이트 제목이 불이 난 동안에도 떠 있었다.
    /// </summary>
    public string Story()
    {
        var w = World; var d = View;
        if (ShotNow is { } shot) return $"[{shot.Kind}] {shot.Caption}";
        if (d.SceneRoot != 0 && d.Chains.TryGetValue(d.SceneRoot, out var rec) && rec.Beats.Count > 0 && w.Tick - rec.Beats[^1].Tick < Time.Minutes(10) && rec.Beats[^1].Cause != 0)
            return Spread(d.SceneRoot, w.Causes.Story(rec.Beats[^1].Cause));
        if (d.Highlights.Count > 0)
        {
            var h = d.Highlights[^1];
            if (w.Tick - h.Last < Time.Minutes(10) && h.Cause != 0) return Spread(h.Root, w.Causes.Story(h.Cause));
        }
        return Daily();
        string Spread(int root, string story) { string sp = d.SpreadText(root); return sp.Length > 0 ? $"[{sp}] {story}" : story; }   // 사슬이 건넌 갈래 (우주 → 선체 → 사람 …)
    }

    /// <summary>조용할 때 아래 줄: 카메라 가운데 방의 사람들이 하는 일 (셋까지) — 방에 아무도 없으면 방 이름만.</summary>
    private string Daily()
    {
        var w = World; var mind = w.Find<Mind>(); var life = w.Find<Life>();
        var L = w.Layout;
        int cx = (int)Math.Clamp(CamX, 0, L.W - 1), cy = (int)Math.Clamp(CamY, 0, L.H - 1);
        int room = L.Room[L.Index(cx, cy)];
        if (room < 0 || mind == null || life == null) return "";
        var who = mind.Agents.Where(a => (!a.IsPerson || a.C.Body >= 0 && a.C.Body < life.Bodies.Count && life.Bodies[a.C.Body].Alive != 0) && mind.RoomOf(a.C.Cell) == room)
            .OrderBy(a => a.IsPerson ? 0 : 1).Take(3).Select(a => $"{mind.NameOf(a.Id)}: {mind.Thought(a.Id)}").ToList();
        return who.Count == 0 ? L.Rooms[room].Name : $"{L.Rooms[room].Name} — {string.Join(" · ", who)}";
    }

    /// <summary>다시보기: 하이라이트(없으면 가장 최근 것)를 첫 조짐 직전 찍기부터 다시 보여 준다 — 산 세계는 그동안 멈춘다. 고리에 없으면 false.</summary>
    public bool StartReplay(Highlight? h = null)
    {
        h ??= Dir.Highlights.Count > 0 ? Dir.Highlights[^1] : null;
        if (h == null) return false;
        var w = Dir.Replay(Sim, h);
        if (w == null) return false;
        _replay = w; _replayOf = h;
        _replayDir = new Director(w.Layout) { KeepRing = false, AheadInBackground = true };
        _shots = Dir.Shots(Sim, h);
        _replayEnd = Math.Min(Sim.Tick, Math.Max(h.Last + Time.Minutes(ReplayAfterMin), _shots.Count > 0 ? _shots[^1].To : 0));
        _carry = 0; ClearTrails(); _prevPose = Array.Empty<(double, double, double)>(); _prevTick = long.MinValue;
        Banner = "다시보기 — " + h.Title; BannerLeft = BannerSec;
        return true;
    }

    /// <summary>다시보기를 끝내고 산 세계로 돌아간다.</summary>
    public void StopReplay()
    {
        if (_replay == null) return;
        _replay = null; _replayDir = null; _replayOf = null; _shots = new();
        _carry = 0; ClearTrails(); _prevPose = Array.Empty<(double, double, double)>(); _prevTick = long.MinValue;
    }

    private void ClearTrails() { Array.Clear(_trailLen); }
}
