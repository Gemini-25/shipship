using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.2 · v18.3 사람이 하는 일: 숨은 고양이 찾기 · 밥 주기 · 덮어 주기 · 바구미 포대 열어 보기 · 골라 버리기 · 화분 물 · 배수구 뚫기 · 쓰레기통 비우기.
public sealed class EcoActivity : Activity
{
    public override string Id => "eco";
    public override string Label => "배 안의 생물 · 배수";

    private enum K { None, FindCat, Feed, Cover, Inspect, Treat, Water, Unclog, Empty }

    private static Cell? Beside(World w, Cell at, DistanceField dist)
    {
        foreach (var d in Cell.Dirs4) { var n = at + d; if (w.Ship.IsWalkable(n) && w.Ship.FurnitureAt(n) == null && dist.Reachable(n)) return n; }
        foreach (var d in Cell.Dirs8) { var n = at + d; if (w.Ship.IsWalkable(n) && dist.Reachable(n)) return n; }
        return dist.Reachable(at) ? at : null;
    }

    private static (K, float, string, object?) Pick(CrewMember c, World w, DistanceField dist)
    {
        var e = w.Eco;
        if (EcoSystem.Off || !c.CanAct || c.Outside || c.Room == null || w.ZeroG.Weightless) return (K.None, 0f, "", null);
        bool crisis = Crisis.Acting(w);
        if (e.Cat is ShipCat cat)
        {
            bool fav = cat.Favorite == c.Id;
            float fond = EcoSystem.Fond(cat, c.Id);
            if (!cat.Alive && !cat.Covered && fav && !crisis) return (K.Cover, 0.8f, $"{Ko.EulReul(cat.Name)} 덮어 준다", cat);
            if (cat.State == CatState.Hide && !e.Alarm && !crisis && (cat.SearchBy < 0 || cat.SearchBy == c.Id) && !c.IsChild)
            {
                var favc = e.CrewOf(cat.Favorite);
                bool favCan = favc != null && favc.CanAct && !favc.Outside;
                if (fav || !favCan && fond > 0.05f && w.Tick - cat.HiddenSince > SimTime.Hours(1))
                    return (K.FindCat, fav ? 0.95f : 0.55f, $"숨은 {Ko.EulReul(cat.Name)} 찾는다", cat);
            }
            if (cat.Alive && cat.State == CatState.Beg && cat.Path == null && !crisis && (fav || c.Room.Id == cat.RoomId) && e.Bowl is Cell && c.Job?.Urgent != true)
                return (K.Feed, 0.5f + 0.35f * fond, $"{cat.Name} 밥을 챙긴다", cat);
        }
        if (crisis) return (K.None, 0f, "", null);
        foreach (var x in e.Pests)
        {
            if (x.Treated) continue;
            if (x.Found && (x.TreatBy < 0 || x.TreatBy == c.Id) && (c.SkillLevel(Skill.Cooking) >= 0.3f || c.SkillLevel(Skill.Botany) >= 0.3f || c.Traits.Diligence > 0.6f))
                return (K.Treat, 0.62f, "바구미 먹은 포대를 골라내고 선반을 닦는다", x);
            if (!x.Found && x.Suspect && (x.TreatBy < 0 || x.TreatBy == c.Id) && !c.IsChild)
                return (K.Inspect, 0.55f, "컴퓨터 말대로 선반 곡식 포대를 열어 본다", x);
        }
        foreach (var d in w.Drains.Drains)
        {
            if (d.ClaimedBy >= 0 && d.ClaimedBy != c.Id) continue;
            if (d.Backflow && c.SkillLevel(Skill.Mechanics) >= 0.15f) return (K.Unclog, 0.85f, $"막힌 {DrainSystem.Name(d.Kind)}를 뚫는다", d);
            if (d.Warned && d.Clog > 0.72f && c.SkillLevel(Skill.Mechanics) >= 0.3f) return (K.Unclog, 0.45f, $"{DrainSystem.Name(d.Kind)} 막히기 전에 뚫는다", d);
        }
        foreach (var p in e.Plants)
        {
            if (p.Dead || p.Floating || p.ClaimedBy >= 0 && p.ClaimedBy != c.Id) continue;
            if (p.Carer == c.Id && (p.Water < 0.35f || p.Spilled)) return (K.Water, 0.42f + 0.2f * c.Traits.Diligence + (p.Stage >= 2 ? 0.2f : 0f), $"{p.Name} 화분에 물을 준다", p);
            var carer = e.CrewOf(p.Carer);
            bool carerCant = carer == null || carer.Dead || carer.Down || carer.Away || carer.Vitals.Injury > 0.3f;
            if (p.Stage >= 2 && p.Water < 0.2f && carerCant && c.Room.Id == p.RoomId) return (K.Water, 0.45f, $"시든 {p.Name}에 물을 준다 (돌보는 사람이 못 일어난다)", p);
        }
        foreach (var b in w.Drains.Bins)
        {
            if (!b.Over || b.ClaimedBy >= 0 && b.ClaimedBy != c.Id) continue;
            if (c.Room.Id == b.RoomId && c.Traits.Diligence >= 0.35f || w.Culture.Follows(c, CustomKind.SortWaste) && c.Room.Id == b.RoomId)
                return (K.Empty, 0.4f, "넘친 쓰레기통을 비운다", b);
        }
        return (K.None, 0f, "", null);
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var (k, s, why, _) = Pick(c, w, dist);
        return k == K.None ? (0f, "—") : (s, why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (k, _, _, o) = Pick(c, w, dist);
        var e = w.Eco;
        switch (k)
        {
            case K.FindCat: return Find(c, w, dist, (ShipCat)o!);
            case K.Feed when e.Bowl is Cell bowl && Beside(w, bowl, dist) is Cell fs:
                return new Job(this, "고양이 밥", new List<Toil>
                {
                    new GotoToil(fs),
                    new WaitToil(SimTime.Minutes(1), Pose.Working, bowl.Center),
                    new DoToil((cm, world) => { world.Eco.Feed(cm); return true; }),
                });
            case K.Cover when o is ShipCat dead && Beside(w, dead.Cell, dist) is Cell cs:
                return new Job(this, "고양이를 덮어 준다", new List<Toil>
                {
                    new GotoToil(cs),
                    new WaitToil(SimTime.Minutes(4), Pose.Sitting, dead.Pos),
                    new DoToil((cm, world) => { world.Eco.Cover(cm); return true; }),
                });
            case K.Inspect or K.Treat when o is Weevils x && w.Ship.Furniture.FirstOrDefault(f => f.Id == x.Shelf) is Furniture shelf && shelf.UseSpots.Count > 0:
            {
                x.TreatBy = c.Id;
                var spot = shelf.UseSpots.FirstOrDefault(s => dist.Reachable(s));
                if (!dist.Reachable(spot)) return null;
                var toils = new List<Toil> { new GotoToil(spot) };
                if (k == K.Inspect)
                {
                    toils.Add(new WaitToil(SimTime.Minutes(2), Pose.Working, shelf.Center));
                    toils.Add(new DoToil((cm, world) => { if (!x.Found) world.Eco.Discover(x, shelf, cm); x.TreatBy = -1; return true; }));
                }
                else
                {
                    toils.Add(new WorkToil(0.35f, Skill.Cooking, shelf.Center));
                    toils.Add(new DoToil((cm, world) => { world.Eco.Treat(x, cm); return true; }));
                }
                return new Job(this, k == K.Inspect ? "곡식 포대 확인" : "바구미 방제", toils) { OnFinished = (cm, world, st) => { if (!x.Treated && x.TreatBy == cm.Id) x.TreatBy = -1; } };
            }
            case K.Water when o is Plant p && Beside(w, p.At, dist) is Cell ps:
                p.ClaimedBy = c.Id;
                return new Job(this, "화분 물 주기", new List<Toil>
                {
                    new GotoToil(ps),
                    new WorkToil(0.05f, Skill.Botany, p.At.Center),
                    new DoToil((cm, world) => { world.Eco.WaterPlant(p, cm); return true; }),
                }) { OnFinished = (cm, world, _) => { if (p.ClaimedBy == cm.Id) p.ClaimedBy = -1; } };
            case K.Unclog when o is Drain d && Beside(w, d.At, dist) is Cell ds:
                d.ClaimedBy = c.Id;
                return new Job(this, "배수구 뚫기", new List<Toil>
                {
                    new GotoToil(ds),
                    new WorkToil(d.Backflow ? 0.3f : 0.2f, Skill.Mechanics, d.At.Center),
                    new DoToil((cm, world) => { world.Drains.Clear(d, cm); return true; }),
                }) { OnFinished = (cm, world, _) => { if (d.ClaimedBy == cm.Id) d.ClaimedBy = -1; } };
            case K.Empty when o is Bin b && Beside(w, b.At, dist) is Cell bs:
                b.ClaimedBy = c.Id;
                return new Job(this, "쓰레기통 비우기", new List<Toil>
                {
                    new GotoToil(bs),
                    new WorkToil(0.06f, Skill.Mechanics, b.At.Center),
                    new DoToil((cm, world) => { world.Drains.Empty(b, cm); return true; }),
                }) { OnFinished = (cm, world, _) => { if (b.ClaimedBy == cm.Id) b.ClaimedBy = -1; } };
        }
        return null;
    }

    /// <summary>찾는 사람은 고양이가 어디 숨었는지 모른다: 컴퓨터 귀띔(들었으면) → 전에 숨었던 방 → 자기 침대 방 → 창고 순으로 들여다본다.</summary>
    private Job? Find(CrewMember c, World w, DistanceField dist, ShipCat cat)
    {
        var e = w.Eco;
        var order = new List<Room>();
        void Add(Room? r) { if (r != null && !r.Detached && !order.Contains(r) && !cat.Searched.Contains(r.Id) && r.Cells.Count > 0) order.Add(r); }
        if (cat.Hint >= 0 && (cat.HintHeard || cat.Favorite != c.Id)) Add(e.RoomOf(cat.Hint));
        foreach (var id in cat.KnownHides) Add(e.RoomOf(id));
        Add(c.Bed?.Room);
        foreach (var r in w.Ship.Rooms.Where(r => r.Kind is RoomType.Quarters or RoomType.Storage or RoomType.Laundry or RoomType.PrivateCabins or RoomType.Cargo).OrderBy(r => (r.Cells[0].Center - c.Position).LengthSquared()).ThenBy(r => r.Id)) Add(r);
        if (order.Count == 0) { cat.Searched.Clear(); return null; }
        cat.SearchBy = c.Id;
        var toils = new List<Toil>();
        foreach (var r in order.Take(3))
        {
            var look = r.Furniture.Where(f => f.Type is FurnitureType.Bed or FurnitureType.Cot or FurnitureType.Shelf or FurnitureType.Bookshelf or FurnitureType.WashingMachine && f.UseSpots.Count > 0)
                           .OrderBy(f => f.Id == cat.HideNear && cat.HideRoom == r.Id ? 0 : 1).ThenBy(f => f.Id).SelectMany(f => f.UseSpots).FirstOrDefault(s => dist.Reachable(s));
            if (!dist.Reachable(look)) look = r.Cells.FirstOrDefault(x => dist.Reachable(x) && w.Ship.IsWalkable(x));
            if (!dist.Reachable(look)) continue;
            int rid = r.Id;
            toils.Add(new GotoToil(look, cm => cat.FoundBy < 0));
            toils.Add(new DoToil((cm, world) =>
            {
                if (cat.FoundBy >= 0) return true;
                cat.Searched.Add(rid);
                if (cat.State == CatState.Hide && cat.RoomId == rid) world.Eco.FoundCat(cm);
                else if (cm.IsAwake && world.Tick % 3 == 0) cm.Say(world, Persona.Say(cm, $"{cat.Name}? 여기 없나…"));
                return true;
            }));
        }
        if (toils.Count == 0) return null;
        toils.Add(new GotoToilLate(cm => cat.FoundBy == cm.Id && cat.State == CatState.Hide ? cat.Cell : null));
        toils.Add(new DoToil((cm, world) => { if (cat.FoundBy == cm.Id && cat.State == CatState.Hide && (cat.Pos - cm.Position).LengthSquared() < 4f) world.Eco.PickUp(cm); return true; }));
        toils.Add(new GotoToilLate(cm => cat.CarriedBy == cm.Id ? (world2(cm) is Cell home ? home : null) : null));
        toils.Add(new DoToil((cm, world) => { world.Eco.PutDown(cm); return true; }));
        return new Job(this, "숨은 고양이 찾기", toils)
        {
            InterruptMargin = 0.5f,
            OnFinished = (cm, world, _) =>
            {
                if (cat.SearchBy == cm.Id) cat.SearchBy = -1;
                if (cat.CarriedBy == cm.Id) world.Eco.PutDown(cm);
            },
        };
        Cell? world2(CrewMember cm) => e.CatBed is Cell cb && dist.Reachable(cb) ? cb : cm.Cell;
    }
}
