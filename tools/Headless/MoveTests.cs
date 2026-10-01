using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v14.5 움직임: 비켜서기 · 막힌 통로 · 조용히 걷기 · 문 앞 확인 · 경보에 짐 내려놓기 · 큰 소리에 움찔
public static partial class Program
{
    private static readonly string[] MoveShips = { "Hanbit", "Mirinae", "Kestrel", "Eunha", "Cheonma" };

    /// <summary>시험용: 정한 곳까지 걸어간 뒤 서 있게 한다 (두 시간 동안 다른 생각을 하지 않는다).</summary>
    private static void Walk(World w, CrewMember c, Cell goal, bool urgent = false)
    {
        c.EndJob(w, ToilStatus.Interrupted);
        c.StartJob(new Job(null, "시험 이동", new Toil[] { new GotoToil(goal), new WaitToil(SimTime.Hours(2), Pose.Standing) }) { Urgent = urgent }, w, null);
        c.NextThinkTick = w.Tick + SimTime.Hours(2);
    }

    private static void Stay(World w, CrewMember c, Cell at, Pose pose)
    {
        c.EndJob(w, ToilStatus.Interrupted);
        Place(c, at);
        c.StartJob(new Job(null, pose == Pose.Sleeping ? "수면" : "시험 작업", new Toil[] { new WaitToil(SimTime.Hours(3), pose) }), w, null);
        c.NextThinkTick = w.Tick + SimTime.Hours(3);
    }

    private static void Place(CrewMember c, Cell at)
    {
        c.Position = at.Center; c.PreviousPosition = c.Position;
    }

    /// <summary>복도에서 둘이 엇갈리기 어려운 칸이 곧게 이어진 곳.</summary>
    private static List<Cell>? NarrowRun(World w, int len)
    {
        foreach (var room in w.Ship.LiveRooms.Where(r => r.Type == RoomType.Corridor))
            foreach (var start in room.Cells)
                foreach (var d in Cell.Dirs4)
                {
                    var run = new List<Cell>();
                    var cur = start;
                    while (w.Ship.IsWalkable(cur) && w.Ship.DoorAt(cur) == null && w.Ship.RoomAt(cur) == room && w.Movement.Tight(cur, d) && run.Count < len) { run.Add(cur); cur += d; }
                    if (run.Count >= len) return run;
                }
        return null;
    }

    private static (World w, List<Cell> run)? NarrowShip(int seed, int len)
    {
        foreach (var ship in MoveShips)
        {
            World w;
            try { w = DayOne(seed, ship); } catch { continue; }
            if (NarrowRun(w, len) is List<Cell> run) return (w, run);
        }
        return null;
    }

    private static int RunMoveTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"움직임 점검 (v14.5) · 시드 {seed}\n");
        try
        {
            // 1) 좁은 통로: 환자를 업은 사람에게 마주 오던 사람이 비켜선다 (업은 사람은 늦어지지 않는다)
            {
                var found = NarrowShip(seed, 7);
                if (found is not var (w, run)) { Check("좁은 통로를 찾았다", false, "폭 두 칸 이하로 곧게 이어진 통로가 있는 배가 없다"); return 1; }
                var adults = w.Crew.Where(c => !c.IsChild && c.CanAct).ToList();
                var carrier = adults[0]; var patient = adults[1]; var other = adults[2];
                foreach (var c in w.Crew.Where(c => c != carrier && c != patient && c != other && run.Contains(c.Cell))) Walk(w, c, w.Ship.Rooms.First(r => r.Type != RoomType.Corridor && !r.Detached).Cells.First(w.Ship.IsOpenFloor));
                Place(carrier, run[0]); Place(other, run[^1]);
                Walk(w, carrier, run[^1]); Walk(w, other, run[0]);
                patient.EndJob(w, ToilStatus.Interrupted);
                patient.Vitals.Health = 0.2f; patient.Down = true; patient.Pose = Pose.Down;
                patient.CarriedBy = carrier; carrier.CarryingPerson = patient; // 걷기 일을 준 뒤에 업힌다 (일을 바꾸면 업은 사람을 내려놓는다)
                long t0 = w.Tick; long arrived = -1; bool yielded = false; string? line = null;
                for (int t = 0; t < 150 && arrived < 0; t++)
                {
                    w.Step();
                    patient.Down = true;
                    if (other.Gait.YieldUntil >= w.Tick && other.Gait.YieldTo == carrier.Id) { yielded = true; line ??= other.Gait.Line(other, w); }
                    if (carrier.Cell == run[^1]) arrived = w.Tick;
                }
                float ideal = (run.Count - 1) / (Locomotion.Speed(carrier));
                float took = arrived < 0 ? 999f : arrived - t0;
                Check("비켜서기 — 좁은 통로에서 환자를 업은 사람에게 마주 오던 사람이 길을 내준다", yielded && w.Movement.Stats.ForPatient >= 1 && arrived > 0 && took <= ideal * 1.35f + 3,
                    $"{other.Name} → {carrier.Name}: {line ?? "-"} · 업은 사람 {took:0}틱(막힘 없이 {ideal:0}틱) · {w.Movement.Stats.Summary()}");
            }

            // 2) 통로에서 일하는 사람: 잠깐 기다렸다가 돌아가거나 비집고 지나간다
            {
                var found = NarrowShip(seed, 6);
                if (found is var (w, run))
                {
                    var adults = w.Crew.Where(c => !c.IsChild && c.CanAct).ToList();
                    var worker = adults[0]; var walker = adults[1];
                    Stay(w, worker, run[3], Pose.Working);
                    Place(walker, run[0]);
                    Walk(w, walker, run[^1]);
                    long t0 = w.Tick, arrived = -1;
                    for (int t = 0; t < 400 && arrived < 0; t++)
                    {
                        w.Step();
                        if (walker.Cell == run[^1]) arrived = w.Tick;
                    }
                    var st = w.Movement.Stats;
                    var mark = w.Ship.RoomAt(run[3])?.Marks.LastOrDefault(m => m.Text.Contains("통로에서 일해"));
                    Check("막힌 통로 — 일하는 사람 앞에서 기다렸다가, 돌아갈 길이 있으면 돌아가고 없으면 양해를 구하고 지나간다",
                        arrived > 0 && st.Reroutes + st.Squeezes >= 1,
                        $"{walker.Name} {(arrived > 0 ? $"{arrived - t0}틱 만에 지나감" : "못 지나감")} · 돌아감 {st.Reroutes} · 비집고 {st.Squeezes} · 방의 기억: {mark?.Text ?? "-"}");
                }
            }

            // 3) 자는 사람 곁은 조용히 (느리게) — 뛰어 지나가면 선잠이 깬다
            {
                int quietTicks = 0, plainTicks = 0, woke = 0; string? line = null;
                foreach (bool sleeper in new[] { true, false })
                {
                    var w = DayOne(seed, "Hanbit");
                    var bedroom = w.Ship.LiveRooms.Where(r => r.Furniture.Any(f => f.Type == FurnitureType.Bed)).OrderByDescending(r => r.Cells.Count(w.Ship.IsOpenFloor)).First();
                    var floor = bedroom.Cells.Where(w.Ship.IsOpenFloor).ToList();
                    var a = floor.OrderBy(c => c.X + c.Y).First(); var b = floor.OrderByDescending(c => c.X + c.Y).First();
                    var adults = w.Crew.Where(c => !c.IsChild && c.CanAct).ToList();
                    var walker = adults[0]; var sl = adults[1];
                    foreach (var c in w.Crew.Where(c => c != walker && c != sl && c.Room == bedroom)) Walk(w, c, w.Ship.Rooms.First(r => r != bedroom && r.Type != RoomType.Corridor && !r.Detached).Cells.First(w.Ship.IsOpenFloor));
                    var route = w.Paths.Find(a, b) ?? new List<Cell> { floor[floor.Count / 2] };
                    var mid = route[route.Count / 2];
                    var bedside = Cell.Dirs4.Select(d => mid + d).Where(x => floor.Contains(x) && !route.Contains(x)).DefaultIfEmpty(mid).First();
                    if (sleeper) { Stay(w, sl, bedside, Pose.Sleeping); sl.Needs.Rest = 0.95f; }
                    else Walk(w, sl, w.Ship.Rooms.First(r => r != bedroom && r.Type != RoomType.Corridor && !r.Detached).Cells.First(w.Ship.IsOpenFloor));
                    Place(walker, a); Walk(w, walker, b);
                    long t0 = w.Tick;
                    for (int t = 0; t < 300 && walker.Cell != b; t++) { w.Step(); line ??= walker.Gait.Line(walker, w); }
                    if (sleeper) quietTicks = (int)(w.Tick - t0); else plainTicks = (int)(w.Tick - t0);
                    if (!sleeper) continue;
                    // 급한 사람이 뛰어 지나가면
                    for (int pass = 0; pass < 10 && w.Movement.Stats.Woke == 0; pass++)
                    {
                        Walk(w, walker, pass % 2 == 0 ? a : b, urgent: true);
                        for (int t = 0; t < 300 && (t < 2 || walker.IsMoving); t++) w.Step(); // 첫 걸음을 떼기 전엔 아직 길이 없다
                        if (sl.Pose != Pose.Sleeping) { Stay(w, sl, sl.Cell, Pose.Sleeping); sl.Needs.Rest = 0.95f; }
                    }
                    woke = w.Movement.Stats.Woke;
                }
                Check("조용히 — 자는 사람이 있는 방은 발소리를 죽여 천천히 지나고, 뛰어 지나가면 선잠을 깨운다", quietTicks > plainTicks * 1.15f && woke >= 1,
                    $"자는 사람 곁 {quietTicks}틱 ↔ 없을 때 {plainTicks}틱 · {line ?? "-"} · 뛰어 지나가 깨움 {woke}");
            }

            // 4) 위험한 문 앞: 계기를 보고 들어간다
            {
                var w = DayOne(seed, "Hanbit");
                var door = w.Ship.Doors.First(d => !d.IsExternal && !d.Removed && d.RoomA is Room ra && d.RoomB is Room rb && !ra.Detached && !rb.Detached && ra.Type != RoomType.Corridor);
                var hot = door.RoomA!;
                var from = door.RoomB!;
                var inside = hot.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => Math.Abs(c.X - door.Cell.X) + Math.Abs(c.Y - door.Cell.Y)).Skip(1).First();
                var outside = from.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => Math.Abs(c.X - door.Cell.X) + Math.Abs(c.Y - door.Cell.Y)).Skip(1).First();
                var walker = w.Crew.First(c => !c.IsChild && c.CanAct);
                Place(walker, outside); Walk(w, walker, inside);
                string? reading = null; int paused = 0;
                for (int t = 0; t < 200 && walker.Cell != inside; t++)
                {
                    hot.Air.Temperature = 52f;
                    w.Step();
                    if (walker.Gait.DoorCheckUntil >= w.Tick) { paused++; reading ??= walker.Gait.Line(walker, w); }
                }
                Check("문 앞 확인 — 너머가 위험한 문 앞에서 멈춰 계기를 본다 (들어갈지는 그다음 판단)", w.Movement.Stats.DoorChecks >= 1 && paused >= 1,
                    $"{walker.Name}: {reading ?? "-"} · 멈춘 틱 {paused} · {(walker.Cell == inside ? "들어갔다" : walker.Room == hot ? "들어갔다가 나왔다" : "들어가지 않고 돌아섰다")}");
            }

            // 5) 경보: 보관함이 멀면 들고 있던 짐을 그 자리에 내려놓고 간다 → 조용해지면 챙긴다
            {
                var w = DayOne(seed, "Hanbit");
                var c = w.Crew.First(x => !x.IsChild && x.CanAct);
                var flood0 = w.Paths.Flood(c.Cell, c.PathProfile);
                var far = w.Ship.LiveRooms.Where(r => !r.Detached && r.Cells.Any(w.Ship.IsOpenFloor))
                    .SelectMany(r => r.Cells.Where(w.Ship.IsOpenFloor))
                    .OrderByDescending(cl => w.Ship.Containers.SelectMany(f => f.UseSpots).Select(s => Math.Abs(s.X - cl.X) + Math.Abs(s.Y - cl.Y)).DefaultIfEmpty(0).Min()).First();
                Place(c, far);
                c.EndJob(w, ToilStatus.Interrupted);
                c.Carrying = new ItemStack(ItemKind.Produce, 4); // 수확한 채소를 나르던 중
                // 먼 방에 불
                var fireRoom = w.Ship.LiveRooms.Where(r => r != c.Room && r.Type != RoomType.Corridor && !r.Detached).OrderByDescending(r => (r.Cells[0].Center - c.Position).Length()).First();
                w.Fire.Ignite(fireRoom.Cells.First(w.Ship.IsOpenFloor), 0.6f);
                Run(w, SimTime.Minutes(2));
                bool hurry = w.Movement.Hurry(w);
                c.EndJob(w, ToilStatus.Interrupted);
                var toils = Plans.DropOff(c, w, w.Paths.Flood(c.Cell, c.PathProfile));
                c.StartJob(new Job(null, "짐 내려놓기", toils), w, null);
                Run(w, 3);
                int stashes = w.Movement.Stashes.Count;
                bool handsFree = c.Carrying == null;
                // 실링폼(수리재)은 내려놓지 않는다 — 그 일에 쓸 수 있다
                c.EndJob(w, ToilStatus.Interrupted);
                c.Carrying = new ItemStack(ItemKind.Sealant, 1);
                c.StartJob(new Job(null, "짐 내려놓기", Plans.DropOff(c, w, w.Paths.Flood(c.Cell, c.PathProfile))), w, null);
                Run(w, 3);
                bool keptTool = w.Movement.Stashes.Count == stashes;
                c.EndJob(w, ToilStatus.Interrupted); c.Carrying = null;
                // 불이 꺼지고 조용해지면 챙긴다
                w.Fire.ClearRoom(fireRoom);
                for (int h = 0; h < 16 && w.Movement.Stashes.Count > 0; h++) Run(w, SimTime.Hours(1));
                var st = w.Movement.Stats;
                Check("경보 — 보관함이 멀면 나르던 먹을 것을 그 자리에 내려놓고 가고, 조용해지면 챙긴다 (공구·부품은 들고 간다)", hurry && stashes == 1 && handsFree && keptTool && st.PickedUp >= 1 && w.Movement.Stashes.Count == 0,
                    $"경보 {(hurry ? "예" : "아니오")} · 채소 내려놓음 {st.SetDowns} · 손 {(handsFree ? "비움" : "그대로")} · 실링폼은 {(keptTool ? "들고 감" : "내려놓음")} · 챙김 {st.PickedUp} · 남은 짐 {w.Movement.Stashes.Count}");
            }

            // 6) 큰 소리: 가까운 사람은 움찔하며 그쪽을 보고, 자던 사람은 깨고, 먼 사람은 모른다
            {
                var w = DayOne(seed, "Hanbit");
                var room = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && !r.Detached).OrderByDescending(r => r.Cells.Count(w.Ship.IsOpenFloor)).First();
                var floor = room.Cells.Where(w.Ship.IsOpenFloor).ToList();
                var adults = w.Crew.Where(c => !c.IsChild && c.CanAct).ToList();
                var awake = adults[0]; var sleeper = adults[1]; var far = adults[2];
                var at = floor[floor.Count / 2].Center;
                Cell Near(float d) => floor.OrderBy(x => MathF.Abs((x.Center - at).Length() - d)).First();
                Stay(w, awake, Near(4f), Pose.Standing);
                Stay(w, sleeper, Near(6f) == Near(4f) ? Near(7f) : Near(6f), Pose.Sleeping);
                var away = w.Ship.LiveRooms.Where(r => r != room && !r.Detached).SelectMany(r => r.Cells.Where(w.Ship.IsOpenFloor)).OrderByDescending(cl => (cl.Center - at).Length()).First();
                Stay(w, far, away, Pose.Standing);
                Run(w, 2); // 자리를 잡는다 (어느 방에 있는지)
                awake.Facing = System.Numerics.Vector2.Normalize(awake.Position - at); // 등지고 서 있다
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2")
                    Console.WriteLine($"      {awake.Name} 방 {awake.Room?.Name} / {room.Name} · 거리 {(awake.Position - at).Length():0.0} · 쓰러짐 {awake.Down} 자세 {awake.Pose} 밖 {awake.Outside}");
                w.Movement.Bang(room, at, 0.8f, "시험: 압력 용기가 터졌다");
                bool startled = awake.Gait.StartleUntil >= w.Tick;
                float face = System.Numerics.Vector2.Dot(awake.Facing, System.Numerics.Vector2.Normalize(at - awake.Position));
                string? line = awake.Gait.Line(awake, w);
                Run(w, 3);
                bool woke = sleeper.Pose != Pose.Sleeping || w.Movement.Stats.Woke >= 1;
                bool farCalm = far.Gait.StartleTick < 0 || (far.Position - at).Length() < 18f;
                // 실제 사고에서도: 운석 충돌 · 폭발
                var w2 = DayOne(seed, "Hanbit");
                Player.Hazard(w2, HazardKind.MeteorShower, default, -1);
                Run(w2, SimTime.Hours(1));
                Check("움찔 — 큰 소리가 나면 가까운 사람은 그쪽을 보고 멈칫하고, 자던 사람은 깨고, 먼 사람은 모른다", startled && face > 0.7f && woke && farCalm,
                    $"{awake.Name}: {line ?? "-"} (바라봄 {face:0.00}) · {sleeper.Name} {(woke ? "깼다" : "잔다")} · {far.Name} {(farCalm ? "모른다" : "놀랐다")} · 운석우 한 시간: 움찔 {w2.Movement.Stats.Startles}");
            }
            // 7) 결정론: 사람이 많은 배에서 비켜서기·돌아가기가 섞여도 같은 시드 같은 지문
            {
                (uint, string) H()
                {
                    var w = World.CreateDefault(seed, 0, "Eunha");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                    return (SaveGame.StateHash(w), w.Movement.Stats.Summary());
                }
                var (x, sx) = H(); var (y, _) = H();
                Check("결정론 — 스무 명이 오가는 배도 같은 시드 같은 지문 (하루 반 동안의 걸음)", x == y, $"{x:x8} / {y:x8} · {sx}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 움직임 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
