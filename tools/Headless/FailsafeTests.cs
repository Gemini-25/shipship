using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v16.19 페일세이프 설계 · 내구 재조정 · 중대형 사고
public static partial class Program
{
    private static int RunFailsafeTest(int seed)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.FirstOrDefault(a => a.StartsWith("--measure")) is string ms)
        {
            float days = ms.Contains('=') && float.TryParse(ms.Split('=')[1], out var dd) ? dd : 2f;
            int seeds = args.FirstOrDefault(a => a.StartsWith("--seeds=")) is string ss && int.TryParse(ss[8..], out var sn) ? sn : 2;
            string[]? only = args.FirstOrDefault(a => a.StartsWith("--ships=")) is string sh ? sh[8..].Split(',') : null;
            Durability.Legacy = args.Contains("--legacy");
            try { return RunFailsafeMeasure(seed, days, seeds, only); } finally { Durability.Legacy = false; }
        }
        _fails = 0;
        Console.WriteLine($"페일세이프 · 내구 · 중대형 사고 점검 (v16.19) · 시드 {seed}\n");
        try
        {
            string part = args.FirstOrDefault(a => a.StartsWith("--part="))?[7..] ?? "all";
            bool P(string p) => part == "all" || part.Split(',').Contains(p);
            if (P("compart")) FsCompartment(seed);
            if (P("redund")) FsRedundancy(seed);
            if (P("shed")) FsShed(seed);
            if (P("major")) FsMajors(seed);
            if (P("story")) FsStory(seed);
            if (P("bundle")) FsBundle2(seed);
            if (P("ba")) FsBeforeAfter(seed);
            if (P("det")) FsDeterminism(seed);
            if (part == "gasdbg")
            {
                var w = DayOne(seed, "Hanbit");
                var room = w.Ship.RoomsOf(RoomType.LifeSupport).First();
                Player.Hazard(w, HazardKind.GasLeak, room.Cells[0]);
                for (int h = 0; h < 30; h++)
                {
                    Run(w, SimTime.Minutes(30));
                    var src = w.Hazards.GasSource(room);
                    Console.WriteLine($"  {h * 0.5f:0.0}h 독 {room.Air.Toxin:0.00} 샘 {(src == null ? "-" : src.Body.Type.ToString())} 댐퍼 {room.VentOpen} · 문 {string.Join(",", room.Doors.Select(d => (d.Locked ? "L" : "o") + (w.Failsafe.Latched(d) ? "*" : "")))} · 방 안 {string.Join(",", w.Crew.Where(c => c.Room == room).Select(c => c.Name + (c.Suit != null ? "(옷)" : "")))}");
                    foreach (var o in w.Board.Open.Where(o => o.Target?.Room == room)) Console.WriteLine($"     일감 {o.Kind} {o.Title} 맡은 {o.Assignee?.Name ?? "-"} 막힘 {o.BlockedReason ?? "-"}");
                }
                foreach (var e in w.Major.Cases) Console.WriteLine($"  큰 사고 {e.Kind} {e.Phase} 방 {e.Room} 시작 {SimTime.Clock(e.Start)}");
                foreach (var l in w.Log.Entries.Where(l => l.Text.Contains("가스") || l.Text.Contains("암모니아") || l.Text.Contains("냉매")).TakeLast(12)) Console.WriteLine($"  기록 {SimTime.Clock(l.Tick)} {l.Text}");
                foreach (var c in w.Crew.Where(c => !c.Dead).Take(6)) Console.WriteLine($"  {c.Name} {c.Room?.Name} 일 {c.Job?.Label}");
            }
            if (part == "cosdbg")
            {
                var w = DayOne(seed, "Hanbit");
                w.Propulsion.Propellant = 0f;
                var e = w.Cosmic.Force(CosmicKind.BigAsteroid, 8f, close: true);
                if (w.Automation.Asks.Pending("cosmic:avoid:" + e.Id) is Proposal pp) w.Automation.Asks.Decide(pp, true, "관찰자");
                var room = w.Ship.Rooms[e.TargetRoom];
                var stay = w.Crew.First(c => !c.Dead);
                stay.Position = room.Cells.First(c => w.Ship.IsWalkable(c)).Center;
                bool lastL = false, lastA = false;
                for (long t = 0; t < SimTime.Hours(10) && w.Tick < e.Arrive - 1; t++)
                {
                    w.Step();
                    if (room.Lockdown != lastL || room.Abandoned != lastA) { Console.WriteLine($"  {SimTime.Clock(w.Tick)} 봉쇄 {room.Lockdown} 비움 {room.Abandoned} 압 {room.Air.Pressure:0} 샘 {room.Leaking}"); lastL = room.Lockdown; lastA = room.Abandoned; }
                }
                Console.WriteLine($"  끝 {SimTime.Clock(w.Tick)} 봉쇄 {room.Lockdown} 비움 {room.Abandoned} 계획 {e.SealPlan} 봉함 {e.Sealed}");
                foreach (var l in w.Log.Entries.Where(l => l.Text.Contains(room.Name)).TakeLast(15)) Console.WriteLine($"  기록 {SimTime.Clock(l.Tick)} {l.Text}");
            }
            if (part == "scaledbg") FsScaleDebug(seed);
            if (part == "bodydbg")
            {
                var w = DayOne(seed, "Hanbit");
                RunUntilHour(w, 11f);
                var door = w.Ship.Doors.Where(d => !d.IsExternal && !d.Bulkhead && d.RoomA != null && d.RoomB != null && w.Body.DoorOf(d)!.Zone == AccessZone.Open
                    && d.RoomA.Kind != RoomType.Reactor && d.RoomB.Kind != RoomType.Reactor).OrderBy(d => d.Id).Skip(2).First();
                var dir = door.ConnectsVertically ? new Cell(0, 1) : new Cell(1, 0);
                Cell from = door.Cell - dir - dir, to = door.Cell + dir + dir;
                if (!w.Ship.IsWalkable(from) || !w.Ship.IsWalkable(to)) { from = door.Cell - dir; to = door.Cell + dir; }
                door.RoomA!.BreakerOff = true; door.RoomB!.BreakerOff = true; Run(w, World.SystemInterval * 2);
                var p = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside).OrderBy(c => c.Id).First();
                Teleport(w, p, from);
                w.Step();
                Force(w, p, new Job(null, "지나가기", new List<Toil> { new GotoToil(to), new WaitToil(30, Pose.Standing) }));
                for (int t = 0; t < SimTime.Minutes(10); t++)
                {
                    w.Step();
                    if (t % 25 == 0) Console.WriteLine($"  t{t} {p.Cell} → {to} · 문 열림 {door.Openness:0.00} 잠김 {door.Locked} 걸림 {w.Failsafe.Latched(door)} 전기 {door.Powered} · {door.RoomA.Name} {door.RoomA.Air.Pressure:0} {door.RoomB.Name} {door.RoomB.Air.Pressure:0} · 길 {p.Path?.Count} · 일 {p.Job?.Label} {p.Name}");
                    if (p.Cell == to) break;
                }
            }
        }
        finally { Durability.Legacy = false; }
        Console.WriteLine(_fails == 0 ? "\n✔ 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    // ── 1) 구획: 컴퓨터가 멎고 문이 열려 있어도 감압은 맞은 방 + 이웃 하나를 넘지 않는다 ──
    private static (int dragged, int maxLow, World w, Room hit) FsMeteorRoom(int seed, bool legacy)
    {
        Durability.Legacy = legacy;
        try
        {
            var w = DayOne(seed, "Hanbit");
            // 주 컴퓨터가 멎었다 — 자동 격벽 · 댐퍼가 없다 (예비 코어도 없다)
            w.Automation.Backup = false;
            foreach (var f in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(f.Machine!, FaultKind.StorageFault);
            Run(w, SimTime.Minutes(2));
            var hit = w.Ship.Rooms.Where(r => !r.Detached && r.Type is RoomType.Quarters or RoomType.Lounge or RoomType.Mess or RoomType.Storage
                    && r.Doors.Count(d => !d.IsExternal) >= 1 && w.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == r))
                .OrderBy(r => r.Id).First();
            // 사람이 드나들어 문이 열려 있다 (선실 주인이 열어 둔 문)
            foreach (var d in hit.Doors.Where(d => !d.IsExternal)) d.HoldOpen = true;
            Incidents.Meteor(w, Scenarios.OuterTarget(w, hit), 1.1f);
            int maxLow = 0;
            var dragged = new HashSet<int>();
            for (int i = 0; i < 40; i++)
            {
                Run(w, SimTime.Minutes(1));
                int low = 0;
                foreach (var r in w.Ship.Rooms.Where(r => !r.Detached))
                    if (r.Air.Pressure < 70f) { low++; if (!r.Leaking && r != hit) dragged.Add(r.Id); }
                maxLow = Math.Max(maxLow, low);
            }
            return (dragged.Count, maxLow, w, hit);
        }
        finally { Durability.Legacy = false; }
    }

    private static void FsCompartment(int seed)
    {
        Console.WriteLine("── 구획: 차압 문 · 역류 방지 댐퍼 · 비상 칸막이 ──");
        var (newDrag, newLow, w, hit) = FsMeteorRoom(seed, false);
        var (oldDrag, oldLow, _, _) = FsMeteorRoom(seed, true);
        Check("운석 파공 — 컴퓨터가 멎고 문이 열려 있어도 감압이 맞은 방 + 이웃 하나를 넘지 않는다", newDrag <= 1,
            $"{hit.Name} · 끌려간 방 {newDrag} (예전 {oldDrag}) · 70kPa 아래 최대 {newLow}방 (예전 {oldLow})");
        if (Environment.GetCommandLineArgs().Contains("--fsdebug")) foreach (var e in w.Failsafe.Events.Take(40)) Console.WriteLine($"    [{SimTime.Clock(e.Tick)}] {e.Kind} {e.Text}");
        Check("차압 문이 저절로 닫혀 걸렸다 (전기 · 컴퓨터 없이)", w.Failsafe.Latches > 0, $"걸림 {w.Failsafe.Latches} · 풀림 {w.Failsafe.Unlatches} · 실패 {w.Failsafe.LatchFails}");
        Check("새는 방 환기구가 저절로 닫혔다 (역류 방지 댐퍼)", !hit.VentOpen || w.Failsafe.DamperCloses > 0, $"닫힘 {w.Failsafe.DamperCloses}");
        // 승무원: 문이 닫히는 것을 본 사람은 저쪽이 샌다고 믿는다
        bool believed = w.Crew.Any(c => !c.Dead && w.Brain2.Beliefs.Believes(c, Topic.Breach, hit.Id));
        Check("승무원 — 차압 문이 닫히는 걸 본 사람은 그 방이 샌다고 믿고 피한다", w.Failsafe.Believed > 0 || believed, $"믿음 {w.Failsafe.Believed} · 마음 놓음 {w.Failsafe.Calmed}");
        // 주 컴퓨터를 고치면: 멎어 있던 동안 배가 스스로 버틴 일을 읽고 정리한다
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.MainComputer)) f.Machine!.Faults.Clear();
        Run(w, SimTime.Minutes(5));
        Check("주 컴퓨터 — 다시 돌면 멎은 동안 저절로 닫힌 문 · 댐퍼를 읽고 판단 근거로 남긴다", w.Failsafe.Reviews > 0 && (w.Automation.Book.Acts.Any(a => a.Key.StartsWith("fs:")) || w.Automation.Reasoning.Any(r => r.text.Contains("차압 문"))),
            w.Automation.Reasoning.LastOrDefault(r => r.text.Contains("차압")).text ?? $"정리 {w.Failsafe.Reviews} · 기록 {w.Automation.Book.Acts.Count(a => a.Key.StartsWith("fs:"))}");
        // 휜 문틀은 걸리지 않는다 → 경고
        {
            var w2 = DayOne(seed, "Mirinae");
            var room = w2.Ship.Rooms.Where(r => !r.Detached && r.Type is RoomType.Quarters or RoomType.Storage or RoomType.Lounge or RoomType.Mess && r.Doors.Count(d => !d.IsExternal) >= 1
                && w2.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w2.Ship, kv.Key) == r)).OrderBy(r => r.Id).First();
            var bent = room.Doors.First(d => !d.IsExternal);
            foreach (var d in room.Doors.Where(d => !d.IsExternal)) d.Bent = 0.5f; // 문틀이 휘었다
            var hullCell = w2.Ship.Walls.First(kv => kv.Value.IsHull && Hull.InsideRoom(w2.Ship, kv.Key) == room).Key;
            Hull.Damage(w2.Ship, hullCell, 1.5f);
            Run(w2, SimTime.Minutes(5));
            if (Environment.GetCommandLineArgs().Contains("--fsdebug"))
                Console.WriteLine($"    {room.Name} 샘 {room.Leaking} {room.Air.Pressure:0}kPa · 문 {bent.RoomA?.Name}/{bent.RoomB?.Name} 열림 {bent.Openness:0.00} 잠김 {bent.Locked} · 옆 {(bent.RoomA == room ? bent.RoomB : bent.RoomA)?.Air.Pressure:0}kPa · 사건 {string.Join(" / ", w2.Failsafe.Events.Select(e => e.Text))}");
            Check("휜 문틀 — 차압 문이 못 닫히면 경보 · 컴퓨터가 사람을 보내라고 한다", w2.Failsafe.LatchFails > 0, $"실패 {w2.Failsafe.LatchFails} · {w2.Failsafe.Events.LastOrDefault(e => e.Kind == "fail")?.Text}");
        }
        // 큰 방: 비상 칸막이
        {
            var w3 = DayOne(seed, "Eunha");
            var big = w3.Ship.Rooms.Where(r => !r.Detached && r.Partition > 0 && r.Type != RoomType.Corridor && w3.Ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(w3.Ship, kv.Key) == r)).OrderByDescending(r => r.Volume).FirstOrDefault();
            if (big == null) Check("큰 방 비상 칸막이", false, "칸막이 있는 방이 없다");
            else
            {
                Incidents.Meteor(w3, Scenarios.OuterTarget(w3, big), 1.2f);
                Run(w3, SimTime.Minutes(3));
                Check("큰 방에 큰 구멍 — 비상 칸막이가 펼쳐져 천천히 빠진다", big.Partition == 2 || w3.Failsafe.PartitionDeploys > 0,
                    $"{big.Name} {big.Volume}칸 · 누출 {big.LeakArea:0.00} · 칸막이 {big.Partition} · {big.Air.Pressure:0}kPa");
            }
        }
        // 미세 누출은 천천히
        Check("미세 누출과 파공 구분 — 균열은 쉬익 천천히, 파공은 그대로", Durability.LeakArea(0.05f) < 0.05f * 0.5f && Durability.LeakArea(0.6f) == 0.6f,
            $"0.05 → {Durability.LeakArea(0.05f):0.000} · 0.6 → {Durability.LeakArea(0.6f):0.00}");
    }

    // ── 2) 이중화: 간선 하나 · 회로 하나가 끊겨도 생명유지 · 컴퓨터는 다른 갈래로 산다 ──
    private static void FsRedundancy(int seed)
    {
        Console.WriteLine("── 예비 회로 · 보조 간선 · 장갑 벽 ──");
        foreach (var ship in new[] { "Mirinae", "Hanbit" })
        {
            var w = DayOne(seed, ship);
            var ls = w.Ship.RoomsOf(RoomType.LifeSupport).First(r => !r.Detached);
            var comp = w.Ship.FurnitureOf(FurnitureType.MainComputer).First().Room;
            Check($"{ship}: 필수 방은 처음부터 예비 회로 · 보조 간선", ls.AltCircuit >= 0 && comp.AltCircuit >= 0 && w.Net.Rings.Count(r => r.kind == NetKind.Power && (r.to == ls.Id || r.from == ls.Id)) > 0,
                $"생명유지 {PowerGrid.CircuitName(ls.Circuit)}→{(ls.AltCircuit >= 0 ? PowerGrid.CircuitName(ls.AltCircuit) : "-")} · 컴퓨터실 {PowerGrid.CircuitName(comp.Circuit)}→{(comp.AltCircuit >= 0 ? PowerGrid.CircuitName(comp.AltCircuit) : "-")} · 보조 간선 {w.Net.Rings.Count(r => r.kind == NetKind.Power)}");
            // 간선: 생명유지실 문 쪽 간선을 모두 끊는다
            foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Power && l.Door != null && (l.Door.RoomA == ls || l.Door.RoomB == ls)).ToList()) w.Net.Hurt(l, 1f, "시험");
            foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Power && l.Door != null && (l.Door.RoomA == comp || l.Door.RoomB == comp)).ToList()) w.Net.Hurt(l, 1f, "시험");
            // 회로: 생명유지실 · 컴퓨터실의 주 회로가 단락으로 죽는다 (원격으로 다시 올릴 수 없는 고장 — 차단기 트립은 컴퓨터가 곧 올려 시험이 안 된다)
            var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
            foreach (int c in new[] { ls.Circuit, comp.Circuit }.Distinct())
                if (!panel.Faults.Any(f => f.Circuit == c)) panel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = c });
            var cm = w.Ship.FurnitureOf(FurnitureType.MainComputer).First().Machine!;
            int lsDark = 0, cmDark = 0; // 10분 동안 매 시스템 틱 — 한 번이라도 꺼졌나
            for (int t = 0; t < SimTime.Minutes(10); t++)
            {
                w.Step();
                if (w.Tick % World.SystemInterval != 1) continue;
                if (!ls.Powered) lsDark++;
                if (!cm.Powered) cmDark++;
            }
            Check($"{ship}: 간선 · 주 회로가 끊겨도 생명유지실 · 주 컴퓨터는 한 번도 꺼지지 않는다", lsDark == 0 && cmDark == 0 && ls.Powered && comp.Powered && cm.Powered && w.Automation.MainOnline && w.Failsafe.Transfers > 0,
                $"생명유지 꺼진 틱 {lsDark}(간선 {(ls.PowerLinked ? "이어짐" : "끊김")}) · 컴퓨터 꺼진 틱 {cmDark} · 넘어간 방 {w.Failsafe.Transfers}");
            if (ship == "Hanbit")
            {
                Check("주 컴퓨터 — 예비 회로로 넘어간 것을 읽고 판단 근거로 남긴다", w.Automation.Book.Acts.Any(a => a.Key.StartsWith("fs:ats")) || w.Automation.Reasoning.Any(r => r.text.Contains("예비 회로")),
                    w.Automation.Reasoning.LastOrDefault(r => r.text.Contains("예비 회로")).text ?? "(기록만)");
                // 예전 값이면 꺼진다 (비교)
                Durability.Legacy = true;
                try
                {
                    var o = DayOne(seed, ship);
                    var ols = o.Ship.RoomsOf(RoomType.LifeSupport).First(r => !r.Detached);
                    var opanel = o.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
                    opanel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = o.Tick, Circuit = ols.Circuit });
                    int oDark = 0;
                    for (int t = 0; t < SimTime.Minutes(10); t++) { o.Step(); if (o.Tick % World.SystemInterval == 1 && !ols.Powered) oDark++; }
                    Check("비교 — 예전 배는 같은 회로 하나가 죽자 생명유지실이 꺼졌다", oDark > 0, $"예전 생명유지 꺼진 틱 {oDark}");
                }
                finally { Durability.Legacy = false; }
                Check("장갑 벽 — 원자로 · 배전 · 주 컴퓨터실 벽은 충격 피해 절반", w.Failsafe.ArmorWalls > 0 && w.Ship.Walls.Any(kv => kv.Value.Armor <= 0.5f),
                    $"장갑 벽 {w.Failsafe.ArmorWalls}칸");
            }
        }
        // 필수도 표
        Check("필수도 표 — 산소 발생기 · 주 컴퓨터는 생명 · 커피 머신은 편의 · 함교는 운항", ShipSim.Core.Essentials.Of(FurnitureType.OxygenGenerator) == Essential.Vital && ShipSim.Core.Essentials.Of(FurnitureType.MainComputer) == Essential.Vital
            && ShipSim.Core.Essentials.Of(FurnitureType.CoffeeMachine) == Essential.Comfort && ShipSim.Core.Essentials.Of(RoomType.Bridge) == Essential.Core,
            string.Join(" · ", Enum.GetValues<Essential>().Select(e => $"{ShipSim.Core.Essentials.Name(e)} {Enum.GetValues<FurnitureType>().Count(t => ShipSim.Core.Essentials.Of(t) == e)}")));
    }

    // ── 3) 우아한 저하: 원자로가 멈추면 계전기가 편의 설비부터 내린다 ──
    private static void FsShed(int seed)
    {
        Console.WriteLine("── 부하 차단 계전기 ──");
        var w = DayOne(seed, "Hanbit");
        // 냉각 펌프가 모두 들러붙고 예비 부품도 없다 — 원자로가 서고 배터리로 버틴다
        foreach (var box in w.Ship.Containers) foreach (var it in new[] { ItemKind.Pump, ItemKind.Bearing, ItemKind.Lubricant }) box.Storage!.Take(it, 99);
        foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
        int shedAt = -1, shed1 = -1;
        for (int i = 0; i < 36 * 6 && shedAt < 0; i++)
        {
            Run(w, SimTime.Minutes(10));
            foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) if (p.Machine!.Faults.Count == 0) w.Machines.Break(p.Machine!, FaultKind.PumpSeized); // 고칠 부품이 없다
            if (w.Failsafe.ShedLevel >= 1 && shed1 < 0) shed1 = i;
            if (w.Failsafe.ShedLevel >= 2) shedAt = i;
        }
        if (Environment.GetCommandLineArgs().Contains("--fsdebug")) Console.WriteLine($"    냉각 {w.Power.CoolingCapacity:0.0} · 원자로 {w.Power.ReactorOnline} 한계 {w.Power.ReactorLimit:0} 수요 {w.Power.Demand:0} · 배터리 {w.Power.BatteryPercent:0.00} 흐름 {w.Power.BatteryFlow:0.0} · 펌프 고장 {w.Ship.FurnitureOf(FurnitureType.CoolantPump).Count(p => p.Machine!.Faults.Count > 0)}/{w.Ship.FurnitureOf(FurnitureType.CoolantPump).Count()}");
        var low = w.Ship.Machines.Where(m => m.Spec.PowerDraw > 0f && ShipSim.Core.Essentials.Of(m.Body.Type) >= Essential.Support && !m.Body.Room.Detached).ToList();
        var vital = w.Ship.Machines.Where(m => m.Spec.PowerDraw > 0f && m.Body.Type is FurnitureType.OxygenGenerator or FurnitureType.MainComputer).ToList();
        Check("원자로가 멈춰 배터리로 버티면 — 편의 · 작업 설비부터 내리고 생명유지 · 컴퓨터는 남긴다", shed1 >= 0 && shedAt >= shed1 && vital.All(m => !m.Parked) && low.Count > 0 && low.All(m => m.Parked),
            $"원자로 {(w.Power.ReactorOnline ? "가동" : "정지")} · 1단 {shed1 * 10}분 · 2단 {shedAt * 10}분 · 배터리 {w.Power.BatteryPercent * 100:0}% · 내린 설비 {w.Failsafe.ShedNow}/{low.Count}");
        bool reasoned = w.Automation.Book.Acts.Any(a => a.Key == "fs:shed") || w.Automation.Reasoning.Any(r => r.text.Contains("계전기"));
        Check("주 컴퓨터 — 계전기가 내린 것을 읽고 남은 배터리와 함께 적는다", reasoned, w.Automation.Reasoning.LastOrDefault(r => r.text.Contains("계전기")).text ?? "(기록만)");
    }

    // ── 4) 중대형 사고 24: 모두 일어나고 · 대응 · 흔적 · 기억이 남는다 ──
    private static void FsMajors(int seed)
    {
        Console.WriteLine("── 계통 · 배 전체 사고 24 ──");
        var kinds = Enum.GetValues<MajorKind>();
        Check("표 — 24종 · 계통과 배 전체 · 규모 표에 줄이 있다", kinds.Length >= 20 && kinds.Length <= 30 && MajorIncidentSystem.All.Select((s, i) => (int)s.Kind == i).All(x => x)
            && MajorIncidentSystem.All.All(s => ScaleTable.Row("major:" + s.Key)?.Base == s.Scale && s.Scale >= IncidentScale.System) && ScaleTable.Unclassified().Count == 0,
            $"{kinds.Length}종 · 계통 {MajorIncidentSystem.All.Count(s => s.Scale == IncidentScale.System)} · 배 전체 {MajorIncidentSystem.All.Count(s => s.Scale == IncidentScale.Ship)}");
        Check("사고마다 원인 · 전조 · 대응 · 흔적 · 기억 글이 있다", MajorIncidentSystem.All.All(s => s.Cause.Length > 4 && s.Omen.Length > 4 && s.Response.Length > 4 && s.Trace.Length > 4 && s.Memory.Length > 4), "");
        var started = new List<string>();
        var failed = new List<string>();
        var settledOrResponded = new List<string>();
        var groups = kinds.Select((k, i) => (k, i)).GroupBy(x => x.i % 4).Select(g => g.Select(x => x.k).ToList()).ToList();
        var ships = new[] { "Hanbit", "Eunha", "Hanbit", "Eunha" };
        int traces = 0, responders = 0, scaleCases = 0, broadcasts = 0, memories = 0;
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var w = DayOne(seed + gi, ships[gi]);
            foreach (var kind in groups[gi])
            {
                string? what = w.Major.Start(kind, null, omen: false);
                if (what == null) { failed.Add(MajorIncidentSystem.Spec(kind).Name); continue; }
                started.Add(MajorIncidentSystem.Spec(kind).Name);
                var k = w.Major.Cases.Last(c => c.Kind == kind);
                Run(w, SimTime.Hours(5));
                if (k.Responders.Count > 0 || k.Phase == MajorPhase.Contained) settledOrResponded.Add(MajorIncidentSystem.Spec(kind).Name);
                responders += k.Responders.Count;
                if (w.Scale.Cases.Any(c => c.Key == "major:" + MajorIncidentSystem.Spec(kind).Key)) scaleCases++;
            }
            Run(w, SimTime.Hours(6));
            traces += w.Major.Traces.Count;
            broadcasts += w.Major.Broadcasts;
            memories += w.Crew.Sum(c => c.Memory.Marks.Count(m => MajorIncidentSystem.All.Any(s => m.Text.Contains(s.Name))));
        }
        Check("24종이 모두 실제로 일어난다", failed.Count == 0, $"일어남 {started.Count}" + (failed.Count > 0 ? $" · 못 건 것: {string.Join(", ", failed)}" : ""));
        Check("대응 — 사람이 붙었거나 수습됐다 (대부분)", settledOrResponded.Count >= started.Count * 0.75f, $"{settledOrResponded.Count}/{started.Count} · 붙은 사람 연 {responders}명");
        Check("규모 체계가 사건으로 잡는다 (계통 · 배 전체 대응 인원)", scaleCases >= started.Count * 0.9f, $"{scaleCases}/{started.Count}");
        Check("흔적 · 기억이 남는다 (그림 흔적 · 승무원 기억)", traces >= started.Count / 2 && memories > 0, $"흔적 {traces} · 기억 {memories}");
        Check("주 컴퓨터 — 발생하면 대응 순서를 방송한다", broadcasts >= started.Count / 2, $"방송 {broadcasts}");
        // 전조: 컴퓨터가 읽고 · 승무원이 손보면 사고가 오지 않는다
        {
            var w = DayOne(seed, "Hanbit");
            string? what = w.Major.Start(MajorKind.CoolantHeader, null, omen: true);
            var k = w.Major.Cases.LastOrDefault(c => c.Kind == MajorKind.CoolantHeader);
            var f = k == null ? null : w.Ship.Furniture.FirstOrDefault(x => x.Id == k.Machine);
            bool omen = k?.Phase == MajorPhase.Omen && f?.Machine?.Omen != null;
            if (f?.Machine?.Omen is Omen o) { o.Known = true; o.KnownBy = "감지기"; }
            Run(w, SimTime.Minutes(2));
            Check("전조 — 설비에 기척이 깃들고 주 컴퓨터가 그 사고로 판단해 미리 손보자고 한다", omen && k!.OmenRead && w.Major.OmensRead > 0, what ?? "(못 걸었다)");
            var fixer = w.Crew.First(c => c.CanAct);
            if (f?.Machine != null) Prevention.Fixed(w, f.Machine, fixer, null);
            Run(w, SimTime.Minutes(2));
            Check("전조를 손보면 사고가 오지 않는다 (막았다는 기록)", k?.Phase == MajorPhase.Averted && w.Major.Averted > 0 && w.History.Events.Any(e => e.Text.Contains("미리 손봐")), $"{k?.Phase}");
            // 아무도 손보지 않으면 때가 되어 터진다
            string? w2 = w.Major.Start(MajorKind.AirPlantFail, null, omen: true);
            var k2 = w.Major.Cases.LastOrDefault(c => c.Kind == MajorKind.AirPlantFail);
            var f2 = k2 == null ? null : w.Ship.Furniture.FirstOrDefault(x => x.Id == k2.Machine);
            if (f2?.Machine?.Omen is Omen o2) o2.Due = w.Tick + SimTime.Minutes(1);
            Run(w, SimTime.Minutes(3));
            Check("전조를 놓치면 때가 되어 터진다", k2?.Phase is MajorPhase.Active or MajorPhase.Contained, $"{w2} → {k2?.Phase}");
        }
    }

    // ── 5) 이야기꾼: 규모 비율로 고른다 ──
    private static void FsStory(int seed)
    {
        Console.WriteLine("── 사고 분포 ──");
        var w = DayOne(seed, "Hanbit");
        var pool = new List<(string key, float weight)> { ("meteor", 10f), ("fire", 8f), ("break", 10f), ("bigmeteor", 3f) };
        foreach (var s in Hazards.All) pool.Add((s.Kind.ToString(), s.Weight));
        float Share(List<(string key, float weight)> p, Func<IncidentScale, bool> f) => p.Where(x => f(ScaleTable.OfKey(x.key))).Sum(x => x.weight) / p.Sum(x => x.weight);
        float smallBefore = Share(pool, s => s <= IncidentScale.Room), bigBefore = Share(pool, s => s >= IncidentScale.System);
        MajorIncidentSystem.AddToPool(w, pool, false, 1f);
        MajorIncidentSystem.ShapeByScale(pool);
        float smallAfter = Share(pool, s => s <= IncidentScale.Room), bigAfter = Share(pool, s => s >= IncidentScale.System);
        Check("자잘한 사고 비율이 줄고 계통 · 배 전체가 늘었다", smallAfter < smallBefore - 0.1f && bigAfter >= 0.55f,
            $"방 이하 {smallBefore * 100:0}% → {smallAfter * 100:0}% · 계통 이상 {bigBefore * 100:0}% → {bigAfter * 100:0}%");
        string? what = w.Hazards.FireStory("major:trunkfire", null);
        Check("이야기꾼 열쇠로 큰 사고를 건다", what != null && w.Major.Cases.Count > 0, what ?? "");
        // 이야기꾼 항해: 큰 사고가 실제로 고른다
        float p0 = Storyteller.PersonaValue, l0 = Storyteller.LevelValue;
        try
        {
            Storyteller.PersonaValue = 1; Storyteller.LevelValue = 4;
            var v = DayOne(seed + 1, "Hanbit");
            Run(v, SimTime.TicksPerDay * 4);
            int majors = v.Major.Cases.Count;
            var keys = v.Story.Journal.Select(j => j.what).ToList();
            Check("이야기꾼 항해 4일 — 큰 사고가 섞여 나온다", majors > 0 || keys.Any(k => MajorIncidentSystem.All.Any(s => k.Contains(s.Name))), $"이야기꾼 {v.Story.Fired}건 · 큰 사고 {majors}: {string.Join(", ", keys.Take(6))}");
        }
        finally { Storyteller.PersonaValue = p0; Storyteller.LevelValue = l0; }
    }

    // ── 6) 보통 재해 묶음: 운석우 · 화재 · 정전 · 배관 파열 — 시드 여럿 평균 사망 0~1 ──
    private static void FsBundle2(int seed)
    {
        Console.WriteLine("── 보통 재해 묶음 ──");
        int runs = 0, deaths = 0;
        float essDark = 0f;
        var causes = new List<string>();
        foreach (var ship in new[] { "Mirinae", "Hanbit" })
            foreach (int s in new[] { seed, seed + 4, seed + 8 })
            {
                var w = DayOne(s, ship);
                int d0 = w.Crew.Count(c => c.Dead);
                var ess = w.Ship.Rooms.Where(r => !r.Detached && ShipSim.Core.Essentials.DualFeed(r)).ToList();
                foreach (var key in FsBundle)
                {
                    w.Hazards.FireStory(key, null);
                    for (int i = 0; i < 8 * 6; i++)
                    {
                        Run(w, SimTime.Minutes(10));
                        essDark += ess.Count(r => !r.Powered && !r.Detached) / 6f;
                    }
                }
                runs++;
                var dead = w.Crew.Where(c => c.Dead).ToList();
                deaths += dead.Count - d0;
                causes.AddRange(dead.Select(c => c.Vitals.InjuryCause ?? "?"));
            }
        float mean = deaths / (float)runs;
        Check("보통 재해 넷을 겪어도 평균 사망 0~1명 (가끔)", mean <= 1f, $"{runs}척 · 사망 {deaths} (평균 {mean:0.00}){(causes.Count > 0 ? " · " + string.Join(", ", causes) : "")}");
        Check("필수 방 정전 — 재해 넷 동안 한 척에 평균 2방시간 아래", essDark / runs < 2f, $"평균 {essDark / runs:0.00}방시간");
    }

    // ── 7) 전/후 측정표 (같은 실행 파일 — 예전 값으로 한 번, 지금 값으로 한 번) ──
    private static void FsBeforeAfter(int seed)
    {
        Console.WriteLine("── 전/후 측정 (재해 묶음 · 2척 × 시드 1) ──");
        var sw = Stopwatch.StartNew();
        FsProbe M(bool legacy)
        {
            Durability.Legacy = legacy;
            try
            {
                var all = new FsProbe();
                foreach (var ship in new[] { "Mirinae", "Hanbit" })
                    foreach (var key in FsBundle) all.Add(FsDisaster(seed, ship, key, 10f));
                return all;
            }
            finally { Durability.Legacy = false; }
        }
        var before = M(true);
        var after = M(false);
        Console.WriteLine("  " + FsLine("전", before, 8));
        Console.WriteLine("  " + FsLine("후", after, 8));
        Check("전/후 — 정전 방시간 · 필수 방 정전이 줄었다", after.DarkRoomH < before.DarkRoomH * 0.7f && after.DarkEssH <= before.DarkEssH,
            $"정전 방시간 {before.DarkRoomH:0} → {after.DarkRoomH:0} · 필수 {before.DarkEssH:0.0} → {after.DarkEssH:0.0} · 설비 고장 {before.Faults} → {after.Faults} ({sw.Elapsed.TotalSeconds:0}초)");
        Check("전/후 — 감압이 옆방을 끌고 가지 않는다", after.DecoDragged <= before.DecoDragged && after.DecoMaxRooms <= Math.Max(2, before.DecoMaxRooms), $"끌려간 방 {before.DecoDragged} → {after.DecoDragged} · 최대 {before.DecoMaxRooms} → {after.DecoMaxRooms}");
    }

    private static void FsScaleDebug(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var galley = w.Ship.RoomsOf(RoomType.Galley).First();
        bool Safe(Room r) => !r.Detached && r != galley && r.Type is not (RoomType.Corridor or RoomType.Reactor) && r.Cells.Any(w.Ship.IsOpenFloor)
                             && !r.Furniture.Any(x => x.Type is FurnitureType.CoolantPump or FurnitureType.PowerPanel or FurnitureType.MainComputer or FurnitureType.ReactorCore or FurnitureType.Battery);
        var others = w.Ship.Rooms.Where(Safe).OrderBy(r => r.Id).ToList();
        Incidents.Fire(w, galley.Cells.First(w.Ship.IsOpenFloor));
        Run(w, SimTime.Minutes(2));
        var k = w.Scale.OpenCases.FirstOrDefault(x => x.Key == "cause:Fire" && (x.RoomId == galley.Id || x.Rooms.Contains(galley.Id)));
        if (k != null) using (w.Causes.Because(k.Root)) foreach (var r in others.Take(3)) w.Fire.Ignite(r.Cells.First(w.Ship.IsOpenFloor), 0.35f);
        Run(w, SimTime.Minutes(4));
        for (int waited = 0; waited < 180 && k != null && k.Open; waited++)
        {
            foreach (var r in w.Ship.Rooms) if (w.Fire.CountIn(r) > 0) w.Fire.ClearRoom(r);
            Run(w, SimTime.Minutes(1));
        }
        Console.WriteLine($"사건 {(k == null ? "없음" : k.Open ? "열림" : "닫힘")}");
        var inc = k == null ? null : w.Causes.IncidentOf(k.Root);
        if (inc != null) foreach (var id in inc.Nodes) { var n = w.Causes.Node(id); if (n.Open) Console.WriteLine($"  열린 고리 {n.Kind} {n.Key} {n.Text} 방 {n.RoomId}"); }
        foreach (var fc in w.Automation.FireCases) Console.WriteLine($"  소화 {fc.RoomId} 단계 {fc.Stage} {fc.Method} — {fc.Status}");
        foreach (var r in w.Ship.Rooms.Where(r => r.Id is 0 or 4))
            Console.WriteLine($"  {r.Name} {r.Air.Pressure:0}kPa O2 {r.Air.O2:0.0} 불 {w.Fire.CountIn(r)} · 갈아냄 {r.Flushing} 진공 {r.Purging} 질식 {r.Inerting} 댐퍼 {r.VentOpen} 전기 {r.Powered} 잠금 {r.Lockdown} 붙듦 {r.ResponseHold} · 탱크 {w.Air.Reserve:0} · 문 {string.Join(",", r.Doors.Select(d => (d.Locked ? "L" : "o") + (w.Failsafe.Latched(d) ? "*" : "")))}");
    }

    // ── 8) 결정론 · 성능 ──
    private static void FsDeterminism(int seed)
    {
        Console.WriteLine("── 결정론 · 성능 ──");
        uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
        uint a = H(), b = H();
        Check("결정론 — 같은 시드 두 번 같은 지문", a == b, $"{a:x8} / {b:x8}");
        double T(bool legacy)
        {
            Durability.Legacy = legacy;
            try
            {
                var w = World.CreateDefault(seed, 0, "Cheonma");
                Run(w, SimTime.Hours(1));
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.Hours(6));
                return sw.Elapsed.TotalSeconds;
            }
            finally { Durability.Legacy = false; }
        }
        double t0 = T(true), t1 = T(false);
        Check("성능 — 30인 배 6시간이 예전보다 크게 늘지 않는다", t1 < t0 * 1.15 + 0.3, $"예전 {t0:0.00}초 · 지금 {t1:0.00}초 ({(t1 / Math.Max(0.001, t0) - 1) * 100:+0;-0}%)");
    }
}
