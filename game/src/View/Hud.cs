using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 화면 UI. 전부 직접 그려서(Control 노드 조립 대신) 모양을 자유롭게 다듬을 수 있게 했다.
/// 카드 영역 밖의 클릭은 그대로 월드로 넘어간다(_HasPoint).
/// </summary>
public partial class Hud : Control
{
    public const float RightColumnWidth = 324f;
    /// <summary>펼친 기록 카드 높이 (사고 카드 · 미니맵 띠가 이 높이에 맞춘다).</summary>
    public const float LogFullHeight = 190f;
    /// <summary>
    /// 지금 왼쪽 아래 기록 카드가 차지하는 높이 — 조용한 HUD에서 접히면 낮아지고, 그 위의 컴퓨터 카드가 따라 내려와
    /// 늘 화면 왼쪽 아래 가장 잘 보이는 자리에 붙는다.
    /// </summary>
    public float LogHeight => Quiet && !_logOpen ? _logFoldedH : LogFullHeight;
    private float _logFoldedH = 36f;
    /// <summary>왼쪽 아래 컴퓨터 카드 너비 (HudComputer와 같은 값 — 다른 띠가 그 자리를 비워 둔다).</summary>
    private const float ComputerCardWidth = 318f;
    public const float TopHeight = 110f;
    private const float Margin = 16f;
    private const int LogRows = 7;

    private Main _main = null!;
    private World _world = null!;
    private readonly List<Rect2> _cards = new();
    private readonly List<(Rect2 rect, Action action)> _buttons = new();
    private float _time;
    private int _crewTab = CardTab;

    /// <summary>승무원 상세의 탭 (0 상태, 1 판단, 2 관계, 3 기억, 4 몸·일기, 5 물건, 6 요약 카드 — v16.2 기본).</summary>
    public int CrewTab { get => _crewTab; set => _crewTab = Math.Clamp(value, 0, CardTab); }

    public void Init(Main main, World world)
    {
        _main = main;
        _world = world;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        SetupPauseTint(main); // v16.2 일시정지는 채도로
    }

    public bool IsOverUi(Vector2 screen)
    {
        foreach (var r in _cards)
            if (r.HasPoint(screen)) return true;
        return false;
    }

    public override bool _HasPoint(Vector2 point) => IsOverUi(point);

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } wheel && (ScrollChronicle(wheel) || ScrollChain(wheel) || ScrollLog(wheel)))
        {
            AcceptEvent();
            return;
        }
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            // 나중에 그린 것(위에 떠 있는 메뉴)이 먼저 받는다
            for (int i = _buttons.Count - 1; i >= 0; i--)
            {
                var (rect, action) = _buttons[i];
                if (!rect.HasPoint(mb.Position)) continue;
                action();
                break;
            }
            AcceptEvent();
        }
    }

    private Vector2 Screen => GetViewportRect().Size;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (Size != Screen) Size = Screen;
        _watch.Sample(_world); // v16.2 자원 추세 (10분마다, 읽기만)
        UpdatePauseTint((float)delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        _cards.Clear();
        _buttons.Clear();
        _tip = null;
        var mouse = GetLocalMousePosition();
        MeasureLog(); // v16.2 접힌 기록 높이 → 컴퓨터 카드가 그 바로 위에

        float topRight = DrawTopBar(mouse);
        DrawStatus(topRight + 10f, mouse);
        // v16.2 조용한 HUD: 보기 · 사고 도구는 접어 두고 열 때만
        if (!Quiet || _toolsOpen || _hazardMenu || _main.Tool != IncidentTool.None)
        {
            float viewRight = DrawViewModes(mouse);
            DrawIncidentTools(viewRight + 10f, mouse);
            if (Quiet) DrawToolsFoldButton(_toolsRight + 6f, mouse);
        }
        else DrawToolsFolded(mouse);
        DrawProfile();

        // v16.2 조용한 HUD: 승무원은 살펴볼 사람만 (머리글을 누르면 모두)
        // 승무원 카드가 주인공: 조용한 HUD에서 누군가를 고르면 위 칸은 줄여 카드에 자리를 준다
        float y = Quiet && !_rosterOpen ? DrawCrewSummary(mouse, _main.SelectedCrew != null ? 2 : 6) : DrawRoster(mouse);
        if (Quiet && _rosterOpen) DrawRosterFold(y, mouse);
        float room = Screen.Y - y - 10f - Margin - 40f; // 오른쪽 아래 ? 단추 · 힌트 자리
        if (_main.SelectedCrew is CrewMember crew) DrawCrewInspector(crew, y + 10f, mouse);
        else if (_main.SelectedRobot is Robot robot) DrawRobotInspector(robot, y + 10f, room);
        else if (_main.SelectedFurniture is Furniture f) { if (!DrawFurnitureCodex(f, y + 10f, room, mouse)) DrawMachineInspector(f, y + 10f, room, mouse); }
        else if (_main.SelectedRoom is Room r) { if (!DrawRoomCodex(r, y + 10f, room, mouse)) DrawRoomInspector(r, y + 10f, mouse); }
        else
        {
            // 아무도 고르지 않았으면: 지금 눈여겨볼 한 사람의 작은 승무원 카드 (목표 · 이유 사슬 · 믿음) — 조용한 HUD에서도 늘
            float sy = DrawCrewSpotlight(y + 10f, mouse);
            if (!Quiet || _workOpen || _world.Board.Open.Any(o => o.Urgency >= 0.9f)) DrawWorkBoard(sy + 10f, Screen.Y - sy - 20f - Margin - 40f, mouse); // 조용한 HUD: 긴급 작업만 떠오른다
        }

        DrawComputerCard(mouse); // v16.0 ④ 주컴퓨터 상시 카드 (HudComputer.cs)
        DrawCosmicPanel(mouse); // v18.13 우주 대재난 예보 (HudCosmic.cs)
        DrawLog(mouse);
        DrawMinimap(); // v11.3
        if (MinimapOpen && !ChronicleOpen && !TechOpen && _minimapRect.Size.X > 0f) DrawVoyageBar(_minimapRect); // v12.8 항로
        DrawIncidentCards(mouse); // v12.2 사고 카드
        if (_world.Causes.Notable().Any()) DrawTimeBar(mouse); // v12.2 시간 막대
        if (CouncilOpen) DrawCouncil(mouse); // v18.18 회의록 · 안건 · 파벌 (HudCouncil.cs)
        else if (PolicyOpen) DrawPolicy(mouse); // v13.2 방침·회의 화면
        else if (ControlOpen) DrawControl(mouse); // v12.5 관제 화면
        else if (ChainOpen) DrawChain(mouse); // v12.2 인과 사슬
        else if (ChronicleOpen) DrawChronicle(mouse);
        else if (TechOpen) DrawTech(mouse);
        else if (ScaleCodexOpen) DrawScaleCodex(mouse); // v16.18 사고 도감 — 규모별 (HudScale.cs)
        DrawHelpCorner(mouse); // v16.2 단축키 한 줄 대신 ? 도움말 + 상황 힌트
        DrawBanners();
        DrawSummaryCard(mouse); // v12.8 요약 진행
        DrawCampaign(); // v12.9 임무
        DrawTutorial(mouse); // v12.9 첫 항해 안내
        if (_hazardMenu) DrawHazardMenu(_hazardMenuAt, mouse); // v11.2 떠 있는 메뉴는 맨 위에
        if (HelpOpen) DrawHelp(mouse); // v16.2
        _tip?.Invoke(); // v16.2 툴팁은 맨 위에
        DrawScaleFrame(); // v16.18 지금 가장 큰 사고의 규모로 화면 테두리
        DrawPauseFrame(); // v16.2 일시정지 테두리
    }

    // ─────────────────────────────── 공통 ───────────────────────────────

    /// <summary>패널 (v16.2 UiKit.Panel) + 그 자리는 화면 UI (클릭이 월드로 새지 않게).</summary>
    private void Card(Rect2 rect, Tone? accent = null)
    {
        UiKit.Panel(this, rect, accent);
        _cards.Add(rect);
    }

    private void Button(Rect2 rect, string label, bool active, Vector2 mouse, Action action, int size = 13)
    {
        bool hover = rect.HasPoint(mouse);
        var bg = active ? Palette.Accent.WithAlpha(0.16f) : hover ? Ui.Hover : new Color(1, 1, 1, 0f);
        Gfx.RoundRect(this, rect, bg, Ui.RadiusControl, active ? Palette.Accent.WithAlpha(0.45f) : null);
        Gfx.TextCentered(this, Fonts.Bold, rect.GetCenter(), label, size, active ? Palette.Accent : hover ? Palette.Text : Palette.TextDim);
        _buttons.Add((rect, action));
    }

    private void Divider(float x0, float x1, float y) => UiKit.Divider(this, x0, x1, y);

    /// <summary>칸 제목 (v16.2 UiKit.Header — 제목마다 아이콘이 붙는다).</summary>
    private void SectionTitle(float x, float y, string text, string? icon = null) =>
        UiKit.Header(this, x, x + RightColumnWidth, y, text, null, icon ?? SectionIcon(text));

    /// <summary>칸 제목 → 아이콘 (같은 뜻은 어디서나 같은 그림).</summary>
    private static string? SectionIcon(string title) =>
        title.StartsWith("승무원") ? "people" : title.StartsWith("관계") || title.StartsWith("왜 그런 사이") ? "relation"
        : title.StartsWith("기술") || title.StartsWith("자격") ? "skill-engineering" : title.StartsWith("목표") ? "target"
        : title.StartsWith("지금 연결") ? "cable" : title.StartsWith("지금") ? "clock" : title.StartsWith("할 일 후보") ? "why"
        : title.StartsWith("지나온 일") || title.StartsWith("지금까지") || title.StartsWith("이 배에서") ? "memory"
        : title.StartsWith("이력") || title.StartsWith("일기") || title.StartsWith("회의록") ? "log"
        : title.StartsWith("있는 사람") ? "crew" : title.StartsWith("설비") || title.StartsWith("부품") ? "parts"
        : title.Contains("작업") ? "work" : title.StartsWith("보관") || title.StartsWith("가진 것") ? "materials"
        : title.StartsWith("몸") ? "health" : title.StartsWith("두려움") || title.StartsWith("무서운 곳") ? "panic"
        : title.StartsWith("성격") ? "stress" : title.StartsWith("함께 넘긴") ? "incident" : title.StartsWith("습관") ? "rest"
        : title.StartsWith("취미") ? "game-table" : title.StartsWith("말버릇") ? "social" : title.StartsWith("칭호") ? "star"
        : title.StartsWith("파벌") ? "people" : null;

    /// <summary>굵은 글씨 기준으로 너비에 맞춰 접는다 (v16.2 UiKit.Wrap).</summary>
    private static List<string> WrapText(string text, float width, int size) => UiKit.Wrap(text, width, size, Fonts.Bold);

    private void Row(float x, float right, float y, string label, float value, Color color, string valueText, bool alarm = false) =>
        UiKit.Row(this, x, right, y, label, value, color, valueText, alarm);

    private static string Pct(float v) => $"{Mathf.RoundToInt(v * 100)}%";

    // ─────────────────────────────── 상단 ───────────────────────────────

    private float DrawTopBar(Vector2 mouse)
    {
        string name = _world.Ship.Name;
        string day = $"{_world.Day}일차";
        string[] labels = { "II", "1×", "3×", "10×", "30×" };

        float nameW = Gfx.Width(Fonts.Bold, name, Ui.TextTitle);
        float clockW = Gfx.Width(Fonts.Bold, "00:00", Ui.TextClock);
        float dayW = Gfx.Width(Fonts.Body, day, Ui.TextBody);
        const float btnW = 40f, btnGap = 4f;
        float speedW = labels.Length * btnW + (labels.Length - 1) * btnGap;
        float w = 18 + 16 + nameW + 20 + 20 + clockW + 8 + dayW + 20 + 14 + speedW + 8 + 52 + 12;

        var card = new Rect2(Margin, Margin, w, 52f);
        Card(card);
        float cy = card.GetCenter().Y;
        float x = card.Position.X + 18;

        var status = _main.Paused ? Palette.Warning : Palette.Good;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 3f);
        DrawCircle(new Vector2(x + 4, cy), 7f, status.WithAlpha(0.12f + 0.12f * pulse), true, -1f, true);
        DrawCircle(new Vector2(x + 4, cy), 3.5f, status, true, -1f, true);
        x += 16;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextTitle)), name, Ui.TextTitle, Palette.Text);
        x += nameW + 20;
        DrawLine(new Vector2(x, card.Position.Y + 13), new Vector2(x, card.End.Y - 13), Palette.PanelBorder, 1f);
        x += 20;

        float baseline = cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextClock);
        Gfx.Text(this, Fonts.Bold, new Vector2(x, baseline), _world.Clock, Ui.TextClock, Palette.Text);
        x += clockW + 8;
        Gfx.Text(this, Fonts.Body, new Vector2(x, baseline), day, Ui.TextBody, Palette.TextDim);
        x += dayW + 20;
        DrawLine(new Vector2(x, card.Position.Y + 13), new Vector2(x, card.End.Y - 13), Palette.PanelBorder, 1f);
        x += 14;

        for (int i = 0; i < labels.Length; i++)
        {
            var rect = new Rect2(x + i * (btnW + btnGap), cy - 14f, btnW, 28f);
            bool active = i == 0 ? _main.Paused : !_main.Paused && _main.SpeedIndex == i - 1;
            int speed = i - 1;
            Button(rect, labels[i], active, mouse, i == 0 ? _main.TogglePause : () => _main.SetSpeed(speed));
        }
        // v12.2 하이라이트 모드: 배속을 저절로
        var auto = new Rect2(x + labels.Length * (btnW + btnGap) + 4, cy - 14f, 52f, 28f);
        Button(auto, _main.SlowMotion ? "느리게" : "자동", _main.Highlight, mouse, _main.ToggleHighlight, Ui.TextBody);
        return card.End.X;
    }

    /// <summary>우주선 상태 한 줄: 전력, 배터리, 공기, 물, 식량, 작업.</summary>
    private void DrawStatus(float x0, Vector2 mouse)
    {
        var p = _world.Power;
        var ship = _world.Ship;
        var living = ship.Rooms.Where(r => !r.Abandoned).DefaultIfEmpty(ship.Rooms[0]).ToList(); // 포기한 구획은 빼고
        float avgO2 = living.Sum(r => r.Air.O2 * r.Volume) / living.Sum(r => r.Volume);
        float maxCO2 = living.Max(r => r.Air.CO2);
        int meals = ship.CountStored(ItemKind.Meal);
        int produce = ship.CountStored(ItemKind.Produce);
        int urgent = _world.Board.Open.Count(o => o.Urgency >= 0.9f);

        var chips = new List<(string label, string value, Color color, float? bar)>
        {
            p.ReactorOnline && p.LowPowerMode
                ? ("전력", $"{p.Delivered:0}/{p.ReactorLimit:0} kW · 저출력 수동", Palette.Warning, null)
            : p.ReactorOnline
                ? ("전력", p.ReactorRamp < 1f ? $"{p.Delivered:0}/{p.ReactorLimit:0} kW · 재기동 {Pct(p.ReactorRamp)}"
                        : p.Brownout ? $"{p.Delivered:0}/{p.ReactorLimit:0} kW · 저출력 운영" : $"{p.Delivered:0}/{p.ReactorLimit:0} kW",
                    p.ShedCount > 0 ? Palette.Danger : p.Brownout || p.Delivered > p.ReactorLimit ? Palette.Warning : Palette.Text, null)
                : ("전력", p.AuxRunning ? $"원자로 정지 · 보조 {p.AuxOutput:0.0} kW" : "원자로 정지", Palette.Danger, null),
            ("배터리", p.BatteryFlow < -0.1f ? $"{Pct(p.BatteryPercent)} 방전" : Pct(p.BatteryPercent),
                p.BatteryPercent < 0.2f ? Palette.Danger : p.BatteryFlow < -0.1f ? Palette.Warning : Palette.Text, p.BatteryPercent),
            ("산소", $"{avgO2:0.0} kPa", avgO2 < 17f ? Palette.Danger : avgO2 < 19.5f ? Palette.Warning : Palette.Text, null),

            ("공기 탱크", Pct(_world.Air.Reserve / _world.Air.ReserveCapacity),
                _world.Air.Reserve < _world.Air.ReserveCapacity * 0.25f ? Palette.Danger : _world.Air.Reserve < _world.Air.ReserveCapacity * 0.6f ? Palette.Warning : Palette.Text,
                _world.Air.Reserve / _world.Air.ReserveCapacity),
            ("물", $"{_world.Water.Level:0} L", _world.Water.Level < 60f ? Palette.Warning : Palette.Text, _world.Water.Level / _world.Water.Capacity),
            ("식량", $"식사 {meals} · 채소 {produce}", meals + produce < _world.Crew.Count * 3 ? Palette.Warning : Palette.Text, null),
            ("작업", urgent > 0 ? $"{_world.Board.OpenCount}건 · 긴급 {urgent}" : $"{_world.Board.OpenCount}건", urgent > 0 ? Palette.Danger : Palette.Text, null),
        };
        // 우주선이 설계와 달라진 곳 (있을 때만): 임시 배선, 절전으로 내린 회로, 뜯긴 설비, 임시 침실
        var changed = new List<string>();
        int jumpers = p.Jumpers.Count(j => j.Active);
        if (jumpers > 0) changed.Add($"배선 {jumpers}");
        var off = Enumerable.Range(0, PowerGrid.CircuitCount).Where(i => p.ManualOff[i]).Select(PowerGrid.CircuitName).ToList();
        if (off.Count > 0) changed.Add($"절전 {string.Join("·", off)}");
        int stripped = ship.Machines.Count(m => m.Has(FaultKind.Stripped));
        if (stripped > 0) changed.Add($"뜯김 {stripped}");
        int dorms = ship.Rooms.Count(r => r.Purpose == "임시 침실");
        if (dorms > 0) changed.Add($"임시 침실 {dorms}");
        if (changed.Count > 0) chips.Add(("땜질", string.Join(" · ", changed), new Color("#e0b64a"), null));
        // v8 구조와 드론: 하중이 몰린 방, 떨어져 나간 방, 드론 출동
        var st = _world.Structure;
        var structure = new List<string>();
        var stressed = ship.LiveRooms.Where(r => r.DesignJoints > 0 && r.Jettison == null && !(r.Docked && !r.Wreck) && StructureSystem.StressOf(StructureSystem.KnownCapacity(r), r.DesignJoints, StructureSystem.FrameLost(_world, r)) > 1f).ToList();
        var jett = ship.Rooms.Where(r => r.Jettison != null).ToList();
        if (jett.Count > 0) structure.Add($"사출 준비 {string.Join("·", jett.Select(r => r.Name))}");
        var frags = st.Fragments.Where(f => f.State != FragmentState.Lost).ToList();
        if (frags.Count > 0) structure.Add($"분리 {string.Join("·", frags.Select(f => f.Room.Name))}");
        if (stressed.Count > 0) structure.Add($"하중 {string.Join("·", stressed.Select(r => r.Name))}");
        var weak = ship.LiveRooms.Where(r => r.DesignJoints > 0 && r.Jettison == null && !stressed.Contains(r) && !r.Docked
                                             && r.Joints.Any(j => !j.Released && j.Known < 0.6f)).ToList();
        if (weak.Count > 0) structure.Add($"약함 {string.Join("·", weak.Select(r => r.Name))}");
        var docked = ship.LiveRooms.Where(r => r.Docked && !r.Wreck).ToList();
        if (docked.Count > 0) structure.Add($"도킹 {string.Join("·", docked.Select(r => r.Name))}");
        int unseen = st.Unseen.Count;
        if (unseen > 0) structure.Add($"미확인 {unseen}");
        var drones = _world.Drones.Drones;
        int outside = drones.Count(d => d.State is not (DroneState.Docked or DroneState.Lost));
        int broken = drones.Count(d => d.Faulty || d.Wrecked || d.State is DroneState.Adrift or DroneState.Lost);
        int eva = _world.Crew.Count(c => c.Outside && !c.Dead);
        string droneText = $"드론 {drones.Count - broken}/{drones.Count}" + (outside > 0 ? $" · 출동 {outside}" : "") + (eva > 0 ? $" · EVA {eva}" : "");
        // v10.10 선내 로봇 (있을 때)
        var robots = _world.Robots.Robots;
        int robotsDown = robots.Count(r => r.Fault != null || r.State is RobotState.Stalled or RobotState.Towed or RobotState.Lost);
        if (robots.Count > 0) droneText += $" · 로봇 {robots.Count - robotsDown}/{robots.Count}";
        broken += robotsDown;
        chips.Add(("구조", structure.Count > 0 ? string.Join(" · ", structure) + " · " + droneText : "정상 · " + droneText,
            frags.Count > 0 || jett.Count > 0 || stressed.Count > 0 ? Palette.Danger : broken > 0 || unseen > 0 || weak.Count > 0 || docked.Count > 0 ? Palette.Warning : Palette.Text, null));
        // CO2는 짙어질 때만 (자리가 모자라다)
        if (maxCO2 > 0.5f) chips.Insert(3, ("CO2", $"{maxCO2:0.00}", maxCO2 > 1.5f ? Palette.Danger : maxCO2 > 0.8f ? Palette.Warning : Palette.Text, null));
        // v9 냉각: 냉각수·노심 온도·이상 있는 배관
        var net = _world.Piping;
        if (net.Built)
        {
            var bad = net.Segments.Where(x => !x.Sound || x.Closed || x.Leaking || x.RadiatorCondition < 0.7f).ToList();
            string cool = $"{net.CoolantFraction * 100:0}% · {p.ReactorTemperature:0}℃" + (bad.Count > 0 ? $" · 배관 {bad.Count}" : "");
            var cc = p.ReactorTemperature >= PowerGrid.OverheatWarnC || net.CoolantFraction < 0.5f || bad.Any(x => x.Leaking && x.IsCoolant) ? Palette.Danger
                : bad.Count > 0 || net.CoolantFraction < 0.8f ? Palette.Warning : Palette.Text;
            chips.Insert(Math.Max(0, chips.Count - 1), ("냉각수·노심", cool, cc, net.CoolantFraction));
        }
        // v9.2 자동화: 꺼졌을 때만 (격벽·댐퍼·경보·부하를 손으로)
        var auto = _world.Automation;
        if (auto.Present && !auto.MainOnline)
            chips.Insert(Math.Max(0, chips.Count - 1), ("자동화", auto.BackupActive ? "꺼짐 · 예비 제어기" : "꺼짐 · 손으로", Palette.Danger, null));
        // v10.1 통신실: 다가오는 운석 (관찰자는 던진 걸 안다 — 배가 언제 알아채는지는 따로), 센서가 멈췄을 때
        var sens = _world.Sensors;
        if (sens.Incoming.Count > 0)
        {
            var next = sens.Incoming.OrderBy(m => m.Arrive).First();
            string seen = next.Warned == WarnLevel.None ? "배는 아직 모른다" : SensorSystem.LevelName(next.Warned) + (next.Warned >= WarnLevel.Manual ? $" · {next.Room?.Name ?? "선체"}" : "");
            chips.Insert(0, ("운석 접근", $"{next.MinutesLeft(_world.Tick):0.0}분 · {seen}" + (sens.Incoming.Count > 1 ? $" 외 {sens.Incoming.Count - 1}" : ""), Palette.Danger, null));
        }
        else if (sens.Array != null && !sens.Tracking)
            chips.Insert(Math.Max(0, chips.Count - 1), ("센서", !sens.Online ? "꺼짐 · 경보는 창밖을 보는 사람뿐" : sens.Operator != null ? "수동 판독 중" : "궤적 계산 없음 · 통신실이 비었다", Palette.Warning, null));
        // v10.10 비축 방침 (평시가 아닐 때) · v11.0 알아챈 전조
        var ledger = _world.Ledger;
        if (ledger.Mode != StockMode.Normal)
            chips.Insert(Math.Max(0, chips.Count - 1), ("비축", Logistics.ModeName(ledger.Mode) + (ledger.ModeWhy.Length > 0 ? $" · {ledger.ModeWhy}" : ""),
                ledger.Mode == StockMode.Extreme ? Palette.Danger : Palette.Warning, null));
        // v11.2 항로·추진: 잔해 지대이거나, 연소 중이거나, 추진제가 모자랄 때만
        var prop = _world.Propulsion;
        if (prop.Zone != ZoneKind.Normal || prop.Burning || prop.Propellant < prop.EvadeCost * 2f)
            chips.Insert(Math.Max(0, chips.Count - 1), ("항로", $"{PropulsionSystem.ZoneName(prop.Zone)}" + (prop.Burning ? " · 연소" : "") + $" · 추진제 {prop.Propellant / Math.Max(1f, prop.Capacity) * 100:0}%",
                prop.Burning ? new Color("#ffb070") : prop.Zone == ZoneKind.Debris ? Palette.Warning : Palette.Text, prop.Propellant / Math.Max(1f, prop.Capacity)));
        // v11.2 사고: 태양 폭풍, 가스가 찬 방, 병충해, 무작위 사고가 켜져 있으면
        var hz = _world.Hazards;
        if (hz.StormActive)
            chips.Insert(0, ("태양 폭풍", $"{hz.StormHoursLeft:0.0}시간 · 센서 흐림 · 선외 금지", new Color("#c9a0ff"), null));
        var gassed = ship.Rooms.Where(r => r.Air.Toxin > 0.15f).ToList();
        if (gassed.Count > 0)
            chips.Insert(Math.Max(0, chips.Count - 1), ("유독 가스", string.Join("·", gassed.Take(2).Select(r => r.Name)) + (gassed.Count > 2 ? $" 외 {gassed.Count - 2}" : "") + $" · {gassed.Max(r => r.Air.Toxin) * 100:0}%", new Color("#b5e34d"), null));
        // v11.2 교신: 보급 캡슐 · 구조 요청 · 탈출 캡슐
        var cms = _world.Comms;
        if (cms.SupplyDocked) chips.Insert(Math.Max(0, chips.Count - 1), ("보급 캡슐", "에어락에 붙었다 · 짐을 내린다", new Color("#e0b64a"), null));
        else if (cms.SupplyEta >= 0) chips.Insert(Math.Max(0, chips.Count - 1), ("보급 캡슐", $"{(cms.SupplyEta - _world.Tick) / (float)SimTime.TicksPerHour:0}시간 뒤", new Color("#e0b64a"), null));
        if (cms.SignalOpen) chips.Insert(0, ("구조 요청", $"생존자 {cms.SignalSurvivors}명 · {(cms.SignalUntil - _world.Tick) / (float)SimTime.TicksPerHour:0}시간 안에", new Color("#f47b7b"), null));
        else if (cms.PodEta >= 0) chips.Insert(Math.Max(0, chips.Count - 1), ("탈출 캡슐", $"{(cms.PodEta - _world.Tick) / (float)SimTime.TicksPerHour:0.0}시간 뒤 도킹", new Color("#f47b7b"), null));
        // v10.11 배급 · 먹을 것이 사흘치 아래
        float foodDays = FoodPolicy.FoodDays(_world);
        if (_world.Food.Rationing)
            chips.Insert(Math.Max(0, chips.Count - 1), ("배급", $"먹을 것 {foodDays:0.0}일치 · {(_world.Tick - _world.Food.RationingSince) / (float)SimTime.TicksPerDay:0.0}일째", Palette.Warning, null));
        else if (foodDays < 3f)
            chips.Insert(Math.Max(0, chips.Count - 1), ("먹을 것", $"{foodDays:0.0}일치", foodDays < 2f ? Palette.Danger : Palette.Warning, null));
        int blight = ship.Machines.Count(m => m.Crop is { BlightKnown: true });
        if (blight > 0) chips.Insert(Math.Max(0, chips.Count - 1), ("병충해", $"재배대 {blight}곳", Palette.Warning, null));
        int omens = ship.Machines.Count(m => m.Omen is { Known: true });
        if (omens > 0) chips.Insert(Math.Max(0, chips.Count - 1), ("전조", $"{omens}건 · 손볼 것", Palette.Warning, null));
        // (재료·채집은 함선 지표 카드에)

        DrawStatusChips(x0, chips, mouse); // v16.2 조용한 HUD · 아이콘 · 추세 · "n시간 뒤 부족" (HudResources.cs)
    }

    /// <summary>함선 지표 (처음 출발할 때 = 100): 사고와 개조를 거치며 어떤 건 떨어지고 어떤 건 오른다.</summary>
    private void DrawProfile()
    {
        if (_world.InitialProfile is not ShipProfile basis) return;
        if (_world.Tick % 30 == 0 || _profile == null) _profile = ShipProfile.Measure(_world).RelativeTo(basis);
        var values = _profile.Values;
        if (Quiet && !_toolsOpen && values.All(v => v >= 90f)) return; // v16.2 조용한 HUD: 떨어진 지표가 있을 때만 떠오른다
        var widths = ShipProfile.Names.Select((n, i) => Gfx.Width(Fonts.Body, n, Ui.TextTiny) + 4 + Gfx.Width(Fonts.Bold, $"{values[i]:0}", Ui.TextBody) + 14).ToList();
        // 재료: 기본 수리재·부품 재고와 채집 (평소엔 천천히 쌓이고, 큰 사고는 몇 주 치를 쓴다)
        var ship = _world.Ship;
        int basic = ItemKinds.All.Where(k => ItemKinds.Tier(k) == ItemTier.Basic).Sum(ship.CountStored);
        int parts = ItemKinds.All.Where(k => ItemKinds.Tier(k) is ItemTier.General or ItemTier.Advanced).Sum(ship.CountStored);
        bool collecting = ship.FurnitureOf(FurnitureType.Collector).Any(f => f.Machine!.Efficiency > 0f);
        string mats = $"수리재 {basic} · 부품 {parts} · 채집 {(collecting ? _world.Space.DensityName : "멈춤")}";
        float titleW = Gfx.Width(Fonts.Body, "함선 지표 · 처음 = 100", Ui.TextTiny) + 16 + Gfx.Width(Fonts.Bold, mats, Ui.TextTiny);
        string evo = EvolutionSummary();
        titleW = Mathf.Max(titleW, Gfx.Width(Fonts.Body, evo, Ui.TextTiny));
        // v12.4 이야기꾼: 성격·난이도·긴장·여력·다음 사고까지
        string story = StoryLine();
        titleW = Mathf.Max(titleW, Gfx.Width(Fonts.Body, story, Ui.TextTiny));
        var card = new Rect2(Margin, Margin + 52f + 8f + 40f + 8f, 28 + Mathf.Max(widths.Sum() - 14, titleW), story.Length > 0 ? 80f : 64f);
        Card(card);
        Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 14, card.Position.Y + 16), "함선 지표 · 처음 = 100", Ui.TextTiny, Palette.TextMuted);
        Gfx.TextRight(this, Fonts.Bold, new Vector2(card.End.X - 14, card.Position.Y + 16), mats, Ui.TextTiny,
            basic < 20 || parts < 5 || !collecting ? Palette.Warning : Palette.TextDim);
        float x = card.Position.X + 14;
        for (int i = 0; i < values.Length; i++)
        {
            float v = values[i];
            var col = v >= 104f ? new Color("#6fd3b0") : v >= 90f ? Palette.Text : v >= 70f ? Palette.Warning : Palette.Danger;
            Gfx.Text(this, Fonts.Body, new Vector2(x, card.Position.Y + 36), ShipProfile.Names[i], Ui.TextTiny, Palette.TextDim);
            float nw = Gfx.Width(Fonts.Body, ShipProfile.Names[i], Ui.TextTiny);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + nw + 4, card.Position.Y + 37), $"{v:0}", Ui.TextBody, col);
            x += widths[i];
        }
        // 진화: 겪은 사고에 따라 고쳐 짠 것과 배운 것 (v7)
        Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 14, card.Position.Y + 55), evo, Ui.TextTiny,
            evo.StartsWith("개조: 아직") ? Palette.TextMuted : new Color("#5fd4e8"));
        if (story.Length > 0) Gfx.Text(this, Fonts.Body, new Vector2(card.Position.X + 14, card.Position.Y + 71), story, Ui.TextTiny, new Color("#e0a3ff"));
    }

    private ShipProfile? _profile;

    private string StoryLine()
    {
        var p = Storyteller.Persona;
        if (p == StoryPersona.Off) return "";
        var s = _world.Story;
        float left = s.Next < 0 ? 0f : (s.Next - _world.Tick) / (float)SimTime.TicksPerHour;
        return $"이야기꾼 {Storyteller.PersonaName(p)} · {Storyteller.LevelName(Storyteller.Level)} · 긴장 {s.Tension():0.00} · 여력 {s.Capacity() * 100:0}% · 다음 {(left > 0 ? $"~{left:0}시간" : "곧")}"
               + (s.LastWhat.Length > 0 ? $" · 지난번 {s.LastWhat}" : "");
    }

    private float DrawViewModes(Vector2 mouse)
    {
        var modes = ViewModes.All;
        const float bw = 50f, gap = 4f;
        var card = new Rect2(Margin, Margin + 52f + 8f, 14 + modes.Length * (bw + gap) - gap + 14 + 60, 40f);
        Card(card);
        float x = card.Position.X + 14;
        foreach (var m in modes)
        {
            var mode = m;
            var r = new Rect2(x, card.Position.Y + 6, bw, 28);
            // v12.3 Shift를 누른 채 누르면 겹쳐 보기 (두 번째 보기)
            Button(r, ViewModes.Name(m), _main.ViewMode == m, mouse, () => { if (Input.IsKeyPressed(Key.Shift) && mode != ViewMode.Normal) _main.SecondaryView = _main.SecondaryView == mode ? null : mode; else _main.ViewMode = mode; }, Ui.TextBody);
            if (_main.SecondaryView == m && _main.ViewMode != m) Gfx.RoundRect(this, r.Grow(-1), new Color(0, 0, 0, 0), 8, Palette.Warning.WithAlpha(0.7f));
            x += bw + gap;
        }
        Gfx.Text(this, Fonts.Body, new Vector2(x + 10, card.GetCenter().Y + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), _main.SecondaryView is ViewMode sv ? $"+{ViewModes.Name(sv)}" : "V 전환", Ui.TextSmall, _main.SecondaryView != null ? Palette.Warning : Palette.TextMuted);
        return card.End.X;
    }

    /// <summary>사고 도구: 골라서 우주선을 클릭하면 그 자리에서 사고가 난다.</summary>
    private void DrawIncidentTools(float x0, Vector2 mouse)
    {
        var tools = IncidentTools.All;
        const float bw = 74f, gap = 4f;
        float titleW = Gfx.Width(Fonts.Bold, "사고", Ui.TextSmall) + 14f;
        const float moreW = 84f;
        var card = new Rect2(x0, Margin + 52f + 8f, 14 + titleW + tools.Length * (bw + gap) + moreW + 14, 40f);
        Card(card);
        float cy = card.GetCenter().Y;
        Gfx.Text(this, Fonts.Bold, new Vector2(card.Position.X + 14, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), "사고", Ui.TextSmall, Palette.Danger.WithAlpha(0.8f));
        float x = card.Position.X + 14 + titleW;
        foreach (var t in tools)
        {
            var tool = t;
            var rect = new Rect2(x, card.Position.Y + 6, bw, 28);
            bool active = _main.Tool == t;
            bool hover = rect.HasPoint(mouse);
            var bg = active ? Palette.Danger.WithAlpha(0.18f) : hover ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0f);
            Gfx.RoundRect(this, rect, bg, 8, active ? Palette.Danger.WithAlpha(0.55f) : null);
            string label = IncidentTools.Name(t);
            float lw = Gfx.Width(Fonts.Bold, label, Ui.TextBody);
            float kw = Gfx.Width(Fonts.Body, IncidentTools.Key(t), Ui.TextTiny);
            float lx = rect.GetCenter().X - (lw + 5 + kw) * 0.5f;
            float by = rect.GetCenter().Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextBody);
            Gfx.Text(this, Fonts.Bold, new Vector2(lx, by), label, Ui.TextBody, active ? Palette.Danger : hover ? Palette.Text : Palette.TextDim);
            Gfx.Text(this, Fonts.Body, new Vector2(lx + lw + 5, by), IncidentTools.Key(t), Ui.TextTiny, Palette.TextMuted);
            _buttons.Add((rect, () => _main.ToggleTool(tool)));
            x += bw + gap;
        }
        // v11.2 사고 더 보기 (15가지) + 무작위 사고
        {
            var rect = new Rect2(x, card.Position.Y + 6, moreW, 28);
            bool active = _hazardMenu || _main.Tool == IncidentTool.Hazard;
            bool hover = rect.HasPoint(mouse);
            Gfx.RoundRect(this, rect, active ? Palette.Danger.WithAlpha(0.18f) : hover ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0f), 8,
                active ? Palette.Danger.WithAlpha(0.55f) : null);
            string label = _main.Tool == IncidentTool.Hazard ? Hazards.Name(_main.ToolHazard) : "더 보기";
            Gfx.TextCentered(this, Fonts.Bold, rect.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), label + (_hazardMenu ? " ▴" : " ▾"), Ui.TextBody,
                active ? Palette.Danger : hover ? Palette.Text : Palette.TextDim);
            // 무작위 사고가 켜져 있으면 작은 표시등
            if (HazardSystem.RandomDays > 0f)
                DrawCircle(new Vector2(rect.End.X - 6, rect.Position.Y + 6), 3f, Palette.Danger.WithAlpha(0.6f + 0.4f * Mathf.Sin(_time * 3f)), true, -1f, true);
            _buttons.Add((rect, () => _hazardMenu = !_hazardMenu));
        }
        _hazardMenuAt = new Vector2(x0, card.End.Y + 6f);
        _toolsRight = card.End.X;
    }

    private Vector2 _hazardMenuAt;
    private float _toolsRight;

    private bool _hazardMenu;

    public void OpenHazardMenu() => _hazardMenu = true;

    /// <summary>열려 있었으면 닫고 true.</summary>
    public bool CloseHazardMenu()
    {
        bool was = _hazardMenu || HelpOpen;
        _hazardMenu = false;
        HelpOpen = false; // v16.2 Esc는 도움말도 닫는다
        return was;
    }

    private static readonly (float days, string name)[] RandomModes =
        { (0f, "끔"), (6f, "드물게 · 6일"), (3f, "보통 · 3일"), (1.5f, "잦게 · 하루 반"), (0.5f, "혼돈 · 반나절") };

    private static int RandomModeIndex()
    {
        float d = HazardSystem.RandomDays;
        if (d <= 0f) return 0;
        int best = 1;
        for (int i = 1; i < RandomModes.Length; i++)
            if (Mathf.Abs(RandomModes[i].days - d) < Mathf.Abs(RandomModes[best].days - d)) best = i;
        return best;
    }

    /// <summary>v11.2 사고 더 보기: 대상이 있는 사고는 고르고 클릭, 배 전체 사고는 누르면 바로. 맨 위는 무작위 사고 주기.</summary>
    private void DrawHazardMenu(Vector2 at, Vector2 mouse)
    {
        var all = Hazards.All;
        int cols = all.Length > 30 ? 5 : 3; // v15 사고 70: 다섯 칸
        float bw = all.Length > 30 ? 128f : 150f, bh = all.Length > 30 ? 24f : 30f;
        const float gap = 6f, pad = 14f;
        int rows = (all.Length + cols - 1) / cols;
        var card = new Rect2(at, new Vector2(pad * 2 + cols * bw + (cols - 1) * gap, pad + 26 + 34 + rows * (bh + gap) + 20));
        Card(card);
        float x = card.Position.X + pad, y = card.Position.Y + pad;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 12), "사고 더 보기", Ui.TextLabel, Palette.Danger.WithAlpha(0.85f));
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - pad, y + 12), "● 배 전체 — 누르면 바로  ○ 대상 — 고른 뒤 클릭", Ui.TextTiny, Palette.TextMuted);
        y += 24;
        // 무작위 사고 (기록되는 수치: 되감기·불러오기가 같은 때에 같은 사고를 낸다)
        int mode = RandomModeIndex();
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 19), "무작위 사고", Ui.TextBody, Palette.TextDim);
        float mx = x + 76;
        for (int i = 0; i < RandomModes.Length; i++)
        {
            var (days, name) = RandomModes[i];
            string label = name.Split(" · ")[0];
            float w = Gfx.Width(Fonts.Bold, label, Ui.TextSmall) + 18;
            var r = new Rect2(mx, y + 6, w, 22);
            bool on = i == mode, hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, on ? Palette.Danger.WithAlpha(0.2f) : hover ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0.02f), 7, on ? Palette.Danger.WithAlpha(0.6f) : null);
            Gfx.TextCentered(this, Fonts.Bold, r.GetCenter() + new Vector2(0, Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), label, Ui.TextSmall, on ? Palette.Danger : hover ? Palette.Text : Palette.TextDim);
            float d = days;
            _buttons.Add((r, () => _main.SetRandomIncidents(d)));
            mx += w + 4;
        }
        y += 34;
        for (int i = 0; i < all.Length; i++)
        {
            var spec = all[i];
            var r = new Rect2(x + (i % cols) * (bw + gap), y + (i / cols) * (bh + gap), bw, bh);
            bool active = _main.Tool == IncidentTool.Hazard && _main.ToolHazard == spec.Kind;
            bool hover = r.HasPoint(mouse);
            Gfx.RoundRect(this, r, active ? Palette.Danger.WithAlpha(0.18f) : hover ? new Color(1, 1, 1, 0.07f) : new Color(1, 1, 1, 0.025f), 8,
                active ? Palette.Danger.WithAlpha(0.6f) : new Color(1, 1, 1, 0.06f));
            bool ship = spec.Target == HazardTarget.Ship;
            Gfx.Text(this, Fonts.Body, new Vector2(r.Position.X + 10, r.GetCenter().Y + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), ship ? "●" : "○", Ui.TextSmall,
                ship ? Palette.Danger.WithAlpha(0.8f) : Palette.TextMuted);
            Gfx.Text(this, Fonts.Bold, new Vector2(r.Position.X + 26, r.GetCenter().Y + Gfx.CenterOffset(Fonts.Bold, Ui.TextBody)), spec.Name, Ui.TextBody,
                active ? Palette.Danger : hover ? Palette.Text : Palette.TextDim);
            int n = _world.Hazards.Count[i];
            if (n > 0) Gfx.TextRight(this, Fonts.Body, new Vector2(r.End.X - 10, r.GetCenter().Y + Gfx.CenterOffset(Fonts.Body, Ui.TextTiny)), $"{n}번", Ui.TextTiny, Palette.TextMuted);
            var kind = spec.Kind;
            _buttons.Add((r, () =>
            {
                _main.PickHazard(kind);
                if (Hazards.Spec(kind).Target != HazardTarget.Ship) _hazardMenu = false;
            }));
            if (hover) _hazardHover = spec.Hint;
        }
        string foot = _hazardHover ?? (_world.Hazards.RandomCount > 0 ? $"무작위 사고 {_world.Hazards.RandomCount}번 · 마지막: {_world.Hazards.LastRandomText}" : "무작위 사고는 되감기·불러오기에도 같은 때 같은 사고로 난다 (항해 번호 · 수치가 같으면)");
        Gfx.Text(this, Fonts.Body, new Vector2(x, card.End.Y - 12), Clip(foot, card.Size.X - pad * 2, Ui.TextTiny), Ui.TextTiny, Palette.TextMuted);
        _hazardHover = null;
    }

    private string? _hazardHover;

    private static string Clip(string s, float width, int size) => UiKit.Fit(s, width, size);

    // ─────────────────────────────── 오른쪽: 승무원 목록 ───────────────────────────────

    private float DrawRoster(Vector2 mouse)
    {
        var crew = _world.Crew;
        if (crew.Count > 12) return DrawRosterCompact(mouse);
        // v10.1: 승무원이 많으면 줄을 좁힌다 (상세 탭이 화면 밖으로 밀려나지 않게)
        float rowH = crew.Count <= 8 ? 36f : crew.Count <= 12 ? 28f : 24f;
        float x0 = Screen.X - Margin - RightColumnWidth;
        var card = new Rect2(x0, Margin, RightColumnWidth, 38f + crew.Count * rowH + 8f);
        Card(card);

        SectionTitle(x0 + 18, card.Position.Y + 24, "승무원");
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 18, card.Position.Y + 24), $"{crew.Count}명", Ui.TextSmall, Palette.TextMuted);

        for (int i = 0; i < crew.Count; i++)
        {
            var c = crew[i];
            var col = Palette.Crew(c.Id);
            var row = new Rect2(x0 + 8, card.Position.Y + 34 + i * rowH, RightColumnWidth - 16, rowH - 4);
            bool selected = _main.SelectedCrew == c;
            bool hover = row.HasPoint(mouse);
            if (selected) Gfx.RoundRect(this, row, col.WithAlpha(0.1f), 8, col.WithAlpha(0.25f));
            else if (hover) Gfx.RoundRect(this, row, new Color(1, 1, 1, 0.04f), 8);

            float cy = row.GetCenter().Y;
            DrawCircle(new Vector2(row.Position.X + 14, cy), 5f, col, true, -1f, true);
            if (_world.Tick - c.AlertedTick < SimTime.Minutes(3) || c.Vitals.Health < 0.5f)
                DrawCircle(new Vector2(row.Position.X + 18, cy - 4), 2.5f, Palette.Danger, true, -1f, true);
            float nx = row.Position.X + 28;
            Gfx.Text(this, Fonts.Bold, new Vector2(nx, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextSubtitle)), c.Name, Ui.TextSubtitle, Palette.Text);
            nx += Gfx.Width(Fonts.Bold, c.Name, Ui.TextSubtitle) + 7;
            Gfx.Text(this, Fonts.Body, new Vector2(nx, cy + Gfx.CenterOffset(Fonts.Body, Ui.TextSmall)), CrewRoles.Name(c.Role), Ui.TextSmall, Palette.TextMuted);

            string where = c.Room?.Name ?? "";
            float rx = row.End.X - 12;
            float by = cy + Gfx.CenterOffset(Fonts.Body, Ui.TextBody);
            Gfx.TextRight(this, Fonts.Body, new Vector2(rx, by), where, Ui.TextSmall, Palette.TextMuted);
            rx -= Gfx.Width(Fonts.Body, where, Ui.TextSmall) + 6;
            string state = c.Dead ? "사망" : c.CarriedBy != null ? "업혀 감" : c.Down ? "쓰러짐" : c.ActivityLabel;
            var stateColor = c.Dead ? Palette.TextMuted : c.Down ? Palette.Danger : col.Lightened(0.2f);
            Gfx.TextRight(this, Fonts.Bold, new Vector2(rx, by), state, Ui.TextBody, stateColor);
            Icons.Draw(this, Icons.CrewState(c, _world.Tick), new Vector2(rx - Gfx.Width(Fonts.Bold, state, Ui.TextBody) - 10, cy), 13, stateColor); // v16.2

            var target = c;
            _buttons.Add((row, () => _main.Select(_main.SelectedCrew == target ? null : target)));
        }
        return card.End.Y;
    }

    /// <summary>v10.7: 큰 배(13명 이상) — 부서별로 묶어 두 줄로 좁게. 점 색은 사람, 왼쪽 띠 색은 부서.</summary>
    private float DrawRosterCompact(Vector2 mouse)
    {
        var groups = _world.Crew.GroupBy(c => c.Role).OrderBy(g => (int)g.Key).ToList();
        const float rowH = 19f, headH = 17f;
        float colW = (RightColumnWidth - 20f) / 2f;
        float height = 38f + groups.Sum(g => headH + (g.Count() + 1) / 2 * rowH) + 6f;
        float x0 = Screen.X - Margin - RightColumnWidth;
        var card = new Rect2(x0, Margin, RightColumnWidth, height);
        Card(card);
        SectionTitle(x0 + 18, card.Position.Y + 24, "승무원");
        int alive = _world.Crew.Count(c => !c.Dead), down = _world.Crew.Count(c => c.Down && !c.Dead);
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 18, card.Position.Y + 24),
            $"{alive}/{_world.Crew.Count}명" + (down > 0 ? $" · 쓰러짐 {down}" : ""), Ui.TextSmall, down > 0 ? Palette.Danger : Palette.TextMuted);
        float y = card.Position.Y + 34;
        foreach (var g in groups)
        {
            var roleCol = RoleColor(g.Key);
            Gfx.Text(this, Fonts.Bold, new Vector2(x0 + 12, y + 12), $"{CrewRoles.Name(g.Key)} {g.Count()}", Ui.TextTiny, roleCol);
            y += headH;
            int i = 0;
            foreach (var c in g)
            {
                var col = Palette.Crew(c.Id);
                var cell = new Rect2(x0 + 8 + (i % 2) * (colW + 4), y + (i / 2) * rowH, colW, rowH - 2);
                bool selected = _main.SelectedCrew == c;
                if (selected) Gfx.RoundRect(this, cell, col.WithAlpha(0.14f), 5, col.WithAlpha(0.3f));
                else if (cell.HasPoint(mouse)) Gfx.RoundRect(this, cell, new Color(1, 1, 1, 0.05f), 5);
                DrawRect(new Rect2(cell.Position.X, cell.Position.Y + 3, 2, cell.Size.Y - 6), roleCol.WithAlpha(0.7f));
                float cy = cell.GetCenter().Y;
                DrawCircle(new Vector2(cell.Position.X + 10, cy), 4f, c.Dead ? Palette.TextMuted : col, true, -1f, true);
                if (_world.Tick - c.AlertedTick < SimTime.Minutes(3) || c.Vitals.Health < 0.5f)
                    DrawCircle(new Vector2(cell.Position.X + 13, cy - 3), 2f, Palette.Danger, true, -1f, true);
                Gfx.Text(this, Fonts.Bold, new Vector2(cell.Position.X + 18, cy + Gfx.CenterOffset(Fonts.Bold, Ui.TextSmall)), c.Name, Ui.TextSmall, c.Dead ? Palette.TextMuted : Palette.Text);
                string state = c.Dead ? "사망" : c.CarriedBy != null ? "업혀 감" : c.Down ? "쓰러짐" : c.ActivityLabel;
                float nameW = Gfx.Width(Fonts.Bold, c.Name, Ui.TextSmall);
                float room = cell.Size.X - 26 - nameW - 14;
                while (state.Length > 1 && Gfx.Width(Fonts.Body, state, Ui.TextTiny) > room) state = state[..^1];
                var sc = c.Dead ? Palette.TextMuted : c.Down ? Palette.Danger : col.Lightened(0.2f);
                Gfx.TextRight(this, Fonts.Body, new Vector2(cell.End.X - 4, cy + Gfx.CenterOffset(Fonts.Body, Ui.TextTiny)), state, Ui.TextTiny, sc);
                Icons.Draw(this, Icons.CrewState(c, _world.Tick), new Vector2(cell.End.X - 4 - Gfx.Width(Fonts.Body, state, Ui.TextTiny) - 7, cy), 11, sc); // v16.2
                var target = c;
                _buttons.Add((cell, () => _main.Select(_main.SelectedCrew == target ? null : target)));
                i++;
            }
            y += (g.Count() + 1) / 2 * rowH;
        }
        return card.End.Y;
    }

    private static Color RoleColor(CrewRole r) => r switch
    {
        CrewRole.Engineer => new Color("#ff9a6b"),
        CrewRole.Medic => new Color("#f47b7b"),
        CrewRole.Pilot => new Color("#7fb2ff"),
        CrewRole.Technician => new Color("#e0b64a"),
        CrewRole.Botanist => new Color("#8fd65a"),
        CrewRole.Electrician => new Color("#c0a0ff"),
        CrewRole.Cook => new Color("#ffc36b"),
        _ => Palette.TextMuted,
    };

    // ─────────────────────────────── 승무원 상세 (탭) ───────────────────────────────

    private void DrawCrewInspector(CrewMember c, float y, Vector2 mouse)
    {
        var col = Palette.Crew(c.Id);
        float x0 = Screen.X - Margin - RightColumnWidth;
        float w = RightColumnWidth;
        // 요약 카드는 담긴 만큼 (지난 그림에서 잰 높이) · 화면 아래 ? 단추 자리까지
        float avail = Screen.Y - y - Margin - 40f;
        float height = _crewTab == CardTab ? Mathf.Clamp(_crewCardH, 320f, Mathf.Max(320f, avail)) : _crewTab == 3 ? 560f : _crewTab >= 2 ? 600f : 500f;
        _crewCardBottom = y + height - 50f; // 따라가기 단추 위까지
        var card = new Rect2(x0, y, w, height);
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;

        // 머리글
        DrawCircle(new Vector2(x + 8, y + 26), 8f, col, true, -1f, true);
        DrawCircle(new Vector2(x + 8, y + 26) + c.Facing.ToGodot() * 3.5f, 3f, col.Lightened(0.6f), true, -1f, true);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 26, y + 31), c.Name, Ui.TextHeading, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 26 + Gfx.Width(Fonts.Bold, c.Name, Ui.TextHeading) + 8, y + 31),
            CrewRoles.Name(c.Role), Ui.TextBody, Palette.TextDim);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 26, y + 49), UiKit.Fit($"{Life.Name(c.Background)} · {Life.Name(c.Value)} · {c.Traits.Summary()}", right - (x + 26), Ui.TextBody, Fonts.Body), Ui.TextBody, Palette.TextMuted); // v12.7 살아온 길·가치관

        // 탭
        (int tab, string name)[] tabs = { (CardTab, "요약"), (0, "상태"), (1, "판단"), (2, "관계"), (3, "기억"), (4, "몸·일기"), (5, "물건") }; // v16.2 요약 카드가 맨 앞
        float tw = (w - 36 - 3 * (tabs.Length - 1)) / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            int tab = tabs[i].tab;
            Button(new Rect2(x + i * (tw + 3), y + 62, tw, 28), tabs[i].name, _crewTab == tab, mouse, () => _crewTab = tab, Ui.TextSmall);
        }
        float cy = y + 102;
        switch (_crewTab)
        {
            case 0: DrawCrewStatus(c, x, right, cy, col); break;
            case 1: DrawCrewThinking(c, x, right, cy, col); break;
            case 2:
                DrawCrewRelations(c, x, right, cy, col);
                // v12.7 관계도: 카드 왼쪽에 따로 (배 전체가 한눈에)
                var gcard = new Rect2(x0 - 12 - 240, y, 240, 270);
                Card(gcard);
                SectionTitle(gcard.Position.X + 16, y + 22, "관계도 — 초록 가까움 · 빨강 사이가 나쁨 · ! 말다툼");
                DrawRelationGraph(c, gcard.Position.X + 16, gcard.End.X - 16, y + 34);
                break;
            case 4: DrawCrewBody(c, x, right, cy, col); break; // v12.7
            case 5: DrawCrewThings(c, x, right, cy, col); break; // v14.3
            case CardTab: DrawCrewCard(c, x, right, cy, col); break; // v16.2 승무원 카드
            default: DrawCrewMemory(c, x, right, cy, col); break;
        }

        var follow = new Rect2(x, card.End.Y - 42, right - x, 28);
        Button(follow, _main.Following ? "따라가는 중  ·  F" : "따라가기  ·  F", _main.Following, mouse, _main.ToggleFollow, Ui.TextBody);
    }

    private void DrawCrewStatus(CrewMember c, float x, float right, float y, Color col)
    {
        SectionTitle(x, y + 10, "지금");
        string activity = c.Dead ? "사망" : c.CarriedBy != null ? $"{c.CarriedBy.Name}에게 업혀 감" : c.Down ? (c.CareBed != null ? "의식 없음 · 치료 침대" : "쓰러짐 — 구조를 기다림") : c.ActivityLabel;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 32), activity, Ui.TextTitle, col.Lightened(0.2f));
        float ax = x + Gfx.Width(Fonts.Bold, activity, Ui.TextTitle) + 8;
        string where = c.IsMoving && c.Job?.TargetRoom != null
            ? $"{c.Room?.Name ?? "?"} → {c.Job.TargetRoom.Name}"
            : c.Room?.Name ?? "";
        Gfx.Text(this, Fonts.Body, new Vector2(ax, y + 32), where, Ui.TextLabel, Palette.TextDim);
        string reason = c.JobReason ?? "—";
        if (c.Gait.Line(c, _world) is string gait) reason = $"{gait}   ·   {reason}"; // v14.5 비켜서는 중 · 문 앞 확인 · 조용히 · 움찔
        if (c.Soil.Line() is string soil) reason += $"   ·   {soil}"; // v14.7 손에 기름 · 옷에 그을음
        if (_world.Culture.Line(c) is string cul) reason += $"   ·   {cul}"; // v14.9 따르는 관행 (이유를 모르면 그렇다고)
        if (c.Carrying is ItemStack held) reason += $"   ·   들고 있음: {held}";
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 51), reason, Ui.TextSmall, Palette.TextMuted);

        float by = y + 64;
        var n = c.Needs;
        var v = c.Vitals;
        Row(x, right, by, "포만감", n.Food, Palette.NeedFood, Pct(n.Food), n.Food < 0.2f);
        Row(x, right, by + 22, "기력", n.Rest, Palette.NeedRest, Pct(n.Rest), n.Rest < 0.2f);
        Row(x, right, by + 44, "스트레스", n.Stress, Palette.NeedStress, Pct(n.Stress), n.Stress > 0.7f);
        Row(x, right, by + 66, "교류", n.Social, Palette.NeedSocial, Pct(n.Social), n.Social < 0.2f);
        Row(x, right, by + 88, "체력", v.Health, Palette.VitalHealth, Pct(v.Health), v.Health < 0.5f);
        Row(x, right, by + 110, "혈중 산소", v.Oxygen, Palette.VitalOxygen, Pct(v.Oxygen), v.Oxygen < 0.85f);
        if (c.Suit is SuitState suit)
            Row(x, right, by + 132, "우주복 O2", suit.Oxygen / SuitState.TankHours, new Color("#dfe6ee"), $"{suit.Oxygen:0.0}시간", suit.Oxygen < 0.75f);
        else if (v.Injury > 0.01f)
            Row(x, right, by + 132, "부상", v.Injury, Palette.Danger, $"{Pct(v.Injury)} {v.InjuryCause}", v.Injury > 0.3f);
        else if (v.Scar > 0.01f) // v11.3 후유증
            Row(x, right, by + 132, "후유증", v.Scar / 0.3f, new Color("#c9a0ff"), $"{Pct(v.Scar)} {v.ScarCause}" + (v.Scar > v.ScarFloor + 0.005f ? " · 재활로 준다" : " · 남는다"), false);

        var s = c.Schedule;
        // v11.3: 배우고 가르친 것 · 재활 · 건져 온 사람
        string grow = (c.Stats.Lessons > 0 ? $" · 배움 {c.Stats.Lessons}번" : "") + (c.Stats.Taught > 0 ? $" · 가르침 {c.Stats.Taught}번" : "")
                      + (c.Stats.RehabSessions > 0 ? $" · 재활 {c.Stats.RehabSessions}번" : "") + (c.Rescued ? " · 탈출 캡슐에서 건짐" : "");
        Gfx.Text(this, Fonts.Body, new Vector2(x, by + 172),
            $"수면 {SimTime.Range(s.SleepStart, s.SleepLength)}   ·   근무 {SimTime.Range(s.WorkStart, s.WorkLength)}" + grow, Ui.TextBody, Palette.TextMuted);

        SectionTitle(x, by + 198, "기술");
        float colW = (right - x - 16) / 2f;
        for (int i = 0; i < Skills.All.Length; i++)
        {
            var skill = Skills.All[i];
            float sx = x + (i % 2) * (colW + 16);
            float sy = by + 208 + (i / 2) * 20;
            float level = c.SkillLevel(skill);
            Icons.Draw(this, Icons.Skill(skill), new Vector2(sx + 6, sy + 8), 12, level >= 0.7f ? col : Palette.TextDim); // v16.2 기술마다 고유 아이콘
            Gfx.Text(this, Fonts.Body, new Vector2(sx + 15, sy + 12), Skills.Name(skill), Ui.TextSmall, Palette.TextDim);
            UiKit.Gauge(this, new Rect2(sx + 46, sy + 6, colW - 74, 4), level, level >= 0.7f ? col : new Color(1, 1, 1, 0.35f));
            Gfx.TextRight(this, Fonts.Body, new Vector2(sx + colW, sy + 12), $"{Mathf.RoundToInt(level * 100)}", Ui.TextSmall, Palette.Text);
        }
    }

    private void DrawCrewThinking(CrewMember c, float x, float right, float y, Color col)
    {
        // v13.3 마음: 목표 계층 · 감정 · 명령 반응 · 아는 사고
        var mind = c.Mind;
        var w = _world;
        var gc = mind.Goal switch { GoalTier.Survival => Palette.Danger, GoalTier.Role => Palette.Accent, GoalTier.Work => Palette.Good, _ => Palette.TextDim };
        SectionTitle(x, y + 10, "목표");
        Gfx.RoundRect(this, new Rect2(x + 34, y - 1, Gfx.Width(Fonts.Bold, MindSystem.GoalName(mind.Goal), Ui.TextSmall) + 12, 16), gc.WithAlpha(0.2f), 4, gc.WithAlpha(0.7f), 1);
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 40, y + 11), MindSystem.GoalName(mind.Goal), Ui.TextSmall, gc);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 50 + Gfx.Width(Fonts.Bold, MindSystem.GoalName(mind.Goal), Ui.TextSmall), y + 11), Fit(mind.GoalWhy, right - x - 120, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim);
        var feel = new System.Collections.Generic.List<(string, Color)>();
        if (mind.Panicking(w.Tick)) feel.Add((mind.Frozen ? "공황 · 얼어붙음" : "공황 · 달아남", Palette.Danger));
        if (mind.Heroic(w.Tick)) feel.Add(("영웅심", Palette.Accent));
        if (mind.Anger > 0.15f) feel.Add(($"분노 {mind.Anger * 100:0}%", Palette.Warning));
        if (c.GriefUntil > w.Tick) feel.Add(("슬픔", new Color("#90caf9")));
        if (w.Meetings.Guilt(c) > 0.05f) feel.Add(($"죄책감 {w.Meetings.Guilt(c) * 100:0}%", Palette.Danger));
        if (w.Society.IsVeteran(c)) feel.Add(("베테랑", new Color("#cfd8dc")));
        if (w.Society.Suspended(c)) feel.Add(("근무 박탈", Palette.Warning));
        if (w.Society.OnProbation(c)) feel.Add(("수습", Palette.TextMuted));
        float ob = w.Minds.Obedience(c);
        feel.Add(($"지시를 따름 {ob * 100:0}%", ob < 0.45f ? Palette.Warning : Palette.TextMuted));
        float fx = x;
        foreach (var (t, fc) in feel)
        {
            float fw = Gfx.Width(Fonts.Body, t, Ui.TextTiny) + 12;
            if (fx + fw > right) break;
            Gfx.RoundRect(this, new Rect2(fx, y + 18, fw, 16), fc.WithAlpha(0.14f), 4, fc.WithAlpha(0.5f), 1);
            Gfx.Text(this, Fonts.Body, new Vector2(fx + 6, y + 30), t, Ui.TextTiny, fc);
            fx += fw + 4;
        }
        string knows = mind.Knows.Count == 0 ? "아는 사고 없음" : "아는 사고: " + string.Join(" · ", mind.Knows.Values.OrderBy(k => k.tick).Select(k => $"{k.what} ({MindSystem.SourceName(k.src)})"));
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + 50), Fit(knows, right - x, Ui.TextTiny, Fonts.Body), Ui.TextTiny, mind.Knows.Count > 0 ? Palette.Warning : Palette.TextMuted);
        // v13.4 일과표: 24시간 띠 (잠 · 근무 · 정기 회의 · 지금)
        {
            float sx = x + 38, sw = right - sx, sy = y + 58;
            Gfx.Text(this, Fonts.Body, new Vector2(x, sy + 9), "일과", Ui.TextTiny, Palette.TextMuted);
            Gfx.RoundRect(this, new Rect2(sx, sy, sw, 10), new Color(1, 1, 1, 0.05f), 3);
            for (int hh = 0; hh < 24; hh++)
            {
                bool sleep = SimTime.InWindow(hh + 0.5f, c.Schedule.SleepStart, c.Schedule.SleepLength);
                bool work = SimTime.InWindow(hh + 0.5f, c.Schedule.WorkStart, c.Schedule.WorkLength);
                if (!sleep && !work) continue;
                DrawRect(new Rect2(sx + sw * hh / 24f, sy + 1, sw / 24f - 1, 8), sleep ? new Color("#5c7cfa").WithAlpha(0.6f) : new Color("#69db7c").WithAlpha(0.55f));
            }
            float mh = w.Meetings.Hour;
            DrawRect(new Rect2(sx + sw * mh / 24f, sy - 2, 2, 14), new Color("#ffd43b"));
            float now = SimTime.HourOfDay(w.Tick);
            DrawLine(new Vector2(sx + sw * now / 24f, sy - 3), new Vector2(sx + sw * now / 24f, sy + 13), Palette.Text, 1.5f);
        }
        y += 82;

        SectionTitle(x, y + 10, "할 일 후보 — 마음이 기운 정도");
        long ago = (_world.Tick - c.LastThinkTick) * 60 / SimTime.TicksPerHour;
        Gfx.TextRight(this, Fonts.Body, new Vector2(right, y + 10), ago < 1 ? "방금 판단" : $"{ago}분 전 판단", Ui.TextSmall, Palette.TextMuted);

        var evals = c.LastEvaluations;
        float maxScore = Math.Max(1f, evals.Count > 0 ? evals.Max(e => e.Score) : 1f);
        float ey = y + 22;
        foreach (var e in evals.Take(9))
        {
            bool chosen = c.Job?.Activity == e.Activity;
            var labelColor = chosen ? col.Lightened(0.2f) : e.Score <= 0.01f ? Palette.TextMuted : Palette.TextDim;
            Gfx.Text(this, chosen ? Fonts.Bold : Fonts.Body, new Vector2(x, ey + 14), e.Activity.Label, Ui.TextLabel, labelColor);
            string reason = e.Reason;
            while (reason.Length > 4 && Gfx.Width(Fonts.Body, reason, Ui.TextSmall) > right - x - 44 - 40) reason = reason[..^2] + "…";
            Gfx.Text(this, Fonts.Body, new Vector2(x + 44, ey + 14), reason, Ui.TextSmall, Palette.TextMuted);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, ey + 14), e.Score.ToString("0.00"), Ui.TextSmall,
                chosen ? Palette.Text : Palette.TextMuted);
            Gfx.Bar(this, new Rect2(x, ey + 20, right - x, 3f), e.Score / maxScore,
                chosen ? col.WithAlpha(0.9f) : new Color(1, 1, 1, 0.22f), new Color(1, 1, 1, 0.04f));
            ey += 32;
        }
    }

    private void DrawCrewRelations(CrewMember c, float x, float right, float y, Color col)
    {
        SectionTitle(x, y + 10, "관계");
        float ry = y + 20;
        // v12.7 사람이 많으면 가장 가깝거나 가장 먼 여섯만 (나머지는 아래 관계도에)
        var rels = c.Relations(_world).ToList();
        if (rels.Count > 6) rels = rels.OrderByDescending(r => MathF.Abs(r.Item2)).Take(6).OrderByDescending(r => r.Item2).ToList();
        foreach (var (who, value) in rels)
        {
            DrawCircle(new Vector2(x + 5, ry + 10), 4f, Palette.Crew(who.Id), true, -1f, true);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 16, ry + 15), who.Name, Ui.TextLabel, Palette.Text);
            string word = value > 0.5f ? "각별함" : value > 0.25f ? "친함" : value > -0.05f ? "보통" : value > -0.3f ? "서먹함" : "불편함";
            // -1~1 막대 (가운데가 0)
            var bar = new Rect2(x + 80, ry + 8, right - x - 80 - 56, 5);
            Gfx.RoundRect(this, bar, new Color(1, 1, 1, 0.07f), 2.5f);
            float mid = bar.Position.X + bar.Size.X * 0.5f;
            float len = bar.Size.X * 0.5f * Mathf.Abs(value);
            var vc = value >= 0 ? Palette.Good : Palette.Danger;
            DrawRect(value >= 0 ? new Rect2(mid, bar.Position.Y, len, bar.Size.Y) : new Rect2(mid - len, bar.Position.Y, len, bar.Size.Y), vc.WithAlpha(0.8f));
            DrawLine(new Vector2(mid, bar.Position.Y - 2), new Vector2(mid, bar.End.Y + 2), new Color(1, 1, 1, 0.25f), 1f);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, ry + 15), word, Ui.TextBody, value >= 0.25f ? Palette.Good : value < -0.05f ? Palette.Warning : Palette.TextDim);
            // v14.4 왜 그런 사이인가 (그 사람의 기억 — 오해일 수도 있다)
            if (_world.Relations.Why(c, who) is RelationMemory why)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(x + 16, ry + 30), Fit($"— {why.Text}", right - x - 16, Ui.TextTiny, Fonts.Body), Ui.TextTiny, why.Weight >= 0f ? new Color("#9fe0b0") : new Color("#ff9a8a"));
                ry += 13;
            }
            ry += 26;
        }

        ry += 10;
        Divider(x, right, ry);
        SectionTitle(x, ry + 22, "지금까지");
        var st = c.Stats;
        int days = Math.Max(1, _world.Day);
        string[] lines =
        {
            $"식사 {st.Meals}회 · 수면 {st.TicksAsleep / (float)SimTime.TicksPerHour:0}시간 · 대화 {st.Chats}회",
            $"정비 {st.Services}회 · 수리 {st.Repairs}회 · 수확 {st.Harvests}회",
            $"조리 {st.MealsCooked}인분 · 하루 평균 근무 {st.TicksWorking / (float)SimTime.TicksPerHour / days:0.0}시간",
            $"사고 대응 {st.Emergencies}회 · 구조 {st.Rescues}회 · 쓰러짐 {st.TimesDown}회",
        };
        for (int i = 0; i < lines.Length; i++)
            Gfx.Text(this, Fonts.Body, new Vector2(x, ry + 42 + i * 19), lines[i], Ui.TextBody, Palette.TextDim);
    }

    // ─────────────────────────────── 설비 상세 ───────────────────────────────

    private void DrawMachineInspector(Furniture f, float y, float maxHeight, Vector2 mouse)
    {
        var m = f.Machine;
        var accent = Palette.Room(f.Room.Kind);
        float x0 = Screen.X - Margin - RightColumnWidth;
        var orders = _world.Board.Open.Where(o => o.Target.Furniture == f).ToList();
        var extra = MachineExtras(f);
        int invLines = f.Storage?.Contents.Count() ?? 0;
        var history = MachineHistoryLines(f, RightColumnWidth - 36);
        var parts = m != null ? MachineParts.For(f.Type) : null; // v12.3 분해도
        float height = 70 + (m != null ? 118 + Math.Max(1, m.Faults.Count) * 20 : 0) + (parts != null ? 36 + parts.Length * 20 : 0) + extra.Count * 20
                       + (f.Storage != null ? 40 + Math.Max(1, invLines) * 20 : 0) + (orders.Count > 0 ? 34 + orders.Count * 20 : 0) + 16
                       + (history.Count > 0 ? 36 + history.Count * 18 : 0);
        height = Mathf.Min(height, maxHeight);
        var card = new Rect2(x0, y, RightColumnWidth, height);
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;

        Gfx.RoundRect(this, new Rect2(x - 2, y + 15, 22, 22), accent.WithAlpha(0.18f), 5, accent.WithAlpha(0.7f));
        Icons.Draw(this, Icons.Furniture(f.Type), new Vector2(x + 9, y + 26), 16, accent.Lightened(0.25f)); // v16.2 설비마다 고유 아이콘
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 26, y + 32), f.Label, Ui.TextLarge, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 26, y + 50), f.Room.Name, Ui.TextBody, Palette.TextMuted);
        if (FixtureArt.Readout(f, _world) is (string rd, int rl)) // v16.24 가까이 본 계기 숫자 · 무엇을 재나
            Gfx.Text(this, Fonts.Body, new Vector2(x + 34 + Gfx.Width(Fonts.Body, f.Room.Name, Ui.TextBody), y + 50), $"계기 {rd} · {FixtureArt.ReadoutWhat(f)}", Ui.TextSmall,
                rl == 2 ? Palette.Danger : rl == 1 ? Palette.Warning : new Color("#7dffa8").Darkened(0.2f));
        if (Codex.Of(f.Type) != null) CodexButton(right, y + 38, mouse); // v12.2 설명서
        float ly = y + 60;

        if (m != null)
        {
            var statusColor = m.Faults.Count > 0 ? Palette.Danger : m.Wear > 0.6f || (!m.Powered && m.Spec.PowerDraw > 0) ? Palette.Warning : Palette.Good;
            Gfx.TextRight(this, Fonts.Bold, new Vector2(right, y + 32), m.StatusText, Ui.TextBody, statusColor);
            Divider(x, right, ly + 4);
            Row(x, right, ly + 12, "수명", m.Condition, Palette.Good, Pct(m.Condition), m.Condition < 0.4f);
            Row(x, right, ly + 34, "마모", m.Wear, Palette.Severity(m.Wear), Pct(m.Wear), m.Wear > 0.6f);
            Row(x, right, ly + 56, "효율", m.Efficiency, Palette.Accent, Pct(m.Efficiency));
            string power = m.Spec.PowerDraw <= 0f ? "전력 소비 없음"
                : $"{m.Demand:0.0} kW · {PowerGrid.CircuitName(f.Room.Circuit)}회로 · {(m.Powered ? "공급 중" : "끊김")}";
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 96), power, Ui.TextBody, m.Powered || m.Spec.PowerDraw <= 0f ? Palette.TextDim : Palette.Danger);
            string service = m.ServiceCount > 0
                ? $"정비 {m.ServiceCount}회 · 마지막 {(_world.Tick - m.LastServiced) / SimTime.TicksPerHour}시간 전 · 고장 {m.FaultCount}회"
                : $"정비 기록 없음 · 고장 {m.FaultCount}회";
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 114), service, Ui.TextSmall, Palette.TextMuted);
            ly += 124;

            if (m.Faults.Count == 0)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 14), "고장 없음", Ui.TextBody, Palette.TextMuted);
                ly += 20;
            }
            foreach (var fault in m.Faults)
            {
                DrawCircle(new Vector2(x + 4, ly + 10), 3.5f, Palette.Danger, true, -1f, true);
                Gfx.Text(this, Fonts.Bold, new Vector2(x + 14, ly + 15), fault.Name, Ui.TextBody, Palette.Danger);
                var mats = fault.Materials;
                string need = mats.Length > 0 ? string.Join(" + ", mats.Select(x => x.count > 1 ? $"{ItemKinds.Name(x.kind)} {x.count}" : ItemKinds.Name(x.kind))) + " 필요" : "부품 불필요";
                Gfx.TextRight(this, Fonts.Body, new Vector2(right, ly + 15), need, Ui.TextSmall, Palette.TextMuted);
                ly += 20;
            }
            if (parts != null) ly = DrawParts(m, parts, x, right, ly, accent);
        }

        foreach (var (label, value) in extra)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 15), label, Ui.TextBody, Palette.TextDim);
            Gfx.TextRight(this, Fonts.Bold, new Vector2(right, ly + 15), value, Ui.TextBody, Palette.Text);
            ly += 20;
        }
        ly = DrawHistoryLines(history, x, right, ly);

        if (f.Storage is Inventory inv)
        {
            Divider(x, right, ly + 8);
            SectionTitle(x, ly + 28, "보관");
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, ly + 28), $"{inv.Total}/{inv.Capacity}", Ui.TextSmall, Palette.TextMuted);
            ly += 34;
            if (inv.Total == 0)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 15), "비어 있음", Ui.TextBody, Palette.TextMuted);
                ly += 20;
            }
            foreach (var (kind, count) in inv.Contents)
            {
                DrawRect(new Rect2(x, ly + 6, 8, 8), Palette.Item(kind));
                Gfx.Text(this, Fonts.Body, new Vector2(x + 16, ly + 15), ItemKinds.Name(kind), Ui.TextBody, Palette.TextDim);
                Gfx.TextRight(this, Fonts.Bold, new Vector2(right, ly + 15), $"{count}", Ui.TextBody, Palette.Text);
                ly += 20;
            }
        }

        if (orders.Count > 0)
        {
            Divider(x, right, ly + 8);
            SectionTitle(x, ly + 28, "걸린 작업");
            ly += 34;
            foreach (var o in orders)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 15), $"#{o.Id} {o.Title}", Ui.TextBody, Palette.TextDim);
                string who = o.Assignee?.Name ?? (o.BlockedUntil > _world.Tick ? "보류" : "대기");
                Gfx.TextRight(this, Fonts.Bold, new Vector2(right, ly + 15), who, Ui.TextBody, o.Assignee != null ? Palette.Crew(o.Assignee.Id) : Palette.TextMuted);
                ly += 20;
            }
        }
    }

    private List<(string label, string value)> MachineExtras(Furniture f)
    {
        var list = new List<(string, string)>();
        var p = _world.Power;
        RobotExtras(f, list); // v10.10 충전대·비상 물자함·우주복 점검·전조
        if (f.Type == FurnitureType.DroneDock)
        {
            // v8: 이 거치대의 드론과 자재칸
            var ds = _world.Drones;
            if (!_world.Automation.DroneControl)
                list.Add(("관제", ds.Manual ? $"자동화 꺼짐 · {Ko.IGa(ds.Pilot!.Name)} 콘솔에서 한 대씩 손으로 몬다" : "자동화 꺼짐 · 콘솔에 앉을 사람을 기다린다"));
            foreach (var d in ds.Drones.Where(d => d.Dock == f))
            {
                string state = d.State switch
                {
                    DroneState.Docked => d.Wrecked ? "부서짐 · 재조립 필요" : d.Faulty ? "고장 · 수리 필요" : d.Battery < 0.99f ? $"충전 {d.Battery * 100:0}%" : "대기",
                    DroneState.Adrift => "표류 — 견인 드론이 건져 와야 한다",
                    DroneState.Lost => "잃음",
                    _ => d.Doing,
                };
                list.Add((d.Name, $"{state} · 배터리 {d.Battery * 100:0}% · 상태 {d.Condition * 100:0}% · 출동 {d.Sorties}회"));
            }
            list.Add(("자재칸", string.Join(" · ", f.Storage!.Contents.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}")).DefaultIfEmptyText("비었다 — 드론이 나갈 수 없다")));
        }
        if (f.Machine is Machine mm)
        {
            if (mm.Grade == MachineGrade.Mk1)
                list.Add(("등급", "Mk.1 임시품 · 출력 65% · 전력 130% · 마모 1.8배"));
            else if (mm.Grade == MachineGrade.Mk3)
                list.Add(("등급", "Mk.3 개량형 · 출력 115% · 마모 0.7배 · 고장 0.6배"));
            else if (mm.Substitutions > 0)
                list.Add(("등급", $"정품 (Mk.1을 거쳐 {mm.Substitutions}회)"));
            var key = Faults.KeyPart(f.Type);
            list.Add(("핵심 부품", $"{ItemKinds.Name(key)} · 재고 {_world.Ship.CountStored(key)}"));
            // v10.5 설비 단계
            if (Tech.HasTree(f.Type))
            {
                var tt = Tech.Of(mm);
                list.Add(("기술 단계", $"{Tech.Roman(tt.Tier)} {tt.Name} · 출력 ×{tt.Output:0.##} · 전기 ×{tt.Power:0.##} · 고장 ×{tt.Faults:0.##}"));
                if (Tech.Next(mm) is TechTier nx)
                    list.Add(("다음 단계", nx.Research <= _world.Research
                        ? $"{nx.Name} — 풀림 · {string.Join(" + ", nx.Cost.Select(c => $"{ItemKinds.Name(c.kind)} {c.count}"))}"
                        : $"{nx.Name} — 연구 {_world.Research:0}/{nx.Research}점"));
            }
            if (Modules.Of(f.Type) is Modules.Spec ms)
                list.Add(("보조 장비", $"{ms.Note} · {(mm.Faults.Count == 0 && (mm.Powered || mm.Spec.PowerDraw <= 0f) ? "작동 중" : "멈춤 (보너스 없음)")}"));
        }
        switch (f.Type)
        {
            case FurnitureType.Collector:
                list.Add(("주변 잔해", $"{_world.Space.DensityName} ({_world.Space.Density:0.00})"));
                list.Add(("채집 누적", $"{_world.Collection.Collected}개"));
                break;
            case FurnitureType.Refinery:
                list.Add(("정제", "원료 → 금속판·구조재·케이블·전자재·퓨즈·실링폼"));
                list.Add(("얼음", $"공기 탱크 {Pct(_world.Air.Reserve / _world.Air.ReserveCapacity)}"));
                break;
            case FurnitureType.Workbench:
                list.Add(("제작", "금속판·케이블·전자재 → 모터·펌프·베어링·제어기·센서"));
                break;
        }
        switch (f.Type)
        {
            case FurnitureType.ReactorCore:
                list.Add(("상태", !p.ReactorOnline ? "긴급 정지 (SCRAM)" : p.LowPowerMode ? "저출력 수동 운전 (자연 순환 냉각)"
                    : p.ReactorRamp < 1f ? $"재기동 중 {Pct(p.ReactorRamp)}" : "가동"));
                list.Add(("출력", $"{p.ReactorOutput:0.0} / {p.ReactorLimit:0.0} kW"));
                list.Add(("노심 온도", $"{p.ReactorTemperature:0}℃"));
                list.Add(("냉각 능력", $"{p.CoolingCapacity:0.0} kW" + (!p.ReactorOnline ? $" (재기동에 {PowerGrid.RestartCoolingKw:0} 필요)" : "")));
                break;
            case FurnitureType.AuxGenerator:
                list.Add(("상태", p.AuxRunning ? $"가동 · {p.AuxOutput:0.0}/{p.AuxRated:0} kW" : "대기 (손으로 시동)"));
                list.Add(("연료", $"{p.AuxFuel:0.0} / {PowerGrid.AuxFuelHours:0} 시간"));
                list.Add(("공급", "A 회로(필수)만"));
                break;
            case FurnitureType.CoolantPump:
                list.Add(("감당 출력", $"{f.Machine!.Efficiency * f.Machine.Rating * PowerGrid.CoolingPerPumpKw:0.0} kW"));
                break;
            case FurnitureType.PowerPanel:
                if (p.Brownout)
                    list.Add(("저출력", $"{(_world.Tick - p.BrownoutSince) / (float)SimTime.TicksPerHour:0}시간째 · {p.ParkedCount}대 내림 · {p.FullDemand:0}/{p.ReactorLimit:0}kW"));
                for (int i = 0; i < PowerGrid.CircuitCount; i++)
                {
                    var j = p.FeedingJumper(i);
                    string state = p.ManualOff[i] ? "절전으로 내림"
                        : p.CircuitLive[i] ? "정상"
                        : j != null ? $"{(j.Permanent ? "예비" : "임시")} 배선({PowerGrid.CircuitName(j.From)}→) {j.Load:0}/{j.Capacity:0} kW"
                        : "차단";
                    list.Add(($"{PowerGrid.CircuitName(i)} 회로 ({PowerGrid.CircuitRole(i)})", state));
                }
                foreach (var j in p.Jumpers.Where(j => !j.Active))
                    list.Add(j.Permanent
                        ? ("예비 배선", $"{PowerGrid.CircuitName(j.From)}→{PowerGrid.CircuitName(j.To)}" + (j.Burnt ? " · 과부하로 탐" : " · 대기 (끊기면 저절로)"))
                        : ("배선 흔적", $"{PowerGrid.CircuitName(j.From)}→{PowerGrid.CircuitName(j.To)}" + (j.Burnt ? " · 과부하로 탐" : " · 지금은 안 씀")));
                break;
            case FurnitureType.Battery:
                list.Add(("잔량", $"{p.BatteryCharge:0} / {p.BatteryCapacity:0} kWh"));
                list.Add(("흐름", p.BatteryFlow >= 0 ? $"충전 {p.BatteryFlow:0.0} kW" : $"방전 {-p.BatteryFlow:0.0} kW"));
                break;
            case FurnitureType.OxygenGenerator:
                list.Add(("우주선 산소 생산", $"{_world.Air.O2Produced:0} / {_world.Air.O2Capacity:0}"));
                break;
            case FurnitureType.WaterRecycler:
                list.Add(("정수 탱크", $"{_world.Water.Level:0} / {_world.Water.Capacity:0} L"));
                list.Add(("생산 / 사용", $"{_world.Water.Produced:0.0} / {_world.Water.Consumed:0.0} L/h"));
                break;
            case FurnitureType.GrowBed when f.Machine?.Crop is CropState crop:
                list.Add(("생장", crop.Ripe ? "수확 가능" : Pct(crop.Growth)));
                list.Add(("돌봄", Pct(crop.Care)));
                if (crop.Blight > 0f) list.Add(("병충해", $"{Pct(crop.Blight)} · " + (crop.BlightKnown ? "약을 쳐야 한다" : "아무도 모른다")));
                break;
            case FurnitureType.Stove:
                list.Add(("조리 중", f.Machine!.Active ? "예" : "아니오"));
                break;
        }
        return list;
    }

    // ─────────────────────────────── 방 상세 ───────────────────────────────

    private void DrawRoomInspector(Room room, float y, Vector2 mouse)
    {
        var accent = Palette.Room(room.Kind);
        float x0 = Screen.X - Margin - RightColumnWidth;
        var inside = _world.Crew.Where(c => c.Room == room).ToList();
        var equipment = room.Furniture.Where(f => f.Machine != null || f.Storage != null).ToList();
        var status = RoomStatusLines(room);
        var history = RoomHistoryLines(room, RightColumnWidth - 36);
        float height = 236f + status.Count * 20f + Math.Max(1, inside.Count) * 22f + Math.Max(1, Math.Min(8, equipment.Count)) * 20f
                       + (history.Count > 0 ? 40 + history.Count * 18 : 0);
        var card = new Rect2(x0, y, RightColumnWidth, height);
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;

        Gfx.RoundRect(this, new Rect2(x - 2, y + 15, 22, 22), accent.WithAlpha(0.18f), 5, accent.WithAlpha(0.7f));
        Icons.Draw(this, Icons.Room(room.Kind), new Vector2(x + 9, y + 26), 16, accent.Lightened(0.25f)); // v16.2 방 종류마다 고유 아이콘
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 26, y + 32), room.Name, Ui.TextHeading, Palette.Text);
        string power = room.Powered ? $"{PowerGrid.CircuitName(room.Circuit)}회로 · 전력 정상" : $"{PowerGrid.CircuitName(room.Circuit)}회로 · 정전";
        Gfx.TextRight(this, Fonts.Body, new Vector2(right, y + 20), power, Ui.TextBody, room.Powered ? Palette.TextMuted : Palette.Danger);
        CodexButton(right, y + 25, mouse); // v12.2 설명서
        Divider(x, right, y + 48);

        var air = room.Air;
        float ay = y + 54;
        Row(x, right, ay, "산소", Mathf.Clamp(air.O2 / 21f, 0f, 1f), Palette.VitalOxygen, $"{air.O2:0.0} kPa", air.O2 < 17f);
        Row(x, right, ay + 22, "CO2", Mathf.Clamp(air.CO2 / 3f, 0f, 1f), Palette.NeedStress, $"{air.CO2:0.00}", air.CO2 > 1f);
        Row(x, right, ay + 44, "기압", Mathf.Clamp(air.Pressure / 101f, 0f, 1f), Palette.Accent, $"{air.Pressure:0} kPa", air.Pressure < 80f);
        Row(x, right, ay + 66, "온도", Mathf.Clamp((air.Temperature + 10f) / 60f, 0f, 1f), new Color("#ff9a6b"), $"{air.Temperature:0.0}℃",
            air.Temperature < 12f || air.Temperature > 32f);
        Row(x, right, ay + 88, "연기", air.Smoke, new Color("#9aa3b5"), Pct(air.Smoke), air.Smoke > 0.2f);

        float ly = ay + 116;
        foreach (var (label, value, color) in status)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 12), label, Ui.TextBody, Palette.TextDim);
            // 값이 길어 이름과 겹치면 다음 줄로 내린다
            float lw = Fonts.Body.GetStringSize(label, HorizontalAlignment.Left, -1, 12).X;
            float vw = Fonts.Bold.GetStringSize(value, HorizontalAlignment.Left, -1, 12).X;
            if (vw > right - x - lw - 14f)
            {
                // 너무 길면 다음 줄부터 패널 너비에 맞춰 접는다
                foreach (var part in WrapText(value, right - x, Ui.TextBody))
                {
                    ly += 18;
                    Gfx.TextRight(this, Fonts.Bold, new Vector2(right, ly + 12), part, Ui.TextBody, color);
                }
            }
            else Gfx.TextRight(this, Fonts.Bold, new Vector2(right, ly + 12), value, Ui.TextBody, color);
            ly += 20;
        }
        ly += 16;
        SectionTitle(x, ly, "있는 사람");
        ly += 8;
        if (inside.Count == 0)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 16), "아무도 없음", Ui.TextBody, Palette.TextMuted);
            ly += 22;
        }
        foreach (var c in inside)
        {
            Icons.Draw(this, Icons.CrewState(c, _world.Tick), new Vector2(x + 6, ly + 11), 13, Palette.Crew(c.Id)); // 지금 상태 아이콘 (색 = 사람)
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 18, ly + 16), c.Name, Ui.TextLabel, Palette.Text);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, ly + 16), c.ActivityLabel, Ui.TextBody, Palette.TextDim);
            ly += 22;
        }

        ly += 18;
        SectionTitle(x, ly, "설비");
        ly += 8;
        if (equipment.Count == 0) Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 16), "없음", Ui.TextBody, Palette.TextMuted);
        foreach (var f in equipment.Take(8))
        {
            var m = f.Machine;
            var dot = m == null ? Palette.TextMuted : m.Faults.Count > 0 ? Palette.Danger : m.Wear > 0.6f ? Palette.Warning : Palette.Good;
            Icons.Draw(this, Icons.Furniture(f.Type), new Vector2(x + 6, ly + 10), 13, dot); // v16.2 설비 이름 옆엔 그 설비의 아이콘 (색 = 상태)
            Gfx.Text(this, Fonts.Body, new Vector2(x + 18, ly + 15), f.Label, Ui.TextBody, Palette.TextDim);
            string info = m != null ? m.StatusText : f.Storage != null ? $"{f.Storage.Total}/{f.Storage.Capacity}" : "";
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, ly + 15), info, Ui.TextSmall, Palette.TextMuted);
            ly += 20;
        }
        DrawHistoryLines(history, x, right, ly + 4);
    }

    /// <summary>방의 사고 관련 상태: 외벽, 환기 댐퍼, 격벽, 불.</summary>
    private List<(string label, string value, Color color)> RoomStatusLines(Room room)
    {
        var lines = new List<(string, string, Color)>();
        var ship = _world.Ship;
        // v11.2 유독 가스
        if (room.Air.Toxin > 0.01f)
        {
            var src = _world.Hazards.GasSource(room);
            lines.Add(("유독 가스", $"{room.Air.Toxin * 100:0}%" + (src != null ? $" · {src.Body.Label}에서 새는 중" : " · 걷히는 중") + (room.Air.Toxin > 0.2f ? " · 우주복 없이는 위험" : ""),
                room.Air.Toxin > 0.2f ? Palette.Danger : Palette.Warning));
        }
        // v12.3 물·습기·분전함·밸브
        if (room.Flood > 1f || room.Humidity > 0.7f || room.BreakerOff || room.ValveShut)
        {
            var parts = new List<string>();
            if (room.Flood > 1f) parts.Add($"바닥 물 {MoistureSystem.DepthCm(room):0.#}cm ({room.Flood:0}L)" + (room.Powered && MoistureSystem.Depth(room) > 0.12f ? " · 전기가 살아 있다!" : ""));
            if (room.Humidity > 0.7f) parts.Add($"습도 {room.Humidity * 100:0}%" + (room.Humidity > 0.8f ? " · 결로·부식" : ""));
            if (room.BreakerOff) parts.Add("분전함 내림 (정전)");
            if (room.ValveShut) parts.Add("급수 밸브 잠금 (단수)");
            bool danger = room.Powered && MoistureSystem.Depth(room) > 0.12f;
            lines.Add(("물·습기", string.Join(" · ", parts), danger ? Palette.Danger : Palette.Warning));
        }
        if (_world.Portable.RoomLine(room) is string pl) lines.Add(("이동식 장비", pl, pl.Contains("고장") || pl.Contains("젖었다") ? Palette.Warning : Palette.Accent)); // v16.7
        // v12.1 배 전체 망: 이 방으로 들어오는 전력 간선·급수관·환기 덕트
        if (!room.Detached && _world.Net.Links.Count > 0)
        {
            var parts = new List<string>();
            bool bad = false, warn = false;
            foreach (var k in new[] { NetKind.Power, NetKind.Water, NetKind.Air, NetKind.Data })
            {
                if (k == NetKind.Water && !UtilityNet.NeedsWater(room)) continue;
                var mine = _world.Net.Links.Where(l => l.Kind == k && (l.Room == room || l.Door != null && (l.Door.RoomA == room || l.Door.RoomB == room))).ToList();
                var worst = mine.OrderBy(l => l.Integrity).FirstOrDefault();
                string name = k switch { NetKind.Power => "전력", NetKind.Water => "급수", NetKind.Data => "데이터", _ => "덕트" };
                // v14.8 얼마나 오나: 전압 · 수압 · 환기가 모자라면 몇 %인지와 까닭
                if (k != NetKind.Data && UtilityNet.Fed(k, room) && _world.Flow.Share(k, room) is { Frac: < 0.9f } fs)
                {
                    parts.Add($"{(k == NetKind.Power ? "전압" : k == NetKind.Water ? "수압" : "환기")} {fs.Frac * 100:0}%" + (fs.Why != null ? $" ({fs.Why})" : ""));
                    warn = true;
                    continue;
                }
                if (!UtilityNet.Fed(k, room)) { parts.Add($"{name} 끊김"); bad = true; }
                else if (worst != null && worst.Integrity < 0.95f) { parts.Add($"{name} {worst.Integrity * 100:0}%" + (worst.Temp ? " 임시" : "")); warn = true; }
                else if (worst != null && worst.Temp) { parts.Add($"{name} 임시로 이음"); warn = true; }
                else parts.Add($"{name} 이어짐");
            }
            lines.Add(("배선·배관", string.Join(" · ", parts), bad ? Palette.Danger : warn ? Palette.Warning : Palette.TextDim));
        }
        var walls = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == room).Select(kv => kv.Value).ToList();
        if (walls.Count > 0)
        {
            var damaged = walls.Where(w => w.StageIndex > 0 || w.Patched).GroupBy(w => w.Stage).Select(g => $"{g.Key} {g.Count()}").ToList();
            int welds = walls.Sum(w => w.Welds);
            string hull = damaged.Count > 0 ? string.Join(" · ", damaged) : "이상 없음";
            float weakest = walls.Min(w => w.MaxIntegrity);
            if (welds > 0) hull += $" · 용접 {welds}회" + (weakest < 0.995f ? $" (가장 약한 곳 최대 강도 {weakest * 100:0}%)" : "");
            int plates = walls.Count(w => w.Reinforced);
            if (plates > 0) hull += $" · 보강 {plates}칸";
            int swaps = walls.Sum(w => w.Replacements);
            if (swaps > 0) hull += $" · 패널 교체 {swaps}회";
            var col = room.Leaking ? Palette.Danger : damaged.Count > 0 ? Palette.Warning : Palette.TextDim;
            lines.Add(("외벽", hull, col));
        }
        // v8 구조: 연결부 (아는 값 · 검사 전이면 미확인), 하중, 버틸 시간, 사출 준비, 임시 도킹
        if (room.DesignJoints > 0 && !room.Detached)
        {
            int frames = StructureSystem.FrameLost(_world, room);
            float stress = StructureSystem.StressOf(StructureSystem.KnownCapacity(room), room.DesignJoints, frames);
            float ttf = StructureSystem.HoursToFailure(room, true, frames);
            string js = string.Join(" ", room.Joints.Select(j => (j.Truss ? "T" : "") + (j.Released ? "풀림" : j.KnownBroken ? "✕" : $"{j.Known * 100:0}")));
            bool unseen = _world.Structure.Unseen.ContainsKey(room.Id);
            string text = $"{js} · 하중 {stress * 100:0}%" + (ttf < 72f ? $" · {ttf:0}시간" : "") + (unseen ? " · 미확인" : "");
            if (frames > 0) text += $" · 골조 {frames}";
            lines.Add(("연결부", text, stress > 1f || room.Joints.Any(j => j.KnownBroken && !j.Released) ? Palette.Danger : unseen || room.Joints.Any(j => j.Known < 0.7f) ? Palette.Warning : Palette.TextDim));
        }
        else if (room.Type == RoomType.Corridor) lines.Add(("연결부", "용골 (모든 방이 여기에 붙어 있다)", Palette.TextMuted));
        // v12.6 세부 종류 · 환경 · 맡은 일(겸용의 대가)
        if (room.Special != null) lines.Add(("종류", $"{RoomTypes.Name(room.Kind)} — {RoomTypes.Name(room.Type)} 노릇도 한다", Palette.Room(room.Kind)));
        {
            var env = new List<string>();
            if (room.Noise > 0.06f) env.Add($"소음 {room.Noise * 100:0}%");
            if (room.Vibration > 0.06f) env.Add($"진동 {room.Vibration * 100:0}%");
            if (room.Smell > 0.06f) env.Add($"냄새 {room.Smell * 100:0}%");
            if (room.Radiation > 0.08f) env.Add($"방사선 {room.Radiation * 100:0}%");
            if ((RoomCatalog.Tags(room.Kind) & RoomTag.Sleep) != 0) env.Add($"잠의 질 {AmbienceSystem.SleepFactor(room) * 100:0}%");
            if (env.Count > 0) lines.Add(("환경", string.Join(" · ", env), room.Radiation > 0.2f || room.Noise > 0.5f ? Palette.Warning : Palette.TextDim));
            foreach (var fn in RoomCatalog.Functions)
            {
                float k = Facilities.Factor(room, fn.Key);
                if (k <= 0f) continue;
                var (best, _) = Facilities.Best(_world.Ship, fn.Key);
                if (k >= 1f) lines.Add(("맡은 일", $"{fn.Name} (전용)", new Color("#8fd65a")));
                else if (best == room) lines.Add(("겸용", $"{fn.Name} {k * 100:0}% — {fn.Missing}", Palette.Warning));
            }
            if (room.Compartment >= 0) lines.Add(("구획", $"{room.Compartment + 1}구획 (격벽 문 {room.Doors.Count(d => d.Bulkhead)})", Palette.TextDim));
        }
        // v10.6 방 모듈: 달린 것 · 더 달 수 있는 것
        foreach (var spec in Modules.All.Where(s => s.Room == room.Type))
        {
            int have = room.Furniture.Count(f => f.Type == spec.Type);
            int working = Modules.Working(_world, spec.Type, room);
            string txt = have == 0 ? $"없음 · 달 수 있다 (최대 {spec.Max}) · {spec.Note}" : $"{working}/{have} 작동 · 최대 {spec.Max} · {spec.Note}";
            lines.Add((Modules.Name(spec.Type), txt, have > working ? Palette.Warning : have > 0 ? new Color("#8fd65a") : Palette.TextMuted));
        }
        // v9 배관: 이 방에 밸브가 있거나 이 방을 지나는 관
        foreach (var seg in _world.Piping.In(room))
        {
            bool bad = !seg.Sound || seg.Closed || seg.Leaking || seg.Bypass > 0f || (seg.Radiator.Count > 0 && seg.RadiatorCondition < 0.7f);
            string txt = seg.StateText + (seg.Leaking ? $" · {seg.LeakRate:0}L/시간" : "") + (seg.Bypass > 0f ? $" · 우회 {seg.Bypass * 100:0}%" : "")
                         + (seg.Radiator.Count > 0 ? $" · 방열판 {seg.RadiatorCondition * 100:0}%" : "") + (seg.Breaks > 0 ? $" · 터짐 {seg.Breaks}" : "");
            lines.Add((seg.Name, txt, seg.Leaking || seg.Severed ? Palette.Danger : bad ? Palette.Warning : Palette.TextDim));
        }
        if (_world.Automation.ComputerBody is Furniture comp && comp.Room == room)
        {
            var au = _world.Automation;
            bool vent = au.Ventilated(room);
            string st = au.MainOnline ? "가동 · 자동화 켜짐" : $"정지 ({comp.Machine?.Faults.FirstOrDefault()?.Name ?? "전기 없음"})" + (au.BackupActive ? " · 예비 제어기" : "");
            lines.Add((comp.Machine?.Grade == MachineGrade.Mk1 ? "임시 제어 컴퓨터" : "주 컴퓨터", $"{st} · {room.Air.Temperature:0}℃ · 환기 {(vent ? "됨" : "끊김")}",
                !au.MainOnline || room.Air.Temperature > AutomationSystem.OverheatC - 4f ? Palette.Danger : !vent ? Palette.Warning : Palette.TextDim));
            if (room.DamperStuck) lines.Add(("댐퍼", "구동기가 걸렸다 — 손으로 풀어야 한다", Palette.Warning));
        }
        if (room.Type == RoomType.Cooling && _world.Piping.Built)
            lines.Add(("냉각", $"{_world.Piping.CoolingKw:0}kW · 냉각수 {_world.Piping.CoolantFraction * 100:0}% · 노심 {_world.Power.ReactorTemperature:0}℃",
                _world.Power.ReactorTemperature >= PowerGrid.OverheatWarnC ? Palette.Danger : Palette.TextDim));
        if (room.Jettison is JettisonPlan jp)
            lines.Add(("사출", $"{JettisonPlan.StageName(jp.Stage)} 단계" + (jp.Bolts ? " · 폭발 볼트" : "") + (jp.Decider != null ? $" · 결정 {jp.Decider.Name}" : ""), Palette.Danger));
        if (room.Detached && room.Fragment is Fragment fr)
            lines.Add(("분리", $"{(fr.Jettisoned ? "사출" : "뜯겨 나감")} · {fr.Distance:0}칸 · {(fr.State == FragmentState.Towed ? "견인 중" : fr.State == FragmentState.Moored ? "계류" : fr.State == FragmentState.Lost ? "잃음" : "표류")}", Palette.Danger));
        if (room.Docked) lines.Add(("도킹", room.Wreck ? "잔해로 둔다 — 부품과 물자만 꺼내 쓴다" : room.Restoring ? $"되살리는 중 — 재연결 {(room.PowerCut ? "전력" : room.PipesCut && room.HasPipes ? "배관" : "환기")} 차례" : "임시 도킹 — 되살릴지 정하는 중", new Color("#e0b64a")));
        if (room.Detachments > 0 || room.Retrievals > 0)
            lines.Add(("이력", $"떨어져 나감 {room.Detachments}회 · 사출 {room.Jettisons}회 · 되찾음 {room.Retrievals}회", Palette.TextDim));
        if (room.Abandoned) lines.Add(("상태", $"폐쇄 구역 — {room.AbandonReason}", Palette.TextDim));
        if (room.Purpose is string purpose)
        {
            int cots = room.Furniture.Count(f => f.Type == FurnitureType.Cot);
            int benches = room.Furniture.Count(f => f.Type == FurnitureType.Workbench);
            int beds = room.Furniture.Count(f => f.Type == FurnitureType.GrowBed);
            string what = purpose.Contains("침실") ? $"간이침대 {cots}개" : purpose.Contains("재배") ? $"재배대 {beds}개" : $"임시 작업대 {benches}개";
            lines.Add(("용도", $"{purpose} · {what}", new Color("#e0b64a")));
        }
        // v10.2 칸막이: 같은 방에서 나뉜 칸
        if (room.Partitioned && room.Doors.Select(d => d.RoomA == room ? d.RoomB : d.RoomA).FirstOrDefault(o => o != null && o.Partitioned && o.Type == room.Type) is Room twin)
            lines.Add(("칸막이", $"{Ko.WaGwa(twin.Name)} 격벽 문 하나로 나뉘었다 ({room.Cells.Count}칸 · {twin.Cells.Count}칸)", new Color("#e0b64a")));
        if (_world.Power.ManualOff[room.Circuit]) lines.Add(("전력", $"{PowerGrid.CircuitName(room.Circuit)} 회로를 절전으로 내렸다", Palette.TextMuted));
        string damper = room.VentOpen ? "열림" : "닫힘";
        if (room.VentOpen != Hull.WantVentOpen(_world, room))
            damper += room.DamperStuck ? " · 구동기 걸림, 손으로"
                    : !room.Powered ? " · 전기 없음, 손으로 조작 필요"
                    : !_world.Automation.Dampers ? " · 자동화 꺼짐, 손으로"
                    : " · 곧 자동 조정";
        lines.Add(("환기 댐퍼", damper, room.VentOpen ? Palette.TextDim : Palette.Warning));
        if (room.Lockdown) lines.Add(("격벽", room.Leaking ? "폐쇄 · 공기 새는 중" : "폐쇄 · 재가압 대기", Palette.Danger));
        // v9.4 조명·문 구동기
        if (room.LightsOut)
            lines.Add(("조명", $"나감 · {(_world.Tick - room.LightsOutSince) / (float)SimTime.TicksPerHour:0}시간째 — 캄캄해서 일이 느리다 (케이블 1)", Palette.Warning));
        int brokenDoors = room.Doors.Count(d => d.MotorBroken && !d.Removed);
        if (brokenDoors > 0) lines.Add(("문", $"구동기 고장 {brokenDoors} — 손으로 천천히 열고 저절로 안 잠긴다", Palette.Warning));
        else if (room.Doors.Any(d => d.MotorMk1)) lines.Add(("문", "임시 구동기 — 느리다", Palette.TextMuted));
        int fires = _world.Fire.CountIn(room);
        if (fires > 0) lines.Add(("화재", _world.Fire.IsKnown(room) ? $"{fires}칸 불타는 중" : $"{fires}칸 (아직 아무도 모름)", Palette.Danger));
        return lines;
    }

    // ─────────────────────────────── 작업 목록 ───────────────────────────────

    private void DrawWorkBoard(float y, float maxHeight, Vector2 mouse)
    {
        float x0 = Screen.X - Margin - RightColumnWidth;
        var all = _world.Board.Open.ToList();
        var urgentOrders = all.Where(o => o.Urgency >= 0.9f).ToList();
        // 긴급한 일이 있으면 그것만 크게, 나머지는 한 줄로 접는다
        var orders = urgentOrders.Count > 0 ? urgentOrders : all;
        int folded = all.Count - orders.Count;
        const float rowH = 38f;
        int rows = Math.Max(1, Math.Min(orders.Count, (int)((maxHeight - 60 - (folded > 0 ? 26 : 0)) / rowH)));
        var card = new Rect2(x0, y, RightColumnWidth, 52 + rows * rowH + 8 + (folded > 0 ? 26 : 0));
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;
        if (urgentOrders.Count > 0)
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(_time * 4f);
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 25), $"긴급 작업 {urgentOrders.Count}", Ui.TextBody, Palette.Danger.WithAlpha(pulse));
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, y + 25), "사고 대응이 먼저", Ui.TextSmall, Palette.TextMuted);
        }
        else
        {
            SectionTitle(x, y + 25, "작업 목록");
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, y + 25), "승무원이 스스로 골라 맡는 일", Ui.TextSmall, Palette.TextMuted);
        }
        if (folded > 0)
            Gfx.Text(this, Fonts.Body, new Vector2(x, card.End.Y - 14), $"그 밖의 작업 {folded}건은 사고가 가라앉은 뒤에", Ui.TextSmall, Palette.TextMuted);

        if (orders.Count == 0)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 58), "할 일이 없다. 우주선이 평온하다.", Ui.TextBody, Palette.TextMuted);
            return;
        }

        float ry = y + 38;
        foreach (var o in orders.Take(rows))
        {
            var row = new Rect2(x0 + 8, ry, RightColumnWidth - 16, rowH - 4);
            if (row.HasPoint(mouse)) Gfx.RoundRect(this, row, new Color(1, 1, 1, 0.04f), 8);
            var uc = o.Urgency >= 0.9f ? Palette.Danger : o.Urgency >= 0.55f ? Palette.Warning : Palette.Accent;
            Gfx.RoundRect(this, new Rect2(row.Position.X + 6, row.Position.Y + 6, 3, row.Size.Y - 12), uc, 1.5f);
            string oi = o.Target.Furniture is Furniture of ? Icons.Furniture(of.Type) : o.Target.CurrentRoom is Room orr ? Icons.Room(orr.Kind) : "work";
            Icons.Draw(this, oi, new Vector2(x + 9, ry + 11), 14, uc); // v16.2 대상마다 고유 아이콘
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 22, ry + 15), o.Title, Ui.TextBody, Palette.Text);
            string detail = o.BlockedUntil > _world.Tick && o.BlockedReason != null ? $"보류 — {o.BlockedReason}" : o.Detail;
            Gfx.Text(this, Fonts.Body, new Vector2(x + 22, ry + 30), detail, Ui.TextSmall, o.BlockedUntil > _world.Tick ? Palette.Warning : Palette.TextMuted);
            if (o.Assignee is CrewMember a)
                Gfx.Pill(this, Fonts.Bold, new Vector2(right - Gfx.Width(Fonts.Bold, a.Name, Ui.TextSmall) * 0.5f - 7, ry + 17), a.Name, Ui.TextSmall,
                    Palette.Crew(a.Id), new Color(0, 0, 0, 0.3f), Palette.Crew(a.Id).WithAlpha(0.4f));
            else
                Gfx.TextRight(this, Fonts.Body, new Vector2(right, ry + 21), $"긴급도 {o.Urgency:0.00}", Ui.TextSmall, Palette.TextMuted);
            var target = o.Target;
            _buttons.Add((row, () => _main.Focus(target)));
            ry += rowH;
        }
    }

    private void DrawBanners()
    {
        float cx = (Screen.X - RightColumnWidth - Margin) * 0.5f;
        float y = Margin + 52f + 8f + 40f + 26f;
        if (_main.Replaying is ReplayRunner rr)
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(_time * 4f);
            UiKit.Banner(this, new Vector2(cx, y), $"불러오는 중 — 같은 항해 번호에서 역사를 다시 돌린다 {Pct(rr.Progress)} · {_world.Day}일차", Tone.Info, pulse, "clock");
            y += 36f;
        }
        if (_main.Notice is string notice && (Time.GetTicksMsec() - _main.NoticeMsec < 6000 || Engine.GetProcessFrames() - _main.NoticeFrame < 120))
        {
            UiKit.Banner(this, new Vector2(cx, y), notice, Tone.Good, 1f, "info");
            y += 36f;
        }
        if (_main.Paused && !Quiet) // v16.2 조용한 HUD에서 일시정지는 글 대신 화면 테두리 · 채도로만
        {
            float pulse = 0.6f + 0.4f * Mathf.Sin(_time * 3f);
            UiKit.Banner(this, new Vector2(cx, y), "일시정지  ·  Space로 재개", Tone.Caution, pulse, "pause");
            y += 36f;
        }
        if (_main.Tool != IncidentTool.None)
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(_time * 4f);
            UiKit.Banner(this, new Vector2(cx, y), $"{_main.ToolHint}  ·  Shift 연속 · Esc 취소", Tone.Danger, pulse, "incident");
            y += 36f;
        }
        var alert = _world.Alerts.LastOrDefault();
        if (alert != null && alert.Level >= AlertLevel.Warning && _world.Tick - alert.Tick < SimTime.Hours(1))
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(_time * 5f);
            UiKit.Banner(this, new Vector2(cx, y), $"경보 · {alert.Text}", alert.Level == AlertLevel.Critical ? Tone.Danger : Tone.Caution, pulse, "alert");
        }
    }
}

internal static class HudText
{
    public static string DefaultIfEmptyText(this string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
}
