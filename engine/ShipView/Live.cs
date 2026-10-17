using System.Collections.Concurrent;
using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>관전 화면 한 장: 그림 + 그 안에서 화면에 보일 곳(픽셀 — 카메라가 칸 사이에 있어도 매끄럽게) + 글.</summary>
public sealed class LiveFrame
{
    public Img Img = null!;
    public float SrcX, SrcY, SrcW, SrcH;
    public string Ship = "", Status = "", Story = "", Banner = "", Mood = "";
    public float BannerAlpha;
    public long Tick;
}

/// <summary>
/// 관전 화면의 속 (docs/ENGINE.md §새 화면 2단계) — 세계를 돌리고, 연출이 정한 빠르기 · 볼 곳을 따르고, 그린다.
/// Godot 장면은 이것을 작업 스레드에서 부르고 나온 그림을 텍스처로 올리기만 한다 (메인 스레드는 세계를 만지지 않는다 — 60프레임).
/// 실험실(--live)도 같은 것을 돌려 PNG로 확인한다. 세계는 읽기만 한다 (연출 · 그리기 — 결정론 그대로).
/// </summary>
public sealed class LiveShow
{
    public readonly Sim Sim;
    public readonly Director Dir;
    public readonly Painter Painter;
    public readonly string ShipName;

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
        Dir = new Director(sim.Layout) { KeepRing = false };
        Painter = new Painter(sim.Layout) { Nudge = Nudge };
        var L = sim.Layout;
        int x0 = L.W, y0 = L.H, x1 = 0, y1 = 0;
        for (int i = 0; i < L.N; i++)
            if (L.Kind[i] != CellKind.Outside) { x0 = Math.Min(x0, L.X(i)); y0 = Math.Min(y0, L.Y(i)); x1 = Math.Max(x1, L.X(i)); y1 = Math.Max(y1, L.Y(i)); }
        _bbox = (x0 - 2, y0 - 2, x1 + 2, y1 + 2);
        CamX = (_bbox.x0 + _bbox.x1 + 1) / 2.0; CamY = (_bbox.y0 + _bbox.y1 + 1) / 2.0;
    }

    /// <summary>배 하나를 사람 · 로봇과 함께 띄운다 (침대마다 한 사람 · 충전독마다 로봇) · 이야기꾼을 켠다.</summary>
    public static LiveShow Crewed(string ship, ulong seed, StoryStyle style = StoryStyle.Balanced, byte level = 4)
    {
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        var st = s.Get<Storyteller>(); st.Enabled = true; st.Style = style; st.Level = level;
        return new LiveShow(s, lay.Name);
    }

    public double Speed => SpeedOverride > 0 ? SpeedOverride : Dir.Speed;
    public (int x0, int y0, int x1, int y1) Bounds => _bbox;

    /// <summary>화면 시간 realSec만큼 나아간다 — 돌린 틱 수.</summary>
    public int Advance(double realSec)
    {
        int n = 0;
        if (!Paused)
        {
            _carry += Speed * realSec;
            n = (int)Math.Min(_carry, MaxTicksPerCall);
            _carry -= n;
            if (_carry >= 1) _carry -= Math.Floor(_carry);   // 밀린 것은 버린다 (다음 장에 몰아 돌리지 않는다)
            for (int i = 0; i < n; i++)
            {
                if (i == n - 1) Remember();
                Sim.Step(); Dir.Observe(Sim);
            }
            if (n > 0) Trace();
        }
        _anim += realSec;
        if (Dir.Highlights.Count > _lastHighlights)
        {
            _lastHighlights = Dir.Highlights.Count;
            Banner = Dir.Highlights[^1].Title; BannerLeft = BannerSec;
        }
        BannerLeft = Math.Max(0, BannerLeft - realSec);
        // 카메라: 연출이 고른 곳으로 부드럽게 (전체 보기면 배 가운데)
        var L = Sim.Layout;
        double tx = CamX, ty = CamY, tw = ViewW;
        if (Overview) { tx = (_bbox.x0 + _bbox.x1 + 1) / 2.0; ty = (_bbox.y0 + _bbox.y1 + 1) / 2.0; tw = Math.Max(_bbox.x1 - _bbox.x0 + 1, (_bbox.y1 - _bbox.y0 + 1) / Aspect); }
        else if (Follow && Dir.FocusCell >= 0) { tx = L.X(Dir.FocusCell) + 0.5; ty = L.Y(Dir.FocusCell) + 0.5; tw = Math.Clamp(Dir.Zoom, FollowMin, FollowMax); }
        double k = 1 - Math.Exp(-realSec * 2.5);
        CamX += (tx - CamX) * k; CamY += (ty - CamY) * k; ViewW += (tw - ViewW) * k;
        return n;
    }

    /// <summary>마지막 틱 앞: 지금 칸과 앞으로 걸을 길(몇 칸)을 적어 둔다.</summary>
    private void Remember()
    {
        var mind = Sim.Find<Mind>();
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
        var mind = Sim.Find<Mind>();
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

    /// <summary>손으로 카메라 옮기기 (따라가기를 끈다).</summary>
    public void Pan(double dx, double dy) { Follow = false; Overview = false; var c = Camera(); CamX = c.left + c.w / 2 + dx; CamY = c.top + c.h / 2 + dy; }
    public void ZoomBy(double f) { Follow = false; Overview = false; ViewW = Math.Clamp(ViewW * f, MinView, MaxView); }

    /// <summary>지금 화면이 보는 곳 (칸 · 소수): 배 둘레 안으로 맞춘다 (배보다 넓으면 가운데).</summary>
    public (double left, double top, double w, double h) Camera()
    {
        double w = ViewW, h = ViewW * Aspect;
        double bw = _bbox.x1 - _bbox.x0 + 1, bh = _bbox.y1 - _bbox.y0 + 1;
        double left = CamX - w / 2, top = CamY - h / 2;
        left = w >= bw ? _bbox.x0 - (w - bw) / 2 : Math.Clamp(left, _bbox.x0, _bbox.x1 + 1 - w);
        top = h >= bh ? _bbox.y0 - (h - bh) / 2 : Math.Clamp(top, _bbox.y0, _bbox.y1 + 1 - h);
        return (left, top, w, h);
    }

    /// <summary>지금 화면을 그린다 (32px 칸 · 보이는 곳을 덮는 칸들 + 그 안의 보일 곳).</summary>
    public LiveFrame Render()
    {
        var (left, top, w, h) = Camera();
        int x0 = (int)Math.Floor(left), y0 = (int)Math.Floor(top);
        int wc = (int)Math.Ceiling(w) + 1, hc = (int)Math.Ceiling(h) + 1; // 줌이 바뀔 때만 크기가 바뀐다 (텍스처를 덜 새로 만든다)
        Img? reuse = null;
        var keep = new List<Img>();
        while (_spare.TryTake(out var sp)) { if (reuse == null && sp.W == wc * Painter.T && sp.H == hc * Painter.T) reuse = sp; else keep.Add(sp); }
        foreach (var k in keep.Take(2)) _spare.Add(k);
        var img = Painter.Frame(Sim, x0, y0, wc, hc, (int)(_anim * AnimFps), reuse);
        return new LiveFrame
        {
            Img = img,
            SrcX = (float)((left - x0) * Painter.T), SrcY = (float)((top - y0) * Painter.T),
            SrcW = (float)(w * Painter.T), SrcH = (float)(h * Painter.T),
            Ship = ShipName, Status = Status(), Story = Story(), Mood = Dir.Mood,
            Banner = BannerLeft > 0 ? Banner : "", BannerAlpha = (float)Math.Clamp(Math.Min(BannerLeft, BannerSec - BannerLeft) / 0.5, 0, 1),
            Tick = Sim.Tick,
        };
    }

    /// <summary>다 쓴 그림을 돌려준다 (화면이 텍스처로 옮긴 뒤 — 다음 장에 다시 쓴다).</summary>
    public void Recycle(Img img) { if (_spare.Count < 3) _spare.Add(img); }

    /// <summary>위 줄: 배 · 시각 · 분위기 · 빠르기 · 사람.</summary>
    public string Status()
    {
        var life = Sim.Find<Life>(); var mind = Sim.Find<Mind>();
        int alive = 0, all = 0;
        if (mind != null && life != null)
            foreach (var a in mind.Agents) if (a.IsPerson) { all++; if (life.Bodies[a.C.Body].Alive != 0) alive++; }
        string speed = Paused ? "멈춤" : $"×{Speed * Time.SecondsPerTick:0}";
        return $"{ShipName} · {Time.Clock(Sim.Tick)} · {Dir.Mood} · {speed} · 사람 {alive}/{all}";
    }

    /// <summary>아래 줄: 지금 장면의 원인 사슬 (가장 최근 하이라이트가 아직 이어지면) — 아니면 연출 자막.</summary>
    public string Story()
    {
        if (Dir.Highlights.Count > 0)
        {
            var h = Dir.Highlights[^1];
            if (Sim.Tick - h.Last < Time.Minutes(10) && h.Cause != 0) return Sim.Causes.Story(h.Cause);
        }
        return Dir.Caption;
    }
}
