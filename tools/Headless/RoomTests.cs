using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.17 방은 승무원이 정한다: 쓰임 → 용도 · 승무원 안건 → 회의 → 실제 공사 · 이름 · 기관 설비 이전 · 옛 자리 습관
public static partial class Program
{
    private static int RunRoomTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"방은 승무원이 정한다 (v16.17) · 시드 {seed}\n");
        bool thinkWas = RoomPlanSystem.ThinkOff;

        // ── 1) 회의가 창고 절반을 운동실로 → 칸막이 · 선반 · 운동 기구를 실제로 옮기고 '땀방' → 한동안 옛 자리로 가다 헷갈린다 ──
        {
            var w = DayOne(seed, "Hanbit");
            RoomPlanSystem.ThinkOff = true; // 다른 안건이 끼지 않게
            foreach (var c in w.Crew) c.Fitness = 0.25f; // 다들 몸이 굳었다
            var by = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).First();
            var store = w.Ship.Rooms.First(r => r.Type == RoomType.Storage);
            int shelves0 = store.Furniture.Count(f => f.Type == FurnitureType.Shelf);
            var draft = w.RoomPlans.GymPlan(by, 0.25f);
            var plan = draft != null ? w.RoomPlans.Propose(draft) : null;
            Check("안건 — 승무원이 낸다 · 컴퓨터가 위험 · 공사 기간 · 순서를 붙인다", plan != null && plan.Advice.Contains("위험") && plan.Steps.Count >= 3 && plan.Proposer == by.Id,
                plan == null ? "창고를 나눌 수 없다" : $"{plan.Title} · {plan.Why} · {plan.Advice}");
            if (plan == null) { RoomPlanSystem.ThinkOff = thinkWas; return 1; }
            long t0 = w.Tick;
            while (plan.State == "제안" && w.Tick - t0 < SimTime.TicksPerDay * 2) w.Step();
            var rec = w.Meetings.Minutes.LastOrDefault(m => m.Items.Any(i => i.Topic == "room:" + plan.Id));
            var item = rec?.Items.First(i => i.Topic == "room:" + plan.Id);
            Check("회의 — 토론 · 표결로 정한다", plan.State is "공사" or "완료" && item != null && item.Passed && item.Speeches.Count > 0,
                $"{plan.State} · 찬성 {plan.For.Count} · 반대 {plan.Against.Count}" + (item != null ? $" · 발언 {item.Speeches.Count} · {string.Join(" / ", item.Speeches.Take(3).Select(s => $"{w.Crew[s.Who].Name}: {s.Text}"))}" : " · 회의 없음"));
            long t1 = w.Tick;
            int confusedDuring = 0;
            while (plan.State == "공사" && w.Tick - t1 < SimTime.TicksPerDay * 2) w.Step();
            var rs = w.RoomPlans.Stats;
            var gym = plan.NewRoomId >= 0 ? w.Ship.Rooms[plan.NewRoomId] : null;
            float hours = (w.Tick - t1) / (float)SimTime.TicksPerHour;
            Check("실제 공사 — 칸막이 · 선반을 둘이/카트로 내보내고 · 운동 기구를 들인다",
                plan.State == "완료" && gym != null && rs.Walls == 1 && rs.Moves >= 1 && rs.CoCarry + rs.CartHauls >= 1
                && gym.Furniture.Any(f => f.Type == FurnitureType.Treadmill) && !gym.Furniture.Any(f => f.Type == FurnitureType.Shelf),
                $"{plan.State} · {hours:0.#}시간(컴퓨터 예상 {plan.Hours:0.#}) · 칸막이 {rs.Walls} · 옮김 {rs.Moves} · 같이 듦 {rs.CoCarry} · 카트 {rs.CartHauls} · 혼자 {rs.SoloLifts} · 내려놓음 {rs.SetDowns} · 기구 짬 {rs.Assembled}"
                + (gym != null ? $" · {gym.Name}: {string.Join(",", gym.Furniture.Select(f => f.Label))}" : "")
                + " · 단계 " + string.Join(" ", w.RoomPlans.Tasks.Where(t => t.PlanId == plan.Id).Select(t => $"{t.Kind}{(t.Done ? "✓" : "")}/{t.Step}/L{t.Leader}H{t.Helper}N{t.Need}{(t.Lifted ? "↑" : "")}{(t.SetDown != null ? "↓" : "")}p{t.Progress:0.0}")));
            bool named = gym != null && gym.CustomName == "땀방" && gym.Name == "땀방";
            bool logged = w.History.Events.Any(e => e.Text.Contains("'땀방'"));
            Check("이름 — '땀방' 표지판 (화면 · 기록에 그 이름)", named && logged, $"{gym?.Name} · 기록 {(logged ? "있음" : "없음")}");
            // 쓰임: 몸이 굳은 사람이 땀방에 운동하러 간다 → 용도가 운동실이 된다 · 시스템들이 그걸 본다
            long t2 = w.Tick;
            while (w.Tick - t2 < SimTime.Hours(30) && (gym?.UsedAs != RoomType.Gym || rs.Workouts == 0)) w.Step();
            var ex = Facilities.Best(w.Ship, "exercise");
            Check("쓰임 → 용도 — 땀방은 운동실로 쓰인다 (Facilities가 그 방을 고른다)", gym != null && gym.UsedAs == RoomType.Gym && ex.room == gym && rs.Workouts >= 1,
                $"{gym?.Name} 지금 용도 {gym?.UsedAs?.ToString() ?? "설계대로"} · 운동 {rs.Workouts}번 · 운동할 곳 {ex.room?.Name ?? "없음"}({ex.factor:0.##})");
            // 옛 자리 습관: 선반을 옮긴 걸 못 본 사람이 옛 자리(이제 땀방)로 물건 찾으러 간다
            var ru = w.RoomUse;
            long t3 = w.Tick;
            while (ru.Stats.Confusions == 0 && w.Tick - t3 < SimTime.Hours(20)) w.Step();
            bool natural = ru.Stats.Confusions > 0;
            if (!natural)
            {
                // 저절로 안 났으면: 그 선반에서 꺼내는 일을 맡긴다 (옛 자리에 익숙한 사람)
                foreach (var m in ru.Moved)
                {
                    var shelf = w.Ship.Furniture[m.Furniture];
                    var who = w.Crew.FirstOrDefault(c => c.CanAct && !c.Outside && m.Habit.Contains(c.Id) && !m.Knows.Contains(c.Id));
                    if (who == null || shelf.Storage == null || shelf.UseSpots.Count == 0) continue;
                    var kind = Enum.GetValues<ItemKind>().FirstOrDefault(k => shelf.Storage.Count(k) > 0);
                    who.EndJob(w, ToilStatus.Interrupted);
                    who.StartJob(new Job(null, "물건 꺼내기", new Toil[] { new GotoToil(shelf.UseSpots[0]), new TakeToil(shelf, kind, 1, true) }), w, null);
                    break;
                }
                long t4 = w.Tick;
                while (ru.Stats.Confusions == 0 && w.Tick - t4 < SimTime.Hours(1)) w.Step();
            }
            var line = w.Log.Entries.Where(e => e.Text.Contains("헷갈렸다")).Select(e => e.Text).LastOrDefault();
            Check("습관 — 한동안 옛 자리(땀방)로 물건 찾으러 갔다가 헷갈린다", ru.Stats.Confusions > 0 && line != null,
                $"{(natural ? "저절로" : "맡긴 일로")} · 헷갈림 {ru.Stats.Confusions} · 바로 기억 {ru.Stats.Learned} · {line}");
            RoomPlanSystem.ThinkOff = thinkWas;
        }

        // ── 2) 쓰임으로 용도가 바뀐다: 침실에서 침대를 빼고 선반을 들이면 창고 — 컴퓨터가 감시 기준을 바꾼다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var q = w.Ship.Rooms.First(r => r.Type == RoomType.Quarters);
            float rest0 = Facilities.Factor(q, "rest");
            foreach (var bed in q.Furniture.Where(f => FurnitureTypes.Sleepable(f.Type)).ToList())
            {
                foreach (var c in w.Crew) if (c.Bed == bed) c.Bed = null; // 침대를 뺐다 — 주인은 다른 데서 잔다
                w.Ship.Stow(bed);
            }
            int added = 0;
            foreach (var c in q.Cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToList())
            {
                if (added >= 5) break;
                if (!w.Ship.IsOpenFloor(c) || !Cell.Dirs4.Any(d => w.Ship.Grid.Kind(c + d) == TileKind.Wall) || !Adaptation.SafeToBlock(w, q, c)) continue;
                if (q.Doors.Any(d => Math.Abs(d.Cell.X - c.X) + Math.Abs(d.Cell.Y - c.Y) <= 2)) continue;
                var sh = w.Ship.AddFurniture(FurnitureType.Shelf, c);
                sh.Storage = new Inventory(60, ItemKinds.Shelved);
                sh.Storage.Add(ItemKind.Cable, 4);
                added++;
            }
            w.Paths.Invalidate();
            Run(w, SimTime.Hours(3));
            var act = w.Automation.Book.Acts.LastOrDefault(a => a.RoomId == q.Id && a.Observe.Contains("실제"));
            Check("쓰임 → 용도 — 침대를 빼고 선반을 들인 침실은 창고로 쓰인다 · 잠자리 판정이 그걸 본다 · 컴퓨터가 읽는다",
                q.UsedAs == RoomType.Storage && Facilities.Factor(q, "rest") < rest0 && act != null && w.History.Events.Any(e => e.Text.Contains("창고로 쓰인다")),
                $"{q.Name}: 지금 용도 {q.UsedAs?.ToString() ?? "설계대로"} · 선반 {added} · 깊은 잠 {rest0:0.##} → {Facilities.Factor(q, "rest"):0.##} · 컴퓨터: {act?.Judge} {act?.Act}");
        }

        // ── 3) 기관 설비 이전 — 컴퓨터가 위험 · 멈추는 계통 · 공사 순서를 알리고 카드를 낸다 · 떼어 내면 멈추고 다시 이으면 돈다 ──
        {
            var w = DayOne(seed, "Hanbit");
            RoomPlanSystem.ThinkOff = true;
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).OrderBy(f => f.Id).Last();
            var dest = w.Ship.Rooms.Where(r => r.Type is RoomType.Workshop or RoomType.Storage && w.RoomPlans.Spot(pump.Width, pump.Height, r, null) != null).OrderBy(r => r.Id).First();
            var eng = w.Crew.Where(c => !c.Dead && c.Role == CrewRole.Engineer).OrderBy(c => c.Id).FirstOrDefault() ?? w.Crew.First(c => !c.Dead);
            var safety = w.Crew.FirstOrDefault(c => !c.Dead && c.Value == CrewValue.Safety && c != eng);
            var plan = w.RoomPlans.ProposeMove(eng, pump, dest, "냉각실 외벽이 두 번 뚫렸다 (시험)");
            var card = w.Automation.Asks.All.FirstOrDefault(p => p.Key == "roomplan:" + plan.Id);
            var adv = w.Automation.Book.Acts.LastOrDefault(a => a.Kind == ActKind.Advice && a.Observe.Contains(pump.Label));
            Check("기관 설비 이전 — 컴퓨터가 위험 · 멈추는 계통 · 공사 순서를 알린다 (제안 카드)",
                plan.Kind == RoomPlanKind.MoveEngine && plan.Risk > 0.3f && plan.Stops.Contains("원자로") && plan.Steps.Count >= 4 && card != null && adv != null,
                $"위험 {plan.Risk * 100:0}% · {plan.Hours:0.#}시간 · 멈춤 {plan.Stops} · 카드 {(card != null ? card.Title : "없음")} · {adv?.Act}");
            if (safety != null)
            {
                var (so, swhy) = w.RoomPlans.Opinion(safety, plan);
                var (eo, _) = w.RoomPlans.Opinion(w.Crew.First(c => !c.Dead && c.Value != CrewValue.Safety && c != eng), plan);
                Check("안전을 중시하는 사람은 위험을 듣고 망설인다", so < eo, $"{safety.Name} {so:0.00} ({swhy}) < 다른 사람 {eo:0.00}");
            }
            w.Automation.Asks.Decide(card!, true, "관찰자", "시험");
            w.RoomPlans.Start(plan);
            long t0 = w.Tick;
            bool parked = false, hatch = false;
            while (plan.State == "공사" && w.Tick - t0 < SimTime.TicksPerDay)
            {
                w.Step();
                if (!parked && pump.Machine!.Parked && w.RoomPlans.Tasks.Any(t => t.PlanId == plan.Id && t.Kind == RoomTaskKind.Disconnect && t.Done))
                {
                    parked = true;
                    hatch = plan.Hatches.Any(h => w.Body.HatchOpenAt(h) && w.Body.Mark(h, CellMark.Tape) > 0.5f);
                }
            }
            Check("떼어 낸 펌프는 멈춘다 (점검 뚜껑 · 테이프) → 옮겨 다시 이으면 돈다", parked && hatch && plan.State == "완료" && pump.Room == dest && !pump.Machine!.Parked && pump.Machine.Feed >= 0.6f && plan.OrderKept,
                $"멈춤 {(parked ? "✔" : "✘")} · 뚜껑/테이프 {(hatch ? "✔" : "✘")} · {plan.State} · {pump.Room.Name} · 사고 {plan.Accidents} · 순서 {(plan.OrderKept ? "받음" : "안 받음")}");
            RoomPlanSystem.ThinkOff = thinkWas;
        }

        // ── 4) 이름이 기록 · 저장에 남는다: 관찰자가 낸 불을 함께 넘긴 식당 → 승무원이 '그날의 식당'을 제안 → 회의 → 표지판 → 저장 · 불러오기 ──
        {
            var w = DayOne(seed, "Hanbit");
            var mess = w.Ship.Rooms.First(r => r.Type == RoomType.Mess);
            Player.Fire(w, mess.Cells.First(w.Ship.IsOpenFloor));
            long t0 = w.Tick;
            while (mess.CustomName == null && w.Tick - t0 < SimTime.TicksPerDay * 3) w.Step();
            Run(w, SimTime.Hours(2));
            string text = SaveGame.Write(w);
            var runner = new ReplayRunner(text);
            while (!runner.Advance(50000)) { }
            var mess2 = runner.World.Ship.Rooms[mess.Id];
            bool hist = w.History.Events.Any(e => mess.CustomName != null && e.Text.Contains(mess.CustomName));
            Check("이름 — 겪은 일에서 나와 기록 · 저장(지문)에 남는다", mess.CustomName != null && hist && runner.Verified && mess2.CustomName == mess.CustomName,
                $"{mess.CustomName ?? "(이름 없음)"} · 기록 {(hist ? "있음" : "없음")} · 불러오기 {(runner.Verified ? "같은 역사" : "어긋남")} · 불러온 배: {mess2.Name}"
                + $" · 안건 {w.RoomPlans.Stats.Proposed} · {string.Join(" / ", w.RoomPlans.Plans.Select(p => $"{p.Title}:{p.State}"))}"
                + $" · 쓰임 바뀜 {w.RoomUse.Stats.Changes}: {string.Join(" / ", w.History.Events.Where(e => e.Text.Contains("쓰인다")).Select(e => e.Text).Take(4))}");
        }

        // ── 5) 결정론 ──
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 — 같은 시드는 같은 지문", a == b, $"{a:x8} / {b:x8}");
        }

        RoomPlanSystem.ThinkOff = thinkWas;
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
