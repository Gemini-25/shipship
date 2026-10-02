using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v17.5 사고 뒤 며칠 · 꿈 · 장소의 기억: 그을음 냄새 → 다른 방 식사 → 돌아옴 · 냉장고 고르기 · 침구 널기 · 개인 조명 · 굳어 정식 개조 ·
//  떠난 사람의 자리 · 장소 피하기가 옅어짐 · 악몽 → 아침 기분 · 일기 · 식탁 · 이어진 흔적만 따로 · 긴 항해 뒤 흔적으로 겪은 일 맞히기 · 결정론 · 성능.
public static partial class Program
{
    private static int RunAfterTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"사고 뒤 며칠 · 꿈 · 장소의 기억 (v17.5) · 시드 {seed}\n");
        string? pick = Environment.GetEnvironmentVariable("ASEC");
        bool Do(string k) => pick == null || pick.Split(',').Contains(k);
        bool thinkWas = RoomPlanSystem.ThinkOff;

        static bool Until(World w, Func<bool> cond, long max, int step = 0)
        {
            if (step <= 0) step = SimTime.Minutes(5);
            long t0 = w.Tick;
            while (!cond()) { if (w.Tick - t0 >= max) return false; Run(w, step); }
            return true;
        }

        // ── 긴 항해 한 척: 2일 불 → 4일 물 → 5일 냉장고 → 6일 죽음 → 10일 흔적 읽기 ──
        World? v = null;
        var truth = new SortedSet<string>(StringComparer.Ordinal);
        if (Do("voyage"))
        {
            RoomPlanSystem.ThinkOff = true; // 다른 방 안건이 끼지 않게 (굳음 안건은 사고 뒤 손질이 낸다)
            v = DayOne(seed, "Hanbit");
            var w = v;
            var af = w.After;
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            // 그림 그리는 사람 · 식당 벽의 풍경화
            var painter = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).Skip(1).First();
            if (!painter.Hobbies.Contains(Hobby.Painting)) painter.Hobbies.Add(Hobby.Painting);
            var pic = w.Props.Place(Props.Get("landscape"), mess, painter, "취미로 그림");
            foreach (var c in w.Crew) if (c.Id % 3 == 0 && !c.Habits.Contains(Habit.NeatFreak)) c.Habits.Add(Habit.NeatFreak); // 코가 예민한 사람들
            // 1) 불: 식당 한가운데
            var fireCell = mess.Cells.Where(c => w.Ship.IsWalkable(c) && w.Ship.FurnitureAt(c) == null).OrderBy(c => (c.Center - mess.Cells[mess.Cells.Count / 2].Center).LengthSquared()).First();
            w.Fire.Ignite(fireCell, 0.5f);
            bool known = Until(w, () => mess.Fires > 0, SimTime.Hours(1), SimTime.Minutes(1));
            bool outFire = Until(w, () => w.Fire.CountIn(mess) == 0, SimTime.Hours(6), SimTime.Minutes(2));
            if (!outFire) w.Fire.ClearRoom(mess);
            Run(w, SimTime.Minutes(10));
            float soot0 = af.StaleSoot(mess);
            truth.Add("불");
            Check("불 뒤 — 식당에 묵은 그을음 냄새가 남는다 (방 냄새에 얹힌다)", known && soot0 > 0.4f && mess.Smell > 0.25f,
                $"불 {(known ? "알아챔" : "모름")} · {(outFire ? "꺼짐" : "직접 끔")} · 묵은 냄새 {soot0:0.00} · 방 냄새 {mess.Smell:0.00} · 남은 그을음 칸 {af.SootMarks(mess)}");
            // 다른 방에서 먹는다
            foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.5f);
            Until(w, () => af.Stats.AwayMeals >= 4, SimTime.Hours(20));
            var awayRooms = af.Setups.Where(s => s.Kind == 0).Select(s => w.Ship.Rooms[s.Room].Name).Distinct().ToList();
            Check("그을음 냄새 — 코가 예민한 사람부터 접시를 들고 다른 방에서 먹는다", af.Stats.AwayMeals >= 3 && af.Stats.AwayEaters >= 2 && awayRooms.Count >= 1,
                $"다른 방 식사 {af.Stats.AwayMeals}끼 · {af.Stats.AwayEaters}명 · 어디: {string.Join(",", awayRooms)} · 묵은 냄새 {af.StaleSoot(mess):0.00}");
            Until(w, () => af.Stats.VentBoosts >= 1, SimTime.Hours(4));
            Check("주컴퓨터 — 공기 감지기의 그을음 입자를 읽고 환기량을 올리고 공기청정기 · 벽 닦기를 부탁한다", af.Stats.VentBoosts >= 1
                && w.Automation.Book.Acts.Any(a => a.Observe.Contains("그을음 입자")),
                $"환기량 올림 {af.Stats.VentBoosts} · " + (w.Automation.Book.Acts.LastOrDefault(a => a.Observe.Contains("그을음 입자")) is ComputerAct ca ? $"{ca.Observe} / {ca.Judge} / {ca.Request}" : "기록 없음"));
            // 냄새가 빠지면 돌아온다
            long tRet = w.Tick;
            bool back = Until(w, () => af.Stats.SootReturns >= 2, SimTime.TicksPerDay * 4, SimTime.Minutes(10));
            Check("며칠 뒤 — 냄새가 빠지자 하나둘 식당으로 돌아온다", back && af.StaleSoot(mess) < 0.35f,
                $"돌아옴 {af.Stats.SootReturns}명 · 불 뒤 {(w.Tick - af.SootSince(mess)) / (float)SimTime.TicksPerDay:0.0}일 · 지금 냄새 {af.StaleSoot(mess):0.00} · 공기청정기 {w.Portable.Stats.Setups}");
            // 임시 식탁이 굳어 정식 개조로 (회의 → 공사)
            var su = af.Setups.Where(s => s.Kind == 0).OrderByDescending(s => s.Uses).FirstOrDefault();
            Until(w, () => su?.Plan >= 0, SimTime.Hours(3));
            var plan = su != null && su.Plan >= 0 ? w.RoomPlans.Plans.FirstOrDefault(p => p.Id == su.Plan) : null;
            if (plan != null && plan.State == "제안") { plan.State = "통과"; w.RoomPlans.Start(plan); }
            Check("굳음 ① — 며칠 쓰던 임시 식탁이 회의 안건이 된다 (거기서 가장 많이 먹은 사람이 낸다 · 까닭 · 공사로)", plan != null && plan.Source == "겪은 일" && plan.State is "공사" or "완료" && su!.Users.ContainsKey(plan.Proposer),
                plan == null ? $"안건 없음 · 임시 식탁 {su?.Uses}끼 {su?.Days.Count}일" : $"{w.Crew[plan.Proposer].Name}: {plan.Title} · {plan.Why} · {plan.State} · 임시 식탁 {su!.Uses}끼 {su.Days.Count}일 {su.Users.Count}명");
            // 불탄 그림을 다시 그린다
            bool burned = pic != null && !w.Props.Placed.Contains(pic);
            Until(w, () => af.Stats.Repainted > 0, SimTime.Hours(30));
            var rp = af.Repaints.FirstOrDefault();
            Check("불탄 그림 — 그리던 사람이 다시 그려 걸며 그을린 흔적을 남긴다", burned && rp != null && w.Props.Placed.Any(p => p.Id == rp.Prop && p.Label == rp.Keep),
                $"탔나 {burned} · {(rp != null ? $"{w.Crew[rp.Painter].Name}: {rp.Name} — {rp.Keep}" : $"아직 (탄 그림 {af.Burned.Count})")}");

            // 2) 물: 선실 바닥에 물 → 침구가 젖는다 → 마른 방에 널고 → 걷어 온다
            var qu = w.Ship.RoomsOf(RoomType.Quarters).First();
            w.Moisture.AddWater(qu, qu.Cells.Count * 20f * 0.08f);
            Until(w, () => af.WetBeds.Count > 0, SimTime.Hours(1));
            int wet = af.WetBeds.Count;
            Until(w, () => MoistureSystem.Depth(qu) < 0.01f, SimTime.Hours(6));
            if (MoistureSystem.Depth(qu) >= 0.01f) qu.Flood = 0f;
            truth.Add("물");
            Until(w, () => af.Stats.Hung >= 2, SimTime.Hours(18));
            var line = af.Lines.FirstOrDefault();
            var lineRoom = line != null ? w.Ship.Rooms[line.Room] : null;
            Check("물 사고 뒤 — 젖은 침구를 걷어 따뜻하고 마른 다른 방 벽에 줄을 매고 넌다", wet >= 2 && af.Stats.Hung >= 1 && lineRoom != null && lineRoom != qu,
                $"젖은 침대 {wet} · 널음 {af.Stats.Hung} · {lineRoom?.Name}({lineRoom?.Air.Temperature:0}℃ · 습도 {lineRoom?.Humidity * 100:0}% · 마르는 빠르기 {(lineRoom != null ? af.DryRate(lineRoom) : 0):0.00})");
            Until(w, () => af.Stats.Fetched >= 1, SimTime.Hours(40), SimTime.Minutes(10));
            var fl = af.Lines.FirstOrDefault(l => l.Done);
            Check("마르면 걷어 와 다시 깐다 (온도 · 습도가 걸린 시간을 정한다) · 빨랫줄 고리는 벽에 남는다", fl != null && af.Traces.Any(t => t.Kind == AfterTraceKind.DryHooks),
                fl != null ? $"{(fl.Dried - fl.Hung) / (float)SimTime.TicksPerHour:0}시간 만에 마름 · 걷음 {af.Stats.Fetched} · 잊음 {af.Stats.ForgotLines} · 젖은 채 잔 밤 {af.Stats.WetNights} · 맨 매트리스 {af.Stats.BareNights}" : $"아직 · {string.Join(" ", af.Lines.Select(l => $"{l.Wet:0.00}"))}");

            // 3) 정전 뒤 냉장고: 주방 전기가 다섯 시간 끊긴다 → 다시 돌면 골라 버린다
            Until(w, () => SimTime.HourOfDay(w.Tick) is >= 22f and < 23f, SimTime.TicksPerDay, SimTime.Minutes(10)); // 밤: 다들 잘 때
            var fridge = w.Ship.FurnitureOf(FurnitureType.Fridge).First();
            int stock = w.Ship.CountStored(ItemKind.Meal);
            var cook = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(c => c.RawSkill(Skill.Cooking)).First();
            w.Cooking.Batches.RemoveAll(b => b.InFridge && !b.Jar);
            var old = new Batch { Id = 90001, Recipe = 0, Cook = cook.Id, CookName = cook.Name, Cooked = w.Tick - SimTime.Hours(30), ReadyAt = w.Tick - SimTime.Hours(29), Quality = 0.7f, Temp = 4f, Portions = 5, Made = 6, Fresh = 0.62f, InFridge = true, Room = fridge.Room };
            var fresh = new Batch { Id = 90002, Recipe = 1, Cook = cook.Id, CookName = cook.Name, Cooked = w.Tick - SimTime.Hours(3), ReadyAt = w.Tick - SimTime.Hours(3), Quality = 0.7f, Temp = 4f, Portions = 6, Made = 6, Fresh = 0.97f, InFridge = true, Room = fridge.Room };
            w.Cooking.Batches.Add(old); w.Cooking.Batches.Add(fresh);
            foreach (var c in w.Crew) c.Needs.Food = 1f; // 밤새 아무도 냉장고를 열지 않게
            fridge.Room.PowerCut = true;
            bool stopped = Until(w, () => fridge.Machine is Machine fm && (fm.Stopped || fm.Efficiency < 0.2f), SimTime.Hours(1), SimTime.Minutes(1));
            Run(w, SimTime.Hours(5));
            fridge.Room.PowerCut = false;
            truth.Add("정전");
            Until(w, () => af.Stats.FridgeSorts > 0, SimTime.Hours(10));
            var fsort = af.Fridges.LastOrDefault();
            Check("정전 뒤 냉장고 — 주컴퓨터는 온도 기록을, 사람은 코를 믿고 냄비를 골라 버리고 남긴다 · 문에 쪽지", stopped && fsort != null && fsort.Done >= 0 && fsort.Thrown >= 1 && fsort.Thrown + fsort.Kept == 2
                && w.Automation.Book.Acts.Any(a => a.Observe.Contains("냉장고") && a.Observe.Contains("정지")) && af.Traces.Any(t => t.Kind == AfterTraceKind.FridgeNote),
                fsort == null ? $"고르기 없음 (멈춤 {stopped} · 식사 {stock} · 냉장고 {fridge.Machine?.Efficiency:0.00} · " + string.Join(" ", w.Cooking.Batches.Where(b => b.Id >= 90001).Select(b => $"{b.Id}:{b.Portions}그릇 신선 {b.Fresh:0.00} {(b.InFridge ? "냉장" : "")}{(b.Spoiled ? "상함" : "")}")) + $" · 냄비 {w.Cooking.Batches.Count})" : $"{w.Crew[Math.Max(0, fsort.By)].Name} · 버림 {fsort.Thrown} · 둠 {fsort.Kept} · 코 {fsort.ByNose} · 기록 {fsort.ByLog} · 온도 {string.Join(",", fsort.MaxTemp.Values.Select(t => $"{t:0}℃"))} · 쪽지 '{fsort.Note}'");

            // 4) 죽음: 늘 앉던 자리가 있는 사람 · 가까운 친구 (걱정 많음)
            RoomPlanSystem.ThinkOff = true;
            var victim = w.Crew.Where(c => !c.Dead && !c.IsChild && af.Peek(c)?.SeatUse.Count > 0).OrderByDescending(c => af.Peek(c)!.SeatUse.Values.Max()).ThenBy(c => c.Id).FirstOrDefault()
                         ?? w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).Last();
            var friend = w.Crew.Where(c => !c.Dead && !c.IsChild && c != victim && c != painter).OrderBy(c => c.Id).First();
            friend.ChangeAffinity(victim, 0.8f - friend.AffinityTo(victim)); victim.ChangeAffinity(friend, 0.8f - victim.AffinityTo(friend));
            if (!friend.Habits.Contains(Habit.Worrier)) friend.Habits.Add(Habit.Worrier);
            foreach (var c in w.Crew) if (!c.Dead && c != victim && c != friend && c.AffinityTo(victim) < 0.3f) c.ChangeAffinity(victim, 0.3f - c.AffinityTo(victim));
            var deathRoom = victim.Room;
            Player.AllowDeath(w, true);
            victim.Vitals.Health = 0.0005f;
            Until(w, () => victim.Dead, SimTime.Hours(1), SimTime.Minutes(1));
            Player.AllowDeath(w, false);
            truth.Add("죽음");
            var deathCell = victim.Cell;
            Run(w, SimTime.Minutes(10));
            var es = af.Seats.FirstOrDefault(s => s.Crew == victim.Id);
            var fm2 = af.Peek(friend);
            Check("떠난 사람 — 늘 앉던 의자가 빈자리로 남고 · 가까웠던 사람은 혼자 먹으려 한다", victim.Dead && es != null && fm2 != null && fm2.Stage == 2,
                $"{victim.Name} · 의자 {es?.Seat} · 늘 앉음 {string.Join(",", af.Peek(victim)?.SeatUse.Select(kv => $"{kv.Key}:{kv.Value}") ?? Array.Empty<string>())} · {friend.Name} 혼자 {fm2?.Withdraw:0.00}");
            // 장소의 기억: 친구는 쓰러진 자리를 돌아서 간다 · 사람마다 다르다
            Run(w, SimTime.Minutes(35));
            int costF = af.PlaceCost(friend, deathCell);
            var brave = w.Crew.Where(c => !c.Dead && c != friend && af.PlaceCost(c, deathCell) > 0).OrderByDescending(c => c.Traits.Bravery).FirstOrDefault();
            var timid = w.Crew.Where(c => !c.Dead && c != friend && af.PlaceCost(c, deathCell) > 0).OrderBy(c => c.Traits.Bravery).FirstOrDefault();
            bool detour = false;
            string pathNote = "";
            if (deathRoom != null && friend.Memory.Spots != null)
            {
                var pairs = new List<(Cell, Cell)>();
                for (int r0 = 2; r0 <= 4; r0++)
                    foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
                        pairs.Add((new Cell(deathCell.X - dx * r0, deathCell.Y - dy * r0), new Cell(deathCell.X + dx * r0, deathCell.Y + dy * r0)));
                foreach (var (a, b) in pairs)
                {
                    if (!w.Ship.IsWalkable(a) || !w.Ship.IsWalkable(b)) continue;
                    var plain = w.Paths.Find(a, b, PathProfile.Default);
                    var mem = w.Paths.Find(a, b, PathProfile.Default with { Spots = friend.Memory.Spots });
                    if (plain == null || mem == null || !plain.Contains(deathCell)) continue;
                    pathNote = $"그냥 {plain.Count}칸(그 자리 지남) · 기억 {mem.Count}칸(그 자리 {(mem.Contains(deathCell) ? "지남" : "안 지남")})";
                    if (plain.Contains(deathCell) && !mem.Contains(deathCell)) { detour = true; break; }
                }
            }
            Check("장소의 기억 — 쓰러진 자리를 길 고르기에서 피한다 (가까웠던 사람이 더 · 겁 많은 사람이 더)", costF > 0 && (detour || pathNote == "") && (brave == null || timid == null || af.PlaceCost(timid, deathCell) >= af.PlaceCost(brave, deathCell)),
                $"{friend.Name} 비용 {costF} · 용감한 {brave?.Name} {(brave != null ? af.PlaceCost(brave, deathCell) : 0)} · 겁 많은 {timid?.Name} {(timid != null ? af.PlaceCost(timid, deathCell) : 0)} · {pathNote}");
            // 아침: 악몽 → 기분 · 일기 · 식탁
            int diary0 = friend.Diary.Count;
            Until(w, () => af.Stats.Nightmares > 0 && af.Minds.Values.Any(m => m.Last is Dream { Woke: >= 0 }), SimTime.TicksPerDay * 2, SimTime.Minutes(10));
            var dreamer = af.Minds.Values.Where(m => m.Last is Dream { Woke: >= 0 }).OrderByDescending(m => m.Last!.Nightmare || m.Last.Grief ? 1 : 0).ThenByDescending(m => m.Last!.Woke).Select(m => w.Crew[m.Id]).FirstOrDefault();
            var dd = dreamer != null ? af.Peek(dreamer)!.Last : null;
            bool diaryHas = dreamer != null && dreamer.Diary.Any(d => d.text.Contains("꿈"));
            float fear = dd?.Mood ?? 0f; // 깬 직후의 기분
            Check("꿈 — 최근 사건으로 악몽 · 그리움 → 잠버릇(뒤척임 · 깸) · 아침 기분 · 일기", dd != null && diaryHas && fear > 0.05f && af.Stats.Dreams >= 2,
                dd == null ? $"꿈 없음 ({af.Stats.Summary()})" : $"{dreamer!.Name}: {dd.Text} ({(dd.Nightmare ? "악몽" : dd.Grief ? "그리움" : "꿈")} · {dd.Wakes}번 깸 · 옆 사람 {dd.WokeOthers.Count}) · 깬 직후 기분(두려움+슬픔) {fear:0.00} · 일기 '{dreamer.Diary.LastOrDefault(d => d.text.Contains("꿈")).text}' · 꿈 {af.Stats.Dreams} 악몽 {af.Stats.Nightmares} 그리움 {af.Stats.GriefDreams}");
            foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.45f);
            Until(w, () => af.Stats.TableTalks + af.Stats.Kept2 > 0, SimTime.TicksPerDay * 2, SimTime.Minutes(10));
            Check("아침 식탁 — 간밤 꿈 이야기 (같은 꿈 · 위로 · 놀림 · 혼자 삭임)", af.Stats.TableTalks + af.Stats.Kept2 > 0,
                $"이야기 {af.Stats.TableTalks} · 같은 꿈 {af.Stats.SharedDreams} · 놀림 {af.Stats.Teased} · 혼자 삭임 {af.Stats.Kept2} · " + string.Join(" / ", w.Log.Entries.Where(e => e.Text.StartsWith("아침 식탁")).TakeLast(2).Select(e => e.Text)));
            // 빈자리 앞 멈춤 · 혼자 먹다 돌아옴
            Until(w, () => af.Stats.Pauses > 0 && af.Stats.Alone > 0, SimTime.TicksPerDay * 2, SimTime.Minutes(10));
            Check("빈자리 — 가까웠던 사람은 그 의자 앞에서 잠깐 멈추고 컵 하나를 놓는다 · 그 의자는 비워 둔다", af.Stats.Pauses > 0 && es != null && es.Cup && (es.Active || es.ReleasedTo >= 0),
                $"멈춤 {af.Stats.Pauses} · 컵 {es?.Cup} ({(es != null && es.CupBy >= 0 ? w.Crew[es.CupBy].Name : "")}) · 비움 {es?.Active}");
            Until(w, () => af.Stats.Corner + af.Stats.BackToSeat > 0, SimTime.TicksPerDay * 4, SimTime.Minutes(10));
            Check("혼자 먹다가 → 식당 구석 → 늘 앉던 자리로 조금씩 돌아온다", af.Stats.Alone > 0 && af.Stats.Corner + af.Stats.BackToSeat > 0,
                $"혼자 {af.Stats.Alone}끼 · 구석으로 {af.Stats.Corner} · 제자리 {af.Stats.BackToSeat} · {friend.Name} {fm2?.Withdraw:0.00}");
            // 신입: 그 의자를 모른다 → 누가 그 사람 이야기를 해 준다
            string newcomerNote = "";
            if (es != null && es.Active)
            {
                var nc = w.AddSurvivor(w.Ship.RoomsOf(RoomType.Mess).First().Cells.First(c => w.Ship.IsWalkable(c) && w.Ship.FurnitureAt(c) == null));
                nc.Vitals.Health = 1f; nc.Vitals.Injury = 0f;
                var others = w.Ship.RoomsOf(RoomType.Mess).SelectMany(r => r.Furniture).Where(f => f.Type == FurnitureType.Seat && f.Id != es.Seat).ToList();
                foreach (var f in others) f.ReservedBy = victim; // 다른 의자는 잠시 맡아 둔다 (신입이 그 의자에 앉게)
                nc.Needs.Food = 0.2f;
                var teller = friend;
                teller.Needs.Food = 0.2f;
                Until(w, () => af.Stats.NewcomerTold > 0, SimTime.Hours(30), SimTime.Minutes(2));
                foreach (var f in others) if (f.ReservedBy == victim) f.ReservedBy = null;
                newcomerNote = $"신입 {nc.Name}: {nc.Job?.Label ?? "—"} · {nc.Room?.Name} · 식사 {nc.Stats.Meals} · 앉은 의자 {string.Join(",", af.Peek(nc)?.SeatUse.Select(kv => $"{kv.Key}:{kv.Value}") ?? Array.Empty<string>())}";
            }
            Check("신입 — 떠난 사람의 의자에 앉으면 가까웠던 사람이 그 사람 이야기를 해 주고 (자리를 내주거나 · 더 비워 둔다)", af.Stats.NewcomerTold > 0 || es != null && !es.Active,
                $"이야기 {af.Stats.NewcomerTold} · 내줌 {af.Stats.SeatsReleased} · 더 비움 {af.Stats.SeatsKept} · {newcomerNote} · " + string.Join(" / ", w.Log.Entries.Where(e => e.Text.Contains("신입") && e.Text.Contains("자리")).TakeLast(1).Select(e => e.Text)));
            // 장소 피하기가 옅어진다
            Until(w, () => w.Tick >= SimTime.TicksPerDay * 10, SimTime.TicksPerDay * 6, SimTime.Hours(1));
            int costLater = af.PlaceCost(friend, deathCell);
            Check("며칠 뒤 — 장소 피하기가 옅어진다 (사람마다 다른 빠르기)", costF > 0 && costLater < costF,
                $"{friend.Name} {costF} → {costLater} ({(w.Tick - es?.Since ?? 0) / (float)SimTime.TicksPerDay:0.0}일 · 꽃 {af.Stats.Flowers})");
            // 굳음 ②: 며칠 뒤 공사가 끝나 정식 식탁 자리
            Until(w, () => plan == null || plan.State is "완료" or "중단", SimTime.TicksPerDay * 2, SimTime.Minutes(30));
            Run(w, SimTime.Hours(1.1f));
            var corner = af.Traces.FirstOrDefault(t => t.Kind == AfterTraceKind.DiningCorner);
            var cloth = plan != null ? w.Ship.Rooms[plan.RoomId].Decor.FirstOrDefault(d => d.Spec.Id == "tablecloth") : null;
            Check("굳음 ② — 공사가 끝나 정식 식탁 자리로 꾸며지고 흔적으로 남는다", plan != null && plan.State == "완료" && cloth != null && corner != null,
                plan == null ? "안건 없음" : $"{plan.State} · {cloth?.Name} · {corner?.Text}"
                + (plan.State != "완료" ? " · 단계 " + string.Join(" ", w.RoomPlans.Tasks.Where(t => t.PlanId == plan.Id).Select(t => $"{t.Kind}{(t.Done ? "✓" : "")}/L{t.Leader}/p{t.Progress:0.0}")) + $" · 낸 사람 {w.Crew[plan.Proposer].Name}: {w.Crew[plan.Proposer].Job?.Label} · 고른 일 {w.RoomPlans.Choose(w.Crew[plan.Proposer], w.Paths.Flood(w.Crew[plan.Proposer].Cell, w.Crew[plan.Proposer].PathProfile))?.Why ?? "없음"} · 방공사 점수 {new RoomWorkActivity().Score(w.Crew[plan.Proposer], w, w.Paths.Flood(w.Crew[plan.Proposer].Cell, w.Crew[plan.Proposer].PathProfile)).Item1:0.00}" : ""));
            RoomPlanSystem.ThinkOff = thinkWas;
        }

        // ── 개인 조명: 정전된 식당의 작업등이 눈부시다 → 선실의 독서등을 가져온다 → 고치면 도로 / 두기 ──
        if (Do("lamp"))
        {
            var w = DayOne(seed, "Hanbit");
            var af = w.After;
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            var reader = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Bed != null).OrderBy(c => c.Id).First();
            foreach (var c in w.Crew) c.Habits.Remove(Habit.Generous); foreach (var c in w.Crew) { c.Habits.Remove(Habit.Cheerful); c.Habits.Remove(Habit.Optimist); }
            if (!reader.Habits.Contains(Habit.Bookworm)) reader.Habits.Add(Habit.Bookworm);
            if (af.LampOf(reader) == null) w.Props.Place(Props.Get("readlamp"), reader.Bed!.Room, reader, "처음부터", near: reader.Bed.Cells[0]);
            var lampProp = af.LampOf(reader);
            mess.PowerCut = true;
            Until(w, () => w.Portable.Devices.Any(d => d.Kind == PortableKind.WorkLamp && d.Running && d.Placed && w.Ship.RoomAt(d.At) == mess), SimTime.Hours(4));
            reader.Needs.Food = 0.25f;
            foreach (var c in w.Crew) c.Needs.Food = MathF.Min(c.Needs.Food, 0.4f);
            bool moved = Until(w, () => af.Stats.LampMoves > 0, SimTime.Hours(20));
            var lm = af.Lamps.FirstOrDefault();
            Check("임시 조명 — 작업등이 차고 눈부시다 → 선실의 독서등을 가져와 식탁에 켠다", moved && lm != null && lampProp != null && lampProp.RoomId == mess.Id,
                lm == null ? $"없음 · 작업등 {w.Portable.Devices.Count(d => d.Kind == PortableKind.WorkLamp && d.Running)} · 눈부심 {af.Glaring(mess)}" : $"{w.Crew[lm.Owner].Name} · {lampProp?.Name} → {w.Ship.Rooms[lampProp!.RoomId].Name} · 눈부심 {af.Stats.Glare}");
            mess.PowerCut = false;
            Until(w, () => af.Stats.LampBack + af.Stats.LampStays > 0, SimTime.Hours(36));
            Check("조명이 돌아오면 — 주인이 도로 가져가거나 그대로 두기로 한다", af.Stats.LampBack + af.Stats.LampStays > 0,
                $"도로 {af.Stats.LampBack} · 그대로 {af.Stats.LampStays} · 등 자리 {(lampProp != null ? w.Ship.Rooms[lampProp.RoomId].Name : "?")}"
                + (lm != null ? $" · 위기 {Crisis.Acting(w)} · 할 일 {af.Pick(w.Crew[lm.Owner], w.Paths.Flood(w.Crew[lm.Owner].Cell, w.Crew[lm.Owner].PathProfile))?.Why ?? "없음"} · 옮김 {lm.Moved} 돌려줌 {lm.Returned} 원함 {lm.WantBack} 둠 {lm.Stays} 다시 밝음 {lm.LitAgain} 들고 {lm.Carrying} 맡음 {lm.ClaimedBy} · 주인 {w.Crew[lm.Owner].Name}: {w.Crew[lm.Owner].Job?.Label} · 식당 전기 {mess.Powered} 조명 {!mess.LightsOut}" : ""));
        }

        // ── 긴 항해 뒤 흔적 목록으로 겪은 일을 맞힌다 ──
        if (v != null)
        {
            var w = v;
            var af = w.After;
            if (w.History.Fires > 0) truth.Add("불");
            if (w.History.Deaths > 0) truth.Add("죽음");
            if (w.Major.Traces.Count > 0) truth.Add("큰 사고");
            var list = af.TraceList();
            var guess = af.Infer();
            int stains = list.Count(l => !l.Linked), linked = list.Count(l => l.Linked);
            Console.WriteLine("  흔적 목록:");
            foreach (var l in list.Take(24)) Console.WriteLine($"    {(l.Linked ? "·" : "□")} {l.Text}");
            bool recall = truth.All(guess.Contains);
            bool precise = guess.All(g => truth.Contains(g) || g == "정전" && w.Fixtures.LightFailures > 0 || g == "물" && w.Moisture.Stats.Floods > 0);
            Check("흔적 — 일반 얼룩은 방마다 묶고 사람 · 물건 · 사건과 이어진 흔적만 따로", stains >= 1 && linked >= 5 && list.Where(l => !l.Linked).Select(l => l.Room).Distinct().Count() == stains,
                $"묶은 얼룩 {stains}줄 · 이어진 흔적 {linked}줄");
            Check($"긴 항해({SimTime.Day(w.Tick)}일) 뒤 — 흔적과 생활 패턴만 보고 겪은 일을 맞힌다", recall && precise,
                $"겪은 일 {string.Join(",", truth)} · 짐작 {string.Join(",", guess)}");
            Console.WriteLine($"  {af.Stats.Summary()}");
        }

        // ── 손대지 않은 10일 항해 (이야기꾼의 사고만): 흔적으로 짐작한 것은 모두 실제로 겪은 일이다 ──
        if (Do("natural"))
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Player.AllowDeath(w, true);
            Run(w, SimTime.TicksPerDay * 10);
            var af = w.After;
            var real = new SortedSet<string>(StringComparer.Ordinal);
            if (w.History.Fires > 0) real.Add("불");
            if (w.History.Deaths > 0) real.Add("죽음");
            if (w.Major.Traces.Count > 0) real.Add("큰 사고");
            if (w.Moisture.Stats.Floods > 0 || af.Stats.WetBeds > 0) real.Add("물");
            if (w.Fixtures.LightFailures > 0 || af.Fridges.Count > 0 || w.Scale.Cases.Any(k => k.KindsSeen.Contains(CauseKind.Outage))) real.Add("정전");
            var guess = af.Infer();
            Check("손대지 않은 10일 항해 — 흔적으로 짐작한 일은 모두 실제로 겪은 일이다 (지어내지 않는다)", guess.All(real.Contains),
                $"겪은 일 {string.Join(",", real)} · 짐작 {string.Join(",", guess)} · 흔적 {af.TraceList().Count}줄 · {af.Stats.Summary()}");
        }

        // ── 결정론 ──
        if (Do("det"))
        {
            uint H()
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                var mess = w.Ship.RoomsOf(RoomType.Mess).First();
                w.Fire.Ignite(mess.Cells[mess.Cells.Count / 2], 0.4f);
                Run(w, SimTime.Hours(14));
                return SaveGame.StateHash(w);
            }
            uint a = H(), b = H();
            Check("결정론 — 같은 시드면 같은 사고 뒤 며칠", a == b, $"{a:x8} / {b:x8}");
        }

        // ── 성능: 30인 배 하루 ──
        if (Do("perf"))
        {
            double Day(bool off)
            {
                AftermathSystem.Off = off;
                var w = World.CreateDefault(seed, 0, "Cheonma");
                Run(w, SimTime.Hours(2));
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                AftermathSystem.Off = false;
                return sw.Elapsed.TotalSeconds;
            }
            double t0 = Math.Min(Day(true), Day(true)), t1 = Math.Min(Day(false), Day(false));
            Console.WriteLine($"  성능 (30인 하루): 끔 {t0:0.0}초 · 켬 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%)");
            Check("성능 · 30인 배 하루가 크게 늘지 않는다", t1 < t0 * 1.1, $"{t0:0.0} → {t1:0.0}초");
        }

        RoomPlanSystem.ThinkOff = thinkWas;
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
