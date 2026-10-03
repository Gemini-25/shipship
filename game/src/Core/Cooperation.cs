using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.4 공간과 협력 — 공간이 일을 바꾸고, 둘이 하는 일은 서로를 기다린다.
//  · 작업장: 정비 · 수리 자리에 공구함 · 분해한 부품 · 뗀 덮개를 펼친다 (통로 칸이면 ObjectPhysics 통로 점유와 같은 규칙으로 길이 좁아진다).
//    급한 일로 자리를 비워도 그대로 남고, 돌아와 이어 한다 (손대지 않았으면 바로 · 누가 건드렸으면 헷갈린다 · 남이 이으면 어디까지 했나 더듬는다).
//  · 앞 상자: 꽉 찬 선반에서 큰 부품을 꺼내려면 앞 상자부터 바닥에 내려놓는다 (급하면 그대로 두고 간다 — 통로의 상자).
//  · 카트: 격벽 · 휜 문틀 · 에어락 문턱은 카트가 못 넘는다 — 짐을 내려 옮겨 싣는다.
//  · 옆 설비: 바로 옆에서 돌아가는 설비는 잠시 멈춰야 손이 들어간다 — 생명 유지 설비는 주 컴퓨터 승인 (거절되면 옆에서 조심조심).
//  · 둘이 하는 일: 무거운 부품(호이스트가 없으면) · 구조 패널은 한 명이 잡고 한 명이 체결 — 부른 짝이 안 오면 시간 상한 뒤 혼자(지그) 또는 보류 (교착 없음).
//    손발이 맞는 짝(관계 · 함께 한 횟수)일수록 먼저 오고 · 빨리 한다.
//  · 공구 · 시험대 예약: 먼저 온 사람 · 급한 일 먼저 · 오래 기다리면 투덜 — 주 컴퓨터가 충돌을 보고 순서를 제안한다.
// 줄 서기는 Queues.cs, 구경꾼은 Crowds.cs.

public enum SiteItemKind : byte { Toolbox, PartsTray, Panel, BigPart, FrontBox }
public enum SiteState : byte { Active, Left }

/// <summary>작업장 · 통로에 놓인 것 하나 (공구함 · 부품 쟁반 · 뗀 덮개 · 큰 부품 · 앞 상자).</summary>
public sealed class SiteItem
{
    public int Id { get; init; }
    public SiteItemKind Kind { get; init; }
    public Cell At { get; internal set; }
    /// <summary>칸 안의 자리 (-0.3~0.3) · 돌아간 각도 (화면).</summary>
    public Vector2 Off { get; internal set; }
    public float Angle { get; internal set; }
    /// <summary>지나가던 사람이 걷어차 흩어졌다.</summary>
    public bool Kicked { get; internal set; }
    public int Bulk => CoopSystem.BulkOf(Kind);
    public string Name => CoopSystem.NameOf(Kind);
}

/// <summary>펼쳐 둔 작업장: 일감 하나 · 설비 하나 · 펼친 사람 · 남은 것들.</summary>
public sealed class Worksite
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    internal WorkOrder Order { get; init; } = null!;
    public WorkKind Kind { get; init; }
    public int FurnitureId { get; init; } = -1;
    public string Label { get; init; } = "";
    public Cell Spot { get; init; }
    public Vector2 Face { get; init; }
    public int Owner { get; internal set; } = -1;
    public SiteState State { get; internal set; }
    public long Opened { get; init; }
    public long LeftAt { get; internal set; } = -1;
    /// <summary>왜 비웠나 (그때 맡은 일).</summary>
    public string Why { get; internal set; } = "";
    public bool Touched { get; internal set; }
    public int Toucher { get; internal set; } = -1;
    public int Resumes { get; internal set; }
    public float Progress { get; internal set; }
    /// <summary>비운 동안 펼친 사람 몫으로 맡아 둔다 (두 시간 · 그 사람이 못 오면 풀린다).</summary>
    public bool Reserved { get; internal set; }
    public List<SiteItem> Items { get; } = new();
}

/// <summary>앞 상자: 큰 부품을 꺼내느라 바닥에 내려놓은 상자.</summary>
public sealed class DugBox
{
    public SiteItem Item { get; init; } = null!;
    public int ShelfId { get; init; }
    public int By { get; init; }
    public long Since { get; init; }
    public long Until { get; internal set; }
    /// <summary>급해서 바닥에 둔 채 갔다.</summary>
    public bool Left { get; internal set; }
    public int TidyBy { get; internal set; } = -1;
    /// <summary>주 컴퓨터가 통로 상자를 알렸다 (믿는 사람이 먼저 치운다).</summary>
    public bool Warned { get; internal set; }
}

/// <summary>"누가 좀 잡아 줘": 둘이 하는 일의 부름.</summary>
public sealed class PairCall
{
    public int Id { get; init; }
    public int Caller { get; init; }
    public int OrderId { get; init; } = -1;
    public Cell Spot { get; init; }
    public Vector2 Face { get; init; }
    public long Opened { get; init; }
    /// <summary>이때까지 안 오면 혼자 하거나 보류한다 (교착 방지).</summary>
    public long Cap { get; internal set; }
    public int Helper { get; internal set; } = -1;
    public bool Arrived { get; internal set; }
    public bool Done { get; internal set; }
    public bool Urgent { get; init; }
    public string Part { get; init; } = "";
    public string Outcome { get; internal set; } = "";
    public long HelperSeen { get; internal set; }
}

/// <summary>공구 · 시험대 예약판: 쓰는 사람 · 기다리는 사람 (온 순서).</summary>
public sealed class BenchBook
{
    public int FurnitureId { get; init; }
    public string Name { get; init; } = "";
    public int User { get; internal set; } = -1;
    public long UserSince { get; internal set; }
    public bool UserUrgent { get; internal set; }
    public List<int> Waiting { get; } = new();
    public List<long> Since { get; } = new();
    public long Advised { get; internal set; } = -1;
}

/// <summary>옆에서 일하느라 잠시 멈춘 설비.</summary>
public sealed class PausedFixture
{
    public int FurnitureId { get; init; }
    public string Name { get; init; } = "";
    public int By { get; init; }
    public long Since { get; init; }
    public long Until { get; internal set; }
    /// <summary>주 컴퓨터가 승인했다 (시간이 끝나면 컴퓨터가 다시 켠다).</summary>
    public bool Approved { get; init; }
    /// <summary>끈 사람이 잊고 갔다 (컴퓨터가 알린다).</summary>
    public bool Forgotten { get; internal set; }
    public bool Done { get; internal set; }
}

/// <summary>좁은 문 앞에서 카트 짐을 옮겨 싣는 중.</summary>
public sealed class CartTransfer
{
    public int Crew { get; init; }
    public int DoorId { get; init; }
    public Cell At { get; init; }
    public long Since { get; init; }
    public long Until { get; init; }
    public bool Helped { get; init; }
}

public sealed class CoopStats
{
    public int Sites, Left, Resumed, Confused, Inherited, Kicks, Packed, Digs, BoxesLeft, BoxesTidied, Transfers, TransfersHelped,
        Pauses, Approved, Denied, Careful, Forgotten, Restored, Calls, Arrived, Solos, Holds, Late, PairJobs, GoodPairs,
        BenchWaits, Grumbles, Preempts, ComputerOrders, AisleWarns, Narrowed;
    public float WaitMinutes, BenchMinutes;
    public string Line() =>
        $"작업장 {Sites}(비움 {Left} · 이어 함 {Resumed} · 헷갈림 {Confused} · 남이 이음 {Inherited} · 걷어참 {Kicks} · 챙김 {Packed}) · 앞 상자 {Digs}(바닥에 둠 {BoxesLeft} · 되돌림 {BoxesTidied}) · " +
        $"카트 옮겨 싣기 {Transfers}(거듦 {TransfersHelped}) · 옆 설비 멈춤 {Pauses}(승인 {Approved} · 거절 {Denied} · 조심 {Careful} · 잊음 {Forgotten} · 다시 켬 {Restored}) · " +
        $"짝 부름 {Calls}(옴 {Arrived} · 혼자 {Solos} · 보류 {Holds} · 늦게 옴 {Late} · 함께 끝냄 {PairJobs} · 손발이 맞는 짝 {GoodPairs}) · 기다림 {WaitMinutes:0}분 · " +
        $"시험대 대기 {BenchWaits}(투덜 {Grumbles} · 급한 일 먼저 {Preempts} · 컴퓨터 순서 {ComputerOrders} · {BenchMinutes:0}분) · 통로 경고 {AisleWarns} · 좁아진 통로 {Narrowed}";
}

public sealed partial class CoopSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 1741));

    public QueueSystem Queues { get; }
    public CrowdSystem Crowds { get; }
    public CoopStats Stats { get; } = new();

    public List<Worksite> Sites { get; } = new();
    public List<DugBox> Boxes { get; } = new();
    public List<PairCall> Calls { get; } = new();
    public List<BenchBook> Benches { get; } = new();
    public List<PausedFixture> Paused { get; } = new();
    public List<CartTransfer> Transfers { get; } = new();
    /// <summary>둘이 함께 끝낸 횟수 (작은 id, 큰 id).</summary>
    public SortedDictionary<long, int> PairCounts { get; } = new();

    private readonly Dictionary<int, Session> _sess = new();
    private readonly Dictionary<int, DugBox> _digging = new();
    private readonly Dictionary<long, long> _holds = new();
    private readonly Dictionary<int, int> _holdN = new(); // v16.24 짝을 못 구해 보류한 횟수 (일마다)
    private readonly Dictionary<long, long> _passed = new();
    private int _nextId = 1;
    private byte[] _slow = Array.Empty<byte>();
    private long _nextComputer;
    private long _nextStale; // v16.24 손대지 못한 채 맡고만 있는 일
    private readonly Dictionary<int, (float prog, long since, int who)> _stale = new();
    public int StaleDrops;

    /// <summary>시험용: 아무도 거들러 오지 않는다 (교착 방지 확인).</summary>
    public bool NoHelpers { get; set; }

    public CoopSystem(World w)
    {
        _w = w;
        Queues = new QueueSystem(w, this);
        Crowds = new CrowdSystem(w, this);
    }

    public int NextId() => _nextId++;

    // ─────────────────────────────── 표 ───────────────────────────────

    public static int BulkOf(SiteItemKind k) => k switch
    {
        SiteItemKind.Toolbox => 3, SiteItemKind.PartsTray => 4, SiteItemKind.Panel => 6, SiteItemKind.BigPart => 6, _ => 8, // 앞 상자 = 종이 상자와 같다
    };

    public static string NameOf(SiteItemKind k) => k switch
    {
        SiteItemKind.Toolbox => "펼친 공구함", SiteItemKind.PartsTray => "분해한 부품 쟁반", SiteItemKind.Panel => "뗀 덮개", SiteItemKind.BigPart => "큰 부품", _ => "꺼내 둔 앞 상자",
    };

    /// <summary>작업장을 펼치는 일 (설비 · 문 · 배선 · 관에 손을 대고 오래 붙어 있는 일).</summary>
    public static bool SiteKind(WorkKind k) => k is WorkKind.Repair or WorkKind.Maintain or WorkKind.PreventiveCheck or WorkKind.Calibrate or WorkKind.Upgrade
        or WorkKind.RestoreGrade or WorkKind.InstallSubstitute or WorkKind.ReplacePanel or WorkKind.RepairDoor or WorkKind.FixLights or WorkKind.Rewire
        or WorkKind.Reline or WorkKind.RepairRadiator or WorkKind.PatchPipe or WorkKind.ReplacePipe or WorkKind.RepairRobot or WorkKind.RepairNet;

    /// <summary>같이 쓰는 공구 · 시험대 (한 번에 한 사람).</summary>
    public static bool BenchType(FurnitureType t) => t is FurnitureType.Workbench or FurnitureType.Fabricator or FurnitureType.PartTestBench or FurnitureType.Lathe
        or FurnitureType.SolderStation or FurnitureType.CalibrationRig or FurnitureType.DiagnosticScanner or FurnitureType.Refinery;

    /// <summary>옆에서 손을 넣으려면 멈춰야 하는 설비 (도는 날개 · 열 · 고압).</summary>
    public static bool Noisy(FurnitureType t) => t is FurnitureType.CoolantPump or FurnitureType.AuxGenerator or FurnitureType.Stove or FurnitureType.Oven
        or FurnitureType.OxygenGenerator or FurnitureType.WaterRecycler or FurnitureType.HeatExchanger or FurnitureType.Lathe or FurnitureType.PowerPanel
        or FurnitureType.Battery or FurnitureType.CapacitorBank or FurnitureType.Scrubber or FurnitureType.Fabricator or FurnitureType.Refinery or FurnitureType.EngineCore
        or FurnitureType.AirPurifier or FurnitureType.DishWasher or FurnitureType.WashingMachine or FurnitureType.Dehumidifier or FurnitureType.AirlockPump;

    /// <summary>멈추려면 주 컴퓨터 승인이 드는 설비 (숨 · 물 · 냉각 · 전기).</summary>
    public static bool Critical(FurnitureType t) => t is FurnitureType.OxygenGenerator or FurnitureType.WaterRecycler or FurnitureType.CoolantPump or FurnitureType.Scrubber
        or FurnitureType.PowerPanel or FurnitureType.Battery or FurnitureType.CapacitorBank or FurnitureType.EngineCore or FurnitureType.HeatExchanger or FurnitureType.AirlockPump;

    /// <summary>큰 부품 (꽉 찬 선반에서는 앞 상자부터 치워야 꺼낸다).</summary>
    public static bool Bulky(ItemKind k) => k is ItemKind.Motor or ItemKind.Pump or ItemKind.Plate or ItemKind.Structure or ItemKind.ReactorControl or ItemKind.Impeller
        or ItemKind.PowerController or ItemKind.Fuel or ItemKind.Coupling or ItemKind.Gear;

    /// <summary>카트가 못 넘는 문: 격벽 문틀 · 휜 문틀 · 손으로 반만 여는 문 · 에어락 · 감압실 문턱.</summary>
    public static bool NarrowForCart(Door d) => d.Bulkhead || d.Bent > 0.15f || d.IsExternal || d.MotorBroken && !d.MotorMk1
        || d.RoomA?.Type is RoomType.Airlock or RoomType.QuarantineLock or RoomType.Decon || d.RoomB?.Type is RoomType.Airlock or RoomType.QuarantineLock or RoomType.Decon;

    private static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    public int PairCount(int a, int b) => PairCounts.TryGetValue(PairKey(a, b), out var n) ? n : 0;

    private CrewMember? CrewById(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }
    private Furniture? Furn(int id) { foreach (var f in _w.Ship.Furniture) if (f.Id == id) return f; return null; }

    // ─────────────────────────────── 틱 ───────────────────────────────

    /// <summary>성능 점검: Update에 쓴 시간 (Stopwatch 틱 — 시뮬레이션에는 쓰지 않는다).</summary>
    public static long UpdateTicks;

    public void Update(float dt)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Step(dt);
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    private void Step(float dt)
    {
        var w = _w;
        long now = w.Tick;
        EndSessions(now);
        UpdateSites(now);
        UpdateBoxes(now);
        UpdateCalls(now);
        UpdateBenches(now, dt);
        UpdatePaused(now);
        Transfers.RemoveAll(t => t.Until < now - SimTime.Minutes(10));
        if (_holdN.Count > 16) foreach (var k in _holdN.Keys.Where(id => !w.Board.OpenUnsorted.Any(x => x.Id == id)).ToList()) _holdN.Remove(k);
        foreach (var (k, until) in _holds.ToList())
        {
            if (until <= now) { _holds.Remove(k); continue; }
            var o = w.Board.Open.FirstOrDefault(x => x.Id == (int)k);
            if (o != null && o.BlockedUntil < until) o.BlockedUntil = until; // 다른 계획이 짧게 미룬 것을 다시 늘린다 (보류)
        }
        if (_passed.Count > 64) foreach (var k in _passed.Where(kv => now - kv.Value > SimTime.Minutes(5)).Select(kv => kv.Key).ToList()) _passed.Remove(k);
        Queues.Update(dt);
        Crowds.Update(dt);
        if (now >= _nextComputer) { _nextComputer = now + SimTime.Minutes(5); ComputerWatch(); }
        if (now >= _nextStale) { _nextStale = now + SimTime.Minutes(10); Stale(now); }
        RebuildSlow();
    }

    // ───────────────────────────── 작업 세션 ─────────────────────────────

    private const int PBench = 0, PNeighbor = 1, PSetup = 2, PPartner = 3, PWork = 4;

    private sealed class Session
    {
        public Job Job = null!;
        public WorkOrder? Order;
        public Furniture? F;
        public Cell Spot;
        public Vector2 Face;
        public bool Relevant, Urgent, SiteWork, NeedPair, Solo, Careful, Ordered, Waiting, Ended, Finished;
        public bool Paired; public long HoldFrom = -1; // 통합7 조여 붙이는 동안만 잡아 준다
        public bool Held; // v16.24 짝이 안 와 보류 — 자리를 맡아 두지 않는다 (다른 사람 · 나중에)
        public int Phase;
        public long Until = -1, BoostUntil, WaitFrom = -1;
        public float Boost = 1f, SetupMin;
        public int Grumbles;
        public Worksite? Site;
        public BenchBook? Bench;
        public Furniture? Neighbor;
        public PairCall? Call;
        public PausedFixture? Pause;
    }

    /// <summary>WorkToil 한 틱의 배율: 0 = 기다린다(예약 · 옆 설비 · 펼치기 · 짝), 음수 = 그만둔다(보류), 그 밖 = 속도 (이어 함 · 헷갈림 · 혼자 · 짝).</summary>
    public float WorkMul(CrewMember c, WorkOrder? resume, Vector2? face)
    {
        var job = c.Job;
        if (job == null) return 1f;
        if (!_sess.TryGetValue(c.Id, out var s) || s.Job != job)
        {
            if (s != null) EndSession(c, s, _w.Tick);
            s = Begin(c, job, resume, face);
            _sess[c.Id] = s;
        }
        if (!s.Relevant) return 1f;
        long now = _w.Tick;
        if (s.Phase == PBench)
        {
            if (!BenchTurn(c, s, now)) return Wait(c, s, now);
            s.Phase = PNeighbor;
        }
        if (s.Phase == PNeighbor)
        {
            if (s.Neighbor != null)
            {
                if (s.Until < 0) { s.Until = now + SimTime.Minutes(1); c.Say(_w, Persona.Say(c, $"옆 {s.Neighbor.Label}부터 멈춰야 손이 들어가")); }
                if (now < s.Until) { c.Pose = Pose.Working; Locomotion.Face(c, s.Neighbor.Center); return 0f; }
                DecideNeighbor(c, s, now);
            }
            s.Phase = PSetup; s.Until = -1;
        }
        if (s.Phase == PSetup)
        {
            if (s.SetupMin > 0f)
            {
                if (s.Until < 0) s.Until = now + SimTime.Minutes(s.SetupMin);
                if (now < s.Until) { c.Pose = Pose.Working; return 0f; }
            }
            if (s.SiteWork && s.Site == null) OpenSite(c, s, now);
            s.Phase = s.NeedPair ? PPartner : PWork; s.Until = -1;
        }
        if (s.Phase == PPartner)
        {
            float r = PartnerStep(c, s, now);
            if (r <= 0f) return r;
            s.Phase = PWork;
        }
        if (s.Waiting) { s.Waiting = false; c.Pose = Pose.Working; Locomotion.Face(c, s.Face); }
        float m = 1f;
        if (s.BoostUntil > now) m *= s.Boost;
        if (s.Solo) m *= 0.6f; // 혼자 지그로 고정하고 한다
        if (s.Careful) m *= 0.8f; // 옆에서 돌아가는 설비 곁에서 조심조심
        if (s.Call is { Arrived: true, Done: false } call)
        {
            if (HelperHere(call))
            {
                call.HelperSeen = now;
                m *= 1.15f + 0.03f * Math.Min(5, PairCount(c.Id, call.Helper)); // 손발이 맞는 짝일수록
                // 통합7 무거운 부품은 자리에 맞춰 조여 붙일 때까지만 잡아 준다 — 한 시간 넘는 수리 내내 붙들려 있다 끼니때 떠나던 것
                if (s.HoldFrom < 0) s.HoldFrom = now;
                else if (now - s.HoldFrom >= SimTime.Minutes(20))
                {
                    call.Done = true; call.Outcome = "함께 조여 붙였다 — 나머지는 혼자";
                    PairDone(c, call, now);
                    s.Paired = true;
                    c.Say(_w, Persona.Say(c, "됐어, 붙었다 — 고마워, 나머진 내가 할게"));
                }
            }
            else if (now - call.HelperSeen > SimTime.Minutes(2))
            {
                call.Done = true; call.Outcome = "잡아 주던 사람이 갔다 — 혼자 마저";
                s.Solo = true;
            }
        }
        if (s.Site != null && resume != null) s.Site.Progress = resume.Progress;
        return m;
    }

    private float Wait(CrewMember c, Session s, long now)
    {
        if (!s.Waiting) { s.Waiting = true; s.WaitFrom = now; }
        if (c.Pose == Pose.Working) c.Pose = Pose.Standing;
        Stats.WaitMinutes += 60f / SimTime.TicksPerHour;
        return 0f;
    }

    private Session Begin(CrewMember c, Job job, WorkOrder? resume, Vector2? face)
    {
        var w = _w;
        var o = job.Order ?? resume;
        var s = new Session { Job = job, Order = o, Urgent = job.Urgent, Spot = c.Cell, Face = face ?? c.Position };
        if (face is Vector2 fv && w.Ship.FurnitureAt(Cell.FromPosition(fv)) is Furniture ff) s.F = ff;
        s.F ??= job.Target;
        bool bench = s.F != null && BenchType(s.F.Type) && !s.F.Stowed;
        bool site = o != null && SiteKind(o.Kind) && (o.Robot == null || RobotSystem.Joinable(o)) && !c.Outside && !o.External && c.Room != null; // 통합7 로봇이 맡은 정비에 사람이 손을 보태도 그 사람은 제 공구를 펼친다 (한빛호 냉각 펌프)
        if (!bench && !site) { s.Relevant = false; return s; }
        s.Relevant = true;
        s.Phase = bench ? PBench : PNeighbor;
        if (bench) s.Bench = BookOf(s.F!);
        if (!site) return s;
        s.SiteWork = true;
        s.NeedPair = NeedsPartner(c, o!, s.F);
        s.Neighbor = FindNeighbor(s);
        long now = w.Tick;
        var ex = Sites.FirstOrDefault(x => x.OrderId == o!.Id);
        if (ex == null) { s.SetupMin = c.Job?.Urgent == true ? 0.6f : 1.5f; return s; } // 공구를 펼친다
        s.Site = ex;
        bool same = ex.Owner == c.Id;
        if (ex.State == SiteState.Active && same) return s; // 같은 자리 그대로 (계획만 다시 짰다)
        bool longGap = ex.LeftAt >= 0 && now - ex.LeftAt > SimTime.Minutes(5);
        ex.State = SiteState.Active;
        ex.Reserved = false;
        if (same && !longGap && !ex.Touched) return s;
        ex.Resumes++;
        string pct = $"{(int)(o!.Progress * 100f)}%";
        if (same && !ex.Touched)
        {
            s.Boost = 1.2f; s.BoostUntil = now + SimTime.Minutes(15);
            Stats.Resumed++;
            c.Say(w, Persona.Say(c, "놓고 간 그대로네 — 이어서 하자"));
            w.Log.Add(now, LogKind.Work, $"{Ko.EulReul(ex.Label)} 아까 펼쳐 둔 자리에서 이어 한다 ({pct}) — 공구 · 뜯은 부품이 그대로다", c.Id);
            if (Furn(ex.FurnitureId)?.Machine is Machine m0) MarkLog.Add(m0.Marks, now, $"{c.Name}: 비웠던 자리로 돌아와 이어 함 ({pct})");
        }
        else if (same)
        {
            s.SetupMin = 2.5f; s.Boost = 0.75f; s.BoostUntil = now + SimTime.Minutes(20);
            Stats.Confused++;
            var who = CrewById(ex.Toucher);
            c.Say(w, Persona.Say(c, "어? 누가 만졌지… 볼트가 어디 갔어"));
            w.Log.Add(now, LogKind.Life, $"{ex.Label} 자리로 돌아오니 펼쳐 둔 부품이 흩어져 있다 — 헷갈려 다시 맞춰 본다" + (who != null ? $" ({Ko.IGa(who.Name)} 지나가다 건드렸다)" : ""), c.Id);
            if (who != null && who != c) { c.ChangeAffinity(who, -0.02f); _w.Brain2.Emotions.Feel(c, Feeling.Anger, 0.12f, "펼쳐 둔 부품을 누가 건드렸다", who); }
            MarkLog.Add(c.Memory.Marks, now, $"{ex.Label} 정비 자리 — 돌아오니 부품이 흩어져 있었다");
        }
        else
        {
            var prev = CrewById(ex.Owner);
            s.SetupMin = 1f; s.Boost = 0.85f; s.BoostUntil = now + SimTime.Minutes(15);
            Stats.Inherited++;
            c.Say(w, Persona.Say(c, prev != null ? $"{Ko.IGa(prev.Name)} 뜯어 놓은 걸 — 어디까지 했지?" : "누가 뜯어 놓은 걸 — 어디까지 했지?"));
            w.Log.Add(now, LogKind.Work, $"{Ko.IGa(prev?.Name ?? "누가")} 펼쳐 두고 간 {ex.Label} 자리를 이어받는다 ({pct}) — 남의 순서라 더듬는다", c.Id);
            ex.Owner = c.Id;
        }
        ex.Touched = false; ex.Toucher = -1;
        return s;
    }

    private void EndSessions(long now)
    {
        foreach (var c in _w.Crew)
        {
            if (!_sess.TryGetValue(c.Id, out var s)) continue;
            if (s.Job == c.Job && !c.Dead) continue;
            EndSession(c, s, now);
            _sess.Remove(c.Id);
        }
    }

    /// <summary>WorkToil.End: 손일 단계가 끝났다 (다 했나 · 끊겼나). 시험대는 바로 놓는다.</summary>
    public void WorkEnded(CrewMember c, bool finished)
    {
        if (!_sess.TryGetValue(c.Id, out var s) || s.Job != c.Job) return;
        s.Ended = true;
        s.Finished = finished;
        if (s.Bench is BenchBook b) { ReleaseBench(b, c.Id); RemoveWaiter(b, c.Id); }
    }

    private void EndSession(CrewMember c, Session s, long now)
    {
        var w = _w;
        if (s.Bench is BenchBook b) { if (b.User == c.Id) ReleaseBench(b, c.Id); RemoveWaiter(b, c.Id); }
        if (s.Call is PairCall call && !call.Done)
        {
            call.Done = true;
            call.Outcome = s.Order?.Closed == true ? "함께 끝냈다" : "일이 끊겼다";
        }
        if (s.Call is { Arrived: true } pc && pc.Helper >= 0 && s.Order?.Closed == true && !s.Paired) PairDone(c, pc, now);
        if (s.Pause is PausedFixture pa && !pa.Done && pa.Approved) Restore(pa, c, "다시 켰다 — 끝났다고 알리자 컴퓨터가 돌렸다");
        else if (s.Pause is PausedFixture p && !p.Done && !p.Approved)
        {
            // 끈 사람이 다시 켠다 — 잘 잊는 사람은 그대로 두고 간다 (컴퓨터가 알린다)
            if (Life.Has(c, Habit.Forgetful) || Life.Has(c, Habit.Messy) && R.Chance(0.4f)) { p.Forgotten = true; Stats.Forgotten++; }
            else Restore(p, c, "일을 마치고 다시 켰다");
        }
        var site = s.Site;
        if (site == null || site.Owner != c.Id) return;
        if (s.Order == null || s.Order.Closed || s.Finished) { Pack(site, c, now); return; }
        if (c.Job?.Order == s.Order && !c.Dead) return; // 같은 일을 다시 계획했을 뿐
        if (site.State == SiteState.Left) return;
        site.State = SiteState.Left;
        site.LeftAt = now;
        // 급하지 않은 일은 펼친 사람 몫으로 맡아 둔다 (곧 돌아온다) — 다른 사람은 남의 펼친 자리에 손대지 않는다
        if (!s.Held && s.Order.Urgency < 0.9f && !c.Dead && (s.Order.Assignee == null || s.Order.Assignee == c)) { s.Order.Assignee = c; site.Reserved = true; }
        site.Why = c.Dead ? "쓰러졌다" : c.Down ? "다쳐 쓰러졌다" : c.Job?.Label ?? "자리를 비웠다";
        Stats.Left++;
        bool urgentCall = c.Job?.Urgent == true || Crisis.Acting(w) || c.Job?.Activity is EvacuateActivity or MusterActivity;
        string pct = $"{(int)(s.Order.Progress * 100f)}%";
        w.Log.Add(now, LogKind.Life, urgentCall
            ? $"{Ko.EulReul(site.Label)} {pct}에서 손을 놓고 {Ko.EuRo(site.Why)} — 공구 · 뜯은 부품은 그 자리에 그대로"
            : $"{site.Label} 자리를 비운다 ({pct} · {site.Why}) — 펼친 공구는 그대로 둔다", c.Id);
        if (Furn(site.FurnitureId)?.Machine is Machine m) MarkLog.Add(m.Marks, now, $"{c.Name}: {pct}에서 손을 놓음 ({site.Why}) — 공구 · 부품을 둔 채");
    }

    // ───────────────────────────── 작업장 ─────────────────────────────

    private void OpenSite(CrewMember c, Session s, long now)
    {
        var w = _w;
        var o = s.Order!;
        var site = new Worksite
        {
            Id = NextId(), OrderId = o.Id, Order = o, Kind = o.Kind, FurnitureId = s.F?.Id ?? -1, Label = s.F?.Label ?? o.Target.Label,
            Spot = s.Spot, Face = s.Face, Owner = c.Id, Opened = now,
        };
        var kinds = new List<SiteItemKind> { SiteItemKind.Toolbox };
        bool machine = s.F?.Machine != null;
        if (o.Kind is WorkKind.Repair or WorkKind.Maintain or WorkKind.Upgrade or WorkKind.RestoreGrade or WorkKind.InstallSubstitute or WorkKind.RepairRobot || machine) kinds.Add(SiteItemKind.PartsTray);
        if (machine && o.Kind is WorkKind.Repair or WorkKind.Maintain or WorkKind.Upgrade or WorkKind.PreventiveCheck or WorkKind.RestoreGrade or WorkKind.InstallSubstitute
            || o.Kind is WorkKind.ReplacePanel or WorkKind.RepairDoor or WorkKind.FixLights) kinds.Add(SiteItemKind.Panel);
        if (s.NeedPair && o.Kind == WorkKind.Repair) kinds.Add(SiteItemKind.BigPart);
        var cells = LayoutCells(s.Spot, kinds.Count);
        for (int i = 0; i < kinds.Count; i++)
        {
            var at = cells[Math.Min(i, cells.Count - 1)];
            float a = (site.Id * 37 + i * 61) % 100 / 100f;
            site.Items.Add(new SiteItem { Id = NextId(), Kind = kinds[i], At = at, Off = new Vector2((a - 0.5f) * 0.3f, ((a * 7f) % 1f - 0.5f) * 0.3f), Angle = (a - 0.5f) * 0.8f });
        }
        Sites.Add(site);
        s.Site = site;
        Stats.Sites++;
        if (site.Items.Any(t => w.Matter.InAisle(t.At))) Stats.Narrowed++;
    }

    /// <summary>작업 칸 둘레에서 펼칠 칸: 빈 바닥 · 문 아닌 곳 · 통로가 아닌 곳을 먼저 (없으면 통로에 — 길이 좁아진다).</summary>
    private List<Cell> LayoutCells(Cell spot, int n)
    {
        var ship = _w.Ship;
        var room = ship.RoomAt(spot);
        var cand = new List<(Cell c, int key)>();
        for (int r = 1; r <= 2 && cand.Count < n; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var c = new Cell(spot.X + dx, spot.Y + dy);
                    if (!ship.IsOpenFloor(c) || ship.DoorAt(c) != null || ship.RoomAt(c) != room) continue;
                    if (Sites.Any(s => s.Items.Any(t => t.At == c)) || cand.Any(x => x.c == c)) continue;
                    int key = (_w.Matter.InAisle(c) ? 1000 : 0) + r * 100 + Math.Abs(dx) + Math.Abs(dy) * 2;
                    cand.Add((c, key));
                }
        var list = cand.OrderBy(x => x.key).ThenBy(x => x.c.Y).ThenBy(x => x.c.X).Select(x => x.c).Take(n).ToList();
        if (list.Count == 0) list.Add(spot);
        return list;
    }

    private void UpdateSites(long now)
    {
        var w = _w;
        for (int i = Sites.Count - 1; i >= 0; i--)
        {
            var site = Sites[i];
            if (site.Order.Closed)
            {
                var owner = CrewById(site.Owner);
                Pack(site, owner, now);
                continue;
            }
            if (site.State != SiteState.Left) continue;
            if (site.Reserved && (now - site.LeftAt > SimTime.Hours(2) || CrewById(site.Owner) is not { CanAct: true, Away: false }))
            {
                site.Reserved = false; // 못 돌아온다 — 다른 사람이 이어받을 수 있게
                if (site.Order.Assignee?.Id == site.Owner && site.Order.Assignee.Job?.Order != site.Order) site.Order.Assignee = null;
            }
            else if (site.Reserved && site.Order.Assignee == null && CrewById(site.Owner) is CrewMember own) site.Order.Assignee = own; // 다시 불려 갔어도 제 몫으로 둔다
            // 비워 둔 자리를 지나가는 사람: 급하면 걷어차고, 아니어도 가끔 건드린다
            foreach (var c in w.Crew)
            {
                if (c.Id == site.Owner || !c.CanAct || c.Outside || c.CarriedBy != null || !c.IsMoving) continue;
                var cell = c.Cell;
                foreach (var t in site.Items)
                {
                    if (t.At != cell) continue;
                    float p = c.Job?.Urgent == true || c.Dashing ? 0.6f : 0.12f;
                    if (!R.Chance(p)) break;
                    t.Kicked = true;
                    t.Off = new Vector2(Math.Clamp(t.Off.X + R.Range(-0.25f, 0.25f), -0.38f, 0.38f), Math.Clamp(t.Off.Y + R.Range(-0.25f, 0.25f), -0.38f, 0.38f));
                    t.Angle += R.Range(-1.2f, 1.2f);
                    if (!site.Touched)
                    {
                        Stats.Kicks++;
                        w.Log.Add(now, LogKind.Life, $"{(c.Job?.Urgent == true ? "급히 " : "")}지나가다 {site.Label} 옆에 펼쳐 둔 {Ko.EulReul(t.Name)} 건드렸다", c.Id);
                    }
                    site.Touched = true; site.Toucher = c.Id;
                    break;
                }
            }
        }
    }

    private void Pack(Worksite site, CrewMember? by, long now)
    {
        Sites.Remove(site);
        Stats.Packed++;
        if (by != null && site.State == SiteState.Left && site.Order.Closed && site.Owner == by.Id && by.Room != null)
            MarkLog.Add(by.Memory.Marks, now, $"{site.Label} — 펼쳐 둔 공구를 챙겼다");
    }

    /// <summary>ChoresActivity.Appeal: 펼쳐 두고 간 사람은 제 자리로 돌아오고, 남의 펼친 자리는 조금 꺼린다.</summary>
    public float SiteBias(CrewMember c, WorkOrder o)
    {
        if (Sites.Count == 0) return 0f;
        foreach (var s in Sites)
        {
            if (s.OrderId != o.Id || s.State != SiteState.Left) continue;
            if (s.Owner == c.Id) return 0.2f;
            // 남이 펼쳐 두고 간 자리: 그 사람이 돌아올 수 있으면 한동안 손대지 않는다 (급한 일은 예외)
            if (CrewById(s.Owner) is not { CanAct: true }) return 0f;
            return o.Urgency >= 0.9f ? -0.05f : _w.Tick - s.LeftAt < SimTime.Hours(2) ? -0.35f : -0.08f;
        }
        return 0f;
    }

    // ───────────────────────────── 앞 상자 ─────────────────────────────

    /// <summary>TakeToil: 큰 부품을 꽉 찬 선반에서 꺼내려면 앞 상자부터 바닥에 내려놓는다. true = 아직 치우는 중.</summary>
    public bool Dig(CrewMember c, Furniture from, ItemKind kind)
    {
        if (!Bulky(kind) || from.Storage is not Inventory inv || from.Type is not (FurnitureType.Shelf or FurnitureType.SupplyCache)) return false;
        var w = _w;
        long now = w.Tick;
        if (_digging.TryGetValue(c.Id, out var dig))
        {
            if (dig.ShelfId != from.Id) { _digging.Remove(c.Id); dig = null; }
            else if (now < dig.Until) { c.Pose = Pose.Working; Locomotion.Face(c, from.Center); return true; }
            else
            {
                _digging.Remove(c.Id);
                if (c.Job?.Urgent == true || Crisis.Acting(w))
                {
                    dig.Left = true;
                    Stats.BoxesLeft++;
                    w.Log.Add(now, LogKind.Life, $"급해서 앞 상자를 {from.Room.Name} 바닥에 둔 채 {ItemKinds.Name(kind)}만 들고 간다", c.Id);
                    MarkLog.Add(from.Room.Marks, now, $"{c.Name}: 앞 상자를 바닥에 두고 감");
                }
                else Boxes.Remove(dig); // 상자를 다시 밀어 넣었다
                return false;
            }
        }
        if (Boxes.Any(b => b.ShelfId == from.Id && b.Left)) return false; // 앞 상자는 이미 나와 있다
        float fill = inv.Total / (float)Math.Max(1, inv.Capacity);
        int others = inv.Total - inv.Count(kind);
        if (fill < 0.55f || others < 4) return false;
        float min = (2f + 4f * fill) * (c.Job?.Urgent == true ? 0.5f : 1f) * (Life.Has(c, Habit.Methodical) ? 1.15f : 1f);
        var spot = LayoutCells(c.Cell, 1)[0];
        var box = new DugBox
        {
            Item = new SiteItem { Id = NextId(), Kind = SiteItemKind.FrontBox, At = spot, Off = new Vector2(0.05f, -0.05f), Angle = 0.15f },
            ShelfId = from.Id, By = c.Id, Since = now, Until = now + SimTime.Minutes(min),
        };
        Boxes.Add(box);
        _digging[c.Id] = box;
        Stats.Digs++;
        c.Pose = Pose.Working;
        c.Say(w, Persona.Say(c, $"{Ko.IGa(ItemKinds.Name(kind))} 안쪽이야 — 앞 상자부터 빼고"));
        return true;
    }

    private void UpdateBoxes(long now)
    {
        // 치우던 사람이 일을 그만두면 (경보 등) 상자는 바닥에 남는다
        foreach (var c in _w.Crew)
            if (_digging.TryGetValue(c.Id, out var d) && (c.Job?.Current is not TakeToil || c.Dead || c.Down))
            {
                _digging.Remove(c.Id);
                d.Left = true;
                Stats.BoxesLeft++;
            }
    }

    // ───────────────────────────── 좁은 문 · 카트 ─────────────────────────────

    /// <summary>Locomotion.Step: 펼친 부품 · 앞 상자 · 구경꾼 무리를 비집고 · 카트가 좁은 문 앞에서 짐을 옮겨 싣는다.</summary>
    public float SqueezeMul(CrewMember c, List<Cell> path)
    {
        if (c.PathIndex >= path.Count || c.Outside) return 1f;
        var w = _w;
        var next = path[c.PathIndex];
        float m = 1f;
        if (w.Ship.DoorAt(next) is Door door && NarrowForCart(door))
        {
            float t = CartDoor(c, door, next);
            if (t <= 0f) return 0f;
        }
        if (!w.Ship.Grid.InBounds(next)) return m;
        int i = w.Ship.Grid.Index(next);
        if (i >= _slow.Length) return m;
        byte b = _slow[i];
        if (b == 0) return m;
        if ((b & 4) != 0 && !Crowds.IsWatcher(c)) { m *= c.Job?.Urgent == true ? 0.45f : 0.6f; Crowds.NoteSqueeze(c, next); } // 구경꾼 사이를 비집는다
        if ((b & 2) != 0) m *= 0.7f; // 바닥의 상자를 돌아
        if ((b & 1) != 0) m *= 0.8f; // 펼친 부품을 밟지 않게
        return m;
    }

    private float CartDoor(CrewMember c, Door door, Cell at)
    {
        var w = _w;
        long now = w.Tick;
        long key = ((long)c.Id << 32) | (uint)door.Id;
        if (_passed.TryGetValue(key, out var passedAt) && now - passedAt < SimTime.Minutes(3)) return 1f;
        foreach (var t in Transfers)
            if (t.Crew == c.Id && t.DoorId == door.Id && t.Until > now - 1)
            {
                if (now < t.Until) { c.Pose = Pose.Working; return 0f; }
                _passed[key] = now;
                return 1f;
            }
        if (!PushingCart(c)) return 1f;
        bool helped = false;
        foreach (var o in w.Crew)
            if (o != c && o.CanAct && !o.IsMoving && (o.Position - c.Position).LengthSquared() < 2.3f * 2.3f && o.Job?.Urgent != true) { helped = true; break; }
        float min = (c.Job?.Urgent == true ? 1.5f : 3f) * (helped ? 0.5f : 1f);
        Transfers.Add(new CartTransfer { Crew = c.Id, DoorId = door.Id, At = at, Since = now, Until = now + SimTime.Minutes(min), Helped = helped });
        Stats.Transfers++;
        if (helped) Stats.TransfersHelped++;
        string why = door.Bulkhead ? "격벽 문턱" : door.Bent > 0.15f ? "휜 문틀" : door.MotorBroken ? "반만 열리는 문" : "문턱";
        c.Say(w, Persona.Say(c, $"카트가 {why}에 걸려 — 짐을 옮겨 싣자"));
        w.Log.Add(now, LogKind.Life, $"카트가 {Ko.EulReul(why)} 못 넘는다 — 짐을 내려 문 너머로 옮겨 싣는다" + (helped ? " (곁의 사람이 거든다)" : ""), c.Id);
        c.Pose = Pose.Working;
        return 0f;
    }

    private bool PushingCart(CrewMember c)
    {
        foreach (var d in _w.Portable.Devices) if (d.Kind == PortableKind.Cart && d.HeldBy == c) return true;
        return _w.RoomPlans.TaskOf(c) is { Cart: true, Lifted: true };
    }

    /// <summary>Body.FillPathCost: 펼친 부품 · 앞 상자 · 구경꾼 (통로 칸에 놓인 것만 — ObjectPhysics 통로 점유와 같은 규칙).</summary>
    public void PathCost(int[] cost)
    {
        var w = _w;
        var grid = w.Ship.Grid;
        foreach (var s in Sites)
            foreach (var t in s.Items)
                if (grid.InBounds(t.At) && w.Matter.InAisle(t.At)) { int i = grid.Index(t.At); if (i < cost.Length) cost[i] += t.Bulk; }
        foreach (var b in Boxes)
            if (grid.InBounds(b.Item.At) && w.Matter.InAisle(b.Item.At)) { int i = grid.Index(b.Item.At); if (i < cost.Length) cost[i] += b.Item.Bulk; }
        Crowds.PathCost(cost);
        Queues.PathCost(cost);
    }

    private void RebuildSlow()
    {
        var grid = _w.Ship.Grid;
        if (_slow.Length != grid.CellCount) _slow = new byte[grid.CellCount];
        else Array.Clear(_slow);
        foreach (var s in Sites) foreach (var t in s.Items) if (grid.InBounds(t.At)) _slow[grid.Index(t.At)] |= 1;
        foreach (var b in Boxes) if (grid.InBounds(b.Item.At)) _slow[grid.Index(b.Item.At)] |= 2;
        foreach (var cell in Crowds.CrowdCells()) if (grid.InBounds(cell)) _slow[grid.Index(cell)] |= 4;
    }

    // ───────────────────────────── 옆 설비 ─────────────────────────────

    private Furniture? FindNeighbor(Session s)
    {
        if (s.F?.Machine == null || s.Order is not WorkOrder o
            || o.Kind is not (WorkKind.Repair or WorkKind.Maintain or WorkKind.PreventiveCheck or WorkKind.Calibrate or WorkKind.Upgrade or WorkKind.RestoreGrade or WorkKind.InstallSubstitute)) return null;
        Furniture? best = null;
        foreach (var g in s.F.Room.Furniture)
        {
            if (g == s.F || g.Stowed || g.Machine is not Machine m || !Noisy(g.Type)) continue;
            if (!m.Powered || !m.Active || m.Parked || m.Stopped) continue;
            bool near = false;
            foreach (var gc in g.Cells) if (Math.Max(Math.Abs(gc.X - s.Spot.X), Math.Abs(gc.Y - s.Spot.Y)) <= 1) { near = true; break; }
            if (!near) continue;
            if (Paused.Any(p => p.FurnitureId == g.Id && !p.Done)) continue;
            if (best == null || g.Id < best.Id) best = g;
        }
        return best;
    }

    private void DecideNeighbor(CrewMember c, Session s, long now)
    {
        var w = _w;
        var g = s.Neighbor!;
        var room = g.Room;
        var au = w.Automation;
        bool online = au.Present && au.MainOnline;
        if (!Critical(g.Type))
        {
            Pause(g, c, s, now, approved: false, minutes: 120f);
            w.Log.Add(now, LogKind.Work, $"{Ko.EulReul(g.Label)} 잠시 끄고 {s.F!.Label}에 손을 넣는다", c.Id);
            return;
        }
        int others = 0;
        foreach (var f in w.Ship.FurnitureOf(g.Type)) if (f != g && f.Machine is { Stopped: false, Parked: false, Powered: true }) others++;
        string? risk = ResourceRisk(g.Type);
        if (online)
        {
            bool deny = others == 0 && (Crisis.Acting(w) || risk != null);
            float mins = others == 0 ? 20f : s.Urgent ? 30f : 45f;
            if (deny)
            {
                au.Book.Add(ActKind.Advice, room, $"{c.Name}: {s.F!.Label} 작업 — 옆 {g.Label} 정지 요청", $"판단: 같은 설비가 하나뿐 · {risk ?? "비상 중"} — 지금 멈추면 위험", "거절",
                    "멈추지 말고 옆에서 조심해 작업하라", $"coop:deny:{g.Id}", SimTime.Minutes(30), 20f);
                s.Careful = true;
                Stats.Denied++; Stats.Careful++;
                c.Say(w, Persona.Say(c, $"컴퓨터가 {Ko.EunNeun(g.Label)} 못 끈대 — 조심조심"));
                return;
            }
            au.Book.Add(ActKind.Module, room, $"{c.Name}: {s.F!.Label} 작업 — 옆 {g.Label} 정지 요청", others > 0 ? $"판단: 같은 설비 {others}대가 버틴다 — {mins:0}분은 괜찮다" : $"판단: 하나뿐이라 짧게만 ({mins:0}분)",
                $"{g.Label} 일시 정지 승인 ({mins:0}분)", "끝나면 알려 달라 — 시간이 되면 다시 켠다", $"coop:ok:{g.Id}", SimTime.Minutes(5), mins);
            Pause(g, c, s, now, approved: true, minutes: mins);
            Stats.Approved++;
            c.Say(w, Persona.Say(c, $"컴퓨터 승인 — {g.Label} {mins:0}분 멈춘다"));
            return;
        }
        // 컴퓨터가 없다: 사람이 정한다 — 안전을 앞세우는 사람은 끄고, 일을 앞세우는 사람은 옆에서 조심
        if (c.Value is CrewValue.Safety or CrewValue.Rules || Life.Has(c, Habit.Methodical))
        {
            Pause(g, c, s, now, approved: false, minutes: 60f);
            w.Log.Add(now, LogKind.Work, $"컴퓨터에 물을 수 없어 스스로 {Ko.EulReul(g.Label)} 잠시 끈다", c.Id);
        }
        else { s.Careful = true; Stats.Careful++; c.Say(w, Persona.Say(c, $"{g.Label} 옆이라 조심조심")); }
    }

    private string? ResourceRisk(FurnitureType t)
    {
        var w = _w;
        switch (t)
        {
            case FurnitureType.OxygenGenerator or FurnitureType.Scrubber:
            {
                float sum = 0f; int n = 0;
                foreach (var r in w.Ship.LiveRooms) if (!r.Unbreathable && r.Type != RoomType.Corridor) { sum += r.Air.O2; n++; }
                return n > 0 && sum / n < 20f ? $"산소 평균 {sum / n:0.0}kPa" : null;
            }
            case FurnitureType.CoolantPump or FurnitureType.HeatExchanger:
                return w.Power.ReactorTemperature > 340f ? $"원자로 {w.Power.ReactorTemperature:0}℃" : null;
            case FurnitureType.PowerPanel or FurnitureType.Battery or FurnitureType.CapacitorBank:
                return w.Power.Brownout ? "저출력 운영 중" : null;
            default: return null;
        }
    }

    private void Pause(Furniture g, CrewMember c, Session s, long now, bool approved, float minutes)
    {
        g.Machine!.Parked = true;
        var p = new PausedFixture { FurnitureId = g.Id, Name = g.Label, By = c.Id, Since = now, Until = now + SimTime.Minutes(minutes), Approved = approved };
        Paused.Add(p);
        s.Pause = p;
        Stats.Pauses++;
        MarkLog.Add(g.Machine.Marks, now, $"{c.Name}: 옆 설비 작업으로 잠시 멈춤" + (approved ? " (컴퓨터 승인)" : ""));
    }

    /// <summary>Power.UpdateParking: 전력 계산이 내려 둔 설비를 새로 고를 때 다시 멈춰 둔다.</summary>
    public void Park()
    {
        foreach (var p in Paused)
            if (!p.Done && Furn(p.FurnitureId)?.Machine is Machine m) m.Parked = true;
    }

    private void Restore(PausedFixture p, CrewMember? by, string why)
    {
        p.Done = true;
        if (Furn(p.FurnitureId)?.Machine is Machine m) m.Parked = false;
        Stats.Restored++;
        if (by != null) _w.Log.Add(_w.Tick, LogKind.Work, $"{Ko.EulReul(p.Name)} {why}", by.Id);
    }

    private void UpdatePaused(long now)
    {
        var w = _w;
        var au = w.Automation;
        bool online = au.Present && au.MainOnline;
        foreach (var p in Paused)
        {
            if (p.Done) continue;
            if (Furn(p.FurnitureId) is not Furniture f || f.Machine is not Machine m) { p.Done = true; continue; }
            if (p.Approved && now >= p.Until)
            {
                // 승인 시간이 끝났다: 컴퓨터가 다시 켠다 (곁에서 일하던 사람은 조심조심으로)
                au.Book.Add(ActKind.Module, f.Room, $"{p.Name} 정지 승인 {(now - p.Since) / SimTime.Minutes(1)}분 끝", "판단: 더 멈추면 계통이 버티지 못한다", $"{p.Name} 다시 켬", "곁에서 일하는 사람은 조심하라", $"coop:resume:{p.FurnitureId}", SimTime.Minutes(5), 10f);
                Restore(p, null, "");
                foreach (var c in w.Crew) if (_sess.TryGetValue(c.Id, out var s) && s.Pause == p) { s.Careful = true; Stats.Careful++; }
                continue;
            }
            if (p.Forgotten && now - p.Since > SimTime.Minutes(20))
            {
                if (online)
                {
                    au.Book.Add(ActKind.Module, f.Room, $"{Ko.IGa(p.Name)} {(now - p.Since) / SimTime.Minutes(1)}분째 꺼져 있다 (정비 때 끈 뒤 안 켰다)", "판단: 꺼 둔 채 잊었다", $"{p.Name} 다시 켬",
                        $"{CrewById(p.By)?.Name ?? "끈 사람"}에게 알림 — 끈 설비는 다시 켜 달라", $"coop:forgot:{p.FurnitureId}", SimTime.Minutes(30), 10f);
                    if (CrewById(p.By) is CrewMember by) MarkLog.Add(by.Memory.Marks, now, $"{Ko.EulReul(p.Name)} 꺼 둔 채 잊었다 — 컴퓨터가 켰다");
                    Restore(p, null, "");
                }
                else if (now - p.Since > SimTime.Hours(2)) Restore(p, null, ""); // 누군가 알아채 켰다
            }
            else if (!p.Approved && !p.Forgotten && now >= p.Until) Restore(p, CrewById(p.By), "다시 켰다");
        }
        Paused.RemoveAll(p => p.Done && now - p.Since > SimTime.Hours(6));
    }

    // ───────────────────────────── 둘이 하는 일 ─────────────────────────────

    private bool NeedsPartner(CrewMember c, WorkOrder o, Furniture? f)
    {
        if (o.External) return false;
        if (o.Kind == WorkKind.Repair && f?.Machine is Machine m)
        {
            var fault = m.Faults.FirstOrDefault(x => x.Kind == o.Fault && x.Circuit == o.Circuit);
            // 무거운 새 부품을 들고 왔을 때 (긴급 우회 · 부분 수리는 들어 올릴 것이 없다) — 호이스트가 있으면 매달아 든다
            return fault != null && PartsSystem.Heavy(fault.Spec.Part) && c.Carrying?.Kind == fault.Spec.Part && Modules.Working(_w, FurnitureType.Hoist) == 0;
        }
        return o.Kind is WorkKind.ReplacePanel or WorkKind.RepairDoor;
    }

    /// <summary>짝 단계: 1 = 시작 · 0 = 기다림 · -1 = 보류 (그만둔다).</summary>
    private float PartnerStep(CrewMember c, Session s, long now)
    {
        var w = _w;
        if (s.Call == null)
        {
            string part = s.Order?.Kind == WorkKind.Repair && s.F?.Machine?.Faults.FirstOrDefault(x => x.Kind == s.Order.Fault)?.Spec.Part is ItemKind pk ? ItemKinds.Name(pk)
                : s.Order?.Kind == WorkKind.RepairDoor ? "문짝" : "패널";
            float min = (s.Urgent ? 5f : 15f) * (Life.Has(c, Habit.Patient) ? 1.4f : Life.Has(c, Habit.Hasty) ? 0.6f : 1f);
            s.Call = new PairCall { Id = NextId(), Caller = c.Id, OrderId = s.Order?.Id ?? -1, Spot = c.Cell, Face = s.Face, Opened = now, Cap = now + SimTime.Minutes(min), Urgent = s.Urgent, Part = part };
            Calls.Add(s.Call);
            Stats.Calls++;
            // 부르는 소리를 들은 곁의 사람들이 다시 생각한다 (같은 방 · 옆방)
            foreach (var nb in w.Crew)
                if (nb != c && nb.CanAct && !nb.Outside && nb.Job?.Urgent != true && (nb.Position - c.Position).LengthSquared() < 144f && nb.NextThinkTick > now + 1) nb.NextThinkTick = now + 1 + nb.Id % 5;
            c.Say(w, Persona.Say(c, $"누가 {part} 좀 잡아 줘 — 내가 조일게"));
        }
        var call = s.Call;
        if (call.Helper >= 0 && HelperHere(call))
        {
            call.Arrived = true; call.HelperSeen = now;
            Stats.Arrived++;
            var h = CrewById(call.Helper)!;
            int n = PairCount(c.Id, h.Id);
            w.Log.Add(now, LogKind.Work, $"{Ko.IGa(h.Name)} {Ko.EulReul(call.Part)} 잡고 {Ko.IGa(c.Name)} 조인다" + (n >= 2 ? $" — 손발이 척척 맞는다 (같이 한 게 벌써 {n + 1}번째)" : ""), c.Id);
            h.Say(w, Persona.Say(h, n >= 2 ? "늘 하던 대로 — 잡았어" : "잡았어, 조여"));
            c.Pose = Pose.Working;
            return 1f;
        }
        if (now < call.Cap) { if (now - call.Opened > SimTime.Minutes(6) && c.SaidUntil < now) c.Say(w, Persona.Say(c, "아무도 없나… 조금만 더 기다려 보자")); return Wait(c, s, now); }
        // 시간 상한: 혼자 방법(지그로 고정) 또는 보류 — 교착 없음
        var late = call.Helper >= 0 ? CrewById(call.Helper) : null;
        float solo = (s.Urgent ? 0.5f : 0f) + (Life.Has(c, Habit.Hasty) ? 0.2f : 0f) + (Life.Has(c, Habit.Daredevil) ? 0.2f : 0f) + 0.3f * c.Traits.Bravery
                     + (c.Value == CrewValue.Efficiency ? 0.15f : 0f) - (Life.Has(c, Habit.Methodical) ? 0.2f : 0f) - (c.Value == CrewValue.Safety ? 0.2f : 0f) - 0.5f * c.Vitals.Injury;
        call.Done = true;
        if (late != null) { Stats.Late++; c.ChangeAffinity(late, -0.01f); }
        int held = s.Order is WorkOrder ho ? _holdN.GetValueOrDefault(ho.Id) : 0; // v16.24 두 번 허탕 쳤으면 더 미루지 않는다
        if (solo >= 0.3f || s.Urgent || held >= 2)
        {
            call.Outcome = "아무도 안 와 혼자 지그로";
            if (held >= 2) { c.Say(w, Persona.Say(c, "벌써 몇 번째야 — 지그로 물려 놓고 혼자 끝낸다")); s.Solo = true; Stats.Solos++; w.Log.Add(now, LogKind.Work, $"{Ko.EulReul(call.Part)} 잡아 줄 사람을 {held}번 기다리다 말았다 — 지그로 물려 놓고 혼자 천천히 한다", c.Id); c.Pose = Pose.Working; return 1f; }
            s.Solo = true;
            Stats.Solos++;
            c.Say(w, Persona.Say(c, "혼자 하자 — 지그로 물려 놓고"));
            w.Log.Add(now, LogKind.Work, $"{Ko.EulReul(call.Part)} 잡아 줄 사람이 {(late != null ? $"늦는다 ({late.Name})" : "안 온다")} — 혼자 지그로 물려 놓고 천천히 한다", c.Id);
            c.Pose = Pose.Working;
            return 1f;
        }
        call.Outcome = "보류";
        Stats.Holds++;
        var o = s.Order!;
        _holds[o.Id] = now + SimTime.Hours(1);
        _holdN[o.Id] = held + 1;
        s.Held = true;            // v16.24 맡아 두지 않는다 — 시간이 되면 손이 빈 누구든 (둘이 있을 때) 다시 잡는다
        w.Board.Release(o, c);
        w.Board.Block(o, $"{Ko.EulReul(call.Part)} 잡아 줄 사람이 없다 — 한 시간 뒤 다시", 1f);
        c.Say(w, Persona.Say(c, "혼자는 무리야 — 사람 있을 때 하자"));
        return -1f;
    }

    /// <summary>v16.24 맡아 둔 채 다른 일만 하고 진척이 한 시간 반 넘게 그대로인 일 — 내려놓는다 (자리도 풀고 · 까닭을 남기고 · 손이 빈 사람이 잇는다).</summary>
    private void Stale(long now)
    {
        var w = _w;
        foreach (var o in w.Board.Open)
        {
            if (o.Assignee is not CrewMember a) { _stale.Remove(o.Id); continue; }
            if (!_stale.TryGetValue(o.Id, out var s) || s.who != a.Id || MathF.Abs(s.prog - o.Progress) > 1e-4f) { _stale[o.Id] = (o.Progress, now, a.Id); continue; }
            if (a.Job?.Order == o && a.CanAct) continue; // 지금 붙어 있다 (기다리는 중이어도)
            if (now - s.since < SimTime.Minutes(90)) continue;
            foreach (var site in Sites) if (site.OrderId == o.Id) site.Reserved = false;
            w.Board.Release(o, a);
            _stale.Remove(o.Id);
            StaleDrops++;
            string why = !a.CanAct ? (a.Down ? "쓰러져서" : "자리에 없어서") : a.Job?.Label is string l ? $"{l}에 붙들려" : "짬이 안 나서";
            w.Log.Add(now, LogKind.Work, $"{a.Name}: {o.Title} — {why} {(now - s.since) / (float)SimTime.TicksPerHour:0.#}시간째 손을 못 댔다 · 맡은 것을 내려놓는다 (손이 빈 사람이 잇는다)", a.Id);
        }
        if (_stale.Count > 64) foreach (var k in _stale.Keys.Where(id => !w.Board.OpenUnsorted.Any(x => x.Id == id)).ToList()) _stale.Remove(k);
    }

    private bool HelperHere(PairCall call)
    {
        if (call.Helper < 0 || CrewById(call.Helper) is not CrewMember h || !h.CanAct) return false;
        if (h.Job?.Activity is not LendHandActivity || h.Job.Current is not HoldPartToil) return false;
        return (h.Position - call.Spot.Center).LengthSquared() < 2.1f * 2.1f;
    }

    private void PairDone(CrewMember c, PairCall call, long now)
    {
        var h = CrewById(call.Helper);
        if (h == null) return;
        long k = PairKey(c.Id, h.Id);
        int n = PairCount(c.Id, h.Id) + 1;
        PairCounts[k] = n;
        Stats.PairJobs++;
        c.ChangeAffinity(h, 0.03f); h.ChangeAffinity(c, 0.03f);
        if (n == 3)
        {
            Stats.GoodPairs++;
            _w.Relations.Remember(c, h, RelationReason.GoodPartner, $"함께 {n}번 — 잡고 조이는 손발이 맞는다");
            _w.Relations.Remember(h, c, RelationReason.GoodPartner, $"함께 {n}번 — 잡고 조이는 손발이 맞는다");
            _w.Log.Add(now, LogKind.Life, $"{Ko.WaGwa(c.Name)} {Ko.EunNeun(h.Name)} 이제 말없이도 손발이 맞는다 (함께 {n}번)", c.Id);
        }
    }

    private void UpdateCalls(long now)
    {
        foreach (var call in Calls)
        {
            if (call.Done) continue;
            if (CrewById(call.Caller) is not CrewMember c || !c.CanAct || !_sess.TryGetValue(c.Id, out var s) || s.Call != call) { call.Done = true; call.Outcome = "부른 사람이 갔다"; continue; }
            if (call.Helper >= 0 && CrewById(call.Helper) is CrewMember h && (h.Job?.Activity is not LendHandActivity || !h.CanAct)) call.Helper = -1; // 오던 사람이 다른 일로 갔다
        }
        Calls.RemoveAll(x => x.Done && now - x.Opened > SimTime.Hours(3));
    }

    /// <summary>LendHandActivity: 이 사람이 갈 만한 부름 (가장 가까운 · 친한 · 손발이 맞는).</summary>
    internal (PairCall? call, float score, string why) BestCall(CrewMember c, DistanceField dist)
    {
        if (NoHelpers || Calls.Count == 0) return (null, 0f, "");
        PairCall? best = null; float bs = 0f; string why = "";
        foreach (var call in Calls)
        {
            if (call.Done || call.Caller == c.Id || call.Helper >= 0 && call.Helper != c.Id) continue;
            if (CrewById(call.Caller) is not CrewMember caller) continue;
            int d = dist.Get(call.Spot);
            if (d < 0) continue;
            int n = PairCount(c.Id, caller.Id);
            float s = 0.55f + 0.3f * MathF.Max(0f, c.AffinityTo(caller)) + 0.04f * Math.Min(5, n) + 0.1f * c.Traits.Sociability + (call.Urgent ? 0.3f : 0f)
                      - d / 4000f + (Life.Has(c, Habit.Generous) ? 0.05f : 0f) - (Life.Has(c, Habit.Loner) ? 0.1f : 0f) - (c.AffinityTo(caller) < -0.3f ? 0.25f : 0f);
            if (s > bs) { bs = s; best = call; why = n >= 2 ? $"{Ko.IGa(caller.Name)} 부른다 — 늘 같이 하던 사이" : $"{Ko.IGa(caller.Name)} {Ko.EulReul(call.Part)} 잡아 달란다"; }
        }
        return (best, bs, why);
    }

    // ───────────────────────────── 공구 · 시험대 예약 ─────────────────────────────

    private BenchBook BookOf(Furniture f)
    {
        foreach (var b in Benches) if (b.FurnitureId == f.Id) return b;
        var nb = new BenchBook { FurnitureId = f.Id, Name = f.Label };
        Benches.Add(nb);
        return nb;
    }

    private bool BenchTurn(CrewMember c, Session s, long now)
    {
        var w = _w;
        var b = s.Bench!;
        if (b.User == c.Id) return true;
        var user = b.User >= 0 ? CrewById(b.User) : null;
        if (user != null && (!_sess.TryGetValue(user.Id, out var us) || us.Bench != b || !user.CanAct)) { b.User = -1; user = null; } // 비었다
        if (user == null)
        {
            // 기다리는 사람이 있으면 순서대로 (급한 일 먼저 · 그다음 온 순서)
            int first = FirstWaiter(b);
            if (first >= 0 && first != c.Id && !b.Waiting.Contains(c.Id)) { AddWaiter(b, c.Id, now); return false; }
            if (first >= 0 && first != c.Id) return false;
            Take(b, c, s, now);
            return true;
        }
        // 급한 일은 먼저 쓴다 (쓰던 사람은 손을 멈추고 비켜 준다)
        if (s.Urgent && !b.UserUrgent)
        {
            Stats.Preempts++;
            user.Say(w, Persona.Say(user, "급한 거면 먼저 해"));
            c.Say(w, Persona.Say(c, $"미안, 급해서 {b.Name} 먼저 쓸게"));
            w.Log.Add(now, LogKind.Work, $"급한 일이라 {Ko.IGa(user.Name)} 쓰던 {Ko.EulReul(b.Name)} 먼저 쓴다 — {Ko.EunNeun(user.Name)} 기다린다", c.Id);
            if (_sess.TryGetValue(user.Id, out var u2)) { u2.Phase = PBench; }
            b.Waiting.Insert(0, user.Id); b.Since.Insert(0, now);
            Take(b, c, s, now);
            return true;
        }
        if (!b.Waiting.Contains(c.Id))
        {
            AddWaiter(b, c.Id, now);
            Stats.BenchWaits++;
            c.Say(w, Persona.Say(c, $"{Ko.IGa(user.Name)} {Ko.EulReul(b.Name)} 쓰는 중 — 다음은 나"));
        }
        // 오래 기다리면 투덜 (컴퓨터가 순서 · 시간을 알려 줬으면 덜)
        int idx = b.Waiting.IndexOf(c.Id);
        long waited = now - b.Since[idx];
        Stats.BenchMinutes += 60f / SimTime.TicksPerHour;
        if (waited > SimTime.Minutes(8 + 6 * s.Grumbles) && s.Grumbles < 3)
        {
            s.Grumbles++;
            if (s.Ordered) c.Say(w, Persona.Say(c, "컴퓨터 말로는 곧이래"));
            else
            {
                Stats.Grumbles++;
                c.Say(w, Persona.Say(c, Life.Has(c, Habit.Grumbler) || Life.Has(c, Habit.ShortTempered) ? $"{user.Name}, 아직이야? 하루 종일 쓰네" : "아직 멀었어?"));
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
                if (!b.UserUrgent) c.ChangeAffinity(user, -0.01f);
            }
        }
        return false;
    }

    private void Take(BenchBook b, CrewMember c, Session s, long now)
    {
        RemoveWaiter(b, c.Id);
        b.User = c.Id; b.UserSince = now; b.UserUrgent = s.Urgent;
    }

    private static int FirstWaiter(BenchBook b) => b.Waiting.Count == 0 ? -1 : b.Waiting[0];

    private void AddWaiter(BenchBook b, int id, long now)
    {
        if (b.Waiting.Contains(id)) return;
        // 급한 일은 기다리는 줄에서도 앞으로
        bool urgent = _sess.TryGetValue(id, out var s) && s.Urgent;
        int at = b.Waiting.Count;
        if (urgent) for (int i = 0; i < b.Waiting.Count; i++) if (!(_sess.TryGetValue(b.Waiting[i], out var o) && o.Urgent)) { at = i; break; }
        b.Waiting.Insert(at, id); b.Since.Insert(at, now);
    }

    private static void RemoveWaiter(BenchBook b, int id)
    {
        int i = b.Waiting.IndexOf(id);
        if (i < 0) return;
        b.Waiting.RemoveAt(i); b.Since.RemoveAt(i);
    }

    private void ReleaseBench(BenchBook b, int id)
    {
        if (b.User != id) return;
        b.User = -1; b.UserUrgent = false;
    }

    private void UpdateBenches(long now, float dt)
    {
        var w = _w;
        foreach (var b in Benches)
        {
            // 떠난 사람은 줄에서 뺀다
            for (int i = b.Waiting.Count - 1; i >= 0; i--)
                if (!_sess.TryGetValue(b.Waiting[i], out var s) || s.Bench != b) { b.Waiting.RemoveAt(i); b.Since.RemoveAt(i); }
            if (b.User >= 0 && (!_sess.TryGetValue(b.User, out var us) || us.Bench != b)) b.User = -1;
            if (b.Waiting.Count == 0 || b.User < 0) continue;
            long longest = now - b.Since.Min();
            bool conflict = b.Waiting.Count >= 2 || longest > SimTime.Minutes(10);
            if (!conflict || b.Advised >= 0 && now - b.Advised < SimTime.Minutes(30)) continue;
            var au = w.Automation;
            if (!au.Present || !au.MainOnline) continue;
            var user = CrewById(b.User);
            var names = b.Waiting.Select(id => CrewById(id)?.Name ?? "?").ToList();
            float left = user?.Job?.Current is WorkToil wt && wt.Progress is float p ? (1f - p) * 40f : 20f;
            var f = Furn(b.FurnitureId);
            if (au.Book.Add(ActKind.Advice, f?.Room, $"{b.Name} 예약 충돌 — {user?.Name ?? "?"} 사용 중 · 대기 {string.Join("·", names)}",
                    $"판단: 급한 일 먼저 · 그다음 온 순서 — {user?.Name ?? "?"} 남은 시간 약 {left:0}분", $"순서 제안: {string.Join(" → ", names)}", "기다리는 동안 다른 일을 해도 된다 — 차례가 오면 알린다",
                    $"coop:bench:{b.FurnitureId}", SimTime.Minutes(30), 30f) == null) continue;
            b.Advised = now;
            Stats.ComputerOrders++;
            foreach (var id in b.Waiting)
                if (_sess.TryGetValue(id, out var s) && CrewById(id) is CrewMember wc && au.Trusts.Of(wc) >= 0.4f) s.Ordered = true; // 믿는 사람은 순서를 받아들여 덜 투덜댄다
        }
    }

    // ───────────────────────────── 주 컴퓨터 ─────────────────────────────

    /// <summary>막힌 통로: 비워 둔 작업장 · 바닥의 상자가 통로 칸에 오래 있으면 알린다 (치우거나 돌아가라).</summary>
    private void ComputerWatch()
    {
        var w = _w;
        var au = w.Automation;
        if (!au.Present || !au.MainOnline) return;
        long now = w.Tick;
        foreach (var s in Sites)
        {
            if (s.State != SiteState.Left || now - s.LeftAt < SimTime.Minutes(10)) continue;
            int aisle = s.Items.Count(t => w.Matter.InAisle(t.At));
            if (aisle == 0) continue;
            var room = w.Ship.RoomAt(s.Spot);
            if (room == null || !room.DataLinked) continue;
            if (au.Book.Add(ActKind.Advice, room, $"{room.Name} 통로에 펼쳐 둔 공구 · 부품 {aisle}점 ({s.Label} 작업, {(now - s.LeftAt) / SimTime.Minutes(1)}분째 비움)",
                    "판단: 통로가 좁아져 다니는 길이 돌아간다 · 급한 사람이 걷어찰 수 있다", "선내 메시지", $"{Ko.EunNeun(CrewById(s.Owner)?.Name ?? "맡은 사람")} 돌아와 이어 하거나 한쪽으로 모아 달라",
                    $"coop:aisle:{s.Id}", SimTime.Hours(2), 30f) != null) Stats.AisleWarns++;
        }
        foreach (var b in Boxes)
        {
            if (!b.Left || now - b.Since < SimTime.Minutes(15) || !w.Matter.InAisle(b.Item.At)) continue;
            var room = w.Ship.RoomAt(b.Item.At);
            if (room == null || !room.DataLinked) continue;
            if (au.Book.Add(ActKind.Advice, room, $"{room.Name} 통로에 상자", "판단: 큰 부품을 꺼내며 내려놓은 앞 상자 — 통로를 좁힌다", "선내 메시지", "지나는 사람이 선반에 되돌려 달라",
                    $"coop:box:{b.Item.Id}", SimTime.Hours(2), 30f) != null) { Stats.AisleWarns++; b.Warned = true; }
        }
    }

    // ───────────────────────────── 화면 · 지문 ─────────────────────────────

    /// <summary>화면용: 이 사람이 지금 무엇을 기다리나 (예약 · 짝 · 옆 설비 · 펼치기) — 없으면 null.</summary>
    public string? WaitingFor(CrewMember c)
    {
        if (!_sess.TryGetValue(c.Id, out var s) || s.Job != c.Job || !s.Relevant) return null;
        return s.Phase switch
        {
            PBench => "예약 대기",
            PNeighbor when s.Neighbor != null => "옆 설비 멈춤",
            PSetup when s.SetupMin > 0f => s.Site != null ? "다시 맞춰 봄" : "공구 펼침",
            PPartner => "짝 기다림",
            _ => s.Solo ? "혼자 (지그)" : s.Careful ? "조심조심" : null,
        };
    }

    /// <summary>화면용: 기다림 진행 (0~1 — 짝 부름 상한 · 예약 대기).</summary>
    public float WaitFrac(CrewMember c)
    {
        if (!_sess.TryGetValue(c.Id, out var s) || s.Job != c.Job) return 0f;
        long now = _w.Tick;
        if (s.Phase == PPartner && s.Call is PairCall call && !call.Done) return Math.Clamp((now - call.Opened) / (float)Math.Max(1, call.Cap - call.Opened), 0f, 1f);
        if (s.Phase == PBench && s.Bench is BenchBook b && b.Waiting.IndexOf(c.Id) is int i and >= 0) return Math.Clamp((now - b.Since[i]) / (float)SimTime.Minutes(30), 0f, 1f);
        if (s.Until > 0 && s.Phase is PNeighbor or PSetup) return 1f - Math.Clamp((s.Until - now) / (float)SimTime.Minutes(2), 0f, 1f);
        return 0f;
    }

    public bool IsSolo(CrewMember c) => _sess.TryGetValue(c.Id, out var s) && s.Job == c.Job && s.Solo;
    public PausedFixture? PauseOf(Furniture f) { foreach (var p in Paused) if (!p.Done && p.FurnitureId == f.Id) return p; return null; }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Sites.Count);
        foreach (var s in Sites) { I(s.OrderId); I(s.Owner); I((int)s.State + (s.Touched ? 4 : 0)); foreach (var t in s.Items) { I(t.At.X * 1000 + t.At.Y); I(t.Kicked ? 1 : 0); } }
        I(Boxes.Count); foreach (var b in Boxes) { I(b.Item.At.X * 1000 + b.Item.At.Y); I(b.Left ? 1 : 0); }
        I(Calls.Count); foreach (var c in Calls) { I(c.Caller); I(c.Helper); I((c.Arrived ? 1 : 0) + (c.Done ? 2 : 0)); }
        foreach (var b in Benches) { I(b.User); I(b.Waiting.Count); }
        I(Paused.Count(p => !p.Done));
        foreach (var kv in PairCounts) { I(kv.Key); I(kv.Value); }
        var st = Stats;
        I(st.Sites); I(st.Left); I(st.Resumed); I(st.Confused); I(st.Kicks); I(st.Digs); I(st.Transfers); I(st.Pauses); I(st.Approved); I(st.Denied);
        I(st.Calls); I(st.Arrived); I(st.Solos); I(st.Holds); I(st.PairJobs); I(st.BenchWaits); I(st.Grumbles); I(st.Preempts); I(st.ComputerOrders); I(st.AisleWarns);
        Queues.Hash(I, F);
        Crowds.Hash(I, F);
    }
}

/// <summary>거들기: "누가 좀 잡아 줘" — 부른 사람 곁에서 부품을 잡아 준다 (친한 사람 · 손발이 맞는 짝이 먼저 온다).</summary>
public sealed class LendHandActivity : Activity
{
    public override string Id => "lendhand";
    public override string Label => "잡아 주기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Coop.Calls.Count == 0 || !c.CanAct || c.IsChild || c.Outside || c.CarryingPerson != null || c.Job?.Urgent == true) return (0f, "—");
        var (call, s, why) = w.Coop.BestCall(c, dist);
        if (call == null) return (0f, "—");
        if (Bedtime(c, w)) s -= 0.35f;
        if (c.Pose == Pose.Sleeping) s -= 0.6f;
        if (c.Needs.Hunger > 0.85f) s -= 0.3f;
        return (MathF.Max(0f, s), why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (call, _, _) = w.Coop.BestCall(c, dist);
        if (call == null) return null;
        var ship = w.Ship;
        Cell? at = null; int bd = int.MaxValue;
        foreach (var d in Cell.Dirs8)
        {
            var x = call.Spot + d;
            if (!ship.IsWalkable(x) || ship.DoorAt(x) != null || ship.FurnitureAt(x) is Furniture f && !FurnitureTypes.Walkable(f.Type)) continue;
            int dd = dist.Get(x);
            if (dd < 0 || dd >= bd) continue;
            at = x; bd = dd;
        }
        if (at is not Cell spot) return null;
        call.Helper = c.Id;
        var caller = w.Crew.FirstOrDefault(x => x.Id == call.Caller);
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new HoldPartToil(call));
        return new Job(this, "잡아 주기", toils)
        {
            LogText = $"{Ko.IGa(caller?.Name ?? "누가")} {Ko.EulReul(call.Part)} 잡아 달란다 — 거들러 간다", LogKind = LogKind.Work, InterruptMargin = 0.25f,
            OnFinished = (cm, world, st) => { if (call.Helper == cm.Id && !call.Arrived) call.Helper = -1; },
        };
    }
}

/// <summary>부품을 잡고 있는다 (부른 사람이 체결을 끝내거나 · 그만두면 끝).</summary>
public sealed class HoldPartToil : Toil
{
    private readonly PairCall _call;
    private int _elapsed;
    internal PairCall Call => _call;
    public HoldPartToil(PairCall call) => _call = call;

    public override void Begin(CrewMember c, World w)
    {
        c.Pose = Pose.Working;
        Locomotion.Face(c, _call.Face);
        if (_call.Done)
        {
            w.Coop.Stats.Late++;
            c.Say(w, Persona.Say(c, "늦었네 — 벌써 했구나"));
        }
    }

    public override ToilStatus Tick(CrewMember c, World w)
    {
        _elapsed++;
        if (_call.Done || _elapsed > SimTime.Hours(2)) return ToilStatus.Succeeded;
        c.Pose = Pose.Working;
        return ToilStatus.Running;
    }
}

/// <summary>통로에 두고 간 앞 상자를 선반에 되돌린다 (지나는 사람 · 깔끔한 사람이 먼저).</summary>
public sealed class SpaceTidyActivity : Activity
{
    public override string Id => "spacetidy";
    public override string Label => "통로 상자 치우기";

    private static DugBox? Pick(CrewMember c, World w, DistanceField dist, out int d)
    {
        d = -1;
        DugBox? best = null;
        foreach (var b in w.Coop.Boxes)
        {
            if (!b.Left || b.TidyBy >= 0 && b.TidyBy != c.Id || w.Tick - b.Since < SimTime.Minutes(10)) continue;
            int dd = dist.Get(b.Item.At);
            if (dd < 0 || best != null && dd >= d) continue;
            best = b; d = dd;
        }
        return best;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Coop.Boxes.Count == 0 || !c.CanAct || c.IsChild || Crisis.Acting(w)) return (0f, "—");
        var b = Pick(c, w, dist, out int d);
        if (b == null || d > 400) return (0f, "—");
        float s = 0.34f + (Life.Has(c, Habit.NeatFreak) ? 0.15f : 0f) + (b.By == c.Id ? 0.15f : 0f) + (w.Matter.InAisle(b.Item.At) ? 0.12f : 0f) - d / 3000f - (Life.Has(c, Habit.Messy) ? 0.1f : 0f)
                  + (b.Warned ? 0.15f * w.Automation.Trusts.Of(c) : 0f); // 컴퓨터가 치워 달라고 했다 (믿는 만큼)
        if (Bedtime(c, w)) s -= 0.3f;
        return (MathF.Max(0f, s), b.By == c.Id ? "내가 꺼내 둔 상자 — 되돌려 놓자" : "통로에 상자가 나와 있다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var b = Pick(c, w, dist, out _);
        if (b == null) return null;
        b.TidyBy = c.Id;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(b.Item.At));
        toils.Add(new WaitToil(SimTime.Minutes(2), Pose.Working));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Coop.Boxes.Remove(b);
            world.Coop.Stats.BoxesTidied++;
            world.Log.Add(world.Tick, LogKind.Life, b.By == cm.Id ? "바닥에 두고 갔던 앞 상자를 선반에 되돌렸다" : "통로에 나와 있던 상자를 선반에 되돌렸다", cm.Id);
            if (b.By != cm.Id && world.Crew.FirstOrDefault(x => x.Id == b.By) is CrewMember by) by.ChangeAffinity(cm, 0.01f);
            return true;
        }));
        return new Job(this, "상자 되돌리기", toils) { LogText = "통로에 나온 상자를 치운다", LogKind = LogKind.Life, OnFinished = (cm, world, st) => { if (b.TidyBy == cm.Id) b.TidyBy = -1; } };
    }
}
