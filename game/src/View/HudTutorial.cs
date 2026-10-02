using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.9 첫 항해 안내: 처음 켠 사람에게 한 단계씩 — 보는 법 → 사람 → 보기 화면 → 설명서 → 사고 → 인과 사슬 → 배속 → 요약 진행.
// 하라는 것을 하면 저절로 다음으로 넘어간다 (건너뛰기도 된다). 관찰자 게임이라, 직접 조작보다 "어디를 보면 무엇이 보이는지"를 알려 준다.
public partial class Hud
{
    public bool TutorialOn { get; set; }
    public int TutorialStep { get; private set; }
    private float _tutZoom0 = -1f;
    private Vector2 _tutCam0;
    private ViewMode _tutView0;
    private int _tutIncidents0 = -1;

    private (string title, string text, Func<bool> done)[] TutorialSteps => new (string, string, Func<bool>)[]
    {
        ("배 둘러보기", "휠로 확대·축소하고, 오른쪽 버튼으로 끌어 배를 둘러보세요. 배는 스스로 돌아갑니다 — 당신은 지켜보는 사람입니다.",
            () => MathF.Abs(GetViewport().GetCanvasTransform().X.Length() - _tutZoom0) > 0.05f || (_main.Camera.Position - _tutCam0).Length() > 80f),
        ("사람 보기", "승무원을 클릭해 보세요. 무엇을 왜 하는지, 몸·일기·관계까지 보입니다.", () => _main.SelectedCrew != null),
        ("보기 화면", "V를 누르거나 위쪽 탭으로 보기를 바꿔 보세요 — 전력·공기·배관·감지기·환경. Shift를 누른 채 고르면 겹쳐 봅니다.", () => _main.ViewMode != _tutView0),
        ("설명서", "I를 누르고 설비나 방을 클릭하면 하는 일·원리·멈추면 생기는 일이 나옵니다.", () => CodexMode),
        ("사고 일으키기", "사고 메뉴(Z·X·C·B·P 또는 위쪽 사고 버튼)로 작은 운석 하나를 떨어뜨려 보세요. 배가 어떻게 대응하는지 지켜보세요.",
            () => _world.Causes.Incidents.Count > _tutIncidents0),
        ("인과 사슬", "K를 누르면 무엇이 무엇을 일으켰는지 사슬로 보입니다. 사고 카드를 눌러도 됩니다.", () => ChainOpen),
        ("배속", "1~4로 배속을 바꾸거나 L로 자동 배속을 켜 보세요 — 평온하면 빠르게, 일이 나면 1배속으로 돌아옵니다.", () => _main.SpeedIndex > 0 || Settings.Highlight),
        ("빨리 감기", "U를 누르면 큰 일이 날 때까지 며칠을 빨리 감고, 그동안 있었던 일을 한 장으로 보여 줍니다. 이걸로 안내는 끝입니다.", () => _main.Summarizing || SummaryLines != null),
    };

    public void StartTutorial()
    {
        TutorialOn = true;
        TutorialStep = 0;
        Arm();
    }

    private void Arm()
    {
        _tutZoom0 = GetViewport().GetCanvasTransform().X.Length();
        _tutCam0 = _main.Camera.Position;
        _tutView0 = _main.ViewMode;
        _tutIncidents0 = _world.Causes.Incidents.Count;
    }

    private void DrawTutorial(Vector2 mouse)
    {
        if (!TutorialOn) return;
        var steps = TutorialSteps;
        if (TutorialStep >= steps.Length) { TutorialOn = false; Settings.Tutorial = false; Settings.Save(); return; }
        var (title, text, done) = steps[TutorialStep];
        if (done()) { TutorialStep++; Arm(); return; }
        float w = 380f;
        var card = new Rect2(Margin, Screen.Y - LogFullHeight - Margin - 120f, w, 108f);
        Card(card);
        Gfx.Text(this, Fonts.Bold, card.Position + new Vector2(16, 26), $"첫 항해 안내 {TutorialStep + 1}/{steps.Length} — {title}", Ui.TextSubtitle, Palette.Accent);
        // 줄바꿈 (폭에 맞게)
        var words = text.Split(' ');
        string line = "";
        float y = card.Position.Y + 48;
        foreach (var word in words)
        {
            string t = line.Length == 0 ? word : line + " " + word;
            if (Gfx.Width(Fonts.Body, t, Ui.TextBody) > w - 32) { Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 16, y), line, Ui.TextBody, Palette.TextDim); y += 17; line = word; }
            else line = t;
        }
        if (line.Length > 0) Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 16, y), line, Ui.TextBody, Palette.TextDim);
        Button(new Rect2(card.End.X - 120, card.Position.Y + 8, 52, 24), "다음", false, mouse, () => { TutorialStep++; Arm(); }, Ui.TextSmall);
        Button(new Rect2(card.End.X - 64, card.Position.Y + 8, 52, 24), "끄기", false, mouse, () => { TutorialOn = false; Settings.Tutorial = false; Settings.Save(); }, Ui.TextSmall);
    }
}

public partial class Hud
{
    /// <summary>v12.9 임무 줄: 지금 장·목표·진행 (항로 막대 바로 위).</summary>
    private void DrawCampaign()
    {
        var cp = _world.Campaign;
        var gen = _world.Generation;
        if (!cp.Active && !gen.Enabled) return;
        if (!MinimapOpen || _minimapRect.Size.X <= 0f || ChronicleOpen || TechOpen) return;
        var bar = new Rect2(_minimapRect.Position.X, _minimapRect.Position.Y - 46f - 30f, _minimapRect.Size.X, 26f);
        Gfx.RoundRect(this, bar, new Color(0.06f, 0.05f, 0.1f, 0.9f), 7f, new Color("#b58cff").WithAlpha(0.4f));
        string text;
        float prog;
        if (cp.Current is Mission m)
        {
            var (p, line) = cp.Status();
            prog = p;
            text = $"{m.Chapter}장 「{m.Title}」 · {line}" + (cp.Choice != null ? $" · {cp.Choice}" : "");
        }
        else
        {
            prog = 1f;
            int kids = _world.Crew.Count(c => !c.Dead && c.IsChild), born = gen.Births;
            float avg = _world.Crew.Where(c => !c.Dead).Select(c => c.Age).DefaultIfEmpty(0f).Average();
            text = $"세대선 {gen.Anniversaries + 1}년째 · 태어남 {born} · 아이 {kids} · 평균 {avg:0}살 · 노환 {gen.Elders}";
        }
        Gfx.Bar(this, new Rect2(bar.Position.X + 8f, bar.End.Y - 5f, bar.Size.X - 16f, 2f), prog, new Color("#b58cff"));
        string t = text;
        while (t.Length > 6 && Gfx.Width(Fonts.Body, t, Ui.TextSmall) > bar.Size.X - 16f) t = t[..^2] + "…";
        Gfx.Text(this, Fonts.Body, new Vector2(bar.Position.X + 8f, bar.Position.Y + 15f), t, Ui.TextSmall, new Color("#d7c6ff"));
    }
}
