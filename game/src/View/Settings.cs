using Godot;

namespace ShipSim.View;

/// <summary>
/// v10 옵션: 소리, 경보 때 자동 정지, 사건 알림, 시작 배속. user://settings.cfg 에 남는다.
/// </summary>
public static class Settings
{
    private const string Path = "user://settings.cfg";

    /// <summary>전체 소리 0~1.</summary>
    public static float Volume { get; set; } = 0.7f;

    /// <summary>경보·운석·문 같은 효과음.</summary>
    public static bool Effects { get; set; } = true;

    /// <summary>원자로·환기 기계음, 새는 소리, 불 소리 같은 배경음.</summary>
    public static bool Ambience { get; set; } = true;

    /// <summary>치명 경보가 울리면 저절로 일시정지.</summary>
    public static bool AutoPauseCritical { get; set; } = false;

    /// <summary>사건이 나면 화면 가운데 위에 알림 (N으로 그곳에 간다).</summary>
    public static bool EventToasts { get; set; } = true;

    /// <summary>시작 배속 (0~3).</summary>
    public static int StartSpeed { get; set; } = 0;

    /// <summary>v10.1: 새 항해의 승무원 수 (기본 6명 = 침대 수, 그 위는 간이침대에서 잔다).</summary>
    public static int Crew { get; set; } = ShipSim.Core.World.DefaultCrewSize;

    /// <summary>v10.7: 새 항해의 배 ("auto"면 인원에 맞는 배).</summary>
    public static string Ship { get; set; } = "auto";

    public static string ShipFor(int crew) =>
        ShipSim.Core.ShipCatalog.Find(Ship)?.Key ?? ShipSim.Core.ShipCatalog.ForCrew(crew).Key;

    /// <summary>v10.7: 밸런스 수치 파일 (한 줄에 `열쇠 = 값`). 없으면 기본값으로 만들어 둔다 — 게임 밖에서 고쳐도 된다.</summary>
    public const string TuningPath = "user://tuning.cfg";

    public static void LoadTuning()
    {
        ShipSim.Core.Tuning.ResetDefaults();
        string path = ProjectSettings.GlobalizePath(TuningPath);
        try
        {
            if (System.IO.File.Exists(path)) ShipSim.Core.Tuning.Load(System.IO.File.ReadAllText(path));
            else System.IO.File.WriteAllText(path, ShipSim.Core.Tuning.Write());
        }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { GD.PrintErr($"tuning: {e.Message}"); }
    }

    public static void SaveTuning()
    {
        try { System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(TuningPath), ShipSim.Core.Tuning.Write()); }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { GD.PrintErr($"tuning: {e.Message}"); }
    }

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        Volume = (float)cfg.GetValue("sound", "volume", Volume).AsDouble();
        Effects = cfg.GetValue("sound", "effects", Effects).AsBool();
        Ambience = cfg.GetValue("sound", "ambience", Ambience).AsBool();
        AutoPauseCritical = cfg.GetValue("play", "auto_pause_critical", AutoPauseCritical).AsBool();
        EventToasts = cfg.GetValue("play", "event_toasts", EventToasts).AsBool();
        StartSpeed = Mathf.Clamp(cfg.GetValue("play", "start_speed", StartSpeed).AsInt32(), 0, 3);
        Crew = Mathf.Clamp(cfg.GetValue("voyage", "crew", Crew).AsInt32(), 1, ShipSim.Core.World.MaxCrew);
        Ship = cfg.GetValue("voyage", "ship", Ship).AsString();
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("sound", "volume", Volume);
        cfg.SetValue("sound", "effects", Effects);
        cfg.SetValue("sound", "ambience", Ambience);
        cfg.SetValue("play", "auto_pause_critical", AutoPauseCritical);
        cfg.SetValue("play", "event_toasts", EventToasts);
        cfg.SetValue("play", "start_speed", StartSpeed);
        cfg.SetValue("voyage", "crew", Crew);
        cfg.SetValue("voyage", "ship", Ship);
        cfg.Save(Path);
    }
}

/// <summary>옵션 창 (O). Godot 기본 컨트롤로 만든다.</summary>
public partial class OptionsPanel : PanelContainer
{
    private Main _main = null!;

    public void Init(Main main)
    {
        _main = main;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        var style = new StyleBoxFlat
        {
            BgColor = new Color("#0d1117").Lerp(new Color("#161b22"), 0.5f),
            BorderColor = new Color("#30363d"),
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 18, ContentMarginBottom = 18,
        };
        style.SetBorderWidthAll(1);
        AddThemeStyleboxOverride("panel", style);
        CustomMinimumSize = new Vector2(420, 0);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);

        box.AddChild(Title($"설정 · ShipSim v{ProjectSettings.GetSetting("application/config/version", "")}")); // v11.3 버전 표기
        box.AddChild(Caption("소리"));
        var vol = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = Settings.Volume * 100, CustomMinimumSize = new Vector2(360, 24) };
        var volLabel = Label($"전체 소리 {Settings.Volume * 100:0}%");
        vol.ValueChanged += v => { Settings.Volume = (float)v / 100f; volLabel.Text = $"전체 소리 {v:0}%"; Changed(); };
        box.AddChild(volLabel);
        box.AddChild(vol);
        box.AddChild(Check("효과음 (경보·운석·감압·문·발소리·로봇)", Settings.Effects, on => Settings.Effects = on));
        box.AddChild(Check("배경음 (기계음·새는 소리·불)", Settings.Ambience, on => Settings.Ambience = on));
        box.AddChild(Caption("관찰"));
        box.AddChild(Check("치명 경보가 울리면 저절로 일시정지", Settings.AutoPauseCritical, on => Settings.AutoPauseCritical = on));
        box.AddChild(Check("사건 알림 (N: 그곳으로 · [ ] 사건 넘기기 · R 사건 직전으로 되감기)", Settings.EventToasts, on => Settings.EventToasts = on));
        // v11.2 무작위 사고: 관찰자가 던지지 않아도 배가 사고를 겪는다 (사고 도구의 "더 보기"에서도 바꾼다)
        _random = new OptionButton { CustomMinimumSize = new Vector2(360, 0) };
        foreach (var (_, name) in RandomChoices) _random.AddItem($"무작위 사고: {name}");
        _random.ItemSelected += i => _main.SetRandomIncidents(RandomChoices[i].days);
        box.AddChild(_random);
        var speed = new OptionButton();
        foreach (var s in Main.Speeds) speed.AddItem($"시작 배속 {s}×");
        speed.Selected = Settings.StartSpeed;
        speed.ItemSelected += i => { Settings.StartSpeed = (int)i; Changed(); };
        box.AddChild(speed);
        // v10.1: 새 항해 — 승무원 수 (6명 넘게 태우면 간이침대에서 자고, 식량·물이 버티는 선을 넘을 수 있다)
        box.AddChild(Caption("새 항해"));
        // v10.7: 배 고르기 — 자동이면 인원에 맞는 배 (4·6·12·20·30인용)
        var ship = new OptionButton { CustomMinimumSize = new Vector2(360, 0) };
        ship.AddItem("배: 자동 (인원에 맞는 배)");
        foreach (var t in ShipSim.Core.ShipCatalog.All) ship.AddItem($"배: {t.Name} · {t.Crew}인용 — {t.Note}");
        ship.Selected = Settings.Ship == "auto" ? 0 : 1 + System.Array.FindIndex(ShipSim.Core.ShipCatalog.All, t => t.Key == Settings.Ship);
        if (ship.Selected < 0) ship.Selected = 0;
        var crewLabel = Label(CrewText(Settings.Crew));
        var crew = new HSlider { MinValue = 1, MaxValue = ShipSim.Core.World.MaxCrew, Step = 1, Value = Settings.Crew, CustomMinimumSize = new Vector2(360, 24) };
        crew.ValueChanged += v => { Settings.Crew = (int)v; crewLabel.Text = CrewText((int)v); Changed(); };
        ship.ItemSelected += i =>
        {
            Settings.Ship = i == 0 ? "auto" : ShipSim.Core.ShipCatalog.All[i - 1].Key;
            if (i > 0) crew.Value = ShipSim.Core.ShipCatalog.All[i - 1].Crew; // 고른 배의 설계 인원으로 (바꿔도 된다)
            crewLabel.Text = CrewText(Settings.Crew);
            Changed();
        };
        box.AddChild(ship);
        box.AddChild(crewLabel);
        box.AddChild(crew);
        var voyage = new HBoxContainer();
        voyage.AddThemeConstantOverride("separation", 8);
        var same = new Button { Text = "같은 시드로 새 항해" };
        same.Pressed += () => { Toggle(); _main.NewVoyage(Settings.Crew, newSeed: false); };
        var fresh = new Button { Text = "새 시드로 새 항해" };
        fresh.Pressed += () => { Toggle(); _main.NewVoyage(Settings.Crew, newSeed: true); };
        voyage.AddChild(same);
        voyage.AddChild(fresh);
        box.AddChild(voyage);

        // v10.7: 밸런스 수치 — 바꾸면 지금 항해에도 기록되어 적용되고(되감기·불러오기가 같은 값을 쓴다), tuning.cfg에 남아 다음 항해에도 쓰인다
        var tuneToggle = new Button { Text = "밸런스 수치 ▸", Flat = true, Alignment = HorizontalAlignment.Left };
        var tuneScroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 230), Visible = false, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var tuneBox = new VBoxContainer();
        tuneBox.AddThemeConstantOverride("separation", 4);
        tuneScroll.AddChild(tuneBox);
        tuneToggle.Pressed += () =>
        {
            tuneScroll.Visible = !tuneScroll.Visible;
            tuneToggle.Text = tuneScroll.Visible ? "밸런스 수치 ▾" : "밸런스 수치 ▸";
            if (Visible) Position = (GetViewportRect().Size - GetCombinedMinimumSize()) * 0.5f;
        };
        var spins = new System.Collections.Generic.List<(ShipSim.Core.TuningEntry e, SpinBox spin)>();
        foreach (var e in ShipSim.Core.Tuning.Entries)
        {
            var row = new HBoxContainer();
            var l = new Label { Text = e.Label, CustomMinimumSize = new Vector2(250, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.TooltipText = $"{e.Key} · 기본 {e.Default:0.###} · {e.Min:0.###}~{e.Max:0.###}";
            float step = e.Max - e.Min > 50f ? 1f : e.Max - e.Min > 5f ? 0.1f : 0.01f;
            var spin = new SpinBox { MinValue = e.Min, MaxValue = e.Max, Step = step, Value = e.Get(), CustomMinimumSize = new Vector2(110, 0) };
            var entry = e;
            spin.ValueChanged += v =>
            {
                if (System.MathF.Abs((float)v - entry.Get()) < 1e-6f) return;
                if (_main.Replaying == null) ShipSim.Core.Player.Tune(_main.Sim, entry.Key, (float)v);
                else ShipSim.Core.Tuning.Apply(entry.Key, (float)v);
                Settings.SaveTuning();
            };
            spins.Add((e, spin));
            row.AddChild(l);
            row.AddChild(spin);
            tuneBox.AddChild(row);
        }
        var reset = new Button { Text = "모두 기본값으로" };
        reset.Pressed += () => { foreach (var (e, spin) in spins) spin.Value = e.Default; };
        tuneBox.AddChild(reset);
        var note = Caption("설정 폴더의 tuning.cfg를 고쳐도 된다 (새 항해부터)");
        tuneBox.AddChild(note);
        box.AddChild(tuneToggle);
        box.AddChild(tuneScroll);

        var close = new Button { Text = "닫기 (O)" };
        close.Pressed += () => Toggle();
        box.AddChild(close);
    }

    private OptionButton _random = null!;

    private static readonly (float days, string name)[] RandomChoices =
        { (0f, "끔 (관찰자가 던진 것만)"), (6f, "드물게 — 평균 6일에 한 번"), (3f, "보통 — 3일에 한 번"), (1.5f, "잦게 — 하루 반에 한 번"), (0.5f, "혼돈 — 반나절에 한 번") };

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            float d = ShipSim.Core.HazardSystem.RandomDays;
            int best = 0;
            for (int i = 1; i < RandomChoices.Length; i++)
                if (d > 0f && (best == 0 || System.MathF.Abs(RandomChoices[i].days - d) < System.MathF.Abs(RandomChoices[best].days - d))) best = i;
            _random.Selected = best;
            var vp = GetViewportRect().Size;
            Position = (vp - GetCombinedMinimumSize()) * 0.5f;
        }
    }

    private static void Changed()
    {
        Settings.Save();
        SoundSystem.ApplyVolume();
    }

    private static Label Title(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 20);
        return l;
    }

    private static Label Caption(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", new Color("#8b949e"));
        l.AddThemeFontSizeOverride("font_size", 13);
        return l;
    }

    private static Label Label(string text) => new() { Text = text };

    private static string CrewText(int n)
    {
        var t = ShipSim.Core.ShipCatalog.Find(Settings.ShipFor(n)) ?? ShipSim.Core.ShipCatalog.Default;
        return $"승무원 {n}명 · {t.Name}({t.Crew}인용)" + (n == t.Crew ? " — 설계 인원"
            : n > t.Crew ? $" — 간이침대 {n - t.Crew}개 · 식량·물이 빠듯하다" : " — 일손이 모자라다");
    }

    private static CheckBox Check(string text, bool value, System.Action<bool> set)
    {
        var c = new CheckBox { Text = text, ButtonPressed = value };
        c.Toggled += on => { set(on); Changed(); };
        return c;
    }
}
