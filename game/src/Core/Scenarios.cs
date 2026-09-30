using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 사고 시험 목록. 헤드리스 도구와 게임(실행 인자 --scenario=)이 같이 쓴다.
/// 시나리오는 "처음 상황"만 만든다 — 그다음은 전부 시스템과 승무원이 알아서 한다.
/// </summary>
public static class Scenarios
{
    public static readonly (string Name, string Description)[] All =
    {
        ("o2", "산소 발생기 두 대 모두 고장"),
        ("cooling", "냉각 펌프 한 대 고착 + 한 대 베어링 마모"),
        ("breaker", "B 회로 단락 + D 회로 차단기 트립"),
        ("meteor", "작은 운석 — 수경재배실 외벽"),
        ("bigmeteor", "큰 운석 — 창고 외벽"),
        ("powerhit", "큰 운석 — 배전실 (A 회로 → 냉각 → SCRAM)"),
        ("fire", "주방 화재"),
        ("combo", "큰 운석(창고) + 주방 화재 — 정전으로 감지 지연"),
        ("blackout", "냉각 펌프 둘 고착 + 배터리 6%"),
        ("injury", "정비사 부상"),
        ("downed", "정비사가 창고에서 쓰러짐 → 누군가 업어서 의무실로"),
        ("chaos", "큰 운석 둘(생명유지실·창고) + 식당 화재"),
        // ── 극단 상황: 몇 개는 실패해야 정상 ──
        ("nosuits", "우주복 두 벌뿐인데 두 구획 동시 파공"),
        ("nosealant", "실링폼 두 개뿐인데 큰 파공 세 곳"),
        ("keycrew", "기관장·정비사 중상 + 냉각 펌프 둘 고착"),
        ("nofuel", "보조 발전기 연료 2시간 + 배터리 바닥 + 냉각 펌프 둘 고착"),
        ("overload", "운석 둘 + 화재 둘 + 고장 셋 동시에"),
        ("secondhit", "공기 탱크가 빈 상태에서 큰 파공"),
        // ── 적응 시험 (v5): 정석으로는 못 고친다 ──
        ("darkship", "암흑 우주선: 냉각 펌프 둘 고착 + 배터리 0 + 보조 발전기 연료·연료통 없음"),
        ("nofuses", "퓨즈 하나도 없는데 A·B 회로 단락"),
        ("noparts", "일반 부품·금속판 없이 냉각 펌프 둘 고착 → 정제 → 부품 제작"),
        ("lostquarters", "실링폼 없이 침실 큰 파공 → 침실 포기"),
        // ── 재료와 순환 (v6) ──
        ("wreck", "냉각 펌프 하나 파손 + 펌프 부품 없음 → 금속판으로 펌프 부품을 만들어 다시 짜 맞춤"),
        ("noreactorctl", "원자로 제어봉 구동 불량 + 원자로 제어부·희귀 소재 없음 → Mk.1 수동 제어기"),
        ("nomaterials", "수리재·부품·원료 전부 없고 채집 장치 고장 + 냉각 펌프 고착 → 뜯어 쓰기"),
        ("lostworkshop", "실링폼 없이 정비실 큰 파공 → 정비실 포기 → 임시 정비실"),
        // ── 외부 작업과 구조 분리 (v8) ──
        ("hanging", "거대 운석 — 함교 외벽 (연결부가 끊기고 골조가 뜯긴다)"),
        ("tearoff", "연결부가 이미 몰래 피로한 함교에 거대 운석 → 뜯겨 나감"),
        ("nodrones", "드론 모두 고장 + 전자재 없음 + 엔진실에 큰 운석 둘 → EVA로 검사·보강"),
        ("jettisonfire", "소화기 없이 주방 화재 + 환기 댐퍼 고착 + 주방 문이 열에 휘어 열림 → 끌 수 없는 불"),
        ("adrift", "수경재배실이 떨어져 나간 채로 시작 → 견인·임시 도킹·재연결"),
        ("nostructure", "구조재 없이 식당에 거대 운석 → 이을 재료가 없다"),
        ("jettison", "구조재 없이 함교 연결부 넷 중 둘이 끊어지고 둘은 반쯤 → 사출할지, 버틸지 (피로와의 경주)"),
        // ── 배관과 냉각 루프 (v9) ──
        ("hotleg", "냉각 고온관이 끊어짐 → 냉각 상실 → 긴급 정지 → 밸브 잠그고 관 교체 → 냉각수 보충 → 재기동"),
        ("hotleak", "냉각 고온관에 작은 누수 → 원자로를 세우고 잠글까, 냉각수를 부어 가며 버틸까"),
        ("branch", "1번 냉각 루프가 끊어짐 → 그 루프만 잠그고 절반의 냉각으로 → 부하 차단 → 교체"),
        ("radiator", "냉각실 위쪽 외벽에 큰 운석 → 방열판·귀환관 손상 → 선체 밖 수리"),
        ("noplates", "금속판 하나뿐인데 고온관이 끊어짐 → 우회 배관"),
        ("coolantleak", "귀환관이 새는데 실링폼도 금속판도 없다 → 냉각수를 부어 가며 버티다 물이 바닥난다"),
        ("watermain", "급수 본관이 끊어짐 → 수경재배실에 물이 끊긴다 → 교체"),
        ("feedline", "냉각수 보충관이 끊어지고 냉각수 45% → 물통으로 나른다"),
        // ── 주 컴퓨터와 자동화 (v9.2) ──
        ("cpuheat", "함교 댐퍼 구동기가 닫힌 채 걸림 → 함교가 달아오른다 → 풀기 전에 주 컴퓨터가 과열하면 자동화 정지"),
        ("cpubreach", "주 컴퓨터 저장장치 오류 + 창고에 큰 운석 → 격벽이 저절로 안 닫힌다 → 손으로"),
        ("cpuparts", "전자재 없이 주 컴퓨터 저장장치 오류 → 자동화 없이 오래 버틴다 (콘솔을 뜯어 전자재)"),
        ("cpufire", "주 컴퓨터가 멈춘 채 식당 화재 → 경보가 돌지 않는다 → 누가 보기 전까지 모른다"),
        ("cpupower", "주 컴퓨터가 멈춘 채 1번 냉각 루프가 끊어짐 → 제어봉이 느리고, 전기가 모자란데 우선순위를 모른다"),
        // ── 문 구동기·조명 (v9.4) ──
        ("doormotor", "의무실·통로 문 구동기 고장 + 창고에서 정비사가 쓰러짐 → 업고 가는 길에 문이 손으로만 열린다"),
        ("doorbreach", "창고 문 구동기 고장 + 창고에 큰 운석 → 격벽이 저절로 안 잠긴다 → 통로로 번지기 전에 손으로"),
        ("darkfire", "정비실 조명이 나간 채 정비실 화재 → 캄캄한 방에서 불을 끈다"),
        ("nomotors", "모터·케이블·금속판 없이 문 구동기 셋 고장 → 손으로 여닫는 배"),
    };

    public static readonly string[] Extreme = { "nosuits", "nosealant", "keycrew", "nofuel", "overload", "secondhit" };
    public static readonly string[] Adaptive = { "nofuel", "darkship", "nofuses", "noparts", "lostquarters" };
    public static readonly string[] Materials = { "noparts", "wreck", "noreactorctl", "nomaterials", "lostworkshop" };
    public static readonly string[] Structural = { "hanging", "tearoff", "nodrones", "jettisonfire", "adrift", "nostructure", "jettison" };
    public static readonly string[] Piping = { "hotleg", "hotleak", "branch", "radiator", "noplates", "coolantleak", "watermain", "feedline" };
    public static readonly string[] Automation = { "cpuheat", "cpubreach", "cpuparts", "cpufire", "cpupower" };
    public static readonly string[] Fixtures = { "doormotor", "doorbreach", "darkfire", "nomotors" };

    /// <summary>
    /// 물자 부족 모드: 부품·수리재·원료·연료통을 바닥내고 소모품(필터·윤활유)도 조금만 남긴다.
    /// v6부터는 채집 장치가 살아 있는 한 천천히 다시 쌓인다. (실링폼 부족은 nosealant 시나리오가 따로 본다)
    /// 이 상태로 몇십 일 사고를 겪으면 우주선은 설계와 다른 모습이 된다 (v5 기준).
    /// </summary>
    public static void Scarcity(World w)
    {
        foreach (var k in ItemKinds.All)
        {
            var tier = ItemKinds.Tier(k);
            if (tier is ItemTier.General or ItemTier.Advanced or ItemTier.Raw) LimitStock(w, k, 0);
        }
        LimitStock(w, ItemKind.Plate, 0);
        LimitStock(w, ItemKind.Structure, 0);
        LimitStock(w, ItemKind.Electronics, 0);
        LimitStock(w, ItemKind.Cable, 2);
        LimitStock(w, ItemKind.Fuse, 0);
        LimitStock(w, ItemKind.Fuel, 0);
        LimitStock(w, ItemKind.Filter, 6);
        LimitStock(w, ItemKind.Lubricant, 6);
        w.Board.RequestScan();
    }

    /// <summary>모든 수리재·부품·원료를 없앤다 (채집 장치까지 멈추면 정말로 바닥).</summary>
    public static void NoMaterials(World w)
    {
        foreach (var k in ItemKinds.All)
            if (ItemKinds.Tier(k) != ItemTier.Supply) LimitStock(w, k, 0);
    }

    /// <summary>그 방에서 선체 벽에 가장 가까운 바닥 칸 (운석 과녁).</summary>
    public static Cell OuterTarget(World w, RoomType type)
    {
        var ship = w.Ship;
        // v10.10: 같은 종류의 방이 여럿이면(여러 층 배의 침실 둘) 외벽에 닿은 방을 고른다
        var room = ship.RoomsOf(type).FirstOrDefault(r => ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r))
                   ?? ship.RoomsOf(type).First();
        return OuterTarget(w, room);
    }

    /// <summary>그 방에서 선체 벽에 가장 가까운 바닥 칸.</summary>
    public static Cell OuterTarget(World w, Room room)
    {
        var ship = w.Ship;
        // v11.1: 모서리 외벽(방 칸과 대각으로만 닿는 벽)은 빼고 — 전에는 그런 벽을 고르면 옆 칸을 못 찾아 멈췄다
        var hull = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == room
                                          && Cell.Dirs4.Any(d => ship.RoomAt(kv.Key + d) == room)).Select(kv => kv.Key).ToList();
        if (hull.Count == 0) return room.Cells.First();
        var wall = hull.OrderBy(c => MathF.Abs(c.Y - room.Center.Y) + MathF.Abs(c.X - room.Center.X) * 0.3f).ThenBy(c => c.Y).ThenBy(c => c.X).First();
        return Cell.Dirs4.Select(d => wall + d).First(c => ship.RoomAt(c) == room);
    }

    private static Cell FloorIn(World w, RoomType type) => w.Ship.RoomsOf(type).First().Cells.First(w.Ship.IsOpenFloor);

    /// <summary>우주선 전체에서 그 물건을 keep개만 남기고 치운다 (물자 부족 시험).</summary>
    public static void LimitStock(World w, ItemKind kind, int keep)
    {
        int left = keep;
        foreach (var f in w.Ship.Containers)
        {
            int have = f.Storage!.Count(kind);
            int stay = Math.Min(have, left);
            f.Storage.Take(kind, have - stay);
            left -= stay;
        }
    }

    public static void Injure(World w, CrewMember c, float health, float injury, string cause)
    {
        c.Vitals.Injury = MathF.Max(c.Vitals.Injury, injury);
        c.Vitals.InjuryCause = cause;
        c.Vitals.Health = MathF.Min(c.Vitals.Health, health);
        c.Interrupt(w);
    }

    /// <summary>시나리오를 건다. 모르는 이름이면 false. focus는 지켜볼 방.</summary>
    public static bool Apply(World w, string name, out Room? focus)
    {
        var ship = w.Ship;
        focus = null;
        Room R(RoomType t) => ship.RoomsOf(t).First();
        void Meteor(RoomType t, float size) => Incidents.Meteor(w, OuterTarget(w, t), size);

        switch (name)
        {
            case "o2":
                foreach (var g in ship.FurnitureOf(FurnitureType.OxygenGenerator)) w.Machines.Break(g.Machine!, FaultKind.ElectrolyzerFault);
                focus = R(RoomType.LifeSupport);
                break;
            case "cooling":
            {
                var pumps = ship.FurnitureOf(FurnitureType.CoolantPump).ToList();
                w.Machines.Break(pumps[0].Machine!, FaultKind.PumpSeized);
                w.Machines.Break(pumps[1].Machine!, FaultKind.BearingWear);
                focus = R(RoomType.Cooling);
                break;
            }
            case "breaker":
            {
                var panel = ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
                panel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = 1 });
                panel.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = 3 });
                w.RaiseAlert("배전반 B 회로 단락, D 회로 차단기 트립", panel.Body.Room, AlertLevel.Critical, true);
                focus = panel.Body.Room;
                break;
            }
            case "meteor": Meteor(RoomType.Hydroponics, 0.35f); focus = R(RoomType.Hydroponics); break;
            case "bigmeteor": Meteor(RoomType.Storage, 1f); focus = R(RoomType.Storage); break;
            case "powerhit": Meteor(RoomType.Power, 0.9f); focus = R(RoomType.Power); break;
            case "fire": Incidents.Fire(w, FloorIn(w, RoomType.Galley)); focus = R(RoomType.Galley); break;
            case "combo":
                Meteor(RoomType.Storage, 1f);
                Incidents.Fire(w, FloorIn(w, RoomType.Galley));
                focus = R(RoomType.Galley);
                break;
            case "blackout":
                foreach (var p in ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.06f;
                focus = R(RoomType.Reactor);
                break;
            case "injury":
            {
                var hurt = w.Crew.First(c => c.Role == CrewRole.Technician);
                Injure(w, hurt, 0.35f, 0.5f, "작업 중 사고");
                focus = hurt.Room;
                break;
            }
            case "downed":
            {
                var who = w.Crew.First(c => c.Role == CrewRole.Technician);
                var spot = R(RoomType.Storage).Cells.First(ship.IsOpenFloor);
                who.EndJob(w, ToilStatus.Interrupted);
                who.Position = spot.Center;
                who.PreviousPosition = spot.Center;
                who.Room = R(RoomType.Storage);
                Injure(w, who, 0.08f, 0.6f, "선반이 무너짐");
                focus = R(RoomType.Storage);
                break;
            }
            case "chaos":
                Meteor(RoomType.LifeSupport, 1f);
                Meteor(RoomType.Storage, 0.8f);
                Incidents.Fire(w, FloorIn(w, RoomType.Mess));
                focus = R(RoomType.LifeSupport);
                break;

            case "nosuits":
                foreach (var locker in ship.FurnitureOf(FurnitureType.SuitLocker)) locker.Storage!.Take(ItemKind.Suit, 99);
                ship.FurnitureOf(FurnitureType.SuitLocker).First().Storage!.Add(ItemKind.Suit, 2);
                Meteor(RoomType.Storage, 1f);
                Meteor(RoomType.Hydroponics, 0.9f);
                focus = R(RoomType.Storage);
                break;
            case "nosealant":
                LimitStock(w, ItemKind.Sealant, 2);
                Meteor(RoomType.Storage, 1f);
                Meteor(RoomType.Hydroponics, 0.9f);
                Meteor(RoomType.Quarters, 0.9f);
                focus = R(RoomType.Quarters);
                break;
            case "keycrew":
                foreach (var c in w.Crew.Where(c => c.Role is CrewRole.Engineer or CrewRole.Technician))
                    Injure(w, c, 0.2f, 0.7f, "냉각실 배관 폭발");
                foreach (var p in ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                focus = R(RoomType.Reactor);
                break;
            case "nofuel":
                foreach (var p in ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.02f;
                w.Power.AuxFuel = 2f;
                focus = R(RoomType.Reactor);
                break;
            case "overload":
                Meteor(RoomType.Power, 0.9f);
                Meteor(RoomType.Mess, 0.8f);
                Incidents.Fire(w, FloorIn(w, RoomType.Galley));
                Incidents.Fire(w, FloorIn(w, RoomType.Quarters));
                w.Machines.Break(ship.FurnitureOf(FurnitureType.OxygenGenerator).First().Machine!);
                w.Machines.Break(ship.FurnitureOf(FurnitureType.WaterRecycler).First().Machine!);
                w.Machines.Break(ship.FurnitureOf(FurnitureType.CoolantPump).First().Machine!);
                focus = R(RoomType.Power);
                break;
            case "secondhit":
                w.Air.Reserve = 0f;
                Meteor(RoomType.Hydroponics, 1f);
                focus = R(RoomType.Hydroponics);
                break;

            case "darkship":
                foreach (var p in ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                w.Power.BatteryCharge = 0f;
                w.Power.AuxFuel = 0f;
                LimitStock(w, ItemKind.Fuel, 0);
                focus = R(RoomType.Reactor);
                break;
            case "nofuses":
            {
                LimitStock(w, ItemKind.Fuse, 0);
                var panel = ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
                panel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = 0 });
                panel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = 1 });
                w.RaiseAlert("배전반 A·B 회로 단락 — 예비 퓨즈 없음", panel.Body.Room, AlertLevel.Critical, true);
                focus = panel.Body.Room;
                break;
            }
            case "noparts":
                foreach (var k in ItemKinds.All.Where(k => ItemKinds.Tier(k) is ItemTier.General)) LimitStock(w, k, 0);
                LimitStock(w, ItemKind.Plate, 0);
                foreach (var p in ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                focus = R(RoomType.Cooling);
                break;
            case "wreck":
            {
                LimitStock(w, ItemKind.Pump, 0);
                var pump = ship.FurnitureOf(FurnitureType.CoolantPump).First().Machine!;
                pump.Condition = 0.1f; // 다음 계산에서 파손으로 넘어간다
                focus = R(RoomType.Cooling);
                break;
            }
            case "noreactorctl":
            {
                LimitStock(w, ItemKind.ReactorControl, 0);
                LimitStock(w, ItemKind.Rare, 0);
                var core = ship.FurnitureOf(FurnitureType.ReactorCore).First().Machine!;
                w.Machines.Break(core, FaultKind.ControlFault);
                focus = R(RoomType.Reactor);
                break;
            }
            case "nomaterials":
                NoMaterials(w);
                foreach (var col in ship.FurnitureOf(FurnitureType.Collector)) w.Machines.Break(col.Machine!, FaultKind.ArmMotor);
                foreach (var p in ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
                focus = R(RoomType.Cooling);
                break;
            case "lostworkshop":
                LimitStock(w, ItemKind.Sealant, 0);
                Meteor(RoomType.Workshop, 1f);
                focus = R(RoomType.Workshop);
                break;
            case "lostquarters":
                LimitStock(w, ItemKind.Sealant, 0);
                Meteor(RoomType.Quarters, 1f);
                focus = R(RoomType.Quarters);
                break;

            case "hanging":
                Meteor(RoomType.Bridge, 1.5f);
                focus = R(RoomType.Bridge);
                break;
            case "tearoff":
                // 오래된 피로: 연결부가 몰래 약해져 있다 (승무원은 모른다 — 검사한 지 오래됐다)
                foreach (var j in R(RoomType.Bridge).Joints) j.Strength = 0.35f;
                Meteor(RoomType.Bridge, 1.2f);
                focus = R(RoomType.Bridge);
                break;
            case "nodrones":
                foreach (var d in w.Drones.Drones) d.Faulty = true;
                LimitStock(w, ItemKind.Electronics, 0);
                Meteor(RoomType.Engine, 1f);
                Meteor(RoomType.Engine, 0.9f);
                focus = R(RoomType.Engine);
                break;
            case "jettisonfire":
            {
                LimitStock(w, ItemKind.Extinguisher, 0);
                var g = R(RoomType.Galley);
                g.DamperJammed = true;
                // 주방 문이 모두 열에 휘어 열린 채 걸렸다 → 식당·통로 공기가 불을 먹인다
                foreach (var d in g.Doors.Where(d => !d.IsExternal)) d.JammedOpen = true;
                foreach (var c in g.Cells.Where(ship.IsOpenFloor).Take(4)) Incidents.Fire(w, c);
                focus = g;
                break;
            }
            case "adrift":
            {
                var hy = R(RoomType.Hydroponics);
                foreach (var c in w.Crew.Where(c => c.Room == hy)) { var to = R(RoomType.Corridor).Cells.First(ship.IsOpenFloor); c.Position = to.Center; c.PreviousPosition = to.Center; c.Room = R(RoomType.Corridor); }
                w.Structure.Detach(hy, "연결부가 모두 끊어졌다 (시나리오)", controlled: false);
                focus = hy;
                break;
            }
            case "jettison":
            {
                LimitStock(w, ItemKind.Structure, 0);
                LimitStock(w, ItemKind.MetalOre, 0);
                foreach (var dock in ship.FurnitureOf(FurnitureType.DroneDock)) dock.Storage!.Take(ItemKind.Structure, 99);
                var br = R(RoomType.Bridge);
                for (int i = 0; i < br.Joints.Count; i++)
                {
                    var j = br.Joints[i];
                    if (i < br.Joints.Count - 2) { j.Strength = 0f; j.Known = 0f; }
                    else { j.Strength = j.Known = i == br.Joints.Count - 1 ? 0.5f : 0.45f; }
                }
                w.RaiseAlert("함교 연결부가 둘만 남았다", br, AlertLevel.Critical, shipWide: true);
                focus = br;
                break;
            }
            // ── v9 배관 ──
            case "hotleg":
            {
                var p = w.Piping.HotLeg!;
                w.Piping.Damage(p, 0.9f, p.Path[p.Path.Count / 2], "배관 파손 (시나리오)");
                focus = R(RoomType.Cooling);
                break;
            }
            case "hotleak":
            {
                var p = w.Piping.HotLeg!;
                w.Piping.Damage(p, 0.42f, p.Path[p.Path.Count / 2 + 1], "배관 파손 (시나리오)");
                focus = R(RoomType.Cooling);
                break;
            }
            case "branch":
            {
                var p = w.Piping.Branches[0];
                w.Piping.Damage(p, 0.9f, p.Path[p.Path.Count / 2], "배관 파손 (시나리오)");
                focus = R(RoomType.Cooling);
                break;
            }
            case "radiator":
                Meteor(RoomType.Cooling, 1f);
                focus = R(RoomType.Cooling);
                break;
            case "noplates":
            {
                LimitStock(w, ItemKind.Plate, 1);
                LimitStock(w, ItemKind.MetalOre, 0);
                foreach (var dock in ship.FurnitureOf(FurnitureType.DroneDock)) dock.Storage!.Take(ItemKind.Plate, 99);
                var p = w.Piping.HotLeg!;
                w.Piping.Damage(p, 0.9f, p.Path[p.Path.Count / 2], "배관 파손 (시나리오)");
                focus = R(RoomType.Cooling);
                break;
            }
            case "coolantleak":
            {
                LimitStock(w, ItemKind.Sealant, 0);
                LimitStock(w, ItemKind.Plate, 0);
                LimitStock(w, ItemKind.MetalOre, 0);
                foreach (var dock in ship.FurnitureOf(FurnitureType.DroneDock)) { dock.Storage!.Take(ItemKind.Plate, 99); dock.Storage.Take(ItemKind.Sealant, 99); }
                w.Water.Level = MathF.Min(w.Water.Level, 150f);
                var p = w.Piping.ColdLeg!;
                w.Piping.Damage(p, 0.5f, p.Path[p.Path.Count - 2], "배관 파손 (시나리오)");
                focus = R(RoomType.Reactor);
                break;
            }
            case "watermain":
            {
                var p = w.Piping.WaterMain!;
                w.Piping.Damage(p, 0.9f, p.Path[p.Path.Count / 2], "배관 파손 (시나리오)");
                focus = R(RoomType.Hydroponics);
                break;
            }
            case "feedline":
            {
                var p = w.Piping.WaterFeed!;
                w.Piping.Damage(p, 0.9f, p.Path[p.Path.Count / 2], "배관 파손 (시나리오)");
                w.Piping.Coolant = PipeNetwork.CoolantMax * 0.45f;
                focus = R(RoomType.Cooling);
                break;
            }
            // ── v9.2 자동화 ──
            case "cpuheat":
            {
                var br = R(RoomType.Bridge);
                br.DamperStuck = true;
                br.VentOpen = false;
                focus = br;
                break;
            }
            case "cpubreach":
            case "cpuparts":
            case "cpufire":
            case "cpupower":
            {
                var comp = w.Automation.Computer;
                if (comp != null && !comp.Has(FaultKind.StorageFault)) w.Machines.Break(comp, FaultKind.StorageFault);
                if (name == "cpubreach") { Meteor(RoomType.Storage, 1f); focus = R(RoomType.Storage); }
                else if (name == "cpupower")
                {
                    var b = w.Piping.Branches[0];
                    w.Piping.Damage(b, 0.9f, b.Path[b.Path.Count / 2], "배관 파손 (시나리오)");
                    focus = R(RoomType.Cooling);
                }
                else if (name == "cpufire")
                {
                    var mess = R(RoomType.Mess);
                    foreach (var c in mess.Cells.Where(ship.IsOpenFloor).Take(2)) Incidents.Fire(w, c);
                    focus = mess;
                }
                else
                {
                    LimitStock(w, ItemKind.Electronics, 0);
                    foreach (var dock in ship.FurnitureOf(FurnitureType.DroneDock)) dock.Storage!.Take(ItemKind.Electronics, 99);
                    focus = R(RoomType.Bridge);
                }
                break;
            }
            // ── v9.4 문 구동기·조명 ──
            case "doormotor":
            {
                var med = R(RoomType.Medbay);
                foreach (var d in med.Doors.Where(d => !d.IsExternal)) w.Fixtures.BreakDoor(d, "시나리오");
                foreach (var d in R(RoomType.Storage).Doors.Where(d => !d.IsExternal)) w.Fixtures.BreakDoor(d, "시나리오");
                var victim = w.Crew.First(c => c.Role == CrewRole.Technician);
                var st = R(RoomType.Storage);
                var spot = st.Cells.First(ship.IsOpenFloor);
                victim.EndJob(w, ToilStatus.Interrupted);
                victim.Position = spot.Center;
                victim.PreviousPosition = spot.Center;
                victim.Room = st;
                Injure(w, victim, 0.08f, 0.6f, "선반이 무너짐");
                focus = med;
                break;
            }
            case "doorbreach":
            {
                var st = R(RoomType.Storage);
                foreach (var d in st.Doors.Where(d => !d.IsExternal)) w.Fixtures.BreakDoor(d, "시나리오");
                Meteor(RoomType.Storage, 1f);
                focus = st;
                break;
            }
            case "darkfire":
            {
                var ws = R(RoomType.Workshop);
                w.Fixtures.LightsFail(ws, "배선 단락 (시나리오)");
                foreach (var c in ws.Cells.Where(ship.IsOpenFloor).Take(2)) Incidents.Fire(w, c);
                focus = ws;
                break;
            }
            case "nomotors":
            {
                LimitStock(w, ItemKind.Motor, 0);
                LimitStock(w, ItemKind.Cable, 0);
                LimitStock(w, ItemKind.Plate, 0);
                foreach (var dock in ship.FurnitureOf(FurnitureType.DroneDock)) dock.Storage!.Take(ItemKind.Plate, 99);
                var cor = R(RoomType.Corridor);
                foreach (var d in cor.Doors.Where(d => !d.IsExternal).Take(3)) w.Fixtures.BreakDoor(d, "시나리오");
                focus = cor;
                break;
            }
            case "nostructure":
                LimitStock(w, ItemKind.Structure, 0);
                foreach (var dock in ship.FurnitureOf(FurnitureType.DroneDock)) dock.Storage!.Take(ItemKind.Structure, 99);
                LimitStock(w, ItemKind.MetalOre, 0);
                Meteor(RoomType.Mess, 1.5f);
                focus = R(RoomType.Mess);
                break;
            default:
                return false;
        }
        w.History.NoteCause(w, All.FirstOrDefault(x => x.Name == name).Description ?? name);
        w.Board.RequestScan();
        return true;
    }

    /// <summary>
    /// 무작위 사고 한 묶음 (실험용). 운석·화재·고장·배관 파손(v9)을 섞어 1~4개. 무엇을 걸었는지 설명을 돌려준다.
    /// </summary>
    public static string RandomIncident(World w, Rng rng)
    {
        var ship = w.Ship;
        var rooms = ship.Rooms.Where(r => r.Type != RoomType.Corridor).ToList();
        var parts = new List<string>();
        // 한 개 40% · 두 개 35% · 세네 개 25% (플레이어는 가끔 크게 던진다)
        float c0 = rng.Float();
        int count = c0 < 0.4f ? 1 : c0 < 0.75f ? 2 : rng.Chance(0.5f) ? 3 : 4;
        for (int i = 0; i < count; i++)
        {
            float roll = rng.Float();
            if (roll < 0.5f)
            {
                // 운석: 선체 벽이 있는 방 중에서
                var hullRooms = rooms.Where(r => ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r)).ToList();
                var room = rng.Pick(hullRooms);
                float size = rng.Chance(0.4f) ? rng.Range(0.8f, 1f) : rng.Range(0.3f, 0.6f);
                Player.Meteor(w, OuterTarget(w, room), size);
                parts.Add($"{(size >= 0.7f ? "큰" : "작은")} 운석({room.Name})");
            }
            else if (roll < 0.8f)
            {
                var room = rng.Pick(rooms);
                var floor = room.Cells.Where(ship.IsOpenFloor).ToList();
                if (floor.Count == 0) continue;
                Player.Fire(w, rng.Pick(floor));
                parts.Add($"화재({room.Name})");
            }
            else if (roll < 0.9f || w.Piping.Segments.Count == 0)
            {
                var machines = ship.Machines.Where(m => m.Spec.Critical || rng.Chance(0.3f)).ToList();
                var m = rng.Pick(machines);
                Player.Break(w, m.Body);
                parts.Add($"고장({m.Name})");
            }
            else
            {
                // v9: 배관 파손 (관 한 구간의 한 칸)
                var seg = rng.Pick(w.Piping.Segments);
                var at = rng.Pick(seg.Path);
                var hit = Player.PipeBurst(w, at, rng.Range(0.1f, 1f));
                parts.Add($"배관 파손({hit?.Name ?? seg.Name})");
            }
        }
        w.Board.RequestScan();
        return string.Join(" + ", parts);
    }
}
