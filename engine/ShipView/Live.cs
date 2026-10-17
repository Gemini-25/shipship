using System.Collections.Concurrent;
using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>관전 화면 한 장: 그림 + 그 안에서 화면에 보일 곳(픽셀 — 카메라가 칸 사이에 있어도 매끄럽게) + 글.</summary>
public sealed class LiveFrame
{
    public Img Img = null!;
    public float SrcX, SrcY, SrcW, SrcH;
    public string Ship = "", Status = "", Story = "", Banner = "", Mood = "", Notice = "", Storyteller = "";
    public float BannerAlpha;
    public long Tick;
    public string Replay = "";            // 다시보기 중이면 그 하이라이트 제목
    public double CamLeft, CamTop, CamW, CamH;   // 화면이 보는 곳 (칸 · 소수) — 누른 자리를 칸으로 바꿀 때
    public string[] Card = Array.Empty<string>(); // 고른 사람 · 로봇 카드 (첫 줄: 이름)
    public double SelX = double.NaN, SelY;        // 고른 이가 그려진 자리 (칸 · 가운데)
    public string Broadcast = "";                 // 주컴퓨터 방송 자막 (아침 방송 · 몇 초)
    public float BroadcastAlpha;
    /// <summary>열린 판 (하이라이트 목록 · 이야기꾼 · 일지) — 제목이 비면 닫혔다.</summary>
    public string PanelTitle = "", PanelFoot = "";
    public PanelRow[] PanelRows = Array.Empty<PanelRow>();
    public int PanelPick = -1;
}

/// <summary>판 한 줄: 굵은 첫 줄 · 옅은 둘째 줄(여러 줄일 수 있다) · 켜짐(지금 고른 성격 · 다시보기 됨 등).</summary>
public readonly record struct PanelRow(string Main, string Sub, bool On);

/// <summary>관전 화면의 판.</summary>
public enum PanelKind { None, Highlights, Storyteller, Journal }

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
        Dir = new Director(sim.Layout);
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

    /// <summary>화면 1초에 틱: 함장이 고른 빠르기 · 아니면 연출의 빠르기 (다시보기에선 정점 1분 전까지 조용함 · 조짐을 세 배로 — 이미 본 장면이다).</summary>
    public double Speed
    {
        get
        {
            if (SpeedOverride > 0) return SpeedOverride;
            double v = View.Speed;
            if (_replayOf != null && _replay != null && v >= Director.OmenSpeed && _replay.Tick < _replayOf.Tick - Time.Minutes(1)) v *= ReplayFast;
            return v;
        }
    }
    public const double ReplayFast = 3;
    public (int x0, int y0, int x1, int y1) Bounds => _bbox;

    /// <summary>화면 없이 앞으로 감기 (연출은 함께 본다 — 하이라이트 · 다시보기 고리가 쌓인다). 확인 · 화면 사진용.</summary>
    public void FastForward(long ticks)
    {
        for (long i = 0; i < ticks; i++) { Sim.Step(); Dir.Observe(Sim); }
        WatchComputer(0);
    }

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
            var w = World; var d = View;
            for (int i = 0; i < n; i++)
            {
                if (i == n - 1) Remember();
                w.Step(); d.Observe(w);
                if (_replay != null && w.Tick >= _replayEnd) break;
            }
            if (n > 0) Trace();
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
        // 카메라: 연출이 고른 곳으로 부드럽게 (전체 보기면 배 가운데)
        var L = Sim.Layout;
        var vd = View;
        double tx = CamX, ty = CamY, tw = ViewW;
        if (Overview) { tx = (_bbox.x0 + _bbox.x1 + 1) / 2.0; ty = (_bbox.y0 + _bbox.y1 + 1) / 2.0; tw = Math.Max(_bbox.x1 - _bbox.x0 + 1, (_bbox.y1 - _bbox.y0 + 1) / Aspect); }
        else if (Selected >= 0 && FollowSelected) { var (sx, sy) = DrawnAt(Selected); if (!double.IsNaN(sx)) { tx = sx; ty = sy; tw = Math.Clamp(ViewW, FollowMin, FollowMax); } }
        else if (Follow && vd.FocusCell >= 0) { tx = L.X(vd.FocusCell) + 0.5; ty = L.Y(vd.FocusCell) + 0.5; tw = Math.Clamp(vd.Zoom, FollowMin, FollowMax); }
        double k = 1 - Math.Exp(-realSec * 2.5);
        CamX += (tx - CamX) * k; CamY += (ty - CamY) * k; ViewW += (tw - ViewW) * k;
        Ears.Update(this, realSec);
        return n;
    }

    /// <summary>마지막 틱 앞: 지금 칸과 앞으로 걸을 길(몇 칸)을 적어 둔다.</summary>
    private void Remember()
    {
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

    /// <summary>그 사람 · 로봇이 그려진 자리 (칸 · 가운데 — 걸음 사이 메우기 포함).</summary>
    public (double x, double y) DrawnAt(int agent)
    {
        var mind = World.Find<Mind>();
        if (mind == null || (uint)agent >= (uint)mind.Agents.Count) return (double.NaN, 0);
        var a = mind.Agents[agent];
        var L = Sim.Layout;
        var (nx, ny) = Nudge(a);
        return (L.X(a.C.Cell) + 0.5 + nx / (double)Painter.T, L.Y(a.C.Cell) + 0.5 + ny / (double)Painter.T);
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
        lines.Add(mind.NameOf(a.Id) + role);
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
            lines.Add($"전지 {a.C.Battery * 100:0}% · 손상 {a.C.Damage * 100:0}%");
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
        var img = Painter.Frame(World, x0, y0, wc, hc, (int)(_anim * AnimFps), reuse);
        (double x, double y) sel = Selected >= 0 ? DrawnAt(Selected) : (double.NaN, 0.0);
        return new LiveFrame
        {
            Img = img,
            SrcX = (float)((left - x0) * Painter.T), SrcY = (float)((top - y0) * Painter.T),
            SrcW = (float)(w * Painter.T), SrcH = (float)(h * Painter.T),
            Ship = ShipName, Status = Status(), Story = Story(), Mood = View.Mood,
            Banner = BannerLeft > 0 ? Banner : "", BannerAlpha = (float)Math.Clamp(Math.Min(BannerLeft, BannerSec - BannerLeft) / 0.5, 0, 1),
            Tick = World.Tick, Replay = _replayOf?.Title ?? "",
            CamLeft = left, CamTop = top, CamW = w, CamH = h,
            Card = Card(), SelX = sel.x, SelY = sel.y,
            Notice = Notice, Storyteller = StoryLine(),
            Broadcast = _castLeft > 0 ? _cast : "", BroadcastAlpha = (float)Math.Clamp(Math.Min(_castLeft, CastSec - _castLeft) / 0.5, 0, 1),
            PanelTitle = Panel == PanelKind.None ? "" : PanelTitleOf(Panel), PanelRows = Panel == PanelKind.None ? Array.Empty<PanelRow>() : PanelRows(), PanelPick = Panel == PanelKind.None ? -1 : PanelPick,
            PanelFoot = Panel == PanelKind.None ? "" : PanelFootOf(),
        };
    }

    /// <summary>다 쓴 그림을 돌려준다 (화면이 텍스처로 옮긴 뒤 — 다음 장에 다시 쓴다).</summary>
    public void Recycle(Img img) { if (_spare.Count < 3) _spare.Add(img); }

    // ── 주컴퓨터 방송 · 일지 (⑩) — 산 세계에서만 ──
    private string _cast = ""; private double _castLeft; private int _journals = -1; private string _morning = "";
    public const double CastSec = 12;

    /// <summary>아침 방송이 나오면 자막으로 몇 초 · 일지를 쓰면 알림 (J로 보기).</summary>
    private void WatchComputer(double realSec)
    {
        _castLeft = Math.Max(0, _castLeft - realSec);
        var sm = Sim.Find<ShipMind>();
        if (sm == null) return;
        if (_journals < 0) { _journals = sm.Journal.Count; _morning = sm.Morning; }
        if (!ReferenceEquals(sm.Morning, _morning) && sm.Morning != _morning) { _morning = sm.Morning; _cast = "주컴퓨터 방송 — " + sm.Morning; _castLeft = CastSec; }
        if (sm.Journal.Count != _journals) { _journals = sm.Journal.Count; if (_journals > 0) Notify("주컴퓨터가 일지를 썼다 — J로 보기", 5); }
    }

    // ── 판: 하이라이트 목록(L) · 이야기꾼(T) · 일지(J) ──
    public PanelKind Panel;
    public int PanelPick;

    /// <summary>판을 열거나 (같은 판이면) 닫는다. 열 때 맨 위(가장 새것 · 지금 성격)를 고른다.</summary>
    public void TogglePanel(PanelKind k)
    {
        if (Panel == k) { Panel = PanelKind.None; return; }
        Panel = k;
        var st = Sim.Find<Storyteller>();
        PanelPick = k == PanelKind.Storyteller && st != null ? (int)st.Style : 0;
    }

    /// <summary>↑ ↓: 판에서 고르기.</summary>
    public void PanelMove(int d)
    {
        int n = Panel switch { PanelKind.Highlights => Dir.Highlights.Count, PanelKind.Storyteller => Enum.GetValues<StoryStyle>().Length, PanelKind.Journal => Sim.Find<ShipMind>()?.Journal.Count ?? 0, _ => 0 };
        if (n == 0) { PanelPick = 0; return; }
        PanelPick = Math.Clamp(PanelPick + d, 0, n - 1);
    }

    /// <summary>← →: 이야기꾼 판에서 가혹함.</summary>
    public void PanelSide(int d) { if (Panel == PanelKind.Storyteller) Harsher(d); }

    /// <summary>Enter: 하이라이트는 다시보기 · 이야기꾼은 고른 성격으로. 한 일이 있으면 true.</summary>
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
            case PanelKind.Storyteller:
            {
                var st = Sim.Find<Storyteller>(); if (st == null) return false;
                st.Style = (StoryStyle)Math.Clamp(PanelPick, 0, Enum.GetValues<StoryStyle>().Length - 1);
                Notify("이야기꾼: " + StoryLine());
                return true;
            }
        }
        return false;
    }

    private List<Highlight> HighlightsNewest() { var l = Dir.Highlights.ToList(); l.Reverse(); return l; }

    /// <summary>그 하이라이트를 다시 볼 수 있나 (다시보기 고리에 그 앞 찍기가 있다).</summary>
    public bool CanReplay(Highlight h) => Dir.Ring.Count > 0 && Dir.Ring[0].tick <= h.Tick;

    private static string HourMin(long t) { long m = t % Time.TicksPerDay * 60 / Time.TicksPerHour; return $"{t / Time.TicksPerDay + 1}일 {m / 60:00}:{m % 60:00}"; }

    private string PanelTitleOf(PanelKind k) => k switch
    {
        PanelKind.Highlights => $"볼 만했던 장면 ({Dir.Highlights.Count})",
        PanelKind.Storyteller => "이야기꾼",
        PanelKind.Journal => "주컴퓨터 일지",
        _ => "",
    };

    private string PanelFootOf() => Panel switch
    {
        PanelKind.Highlights => "↑ ↓ 고르기 · Enter 다시보기 · L 닫기",
        PanelKind.Storyteller => "↑ ↓ 성격 · Enter 고르기 · ← → 가혹함 · T 닫기",
        PanelKind.Journal => "↑ ↓ 고르기 · J 닫기",
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
            case PanelKind.Storyteller:
            {
                var st = Sim.Find<Storyteller>();
                if (st == null) break;
                foreach (var style in Enum.GetValues<StoryStyle>())
                    rows.Add(new PanelRow(Storyteller.StyleName(style), Storyteller.StyleLine(style), style == st.Style));
                string bar = new string('■', st.Level) + new string('□', 8 - st.Level);
                string last = st.LastKind == SeedKind.None ? "아직 없음" : $"{Storyteller.SeedName(st.LastKind)} ({HourMin(st.LastSeedTick)})";
                rows.Add(new PanelRow($"가혹함  ◀ {bar} ▶  {st.Level} {Storyteller.LevelName(st.Level)}",
                    $"심은 조짐 {st.Seeds.Count}개 · 마지막 {last} · 한 번 심으면 {st.RestHours:0.#}시간 쉰다\n지금 긴장 {st.Tension + st.Pending:0.0} / 바라는 긴장 {st.Target(Sim.Tick):0.0}", false));
                break;
            }
            case PanelKind.Journal:
            {
                var sm = Sim.Find<ShipMind>();
                if (sm == null) break;
                for (int i = sm.Journal.Count - 1; i >= 0; i--)
                {
                    var (t, text) = sm.Journal[i];
                    var lines = text.Split('\n');
                    int row = sm.Journal.Count - 1 - i;
                    rows.Add(new PanelRow($"{HourMin(t)}  {lines[0]}", row == PanelPick ? string.Join("\n", lines.Skip(1)) : "", false));
                }
                if (rows.Count == 0) rows.Add(new PanelRow("아직 일지가 없다", $"주컴퓨터는 {sm.JournalHour}시에 하루를 적는다", false));
                if (sm.Morning.Length > 0) rows.Add(new PanelRow("아침 방송", sm.Morning, false));
                break;
            }
        }
        return rows.ToArray();
    }

    // ── 함장이 고르는 이야기꾼 (⑫) ──
    private string _notice = ""; private double _noticeLeft;
    /// <summary>잠깐 띄우는 알림 (이야기꾼을 바꿨다 등) — 남은 초가 0이면 빈 글.</summary>
    public string Notice => _noticeLeft > 0 ? _notice : "";
    public void Notify(string text, double seconds = 3) { _notice = text; _noticeLeft = seconds; }
    public void TickNotice(double realSec) => _noticeLeft = Math.Max(0, _noticeLeft - realSec);

    /// <summary>이야기꾼 한 줄: 성격 · 가혹함.</summary>
    public string StoryLine()
    {
        var st = Sim.Find<Storyteller>();
        return st == null ? "" : $"{Storyteller.StyleName(st.Style)} · 가혹함 {st.Level} {Storyteller.LevelName(st.Level)}";
    }

    /// <summary>이야기꾼 성격을 다음 것으로 (고른 → 잔잔한 → 몰아치는 → 느린 불씨 → 우주의 변덕 → 사람의 이야기).</summary>
    public void CycleStyle()
    {
        var st = Sim.Find<Storyteller>(); if (st == null) return;
        st.Style = (StoryStyle)(((int)st.Style + 1) % Enum.GetValues<StoryStyle>().Length);
        Notify("이야기꾼: " + StoryLine());
    }

    /// <summary>가혹함을 한 칸 올리거나 내린다 (1 ~ 8).</summary>
    public void Harsher(int d)
    {
        var st = Sim.Find<Storyteller>(); if (st == null) return;
        st.Level = (byte)Math.Clamp(st.Level + d, 1, 8);
        Notify("이야기꾼: " + StoryLine());
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
        if (d.Highlights.Count > 0)
        {
            var h = d.Highlights[^1];
            if (w.Tick - h.Last < Time.Minutes(10) && h.Cause != 0) return w.Causes.Story(h.Cause);
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
        _replayDir = new Director(w.Layout) { KeepRing = false };
        _replayEnd = Math.Min(Sim.Tick, h.Last + Time.Minutes(ReplayAfterMin));
        _carry = 0; ClearTrails();
        Banner = "다시보기 — " + h.Title; BannerLeft = BannerSec;
        return true;
    }

    /// <summary>다시보기를 끝내고 산 세계로 돌아간다.</summary>
    public void StopReplay()
    {
        if (_replay == null) return;
        _replay = null; _replayDir = null; _replayOf = null;
        _carry = 0; ClearTrails();
    }

    private void ClearTrails() { Array.Clear(_trailLen); }
}
