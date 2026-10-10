using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v19 2묶음: 굶주림 · 탈진 원인 기록.
//   굶주린 사람마다 5분마다 "왜"를 적는다 — 겹쳐도 된다 (한 사람이 배송 · 접근 둘 다일 수 있다):
//     생산: 배 전체에 먹을 것 자체가 모자란다
//     배송: 냉장고 · 창고에는 있는데 그 사람 구역의 배식기가 비었거나 멈췄다 (가까운 선반을 안 채운 것)
//     접근: 먹을 것은 있는데 길 · 줄 · 문 · 위험 · 먼 거리가 막는다 (먼 비상 선반까지 걷는 것)
//     행동: 먹으러 가지 않는다 — 일 · 회의 · 잠에 붙들려 끼니를 미루거나 먹다 말았다
//   지친 사람도 따로: 수면 기회 부족 · 침대 접근 · 잠자리 환경 · 수면 중단 · 작업 지속.
//   식사 · 잠이 끊긴 횟수와 끊은 것, 배식기 보충 요청 → 채워지기까지, 긴급 일이 사람을 못 구해 기다린 시간도.
// 기록만 한다 — 난수를 쓰지 않고 배의 상태를 바꾸지 않는다 (결정론에 닿지 않는다).
public sealed class LifeWatch
{
    public static readonly string[] HungerKinds = { "생산", "배송", "접근", "행동" };
    public static readonly string[] TiredKinds = { "수면 기회 부족", "침대 접근", "잠자리 환경", "수면 중단", "작업 지속" };

    /// <summary>굶주림 문턱 (포만감) · 탈진 문턱 (기력).</summary>
    public const float HungryBelow = 0.15f, TiredBelow = 0.15f;

    private readonly World _w;
    private long _next;
    public float HungryHours, TiredHours;
    public readonly float[] HungerHours = new float[4];
    public readonly float[] TiredKindHours = new float[5];
    /// <summary>원인 아래 갈래 (예: "행동 · 작업", "접근 · 배식 줄") → 사람·시간.</summary>
    public readonly Dictionary<string, float> HungerWhy = new(), TiredWhy = new();
    public int MealBreaks, SleepBreaks;
    /// <summary>줄 끝에 받으려는데 배식기가 비었다 (식당 이름별).</summary>
    public readonly Dictionary<string, int> EmptyAtTake = new(), EmptyAtTakeWhy = new();
    public readonly Dictionary<string, int> MealBreakWhy = new(), SleepBreakWhy = new();
    /// <summary>배식기 보충: 요청(6인분 아래 · 냉장고에 있음)부터 채워지기까지 (분).</summary>
    public readonly List<float> RestockMinutes = new();
    public float EmptyDispenserHours;
    /// <summary>긴급 일(급함 0.8↑)이 맡을 사람 · 로봇 없이 20분 넘게 기다린 건수 · 그때 식사 · 휴식 · 잠 중이던 사람 수의 합.</summary>
    public int UrgentSeen, UrgentLate, UrgentLateLifeCrew;
    public readonly List<float> UrgentWaitMinutes = new();
    public readonly Dictionary<string, int> UrgentLateKinds = new();

    /// <summary>v19 손일 (로봇 · 사람 둘 다 하는 일): 올라와서 끝나기까지 (분) — 마지막에 누가 쥐었나로 나눈다.</summary>
    public readonly List<float> HandByRobot = new(), HandByHuman = new();
    /// <summary>v19 로봇에게 미룬 채(쓸 로봇이 논다 — 사람은 의욕을 반으로) 아무도 안 잡은 시간 · 20분 넘게 그랬던 일감.</summary>
    public float DeferWaitHours;
    public int DeferLate;
    public readonly Dictionary<string, int> DeferLateKinds = new();
    /// <summary>v19 손일 종류별: (건수, 잡히기까지 분 합, 잡힌 뒤 끝나기까지 분 합, 급함 합).</summary>
    public readonly Dictionary<string, (int n, float wait, float work, float urg)> HandKinds = new();
    private readonly Dictionary<int, (long posted, bool robot, string kind, long claimed, float urg)> _hand = new();
    private readonly Dictionary<int, long> _deferSince = new();
    private readonly HashSet<int> _deferLate = new();

    private readonly Dictionary<int, long> _jobStart = new();
    private readonly Dictionary<int, (bool meal, long tick, string how)> _pendingBreak = new();
    private readonly Dictionary<int, long> _lastSleepBreak = new();
    private readonly Dictionary<int, long> _restockSince = new();
    private readonly HashSet<int> _urgentSeen = new(), _urgentLate = new();
    private readonly Dictionary<int, long> _urgentWaitStart = new();

    public LifeWatch(World w) => _w = w;

    // ── 일이 끝나고 시작될 때 (CrewMember.EndJob · StartJob) ──

    internal void OnJobEnded(CrewMember c, Job job, ToilStatus status)
    {
        if (status == ToilStatus.Succeeded) return;
        if (job.Activity is EatActivity && c.Needs.Food < 0.9f)
        {
            _pendingBreak[c.Id] = (true, _w.Tick, (status == ToilStatus.Failed ? "실패 " : "") + Where(job.Current ?? job.FailedAt, c));
            if (status == ToilStatus.Failed && job.FailedAt is TakeToil tt && job.TargetRoom is Room r)
            {
                EmptyAtTake[r.Name] = EmptyAtTake.GetValueOrDefault(r.Name) + 1;
                var f = tt.From;
                int n = f.Storage?.Count(ItemKind.Meal) ?? -1;
                var order = _w.Board.OpenUnsorted.FirstOrDefault(o => o.Kind == WorkKind.Restock && o.Target.Furniture == f);
                string why = c.Carrying is ItemStack held && held.Kind != ItemKind.Meal ? $"다른 것을 들고 있었다 ({held.Kind})"
                    : n > 0 ? $"남아 있었다 ({n})"
                    : order == null ? "비었는데 채우기 일이 없다" + (_w.Ship.FurnitureOf(FurnitureType.Fridge).Sum(x => x.Storage!.Count(ItemKind.Meal)) == 0 ? " (냉장고도 빔)" : "")
                    : order.Robot != null ? "비었다 · 로봇이 채우러 오는 중" : order.Assignee != null ? "비었다 · 사람이 채우러 오는 중" : "비었다 · 채우기 일을 아무도 안 맡음";
                if (f.Type != FurnitureType.MealDispenser) why = $"{f.Name} " + why;
                EmptyAtTakeWhy[why] = EmptyAtTakeWhy.GetValueOrDefault(why) + 1;
            }
        }
        else if (job.Activity is SleepActivity && c.Needs.Rest < 0.6f)
            _pendingBreak[c.Id] = (false, _w.Tick, c.Pose == Pose.Sleeping ? "자다가" : "자러 가다가");
    }

    private static string Where(Toil? t, CrewMember c) => t switch
    {
        QueueToil q => q.FailWhy != null ? $"줄에서 ({q.FailWhy})" : "줄에서",
        GotoToil => "가다가",
        TakeToil => "받으려다",
        WaitToil when c.Pose == Pose.Sitting => "먹다가",
        WaitToil => "서서 먹다가",
        _ => t?.GetType().Name ?? "도중에",
    };

    internal void OnJobStarted(CrewMember c, Job job)
    {
        _jobStart[c.Id] = _w.Tick;
        if (_pendingBreak.Remove(c.Id, out var p)) Break(c, p.meal, $"{p.how} → {job.Activity?.Label ?? job.Label}");
    }

    private void Break(CrewMember c, bool meal, string why)
    {
        if (meal) { MealBreaks++; MealBreakWhy[why] = MealBreakWhy.GetValueOrDefault(why) + 1; }
        else { SleepBreaks++; SleepBreakWhy[why] = SleepBreakWhy.GetValueOrDefault(why) + 1; _lastSleepBreak[c.Id] = _w.Tick; }
    }

    private float JobMinutes(CrewMember c) => _jobStart.TryGetValue(c.Id, out var t) ? (_w.Tick - t) / (float)SimTime.Minutes(1) : 0f;

    private static bool Eating(Job? j) => j?.Activity is EatActivity or SavedPlateActivity or SharedMealActivity;

    private static bool Leisure(Job? j) => j?.Activity is EatActivity or SleepActivity or RelaxActivity or ChatActivity or HobbyActivity or WanderActivity;

    // ── 5분마다 ──

    public void Update()
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(5);
        const float dt = 5f / 60f;
        // 이름이 붙지 않고 끝난 중단 (쓰러짐 · 실려 감)
        foreach (var (id, p) in _pendingBreak.Where(kv => w.Tick - kv.Value.tick > 0).ToList())
        {
            _pendingBreak.Remove(id);
            if (w.Crew.FirstOrDefault(x => x.Id == id) is CrewMember who) Break(who, p.meal, $"{p.how} → {(who.Down ? "쓰러짐" : "멈춤")}");
        }

        var alive = w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away && !c.Outside).ToList();
        if (alive.Count == 0) return;
        var ship = w.Ship;
        // 배 전체 먹을 것 (한 번만 센다)
        int meals = 0, rations = 0, produce = 0, fridgeMeals = 0;
        var dispensers = new List<(Furniture f, int n, bool ok)>();
        foreach (var f in ship.Containers)
        {
            if (f.Room.Detached) continue;
            int m = f.Storage!.Count(ItemKind.Meal);
            meals += m; rations += f.Storage.Count(ItemKind.Ration); produce += f.Storage.Count(ItemKind.Produce);
            if (f.Type == FurnitureType.Fridge) fridgeMeals += m;
            if (f.Type == FurnitureType.MealDispenser) dispensers.Add((f, m, f.Machine!.Efficiency > 0f && !f.Room.OffLimits));
        }
        int edible = meals + rations + produce / Math.Max(1, FoodChain.ProducePerBatch) * FoodChain.MealsPerBatch;
        bool scarce = edible < alive.Count;
        bool central = fridgeMeals > 0 || rations > 0;

        // 배식기 보충 (요청 → 채워짐)
        foreach (var (f, n, ok) in dispensers)
        {
            if (n == 0 && fridgeMeals > 0) EmptyDispenserHours += dt;
            bool asked = n < 6 && fridgeMeals > 0;
            if (asked && !_restockSince.ContainsKey(f.Id)) _restockSince[f.Id] = w.Tick;
            else if (n >= 6 && _restockSince.Remove(f.Id, out var since)) RestockMinutes.Add((w.Tick - since) / (float)SimTime.Minutes(1));
            else if (!asked && n < 6 && _restockSince.ContainsKey(f.Id) && fridgeMeals == 0) _restockSince.Remove(f.Id); // 냉장고도 비었다 (보충이 아니라 생산 문제)
        }

        foreach (var c in alive)
        {
            if (c.Needs.Food < HungryBelow) Hunger(c, dt, scarce, central, dispensers, edible);
            if (c.Needs.Rest < TiredBelow) Tired(c, dt);
        }

        // v19 손일: 누가 끝냈고 얼마나 걸렸나 · 로봇에게 미룬 채 비어 있던 시간
        var openIds = new HashSet<int>();
        foreach (var o in w.Board.OpenUnsorted)
        {
            if (!RobotSystem.HandWork(o.Kind)) continue;
            openIds.Add(o.Id);
            bool had = _hand.TryGetValue(o.Id, out var h);
            bool robot = had && h.robot;
            if (o.Robot != null) robot = true; else if (o.Assignee != null) robot = false;
            long claimed = had ? h.claimed : -1;
            if (claimed < 0 && (o.Robot != null || o.Assignee != null)) claimed = w.Tick;
            _hand[o.Id] = (o.Posted, robot, WorkKinds.Name(o.Kind), claimed, MathF.Max(had ? h.urg : 0f, o.Urgency));
            bool idle = o.Assignee == null && o.Robot == null && o.BlockedUntil <= w.Tick;
            if (idle && o.Urgency < 1f && w.Robots.HandsFree(o))
            {
                DeferWaitHours += dt;
                if (!_deferSince.ContainsKey(o.Id)) _deferSince[o.Id] = w.Tick;
                if (w.Tick - _deferSince[o.Id] > SimTime.Minutes(20) && _deferLate.Add(o.Id)) { DeferLate++; Note(DeferLateKinds, WorkKinds.Name(o.Kind)); }
            }
            else _deferSince.Remove(o.Id);
        }
        foreach (var id in _hand.Keys.Where(id => !openIds.Contains(id)).ToList())
        {
            var (posted, robot, kind, claimed, urg) = _hand[id];
            _hand.Remove(id); _deferSince.Remove(id);
            (robot ? HandByRobot : HandByHuman).Add((w.Tick - posted) / (float)SimTime.Minutes(1));
            float wait = ((claimed >= 0 ? claimed : w.Tick) - posted) / (float)SimTime.Minutes(1), work = claimed >= 0 ? (w.Tick - claimed) / (float)SimTime.Minutes(1) : 0f;
            var k = HandKinds.GetValueOrDefault(kind);
            HandKinds[kind] = (k.n + 1, k.wait + wait, k.work + work, k.urg + urg);
        }

        // 긴급 일: 맡을 사람 · 로봇 없이 기다린 시간
        int life = -1;
        foreach (var o in w.Board.OpenUnsorted)
        {
            if (o.Urgency < 0.8f || o.Kind == WorkKind.Upgrade) continue; // 평시 개조는 점수가 높아도 급한 일이 아니다
            if (_urgentSeen.Add(o.Id)) UrgentSeen++;
            bool waiting = o.Assignee == null && o.Robot == null && o.BlockedUntil <= w.Tick;
            if (!waiting) { if (_urgentWaitStart.Remove(o.Id, out var st)) UrgentWaitMinutes.Add((w.Tick - st) / (float)SimTime.Minutes(1)); continue; }
            if (!_urgentWaitStart.ContainsKey(o.Id)) _urgentWaitStart[o.Id] = Math.Max(o.Posted, w.Tick - SimTime.Minutes(5));
            if (w.Tick - _urgentWaitStart[o.Id] > SimTime.Minutes(20) && _urgentLate.Add(o.Id))
            {
                UrgentLate++;
                Note(UrgentLateKinds, WorkKinds.Name(o.Kind));
                if (life < 0) life = alive.Count(c => !c.Down && Leisure(c.Job));
                UrgentLateLifeCrew += life;
            }
        }
    }

    private void Note(Dictionary<string, float> d, string k, float dt) => d[k] = d.GetValueOrDefault(k) + dt;
    private static void Note(Dictionary<string, int> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;

    private void Hunger(CrewMember c, float dt, bool scarce, bool central, List<(Furniture f, int n, bool ok)> dispensers, int edible)
    {
        HungryHours += dt;
        bool[] on = new bool[4];
        void Mark(int k, string why) { on[k] = true; Note(HungerWhy, $"{HungerKinds[k]} · {why}", dt); }
        if (scarce) Mark(0, edible == 0 ? "먹을 것이 없다" : "한 끼씩도 모자란다");
        // 그 사람 구역의 배식기 (가까운 것)
        var near = dispensers.Count == 0 ? default : dispensers.OrderBy(d => (d.f.Center - c.Position).LengthSquared()).First();
        if (near.f != null && central && (near.n == 0 || !near.ok)) Mark(1, !near.ok ? "가까운 배식기가 멈췄다" : "가까운 배식기가 비었다");
        var job = c.Job;
        if (Eating(job))
        {
            float mins = JobMinutes(c);
            if (job!.Current is QueueToil) Mark(2, "배식 줄");
            else if (job.Current is GotoToil && mins > 20f) Mark(2, job.Label is "비상식량" or "날채소" ? "먼 비상 선반까지 걷는다" : "먼 배식기까지 걷는다");
        }
        else if (c.Down || c.CareBed != null) Mark(2, "쓰러져 못 간다");
        else
        {
            var eat = c.LastEvaluations?.FirstOrDefault(e => e.Activity is EatActivity);
            if (eat is { Score: <= 0f } && edible > 0 && !scarce) Mark(2, "닿을 수 있는 먹을 것이 없다 (길 · 문)");
            else if (eat?.Reason.Contains("태양 폭풍") == true) Mark(2, "위험 — 지나가길 기다린다");
            else Mark(3, job?.Activity?.Label ?? "아무것도 안 함");
        }
        for (int k = 0; k < 4; k++) if (on[k]) HungerHours[k] += dt;
    }

    private void Tired(CrewMember c, float dt)
    {
        var w = _w;
        TiredHours += dt;
        bool[] on = new bool[5];
        void Mark(int k, string why) { on[k] = true; Note(TiredWhy, $"{TiredKinds[k]} · {why}", dt); }
        bool asleep = c.Pose is Pose.Sleeping;
        if (asleep)
        {
            var under = w.Ship.FurnitureAt(c.Cell);
            if (!(under != null && FurnitureTypes.Sleepable(under.Type))) Mark(1, "침대가 아닌 데서 잔다");
            var air = c.Room?.Air;
            if (AmbienceSystem.SleepFactor(c.Room) < 0.9f) Mark(2, "시끄럽다 · 흔들린다");
            if (air != null && (air.CO2 > 1f || air.Temperature < 12f || air.Temperature > 32f)) Mark(2, "공기 · 온도");
            if (c.Memory.FearOf(c.Room) > 0.1f) Mark(2, "무서운 방");
        }
        else
        {
            var job = c.Job;
            if (job?.Activity is SleepActivity && job.Current is GotoToil && JobMinutes(c) > 20f) Mark(1, "침대가 멀다");
            if (_lastSleepBreak.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.Hours(6)) Mark(3, "최근에 잠이 끊겼다");
            bool working = c.Pose == Pose.Working || job?.Order != null || job?.Urgent == true;
            if (working) Mark(4, job?.Urgent == true || Crisis.Acting(w) ? "비상 일" : "일을 놓지 않는다");
            else if (job?.Activity is not SleepActivity)
            {
                bool bedtime = SimTime.InWindow(SimTime.HourOfDay(w.Tick), c.Schedule.SleepStart, c.Schedule.SleepLength);
                Mark(0, bedtime ? $"잘 시간인데 {job?.Activity?.Label ?? "서성임"}" : "잘 시간이 아니다");
            }
        }
        for (int k = 0; k < 5; k++) if (on[k]) TiredKindHours[k] += dt;
    }

    public string Report()
    {
        static string Top(Dictionary<string, float> d, int n) => string.Join(" · ", d.OrderByDescending(kv => kv.Value).Take(n).Select(kv => $"{kv.Key} {kv.Value:0.#}"));
        static string TopI(Dictionary<string, int> d, int n) => string.Join(" · ", d.OrderByDescending(kv => kv.Value).Take(n).Select(kv => $"{kv.Key} {kv.Value}"));
        var rs = RestockMinutes;
        return $"굶주림 {HungryHours:0.#}사람·시간 (" + string.Join(" · ", HungerKinds.Select((k, i) => $"{k} {HungerHours[i]:0.#}")) + ")\n"
             + $"   갈래: {Top(HungerWhy, 8)}\n"
             + $"탈진 {TiredHours:0.#}사람·시간 (" + string.Join(" · ", TiredKinds.Select((k, i) => $"{k} {TiredKindHours[i]:0.#}")) + ")\n"
             + $"   갈래: {Top(TiredWhy, 8)}\n"
             + $"식사 중단 {MealBreaks} ({TopI(MealBreakWhy, 8)})\n수면 중단 {SleepBreaks} ({TopI(SleepBreakWhy, 8)})\n"
             + (EmptyAtTake.Count > 0 ? $"받으려니 빈 배식기: {TopI(EmptyAtTake, 6)} · 왜: {TopI(EmptyAtTakeWhy, 6)}\n" : "")
             + $"배식기 보충 {rs.Count}번 · 평균 {(rs.Count > 0 ? rs.Average() : 0):0}분 · 최대 {(rs.Count > 0 ? rs.Max() : 0):0}분 · 냉장고엔 있는데 빈 배식기 {EmptyDispenserHours:0.#}대·시간\n"
             + $"손일 로봇 {HandByRobot.Count}건 · 평균 {(HandByRobot.Count > 0 ? HandByRobot.Average() : 0):0}분 ↔ 사람 {HandByHuman.Count}건 · 평균 {(HandByHuman.Count > 0 ? HandByHuman.Average() : 0):0}분 · 로봇에게 미룬 채 빈 일감 {DeferWaitHours:0.#}일감·시간 (20분 넘게 {DeferLate}건{(DeferLate > 0 ? ": " + TopI(DeferLateKinds, 5) : "")})\n"
             + $"   손일 갈래 (건수 · 잡히기까지 · 잡힌 뒤 · 급함): {string.Join(" · ", HandKinds.OrderByDescending(kv => kv.Value.wait + kv.Value.work).Take(6).Select(kv => $"{kv.Key} {kv.Value.n}건 {kv.Value.wait / kv.Value.n:0}+{kv.Value.work / kv.Value.n:0}분 ({kv.Value.urg / kv.Value.n:0.00})"))}\n"
             + $"긴급 일 {UrgentSeen}건 · 20분 넘게 맡을 이 없이 기다림 {UrgentLate}건 (그때 식사 · 휴식 · 잠 중 평균 {(UrgentLate > 0 ? UrgentLateLifeCrew / (float)UrgentLate : 0):0.#}명) · 기다림 평균 {(UrgentWaitMinutes.Count > 0 ? UrgentWaitMinutes.Average() : 0):0}분{(UrgentLateKinds.Count > 0 ? " (" + string.Join(" · ", UrgentLateKinds.Select(k => $"{k.Key} {k.Value}")) + ")" : "")}";
    }
}
