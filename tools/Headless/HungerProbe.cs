using System;
using System.Linq;
using ShipSim.Core;

// v23 설계도 손보기 진단 (--hungerprobe 일 배): 식량이 쌓였는데 굶는 사람 — 누가 · 어디서 · 무엇을 하다가.
public static partial class Program
{
    private static int RunHungerProbe(int days, int seed, string ship)
    {
        var w = World.CreateDefault(seed, 0, ship);
        w.CrewCanDie = true;
        long end = (long)days * SimTime.TicksPerDay;
        var seen = new System.Collections.Generic.Dictionary<int, int>();
        while (w.Tick < end)
        {
            Run(w, SimTime.Minutes(30));
            foreach (var c in w.Crew.Where(c => !c.Dead && c.Needs.Food <= 0.04f))
            {
                int n = seen.TryGetValue(c.Id, out var k) ? k : 0;
                seen[c.Id] = n + 1;
                if (n >= 6) continue;
                var meal = w.Ship.CountStored(ItemKind.Meal);
                if (n < 2)
                {
                    var flood = w.Paths.Flood(c.Cell, c.PathProfile);
                    var disp = w.Ship.FurnitureOf(FurnitureType.MealDispenser).Where(f => f.UseSpots.Count > 0).ToList();
                    Console.WriteLine($"      길 규칙 {c.PathProfile} · 두뇌 지도로 닿는 배식기 {disp.Count(f => flood.Reachable(f.UseSpots[0]))}/{disp.Count} · 생각 {(c.LastThinkTick < 0 ? "-" : SimTime.Clock(c.LastThinkTick))} · 다음 {SimTime.Clock(c.NextThinkTick)} · 점수 {string.Join(" / ", (c.LastEvaluations ?? System.Array.Empty<Evaluation>()).Take(4).Select(e => $"{e.Activity.GetType().Name} {e.Score:0.00} {e.Reason}"))}");
                    int R(PathProfile pp) { var fl = w.Paths.Flood(c.Cell, pp); return disp.Count(f => fl.Reachable(f.UseSpots[0])); }
                    Console.WriteLine($"      기어가기 허용 {R(c.PathProfile with { NoCrawl = false })} · 위험 1배 {R(c.PathProfile with { HazardScale = 1f })} · 기본 {R(PathProfile.Default)} · 기본+나 {R(PathProfile.Default with { Who = c.Id })} · 위험 0 {R(c.PathProfile with { HazardScale = 0f })}");
                    var ff = typeof(EatActivity).GetMethod("FindFood", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object[] { c, w, flood });
                    var ft = (System.Runtime.CompilerServices.ITuple)ff!;
                    Console.WriteLine($"      음식 찾기 → {(ft[0] as Furniture)?.Label} ({(ft[0] as Furniture)?.Room.Name}) · {ft[2]} · 칸 {flood.Get((Cell)ft[1]!)} · 식당 출입 {string.Join(",", w.Ship.RoomsOf(RoomType.Mess).Select(r => w.Body.MayEnter(c, r)))} · 배식기 칸 거리 {string.Join(",", disp.Select(f => flood.Get(f.UseSpots[0])))}");
                    Console.WriteLine($"      짐 {c.Carrying?.Kind}×{c.Carrying?.Count ?? 0} · 업은 사람 {c.CarryingPerson?.Name ?? "-"} · 부상 {c.Vitals.Injury:0.00} · 서 있는 칸 걷기 {w.Ship.IsWalkable(c.Cell)} · 통로 {w.Paths.Crawl[w.Ship.Grid.Index(c.Cell)]}");
                    foreach (var d in c.Room?.Doors ?? Enumerable.Empty<Door>())
                        Console.WriteLine($"      문{d.Id} {d.Cell} 걷기 {w.Ship.IsWalkable(d.Cell)} · 열림 {d.Openness:0.0} · 잠김 {d.Locked} · 용접 {d.Welded} · 떼어냄 {d.Removed} · {d.RoomA?.Name}↔{d.RoomB?.Name}");
                    var door = c.Room?.Doors.Select(d => $"문{d.Id}:{(d.Locked ? "잠김" : "")}{(d.Welded ? "용접" : "")}{(d.Bulkhead ? "격벽" : "")}{(d.Powered ? "" : "전기끊김")}{(d.MotorBroken ? "모터고장" : "")}");
                    Console.WriteLine($"      방 문 {string.Join(",", door ?? Enumerable.Empty<string>())}");
                }
                if (n < 2)
                    foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null && (f.Type is FurnitureType.MealDispenser or FurnitureType.Fridge) && f.Storage.Count(ItemKind.Meal) > 0).Take(12))
                    {
                        var path = f.UseSpots.Count > 0 ? w.Paths.Find(c.Cell, f.UseSpots[0], PathProfile.Default with { Who = c.Id }) : null;
                        Console.WriteLine($"      식사 {f.Storage!.Count(ItemKind.Meal)} · {f.Label} ({f.Room.Name}) · {(path == null ? "길 없음" : path.Count + "칸")} · 효율 {f.Machine?.Efficiency ?? 1f:0.0} · 줄 {w.Coop.Queues.LineAt(QueueKind.Meal, f)}");
                    }
                Console.WriteLine($"{SimTime.Day(w.Tick)}일 {SimTime.Clock(w.Tick)} {c.Name}({c.Role}) 포만 {c.Needs.Food * 100:0}% · 하는 일 {c.Job?.Label ?? "없음"} · 방 {c.Room?.Name ?? "-"} · 길 {(c.Path?.Count ?? 0)}칸 · 깨어 {c.IsAwake} · 바깥 {c.Outside}/{c.Away} · 쓰러짐 {c.Down} · 잠 {c.Needs.Rest * 100:0}% · 병 {string.Join(",", c.Ailments.Select(a => a.Id))} · 배의 식사 {meal}");
            }
        }
        Console.WriteLine("굶은 횟수 (30분마다): " + string.Join(" · ", seen.OrderByDescending(kv => kv.Value).Select(kv => $"{w.Crew[kv.Key].Name} {kv.Value}")));
        return 0;
    }
}

// v23 설계도 손보기 근거 (--shipcensus): 배마다 인원 대비 설비 수 · 원자로 · 정수 용량 (시뮬레이션 없이)
public static partial class Program
{
    private static int RunShipCensus(int seed)
    {
        Console.WriteLine("배 | 인원 | 원자로kW | 정수기 | 정수 L/h | 산소 | 재배대 | 조리대 | 배식기 | 배터리 | 보조발전");
        foreach (var t in ShipCatalog.All)
        {
            var w = World.CreateDefault(seed, 0, t.Key);
            var s = w.Ship;
            int N(FurnitureType ft) => s.FurnitureOf(ft).Count();
            float waterCap = s.FurnitureOf(FurnitureType.WaterRecycler).Sum(f => f.Machine!.Rating) * WaterSystem.RecyclerLitersPerHour;
            Console.WriteLine($"{t.Name}({t.Key}) | {w.Crew.Count} | {w.Power.ReactorRated:0} | {N(FurnitureType.WaterRecycler)} | {waterCap:0.0} | {N(FurnitureType.OxygenGenerator)} | {N(FurnitureType.GrowBed)} | {N(FurnitureType.Stove)} | {N(FurnitureType.MealDispenser)} | {N(FurnitureType.Battery)} | {N(FurnitureType.AuxGenerator)}");
        }
        return 0;
    }
}

// v23 기술 개편 준비 (--techdump): 지금 기술 전부 (시대 · 분야 · 이름 · 효과 · 그물 마디)
public static partial class Program
{
    private static int RunTechDump()
    {
        foreach (var t in TechWeb.Every.OrderBy(t => t.Field).ThenBy(t => t.Era))
        {
            var n = TechWeb.Node(t.Id);
            Console.WriteLine($"{t.Field}\t{t.Era}\t{t.Id}\t{t.Name}\t{t.Cost:0}\t{t.Effect}\t{(n.Hidden ? "숨김" : "")}{(n.Gate != null ? " 조건:" + n.Gate.Text : "")}{(n.Combo != null ? " 조합:" + string.Join("+", n.Combo) : "")}{(n.Trial ? " 역설계" : "")}");
        }
        Console.WriteLine($"모두 {TechWeb.Every.Length}");
        return 0;
    }
}
