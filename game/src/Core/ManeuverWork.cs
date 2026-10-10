using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.0 기동 준비 · 버티기 · 뒤처리 — 사람이 직접 걸어가 손으로 한다.
//  준비(몇 분짜리, 기동이 오기 전): 짐 내려놓기 · 냄비 집게 · 히터 끄기 · 컵 쥐기 · 선반 걸쇠 · 카트 끈 · 깨지기 쉬운 것 묶기 → 앉기 / 손잡이.
//  넘어진 사람은 잠깐 주저앉았다 일어난다.
//  뒤처리: 냄비 집게 풀기(데우다 만 수프는 이름표 접시로) · 빗자루로 조각 쓸기(밥 먹은 사람이 먼저) · 떨어진 것 주워 제자리 ·
//   넘어진 히터 · 굴러간 카트 제자리 · 꺼 둔 히터 다시 켜기 · 걸쇠 점검 · 관행(쓰고 난 선반 걸쇠) · 화물 옮기기(무게중심).

public enum AfterKind : byte { None, Warm, Unclamp, Sweep, Pickup, Right, HeaterOn, Check, HabitLatch, Rebalance }

public sealed partial class ManeuverSystem
{
    /// <summary>쥐고 있는 컵 · 짐을 내려놓은 사람 · 묶어 둔 물건.</summary>
    public SortedSet<int> HeldCups { get; } = new();
    public SortedSet<int> Lowered { get; } = new();
    public SortedSet<int> Tied { get; } = new();
    /// <summary>앉아서 버티는 사람 (자리).</summary>
    public SortedDictionary<int, Cell> Seated { get; } = new();

    internal List<PrepTask>? PrepOf(CrewMember c) => Preps.TryGetValue(c.Id, out var l) && l.Any(x => !x.Done) ? l : null;

    internal void ApplyPrep(CrewMember c, PrepTask t)
    {
        var w = _w;
        if (t.Done) return;
        t.Done = true;
        var room = c.Room;
        switch (t.Kind)
        {
            case PrepKind.SetDown:
                foreach (var d in w.Portable.Devices.Where(d => d.HeldBy == c).ToList()) { d.HeldBy = null; d.At = c.Cell; d.Stored = false; d.PlacedSince = w.Tick; }
                if (c.Carrying is ItemStack st && st.Count >= 2 && room != null && room.Furniture.FirstOrDefault(f => f.Storage is Inventory inv && inv.Free >= st.Count && inv.Accepts(st.Kind)) is Furniture box)
                { box.Storage!.Add(st.Kind, st.Count); c.Carrying = null; }
                Lowered.Add(c.Id);
                Stats.SetDown++;
                MarkLog.Add(c.Memory.Marks, w.Tick, "기동 전에 들고 있던 짐을 내려놓았다");
                break;
            case PrepKind.ClampPot:
                if (w.Ship.Furniture.FirstOrDefault(f => f.Id == t.Target) is not Furniture stove) break;
                Clamped.Add(stove.Id);
                ClampedBy[stove.Id] = c.Id;
                foreach (var x in Warming) if (x.Stove == stove.Id && !x.Stopped) { x.Kept = true; x.KeptBy = c.Id; }
                Stats.Clamped++;
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 화구 불을 끄고 냄비에 집게를 물렸다", c.Id);
                break;
            case PrepKind.HeaterOff:
                if (w.Portable.Devices.FirstOrDefault(d => d.Id == t.Target) is not PortableDevice h || !h.On) break;
                h.On = false;
                HeaterOffBy[h.Id] = c.Id;
                Stats.HeatersOff++;
                if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{Ko.IGa(c.Name)} 기동 전에 이동식 히터를 껐다");
                break;
            case PrepKind.HoldCup:
                if (!w.Info.OnTable.ContainsKey(t.Target)) break;
                HeldCups.Add(t.Target);
                Stats.CupsHeld++;
                break;
            case PrepKind.LatchShelf:
                if (Latched.Add(t.Target)) Stats.Latched++;
                break;
            case PrepKind.StrapCart:
                if (Strapped.Add(t.Target)) Stats.Strapped++;
                break;
            case PrepKind.TieDown:
                if (w.Matter.Get(t.Target) is Article a && a.Loose) { a.Fixed = true; Tied.Add(a.Id); Stats.TiedDown++; }
                break;
        }
        foreach (var k in new[] { 1, 2, 3, 4, 5, 6 }) Unclaim(k, t.Target);
    }

    /// <summary>앉을 자리 (가까운 의자 · 식탁 · 침대 · 콘솔 — 비어 있는) 아니면 손잡이 (벽 곁 칸).</summary>
    internal (Cell spot, bool seat, Cell wall, Vector2 face)? BraceSpot(CrewMember c, Cell from)
    {
        var w = _w;
        if (w.Ship.RoomAt(from) is not Room room) return null;
        Cell? best = null; Vector2 face = default; float bd = 4.6f * 4.6f;
        foreach (var f in room.Furniture)
        {
            if (f.Type is not (FurnitureType.Seat or FurnitureType.Table or FurnitureType.Console or FurnitureType.Bed or FurnitureType.Cot or FurnitureType.GameTable or FurnitureType.MedBed)) continue;
            foreach (var s in f.UseSpots)
            {
                float d = (s.Center - from.Center).LengthSquared();
                if (d >= bd || !w.Ship.IsWalkable(s) || w.IsSpotTaken(s, c) || Seated.ContainsValue(s)) continue;
                bd = d; best = s; face = f.Center;
            }
        }
        if (best is Cell seat) return (seat, true, default, face);
        // 손잡이: 벽 곁 칸 (가까운 것)
        Cell? hold = null; Cell wall = default; float hd = float.MaxValue;
        foreach (var cell in room.Cells)
        {
            float d = (cell.Center - from.Center).LengthSquared();
            if (d >= hd || !w.Ship.IsOpenFloor(cell) || Grips.Values.Any(g => g.at == cell)) continue;
            foreach (var dd in Cell.Dirs4)
                if (w.Ship.Grid.Kind(cell + dd) == TileKind.Wall) { hold = cell; wall = cell + dd; hd = d; break; }
        }
        if (hold is Cell h) return (h, false, wall, wall.Center);
        return (c.Cell, false, c.Cell, c.Position);
    }

    // ── 뒤처리 고르기 ──

    internal (AfterKind kind, float score, string why, int target) PickAfter(CrewMember c, DistanceField dist)
    {
        var w = _w;
        if (Active || c.IsChild || c.Room is not Room here) return (AfterKind.None, 0f, "", -1);
        // 데우는 냄비 지키기 (조리사)
        foreach (var x in Warming)
            if (x.Cook == c.Id && !x.Stopped && x.Plate < 0 && !Clamped.Contains(x.Stove))
                return (AfterKind.Warm, 0.95f, $"{CrewOf(x.For)?.Name} 몫 수프를 데운다", x.Stove);
        // 집게 풀기 (물린 사람이 먼저 · 30분 지나면 요리할 줄 아는 아무나)
        foreach (var (st, by) in ClampedBy)
            if (by == c.Id || c.SkillLevel(Skill.Cooking) >= 0.3f && Recent.Count > 0 && w.Tick - Recent[^1].End > SimTime.Minutes(30))
                if (!Claimed(7, st, c)) return (AfterKind.Unclamp, by == c.Id ? 0.9f : 0.5f, "기동이 끝났다 — 냄비 집게를 푼다", st);
        // 빗자루: 밥을 다 먹은 사람이 먼저 (식탁 밑 조각)
        if (Shards.Count > 0 && BroomBy < 0 && Broom() is Cell broom && dist.Reachable(broom))
        {
            bool ate = c.LastActivityId == "eat";
            foreach (var s in Shards)
            {
                if (s.ClaimedBy >= 0 || w.Body.Mark(s.At, CellMark.Glass) < 0.2f) continue;
                var r = w.Ship.RoomAt(s.At);
                bool near = r == here || r != null && here.Doors.Any(d => d.RoomA == r || d.RoomB == r);
                if (!near && !ate) continue;
                float sc = ate && near ? 0.95f : Life.Has(c, Habit.NeatFreak) ? 0.5f : w.Tick - s.Tick > SimTime.Hours(2) ? 0.35f : 0f;
                if (sc <= 0f || Beside(s.At, c) is not Cell sp || !dist.Reachable(sp)) continue;
                return (AfterKind.Sweep, sc, ate ? $"다 먹고 보니 바닥에 깨진 {s.What} 조각 — 빗자루를 가져온다" : $"깨진 {s.What} 조각이 그대로다", Shards.IndexOf(s));
            }
        }
        // 떨어진 것 줍기 · 넘어진 히터 · 굴러간 카트
        foreach (var t in Fallen)
        {
            if (t.Broken || t.ClaimedBy >= 0 || t.RoomId != here.Id || Beside(t.At, c) is not Cell sp || !dist.Reachable(sp)) continue;
            return (AfterKind.Pickup, 0.45f + (Life.Has(c, Habit.NeatFreak) ? 0.15f : 0f) - (Life.Has(c, Habit.Messy) ? 0.25f : 0f), $"떨어진 {Name(t.Kind)} — 주워 제자리에", t.Id);
        }
        foreach (var (id, _) in Tipped.Concat(Rolled))
            if (w.Portable.Devices.FirstOrDefault(d => d.Id == id) is PortableDevice d && d.Placed && w.Ship.RoomAt(d.At) == here && !Claimed(8, id, c))
                return (AfterKind.Right, 0.5f, d.Kind == PortableKind.Cart ? "굴러간 카트를 제자리로" : "넘어진 히터를 세운다", id);
        // 꺼 둔 히터 (끈 사람이 · 방이 아직 추우면)
        foreach (var (id, by) in HeaterOffBy)
            if (by == c.Id && w.Portable.Devices.FirstOrDefault(d => d.Id == id) is PortableDevice h && h.Placed && !Tipped.ContainsKey(id)
                && (w.Ship.RoomAt(h.At)?.Air.Temperature ?? 20f) < 20f && !(Life.Has(c, Habit.Forgetful) && (c.Id + id) % 2 == 0))
                return (AfterKind.HeaterOn, 0.5f, "기동 전에 꺼 둔 히터를 다시 켠다", id);
        // 걸쇠 · 끈 점검 (손재주 있는 사람)
        if (CheckRooms.Count > 0 && c.SkillLevel(Skill.Mechanics) >= 0.35f)
            foreach (var rid in CheckRooms)
                if (!Claimed(9, rid, c) && rid < w.Ship.Rooms.Count && !w.Ship.Rooms[rid].Detached && dist.Reachable(w.Ship.Rooms[rid].Cells[0]))
                    return (AfterKind.Check, 0.5f + 0.1f * c.Traits.Diligence, $"{w.Ship.Rooms[rid].Name} 걸쇠 · 끈 점검", rid);
        // 관행: 쓰고 난 선반은 걸쇠를 건다
        if (w.Culture.Follows(c, CustomKind.StowAway) && !Life.Has(c, Habit.Messy))
            foreach (var f in here.Furniture)
                if (Shelfish(f.Type) && !Latched.Contains(f.Id) && !Claimed(4, f.Id, c))
                    return (AfterKind.HabitLatch, 0.3f, $"{f.Label} 걸쇠 (늘 그렇게 한다)", f.Id);
        // 화물 옮기기 (컴퓨터가 계산해 부탁한 것)
        if (Rebalance is var (from, to, _) && !Claimed(10, from, c) && c.SkillLevel(Skill.Mechanics) >= 0.2f && !c.IsChild)
            return (AfterKind.Rebalance, 0.42f, "컴퓨터 말대로 짐을 옮긴다 (배 무게중심)", from);
        return (AfterKind.None, 0f, "", -1);
    }
}

public sealed class ManeuverActivity : Activity
{
    public override string Id => "maneuver";
    public override string Label => "기동 대비";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var ms = w.Maneuver;
        if (ManeuverSystem.Off || !c.CanAct || c.Outside || c.Room == null || c.CarriedBy != null) return (0f, "—");
        if (ms.Down(c) is Tumble t) return (3f, t.FromBed ? "침대에서 굴러떨어졌다" : "넘어졌다 — 정신을 차린다");
        if (ms.Active)
        {
            if (ms.PrepOf(c) is { } list) return (2.2f, $"{ms.Pending!.Talk} 준비 — {Say(list.First(x => !x.Done).Kind)}");
            if (c.Job?.Activity is ManeuverActivity && ms.Pending!.Knew.Contains(c.Id)) return (2.2f, "붙잡고 버틴다");
            return (0f, "—");
        }
        var (kind, s, why, _) = ms.PickAfter(c, dist);
        return kind == AfterKind.None ? (0f, "—") : (s, why);
    }

    private static string Say(PrepKind k) => k switch
    {
        PrepKind.ClampPot => "냄비 집게", PrepKind.HeaterOff => "히터 끄기", PrepKind.SetDown => "짐 내려놓기", PrepKind.HoldCup => "컵 쥐기",
        PrepKind.LatchShelf => "선반 걸쇠", PrepKind.StrapCart => "카트 끈", PrepKind.TieDown => "깨질 것 묶기", _ => "앉거나 손잡이",
    };

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var ms = w.Maneuver;
        if (ms.Down(c) is Tumble t)
        {
            var up = new List<Toil>
            {
                new WaitToil(SimTime.Minutes(t.Hurt ? 2.5f : 1.2f), Pose.Sitting, c.Position - t.Dir),
                new DoToil((cm, world) => { t.Up = true; return true; }),
            };
            return new Job(this, t.FromBed ? "바닥에서 일어난다" : "주저앉았다 일어난다", up) { InterruptMargin = 0.9f, OnFinished = (cm, world, _) => t.Up = true };
        }
        if (ms.Active && ms.PrepOf(c) is { } list) return Prep(c, w, dist, list);
        var (kind, _, why, target) = ms.PickAfter(c, dist);
        return kind switch
        {
            AfterKind.Warm => Warm(c, w, target),
            AfterKind.Unclamp => Unclamp(c, w, target),
            AfterKind.Sweep => Sweep(c, w, dist, target),
            AfterKind.Pickup => Pickup(c, w, dist, target),
            AfterKind.Right => Right(c, w, target),
            AfterKind.HeaterOn => HeaterOn(c, w, target),
            AfterKind.Check => Check(c, w, target),
            AfterKind.HabitLatch => HabitLatch(c, w, target),
            AfterKind.Rebalance => Rebalance(c, w),
            _ => null,
        };
    }

    private Job Prep(CrewMember c, World w, DistanceField dist, List<PrepTask> list)
    {
        var ms = w.Maneuver;
        var m = ms.Pending!;
        var toils = new List<Toil>();
        bool Gone(World world) => world.Maneuver.Pending != m || m.Over;
        foreach (var t in list.Where(x => !x.Done && x.Kind != PrepKind.Brace))
        {
            if (t.Kind != PrepKind.SetDown) toils.Add(new GotoToil(t.Spot));
            float h = t.Kind switch { PrepKind.ClampPot => 0.012f, PrepKind.LatchShelf => 0.01f, PrepKind.StrapCart => 0.018f, PrepKind.TieDown => 0.01f, _ => 0.004f };
            var skill = t.Kind switch { PrepKind.ClampPot => Skill.Cooking, PrepKind.HeaterOff => Skill.Electrical, _ => Skill.Mechanics };
            toils.Add(new WorkToil(h, skill, t.Face) { CanContinue = (cm, world) => !Gone(world) && !m.Hit });
            toils.Add(new DoToil((cm, world) => { world.Maneuver.ApplyPrep(cm, t); return true; }));
        }
        // 버티기: 앉을 자리 · 손잡이 (자리는 갈 때 고른다 — 그새 누가 앉았을 수 있다)
        var last = list.LastOrDefault(x => !x.Done && x.Kind != PrepKind.Brace && x.Kind != PrepKind.SetDown)?.Spot ?? c.Cell;
        var b = ms.BraceSpot(c, last);
        if (b is { } bs && dist.Reachable(bs.spot)) toils.Add(new GotoToil(bs.spot));
        toils.Add(new DoToil((cm, world) =>
        {
            var ms2 = world.Maneuver;
            foreach (var x in list) if (x.Kind == PrepKind.Brace) x.Done = true;
            if (b is not { } bb) return true;
            if (bb.seat) { ms2.Seated[cm.Id] = bb.spot; ms2.Stats.Sat++; }
            else { ms2.Grips[cm.Id] = (bb.spot, bb.wall); ms2.Stats.Gripped++; }
            return true;
        }));
        toils.Add(new WaitToil(SimTime.Minutes(12), Pose.Standing) { DoneWhen = (cm, world) => Gone(world), EveryTick = (cm, world) => { if (b is { seat: true } && cm.Pose != Pose.Sitting) { cm.Pose = Pose.Sitting; Locomotion.Face(cm, b.Value.face); } } });
        return new Job(this, $"{m.Talk} 준비", toils)
        {
            LogText = $"{m.Talk} 준비 — {string.Join(" · ", list.Select(x => Say(x.Kind)).Distinct())}", LogKind = LogKind.Work, InterruptMargin = 0.8f,
            OnFinished = (cm, world, _) => { world.Maneuver.Grips.Remove(cm.Id); world.Maneuver.Seated.Remove(cm.Id); },
        };
    }

    private Job? Warm(CrewMember c, World w, int stoveId)
    {
        var ms = w.Maneuver;
        var x = ms.Warming.FirstOrDefault(y => y.Stove == stoveId && !y.Stopped);
        var st = w.Ship.Furniture.FirstOrDefault(f => f.Id == stoveId);
        if (x == null || st == null || ms.Near(st, c) is not Cell spot) return null;
        var toils = new List<Toil>
        {
            new GotoToil(spot),
            new WaitToil(SimTime.Minutes(40), Pose.Working, st.Center) { DoneWhen = (cm, world) => x.Stopped || x.Plate >= 0 || world.Maneuver.Active || world.Maneuver.Clamped.Contains(stoveId) },
        };
        var who = w.Crew.FirstOrDefault(y => y.Id == x.For);
        return new Job(this, "수프 데우기", toils) { LogText = $"{who?.Name} 몫 수프를 화구에 올려 데운다", TargetRoom = st.Room, Target = st };
    }

    private Job? Unclamp(CrewMember c, World w, int stoveId)
    {
        var ms = w.Maneuver;
        var st = w.Ship.Furniture.FirstOrDefault(f => f.Id == stoveId);
        if (st == null || ms.Near(st, c) is not Cell spot) return null;
        ms.Claim(7, stoveId, c);
        var toils = new List<Toil>
        {
            new GotoToil(spot),
            new WorkToil(0.01f, Skill.Cooking, st.Center),
            new DoToil((cm, world) =>
            {
                var m2 = world.Maneuver;
                m2.Clamped.Remove(stoveId); m2.ClampedBy.Remove(stoveId);
                m2.Stats.Unclamped++;
                var x = m2.Warming.LastOrDefault(y => y.Stove == stoveId && y.Kept && y.Plate < 0);
                var b = x != null ? world.Cooking.Batches.FirstOrDefault(y => y.Id == x.Batch) : null;
                if (x != null && b != null && b.Portions > 0 && m2.Serve(x, cm, b))
                {
                    m2.Stats.PlatesKept++;
                    var who = world.Crew.FirstOrDefault(y => y.Id == x.For);
                    world.Log.Add(world.Tick, LogKind.Life, $"{Ko.IGa(cm.Name)} 기동이 끝나 냄비 집게를 풀었다 — 데우다 만 {Ko.EulReul(b.Spec.Name)} {who?.Name} 몫으로 덜어 이름표를 붙였다 (불은 다시 안 켰다)", cm.Id);
                }
                else world.Log.Add(world.Tick, LogKind.Life, $"{Ko.IGa(cm.Name)} 냄비 집게를 풀었다", cm.Id);
                return true;
            }),
        };
        return new Job(this, "냄비 집게 풀기", toils) { TargetRoom = st.Room, Target = st, OnFinished = (cm, world, _) => world.Maneuver.Unclaim(7, stoveId) };
    }

    private Job? Sweep(CrewMember c, World w, DistanceField dist, int idx)
    {
        var ms = w.Maneuver;
        if (idx < 0 || idx >= ms.Shards.Count || ms.Broom() is not Cell broom) return null;
        var s = ms.Shards[idx];
        if (ms.Beside(s.At, c) is not Cell spot) return null;
        bool ate = c.LastActivityId == "eat";
        s.ClaimedBy = c.Id;
        ms.BroomBy = c.Id;
        var toils = new List<Toil>
        {
            new GotoToil(broom),
            new WorkToil(0.003f, Skill.Mechanics, broom.Center),
            new GotoToil(spot),
            new WorkToil(0.05f, Skill.Mechanics, s.At.Center),
            new DoToil((cm, world) =>
            {
                var m2 = world.Maneuver;
                int n = 0;
                foreach (var x in m2.Shards.Where(x => (x.At.Center - s.At.Center).LengthSquared() <= 2.1f).ToList())
                {
                    world.Body.SetMark(x.At, CellMark.Glass, 0f, "");
                    n++;
                }
                m2.Stats.Swept += n;
                world.Log.Add(world.Tick, LogKind.Life, ate ? $"{Ko.IGa(cm.Name)} 밥을 다 먹고 빗자루로 깨진 {s.What} 조각을 쓸어 담았다" : $"{Ko.IGa(cm.Name)} 빗자루로 깨진 {s.What} 조각을 쓸어 담았다", cm.Id);
                MarkLog.Add(cm.Memory.Marks, world.Tick, $"식탁 밑 깨진 {s.What} 조각을 쓸었다");
                if (s.Cup >= 0 && world.Belongings.Get(s.Cup) is Belonging cup && cup.Owner != cm.Id && world.Crew.FirstOrDefault(o => o.Id == cup.Owner) is CrewMember owner && !owner.Dead)
                    owner.ChangeAffinity(cm, 0.02f);
                return true;
            }),
            new GotoToil(broom),
            new DoToil((cm, world) => { world.Maneuver.BroomBy = -1; return true; }),
        };
        return new Job(this, "빗자루질", toils)
        {
            LogText = ate ? "밥을 다 먹고 빗자루를 가져온다" : "빗자루를 가져온다",
            OnFinished = (cm, world, _) => { if (world.Maneuver.BroomBy == cm.Id) world.Maneuver.BroomBy = -1; if (s.ClaimedBy == cm.Id) s.ClaimedBy = -1; },
        };
    }

    private Job? Pickup(CrewMember c, World w, DistanceField dist, int id)
    {
        var ms = w.Maneuver;
        var first = ms.Fallen.FirstOrDefault(t => t.Id == id);
        if (first == null) return null;
        var picks = ms.Fallen.Where(t => !t.Broken && t.ClaimedBy < 0 && t.RoomId == first.RoomId && t.From == first.From).Take(3).ToList();
        var shelf = w.Ship.Furniture.FirstOrDefault(f => f.Id == first.From);
        var toils = new List<Toil>();
        foreach (var t in picks)
        {
            t.ClaimedBy = c.Id;
            if (ms.Beside(t.At, c) is not Cell sp) continue;
            toils.Add(new GotoToil(sp));
            toils.Add(new WorkToil(0.004f, Skill.Mechanics, t.At.Center));
            toils.Add(new DoToil((cm, world) => { world.Maneuver.Fallen.Remove(t); world.Maneuver.Stats.Picked++; return true; }));
        }
        if (shelf != null && ms.Near(shelf, c) is Cell back)
        {
            toils.Add(new GotoToil(back));
            toils.Add(new WorkToil(0.01f, Skill.Mechanics, shelf.Center));
            toils.Add(new DoToil((cm, world) => { MarkLog.Add(shelf.Room.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 떨어진 것을 주워 {shelf.Label}에 도로 올렸다"); return true; }));
        }
        return new Job(this, "떨어진 것 줍기", toils)
        {
            LogText = $"떨어진 {ManeuverSystem.Name(first.Kind)}{(picks.Count > 1 ? $" 등 {picks.Count}개" : "")}를 주워 제자리에",
            OnFinished = (cm, world, _) => { foreach (var t in picks) if (t.ClaimedBy == cm.Id) t.ClaimedBy = -1; },
        };
    }

    private Job? Right(CrewMember c, World w, int id)
    {
        var ms = w.Maneuver;
        var d = w.Portable.Devices.FirstOrDefault(x => x.Id == id);
        if (d == null || ms.Beside(d.At, c) is not Cell sp) return null;
        ms.Claim(8, id, c);
        bool cart = d.Kind == PortableKind.Cart;
        var home = ms.Rolled.TryGetValue(id, out var h) ? h : d.At;
        var toils = new List<Toil> { new GotoToil(sp), new WorkToil(0.01f, Skill.Mechanics, d.At.Center) };
        if (cart && home != d.At && ms.Beside(home, c) is Cell hs)
        {
            toils.Add(new DoToil((cm, world) => true));
            toils.Add(new GotoToil(hs));
        }
        toils.Add(new DoToil((cm, world) =>
        {
            var m2 = world.Maneuver;
            if (cart && m2.Rolled.Remove(id) && world.Ship.IsOpenFloor(home) && !world.Portable.Occupied(home)) d.At = home;
            m2.Tipped.Remove(id);
            m2.Stats.Righted++;
            return true;
        }));
        return new Job(this, cart ? "카트 제자리" : "히터 세우기", toils) { OnFinished = (cm, world, _) => world.Maneuver.Unclaim(8, id) };
    }

    private Job? HeaterOn(CrewMember c, World w, int id)
    {
        var ms = w.Maneuver;
        var d = w.Portable.Devices.FirstOrDefault(x => x.Id == id);
        if (d == null || ms.Beside(d.At, c) is not Cell sp) return null;
        var toils = new List<Toil>
        {
            new GotoToil(sp),
            new WorkToil(0.004f, Skill.Electrical, d.At.Center),
            new DoToil((cm, world) => { d.On = true; world.Maneuver.HeaterOffBy.Remove(id); world.Maneuver.Stats.HeatersBack++; return true; }),
        };
        return new Job(this, "히터 다시 켜기", toils) { LogText = "기동 전에 꺼 둔 히터를 다시 켠다" };
    }

    private Job? Check(CrewMember c, World w, int roomId)
    {
        var ms = w.Maneuver;
        if (roomId < 0 || roomId >= w.Ship.Rooms.Count) return null;
        var room = w.Ship.Rooms[roomId];
        ms.Claim(9, roomId, c);
        var toils = new List<Toil>();
        var shelves = room.Furniture.Where(f => ManeuverSystem.Shelfish(f.Type)).OrderBy(f => f.Id).Take(3).ToList();
        foreach (var f in shelves)
        {
            if (ms.Near(f, c) is not Cell sp) continue;
            toils.Add(new GotoToil(sp));
            toils.Add(new WorkToil(0.012f, Skill.Mechanics, f.Center));
            toils.Add(new DoToil((cm, world) =>
            {
                var m2 = world.Maneuver;
                if (m2.Loose.Remove(f.Id)) { m2.Stats.Refixed++; MarkLog.Add(room.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 헐거워진 {f.Label} 걸쇠를 조였다"); }
                if (m2.Latched.Add(f.Id)) MarkLog.Add(room.Marks, world.Tick, $"{Ko.IGa(cm.Name)} 점검하며 {f.Label} 걸쇠를 걸었다");
                return true;
            }));
        }
        if (toils.Count == 0 && room.Cells.FirstOrDefault(cl => w.Ship.IsOpenFloor(cl)) is Cell any && any != default)
        { toils.Add(new GotoToil(any)); toils.Add(new WorkToil(0.01f, Skill.Mechanics, room.Center)); }
        toils.Add(new DoToil((cm, world) =>
        {
            var m2 = world.Maneuver;
            m2.CheckRooms.Remove(roomId);
            foreach (var id in m2.Loose.Where(x => x < 0).ToList())
                if (world.Matter.Get(-1 - id) is Article a && world.Ship.RoomAt(a.At) == room) { a.Fixed = true; m2.Loose.Remove(id); m2.Stats.Refixed++; }
            m2.Stats.Checked++;
            return true;
        }));
        return new Job(this, "걸쇠 점검", toils) { LogText = $"{room.Name} 걸쇠 · 끈 점검", LogKind = LogKind.Work, TargetRoom = room, OnFinished = (cm, world, _) => world.Maneuver.Unclaim(9, roomId) };
    }

    private Job? HabitLatch(CrewMember c, World w, int id)
    {
        var ms = w.Maneuver;
        var f = w.Ship.Furniture.FirstOrDefault(x => x.Id == id);
        if (f == null || ms.Near(f, c) is not Cell sp) return null;
        ms.Claim(4, id, c);
        var toils = new List<Toil>
        {
            new GotoToil(sp),
            new WorkToil(0.006f, Skill.Mechanics, f.Center),
            new DoToil((cm, world) => { if (world.Maneuver.Latched.Add(id)) world.Maneuver.Stats.HabitLatches++; return true; }),
        };
        return new Job(this, "선반 걸쇠", toils) { OnFinished = (cm, world, _) => world.Maneuver.Unclaim(4, id) };
    }

    private Job? Rebalance(CrewMember c, World w)
    {
        var ms = w.Maneuver;
        if (ms.Rebalance is not var (fromId, toId, n)) return null;
        var from = w.Ship.Furniture.FirstOrDefault(f => f.Id == fromId);
        var to = w.Ship.Furniture.FirstOrDefault(f => f.Id == toId);
        if (from?.Storage == null || to?.Storage == null || ms.Near(from, c) is not Cell a || ms.Near(to, c) is not Cell b) return null;
        ms.Claim(10, fromId, c);
        var toils = new List<Toil>
        {
            new GotoToil(a),
            new WorkToil(0.06f, Skill.Mechanics, from.Center),
            new GotoToil(b),
            new WorkToil(0.04f, Skill.Mechanics, to.Center),
            new DoToil((cm, world) =>
            {
                int moved = world.Maneuver.MoveCargo(from, to, n);
                if (moved > 0) world.Log.Add(world.Tick, LogKind.Work, $"{Ko.IGa(cm.Name)} {from.Room.Name} 짐 {moved}개를 {to.Room.Name}로 옮겼다 — 컴퓨터가 무게중심을 다시 쟀다 (기동 추력 {world.Maneuver.TrimMul * 100:0}%)", cm.Id);
                return true;
            }),
        };
        return new Job(this, "짐 옮기기", toils) { LogKind = LogKind.Work, OnFinished = (cm, world, _) => world.Maneuver.Unclaim(10, fromId) };
    }
}
