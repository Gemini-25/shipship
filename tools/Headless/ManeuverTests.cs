using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ShipSim.Core;



// v17.0 기동 · 충격과 고정 점검 (--maneuvertest).
public static partial class Program
{
    private static Cell MvFloor(World w, Room r, Vector2 near) =>
        r.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.DoorAt(x) == null && !w.Portable.Occupied(x)).OrderBy(x => (x.Center - near).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).First();

    private static void MvSleep(World w, CrewMember c)
    {
        if (c.Bed?.UseSpots.FirstOrDefault() is Cell s && s != default) Teleport(w, c, c.Bed.Cells[0]);
        Force(w, c, new Job(null, "시험: 잠", new Toil[] { new WaitToil(SimTime.Hours(6), Pose.Sleeping) }) { InterruptMargin = 9f }, SimTime.Hours(6));
    }

    private static int RunManeuverTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"기동 · 충격과 고정 점검 (시드 {seed})");

        // ── 1) 묶음 검증 장면: 야간 근무자 몫 수프를 데우던 중 회피 기동 경보 ──
        {
            var w = DayOne(seed, "Mirinae");
            var ms = w.Maneuver;
            var stove = w.Ship.FurnitureOf(FurnitureType.Stove).First();
            var kitchen = stove.Room;
            var mess = w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault() ?? kitchen;
            var cook = w.Cooking.HeadCook!;
            var adults = w.Crew.Where(c => !c.Dead && !c.IsChild && c != cook).OrderBy(c => c.Id).ToList();
            var mech = adults.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).ThenBy(c => c.Id).First();
            var helper = adults.Where(c => c != mech && !Life.Has(c, Habit.Forgetful)).OrderBy(c => c.Id).First();
            w.Cooking.ForceNext = "vegsoup";
            w.Cooking.OnCooked(cook, stove, 6);
            var soup = w.Cooking.Batches.Last(x => !x.Jar);
            soup.Temp = 32f; // 저녁에 만든 것이 식어 있었다
            // 조리사는 화구 앞, 다른 사람은 이동식 히터 곁 (추운 밤)
            Teleport(w, cook, ms.Near(stove, cook)!.Value);
            Force(w, cook, new Job(null, "시험: 화구 앞", new Toil[] { new WaitToil(SimTime.Hours(1), Pose.Working, stove.Center) }), SimTime.Hours(1));
            ms.Warm(cook, stove, mech, soup);
            var heater = w.Portable.Devices.First(d => d.Kind == PortableKind.Heater);
            var hcell = MvFloor(w, kitchen, stove.Center + new Vector2(2.5f, 1f));
            w.Portable.PlaceNow(heater, hcell, helper, "cold:" + kitchen.Id, outlet: kitchen);
            Teleport(w, helper, MvFloor(w, kitchen, hcell.Center + new Vector2(1f, 0f)));
            Force(w, helper, new Job(null, "시험: 히터 곁에서 손 녹이기", new Toil[] { new WaitToil(SimTime.Hours(1), Pose.Standing, hcell.Center) }), SimTime.Hours(1));
            // 정비사는 엔진실에서 밤일 (늦게 온다)
            var engine = w.Ship.RoomsOf(RoomType.Engine).FirstOrDefault() ?? w.Ship.RoomsOf(RoomType.Workshop).First();
            Teleport(w, mech, MvFloor(w, engine, engine.Center));
            Force(w, mech, new Job(null, "시험: 엔진실 밤일", new Toil[] { new WaitToil(SimTime.Hours(3), Pose.Working, engine.Center) }) { InterruptMargin = 9f }, SimTime.Hours(3));
            mech.Needs.Food = 0.7f;
            // 식탁에 두고 간 컵 셋 (주인들은 침실)
            var table = mess.Furniture.First(f => f.Type == FurnitureType.Table);
            int cups = 0;
            foreach (var tc in w.Info.OnTable.Values.ToList()) { w.Info.OnTable.Remove(tc.Cup); if (w.Belongings.Get(tc.Cup) is Belonging ob) ob.At = null; }
            var spots = table.UseSpots.Concat(table.Cells).Distinct().ToList();
            foreach (var owner in adults.Where(c => c != mech && c != helper).Take(3))
            {
                var cup = w.Belongings.All.FirstOrDefault(b => b.Owner == owner.Id && b.Kind == BelongingKind.Mug && b.Usable);
                if (cup == null) continue;
                w.Info.PutOnTable(owner, cup, table.Cells[cups % table.Cells.Count], spots[cups % spots.Count], left: true);
                MvSleep(w, owner);
                cups++;
            }
            Run(w, SimTime.Minutes(10));
            float warmTemp = soup.Temp;
            var m = ms.Begin(ManeuverKind.Evasion, 3f, 0.45f, "자동 조종");
            var plans = ms.Preps.ToDictionary(kv => kv.Key, kv => string.Join(">", kv.Value.Select(x => x.Kind)));
            var bc = w.Automation.Speak.Recent.LastOrDefault();
            Console.WriteLine($"   조리사 {cook.Name}({cook.Room?.Name} · {cook.ActivityLabel} · {cook.Pose}) · 히터 곁 {helper.Name}({helper.Room?.Name}) · 정비사 {mech.Name}");
            Console.WriteLine($"   방송: \"{bc?.Text}\" · 들은 사람 {bc?.HeardBy.Count} · 준비 {string.Join(" / ", plans.OrderBy(kv => kv.Key).Select(kv => $"{w.Crew[kv.Key].Name}:{kv.Value}"))}");
            Check("주컴퓨터가 \"3분 뒤 회피 기동\"을 방송하고 들은 사람마다 준비가 다르다 (하던 일 · 성격 · 습관)",
                bc != null && bc.Text.Contains("3분 뒤") && bc.Text.Contains("회피 기동") && plans.Values.Distinct().Count() >= 3 && plans.ContainsKey(cook.Id) && plans.ContainsKey(helper.Id),
                $"\"{bc?.Text}\" · 준비 {plans.Count}명 · 서로 다른 순서 {plans.Values.Distinct().Count()}");
            for (int t = 0; t < SimTime.Minutes(6) && !m.Over; t++) w.Step();
            bool clamped = m.Held >= 1 && m.Spilled == 0 && ms.ClampedBy.GetValueOrDefault(stove.Id, -1) == cook.Id;
            Check("조리사는 냄비에 집게를 물린다 → 기동에도 수프가 쏟아지지 않는다", clamped && soup.Portions > 0,
                $"버틴 냄비 {m.Held} · 쏟음 {m.Spilled} · 집게 {(ms.ClampedBy.TryGetValue(stove.Id, out var cb2) ? w.Crew[cb2].Name : "없음")} · 남은 {soup.Portions}접시 · 데우던 온도 {warmTemp:0}℃");
            Check("다른 사람은 이동식 히터를 끈다 → 넘어져도 바닥이 그을리지 않는다", ms.Stats.HeatersOff >= 1 && !heater.On && w.Body.Mark(hcell, CellMark.Soot) < 0.1f,
                $"끈 사람 {(ms.HeaterOffBy.TryGetValue(heater.Id, out var hb) ? w.Crew[hb].Name : "없음")} · 히터 {(heater.On ? "켜짐" : "꺼짐")} · 넘어짐 {ms.Tipped.ContainsKey(heater.Id)} · 그을음 {w.Body.Mark(hcell, CellMark.Soot):0.00}");
            int shards = ms.Shards.Count(s => w.Ship.RoomAt(s.At) == mess);
            Check("식탁 컵이 떨어져 깨지고 조각이 바닥에 남는다 (아무도 바로 치우지 않는다)", m.CupsFell >= 1 && shards >= 1,
                $"떨어진 컵 {m.CupsFell}/{cups} · 깨진 조각 자리 {shards} · 앉음 {ms.Stats.Sat} · 손잡이 {ms.Stats.Gripped} · 넘어짐 {m.Fell}");
            // 기동 뒤: 조리사가 집게를 풀고, 데우다 만 수프를 정비사 몫으로 덜어 둔다
            Plate? plate = null;
            float tempAtFind = -1f;
            void Tick() { bool before = plate?.Found ?? false; w.Step(); if (plate != null && !before && plate.Found) tempAtFind = plate.Temp; }
            for (int t = 0; t < SimTime.Minutes(60) && plate == null; t++) { w.Step(); plate = w.Cooking.PlateFor(mech); }
            Check("기동이 끝나면 조리사가 집게를 풀고 데우다 만 수프를 정비사 몫으로 덜어 이름표를 붙인다", plate != null && plate.By == cook.Id && ms.Stats.PlatesKept >= 1,
                $"접시 {(plate != null ? $"{plate.Temp:0}℃ · 덜어 둔 사람 {w.Crew[plate.By].Name}" : "없음")} · 집게 풂 {ms.Stats.Unclamped} · {IfLog(w, cook.Id, "집게")}");
            // 식사를 마친 사람이 빗자루로 조각을 쓴다
            var diner = adults.Where(c => c != mech && c != helper && !w.Info.OnTable.Values.Any(t => t.User == c.Id) && c.CanAct).OrderBy(c => c.Id).FirstOrDefault() ?? helper;
            Teleport(w, diner, MvFloor(w, mess, table.Center));
            diner.Needs.Food = 0.15f;
            Run(w, 2);
            var eatJob = Brain.Activities.OfType<EatActivity>().First().Plan(diner, w, w.Paths.Flood(diner.Cell, diner.PathProfile));
            if (eatJob != null) Force(w, diner, eatJob, SimTime.Hours(1)); else IfIdle(w, diner);
            SpQuiet(w, new[] { diner }, SimTime.Hours(3)); // 통합8 지나가던 다른 사람이 먼저 쓸어 담으면 밥 먹은 사람이 쓸 조각이 없다 — 식당엔 그 사람뿐
            int swept0 = ms.Stats.Swept;
            for (int t = 0; t < SimTime.Hours(3) && ms.Stats.Swept == swept0; t++) Tick();
            var sweepLog = w.Log.Entries.LastOrDefault(e => e.Text.Contains("빗자루로 깨진")).Text ?? "";
            Check("식사를 마친 사람이 빗자루로 깨진 컵 조각을 쓸어 담는다", ms.Stats.Swept > swept0 && sweepLog.Contains("밥을 다 먹고"),
                $"쓸어 냄 {ms.Stats.Swept - swept0} · \"{sweepLog}\" · {diner.Name}({diner.ActivityLabel})");
            // 늦게 온 정비사: 식은 수프가 제 몫인 걸 알고 고마워한다
            if (mech.Job != null) mech.EndJob(w, ToilStatus.Interrupted);
            mech.NextThinkTick = w.Tick + 1;
            mech.Needs.Food = 0.12f;
            mech.Needs.Rest = MathF.Max(mech.Needs.Rest, 0.85f); // 밤일을 막 끝낸 참 — 먹고 잔다
            for (int t = 0; t < SimTime.Hours(8) && plate != null && !(plate.Found && ms.Stats.Thanked > 0 && (plate.Eaten || t > SimTime.Hours(1))); t++) Tick();
            var mem = w.Relations.Of(mech, cook).FirstOrDefault(x => x.Text.Contains("수프 냄비"));
            Check("늦게 온 정비사가 식은 수프가 제 몫인 걸 알고 고마워한다 (관계 · 일기)", plate is { Found: true } && tempAtFind is >= 0f and < 45f && ms.Stats.Thanked >= 1 && mem != null,
                $"접시 찾음 {plate?.Found} · 먹음 {plate?.Eaten} · 상함 {plate?.Spoiled} · {mech.Name}({mech.ActivityLabel} · 배고픔 {mech.Needs.Food:0.00}) · 찾을 때 {tempAtFind:0}℃ · 고맙다 {ms.Stats.Thanked} · 기억 \"{mem?.Text}\" · 일기 \"{mech.Diary.LastOrDefault().text}\"");
            Console.WriteLine($"   {ms.Stats.Line()}");
        }

        // ── 2) 예고 없는 충격: 고정 안 된 것만 떨어진다 · 잠든 사람은 침대 끈 ──
        int firstDrops;
        World w2;
        {
            var w = w2 = DayOne(seed, "Hanbit");
            var ms = w.Maneuver;
            var shelves = w.Ship.Furniture.Where(f => ManeuverSystem.Shelfish(f.Type) && !f.Room.Detached).OrderBy(f => f.Id).ToList();
            var tight = shelves[0];
            ms.Latched.Add(tight.Id);
            var cart = w.Portable.Devices.FirstOrDefault(d => d.Kind == PortableKind.Cart);
            var room = w.Ship.RoomsOf(RoomType.Storage).FirstOrDefault() ?? w.Ship.RoomsOf(RoomType.Workshop).First();
            var dir = new Vector2(-1, 0); // 배가 왼쪽으로 밀린다 → 물건은 오른쪽으로
            Cell cartAt = default;
            if (cart != null) { cartAt = MvFloor(w, room, room.Center); w.Portable.PlaceNow(cart, cartAt, null, "test"); ms.Strapped.Add(cart.Id); }
            // 바닥 물건 둘: 하나는 끈으로 묶고 하나는 그냥 (오른쪽이 트인 칸)
            var free = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsOpenFloor(x + new Cell(1, 0)) && w.Ship.IsOpenFloor(x + new Cell(2, 0)) && x != cartAt && !w.Matter.Any(x)).OrderBy(x => x.X * 1000 + x.Y).ToList();
            var loose = w.Matter.Add(ArticleKind.WaterJug, free[0], "시험");
            var tied = w.Matter.Add(ArticleKind.WaterJug, free[^1], "시험");
            tied.Fixed = true;
            var looseAt = loose.At; var tiedAt = tied.At;
            // 잠든 두 사람: 한 사람은 침대 끈, 한 사람은 그냥
            var sleepers = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Bed != null).OrderBy(c => c.Id).Take(2).ToList();
            foreach (var s in sleepers) MvSleep(w, s);
            Run(w, 2);
            var strapped = sleepers[0];
            if (ms.BedUnder(strapped) is Furniture bed) ms.Bunks.Add(bed.Id);
            var hpBefore = strapped.Vitals.Injury;
            int fell0 = ms.Stats.Fell;
            ms.Shock(0.8f, dir, null, "운석 충돌", warned: false);
            var m = ms.Recent[^1];
            firstDrops = m.Dropped;
            int fromTight = ms.Fallen.Count(f => f.From == tight.Id);
            int fromLoose = ms.Fallen.Count(f => f.From != tight.Id);
            Check("예고 없는 충격 — 걸쇠 건 선반 · 끈 맨 카트 · 묶은 물통은 버티고, 안 건 선반 · 풀린 물통만 떨어지고 미끄러진다",
                fromTight == 0 && fromLoose >= 1 && loose.At != looseAt && tied.At == tiedAt && tied.Fixed && (cart == null || cart.At == cartAt),
                $"걸쇠 선반 {fromTight} · 다른 선반 {fromLoose}(깨짐 {m.Broken}) · 물통 {looseAt}→{loose.At} / 묶은 것 {tiedAt}→{tied.At} · 카트 {(cart == null ? "없음" : cart.At == cartAt ? "그대로" : "움직임")} · 버팀 {m.Fast}");
            bool tumbled = ms.Tumbles.Any(t => t.Crew == strapped.Id);
            Check("잠든 사람은 침대 끈 덕에 안전하다 (끈 없는 사람은 굴러떨어질 수 있다)", !tumbled && strapped.Vitals.Injury <= hpBefore + 0.001f && ms.Stats.SleepersSafe >= 1,
                $"끈 맨 {strapped.Name}: 굴러떨어짐 {tumbled} · 안전 {ms.Stats.SleepersSafe} · 끈 없는 {sleepers[1].Name}: {(ms.Tumbles.Any(t => t.Crew == sleepers[1].Id) ? "떨어졌다" : "버텼다")}");
            Check("서 있던 사람은 넘어진다 (가끔 다친다) — 그 순간에만 판정", ms.Stats.Fell > fell0,
                $"넘어짐 {ms.Stats.Fell - fell0} · 다침 {m.Hurt} · 버틴 사람 {m.Safe}");
            // 넘어진 사람은 주저앉았다 일어나고, 뒤처리(줍기 · 점검)가 이어진다
            Run(w, SimTime.Hours(3));
            Check("뒤처리 — 떨어진 것을 주워 제자리에 · 걸쇠 점검 · 넘어진 사람은 일어난다", ms.Stats.Picked >= 1 && ms.Stats.Checked >= 1 && ms.Tumbles.All(t => t.Up),
                $"주움 {ms.Stats.Picked} · 점검 {ms.Stats.Checked}(조임 {ms.Stats.Refixed}) · 남은 것 {ms.Fallen.Count(f => !f.Broken)} · 제자리 {ms.Stats.Righted}");
        }

        // ── 3) 여러 번 겪으면 고정하는 관행이 생긴다 ──
        {
            var w = w2;
            var ms = w.Maneuver;
            for (int k = 0; k < 3 && w.Culture.Of(CustomKind.StowAway) == null; k++)
            {
                ms.Latched.Clear();
                ms.Shock(0.8f, new Vector2(k % 2 == 0 ? 1 : -1, 0), null, "작은 운석", warned: false);
                Run(w, SimTime.Hours(2));
            }
            var cu = w.Culture.Of(CustomKind.StowAway);
            int latched0 = ms.Latched.Count;
            int shelves = w.Ship.Furniture.Count(f => ManeuverSystem.Shelfish(f.Type) && !f.Room.Detached);
            // 통합8 쓰고 난 선반에만 거니 선반 반에 걸리기까지 열 시간이 빠듯하다 (14/30) — 반에 걸릴 때까지 (하루 안)
            for (int h = 0; h < 24 && (h < 10 || ms.Latched.Count < shelves / 2); h++) Run(w, SimTime.Hours(1));
            int dropsBefore = ms.Stats.Dropped;
            ms.Shock(0.8f, new Vector2(-1, 0), null, "운석 충돌", warned: false);
            int after = ms.Stats.Dropped - dropsBefore;
            Check("여러 번 잃고 나면 배에 관행이 생긴다 → 쓰고 난 선반에 걸쇠 · 다음 충격에 덜 떨어진다", cu != null && ms.Stats.HabitLatches + ms.Stats.Checked >= 1 && ms.Latched.Count >= shelves / 2 && after < firstDrops,
                $"관행 {(cu != null ? $"\"{CultureSystem.Name(cu.Kind)}\" ({cu.Origin}) · 따름 {cu.Followers.Count}" : "없음")} · 걸쇠 {latched0}→{ms.Latched.Count}/{shelves} (습관 {ms.Stats.HabitLatches}) · 떨어짐 처음 {firstDrops} → 지금 {after}");
        }

        // ── 4) 화물 무게중심이 기동 성능에 (컴퓨터가 계산 · 사람이 옮긴다) ──
        {
            var w = DayOne(seed, "Hanbit");
            var ms = w.Maneuver;
            ms.Trim();
            float t0 = ms.TrimMul, thrust0 = w.Propulsion.Thrust, imb0 = ms.Imbalance;
            // 한쪽 창고로 몰아넣는다
            var boxes = w.Ship.Containers.OrderBy(f => f.Id).ToList();
            var axis = new Vector2(1, 0);
            var right = boxes.Where(f => f.Storage!.Free > 0).OrderByDescending(f => Vector2.Dot(f.Center - ms.ShipCenter, axis)).ThenBy(f => f.Id).ToList();
            foreach (var from in boxes.OrderBy(f => Vector2.Dot(f.Center - ms.ShipCenter, axis)).ThenBy(f => f.Id))
                foreach (var to in right)
                {
                    if (Vector2.Dot(to.Center - from.Center, axis) <= 0.5f) continue;
                    foreach (var (k, n) in from.Storage!.Contents.ToList<(ItemKind kind, int count)>()) { int took = from.Storage.Take(k, n); int put = to.Storage!.Add(k, took); if (put < took) from.Storage.Add(k, took - put); }
                }
            ms.Trim();
            float t1 = ms.TrimMul, thrust1 = w.Propulsion.Thrust, imb = ms.Imbalance;
            var advice = w.Automation.Book.Acts.LastOrDefault(a => a.Key == "maneuver:trim");
            Check("화물이 한쪽으로 쏠리면 컴퓨터가 계산한다 — 기동 추력이 줄고 옮기자고 한다", t1 < t0 - 0.03f && thrust1 < thrust0 && ms.Rebalance != null && advice != null,
                $"처음 {t0:0.00}(쏠림 {imb0:0.00}) → 몰아넣은 뒤 {t1:0.00}(쏠림 {imb:0.00}) · 추력 {thrust0 * 100:0}% → {thrust1 * 100:0}% · 화물 {ms.CargoKg:0}kg · \"{advice?.Observe}\" / \"{advice?.Judge}\"");
            Run(w, SimTime.Hours(8));
            Check("사람들이 짐을 옮기면 무게중심이 돌아오고 추력이 는다", ms.Stats.Rebalanced >= 1 && ms.TrimMul > t1,
                $"옮김 {ms.Stats.Rebalanced} · 추력 배율 {t1:0.00} → {ms.TrimMul:0.00} · {IfLog(w, -1, "무게중심을 다시")}");
        }

        // ── 5) 운석 경보: 컴퓨터가 점화를 늦춰 붙잡을 틈을 준다 (Propulsion) ──
        {
            var w = DayOne(seed, "Hanbit");
            var ms = w.Maneuver;
            w.Propulsion.Place(ZoneKind.Debris);
            var hull = w.Ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Key).OrderBy(c => c.X * 1000 + c.Y).First();
            var met = w.Sensors.Launch(hull, 0.3f);
            Maneuver? m = null;
            for (int t = 0; t < SimTime.Minutes(8) && m == null; t++) { w.Step(); m = ms.Recent.LastOrDefault(x => x.Kind == ManeuverKind.Evasion); }
            float lead = m != null ? (m.Ignite - m.Start) / (float)SimTime.Minutes(1) : -1f;
            Check("운석 경보 — 주컴퓨터가 운석까지 남은 시간을 재서 점화를 늦추고 방송한다 (같은 기동이 물건 · 사람에)", m != null && m.Announced && lead >= 1.5f && ms.Stats.Holds >= 1,
                $"점화까지 {lead:0.0}분 · 방송 {m?.Announced} · \"{ms.LastHold}\" · 들은 사람 {m?.Knew.Count}");
        }

        // ── 6) 결정론 · 성능 ──
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 — 같은 시드 두 번이 같다", a == b, $"{a:x8} / {b:x8}");
            double Time(bool off)
            {
                ManeuverSystem.Off = off;
                var w = World.CreateDefault(seed, 0, ShipGenerator.KeyFor(30, seed));
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                sw.Stop();
                ManeuverSystem.Off = false;
                return sw.Elapsed.TotalSeconds;
            }
            ManeuverSystem.UpdateTicks = 0;
            double off1 = Time(true), on1 = Time(false);
            double ratio = on1 / off1;
            if (ratio > 1.1) { double off2 = Time(true), on2 = Time(false); ratio = Math.Min(on1, on2) / Math.Min(off1, off2); }
            double own = ManeuverSystem.UpdateTicks * 1000.0 / Stopwatch.Frequency;
            Check("성능 — 30명 배 하루가 10% 안쪽으로 느려진다", ratio <= 1.10, $"{ratio:0.000}배 (끔 {off1:0.0}초 · 켬 {on1:0.0}초 · 이 시스템 틱 {own:0}ms)");
        }

        Console.WriteLine(_fails == 0 ? "\n기동 점검 통과" : $"\n기동 점검 실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
