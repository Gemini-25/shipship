using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v11.2 외부 교신 (통신실의 또 하나의 존재 이유).
///   조난 신호: 공기·먹을 것·물·실링폼이 바닥나 혼자 버티기 어려우면 회의로 통신실에서 신호를 보낸다 (엿새에 한 번).
///             하루~이틀 뒤 보급 캡슐이 에어락 바깥 해치에 붙고, 사람이 짐을 내려야 창고에 들어간다 (공기 탱크·물은 관으로 바로).
///   구조 요청 수신: 근처의 탈출 캡슐이 신호를 보낸다 (관찰자 사고 · 무작위). 하루 안에 회의로 건질지 정한다 —
///             추진제를 써서 다가가야 하고, 먹을 입이 는다. 건지면 다친 생존자 1~3명이 에어락으로 올라와 승무원이 된다.
/// 통신실이 멀쩡하고(전기·콘솔) 누가 앉아야 보낼 수 있다 — 통신실을 잃은 배는 도움을 청할 수도 들을 수도 없다.
/// </summary>
public sealed class CommsSystem
{
    private readonly World _w;
    public CommsSystem(World w) => _w = w;

    // 조난 신호 · 보급
    public long DistressAt { get; internal set; } = -1;
    public long SupplyEta { get; internal set; } = -1;
    public bool SupplyDocked { get; internal set; }
    public long SupplyDockedAt { get; internal set; } = -1;
    public List<(ItemKind kind, int count)> Cargo { get; } = new();
    public float CargoAir { get; internal set; }
    public float CargoWater { get; internal set; }
    public int Distresses { get; internal set; }
    public int Supplies { get; internal set; }

    // 구조 요청
    public long SignalAt { get; internal set; } = -1;
    public long SignalUntil { get; internal set; } = -1;
    public int SignalSurvivors { get; internal set; }
    public long PodEta { get; internal set; } = -1;
    public int Rescued { get; internal set; }
    public int SignalsMissed { get; internal set; }

    public bool SignalOpen => SignalAt >= 0 && _w.Tick < SignalUntil && PodEta < 0;

    /// <summary>통신 콘솔 (통신실의 콘솔 — 돌아가야 보내고 듣는다).</summary>
    public Furniture? Console => _w.Sensors.CommsRoom is Room r && !r.Detached && !r.Abandoned && !r.OffLimits
        ? r.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Console && f.Machine is Machine m && m.Efficiency > 0f && f.UseSpots.Count > 0)
        : null;

    public Room? Airlock => _w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault(r => !r.Detached && !r.Abandoned);

    /// <summary>지금 배가 혼자 버티기 어려운 까닭 (없으면 null).</summary>
    public string? Crisis()
    {
        var w = _w;
        if (w.Air.Reserve < w.Air.ReserveCapacity * 0.2f) return $"공기 탱크 {w.Air.Reserve / w.Air.ReserveCapacity * 100:0}%";
        float days = FoodPolicy.FoodDays(w);
        int crew = w.Crew.Count(c => !c.Dead);
        if (days < 1.2f && FoodPolicy.GrowingPerDay(w) < crew * FoodPolicy.MealsPerPersonDay * 0.8f) return $"먹을 것 {days:0.0}일치";
        if (w.Water.Level < w.Water.Capacity * 0.1f) return $"물 {w.Water.Level:0}L";
        if (w.Board.Have(ItemKind.Sealant) == 0 && w.Ship.Rooms.Any(r => r.Leaking && !r.Abandoned && !r.Detached)) return "새는 방을 막을 실링폼이 없다";
        return null;
    }

    /// <summary>보급 캡슐에 실어 보내는 것 (모자란 것부터 — 사람 수에 맞춰).</summary>
    private void Pack()
    {
        var w = _w;
        int crew = Math.Max(1, w.Crew.Count(c => !c.Dead));
        float scale = MathF.Max(1f, crew / 6f);
        Cargo.Clear();
        Cargo.Add((ItemKind.Meal, (int)(crew * 6)));
        Cargo.Add((ItemKind.Sealant, (int)(6 * scale)));
        Cargo.Add((ItemKind.Plate, (int)(6 * scale)));
        Cargo.Add((ItemKind.Filter, (int)(3 * scale)));
        Cargo.Add((ItemKind.MedKit, (int)(2 * scale)));
        Cargo.Add((ItemKind.Cable, (int)(3 * scale)));
        CargoAir = w.Air.ReserveCapacity * 0.4f;
        CargoWater = w.Water.Capacity * 0.3f;
    }

    internal void SendDistress(CrewMember by, string why)
    {
        var w = _w;
        DistressAt = w.Tick;
        Distresses++;
        SupplyEta = w.Tick + SimTime.Hours(w.Rng.Range(20f, 40f));
        Pack();
        w.History.Add(w, HistoryKind.Decision, $"{Ko.IGa(by.Name)} 조난 신호를 보냈다 — {why} · 보급선이 {(SupplyEta - w.Tick) / (float)SimTime.TicksPerHour:0}시간 뒤 캡슐을 보낸다고 답했다",
            w.Sensors.CommsRoom, new[] { by }, log: true);
        w.RaiseAlert($"조난 신호 — 보급 캡슐 {(SupplyEta - w.Tick) / (float)SimTime.TicksPerHour:0}시간 뒤", w.Sensors.CommsRoom, AlertLevel.Notice, shipWide: true);
    }

    internal void AnswerSignal(CrewMember by)
    {
        var w = _w;
        var p = w.Propulsion;
        p.Propellant = MathF.Max(0f, p.Propellant - p.EvadeCost * 2f);
        PodEta = w.Tick + SimTime.Hours(w.Rng.Range(4f, 8f));
        w.History.Add(w, HistoryKind.Decision, $"{Ko.IGa(by.Name)} 탈출 캡슐 쪽으로 배를 돌렸다 — 생존자 {SignalSurvivors}명 · {(PodEta - w.Tick) / (float)SimTime.TicksPerHour:0}시간 뒤 에어락에 붙는다 (추진제 {p.EvadeCost * 2f:0}kg)",
            null, new[] { by }, log: true);
    }

    /// <summary>구조 요청을 받는다 (관찰자 사고 · 무작위). 통신실이 멀쩡해야 듣는다.</summary>
    internal string? ReceiveSignal()
    {
        var w = _w;
        if (SignalOpen || PodEta >= 0 || Console == null) return null;
        SignalAt = w.Tick;
        SignalUntil = w.Tick + SimTime.Hours(24);
        SignalSurvivors = 1 + w.Rng.Range(0, 3);
        w.RaiseAlert($"구조 요청 수신 — 탈출 캡슐 · 생존자 {SignalSurvivors}명 (하루 안에 정해야 한다)", w.Sensors.CommsRoom, AlertLevel.Warning, shipWide: true);
        w.History.Add(w, HistoryKind.Incident, $"통신실이 탈출 캡슐의 구조 요청을 받았다 — 생존자 {SignalSurvivors}명", w.Sensors.CommsRoom);
        w.Board.RequestScan();
        return "구조 요청";
    }

    public void SystemUpdate(float dt)
    {
        var w = _w;
        // 보급 캡슐이 도착했다 — 에어락 바깥 해치에 붙는다
        if (SupplyEta >= 0 && w.Tick >= SupplyEta && !SupplyDocked)
        {
            SupplyEta = -1;
            if (Airlock == null)
            {
                w.Log.Add(w.Tick, LogKind.Warning, "보급 캡슐이 왔지만 붙을 에어락이 없어 돌아갔다");
                Cargo.Clear();
            }
            else
            {
                SupplyDocked = true;
                SupplyDockedAt = w.Tick;
                w.RaiseAlert("보급 캡슐이 에어락에 붙었다 — 짐을 내려야 한다", Airlock, AlertLevel.Notice, shipWide: true);
                w.History.Add(w, HistoryKind.Response, $"보급 캡슐이 에어락에 붙었다 — {string.Join(" · ", Cargo.Select(c => $"{ItemKinds.Name(c.kind)} {c.count}"))} · 공기 · 물", Airlock);
                w.Board.RequestScan();
            }
        }
        // 구조 요청이 식었다 (정하지 못했거나 부결)
        if (SignalAt >= 0 && PodEta < 0 && w.Tick >= SignalUntil)
        {
            SignalsMissed++;
            SignalAt = -1;
            w.History.Add(w, HistoryKind.Memory, "탈출 캡슐의 신호가 끊겼다 — 건지지 못했다", w.Sensors.CommsRoom);
            foreach (var c in w.Crew.Where(c => !c.Dead))
            {
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f * (1.2f - c.Traits.Calm));
                MarkLog.Add(c.Memory.Marks, w.Tick, "탈출 캡슐의 신호가 끊겼다");
            }
        }
        // 탈출 캡슐이 붙었다 — 생존자가 에어락으로 올라온다
        if (PodEta >= 0 && w.Tick >= PodEta)
        {
            PodEta = -1;
            SignalAt = -1;
            var air = Airlock;
            if (air == null) { w.Log.Add(w.Tick, LogKind.Warning, "탈출 캡슐이 왔지만 붙을 에어락이 없다"); return; }
            var spots = air.Cells.Where(w.Ship.IsOpenFloor).ToList();
            var names = new List<string>();
            var joined = new List<CrewMember>();
            for (int i = 0; i < SignalSurvivors && spots.Count > 0; i++)
            {
                var c = w.AddSurvivor(spots[i % spots.Count]);
                names.Add(c.Name);
                joined.Add(c);
                Rescued++;
            }
            w.Board.RequestScan();
            w.RaiseAlert($"탈출 캡슐 도킹 — {string.Join("·", names)} 구조 (다쳤다 · 의무실로)", air, AlertLevel.Notice, shipWide: true);
            w.History.Add(w, HistoryKind.Milestone, $"탈출 캡슐에서 {Ko.EulReul(string.Join("·", names))} 건졌다 — 이제 승무원 {w.Crew.Count(c => !c.Dead)}명", air, joined, log: true);
        }
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v11.2 교신: 조난 신호(회의), 보급 캡슐 내리기, 구조 요청에 답하기(회의).</summary>
    private void ScanComms(Poster post)
    {
        var w = _world;
        var cm = w.Comms;
        var console = cm.Console;
        // 조난 신호: 혼자 버티기 어렵고, 엿새 안에 보낸 적 없고, 보급을 기다리는 중이 아니면
        if (console != null && cm.SupplyEta < 0 && !cm.SupplyDocked && (cm.DistressAt < 0 || w.Tick - cm.DistressAt > SimTime.TicksPerDay * 6L)
            && cm.Crisis() is string why)
            post(WorkKind.Distress, WorkTarget.Of(console), 0.85f, Skill.Piloting, $"{why} → 조난 신호 (보급선이 캡슐을 보낸다)");
        // 보급 캡슐 내리기
        if (cm.SupplyDocked && cm.Airlock is Room air)
            post(WorkKind.UnloadSupply, WorkTarget.OfRoom(air), 0.7f, Skill.Mechanics,
                $"보급 캡슐 · {string.Join(" · ", cm.Cargo.Take(3).Select(c => $"{ItemKinds.Name(c.kind)} {c.count}"))} 외");
        // 구조 요청에 답하기: 조타(함교 콘솔 → 예비 조타석 → 통신 콘솔)
        if (cm.SignalOpen && w.Propulsion.Thrust >= 0.3f && w.Propulsion.Propellant >= w.Propulsion.EvadeCost * 2f)
        {
            var bridge = w.Ship.RoomsOf(RoomType.Bridge).FirstOrDefault(r => !r.Abandoned && !r.Detached);
            var helm = bridge?.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Console && f.Machine is Machine hm && !hm.Stopped && f.UseSpots.Count > 0)
                       ?? w.Propulsion.AuxHelm ?? console;
            if (helm != null)
                post(WorkKind.AnswerSignal, WorkTarget.Of(helm), 0.75f, Skill.Piloting,
                    $"탈출 캡슐 · 생존자 {cm.SignalSurvivors}명 · {(cm.SignalUntil - w.Tick) / (float)SimTime.TicksPerHour:0}시간 안에 · 추진제 {w.Propulsion.EvadeCost * 2f:0}kg");
        }
    }
}

public static partial class WorkPlanners
{
    private static Job SendDistress(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var console = o.Target.Furniture!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, Skill.Piloting, console.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            if (world.Comms.SupplyEta >= 0 || world.Comms.SupplyDocked) return true;
            world.Comms.SendDistress(cm, world.Comms.Crisis() ?? "버티기 어렵다");
            return true;
        }));
        return Wrap(a, o, c, w, "조난 신호", toils, "통신실에서 조난 신호를 보내러 간다", LogKind.Warning);
    }

    private static Job UnloadSupply(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var room = o.Target.Room!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(1.5f, Skill.Mechanics, room.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            var cms = world.Comms;
            world.Board.Close(o);
            if (!cms.SupplyDocked) return true;
            var ship = world.Ship;
            foreach (var (kind, count) in cms.Cargo)
            {
                int left = count;
                var boxes = kind == ItemKind.Meal
                    ? ship.Containers.Where(f => f.Type is FurnitureType.Fridge or FurnitureType.MealDispenser)
                    : ship.Containers.Where(f => f.Type == FurnitureType.Shelf);
                foreach (var box in boxes)
                {
                    left -= box.Storage!.Add(kind, left);
                    if (left <= 0) break;
                }
            }
            world.Air.Reserve = MathF.Min(world.Air.ReserveCapacity, world.Air.Reserve + cms.CargoAir);
            world.Water.Level = MathF.Min(world.Water.Capacity, world.Water.Level + cms.CargoWater);
            cms.SupplyDocked = false;
            cms.Supplies++;
            world.CycleAirlock();
            world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 보급 캡슐의 짐을 내렸다 — {string.Join(" · ", cms.Cargo.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}"))} · 공기 탱크·물탱크를 채웠다",
                room, new[] { cm }, log: true);
            cms.Cargo.Clear();
            return true;
        }));
        return Wrap(a, o, c, w, "보급 내리기", toils, "에어락의 보급 캡슐 짐을 내리러 간다");
    }

    private static Job AnswerSignal(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        var helm = o.Target.Furniture!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.5f, Skill.Piloting, helm.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            if (!world.Comms.SignalOpen) return true;
            world.Comms.AnswerSignal(cm);
            return true;
        }));
        return Wrap(a, o, c, w, "탈출 캡슐 구조", toils, "탈출 캡슐 쪽으로 배를 돌리러 간다");
    }
}

public static partial class Council
{
    internal static float CommsPressure(World w, WorkOrder o) => o.Kind switch
    {
        WorkKind.Distress => 0.65f + (w.Air.Reserve < w.Air.ReserveCapacity * 0.1f || FoodPolicy.FoodDays(w) < 0.5f ? 0.25f : 0.1f),
        // v13.4 방침(구조 신호): 늘 간다 · 여유 있을 때 · 무시
        _ => 0.5f + (w.Tick - w.Comms.SignalAt > SimTime.Hours(12) ? 0.1f : 0f) + w.Policies["distress"] switch { 0 => 0.3f, 2 => -0.6f, _ => 0f },
    };

    internal static void CommsTerms(World w, CrewMember c, WorkOrder o, List<(float v, string why)> terms, float pressure)
    {
        var t = c.Traits;
        if (o.Kind == WorkKind.Distress)
        {
            if (c.Role is CrewRole.Pilot or CrewRole.Medic) terms.Add((0.2f, "혼자서는 못 버틴다"));
            terms.Add((-0.15f * t.Bravery, "아직 버틸 수 있다"));
            terms.Add((0.1f * (1f - t.Calm), "도움을 청하자"));
            terms.Add((MathF.Max(0f, pressure - 0.7f), "바닥이 보인다"));
            return;
        }
        // 구조 요청: 사람을 두고 갈 수 없다 ↔ 먹을 입이 는다 · 추진제
        float days = FoodPolicy.FoodDays(w);
        var p = w.Propulsion;
        if (c.Role == CrewRole.Medic) terms.Add((0.35f, "사람을 두고 갈 수 없다"));
        terms.Add((0.2f * t.Sociability + 0.1f * t.Diligence, "우리도 누군가 건져 주길 바랄 것이다"));
        if (days < 4f && c.Role is CrewRole.Cook or CrewRole.Botanist) terms.Add((-0.3f * (4f - days) / 4f - 0.05f, $"먹을 것이 {days:0.0}일치다 — 입이 는다"));
        if (c.Role is CrewRole.Engineer or CrewRole.Pilot && p.Propellant < p.Capacity * 0.4f) terms.Add((-0.2f, $"추진제가 {p.Propellant:0}kg뿐이다"));
        if (w.Crew.Count(x => !x.Dead) >= w.Ship.FurnitureOf(FurnitureType.Bed).Count()) terms.Add((-0.08f, "잘 자리가 없다"));
        terms.Add((-0.1f * (1f - t.Bravery), "캡슐에 무엇이 있을지 모른다"));
    }
}
