using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.25 여러 갈래 해법 — 손으로 하는 일 (Activity · Toil). 고른 갈래마다: 물건 가져오기 → 그 자리로 → 일 → 결과(규칙) · 부작용 · 그림 · 나중 일.

/// <summary>사람을 실어 옮길 때 빠르기 (들것 · 짐수레 · 무중력 밀기) — Locomotion.Speed 가 읽는다.</summary>
public sealed class CarryGotoToil : Toil
{
    private readonly GotoToil _inner;
    public float Boost { get; }
    public CarryGotoToil(Cell target, float boost) { _inner = new GotoToil(target); Boost = boost; }
    public override void Begin(CrewMember c, World w) => _inner.Begin(c, w);
    public override ToilStatus Tick(CrewMember c, World w) => _inner.Tick(c, w);
    public override void End(CrewMember c, World w) => _inner.End(c, w);
    public static float Mul(CrewMember c) => c.Job?.Current is CarryGotoToil t ? t.Boost : 1f;
}

public sealed class WayActivity : Activity
{
    public override string Id => "ways";
    public override string Label => "임시방편";

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        var ways = w.Ways;
        if (WaysSystem.Off) return (0f, "—");
        if (ways.TryOf(c) is WayTry t) return (t.Helper ? 2.3f : 2.4f, t.Why.Length > 0 ? t.Why : t.Way.Name);
        if (c.Down || Crisis.Acting(w) || c.IsChild || ways.Follows.Count == 0) return (0f, "—");
        if (ways.FollowFor(c) is WayFollow f) return (0.42f + (c.Stations.Contains(f.RoomId >= 0 && f.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[f.RoomId].Kind : RoomType.Storage) ? 0.1f : 0f), f.Text);
        return (0f, "—");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var ways = w.Ways;
        if (ways.TryOf(c) is WayTry t)
        {
            var job = WaysWork.Build(this, c, w, dist, t, out string? why);
            if (job == null) ways.Finish(t, false, why ?? "그럴 수가 없었다", t.Helper ? null : c);
            return job;
        }
        if (ways.FollowFor(c) is WayFollow f) return WaysWork.BuildFollow(this, c, w, dist, f);
        return null;
    }
}

public static class WaysWork
{
    private static Job Wrap(Activity a, WayTry t, World w, string label, List<Toil> toils, Room? room, string log) =>
        new(a, label, toils) { Urgent = true, InterruptMargin = 0.6f, LogText = log, LogKind = LogKind.Work, AlwaysLog = true, TargetRoom = room };

    private static Cell? Spot(World w, DistanceField dist, CrewMember c, Cell target, Room? inRoom = null, Room? notRoom = null)
    {
        Cell? best = null;
        int bd = int.MaxValue;
        foreach (var d in Cell.Dirs8.Append(new Cell(0, 0)))
        {
            var s = target + d;
            if (!w.Ship.IsWalkable(s) || w.Ship.DoorAt(s) != null && d != new Cell(0, 0)) continue;
            var r = w.Ship.RoomAt(s);
            if (inRoom != null && r != inRoom || notRoom != null && r == notRoom) continue;
            int dd = dist.Get(s);
            if (dd < 0) continue;
            if (w.IsSpotTaken(s, c)) dd += 200;
            if (dd < bd) { bd = dd; best = s; }
        }
        return best;
    }

    private static Cell? FurnSpot(World w, DistanceField dist, CrewMember c, Furniture f) => Plans.WorkSpot(f, w, dist, c);

    private static Cell? NearestFire(World w, Room room, CrewMember c)
    {
        Cell? best = null; float bd = float.MaxValue;
        foreach (var (cell, _) in w.Fire.Fires)
        {
            if (w.Ship.RoomAt(cell) != room) continue;
            float dx = cell.X - c.Cell.X, dy = cell.Y - c.Cell.Y, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = cell; }
        }
        return best;
    }

    /// <summary>창고에서 가져오기 (실링폼 · 케이블 · 구급 키트 · 금속판).</summary>
    private static bool Fetch(CrewMember c, World w, DistanceField dist, ItemKind kind, int n, List<Toil> toils)
    {
        if (c.Carrying is ItemStack held && held.Kind == kind && held.Count >= n) return true;
        var (box, spot) = Plans.NearestContainer(w, dist, c, f => f.Storage is Inventory inv && inv.Count(kind) >= n);
        if (box == null) return false;
        toils.Add(new GotoToil(spot));
        toils.Add(new TakeToil(box, kind, n));
        return true;
    }

    private static void Use(CrewMember c, ItemKind kind, int n)
    {
        if (c.Carrying is not ItemStack s || s.Kind != kind) return;
        c.Carrying = s.Count > n ? new ItemStack(kind, s.Count - n) : null;
    }

    private static Toil Work(float minutes, Skill skill, Cell face, Func<CrewMember, World, bool>? keep = null) =>
        new WorkToil(MathF.Max(1f, minutes) / 60f, skill, face.Center) { CanContinue = keep };

    /// <summary>근처 물건을 집어 든다 (못 찾으면 실패).</summary>
    private static bool TakeThing(World w, DistanceField dist, CrewMember c, int articleId, List<Toil> toils)
    {
        if (w.Matter.Get(articleId) is not Article a) return false;
        if (a.CarriedBy == c.Id) return true;
        var spot = Spot(w, dist, c, a.At);
        if (spot is not Cell s) return false;
        toils.Add(new GotoToil(s));
        toils.Add(new DoToil((cm, world) => { if (a.CarriedBy >= 0 || !world.Matter.Things.Contains(a)) return false; world.Matter.Carry(a, cm); return true; }));
        return true;
    }

    public static Job? Build(Activity act, CrewMember c, World w, DistanceField dist, WayTry t, out string? why)
    {
        why = null;
        var ways = w.Ways;
        var k = ways.Case(t.CaseId);
        if (k == null || !k.Open && !t.Helper) { why = "벌써 풀렸다"; return null; }
        var room = ways.RoomOf(k);
        var way = t.Way;
        var toils = Plans.DropOff(c, w, dist);
        float speed = ways.SpeedMul(c, way);
        float minutes = way.Minutes * speed;
        int furnId = ways.FurnOf(t), artId = ways.ArticleOf(t);
        var furn = furnId >= 0 ? ways.FurnById(furnId) : null;
        if (t.Started < 0) t.Started = w.Tick;
        t.State = 1;
        string log = $"{way.Name} — {t.Why}";
        switch (way.Fx)
        {
            // ── 불 ──
            case WayFx.Seal or WayFx.LetBurn:
            {
                if (room == null) return null;
                // 가장 가까운 문 · 안에 있으면 밖으로 나가서 닫는다
                Door? door = null; int bd = int.MaxValue;
                foreach (var d in room.Doors) { if (d.IsExternal || d.Removed || d.JammedOpen) continue; int dd = Math.Abs(d.Cell.X - c.Cell.X) + Math.Abs(d.Cell.Y - c.Cell.Y); if (dd < bd) { bd = dd; door = d; } }
                if (door == null) { why = "닫을 문이 없다"; return null; }
                var spot = Spot(w, dist, c, door.Cell, notRoom: room);
                if (spot is not Cell s) { why = "문까지 못 간다"; return null; }
                toils.Add(new GotoToil(s));
                toils.Add(Work(minutes, Skill.Mechanics, door.Cell));
                toils.Add(new DoToil((cm, world) =>
                {
                    world.Ways.Seal(room, t, way.Fx == WayFx.LetBurn);
                    world.Ways.AddMark(new WayMark { Look = way.Fx == WayFx.LetBurn ? WayLook.Burnt : WayLook.Seal, At = door.Cell, RoomId = room.Id, DoorId = door.Id, Since = world.Tick, Try = t.Id, Who = cm.Id, Dir = s - door.Cell });
                    world.Log.Add(world.Tick, LogKind.Work, way.Fx == WayFx.LetBurn ? $"{room.Name} 문을 잠갔다 — 타게 두고 가둔다" : $"{room.Name} 문을 닫고 틈을 막았다 — 숨을 끊는다", cm.Id);
                    cm.Say(world, way.Fx == WayFx.LetBurn ? "들어가지 마 — 다 타면 꺼진다" : "이제 안에서 숨이 막힐 거다");
                    return true;
                }));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            case WayFx.Douse or WayFx.CutDouse:
            {
                if (room == null) return null;
                bool fromFurn = furn != null && furn.Type is FurnitureType.GrowBed or FurnitureType.WaterRecycler;
                if (fromFurn)
                {
                    if (FurnSpot(w, dist, c, furn!) is not Cell fs) { why = "물탱크에 못 간다"; return null; }
                    toils.Add(new GotoToil(fs));
                    toils.Add(Work(2f, Skill.Mechanics, furn!.Cells[0]));
                }
                else if (artId < 0 || !TakeThing(w, dist, c, artId, toils)) { why = "물이 없다"; return null; }
                if (way.Fx == WayFx.CutDouse)
                {
                    var bs = Spot(w, dist, c, room.Doors.Where(d => !d.IsExternal).Select(d => d.Cell).DefaultIfEmpty(room.Cells[0]).First(), notRoom: room) ?? Spot(w, dist, c, room.Cells[0]);
                    if (bs is Cell b) { toils.Add(new GotoToil(b)); toils.Add(Work(2f, Skill.Electrical, b)); toils.Add(new DoToil((cm, world) => { world.Moisture.Isolate(room, cm); world.Ways.AddFollow(new WayFollow { Id = world.Ways.NextFollow(), WayId = way.Id, Text = $"{room.Name} 분전함 다시 올리기", Kind = 2, RoomId = room.Id, At = b, Since = world.Tick }); return true; })); }
                }
                var aim0 = NearestFire(w, room, c) ?? k.At;
                if (Spot(w, dist, c, aim0, inRoom: room) is not Cell ds) { why = "불까지 못 간다"; return null; }
                toils.Add(new GotoToil(ds));
                toils.Add(Work(MathF.Max(1f, minutes * 0.5f), Skill.Mechanics, aim0));
                toils.Add(new DoToil((cm, world) => Douse(world, cm, t, room, fromFurn ? furn : null, artId)));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            case WayFx.Smother:
            {
                if (room == null) return null;
                if (artId >= 0) { if (!TakeThing(w, dist, c, artId, toils)) { why = "천이 없다"; return null; } }
                else if (furn != null && FurnSpot(w, dist, c, furn) is Cell fs) { toils.Add(new GotoToil(fs)); toils.Add(Work(1f, Skill.Mechanics, furn.Cells[0])); }
                else { why = "덮을 게 없다"; return null; }
                var aim0 = NearestFire(w, room, c) ?? k.At;
                if (Spot(w, dist, c, aim0, inRoom: room) is not Cell ss) { why = "불까지 못 간다"; return null; }
                toils.Add(new GotoToil(ss));
                toils.Add(Work(minutes, Skill.Mechanics, aim0));
                toils.Add(new DoToil((cm, world) => Smother(world, cm, t, room, furn, artId)));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            case WayFx.Eject:
            {
                if (room == null || artId < 0 || !TakeThing(w, dist, c, artId, toils)) { why = "들 게 없다"; return null; }
                var outDoor = room.Doors.Where(d => !d.Removed).OrderBy(d => Math.Abs(d.Cell.X - c.Cell.X) + Math.Abs(d.Cell.Y - c.Cell.Y)).FirstOrDefault();
                var drop = outDoor == null ? null : Spot(w, dist, c, outDoor.Cell, notRoom: room);
                if (drop is not Cell dr) { why = "내갈 데가 없다"; return null; }
                toils.Add(new DoToil((cm, world) => { if (world.Matter.Get(artId) is Article a) world.Fire.Suppress(a.At, 0.8f, 1f); NeedsSystem.AddInjury(cm.Vitals, 0.04f, "손 화상"); return true; }));
                toils.Add(new GotoToil(dr));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (world.Matter.Get(artId) is Article a) { world.Matter.Place(a, dr); a.Char = 1f; a.Ruined = true; a.Smolder = false; }
                    world.Ways.AddMark(new WayMark { Look = WayLook.Eject, At = dr, RoomId = room.Id, Since = world.Tick, Try = t.Id, Who = cm.Id });
                    bool out1 = world.Fire.CountIn(room) == 0;
                    world.Ways.Finish(t, out1, out1 ? $"타던 {t.Thing}을 문밖에 내던졌다" : "들고 나왔는데 불이 남았다", cm);
                    return true;
                }));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            // ── 파공 ──
            case WayFx.Plug or WayFx.Freeze or WayFx.Brace:
            {
                if (room == null || w.Ship.WallAt(k.At) is not WallState wall) return null;
                if (way.Fx != WayFx.Brace)
                {
                    if (artId >= 0) { if (!TakeThing(w, dist, c, artId, toils)) { why = "가져올 게 없다"; return null; } }
                    else if (furn != null && FurnSpot(w, dist, c, furn) is Cell fs) { toils.Add(new GotoToil(fs)); toils.Add(Work(way.Fx == WayFx.Freeze ? 3f : 2f, Skill.Mechanics, furn.Cells[0])); }
                    else { why = "댈 게 없다"; return null; }
                }
                if (Spot(w, dist, c, k.At, inRoom: room) is not Cell ps) { why = "구멍까지 못 간다"; return null; }
                toils.Add(new GotoToil(ps));
                toils.Add(Work(minutes, way.Fx == WayFx.Freeze ? Skill.Engineering : Skill.Mechanics, k.At));
                toils.Add(new DoToil((cm, world) => Plug(world, cm, t, room, wall, k.At, furn, artId, ps)));
                if (way.Fx == WayFx.Brace) toils.Add(new WaitToil(SimTime.Minutes(10), Pose.Working, k.At.Center));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            // ── 갇힘 ──
            case WayFx.Crawl:
            {
                if (room == null || ways.CrawlOut(room) is not Cell exit) { why = "통로가 막혔다"; return null; }
                if (!dist.Reachable(exit)) { why = "통로로 못 들어간다"; return null; }
                toils.Add(new GotoToil(exit));
                toils.Add(new DoToil((cm, world) =>
                {
                    bool outOk = cm.Room != room;
                    var hatch = world.Body.WallList.FirstOrDefault(wb => wb.Crawl && (wb.CrawlA == room.Id || wb.CrawlB == room.Id));
                    if (hatch != null) world.Ways.AddMark(new WayMark { Look = WayLook.CrawlHatch, At = hatch.Cell, RoomId = room.Id, Since = world.Tick, Try = t.Id, Who = cm.Id, Until = world.Tick + SimTime.Hours(12) });
                    world.Ways.Finish(t, outOk, outOk ? "벽 속 통로로 기어 나왔다" : "통로 끝이 막혀 있었다", cm);
                    if (outOk) world.History.Add(world, HistoryKind.Response, $"{room.Name}에 갇힌 {Ko.IGa(cm.Name)} 정비 통로로 기어 나왔다", room, new[] { cm });
                    return true;
                }));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            case WayFx.Pry or WayFx.Cut or WayFx.Bypass or WayFx.Blow:
            {
                if (room == null || k.DoorId < 0 || k.DoorId >= w.Ship.Doors.Count) return null;
                var door = w.Ship.Doors[k.DoorId];
                if (!WaysSystem.Stuck(door)) { why = "문이 벌써 열렸다"; return null; }
                // 연장: 공구함 · 작업대 · 케이블
                if (artId >= 0 && w.Matter.Get(artId) is Article tb)
                {
                    if (Spot(w, dist, c, tb.At) is Cell ts) { toils.Add(new GotoToil(ts)); toils.Add(Work(1f, Skill.Mechanics, tb.At)); }
                    else { why = "공구함까지 못 간다"; return null; }
                }
                else if (furn != null && FurnSpot(w, dist, c, furn) is Cell fs) { toils.Add(new GotoToil(fs)); toils.Add(Work(1.5f, Skill.Mechanics, furn.Cells[0])); }
                if (way.Item is ItemKind it && !Fetch(c, w, dist, it, way.ItemCount, toils)) { why = $"{ItemKinds.Name(it)} 없음"; return null; }
                bool subject = c.Id == k.CrewId;
                var spot = subject ? Spot(w, dist, c, door.Cell, inRoom: room) : Spot(w, dist, c, door.Cell, notRoom: room);
                if (spot is not Cell s) { why = "문까지 못 간다"; return null; }
                toils.Add(new GotoToil(s));
                float m = way.Fx == WayFx.Pry ? minutes * (1.25f - 0.5f * c.Fitness) : minutes;
                toils.Add(Work(m, way.Fx == WayFx.Bypass ? Skill.Electrical : Skill.Mechanics, door.Cell, (cm, world) => WaysSystem.Stuck(door)));
                toils.Add(new DoToil((cm, world) => OpenDoor(world, cm, t, room, door, s)));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            case WayFx.Signal:
            {
                var target = way.Id == "comms.runner" ? RunnerGoal(w, c, room) : k.At;
                if (Spot(w, dist, c, target) is not Cell s) { why = "갈 수가 없다"; return null; }
                if (artId >= 0 && !TakeThing(w, dist, c, artId, toils)) { why = "무전기가 없다"; return null; }
                toils.Add(new GotoToil(s));
                toils.Add(Work(minutes, Skill.Mechanics, target));
                toils.Add(new DoToil((cm, world) =>
                {
                    var look = way.Look;
                    if (look != WayLook.None) world.Ways.AddMark(new WayMark { Look = look, At = cm.Cell, RoomId = room?.Id ?? -1, Since = world.Tick, Try = t.Id, Who = cm.Id });
                    // 두드림 · 불빛 · 뛰어 전한 말: 옆방 사람이 알아듣고 온다
                    foreach (var o in world.Crew) if (o != cm && o.CanAct && Math.Abs(o.Cell.X - cm.Cell.X) + Math.Abs(o.Cell.Y - cm.Cell.Y) < 14) o.Interrupt(world);
                    if (k.Snag == Snag.Trapped) k.NextChoose = world.Tick;
                    world.Ways.Finish(t, true, way.Id switch { "trap.knock" => "옆방에서 두드림을 들었다", "comms.runner" => "직접 뛰어가 전했다", _ => "" }, cm);
                    return true;
                }));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            // ── 정전 ──
            case WayFx.RobotBattery:
            {
                var bot = w.Robots.Robots.Where(r => r.Operational && r.AtDock && !r.Disabled).OrderBy(r => Math.Abs(r.Cell.X - c.Cell.X) + Math.Abs(r.Cell.Y - c.Cell.Y)).FirstOrDefault();
                if (bot == null || Spot(w, dist, c, bot.Cell) is not Cell s) { why = "쉬는 로봇이 없다"; return null; }
                toils.Add(new GotoToil(s));
                toils.Add(Work(minutes, Skill.Electrical, bot.Cell));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (!bot.Operational) return false;
                    float kwh = 3f + 4f * bot.Battery;
                    world.Power.BatteryCharge = MathF.Min(world.Power.BatteryCapacity, world.Power.BatteryCharge + kwh);
                    bot.Battery = 0f;
                    bot.Disabled = true;
                    MarkLog.Add(bot.Marks, world.Tick, $"{cm.Name}: 배터리를 빼 갔다 (정전)");
                    world.Ways.AddMark(new WayMark { Look = WayLook.RobotBattery, At = bot.Cell, RoomId = bot.Room?.Id ?? -1, Since = world.Tick, Try = t.Id, Who = cm.Id });
                    world.Ways.AddFollow(new WayFollow { Id = world.Ways.NextFollow(), WayId = way.Id, Text = $"{bot.Name} 배터리 다시 끼우기", Kind = 3, RobotId = bot.Id, At = bot.Cell, RoomId = bot.Room?.Id ?? -1, Since = world.Tick });
                    world.Ways.Finish(t, true, $"{Ko.EulReul(bot.Name)} 세우고 배터리를 물렸다 ({kwh:0}kWh)", cm);
                    return true;
                }));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            case WayFx.Pedal:
            {
                if (furn == null || FurnSpot(w, dist, c, furn) is not Cell s) { why = "기구가 없다"; return null; }
                toils.Add(new GotoToil(s));
                toils.Add(Work(minutes, Skill.Mechanics, furn.Cells[0]));
                toils.Add(new DoToil((cm, world) =>
                {
                    world.Power.BatteryCharge = MathF.Min(world.Power.BatteryCapacity, world.Power.BatteryCharge + 1.2f);
                    cm.Needs.Rest = MathF.Max(0f, cm.Needs.Rest - 0.25f);
                    world.Ways.AddMark(new WayMark { Look = WayLook.Pedal, At = furn.Cells[0], RoomId = furn.Room.Id, Since = world.Tick, Try = t.Id, Who = cm.Id, Until = world.Tick + SimTime.Hours(3) });
                    world.Ways.Finish(t, true, "다리가 후들거린다", cm);
                    return true;
                }));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            // ── 부상자 ──
            case WayFx.Stretcher or WayFx.Cart or WayFx.Push:
            {
                var pt = w.Crew.FirstOrDefault(x => x.Id == k.CrewId);
                if (pt == null || !pt.Down || pt.Dead) { why = "벌써 옮겼다"; return null; }
                if (way.Fx == WayFx.Cart && furn != null && FurnSpot(w, dist, c, furn) is Cell cs) { toils.Add(new GotoToil(cs)); toils.Add(Work(1f, Skill.Mechanics, furn.Cells[0])); }
                if (Spot(w, dist, c, pt.Cell) is not Cell ps) { why = "다친 사람에게 못 간다"; return null; }
                var dest = SafeSpot(w, dist, c, pt);
                if (dest is not Cell dc) { why = "옮길 곳이 없다"; return null; }
                toils.Add(new GotoToil(ps));
                if (t.Helper)
                {
                    toils.Add(new WaitToil(SimTime.Minutes(2), Pose.Working, pt.Cell.Center));
                    toils.Add(new GotoToil(dc));
                    toils.Add(new DoToil((cm, world) => { world.Ways.Finish(t, true, "같이 들었다", cm); return true; }));
                    return Wrap(act, t, w, "들것 거들기", toils, room, $"{Ko.EulReul(pt.Name)} 같이 든다");
                }
                toils.Add(new DoToil((cm, world) =>
                {
                    if (!pt.Down || pt.Dead || pt.CarriedBy != null) return false;
                    if ((pt.Position - cm.Position).Length() > 2.2f) return false;
                    pt.CarriedBy = cm; cm.CarryingPerson = pt;
                    world.Ways.AddMark(new WayMark { Look = way.Look == WayLook.None ? WayLook.Stretcher : way.Look, At = pt.Cell, RoomId = pt.Room?.Id ?? -1, Since = world.Tick, Try = t.Id, Who = cm.Id, Until = world.Tick + SimTime.Hours(2) });
                    return true;
                }));
                float boost = way.Fx switch { WayFx.Stretcher => 1.45f, WayFx.Cart => 1.55f, _ => 1.7f };
                toils.Add(new CarryGotoToil(dc, boost));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (cm.CarryingPerson != pt) return false;
                    pt.CarriedBy = null; cm.CarryingPerson = null;
                    pt.Position = dc.Center; pt.PreviousPosition = dc.Center; pt.Room = world.Ship.RoomAt(dc);
                    var bed = world.Ship.FurnitureOf(FurnitureType.MedBed).FirstOrDefault(b => b.UseSpots.Contains(dc));
                    if (bed != null && (bed.ReservedBy == null || bed.ReservedBy == pt)) { bed.ReservedBy = pt; pt.CareBed = bed; } else pt.LaidSafe = true;
                    cm.Stats.Rescues++;
                    pt.ChangeAffinity(cm, 0.2f);
                    world.Relations.Remember(pt, cm, RelationReason.SavedMe, way.Fx == WayFx.Stretcher ? "들것에 실어 옮겨 줬다" : way.Fx == WayFx.Cart ? "수레에 눕혀 옮겨 줬다" : "띄워 밀어 옮겨 줬다");
                    world.Ways.Finish(t, true, $"{Ko.EulReul(pt.Name)} {(bed != null ? "치료 침대로" : Ko.EuRo(pt.Room?.Name ?? "?"))} 옮겼다", cm);
                    return true;
                }));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            case WayFx.TreatHere or WayFx.Guided:
            {
                var pt = w.Crew.FirstOrDefault(x => x.Id == k.CrewId);
                if (pt == null || pt.Dead) { why = "늦었다"; return null; }
                if (!Fetch(c, w, dist, ItemKind.MedKit, 1, toils)) { why = "구급 키트가 없다"; return null; }
                if (way.Fx == WayFx.Guided && furn != null && FurnSpot(w, dist, c, furn) is Cell gs) { toils.Add(new GotoToil(gs)); toils.Add(Work(2f, Skill.Medicine, furn.Cells[0])); }
                if (Spot(w, dist, c, pt.Cell) is not Cell ps) { why = "다친 사람에게 못 간다"; return null; }
                toils.Add(new GotoToil(ps));
                toils.Add(Work(minutes, Skill.Medicine, pt.Cell, (cm, world) => cm.Carrying?.Kind == ItemKind.MedKit));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (cm.Carrying?.Kind != ItemKind.MedKit) return false;
                    Use(cm, ItemKind.MedKit, 1);
                    float skill = way.Fx == WayFx.Guided ? MathF.Max(cm.SkillLevel(Skill.Medicine), 0.45f) : cm.SkillLevel(Skill.Medicine);
                    pt.Vitals.Injury = MathF.Max(0f, pt.Vitals.Injury - (0.08f + 0.2f * skill));
                    pt.Vitals.Health = MathF.Min(1f, pt.Vitals.Health + 0.05f + 0.1f * skill);
                    if (world.Casualty.Of(pt) is Trauma tr && tr.Kind == TraumaKind.Bleed) tr.Rate *= 0.35f + 0.3f * (1f - skill);
                    cm.Practice(Skill.Medicine, 0.03f);
                    world.Ways.AddMark(new WayMark { Look = way.Look, At = pt.Cell, RoomId = pt.Room?.Id ?? -1, Since = world.Tick, Try = t.Id, Who = cm.Id, Until = world.Tick + SimTime.Hours(6) });
                    if (way.Fx == WayFx.Guided) cm.Say(world, "컴퓨터, 다음은? — 누르고, 감고, 높이 든다");
                    world.Ways.Finish(t, true, way.Fx == WayFx.Guided ? "단말이 알려 주는 대로 지혈했다" : "그 자리에서 지혈했다", cm);
                    return true;
                }));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            // ── 부품 ──
            case WayFx.Strip:
            {
                var target = ways.MachineById(k.FurnId);
                var donor = furn?.Machine;
                if (target == null || donor == null || k.Part is not ItemKind part || donor.Has(FaultKind.Stripped)) { why = "뗄 데가 없다"; return null; }
                if (FurnSpot(w, dist, c, donor.Body) is not Cell ds || FurnSpot(w, dist, c, target.Body) is not Cell ts) { why = "설비까지 못 간다"; return null; }
                toils.Add(new GotoToil(ds));
                toils.Add(Work(minutes * 0.7f, Skill.Mechanics, donor.Body.Cells[0], (cm, world) => !donor.Has(FaultKind.Stripped)));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (donor.Has(FaultKind.Stripped)) return false;
                    donor.Faults.Add(new Fault { Kind = FaultKind.Stripped, Since = world.Tick, PartOverride = part });
                    world.UsedParts.Add(part);
                    world.Parts.Salvaged(part, 1, donor.Name);
                    donor.Condition = MathF.Max(0.2f, donor.Condition - 0.1f);
                    donor.Active = false;
                    donor.TimesStripped++;
                    world.Adapt.Stripped++;
                    cm.Carrying = new ItemStack(part, 1);
                    world.Ways.NoteStrip(donor, cm.Id);
                    MarkLog.Add(donor.Marks, world.Tick, $"{cm.Name}: {ItemKinds.Name(part)} 떼어 감 ({target.Name}에 쓰려고)");
                    world.Ways.AddMark(new WayMark { Look = WayLook.Stripped, At = donor.Body.Cells[0], RoomId = donor.Body.Room.Id, Since = world.Tick, Try = t.Id, Who = cm.Id });
                    world.Log.Add(world.Tick, LogKind.Warning, $"{Ko.EulReul(donor.Name)} 열어 {Ko.EulReul(ItemKinds.Name(part))} 떼어 냈다 — {Ko.EunNeun(donor.Name)} 이제 멈춘다", cm.Id);
                    return true;
                }));
                toils.Add(new GotoToil(ts));
                toils.Add(Work(MathF.Max(6f, minutes * 0.3f), Skill.Mechanics, target.Body.Cells[0]));
                toils.Add(new DoToil((cm, world) => Install(world, cm, t, target, part, proper: true, $"{donor.Name}에서 떼어 온")));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            case WayFx.Improvise or WayFx.Lathe or WayFx.Recycle:
            {
                var target = ways.MachineById(k.FurnId);
                if (target == null) { why = "고칠 설비가 없다"; return null; }
                var part = k.Part;
                if (way.Fx == WayFx.Lathe)
                {
                    if (furn == null || part is not ItemKind lp || !Fetch(c, w, dist, ItemKind.Plate, 1, toils) || FurnSpot(w, dist, c, furn) is not Cell ls) { why = "깎을 판이 없다"; return null; }
                    toils.Add(new GotoToil(ls));
                    toils.Add(Work(minutes * 0.8f, Skill.Engineering, furn.Cells[0]));
                    toils.Add(new DoToil((cm, world) =>
                    {
                        if (cm.Carrying?.Kind != ItemKind.Plate) return false;
                        cm.Carrying = new ItemStack(lp, 1);
                        cm.Practice(Skill.Engineering, 0.03f);
                        world.Ways.AddMark(new WayMark { Look = WayLook.Shavings, At = furn.Cells[0], RoomId = furn.Room.Id, Since = world.Tick, Try = t.Id, Who = cm.Id, Until = world.Tick + SimTime.Hours(10) });
                        return true;
                    }));
                }
                else if (way.Fx == WayFx.Recycle)
                {
                    var heap = w.Ship.LiveRooms.FirstOrDefault(r => r.Kind is RoomType.Recycling or RoomType.Storage);
                    if (heap == null || Spot(w, dist, c, heap.Cells[heap.Cells.Count / 2]) is not Cell hs) { why = "고철 더미가 없다"; return null; }
                    toils.Add(new GotoToil(hs));
                    toils.Add(Work(minutes * 0.7f, Skill.Mechanics, hs));
                    toils.Add(new DoToil((cm, world) =>
                    {
                        if (part is not ItemKind rp || !world.Ways.Roll(0.35f + 0.3f * cm.SkillLevel(Skill.Mechanics))) { world.Ways.Finish(t, false, "쓸 만한 게 없었다", cm); return false; }
                        cm.Carrying = new ItemStack(rp, 1);
                        return true;
                    }));
                }
                else if (artId >= 0 && !TakeThing(w, dist, c, artId, toils)) { why = "천이 없다"; return null; }
                if (FurnSpot(w, dist, c, target.Body) is not Cell ms) { why = "설비까지 못 간다"; return null; }
                toils.Add(new GotoToil(ms));
                toils.Add(Work(way.Fx == WayFx.Improvise ? minutes : MathF.Max(6f, minutes * 0.2f), Skill.Mechanics, target.Body.Cells[0]));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (way.Fx == WayFx.Improvise)
                    {
                        if (artId >= 0 && world.Matter.Get(artId) is Article a) world.Matter.Remove(a);
                        return Install(world, cm, t, target, part, proper: false, t.Thing);
                    }
                    if (part is ItemKind ip && cm.Carrying?.Kind == ip) return Install(world, cm, t, target, part, proper: true, way.Fx == WayFx.Lathe ? "깎아 만든" : "고철에서 건진");
                    return false;
                }));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
            // ── 그 밖 (공기 · 모이기 · 아끼기 · 묶기 · 쐐기 · 테이프 · 보온기 · 닦기 · 등 · 단순) ──
            default:
            {
                Cell target = k.At;
                if (furn != null) target = furn.Cells[0];
                if (artId >= 0 && way.Fx is not (WayFx.Strap or WayFx.Wedge) && !TakeThing(w, dist, c, artId, toils)) { why = "물건이 없다"; return null; }
                if (way.Item is ItemKind it2 && !Fetch(c, w, dist, it2, way.ItemCount, toils)) { why = $"{ItemKinds.Name(it2)} 없음"; return null; }
                Room? goal = way.Fx == WayFx.Gather ? GatherRoom(w, k, room) : null;
                if (goal != null) target = goal.Cells.FirstOrDefault(w.Ship.IsOpenFloor);
                if (way.Fx is WayFx.Strap or WayFx.Wedge && artId >= 0 && w.Matter.Get(artId) is Article cargo) target = cargo.At;
                if ((furn != null && goal == null ? FurnSpot(w, dist, c, furn) : Spot(w, dist, c, target)) is not Cell s) { why = "갈 수가 없다"; return null; }
                toils.Add(new GotoToil(s));
                toils.Add(Work(minutes, way.Skill, target));
                toils.Add(new DoToil((cm, world) => Simple(world, cm, t, room, goal, furn, artId, target)));
                return Wrap(act, t, w, way.Name, toils, room, log);
            }
        }
    }

    // ───────────── 결과 (규칙에서) ─────────────

    private static bool Douse(World w, CrewMember c, WayTry t, Room room, Furniture? src, int artId)
    {
        var ways = w.Ways;
        var aim = NearestFire(w, room, c);
        float liters;
        if (src != null)
        {
            liters = 40f;
            if (src.Type == FurnitureType.GrowBed && src.Machine?.Crop is CropState crop) { crop.DryHours += 8f; crop.Care = MathF.Max(0f, crop.Care - 0.25f); ways.AddFollow(new WayFollow { Id = ways.NextFollow(), WayId = t.WayId, Text = $"{src.Name} 양액 다시 채우기", Kind = 5, FurnId = src.Id, RoomId = src.Room.Id, At = src.Cells[0], Since = w.Tick }); }
            else w.Water.Level = MathF.Max(0f, w.Water.Level - liters);
        }
        else if (w.Matter.Get(artId) is Article jug)
        {
            liters = MathF.Max(2f, jug.Contents);
            jug.Contents = 0f;
            w.Matter.Place(jug, c.Cell);
        }
        else return false;
        if (aim is Cell a) w.Fire.Suppress(a, src != null ? 2.3f : 1.3f, src != null ? 1f : 0.9f);
        w.Moisture.AddWater(room, liters * 0.7f);
        ways.AddMark(new WayMark { Look = src != null ? (src.Type == FurnitureType.GrowBed ? WayLook.HydroHose : WayLook.Douse) : WayLook.Bucket, At = aim ?? c.Cell, RoomId = room.Id, Since = w.Tick, Try = t.Id, Who = c.Id, Until = w.Tick + SimTime.Hours(6), Mat = Material.Liquid, Dir = src != null ? src.Cells[0] - (aim ?? c.Cell) : default });
        // 물 + 전기 = 감전 (전원을 내렸으면 없다)
        float live = aim is Cell la ? WaysRules.LiveNear(w, room, la) : 0f;
        if (live > 0f && ways.Roll(WaysRules.ShockChance(w, c, live)))
        {
            float dmg = 0.06f + 0.1f * live;
            c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg);
            NeedsSystem.AddInjury(c.Vitals, dmg * 0.8f, "감전");
            ways.Stats.Shocks++;
            w.Causes.Effect(CauseKind.Shock, "", $"{c.Name} 감전 (전기 불에 물)", room, c.Position, lasting: false);
            w.Log.Add(w.Tick, LogKind.Warning, $"전기가 살아 있는 불에 물을 부었다 — {Ko.IGa(c.Name)} 감전됐다", c.Id);
            Memory.Shake(w, c, 0.08f, "전기 불에 물을 붓다 감전됐다");
            c.Interrupt(w);
            ways.Finish(t, false, "전기 불에 물을 붓다 감전됐다", c);
            return true;
        }
        if (w.Fire.CountIn(room) == 0) { ways.Finish(t, true, $"물 {liters:0}리터로 껐다 — 바닥이 흥건하다", c); return true; }
        t.Rounds++;
        if (t.Rounds >= 3) ways.Finish(t, false, "물이 모자랐다", c);
        else t.State = 0; // 물을 더 가져온다
        return true;
    }

    private static bool Smother(World w, CrewMember c, WayTry t, Room room, Furniture? furn, int artId)
    {
        var ways = w.Ways;
        var aim = NearestFire(w, room, c);
        if (aim is not Cell a) { ways.Finish(t, true, "벌써 꺼졌다", c); return true; }
        var cloth = artId >= 0 ? w.Matter.Get(artId) : null;
        var mat = cloth?.Mat ?? Material.Fabric;
        float wet = cloth?.WetFrac ?? 0f;
        bool blanket = furn?.Type == FurnitureType.FireBlanket;
        float p = WaysRules.SmotherChance(mat, wet, w.Fire.At(a), w.Fire.CountIn(room)) + (blanket ? 0.25f : 0f);
        bool ok = ways.Roll(p);
        if (cloth != null) { w.Matter.Place(cloth, a); cloth.Char = MathF.Min(1f, cloth.Char + (ok ? 0.5f : 1f)); if (!ok) cloth.Smolder = true; }
        ways.AddMark(new WayMark { Look = WayLook.Smother, At = a, RoomId = room.Id, Since = w.Tick, Try = t.Id, Who = c.Id, Ok = ok, Mat = mat, Until = w.Tick + SimTime.Hours(8) });
        if (ok)
        {
            w.Fire.Suppress(a, 1.1f, 1f);
            bool done = w.Fire.CountIn(room) == 0;
            if (done) ways.Finish(t, true, blanket ? "방화 담요로 덮어 껐다" : $"{(t.Thing.Length > 0 ? t.Thing : "천")}을 덮어 껐다", c);
            else { t.Rounds++; if (t.Rounds >= 2) ways.Finish(t, false, "한 군데는 덮었는데 옆으로 번졌다", c); else t.State = 0; }
            return true;
        }
        NeedsSystem.AddInjury(c.Vitals, 0.08f, "화상");
        w.Log.Add(w.Tick, LogKind.Warning, $"덮은 {(t.Thing.Length > 0 ? t.Thing : "천")}에 불이 옮겨붙었다 — {Ko.IGa(c.Name)} 손을 데었다", c.Id);
        ways.Finish(t, false, "마른 천에 불이 옮겨붙었다", c);
        return true;
    }

    private static bool Plug(World w, CrewMember c, WayTry t, Room room, WallState wall, Cell at, Furniture? furn, int artId, Cell from)
    {
        var ways = w.Ways;
        var way = t.Way;
        if (wall.Patched || wall.Breach <= 0f) { ways.Finish(t, true, "누가 먼저 막았다", c); return true; }
        Material mat; float bulk; string name;
        var a = artId >= 0 ? w.Matter.Get(artId) : null;
        if (way.Fx == WayFx.Brace) { mat = Material.Fabric; bulk = 0.2f; name = "등"; }
        else if (way.Fx == WayFx.Freeze) { mat = Material.Ice; bulk = 0.1f; name = a?.Name ?? "얼음 마개"; }
        else if (a != null) { mat = a.Mat; bulk = WaysRules.Bulk(a.Kind); name = a.Name; }
        else if (furn != null) { var fi = WaysRules.FromFurniture(furn.Type); mat = fi.mat; bulk = fi.bulk; name = fi.name; }
        else return false;
        float q = way.Fx switch { WayFx.Brace => 0.5f, WayFx.Freeze => 0.62f - (wall.Breach >= 0.25f ? 0.15f : 0f), _ => WaysRules.PlugQuality(mat, bulk, wall.Breach) };
        q *= 0.85f + 0.3f * c.SkillLevel(Skill.Mechanics);
        if (q < 0.32f)
        {
            if (a != null) { w.Matter.Remove(a); }
            w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.EulReul(name)} 구멍에 댔는데 바람에 빨려 나갔다", c.Id);
            ways.Finish(t, false, $"{Ko.IGa(name)} 빨려 나갔다", c);
            return true;
        }
        wall.Patched = true;
        wall.PatchQuality = MathF.Min(0.9f, q);
        float decay = way.Fx switch { WayFx.Brace => 1.6f, WayFx.Freeze => 0.22f, _ => WaysRules.PlugDecayPerHour(mat, room.Air.Pressure) };
        int fid = ways.NextFollow();
        ways.AddPlug(new WayPlug { Wall = at, Mat = mat, Decay = decay, Name = name, Since = w.Tick, Follow = fid });
        ways.AddFollow(new WayFollow { Id = fid, WayId = way.Id, Text = $"{room.Name} 구멍 — {Ko.EulReul(name)} 떼고 제대로 막기", Kind = 0, At = at, RoomId = room.Id, FurnId = furn?.Id ?? -1, Since = w.Tick });
        if (furn != null && furn.Type is FurnitureType.Bed or FurnitureType.Cot or FurnitureType.MedBed) ways.Bare(furn.Id, true);
        if (a != null) w.Matter.Remove(a); // 구멍 속에 끼었다
        if (way.Fx == WayFx.Brace) { NeedsSystem.AddInjury(c.Vitals, 0.05f, "등에 멍 · 동상"); c.Say(w, "차갑다 — 빨리 실링폼!"); }
        ways.AddMark(new WayMark { Look = way.Look, At = at, RoomId = room.Id, Since = w.Tick, Try = t.Id, Who = c.Id, Mat = mat, Q = wall.PatchQuality, Dir = from - at, Active = false });
        MarkLog.Add(wall.Marks, w.Tick, $"{c.Name}: {name}(으)로 임시로 막음");
        ways.Finish(t, true, $"{Ko.EuRo(name)} 막았다 — 오래는 못 간다", c);
        w.Board.RequestScan();
        return true;
    }

    private static bool OpenDoor(World w, CrewMember c, WayTry t, Room room, Door door, Cell from)
    {
        var ways = w.Ways;
        var way = t.Way;
        if (!WaysSystem.Stuck(door)) { ways.Finish(t, true, "문이 이미 열렸다", c); return true; }
        string res;
        switch (way.Fx)
        {
            case WayFx.Pry:
                door.Welded = false; door.Locked = false;
                door.Bent = MathF.Max(door.Bent, 0.55f);
                door.Openness = MathF.Max(door.Openness, 0.6f);
                door.Request();
                res = "쇠지레로 문틀째 벌렸다";
                ways.AddFollow(new WayFollow { Id = ways.NextFollow(), WayId = way.Id, Text = $"{room.Name} 휜 문틀 펴기", Kind = 1, DoorId = door.Id, At = door.Cell, RoomId = room.Id, Since = w.Tick });
                break;
            case WayFx.Bypass:
                door.Welded = false; door.Locked = false; door.MotorBroken = false;
                door.Request();
                Use(c, ItemKind.Cable, 1);
                res = "모터에 선을 물려 열었다";
                break;
            default: // 자르기 · 벽 뚫기 · 폭파 — 문짝이 없어진다
                door.JammedOpen = true; door.Welded = false; door.Locked = false; door.MotorBroken = true; door.Openness = 1f; door.Bent = 1f;
                res = way.Fx == WayFx.Blow ? "문을 날려 버렸다" : way.Id == "trap.wall" ? "옆 벽을 뚫었다" : "문을 잘라 냈다";
                ways.AddFollow(new WayFollow { Id = ways.NextFollow(), WayId = way.Id, Text = $"{room.Name} 새 문 달기", Kind = 1, DoorId = door.Id, At = door.Cell, RoomId = room.Id, Since = w.Tick });
                if (way.Fx == WayFx.Blow)
                    foreach (var o in w.Crew)
                    {
                        if (o.Dead || Math.Abs(o.Cell.X - door.Cell.X) + Math.Abs(o.Cell.Y - door.Cell.Y) > 2) continue;
                        NeedsSystem.AddInjury(o.Vitals, o == c ? 0.04f : 0.1f, "파편");
                        w.Log.Add(w.Tick, LogKind.Warning, $"문 파편이 튀었다 — {Ko.IGa(o.Name)} 다쳤다", o.Id);
                    }
                break;
        }
        ways.AddMark(new WayMark { Look = way.Look, At = door.Cell, RoomId = room.Id, DoorId = door.Id, Since = w.Tick, Try = t.Id, Who = c.Id, Dir = from - door.Cell, Active = false });
        MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: {res}");
        var trapped = w.Crew.Where(o => !o.Dead && o.Room == room && o != c).ToList();
        foreach (var o in trapped) { o.Interrupt(w); if (o != c) o.ChangeAffinity(c, 0.15f); }
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(c.Name)} {room.Name}에 갇힌 {(trapped.Count > 0 ? string.Join("·", trapped.Select(x => x.Name)) : "사람")}을 꺼내려 {res}", room, trapped.Prepend(c));
        ways.Finish(t, true, res, c);
        return true;
    }

    private static bool Install(World w, CrewMember c, WayTry t, Machine m, ItemKind? part, bool proper, string how)
    {
        var ways = w.Ways;
        var fault = m.Faults.FirstOrDefault(f => f.Part == part && f.Kind is not FaultKind.Stripped and not FaultKind.BreakerTrip)
                    ?? (t.Way.Snag == Snag.Clog ? m.Faults.FirstOrDefault(f => f.Kind is FaultKind.FilterClogged or FaultKind.NutrientClog or FaultKind.InjectorClog or FaultKind.NozzleClog or FaultKind.HeatsinkClog or FaultKind.DrainClog) : null);
        if (fault == null) { if (part is ItemKind p0 && c.Carrying?.Kind == p0) c.Carrying = null; ways.Finish(t, true, "벌써 고쳐져 있었다", c); return true; }
        if (part is ItemKind p && c.Carrying?.Kind == p) Use(c, p, 1);
        m.Faults.Remove(fault);
        m.Condition = MathF.Min(1f, m.Condition + 0.01f);
        if (!proper)
        {
            m.Grade = MachineGrade.Mk1; // 임시품: 덜 나오고 더 자주 선다 — 부품이 넉넉해지면 기존 수순이 정품으로 되돌린다
            m.Wear = MathF.Min(1f, m.Wear + 0.2f);
            ways.AddMark(new WayMark { Look = WayLook.Improvised, At = m.Body.Cells[0], RoomId = m.Body.Room.Id, Since = w.Tick, Try = t.Id, Who = c.Id, Active = false });
        }
        c.Practice(m.Spec.Skill, 0.03f);
        MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: {how} {(part is ItemKind pp ? ItemKinds.Name(pp) : "")}{(proper ? "" : " (임시)")}로 수리");
        ways.Finish(t, true, $"{how}{(how.EndsWith("온") || how.EndsWith("든") || how.EndsWith("진") ? " " : "(으)로 ")}{(proper && part is ItemKind p2 ? ItemKinds.Name(p2) + "로 " : "")}{Ko.EulReul(m.Name)} 살렸다", c);
        return true;
    }

    private static bool Simple(World w, CrewMember c, WayTry t, Room? room, Room? goal, Furniture? furn, int artId, Cell at)
    {
        var ways = w.Ways;
        var way = t.Way;
        var r = room ?? c.Room;
        string res = "";
        switch (way.Fx)
        {
            case WayFx.Strap or WayFx.Wedge:
            {
                if (artId >= 0 && w.Matter.Get(artId) is Article cargo) { cargo.Vel = default; cargo.Fixed = true; }
                if (way.Item is ItemKind it) Use(c, it, way.ItemCount);
                res = way.Fx == WayFx.Strap ? "케이블로 동여맸다" : "숟가락을 바퀴 밑에 끼웠다";
                break;
            }
            case WayFx.TapeHose:
                if (r != null) r.Flood *= 0.3f;
                res = "테이프로 감고 호스로 물길을 돌렸다";
                break;
            case WayFx.Warmer:
                if (r != null) ways.ApplyGen(r, way, c);
                if (way.Item is ItemKind it2) Use(c, it2, 1);
                res = "냄비 보온기가 데워진다";
                break;
            case WayFx.Mop:
                if (r != null)
                {
                    foreach (var cell in r.Cells) if (w.Matter.SpillAt(cell) is Spill s) s.Liters *= 0.2f;
                    r.Flood *= 0.6f;
                }
                if (artId >= 0 && w.Matter.Get(artId) is Article towel) { towel.Water = MathF.Min(towel.Spec.Capacity, towel.Water + towel.Spec.Capacity); w.Matter.Place(towel, c.Cell); }
                res = "바닥을 훔쳤다";
                break;
            case WayFx.Ration:
            {
                int n = 0;
                foreach (var o in w.Crew)
                {
                    if (o.Dead || o.Away) continue;
                    if (way.Snag == Snag.Food && n < 12 && w.Ship.Furniture.FirstOrDefault(f => f.Storage is Inventory inv && inv.Count(ItemKind.Ration) > 0) is Furniture box) { box.Storage!.Take(ItemKind.Ration, 1); o.Needs.Food = MathF.Min(1f, o.Needs.Food + 0.3f); n++; }
                    o.Needs.Stress = Math.Clamp(o.Needs.Stress + way.Gen.Stress, 0f, 1f);
                }
                res = way.Snag == Snag.Food ? $"비상식량 {n}개를 나눴다" : "다들 아껴 쓰기로 했다";
                break;
            }
            case WayFx.Gather:
                if (r != null) foreach (var o in w.Crew) if (o != c && o.CanAct && o.Room == r) o.Interrupt(w);
                if (goal != null && way.Gen != default) ways.ApplyGen(goal, way, c);
                res = goal != null ? $"{Ko.EuRo(goal.Name)} 모였다" : "다들 피했다";
                break;
            case WayFx.Lamp:
                if (r != null) ways.Lamp(r, MathF.Max(1f, way.Gen.LightHours));
                res = "불빛을 놓았다";
                break;
            default:
                if (r != null) ways.ApplyGen(r, way, c);
                if (way.Id == "heat.off" && r != null && r.Furniture.Select(f => f.Machine).Where(m => m is { Active: true, Powered: true }).OrderByDescending(m => m!.Spec.PowerDraw).FirstOrDefault() is Machine hot)
                {
                    hot.Active = false;
                    ways.AddFollow(new WayFollow { Id = ways.NextFollow(), WayId = way.Id, Text = $"{hot.Name} 식으면 다시 켜기", Kind = 7, FurnId = hot.Body.Id, RoomId = r.Id, At = hot.Body.Cells[0], Since = w.Tick });
                    res = $"{Ko.EulReul(hot.Name)} 껐다";
                }
                if (way.Snag == Snag.Spark && r != null) foreach (var j in w.Matter.Junctions) if (j.Room == r.Id && j.Live) { j.Taped = true; j.Fixed = true; j.Live = false; }
                if (way.Snag == Snag.Clog && ways.MachineById(ways.Case(t.CaseId)?.FurnId ?? -1) is Machine cm2) return Install(w, c, t, cm2, null, proper: way.Id != "clog.plunger", way.Name);
                break;
        }
        if (way.Look != WayLook.None) ways.AddMark(new WayMark { Look = way.Look, At = at, RoomId = (goal ?? r)?.Id ?? -1, Since = w.Tick, Try = t.Id, Who = c.Id, Until = w.Tick + SimTime.Hours(way.Fx is WayFx.Strap or WayFx.Wedge or WayFx.TapeHose ? 24 : 6) });
        ways.Finish(t, true, res, c);
        return true;
    }

    private static Cell RunnerGoal(World w, CrewMember c, Room? from)
    {
        var bridge = w.Ship.LiveRooms.FirstOrDefault(r => r.Kind == RoomType.Bridge && r != from) ?? w.Ship.LiveRooms.FirstOrDefault(r => r != from && r.DataLinked);
        return bridge?.Cells.FirstOrDefault(w.Ship.IsOpenFloor) ?? c.Cell;
    }

    private static Room? GatherRoom(World w, WayCase k, Room? from)
    {
        var ship = w.Ship;
        Room? best = null; float bs = float.MinValue;
        foreach (var r in ship.LiveRooms)
        {
            if (r == from || r.Abandoned || r.Leaking || r.Unbreathable || w.Fire.Count > 0 && w.Fire.CountIn(r) > 0) continue;
            float s = k.Snag switch
            {
                Snag.Oxygen => r.Air.O2 + (r.Kind is RoomType.Hydroponics or RoomType.LifeSupport ? 3f : 0f),
                Snag.Cold => r.Air.Temperature,
                Snag.Overheat => -r.Air.Temperature,
                _ => -(Math.Abs(r.Cells[0].X - k.At.X) + Math.Abs(r.Cells[0].Y - k.At.Y)) * 0.1f - r.Air.Smoke * 10f - r.Air.Toxin * 10f,
            };
            if (from != null && !from.Doors.Any(d => d.RoomA == r || d.RoomB == r)) s -= 2f;
            if (s > bs) { bs = s; best = r; }
        }
        return best;
    }

    private static Cell? SafeSpot(World w, DistanceField dist, CrewMember c, CrewMember pt)
    {
        var bed = w.Ship.FurnitureOf(FurnitureType.MedBed).Where(b => (b.ReservedBy == null || b.ReservedBy == pt) && b.UseSpots.Count > 0 && dist.Reachable(b.UseSpots[0])).OrderBy(b => dist.Get(b.UseSpots[0])).FirstOrDefault();
        if (bed != null) return bed.UseSpots[0];
        Cell? safe = null; int best = int.MaxValue;
        foreach (var room in w.Ship.Rooms)
        {
            if (room.Detached || Atmosphere.Danger(room) > 0.1f || room.Leaking || room.Abandoned || w.Fire.CountIn(room) > 0) continue;
            foreach (var cell in room.Cells) { int d = dist.Get(cell); if (d < 0 || d >= best || !w.Ship.IsOpenFloor(cell)) continue; best = d; safe = cell; }
        }
        return safe;
    }

    // ───────────── 나중에 제대로 ─────────────

    public static Job? BuildFollow(Activity act, CrewMember c, World w, DistanceField dist, WayFollow f)
    {
        var ways = w.Ways;
        var room = f.RoomId >= 0 && f.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[f.RoomId] : null;
        var toils = Plans.DropOff(c, w, dist);
        Cell target = f.At;
        Skill skill = Skill.Mechanics;
        float hours = 0.4f;
        switch (f.Kind)
        {
            case 0:
            {
                var wall = w.Ship.WallAt(f.At);
                if (wall == null) return null;
                var kind = w.Ship.CountStored(ItemKind.Sealant) >= Hull.SealantFor(wall) ? ItemKind.Sealant : ItemKind.Plate;
                if (!Fetch(c, w, dist, kind, kind == ItemKind.Sealant ? Hull.SealantFor(wall) : 2, toils)) return null;
                hours = kind == ItemKind.Sealant ? 0.4f : 0.9f;
                break;
            }
            case 1:
                if (w.Ship.Doors[f.DoorId].JammedOpen && !Fetch(c, w, dist, ItemKind.Plate, 1, toils)) return null;
                hours = w.Ship.Doors[f.DoorId].JammedOpen ? 1f : 0.5f;
                break;
            case 2 or 3: skill = Skill.Electrical; hours = 0.2f; if (f.Kind == 3 && w.Robots.Robots.FirstOrDefault(r => r.Id == f.RobotId) is Robot rb) target = rb.Cell; break;
            case 4 or 5 or 6 or 7: hours = 0.25f; if (ways.FurnById(f.FurnId) is Furniture ff) target = ff.Cells[0]; break;
        }
        var spot = room != null && f.Kind is 0 ? Spot(w, dist, c, target, inRoom: room) : Spot(w, dist, c, target);
        if (spot is not Cell s) return null;
        f.Claimed = c.Id;
        toils.Add(new GotoToil(s));
        toils.Add(new WorkToil(hours, skill, target.Center));
        toils.Add(new DoToil((cm, world) => world.Ways.DoFollow(f, cm)));
        return new Job(act, f.Text, toils) { LogText = $"{f.Text} — 급한 대로 해 둔 것을 제대로", LogKind = LogKind.Work, TargetRoom = room };
    }
}

public sealed partial class WaysSystem
{
    public int NextFollow() => _nextFollow++;
    public bool Roll(float p) => R.Chance(p);
    private readonly List<int> _bare = new();
    /// <summary>매트리스를 떼어 간 침대 (맨 틀).</summary>
    public bool IsBare(int furnId) => _bare.Contains(furnId);
    internal void Bare(int furnId, bool on) { if (on) { if (!_bare.Contains(furnId)) _bare.Add(furnId); } else _bare.Remove(furnId); }

    /// <summary>그 사람이 할 만한 나중 일 (자원 · 솜씨 · 안전).</summary>
    public WayFollow? FollowFor(CrewMember c)
    {
        var w = _w;
        foreach (var f in Follows)
        {
            if (f.Done >= 0) continue;
            if (f.Claimed >= 0 && f.Claimed != c.Id && w.Crew.FirstOrDefault(x => x.Id == f.Claimed) is CrewMember o && o.Job?.Activity is WayActivity && o.CanAct) continue;
            var room = f.RoomId >= 0 && f.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[f.RoomId] : null;
            if (room != null && (room.Abandoned || room.Detached || w.Fire.Count > 0 && w.Fire.CountIn(room) > 0)) continue;
            switch (f.Kind)
            {
                case 0:
                    if (w.Ship.WallAt(f.At) is not WallState wall || !wall.Patched) { f.Done = w.Tick; continue; }
                    if (w.Ship.CountStored(ItemKind.Sealant) < Hull.SealantFor(wall) && w.Ship.CountStored(ItemKind.Plate) < 2) continue;
                    if (room != null && room.Air.Pressure < 60f && c.Suit == null) continue;
                    break;
                case 1:
                    if (f.DoorId < 0 || f.DoorId >= w.Ship.Doors.Count) { f.Done = w.Tick; continue; }
                    if (w.Ship.Doors[f.DoorId].JammedOpen && w.Ship.CountStored(ItemKind.Plate) < 1) continue;
                    break;
                case 2: if (room == null || !room.BreakerOff) { f.Done = w.Tick; continue; } if (MoistureSystem.Depth(room) > 0.05f) continue; break;
                case 3: if (!w.Power.ReactorOnline && !w.Power.AuxRunning) continue; break;
                case 4: if (Follows.Any(x => x.Kind == 0 && x.FurnId == f.FurnId && x.Done < 0)) continue; break;
                case 5: if (w.Water.Level < 60f) continue; break;
                case 7: if (room != null && room.Air.Temperature > 30f) continue; break;
            }
            if (f.Kind is 2 or 3 && c.SkillLevel(Skill.Electrical) < 0.2f) continue;
            return f;
        }
        return null;
    }

    internal bool DoFollow(WayFollow f, CrewMember c)
    {
        var w = _w;
        if (f.Done >= 0) return true;
        var room = f.RoomId >= 0 && f.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[f.RoomId] : null;
        string what = f.Text;
        switch (f.Kind)
        {
            case 0:
            {
                if (w.Ship.WallAt(f.At) is not WallState wall) break;
                bool sealant = c.Carrying?.Kind == ItemKind.Sealant;
                if (!sealant && c.Carrying?.Kind != ItemKind.Plate) return false;
                var plug = Plugs.FirstOrDefault(p => p.Wall == f.At && !p.Gone);
                c.Carrying = null;
                float skill = c.SkillLevel(Skill.Mechanics);
                wall.Patched = true;
                wall.PatchQuality = 0.55f + 0.4f * skill;
                if (sealant) wall.Seals++; else { wall.Welds++; wall.TotalWelds++; }
                if (plug != null) plug.Gone = true;
                Marks.RemoveAll(m => m.At == f.At && m.Look is WayLook.MattressPlug or WayLook.TablePlug or WayLook.PotPlug or WayLook.CratePlug or WayLook.MatPlug or WayLook.FrostPlug or WayLook.BackPlug or WayLook.RobotBrace);
                what = $"{room?.Name ?? "선체"} 구멍에 댄 {(plug != null ? Ko.EulReul(plug.Name) : "임시 마개를")} 떼고 {(sealant ? "실링폼으로" : "금속판을 덧대")} 제대로 막았다";
                if (f.FurnId >= 0) AddFollow(new WayFollow { Id = NextFollow(), WayId = f.WayId, Text = $"{FurnById(f.FurnId)?.Name ?? "침대"} 매트리스 돌려 놓기", Kind = 4, FurnId = f.FurnId, RoomId = FurnById(f.FurnId)?.Room.Id ?? -1, At = FurnById(f.FurnId)?.Cells[0] ?? f.At, Since = w.Tick });
                break;
            }
            case 1:
            {
                var d = w.Ship.Doors[f.DoorId];
                if (d.JammedOpen) { if (c.Carrying?.Kind == ItemKind.Plate) c.Carrying = null; d.JammedOpen = false; d.MotorBroken = false; d.Openness = 1f; }
                d.Bent = 0f;
                Marks.RemoveAll(m => m.DoorId == f.DoorId && m.Look is WayLook.PriedDoor or WayLook.CutDoor or WayLook.BlownDoor or WayLook.WallHole);
                what = $"{room?.Name ?? "?"} 문틀을 펴고 문을 다시 달았다";
                break;
            }
            case 2: if (room != null) w.Moisture.Restore(room, c); break;
            case 3:
                if (w.Robots.Robots.FirstOrDefault(r => r.Id == f.RobotId) is Robot rb) { rb.Disabled = false; rb.Battery = MathF.Max(rb.Battery, 0.3f); MarkLog.Add(rb.Marks, w.Tick, $"{c.Name}: 배터리를 다시 끼웠다"); }
                Marks.RemoveAll(m => m.Look == WayLook.RobotBattery && m.At == f.At);
                break;
            case 4: Bare(f.FurnId, false); what = $"{FurnById(f.FurnId)?.Name ?? "침대"}에 매트리스를 돌려 놓았다 (말려서)"; break;
            case 5: if (FurnById(f.FurnId)?.Machine?.Crop is CropState crop) { crop.DryHours = 0f; crop.Care = MathF.Min(1f, crop.Care + 0.2f); } w.Water.Level = MathF.Max(0f, w.Water.Level - 20f); break;
            case 6: if (room != null) { room.Air.Smoke *= 0.3f; room.VentOpen = true; } break;
            case 7: if (MachineById(f.FurnId) is Machine m) m.Active = true; break;
        }
        f.Done = w.Tick;
        f.By = c.Id;
        Stats.FollowsDone++;
        w.Log.Add(w.Tick, LogKind.Work, what, c.Id);
        if (f.Kind is 0 or 1) w.History.Add(w, HistoryKind.Recovery, $"{Ko.IGa(c.Name)} {what}", room, new[] { c });
        return true;
    }
}
