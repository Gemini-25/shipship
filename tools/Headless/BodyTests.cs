using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.3 배 본체: 한 칸 3층 · 재질 · 칸 상태 · 닳는 바닥 · 벽 층 · 문 — 검증 장면을 헤드리스에서 실제로 일으킨다
public static partial class Program
{
    /// <summary>그 사람에게 이 일만 시킨다 (판단이 끼어들지 않게).</summary>
    private static void Force(World w, CrewMember c, Job job, long hold = 0)
    {
        if (c.Job != null) c.EndJob(w, ToilStatus.Interrupted);
        c.StartJob(job, w, null);
        c.NextThinkTick = w.Tick + (hold > 0 ? hold : SimTime.Hours(3));
    }

    private static void Teleport(World w, CrewMember c, Cell cell)
    {
        c.Position = cell.Center;
        c.Path = null;
    }

    private static void RunUntilHour(World w, float hour)
    {
        for (int k = 0; k < SimTime.TicksPerDay && Math.Abs(SimTime.HourOfDay(w.Tick) - hour) > 0.01f; k++) w.Step();
    }

    private static int RunBodyTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"배 본체 점검 (v16.3) · 시드 {seed}\n");

        // 0) 재질 표 · 한 칸 3층 기본값
        {
            var floors = new[] { Material.Grate, Material.Rubber, Material.Tile, Material.Carpet, Material.MetalPlate };
            var tile = Materials.Of(Material.Tile);
            bool table = floors.Select(m => Materials.Name(m)).Distinct().Count() == 5
                && Materials.SlipNow(Material.Tile, 1f, 0f, 0f, 0f) > 0.6f && Materials.SlipNow(Material.Grate, 1f, 0f, 0f, 0f) < 0.2f
                && Materials.Of(Material.Carpet).Loud < Materials.Of(Material.Grate).Loud && Materials.Of(Material.Grate).SmallLoss > 0.5f
                && Materials.Of(Material.Partition).SoundBlock < Materials.Of(Material.Panel).SoundBlock && Materials.Of(Material.Rubber).Insulate > 0.8f;
            Check("재질 표 — 바닥재 5종 · 젖은 타일은 미끄럽고 격자는 물이 빠진다 · 카펫은 조용하다 · 칸막이는 소리가 샌다", table,
                string.Join(" · ", floors.Select(m => $"{Materials.Name(m)} 마름 {Materials.Of(m).Slip:0.00}/젖음 {Materials.Of(m).WetSlip:0.00}/소리 {Materials.Of(m).Loud:0.0}")));
            var w = World.CreateDefault(seed, 0, "Hanbit");
            var b = w.Body;
            var grid = w.Ship.Grid;
            int cells = 0, none = 0, under = 0, ceil = 0, spots = 0;
            var used = new HashSet<Material>();
            foreach (var r in w.Ship.LiveRooms)
                foreach (var c in r.Cells)
                {
                    int i = grid.Index(c);
                    cells++;
                    if (b.Floor[i] == Material.None) none++;
                    used.Add(b.Floor[i]);
                    if (b.Under[i] != UnderFlags.None) under++;
                    if ((b.Ceiling[i] & CeilingFlags.Light) != 0) ceil++;
                    if ((b.Under[i] & UnderFlags.HatchSpot) != 0) spots++;
                }
            Check("한 칸 3층 — 모든 칸에 바닥재 · 바닥 아래 · 천장이 방 종류로 정해진다", none == 0 && used.Count >= 4 && under == cells && ceil > cells / 6 && spots > 10,
                $"칸 {cells} · 바닥재 {string.Join("/", used.Select(Materials.Name))} · 조명 {ceil} · 점검 뚜껑 자리 {spots} · 벽 {b.WallList.Count}(칸막이 {b.WallList.Count(x => x.Thin)} · 창 {b.WallList.Count(x => x.Window)}) · 장착물 {b.Mounts.Count} · 통제 문 {b.Doors.Count(x => x.Lock != LockKind.None)}");
        }

        // 1) 잠긴 약품고 앞에서 기다리던 사람이 권한 있는 의무관을 부른다
        {
            var w = DayOne(seed, "Hanbit");
            RunUntilHour(w, 10f);
            var b = w.Body;
            var pharm = w.Ship.LiveRooms.Where(r => r.Kind == RoomType.Storage && r.Doors.Count >= 1 && r.Cells.Count >= 4).OrderBy(r => r.Doors.Count).ThenBy(r => r.Id).First();
            b.SetZone(pharm, AccessZone.Medicine, LockKind.Card);
            var medics = w.Crew.Where(c => c.Role == CrewRole.Medic && !c.Dead).ToList();
            foreach (var m in medics) if (m.Pose == Pose.Sleeping) { m.Pose = Pose.Standing; m.NextThinkTick = w.Tick; }
            var p = w.Crew.Where(c => c.CanAct && !c.IsChild && c.Role != CrewRole.Medic && c.Id != w.Command.CaptainId && c.Room != pharm && !c.Outside)
                .OrderBy(c => (c.Position - pharm.Center).LengthSquared()).First();
            var inside = pharm.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => (c.Center - pharm.Center).LengthSquared()).First();
            Force(w, p, new Job(null, "약 가지러", new List<Toil> { new GotoToil(inside), new WaitToil(10, Pose.Standing) }));
            bool reached = false, waited = false;
            CrewMember? helper = null;
            for (int t = 0; t < SimTime.Minutes(70) && !reached; t++) // 통합: 새 배는 의무실에서 약품고까지 서른 칸 넘게 걸어온다 (40분이면 오는 도중에 끝났다)
            {
                w.Step();
                if (b.Doors.Any(d => d.Caller == p.Id)) waited = true;
                foreach (var d in b.Doors) if (d.Helper >= 0 && d.Caller == p.Id) helper ??= w.Crew.First(x => x.Id == d.Helper);
                if (p.Room == pharm) reached = true;
                if (p.Job == null || p.Job.Label != "약 가지러") break;
            }
            var call = w.Log.Entries.LastOrDefault(e => e.Text.Contains("불렀다") && e.CrewId == p.Id).Text;
            var let = w.Log.Entries.LastOrDefault(e => e.Text.Contains("문을 열어 줬다")).Text;
            Check("약품고 — 잠긴 문 앞에서 기다리다 권한 있는 의무관을 부르고, 와서 열어 줘 들어간다",
                waited && helper != null && (helper.Role == CrewRole.Medic || helper.Id == w.Command.CaptainId) && b.Stats.LetIn > 0 && reached,
                $"{p.Name}({CrewRoles.Name(p.Role)}) → {pharm.Name} · 부른 사람 {helper?.Name ?? "없음"}({(helper != null ? CrewRoles.Name(helper.Role) : "-")}) · 기다림 {b.Stats.Waits} · 부름 {b.Stats.Calls} · 열어 줌 {b.Stats.LetIn} · 들어감 {reached} · \"{call}\" · \"{let}\"");
            // 화재 때 자동 해제 (방침)
            var door = pharm.Doors.First(d => !d.IsExternal);
            var db = b.DoorOf(door)!;
            bool lockedBefore = b.Engaged(db, door);
            var fireCell = w.Ship.LiveRooms.First(r => r.Kind == RoomType.Galley).Cells.First(c => w.Ship.IsOpenFloor(c));
            w.Fire.Ignite(fireCell, 0.4f);
            Run(w, World.SystemInterval + 1);
            bool releasedFire = !b.Engaged(db, door);
            w.Policies.Set("doorfire", 1, "시험");
            Run(w, World.SystemInterval + 1);
            bool keptLocked = b.Engaged(db, door) || w.Fire.Count == 0;
            Check("화재 — 방침(자동 해제)이면 출입 통제가 풀리고, '그대로'면 잠긴 채", lockedBefore && releasedFire && keptLocked, $"전 {lockedBefore} · 불 나자 풀림 {releasedFire} · 방침 바꾸면 잠김 {keptLocked} · 해제 {b.Stats.FireReleases}");
        }

        // 2) 정전에 문을 손으로 돌려 연다 (시간이 더 걸린다)
        {
            long Cross(bool blackout, out int cranks)
            {
                var w = DayOne(seed, "Hanbit");
                RunUntilHour(w, 11f);
                var door = w.Ship.Doors.Where(d => !d.IsExternal && !d.Bulkhead && d.RoomA != null && d.RoomB != null && w.Body.DoorOf(d)!.Zone == AccessZone.Open
                    && d.RoomA.Kind != RoomType.Reactor && d.RoomB.Kind != RoomType.Reactor).OrderBy(d => d.Id).Skip(2).First();
                var dir = door.ConnectsVertically ? new Cell(0, 1) : new Cell(1, 0);
                Cell from = door.Cell - dir - dir, to = door.Cell + dir + dir;
                if (!w.Ship.IsWalkable(from) || !w.Ship.IsWalkable(to)) { from = door.Cell - dir; to = door.Cell + dir; }
                if (blackout) { door.RoomA!.BreakerOff = true; door.RoomB!.BreakerOff = true; Run(w, World.SystemInterval * 2); }
                var p = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside).OrderBy(c => c.Id).First();
                Teleport(w, p, from);
                w.Step();
                if (blackout) door.Openness = 0f; // 통합8 앞서 누가 손으로 돌려 열어 둔 문이면 그냥 지나간다 — 닫힌 문 앞에서 잰다
                Force(w, p, new Job(null, "지나가기", new List<Toil> { new GotoToil(to), new WaitToil(30, Pose.Standing) }));
                long t0 = w.Tick;
                var past = door.Cell + dir; // 문을 넘어선 첫 칸 (정전이면 넘자마자 배전반을 보러 가는 사람도 있다 — 지나간 때를 잰다)
                bool crossed = false;
                for (int t = 0; t < SimTime.Minutes(10); t++)
                {
                    w.Step();
                    if (p.Cell == to || p.Cell == past) { crossed = true; break; }
                }
                cranks = w.Body.Stats.Cranks;
                bool powered = door.Powered;
                Console.WriteLine($"    {(blackout ? "정전" : "평시")}: {door.RoomA!.Name}·{door.RoomB!.Name} 문 전기 {powered} · {w.Tick - t0}틱");
                return crossed ? w.Tick - t0 : 99999;
            }
            long normal = Cross(false, out _);
            long dark = Cross(true, out int cranks);
            Check("정전 — 문을 손으로 돌려 연다 (지나는 데 시간이 더 걸린다)", dark < 99999 && dark >= normal + 15 && cranks > 0, $"평시 {normal}틱 · 정전 {dark}틱 · 손으로 돌림 {cranks}");
        }

        // 3) 옆 선실 대화가 칸막이로 새어 엿듣는다 (기억에 남는다)
        {
            var w = DayOne(seed, "Mirinae");
            RunUntilHour(w, 14f);
            var q = w.Ship.LiveRooms.First(r => r.Kind == RoomType.Quarters);
            var plan = Remodel.FindSplit(w, q);
            Room? q2 = plan != null ? Remodel.Apply(w, plan) : null;
            Run(w, SimTime.Minutes(2));
            var b = w.Body;
            bool thin = q2 != null && b.ThinNeighbors(q).Contains(q2.Id);
            string detail = $"칸막이 {b.WallList.Count(x => x.Thin)}칸 · {q.Name}↔{q2?.Name}";
            if (q2 != null && plan != null)
            {
                var wallCell = plan.Wall[plan.Wall.Count / 2];
                Cell? side1 = null, side2 = null;
                foreach (var d in Cell.Dirs4)
                {
                    var n = wallCell + d;
                    if (w.Ship.RoomAt(n) == q && w.Ship.IsWalkable(n)) side1 ??= n;
                    if (w.Ship.RoomAt(n) == q2 && w.Ship.IsWalkable(n)) side2 ??= n;
                }
                side1 ??= q.Cells.Where(w.Ship.IsWalkable).OrderBy(c => (c.Center - wallCell.Center).LengthSquared()).First();
                side2 ??= q2.Cells.Where(w.Ship.IsWalkable).OrderBy(c => (c.Center - wallCell.Center).LengthSquared()).First();
                var talkers = w.Crew.Where(c => c.CanAct && !c.IsChild).OrderBy(c => c.Id).Take(3).ToList();
                CrewMember A = talkers[0], B = talkers[1], L = talkers[2];
                var x = w.Crew.FirstOrDefault(c => c != A && c != B && c != L);
                if (x != null) w.Relations.Remember(A, x, RelationReason.TookMyThing, "내 공구를 말없이 가져갔다");
                var nearA = q.Cells.Where(c => w.Ship.IsWalkable(c) && c != side1).OrderBy(c => (c.Center - side1.Value.Center).LengthSquared()).First();
                Teleport(w, A, side1.Value); Teleport(w, B, nearA); Teleport(w, L, side2.Value);
                w.Step();
                Job Talk(CrewMember partner) => new Job(null, "대화", new List<Toil> { new WaitToil(SimTime.Hours(2), Pose.Standing, partner.Position) { EveryTick = (cm, _) => { cm.TalkingTo = partner; } } });
                Force(w, A, Talk(B)); Force(w, B, Talk(A));
                Force(w, L, new Job(null, "책 읽기", new List<Toil> { new WaitToil(SimTime.Hours(2), Pose.Standing) }));
                for (int t = 0; t < SimTime.Hours(1.5f) && !b.Heard.Any(h => h.Listener == L.Id); t++) w.Step();
                var mem = b.Heard.LastOrDefault(h => h.Listener == L.Id);
                detail += $" · {A.Name}·{B.Name} 대화 → 엿들은 {L.Name}: {(mem != null ? $"{SimTime.Clock(mem.Tick)} \"{mem.What}\"" : "없음")} · 엿들음 {b.Stats.Overheard}";
                Check("엿듣기 — 옆 선실 대화가 얇은 칸막이로 새어 엿듣고, 기억에 남는다", thin && mem != null, detail);

                // 선실 노크: 자는 사람은 대답이 없다 · 깨어 있으면 "들어와"
                b.SetZone(q2, AccessZone.Cabin, LockKind.Key);
                var owner = w.Crew.FirstOrDefault(c => q2.Furniture.Any(f => f.Type == FurnitureType.Bed && f.Owner == c) && c != A && c != B && c != L && c.CanAct);
                if (owner != null)
                {
                    var bedSpot = q2.Cells.Where(w.Ship.IsWalkable).OrderBy(c => (c.Center - q2.Center).LengthSquared()).First();
                    Teleport(w, owner, bedSpot);
                    Force(w, owner, new Job(null, "낮잠", new List<Toil> { new WaitToil(SimTime.Hours(2), Pose.Sleeping) }));
                    foreach (var c in new[] { A, B, L }) if (c.Job != null) c.EndJob(w, ToilStatus.Interrupted);
                    var door = plan.Door;
                    var visitor = A;
                    var outside = q.Cells.Where(w.Ship.IsWalkable).OrderBy(c => (c.Center - door.Center).LengthSquared()).First();
                    Teleport(w, visitor, outside);
                    var target = q2.Cells.Where(c => w.Ship.IsWalkable(c) && c != bedSpot).OrderBy(c => (c.Center - door.Center).LengthSquared()).Skip(1).First();
                    w.Step();
                    Force(w, visitor, new Job(null, "들르기", new List<Toil> { new GotoToil(target) }));
                    int k0 = b.Stats.Knocks, n0 = b.Stats.NoAnswer;
                    for (int t = 0; t < SimTime.Minutes(5) && visitor.Job?.Label == "들르기"; t++) w.Step();
                    bool noAnswer = b.Stats.Knocks > k0 && b.Stats.NoAnswer > n0 && visitor.Room != q2;
                    string phase1 = $"1차: 노크 {b.Stats.Knocks - k0} · 대답 없음 {b.Stats.NoAnswer - n0} · 있는 곳 {visitor.Room?.Name ?? "?"}{visitor.Cell} 문 {door} 바깥 {outside} 목표 {target} · 주인 {owner.Pose}";
                    // 깨어 있으면
                    Force(w, owner, new Job(null, "책 읽기", new List<Toil> { new WaitToil(SimTime.Hours(2), Pose.Sitting) }));
                    b.DoorOf(w.Ship.DoorAt(door)!)!.Knocker = -1;
                    Run(w, SimTime.Minutes(70)); // 돌아선 사람은 한 시간 동안 다시 두드리지 않는다
                    // 그새 주인이 나갔을 수 있다 — 다시 제 선실에서 책을 읽게 (깨어 있음)
                    Teleport(w, owner, bedSpot);
                    w.Step();
                    Force(w, owner, new Job(null, "책 읽기", new List<Toil> { new WaitToil(SimTime.Hours(2), Pose.Sitting) }));
                    b.DoorOf(w.Ship.DoorAt(door)!)!.Knocker = -1;
                    Teleport(w, visitor, outside);
                    w.Step();
                    Force(w, visitor, new Job(null, "들르기", new List<Toil> { new GotoToil(target), new WaitToil(20, Pose.Standing) }));
                    int a0 = b.Stats.Answered, d0 = b.Stats.DropIns;
                    bool entered = false;
                    for (int t = 0; t < SimTime.Minutes(5) && !entered; t++) { w.Step(); entered = visitor.Room == q2; }
                    Check("선실 노크 — 자는 사람은 대답이 없어 돌아서고, 깨어 있으면 \"들어와\"", noAnswer && (b.Stats.Answered > a0 || b.Stats.DropIns > d0) && entered,
                        $"{phase1} · {visitor.Name} → {owner.Name} 선실 · 노크 {b.Stats.Knocks} · 대답 없음 {b.Stats.NoAnswer} · 대답 {b.Stats.Answered} · 열어 둔 문에 들름 {b.Stats.DropIns} · 들어감 {entered}");
                }
            }
            else Check("엿듣기 — 칸막이를 세울 수 있어야 한다", false, detail);
        }

        // 4) 점검 뚜껑: 배관 수리가 뚜껑을 열고 · 열린 칸은 돌아서 간다
        {
            var w = DayOne(seed, "Hanbit");
            var b = w.Body;
            var cor = w.Ship.LiveRooms.Where(r => r.Kind == RoomType.Corridor).OrderByDescending(r => r.Cells.Count).First();
            // 수리하는 사람이 실제로 손을 대는 동안 뚜껑이 열린다
            var fixer = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside).OrderBy(c => c.Id).Skip(1).First();
            var at = cor.Cells.Where(c => w.Ship.IsOpenFloor(c) && (b.Under[w.Ship.Grid.Index(c)] & UnderFlags.HatchSpot) != 0).OrderBy(c => c.X).Skip(3).First();
            Teleport(w, fixer, at);
            w.Step();
            // 통합: 땜질 일감은 진짜 관을 가리킨다 (관 없는 일감은 로봇이 곁에서 거들며 이름을 부를 때 멈췄다)
            var pseg = w.Piping.Segments.FirstOrDefault(sg => sg.Path.Count > 0);
            var order = new WorkOrder { Id = 999999, Kind = WorkKind.PatchPipe, Target = pseg != null ? WorkTarget.OfPipe(pseg, cor) : WorkTarget.OfRoom(cor), Skill = Skill.Mechanics, Posted = w.Tick };
            Force(w, fixer, new Job(null, "배관 땜질", new List<Toil> { new WorkToil(0.4f, Skill.Mechanics, at.Center) }) { Order = order });
            int open0 = b.Stats.HatchOpens;
            bool sawOpen = false;
            for (int t = 0; t < SimTime.Hours(1) && fixer.Job?.Label == "배관 땜질"; t++) { w.Step(); sawOpen |= b.Hatches.Any(h => h.By == fixer.Id && !h.Forgotten); }
            Run(w, World.SystemInterval * 2);
            bool closedOrForgot = !b.Hatches.Any(h => h.By == fixer.Id && !h.Forgotten);
            Check("뚜껑 — 배관 수리하는 동안 점검 뚜껑을 열고, 손을 떼면 닫는다 (잊기도)", sawOpen && b.Stats.HatchOpens > open0 && closedOrForgot,
                $"연 횟수 {b.Stats.HatchOpens - open0} · 남은 뚜껑 {b.Hatches.Count}(잊음 {b.Stats.HatchForgot})");
            foreach (var h in b.Hatches.ToList()) b.CloseHatch(h);

            // 열린 뚜껑 칸을 돌아서 간다
            var cells = cor.Cells.Where(w.Ship.IsWalkable).ToList();
            Cell start = default, goal = default, mid = default;
            bool found = false;
            foreach (var s in cells.OrderBy(c => c.Y).ThenBy(c => c.X))
            {
                var g = new Cell(s.X + 6, s.Y);
                var m = new Cell(s.X + 3, s.Y);
                if (!cells.Contains(g) || !cells.Contains(m)) continue;
                var path = w.Paths.Find(s, g);
                if (path == null || !path.Contains(m)) continue;
                if (!Cell.Dirs4.Any(d => d.Y != 0 && cells.Contains(m + d))) continue;
                start = s; goal = g; mid = m; found = true;
                break;
            }
            if (found)
            {
                b.OpenHatchAt(mid, -1, -1, "시험");
                Run(w, World.SystemInterval + 1);
                var path = w.Paths.Find(start, goal);
                var walker = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside && c != fixer).OrderBy(c => c.Id).First();
                Teleport(w, walker, start);
                w.Step();
                Force(w, walker, new Job(null, "지나가기", new List<Toil> { new GotoToil(goal) }));
                bool stepped = false;
                for (int t = 0; t < SimTime.Minutes(5) && walker.Job?.Label == "지나가기"; t++) { w.Step(); stepped |= walker.Cell == mid; }
                Check("우회 — 점검 뚜껑이 열린 칸을 돌아서 간다", path != null && !path.Contains(mid) && !stepped && walker.Cell == goal,
                    $"{start}→{goal} · 뚜껑 {mid} · 길 {path?.Count}칸 · 밟음 {stepped} · 도착 {walker.Cell == goal}");
            }
            else Check("우회 — 통로에 곧은 길이 있어야 한다", false);
        }

        // 5) 젖은 타일에서 뛰면 미끄러질 수 있다 · 바닥 물(침수)이 칸 상태로
        {
            float runRisk = 0f, walkRisk = 0f;
            int Slips(bool urgent, bool wet, out string where, out string seen)
            {
                var w = DayOne(seed, "Hanbit");
                var b = w.Body;
                var room = w.Ship.LiveRooms.Where(r => b.FloorAt(r.Cells[0]) == Material.Tile && r.Cells.Count(w.Ship.IsWalkable) >= 8).OrderByDescending(r => r.Cells.Count).First();
                where = room.Name;
                w.Automation.Speak.BreakSpeaker(room, "시험", 6f); // 방송을 못 듣는다 — 아무도 조심하라는 말을 못 들은 바닥
                if (wet) foreach (var c in room.Cells) b.SetMark(c, CellMark.Wet, 1f, "시험");
                var walk = room.Cells.Where(w.Ship.IsWalkable).ToList();
                Cell a = walk.OrderBy(c => c.X).ThenBy(c => c.Y).First(), z = walk.OrderByDescending(c => c.X).ThenByDescending(c => c.Y).First();
                var p = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside).OrderBy(c => c.Id).First();
                var by = w.Crew.Where(c => c != p && c.CanAct && !c.IsChild && !c.Outside).OrderBy(c => c.Id).First();
                Teleport(w, p, a);
                Teleport(w, by, walk.OrderBy(c => (c.Center - (a.Center + z.Center) * 0.5f).LengthSquared()).First());
                w.Step();
                Force(w, by, new Job(null, "구경", new List<Toil> { new WaitToil(SimTime.Hours(2), Pose.Standing) }));
                int wi = w.Ship.Grid.Index(walk[walk.Count / 2]);
                if (wet && urgent) { runRisk = b.SlipRisk(p, wi, true, false); walkRisk = b.SlipRisk(p, wi, false, false); }
                var toils = new List<Toil>();
                for (int k = 0; k < 12; k++) toils.Add(new GotoToil(k % 2 == 0 ? z : a));
                int s0 = b.Stats.Slips;
                Force(w, p, new Job(null, "왔다 갔다", toils) { Urgent = urgent });
                for (int t = 0; t < SimTime.Hours(1) && p.Job?.Label == "왔다 갔다"; t++)
                {
                    w.Step();
                    if (wet) foreach (var c in room.Cells) if (b.Mark(c, CellMark.Wet) < 0.9f) b.SetMark(c, CellMark.Wet, 1f, "시험");
                }
                seen = $"곁에 선 {by.Name}: 봤다 {b.Stats.Witnessed} · 조심 {b.Cautious(by, room)}({b.CautionWhy(by)}) · 넘어진 사람 조심 {b.Cautious(p, room)}";
                if (wet && urgent && b.Stats.Slips > s0 && !(b.Stats.Witnessed > 0 && b.Cautious(by, room))) seen = "✘ " + seen;
                return b.Stats.Slips - s0;
            }
            int run = Slips(true, true, out var where, out var seenText), dry = Slips(true, false, out _, out _);
            Check("미끄럼 — 젖은 타일에서 뛰면 미끄러진다 (걸으면 드물고, 마른 바닥에선 없다)", run >= 1 && runRisk > walkRisk * 4f && walkRisk > 0f && dry == 0,
                $"{where} · 젖은 바닥 뛰기 {run}번 넘어짐 · 한 칸 위험 뛰기 {runRisk:0.000} / 걷기 {walkRisk:0.000} · 마른 바닥 뛰기 {dry}");
            Check("★★ 승무원 — 옆 사람이 미끄러지는 걸 본 사람은 그 방에서 조심해서 걷는다", run >= 1 && !seenText.StartsWith("✘"), seenText);

            var w2 = DayOne(seed, "Hanbit");
            var flood = w2.Ship.LiveRooms.First(r => r.Kind == RoomType.Galley);
            flood.Flood = flood.Cells.Count * 20f * 0.3f;
            Run(w2, SimTime.Minutes(2));
            var c0 = flood.Cells.First(w2.Ship.IsWalkable);
            var ms = w2.Body.MarksAt(c0);
            // 양방향: 격자 바닥은 물을 빌지로 빼고(침수가 준다) · 젖은 바닥은 불이 덜 번지고 카펫은 잘 번진다
            var w3 = DayOne(seed, "Hanbit");
            var grateRoom = w3.Ship.LiveRooms.First(r => w3.Body.FloorAt(r.Cells[0]) == Material.Grate);
            grateRoom.Flood = grateRoom.Cells.Count * 20f * 0.2f;
            Run(w3, SimTime.Minutes(30));
            var carpetCell = w3.Ship.LiveRooms.SelectMany(r => r.Cells).FirstOrDefault(c => w3.Body.FloorAt(c) == Material.Carpet);
            var metalCell = w3.Ship.LiveRooms.SelectMany(r => r.Cells).First(c => w3.Body.FloorAt(c) == Material.MetalPlate);
            float dryMetal = w3.Body.SpreadMul(metalCell), carpetMul = w3.Body.FloorAt(carpetCell) == Material.Carpet ? w3.Body.SpreadMul(carpetCell) : 1.3f;
            w3.Body.SetMark(metalCell, CellMark.Wet, 1f, "시험");
            float wetMetal = w3.Body.SpreadMul(metalCell);
            Check("양방향 — 격자 바닥은 침수를 빼내고 · 불은 카펫에서 잘, 젖은 바닥에선 덜 번진다", w3.Body.Stats.Drained > 1f && carpetMul > dryMetal && wetMetal < dryMetal * 0.6f,
                $"{grateRoom.Name} 빠진 물 {w3.Body.Stats.Drained:0}L · 번짐 배율 카펫 {carpetMul:0.00} · 마른 금속판 {dryMetal:0.00} · 젖은 금속판 {wetMetal:0.00}");
            Check("칸 상태 — 바닥 물(침수)이 젖음으로 (값 + 원인 + 시각)", ms != null && ms[CellMark.Wet] > 0.3f && ms.Cause[(int)CellMark.Wet] == "침수" && ms.Since[(int)CellMark.Wet] > 0,
                $"{flood.Name} {Materials.Name(w2.Body.FloorAt(c0))} 젖음 {ms?[CellMark.Wet]:0.00} · 원인 {ms?.Cause[(int)CellMark.Wet]} · {(ms != null ? SimTime.Clock(ms.Since[(int)CellMark.Wet]) : "-")}");
        }

        // 6) 패킹이 삭은 문: 압력 차에 미세 누출 · 휘파람 / 표시판 고장이면 오판
        {
            var w = DayOne(seed, "Hanbit");
            var b = w.Body;
            var door = w.Ship.Doors.First(d => !d.IsExternal && d.RoomA != null && d.RoomB != null && d.RoomA.Kind != RoomType.Corridor && b.DoorOf(d)!.Zone == AccessZone.Open);
            var db = b.DoorOf(door)!;
            db.Gasket = 0.05f;
            door.Openness = 0f;
            var lo = door.RoomA!;
            lo.Air.O2 -= 12f; lo.Air.N2 -= 20f;
            float dp0 = MathF.Abs(lo.Air.Pressure - door.RoomB!.Air.Pressure);
            for (int t = 0; t < World.SystemInterval * 3; t++) { w.Step(); door.Openness = 0f; }
            Check("패킹 — 삭은 문은 닫혀 있어도 새고 휘파람 소리가 난다", b.Stats.Whistles > 0 && b.Stats.MicroLeaks > 0,
                $"{lo.Name}·{door.RoomB.Name} 압력 차 {dp0:0.0}kPa · 휘파람 {b.Stats.Whistles} · 누출 {b.Stats.MicroLeaks}");
            for (int t = 0; t < SimTime.Minutes(2); t++) { w.Step(); door.Openness = 0f; }
            var gact = w.Automation.Book.Acts.LastOrDefault(a => a.Key == "gasket:" + door.Id);
            Check("★★ 주 컴퓨터 — 닫힌 문 너머 압력 누출을 읽고 '패킹 노화' 판단 · 정비 요청 (손보기가 먼저 한다)", db.Flagged && gact != null,
                gact != null ? $"관측: {gact.Observe} · 판단: {gact.Judge} · 조치: {gact.Act} · 요청: {gact.Request}" : $"요청 {db.Flagged} · 기록 없음");
            db.IndicatorBroken = true; db.IndicatorSaysSafe = true;
            string? shown = b.Reading(door, "8kPa");
            Check("표시판 — 고장이면 진공을 '정상'으로 오판한다", shown == null && b.Stats.FalseReadings > 0, $"실제 8kPa → 표시 {shown ?? "정상"}");

            // 주 컴퓨터: 표시판 '정상' ↔ 압력 감지기 진공 → 표시판을 믿지 않고 방송 · 들은 사람은 실제 위험을 안다
            db.Gasket = 1f;
            for (int t = 0; t < SimTime.Minutes(2); t++) { lo.Air.O2 = 0f; lo.Air.N2 = 0.5f; door.Openness = 0f; w.Step(); }
            var iact = w.Automation.Book.Acts.LastOrDefault(a => a.Key == "indicator:" + door.Id);
            var bc = w.Automation.Speak.Recent.LastOrDefault(x => x.Text.Contains("표시판이 고장"));
            var heard = bc == null ? null : w.Crew.FirstOrDefault(c => bc.HeardBy.Contains(c.Id));
            var deaf = bc == null ? null : w.Crew.FirstOrDefault(c => !c.Dead && !bc.HeardBy.Contains(c.Id));
            string? toHeard = heard != null ? b.Reading(door, "진공", heard) : null;
            Check("★★ 표시판 대조 — 주 컴퓨터는 고장 난 표시판 대신 압력 감지기를 믿고 방송한다 · 들은 사람은 표시판에 속지 않는다", db.IndicatorWarned && iact != null && bc != null && toHeard == "진공",
                $"{iact?.Observe} → {iact?.Judge} · 방송 들은 {bc?.HeardBy.Count}명 · {heard?.Name}: {toHeard ?? "정상"} · 못 들은 {deaf?.Name ?? "-"}: {(deaf != null ? b.Reading(door, "진공", deaf) ?? "정상(오판)" : "-")}");
        }

        // 6-2) ★★ 주 컴퓨터가 바닥 물 감지기를 읽고 방송 → 들은 사람만 조심 · 걸레질 요청 → 손보기가 닦는다
        {
            var w = DayOne(seed, "Hanbit");
            var b = w.Body;
            var room = w.Ship.LiveRooms.Where(r => b.FloorAt(r.Cells[0]) == Material.Tile && r.Cells.Count(w.Ship.IsWalkable) >= 8).OrderByDescending(r => r.Cells.Count).First();
            var deafRoom = w.Ship.LiveRooms.First(r => r != room && r.Kind == RoomType.Storage || r != room && r.Kind == RoomType.Workshop);
            w.Automation.Speak.BreakSpeaker(deafRoom, "시험", 6f);
            var A = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside && c.IsAwake).OrderBy(c => c.Id).First();
            var B = w.Crew.Where(c => c != A && c.CanAct && !c.IsChild && !c.Outside && c.IsAwake).OrderBy(c => c.Id).First();
            Teleport(w, A, room.Cells.First(w.Ship.IsWalkable));
            Teleport(w, B, deafRoom.Cells.First(w.Ship.IsWalkable));
            w.Step();
            Force(w, A, new Job(null, "기다림", new List<Toil> { new WaitToil(SimTime.Minutes(10), Pose.Standing) }), SimTime.Minutes(10));
            Force(w, B, new Job(null, "기다림", new List<Toil> { new WaitToil(SimTime.Minutes(10), Pose.Standing) }), SimTime.Minutes(10));
            foreach (var c in room.Cells) b.SetMark(c, CellMark.Wet, 1f, "시험");
            Run(w, SimTime.Minutes(2));
            var act = w.Automation.Book.Acts.LastOrDefault(a => a.Key == "wetfloor:" + room.Id);
            var bc = w.Automation.Speak.Recent.LastOrDefault(x => x.Text.Contains("바닥이 젖었다"));
            int wi = w.Ship.Grid.Index(room.Cells.Where(w.Ship.IsWalkable).ElementAt(3));
            float rA = b.SlipRisk(A, wi, true, false), rB = b.SlipRisk(B, wi, true, false);
            Check("★★ 주 컴퓨터 — 바닥 물 감지기로 젖은 타일을 읽고 '뛰지 마라' 방송 · 걸레질 요청 — 들은 사람만 조심한다",
                act != null && bc != null && b.Cautious(A, room) && !b.Cautious(B, room) && rA < rB * 0.2f && b.MopRequested(room),
                $"{act?.Observe} → {act?.Judge} · {act?.Act} · 요청 {act?.Request} · 들은 {Ko.IGa(A.Name)} 조심 {b.Cautious(A, room)}(뛰어도 위험 {rA:0.000}) · {deafRoom.Name}(스피커 고장)의 {Ko.IGa(B.Name)} 조심 {b.Cautious(B, room)}(위험 {rB:0.000})");
            // 걸레질: 계속 젖게 두면 손보기가 요청을 받고 닦으러 온다
            int m0 = b.Stats.Mopped;
            string? mopper = null;
            for (int t = 0; t < SimTime.Hours(3) && b.Stats.Mopped == m0; t++)
            {
                w.Step();
                if (t % 30 == 0) foreach (var c in room.Cells) if (b.Mark(c, CellMark.Wet) > 0.05f && b.Mark(c, CellMark.Wet) < 0.9f) b.SetMark(c, CellMark.Wet, 1f, "시험");
                mopper ??= w.Crew.FirstOrDefault(c => c.Job?.Label == "젖은 바닥 걸레질")?.Name;
            }
            Check("★★ 승무원 — 컴퓨터의 걸레질 요청을 받고 젖은 바닥을 닦는다", b.Stats.Mopped > m0, $"닦은 사람 {mopper ?? "없음"} · 걸레질 {b.Stats.Mopped - m0}번 · {SimTime.Clock(w.Tick)}");
        }

        // 6-3) 상호작용: 엎지른 국 → 젖은 칸 · 오래 젖은 카펫 → 곰팡이 → 균 → 악취
        {
            var w = DayOne(seed, "Hanbit");
            var b = w.Body;
            var mess = w.Ship.LiveRooms.FirstOrDefault(r => r.Kind == RoomType.Mess) ?? w.Ship.LiveRooms.First(r => r.Kind == RoomType.Galley);
            var cook = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside).OrderBy(c => c.Id).First();
            Teleport(w, cook, mess.Cells.First(w.Ship.IsWalkable));
            w.Step();
            var spill = w.Scenes.OpenSpill(cook);
            Run(w, SimTime.Minutes(2));
            var stain = spill?.Things.FirstOrDefault(t => t.Kind == ThingKind.Stain);
            var sm = stain != null ? b.MarksAt(stain.At) : null;
            Check("상호작용 — 엎지른 국(일상 장면)이 그 칸을 적신다 (바닥재대로 미끄럽다)", sm != null && sm[CellMark.Wet] > 0.3f && sm.Cause[(int)CellMark.Wet] == "엎지른 국",
                $"{mess.Name} {(stain != null ? Materials.Name(b.FloorAt(stain.At)) : "-")} 젖음 {sm?[CellMark.Wet]:0.00} · 원인 {sm?.Cause[(int)CellMark.Wet] ?? "-"} · 미끄러움 {(stain != null ? b.SlipAt(w.Ship.Grid.Index(stain.At)) : 0f):0.00}");

            var cabin = w.Ship.LiveRooms.First(r => b.FloorAt(r.Cells[0]) == Material.Carpet);
            var soil = w.Soil.RoomSoil(cabin);
            float bio0 = soil[(int)SoilKind.Bio], foul0 = w.Smells.Level(cabin, SmellKind.Foul);
            var wetCells = cabin.Cells.Where(w.Ship.IsWalkable).Take(4).ToList();
            for (int t = 0; t < SimTime.Hours(4); t++)
            {
                if (t % 20 == 0) foreach (var c in wetCells) b.RaiseMark(c, CellMark.Wet, 0.9f, "샌 물");
                w.Step();
            }
            float bio = soil[(int)SoilKind.Bio], foul = w.Smells.Level(cabin, SmellKind.Foul);
            Check("상호작용 — 오래 젖은 카펫에 곰팡이 → 방에 균 → 냄새가 악취로 번진다", bio > bio0 + 0.2f && foul > foul0 && b.Stats.Mildew > 0,
                $"{cabin.Name} 균 {bio0:0.00}→{bio:0.00} · 악취 {foul0:0.00}→{foul:0.00} · 곰팡이 {b.Stats.Mildew}");
        }

        // 6-4) 정비 통로: 문으로 바로 이어지지 않은 두 방 사이를 느리게 기어서 질러간다 · 업은 사람 · 다친 사람은 못 지난다 · 압력 차면 덮개가 잠긴다
        {
            var w = DayOne(seed, "Hanbit");
            var b = w.Body;
            Run(w, SimTime.Minutes(1));
            WallBody? cw = null;
            Cell sa = default, sb = default;
            List<Cell>? via = null, around = null;
            foreach (var x in b.WallList.Where(x => x.Crawl && x.CrawlOpen))
            {
                var a0 = Cell.Dirs4.Select(dd => x.Cell + dd).First(c => w.Ship.RoomAt(c)?.Id == x.CrawlA);
                var b0 = x.Cell + (x.Cell - a0);
                var p1 = w.Paths.Find(a0, b0, PathProfile.Default);
                if (p1 == null || !p1.Contains(x.Cell)) continue;
                cw = x; sa = a0; sb = b0; via = p1;
                around = w.Paths.Find(a0, b0, PathProfile.Default with { NoCrawl = true });
                break;
            }
            if (cw != null)
            {
                var p = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside).OrderBy(c => c.Id).First();
                Teleport(w, p, sa);
                w.Step();
                Force(w, p, new Job(null, "질러가기", new List<Toil> { new GotoToil(sb) }));
                int c0 = b.Stats.Crawls;
                long t0 = w.Tick;
                for (int t = 0; t < SimTime.Minutes(10) && p.Cell != sb; t++) w.Step();
                bool crawled = b.Stats.Crawls > c0 && p.Cell == sb;
                long took = w.Tick - t0;
                // 한쪽 방 압력이 떨어지면 덮개가 잠기고 길에서 빠진다
                var ra = w.Ship.Rooms[cw.CrawlA];
                for (int t = 0; t < SimTime.Minutes(2); t++) { ra.Air.O2 = 6f; ra.Air.N2 = 30f; w.Step(); }
                var after = w.Paths.Find(sa, sb, PathProfile.Default);
                bool sealedOff = !cw.CrawlOpen && (after == null || !after.Contains(cw.Cell));
                Check("정비 통로 — 문 없는 두 방 사이를 기어서 질러간다 (느리다) · 업은 사람 · 다친 사람은 돌아간다 · 압력 차면 덮개가 잠긴다",
                    crawled && (around == null || !around.Contains(cw.Cell)) && sealedOff,
                    $"{w.Ship.Rooms[cw.CrawlA].Name}↔{w.Ship.Rooms[cw.CrawlB].Name} {cw.Cell} · 통로로 {via!.Count}칸 · 못 기는 사람은 {(around != null ? $"{around.Count}칸 돌아감" : "길 없음")} · {p.Name} 기어감 {crawled}({took}틱) · 압력 차 뒤 덮개 열림 {cw.CrawlOpen} · 정비 통로 {b.Stats.Crawlways}곳");
            }
            else Check("정비 통로 — 배에 쓸 만한 정비 통로가 있어야 한다", false, $"{b.Stats.Crawlways}곳 · 열림 {b.WallList.Count(x => x.Crawl && x.CrawlOpen)}");
        }

        // 7) 모든 배 (+ 생성 배)에서 하루 정상
        {
            var keys = ShipCatalog.All.Select(t => t.Key).Append(ShipGenerator.KeyFor(12, seed)).ToList();
            var bad = new List<string>();
            int grease = 0;
            foreach (var key in keys)
            {
                try
                {
                    var w = World.CreateDefault(seed, 0, key);
                    int crew = w.Crew.Count(c => !c.Dead);
                    Run(w, SimTime.TicksPerDay);
                    var b = w.Body;
                    grease += b.Stats.Grease;
                    int dead = crew - w.Crew.Count(c => !c.Dead);
                    float worn = b.Wear.Max();
                    int noFloor = w.Ship.LiveRooms.Sum(r => r.Cells.Count(c => b.FloorAt(c) == Material.None));
                    bool ok = dead == 0 && worn > 0f && (crew < 4 || worn > 0.01f) && /* 두세 명 배는 하루에 조금만 닳는다 */ noFloor == 0 && b.Stats.Falls <= crew * 2 && b.Stats.GaveUp <= crew * 3;
                    if (!ok) bad.Add(key);
                    var dark = w.Ship.Doors.Where(d => !d.Powered && !d.Removed && !d.IsExternal).ToList();
                    Console.WriteLine($"    {w.Ship.Name}: 사람 {crew} · 사망 {dead} · 가장 닳은 칸 {worn:0.000} · 전기 없는 문 {dark.Count}({string.Join(",", dark.Take(3).Select(d => $"{d.RoomA?.Name}·{d.RoomB?.Name}{(d.MotorBroken ? "(모터)" : "")}"))}) · {b.Stats}");
                }
                catch (Exception e) { bad.Add($"{key}: {e.GetType().Name} {e.Message}"); Console.WriteLine(e); }
            }
            Check("모든 배 — 카탈로그 배와 생성 배에서 하루가 정상으로 지나고 바닥이 닳는다", bad.Count == 0, $"{keys.Count}척" + (bad.Count > 0 ? $" · 문제: {string.Join(", ", bad)}" : ""));
            Check("상호작용 — 끓는 화구 앞에 기름이 튄다 (조리 → 주방 바닥)", grease > 0, $"하루 동안 기름 튐 {grease}번 (모든 배)");
        }

        // 8) 결정론
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 — 같은 시드 두 번 지문이 같다", h1 == h2, $"{h1:x8} / {h2:x8}");
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
