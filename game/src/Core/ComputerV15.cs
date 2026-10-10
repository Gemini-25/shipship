using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15.9 컴퓨터 모듈 7 → 20: 주 컴퓨터에 올리는 기능 모듈 13.
// 겪은 일이 있어야 올린다(그 일이 없으면 연구가 쌓였을 때) — 하루에 하나씩, 출항 이튿날부터, 주 컴퓨터가 돌 때만.
// 효과는 기존 자리에 작은 배율이나 자동 조치로 들어간다 (주 컴퓨터가 멈추면 같이 멈춘다 · 방마다 쓰는 것은 데이터선이 이어져야):
//   전조 감지 · 마모 · 물 소비 · 빈 방 전력 · 냉장 부패 · 운석 예보 · 졸음 실수 · 감지기 틀어짐 · 끊긴 방 경보 · 방 오염 · 역류 · 문 압 · 관행 기록.

public static class ComputerV15
{
    public sealed record Row(ComputerModule Module, string Name, string Note, Func<World, string?> Why, float Research);

    private static int Faults(World w) => w.Ship.Machines.Sum(m => m.FaultCount);
    private static string? N(bool ok, string why) => ok ? why : null;

    public static readonly Row[] Rows =
    {
        new(ComputerModule.Foresight, "전조 분석", "감지기가 전조를 30% 더 잘 잡고, 잡은 전조는 부하를 낮춰 고장까지 여유를 4분의 1 더 번다",
            w => N(w.Precursors.Missed >= 1, $"전조를 놓쳐 고장 {w.Precursors.Missed}번") ?? N(w.Eras.Has("checklist") && w.Precursors.Detected >= 3, "점검표 문화 · 전조를 여러 번 잡았다"), 30f),
        new(ComputerModule.MaintPlan, "정비 일정", "설비를 돌려 가며 쉬게 하고 정비 순서를 짠다 — 데이터선이 닿는 설비 마모 −15%",
            w => N(Faults(w) >= 6, $"고장 {Faults(w)}번"), 30f),
        new(ComputerModule.WaterPlan, "물 관리", "샤워·세탁·관개 시간을 맞춰 물 소비 −12%",
            w => N(w.Water.Level < w.Water.Capacity * 0.4f, $"물탱크가 {w.Water.Level / MathF.Max(1f, w.Water.Capacity) * 100:0}%까지 내려갔다") ?? N(w.Policies["water"] >= 1, "물을 아끼는 방침"), 25f),
        new(ComputerModule.PowerShare, "전력 분배", "빈 방 조명·환기를 낮추고(−40%) 쉬는 설비의 대기 전력을 반으로",
            w => N(w.Flow.Stats.Brownouts >= 1, $"전압 강하 {w.Flow.Stats.Brownouts}번") ?? N(w.Power.ShedCount > 0, "부하를 끊어 냈다") ?? N(w.Eras.Has("smartgrid"), "지능형 배전"), 30f),
        new(ComputerModule.CargoSort, "화물 정리", "냉장고가 멈추면 상할 것부터 찬 칸으로 옮겨 쌓게 한다 — 멈춘 냉장고 속 부패 −60%",
            w => N(w.Hazards.FoodDiscarded >= 1, $"버린 음식 {w.Hazards.FoodDiscarded}") ?? N(w.Movement.Stashes.Count >= 4, $"내려놓고 간 짐 {w.Movement.Stashes.Count}"), 35f),
        new(ComputerModule.RouteForecast, "항로 위험 예보", "궤적 계산을 앞당겨 운석을 30% 더 일찍 알린다",
            w => N(w.History.Meteors >= 1, $"운석 {w.History.Meteors}번") ?? N(w.Sensors.Unwarned >= 1, "경보 없이 맞았다"), 30f),
        new(ComputerModule.FatigueAlert, "피로 경보", "기력이 바닥난 사람이 일을 잡으면 경보로 한 번 더 확인시킨다 — 졸려서 하는 실수 4배 → 2.4배",
            w => N(w.Life.Stats.Mistakes >= 2, $"실수 {w.Life.Stats.Mistakes}번"), 30f),
        new(ComputerModule.AutoCalib, "감지기 자동 교정", "데이터선이 닿는 감지기의 틀어짐을 스스로 맞춘다 — 틀어지는 속도 −60%",
            w => N(w.Watch.Stats.Phantoms >= 1, $"계기 오류 {w.Watch.Stats.Phantoms}번"), 35f),
        new(ComputerModule.CommsRelay, "통신 중계", "데이터선이 끊긴 방도 통신실 무선으로 경보·계기 값을 잇는다",
            w => N(w.Ship.Rooms.Any(r => !r.DataLinked && !r.Detached), "데이터선이 끊긴 방이 있었다"), 40f),
        new(ComputerModule.SoilWatch, "오염 감시", "조리실·의무실·재배실·식당 오염을 재서 환기·세정을 먼저 돌린다 — 그 방이 2.5배 빨리 깨끗해진다",
            w => N(w.Soil.Stats.TaintedMeals + w.Soil.Stats.WoundInfections + w.Soil.Stats.ContactCatches >= 1, "더러운 손으로 탈이 났다"), 35f),
        new(ComputerModule.PipeWatch, "배관 압력 감시", "수압이 떨어진 방은 체크 밸브를 닫아 역류 −65% · 물이 더러워지면 그 구간부터 돌려 씻는다(1.5배)",
            w => N(w.Flow.Stats.Backflows >= 1, $"역류 {w.Flow.Stats.Backflows}번") ?? N(w.Flow.Stats.LowPressure >= 2, $"수압 낮음 {w.Flow.Stats.LowPressure}번"), 40f),
        new(ComputerModule.DoorPressure, "문 압력 경보", "새지 않는 두 방 사이 문에 압이 걸리면 알리고 균압 밸브를 미리 연다 (사람이 문 앞에서 기다리지 않게)",
            w => N(w.Flow.Stats.Equalized >= 3, $"문에 압이 걸린 일 {w.Flow.Stats.Equalized}번") ?? N(w.History.Breaches >= 1, $"선체 구멍 {w.History.Breaches}번"), 40f),
        new(ComputerModule.Archive, "기록 보관", "관행이 생긴 까닭을 예비 기억 장치에 적어 둔다 — 컴퓨터가 멈춰도 남고, 신입이 스스로 찾아 읽는다",
            w => N(w.Culture.Stats.Forgotten >= 1, "이유가 잊힌 관행이 생겼다") ?? N(w.Culture.Customs.Count >= 2, $"관행 {w.Culture.Customs.Count}개"), 45f),
    };

    private static readonly Dictionary<ComputerModule, Row> ByModule = Rows.ToDictionary(r => r.Module);
    public static Row? Of(ComputerModule m) => ByModule.TryGetValue(m, out var r) ? r : null;
    public static string Name(ComputerModule m) => Of(m)?.Name ?? m.ToString();
    public static string Note(ComputerModule m) => Of(m)?.Note ?? "";

    /// <summary>지금 올릴 까닭 (겪은 일 · 아니면 연구) — 없으면 null.</summary>
    public static string? Why(World w, Row r) => r.Why(w) ?? (w.Research >= r.Research ? $"연구 {w.Research:0}점" : null);

    // ── 효과 ──

    /// <summary>모듈이 올라가 있고 주 컴퓨터가 돈다.</summary>
    public static bool On(World w, ComputerModule m) => w.Automation.Active(m) && w.Automation.MainOnline; // v16.6 연산이 모자라 잠시 끈 모듈은 쉰다
    private static bool On(World w, ComputerModule m, Room r) => r.DataLinked && On(w, m);

    /// <summary>전조 분석: 감지기가 진짜 전조를 잡는 배율 (계기 오류는 그대로).</summary>
    public static float OmenSensorMul(World w, bool phantom) => !phantom && On(w, ComputerModule.Foresight) ? 1.3f : 1f;

    /// <summary>전조 분석: 잡은 전조 — 부하를 낮춰 고장까지 남은 시간을 4분의 1 늘린다.</summary>
    public static void OnDetect(World w, Machine m, Omen o)
    {
        if (o.Cause == OmenCause.Phantom || !On(w, ComputerModule.Foresight, m.Body.Room) || o.Due <= w.Tick) return;
        long gain = (long)((o.Due - w.Tick) * 0.25f);
        o.Due += gain;
        w.Automation.V15Acts[ComputerModule.Foresight]++;
        w.Automation.Book.Today.Deferred++; w.Automation.Book.Today.DeferredHours += gain / (float)SimTime.TicksPerHour; // v16.0 하루 보고
        w.Automation.Book.Add(ActKind.Module, m.Body.Room, $"{m.Name} 전조 ({Prevention.Name(o.Kind)})", "고장 전에 부하를 낮추면 버틴다", $"부하를 낮춰 고장을 {gain / (float)SimTime.TicksPerHour:0}시간 늦춤", "정비", "", 0, 120f,
            (world, a) => m.Faults.Count == 0 ? (1, "맞았다 — 아직 멀쩡하다") : (2, "보류 — 결국 고장 났다"));
        MarkLog.Add(m.Marks, w.Tick, "전조 분석 — 부하를 낮춰 고장을 늦췄다");
    }

    /// <summary>정비 일정: 마모 배율.</summary>
    public static float WearMul(World w, Machine m) => On(w, ComputerModule.MaintPlan, m.Body.Room) ? 0.85f : 1f;

    /// <summary>물 관리: 물 소비 배율.</summary>
    public static float WaterUseMul(World w) => On(w, ComputerModule.WaterPlan) ? 0.88f : 1f;

    /// <summary>전력 분배: 아무도 없는 방의 조명·환기.</summary>
    public static float RoomKwMul(World w, Room r) => On(w, ComputerModule.PowerShare, r) && !w.Crew.Any(c => !c.Dead && c.Room == r) ? 0.6f : 1f;

    /// <summary>전력 분배: 쉬는 설비의 대기 전력.</summary>
    public static float IdleKwMul(World w, Machine m) => !m.Active && On(w, ComputerModule.PowerShare, m.Body.Room) ? 0.5f : 1f;

    /// <summary>화물 정리: 멈춘 냉장고 속 음식이 상하는 배율.</summary>
    public static float SpoilMul(World w) => On(w, ComputerModule.CargoSort) ? 0.4f : 1f;

    /// <summary>항로 위험 예보: 센서 궤적 경보가 앞서는 배율.</summary>
    public static float LeadMul(World w) => On(w, ComputerModule.RouteForecast) ? 1.3f : 1f;

    /// <summary>피로 경보: 졸려서 하는 실수 배율 (원래 4배).</summary>
    public static float TiredMistake(World w) => On(w, ComputerModule.FatigueAlert) ? 2.4f : 4f;

    /// <summary>감지기 자동 교정: 감지기가 틀어지는 속도 배율.</summary>
    public static float DriftMul(World w, Room r) => On(w, ComputerModule.AutoCalib, r) ? 0.4f : 1f;

    /// <summary>통신 중계: 데이터선이 끊긴 방도 통신실 무선으로 잇는다 (통신실에 전기가 있어야).</summary>
    public static bool Relay(World w) => On(w, ComputerModule.CommsRelay) && w.Sensors.CommsRoom is Room cr && cr.Powered && !cr.Detached;

    /// <summary>오염 감시: 깨끗해야 할 방이 옅어지는 배율.</summary>
    public static float SoilMul(World w, Room r) => SoilSystem.CleanRoom(r) && On(w, ComputerModule.SoilWatch, r) ? 2.5f : 1f;

    /// <summary>배관 압력 감시: 수압이 떨어진 방의 역류 배율.</summary>
    public static float BackflowMul(World w, Room r) => On(w, ComputerModule.PipeWatch, r) ? 0.35f : 1f;

    /// <summary>배관 압력 감시: 더러워진 물이 되돌아오는 배율.</summary>
    public static float FlushMul(World w) => On(w, ComputerModule.PipeWatch) ? 1.5f : 1f;

    /// <summary>기록 보관: 관행의 까닭이 예비 기억 장치에 남는다 (주 컴퓨터가 멈춰도).</summary>
    public static bool Archived(World w) => w.Automation.Has(ComputerModule.Archive);

    /// <summary>문 압력 경보: 컴퓨터가 균압 밸브를 대신 열어도 되는 쪽 (새지 않고 · 불·연기 없고 · 대응 중이 아니고 · 에어락이 아닌 방).</summary>
    public static bool CalmSide(World w, Room r) =>
        !r.Detached && r.DataLinked && !r.Leaking && !r.Abandoned && !r.OffLimits && !r.ResponseHold && r.Type != RoomType.Airlock
        && r.Air.Pressure >= 20f && r.Air.Smoke < 0.1f && r.Air.Toxin < 0.05f && r.Air.CO < 0.02f && w.Fire.CountIn(r) == 0 && w.Automation.InZone(r);
}

public sealed partial class AutomationSystem
{
    /// <summary>v15.9 모듈마다 스스로 한 일 (전조를 늦춤 · 문 압을 맞춤 …).</summary>
    public Dictionary<ComputerModule, int> V15Acts { get; } = Enum.GetValues<ComputerModule>().ToDictionary(m => m, _ => 0);

    private long _v15Last = -SimTime.TicksPerDay * 2;
    /// <summary>시험용: 새 모듈을 스스로 올리지 않는다 (모듈 없는 배를 볼 때).</summary>
    public bool V15NoAuto { get; set; }
    private readonly HashSet<int> _doorEq = new();

    /// <summary>주 컴퓨터가 돌 때 (Respond 끝에서): 문 압을 맞추고, 한 시간마다 새 모듈을 올릴지 본다.</summary>
    internal void RespondV15(float dt)
    {
        var w = _world;
        if (Has(ComputerModule.DoorPressure)) DoorPressure(dt);
        // v16.20 모듈은 첫날부터 다 있다 — 겪은 일로 등급을 올리는 건 GrowModules (ComputerCore.cs)
    }

    /// <summary>문 압력 경보: 새지 않는 두 방 사이 문에 압이 걸렸으면 알리고 균압 밸브를 미리 연다.</summary>
    private void DoorPressure(float dt)
    {
        var w = _world;
        foreach (var d in w.Ship.Doors)
        {
            if (d.IsExternal || d.RoomA is not Room a || d.RoomB is not Room b || a == b) continue;
            bool pressed = FlowSystem.DoorDelta(a, b) >= FlowSystem.EqualizeAbove;
            if (!pressed || !ComputerV15.CalmSide(w, a) || !ComputerV15.CalmSide(w, b)) { if (!pressed && _doorEq.Remove(d.Id)) { V15Acts[ComputerModule.DoorPressure]++; Book.Today.DoorEq++; } continue; }
            if (_doorEq.Add(d.Id))
            {
                w.Log.Add(w.Tick, LogKind.Ship, $"문 압력 경보 — {a.Name}↔{b.Name} {FlowSystem.DoorDelta(a, b):0}kPa · 균압 밸브를 미리 연다");
                Book.Add(ActKind.Valve, a, $"{a.Name}↔{b.Name} 문에 {FlowSystem.DoorDelta(a, b):0}kPa", "새지 않는 두 방 — 맞춰도 된다", "균압 밸브를 연다", "", "deq:" + d.Id, SimTime.Minutes(30), 5f);
            }
            w.Flow.Equalize(a, b, MathF.Min(0.5f, 4f * dt));
            if (FlowSystem.DoorDelta(a, b) < FlowSystem.EqualizeAbove && _doorEq.Remove(d.Id)) { V15Acts[ComputerModule.DoorPressure]++; Book.Today.DoorEq++; }
        }
    }
}
