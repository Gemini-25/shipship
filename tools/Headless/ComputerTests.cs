using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.0 ④ · v16.6 주컴퓨터: 상시 카드 · 다섯 칸 기록(채점) · 하루 보고 · 컴퓨터가 믿는 배 · 제안 → 승인 · 사람마다 신뢰 · 방송 · 재부팅 · 연산 자원 · 새 모듈
public static partial class Program
{
    private static void Put(World w, CrewMember c, Room room, bool last = false)
    {
        c.EndJob(w, ToilStatus.Interrupted);
        var cells = room.Cells.Where(w.Ship.IsOpenFloor).ToList();
        c.Position = (last ? cells.Last() : cells.First()).Center;
        c.PreviousPosition = c.Position;
    }

    private static int RunComputerTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"주컴퓨터 점검 (v16.0 ④ · v16.6) · 시드 {seed}\n");
        try
        {
            // 1) 화재 때 상시 카드 한 줄 · 다섯 칸 기록이 채점된다
            {
                var w = DayOne(seed, "Mirinae");
                var room = StoreRoom(w);
                ClearRoom(w, room);
                var c = w.Crew.First(x => x.CanAct && x.IsAwake);
                Put(w, c, room);
                BigFire(w, room, 6);
                string seen = "";
                for (int i = 0; i < 120 && seen == ""; i++)
                {
                    Run(w, World.SystemInterval);
                    string now = w.Automation.NowLine;
                    if (now.Contains("화재") && now.Contains("댐퍼 폐쇄") && now.Contains("대피 기다림")) seen = now;
                }
                Check("상시 카드 — 화재 때 \"방 화재 · 댐퍼 폐쇄 · 대피 기다림 n초\"", seen.StartsWith(room.Name), $"\"{seen}\" · 지금: {w.Automation.NowLine}");
                Run(w, SimTime.Minutes(40));
                var book = w.Automation.Book;
                var full = book.Acts.FirstOrDefault(a => a.Kind == ActKind.Suppress && a.Observe != "" && a.Judge != "" && a.Act != "" && a.Request != "" && a.Graded && a.Result != "");
                int graded = book.Acts.Count(a => a.Graded);
                Check("다섯 칸 기록 — 소화 조치가 관찰 · 판단 · 조치 · 요청 · 결과로 남고 몇 분 뒤 채점된다", full != null && graded >= 3,
                    full != null ? $"[{full.Kind}] 관찰 {full.Observe} | 판단 {full.Judge} | 조치 {full.Act} | 요청 {full.Request} | 결과 {full.Result} · 채점 {graded}/{book.Acts.Count} (맞음 {book.Right} · 틀림 {book.Wrong})" : $"기록 {book.Acts.Count} · 채점 {graded} · " + string.Join(" / ", book.Acts.Take(4).Select(a => $"{a.Kind}:{a.Act}:{a.Result}")));
                Console.WriteLine($"    카드: {seen}");
            }

            // 2) 하루 보고 · 새 모듈이 실제 물건을 만든다 · 방송은 들은 방 사람만
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                foreach (var m in new[] { ComputerModule.PowerShare, ComputerModule.WaterPlan, ComputerModule.FatigueAlert, ComputerModule.Foresight, ComputerModule.MaintPlan,
                             ComputerModule.Roster, ComputerModule.MealPlan, ComputerModule.AutoLog, ComputerModule.Assistant, ComputerModule.Access })
                    a.Install(m);
                int reports0 = a.Book.Reports.Count;
                Run(w, SimTime.TicksPerDay);
                var rep = a.Book.Reports.LastOrDefault();
                Check("하루 보고 — 배율 모듈이 아낀 양이 숫자로 쌓인다 (전력 kWh > 0)", a.Book.Reports.Count > reports0 && rep != null && rep.Kwh > 0f && rep.Text.Contains("kWh"),
                    rep?.Text ?? "보고 없음");
                var apps = a.Apps;
                Check("새 모듈 — 당번표 · 식단 · 항해 일지 · 개인 메시지 · 물 예측 곡선 · 출입 기록을 실제로 만든다",
                    apps.Roster.Count >= 4 && apps.Menu.Count >= 3 && apps.Logbook.Count >= 1 && apps.Messages.Count > 0 && apps.WaterForecast[0] > 0f && apps.AccessLog.Count > 0,
                    $"당번 {apps.Roster.Count} · 식단 {apps.Menu.Count} ({apps.Menu.FirstOrDefault()}) · 일지 {apps.Logbook.Count} · 메시지 {apps.Messages.Count} · 물 {apps.WaterForecast[0]:0} → 30일 {apps.WaterForecast[30]:0}L · 출입 {apps.AccessLog.Count} · 정비 일정 {apps.MaintPlan.Count}");

                // 방송: 스피커가 고장 난 방 사람은 못 듣는다
                var b0 = w.Crew.FirstOrDefault(c => c.IsAwake && !c.Dead && c.Room != null && a.Speak.SpeakerWorks(c.Room));
                var deaf = w.Crew.FirstOrDefault(c => !c.Dead && c.Room != null && b0 != null && c.Room != b0.Room && c.IsAwake);
                if (deaf == null && b0 != null)
                {
                    deaf = w.Crew.First(c => c != b0 && !c.Dead && c.CanAct);
                    var other = w.Ship.LiveRooms.First(r => r != b0.Room && r.Type != RoomType.Corridor && r.Cells.Any(w.Ship.IsOpenFloor));
                    Put(w, deaf, other);
                    Run(w, 1);
                }
                if (deaf?.Room != null) a.Speak.BreakSpeaker(deaf.Room, "시험");
                var bc = a.Speak.Announce("시험 방송 — 식당으로 모여라", null, 1);
                Check("선내 방송 — 들은 방 사람만 안다 (스피커가 고장 난 방은 못 듣는다)", bc != null && b0 != null && deaf != null && a.Speak.Heard(b0, bc.Id) && !a.Speak.Heard(deaf, bc.Id),
                    bc == null ? "방송 없음" : $"들음 {bc.HeardBy.Count}명({b0?.Name} {b0?.Room?.Name}) · 못 들음 {bc.Missed.Count}명({deaf?.Name} {deaf?.Room?.Name}) · 안 울린 방 {bc.Silent.Count}");

                // 연산 자원: 모듈을 모두 올리면 부하가 넘쳐 비필수부터 끈다
                foreach (var m in Enum.GetValues<ComputerModule>()) a.Install(m);
                float load0 = a.Demand() / a.Capacity;
                Run(w, SimTime.Minutes(3));
                Check("연산 자원 — 부하가 넘치면 우선순위 낮은 모듈(오락 보관함)부터 끈다", load0 > 0.9f && a.Suspended.Contains(ComputerModule.MediaVault) && a.Load <= 0.9f,
                    $"부하 {load0 * 100:0}% → {a.Load * 100:0}% · 끈 모듈 {string.Join("·", a.Suspended.Select(AutomationSystem.ModuleName))} · 컴퓨터 방 열 +{a.HeatFor(a.Computer!.Body.Room):0.0}℃");
            }

            // 3) 검증 장면: 문 감지기가 틀어진 방 — 컴퓨터는 비었다고 믿고 진공 소화를 제안 → 거절 → 사람이 확인하러 가 쓰러진 사람을 데리고 나온다 → 그 사람의 컴퓨터 신뢰가 바뀐다
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                Player.Policy(w, "computerask", 1);
                Player.Policy(w, "inertfire", 0);
                var room = StoreRoom(w);
                ClearRoom(w, room);
                var victim = w.Crew.Where(c => c.CanAct && c.Id != w.Command.CaptainId).OrderBy(c => c.Id).First();
                Put(w, victim, room, last: true);
                victim.Vitals.Oxygen = 0.1f; victim.Down = true; victim.Pose = Pose.Down;
                float trust0 = a.Trusts.Of(victim);
                a.Belief.Break(room, SensorFault.Blind, "시험: 문 감지기 틀어짐");
                BigFire(w, room, 7);
                Proposal? p = null;
                bool diverged = false; string why = ""; int believed = -1;
                for (int i = 0; i < 12 * 25 / World.SystemInterval + 40 && p == null; i++)
                {
                    Run(w, World.SystemInterval);
                    if (victim.CarriedBy == null && victim.Room == room) { victim.Down = true; victim.Pose = Pose.Down; }
                    p = a.Asks.Open.FirstOrDefault(x => x.RoomId == room.Id);
                }
                if (p != null) { diverged = a.Belief.Diverged(room, out why); believed = a.Belief.Of(room).People; }
                Check("믿는 배 — 문 감지기가 틀어진 방을 컴퓨터는 비었다고 믿고 진공 소화를 제안한다 (믿음 ≠ 실제)",
                    p != null && p.Kind == "vacuum" && p.Believed == 0 && p.Inside.Contains(victim.Id) && diverged && !room.Purging,
                    p == null ? $"제안 없음 · 수순 {string.Join(",", a.FireCases.Select(f => $"{f.Stage}:{f.Method}:{f.Status}"))}" : $"제안: {p.Title} · 근거 {p.Basis} · 예상 {p.Effect} · 믿음 {believed}명 · 다름: {why}");
                if (p != null)
                {
                    a.Asks.Decide(p, false, "플레이어");
                    bool rescued = false, purgedOnHim = false; int minutes = 0;
                    for (; minutes < 40 && !rescued; minutes++)
                    {
                        // 거절을 지켰나: 그 사람이 안에 있는 동안 진공(공기 빼기)이 한 번도 없어야 한다 (데리고 나온 뒤 다시 승인받아 빼는 건 된다)
                        for (int s = 0; s < SimTime.Minutes(1); s += World.SystemInterval) { Run(w, World.SystemInterval); purgedOnHim |= room.Purging && victim.Room == room; }
                        if (victim.CarriedBy == null && victim.Room == room && !victim.LaidSafe) { victim.Down = true; victim.Pose = Pose.Down; }
                        rescued = victim.Room != room && victim.CarriedBy == null && !victim.Dead;
                        if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "3")
                        {
                            var kk = a.Checks.Concat(a.ChecksDone).FirstOrDefault(x => x.RoomId == room.Id);
                            var ch = kk != null ? w.Crew.First(c => c.Id == kk.CheckerId) : null;
                            Console.WriteLine($"   {minutes}분 확인자 {ch?.Name} {ch?.Room?.Name} 일 {ch?.Job?.Label} 단계 {ch?.Job?.Current?.GetType().Name} 업음 {ch?.CarryingPerson?.Name} · 피해자 {victim.Room?.Name} 업힘 {victim.CarriedBy?.Name} 불 {w.Fire.CountIn(room)} · {w.Automation.NowLine}");
                        }
                    }
                    var k = a.ChecksDone.Concat(a.Checks).FirstOrDefault(x => x.RoomId == room.Id);
                    var checker = k != null ? w.Crew.FirstOrDefault(c => c.Id == k.CheckerId) : null;
                    Check("거절 → 사람이 확인하러 가 안에 있던 사람을 데리고 나온다", rescued && k != null && k.Seen && k.Found.Contains(victim.Id) && !purgedOnHim,
                        $"{minutes}분 · 확인 {checker?.Name ?? "-"} ({(k?.Seen == true ? $"봤다 · 안에 {k.Found.Count}명" : "못 봤다")}) · {victim.Name} → {victim.Room?.Name} · 진공 {(a.Vacuumed > 0 ? "했다" : "안 했다")}{(purgedOnHim ? " (안에 사람이 있을 때!)" : a.Vacuumed > 0 ? " (데리고 나온 뒤 다시 승인)" : "")} · 믿음 고침 {a.Belief.Repairs}");
                    Run(w, SimTime.Minutes(8));
                    float trust1 = a.Trusts.Of(victim);
                    var others = w.Crew.Where(c => !c.Dead && c != victim).Select(c => a.Trusts.Of(c)).ToList();
                    Check("신뢰 — 그 사람의 컴퓨터 신뢰가 바뀐다 (사람마다 다르다) · 제안 결과가 채점된다",
                        trust1 < trust0 - 0.1f && others.Max() - trust1 > 0.1f && p.Score == -1 && p.Result.Contains("거절이 옳았다"),
                        $"{victim.Name} {trust0 * 100:0}% → {trust1 * 100:0}% ({a.Trusts.LastWhy.GetValueOrDefault(victim.Id)}) · 다른 사람 {others.Min() * 100:0}~{others.Max() * 100:0}% · 제안 결과: {p.Result}");
                    Console.WriteLine($"    일기: {victim.Diary.LastOrDefault().text}");
                }
            }

            // 4) 재부팅 중에는 자동 조치가 없다 (사람이 손으로) · 오경보가 잦은 감지기는 덜 믿는다
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                var room = StoreRoom(w);
                ClearRoom(w, room);
                a.Reboot("시험 재부팅", 4f);
                int acts0 = a.Book.Total;
                BigFire(w, room, 4);
                bool offline = true, cases = false; string card = "";
                for (int i = 0; i < 6 && a.Rebooting; i++)
                {
                    Run(w, World.SystemInterval);
                    offline &= !a.MainOnline;
                    cases |= a.FireCases.Count > 0;
                    card = a.NowLine;
                }
                int during = a.Book.Total - acts0;
                bool vent = room.VentOpen;
                Run(w, SimTime.Minutes(6));
                Check("재부팅 — 몇 분 동안 자동 조치가 없고(댐퍼·소화 수순 없음), 다시 켜지면 돌아온다",
                    offline && during == 0 && !cases && vent && a.MainOnline && a.Book.Total > acts0 && card.Contains("재부팅"),
                    $"재부팅 중 조치 {during} · 수순 {(cases ? "있음" : "없음")} · 댐퍼 {(vent ? "그대로" : "닫힘")} · 카드 \"{card}\" · 다시 켠 뒤 조치 {a.Book.Total - acts0}");

                Run(w, SimTime.Hours(2));
                var quiet = w.Ship.LiveRooms.First(r => r.Type == RoomType.Lounge);
                a.Belief.Break(quiet, SensorFault.Ghost, "시험: 업데이트 버그");
                var k = a.RequestCheck(quiet, "시험 — 화재 감지기 경보", ghost: true);
                for (int m = 0; m < 30 && k is { Seen: false }; m++) Run(w, SimTime.Minutes(1));
                var b = a.Belief.Of(quiet);
                Check("배우기 — 헛불을 사람이 확인하면 그 감지기를 덜 믿는다", k != null && k.Seen && b.FalseAlarms == 1 && b.Trust < 1f && b.Fault == SensorFault.None,
                    $"확인 {(k?.Seen == true ? "했다" : "못 했다")} · 오경보 {b.FalseAlarms} · 감지기 믿음 {b.Trust * 100:0}% · 고장 {BeliefModel.FaultName(b.Fault)}");
            }

            // 5) 문 본체 × 컴퓨터: 출입 관리 모듈이 다친 사람을 잠긴 약품고에 원격으로 들인다 (기다리지 않는다 · 신뢰 ↑)
            //    문 감지기 고장 → 컴퓨터는 그 방을 비었다고 믿는다 → 사람 확인으로 드러나면 정비 일정표 맨 앞에 올린다
            {
                var w = DayOne(seed, "Hanbit");
                RunUntilHour(w, 10f);
                var a = w.Automation;
                a.Install(ComputerModule.Access);
                var b = w.Body;
                var pharm = w.Ship.LiveRooms.Where(r => r.Kind == RoomType.Storage && r.Doors.Count >= 1 && r.Cells.Count >= 4 && r.DataLinked).OrderBy(r => r.Doors.Count).ThenBy(r => r.Id).First();
                b.SetZone(pharm, AccessZone.Medicine, LockKind.Card);
                var p = w.Crew.Where(c => c.CanAct && !c.IsChild && c.Role != CrewRole.Medic && c.Id != w.Command.CaptainId && c.Room != pharm && !c.Outside)
                    .OrderBy(c => (c.Position - pharm.Center).LengthSquared()).First();
                p.Vitals.Injury = 0.35f;
                float tr0 = a.Trusts.Of(p);
                var inside = pharm.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => (c.Center - pharm.Center).LengthSquared()).First();
                Force(w, p, new Job(null, "약 가지러", new List<Toil> { new GotoToil(inside), new WaitToil(10, Pose.Standing) }));
                bool reached = false;
                for (int t = 0; t < SimTime.Minutes(30) && !reached; t++) { w.Step(); reached = p.Room == pharm; if (p.Job == null) break; }
                var line = w.Log.Entries.LastOrDefault(e => e.Text.Contains("원격으로 열었다")).Text;
                var act = a.Book.Acts.LastOrDefault(x => x.Kind == ActKind.Door);
                Check("출입 관리 × 문 — 다친 사람이 잠긴 약품고 앞에 서면 컴퓨터가 원격으로 연다 (사람을 부르지 않는다 · 신뢰 ↑ · 다섯 칸 기록)",
                    reached && a.Passes > 0 && b.Stats.LetIn == 0 && a.Trusts.Of(p) > tr0 && act != null && act.Act.Contains("원격"),
                    $"{p.Name} → {pharm.Name} 들어감 {reached} · 원격 열기 {a.Passes} · 사람이 열어 줌 {b.Stats.LetIn} · 신뢰 {tr0 * 100:0}→{a.Trusts.Of(p) * 100:0}% · \"{line}\" · [{act?.Observe} | {act?.Judge} | {act?.Act}]");

                // 문 감지기 고장 → 믿음 Blind
                var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Quarters or RoomType.Lounge && r.DataLinked && r.Doors.Any(d => !d.IsExternal && b.DoorOf(d) != null)).OrderBy(r => r.Id).First();
                var door = room.Doors.First(d => !d.IsExternal && b.DoorOf(d) != null);
                var db = b.DoorOf(door)!;
                db.SensorBroken = true;
                if (b.Doors.FirstOrDefault(x => x.Inner == room.Id) is DoorBody inn) inn.SensorBroken = true;
                Run(w, SimTime.Minutes(6));
                var bel = a.Belief.Of(room);
                bool blind = bel.Fault == SensorFault.Blind && a.DoorBlinds > 0;
                var guest = w.Crew.Where(c => c.CanAct && !c.IsChild && c != p).OrderBy(c => c.Id).First();
                Put(w, guest, room);
                guest.NextThinkTick = w.Tick + SimTime.Hours(1);
                Run(w, World.SystemInterval * 2);
                int believed = a.Belief.Of(room).People;
                var k = a.RequestCheck(room, "시험 — 사람 수 확인");
                for (int m = 0; m < 30 && k is { Seen: false }; m++) { Run(w, SimTime.Minutes(1)); if (guest.Room != room) Put(w, guest, room); }
                Run(w, SimTime.Minutes(6));
                bool known = a.DoorsKnown > 0 && a.Apps.MaintPlan.Any(x => x.Machine == "문 감지기");
                int brokenDoor = b.Doors.Where(x => x.SensorBroken).Select(x => x.Door).DefaultIfEmpty(-1).First();
                Check("문 감지기 고장 → 컴퓨터는 사람이 있는 방을 비었다고 믿는다 → 사람 확인으로 드러나 정비 일정표 맨 앞에 올린다 (수리 우선)",
                    blind && believed == 0 && k != null && k.Seen && known,
                    $"{room.Name} 고장 {BeliefModel.FaultName(bel.Fault)} · 믿음 {believed}명 ↔ 실제 {BeliefModel.Actual(w, room)}명 · 확인 {(k?.Seen == true ? "했다" : "못 했다")} · 알게 된 문 {a.DoorsKnown} (수리 우선 {a.DoorKnownBroken(brokenDoor)}) · 정비 일정 {string.Join(" / ", a.Apps.MaintPlan.Take(2).Select(x => $"{x.Machine}({x.Room}) {x.Why}"))}");
            }

            // 6) 식단 × 식량: 식단 계획 모듈은 배급을 하루 앞당긴다 (주방 당번이 배급표를 붙인다) → 배고픈 사람이 컴퓨터 식단을 탓한다 → 컴퓨터가 거둔다
            {
                World Low(bool plan)
                {
                    var w = DayOne(seed, "Hanbit");
                    int crew = w.Crew.Count(c => !c.Dead);
                    Scenarios.LimitStock(w, ItemKind.Meal, crew);
                    Scenarios.LimitStock(w, ItemKind.Ration, 0);
                    Scenarios.LimitStock(w, ItemKind.Produce, 0);
                    int want = (int)(crew * FoodPolicy.MealsPerPersonDay * 2.7f) - (int)FoodPolicy.FoodStock(w);
                    foreach (var box in w.Ship.Containers) if (want > 0 && box.Storage!.Accepts(ItemKind.Ration)) want -= box.Storage.Add(ItemKind.Ration, want);
                    foreach (var bed in w.Ship.FurnitureOf(FurnitureType.GrowBed).Where((_, i) => i % 4 != 0).ToList()) { bed.Machine!.Crop!.Growth = 0.05f; w.Machines.Break(bed.Machine, FaultKind.Wrecked); }
                    if (plan) w.Automation.Install(ComputerModule.MealPlan);
                    return w;
                }
                var wa = Low(true);
                var wb = Low(false);
                float days0 = FoodPolicy.FoodDays(wa);
                long ta = -1, tb = -1;
                for (int t = 0; t < SimTime.Hours(20) && (ta < 0 || tb < 0); t++)
                {
                    wa.Step(); wb.Step();
                    if (ta < 0 && wa.Food.Rationing) ta = wa.Tick;
                    if (tb < 0 && wb.Food.Rationing) tb = wb.Tick;
                }
                var adv = wa.Automation.Book.Acts.FirstOrDefault(x => x.Key == "rationlead");
                Check("식단 × 식량 — 식단 계획 모듈이 바닥나는 날을 먼저 보고 배급을 앞당긴다 (주방 당번이 배급표를 붙인다)",
                    ta >= 0 && (tb < 0 || ta < tb) && wa.Automation.RationLeads > 0 && adv != null,
                    $"처음 {days0:0.0}일치 · 모듈 있음: {(ta >= 0 ? SimTime.Clock(ta) : "안 함")} ↔ 없음: {(tb >= 0 ? SimTime.Clock(tb) : "20시간 안에 안 함")} · 기록 [{adv?.Observe} | {adv?.Judge} | {adv?.Act}]");
                var au = wa.Automation;
                var eaters = wa.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
                var hungry = eaters.Take(eaters.Count / 2 + 1).ToList();
                var tr0 = hungry.Select(c => au.Trusts.Of(c)).ToList();
                foreach (var c in hungry) { c.Needs.Food = 0.15f; if (!c.IsAwake) { c.Pose = Pose.Standing; } }
                for (int m = 0; m < 12 && !au.MealEased; m++) { foreach (var c in hungry) c.Needs.Food = MathF.Min(c.Needs.Food, 0.15f); Run(wa, SimTime.Minutes(1)); }
                int down = hungry.Where((c, i) => au.Trusts.Of(c) < tr0[i]).Count();
                Check("배고픈 사람이 컴퓨터 식단을 탓한다(신뢰 ↓) → 불평이 절반을 넘으면 컴퓨터가 앞당기기를 거둔다 (배우기)",
                    au.MealGripes > 0 && down > 0 && au.MealEased && au.RationLead == 0f && au.Learn.WrongCalls.Any(x => x.kind == "ration"),
                    $"불평 {au.MealGripes} · 신뢰 내려간 사람 {down}/{hungry.Count} · 앞당김 기준 {au.RationLead:0} · 기억: {au.Learn.WrongCalls.LastOrDefault().why}");
            }

            // 7) 대재난: 예보 방송 → 들은 사람만(믿으면) 미리 대피소로 · 스피커 고장 방 사람은 그대로 → EMP: 스피커 타고 감지기 값 깨지고 재부팅 → 예보가 맞아 들은 사람 신뢰 ↑
            {
                var w = DayOne(seed, "Mirinae");
                RunUntilHour(w, 11f);
                var a = w.Automation;
                var shelter = Facilities.Best(w.Ship, "shelter").room!;
                var awake = w.Crew.Where(c => c.CanAct && c.IsAwake && !c.IsChild && !c.Outside && c.Room != null && c.Room != shelter).OrderBy(c => c.Id).ToList();
                var hearer = awake.First(c => a.Speak.SpeakerWorks(c.Room!));
                var deaf = awake.First(c => c != hearer && c.Room != hearer.Room);
                a.Speak.BreakSpeaker(deaf.Room!, "시험");
                foreach (var c in new[] { hearer, deaf }) a.Trusts.Change(c, 0.7f - a.Trusts.Of(c), "시험", quiet: true);
                float th0 = a.Trusts.Of(hearer);
                var b = a.CosmicForecast("궤도 무기 EMP", 20f, CosmicFx.Emp | CosmicFx.Shock);
                bool heedH = false, heedD = false, inShelter = false;
                for (int m = 0; m < 25 && !inShelter; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    heedH |= hearer.Job?.Activity is HeedBroadcastActivity;
                    heedD |= deaf.Job?.Activity is HeedBroadcastActivity;
                    inShelter |= hearer.Room == shelter;
                    if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "7")
                        Console.WriteLine($"   {m}분 {hearer.Name} {hearer.Room?.Name} {hearer.Cell} 일 {hearer.Job?.Label} 단계 {hearer.Job?.Current?.GetType().Name} 생각 {string.Join(" / ", hearer.LastEvaluations.Take(3).Select(e => $"{e.Activity.Id}:{e.Score:0.00}"))} · 대피소 {shelter.Name}");
                }
                Check("예보 방송 → 들은 사람만 미리 대피소로 간다 (스피커 고장 방 사람은 모른다)",
                    b != null && b.HeardBy.Contains(hearer.Id) && !b.HeardBy.Contains(deaf.Id) && heedH && inShelter && !heedD,
                    $"방송 \"{b?.Text}\" · {hearer.Name}({hearer.Room?.Name}) 들음 · 대피 {(inShelter ? shelter.Name + " 도착" : heedH ? $"가는 중 ({hearer.Job?.Label} · {hearer.Room?.Name})" : "안 감")} · {deaf.Name}({deaf.Room?.Name}) 못 들음 · 대피 {(heedD ? "했다" : "안 했다")}");
                int broken0 = w.Ship.LiveRooms.Count(r => a.Speak.SpeakerBroken(r));
                int stuck0 = a.Belief.Corruptions;
                a.CosmicHit("궤도 무기 EMP", CosmicFx.Emp | CosmicFx.Shock, 0.8f);
                bool rebooting = a.Rebooting;
                Run(w, SimTime.Minutes(2));
                var fc = a.SpaceForecasts.LastOrDefault();
                Check("EMP — 스피커 회로가 타고 감지기 값이 깨지고(낡은 믿음) 재부팅 · 예보가 맞아 미리 피한 사람의 신뢰 ↑",
                    rebooting && w.Ship.LiveRooms.Count(r => a.Speak.SpeakerBroken(r)) > broken0 && a.Belief.Corruptions > stuck0 && fc is { Graded: true, Hit: true } && a.Trusts.Of(hearer) > th0,
                    $"재부팅 {rebooting} · 스피커 고장 {broken0} → {w.Ship.LiveRooms.Count(r => a.Speak.SpeakerBroken(r))} · 값 깨짐 {stuck0} → {a.Belief.Corruptions} · 예보 {(fc?.Hit == true ? "맞음" : "?")} · {hearer.Name} 신뢰 {th0 * 100:0}→{a.Trusts.Of(hearer) * 100:0}%");
            }

            // 8) 앞날 예측 (v16.16 바탕): 한 시간마다 내다보고 여섯 시간 뒤 채점 — 갈래마다 맞힌 비율이 믿음이 된다
            {
                var w = DayOne(seed, "Mirinae");
                Run(w, SimTime.Hours(16));
                var f = w.Automation.Foresight;
                Check("앞날 예측 — 지켜보는 값을 내다보고 기한에 채점한다 (맞힌 비율 = 다음 예측의 믿음)",
                    f.Current.Count >= 4 && f.Graded.Count >= 8 && f.Hits > 0,
                    $"지금 예측 {f.Current.Count} ({string.Join(" · ", f.Current.Take(4).Select(p => $"{p.Name} {p.Now:0.#}→{p.Value:0.#}{p.Unit} 믿음 {p.Confidence * 100:0}%"))}) · 채점 {f.Graded.Count} (맞음 {f.Hits} · 틀림 {f.Misses}) · 계획 {f.Plan.Count}");
            }

            // 9) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 주컴퓨터 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
