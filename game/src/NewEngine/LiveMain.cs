using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Godot;
using ShipEngine;
using ShipView;
using ShipView.Audio;
using Thread = System.Threading.Thread;
using Time = ShipEngine.Time;

namespace ShipLive;

/// <summary>
/// 새 엔진 판 — 처음 화면에서 '새 엔진 판'을 고르면 뜬다 (docs/ENGINE.md §새 화면 2단계). 옛 게임의 코어 · 배 · 화면은 하나도 쓰지 않는다:
/// 세계는 engine/ShipEngine · 배는 engine/data/ships (새로 설계한 일곱 척) · 연출과 그리기는 engine/ShipView (도트 그림만 옛 것을 빌린다).
/// 이 장면은 얇은 껍데기 — 작업 스레드: 세계를 돌리고(연출이 정한 빠르기) 그린다 (<see cref="LiveShow"/>). 메인 스레드: 다 그려진 그림을 텍스처로 올리고 글만 쓴다 —
/// 세계가 무거운 순간(주컴퓨터가 앞날을 그려 볼 때)에도 화면은 60프레임으로 돈다.
/// 함장의 손은 명령 줄로 작업 스레드에 넘긴다 (두 스레드가 세계를 함께 만지지 않는다).
/// 마우스만으로 된다 (docs/UI.md — 함장 요청 10-23): 자리 · 누를 자리는 <see cref="HudLayout"/>이 순수 C#으로 셈하고 (실험실이 시험) · 여기는 그리기와 '여기 무엇?'만 묻는다.
/// 키는 하나도 빼지 않았다 — 키로 하는 일마다 마우스 길이 있다 (F3 계기판만 키로).
/// </summary>
public partial class LiveMain : Node2D
{
    // ── 작업 스레드와 나누는 것 ──
    private LiveShow? _show;                                   // 작업 스레드가 만든다 (메인은 다 쓴 그림을 돌려줄 때만)
    private readonly ConcurrentQueue<Action<LiveShow>> _cmds = new();
    private sealed class Pic { public LiveFrame F = null!; public int Gen; }
    private Pic? _ready;                                       // 막 그려진 한 장 (Interlocked)
    private Thread? _worker;
    private volatile bool _running;
    private volatile string? _launchShip;                      // 새 배를 띄워 달라 (작업 스레드가 받는다)
    private ulong _launchSeed;
    private volatile int _gen;                                 // 띄운 배 번호 — 옛 배의 그림이 늦게 와도 버린다
    private double _aspect = 9.0 / 16.0;                       // 화면 세로/가로 (메인이 쓰고 작업 스레드가 읽는다)

    // ── 메인 스레드 ──
    private LiveWorldPic? _pic;
    /// <summary>샤프 확대 (도트 안쪽은 그대로 · 도트 경계 1픽셀만 섞는다 — docs/GRAPHICS.md T1).</summary>
    private const string SharpShader = @"shader_type canvas_item;
void fragment() {
    vec2 tex = 1.0 / TEXTURE_PIXEL_SIZE;
    vec2 texel = UV * tex;
    vec2 scale = max(1.0 / fwidth(texel), vec2(1.0));
    vec2 c = fract(texel) - 0.5;
    vec2 r = 0.5 - 0.5 / scale;
    vec2 f = (c - clamp(c, -r, r)) * scale + 0.5;
    COLOR = texture(TEXTURE, (floor(texel) + f) / tex);
}";

    /// <summary>자식이 부르는 관전 그림 그리기 (메인 스레드).</summary>
    internal void DrawWorld(CanvasItem on)
    {
        if (_tex == null || _shown == null) return;
        var f = _shown;
        on.DrawTextureRectRegion(_tex, new Rect2(Vector2.Zero, GetViewportRect().Size), new Rect2(f.SrcX, f.SrcY, f.SrcW, f.SrcH));
    }

    private Image? _image;
    private ImageTexture? _tex;
    private LiveFrame? _shown;
    private Font _font = null!, _bold = null!;
    private int _thoughts = -1;
    private string[] _ships = Array.Empty<string>(), _shipNames = Array.Empty<string>();
    private int _shipAt;
    private ulong _seed = 18;
    private double _escAt = -99, _nAt = -99, _rAt = -99, _clock;   // 항해를 버리는 키는 3초 안에 두 번 (함장 결정)
    private bool _meter;
    private const double CutDipSec = 0.18;
    private double _cutDip;
    private int _gotFrames, _fpsFrames, _fpsTotal; private double _meterT; private int _picPerSec, _screenPerSec;
    private Vector2 _lastSize;
    // 마우스 (docs/UI.md 1.4): 자리표 · 화면 상태 · 끌기
    private readonly Hud _hud = new();
    private readonly HudState _hs = new();
    private bool _dragging, _midDrag;
    private Vector2 _pressAt; private float _dragDist;
    private bool _pressDouble;
    private Input.CursorShape _cursor = Input.CursorShape.Arrow;
    private readonly Dictionary<(string, int, bool), float> _widthCache = new();
    private readonly Dictionary<(string, int, float, int), float> _heightCache = new();
    // 사람 줄 (UI U2): 얼굴 그림 텍스처 (바뀔 때만 다시 올린다)
    private ImageTexture? _facesTex;
    private int _facesVer = -1;

    // ── 소리: 작업 스레드의 귀가 보낸 것을 메인 스레드의 섞개가 받아 그리고, Godot에는 다 섞인 소리만 밀어 넣는다 ──
    private readonly Mixer _mix = new();
    private AudioStreamGeneratorPlayback? _playback;
    private float[] _audio = new float[4096];
    private Vector2[] _push = Array.Empty<Vector2>();

    // ── 확인용 (명령 줄: --shots=폴더 --every=초 --for=초) — 몇 초마다 화면을 찍고, 끝나면 프레임 시간을 적고 나간다 ──
    private Overlay _overlay;
    private string? _shotDir;
    private double _shotEvery = 10, _runFor, _runT, _nextShot = 3;
    private int _shotN, _picsTotal, _pick = -1;
    private double _view = -1;   // --view=칸 수 (0 = 배 전체) — 관찰 장면용 확대
    private string? _ui;          // --ui=장면[:작은 탭] (docs/UI2.md §7 결함 6 — 화면 사진용: person · robot · robots · computer · hand) — 3초에 차린다
    private string? _shotName;    // --shot=이름: 5초에 한 장 (이름.png) 찍고 몇 초마다 찍기는 하지 않는다
    private PanelKind _openPanel;
    private bool _ignite;                                      // --ignite: 감은 뒤 침대 하나에 불씨 (화면 사진용 — 사건 띠 · 불 연출)
    private double _skipHours;                                 // --skip=시간: 띄우기 전에 그만큼 앞으로 감는다 (화면 사진용)
    private bool _noHold;                                      // 확인용 띄우기는 출항 준비로 붙잡지 않는다 (CI 10초가 세계를 돌리게)
    private bool _helpOnce;                                    // --help: 화면 사진용 — 도움말 창을 연다
    private readonly System.Collections.Generic.List<double> _frameMs = new();
    private double _hudMs, _drawMs;
    private readonly Dictionary<string, double> _laps = new();   // 확인용: 메인 스레드의 자리표 · 그리기 시간 (모은 ms)

    public override void _Ready()
    {
        Warmup.Start();   // 큰 메서드를 처음 부를 때의 멈칫(20~40ms)을 미리 — 낮은 우선순위 스레드
        LoadFonts();
        TextureFilter = TextureFilterEnum.Nearest;
        // 관전 그림은 자식이 그린다 (텍스처 T1 — 샤프 확대): 1.875배 같은 비정수 배율에서도 도트 너비가 고르고 경계 1픽셀만 섞인다 · 글 · 판은 부모가 위에
        _pic = new LiveWorldPic { Owner = this, ShowBehindParent = true, TextureFilter = TextureFilterEnum.Linear, Material = new ShaderMaterial { Shader = new Shader { Code = SharpShader } } };
        AddChild(_pic);
        var player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = Dsp.Rate, BufferLength = 0.2f } };
        AddChild(player);
        player.Play();
        _playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _ships = Layout.ShipKeys().ToArray();
        _shipNames = _ships.Select(k => { try { var l = Layout.Ship(k, Tables.Default); return $"{l.Name} · {l.Furniture.Count(f => f.Type == "Bed")}명"; } catch { return k; } }).ToArray();
        string ship = _ships.Contains("Didim") ? "Didim" : _ships.FirstOrDefault() ?? "Didim";
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--ship=")) ship = a[7..];
            else if (a.StartsWith("--seed=") && ulong.TryParse(a[7..], out var sd)) _seed = sd;
            else if (a.StartsWith("--shots=")) _shotDir = a[8..];
            else if (a.StartsWith("--every=")) _shotEvery = double.Parse(a[8..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--for=")) _runFor = double.Parse(a[6..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--pick=") && int.TryParse(a[7..], out var pk)) _pick = pk;
            else if (a.StartsWith("--view=")) _view = a[7..] == "all" ? 0 : double.TryParse(a[7..], out var vw) ? vw : -1;
            else if (a.StartsWith("--ui=")) _ui = a[5..];
            else if (a.StartsWith("--shot=")) { _shotName = a[7..]; _nextShot = Math.Max(_nextShot, 5); _shotEvery = 1e9; }
            else if (a.StartsWith("--shotat=")) _nextShot = double.Parse(a[9..], System.Globalization.CultureInfo.InvariantCulture);   // --shot= 한 장을 찍는 초 (기본 5)
            else if (a.StartsWith("--skip=")) _skipHours = double.Parse(a[7..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a == "--ignite") _ignite = true;
            else if (a == "--help") _helpOnce = true;
            else if (a == "--hold") _noHold = false;
            else if (a.StartsWith("--panel=")) _openPanel = a[8..] switch { "L" => PanelKind.Highlights, "T" => PanelKind.Route, "J" => PanelKind.Journal, "C" => PanelKind.Chain, "P" => PanelKind.People, "G" => PanelKind.Robots, "X" => PanelKind.Hand, _ => PanelKind.None };
            else if (a.StartsWith("--overlay=") && Enum.TryParse<Overlay>(a[10..], out var ov)) _overlay = ov;
            else if (a.StartsWith("--thoughts=") && int.TryParse(a[11..], out var th)) _thoughts = Math.Clamp(th, 0, 2);
        }
        var args = OS.GetCmdlineUserArgs();
        _noHold = !args.Contains("--hold") && args.Any(a => a.StartsWith("--for") || a.StartsWith("--shots") || a.StartsWith("--skip") || a == "--ignite" || a.StartsWith("--panel") || a.StartsWith("--pick") || a.StartsWith("--ui"));
        if (_shotName != null) _shotDir ??= "docs/screens/ui";
        if (_shotDir != null) System.IO.Directory.CreateDirectory(_shotDir);
        _shipAt = Math.Max(0, Array.IndexOf(_ships, ship));
        Launch(ship, _seed);
        _running = true;
        _worker = new Thread(Work) { IsBackground = true, Name = "세계" };
        _worker.Start();
    }

    public override void _ExitTree()
    {
        _running = false;
        _worker?.Join(2000);
    }

    private void Launch(string ship, ulong seed)
    {
        _launchSeed = seed;
        _gen++;
        _launchShip = ship;
        _shown = null;
        _hs.HelpBlink = 8;
    }

    // ───────────────────────── 작업 스레드 ─────────────────────────

    private void Work()
    {
        var clock = Stopwatch.StartNew();
        double last = 0;
        int gen = 0;
        while (_running)
        {
            var want = _launchShip;
            if (want != null)
            {
                _launchShip = null;
                gen = _gen;
                while (_cmds.TryDequeue(out _)) { }               // 옛 배에 내린 손은 버린다
                _show = LiveShow.Crewed(want, _launchSeed);
                Warmup.Crisis(_show.Sim);   // 위기 길 데우기: 첫 불의 멈칫 (새뜰 6.9ms → 2ms 아래) — 복제로 · 한 번만
                _show.DayMinutes = LiveShow.DefaultDayMinutes;   // 하루 길이 목표 (기본 3분 · 빠르기 단추 '3분')
                if (_skipHours > 0) { _show.FastForward(Time.Hours(_skipHours)); _skipHours = 0; }
                if (_ignite)
                {
                    _ignite = false;
                    var w = _show.Sim;
                    var bed = w.Layout.Furniture.FirstOrDefault(fu => fu.Type == "Bed" && fu.Cells.Length > 0);
                    if (bed != null) { int c = bed.Cells[0]; w.Get<ShipEngine.Chemistry>().Ignite(c, 3e6, w.Causes.Add(CauseKind.Setup, "침대 밑에서 불씨가 튀었다", w.Tick)); _show.LookAt(c); }
                }
                if (!_noHold) _show.HoldForDeparture();          // 출항 준비 (docs/UI.md 4 — 추천 A): 멈춘 채 항로 판 · 항로를 누르거나 [이대로 출항]
                last = clock.Elapsed.TotalSeconds;
            }
            var show = _show;
            if (show == null) { Thread.Sleep(5); continue; }
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.25, now - last);
            last = now;
            while (_cmds.TryDequeue(out var cmd)) cmd(show);
            show.Aspect = _aspect;
            show.Advance(dt);
            var pic = new Pic { F = show.Render(), Gen = gen };
            var old = Interlocked.Exchange(ref _ready, pic);
            if (old != null) show.Recycle(old.F.Img);            // 메인이 미처 못 가져간 장
            double spent = clock.Elapsed.TotalSeconds - now;
            if (spent < 1 / 60.0) Thread.Sleep((int)((1 / 60.0 - spent) * 1000)); // 그림은 많아야 1초에 60장
        }
    }

    // ───────────────────────── 메인 스레드 ─────────────────────────

    public override void _Process(double delta)
    {
        _clock += delta;
        _cutDip = Math.Max(0, _cutDip - delta);
        _hs.HelpBlink = Math.Max(0, _hs.HelpBlink - delta);
        var size = GetViewportRect().Size;
        if (size != _lastSize && size.X > 0) { _lastSize = size; _aspect = size.Y / size.X; }
        // 손으로 카메라 옮기기 (누르고 있는 동안)
        var pan = Vector2.Zero;
        bool arrows = !PanelOpen && _hs.Ask == HudAsk.None;   // 판이 열려 있으면 화살표는 판에서 고르기
        if (_hs.Ask == HudAsk.None && !_hs.Help)
        {
            if (Input.IsKeyPressed(Key.A) || arrows && Input.IsKeyPressed(Key.Left)) pan.X -= 1;
            if (Input.IsKeyPressed(Key.D) || arrows && Input.IsKeyPressed(Key.Right)) pan.X += 1;
            if (Input.IsKeyPressed(Key.W) || arrows && Input.IsKeyPressed(Key.Up)) pan.Y -= 1;
            if (Input.IsKeyPressed(Key.S) || arrows && Input.IsKeyPressed(Key.Down)) pan.Y += 1;
        }
        if (pan != Vector2.Zero && _shown != null)
        {
            double cells = _shown.CamW * 0.8 * delta;
            double dx = pan.X * cells, dy = pan.Y * cells;
            _cmds.Enqueue(s => s.Pan(dx, dy));
        }

        var pic = Interlocked.Exchange(ref _ready, null);
        if (pic != null && pic.Gen != _gen) { _show?.Recycle(pic.F.Img); pic = null; } // 바꾸기 전 배의 그림
        if (pic != null)
        {
            var f = pic.F;
            var img = f.Img;
            if (_image == null || _image.GetWidth() != img.W || _image.GetHeight() != img.H)
            {
                _image = Image.CreateFromData(img.W, img.H, false, Image.Format.Rgba8, img.Px);
                _tex = ImageTexture.CreateFromImage(_image);
            }
            else
            {
                _image.SetData(img.W, img.H, false, Image.Format.Rgba8, img.Px);
                _tex!.Update(_image);
            }
            _show?.Recycle(img);                                  // 텍스처로 옮겼다 — 다음 장에 다시 쓴다
            if (_shown == null || _shown.Ship != f.Ship) DisplayServer.WindowSetTitle($"ShipSim · 새 엔진 판 — {f.Ship}");
            _shown = f;
            if (f.Faces != null && f.FacesVersion != _facesVer)
            {
                var fi = Image.CreateFromData(f.Faces.W, f.Faces.H, false, Image.Format.Rgba8, f.Faces.Px);
                if (_facesTex != null && _facesTex.GetWidth() == fi.GetWidth() && _facesTex.GetHeight() == fi.GetHeight()) _facesTex.Update(fi);
                else _facesTex = ImageTexture.CreateFromImage(fi);
                _facesVer = f.FacesVersion;
            }
            if (f.Cut) _cutDip = CutDipSec;   // 카메라가 끊어 넘긴 장: 짧게 어두워졌다 밝아진다
            _gotFrames++; _picsTotal++;
        }
        long th0 = Stopwatch.GetTimestamp();
        if (_shown != null) { BuildHud(); Hover(); }
        _hudMs += (Stopwatch.GetTimestamp() - th0) * 1000.0 / Stopwatch.Frequency;
        PumpAudio();
        _fpsFrames++; _fpsTotal++;
        _meterT += delta;
        if (_meterT >= 1) { _picPerSec = _gotFrames; _screenPerSec = _fpsFrames; _gotFrames = _fpsFrames = 0; _meterT = 0; }
        QueueRedraw();
        _pic?.QueueRedraw();
        if (_shotDir != null || _runFor > 0) Check(delta);
    }

    /// <summary>자리표를 이 장의 그림 · 창 크기로 다시 셈한다 (목록은 다시 쓴다 · 글 크기는 붙잡아 둔다).</summary>
    private void BuildHud()
    {
        var vp = GetViewportRect().Size;
        HudLayout.Build(_shown!, _hs, vp.X, vp.Y, MeasureW, MeasureH, _hud, _shipNames);
    }

    private float MeasureW(string t, int px, bool bold)
    {
        var key = (t, px, bold);
        if (_widthCache.TryGetValue(key, out var w)) return w;
        if (_widthCache.Count > 6000) _widthCache.Clear();
        return _widthCache[key] = (bold ? _bold : _font).GetStringSize(t, HorizontalAlignment.Left, -1, px).X;
    }

    private readonly Dictionary<(string, int, float, int), float> _heightCacheB = new();
    private float MeasureHB(string t, int px, float width, int maxLines)
    {
        var key = (t, px, MathF.Round(width), maxLines);
        if (_heightCacheB.TryGetValue(key, out var h)) return h;
        if (_heightCacheB.Count > 4000) _heightCacheB.Clear();
        return _heightCacheB[key] = _bold.GetMultilineStringSize(t, HorizontalAlignment.Left, width, px, maxLines).Y;
    }

    private float MeasureH(string t, int px, float width, int maxLines)
    {
        var key = (t, px, MathF.Round(width), maxLines);
        if (_heightCache.TryGetValue(key, out var h)) return h;
        if (_heightCache.Count > 6000) _heightCache.Clear();
        return _heightCache[key] = _font.GetMultilineStringSize(t, HorizontalAlignment.Left, width, px, maxLines).Y;
    }

    /// <summary>마우스 올림: 손 모양 커서 · 세계의 사람 위면 그 사람을 작업 스레드에 알린다 (이름 · 하는 일).</summary>
    private void Hover()
    {
        var mouse = GetViewport().GetMousePosition();
        var r = _hud.At(mouse.X / _hud.U, mouse.Y / _hud.U);
        int agent = -1;
        if (r.Kind == RouteKind.World && !_dragging)
        {
            var (cx, cy) = CellAt(mouse);
            agent = LiveShow.Nearest(_shown!.Spots, cx, cy);
        }
        if (agent != _hs.HoverAgent) { _hs.HoverAgent = agent; int a = agent; _cmds.Enqueue(s => s.SetHover(a)); }
        bool aiming = _shown!.Armed != HandKind.None && r.Kind == RouteKind.World;
        if (aiming && _hs.HandDrag == 0 && _shotName == null)   // 화면 사진 (--shot)은 차린 겨눔 자리를 그대로 둔다
        {
            // 겨누는 중: 올린 칸을 작업 스레드에 (칸이 0.02 넘게 바뀔 때만 — 겨눔 미리 보기 한 점)
            var (hx, hy) = CellAt(mouse);
            if (double.IsNaN(_hs.HoverSentX) || Math.Abs(hx - _hs.HoverSentX) > 0.02 || Math.Abs(hy - _hs.HoverSentY) > 0.02) { _hs.HoverSentX = hx; _hs.HoverSentY = hy; _cmds.Enqueue(s => s.SetHoverAt(hx, hy)); }
        }
        long hk = r.Hit != null ? ((long)r.Hit.Act << 40) ^ r.Hit.Key : long.MinValue;
        if (hk != _hs.HoverKey) { _hs.HoverKey = hk; _hs.HoverSince = _clock; }
        var shape = _hs.HandDrag != 0 || _dragging && _dragDist >= 6 || _midDrag ? Input.CursorShape.Drag
            : aiming ? (_shown.Aim.Ok || _shown.Armed == HandKind.Shove ? Input.CursorShape.Cross : Input.CursorShape.Forbidden)   // 겨누는 중: 십자 · 안 되는 칸은 금지
            : r.Hit is { Enabled: true } || agent >= 0 ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow;
        if (shape != _cursor) { _cursor = shape; Input.SetDefaultCursorShape(shape); }
    }

    private (double x, double y) CellAt(Vector2 p)
    {
        var vp = GetViewportRect().Size; var f = _shown!;
        return (f.CamLeft + p.X / vp.X * f.CamW, f.CamTop + p.Y / vp.Y * f.CamH);
    }

    /// <summary>소리: 귀가 보낸 것을 받고, Godot 소리 버퍼가 빈 만큼 섞어 채운다 (메인 스레드 — 세계가 무거운 순간에도 이어 도는 소리는 끊기지 않는다).</summary>
    private void PumpAudio()
    {
        var show = _show;
        if (show == null || _playback == null) return;
        _mix.Take(show.Ears);
        int frames = Math.Min(_playback.GetFramesAvailable(), 4096);
        if (frames <= 0) return;
        if (_audio.Length < frames * 2) _audio = new float[frames * 2];
        if (_push.Length != frames) _push = new Vector2[frames];
        _mix.Master = _hs.Mute == 2 ? 0 : 0.9f;
        _mix.MusicGain = _hs.Mute == 0 ? Mixer.MusicLevel : 0;
        _mix.Render(_audio, frames);
        for (int i = 0; i < frames; i++) _push[i] = new Vector2(_audio[2 * i], _audio[2 * i + 1]);
        _playback.PushBuffer(_push);
    }

    /// <summary>확인용: 화면을 찍고 · 프레임 시간을 모으고 · 정한 시간이 지나면 적고 나간다.</summary>
    private void Check(double delta)
    {
        _runT += delta;
        if (_pick >= 0 && _runT > 3) { int who = _pick; _pick = -1; _cmds.Enqueue(s => s.SelectAgent(who)); }
        if (_openPanel != PanelKind.None && _runT > 3) { var k = _openPanel; _openPanel = PanelKind.None; _cmds.Enqueue(s => s.TogglePanel(k)); }
        if (_view >= 0 && _runT > 2) { double v = _view; _view = -1; _cmds.Enqueue(s => { if (v == 0) s.Overview = true; else { s.ZoomBy(1); s.ViewW = v; } }); }
        if (_overlay != Overlay.None && _runT > 2) { var o = _overlay; _overlay = Overlay.None; _cmds.Enqueue(s => s.Show = o); }
        if (_thoughts >= 0) { int th = _thoughts; _thoughts = -1; _cmds.Enqueue(s => s.ThoughtBubbles = th); }
        if (_helpOnce && _runT > 2) { _helpOnce = false; _hs.Help = true; }
        if (_ui != null && _runT > 3) { string ui = _ui; _ui = null; _cmds.Enqueue(s => SetUpUi(s, ui)); }
        if (_shown != null && _runT > 1) _frameMs.Add(delta * 1000);
        if (_shotDir != null && _shown != null && _runT >= _nextShot)
        {
            _nextShot = _runT + _shotEvery;
            var img = GetViewport().GetTexture().GetImage();
            string path = System.IO.Path.Combine(_shotDir, _shotName != null ? _shotName + ".png" : $"{_shotN++:00}-{_runT:000}s.png");
            img.SavePng(path);
            GD.Print($"{path} · {_shown.Status} · {_shown.Story}");
        }
        if (_runFor > 0 && _runT >= _runFor)
        {
            var o = _frameMs.OrderBy(x => x).ToList();
            double P(double q) => o.Count == 0 ? 0 : o[(int)Math.Min(o.Count - 1, q * o.Count)];
            GD.Print($"화면 {_runT:0}초 · 그림 {_picsTotal}장 · 게임 {Time.Clock(_shown?.Tick ?? 0)} · 프레임 {o.Count} · 보통 {P(0.5):0.0}ms · 99% {P(0.99):0.0}ms · 가장 김 {(o.Count > 0 ? o[^1] : 0):0.0}ms · 17ms 넘은 프레임 {o.Count(x => x > 17.5)} · 메인: 자리표 {_hudMs / Math.Max(1, _fpsTotal):0.00}ms · 그리기 명령 {_drawMs / Math.Max(1, _fpsTotal):0.00}ms ({string.Join(" · ", _laps.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / Math.Max(1, _fpsTotal):0.00}"))})");
            _runFor = 0;
            GetTree().Quit();
        }
    }

    /// <summary>화면 사진용 차림 (--ui=장면[:작은 탭] — 작업 스레드): 사람 · 로봇 자세히 (첫 사람 · 첫 로봇) · 로봇 판 · 주컴퓨터 · 사고 판.</summary>
    private static void SetUpUi(LiveShow s, string ui)
    {
        var parts = ui.Split(':');
        int sub = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        var mind = s.World.Find<ShipEngine.Mind>();
        int First(bool person) { if (mind != null) foreach (var a in mind.Agents) if (a.IsPerson == person) return a.Id; return -1; }
        switch (parts[0])
        {
            case "person": s.OpenDetail(DetailKind.Person, First(true)); s.SetDetailSub(sub); break;
            case "robot": s.OpenDetail(DetailKind.Robot, First(false)); s.SetDetailSub(sub); break;
            case "robots": if (s.Panel != PanelKind.Robots) s.TogglePanel(PanelKind.Robots); break;
            case "computer": s.OpenDetail(DetailKind.Computer, -1); s.SetDetailSub(sub); break;
            case "hand": if (s.Panel != PanelKind.Hand) s.TogglePanel(PanelKind.Hand); break;
            case "hand-armed" or "hand-fire":
            {
                // 침대 위에 불씨를 겨눈다 (hand-fire는 곧 일으킨다 — 몇 초 뒤 연기 · 말 창 · 사건 띠)
                var L = s.Sim.Layout;
                var bed = L.Furniture.FirstOrDefault(fu => fu.Type == "Bed" && fu.Cells.Length > 0);
                if (bed == null) break;
                int c = bed.Cells[0];
                if (s.Panel != PanelKind.Hand) s.TogglePanel(PanelKind.Hand);
                s.ArmHand(HandKind.Fire, (byte)(parts[0] == "hand-fire" ? 2 : 1));
                s.SetHoverAt(L.X(c) + 0.5, L.Y(c) + 0.5);
                if (parts[0] == "hand-fire") s.HandAt(L.X(c) + 0.5, L.Y(c) + 0.5);
                s.LookAt(c); s.ViewW = 26; s.CamX += 6;   // 판이 오른쪽을 가린다 — 겨눔 이름표가 보이게 왼쪽으로
                break;
            }
            case "hand-rock":
            {
                // 외판 칸에 운석을 겨눈다 (바깥에서 그 칸까지 오는 줄)
                var L = s.Sim.Layout;
                int best = -1; double bd = double.MaxValue, mx = L.W / 2.0, my = L.H / 2.0;
                for (int c = 0; c < L.N; c++)
                {
                    if (L.Kind[c] != CellKind.Wall || !L.Hull[c] || ShipEngine.Hand.Why(s.Sim, new HandOrder { Kind = HandKind.Rock, Cell = c, Size = 1 }).Length > 0) continue;
                    double d = Math.Abs(L.X(c) + 0.5 - mx) + Math.Abs(L.Y(c) + 0.5 - (my - L.H));   // 위쪽 가운데 외판
                    if (d < bd) { bd = d; best = c; }
                }
                if (best < 0) break;
                if (s.Panel != PanelKind.Hand) s.TogglePanel(PanelKind.Hand);
                s.ArmHand(HandKind.Rock, 1);
                s.SetHoverAt(L.X(best) + 0.5, L.Y(best) + 0.5);
                s.LookAt(best); s.ViewW = 40;
                break;
            }
        }
    }

    // ───────────────────────── 그리기 ─────────────────────────

    private float U => _hud.U;
    private Rect2 R(RectF r) => new(r.X * U, r.Y * U, r.W * U, r.H * U);
    private Vector2 V(float x, float y) => new(x * U, y * U);
    private int Fs(int size) => Math.Max(13, (int)Math.Round(size * U));

    // 단추 색 (docs/UI.md 6.4)
    private static readonly Color BtnBg = new(0.06f, 0.08f, 0.12f, 0.8f), BtnEdge = new(0.3f, 0.38f, 0.5f, 0.7f), OnBg = new(0.25f, 0.42f, 0.7f, 0.95f), OnEdge = new(0.65f, 0.8f, 1f);
    private static readonly Color Ink = new(0.92f, 0.94f, 0.98f), InkDim = new(0.62f, 0.68f, 0.78f), PanelBg = new(0.03f, 0.04f, 0.07f, 0.9f);

    private Vector2 _mouse;   // 이 장의 마우스 자리 (한 번만 묻는다 — 단추마다 묻지 않게)
    private bool Hovered(UiHit h) => h.R.Contains(_mouse.X / U, _mouse.Y / U);

    /// <summary>단추 하나: 바탕 · 테두리 · 글 (켜짐 · 올림 · 못 누름 · 위험).</summary>
    private void DrawButton(UiHit h, string text, int size = 15, bool danger = false)
    {
        var r = R(h.R);
        bool hover = Hovered(h) && h.Enabled;
        DrawRect(r, h.On ? OnBg : new Color(BtnBg, hover ? 0.95f : 0.8f));
        DrawRect(r, danger ? new Color(1f, 0.36f, 0.3f) : h.On ? OnEdge : new Color(BtnEdge, hover ? 1 : 0.7f), false, 1);
        if (text.Length > 0)
        {
            var col = !h.Enabled ? new Color(Ink, 0.4f) : h.On ? Colors.White : new Color(0.8f, 0.85f, 0.93f);
            DrawString(h.On ? _bold : _font, new Vector2(r.Position.X, r.Position.Y + r.Size.Y / 2 + Fs(size) * 0.36f), text, HorizontalAlignment.Center, r.Size.X, Fs(size), col);
        }
    }

    public override void _Draw()
    {
        long td0 = Stopwatch.GetTimestamp();
        DrawAll();
        _drawMs += (Stopwatch.GetTimestamp() - td0) * 1000.0 / Stopwatch.Frequency;
    }

    private void DrawAll()
    {
        var vp = GetViewportRect().Size;
        _mouse = GetViewport().GetMousePosition();
        if (_tex == null || _shown == null)
        {
            DrawCentered("새 엔진으로 배를 띄우는 중…", vp / 2, 26, new Color(0.8f, 0.85f, 0.95f));
            return;
        }
        var f = _shown;   // 관전 그림 자체는 자식 (LiveWorldPic)이 뒤에 그린다
        if (_cutDip > 0) DrawRect(new Rect2(Vector2.Zero, vp), new Color(0, 0, 0, (float)(0.7 * _cutDip / CutDipSec)));   // 컷 전환
        if (f.Replay.Length > 0) DrawRect(new Rect2(0, 44 * U, vp.X, vp.Y - 44 * U), new Color(0.95f, 0.78f, 0.45f, 0.75f), false, 4);   // 다시보기: 가장자리를 옛 필름 빛으로
        long t = Stopwatch.GetTimestamp();
        void Lap(string k) { if (_runFor <= 0) return; long n = Stopwatch.GetTimestamp(); _laps[k] = _laps.GetValueOrDefault(k) + (n - t) * 1000.0 / Stopwatch.Frequency; t = n; }
        DrawWorldMarks(f, vp); Lap("세계 위");
        DrawBubbles(f, vp); Lap("풍선");
        DrawTop(f); Lap("위 줄");
        DrawVitals(f); Lap("띠");
        DrawTabs(f); Lap("탭");
        if (f.Card.Length > 0) DrawCard(f);
        Lap("카드");
        DrawFeed(f); Lap("사건");
        DrawCenter(f); Lap("가운데");
        DrawTalk(f); Lap("말 창");
        DrawStory(f); Lap("아래 줄");
        if (f.Broadcast.Length > 0 && f.BroadcastAlpha > 0.05f) DrawBroadcast(f);
        DrawCaptions(f);
        DrawCrew(f); Lap("사람 줄");
        DrawViewGroup(f); Lap("보기");
        if (f.Legend.Length > 0) DrawLegend(f);
        if (f.PanelTitle.Length > 0) DrawPanel(f);
        Lap("판");
        DrawOpen(f);
        if (_meter) DrawString(_font, new Vector2(vp.X - 340, 120), $"초당 화면 {_screenPerSec}장 · 그림 {_picPerSec}장 · 소리 {_mix.Voices}개", HorizontalAlignment.Right, 324, 14, new Color(0.6f, 0.9f, 0.6f));
        if (_hs.Help) DrawHelp(vp);
        if (_hs.Ask != HudAsk.None) DrawAsk(vp);
        DrawTip();
    }

    /// <summary>세계 위: 고른 이의 고리 · 마우스를 올린 이의 옅은 고리와 머리 위 이름 · 함장의 손 (겨눔 고리 · 이름표 · 운석 줄 · 구름 화살표 · 끄는 화살표 · 누른 칸 고리 · 기다리는 손 점).</summary>
    private void DrawWorldMarks(LiveFrame f, Vector2 vp)
    {
        Vector2 Px(double x, double y) => new((float)((x - f.CamLeft) / f.CamW * vp.X), (float)((y - f.CamTop) / f.CamH * vp.Y));
        float cellPx = (float)(vp.X / f.CamW);
        DrawHandMarks(f, vp, Px, cellPx);
        if (!double.IsNaN(f.SelX)) DrawArc(Px(f.SelX, f.SelY), cellPx * 0.62f, 0, Mathf.Tau, 40, new Color(1f, 0.9f, 0.45f, 0.9f), 2.5f);
        if (f.HoverId >= 0 && f.HoverId != f.Selected && f.HoverName.Length > 0)
        {
            var sp = f.Spots.FirstOrDefault(s => s.Id == f.HoverId);
            if (sp.Id == f.HoverId && !double.IsNaN(sp.X))
            {
                var c = Px(sp.X, sp.Y);
                DrawArc(c, cellPx * 0.58f, 0, Mathf.Tau, 32, new Color(0.75f, 0.85f, 1f, 0.7f), 1.5f);
                string t = f.HoverName;
                var sz = _bold.GetStringSize(t, HorizontalAlignment.Left, -1, Fs(14));
                var box = new Rect2(c.X - sz.X / 2 - 8, c.Y - cellPx * 1.05f - sz.Y, sz.X + 16, sz.Y + 4);
                DrawRect(box, new Color(0.03f, 0.04f, 0.07f, 0.85f));
                DrawString(_bold, new Vector2(box.Position.X + 8, box.End.Y - 6), t, HorizontalAlignment.Left, -1, Fs(14), Ink);
            }
        }
    }

    private static readonly Color Amber = new(1f, 0.72f, 0.3f), Refuse = new(1f, 0.36f, 0.3f);

    /// <summary>함장의 손 표 (docs/UI2.md §6.4 — 그리기만): 누른 칸에 0.5초 퍼지는 고리 · 기다리는 손 깜빡이는 점 · 겨눔 고리 (되면 호박색 · 안 되면 빨강) · 이름표 · 운석이 오는 줄 · 구름 화살표 · 끄는 화살표와 빠르기.</summary>
    private void DrawHandMarks(LiveFrame f, Vector2 vp, Func<double, double, Vector2> px, float cellPx)
    {
        float blink = 0.5f + 0.5f * Mathf.Sin(Godot.Time.GetTicksMsec() / 1000f * Mathf.Tau * 1.5f);
        foreach (var m in f.HandMarks)
        {
            var c = px(m.X, m.Y);
            if (m.Waiting) DrawCircle(c, 5 * U, new Color(Amber, 0.35f + 0.65f * blink));
            else DrawArc(c, cellPx * (0.3f + m.Age * 2.4f), 0, Mathf.Tau, 40, new Color(Amber, Math.Max(0, 1 - m.Age / 0.5f)), 3 * U);
        }
        if (_hs.HandDrag != 0)
        {
            var a = px(_hs.HandDragX, _hs.HandDragY); var b = _mouse;
            var d = b - a; float len = d.Length();
            if (len > 2)
            {
                float v = Math.Min(8, len / cellPx * 2);
                var tip = len / cellPx * 2 > 8 ? a + d / len * (cellPx * 4) : b;
                DrawLine(a, tip, Amber, 3 * U);
                var u = (tip - a).Normalized(); var n = new Vector2(-u.Y, u.X);
                DrawPolygon(new[] { tip, tip - u * 12 * U + n * 7 * U, tip - u * 12 * U - n * 7 * U }, new[] { Amber });
                DrawLabel($"{v:0.0}m/s", tip + new Vector2(10 * U, -10 * U), Amber);
            }
            return;
        }
        if (f.Armed == HandKind.None || double.IsNaN(f.Aim.X)) return;
        var col = f.Aim.Ok ? Amber : Refuse;
        var at = px(f.Aim.X, f.Aim.Y);
        if (f.Armed == HandKind.Rock && f.Aim.Ok)
        {
            var far = px(f.Aim.X + f.Aim.Dx * 14, f.Aim.Y + f.Aim.Dy * 14);
            DrawDashedLine(far, at, new Color(col, 0.85f), 2 * U, 10 * U);
        }
        if (f.Armed == HandKind.Cloud && (f.Aim.Dx != 0 || f.Aim.Dy != 0))
        {
            var from = px(f.Aim.X + f.Aim.Dx * 6, f.Aim.Y + f.Aim.Dy * 6);
            var u = (at - from).Normalized(); var n = new Vector2(-u.Y, u.X);
            DrawLine(from, at, col, 3 * U);
            DrawPolygon(new[] { at, at - u * 14 * U + n * 8 * U, at - u * 14 * U - n * 8 * U }, new[] { col });
        }
        float rr = cellPx * 0.55f;
        DrawArc(at, rr, 0, Mathf.Tau, 40, col, 2.5f * U);
        DrawLine(at + new Vector2(-rr * 1.4f, 0), at + new Vector2(-rr * 0.6f, 0), col, 2 * U); DrawLine(at + new Vector2(rr * 0.6f, 0), at + new Vector2(rr * 1.4f, 0), col, 2 * U);
        DrawLine(at + new Vector2(0, -rr * 1.4f), at + new Vector2(0, -rr * 0.6f), col, 2 * U); DrawLine(at + new Vector2(0, rr * 0.6f), at + new Vector2(0, rr * 1.4f), col, 2 * U);
        DrawLabel(f.Aim.Ok ? f.Aim.Label : $"여기는 안 된다 — {f.Aim.Why}", at + new Vector2(0, -rr * 1.6f - 14 * U), col, true);
    }

    /// <summary>세계 위 이름표 (어두운 바탕 · 가운데 맞춤이면 그 자리 위 가운데).</summary>
    private void DrawLabel(string t, Vector2 at, Color col, bool center = false)
    {
        var sz = _bold.GetStringSize(t, HorizontalAlignment.Left, -1, Fs(14));
        var box = new Rect2(at.X - (center ? sz.X / 2 + 8 : 0), at.Y - sz.Y, sz.X + 16, sz.Y + 6);
        // 판 · 카드가 가리지 않게 (그 사이 세계 칸 안으로 민다)
        float lo = _hud.Areas.TryGetValue("카드", out var cd) ? (cd.Right + 8) * U : 8, hi = (_hud.Areas.TryGetValue("판", out var pa) ? pa.X - 8 : _hud.W - 8) * U;
        if (box.End.X > hi) box.Position = new Vector2(Math.Max(lo, hi - box.Size.X), box.Position.Y);
        if (box.Position.X < lo) box.Position = new Vector2(lo, box.Position.Y);
        DrawRect(box, new Color(0.03f, 0.04f, 0.07f, 0.88f));
        DrawRect(box, new Color(col, 0.8f), false, 1);
        DrawString(_bold, new Vector2(box.Position.X + 8, box.End.Y - 7), t, HorizontalAlignment.Left, -1, Fs(14), col);
    }

    private readonly StyleBoxFlat _speech = new() { CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7, AntiAliasing = true };
    private readonly StyleBoxFlat _cloud = new() { CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14, AntiAliasing = true };
    private static readonly string[] ThoughtWords = { "속마음 풍선: 끔", "속마음 풍선: 고른 이", "속마음 풍선: 화면 안 셋" };

    /// <summary>머리 위 풍선: 입말 = 흰 둥근 네모 + 꼬리 · 속말 = 옅은 푸른 구름 + 작은 동그라미 둘 · 판이 가린 자리는 그리지 않는다 · 겹치면 위로 민다.</summary>
    private void DrawBubbles(LiveFrame f, Vector2 vp)
    {
        if (f.Bubbles.Length == 0) return;
        float cellPx = (float)(vp.X / f.CamW);
        int fs = Fs((int)Math.Clamp(cellPx * 0.42f, 13, 17));
        float lineH = _font.GetHeight(fs);
        // 자리부터: 머리 위 가운데 → 오른쪽 · 왼쪽으로 비킴 → 겹친 풍선 위로 (여섯 번까지) · 판이 가린 머리 · 위 줄에 닿는 것은 뺀다
        _bubblePlaced.Clear();
        if (!double.IsNaN(f.SelX))   // 고른 이의 몸은 가리지 않는다 (그리지 않는 자리)
        {
            var c = new Vector2((float)((f.SelX - f.CamLeft) / f.CamW * vp.X), (float)((f.SelY - f.CamTop) / f.CamH * vp.Y));
            _bubblePlaced.Add((new Rect2(c - new Vector2(0.45f, 0.62f) * cellPx, new Vector2(0.9f, 1.1f) * cellPx), c, false, 0, Array.Empty<string>()));
        }
        foreach (var b in f.Bubbles)
        {
            var head = new Vector2((float)((b.X - f.CamLeft) / f.CamW * vp.X), (float)((b.Y - f.CamTop) / f.CamH * vp.Y));
            if (_hud.At(head.X / U, head.Y / U).Kind != RouteKind.World) continue;   // 판 · 손잡이가 가린 머리
            var lines = b.Text.Split('\n');
            float w = 0;
            foreach (var l in lines) w = Math.Max(w, MeasureW(l, fs, false));
            var size = new Vector2(w + 18 * U, lines.Length * lineH + 10 * U);
            float top = head.Y - size.Y - 10 * U, gap = 6 * U;
            Rect2 Fit(float x, float y) => new(new Vector2(Math.Clamp(x, 4, vp.X - size.X - 4), y), size);
            bool Free(Rect2 r) => _bubblePlaced.TrueForAll(p => !p.box.Grow(2 * U).Intersects(r));
            var box = Fit(head.X - size.X / 2, top);
            if (!Free(box))
            {
                var hit = _bubblePlaced.First(p => p.box.Grow(2 * U).Intersects(box)).box;
                var right = Fit(hit.End.X + gap, top); var left = Fit(hit.Position.X - size.X - gap, top);
                if (Free(right)) box = right;
                else if (Free(left)) box = left;
                else
                    for (int k = 0; k < 6 && !Free(box); k++)
                        box.Position = new Vector2(box.Position.X, _bubblePlaced.Where(p => p.box.Grow(2 * U).Intersects(box)).Min(p => p.box.Position.Y) - size.Y - gap);
            }
            if (box.Position.Y < 46 * U || !Free(box)) continue;
            _bubblePlaced.Add((box, head, b.Inner, b.Alpha, lines));
        }
        // 꼬리 · 구름 점을 먼저 (다른 풍선 글 뒤로 숨는다) → 풍선 → 글
        foreach (var (box, head, inner, a, lines) in _bubblePlaced)
        {
            if (lines.Length == 0) continue;
            if (inner)
            {
                var fill = new Color(0.86f, 0.92f, 1f, 0.82f * a);
                var foot = new Vector2(Math.Clamp(head.X, box.Position.X + 10 * U, box.End.X - 10 * U), box.End.Y);
                DrawCircle(foot.Lerp(head, 0.35f), 3.2f * U, fill);
                DrawCircle(foot.Lerp(head, 0.7f), 2f * U, fill);
            }
            else
            {
                var fill = new Color(0.98f, 0.97f, 0.93f, 0.95f * a);
                float tx = Math.Clamp(head.X, box.Position.X + 10 * U, box.End.X - 10 * U);
                DrawColoredPolygon(new[] { new Vector2(tx - 5 * U, box.End.Y - 1), new Vector2(tx + 5 * U, box.End.Y - 1), new Vector2(head.X, head.Y - 2 * U) }, fill);
            }
        }
        foreach (var (box, _, inner, a, lines) in _bubblePlaced)
        {
            if (lines.Length == 0) continue;
            if (inner) DrawStyleBox(_cloud, box, new Color(0.86f, 0.92f, 1f, 0.82f * a));
            else DrawStyleBox(_speech, box, new Color(0.98f, 0.97f, 0.93f, 0.95f * a));
            var ink = inner ? new Color(0.18f, 0.24f, 0.36f, a) : new Color(0.08f, 0.08f, 0.1f, a);
            for (int i = 0; i < lines.Length; i++)
                DrawString(_font, new Vector2(box.Position.X + 9 * U, box.Position.Y + 5 * U + lineH * i + lineH * 0.78f), lines[i], HorizontalAlignment.Left, -1, fs, ink);
        }
    }

    private readonly List<(Rect2 box, Vector2 head, bool inner, float alpha, string[] lines)> _bubblePlaced = new();

    private void DrawStyleBox(StyleBoxFlat sb, Rect2 r, Color c) { sb.BgColor = c; DrawStyleBox(sb, r); }

    /// <summary>위 줄: 분위기 점 · 상태 글 · 멈춤 · 빠르기 · 고정 ▾ · '새 엔진 판' · 소리 · 자막 · 도움말 · 메뉴.</summary>
    private void DrawTop(LiveFrame f)
    {
        var mood = MoodColor(f.Mood);
        DrawRect(R(new RectF(0, 0, _hud.W, 44)), new Color(0.03f, 0.04f, 0.07f, 0.84f));
        DrawRect(R(new RectF(0, 42, _hud.W, 2)), new Color(mood, 0.9f));
        DrawCircle(V(22, 22), 7 * U, mood);
        var st = R(_hud.Area("상태 글"));
        DrawString(_bold, new Vector2(st.Position.X, 29 * U), f.Status, HorizontalAlignment.Left, st.Size.X, Fs(18), Ink);
        var badge = R(_hud.Area("표"));
        DrawRect(badge, new Color(0.1f, 0.42f, 0.38f, 0.95f));
        DrawRect(badge, new Color(0.45f, 0.95f, 0.8f, 0.9f), false, 1.5f);
        DrawString(_bold, new Vector2(badge.Position.X, badge.Position.Y + badge.Size.Y / 2 + Fs(15) * 0.36f), "새 엔진 판", HorizontalAlignment.Center, badge.Size.X, Fs(15), new Color(0.88f, 1f, 0.95f));
        foreach (var h in _hud.Hits)
        {
            switch (h.Act)
            {
                case UiAct.Pause:
                {
                    DrawButton(h, "");
                    var r = R(h.R); var c = new Color(0.9f, 0.93f, 1f); var m = r.GetCenter();
                    if (f.Paused || f.Holding) DrawColoredPolygon(new[] { m + new Vector2(-5, -7) * U, m + new Vector2(-5, 7) * U, m + new Vector2(7, 0) * U }, c);
                    else { DrawRect(new Rect2(m + new Vector2(-6, -7) * U, new Vector2(4, 14) * U), c); DrawRect(new Rect2(m + new Vector2(2, -7) * U, new Vector2(4, 14) * U), c); }
                    break;
                }
                case UiAct.Speed: DrawButton(h, LiveShow.SpeedLabels[h.Key]); break;
                case UiAct.FixedMenu:
                {
                    DrawButton(h, f.FixedPick >= 0 ? $"×{LiveShow.FixedGameSecPerSec[f.FixedPick]:0}   " : "고정   ");
                    var r = R(h.R); var m = new Vector2(r.End.X - 14 * U, r.GetCenter().Y);
                    DrawColoredPolygon(new[] { m + new Vector2(-5, -3) * U, m + new Vector2(5, -3) * U, m + new Vector2(0, 4) * U }, new Color(0.85f, 0.9f, 1f));
                    break;
                }
                case UiAct.Sound or UiAct.Captions or UiAct.Help or UiAct.Menu when h.R.Y < 44:
                {
                    bool blink = h.Act == UiAct.Help && _hs.HelpBlink > 0 && (int)(_clock * 2) % 2 == 0;
                    DrawButton(h with { On = h.On && h.Act != UiAct.Sound && h.Act != UiAct.Captions || blink }, "");
                    var c = R(h.R).GetCenter();
                    var ink = blink ? new Color(1f, 0.78f, 0.35f) : Ink;
                    if (h.Act == UiAct.Sound) DrawSpeaker(c, ink, _hs.Mute);
                    else if (h.Act == UiAct.Captions) DrawBubbleIcon(c, f.CaptionsOn ? ink : new Color(ink, 0.45f));
                    else if (h.Act == UiAct.Help) DrawString(_bold, new Vector2(c.X - 10 * U, c.Y + 7 * U), "?", HorizontalAlignment.Center, 20 * U, Fs(19), ink);
                    else for (int i = -1; i <= 1; i++) DrawRect(new Rect2(c + new Vector2(-8, i * 5 - 1) * U, new Vector2(16, 2.5f) * U), ink);
                    break;
                }
            }
        }
    }

    private void DrawSpeaker(Vector2 c, Color col, int mute)
    {
        DrawColoredPolygon(new[] { c + new Vector2(-9, -3) * U, c + new Vector2(-5, -3) * U, c + new Vector2(0, -8) * U, c + new Vector2(0, 8) * U, c + new Vector2(-5, 3) * U, c + new Vector2(-9, 3) * U }, col);
        if (mute == 2) { DrawLine(c + new Vector2(3, -5) * U, c + new Vector2(11, 5) * U, col, 2 * U); DrawLine(c + new Vector2(3, 5) * U, c + new Vector2(11, -5) * U, col, 2 * U); return; }
        DrawArc(c + new Vector2(1, 0) * U, 5 * U, -0.9f, 0.9f, 8, col, 1.5f * U);
        if (mute == 0) DrawArc(c + new Vector2(1, 0) * U, 9 * U, -0.9f, 0.9f, 10, col, 1.5f * U);
    }

    private void DrawBubbleIcon(Vector2 c, Color col)
    {
        DrawRect(new Rect2(c + new Vector2(-10, -7) * U, new Vector2(20, 12) * U), col, false, 1.5f * U);
        DrawColoredPolygon(new[] { c + new Vector2(-4, 5) * U, c + new Vector2(2, 5) * U, c + new Vector2(-6, 10) * U }, col);
        DrawLine(c + new Vector2(-6, -2) * U, c + new Vector2(6, -2) * U, col, 1.5f * U);
        DrawLine(c + new Vector2(-6, 1.5f) * U, c + new Vector2(3, 1.5f) * U, col, 1.5f * U);
    }

    /// <summary>판 탭 다섯 (둘째 줄 오른쪽): 켜진 탭은 판과 이어진다 · 주컴퓨터에 안 읽은 일지면 호박색 점.</summary>
    private void DrawTabs(LiveFrame f)
    {
        foreach (var h in _hud.Hits)
        {
            if (h.Act != UiAct.Tab) continue;
            string label = HudLayout.TabNames[h.Key] + (h.Key == 0 && f.Highlights > 0 ? $" {f.Highlights}" : "");
            DrawButton(h, label);
            if (HudLayout.TabKinds[h.Key] == PanelKind.Hand)
            {
                // 사고 탭: 호박색 (세계를 바꾸는 손) · 겨누는 중이면 깜빡인다
                float pulse = f.Armed != HandKind.None ? 0.55f + 0.45f * Mathf.Sin(Godot.Time.GetTicksMsec() / 1000f * Mathf.Tau * 1.2f) : 1;
                if (!h.On || f.Armed != HandKind.None) DrawRect(R(h.R), new Color(1f, 0.72f, 0.3f, 0.85f * pulse), false, f.Armed != HandKind.None ? 2.5f : 1.5f);
            }
            if (h.Key == 2 && f.JournalNew) DrawCircle(R(h.R).Position + new Vector2(R(h.R).Size.X - 7 * U, 7 * U), 4 * U, new Color(1f, 0.72f, 0.3f));
        }
    }

    /// <summary>배 상태 띠 (UI U1 — 늘 보인다): 공기 · 사람 · 전력 · 선체 · 물자 · 불 — 누르면 그 자리로.</summary>
    private void DrawVitals(LiveFrame f)
    {
        float pulse = 0.65f + 0.35f * Mathf.Sin(Godot.Time.GetTicksMsec() / 1000f * Mathf.Tau * 0.8f);
        foreach (var h in _hud.Hits)
        {
            if (h.Act != UiAct.Vital || h.Key >= f.Vitals.Length) continue;
            var g = f.Vitals[h.Key];
            var r = R(h.R);
            var col = g.Level switch { 2 => new Color(1f, 0.36f, 0.3f), 1 => new Color(1f, 0.76f, 0.32f), _ => new Color(0.5f, 0.86f, 0.76f) };
            DrawRect(r, new Color(0.03f, 0.04f, 0.07f, Hovered(h) ? 0.95f : 0.8f));
            DrawRect(new Rect2(r.Position, new Vector2(3 * U, r.Size.Y)), g.Level == 2 ? new Color(col, pulse) : col);
            if (g.Level == 2) DrawRect(r, new Color(col, 0.55f * pulse), false, 1);
            if (Hovered(h)) DrawRect(r, new Color(0.6f, 0.75f, 1f, 0.8f), false, 1);
            DrawIcon(g.Icon, r.Position + new Vector2(17 * U, r.Size.Y / 2), 8 * U, g.Level == 0 ? new Color(0.78f, 0.86f, 0.92f) : col);
            float nw = MeasureW(g.Name, Fs(14), true);
            float base0 = r.Position.Y + r.Size.Y / 2 + Fs(14) * 0.36f;
            DrawString(_bold, new Vector2(r.Position.X + 30 * U, base0), g.Name, HorizontalAlignment.Left, -1, Fs(14), InkDim);
            DrawString(_font, new Vector2(r.Position.X + 30 * U + nw + 8 * U, base0), _hud.VitalsShort && g.Short.Length > 0 ? g.Short : g.Value, HorizontalAlignment.Left, Math.Max(10, r.Size.X - 30 * U - nw - 14 * U), Fs(14), g.Level == 0 ? Ink : col);
        }
    }

    /// <summary>띠의 작은 그림 (도형으로 — 가운데 c · 반지름 s).</summary>
    private void DrawIcon(string key, Vector2 c, float s, Color col)
    {
        switch (key)
        {
            case "computer":   // 주컴퓨터: 화면 + 받침
                DrawRect(new Rect2(c + new Vector2(-s, -s * 0.8f), new Vector2(s * 2, s * 1.3f)), col, false, 1.5f);
                DrawLine(c + new Vector2(0, s * 0.5f), c + new Vector2(0, s * 0.95f), col, 1.5f);
                DrawLine(c + new Vector2(-s * 0.55f, s * 0.95f), c + new Vector2(s * 0.55f, s * 0.95f), col, 1.5f);
                DrawLine(c + new Vector2(-s * 0.5f, -s * 0.25f), c + new Vector2(s * 0.3f, -s * 0.25f), col, 1.5f);
                break;
            case "air":   // 공기: 동그라미 속 거품 둘
                DrawArc(c, s, 0, Mathf.Tau, 20, col, 1.5f);
                DrawCircle(c + new Vector2(-s * 0.3f, s * 0.15f), s * 0.28f, col);
                DrawCircle(c + new Vector2(s * 0.3f, -s * 0.25f), s * 0.2f, col);
                break;
            case "crew":  // 사람: 머리와 어깨
                DrawCircle(c + new Vector2(0, -s * 0.35f), s * 0.38f, col);
                DrawArc(c + new Vector2(0, s * 0.95f), s * 0.75f, Mathf.Pi * 1.08f, Mathf.Pi * 1.92f, 12, col, 2f);
                break;
            case "power": // 전력: 번개
                DrawColoredPolygon(new[] { c + new Vector2(s * 0.2f, -s), c + new Vector2(-s * 0.55f, s * 0.15f), c + new Vector2(-s * 0.05f, s * 0.15f),
                    c + new Vector2(-s * 0.25f, s), c + new Vector2(s * 0.55f, -s * 0.2f), c + new Vector2(s * 0.05f, -s * 0.2f) }, col);
                break;
            case "hull":  // 선체: 육각 판
            {
                var pts = new Vector2[7];
                for (int i = 0; i < 7; i++) { float a = Mathf.Pi / 6 + i * Mathf.Pi / 3; pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s; }
                DrawPolyline(pts, col, 1.5f);
                break;
            }
            case "supply": // 물자: 상자
                DrawRect(new Rect2(c.X - s * 0.85f, c.Y - s * 0.6f, s * 1.7f, s * 1.4f), col, false, 1.5f);
                DrawLine(c + new Vector2(-s * 0.85f, -s * 0.15f), c + new Vector2(s * 0.85f, -s * 0.15f), col, 1.5f);
                break;
            case "fire":  // 불: 불꽃
                DrawColoredPolygon(new[] { c + new Vector2(0, -s), c + new Vector2(s * 0.55f, -s * 0.1f), c + new Vector2(s * 0.6f, s * 0.45f), c + new Vector2(0, s),
                    c + new Vector2(-s * 0.6f, s * 0.45f), c + new Vector2(-s * 0.45f, -s * 0.2f), c + new Vector2(-s * 0.15f, s * 0.05f) }, col);
                break;
            default:
                DrawCircle(c, s * 0.5f, col);
                break;
        }
    }

    /// <summary>고른 이의 카드 (왼쪽 위): 이름 줄 (누르면 그 자리로) · [따라가기] · [×] · 줄마다 두 줄까지.</summary>
    private void DrawCard(LiveFrame f)
    {
        var box = R(_hud.Area("카드"));
        DrawRect(box, new Color(0.03f, 0.04f, 0.07f, 0.88f));
        DrawRect(box, new Color(1f, 0.9f, 0.45f, 0.7f), false, 1.5f);
        float w = box.Size.X, cy = box.Position.Y + 28 * U;
        foreach (var h in _hud.Hits)
        {
            if (h.Act == UiAct.CardFollow) DrawButton(h, "따라가기", 14);
            else if (h.Act == UiAct.CardClose) { DrawButton(h, ""); DrawCross(R(h.R).GetCenter(), 6 * U, Ink); }
            else if (h.Act == UiAct.DetailOpen) { DrawButton(h, "자세히 보기", 14); DrawChevron(R(h.R).GetCenter() + new Vector2(MeasureW("자세히 보기", Fs(14), false) / 2 + 12 * U, 0), 4 * U, +1, InkDim); }
            else if (h.Act == UiAct.CardName && Hovered(h)) DrawLine(new Vector2(box.Position.X + 16 * U, cy + 4 * U), new Vector2(box.Position.X + 16 * U + _bold.GetStringSize(f.Card[0], HorizontalAlignment.Left, -1, Fs(19)).X, cy + 4 * U), new Color(1f, 0.92f, 0.65f), 1);
        }
        float cardEnd = box.End.Y - (_hud.Hits.Any(x => x.Act == UiAct.DetailOpen) ? 36 * U : 0);
        for (int i = 0; i < f.Card.Length; i++)
        {
            bool head = i == 0;
            float avail = head ? w - 28 * U - 130 * U : w - 28 * U;
            int size = Fs(head ? 19 : 16);
            float lh = Math.Max(26 * U, (head ? MeasureHB(f.Card[i], size, avail, 1) : MeasureH(f.Card[i], size, avail, 2)) + 6 * U);
            if (cy + lh - 20 * U > cardEnd) break;
            DrawMultilineString(head ? _bold : _font, new Vector2(box.Position.X + 16 * U, cy), f.Card[i], HorizontalAlignment.Left, avail, size, head ? 1 : 2,
                head ? new Color(1f, 0.92f, 0.65f) : new Color(0.86f, 0.89f, 0.95f));
            cy += lh;
        }
    }

    private static string HoursMin(double minutes) { int h = (int)(minutes / 60), m = (int)(minutes % 60); return h > 0 ? $"{h}시간 {m}분" : $"{m}분"; }

    /// <summary>꺾쇠 (도형 — 글꼴에 기대지 않는다): dir +1 오른쪽 › · −1 왼쪽 ‹.</summary>
    private void DrawChevron(Vector2 c, float s, int dir, Color col)
    {
        DrawLine(c + new Vector2(-s * dir * 0.6f, -s), c + new Vector2(s * dir * 0.6f, 0), col, 2 * U);
        DrawLine(c + new Vector2(s * dir * 0.6f, 0), c + new Vector2(-s * dir * 0.6f, s), col, 2 * U);
    }

    private void DrawCross(Vector2 c, float s, Color col)
    {
        DrawLine(c + new Vector2(-s, -s), c + new Vector2(s, s), col, 2 * U);
        DrawLine(c + new Vector2(-s, s), c + new Vector2(s, -s), col, 2 * U);
    }

    /// <summary>사건 띠 (UI U4 — 카드 아래): 주컴퓨터가 아는 지금의 사건 · 막 끝난 사건 — 누르면 그 자리로.</summary>
    private void DrawFeed(LiveFrame f)
    {
        float pulse = 0.55f + 0.45f * Mathf.Sin(Godot.Time.GetTicksMsec() / 1000f * Mathf.Tau);
        var mouse = _mouse;
        for (int i = 0; i < f.Feed.Length; i++)
        {
            if (!_hud.Areas.TryGetValue("사건" + i, out var rf)) break;
            var it = f.Feed[i];
            var r = R(rf);
            bool hover = it.Cell >= 0 && rf.Contains(mouse.X / U, mouse.Y / U);
            DrawRect(r, new Color(0.03f, 0.04f, 0.07f, it.Active ? 0.86f : 0.6f));
            var tone = it.Level switch { 2 => new Color(1f, 0.35f, 0.3f, it.Active ? pulse : 0.5f), 1 => new Color(1f, 0.76f, 0.32f, it.Active ? 1 : 0.5f), _ => new Color(0.5f, 0.68f, 0.95f, it.Active ? 1 : 0.5f) };
            DrawRect(new Rect2(r.Position, new Vector2(4 * U, r.Size.Y)), tone);
            var text = it.Active ? Ink : new Color(0.6f, 0.64f, 0.72f);
            float base0 = r.Position.Y + r.Size.Y / 2 + Fs(15) * 0.36f;
            DrawString(_font, new Vector2(r.Position.X + 10 * U, base0), it.Time, HorizontalAlignment.Left, -1, Fs(14), InkDim);
            DrawString(it.Active && it.Level == 2 ? _bold : _font, new Vector2(r.Position.X + 58 * U, base0), it.Text, HorizontalAlignment.Left, r.Size.X - 66 * U, Fs(15), text);
            if (hover) DrawRect(r, new Color(0.6f, 0.75f, 1f, 0.8f), false, 1);
        }
    }

    /// <summary>가운데 칸 (위): 다시보기 띠 → 출항 준비 띠 → ★ 새 하이라이트 → 알림.</summary>
    private void DrawCenter(LiveFrame f)
    {
        if (_hud.Areas.TryGetValue("다시보기 띠", out var rb))
        {
            var r = R(rb); var film = new Color(0.95f, 0.78f, 0.45f);
            DrawRect(r, new Color(0.08f, 0.06f, 0.03f, 0.9f)); DrawRect(r, film, false, 2);
            DrawString(_bold, new Vector2(r.Position.X + 14 * U, r.Position.Y + r.Size.Y / 2 + Fs(16) * 0.36f), "다시보기 · " + f.Replay, HorizontalAlignment.Left, r.Size.X - 140 * U, Fs(16), new Color(1f, 0.88f, 0.62f));
            foreach (var h in _hud.Hits) if (h.Act == UiAct.ReplayStop) { DrawButton(h, "   돌아오기", 15); var p = R(h.R).Position; DrawRect(new Rect2(p + new Vector2(14, 11) * U, new Vector2(10, 10) * U), film); }
        }
        if (_hud.Areas.TryGetValue("출항 띠", out var hb))
        {
            var r = R(hb);
            DrawRect(r, new Color(0.03f, 0.06f, 0.1f, 0.92f)); DrawRect(r, new Color(0.5f, 0.85f, 1f, 0.9f), false, 1.5f);
            string t = double.IsNaN(f.DepartIn) ? "출항 준비 중" : $"출항 준비 중 · 출발까지 {HoursMin(f.DepartIn)}";
            DrawString(_bold, new Vector2(r.Position.X + 14 * U, r.Position.Y + r.Size.Y / 2 + Fs(16) * 0.36f), t, HorizontalAlignment.Left, r.Size.X - 300 * U, Fs(16), Ink);
            foreach (var h in _hud.Hits) { if (h.Act == UiAct.Depart && h.R.Y >= hb.Y && h.R.Bottom <= hb.Bottom) DrawButton(h, "이대로 출항"); else if (h.Act == UiAct.RouteTab) DrawButton(h, "항로 고르기"); }
        }
        if (_hud.Areas.TryGetValue("★ 띠", out var sb))
        {
            var r = R(sb); float a = f.BannerAlpha;
            DrawRect(r, new Color(0.08f, 0.05f, 0.03f, 0.85f * a));
            DrawRect(r, new Color(1f, 0.72f, 0.3f, 0.9f * a), false, 2);
            bool btn = _hud.Hits.Any(h => h.Act == UiAct.BannerReplay);
            DrawMultilineString(_bold, new Vector2(r.Position.X + 20 * U, r.Position.Y + 9 * U + Fs(22) * 0.9f), "★ " + f.Banner, HorizontalAlignment.Left, r.Size.X - 40 * U - (btn ? 104 * U : 0), Fs(22), 2, new Color(1f, 0.86f, 0.6f, a));
            foreach (var h in _hud.Hits) if (h.Act == UiAct.BannerReplay) DrawButton(h, "▶ 다시보기", 14);
        }
        if (_hud.Areas.TryGetValue("알림", out var nb))
        {
            var r = R(nb);
            bool link = f.NoticeLink != PanelKind.None;
            var hit = _hud.Hits.FirstOrDefault(h => h.Act == UiAct.Notice);
            DrawRect(r, new Color(0.04f, 0.06f, 0.1f, hit != null && Hovered(hit) ? 0.97f : 0.9f));
            DrawRect(r, new Color(0.5f, 0.65f, 0.9f, 0.9f), false, 1);
            DrawMultilineString(_font, new Vector2(r.Position.X + 14 * U, r.Position.Y + 7 * U + Fs(17) * 0.9f), f.Notice + (link ? "  ›" : ""), HorizontalAlignment.Left, r.Size.X - 24 * U, Fs(17), 2, new Color(0.85f, 0.9f, 1f));
        }
    }

    /// <summary>아래 줄 (늘 잡아 둔다): 지금 장면의 까닭 (원인 사슬 — 누르면 사슬 판) · 비면 옅게 배 이름.</summary>
    private void DrawStory(LiveFrame f)
    {
        var r = R(_hud.Area("아래 줄"));
        DrawRect(r, new Color(0.03f, 0.04f, 0.07f, 0.8f));
        string text = f.Story.Length > 0 ? f.Story : $"{f.Ship} — 둘러보는 중";
        var hit = _hud.Hits.FirstOrDefault(h => h.Act == UiAct.Story);
        DrawMultilineString(_font, new Vector2(r.Position.X + 20 * U, r.Position.Y + 26 * U), text, HorizontalAlignment.Left, r.Size.X - 40 * U, Fs(17), 2,
            f.Story.Length > 0 ? new Color(0.88f, 0.9f, 0.95f) : new Color(0.6f, 0.65f, 0.75f));
        if (hit != null && Hovered(hit)) DrawRect(r, new Color(0.6f, 0.75f, 1f, 0.5f), false, 1);
    }

    private void DrawBroadcast(LiveFrame f)
    {
        var r = R(_hud.Area("방송")); float a = f.BroadcastAlpha;
        DrawRect(r, new Color(0.02f, 0.06f, 0.08f, 0.88f * a));
        DrawRect(r, new Color(0.45f, 0.85f, 0.9f, 0.85f * a), false, 1.5f);
        DrawMultilineString(_font, new Vector2(r.Position.X + 20 * U, r.Position.Y + 8 * U + Fs(17) * 0.9f), f.Broadcast, HorizontalAlignment.Left, r.Size.X - 40 * U, Fs(17), 3, new Color(0.8f, 0.96f, 1f, a));
    }

    /// <summary>소리 자막 (U · 자막 단추로 켜고 끈다 — 소리 단계 S6): 무엇이 · 어느 쪽에서 (화살표 · 곁) — 누름은 지나간다.</summary>
    private void DrawCaptions(LiveFrame f)
    {
        if (!_hud.Areas.TryGetValue("자막", out var ca)) return;
        float y = ca.Y * U + 20 * U;
        foreach (var c in f.Captions)
        {
            float a = (float)Math.Clamp(c.Left / 0.4, 0, 1);
            string line = (c.Dir.Length > 0 ? c.Dir + "  " : "") + c.Text + (c.Count > 1 ? $" ×{c.Count}" : "");
            var sz = _font.GetStringSize(line, HorizontalAlignment.Left, -1, Fs(16));
            DrawRect(new Rect2(ca.X * U, y - 18 * U, sz.X + 20 * U, 24 * U), new Color(0.02f, 0.03f, 0.05f, 0.72f * a));
            DrawString(_font, new Vector2(ca.X * U + 10 * U, y), line, HorizontalAlignment.Left, -1, Fs(16), new Color(0.92f, 0.94f, 0.98f, a));
            y += 26 * U;
        }
    }

    /// <summary>사람 줄 (UI U2 — 아래): 얼굴(정수 배율 · 가운데) · 이름 · 단계 테두리 · 누르면 따라간다 · 넘치면 '+n' (누르면 사람 판).</summary>
    private void DrawCrew(LiveFrame f)
    {
        if (_facesTex == null || _hud.CrewSlots.Count == 0) return;
        float pulse = 0.6f + 0.4f * Mathf.Sin(Godot.Time.GetTicksMsec() / 1000f * Mathf.Tau * 0.8f);
        int fs = Math.Max(1, (int)Math.Floor(_hud.FaceScale * U));
        foreach (var (id, rf) in _hud.CrewSlots)
        {
            var c = f.Crew.FirstOrDefault(k => k.Id == id);
            var r = R(rf);
            DrawRect(r, new Color(0.03f, 0.04f, 0.07f, 0.82f));
            var src = new Rect2(c.Slot % f.FacesPerRow * Painter.FaceW, c.Slot / f.FacesPerRow * Painter.FaceH, Painter.FaceW, Painter.FaceH);
            var size = new Vector2(Painter.FaceW * fs, Painter.FaceH * fs);
            var dst = new Rect2(new Vector2(MathF.Round(r.Position.X + (r.Size.X - size.X) / 2), MathF.Round(r.Position.Y + 2 * U)), size);
            DrawTextureRectRegion(_facesTex, dst, src, c.Level == 3 ? new Color(0.45f, 0.45f, 0.5f, 0.8f) : Colors.White);
            var edge = c.Id == f.Selected ? new Color(1f, 0.86f, 0.4f)
                : c.Level switch { 3 => new Color(0.3f, 0.3f, 0.34f), 2 => new Color(1f, 0.35f, 0.3f, pulse), 1 => new Color(1f, 0.76f, 0.32f), _ => new Color(0.4f, 0.52f, 0.7f, 0.7f) };
            bool hover = rf.Contains(_mouse.X / U, _mouse.Y / U);
            DrawRect(r, hover ? new Color(0.7f, 0.82f, 1f) : edge, false, c.Id == f.Selected || c.Level == 2 || hover ? 2 : 1);
            if (_hud.CrewNames)
            {
                string nm = c.Name.Length > 4 ? c.Name[..4] : c.Name;
                DrawString(_font, new Vector2(r.Position.X, r.End.Y - 5 * U), nm, HorizontalAlignment.Center, r.Size.X, Fs(14), c.Level == 3 ? new Color(0.5f, 0.5f, 0.55f) : new Color(0.86f, 0.9f, 0.96f));
            }
        }
        foreach (var h in _hud.Hits) if (h.Act == UiAct.CrewMore) DrawButton(h, $"+{_hud.CrewMore}", 14);
        DrawRobots(f);
    }

    /// <summary>로봇 칩 줄: 칩 글 (굵게) + 하는 일 낱말 · 테두리는 형편 (멈춤 회색 · 못 움직임 붉게 깜빡 · 전지 적음 호박색) · 고른 로봇은 금빛.</summary>
    private void DrawRobots(LiveFrame f)
    {
        float pulse = 0.6f + 0.4f * Mathf.Sin(Godot.Time.GetTicksMsec() / 1000f * Mathf.Tau * 0.8f);
        foreach (var h in _hud.Hits)
        {
            if (h.Act == UiAct.RobotMore) { DrawButton(h, $"+{f.Robots.Length - _hud.Hits.Count(x => x.Act == UiAct.Robot)}", 13); continue; }
            if (h.Act != UiAct.Robot) continue;
            var c = f.Robots.FirstOrDefault(x => x.Id == h.Key);
            var r = R(h.R);
            bool hover = h.R.Contains(_mouse.X / U, _mouse.Y / U);
            DrawRect(r, new Color(0.03f, 0.04f, 0.07f, 0.82f));
            var edge = h.On ? new Color(1f, 0.86f, 0.4f) : c.Level switch { 3 => new Color(0.3f, 0.3f, 0.34f), 2 => new Color(1f, 0.35f, 0.3f, pulse), 1 => new Color(1f, 0.76f, 0.32f), _ => new Color(0.45f, 0.75f, 0.85f, 0.75f) };
            DrawRect(r, hover ? new Color(0.7f, 0.82f, 1f) : edge, false, h.On || c.Level == 2 || hover ? 2 : 1);
            var ink = c.Level == 3 ? new Color(0.5f, 0.5f, 0.55f) : Ink;
            float tagW = MeasureW(c.Tag, Fs(13), true);
            DrawString(_bold, new Vector2(r.Position.X + 9 * U, r.End.Y - 8 * U), c.Tag, HorizontalAlignment.Left, -1, Fs(13), c.Level == 3 ? ink : new Color(0.6f, 0.88f, 0.95f));
            DrawString(_font, new Vector2(r.Position.X + 9 * U + tagW + 6 * U, r.End.Y - 8 * U), c.Word, HorizontalAlignment.Left, -1, Fs(13), ink);
        }
    }

    /// <summary>주컴퓨터 말 창 (탭 밑 오른쪽): 머리 '주컴퓨터' + 줄 셋 (맡김 · 막힘은 호박색 점 · 봉쇄 · 막힌 곳은 붉은 점) · 누르면 주컴퓨터 판.</summary>
    private void DrawTalk(LiveFrame f)
    {
        if (f.Talk.Length == 0 || !_hud.Areas.TryGetValue("말 창", out var a)) return;
        var r = R(a);
        float alpha = f.Talk.Max(t => t.Alpha);
        bool hover = a.Contains(_mouse.X / U, _mouse.Y / U);
        DrawRect(r, new Color(0.03f, 0.04f, 0.07f, 0.86f * Math.Max(alpha, 0.3f)));
        DrawRect(r, hover ? new Color(0.7f, 0.82f, 1f) : new Color(0.45f, 0.6f, 0.85f, 0.7f * alpha), false, hover ? 2 : 1);
        DrawIcon("computer", r.Position + new Vector2(18, 17) * U, 7 * U, new Color(0.6f, 0.88f, 0.95f, alpha));
        DrawString(_bold, r.Position + new Vector2(34, 23) * U, "주컴퓨터", HorizontalAlignment.Left, -1, Fs(14), new Color(0.6f, 0.88f, 0.95f, alpha));
        for (int i = 0; i < f.Talk.Length; i++)
        {
            var t = f.Talk[i];
            float y = r.Position.Y + (34 + i * 24) * U;
            if (t.Level > 0) DrawCircle(new Vector2(r.Position.X + 14 * U, y + 12 * U), 3.5f * U, t.Level == 2 ? new Color(1f, 0.4f, 0.35f, t.Alpha) : new Color(1f, 0.76f, 0.32f, t.Alpha));
            DrawString(_font, new Vector2(r.Position.X + 24 * U, y + 17 * U), t.Text, HorizontalAlignment.Left, r.Size.X - 32 * U, Fs(14), new Color(0.86f, 0.9f, 0.96f, t.Alpha));
        }
    }

    /// <summary>보기 묶음 (오른쪽 아래): 연출 따라가기 (손으로 옮겼으면 남은 초 고리) · 배 전체 · 축소 · 확대 · 겹쳐 보기 · 방금 장면.</summary>
    private void DrawViewGroup(LiveFrame f)
    {
        foreach (var h in _hud.Hits)
        {
            var c = R(h.R).GetCenter(); var col = h.Enabled ? Ink : new Color(Ink, 0.4f);
            switch (h.Act)
            {
                case UiAct.Director:
                    DrawButton(h, "");
                    DrawRect(new Rect2(c + new Vector2(-9, -5) * U, new Vector2(12, 10) * U), col, false, 1.5f * U);
                    DrawColoredPolygon(new[] { c + new Vector2(3, -2) * U, c + new Vector2(9, -5) * U, c + new Vector2(9, 5) * U, c + new Vector2(3, 2) * U }, col);
                    if (f.HandLeft > 0) DrawArc(c, 17 * U, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (float)(f.HandLeft / LiveShow.HandHoldSec), 24, new Color(1f, 0.78f, 0.35f), 2 * U);
                    break;
                case UiAct.Overview:
                    DrawButton(h, "");
                    for (int i = 0; i < 4; i++) { float sx = i % 2 == 0 ? -1 : 1, sy = i < 2 ? -1 : 1; var p = c + new Vector2(sx * 8, sy * 8) * U; DrawLine(p, p - new Vector2(sx * 5, 0) * U, col, 2 * U); DrawLine(p, p - new Vector2(0, sy * 5) * U, col, 2 * U); }
                    break;
                case UiAct.ZoomOut: DrawButton(h, ""); DrawRect(new Rect2(c + new Vector2(-8, -1.2f) * U, new Vector2(16, 2.5f) * U), col); break;
                case UiAct.ZoomIn: DrawButton(h, ""); DrawRect(new Rect2(c + new Vector2(-8, -1.2f) * U, new Vector2(16, 2.5f) * U), col); DrawRect(new Rect2(c + new Vector2(-1.2f, -8) * U, new Vector2(2.5f, 16) * U), col); break;
                case UiAct.OverlayMenu when h.Key == 4:
                    DrawButton(h, "");
                    DrawColoredPolygon(new[] { c + new Vector2(0, -9) * U, c + new Vector2(9, -3) * U, c + new Vector2(0, 3) * U, c + new Vector2(-9, -3) * U }, new Color(col, 0.5f));
                    DrawPolyline(new[] { c + new Vector2(-9, 2) * U, c + new Vector2(0, 8) * U, c + new Vector2(9, 2) * U }, col, 1.5f * U);
                    break;
                case UiAct.ReplayLast:
                    DrawButton(h, "");
                    DrawArc(c, 8 * U, -Mathf.Pi * 0.3f, Mathf.Pi * 1.4f, 18, col, 2 * U);
                    DrawColoredPolygon(new[] { c + new Vector2(5, -10) * U, c + new Vector2(10, -4) * U, c + new Vector2(3, -3) * U }, col);
                    break;
            }
        }
    }

    private void DrawLegend(LiveFrame f)
    {
        var r = R(_hud.Area("범례"));
        DrawRect(r, new Color(0.03f, 0.04f, 0.07f, 0.88f));
        DrawRect(r, new Color(0.45f, 0.6f, 0.85f, 0.8f), false, 1);
        DrawMultilineString(_font, new Vector2(r.Position.X + 12 * U, r.Position.Y + 8 * U + Fs(15) * 0.9f), "겹쳐 보기: " + f.Legend, HorizontalAlignment.Left, r.Size.X - 24 * U, Fs(15), 2, new Color(0.85f, 0.9f, 1f));
    }

    /// <summary>판 (오른쪽): 머리 (제목 · [사슬 선] · ×) · 출항 준비 줄 · 보이는 줄 (올리면 밝힘 · 누르면 그 줄 · ▶) · 넘김 막대 · 바닥 글.</summary>
    private void DrawPanel(LiveFrame f)
    {
        var box = R(_hud.Area("판"));
        DrawRect(box, new Color(0.03f, 0.04f, 0.07f, 0.92f));
        DrawRect(box, new Color(0.55f, 0.7f, 0.95f, 0.8f), false, 1.5f);
        float inner = box.Size.X - 54 * U;
        if (f.Detail != DetailKind.None) DrawDetailHead(f, box);
        else DrawString(_bold, new Vector2(box.Position.X + 18 * U, box.Position.Y + 32 * U), f.PanelTitle, HorizontalAlignment.Left, inner - 180 * U, Fs(20), new Color(0.9f, 0.94f, 1f));
        foreach (var h in _hud.Hits)
        {
            if (h.Act == UiAct.PanelClose) { DrawButton(h, ""); DrawCross(R(h.R).GetCenter(), 7 * U, Ink); }
            else if (h.Act == UiAct.ChainLines) DrawButton(h, "사슬 선 보기", 14);
            else if (h.Act == UiAct.DetailBack) { DrawButton(h, "목록", 14); DrawChevron(R(h.R).Position + new Vector2(14 * U, R(h.R).Size.Y / 2), 4 * U, -1, InkDim); }
            else if (h.Act == UiAct.DetailFollow) DrawButton(h, f.DetailFollow ? "따라가는 중" : "따라가기", 14);
            else if (h.Act == UiAct.DetailTab && h.Key < f.DetailTabs.Length) DrawButton(h, f.DetailTabs[h.Key], 15);
        }
        if (_hud.Areas.TryGetValue("겨눔 줄", out var ar))
        {
            // 겨눔 줄 (호박색 바탕): 겨누는 갈래 · 할 일 · 기다리는 손
            var r = R(ar);
            DrawRect(r, new Color(0.24f, 0.15f, 0.04f, 0.95f));
            DrawRect(r, new Color(1f, 0.72f, 0.3f, 0.9f), false, 1.5f);
            string t = f.Armed == HandKind.None ? "" : $"겨누는 중: {LiveShow.HandName(f.Armed)}{(LiveShow.HandSized(f.Armed) ? " · " + LiveShow.HandSizeNames[Math.Min((int)f.ArmedSize, 2)] : "")} — {(LiveShow.HandAimless(f.Armed) ? "[일으키기]를 누른다" : f.Armed == HandKind.Shove ? "물건 위에서 눌러 끌고 놓는다" : "배 위를 누른다")}";
            if (f.HandWaiting > 0) t += (t.Length > 0 ? " · " : "") + $"기다리는 손 {f.HandWaiting} — 시간이 가면 일어난다";
            float bw = 0; foreach (var h in _hud.Hits) if (h.Act is UiAct.HandCancel or UiAct.HandNow) bw += (h.R.W + 6) * U;
            DrawString(_font, new Vector2(r.Position.X + 10 * U, r.Position.Y + r.Size.Y / 2 + Fs(15) * 0.36f), t, HorizontalAlignment.Left, Math.Max(10, r.Size.X - 20 * U - bw), Fs(15), new Color(1f, 0.9f, 0.7f));
            foreach (var h in _hud.Hits)
            {
                if (h.Act == UiAct.HandCancel) DrawButton(h, "그만두기");
                else if (h.Act == UiAct.HandNow) DrawButton(h, "일으키기", 15, true);
            }
        }
        if (_hud.Areas.TryGetValue("출항 줄", out var ho))
        {
            var r = R(ho);
            DrawRect(r, new Color(0.06f, 0.12f, 0.18f, 0.95f));
            string t = double.IsNaN(f.DepartIn) ? "출항 준비" : $"출항까지 {HoursMin(f.DepartIn)} · 항로를 누르면 바꾼다";
            DrawString(_font, new Vector2(r.Position.X + 10 * U, r.Position.Y + r.Size.Y / 2 + Fs(15) * 0.36f), t, HorizontalAlignment.Left, r.Size.X - 180 * U, Fs(15), Ink);
            foreach (var h in _hud.Hits) if (h.Act == UiAct.Depart && h.R.Y >= ho.Y && h.R.Bottom <= ho.Bottom + 1) DrawButton(h, "이대로 출항");
        }
        var mouse = _mouse;
        foreach (var (i, rf) in _hud.PanelLines)
        {
            var row = f.PanelRows[i];
            var r = R(rf);
            if (i == f.PanelPick) DrawRect(r, new Color(0.3f, 0.45f, 0.75f, 0.35f));
            else if (rf.Contains(mouse.X / U, mouse.Y / U) && row.Key != 0) DrawRect(r, new Color(0.3f, 0.45f, 0.75f, 0.16f));
            float x0 = r.Position.X + 12 * U, y = r.Position.Y;
            float lx = 0;
            if (row.Face >= 0 && _facesTex != null)
            {
                lx = 56 * U;
                int fs = Math.Max(1, (int)Math.Floor(2 * U));
                var src = new Rect2(row.Face % f.FacesPerRow * Painter.FaceW, row.Face / f.FacesPerRow * Painter.FaceH, Painter.FaceW, Painter.FaceH);
                var dst = new Rect2(x0, y + 2 * U, Painter.FaceW * fs, Painter.FaceH * fs);
                DrawRect(dst.Grow(2), new Color(0.06f, 0.08f, 0.12f, 1f));
                DrawTextureRectRegion(_facesTex, dst, src, row.Bars == null ? new Color(0.45f, 0.45f, 0.5f, 0.8f) : Colors.White);
                DrawRect(dst.Grow(2), row.On ? new Color(1f, 0.35f, 0.3f) : new Color(0.4f, 0.52f, 0.7f, 0.7f), false, 1);
            }
            bool act = row.Act.Length > 0;
            bool dim = f.Panel == PanelKind.Route && row.Key >= 1 && !row.ActOn || f.Panel == PanelKind.Hand && row.Key >> 40 == 5 && !row.ActOn;
            var head = dim ? new Color(0.55f, 0.58f, 0.66f) : row.On ? new Color(1f, 0.88f, 0.55f) : new Color(0.9f, 0.92f, 0.97f);
            float mainW = r.Size.X - 24 * U - lx - (act ? 104 * U : 0);
            DrawMultilineString(_bold, new Vector2(x0 + lx, y + 6 * U + Fs(16) * 0.9f), (row.On ? "● " : "") + row.Main, HorizontalAlignment.Left, mainW, Fs(16), 2, head);
            float mh = MeasureHB((row.On ? "● " : "") + row.Main, Fs(16), mainW, 2);
            float ty = y + 10 * U + mh;
            if (row.Bars is { Length: > 0 } bars)
            {
                float bw = Math.Min(110 * U, (r.Size.X - 24 * U - lx) / bars.Length - 8 * U), bx = x0 + lx;
                foreach (var bar in bars)
                {
                    DrawString(_font, new Vector2(bx, ty + 11 * U), bar.Name, HorizontalAlignment.Left, -1, Fs(13), InkDim);
                    float lw = MeasureW(bar.Name, Fs(13), false) + 4 * U;
                    var track = new Rect2(bx + lw, ty + 4 * U, bw - lw, 7 * U);
                    DrawRect(track, new Color(0.12f, 0.15f, 0.2f));
                    var fill = bar.Level switch { 2 => new Color(1f, 0.38f, 0.32f), 1 => new Color(1f, 0.76f, 0.32f), _ => new Color(0.45f, 0.82f, 0.62f) };
                    DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * bar.Value, track.Size.Y)), fill);
                    bx += bw + 8 * U;
                }
                ty += 16 * U;
            }
            if (row.Sub.Length > 0)
                DrawMultilineString(_font, new Vector2(x0 + lx, ty + Fs(15) * 0.95f), row.Sub, HorizontalAlignment.Left, r.Size.X - 24 * U - lx, Fs(15), 8, new Color(0.68f, 0.73f, 0.83f));
        }
        foreach (var h in _hud.Hits)
        {
            if (h.Act == UiAct.PanelRowAct) DrawButton(h, "▶ 다시보기", 14);
            else if (h.Act == UiAct.HandSize) DrawButton(h, LiveShow.HandSizeNames[(int)((h.Key - LiveShow.KeyHandSize(HandKind.None, 0)) % 4)], 14);
        }
        if (_hud.Areas.TryGetValue("막대", out var bf))
        {
            var bar = R(bf);
            float track = bar.Size.Y;
            int total = Math.Max(1, _hud.PanelTotal), vis = Math.Max(1, _hud.PanelLast - _hud.PanelFirst);
            float len = Math.Max(24 * U, track * vis / total), at = track * _hud.PanelFirst / total;
            DrawRect(new Rect2(bar.Position.X + bar.Size.X / 2 - 3 * U, bar.Position.Y, 6 * U, track), new Color(0.12f, 0.15f, 0.2f, 0.9f));
            DrawRect(new Rect2(bar.Position.X + bar.Size.X / 2 - 3 * U, bar.Position.Y + at, 6 * U, Math.Min(len, track - at)), new Color(0.55f, 0.68f, 0.9f, 0.95f));
        }
        DrawString(_font, new Vector2(box.Position.X + 18 * U, box.End.Y - 12 * U), f.PanelFoot, HorizontalAlignment.Left, box.Size.X - 36 * U, Fs(15), new Color(0.6f, 0.68f, 0.8f));
    }

    /// <summary>자세한 판 머리 (docs/UI2.md §5.2): 얼굴 (사람 · 정수 배율) · 이름 굵게 · 둘째 줄 옅게 — 단추 · 작은 탭은 손잡이로 그린다.</summary>
    private void DrawDetailHead(LiveFrame f, Rect2 box)
    {
        float x = box.Position.X + 18 * U, y = box.Position.Y;
        if (f.DetailFace >= 0 && _facesTex != null)
        {
            int fs = Math.Max(1, (int)Math.Floor(2 * U));
            var src = new Rect2(f.DetailFace % f.FacesPerRow * Painter.FaceW, f.DetailFace / f.FacesPerRow * Painter.FaceH, Painter.FaceW, Painter.FaceH);
            var dst = new Rect2(box.Position.X + 12 * U, y + 8 * U, Painter.FaceW * fs, Painter.FaceH * fs);
            DrawRect(dst.Grow(2), new Color(0.06f, 0.08f, 0.12f, 1f));
            DrawTextureRectRegion(_facesTex, dst, src, Colors.White);
            DrawRect(dst.Grow(2), new Color(0.4f, 0.52f, 0.7f, 0.7f), false, 1);
            x = box.Position.X + 72 * U;
        }
        float right = box.End.X - 40 * U;
        foreach (var h in _hud.Hits) if (h.Act is UiAct.DetailFollow or UiAct.DetailBack && h.R.Y < _hud.Area("판").Y + 56) right = Math.Min(right, R(h.R).Position.X);
        DrawString(_bold, new Vector2(x, y + 28 * U), f.PanelTitle, HorizontalAlignment.Left, Math.Max(10, right - x - 8 * U), Fs(20), new Color(0.9f, 0.94f, 1f));
        DrawString(_font, new Vector2(x, y + 49 * U), f.DetailLine, HorizontalAlignment.Left, Math.Max(10, box.End.X - x - 16 * U), Fs(15), InkDim);
    }

    /// <summary>펼친 목록 (고정 빠르기 · 겹쳐 보기 · 메뉴 · 배 바꾸기).</summary>
    private void DrawOpen(LiveFrame f)
    {
        foreach (var name in new[] { "목록 고정", "목록 겹쳐 보기", "목록 메뉴", "목록 배" })
        {
            if (!_hud.Areas.TryGetValue(name, out var a)) continue;
            var r = R(a);
            DrawRect(r, new Color(0.03f, 0.04f, 0.07f, 0.96f));
            DrawRect(r, new Color(0.55f, 0.7f, 0.95f, 0.9f), false, 1.5f);
        }
        foreach (var h in _hud.Hits)
        {
            switch (h.Act)
            {
                case UiAct.Fixed: DrawButton(h, $"×{LiveShow.FixedGameSecPerSec[h.Key]:0}"); break;
                case UiAct.Overlay:
                    DrawButton(h with { On = false }, "");
                    DrawString(h.On ? _bold : _font, R(h.R).Position + new Vector2(14, 22) * U, (h.On ? "● " : "") + HudLayout.OverlayWords[h.Key], HorizontalAlignment.Left, R(h.R).Size.X - 20 * U, Fs(15), Ink);
                    break;
                case UiAct.MenuShip or UiAct.MenuRestart or UiAct.MenuStart or UiAct.Sound or UiAct.Captions or UiAct.Speech or UiAct.Thoughts or UiAct.Help when h.Key >= 100:
                {
                    string t = h.Act switch
                    {
                        UiAct.MenuShip => "배 바꾸기  ›", UiAct.MenuRestart => "같은 배로 새 항해", UiAct.MenuStart => "처음 화면으로",
                        UiAct.Sound => _hs.Mute switch { 0 => "소리: 다 켬", 1 => "소리: 음악 끔", _ => "소리: 끔" }, UiAct.Captions => f.CaptionsOn ? "소리 자막: 켬" : "소리 자막: 끔",
                        UiAct.Speech => f.SpeechOn ? "말풍선: 켬" : "말풍선: 끔", UiAct.Thoughts => ThoughtWords[Math.Clamp(f.ThoughtsOn, 0, 2)], _ => "도움말",
                    };
                    DrawButton(h with { On = false }, "");
                    DrawString(_font, R(h.R).Position + new Vector2(14, 24) * U, t, HorizontalAlignment.Left, R(h.R).Size.X - 20 * U, Fs(15), Ink);
                    break;
                }
                case UiAct.ShipPick:
                    DrawButton(h with { On = false }, "");
                    DrawString(_font, R(h.R).Position + new Vector2(14, 24) * U, h.Key < _shipNames.Length ? _shipNames[h.Key] : "", HorizontalAlignment.Left, R(h.R).Size.X - 20 * U, Fs(15), Ink);
                    break;
            }
        }
    }

    /// <summary>풀이 글: 손잡이에 0.35초 머물면 (끄는 동안은 띄우지 않는다) — 커서 + (16, 20) · 아래쪽이면 커서 위.</summary>
    private void DrawTip()
    {
        if (_dragging && _dragDist >= 6 || _clock - _hs.HoverSince < 0.35) return;
        var m = _mouse;
        var r = _hud.At(m.X / U, m.Y / U);
        string tip = r.Hit == null ? "" : r.Hit.Enabled ? r.Hit.Tip : (r.Hit.Why.Length > 0 ? r.Hit.Why : r.Hit.Tip);
        if (r.Hit == null && r.Kind == RouteKind.World && _shown is { HoverId: >= 0 } f && f.HoverDoing.Length > 0) tip = $"{f.HoverName} — {f.HoverDoing}";
        if (string.IsNullOrEmpty(tip)) return;
        float maxW = 420 * U;
        var sz = _font.GetMultilineStringSize(tip, HorizontalAlignment.Left, maxW, Fs(14), 4);
        var vp = GetViewportRect().Size;
        var pos = m + new Vector2(16, 20) * U;
        if (pos.X + sz.X + 20 * U > vp.X) pos.X = vp.X - sz.X - 24 * U;
        if (pos.Y + sz.Y + 14 * U > vp.Y) pos.Y = m.Y - sz.Y - 20 * U;
        var box = new Rect2(pos, new Vector2(sz.X + 20 * U, sz.Y + 12 * U));
        DrawRect(box, new Color(0.03f, 0.04f, 0.07f, 0.95f));
        DrawRect(box, new Color(0.55f, 0.7f, 0.95f, 0.8f), false, 1);
        DrawMultilineString(_font, new Vector2(pos.X + 10 * U, pos.Y + 6 * U + Fs(14) * 0.9f), tip, HorizontalAlignment.Left, maxW, Fs(14), 4, Ink);
    }

    private void DrawHelp(Vector2 vp)
    {
        var a = R(_hud.Area("창 도움말"));
        DrawRect(new Rect2(Vector2.Zero, vp), new Color(0, 0, 0, 0.45f));
        DrawRect(a, new Color(0.03f, 0.04f, 0.07f, 0.96f));
        DrawRect(a, new Color(0.45f, 0.6f, 0.85f, 0.9f), false, 1.5f);
        DrawString(_bold, a.Position + new Vector2(24, 36) * U, "도움말", HorizontalAlignment.Left, -1, Fs(20), Ink);
        foreach (var h in _hud.Hits) if (h.Act == UiAct.Help && h.Key == 1) { DrawButton(h, ""); DrawCross(R(h.R).GetCenter(), 7 * U, Ink); }
        string[] lines =
        {
            "빠르기 단추   멈춤 · 하루 24분 · 10분 · 3분 · 1분 · 최대 · 실시간 — 일이 나면 연출이 늦춘다 (스페이스 · 1~6)",
            "고정 빠르기 단추   ×1 · ×10 · ×60 · ×600 · ×3600 — 늦추지 않는다 (Shift + 1~5)",
            "끌기 · 가운데 단추 끌기   카메라 옮기기 (W A S D · 화살표)",
            "휠 · 두 번 누르기   커서 쪽으로 확대 / 축소 · 판 위에서는 넘기기 (+ −)",
            "사람 · 로봇 누르기 · 사람 줄 · 로봇 칩 누르기   생각 · 몸 보기 · 따라가기 — 로봇은 하는 일 · 맡은 까닭 · 최근 판단 (카드의 × 로 놓기 · Esc)",
            "주컴퓨터 말 창 (오른쪽 위)   주컴퓨터가 방금 한 말 세 줄 — 누르면 주컴퓨터 판 (J)",
            "오른쪽 누르기   뒤로 — 펼친 목록 → 판 → 고른 이 (Esc)",
            "판 탭   장면 (L) · 항로 (T) · 주컴퓨터 (J) · 사슬 (C) · 사람 (P) — 줄을 눌러 고른다",
            "오른쪽 아래   연출 따라가기 (F) · 배 전체 (Tab) · 축소 · 확대 · 겹쳐 보기 (O) · 방금 장면 다시보기 (B)",
            "겹쳐 보기   공기 · 열 · 연기 · 사슬 선 · 선과 관",
            "위 오른쪽   소리 (M) · 소리 자막 (U) · 도움말 (H) · 메뉴 — 말풍선 (K) · 속마음 풍선: 끔 · 고른 이 · 화면 안 셋 (I)",
            "메뉴   배 바꾸기 (N 두 번) · 같은 배로 새 항해 (R 두 번) · 처음 화면 (Esc 두 번)",
            "띠 · 알림 누르기   그 자리 · 그 판으로",
            $"항로   출항 전에만 여덟 항로 가운데 고른다 · 지금 항로: {_shown?.Route}",
        };
        float y = a.Position.Y + 72 * U;
        foreach (var l in lines)
        {
            float lh = _font.GetMultilineStringSize(l, HorizontalAlignment.Left, a.Size.X - 48 * U, Fs(16), 2).Y + 8 * U;
            if (y + lh > a.End.Y - 8 * U) break;
            DrawMultilineString(_font, new Vector2(a.Position.X + 24 * U, y + Fs(16) * 0.9f), l, HorizontalAlignment.Left, a.Size.X - 48 * U, Fs(16), 2, new Color(0.85f, 0.88f, 0.95f));
            y += lh;
        }
    }

    private void DrawAsk(Vector2 vp)
    {
        var a = R(_hud.Area("창 확인"));
        DrawRect(new Rect2(Vector2.Zero, vp), new Color(0, 0, 0, 0.5f));
        DrawRect(a, new Color(0.03f, 0.04f, 0.07f, 0.97f));
        DrawRect(a, new Color(1f, 0.6f, 0.4f, 0.9f), false, 1.5f);
        string q = _hs.Ask switch
        {
            HudAsk.Ship => $"'{_hs.AskShip}'로 새로 띄울까요?\n지금 항해는 사라집니다.",
            HudAsk.Restart => "같은 배로 새 항해를 띄울까요?\n지금 항해는 사라집니다.",
            _ => "처음 화면으로 갈까요?\n지금 항해는 사라집니다.",
        };
        DrawMultilineString(_bold, a.Position + new Vector2(24, 44) * U, q, HorizontalAlignment.Center, a.Size.X - 48 * U, Fs(17), 3, Ink);
        foreach (var h in _hud.Hits)
        {
            if (h.Act == UiAct.AskNo) DrawButton(h, "그만두기");
            else if (h.Act == UiAct.AskYes) DrawButton(h, _hs.Ask == HudAsk.Start ? "처음 화면으로" : "새로 띄우기", 15, true);
        }
    }

    public const string StartScene = "res://scenes/Start.tscn";

    private bool PanelOpen => _shown != null && _shown.PanelTitle.Length > 0;

    // ───────────────────────── 손 ─────────────────────────

    /// <summary>손잡이 하나를 눌렀다 (마우스 · 키가 같은 길).</summary>
    private void Do(UiHit h)
    {
        var f = _shown;
        if (!h.Enabled) { string why = h.Why.Length > 0 ? h.Why : h.Tip; if (why.Length > 0) _cmds.Enqueue(s => s.Notify(why)); return; }
        long k = h.Key;
        switch (h.Act)
        {
            case UiAct.Pause: _cmds.Enqueue(s => s.TogglePause()); break;
            case UiAct.Speed: { int i = (int)k; _cmds.Enqueue(s => s.SetSpeed(i)); break; }
            case UiAct.FixedMenu: _hs.Open = _hs.Open == HudOpen.Fixed ? HudOpen.None : HudOpen.Fixed; break;
            case UiAct.Fixed: { int i = (int)k; _cmds.Enqueue(s => s.SetFixed(i)); _hs.Open = HudOpen.None; break; }
            case UiAct.Sound: _hs.Mute = (_hs.Mute + 1) % 3; { string t = _hs.Mute switch { 0 => "소리 · 음악 켬", 1 => "음악 끔 (배 안 소리만)", _ => "소리 끔" }; _cmds.Enqueue(s => s.Notify(t)); } break;
            case UiAct.Captions: _cmds.Enqueue(s => { s.Ears.Captions = !s.Ears.Captions; s.Notify(s.Ears.Captions ? "소리 자막 켬 (무엇이 · 어느 쪽에서)" : "소리 자막 끔"); }); break;
            case UiAct.Speech: _cmds.Enqueue(s => { s.SpeechBubbles = !s.SpeechBubbles; s.Notify(s.SpeechBubbles ? "말풍선 켬 — 소리 내어 한 말 · 외침" : "말풍선 끔"); }); break;
            case UiAct.Thoughts: _cmds.Enqueue(s => { s.ThoughtBubbles = (s.ThoughtBubbles + 1) % 3; s.Notify(ThoughtWords[s.ThoughtBubbles]); }); break;
            case UiAct.Help: _hs.Help = k == 0 || k >= 100 ? !_hs.Help : false; _hs.HelpBlink = 0; _hs.Open = HudOpen.None; break;
            case UiAct.Menu: _hs.Open = _hs.Open is HudOpen.Menu or HudOpen.Ships ? HudOpen.None : HudOpen.Menu; break;
            case UiAct.MenuShip: _hs.Open = _hs.Open == HudOpen.Ships ? HudOpen.Menu : HudOpen.Ships; break;
            case UiAct.ShipPick: if (k < _ships.Length) { _hs.Ask = HudAsk.Ship; _hs.AskShip = _shipNames[k]; _askShipAt = (int)k; _hs.AskFocus = 0; _hs.Open = HudOpen.None; } break;
            case UiAct.MenuRestart: _hs.Ask = HudAsk.Restart; _hs.AskFocus = 0; _hs.Open = HudOpen.None; break;
            case UiAct.MenuStart: _hs.Ask = HudAsk.Start; _hs.AskFocus = 0; _hs.Open = HudOpen.None; break;
            case UiAct.AskNo: _hs.Ask = HudAsk.None; break;
            case UiAct.AskYes: AskYes(); break;
            case UiAct.Tab: { var kind = HudLayout.TabKinds[k]; _cmds.Enqueue(s => s.TogglePanel(kind)); break; }
            case UiAct.RouteTab: _cmds.Enqueue(s => { if (s.Panel != PanelKind.Route) s.TogglePanel(PanelKind.Route); }); break;
            case UiAct.PanelClose: _cmds.Enqueue(s => s.ClosePanel()); break;
            case UiAct.DetailTab: { int i = (int)k; _cmds.Enqueue(s => s.SetDetailSub(i)); break; }
            case UiAct.DetailBack: _cmds.Enqueue(s => s.DetailBack()); break;
            case UiAct.DetailFollow: _cmds.Enqueue(s => s.DetailFollow()); break;
            case UiAct.HandSize: _cmds.Enqueue(s => s.HandSizeClick(k)); break;
            case UiAct.HandCancel: _cmds.Enqueue(s => s.Disarm()); break;
            case UiAct.HandNow: _cmds.Enqueue(s => s.HandNow()); break;
            case UiAct.DetailOpen: { int id = f?.Selected ?? -1; _cmds.Enqueue(s => s.OpenDetail(DetailKind.Person, id)); break; }   // 로봇이면 OpenDetail이 로봇 자세히로 고친다
            case UiAct.PanelRow: _cmds.Enqueue(s => s.PanelClick(k)); break;
            case UiAct.PanelRowAct: _cmds.Enqueue(s => s.PanelAct(k)); break;
            case UiAct.ChainLines: { bool on = f?.Show != Overlay.Chain; _cmds.Enqueue(s => s.SetChainLines(on)); break; }
            case UiAct.Depart: _cmds.Enqueue(s => s.Depart()); break;
            case UiAct.CardFollow: _cmds.Enqueue(s => s.FollowAgain()); break;
            case UiAct.CardClose: _cmds.Enqueue(s => s.Release()); break;
            case UiAct.CardName: { int id = f?.Selected ?? -1; _cmds.Enqueue(s => s.CenterOn(id)); break; }
            case UiAct.Crew: { int id = (int)k; _cmds.Enqueue(s => s.SelectAgent(id)); break; }
            case UiAct.CrewMore: _cmds.Enqueue(s => { if (s.Panel != PanelKind.People || s.Detail != DetailKind.None) { s.ClosePanel(); s.TogglePanel(PanelKind.People); } }); break;
            case UiAct.RobotMore: _cmds.Enqueue(s => { if (s.Panel != PanelKind.Robots || s.Detail != DetailKind.None) { s.ClosePanel(); s.TogglePanel(PanelKind.Robots); } }); break;
            case UiAct.Robot: { int id = (int)k; _cmds.Enqueue(s => s.SelectAgent(id)); break; }
            case UiAct.Talk: _cmds.Enqueue(s => { if (s.Panel != PanelKind.Journal) s.TogglePanel(PanelKind.Journal); }); break;
            case UiAct.Feed: if (f != null && k < f.Feed.Length) { int cell = f.Feed[k].Cell; _cmds.Enqueue(s => s.LookAt(cell)); } break;
            case UiAct.Vital: if (f != null && k < f.Vitals.Length) { string icon = f.Vitals[k].Icon; _cmds.Enqueue(s => s.GaugeGo(icon)); } break;
            case UiAct.Notice: _cmds.Enqueue(s => s.NoticeGo()); break;
            case UiAct.BannerReplay: _cmds.Enqueue(s => s.StartReplay()); break;
            case UiAct.Broadcast: _cmds.Enqueue(s => s.BroadcastGo()); break;
            case UiAct.Story: _cmds.Enqueue(s => { if (s.Panel != PanelKind.Chain) s.TogglePanel(PanelKind.Chain); }); break;
            case UiAct.ReplayStop: _cmds.Enqueue(s => s.StopReplay()); break;
            case UiAct.Director: _cmds.Enqueue(s => s.DirectorCam()); break;
            case UiAct.Overview: _cmds.Enqueue(s => s.ToggleOverview()); break;
            case UiAct.ZoomIn: _cmds.Enqueue(s => s.ZoomBy(0.85)); break;
            case UiAct.ZoomOut: _cmds.Enqueue(s => s.ZoomBy(1 / 0.85)); break;
            case UiAct.OverlayMenu: _hs.Open = _hs.Open == HudOpen.Overlay ? HudOpen.None : HudOpen.Overlay; break;
            case UiAct.Overlay: { var o = (Overlay)k; _cmds.Enqueue(s => s.SetOverlay(o)); _hs.Open = HudOpen.None; break; }
            case UiAct.ReplayLast: _cmds.Enqueue(s => { if (s.Replaying) s.StopReplay(); else s.StartReplay(); }); break;
        }
    }

    private int _askShipAt;

    private void AskYes()
    {
        var ask = _hs.Ask;
        _hs.Ask = HudAsk.None;
        switch (ask)
        {
            case HudAsk.Ship: _shipAt = _askShipAt; Launch(_ships[_shipAt], _seed); break;
            case HudAsk.Restart: _seed++; Launch(_ships[_shipAt], _seed); break;
            case HudAsk.Start: GetTree().ChangeSceneToFile(StartScene); break;
        }
    }

    /// <summary>뒤로 (오른쪽 누르기 · 뒤로 단추 · Esc): 확인 창 → 펼친 목록 → 도움말 → 자세히 (목록으로) → 판 → 고른 이. 한 일이 있으면 true.</summary>
    private bool Back()
    {
        if (_shown == null) return false;
        switch (_hs.Back(_shown))
        {
            case BackStep.Disarm: _cmds.Enqueue(s => s.Disarm()); return true;
            case BackStep.CloseDetail: _cmds.Enqueue(s => s.DetailBack()); return true;
            case BackStep.ClosePanel: _cmds.Enqueue(s => s.ClosePanel()); return true;
            case BackStep.Release: _cmds.Enqueue(s => s.Release()); return true;
            case BackStep.None: return false;
            default: return true;
        }
    }

    /// <summary>항해를 버리는 키는 3초 안에 두 번 — 처음엔 알리기만.</summary>
    private bool Twice(ref double at, string what)
    {
        if (_clock - at < 3) { at = -99; return true; }
        at = _clock;
        _cmds.Enqueue(s => s.Notify($"{what} — 키를 한 번 더 누르면 (이 항해는 사라진다)"));
        return false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_shown == null) return;
        if (e is InputEventKey { Pressed: true } key) { OnKey(key); return; }
        if (e is InputEventMouseButton mb) { Mouse(mb); return; }
        if (e is InputEventMouseMotion mm)
        {
            if (!float.IsNaN(_hs.DragBarY) && _hud.Areas.TryGetValue("막대", out var bar))
            {
                float dy = mm.Position.Y / U - _hs.DragBarY;
                int total = Math.Max(1, _hud.PanelTotal), vis = Math.Max(1, _hud.PanelLast - _hud.PanelFirst);
                _hs.PanelFirst = Math.Clamp(_hs.DragBarFirst + (int)Math.Round(dy / Math.Max(1, bar.H) * total), 0, Math.Max(0, total - vis));
                return;
            }
            if (_dragging || _midDrag)
            {
                _dragDist += mm.Relative.Length();
                if (_dragging && _dragDist < 6) return;
                double cellPx = GetViewportRect().Size.X / (_shown.CamW);
                double dx = -mm.Relative.X / cellPx, dy = -mm.Relative.Y / cellPx;
                _cmds.Enqueue(s => s.Pan(dx, dy));
            }
            return;
        }
        if (e is InputEventMagnifyGesture mg)
        {
            var (cx, cy) = CellAt(mg.Position); double fz = 1 / Math.Max(0.2, mg.Factor);
            _cmds.Enqueue(s => s.ZoomAt(fz, cx, cy));
            return;
        }
        if (e is InputEventPanGesture pg)
        {
            var r = _hud.At(pg.Position.X / U, pg.Position.Y / U);
            if (r.Kind != RouteKind.World && _hud.Areas.ContainsKey("판") && _hud.Area("판").Contains(pg.Position.X / U, pg.Position.Y / U)) _hs.Scroll(pg.Delta.Y * 0.5, _hud.PanelTotal, _hud.PanelLast - _hud.PanelFirst);
            else { double cellPx = GetViewportRect().Size.X / (_shown.CamW); double dx = pg.Delta.X * 8 / cellPx, dy = pg.Delta.Y * 8 / cellPx; _cmds.Enqueue(s => s.Pan(dx, dy)); }
        }
    }

    private void Mouse(InputEventMouseButton mb)
    {
        var pos = mb.Position;
        float lx = pos.X / U, ly = pos.Y / U;
        var route = _hud.At(lx, ly);
        switch (mb.ButtonIndex)
        {
            case MouseButton.WheelUp or MouseButton.WheelDown:
            {
                if (!mb.Pressed) return;
                double factor = mb.Factor > 0 ? mb.Factor : 1, dir = mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1;
                if (_hud.Areas.TryGetValue("판", out var pr) && pr.Contains(lx, ly)) { _hs.Scroll(dir * factor, _hud.PanelTotal, _hud.PanelLast - _hud.PanelFirst); return; }
                if (_hs.Open == HudOpen.Overlay && _hud.Area("목록 겹쳐 보기").Contains(lx, ly) && _shown != null) { var o = (Overlay)(((int)_shown.Show + (dir > 0 ? 1 : 5)) % 6); _cmds.Enqueue(s => s.SetOverlay(o)); return; }
                if (route.Kind != RouteKind.World) return;
                var (cx, cy) = CellAt(pos); double fz = Math.Pow(0.9, -dir * factor);
                _cmds.Enqueue(s => s.ZoomAt(fz, cx, cy));
                return;
            }
            case MouseButton.Right or MouseButton.Xbutton1:
                if (!mb.Pressed) Back();
                return;
            case MouseButton.Middle:
                _midDrag = mb.Pressed; _dragDist = 0;
                return;
            case MouseButton.Left:
                if (mb.Pressed)
                {
                    _hs.Press(route);
                    _pressAt = pos; _dragDist = 0; _pressDouble = mb.DoubleClick;
                    _dragging = _hs.PressWorld;
                    if (_hs.PressWorld && _shown is { Armed: HandKind.Shove, Aim: { Ok: true, Obj: > 0 } } sf)
                    {
                        // 밀어 던지기: 물체 위에서 누르면 끌기 (카메라는 움직이지 않는다)
                        _hs.HandDrag = sf.Aim.Obj; _hs.HandDragX = sf.Aim.X; _hs.HandDragY = sf.Aim.Y; _dragging = false;
                    }
                    if (route.Hit is { Act: UiAct.PanelBar } && _hud.Areas.TryGetValue("막대", out var bar))
                    {
                        // 막대: 손잡이를 끌면 넘기고 · 길을 누르면 한 쪽씩
                        int total = Math.Max(1, _hud.PanelTotal), vis = Math.Max(1, _hud.PanelLast - _hud.PanelFirst);
                        float thumbY = bar.Y + bar.H * _hud.PanelFirst / total, thumbH = Math.Max(24, bar.H * vis / total);
                        if (ly >= thumbY && ly <= thumbY + thumbH) { _hs.DragBarY = ly; _hs.DragBarFirst = _hs.PanelFirst; }
                        else _hs.Scroll(ly < thumbY ? -vis : vis, total, vis);
                    }
                    return;
                }
                _hs.DragBarY = float.NaN;
                if (_hs.HandDrag != 0)
                {
                    // 밀어 던지기를 놓았다: 끈 쪽 · 끈 길이 (0.5칸에 1m/s · 8m/s까지)
                    var (ex, ey) = CellAt(pos);
                    double vx = (ex - _hs.HandDragX) * 2, vy = (ey - _hs.HandDragY) * 2, v = Math.Sqrt(vx * vx + vy * vy);
                    if (v > 8) { vx *= 8 / v; vy *= 8 / v; }
                    int id = _hs.HandDrag; _hs.HandDrag = 0; _hs.Release(route); _dragging = false;
                    _cmds.Enqueue(s => s.HandShove(id, vx, vy));
                    return;
                }
                bool wasWorld = _hs.PressWorld;
                var hit = _hs.Release(route);
                bool dragged = _dragDist >= 6;
                _dragging = false;
                if (hit != null) { if (hit.Act != UiAct.PanelBar) Do(hit); return; }
                if (wasWorld && route.Kind == RouteKind.World && !dragged)
                {
                    // 세계를 눌렀다: 올린 사람과 같은 셈으로 찾아 고른다 (없으면 놓는다) · 두 번 누르면 그 칸 쪽으로 확대
                    var (cx, cy) = CellAt(pos);
                    if (_shown!.Armed != HandKind.None) { _cmds.Enqueue(s => s.HandAt(cx, cy)); return; }   // 겨누는 중: 그 자리에 일으킨다 (한 번 누르면 한 번)
                    int id = LiveShow.Nearest(_shown!.Spots, cx, cy);
                    if (id >= 0) _cmds.Enqueue(s => s.SelectAgent(id)); else _cmds.Enqueue(s => s.Release());
                    if (_pressDouble) _cmds.Enqueue(s => s.ZoomAt(0.6, cx, cy));
                }
                return;
        }
    }

    private void OnKey(InputEventKey k)
    {
        // 창이 떠 있으면 먼저 받고 그 밖의 키는 삼킨다
        if (_hs.Ask != HudAsk.None)
        {
            if (k.Echo) return;
            switch (k.Keycode)
            {
                case Godot.Key.Escape: _hs.Ask = HudAsk.None; break;
                case Godot.Key.Left or Godot.Key.Right or Godot.Key.Tab: _hs.AskFocus = 1 - _hs.AskFocus; break;
                case Godot.Key.Enter or Godot.Key.KpEnter: if (_hs.AskFocus == 1) AskYes(); else _hs.Ask = HudAsk.None; break;
            }
            return;
        }
        if (_hs.Open != HudOpen.None && k.Keycode == Godot.Key.Escape && !k.Echo) { _hs.Open = HudOpen.None; return; }
        if (_hs.Help && !k.Echo && k.Keycode is Godot.Key.Escape or Godot.Key.H) { _hs.Help = false; return; }
        if (_hs.Help) return;
        if (PanelOpen)
        {
            // 판이 열려 있다: 화살표 · Enter · Esc는 판에 (누르고 있으면 되풀이)
            switch (k.Keycode)
            {
                case Godot.Key.Up: _cmds.Enqueue(s => s.PanelMove(-1)); return;
                case Godot.Key.Down: _cmds.Enqueue(s => s.PanelMove(+1)); return;
                case Godot.Key.Left when !k.Echo: _cmds.Enqueue(s => s.PanelSide(-1)); return;
                case Godot.Key.Right when !k.Echo: _cmds.Enqueue(s => s.PanelSide(+1)); return;
                case Godot.Key.Enter or Godot.Key.KpEnter when !k.Echo: _cmds.Enqueue(s => s.PanelEnter()); return;
                case Godot.Key.Escape when !k.Echo: Back(); return;   // 자세히 → 목록 → 닫기 (뒤로 차례)
            }
        }
        if (k.Echo) return;
        UiHit Hk(UiAct a, long key = 0) => new(default, a, key);
        switch (k.Keycode)
        {
            case Godot.Key.Space: Do(Hk(UiAct.Pause)); break;
            case >= Godot.Key.Key1 and <= Godot.Key.Key5 when k.ShiftPressed: Do(Hk(UiAct.Fixed, k.Keycode - Godot.Key.Key1)); break;
            case >= Godot.Key.Key1 and <= Godot.Key.Key6: Do(Hk(UiAct.Speed, k.Keycode - Godot.Key.Key1)); break;
            case Godot.Key.F: Do(Hk(UiAct.Director)); break;
            case Godot.Key.B: Do(Hk(UiAct.ReplayLast)); break;
            case Godot.Key.Tab: Do(Hk(UiAct.Overview)); break;
            case Godot.Key.Equal or Godot.Key.KpAdd: Do(Hk(UiAct.ZoomIn)); break;
            case Godot.Key.Minus or Godot.Key.KpSubtract: Do(Hk(UiAct.ZoomOut)); break;
            case Godot.Key.N: if (Twice(ref _nAt, "다음 배로")) { _shipAt = (_shipAt + 1) % Math.Max(1, _ships.Length); Launch(_ships[_shipAt], _seed); } break;
            case Godot.Key.R: if (Twice(ref _rAt, "같은 배를 새로")) { _seed++; Launch(_ships[_shipAt], _seed); } break;
            case Godot.Key.H: Do(Hk(UiAct.Help)); break;
            case Godot.Key.T: Do(Hk(UiAct.Tab, 1)); break;
            case Godot.Key.L: Do(Hk(UiAct.Tab, 0)); break;
            case Godot.Key.J: Do(Hk(UiAct.Tab, 2)); break;
            case Godot.Key.C: Do(Hk(UiAct.Tab, 3)); break;
            case Godot.Key.P: Do(Hk(UiAct.Tab, 4)); break;
            case Godot.Key.G: Do(Hk(UiAct.Tab, 5)); break;
            case Godot.Key.X: Do(Hk(UiAct.Tab, 6)); break;
            case Godot.Key.O: _cmds.Enqueue(s => s.CycleOverlay()); break;
            case Godot.Key.F3: _meter = !_meter; break;
            case Godot.Key.U: Do(Hk(UiAct.Captions)); break;
            case Godot.Key.M: Do(Hk(UiAct.Sound)); break;
            case Godot.Key.K: Do(Hk(UiAct.Speech)); break;
            case Godot.Key.I: Do(Hk(UiAct.Thoughts)); break;
            case Godot.Key.Escape:
                if (Back()) break;   // 펼친 목록 → 판 → 고른 이를 먼저 닫는다
                if (Twice(ref _escAt, "처음 화면으로")) GetTree().ChangeSceneToFile(StartScene);
                break;
        }
    }

    private static Color MoodColor(string mood) => mood switch
    {
        "조짐" => new Color(0.95f, 0.8f, 0.3f),
        "위험" => new Color(1f, 0.55f, 0.2f),
        "결정적 순간" => new Color(1f, 0.25f, 0.25f),
        _ => new Color(0.45f, 0.65f, 0.9f),
    };

    private void DrawCentered(string text, Vector2 at, int size, Color c)
    {
        var sz = _bold.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        DrawString(_bold, at - new Vector2(sz.X / 2, -size / 3f), text, HorizontalAlignment.Left, -1, size, c);
    }

    // ── 글꼴: 같은 프로젝트의 Pretendard · 없으면 시스템 한글 글꼴 ──
    // 숫자는 모두 같은 너비 (tnum) — 시계 · 기압 · 온도가 바뀌어도 글줄이 흔들리지 않는다 (그래픽 결정 14)
    private void LoadFonts()
    {
        _font = Tabular(LoadFont("Pretendard-Medium.otf", 500));
        _bold = Tabular(LoadFont("Pretendard-Bold.otf", 700));
        ThemeDB.FallbackFont = _font;
    }

    private static Font Tabular(Font f)
    {
        var ts = TextServerManager.GetPrimaryInterface();
        return new FontVariation { BaseFont = f, OpentypeFeatures = new Godot.Collections.Dictionary { [ts.NameToTag("tnum")] = 1 } };
    }

    private static Font LoadFont(string file, int weight)
    {
        string res = "res://assets/fonts/" + file;
        if (ResourceLoader.Exists(res) && GD.Load<Font>(res) is Font f) return f;
        return new SystemFont
        {
            FontNames = new[] { "Pretendard", "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "sans-serif" },
            FontWeight = weight,
        };
    }
}

/// <summary>관전 그림 노드 (LiveMain의 자식 · 부모 뒤에 그린다 — 샤프 확대 셰이더 · 선형 거르기).</summary>
public partial class LiveWorldPic : Node2D
{
    internal LiveMain Owner = null!;
    public override void _Draw() => Owner.DrawWorld(this);
}
