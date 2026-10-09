using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v19 우주급 고유 장면 (1단계 8종) — 일반 사고의 강화판이 아니라 그 재난에만 있는 상황.
//   초신성: 물벽과 대피소의 긴 밤 (파도가 세고 길다)            감마선: 배를 돌려 무거운 설비를 방패로 — 그늘 쪽 방으로
//   마그네타: 암흑 시간 — 주 컴퓨터 · 로봇 · 드론 · 조명이 멎는다 → 손으로 · 깨어난 컴퓨터에게 빈 시간을 적어 준다
//   블랙홀 조석: 배가 늘어난다 — 끝 구획 연결부가 당겨지고 · 바깥 시계와 어긋난다 · 끊어지기 전에 떼어 낼까
//   큰 소행성: 추진제냐 구획이냐 — 안 비키면 그 구획을 잃는다      해적 함대: 도망 · 교섭 · 버팀 (승선 시도)
//   다른 배 원자로 폭발: 낙진을 뒤집어쓴 생존자를 받을까 (제염)     행성 고리: 연속 회피 기동 — 조종사가 지쳐 간다
// 공통: 비키기는 '줄이기' (가장자리는 닿는다) · 연소한 만큼 항로가 늦어진다.

public sealed partial class CosmicEvent
{
    /// <summary>장면의 선택: 해적 1 도망 2 교섭 3 버팀 · 원자로 1 받는다 −1 거절 · 감마선 1 돌렸다 −1 그대로 · 마그네타 1 컴퓨터를 지켰다 2 기억이 비었다.</summary>
    public int SceneChoice { get; set; }
    /// <summary>다음 장면 시각 (고리 기동 · 승선 시도 · 탈출정 도착).</summary>
    public long SceneNext { get; set; } = -1;
    public int SceneCount { get; set; }
    /// <summary>조석: 배가 늘어난 정도 (0~1) · 바깥 시계보다 늦은 분.</summary>
    public float Stretch { get; set; }
    public float DriftMin { get; set; }
    /// <summary>감마선: 돌려 놓을 방향 (돌리는 데 40분 — 줄기가 먼저 오면 소용없다).</summary>
    public float TurnTo { get; set; } = float.NaN;
    public string TurnNames { get; set; } = "";
    /// <summary>마그네타: 암흑 시간이 끝나는 때.</summary>
    public long DarkUntil { get; set; } = -1;
    /// <summary>끈 로봇 · 멈춘 드론 · 원래 꺼져 있던 방 · 태운 생존자.</summary>
    public List<int> SceneRobots { get; } = new();
    public List<int> SceneDrones { get; } = new();
    public List<int> SceneDark { get; } = new();
    public List<int> SceneSurvivors { get; } = new();
    /// <summary>화면 띠에 보일 지금 상황 한 줄.</summary>
    public string SceneLine { get; set; } = "";
}

public sealed partial class CosmicSystem
{
    /// <summary>비켜도 닿는 몫: 부딪히는 것(파편 · 충격 · 조석) · 나머지(빛 · 방사선 · 펄스).</summary>
    public const float GrazeContact = 0.4f, GrazeOther = 0.5f;

    /// <summary>비켜도 닿는 몫 — 조석과 고리면은 비켜 봐야 덜 받을 뿐이다.</summary>
    private static float GrazeOf(CosmicEvent e, bool contact) => e.Kind switch
    {
        CosmicKind.BlackHoleTide => 0.65f,
        CosmicKind.PlanetRing => 0.6f,
        _ => contact ? GrazeContact : GrazeOther,
    };

    private readonly Dictionary<int, float> _contam = new();
    /// <summary>조석: 연결부가 버티지 못해 비우는 방.</summary>
    private readonly HashSet<int> _strain = new();
    public bool Straining(Room r) => _strain.Contains(r.Id);
    /// <summary>낙진을 뒤집어쓴 사람 (제염 전까지 곁 사람에게 방사선).</summary>
    public float Contamination(CrewMember c) => _contam.GetValueOrDefault(c.Id);
    public IReadOnlyDictionary<int, float> Contaminated => _contam;

    private readonly Dictionary<(int room, int ev), float> _shade = new();
    private int _shadeVersion = -1;
    private float _shadeSide = float.NaN;

    // ───────────────────────────── 공통 ─────────────────────────────

    /// <summary>항로를 바꾼 만큼 늦어진다 (식량 · 물이 그만큼 더 든다).</summary>
    private void Delay(CosmicEvent e, float cost)
    {
        var w = _w;
        float days = e.Spec.AvoidFuel * 0.25f;
        if (days <= 0f) return;
        w.Voyage.Shift(-days);
        w.Log.Add(w.Tick, LogKind.Ship, $"{e.Spec.Name} — 항로를 바꾼 만큼 {days * 24f:0}시간 늦어진다 (추진제 {cost:0}kg)");
    }

    // ───────────────────────────── 장면 훅 ─────────────────────────────

    /// <summary>예보 · 대비 동안 (틱마다): 장면마다 미리 정할 것.</summary>
    private void SceneBefore(CosmicEvent e)
    {
        if (e.Ghost || !e.Known || e.Phase > CosmicPhase.Brace) return;
        switch (e.Kind)
        {
            case CosmicKind.GammaBurst: TurnDecision(e); break;
            case CosmicKind.PirateFleet: PirateDecision(e); break;
        }
    }

    /// <summary>단계가 시작되는 순간.</summary>
    private void SceneStage(CosmicEvent e, int i)
    {
        switch (e.Kind)
        {
            case CosmicKind.MagnetarStorm when i == 0: BlackOut(e); break;
            case CosmicKind.ReactorBlast when i == 1: Distress(e); break;
            case CosmicKind.PlanetRing when i == 0: e.SceneNext = _w.Tick + SimTime.Minutes(R.Range(3f, 6f)); break;
            case CosmicKind.PirateFleet when i == 0 && e.SceneChoice is 0 or 3 && !e.Avoided: e.SceneNext = _w.Tick + SimTime.Minutes(R.Range(50f, 80f)); break;
        }
    }

    /// <summary>시스템 틱마다 (모든 단계).</summary>
    private void SceneTick(CosmicEvent e, float dt, bool hourly)
    {
        switch (e.Kind)
        {
            case CosmicKind.MagnetarStorm: DarkTick(e); break;
            case CosmicKind.GammaBurst: GammaTick(e); break;
            case CosmicKind.BlackHoleTide: TideTick(e, dt, hourly); break;
            case CosmicKind.PlanetRing: RingTick(e); break;
            case CosmicKind.PirateFleet: BoardTick(e); break;
            case CosmicKind.ReactorBlast: PodTick(e); break;
        }
    }

    /// <summary>재난이 지나간 순간 (후유증으로 넘어갈 때).</summary>
    private void SceneAfter(CosmicEvent e)
    {
        var w = _w;
        if (e.Kind == CosmicKind.BlackHoleTide) _strain.Clear();
        switch (e.Kind)
        {
            case CosmicKind.MagnetarStorm: if (e.DarkUntil > w.Tick) LightsBack(e); break;
            case CosmicKind.BlackHoleTide when e.DriftMin >= 1f:
                w.History.Add(w, HistoryKind.Lesson, $"블랙홀 곁을 지나왔다 — 배 시계가 바깥보다 {e.DriftMin:0}분 늦다. 그 시간은 우리에게만 흘렀다", log: true);
                foreach (var c in w.Crew.Where(c => !c.Dead)) MarkLog.Add(c.Memory.Marks, w.Tick, $"블랙홀 곁 — 바깥보다 {e.DriftMin:0}분 늦게 산 날");
                break;
        }
        e.SceneLine = "";
    }

    /// <summary>장면마다 더하는 대비 일 (대비 계획을 짤 때).</summary>
    private void ScenePlan(CosmicEvent e)
    {
        var w = _w;
        if (e.Kind == CosmicKind.PirateFleet && e.SceneChoice is 0 or 3)
            foreach (var air in w.Ship.RoomsOf(RoomType.Airlock).Where(r => !r.Detached).OrderBy(r => r.Id).Take(2))
                AddTask(e, BraceKind.Barricade, air.Id, -1, 0.5f, $"{air.Name} 안쪽 문 막기 (쇠막대 · 용접)");
    }

    // ───────────────────────────── 감마선: 배를 방패로 ─────────────────────────────

    /// <summary>무거운 설비 쪽 (엔진 · 원자로 · 창고 · 화물): 배 한가운데에서 그쪽으로.</summary>
    private (Vector2 dir, string names)? HeavySide()
    {
        var ship = _w.Ship;
        var heavy = ship.Rooms.Where(r => !r.Detached && r.Type is RoomType.Engine or RoomType.Reactor or RoomType.Storage or RoomType.Cargo).OrderBy(r => r.Id).ToList();
        if (heavy.Count == 0) return null;
        var center = ShipCenter();
        var s = Vector2.Zero;
        foreach (var r in heavy) s += r.Center - center;
        if (s.LengthSquared() < 1f) return null;
        return (Vector2.Normalize(s), string.Join("·", heavy.Select(r => r.Name).Distinct().Take(3)));
    }

    private void TurnDecision(CosmicEvent e)
    {
        var w = _w;
        if (e.SceneChoice != 0) return;
        if (HeavySide() is not { } hs) { e.SceneChoice = -1; return; }
        float cost = 3f * MathF.Max(0.3f, w.Propulsion.Scale);
        string key = "cosmic:turn:" + e.Id;
        if (w.Propulsion.Propellant < cost || w.Propulsion.Control().quality <= 0f)
        {
            // 자세 제어 분사에 쓸 추진제가 없거나 조종할 수 없다 — 그대로 받는다 (그늘 쪽 방으로 숨을 수밖에)
            e.SceneChoice = -1;
            e.SceneLine = w.Propulsion.Propellant < cost ? "추진제가 모자라 배를 돌리지 못한다" : "배를 돌릴 조종이 없다";
            w.History.Add(w, HistoryKind.Incident, $"{e.Spec.Name} — {e.SceneLine}. 줄기를 그대로 받는다", log: true);
            return;
        }
        if (ComputerUp)
        {
            var p = w.Automation.Asks.Latest(key);
            if (p == null)
            {
                w.Automation.Asks.Propose(key, "cosmic-turn", null, $"배를 돌려 {hs.names} 쪽으로 줄기를 받는다 (자세 제어 · 추진제 {cost:0}kg)",
                    "감마선은 한쪽에서만 꿰뚫고 온다 — 무거운 설비 뒤가 그늘", "그늘 쪽 방은 거의 쬐지 않는다 · 사람은 그늘 쪽으로", 30f, null,
                    onAccept: (world, _) => BeginTurn(e, hs.dir, hs.names, cost));
                e.SceneLine = "배를 돌릴지 묻는다";
            }
            else if (p.State != ProposalState.Pending && !p.Accepted) { e.SceneChoice = -1; e.SceneLine = "배를 돌리지 않는다 — 그대로 받는다"; }
            return;
        }
        // 컴퓨터 없이: 조종할 수 있는 사람이 있으면 손으로 돌린다
        var boss = Boss();
        bool yes = boss != null && w.Propulsion.Control().quality > 0f && w.Propulsion.Propellant >= cost;
        if (yes) BeginTurn(e, hs.dir, hs.names, cost);
        else { e.SceneChoice = -1; e.SceneLine = "배를 돌릴 사람이 없다"; }
    }

    /// <summary>자세 제어 시작: 무거운 배를 돌리는 데 40분 걸린다.</summary>
    private void BeginTurn(CosmicEvent e, Vector2 heavy, string names, float cost)
    {
        var w = _w;
        if (e.SceneChoice is 1 or 4) return;
        if (w.Propulsion.Propellant < cost) { e.SceneChoice = -1; e.SceneLine = "추진제가 모자라 배를 돌리지 못한다"; return; }
        e.SceneChoice = 4;
        e.TurnTo = MathF.Atan2(heavy.Y, heavy.X);
        e.TurnNames = names;
        e.SceneNext = w.Tick + SimTime.Minutes(40);
        w.Propulsion.Propellant = MathF.Max(0f, w.Propulsion.Propellant - cost);
        e.SceneLine = $"배를 돌리는 중 — {names} 쪽을 줄기로 (40분)";
        w.Log.Add(w.Tick, LogKind.Ship, $"감마선 대비 — 자세 제어 분사 시작: {names} 쪽을 줄기로 (추진제 {cost:0}kg · 40분)");
    }

    private void GammaTick(CosmicEvent e)
    {
        var w = _w;
        if (e.SceneChoice != 4) return;
        bool beam = e.Phase == CosmicPhase.Impact && StageActive(e, 0);
        if (w.Tick < e.SceneNext && !beam) { e.SceneLine = $"배를 돌리는 중 — {(e.SceneNext - w.Tick) / (float)SimTime.Minutes(1):0}분 남았다"; return; }
        if (w.Tick < e.SceneNext && beam)
        {
            e.SceneChoice = -1;
            e.SceneLine = "다 돌리기 전에 줄기가 왔다";
            w.History.Add(w, HistoryKind.Incident, $"배를 다 돌리기 전에 감마선 줄기가 왔다 — {e.TurnNames}는 방패가 되지 못했다", log: true);
            return;
        }
        e.SceneChoice = 1;
        e.Side = e.TurnTo; // 줄기가 무거운 쪽에서 들어온다 — 그 뒤가 그늘
        e.SceneLine = $"배를 돌렸다 — {e.TurnNames}가 방패";
        w.History.Add(w, HistoryKind.Decision, $"감마선 줄기를 {e.TurnNames} 쪽으로 받도록 배를 돌렸다 — 반대편이 그늘", log: true);
        Broadcast(e, $"배를 돌렸다 — 줄기는 {e.TurnNames} 쪽으로 온다. 그 반대편 방으로", 2);
    }

    /// <summary>그늘: 줄기가 오는 쪽으로 배 안에 무엇이 가로막고 있나 (벽 · 설비 · 무거운 방). 1 = 다 쬔다.</summary>
    public float Shade(Room r, CosmicEvent e)
    {
        var w = _w;
        if (w.Structure.Version != _shadeVersion || e.Side != _shadeSide) { _shade.Clear(); _shadeVersion = w.Structure.Version; _shadeSide = e.Side; }
        if (_shade.TryGetValue((r.Id, e.Id), out var v)) return v;
        var grid = w.Ship.Grid;
        var dir = Dir(e);
        float mass = 0f;
        var p = r.Center;
        for (int k = 0; k < 160; k++)
        {
            p += dir;
            var cell = new Cell((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
            if (!grid.InBounds(cell)) break;
            var kind = grid.Kind(cell);
            if (kind == TileKind.Void) continue;
            var other = w.Ship.RoomAt(cell);
            if (other == r) continue;
            if (kind == TileKind.Wall) mass += 1f;
            else if (other != null) mass += other.Type is RoomType.Engine or RoomType.Reactor or RoomType.Storage or RoomType.Cargo ? 1.6f : 0.35f;
            if (w.Ship.FurnitureAt(cell) != null) mass += 0.8f;
        }
        v = Math.Clamp(MathF.Exp(-0.07f * mass), 0.04f, 1f);
        _shade[(r.Id, e.Id)] = v;
        return v;
    }

    // ───────────────────────────── 마그네타: 암흑 시간 ─────────────────────────────

    private void BlackOut(CosmicEvent e)
    {
        var w = _w;
        float p = Eff(e, e.Spec.Stages[0]);
        if (p <= 0f) return;
        float hours = (2.5f + 1.5f * p) * (e.Avoided ? 0.5f : 1f);
        e.DarkUntil = w.Tick + SimTime.Hours(hours);
        // 주 컴퓨터: 미리 내려 둔 것은 살아남는다 (다시 켜면 그만) — 켜 둔 채 맞으면 몇 시간 멎고 기억이 빈다
        var au = w.Automation;
        bool kept = au.Computer?.Body is Furniture cf && _safed.Contains(cf.Id);
        if (au.Present && !kept && au.MainOnline) { au.Reboot($"{e.Spec.Name} — 회로가 다 식을 때까지", hours * 60f); e.SceneChoice = 2; }
        else if (kept) e.SceneChoice = 1;
        // 생명유지 설비의 제어부: 끌 수 없는 것이라 그대로 맞는다 (컴퓨터도 로봇도 없는 어둠 속에서 사람이 고쳐야 한다)
        int burnt = 0;
        foreach (var f in w.Ship.Furniture.Where(f => f.Type is FurnitureType.OxygenGenerator or FurnitureType.Scrubber && f.Machine != null && !f.Room.Detached && !_safed.Contains(f.Id)).OrderBy(f => f.Id).ToList())
            if (f.Machine!.Faults.Count == 0 && R.Chance(0.7f * p) && w.Machines.Break(f.Machine, FaultKind.ControlFault) != null) { burnt++; MarkLog.Add(f.Machine.Marks, w.Tick, $"{e.Spec.Name} 자기장 벼락에 제어부가 탔다"); }
        if (burnt > 0) w.RaiseAlert($"생명유지 설비 {burnt}대의 제어부가 탔다 — 손으로 고쳐야 한다", null, AlertLevel.Critical, shipWide: true);
        foreach (var r in w.Robots.Robots.OrderBy(r => r.Id))
            if (!r.Disabled && !(r.State == RobotState.Docked && _safed.Contains(r.Dock.Id))) { r.Disabled = true; e.SceneRobots.Add(r.Id); }
        foreach (var d in w.Drones.Drones)
            if (!d.Faulty) { d.Faulty = true; e.SceneDrones.Add(d.Id); }
        foreach (var r in w.Ship.Rooms.Where(r => !r.Detached).OrderBy(r => r.Id))
        {
            if (r.LightsOut) continue;
            r.LightsOut = true;
            e.SceneDark.Add(r.Id);
        }
        e.SceneLine = $"암흑 시간 — {hours:0.#}시간 동안 전자 장치가 멎는다";
        w.History.Add(w, HistoryKind.Incident, $"{e.Spec.Name} 자기장 벼락 — 주 컴퓨터 · 로봇 {e.SceneRobots.Count}대 · 드론 {e.SceneDrones.Count}대 · 조명이 한꺼번에 멎었다. 암흑 시간 {hours:0.#}시간 (우주급)", crew: w.Crew.Where(c => !c.Dead), log: true);
        foreach (var c in w.Crew.Where(c => !c.Dead && c.IsAwake))
        {
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.12f * (1.2f - c.Traits.Bravery));
            MarkLog.Add(c.Memory.Marks, w.Tick, "암흑 시간 — 배의 불이 다 꺼졌다");
        }
        var lead = Boss();
        if (lead != null) w.Log.Add(w.Tick, LogKind.Warning, $"{lead.Name}: 손전등을 나눠 줘 — 격벽은 손잡이로, 점호는 목소리로", lead.Id);
    }

    private void DarkTick(CosmicEvent e)
    {
        var w = _w;
        if (e.DarkUntil < 0) return;
        if (w.Tick < e.DarkUntil)
        {
            // 누가 고쳐 켜도 자기장이 다시 끈다
            foreach (int id in e.SceneDark) if (id < w.Ship.Rooms.Count && !w.Ship.Rooms[id].Detached) w.Ship.Rooms[id].LightsOut = true;
            foreach (var r in w.Robots.Robots) if (e.SceneRobots.Contains(r.Id)) r.Disabled = true;
            float left = (e.DarkUntil - w.Tick) / (float)SimTime.TicksPerHour;
            e.SceneLine = $"암흑 시간 — {(int)left}시간 {(int)((left - (int)left) * 60f):00}분 남았다 · 손으로 굴린다";
            return;
        }
        LightsBack(e);
    }

    private void LightsBack(CosmicEvent e)
    {
        var w = _w;
        if (e.DarkUntil < 0) return;
        e.DarkUntil = -1;
        foreach (int id in e.SceneDark) if (id < w.Ship.Rooms.Count) w.Ship.Rooms[id].LightsOut = false;
        foreach (var r in w.Robots.Robots) if (e.SceneRobots.Contains(r.Id)) r.Disabled = false;
        foreach (var d in w.Drones.Drones) if (e.SceneDrones.Contains(d.Id)) d.Faulty = false;
        e.SceneDark.Clear(); e.SceneRobots.Clear(); e.SceneDrones.Clear();
        w.History.Add(w, HistoryKind.Recovery, "암흑 시간이 끝났다 — 조명 · 로봇 · 드론이 돌아왔다", log: true);
        if (e.SceneChoice == 2 && w.Automation.Computer?.Body is Furniture mc)
        {
            AddTask(e, BraceKind.Logbook, mc.Room.Id, mc.Id, 0.4f, "꺼져 있던 동안의 일을 주 컴퓨터에 적어 넣기");
            e.SceneLine = "주 컴퓨터가 깨어났다 — 꺼져 있던 시간의 기억이 비었다";
            w.Automation.Speak.Announce(w.Automation.Voice.Style("다시 켜졌습니다. 꺼져 있던 동안의 기록이 비어 있습니다 — 무슨 일이 있었는지 알려 주십시오"), mc.Room, 2);
            foreach (var c in w.Crew.Where(c => !c.Dead)) w.Automation.Trusts.Change(c, -0.03f, "암흑 시간 동안 컴퓨터가 아무것도 몰랐다", quiet: true);
        }
        else e.SceneLine = e.SceneChoice == 1 ? "미리 내려 둔 주 컴퓨터는 멀쩡하다 — 다시 켜면 그만" : "";
    }

    // ───────────────────────────── 블랙홀 조석: 늘어나는 배 ─────────────────────────────

    private void TideTick(CosmicEvent e, float dt, bool hourly)
    {
        var w = _w;
        float p = FxNow(e, CosmicFx.Tidal);
        if (p <= 0f || e.Phase != CosmicPhase.Impact) return;
        e.Stretch = MathF.Min(1f, e.Stretch + 0.08f * p * dt);
        e.DriftMin += 4f * p * dt;
        e.SceneLine = $"배가 {e.Stretch * 100:0}% 늘어났다 · 바깥 시계보다 {e.DriftMin:0}분 늦다";
        if (!hourly) return;
        // 끝 구획의 연결부가 당겨진다 (조석 축을 따라 한가운데에서 멀수록)
        var center = ShipCenter();
        var dir = Dir(e);
        var rooms = w.Ship.Rooms.Where(r => !r.Detached && r.DesignJoints > 0).ToList();
        if (rooms.Count == 0) return;
        float half = MathF.Max(4f, rooms.Max(r => MathF.Abs(Vector2.Dot(r.Center - center, dir))));
        Room? worst = null;
        float worstLeft = float.MaxValue;
        foreach (var r in rooms.OrderBy(r => r.Id))
        {
            float t = MathF.Abs(Vector2.Dot(r.Center - center, dir)) / half;
            if (t < 0.45f) continue;
            foreach (var j in r.Joints.Where(j => !j.Broken).OrderBy(j => j.Id))
            {
                j.Strength = MathF.Max(0f, j.Strength - 0.07f * p * t * t * (1f + e.Stretch));
                if (j.Strength <= 0.02f) w.Structure.Break(j, "조석 — 당겨져 끊어졌다");
            }
            float cap = StructureSystem.Capacity(r), snap = StructureSystem.SnapAt(r);
            if (cap - snap < worstLeft) { worstLeft = cap - snap; worst = r; }
            // 끊어지기 전에 사람을 뺀다 (컴퓨터든 사람이든 연결부 소리를 들으면) — 늦으면 그 방째 떠내려간다
            if (cap - snap < 0.5f && _strain.Add(r.Id))
                Broadcast(e, $"{r.Name} 연결부가 버티지 못한다 — 그 방에서 나와라", 2);
        }
        // 끊어지기 전에 떼어 낼까: 사람이 없고 없어도 되는 끝 구획이면 컴퓨터가 묻는다
        if (worst != null && worstLeft < 0.35f && ComputerUp && Council.Essential(w, worst) == null && !w.Crew.Any(c => !c.Dead && c.Room == worst))
        {
            string key = $"cosmic:shed:{e.Id}:{worst.Id}";
            var room = worst;
            if (w.Automation.Asks.Latest(key) == null)
                w.Automation.Asks.Propose(key, "cosmic-shed", room, $"{room.Name} 미리 떼어 낸다 (연결부가 버티지 못한다)",
                    $"남은 연결부 힘 {StructureSystem.Capacity(room):0.00} · 늘어남 {e.Stretch * 100:0}%", "제멋대로 찢겨 나가며 옆 벽을 뜯기 전에 — 그 방은 잃는다", 20f, 0,
                    onAccept: (world, _) =>
                    {
                        if (room.Detached || world.Crew.Any(c => !c.Dead && c.Room == room)) return;
                        // 미리 떼어 내는 것: 댐퍼 · 전선 · 관을 먼저 끊고 닫는다 (찢겨 나갈 때와 달리 환기관 · 회로가 우주로 열리지 않는다)
                        room.VentOpen = false;
                        room.PowerCut = true;
                        room.PipesCut = room.HasPipes;
                        world.Structure.Detach(room, "조석 — 찢기기 전에 떼어 냈다", controlled: true);
                        world.History.Add(world, HistoryKind.Decision, $"{Ko.EulReul(room.Name)} 떼어 냈다 — 블랙홀 조석에 찢겨 나가기 전에", room, log: true);
                    });
        }
    }

    // ───────────────────────────── 큰 소행성: 그 구획을 잃는다 ─────────────────────────────

    /// <summary>비키지 않은 큰 소행성: 들이받은 구획이 뜯겨 나간다 (비우고 봉쇄해 뒀으면 사람은 산다).</summary>
    private void TearOff(CosmicEvent e)
    {
        var w = _w;
        if (e.Kind != CosmicKind.BigAsteroid || e.Avoided || e.TargetRoom < 0) return;
        var room = w.Ship.Rooms[e.TargetRoom];
        if (room.Detached) return;
        w.Structure.Detach(room, $"{e.Spec.Name}이 뜯어 갔다", controlled: false);
        e.SceneLine = $"{room.Name}을 잃었다";
        w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(e.Spec.Name)} {Ko.EulReul(room.Name)} 통째로 뜯어 갔다" + (e.Sealed ? " — 비우고 봉쇄해 둔 덕에 사람은 없었다" : ""), room, log: true);
    }

    // ───────────────────────────── 해적 함대: 도망 · 교섭 · 버팀 ─────────────────────────────

    private void PirateDecision(CosmicEvent e)
    {
        var w = _w;
        if (e.SceneChoice != 0) return;
        if (e.AvoidPlan == 1) { if (e.Avoided) { e.SceneChoice = 1; e.SceneLine = "따돌렸다"; } else e.SceneLine = "전속력으로 도망친다"; if (!e.AvoidFailed) return; }
        if (e.AvoidPlan == 0) return; // 도망칠지 먼저
        // 도망치지 않기로 했거나 못 따돌렸다 → 교섭할까
        string key = "cosmic:tribute:" + e.Id;
        var (food, parts) = TributeSize();
        string title = $"해적에게 물자를 내주고 보낸다 (식량 {food} · 예비 자재 {parts})";
        if (ComputerUp)
        {
            var p = w.Automation.Asks.Latest(key);
            if (p == null)
            {
                w.Automation.Asks.Propose(key, "cosmic-tribute", null, title, "도망치지 못한다 — 쏘아 대면 외판이 뚫리고 승선할 수도", "내주면 쏘지 않고 간다 · 버티면 에어락을 막아야 한다", 30f, null,
                    onAccept: (world, _) => Tribute(e));
                e.SceneLine = "교섭할지 묻는다";
            }
            else if (p.State != ProposalState.Pending && !p.Accepted) Resist(e, $"{p.DecidedBy} — 내주지 않는다");
            return;
        }
        var boss = Boss();
        if (boss == null) { Resist(e, "정할 사람이 없다"); return; }
        float give = boss.Value switch { CrewValue.Safety => 0.75f, CrewValue.People => 0.65f, CrewValue.Efficiency => 0.55f, CrewValue.Rules => 0.3f, _ => 0.15f };
        w.Meetings.Record($"해적 함대 — {(give >= 0.5f ? "물자를 내준다" : "버틴다")}", "cosmic:tribute", -1, boss, give >= 0.5f ? new List<CrewMember> { boss } : new(), give >= 0.5f ? new() : new List<CrewMember> { boss }, e.Spec.Name);
        if (give >= 0.5f) Tribute(e); else Resist(e, $"{boss.Name} — 내주지 않는다 ({MeetingSystem.ValueName(boss.Value)})");
    }

    private (int food, int parts) TributeSize()
    {
        var ship = _w.Ship;
        int food = ship.CountStored(ItemKind.Meal) + ship.CountStored(ItemKind.Ration) + ship.CountStored(ItemKind.Produce);
        int parts = TributeKinds.Sum(k => ship.CountStored(k));
        return (food / 4, parts * 3 / 10);
    }

    private static readonly ItemKind[] TributeKinds = { ItemKind.Plate, ItemKind.Cable, ItemKind.Electronics, ItemKind.Sealant };

    private void Tribute(CosmicEvent e)
    {
        var w = _w;
        if (e.SceneChoice == 2) return;
        e.SceneChoice = 2;
        var (food, parts) = TributeSize();
        int gave = Take(new[] { ItemKind.Ration, ItemKind.Meal, ItemKind.Produce }, food) + Take(TributeKinds, parts);
        e.Power *= 0.1f; // 경고 사격만
        e.SceneLine = $"물자를 내줬다 ({gave}개) — 해적이 물러간다";
        w.History.Add(w, HistoryKind.Decision, $"해적 함대에 식량 · 자재 {gave}개를 내주고 보냈다", log: true);
        foreach (var c in w.Crew.Where(c => !c.Dead))
        {
            if (c.Value == CrewValue.Freedom) { c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.1f); MarkLog.Add(c.Memory.Marks, w.Tick, "해적에게 굽혔다 — 분하다"); }
            else c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
        }
    }

    private int Take(ItemKind[] kinds, int n)
    {
        int got = 0;
        foreach (var k in kinds)
            foreach (var box in _w.Ship.Containers.OrderBy(f => f.Id))
            {
                if (got >= n) return got;
                got += box.Storage!.Take(k, Math.Min(n - got, box.Storage.Count(k)));
            }
        return got;
    }

    private void Resist(CosmicEvent e, string why)
    {
        var w = _w;
        if (e.SceneChoice == 3) return;
        e.SceneChoice = 3;
        e.SceneLine = "버틴다 — 에어락을 막는다";
        w.Log.Add(w.Tick, LogKind.Warning, $"해적 함대 — 버틴다 · {why}");
        if (e.Phase == CosmicPhase.Brace) ScenePlan(e);
    }

    /// <summary>승선 시도: 에어락을 막아 뒀으면 물러가고, 아니면 가까운 방을 털고 사람을 다치게 한다.</summary>
    private void BoardTick(CosmicEvent e)
    {
        var w = _w;
        if (e.SceneNext < 0 || w.Tick < e.SceneNext || e.SceneChoice is 1 or 2 || e.Avoided) return;
        e.SceneNext = -1;
        var air = w.Ship.RoomsOf(RoomType.Airlock).Where(r => !r.Detached).OrderBy(r => r.Id).FirstOrDefault();
        if (air == null) return;
        bool barred = e.Tasks.Any(t => t.Kind == BraceKind.Barricade && t.Done && t.RoomId == air.Id);
        if (barred)
        {
            e.SceneLine = "승선 시도를 막아 냈다";
            w.History.Add(w, HistoryKind.Response, $"해적이 {Ko.EulReul(air.Name)} 뜯다가 물러났다 — 안쪽 문을 막아 둔 덕", air, log: true);
            foreach (var d in air.Doors.Where(d => !d.IsExternal)) d.Bent = MathF.Min(1f, d.Bent + 0.5f);
            return;
        }
        // 에어락과 이웃 방을 턴다
        var near = new List<Room> { air };
        near.AddRange(air.Doors.Select(d => d.RoomA == air ? d.RoomB : d.RoomA).OfType<Room>().Where(r => !r.Detached && r != air).Distinct().OrderBy(r => r.Id));
        int stolen = 0;
        foreach (var box in w.Ship.Containers.Where(f => near.Contains(f.Room)).OrderBy(f => f.Id))
            foreach (var k in TributeKinds.Concat(new[] { ItemKind.Ration, ItemKind.Meal }))
                stolen += box.Storage!.Take(k, (box.Storage.Count(k) + 1) / 2);
        int hurt = 0;
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.Outside && near.Contains(c.Room!)).OrderBy(c => c.Id))
        {
            NeedsSystem.AddInjury(c.Vitals, R.Range(0.2f, 0.5f), "해적");
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.3f);
            Memory.Frighten(w, c, c.Room!, 0.6f, "해적이 올라왔다");
            hurt++;
        }
        e.SceneLine = $"해적이 올라왔다 — {stolen}개를 털어 가고 {hurt}명이 다쳤다";
        w.History.Add(w, HistoryKind.Damage, $"해적이 {Ko.EulReul(air.Name)} 뜯고 올라와 {stolen}개를 털어 갔다 — {hurt}명 다침", air, log: true);
        w.RaiseAlert($"해적 승선 — {air.Name}", air, AlertLevel.Critical, shipWide: true);
    }

    // ───────────────────────────── 다른 배 원자로 폭발: 오염된 생존자 ─────────────────────────────

    private void Distress(CosmicEvent e)
    {
        var w = _w;
        if (e.SceneChoice != 0 || w.Ship.RoomsOf(RoomType.Airlock).All(r => r.Detached)) return;
        int n = R.Range(1, 4);
        e.SceneCount = n;
        string title = $"탈출정을 받는다 — 생존자 {n}명 (낙진을 뒤집어썼다 · 제염해야 한다)";
        Broadcast(e, $"조난 신호 — 터진 배의 탈출정이 다가온다. 생존자 {n}명", 2);
        e.SceneLine = $"조난 신호 — 생존자 {n}명";
        if (ComputerUp)
            w.Automation.Asks.Propose("cosmic:pod:" + e.Id, "cosmic-pod", null, title, "탈출정 산소가 몇 시간 남지 않았다", "받으면 제염 전까지 곁 사람이 쬔다 · 식량이 더 든다 · 안 받으면 그들은 죽는다", 20f, null,
                onAccept: (world, _) => Accept(e), grader: (world, p) => null);
        var boss = Boss();
        if (!ComputerUp)
        {
            float want = boss == null ? 0f : boss.Value switch { CrewValue.People => 0.85f, CrewValue.Rules => 0.6f, CrewValue.Freedom => 0.55f, CrewValue.Efficiency => 0.35f, _ => 0.3f };
            if (want >= 0.5f) Accept(e); else Refuse(e, boss?.Name ?? "정할 사람이 없다");
        }
    }

    private void Accept(CosmicEvent e)
    {
        if (e.SceneChoice != 0) return;
        e.SceneChoice = 1;
        e.SceneNext = _w.Tick + SimTime.Minutes(R.Range(60f, 100f));
        e.SceneLine = "탈출정을 받는다 — 에어락으로 온다";
        _w.Log.Add(_w.Tick, LogKind.Ship, $"{e.Spec.Name} — 탈출정을 받기로 했다 (생존자 {e.SceneCount}명)");
    }

    private void Refuse(CosmicEvent e, string who)
    {
        var w = _w;
        if (e.SceneChoice != 0) return;
        e.SceneChoice = -1;
        e.SceneLine = "탈출정을 받지 않았다";
        w.History.Add(w, HistoryKind.Decision, $"탈출정을 받지 않았다 ({who}) — 생존자 {e.SceneCount}명을 태운 채 멀어져 갔다", log: true);
        foreach (var c in w.Crew.Where(c => !c.Dead))
        {
            float hit = c.Value == CrewValue.People ? 0.25f : 0.08f;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + hit);
            MarkLog.Add(c.Memory.Marks, w.Tick, "탈출정을 외면했다");
        }
    }

    private void PodTick(CosmicEvent e)
    {
        var w = _w;
        // 제안이 거절로 끝났으면
        if (e.SceneChoice == 0 && w.Automation.Asks.Latest("cosmic:pod:" + e.Id) is Proposal p && p.State != ProposalState.Pending && !p.Accepted) Refuse(e, p.DecidedBy);
        if (e.SceneChoice != 1 || e.SceneNext < 0 || w.Tick < e.SceneNext) return;
        e.SceneNext = -1;
        var air = w.Ship.RoomsOf(RoomType.Airlock).Where(r => !r.Detached).OrderBy(r => r.Id).FirstOrDefault();
        var at = air?.Cells.FirstOrDefault(w.Ship.IsOpenFloor);
        if (air == null || at == null || at == default(Cell)) return;
        for (int k = 0; k < e.SceneCount; k++)
        {
            var s = w.AddSurvivor(at.Value);
            s.Dose = R.Range(1.2f, 3f);
            _contam[s.Id] = 1f;
            e.SceneSurvivors.Add(s.Id);
            MarkLog.Add(s.Memory.Marks, w.Tick, $"{e.Spec.Name}에서 살아남아 이 배에 올랐다");
        }
        var decon = w.Ship.FurnitureOf(FurnitureType.DeconShower).FirstOrDefault(f => !f.Room.Detached);
        var room = decon?.Room ?? w.Ship.RoomsOf(RoomType.Medbay).FirstOrDefault(r => !r.Detached) ?? air;
        AddTask(e, BraceKind.Decon, room.Id, decon?.Id ?? -1, 0.4f, $"생존자 제염 — {room.Name}에서 옷을 벗기고 씻긴다");
        e.SceneLine = $"생존자 {e.SceneCount}명이 올랐다 — 제염 전까지 곁에 가면 쬔다";
        w.History.Add(w, HistoryKind.Response, $"{e.Spec.Name}의 탈출정에서 생존자 {e.SceneCount}명을 받았다 — 낙진을 뒤집어쓴 채", air, log: true);
    }

    /// <summary>오염된 사람이 있는 방은 방사선 (제염 전까지 · 시간이 지나면 조금씩 빠진다).</summary>
    private void Contamination(float[] rad)
    {
        var w = _w;
        if (_contam.Count == 0) return;
        foreach (var id in _contam.Keys.OrderBy(x => x).ToList())
        {
            var c = w.Crew.FirstOrDefault(x => x.Id == id);
            if (c == null || c.Dead || c.Room == null) { _contam.Remove(id); continue; }
            if (c.Room.Id < rad.Length) rad[c.Room.Id] = MathF.Max(rad[c.Room.Id], 0.9f * _contam[id]);
        }
    }

    // ───────────────────────────── 행성 고리: 연속 회피 기동 ─────────────────────────────

    private void RingTick(CosmicEvent e)
    {
        var w = _w;
        if (e.SceneNext < 0 || w.Tick < e.SceneNext) return;
        if (FxNow(e, CosmicFx.Debris) <= 0f) { e.SceneNext = -1; return; }
        e.SceneNext = w.Tick + SimTime.Minutes(R.Range(8f, 14f));
        var (q, by, who) = w.Propulsion.Control();
        if (q <= 0f || w.Propulsion.Thrust < 0.1f) { e.SceneLine = "고리 속 — 조종할 사람이 없다"; return; }
        e.SceneCount++;
        w.Maneuver.Begin(ManeuverKind.Evasion, 1f, R.Range(0.25f, 0.42f), $"고리 알갱이 덩어리를 비킨다 ({by})", 0.8f);
        if (who != null)
        {
            who.Needs.Rest = MathF.Max(0f, who.Needs.Rest - 0.05f);
            who.Needs.Stress = MathF.Min(1f, who.Needs.Stress + 0.03f);
        }
        e.SceneLine = $"고리 속 회피 기동 {e.SceneCount}번째 — {by}" + (who != null && who.Needs.Rest < 0.3f ? " (지쳐 간다)" : "");
    }

    /// <summary>고리 알갱이를 피할 확률: 조종 솜씨 × 조종사 기운 (지치면 놓친다).</summary>
    private float RingDodge()
    {
        var (q, _, who) = _w.Propulsion.Control();
        if (q <= 0f) return 0f;
        float rest = who?.Needs.Rest ?? 0.7f;
        return Math.Clamp(q * (0.35f + 0.5f * rest), 0f, 0.8f);
    }
}
