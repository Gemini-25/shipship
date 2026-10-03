using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.5 사고 뒤 며칠의 손질 (실제 행동): 젖은 침구 널기 · 걷기 · 냉장고 고르기 · 독서등 가져오기 · 돌려놓기 · 불탄 그림 다시 그리기,
//  그리고 식사 자리 (그을음 냄새 · 혼자 먹기 → 다른 방 · 선실 / 떠난 사람의 빈자리 앞에서 잠깐).
// 경보 · 위기 중에는 하지 않는다. 끊기면 맡은 표를 풀고 들고 있던 것은 그 자리에 둔다 (다음에 이어서).

public enum AfterTaskKind : byte { Hang, Fetch, Sort, Lamp, LampBack, Repaint }

public sealed record AfterTask(AfterTaskKind Kind, float Score, string Why, object Target, Room? Room = null, Cell Spot = default, Cell Hook = default);

/// <summary>다른 곳에서 먹는다: 어디서 · 왜.</summary>
public sealed class AwayPlan
{
    public Room Room { get; init; } = null!;
    public Room? From { get; init; }
    public Cell Spot { get; init; }
    public Vector2 Face { get; init; }
    public string Why { get; init; } = "";
    public byte Kind { get; init; } // 0 그을음 냄새 · 1 혼자
}

public sealed class AfterActivity : Activity
{
    public override string Id => "after";
    public override string Label => "사고 뒤 손질";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (AftermathSystem.Off || !AftermathSystem.Adult(c) || Crisis.Acting(w)) return (0f, "—");
        var t = w.After.Pick(c, dist);
        return t == null ? (0f, "—") : (t.Score, t.Why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var t = w.After.Pick(c, dist);
        return t == null ? null : w.After.PlanTask(c, t, dist, this);
    }
}

public sealed partial class AftermathSystem
{
    private readonly Dictionary<int, (long tick, AfterTask? task)> _pick = new();

    /// <summary>이 사람이 지금 할 사고 뒤 손질 (가장 급한 것 하나).</summary>
    public AfterTask? Pick(CrewMember c, DistanceField dist)
    {
        var w = _w;
        if (_pick.TryGetValue(c.Id, out var memo) && memo.tick == w.Tick) return memo.task;
        AfterTask? best = null;
        void Consider(AfterTask? t) { if (t != null && (best == null || t.Score > best.Score)) best = t; }
        bool shift = Activity0.IsOnShift(c, w);
        bool bed = Activity0.IsBedtime(c, w);
        // 1) 냉장고 고르기 — 요리 솜씨가 있는 사람
        foreach (var fs in Fridges)
        {
            if (fs.Done >= 0 || fs.ClaimedBy >= 0 && fs.ClaimedBy != c.Id) continue;
            float cook = c.RawSkill(Skill.Cooking);
            if (cook < 0.3f && w.Tick - fs.On < SimTime.Hours(3)) continue;
            if (w.Ship.Furniture.FirstOrDefault(f => f.Id == fs.Fridge) is not Furniture fr || fr.UseSpots.Count == 0 || !dist.Reachable(fr.UseSpots[0])) continue;
            Consider(new AfterTask(AfterTaskKind.Sort, 0.42f + 0.3f * cook, $"냉장고가 {fs.Hours:0.#}시간 멈췄었다 — 냄비를 골라야 한다", fs));
        }
        // 2) 젖은 침구 널기 · 마른 침구 걷기
        if (c.Bed is Furniture b && !b.Room.Detached && BedWet(b) > 0.3f && LineOf(b) == null && MoistureSystem.Depth(b.Room) < 0.01f && b.UseSpots.Count > 0 && dist.Reachable(b.UseSpots[0])
            && DrySpot(b, dist) is var (dr, at, hook))
            Consider(new AfterTask(AfterTaskKind.Hang, (shift ? 0.28f : 0.5f) + (bed ? 0.2f : 0f), $"침구가 젖었다 — {dr.Name}에 널어 말린다", b, dr, at, hook));
        foreach (var l in Lines)
        {
            if (l.Done || l.Dried < 0 || l.ClaimedBy >= 0 && l.ClaimedBy != c.Id) continue;
            bool mine = l.Owner == c.Id;
            bool neat = l.Forgot && c.Habits.Contains(Habit.NeatFreak);
            bool orphan = Crew(l.Owner) is not CrewMember ow || ow.Dead;
            if (!mine && !neat && !(orphan && w.Tick - l.Dried > SimTime.Hours(20))) continue;
            if (!dist.Reachable(l.At)) continue;
            Consider(new AfterTask(AfterTaskKind.Fetch, mine ? (shift ? 0.25f : 0.42f) + (bed ? 0.35f : 0f) : 0.3f, mine ? "널어 둔 침구가 말랐다" : "누가 걷지 않은 침구", l));
        }
        // 3) 독서등 가져오기 · 돌려놓기
        foreach (var lm in Lamps)
        {
            if (lm.Owner != c.Id || lm.ClaimedBy >= 0 && lm.ClaimedBy != c.Id || lm.Returned >= 0) continue;
            if (lm.Moved < 0 && w.Tick - lm.Asked < SimTime.Hours(12))
                Consider(new AfterTask(AfterTaskKind.Lamp, shift ? 0.25f : 0.46f, "작업등 불빛이 눈부시다 — 선실의 등을 가져온다", lm));
            else if (lm.Active && lm.WantBack && lm.Carrying < 0)
                Consider(new AfterTask(AfterTaskKind.LampBack, (shift ? 0.18f : 0.36f) + (bed ? 0.3f : 0f), "조명이 돌아왔다 — 등을 선실로 가져간다", lm));
        }
        // 4) 불탄 그림 다시 그리기 — 그림 그리는 사람 (만든 사람이 살아 있으면 하루는 그 사람 몫) · 통합6 쉬는 시간의 붓은 다른 그림보다 이걸 먼저 든다
        if (!shift && !bed && c.Hobbies.Contains(Hobby.Painting))
            foreach (var bp in Burned)
            {
                if (bp.Done >= 0 || bp.ClaimedBy >= 0 && bp.ClaimedBy != c.Id || w.Tick - bp.Tick < SimTime.Hours(10)) continue;
                if (RoomById(bp.Room) is not Room pr || pr.Detached || StaleSoot(pr) > 0.4f) continue;
                var maker = Crew(bp.Maker);
                if (maker != null && !maker.Dead && maker != c && w.Tick - bp.Tick < SimTime.Hours(34)) continue;
                if (WalkNear(bp.At, dist) is not Cell ps) continue;
                Consider(new AfterTask(AfterTaskKind.Repaint, 0.5f + (c.Habits.Contains(Habit.Homesick) ? 0.06f : 0f) + (maker != null && maker.Dead ? 0.08f : 0f),
                    maker != null && maker.Dead ? $"{maker.Name}의 {bp.Name} — 불에 탔다, 이어 그린다" : $"불에 탄 {Ko.EulReul(bp.Name)} 다시 그린다", bp, pr, ps));
            }
        _pick[c.Id] = (w.Tick, best);
        return best;
    }

    /// <summary>Activity의 보호된 판정 (근무 · 취침 시간)을 쓰려는 다리.</summary>
    private sealed class Activity0 : Activity
    {
        public override string Id => "after0";
        public override string Label => "";
        public override (float, string) Score(CrewMember c, World w, DistanceField dist) => (0f, "");
        public override Job? Plan(CrewMember c, World w, DistanceField dist) => null;
        public static bool IsOnShift(CrewMember c, World w) => OnShift(c, w);
        public static bool IsBedtime(CrewMember c, World w) => Bedtime(c, w);
    }

    internal Cell? WalkNear(Cell at, DistanceField dist)
    {
        var ship = _w.Ship;
        if (ship.IsWalkable(at) && ship.FurnitureAt(at) == null && dist.Reachable(at)) return at;
        foreach (var d in Cell.Dirs8)
        {
            var n = at + d;
            if (ship.Grid.InBounds(n) && ship.IsWalkable(n) && ship.FurnitureAt(n) == null && dist.Reachable(n)) return n;
        }
        foreach (var d in Cell.Dirs8)
        {
            var n = at + d;
            if (ship.Grid.InBounds(n) && dist.Reachable(n)) return n;
        }
        return null;
    }

    /// <summary>침구를 널 자리: 따뜻하고 마른 방의 벽 곁 빈칸 (세탁실 · 기관실이 좋다 · 식당 · 주방 · 의무실 · 통로는 피한다).</summary>
    public (Room room, Cell at, Cell hook)? DrySpot(Furniture bed, DistanceField dist)
    {
        var w = _w;
        (Room room, Cell at, Cell hook)? best = null;
        float bs = float.MinValue;
        foreach (var r in w.Ship.Rooms)
        {
            if (r == bed.Room || r.Detached || r.Abandoned || r.OffLimits || r.Unbreathable) continue;
            if (r.Kind is RoomType.Corridor or RoomType.Airlock or RoomType.Reactor or RoomType.Bridge or RoomType.Medbay or RoomType.Galley or RoomType.Mess
                or RoomType.Hydroponics or RoomType.Quarantine or RoomType.Morgue or RoomType.Decon or RoomType.ServerRoom or RoomType.DockingBay or RoomType.PropellantTank) continue;
            float kind = r.Kind switch { RoomType.Laundry => 0.8f, RoomType.Engine or RoomType.Power or RoomType.LifeSupport or RoomType.BatteryRoom => 0.35f, RoomType.Lounge => 0.15f, _ => 0f };
            if (Lines.Any(l => !l.Done && l.Room == r.Id)) kind += 0.3f; // 이미 널어 둔 데로
            float s = DryRate(r) + kind;
            if (s <= bs) continue;
            foreach (var cell in r.Cells)
            {
                if (!w.Ship.IsWalkable(cell) || w.Ship.FurnitureAt(cell) != null || !dist.Reachable(cell) || Lines.Any(l => !l.Done && l.At == cell)) continue;
                Cell? wall = null;
                foreach (var d in Cell.Dirs4) { var n = cell + d; if (w.Ship.Grid.InBounds(n) && !w.Ship.IsWalkable(n) && w.Ship.RoomAt(n) == null) { wall = n; break; } }
                if (wall is not Cell hk) continue;
                if (w.Ship.Doors.Any(dr => dr.Cell == cell)) continue;
                float ds = s - dist.Get(cell) / 4000f;
                if (ds <= bs) continue;
                bs = ds;
                best = (r, cell, hk);
                break;
            }
        }
        return best;
    }

    public Job? PlanTask(CrewMember c, AfterTask t, DistanceField dist, Activity act)
    {
        var w = _w;
        var toils = Plans.DropOff(c, w, dist);
        switch (t.Kind)
        {
            case AfterTaskKind.Hang when t.Target is Furniture bed && t.Room is Room dr:
            {
                var at = t.Spot;
                var hook = t.Hook;
                toils.Add(new GotoToil(bed.UseSpots[0]));
                toils.Add(new WorkToil(0.12f, Skill.Mechanics, bed.Center));
                toils.Add(new DoToil((cm, world) => { world.After.Mind(cm).CarryBundle = bed.Id; return true; }));
                toils.Add(new GotoToil(at));
                toils.Add(new WorkToil(0.15f, Skill.Mechanics, hook.Center));
                toils.Add(new DoToil((cm, world) => { world.After.Hang(cm, bed, dr, at, hook); return true; }));
                return new Job(act, "침구 널기", toils)
                {
                    LogText = $"젖은 침구를 걷어 {Ko.EuRo(dr.Name)} 가져간다", TargetRoom = dr, InterruptMargin = 0.45f,
                    OnFinished = (cm, world, st) => { var m = world.After.Mind(cm); m.CarryBundle = -1; },
                };
            }
            case AfterTaskKind.Fetch when t.Target is DryLine l:
            {
                var bedF = w.Ship.Furniture.FirstOrDefault(f => f.Id == l.Bed);
                if (bedF == null || bedF.UseSpots.Count == 0 || !dist.Reachable(l.At)) return null;
                l.ClaimedBy = c.Id;
                toils.Add(new GotoToil(l.At));
                toils.Add(new WorkToil(0.1f, Skill.Mechanics, l.Hook.Center));
                toils.Add(new DoToil((cm, world) => { world.After.Mind(cm).CarryBundle = l.Bed; return true; }));
                toils.Add(new GotoToil(bedF.UseSpots[0]));
                toils.Add(new WorkToil(0.12f, Skill.Mechanics, bedF.Center));
                toils.Add(new DoToil((cm, world) => { world.After.Fetched(cm, l, bedF); return true; }));
                return new Job(act, "침구 걷기", toils)
                {
                    LogText = l.Owner == c.Id ? "마른 침구를 걷으러 간다" : $"{Crew(l.Owner)?.Name}의 침구를 걷어 준다", InterruptMargin = 0.45f,
                    OnFinished = (cm, world, st) => { world.After.Mind(cm).CarryBundle = -1; if (!l.Done) l.ClaimedBy = -1; },
                };
            }
            case AfterTaskKind.Sort when t.Target is FridgeSort fs:
            {
                var fr = w.Ship.Furniture.First(f => f.Id == fs.Fridge);
                fs.ClaimedBy = c.Id;
                toils.Add(new GotoToil(fr.UseSpots[0]));
                toils.Add(new WorkToil(0.25f, Skill.Cooking, fr.Center));
                toils.Add(new DoToil((cm, world) => { world.After.Sort(cm, fs, fr); return true; }));
                return new Job(act, "냉장고 고르기", toils)
                {
                    LogText = "멈췄던 냉장고를 열어 냄비를 골라 본다", TargetRoom = fr.Room, InterruptMargin = 0.25f,
                    OnFinished = (cm, world, st) => { if (fs.Done < 0) fs.ClaimedBy = -1; },
                };
            }
            case AfterTaskKind.Lamp when t.Target is LampMove lm:
            {
                var prop = w.Props.Placed.FirstOrDefault(p => p.Id == lm.Prop);
                var to = RoomById(lm.To);
                if (prop == null || to == null || WalkNear(prop.At, dist) is not Cell pick || LampSpot(to, dist) is not var (spot, table)) return null;
                lm.ClaimedBy = c.Id;
                toils.Add(new GotoToil(pick));
                toils.Add(new DoToil((cm, world) => world.After.LiftLamp(cm, lm)));
                toils.Add(new GotoToil(spot));
                toils.Add(new WorkToil(0.05f, Skill.Electrical, table.Center));
                toils.Add(new DoToil((cm, world) => { world.After.SetLamp(cm, lm, to, table); return true; }));
                return new Job(act, "등 가져오기", toils)
                {
                    LogText = $"선실의 {Ko.EulReul(prop.Spec.Name)} 가지러 간다", TargetRoom = to, InterruptMargin = 0.5f,
                    OnFinished = (cm, world, st) => world.After.LampDropped(cm, lm),
                };
            }
            case AfterTaskKind.LampBack when t.Target is LampMove lb:
            {
                var prop = w.Props.Placed.FirstOrDefault(p => p.Id == lb.Prop);
                var home = RoomById(lb.From);
                if (prop == null || home == null || WalkNear(prop.At, dist) is not Cell pick) return null;
                // 제자리(침대 곁)에 못 가면 선실의 아무 빈칸에라도 (공사 중인 방)
                Cell? back0 = WalkNear(lb.Home, dist);
                if (back0 == null && c.Bed?.Room == home) foreach (var bc in c.Bed.Cells) if ((back0 = WalkNear(bc, dist)) != null) break;
                if (back0 == null) foreach (var hc in home.Cells) if (w.Ship.IsWalkable(hc) && dist.Reachable(hc)) { back0 = hc; break; }
                if (back0 is not Cell back) return null;
                lb.ClaimedBy = c.Id;
                toils.Add(new GotoToil(pick));
                toils.Add(new DoToil((cm, world) => world.After.LiftLamp(cm, lb)));
                toils.Add(new GotoToil(back));
                toils.Add(new DoToil((cm, world) => { world.After.HomeLamp(cm, lb); return true; }));
                return new Job(act, "등 돌려놓기", toils)
                {
                    LogText = $"{Ko.EulReul(prop.Spec.Name)} 선실로 가져간다", TargetRoom = home, InterruptMargin = 0.5f,
                    OnFinished = (cm, world, st) => world.After.LampDropped(cm, lb),
                };
            }
            case AfterTaskKind.Repaint when t.Target is BurnedPic bp && t.Room is Room pr:
            {
                bp.ClaimedBy = c.Id;
                float left = MathF.Max(0.2f, 1.4f * (1f - bp.Progress));
                toils.Add(new GotoToil(t.Spot));
                toils.Add(new WorkToil(left, Skill.Mechanics, bp.At.Center));
                toils.Add(new DoToil((cm, world) => { world.After.Repainted(cm, bp, pr); return true; }));
                return new Job(act, "그림 다시 그리기", toils)
                {
                    LogText = t.Why, LogKind = LogKind.Life, TargetRoom = pr, InterruptMargin = 0.15f,
                    OnFinished = (cm, world, st) => { if (bp.Done < 0) { bp.ClaimedBy = -1; if (st != ToilStatus.Succeeded) bp.Progress = MathF.Min(0.8f, bp.Progress + 0.25f); } },
                };
            }
        }
        return null;
    }

    // ── 침구 ──

    internal void Hang(CrewMember c, Furniture bed, Room room, Cell at, Cell hook)
    {
        var w = _w;
        var m = Mind(c);
        m.CarryBundle = -1;
        var owner = bed.Owner ?? w.Crew.FirstOrDefault(x => x.Bed == bed) ?? c;
        var l = new DryLine { Id = _ids++, Bed = bed.Id, Owner = owner.Id, By = c.Id, Room = room.Id, At = at, Hook = hook, Hung = w.Tick, Wet = MathF.Max(0.5f, BedWet(bed)) };
        Lines.Add(l);
        WetBeds.Remove(bed.Id);
        Stats.Hung++;
        // 빨랫줄 고리: 방마다 한 줄 (몇 개 · 누가 처음 박았나 · 어느 물난리)
        var hooks = Traces.FirstOrDefault(t => t.Kind == AfterTraceKind.DryHooks && t.Room == room.Id);
        if (hooks == null)
            AddTrace(hooks = new AfterTrace { Kind = AfterTraceKind.DryHooks, Room = room.Id, At = hook, Tick = w.Tick, Crew = c.Id, Event = $"{bed.Room.Name} 침수" });
        int n = Lines.Where(x => x.Room == room.Id).Select(x => x.Hook).Distinct().Count();
        hooks.Text = $"{room.Name} 벽의 빨랫줄 고리 {n}개 — {Ko.IGa(Crew(hooks.Crew)?.Name ?? c.Name)} {bed.Room.Name} 물난리 때 처음 박았다";
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 젖은 침구를 걷어 {room.Name} 벽에 줄을 매고 널었다 ({room.Air.Temperature:0}℃ · 습도 {room.Humidity * 100:0}%)", c.Id);
        MarkLog.Add(room.Marks, w.Tick, $"{owner.Name}의 침구를 널어 말림");
        if (Lines.Count > 30) Lines.RemoveAll(x => x.Done && w.Tick - x.Fetched > SimTime.TicksPerDay * 2);
    }

    internal void Fetched(CrewMember c, DryLine l, Furniture bed)
    {
        var w = _w;
        Mind(c).CarryBundle = -1;
        l.Fetched = w.Tick;
        WetBeds.Remove(bed.Id);
        Stats.Fetched++;
        var owner = Crew(l.Owner);
        float hours = (l.Dried - l.Hung) / (float)SimTime.TicksPerHour;
        var room = RoomById(l.Room);
        w.Log.Add(w.Tick, LogKind.Life, owner == c ? $"{Ko.IGa(c.Name)} {room?.Name}에 널어 둔 침구를 걷어 와 다시 깔았다 ({hours:0}시간 만에 말랐다)"
            : $"{Ko.IGa(c.Name)} {owner?.Name}의 침구를 걷어 침대에 깔아 주었다", c.Id);
        if (owner != null && owner != c && !owner.Dead)
        {
            owner.ChangeAffinity(c, 0.05f);
            Life.Diary(w, owner, Persona.Say(owner, $"{Ko.IGa(c.Name)} 내 침구를 걷어다 깔아 놓았다"));
        }
        else if (owner == c) Life.Diary(w, c, Persona.Say(c, "침구가 바싹 말랐다. 오늘은 제대로 자겠다"));
    }

    // ── 냉장고 ──

    internal void Sort(CrewMember c, FridgeSort fs, Furniture fr)
    {
        var w = _w;
        if (fs.Done >= 0) return;
        fs.Done = w.Tick;
        fs.By = c.Id;
        bool trust = w.Automation.MainOnline && w.Automation.Trusts.Of(c) >= 0.55f;
        var thrown = new List<string>();
        var kept = new List<string>();
        foreach (var b in w.Cooking.Batches.Where(x => x.InFridge && !x.Spoiled && !x.Jar && x.Portions > 0).OrderBy(x => x.Id).ToList())
        {
            bool comp = fs.ComputerSaysThrow.Contains(b.Id);
            float perceived = b.Fresh + (R.Float() - 0.5f) * 0.3f * (1.3f - SmellSystem.Nose(c));
            bool toss = trust ? comp || perceived < 0.45f : perceived < 0.55f;
            if (trust && comp) { fs.ByLog++; Stats.ByLog++; } else { fs.ByNose++; Stats.ByNose++; }
            if (toss)
            {
                int n = b.Portions;
                Life.Take(w, ItemKind.Meal, Math.Min(n, w.Ship.CountStored(ItemKind.Meal)));
                b.Spoiled = true;
                b.SpoiledAt = w.Tick;
                b.Portions = 0;
                w.Cooking.Discard(b.Room ?? fr.Room, c);
                fs.Thrown++;
                Stats.Thrown++;
                thrown.Add(b.Spec.Name);
            }
            else
            {
                b.Flagged = true; // 먼저 먹을 것
                fs.Kept++;
                fs.KeptIds.Add(b.Id);
                Stats.Kept++;
                kept.Add(b.Spec.Name);
            }
        }
        Stats.FridgeSorts++;
        fs.Note = $"{SimTime.Day(fs.Off)}일 정전 {fs.Hours:0}시간 — 버림 {fs.Thrown}" + (fs.Kept > 0 ? $" · 먼저 먹을 것 {fs.Kept}" : "");
        string how = fs.ByLog > fs.ByNose ? "온도 기록을 보며" : "하나씩 냄새를 맡아 보고";
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(c.Name)} 냉장고를 열어 {how} 골랐다 — " + (thrown.Count > 0 ? $"{string.Join(" · ", thrown)} 버림" : "버린 것 없음")
            + (kept.Count > 0 ? $" · {string.Join(" · ", kept)} 남김" : "") + " · 문에 쪽지를 붙였다", c.Id);
        Life.Diary(w, c, Persona.Say(c, thrown.Count > 0 ? $"정전에 냉장고가 멈췄던 탓에 {thrown.Count}냄비를 버렸다. 아깝다" : "냉장고는 다행히 버틸 만했다"));
        if (thrown.Count > 0) w.Brain2.Emotions.Feel(c, Feeling.Sadness, 0.05f, "음식을 버렸다");
        AddTrace(new AfterTrace { Kind = AfterTraceKind.FridgeNote, Room = fr.Room.Id, At = fr.Cells[0], Tick = w.Tick, Crew = c.Id, Item = fr.Id, Event = "정전", Text = $"냉장고 문의 쪽지 — '{fs.Note}' ({c.Name})" });
    }

    /// <summary>한 시간마다: 남긴 냄비가 시었나 — 고른 사람은 다음엔 온도 기록을 더 믿는다.</summary>
    private void FridgeLessons()
    {
        var w = _w;
        foreach (var fs in Fridges)
        {
            if (fs.Learned || fs.Done < 0 || w.Tick - fs.Done > SimTime.Hours(48)) continue;
            foreach (var id in fs.KeptIds)
            {
                var b = w.Cooking.Batches.FirstOrDefault(x => x.Id == id);
                if (b == null || !b.Spoiled) continue;
                fs.Learned = true;
                Stats.SourKept++;
                if (Crew(fs.By) is CrewMember by && !by.Dead)
                {
                    if (fs.ByNose >= fs.ByLog)
                    {
                        Stats.NoseLessons++;
                        w.Automation.Trusts.Change(by, 0.08f, "냄새로 남긴 냄비가 시었다 — 온도 기록이 맞았다");
                        Life.Diary(w, by, Persona.Say(by, $"남겨 둔 {Ko.IGa(b.Spec.Name)} 결국 시었다. 다음엔 컴퓨터 온도 기록을 볼 걸"));
                    }
                    w.Brain2.Emotions.Feel(by, Feeling.Shame, 0.08f, $"남긴 {Ko.IGa(b.Spec.Name)} 시었다");
                }
                break;
            }
        }
    }

    // ── 독서등 ──

    private (Cell spot, Furniture table)? LampSpot(Room to, DistanceField dist)
    {
        foreach (var f in to.Furniture.Where(f => f.Type is FurnitureType.Table or FurnitureType.GameTable or FurnitureType.Seat or FurnitureType.Bookshelf).OrderBy(f => f.Type == FurnitureType.Table ? 0 : 1).ThenBy(f => f.Id))
        {
            var spot = f.UseSpots.FirstOrDefault(dist.Reachable);
            if (spot != default || f.UseSpots.Count > 0 && dist.Reachable(f.UseSpots[0])) return (spot != default ? spot : f.UseSpots[0], f);
            if (WalkNear(f.Cells[0], dist) is Cell n) return (n, f);
        }
        return null;
    }

    internal bool LiftLamp(CrewMember c, LampMove lm)
    {
        var w = _w;
        var prop = w.Props.Placed.FirstOrDefault(p => p.Id == lm.Prop);
        if (prop == null) return false;
        if (RoomById(prop.RoomId) is Room r) r.Decor.Remove(prop);
        lm.Carrying = c.Id;
        return true;
    }

    internal void SetLamp(CrewMember c, LampMove lm, Room to, Furniture table)
    {
        var w = _w;
        var prop = w.Props.Placed.FirstOrDefault(p => p.Id == lm.Prop);
        lm.Carrying = -1;
        if (prop == null) return;
        prop.RoomId = to.Id;
        prop.At = table.Cells[0];
        if (!to.Decor.Contains(prop)) to.Decor.Add(prop);
        lm.Moved = w.Tick;
        Stats.LampMoves++;
        var su = Setup(1, to, "정전 때", lm.Id);
        su.Uses++;
        su.Days.Add(SimTime.Day(w.Tick));
        AddTrace(new AfterTrace { Kind = AfterTraceKind.PersonalLamp, Room = to.Id, At = table.Cells[0], Tick = w.Tick, Crew = c.Id, Item = prop.Id, Event = $"{to.Name} 조명 고장",
            Text = $"{to.Name} 식탁 위의 {prop.Spec.Name} — {c.Name}의 선실에서 가져온 것" });
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 선실의 {Ko.EulReul(prop.Spec.Name)} 가져와 {to.Name} {Ko.WaGwa(table.Label != "" ? table.Label : table.Name)} 위에 켰다 — 작업등 대신 노란 불빛", c.Id);
        MarkLog.Add(to.Marks, w.Tick, $"{c.Name}의 {prop.Spec.Name} (정전 때)");
        Life.Diary(w, c, Persona.Say(c, $"{to.Name}에 내 {Ko.EulReul(prop.Spec.Name)} 가져다 놓았다. 그 불빛이 훨씬 낫다"));
    }

    internal void HomeLamp(CrewMember c, LampMove lm)
    {
        var w = _w;
        var prop = w.Props.Placed.FirstOrDefault(p => p.Id == lm.Prop);
        lm.Carrying = -1;
        if (prop == null || RoomById(lm.From) is not Room home) return;
        prop.RoomId = home.Id;
        prop.At = lm.Home;
        if (!home.Decor.Contains(prop)) home.Decor.Add(prop);
        lm.Returned = w.Tick;
        Stats.LampBack++;
        foreach (var t in Traces) if (t.Kind == AfterTraceKind.PersonalLamp && t.Item == prop.Id && !t.Gone) t.Gone = true;
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {Ko.EulReul(prop.Spec.Name)} 다시 선실 침대 곁으로 가져갔다", c.Id);
    }

    internal void LampDropped(CrewMember c, LampMove lm)
    {
        if (lm.ClaimedBy == c.Id) lm.ClaimedBy = -1;
        if (lm.Carrying != c.Id) return;
        // 들고 가다 끊겼다: 그 자리에 내려놓는다 (다음에 이어서)
        var w = _w;
        var prop = w.Props.Placed.FirstOrDefault(p => p.Id == lm.Prop);
        lm.Carrying = -1;
        if (prop == null || c.Room is not Room r) return;
        prop.RoomId = r.Id;
        prop.At = c.Cell;
        if (!r.Decor.Contains(prop)) r.Decor.Add(prop);
    }

    // ── 그림 ──

    internal void Repainted(CrewMember c, BurnedPic bp, Room room)
    {
        var w = _w;
        if (bp.Done >= 0) return;
        var maker = Crew(bp.Maker);
        bool forDead = maker != null && maker.Dead;
        string keep = forDead ? $"{Ko.IGa(maker!.Name)} 그리던 것을 이어 그림 — 서명 둘"
            : c.Habits.Contains(Habit.Perfectionist) ? "그을린 액자틀을 그대로 씀"
            : c.Habits.Contains(Habit.Homesick) || c.Habits.Contains(Habit.Collector) ? "타다 남은 귀퉁이를 붙여 넣음"
            : "모서리 그을음을 일부러 남김";
        Life.Take(w, ItemKind.Paint, 1);
        var spec = Props.Get(bp.Spec);
        var placed = w.Props.Place(spec, room, c, $"다시 그림 ({SimTime.Day(bp.Tick)}일 불)", keep, near: bp.At) ?? w.Props.Place(spec, room, c, $"다시 그림 ({SimTime.Day(bp.Tick)}일 불)", keep);
        if (placed == null) return;
        bp.Done = w.Tick;
        Repaints.Add(new Repaint { Prop = placed.Id, Room = room.Id, At = placed.At, Painter = c.Id, Maker = bp.Maker, ForDead = forDead, Keep = keep, Tick = w.Tick, Name = spec.Name });
        Stats.Repainted++;
        if (forDead) Stats.ForDead++;
        AddTrace(new AfterTrace { Kind = AfterTraceKind.Repainted, Room = room.Id, At = placed.At, Tick = w.Tick, Crew = forDead ? bp.Maker : c.Id, Item = placed.Id, Event = $"{room.Name} 화재",
            Text = $"{room.Name}의 {spec.Name} — {Ko.IGa(c.Name)} 다시 그렸다 ({keep})" });
        w.Brain2.Emotions.Feel(c, Feeling.Pride, 0.15f, $"{Ko.EulReul(spec.Name)} 다시 그렸다");
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 불에 탄 {Ko.EulReul(spec.Name)} 다시 그려 {room.Name}에 걸었다 — {keep}", c.Id);
        w.History.Add(w, HistoryKind.Memory, $"{room.Name}의 {Ko.EulReul(spec.Name)} {Ko.IGa(c.Name)} 다시 그렸다 — {keep}", room, forDead ? new[] { c, maker! } : new[] { c }, log: false);
        Life.Diary(w, c, Persona.Say(c, forDead ? $"{Ko.IGa(maker!.Name)} 그리던 그림을 이어 그렸다. 구석에 그 사람 서명을 남겼다" : $"불에 탄 {Ko.EulReul(spec.Name)} 다시 그렸다. 그을린 자국은 지우지 않았다"));
        foreach (var o in w.Crew)
            if (!o.Dead && o != c && o.Room == room && o.IsAwake) { w.Brain2.Emotions.Feel(o, Feeling.Joy, 0.06f, $"다시 걸린 {spec.Name}"); if (forDead) w.Brain2.Emotions.Feel(o, Feeling.Sadness, 0.04f, maker!.Name, maker); }
    }

    // ───────────────────────────── 식사 자리 (EatActivity 훅) ─────────────────────────────

    /// <summary>다른 곳에서 먹는다: 혼자이고 싶은 사람은 선실에서 · 식당에 묵은 그을음 냄새가 나면 다른 방에서.</summary>
    public AwayPlan? EatAway(CrewMember c, Furniture? seat, DistanceField dist)
    {
        if (Off || c.IsChild) return null;
        var w = _w;
        var m = Peek(c);
        if (m is { Stage: 2 } && c.Bed is Furniture bed && !bed.Room.Detached && bed.UseSpots.Count > 0 && dist.Reachable(bed.UseSpots[0]))
            return new AwayPlan { Room = bed.Room, Spot = bed.UseSpots[0], Face = bed.Center, Why = m.WithdrawWhy ?? "", Kind = 1 };
        Room? mess = seat?.Room;
        if (mess == null) foreach (var r in w.Ship.RoomsOf(RoomType.Mess)) if (!r.OffLimits) { mess = r; break; }
        if (mess == null || SootFor(c, mess) < SootAvoid) return null;
        return AltDining(c, mess, dist);
    }

    internal AwayPlan? AltDining(CrewMember c, Room mess, DistanceField dist, string why = "그을음 냄새로", Func<Room, bool>? ok = null) // v18.3 하수 냄새도
    {
        var w = _w;
        int Pref(Room r) => Setups.Any(s => s.Kind == 0 && s.Room == r.Id && s.Plan != -2 && w.Tick - s.Last < SimTime.TicksPerDay) ? 0
            : r.Kind switch { RoomType.Lounge => 1, RoomType.Observatory => 2, RoomType.Chapel => 3, RoomType.Hydroponics => 4, _ => 9 };
        foreach (var r in w.Ship.Rooms.Where(r => r != mess && !r.Detached && !r.Abandoned && !r.OffLimits && !r.Unbreathable && Pref(r) < 9 && SootFor(c, r) < SootAvoid * 0.6f && (ok == null || ok(r)))
                     .OrderBy(Pref).ThenBy(r => r.Id))
        {
            var face = r.Furniture.Where(f => f.Type is FurnitureType.Table or FurnitureType.GameTable).OrderBy(f => f.Id).Select(f => (Vector2?)f.Center).FirstOrDefault()
                       ?? (r.Cells.Count > 0 ? r.Cells[r.Cells.Count / 2].Center : Vector2.Zero);
            foreach (var f in r.Furniture.Where(f => f.Type is FurnitureType.Seat or FurnitureType.GameTable or FurnitureType.Table && f.UseSpots.Count > 0).OrderBy(f => f.Type == FurnitureType.Seat ? 0 : 1).ThenBy(f => f.Id))
                foreach (var s in f.UseSpots)
                    if (dist.Reachable(s) && !w.IsSpotTaken(s, c) && (f.ReservedBy == null || f.ReservedBy == c))
                        return new AwayPlan { Room = r, From = mess, Spot = s, Face = face, Why = why, Kind = 0 };
            Cell? best = null;
            float bd = float.MaxValue;
            foreach (var cell in r.Cells)
            {
                if (!w.Ship.IsWalkable(cell) || w.Ship.FurnitureAt(cell) != null || !dist.Reachable(cell) || w.IsSpotTaken(cell, c)) continue;
                float d = (cell.Center - face).LengthSquared();
                if (d < bd) { bd = d; best = cell; }
            }
            if (best is Cell b) return new AwayPlan { Room = r, From = mess, Spot = b, Face = face, Why = why, Kind = 0 };
        }
        if (c.Bed is Furniture bed && !bed.Room.Detached && bed.Room != mess && bed.UseSpots.Count > 0 && dist.Reachable(bed.UseSpots[0]))
            return new AwayPlan { Room = bed.Room, From = mess, Spot = bed.UseSpots[0], Face = bed.Center, Why = why, Kind = 0 };
        return null;
    }

    public List<Toil> AwayToils(AwayPlan p) => new()
    {
        new GotoToil(p.Spot),
        new DoToil((cm, world) => { world.After.AteAway(cm, p); return true; }),
    };

    internal void AteAway(CrewMember c, AwayPlan p)
    {
        var w = _w;
        var m = Mind(c);
        m.AwayMeals++;
        m.AwayLast = w.Tick;
        Stats.AwayMeals++;
        AwayPlates.Add((w.Tick, p.Spot, c.Id));
        if (AwayPlates.Count > 40) AwayPlates.RemoveAt(0);
        if (p.Kind == 1)
        {
            Stats.Alone++;
            if (m.AteStage != 2)
            {
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.EunNeun(c.Name)} {m.WithdrawWhy ?? ""} 식당에 가지 않고 선실에서 혼자 먹는다".Replace("  ", " "), c.Id);
                Life.Diary(w, c, Persona.Say(c, "식당에 갈 기분이 아니었다. 침대에 걸터앉아 먹었다"));
            }
            m.AteStage = 2;
            return;
        }
        if (m.AwayWhy != "soot")
        {
            Stats.AwayEaters++;
            m.AwayFirst = w.Tick;
            w.Log.Add(w.Tick, LogKind.Life, $"{p.From?.Name ?? "식당"}에 {(p.Why == "하수 냄새로" ? "하수 냄새가 퍼져" : "그을음 냄새가 남아")} {Ko.IGa(c.Name)} 접시를 들고 {Ko.EuRo(p.Room.Name)} 갔다", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"{Ko.EunNeun(p.From?.Name ?? "식당")} {(p.Why == "하수 냄새로" ? "하수 냄새가 진동한다" : "아직 탄내가 난다")}. {p.Room.Name}에서 먹었다"));
        }
        m.AwayWhy = "soot";
        m.AwayRoom = p.Room.Id;
        m.AwayFrom = p.From?.Id ?? -1;
        if (p.Room.Kind != RoomType.Quarters || c.Bed?.Room != p.Room)
        {
            var su = Setup(0, p.Room, p.Why);
            su.Uses++;
            su.Last = w.Tick;
            su.Days.Add(SimTime.Day(w.Tick));
            su.Users[c.Id] = su.Users.GetValueOrDefault(c.Id) + 1;
        }
    }
}
