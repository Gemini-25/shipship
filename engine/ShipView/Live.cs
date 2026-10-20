using System.Collections.Concurrent;
using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>관전 화면 한 장: 그림 + 그 안에서 화면에 보일 곳(픽셀 — 카메라가 칸 사이에 있어도 매끄럽게) + 글.</summary>
public sealed class LiveFrame
{
    public Img Img = null!;
    public float SrcX, SrcY, SrcW, SrcH;
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
}

/// <summary>판 한 줄: 굵은 첫 줄 · 옅은 둘째 줄(여러 줄일 수 있다) · 켜짐(지금 고른 성격 · 다시보기 됨 등).</summary>
public readonly record struct PanelRow(string Main, string Sub, bool On);

/// <summary>관전 화면의 판.</summary>
public enum PanelKind { None, Highlights, Route, Journal, Chain, People }

/// <summary>
/// 관전 화면의 속 (docs/ENGINE.md §새 화면 2단계) — 세계를 돌리고, 연출이 정한 빠르기 · 볼 곳을 따르고, 그린다.
/// Godot 장면은 이것을 작업 스레드에서 부르고 나온 그림을 텍스처로 올리기만 한다 (메인 스레드는 세계를 만지지 않는다 — 60프레임).
/// 실험실(--live)도 같은 것을 돌려 PNG로 확인한다. 세계는 읽기만 한다 (연출 · 그리기 — 결정론 그대로).
/// </summary>
public sealed class LiveShow
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
    public double SpeedOverride;          // 0: 연출이 정한 빠르기 · 그 밖: 화면 1초에 틱
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
        Dir = new Director(sim.Layout) { AheadInBackground = true };   // 미리 보기의 앞선 세계는 다른 코어에서 (결과는 같다)
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
            return View.Speed;
        }
    }
    public (int x0, int y0, int x1, int y1) Bounds => _bbox;

    /// <summary>화면 없이 앞으로 감기 (연출은 함께 본다 — 하이라이트 · 다시보기 고리가 쌓인다). 확인 · 화면 사진용.</summary>
    public void FastForward(long ticks)
    {
        for (long i = 0; i < ticks; i++) { Sim.Step(); Dir.Observe(Sim); }
        WatchComputer(0);
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
            d.Foresee = Speed <= Director.CalmSpeed;   // 함장이 빨리 감는 동안은 미리 보지 않는다 (복제가 1초에 수십 번 — 볼 틈도 없다)
            for (int i = 0; i < n; i++)
            {
                if (i == n - 1) Remember();
                AL("자세 · 걸음");
                w.Step(); AL("세계");
                d.Observe(w); AL("연출");
                if (_replay != null && w.Tick >= _replayEnd) break;
            }
            if (n > 0) Trace();
            AL("자세 · 걸음");
            if (_replay != null && _replay.Tick >= _replayEnd) StopReplay();
        }
        _anim += realSec;
        if (Dir.Highlights.Count > _lastHighlights)
        {
            _lastHighlights = Dir.Highlights.Count;
            Banner = Dir.Highlights[^1].Title; BannerLeft = BannerSec;
        }
        BannerLeft = Math.Max(0, BannerLeft - realSec);
        TickNotice(realSec);
        WatchComputer(realSec);
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
    // ④ 칸 강체: 지난 틱 끝의 몸마다 배 틀 무게중심 자리 · 자세 (틱 사이를 메운다 — 걸음처럼 한 틱 늦게) · 배의 자세
    private (double qx, double qy, double th)[] _prevPose = Array.Empty<(double, double, double)>();
    private double _prevSky;
    private long _prevTick = long.MinValue;

    private void Remember()
    {
        var st = World.Find<Structure>();
        if (st != null)
        {
            _prevPose = new (double, double, double)[st.Bodies.Count];
            for (int b = 0; b < _prevPose.Length; b++) _prevPose[b] = PoseNow(st, b);
            _prevSky = st.Bodies.Count > 0 ? st.Bodies[0].Th : 0;
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

    /// <summary>몸 b의 지금 배 틀 무게중심 자리 · 배에 대한 자세.</summary>
    private static (double qx, double qy, double th) PoseNow(Structure st, int b)
    {
        if (b <= 0) return (0, 0, 0);
        var o = st.Bodies[b];
        var (cs, sn, tx, ty) = st.ToShipFrame(b);
        return (cs * o.Lx - sn * o.Ly + tx, sn * o.Lx + cs * o.Ly + ty, Math.Atan2(sn, cs));
    }

    /// <summary>몸 b를 그릴 배 틀 자세: 지난 틱 끝 → 지금을 틱 사이 비율만큼 (무게중심 자리 · 자세를 따로 — 도는 조각이 흔들리지 않게). 이번 틱에 떨어져 나간 조각은 붙어 있던 자리에서.</summary>
    private (double cs, double sn, double tx, double ty) PoseDrawn(Structure st, int b)
    {
        if (b <= 0 || b >= st.Bodies.Count) return (1, 0, 0, 0);
        var o = st.Bodies[b];
        var now = PoseNow(st, b);
        bool knew = World.Tick == _prevTick + 1 && _prevPose.Length > 0;   // 바로 지난 틱을 안다 (앞으로 감기 · 다시보기로 건너뛰었으면 지금 그대로)
        (double qx, double qy, double th) was = !knew ? now : b < _prevPose.Length ? _prevPose[b] : (o.Lx, o.Ly, 0.0);
        double f = Math.Clamp(_carry, 0, 1);
        double dth = Math.IEEERemainder(now.th - was.th, 2 * Math.PI);
        double th = was.th + dth * f, qx = was.qx + (now.qx - was.qx) * f, qy = was.qy + (now.qy - was.qy) * f;
        double cs = Math.Cos(th), sn = Math.Sin(th);
        return (cs, sn, qx - (cs * o.Lx - sn * o.Ly), qy - (sn * o.Lx + cs * o.Ly));
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
        var mind = World.Find<Mind>();
        int best = -1; double bd = 0.9 * 0.9;
        if (mind != null)
            foreach (var a in mind.Agents)
            {
                var (ax, ay) = DrawnAt(a.Id);
                double d = (ax - x) * (ax - x) + (ay - y - 0.15) * (ay - y - 0.15); // 그림은 칸 위쪽으로 조금 솟는다
                if (d < bd) { bd = d; best = a.Id; }
            }
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
            lines.Add("생각: " + (b.Conscious == 0 ? "(의식이 없다)" : b.Asleep != 0 ? "(자는 중)" : mind.Thought(a.Id)));
            string pondered = mind.RecentThought(a.Id, w.Tick, Time.Hours(1));   // 판단 엔진: 막혀서 한 궁리 (한 시간 안)
            if (pondered.Length > 0) lines.Add("궁리: " + (pondered.StartsWith("생각: ") ? pondered[4..] : pondered));
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
            lines.Add("하는 일: " + mind.Thought(a.Id));
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

    /// <summary>손으로 카메라 옮기기 (따라가기를 끈다).</summary>
    public void Pan(double dx, double dy) { Follow = false; Overview = false; FollowSelected = false; var c = Camera(); CamX = c.left + c.w / 2 + dx; CamY = c.top + c.h / 2 + dy; }
    public void ZoomBy(double f) { if (!FollowSelected) Follow = false; Overview = false; ViewW = Math.Clamp(ViewW * f, MinView, MaxView); }

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
    public LiveFrame Render()
    {
        var (left, top, w, h) = Camera();
        int x0 = (int)Math.Floor(left), y0 = (int)Math.Floor(top);
        int wc = (int)Math.Ceiling(w) + 1, hc = (int)Math.Ceiling(h) + 1; // 줌이 바뀔 때만 크기가 바뀐다 (텍스처를 덜 새로 만든다)
        // 그림판은 넉넉히 (그리는 칸은 그대로) — 확대가 부드럽게 바뀌는 동안 장마다 새 그림판(몇 MB) · 새 텍스처를 만들지 않게:
        // 남은 그림판이 담을 수 있고 넓이가 1.5배 안이면 그대로 쓰고 (가장 작은 것) · 새로 잡을 때는 가로세로 1/8과 한 칸씩 더
        int needW = wc * Painter.T, needH = hc * Painter.T;
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
        if (reuse == null) { reuse = new Img((wc + wc / 8 + 1) * Painter.T, (hc + hc / 8 + 1) * Painter.T); NewCanvas++; } // 넓이 1.35배쯤 — 다음 장에 그대로 쓰인다
        Painter.Show = Show; Painter.ChainCells = Show == Overlay.Chain ? ChainCellsNow() : null;
        // ④ 칸 강체: 틱 사이를 메운 몸의 자세 · 배의 자세(별)
        var stb = World.Find<Structure>();
        if (stb != null && stb.Bodies.Count > 1)
        {
            var poses = new (double, double, double, double)[stb.Bodies.Count];
            for (int b = 0; b < poses.Length; b++) poses[b] = PoseDrawn(stb, b);
            Painter.Poses = poses;
            double th0 = stb.Bodies[0].Th;
            Painter.SkyTh = _prevPose.Length > 0 && World.Tick == _prevTick + 1 ? _prevSky + Math.IEEERemainder(th0 - _prevSky, 2 * Math.PI) * Math.Clamp(_carry, 0, 1) : th0;
        }
        else { Painter.Poses = null; Painter.SkyTh = double.NaN; }
        Painter.Sub = Math.Clamp(_carry, 0, 1);
        var img = Painter.Frame(World, x0, y0, wc, hc, (int)(_anim * AnimFps), reuse);
        (double x, double y) sel = Selected >= 0 ? DrawnAt(Selected) : (double.NaN, 0.0);
        var (shx, shy) = Shake();
        float srcX = Math.Clamp((float)((left - x0) * Painter.T) + shx, 0, Math.Max(0, needW - (float)(w * Painter.T)));   // 그린 칸 안에서만 (그림판의 나머지는 지난 장)
        float srcY = Math.Clamp((float)((top - y0) * Painter.T) + shy, 0, Math.Max(0, needH - (float)(h * Painter.T)));
        return new LiveFrame
        {
            Img = img,
            SrcX = srcX, SrcY = srcY,
            SrcW = (float)(w * Painter.T), SrcH = (float)(h * Painter.T),
            Ship = ShipName, Status = Status(), Story = Story(), Mood = View.Mood, Cut = Cut,
            Banner = BannerLeft > 0 ? Banner : "", BannerAlpha = (float)Math.Clamp(Math.Min(BannerLeft, BannerSec - BannerLeft) / 0.5, 0, 1),
            Tick = World.Tick, Replay = _replayOf == null ? "" : ShotNow is { } sn ? $"{_replayOf.Title} · 컷 {_shots.IndexOf(sn) + 1}/{_shots.Count} {sn.Kind}" : _replayOf.Title,
            CamLeft = left, CamTop = top, CamW = w, CamH = h,
            Card = Card(), SelX = sel.x, SelY = sel.y,
            Notice = Notice, Route = RouteLine(),
            Broadcast = _castLeft > 0 ? _cast : "", BroadcastAlpha = (float)Math.Clamp(Math.Min(_castLeft, CastSec - _castLeft) / 0.5, 0, 1),
            Captions = Ears.Captions ? Ears.CaptionsNow() : Array.Empty<ShipView.Audio.SoundCaption>(),
            PanelTitle = Panel == PanelKind.None ? "" : PanelTitleOf(Panel), PanelRows = Panel == PanelKind.None ? Array.Empty<PanelRow>() : PanelRows(), PanelPick = Panel == PanelKind.None ? -1 : PanelPick,
            PanelFoot = Panel == PanelKind.None ? "" : PanelFootOf(),
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
    private string _cast = ""; private double _castLeft; private int _journals = -1; private long _castTick = long.MinValue;
    public const double CastSec = 12;

    /// <summary>주컴퓨터가 방송하면 (아침 방송 · 항해 달력 — 소리 단계 S6) 자막으로 몇 초 · 일지를 쓰면 알림 (J로 보기).</summary>
    private void WatchComputer(double realSec)
    {
        _castLeft = Math.Max(0, _castLeft - realSec);
        var sm = Sim.Find<ShipMind>();
        if (sm == null) return;
        if (_journals < 0) { _journals = sm.Journal.Count; _castTick = sm.Broadcasts.Count > 0 ? sm.Broadcasts[^1].tick : long.MinValue; }
        if (sm.Broadcasts.Count > 0 && sm.Broadcasts[^1].tick != _castTick) { _castTick = sm.Broadcasts[^1].tick; _cast = "주컴퓨터 방송 — " + sm.Broadcasts[^1].text; _castLeft = CastSec; }
        if (sm.Journal.Count != _journals) { _journals = sm.Journal.Count; if (_journals > 0) Notify("주컴퓨터가 일지를 썼다 — J로 보기", 5); }
    }

    // ── 판: 하이라이트 목록(L) · 항로(T) · 일지(J) ──
    public PanelKind Panel;
    public int PanelPick;

    /// <summary>판을 열거나 (같은 판이면) 닫는다. 열 때 맨 위(가장 새것 · 지금 성격)를 고른다.</summary>
    public void TogglePanel(PanelKind k)
    {
        if (Panel == k) { Panel = PanelKind.None; return; }
        Panel = k;
        var o = Sim.Find<Outside>();
        PanelPick = k == PanelKind.Route && o != null ? Math.Max(0, o.Route - 1) : 0;
    }

    /// <summary>↑ ↓: 판에서 고르기.</summary>
    public void PanelMove(int d)
    {
        int n = Panel switch { PanelKind.Highlights => Dir.Highlights.Count, PanelKind.Route => Sim.Tables.Space.Routes.Count, PanelKind.None => 0, _ => PanelRows().Length };
        if (n == 0) { PanelPick = 0; return; }
        PanelPick = Math.Clamp(PanelPick + d, 0, n - 1);
    }

    /// <summary>← →: (판마다 — 지금은 쓰는 판이 없다).</summary>
    public void PanelSide(int d) { }

    /// <summary>Enter: 하이라이트는 다시보기 · 항로는 출항 전이면 고른 항로로. 한 일이 있으면 true.</summary>
    public bool PanelEnter()
    {
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
        Follow = false; Overview = false; FollowSelected = false;
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
        PanelKind.Highlights => "↑ ↓ 고르기 · Enter 다시보기 · L 닫기",
        PanelKind.Route => "↑ ↓ 항로 · Enter 고르기 (출항 전에만) · T 닫기",
        PanelKind.Journal => "↑ ↓ 고르기 · J 닫기",
        PanelKind.Chain => "↑ ↓ 고르기 · Enter 그 자리 보기 · O 사슬 선 · C 닫기",
        PanelKind.People => "↑ ↓ 고르기 · Enter 따라가기 · P 닫기",
        _ => "",
    };

    /// <summary>열린 판의 줄들.</summary>
    public PanelRow[] PanelRows()
    {
        var rows = new List<PanelRow>();
        switch (Panel)
        {
            case PanelKind.Highlights:
                foreach (var h in HighlightsNewest())
                {
                    string story = h.Cause != 0 ? Sim.Causes.Story(h.Cause) : "";
                    string said = h.Line.Length > 0 ? h.Line : story.Length > 0 && story != h.Title ? story : "";
                    string when = CanReplay(h) ? "Enter로 다시보기" : "한 시간 넘게 지나 다시 볼 수 없다";
                    rows.Add(new PanelRow($"{HourMin(h.Tick)}  {(h.People ? "사람 이야기 · " : "")}{h.Title}", said.Length > 0 ? said + "\n" + when : when, CanReplay(h)));
                }
                if (rows.Count == 0) rows.Add(new PanelRow("아직 볼 만한 장면이 없다", "조용한 항해 — 일이 나면 여기에 쌓인다", false));
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
                    rows.Add(new PanelRow($"{i + 1}  {r.Name}", $"{r.Line}\n파편 ×{r.Zones:0.##} · 깊은 우주 ×{r.Deep:0.##} · 지나는 배 ×{r.Traffic:0.##}{(r.Incline > 0 ? $" · 면 {r.Incline:0.#}° (출발 +{dv:0}m/s)" : "")}", o.Route == i + 1));
                }
                var zone = o.ZoneAt(o.Distance); var ahead = o.ZoneAhead(Sim.Tick, 24);
                var sense = Sim.Find<Sensors>();
                rows.Add(new PanelRow(o.VoyageTicks > 0 ? $"항해 {(Sim.Tick - o.VoyageStart) / (double)Time.TicksPerDay:0}일째 / {o.VoyageTicks / (double)Time.TicksPerDay:0}일 · {o.Distance:0.000}AU" : "출항 전 — 항로를 고를 수 있다",
                    $"{(zone != null ? $"지금 {zone.Name}" : "깊은 우주")}{(ahead != null ? $" · 하루 안에 {ahead.Name}" : "")} · 고향까지 빛 {o.HomeLightMinutes(Sim.Tick):0}분 · 센서에 잡힌 것 {sense?.Tracks.Count ?? 0}", false));
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
                        (who.Count > 0 ? string.Join(" · ", who) : "아직 손을 쓰지 못했다") + (chain.Length > 0 ? "\n궁리: " + chain : "") + (plan.Length > 0 ? "\n계획: " + plan : ""), true));
                }
                string blind = sm.BlindLine();
                rows.Add(new PanelRow("배 형편", sm.ShipLine(Sim, true) + (blind.Length > 0 ? "\n" + blind : "") + (sm.HelmLine.Length > 0 ? "\n항로: " + sm.HelmLine : ""), false));
                var manual = Sim.Find<Society>()?.Manual.Where(e => e.Removed == 0).ToList();   // 판단 엔진 (바): 회의가 넣은 매뉴얼
                if (manual != null && manual.Count > 0)
                    rows.Add(new PanelRow($"매뉴얼 ({manual.Count})", string.Join("\n", manual.Select(e => $"{e.Name} — 찬성 {e.Yes}/{e.Of} · 통한 번 {e.Wins}{(e.Fails > 0 ? $" · 틀어진 번 {e.Fails}" : "")}")), false));
                int head = rows.Count;
                for (int i = sm.Journal.Count - 1; i >= 0; i--)
                {
                    var (t, text) = sm.Journal[i];
                    var lines = text.Split('\n');
                    int row = head + sm.Journal.Count - 1 - i;
                    rows.Add(new PanelRow($"{HourMin(t)}  {lines[0]}", row == PanelPick ? string.Join("\n", lines.Skip(1)) : "", false));
                }
                if (rows.Count == head) rows.Add(new PanelRow("아직 일지가 없다", $"주컴퓨터는 {sm.JournalHour}시에 하루를 적는다", false));
                if (sm.Morning.Length > 0) rows.Add(new PanelRow("아침 방송", sm.Morning, false));
                break;
            }
            case PanelKind.Chain:
            {
                var chain = ChainNow();
                if (chain.Count == 0) { rows.Add(new PanelRow("지금 이어지는 사슬이 없다", "일이 나면 그 일이 어디서 시작됐는지 여기에 차례로 적힌다", false)); break; }
                var w = World; var d = View;
                var root = d.Chains.GetValueOrDefault(chain[0]);
                string spread = d.SpreadText(chain[0]);
                rows.Add(new PanelRow(spread.Length > 0 ? $"건넌 갈래: {spread}" : "한 갈래 안의 일",
                    root != null ? $"이 뿌리에서 난 큰 일 {root.Facts} · 가지 {root.Leaves.Count} · {HourMin(root.First)}부터" : "", false));
                var cm = w.Find<ShipMind>();
                foreach (int c in chain)
                {
                    var n = w.Causes[c];
                    string kind = Director.PlainName[Director.EngineOf(n.Kind)];
                    string where = n.Cell >= 0 && w.Layout.Room[n.Cell] >= 0 ? (cm?.RoomName(w.Layout.Room[n.Cell]) ?? w.Layout.Rooms[w.Layout.Room[n.Cell]].Name) : n.Cell >= 0 ? "통로 · 벽" : "배 바깥 · 배 전체";
                    rows.Add(new PanelRow((kind.Length > 0 ? $"[{kind}] " : "") + w.Causes.Text(c), $"{HourMin(n.Tick)} · {where}", n.Cell >= 0));
                }
                break;
            }
            case PanelKind.People:
            {
                var w = World; var mind = w.Find<Mind>(); var life = w.Find<Life>(); var so = w.Find<Society>();
                if (mind == null || life == null) break;
                if (so != null && so.CultureLine(w) is string culture) rows.Add(new PanelRow("이 배의 관습", culture, false));   // ⑨ 함께 먹는 때 · 금기 · 배의 규칙
                foreach (int id in PeopleOrder())
                {
                    var a = mind.Agents[id]; var b = life.Bodies[a.C.Body];
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
                    rows.Add(new PanelRow($"{mind.NameOf(id)}{(so?.NickOf(id) is string nk ? $" '{nk}'" : "")} — {state}",
                        $"몸: {string.Join(" · ", body)}{(heart.Count > 0 ? " / 마음: " + string.Join(" · ", heart) : "")}{(near.Count > 0 ? "\n" + string.Join(" · ", near) : "")}{(pondered.Length > 0 ? "\n궁리: " + (pondered.StartsWith("생각: ") ? pondered[4..] : pondered) : "")}", danger));
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
    public void Notify(string text, double seconds = 3) { _notice = text; _noticeLeft = seconds; }
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
        if (double.IsNaN(o.PendingTo) || o.VoyageTicks > 0) { Notify("이미 떠났다 — 항로는 출항 때만 고른다"); return false; }
        o.Route = route;
        var due = o.Plan.FirstOrDefault(p => p.Kind == BurnKind.Depart);
        double minutes = due.Kind == BurnKind.Depart ? Math.Max(1, (due.Due - Sim.Tick) / (double)Time.TicksPerHour * 60) : 20;
        int c = Sim.Causes.Add(CauseKind.Route, $"함장이 항로를 골랐다 — {route} '{o.Rt!.Name}'", Sim.Tick);
        o.PlanVoyage(Sim, o.PendingTo, o.PendingName, minutes, c);
        Notify(RouteLine() + " — " + o.Rt.Line);
        return true;
    }

    /// <summary>위 줄: 배 · 시각 · 분위기 · 빠르기 · 사람.</summary>
    public string Status()
    {
        var w = World;
        var life = w.Find<Life>(); var mind = w.Find<Mind>();
        int alive = 0, all = 0;
        if (mind != null && life != null)
            foreach (var a in mind.Agents) if (a.IsPerson) { all++; if (life.Bodies[a.C.Body].Alive != 0) alive++; }
        string speed = Paused ? "멈춤" : $"×{Speed * Time.SecondsPerTick:0}";
        return $"{(_replay != null ? "다시보기 · " : "")}{ShipName} · {Time.Clock(w.Tick)} · {View.Mood} · {speed} · 사람 {alive}/{all}";
    }

    /// <summary>아래 줄: 지금 장면의 원인 사슬 (가장 최근 하이라이트가 아직 이어지면) — 아니면 연출 자막.</summary>
    public string Story()
    {
        var w = World; var d = View;
        if (ShotNow is { } shot) return $"[{shot.Kind}] {shot.Caption}";
        if (d.Highlights.Count > 0)
        {
            var h = d.Highlights[^1];
            if (w.Tick - h.Last < Time.Minutes(10) && h.Cause != 0)
            {
                string spread = d.SpreadText(h.Root);   // 사슬이 건넌 갈래 (우주 → 선체 → 사람 …)
                return spread.Length > 0 ? $"[{spread}] {w.Causes.Story(h.Cause)}" : w.Causes.Story(h.Cause);
            }
        }
        return d.Caption;
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
        _carry = 0; ClearTrails(); _prevPose = Array.Empty<(double, double, double)>();
        Banner = "다시보기 — " + h.Title; BannerLeft = BannerSec;
        return true;
    }

    /// <summary>다시보기를 끝내고 산 세계로 돌아간다.</summary>
    public void StopReplay()
    {
        if (_replay == null) return;
        _replay = null; _replayDir = null; _replayOf = null; _shots = new();
        _carry = 0; ClearTrails(); _prevPose = Array.Empty<(double, double, double)>();
    }

    private void ClearTrails() { Array.Clear(_trailLen); }
}
