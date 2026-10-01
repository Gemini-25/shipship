using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.13 우주 대재난 앞에서 승무원이 하는 일: 나가기(봉쇄 구획) · 숨기 · 대비 일 · 서로 알리고 깨우기 · 창밖 보기 · 그날의 밤.
// 아는 사람만 움직인다 (방송을 들었거나 · 들었거나 · 봤거나). 컴퓨터를 믿는 만큼 서두른다.

internal static class CosmicCrew
{
    public static bool Free(CrewMember c) => c.CanAct && !c.Outside && c.Room != null && c.CarriedBy == null;

    /// <summary>방 안에서 갈 수 있는 빈 바닥 (가까운 순).</summary>
    public static Cell? SpotIn(Room room, CrewMember c, World w, DistanceField dist, bool free = true)
    {
        Cell? best = null;
        int bestCost = int.MaxValue;
        foreach (var cell in room.Cells)
        {
            int d = dist.Get(cell);
            if (d < 0 || d >= bestCost || !w.Ship.IsOpenFloor(cell) || free && w.IsSpotTaken(cell, c)) continue;
            best = cell;
            bestCost = d;
        }
        return best;
    }

    public static Cell? SpotAt(Furniture f, CrewMember c, World w, DistanceField dist) =>
        f.UseSpots.Where(s => dist.Reachable(s)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault() ?? SpotIn(f.Room, c, w, dist, free: false);

    public static bool NeedsShelter(CosmicSpec s) => s.Has(CosmicFx.Radiation) || s.Has(CosmicFx.Shock) || s.Has(CosmicFx.Heat) && s.Has(CosmicFx.Plasma);

    /// <summary>이 사람에게 지금 숨을 이유가 있나 (아는 것 · 믿는 것 · 실제로 쬐는 것).</summary>
    public static (CosmicEvent? e, float urgency, string why) ShelterCall(CrewMember c, World w)
    {
        var cs = w.Cosmic;
        foreach (var e in cs.Events)
        {
            if (e.Phase is not (CosmicPhase.Brace or CosmicPhase.Impact) || !NeedsShelter(e.Spec) || !cs.Knows(c, e)) continue;
            bool burning = cs.FxNow(e, CosmicFx.Radiation) > 0f || cs.FxNow(e, CosmicFx.Shock) > 0f || cs.FxNow(e, CosmicFx.Heat) > 0f;
            long harm = cs.NextHarm(e);
            if (harm == long.MaxValue) continue;
            float lead = cs.Follows(c, CosmicCustomKind.Drill) ? 1.6f : 1f;
            float hours = e.HoursTo(w.Tick, harm);
            if (!burning && hours > lead) continue;
            // 컴퓨터를 못 믿는 자유파는 실제로 쬘 때까지 버틴다
            if (!burning && c.Value == CrewValue.Freedom && w.Automation.Trusts.Of(c) < 0.35f) continue;
            float urg = burning ? 1f : Math.Clamp(1f - hours / lead, 0f, 1f);
            return (e, urg, burning ? $"{e.Spec.Name} — 지금 쏟아진다" : $"{e.Spec.Name} — {hours * 60f:0}분 뒤");
        }
        return (null, 0f, "");
    }
}

/// <summary>봉쇄할 구획에 있으면 나간다.</summary>
public sealed class CosmicEvacuateActivity : Activity
{
    public override string Id => "cosmicevac";
    public override string Label => "구획 비우기";

    private static CosmicEvent? Target(CrewMember c, World w) =>
        c.Room == null ? null : w.Cosmic.Events.FirstOrDefault(e => e.SealPlan && !e.Avoided && e.Phase <= CosmicPhase.Impact && w.Tick < e.Arrive + SimTime.Minutes(10)
            && (e.TargetRoom == c.Room.Id || e.Evac.Contains(c.Room.Id) && w.Tick >= e.Arrive - SimTime.Hours(2f)));

    /// <summary>파편이 지나갈 방인가 (거기로 숨거나 비켜 가지 않는다).</summary>
    public static bool InLine(Room r, World w) => w.Cosmic.Events.Any(e => e.SealPlan && !e.Avoided && e.Phase <= CosmicPhase.Impact && w.Tick < e.Arrive + SimTime.Minutes(10) && (e.TargetRoom == r.Id || e.Evac.Contains(r.Id)));

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!CosmicCrew.Free(c) || Target(c, w) is not CosmicEvent e) return (0f, "—");
        return (1.05f, $"{e.Spec.Name} 충돌 예상 — {c.Room!.Name}에서 나간다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Target(c, w) is not CosmicEvent e) return null;
        var here = c.Room!;
        foreach (var r in w.Ship.Rooms.Where(r => r != here && !r.Detached && !r.Abandoned && !r.Leaking && !InLine(r, w)).OrderBy(r => (r.Center - here.Center).LengthSquared()).ThenBy(r => r.Id))
        {
            if (CosmicCrew.SpotIn(r, c, w, dist) is not Cell at) continue;
            return new Job(this, "구획 비우기", new List<Toil> { new GotoToil(at), new WaitToil(SimTime.Minutes(5), Pose.Standing) })
            {
                LogText = $"{e.Spec.Name} — {Ko.EulReul(here.Name)} 비운다", LogKind = LogKind.Warning, TargetRoom = r, Urgent = true, InterruptMargin = 0.3f,
            };
        }
        return null;
    }
}

/// <summary>대피: 방사선 · 충격이 오기 전에 차폐된 곳(대피소 · 물벽 · 배 안쪽)으로, 지나갈 때까지.</summary>
public sealed class CosmicShelterActivity : Activity
{
    public override string Id => "cosmicshelter";
    public override string Label => "우주 재난 대피";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!CosmicCrew.Free(c)) return (0f, "—");
        var (e, urg, why) = CosmicCrew.ShelterCall(c, w);
        if (e == null) return (0f, "—");
        if (w.Cosmic.RelExposure(c.Room!) <= 0.32f && !CosmicEvacuateActivity.InLine(c.Room!, w)) return (0.9f + 0.1f * urg, $"{why} — 여기서 기다린다");
        return (0.85f + 0.3f * urg + (w.Cosmic.Follows(c, CosmicCustomKind.Drill) ? 0.1f : 0f), $"{why} — 대피");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var cs = w.Cosmic;
        var (e, _, _) = CosmicCrew.ShelterCall(c, w);
        if (e == null) return null;
        var here = c.Room!;
        if (cs.RelExposure(here) <= 0.32f && !CosmicEvacuateActivity.InLine(here, w))
            return new Job(this, "대피", new List<Toil> { new WaitToil(SimTime.Minutes(40), c.Needs.Rest < 0.35f ? Pose.Sleeping : Pose.Sitting) }) { TargetRoom = here };
        // 숨을 곳: 차폐 → 물벽 → 배 안쪽 · 너무 붐비면 다음 곳
        var order = cs.Refuges();
        order.AddRange(w.Ship.Rooms.Where(r => !order.Contains(r) && !r.Detached && !r.OffLimits && !r.Leaking && !r.Abandoned).OrderBy(cs.RelExposure).ThenBy(r => r.Id));
        foreach (var room in order)
        {
            if (cs.RelExposure(room) >= cs.RelExposure(here) - 0.1f || CosmicEvacuateActivity.InLine(room, w)) continue;
            int inside = w.Crew.Count(o => !o.Dead && o != c && (o.Room == room || o.Job?.TargetRoom == room && o.Job.Activity is CosmicShelterActivity));
            if (inside >= Math.Max(3, room.Cells.Count / 2)) continue; // 꽉 찼다
            if (CosmicCrew.SpotIn(room, c, w, dist) is not Cell at) continue;
            string where = (RoomCatalog.Tags(room.Kind) & RoomTag.Shielded) != 0 ? Ko.EuRo(room.Name) : cs.Water(room) > 0.3f ? $"물벽을 친 {Ko.EuRo(room.Name)}" : $"안쪽 {Ko.EuRo(room.Name)}";
            return new Job(this, "우주 재난 대피", new List<Toil> { new GotoToil(at), new WaitToil(SimTime.Minutes(40), Pose.Sitting) })
            {
                LogText = $"{e.Spec.Name} — {where}", LogKind = LogKind.Warning, TargetRoom = room, Urgent = true, InterruptMargin = 0.3f,
                OnFinished = (cm, world, st) => { if (st == ToilStatus.Succeeded && cm.Room == room) world.Cosmic.Stats.Sheltered++; },
            };
        }
        return null;
    }
}

/// <summary>대비 일: 물벽 · 물자 · 덮개 · 장비 끄기 · 고정 · 봉쇄 · 바깥 설비 접기 · 보온 · 손 조종 준비 · 다시 켜기.</summary>
public sealed class CosmicBraceActivity : Activity
{
    public override string Id => "cosmicbrace";
    public override string Label => "대재난 대비";

    private static IEnumerable<(CosmicEvent e, BraceTask t)> OpenTasks(CrewMember c, World w)
    {
        var cs = w.Cosmic;
        foreach (var e in cs.Events)
        {
            if (e.Phase == CosmicPhase.Done || e.Tasks.Count == 0) continue;
            bool knows = cs.Knows(c, e);
            foreach (var t in e.Tasks)
                if (cs.Open(e, t) && (knows || t.Kind == BraceKind.Restart)) yield return (e, t);
        }
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!CosmicCrew.Free(c) || c.IsChild) return (0f, "—");
        var first = OpenTasks(c, w).FirstOrDefault();
        if (first.t == null) return (0f, "—");
        var (e, t) = first;
        if (t.Kind == BraceKind.Restart) return (0.55f, $"{e.Spec.Name} 지나감 — 꺼 둔 장비를 다시 켠다");
        var cs = w.Cosmic;
        float hours = e.HoursTo(w.Tick, e.Predicted);
        float urg = Math.Clamp(1f - hours / MathF.Max(1f, cs.BraceLead(e)), 0f, 1f);
        float trust = w.Automation.Trusts.Of(c);
        float s = 0.6f + 0.22f * urg + 0.25f * (trust - 0.5f) + (c.Value == CrewValue.Rules ? 0.05f : 0f) + (cs.Follows(c, CosmicCustomKind.Drill) ? 0.1f : 0f);
        if (c.Needs.Rest < 0.25f) s -= 0.25f; // 지쳤다
        if (c.Vitals.Health < 0.5f) s -= 0.3f;
        if (t.Kind == BraceKind.Seal) s += 0.15f;
        return (s, $"{e.Spec.Name} 대비 — {t.Label} (컴퓨터를 믿는 정도 {trust * 100:0}%)");
    }

    private static Skill SkillOf(BraceKind k) => k switch
    {
        BraceKind.PowerDown or BraceKind.Restart or BraceKind.Shutters => Skill.Electrical, BraceKind.Pilot => Skill.Piloting, BraceKind.Supplies => Skill.Medicine, _ => Skill.Mechanics,
    };

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var ship = w.Ship;
        // 가까운 일부터
        (CosmicEvent e, BraceTask t, Cell at)? pick = null;
        int best = int.MaxValue;
        foreach (var (e, t) in OpenTasks(c, w))
        {
            Cell? spot = null;
            if (t.FurnitureId >= 0 && ship.Furniture.FirstOrDefault(f => f.Id == t.FurnitureId) is Furniture f) spot = CosmicCrew.SpotAt(f, c, w, dist);
            else if (t.RoomId >= 0 && t.RoomId < ship.Rooms.Count)
            {
                var room = ship.Rooms[t.RoomId];
                if (t.Kind == BraceKind.Seal)
                    spot = room.Doors.Where(d => !d.IsExternal).SelectMany(d => Cell.Dirs4.Select(dd => d.Cell + dd)).Where(x => ship.RoomAt(x) is Room o && o != room && dist.Reachable(x) && ship.IsWalkable(x)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
                else spot = CosmicCrew.SpotIn(room, c, w, dist, free: false);
            }
            if (spot is not Cell s) continue;
            int d0 = dist.Get(s) + (t.Kind == BraceKind.Seal ? -500 : 0) + (t.Last ? -200 : 0);
            if (d0 < best) { best = d0; pick = (e, t, s); }
        }
        if (pick is not { } p) return null;
        var (ev, task, cell) = p;
        task.By = c.Id;
        task.ClaimedAt = w.Tick;
        var toils = new List<Toil>();
        // 물벽 · 물자: 먼저 가지러 간다 (정수기 · 창고)
        if (task.Kind == BraceKind.WaterWall && ship.FurnitureOf(FurnitureType.WaterRecycler).FirstOrDefault() is Furniture rec && CosmicCrew.SpotAt(rec, c, w, dist) is Cell rs)
        { toils.Add(new GotoToil(rs)); toils.Add(new WorkToil(0.1f, Skill.Mechanics, rec.Center)); }
        if (task.Kind == BraceKind.Supplies && ship.Rooms.FirstOrDefault(r => !r.Detached && r.Type is RoomType.Storage or RoomType.Mess && r.Id != task.RoomId) is Room store && CosmicCrew.SpotIn(store, c, w, dist, free: false) is Cell ss)
        { toils.Add(new GotoToil(ss)); toils.Add(new WorkToil(0.08f, Skill.Mechanics, null)); }
        toils.Add(new GotoToil(cell));
        toils.Add(new WorkToil(task.Hours, SkillOf(task.Kind), null));
        toils.Add(new DoToil((cm, world) =>
        {
            var cs = world.Cosmic;
            if (!cs.Complete(ev, task, cm)) { task.By = -1; return true; }
            // 같이 대비한 사람끼리 가까워진다
            foreach (var o in world.Crew.Where(o => o != cm && !o.Dead && o.Room == cm.Room && o.Job?.Activity is CosmicBraceActivity).OrderBy(o => o.Id).Take(2))
            { cm.ChangeAffinity(o, 0.03f); o.ChangeAffinity(cm, 0.03f); }
            world.Log.Add(world.Tick, LogKind.Work, $"{ev.Spec.Name} 대비 — {task.Label}", cm.Id);
            return true;
        }));
        var tr = task.RoomId >= 0 && task.RoomId < ship.Rooms.Count ? ship.Rooms[task.RoomId] : null;
        return new Job(this, task.Label, toils)
        {
            LogText = $"{ev.Spec.Name} 대비 — {task.Label}", LogKind = LogKind.Work, TargetRoom = tr, Urgent = task.Kind == BraceKind.Seal,
            OnFinished = (cm, world, st) => { if (!task.Done && task.By == cm.Id) task.By = -1; },
        };
    }
}

/// <summary>서로 돕기: 모르는 사람에게 알리고, 바깥 쪽 방에서 자는 사람을 깨운다.</summary>
public sealed class CosmicWarnActivity : Activity
{
    public override string Id => "cosmicwarn";
    public override string Label => "알리러 가기";

    private static (CosmicEvent e, CrewMember who, bool wake)? Need(CrewMember c, World w)
    {
        var cs = w.Cosmic;
        foreach (var e in cs.Events)
        {
            // 비울 구획에서 자는 사람은 깨워서 데리고 나온다 (시간과 상관없이)
            if (e.SealPlan && !e.Sealed && !e.Avoided && w.Tick < e.Arrive && cs.Knows(c, e))
                foreach (var o in w.Crew.Where(o => o != c && !o.Dead && !o.Outside && o.Room != null && o.Pose == Pose.Sleeping && (o.Room.Id == e.TargetRoom || e.Evac.Contains(o.Room.Id))).OrderBy(o => o.Id))
                    if (!cs.WarnClaimed(o, c)) return (e, o, true);
            if (e.Phase is not (CosmicPhase.Brace or CosmicPhase.Impact) || !cs.Knows(c, e)) continue;
            long harm = cs.NextHarm(e);
            float hours = harm == long.MaxValue ? 99f : e.HoursTo(w.Tick, harm);
            if (hours > 3f) continue;
            foreach (var o in w.Crew.Where(o => o != c && !o.Dead && !o.Outside && o.Room != null).OrderBy(o => o.Id))
            {
                if (cs.WarnClaimed(o, c)) continue;
                if (!cs.Knows(o, e)) return (e, o, o.Pose == Pose.Sleeping);
                if (hours < 1.2f && o.Pose == Pose.Sleeping && CosmicCrew.NeedsShelter(e.Spec) && cs.RelExposure(o.Room!) > 0.32f) return (e, o, true);
            }
        }
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!CosmicCrew.Free(c) || c.IsChild || Need(c, w) is not { } n) return (0f, "—");
        if (!dist.Reachable(n.who.Cell)) return (0f, "닿지 않는다");
        float s = 0.72f + (c.Value == CrewValue.People ? 0.15f : 0f) + 0.15f * Math.Clamp(c.AffinityTo(n.who), 0f, 1f);
        return (s, n.wake ? $"{Ko.EulReul(n.who.Name)} 깨워야 한다 — {n.e.Spec.Name}" : $"{n.who.Name}은(는) 아직 모른다 — {n.e.Spec.Name}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Need(c, w) is not { } n) return null;
        var (e, who, wake) = n;
        w.Cosmic.ClaimWarn(who, c);
        var at = who.Cell;
        return new Job(this, wake ? "깨우러 가기" : "알리러 가기", new List<Toil>
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(1), Pose.Standing),
            new DoToil((cm, world) =>
            {
                var cs = world.Cosmic;
                cs.ReleaseWarn(who);
                if (who.Dead) return true;
                cs.Tell(who, e, cm, wake);
                return true;
            }),
        })
        {
            LogText = wake ? $"{Ko.EulReul(who.Name)} 깨우러 간다 — {e.Spec.Name}" : $"{who.Name}에게 {e.Spec.Name}을(를) 알리러 간다", LogKind = LogKind.Life, Urgent = wake,
            OnFinished = (cm, world, st) => world.Cosmic.ReleaseWarn(who),
        };
    }
}

/// <summary>창밖을 본다: 다가오는 것 · 지나간 자리 · 새 성운 (보는 사람은 알게 되고, 담대한 사람은 경이를, 겁 많은 사람은 두려움을).</summary>
public sealed class CosmicLookActivity : Activity
{
    public override string Id => "cosmiclook";
    public override string Label => "창밖 보기";

    private static Room? Window(CrewMember c, World w, DistanceField dist) =>
        w.Ship.Rooms.Where(r => !r.Detached && !r.Abandoned && !r.Leaking && w.Body.WindowsOf(r) > 0 && !w.Cosmic.Shut(r.Id) && w.Cosmic.Radiation(r) < 0.15f)
            .OrderBy(r => r.Kind == RoomType.Observatory ? 0 : 1).ThenBy(r => CosmicCrew.SpotIn(r, c, w, dist) is Cell x ? dist.Get(x) : int.MaxValue).ThenBy(r => r.Id).FirstOrDefault();

    private static (string key, string what, CosmicEvent? e)? Sight(CrewMember c, World w)
    {
        var cs = w.Cosmic;
        foreach (var e in cs.Events)
        {
            if (e.Ghost || e.Phase == CosmicPhase.Done) continue;
            bool visible = e.Phase == CosmicPhase.Impact || e.Phase <= CosmicPhase.Brace && e.HoursTo(w.Tick, e.Arrive) < 24f;
            if (!visible) continue;
            string key = $"cosmiclook:{e.Id}:{(int)e.Phase}";
            if (!cs.Looked(c, key)) return (key, e.Phase == CosmicPhase.Impact ? $"{e.Spec.Name}이(가) 창밖을 채웠다" : $"창밖에 {e.Spec.Name}의 징조", e);
        }
        foreach (var s in cs.Sky)
        {
            if (w.Tick - s.Tick > SimTime.TicksPerDay * 5) continue;
            string key = $"cosmiclook:sky:{s.EventId}";
            if (!cs.Looked(c, key)) return (key, $"창밖에 {s.Name}", null);
        }
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!CosmicCrew.Free(c) || !c.IsAwake || w.Cosmic.Events.Count == 0 || Crisis.Acting(w) || Sight(c, w) is not { } s) return (0f, "—");
        if (CosmicCrew.ShelterCall(c, w).e != null) return (0f, "숨어야 한다");
        return (0.34f + 0.15f * c.Traits.Sociability, s.what);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Sight(c, w) is not { } s || Window(c, w, dist) is not Room room || CosmicCrew.SpotIn(room, c, w, dist) is not Cell at) return null;
        return new Job(this, "창밖 보기", new List<Toil>
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(12), Pose.Standing),
            new DoToil((cm, world) =>
            {
                world.Cosmic.MarkLooked(cm, s.key);
                if (s.e != null) world.Cosmic.SawIt(cm, s.e);
                bool brave = cm.Traits.Bravery >= 0.5f;
                cm.Needs.Stress = Math.Clamp(cm.Needs.Stress + (brave ? -0.05f : 0.05f), 0f, 1f);
                if (!brave && s.e != null && cm.Room != null) Memory.Frighten(world, cm, cm.Room, 0.04f, s.what);
                world.Cosmic.Stats.Looks++;
                MarkLog.Add(cm.Memory.Marks, world.Tick, $"{s.what} — {(brave ? "아름다웠다" : "무서웠다")}");
                return true;
            }),
        }) { LogText = s.what, LogKind = LogKind.Life, TargetRoom = room };
    }
}

/// <summary>관행 — 그날의 밤: 날이 오면 저녁에 창가에 모여 그날 생긴 하늘을 본다.</summary>
public sealed class CosmicVigilActivity : Activity
{
    public override string Id => "cosmicvigil";
    public override string Label => "그날의 밤";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var cs = w.Cosmic;
        if (!CosmicCrew.Free(c) || !cs.IsVigilDay || cs.CustomOf(CosmicCustomKind.Vigil) is not CosmicCustom cu || !cu.Followers.Contains(c.Id) || cu.Kept.Contains(c.Id) || Crisis.Acting(w)) return (0f, "—");
        float h = SimTime.HourOfDay(w.Tick);
        if (h < 19f || h > 23f) return (0f, "저녁에");
        return (0.6f, $"그날의 밤 — {cu.Origin}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var room = w.Ship.Rooms.Where(r => !r.Detached && !r.Abandoned && w.Body.WindowsOf(r) > 0).OrderBy(r => r.Kind == RoomType.Observatory ? 0 : r.Type is RoomType.Lounge or RoomType.Mess ? 1 : 2).ThenBy(r => r.Id).FirstOrDefault()
                   ?? w.Ship.Rooms.FirstOrDefault(r => !r.Detached && r.Type is RoomType.Mess or RoomType.Lounge);
        if (room == null || CosmicCrew.SpotIn(room, c, w, dist) is not Cell at) return null;
        var sky = w.Cosmic.Sky.LastOrDefault();
        return new Job(this, "그날의 밤", new List<Toil>
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(30), Pose.Standing),
            new DoToil((cm, world) =>
            {
                world.Cosmic.KeepVigil(cm);
                cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.08f);
                cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.2f);
                cm.Memory.Trauma = MathF.Max(0f, cm.Memory.Trauma - 0.01f);
                MarkLog.Add(cm.Memory.Marks, world.Tick, sky != null ? $"그날의 밤 — {sky.Name}을(를) 봤다" : "그날의 밤을 지켰다");
                return true;
            }),
        }) { LogText = sky != null ? $"그날의 밤 — 창밖의 {sky.Name}" : "그날의 밤", LogKind = LogKind.Life, TargetRoom = room };
    }
}
