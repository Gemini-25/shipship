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

    /// <summary>v17.6 상황 음악 (평시 · 긴장 · 위기 · 추모) 0~1.</summary>
    public static float MusicVolume { get; set; } = 0.5f;
    /// <summary>v17.6 배 안에서 트는 음악 (방 스피커 — 벽 · 문 너머로 먹먹하게) 0~1.</summary>
    public static float CabinVolume { get; set; } = 0.6f;
    /// <summary>v17.6 글자 크기 배율.</summary>
    public static float TextScale { get => Gfx.TextScale; set => Gfx.TextScale = Mathf.Clamp(value, 0.85f, 1.3f); }
    /// <summary>v17.6 색약 팔레트 (색과 함께 모양 · 무늬로 가른다).</summary>
    public static bool ColorSafeOn { get => ColorSafe.On; set => ColorSafe.On = value; }

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

    /// <summary>v12.0: 새 항해에서 승무원이 죽을 수 있다 (기본 켜짐 — 쓰러진 채 체력이 바닥나면). 끄면 쓰러진 채 버틴다.</summary>
    public static bool Death { get; set; } = true;

    /// <summary>v12.2 하이라이트 모드: 평온하면 빠르게, 사고·주목할 일이 나면 1배속으로.</summary>
    public static bool Highlight { get; set; }

    /// <summary>v12.9 첫 항해 안내 (처음 켜면 켜져 있다 — 끝까지 가거나 끄면 다음부터는 꺼진다).</summary>
    public static bool Tutorial { get; set; } = true;

    /// <summary>v12.2 하이라이트 모드에서 카메라가 사고 현장으로 가고, 결정적인 순간엔 느리게.</summary>
    public static bool AutoCamera { get; set; } = true;

    /// <summary>v16.2 화면 UI를 전부 띄운다 (끄면 조용한 HUD — 이상이 생긴 것만 떠오른다).</summary>
    public static bool ShowAllHud { get; set; }

    /// <summary>v10.7: 새 항해의 배 ("auto"면 인원에 맞는 배).</summary>
    public static string Ship { get; set; } = "auto";

    public static string ShipFor(int crew) =>
        Ship == "gen" ? ShipSim.Core.ShipGenerator.KeyFor(System.Math.Clamp(crew, 4, 40), (int)(System.DateTime.Now.Ticks / 10_000_000 % 100_000)) // v12.6 새 항해마다 다른 배
        : ShipSim.Core.ShipCatalog.Find(Ship)?.Key ?? ShipSim.Core.ShipCatalog.ForCrew(crew).Key;

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
        Death = cfg.GetValue("voyage", "death", Death).AsBool();
        Highlight = cfg.GetValue("play", "highlight", Highlight).AsBool();
        Tutorial = cfg.GetValue("play", "tutorial", Tutorial).AsBool();
        AutoCamera = cfg.GetValue("play", "auto_camera", AutoCamera).AsBool();
        ShowAllHud = cfg.GetValue("ui", "show_all", ShowAllHud).AsBool(); // v16.2
        MusicVolume = (float)cfg.GetValue("sound", "music", MusicVolume).AsDouble(); // v17.6
        CabinVolume = (float)cfg.GetValue("sound", "cabin_music", CabinVolume).AsDouble();
        TextScale = (float)cfg.GetValue("ui", "text_scale", TextScale).AsDouble();
        ColorSafeOn = cfg.GetValue("ui", "color_safe", ColorSafeOn).AsBool();
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
        cfg.SetValue("voyage", "death", Death);
        cfg.SetValue("play", "highlight", Highlight);
        cfg.SetValue("play", "tutorial", Tutorial);
        cfg.SetValue("play", "auto_camera", AutoCamera);
        cfg.SetValue("ui", "show_all", ShowAllHud); // v16.2
        cfg.SetValue("sound", "music", MusicVolume); // v17.6
        cfg.SetValue("sound", "cabin_music", CabinVolume);
        cfg.SetValue("ui", "text_scale", TextScale);
        cfg.SetValue("ui", "color_safe", ColorSafeOn);
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
        // 창이 화면보다 크면 위(제목 · 소리)와 아래(새 항해 · 닫기)가 화면 밖으로 잘렸다 —
        // 머리와 바닥은 고정하고 가운데만 스크롤 · 창 크기는 화면에 맞춘다 (Layout)
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 10);
        AddChild(outer);
        var head = new HBoxContainer();
        var title = Title($"설정 · ShipSim v{ProjectSettings.GetSetting("application/config/version", "")}"); // v11.3 버전 표기
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.ClipText = true;
        head.AddChild(title);
        var x = new Button { Text = "✕", Flat = true, TooltipText = "닫기 (O)" };
        x.Pressed += () => Toggle();
        head.AddChild(x);
        outer.AddChild(head);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        outer.AddChild(_scroll);
        var box = _box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(box);
        box.AddChild(Caption("소리"));
        var vol = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = Settings.Volume * 100, CustomMinimumSize = new Vector2(360, 24) };
        var volLabel = Label($"전체 소리 {Settings.Volume * 100:0}%");
        vol.ValueChanged += v => { Settings.Volume = (float)v / 100f; volLabel.Text = $"전체 소리 {v:0}%"; Changed(); };
        box.AddChild(volLabel);
        box.AddChild(vol);
        box.AddChild(Check("효과음 (경보·운석·감압·문·발소리·로봇)", Settings.Effects, on => Settings.Effects = on));
        box.AddChild(Slider("배경 음악", Settings.MusicVolume, v => Settings.MusicVolume = v)); // v17.6
        box.AddChild(Slider("배 안에서 트는 음악", Settings.CabinVolume, v => Settings.CabinVolume = v));
        box.AddChild(Caption("보기"));
        box.AddChild(Slider("글자 크기", (Settings.TextScale - 0.85f) / 0.45f, v => Settings.TextScale = 0.85f + v * 0.45f, () => $"{Settings.TextScale * 100:0}%"));
        box.AddChild(Check("색약 팔레트 (사람은 표식 모양, 방은 바닥 무늬로도 가른다)", Settings.ColorSafeOn, on => Settings.ColorSafeOn = on));
        box.AddChild(Check("배경음 (기계음·새는 소리·불)", Settings.Ambience, on => Settings.Ambience = on));
        box.AddChild(Caption("관찰"));
        box.AddChild(Check("치명 경보가 울리면 저절로 일시정지", Settings.AutoPauseCritical, on => Settings.AutoPauseCritical = on));
        box.AddChild(Check("사건 알림 (N: 그곳으로 · [ ] 사건 넘기기 · R 사건 직전으로 되감기)", Settings.EventToasts, on => Settings.EventToasts = on));
        // v11.2 무작위 사고: 관찰자가 던지지 않아도 배가 사고를 겪는다 (사고 도구의 "더 보기"에서도 바꾼다)
        _random = new OptionButton { CustomMinimumSize = new Vector2(360, 0) };
        foreach (var (_, name) in RandomChoices) _random.AddItem($"무작위 사고: {name}");
        _random.ItemSelected += i => _main.SetRandomIncidents(RandomChoices[i].days);
        box.AddChild(_random);
        // v12.4 이야기꾼 · 난이도 (켜면 무작위 사고 대신 이야기꾼이 사고를 낸다)
        _persona = new OptionButton { CustomMinimumSize = new Vector2(360, 0) };
        foreach (var name in new[] { "이야기꾼: 끔 (위의 무작위 사고)", "이야기꾼: 꾸준형 — 고르게, 거의 추스르면 다음", "이야기꾼: 몰아치기형 — 오래 조용하다 한꺼번에", "이야기꾼: 무작위형 — 예측 불가", "이야기꾼: 시험관형 — 배의 급소를 노린다",
                                   "이야기꾼: 느린 불씨형 — 작게 시작해 점점 잦고 크게", "이야기꾼: 계절형 — 철마다 한 갈래를 몰아서", // v15.7
                                   "이야기꾼: 자비형 — 다 추스르고 막을 물자가 있을 때만", "이야기꾼: 앙갚음형 — 잘 버틴 만큼 되갚는다" })
            _persona.AddItem(name);
        _persona.ItemSelected += i => _main.SetTuned("story.persona", i);
        box.AddChild(_persona);
        _level = new OptionButton { CustomMinimumSize = new Vector2(360, 0) };
        foreach (var name in new[] { "난이도 1 느긋", "난이도 2 쉬움", "난이도 3 보통", "난이도 4 어려움", "난이도 5 가혹" })
            _level.AddItem($"{name} (시작 물자는 새 항해부터)");
        _level.ItemSelected += i => _main.SetTuned("story.level", i + 1);
        box.AddChild(_level);
        // v12.9 항해 방식 (새 항해부터)
        _mode = new OptionButton { CustomMinimumSize = new Vector2(360, 0) };
        foreach (var name in new[] { "항해: 자유 항해 (샌드박스)", "항해: 캠페인 — 이어지는 임무와 갈림길, 끝은 세대선", "항해: 처음부터 세대선 — 아이가 태어나고 자란다" })
            _mode.AddItem($"{name} (새 항해부터)");
        _mode.ItemSelected += i => _main.SetTuned("mode.campaign", i);
        box.AddChild(_mode);
        box.AddChild(Check("첫 항해 안내 (새 항해를 시작하면 한 단계씩)", Settings.Tutorial, on => Settings.Tutorial = on));
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
        ship.AddItem("배: 절차 생성 — 인원에 맞춰 새 항해마다 다른 배 (새 방 섞임 · 20인 넘으면 구획 격벽)"); // v12.6
        int genIndex = ShipSim.Core.ShipCatalog.All.Length + 1;
        ship.Selected = Settings.Ship == "auto" ? 0 : Settings.Ship == "gen" ? genIndex : 1 + System.Array.FindIndex(ShipSim.Core.ShipCatalog.All, t => t.Key == Settings.Ship);
        if (ship.Selected < 0) ship.Selected = 0;
        var crewLabel = Label(CrewText(Settings.Crew));
        var crew = new HSlider { MinValue = 1, MaxValue = ShipSim.Core.World.MaxCrew, Step = 1, Value = Settings.Crew, CustomMinimumSize = new Vector2(360, 24) };
        crew.ValueChanged += v => { Settings.Crew = (int)v; crewLabel.Text = CrewText((int)v); Changed(); };
        var preview = new ShipPreview { CustomMinimumSize = new Vector2(360, 200) }; // v16.22 고른 배의 크기 · 윤곽 (같은 축척)
        ship.ItemSelected += i =>
        {
            Settings.Ship = i == 0 ? "auto" : i == genIndex ? "gen" : ShipSim.Core.ShipCatalog.All[i - 1].Key;
            if (i > 0 && i < genIndex) crew.Value = ShipSim.Core.ShipCatalog.All[i - 1].Crew; // 고른 배의 설계 인원으로 (바꿔도 된다)
            crewLabel.Text = CrewText(Settings.Crew);
            preview.Show(Settings.Ship, Settings.Crew);
            Changed();
        };
        crew.ValueChanged += v => preview.Show(Settings.Ship, (int)v);
        box.AddChild(ship);
        box.AddChild(preview);
        preview.Show(Settings.Ship, Settings.Crew);
        box.AddChild(crewLabel);
        box.AddChild(crew);
        box.AddChild(Check("승무원이 죽을 수 있다 (새 항해부터)", Settings.Death, on => Settings.Death = on)); // v12.0
        box.AddChild(Check("자동 배속: 평온하면 빠르게, 사고가 나면 1배속 (L)", Settings.Highlight, on => Settings.Highlight = on)); // v12.2
        box.AddChild(Check("자동 배속일 때 화면이 사고 현장으로 · 고비에서는 느리게", Settings.AutoCamera, on => Settings.AutoCamera = on));
        box.AddChild(Check("화면 안내 전부 보기 (끄면 조용한 화면 — 이상이 생긴 것만 떠오른다)", Settings.ShowAllHud, on => Settings.ShowAllHud = on)); // v16.2
        var voyage = new HBoxContainer();
        voyage.AddThemeConstantOverride("separation", 8);
        var same = new Button { Text = "같은 항해 번호로 새 항해" };
        same.Pressed += () => { Toggle(); _main.NewVoyage(Settings.Crew, newSeed: false); };
        var fresh = new Button { Text = "새 항해 번호로 새 항해" };
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
            if (Visible) Callable.From(Layout).CallDeferred();
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
        outer.AddChild(close);
        Wrap(box);
    }

    private ScrollContainer _scroll = null!;
    private VBoxContainer _box = null!;
    private bool _hooked;

    /// <summary>긴 항목 글이 창을 넓혀 옆으로 잘리지 않게: 체크 · 글은 줄바꿈, 고르기 상자는 창 폭에 맞춰 자른다.</summary>
    private static void Wrap(Node n)
    {
        foreach (var ch in n.GetChildren())
        {
            switch (ch)
            {
                case OptionButton ob: ob.FitToLongestItem = false; ob.ClipText = true; ob.CustomMinimumSize = new Vector2(0, ob.CustomMinimumSize.Y); ob.SizeFlagsHorizontal = SizeFlags.ExpandFill; ob.TooltipText = "펼쳐서 고른다"; break;
                case CheckBox cb: cb.AutowrapMode = TextServer.AutowrapMode.WordSmart; cb.SizeFlagsHorizontal = SizeFlags.ExpandFill; break;
                case Godot.Label l when l.GetParent() is not HBoxContainer: l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = SizeFlags.ExpandFill; break;
                case HSlider hs: hs.CustomMinimumSize = new Vector2(0, hs.CustomMinimumSize.Y); hs.SizeFlagsHorizontal = SizeFlags.ExpandFill; break;
            }
            if (ch is not ScrollContainer) Wrap(ch);
        }
    }

    /// <summary>창 크기를 화면에 맞춘다: 폭은 최대 620 · 높이는 내용만큼(넘치면 스크롤) · 화면 가운데.</summary>
    private void Layout()
    {
        var vp = GetViewportRect().Size;
        const float margin = 16f;
        float w = System.MathF.Min(620f, vp.X - 2f * margin);
        float chrome = 36f + 10f * 2f + 44f + 40f; // 안쪽 여백 · 간격 · 머리 · 바닥
        float want = _box.GetCombinedMinimumSize().Y;
        float body = System.MathF.Max(120f, System.MathF.Min(want, vp.Y - 2f * margin - chrome));
        _scroll.CustomMinimumSize = new Vector2(w - 44f, body);
        CustomMinimumSize = new Vector2(w, 0f);
        Size = Vector2.Zero; // 내용에 맞춰 다시 잰다
        var size = GetCombinedMinimumSize();
        Size = size;
        Position = new Vector2(System.MathF.Max(margin, (vp.X - size.X) * 0.5f), System.MathF.Max(margin, (vp.Y - size.Y) * 0.5f));
    }

    private OptionButton _random = null!;
    private OptionButton _persona = null!, _level = null!, _mode = null!;

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
            _persona.Selected = (int)ShipSim.Core.Storyteller.Persona;
            _level.Selected = ShipSim.Core.Storyteller.Level - 1;
            _mode.Selected = (int)ShipSim.Core.CampaignSystem.ModeValue;
            if (!_hooked) { GetViewport().SizeChanged += () => { if (Visible) Layout(); }; _hooked = true; }
            _scroll.ScrollVertical = 0;
            Layout();
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

    /// <summary>v17.6 이름 + 값 + 미끄럼 막대 한 묶음 (0~1).</summary>
    private static Control Slider(string name, float value, System.Action<float> set, System.Func<string>? shown = null)
    {
        var box = new VBoxContainer();
        var label = Label($"{name} {(shown != null ? shown() : $"{value * 100:0}%")}");
        var sl = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = value * 100, CustomMinimumSize = new Vector2(360, 20) };
        sl.ValueChanged += v => { set((float)v / 100f); label.Text = $"{name} {(shown != null ? shown() : $"{v:0}%")}"; Changed(); };
        box.AddChild(label);
        box.AddChild(sl);
        return box;
    }

    private static string CrewText(int n)
    {
        if (Settings.Ship == "gen") return $"승무원 {n}명 · 절차 생성 배({System.Math.Clamp(n, 4, 40)}인용)";
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
