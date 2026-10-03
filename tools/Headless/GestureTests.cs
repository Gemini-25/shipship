using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v17.2 몸짓 · 소리 · 버릇 — 검증 장면을 헤드리스에서 실제로 일으킨다
public static partial class Program
{
    private static readonly HashSet<Mien> _mienSeen = new();
    private static readonly HashSet<Noise> _noiseHeard = new();
    private static readonly HashSet<Tic> _ticSeen = new();

    private static void TallyGest(World w)
    {
        foreach (Mien m in Enum.GetValues<Mien>()) if (m != Mien.None && w.Gestures.Stats.ByMien[(int)m] > 0) _mienSeen.Add(m);
        foreach (Noise k in Enum.GetValues<Noise>()) if (w.Hearing.Stats.HeardKind[(int)k] > 0) _noiseHeard.Add(k);
        foreach (Tic t in Enum.GetValues<Tic>()) if (w.Gestures.Stats.ByTic[(int)t] > 0) _ticSeen.Add(t);
    }

    private static void RunE(World w, long ticks, Action every) { for (long t = 0; t < ticks; t++) { every(); w.Step(); } }

    private static World GDay(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        RunUntilHour(w, 10f);
        return w;
    }

    private static void GWalk(World w, CrewMember c, Cell to, string label = "짐 나르기")
    {
        c.EndJob(w, ToilStatus.Interrupted);
        if (c.Pose == Pose.Sleeping) c.Pose = Pose.Standing;
        c.StartJob(new Job(null, label, new Toil[] { new GotoToil(to), new WaitToil(SimTime.Hours(2), Pose.Standing) }), w, null);
        c.NextThinkTick = w.Tick + SimTime.Hours(3);
    }

    private static void WorkAt(World w, CrewMember c, Cell at, Skill skill, System.Numerics.Vector2 face, float hours = 2f)
    {
        c.EndJob(w, ToilStatus.Interrupted);
        c.Position = at.Center; c.PreviousPosition = c.Position;
        c.StartJob(new Job(null, "정비", new Toil[] { new WorkToil(hours, skill, face) }), w, null);
        c.NextThinkTick = w.Tick + SimTime.Hours(3);
    }

    /// <summary>문 하나와 양쪽 바닥 칸 (서로 다른 방).</summary>
    private static (Door d, Cell a, Cell b, Cell dir)? DoorSpot(World w, Func<Door, bool>? ok = null)
    {
        foreach (var d in w.Ship.Doors.OrderBy(d => d.Id))
        {
            if (d.IsExternal || d.Removed || d.Welded || d.Bulkhead || d.RoomA is not Room ra || d.RoomB is not Room rb || ra.Detached || rb.Detached || ra == rb) continue;
            if (ok != null && !ok(d)) continue;
            foreach (var dir in new[] { new Cell(1, 0), new Cell(0, 1) })
            {
                var a = new Cell(d.Cell.X - dir.X, d.Cell.Y - dir.Y);
                var b = new Cell(d.Cell.X + dir.X, d.Cell.Y + dir.Y);
                var a2 = new Cell(a.X - dir.X, a.Y - dir.Y);
                var b2 = new Cell(b.X + dir.X, b.Y + dir.Y);
                var s = w.Ship;
                if (s.RoomAt(a) is Room x && s.RoomAt(b) is Room y && x != y && s.IsWalkable(a) && s.IsWalkable(b) && s.IsWalkable(a2) && s.IsWalkable(b2) && s.RoomAt(a2) == x && s.RoomAt(b2) == y)
                    return (d, a, b, dir);
            }
        }
        return null;
    }

    private static void Away(World w, IEnumerable<CrewMember> keep, Cell near, float radius)
    {
        var far = w.Ship.Rooms.Where(r => !r.Detached && r.Type == RoomType.Quarters).SelectMany(r => r.Cells).Where(x => w.Ship.IsWalkable(x) && (x.Center - near.Center).Length() > 12f).ToList();
        int i = 0;
        foreach (var c in w.Crew)
        {
            if (keep.Contains(c) || c.Dead || (c.Position - near.Center).Length() > radius || far.Count == 0) continue;
            Stay(w, c, far[(i++ * 7) % far.Count], Pose.Standing);
        }
    }

    private static int RunGestureTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"몸짓 · 소리 · 버릇 점검 (v17.2) · 시드 {seed}\n");
        string only = Environment.GetEnvironmentVariable("SHIPSIM_SCENE") ?? "";
        bool Do(string k) => only == "" || only.Split(',').Contains(k);
        _mienSeen.Clear(); _noiseHeard.Clear(); _ticSeen.Clear();

        // ── 1) 양손 짐으로 손이 필요한 문 앞: 혼자면 내려놓고 · 곁에 사람이 있으면 부탁
        if (Do("1"))
        {
            var w = GDay(seed);
            var spot = DoorSpot(w);
            if (spot is var (door, a, b, dir))
            {
                var db = w.Body.DoorOf(door)!;
                db.SensorBroken = true; // 센서가 먹통 — 버튼을 손으로 눌러야 한다
                var ppl = Awake(w, 2);
                var c = ppl[0];
                Away(w, new[] { c }, door.Cell, 9f);
                c.Carrying = new ItemStack(ItemKind.Structure, 3);
                Place(c, new Cell(a.X - dir.X, a.Y - dir.Y));
                var goal = new Cell(b.X + dir.X, b.Y + dir.Y);
                GWalk(w, c, goal);
                var gs = w.Gestures;
                bool passed = false, sawBox = false;
                for (int t = 0; t < 400 && !passed; t++)
                {
                    w.Step();
                    db.SensorBroken = true;
                    if (gs.MienOf(c) == Mien.SetDown && gs.Peek(c)?.Spot != null) sawBox = true;
                    if (c.Room == w.Ship.RoomAt(goal)) passed = true;
                }
                Check("양손 짐 · 혼자 — 문 앞에서 짐을 내려놓고 버튼을 누른 뒤 다시 든다", gs.Stats.SetDowns >= 1 && sawBox && passed,
                    $"내려놓기 {gs.Stats.SetDowns} · 바닥 짐 그림 {sawBox} · 지나감 {passed} · 센서 버튼 {w.Body.Stats.SensorPresses}");
                // 곁에 사람이 있으면 부탁한다
                Run(w, SimTime.Minutes(12));
                var h = ppl[1];
                c.Carrying = new ItemStack(ItemKind.Structure, 3);
                h.Carrying = null;
                Stay(w, h, new Cell(a.X + dir.Y, a.Y + dir.X) is var side && w.Ship.IsWalkable(side) && w.Ship.RoomAt(side) == w.Ship.RoomAt(a) ? side : a, Pose.Standing);
                Place(c, new Cell(a.X - dir.X, a.Y - dir.X == 0 ? a.Y - dir.Y : a.Y - dir.Y));
                GWalk(w, c, goal);
                bool held = false; passed = false;
                for (int t = 0; t < 400 && !passed; t++)
                {
                    w.Step();
                    db.SensorBroken = true;
                    if (gs.MienOf(h) == Mien.HoldDoor) held = true;
                    if (c.Room == w.Ship.RoomAt(goal)) passed = true;
                }
                var ask = gs.Notes.LastOrDefault(n => n.Mien == Mien.AskDoor);
                Check("양손 짐 · 곁에 사람 — \"문 좀 잡아 줄래?\" · 그 사람이 문을 잡아 준다", gs.Stats.Asked >= 1 && held && passed,
                    $"부탁 {gs.Stats.Asked} · 잡아 줌 {held} · 지나감 {passed} · \"{ask?.Line}\" · 내려놓기 {gs.Stats.SetDowns} · {h.Name}@{h.Room?.Name} {h.Pose} {h.Carrying} · 문 {door.Openness:0.0}");
                TallyGest(w);
            }
            else Check("양손 짐 — 시험할 문이 없다", false);
        }

        // ── 2) 말을 걸면 고개부터 돌리고, 하던 걸 정리하고, 그다음 대답
        if (Do("2"))
        {
            var w = GDay(seed);
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Workshop or RoomType.Engine or RoomType.Mess or RoomType.Lounge && !r.OffLimits).OrderByDescending(r => r.Cells.Count).First();
            var ppl = Awake(w, 4, c => -c.Traits.Sociability);
            var worker = ppl.Last();
            var cells = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).ToList();
            Gather(w, ppl, room);
            Stay(w, worker, cells[cells.Count / 2], Pose.Working);
            worker.Pose = Pose.Working;
            w.React.MarkOdd(worker, "혼잣말을 한다", 90f);
            var gs = w.Gestures;
            long turnAt = -1, wrapAt = -1, answerAt = -1;
            int answered0 = gs.Stats.Answered;
            for (int t = 0; t < SimTime.Hours(5) && answerAt < 0; t++)
            {
                if (t % SimTime.Minutes(40) == SimTime.Minutes(39) && gs.Stats.Addressed == 0) { Gather(w, ppl.Take(3).ToList(), room); w.React.MarkOdd(worker, t % 2 == 0 ? "혼잣말을 한다" : "벽에 대고 중얼거린다", 90f); }
                w.Step();
                if (worker.Pose != Pose.Working && worker.Job?.Current is WaitToil) worker.Pose = Pose.Working;
                var m = gs.MienOf(worker);
                if (m == Mien.HeadTurn && turnAt < 0) turnAt = w.Tick;
                if (m == Mien.WrapUp && wrapAt < 0 && turnAt >= 0) wrapAt = w.Tick;
                if (gs.Stats.Answered > answered0) answerAt = w.Tick;
            }
            Check("말 걸면 — 고개부터 돌리고 (그다음) 하던 걸 정리한다", turnAt >= 0 && wrapAt > turnAt, $"고개 {turnAt} · 정리 {wrapAt} · 말 걸림 {gs.Stats.Addressed} · 정리 {gs.Stats.WrapUps}");
            Check("말 걸면 — 정리를 마친 뒤에 대답한다", answerAt > wrapAt && wrapAt > 0 && gs.Stats.AnsweredAfterWrap == gs.Stats.Answered,
                $"대답 {answerAt} · 대답 {gs.Stats.Answered}(정리 뒤 {gs.Stats.AnsweredAfterWrap}) · 반응 말 걸기 {w.React.Stats.Talks}/{w.React.Stats.Replies}");
            TallyGest(w);
        }

        // ── 3) 쓰러진 사람 곁을 지나면 무릎 — 숨을 살피고 부른다 (들은 사람은 안다)
        if (Do("3"))
        {
            var w = GDay(seed);
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Lounge or RoomType.Workshop && r.Cells.Count >= 12).OrderByDescending(r => r.Cells.Count).First();
            var cells = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).OrderBy(x => x.X).ThenBy(x => x.Y).ToList();
            var ppl = Awake(w, 3, c => c.Role == CrewRole.Medic ? 1 : 0);
            var down = ppl[0]; var passer = ppl[1]; var other = ppl[2];
            Stay(w, down, cells[cells.Count / 2], Pose.Standing);
            down.Vitals.Health = 0.1f;
            Run(w, 3);
            var far = cells.First(x => (x.Center - down.Position).Length() > 3.5f);
            Stay(w, other, far, Pose.Standing);
            var start = cells.Where(x => (x.Center - down.Position).Length() is > 2.5f and < 4f).OrderBy(x => x.X).First();
            Place(passer, start);
            var goal = cells.Where(x => (x.Center - start.Center).Length() > 4f && (x.Center - down.Position).Length() < 3f).OrderByDescending(x => (x.Center - start.Center).Length()).FirstOrDefault();
            GWalk(w, passer, goal != default ? goal : down.Cell, "지나가기");
            bool knelt = false;
            for (int t = 0; t < 200; t++)
            {
                if (down.Vitals.Health > 0.12f) down.Vitals.Health = 0.1f;
                w.Step();
                if (Puppet.PoseOf(w, passer) == PuppetPose.Kneel && w.Gestures.MienOf(passer) == Mien.KneelBy) knelt = true;
            }
            var gs = w.Gestures;
            var kn = gs.Notes.FirstOrDefault(n => n.Mien == Mien.KneelBy);
            bool heard = w.Brain2.Beliefs.Believes(other, Topic.Down, down.Id);
            Check("쓰러진 사람 곁을 지나면 — 무릎을 꿇고 숨을 살핀다", down.Down && knelt && gs.Stats.Kneels >= 1, $"쓰러짐 {down.Down} · 무릎 {gs.Stats.Kneels} · 인형 무릎 {knelt} · \"{kn?.Line}\"");
            Check("무릎 꿇은 사람이 부른다 — 소리가 닿는 사람은 쓰러진 줄 안다", gs.Stats.Calls >= 1 && heard, $"부름 {gs.Stats.Calls} · {other.Name} 앎 {heard}");
            TallyGest(w);
        }

        // ── 4) 신입은 계기와 선배 얼굴을 번갈아 본다 · 선배가 끄덕인다
        if (Do("4"))
        {
            var w = GDay(seed);
            var m = w.Ship.Machines.Where(x => x.Body.Room is Room r && !r.OffLimits && r.Type != RoomType.Corridor).OrderBy(x => x.Body.Id).First();
            var room = m.Body.Room!;
            var cells = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - m.Body.Center).Length()).ToList();
            var ppl = Awake(w, 2);
            var rookie = ppl[0]; var senior = ppl[1];
            var skill = m.Spec.Skill;
            rookie.SkillLevels[(int)skill] = 0.1f; senior.SkillLevels[(int)skill] = 0.85f;
            Stay(w, senior, cells[Math.Min(3, cells.Count - 1)], Pose.Standing);
            WorkAt(w, rookie, cells[0], skill, m.Body.Center);
            bool gauge = false, face = false;
            for (int t = 0; t < SimTime.Minutes(12); t++)
            {
                w.Step();
                var mm = w.Gestures.MienOf(rookie);
                if (mm == Mien.GlanceGauge) gauge = true;
                if (mm == Mien.GlanceSenior && w.Gestures.GazeOf(rookie) is System.Numerics.Vector2 g && (g - senior.Position).Length() < 0.01f) face = true;
            }
            var gs = w.Gestures;
            Check("신입 — 계기와 선배 얼굴을 번갈아 본다 · 선배가 끄덕여 준다", gauge && face && gs.Stats.Glances >= 4 && gs.Stats.Nods >= 1,
                $"계기 {gauge} · 선배 얼굴 {face} · 번갈아 {gs.Stats.Glances} · 끄덕임 {gs.Stats.Nods}");
            TallyGest(w);
        }

        // ── 5) 다친 팔 → 공구를 다른 손으로 · 익숙한 부위부터 (반복 고장)
        if (Do("5"))
        {
            var w = GDay(seed);
            var m = w.Ship.Machines.Where(x => x.Body.Room is Room r && !r.OffLimits && x.Spec.FaultKinds.Length > 0 && w.Parts.Owner(x, x.Spec.FaultKinds[0]) != null).OrderBy(x => x.Body.Id).First();
            var room = m.Body.Room!;
            var cells = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - m.Body.Center).Length()).ToList();
            var c = Awake(w, 1)[0];
            var ms = w.Gestures.Of(c);
            var arm = ms.LeftHanded ? BodyPart.LeftArm : BodyPart.RightArm;
            c.Vitals.Injury = 0.5f;
            c.Vitals.Wounds.Add(new Wound { Part = arm, Kind = WoundKind.Fracture, Weight = 1f, Cause = "시험" });
            WorkAt(w, c, cells[0], m.Spec.Skill, m.Body.Center);
            Run(w, SimTime.Minutes(3));
            var sw = w.Gestures.Notes.FirstOrDefault(n => n.Mien == Mien.SwapGrip && n.Crew == c.Id);
            Check("다친 팔 — 공구를 다른 손으로 바꿔 쥔다", sw != null && Puppet.Arm(c, arm) == ArmState.Hurt, $"팔 {Puppet.Arm(c, arm)} · \"{sw?.Line}\" · 손 바꿈 {w.Gestures.Stats.Swaps}");
            // 반복 고장: 전에 같은 부위를 고쳐 본 사람은 그 부위부터 (수리 시간이 준다)
            var k = m.Spec.FaultKinds[0];
            var part = w.Parts.Owner(m, k)!;
            float f1 = w.Gestures.Familiar(c, m, k);
            part.Failures++;
            float f2 = w.Gestures.Familiar(c, m, k);
            c.Vitals.Wounds.Clear(); c.Vitals.Injury = 0f;
            WorkAt(w, c, cells[0], m.Spec.Skill, m.Body.Center);
            Run(w, SimTime.Minutes(3));
            var ks = w.Gestures.Notes.FirstOrDefault(n => n.Mien == Mien.KnownSpot && n.Crew == c.Id);
            Check("반복 고장 — 익숙한 부위부터 손이 간다 (수리가 빠르다)", f1 == 1f && f2 < 1f && ks != null, $"처음 {f1} · 다시 {f2} · \"{ks?.Line}\"");
            TallyGest(w);
        }

        // ── 6) 낡은 베어링의 주기 잡음 — 들어 온 사람은 수리 뒤 조용해진 걸 안다 · 컴퓨터도 듣는다
        if (Do("6"))
        {
            var w = GDay(seed);
            var m = w.Ship.Machines.Where(x => x.Body.Room is Room r && !r.OffLimits && r.Type != RoomType.Corridor && x.Active && x.Powered && x.Omen == null && x.Faults.Count == 0)
                .OrderByDescending(x => x.Body.Room!.Cells.Count).ThenBy(x => x.Body.Id).First();
            var room = m.Body.Room!;
            m.Omen = new Omen { Kind = OmenKind.Vibration, Fault = FaultKind.BearingWear, Cause = OmenCause.BearingWear, Since = w.Tick, Due = w.Tick + SimTime.Hours(40) };
            var ppl = Awake(w, 2, c => c.SkillLevel(Skill.Mechanics)); // 솜씨 없는 사람 둘 (확인하러 가지 않게)
            Gather(w, ppl, room);
            RunE(w, SimTime.Minutes(40), () => { if (ppl.Any(c => c.Room != room)) Gather(w, ppl, room); });
            bool noisy = w.Hearing.Sources.Any(s => s.Kind == Noise.Bearing && s.Owner == m.Body.Id && s.Period > 0f);
            bool heard = w.Hearing.In(room).Any(h => w.Hearing.Sources[h.Src].Kind == Noise.Bearing);
            Check("낡은 베어링 — 일정한 간격의 잡음이 소리 정보로 난다 (방 사람이 듣는다)", noisy && heard, $"{m.Name} 소리 {noisy} · 방에서 들림 {heard}");
            int q0 = w.Hearing.Stats.Quieted;
            m.Omen = null; // 손봤다
            RunE(w, SimTime.Minutes(2), () => { if (ppl.Any(c => c.Room != room)) Gather(w, ppl, room); });
            bool gone = !w.Hearing.Sources.Any(s => s.Kind == Noise.Bearing && s.Owner == m.Body.Id);
            var qn = w.Hearing.Notes.LastOrDefault(n => n.Kind == Noise.Bearing && n.Line.Length > 0);
            Check("수리가 끝나면 거슬리던 잡음이 사라진다 — 들어 온 사람이 알아챈다", gone && w.Hearing.Stats.Quieted > q0, $"사라짐 {gone} · 알아챔 {w.Hearing.Stats.Quieted - q0} · \"{qn?.Line}\"");
            // 컴퓨터 진동 마이크: 전조 없이 낡은 베어링이 오래 울면 정비 권고
            var m2 = w.Ship.Machines.Where(x => x != m && x.Body.Room is Room r && r.DataLinked && x.Active && x.Powered && x.Omen == null && w.Parts.Of(x).Any(p => p.Kind == ItemKind.Bearing)).OrderBy(x => x.Body.Id).FirstOrDefault();
            if (m2 != null)
            {
                var bp = w.Parts.Of(m2).First(p => p.Kind == ItemKind.Bearing);
                bp.Hours = bp.Life * 1.05f;
                Run(w, SimTime.Hours(4));
                Check("주 컴퓨터 — 진동 마이크로 오래가는 베어링 잡음을 듣고 정비를 권한다", w.Hearing.Stats.Advice >= 1, $"{m2.Name} · 권고 {w.Hearing.Stats.Advice} · {w.Hearing.Stats.Summary()}");
            }
            else Check("주 컴퓨터 — 베어링 달린 설비가 없다", false);
            TallyGest(w);
        }

        // ── 7) 정전 순간 팬 소리가 사라지고 · 가려졌던 물방울 소리가 들린다
        if (Do("7"))
        {
            var w = GDay(seed);
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Galley or RoomType.Workshop or RoomType.Lounge && r.Powered).OrderBy(r => r.Id).First();
            var ppl = Awake(w, 3);
            Gather(w, ppl, room);
            room.Humidity = 0.95f;
            RunE(w, SimTime.Minutes(10), () => room.Humidity = 0.95f);
            int drips0 = w.Hearing.Stats.Drips;
            bool masked = w.Hearing.In(room).Any(h => w.Hearing.Sources[h.Src].Kind == Noise.Drip && h.Masked);
            room.BreakerOff = true; // 정전
            RunE(w, SimTime.Minutes(3), () => { room.Humidity = 0.95f; room.BreakerOff = true; });
            bool fanGone = !w.Hearing.Sources.Any(s => s.Kind == Noise.Fan && s.Room == room.Id);
            var fn = w.Hearing.Notes.FirstOrDefault(n => n.Kind == Noise.Fan);
            var dn = w.Hearing.Notes.LastOrDefault(n => n.Kind == Noise.Drip);
            Check("정전 순간 팬 소리가 사라진다 — 멎은 걸 듣고 정전인 줄 안다", fanGone && w.Hearing.Stats.FanStops >= 1, $"팬 없음 {fanGone} · 알아챔 {w.Hearing.Stats.FanStops} · \"{fn?.Line}\"");
            Check("팬에 가려졌던 물방울 소리가 들린다", masked && drips0 == 0 && w.Hearing.Stats.Drips >= 1, $"가려짐 {masked} · 전 {drips0} · 뒤 {w.Hearing.Stats.Drips} · \"{dn?.Line}\"");
            room.BreakerOff = false;
            TallyGest(w);
        }

        // ── 8) 소리가 문 · 벽 층을 따라 퍼진다 — 닫힌 문 너머는 먹먹해 못 듣고, 열리면 듣고 확인하러 간다
        if (Do("8"))
        {
            var w = GDay(seed);
            Machine? m = null; Door? door = null; Room? next = null;
            foreach (var x in w.Ship.Machines.OrderBy(x => x.Body.Id))
            {
                if (x.Body.Room is not Room r || r.OffLimits || r.Type == RoomType.Corridor || !x.Active || !x.Powered || x.Omen != null) continue;
                var d = w.Ship.Doors.Where(d => !d.IsExternal && !d.Bulkhead && (d.RoomA == r || d.RoomB == r) && (d.RoomA == r ? d.RoomB : d.RoomA) is Room o && !o.Detached && !o.OffLimits).OrderBy(d => d.Id).FirstOrDefault();
                if (d == null) continue;
                m = x; door = d; next = d.RoomA == r ? d.RoomB : d.RoomA; break;
            }
            if (m != null && door != null && next != null)
            {
                var room = m.Body.Room!;
                room.DataLinked = false; // 감지기가 먼저 잡지 않게 (사람이 소리로 듣는 장면)
                foreach (var c in w.Crew) if (c.Room == room && !c.Dead) Stay(w, c, next.Cells.First(x => w.Ship.IsWalkable(x)), Pose.Standing);
                m.Omen = new Omen { Kind = OmenKind.Vibration, Fault = FaultKind.BearingWear, Cause = OmenCause.BearingWear, Since = w.Tick, Due = w.Tick + SimTime.Hours(40) };
                var Ls = Awake(w, 2, c => -c.SkillLevel(Skill.Mechanics) - c.Traits.Diligence);
                var L = Ls[0];
                var lcs = next.Cells.Where(x => w.Ship.IsWalkable(x) && w.Ship.IsOpenFloor(x)).OrderBy(x => (x.Center - door.Cell.Center).Length()).Skip(1).Take(2).ToList();
                for (int i = 0; i < Ls.Count; i++) Stay(w, Ls[i], lcs[i % lcs.Count], Pose.Standing);
                // 닫힌 문
                RunE(w, SimTime.Minutes(5), () => { door.Openness = 0f; door.JammedOpen = false; });
                var hc = w.Hearing.In(next).Where(h => w.Hearing.Sources[h.Src].Kind == Noise.Bearing && w.Hearing.Sources[h.Src].Owner == m.Body.Id).ToList();
                float closedPass = w.Hearing.Pass(room, next);
                bool strangeClosed = w.Hearing.Strange(L, out _);
                Check("닫힌 문 너머 — 먹먹해서 못 듣는다", !strangeClosed && hc.All(h => h.Muffled && h.Level * w.Hearing.Sense(L, Noise.Bearing) < HearingSystem.Audible),
                    $"문 통과 {closedPass:0.00} · 들림 {hc.Count}({string.Join(",", hc.Select(h => $"{h.Level:0.00}{(h.Muffled ? "먹먹" : "")}"))}) · 낯섦 {strangeClosed}");
                // 문을 열어 둔다
                int checks0 = w.React.Stats.Checks;
                bool strangeOpen = false;
                RunE(w, SimTime.Minutes(2), () => { door.Openness = 1f; door.JammedOpen = true; });
                strangeOpen = w.Hearing.Strange(L, out var heardM) && heardM == m;
                float openPass = w.Hearing.Pass(room, next);
                RunE(w, SimTime.Hours(3), () => { door.Openness = 1f; door.JammedOpen = true; });
                var sn = w.React.NotesOf(Stir.Sound).Where(n => Ls.Any(l => l.Id == n.Crew)).ToList();
                Check("열린 문 너머 — 들린다 (문 · 벽 층 따라 · 열린 문이 더 잘 넘는다)", strangeOpen && openPass > closedPass * 3f, $"닫힘 {closedPass:0.00} → 열림 {openPass:0.00} · 낯섦 {strangeOpen}");
                Check("들은 소리로 — 쳐다보고 확인하러 간다", sn.Count >= 1 && w.React.Stats.Checks > checks0, $"반응 {sn.Count} · 확인 {w.React.Stats.Checks - checks0} · 찾음 {w.React.Stats.Found} · \"{sn.Select(n => n.Line).FirstOrDefault(l => l.Length > 0)}\"");
                // 벽 층: 얇은 칸막이가 패널벽보다 소리를 더 넘긴다
                var thin = Materials.Of(Material.Partition).SoundBlock; var panel = Materials.Of(Material.Panel).SoundBlock;
                Check("벽 층 — 칸막이는 패널벽보다 소리를 더 넘긴다 (재질 표)", thin < panel, $"칸막이 {thin} · 패널 {panel}");
                room.DataLinked = true;
            }
            else Check("소리 퍼짐 — 시험할 설비 · 문이 없다", false);
        }

        // ── 9) 버릇: 한 사람당 둘 · 스트레스로 바뀌고 동료가 알아챈다 · 흥얼거림을 따라 부른다
        if (Do("9"))
        {
            var w = GDay(seed);
            var gs = w.Gestures;
            var crew = w.Crew.Where(c => !c.Dead).ToList();
            foreach (var c in crew) gs.Of(c);
            bool two = crew.All(c => gs.Of(c).Tics[0] != gs.Of(c).Tics[1]);
            int pairs = crew.Select(c => $"{(int)gs.Of(c).Tics[0]}:{(int)gs.Of(c).Tics[1]}").Distinct().Count();
            Check("버릇 — 한 사람당 둘 (서로 다르다 · 사람마다 짝이 다르다)", two && pairs >= crew.Count * 0.5f, $"{crew.Count}명 · 짝 {pairs}가지 · 예: {string.Join(" / ", crew.Take(3).Select(c => $"{c.Name} {GestureSystem.TicName(gs.Of(c).Tics[0])}·{GestureSystem.TicName(gs.Of(c).Tics[1])}"))}");
            // 스트레스 → 버릇이 바뀐다 → 동료가 알아챈다
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess && !r.OffLimits).OrderByDescending(r => r.Cells.Count).First();
            var ppl = Awake(w, 2, c => -c.Traits.Sociability);
            var x = ppl[0]; var y = ppl[1];
            x.Affinity[y.Id] = 0.5f; y.Affinity[x.Id] = 0.5f;
            Gather(w, ppl, room);
            foreach (var c in ppl) Stay(w, c, c.Cell, Pose.Sitting);
            var before = gs.Of(x).Tics.ToArray();
            RunE(w, SimTime.Hours(6), () => { x.Needs.Stress = 0.85f; if (x.Room != room || y.Room != room) { Gather(w, ppl, room); foreach (var c in ppl) Stay(w, c, c.Cell, Pose.Sitting); } });
            var xs = gs.Of(x);
            var nt = gs.Notes.FirstOrDefault(n => n.Mien == Mien.Notice && n.Crew == y.Id && n.Other == x.Id);
            Check("스트레스가 쌓이면 버릇이 바뀐다", xs.StressTic != null && !before.Contains(xs.StressTic.Value), $"{x.Name}: {GestureSystem.TicName(before[0])}·{GestureSystem.TicName(before[1])} → {(xs.StressTic is Tic t ? GestureSystem.TicName(t) : "그대로")}");
            Check("동료가 알아챈다 (\"너 요즘 손톱 물더라\")", nt != null && gs.Stats.Noticed >= 1, $"알아챔 {gs.Stats.Noticed}(털어놓음 {gs.Stats.Opened} · 발뺌 {gs.Stats.Denied}) · \"{nt?.Line}\"");
            // 흥얼거림 → 아는 사람이 따라 부른다 · 모르는 사람은 배운다
            var hum = ppl[0];
            var hs = gs.Of(hum);
            hum.Needs.Stress = 0.1f;
            var k = Awake(w, 4).Where(c => c != hum).ToList();
            var know = k[0];
            gs.Of(know).TuneHeard[hs.Tune] = 5;
            Gather(w, new List<CrewMember> { hum, know, k[1] }, room);
            foreach (var c in new[] { hum, know, k[1] }) Stay(w, c, c.Cell, Pose.Sitting);
            hs.Tics[0] = Tic.Hum;
            int sing0 = gs.Stats.SingAlong;
            for (int i = 0; i < 6 && gs.Stats.SingAlong == sing0; i++)
            {
                hs.Doing = Tic.Hum; hs.TicUntil = w.Tick + SimTime.Minutes(5); hs.StressTic = null;
                Run(w, SimTime.Minutes(6));
            }
            var sg = gs.Notes.FirstOrDefault(n => n.Mien == Mien.SingAlong);
            Check("흥얼거리면 아는 사람이 따라 부른다 (모르는 사람은 듣다가 배운다)", gs.Stats.SingAlong > sing0 && gs.Of(k[1]).TuneHeard.ContainsKey(hs.Tune),
                $"따라 부름 {gs.Stats.SingAlong - sing0} · \"{sg?.Line}\" · {k[1].Name} 들은 횟수 {gs.Of(k[1]).TuneHeard.GetValueOrDefault(hs.Tune)}");
            TallyGest(w);
        }

        // ── 10) 끊긴 대화 — 문 앞에서 끊겼다 "아까 하던 얘기…"로 이어 간다
        if (Do("10"))
        {
            var w = GDay(seed);
            var spot = DoorSpot(w);
            if (spot is var (door, a, b, dir))
            {
                var ppl = Awake(w, 2);
                var p = ppl[0]; var q = ppl[1];
                Stay(w, p, a, Pose.Standing);
                var qa = new Cell(a.X - dir.X, a.Y - dir.Y);
                Stay(w, q, qa, Pose.Standing);
                for (int t = 0; t < 75; t++) { p.TalkingTo = q; q.TalkingTo = p; w.Step(); }
                p.TalkingTo = null; q.TalkingTo = null;
                Place(p, new Cell(b.X + dir.X, b.Y + dir.Y)); // 불려 가 문을 나섰다
                GWalk(w, p, new Cell(b.X + dir.X, b.Y + dir.Y), "부름");
                Run(w, 30);
                var gs = w.Gestures;
                Check("대화가 문 앞에서 끊긴다", gs.Stats.Cut >= 1 && gs.Stats.CutAtDoor >= 1, $"끊김 {gs.Stats.Cut} · 문 앞 {gs.Stats.CutAtDoor} · 이야기 \"{gs.Cuts.FirstOrDefault()?.Topic}\"");
                Run(w, SimTime.Minutes(15));
                Stay(w, p, new Cell(a.X - dir.X * 2, a.Y - dir.Y * 2) is var c2 && w.Ship.IsWalkable(c2) && w.Ship.RoomAt(c2) == w.Ship.RoomAt(a) ? c2 : a, Pose.Standing);
                Stay(w, q, qa, Pose.Standing);
                Run(w, SimTime.Minutes(5));
                var rn = gs.Notes.FirstOrDefault(n => n.Mien == Mien.Resume);
                Check("다시 마주치면 \"아까 하던 얘기…\"로 이어 간다", gs.Stats.Resumed >= 1 && rn != null && rn.Line.Contains("아까"), $"이어 감 {gs.Stats.Resumed} · \"{rn?.Line}\"");
                TallyGest(w);
            }
            else Check("끊긴 대화 — 문이 없다", false);
        }

        // ── 11) 뜨거운 잔을 들고 가다 급정지 · 어두운 설비에 등 옮기기
        if (Do("11"))
        {
            var w = GDay(seed);
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Lounge or RoomType.Galley && r.Cells.Count >= 12).OrderByDescending(r => r.Cells.Count).First();
            var cells = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).OrderBy(x => x.X).ThenBy(x => x.Y).ToList();
            var c = Awake(w, 1)[0];
            var gs = w.Gestures;
            for (int i = 0; i < 4 && gs.Stats.Steadied + gs.Stats.Spills == 0; i++)
            {
                Place(c, cells[0]);
                GWalk(w, c, cells[^1], "커피 들고 가기");
                Run(w, 30);
                c.Gait.StartleUntil = w.Tick + 4; c.Gait.StartleAt = c.Position + c.Facing;
                Run(w, 20);
                Run(w, SimTime.Minutes(4));
            }
            var cn = gs.Notes.FirstOrDefault(n => n.Mien is Mien.CupSteady or Mien.CupSpill);
            Check("뜨거운 잔 — 갑자기 멈추면 잔을 내민 채 버티거나 흘린다 (흘리면 바닥이 젖는다)", gs.Stats.Steadied + gs.Stats.Spills >= 1 && (gs.Stats.Spills == 0 || w.Body.Stats != null),
                $"버팀 {gs.Stats.Steadied} · 흘림 {gs.Stats.Spills} · {cn?.Mien} \"{cn?.Line}\"");
            // 어두운 방: 작업등을 일하는 자리 곁으로
            var m = w.Ship.Machines.Where(x => x.Body.Room is Room r && !r.OffLimits && r.Type != RoomType.Corridor && r.Cells.Count >= 10).OrderByDescending(x => x.Body.Room!.Cells.Count).ThenBy(x => x.Body.Id).First();
            var mr = m.Body.Room!;
            var mc = mr.Cells.Where(x => w.Ship.IsOpenFloor(x) && w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - m.Body.Center).Length()).ToList();
            var worker = Awake(w, 2).First(x => x != c);
            mr.LightsOut = true;
            var lamp = w.Portable.Add(PortableKind.WorkLamp, mc.Where(x => (x.Center - m.Body.Center).Length() is > 4.5f and < 8f).DefaultIfEmpty(mc[^1]).First());
            lamp.Stored = false; lamp.On = true;
            ReactSystem.Off = true; // 어둠 반응(손전등 가지러 감)이 일을 끊지 않게 — 이 장면은 일하던 자리의 등만 본다
            WorkAt(w, worker, mc[0], m.Spec.Skill, m.Body.Center);
            var lamp0 = lamp.At;
            RunE(w, SimTime.Minutes(4), () => { mr.LightsOut = true; if (worker.Job?.Label != "정비") WorkAt(w, worker, mc[0], m.Spec.Skill, m.Body.Center); });
            ReactSystem.Off = false;
            var ln = gs.Notes.FirstOrDefault(n => n.Mien == Mien.LampCarry);
            Check("어두운 설비 — 멀리 있던 작업등을 일하는 자리 곁으로 옮긴다", gs.Stats.LampMoves >= 1 && lamp.At != lamp0 && (lamp.At.Center - m.Body.Center).Length() < (lamp0.Center - m.Body.Center).Length(),
                $"옮김 {gs.Stats.LampMoves} · {lamp0} → {lamp.At} · 켜짐 {lamp.Running} · \"{ln?.Line}\"");
            mr.LightsOut = false;
            // 선체 삐걱 (기온이 바뀔 때)
            var hull = w.Ship.LiveRooms.Where(r => w.Body.WallList.Any(wb => wb.Hull && wb.Room == r.Id)).OrderBy(r => r.Id).First();
            float t0 = hull.Air.Temperature;
            int cr0 = w.Hearing.Stats.ByKind[(int)Noise.Creak];
            RunE(w, SimTime.Minutes(40), () => hull.Air.Temperature = t0 - 9f);
            Check("기온이 바뀌면 선체가 삐걱거린다", w.Hearing.Stats.ByKind[(int)Noise.Creak] > cr0, $"{hull.Name} 삐걱 {w.Hearing.Stats.ByKind[(int)Noise.Creak] - cr0}");
            TallyGest(w);
        }

        // ── 12) 하루: 버릇 · 발소리 · 재질 · "이름표 없이 행동과 소리로 안다"를 대신할 측정
        if (Do("12"))
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
            var hs = w.Hearing.Stats; var gs = w.Gestures.Stats;
            Console.WriteLine($"   하루: {hs.Summary()}\n   하루: {gs.Summary()}");
            var stepMats = w.Hearing.Sources.Where(s => s.Kind == Noise.Step).Select(s => s.Sub).Distinct().Count();
            Check("하루 — 버릇이 저절로 보인다 (여러 종류)", gs.Tics >= 20 && gs.ByTic.Count(x => x > 0) >= 6, $"버릇 {gs.Tics} · 종류 {gs.ByTic.Count(x => x > 0)}");
            Check("하루 — 발소리(재질별) · 옆방 발소리로 누군지 안다", hs.ByKind[(int)Noise.Step] > 0 && hs.StepsKnown >= 1, $"발소리 {hs.ByKind[(int)Noise.Step]} · 앎 {hs.StepsKnown} · 지금 재질 {stepMats}가지");
            TallyGest(w);
            Console.WriteLine($"   모은 몸짓 {_mienSeen.Count}가지: {string.Join(" ", _mienSeen)}\n   들은 소리 {_noiseHeard.Count}가지: {string.Join(" ", _noiseHeard)}\n   본 버릇 {_ticSeen.Count}가지");
            if (only == "")
                Check("이름표 없이 — 행동 · 소리 표시 종류가 충분하다 (몸짓 ≥ 14 · 소리 ≥ 10 · 버릇 ≥ 8)", _mienSeen.Count >= 14 && _noiseHeard.Count >= 10 && _ticSeen.Count >= 8,
                    $"몸짓 {_mienSeen.Count} · 소리 {_noiseHeard.Count} · 버릇 {_ticSeen.Count}");
        }

        // ── 13) 결정론 · 성능
        if (Do("13"))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 · 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");
            if (Environment.GetEnvironmentVariable("SHIPSIM_NOPERF") != "1")
            {
                double Day(bool off)
                {
                    HearingSystem.Off = GestureSystem.Off = off;
                    var w = World.CreateDefault(seed, 0, "Cheonma");
                    Run(w, SimTime.Hours(2));
                    HearingSystem.UpdateTicks = GestureSystem.UpdateTicks = 0;
                    var sw = Stopwatch.StartNew();
                    Run(w, SimTime.TicksPerDay);
                    HearingSystem.Off = GestureSystem.Off = false;
                    return sw.Elapsed.TotalSeconds;
                }
                double t0 = Math.Min(Day(true), Day(true)), t1 = Math.Min(Day(false), Day(false));
                double own = (HearingSystem.UpdateTicks + GestureSystem.UpdateTicks) * 1.0 / Stopwatch.Frequency;
                Console.WriteLine($"   성능 (30인 하루): 끔 {t0:0.0}초 · 켬 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%) · 이 시스템 틱 {own * 1000:0}ms");
                Check("성능 · 30인 배 하루 ±10%", t1 < t0 * 1.10, $"{t0:0.0} → {t1:0.0}초");
            }
        }

        Console.WriteLine(_fails == 0 ? "\n몸짓 · 소리 · 버릇: 모두 통과" : $"\n몸짓 · 소리 · 버릇: 실패 {_fails}");
        return _fails;
    }
}
