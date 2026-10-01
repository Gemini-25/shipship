using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.8 실제 음식 · 냄새: 늦게 온 정비사의 남겨 둔 접시 · 빵 냄새에 모이는 사람들 · 경보보다 먼저 탄 냄새 ·
// 조리사가 다치면 배우던 사람이 대신(맛이 달라진 걸 안다) · 전기가 모자라면 차갑게 · 냄비 단위 신선도 · 결정론
public static partial class Program
{
    private static int RunFoodTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"음식 · 냄새 점검 (v16.8) · 시드 {seed}\n");
        try
        {
            Console.WriteLine($"  레시피 {Dishes.All.Length}가지 — 예: {Dishes.Line(Dishes.All[1])} / {Dishes.Line(Dishes.All[17])} / {Dishes.Line(Dishes.All[23])}");

            // 0) 평소의 하루: 냄비가 생기고 접시로 나뉘어 먹히고, 냄새를 맡는다
            {
                var w = DayOne(seed, "Hanbit");
                Run(w, SimTime.TicksPerDay);
                var st = w.Cooking.Stats;
                Check("평소 — 조리하면 냄비가 생기고, 먹을 때 접시로 나뉜다 · 냄새를 맡는다", st.Batches > 0 && st.Served > 0 && w.Smells.Stats.Sniffs > 0,
                    $"{st.Summary()} · {w.Smells.Stats.Summary()}");
            }

            // 1) 냄비 단위 신선도: 냉장고에 들어간 냄비는 싱싱하고, 냉장고가 멈추면 상해서 냄새가 나고 버린다
            {
                (float fresh, bool spoiled, float foul, int complaints, int left) Pot(bool fridgeOn)
                {
                    var w = DayOne(seed, "Mirinae");
                    void Stop() { if (!fridgeOn) foreach (var f in w.Ship.FurnitureOf(FurnitureType.Fridge)) if (f.Machine!.Efficiency > 0.2f) w.Machines.Break(f.Machine, FaultKind.Wrecked); }
                    Stop(); // 냉장고가 멈췄다 (고쳐도 또 멈춘다)
                    var cook = w.Cooking.HeadCook!;
                    var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
                    w.Cooking.ForceNext = "stew";
                    int put = 0; // 큰 냄비 하나 (서른 그릇) — 재고에도 그만큼 들어간다
                    foreach (var f in w.Ship.Containers.Where(f => f.Storage!.Accepts(ItemKind.Meal)).OrderBy(f => f.Type == FurnitureType.Fridge ? 0 : 1))
                        if ((put += f.Storage!.Add(ItemKind.Meal, 30 - put)) >= 30) break;
                    w.Cooking.OnCooked(cook, stove, put);
                    var b = w.Cooking.Batches.Last();
                    float foul = 0f;
                    for (int m = 0; m < 34 * 6; m++)
                    {
                        Run(w, SimTime.Minutes(10));
                        Stop();
                        if (b.Room != null) foul = MathF.Max(foul, w.Smells.Level(b.Room, SmellKind.Foul));
                    }
                    return (b.Fresh, b.Spoiled, foul, w.Smells.Stats.Complaints, w.Cooking.Batches.Count(x => !x.Jar));
                }
                var on = Pot(true);
                var off = Pot(false);
                Check("냄비 신선도 — 냉장고에 든 냄비는 싱싱하고 · 냉장고가 멈추면 상해서 냄새가 나고 버린다", on.fresh > 0.7f && !on.spoiled && off.spoiled && off.foul > 0.1f,
                    $"냉장 {on.fresh * 100:0}% · 멈춤 {off.fresh * 100:0}% 상함 {off.spoiled} · 악취 최고 {off.foul * 100:0}% · 불평 {off.complaints} · 남은 냄비 {on.left}/{off.left}");
            }

            // 2) 늦게 퇴근한 정비사: 일하느라 저녁을 놓쳤다 → 누군가 이름표를 붙여 덜어 둔다 → 식은 수프를 먹으려다 제 몫인 걸 안다
            {
                var w = DayOne(seed, "Mirinae");
                var cook = w.Cooking.HeadCook!;
                var mech = w.Crew.Where(c => c != cook && !c.IsChild).OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
                var pump = w.Ship.Machines.First(m => m.Body.Type == FurnitureType.CoolantPump);
                w.Machines.Break(pump, FaultKind.BearingWear);
                mech.Needs.Food = 0.76f; // 아직 배고프지 않아 일을 붙든다
                Run(w, SimTime.Minutes(20));
                var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
                w.Cooking.ForceNext = "vegsoup";
                w.Cooking.OnCooked(cook, stove, 6);
                var soup = w.Cooking.Batches.Last();
                Plate? plate = null;
                string how = "자연";
                string dbg = "";
                for (int m = 0; m < 180 && plate == null; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    plate = w.Cooking.Plates.FirstOrDefault(p => p.For == mech.Id);
                }
                if (plate == null)
                {
                    // 아무도 식당에 없었다 — 조리사에게 덜어 두게 한다 (장면을 꾸민다)
                    how = "부탁";
                    if (soup.Portions <= 0) { w.Cooking.ForceNext = "vegsoup"; w.Cooking.OnCooked(cook, stove, 6); soup = w.Cooking.Batches.Last(); }
                    var saver = w.Crew.Where(c => c != mech && c.CanAct && !c.IsChild).OrderByDescending(c => c == cook).First();
                    w.Cooking.AskToSave(saver, mech, soup);
                    for (int m = 0; m < 120 && plate == null; m++) { Run(w, SimTime.Minutes(1)); plate = w.Cooking.Plates.FirstOrDefault(p => p.For == mech.Id); }
                    dbg = $" · 맡긴 사람 {saver.Name}({saver.ActivityLabel}) · 남은 {soup.Portions}";
                }
                float tempAtFind = -1f;
                for (int m = 0; m < 14 * 60 && plate != null && !plate.Eaten; m++)
                {
                    bool before = plate.Found;
                    Run(w, SimTime.Minutes(1));
                    if (!before && plate.Found) tempAtFind = plate.Temp;
                }
                var by = plate != null ? w.Crew[plate.By] : null;
                var mem = by != null ? w.Relations.Of(mech, by).FirstOrDefault(x => x.Text.Contains("남겨")) : null;
                string diary = mech.Diary.LastOrDefault(d => d.text.Contains("이름표")).text ?? "";
                Check("늦게 온 정비사 — 식은 수프를 먹으려다 이름표를 보고 제 몫인 걸 안다 (기억 · 관계)",
                    plate is { Found: true, Eaten: true } && tempAtFind is >= 0f and < 45f && mem != null && diary.Length > 0,
                    $"{how}{dbg} · {mech.Name}({mech.ActivityLabel} · 배고픔 {mech.Needs.Hunger:0.00}) ← {by?.Name} · 찾을 때 {tempAtFind:0}℃ · 기억 \"{mem?.Text}\" · 일기 \"{diary}\" · {w.Cooking.Stats.Summary()}");
            }

            // 3) 빵 굽는 냄새: 주방에서 빵을 구우면 냄새가 문과 덕트로 퍼지고, 배고픈 사람들이 주방으로 모인다
            {
                var w = DayOne(seed, "Hanbit");
                Run(w, SimTime.Hours(3)); // 10시쯤 — 아침과 점심 사이
                int crew = w.Crew.Count(c => !c.Dead);
                foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.55f);
                int keep = crew * 2;
                int have = w.Ship.CountStored(ItemKind.Meal);
                if (have > keep) Life.Take(w, ItemKind.Meal, have - keep); // 식사가 모자라 조리 일감이 선다
                w.Cooking.ForceNext = "bread";
                long bakeAt = -1;
                for (int m = 0; m < 240 && bakeAt < 0; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (w.Ship.FurnitureOf(FurnitureType.Stove).Any(s => w.Cooking.CookingAt(s)?.Kind == DishKind.Bread)) bakeAt = w.Tick;
                }
                var galley = w.Ship.FurnitureOf(FurnitureType.Stove).FirstOrDefault(s => w.Cooking.CookingAt(s) != null)?.Room;
                var came = new HashSet<int>();
                float spread = 0f;
                for (int m = 0; m < 70 && bakeAt >= 0; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    foreach (var c in w.Crew) if (c.Job?.Activity is FollowSmellActivity) came.Add(c.Id);
                    if (galley != null)
                        foreach (var (o, _) in w.Ambience.Neighbors(galley)) spread = MathF.Max(spread, w.Smells.Level(o, SmellKind.Bread));
                }
                Check("빵 굽는 냄새 — 냄새가 옆방으로 번지고 사람들이 주방으로 모인다", bakeAt >= 0 && came.Count >= 2 && spread > SmellSystem.Threshold(SmellKind.Bread),
                    $"굽기 시작 {(bakeAt >= 0 ? SimTime.Clock(bakeAt) : "없음")} · 옆방 빵 냄새 최고 {spread * 100:0}% · 냄새 따라온 사람 {came.Count}명 ({string.Join(", ", came.Select(i => w.Crew[i].Name))}) · {w.Smells.Stats.Summary()}");
            }

            // 4) 탄 냄새: 불려 나간 조리사가 냄비를 불 위에 두고 갔다 → 냄새를 맡은 사람이 경보(불꽃 감지)보다 먼저 확인하러 가서 내린다
            {
                var w = DayOne(seed, "Mirinae");
                Run(w, SimTime.Hours(2));
                var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
                long alerts0 = w.AlertSerial;
                w.Cooking.LeaveOnBurner(stove, null);
                long left = w.Tick, checkAt = -1, alarmAt = -1;
                string? checker = null;
                for (int m = 0; m < 90; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (checkAt < 0 && w.Crew.FirstOrDefault(c => c.Job?.Activity is CheckSmellActivity) is CrewMember ch) { checkAt = w.Tick; checker = ch.Name; }
                    if (alarmAt < 0 && w.Alerts.Any(a => a.Serial > alerts0 && a.Text.Contains("화재"))) alarmAt = w.Tick;
                    if (w.Cooking.ScorchingIn(stove.Room) == null && m > 2) break;
                }
                var st = w.Cooking.Stats;
                Check("탄 냄새 — 맡은 사람이 경보보다 먼저 확인하러 가서 불 위의 냄비를 내린다", checkAt >= 0 && (alarmAt < 0 || checkAt < alarmAt) && st.ScorchCaught > 0,
                    $"{checker} 확인 출발 {(checkAt >= 0 ? $"+{(checkAt - left) / (float)SimTime.TicksPerHour * 60:0}분" : "없음")} · 경보 {(alarmAt >= 0 ? $"+{(alarmAt - left) / (float)SimTime.TicksPerHour * 60:0}분" : "없음")} · {st.Summary()} · {w.Smells.Stats.Summary()}");
            }

            // 4b) 감지기가 꺼진 방의 불: 연기 냄새가 옆방으로 새어 나가 맡은 사람이 먼저 찾는다
            {
                var w = DayOne(seed, "Hanbit");
                Run(w, SimTime.Hours(2));
                foreach (var rb in w.Robots.Robots.Where(r => RobotsV15.Base(r.Kind) == RobotKind.Safety)) w.Robots.ForceFault(rb, RobotFault.Drive); // 순찰 로봇은 멈춰 있다
                var room = w.Ship.LiveRooms.Where(r => r.Type is not (RoomType.Corridor or RoomType.Galley or RoomType.Mess) && !r.Abandoned && w.Crew.All(c => c.Room != r)
                        && w.Ambience.Neighbors(r).Any(n => n.door && w.Crew.Any(c => c.IsAwake && c.Room == n.room)) && r.Cells.Any(c => w.Ship.IsWalkable(c)))
                    .OrderBy(r => r.Id).First();
                room.BreakerOff = true; // 분전함을 내려 감지기가 꺼졌다
                var cell = room.Cells.First(c => w.Ship.IsWalkable(c));
                w.Fire.Ignite(cell, 0.25f);
                long checkAt = -1;
                for (int m = 0; m < 60 && !w.Fire.IsKnown(room); m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (checkAt < 0 && w.Crew.Any(c => c.Job?.Activity is CheckSmellActivity)) checkAt = w.Tick;
                }
                string how = w.Alerts.LastOrDefault(a => a.Text.Contains("화재"))?.Text ?? "";
                Check("감지기 없는 방의 불 — 탄 냄새를 따라온 사람이 먼저 찾는다", checkAt >= 0 && w.Fire.IsKnown(room) && w.Smells.Stats.FireBySmell > 0,
                    $"{room.Name} · {how} · {w.Smells.Stats.Summary()}");
            }

            // 5) 조리사가 다치면 배우던 사람이 대신 서고, 먹는 사람이 맛이 달라진 걸 알아챈다
            {
                var w = DayOne(seed, "Mirinae");
                var head = w.Cooking.HeadCook!;
                var appr = w.Cooking.Apprentice!;
                Run(w, SimTime.TicksPerDay); // 다들 조리사의 맛에 익숙해진다
                appr.SkillLevels[(int)Skill.Cooking] = MathF.Max(0.05f, head.RawSkill(Skill.Cooking) - 0.35f); // 아직 배우는 중
                NeedsSystem.AddInjury(head.Vitals, 0.6f, "주방 화상");
                int cover0 = w.Cooking.Stats.CoverCooks, taste0 = w.Cooking.Stats.TasteNoticed;
                for (int h = 0; h < 36 && (w.Cooking.Stats.CoverCooks == cover0 || w.Cooking.Stats.TasteNoticed == taste0); h++)
                {
                    Run(w, SimTime.Hours(1));
                    if (head.Vitals.Injury < 0.3f) NeedsSystem.AddInjury(head.Vitals, 0.3f, "주방 화상");
                }
                var byAppr = w.Cooking.Batches.Any(b => b.Cook == appr.Id) || w.Log.Entries.Any(e => e.CrewId == appr.Id && e.Text.StartsWith("오늘은"));
                var noticed = w.Crew.SelectMany(c => c.Diary.Select(d => (c.Name, d.text))).Where(d => d.text.Contains("맛")).Select(d => $"{d.Name}: {d.text}").LastOrDefault();
                Check("조리사 부상 — 배우던 사람이 대신 서고, 맛이 달라진 걸 알아챈다 (일기)", byAppr && w.Cooking.Stats.TasteNoticed > taste0 && noticed != null,
                    $"조리사 {head.Name} · 배우던 사람 {appr.Name} · 대신 {w.Cooking.Stats.CoverCooks - cover0}번 · 알아챔 {w.Cooking.Stats.TasteNoticed - taste0} · \"{noticed}\"");
            }

            // 6) 전기가 모자라면 데우지 않고 차갑게 먹는다 (넉넉하면 데운다)
            {
                (int cold, int reheated) Meal(bool brownout)
                {
                    var w = DayOne(seed, "Mirinae");
                    var cook = w.Cooking.HeadCook!;
                    var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
                    w.Cooking.ForceNext = "vegsoup";
                    w.Cooking.OnCooked(cook, stove, 12);
                    w.Cooking.Batches.Last().Temp = 22f; // 한참 놓여 식었다
                    if (brownout) w.Power.Brownout = true;
                    foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.3f);
                    Run(w, SimTime.Hours(1.5f));
                    return (w.Cooking.Stats.ColdNoPower, w.Cooking.Stats.Reheated);
                }
                var low = Meal(true);
                var ok = Meal(false);
                Check("전력 부족 — 식은 수프를 데우지 않고 차갑게 먹는다 (전기가 넉넉하면 데운다)", low.cold > 0 && ok.reheated > 0 && ok.cold == 0,
                    $"저출력: 차갑게 {low.cold} · 데움 {low.reheated} / 평소: 차갑게 {ok.cold} · 데움 {ok.reheated}");
            }

            // 7) 기항지에서 고향 재료 → 고향 음식을 먹은 사람이 고향 맛을 느낀다
            {
                var w = DayOne(seed, "Mirinae");
                var lines = new List<string>();
                w.Cooking.OnPort("시험 정거장", lines);
                var who = w.Crew.FirstOrDefault(c => !c.Dead && Dishes.HomeDish(w, c).Uses(Ingredient.HomeGoods) && w.Cooking.HomeGoods[Dishes.IndexOf(Dishes.HomeDish(w, c).Id)] > 0)
                          ?? w.Crew.First(c => !c.Dead && !Dishes.HomeDish(w, c).Uses(Ingredient.HomeGoods));
                var dish = Dishes.HomeDish(w, who);
                w.Cooking.ForceNext = dish.Id;
                var cook = w.Cooking.HeadCook!;
                w.Cooking.OnCooked(cook, w.Ship.FurnitureOf(FurnitureType.Stove).First(), 8);
                who.Needs.Food = 0.25f;
                Run(w, SimTime.Hours(2));
                Check("고향 음식 — 기항지에서 재료를 사고, 그 음식을 먹은 사람이 고향 맛을 느낀다", w.Cooking.Stats.HomeMeals > 0,
                    $"{who.Name}의 고향 음식 {dish.Name} · 기항지 \"{string.Join(", ", lines)}\" · {who.Diary.LastOrDefault(d => d.text.Contains("고향")).text}");
            }

            // 8) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 냄비 · 접시 · 냄새가 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 음식 · 냄새 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
