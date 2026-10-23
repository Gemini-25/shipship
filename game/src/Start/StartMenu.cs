using System;
using System.Linq;
using Godot;

namespace ShipSim.Start;

/// <summary>
/// 처음 화면: 기존 판(옛 게임 — scenes/Main.tscn)과 새 엔진 판(새로 만든 엔진만 — scenes/NewEngine.tscn) 가운데 고른다.
/// 두 판은 서로의 코드를 쓰지 않는다 — 도트 그림만 같다.
/// 명령 줄: --new 는 새 엔진 판으로 · --old 는 기존 판으로 바로 간다 · 그 밖의 인자만 있으면 기존 판 (옛 게임의 실행 인자를 그대로 쓰던 대로).
/// 확인용: --menu-shot=파일.png 은 이 화면을 찍고 나간다.
/// </summary>
public partial class StartMenu : Node2D
{
    private const string OldScene = "res://scenes/Main.tscn", NewScene = "res://scenes/NewEngine.tscn";
    private const string LastFile = "user://start_choice.txt";

    private sealed record Choice(string Title, string Lead, string[] Lines, string Scene, Color Accent);

    private Choice[] _choices = Array.Empty<Choice>();
    private int _pick = 1, _hover = -1;
    private Rect2[] _cards = Array.Empty<Rect2>();
    private Rect2 _quit;
    private int _pressCard = -1; private bool _pressQuit;
    private Font _font = null!, _bold = null!;
    private double _t;
    private string? _shot;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _shot = args.FirstOrDefault(a => a.StartsWith("--menu-shot="))?[12..];
        if (_shot != null) args = Array.Empty<string>();
        if (args.Contains("--new")) { Go(NewScene, remember: false); return; }
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
                new[] { "새로 설계한 배 " + ShipNames(), "물리 · 화학 · 몸 · 마음 · 주컴퓨터 · 이야기꾼 · 연출 — 모두 새것", "옛 배 · 옛 화면은 쓰지 않는다 (도트 그림만 같다)" },
                NewScene, new Color(0.45f, 0.95f, 0.8f)),
        };
        try { if (FileAccess.FileExists(LastFile) && FileAccess.GetFileAsString(LastFile).Trim() == "old") _pick = 0; } catch { }
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
        QueueRedraw();
        if (_shot != null && _t > 1.5)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shot);
            GD.Print($"처음 화면 → {_shot}");
            _shot = null;
            GetTree().Quit();
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

        float cx = vp.X / 2;
        DrawCentered(_bold, "ShipSim", new Vector2(cx, vp.Y * 0.16f), 54, new Color(0.92f, 0.94f, 0.98f));
        DrawCentered(_font, "우주선 한 척을 지켜본다 — 어느 판으로 볼까요?", new Vector2(cx, vp.Y * 0.16f + 52), 20, new Color(0.65f, 0.7f, 0.8f));

        float gap = 36, w = Math.Min(560, (vp.X - 3 * gap) / 2), h = 250, top = vp.Y * 0.32f;
        _cards = new Rect2[_choices.Length];
        for (int i = 0; i < _choices.Length; i++)
        {
            var c = _choices[i];
            var r = new Rect2(cx - w - gap / 2 + i * (w + gap), top, w, h);
            _cards[i] = r;
            bool on = i == _pick, hot = i == _hover;
            DrawRect(r, new Color(0.04f, 0.055f, 0.09f, 0.94f));
            DrawRect(r, new Color(c.Accent, on ? 1f : hot ? 0.7f : 0.35f), false, on ? 3 : 1.5f);
            if (on) DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, 6)), c.Accent);
            DrawString(_bold, new Vector2(r.Position.X + 28, r.Position.Y + 64), $"{i + 1}", HorizontalAlignment.Left, -1, 22, new Color(c.Accent, 0.8f));
            DrawString(_bold, new Vector2(r.Position.X + 60, r.Position.Y + 64), c.Title, HorizontalAlignment.Left, -1, 34, new Color(0.95f, 0.96f, 1f));
            DrawString(_font, new Vector2(r.Position.X + 30, r.Position.Y + 110), c.Lead, HorizontalAlignment.Left, w - 56, 20, c.Accent);
            float y = r.Position.Y + 152;
            foreach (var line in c.Lines)
            {
                var sz = _font.GetMultilineStringSize(line, HorizontalAlignment.Left, w - 72, 16, 3);
                DrawCircle(new Vector2(r.Position.X + 36, y - 5), 2.5f, new Color(c.Accent, 0.8f));
                DrawMultilineString(_font, new Vector2(r.Position.X + 48, y), line, HorizontalAlignment.Left, w - 72, 16, 3, new Color(0.8f, 0.84f, 0.92f));
                y += sz.Y + 12;
            }
        }
        DrawCentered(_font, "판을 눌러 고른다 (1 · 2 · ← → · Enter)", new Vector2(cx, top + h + 56), 17, new Color(0.6f, 0.65f, 0.75f));
        DrawCentered(_font, "새 엔진 판에서는 오른쪽 위 메뉴 → 처음 화면으로 (Esc 두 번)", new Vector2(cx, top + h + 84), 15, new Color(0.5f, 0.55f, 0.65f));
        // 나가기 단추 (마우스만으로 — docs/UI.md 3.9)
        _quit = new Rect2(cx - 70, top + h + 108, 140, 40);
        bool qh = _quit.HasPoint(GetViewport().GetMousePosition());
        DrawRect(_quit, new Color(0.06f, 0.08f, 0.12f, qh ? 0.95f : 0.8f));
        DrawRect(_quit, new Color(0.3f, 0.38f, 0.5f, qh ? 1 : 0.7f), false, 1);
        DrawString(_font, new Vector2(_quit.Position.X, _quit.Position.Y + 26), "나가기 (Esc)", HorizontalAlignment.Center, 140, 16, new Color(0.8f, 0.85f, 0.93f));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_choices.Length == 0) return;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            switch (k.Keycode)
            {
                case Key.Key1 or Key.Kp1: Go(OldScene); break;
                case Key.Key2 or Key.Kp2: Go(NewScene); break;
                case Key.Left or Key.Up: _pick = 0; break;
                case Key.Right or Key.Down: _pick = 1; break;
                case Key.Enter or Key.KpEnter or Key.Space: Go(_choices[_pick].Scene); break;
                case Key.Escape: GetTree().Quit(); break;
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            _hover = Array.FindIndex(_cards, r => r.HasPoint(mm.Position));
            if (_hover >= 0) _pick = _hover;
            Input.SetDefaultCursorShape(_hover >= 0 || _quit.HasPoint(mm.Position) ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow);
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            // 누름이 아니라 놓을 때 고른다 (누른 판과 놓은 판이 같을 때만 — docs/UI.md 같은 손잡이 규칙)
            int at = Array.FindIndex(_cards, r => r.HasPoint(mb.Position));
            bool quit = _quit.HasPoint(mb.Position);
            if (mb.Pressed) { _pressCard = at; _pressQuit = quit; return; }
            if (at >= 0 && at == _pressCard) Go(_choices[at].Scene);
            else if (quit && _pressQuit) GetTree().Quit();
            _pressCard = -1; _pressQuit = false;
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
