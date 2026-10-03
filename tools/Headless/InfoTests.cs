using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v17.3 개인 자리 · 못 끝낸 일 · 정보 차이 · 선내 메신저 · 사진 · 공동 장부 — 검증 장면을 헤드리스에서 실제로 일으킨다
public static partial class Program
{
    private static List<CrewMember> IfAdults(World w) => w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct && !c.Away && !c.Outside).OrderBy(c => c.Id).ToList();

    private static void IfIdle(World w, CrewMember c, long ticks = 0) =>
        Force(w, c, new Job(null, "시험: 잠깐 서 있음", new Toil[] { new WaitToil(SimTime.Minutes(1), Pose.Standing) }), ticks > 0 ? ticks : SimTime.Minutes(1));

    private static string IfLog(World w, int crew, string part) =>
        w.Log.Entries.LastOrDefault(e => (crew < 0 || e.CrewId == crew) && e.Text.Contains(part)).Text ?? "";

    private static Room? IfNextRoom(World w, Room room) =>
        w.Ship.Doors.Select(d => d.RoomA == room ? d.RoomB : d.RoomB == room ? d.RoomA : null).Where(r => r != null && r != room && !r.OffLimits).OrderBy(r => r!.Id).FirstOrDefault();

    private static Cell IfFloor(World w, Room r) => r.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.DoorAt(x) == null).OrderBy(x => (x.Center - r.Center).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).First();

    /// <summary>그 시각에 근무도 잠도 아닌가.</summary>
    private static bool IfFree(CrewMember c, float h) =>
        !SimTime.InWindow(h, c.Schedule.WorkStart, c.Schedule.WorkLength) && !SimTime.InWindow(h, c.Schedule.SleepStart, c.Schedule.SleepLength)
        && !SimTime.InWindow((h + 1.5f) % 24f, c.Schedule.SleepStart, c.Schedule.SleepLength);

    /// <summary>근무를 마치고 잠들기 전 (그 사람이 한가한 때).</summary>
    private static float IfFreeHour(CrewMember c)
    {
        for (float d = 0.5f; d < 24f; d += 0.5f)
        {
            float h = (c.Schedule.WorkStart + c.Schedule.WorkLength + d) % 24f;
            if (IfFree(c, h) && IfFree(c, (h + 2f) % 24f)) return h;
        }
        return (c.Schedule.WorkStart + c.Schedule.WorkLength + 0.5f) % 24f;
    }

    private static int RunInfoTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"개인 자리 · 못 끝낸 일 · 정보 차이 · 선내 메신저 · 사진 · 공동 장부 점검 (v17.3) · 시드 {seed}\n");

        // ── 1) 컵이 깨지고 → 소리만 들은 사람이 확인하러 → 발견 · 메신저 → 주인은 발견자를 의심 → 본 사람의 해명 → 사과 ──
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 16.5f);
            var info = w.Info;
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            var next = IfNextRoom(w, mess)!;
            var adults = IfAdults(w).OrderBy(c => IfFree(c, 16.5f) && IfFree(c, 18.5f) ? 0 : IfFree(c, 16.5f) ? 1 : 2).ThenBy(c => c.Id).ToList();
            bool Mild(CrewMember c) => Life.Has(c, Habit.Patient) || Life.Has(c, Habit.Optimist) || Life.Has(c, Habit.Generous);
            var owner = adults.First(c => !Mild(c) && w.Belongings.All.Any(b => b.Owner == c.Id && b.Kind == BelongingKind.Mug && b.Usable));
            var finder = adults.First(c => c != owner && !Life.Has(c, Habit.Loner) && c.Traits.Sociability >= 0.25f);
            var witness = adults.First(c => c != owner && c != finder && !Life.Has(c, Habit.Talker) && !Life.Has(c, Habit.Joker));
            owner.ChangeAffinity(finder, -0.3f - owner.AffinityTo(finder)); // 원래 좀 서먹한 사이
            var keep = new[] { owner, finder, witness };
            info.Intents.RemoveAll(i => keep.Any(c => c.Id == i.Crew)); // 전날 일은 접어 둔다
            SpQuiet(w, keep, SimTime.Hours(9));
            var table = mess.Furniture.First(f => f.Type == FurnitureType.Table);
            var seat = mess.Furniture.Where(f => f.Type == FurnitureType.Seat && f.UseSpots.Count > 0).OrderBy(f => (f.Center - table.Center).LengthSquared()).ThenBy(f => f.Id).First();
            var far = mess.Furniture.Where(f => f.Type == FurnitureType.Seat && f.UseSpots.Count > 0 && f != seat).OrderByDescending(f => (f.Center - table.Center).LengthSquared()).ThenBy(f => f.Id).First();
            Teleport(w, witness, far.UseSpots[0]);
            Force(w, witness, new Job(null, "시험: 구석에서 책", new Toil[] { new WaitToil(SimTime.Minutes(30), Pose.Sitting, table.Center) }), SimTime.Minutes(30));
            Teleport(w, finder, IfFloor(w, next));
            IfIdle(w, finder);
            var home = owner.Bed?.Room ?? w.Ship.RoomsOf(RoomType.Quarters).First();
            Teleport(w, owner, IfFloor(w, home));
            Force(w, owner, new Job(null, "시험: 침실에서 쉼", new Toil[] { new WaitToil(SimTime.Minutes(80), Pose.Sitting) }) { InterruptMargin = 9f }, SimTime.Minutes(80)); // 주인은 침실에 (식당엔 안 간다 — 메신저로만 안다)
            Run(w, 2);
            var cup = w.Belongings.All.First(b => b.Owner == owner.Id && b.Kind == BelongingKind.Mug && b.Usable);
            foreach (var tc in info.OnTable.Values.ToList()) { info.OnTable.Remove(tc.Cup); if (w.Belongings.Get(tc.Cup) is Belonging ob) ob.At = null; }
            info.PutOnTable(owner, cup, table.Cells[0], seat.UseSpots[0], left: true);
            int broke = info.Jolt(mess, 0.9f, "자세 제어 분사", force: true);
            var k = info.Cases.LastOrDefault();
            Check("배가 덜컹하자 식탁 끝 컵이 떨어져 깨진다 (조각 → 바닥 유리 표시)", broke == 1 && k != null && !cup.Usable && w.Body.Mark(k.At, CellMark.Glass) > 0.3f,
                $"깨짐 {broke} · 컵 상태 {cup.Condition:0.00} · 유리 {(k != null ? w.Body.Mark(k.At, CellMark.Glass) : 0f):0.00} · {IfLog(w, -1, "떨어져 깨졌다")}");
            if (k == null) return 1;
            Check("같은 방 사람은 보고(원인을 안다) · 옆방 사람은 소리만 듣는다", k.Saw.Contains(witness.Id) && k.Heard.Contains(finder.Id) && !k.Saw.Contains(owner.Id)
                && w.Brain2.Beliefs.Get(witness, Topic.Thing, cup.Id)?.Value == 3 && w.Brain2.Beliefs.Get(owner, Topic.Thing, cup.Id) == null,
                $"본 사람 {string.Join("·", k.Saw.Select(i => w.Crew[i].Name))} · 소리만 {string.Join("·", k.Heard.Select(i => w.Crew[i].Name))} · 주인 {owner.Name}은(는) 모른다");
            long t0 = w.Tick;
            bool sawCheckJob = false;
            for (int i = 0; i < 90 && k.Finder < 0; i++)
            {
                Run(w, SimTime.Minutes(1));
                if (finder.Job?.Activity is InfoActivity) sawCheckJob = true;
            }
            Check("소리만 들은 사람이 확인하러 간다 → 처음 발견하고 메신저에 알린다", sawCheckJob && k.Finder == finder.Id && w.Info.Chat.All.Any(m => m.Kind == ChatKind.Notice && m.Case == k.Id),
                $"발견자 {(k.Finder >= 0 ? w.Crew[k.Finder].Name : "-")} · {(w.Tick - t0) / SimTime.Minutes(1)}분 · \"{w.Info.Chat.All.LastOrDefault(m => m.Case == k.Id)?.Text}\" · {IfLog(w, finder.Id, "소리")}");
            for (int i = 0; i < 240 && k.Accused < 0; i++) Run(w, SimTime.Minutes(1));
            var blame = w.Relations.Of(finder, owner).FirstOrDefault(m => m.Reason == RelationReason.BlamedMe);
            Check("주인은 본 적이 없어 발견자를 의심하고 찾아가 몰아붙인다 (억울한 사람은 서운하다)", k.Suspect == finder.Id && k.Accused >= 0 && blame != null && info.SpatWith(owner, finder) != null,
                $"의심 {(k.Suspect >= 0 ? w.Crew[k.Suspect].Name : "-")} · 몰아붙임 {(k.Accused >= 0 ? SimTime.Clock(k.Accused) : "-")} · 기억: {blame?.Text} · 믿음: {(w.Brain2.Beliefs.Get(owner, Topic.Thing, cup.Id) is Belief bb ? w.Brain2.Beliefs.Describe(bb) : "-")}");
            bool dbg = Environment.GetEnvironmentVariable("INFO_DEBUG") == "1";
            for (int i = 0; i < 60 * 20 && k.Apologized < 0; i++)
            {
                Run(w, SimTime.Minutes(1));
                if (dbg && i % 10 == 0) Console.WriteLine($"     [{SimTime.Clock(w.Tick)}] 본 사람 {witness.Name} ({witness.Room?.Name}): {witness.Job?.Label ?? "-"} · 안 읽음 {info.Chat.Unread(witness)} · 못 봄 {info.Chat.CannotRead(witness)} · 할 일 {string.Join(",", info.Intents.Where(x => x.Crew == witness.Id).Select(x => x.Do + ":" + x.Score.ToString("0.00")))} · 몰아붙임 {k.Accused} · 판단 {string.Join(" / ", witness.LastEvaluations?.Take(3).Select(e => e.Activity.Id + ":" + e.Score.ToString("0.00")) ?? Array.Empty<string>())}");
            }
            var sorry = w.Relations.Of(finder, owner).FirstOrDefault(m => m.Reason == RelationReason.Apologized);
            Check("본 사람이 \"충격에 떨어졌다\"고 해명 → 주인이 사과한다 (오해가 풀리고 몰아붙인 사람의 신용은 깎인다)",
                k.ExplainedBy == witness.Id && k.Apologized >= 0 && sorry != null && blame is { Revealed: true } && info.Cred(owner) < info.Cred(witness),
                $"해명 {(k.ExplainedBy >= 0 ? w.Crew[k.ExplainedBy].Name : k.ExplainedBy.ToString())} {(k.Explained >= 0 ? SimTime.Clock(k.Explained) : "-")} · 사과 {(k.Apologized >= 0 ? SimTime.Clock(k.Apologized) : "-")} · 신용 주인 {info.Cred(owner):0.00} / 본 사람 {info.Cred(witness):0.00} · {IfLog(w, owner.Id, "사과")}");
            Check("주인은 깨진 컵을 붙일 일을 못 끝낸 일로 품는다 (두뇌 중기 목표)", info.Todos.Any(t => t.Kind == TodoKind.MendCup && t.Item == cup.Id),
                string.Join(" · ", info.TodosOf(owner).Select(t => InfoSystem.TodoText(t, w))));
            Console.WriteLine($"   메신저: {string.Join(" / ", w.Info.Chat.All.Where(m => m.Tick >= t0).Take(6).Select(m => $"{(m.Author >= 0 ? w.Crew[m.Author].Name : "주 컴퓨터")}: {m.Text}"))}");
        }

        // ── 1b) 본 사람이 없으면 주 컴퓨터가 그 시각 기록을 댄다 ──
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 16.5f);
            var info = w.Info;
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            var adults = IfAdults(w).OrderBy(c => IfFree(c, 16.5f) ? 0 : 1).ThenBy(c => c.Id).ToList();
            var owner = adults.First(c => !Life.Has(c, Habit.Patient) && !Life.Has(c, Habit.Optimist) && !Life.Has(c, Habit.Generous) && w.Belongings.All.Any(b => b.Owner == c.Id && b.Kind == BelongingKind.Mug && b.Usable));
            var other = adults.First(c => c != owner);
            owner.ChangeAffinity(other, -0.4f - owner.AffinityTo(other));
            SpQuiet(w, new[] { owner, other }, SimTime.Hours(9));
            var table = mess.Furniture.First(f => f.Type == FurnitureType.Table);
            var seat = mess.Furniture.Where(f => f.Type == FurnitureType.Seat && f.UseSpots.Count > 0).OrderBy(f => (f.Center - table.Center).LengthSquared()).ThenBy(f => f.Id).First();
            Teleport(w, owner, IfFloor(w, owner.Bed?.Room ?? w.Ship.RoomsOf(RoomType.Quarters).First()));
            IfIdle(w, owner);
            Run(w, 2); // 방이 바뀐 걸 반영
            var cup = w.Belongings.All.First(b => b.Owner == owner.Id && b.Kind == BelongingKind.Mug && b.Usable);
            foreach (var tc in info.OnTable.Values.ToList()) { info.OnTable.Remove(tc.Cup); if (w.Belongings.Get(tc.Cup) is Belonging ob) ob.At = null; }
            info.PutOnTable(owner, cup, table.Cells[0], seat.UseSpots[0], left: true);
            info.Jolt(mess, 0.8f, "자세 제어 분사", force: true);
            var k = info.Cases.Last();
            // 주인이 와서 보고, 그 방에 있던 사람을 의심한다
            Teleport(w, other, IfFloor(w, mess));
            Force(w, other, new Job(null, "시험: 식당에 서 있음", new Toil[] { new WaitToil(SimTime.Hours(1), Pose.Standing) }) { InterruptMargin = 9f }, SimTime.Hours(1));
            Teleport(w, owner, IfFloor(w, mess));
            info.Intents.RemoveAll(i => i.Crew == owner.Id || i.Crew == other.Id);
            Force(w, owner, new Job(null, "시험: 둘러봄", new Toil[] { new WaitToil(SimTime.Minutes(8), Pose.Standing) }) { InterruptMargin = 9f }, SimTime.Minutes(8));
            for (int i = 0; i < 60 * 20 && (k.Accused < 0 || k.Apologized < 0); i++)
            {
                Run(w, SimTime.Minutes(1));
                if (Environment.GetEnvironmentVariable("INFO_DEBUG") == "1" && (i < 12 || i % 60 == 0)) Console.WriteLine($"     [{SimTime.Clock(w.Tick)}] 1b 주인 {owner.Name} ({owner.Room?.Name}) · 다른 {other.Name} ({other.Room?.Name}) · 앎 {string.Join(",", k.Knows)} · 발견 {k.Finder} · 쓸림 {k.Swept} · 유리 {w.Body.Mark(k.At, CellMark.Glass):0.00} · 주인 앎 {k.OwnerKnows}/{k.OwnerTruth} · 의심 {k.Suspect} · 물음 {k.Asked}");
            }
            Check("본 사람이 없으면: 몰아붙인 뒤 주 컴퓨터가 메신저에 그 시각 기록(덜컹)을 대고, 주인은 믿는 만큼 받아들여 사과한다",
                k.Accused >= 0 && k.ComputerSaid >= 0 && (k.ExplainedBy == -2 && k.Apologized >= 0 || w.Automation.Trusts.Of(owner) < 0.35f),
                $"의심 {(k.Suspect >= 0 ? w.Crew[k.Suspect].Name : "-")} · 컴퓨터 {(k.ComputerSaid >= 0 ? SimTime.Clock(k.ComputerSaid) : "-")} · 컴퓨터 신뢰 {w.Automation.Trusts.Of(owner):0.00} · 사과 {(k.Apologized >= 0 ? SimTime.Clock(k.Apologized) : "-")} · \"{info.Chat.All.LastOrDefault(m => m.Kind == ChatKind.Computer)?.Text}\"");
        }

        // ── 2) 메신저를 못 본 사람만 회의 시간 변경을 모르고 늦는다 (컴퓨터가 읽음 표시를 보고 부른다) ──
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.Minutes(30));
            var info = w.Info;
            float baseHour = w.Meetings.Hour;
            RunUntilHour(w, baseHour - 3f);
            var cap = w.Command.Captain ?? IfAdults(w).First();
            var adults = IfAdults(w).Where(c => c != cap && c.IsAwake).ToList();
            var x = adults.Where(c => c.Bed != null).OrderByDescending(c => c.Id).First();
            info.Chat.LeaveTablet(x, 9f);
            var work = w.Ship.Rooms.Where(r => r.Type is not (RoomType.Quarters or RoomType.Mess or RoomType.MeetingRoom or RoomType.Corridor or RoomType.Lounge) && !r.OffLimits && r.Cells.Any(c => w.Ship.IsOpenFloor(c))).OrderBy(r => r.Id).First();
            Teleport(w, x, IfFloor(w, work));
            x.Needs.Food = 1f;
            Force(w, x, new Job(null, "시험: 작업실에서 일", new Toil[] { new WaitToil(SimTime.Hours(2.5f), Pose.Working) }) { InterruptMargin = 0.6f }, SimTime.Hours(2.5f)); // 점심도 거르고 일한다
            var mv = info.Chat.MoveMeeting(cap, baseHour - 1f, "저녁 교대가 겹쳐서");
            Run(w, SimTime.Minutes(5));
            bool tabletAway = x.Room != x.Bed?.Room && !info.Chat.HasTablet(x);
            for (int i = 0; i < 60 * 9 && w.Meetings.Session == null; i++) Run(w, SimTime.Minutes(1));
            var rec = w.Meetings.Session;
            var att = rec?.Attendees.ToList() ?? new List<int>();
            for (int i = 0; i < 120 && (mv != null && !mv.Late.Contains(x.Id) && !mv.Missed.Contains(x.Id)); i++) Run(w, SimTime.Minutes(1));
            var readers = att.Where(id => id != x.Id).ToList();
            bool onlyUnaware = mv != null && mv.Late.All(id => !info.Chat.All.Any(m => m.Id == mv.Msg) || id == x.Id || !readers.Contains(id));
            Check("회의 시간 변경은 메신저로만 — 단말을 두고 나온 사람은 모른다 (다른 사람은 읽고 시간 맞춰 모인다)",
                mv != null && tabletAway && rec != null && readers.Count >= 2 && !att.Contains(x.Id) && Math.Abs(SimTime.HourOfDay(rec.Tick) - mv.To) < 1.0f,
                $"원래 {baseHour:0}시 → {mv?.To:0}시 · 회의 {(rec != null ? SimTime.Clock(rec.Tick) : "-")} · 모인 사람 {att.Count}명 · {x.Name} 빠짐 {!att.Contains(x.Id)} · 단말 {(tabletAway ? "침실에" : "?")}");
            Check("못 본 사람만 늦는다 (컴퓨터가 읽음 표시를 보고 스피커로 부르면 늦게라도 온다 · 아니면 원래 시각에 빈 회의실)",
                mv != null && (mv.Late.Contains(x.Id) || mv.Missed.Contains(x.Id)) && onlyUnaware && mv.Late.Concat(mv.Missed).All(id => id == x.Id || !readers.Contains(id)),
                $"늦음 {string.Join("·", mv?.Late.Select(i => w.Crew[i].Name) ?? Array.Empty<string>())} · 놓침 {string.Join("·", mv?.Missed.Select(i => w.Crew[i].Name) ?? Array.Empty<string>())} · 컴퓨터가 부름 {string.Join("·", mv?.Paged.Select(i => w.Crew[i].Name) ?? Array.Empty<string>())} · {IfLog(w, x.Id, "회의")}");
        }

        // ── 3) 식당 자리: 늘 앉던 자리 · 다툰 사람 곁은 피한다 ──
        {
            var w = DayOne(seed, "Hanbit");
            for (int d = 0; d < 2; d++) Run(w, SimTime.TicksPerDay);
            var info = w.Info;
            var regulars = w.Crew.Where(c => !c.Dead && info.Regular(c) >= 0).ToList();
            Check("식당에서 늘 앉는 자리가 생긴다 (같은 자리에 거듭 앉는다)", regulars.Count >= 3 && info.Stats.Regulars * 3 >= info.Stats.SeatPicks - w.Crew.Count,
                $"자리 고름 {info.Stats.SeatPicks} · 늘 앉던 자리 {info.Stats.Regulars} · 늘 앉는 자리가 있는 사람 {regulars.Count}명");
            var s = regulars.OrderBy(c => c.Id).First();
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            SpQuiet(w, new[] { s }, SimTime.Hours(3));
            Teleport(w, s, IfFloor(w, mess));
            var plain = SpEat(w, s);
            var seat0 = plain?.Reservations.FirstOrDefault(f => f.Type == FurnitureType.Seat);
            plain?.Reservations.ForEach(f => { if (f.ReservedBy == s) f.ReservedBy = null; });
            // 다툰 사람이 그 자리 곁에 앉아 있다
            var q = w.Crew.Where(c => c != s && !c.Dead && !c.IsChild).OrderBy(c => c.Id).First();
            var beside = mess.Furniture.Where(f => f.Type == FurnitureType.Seat && f != seat0 && f.UseSpots.Count > 0 && seat0 != null).OrderBy(f => (f.Center - seat0!.Center).LengthSquared()).ThenBy(f => f.Id).FirstOrDefault();
            if (beside != null)
            {
                Teleport(w, q, beside.UseSpots[0]);
                var sitJob = new Job(null, "시험: 앉아 있음", new Toil[] { new WaitToil(SimTime.Hours(2), Pose.Sitting) }) { InterruptMargin = 9f };
                Force(w, q, sitJob, SimTime.Hours(2));
                sitJob.Reserve(beside, q);
                Run(w, 2);
            }
            info.OnQuarrel(s, q, "시험 말다툼");
            var after = SpEat(w, s);
            var seat1 = after?.Reservations.FirstOrDefault(f => f.Type == FurnitureType.Seat);
            float d0 = seat0 != null && beside != null ? (seat0.Center - beside.Center).Length() : 0f;
            float d1 = seat1 != null && beside != null ? (seat1.Center - beside.Center).Length() : 0f;
            Check("방금 다툰 사람이 앉은 곁은 피해 떨어진 자리를 고른다", seat0 != null && seat1 != null && d1 > d0 + 0.5f,
                $"{s.Name}: 늘 앉던 자리 #{info.Regular(s)} · 평소 고른 자리 #{seat0?.Id} (다툰 {q.Name}와 {d0:0.0}칸) → 다툰 뒤 #{seat1?.Id} ({d1:0.0}칸)");
        }

        // ── 4) 못 끝낸 일: 두뇌 중기 목표 · 시간 날 때 이어서 · 끊겨도 한 만큼 남는다 · 떠나면 가까운 사람이 이어받는다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var info = w.Info;
            var before = info.Todos.ToDictionary(t => t.Id, t => t.Progress);
            int per = w.Crew.Where(c => !c.Dead && !c.IsChild).Max(c => info.TodosOf(c).Count());
            var someone = w.Crew.Where(c => !c.Dead && !c.IsChild && info.TodosOf(c).Any()).OrderBy(c => c.Id).First();
            var goal = w.Brain2.Goals.Peek(someone).FirstOrDefault(g => g.Key.StartsWith("todo"));
            var grown = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Profiled).ToList();
            int with = grown.Count(c => info.TodosOf(c).Count(t => t.Kind is TodoKind.Model or TodoKind.Lamp or TodoKind.Gift) is >= 1 and <= 3);
            Check("사람마다 못 끝낸 개인 일 한두세 개 — 두뇌의 중기 목표로 보인다", with >= grown.Count * 0.9f && goal != null,
                $"{info.Todos.Count}개 · 가진 사람 {with}/{grown.Count} · {someone.Name}: {string.Join(" · ", info.TodosOf(someone).Select(t => $"{InfoSystem.TodoText(t, w)} {t.Progress * 100:0}%"))} · 목표 \"{goal?.Text}\"");
            Run(w, SimTime.TicksPerDay);
            var moved = info.Todos.Where(t => before.TryGetValue(t.Id, out var p0) && t.Progress > p0 + 0.01f).ToList();
            Check("시간이 나면 이어서 한다 (하루 사이 진척 · 끝낸 것)", moved.Count >= 2 && info.Stats.TodoWork >= 2,
                $"진척 {moved.Count}개 · 이어 함 {info.Stats.TodoWork} · 끝냄 {info.Stats.TodoDone} · 예: {string.Join(" · ", moved.Take(3).Select(t => $"{w.Crew[t.Owner].Name} {InfoSystem.TodoText(t, w)} {before[t.Id] * 100:0}→{t.Progress * 100:0}%"))}");
            // 끊겨도 한 만큼은 남는다
            var u = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct && info.TodosOf(c).Any(t => t.Kind is TodoKind.Model or TodoKind.Lamp && t.Progress < 0.7f)).OrderBy(c => c.Id).First();
            var tt = info.TodosOf(u).First(t => t.Kind is TodoKind.Model or TodoKind.Lamp && t.Progress < 0.7f);
            float p1 = tt.Progress;
            int s1 = tt.Sessions;
            SpQuiet(w, new[] { u }, SimTime.Hours(4));
            RunUntilHour(w, (u.Schedule.WorkStart + u.Schedule.WorkLength + 1f) % 24f);
            info.Intents.RemoveAll(i => i.Crew == u.Id);
            info.Dirty = 0;
            var act = Brain.Activities.OfType<InfoActivity>().First();
            var tj = act.PlanTodo(u, w, w.Paths.Flood(u.Cell, u.PathProfile), tt);
            p1 = tt.Progress;
            s1 = tt.Sessions;
            if (tj != null) Force(w, u, tj, SimTime.Hours(1));
            Run(w, SimTime.Minutes(40));
            if (u.Job == tj && tj != null) u.EndJob(w, ToilStatus.Interrupted);
            float p2 = tt.Progress;
            int s2 = tt.Sessions;
            for (int i = 0; i < 60 * 30 && tt.Sessions == s2 && !tt.Done; i++) Run(w, SimTime.Minutes(1));
            Check("경보에 끊겨도 한 만큼은 남고, 시간이 나면 그 자리부터 이어 한다", tj != null && p2 > p1 + 0.005f && s2 > s1 && (tt.Sessions > s2 || tt.Done) && tt.Progress > p2,
                $"{u.Name} {InfoSystem.TodoText(tt, w)}: {p1 * 100:0}% → 끊긴 뒤 {p2 * 100:0}% → 다시 {tt.Progress * 100:0}% (손댄 횟수 {tt.Sessions}) · {IfLog(w, u.Id, "이어서")}");
            // 떠난 사람의 일은 가까웠던 사람이 이어받는다
            var dead = w.Crew.Where(c => !c.Dead && !c.IsChild && info.TodosOf(c).Any(t => t.Kind is TodoKind.Model && t.Progress >= 0.2f)).OrderBy(c => c.Id).FirstOrDefault();
            if (dead != null)
            {
                var friend = w.Crew.Where(c => c != dead && !c.Dead && !c.IsChild).OrderBy(c => c.Id).First();
                friend.ChangeAffinity(dead, 0.7f);
                w.KillAway(dead);
                Run(w, SimTime.Hours(1));
                var inh = info.Todos.FirstOrDefault(t => t.From == dead.Id && !t.Done);
                Check("떠난 사람이 끝내지 못한 모형은 가까웠던 사람이 이어받는다", inh != null && w.Brain2.Goals.Has(w.Crew[inh.Owner], "todo" + inh.Id),
                    inh != null ? $"{w.Crew[inh.Owner].Name}: {InfoSystem.TodoText(inh, w)} {inh.Progress * 100:0}%" : "이어받은 사람 없음");
            }
        }

        // ── 5) 남이 치운 물건: 마지막에 둔 곳부터 찾는다 → 사물함 → 메신저로 묻고 치운 사람이 답한다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var info = w.Info;
            var p = IfAdults(w).Where(c => c.Bed != null).OrderBy(c => c.Id).First();
            float ph0 = IfFreeHour(p);
            RunUntilHour(w, ph0);
            var n = IfAdults(w).Where(c => c != p && c.Bed != null && !Life.Has(c, Habit.Loner)).OrderBy(c => IfFree(c, ph0) && IfFree(c, (ph0 + 2f) % 24f) ? 0 : 1).ThenBy(c => c.Id).First();
            var lounge = w.Ship.RoomsOf(RoomType.Lounge).FirstOrDefault() ?? w.Ship.RoomsOf(RoomType.Mess).First();
            var b = w.Belongings.Seed2(p, BelongingKind.Book, "시험");
            b.Name = "『고요한 궤도』";
            var spot = IfFloor(w, lounge);
            SpQuiet(w, new[] { p, n }, SimTime.Hours(8));
            Teleport(w, p, spot);
            Force(w, p, new Job(null, "시험: 책 읽기", new Toil[] { new WaitToil(SimTime.Minutes(7), Pose.Sitting) }) { InterruptMargin = 9f }, SimTime.Minutes(7));
            b.At = spot; b.Open = true; b.OpenSince = w.Tick;
            Run(w, SimTime.Minutes(6));
            var seenAt = info.Believed(b);
            // 주인은 식당으로 · 정리하는 사람이 와서 사물함에 넣는다 (주인은 모른다)
            Teleport(w, p, IfFloor(w, w.Ship.RoomsOf(RoomType.Mess).First()));
            IfIdle(w, p, SimTime.Minutes(30));
            Teleport(w, n, spot);
            Force(w, n, new Job(null, "시험: 둘러보기", new Toil[] { new WaitToil(SimTime.Minutes(7), Pose.Standing) }) { InterruptMargin = 9f }, SimTime.Minutes(7));
            Run(w, SimTime.Minutes(6));
            w.Belongings.PickUp(n, b);
            w.Belongings.Stow(n, b);
            Run(w, 2);
            bool wanted = info.Want(p, b);
            p.NextThinkTick = w.Tick + 1;
            Job? sj = null;
            for (int i = 0; i < 240 && sj == null; i++) { Run(w, SimTime.Minutes(1)); if (p.Job?.Activity is InfoActivity && p.Job.Label == "물건 찾기") sj = p.Job; if (Environment.GetEnvironmentVariable("INFO_DEBUG") == "1" && i % 10 == 0) Console.WriteLine($"     [{SimTime.Clock(w.Tick)}] {p.Name} ({p.Room?.Name}) 원함 {wanted}: {p.Job?.Label ?? "-"} · 할 일 {string.Join(",", info.Intents.Where(x => x.Crew == p.Id).Select(x => x.Do + ":" + x.Score.ToString("0.00")))} · 판단 {string.Join(" / ", p.LastEvaluations.Take(3).Select(e => e.Activity.Id + ":" + e.Score.ToString("0.00")))}"); }
            Check("주인은 마지막으로 본 곳만 안다 — 마지막에 둔 곳부터 찾으러 간다", wanted && Math.Abs(seenAt.X - spot.X) <= 1 && sj != null && info.Stats.LastSeenFirst >= 1 && IfLog(w, p.Id, "마지막에 둔") != "",
                $"믿는 곳 {seenAt} · 실제 {(b.At?.ToString() ?? "사물함")} · \"{IfLog(w, p.Id, "찾아본다")}\"");
            for (int i = 0; i < 120 && info.Stats.FoundElsewhere + info.Stats.FoundThere == 0; i++)
            {
                Run(w, SimTime.Minutes(1));
            }
            for (int i = 0; i < 120 && info.Stats.Answered == 0; i++) Run(w, SimTime.Minutes(1));
            var ask = info.Chat.All.LastOrDefault(m => m.Thing == b.Id && m.Kind == ChatKind.Ask);
            var ans = info.Chat.All.LastOrDefault(m => m.Thing == b.Id && m.Kind == ChatKind.Answer);
            Check("없으면 방 둘레 → 사물함에서 찾고, 누가 치웠나 메신저에 묻는다 → 치운 사람이 답한다", info.Stats.FoundElsewhere >= 1 && ask != null && ans != null && ans.Author == n.Id,
                $"찾음 {info.Stats.FoundElsewhere} · 물음 \"{ask?.Text}\" · 답 {(ans != null ? w.Crew[ans.Author].Name : "-")} \"{ans?.Text}\" · {IfLog(w, p.Id, "찾았다")}");
        }

        // ── 6) 사진: 중요한 날 찍어 개인 물건 · 메신저 · 벽에 건다 · 지나는 사람이 보고 떠올린다 ──
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 17f);
            var info = w.Info;
            var lounge = w.Ship.RoomsOf(RoomType.Lounge).FirstOrDefault() ?? w.Ship.RoomsOf(RoomType.Mess).First();
            var adults = IfAdults(w).OrderBy(c => IfFree(c, 17f) && IfFree(c, 19f) ? 0 : 1).ThenBy(c => c.Id).ToList();
            var ppl = adults.Take(4).ToList();
            var taker = ppl[0];
            SpQuiet(w, ppl, SimTime.Hours(10));
            foreach (var c in ppl) { Teleport(w, c, IfFloor(w, lounge)); IfIdle(w, c); }
            var ph = info.Snap(taker, lounge, PhotoScene.Group, "출항 열흘째", ppl);
            var pb = w.Belongings.Get(ph.Belonging);
            Check("중요한 날 찍은 사진은 찍은 사람의 개인 물건이 되고 메신저에 올라간다", pb != null && pb.Owner == taker.Id && pb.Kind == BelongingKind.Photo && info.Chat.All.Any(m => m.Photo == ph.Id),
                $"{pb?.Name} · {ph.People.Length}명 · \"{info.Chat.All.LastOrDefault(m => m.Photo == ph.Id)?.Text}\"");
            for (int i = 0; i < 360 && !ph.Hung; i++)
            {
                taker.NextThinkTick = Math.Min(taker.NextThinkTick, w.Tick + SimTime.Minutes(5));
                Run(w, SimTime.Minutes(1));
            }
            Check("찍은 사람이 사진을 벽에 건다 (정리하는 사람도 벽 사진은 그대로)", ph.Hung && pb!.At == ph.Wall && w.Ship.RoomAt(ph.Wall) != null,
                $"{(ph.Hung ? $"{w.Ship.RoomAt(ph.Wall)?.Name} 벽 {ph.Wall}" : "아직")} · {IfLog(w, taker.Id, "걸었다")}");
            // 사진 속 사람과 다툰 사람 · 떠난 사람
            var v = ppl[1];
            var foe = ppl.Skip(1).First(c => c != v);
            if (!ph.Hung) return 1;
            RunUntilHour(w, IfFreeHour(v));
            info.OnQuarrel(v, foe, "시험 말다툼");
            float aff0 = v.AffinityTo(foe);
            SpQuiet(w, new[] { v }, SimTime.Hours(4));
            Teleport(w, v, IfFloor(w, w.Ship.RoomAt(ph.Wall)!));
            for (int i = 0; i < 240 && !ph.SeenBy.Contains(v.Id); i++) { v.NextThinkTick = Math.Min(v.NextThinkTick, w.Tick + SimTime.Minutes(3)); Run(w, SimTime.Minutes(1)); }
            Check("지나는 사람이 벽 사진 앞에 멈춰 떠올린다 — 다툰 사람과 나란히 웃던 얼굴 (마음이 풀리고 화해할 마음)",
                ph.Views >= 1 && v.AffinityTo(foe) > aff0 && w.Brain2.Goals.Has(v, "reconcile") && v.Diary.Any(d => d.text.Contains("사진")),
                $"본 횟수 {ph.Views} · {v.Name}→{foe.Name} {aff0:0.00}→{v.AffinityTo(foe):0.00} · 일기 \"{v.Diary.LastOrDefault(d => d.text.Contains("사진")).text}\"");
        }

        // ── 7) 공동 장부: 혼자만 설거지 → 메신저 불만 → 찔린 사람 · 회의 안건(컴퓨터가 장부 숫자) → 당번 ──
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 12f);
            var info = w.Info;
            var adults = IfAdults(w);
            var a = adults.First(c => Life.Has(c, Habit.NeatFreak) || Life.Has(c, Habit.Grumbler)) ;
            var f = adults.Where(c => c != a && c.IsAwake).OrderBy(c => c.Id).First();
            for (int i = 0; i < 4; i++) info.Note(a, LedgerKind.Dishes, null, "설거지");
            f.Stats.Meals += 6;
            Run(w, SimTime.Minutes(6));
            int ate = info.Ate(f);
            RunUntilHour(w, 18.2f);
            for (int i = 0; i < 300 && info.Stats.Gripes == 0; i++) Run(w, SimTime.Minutes(1));
            var gripe = info.Chat.All.LastOrDefault(m => m.Kind == ChatKind.Gripe && m.Thing == -2);
            for (int i = 0; i < 180 && info.Stats.Guilty == 0; i++) Run(w, SimTime.Minutes(1));
            Check("혼자만 설거지한 사람이 장부를 보고 메신저에 불만 → 먹기만 한 사람은 읽고 찔린다 (관계에 남는다)",
                gripe != null && info.Stats.Guilty >= 1 && w.Relations.Of(a, f).Any(m => m.Reason == RelationReason.FreeRide),
                $"{a.Name}: \"{gripe?.Text}\" · {f.Name} 먹은 끼니 {ate} · 찔린 사람 {info.Stats.Guilty} · 설거지 {info.Stats.Dishes}");
            for (int i = 0; i < 60 * 30 && info.Stats.Agenda == 0; i++) Run(w, SimTime.Minutes(1));
            var item = w.Meetings.Minutes.SelectMany(m => m.Items).LastOrDefault(it => it.Topic == "ledger:dishes");
            Check("무임승차 불만이 회의 안건이 된다 — 주 컴퓨터가 장부 숫자를 댄다", item != null && (item.Computer ?? "").Contains("장부"),
                $"안건 \"{item?.Title}\" → {item?.Outcome} (찬 {item?.Yes} · 반 {item?.No}) · {item?.Computer} · 당번 {info.Roster.Count}명");
            if (info.Roster.Count > 0)
            {
                for (int i = 0; i < 60 * 30 && info.Stats.RosterDone == 0; i++) Run(w, SimTime.Minutes(1));
                Check("당번표가 붙으면 그날 당번이 설거지를 한다", info.Stats.RosterDone >= 1, $"당번 설거지 {info.Stats.RosterDone} · 오늘 당번 {info.OnDuty()?.Name}");
            }
        }

        // ── 8) 자연스럽게 흘러가는 사흘: 메신저 · 장부 · 사진 · 덜컹 ──
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.TicksPerDay * 3);
            var info = w.Info;
            Console.WriteLine($"   사흘: {info.Stats.Line()}");
            Console.WriteLine($"   메신저 {info.Chat.Posts}개 · 읽음 {info.Chat.Reads} · 못 봄 {info.Chat.Unseen}");
            Check("사흘 동안 메신저 글 · 장부(설거지) · 늘 앉는 자리가 저절로 생긴다", info.Chat.Posts >= 2 && info.Stats.Dishes >= 2 && info.Stats.Regulars >= 3 && info.Stats.TodoWork >= 2,
                $"글 {info.Chat.Posts} · 설거지 {info.Stats.Dishes} · 늘 앉던 자리 {info.Stats.Regulars} · 못 끝낸 일 손댐 {info.Stats.TodoWork}");
        }

        // ── 9) 결정론 · 성능 ──
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 · 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");
            var sw = Stopwatch.StartNew();
            var big = World.CreateDefault(seed, 0, "Cheonma");
            InfoSystem.UpdateTicks = 0;
            Run(big, SimTime.TicksPerDay);
            sw.Stop();
            double ms = InfoSystem.UpdateTicks * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"   30인 배 하루: {big.Info.Stats.Line()}");
            Check("성능: 30인 배(천마) 하루 — 이 시스템 틱이 전체의 3% 아래", ms < sw.Elapsed.TotalMilliseconds * 0.03,
                $"전체 {sw.Elapsed.TotalMilliseconds:0}ms · 이 시스템 {ms:0.0}ms ({ms / sw.Elapsed.TotalMilliseconds * 100:0.00}%) · 승무원 {big.Crew.Count}명");
        }

        return _fails == 0 ? 0 : 1;
    }
}
