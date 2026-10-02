using System;
using System.Globalization;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>관찰자가 일으킬 수 있는 사고. 도구를 고르고 우주선을 클릭하면 그 자리에서 일어난다.</summary>
public enum IncidentTool { None, SmallMeteor, BigMeteor, HugeMeteor, Fire, Break, PipeBurst, Hazard /* v11.2 사고 더 보기 (ToolHazard) */ }

public static class IncidentTools
{
    public static readonly IncidentTool[] All = { IncidentTool.SmallMeteor, IncidentTool.BigMeteor, IncidentTool.HugeMeteor, IncidentTool.Fire, IncidentTool.Break, IncidentTool.PipeBurst };

    public static string Name(IncidentTool t) => t switch
    {
        IncidentTool.SmallMeteor => "운석(소)",
        IncidentTool.BigMeteor => "운석(대)",
        IncidentTool.HugeMeteor => "거대 운석",
        IncidentTool.Fire => "화재",
        IncidentTool.Break => "고장",
        IncidentTool.PipeBurst => "배관",
        _ => "",
    };

    public static string Key(IncidentTool t) => t switch
    {
        IncidentTool.SmallMeteor => "Z",
        IncidentTool.BigMeteor => "X",
        IncidentTool.HugeMeteor => "M",
        IncidentTool.Fire => "C",
        IncidentTool.Break => "B",
        IncidentTool.PipeBurst => "P",
        _ => "",
    };

    public static string Hint(IncidentTool t) => t switch
    {
        IncidentTool.SmallMeteor => "작은 운석 — 맞힐 곳을 클릭 (6분 동안 날아와 가까운 외벽으로 들어온다 · 통신실 센서가 먼저 볼 수 있다)",
        IncidentTool.BigMeteor => "큰 운석 — 맞힐 곳을 클릭 (6분 뒤 파공·파편·배선 손상 · 경보를 받으면 그 방 사람이 빠진다)",
        IncidentTool.HugeMeteor => "거대 운석 — 맞힐 곳을 클릭 (6분 뒤 연결부가 끊기고 골조가 뜯긴다 · 방이 떨어져 나갈 수 있다)",
        IncidentTool.Fire => "화재 — 불을 붙일 바닥을 클릭",
        IncidentTool.Break => "고장 — 망가뜨릴 설비를 클릭",
        IncidentTool.PipeBurst => "배관 파손 — 터뜨릴 관 가까이를 클릭 (냉각 고온관·분기·귀환관·급수관)",
        _ => "",
    };
}

/// <summary>
/// 게임 루트. 시뮬레이션(Core.World)을 고정 틱으로 돌리고, 화면 노드들을 묶는다.
/// 시뮬레이션은 화면을 모르고, 화면은 시뮬레이션을 읽기만 한다.
/// </summary>
public partial class Main : Node2D
{
    public static readonly int[] Speeds = { 1, 3, 10, 30 };
    private const int MaxStepsPerFrame = 240;

    public World Sim { get; private set; } = null!;
    public ShipView ShipView { get; private set; } = null!;
    public CameraRig Camera { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;

    public int SpeedIndex { get; private set; }
    public bool Paused { get; private set; }
    public ViewMode ViewMode { get; set; } = ViewMode.Normal;
    /// <summary>v12.3 겹쳐 보기: 두 번째 보기 (보기 탭을 Shift로 누른다).</summary>
    public ViewMode? SecondaryView { get; set; }

    /// <summary>현재 틱과 다음 틱 사이 어디쯤인지 (0~1). 부드러운 움직임용.</summary>
    public float Alpha { get; private set; } = 1f;

    public CrewMember? SelectedCrew { get; private set; }
    public Room? SelectedRoom { get; private set; }
    public Furniture? SelectedFurniture { get; private set; }

    /// <summary>v10.10: 고른 선내 로봇.</summary>
    public Robot? SelectedRobot { get; private set; }
    public Robot? HoveredRobot { get; private set; }
    public CrewMember? HoveredCrew { get; private set; }
    public Room? HoveredRoom { get; private set; }
    public Furniture? HoveredFurniture { get; private set; }
    public bool Following => Camera.FollowTarget != null;

    /// <summary>지금 고른 사고 도구 (None이면 평소처럼 선택).</summary>
    public IncidentTool Tool { get; set; } = IncidentTool.None;

    /// <summary>v11.2: Tool이 Hazard일 때 어떤 사고인지.</summary>
    public HazardKind ToolHazard { get; private set; }

    public string ToolHint => Tool == IncidentTool.Hazard ? Hazards.Spec(ToolHazard).Hint + " · 클릭" : IncidentTools.Hint(Tool);

    /// <summary>불러오는 중이면 그 재생기 (같은 시드에서 기록된 사고를 다시 일으키며 저장한 틱까지 빨리 감는다).</summary>
    public ReplayRunner? Replaying { get; private set; }

    /// <summary>잠깐 띄우는 알림 (저장했다, 불러왔다).</summary>
    public string? Notice { get; private set; }
    public ulong NoticeMsec { get; private set; }

    /// <summary>다음 장면에서 불러올 저장 내용 (F9 → 장면을 다시 띄운다).</summary>
    private static string? _pendingLoad;

    /// <summary>v10.1: 다음 장면에서 새로 띄울 항해 (시드, 승무원 수).</summary>
    private static (int seed, int crew, string ship)? _pendingNew;

    // ── v10 관찰 보조 ──
    public SoundSystem Sound { get; private set; } = null!;
    public OptionsPanel Options { get; private set; } = null!;
    private long _seenAlert; // v10.3: 마지막으로 본 경보 순번 (한 프레임에 여러 경보가 울려도 빠뜨리지 않는다)
    private int _episodeCursor = -1;

    public static string SavePath => System.IO.Path.Combine(OS.GetUserDataDir(), "shipsim-save.txt");

    public ulong NoticeFrame { get; private set; }

    public void ShowNotice(string text)
    {
        Notice = text;
        NoticeMsec = Time.GetTicksMsec();
        NoticeFrame = Engine.GetProcessFrames();
    }

    private Starfield _stars = null!;
    private double _accumulator;
    private string? _screenshotPath;
    private int _screenshotFrames = -1;
    private Input.CursorShape _cursor = Input.CursorShape.Arrow;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Palette.Space);
        Settings.Load();
        Fonts.Load();
        Textures.Load();
        // --seed=N 은 다른 모든 인자보다 먼저 (우주선을 만들 때 쓰인다). --load=경로 / F9는 저장한 역사를 다시 돌린다
        int seed = 20260929, crew = Settings.Crew;
        string? shipArg = null;
        bool shot = false;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--seed=") && int.TryParse(arg[7..], out var sd)) seed = sd;
            if (arg.StartsWith("--crew=") && int.TryParse(arg[7..], out var cr)) crew = cr;
            if (arg.StartsWith("--ship=")) shipArg = ShipCatalog.Find(arg[7..])?.Key;
            if (!_commandLineDone && arg.StartsWith("--load=") && System.IO.File.Exists(arg[7..])) _pendingLoad ??= System.IO.File.ReadAllText(arg[7..]);
            if (arg.StartsWith("--shot=")) shot = true;
        }
        string? loadError = null;
        bool loaded = false;
        if (_pendingLoad != null)
        {
            // v10.3: 망가진 저장 파일이면 새 항해로 시작하고 까닭을 알린다 (예전에는 장면이 멈췄다)
            try
            {
                Replaying = new ReplayRunner(_pendingLoad);
                Sim = Replaying.World;
                loaded = true;
            }
            catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                loadError = e.Message;
                Replaying = null;
            }
            _pendingLoad = null;
            // 화면을 찍는 실행이면 기다리지 않고 한 번에 끝까지
            if (shot && Replaying != null) FinishReplay(Replaying.Advance(int.MaxValue));
        }
        if (!loaded)
        {
            string shipKey = shipArg ?? Settings.ShipFor(crew);
            if (_pendingNew is { } nv) { seed = nv.seed; crew = nv.crew; shipKey = nv.ship; }
            else if (shipArg != null && !OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--crew="))) crew = 0; // --ship만 주면 그 배의 설계 인원
            Settings.LoadTuning(); // v10.7: 새 항해는 tuning.cfg의 수치로 시작한다
            Sim = World.CreateDefault(seed, crew, shipKey);
            // v12.0: 사망은 기본으로 켜져 있다 (설정에서 끌 수 있다) — 관찰자 기록에 남아 불러오기·되감기도 같다
            if (Settings.Death && !OS.GetCmdlineUserArgs().Contains("--nodeath")) Core.Player.AllowDeath(Sim, true);
        }

        var background = new CanvasLayer { Name = "Background", Layer = -10 };
        AddChild(background);
        _stars = new Starfield { Name = "Stars" };
        background.AddChild(_stars);
        background.AddChild(new CosmicSky { Name = "CosmicSky", Main = this }); // v18.13 우주 대재난 하늘 · 섬광 · 소리 · 바깥 시점

        ShipView = new ShipView { Name = "Ship" };
        AddChild(ShipView);
        ShipView.Init(this, Sim);

        Camera = new CameraRig { Name = "Camera" };
        AddChild(Camera);
        Camera.MakeCurrent();

        var labels = new CanvasLayer { Name = "Labels", Layer = 5 };
        AddChild(labels);
        var overlay = new LabelOverlay { Name = "LabelOverlay" };
        overlay.Init(this, Sim);
        labels.AddChild(overlay);

        var ui = new CanvasLayer { Name = "UI", Layer = 10 };
        AddChild(ui);
        Hud = new Hud { Name = "Hud" };
        ui.AddChild(Hud);
        Hud.Init(this, Sim);

        // v10: 소리와 옵션 창
        Sound = new SoundSystem { Name = "Sound" };
        AddChild(Sound);
        Sound.Init(this);
        var options = new CanvasLayer { Name = "Options", Layer = 20 };
        AddChild(options);
        Options = new OptionsPanel { Name = "OptionsPanel" };
        options.AddChild(Options);
        Options.Init(this);
        SpeedIndex = Settings.StartSpeed;
        _seenAlert = Sim.AlertSerial;

        FitCamera();
        // 실행 인자는 처음 한 번만 (F9·되감기로 장면을 다시 띄울 때 또 워프하지 않게). 화면 찍기는 매번
        ApplyCommandLine(onlyShot: _commandLineDone);
        _commandLineDone = true;
        // v12.9 첫 항해 안내: 새 항해이고, 안내를 켜 두었고, 화면 찍기가 아니면
        if (!loaded && Settings.Tutorial && !OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--shot"))) Hud.CallDeferred(nameof(Hud.StartTutorial));
        if (loadError != null) ShowNotice($"저장 파일을 불러오지 못해 새 항해로 시작했다 — {loadError}");
    }

    private static bool _commandLineDone;

    public override void _Process(double delta)
    {
        if (Replaying is ReplayRunner rr)
        {
            // 불러오는 중: 한 프레임에 몇천 틱씩 빨리 감는다 (역사가 화면에서 다시 흐른다)
            FinishReplay(rr.Advance(9000));
        }
        else if (StepSummary()) { WatchAlerts(); } // v12.8 요약 진행: 화면 없이 빨리 감는다
        else if (!Paused)
        {
            _accumulator += delta * Speeds[SpeedIndex] * (SlowMotion ? 0.3 : 1.0) * SimTime.TicksPerSecond; // v12.2 결정적 순간엔 느리게
            int steps = (int)Math.Floor(_accumulator);
            if (steps > MaxStepsPerFrame)
            {
                steps = MaxStepsPerFrame;
                _accumulator = 0;
            }
            else
            {
                _accumulator -= steps;
            }
            for (int i = 0; i < steps; i++) Sim.Step();
            Alpha = (float)_accumulator;
            WatchAlerts();
        }

        UpdateHighlight(delta); // v12.2 하이라이트 모드 · 자동 카메라
        UpdateHover();
        _stars.CameraPosition = Camera.Position;
        // v12.2 운항: 별이 뒤로 흐른다 (배속에 비례, 회피 기동 연소 중엔 더 빠르게), 잔해 지대에선 잔해가 지나간다
        _stars.Cruise = Paused || Replaying != null ? 0f : 7f * Speeds[SpeedIndex] * (SlowMotion ? 0.3f : 1f) * (Sim.Propulsion.Burning ? 3f : 1f);
        _stars.Debris = Sim.Propulsion.Zone == ZoneKind.Debris;
        _stars.Storm = Sim.Hazards.StormActive ? 1f : 0f;

        if (_screenshotFrames > 0 && --_screenshotFrames == 0) TakeScreenshotAndQuit();
    }

    private void FinishReplay(bool done)
    {
        if (!done || Replaying == null) return;
        if (Replaying.Rewind)
            ShowNotice($"되감기 — {Sim.Day}일차 {Sim.Clock} · 여기서부터 같은 역사가 다시 흐른다" +
                       (Replaying.Upcoming > 0 ? $" (앞으로 원래 사고 {Replaying.Upcoming}건이 다시 일어난다 · 새 사고를 일으키면 거기서 갈라진다)" : " (Space로 재생)"));
        else
        {
            bool ok = Replaying.Verified;
            ShowNotice(ok ? $"불러오기 완료 — {Sim.Day}일차 {Sim.Clock} · 지문 일치 (같은 역사)" : "불러오기 완료 — 지문이 다르다 (다른 판 게임으로 저장한 파일?)");
        }
        Replaying = null;
        Paused = true;
        _seenAlert = Sim.AlertSerial;
    }

    // ─────────────────────────────── v10 관찰 보조: 사건 알림 · 사건 넘기기 · 되감기 ───────────────────────────────

    /// <summary>새 치명 경보: 알림을 띄우고, 설정이면 저절로 멈춘다.</summary>
    private void WatchAlerts()
    {
        if (Sim.AlertSerial == _seenAlert) return;
        // 새로 울린 경보 중 가장 무거운 것 (치명이 뒤따른 경고에 묻히지 않게)
        var fresh = Sim.Alerts.Where(a => a.Serial > _seenAlert).ToList();
        _seenAlert = Sim.AlertSerial;
        var last = fresh.LastOrDefault(a => a.Level == AlertLevel.Critical);
        if (last == null) return;
        if (Settings.AutoPauseCritical)
        {
            Paused = true;
            ShowNotice($"치명 경보 — 저절로 멈췄다: {last.Text}" + (last.Room != null ? " · N: 그곳으로" : ""));
        }
        else if (Settings.EventToasts && last.Room != null) ShowNotice($"{last.Text} · N: 그곳으로");
    }

    /// <summary>N: 가장 최근 경보가 난 곳으로.</summary>
    private void JumpToLatestAlert()
    {
        var a = Sim.Alerts.LastOrDefault(x => x.Room != null && !x.Room.Detached);
        if (a == null) { ShowNotice("아직 알릴 만한 사건이 없다"); return; }
        FocusRoom(a.Room!);
        ShowNotice($"{SimTime.Day(a.Tick)}일 {SimTime.Clock(a.Tick)} · {a.Text}");
    }

    /// <summary>[ ]: 지금까지의 사고(에피소드)를 하나씩 넘겨 본다.</summary>
    private void CycleEpisode(int dir)
    {
        var eps = Sim.History.Episodes;
        if (eps.Count == 0) { ShowNotice("아직 사고가 없었다"); return; }
        _episodeCursor = _episodeCursor < 0 ? (dir > 0 ? 0 : eps.Count - 1) : ((_episodeCursor + dir) % eps.Count + eps.Count) % eps.Count;
        var ep = eps[_episodeCursor];
        if (Sim.Ship.Rooms.FirstOrDefault(r => r.Id == ep.RoomId) is Room room && !room.Detached) FocusRoom(room);
        ShowNotice($"사고 {_episodeCursor + 1}/{eps.Count} · {SimTime.Day(ep.Start)}일 {SimTime.Clock(ep.Start)} · {ep.Cause}" +
                   (ep.End >= 0 ? $" · {(ep.End - ep.Start) / (float)SimTime.TicksPerHour:0.#}시간 만에 수습" : " · 아직 수습 중") + " · R: 직전으로 되감기");
    }

    /// <summary>R: 고른 사고(없으면 가장 최근 치명 경보)의 3분 전으로 되감는다 — 같은 시드·같은 관찰자 기록으로 다시 돌린다.</summary>
    private void RewindToEvent()
    {
        long tick = -1;
        var eps = Sim.History.Episodes;
        if (_episodeCursor >= 0 && _episodeCursor < eps.Count) tick = eps[_episodeCursor].Start;
        else if (Sim.Alerts.LastOrDefault(a => a.Level == AlertLevel.Critical) is Alert a) tick = a.Tick;
        if (tick < 0) { ShowNotice("되감을 사건이 없다 ([ ]로 사고를 고르거나, 치명 경보가 난 뒤에)"); return; }
        _pendingLoad = Core.SaveGame.WriteAt(Sim, Math.Max(0, tick - SimTime.Minutes(3)));
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    /// <summary>v12.2 그 틱의 조금 전으로 되감는다 (사고 사슬의 '직전으로').</summary>
    public void RewindTo(long tick)
    {
        _pendingLoad = Core.SaveGame.WriteAt(Sim, Math.Max(0, tick));
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    public void FocusRoom(Room room)
    {
        Camera.Position = ShipView.ToPx(room.Center) + new Vector2(Hud.RightColumnWidth * 0.5f, 0f) / Camera.Zoom.X;
        SelectRoom(room);
    }

    // ─────────────────────────────── 저장·불러오기 ───────────────────────────────

    public void SaveGame(string? path = null)
    {
        path ??= SavePath;
        System.IO.File.WriteAllText(path, Core.SaveGame.Write(Sim));
        ShowNotice($"저장했다 — {Sim.Day}일차 {Sim.Clock} · 관찰자 기록 {Sim.Commands.Count}줄 · 시드 {Sim.Seed}");
        GD.Print($"saved: {path}");
    }

    /// <summary>v10.1: 새 항해 — 승무원 수를 바꾸거나 시드를 새로 뽑아 처음부터.</summary>
    public void NewVoyage(int crew, bool newSeed)
    {
        int seed = newSeed ? (int)(Time.GetUnixTimeFromSystem() % 100_000_000) : Sim.Seed;
        _pendingNew = (seed, crew, Settings.ShipFor(crew));
        _pendingLoad = null;
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    public void LoadGame(string? path = null)
    {
        path ??= SavePath;
        if (!System.IO.File.Exists(path)) { ShowNotice("저장한 게임이 없다 (F5로 저장)"); return; }
        // v10.3: 읽기·해석에 실패하면 지금 게임을 그대로 두고 까닭을 알린다
        string text;
        try
        {
            text = System.IO.File.ReadAllText(path);
            Core.SaveGame.Parse(text);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            ShowNotice($"불러오지 못했다 — {e.Message}");
            return;
        }
        _pendingLoad = text;
        GetTree().ReloadCurrentScene();
    }

    private void UpdateHover()
    {
        var screen = GetViewport().GetMousePosition();
        if (Hud.IsOverUi(screen))
        {
            HoveredCrew = null;
            HoveredRoom = null;
            HoveredFurniture = null;
            HoveredRobot = null;
        }
        else
        {
            var world = GetGlobalMousePosition();
            HoveredCrew = ShipView.PickCrew(world);
            HoveredRobot = HoveredCrew == null ? ShipView.PickRobot(world) : null;
            HoveredFurniture = HoveredCrew == null && HoveredRobot == null ? ShipView.PickFurniture(world) : null;
            HoveredRoom = HoveredCrew == null && HoveredFurniture == null && HoveredRobot == null ? Sim.Ship.RoomAt(ShipView.CellAtPx(world)) : null;
        }
        var cursor = Tool != IncidentTool.None ? Input.CursorShape.Cross
            : HoveredCrew != null || HoveredFurniture != null || HoveredRobot != null ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow;
        if (cursor != _cursor)
        {
            _cursor = cursor;
            Input.SetDefaultCursorShape(cursor);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            var world = GetGlobalMousePosition();
            if (Tool != IncidentTool.None)
            {
                if (Replaying != null) { GetViewport().SetInputAsHandled(); return; } // 불러오는 동안엔 역사를 바꾸지 않는다
                if (ApplyTool(world) && !click.ShiftPressed) Tool = IncidentTool.None; // Shift를 누르고 있으면 계속
                GetViewport().SetInputAsHandled();
                return;
            }
            var crew = ShipView.PickCrew(world);
            var cell = ShipView.CellAtPx(world);
            if (crew != null) Select(crew);
            else if (ShipView.PickRobot(world) is Robot robot) SelectRobot(robot);
            else if (ShipView.PickFurniture(world) is Furniture f) SelectFurniture(f);
            else if (Sim.Ship.RoomAt(cell) is Room room) SelectRoom(room);
            else if (Sim.Ship.WallAt(cell) != null) SelectRoom(Hull.InsideRoom(Sim.Ship, cell)); // 벽을 누르면 그 벽의 방
            else SelectRoom(null);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Space: TogglePause(); break;
                case Key.Key1: SetSpeed(0); break;
                case Key.Key2: SetSpeed(1); break;
                case Key.Key3: SetSpeed(2); break;
                case Key.Key4: SetSpeed(3); break;
                case Key.V: CycleView(key.ShiftPressed ? -1 : 1); break;
                case Key.Tab: CycleCrew(key.ShiftPressed ? -1 : 1); break;
                case Key.F: ToggleFollow(); break;
                case Key.Home:
                case Key.H: FitCamera(); break;
                case Key.Escape:
                    if (Tool != IncidentTool.None) Tool = IncidentTool.None;
                    else if (!Hud.CloseHazardMenu()) ClearSelection();
                    break;
                case Key.Z: ToggleTool(IncidentTool.SmallMeteor); break;
                case Key.X: ToggleTool(IncidentTool.BigMeteor); break;
                case Key.M: ToggleTool(IncidentTool.HugeMeteor); break;
                case Key.C: ToggleTool(IncidentTool.Fire); break;
                case Key.B: ToggleTool(IncidentTool.Break); break;
                case Key.P: ToggleTool(IncidentTool.PipeBurst); break;
                case Key.J: Hud.ToggleChronicle(); break;
                case Key.K: if (key.ShiftPressed) Hud.ToggleScaleCodex(); else Hud.ToggleChain(); break; // v16.18 ⇧K 사고 도감 (규모별)
                case Key.U: ToggleSummary(); break; // v12.8 요약 진행
                case Key.L: ToggleHighlight(); break;
                case Key.I: Hud.ToggleCodex(); break;
                case Key.Y: Hud.ToggleControl(); break;
                case Key.E: Hud.TogglePolicy(); break; // v13.2 방침·회의
                case Key.G: Hud.ToggleMinimap(); break;
                case Key.T: if (key.ShiftPressed) Hud.ToggleTechWeb(); else Hud.ToggleTech(); break; // v16.14 Shift+T 기술 지도
                case Key.F5: SaveGame(); break;
                case Key.F9: LoadGame(); break;
                case Key.O: Options.Toggle(); break;
                case Key.N: JumpToLatestAlert(); break;
                case Key.Bracketleft: CycleEpisode(-1); break;
                case Key.Bracketright: CycleEpisode(1); break;
                case Key.R: RewindToEvent(); break;
                case Key.Slash or Key.Question or Key.F1: Hud.ToggleHelp(); break; // v16.2 ? 도움말
                default: return;
            }
            GetViewport().SetInputAsHandled();
        }
    }

    // ─────────────────────────────── 조작 ───────────────────────────────

    public void TogglePause() => Paused = !Paused;

    public void SetSpeed(int index)
    {
        SpeedIndex = Math.Clamp(index, 0, Speeds.Length - 1);
        Paused = false;
        if (Settings.Highlight) { Settings.Highlight = false; Settings.Save(); ShowNotice("배속을 손으로 골라 하이라이트 모드를 껐다 (L로 다시)"); }
    }

    public void CycleView(int dir)
    {
        int n = ViewModes.All.Length;
        ViewMode = ViewModes.All[((int)ViewMode + dir + n) % n];
    }

    public void Select(CrewMember? crew)
    {
        SelectedCrew = crew;
        SelectedRoom = null;
        SelectedFurniture = null;
        SelectedRobot = null;
        if (crew == null) Camera.FollowTarget = null;
    }

    public void SelectRoom(Room? room)
    {
        SelectedRoom = room;
        SelectedCrew = null;
        SelectedFurniture = null;
        SelectedRobot = null;
        Camera.FollowTarget = null;
    }

    public void SelectFurniture(Furniture? f)
    {
        SelectedFurniture = f;
        SelectedCrew = null;
        SelectedRoom = null;
        SelectedRobot = null;
        Camera.FollowTarget = null;
    }

    public void SelectRobot(Robot? r)
    {
        SelectedRobot = r;
        SelectedCrew = null;
        SelectedRoom = null;
        SelectedFurniture = null;
        Camera.FollowTarget = null;
    }

    public void ClearSelection()
    {
        SelectedCrew = null;
        SelectedRoom = null;
        SelectedFurniture = null;
        SelectedRobot = null;
        Camera.FollowTarget = null;
    }

    public void ToggleFollow()
    {
        if (Camera.FollowTarget != null || SelectedCrew == null)
        {
            Camera.FollowTarget = null;
            return;
        }
        var target = SelectedCrew;
        Camera.FollowTarget = () => SelectedCrew == target ? ShipView.CrewPx(target) : null;
    }

    /// <summary>화면에서 그 설비 쪽으로 카메라를 옮긴다.</summary>
    public void Focus(Furniture f)
    {
        SelectFurniture(f);
        Camera.Position = ShipView.FurnitureRect(f).GetCenter();
    }

    /// <summary>작업 대상 쪽으로 카메라를 옮기고 알맞은 것을 선택한다 (작업 목록에서 클릭).</summary>
    public void Focus(WorkTarget t)
    {
        if (t.Furniture is Furniture f) { Focus(f); return; }
        if (t.Crew is CrewMember c) Select(c);
        else SelectRoom(t.CurrentRoom);
        Camera.Position = ShipView.ToPx(t.Center);
    }

    // ─────────────────────────────── 사고 도구 ───────────────────────────────

    public void ToggleTool(IncidentTool t) => Tool = Tool == t ? IncidentTool.None : t;

    /// <summary>v11.2 사고 더 보기: 배 전체에 거는 사고는 바로, 대상이 있는 사고는 클릭할 곳을 고르게 한다.</summary>
    public void PickHazard(HazardKind k)
    {
        if (Hazards.Spec(k).Target == HazardTarget.Ship)
        {
            Tool = IncidentTool.None;
            if (Replaying != null) return; // 불러오는 동안엔 역사를 바꾸지 않는다
            if (Player.Hazard(Sim, k, default) == null)
                Sim.Log.Add(Sim.Tick, LogKind.Ship, $"{Hazards.Name(k)}: 지금은 걸 수 없다" + (k switch
                {
                    HazardKind.ReactorTransient => " (원자로가 꺼져 있다)",
                    HazardKind.ComputerFault => " (주 컴퓨터가 없거나 이미 고장)",
                    HazardKind.DebrisCloud => " (이미 잔해 지대다)",
                    HazardKind.WaterContamination => " (정수기가 없거나 물이 거의 없다)",
                    _ => "",
                }));
            return;
        }
        if (Tool == IncidentTool.Hazard && ToolHazard == k) { Tool = IncidentTool.None; return; }
        ToolHazard = k;
        Tool = IncidentTool.Hazard;
    }

    /// <summary>v11.2 무작위 사고 주기 (평균 며칠에 한 번, 0이면 끔). 기록되는 수치라 되감기·불러오기가 같은 역사를 흘린다.</summary>
    public void SetRandomIncidents(float days, bool persist = true)
    {
        if (MathF.Abs(HazardSystem.RandomDays - days) < 1e-4f) return;
        if (Replaying == null) Player.Tune(Sim, "incident.days", days);
        else Tuning.Apply("incident.days", days);
        if (persist) Settings.SaveTuning(); // 다음 항해에도 (명령줄 --random은 이번 항해만)
    }

    /// <summary>v12.4 기록되는 밸런스 수치 하나를 바꾼다 (이야기꾼·난이도 등 — 다음 항해에도).</summary>
    public void SetTuned(string key, float value)
    {
        if (Tuning.Find(key) is not TuningEntry e || MathF.Abs(e.Get() - value) < 1e-4f) return;
        if (Replaying == null) Player.Tune(Sim, key, value);
        else Tuning.Apply(key, value);
        Settings.SaveTuning();
    }

    /// <summary>v11.2: 사고 더 보기의 대상 (화면 미리보기와 클릭이 같은 규칙).</summary>
    public (Cell at, int id, bool ok) HazardAim(Vector2 worldPx)
    {
        var cell = ShipView.CellAtPx(worldPx);
        var k = ToolHazard;
        switch (Hazards.Spec(k).Target)
        {
            case HazardTarget.Room:
                return Hazards.RoomAt(Sim, cell) is Room r && !r.Detached ? (cell, -1, true) : (cell, -1, false);
            case HazardTarget.Machine:
                return Hazards.MachineAt(Sim, k, cell) is Furniture f ? (f.Cells[0], -1, true) : (cell, -1, false);
            case HazardTarget.Hull:
                return Hazards.HullAt(Sim, cell) is Cell h ? (h, -1, true) : (cell, -1, false);
            case HazardTarget.Door:
                return Hazards.DoorAt(Sim, cell) is Door d ? (d.Cell, -1, true) : (cell, -1, false);
            case HazardTarget.Crew:
                return ShipView.PickCrew(worldPx) is CrewMember c && !c.Dead ? (cell, c.Id, true) : (cell, -1, false);
            case HazardTarget.Robot:
                return ShipView.PickRobot(worldPx) is Robot rb ? (cell, rb.Id, true) : (cell, -1, false);
        }
        return (cell, -1, true);
    }

    /// <summary>고른 도구로 그 자리에 사고를 일으킨다. 알맞은 대상이 아니면 false.</summary>
    private bool ApplyTool(Vector2 worldPx)
    {
        var cell = ShipView.CellAtPx(worldPx);
        switch (Tool)
        {
            case IncidentTool.SmallMeteor:
            case IncidentTool.BigMeteor:
            case IncidentTool.HugeMeteor:
            {
                float size = Tool == IncidentTool.HugeMeteor ? 1.5f : Tool == IncidentTool.BigMeteor ? 1f : 0.35f;
                return Player.Meteor(Sim, cell, size) != null; // 흔들림·연출은 화면이 충돌 기록을 보고 한다 (Player: 저장용으로 기록)
            }
            case IncidentTool.Fire:
            {
                if (Sim.Ship.RoomAt(cell) == null) return false;
                if (!Sim.Ship.IsOpenFloor(cell))
                {
                    // 설비 위를 눌렀으면 그 옆 빈 바닥에
                    var near = Cell.Dirs8.Select(d => cell + d).Where(Sim.Ship.IsOpenFloor).Cast<Cell?>().FirstOrDefault();
                    if (near is not Cell n) return false;
                    cell = n;
                }
                return Player.Fire(Sim, cell);
            }
            case IncidentTool.Break:
            {
                var f = Sim.Ship.FurnitureAt(cell);
                return f?.Machine != null && Player.Break(Sim, f);
            }
            case IncidentTool.PipeBurst:
                return Player.PipeBurst(Sim, cell, 0.9f) != null;
            case IncidentTool.Hazard:
            {
                var (at, id, ok) = HazardAim(worldPx);
                if (!ok) return false;
                if (Player.Hazard(Sim, ToolHazard, at, id) != null) return true;
                Sim.Log.Add(Sim.Tick, LogKind.Ship, $"{Hazards.Name(ToolHazard)}: 여기에는 걸 수 없다" + (ToolHazard switch
                {
                    HazardKind.GasLeak => " (설비가 없는 방)",
                    HazardKind.FoodPoisoning => " (식사가 없다)",
                    HazardKind.CropBlight => " (이미 병충해가 있다)",
                    HazardKind.DoorJam => " (이미 고장 난 문)",
                    HazardKind.LightsOut => " (이미 캄캄하다)",
                    _ => "",
                }));
                return false;
            }
        }
        return false;
    }

    private void CycleCrew(int dir)
    {
        if (Sim.Crew.Count == 0) return;
        int i = SelectedCrew == null ? (dir > 0 ? 0 : Sim.Crew.Count - 1)
            : (Sim.Crew.IndexOf(SelectedCrew) + dir + Sim.Crew.Count) % Sim.Crew.Count;
        bool follow = Following;
        Select(Sim.Crew[i]);
        if (follow) { Camera.FollowTarget = null; ToggleFollow(); }
    }

    public void FitCamera()
    {
        var size = GetViewportRect().Size;
        // 오른쪽 패널, 위쪽 상태 막대, 아래쪽 기록 카드를 피해서 배치
        var area = new Rect2(24f, Hud.TopHeight + 12f, size.X - Hud.RightColumnWidth - 64f, size.Y - Hud.TopHeight - Hud.LogHeight - 40f);
        Camera.FitTo(ShipView.Bounds, area);
    }

    // ─────────────────────────────── 디버그 옵션 ───────────────────────────────
    // godot -- --warp=6 --select=0 --speed=2 --view=Air --shot=shot.png
    //   --warp=시간        시작 전에 시뮬레이션을 그만큼 미리 돌린다
    //   --select=번호      승무원 선택
    //   --room=이름        방 선택 (RoomType 이름)
    //   --machine=종류     그 종류 첫 설비 선택 (FurnitureType 이름)
    //   --break=종류       그 종류 첫 설비를 고장 낸다 (사고 시험)
    //   --view=모드        Normal / Power / Air / Temperature / Condition / Trace
    //   --netcut=설비:망   그 설비가 있는 방에서 나가는 망 토막을 끊는다 (Power/Water/Air)
    //   --shot=경로        몇 프레임 뒤 화면을 PNG로 저장하고 종료
    //   --scarcity         부품 바닥 (v5), --history=일:간격 무작위 사고를 겪은 우주선, --seed=N 시드

    private void ApplyCommandLine(bool onlyShot = false)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            var parts = arg.Split('=', 2);
            string value = parts.Length > 1 ? parts[1] : "";
            if (onlyShot && parts[0] != "--shot") continue;
            switch (parts[0])
            {
                case "--options":
                    Options.Toggle();
                    break;
                case "--hud": Settings.ShowAllHud = value == "all"; if (value == "help") Hud.HelpOpen = true; break; // v16.2 화면 시험: --hud=all | quiet | help
                case "--episode":
                    for (int k = 0; k < int.Parse(value, CultureInfo.InvariantCulture); k++) CycleEpisode(1);
                    break;
                case "--rewind":
                    RewindToEvent();
                    return;
                case "--warp":
                    int ticks = SimTime.Hours(float.Parse(value, CultureInfo.InvariantCulture));
                    for (int i = 0; i < ticks; i++) Sim.Step();
                    break;
                case "--select":
                    int id = int.Parse(value, CultureInfo.InvariantCulture);
                    Select(Sim.Crew.FirstOrDefault(c => c.Id == id));
                    break;
                case "--room":
                    SelectRoom(Sim.Ship.Rooms.FirstOrDefault(r => r.Type.ToString() == value));
                    break;
                case "--machine":
                    SelectFurniture(Sim.Ship.Furniture.FirstOrDefault(f => f.Type.ToString() == value));
                    break;
                case "--partition":
                {
                    // --partition=Mess (v10.2 칸막이를 바로 세운다 — 화면 확인용)
                    var room = Sim.Ship.Rooms.FirstOrDefault(r => r.Type.ToString() == value);
                    if (room != null && Remodel.FindSplit(Sim, room) is Remodel.SplitPlan sp) Remodel.Apply(Sim, sp);
                    break;
                }
                case "--merge":
                {
                    // --merge=Mess (v10.12 그 방의 칸막이를 곧바로 걷는다 — 화면 확인용)
                    var inner = Sim.Ship.Rooms.FirstOrDefault(r => !r.Merged && r.SplitFrom?.Type.ToString() == value);
                    if (inner != null) Remodel2.Merge(Sim, inner);
                    break;
                }
                case "--relocate":
                {
                    // --relocate=Power (v10.12 그 방의 옮길 수 있는 설비를 안쪽 방으로 — 화면 확인용)
                    var f = Sim.Ship.Furniture.FirstOrDefault(x => !x.Stowed && x.Room.Type.ToString() == value && Remodel2.Movable(x.Type) && x.Machine != null);
                    if (f != null && Remodel2.FindSpot(Sim, f) is { } spot) Remodel2.Move(Sim, f, spot.room, spot.cells);
                    break;
                }
                case "--break":
                    // --break=CoolantPump (첫 대), --break=OxygenGenerator* (그 종류 전부)
                    Player.BreakAll(Sim, value.TrimEnd('*'), value.EndsWith("*"));
                    break;
                case "--control":
                    Hud.ToggleControl();
                    break;
                case "--policies": // v13.2 방침·회의 화면
                    Hud.TogglePolicy();
                    break;
                case "--review": // v13.2 화면 시험: --review=vacuumfire:0 (다음 정기 회의의 사후 검토 안건)
                {
                    var rp = value.Split(':');
                    if (rp.Length == 2 && int.TryParse(rp[1], out int rv)) Sim.Meetings.QueueReview(rp[0], rv, "창고 화재 때 소화 수순 중에 사람이 쓰러졌다");
                    break;
                }
                case "--codex":
                    Hud.CodexMode = true;
                    break;
                case "--flood":
                {
                    // --flood=Galley:0.5 (방 종류:깊이 0~1)
                    var bits = value.Split(':');
                    var room = Sim.Ship.Rooms.FirstOrDefault(r => r.Type.ToString() == bits[0]);
                    if (room != null) Sim.Moisture.AddWater(room, room.Cells.Count * 20f * (bits.Length > 1 ? float.Parse(bits[1], CultureInfo.InvariantCulture) : 0.5f));
                    break;
                }
                case "--chain":
                    Hud.ToggleChain(); // v12.2 가장 최근 사고의 인과 사슬
                    break;
                case "--netcut":
                {
                    // --netcut=PowerPanel:Power (그 설비가 있는 방에서 나가는 그 망 토막을 모두 끊는다)
                    var bits = value.Split(':');
                    var f = Sim.Ship.Furniture.FirstOrDefault(x => x.Type.ToString() == bits[0]);
                    if (f == null) break;
                    var kind = bits.Length > 1 && System.Enum.TryParse<NetKind>(bits[1], out var nk) ? nk : NetKind.Power;
                    Sim.Net.EnsureBuilt();
                    foreach (var l in Sim.Net.Links.Where(l => l.Kind == kind && l.Door != null && (l.Door.RoomA == f.Room || l.Door.RoomB == f.Room)).Take(3)) Sim.Net.Hurt(l, 1f, "시험");
                    Sim.Net.Update(0f);
                    break;
                }
                case "--meteor":
                case "--fire":
                {
                    // --meteor=Storage:1 (방 종류:크기), --fire=Galley
                    var bits = value.Split(':');
                    var room = Sim.Ship.Rooms.FirstOrDefault(r => r.Type.ToString() == bits[0]);
                    if (room == null) break;
                    if (parts[0] == "--fire")
                        Player.Fire(Sim, room.Cells.First(Sim.Ship.IsOpenFloor));
                    else
                    {
                        float size = bits.Length > 1 ? float.Parse(bits[1], CultureInfo.InvariantCulture) : 0.35f;
                        var hull = Sim.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(Sim.Ship, kv.Key) == room).Select(kv => kv.Key).ToList();
                        var wall = hull.OrderBy(c => Mathf.Abs(c.Y - room.Center.Y) + Mathf.Abs(c.X - room.Center.X) * 0.3f).First();
                        Player.Meteor(Sim, Cell.Dirs4.Select(d => wall + d).First(c => Sim.Ship.RoomAt(c) == room), size);
                    }
                    break;
                }
                case "--bigfire": // v13.0 화면 시험: --bigfire=Storage:7 (방 종류:칸 수)
                {
                    var bf = value.Split(':');
                    if (Sim.Ship.Rooms.FirstOrDefault(r => r.Type.ToString() == bf[0]) is Room bfr)
                        foreach (var c in bfr.Cells.Where(Sim.Ship.IsOpenFloor).Take(bf.Length > 1 ? int.Parse(bf[1]) : 7)) Sim.Fire.Ignite(c, 0.9f);
                    break;
                }
                case "--policy": // v13.0 화면 시험: --policy=inertfire:0
                {
                    var pp = value.Split(':');
                    if (pp.Length == 2 && int.TryParse(pp[1], out int pv)) Core.Player.Policy(Sim, pp[0], pv); // v13.4 기록된다
                    break;
                }
                case "--warpmin": // 몇 분 앞으로
                {
                    int wt = SimTime.Minutes(float.Parse(value, CultureInfo.InvariantCulture));
                    for (int i = 0; i < wt; i++) Sim.Step();
                    break;
                }
                case "--scenario":
                    // 헤드리스 도구와 같은 사고 시험 (Core/Scenarios.cs): --scenario=nosealant
                    if (Player.Scenario(Sim, value, out var sf) && sf != null) SelectRoom(sf);
                    break;
                case "--death":
                    Player.AllowDeath(Sim, true);
                    break;
                case "--scarcity":
                    // 예비 부품·케이블·퓨즈·연료통을 바닥내고 소모품도 조금만 (v5 적응 시험)
                    Player.Scarcity(Sim);
                    break;
                case "--gate":
                {
                    // --gate=0,1,2,3 : 헤드리스 게이트 시험과 같은 사고 (창고 운석·주방 화재·냉각 펌프·침실 운석·배전실 운석·식당 화재·수경재배실 운석)를
                    //                   2일 07시·6일 13시·10일 19시·14일 01시에 그 순서로 겪게 하고 --warp 까지 돌린다
                    var order = value.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                    (int day, int hour)[] at = { (2, 7), (6, 13), (10, 19), (14, 1) };
                    for (int k = 0; k < order.Length && k < at.Length; k++)
                    {
                        long when = (long)(at[k].day - 1) * SimTime.TicksPerDay + SimTime.Hours(at[k].hour);
                        while (Sim.Tick < when) Sim.Step();
                        GateIncident(order[k]);
                    }
                    break;
                }
                case "--save":
                    SaveGame(value.Length > 0 ? value : null);
                    break;
                case "--chronicle":
                    Hud.ChronicleOpen = true;
                    break;
                case "--tech":
                    Hud.TechOpen = true;
                    break;
                case "--tune":
                {
                    // --tune=research.bench:20 (기록된다)
                    var kv = value.Split(':');
                    if (kv.Length == 2) Core.Player.Tune(Sim, kv[0], float.Parse(kv[1], CultureInfo.InvariantCulture));
                    break;
                }
                case "--research":
                    // 화면 확인용: 연구를 채운다 (기록되지 않는다 — 저장·되감기와 어긋난다)
                    Sim.Research = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--tier":
                {
                    // 화면 확인용: --tier=ReactorCore:3 (기록되지 않는다)
                    var kv = value.Split(':');
                    foreach (var f in Sim.Ship.Furniture.Where(f => f.Type.ToString() == kv[0] && f.Machine != null))
                        f.Machine!.Tier = Math.Clamp(int.Parse(kv[1], CultureInfo.InvariantCulture), 1, Tech.Tiers(f.Type).Count);
                    break;
                }
                case "--modules":
                {
                    // 화면 확인용: 모든 방에 달 수 있는 모듈을 다 단다 (기록되지 않는다)
                    foreach (var spec in Modules.All)
                        foreach (var room in Sim.Ship.RoomsOf(spec.Room).ToList())
                            for (int k = room.Furniture.Count(f => f.Type == spec.Type); k < spec.Max; k++)
                                if (Modules.Spot(Sim, room) is Cell at) Modules.Install(Sim, room, spec.Type, Sim.Crew[0], at);
                    break;
                }
                case "--history":
                {
                    // --history=20:5 → 20일 동안 5일마다 무작위 사고를 겪은 우주선 (헤드리스 --scarcity와 같은 방식)
                    var hb = value.Split(':');
                    int days = int.Parse(hb[0], CultureInfo.InvariantCulture);
                    int every = hb.Length > 1 ? int.Parse(hb[1], CultureInfo.InvariantCulture) : 5;
                    var rng = new Rng(unchecked((int)((uint)Sim.Rng.Range(0, 1 << 30) * 2654435761u ^ 0x5eed1234u)));
                    for (int day = 1; day <= days; day++)
                    {
                        if (day % every == 0) Scenarios.RandomIncident(Sim, rng);
                        for (int i = 0; i < SimTime.TicksPerDay; i++) Sim.Step();
                    }
                    break;
                }
                case "--robot":
                {
                    // 화면 확인용: --robot=번호 → 그 로봇을 고르고 카메라를 옮긴다
                    int ri = int.Parse(value, CultureInfo.InvariantCulture);
                    if (ri >= 0 && ri < Sim.Robots.Robots.Count)
                    {
                        var rb = Sim.Robots.Robots[ri];
                        SelectRobot(rb);
                        Camera.Position = ShipView.ToPx(rb.Position) + new Vector2(Hud.RightColumnWidth * 0.5f, 0f) / Camera.Zoom.X;
                    }
                    break;
                }
                case "--focus":
                    if (Sim.Ship.Rooms.FirstOrDefault(r => r.Type.ToString() == value) is Room fr)
                        Camera.Position = ShipView.ToPx(fr.Center) + new Vector2(Hud.RightColumnWidth * 0.5f, 0f) / Camera.Zoom.X;
                    break;
                case "--tool":
                    if (Enum.TryParse<IncidentTool>(value, out var tool)) Tool = tool;
                    break;
                case "--hazard":
                {
                    // v11.2 화면 확인용: --hazard=GasLeak:LifeSupport (방 종류) · --hazard=CropBlight:Hydroponics (그 방 첫 재배대)
                    //   · --hazard=SolarStorm · --hazard=WorkAccident:0 (승무원 번호) · --hazard=RobotMalfunction:0 (로봇 번호)
                    var bits = value.Split(':');
                    if (!Enum.TryParse<HazardKind>(bits[0], out var hk)) break;
                    Cell at = default;
                    int hid = -1;
                    string harg = bits.Length > 1 ? bits[1] : "";
                    switch (Hazards.Spec(hk).Target)
                    {
                        case HazardTarget.Room when Enum.TryParse<RoomType>(harg, out var rt):
                            at = Sim.Ship.RoomsOf(rt).First().Cells.First(Sim.Ship.IsOpenFloor);
                            break;
                        case HazardTarget.Machine:
                        {
                            var want = hk == HazardKind.CropBlight ? FurnitureType.GrowBed : FurnitureType.MealDispenser;
                            var fs = Sim.Ship.FurnitureOf(want).Where(f => Hazards.MachineAt(Sim, hk, f.Cells[0]) == f).ToList();
                            if (Enum.TryParse<RoomType>(harg, out var mr)) fs = fs.Where(f => f.Room.Type == mr).ToList();
                            if (fs.Count > 0) at = fs[0].Cells[0];
                            break;
                        }
                        case HazardTarget.Hull when Enum.TryParse<RoomType>(harg, out var hr):
                            at = Scenarios.OuterTarget(Sim, hr);
                            break;
                        case HazardTarget.Door:
                            at = Sim.Ship.Doors.First(d => !d.IsExternal && d.RoomA != null && d.RoomB != null).Cell;
                            break;
                        case HazardTarget.Crew:
                        case HazardTarget.Robot:
                            hid = int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
                            if (Hazards.Spec(hk).Target == HazardTarget.Robot && hid < Sim.Robots.Robots.Count) hid = Sim.Robots.Robots[hid].Id;
                            break;
                    }
                    Player.Hazard(Sim, hk, at, hid);
                    break;
                }
                case "--airlow":
                    // v11.2 화면 확인용: 공기 탱크를 그 비율로 (조난 신호 장면) — 기록되지 않는다
                    Sim.Air.Reserve = Sim.Air.ReserveCapacity * float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--hazardmenu":
                    Hud.OpenHazardMenu();
                    break;
                case "--random":
                    SetRandomIncidents(float.Parse(value, CultureInfo.InvariantCulture), persist: false);
                    break;
                case "--view":
                    if (Enum.TryParse<ViewMode>(value, out var vm)) ViewMode = vm;
                    break;
                case "--tutorial": // v12.9 첫 항해 안내
                    Hud.StartTutorial();
                    break;
                case "--campaign": // v12.9 화면 시험: 지금 항해를 캠페인(1)·세대선(2)으로 (기록되지 않는다 — 설정의 '항해 방식'을 쓴다)
                    if (value == "2") Sim.Generation.Enable(); else Sim.Campaign.Start();
                    break;
                case "--summary": // v12.8 시험: 시작하자마자 요약 진행
                    CallDeferred(nameof(StartSummaryDeferred), float.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case "--view2":
                    if (Enum.TryParse<ViewMode>(value, out var vm2)) SecondaryView = vm2;
                    break;
                case "--speed":
                    SetSpeed(int.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case "--pause":
                    Paused = true;
                    break;
                case "--tab":
                    Hud.CrewTab = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--injure": // v12.7 시험: --injure=사람번호:크기:원인
                {
                    var p = value.Split(':', 3);
                    if (p.Length == 3 && int.TryParse(p[0], out int ci) && ci < Sim.Crew.Count)
                        NeedsSystem.AddInjury(Sim.Crew[ci].Vitals, float.Parse(p[1], CultureInfo.InvariantCulture), p[2]);
                    break;
                }
                case "--zoom":
                    Camera.ZoomAt(float.Parse(value, CultureInfo.InvariantCulture), GetViewportRect().Size * 0.5f);
                    break;
                case "--shot":
                    _screenshotPath = value;
                    if (_screenshotFrames < 0) _screenshotFrames = 30;
                    break;
                case "--frames":
                    _screenshotFrames = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
            }
        }
    }

    private void GateIncident(int i)
    {
        var ship = Sim.Ship;
        Cell Floor(RoomType t) => ship.RoomsOf(t).First().Cells.First(ship.IsOpenFloor);
        switch (i)
        {
            case 0: Player.Meteor(Sim, Scenarios.OuterTarget(Sim, RoomType.Storage), 1f); break;
            case 1: Player.Fire(Sim, Floor(RoomType.Galley)); break;
            case 2: Player.BreakAll(Sim, "CoolantPump", true, FaultKind.PumpSeized); break;
            case 3: Player.Meteor(Sim, Scenarios.OuterTarget(Sim, RoomType.Quarters), 0.8f); break;
            case 4: Player.Meteor(Sim, Scenarios.OuterTarget(Sim, RoomType.Power), 0.9f); break;
            case 5: Player.Fire(Sim, Floor(RoomType.Mess)); break;
            case 6: Player.Meteor(Sim, Scenarios.OuterTarget(Sim, RoomType.Hydroponics), 0.9f); break;
        }
    }

    private void TakeScreenshotAndQuit()
    {
        if (_screenshotPath == null) return;
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(_screenshotPath);
        GD.Print($"screenshot saved: {_screenshotPath}");
        GetTree().Quit();
    }
}
