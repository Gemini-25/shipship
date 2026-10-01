using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.5 중앙 컴퓨터 등급 · 수동 조종 · 판단 근거 · 연쇄 예측 · 방침.
//  I 경보 — 감지·경보·일지 / II 자동 차단 — 격벽·댐퍼·분전함·부하 차단 / III 조정 — 차례로 재가동·원격 급수 밸브
//  IV 추론 — 원인 추정과 판단 근거를 말한다 · 전조를 먼저 본다 / V 지휘 — 연쇄 예측 · 드론·로봇 지휘
// v13.0 V 지휘가 기본이다. I~IV는 기술 단계가 아니라 고장 사다리 — 지금 살아 있는 기능:
//  데이터망이 4분의 1 넘게 끊기거나 컴퓨터가 상하면(효율 60% 아래) IV, 절반 넘게 끊기거나 크게 상하면 III,
//  주 컴퓨터가 서면 예비 제어기만 II, 그것도 서면 I. 데이터선이 끊긴 방은 그 방만 손으로.
// 컴퓨터는 보수적이다: 피해는 막지만 넓게 끊는다. 판단력 좋은 사람이 관제석에서 수동 조종하면 좁게·빨리 되돌린다.

public sealed partial class AutomationSystem
{
    public static string LevelName(int l) => l switch { 1 => "I 경보", 2 => "II 자동 차단", 3 => "III 조정", 4 => "IV 추론", _ => "V 지휘" };

    /// <summary>등급 (v13.0 고장 사다리): 멀쩡하면 V. 데이터망·컴퓨터가 상한 만큼 내려간다. 예비 제어기만 돌면 II, 모두 멎으면 I.</summary>
    public int Level
    {
        get
        {
            if (!Present) return 2;
            if (!MainOnline) return BackupActive ? 2 : 1;
            if (_levelTick == _world.Tick) return _levelCached;
            _levelTick = _world.Tick;
            float eff = Computer?.Efficiency ?? 0f;
            int live = 0, linked = 0;
            foreach (var r in _world.Ship.LiveRooms) { live++; if (r.DataLinked) linked++; }
            float coverage = live > 0 ? linked / (float)live : 1f;
            int lvl = coverage < 0.5f || eff < 0.35f ? 3 : coverage < 0.75f || eff < 0.6f ? 4 : 5;
            return _levelCached = Math.Clamp(Math.Min(lvl, LevelCap), 1, 5);
        }
    }
    private long _levelTick = -1;
    private int _levelCached = 5;

    /// <summary>시험·화면 점검용 상한 (기본 V).</summary>
    public int LevelCap { get; set; } = 5;

    /// <summary>등급이 왜 내려갔나 (관제 화면).</summary>
    public string LevelWhy
    {
        get
        {
            if (!Present) return "주 컴퓨터가 없는 배";
            if (!MainOnline) return BackupActive ? "주 컴퓨터 정지 — 예비 제어기만" : "주 컴퓨터·예비 제어기 정지";
            float eff = Computer?.Efficiency ?? 0f;
            int live = _world.Ship.LiveRooms.Count(), linked = _world.Ship.LiveRooms.Count(r => r.DataLinked);
            if (LevelCap < 5) return $"상한 {LevelName(LevelCap)}";
            if (linked < live) return $"데이터선 {live - linked}/{live}방 끊김 · 컴퓨터 {eff * 100:0}%";
            return eff < 0.6f ? $"컴퓨터 {eff * 100:0}%" : "정상";
        }
    }

    /// <summary>관제석에서 수동 조종 중인 사람.</summary>
    public CrewMember? Operator { get; private set; }

    /// <summary>감압 때: 배 우선(바로 닫는다) ↔ 사람 우선(안에 사람이 있으면 카운트다운). 승무원 회의가 정한다 (v13.0 방침 "감압 격벽").</summary>
    public bool ShipFirst
    {
        get => _world.Policies["decompress"] == 1;
        set => _world.Policies.Set("decompress", value ? 1 : 0, "시험·설정");
    }
    public string PolicyNote { get; set; } = "처음 정한 대로 (사람 우선)";
    public int LateSeals { get; set; }      // 기다리는 사이 옆방까지 공기가 빠졌다
    public int TrappedCasualties { get; set; } // 닫힌 방 안에서 쓰러졌다
    private long _policyReviewed = -1;

    /// <summary>판단 근거 (관제 화면 · 기록).</summary>
    public List<(long tick, string text)> Reasoning { get; } = new();
    private readonly Dictionary<string, long> _said = new();

    public void Reason(string key, string text, long cooldown = -1)
    {
        var w = _world;
        if (Level < 4 && Operator == null) return; // 판단 근거를 말하는 건 IV부터 (사람이 조종하면 사람이 말한다)
        if (cooldown < 0) cooldown = SimTime.Hours(1);
        if (_said.TryGetValue(key, out var t) && w.Tick - t < cooldown) return;
        _said[key] = w.Tick;
        Reasoning.Add((w.Tick, text));
        if (Reasoning.Count > 60) Reasoning.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, (Operator != null ? $"{Operator.Name}(관제): " : "주 컴퓨터: ") + text);
    }

    /// <summary>시스템 틱마다 (Update 끝에서): 조종하는 사람 · 연쇄 예측 · 방침 검토.</summary>
    internal void Think(float dt)
    {
        var w = _world;
        Operator = MainOnline ? w.Crew.FirstOrDefault(c => !c.Dead && c.CanAct && c.Job?.Order?.Kind == WorkKind.ManualControl && c.Pose == Pose.Working) : null;
        if (!MainOnline) return;

        // V 지휘: 연쇄 예측 — 배터리가 언제 바닥나나, 산소가 언제 모자라나
        if (Level >= 5 || Operator != null)
        {
            var p = w.Power;
            if (!p.ReactorOnline && p.BatteryCapacity > 0f && p.BatteryFlow < -0.5f)
            {
                float hours = p.BatteryCharge / -p.BatteryFlow; // 충전량은 kWh
                Reason("battery", $"예측: 이대로면 배터리가 {hours:0.#}시간 뒤 바닥 — " + (p.AuxRunning ? "보조 발전기가 도는 중" : "보조 발전기 기동을 권한다") + (w.Power.ReactorPoison > 0.3f ? $" · 원자로 재기동은 제논 독 때문에 {w.Power.ReactorPoison * 100:0}%에서 시작" : ""), SimTime.Hours(2));
            }
            foreach (var r in w.Ship.LiveRooms)
            {
                if (r.Detached || r.Abandoned || !w.Crew.Any(c => c.Room == r && !c.Dead)) continue;
                if (r.Air.O2 < 17f && !Atmosphere.Vented(r))
                    Reason($"o2:{r.Id}", $"예측: {r.Name} 산소 {r.Air.O2:0.0}kPa · 환기가 끊겨 계속 준다 — 사람을 옮기거나 덕트를 이어라", SimTime.Hours(1));
            }
        }

        // IV 추론: 원인을 짚어 말한다 (인과 사슬에서)
        if (Level >= 4 || Operator != null)
        {
            var log = w.Causes;
            int scram = log.OpenNode("scram");
            if (scram >= 0 && log.Node(scram).Parent is int sp && sp >= 0)
                Reason("scram", $"원인 추정: {log.Node(sp).Text} → 냉각이 모자라 원자로 긴급 정지 · 조치: 부하를 줄이고 배터리로 버틴다 · 요청: 냉각 계통부터", SimTime.Hours(3));
            foreach (var inc in log.Incidents.Where(i => i.Open).TakeLast(3))
                foreach (var id in inc.Nodes)
                {
                    var n = log.Node(id);
                    if (!n.Open || n.Parent < 0) continue;
                    if (n.Kind == CauseKind.Outage) Reason($"out:{id}", $"{n.Text} · 원인 추정: {log.Node(n.Parent).Text} · 요청: 그곳부터 잇기", SimTime.Hours(2));
                    else if (n.Kind == CauseKind.Fire) Reason($"fire:{id}", $"{n.Text} · 조치: 그 방 댐퍼 폐쇄 · 요청: 진화 (소화기)", SimTime.Hours(2));
                    else if (n.Kind == CauseKind.Casualty) Reason($"cas:{id}", $"{n.Text} · 원인 추정: {log.Node(n.Parent).Text} · 요청: 구조·치료", SimTime.Hours(2));
                }
        }

        // 닫힌 방 안에 남았던 사람이 한 시간 안에 쓰러졌나
        foreach (var (roomId, since) in w.Hull.Trapped.ToList())
        {
            if (w.Tick - since > SimTime.Hours(1)) { w.Hull.Trapped.Remove(roomId); continue; }
            if (w.Crew.Any(c => c.Down && c.Room?.Id == roomId)) { TrappedCasualties++; w.Hull.Trapped.Remove(roomId); }
        }

        // 방침 검토 (하루 한 번): 늦게 닫아 옆방까지 잃었다 ↔ 닫힌 방에서 사람이 쓰러졌다
        if (_policyReviewed < 0) _policyReviewed = w.Tick;
        if (w.Tick - _policyReviewed >= SimTime.TicksPerDay)
        {
            _policyReviewed = w.Tick;
            if (!ShipFirst && LateSeals >= 2) Vote(true, $"감압 때 사람을 기다리다 옆방까지 공기를 {LateSeals}번 잃었다");
            else if (ShipFirst && TrappedCasualties >= 1) Vote(false, $"바로 닫은 격벽 안에서 {TrappedCasualties}명이 쓰러졌다");
        }
    }

    /// <summary>승무원 회의: 사람 우선 ↔ 배 우선 (침착하고 성실한 사람은 배 우선, 용감한 사람은 사람 우선 쪽으로).</summary>
    private void Vote(bool shipFirst, string why)
    {
        var w = _world;
        var voters = w.Crew.Where(c => !c.Dead && c.CanAct && !c.IsChild).ToList();
        if (voters.Count == 0) return;
        // v12.7 가치관: 효율·규칙을 앞세우는 사람은 배 우선, 사람을 앞세우는 사람은 사람 우선 쪽으로 기운다
        static float Lean(CrewMember c) => c.Value switch { CrewValue.Efficiency => 0.15f, CrewValue.Rules => 0.1f, CrewValue.People => -0.2f, CrewValue.Safety => -0.05f, _ => 0f };
        int yes = voters.Count(c => (c.Traits.Calm + c.Traits.Diligence) * 0.5f - 0.3f * c.Traits.Bravery + Lean(c) + (shipFirst ? 0.1f : -0.1f) > 0.3f == shipFirst);
        int no = voters.Count - yes;
        bool pass = yes > no;
        string rule = shipFirst ? "감압 때 격벽을 바로 닫는다 (배 우선)" : "안에 사람이 있으면 격벽을 기다린다 (사람 우선)";
        if (pass)
        {
            w.Policies.Set("decompress", shipFirst ? 1 : 0, why, yes, no);
            PolicyNote = $"{SimTime.Day(w.Tick)}일 회의: {rule} — 찬성 {yes} · 반대 {no}";
            LateSeals = 0; TrappedCasualties = 0;
        }
        w.History.Add(w, HistoryKind.Decision, $"회의: {rule}? — {why} · 찬성 {yes} · 반대 {no} → {(pass ? "정했다" : "그대로")}", crew: voters, log: true);
        w.History.DecisionsMade++;
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v12.5 수동 조종: 위기 때 판단력 좋은 사람(전기·공학 솜씨)이 관제석에 앉는다 — 컴퓨터보다 좁게 끊고 빨리 되돌린다.</summary>
    private void ScanManualControl(Poster post)
    {
        var w = _world;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline || a.Level < 2) return;
        var level = Crisis.Level(w);
        bool trouble = level >= CrisisLevel.Alert || w.Ship.Rooms.Any(r => !r.Detached && (MoistureSystem.Depth(r) > 0.08f || r.BreakerOff || r.LockPendingUntil >= 0));
        if (!trouble) return;
        var seat = w.Ship.FurnitureOf(FurnitureType.Console).Where(f => f.Room.Type == RoomType.Bridge && !f.Room.Detached && f.Machine is { Stopped: false }).OrderBy(f => f.Id).FirstOrDefault()
                   ?? a.ComputerBody;
        if (seat == null || seat.Room.Detached) return;
        var spot = seat.UseSpots.FirstOrDefault(c => w.Ship.IsOpenFloor(c));
        if (spot == default) spot = seat.Room.Cells.FirstOrDefault(c => w.Ship.IsOpenFloor(c));
        if (spot == default) return;
        post(WorkKind.ManualControl, WorkTarget.AtCell(spot, seat.Room), level >= CrisisLevel.Emergency ? 0.9f : 0.75f, Skill.Electrical,
            $"관제석 수동 조종 — 컴퓨터(등급 {AutomationSystem.LevelName(a.Level)})보다 좁게 끊고 빨리 되돌린다", minSkill: 0.45f);
    }
}

public static partial class WorkPlanners
{
    /// <summary>관제석에 앉아 30분씩 (위기가 끝날 때까지 이어서).</summary>
    private static Job? ManualControl(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, o.Skill, at.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o); // 다음 스캔에서 아직 위기면 다시 올라온다
            return true;
        }));
        return Wrap(a, o, c, w, "수동 조종", toils, $"관제석 수동 조종 ({AutomationSystem.LevelName(w.Automation.Level)})");
    }
}
