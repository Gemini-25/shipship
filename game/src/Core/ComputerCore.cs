using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.20 우주선급 주컴퓨터 — ① 모듈은 잠금 해제가 아니다 · ② 다쳐도 느려질 뿐.
//  ① 첫날부터 모듈 전부. 연구 · 설비 단계 · 시대 · 겪은 일은 품질 계수를 올린다:
//     정확도(예측 · 감지 잡음) · 속도(판단 주기) · 동시 처리 사고 수 · 예측 거리(분). 같은 모듈을 다시 "달면" 등급이 오른다 (경험 · 기술).
//  ② 주 코어 + 예비 코어(같은 랙 · 따로 도는 판) + 구역 소형 제어기(방마다 · 격벽 · 댐퍼 · 경보) + 컴퓨터 전용 비상 전원(UPS)
//     + 데이터선 고리(보조 간선) + 무선 예비(통신 중계 — 감지 · 경보만, 손은 데이터선이 있어야).
//     등급은 연산 여유로 천천히 내려간다 (데이터선이 반 끊겨도 판단은 유지 · 그 구역만 손으로).
//     과열 → 정지 대신 안전 모드(연산 반 · 열 40%) — 환기가 돌아오면 풀린다. 극한(48℃↑)이면 주 코어만 멎고 예비 코어가 붙잡는다.
//     재부팅은 단계적으로: 예비 코어가 핵심 고리(격벽 · 댐퍼 · 경보 · 부하 우선순위 · 진행 중인 소화)를 붙잡은 채 → 주 코어 → 모듈을 하나씩.
//  자기 절전: 전기가 모자라거나 UPS로 버틸 때 비필수 모듈을 끄고 자기 전력을 10%로 — 연산은 줄지만 필수 회로 몫을 늘린다.

public sealed class ShipCore
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 2027));

    public ShipCore(World w) => _w = w;

    // ───────────── ① 품질 계수 ─────────────

    /// <summary>정확도 0.5~0.97: 미리 돌려 보기의 잡음 · 감지 믿음.</summary>
    public float Accuracy { get; private set; } = 0.7f;
    /// <summary>판단 속도 배율 (1 = 기본 · 클수록 짧은 주기로 다시 본다).</summary>
    public float Speed { get; private set; } = 1f;
    /// <summary>한 번에 다루는 사고 수 (넘치면 순서대로 기다린다).</summary>
    public int Concurrency { get; private set; } = 2;
    /// <summary>미리 돌려 보는 거리 (분).</summary>
    public float Horizon { get; private set; } = 6f;
    public string QualityWhy { get; private set; } = "";
    private readonly Dictionary<ComputerModule, int> _grade = new();
    public int Grade(ComputerModule m) => _grade.TryGetValue(m, out var g) ? g : 0;
    public int GradeSum => _grade.Values.Sum();
    public int Upgrades;
    public List<(long tick, string text)> Growth { get; } = new();
    private long _qualityNext;

    /// <summary>모듈 등급을 올린다 (이미 단 모듈을 다시 "달면" — 기술 · 겪은 일).</summary>
    internal bool Upgrade(ComputerModule m, string why)
    {
        int g = Grade(m);
        if (g >= 3) return false;
        _grade[m] = g + 1;
        Upgrades++;
        string text = $"{Ko.IGa(AutomationSystem.ModuleName(m))} 더 정확해졌다 — {why}";
        Growth.Add((_w.Tick, text));
        if (Growth.Count > 40) Growth.RemoveAt(0);
        _w.History.Add(_w, HistoryKind.Decision, $"주 컴퓨터의 {text}", null, log: true);
        _qualityNext = 0;
        return true;
    }

    private static int Tier(World w) => w.Automation.Computer?.Tier ?? 1;

    /// <summary>한 시간마다: 연구 · 단계 · 시대 · 기술 · 겪은 일(모듈 등급)로 품질을 다시 잰다.</summary>
    private void Quality()
    {
        var w = _w;
        if (w.Tick < _qualityNext) return;
        _qualityNext = w.Tick + SimTime.TicksPerHour;
        int tier = Tier(w);
        int computing = 0;
        foreach (var id in w.Eras.Known) if (TechWeb.Find(id)?.Field == TechField.Computing) computing++;
        bool central = w.Eras.Has("centralcpu"), dist = w.Eras.Has("distctrl"), grid = w.Eras.Has("smartgrid"), ai = w.Eras.Has("aicaptain");
        float research = MathF.Min(0.08f, w.Research * 0.0004f);
        float exp = MathF.Min(0.08f, GradeSum * 0.008f);
        Accuracy = Math.Clamp(0.62f + 0.03f * computing + (central ? 0.08f : 0f) + (ai ? 0.06f : 0f) + 0.03f * (tier - 1) + research + exp, 0.5f, 0.97f);
        Speed = 1f + 0.15f * (tier - 1) + (ai ? 0.25f : 0f) + (dist ? 0.15f : 0f) + (grid ? 0.1f : 0f) + 0.05f * (w.Eras.Era - 1);
        Concurrency = 2 + (tier >= 2 ? 1 : 0) + (dist ? 1 : 0) + (ai ? 1 : 0);
        Horizon = 4f + 2f * tier + (central ? 4f : 0f) + (ai ? 2f : 0f) + 0.5f * (w.Eras.Era - 1);
        QualityWhy = $"본체 {tier}단계 · {EraSystem.EraName(w.Eras.Era)} · 계산 기술 {computing}가지" + (central ? " · 중앙 컴퓨터" : "") + (dist ? " · 분산 제어" : "") + (grid ? " · 지능형 배전" : "") + (ai ? " · 부함장 보조" : "")
                     + $" · 연구 {w.Research:0}점 · 겪고 나서 는 판단 {GradeSum}가지";
    }

    // ───────────── ② 이중화 ─────────────

    /// <summary>UPS 남은 시간 (분, 주 코어가 다 돌 때 기준).</summary>
    public float Ups { get; private set; } = -1f;
    public float UpsCapacity => 60f + 20f * (Tier(_w) - 1);
    public bool OnUps { get; private set; }
    public int UpsRuns;
    public bool SafeMode { get; private set; }
    public long SafeSince { get; private set; } = -1;
    public int SafeModes;
    private long _coolSince = -1, _extremeSince = -1;
    /// <summary>주 코어가 멎었는데 예비 코어가 핵심 고리를 붙잡고 있다 (등급 III).</summary>
    public bool BackupCore { get; private set; }
    public long BackupSince { get; private set; } = -1;
    public int Takeovers;
    public float BackupHours;
    /// <summary>자기 절전 (비필수 모듈을 끄고 자기 전력 10%).</summary>
    public bool SelfSaving { get; private set; }
    public string SelfSavingWhy { get; private set; } = "";
    public int SelfSaves;
    public float SavedKwh;
    /// <summary>재부팅 때 붙잡아 둔 모듈 (다시 켜지면 하나씩 돌아온다).</summary>
    public int StagedBack;

    /// <summary>주 코어 건강 0~1 (UPS로 버티면 전기가 없어도).</summary>
    public float MainHealth(Machine? m)
    {
        if (m == null || m.Body.Room.Detached) return 0f;
        if (m.Powered) return m.Efficiency;
        if (!OnUps) return 0f;
        return m.FaultFactor * Grades.Output(m.Grade) * (0.6f + 0.4f * m.Condition) * (1f - 0.25f * m.Wear * m.Wear);
    }

    /// <summary>예비 코어가 돌 수 있나: 랙이 있고(부서지거나 뜯기지 않고) · 전기나 UPS · 55℃ 아래. 저장장치 오류 · 과열 정지는 주 코어만 멈춘다.</summary>
    public bool BackupAlive(Machine? m)
    {
        if (m == null || m.Body.Room.Detached || m.Has(FaultKind.Wrecked) || m.Has(FaultKind.Stripped)) return false;
        if (m.Body.Room.Air.Temperature > 55f) return false;
        return m.Powered || Ups > 0f;
    }

    /// <summary>연산 용량 (단위): 크게 — 부하는 극한에서만 문제. 단계 · 건강 · 전압 · 안전 모드 · 절전 · 예비 코어.</summary>
    public float Capacity()
    {
        var a = _w.Automation;
        var m = a.Computer;
        if (m == null) return 20f;
        float baseC = 60f + 20f * m.Tier;
        float health = a.MainOnline ? MathF.Max(0.3f, MainHealth(m)) : BackupCore ? 0.4f : 0.3f;
        return baseC * (a.MateOrNull?.CapacityMul ?? 1f) * health * (_w.Power.Brownout ? 0.8f : 1f) * (SafeMode ? 0.5f : 1f) * (SelfSaving ? 0.75f : 1f);
    }

    /// <summary>방에 내는 열 배율 (안전 모드 · 절전 · UPS).</summary>
    public float HeatMul => (SafeMode ? 0.4f : 1f) * (SelfSaving ? 0.6f : 1f);

    /// <summary>시스템 틱마다 (주 코어 판정 전): UPS · 과열 안전 모드 · 품질.</summary>
    internal void Before(Machine? m, float dt)
    {
        var w = _w;
        Quality();
        if (Ups < 0f) Ups = UpsCapacity;
        // UPS: 전기가 있으면 채우고, 없으면 쓴다 (절전이면 반 · 예비 코어만이면 4분의 1)
        bool intact = m != null && !m.Body.Room.Detached && !m.Has(FaultKind.Wrecked) && !m.Has(FaultKind.Stripped);
        bool was = OnUps;
        OnUps = intact && !m!.Powered && Ups > 0f;
        if (intact && m!.Powered) Ups = MathF.Min(UpsCapacity, Ups + UpsCapacity * 0.5f * dt);
        else if (OnUps)
        {
            float use = 60f * dt * (SelfSaving ? 0.5f : 1f) * (w.Automation.MainOnline ? 1f : 0.25f);
            Ups = MathF.Max(0f, Ups - use);
        }
        if (OnUps && !was)
        {
            UpsRuns++;
            w.Automation.Book.Add(ActKind.Shed, m!.Body.Room, $"{m.Body.Room.Name} 정전 — 주 컴퓨터에 전기가 끊겼다", $"비상 전지로 {Ups:0}분 — 연산을 줄이면 두 배", "비상 전지로 넘어감 · 제 연산을 줄임", "전기 담당은 컴퓨터 쪽 회로를 봐 달라", "ups", SimTime.Minutes(20), 20f,
                (world, act) => (world.Automation.Computer?.Powered == true || world.Automation.CoreOnline ? 1 : -1, world.Automation.Computer?.Powered == true ? "맞았다 — 전기가 돌아올 때까지 버텼다" : world.Automation.CoreOnline ? "버티는 중" : "틀렸다 — 비상 전지가 바닥났다"));
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 전기가 끊겨 비상 전지로 버팁니다 ({Ups:0}분 · 연산을 줄이면 두 배)");
        }
        else if (!OnUps && was && intact && m!.Powered) w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 전기가 돌아왔습니다 — 비상 전지 {Ups:0}분어치를 남기고 다시 채웁니다");
        Thermal(m, dt);
    }

    /// <summary>과열 → 정지 대신 안전 모드. 환기가 돌아오고 식으면 푼다. 극한이면 주 코어만 멎는다.</summary>
    private void Thermal(Machine? m, float dt)
    {
        var w = _w;
        var a = w.Automation;
        if (m == null || m.Body.Room.Detached || !(m.Powered || OnUps) || m.Has(FaultKind.Wrecked)) { if (SafeMode) ExitSafe("컴퓨터가 멎었다"); return; }
        var room = m.Body.Room;
        float t = room.Air.Temperature;
        if (!SafeMode && t > AutomationSystem.OverheatC && !m.Has(FaultKind.Overheat))
        {
            SafeMode = true;
            SafeSince = w.Tick;
            SafeModes++;
            _coolSince = -1;
            bool vent = a.Ventilated(room);
            a.Book.Add(ActKind.Module, room, $"{room.Name} {t:0}℃ — 버틸 온도({AutomationSystem.OverheatC:0}℃)를 넘었다" + (vent ? "" : " · 환기가 끊겼다"), "멈추면 배 전체를 손으로 돌려야 한다 — 멈추지 않고 열을 줄인다",
                "연산을 반으로 줄여 열을 낮춤 · 급하지 않은 일은 쉼", vent ? "그 방 온도를 봐 달라" : "환기 댐퍼 · 생명유지실 전기부터", "safe", SimTime.Minutes(30), 20f,
                (world, act) => (world.Automation.MainOnline ? 1 : -1, world.Automation.MainOnline ? "맞았다 — 멈추지 않고 버텼다" : "틀렸다 — 그래도 멎었다"));
            a.Speak.Announce(a.Voice.Style($"{Ko.IGa(room.Name)} 뜨겁습니다 — 열을 줄이려 느리게 돕니다. 멈추지는 않습니다"), room, 1);
            w.History.Add(w, HistoryKind.Damage, $"주 컴퓨터가 달아올랐다 — {room.Name} {t:0}℃, 느리게 돌며 버틴다" + (vent ? "" : " (환기가 끊겼다)"), room);
            MarkLog.Add(m.Marks, w.Tick, $"달아올라 느리게 돎 ({t:0}℃)");
        }
        else if (SafeMode)
        {
            bool cool = t < AutomationSystem.RestartC + 1f && a.Ventilated(room);
            if (!cool) _coolSince = -1;
            else if (_coolSince < 0) _coolSince = w.Tick;
            else if (w.Tick - _coolSince >= SimTime.Minutes(5)) ExitSafe($"{t:0}℃ · 환기가 돈다");
        }
        // 극한: 안전 모드로도 못 버티면 주 코어만 과열 정지 (예비 코어가 붙잡는다 · 식으면 사람이 다시 켠다)
        bool extreme = SafeMode && t > AutomationSystem.OverheatC + 10f && !m.Has(FaultKind.Overheat) && m.Powered;
        if (!extreme) _extremeSince = -1;
        else if (_extremeSince < 0) _extremeSince = w.Tick;
        if (extreme && w.Tick - _extremeSince >= SimTime.Minutes(3)) // 느리게 돌아도 3분 넘게 48℃ 위 — 본체를 지킨다
        {
            _extremeSince = -1;
            a.Overheats++;
            w.Machines.Break(m, FaultKind.Overheat);
            MarkLog.Add(m.Marks, w.Tick, $"본체 과열 정지 ({t:0}℃) — 예비 연산기가 붙잡았다");
            w.History.Add(w, HistoryKind.Damage, $"주 컴퓨터 본체가 과열로 멎었다 — {room.Name} {t:0}℃ (느리게 돌아도 못 버텼다 · 예비 연산기가 격벽 · 댐퍼 · 경보를 붙잡았다)", room);
        }
    }

    private void ExitSafe(string why)
    {
        if (!SafeMode) return;
        SafeMode = false;
        float min = (_w.Tick - SafeSince) / (float)SimTime.Minutes(1);
        _w.Log.Add(_w.Tick, LogKind.Ship, $"주 컴퓨터: 식었습니다 — 다시 제 속도로 돕니다 ({why} · {min:0}분 만)");
    }

    /// <summary>주 코어 판정 뒤: 예비 코어가 붙잡나 · 자기 절전.</summary>
    internal void After(Machine? m, bool main, float dt)
    {
        var w = _w;
        var a = w.Automation;
        bool hold = !main && BackupAlive(m);
        if (hold && !BackupCore)
        {
            BackupSince = w.Tick;
            Takeovers++;
            _takeover = a.Rebooting ? $"재부팅 — {a.RebootWhy}" : m == null ? "?" : !m.Powered && !OnUps ? "전기" : m.Faults.FirstOrDefault()?.Name ?? (OnUps ? "비상 전지 바닥" : "멈춤"); // 기록은 본체가 멎은 뒤에 (예비 연산기 이름으로)
        }
        if (!hold && BackupCore && main) w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터 본체가 돌아왔다 — 예비 연산기가 {(w.Tick - BackupSince) / (float)SimTime.Minutes(1):0}분 붙잡고 있던 일을 넘겨받았다");
        BackupCore = hold;
        if (hold) BackupHours += dt;
        // 자기 절전: UPS · 전기가 모자란다 (원자로가 서고 배터리가 떨어진다) · 안전 모드
        var p = w.Power;
        bool shortPower = !p.ReactorOnline && p.BatteryPercent < 0.35f && p.BatteryFlow < -0.2f || p.DeficitSince >= 0 || p.Brownout && p.BatteryPercent < 0.3f;
        string sw = OnUps ? $"비상 전지로 버팀 ({Ups:0}분)" : shortPower ? $"전기가 모자라다 (배터리 {p.BatteryPercent * 100:0}%)" : "";
        bool want = sw != "" && (main || hold);
        if (want && !SelfSaving)
        {
            SelfSaving = true;
            SelfSavingWhy = sw;
            SelfSaves++;
            a.Book.Add(ActKind.Shed, m?.Body.Room, sw, "저도 전기를 씁니다 — 그만큼 생명유지 쪽으로", "제 연산을 줄임 (급하지 않은 일은 쉼)", "", "selfsave", SimTime.Minutes(30), 30f,
                (world, act) => (world.Power.BatteryPercent > 0.05f || world.Power.ReactorOnline ? 1 : 2, world.Power.BatteryPercent > 0.05f || world.Power.ReactorOnline ? "맞았다 — 필수 회로가 버텼다" : "배터리가 바닥났다"));
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {sw} — 제 연산도 줄입니다");
        }
        else if (!want && SelfSaving && (p.ReactorOnline && p.BatteryPercent > 0.45f && !OnUps || !(main || hold)))
        {
            SelfSaving = false;
            SelfSavingWhy = "";
            w.Log.Add(w.Tick, LogKind.Ship, "주 컴퓨터: 전기가 넉넉해져 연산을 되돌립니다");
        }
        if (m != null) m.Active = !(SelfSaving && (OnUps || !p.ReactorOnline || p.DeficitSince >= 0)); // 대기 전력 10%
        if (SelfSaving && m != null && m.Powered) SavedKwh += m.Spec.PowerDraw * 0.9f * dt;
    }

    private string? _takeover;

    /// <summary>본체가 멎은 뒤(MainOnline이 바뀐 다음) 넘겨받은 일을 예비 연산기 이름으로 적는다.</summary>
    internal void Flush()
    {
        if (_takeover is not string why) return;
        _takeover = null;
        var a = _w.Automation;
        a.Book.Add(ActKind.Module, a.Computer?.Body.Room, $"본체 멎음 — {why}", "예비 연산기가 넘겨받는다 (앞일 예측 · 원인 짚기는 쉰다)", "예비 연산기: 격벽 · 댐퍼 · 경보 · 급한 곳부터 전기 · 하던 소화", a.Rebooting ? "기다려 달라" : "본체를 고쳐 달라", "takeover", SimTime.Minutes(10), 10f,
            (world, act) => (world.Automation.CoreOnline ? 1 : -1, world.Automation.MainOnline ? "본체가 돌아왔다" : world.Automation.CoreOnline ? "예비 연산기가 붙잡고 있다" : "예비 연산기도 멎었다"));
    }

    /// <summary>재부팅 단계 (화면 · 기록): 0 아님 · 1 예비 코어 인수 · 2 기억 점검 · 3 주 코어 복귀 준비.</summary>
    public int RebootStage
    {
        get
        {
            var a = _w.Automation;
            if (!a.Rebooting || a.RebootUntil <= a.RebootStarted) return 0;
            float f = (_w.Tick - a.RebootStarted) / (float)(a.RebootUntil - a.RebootStarted);
            return f < 0.35f ? 1 : f < 0.8f ? 2 : 3;
        }
    }

    public static string StageName(int s) => s switch { 1 => "예비 연산기가 붙잡는 중", 2 => "기억을 확인하는 중", 3 => "본체를 다시 올리는 중", _ => "" };

    // ───────────── 데이터선 · 무선 · 구역 제어기 (방마다) ─────────────

    /// <summary>방마다 컴퓨터가 닿는 길: 2 데이터선(손까지) · 1 무선(감지 · 경보만 — 손은 사람이) · 0 못 본다.</summary>
    public int Reach(Room r) => r.Detached ? 0 : r.DataLinked ? 2 : ComputerV15.Relay(_w) && r.Powered ? 1 : 0;

    /// <summary>방마다 구역 소형 제어기가 사는가 (그 방 전기 · 데이터선이 끊겨도 제자리 반사만).</summary>
    public bool ZoneController(Room r) => !r.Detached && r.Powered && r.Type != RoomType.Corridor;

    internal void Hash(Action<long> I, Action<float> F)
    {
        F(Accuracy); F(Speed); I(Concurrency); F(Horizon); I(Upgrades); F(Ups); I(OnUps ? 1 : 0); I(SafeMode ? 1 : 0); I(SafeModes); I(BackupCore ? 1 : 0); I(Takeovers); I(SelfSaving ? 1 : 0); I(SelfSaves); I(StagedBack);
        foreach (var m in Enum.GetValues<ComputerModule>()) I(Grade(m));
    }
}

public sealed partial class AutomationSystem
{
    private ShipCore? _core;
    /// <summary>v16.20 주컴퓨터 몸: 품질 · UPS · 안전 모드 · 예비 코어 · 자기 절전.</summary>
    public ShipCore Core => _core ??= new ShipCore(_world);

    /// <summary>핵심 고리가 돈다 (주 코어 또는 예비 코어).</summary>
    public bool CoreOnline => MainOnline || _core is { BackupCore: true };

    /// <summary>v16.20 겪은 일로 자란다: 하루에 한 번, 모듈 표의 "왜"(겪은 일 · 기술 · 연구)가 맞는 모듈 하나의 등급을 올린다 (모듈은 첫날부터 다 있다).</summary>
    private long _growNext;
    internal void GrowModules()
    {
        var w = _world;
        if (w.Tick < _growNext || !MainOnline || V15NoAuto) return;
        _growNext = w.Tick + SimTime.Hours(1);
        if (w.Tick <= SimTime.TicksPerDay || w.Tick - _v15Last < SimTime.TicksPerDay) return;
        foreach (var r in ComputerV15.Rows)
        {
            if (Core.Grade(r.Module) >= 1 || ComputerV15.Why(w, r) is not string why) continue;
            _v15Last = w.Tick;
            Install(r.Module, why);
            return;
        }
        if (w.Tick <= SimTime.TicksPerDay * 2) return; // 생활 쪽 판단은 사흘째부터 (업데이트 버그도 그때부터)
        foreach (var r in ComputerV16.Rows)
        {
            if (Core.Grade(r.Module) >= 1 || ComputerV16.Why(w, r) is not string why) continue;
            _v15Last = w.Tick;
            Install(r.Module, why);
            MaybeBug(r.Module); // 업데이트는 가끔 버그를 데려온다
            return;
        }
    }

    internal void HashShip(Action<long> I, Action<float> F)
    {
        _core?.Hash(I, F);
        _triage?.Hash(I, F);
        _foresee?.Hash(I, F);
        _character?.Hash(I, F);
        _command?.Hash(I, F);
        Hash26(I, F); // v16.26
        Hash27(I, F); // v16.27
    }
}
