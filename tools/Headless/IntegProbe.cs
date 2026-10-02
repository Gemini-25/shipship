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
            for (int t = 0; t < SimTime.Minutes(60); t++)
            {
                w.Step();
                if (p.Room != last) { Console.WriteLine($"   {SimTime.Clock(w.Tick)} {p.Name} → {p.Room?.Name ?? "(방 밖 " + w.Ship.Grid.Kind(p.Cell) + ")"} {p.Cell}"); last = p.Room; }
                if (t % SimTime.Minutes(1) == 0) foreach (var db in b.Doors.Where(x => x.Caller >= 0)) { var hh = w.Crew.FirstOrDefault(x => x.Id == db.Helper); Console.WriteLine($"      {SimTime.Clock(w.Tick)} 부른 {db.Caller} 도움 {hh?.Name ?? "-"} {hh?.Job?.Label}/{hh?.Job?.Activity?.Id} 위치 {hh?.Room?.Name} 다음생각 {(hh != null ? hh.NextThinkTick - w.Tick : 0)} 깸 {hh?.IsAwake} · 바깥칸 {w.Body.OuterCell(w.Ship.Doors[db.Door], db)} 닿음 {(hh != null && w.Body.OuterCell(w.Ship.Doors[db.Door], db) is Cell oc ? w.Paths.Flood(hh.Cell, hh.PathProfile).Get(oc) : -9)}"); }
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

        if (what.Contains("robot"))
        {
            var w = DayOne(seed, "Hanbit");
            var r = w.Robots.Robots.First(x => x.Kind == RobotKind.Maintainer);
            w.Robots.ForceFault(r, RobotFault.Drive);
            for (int h = 0; h < 14; h++)
            {
                Run(w, SimTime.Hours(1));
                var os = w.Board.All.Where(o => !o.Closed && (o.Kind is WorkKind.RepairRobot or WorkKind.FetchRobot)).ToList();
                Console.WriteLine($"   {h + 1}h 고장 {r.Fault} 상태 {r.State} 위치 {r.Position} · 일 {string.Join(" / ", os.Select(o => $"{o.Kind} {o.Urgency:0.00} {o.Assignee?.Name ?? "-"} 로봇 {o.Robot?.Name ?? "-"}({o.Robot?.State} {o.Robot?.Kind} 배터리 {o.Robot?.Battery:0.00} 고침 {o.Robot?.Fixing?.Name} 위치 {o.Robot?.Position}) {o.BlockedReason}"))} · 부품 {w.Ship.CountStored(ItemKind.Motor)}");
                if (r.Fault == null) break;
                if (h == 1)
                {
                    foreach (var e in w.Log.Entries.Where(e => e.Tick > w.Tick - SimTime.Hours(2) && (e.Text.Contains("로봇") || e.Text.Contains("모터") || w.Crew.FirstOrDefault(c => c.Id == e.CrewId)?.Name == "온다인")).Take(25))
                        Console.WriteLine($"      {SimTime.Clock(e.Tick)} [{w.Crew.FirstOrDefault(c => c.Id == e.CrewId)?.Name}] {e.Text}");
                    var oo = w.Board.All.First(o => !o.Closed && o.Kind == WorkKind.RepairRobot);
                    foreach (var c in w.Crew.Where(c => c.CanAct).Take(12))
                    {
                        var dist = w.Paths.Flood(c.Cell, c.PathProfile);
                        Console.WriteLine($"      {c.Name}: 매력 {ChoresActivity.Appeal(c, w, oo, dist, out int dd):0.00} 거리 {dd} · 일 {c.Job?.Label}");
                    }
                }
            }
        }
        if (what.Contains("fooddays"))
            foreach (var key in new[] { "Kestrel", "Mirinae", "Hanbit", "Eunha", "Cheonma" })
            {
                var w = DayOne(seed, key);
                Console.WriteLine($"   {key}: 사람 {w.Crew.Count} · 먹을 것 {FoodPolicy.FoodDays(w):0.0}일 ({FoodPolicy.FoodStock(w):0}끼) · 재배 {FoodPolicy.GrowingPerDay(w):0}/{w.Crew.Count * FoodPolicy.MealsPerPersonDay:0}");
            }

        if (what.Contains("press"))
        {
            var w = DayOne(seed, "Mirinae"); w.CrewCanDie = true;
            var room = AfxQuietRoom(w);
            var v = w.Crew.First(c => c.CanAct); var h = w.Crew.First(c => c.CanAct && c != v);
            Put(w, v, room); Run(w, 1); AfxNextTo(w, h, v); h.EndJob(w, ToilStatus.Interrupted);
            v.Vitals.Health = 0.55f; NeedsSystem.AddInjury(v.Vitals, 0.45f, "운석 파편");
            Run(w, World.SystemInterval * 2);
            var spot = v.Cell;
            for (int i = 0; i < SimTime.Minutes(5); i++)
            {
                if (i % 20 == 0) { v.Position = spot.Center; AfxNextTo(w, h, v); h.EndJob(w, ToilStatus.Interrupted); AfxStay(w, h); }
                w.Step();
                if (w.Tick % World.SystemInterval == 0 && i < 100) Console.WriteLine($"      sys {i}: h방 {h.Room?.Name} v방 {v.Room?.Name} 급함 {h.Job?.Urgent} 대상방 {h.Job?.Target?.Room?.Name} 대상사람 {h.Job?.Order?.Target.Crew?.Name} 거리 {(h.Position - v.Position).Length():0.00} 다른 {string.Join(",", w.Crew.Where(o => o != v && o != h && o.Room == v.Room).Select(o => o.Name))} · 도움 {w.Casualty.Of(v)?.Helper}");
                if (i % 20 == 1) { var t = w.Casualty.Of(v); Console.WriteLine($"   {i} v {v.Room?.Name} {v.Cell} pose {v.Pose} · h {h.Name} {h.Room?.Name} {h.Cell} canact {h.CanAct} pose {h.Pose} out {h.Outside}/{v.Outside} 급함 {h.Job?.Urgent} 일 {h.Job?.Label} · 거리 {(h.Position - v.Position).Length():0.00} · 상처 {t?.Kind} {t?.Rate:0.000} 도움 {t?.Helper}"); }
            }
        }

        if (what.Contains("storm"))
        {
            var w = DayOne(seed, "Hanbit"); w.CrewCanDie = true;
            RunUntilHour(w, 2f);
            w.Hazards.StartStorm();
            typeof(HazardSystem).GetProperty("StormPeak")!.SetValue(w.Hazards, 2.5f);
            for (int q = 0; q <= 16; q++)
            {
                if (q % 2 == 0)
                {
                    Console.WriteLine($"   {SimTime.Clock(w.Tick)} 폭풍 {w.Ambience.StormPower:0.00}");
                    foreach (var c in w.Crew.Where(c => !c.Dead).Take(12))
                        Console.WriteLine($"      {c.Name,-6} {c.Room?.Name ?? (c.Outside ? "선외" : "-"),-8} 노출 {(c.Room != null ? w.Ambience.Exposure(c.Room) : 0):0.00} 방사 {c.Room?.Radiation:0.00} 누적 {c.Dose:0.00}Sv · {c.Pose} {c.Job?.Label}");
                }
                Run(w, SimTime.Minutes(15));
            }
        }

        if (what.Contains("major"))
        {
            string list = Environment.GetEnvironmentVariable("MAJ") ?? "reactorcool,multifire,platetear,cascadedecomp,smokespread,ammonia,lscascade,crackrun,cargo,trunkfire";
            foreach (var key in list.Split(','))
            {
                var w = DayOne(seed, Environment.GetEnvironmentVariable("SHIP") ?? "Hanbit"); w.CrewCanDie = true;
                RunUntilHour(w, float.Parse(Environment.GetEnvironmentVariable("HOUR") ?? "14"));
                var inj0 = w.Crew.ToDictionary(c => c.Id, c => c.Vitals.Injury);
                var minHp = w.Crew.ToDictionary(c => c.Id, c => 1f);
                string? what2 = w.Major.Fire("major:" + key, null);
                float maxHeat = 0f, maxTemp = 0f, maxSmoke = 0f, minP = 200f;
                var hurt = new Dictionary<string, float>();
                for (int m = 0; m < 6 * 60; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    foreach (var c in w.Crew)
                    {
                        if (c.Vitals.Health < minHp[c.Id]) minHp[c.Id] = c.Vitals.Health;
                        maxHeat = MathF.Max(maxHeat, w.Perils.HeatOf(c));
                        if (c.Room != null) { maxTemp = MathF.Max(maxTemp, c.Room.Air.Temperature); maxSmoke = MathF.Max(maxSmoke, c.Room.Air.Smoke); minP = MathF.Min(minP, c.Room.Air.Pressure); }
                    }
                }
                foreach (var c in w.Crew) { float d = c.Vitals.Injury - inj0[c.Id]; if (d > 0.01f) { string k = c.Vitals.InjuryCause ?? "?"; hurt[k] = hurt.GetValueOrDefault(k) + d; } }
                Console.WriteLine($"   {key}: {what2} · 죽음 {w.Crew.Count(c => c.Dead)} · 쓰러짐 {w.History.Collapses} · 최저 체력 {minHp.Values.Min():0.00} · 사람이 겪은 최고 {maxTemp:0}℃ 연기 {maxSmoke:0.00} 최저 압력 {minP:0} · 열 {maxHeat:0.00} · 다침 {string.Join(" ", hurt.Select(kv => $"{kv.Key} {kv.Value:0.00}"))} · 출혈 {w.Casualty.Bleeds}");
            }
        }
    }
}
