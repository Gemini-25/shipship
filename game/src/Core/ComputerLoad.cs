using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.6 연산 자원: 모듈마다 부하가 있고, 부하가 크면 컴퓨터가 더 달아오른다 (과열 정지와 이어진다).
// 우선순위: 위기(비상 이상)에는 비필수 모듈(오락 보관함 · 개인 비서 · 조명 주기 …)부터 끄고 — 영화가 끊기면 사람들이 불평한다 —
// 부하가 90%를 넘으면 낮은 순위부터 더 끈다. 평온해지면 하나씩 다시 켠다.
// 재부팅: 몇 분 동안 주 코어가 멎는다 — v16.20 예비 코어가 핵심 고리(격벽 · 댐퍼 · 경보 · 부하 우선순위 · 진행 중인 소화)를 붙잡은 채 단계적으로,
//  다시 켜지면 모듈이 하나씩 돌아온다. 업데이트 버그(헛불 경보) · 방사선(값이 깨지고 가끔 멎는다)이 부른다.
// v16.20 연산 용량을 크게 (모듈 전부 + 평소 사고로는 넘치지 않는다) — 넘치는 건 극한(겹친 큰 사고 · 안전 모드 · 예비 코어 · 절전)에서만.

public sealed partial class AutomationSystem
{
    /// <summary>모듈 하나가 쓰는 연산 (단위).</summary>
    public static int ModuleLoad(ComputerModule m) => m switch
    {
        ComputerModule.FireResponse or ComputerModule.AirZones or ComputerModule.BioMonitor or ComputerModule.Preempt => 2,
        ComputerModule.Foresight or ComputerModule.RouteForecast => 2,
        ComputerModule.MediaVault => 3,
        ComputerModule.Assistant => 2,
        _ => 1,
    };

    /// <summary>끄는 순서 (낮을수록 먼저 끈다 · 9는 끄지 않는다).</summary>
    public static int ModulePriority(ComputerModule m) => m switch
    {
        ComputerModule.MediaVault => 0,
        ComputerModule.Assistant => 1,
        ComputerModule.LightCycle or ComputerModule.MealPlan or ComputerModule.AutoLog or ComputerModule.Training => 2,
        ComputerModule.Roster or ComputerModule.Archive or ComputerModule.CargoSort or ComputerModule.Balance or ComputerModule.QuietNight => 3,
        ComputerModule.WaterPlan or ComputerModule.PowerShare or ComputerModule.MaintPlan or ComputerModule.SoilWatch => 4,
        ComputerModule.Foresight or ComputerModule.FatigueAlert or ComputerModule.AutoCalib => 5,
        ComputerModule.RouteForecast or ComputerModule.Access or ComputerModule.Lessons or ComputerModule.ResourceAlloc => 6,
        ComputerModule.Preempt => 7,
        _ => 9,
    };

    /// <summary>연산 자원이 모자라 잠시 끈 모듈.</summary>
    public HashSet<ComputerModule> Suspended { get; } = new();
    public int Suspensions, Complaints;

    /// <summary>연산 용량 (컴퓨터 단계 · 효율).</summary>
    public float Capacity => Core.Capacity(); // v16.20 (60 + 20·단계) × 건강 · 전압 ×0.8 · 안전 모드 ×0.5 · 절전 ×0.75 (ComputerCore.cs)

    /// <summary>지금 부하 0~ (1 = 꽉 참): 켜진 모듈 + 진행 중인 대응 (불 · 공기 구역 · 제안 · 확인).</summary>
    public float Load { get; private set; }

    /// <summary>모듈 부하 + 위기 처리 부하 (단위).</summary>
    public float Demand()
    {
        float d = 0f;
        foreach (var m in _modules) if (!Suspended.Contains(m)) d += ModuleLoad(m);
        d += 2f * FireCases.Count + (ZoneActive ? 2f : 0f) + Asks.Open.Count() + Checks.Count * 0.5f;
        if (_planner is { Busy: true }) d += 0.5f; // v16.16 협상 중인 계획 (안건 · 제안)
        if (_foresee != null) d += _foresee.Load; // v16.20 미리 돌려 보기 · 겹친 사고
        if (_triage is { Busy: true }) d += 1f; // v16.20 전력 트리아지
        d += 0.15f * _wireless; // v16.20 무선으로 읽는 방 (대역이 좁아 연산을 더 쓴다)
        if (_fix != null) d += 0.4f * _fix.Plans.Count(p => p.Open) + (_probe != null ? 0.3f * _probe.Cases.Count(c => c.State == "확인 중") : 0f); // v16.26 계획 · 확인
        return d;
    }

    // ── 재부팅 ──
    public long RebootUntil { get; private set; } = -1;
    public long RebootStarted { get; private set; } = -1;
    public bool Rebooting => RebootUntil > _world.Tick;
    public string RebootWhy { get; private set; } = "";
    public int Reboots;

    /// <summary>몇 분 동안 멎는다 — 그동안 자동 조치는 없다 (사람이 손으로).</summary>
    public void Reboot(string why, float minutes = 4f)
    {
        var w = _world;
        if (!Present || Rebooting) return;
        RebootUntil = w.Tick + SimTime.Minutes(minutes);
        RebootStarted = w.Tick;
        RebootWhy = why;
        Reboots++;
        bool hold = Core.BackupAlive(Computer); // v16.20 단계적 재부팅: 예비 코어가 핵심 고리를 붙잡는다
        Book.Add(ActKind.Reboot, Computer?.Body.Room, why, hold ? $"{minutes:0}분 — 예비 연산기가 격벽·댐퍼·경보를 붙잡은 채 차례로" : $"{minutes:0}분 멎는다 — 그동안 사람이 손으로", "재부팅", hold ? "큰 판단은 기다려 달라" : "격벽·댐퍼·경보를 손으로", "", 0, minutes + 5f,
            (world, a) => (world.Automation.MainOnline ? 1 : 2, world.Automation.MainOnline ? "다시 켜졌다" : "아직 멎어 있다"));
        Speak.Announce(Voice.Style(hold ? $"다시 켭니다 — {minutes:0}분. 그동안 격벽 · 댐퍼 · 경보는 예비 연산기가 맡습니다. 제안은 그 뒤에" : $"재부팅 — {minutes:0}분 동안 자동화가 멎는다. 격벽과 댐퍼는 손으로"), null, 2);
        w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터 재부팅 — {why} ({minutes:0}분" + (hold ? " · 예비 연산기가 격벽 · 댐퍼 · 경보를 붙잡았다)" : " 동안 사람이 손으로)"), Computer?.Body.Room, log: true);
        // 모듈은 다시 켜질 때 하나씩 돌아온다 (핵심 · 높은 순위부터)
        foreach (var mod in _modules) if (ModulePriority(mod) < 8 && Suspended.Add(mod)) Core.StagedBack++;
    }

    // ── 업데이트 버그 · 방사선 ──
    public long BugUntil { get; private set; } = -1;
    public ComputerModule? BugModule { get; private set; }
    public int UpdateBugs, BugAlarms;
    private long _bugNext, _resNext;
    private int _bugAlarmsNow;
    private Rng? _resRng;
    private Rng RR => _resRng ??= new Rng(unchecked(_world.Seed * 4513 + 61));
    private readonly Dictionary<int, long> _complained = new();

    /// <summary>새 모듈을 올리면 가끔 버그가 따라온다 (헛불 경보) — 두 번 헛불이 나면 업데이트를 되돌리고 재부팅한다.</summary>
    internal void MaybeBug(ComputerModule m)
    {
        if (!RR.Chance(0.3f)) return;
        BugUntil = _world.Tick + SimTime.Hours(8);
        BugModule = m;
        _bugAlarmsNow = 0;
        _bugNext = _world.Tick + SimTime.Hours(RR.Range(0.5f, 1.5f));
        UpdateBugs++;
    }

    /// <summary>시스템 틱마다: 부하 · 우선순위 · 업데이트 버그 · 방사선.</summary>
    private void Resources(float dt)
    {
        var w = _world;
        Load = Demand() / MathF.Max(1f, Capacity);
        if (!MainOnline) return;
        if (w.Tick < _resNext) return;
        _resNext = w.Tick + SimTime.Minutes(1);
        var level = Crisis.Level(w);
        bool crisis = level >= CrisisLevel.Emergency;
        int saving = Core.SelfSaving ? 4 : Core.SafeMode ? 2 : -1; // v16.20 자기 절전 · 안전 모드: 그 순위까지 끈다
        // 끄기: 위기면 비필수(순위 2 이하)를 · 부하가 90%를 넘으면 낮은 순위부터
        foreach (var m in _modules.OrderBy(ModulePriority).ThenBy(m => (int)m).ToList())
        {
            int pr = ModulePriority(m);
            if (Suspended.Contains(m) || pr >= 8) continue;
            bool cut = crisis && pr <= 2 || Load > 0.9f || pr <= saving;
            if (!cut) break;
            Suspended.Add(m);
            Suspensions++;
            Load = Demand() / MathF.Max(1f, Capacity);
            string cause = pr <= saving ? (Core.SelfSaving ? "전기를 아낀다" : "달아올랐다") : crisis ? "위기" : "부하";
            Book.Add(ActKind.Module, null, crisis ? $"위기 ({Crisis.Name(level)}) · 부하 {Load * 100:0}%" : $"{cause} · 부하 {Load * 100:0}%", $"우선순위 {pr} — 비필수", $"{ModuleName(m)} 잠시 끔", "", "susp:" + (int)m, SimTime.Minutes(30), 30f);
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 연산을 아끼려 {Ko.EulReul(ModuleName(m))} 잠시 쉰다 ({cause} · {Load * 100:0}%)");
            if (m == ComputerModule.MediaVault) MediaCut();
            if (!crisis && Load <= 0.75f && pr > saving) break;
        }
        // 다시 켜기: 평온하고 부하가 낮으면 하나씩 (높은 순위부터)
        int floor = Math.Max(saving, crisis ? 2 : -1); // 위기면 비필수(순위 2 이하)는 쉬게 두고, 나머지는 하나씩 되찾는다 (v16.20 재부팅 뒤)
        if (Suspended.Count > 0 && Suspended.Any(m => ModulePriority(m) > floor))
        {
            var back = Suspended.Where(m => ModulePriority(m) > floor).OrderByDescending(ModulePriority).ThenBy(m => (int)m).First();
            float after = (Demand() + ModuleLoad(back)) / MathF.Max(1f, Capacity);
            if (after <= 0.7f)
            {
                Suspended.Remove(back);
                Load = Demand() / MathF.Max(1f, Capacity);
                w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {Ko.EulReul(ModuleName(back))} 다시 돌린다 (부하 {Load * 100:0}%)");
            }
        }
        // 업데이트 버그: 데이터선이 닿는 방 하나에 헛불
        if (BugUntil > w.Tick && w.Tick >= _bugNext)
        {
            _bugNext = w.Tick + SimTime.Hours(RR.Range(1f, 2f));
            var rooms = w.Ship.LiveRooms.Where(r => r.DataLinked && r.Type != RoomType.Corridor && w.Fire.CountIn(r) == 0 && Belief.Of(r).Fault == SensorFault.None).ToList();
            if (rooms.Count > 0)
            {
                var r = RR.Pick(rooms);
                Belief.Break(r, SensorFault.Ghost, $"업데이트 버그 ({(BugModule is ComputerModule bm ? ModuleName(bm) : "?")})");
                BugAlarms++;
                _bugAlarmsNow++;
                GhostAlarm(r);
                if (_bugAlarmsNow >= 2)
                {
                    Reason("bug", $"헛불 경보가 {_bugAlarmsNow}번 — 원인 추정: {(BugModule is ComputerModule b2 ? ModuleName(b2) + " 쪽" : "새")} 업데이트 · 조치: 업데이트를 되돌리고 재부팅", 0);
                    BugUntil = -1;
                    Reboot("업데이트 버그 — 되돌린다", 3f);
                }
            }
        }
        // 방사선 폭풍: 연산이 가끔 깨진다 (드물게 멎고 다시 켠다)
        if (w.Hazards.StormActive && RR.Chance(0.0006f)) Reboot("방사선 — 연산 오류", 3f);
    }

    /// <summary>감지기 헛불 — 믿지 못하는 감지기면 배 전체 경보 대신 사람 확인을 먼저 부른다.</summary>
    private void GhostAlarm(Room r)
    {
        var w = _world;
        var b = Belief.Of(r);
        bool doubt = b.Trust < 0.7f || Has(ComputerModule.Lessons) && b.FalseAlarms >= 1;
        Book.Add(ActKind.Alarm, r, $"{r.Name} 화재 감지기", doubt ? $"이 감지기는 오경보 {b.FalseAlarms}번 — 덜 믿는다" : "화재로 본다", doubt ? "사람 확인을 먼저 부른다" : "화재 경보",
            "확인", "ghost:" + r.Id, SimTime.Minutes(10), 15f, (world, a) => (-1, "틀렸다 — 헛불이었다 (오경보)"));
        if (!doubt) Speak.Announce(Voice.Style($"{r.Name} 화재 감지 — 가까운 사람은 확인하라"), r, 2);
        RequestCheck(r, doubt ? "오경보가 잦은 감지기 — 불이 있는지 직접 본다" : "화재 감지기 경보 — 불을 직접 본다", ghost: true);
    }

    /// <summary>오락 보관함이 꺼졌다 — 보던 영화가 끊긴 사람들이 불평한다.</summary>
    private void MediaCut()
    {
        var w = _world;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.Room is not Room r || r.Type is not (RoomType.Lounge or RoomType.Quarters or RoomType.Mess)) continue;
            if (_complained.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.Hours(4)) continue;
            _complained[c.Id] = w.Tick;
            Complaints++;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
            w.Log.Add(w.Tick, LogKind.Life, Persona.Say(c, "보던 영화가 끊겼다 — 컴퓨터가 오락 보관함을 껐다"), c.Id);
            Trusts.Change(c, -0.01f, "보던 영화를 끊었다", quiet: true);
        }
    }
}
