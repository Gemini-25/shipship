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
                    var b = w.Cooking.Batches.Last(x => !x.Jar); // (조리 뒤 항아리를 앉혔을 수도 있다)
                    b.FlagIgnored = true; // 조리사가 주컴퓨터 알림을 흘려들었다 (컴퓨터 쪽 길은 10번 시험이 본다)
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
                w.Cooking.OnCooked(cook, stove, 8); // 넉넉히 — 배가 고프면 일찍 먹으러 오니 여섯 그릇은 덜어 두기 전에 바닥났다
                var soup = w.Cooking.Batches.Last(x => !x.Jar);
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
                    if (soup.Portions <= 0) { w.Cooking.ForceNext = "vegsoup"; w.Cooking.OnCooked(cook, stove, 6); soup = w.Cooking.Batches.Last(x => !x.Jar); }
                    // 손이 빈 사람에게 (조리사가 배우기 · 수리에 붙어 있으면 두 시간을 넘겼다)
                    var saver = w.Crew.Where(c => c != mech && c.CanAct && !c.IsChild && c.IsAwake).OrderByDescending(c => c.Job?.Order == null).ThenByDescending(c => c == cook).ThenBy(c => c.Id).First();
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
                foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.45f); // 통합8 끼니 사이 출출한 정도(0.55)로는 꾸밈 · 일에 밀려 한 사람만 왔다 — 배고픈 사람들
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
                if (Environment.GetEnvironmentVariable("FOODDBG") != null && galley != null) Console.WriteLine($"    [빵 이웃] {galley.Name}: " + string.Join(", ", w.Ambience.Neighbors(galley).Select(n => $"{n.room.Name}{(n.door ? "(문)" : "(벽)")}")));
                var came = new HashSet<int>();
                float spread = 0f;
                for (int m = 0; m < 70 && bakeAt >= 0; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    foreach (var c in w.Crew) if (c.Job?.Activity is FollowSmellActivity) came.Add(c.Id);
                    if (Environment.GetEnvironmentVariable("FOODDBG") != null && m % 5 == 0) Console.WriteLine($"    [빵 {m}] " + string.Join(" / ", w.Crew.Where(c => !c.Dead).Select(c => $"{c.Name}@{c.Room?.Name} {(c.Room != null ? w.Smells.Level(c.Room, SmellKind.Bread) : 0f):0.000} {(w.Smells.Smelled(c, SmellKind.Bread, SimTime.Minutes(20)) != null ? "맡음" : "")} 배고픔{c.Needs.Hunger:0.00} {c.ActivityLabel}")));
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
                float hereMax = 0f, nbMax = 0f;
                string where = "";
                for (int m = 0; m < 90; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    hereMax = MathF.Max(hereMax, w.Smells.Level(stove.Room, SmellKind.Burnt));
                    foreach (var (o, _) in w.Ambience.Neighbors(stove.Room)) nbMax = MathF.Max(nbMax, w.Smells.Level(o, SmellKind.Burnt));
                    if (Environment.GetEnvironmentVariable("FOODDBG") != null && m % 3 == 0) Console.WriteLine($"    [탄 {m}] " + string.Join(" / ", w.Crew.Where(c => !c.Dead).Select(c => $"{c.Name}@{c.Room?.Name} {(c.Room != null ? w.Smells.Level(c.Room, SmellKind.Burnt) : 0f):0.000} {(w.Smells.Smelled(c, SmellKind.Burnt, SimTime.Minutes(20)) != null ? "맡음" : "")} {c.ActivityLabel}")));
                    if (m == 10) where = string.Join(", ", w.Crew.Where(c => !c.Dead).Select(c => $"{c.Name}@{c.Room?.Name}{(c.IsAwake ? "" : "(잠)")}"));
                    if (checkAt < 0 && w.Crew.FirstOrDefault(c => c.Job?.Activity is CheckSmellActivity) is CrewMember ch) { checkAt = w.Tick; checker = ch.Name; }
                    if (checkAt < 0 && w.Smells.LastCheck.Tick >= left && w.Smells.LastCheck.Room == stove.Room.Id) { checkAt = w.Smells.LastCheck.Tick; checker = w.Crew[w.Smells.LastCheck.Crew].Name; } // 1분이 안 걸린 확인 (곁에 있던 사람)
                    if (alarmAt < 0 && w.Alerts.Any(a => a.Serial > alerts0 && a.Text.Contains("화재"))) alarmAt = w.Tick;
                    if (w.Cooking.ScorchingIn(stove.Room) == null && m > 2) break;
                }
                var st = w.Cooking.Stats;
                Check("탄 냄새 — 맡은 사람이 경보보다 먼저 확인하러 가서 불 위의 냄비를 내린다", checkAt >= 0 && (alarmAt < 0 || checkAt < alarmAt) && st.ScorchCaught > 0,
                    $"주방 탄내 최고 {hereMax * 100:0}% · 이웃 {nbMax * 100:0}% · [{where}] · {checker} 확인 출발 {(checkAt >= 0 ? $"+{(checkAt - left) / (float)SimTime.TicksPerHour * 60:0}분" : "없음")} · 경보 {(alarmAt >= 0 ? $"+{(alarmAt - left) / (float)SimTime.TicksPerHour * 60:0}분" : "없음")} · {st.Summary()} · {w.Smells.Stats.Summary()}");
            }

            // 4b) 감지기가 꺼진 방의 불: 연기 냄새가 옆방으로 새어 나가 맡은 사람이 먼저 찾는다
            {
                var w = DayOne(seed, "Hanbit");
                Run(w, SimTime.Hours(2));
                foreach (var rb in w.Robots.Robots.Where(r => RobotsV15.Base(r.Kind) == RobotKind.Safety)) w.Robots.ForceFault(rb, RobotFault.Drive); // 순찰 로봇은 멈춰 있다
                // 문으로 이어진 옆방에 깨어 있는 사람이 있는 빈 방 — 두뇌 2.0 뒤로는 2시간째에 통로가 빌 때가 있어(모두 방 안에서 일함) 그런 순간이 올 때까지 1분씩 기다린다
                Room? PickRoom() => w.Ship.LiveRooms.Where(r => r.Type is not (RoomType.Corridor or RoomType.Galley or RoomType.Mess) && !r.Abandoned && w.Crew.All(c => c.Room != r)
                        && w.Ambience.Neighbors(r).Any(n => n.door && w.Crew.Any(c => c.IsAwake && c.Room == n.room)) && r.Cells.Any(c => w.Ship.IsWalkable(c)))
                    .OrderBy(r => r.Id).FirstOrDefault();
                Room? picked = PickRoom();
                for (int k = 0; k < 180 && picked == null; k++) { Run(w, SimTime.Minutes(1)); picked = PickRoom(); }
                var room = picked ?? throw new InvalidOperationException("4b 장면: 문 옆에 깨어 있는 사람이 있는 빈 방이 3시간 동안 없다");
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
                // 주컴퓨터: 사람 코가 먼저 찾은 불을 감지기 상태와 견주어 적고, 화구 자리 비움 감시를 켠다
                var act = w.Automation.Book.Acts.LastOrDefault(a => a.Key.StartsWith("food:smell"));
                Check("주컴퓨터 — 탄 냄새 신고와 감지기를 견줘 기록하고(감지기 꺼짐) 화구 감시를 켠다", act != null && act.Judge.Contains("감지기") && w.Cooking.Watch.BurnerWatch,
                    $"{act?.Observe} | {act?.Judge} | {act?.Act} | {act?.Request} · {w.Cooking.Watch.Summary()}");
            }

            // 4c) 주컴퓨터의 화구 감시: 배운 뒤로는 화구가 켜진 채 곁에 아무도 없으면 가장 가까운 사람을 부른다 → 그 사람이 가서 냄비를 내린다
            {
                var w = DayOne(seed, "Hanbit");
                Run(w, SimTime.Hours(2));
                w.Cooking.Watch.Learn();
                var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
                // 화구 자리 비움: 주방에 있던 사람들이 급히 불려 나갔다 (Interrupt만으로는 화구 앞에 그대로 서 있다)
                var away = w.Ship.LiveRooms.Where(r => r != stove.Room && r.Type != RoomType.Corridor && !r.OffLimits && w.Ambience.Neighbors(stove.Room).All(n => n.room != r))
                    .OrderBy(r => r.Id).Select(r => r.Cells.FirstOrDefault(w.Ship.IsOpenFloor)).First(c => c != default);
                var cookAway = w.Crew.FirstOrDefault(c => !c.Dead && c.Room == stove.Room && c.Job?.Order?.Kind == WorkKind.Cook);
                foreach (var c in w.Crew.Where(c => c.Room == stove.Room).ToList()) { c.Interrupt(w); Teleport(w, c, away); c.PreviousPosition = c.Position; }
                w.Cooking.LeaveOnBurner(stove, cookAway);
                string? asked = null;
                for (int m = 0; m < 45 && w.Cooking.ScorchingIn(stove.Room) != null; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (asked == null && w.Crew.FirstOrDefault(c => w.Smells.AskedRoom(c) == stove.Room) is CrewMember a) asked = a.Name;
                }
                var st = w.Cooking.Stats;
                if (Environment.GetEnvironmentVariable("FOODDBG") != null) Console.WriteLine("    [화구] " + string.Join(" / ", w.Log.Entries.Where(e => e.Text.Contains("냄비") || e.Text.Contains("탄 냄새")).TakeLast(6).Select(e => SimTime.Clock(e.Tick) + " " + e.Text)));
                Check("주컴퓨터 — 화구 자리 비움을 보고 가까운 사람을 부른다 → 가서 불 위의 냄비를 내린다", w.Cooking.Watch.BurnerCalls > 0 && st.ScorchCaught > 0 && st.ScorchFires == 0,
                    $"부른 사람 {asked} · {w.Cooking.Watch.Summary()} · 먼저 내림 {st.ScorchCaught} · 불 {st.ScorchFires} · {w.Smells.Stats.Summary()}");
            }

            // 5) 조리사가 다치면 배우던 사람이 대신 서고, 먹는 사람이 맛이 달라진 걸 알아챈다
            {
                var w = DayOne(seed, "Mirinae");
                var head = w.Cooking.HeadCook!;
                var appr = w.Cooking.Apprentice!;
                Scenarios.LimitStock(w, ItemKind.Meal, w.Crew.Count); // v19 보존식이 넉넉하면 첫날 냄비가 거의 없어 '늘 먹던 맛'이 조리사 맛으로 잡히지 않았다 (끼니 44번이 보존식)
                Run(w, SimTime.TicksPerDay); // 다들 조리사의 맛에 익숙해진다
                appr.SkillLevels[(int)Skill.Cooking] = MathF.Max(0.05f, head.RawSkill(Skill.Cooking) - 0.5f); // 아직 배우는 중 (0.35 차이는 맛 차이가 알아챌 문턱 0.08에 걸려 끼니 순서에 따라 갈렸다)
                NeedsSystem.AddInjury(head.Vitals, 0.6f, "주방 화상");
                int cover0 = w.Cooking.Stats.CoverCooks, taste0 = w.Cooking.Stats.TasteNoticed;
                long hurtAt = w.Tick;
                for (int h = 0; h < 36 && (w.Cooking.Stats.CoverCooks == cover0 || w.Cooking.Stats.TasteNoticed == taste0); h++)
                {
                    Run(w, SimTime.Hours(1));
                    if (head.Vitals.Injury < 0.3f) NeedsSystem.AddInjury(head.Vitals, 0.3f, "주방 화상");
                }
                var byAppr = w.Cooking.Stats.CoverCooks > cover0 && !w.Cooking.Batches.Any(b => b.Cook == head.Id && b.Cooked > hurtAt); // 다친 조리사 대신 다른 사람(대개 배우던 사람 — 자고 있으면 깨어 있는 사람)이 섰다
                var noticed = w.Crew.SelectMany(c => c.Diary.Select(d => (c.Name, d.text))).Where(d => d.text.Contains("맛")).Select(d => $"{d.Name}: {d.text}").LastOrDefault();
                Check("조리사 부상 — 배우던 사람이 대신 서고, 맛이 달라진 걸 알아챈다 (일기)", byAppr && w.Cooking.Stats.TasteNoticed > taste0 && noticed != null,
                    $"조리사 {head.Name} · 배우던 사람 {appr.Name}(지금 {w.Cooking.Apprentice?.Name}) · 조리한 사람 {string.Join(",", w.Log.Entries.Where(e => e.Text.StartsWith("오늘은") && e.CrewId >= 0).Select(e => w.Crew[e.CrewId].Name).Distinct())} · 대신 {w.Cooking.Stats.CoverCooks - cover0}번 · 알아챔 {w.Cooking.Stats.TasteNoticed - taste0} · \"{noticed}\"");
            }

            // 6) 전기가 모자라면 데우지 않고 차갑게 먹는다 (넉넉하면 데운다)
            {
                (int cold, int reheated, string sum) Meal(bool brownout)
                {
                    var w = DayOne(seed, "Mirinae");
                    var cook = w.Cooking.HeadCook!;
                    var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
                    w.Cooking.ForceNext = "vegsoup";
                    w.Cooking.OnCooked(cook, stove, AddMeals(w, 12)); // 재고에도 그만큼
                    w.Cooking.Batches.Last(x => !x.Jar).Temp = 22f; // 한참 놓여 식었다
                    foreach (var x in w.Cooking.Batches.Where(x => !x.Jar && x != w.Cooking.Batches.Last(y => !y.Jar))) x.Portions = 0; // 다른 냄비는 비었다
                    if (brownout) w.Power.Brownout = true;
                    foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.3f);
                    int served0 = w.Cooking.Stats.Served;
                    var soup = w.Cooking.Batches.Last(x => !x.Jar);
                    Run(w, SimTime.Hours(1.5f));
                    string dbg = $"먹음 {w.Cooking.Stats.Served - served0} · 수프 {soup.Spec.Name} {soup.Portions}그릇 {soup.Temp:0}℃ 냉장 {soup.InFridge} · 냄비들 {string.Join(",", w.Cooking.Batches.Select(b => $"{b.Spec.Name}{b.Portions}/{b.Temp:0}℃{(b == soup ? "*" : "")}"))} ·식사 재고 {w.Ship.CountStored(ItemKind.Meal)} · "
                        + string.Join(" / ", w.Crew.Where(c => !c.Dead).Select(c => $"{c.Name} {c.Needs.Hunger:0.00} {c.ActivityLabel}"));
                    return (brownout ? w.Cooking.Stats.ColdNoPower : w.Cooking.Stats.Cold, w.Cooking.Stats.Reheated, dbg);
                }
                var low = Meal(true);
                var ok = Meal(false);
                Check("전력 부족 — 식은 수프를 데우지 않고 차갑게 먹는다 (전기가 넉넉하면 데운다)", low.cold > 0 && ok.reheated > 0 && ok.cold == 0,
                    $"저출력: 차갑게 {low.cold} · 데움 {low.reheated} / 평소: 차갑게 {ok.cold} · 데움 {ok.reheated} · [{low.sum}]");
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
                for (int q = 0; q < 8; q++)
                {
                    Run(w, SimTime.Minutes(15));
                    if (Environment.GetEnvironmentVariable("FOODDBG") != null) Console.WriteLine($"    [고향 {q}] {SimTime.Clock(w.Tick)} {who.Name}@{who.Room?.Name} {who.ActivityLabel} 배고픔 {who.Needs.Hunger:0.00} 깸 {who.IsAwake} · 먹음 {w.Cooking.Stats.Served} · 냄비 {string.Join(",", w.Cooking.Batches.Select(b => $"{b.Spec.Name}{b.Portions}"))}");
                }
                Check("고향 음식 — 기항지에서 재료를 사고, 그 음식을 먹은 사람이 고향 맛을 느낀다", w.Cooking.Stats.HomeMeals > 0,
                    $"{who.Name}의 고향 음식 {dish.Name} · 기항지 \"{string.Join(", ", lines)}\" · {who.Diary.LastOrDefault(d => d.text.Contains("고향")).text}");
            }

            int AddMeals(World w, int n)
            {
                int put = 0;
                foreach (var f in w.Ship.Containers.Where(f => f.Storage!.Accepts(ItemKind.Meal)).OrderBy(f => f.Type == FurnitureType.Fridge ? 0 : 1).ThenBy(f => f.Id))
                    if ((put += f.Storage!.Add(ItemKind.Meal, n - put)) >= n) break;
                return put;
            }

            // 9) 정전 → 냉장고가 멈춤 → 냄비가 상해 감 → 주컴퓨터 경보 · 시큼한 끼니 → 식중독
            {
                var w = DayOne(seed, "Hanbit");
                var cook = w.Cooking.HeadCook!;
                var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
                w.Cooking.ForceNext = "stew";
                w.Cooking.OnCooked(cook, stove, AddMeals(w, 30));
                var b = w.Cooking.Batches.Last(x => !x.Jar);
                foreach (var x in w.Cooking.Batches.Where(x => x != b && !x.Jar)) x.Portions = 0; // 다른 냄비는 다 먹었다
                foreach (var st0 in w.Ship.FurnitureOf(FurnitureType.Stove)) w.Machines.Break(st0.Machine!, FaultKind.Wrecked); // 화구도 멈춰 새로 끓일 수 없다
                var fr = w.Ship.FurnitureOf(FurnitureType.Fridge).ToList();
                b.InFridge = true; b.Room = fr.FirstOrDefault()?.Room ?? b.Room; b.Temp = 4f;
                foreach (var f in fr) f.Room.BreakerOff = true; // 정전: 냉장고 방의 분전함이 내려갔다
                float f0 = b.Fresh;
                for (int m = 0; m < 12; m++) { Run(w, SimTime.Minutes(10)); foreach (var f in fr) f.Room.BreakerOff = true; } // 누가 분전함을 올려도 또 내려간다
                foreach (var f in fr) f.Room.BreakerOff = true;
                float rot = (f0 - b.Fresh) / 2f; // 시간당 (냉장이면 0.004)
                b.Fresh = 0.42f; // … 그렇게 아홉 시간쯤 지났다 (장면을 당긴다)
                float freshAt = b.Fresh;
                foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.25f);
                for (int m = 0; m < 18 && w.Cooking.Stats.SpoilPoison == 0; m++) { Run(w, SimTime.Minutes(10)); foreach (var f in fr) f.Room.BreakerOff = true; }
                var st = w.Cooking.Stats;
                int sick = w.Crew.Count(c => c.PoisonAt >= 0 || c.Ailments.Count > 0);
                var act = w.Automation.Book.Acts.FirstOrDefault(a => a.Key == "food:fridge");
                Check("정전 → 냉장고 멈춤 → 냄비가 상해 감 → 주컴퓨터 경보 · 시큼한 끼니 → 식중독", rot > 0.012f && w.Cooking.Watch.FridgeAlerts > 0 && act != null && st.SourMeals > 0 && st.SpoilPoison > 0,
                    $"정전 중 신선도 시간당 -{rot * 100:0.0}% · {freshAt * 100:0}%에서 먹음 · 경보 \"{w.Alerts.LastOrDefault(a => a.Text.Contains("냉장고"))?.Text}\" · 기록 [{act?.Observe} | {act?.Judge} | {act?.Request}] · 시큼한 끼니 {st.SourMeals} · 식중독 {st.SpoilPoison} · 앓는 사람 {sick}");
            }

            // 10) 주컴퓨터 신선도 알림: 냉장 냄비가 상해 가면 조리사에게 "먼저 쓰라" → 조리사가 받아들이면 다음 조리에 그 냄비부터 넣는다
            {
                var w = DayOne(seed, "Mirinae");
                var cook = w.Cooking.HeadCook!;
                var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
                Batch? flagged = null;
                int tries = 0;
                while (flagged == null && tries++ < 5)
                {
                    w.Cooking.ForceNext = "curry";
                    w.Cooking.OnCooked(cook, stove, AddMeals(w, 6));
                    var b = w.Cooking.Batches.Last(x => !x.Jar);
                    b.InFridge = true; b.Fresh = 0.55f;
                    Run(w, SimTime.Minutes(12));
                    if (b.Flagged) flagged = b;
                }
                int lo0 = w.Cooking.Stats.Leftovers;
                w.Cooking.HoursMul(stove, cook); // 조리 일감을 짤 때처럼: 무엇을 만들지 고른다
                w.Cooking.OnCooked(cook, stove, AddMeals(w, 6));
                bool used = flagged != null && !w.Cooking.Batches.Contains(flagged);
                var made = w.Cooking.Batches.Last(x => !x.Jar);
                var act = w.Automation.Book.Acts.LastOrDefault(a => a.Key.StartsWith("food:fresh"));
                var msg = w.Automation.Apps.Messages.LastOrDefault(m => m.Kind == "식단" && m.CrewId == cook.Id);
                Check("주컴퓨터 — 냄비 신선도를 읽고 조리사에게 알린다 → 조리사가 그 냄비부터 쓴다 (남은 것 요리)", act != null && msg != null && used && w.Cooking.Stats.Leftovers > lo0,
                    $"알림 {tries}번째에 받아들임 · 메시지 \"{msg?.Text}\" · 기록 [{act?.Observe} | {act?.Judge} | {act?.Act}] · 만든 것 {made.Spec.Name} {made.Portions}그릇 · {w.Cooking.Watch.Summary()}");
            }

            // 11) 오염: 지저분한 주방(균)에서 만든 냄비에 균이 들고 · 기름진 전이 화구 앞 바닥에 기름을 튀긴다 (배 본체 칸 상태)
            {
                var w = DayOne(seed, "Mirinae");
                var cook = w.Cooking.HeadCook!;
                var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
                w.Soil.RoomSoil(stove.Room)[(int)SoilKind.Bio] = 0.9f;
                int t0 = w.Ship.Containers.Sum(f => f.Storage!.Tainted);
                float oil0 = stove.UseSpots.Count > 0 ? w.Body.Mark(stove.UseSpots[0], CellMark.Oil) : 0f;
                w.Cooking.ForceNext = "pancake";
                w.Cooking.OnCooked(cook, stove, AddMeals(w, 8));
                var b = w.Cooking.Batches.Last(x => !x.Jar);
                int t1 = w.Ship.Containers.Sum(f => f.Storage!.Tainted);
                float oil = stove.UseSpots.Count > 0 ? w.Body.Mark(stove.UseSpots[0], CellMark.Oil) : 0f;
                foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.3f);
                Run(w, SimTime.Hours(2));
                Check("오염 — 지저분한 주방의 냄비에 균이 들고(보관함 식사에 균) · 전을 부치면 화구 앞 바닥에 기름이 튄다", b.Germy && t1 > t0 && oil > oil0 + 0.1f,
                    $"균 든 냄비 {b.Germy} · 균 든 식사 {t0}→{t1} · 바닥 기름 {oil0 * 100:0}→{oil * 100:0}% · 균 든 끼니 {w.Cooking.Stats.GermMeals} · 앓는 사람 {w.Crew.Count(c => c.PoisonAt >= 0)}");
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
