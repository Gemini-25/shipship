using System;
using System.Collections.Concurrent;
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
    private const double CutDipSec = 0.18;
    private double _cutDip;
    private int _gotFrames, _fpsFrames; private double _meterT; private int _picPerSec, _screenPerSec;
    private Vector2 _lastSize;
    private bool _dragging;
    private Vector2 _pressAt; private float _dragDist;

    // ── 소리: 작업 스레드의 귀가 보낸 것을 메인 스레드의 섞개가 받아 그리고, Godot에는 다 섞인 소리만 밀어 넣는다 ──
    private readonly Mixer _mix = new();
    private AudioStreamGeneratorPlayback? _playback;
    private float[] _audio = new float[4096];
    private Vector2[] _push = Array.Empty<Vector2>();
    private int _mute;                                         // M: 0 다 켬 · 1 음악만 끔 · 2 다 끔

    // ── 확인용 (명령 줄: --shots=폴더 --every=초 --for=초) — 몇 초마다 화면을 찍고, 끝나면 프레임 시간을 적고 나간다 ──
    private Overlay _overlay;
    private string? _shotDir;
    private double _shotEvery = 10, _runFor, _runT, _nextShot = 3;
    private int _shotN, _picsTotal, _pick = -1;
    private PanelKind _openPanel;
    private double _skipHours;                                 // --skip=시간: 띄우기 전에 그만큼 앞으로 감는다 (화면 사진용)                              // --panel=L|T|J: 3초 뒤 그 판을 연다 (화면 사진용)
    private readonly System.Collections.Generic.List<double> _frameMs = new();

    public override void _Ready()
    {
        LoadFonts();
        TextureFilter = TextureFilterEnum.Nearest;
        var player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = Dsp.Rate, BufferLength = 0.2f } };
        AddChild(player);
        player.Play();
        _playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _ships = Layout.ShipKeys().ToArray();
        string ship = _ships.Contains("Didim") ? "Didim" : _ships.FirstOrDefault() ?? "Didim";
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--ship=")) ship = a[7..];
            else if (a.StartsWith("--seed=") && ulong.TryParse(a[7..], out var sd)) _seed = sd;
            else if (a.StartsWith("--shots=")) _shotDir = a[8..];
            else if (a.StartsWith("--every=")) _shotEvery = double.Parse(a[8..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--for=")) _runFor = double.Parse(a[6..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--pick=") && int.TryParse(a[7..], out var pk)) _pick = pk;
            else if (a.StartsWith("--skip=")) _skipHours = double.Parse(a[7..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--panel=")) _openPanel = a[8..] switch { "L" => PanelKind.Highlights, "T" => PanelKind.Route, "J" => PanelKind.Journal, "C" => PanelKind.Chain, "P" => PanelKind.People, _ => PanelKind.None };
            else if (a.StartsWith("--overlay=") && Enum.TryParse<Overlay>(a[10..], out var ov)) _overlay = ov;
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
                if (_skipHours > 0) { _show.FastForward(Time.Hours(_skipHours)); _skipHours = 0; }
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
        _cutDip = Math.Max(0, _cutDip - delta);
        var size = GetViewportRect().Size;
        if (size != _lastSize && size.X > 0) { _lastSize = size; _aspect = size.Y / size.X; }
        // 손으로 카메라 옮기기 (누르고 있는 동안)
        var pan = Vector2.Zero;
        bool arrows = !PanelOpen;                                // 판이 열려 있으면 화살표는 판에서 고르기
        if (Input.IsKeyPressed(Key.A) || arrows && Input.IsKeyPressed(Key.Left)) pan.X -= 1;
        if (Input.IsKeyPressed(Key.D) || arrows && Input.IsKeyPressed(Key.Right)) pan.X += 1;
        if (Input.IsKeyPressed(Key.W) || arrows && Input.IsKeyPressed(Key.Up)) pan.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || arrows && Input.IsKeyPressed(Key.Down)) pan.Y += 1;
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
            if (_shown == null || _shown.Ship != f.Ship) DisplayServer.WindowSetTitle($"ShipSim · 새 엔진 판 — {f.Ship}");
            _shown = f;
            if (f.Cut) _cutDip = CutDipSec;   // 카메라가 끊어 넘긴 장: 짧게 어두워졌다 밝아진다
            _gotFrames++; _picsTotal++;
        }
        PumpAudio();
        _fpsFrames++;
        _meterT += delta;
        if (_meterT >= 1) { _picPerSec = _gotFrames; _screenPerSec = _fpsFrames; _gotFrames = _fpsFrames = 0; _meterT = 0; }
        if (_helpLeft > 0) { _helpLeft -= delta; if (_helpLeft <= 0) _help = false; }
        QueueRedraw();
        if (_shotDir != null || _runFor > 0) Check(delta);
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
        _mix.Master = _mute == 2 ? 0 : 0.9f;
        _mix.MusicGain = _mute == 0 ? Mixer.MusicLevel : 0;
        _mix.Render(_audio, frames);
        for (int i = 0; i < frames; i++) _push[i] = new Vector2(_audio[2 * i], _audio[2 * i + 1]);
        _playback.PushBuffer(_push);
    }

    /// <summary>확인용: 화면을 찍고 · 프레임 시간을 모으고 · 정한 시간이 지나면 적고 나간다.</summary>
    private void Check(double delta)
    {
        _runT += delta;
        if (_pick >= 0 && _runT > 3) { int who = _pick; _pick = -1; _cmds.Enqueue(s => { s.Selected = who; s.FollowSelected = true; }); }
        if (_openPanel != PanelKind.None && _runT > 3) { var k = _openPanel; _openPanel = PanelKind.None; _cmds.Enqueue(s => s.TogglePanel(k)); }
        if (_overlay != Overlay.None && _runT > 2) { var o = _overlay; _overlay = Overlay.None; _cmds.Enqueue(s => s.Show = o); }
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
            DrawCentered("새 엔진으로 배를 띄우는 중…", vp / 2, 26, new Color(0.8f, 0.85f, 0.95f));
            return;
        }
        var f = _shown;
        DrawTextureRectRegion(_tex, new Rect2(Vector2.Zero, vp), new Rect2(f.SrcX, f.SrcY, f.SrcW, f.SrcH));
        if (_cutDip > 0) DrawRect(new Rect2(Vector2.Zero, vp), new Color(0, 0, 0, (float)(0.7 * _cutDip / CutDipSec)));   // 컷 전환

        // 위 줄: 배 · 시각 · 분위기 · 빠르기 · 사람
        var mood = MoodColor(f.Mood);
        DrawRect(new Rect2(0, 0, vp.X, 40), new Color(0.03f, 0.04f, 0.07f, 0.82f));
        DrawRect(new Rect2(0, 38, vp.X, 2), new Color(mood, 0.9f));
        DrawCircle(new Vector2(22, 20), 7, mood);
        DrawString(_bold, new Vector2(38, 27), f.Status, HorizontalAlignment.Left, vp.X - 380, 18, new Color(0.92f, 0.94f, 0.98f));
        DrawBadge(new Vector2(vp.X - 248, 20));
        if (f.Replay.Length > 0)
        {
            // 다시보기: 가장자리를 옛 필름 빛으로
            var film = new Color(0.95f, 0.78f, 0.45f, 0.75f);
            DrawRect(new Rect2(0, 40, vp.X, vp.Y - 40), film, false, 4);
        }
        string hint = _speedAt == 0 ? "빠르기: 연출" : $"빠르기: ×{GameSecPerSec[_speedAt]:0} (손)";
        DrawString(_font, new Vector2(vp.X - 230, 27), hint + " · H 도움말", HorizontalAlignment.Right, 214, 15, new Color(0.65f, 0.7f, 0.8f));

        // 함장이 항로를 골랐다 (몇 초)
        if (f.Notice.Length > 0)
        {
            var sz = _font.GetStringSize(f.Notice, HorizontalAlignment.Left, -1, 17);
            var r = new Rect2(vp.X - sz.X - 40, 48, sz.X + 24, 32);
            DrawRect(r, new Color(0.04f, 0.06f, 0.1f, 0.9f));
            DrawRect(r, new Color(0.5f, 0.65f, 0.9f, 0.9f), false, 1);
            DrawString(_font, new Vector2(r.Position.X + 12, r.Position.Y + 22), f.Notice, HorizontalAlignment.Left, -1, 17, new Color(0.85f, 0.9f, 1f));
        }

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

        // 주컴퓨터 방송 자막 (아침 방송)
        if (f.Broadcast.Length > 0 && f.BroadcastAlpha > 0)
        {
            float bw = Math.Min(vp.X - 80, 980);
            var tsz = _font.GetMultilineStringSize(f.Broadcast, HorizontalAlignment.Left, bw - 40, 17, 3);
            var r = new Rect2(vp.X / 2 - bw / 2, vp.Y - 64 - tsz.Y - 34, bw, tsz.Y + 22);
            DrawRect(r, new Color(0.02f, 0.06f, 0.08f, 0.88f * f.BroadcastAlpha));
            DrawRect(r, new Color(0.45f, 0.85f, 0.9f, 0.85f * f.BroadcastAlpha), false, 1.5f);
            DrawMultilineString(_font, new Vector2(r.Position.X + 20, r.Position.Y + 28), f.Broadcast, HorizontalAlignment.Left, bw - 40, 17, 3, new Color(0.8f, 0.96f, 1f, f.BroadcastAlpha));
        }

        if (f.Card.Length > 0) DrawCard(f, vp);
        if (f.PanelTitle.Length > 0) DrawPanel(f, vp);
        if (_meter) DrawString(_font, new Vector2(vp.X - 340, 60), $"초당 화면 {_screenPerSec}장 · 그림 {_picPerSec}장 · 소리 {_mix.Voices}개", HorizontalAlignment.Right, 324, 14, new Color(0.6f, 0.9f, 0.6f));
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

    /// <summary>판 (하이라이트 목록 · 항로 · 일지): 오른쪽에 · 고른 줄이 늘 보이게 내려 보인다.</summary>
    private void DrawPanel(LiveFrame f, Vector2 vp)
    {
        float w = Math.Min(640, Math.Max(380, vp.X * 0.42f)), top = 52, bottom = vp.Y - 76;
        float inner = w - 36;
        var heights = f.PanelRows.Select(r => 26f + (r.Sub.Length > 0 ? _font.GetMultilineStringSize(r.Sub, HorizontalAlignment.Left, inner, 14, 8).Y + 4 : 0)).ToArray();
        float room = bottom - top - 50 - 34;
        int first = 0;
        float Span(int a, int b) { float t = 0; for (int i = a; i <= b && i < heights.Length; i++) t += heights[i]; return t; }
        if (f.PanelPick >= 0) while (first < f.PanelPick && Span(first, f.PanelPick) > room) first++;
        float used = 0; int last = first;
        while (last < heights.Length && used + heights[last] <= room) { used += heights[last]; last++; }
        float h = 50 + used + 34;
        var box = new Rect2(vp.X - w - 16, top, w, Math.Min(h, bottom - top));
        DrawRect(box, new Color(0.03f, 0.04f, 0.07f, 0.92f));
        DrawRect(box, new Color(0.55f, 0.7f, 0.95f, 0.8f), false, 1.5f);
        DrawString(_bold, new Vector2(box.Position.X + 18, box.Position.Y + 32), f.PanelTitle, HorizontalAlignment.Left, inner, 20, new Color(0.9f, 0.94f, 1f));
        float y = box.Position.Y + 50;
        for (int i = first; i < last; i++)
        {
            var r = f.PanelRows[i];
            if (i == f.PanelPick) DrawRect(new Rect2(box.Position.X + 6, y - 2, w - 12, heights[i]), new Color(0.3f, 0.45f, 0.75f, 0.35f));
            var head = r.On ? new Color(1f, 0.88f, 0.55f) : new Color(0.9f, 0.92f, 0.97f);
            DrawString(_bold, new Vector2(box.Position.X + 18, y + 17), (r.On ? "● " : "") + r.Main, HorizontalAlignment.Left, inner, 16, head);
            if (r.Sub.Length > 0)
                DrawMultilineString(_font, new Vector2(box.Position.X + 18, y + 38), r.Sub, HorizontalAlignment.Left, inner, 14, 8, new Color(0.68f, 0.73f, 0.83f));
            y += heights[i];
        }
        if (first > 0 || last < heights.Length)
            DrawString(_font, new Vector2(box.Position.X + w - 120, box.Position.Y + 32), $"{f.PanelPick + 1}/{heights.Length}", HorizontalAlignment.Right, 100, 14, new Color(0.6f, 0.65f, 0.75f));
        DrawString(_font, new Vector2(box.Position.X + 18, box.End.Y - 12), f.PanelFoot, HorizontalAlignment.Left, inner, 14, new Color(0.6f, 0.68f, 0.8f));
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
            "L  볼 만했던 장면 (골라서 다시보기)",
            "T  항로 (1~8 · 출항 전에 고르기 · 지금 구간)",
            "J  주컴퓨터 (지금 사건 · 배 형편 · 일지)",
            "C  사슬 (지금 장면은 어디서 시작됐나)      P  사람",
            "O  겹쳐 보기: 공기 → 열 → 연기 → 사슬 선 → 끔",
            "M  다 켬 → 음악 끔 → 소리 끔",
            "H  도움말      Esc  처음 화면 (기존 판 · 새 엔진 판 고르기)",
            $"지금 항로: {_shown?.Route}",
        };
        float w = 470, h = 26 + lines.Length * 26;
        var r = new Rect2(vp.X - w - 20, vp.Y - h - 84, w, h);
        DrawRect(r, new Color(0.03f, 0.04f, 0.07f, 0.88f));
        DrawRect(r, new Color(0.35f, 0.45f, 0.6f, 0.8f), false, 1);
        for (int i = 0; i < lines.Length; i++)
            DrawString(_font, new Vector2(r.Position.X + 18, r.Position.Y + 30 + i * 26), lines[i], HorizontalAlignment.Left, w - 30, 16, new Color(0.85f, 0.88f, 0.95f));
    }

    public const string StartScene = "res://scenes/Start.tscn";

    /// <summary>위 줄 오른쪽의 '새 엔진' 표 — 늘 보여 옛 게임과 헷갈리지 않는다 (오른쪽 끝을 at.X에 맞춘다).</summary>
    private void DrawBadge(Vector2 at)
    {
        const string text = "새 엔진 판";
        var sz = _bold.GetStringSize(text, HorizontalAlignment.Left, -1, 15);
        var r = new Rect2(at.X - sz.X - 22, at.Y - 13, sz.X + 22, 26);
        DrawRect(r, new Color(0.1f, 0.42f, 0.38f, 0.95f));
        DrawRect(r, new Color(0.45f, 0.95f, 0.8f, 0.9f), false, 1.5f);
        DrawString(_bold, new Vector2(r.Position.X + 11, at.Y + 6), text, HorizontalAlignment.Left, -1, 15, new Color(0.88f, 1f, 0.95f));
    }

    private bool PanelOpen => _shown != null && _shown.PanelTitle.Length > 0;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } pk && PanelOpen)
        {
            // 판이 열려 있다: 화살표 · Enter · Esc는 판에 (누르고 있으면 되풀이)
            switch (pk.Keycode)
            {
                case Key.Up: _cmds.Enqueue(s => s.PanelMove(-1)); return;
                case Key.Down: _cmds.Enqueue(s => s.PanelMove(+1)); return;
                case Key.Left when !pk.Echo: _cmds.Enqueue(s => s.PanelSide(-1)); return;
                case Key.Right when !pk.Echo: _cmds.Enqueue(s => s.PanelSide(+1)); return;
                case Key.Enter or Key.KpEnter when !pk.Echo: _cmds.Enqueue(s => s.PanelEnter()); return;
                case Key.Escape when !pk.Echo: _cmds.Enqueue(s => s.Panel = PanelKind.None); return;
            }
        }
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
                case Key.T: _cmds.Enqueue(s => s.TogglePanel(PanelKind.Route)); break;
                case Key.L: _cmds.Enqueue(s => s.TogglePanel(PanelKind.Highlights)); break;
                case Key.J: _cmds.Enqueue(s => s.TogglePanel(PanelKind.Journal)); break;
                case Key.C: _cmds.Enqueue(s => s.TogglePanel(PanelKind.Chain)); break;
                case Key.P: _cmds.Enqueue(s => s.TogglePanel(PanelKind.People)); break;
                case Key.O: _cmds.Enqueue(s => s.CycleOverlay()); break;
                case Key.F3: _meter = !_meter; break;
                case Key.M: _mute = (_mute + 1) % 3; { string t = _mute switch { 0 => "소리 · 음악 켬", 1 => "음악 끔 (배 안 소리만)", _ => "소리 끔" }; _cmds.Enqueue(s => s.Notify(t)); } break;
                case Key.Escape: GetTree().ChangeSceneToFile(StartScene); break;
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
