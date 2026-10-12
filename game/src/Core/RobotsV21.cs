using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v21 로봇 · 드론 확장 — "로봇이 기본":
//  · 대수: 사람 한 명에 로봇 두 대 (상한 80) · 배를 띄울 때 종류마다 집 방 벽가에 정비대를 단다 (방마다 고르게).
//  · 모든 로봇은 기본 행동을 다 한다 (나르기 · 정비 · 재배 · 조명 · 차단기 · 밸브 …) — 종류마다 특기가 있어 그 일은 빠르고 먼저 맡는다.
//    특기가 아닌 일은 특기 로봇이 먼저 잡게 몇 분 기다렸다가 (급하면 바로) · 조금 느리게 한다. 의료 로봇은 늘 대기 — 급한 일만.
//  · 새 종류 9: 청소 · 조리 (생활) · 배관 · 지게 · 환기 (설비) · 구조 · 봉쇄 · 공기 (안전) · 의사 (의료).
//  · 소형 원자로: 로봇마다 품고 다녀 충전대 전기가 필요 없다 (정전에도 움직인다). 정비대는 쉬고 스스로 고치는 자리.
//    원자로가 상하면(출력 저하) 내장 전지로 버티다 멈춘다. 아주 가끔 폭주한다 — 스스로 긴급 정지하거나, 빈 방으로 걸어가
//    소방 로봇이 거품으로 식히기를 기다린다. 못 식히면 터진다.
//  · 긴급 지정: 위기(불 · 쓰러진 사람 · 파공 · 침수)에 일손이 모자라면 주 컴퓨터가 맡은 일 밖의 로봇을 돌린다 —
//    권한(로봇 지휘)이 자동이면 바로, 제안이면 함장에게 묻고, 조언이면 기록만. 컴퓨터가 멎으면 로봇은 처음 맡은 일을 한다.
//  · 개량 단계 Mk.I~III (정비 로봇이 부품으로 올린다) · 숙련 (특기 일을 할수록 빠르고 덜 헛짚는다).

public enum DraftKind : byte { None, Fire, Rescue, Breach, Flood }

public sealed partial class Robot
{
    /// <summary>v21 개량 단계 (1 ~ 3).</summary>
    public int Mk { get; internal set; } = 1;
    /// <summary>v21 숙련 0~1 (특기 일을 할수록).</summary>
    public float Skill { get; internal set; }
    /// <summary>v21 긴급 지정 (주 컴퓨터가 맡은 일 밖의 위기 일에 돌렸다).</summary>
    public DraftKind Draft { get; internal set; }
    public long DraftUntil { get; internal set; } = -1;
    public int DraftRoom { get; internal set; } = -1;
    /// <summary>v21 소형 원자로 열 0~1 (폭주하면 1에 가깝다).</summary>
    public float CoreHeat { get; internal set; }
    /// <summary>폭주가 시작된 틱 (-1 아님) · 터질 틱.</summary>
    public long CoreRunaway { get; internal set; } = -1;
    public long CoreBlowAt { get; internal set; } = -1;
    /// <summary>개량 받는 중 (정비대에 묶어 둔다).</summary>
    public bool Upgrading { get; internal set; }

    public bool Drafted(World w) => Draft != DraftKind.None && w.Tick < DraftUntil;
    public bool Runaway => CoreRunaway >= 0;
}

public sealed class RobotV21Stats
{
    public int Joins;
    public int Drafts, DraftAsks, DraftAdvice, Runaways, Scrams, Cooled, Blasts, Upgrades, Wipes, Meals, Pumped, Seals, Treats, Wakes, Dampers;
    public override string ToString() =>
        $"같이 하기 {Joins} · 긴급 지정 {Drafts}(물음 {DraftAsks} · 조언 {DraftAdvice}) · 원자로 폭주 {Runaways}(자동 정지 {Scrams} · 식힘 {Cooled} · 폭발 {Blasts}) · 개량 {Upgrades} · " +
        $"바닥 닦기 {Wipes} · 조리 {Meals} · 물 퍼냄 {Pumped} · 봉합 {Seals} · 치료 {Treats} · 깨움 {Wakes} · 댐퍼 {Dampers}";
}

public static class RobotsV21
{
    /// <summary>시험: 모든 로봇이 기본 행동을 다 한다 (끄면 예전처럼 특기만).</summary>
    public static bool General = true;
    /// <summary>시험: 소형 원자로 (끄면 예전처럼 충전대 전기로).</summary>
    public static bool Reactor = true;
    /// <summary>시험: 사람 한 명에 두 대 (끄면 예전 구성 2~9대).</summary>
    public static bool Expanded = true;
    public const int RobotCap = 80;
    /// <summary>원자로가 내장 전지를 채우는 빠르기 (시간당).</summary>
    public const float ReactorPerHour = 0.25f;
    /// <summary>특기가 아닌 일: 특기 로봇이 먼저 잡게 기다리는 시간 · 일 시간 배율.</summary>
    public const float GeneralWaitMin = 6f, GeneralSlow = 1.3f;

    /// <summary>모든 로봇이 하는 기본 행동 (작업 목록의 일).</summary>
    public static bool Basic(WorkKind w) => w is WorkKind.Restock or WorkKind.StockDock or WorkKind.CarryWater or WorkKind.StockCache or WorkKind.StowCot
        or WorkKind.RefillPropellant or WorkKind.ShelterFood or WorkKind.EmptyBucket or WorkKind.Maintain or WorkKind.FixLights or WorkKind.FlushMain
        or WorkKind.ClearMain or WorkKind.LookRoom or WorkKind.SealSeep or WorkKind.Tend or WorkKind.Harvest or WorkKind.CleanUp or WorkKind.ServiceNozzle or WorkKind.RepairRelay;

    /// <summary>놀고 있는 특기 로봇이 있으면 사람이 미루는 v21 특기 일 (급한 일 · 다친 사람 치료는 사람도 바로 한다).</summary>
    public static bool Yields(WorkKind w) => w is WorkKind.CleanUp or WorkKind.ClearRubble or WorkKind.Cook or WorkKind.PumpOut or WorkKind.ShutRoomValve or WorkKind.OpenRoomValve
        or WorkKind.OperateDamper or WorkKind.CoolDown or WorkKind.SealO2Line or WorkKind.BleedRoom or WorkKind.WakeCrew or WorkKind.SealBreach or WorkKind.CrankDoor
        or WorkKind.Treat or WorkKind.ServiceNozzle or WorkKind.RepairRelay;

    /// <summary>긴급 지정으로 맡는 위기 일.</summary>
    public static bool DraftJob(DraftKind d, WorkKind w) => d switch
    {
        DraftKind.Breach => w is WorkKind.SealBreach or WorkKind.CrankDoor,
        DraftKind.Flood => w is WorkKind.PumpOut or WorkKind.ShutRoomValve or WorkKind.CloseValve or WorkKind.PatchPipe,
        _ => false,
    };

    public static string DraftName(DraftKind d) => d switch
    {
        DraftKind.Fire => "불 끄기", DraftKind.Rescue => "쓰러진 사람 옮기기", DraftKind.Breach => "파공 막기", DraftKind.Flood => "물 막기 · 퍼내기", _ => "",
    };

    /// <summary>의료 로봇: 늘 대기 (기본 행동은 급한 것만).</summary>
    public static bool OnCall(RobotKind k) => k is RobotKind.Stretcher or RobotKind.Nurse or RobotKind.Medic or RobotKind.Rescuer;

    public static bool Medical(RobotKind k) => k is RobotKind.Stretcher or RobotKind.Nurse or RobotKind.Medic;

    public static string MkName(int mk) => mk switch { <= 1 => "Mk.I", 2 => "Mk.II", _ => "Mk.III" };

    // ─────────────────────────────── 대수 · 구성 ───────────────────────────────

    /// <summary>처음 한 대씩 (앞에서부터) — 작은 배도 정비 · 재배 · 운반 · 방재 · 의료가 먼저 선다.</summary>
    private static readonly RobotKind[] First =
    {
        RobotKind.Maintainer, RobotKind.Gardener, RobotKind.Hauler, RobotKind.Safety, RobotKind.Medic, RobotKind.Cleaner, RobotKind.Nurse, RobotKind.Rescuer,
        RobotKind.Chef, RobotKind.Plumber, RobotKind.Lineman, RobotKind.Firefighter, RobotKind.Stretcher, RobotKind.Sealer, RobotKind.Courier, RobotKind.Harvester,
        RobotKind.Ventman, RobotKind.Airman, RobotKind.Lifter, RobotKind.Tanker, RobotKind.Sentry, RobotKind.Overhauler, RobotKind.Stocker, RobotKind.Tender,
        RobotKind.Assistant, RobotKind.Utility,
    };

    /// <summary>그다음은 일이 많은 종류부터 돌려 가며.</summary>
    private static readonly RobotKind[] Bulk =
    {
        RobotKind.Hauler, RobotKind.Maintainer, RobotKind.Cleaner, RobotKind.Gardener, RobotKind.Courier, RobotKind.Safety, RobotKind.Medic, RobotKind.Plumber,
        RobotKind.Lineman, RobotKind.Nurse, RobotKind.Rescuer, RobotKind.Chef, RobotKind.Harvester, RobotKind.Firefighter, RobotKind.Lifter, RobotKind.Sealer,
        RobotKind.Tender, RobotKind.Stretcher, RobotKind.Utility, RobotKind.Overhauler,
    };

    /// <summary>사람 한 명에 두 대 (적어도 넷 · 많아야 80).</summary>
    public static int Count(int beds) => Math.Clamp(beds * 2, 4, RobotCap);

    public static RobotKind[] Complement(int beds)
    {
        int n = Count(beds);
        var list = new List<RobotKind>(n);
        for (int i = 0; i < First.Length && list.Count < n; i++) list.Add(First[i]);
        for (int i = 0; list.Count < n; i++) list.Add(Bulk[i % Bulk.Length]);
        return list.ToArray();
    }

    /// <summary>종류마다 정비대를 둘 방 (먼저 것부터 · 같은 종류 방이 여럿이면 덜 찬 방).</summary>
    public static RoomType[] HomeRooms(RobotKind k) => k switch
    {
        RobotKind.Cleaner => new[] { RoomType.Corridor, RoomType.Recycling, RoomType.Mess, RoomType.Lounge, RoomType.RobotBay, RoomType.Storage },
        RobotKind.Chef => new[] { RoomType.Galley, RoomType.Mess, RoomType.Freezer, RoomType.RobotBay, RoomType.Storage },
        RobotKind.Plumber => new[] { RoomType.PumpRoom, RoomType.WaterPlant, RoomType.Cooling, RoomType.LifeSupport, RoomType.Workshop, RoomType.RobotBay, RoomType.Storage },
        RobotKind.Lifter => new[] { RoomType.Cargo, RoomType.Storage, RoomType.Workshop, RoomType.RobotBay, RoomType.Corridor },
        RobotKind.Ventman => new[] { RoomType.HvacRoom, RoomType.LifeSupport, RoomType.Workshop, RoomType.RobotBay, RoomType.Storage },
        RobotKind.Rescuer => new[] { RoomType.Triage, RoomType.Medbay, RoomType.Corridor, RoomType.RobotBay, RoomType.Storage },
        RobotKind.Sealer => new[] { RoomType.SuppressionRoom, RoomType.Airlock, RoomType.EvaPrep, RoomType.Workshop, RoomType.RobotBay, RoomType.Corridor },
        RobotKind.Airman => new[] { RoomType.LifeSupport, RoomType.HvacRoom, RoomType.GasStorage, RoomType.RobotBay, RoomType.Storage },
        RobotKind.Medic or RobotKind.Nurse or RobotKind.Stretcher => new[] { RoomType.Medbay, RoomType.Triage, RoomType.Quarantine, RoomType.Hyperbaric, RoomType.RobotBay, RoomType.Corridor, RoomType.Storage },
        RobotKind.Firefighter or RobotKind.Sentry => new[] { RoomType.SuppressionRoom, RoomType.Workshop, RoomType.RobotBay, RoomType.Corridor, RoomType.Storage },
        RobotKind.Lineman => new[] { RoomType.Power, RoomType.Substation, RoomType.Workshop, RoomType.RobotBay, RoomType.Storage },
        RobotKind.Harvester or RobotKind.Tender => new[] { RoomType.Hydroponics, RoomType.Garden, RoomType.AlgaeLab, RoomType.MushroomFarm, RoomType.RobotBay, RoomType.Storage },
        RobotKind.Courier or RobotKind.Tanker => new[] { RoomType.Mess, RoomType.Galley, RoomType.Storage, RoomType.RobotBay, RoomType.Corridor },
        _ => Array.Empty<RoomType>(),
    };

    /// <summary>집 방이 없을 때 (어느 배에나 있는 방).</summary>
    public static readonly RoomType[] Fallback = { RoomType.RobotBay, RoomType.Storage, RoomType.Cargo, RoomType.Workshop, RoomType.Mess, RoomType.Lounge, RoomType.Galley };

    // ─────────────────────────────── 솜씨 ───────────────────────────────

    /// <summary>일 시간 배율: 종류 · 특기 아님 · 개량 단계 · 숙련.</summary>
    public static float WorkMul(Robot r, WorkOrder? o)
    {
        float m = RobotSystem.WorkFactor(r.Kind);
        if (General && o != null && !RobotSystem.Special(r.Kind, o.Kind)) m *= GeneralSlow;
        m *= 1f - 0.12f * (Math.Clamp(r.Mk, 1, 3) - 1);
        m *= 1f - 0.2f * r.Skill;
        return m;
    }

    public static float SpeedMul(Robot r) => 1f + 0.1f * (Math.Clamp(r.Mk, 1, 3) - 1);
    public static float FaultMul(Robot r) => (1f - 0.2f * (Math.Clamp(r.Mk, 1, 3) - 1)) * (1f - 0.3f * r.Skill);
}

public sealed partial class RobotSystem
{
    public RobotV21Stats V21 { get; } = new();
    private long _v21Minute, _v21Upgrade;
    private Rng? _v21Rng;
    private Rng R21 => _v21Rng ??= new Rng(unchecked(_world.Seed * 9173 + 211));

    /// <summary>불을 끄나 (방재 종류 · 긴급 지정된 로봇은 소화기를 든다).</summary>
    public static bool FightsNow(Robot r, World w) => RobotsV15.Fights(r.Kind) || r.Draft == DraftKind.Fire && r.Drafted(w);
    /// <summary>소화 거품 세기 · 오래가기 (방재가 아니면 작은 소화기).</summary>
    public static float FoamRateOf(Robot r) => RobotsV15.Fights(r.Kind) ? RobotsV15.FoamRate(r.Kind) : 6f;
    public static float FoamHoursOf(Robot r) => RobotsV15.Fights(r.Kind) ? RobotsV15.FoamHours(r.Kind) : 0.25f;

    /// <summary>원자로가 내장 전지를 채우는 빠르기 (출력 저하 · 폭주면 0).</summary>
    public static float ReactorRate(Robot r) =>
        !RobotsV21.Reactor || r.Fault == RobotFault.Cell || r.Runaway || r.Wrecked ? 0f : RobotsV21.ReactorPerHour * (0.5f + 0.5f * r.Condition);

    // ─────────────────────────────── 일 고르기 ───────────────────────────────

    /// <summary>이 로봇이 이 일을 맡을 만한가 (특기 · 사람 몫 손일 · 긴급 지정 · 기본 행동은 특기 로봇이 먼저).</summary>
    private bool Eligible(Robot r, WorkOrder o)
    {
        var w = _world;
        if (o.Kind == WorkKind.Cook && PeopleCook(o)) return false; // 요리사 · 배우는 사람이 있으면 사람이 짓는다 (고향 음식 · 맛 · 대신 서기는 사람의 이야기)
        if (RobotBody.Gripless(r) && RobotBody.HandJob(o.Kind)) return false; // v22 두 팔을 다 잃었다 — 나르기 · 밸브 · 차단기만
        if (Special(r.Kind, o.Kind) && RobotsV15.Takes(r.Kind, o)) return !RobotBody.ToolLost(r) || !RobotBody.HandJob(o.Kind); // v22 특기 도구를 잃으면 특기 일은 못 한다
        if (r.Drafted(w) && RobotsV21.DraftJob(r.Draft, o.Kind)) return true;
        if (Stands(r, o)) return !RobotsV21.General || Fixer(r.Kind) || Carrier(r.Kind) || Waited(o) || o.Kind is WorkKind.ResetBreaker or WorkKind.StartAux or WorkKind.CloseValve or WorkKind.OpenValve;
        if (!RobotsV21.General || !RobotsV21.Basic(o.Kind)) return false;
        if (RobotsV21.OnCall(r.Kind) && o.Urgency < 0.7f) return false; // 의료 · 구조 로봇은 자리를 지킨다
        return Waited(o);
    }

    /// <summary>
    /// 끼니는 사람이 먼저: 조리 책임자 · 배우는 사람이 있는 배면 45분 동안은 사람 몫 — 조리사가 다치면 배우던 사람이 대신 서고 맛이 달라진다.
    /// 조리 로봇은 그 뒤 (다들 바쁘거나 자는 밤) · 요리할 사람이 없는 배 · 무인 운항에서 짓는다.
    /// </summary>
    private bool PeopleCook(WorkOrder o)
    {
        var w = _world;
        if (w.Tick - o.Posted >= SimTime.Minutes(45) || w.Automation.Unattended) return false;
        var ck = w.Cooking;
        return ck.HeadCook is { Dead: false } && w.Crew.Any(c => c.CanAct && !c.IsChild && !c.Outside);
    }

    /// <summary>특기 로봇이 먼저 잡을 만큼 기다렸나 (급하거나 · 사람이 없으면 바로).</summary>
    private bool Waited(WorkOrder o) => o.Urgency >= 0.7f || _world.Tick - o.Posted >= SimTime.Minutes(RobotsV21.GeneralWaitMin) || _world.Automation.Unattended;

    // 칸 차지 (일할 칸 고르기 — 틱마다 한 번 모은다)
    private long _occTick = -1;
    private readonly HashSet<Cell> _crewCells = new();
    private readonly Dictionary<Cell, int> _botCells = new();

    private bool Occupied(Robot r, Cell s, out bool byBot)
    {
        if (_occTick != _world.Tick)
        {
            _occTick = _world.Tick;
            _crewCells.Clear();
            _botCells.Clear();
            foreach (var c in _world.Crew) if (!c.Dead) _crewCells.Add(c.Cell);
            foreach (var x in Robots) if (x.State == RobotState.Active) _botCells[x.Cell] = _botCells.GetValueOrDefault(x.Cell) + 1;
        }
        int bots = _botCells.GetValueOrDefault(s) - (r.State == RobotState.Active && r.Cell == s ? 1 : 0);
        byBot = bots > 0;
        return _crewCells.Contains(s);
    }

    // ─────────────────────────────── 매 시스템 틱 ───────────────────────────────

    /// <summary>원자로 · 멈춘 로봇 되살리기 · 1분마다 긴급 지정 · 몇 시간마다 개량.</summary>
    internal void TickV21(float dt)
    {
        var w = _world;
        foreach (var r in Robots)
        {
            if (r.State == RobotState.Lost || r.Wrecked || r.Aboard != null) continue;
            CoreTick(r, dt);
            // 방전으로 멈췄던 로봇: 원자로가 내장 전지를 다시 채우면 스스로 일어나 정비대로
            if (RobotsV21.Reactor && r.State == RobotState.Stalled && r.Fault == null && !r.Runaway && r.TowedBy == null && r.TowBot == null)
            {
                r.Battery = MathF.Min(1f, r.Battery + ReactorRate(r) * dt);
                if (r.Battery >= 0.3f)
                {
                    SetState(r, RobotState.Active);
                    GoHome(r, "원자로가 전지를 다시 채웠다 — 정비대로");
                }
            }
        }
        if (w.Tick >= _v21Minute)
        {
            _v21Minute = w.Tick + SimTime.Minutes(1);
            BodyMinute(); // v22 폭발 · 파편 · 물 · 진공 · 열 · 추위 · 방사선이 부위를 깎는다
            SprintWear(); // v23 지휘 건으로 뛴 구동부
            SalvageTick(); // v22 잔해 회수 · 해체 · 뜯어 살리기 · 새 몸 조립 (5분마다)
            DraftTick();
            CallSpecialists();
            DeliverFinds();
            JoinTick();
        }
        if (w.Tick >= _v21Upgrade)
        {
            _v21Upgrade = w.Tick + SimTime.Hours(3);
            UpgradeTick();
        }
    }

    /// <summary>특기 부르기 (1분마다): 특기 일이 올라왔는데 그 특기 로봇이 모두 남의 가벼운 일을 하고 있으면 하나를 불러 바꾼다.</summary>
    private void CallSpecialists()
    {
        var w = _world;
        if (!RobotsV21.General) return;
        foreach (var o in w.Board.RobotCandidates())
        {
            if (o.Closed || o.Assignee != null || o.Robot != null || o.Drone != null || o.Urgency < 0.3f) continue;
            Robot? best = null; float bd = float.MaxValue; bool idle = false, any = false;
            foreach (var r in Robots)
            {
                if (!r.Operational || r.Runaway || r.Upgrading || r.Wrecked || !Special(r.Kind, o.Kind) || !RobotsV15.Takes(r.Kind, o)) continue;
                any = true;
                if (r.State == RobotState.Docked || r.State == RobotState.Active && r.Steps == null)
                {
                    idle = true;
                    if (r.AtDock && r.NextDecide > w.Tick) r.NextDecide = w.Tick; // 정비대에서 기다리던 특기 로봇을 바로 깨운다
                    break;
                }
                if (Callable(r) && (r.Order == null || r.Order.Urgency < o.Urgency) && r.Partner == null && !r.Homing)
                {
                    float d = Far(r.Position, o.Target.Center);
                    if (d < bd) { bd = d; best = r; }
                }
            }
            if (!any || idle || best == null) continue;
            best.Mind.Say($"내 특기 일이 올라왔다 — {o.Title}. 하던 일은 내려놓는다", w.Tick);
            DropTask(best);
            Redirect(best, 0.9f);
        }
    }

    /// <summary>같이 하기 (1분마다): 오래 걸리는 일을 하는 로봇 곁에 한가한 로봇이 붙어 진척을 함께 채운다 (일 하나에 한 대).</summary>
    private void JoinTick()
    {
        var w = _world;
        if (!RobotsV21.General || FleetSystem.Off) return;
        foreach (var r in Robots)
        {
            if (r.State != RobotState.Active || r.Order is not WorkOrder o || r.Steps == null || r.StepIndex >= r.Steps.Count) continue;
            if (r.Steps[r.StepIndex] is not RWork rw || rw.Shared != o || o.Progress > 0.5f || rw.Hours < 0.4f) continue;
            if (o.Kind is not (WorkKind.ClearRubble or WorkKind.PumpOut or WorkKind.CleanUp or WorkKind.Maintain or WorkKind.SealSeep or WorkKind.FlushMain or WorkKind.ClearMain or WorkKind.Cook or WorkKind.Tend or WorkKind.Harvest)) continue;
            if (Robots.Any(x => x != r && x.Steps != null && x.StepIndex < x.Steps.Count && x.Steps[x.StepIndex] is RWork xw && xw.Shared == o)) continue;
            Robot? mate = null; float bd = 14f;
            foreach (var x in Robots)
            {
                if (x == r || !Free(x) || x.Runaway || x.Upgrading || RobotsV21.OnCall(x.Kind) || !CanDo(x.Kind, o.Kind)) continue;
                float d = Far(x.Position, r.Position);
                if (d < bd) { bd = d; mate = x; }
            }
            if (mate == null) continue;
            Cell? spot = null;
            foreach (var d in Cell.Dirs8) { var c = r.Cell + d; if (w.Ship.IsWalkable(c) && w.Ship.RoomAt(c) == r.Room) { spot = c; break; } }
            if (spot is not Cell at) continue;
            if (mate.State == RobotState.Docked) SetState(mate, RobotState.Active);
            var main = r;
            Begin(mate, new List<RobotStep>
            {
                new RGoto(at), new RWork(rw.Hours, o, r.Position) { CanContinue = (_, _) => !o.Closed },
                new RDo((rb, world) => { Done(rb, null); return true; }),
            }, $"{Ko.WaGwa(main.Name)} 같이 — {o.Title}");
            mate.Mind.Say($"{main.Name} 혼자 하기엔 오래 걸린다 — 같이 한다 ({o.Title})", w.Tick);
            V21.Joins++;
        }
    }

    /// <summary>남의 가벼운 일을 하는 특기 로봇은 제 특기 일에는 "놀고 있다"고 본다 (부르면 바꾼다).</summary>
    internal static bool Callable(Robot r) => r.State == RobotState.Active && r.Cargo == null && r.Hauling == null && r.Fixing == null && !r.FightingFire && !r.Runaway
        && (r.Order is WorkOrder ro ? !Special(r.Kind, ro.Kind) && ro.Urgency < 0.7f
            : r.Steps != null && r.StepIndex < r.Steps.Count && r.Steps.Skip(r.StepIndex).Any(s => s is RWork { Shared: not null })); // 남의 일을 거들던 (같이 하기) 로봇

    // ─────────────────────────────── 소형 원자로 ───────────────────────────────

    /// <summary>폭주 빈도: 로봇 · 시간당 (닳을수록 · 개량 단계가 낮을수록).</summary>
    private const float RunawayPerHour = 2e-5f;

    private void CoreTick(Robot r, float dt)
    {
        var w = _world;
        if (!RobotsV21.Reactor) return;
        bool nearFire = w.Fire.Count > 0 && w.Fire.AnyWithin(r.Cell, 1.2f);
        if (!r.Runaway)
        {
            // 불길 · 그을림 · 출력 저하는 열을 올린다 — 식는 것보다 빠르면 쌓인다
            float up = (nearFire ? (RobotsV15.Fireproof(r.Kind) ? 0.25f : 1.1f) : 0f) + (r.Fault == RobotFault.Scorched ? 0.15f : 0f) + (r.Fault == RobotFault.Cell ? 0.2f : 0f);
            r.CoreHeat = Math.Clamp(r.CoreHeat + (up - 0.35f) * dt, 0f, 1f);
            float hazard = RunawayPerHour * (1f + 6f * (1f - r.Condition)) * RobotsV21.FaultMul(r) * Durability.RobotFault;
            if (r.CoreHeat >= 0.6f || R21.Chance(hazard * dt)) StartRunaway(r, r.CoreHeat >= 0.6f ? "불길에 달아올랐다" : "노심 제어가 흔들렸다");
            return;
        }
        // 폭주 중: 식혀 주는 로봇이 곁에 있으면 내려간다 (RCool) · 아니면 오른다
        r.CoreHeat = Math.Clamp(r.CoreHeat + (nearFire ? 0.6f : 0.25f) * dt, 0f, 1f);
        if (r.CoreHeat < 0.35f)
        {
            r.CoreRunaway = -1;
            r.CoreBlowAt = -1;
            V21.Cooled++;
            if (r.Fault == null) Break(r, RobotFault.Cell);
            MarkLog.Add(r.Marks, w.Tick, "원자로 폭주를 식혀 막았다 — 출력 저하로 세웠다");
            w.Log.Add(w.Tick, LogKind.Ship, $"{r.Name} 소형 원자로 — 식혀서 폭주가 멎었다 (정지 · 수리 필요)");
            w.History.Add(w, HistoryKind.Response, $"{r.Name} 원자로 폭주를 거품으로 식혀 막았다", r.Room);
            return;
        }
        if (w.Tick >= r.CoreBlowAt) Blow(r);
    }

    /// <summary>폭주가 시작됐다: 먼저 스스로 긴급 정지를 걸어 본다 — 안 되면 사람 없는 방으로 걸어가 식혀 주기를 기다린다.</summary>
    internal void StartRunaway(Robot r, string why)
    {
        var w = _world;
        V21.Runaways++;
        float scram = 0.6f + 0.1f * (Math.Clamp(r.Mk, 1, 3) - 1) + 0.15f * r.Condition;
        if (R21.Chance(scram))
        {
            V21.Scrams++;
            r.CoreHeat = 0.2f;
            if (r.Fault == null) Break(r, RobotFault.Cell);
            MarkLog.Add(r.Marks, w.Tick, $"원자로 이상 ({why}) — 스스로 긴급 정지");
            w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name} 소형 원자로 이상 ({why}) — 스스로 긴급 정지했다 (수리 필요)");
            return;
        }
        r.CoreRunaway = w.Tick;
        r.CoreHeat = MathF.Max(r.CoreHeat, 0.8f);
        r.CoreBlowAt = w.Tick + SimTime.Minutes(R21.Range(3f, 7f));
        DropTask(r);
        r.Draft = DraftKind.None;
        MarkLog.Add(r.Marks, w.Tick, $"원자로 폭주 ({why})");
        string where = r.Room?.Name ?? "?";
        w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name} 소형 원자로 폭주 — {where} ({why}) · 몇 분 안에 터질 수 있다");
        w.History.Add(w, HistoryKind.Damage, $"{r.Name} 소형 원자로가 폭주했다 ({where})", r.Room);
        var a = w.Automation;
        if (a.Present && a.MainOnline) a.Speak.Announce(a.Voice.Style($"{where} — {r.Name} 원자로 폭주. 곁의 사람은 물러나라. 소방 로봇을 보낸다"), r.Room, 2);
        // 움직일 수 있으면 사람 없는 가까운 방으로 (정비대 · 통로가 아닌 곳)
        if (r.State is RobotState.Active or RobotState.Docked && r.Fault is null or RobotFault.Cell or RobotFault.Sensor)
        {
            var dist = w.Paths.Flood(r.Cell, Profile);
            Cell? best = null; int bd = int.MaxValue;
            foreach (var room in w.Ship.LiveRooms)
            {
                if (room.Type == RoomType.Corridor || room.Abandoned || w.Crew.Any(c => !c.Dead && c.Room == room)) continue;
                var spot = room.Cells.Where(w.Ship.IsOpenFloor).Where(dist.Reachable).OrderBy(c => (c.Center - room.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
                if (spot is not Cell s) continue;
                int d = dist.Get(s);
                if (d < bd) { bd = d; best = s; }
            }
            if (best is Cell to && bd < 5000)
            {
                if (r.State == RobotState.Docked) SetState(r, RobotState.Active);
                Begin(r, new List<RobotStep> { new RGoto(to), new RHold() }, "원자로 폭주 — 사람 없는 방으로 물러난다");
                r.Mind.Say("내 원자로가 폭주한다 — 사람에게서 떨어진다", w.Tick);
            }
        }
        SendCooler(r);
    }

    /// <summary>폭주한 로봇에게 거품을 가진 로봇 하나를 보낸다 (권한과 상관없이 — 배를 지키는 일).</summary>
    private void SendCooler(Robot hot)
    {
        var w = _world;
        Robot? best = null; float bd = float.MaxValue;
        foreach (var x in Robots)
        {
            if (x == hot || !FightsNow(x, w) || !x.Operational || x.Runaway || x.Foam < 0.2f || x.Wrecked || x.Hauling != null) continue;
            if (x.Steps?.Any(s => s is RCool) == true) continue;
            float d = Far(x.Position, hot.Position);
            if (d < bd) { bd = d; best = x; }
        }
        if (best == null) return;
        if (best.State == RobotState.Active) DropTask(best);
        if (best.State == RobotState.Docked) SetState(best, RobotState.Active);
        var steps = new List<RobotStep>
        {
            new RGoto(rb => { foreach (var d in Cell.Dirs8) { var c = hot.Cell + d; if (_world.Ship.IsWalkable(c)) return c; } return hot.Cell; }),
            new RCool(hot),
        };
        Begin(best, steps, $"폭주한 {Ko.EulReul(hot.Name)} 식히러");
        best.Mind.Say($"{hot.Name} 원자로 폭주 — 거품으로 식힌다", w.Tick);
        w.Fleet.Line(CmdTarget.Robot, best.Id, hot.Room, $"{best.Name}: {hot.Name} 원자로 식히기", "폭주 — 몇 분 안에 터질 수 있다", 0.95f, 10f);
    }

    private void Blow(Robot r)
    {
        var w = _world;
        V21.Blasts++;
        var at = r.Cell;
        string where = r.Room?.Name ?? "?";
        r.CoreRunaway = -1;
        r.CoreBlowAt = -1;
        Wreck(r, $"소형 원자로가 터졌다 ({where})");
        w.Blast.Detonate(at, 0.45f, BlastKind.Battery, $"{r.Name} 소형 원자로 폭발");
    }

    /// <summary>시험: 이 로봇의 원자로를 폭주시킨다 (긴급 정지 없이).</summary>
    public void ForceRunaway(Robot r, float minutes = 5f)
    {
        r.CoreRunaway = _world.Tick;
        r.CoreHeat = 0.85f;
        r.CoreBlowAt = _world.Tick + SimTime.Minutes(minutes);
        V21.Runaways++;
        DropTask(r);
        SendCooler(r);
    }

    // ─────────────────────────────── 긴급 지정 ───────────────────────────────

    private readonly SortedDictionary<string, long> _draftSaid = new(StringComparer.Ordinal);

    /// <summary>1분마다: 위기에 일손이 모자라면 맡은 일 밖의 로봇을 돌린다 (주 컴퓨터 · 로봇 지휘 권한에 따라).</summary>
    private void DraftTick()
    {
        var w = _world;
        var a = w.Automation;
        // 끝난 지정은 푼다
        foreach (var r in Robots)
            if (r.Draft != DraftKind.None && r.CaseId < 0 && (!r.Drafted(w) || !CrisisLasts(r.Draft, r.DraftRoom))) // v23 지휘 건 로봇은 지휘관이 푼다
            {
                r.Draft = DraftKind.None;
                r.DraftRoom = -1;
                if (r.Operational) r.Mind.Say("긴급 지정이 풀렸다 — 내 일로 돌아간다", w.Tick);
            }
        if (!RobotsV21.General || FleetSystem.Off || !a.Present || !a.MainOnline) return; // 컴퓨터가 멎으면 로봇은 처음 맡은 일을 한다
        if (w.Commander.Active) return; // v23 불 · 다친 사람 · 파공은 사고 지휘관이 지휘 건으로 직접 동원한다
        var level = a.Authority.Level(Domain.Fleet);
        // 불: 방마다 거품을 든 로봇이 모자라면
        if (w.Fire.Count > 0)
            foreach (var room in w.Ship.LiveRooms)
            {
                int cells = w.Fire.CountIn(room);
                if (cells == 0 || room.Abandoned) continue;
                if (!room.DataLinked && !a.FireCases.Any(fc => fc.RoomId == room.Id)) continue; // 컴퓨터가 아는 불만 (데이터선이 끊긴 방은 누가 알려 올 때까지 모른다)
                int want = Math.Clamp(1 + cells / 4, 1, 4);
                int have = Robots.Count(x => FightsNow(x, w) && x.Operational && x.Foam > 0.15f && (x.Room == room || x.FightingFire));
                Need(DraftKind.Fire, room, want - have, $"{room.Name} 불 {cells}칸 · 거품 든 로봇 {have}대", level);
            }
        // 쓰러진 사람: 옮길 로봇(들것 · 구조)이 모자라면
        int down = w.Board.All.Count(o => o.Kind == WorkKind.Rescue && !o.Closed && o.Assignee == null && o.Robot == null && w.Tick - o.Posted >= SimTime.Minutes(1));
        if (down > 0)
        {
            int carriers = Robots.Count(x => x.Operational && (x.Kind is RobotKind.Stretcher or RobotKind.Rescuer || x.Draft == DraftKind.Rescue && x.Drafted(w)) && (x.Steps == null || x.AtDock));
            var room = w.Board.All.First(o => o.Kind == WorkKind.Rescue && !o.Closed).Target.CurrentRoom;
            Need(DraftKind.Rescue, room, down - carriers, $"쓰러진 사람 {down}명 · 옮길 로봇 {carriers}대", level);
        }
        // 파공 · 침수: 맡는 손이 없이 2분 넘게 열린 일
        foreach (var o in w.Board.All)
        {
            if (o.Closed || o.Assignee != null || o.Robot != null || o.Drone != null || w.Tick - o.Posted < SimTime.Minutes(2)) continue;
            var kind = o.Kind == WorkKind.SealBreach && o.Target.Kind == TargetKind.Wall ? DraftKind.Breach : o.Kind == WorkKind.PumpOut && o.Urgency >= 0.5f ? DraftKind.Flood : DraftKind.None;
            if (kind == DraftKind.None) continue;
            var room = o.Target.CurrentRoom;
            if (Robots.Any(x => x.Draft == kind && x.DraftRoom == (room?.Id ?? -1) && x.Drafted(w))) continue;
            Need(kind, room, 1, $"{o.Title} — {(int)((w.Tick - o.Posted) / SimTime.Minutes(1))}분째 아무도 못 맡았다", level);
        }
    }

    private bool CrisisLasts(DraftKind d, int roomId)
    {
        var w = _world;
        var room = roomId >= 0 && roomId < w.Ship.Rooms.Count ? w.Ship.Rooms[roomId] : null;
        return d switch
        {
            DraftKind.Fire => room != null ? w.Fire.CountIn(room) > 0 : w.Fire.Count > 0,
            DraftKind.Rescue => w.Board.All.Any(o => o.Kind == WorkKind.Rescue && !o.Closed) || w.MedBots.Wants0(),
            DraftKind.Breach => w.Board.All.Any(o => o.Kind is WorkKind.SealBreach or WorkKind.CrankDoor && !o.Closed && (room == null || o.Target.CurrentRoom == room)),
            DraftKind.Flood => w.Board.All.Any(o => o.Kind is WorkKind.PumpOut or WorkKind.ShutRoomValve && !o.Closed && (room == null || o.Target.CurrentRoom == room)),
            _ => false,
        };
    }

    /// <summary>모자란 만큼 돌린다 — 권한이 자동이면 바로, 제안이면 묻고, 조언이면 기록만.</summary>
    private void Need(DraftKind kind, Room? room, int lack, string why, AuthLevel level)
    {
        var w = _world;
        if (lack <= 0) return;
        var picks = Pick(kind, room, lack);
        if (picks.Count == 0) return;
        string key = $"draft:{kind}:{room?.Id ?? -1}";
        string what = $"로봇 {picks.Count}대를 {RobotsV21.DraftName(kind)}{(room != null ? $" ({room.Name})" : "")}에 돌린다";
        var a = w.Automation;
        switch (level)
        {
            case AuthLevel.Auto:
                Apply(kind, room, picks, why);
                break;
            case AuthLevel.Propose:
                if (a.Asks.Pending(key) != null || _draftSaid.TryGetValue(key, out var t0) && w.Tick - t0 < SimTime.Minutes(10)) return;
                _draftSaid[key] = w.Tick;
                V21.DraftAsks++;
                var ids = picks.Select(p => p.Id).ToArray();
                a.Asks.Propose(key, "draft", room, what, why, $"맡은 일을 잠깐 내려놓고 {RobotsV21.DraftName(kind)}", 3f, null,
                    (world, p) =>
                    {
                        var bots = world.Robots.Robots.Where(x => ids.Contains(x.Id) && x.Operational && !x.Runaway).ToList();
                        world.Robots.Apply(kind, room, bots, why);
                    },
                    (world, p) => (world.Robots.CrisisLasts(kind, room?.Id ?? -1) ? 2 : 1, world.Robots.CrisisLasts(kind, room?.Id ?? -1) ? "보류 — 아직 위기" : "맞았다 — 위기가 걷혔다"));
                break;
            default:
                if (_draftSaid.TryGetValue(key, out var t1) && w.Tick - t1 < SimTime.Minutes(30)) return;
                _draftSaid[key] = w.Tick;
                V21.DraftAdvice++;
                a.Book.Add(ActKind.Advice, room, why, $"{what} — 권한이 조언뿐이다", "사람에게 맡긴다", "", key, SimTime.Minutes(30), 30f);
                break;
        }
    }

    private List<Robot> Pick(DraftKind kind, Room? room, int lack)
    {
        var w = _world;
        var target = room?.Center ?? Vector2.Zero;
        var list = new List<(Robot r, float d)>();
        foreach (var r in Robots)
        {
            if (!Free(r) || r.Drafted(w) || r.Runaway || r.Upgrading) continue;
            if (kind == DraftKind.Fire && RobotsV15.Fights(r.Kind)) continue; // 방재 로봇은 원래 간다
            if (kind == DraftKind.Rescue && r.Kind is RobotKind.Stretcher or RobotKind.Rescuer) continue;
            if (kind == DraftKind.Breach && RobotSystem.Special(r.Kind, WorkKind.SealBreach)) continue;
            if (RobotsV21.Medical(r.Kind) && kind != DraftKind.Rescue) continue; // 의료 로봇은 다친 사람 곁에
            list.Add((r, room != null ? Far(r.Position, target) : 0f));
        }
        return list.OrderBy(x => x.d).ThenBy(x => x.r.Id).Take(lack).Select(x => x.r).ToList();
    }

    internal void Apply(DraftKind kind, Room? room, List<Robot> bots, string why)
    {
        var w = _world;
        foreach (var r in bots)
        {
            r.Draft = kind;
            r.DraftRoom = room?.Id ?? -1;
            r.DraftUntil = w.Tick + SimTime.Minutes(40);
            if (kind == DraftKind.Fire) r.Foam = MathF.Max(r.Foam, 0.6f); // 정비대 곁 소화기를 집어 든다
            V21.Drafts++;
            r.Mind.Say($"주 컴퓨터 긴급 지정 — {RobotsV21.DraftName(kind)} ({why})", w.Tick);
            MarkLog.Add(r.Marks, w.Tick, $"긴급 지정: {RobotsV21.DraftName(kind)}");
            w.Fleet.Line(CmdTarget.Robot, r.Id, room, $"{r.Name}: 긴급 지정 — {RobotsV21.DraftName(kind)}", why, 0.9f, 40f);
            if (r.State == RobotState.Active && r.Order is { Urgency: < 0.7f }) DropTask(r);
            Redirect(r, 0.9f);
        }
        if (bots.Count > 0)
            w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: 로봇 {bots.Count}대 긴급 지정 — {RobotsV21.DraftName(kind)}{(room != null ? $" ({room.Name})" : "")} · {string.Join(" · ", bots.Select(b => b.Name))}");
    }

    // ─────────────────────────────── 개량 · 숙련 ───────────────────────────────

    /// <summary>한 일을 마쳤다: 특기 일이면 숙련이 는다.</summary>
    private static void Learn(Robot r)
    {
        bool special = r.Order is WorkOrder o && Special(r.Kind, o.Kind);
        r.Skill = MathF.Min(1f, r.Skill + (1f - r.Skill) * (special ? 0.012f : 0.004f));
    }

    private static (ItemKind kind, int count)[] MkParts(int to) => to switch
    {
        2 => new[] { (ItemKind.Electronics, 1), (ItemKind.Motor, 1) },
        _ => new[] { (ItemKind.Electronics, 1), (ItemKind.Sensor, 1), (ItemKind.Motor, 1) },
    };

    /// <summary>패널 뒤에서 꺼낸 쪽지 · 술병: 하던 일을 마친 로봇이 가장 가까운 깨어 있는 사람에게 가져다준다 (로봇은 읽지 않는다).</summary>
    private void DeliverFinds()
    {
        var w = _world;
        foreach (var f in w.Origin.Finds)
        {
            if (f.Found || f.Courier < 0) continue;
            var r = Robots.FirstOrDefault(x => x.Id == f.Courier);
            if (r == null || !r.Operational || r.Runaway) { f.Courier = -1; continue; }
            if (!Free(r)) continue; // 하던 일부터
            CrewMember? Near(Robot rb) => w.Crew.Where(c => !c.Dead && c.CanAct && !c.IsChild && c.IsAwake && !c.Outside && c.Room is Room cr && !cr.Abandoned)
                .OrderBy(c => (c.Position - rb.Position).LengthSquared()).ThenBy(c => c.Id).FirstOrDefault();
            if (Near(r) == null) continue;
            var find = f;
            var room = w.Ship.Rooms[f.RoomId].Name;
            if (r.State == RobotState.Docked) SetState(r, RobotState.Active);
            Begin(r, new List<RobotStep>
            {
                new RGoto(rb => Near(rb)?.Cell),
                new RDo((rb, world) =>
                {
                    var c = Near(rb);
                    if (c == null || (c.Position - rb.Position).Length() > 3f) { find.Courier = rb.Id; Done(rb, null); return true; } // 그새 자리를 떴다 — 다음에 다시 따라간다
                    world.Origin.HandOver(find, c, $"{Ko.IGa(rb.Name)} {room} 패널 뒤에서 꺼내 온");
                    Done(rb, null);
                    return true;
                }),
            }, $"찾은 {find.KindName} 건네기");
            f.Courier = -1;
        }
    }

    /// <summary>마지막으로 개량을 시작한 때 (하루에 한 대까지 — 부품은 고치는 데 먼저 쓴다).</summary>
    public long LastUpgrade { get; internal set; } = -SimTime.TicksPerDay;

    /// <summary>몇 시간마다: 평시에 · 부품이 넉넉하면 · 일을 많이 한 로봇 하나를 정비 로봇이 개량한다 (하루에 한 대).</summary>
    private void UpgradeTick()
    {
        var w = _world;
        if (FleetSystem.Off || w.Fleet.Mode != "평시" || Robots.Any(x => x.Upgrading) || w.Tick - LastUpgrade < SimTime.TicksPerDay) return;
        var b = Robots.Where(x => x.AtDock && x.Operational && !x.Runaway && x.Mk < 3 && x.Fault == null && x.JobsDone >= 30 * x.Mk && !x.Dock.Room.Detached)
            .OrderByDescending(x => x.JobsDone - 30 * x.Mk).ThenBy(x => x.Id).FirstOrDefault();
        if (b == null) return;
        var parts = MkParts(b.Mk + 1);
        if (!parts.All(p => w.Ship.CountStored(p.kind) >= p.count + 6)) return; // 수리 몫은 넉넉히 남긴다
        Robot? fixer = null; float bd = float.MaxValue;
        foreach (var r in Robots)
        {
            if (r == b || !Free(r) || !r.AtDock || !(Fixer(r.Kind) || RobotsV15.Base(r.Kind) == RobotKind.Maintainer)) continue;
            float d = Far(r.DockPosition, b.DockPosition);
            if (d < bd) { bd = d; fixer = r; }
        }
        if (fixer == null) return;
        var dist = w.Paths.Flood(fixer.Cell, Profile);
        var (main, mainN) = parts[0];
        var (box, spot) = Nearest(w, dist, x => x.Storage!.Count(main) >= mainN);
        var near = b.Dock.UseSpots.Where(c => w.Ship.IsWalkable(c) && dist.Reachable(c)).OrderBy(c => c.X).ThenBy(c => c.Y).Cast<Cell?>().FirstOrDefault();
        if (box == null || near is not Cell at) return;
        int to = b.Mk + 1;
        b.Upgrading = true;
        LastUpgrade = w.Tick;
        SetState(fixer, RobotState.Active);
        var steps = new List<RobotStep>
        {
            new RGoto(spot), new RTake(box, main, mainN), new RGoto(at),
            new RWork(1.2f, null, b.DockPosition) { CanContinue = (_, _) => b.AtDock && b.Operational },
            new RDo((rb, world) => world.Robots.Upgraded(rb, b, to, parts)),
        };
        fixer.Fixing = b;
        Begin(fixer, steps, $"{Ko.EulReul(b.Name)} {RobotsV21.MkName(to)}로 개량");
        fixer.Mind.Say($"{b.Name} — 일 {b.JobsDone}번 · 부품이 넉넉하다 · {RobotsV21.MkName(to)}로 올린다", w.Tick);
        w.Fleet.Line(CmdTarget.Robot, fixer.Id, b.Dock.Room, $"{fixer.Name}: {b.Name} 개량", $"{RobotsV21.MkName(b.Mk)} → {RobotsV21.MkName(to)}", 0.3f, 90f);
    }

    internal bool Upgraded(Robot r, Robot b, int to, (ItemKind kind, int count)[] parts)
    {
        var w = _world;
        r.Fixing = null;
        b.Upgrading = false;
        var (main, mainN) = parts[0];
        if (r.Cargo is not ItemStack held || held.Kind != main || held.Count < mainN) { Done(r, null); return true; }
        if (!parts.Skip(1).All(p => w.Ship.CountStored(p.kind) >= p.count)) { Done(r, $"{b.Name} 개량 — 부품이 모자라 그만뒀다"); return true; }
        r.Cargo = held.Count > mainN ? new ItemStack(main, held.Count - mainN) : null;
        foreach (var (k, n) in parts.Skip(1))
        {
            int left = n;
            foreach (var c in w.Ship.Containers.Where(c => !c.Room.Detached && c.Type != FurnitureType.DroneDock).OrderBy(c => (c.Center - b.DockPosition).LengthSquared()))
            {
                if (left <= 0) break;
                left -= c.Storage!.Take(k, left);
            }
        }
        b.Mk = to;
        b.Condition = MathF.Max(b.Condition, 0.95f);
        V21.Upgrades++;
        MarkLog.Add(b.Marks, w.Tick, $"{Ko.IGa(r.Name)} {RobotsV21.MkName(to)}로 개량했다");
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(r.Name)} {Ko.EulReul(b.Name)} {RobotsV21.MkName(to)}로 개량했다 (빨라지고 덜 고장 난다)", b.Dock.Room);
        Done(r, $"{Ko.EulReul(b.Name)} {RobotsV21.MkName(to)}로 개량했다");
        return true;
    }

    // ─────────────────────────────── 새 특기 일 ───────────────────────────────

    /// <summary>v21 새 일의 단계 (청소 · 조리 · 물 퍼내기 · 댐퍼 · 식히기 · 깨우기 · 봉합 · 격벽 · 산소관 · 역화 · 치료).</summary>
    private List<RobotStep>? PlanV21(Robot r, WorkOrder o, Cell at, DistanceField dist, out string? blocked)
    {
        blocked = null;
        var w = _world;
        var steps = new List<RobotStep>();
        switch (o.Kind)
        {
            case WorkKind.CleanUp:
            {
                if (o.Target.Furniture?.Machine is not Machine m) { w.Board.Close(o); return null; }
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.25f, o, m.Body.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed) return true;
                    m.Fouled = 0f;
                    world.Volatile.Stats.Cleaned++;
                    MarkLog.Add(m.Marks, world.Tick, $"{rb.Name}: 분말·그을음을 닦았다");
                    world.Board.Close(o);
                    Done(rb, null);
                    return true;
                }));
                return steps;
            }
            case WorkKind.ClearRubble:
            {
                var cell = o.Target.Cell;
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.3f + 0.3f * w.Ship.Rubble.GetValueOrDefault(cell), o, cell.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    world.Board.Close(o);
                    if (!world.Ship.Rubble.Remove(cell)) return true;
                    if (world.Ship.DoorAt(cell) is Door door) door.Blocked = false;
                    world.Volatile.Stats.RubbleCleared++;
                    world.Paths.Invalidate();
                    bool plate = world.Rng.Chance(0.35f) && world.Ship.Containers.FirstOrDefault(f => f.Storage!.Free > 0) is Furniture box && box.Storage!.Add(ItemKind.Plate, 1) > 0;
                    Done(rb, plate ? "잔해를 치웠다 — 쓸 만한 금속판 하나를 건졌다" : "잔해를 치웠다");
                    return true;
                }));
                return steps;
            }
            case WorkKind.Cook:
            {
                var stove = o.Target.Furniture!;
                var (box, spot) = Nearest(w, dist, b => b.Type != FurnitureType.DroneDock && b.Storage!.Count(ItemKind.Produce) >= FoodChain.ProducePerBatch);
                if (box == null) { blocked = "채소 부족"; return null; }
                steps.Add(new RGoto(spot));
                steps.Add(new RTake(box, ItemKind.Produce, FoodChain.ProducePerBatch));
                steps.Add(new RGoto(at));
                steps.Add(new RWork(FoodChain.CookHours, o, stove.Center) { CanContinue = (_, _) => stove.Machine!.Efficiency > 0f });
                steps.Add(new RDo((rb, world) =>
                {
                    if (rb.Cargo is not ItemStack held || held.Kind != ItemKind.Produce || held.Count < FoodChain.ProducePerBatch) return false;
                    rb.Cargo = new ItemStack(ItemKind.Meal, FoodChain.MealsPerBatch);
                    V21.Meals += FoodChain.MealsPerBatch;
                    world.Board.Close(o);
                    Done(rb, $"식사 {FoodChain.MealsPerBatch}인분을 지었다");
                    return true;
                }));
                var dispenser = w.Ship.FurnitureOf(FurnitureType.MealDispenser)
                    .Where(d => d.UseSpots.Count > 0 && dist.Reachable(d.UseSpots[0]) && d.Storage!.Free > 0).OrderByDescending(d => d.Storage!.Free).FirstOrDefault();
                if (dispenser != null) { steps.Add(new RGoto(dispenser.UseSpots[0])); steps.Add(new RPut(dispenser)); }
                var (fridge, fs) = Nearest(w, dist, f => f.Type == FurnitureType.Fridge && f.Storage!.Free > 0);
                if (fridge != null) { steps.Add(new RGoto(_ => r.Cargo != null ? fs : null)); steps.Add(new RPut(fridge)); }
                return steps;
            }
            case WorkKind.PumpOut:
            {
                var room = o.Target.CurrentRoom;
                if (room == null || room.Flood < 1f) { if (room != null) w.Board.Close(o); return null; }
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.5f, o, at.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    float take = MathF.Min(room.Flood, (rb.Kind == RobotKind.Plumber ? 140f : 100f) * (world.History.Doctrine.FloodDrill ? 1.5f : 1f));
                    room.Flood -= take;
                    float back = take * 0.7f;
                    world.Water.Drain(back);
                    world.Moisture.Stats.Pumped += take;
                    world.Moisture.Stats.Recovered += back;
                    V21.Pumped++;
                    if (MoistureSystem.Depth(room) < 0.06f) world.Board.Close(o); else o.Progress = 0f;
                    Done(rb, $"{room.Name} 바닥 물을 퍼냈다 ({take:0}L · {back:0}L는 정수기로)");
                    return true;
                }));
                return steps;
            }
            case WorkKind.ShutRoomValve or WorkKind.OpenRoomValve:
            {
                var room = o.Target.CurrentRoom;
                if (room == null) { w.Board.Close(o); return null; }
                bool shut = o.Kind == WorkKind.ShutRoomValve;
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.1f, o, at.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    world.Board.Close(o);
                    room.ValveShut = shut;
                    if (shut) world.Moisture.Stats.Valves++;
                    MarkLog.Add(room.Marks, world.Tick, $"{rb.Name}: 급수 밸브 {(shut ? "잠금" : "다시 엶")}");
                    Done(rb, $"{room.Name} 급수 밸브를 {(shut ? "잠갔다 — 새는 물이 멈춘다" : "다시 열었다")}");
                    return true;
                }));
                return steps;
            }
            case WorkKind.OperateDamper:
            {
                var room = o.Target.Room!;
                bool jammed = room.DamperJammed;
                steps.Add(new RGoto(at));
                steps.Add(new RWork(jammed ? 0.75f : room.DamperStuck ? 0.5f : 0.25f, null, room.DamperSpot.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    if (room.DamperJammed)
                    {
                        float chance = rb.Kind == RobotKind.Ventman ? 0.75f : 0.5f;
                        if (!world.Rng.Chance(chance + 0.2f * rb.Skill))
                        {
                            world.Board.Block(o, null, 0.3f);
                            Done(rb, $"{room.Name} 환기 댐퍼가 열에 걸려 꿈쩍도 안 한다");
                            return true;
                        }
                        room.DamperJammed = false;
                        world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(rb.Name)} 열에 걸린 {room.Name} 환기 댐퍼를 비틀어 닫았다", room);
                    }
                    room.DamperStuck = false;
                    room.VentOpen = Hull.WantVentOpen(world, room);
                    V21.Dampers++;
                    world.Board.Close(o);
                    Done(rb, $"{room.Name} 환기 댐퍼를 손으로 {(room.VentOpen ? "열었다" : "닫았다")}");
                    return true;
                }));
                return steps;
            }
            case WorkKind.CoolDown:
            {
                if (o.Target.Furniture?.Machine is not Machine m) { w.Board.Close(o); return null; }
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.15f, null, m.Body.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    world.Board.Close(o);
                    m.Parked = true;
                    m.CoolingDown = true;
                    m.Heat = MathF.Max(0f, m.Heat - 0.15f);
                    m.Vapor = MathF.Max(0f, m.Vapor - 0.3f);
                    world.Volatile.Stats.CooledDown++;
                    if (!m.Body.Room.VentOpen && m.Vapor > 0.2f) m.Body.Room.VentOpen = true;
                    MarkLog.Add(m.Marks, world.Tick, $"{rb.Name}: 달아올라 내려 식힘 ({m.Heat * 100:0}%)");
                    Done(rb, $"{Ko.EulReul(m.Name)} 내려 식힌다 (열 {m.Heat * 100:0}%)");
                    return true;
                }));
                return steps;
            }
            case WorkKind.WakeCrew:
            {
                if (o.Target.Crew is not CrewMember sleeper) { w.Board.Close(o); return null; }
                var near = Cell.Dirs8.Select(d => sleeper.Cell + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
                if (near is not Cell spot) { blocked = "곁에 갈 수 없다"; return null; }
                steps.Add(new RGoto(spot));
                steps.Add(new RWork(0.02f, null, sleeper.Position));
                steps.Add(new RDo((rb, world) =>
                {
                    world.Board.Close(o);
                    if (sleeper.Pose != Pose.Sleeping) return true;
                    sleeper.DeepAsleep = false;
                    sleeper.EndJob(world, ToilStatus.Interrupted);
                    sleeper.Pose = Pose.Standing;
                    sleeper.Interrupt(world);
                    sleeper.Needs.Stress = MathF.Min(1f, sleeper.Needs.Stress + 0.08f);
                    sleeper.Say(world, "뭐, 뭐야?! 로봇이…");
                    V21.Wakes++;
                    Done(rb, $"{Ko.EulReul(sleeper.Name)} 경보음으로 깨웠다 ({Crisis.Now(world).Top})");
                    return true;
                }));
                return steps;
            }
            case WorkKind.SealBreach when o.Target.Kind == TargetKind.Wall:
            {
                var cell = o.Target.Cell;
                if (w.Ship.WallAt(cell) is not WallState wall || o.Target.Room is not Room room) { w.Board.Close(o); return null; }
                int sealant = Hull.SealantFor(wall);
                bool plate = wall.FrameLost || w.Ship.CountStored(ItemKind.Sealant) < sealant;
                var item = plate ? ItemKind.Plate : ItemKind.Sealant;
                int need = plate ? 2 : sealant;
                var (box, spot) = Nearest(w, dist, b => b.Storage!.Count(item) >= need);
                if (box == null) { blocked = plate ? "금속판 없음" : "실링폼 없음"; return null; }
                steps.Add(new RGoto(spot));
                steps.Add(new RTake(box, item, need));
                steps.Add(new RGoto(at));
                steps.Add(new RWork(plate ? 1.2f : wall.Breach >= 0.25f ? 0.6f : 0.35f, o, cell.Center) { CanContinue = (rb, _) => rb.Cargo?.Kind == item });
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed) return true;
                    if (rb.Cargo is not ItemStack held || held.Kind != item || held.Count < need) return false;
                    rb.Cargo = held.Count > need ? new ItemStack(item, held.Count - need) : null;
                    // 로봇은 바람에 당황하지 않는다 — 그래도 큰 구멍은 가끔 안 붙는다
                    if (!plate && world.Rng.Chance((wall.Breach >= 0.25f ? 0.12f : 0.06f) * (1f - 0.5f * rb.Skill)))
                    {
                        world.Board.Block(o, null, 0.05f);
                        MarkLog.Add(wall.Marks, world.Tick, $"{rb.Name}: 봉합 실패");
                        Done(rb, $"{room.Name} 파공에 실링폼이 제대로 붙지 않았다 — 다시");
                        return true;
                    }
                    wall.Patched = true;
                    wall.PatchQuality = (plate ? 0.65f : 0.7f) + 0.2f * rb.Skill;
                    wall.Seals++;
                    V21.Seals++;
                    MarkLog.Add(wall.Marks, world.Tick, $"{rb.Name}: {(plate ? "금속판 덮개" : "실링폼")} 봉합 ({wall.PatchQuality * 100:0}%)");
                    world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(rb.Name)} {room.Name} 파공을 안쪽에서 {(plate ? "금속판으로" : "실링폼으로")} 막았다", room, null, cell);
                    world.Board.Close(o);
                    Done(rb, $"{room.Name} 파공을 안쪽에서 막았다 (봉합 {wall.PatchQuality * 100:0}%)");
                    return true;
                }));
                return steps;
            }
            case WorkKind.CrankDoor:
            {
                if (o.Target.Door is not Door door) { w.Board.Close(o); return null; }
                bool jammed = door.JammedOpen;
                steps.Add(new RGoto(at));
                steps.Add(new RWork(jammed ? 0.5f : 0.2f, null, door.Cell.Center) { CanContinue = (_, _) => jammed ? door.JammedOpen : !door.Locked });
                steps.Add(new RDo((rb, world) =>
                {
                    if (door.JammedOpen)
                    {
                        if (!world.Rng.Chance(0.55f + 0.2f * rb.Skill)) { world.Board.Block(o, null, 0.3f); Done(rb, "휜 문이 꿈쩍도 안 한다"); return true; }
                        door.JammedOpen = false;
                        door.Openness = 0f;
                    }
                    door.Locked = true;
                    world.Board.Close(o);
                    Done(rb, $"격벽을 손으로 돌려 닫았다 ({o.Target.Room?.Name})");
                    return true;
                }));
                return steps;
            }
            case WorkKind.SealO2Line:
            {
                var room = o.Target.Room!;
                var (box, spot) = Nearest(w, dist, b => b.Storage!.Count(ItemKind.Sealant) > 0);
                if (box == null) { blocked = "실링폼 없음"; return null; }
                steps.Add(new RGoto(spot));
                steps.Add(new RTake(box, ItemKind.Sealant, 1));
                steps.Add(new RGoto(at));
                steps.Add(new RWork(0.4f, o, room.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed) return true;
                    if (rb.Cargo?.Kind != ItemKind.Sealant) return false;
                    rb.Cargo = rb.Cargo.Value.Count > 1 ? new ItemStack(ItemKind.Sealant, rb.Cargo.Value.Count - 1) : null;
                    room.O2Leak = 0f;
                    MarkLog.Add(room.Marks, world.Tick, $"{rb.Name}: 산소관 누출을 막았다");
                    world.Board.Close(o);
                    Done(rb, $"{room.Name} 산소관 누출을 막았다");
                    return true;
                }));
                return steps;
            }
            case WorkKind.BleedRoom:
            {
                var room = o.Target.Room!;
                var door = room.Doors.Where(d => !d.Removed).Select(d => (d, other: d.RoomA == room ? d.RoomB : d.RoomA))
                    .Where(x => x.other != null && x.other.Air.O2 > 15f).Select(x => x.d).FirstOrDefault();
                if (door == null) { blocked = "공기를 넣을 문이 없다"; return null; }
                var outside = Cell.Dirs4.Select(d => door.Cell + d).Where(x => w.Ship.RoomAt(x) != room && w.Ship.IsWalkable(x) && dist.Reachable(x)).Cast<Cell?>().FirstOrDefault();
                if (outside is not Cell spot) { blocked = "문 앞에 설 자리가 없다"; return null; }
                steps.Add(new RGoto(spot));
                steps.Add(new RWork(0.4f, null, door.Cell.Center));
                steps.Add(new RDo((rb, world) =>
                {
                    world.Board.Close(o);
                    var inner = room.Cells.Where(x => world.Ship.IsOpenFloor(x)).OrderBy(x => (x.Center - door.Cell.Center).LengthSquared()).FirstOrDefault();
                    if (inner != default) world.Fire.Ignite(inner, 0.15f);
                    room.Backdraft = 0f;
                    room.BackdraftKnown = false;
                    room.Air.O2 += 4f;
                    world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(rb.Name)} {room.Name} 문틈으로 공기를 조금씩 넣어 역화를 막았다", room);
                    Done(rb, $"{room.Name}에 문틈으로 조금씩 공기를 넣어 고인 가스를 태웠다");
                    return true;
                }));
                return steps;
            }
            case WorkKind.Treat:
            {
                if (o.Target.Crew is not CrewMember pt || pt.Dead) { w.Board.Close(o); return null; }
                if (w.Blood.NeedsBlood(pt) && (w.Blood.CompatibleFor(pt) > 0 || w.Pharmacy.Stock(ItemKind.BloodSubstitute) > 0) && pt.Vitals.Health > 0.08f)
                { blocked = "수혈이 먼저"; return null; } // 피를 많이 잃은 사람: 피부터 (치료로 체력만 올리면 일어나 투여 펌프 침대를 떠났다)
                var (box, spot) = Nearest(w, dist, b => b.Type != FurnitureType.DroneDock && b.Storage!.Count(ItemKind.MedKit) > 0);
                bool kit = box != null;
                if (box != null) { steps.Add(new RGoto(spot)); steps.Add(new RTake(box, ItemKind.MedKit, 1)); }
                Cell? Beside(Robot rb) => (pt.Position - rb.Position).Length() < 2f || pt.Dead ? null
                    : Cell.Dirs8.Select(d => pt.Cell + d).Where(x => w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - rb.Position).LengthSquared()).Cast<Cell?>().FirstOrDefault();
                steps.Add(new RGoto(at));
                steps.Add(new RGoto(Beside));
                steps.Add(new RWork(0.5f, null, pt.Position) { CanContinue = (rb, _) => (!kit || rb.Cargo?.Kind == ItemKind.MedKit) && (pt.Position - rb.Position).Length() < 2.4f });
                steps.Add(new RDo((rb, world) =>
                {
                    if (o.Closed || pt.Dead) { world.Board.Close(o); return true; }
                    bool used = kit && rb.Cargo?.Kind == ItemKind.MedKit;
                    if (used) rb.Cargo = rb.Cargo!.Value.Count > 1 ? new ItemStack(ItemKind.MedKit, rb.Cargo.Value.Count - 1) : null;
                    bool bandage = !used && ItemsV15.Use(world, ItemKind.Bandage);
                    // 의사 로봇은 보통 의무관만큼 (숙련이 오르면 더) · 다른 로봇은 응급 처치만
                    float skill = (rb.Kind == RobotKind.Medic ? 0.55f : 0.3f) + 0.3f * rb.Skill, eff = used ? 1f : bandage ? 0.75f : 0.5f;
                    pt.Vitals.Health = MathF.Min(pt.Vitals.MaxHealth, pt.Vitals.Health + (0.15f + 0.25f * skill) * eff * MathF.Max(0.25f, world.Perils.MarrowMul(pt)));
                    pt.Vitals.Injury = MathF.Max(0f, pt.Vitals.Injury - (0.06f + 0.14f * skill) * eff);
                    pt.Vitals.TreatedTick = world.Tick;
                    if (used && rb.Kind == RobotKind.Medic) world.Ailments.TreatedByBot(pt, rb.Name); // 의사 로봇: 진단하고 약을 쓴다
                    V21.Treats++;
                    world.Board.Close(o);
                    Done(rb, $"{Ko.EulReul(pt.Name)} 치료했다 ({(used ? "구급 키트" : bandage ? "붕대" : "천과 소독약")} · 체력 {pt.Vitals.Health * 100:0}%)");
                    return true;
                }));
                return steps;
            }
        }
        return null;
    }

    /// <summary>
    /// 바닥 얼룩 (기름 · 그을음 · 미끄러운 물기 — 사고 흔적) — 사람의 "배 손보기"와 같은 기준.
    /// 깨진 컵 조각은 사람이 빗자루로 쓴다 (밥 먹고 쓸어 담는 사람 · 누구 컵이었나) — 폭발 · 불 자리의 유리만 그을음과 함께 치운다.
    /// </summary>
    private static bool Stained(World w, CellState s, int i) =>
        s.V[(int)CellMark.Oil] > 0.3f || s.V[(int)CellMark.Soot] > 0.4f && w.Fire.Count == 0
        || s.V[(int)CellMark.Wet] > 0.4f && w.Body.SlipAt(i) > 0.35f;

    /// <summary>청소 로봇: 쉬는 동안 얼룩이 가장 많은 방에 가서 바닥을 닦는다 (기름 · 그을음 · 물기 — 네 칸까지).</summary>
    private bool TryWipe(Robot r, DistanceField dist)
    {
        var w = _world;
        if (r.Kind != RobotKind.Cleaner || w.Tick < r.NextPatrol) return false;
        r.NextPatrol = w.Tick + SimTime.Minutes(40);
        var byRoom = new SortedDictionary<int, List<Cell>>();
        foreach (var (i, st) in w.Body.Marks)
        {
            if (!Stained(w, st, i)) continue;
            var cell = w.Ship.Grid.CellAt(i);
            if (w.Ship.RoomAt(cell) is not Room room || room.Abandoned || room.OffLimits || w.Fire.CountIn(room) > 0 || !dist.Reachable(cell) && !Cell.Dirs4.Any(d => dist.Reachable(cell + d))) continue;
            if (!byRoom.TryGetValue(room.Id, out var list)) byRoom[room.Id] = list = new List<Cell>();
            list.Add(cell);
        }
        if (byRoom.Count == 0) return false;
        var (rid, cells) = byRoom.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key).First();
        var target = w.Ship.Rooms[rid];
        var steps = new List<RobotStep>();
        foreach (var c in cells.OrderBy(c => dist.Reachable(c) ? dist.Get(c) : int.MaxValue).Take(4))
        {
            var stand = w.Ship.IsWalkable(c) && dist.Reachable(c) ? c : Cell.Dirs4.Select(d => c + d).First(x => dist.Reachable(x));
            var spot = c;
            steps.Add(new RGoto(stand));
            steps.Add(new RWork(0.12f, null, spot.Center));
            steps.Add(new RDo((rb, world) =>
            {
                var b = world.Body;
                if (b.MarksAt(spot) is not CellState st) return true;
                if (st.V[(int)CellMark.Soot] > 0.4f && world.Fire.Scorch.TryGetValue(spot, out var sc)) world.Fire.Scorch[spot] = sc * 0.3f;
                if (st.V[(int)CellMark.Wet] > 0.2f) { foreach (var dd in Cell.Dirs4) if (b.Mark(spot + dd, CellMark.Wet) > 0.2f) b.SetMark(spot + dd, CellMark.Wet, 0f, ""); b.Stats.Mopped++; }
                b.SetMark(spot, CellMark.Wet, 0f, "");
                if (st.V[(int)CellMark.Soot] > 0.2f) b.SetMark(spot, CellMark.Glass, 0f, ""); // 사고 자리 유리만
                b.SetMark(spot, CellMark.Oil, 0f, "");
                b.SetMark(spot, CellMark.Soot, 0f, "");
                b.Stats.Cleaned++;
                V21.Wipes++;
                return true;
            }));
        }
        steps.Add(new RDo((rb, world) => { Done(rb, $"{target.Name} 바닥 얼룩을 닦았다"); return true; }));
        Begin(r, steps, $"{target.Name} 바닥 닦기 (얼룩 {cells.Count}칸)");
        return true;
    }
}

/// <summary>v21 원자로가 폭주한 로봇: 사람 없는 방에 서서 기다린다 (식혀 주거나 · 터지거나).</summary>
internal sealed class RHold : RobotStep
{
    public override ToilStatus Tick(Robot r, World w) => r.Runaway ? ToilStatus.Running : ToilStatus.Succeeded;
}

/// <summary>v21 폭주한 로봇에게 거품을 뿌려 식힌다 (거품이 떨어지거나 · 식거나 · 터지면 끝).</summary>
internal sealed class RCool : RobotStep
{
    private readonly Robot _hot;
    public RCool(Robot hot) => _hot = hot;
    public override ToilStatus Tick(Robot r, World w)
    {
        if (!_hot.Runaway || r.Foam <= 0.01f) return ToilStatus.Succeeded;
        if ((_hot.Position - r.Position).LengthSquared() > 2.6f * 2.6f) return ToilStatus.Failed; // 멀어졌다 — 다시 따라간다
        r.Facing = Vector2.Normalize(_hot.Position - r.Position + new Vector2(0.0001f, 0f));
        _hot.CoreHeat = MathF.Max(0f, _hot.CoreHeat - 15f / SimTime.TicksPerHour * (RobotsV15.Fights(r.Kind) ? 1f : 0.6f));
        r.Foam = MathF.Max(0f, r.Foam - 1f / (RobotSystem.FoamHoursOf(r) * SimTime.TicksPerHour));
        return ToilStatus.Running;
    }
}
