using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 임시 (통합7 살피기 — 커밋 전에 지운다)
public static partial class Program
{
    private static int RunDbg7(int seed, string[] args)
    {
        string which = args.SkipWhile(a => a != "--dbg7").Skip(1).FirstOrDefault() ?? "chess";
        if (which == "storm")
        {
            foreach (var key in new[] { ShipGenerator.KeyFor(12, seed), "Hanbit" })
            {
                var w = World.CreateDefault(seed, 0, key);
                Run(w, SimTime.Hours(1));
                Hazards.Apply(w, HazardKind.SolarStorm, default, -1);
                Run(w, SimTime.Hours(1));
                foreach (var r in w.Ship.Rooms.Where(r => r.Type == RoomType.Shelter || r.Name.Contains("통로")))
                    Console.WriteLine($"{key} {r.Name} rad {r.Radiation:0.00} storm {w.Ambience.StormPower:0.00} exp {w.Ambience.Exposure(r):0.00} win {w.Body.OpenWindows(r)} cosmic {w.Cosmic.Radiation(r):0.00} cells {r.Cells.Count} tags {RoomCatalog.Tags(r.Kind)}");
            }
        }
        if (which == "shel")
        {
            foreach (var t in ShipCatalog.All) { var w = World.CreateDefault(seed, 0, t.Key); Console.WriteLine($"{t.Key} {t.Name} crew {w.Crew.Count} shelter {w.Ship.KindOf(RoomType.Shelter).Count()} storage {w.Ship.KindOf(RoomType.Storage).Count()}"); }
        }
        if (which == "splice")
        {
            var w = DayOne(seed, "Mirinae");
            w.Net.Update(0f); w.Flow.Update(0.01f);
            var src = w.Net.SourceRoom(NetKind.Power)!;
            var trunk = w.Net.Links.Where(l => l.Kind == NetKind.Power && !UtilityNet.IsRing(l) && l.Door != null && (l.Door.RoomA == src || l.Door.RoomB == src) && l.Key.EndsWith(":X")).First();
            var splicer = w.Crew.First(c => c.CanAct && !c.IsChild);
            trunk.Temp = true; trunk.Integrity = float.Parse(Environment.GetEnvironmentVariable("INTEG") ?? "0.75"); trunk.SplicedBy = splicer.Id;
            w.Net.Update(0f); w.Flow.Update(0.01f);
            for (int m = 0; m < 12 * 60 && trunk.Temp; m++)
            {
                Run(w, SimTime.Minutes(1));
                if (m % 5 == 0)
                {
                    var o = w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.RepairNet && o.Circuit == trunk.Id);
                    Console.WriteLine($"{SimTime.Clock(w.Tick)} heat {w.Flow.SpliceHeat(trunk):0.00} int {trunk.Integrity:0.00} order {(o == null ? "-" : $"{o.Urgency:0.00} {o.Assignee?.Name} {o.Assignee?.Job?.Label}")} crisis {Crisis.Level(w)}");
                }
            }
            Console.WriteLine($"{SimTime.Clock(w.Tick)} temp {trunk.Temp}");
        }
        if (which == "boom")
        {
            var w = DayOne(seed, "Hanbit");
            for (int d = 0; d < 5 * 24 * 12 && w.Volatile.Blasts.Count == 0; d++) Run(w, SimTime.Minutes(5));
            foreach (var l in w.Log.Entries.Where(l => l.Tick > w.Tick - SimTime.Hours(2))) Console.WriteLine($"  L {SimTime.Clock(l.Tick)} {l.Text}");
            foreach (var b in w.Volatile.Blasts) Console.WriteLine($"{SimTime.Clock(b.Tick)} d{b.Tick / SimTime.TicksPerDay} {w.Ship.RoomAt(b.At)?.Name} {b.Power:0.00} {b.Cause}");
            foreach (var h in w.History.Events.Where(h => w.Volatile.Blasts.Count > 0 && h.Tick > w.Volatile.Blasts[0].Tick - SimTime.Hours(3) && h.Tick <= w.Volatile.Blasts[0].Tick + SimTime.Minutes(5))) Console.WriteLine($"  {SimTime.Clock(h.Tick)} {h.Text}");
        }
        if (which == "stall")
        {
            var w = DayOne(seed, "Kestrel"); w.CrewCanDie = true;
            var d = w.Ship.Doors.Where(x => !x.IsExternal && x.RoomA != null && x.RoomB != null && !x.MotorBroken).OrderBy(x => x.Id).First();
            var solo = w.Crew.Where(c => c.CanAct).OrderByDescending(c => c.SkillLevel(Skill.Engineering)).First();
            foreach (var o in w.Crew.Where(o => o != solo)) o.Away = true;
            w.Fixtures.BreakDoor(d, "구동기 모터 탐");
            w.Board.RequestScan();
            for (int m = 0; m < 14 * 60 && d.MotorBroken; m += 20)
            {
                Run(w, SimTime.Minutes(20));
                var o = w.Board.All.FirstOrDefault(o => o.Kind == WorkKind.RepairDoor && !o.Closed);
                var dist = w.Paths.Flood(solo.Cell, solo.PathProfile);
                Console.WriteLine($"{SimTime.Clock(w.Tick)} {solo.Name} {solo.ActivityLabel} · order {(o == null ? "-" : $"u{o.Urgency:0.00} asg {o.Assignee?.Name} blk {o.BlockedReason} until {(o.BlockedUntil > w.Tick ? SimTime.Clock(o.BlockedUntil) : "-")} prog {o.Progress:0.00} ap {ChoresActivity.Appeal(solo, w, o, dist, out _):0.00} av {w.Board.AvailableTo(solo).Contains(o)}")} · ev[{string.Join(", ", solo.LastEvaluations.Take(3).Select(e => $"{e.Activity?.Label} {e.Score:0.00}"))}] solos {w.Coop.Stats.Solos} holds {w.Coop.Stats.Holds}");
            }
        }
        if (which == "ration")
        {
            var w = DayOne(seed, "Hanbit");
            int crew = w.Crew.Count(c => !c.Dead);
            Scenarios.LimitStock(w, ItemKind.Meal, crew);
            Scenarios.LimitStock(w, ItemKind.Ration, 0);
            Scenarios.LimitStock(w, ItemKind.Produce, 0);
            int want = (int)(crew * FoodPolicy.MealsPerPersonDay * 2.7f) - (int)FoodPolicy.FoodStock(w);
            foreach (var box in w.Ship.Containers) if (want > 0 && box.Storage!.Accepts(ItemKind.Ration)) want -= box.Storage.Add(ItemKind.Ration, want);
            foreach (var bed in w.Ship.FurnitureOf(FurnitureType.GrowBed).Where((_, i) => i % 6 != 0).ToList()) { bed.Machine!.Crop!.Growth = 0.05f; w.Machines.Break(bed.Machine, FaultKind.Wrecked); }
            w.Automation.Install(ComputerModule.MealPlan);
            for (int h = 0; h < 20 && !w.Food.Rationing; h++)
            {
                Run(w, SimTime.Hours(1));
                var o = w.Board.All.FirstOrDefault(o => o.Kind == WorkKind.Ration && !o.Closed);
                var cook = w.Crew.FirstOrDefault(c => c.Role == CrewRole.Cook);
                Console.WriteLine($"{SimTime.Clock(w.Tick)} grow {FoodPolicy.GrowingPerDay(w):0} soon {FoodPolicy.HarvestSoon(w, 72f):0} stock {FoodPolicy.FoodStock(w):0} beds-broken {w.Ship.FurnitureOf(FurnitureType.GrowBed).Count(b => b.Machine!.Faults.Count > 0)} days {FoodPolicy.FoodDays(w):0.00} lead {w.Automation.RationLead} leads {w.Automation.RationLeads} order {(o == null ? "-" : $"{o.Urgency:0.00} {o.Assignee?.Name} blk {o.BlockedReason}")} · cook {cook?.Name} {cook?.ActivityLabel} · crisis {Crisis.Level(w)} · asks {w.Automation.Asks.Needed("ration")}");
            }
        }
        if (which == "bchess")
        {
            var w = DayOne(seed, "Hanbit");
            var players = w.Crew.Where(c => !c.IsChild).Take(4).ToList();
            foreach (var c in players)
            {
                c.Hobbies.Clear(); c.Hobbies.Add(Hobby.Chess);
                if (!w.Belongings.Of(c).Any(b => b.Kind == BelongingKind.ChessSet)) w.Belongings.Seed2(c, BelongingKind.ChessSet);
            }
            for (int h = 0; h < 36; h += 3)
            {
                Run(w, SimTime.Hours(3));
                Console.WriteLine($"{SimTime.Clock(w.Tick)} games: " + string.Join(" | ", w.Belongings.Games.Select(g => $"#{g.Id} {g.A}v{g.B} {g.Moves:0}/{g.Target:0} done {g.Done} scene {g.Scene}")));
                foreach (var sc in w.Scenes.Scenes.Where(x => x.Kind == SceneKind.Chess).TakeLast(3)) Console.WriteLine($"   scene {sc.Id} {sc.Stage} open {sc.Open} host {sc.Host} other {sc.Other} · {string.Join(" / ", sc.Trail.TakeLast(4))}");
            }
        }
        if (which == "chess")
        {
            var w = DayOne(seed, "Hanbit");
            var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            var a = adults[0]; var b = adults[1]; var cof = adults[2];
            foreach (var c in new[] { a, b, cof }) ScFree(w, c);
            if (!a.Hobbies.Contains(Hobby.Chess)) a.Hobbies.Add(Hobby.Chess);
            if (w.Belongings.ItemFor(a, Hobby.Chess) == null) w.Belongings.Seed2(a, BelongingKind.ChessSet);
            var s = w.Scenes.OpenChess(a, b)!;
            if (s.Other < 0) { s.Other = b.Id; s.Invited.Add(b.Id); s.Declined.Clear(); }
            var it = w.Belongings.ItemFor(a, Hobby.Chess);
            Console.WriteLine($"set {it?.Id} at {it?.At} · room {s.RoomId} table {s.Table} · item {s.Item}");
            for (int i = 0; i < 16; i++)
            {
                Run(w, SimTime.Minutes(10));
                Console.WriteLine($"{SimTime.Clock(w.Tick)} {s.Stage} game {s.Game} here[{string.Join(",", s.Here)}] · A {a.Name} {a.Cell} room {a.Room?.Id}: {a.ActivityLabel} [{a.Job?.Activity?.GetType().Name}] · B {b.Cell}: {b.ActivityLabel} · crisis {Crisis.Acting(w)}");
            }
        }
        if (which == "site")
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            var f = w.Ship.Furniture.Where(x => x.Machine is Machine m && !x.Stowed && m.Faults.Count == 0 && m.Spec.ServiceHours >= 0.7f && x.Room.Type != RoomType.Corridor)
                .OrderBy(x => x.Id).Skip(1).First();
            var o = SpMaintain(w, f)!;
            float h0 = SimTime.HourOfDay(w.Tick);
            var c = w.Crew.Where(x => x.CanAct && !x.IsChild && SimTime.InWindow(h0, x.Schedule.WorkStart, x.Schedule.WorkLength) && SimTime.InWindow(h0 + 3f, x.Schedule.WorkStart, x.Schedule.WorkLength))
                        .OrderByDescending(x => x.SkillLevel(f.Machine!.Spec.Skill)).ThenBy(x => x.Id).FirstOrDefault() ?? SpWorker(w, f.Machine!.Spec.Skill);
            foreach (var x in w.Crew) if (x.Job?.Order == o) x.EndJob(w, ToilStatus.Interrupted);
            var job = SpChore(w, c, o)!;
            Console.WriteLine($"job {job.Label} toils: {string.Join(",", job.Toils.Select(t => t.GetType().Name))} · order {o.Kind} robot {o.Robot} ext {o.External}");
            Force(w, c, job, SimTime.Hours(4));
            for (int k = 0; k < 40 && o.Progress < 0.15f; k++)
            {
                Run(w, SimTime.Minutes(3));
                Console.WriteLine($"{SimTime.Clock(w.Tick)} {c.Name} {c.Cell} room {c.Room?.Name} {c.ActivityLabel} [{c.Job?.Current?.GetType().Name}] same {c.Job == job} prog {o.Progress:0.00} sites {w.Coop.Sites.Count(s => s.OrderId == o.Id)} assignee {o.Assignee?.Name}");
            }
        }
        if (which == "pair")
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            SpNoHoist(w);
            var fridge = w.Ship.FurnitureOf(FurnitureType.Fridge).OrderBy(x => x.Id).First();
            var shelf = w.Ship.FurnitureOf(FurnitureType.Shelf).Where(x => x.Storage!.Accepts(ItemKind.Motor)).OrderBy(x => x.Id).First();
            shelf.Storage!.Add(ItemKind.Motor, 2);
            w.Machines.Break(fridge.Machine!, FaultKind.CompressorFail);
            w.Board.RequestScan();
            Run(w, SimTime.Minutes(1));
            var o = w.Board.Open.First(x => x.Kind == WorkKind.Repair && x.Target.Furniture == fridge);
            var c = SpWorker(w, Skill.Mechanics);
            Force(w, c, SpChore(w, c, o)!, SimTime.Hours(4));
            PairCall? call = null;
            for (int k = 0; k < 200 && !o.Closed; k++)
            {
                Run(w, SimTime.Minutes(1));
                call ??= w.Coop.Calls.FirstOrDefault(x => x.Caller == c.Id);
                var h = call == null || call.Helper < 0 ? null : w.Crew.First(x => x.Id == call.Helper);
                if (k % 6 == 0) Console.WriteLine($"{SimTime.Clock(w.Tick)} {c.Name} {c.Cell} {c.ActivityLabel} [{c.Job?.Current?.GetType().Name}] · call {call?.Arrived} {call?.Done} {call?.Outcome} calls {string.Join(";", w.Coop.Calls.Where(x => x.OrderId == o.Id).Select(x => $"{x.Id}:c{x.Caller}:{x.Outcome}"))} job {c.Job?.GetHashCode()} prog {o.Progress:0.00} oid {o.Id} asg {o.Assignee?.Name} blk {o.BlockedReason} · H {h?.Name} {h?.Cell} {h?.ActivityLabel} [{h?.Job?.Activity?.GetType().Name}/{h?.Job?.Current?.GetType().Name}] spot {call?.Spot}");
            }
        }
        if (which == "nohelp")
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            SpNoHoist(w);
            w.Coop.NoHelpers = true;
            var shelf = w.Ship.FurnitureOf(FurnitureType.Shelf).Where(x => x.Storage!.Accepts(ItemKind.Motor)).OrderBy(x => x.Id).First();
            shelf.Storage!.Add(ItemKind.Motor, 2); shelf.Storage.Add(ItemKind.Pump, 2);
            var fridge = w.Ship.FurnitureOf(FurnitureType.Fridge).OrderBy(x => x.Id).First();
            var pump = w.Ship.Furniture.Where(x => x.Machine != null && !x.Stowed && x.Type is FurnitureType.WaterRecycler or FurnitureType.HeatExchanger).OrderBy(x => x.Id).First();
            w.Machines.Break(fridge.Machine!, FaultKind.CompressorFail);
            w.Machines.Break(pump.Machine!, FaultKind.PumpSeized);
            w.Board.RequestScan();
            Run(w, SimTime.Minutes(1));
            var o1 = w.Board.Open.First(x => x.Kind == WorkKind.Repair && x.Target.Furniture == fridge);
            var o2 = w.Board.Open.First(x => x.Kind == WorkKind.Repair && x.Target.Furniture == pump);
            var a = SpWorker(w, Skill.Mechanics);
            var b = SpWorker(w, Skill.Mechanics, a);
            a.Habits.Remove(Habit.Hasty); a.Habits.Add(Habit.Methodical);
            var ja = SpChore(w, a, o1)!; var jb = SpChore(w, b, o2)!;
            Console.WriteLine($"A {a.Name} {string.Join(",", ja.Toils.Select(t => t.GetType().Name))} · B {b.Name} {string.Join(",", jb.Toils.Select(t => t.GetType().Name))} · fault parts {fridge.Machine!.Faults.FirstOrDefault()?.Spec.Part} {pump.Machine!.Faults.FirstOrDefault()?.Spec.Part}");
            Force(w, a, ja, SimTime.Hours(3));
            Force(w, b, jb, SimTime.Hours(3));
            for (int k = 0; k < 50; k++)
            {
                Run(w, SimTime.Minutes(1));
                Console.WriteLine($"{SimTime.Clock(w.Tick)} A {a.Cell} {a.ActivityLabel} [{a.Job?.Current?.GetType().Name}] carry {a.Carrying?.Kind} · B {b.Cell} {b.ActivityLabel} [{b.Job?.Current?.GetType().Name}] carry {b.Carrying?.Kind} · calls {w.Coop.Calls.Count} · o1 {o1.Progress:0.00} o2 {o2.Progress:0.00}");
            }
        }
        if (which == "craft")
        {
            var w = DayOne(seed, "Mirinae");
            var spec = Props.All.First(p => p.Source == PropSource.Craft && p.Material is ItemKind m && w.Ship.CountStored(m) > 0 && !p.Kid);
            var maker = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderBy(c => c.Id).Skip(3).First();
            ScFree(w, maker);
            w.Scenes.Craft(maker, spec);
            var s = w.Scenes.Scenes.Last();
            Console.WriteLine($"spec {spec.Name} mat {spec.Material} room {s.RoomId} {s.Kind} {s.Stage} open {s.Open}");
            for (int i = 0; i < 40; i++)
            {
                Run(w, SimTime.Minutes(5));
                Console.WriteLine($"{SimTime.Clock(w.Tick)} {s.Stage} {s.Progress:0.00} open {s.Open} · {maker.Name} {maker.Cell} {maker.Room?.Name}: {maker.ActivityLabel} [{maker.Job?.Activity?.GetType().Name}/{maker.Job?.Current?.GetType().Name}] · {s.Trail.LastOrDefault()}");
            }
        }
        if (which == "size")
        {
            string ship = args.SkipWhile(a => a != "--dbg7").Skip(2).FirstOrDefault() ?? "Busitdol";
            var w = World.CreateDefault(seed, 0, ship);
            string? who = args.SkipWhile(a => a != "--who").Skip(1).FirstOrDefault();
            for (int h = 0; h < 24 * 6; h++)
            {
                Run(w, SimTime.Minutes(10));
                if (who != null)
                {
                    var p = w.Crew.First(c => c.Name == who);
                    var ev = p.LastEvaluations.FirstOrDefault(e => e.Activity is EatActivity); Console.WriteLine($"{SimTime.Clock(w.Tick)} {p.Needs.Food:0.00} urg {p.Job?.Urgent} m {p.Job?.InterruptMargin:0.00} eat {ev.Score:0.00} ({ev.Reason}) top {p.LastEvaluations.FirstOrDefault().Activity?.Label} {p.LastEvaluations.FirstOrDefault().Score:0.00} {p.Cell} {p.Room?.Name}: {p.ActivityLabel} [{p.Job?.Activity?.GetType().Name}/{p.Job?.Current?.GetType().Name}] meals {p.Stats.Meals} · 식사 {w.Ship.CountStored(ItemKind.Meal)} 채소 {w.Ship.CountStored(ItemKind.Produce)} 비상 {w.Ship.CountStored(ItemKind.Ration)}");
                }
                else if (h % 6 == 5)
                {
                    var lo = w.Crew.Where(c => !c.Dead).OrderBy(c => c.Needs.Food).Take(2);
                    Console.WriteLine($"{SimTime.Clock(w.Tick)} " + string.Join(" | ", lo.Select(c => $"{c.Name} {c.Needs.Food:0.00} {c.Room?.Name}: {c.ActivityLabel} meals {c.Stats.Meals}")));
                }
            }
        }
        if (which == "find")
        {
            var w = World.CreateDefault(seed, 0, "Busitdol");
            Run(w, SimTime.Hours(2));
            var o = w.Origin;
            var note = o.Finds.Where(f => !f.Found && f.Kind == FindKind.Note && f.Machine >= 0).OrderBy(f => f.Id).FirstOrDefault()!;
            var fu = w.Ship.Furniture[note.Machine];
            var m = fu.Machine!;
            w.Machines.Break(m);
            var nr = w.Ship.Rooms[note.RoomId]; Console.WriteLine($"off {nr.OffLimits} aband {nr.Abandoned} det {nr.Detached} sealed {note.Sealed} stowed {fu.Stowed} powered {m.Powered} stopped {m.Stopped} · note at {note.At} room {nr.Name} machine {fu.Label} {fu.Cells[0]} faults {string.Join(",", m.Faults.Select(x => x.Kind))}");
            for (int i = 0; i < 72 && !note.Found; i++)
            {
                Run(w, SimTime.Minutes(10));
                var orders = w.Board.Open.Where(x => x.Target.Furniture == fu).ToList();
                var workers = w.Crew.Where(c => c.Job?.Order?.Target.Furniture == fu).Select(c => $"{c.Name} {c.Cell} {c.Pose}");
                Console.WriteLine($"{SimTime.Clock(w.Tick)} faults {m.Faults.Count} svc {m.ServiceCount} orders {string.Join(",", orders.Select(x => $"{x.Kind} as {x.Assignee?.Name} robot {x.Robot?.Name} blk {x.BlockedReason}"))} · {string.Join(";", workers)}");
            }
        }
        if (which == "spof")
        {
            var w = World.CreateDefault(seed, 0, "Saeteo");
            for (int h = 0; h < 6; h++)
            {
                Run(w, SimTime.Minutes(20));
                Console.WriteLine($"{SimTime.Clock(w.Tick)} present {w.Automation.Present} online {w.Automation.MainOnline} rings {w.Net.Rings.Count} src {w.Net.SourceRoom(NetKind.Power)?.Name} said {w.Automation.Book.Acts.Any(x => x.Key == "origin:spof")} designer {w.Origin.Info?.Designer}");
            }
        }
        if (which == "door")
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            var a = w.Automation;
            a.Install(ComputerModule.Access);
            var b = w.Body;
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Quarters or RoomType.Lounge && r.DataLinked && r.Doors.Any(d => !d.IsExternal && b.DoorOf(d) != null)).OrderBy(r => r.Id).First();
            var door = room.Doors.First(d => !d.IsExternal && b.DoorOf(d) != null);
            var db = b.DoorOf(door)!;
            db.SensorBroken = true;
            if (b.Doors.FirstOrDefault(x => x.Inner == room.Id) is DoorBody inn) inn.SensorBroken = true;
            Run(w, SimTime.Minutes(6));
            var guest = w.Crew.Where(c => c.CanAct && !c.IsChild).OrderBy(c => c.Id).First();
            Put(w, guest, room);
            guest.NextThinkTick = w.Tick + SimTime.Hours(1);
            for (int i = 0; i < 12; i++)
            {
                Run(w, 15);
                var bel = a.Belief.Of(room);
                Console.WriteLine($"{SimTime.Clock(w.Tick)} {room.Name} fault {bel.Fault} people {bel.People} actual {BeliefModel.Actual(w, room)} blinds {a.DoorBlinds} doors {room.Doors.Count} inner {b.Doors.Count(x => x.Inner == room.Id)} broken {b.Doors.Count(x => x.SensorBroken)}");
            }
        }
        if (which == "crowd")
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 15f);
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Storage or RoomType.Workshop or RoomType.Lounge or RoomType.Hydroponics && r.Doors.Any(d => !d.IsExternal && (d.RoomA?.Type == RoomType.Corridor || d.RoomB?.Type == RoomType.Corridor)))
                .OrderBy(r => r.Id).First();
            float hr = SimTime.HourOfDay(w.Tick);
            bool Off(CrewMember c) => !SimTime.InWindow(hr, c.Schedule.WorkStart, c.Schedule.WorkLength) && !SimTime.InWindow(hr, c.Schedule.SleepStart, c.Schedule.SleepLength);
            var curious = w.Crew.Where(c => c.CanAct && !c.IsChild).OrderByDescending(c => Off(c)).ThenByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).Take(5).ToList();
            var corridor = room.Doors.Select(d => d.RoomA == room ? d.RoomB : d.RoomA).First(r => r?.Type == RoomType.Corridor)!;
            foreach (var c in curious)
            {
                c.Habits.Remove(Habit.Loner); c.Habits.Add(Habit.Gazer); c.Habits.Add(Habit.Talker);
                ScFree(w, c);
                Teleport(w, c, corridor.Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => (x.Center - room.Center).LengthSquared()).Skip(3 + curious.IndexOf(c)).First());
                c.Needs.Food = 0.95f;
                Force(w, c, new Job(null, "시험: 쉬는 중", new Toil[] { new WaitToil(SimTime.Minutes(2), Pose.Standing) }), 1);
            }
            w.Policies.Set("inertfire", 0, "시험"); w.Policies.Set("vacuumfire", 0, "시험");
            foreach (var fc in room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => (x.Center - room.Center).LengthSquared()).Take(3)) Incidents.Fire(w, fc);
            var cs = w.Coop.Crowds;
            Console.WriteLine($"room {room.Name} policies inert {w.Policies["inertfire"]} data {room.DataLinked}");
            for (int k = 0; k < 40; k++)
            {
                Run(w, SimTime.Minutes(1));
                var sc = cs.Scenes.LastOrDefault();
                var door = sc?.Spots.FirstOrDefault().Center ?? room.Center;
                var resp = w.Crew.Where(c => c.Job is Job j && (j.Urgent || j.Order != null) && (j.TargetRoom?.Id == room.Id || j.Order?.Target.CurrentRoom == room)).Select(c => $"{c.Name}@{(c.Position - door).Length():0}:{c.Job!.Label}:{c.Job.TargetRoom?.Name}");
                Console.WriteLine($"{SimTime.Clock(w.Tick)} known {w.Fire.IsKnown(room)} fire {w.Fire.CountIn(room)} watchers {sc?.Watchers.Count} sq {cs.Stats.Squeezes} sh {cs.Stats.Shouts} resp [{string.Join(",", resp)}] · cur {string.Join(" | ", curious.Select(c => $"{c.Name} {c.ActivityLabel}"))}");
            }
        }
        if (which == "cup")
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 16.5f);
            var info = w.Info;
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            var next = IfNextRoom(w, mess)!;
            var adults = IfAdults(w).OrderBy(c => IfFree(c, 16.5f) && IfFree(c, 18.5f) ? 0 : IfFree(c, 16.5f) ? 1 : 2).ThenBy(c => c.Id).ToList();
            bool Mild(CrewMember c) => Life.Has(c, Habit.Patient) || Life.Has(c, Habit.Optimist) || Life.Has(c, Habit.Generous);
            var owner = adults.First(c => !Mild(c) && w.Belongings.All.Any(b => b.Owner == c.Id && b.Kind == BelongingKind.Mug && b.Usable));
            var finder = adults.First(c => c != owner && !Life.Has(c, Habit.Loner) && c.Traits.Sociability >= 0.25f);
            var witness = adults.First(c => c != owner && c != finder && !Life.Has(c, Habit.Talker) && !Life.Has(c, Habit.Joker));
            owner.ChangeAffinity(finder, -0.3f - owner.AffinityTo(finder));
            var keep = new[] { owner, finder, witness };
            info.Intents.RemoveAll(i => keep.Any(c => c.Id == i.Crew));
            SpQuiet(w, keep, SimTime.Hours(9));
            var table = mess.Furniture.First(f => f.Type == FurnitureType.Table);
            var seat = mess.Furniture.Where(f => f.Type == FurnitureType.Seat && f.UseSpots.Count > 0).OrderBy(f => (f.Center - table.Center).LengthSquared()).ThenBy(f => f.Id).First();
            var far = mess.Furniture.Where(f => f.Type == FurnitureType.Seat && f.UseSpots.Count > 0 && f != seat).OrderByDescending(f => (f.Center - table.Center).LengthSquared()).ThenBy(f => f.Id).First();
            Teleport(w, witness, far.UseSpots[0]);
            Force(w, witness, new Job(null, "시험: 구석에서 책", new Toil[] { new WaitToil(SimTime.Minutes(30), Pose.Sitting, table.Center) }), SimTime.Minutes(30));
            Teleport(w, finder, IfFloor(w, next));
            IfIdle(w, finder);
            var home = owner.Bed?.Room ?? w.Ship.RoomsOf(RoomType.Quarters).First();
            Teleport(w, owner, IfFloor(w, home));
            Force(w, owner, new Job(null, "시험: 침실에서 쉼", new Toil[] { new WaitToil(SimTime.Minutes(80), Pose.Sitting) }) { InterruptMargin = 9f }, SimTime.Minutes(80));
            Run(w, 2);
            var cup = w.Belongings.All.First(b => b.Owner == owner.Id && b.Kind == BelongingKind.Mug && b.Usable);
            foreach (var tc in info.OnTable.Values.ToList()) { info.OnTable.Remove(tc.Cup); if (w.Belongings.Get(tc.Cup) is Belonging ob) ob.At = null; }
            info.PutOnTable(owner, cup, table.Cells[0], seat.UseSpots[0], left: true);
            info.Jolt(mess, 0.9f, "자세 제어 분사", force: true);
            var k = info.Cases.Last();
            for (int i = 0; i < 300 && k.Accused < 0; i++)
            {
                Run(w, SimTime.Minutes(1));
                if (i % 10 == 0) Console.WriteLine($"{SimTime.Clock(w.Tick)} finder {k.Finder} suspect {k.Suspect} acc {k.Accused} · owner {owner.Name} {owner.Room?.Name}: {owner.ActivityLabel} [{owner.Job?.Activity?.GetType().Name}] ev[{string.Join(", ", owner.LastEvaluations.Take(3).Select(e => $"{e.Activity?.Label} {e.Score:0.00}"))}] · finder {finder.Name} {finder.Room?.Name}: {finder.ActivityLabel} · intents {string.Join(",", info.Intents.Where(x => x.Crew == owner.Id).Select(x => x.ToString()))}");
            }
        }
        if (which == "bearing")
        {
            var w = BrainDay(seed, "Hanbit");
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
            var part = ItemKind.Bearing;
            foreach (var f in w.Ship.Containers) { int n = f.Storage!.Count(part); if (n > 0) f.Storage.Take(part, n); }
            var shelf = w.Ship.Containers.Where(f => f.Type == FurnitureType.Shelf && f.Storage!.Accepts(part)).OrderBy(f => f.Id).First();
            var worker = w.Crew.Where(c => c.CanAct && c.IsAwake && !c.IsChild && c.SkillLevel(Skill.Mechanics) >= 0.3f).OrderByDescending(c => c.SkillLevel(Skill.Mechanics)).First();
            BrainTrait(worker, "Diligence", 0.95f);
            w.Brain2.Beliefs.Learn(worker, Topic.Item, (int)part, shelf.Id, BeliefSource.Seen, 1f, -1, 2);
            w.Machines.Break(pump.Machine!, FaultKind.BearingWear);
            Run(w, SimTime.Minutes(1));
            var order = w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Repair && o.Target.Furniture == pump && o.Fault == FaultKind.BearingWear);
            CrewPlan? plan = order != null ? w.Brain2.Plans.StartFix(worker, order) : null;
            worker.NextThinkTick = w.Tick + 1;
            for (int m = 0; m < 50 && plan is { Done: false }; m++)
            {
                Run(w, SimTime.Minutes(10));
                var plEval = worker.LastEvaluations.FirstOrDefault(e => e.Activity is PlanActivity);
                Console.WriteLine($"{SimTime.Clock(w.Tick)} step {plan.Step?.Kind}/{plan.Step?.State} · {worker.Name} {worker.Room?.Name}: {worker.ActivityLabel} [{worker.Job?.Activity?.GetType().Name}] plan {plEval.Score:0.00} ({plEval.Reason}) top {worker.LastEvaluations.FirstOrDefault().Activity?.Label} {worker.LastEvaluations.FirstOrDefault().Score:0.00} · order asg {order?.Assignee?.Name} closed {order?.Closed}");
            }
            Console.WriteLine(string.Join(" / ", plan?.Trail ?? new List<string>()));
        }
        if (which == "rehab")
        {
            var w = DayOne(seed, "Mirinae");
            w.Growth.NoFirstAid = true; w.Ailments.Disabled = true;
            foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null)) f.Storage!.Take(ItemKind.MedKit, 999);
            var c = w.Crew.First(x => x.Role == CrewRole.Technician);
            NeedsSystem.AddInjury(c.Vitals, 0.7f, "감압");
            c.Vitals.Health = 0.6f;
            c.Vitals.TreatedTick = w.Tick;
            for (int i = 0; i < 24; i++)
            {
                Run(w, SimTime.Hours(3));
                var ro = w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Rehab && o.Circuit == c.Id);
                var d = w.Paths.Flood(c.Cell, c.PathProfile);
                Console.WriteLine($"{SimTime.Clock(w.Tick)} inj {c.Vitals.Injury:0.00} hp {c.Vitals.Health:0.00} bed {c.CareBed != null} · order {(ro == null ? "-" : $"{ro.Urgency} asg {ro.Assignee?.Name} blk {ro.BlockedReason} ap {ChoresActivity.Appeal(c, w, ro, d, out _):0.00} av {w.Board.AvailableTo(c).Contains(ro)}")} · {c.ActivityLabel} [{c.Job?.Activity?.GetType().Name}] ev[{string.Join(", ", c.LastEvaluations.Take(3).Select(e => $"{e.Activity?.Label} {e.Score:0.00}"))}] rehab {c.Stats.RehabSessions}");
            }
        }
        if (which == "box")
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            var shelf = w.Ship.FurnitureOf(FurnitureType.Shelf).Where(x => x.Storage!.Accepts(ItemKind.Motor)).OrderBy(x => x.Id).First();
            var inv = shelf.Storage!;
            foreach (var k in new[] { ItemKind.Filter, ItemKind.Cable, ItemKind.Fuse, ItemKind.Lubricant, ItemKind.Sealant })
                if (inv.Accepts(k) && inv.Total < inv.Capacity * 0.7f) inv.Add(k, Math.Max(1, ((int)(inv.Capacity * 0.72f) - inv.Total) / 5));
            while (inv.Free < 3 && inv.Contents.FirstOrDefault(x => x.kind != ItemKind.Motor) is { count: > 0 } x0) inv.Take(x0.kind, 1);
            inv.Add(ItemKind.Motor, 1);
            var c = SpWorker(w, Skill.Mechanics);
            Force(w, c, new Job(null, "시험: 모터 꺼내기", new Toil[] { new GotoToil(shelf.UseSpots[0]), new TakeToil(shelf, ItemKind.Motor, 1), new WaitToil(SimTime.Minutes(5), Pose.Standing) }), SimTime.Hours(1));
            for (int k = 0; k < 40 && c.Carrying?.Kind != ItemKind.Motor; k++) Run(w, SimTime.Minutes(1));
            c.Carrying = null;
            inv.Add(ItemKind.Motor, 1);
            Force(w, c, new Job(null, "시험: 급히 모터", new Toil[] { new GotoToil(shelf.UseSpots[0]), new TakeToil(shelf, ItemKind.Motor, 1), new WaitToil(SimTime.Minutes(5), Pose.Standing) }) { Urgent = true }, SimTime.Minutes(20));
            for (int k = 0; k < 20 && c.Carrying?.Kind != ItemKind.Motor; k++) Run(w, SimTime.Minutes(1));
            c.Carrying = null;
            var tidy = Brain.Activities.OfType<SpaceTidyActivity>().First();
            for (int k = 0; k < 24; k++)
            {
                Run(w, SimTime.Minutes(10));
                var b = w.Coop.Boxes.FirstOrDefault(b => b.ShelfId == shelf.Id);
                if (b == null) { Console.WriteLine($"{SimTime.Clock(w.Tick)} tidied"); break; }
                var near = w.Crew.Where(x => !x.Dead && x.CanAct).Select(x => (x, d: w.Paths.Flood(x.Cell, x.PathProfile).Get(b.Item.At))).OrderBy(t => t.d < 0 ? 99999 : t.d).Take(3);
                Console.WriteLine($"{SimTime.Clock(w.Tick)} box at {b.Item.At} left {b.Left} by {b.By} tidyBy {b.TidyBy} · " + string.Join(" | ", near.Select(t => { var (sc, why) = tidy.Score(t.x, w, w.Paths.Flood(t.x.Cell, t.x.PathProfile)); return $"{t.x.Name} d{t.d} sc {sc:0.00} {t.x.ActivityLabel} top {t.x.LastEvaluations.FirstOrDefault().Score:0.00}"; })));
            }
        }
        if (which == "towel")
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            RunUntilHour(w, 10f);
            var m = w.Matter;
            var room = MRoom(w, r => r.DataLinked && r.Kind is RoomType.Laundry or RoomType.Lounge or RoomType.Mess or RoomType.Gym or RoomType.Galley);
            var hub = MHub(w, room)!.Value;
            MHeater(w, room, hub);
            var towel = m.Add(ArticleKind.Towel, hub + new Cell(0, 1), "시험");
            towel.Water = towel.Spec.Capacity;
            var at0 = towel.At; bool fl = false; int cb = -1;
            for (int t = 0; t < SimTime.Hours(3) && m.Stats.HeededWarns == 0; t++)
            {
                w.Step();
                if (towel.Flagged != fl) { fl = towel.Flagged; Console.WriteLine($"{SimTime.Clock(w.Tick)} flagged {fl}"); }
                if (towel.CarriedBy != cb) { cb = towel.CarriedBy; var cc = w.Crew.FirstOrDefault(x => x.Id == cb); Console.WriteLine($"{SimTime.Clock(w.Tick)} carried by {cc?.Name} job {cc?.Job?.Label} [{cc?.Job?.Activity?.GetType().Name}]"); }
                if (towel.At != at0 || t % 1500 == 0) { Console.WriteLine($"{SimTime.Clock(w.Tick)} towel at {towel.At} carried {towel.CarriedBy} claimed {towel.ClaimedBy} wet {towel.WetFrac:0.00} char {towel.Char:0.00} hung {towel.Hung} vib {room.Vibration:0.00} rattled {m.Stats.Rattled} pushed {m.Stats.Pushed} slid {m.Stats.Slid} · heeded {m.Stats.HeededWarns} moved {m.Stats.MovedFromHeat}"); at0 = towel.At; }
            }
        }
        if (which == "cpr")
        {
            var w = DayOne(seed, "Hanbit");
            w.CrewCanDie = true;
            var lab = w.Ship.Rooms.Where(r => r.Type != RoomType.Corridor && !r.Detached && r.Cells.Count(w.Ship.IsOpenFloor) >= 4)
                .OrderBy(r => w.Crew.Count(c => c.Room == r)).ThenByDescending(r => r.Cells.Count).ThenBy(r => r.Id).First();
            var spots = lab.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
            var v = w.Crew.Where(c => c.CanAct && !c.IsChild && c.Role != CrewRole.Medic).OrderBy(c => c.Id).First();
            Stay(w, v, spots[0], Pose.Working);
            var p = w.Crew.Where(c => c != v && c.CanAct && !c.IsChild).OrderByDescending(c => c.Role == CrewRole.Medic).ThenBy(c => c.Id).First();
            Stay(w, p, spots.OrderBy(c => Math.Abs(c.X - spots[0].X) + Math.Abs(c.Y - spots[0].Y)).Skip(1).First(), Pose.Working);
            NeedsSystem.AddInjury(v.Vitals, 0.3f, "축전기 방전 감전");
            w.Casualty.Inflict(v, TraumaKind.Arrest, 0.6f, "축전기 방전 감전");
            Console.WriteLine($"lab {lab.Name} v {v.Name} p {p.Name} {p.Role} food {p.Needs.Food:0.00}");
            for (int i = 0; i < 20 && !v.Dead; i++)
            {
                Run(w, SimTime.Minutes(1));
                Console.WriteLine($"{SimTime.Clock(w.Tick)} v hp {v.Vitals.Health:0.00} dead {v.Dead} · p {p.Cell} {p.Room?.Name}: {p.ActivityLabel} [{p.Job?.Activity?.GetType().Name}] dist {(p.Position - v.Position).Length():0.0} ev[{string.Join(", ", p.LastEvaluations.Take(3).Select(e => $"{e.Activity?.Label} {e.Score:0.00}"))}] revived {w.Casualty.Revived}");
            }
        }
        if (which == "fed")
        {
            string ship = args.SkipWhile(a => a != "--dbg7").Skip(2).FirstOrDefault() ?? "Eunha";
            var w = DayOne(seed, ship);
            var galley = w.Ship.RoomsOf(RoomType.Galley).First();
            if (!args.Contains("--nofire")) w.Fire.Ignite(galley.Cells.First(w.Ship.IsOpenFloor), 0.6f);
            string? who = args.SkipWhile(a => a != "--who").Skip(1).FirstOrDefault();
            if (who != null)
            {
                var p = w.Crew.First(c => c.Name == who);
                long from = w.Tick + SimTime.Hours(float.Parse(args.SkipWhile(a => a != "--from").Skip(1).First()));
                Run(w, (int)(from - w.Tick));
                for (int i = 0; i < 60; i++)
                {
                    Run(w, SimTime.Minutes(2));
                    Console.WriteLine($"{SimTime.Clock(w.Tick)} {p.Needs.Food:0.000} rest {p.Needs.Rest:0.00} ev[{string.Join(", ", p.LastEvaluations.Take(3).Select(e => $"{e.Activity?.Label} {e.Score:0.00} ({e.Reason})"))}] {p.Cell} {p.Room?.Name}: {p.ActivityLabel} [{p.Job?.Activity?.GetType().Name}/{p.Job?.Current?.GetType().Name}] meals {p.Stats.Meals} awake {p.IsAwake}");
                }
                return 0;
            }
            {
                var un = w.Ship.Machines.Where(m => !m.Powered).ToList();
                Console.WriteLine($"unpowered {un.Count}/{w.Ship.Machines.Count()} · parked {un.Count(m => m.Parked)} feed {un.Count(m => m.Feed < 0.3f)} cut {un.Count(m => m.Body.Room.PowerCut)} brk {un.Count(m => m.Body.Room.BreakerOff)} link {un.Count(m => !m.Body.Room.PowerLinked)} aband {un.Count(m => m.Body.Room.Abandoned)} fed {string.Join("", w.Power.CircuitFed.Select(x => x ? 1 : 0))} · reactor {w.Power.ReactorOnline} manual {string.Join("", w.Power.ManualOff.Select(x => x ? 1 : 0))} live {string.Join("", w.Power.CircuitLive.Select(x => x ? 1 : 0))} bat% {w.Power.BatteryPercent:0.00} low {w.Power.LowPowerMode} ramp {w.Power.ReactorRamp:0.00} orders {string.Join(",", w.Board.Open.Where(o => o.Kind is WorkKind.RestoreCircuit or WorkKind.ShedLoad).Select(o => o.Kind + ":" + o.Circuit + ":" + o.Assignee?.Name + ":" + o.BlockedReason))}");
                foreach (var g in un.GroupBy(m => m.Body.Room.Name).Take(12)) Console.WriteLine($"   {g.Key}: {string.Join(",", g.Select(m => m.Name))}");
            }
            for (int i = 0; i < 24; i++)
            {
                Run(w, SimTime.Minutes(30));
                var lo = w.Crew.Where(c => !c.Dead).OrderBy(c => c.Needs.Food).Take(3);
                float powered = w.Ship.Machines.Count(m => m.Powered) / (float)Math.Max(1, w.Ship.Machines.Count());
                if (args.Contains("--orders") && w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.RestoreCircuit) is WorkOrder ro)
                {
                    var dist0 = w.Paths.Flood(w.Crew[0].Cell, w.Crew[0].PathProfile);
                    Console.WriteLine($"   restore u {ro.Urgency} robot {ro.Robot?.Name} drone {ro.Drone} blk {ro.BlockedUntil > w.Tick} cleared {Council.Cleared(ro)} min {ro.MinSkill} · " + string.Join(" | ", w.Crew.Where(c => !c.Dead).Select(c => { var d = w.Paths.Flood(c.Cell, c.PathProfile); return $"{c.Name} av {w.Board.AvailableTo(c).Contains(ro)} ap {ChoresActivity.Appeal(c, w, ro, d, out _):0.00} aware {w.Minds.Aware(c, ro)}"; })));
                }
                var tiredest = w.Crew.Where(c => !c.Dead).OrderBy(c => c.Needs.Rest).First();
                Console.WriteLine($"{SimTime.Clock(w.Tick)} pow {powered:0.00} man {string.Join("", w.Power.ManualOff.Select(x => x ? 1 : 0))} bat% {w.Power.BatteryPercent:0.00} ord {string.Join(",", w.Board.All.Where(o => o.Kind is WorkKind.RestoreCircuit or WorkKind.ShedLoad).Select(o => o.Kind + ":" + o.Circuit + ":" + o.Assignee?.Name + ":" + o.BlockedReason + ":" + o.Closed))} bat {w.Power.BatteryCharge:0} trips {string.Join(",", w.Ship.FurnitureOf(FurnitureType.PowerPanel).SelectMany(f => f.Machine!.Faults).Select(f => f.Kind + ":" + f.Circuit))} rest {tiredest.Name} {tiredest.Needs.Rest:0.00} {tiredest.ActivityLabel} · fire {w.Fire.Count} crisis {Crisis.Acting(w)} food {w.Ship.CountStored(ItemKind.Meal)} · " + string.Join(" | ", lo.Select(c => $"{c.Name} {c.Needs.Food:0.00} {c.Room?.Name}: {c.ActivityLabel} [{c.Job?.Activity?.GetType().Name}] meals {c.Stats.Meals}")));
            }
        }
        return 0;
    }
}
