using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 통합 회귀 진단 (INTEG_ONLY=probe PROBE=…): 실패한 장면을 다시 세우고 무엇이 달라졌는지 찍는다.
public static partial class Program
{
    private static void IgProbe2(int seed, string what)
    {
        if (what.Contains("pharm"))
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            var b = w.Body;
            var pharm = w.Ship.LiveRooms.Where(r => r.Kind == RoomType.Storage && r.Doors.Count >= 1 && r.Cells.Count >= 4).OrderBy(r => r.Doors.Count).ThenBy(r => r.Id).First();
            b.SetZone(pharm, AccessZone.Medicine, LockKind.Card);
            var p = w.Crew.Where(c => c.CanAct && !c.IsChild && c.Role != CrewRole.Medic && c.Id != w.Command.CaptainId && c.Room != pharm && !c.Outside)
                .OrderBy(c => (c.Position - pharm.Center).LengthSquared()).First();
            Console.WriteLine($"약품고 {pharm.Name}#{pharm.Id} 문 {pharm.Doors.Count} · {p.Name} 부상 {p.Vitals.Injury:0.00} 체력 {p.Vitals.Health:0.00} · 방 {p.Room?.Name}");
            foreach (var d in pharm.Doors) Console.WriteLine($"   문 {d.Cell} {d.RoomA?.Name}/{d.RoomB?.Name} 잠김 {d.Locked} 전기 {d.Powered} 몸 {b.Doors.FirstOrDefault(x => x.Door == w.Ship.Doors.IndexOf(d))?.Zone}");
            var inside = pharm.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => (c.Center - pharm.Center).LengthSquared()).First();
            Force(w, p, new Job(null, "약 가지러", new List<Toil> { new GotoToil(inside), new WaitToil(10, Pose.Standing) }));
            Room? last = p.Room;
            for (int t = 0; t < SimTime.Minutes(40); t++)
            {
                w.Step();
                if (p.Room != last) { Console.WriteLine($"   {SimTime.Clock(w.Tick)} {p.Name} → {p.Room?.Name ?? "(방 밖 " + w.Ship.Grid.Kind(p.Cell) + ")"} {p.Cell}"); last = p.Room; }
                if (p.Room == pharm || p.Job == null) break;
            }
            foreach (var e in w.Log.Entries.Where(e => e.Tick > w.Tick - SimTime.Minutes(40) && (e.CrewId == p.Id || e.Text.Contains("문"))).TakeLast(8)) Console.WriteLine($"   기록 {SimTime.Clock(e.Tick)} {e.Text}");
        }
        if (what.Contains("ration"))
        {
            var a = Hungry(seed, "Hanbit", noRation: false);
            for (int h = 0; h < 48; h += 6)
            {
                float days = FoodPolicy.FoodDays(a), grow = FoodPolicy.GrowingPerDay(a), need = a.Crew.Count(c => !c.Dead) * FoodPolicy.MealsPerPersonDay;
                var o = a.Board.All.FirstOrDefault(x => x.Kind == WorkKind.Ration);
                Console.WriteLine($"   {h}h 먹을것 {days:0.00}일 · 재배 {grow:0.0}/{need:0.0} · 배급 {a.Food.Rationing} · 일 {(o == null ? "-" : $"{o.Urgency:0.00} {o.Assignee?.Name ?? "-"} 닫힘 {o.Closed} {o.BlockedReason}")} · 조리대 {a.Ship.FurnitureOf(FurnitureType.Stove).Count(s => s.UseSpots.Count > 0)} 배식기 {a.Ship.FurnitureOf(FurnitureType.MealDispenser).Count()}");
                Run(a, SimTime.Hours(6));
            }
        }
        if (what.Contains("tester"))
        {
            var w = DayOne(seed, "Mirinae");
            var ship = w.Ship;
            Console.WriteLine($"   전력 고리 {w.Net.Rings.Count(r => r.kind == NetKind.Power)} · 펌프+베어링 {ship.CountStored(ItemKind.Pump) + ship.CountStored(ItemKind.Bearing)} · 교정 틀어짐 {ship.Machines.Count(m => m.SensorCal < 0.7f)} · 실링폼 {ship.CountStored(ItemKind.Sealant)} · 구급 {ship.CountStored(ItemKind.MedKit)} · 식사 {ship.CountStored(ItemKind.Meal)}/{w.Crew.Count * 4}");
        }
    }
}
