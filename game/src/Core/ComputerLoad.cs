using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.6 연산 자원: 모듈마다 부하가 있고, 부하가 크면 컴퓨터가 더 달아오른다 (과열 정지와 이어진다).
// 우선순위: 위기(비상 이상)에는 비필수 모듈(오락 보관함 · 개인 비서 · 조명 주기 …)부터 끄고 — 영화가 끊기면 사람들이 불평한다 —
// 부하가 90%를 넘으면 낮은 순위부터 더 끈다. 평온해지면 하나씩 다시 켠다.
// 재부팅: 몇 분 동안 주 컴퓨터가 멎는다 — 그동안 격벽·댐퍼·경보·소화 수순은 사람이 손으로. 업데이트 버그(헛불 경보) · 방사선(값이 깨지고 가끔 멎는다)이 부른다.

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
    public float Capacity => (Computer is Machine m ? (14f + 6f * m.Tier) * MathF.Max(0.3f, m.Efficiency) : 14f) * (_world.Power.Brownout ? 0.6f : 1f); // 전압이 떨어지면 연산을 낮춘다

    /// <summary>지금 부하 0~ (1 = 꽉 참): 켜진 모듈 + 진행 중인 대응 (불 · 공기 구역 · 제안 · 확인).</summary>
    public float Load { get; private set; }

    /// <summary>모듈 부하 + 위기 처리 부하 (단위).</summary>
    public float Demand()
    {
        float d = 0f;
        foreach (var m in _modules) if (!Suspended.Contains(m)) d += ModuleLoad(m);
        d += 2f * FireCases.Count + (ZoneActive ? 2f : 0f) + Asks.Open.Count() + Checks.Count * 0.5f;
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
        Book.Add(ActKind.Reboot, Computer?.Body.Room, why, $"{minutes:0}분 멎는다 — 그동안 사람이 손으로", "재부팅", "격벽·댐퍼·경보를 손으로", "", 0, minutes + 5f,
            (world, a) => (world.Automation.MainOnline ? 1 : 2, world.Automation.MainOnline ? "다시 켜졌다" : "아직 멎어 있다"));
        Speak.Announce(Voice.Style($"재부팅 — {minutes:0}분 동안 자동화가 멎는다. 격벽과 댐퍼는 손으로"), null, 2);
        w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터 재부팅 — {why} ({minutes:0}분 동안 사람이 손으로)", Computer?.Body.Room, log: true);
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
        // 끄기: 위기면 비필수(순위 2 이하)를 · 부하가 90%를 넘으면 낮은 순위부터
        foreach (var m in _modules.OrderBy(ModulePriority).ThenBy(m => (int)m).ToList())
        {
            int pr = ModulePriority(m);
            if (Suspended.Contains(m) || pr >= 8) continue;
            bool cut = crisis && pr <= 2 || Load > 0.9f;
            if (!cut) break;
            Suspended.Add(m);
            Suspensions++;
            Load = Demand() / MathF.Max(1f, Capacity);
            Book.Add(ActKind.Module, null, crisis ? $"위기 ({Crisis.Name(level)}) · 부하 {Load * 100:0}%" : $"부하 {Load * 100:0}%", $"우선순위 {pr} — 비필수", $"{ModuleName(m)} 잠시 끔", "", "susp:" + (int)m, SimTime.Minutes(30), 30f);
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 연산을 아끼려 {ModuleName(m)} 모듈을 잠시 끈다 ({(crisis ? "위기" : "부하")} {Load * 100:0}%)");
            if (m == ComputerModule.MediaVault) MediaCut();
            if (!crisis && Load <= 0.75f) break;
        }
        // 다시 켜기: 평온하고 부하가 낮으면 하나씩 (높은 순위부터)
        if (!crisis && Suspended.Count > 0)
        {
            var back = Suspended.OrderByDescending(ModulePriority).ThenBy(m => (int)m).First();
            float after = (Demand() + ModuleLoad(back)) / MathF.Max(1f, Capacity);
            if (after <= 0.7f)
            {
                Suspended.Remove(back);
                Load = Demand() / MathF.Max(1f, Capacity);
                w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {ModuleName(back)} 모듈을 다시 켠다 (부하 {Load * 100:0}%)");
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
                    Reason("bug", $"헛불 경보가 {_bugAlarmsNow}번 — 원인 추정: {(BugModule is ComputerModule b2 ? ModuleName(b2) : "새")} 모듈 업데이트 · 조치: 업데이트를 되돌리고 재부팅", 0);
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
