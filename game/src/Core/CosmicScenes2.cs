using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v19 우주급 고유 장면 2단계 (나머지 22종) — 재난마다 그 재난에만 있는 상황 · 선택 · 위험.
//   별: 초대형 플레어(양성자 시계 · 작물 덮개) · 코로나 질량 방출(유도 전류 · 전력망 나누기) · 펄서(박자에 맞춰 일한다)
//       중성자별(바짝 스쳐 항로를 줄일까) · 적색 거성(땀으로 빠지는 물 · 배급) · 쌍성 식(언 관이 터진다 · 온기에 모인다)
//       울프-레이에(바람 쪽 외판이 깎인다 · 덧댐)
//   천체: 혜성(얼음을 떠 올까) · 행성 파편(불붙은 파편 · 금속 건지기) · 케슬러(파편이 불어난다 · 빠져나갈까)
//       초고속 먼지(젖빛 창) · 떠돌이 행성(관측할까)
//   인공: 정거장 붕괴(떠다니는 컨테이너 건지기) · 반물질(섬광 실명) · 핵융합 폭주(배기 불꽃이 스칠 쪽을 비운다)
//       기뢰(하나하나 — 드론이 신관을 끊는다) · 궤도 EMP(멈추고 중립을 알릴까 · 두 번째 사격) · 화물선(견인 드론으로 밀어낸다)
//   이상: 암흑 성운(별 없는 밤 · 등불 모임) · 우주선 소나기(비트가 뒤집힌다 · 헛경보) · 이온 성운(쌓이는 전하 · 번개)
//       중력파(배가 숨 쉰다 · 기록)

/// <summary>장면 속 떠다니는 것 (기뢰 · 컨테이너): 배 둘레 방향 · 거리 · 상태 (0 다가옴 1 처리 2 터짐 · 놓침).</summary>
public sealed class CosmicThing
{
    public float Ang { get; set; }
    public float Dist { get; set; }
    public int State { get; set; }
    public long At { get; set; }
}

public sealed partial class CosmicEvent
{
    /// <summary>장면의 쌓이는 값 (전하 · 파편 밀도 · 깎인 외판 · 얻은 물 · 연구).</summary>
    public float SceneLevel { get; set; }
    /// <summary>장면이 짚은 방 (터진 관 · 비우는 방).</summary>
    public List<int> SceneRooms { get; } = new();
    public List<CosmicThing> SceneThings { get; } = new();
}

public sealed partial class CosmicSystem
{
    private readonly Dictionary<string, bool> _asked = new();
    private readonly Dictionary<int, Action<CrewMember>> _rig = new();
    private readonly Dictionary<int, long> _frosted = new();
    private readonly HashSet<int> _scorch = new(), _covered = new(), _drained = new(), _plated = new();
    private bool _ecc, _grounded;

    /// <summary>초고속 먼지에 사포질돼 바깥이 안 보이는 창 (갈아 낼 때까지).</summary>
    public bool Frosted(Room r) => _frosted.TryGetValue(r.Id, out var t) && _w.Tick < t;
    /// <summary>핵융합 폭주: 배기 불꽃이 스칠 쪽이라 비우는 방.</summary>
    public bool Scorching(Room r) => _scorch.Contains(r.Id);
    /// <summary>화면용: 작물 덮개 · 바람 쪽 덧댐 · 방전 막대.</summary>
    public bool Covered(Room r) => _covered.Contains(r.Id);
    public bool Plated(Room r) => _plated.Contains(r.Id);
    public bool Grounded => _grounded;

    private const int PulsePeriod = 11, PulseOn = 2;

    /// <summary>펄서 빔: 지금 빔 속인가.</summary>
    public bool PulseNow(CosmicEvent e)
    {
        long t = _w.Tick - StageAt(e, 0);
        return t >= 0 && t / SimTime.Minutes(1) % PulsePeriod < PulseOn;
    }

    /// <summary>펄서 빔: 다음 빔까지 남은 분 (빔 속이면 0).</summary>
    public float PulseWait(CosmicEvent e)
    {
        long t = _w.Tick - StageAt(e, 0);
        if (t < 0) return (-t) / (float)SimTime.Minutes(1);
        long m = t / SimTime.Minutes(1) % PulsePeriod;
        return m < PulseOn ? 0f : PulsePeriod - m;
    }

    /// <summary>장면의 선택: 컴퓨터가 있으면 제안 (함장이 정한다), 없으면 지휘하는 사람이 가치관대로. null = 아직.</summary>
    private bool? Ask(CosmicEvent e, string key, string title, string basis, string effect, Func<CrewMember, float> want, Action yes)
    {
        var w = _w;
        if (_asked.TryGetValue(key, out var d)) return d;
        if (ComputerUp)
        {
            var p = w.Automation.Asks.Latest(key);
            if (p == null)
            {
                w.Automation.Asks.Propose(key, "cosmic-scene", null, title, basis, effect, 20f, null,
                    onAccept: (world, _) => { if (!_asked.ContainsKey(key)) { _asked[key] = true; yes(); } });
                return null;
            }
            if (p.State == ProposalState.Pending) return null;
            if (!_asked.ContainsKey(key)) _asked[key] = p.Accepted;
            return _asked[key];
        }
        var boss = Boss();
        bool ok = boss != null && want(boss) >= 0.5f;
        _asked[key] = ok;
        if (boss != null)
            w.Meetings.Record($"{e.Spec.Name} — {title}: {(ok ? "한다" : "안 한다")}", key, -1, boss, ok ? new List<CrewMember> { boss } : new(), ok ? new() : new List<CrewMember> { boss }, e.Spec.Name);
        if (ok) yes();
        return ok;
    }

    private static float Want(CrewMember c, float safety, float eff, float people, float rules, float freedom) => c.Value switch
    {
        CrewValue.Safety => safety, CrewValue.Efficiency => eff, CrewValue.People => people, CrewValue.Rules => rules, _ => freedom,
    };

    /// <summary>장면 준비 일 (지나간 뒤 일 포함) — 마치면 효과.</summary>
    private void Rig(CosmicEvent e, int room, int furniture, float hours, string label, Action<CrewMember> fx, bool late = false)
    {
        if (e.Tasks.Any(t => t.Kind == BraceKind.Rig && t.Label == label)) return;
        var t = new BraceTask { Id = _taskNext++, Kind = BraceKind.Rig, RoomId = room, FurnitureId = furniture, Hours = hours, Label = label, Late = late };
        e.Tasks.Add(t);
        _rig[t.Id] = fx;
    }

    private void RigDone(BraceTask t, CrewMember c)
    {
        if (_rig.TryGetValue(t.Id, out var fx)) fx(c);
    }

    private IEnumerable<Room> Outer(float min = 0.5f) => _w.Ship.Rooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor && _w.Ambience.Exposure(r) >= min).OrderBy(r => r.Id);

    private Drone? FreeDrone(params DroneKind[] kinds) =>
        _w.Drones.Drones.Where(d => d.State == DroneState.Docked && !d.Faulty && !d.Wrecked && d.Battery > 0.4f && (kinds.Length == 0 || kinds.Contains(d.Kind)))
            .OrderByDescending(d => d.Condition).ThenBy(d => d.Id).FirstOrDefault();

    private void Stash(ItemKind k, int n)
    {
        if (n <= 0) return;
        foreach (var box in _w.Ship.Containers.OrderBy(f => f.Id))
        {
            n -= box.Storage!.Add(k, n);
            if (n <= 0) return;
        }
    }

    private void Ignite(Room room)
    {
        var ship = _w.Ship;
        var at = room.Cells.Where(ship.IsOpenFloor).OrderBy(c => c.X * 31 + c.Y).Skip(R.Range(0, Math.Max(1, room.Cells.Count / 2))).FirstOrDefault();
        if (at != default) Incidents.Fire(_w, at);
    }

    // ───────────────────────────── 장면 훅 (2단계) ─────────────────────────────

    private void Scene2Before(CosmicEvent e)
    {
        var w = _w;
        switch (e.Kind)
        {
            case CosmicKind.CoronalMass:
                Ask(e, "cosmic:island:" + e.Id, "전력망을 나눠 둔다 — 덜 급한 회로를 미리 내린다", "긴 전선에 유도 전류가 흐른다 — 차단기가 줄줄이 떨어진다",
                    "휴게 · 관측 쪽 불이 몇 시간 꺼진다 · 생명유지는 지킨다", c => Want(c, 0.85f, 0.4f, 0.55f, 0.7f, 0.3f), () => { e.SceneChoice = 1; e.SceneLine = "전력망을 나눠 둔다"; });
                break;
            case CosmicKind.NeutronStar when e.AvoidPlan == -1 && e.SceneChoice == 0:
            {
                float days = 1.5f;
                Ask(e, "cosmic:swing:" + e.Id, $"중성자별 곁을 바짝 스쳐 항로를 줄인다 (+{days:0.#}일)", "어차피 지난다면 중력에 기대어 돈다 — 연료 없이 빨라진다",
                    "대신 조석 · 방사선이 1.6배", c => Want(c, 0.15f, 0.85f, 0.4f, 0.45f, 0.65f), () =>
                    {
                        e.SceneChoice = 1; e.Power *= 1.6f; e.Close = true; e.SceneLevel = days;
                        e.SceneLine = "바짝 스쳐 돈다 — 항로를 줄인다";
                        w.History.Add(w, HistoryKind.Decision, $"{e.Spec.Name} — 바짝 스쳐 돌아 항로를 {days:0.#}일 줄이기로 했다 (대신 더 세게 맞는다)", log: true);
                    });
                if (_asked.TryGetValue("cosmic:swing:" + e.Id, out var no) && !no && e.SceneChoice == 0) e.SceneChoice = -1;
                break;
            }
            case CosmicKind.CometCore when FreeDrone() != null && w.Water.Level < w.Water.Capacity * 0.85f:
                Ask(e, "cosmic:ice:" + e.Id, "드론을 내보내 혜성 얼음을 떠 온다 (물)", "코마의 얼음 알갱이 — 물탱크를 채울 기회", "드론이 알갱이에 맞아 고장 날 수 있다",
                    c => Want(c, 0.25f, 0.85f, 0.5f, 0.45f, 0.75f), () => { e.SceneChoice = 1; e.SceneLine = "얼음을 뜨러 나간다"; });
                break;
            case CosmicKind.AntimatterBreach when e.Phase == CosmicPhase.Brace && e.SceneCount == 0:
                e.SceneCount = -1;
                Broadcast(e, "반물질 섬광이 온다 — 창을 보지 마라. 관측창 덮개를 내려라", 2);
                break;
            case CosmicKind.FusionRunaway when e.Phase == CosmicPhase.Brace && _scorch.Count == 0 && w.Tick >= e.Arrive - SimTime.Minutes(45):
            {
                var center = ShipCenter();
                foreach (var r in Outer().Where(r => Facing(r, e, center) > 0.55f)) _scorch.Add(r.Id);
                if (_scorch.Count > 0)
                    Broadcast(e, $"폭주하는 배의 배기 불꽃이 스친다 — {string.Join("·", w.Ship.Rooms.Where(r => _scorch.Contains(r.Id)).Select(r => r.Name).Distinct().Take(4))}을 비워라", 2);
                e.SceneLine = $"배기 불꽃이 스칠 쪽 {_scorch.Count}곳을 비운다";
                break;
            }
            case CosmicKind.OrbitalEmp:
                Ask(e, "cosmic:comply:" + e.Id, "엔진을 끄고 식별 신호를 보낸다 — 중립을 알린다", "경고 방송: 멈추지 않는 배는 쏜다",
                    "항로가 여섯 시간 늦어진다 · 펄스는 경고 사격만", c => Want(c, 0.8f, 0.35f, 0.6f, 0.85f, 0.15f), () =>
                    {
                        e.SceneChoice = 1; e.Power *= 0.3f; w.Voyage.Shift(-0.25f);
                        e.SceneLine = "엔진을 끄고 식별 신호를 보냈다";
                        w.History.Add(w, HistoryKind.Decision, $"{e.Spec.Name} — 엔진을 끄고 식별 신호를 보냈다 (경고 사격만 받는다)", log: true);
                    });
                break;
            case CosmicKind.Freighter when (e.AvoidPlan == -1 || e.AvoidFailed) && !e.Avoided && e.SceneChoice == 0 && FreeDrone(DroneKind.Tow) != null:
                Ask(e, "cosmic:shove:" + e.Id, "견인 드론으로 화물선을 밀어 낸다", "응답 없는 배 — 엔진이 꺼진 채 미끄러져 온다", "밀어 내면 스쳐 지나간다 · 드론을 잃을 수 있다",
                    c => Want(c, 0.55f, 0.75f, 0.65f, 0.5f, 0.6f), () => { e.SceneChoice = 1; e.SceneNext = Math.Max(w.Tick + SimTime.Minutes(5), e.Arrive - SimTime.Minutes(20)); e.SceneLine = "견인 드론이 화물선 옆구리로 간다"; });
                break;
        }
    }

    private void Scene2Stage(CosmicEvent e, int i)
    {
        var w = _w;
        switch (e.Kind)
        {
            case CosmicKind.SuperFlare when i == 0:
            {
                e.SceneNext = StageAt(e, 1);
                Broadcast(e, $"플레어 섬광 — 양성자가 {(e.SceneNext - w.Tick) / (float)SimTime.Minutes(1):0}분 뒤 닿는다. 선체 밖은 지금 들어와라", 2);
                break;
            }
            case CosmicKind.CoronalMass when i == 0:
                e.SceneNext = w.Tick + SimTime.Minutes(R.Range(25f, 50f));
                if (e.SceneChoice == 1)
                    foreach (var r in w.Ship.Rooms.Where(r => !r.Detached && !r.LightsOut && r.Type is RoomType.Lounge or RoomType.Observatory or RoomType.Gym or RoomType.Theater or RoomType.Chapel or RoomType.Garden).OrderBy(r => r.Id))
                    { r.LightsOut = true; e.SceneDark.Add(r.Id); }
                break;
            case CosmicKind.PulsarBeam when i == 0:
                Broadcast(e, $"펄서 빔 — {PulsePeriod}분마다 {PulseOn}분씩 지나간다. 빔 사이에 움직이고, 빔이 오면 엎드려라", 2);
                break;
            case CosmicKind.RedGiantShell when i == 0:
                Ask(e, "cosmic:ration:" + e.Id, "물을 배급한다 — 한 사람 몫을 줄인다", "열기에 땀으로 물이 빠진다 — 몇 시간이면 탱크가 준다", "물이 덜 줄지만 다들 목마르고 날카롭다",
                    c => Want(c, 0.7f, 0.75f, 0.35f, 0.8f, 0.3f), () => { e.SceneChoice = 1; });
                break;
            case CosmicKind.Kessler when i == 0:
                e.SceneLevel = 1f;
                break;
            case CosmicKind.RoguePlanet when i == 0:
            {
                var room = w.Ship.Rooms.Where(r => !r.Detached && r.Type is RoomType.Observatory or RoomType.Bridge or RoomType.Navigation).OrderBy(r => r.Type == RoomType.Observatory ? 0 : 1).ThenBy(r => r.Id).FirstOrDefault();
                if (room == null) break;
                Ask(e, "cosmic:observe:" + e.Id, $"{room.Name}에 남아 떠돌이 행성을 관측한다 (연구)", "별 없는 행성 — 평생 한 번 볼까 말까", "연구가 크게 오른다 · 남은 사람은 부스러기에 맞을 수 있다",
                    c => Want(c, 0.25f, 0.7f, 0.5f, 0.5f, 0.85f), () =>
                    {
                        e.SceneChoice = 1;
                        Rig(e, room.Id, -1, 2f, $"{room.Name}에서 떠돌이 행성 관측", c =>
                        {
                            w.Research += 18f; e.SceneLevel += 18f;
                            MarkLog.Add(c.Memory.Marks, w.Tick, "별을 가리며 지나가는 떠돌이 행성을 관측했다");
                            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.1f);
                            w.History.Add(w, HistoryKind.Milestone, $"{Ko.IGa(c.Name)} 떠돌이 행성을 관측해 기록을 남겼다 — 연구가 크게 올랐다", room, new[] { c }, log: true);
                        }, late: true);
                    });
                break;
            }
            case CosmicKind.StationCollapse when i == 0:
                e.SceneNext = w.Tick + SimTime.Minutes(R.Range(30f, 50f));
                break;
            case CosmicKind.AntimatterBreach when i == 0:
            {
                int blind = 0;
                foreach (var c in w.Crew.Where(c => !c.Dead && (c.Outside || c.Room is Room r && w.Body.WindowsOf(r) > 0 && c.IsAwake)).OrderBy(c => c.Id).ToList())
                {
                    // 덮개를 내린 창도 틈 · 반사로 섬광이 샌다 (드물게)
                    if (!c.Outside && c.Room is Room cr && _shut.Contains(cr.Id) && !R.Chance(0.12f)) continue;
                    if (w.Ailments.Catch(c, "flashblind", null, $"{e.Spec.Name} 섬광을 맨눈으로") == null) continue;
                    blind++;
                    MarkLog.Add(c.Memory.Marks, w.Tick, "하얀 점이 하늘을 찢었다 — 눈앞이 하얗다");
                }
                e.SceneCount = blind;
                e.SceneLine = blind > 0 ? $"섬광 실명 {blind}명 — 손으로 더듬어 걷는다" : "모두 눈을 가렸다";
                if (blind > 0) w.History.Add(w, HistoryKind.Casualty, $"{e.Spec.Name} 섬광 — {blind}명이 창을 보다 눈이 멀었다 (며칠이면 돌아온다)", log: true);
                break;
            }
            case CosmicKind.FusionRunaway when i == 0:
            {
                int fires = 0, burnt = 0;
                foreach (var r in w.Ship.Rooms.Where(r => _scorch.Contains(r.Id) && !r.Detached).OrderBy(r => r.Id).ToList())
                {
                    r.Air.Temperature += 30f * (Insulated(r) ? 0.4f : 1f) * Eff(e, e.Spec.Stages[0]);
                    if (R.Chance(0.25f)) { Ignite(r); fires++; }
                    foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == r).OrderBy(c => c.Id))
                    {
                        NeedsSystem.AddInjury(c.Vitals, R.Range(0.1f, 0.3f), "배기 불꽃 열기");
                        burnt++;
                    }
                }
                e.SceneLine = $"배기 불꽃이 스쳤다 — 바깥 방이 달아올랐다" + (fires > 0 ? $" · 불 {fires}곳" : "") + (burnt > 0 ? $" · {burnt}명 화상" : "");
                break;
            }
            case CosmicKind.MineField when i == 0:
                e.SceneNext = w.Tick + SimTime.Minutes(R.Range(20f, 40f));
                break;
            case CosmicKind.OrbitalEmp when i == 0 && e.SceneChoice != 1:
                e.SceneNext = w.Tick + SimTime.Minutes(60);
                e.SceneLine = "멈추지 않았다 — 다음 사격이 온다";
                break;
            case CosmicKind.DarkNebula when i == 0:
                Ask(e, "cosmic:lantern:" + e.Id, "식당에 등불을 켜고 모인다 — 별 없는 밤", "별이 사라지면 사람이 먼저 무너진다", "긴장이 덜 오른다 · 일이 조금 늦다",
                    c => Want(c, 0.6f, 0.3f, 0.9f, 0.5f, 0.65f), () => { e.SceneChoice = 1; e.SceneLine = "식당에 등불을 켰다"; });
                break;
            case CosmicKind.CosmicRayShower when i == 0:
                e.SceneNext = w.Tick + SimTime.Minutes(R.Range(30f, 60f));
                break;
            case CosmicKind.GravityWave when i == 0:
            {
                bool rec = w.Sensors.Array?.Machine is Machine sm && sm.Faults.Count == 0;
                if (rec) { w.Research += 10f; e.SceneLevel = 10f; }
                foreach (var m in w.Ship.Machines) m.SensorCal = MathF.Max(0.3f, m.SensorCal - 0.08f);
                foreach (var c in w.Crew.Where(c => !c.Dead && c.IsAwake))
                    MarkLog.Add(c.Memory.Marks, w.Tick, "배가 숨 쉬듯 늘었다 줄었다 — 모두 같은 순간에 느꼈다");
                e.SceneLine = "배가 숨 쉬듯 늘었다 줄었다" + (rec ? " — 중력계가 기록했다" : "");
                w.History.Add(w, HistoryKind.Milestone, "먼 블랙홀 쌍이 합쳐지며 낸 중력파가 배를 지나갔다 — 모두 같은 순간에 느꼈다" + (rec ? " · 중력계가 기록해 연구가 올랐다" : ""), log: true);
                break;
            }
        }
    }

    private void Scene2Tick(CosmicEvent e, float dt, bool hourly)
    {
        var w = _w;
        bool impact = e.Phase == CosmicPhase.Impact;
        switch (e.Kind)
        {
            case CosmicKind.SuperFlare:
            {
                if (impact && StageActive(e, 0) && w.Tick < e.SceneNext)
                    e.SceneLine = $"양성자 도착까지 {(e.SceneNext - w.Tick) / (float)SimTime.Minutes(1):0}분 — 선체 밖은 들어와라";
                float p = StageActive(e, 1) ? Eff(e, e.Spec.Stages[1]) : 0f;
                if (p <= 0f) break;
                int burnt = 0;
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.GrowBed).Where(f => !f.Room.Detached && f.Machine?.Crop != null).OrderBy(f => f.Id))
                {
                    if (_covered.Contains(f.Room.Id) || w.Ambience.Exposure(f.Room) < 0.5f) continue;
                    var crop = f.Machine!.Crop!;
                    crop.Growth = MathF.Max(0f, crop.Growth - 0.05f * p * dt);
                    crop.Care = MathF.Max(0.2f, crop.Care - 0.04f * p * dt);
                    burnt++;
                }
                e.SceneCount = Math.Max(e.SceneCount, burnt);
                e.SceneLine = burnt > 0 ? $"양성자 비 — 덮지 못한 작물 {burnt}대가 탄다" : "양성자 비 — 작물은 덮어 뒀다";
                break;
            }
            case CosmicKind.CoronalMass when impact && e.SceneNext >= 0 && w.Tick >= e.SceneNext:
            {
                if (FxNow(e, CosmicFx.Radiation) <= 0f) { e.SceneNext = -1; break; }
                e.SceneNext = w.Tick + SimTime.Minutes(R.Range(30f, 60f) * (e.SceneChoice == 1 ? 2.8f : 1f));
                var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).Where(f => !f.Room.Detached && f.Machine != null).OrderBy(f => f.Id).FirstOrDefault()?.Machine;
                if (panel == null) break;
                if (w.Machines.Break(panel, FaultKind.BreakerTrip) != null) e.SceneCount++;
                panel.Heat = MathF.Min(1f, panel.Heat + 0.15f);
                e.SceneLine = $"유도 전류 — 차단기가 떨어졌다 ({e.SceneCount}번째)" + (e.SceneChoice == 1 ? " · 나눠 둔 덕에 드물다" : "");
                break;
            }
            case CosmicKind.PulsarBeam when impact && FxNow(e, CosmicFx.Radiation) > 0f:
            {
                bool on = PulseNow(e);
                if (on && e.SceneNext != 1) { e.SceneNext = 1; e.SceneCount++; }
                else if (!on) e.SceneNext = 0;
                e.SceneLine = on ? $"빔 {e.SceneCount}번째 — 엎드려라" : $"다음 빔까지 {PulseWait(e):0}분 — 박자에 맞춰 움직인다";
                break;
            }
            case CosmicKind.RedGiantShell when impact && FxNow(e, CosmicFx.Heat) > 0f:
            {
                int sweat = w.Crew.Count(c => !c.Dead && !c.Away && c.Room?.Air.Temperature > 28f);
                float lost = sweat * 0.25f * dt * (e.SceneChoice == 1 ? 0.5f : 1f);
                w.Water.Level = MathF.Max(0f, w.Water.Level - lost);
                e.SceneLevel += lost;
                if (e.SceneChoice == 1) foreach (var c in w.Crew.Where(c => !c.Dead && c.IsAwake)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.01f * dt);
                float hot = Outer().Select(r => r.Air.Temperature).DefaultIfEmpty(21f).Max();
                e.SceneLine = $"바깥 방 {hot:0}℃ · 땀으로 빠진 물 {e.SceneLevel:0}L" + (e.SceneChoice == 1 ? " · 물 배급 중" : "");
                break;
            }
            case CosmicKind.BinaryEclipse when impact && FxNow(e, CosmicFx.Cold) > 0f:
            {
                if (hourly)
                    foreach (var r in Outer().Where(r => r.HasPipes && r.Air.Temperature < 2f && !Insulated(r) && !_drained.Contains(r.Id) && !e.SceneRooms.Contains(r.Id)).ToList())
                    {
                        float lost = MathF.Min(w.Water.Level, R.Range(15f, 35f));
                        w.Water.Level -= lost;
                        e.SceneRooms.Add(r.Id);
                        e.SceneLevel += lost;
                        var cell = r.Cells.FirstOrDefault(w.Ship.IsOpenFloor);
                        if (cell != default) w.Body.RaiseMark(cell, CellMark.Wet, 0.8f, $"{e.Spec.Name} — 언 관이 터졌다");
                        w.RaiseAlert($"{r.Name} 관이 얼어 터졌다 — 물 {lost:0}L", r, AlertLevel.Warning, shipWide: true);
                    }
                // 온기에 모인다: 따뜻한 방에 셋 넘게 모이면 긴장이 풀린다
                foreach (var g in w.Crew.Where(c => !c.Dead && c.IsAwake && c.Room is Room r && r.Air.Temperature >= 15f).GroupBy(c => c.Room!.Id).Where(g => g.Count() >= 3))
                    foreach (var c in g) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.01f * dt);
                float cold = Outer().Select(r => r.Air.Temperature).DefaultIfEmpty(21f).Min();
                e.SceneLine = $"바깥 방 {cold:0}℃" + (e.SceneRooms.Count > 0 ? $" · 언 관 {e.SceneRooms.Count}곳이 터졌다" : " · 관은 비워 뒀다");
                break;
            }
            case CosmicKind.WolfRayetWind when impact && hourly:
            {
                float p = FxNow(e, CosmicFx.Plasma);
                if (p <= 0f) break;
                var ship = w.Ship;
                var center = ShipCenter();
                var dir = Dir(e);
                float worn = 0f;
                foreach (var (cell, wall) in ship.Walls.ToList())
                {
                    if (!wall.IsHull) continue;
                    var d = cell.Center - center;
                    if (d.LengthSquared() < 0.5f || Vector2.Dot(Vector2.Normalize(d), dir) < 0.6f) continue;
                    var inside = Hull.InsideRoom(ship, cell);
                    float amt = 0.05f * p * (inside != null && _plated.Contains(inside.Id) ? 0.3f : 1f);
                    Hull.Damage(ship, cell, amt);
                    worn += amt;
                }
                e.SceneLevel += worn;
                e.SceneLine = $"청백색 바람이 바람 쪽 외판을 깎는다" + (_plated.Count > 0 ? " · 덧댄 곳은 버틴다" : "");
                break;
            }
            case CosmicKind.CometCore when impact && e.SceneChoice == 1:
            {
                if (FxNow(e, CosmicFx.Debris) <= 0f) break;
                if (e.SceneNext < 0) e.SceneNext = w.Tick + SimTime.Minutes(30);
                if (w.Tick < e.SceneNext) break;
                e.SceneNext = w.Tick + SimTime.Minutes(R.Range(45f, 70f));
                var d = FreeDrone();
                if (d == null) { e.SceneLine = "보낼 드론이 없다"; break; }
                if (w.Water.Capacity - w.Water.Level < 10f) { e.SceneChoice = 2; e.SceneLine = "물탱크가 찼다 — 드론을 거둔다"; break; }
                float got = MathF.Min(R.Range(30f, 60f), MathF.Max(0f, w.Water.Capacity - w.Water.Level));
                w.Water.Level += got;
                e.SceneLevel += got;
                e.SceneCount++;
                d.Battery = MathF.Max(0.1f, d.Battery - 0.35f);
                bool hit = R.Chance(0.3f * FxNow(e, CosmicFx.Debris));
                if (hit) { d.Condition = MathF.Max(0.1f, d.Condition - 0.35f); d.Faulty = true; MarkLog.Add(d.Marks, w.Tick, $"{e.Spec.Name} 얼음을 뜨다 알갱이에 맞았다"); }
                e.SceneLine = $"{d.Name}이 혜성 얼음을 떠 왔다 (물 +{got:0}L)" + (hit ? " — 알갱이에 맞아 고장" : "");
                break;
            }
            case CosmicKind.Kessler when impact && hourly && FxNow(e, CosmicFx.Debris) > 0f:
            {
                e.SceneCount++;
                e.SceneLevel *= e.Avoided ? 1f : 1.35f;
                int n = e.Avoided ? 0 : Math.Min(6, (int)(e.SceneLevel - 0.5f));
                for (int k = 0; k < n; k++)
                    _launch.Add((w.Tick + SimTime.Minutes(R.Range(1f, 59f)), e.Id, R.Range(0.12f, 0.4f), false));
                _launch.Sort((a, b) => a.tick.CompareTo(b.tick));
                e.SceneLine = e.Avoided ? "파편 지대를 빠져나왔다 — 가장자리만 남았다" : $"파편 밀도 ×{e.SceneLevel:0.0} — 시간마다 늘어난다";
                if (e.SceneCount >= 2 && !e.Avoided)
                {
                    float cost = AvoidCost(e) * 0.6f;
                    Ask(e, "cosmic:escape:" + e.Id, $"지금 빠져나간다 (추진제 {cost:0}kg)", "파편이 파편을 부른다 — 머물수록 더 많아진다", "남은 시간 동안 덜 맞는다",
                        c => Want(c, 0.85f, 0.55f, 0.75f, 0.6f, 0.55f), () =>
                        {
                            if (w.Propulsion.Propellant < cost || w.Propulsion.Control().quality <= 0f) { e.SceneLine = "빠져나갈 추진제 · 조종이 없다"; return; }
                            w.Propulsion.Propellant -= cost; e.Avoided = true; e.SceneChoice = 1; e.FuelSpent += cost;
                            w.History.Add(w, HistoryKind.Response, $"{e.Spec.Name} — 늘어나는 파편 속에서 빠져나왔다 (추진제 {cost:0}kg)", log: true);
                        });
                }
                break;
            }
            case CosmicKind.HyperDust when impact:
            {
                float p = FxNow(e, CosmicFx.Plasma);
                if (p <= 0f) break;
                foreach (var r in w.Ship.Rooms.Where(r => !r.Detached && w.Body.WindowsOf(r) > 0 && !_frosted.ContainsKey(r.Id)).OrderBy(r => r.Id).ToList())
                {
                    // 덮개를 내린 창도 틈으로 먼지가 든다 (시간마다 조금씩) — 맨 창은 바로 젖빛이 된다
                    bool shut = _shut.Contains(r.Id);
                    if (shut && !(hourly && R.Chance(0.2f * p))) continue;
                    _frosted[r.Id] = w.Tick + SimTime.Hours(24f * 4f);
                    e.SceneRooms.Add(r.Id);
                    Rig(e, r.Id, -1, 0.5f, shut ? $"{r.Name} 덮개 틈 먼지 털고 창 갈아 내기" : $"{r.Name} 젖빛 창 갈아 내기", c => { _frosted.Remove(r.Id); MarkLog.Add(r.Marks, w.Tick, $"{Ko.IGa(c.Name)} 사포질된 창을 갈아 냈다"); }, late: true);
                }
                if (hourly) foreach (var f in w.Exterior.All) w.Exterior.Damage(f, 0.08f * p, e.Spec.Name);
                e.SceneLine = e.SceneRooms.Count > 0 ? $"먼지가 창을 사포질했다 — 젖빛 창 {e.SceneRooms.Count}곳" : "덮개를 내린 창은 무사하다";
                break;
            }
            case CosmicKind.StationCollapse when impact && e.SceneNext >= 0 && w.Tick >= e.SceneNext:
            {
                if (FxNow(e, CosmicFx.Debris) <= 0f) { e.SceneNext = -1; break; }
                e.SceneNext = w.Tick + SimTime.Minutes(R.Range(50f, 90f));
                var thing = new CosmicThing { Ang = R.Range(0f, MathF.Tau), Dist = 1.4f, At = w.Tick };
                e.SceneThings.Add(thing);
                var d = FreeDrone(DroneKind.Tow) ?? FreeDrone();
                if (d == null) { thing.State = 2; e.SceneLine = "컨테이너가 떠내려간다 — 건질 드론이 없다"; break; }
                d.Battery = MathF.Max(0.1f, d.Battery - 0.3f);
                if (R.Chance(0.65f * d.Condition))
                {
                    thing.State = 1;
                    e.SceneCount++;
                    Stash(ItemKind.Ration, R.Range(4, 11)); Stash(ItemKind.Electronics, R.Range(1, 4)); Stash(ItemKind.Plate, R.Range(2, 6));
                    e.SceneLine = $"{d.Name}이 떠다니는 컨테이너를 건졌다 ({e.SceneCount}개째)";
                }
                else
                {
                    thing.State = 2;
                    if (R.Chance(0.25f)) { d.Condition = MathF.Max(0.1f, d.Condition - 0.3f); MarkLog.Add(d.Marks, w.Tick, "정거장 조각에 스쳤다"); }
                    e.SceneLine = $"{d.Name}이 컨테이너를 놓쳤다";
                }
                break;
            }
            case CosmicKind.MineField when impact && e.SceneNext >= 0 && w.Tick >= e.SceneNext:
            {
                if (FxNow(e, CosmicFx.Hostile) <= 0f) { e.SceneNext = -1; break; }
                e.SceneNext = w.Tick + SimTime.Minutes(R.Range(40f, 70f) * (e.Avoided ? 2f : 1f));
                var mine = new CosmicThing { Ang = R.Range(0f, MathF.Tau), Dist = 1.3f, At = w.Tick };
                e.SceneThings.Add(mine);
                e.SceneCount++;
                var d = FreeDrone(DroneKind.Repair, DroneKind.Build) ?? FreeDrone();
                bool boom;
                if (d != null)
                {
                    d.Battery = MathF.Max(0.1f, d.Battery - 0.3f);
                    boom = !R.Chance(0.7f * d.Condition);
                    if (!boom) { mine.State = 1; Stash(ItemKind.Electronics, 1); e.SceneLine = $"기뢰 {e.SceneCount}번째 — {d.Name}이 신관을 끊었다"; }
                    else
                    {
                        if (R.Chance(0.5f)) { d.State = DroneState.Lost; d.StateSince = w.Tick; d.Doing = "기뢰와 함께 터졌다"; MarkLog.Add(d.Marks, w.Tick, "기뢰 신관을 끊다 함께 터졌다"); w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(d.Name)} 기뢰 신관을 끊다 함께 터졌다", log: true); }
                        else { d.Condition = MathF.Max(0.1f, d.Condition - 0.4f); d.Faulty = true; }
                        e.SceneLine = $"기뢰 {e.SceneCount}번째 — 신관을 끊다 터졌다";
                    }
                }
                else
                {
                    var (q, by, _) = w.Propulsion.Control();
                    if (q > 0f && w.Propulsion.Thrust >= 0.1f) w.Maneuver.Begin(ManeuverKind.Evasion, 1f, R.Range(0.25f, 0.4f), $"기뢰를 비킨다 ({by})", 0.8f);
                    boom = !R.Chance(RingDodge());
                    mine.State = boom ? 2 : 1;
                    e.SceneLine = boom ? $"기뢰 {e.SceneCount}번째 — 비키지 못했다" : $"기뢰 {e.SceneCount}번째 — 비켜 지나갔다";
                }
                if (boom)
                {
                    mine.State = 2;
                    _launch.Add((w.Tick + SimTime.Minutes(1), e.Id, R.Range(0.35f, 0.7f), true));
                    _launch.Sort((a, b) => a.tick.CompareTo(b.tick));
                }
                break;
            }
            case CosmicKind.OrbitalEmp when e.SceneNext >= 0 && w.Tick >= e.SceneNext:
            {
                e.SceneNext = -1;
                Emp(e, 0.7f * e.Power);
                e.SceneLine = "두 번째 펄스 — 멈추지 않은 값";
                w.History.Add(w, HistoryKind.Damage, $"{e.Spec.Name} — 멈추지 않은 배에 두 번째 펄스가 날아왔다", log: true);
                break;
            }
            case CosmicKind.Freighter when e.SceneChoice == 1 && e.SceneNext >= 0 && w.Tick >= e.SceneNext:
            {
                e.SceneNext = -1;
                var d = FreeDrone(DroneKind.Tow);
                if (d == null) { e.SceneLine = "견인 드론이 나가지 못했다"; break; }
                d.Battery = MathF.Max(0.05f, d.Battery - 0.6f);
                if (R.Chance(0.15f + 0.55f * d.Condition))
                {
                    e.Avoided = true; e.SceneChoice = 2;
                    if (e.Sealed) Unseal(e, "밀어 냈다");
                    e.SceneLine = $"{d.Name}이 화물선을 밀어 냈다 — 스쳐 지나간다";
                    w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(d.Name)} 응답 없는 화물선을 밀어 내 정면 충돌을 막았다", log: true);
                }
                else
                {
                    e.SceneChoice = 3;
                    if (R.Chance(0.5f)) { d.State = DroneState.Lost; d.StateSince = w.Tick; d.Doing = "화물선에 짓눌렸다"; MarkLog.Add(d.Marks, w.Tick, "화물선을 밀다 짓눌렸다"); }
                    e.SceneLine = "밀어 내지 못했다 — 들이받는다";
                    w.History.Add(w, HistoryKind.Damage, $"견인 드론이 화물선을 밀어 내지 못했다", log: true);
                }
                break;
            }
            case CosmicKind.DarkNebula when impact && FxNow(e, CosmicFx.Blind) > 0f:
            {
                foreach (var c in w.Crew.Where(c => !c.Dead && c.IsAwake))
                {
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.012f * dt * (e.SceneChoice == 1 ? 0.35f : 1f));
                    if (e.SceneChoice == 1 && c.Room?.Type == RoomType.Mess) c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.05f * dt);
                }
                float h = (w.Tick - StageAt(e, 0)) / (float)SimTime.TicksPerHour;
                e.SceneLine = $"별이 없는 {h:0}시간째" + (e.SceneChoice == 1 ? " — 식당 등불 곁에 모인다" : " — 다들 말이 없다");
                break;
            }
            case CosmicKind.CosmicRayShower when impact && e.SceneNext >= 0 && w.Tick >= e.SceneNext:
            {
                if (FxNow(e, CosmicFx.Radiation) <= 0f) { e.SceneNext = -1; break; }
                e.SceneNext = w.Tick + SimTime.Minutes(R.Range(40f, 80f));
                if (_ecc && !R.Chance(0.3f)) { e.SceneLine = "비트가 뒤집혔다 — 오류 정정 사본이 바로잡았다"; break; }
                var room = w.Ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Corridor).OrderBy(r => r.Id).Skip(R.Range(0, Math.Max(1, w.Ship.Rooms.Count(r => !r.Detached && r.Type != RoomType.Corridor)))).FirstOrDefault();
                if (room == null || !w.Automation.Present) break;
                w.Automation.Belief.Break(room, SensorFault.Ghost, $"{e.Spec.Name} — 비트가 뒤집혔다");
                e.SceneCount++;
                e.SceneRooms.Add(room.Id);
                foreach (var c in w.Crew.Where(c => !c.Dead)) w.Automation.Trusts.Change(c, -0.005f, "우주선이 컴퓨터 기억을 뒤집었다", quiet: true);
                e.SceneLine = $"비트가 뒤집힌다 — {room.Name} 감지기가 헛것을 본다 ({e.SceneCount}번째)";
                break;
            }
            case CosmicKind.IonNebula when impact:
            {
                float p = FxNow(e, CosmicFx.Emp);
                if (p <= 0f) break;
                e.SceneLevel += 0.18f * p * dt * (_grounded ? 0.35f : 1f);
                if (e.SceneLevel >= 1f)
                {
                    e.SceneLevel = 0.15f;
                    e.SceneCount++;
                    FlashNow = MathF.Max(FlashNow, 0.6f);
                    if (w.Exterior.All.Count > 0) { var f = w.Exterior.All[R.Range(0, w.Exterior.All.Count)]; w.Exterior.Damage(f, 0.25f, "이온 성운 방전"); }
                    var m = w.Ship.Furniture.Where(f => !f.Room.Detached && f.Machine != null && Sensitive(f) && !_safed.Contains(f.Id) && f.Machine.Faults.Count == 0).OrderBy(f => f.Id).ToList();
                    if (m.Count > 0 && R.Chance(0.6f)) { var hit = m[R.Range(0, m.Count)]; if (w.Machines.Break(hit.Machine!, FaultKind.ControlFault) != null) MarkLog.Add(hit.Machine!.Marks, w.Tick, "이온 성운 방전에 제어부가 탔다"); }
                    w.RaiseAlert($"{e.Spec.Name} — 쌓인 전하가 번개로 터졌다 ({e.SceneCount}번째)", null, AlertLevel.Warning, shipWide: true);
                }
                e.SceneLine = $"전하 {e.SceneLevel * 100:0}%" + (e.SceneCount > 0 ? $" · 번개 {e.SceneCount}번" : "") + (_grounded ? " · 방전 막대를 폈다" : "");
                break;
            }
        }
    }

    private void Scene2After(CosmicEvent e)
    {
        var w = _w;
        switch (e.Kind)
        {
            case CosmicKind.SuperFlare when e.SceneCount > 0:
                w.History.Add(w, HistoryKind.Damage, $"{e.Spec.Name} 양성자 비에 덮지 못한 작물 {e.SceneCount}대가 탔다", log: true);
                break;
            case CosmicKind.CoronalMass:
                foreach (int id in e.SceneDark) if (id < w.Ship.Rooms.Count) w.Ship.Rooms[id].LightsOut = false;
                e.SceneDark.Clear();
                if (e.SceneCount > 0) w.History.Add(w, HistoryKind.Damage, $"{e.Spec.Name} 유도 전류에 차단기가 {e.SceneCount}번 떨어졌다" + (e.SceneChoice == 1 ? " (전력망을 나눠 둔 덕에 덜했다)" : ""), log: true);
                break;
            case CosmicKind.NeutronStar when e.SceneChoice == 1:
                w.Voyage.Shift(e.SceneLevel);
                w.History.Add(w, HistoryKind.Milestone, $"중성자별 곁을 바짝 돌아 나왔다 — 항로가 {e.SceneLevel:0.#}일 줄었다", log: true);
                break;
            case CosmicKind.RedGiantShell when e.SceneLevel >= 1f:
                w.History.Add(w, HistoryKind.Damage, $"적색 거성 외층을 지나며 땀으로 물 {e.SceneLevel:0}L가 빠졌다" + (e.SceneChoice == 1 ? " (배급으로 버텼다)" : ""), log: true);
                break;
            case CosmicKind.BinaryEclipse when e.SceneRooms.Count > 0:
                w.History.Add(w, HistoryKind.Damage, $"쌍성 식 급랭에 관 {e.SceneRooms.Count}곳이 얼어 터졌다 — 물 {e.SceneLevel:0}L", log: true);
                break;
            case CosmicKind.WolfRayetWind when e.SceneLevel > 0.05f:
                w.History.Add(w, HistoryKind.Damage, $"{e.Spec.Name}이 바람 쪽 외판을 깎았다 (닳음 {e.SceneLevel:0.0})" + (_plated.Count > 0 ? " · 덧댄 곳은 버텼다" : ""), log: true);
                break;
            case CosmicKind.CometCore when e.SceneLevel > 0f:
                w.History.Add(w, HistoryKind.Milestone, $"드론이 혜성 얼음을 {e.SceneCount}번 떠 와 물 {e.SceneLevel:0}L를 채웠다", log: true);
                break;
            case CosmicKind.ShatteredPlanet:
                Ask(e, "cosmic:salvage:" + e.Id, "빛나는 파편에서 금속을 건진다 (드론)", "부서진 행성의 철 · 니켈 덩어리가 곁에 떠 있다", "금속판 · 구조재를 얻는다",
                    c => Want(c, 0.55f, 0.9f, 0.6f, 0.6f, 0.75f), () =>
                    {
                        int plates = R.Range(4, 10), frames = R.Range(2, 6);
                        Stash(ItemKind.Plate, plates); Stash(ItemKind.Structure, frames);
                        w.History.Add(w, HistoryKind.Milestone, $"부서진 행성의 파편에서 금속판 {plates} · 구조재 {frames}을 건졌다", log: true);
                    });
                break;
            case CosmicKind.StationCollapse when e.SceneCount > 0:
                w.History.Add(w, HistoryKind.Milestone, $"무너진 정거장의 컨테이너 {e.SceneCount}개를 드론으로 건졌다", log: true);
                break;
            case CosmicKind.FusionRunaway:
                _scorch.Clear();
                break;
            case CosmicKind.Freighter when FreeDrone(DroneKind.Tow) != null:
            {
                int food = R.Range(6, 15), elec = R.Range(2, 5);
                Stash(ItemKind.Ration, food); Stash(ItemKind.Electronics, elec);
                w.History.Add(w, HistoryKind.Milestone, $"응답 없던 화물선에서 비상식량 {food} · 전자 부품 {elec}을 건졌다 (아무도 타고 있지 않았다)", log: true);
                break;
            }
            case CosmicKind.MineField when e.SceneCount > 0:
                w.History.Add(w, HistoryKind.Response, $"기뢰 지대를 지났다 — 기뢰 {e.SceneCount}개 중 {e.SceneThings.Count(t => t.State == 1)}개를 처리했다", log: true);
                break;
            case CosmicKind.CosmicRayShower when e.SceneCount > 0:
                w.History.Add(w, HistoryKind.Incident, $"우주선 소나기에 컴퓨터 기억이 {e.SceneCount}번 뒤집혀 헛경보가 났다", log: true);
                break;
            case CosmicKind.IonNebula when e.SceneCount > 0:
                w.History.Add(w, HistoryKind.Damage, $"이온 성운에서 쌓인 전하가 번개로 {e.SceneCount}번 터졌다", log: true);
                break;
            case CosmicKind.DarkNebula:
                w.History.Add(w, HistoryKind.Lesson, "암흑 성운을 빠져나왔다 — 별이 하나둘 돌아왔다" + (e.SceneChoice == 1 ? " · 식당 등불 곁에서 버틴 밤" : ""), log: true);
                foreach (var c in w.Crew.Where(c => !c.Dead)) MarkLog.Add(c.Memory.Marks, w.Tick, "별이 다시 보였다");
                break;
        }
    }

    private void Scene2Plan(CosmicEvent e)
    {
        var w = _w;
        switch (e.Kind)
        {
            case CosmicKind.SuperFlare:
                foreach (var r in w.Ship.Rooms.Where(r => !r.Detached && r.Furniture.Any(f => f.Type == FurnitureType.GrowBed) && w.Ambience.Exposure(r) >= 0.5f).OrderBy(r => r.Id).Take(3))
                    Rig(e, r.Id, -1, 0.3f, $"{r.Name} 작물 덮개 씌우기", _ => _covered.Add(r.Id));
                break;
            case CosmicKind.BinaryEclipse:
                foreach (var r in Outer().Where(r => r.HasPipes).Take(3))
                    Rig(e, r.Id, -1, 0.3f, $"{r.Name} 관 물 빼기 (얼어 터지지 않게)", _ => _drained.Add(r.Id));
                break;
            case CosmicKind.WolfRayetWind:
            {
                var center = ShipCenter();
                foreach (var r in Outer().OrderByDescending(r => Facing(r, e, center)).ThenBy(r => r.Id).Take(2))
                    Rig(e, r.Id, -1, 0.5f, $"{r.Name} 바람 쪽 외판 덧대기", _ => _plated.Add(r.Id));
                break;
            }
            case CosmicKind.CosmicRayShower when w.Automation.Computer?.Body is Furniture mc:
                Rig(e, mc.Room.Id, mc.Id, 0.4f, "주 컴퓨터 기억에 오류 정정 사본 걸기", _ => _ecc = true);
                break;
            case CosmicKind.IonNebula:
            {
                var air = w.Ship.RoomsOf(RoomType.Airlock).Where(r => !r.Detached).OrderBy(r => r.Id).FirstOrDefault();
                if (air != null) Rig(e, air.Id, -1, 0.5f, "방전 막대 펴기 (에어락 바깥 고리)", _ => _grounded = true);
                break;
            }
        }
    }

    /// <summary>장면 상태를 지운다 (재난이 모두 끝나 다음 것을 맞을 때).</summary>
    private void Scene2Reset()
    {
        _covered.Clear(); _drained.Clear(); _plated.Clear(); _scorch.Clear();
        _ecc = false; _grounded = false;
    }
}
