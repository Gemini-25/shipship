using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Godot;
using ShipEngine;
using ShipView;
using Thread = System.Threading.Thread;
using Time = ShipEngine.Time;

namespace ShipLive;

/// <summary>
/// 새 엔진 관전 화면 — 얇은 껍데기 (docs/ENGINE.md §새 화면 2단계).
/// 작업 스레드: 세계를 돌리고(연출이 정한 빠르기) 그린다 (<see cref="LiveShow"/>). 메인 스레드: 다 그려진 그림을 텍스처로 올리고 글만 쓴다 —
/// 세계가 무거운 순간(주컴퓨터가 앞날을 그려 볼 때)에도 화면은 60프레임으로 돈다.
/// 함장의 손은 명령 줄로 작업 스레드에 넘긴다 (두 스레드가 세계를 함께 만지지 않는다).
/// </summary>
public partial class Main : Node2D
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
    private Image? _image;
    private ImageTexture? _tex;
    private LiveFrame? _shown;
    private Font _font = null!, _bold = null!;
    private string[] _ships = Array.Empty<string>();
    private int _shipAt;
    private ulong _seed = 18;
    private int _speedAt;                                      // 0: 연출이 정한다
    private static readonly double[] GameSecPerSec = { 0, 1, 10, 60, 600, 3600 };
    private bool _help = true, _meter;
    private double _helpLeft = 12;
    private int _gotFrames, _fpsFrames; private double _meterT; private int _picPerSec, _screenPerSec;
    private Vector2 _lastSize;
    private bool _dragging;
    private Vector2 _pressAt; private float _dragDist;

    // ── 확인용 (명령 줄: --shots=폴더 --every=초 --for=초) — 몇 초마다 화면을 찍고, 끝나면 프레임 시간을 적고 나간다 ──
    private string? _shotDir;
    private double _shotEvery = 10, _runFor, _runT, _nextShot = 3;
    private int _shotN, _picsTotal, _pick = -1;
    private readonly System.Collections.Generic.List<double> _frameMs = new();

    public override void _Ready()
    {
        LoadFonts();
        TextureFilter = TextureFilterEnum.Nearest;
        _ships = Layout.ShipKeys().ToArray();
        string ship = _ships.Contains("Kestrel") ? "Kestrel" : _ships.FirstOrDefault() ?? "Kestrel";
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--ship=")) ship = a[7..];
            else if (a.StartsWith("--seed=") && ulong.TryParse(a[7..], out var sd)) _seed = sd;
            else if (a.StartsWith("--shots=")) _shotDir = a[8..];
            else if (a.StartsWith("--every=")) _shotEvery = double.Parse(a[8..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--for=")) _runFor = double.Parse(a[6..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--pick=") && int.TryParse(a[7..], out var pk)) _pick = pk;
        }
        if (_shotDir != null) { System.IO.Directory.CreateDirectory(_shotDir); _help = true; _helpLeft = 4; }
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
        var size = GetViewportRect().Size;
        if (size != _lastSize && size.X > 0) { _lastSize = size; _aspect = size.Y / size.X; }
        // 손으로 카메라 옮기기 (누르고 있는 동안)
        var pan = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) pan.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) pan.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) pan.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) pan.Y += 1;
        if (pan != Vector2.Zero && _shown != null)
        {
            double cells = _shown.SrcW / Painter.T * 0.8 * delta;
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
            if (_shown == null || _shown.Ship != f.Ship) DisplayServer.WindowSetTitle($"우주선 관전 — {f.Ship}");
            _shown = f;
            _gotFrames++; _picsTotal++;
        }
        _fpsFrames++;
        _meterT += delta;
        if (_meterT >= 1) { _picPerSec = _gotFrames; _screenPerSec = _fpsFrames; _gotFrames = _fpsFrames = 0; _meterT = 0; }
        if (_helpLeft > 0) { _helpLeft -= delta; if (_helpLeft <= 0) _help = false; }
        QueueRedraw();
        if (_shotDir != null || _runFor > 0) Check(delta);
    }

    /// <summary>확인용: 화면을 찍고 · 프레임 시간을 모으고 · 정한 시간이 지나면 적고 나간다.</summary>
    private void Check(double delta)
    {
        _runT += delta;
        if (_pick >= 0 && _runT > 3) { int who = _pick; _pick = -1; _cmds.Enqueue(s => { s.Selected = who; s.FollowSelected = true; }); }
        if (_shown != null && _runT > 1) _frameMs.Add(delta * 1000);
        if (_shotDir != null && _shown != null && _runT >= _nextShot)
        {
            _nextShot = _runT + _shotEvery;
            var img = GetViewport().GetTexture().GetImage();
            string path = System.IO.Path.Combine(_shotDir, $"{_shotN++:00}-{_runT:000}s.png");
            img.SavePng(path);
            GD.Print($"{path} · {_shown.Status} · {_shown.Story}");
        }
        if (_runFor > 0 && _runT >= _runFor)
        {
            var o = _frameMs.OrderBy(x => x).ToList();
            double P(double q) => o.Count == 0 ? 0 : o[(int)Math.Min(o.Count - 1, q * o.Count)];
            GD.Print($"화면 {_runT:0}초 · 그림 {_picsTotal}장 · 게임 {Time.Clock(_shown?.Tick ?? 0)} · 프레임 {o.Count} · 보통 {P(0.5):0.0}ms · 99% {P(0.99):0.0}ms · 가장 김 {(o.Count > 0 ? o[^1] : 0):0.0}ms · 17ms 넘은 프레임 {o.Count(x => x > 17.5)}");
            _runFor = 0;
            GetTree().Quit();
        }
    }

    public override void _Draw()
    {
        var vp = GetViewportRect().Size;
        if (_tex == null || _shown == null)
        {
            DrawCentered("배를 띄우는 중…", vp / 2, 26, new Color(0.8f, 0.85f, 0.95f));
            return;
        }
        var f = _shown;
        DrawTextureRectRegion(_tex, new Rect2(Vector2.Zero, vp), new Rect2(f.SrcX, f.SrcY, f.SrcW, f.SrcH));

        // 위 줄: 배 · 시각 · 분위기 · 빠르기 · 사람
        var mood = MoodColor(f.Mood);
        DrawRect(new Rect2(0, 0, vp.X, 40), new Color(0.03f, 0.04f, 0.07f, 0.82f));
        DrawRect(new Rect2(0, 38, vp.X, 2), new Color(mood, 0.9f));
        DrawCircle(new Vector2(22, 20), 7, mood);
        DrawString(_bold, new Vector2(38, 27), f.Status, HorizontalAlignment.Left, vp.X - 260, 18, new Color(0.92f, 0.94f, 0.98f));
        if (f.Replay.Length > 0)
        {
            // 다시보기: 가장자리를 옛 필름 빛으로
            var film = new Color(0.95f, 0.78f, 0.45f, 0.75f);
            DrawRect(new Rect2(0, 40, vp.X, vp.Y - 40), film, false, 4);
        }
        string hint = _speedAt == 0 ? "빠르기: 연출" : $"빠르기: ×{GameSecPerSec[_speedAt]:0} (손)";
        DrawString(_font, new Vector2(vp.X - 230, 27), hint + " · H 도움말", HorizontalAlignment.Right, 214, 15, new Color(0.65f, 0.7f, 0.8f));

        // 새 하이라이트
        if (f.Banner.Length > 0 && f.BannerAlpha > 0)
        {
            var sz = _bold.GetStringSize("★ " + f.Banner, HorizontalAlignment.Left, -1, 24);
            var r = new Rect2(vp.X / 2 - sz.X / 2 - 20, 58, sz.X + 40, 46);
            DrawRect(r, new Color(0.08f, 0.05f, 0.03f, 0.85f * f.BannerAlpha));
            DrawRect(r, new Color(1f, 0.72f, 0.3f, 0.9f * f.BannerAlpha), false, 2);
            DrawString(_bold, new Vector2(r.Position.X + 20, r.Position.Y + 31), "★ " + f.Banner, HorizontalAlignment.Left, -1, 24, new Color(1f, 0.86f, 0.6f, f.BannerAlpha));
        }

        // 아래 줄: 지금 장면의 까닭 (원인 사슬) 또는 연출 자막
        if (f.Story.Length > 0)
        {
            DrawRect(new Rect2(0, vp.Y - 64, vp.X, 64), new Color(0.03f, 0.04f, 0.07f, 0.8f));
            DrawMultilineString(_font, new Vector2(20, vp.Y - 38), f.Story, HorizontalAlignment.Left, vp.X - 40, 17, 2, new Color(0.88f, 0.9f, 0.95f));
        }

        if (f.Card.Length > 0) DrawCard(f, vp);
        if (_meter) DrawString(_font, new Vector2(vp.X - 300, 60), $"초당 화면 {_screenPerSec}장 · 그림 {_picPerSec}장", HorizontalAlignment.Right, 284, 14, new Color(0.6f, 0.9f, 0.6f));
        if (_help) DrawHelp(vp);
    }

    /// <summary>고른 사람 · 로봇: 그려진 자리에 고리 · 왼쪽에 카드.</summary>
    private void DrawCard(LiveFrame f, Vector2 vp)
    {
        if (!double.IsNaN(f.SelX))
        {
            float px = (float)((f.SelX - f.CamLeft) / f.CamW * vp.X), py = (float)((f.SelY - f.CamTop) / f.CamH * vp.Y);
            float r = (float)(vp.X / f.CamW * 0.62);
            DrawArc(new Vector2(px, py), r, 0, Mathf.Tau, 40, new Color(1f, 0.9f, 0.45f, 0.9f), 2.5f);
        }
        float w = 430, lh = 25, h = 20 + f.Card.Length * lh + 6;
        var box = new Rect2(16, 56, w, h);
        DrawRect(box, new Color(0.03f, 0.04f, 0.07f, 0.88f));
        DrawRect(box, new Color(1f, 0.9f, 0.45f, 0.7f), false, 1.5f);
        for (int i = 0; i < f.Card.Length; i++)
        {
            bool head = i == 0;
            DrawString(head ? _bold : _font, new Vector2(box.Position.X + 16, box.Position.Y + 28 + i * lh), f.Card[i], HorizontalAlignment.Left, w - 28, head ? 19 : 15,
                head ? new Color(1f, 0.92f, 0.65f) : new Color(0.86f, 0.89f, 0.95f));
        }
    }

    private void DrawHelp(Vector2 vp)
    {
        string[] lines =
        {
            "스페이스  멈춤 / 계속",
            "1 ~ 6  빠르기: 연출 · ×1 · ×10 · ×60 · ×600 · ×3600",
            "W A S D · 화살표 · 끌기  카메라 옮기기",
            "휠 · + −  확대 / 축소",
            "F  연출을 따라가기      Tab  배 전체",
            "B  방금 그 장면 다시보기 (한 번 더 누르면 돌아오기)",
            "사람 · 로봇 누르기  생각 · 몸 보기 (그를 따라간다)",
            "N  다음 배      R  같은 배 새로",
            "H  도움말      Esc  나가기",
        };
        float w = 470, h = 26 + lines.Length * 26;
        var r = new Rect2(vp.X - w - 20, vp.Y - h - 84, w, h);
        DrawRect(r, new Color(0.03f, 0.04f, 0.07f, 0.88f));
        DrawRect(r, new Color(0.35f, 0.45f, 0.6f, 0.8f), false, 1);
        for (int i = 0; i < lines.Length; i++)
            DrawString(_font, new Vector2(r.Position.X + 18, r.Position.Y + 30 + i * 26), lines[i], HorizontalAlignment.Left, w - 30, 16, new Color(0.85f, 0.88f, 0.95f));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            switch (k.Keycode)
            {
                case Key.Space: _cmds.Enqueue(s => s.Paused = !s.Paused); break;
                case >= Key.Key1 and <= Key.Key6:
                {
                    _speedAt = (int)(k.Keycode - Key.Key1);
                    double tps = GameSecPerSec[_speedAt] / Time.SecondsPerTick;
                    _cmds.Enqueue(s => s.SpeedOverride = tps);
                    break;
                }
                case Key.F: _cmds.Enqueue(s => { s.Follow = true; s.Overview = false; s.Selected = -1; s.FollowSelected = false; }); break;
                case Key.B: _cmds.Enqueue(s => { if (s.Replaying) s.StopReplay(); else s.StartReplay(); }); break;
                case Key.Tab: _cmds.Enqueue(s => s.Overview = !s.Overview); break;
                case Key.Equal or Key.KpAdd: _cmds.Enqueue(s => s.ZoomBy(0.85)); break;
                case Key.Minus or Key.KpSubtract: _cmds.Enqueue(s => s.ZoomBy(1 / 0.85)); break;
                case Key.N: _shipAt = (_shipAt + 1) % Math.Max(1, _ships.Length); _speedAt = 0; Launch(_ships[_shipAt], _seed); break;
                case Key.R: _seed++; _speedAt = 0; Launch(_ships[_shipAt], _seed); break;
                case Key.H: _help = !_help; _helpLeft = 0; break;
                case Key.F3: _meter = !_meter; break;
                case Key.Escape: GetTree().Quit(); break;
            }
        }
        else if (e is InputEventMouseButton mb)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) _cmds.Enqueue(s => s.ZoomBy(0.9));
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) _cmds.Enqueue(s => s.ZoomBy(1 / 0.9));
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) { _dragging = true; _pressAt = mb.Position; _dragDist = 0; }
                else
                {
                    _dragging = false;
                    if (_dragDist < 6 && _shown != null)
                    {
                        // 누름: 그 자리의 사람 · 로봇을 고른다 (없으면 놓는다)
                        var vp = GetViewportRect().Size; var f = _shown;
                        double cx = f.CamLeft + mb.Position.X / vp.X * f.CamW, cy = f.CamTop + mb.Position.Y / vp.Y * f.CamH;
                        _cmds.Enqueue(s => s.SelectAt(cx, cy));
                    }
                }
            }
        }
        else if (e is InputEventMouseMotion mm && _dragging && _shown != null)
        {
            _dragDist += mm.Relative.Length();
            if (_dragDist < 6) return;
            double cellPx = GetViewportRect().Size.X / (_shown.SrcW / Painter.T);
            double dx = -mm.Relative.X / cellPx, dy = -mm.Relative.Y / cellPx;
            _cmds.Enqueue(s => s.Pan(dx, dy));
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

    // ── 글꼴: 옛 게임의 Pretendard를 그대로 (저장소 안에서 돌릴 때) · 없으면 시스템 한글 글꼴 ──
    private void LoadFonts()
    {
        _font = LoadFont("Pretendard-Medium.otf", 500);
        _bold = LoadFont("Pretendard-Bold.otf", 700);
        ThemeDB.FallbackFont = _font;
    }

    private static Font LoadFont(string file, int weight)
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "../../game/assets/fonts", file));
        if (System.IO.File.Exists(path))
        {
            var ff = new FontFile();
            if (ff.LoadDynamicFont(path) == Error.Ok) return ff;
        }
        return new SystemFont
        {
            FontNames = new[] { "Pretendard", "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "sans-serif" },
            FontWeight = weight,
        };
    }
}
