using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v17.4 공간과 협력 · 줄 서기 · 구경꾼 — 검증 장면을 헤드리스에서 실제로 일으킨다
public static partial class Program
{
    private static Job? SpChore(World w, CrewMember c, WorkOrder o)
    {
        var chores = Brain.Activities.OfType<ChoresActivity>().First();
        var job = WorkPlanners.Build(chores, o, c, w, w.Paths.Flood(c.Cell, c.PathProfile), out var why);
        if (job == null) Console.WriteLine($"   (계획 실패: {o.Title} — {why})");
        else o.Assignee = c;
        return job;
    }

    private static WorkOrder? SpMaintain(World w, Furniture f)
    {
        f.Machine!.Wear = 0.85f;
        w.Board.RequestScan();
        Run(w, SimTime.Minutes(1));
        return w.Board.Open.FirstOrDefault(x => x.Kind == WorkKind.Maintain && x.Target.Furniture == f);
    }

    private static CrewMember SpWorker(World w, Skill sk, params CrewMember[] not) =>
        w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside && !not.Contains(c)).OrderByDescending(c => c.SkillLevel(sk)).ThenBy(c => c.Id).First();

    /// <summary>나머지 사람은 자기 침대 근처에서 잠자게 한다 (장면에 끼어들지 않게).</summary>
    private static void SpQuiet(World w, IEnumerable<CrewMember> keep, long hold)
    {
        foreach (var o in w.Crew)
        {
            if (keep.Contains(o) || !o.CanAct) continue;
            Force(w, o, new Job(null, "시험: 쉼", new Toil[] { new WaitToil((int)hold, Pose.Sleeping) }) { InterruptMargin = 9f }, hold);
        }
    }

    private static Job SpEat(World w, CrewMember c)
    {
        var eat = Brain.Activities.OfType<EatActivity>().First();
        return eat.Plan(c, w, w.Paths.Flood(c.Cell, c.PathProfile))!;
    }

    /// <summary>v16.22 새 한빛호는 호이스트를 단다 (무거운 부품을 매달아 혼자 든다) — 둘이 드는 장면은 호이스트를 치운 배에서.</summary>
    private static void SpNoHoist(World w)
    {
        foreach (var h in w.Ship.FurnitureOf(FurnitureType.Hoist).ToList()) w.Ship.Stow(h);
        w.Paths.Invalidate();
    }

    private static int RunSpaceTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"공간과 협력 · 줄 서기 · 구경꾼 점검 (v17.4) · 시드 {seed}\n");

        // ── 1) 경보로 비운 정비 자리에 공구와 부품이 그대로 있고, 돌아와 이어 한다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var f = w.Ship.Furniture.Where(x => x.Machine is Machine m && !x.Stowed && m.Faults.Count == 0 && m.Spec.ServiceHours >= 0.7f && x.Room.Type != RoomType.Corridor && m.Spec.ServiceItem is null)
                .OrderByDescending(x => x.Machine!.Spec.ServiceHours).ThenBy(x => x.Id).FirstOrDefault()
                ?? w.Ship.Furniture.Where(x => x.Machine is Machine m && !x.Stowed && m.Faults.Count == 0 && m.Spec.ServiceHours >= 0.7f).OrderByDescending(x => x.Machine!.Spec.ServiceHours).First();
            var o = SpMaintain(w, f)!;
            var c = SpWorker(w, f.Machine!.Spec.Skill);
            foreach (var x in w.Crew) if (x.Job?.Order == o) x.EndJob(w, ToilStatus.Interrupted);
            Force(w, c, SpChore(w, c, o)!, SimTime.Hours(4));
            for (int k = 0; k < 40 && o.Progress < 0.3f; k++) Run(w, SimTime.Minutes(3));
            var site = w.Coop.Sites.FirstOrDefault(s => s.OrderId == o.Id);
            Check("정비 자리에 공구 · 분해한 부품을 펼친다", site != null && site.Items.Count >= 2 && site.Owner == c.Id && o.Progress > 0.2f,
                $"{f.Label} · {(site == null ? "작업장 없음" : string.Join("·", site.Items.Select(t => $"{t.Name}{t.At}")))} · 진척 {o.Progress * 100:0}%");
            // 경보: 급한 일로 손을 놓고 달려간다
            float p0 = o.Progress;
            var far = w.Ship.LiveRooms.Where(r => r != f.Room && r.Type != RoomType.Corridor && r.Cells.Any(w.Ship.IsOpenFloor)).OrderByDescending(r => (r.Center - f.Center).LengthSquared()).First();
            var cells = site?.Items.Select(t => t.At).ToList() ?? new List<Cell>();
            Force(w, c, new Job(null, "비상 소집 — 구조", new Toil[] { new GotoToil(far.Cells.First(w.Ship.IsOpenFloor)), new WaitToil(SimTime.Minutes(25), Pose.Working) }) { Urgent = true }, SimTime.Minutes(40));
            Run(w, SimTime.Minutes(5));
            bool left = site != null && site.State == SiteState.Left && w.Coop.Sites.Contains(site) && site.Items.Select(t => t.At).SequenceEqual(cells) && o.Progress >= p0 - 0.001f;
            var leftLog = w.Log.Entries.LastOrDefault(e => e.CrewId == c.Id && e.Text.Contains("그대로")).Text;
            Check("경보로 비운 정비 자리에 공구와 부품이 그대로 (진척도 남는다)", left && leftLog != null,
                $"상태 {site?.State} · 남은 것 {site?.Items.Count} · 진척 {o.Progress * 100:0}% · \"{leftLog}\"");
            // 비워 둔 동안 주 컴퓨터 · 다른 사람
            float before = -1f;
            for (int k = 0; k < 48 && !o.Closed; k++)
            {
                Run(w, SimTime.Minutes(5));
                if (before < 0f && site?.State == SiteState.Active) before = o.Progress;
            }
            var resumeLog = w.Log.Entries.FirstOrDefault(e => e.Text.Contains("펼쳐 둔 자리에서 이어 한다") || e.Text.Contains("자리를 이어받는다")).Text;
            var inh = w.Log.Entries.FirstOrDefault(e => e.Text.Contains("자리를 이어받는다"));
            if (inh.Text != null) Console.WriteLine($"   (이어받은 사람 {w.Crew.FirstOrDefault(x => x.Id == inh.CrewId)?.Name} · {SimTime.Clock(inh.Tick)})");
            bool byOwner = w.Coop.Stats.Resumed >= 1;
            Check("돌아와 이어 한다 (놓고 간 사람이 제 자리로 · 진척은 그대로에서)", o.Closed && (byOwner || w.Coop.Stats.Inherited >= 1) && before >= p0 - 0.01f && !w.Coop.Sites.Contains(site!),
                $"끝남 {o.Closed} · 이어 함 {w.Coop.Stats.Resumed} · 남이 이음 {w.Coop.Stats.Inherited} · 다시 시작 진척 {before * 100:0}% (비울 때 {p0 * 100:0}%) · \"{resumeLog}\" · 챙김 {w.Coop.Stats.Packed}");
        }

        // ── 1b) 비워 둔 자리를 누가 건드리면 돌아온 사람이 헷갈린다 · 펼친 부품이 통로를 좁힌다 ──
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            var f = w.Ship.Furniture.Where(x => x.Machine is Machine m && !x.Stowed && m.Faults.Count == 0 && m.Spec.ServiceHours >= 0.7f && x.Room.Type != RoomType.Corridor)
                .OrderBy(x => x.Id).Skip(1).First();
            var o = SpMaintain(w, f)!;
            float h0 = SimTime.HourOfDay(w.Tick);
            // 교대가 아직 한참 남은 사람 (인수인계로 손을 놓지 않게)
            var c = w.Crew.Where(x => x.CanAct && !x.IsChild && SimTime.InWindow(h0, x.Schedule.WorkStart, x.Schedule.WorkLength) && SimTime.InWindow(h0 + 3f, x.Schedule.WorkStart, x.Schedule.WorkLength))
                        .OrderByDescending(x => x.SkillLevel(f.Machine!.Spec.Skill)).ThenBy(x => x.Id).FirstOrDefault() ?? SpWorker(w, f.Machine!.Spec.Skill);
            var other = w.Crew.First(x => x != c && x.CanAct && !x.IsChild);
            foreach (var x in w.Crew) if (x.Job?.Order == o) x.EndJob(w, ToilStatus.Interrupted);
            Force(w, c, SpChore(w, c, o)!, SimTime.Hours(4));
            for (int k = 0; k < 40 && o.Progress < 0.15f; k++) Run(w, SimTime.Minutes(3));
            var site = w.Coop.Sites.FirstOrDefault(s => s.OrderId == o.Id);
            if (site == null) // 통합: 멈추지 않고 왜 자리가 안 펼쳐졌는지 남긴다
            {
                Check("펼친 부품이 통로를 좁힌다 (ObjectPhysics 통로 점유와 같은 규칙 · 길찾기 비용)", false, $"정비 자리가 안 펼쳐졌다 — {c.Name} {c.Job?.Label} · {c.Room?.Name} · 진척 {o.Progress:P0} · 맡은 사람 {o.Assignee?.Name} · {f.Name}@{f.Room.Name}");
                goto Skip1b;
            }
            Force(w, c, new Job(null, "비상 소집", new Toil[] { new WaitToil(SimTime.Minutes(10), Pose.Standing) }) { Urgent = true }, SimTime.Minutes(12));
            Run(w, SimTime.Minutes(2));
            // 펼친 부품 하나가 통로 칸에 있으면 길찾기 비용이 오른다 (짐 · 상자와 같은 규칙)
            var aisle = w.Ship.LiveRooms.Where(r => r.Type == RoomType.Corridor).SelectMany(r => r.Cells).Where(x => w.Ship.IsOpenFloor(x)).OrderBy(x => x.Y).ThenBy(x => x.X).First();
            int idx = w.Ship.Grid.Index(aisle);
            Run(w, SimTime.Minutes(1));
            int costBefore = w.Paths.CellBody[idx];
            var moved = site.Items[0];
            var home = moved.At;
            moved.At = aisle;
            Run(w, SimTime.Minutes(1));
            int costAfter = w.Paths.CellBody[idx];
            Check("펼친 부품이 통로를 좁힌다 (ObjectPhysics 통로 점유와 같은 규칙 · 길찾기 비용)", costAfter >= costBefore + moved.Bulk && w.Matter.InAisle(aisle),
                $"{moved.Name} → 통로 {aisle} · 비용 {costBefore} → {costAfter}");
            moved.At = home;
            // 지나가던 사람이 건드렸다
            site.Touched = true; site.Toucher = other.Id;
            float aff = c.AffinityTo(other);
            Run(w, SimTime.Minutes(10));
            Force(w, c, SpChore(w, c, o)!, SimTime.Hours(4));
            int confused0 = w.Coop.Stats.Confused; // 통합8 다른 사람이 제 자리에서 먼저 헷갈린 날이 있다 — 이 사람이 헷갈릴 때까지
            for (int k = 0; k < 30 && w.Coop.Stats.Confused == confused0; k++) Run(w, SimTime.Minutes(2));
            var conf = w.Log.Entries.LastOrDefault(e => e.CrewId == c.Id && e.Text.Contains("헷갈려")).Text;
            Check("누가 건드린 자리로 돌아오면 헷갈린다 (느려짐 · 건드린 사람에게 서운함)", w.Coop.Stats.Confused > confused0 && conf != null && c.AffinityTo(other) < aff,
                $"헷갈림 {w.Coop.Stats.Confused} · \"{conf}\" · {c.Name}→{other.Name} {aff:0.00} → {c.AffinityTo(other):0.00}");
            Skip1b:;
        }

        // ── 2) 둘이 하는 일: 한 명은 잡고 한 명은 체결 — 짝이 오면 함께, 안 오면 시간 상한 뒤 혼자 · 보류 (교착 없음) ──
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
            PairCall? call = null; CrewMember? helper = null;
            for (int k = 0; k < 90 && !o.Closed; k++)
            {
                Run(w, SimTime.Minutes(2));
                call ??= w.Coop.Calls.FirstOrDefault(x => x.Caller == c.Id);
                if (call is { Arrived: true }) helper ??= w.Crew.FirstOrDefault(x => x.Id == call.Helper);
            }
            Check("무거운 부품: \"누가 좀 잡아 줘\" → 짝이 와서 잡고 체결한다 (함께 한 횟수 · 사이가 가까워진다)", call != null && call.Arrived && helper != null && o.Closed && w.Coop.PairCount(c.Id, helper.Id) >= 1,
                $"부름 {(call == null ? "없음" : $"{call.Part} · 온 사람 {helper?.Name ?? "-"} · {call.Outcome}")} · 끝남 {o.Closed} · 함께 {(helper == null ? 0 : w.Coop.PairCount(c.Id, helper.Id))}번 · {w.Coop.Stats.Line()[..Math.Min(80, w.Coop.Stats.Line().Length)]}");
        }
        {
            // 아무도 안 온다 (거들 사람이 없다): 둘이 동시에 불러도 시간 상한 뒤 혼자 · 보류 — 교착 없음
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
            a.Habits.Remove(Habit.Hasty); a.Habits.Add(Habit.Methodical); // 꼼꼼한 사람은 보류
            foreach (var x in w.Crew) if (x.Job?.Order == o1 || x.Job?.Order == o2) x.EndJob(w, ToilStatus.Interrupted); // 통합8 먼저 집어 든 사람이 있으면 내려놓는다 (그 사람이 보류된 일을 그대로 이어 했다)
            Force(w, a, SpChore(w, a, o1)!, SimTime.Hours(3));
            Force(w, b, SpChore(w, b, o2)!, SimTime.Hours(3));
            long t0 = w.Tick;
            for (int k = 0; k < 120 && w.Coop.Calls.Count(x => x.Caller == a.Id || x.Caller == b.Id) < 2; k++) Run(w, SimTime.Minutes(1)); // 통합7 부품 선반이 배 반대편이면 둘 다 부를 자리에 닿기까지 사십 분이 넘는다 (온다인: 선반 (83,23) → 냉장고) — 부를 때까지 기다린다
            var calls = w.Coop.Calls.Where(x => x.Caller == a.Id || x.Caller == b.Id).ToList();
            long cap = calls.Count > 0 ? calls.Max(x => x.Cap) : t0;
            while (w.Tick < cap + SimTime.Minutes(3)) w.Step();
            bool resolved = calls.Count == 2 && calls.All(x => x.Done) && w.Coop.Stats.Solos + w.Coop.Stats.Holds >= 2;
            string Wf(CrewMember x) => w.Coop.WaitingFor(x) ?? "-";
            Check("짝이 안 오면 시간 상한 뒤 혼자(지그) · 보류 — 둘이 동시에 불러도 교착 없음", resolved && Wf(a) != "짝 기다림" && Wf(b) != "짝 기다림",
                $"부름 {calls.Count} · {string.Join(" / ", calls.Select(x => $"{w.Crew.First(q => q.Id == x.Caller).Name}: {x.Outcome} (상한 {(x.Cap - x.Opened) / SimTime.Minutes(1)}분)"))} · 혼자 {w.Coop.Stats.Solos} · 보류 {w.Coop.Stats.Holds} · 지금 {Wf(a)}/{Wf(b)}");
            var held = new[] { o1, o2 }.FirstOrDefault(x => x.BlockedReason?.Contains("잡아 줄 사람") == true);
            if (held != null)
            {
                Run(w, SimTime.Minutes(30));
                Check("보류한 일은 정말 미뤄진다 (다른 계획이 짧게 미뤄도 한 시간)", held.BlockedUntil > w.Tick && w.Coop.Sites.Any(s => s.OrderId == held.Id && s.State == SiteState.Left),
                    $"{held.Title} 보류 {held.BlockedReason} · 남은 {(held.BlockedUntil - w.Tick) / SimTime.Minutes(1)}분 · 펼친 자리 남음 {w.Coop.Sites.Any(s => s.OrderId == held.Id)}");
            }
        }

        // ── 3) 공구 · 시험대 예약: 먼저 온 사람 · 급한 일 먼저 · 기다림 투덜 · 주 컴퓨터가 순서를 제안 ──
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            var bench = w.Ship.FurnitureOf(FurnitureType.Workbench).OrderBy(x => x.Id).First();
            var spot = bench.UseSpots[0];
            var u1 = SpWorker(w, Skill.Mechanics);
            var u2 = SpWorker(w, Skill.Mechanics, u1);
            var u3 = SpWorker(w, Skill.Mechanics, u1, u2);
            SpQuiet(w, new[] { u1, u2, u3 }, SimTime.Hours(4));
            foreach (var u in new[] { u1, u2, u3 }) Teleport(w, u, w.Ship.RoomAt(spot)!.Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => (x.Center - spot.Center).LengthSquared()).Skip(1).First());
            Job BenchJob(string label, bool urgent) => new(null, label, new Toil[] { new GotoToil(spot), new WorkToil(0.8f, Skill.Mechanics, bench.Center) }) { Urgent = urgent, InterruptMargin = 9f };
            // 통합7 셋째 사람은 급한 일을 받을 때까지 곁에 있게 한다 (스무 분 동안 자유로 두면 배 반대편까지 가 버려 여섯 분 안에 못 와 '급한 일 먼저'가 안 일어났다)
            Force(w, u3, new Job(null, "시험: 곁에서 기다림", new Toil[] { new WaitToil(SimTime.Minutes(30), Pose.Standing) }) { InterruptMargin = 9f }, SimTime.Minutes(30));
            Force(w, u1, BenchJob("시험: 부품 깎기", false), SimTime.Hours(3));
            Run(w, SimTime.Minutes(4));
            Force(w, u2, BenchJob("시험: 부품 시험", false), SimTime.Hours(3));
            Run(w, SimTime.Minutes(16));
            var book = w.Coop.Benches.First(b => b.FurnitureId == bench.Id);
            bool waited = book.User == u1.Id && book.Waiting.Contains(u2.Id) && w.Coop.Stats.BenchWaits >= 1;
            var acts = w.Automation.Book.Acts.Where(a => a.Key.StartsWith("coop:bench")).ToList();
            Check("시험대는 먼저 온 사람이 쓰고 다음 사람은 기다리며 투덜 · 주 컴퓨터가 충돌을 보고 순서를 제안", waited && (w.Coop.Stats.Grumbles >= 1 || acts.Count >= 1),
                $"사용 {w.Crew.First(x => x.Id == book.User).Name} · 대기 {string.Join("·", book.Waiting.Select(id => w.Crew.First(x => x.Id == id).Name))} · 투덜 {w.Coop.Stats.Grumbles} · 컴퓨터 {acts.Count}" + (acts.Count > 0 ? $" \"{acts[0].Observe} → {acts[0].Act}\"" : ""));
            Force(w, u3, BenchJob("시험: 급한 퓨즈", true), SimTime.Hours(3));
            Run(w, SimTime.Minutes(6));
            Check("급한 일은 먼저 쓴다 (쓰던 사람은 손을 멈추고 기다린다)", w.Coop.Stats.Preempts >= 1 && book.User == u3.Id && book.Waiting.FirstOrDefault() == u1.Id,
                $"급한 일 먼저 {w.Coop.Stats.Preempts} · 지금 {w.Crew.First(x => x.Id == book.User).Name} · 대기 {string.Join("·", book.Waiting.Select(id => w.Crew.First(x => x.Id == id).Name))}");
            var order = new List<int> { u1.Id, u3.Id };
            for (int k = 0; k < SimTime.Hours(4) && u2.Job?.Label == "시험: 부품 시험"; k++)
            {
                w.Step();
                if (book.User >= 0 && book.User != order[^1]) order.Add(book.User);
            }
            string Names(IEnumerable<int> ids) => string.Join(" → ", ids.Select(id => w.Crew.First(x => x.Id == id).Name));
            Check("급한 일 → 먼저 온 사람 → 다음 사람 순서로 돌아간다", order.Count >= 4 && order[2] == u1.Id && order[3] == u2.Id,
                $"쓴 순서 {Names(order)} · 기다린 {w.Coop.Stats.BenchMinutes:0}분");
        }

        // ── 4) 옆 설비를 잠시 멈춰야 접근 (전원 · 컴퓨터 승인) ──
        {
            var w = DayOne(seed, "Hanbit");
            Furniture? f = null, g = null;
            foreach (var x in w.Ship.Furniture.Where(x => x.Machine is { Faults.Count: 0 } && !x.Stowed && x.Machine.Spec.ServiceHours > 0f).OrderBy(x => x.Id))
            {
                foreach (var s in x.UseSpots)
                {
                    g = x.Room.Furniture.Where(y => y != x && !y.Stowed && y.Machine != null && CoopSystem.Noisy(y.Type) && y.Cells.Any(cc => Math.Max(Math.Abs(cc.X - s.X), Math.Abs(cc.Y - s.Y)) <= 1)).OrderBy(y => y.Id).FirstOrDefault();
                    if (g != null) break;
                }
                if (g != null) { f = x; break; }
            }
            if (f == null || g == null) Console.WriteLine("   (이 배에는 옆 설비가 붙은 정비 자리가 없다 — 건너뜀)");
            else
            {
                var o = SpMaintain(w, f)!;
                var c = SpWorker(w, f.Machine!.Spec.Skill);
                // 그 설비 옆 칸에서 일하게
                var spot = f.UseSpots.First(s => g.Cells.Any(cc => Math.Max(Math.Abs(cc.X - s.X), Math.Abs(cc.Y - s.Y)) <= 1));
                Teleport(w, c, spot);
                Force(w, c, SpChore(w, c, o)!, SimTime.Hours(4));
                bool sawParked = false;
                for (int k = 0; k < 60 && !o.Closed; k++) { Run(w, SimTime.Minutes(2)); sawParked |= g.Machine!.Parked; }
                Run(w, SimTime.Minutes(5));
                var acts = w.Automation.Book.Acts.Where(a => a.Key.StartsWith("coop:ok") || a.Key.StartsWith("coop:deny")).ToList();
                bool ok = w.Coop.Stats.Pauses >= 1 && sawParked || w.Coop.Stats.Denied >= 1 || w.Coop.Stats.Careful >= 1;
                Check("옆 설비를 잠시 멈추고 손을 넣는다 (생명 유지 설비는 주 컴퓨터 승인 · 거절되면 조심조심)", ok && o.Closed && (!sawParked || !g.Machine!.Parked || w.Coop.Stats.Forgotten > 0),
                    $"{f.Label} 옆 {g.Label}{(CoopSystem.Critical(g.Type) ? " (승인 필요)" : "")} · 멈춤 {w.Coop.Stats.Pauses} · 승인 {w.Coop.Stats.Approved} · 거절 {w.Coop.Stats.Denied} · 다시 켬 {w.Coop.Stats.Restored} · 지금 {(g.Machine!.Parked ? "꺼짐" : "돎")} · " +
                    string.Join(" / ", acts.Take(2).Select(a => $"{a.Judge} → {a.Act}")));
            }
        }

        // ── 5) 앞 상자를 치워야 큰 부품 · 급하면 바닥에 둔 채 · 통로의 상자를 누가 치운다 ──
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
            Teleport(w, c, shelf.UseSpots[0]); // 장면은 선반 앞에서 — 배 반대편에서 걸어오면 40분을 다 썼다 (시각마다 있는 곳이 달라 갈렸다)
            Force(w, c, new Job(null, "시험: 모터 꺼내기", new Toil[] { new GotoToil(shelf.UseSpots[0]), new TakeToil(shelf, ItemKind.Motor, 1), new WaitToil(SimTime.Minutes(5), Pose.Standing) }), SimTime.Hours(1));
            bool sawBox = false;
            for (int k = 0; k < 40 && c.Carrying?.Kind != ItemKind.Motor; k++) { Run(w, SimTime.Minutes(1)); sawBox |= w.Coop.Boxes.Any(b => b.ShelfId == shelf.Id); }
            Check("꽉 찬 선반에서 큰 부품: 앞 상자부터 바닥에 내리고 꺼낸 뒤 되밀어 넣는다", w.Coop.Stats.Digs >= 1 && sawBox && c.Carrying?.Kind == ItemKind.Motor && !w.Coop.Boxes.Any(b => b.ShelfId == shelf.Id),
                $"채움 {inv.Total}/{inv.Capacity} · 앞 상자 {w.Coop.Stats.Digs} · 들었나 {c.Carrying}");
            c.Carrying = null;
            inv.Add(ItemKind.Motor, 1);
            Force(w, c, new Job(null, "시험: 급히 모터", new Toil[] { new GotoToil(shelf.UseSpots[0]), new TakeToil(shelf, ItemKind.Motor, 1), new WaitToil(SimTime.Minutes(5), Pose.Standing) }) { Urgent = true }, SimTime.Minutes(20));
            for (int k = 0; k < 20 && c.Carrying?.Kind != ItemKind.Motor; k++) Run(w, SimTime.Minutes(1));
            bool leftBox = w.Coop.Boxes.Any(b => b.ShelfId == shelf.Id && b.Left);
            c.Carrying = null;
            for (int k = 0; k < 48 && w.Coop.Boxes.Any(b => b.ShelfId == shelf.Id); k++) Run(w, SimTime.Minutes(5));
            Check("급하면 앞 상자를 바닥에 둔 채 가고 — 나중에 누가 선반에 되돌린다", leftBox && w.Coop.Stats.BoxesLeft >= 1 && w.Coop.Stats.BoxesTidied >= 1,
                $"바닥에 둠 {w.Coop.Stats.BoxesLeft} · 되돌림 {w.Coop.Stats.BoxesTidied} · " + (w.Log.Entries.LastOrDefault(e => e.Text.Contains("상자를 선반")).Text ?? "-"));
        }

        // ── 6) 카트가 좁은 문(격벽 · 휜 문틀)을 못 지나 짐을 옮겨 싣는다 ──
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            var cart = w.Portable.Devices.First(d => d.Kind == PortableKind.Cart);
            var c = SpWorker(w, Skill.Mechanics);
            var door = w.Ship.Doors.Where(d => !d.IsExternal && !d.Removed && d.RoomA != null && d.RoomB != null && !d.RoomA.Detached && !d.RoomB.Detached && w.Paths.Find(c.Cell, d.Cell, c.PathProfile) != null)
                .OrderBy(d => w.Paths.Find(c.Cell, d.Cell, c.PathProfile)!.Count).First();
            door.Bent = 0.25f; // 문틀이 휘었다
            var beyond = (door.RoomA == c.Room ? door.RoomB! : door.RoomA!).Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => (x.Center - door.Cell.Center).LengthSquared()).First();
            var near = (door.RoomA == c.Room ? door.RoomA! : door.RoomB!).Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => (x.Center - door.Cell.Center).LengthSquared()).Skip(2).First();
            Teleport(w, c, near);
            cart.HeldBy = c; cart.Stored = false;
            long t0 = w.Tick;
            Force(w, c, new Job(Brain.Activities.OfType<PortableActivity>().First(), "시험: 카트 밀기", new Toil[] { new GotoToil(beyond), new WaitToil(SimTime.Minutes(20), Pose.Standing) }), SimTime.Hours(1));
            for (int k = 0; k < 400 && c.Cell != beyond; k++) w.Step();
            long took = w.Tick - t0;
            cart.HeldBy = null; cart.Stored = true;
            Check("카트가 휜 문틀을 못 넘는다 — 짐을 내려 옮겨 싣고 지난다 (시간이 든다)", w.Coop.Stats.Transfers >= 1 && c.Cell == beyond && took > SimTime.Minutes(1),
                $"옮겨 싣기 {w.Coop.Stats.Transfers}(거듦 {w.Coop.Stats.TransfersHelped}) · 걸린 {took * 60f / SimTime.TicksPerHour:0.0}분 · " + (w.Log.Entries.LastOrDefault(e => e.Text.Contains("옮겨 싣")).Text ?? "-"));
        }

        // ── 7) 배식 줄 새치기 → 다툼 → 저녁 자리 배치까지 바뀐다 (대조: 새치기 없을 때) ──
        float SeatGap(bool cut, bool kind, out World w, out CrewMember victim, out CrewMember cutter)
        {
            w = DayOne(seed, "Hanbit");
            var ww = w;
            var mess = w.Ship.RoomsOf(RoomType.Mess).OrderBy(r => r.Id).First();
            var disp = mess.Furniture.FirstOrDefault(f => f.Type == FurnitureType.MealDispenser) ?? w.Ship.FurnitureOf(FurnitureType.MealDispenser).First();
            disp.Storage!.Add(ItemKind.Meal, 12);
            var people = w.Crew.Where(c => c.CanAct && !c.IsChild).OrderBy(c => c.Id).Take(3).ToList();
            var (a, v, x) = (people[0], people[1], people[2]);
            victim = v; cutter = x;
            SpQuiet(w, people, SimTime.Hours(3));
            foreach (var p in people) { p.Needs.Food = 0.05f; p.Affinity.Clear(); p.Habits.Remove(Habit.Patient); p.Habits.Remove(Habit.Generous); p.Habits.Remove(Habit.Hasty); p.Habits.Remove(Habit.ShortTempered); p.Habits.Remove(Habit.Grumbler); p.Habits.Remove(Habit.Cheerful); }
            if (kind) { v.Habits.Add(Habit.Patient); v.Habits.Add(Habit.Generous); } else { v.Habits.Add(Habit.ShortTempered); v.Habits.Add(Habit.Grumbler); }
            x.Habits.Add(Habit.Hasty); x.Habits.Add(Habit.ShortTempered);
            foreach (var p in people) Teleport(w, p, disp.UseSpots[0] + new Cell(0, 0));
            Run(w, 2);
            Force(w, x, new Job(null, "시험: 잠깐", new Toil[] { new WaitToil(SimTime.Hours(1), Pose.Standing) }), SimTime.Hours(1)); // 제 차례 전에 스스로 먹으러 가지 않게
            Force(w, a, SpEat(w, a), SimTime.Hours(2));
            Run(w, 3);
            Force(w, v, SpEat(w, v), SimTime.Hours(2));
            Run(w, SimTime.Minutes(1));
            if (cut) w.Coop.Queues.ForceCut = x.Id;
            Force(w, x, SpEat(w, x), SimTime.Hours(2));
            System.Numerics.Vector2? sv = null, sx = null;
            for (int k = 0; k < SimTime.Hours(1.2f) && (sv == null || sx == null); k++)
            {
                w.Step();
                if (sv == null && v.Pose == Pose.Sitting && v.Job?.Activity is EatActivity) sv = v.Position;
                if (sx == null && x.Pose == Pose.Sitting && x.Job?.Activity is EatActivity) sx = x.Position;
            }
            return sv is { } p1 && sx is { } p2 ? (p1 - p2).Length() : -1f;
        }
        {
            float control = SeatGap(false, false, out var w0, out _, out _);
            float gap = SeatGap(true, false, out var w, out var v, out var x);
            var qs = w.Coop.Queues;
            var mem = w.Relations.Of(v, x).FirstOrDefault(m => m.Reason == RelationReason.CutInLine);
            float anger = w.Brain2.Emotions.Get(v, Feeling.Anger);
            Check("배식 줄에 새치기 → 바로 뒷사람(욱하는 성격)과 말다툼 (분노 · 서운함 · 말다툼 기록)", qs.Stats.Cuts >= 1 && qs.Stats.Quarrels >= 1 && mem != null && anger > 0.2f && v.Quarrel > 0,
                $"새치기 {qs.Stats.Cuts} · 다툼 {qs.Stats.Quarrels} · {v.Name} 분노 {anger:0.00} · 기억 \"{mem?.Text}\" · 사이 {v.AffinityTo(x):0.00} · " + (w.Log.Entries.LastOrDefault(e => e.Text.Contains("말다툼")).Text ?? "-"));
            Check("다툰 두 사람은 저녁 자리를 떨어져 앉는다 (대조: 새치기 없을 때보다 멀리)", gap >= 2.5f && (control < 0f || gap > control + 0.5f) && qs.Stats.SeatAway + (gap >= 2.5f ? 1 : 0) >= 1,
                $"자리 사이 {gap:0.0}칸 (새치기 없을 때 {control:0.0}칸) · 떨어져 앉음 {qs.Stats.SeatAway} · " + (w.Log.Entries.LastOrDefault(e => e.Text.Contains("떨어진 자리")).Text ?? "-"));
            // 다음 끼니에도 (자리 고르기 가중치)
            var mess = w.Ship.RoomsOf(RoomType.Mess).OrderBy(r => r.Id).First();
            var nearX = mess.Furniture.Where(f => f.Type == FurnitureType.Seat).OrderBy(f => (f.Center - x.Position).LengthSquared()).First();
            Check("줄에서 다툰 기억이 다음 자리 고르기에도 남는다 (가중치)", qs.SeatBias(v, nearX) > 10f, $"{x.Name} 곁 의자 가중치 {qs.SeatBias(v, nearX):0}");
            float kindGap = SeatGap(true, true, out var wk, out var vk, out var xk);
            var qk = wk.Coop.Queues;
            var thanks = wk.Relations.Of(xk, vk).FirstOrDefault(m => m.Reason == RelationReason.LetMeFirst);
            Check("너그러운 사람은 양보한다 → 고마움 (호감) · 곁에 앉기", qk.Stats.Yields >= 1 && thanks != null && xk.AffinityTo(vk) > 0.04f,
                $"양보 {qk.Stats.Yields} · \"{thanks?.Text}\" · {xk.Name}→{vk.Name} {xk.AffinityTo(vk):0.00} · 자리 사이 {kindGap:0.0}칸 · 곁에 앉음 {qk.Stats.SeatNear}");
        }

        // ── 8) 줄 길이 · 기다림을 주 컴퓨터가 보고 급하지 않은 사람에게 나중에 오라 한다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var mess = w.Ship.RoomsOf(RoomType.Mess).OrderBy(r => r.Id).First();
            var disp = mess.Furniture.FirstOrDefault(f => f.Type == FurnitureType.MealDispenser) ?? w.Ship.FurnitureOf(FurnitureType.MealDispenser).First();
            disp.Storage!.Add(ItemKind.Meal, 12);
            var people = w.Crew.Where(c => c.CanAct && !c.IsChild).OrderBy(c => c.Id).Take(6).ToList();
            SpQuiet(w, people, SimTime.Hours(3));
            for (int i = 0; i < people.Count; i++) { var p = people[i]; p.Needs.Food = i < 2 ? 0.05f : 0.55f; Teleport(w, p, disp.UseSpots[0]); }
            foreach (var p in people) { Force(w, p, SpEat(w, p), SimTime.Hours(2)); Run(w, 2); }
            Run(w, SimTime.Minutes(3));
            var q = w.Coop.Queues.All.FirstOrDefault(x => x.FurnitureId == disp.Id);
            int lineLen = q?.Line.Count ?? 0;
            Run(w, SimTime.Minutes(8));
            var act = w.Automation.Book.Acts.LastOrDefault(a => a.Key.StartsWith("queue:"));
            var held = people.Where(p => p.HoldWhy?.Contains("컴퓨터") == true).ToList();
            Check("줄 자리 · 번호: 받는 칸 뒤로 한 줄 (사람마다 다른 칸)", q != null && q.Slots.Count >= 3 && q.MaxLen >= 3,
                $"줄 {lineLen}명 (가장 길 때 {q?.MaxLen}) · 자리 {q?.Slots.Count} · {string.Join(" ", q?.Slots.Take(5).Select(s => s.ToString()) ?? Array.Empty<string>())}");
            Check("주 컴퓨터가 붐비는 줄을 보고 순서를 나눈다 (믿는 사람은 나중에 온다)", act != null && w.Coop.Queues.Stats.Advised >= 1,
                $"{act?.Observe} · {act?.Judge} → {act?.Request} · 나중에 옴 {w.Coop.Queues.Stats.Deferred}({string.Join("·", held.Select(p => p.Name))})");
        }

        // ── 9) 사고 현장에 구경꾼이 몰려 길을 막고 · 책임자가 "비켜!" · 구경꾼은 소문을 퍼뜨린다 ──
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 15f);
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Storage or RoomType.Workshop or RoomType.Lounge or RoomType.Hydroponics && r.Doors.Any(d => !d.IsExternal && (d.RoomA?.Type == RoomType.Corridor || d.RoomB?.Type == RoomType.Corridor)))
                .OrderBy(r => r.Id).First();
            float hr = SimTime.HourOfDay(w.Tick);
            bool Off(CrewMember c) => !SimTime.InWindow(hr, c.Schedule.WorkStart, c.Schedule.WorkLength) && !SimTime.InWindow(hr, c.Schedule.SleepStart, c.Schedule.SleepLength);
            // 비번이고 깨어 있는 사람 (근무 중인 사람은 일을 놓지 않는다)
            var curious = w.Crew.Where(c => c.CanAct && !c.IsChild).OrderByDescending(c => Off(c)).ThenByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).Take(5).ToList();
            Console.WriteLine($"   (구경할 만한 사람: {string.Join(" · ", curious.Select(c => $"{c.Name}{(Off(c) ? "(비번)" : "(근무)")}"))})");
            var corridor = room.Doors.Select(d => d.RoomA == room ? d.RoomB : d.RoomA).First(r => r?.Type == RoomType.Corridor)!;
            foreach (var c in curious)
            {
                c.Habits.Remove(Habit.Loner); c.Habits.Add(Habit.Gazer); c.Habits.Add(Habit.Talker);
                ScFree(w, c); // 통합7 다섯 중 비번이 한 명뿐인 날엔 구경꾼이 한 명이라 문 앞이 막히지 않았다 — 다섯 다 쉬는 시간으로 세운다
                Teleport(w, c, corridor.Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => (x.Center - room.Center).LengthSquared()).Skip(3 + curious.IndexOf(c)).First());
                c.Needs.Food = 0.95f;
                Force(w, c, new Job(null, "시험: 쉬는 중", new Toil[] { new WaitToil(SimTime.Minutes(2), Pose.Standing) }), 1);
            }
            // 통합7 소화 가스 방침이면 사람은 문을 닫고 가스를 기다린다 (한빛호 창고: 15분 동안 아무도 소화기를 들고 오지 않아 비집을 사람이 없었다) — 사람이 끄러 가는 배로 세운다
            FleetSystem.NoDoorGuard = true; // 구경꾼만 보는 장면 — 소방 로봇이 먼저 들어간 불의 문 앞 대기는 끈다 (문 앞 자리를 먼저 차지해 구경꾼 무리가 서지 않는다)
            w.Policies.Set("inertfire", 0, "시험");
            w.Policies.Set("vacuumfire", 0, "시험");
            foreach (var fc in room.Cells.Where(w.Ship.IsOpenFloor).OrderBy(x => (x.Center - room.Center).LengthSquared()).Take(3)) Incidents.Fire(w, fc);
            int maxWatch = 0; bool squeezed = false; int peakCost = 0;
            var cs = w.Coop.Crowds;
            for (int k = 0; k < SimTime.Minutes(60) && (cs.Stats.Shouts == 0 || k < SimTime.Minutes(20)); k++)
            {
                w.Step();
                if (k % 5 != 0) continue;
                int now = cs.Scenes.Where(s => !s.Ended).Sum(s => s.Watchers.Count);
                maxWatch = Math.Max(maxWatch, now);
                squeezed |= cs.Stats.Squeezes > 0;
                foreach (var cell in cs.CrowdCells()) peakCost = Math.Max(peakCost, w.Paths.CellBody[w.Ship.Grid.Index(cell)]);
            }
            Console.WriteLine($"   구경 {cs.Stats.Line()}");
            Check("사고 현장 문 앞에 구경꾼이 몰려 길을 막는다 (길찾기 비용 · 비집고 지나는 대응자)", maxWatch >= 2 && peakCost > 0 && (squeezed || cs.Stats.Shouts > 0),
                $"가장 많을 때 {maxWatch}명 · 구경꾼 칸 비용 {peakCost} · 비집음 {cs.Stats.Squeezes} · 현장 {cs.Stats.Scenes}");
            FleetSystem.NoDoorGuard = false;
            var shout = w.Log.Entries.LastOrDefault(e => e.Text.Contains("비켜")).Text;
            Check("책임자(지휘 · 당직 · 대응자)가 \"비켜!\" — 구경꾼이 물러난다", cs.Stats.Shouts >= 1 && cs.Stats.Obeyed + cs.Stats.ComputerShoos >= 1 && shout != null,
                $"\"비켜!\" {cs.Stats.Shouts} · 물러남 {cs.Stats.Obeyed} · 버팀 {cs.Stats.Ignored} · 컴퓨터 방송 {cs.Stats.ComputerCalls} · \"{shout}\"");
            for (int k = 0; k < 180 && cs.Stats.Rumors == 0; k++) Run(w, SimTime.Minutes(1));
            var heard = cs.Heard.Where(h => w.Brain2.Beliefs.Get(w.Crew.First(c => c.Id == h.listener), Topic.Fire, room.Id) != null).Select(h => $"{w.Crew.First(c => c.Id == h.teller).Name}→{w.Crew.First(c => c.Id == h.listener).Name}{(h.big ? "(부풀림)" : "")}").ToList();
            Check("구경꾼은 본 것을 소문으로 퍼뜨린다 (믿음 — 말 많은 사람은 부풀린다)", cs.Stats.Rumors >= 1 && (heard.Count >= 1 || !BrainSystem.Enabled),
                $"소문 {cs.Stats.Rumors}(부풀림 {cs.Stats.Exaggerated}) · 소문으로 안 사람 {string.Join("·", heard)}");
        }

        // ── 10) 결정론 · 성능 ──
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 · 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");
            var sw = Stopwatch.StartNew();
            var big = World.CreateDefault(seed, 0, "Cheonma");
            CoopSystem.UpdateTicks = 0;
            Run(big, SimTime.TicksPerDay);
            sw.Stop();
            double coop = CoopSystem.UpdateTicks * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"   30인 배 하루: 협력 {big.Coop.Stats.Line()}");
            Console.WriteLine($"   30인 배 하루: 줄 {big.Coop.Queues.Stats.Line()} · 구경 {big.Coop.Crowds.Stats.Line()}");
            Check("성능: 30인 배(천마) 하루 — 협력 · 줄 · 구경꾼 틱이 전체의 3% 아래", coop < sw.Elapsed.TotalMilliseconds * 0.03,
                $"전체 {sw.Elapsed.TotalMilliseconds:0}ms · 협력 시스템 {coop:0.0}ms ({coop / sw.Elapsed.TotalMilliseconds * 100:0.00}%) · 승무원 {big.Crew.Count}명 · 줄 {big.Coop.Queues.Stats.Served}번 받음");
        }

        return _fails == 0 ? 0 : 1;
    }
}
