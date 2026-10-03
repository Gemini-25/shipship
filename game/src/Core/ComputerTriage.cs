using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.20 ③ 전력 트리아지 — 원격으로 되는 건 컴퓨터가 직접:
//  필수 우선순위표(설비 순위 + 지금 사정: 누운 사람이 있는 치료 침대 · 운석이 오는데 센서 …)로 비필수부터 끄고 필수 회로에 몰아준다 (몰아주기).
//  보조 발전기를 원격으로 켠다 (오래 쉬던 기계라 가끔 안 걸린다 — 두 번 안 되면 사람에게 정확한 지시) · 원자로가 달아오르면 출력을 낮춘다 ·
//  냉각이 돌아오면 원자로 재기동을 기관사에게 부탁한다 (제어봉 구동은 손으로) · 배전반 회로가 단락으로 죽으면 예비 배선(보조 간선)을 원격으로 넣는다.
//  차단기: 원인을 먼저 끊고(스마트 콘센트로 이동식 장비 · 물 찬 방 분전함) 원격으로 올린다 — 같은 원인으로 다시 떨어지면 다시 올리지 않고
//   사람에게 원인을 빼 달라고 한다 (v16.7 D 차단기 반복 버그: 원인을 안 빼고 차단기만 올려 몇 분 뒤 다시 떨어졌다).
//  예비 코어(주 코어가 멎음)는 우선순위표로 끄고 켜는 반사만 · 원인 분석 · 원격 차단기 · 예측은 주 코어가 돌 때만.

public sealed class BreakerCase
{
    public int Circuit { get; init; }
    public long Since { get; init; }
    public string Cause { get; set; } = "";
    public string Sig { get; set; } = "";
    /// <summary>원격으로 끊을 수 있는 원인: "portable" · "water" · "" (모름).</summary>
    public string Kind { get; set; } = "";
    /// <summary>"분석" · "원인 끊음" · "올림" · "보류" · "사람에게".</summary>
    public string State { get; set; } = "분석";
    public bool Open { get; set; } = true;
    public int Decision { get; set; } = -1;
    public bool Same { get; set; }
    public List<string> Cut { get; } = new();
}

public sealed class PowerTriage
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 5153 + 89));
    private readonly HashSet<int> _parked = new();
    private readonly List<int> _parkOrder = new();
    public IReadOnlyCollection<int> Parked => _parked;
    public float ParkedKw { get; private set; }
    public float ReactorCap { get; private set; } = 1f;
    public string Mode { get; private set; } = "평시";
    public string Plan { get; private set; } = "";
    public List<BreakerCase> Cases { get; } = new();
    private readonly Dictionary<int, (string sig, long tick)> _lastReset = new();
    private readonly Dictionary<int, (string sig, long tick)> _prevReset = new(); // 통합8 그 앞의 올림 — 사이에 기동 전류로 한 번 더 떨어져도 같은 원인을 알아본다
    public int LowPower;
    private bool _ownBrownout;
    private long _lowAnnounced = -1;
    public int RemoteResets, CauseCuts, Holds, SameCauseHolds, AuxStarts, AuxFails, AuxHandsAsked, Parks, Unparks, Feeders, Trims, RestartAsks, Decisions;
    private int _auxTries;
    private long _auxNext, _next, _restartAsked = -1, _decidedAt = -1;
    public bool AuxNeedsHands { get; private set; }
    private ForeseeDecision? _power;
    public ForeseeDecision? PowerDecision => _power;

    public PowerTriage(World w) => _w = w;

    public bool Busy => _parked.Count > 0 || Cases.Any(c => c.Open) || Mode != "평시";

    // ───────────── 필수 우선순위표 ─────────────

    /// <summary>설비 순위 (높을수록 필수): 설비 기본 순위 + 지금 사정.</summary>
    public static int Rank(World w, Machine m)
    {
        int r = m.Spec.Priority;
        switch (m.Body.Type)
        {
            case FurnitureType.MainComputer: return 12; // 컴퓨터는 자기 절전으로 스스로 줄인다
            case FurnitureType.MedBed: if (w.Crew.Any(c => !c.Dead && (c.Down || c.CareBed == m.Body))) r = 11; break;
            case FurnitureType.SensorArray: if (w.Sensors.Incoming.Count > 0) r = 10; break;
            case FurnitureType.EngineCore: if (w.Propulsion.Current != null) r = 11; break;
            case FurnitureType.GrowBed or FurnitureType.Fridge: r = Math.Max(r, 8); break; // 먹을 것 — 작물 · 식량은 한 번 잃으면 못 되돌린다
            case FurnitureType.Stove or FurnitureType.MealDispenser: r = Math.Max(r, 7); break;
        }
        if (m.Body.Room.Type is RoomType.LifeSupport) r = Math.Max(r, 9);
        if (w.Automation.ReserveOrNull?.Holds(m) == true) r = Math.Max(r, 10); // v16.26 예약 (이송 중인 부상자 → 의무실)
        return r;
    }

    public static string TierName(int rank) => rank >= 9 ? "필수" : rank >= 6 ? "생활" : rank >= 4 ? "작업" : "편의";

    /// <summary>원격으로 끌 수 있는 설비 (데이터선이 닿고 · 끌 만한 순위 · 사람이 붙어 일하지 않는).</summary>
    private bool Parkable(Machine m, int maxRank) =>
        !m.Body.Room.Detached && m.Body.Room.DataLinked && m.Spec.PowerDraw > 0f && !m.Parked && !m.Stopped && Rank(_w, m) <= maxRank
        && !(m.Active && m.Body.Type is FurnitureType.Refinery or FurnitureType.Workbench or FurnitureType.Fabricator && _w.Crew.Any(c => c.Job?.Target == m.Body && c.Pose == Pose.Working));

    // ───────────── 시스템 틱 ─────────────

    public void Update(float dt)
    {
        var w = _w;
        var a = w.Automation;
        if (w.Tick < _next) return;
        _next = w.Tick + Math.Max(World.SystemInterval, (long)(SimTime.Minutes(1) / a.Core.Speed));
        if (a.MainOnline && a.Level >= 4) Breakers();
        else foreach (var c in Cases) if (c.Open) { c.State = "사람에게 (주 코어 없음)"; c.Open = false; }
        Supply();
        // 끈 설비가 사라졌으면 (떨어져 나감 · 뜯김) 표에서 뺀다
        if (_parked.Count > 0)
        {
            ParkedKw = 0f;
            foreach (var m in w.Ship.Machines)
                if (_parked.Contains(m.Body.Id)) ParkedKw += m.Spec.PowerDraw * Grades.Power(m.Grade) * Tech.Of(m).Power;
        }
        else ParkedKw = 0f;
    }

    /// <summary>Power.UpdateParking 훅: 컴퓨터가 끈 설비를 내려 둔다.</summary>
    internal void Park()
    {
        if (_parked.Count == 0) return;
        foreach (var m in _w.Ship.Machines) if (_parked.Contains(m.Body.Id) && !m.Body.Room.Detached) m.Parked = true;
    }

    // ───────────── 전력: 몰아주기 · 보조 발전기 · 원자로 · 예비 배선 ─────────────

    private void Supply()
    {
        var w = _w;
        var a = w.Automation;
        var p = w.Power;
        var ch = a.Character;
        // v16.24 배터리가 바닥나면 빠지는 흐름이 0이다 — 모자란 만큼(못 준 전기)을 빠지는 양으로 본다 (아니면 '배터리로 버틴다'가 99시간으로 보였다)
        float drain = MathF.Max(MathF.Max(0f, -p.BatteryFlow), p.BatteryPercent < 0.05f ? MathF.Max(0f, p.Demand - p.Delivered) : 0f);
        bool deficit = p.DeficitSince >= 0;
        bool crisis = drain > 0.3f && (!p.ReactorOnline || p.BatteryPercent < 0.6f) || deficit;
        var auxF = w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault(f => !f.Room.Detached);
        bool auxRemote = auxF?.Machine is Machine am && !am.Stopped && !p.AuxRunning && p.AuxFuel > 0.1f && auxF.Room.DataLinked && !AuxNeedsHands;
        float auxKw = auxRemote ? p.AuxRated * MathF.Max(0.3f, auxF!.Machine!.Efficiency > 0f ? auxF.Machine.Efficiency : 0.8f) : 0f;
        float auxFail = auxF?.Machine is Machine am2 ? Math.Clamp(0.1f + 0.4f * (1f - am2.Condition) + 0.25f * am2.Faults.Count, 0f, 0.9f) : 1f;
        float restore = p.ReactorOnline ? 0f : p.CoolingCapacity >= PowerGrid.RestartCoolingKw && p.Reactor is { Stopped: false } ? 1f + p.ReactorPoison * 2f : 99f;
        int crew = w.Crew.Count(c => !c.Dead && !c.Away);

        if (crisis)
        {
            if (Mode == "평시") Mode = "주의";
            // 얼마나 끌 수 있나 (순위 낮은 것부터 · 생활까지): 목표 — 복구까지 · 아니면 12시간 버틸 만큼 (신중하면 더 오래)
            float targetH = (restore < 98f ? restore + 1f : 12f) * (1f + 0.5f * MathF.Max(0f, ch.Caution));
            float need = MathF.Max(0f, drain - MathF.Max(0f, p.BatteryCharge - (a.ReserveOrNull?.KeepKwh ?? 0f)) / MathF.Max(0.5f, targetH)); // v16.26 재기동 여유는 남긴다
            // 배전반이 이미 떨군 설비(전기가 모자라 꺼진 것)도 표에 넣어 둔다 — 전기가 조금 돌아와도 다시 먹지 않게. 줄어드는 몫은 지금 켜진 것만.
            var cands = w.Ship.Machines.Where(m => Parkable(m, 7)).OrderBy(m => Rank(w, m)).ThenByDescending(m => m.Demand).ThenBy(m => m.Body.Id).ToList();
            float parkKw = 0f;
            var pick = new List<Machine>();
            foreach (var m in cands)
            {
                if (parkKw >= need && Rank(w, m) >= 4) break; // 편의(순위 3 아래)는 위기면 다 끈다
                pick.Add(m);
                if (m.Powered) parkKw += m.Demand;
            }
            // 저출력 운영(남는 펌프 · 산소 발생기 하나 · 빈 치료 침대 · 엔진 · 함교 밖 콘솔 · 센서): 사람이 배전반에 가기 전에 여기서 돌린다
            bool hurt = w.Crew.Any(c => !c.Dead && (c.Down || c.Vitals.Injury > 0.25f)) || a.ReserveOrNull?.MedHold == true;
            float lowKw = p.Brownout ? 0f : w.Ship.Machines.Where(m => m.Powered && !m.Body.Room.Detached && (m.Body.Type is FurnitureType.EngineCore or FurnitureType.SensorArray
                || m.Body.Type == FurnitureType.Console && m.Body.Room.Type != RoomType.Bridge || m.Body.Type == FurnitureType.MedBed && !hurt)).Sum(m => m.Demand);
            parkKw += lowKw;
            // 미리 돌려 보기: 위기 시작 · 30분마다 다시
            if (a.MainOnline && a.Level >= 4 && (_power == null || _decidedAt < 0 || w.Tick - _decidedAt > SimTime.Minutes(30)))
            {
                _power = a.Foresee.Power(drain, parkKw, auxKw, auxFail, restore, crew);
                _decidedAt = w.Tick;
                Decisions++;
                Plan = _power.Pick.Name;
            }
            string choice = _power?.Pick.Key ?? (auxKw > 0f ? "aux" : "park"); // 예비 코어: 표대로
            if (choice is "park" or "aux")
            {
                Mode = "몰아주기";
                int n = 0;
                var panelF = w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault();
                if (!p.Brownout && panelF != null && !panelF.Room.Detached && panelF.Room.DataLinked)
                {
                    p.Brownout = true;
                    p.BrownoutSince = w.Tick;
                    p.Brownouts++;
                    LowPower++;
                    _ownBrownout = true;
                    foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.Brownout).ToList()) w.Board.Close(o);
                    a.Command.Line(CmdTarget.Circuit, -1, panelF.Room, "저출력 운영으로 돌림", "남는 펌프 · 산소 발생기 하나 · 빈 치료 침대 · 엔진 · 콘솔을 내리고 먹을 것 · 숨 쉴 것에 전기를", 0.8f, 60f, _power?.Id ?? -1);
                    w.History.Add(w, HistoryKind.Adaptation, $"주 컴퓨터가 저출력 운영으로 돌렸다 — 배터리 {p.BatteryPercent * 100:0}% · {drain:0.#}kW씩 빠진다 (사람이 배전반에 가기 전에)", panelF.Room);
                    w.Board.RequestScan();
                }
                foreach (var m in pick)
                {
                    if (!_parked.Add(m.Body.Id)) continue;
                    _parkOrder.Add(m.Body.Id);
                    Parks++;
                    n++;
                    a.Command.Line(CmdTarget.Machine, m.Body.Id, m.Body.Room, $"{m.Name} 끔 ({TierName(Rank(w, m))} · {m.Demand:0.#}kW)", "생명유지 쪽으로 돌린다", 0.6f, 30f, _power?.Id ?? -1);
                }
                if (n > 0 || LowPower > 0 && _lowAnnounced < 0)
                {
                    _lowAnnounced = w.Tick;
                    string what = (n > 0 ? $"급하지 않은 설비 {n}대" : "") + (n > 0 && p.Brownout ? " · " : "") + (p.Brownout ? "남는 펌프 · 산소 발생기 하나 · 빈 치료 침대 · 엔진" : "");
                    a.Book.Add(ActKind.Shed, null, $"배터리 {p.BatteryPercent * 100:0}%에서 {drain:0.#}kW씩 빠진다" + (p.ReactorOnline ? "" : " · 원자로 정지"), $"생명유지 · 냉각 · 의무실 먼저 — {targetH:0}시간은 버텨야 한다",
                        $"{Ko.EulReul(what)} 내렸다 — 그만큼 생명유지 쪽으로", "", "park", SimTime.Minutes(20), 30f,
                        (world, act) => (world.Power.BatteryPercent > 0.05f || world.Power.ReactorOnline ? 1 : -1, world.Power.BatteryPercent > 0.05f || world.Power.ReactorOnline ? "맞았다 — 필수 회로가 버텼다" : "틀렸다 — 그래도 바닥났다"));
                    w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 전기가 모자라 {what}{Ko.EulReul((n > 0 ? $"({string.Join(" · ", pick.Take(4).Select(m => m.Name))}{(pick.Count > 4 ? " …" : "")})" : ""))} 내리고 생명유지 쪽으로 돌립니다");
                    a.Speak.Announce(a.Voice.Style(a.Character.Flavor("전기가 모자랍니다 — 급하지 않은 것부터 내리고 생명유지실 쪽으로 먼저 돌립니다" + (a.Core.SelfSaving ? ". 제 연산도 줄입니다" : ""))), null, 1);
                }
            }
            if (choice == "aux" && auxRemote) StartAux(auxF!, a);
        }
        else if (_parked.Count > 0 && p.ReactorOnline && p.ReactorRamp >= 1f && p.BatteryPercent > 0.5f && drain < 0.1f)
        {
            // 다시 켜기: 여유가 그 설비만큼 있으면 순위 높은 것부터 하나씩 (기동 전류도 줄인다)
            var back = w.Ship.Machines.Where(m => _parked.Contains(m.Body.Id) && !FixBook.WorkLock(w, m) && a.SelfWatch.Allow(CmdTarget.Machine, m.Body.Id, "다시 켬")).OrderByDescending(m => Rank(w, m)).ThenBy(m => m.Body.Id).FirstOrDefault();
            if (back == null) { if (!w.Ship.Machines.Any(m => _parked.Contains(m.Body.Id))) { _parked.Clear(); _parkOrder.Clear(); } }
            else if (p.ReactorLimit - p.Demand >= back.Spec.PowerDraw * Grades.Power(back.Grade) + 0.5f)
            {
                _parked.Remove(back.Body.Id);
                _parkOrder.Remove(back.Body.Id);
                Unparks++;
                a.Command.Line(CmdTarget.Machine, back.Body.Id, back.Body.Room, $"{back.Name} 다시 켬", "전기에 여유가 돌아왔다 — 하나씩", 0.3f, 10f);
            }
            if (_parked.Count == 0) { Mode = "평시"; Plan = ""; w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 전기에 여유가 생겨 꺼 두었던 설비를 모두 다시 켰습니다"); }
        }
        else if (!crisis && _parked.Count == 0) { Mode = "평시"; _power = null; _decidedAt = -1; Plan = ""; _lowAnnounced = -1; }
        // 저출력 운영을 풀기: 컴퓨터가 건 것은 원자로가 넉넉히 돌면 컴퓨터가 푼다 (사람이 건 것은 사람이)
        if (_ownBrownout && p.Brownout && p.ReactorOnline && p.ReactorRamp >= 1f && p.SurplusSince >= 0 && w.Tick - p.SurplusSince > SimTime.Hours(1))
        {
            p.Brownout = false;
            _ownBrownout = false;
            a.Command.Line(CmdTarget.Circuit, -1, null, "저출력 운영 풂", "원자로가 다 켠 수요보다 넉넉하다", 0.4f, 10f);
            w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 원자로가 넉넉합니다 — 내려 두었던 설비를 다시 올립니다");
        }
        if (!p.Brownout) _ownBrownout = false;
        if (!crisis && !p.AuxRunning) { _auxTries = 0; AuxNeedsHands = false; }

        // 원자로 출력: 노심이 달아오르면 낮춘다 (긴급 정지 전에) · 식으면 되돌린다
        if (p.ReactorOnline && !p.LowPowerMode && p.ReactorTemperature > PowerGrid.OverheatWarnC - 25f && ReactorCap > 0.6f)
        {
            ReactorCap = MathF.Max(0.6f, ReactorCap - 0.1f);
            Trims++;
            a.Command.Line(CmdTarget.Reactor, p.Reactor?.Body.Id ?? -1, p.Reactor?.Body.Room, $"원자로 출력 {ReactorCap * 100:0}%로", $"노심 {p.ReactorTemperature:0}℃ — 긴급 정지 전에 낮춘다", 0.8f, 30f);
            a.Book.Add(ActKind.Shed, p.Reactor?.Body.Room, $"노심 {p.ReactorTemperature:0}℃ (경고 {PowerGrid.OverheatWarnC:0}℃)", "냉각이 출력을 못 따라간다 — 긴급 정지면 몇 시간 못 켠다", $"원자로 출력 상한 {ReactorCap * 100:0}%", "냉각 계통을 본다", "rtrim", SimTime.Minutes(15), 30f,
                (world, act) => (world.Power.ReactorOnline ? 1 : -1, world.Power.ReactorOnline ? "맞았다 — 긴급 정지 없이 버텼다" : "틀렸다 — 그래도 정지했다"));
        }
        else if (ReactorCap < 1f && (!p.ReactorOnline || p.ReactorTemperature < PowerGrid.OverheatWarnC - 60f))
            ReactorCap = MathF.Min(1f, ReactorCap + 0.1f);

        // 원자로 재기동: 냉각이 돌아왔으면 기관사에게 (제어봉은 손으로)
        if (!p.ReactorOnline && a.MainOnline && (_restartAsked < 0 || w.Tick - _restartAsked > SimTime.Minutes(30))
            && w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.RestartReactor && o.Assignee == null) is WorkOrder ro && a.CrewModel.Best(Skill.Engineering) is CrewMember eng)
        {
            _restartAsked = w.Tick;
            RestartAsks++;
            a.Command.Assign(eng, ro, $"냉각 {p.CoolingCapacity:0}kW 확보 — 원자로 재기동 (제어봉 구동은 손으로)", crisis: true);
        }

        // 예비 배선: 배전반 회로가 단락 · 부품 고장으로 죽었다 → 살아 있는 회로에서 원격으로 넣는다 (보조 간선)
        if (a.MainOnline && w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault() is Furniture panel && !panel.Room.Detached && panel.Room.DataLinked && panel.Machine is Machine pm)
            for (int i = 0; i < PowerGrid.CircuitCount; i++)
            {
                if (p.CircuitLive[i] || p.ManualOff[i] || p.FeedingJumper(i) != null || Feeders >= 3) continue;
                if (!pm.Faults.Any(f => f.Circuit == i && f.Kind != FaultKind.BreakerTrip)) continue;
                if (MoistureSystem.Depth(panel.Room) > 0.03f) // 젖은 배전반에 예비 배선을 물리면 또 단락 — 손으로 말리고 고친 뒤에
                {
                    a.Book.Add(ActKind.Breaker, panel.Room, $"{PowerGrid.CircuitName(i)} 회로가 죽었다 · {panel.Room.Name} 바닥에 물", "젖은 배전반에 예비 배선을 물리면 또 단락이 난다", "예비 배선을 넣지 않고 둔다", "물을 퍼내고 배전반을 말려 달라", "feedwet:" + i, SimTime.Hours(1), 30f);
                    continue;
                }
                int from = p.JumperSource(i);
                if (from < 0) continue;
                var cell = panel.Cells.FirstOrDefault();
                p.AddJumper(from, i, cell, 0.7f, permanent: true);
                Feeders++;
                a.Command.Line(CmdTarget.Circuit, i, panel.Room, $"예비 배선 {PowerGrid.CircuitName(from)}→{PowerGrid.CircuitName(i)} 투입", $"{PowerGrid.CircuitName(i)} 회로가 죽었다 — 고칠 때까지 예비 배선으로", 0.8f, 60f);
                a.Book.Add(ActKind.Breaker, panel.Room, $"{PowerGrid.CircuitName(i)} 회로 정전 ({pm.Faults.First(f => f.Circuit == i).Name})", $"예비 배선은 {PowerGrid.JumperCapacityKw * 0.7f:0}kW까지 — 넘치면 급하지 않은 것부터 끈다",
                    $"예비 배선 {PowerGrid.CircuitName(from)}→{Ko.EulReul(PowerGrid.CircuitName(i))} 넣었다", "전기 담당은 회로를 고쳐 달라", "feed:" + i, SimTime.Hours(1), 20f);
            }
    }

    private void StartAux(Furniture auxF, AutomationSystem a)
    {
        var w = _w;
        var p = w.Power;
        if (w.Tick < _auxNext || p.AuxRunning) return;
        _auxNext = w.Tick + SimTime.Minutes(2);
        float fail = Math.Clamp(0.1f + 0.4f * (1f - auxF.Machine!.Condition) + 0.25f * auxF.Machine.Faults.Count, 0f, 0.9f);
        if (R.Chance(fail))
        {
            _auxTries++;
            AuxFails++;
            w.Log.Add(w.Tick, LogKind.Warning, $"{a.Voice.Call}: 보조 발전기 시동이 걸리지 않습니다 ({_auxTries}번째)" + (_auxTries >= 2 ? " — 손으로 당겨 주십시오" : ""));
            a.Command.Line(CmdTarget.Generator, auxF.Id, auxF.Room, "보조 발전기 시동", "안 걸렸다 — 오래 쉬던 기계", 0.9f, 2f, _power?.Id ?? -1, -1, "실패");
            if (_auxTries >= 2)
            {
                AuxNeedsHands = true;
                AuxHandsAsked++;
                w.Board.RequestScan();
            }
            return;
        }
        p.StartAux();
        AuxStarts++;
        a.Command.Line(CmdTarget.Generator, auxF.Id, auxF.Room, "보조 발전기 시동", $"A 회로(생명유지 쪽)에 {p.AuxRated:0}kW · 연료 {p.AuxFuel:0}시간분", 0.9f, 10f, _power?.Id ?? -1);
        a.Book.Add(ActKind.Breaker, auxF.Room, $"배터리 {p.BatteryPercent * 100:0}% · 원자로 {(p.ReactorOnline ? $"{p.ReactorLimit:0}kW" : "정지")}", "사람이 가서 당기기 전에 여기서 켠다", "보조 발전기를 켰다", "연료통을 챙겨 달라", "auxstart", SimTime.Minutes(30), 30f,
            (world, act) => (world.Power.AuxRunning || world.Power.ReactorOnline ? 1 : -1, world.Power.AuxRunning ? "맞았다 — 돌고 있다" : world.Power.ReactorOnline ? "맞았다 — 원자로가 돌아왔다" : "틀렸다 — 멈췄다"));
    }

    // ───────────── 차단기: 원인을 먼저 끊고 원격으로 올린다 ─────────────

    private void Breakers()
    {
        var w = _w;
        var a = w.Automation;
        var panelF = w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault();
        if (panelF?.Machine is not Machine pm || panelF.Room.Detached) return;
        bool remote = panelF.Room.DataLinked;
        foreach (var c in Cases) if (c.Open && !pm.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == c.Circuit)) { c.Open = false; if (c.State is "보류" or "사람에게") c.State = "사람이 올렸다"; }
        foreach (var fault in pm.Faults.Where(f => f.Kind == FaultKind.BreakerTrip).ToList())
        {
            int i = fault.Circuit;
            if (i < 0 || i >= PowerGrid.CircuitCount) continue;
            var bc = Cases.LastOrDefault(c => c.Open && c.Circuit == i);
            if (bc == null)
            {
                var an = Analyze(i); // 떨어진 순간의 원인 (지문 — 같은 원인을 알아본다)
                bc = new BreakerCase { Circuit = i, Since = fault.Since, Cause = an.cause, Sig = an.sig, Kind = an.cutKind };
                Cases.Add(bc);
                if (Cases.Count > 40) Cases.RemoveAt(0);
            }
            string cause = bc.Cause, sig = bc.Sig, cutKind = bc.Kind;
            if (!a.SelfWatch.Allow(CmdTarget.Breaker, i, "차단기 올림")) { if (bc.State != "멈춤") { bc.State = "멈춤"; Holds++; } continue; } // v16.26 같은 차단기를 거듭 올렸다 — 스스로 멈추고 사람에게
            foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.ResetBreaker && o.Circuit == i && o.Assignee == null).ToList()) w.Board.Close(o); // 원인을 볼 동안 사람은 올리러 가지 않는다
            if (!remote) { bc.State = "사람에게 (배전반 데이터선이 끊겼다)"; continue; }
            if (w.Tick - fault.Since < SimTime.Minutes(0.6f) / a.Core.Speed) continue; // 원인을 본다
            bool Same((string sig, long tick) r) => r.sig == sig && w.Tick - r.tick < SimTime.Hours(2) && (sig != "?" || w.Tick - r.tick < SimTime.Minutes(30));
            bool same = _lastReset.TryGetValue(i, out var last) && Same(last) || sig.StartsWith("p:") && _prevReset.TryGetValue(i, out var prev) && Same(prev); // 그 앞의 올림은 꽂아 둔 장비 탓일 때만 (사람 손으로 뺄 수 있는 것)
            bc.Same = same;
            if (bc.Decision < 0)
            {
                float retripNow = cutKind == "portable" ? 0.95f : cutKind == "water" ? 0.9f : 0.2f;
                var d = a.Foresee.Breaker(i, cause, cutKind != "", retripNow, cutKind != "" ? 0.05f : 0.2f, same);
                bc.Decision = d.Id;
                d.Grader = (world, dd) => Grade(world, bc);
                d.GradeAt = w.Tick + SimTime.Minutes(30);
            }
            var dec = a.Foresee.Timeline.FirstOrDefault(x => x.Id == bc.Decision);
            string key = dec?.Pick.Key ?? "hands";
            if (key == "hands")
            {
                if (bc.State != "보류")
                {
                    bc.State = "보류";
                    Holds++;
                    if (same) SameCauseHolds++;
                    Instruct(bc, panelF.Room);
                }
                // 사람이 원인을 뺐으면(같은 원인이 사라졌으면) 그때 원격으로 올린다 · 모르는 원인은 사람이 보고 올린다
                if (cutKind != "" && Analyze(i).cutKind == "") Reset(pm, panelF.Room, bc, "사람이 원인을 뺐다 — 이제 올린다");
                continue;
            }
            if (key == "cut" && bc.State == "분석")
            {
                CutCause(i, cutKind, bc, panelF.Room);
                bc.State = "원인 끊음";
            }
            Reset(pm, panelF.Room, bc, key == "cut" ? "원인부터 끊었다" : "다시 떨어질 이유가 안 보인다");
        }
    }

    /// <summary>원인 분석: 이동식 장비 과부하 · 바닥 물 · 모름.</summary>
    private (string cause, string sig, string cutKind) Analyze(int circuit)
    {
        var w = _w;
        float proj = w.Portable.ProjectedKw(circuit);
        if (proj > PortableSystem.OutletCapKw)
        {
            var ids = w.Portable.Devices.Where(d => d.Placed && d.On && !d.Broken && d.Plug == PortablePlug.Outlet && d.Outlet?.Circuit == circuit).Select(d => d.Id).OrderBy(x => x);
            var names = w.Portable.Devices.Where(d => d.Placed && d.On && !d.Broken && d.Plug == PortablePlug.Outlet && d.Outlet?.Circuit == circuit).GroupBy(d => d.Name).OrderBy(g => g.Key).Select(g => g.Count() > 1 ? $"{g.Key} {g.Count()}대" : g.Key);
            string where = w.Portable.OutletRoom(circuit)?.Name ?? "?";
            return ($"{where} 쪽 콘센트에 {string.Join(" · ", names)} ({proj:0.#}kW — {PortableSystem.OutletCapKw:0.#}kW까지 견딘다)", "p:" + string.Join(",", ids), "portable");
        }
        foreach (var r in w.Ship.LiveRooms)
            if (r.Circuit == circuit && !r.BreakerOff && MoistureSystem.Depth(r) > 0.1f)
                return ($"{r.Name} 바닥에 물 (누전)", "w:" + r.Id, "water");
        if (_lastReset.TryGetValue(circuit, out var lr) && w.Tick - lr.tick < SimTime.Minutes(3))
            return ("방금 올린 회로에 설비가 한꺼번에 켜졌다 (기동 전류)", "i", "");
        return ("이유를 모른다 (한꺼번에 켜진 설비 · 순간 과부하?)", "?", "");
    }

    /// <summary>원인을 원격으로 끊는다: 스마트 콘센트(값싼 장비부터) · 물 찬 방 분전함.</summary>
    private void CutCause(int circuit, string kind, BreakerCase bc, Room panelRoom)
    {
        var w = _w;
        var a = w.Automation;
        if (kind == "portable")
        {
            var cut = w.Portable.RemoteCut(circuit, PortableSystem.OutletCapKw, $"{PowerGrid.CircuitName(circuit)} 회로에 너무 많이 꽂혔다");
            foreach (var d in cut)
            {
                bc.Cut.Add(d.Name);
                a.Command.Line(CmdTarget.Outlet, d.Id, w.Portable.RoomOf(d), $"{w.Portable.RoomOf(d)?.Name ?? "?"} 콘센트 끊음 ({d.Name})", "그대로 두면 차단기가 또 떨어진다", 0.7f, 10f, bc.Decision);
            }
            CauseCuts += cut.Count;
        }
        else if (kind == "water")
        {
            foreach (var r in w.Ship.LiveRooms.Where(r => r.Circuit == circuit && !r.BreakerOff && MoistureSystem.Depth(r) > 0.1f).ToList())
            {
                w.Moisture.Isolate(r, null);
                bc.Cut.Add($"{r.Name} 분전함");
                CauseCuts++;
                a.Command.Line(CmdTarget.Room, r.Id, r, $"{r.Name} 분전함 내림", "바닥에 물 — 누전되는 방을 떼어 낸다", 0.8f, 30f, bc.Decision);
            }
        }
    }

    private void Reset(Machine pm, Room panelRoom, BreakerCase bc, string why)
    {
        var w = _w;
        var a = w.Automation;
        int i = bc.Circuit;
        int n = pm.Faults.RemoveAll(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == i);
        if (n == 0) return;
        foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.ResetBreaker && o.Circuit == i).ToList()) w.Board.Close(o);
        w.Board.RequestScan();
        bc.State = "올림";
        bc.Open = false;
        RemoteResets++;
        a.Command.Close(bc.Decision, why);
        if (_lastReset.TryGetValue(i, out var was) && was.sig != bc.Sig) _prevReset[i] = was;
        _lastReset[i] = (bc.Sig, w.Tick);
        string cut = bc.Cut.Count > 0 ? string.Join(" · ", bc.Cut.GroupBy(x => x).OrderBy(g => g.Key).Select(g => g.Count() > 1 ? $"{g.Key} {g.Count()}대" : $"{g.Key} 하나")) : "";
        a.Command.Line(CmdTarget.Breaker, i, panelRoom, $"{PowerGrid.CircuitName(i)} 회로 차단기 올림", why + (cut != "" ? $" · 끊은 것: {cut}" : ""), 0.9f, 5f, bc.Decision);
        a.Book.Add(ActKind.Breaker, panelRoom, $"{PowerGrid.CircuitName(i)} 회로 차단기가 떨어졌다 — {bc.Cause}", why, cut != "" ? $"{Ko.EulReul(cut)} 끊고 차단기를 올렸다" : "차단기를 올렸다", "", "breset:" + i + ":" + w.Tick, 0, 30f);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {bc.Cause} — " + (cut != "" ? $"{Ko.EulReul(cut)} 끊은 뒤 {PowerGrid.CircuitName(i)} 회로 차단기를 올렸습니다" : $"{PowerGrid.CircuitName(i)} 회로 차단기를 올렸습니다 ({why})"));
        MarkLog.Add(pm.Marks, w.Tick, $"{PowerGrid.CircuitName(i)} 회로 차단기 — 주 컴퓨터가 올림 ({bc.Cause})");
    }

    /// <summary>같은 원인 · 손이 필요한 원인: 다시 올리지 않고 사람에게 정확히 말한다.</summary>
    private void Instruct(BreakerCase bc, Room panelRoom)
    {
        var w = _w;
        var a = w.Automation;
        string what = bc.Sig.StartsWith("p:") ? $"{PowerGrid.CircuitName(bc.Circuit)} 회로 콘센트에서 장비를 하나 빼거나 다른 방 콘센트로 옮겨 주십시오" : bc.Sig.StartsWith("w:") ? "바닥 물을 퍼내고 말려 주십시오" : "배전반을 직접 봐 주십시오";
        string why = bc.Same ? $"같은 이유로 또 떨어졌습니다 — 이번엔 올리지 않겠습니다 ({bc.Cause})" : $"여기서 끊을 수 없는 원인입니다 ({bc.Cause})";
        a.Book.Add(ActKind.Breaker, panelRoom, $"{PowerGrid.CircuitName(bc.Circuit)} 회로 차단기가 떨어졌다 — {bc.Cause}", why, "차단기를 올리지 않고 둔다", what, "bhold:" + bc.Circuit, SimTime.Minutes(30), 30f);
        a.Speak.Announce(a.Voice.Style($"{PowerGrid.CircuitName(bc.Circuit)} 회로 — {why}. {what}"), null, 1);
        // 원인이 있는 곳에 가장 빨리 닿을 사람 하나를 콕 집어 단말로 (전기를 아는 사람이면 조금 더 멀어도) — 여럿에게 뿌리지 않는다
        Room target = bc.Sig.StartsWith("p:") ? w.Portable.OutletRoom(bc.Circuit) ?? panelRoom
                    : bc.Sig.StartsWith("w:") && int.TryParse(bc.Sig[2..], out int rid) && rid >= 0 && rid < w.Ship.Rooms.Count ? w.Ship.Rooms[rid] : panelRoom;
        var tc = target.Cells.Count > 0 ? target.Cells[target.Cells.Count / 2] : panelRoom.Cells.FirstOrDefault();
        CrewMember? who = null;
        float best = float.MaxValue;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.CanAct || c.Away || !c.IsAwake || c.Outside || c.Job?.Urgent == true) continue;
            if (c.Job?.Activity is SleepActivity || c.Needs.Rest < 0.2f) continue; // v16.26 자러 가는 · 녹초인 사람은 부탁을 받아도 자러 간다 (부탁받은 사람이 잠들어 한 시간을 묶였다)
            var cc = c.Cell;
            float d = Math.Abs(cc.X - tc.X) + Math.Abs(cc.Y - tc.Y) - 6f * c.SkillLevel(Skill.Electrical);
            if (d < best || d == best && who != null && c.Id < who.Id) { best = d; who = c; }
        }
        if (who != null)
        {
            if (bc.Sig.StartsWith("p:")) w.Portable.AskUnplug(bc.Circuit, who);
            // 컴퓨터를 믿는 사람은 하던 (급하지 않은) 일을 내려놓고 바로 간다 — 못 믿으면 하던 일을 마치고
            if (who.Job != null && who.Job.Urgent != true && a.Trusts.Of(who) >= 0.35f) who.EndJob(w, ToilStatus.Interrupted);
            w.Automation.Apps.Messages.Add(new PersonalMessage(w.Tick, who.Id, "지시", $"{target.Name} · {PowerGrid.CircuitName(bc.Circuit)} 회로: {what} — {why}"));
            a.Command.Line(CmdTarget.Crew, who.Id, target, $"{who.Name}: {target.Name} — {what}", why, 0.8f, 60f, bc.Decision, -1, "보냄");
        }
    }

    private (int, string)? Grade(World w, BreakerCase bc)
    {
        bool tripped = w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine m && m.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == bc.Circuit);
        bool again = Cases.Any(c => c != bc && c.Circuit == bc.Circuit && c.Since > bc.Since && c.Sig == bc.Sig);
        if (bc.State == "올림") return again ? (-1, "틀렸다 — 같은 원인으로 다시 떨어졌다") : (1, tripped ? "올렸지만 다른 원인으로 다시 떨어졌다" : "맞았다 — 다시 안 떨어졌다");
        if (bc.State is "보류") return tripped ? (2, "아직 사람이 원인을 빼지 않았다") : (1, "맞았다 — 원인을 뺀 뒤에 올라갔다");
        return (2, bc.State);
    }

    /// <summary>방금(2분 안) 원격으로 올린 회로 — 설비를 차례로 켜는 중.</summary>
    public bool Staging(int circuit) => _lastReset.TryGetValue(circuit, out var l) && _w.Tick - l.tick < SimTime.Minutes(2);

    /// <summary>WorkBoard · 사람 계획 훅: 컴퓨터가 그 차단기를 붙잡고 있나 (원인을 보는 중 · 같은 원인 보류). 붙잡으면 사람은 올리지 않는다.</summary>
    public string? Holding(int circuit)
    {
        var a = _w.Automation;
        if (!a.MainOnline || a.Level < 4) return null;
        var bc = Cases.LastOrDefault(c => c.Open && c.Circuit == circuit);
        if (bc == null) return null;
        return bc.State switch
        {
            "분석" or "원인 끊음" => "주 컴퓨터가 원인을 보고 올린다고 한다",
            "보류" when bc.Kind != "" => $"같은 이유로 또 떨어졌다 — 원인부터 빼야 한다 ({bc.Cause})",
            _ => null,
        };
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(_parked.Count); F(ReactorCap); I(RemoteResets); I(CauseCuts); I(Holds); I(AuxStarts); I(AuxFails); I(Parks); I(Unparks); I(Feeders); I(Trims); I(Cases.Count); I(AuxNeedsHands ? 1 : 0);
    }
}

public sealed partial class PortableSystem
{
    private readonly float[] _chargeRoom = new float[PowerGrid.CircuitCount];
    public int ChargeWaits;

    /// <summary>v16.20 똑똑한 충전: 주 컴퓨터가 배전반을 볼 때 회로마다 충전에 쓸 여유(콘센트 한도 − 꽂힌 장비)를 잰다.</summary>
    private bool SmartChargeBudget()
    {
        var a = _w.Automation;
        if (!a.Present || AutomationSystem.Ship20Off || !a.MainOnline || a.Level < 4) return false;
        for (int i = 0; i < PowerGrid.CircuitCount; i++) _chargeRoom[i] = OutletCapKw * 0.95f - ProjectedKw(i);
        return true;
    }

    /// <summary>이 방 충전기가 지금 채워도 되나 (여유가 남은 만큼 차례로 · 데이터선이 끊긴 방은 컴퓨터가 못 막는다).</summary>
    private bool ChargeSlot(Room room)
    {
        if (!room.DataLinked) return true;
        if (_chargeRoom[room.Circuit] < ChargeKw) { ChargeWaits++; return false; }
        _chargeRoom[room.Circuit] -= ChargeKw;
        return true;
    }

    /// <summary>v16.20 스마트 콘센트: 주 컴퓨터가 그 회로 콘센트를 원격으로 끊는다 (값싼 것부터 · 양수기는 마지막) — 그 회로가 목표 아래로 내려갈 때까지.</summary>
    internal List<PortableDevice> RemoteCut(int circuit, float targetKw, string why)
    {
        var w = _w;
        var cut = new List<PortableDevice>();
        int Value(PortableKind k) => k switch { PortableKind.Heater => 0, PortableKind.Purifier => 1, PortableKind.Fan => 2, PortableKind.WorkLamp => 3, PortableKind.Battery => 4, PortableKind.Pump => 5, _ => 6 };
        foreach (var d in Devices.Where(d => d.Placed && d.On && !d.Broken && d.Plug == PortablePlug.Outlet && d.Outlet?.Circuit == circuit)
                     .OrderBy(d => Value(d.Kind)).ThenByDescending(d => d.PlacedSince).ThenBy(d => d.Id).ToList())
        {
            if (ProjectedKw(circuit) <= targetKw) break;
            d.On = false;
            d.Running = false;
            d.LightIntensity = 0f;
            cut.Add(d);
            var r = RoomOf(d);
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터가 {r?.Name ?? "?"} 콘센트 하나를 끊었다 — {d.Name} ({why})");
            if (r != null) MarkLog.Add(r.Marks, w.Tick, $"주 컴퓨터가 {d.Name} 콘센트를 끊음 ({why})");
        }
        return cut;
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v16.20 컴퓨터가 원격으로 못 하는 일만 손에게: 원격 시동이 두 번 안 걸린 보조 발전기.</summary>
    private void ScanComputer(Poster post)
    {
        ScanProbe(post); // v16.26 ② 사람이 가서 보기
        ScanSelf(post); // v16.26 ⑥ 센서가 안 닿는 방 순찰
        var w = _world;
        var tr = w.Automation.TriageOrNull;
        if (tr == null || !tr.AuxNeedsHands || w.Power.AuxRunning || w.Power.AuxFuel <= 0.1f) return;
        if (w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault(f => !f.Room.Detached) is Furniture aux && !aux.Machine!.Stopped)
            post(WorkKind.StartAux, WorkTarget.Of(aux), 1.12f, Skill.Electrical, $"주 컴퓨터: 시동이 두 번 안 걸렸다 — 손으로 당겨 달라 (배터리 {w.Power.BatteryPercent * 100:0}%)");
    }
}

public sealed partial class AutomationSystem
{
    private PowerTriage? _triage;
    /// <summary>v16.20 전력 트리아지 (몰아주기 · 원격 차단기 · 보조 발전기 · 원자로 · 예비 배선).</summary>
    public PowerTriage Triage => _triage ??= new PowerTriage(_world);
    internal PowerTriage? TriageOrNull => _triage;
    /// <summary>Power 훅: 원자로 출력 상한 (트리아지).</summary>
    public float ReactorCap => (_triage?.ReactorCap ?? 1f) * (_fix?.ReactorCap ?? 1f); // v16.26 계획의 감출력
    /// <summary>Power.UpdateParking 훅.</summary>
    internal void Park() => _triage?.Park();
    /// <summary>Moisture 기동 전류 훅: 컴퓨터가 방금 원격으로 올린 회로는 설비를 큰 것부터 하나씩 켠다.</summary>
    public float InrushMul(int circuit) => _triage != null && _triage.Staging(circuit) ? 0.15f : 1f;

    private int _wireless;
    private long _wirelessNext;
    /// <summary>시험: v16.20을 끄고 견준다 (성능).</summary>
    public static bool Ship20Off { get; set; }

    /// <summary>v16.20 한 틱: 명령선 · 전력 트리아지 · 미리 돌려 보기 채점 · 성격 · 무선 방 수.</summary>
    private void Ship20(float dt)
    {
        var w = _world;
        if (!Present) return;
        Core.Flush();
        if (Ship20Off) return;
        Command.Update();
        if (CoreOnline) Triage.Update(dt);
        Foresee.Update();
        Character.Update();
        if (w.Tick >= _wirelessNext)
        {
            _wirelessNext = w.Tick + SimTime.Minutes(1);
            int n = 0;
            foreach (var r in w.Ship.LiveRooms) if (!r.DataLinked && Core.Reach(r) == 1) n++;
            _wireless = n;
        }
        // 손이 필요한 위기: 위험한 방에서 자는 사람은 단말로 깨운다 (감지기가 닿는 방 — 데이터선이나 무선)
        if (CoreOnline)
            foreach (var c in w.Crew)
                if (c.Pose == Pose.Sleeping && !c.Dead && c.Room is Room cr && Core.Reach(cr) > 0 && (cr.Leaking || w.Fire.IsKnown(cr) || cr.Air.CO > 0.08f || cr.Air.O2 < 16f))
                    Command.Wake(c, cr.Leaking ? $"{cr.Name} 공기가 샙니다 — 나가십시오" : w.Fire.IsKnown(cr) ? $"{cr.Name}에 불 — 나가십시오" : cr.Air.CO > 0.08f ? $"{cr.Name}에 일산화탄소 — 나가십시오" : $"{cr.Name} 공기가 나쁩니다 — 나가십시오", cr);
    }
}
