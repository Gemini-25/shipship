using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.20b 로봇 · 드론 두뇌와 성능 · 주컴퓨터 함대 지휘.
//  두뇌(로봇 · 드론 각자): 맡은 일을 단계로(부품 → 고치기 → 시험) · 배터리 계산(다녀올 몫 · 남은 일 · 비상이면 아껴 쓰기) ·
//   자기 보존(운석이 떨어질 방 · 새는 방 · 방사선 — 단계가 높으면 버틴다 · 불은 방열 외피만) · 서로 돕기(멈춘 로봇 끌고 오기 · 고치기 · 무거운 것 같이 들기) ·
//   막히면 다른 길 · 배우기(자주 고장 나는 방은 순찰을 먼저 · 잘 통한 방법) · 사람과 함께(곁에서 거들기 · 급히 지나가는 사람에게 길 비키기).
//  성능: 기술 그물의 로봇 줄기(협동 로봇 팔 · 드론 편대 · 배관 기어 로봇 · 작업 외골격 · 군집 수리 · 자기 복제)를 익힐수록 단계가 오른다 —
//   속도 · 배터리 · 외피(고장 · 피해) · 공구(작업 시간). 그림도 단계마다 달라진다 (View/ShipViewFleet.cs).
//  함대 지휘(주 컴퓨터): 사고 규모로 평시 · 경계 · 비상을 정하고, 불난 방엔 사람보다 소방 로봇을 먼저 · 외벽 파공엔 드론을 밖으로 ·
//   교대 충전 · 떠내려간 드론 건지기 — 판단은 견줘 보기(ComputerForesee) 타임라인에, 지시는 명령선(ComputerCommand)에 남긴다.
//  승무원: 로봇이 먼저 불 속에 들어가는 걸 본 사람은 컴퓨터를 더 믿고 · 로봇이 부서지면 같이 일하던 사람이 아쉬워한다.

/// <summary>로봇 · 드론 한 대의 생각 (화면 카드가 읽는다): 지금 · 다음 · 배터리 · 왜.</summary>
public sealed class UnitMind
{
    public string Now { get; internal set; } = "대기";
    public string Next { get; internal set; } = "";
    public string Why { get; internal set; } = "";
    public string Power { get; internal set; } = "";
    /// <summary>맡은 일의 단계 (부품 가져오기 → 고치기 → 시험 가동 …).</summary>
    public List<string> Stages { get; } = new();
    internal List<int> StageAt { get; } = new();
    public int Stage { get; internal set; }
    public long WhySince { get; internal set; } = -1;
    internal void Say(string why, long tick) { Why = why; WhySince = tick; }
}

public sealed partial class Robot
{
    public UnitMind Mind { get; } = new();
    /// <summary>부서졌다 (불 · 폭발) — 되살릴 수 없다.</summary>
    public bool Wrecked { get; internal set; }
    /// <summary>나를 끌고 가는 로봇.</summary>
    public Robot? TowBot { get; internal set; }
    /// <summary>내가 끌고 가는 로봇.</summary>
    public Robot? Hauling { get; internal set; }
    /// <summary>무거운 짐을 같이 드는 로봇.</summary>
    public Robot? Partner { get; internal set; }
    /// <summary>내가 고치러 가는 로봇.</summary>
    public Robot? Fixing { get; internal set; }
    /// <summary>길을 비켜 기다린 틱 (음수면 잠깐 비키지 않는다).</summary>
    public int YieldTicks { get; internal set; }
}

public sealed partial class Drone
{
    public UnitMind Mind { get; } = new();
    /// <summary>외벽 순찰: 밖에서 용접하러 가는 벽 (작업 목록에 오르기 전의 약해진 벽).</summary>
    public Cell? HullCare { get; internal set; }
}

public sealed class FleetSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7349 + 113));
    internal Rng Rng => R;

    /// <summary>시험용: 함대 지휘 · 두뇌 · 단계를 모두 끈다 (전/후 비교).</summary>
    public static bool Off;
    /// <summary>시험: 소방 로봇이 먼저 들어간 불에 문 앞 대기를 세우지 않는다 (구경꾼만 보는 장면 등).</summary>
    public static bool NoDoorGuard;

    public int Tier { get; private set; } = 1;
    /// <summary>평시 · 경계 · 비상 (사고 규모 · 위기 · 불 · 운석 경보).</summary>
    public string Mode { get; private set; } = "평시";
    public IncidentScale Scale { get; private set; }
    /// <summary>전기가 귀하다: 느리게 움직이고 아껴 쓴다.</summary>
    public bool Thrift { get; private set; }

    public int HullJobs, HullRounds, Tunes, FireFirst, Tows, Fixes, Seals, SealFails, Reliefs, Fetches, Reroutes, Yields, Lifts, Tests, TestFails, Waits, Retreats, Wrecks, Witnessed, Dodges, Mourned, Lines;
    /// <summary>방마다 로봇 고장 횟수 (자주 고장 나는 곳).</summary>
    public SortedDictionary<int, int> RoomFaults { get; } = new();
    /// <summary>방법마다 잘 됐나 · 안 됐나 (실링폼 · 금속판 · 로봇 먼저 · 정비 …).</summary>
    public SortedDictionary<string, (int ok, int fail)> Methods { get; } = new(StringComparer.Ordinal);
    /// <summary>드론 → 맡긴 일 (파공).</summary>
    internal SortedDictionary<int, int> DroneTask { get; } = new();
    /// <summary>드론 → 건질 드론 (견인 드론이 없을 때).</summary>
    internal SortedDictionary<int, int> FetchTask { get; } = new();
    /// <summary>일 → 앞 드론이 한 만큼 (교대).</summary>
    internal SortedDictionary<int, float> Carry { get; } = new();
    private readonly SortedDictionary<int, FireWatch> _fire = new();
    private readonly SortedDictionary<string, long> _decided = new(StringComparer.Ordinal);
    private readonly SortedDictionary<int, SortedDictionary<int, float>> _bond = new();
    private readonly SortedSet<int> _fetchSaid = new();
    private long _next;
    /// <summary>다음 외벽 순찰을 찾아볼 때.</summary>
    internal long NextHullRound, NextHullCare;

    private sealed class FireWatch { public int Robot; public long Since, HoldUntil; public bool Held, Seen; public int Decision; public float Foam0; public readonly HashSet<int> Saw = new(); public int Guard = -1, GuardTries; public Cell GuardSpot; }

    public FleetSystem(World w) => _w = w;

    // ───────────── 단계 (기술 그물) ─────────────

    public static readonly string[] TierTechs = { "cobotarm", "dronenet", "crawler", "exosuit", "swarmrepair", "selfreplicate" };
    private static readonly float[] SpeedT = { 1.1f, 1.2f, 1.3f, 1.42f }, DrainT = { 0.9f, 0.8f, 0.7f, 0.6f }, FaultT = { 0.8f, 0.65f, 0.5f, 0.4f },
        WorkT = { 0.92f, 0.85f, 0.78f, 0.7f }, HurtT = { 0.85f, 0.7f, 0.55f, 0.45f };
    public static string TierName(int t) => t switch { <= 1 => "표준형", 2 => "개량형", 3 => "강화형", _ => "최신형" };
    public static string ToolName(int t) => t switch { <= 1 => "보통 공구", 2 => "정밀 공구", 3 => "동력 공구", _ => "다관절 공구" };
    private int T => Math.Clamp(Tier, 1, 4) - 1;
    private static bool Plain => Off || Durability.Legacy;
    public string TierNote => $"{TierName(Tier)} · {ToolName(Tier)} · 속도 ×{SpeedT[T]:0.00} · 한 번 충전 ×{1f / DrainT[T]:0.0} · 고장 −{(1f - FaultT[T]) * 100:0}%";

    public float Speed(Robot r) => Plain ? 1f : SpeedT[T] * (Thrift ? 0.9f : 1f) * (r.Hauling != null ? 0.6f : HeavyAlone(r) ? 0.75f : 1f);
    public float Drain(Robot r) => Plain ? 1f : DrainT[T] * (Thrift ? 0.8f : 1f) * (r.Hauling != null ? 1.5f : 1f);
    public float FaultMul => Plain ? 1f : FaultT[T];
    public float Work => Plain ? 1f : WorkT[T];
    public float Hurt => Plain ? 1f : HurtT[T];
    public float DroneSpeed => Plain ? 1f : SpeedT[T];
    public float DroneDrain => Plain ? 1f : DrainT[T] * (Thrift ? 0.85f : 1f);

    internal static bool Heavy(ItemStack? c) => c is ItemStack s && (s.Kind is ItemKind.Plate or ItemKind.Structure && s.Count >= 2 || s.Kind is ItemKind.Motor or ItemKind.Pump);
    private static bool HeavyAlone(Robot r) => Heavy(r.Cargo) && (r.Partner is not Robot p || (p.Position - r.Position).LengthSquared() > 1.6f);

    // ───────────── 매 분 ─────────────

    public void Update(float dt)
    {
        var w = _w;
        if (Off) return;
        foreach (var r in w.Robots.Robots) if (r.Fixing != null || r.Hauling != null) w.Robots.LinkJob(r); // 사람이 같은 일을 잡지 않게 (매 틱 · 그런 로봇이 있을 때만)
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(1);
        Retier();
        Assess();
        bool cmd = w.Automation.Present && (w.Automation.MainOnline || w.Automation.Core.BackupCore);
        w.Robots.FleetTick(this, cmd);
        w.Drones.FleetTick(this, cmd);
        if (cmd) { Fires(); Breaches(); }
        else ReleaseFires("관제가 끊겨 사람이 들어간다");
        Minds(cmd);
    }

    private void Retier()
    {
        var w = _w;
        int n = 0;
        foreach (var id in TierTechs) if (w.Eras.Has(id)) n++;
        int t = 1 + Math.Min(3, n);
        if (t == Tier) return;
        bool up = t > Tier;
        Tier = t;
        if (!up) return;
        int bots = w.Robots.Robots.Count(r => r.State != RobotState.Lost), flyers = w.Drones.Drones.Count(d => d.State != DroneState.Lost);
        w.History.Add(w, HistoryKind.Upgrade, $"정비실에서 로봇 {bots}대 · 드론 {flyers}대를 {TierName(t)}으로 고쳐 달았다 — 외피 · 셀 · {ToolName(t)}", null, log: true);
    }

    private void Assess()
    {
        var w = _w;
        var top = IncidentScale.Personal;
        foreach (var k in w.Scale.Cases) if (k.Open && k.Now > top) top = k.Now;
        Scale = top;
        var lvl = Crisis.Level(w);
        Mode = top >= IncidentScale.Ship || lvl >= CrisisLevel.Survival ? "비상"
            : top >= IncidentScale.System || lvl >= CrisisLevel.Emergency || w.Fire.Count > 0 || w.Sensors.Alarm != null ? "경계" : "평시";
        Thrift = Mode == "비상" || w.Power.BatteryPercent < 0.25f && !w.Power.ReactorOnline;
    }

    /// <summary>비상 때는 급하지 않은 일을 미룬다 (배터리 · 손을 사고 쪽에 남긴다). 컴퓨터가 시킨 일은 한다.</summary>
    public bool Allowed(Robot r, WorkOrder o) => Off || Mode != "비상" || o.Urgency >= 0.6f || _w.Automation.Command.Bias(r, o) > 0f;

    // ───────────── 자기 보존 ─────────────

    /// <summary>이 방에 들어가도 되나 (불은 따로 — 방열 외피만).</summary>
    public (bool ok, string what) Judge(Robot r, Room room)
    {
        if (Off) return (true, "");
        var w = _w;
        if (w.Sensors.Threat(room) != null) return (false, "운석이 곧 떨어질 방");
        if (room.Leaking && room.Air.Pressure > 15f && Tier < 2) return (false, "공기가 새어 나가는 방 — 바람에 휩쓸린다");
        float rad = MathF.Max(room.Radiation, w.Cosmic.Radiation(room));
        if (rad > 0.6f && Tier < 3) return (false, "방사선 — 회로가 버티지 못한다");
        return (true, "");
    }

    public bool MayEnter(Robot r, Room room)
    {
        var (ok, what) = Judge(r, room);
        if (!ok && r.Mind.WhySince < _w.Tick - SimTime.Minutes(5)) r.Mind.Say($"{room.Name}의 일은 미룬다 — {what}", _w.Tick);
        return ok;
    }

    // ───────────── 배터리 ─────────────

    /// <summary>다녀올 몫 + 남은 일 몫이 있어야 나간다 (아주 급한 일이면 조금 모자라도).</summary>
    internal bool Affords(Robot r, WorkOrder o, List<RobotStep> steps, Cell spot)
    {
        if (Plain) return true;
        float hours = 0f;
        foreach (var s in steps) if (s is RWork rw) hours += rw.Hours;
        hours *= RobotSystem.WorkFactor(r.Kind) * Work;
        var c = spot.Center;
        float there = (MathF.Abs(c.X - r.Position.X) + MathF.Abs(c.Y - r.Position.Y)) * 1.4f + 4f;
        float back = (MathF.Abs(c.X - r.DockPosition.X) + MathF.Abs(c.Y - r.DockPosition.Y)) * 1.4f + 4f;
        float sp = RobotSystem.Speed(r.Kind) * r.Quirk.Speed * SimTime.TicksPerHour * Speed(r);
        float dm = RobotsV15.Drain(r.Kind) * Durability.RobotDrain * Drain(r);
        float need = ((there + back) / sp * RobotSystem.DrainMove + hours * RobotSystem.DrainWork) * dm + 0.06f;
        r.Mind.Power = $"이 일에 {need * 100:0}% · 지금 {r.Battery * 100:0}%";
        // 충전 시점: 급하지 않은 일이면 거치대에서 반은 채우고 나간다 (바닥 근처로 오가며 일하면 멈춰 서기 쉽다)
        if (r.AtDock && o.Urgency < 0.6f && r.Battery < 0.5f && Mode == "평시")
        {
            Waits++;
            r.Doing = $"충전 {r.Battery * 100:0}% — 급하지 않은 일이라 반은 채우고 나간다";
            if (r.Mind.WhySince < _w.Tick - SimTime.Minutes(10)) r.Mind.Say($"{Ko.EunNeun(o.Title)} 급하지 않다 — 반은 채우고 나간다", _w.Tick);
            return false;
        }
        if (r.Battery >= need || o.Urgency >= 1.1f && r.Battery >= need * 0.7f) return true;
        if (need > 0.9f && r.Battery >= 0.9f)
        {
            r.Mind.Power = $"이 일에 {need * 100:0}% — 한 번 충전으로는 못 끝낸다 · 하다가 교대";
            return true; // 긴 일: 가득 채웠으면 나가서 하다가 돌아갈 몫이 남으면 교대한다
        }
        Waits++;
        r.Mind.Say($"{o.Title} — 다녀오는 데 {need * 100:0}%가 드는데 {r.Battery * 100:0}%뿐이라 더 채우고 나간다", _w.Tick);
        if (r.AtDock) r.Doing = $"충전 {r.Battery * 100:0}% — {o.Title}에 {need * 100:0}% 필요";
        return false;
    }

    /// <summary>일을 골랐다: 왜 · 단계.</summary>
    internal void Chose(Robot r, WorkOrder o, List<RobotStep> steps)
    {
        if (Off) return;
        float bias = _w.Automation.Command.Bias(r, o);
        r.Mind.Say(bias > 0f ? $"주 컴퓨터가 맡겼다 — {CmdWhy(r) ?? o.Title}"
            : o.Urgency >= 1f ? $"지금 가장 급한 일 ({o.Title})" : "맡을 수 있는 일 중 가장 급하고 가까운 일", _w.Tick);
        Stage(r, steps, o);
    }

    private string? CmdWhy(Robot r)
    {
        var lines = _w.Automation.Command.Lines;
        for (int i = lines.Count - 1; i >= 0; i--)
            if (lines[i].Target == CmdTarget.Robot && lines[i].TargetId == r.Id && lines[i].Open) return lines[i].Why;
        return null;
    }

    /// <summary>단계 이름 (걷기는 다음 단계에 묶는다).</summary>
    internal static void Stage(Robot r, List<RobotStep> steps, WorkOrder? o)
    {
        var m = r.Mind;
        m.Stages.Clear();
        m.StageAt.Clear();
        for (int i = 0; i < steps.Count; i++)
        {
            string? name = steps[i] switch
            {
                RTake t => $"{ItemKinds.Name(t.Kind)} 가져오기",
                RWork => o?.Kind switch
                {
                    WorkKind.Maintain => "고치기", WorkKind.FixLights => "조명 갈기", WorkKind.Tend => "돌보기", WorkKind.Harvest => "거두기",
                    WorkKind.StowCot => "접어 두기", WorkKind.RefillPropellant => "채우기", null => r.Fixing is Robot fb ? fb.Fault == null ? "손보기" : "고치기" : "살펴보기", _ => "일하기",
                },
                RTest => "시험 가동",
                RSpray => "거품 뿌리기",
                RAssist => "곁에서 거들기",
                RHitch => "붙잡기",
                RWith => "같이 들기",
                RPut => "내려놓기",
                _ => null,
            };
            if (name == null && i == steps.Count - 1 && steps[i] is RGoto) name = r.Hauling != null || steps.Any(s => s is RHitch) ? "끌고 가기" : "가져다 두기";
            if (name == null && steps[i] is RDo && i > 0 && steps[i - 1] is RGoto && steps.Any(s => s is RHitch) && m.Stages.Count > 0 && m.Stages[^1] == "붙잡기") name = "충전대까지 끌고 가기";
            if (name == null) continue;
            m.Stages.Add(name);
            m.StageAt.Add(i);
        }
        m.Stage = 0;
    }

    // ───────────── 길 · 사람 ─────────────

    /// <summary>앞 칸에 급히 지나가는 사람이 있으면 잠깐 기다린다 (서 있는 사람은 비켜 지나간다 · 오래 막히면 지나간다).</summary>
    public bool GiveWay(Robot r, Cell next)
    {
        if (Off) return false;
        if (r.YieldTicks < 0) { r.YieldTicks++; return false; }
        if (r.YieldTicks >= 30 || r.Hauling != null) { r.YieldTicks = r.YieldTicks >= 30 ? -60 : 0; return false; }
        foreach (var c in _w.Crew)
        {
            if (c.Dead || !c.CanAct || c.Cell != next) continue;
            if (c.Position == c.PreviousPosition && c.Job?.Urgent != true) continue;
            if (r.YieldTicks == 0) { Yields++; r.Mind.Say($"{Ko.IGa(c.Name)} 먼저 지나가게 길을 비켰다", _w.Tick); }
            r.YieldTicks++;
            return true;
        }
        r.YieldTicks = 0;
        return false;
    }

    internal void Rerouted(Robot r, Cell at, Door? door)
    {
        if (Off) return;
        Reroutes++;
        var room = _w.Ship.RoomAt(at);
        r.Mind.Say(door != null && door.Locked ? $"{room?.Name ?? "앞"} 문이 잠겨 다른 길로 돌아간다" : $"{Ko.IGa(room?.Name ?? "앞")} 막혀 다른 길로 돌아간다", _w.Tick);
    }

    // ───────────── 배우기 ─────────────

    internal void Learn(Room? room, string what)
    {
        if (room == null) return;
        RoomFaults[room.Id] = RoomFaults.GetValueOrDefault(room.Id) + 1;
    }

    /// <summary>방마다 운석에 맞은 횟수 (드론 외벽 순찰이 자주 맞는 쪽을 먼저 본다).</summary>
    public SortedDictionary<int, int> Hits { get; } = new();

    internal void Struck(Cell entry)
    {
        if (Hull.InsideRoom(_w.Ship, entry) is Room room) Hits[room.Id] = Hits.GetValueOrDefault(room.Id) + 1;
    }

    /// <summary>순찰 순서에 얹는 무게: 고장이 잦은 방은 그만큼 오래 안 본 셈.</summary>
    public long Hot(Room room) => Off ? 0 : RoomFaults.TryGetValue(room.Id, out int n) ? Math.Min(n, 4) * SimTime.Hours(2) : 0;

    internal void Method(string key, bool ok)
    {
        var (a, b) = Methods.GetValueOrDefault(key);
        Methods[key] = ok ? (a + 1, b) : (a, b + 1);
    }

    /// <summary>잘 통한 정도 (모르면 반반).</summary>
    public float Rate(string key) => Methods.TryGetValue(key, out var m) ? (m.ok + 1f) / (m.ok + m.fail + 2f) : 0.5f;

    public string? Lesson(Room? room)
    {
        if (room != null && RoomFaults.TryGetValue(room.Id, out int n) && n >= 2) return $"{room.Name}에서 {n}번 고장 났다 — 순찰을 먼저";
        return null;
    }

    // ───────────── 불: 사람보다 소방 로봇을 먼저 ─────────────

    private void Fires()
    {
        var w = _w;
        if (w.Fire.Count == 0) { if (_fire.Count > 0) ReleaseFires(null); return; }
        var cells = new SortedDictionary<int, int>();
        foreach (var (c, _) in w.Fire.Fires)
            if (w.Ship.RoomAt(c) is Room rm && !rm.Detached && !rm.Abandoned) cells[rm.Id] = cells.GetValueOrDefault(rm.Id) + 1;
        foreach (var id in _fire.Keys.ToList())
            if (!cells.ContainsKey(id)) { Settle(id, true); _fire.Remove(id); }
        foreach (var (id, n) in cells)
        {
            var room = w.Ship.Rooms[id];
            if (_fire.TryGetValue(id, out var fw))
            {
                var rb = w.Robots.Robots[fw.Robot];
                // 들어갔거나 문턱에서 거품을 뿌리기 시작했다 — 통합8 그 뒤 문 앞에 닿은 사람도 로봇이 끄는 걸 본다 (사람마다 한 번)
                if (rb.Room == room || rb.Foam < fw.Foam0 - 0.01f) { fw.Seen = true; Witness(rb, room, fw); }
                if (fw.Held && (fw.Guard < 0 || w.Crew.FirstOrDefault(x => x.Id == fw.Guard) is not { CanAct: true, Outside: false })) { if (fw.Guard >= 0) EndGuard(fw, "문 앞을 지키던 사람이 움직일 수 없다"); if (fw.GuardTries < 2) PostGuard(room, fw); } // 문 앞 대기 사람이 쓰러지면 한 번 더 고른다
                bool on = rb.Operational && rb.Foam > 0.05f && (rb.FightingFire || rb.Room == room);
                if (!on)
                {
                    if (fw.Held) Hold(room, false);
                    Settle(id, false);
                    _fire.Remove(id);
                    _decided[$"fire:{id}"] = w.Tick;
                    w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: {Ko.IGa(rb.Name)} 물러났다 — {room.Name} 불은 사람 손으로");
                    continue;
                }
                if (fw.Held && w.Tick < fw.HoldUntil) Hold(room, true);
                else if (fw.Held) { fw.Held = false; Hold(room, false); EndGuard(fw, "기다리는 시간이 지났다 — 사람도 들어간다"); }
                continue;
            }
            if (_decided.TryGetValue($"fire:{id}", out long t0) && w.Tick - t0 < SimTime.Minutes(6)) continue;
            Decide(room, n);
        }
    }

    private void Decide(Room room, int cells)
    {
        var w = _w;
        _decided[$"fire:{room.Id}"] = w.Tick;
        Robot? best = null;
        float bestEta = float.MaxValue;
        foreach (var r in w.Robots.Robots)
        {
            if (!RobotsV15.Fights(r.Kind) || !r.Operational || r.Foam < 0.3f || r.Battery < 0.35f || r.Hauling != null || r.Wrecked) continue;
            if (r.FightingFire && !(r.Goal is Cell g && w.Ship.RoomAt(g) == room)) continue; // 다른 방 불에 가 있다
            float d = MathF.Abs(r.Position.X - room.Center.X) + MathF.Abs(r.Position.Y - room.Center.Y);
            float eta = d * 1.3f / (RobotSystem.Speed(r.Kind) * Speed(r) * SimTime.Minutes(1)) + 0.5f;
            if (eta < bestEta) { bestEta = eta; best = r; }
        }
        float crewEta = 6f;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.CanAct || !c.IsAwake || c.IsChild) continue;
            float d = MathF.Abs(c.Position.X - room.Center.X) + MathF.Abs(c.Position.Y - room.Center.Y);
            crewEta = MathF.Min(crewEta, d * 1.3f / (0.1f * SimTime.Minutes(1)) + 1f);
        }
        if (best == null && w.Robots.Robots.Any(r => RobotsV15.Fights(r.Kind) && r.State != RobotState.Lost))
            w.Automation.Book.Add(ActKind.Advice, room, $"{room.Name} 불 {cells}칸 · 나갈 수 있는 소방 로봇이 없다", "거품이 비었거나 멈췄거나 다른 불에 가 있다", "사람이 소화기로", "", $"fleet:nofire:{room.Id}", SimTime.Minutes(20), 10f);
        if (best == null) return;
        float smoke = room.Air.CO > 0.05f ? 1.5f : 1f;
        int inside = w.Automation.Belief.PeopleIn(room) ?? 0; // 컴퓨터가 안에 있다고 믿는 사람 (쓰러졌을 수도) — 그러면 사람을 문 앞에 세우지 않는다
        float crewRisk = MathF.Min(1.6f, 0.25f + 0.08f * cells) * smoke;
        float trust = Rate("fire:robot");
        var opts = new List<ForeseeOption>
        {
            new() { Key = "crew", Name = "사람이 소화기로", People = crewRisk, Ship = 0.01f, Minutes = crewEta, Note = $"{crewEta:0.#}분 뒤 · 불 {cells}칸" },
            new() { Key = "robot", Name = $"{best.Name} 먼저 — 사람은 문 앞에서", People = 0.03f, Ship = 0.015f * MathF.Max(0f, bestEta - crewEta) * (1.3f - 0.6f * trust) + (cells > 10 ? 0.25f : 0f),
                Minutes = bestEta, Note = $"{bestEta:0.#}분 뒤 · 거품 {best.Foam * 100:0}%", Allowed = cells <= 14 && inside == 0,
                Blocked = inside > 0 ? $"안에 {inside}명이 있다고 본다 — 사람도 같이 들어가 데리고 나와야 한다" : "로봇 한 대로 잡기엔 너무 크다" },
            new() { Key = "both", Name = "로봇과 사람이 함께", People = crewRisk * 0.55f, Ship = 0.01f, Minutes = MathF.Min(bestEta, crewEta), Note = "먼저 닿는 쪽부터" },
        };
        var dec = w.Automation.Foresee.Fleet("불", room, $"{room.Name} 불 {cells}칸 — 누가 먼저 들어가나", opts);
        if (dec.Pick.Key == "crew") return;
        if (!w.Robots.FleetFire(best, room)) return;
        bool hold = dec.Pick.Key == "robot";
        var fw = new FireWatch { Robot = best.Id, Since = w.Tick, Held = hold, HoldUntil = w.Tick + SimTime.Minutes(bestEta + 8f), Decision = dec.Id, Foam0 = best.Foam };
        _fire[room.Id] = fw;
        if (hold) { StandBack(room, best); Hold(room, true); PostGuard(room, fw); }
        FireFirst++;
        best.Mind.Say($"주 컴퓨터가 보냈다 — 사람보다 먼저 {room.Name} 불로 ({dec.Reason})", w.Tick);
        Line(CmdTarget.Robot, best.Id, room, $"{best.Name}: {room.Name} 불 — " + (hold ? "사람보다 먼저" : "사람과 함께"), dec.Reason, 0.95f, 20f, dec.Id);
        if (hold) w.Automation.Command.Line(CmdTarget.Broadcast, -1, room, $"{room.Name} — 소방 로봇이 먼저 들어간다 · 문 앞에서 기다려라", "사람이 연기 속에 들어가지 않게", 0.8f, 10f, dec.Id);
    }

    /// <summary>"문 앞에서 기다려라": 소화하러 가던 사람을 세운다 — 컴퓨터를 믿는 사람은 따르고, 못 믿는 사람은 그대로 들어간다.</summary>
    private void StandBack(Room room, Robot bot)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room == room || c.Job?.Order is not { Kind: WorkKind.Extinguish } eo || eo.Target.CurrentRoom != room) continue;
            if (w.Automation.Trusts.Of(c) >= 0.4f)
            {
                c.EndJob(w, ToilStatus.Interrupted);
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 소화기를 든 채 {room.Name} 문 앞에서 멈췄다 — {Ko.IGa(bot.Name)} 먼저 들어간다", c.Id);
            }
            else
            {
                w.Automation.Trusts.Change(c, -0.01f, $"{room.Name} 불 — 로봇을 기다리라는 말을 듣지 않았다", quiet: true);
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 로봇을 기다릴 수 없다며 그대로 {room.Name}에 들어간다", c.Id);
            }
        }
    }

    /// <summary>"문 앞에서 기다려라"의 실제: 가까운 한 사람이 문 앞에 서서 로봇을 지켜본다 — 로봇이 물러나면 바로 이어 들어간다.</summary>
    private void PostGuard(Room room, FireWatch fw)
    {
        var w = _w;
        fw.GuardTries++;
        if (NoDoorGuard) return;
        var spots = new List<Cell>();
        foreach (var d in room.Doors)
        {
            if (d.Removed || d.Welded || d.IsExternal) continue;
            foreach (var dir in Cell.Dirs4)
            {
                var o = d.Cell + dir;
                if (w.Ship.RoomAt(o) is Room orr && orr != room && !orr.Detached && !orr.Leaking && w.Ship.IsWalkable(o) && w.Ship.DoorAt(o) == null && w.Ship.FurnitureAt(o) == null) { spots.Add(o); break; }
            }
        }
        if (spots.Count == 0) return;
        CrewMember? who = null;
        Cell at = default;
        int best = 41; // 배 반대편 사람은 부르지 않는다 (소화 자리는 25칸 더 멀어도)
        foreach (var c in w.Crew)
        {
            if (!c.CanAct || !c.IsAwake || c.IsChild || c.Outside || c.Room == null || c.Room == room || c.Job is { Urgent: true }) continue;
            bool fireRole = w.CrisisCrew.BillRole(c) == StationRole.Fire; // 비상 배치표의 소화 자리가 먼저 선다 (곁에서 구경하던 비번 사람을 끌어오지 않게)
            foreach (var sp in spots)
            {
                int d = Math.Abs(c.Cell.X - sp.X) + Math.Abs(c.Cell.Y - sp.Y) - (fireRole ? 25 : 0);
                if (d < best) { best = d; who = c; at = sp; }
            }
        }
        if (who == null) return;
        fw.Guard = who.Id;
        fw.GuardSpot = at;
        who.Interrupt(w);
        Line(CmdTarget.Crew, who.Id, room, $"{who.Name}: {room.Name} 문 앞에서 대기", "로봇이 물러나면 바로 이어 들어간다", 0.8f, 20f, fw.Decision);
    }

    private void EndGuard(FireWatch fw, string result)
    {
        if (fw.Guard < 0) return;
        CloseLine(CmdTarget.Crew, fw.Guard, result);
        fw.Guard = -1;
    }

    /// <summary>문 앞 대기를 맡은 사람이면 그 방과 설 자리.</summary>
    public (Room room, Cell spot)? GuardOf(CrewMember c)
    {
        foreach (var (id, fw) in _fire)
            if (fw.Guard == c.Id && fw.Held) return (_w.Ship.Rooms[id], fw.GuardSpot);
        return null;
    }

    /// <summary>이 방 불에 소방 로봇이 가 있다 (화재 대응 수순이 소화조로 셈한다).</summary>
    public bool BotOn(Room room) => !Off && _fire.TryGetValue(room.Id, out var fw) && _w.Robots.Robots[fw.Robot] is var rb && rb.Operational && rb.Foam > 0.05f && rb.FightingFire;

    /// <summary>사람의 소화 일을 잠깐 미뤄 둔다 (로봇이 먼저) / 푼다.</summary>
    private void Hold(Room room, bool on)
    {
        var w = _w;
        foreach (var o in w.Board.All)
            if (o.Kind == WorkKind.Extinguish && !o.Closed && o.Assignee == null && o.Target.CurrentRoom == room)
                w.Board.Block(o, null, on ? 2f / 60f : 0f);
    }

    private void Settle(int roomId, bool ok)
    {
        if (!_fire.TryGetValue(roomId, out var fw)) return;
        Method("fire:robot", ok);
        EndGuard(fw, ok ? "로봇이 껐다 — 문 앞에서 지켜봤다" : "로봇이 물러났다 — 사람 손으로");
        if (ok && fw.Held) Hold(_w.Ship.Rooms[roomId], false);
    }

    private void ReleaseFires(string? why)
    {
        foreach (var (id, fw) in _fire)
        {
            if (fw.Held) Hold(_w.Ship.Rooms[id], false);
            EndGuard(fw, why ?? "불이 꺼졌다");
            if (why != null) _w.Log.Add(_w.Tick, LogKind.Warning, $"{_w.Ship.Rooms[id].Name}: {why}");
        }
        _fire.Clear();
    }

    /// <summary>로봇이 먼저 불 속에 들어가는 걸 본 사람: 컴퓨터를 조금 더 믿는다 · 불을 안다.</summary>
    private void Witness(Robot rb, Room room, FireWatch fw)
    {
        var w = _w;
        var saw = fw.Saw;
        CrewMember? first = null;
        bool none = saw.Count == 0;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.IsChild || saw.Contains(c.Id)) continue;
            float d = MathF.Abs(c.Position.X - rb.Position.X) + MathF.Abs(c.Position.Y - rb.Position.Y);
            // 통합8 함교 · 통신실 화면(그 방 카메라)으로 지켜본 사람도 본 것이다 — 사람을 문 앞에 붙잡아 두면 곁에서 본 사람이 없었다
            bool screen = c.Room is { Type: RoomType.Bridge or RoomType.Comms or RoomType.ServerRoom } && room.DataLinked && w.Automation.MainOnline;
            bool door = c.Id == fw.Guard && Math.Abs(c.Cell.X - fw.GuardSpot.X) + Math.Abs(c.Cell.Y - fw.GuardSpot.Y) <= 1; // 문 앞에 선 사람은 문틈으로 본다 (큰 방이면 로봇이 9칸 넘게 떨어져 있어도)
            if (d > 9f && !screen && !door) continue;
            w.Automation.Trusts.Change(c, 0.04f, $"{Ko.IGa(rb.Name)} 사람보다 먼저 불 속에 들어갔다", quiet: true); // 통합8 사람 대신 불에 든 걸 본 일은 작지 않다 (0.02는 그 사이 다른 일로 깎인 몫에 묻혔다)
            w.Brain2.Beliefs.Learn(c, Topic.Fire, room.Id, 1, BeliefSource.Seen, 0.95f);
            first ??= c;
            saw.Add(c.Id);
            Witnessed++;
        }
        if (first != null && none)
            Life.Diary(w, first, Persona.Say(first, $"{Ko.IGa(rb.Name)} 먼저 {room.Name} 연기 속으로 들어갔다. 우리는 문 앞에서 기다렸다"));
    }

    // ───────────── 파공: 드론이 밖에서 ─────────────

    private void Breaches()
    {
        var w = _w;
        if (w.Drones.Drones.Count == 0 || DroneSystem.Hatch(w) == null || !w.Automation.DroneControl) return;
        List<WorkOrder>? list = null;
        foreach (var o in w.Board.All)
            if (o.Kind == WorkKind.SealBreach && !o.Closed && o.Assignee == null && o.Drone == null && o.Target.Kind == TargetKind.Wall && o.Target.Room is Room rr && (!rr.Abandoned || w.Sensors.Alarm == null && w.Hazards.Shower.Count == 0)) // 포기한 구획도 조용할 때 밖에서 막는다 (드론은 방에 들어가지 않는다 — 되찾기 일감이 올라온 구멍만)
                (list ??= new()).Add(o);
        if (list == null) return;
        foreach (var o in list.OrderByDescending(o => o.Urgency).ThenBy(o => o.Id))
        {
            string key = $"seal:{o.Target.Cell.X},{o.Target.Cell.Y}";
            if (_decided.TryGetValue(key, out long t0) && w.Tick - t0 < SimTime.Minutes(12)) continue;
            if (w.Drones.WallSpot(o) is not Vector2 at) continue;
            if (w.Ship.WallAt(o.Target.Cell) is not WallState wall) continue;
            _decided[key] = w.Tick;
            var room = o.Target.Room!;
            var (d, eta, why) = w.Drones.FleetSealer(o, at);
            bool suit = room.Unbreathable || room.Air.Pressure < 60f || wall.Breach >= 0.25f;
            bool rocks = w.Hazards.Shower.Count > 0 || w.Sensors.Alarm != null;
            float crewRisk = (suit ? 0.35f : 0.1f) + (rocks && suit ? 0.45f : rocks ? 0.1f : 0f) + (wall.Breach >= 0.25f ? 0.15f : 0f);
            var opts = new List<ForeseeOption>
            {
                new() { Key = "crew", Name = suit ? "사람이 우주복을 입고 막는다" : "사람이 안에서 막는다", People = crewRisk, Ship = 0.02f, Minutes = suit ? 14f : 6f,
                    Note = rocks ? "파편이 아직 떨어진다" : "" },
                new() { Key = "drone", Name = d != null ? $"{Ko.IGa(d.Name)} 밖에서 막는다" : "드론이 밖에서 막는다", People = 0f, Ship = 0.04f + (rocks ? 0.08f : 0f) + 0.002f * eta,
                    Minutes = eta, Note = rocks ? "드론을 잃을 수 있다" : "", Allowed = d != null, Blocked = why },
            };
            var dec = w.Automation.Foresee.Fleet("파공", room, $"{room.Name} 외벽 파공 {wall.Breach * 100:0}% — 누가 막나", opts);
            dec.Grader = (world, dd) => wall.Breach <= 0f || wall.Patched ? (1, "맞았다 — 막혔다") : null;
            if (dec.Pick.Key != "drone" || d == null) continue;
            DroneTask[d.Id] = o.Id;
            o.Drone = d;
            d.Mind.Say($"주 컴퓨터가 보냈다 — {room.Name} 파공을 밖에서 ({dec.Reason})", w.Tick);
            Line(CmdTarget.Drone, d.Id, room, $"{d.Name}: {room.Name} 파공 — 밖에서 막기", dec.Reason, 0.95f, 40f, dec.Id, o.Id);
        }
    }

    /// <summary>이 파공은 다시 견줘 본다 (맡았던 드론을 교대로 돌렸을 때).</summary>
    internal void Forget(WorkOrder o) => _decided.Remove($"seal:{o.Target.Cell.X},{o.Target.Cell.Y}");

    // ───────────── 명령선 · 생각 ─────────────

    internal void Line(CmdTarget t, int id, Room? room, string what, string why, float pri = 0.8f, float minutes = 30f, int decision = -1, int workOrder = -1)
    {
        var w = _w;
        if (!(w.Automation.Present && (w.Automation.MainOnline || w.Automation.Core.BackupCore))) return;
        w.Automation.Command.Line(t, id, room, what, why, pri, minutes, decision, workOrder, "보냄");
        Lines++;
    }

    /// <summary>드론에게 내린 명령이 끝났다 (명령선 닫기).</summary>
    internal void CloseLine(CmdTarget t, int id, string result)
    {
        foreach (var o in _w.Automation.Command.Lines)
            if (o.Open && o.Target == t && o.TargetId == id && o.WorkOrderId < 0) { o.State = "끝"; o.Result = result; }
    }

    internal void SaidFetch(Drone lost, Drone by)
    {
        if (!_fetchSaid.Add(lost.Id * 100 + by.Id)) return;
        Fetches++;
        by.Mind.Say($"{Ko.IGa(lost.Name)} 떠내려간다 — 건져 온다", _w.Tick);
        Line(CmdTarget.Drone, by.Id, null, $"{by.Name}: 떠내려가는 {lost.Name} 건져 오기", $"{lost.Doing}", 0.85f, 60f);
    }

    /// <summary>같이 일하는 사람 (거들어 준 시간).</summary>
    internal void Bond(Robot r, CrewMember c)
    {
        if (!_bond.TryGetValue(r.Id, out var b)) _bond[r.Id] = b = new();
        b[c.Id] = b.GetValueOrDefault(c.Id) + 1f / 60f;
    }

    /// <summary>로봇이 부서졌다: 같이 일하던 사람이 아쉬워한다.</summary>
    internal void Mourn(Robot r, string why)
    {
        var w = _w;
        Wrecks++;
        CrewMember? keeper = null;
        float best = 0f;
        if (_bond.TryGetValue(r.Id, out var b))
            foreach (var (cid, h) in b)
                if (h > best && w.Crew.FirstOrDefault(x => x.Id == cid) is CrewMember c && !c.Dead) { best = h; keeper = c; }
        keeper ??= w.Crew.Where(c => !c.Dead && !c.IsChild && c.Role is CrewRole.Technician or CrewRole.Engineer).OrderBy(c => c.Id).FirstOrDefault();
        if (keeper == null) return;
        Mourned++;
        keeper.Needs.Stress = MathF.Min(1f, keeper.Needs.Stress + 0.05f);
        Memory.Shake(w, keeper, 0.015f, $"{Ko.EulReul(r.Name)} 잃었다");
        Life.Diary(w, keeper, Persona.Say(keeper, best > 0.5f
            ? $"{Ko.IGa(r.Name)} 부서졌다. 내 옆에서 {best:0}시간을 거들던 녀석이었다"
            : $"{Ko.IGa(r.Name)} 부서졌다. {r.JobsDone}건을 해 준 녀석이었다"));
        w.History.Add(w, HistoryKind.Memory, $"{Ko.IGa(r.Name)} 부서졌다 — {why} · 한 일 {r.JobsDone}건 · {Ko.IGa(keeper.Name)} 아쉬워한다", r.Room, new[] { keeper });
    }

    private void Minds(bool cmd)
    {
        var w = _w;
        foreach (var r in w.Robots.Robots)
        {
            var m = r.Mind;
            if (r.State == RobotState.Lost) { m.Now = r.Doing; m.Next = ""; continue; }
            if (r.Steps != null && m.StageAt.Count > 0 && (m.Stages.Count == 0 || m.StageAt[^1] < r.Steps.Count))
            {
                int k = 0;
                while (k < m.StageAt.Count && m.StageAt[k] < r.StepIndex) k++;
                m.Stage = Math.Min(k, m.Stages.Count - 1);
            }
            bool staged = r.Steps != null && r.State == RobotState.Active && !r.Homing && m.Stages.Count > 1;
            m.Now = staged ? $"{m.Stages[m.Stage]} ({m.Stage + 1}/{m.Stages.Count}) — {r.Doing}" : r.Doing;
            m.Next = r.State switch
            {
                RobotState.Stalled => r.TowBot != null ? "끌려가는 중" : "누가 끌고 가거나 고쳐 주길 기다린다",
                RobotState.Towed => "충전대에서 충전 · 수리",
                RobotState.Docked when r.Fault != null => RobotSystem.CanSelfRepair(r) ? "스스로 고치고 다시 나간다" : "고쳐 주길 기다린다",
                RobotState.Docked => r.Battery < 0.35f ? $"충전 — {(0.35f - r.Battery) / RobotSystem.ChargePerHour * 60f:0}분 뒤 나갈 수 있다" : "할 일이 생기면 나간다",
                _ when staged && m.Stage + 1 < m.Stages.Count => m.Stages[m.Stage + 1],
                _ when r.Homing => "충전 · 대기",
                _ => "끝나면 충전대로 · 다음 일",
            };
            if (r.State == RobotState.Active)
            {
                float back = RobotSystem.FleetReturnCost(r);
                m.Power = $"배터리 {r.Battery * 100:0}% · 돌아갈 몫 {back * 100:0}%" + (Thrift ? " · 아껴 쓰는 중" : "");
            }
            else if (r.State == RobotState.Docked) m.Power = $"배터리 {r.Battery * 100:0}%" + (r.Battery < 0.99f && RobotSystem.DockWorking(r) ? " · 충전 중" : "");
            if (m.WhySince < 0 || w.Tick - m.WhySince > SimTime.Minutes(30))
                m.Say(!cmd ? "관제가 없어 스스로 판단한다" : r.Helping != null ? $"{Ko.EulReul(r.Helping.Name)} 거들면 긴 일이 빨라진다" : r.State == RobotState.Docked ? "할 일이 없어 충전하며 기다린다" : m.Why, w.Tick);
        }
        foreach (var d in w.Drones.Drones)
        {
            var m = d.Mind;
            m.Now = d.Doing;
            m.Next = d.State switch
            {
                DroneState.Outbound => d.Order != null ? $"도착하면 {d.Order.Title}" : d.Fetching != null ? $"{d.Fetching.Name} 붙잡기" : "도착",
                DroneState.Working => "끝나면 복귀 · 충전",
                DroneState.Towing => "거치대까지 끌고 오기",
                DroneState.Returning => d.Hurt.Sheltering ? "파편이 지나가면 다시 일터로" : "거치대 · 충전",
                DroneState.Adrift => "건져 주길 기다린다",
                DroneState.Lost => "",
                _ => DroneTask.ContainsKey(d.Id) ? "충전되면 맡은 파공으로" : d.Battery < 0.35f ? "충전" : "할 일이 생기면 나간다",
            };
            m.Power = $"배터리 {d.Battery * 100:0}%" + (d.Outside && d.State is not (DroneState.Adrift or DroneState.Lost) ? $" · 돌아올 몫 {w.Drones.FleetReturnCost(d) * 100:0}%" : "");
            if (m.WhySince < 0 || w.Tick - m.WhySince > SimTime.Minutes(30))
                m.Say(d.State == DroneState.Docked ? (DroneSystem.DockWorking(d) ? "거치대에서 충전하며 기다린다" : "거치대에 전기가 없다") : m.Why, w.Tick);
        }
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Tier); I(FireFirst); I(Tows); I(Fixes); I(Seals); I(SealFails); I(Reliefs); I(Fetches); I(Reroutes); I(Yields); I(Lifts); I(Tests); I(TestFails);
        I(Waits); I(Retreats); I(Wrecks); I(Dodges); I(HullJobs); I(HullRounds); I(Tunes); I(Hits.Count); I(DroneTask.Count); I(Carry.Count); I(_fire.Count);
    }
}

/// <summary>"문 앞에서 기다려라" — 소방 로봇이 먼저 들어간 불: 주 컴퓨터가 고른 한 사람이 문 앞에 서서 지켜본다 (로봇이 물러나면 이어 들어간다).</summary>
public sealed class DoorGuardActivity : Activity
{
    public override string Id => "doorguard";
    public override string Label => "문 앞 대기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Fire.Count == 0 || c.Down || c.Outside || w.Fleet.GuardOf(c) is not { } g) return (0f, "—");
        return (1.05f, $"주 컴퓨터 — {g.room.Name} 문 앞에서 대기 (소방 로봇이 먼저 들어갔다)");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Fleet.GuardOf(c) is not { } g || dist.Get(g.spot) < 0) return null;
        var room = g.room;
        return new Job(this, "문 앞 대기", new List<Toil>
        {
            new GotoToil(g.spot),
            new WaitToil(SimTime.Minutes(20), Pose.Standing, room.Center) { DoneWhen = (cm, world) => world.Fleet.GuardOf(cm) == null },
        })
        {
            LogText = $"{room.Name} 문 앞에서 소방 로봇을 지켜본다 — 물러나면 이어 들어간다",
            LogKind = LogKind.Work,
            TargetRoom = room,
            Urgent = true,
            InterruptMargin = 0.3f,
        };
    }
}
