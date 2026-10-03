using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.3 개인 일: 소리 난 쪽에 가 보기 · 따지기 · 본 대로 말해 주기 · 사과하기 · 물건 찾기(마지막에 둔 곳부터) ·
//   못 끝낸 일 이어 하기(모형 · 독서등 · 늦은 생일 선물 · 대신 서는 근무 · 컵 붙이기) · 설거지 · 사진 걸기 · 벽 사진 보기 · 원래 시각에 회의실로.
// 경보 · 위기면 내려놓고 (하던 만큼은 남는다), 나중에 시간이 나면 이어 한다.

public sealed class InfoActivity : Activity
{
    public override string Id => "info";
    public override string Label => "제 일";

    private sealed class Choice
    {
        public InfoDo Do;
        public InfoIntent? Intent;
        public Todo? Todo;
        public PhotoInfo? Photo;
        public float S;
        public string Why = "";
    }

    private static Choice? Pick(CrewMember c, World w, DistanceField dist)
    {
        if (c.IsChild || !c.CanAct || c.Outside || c.Down || Crisis.Acting(w)) return null;
        var info = w.Info;
        bool shift = OnShift(c, w), bed = Bedtime(c, w);
        bool free = !shift && !bed && c.Needs.Stress < 0.85f;
        Choice? best = null;
        void Consider(InfoDo d, float s, string why, InfoIntent? i = null, Todo? t = null, PhotoInfo? p = null)
        {
            if (s <= 0.05f || best != null && s <= best.S) return;
            best = new Choice { Do = d, S = s, Why = why, Intent = i, Todo = t, Photo = p };
        }
        foreach (var i in info.Intents)
        {
            if (i.Crew != c.Id || i.Until <= w.Tick) continue;
            float s = i.Score;
            switch (i.Do)
            {
                case InfoDo.CheckSound:
                    if (info.Case(i.Thing) is not CupCase k || k.Knows.Contains(c.Id) || k.Swept || !dist.Reachable(i.At)) continue;
                    if (w.Tick - i.Since < SimTime.Minutes(10)) s += 0.2f; // 쨍그랑 — 무슨 일이지? (하던 걸 멈추고 고개를 든다)
                    else if (w.Tick - i.Since > SimTime.Minutes(20)) s -= 0.25f; // 쨍그랑 직후가 아니면 시들하다
                    break;
                case InfoDo.Confront:
                    if (info.Case(i.Thing) is not CupCase k2 || k2.Explained >= 0 || k2.Accused >= 0 || Person(w, i.Other) is not { Dead: false } o || !dist.Reachable(o.Cell)) continue;
                    // 통합7 화가 식기 전에 · 마주치면 그 자리에서 따진다 (0.5 남짓으로는 취미 · 실험에 밀려 세 시간 뒤 같은 방에 앉아서도 말을 안 꺼냈다)
                    if (w.Tick - i.Since < SimTime.Hours(1)) s += 0.15f;
                    s += 0.5f * w.Brain2.Emotions.Get(c, Feeling.Anger);
                    if (o.Room == c.Room && c.Room != null) s += 0.25f;
                    if (shift) s -= 0.15f;
                    if (bed) s -= 0.2f;
                    break;
                case InfoDo.Explain:
                    if (info.Case(i.Thing) is not CupCase k3 || k3.Explained >= 0 || Person(w, i.Other) is not { Dead: false } o2 || !dist.Reachable(o2.Cell)) continue;
                    if (bed) s -= 0.2f;
                    break;
                case InfoDo.Apologize:
                    if (info.Case(i.Thing) is not CupCase k4 || k4.Apologized >= 0 || Person(w, i.Other) is not { Dead: false } o3 || !dist.Reachable(o3.Cell)) continue;
                    if (bed) s -= 0.2f;
                    if (!o3.IsAwake) s -= 0.3f; // 자는 사람은 깨우지 않는다
                    break;
                case InfoDo.Search:
                    if (w.Belongings.Get(i.Thing) is not Belonging b || b.Holder == c.Id || !dist.Reachable(Near(w, i.At, dist) ?? i.At)) continue;
                    s += 0.2f * MathF.Min(1f, (w.Tick - i.Since) / (float)SimTime.Hours(2)); // 통합8 없어진 걸 알고 나면 갈수록 마음에 걸린다 (꾸밈 · 취미에 밀려 네 시간 내내 안 찾았다)
                    if (shift) s -= 0.18f;
                    if (bed) s -= 0.25f;
                    break;
                case InfoDo.HangPhoto:
                    if (info.Photo(i.Thing) is not PhotoInfo p || p.Hung || bed) continue;
                    if (w.Tick - i.Since < SimTime.Hours(3)) s += 0.15f; // 통합8 찍은 날 저녁에 건다 (꾸밈 · 취미에 밀려 여섯 시간을 넘겼다)
                    if (shift) s -= 0.15f;
                    if (w.Tick - i.Since > SimTime.Hours(6)) s -= 0.1f;
                    break;
                case InfoDo.GoMeeting:
                    break;
                default: continue;
            }
            Consider(i.Do, s, i.Why, i);
        }
        if (free)
        {
            // 못 끝낸 일 (시간 날 때)
            foreach (var t in info.TodosOf(c))
            {
                float s = 0.2f + 0.12f * t.Progress;
                if (t.Cut) s += 0.25f; // 하다 만 것이 눈에 밟힌다 — 통합7 0.12로는 취미(0.5 남짓)에 늘 밀려 하루 반 동안 다시 손대지 않았다
                else if (t.LastWork >= 0 && w.Tick - t.LastWork < SimTime.Hours(3)) s -= 0.12f;
                if (Life.Has(c, Habit.Procrastinator)) s -= 0.08f;
                if (Life.Has(c, Habit.Perfectionist) || Life.Has(c, Habit.Tinkerer)) s += 0.04f;
                if (t.From >= 0) s += 0.08f;
                string why = InfoSystem.TodoText(t, w);
                if (t.Kind == TodoKind.Gift)
                {
                    if (Person(w, t.For) is not { Dead: false } to) continue;
                    if (t.Progress >= 1f) { if (!to.IsAwake || !dist.Reachable(to.Cell)) continue; s = 0.42f; why = $"{to.Name}에게 선물 건네기"; }
                }
                else if (t.Kind == TodoKind.Cover)
                {
                    if (Person(w, t.For) is not { Dead: false } r || !r.IsAwake || !dist.Reachable(r.Cell)) continue;
                    float until = SimTime.HoursFromTo(SimTime.HourOfDay(w.Tick), r.Schedule.WorkStart);
                    bool soon = until < 1.5f || OnShift(r, w) && r.Needs.Fatigue > 0.45f;
                    if (!soon || r.ExcusedUntil > w.Tick || c.Needs.Fatigue > 0.5f) continue;
                    s = 0.45f;
                }
                else if (t.Kind == TodoKind.MendCup && w.Belongings.Get(t.Item) is not { Condition: < 0.3f }) { t.Done = true; continue; }
                Consider(InfoDo.Todo, s, why, t: t);
            }
            var (ds, dw) = info.DishWant(c);
            if (ds > 0f && Sink(w, dist) is not null) Consider(InfoDo.Dishes, ds, dw);
            if (info.PhotoToLook(c) is { } lp && dist.Reachable(Near(w, lp.p.Wall, dist) ?? lp.p.Wall)) Consider(InfoDo.LookPhoto, lp.s, lp.why, p: lp.p);
        }
        return best;
    }

    private static CrewMember? Person(World w, int id) => id < 0 ? null : w.Crew.FirstOrDefault(x => x.Id == id);

    private static Cell? Near(World w, Cell at, DistanceField dist)
    {
        if (w.Ship.IsWalkable(at) && dist.Reachable(at)) return at;
        Cell? best = null; int bd = int.MaxValue;
        foreach (var d in Cell.Dirs8)
        {
            var x = at + d;
            if (!w.Ship.IsWalkable(x) || !dist.Reachable(x)) continue;
            int g = dist.Get(x);
            if (g < bd) { bd = g; best = x; }
        }
        return best;
    }

    private static Furniture? Sink(World w, DistanceField dist)
    {
        Furniture? best = null; int bd = int.MaxValue;
        foreach (var r in w.Ship.RoomsOf(RoomType.Mess))
        {
            if (r.OffLimits || r.Leaking) continue;
            foreach (var f in r.Furniture)
            {
                if (f.Type is not (FurnitureType.DishWasher or FurnitureType.MealDispenser or FurnitureType.Stove) || f.UseSpots.Count == 0 || !dist.Reachable(f.UseSpots[0])) continue;
                int g = dist.Get(f.UseSpots[0]) - (f.Type == FurnitureType.DishWasher ? 40 : 0);
                if (g < bd) { bd = g; best = f; }
            }
        }
        return best;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var p = Pick(c, w, dist);
        return p == null ? (0f, "—") : (p.S, p.Why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var p = Pick(c, w, dist);
        if (p == null) return null;
        var info = w.Info;
        var toils = Plans.DropOff(c, w, dist);
        switch (p.Do)
        {
            case InfoDo.CheckSound:
            {
                var i = p.Intent!;
                var k = info.Case(i.Thing)!;
                var spot = Near(w, i.At, dist);
                if (spot is not Cell s) { info.Drop(i); return null; }
                toils.Add(new GotoToil(s));
                toils.Add(new WaitToil(SimTime.Minutes(1), Pose.Standing, i.At.Center));
                toils.Add(new DoToil((cm, world) => { world.Info.Checked(cm, k); return true; }));
                return new Job(this, "소리 난 쪽 확인", toils) { LogText = $"쨍그랑 소리가 난 {Ko.EuRo(w.Ship.RoomAt(i.At)?.Name ?? "쪽")} 가 본다", TargetRoom = w.Ship.RoomAt(i.At), InterruptMargin = 0.2f, AlwaysLog = true };
            }
            case InfoDo.Confront:
            case InfoDo.Explain:
            case InfoDo.Apologize:
            {
                var i = p.Intent!;
                var k = info.Case(i.Thing)!;
                var o = Person(w, i.Other)!;
                Approach(w, toils, o);
                var d = p.Do;
                toils.Add(new DoToil((cm, world) =>
                {
                    if (o.Dead || (o.Position - cm.Position).LengthSquared() > 9f) return false;
                    Locomotion.Face(cm, o.Position);
                    if (d == InfoDo.Confront) world.Info.Confront(cm, o, k);
                    else if (d == InfoDo.Explain) world.Info.Explain(cm, o, k, true);
                    else world.Info.Apologize(cm, o, k);
                    world.Info.Drop(i);
                    return true;
                }));
                toils.Add(new WaitToil(SimTime.Minutes(2), Pose.Standing, null) { EveryTick = (cm, _) => cm.Facing = Vector2.Normalize(o.Position - cm.Position + new Vector2(0.0001f, 0f)) });
                string log = d switch
                {
                    InfoDo.Confront => $"{o.Name}에게 컵 일을 따지러 간다",
                    InfoDo.Explain => $"{o.Name}에게 컵이 어떻게 깨졌는지 말해 주러 간다",
                    _ => $"{o.Name}에게 사과하러 간다",
                };
                return new Job(this, d == InfoDo.Confront ? "따지기" : d == InfoDo.Explain ? "본 대로 말하기" : "사과", toils) { LogText = log, TargetRoom = o.Room, InterruptMargin = 0.2f, AlwaysLog = true };
            }
            case InfoDo.Search: return SearchJob(c, w, dist, p.Intent!, toils);
            case InfoDo.Todo: return TodoJob(c, w, dist, p.Todo!, toils);
            case InfoDo.Dishes:
            {
                var sink = Sink(w, dist);
                if (sink == null) return null;
                bool machine = sink.Type == FurnitureType.DishWasher && sink.Room.Powered && sink.Machine is { } m && m.Faults.Count == 0;
                int n = info.Dirty;
                toils.Add(new GotoToil(sink.UseSpots[0]));
                toils.Add(new WaitToil(SimTime.Minutes(machine ? 7 : 16), Pose.Working, sink.Center, minTicks: SimTime.Minutes(machine ? 5 : 12)));
                toils.Add(new DoToil((cm, world) => { world.Info.DidDishes(cm, Math.Max(n, world.Info.Dirty)); return true; }));
                return new Job(this, "설거지", toils) { LogText = machine ? "쌓인 그릇을 식기세척기에 넣는다" : "쌓인 그릇을 설거지한다", TargetRoom = sink.Room, InterruptMargin = 0.2f, AlwaysLog = true };
            }
            case InfoDo.HangPhoto:
            {
                var i = p.Intent!;
                var ph = p.Photo ?? info.Photo(i.Thing)!;
                var room = ph.Scene == PhotoScene.Window && c.Bed?.Room is Room q ? q
                    : w.Ship.RoomsOf(RoomType.Lounge).FirstOrDefault(r => !r.OffLimits) ?? w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault(r => !r.OffLimits) ?? c.Room;
                if (room == null || info.WallSpot(room, room.Center) is not { } ws || !dist.Reachable(ws.at)) { info.Drop(i); return null; }
                var (at, dir) = ws;
                toils.Add(new GotoToil(at));
                toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Working, (at + dir).Center));
                toils.Add(new DoToil((cm, world) => { world.Info.HangNow(cm, ph, at, dir); world.Info.Drop(i); return true; }));
                return new Job(this, "사진 걸기", toils) { LogText = $"{ph.Caption} 사진을 {room.Name} 벽에 건다", TargetRoom = room, InterruptMargin = 0.15f, AlwaysLog = true };
            }
            case InfoDo.LookPhoto:
            {
                var ph = p.Photo!;
                var spot = Near(w, ph.Wall, dist) ?? ph.Wall;
                toils.Add(new GotoToil(spot));
                toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Standing, (ph.Wall + ph.WallDir).Center));
                toils.Add(new DoToil((cm, world) => { world.Info.LookAt(cm, ph); return true; }));
                return new Job(this, "사진 보기", toils) { LogText = $"벽에 걸린 {ph.Caption} 사진 앞에 선다", TargetRoom = w.Ship.RoomAt(ph.Wall), InterruptMargin = 0.1f, AlwaysLog = true };
            }
            case InfoDo.GoMeeting:
            {
                var i = p.Intent!;
                var room = w.Ship.RoomsOf(RoomType.MeetingRoom).FirstOrDefault(r => !r.OffLimits) ?? w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault(r => !r.OffLimits);
                var spot = room?.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x)).OrderBy(x => (x.Center - room.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
                if (spot is not Cell s) { info.Drop(i); return null; }
                toils.Add(new GotoToil(s));
                toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Standing, room!.Center) { DoneWhen = (cm, world) => world.Meetings.Session != null || world.Meetings.Gathering });
                toils.Add(new DoToil((cm, world) => { world.Info.Drop(i); if (world.Meetings.Session == null && !world.Meetings.Gathering) world.Info.EmptyMeeting(cm); return true; }));
                return new Job(this, "회의", toils) { LogText = $"회의하러 {Ko.EuRo(room.Name)} 간다", TargetRoom = room, InterruptMargin = 0.2f, AlwaysLog = true };
            }
        }
        return null;
    }

    /// <summary>사람 곁으로 (움직이는 사람이라 출발할 때 다시 본다).</summary>
    private static void Approach(World w, List<Toil> toils, CrewMember o)
    {
        toils.Add(new GotoToilLate(cm => Cell.Dirs8.Select(d => o.Cell + d).Where(x => w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - cm.Position).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).Cast<Cell?>().FirstOrDefault()));
        toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Standing, null)
        {
            DoneWhen = (cm, world) => (o.Position - cm.Position).LengthSquared() <= 6f || o.Dead,
        });
        toils.Add(new ChaseToil(o, SimTime.Minutes(20))); // 통합7 그새 자리를 옮겼으면 따라간다 (한 번 다시 가는 것으로는 서로 상대가 있던 자리로 엇갈려 세 시간을 헛걸음했다)
    }

    /// <summary>통합7 움직이는 사람을 따라잡는다: 1분마다 그 사람의 지금 곁 칸으로 길을 다시 잡는다 (서로 찾아가도 가운데서 만난다).</summary>
    private sealed class ChaseToil : Toil
    {
        private readonly CrewMember _o;
        private readonly int _limit;
        private int _t;
        public ChaseToil(CrewMember o, int limit) { _o = o; _limit = limit; }

        private bool Aim(CrewMember c, World w)
        {
            var o = _o;
            var cell = Cell.Dirs8.Select(d => o.Cell + d).Where(x => w.Ship.IsWalkable(x)).OrderBy(x => (x.Center - c.Position).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).Cast<Cell?>().FirstOrDefault();
            if (cell is not Cell to) return false;
            bool ok = Locomotion.SetDestination(c, w, to);
            if (ok && c.Path != null && c.Path.Count > 0) c.Pose = Pose.Walking;
            return ok;
        }

        public override void Begin(CrewMember c, World w) { _t = 0; if ((_o.Position - c.Position).LengthSquared() > 6f) Aim(c, w); }

        public override ToilStatus Tick(CrewMember c, World w)
        {
            if (_o.Dead) return ToilStatus.Failed;
            if ((_o.Position - c.Position).LengthSquared() <= 6f) { c.Path = null; if (c.Pose == Pose.Walking) c.Pose = Pose.Standing; return ToilStatus.Succeeded; }
            if (++_t > _limit) return ToilStatus.Failed;
            if (_t % SimTime.Minutes(1) == 0 || c.Path == null || c.PathBlocked) { if (!Aim(c, w)) return ToilStatus.Failed; }
            Locomotion.Step(c, w);
            return ToilStatus.Running;
        }

        public override void End(CrewMember c, World w)
        {
            c.Path = null;
            c.Destination = null;
            if (c.Pose == Pose.Walking) c.Pose = Pose.Standing;
        }
    }

    /// <summary>물건 찾기: 마지막에 둔 곳 → 그 방 둘레 → 사물함 → 메신저에 묻기.</summary>
    private Job? SearchJob(CrewMember c, World w, DistanceField dist, InfoIntent i, List<Toil> toils)
    {
        var info = w.Info;
        if (w.Belongings.Get(i.Thing) is not Belonging b) { info.Drop(i); return null; }
        var last = i.At;
        var first = Near(w, last, dist);
        if (first is not Cell f0) { info.Drop(i); return null; }
        var room = w.Ship.RoomAt(last);
        bool found = false;
        bool lastSeen = Near2(last, info.Believed(b));
        if (lastSeen) info.Stats.LastSeenFirst++;
        info.Stats.Searches++;
        bool Here(CrewMember cm, World world, float r)
        {
            if (b.Holder >= 0 || b.At is not Cell at) return false;
            return world.Ship.RoomAt(at) == cm.Room && (at.Center - cm.Position).LengthSquared() <= r * r;
        }
        toils.Add(new GotoToil(f0));
        toils.Add(new WaitToil(SimTime.Minutes(1), Pose.Standing, last.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (Here(cm, world, 1.8f)) { found = true; world.Info.Found(cm, b, lastSeen ? "마지막에 둔 곳에 그대로 있었다" : "들은 곳에 있었다", lastSeen); }
            else cm.Say(world, Persona.Say(cm, lastSeen ? $"분명 여기 뒀는데…" : "여기 있다더니…"));
            return true;
        }));
        // 방 안 둘레
        var around = room?.Cells.Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && (x.Center - last.Center).LengthSquared() is > 3f and < 20f)
            .OrderBy(x => (x.Center - last.Center).LengthSquared()).ThenBy(x => x.X * 1000 + x.Y).Take(1).ToList() ?? new List<Cell>();
        foreach (var a in around)
        {
            toils.Add(new GotoToil(a, _ => !found));
            toils.Add(new WaitToil(SimTime.Minutes(1), Pose.Standing, null) { DoneWhen = (_, _) => found });
            toils.Add(new DoToil((cm, world) =>
            {
                if (!found && Here(cm, world, 6f)) { found = true; world.Info.Found(cm, b, "근처에 밀려나 있었다", false); }
                return true;
            }));
        }
        // 사물함
        toils.Add(new GotoToilLate(cm => found ? null : w.Belongings.Home(b)));
        toils.Add(new DoToil((cm, world) =>
        {
            if (found) return true;
            if (b.At == null && b.Holder < 0 && b.BorrowedBy < 0) { found = true; world.Info.FoundTidied(cm, b); }
            else world.Info.AskWhere(cm, b);
            world.Info.Drop(i);
            return true;
        }));
        return new Job(this, "물건 찾기", toils)
        {
            LogText = lastSeen ? $"마지막에 둔 {room?.Name ?? "곳"}부터 {Ko.EulReul(b.Name)} 찾아본다" : $"들은 대로 {Ko.EuRo(room?.Name ?? "그곳")} {Ko.EulReul(b.Name)} 찾으러 간다",
            TargetRoom = room, InterruptMargin = 0.2f, AlwaysLog = true,
            OnFinished = (cm, world, st) => { if (found) world.Info.Drop(i); },
        };
    }

    /// <summary>그 일을 한 번 이어 하는 작업 (시험 · 다른 시스템이 부를 때).</summary>
    public Job? PlanTodo(CrewMember c, World w, DistanceField dist, Todo t) => TodoJob(c, w, dist, t, Plans.DropOff(c, w, dist));

    private static bool Near2(Cell a, Cell b) => Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;

    /// <summary>못 끝낸 일 한 번 (끊겨도 한 만큼은 남는다).</summary>
    private Job? TodoJob(CrewMember c, World w, DistanceField dist, Todo t, List<Toil> toils)
    {
        var info = w.Info;
        if (t.Kind == TodoKind.Gift && t.Progress >= 1f || t.Kind == TodoKind.Cover)
        {
            var to = Person(w, t.For);
            if (to == null) return null;
            Approach(w, toils, to);
            toils.Add(new DoToil((cm, world) =>
            {
                if (to.Dead || (to.Position - cm.Position).LengthSquared() > 9f) return false;
                Locomotion.Face(cm, to.Position);
                if (t.Kind == TodoKind.Gift) world.Info.Deliver(cm, t, to);
                else world.Info.CoverFor(cm, t, to);
                return true;
            }));
            return new Job(this, t.Kind == TodoKind.Gift ? "선물 건네기" : "대신 근무 제안", toils)
            {
                LogText = t.Kind == TodoKind.Gift ? $"{to.Name}에게 늦은 생일 선물을 건네러 간다" : $"{to.Name}에게 오늘 근무는 내가 서겠다고 말하러 간다",
                TargetRoom = to.Room, InterruptMargin = 0.2f, AlwaysLog = true,
            };
        }
        // 어디서: 독서등 · 컵은 제 침대 곁 · 모형 · 선물은 작업대(없으면 침대 곁)
        Cell? spot = null;
        Vector2 face = c.Position;
        Pose pose = Pose.Sitting;
        if (t.Kind is TodoKind.Model or TodoKind.Gift)
        {
            var bench = w.Ship.FurnitureOf(FurnitureType.Workbench).Where(f => f.UseSpots.Count > 0 && f.ReservedBy == null && dist.Reachable(f.UseSpots[0]) && !w.IsSpotTaken(f.UseSpots[0], c) && !f.Room.OffLimits && f.Room.Type != RoomType.Corridor)
                .OrderBy(f => dist.Get(f.UseSpots[0])).ThenBy(f => f.Id).FirstOrDefault();
            if (bench != null && (c.Bed == null || dist.Get(bench.UseSpots[0]) < dist.Get(c.Bed.UseSpots.Count > 0 ? c.Bed.UseSpots[0] : bench.UseSpots[0]) + 30)) { spot = bench.UseSpots[0]; face = bench.Center; pose = Pose.Working; }
        }
        if (spot == null && c.Bed is Furniture bed && bed.UseSpots.Count > 0 && dist.Reachable(bed.UseSpots[0])) { spot = bed.UseSpots[0]; face = bed.Center; pose = t.Kind == TodoKind.Lamp ? Pose.Working : Pose.Sitting; }
        if (spot is not Cell s) return null;
        long start = -1;
        toils.Add(new GotoToil(s));
        toils.Add(new WaitToil(SimTime.Minutes(t.Kind == TodoKind.Lamp ? 40 : 55), pose, face, minTicks: SimTime.Minutes(20))
        {
            EveryTick = (cm, world) => { if (start < 0) start = world.Tick; },
            DoneWhen = (cm, world) => start >= 0 && t.Progress + (world.Tick - start) / (float)SimTime.TicksPerHour * 0.3f >= 1f,
        });
        bool resumed = t.Sessions > 0;
        return new Job(this, InfoSystem.TodoText(t, w), toils)
        {
            LogText = resumed ? $"{InfoSystem.TodoText(t, w)} — 하던 걸 이어서 한다" : $"{InfoSystem.TodoText(t, w)} — 오랜만에 손을 댄다",
            TargetRoom = w.Ship.RoomAt(s), InterruptMargin = 0.15f, AlwaysLog = true,
            // 경보에 끊겨도 한 만큼은 남는다
            OnFinished = (cm, world, st) => { t.Cut = st != ToilStatus.Succeeded && start >= 0; if (start >= 0) world.Info.WorkOn(cm, t, (world.Tick - start) / (float)SimTime.TicksPerHour); },
        };
    }
}

public sealed partial class InfoSystem
{
    /// <summary>소리 난 쪽에 가 봤다: 조각이 있으면 (처음이면 발견자).</summary>
    internal void Checked(CrewMember c, CupCase k)
    {
        var w = _w;
        Stats.Checked++;
        Intents.RemoveAll(i => i.Crew == c.Id && i.Do == InfoDo.CheckSound && i.Thing == k.Id);
        if (k.Knows.Contains(c.Id)) return;
        k.Knows.Add(c.Id);
        if (k.Swept) { c.Say(w, Persona.Say(c, "누가 벌써 치웠네")); return; }
        if (c.Id == k.Owner) { OwnerLearns(k, BeliefSource.Seen, -1); return; }
        w.Brain2.Beliefs.Learn(c, Topic.Thing, k.Cup, 2, BeliefSource.Seen, 0.9f);
        if (k.Finder < 0) Find(k, c);
    }
}
