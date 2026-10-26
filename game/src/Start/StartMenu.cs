using System;
using System.Linq;
using Godot;
using ShipLive;
using ShipView;

namespace ShipSim.Start;

/// <summary>
/// 처음 화면: 기존 판(옛 게임 — scenes/Main.tscn)과 새 엔진 판(새로 만든 엔진만 — scenes/NewEngine.tscn) 가운데 고른다.
/// 두 판은 서로의 코드를 쓰지 않는다 — 도트 그림만 같다.
/// 새 엔진 판에 저장한 항해가 있으면 판 고르기 위에 이어 보기 카드 (docs/OBSERVER.md §3.3): '디딤호 · 항해 4127 · 43일째 · 6명 가운데 5명' · 어제의 장 · 끈 때 ·
/// [다른 항해 ›] [새 항해 ›] [이어 보기] — 판이 바뀌었으면 '이어 볼 수 없다' · [같은 번호로 새로] [지우기]. 자리는 <see cref="StartLayout"/> (순수 C# — 실험실이 시험).
/// 명령 줄: --new 는 새 엔진 판으로 · --old 는 기존 판으로 바로 간다 · 그 밖의 인자만 있으면 기존 판 (옛 게임의 실행 인자를 그대로 쓰던 대로).
/// 확인용: --menu-shot=파일.png 은 이 화면을 찍고 나간다.
/// </summary>
public partial class StartMenu : Node2D
{
    private const string OldScene = "res://scenes/Main.tscn", NewScene = "res://scenes/NewEngine.tscn";
    private const string LastFile = "user://start_choice.txt";

    private sealed record Choice(string Title, string Lead, string[] Lines, string Scene, Color Accent);

    private Choice[] _choices = Array.Empty<Choice>();
    private readonly Hud _hud = new();
    private readonly HudState _hs = new();
    private readonly StartView _view = new();
    private VoyageStore _store = null!;
    private string[] _shipKeys = Array.Empty<string>(), _shipNames = Array.Empty<string>();
    private UiHit? _press;
    private Font _font = null!, _bold = null!;
    private double _t;
    private string? _shot;
    private Vector2 _mouse;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _shot = args.FirstOrDefault(a => a.StartsWith("--menu-shot="))?[12..];
        if (_shot != null) args = Array.Empty<string>();
        if (args.Contains("--new") || args.Contains("--resume")) { Go(NewScene, remember: false); return; }
        if (args.Contains("--old") || args.Length > 0) { Go(OldScene, remember: false); return; }

        _font = LoadFont("Pretendard-Medium.otf", 500);
        _bold = LoadFont("Pretendard-Bold.otf", 700);
        TextureFilter = TextureFilterEnum.Linear;
        DisplayServer.WindowSetTitle("ShipSim — 판 고르기");
        _choices = new[]
        {
            new Choice("기존 판", "지금까지 만든 게임 그대로",
                new[] { "옛 배 · 옛 화면 · 옛 규칙", "함장 명령 · 기술 · 행성 · 함대", "(새 엔진을 만들기 전의 모든 것)" },
                OldScene, new Color(0.62f, 0.66f, 0.74f)),
            new Choice("새 엔진 판", "새로 만든 엔진만으로 돈다",
                new[] { "새로 설계한 배 " + ShipNames(), "물리 · 화학 · 몸 · 마음 · 주컴퓨터 · 연출 — 모두 새것", "옛 배 · 옛 화면은 쓰지 않는다 (도트 그림만 같다)" },
                NewScene, new Color(0.45f, 0.95f, 0.8f)),
        };
        try { if (FileAccess.FileExists(LastFile) && FileAccess.GetFileAsString(LastFile).Trim() == "old") _view.Pick = 0; } catch { }
        _shipKeys = ShipEngine.Layout.ShipKeys().ToArray();
        _shipNames = _shipKeys.Select(k => { try { var l = ShipEngine.Layout.Ship(k, ShipEngine.Tables.Default); return $"{l.Name} · {l.Furniture.Count(f => f.Type == "Bed")}명"; } catch { return k; } }).ToArray();
        _store = new VoyageStore(ProjectSettings.GlobalizePath("user://voyages"));
        LoadVoyages();
    }

    /// <summary>저장한 항해들을 읽는다 (카드만 — 세계는 읽지 않는다).</summary>
    private void LoadVoyages()
    {
        try { _view.Voyages = _store.List().Take(VoyageStore.KeepVoyages).ToArray(); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { _view.Voyages = Array.Empty<VoyageMeta>(); }
        _view.Latest = _view.Voyages.FirstOrDefault();
        _view.Why = _view.Latest != null ? VoyageStore.Why(_view.Latest) : "";
        _view.ListOpen = false;
    }

    /// <summary>새 엔진에 실린 배 이름 (도면에서 읽는다 — 배가 바뀌어도 맞게).</summary>
    private static string ShipNames()
    {
        try
        {
            var names = ShipEngine.Layout.ShipKeys()
                .Select(k => System.Text.Json.JsonDocument.Parse(ShipEngine.DataFiles.Text($"ships/{k}.json")).RootElement.GetProperty("name").GetString() ?? k)
                .Select(n => n.EndsWith("호") ? n[..^1] : n)
                .ToArray();
            return $"{names.Length}척 ({string.Join(" · ", names)})";
        }
        catch { return "일곱 척"; }
    }

    /// <summary>그 판으로 간다 — 손으로 고른 것만 다음에 밝혀 둔다 (명령 줄로 바로 간 것은 적지 않는다).</summary>
    private void Go(string scene, bool remember = true)
    {
        if (remember)
        {
            try
            {
                using var f = FileAccess.Open(LastFile, FileAccess.ModeFlags.Write);
                f?.StoreString(scene == OldScene ? "old" : "new");
            }
            catch { }
        }
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, scene);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_choices.Length > 0)
        {
            var vp = GetViewportRect().Size;
            StartLayout.Build(_view, _hs, vp.X, vp.Y, Measure, _hud, _shipNames);
            _mouse = GetViewport().GetMousePosition();
            var r = _hud.At(_mouse.X / _hud.U, _mouse.Y / _hud.U);
            Input.SetDefaultCursorShape(r.Hit is { Enabled: true } ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow);
        }
        QueueRedraw();
        if (_shot != null && _t > 1.5)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shot);
            GD.Print($"처음 화면 → {_shot}");
            _shot = null;
            GetTree().Quit();
        }
    }

    private float Measure(string t, int px, bool bold) => (bold ? _bold : _font).GetStringSize(t, HorizontalAlignment.Left, -1, px).X;

    // ───────────────────────── 그리기 ─────────────────────────

    private float U => _hud.U;
    private Rect2 R(RectF r) => new(r.X * U, r.Y * U, r.W * U, r.H * U);
    private int Fs(int size) => Math.Max(13, (int)Math.Round(size * U));
    private static readonly Color Ink = new(0.92f, 0.94f, 0.98f), InkDim = new(0.62f, 0.68f, 0.78f), Mint = new(0.45f, 0.95f, 0.8f);

    private void DrawButton(UiHit h, string text, int size = 16, bool danger = false)
    {
        var r = R(h.R);
        bool hover = h.R.Contains(_mouse.X / U, _mouse.Y / U) && h.Enabled;
        DrawRect(r, h.On ? new Color(0.12f, 0.36f, 0.32f, 0.95f) : new Color(0.06f, 0.08f, 0.12f, hover ? 0.95f : 0.8f));
        DrawRect(r, danger ? new Color(1f, 0.36f, 0.3f) : h.On ? Mint : new Color(0.3f, 0.38f, 0.5f, hover ? 1 : 0.7f), false, 1);
        if (text.Length > 0)
        {
            var col = !h.Enabled ? new Color(Ink, 0.4f) : h.On ? Colors.White : new Color(0.8f, 0.85f, 0.93f);
            DrawString(h.On ? _bold : _font, new Vector2(r.Position.X, r.Position.Y + r.Size.Y / 2 + Fs(size) * 0.36f), text, HorizontalAlignment.Center, r.Size.X, Fs(size), col);
        }
    }

    public override void _Draw()
    {
        if (_choices.Length == 0) return;
        var vp = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, vp), new Color(0.02f, 0.027f, 0.047f));
        // 별
        for (int i = 0; i < 140; i++)
        {
            float x = (float)((i * 7919 % 1000) / 1000.0 * vp.X), y = (float)((i * 104729 % 1000) / 1000.0 * vp.Y);
            float a = 0.25f + 0.35f * (float)(0.5 + 0.5 * Math.Sin(_t * 0.7 + i));
            DrawRect(new Rect2(x, y, i % 9 == 0 ? 2 : 1, i % 9 == 0 ? 2 : 1), new Color(0.8f, 0.85f, 1f, a));
        }
        var title = R(_hud.Area("제목"));
        float cx = vp.X / 2;
        DrawCentered(_bold, "ShipSim", new Vector2(cx, title.Position.Y + Fs(54) * 0.8f), Fs(54), new Color(0.92f, 0.94f, 0.98f));
        DrawCentered(_font, "우주선 한 척을 지켜본다 — 어느 판으로 볼까요?", new Vector2(cx, title.Position.Y + Fs(54) * 0.8f + 46 * U), Fs(20), new Color(0.65f, 0.7f, 0.8f));
        if (_hud.Areas.TryGetValue("이어 보기 카드", out var cardF) && _view.Latest is { } m) DrawResume(R(cardF), m);
        for (int i = 0; i < _choices.Length; i++) DrawChoice(i);
        var tip = R(_hud.Area("풀이"));
        DrawCentered(_font, "판을 눌러 고른다 (1 · 2 · ← → · Enter)", new Vector2(cx, tip.Position.Y + Fs(17)), Fs(17), new Color(0.6f, 0.65f, 0.75f));
        DrawCentered(_font, "새 엔진 판에서는 오른쪽 위 메뉴 → 처음 화면으로 (Esc 두 번) — 그때까지의 항해는 저장해 둔다", new Vector2(cx, tip.Position.Y + Fs(17) + 28 * U), Fs(15), new Color(0.5f, 0.55f, 0.65f));
        foreach (var h in _hud.Hits) if (h.Act == UiAct.StartQuit) DrawButton(h, "나가기 (Esc)");
        if (_hud.Areas.TryGetValue("목록 항해", out var listF)) DrawList(R(listF));
        if (_hs.Ask == HudAsk.NewVoyage) DrawVoyageWindow(vp);
    }

    /// <summary>이어 보기 카드: 첫 줄 (배 · 번호 · 날 · 산 이) · 어제 · 끈 때 · 단추 — 판이 다르면 까닭.</summary>
    private void DrawResume(Rect2 r, VoyageMeta m)
    {
        DrawRect(r, new Color(0.03f, 0.06f, 0.08f, 0.95f));
        DrawRect(r, new Color(Mint, _view.Why.Length > 0 ? 0.4f : 0.9f), false, 2);
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, 4 * U)), new Color(Mint, _view.Why.Length > 0 ? 0.4f : 1f));
        var tr = R(_hud.Area("이어 보기 글"));
        DrawString(_bold, new Vector2(tr.Position.X, r.Position.Y + 38 * U), "이어 보기", HorizontalAlignment.Left, -1, Fs(15), new Color(Mint, 0.85f));
        DrawString(_bold, new Vector2(tr.Position.X + 92 * U, r.Position.Y + 38 * U), VoyageCard.Title(m), HorizontalAlignment.Left, r.Size.X - 140 * U, Fs(21), Ink);
        if (_view.Why.Length > 0)
        {
            DrawString(_font, new Vector2(tr.Position.X, r.Position.Y + 72 * U), _view.Why, HorizontalAlignment.Left, r.Size.X - 48 * U, Fs(16), new Color(1f, 0.75f, 0.55f));
            DrawString(_font, new Vector2(tr.Position.X, r.Position.Y + 104 * U), VoyageCard.When(m), HorizontalAlignment.Left, tr.Size.X, Fs(14), InkDim);
        }
        else
        {
            DrawString(_font, new Vector2(tr.Position.X, r.Position.Y + 72 * U), VoyageCard.Yesterday(m), HorizontalAlignment.Left, r.Size.X - 48 * U, Fs(16), new Color(0.82f, 0.86f, 0.94f));
            DrawString(_font, new Vector2(tr.Position.X, r.Position.Y + 104 * U), VoyageCard.When(m), HorizontalAlignment.Left, tr.Size.X, Fs(14), InkDim);
        }
        foreach (var h in _hud.Hits)
            switch (h.Act)
            {
                case UiAct.StartResume: DrawButton(h, m.End.Length > 0 ? "그대로 지켜보기" : "이어 보기", 16); break;
                case UiAct.StartOther: DrawButton(h, "다른 항해 ›"); break;
                case UiAct.StartNew when h.R.Y < _hud.Area("판 0").Y: DrawButton(h, "새 항해 ›"); break;
                case UiAct.StartRenew: DrawButton(h, "같은 번호로 새로"); break;
                case UiAct.StartDelete: DrawButton(h, "지우기", 16, true); break;
            }
    }

    private void DrawChoice(int i)
    {
        var c = _choices[i];
        var r = R(_hud.Area("판 " + i));
        bool on = i == _view.Pick, hot = r.HasPoint(_mouse);
        DrawRect(r, new Color(0.04f, 0.055f, 0.09f, 0.94f));
        DrawRect(r, new Color(c.Accent, on ? 1f : hot ? 0.7f : 0.35f), false, on ? 3 : 1.5f);
        if (on) DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, 6 * U)), c.Accent);
        DrawString(_bold, new Vector2(r.Position.X + 28 * U, r.Position.Y + 64 * U), $"{i + 1}", HorizontalAlignment.Left, -1, Fs(22), new Color(c.Accent, 0.8f));
        DrawString(_bold, new Vector2(r.Position.X + 60 * U, r.Position.Y + 64 * U), c.Title, HorizontalAlignment.Left, -1, Fs(34), new Color(0.95f, 0.96f, 1f));
        DrawString(_font, new Vector2(r.Position.X + 30 * U, r.Position.Y + 110 * U), c.Lead, HorizontalAlignment.Left, r.Size.X - 56 * U, Fs(20), c.Accent);
        float y = r.Position.Y + 152 * U;
        foreach (var line in c.Lines)
        {
            var sz = _font.GetMultilineStringSize(line, HorizontalAlignment.Left, r.Size.X - 72 * U, Fs(16), 3);
            DrawCircle(new Vector2(r.Position.X + 36 * U, y - 5 * U), 2.5f * U, new Color(c.Accent, 0.8f));
            DrawMultilineString(_font, new Vector2(r.Position.X + 48 * U, y), line, HorizontalAlignment.Left, r.Size.X - 72 * U, Fs(16), 3, new Color(0.8f, 0.84f, 0.92f));
            y += sz.Y + 12 * U;
        }
        foreach (var h in _hud.Hits) if (h.Act == UiAct.StartNew && i == 1 && R(h.R).Position.Y > r.Position.Y) DrawButton(h, "새 항해 ›");
    }

    /// <summary>다른 항해 목록 (다섯까지 — 판이 다른 항해는 옅게).</summary>
    private void DrawList(Rect2 a)
    {
        DrawRect(a, new Color(0.03f, 0.04f, 0.07f, 0.97f));
        DrawRect(a, new Color(0.45f, 0.6f, 0.85f, 0.9f), false, 1.5f);
        foreach (var h in _hud.Hits)
        {
            if (h.Act != UiAct.StartPick || h.Key >= _view.Voyages.Length) continue;
            var m = _view.Voyages[h.Key];
            DrawButton(h with { On = false }, "");
            var r = R(h.R);
            DrawString(_bold, r.Position + new Vector2(14, 22) * U, VoyageCard.Title(m), HorizontalAlignment.Left, r.Size.X - 28 * U, Fs(16), h.Enabled ? Ink : new Color(Ink, 0.4f));
            DrawString(_font, r.Position + new Vector2(14, 42) * U, h.Enabled ? $"{VoyageCard.When(m)} · {VoyageCard.Yesterday(m)}" : h.Why, HorizontalAlignment.Left, r.Size.X - 28 * U, Fs(13), InkDim);
        }
    }

    /// <summary>번호 창 (게임 안 메뉴 [새 항해 ›]와 같은 창 — docs/OBSERVER.md §3.4).</summary>
    private void DrawVoyageWindow(Vector2 vp)
    {
        var a = R(_hud.Area("창 새 항해"));
        DrawRect(new Rect2(Vector2.Zero, vp), new Color(0, 0, 0, 0.5f));
        DrawRect(a, new Color(0.03f, 0.04f, 0.07f, 0.97f));
        DrawRect(a, new Color(Mint, 0.9f), false, 1.5f);
        DrawString(_bold, a.Position + new Vector2(24, 40) * U, "새 항해", HorizontalAlignment.Left, -1, Fs(22), Ink);
        string ship = _shipNames.Length > 0 ? _shipNames[Math.Clamp(_hs.AskShipAt, 0, _shipNames.Length - 1)] : "";
        var sr = R(_hud.Area("새 항해 배"));
        DrawString(_bold, new Vector2(sr.Position.X, sr.Position.Y + sr.Size.Y / 2 + Fs(18) * 0.36f), ship, HorizontalAlignment.Center, sr.Size.X, Fs(18), Ink);
        var nr = R(_hud.Area("새 항해 번호"));
        DrawRect(nr, new Color(0.05f, 0.08f, 0.12f, 0.95f)); DrawRect(nr, _hs.AskNumberOk ? new Color(Mint, 0.9f) : new Color(1f, 0.6f, 0.4f, 0.8f), false, 1.5f);
        string num = _hs.AskNumber.Length > 0 ? "항해 " + _hs.AskNumber : "번호를 넣는다";
        DrawString(_bold, new Vector2(nr.Position.X, nr.Position.Y + nr.Size.Y / 2 + Fs(24) * 0.36f), num, HorizontalAlignment.Center, nr.Size.X, Fs(_hs.AskNumber.Length > 0 ? 24 : 16), _hs.AskNumber.Length > 0 ? Ink : InkDim);
        foreach (var h in _hud.Hits)
        {
            if (h.Act == UiAct.VoyageKey) DrawButton(h, HudLayout.VoyageKeyLabel((int)h.Key), h.Key <= 9 ? 20 : 16);
            else if (h.Act == UiAct.VoyageShip) DrawButton(h, h.Key < 0 ? "‹" : "›", 22);
            else if (h.Act == UiAct.AskNo) DrawButton(h, "그만두기");
            else if (h.Act == UiAct.AskYes) DrawButton(h, "띄우기", 16, h.Enabled && _hs.AskPrune.Length > 0);
        }
        var tr = R(_hud.Area("새 항해 풀이"));
        DrawMultilineString(_font, new Vector2(tr.Position.X, tr.Position.Y + Fs(14) * 0.9f), HudLayout.VoyageNote(_hs), HorizontalAlignment.Center, tr.Size.X, Fs(14), 2, _hs.AskPrune.Length > 0 ? new Color(1f, 0.75f, 0.55f) : InkDim);
    }

    // ───────────────────────── 손 ─────────────────────────

    private void Do(UiHit h)
    {
        if (!h.Enabled) return;
        long k = h.Key;
        switch (h.Act)
        {
            case UiAct.StartResume when _view.Latest != null: LiveMain.PendingResume = _view.Latest; Go(NewScene); break;
            case UiAct.StartOther: _view.ListOpen = !_view.ListOpen; break;
            case UiAct.StartPick when k < _view.Voyages.Length: LiveMain.PendingResume = _view.Voyages[k]; Go(NewScene); break;
            case UiAct.StartNew: OpenVoyage(); break;
            case UiAct.StartRenew when _view.Latest != null: LiveMain.PendingNew = (_view.Latest.Ship, (ulong)_view.Latest.Voyage); Go(NewScene); break;
            case UiAct.StartDelete when _view.Latest != null: _store.Delete(_view.Latest); LoadVoyages(); break;
            case UiAct.StartChoice: _view.Pick = (int)k; Go(_choices[k].Scene); break;
            case UiAct.StartQuit: GetTree().Quit(); break;
            case UiAct.VoyageKey: _hs.VoyageKey((int)k); _hs.AskFocus = 1; break;
            case UiAct.VoyageShip: if (_shipKeys.Length > 0) _hs.AskShipAt = ((_hs.AskShipAt + (int)k) % _shipKeys.Length + _shipKeys.Length) % _shipKeys.Length; break;
            case UiAct.AskNo: _hs.Ask = HudAsk.None; break;
            case UiAct.AskYes: Launch(); break;
        }
    }

    /// <summary>번호 창: 마지막 항해의 배 (없으면 디딤호) · 새로 뽑은 번호 · 항해가 다섯이면 지울 가장 오래된 항해.</summary>
    private void OpenVoyage()
    {
        string ship = _view.Latest?.Ship ?? "Didim";
        _hs.OpenVoyage(Math.Max(0, Array.IndexOf(_shipKeys, ship)), VoyageNumber.Draw());
        _hs.AskPrune = _view.Voyages.Length >= VoyageStore.KeepVoyages ? VoyageCard.Title(_view.Voyages[^1]) : "";
        _view.ListOpen = false;
    }

    private void Launch()
    {
        if (VoyageNumber.Parse(_hs.AskNumber) is not { } n || _shipKeys.Length == 0) return;
        LiveMain.PendingNew = (_shipKeys[Math.Clamp(_hs.AskShipAt, 0, _shipKeys.Length - 1)], n);
        _hs.Ask = HudAsk.None;
        Go(NewScene);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_choices.Length == 0) return;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (_hs.Ask == HudAsk.NewVoyage)
            {
                switch (k.Keycode)
                {
                    case >= Key.Key0 and <= Key.Key9: _hs.VoyageKey((int)(k.Keycode - Key.Key0)); break;
                    case >= Key.Kp0 and <= Key.Kp9: _hs.VoyageKey((int)(k.Keycode - Key.Kp0)); break;
                    case Key.Backspace or Key.Delete: _hs.VoyageKey(10); break;
                    case Key.Left: Do(new UiHit(default, UiAct.VoyageShip, -1)); break;
                    case Key.Right: Do(new UiHit(default, UiAct.VoyageShip, 1)); break;
                    case Key.Enter or Key.KpEnter: Launch(); break;
                    case Key.Escape: _hs.Ask = HudAsk.None; break;
                }
                return;
            }
            switch (k.Keycode)
            {
                case Key.Key1 or Key.Kp1: Go(OldScene); break;
                case Key.Key2 or Key.Kp2: Go(NewScene); break;
                case Key.Left or Key.Up: _view.Pick = 0; break;
                case Key.Right or Key.Down: _view.Pick = 1; break;
                case Key.Enter or Key.KpEnter:
                    if (_view.Latest != null && _view.Why.Length == 0 && _view.Pick == 1) { LiveMain.PendingResume = _view.Latest; Go(NewScene); }
                    else Go(_choices[_view.Pick].Scene);
                    break;
                case Key.Space: Go(_choices[_view.Pick].Scene); break;
                case Key.Escape: if (_view.ListOpen) _view.ListOpen = false; else GetTree().Quit(); break;
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            var r = _hud.At(mm.Position.X / _hud.U, mm.Position.Y / _hud.U);
            if (r.Hit is { Act: UiAct.StartChoice } c) _view.Pick = (int)c.Key;
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            if (_hs.Ask != HudAsk.None) _hs.Ask = HudAsk.None;   // 뒤로: 창 → 목록
            else _view.ListOpen = false;
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            // 누름이 아니라 놓을 때 한다 (누른 손잡이와 놓은 손잡이가 같을 때만 — docs/UI.md 같은 손잡이 규칙) · 목록 밖을 누르면 목록만 닫는다
            var r = _hud.At(mb.Position.X / _hud.U, mb.Position.Y / _hud.U);
            if (mb.Pressed)
            {
                _press = r.Hit;
                if (_view.ListOpen && r.Hit is not { Act: UiAct.StartPick or UiAct.StartOther }) { _view.ListOpen = false; _press = null; }
                return;
            }
            var p = _press; _press = null;
            if (p != null && r.Hit != null && r.Hit.Act == p.Act && r.Hit.Key == p.Key) Do(r.Hit);
        }
    }

    private void DrawCentered(Font font, string text, Vector2 at, int size, Color c)
    {
        var sz = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        DrawString(font, new Vector2(at.X - sz.X / 2, at.Y), text, HorizontalAlignment.Left, -1, size, c);
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
