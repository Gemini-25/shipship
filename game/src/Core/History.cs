using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>연대기에 남는 사건의 종류.</summary>
public enum HistoryKind
{
    Incident,   // 사고의 첫 한 방 (운석·화재·큰 고장)
    Damage,     // 사고가 번진 것 (긴급 정지, 봉합 탈락, 회로 단락)
    Casualty,   // 쓰러짐·중상
    Death,
    Response,   // 사고 대응의 고비 (봉합, 진화, 구조, 수동 기동)
    Adaptation, // 원래 설계와 달라진 것 (구획 포기, 뜯기, Mk.1, 임시 배선, 용도 변경)
    Decision,   // 누가 무엇을 결정했고 누가 반대했는지
    Recovery,   // 사고 수습 (한 사고가 끝남)
    Upgrade,    // 개조·개량 (진화)
    Lesson,     // 교훈 (운영 방침이 바뀜)
    Bond,       // 전우가 됨
    Memory,     // 한 사람 안에 남은 것 (공포, 긴장, 침착해짐)
    Milestone,  // 출항, 저장한 날 같은 이정표
    Structure,  // v8: 방이 떨어져 나가고, 사출하고, 되찾고, 다시 붙인 일
    Maintenance, // v12.0: 당직 일지 — 교대를 건넌 기록, 잘못 짚은 부품, 전해지지 않은 기척
}

/// <summary>벽·방·설비·사람에 남는 이력 한 줄.</summary>
public readonly record struct Mark(long Tick, string Text);

/// <summary>연대기의 한 줄.</summary>
public sealed record HistoryEvent(long Tick, HistoryKind Kind, string Text, int RoomId, int[] CrewIds, Cell? At, int Episode);

/// <summary>
/// 사고 하나의 처음과 끝. 우주선이 위기에 들어서면 열리고, 한 시간 동안 조용하면 닫힌다.
/// 누가 대응했는지, 무엇을 잃었는지를 모아 두었다가 끝날 때 한 줄로 남긴다.
/// </summary>
public sealed class Episode
{
    public int Id { get; init; }
    public long Start { get; init; }
    public long End { get; set; } = -1;
    public string Cause { get; set; } = "";
    public int RoomId { get; set; } = -1;

    /// <summary>승무원 Id → 이 사고에서 해낸 대응 작업 수.</summary>
    public Dictionary<int, int> Responders { get; } = new();

    public List<string> Losses { get; } = new();
    public int Collapses { get; set; }
    public int Deaths { get; set; }
    public Dictionary<ItemKind, int> StockAtStart { get; } = new();

    public bool Open => End < 0;
    public float Hours(long now) => ((End < 0 ? now : End) - Start) / (float)SimTime.TicksPerHour;
}

/// <summary>
/// 운영 방침. 겪은 사고에서 배운 것이 평소 행동을 바꾼다 (비축 목표, 새로 궁리한 제작법).
/// 같은 우주선이라도 무엇을 겪었느냐에 따라 창고 모습이 달라진다.
/// </summary>
public sealed class Doctrine
{
    /// <summary>파공을 여러 번 겪었다 → 실링폼·구조재를 평소보다 넉넉히.</summary>
    public bool SealantReserve { get; set; }

    /// <summary>불을 여러 번 겪었다 → 빈 소화기를 다시 채우는 법을 궁리했다.</summary>
    public bool RefillExtinguishers { get; set; }

    /// <summary>정전을 여러 번 겪었다 → 바이오 연료를 더 비축.</summary>
    public bool FuelReserve { get; set; }

    /// <summary>방이 떨어져 나가거나 연결부가 여러 번 끊겼다 → 구조재를 더 비축 (v8).</summary>
    public bool StructureReserve { get; set; }

    /// <summary>멀쩡한 줄 알았던 연결부가 끊겼다 → 외벽 정기 검사를 이틀에서 하루로 (v8).</summary>
    public bool FrequentInspection { get; set; }

    /// <summary>배관이 여러 번 터졌다 → 금속판을 더 비축하고, 정수 탱크를 채워 둔다 (냉각수 보충용) (v9).</summary>
    public bool PipeReserve { get; set; }

    /// <summary>자동화가 꺼진 채 오래 버텼다 → 격벽·댐퍼를 손으로 다루는 법을 익혔다 (v9.2, 수동 조작 40% 빠르게).</summary>
    public bool ManualDrill { get; set; }

    /// <summary>v12.4 바닥에 물이 여러 번 찼다 → 퍼내기를 먼저 익혔다 (1.5배).</summary>
    public bool FloodDrill { get; set; }

    /// <summary>v12.4 병이 번졌다 → 앓는 사람은 마스크를 쓰고 따로 먹는다 (옮을 확률 절반).</summary>
    public bool Quarantine { get; set; }

    /// <summary>v10.10 비축 방침 (자원 장부가 정한다): 사고 직후엔 쓴 것을 서둘러 채우고, 극한엔 살 길부터.</summary>
    public StockMode Mode { get; set; }

    /// <summary>v13.2 방침(비축): 적게 0.75 · 보통 1 · 많이 1.35.</summary>
    public float StockScale { get; set; } = 1f;

    public int Target(Recipe r)
    {
        int t = r.Product switch
        {
            ItemKind.Sealant when SealantReserve => r.Target + 10,
            ItemKind.Structure => r.Target + (SealantReserve ? 3 : 0) + (StructureReserve ? 4 : 0),
            ItemKind.Fuel when FuelReserve => r.Target + 2,
            ItemKind.Plate when PipeReserve => r.Target + 4,
            _ => r.Target,
        };
        bool lifeline = r.Product is ItemKind.Sealant or ItemKind.Plate or ItemKind.Structure;
        if (StockScale != 1f && t > 0) t = Math.Max(1, (int)MathF.Round(t * StockScale));
        return Mode switch
        {
            StockMode.Recovery when lifeline => (int)MathF.Ceiling(t * 1.3f),
            StockMode.Extreme when lifeline => (int)MathF.Ceiling(t * 1.5f),
            StockMode.Extreme when r.Product is ItemKind.Fuel or ItemKind.Lubricant => Math.Max(1, t / 2),
            _ => t,
        };
    }

    public bool Knows(Recipe r) => r.Lesson switch
    {
        null => true,
        "fire" => RefillExtinguishers,
        _ => false,
    };

    public IEnumerable<string> Summary()
    {
        if (SealantReserve) yield return "실링폼·구조재 넉넉히";
        if (RefillExtinguishers) yield return "소화기 재충전";
        if (FuelReserve) yield return "연료 비축";
        if (StructureReserve) yield return "구조재 비축";
        if (FrequentInspection) yield return "매일 외벽 검사";
        if (PipeReserve) yield return "금속판·물 비축";
        if (ManualDrill) yield return "수동 조작 훈련";
        if (FloodDrill) yield return "침수 대비";
        if (Quarantine) yield return "격리 수칙";
    }
}

/// <summary>
/// 우주선의 역사. 큰 사건의 연대기, 사고 단위의 에피소드, 그리고 교훈의 재료가 되는 누적 횟수.
/// 벽·방·설비·사람 각각의 이력(Mark)은 그 물건에 직접 붙는다.
/// </summary>
public sealed class ShipHistory
{
    public List<HistoryEvent> Events { get; } = new();
    public int Version { get; private set; }

    public List<Episode> Episodes { get; } = new();
    public Episode? Current { get; private set; }

    /// <summary>마지막 위기가 끝난 틱 (평화가 얼마나 이어졌나).</summary>
    public long PeaceSince { get; private set; }

    /// <summary>출항한 틱.</summary>
    public long FoundedTick { get; private set; }

    private long _calmSince = -1;
    private string? _pendingCause;
    private long _pendingCauseTick = -1;
    private string? _lastCritical;
    private long _lastCriticalTick = -1;

    // ── 교훈의 재료: 무엇을 몇 번 겪었나 ──
    public int Meteors { get; set; }
    public int Breaches { get; set; }
    public int Fires { get; set; }
    public int Scrams { get; set; }
    public int CircuitFaults { get; set; }
    public int CriticalBreakdowns { get; set; }
    public int Collapses { get; set; }
    public int Deaths { get; set; }
    public int Abandons { get; set; }
    public float DarkHours { get; set; }
    public Dictionary<int, int> BreachesByRoom { get; } = new();
    public Dictionary<int, int> FiresByRoom { get; } = new();

    /// <summary>운석이 들어온 외벽 칸 (보강할 곳을 고를 때 쓴다).</summary>
    public List<Cell> ImpactCells { get; } = new();

    /// <summary>들어온 자리 → 보강할 때까지 그 자리에 운석이 몇 번 들어왔었나 (새로 맞으면 다시 보강할 수 있다).</summary>
    public Dictionary<Cell, int> ReinforcedAt { get; } = new();

    public Doctrine Doctrine { get; } = new();

    // ── 진화 ──
    public long LastUpgradeTick { get; set; } = -1_000_000;
    public int Upgrades { get; set; }
    public int BatteriesAdded { get; set; }

    /// <summary>v10.1: 굶주림을 겪고 더 짜 넣은 재배대 수.</summary>
    public int GrowBedsAdded { get; set; }

    /// <summary>v10.2: 칸막이로 나눈 방 수.</summary>
    public int Partitions { get; set; }
    /// <summary>v10.12: 걷은 칸막이 · 옮긴 설비.</summary>
    public int Unpartitions { get; set; }
    public int Relocations { get; set; }
    /// <summary>v11.1 분산 운영: 보조 작업대 · 예비 조타석.</summary>
    public int AuxWorkshops { get; set; }
    public int BackupHelms { get; set; }
    public int FeedersAdded { get; set; }

    /// <summary>개조 회의에서 부결된 안 (열쇠 → 다시 꺼낼 수 있는 틱).</summary>
    public Dictionary<string, long> UpgradeVetoedUntil { get; } = new();

    /// <summary>진행 중인 개조 계획 (끝나거나 조건이 사라질 때까지 같은 안을 밀고 간다).</summary>
    public string? PlannedUpgrade { get; set; }

    // ── 결정 ──
    /// <summary>절전 방침이 승인된 기간 (그동안은 회로를 내릴 때마다 다시 묻지 않는다).</summary>
    public long ShedApprovedUntil { get; set; } = -1;

    /// <summary>같은 일을 이미 정했다 (작업 열쇠 → 이때까지는 다시 묻지 않는다). 필요가 잠깐 사라졌다 다시 생겨도 회의를 되풀이하지 않는다.</summary>
    public Dictionary<string, long> ApprovedUntil { get; } = new();
    public int DecisionsMade { get; set; }
    public int DecisionsRejected { get; set; }

    public void Add(World w, HistoryKind kind, string text, Room? room = null, IEnumerable<CrewMember>? crew = null, Cell? at = null,
        bool log = false, int crewLog = -1)
    {
        var ids = crew?.Where(c => c != null).Select(c => c.Id).Distinct().ToArray() ?? Array.Empty<int>();
        Events.Add(new HistoryEvent(w.Tick, kind, text, room?.Id ?? -1, ids, at, Current?.Id ?? -1));
        if (Events.Count > 4000) Events.RemoveRange(0, Events.Count - 4000);
        Version++;
        if (log) w.Log.Add(w.Tick, kind is HistoryKind.Memory or HistoryKind.Bond ? LogKind.Life : LogKind.Ship, text, crewLog);
    }

    /// <summary>출항: 역사의 첫 줄.</summary>
    public void Founded(World w)
    {
        PeaceSince = w.Tick;
        FoundedTick = w.Tick;
        Add(w, HistoryKind.Milestone, $"{w.Ship.Name} 출항 — 승무원 {w.Crew.Count}명 ({string.Join("·", w.Crew.Select(c => c.Name))})", crew: w.Crew);
    }

    /// <summary>관찰자가 사고를 일으켰다 (다음 에피소드의 원인으로 적는다).</summary>
    public void NoteCause(World w, string cause)
    {
        if (Current != null && Current.Cause.Length > 0 && w.Tick - Current.Start < SimTime.Hours(2) && !Current.Cause.Contains(cause))
        {
            Current.Cause += " + " + cause;
            return;
        }
        _pendingCause = _pendingCause != null && w.Tick - _pendingCauseTick < SimTime.Minutes(10) ? _pendingCause + " + " + cause : cause;
        _pendingCauseTick = w.Tick;
    }

    internal void NoteCritical(World w, string text)
    {
        _lastCritical = text;
        _lastCriticalTick = w.Tick;
    }

    /// <summary>위기인지: 불, 새는 방, 쓰러진 사람, 원자로 정지, 멈춘 핵심 설비, 죽은 필수 회로.</summary>
    public static bool Crisis(World w, out string what)
    {
        var ship = w.Ship;
        what = "";
        if (w.Fire.Count > 0 && w.Fire.KnownFires().Any()) { what = "화재"; return true; }
        var leak = ship.Rooms.FirstOrDefault(r => r.Leaking && !r.Abandoned);
        if (leak != null) { what = $"{leak.Name} 감압"; return true; }
        if (w.Crew.Any(c => c.Down && !c.Dead)) { what = "쓰러진 사람"; return true; }
        if (!w.Power.ReactorOnline) { what = "원자로 정지"; return true; }
        var stopped = ship.Machines.FirstOrDefault(m => m.Spec.Critical && m.Stopped && !m.Body.Room.Abandoned && m.Body.Type != FurnitureType.PowerPanel);
        if (stopped != null) { what = $"{stopped.Name} 정지"; return true; }
        if (!w.Power.CircuitFed[0]) { what = "필수 회로 정전"; return true; }
        return false;
    }

    /// <summary>5분마다: 위기가 시작됐는지, 끝났는지.</summary>
    internal void Update(World w)
    {
        float dt = 5f / 60f;
        if (!w.Power.ReactorOnline) DarkHours += dt;

        bool crisis = Crisis(w, out var what);
        if (crisis)
        {
            _calmSince = -1;
            if (Current == null) Begin(w, what);
        }
        else if (Current != null)
        {
            if (_calmSince < 0) _calmSince = w.Tick;
            else if (w.Tick - _calmSince >= SimTime.Hours(1)) Finish(w);
        }
    }

    private void Begin(World w, string what)
    {
        string cause = _pendingCause != null && w.Tick - _pendingCauseTick < SimTime.Hours(1) ? _pendingCause
            : _lastCritical != null && w.Tick - _lastCriticalTick < SimTime.Hours(1) ? _lastCritical
            : what;
        _pendingCause = null;
        var ep = new Episode { Id = Episodes.Count + 1, Start = w.Tick, Cause = cause };
        foreach (var k in ItemKinds.All) ep.StockAtStart[k] = w.Ship.CountStored(k);
        Episodes.Add(ep);
        Current = ep;
    }

    private void Finish(World w)
    {
        var ep = Current!;
        ep.End = _calmSince;
        Current = null;
        PeaceSince = w.Tick;

        // 잃은 것: 사고 동안 쓴 수리재·소모품 (되찾기 어려운 것 위주)
        var used = new List<string>();
        foreach (var k in new[] { ItemKind.Sealant, ItemKind.Plate, ItemKind.Extinguisher, ItemKind.MedKit, ItemKind.Structure, ItemKind.Fuel })
        {
            int d = ep.StockAtStart.GetValueOrDefault(k) - w.Ship.CountStored(k);
            if (d >= 2) used.Add($"{ItemKinds.Name(k)} {d}");
        }
        var responders = ep.Responders.OrderByDescending(kv => kv.Value).Select(kv => w.Crew[kv.Key]).Where(c => !c.Dead).ToList();
        string who = responders.Count > 0 ? " · 대응 " + string.Join("·", responders.Take(4).Select(c => c.Name)) : "";
        string lost = ep.Losses.Count > 0 ? " · " + string.Join(", ", ep.Losses.Distinct().Take(3)) : "";
        string spent = used.Count > 0 ? " · 쓴 것 " + string.Join(" ", used.Take(4)) : "";
        string cas = ep.Deaths > 0 ? $" · 사망 {ep.Deaths}" : ep.Collapses > 0 ? $" · 쓰러짐 {ep.Collapses}" : "";
        float hours = ep.Hours(w.Tick);
        Add(w, HistoryKind.Recovery, $"수습: {ep.Cause} — {Hours(hours)}{who}{cas}{lost}{spent}", crew: responders, log: true);

        // 사고를 넘긴 사람은 조금 침착해진다 (많이 뛴 사람일수록)
        foreach (var r in responders)
            Memory.Steady(w, r, 0.03f + 0.01f * Math.Min(3, ep.Responders[r.Id]));

        // 함께 넘긴 사람들: 가까워지고, 여러 번 넘기면 전우가 된다
        for (int i = 0; i < responders.Count; i++)
        for (int j = i + 1; j < responders.Count; j++)
            Memory.ShareCrisis(w, responders[i], responders[j], ep);

        // 사고를 여러 번 겪으면 무엇을 준비해야 할지 배운다
        Learn(w);
    }

    private static string Hours(float h) => h < 1f ? $"{h * 60:0}분" : h < 48f ? $"{h:0.#}시간" : $"{h / 24f:0.#}일";

    /// <summary>교훈: 겪은 사고가 운영 방침을 바꾼다.</summary>
    private void Learn(World w)
    {
        var d = Doctrine;
        if (!d.FloodDrill && w.Moisture.Stats.Floods >= 2)
        {
            d.FloodDrill = true;
            Add(w, HistoryKind.Lesson, $"교훈: 바닥에 물이 {Times(w.Moisture.Stats.Floods)} 찼다 — 양동이·손펌프 자리를 정하고 퍼내기를 먼저 익혔다 (1.5배 빨리)", log: true);
        }
        if (!d.Quarantine && w.Disease.Stats.Infections >= 3)
        {
            d.Quarantine = true;
            Add(w, HistoryKind.Lesson, $"교훈: 병이 {w.Disease.Stats.Infections}명에게 번졌다 — 앓는 사람은 마스크를 쓰고 따로 먹는다 (옮을 확률 절반)", log: true);
        }
        if (!d.SealantReserve && Breaches >= 3)
        {
            d.SealantReserve = true;
            Add(w, HistoryKind.Lesson, $"교훈: 외벽이 {Times(Breaches)} 뚫렸다 — 실링폼과 구조재를 평소보다 넉넉히 쌓아 두기로 했다", log: true);
        }
        if (!d.RefillExtinguishers && Fires >= 2)
        {
            d.RefillExtinguishers = true;
            Add(w, HistoryKind.Lesson, $"교훈: 불이 {Times(Fires)} 났다 — 빈 소화기를 탄소 원료와 금속판으로 다시 채우는 법을 궁리했다", log: true);
        }
        if (!d.FuelReserve && (Scrams >= 2 || DarkHours >= 6f))
        {
            d.FuelReserve = true;
            Add(w, HistoryKind.Lesson, $"교훈: 원자로가 {Times(Math.Max(1, Scrams))} 멈췄다 — 보조 발전기 연료를 더 비축한다", log: true);
        }
        var st = w.Structure;
        if (!d.StructureReserve && (st.Detachments >= 1 || st.JointBreaks >= 3))
        {
            d.StructureReserve = true;
            Add(w, HistoryKind.Lesson, st.Detachments >= 1
                ? $"교훈: 방이 {Times(st.Detachments)} 떨어져 나갔다 — 연결부를 이을 구조재를 늘 넉넉히 둔다"
                : $"교훈: 연결부가 {Times(st.JointBreaks)} 끊어졌다 — 구조재를 늘 넉넉히 둔다", log: true);
        }
        var net = w.Piping;
        if (!d.PipeReserve && (net.Bursts >= 2 || net.CoolantLost >= 80f))
        {
            d.PipeReserve = true;
            Add(w, HistoryKind.Lesson, net.Bursts >= 2
                ? $"교훈: 배관이 {Times(net.Bursts)} 터졌다 — 관을 갈 금속판을 늘 넉넉히, 냉각수를 부을 물도 채워 둔다"
                : $"교훈: 냉각수를 {net.CoolantLost:0}L 잃었다 — 금속판과 물을 넉넉히 둔다", log: true);
        }
        var auto = w.Automation;
        if (!d.ManualDrill && (auto.OfflineHours >= 3f || auto.Outages >= 2))
        {
            d.ManualDrill = true;
            Add(w, HistoryKind.Lesson, auto.Outages >= 2
                ? $"교훈: 자동화가 {Times(auto.Outages)} 꺼졌다 — 격벽과 댐퍼를 손으로 다루는 훈련을 했다"
                : $"교훈: 자동화 없이 {auto.OfflineHours:0}시간을 버텼다 — 격벽과 댐퍼를 손으로 다루는 훈련을 했다", log: true);
        }
        if (!d.FrequentInspection && (st.SurpriseBreaks >= 1 || st.Detachments - st.Jettisons >= 1))
        {
            d.FrequentInspection = true;
            Add(w, HistoryKind.Lesson, "교훈: 멀쩡한 줄 알았던 연결부가 끊어졌다 — 외벽 정기 검사를 이틀에서 하루로 당긴다", log: true);
        }
    }

    /// <summary>사고 대응 작업을 끝낸 사람을 이번 에피소드에 적는다.</summary>
    internal void Responded(CrewMember c)
    {
        if (Current == null) return;
        Current.Responders[c.Id] = Current.Responders.GetValueOrDefault(c.Id) + 1;
    }

    internal void Lost(string what) => Current?.Losses.Add(what);

    // ─────────────────────────── 이야기로 풀기 ───────────────────────────

    public static string Times(int n) => n switch
    {
        1 => "한 번",
        2 => "두 번",
        3 => "세 번",
        4 => "네 번",
        5 => "다섯 번",
        6 => "여섯 번",
        7 => "일곱 번",
        8 => "여덟 번",
        9 => "아홉 번",
        10 => "열 번",
        _ => $"{n}번",
    };

    public static string Stamp(long tick) => $"{SimTime.Day(tick)}일차 {SimTime.Clock(tick)}";

    /// <summary>"이 벽은 세 번 뚫렸고, 두 번 용접했고, 한 번 통째로 갈았다 · 보강판을 덧댔다"</summary>
    public static string? WallStory(WallState w)
    {
        var parts = new List<string>();
        if (w.Breaches > 0) parts.Add($"{Times(w.Breaches)} 뚫렸고");
        if (w.Seals > 0) parts.Add($"{Times(w.Seals)} 실링폼으로 막았고");
        if (w.TotalWelds > 0) parts.Add($"{Times(w.TotalWelds)} 용접했고");
        if (w.Replacements > 0) parts.Add($"{Times(w.Replacements)} 통째로 갈았고");
        if (w.Reinforced) parts.Add("보강판을 덧댔다");
        if (parts.Count == 0) return w.Scorch > 0.3f ? "불에 그을린 자국이 있다" : null;
        string s = "이 벽은 " + string.Join(", ", parts);
        if (s.EndsWith("고")) s = s[..^1] + "다";
        return s;
    }

    /// <summary>"이 펌프는 고장 네 번 · Mk.1을 거쳐 정품으로 · 부품을 한 번 뜯겼다 · Mk.3 개량"</summary>
    public static string? MachineStory(Machine m)
    {
        var parts = new List<string>();
        int faults = m.FaultCount;
        if (faults > 0) parts.Add($"고장 {Times(faults)}");
        if (m.Substitutions > 0) parts.Add(m.Grade == MachineGrade.Mk1 ? $"Mk.1 임시품으로 버티는 중" : $"Mk.1을 거쳤다");
        if (m.Restores > 0) parts.Add($"정품 복원 {Times(m.Restores)}");
        if (m.TimesStripped > 0) parts.Add($"부품을 {Times(m.TimesStripped)} 뜯겼다");
        if (m.Rebuilds > 0) parts.Add($"파손 뒤 {Times(m.Rebuilds)} 다시 짜 맞췄다");
        if (m.Grade == MachineGrade.Mk3) parts.Add("Mk.3 개량형");
        if (m.Body.Improved && m.Body.Type == FurnitureType.Battery) parts.Add("항해 중에 증설했다");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    public static string? RoomStory(Room r)
    {
        var parts = new List<string>();
        if (r.Breaches > 0) parts.Add($"외벽이 {Times(r.Breaches)} 뚫렸다");
        if (r.Fires > 0) parts.Add($"불이 {Times(r.Fires)} 났다");
        if (r.TimesAbandoned > 0) parts.Add($"{Times(r.TimesAbandoned)} 포기했다" + (r.Abandoned ? "" : " (되찾음)"));
        if (r.Collapses > 0) parts.Add($"{Times(r.Collapses)} 사람이 쓰러졌다");
        if (r.Deaths > 0) parts.Add($"{r.Deaths}명이 죽었다");
        if (r.FormerPurposes.Count > 0) parts.Add($"{string.Join("·", r.FormerPurposes.Distinct())}이었다");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}

/// <summary>물건에 이력을 붙이는 도우미 (최근 것 몇 줄만 남긴다).</summary>
public static class MarkLog
{
    public const int Keep = 12;

    public static void Add(List<Mark> list, long tick, string text)
    {
        list.Add(new Mark(tick, text));
        if (list.Count > Keep) list.RemoveAt(0);
    }
}
