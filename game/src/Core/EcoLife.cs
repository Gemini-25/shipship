using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.2 생태계 — 화분 · 바구미 · 고양이의 시간 (EcoSystem 나머지).
public sealed partial class EcoSystem
{
    // ───────────────────────────── 화분 ─────────────────────────────

    private void PlantTick(float h)
    {
        var w = _w;
        if (Plants.Count == 0) return;
        var calm = new bool[w.Ship.Rooms.Count];
        foreach (var p in Plants)
        {
            if (p.Dead) continue;
            var room = RoomOf(p.RoomId);
            if (room == null || room.Detached) continue;
            float temp = room.Air.Temperature;
            p.Water = MathF.Max(0f, p.Water - Thirst(p.Kind) * h * (temp > 28f ? 1.6f : 1f) * (p.Spilled ? 2f : 1f) * (p.Floating ? 0.6f : 1f));
            float grow = p.Water < 0.15f ? -0.02f : p.Water > 0.3f && !room.Dark ? 0.012f : 0f;
            if (p.Spilled) grow -= 0.01f;
            if (temp < 2f) grow -= 0.05f;
            if (room.Unbreathable) grow -= 0.03f;
            bool was = p.Stage >= 2;
            p.Health = Math.Clamp(p.Health + grow * h, 0f, 1f);
            if (!was && p.Stage >= 2) { Stats.Wilted++; MarkLog.Add(room.Marks, w.Tick, $"{p.Name} 잎이 축 처졌다"); }
            if (w.Fire.At(p.At) > 0.2f) { p.Health = 0f; p.DeadWhy = "불에 탔다"; }
            if (p.Health <= 0f) { PlantDies(p, room); continue; }
            if (p.Stage == 0 && p.RoomId < calm.Length) calm[p.RoomId] = true;
            // 돌보는 사람
            if (CrewOf(p.Carer) is not CrewMember carer || carer.Dead) p.Carer = PickCarer(p);
            // 컴퓨터: 흙이 바싹 마른 지 오래 — 돌보는 사람에게
            if (p.Water < 0.1f && w.Tick - p.Watered > SimTime.Hours(30) && (p.Hinted < 0 || w.Tick - p.Hinted > SimTime.TicksPerDay) && w.Automation.CoreOnline)
            {
                p.Hinted = w.Tick; Stats.PlantHints++;
                var who = CrewOf(p.Carer);
                var b = w.Automation.Speak.Announce(w.Automation.Voice.Style($"{room.Name} {p.Name} 화분 흙이 바싹 말랐습니다" + (who != null ? $" — {who.Name}님, 물 한 번 부탁드립니다." : ".")), room, 1);
                w.Automation.Book.Add(ActKind.Advice, room, $"{room.Name} {p.Name} · 물 {p.Water:0.00} · 잎 {p.Health:0.00}", "마지막 물 준 지 하루가 넘었다 — 시들기 시작한다", "물 주기 부탁", "", $"eco:plant{p.Id}", SimTime.Hours(6));
            }
        }
        // 싱싱한 화분 곁: 조금 덜 팽팽하다
        foreach (var c in w.Crew)
            if (!c.Dead && c.Room is Room r && r.Id < calm.Length && calm[r.Id] && c.IsAwake) { c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.012f * h); Stats.Relief++; }
    }

    private int PickCarer(Plant p)
    {
        var w = _w;
        CrewMember? best = null; float bs = float.MinValue;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild && c.Age < 6f) continue;
            float s = c.SkillLevel(Skill.Botany) + 0.5f * c.Traits.Diligence + (c.Bed?.Room.Id == p.RoomId ? 0.4f : 0f) + ((c.Id * 31 + p.Id * 17) % 10) * 0.02f
                      - 0.15f * Plants.Count(x => x.Carer == c.Id && x != p);
            if (s > bs) { bs = s; best = c; }
        }
        return best?.Id ?? -1;
    }

    private void PlantDies(Plant p, Room room)
    {
        var w = _w;
        p.Dead = true; Stats.PlantDeaths++;
        if (p.DeadWhy == "") p.DeadWhy = p.Water < 0.15f ? "말라 죽었다" : p.Spilled ? "화분이 깨진 채 뿌리가 말랐다" : "시들어 죽었다";
        w.Log.Add(w.Tick, LogKind.Life, $"{room.Name}의 {p.Name} — {p.DeadWhy}");
        MarkLog.Add(room.Marks, w.Tick, $"{p.Name} 화분 — {p.DeadWhy}");
        if (CrewOf(p.Carer) is CrewMember cm && !cm.Dead)
        {
            w.Brain2.Emotions.Feel(cm, Feeling.Sadness, 0.2f, $"돌보던 {Ko.IGa(p.Name)} {p.DeadWhy}");
            MarkLog.Add(cm.Memory.Marks, w.Tick, $"돌보던 {Ko.IGa(p.Name)} {p.DeadWhy}");
            if (cm.IsAwake) cm.Say(w, Persona.Say(cm, cm.Vitals.Injury > 0.2f || cm.Down ? "내가 누워 있는 사이에… 미안하다" : "결국 못 살렸네"));
        }
    }

    /// <summary>물을 줬다 (WaterPlantActivity).</summary>
    internal void WaterPlant(Plant p, CrewMember c)
    {
        var w = _w;
        p.Water = 1f; p.Watered = w.Tick; p.WateredBy = c.Id; p.ClaimedBy = -1;
        Stats.Watered++;
        if (p.Spilled) { p.Spilled = false; Stats.Repotted++; }
        if (p.Floating) p.Fixed = true;
        MarkLog.Add(c.Memory.Marks, w.Tick, $"{p.Name} 화분에 물을 줬다");
        // 남의 화분을 거들었다 — 돌보는 사람이 고마워한다
        if (p.Carer != c.Id && CrewOf(p.Carer) is CrewMember carer && !carer.Dead && (carer.Down || carer.Vitals.Injury > 0.15f || carer.Away))
            w.Relations.Remember(carer, c, RelationReason.DidMyShift, $"내가 못 일어날 때 {p.Name} 화분에 물을 줬다");
        // 물뿌리개 끝에서 흘린 물 (가끔) — 바닥이 젖는다
        if (R.Chance(0.06f)) w.Matter.Pour(p.At, Material.Liquid, 0.15f, "화분에 물을 주다 흘렸다");
    }

    // ───────────────────────────── 곡식 바구미 ─────────────────────────────

    private IEnumerable<Furniture> GrainShelves() =>
        _w.Ship.Furniture.Where(f => f.Type == FurnitureType.Shelf && f.Storage != null && !f.Room.Detached && !f.Stowed && f.Storage.Count(ItemKind.Ration) > 0);

    private void PestTick(float h)
    {
        var w = _w;
        // 곡식 포대에 섞여 온 알 (사흘째부터 드물게 · 밀폐 통 선반은 덜)
        if (w.Day >= 3 && Pests.Count(x => !x.Treated) == 0 && R.Chance(h / (24f * 7f)))
        {
            var shelves = GrainShelves().OrderBy(f => f.Id).ToList();
            if (shelves.Count > 0)
            {
                var s = shelves[R.Range(0, shelves.Count)];
                if (!Sealed.Contains(s.Id) || R.Chance(0.15f)) Infest(s);
            }
        }
        for (int i = 0; i < Pests.Count; i++)
        {
            var x = Pests[i];
            if (x.Treated) continue;
            var shelf = w.Ship.Furniture.FirstOrDefault(f => f.Id == x.Shelf);
            if (shelf == null || shelf.Room.Detached) { x.Treated = true; continue; }
            var room = shelf.Room;
            float temp = room.Air.Temperature;
            float r = temp < 0f ? -0.2f : temp < 8f ? 0f : temp < 15f ? 0.04f : temp > 32f ? 0.08f : 0.12f;
            int grain = shelf.Storage?.Count(ItemKind.Ration) ?? 0;
            if (grain == 0) r = MathF.Min(r, -0.05f);
            float was = x.Pop;
            x.Pop = Math.Clamp(x.Pop + r * x.Pop * (1f - x.Pop) * h + (r < 0f ? r * 0.2f * h : 0f), 0f, 1f);
            if (x.Pop < 0.005f) { x.Treated = true; continue; }
            if (was < 0.3f && x.Pop >= 0.3f) Stats.Bred++;
            // 갉아 먹는다
            if (x.Pop > 0.25f && grain > 0 && R.Chance(x.Pop * 0.6f * h))
            {
                shelf.Storage!.Take(ItemKind.Ration, 1);
                x.Eaten++; Stats.Eaten++;
            }
            if (x.Pop > 0.5f) w.Smells.Emit(room, SmellKind.Foul, 0.04f * x.Pop); // 퀴퀴한 곡식 냄새
            // 옆 선반으로 번진다
            if (x.Pop > 0.65f && R.Chance(0.1f * h))
                foreach (var o in room.Furniture)
                    if (o.Type == FurnitureType.Shelf && o.Id != x.Shelf && (o.Storage?.Count(ItemKind.Ration) ?? 0) > 0 && !Pests.Any(p => p.Shelf == o.Id && !p.Treated) && !Sealed.Contains(o.Id))
                    { Infest(o).Pop = 0.05f; Stats.Spread++; break; }
            // 선반 앞을 지나는 사람이 본다 (많을수록 · 꼼꼼할수록)
            if (!x.Found)
                foreach (var c in w.Crew)
                {
                    if (c.Dead || !c.IsAwake || c.Room != room || (c.Position - shelf.Center).LengthSquared() > 3.2f) continue;
                    float p = x.Pop * (0.25f + 0.5f * c.Traits.Diligence) * (x.Suspect ? 3f : 1f) * h * 4f;
                    if (!R.Chance(MathF.Min(0.9f, p))) continue;
                    Discover(x, shelf, c);
                    break;
                }
        }
        // 컴퓨터: 먹은 양보다 비상식량이 더 줄었다 (몇 시간마다 장부와 선반을 맞춘다)
        if (w.Tick >= _nextAudit)
        {
            _nextAudit = w.Tick + SimTime.Hours(6);
            int lost = Pests.Where(x => !x.Found && !x.Treated).Sum(x => x.Eaten);
            if (lost - _auditSeen >= 3 && w.Automation.CoreOnline && Pests.FirstOrDefault(x => !x.Found && !x.Treated && x.Eaten > 0) is Weevils sus
                && w.Ship.Furniture.FirstOrDefault(f => f.Id == sus.Shelf) is Furniture sf)
            {
                _auditSeen = lost;
                sus.Suspect = true; Stats.FoundByComputer++;
                var b = w.Automation.Speak.Announce(w.Automation.Voice.Style($"{sf.Room.Name} 비상식량이 먹은 기록보다 {lost}개 더 줄었습니다. {sf.Label} 포대를 열어 봐 주십시오 — 벌레일 수 있습니다."), sf.Room, 1);
                w.Automation.Book.Add(ActKind.Advice, sf.Room, $"비상식량 장부 차이 {lost}", "꺼내 먹은 기록 없이 줄었다 — 곡식 벌레 · 쥐 · 몰래 먹기", "선반 확인 부탁", "", "eco:audit", SimTime.Hours(6));
            }
        }
    }

    private Weevils Infest(Furniture shelf)
    {
        var x = new Weevils { Id = _next++, Shelf = shelf.Id, RoomId = shelf.Room.Id, Since = _w.Tick };
        Pests.Add(x);
        Stats.Infest++;
        return x;
    }

    /// <summary>시험 · 사건용: 이 선반에 바구미가 생긴다.</summary>
    public Weevils Seed(Furniture shelf, float pop) { var x = Infest(shelf); x.Pop = pop; return x; }

    internal void Discover(Weevils x, Furniture shelf, CrewMember c)
    {
        var w = _w;
        x.Found = true; x.FoundBy = c.Id; Stats.FoundByCrew++;
        c.Say(w, Persona.Say(c, x.Pop > 0.6f ? "으, 곡식 포대에 바구미가 우글거려!" : "어? 포대에 작은 벌레가… 바구미다"));
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
        MarkLog.Add(c.Memory.Marks, w.Tick, $"{shelf.Room.Name} {shelf.Label} 곡식 포대에서 바구미를 봤다");
        MarkLog.Add(shelf.Room.Marks, w.Tick, $"{shelf.Label} 곡식 포대에 바구미");
        w.Log.Add(w.Tick, LogKind.Warning, $"{shelf.Room.Name}: {Ko.IGa(c.Name)} 비상식량 포대에서 바구미를 찾았다 (벌써 {x.Eaten}개를 먹었다)", c.Id);
    }

    /// <summary>방제: 먹힌 포대를 골라 버리고 선반을 닦고 밀폐 통에 옮긴다.</summary>
    internal void Treat(Weevils x, CrewMember c)
    {
        var w = _w;
        var shelf = w.Ship.Furniture.FirstOrDefault(f => f.Id == x.Shelf);
        int grain = shelf?.Storage?.Count(ItemKind.Ration) ?? 0;
        int toss = Math.Min(grain, (int)MathF.Ceiling(grain * 0.35f * x.Pop));
        if (toss > 0) shelf!.Storage!.Take(ItemKind.Ration, toss);
        x.Treated = true; x.Pop = 0f; x.TreatBy = c.Id;
        Stats.Controlled++; Stats.Discarded += toss;
        if (shelf != null)
        {
            Sealed.Add(shelf.Id);
            w.Drains.Toss(shelf.Room, 0.25f + 0.04f * toss, food: true); // 버린 포대 → 쓰레기통
            MarkLog.Add(shelf.Room.Marks, w.Tick, $"{shelf.Label} 바구미 방제 · 곡식은 밀폐 통으로");
        }
        MarkLog.Add(c.Memory.Marks, w.Tick, $"바구미 먹은 포대를 골라 버리고 선반을 닦았다");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(c.Name)} 바구미 먹은 포대를 골라내 버리고 선반을 닦아 곡식을 밀폐 통에 옮겼다 — 비상식량 {toss}개를 버렸다", c.Id);
        c.Say(w, Persona.Say(c, toss > 2 ? $"{toss}개는 버려야 했어. 아깝다" : "이제 밀폐 통에 넣어 두자"));
    }

    // ───────────────────────────── 고양이 ─────────────────────────────

    public bool Alarm => _w.Fire.Count > 0 && _w.Automation.Alarms;

    /// <summary>놀랐다 (경보 · 쿵 소리 · 폭발): 숨을 곳으로.</summary>
    public void Startle(string why, Cell? near = null)
    {
        if (Cat is not ShipCat cat || !cat.Alive || cat.State is CatState.Carried or CatState.Float) return;
        if (near is Cell n && (n.Center - cat.Pos).LengthSquared() > 64f) return;
        cat.Fear = 1f;
        if (cat.State == CatState.Hide) return;
        GoHide(cat, why);
    }

    private void GoHide(ShipCat cat, string why)
    {
        var w = _w;
        var fav = CrewOf(cat.Favorite);
        Room? best = null; Furniture? under = null; float bs = float.MinValue;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached || r.Unbreathable || r.OffLimits || r.Kind == RoomType.Corridor || w.Fire.Count > 0 && w.Fire.CountIn(r) > 0) continue;
            float s = r.Kind switch { RoomType.Quarters or RoomType.PrivateCabins or RoomType.QuietQuarters => 3f, RoomType.Storage or RoomType.Cargo => 2.5f, RoomType.Laundry => 2f, _ => 0f };
            if (fav?.Bed?.Room == r) s += 2f;
            if (cat.KnownHides.Contains(r.Id)) s += 1f;
            s -= 0.02f * (r.Cells.Count > 0 ? (r.Cells[0].Center - cat.Pos).Length() : 99f);
            Furniture? f = r.Furniture.Where(x => x.Type is FurnitureType.Bed or FurnitureType.Cot or FurnitureType.Shelf or FurnitureType.Bookshelf or FurnitureType.WashingMachine).OrderBy(x => x.Id).FirstOrDefault();
            if (f == null || s <= 0f) continue;
            if (s > bs) { bs = s; best = r; under = f; }
        }
        if (best == null || under == null) return;
        var spot = HideSpot(under);
        if (spot is not Cell sc) return;
        cat.State = CatState.Hide; cat.HideRoom = best.Id; cat.HideNear = under.Id; cat.HiddenSince = w.Tick;
        cat.FoundBy = -1; cat.SearchBy = -1; cat.Searched.Clear(); cat.Hint = -1; cat.HintHeard = false;
        Stats.Hid++;
        PathTo(cat, sc);
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(cat.Name)} {why} — 털을 세우고 어디론가 숨었다");
    }

    private Cell? HideSpot(Furniture f)
    {
        var w = _w;
        foreach (var c in f.Cells.OrderBy(c => c.X * 1000 + c.Y))
            foreach (var d in Cell.Dirs4)
                if (w.Ship.IsWalkable(c + d) && w.Ship.FurnitureAt(c + d) == null && w.Ship.RoomAt(c + d) == f.Room) return c + d;
        return f.UseSpots.Count > 0 ? f.UseSpots[0] : null;
    }

    private void PathTo(ShipCat cat, Cell goal)
    {
        var p = _w.Paths.Find(cat.Cell, goal, PathProfile.Default);
        cat.Path = p != null && p.Count > 0 ? p : null;
        cat.PathAt = 0;
        if (cat.Path == null && _w.Ship.IsWalkable(goal) && (goal.Center - cat.Pos).LengthSquared() < 2.5f) cat.Pos = goal.Center;
    }

    private void CatTick(float dt)
    {
        var w = _w;
        if (Cat is not ShipCat cat || !cat.Alive) return;
        cat.Hunger = MathF.Min(1f, cat.Hunger + 0.035f * dt);
        if (cat.Flail > 0f) cat.Flail = MathF.Max(0f, cat.Flail - dt);
        var room = w.Ship.RoomAt(cat.Cell);
        if (room != null) cat.RoomId = room.Id;
        // 죽음: 불 · 숨 못 쉬는 방 · 오래 굶음
        if (w.Fire.At(cat.Cell) > 0.35f) { CatDies(cat, "불길에 휩싸였다"); return; }
        if (room != null && room.Unbreathable && cat.CarriedBy < 0) { cat.Until = cat.Until <= 0 ? w.Tick + SimTime.Minutes(8) : cat.Until; if (w.Tick >= cat.Until) { CatDies(cat, "숨 쉴 공기가 빠진 방에 갇혔다"); return; } }
        if (cat.Hunger >= 1f && w.Tick - BowlFilled > SimTime.Hours(72) && BowlFilled >= 0) { CatDies(cat, "오래 굶었다"); return; }
        if (Alarm && cat.State is not (CatState.Hide or CatState.Carried or CatState.Float)) Startle("경보가 울렸다");
        if (!Alarm && cat.State != CatState.Hide) cat.Fear = MathF.Max(0f, cat.Fear - 0.6f * dt);
        if (w.Tick < _nextCat) return;
        _nextCat = w.Tick + SimTime.Minutes(3);
        switch (cat.State)
        {
            case CatState.Hide:
                if (cat.Path == null && cat.HiddenSince >= 0 && !Alarm)
                {
                    // 컴퓨터가 움직임으로 짚는다 (좋아하는 사람이 못 찾고 있으면)
                    if (cat.Hint < 0 && w.Tick - cat.HiddenSince > SimTime.Minutes(20) && w.Automation.CoreOnline && RoomOf(cat.HideRoom) is Room hr)
                    {
                        cat.Hint = hr.Id; Stats.CatHints++;
                        string under = w.Ship.Furniture.FirstOrDefault(f => f.Id == cat.HideNear)?.Label ?? "가구";
                        var fav = CrewOf(cat.Favorite);
                        var b = w.Automation.Speak.Announce(w.Automation.Voice.Style($"{hr.Name} {under} 쪽에서 작은 움직임이 잡힙니다 — {cat.Name}일 겁니다."), fav?.Room ?? hr, 1);
                        cat.HintHeard = b != null && fav != null && b.HeardBy.Contains(fav.Id);
                        w.Automation.Book.Add(ActKind.Advice, hr, $"{cat.Name} 숨음 · {(w.Tick - cat.HiddenSince) / SimTime.Minutes(1)}분", "경보 뒤 고양이가 나오지 않는다 — 움직임 감지로 위치를 짚는다", "위치 안내", "", "eco:cat", SimTime.Hours(2));
                    }
                    // 아무도 안 오면 배고플 때 스스로 나온다
                    if (cat.Hunger > 0.9f && w.Tick - cat.HiddenSince > SimTime.Hours(10)) { cat.State = CatState.Roam; cat.HiddenSince = -1; cat.Fear = 0f; }
                }
                return;
            case CatState.Carried or CatState.Float:
                return;
        }
        // 배고프다: 밥그릇 앞에서 운다 · 오래 굶으면 식탁에서 훔친다
        if (cat.Hunger > 0.6f && Bowl is Cell bowl)
        {
            if (cat.State != CatState.Beg) { cat.State = CatState.Beg; Stats.Begged++; PathTo(cat, bowl); }
            if (cat.Path == null) cat.Meow = w.Tick;
            if (cat.Hunger > 0.92f && room != null && room.Kind is RoomType.Mess or RoomType.Galley && R.Chance(0.08f))
            {
                var box = room.Furniture.FirstOrDefault(f => f.Storage != null && f.Storage.Count(ItemKind.Meal) > 0);
                if (box != null)
                {
                    box.Storage!.Take(ItemKind.Meal, 1);
                    cat.Hunger = 0.3f; Stats.Stole++;
                    w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(cat.Name)} 배식대에서 한 끼를 훔쳐 먹었다");
                    foreach (var o in w.Crew) if (!o.Dead && o.Room == room && o.IsAwake) { o.Say(w, Persona.Say(o, $"야, {cat.Name}! 그거 누구 저녁이야")); break; }
                }
            }
            return;
        }
        if (cat.State == CatState.Beg) cat.State = CatState.Roam;
        if (cat.Path != null) return;
        // 좋아하는 사람이 자면 그 침대 발치에서 잔다
        var favc = CrewOf(cat.Favorite);
        if (favc != null && !favc.Dead && favc.Pose == Pose.Sleeping && favc.Bed is Furniture bed && HideSpot(bed) is Cell bc)
        {
            if (cat.State != CatState.Nap || (bc.Center - cat.Pos).LengthSquared() > 2f) { cat.State = CatState.Nap; Stats.Naps++; PathTo(cat, bc); }
            return;
        }
        if (cat.State == CatState.Nap && w.Tick < cat.Until) return;
        // 낮잠 · 따라가기 · 어슬렁
        float roll = R.Float();
        if (roll < 0.25f && CatBed is Cell cb) { cat.State = CatState.Nap; cat.Until = w.Tick + SimTime.Minutes(R.Range(30, 120)); PathTo(cat, cb); return; }
        cat.State = CatState.Roam;
        if (roll < 0.55f && favc != null && !favc.Dead && favc.IsAwake && !favc.Outside && favc.Room is Room fr && !fr.Unbreathable && fr.Kind != RoomType.Corridor)
        {
            Stats.Follows++;
            var goal = fr.Cells[(int)((favc.Id + w.Tick / 97) % fr.Cells.Count)];
            if (w.Ship.IsWalkable(goal)) PathTo(cat, goal);
            return;
        }
        if (room != null && room.Cells.Count > 0)
        {
            var goal = room.Cells[R.Range(0, room.Cells.Count)];
            if (R.Chance(0.3f)) { var other = w.Ship.Rooms.Where(r => !r.Detached && !r.Unbreathable && !r.OffLimits && r.Kind is RoomType.Lounge or RoomType.Mess or RoomType.Quarters or RoomType.Hydroponics or RoomType.Storage or RoomType.Galley or RoomType.Garden).OrderBy(r => r.Id).ToList(); if (other.Count > 0) { var orr = other[R.Range(0, other.Count)]; goal = orr.Cells[R.Range(0, orr.Cells.Count)]; } }
            if (w.Ship.IsWalkable(goal)) PathTo(cat, goal);
        }
        // 화분 잎을 뜯는다
        foreach (var p in Plants)
            if (!p.Dead && p.RoomId == cat.RoomId && (p.At.Center - cat.Pos).LengthSquared() < 2.5f && R.Chance(0.12f))
            {
                p.Health = MathF.Max(0.05f, p.Health - 0.06f); p.Chewed++; Stats.Chewed++;
                if (CrewOf(p.Carer) is CrewMember pc && pc.Room?.Id == p.RoomId && pc.IsAwake) pc.Say(w, Persona.Say(pc, $"{cat.Name}, 또 {p.Name} 잎을 뜯었구나"));
                break;
            }
        // 쓰다듬기: 쉬는 사람 곁
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.Room?.Id != cat.RoomId || (c.Position - cat.Pos).LengthSquared() > 2.2f) continue;
            if (c.Job != null && c.Job.Urgent) continue;
            if (!R.Chance(0.25f + 0.2f * Fond(cat, c.Id))) continue;
            Pet(cat, c);
            break;
        }
    }

    public static float Fond(ShipCat cat, int id) => cat.Fond.TryGetValue(id, out var f) ? f : 0f;

    private void Pet(ShipCat cat, CrewMember c)
    {
        var w = _w;
        cat.Purr = w.Tick; Stats.Pets++;
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f);
        Bond(cat, c, 0.02f);
        if (R.Chance(0.2f)) c.Say(w, Persona.Say(c, cat.Hunger > 0.5f ? $"{cat.Name}, 배고프구나" : $"그르릉거리네, {cat.Name}"));
    }

    private void Bond(ShipCat cat, CrewMember c, float v)
    {
        cat.Fond[c.Id] = MathF.Min(1f, Fond(cat, c.Id) + v);
        var fav = CrewOf(cat.Favorite);
        if (fav == null || fav.Dead || Fond(cat, c.Id) > Fond(cat, cat.Favorite) + 0.25f) cat.Favorite = c.Id;
    }

    /// <summary>밥을 줬다 (FeedCatActivity): 사료 · 떨어지면 비상식량을 나눈다.</summary>
    internal void Feed(CrewMember c)
    {
        var w = _w;
        if (Cat is not ShipCat cat || !cat.Alive) return;
        string what = "사료";
        if (Kibble > 0) Kibble--;
        else
        {
            var box = w.Ship.Containers.Where(f => f.Storage!.Count(ItemKind.Ration) > 0).OrderBy(f => (f.Center - c.Position).LengthSquared()).ThenBy(f => f.Id).FirstOrDefault();
            if (box != null) { box.Storage!.Take(ItemKind.Ration, 1); what = "비상식량 한 봉"; }
            else what = "남은 음식 조금";
        }
        cat.Hunger = 0f; BowlFilled = w.Tick; Stats.Fed++;
        if (cat.State == CatState.Beg) cat.State = CatState.Eat;
        Bond(cat, c, 0.06f);
        MarkLog.Add(c.Memory.Marks, w.Tick, $"{cat.Name}에게 {what}를 줬다");
        if (Kibble == 3) w.Log.Add(w.Tick, LogKind.Warning, $"고양이 사료가 세 번 남았다 — 다음부턴 사람 몫을 나눠야 한다");
    }

    /// <summary>찾았다 · 안았다 · 내려놓았다 (FindCatActivity).</summary>
    internal void FoundCat(CrewMember c)
    {
        if (Cat is not ShipCat cat) return;
        cat.FoundBy = c.Id; Stats.Found++;
        cat.KnownHides.Add(cat.HideRoom);
        c.Say(_w, Persona.Say(c, $"여기 있었구나, {cat.Name}. 무서웠지"));
    }

    internal void PickUp(CrewMember c)
    {
        if (Cat is not ShipCat cat || !cat.Alive) return;
        cat.State = CatState.Carried; cat.CarriedBy = c.Id; cat.Path = null; cat.HiddenSince = -1;
    }

    internal void PutDown(CrewMember c, bool zeroG = false)
    {
        var w = _w;
        if (Cat is not ShipCat cat || cat.CarriedBy != c.Id) return;
        cat.CarriedBy = -1; cat.Fear = 0f; cat.Path = null;
        cat.State = w.ZeroG.Weightless ? CatState.Float : CatState.Nap; cat.Until = w.Tick + SimTime.Minutes(40);
        cat.Pos = c.Position;
        if (zeroG) return;
        Stats.Returned++;
        Bond(cat, c, 0.1f);
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 숨어 있던 {Ko.EulReul(cat.Name)} 찾아 안고 왔다");
        MarkLog.Add(c.Memory.Marks, w.Tick, $"경보 뒤 숨은 {Ko.EulReul(cat.Name)} 찾아 데려왔다");
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
        if (cat.Favorite != c.Id && CrewOf(cat.Favorite) is CrewMember fav && !fav.Dead)
            w.Relations.Remember(fav, c, RelationReason.SavedMyThing, $"숨은 {Ko.EulReul(cat.Name)} 찾아 데려왔다");
    }

    private void CatDies(ShipCat cat, string why)
    {
        var w = _w;
        cat.State = CatState.Dead; cat.Died = w.Tick; cat.DiedWhy = why; cat.Path = null; cat.CarriedBy = -1;
        Stats.Deaths++;
        var room = RoomOf(cat.RoomId);
        w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(cat.Name)} 죽었다 — {why}");
        w.History.Add(w, HistoryKind.Memory, $"배의 고양이 {Ko.IGa(cat.Name)} 죽었다 — {why}", room);
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            float f = Fond(cat, c.Id);
            bool fav = c.Id == cat.Favorite;
            w.Brain2.Emotions.Feel(c, Feeling.Sadness, fav ? 0.6f : 0.1f + 0.3f * f, $"고양이 {Ko.IGa(cat.Name)} 죽었다");
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + (fav ? 0.15f : 0.03f + 0.05f * f));
            if (fav) { Memory.Shake(w, c, 0.08f, $"{Ko.IGa(cat.Name)} 죽었다 — {why}"); MarkLog.Add(c.Memory.Marks, w.Tick, $"{Ko.IGa(cat.Name)} 죽었다 — {why}"); }
        }
    }

    /// <summary>묻어 줄 자리를 덮었다 (좋아하던 사람이 담요로).</summary>
    internal void Cover(CrewMember c)
    {
        if (Cat is not ShipCat cat || cat.Alive || cat.Covered) return;
        cat.Covered = true; Stats.Mourned++;
        MarkLog.Add(c.Memory.Marks, _w.Tick, $"{Ko.EulReul(cat.Name)} 담요로 덮어 줬다");
        _w.Log.Add(_w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {Ko.EulReul(cat.Name)} 작은 담요로 덮어 주고 한참 앉아 있었다", c.Id);
    }

    // ───────────────────────────── 매 틱: 고양이 몸 ─────────────────────────────

    public void Step()
    {
        if (Off || Cat is not ShipCat cat || !cat.Alive) return;
        var w = _w;
        if (cat.CarriedBy >= 0)
        {
            if (CrewOf(cat.CarriedBy) is CrewMember h && !h.Dead) { cat.Pos = h.Position + h.Facing * 0.25f; return; }
            cat.CarriedBy = -1; cat.State = CatState.Roam;
        }
        if (cat.State == CatState.Float)
        {
            var np = cat.Pos + cat.Vel;
            var k = w.Ship.Grid.Kind(Cell.FromPosition(np));
            if (k is TileKind.Floor or TileKind.Door) cat.Pos = np; else cat.Vel = -cat.Vel * 0.6f;
            cat.Flail = 1f;
            // 지나가던 사람이 붙잡는다
            if (w.Tick % 6 == 0)
                foreach (var c in w.Crew)
                    if (!c.Dead && c.IsAwake && !c.Outside && c.Carrying == null && c.CarryingPerson == null && (c.Position - cat.Pos).LengthSquared() < 0.5f)
                    {
                        cat.State = CatState.Carried; cat.CarriedBy = c.Id; Stats.Grabbed++;
                        c.Say(w, Persona.Say(c, $"잡았다 — {cat.Name}, 가만있어"));
                        break;
                    }
            return;
        }
        if (cat.Path == null) return;
        float speed = cat.State == CatState.Hide ? 0.12f : cat.State == CatState.Beg ? 0.07f : 0.045f;
        if (w.ZeroG.Weightless) speed *= 0.4f;
        while (speed > 0f && cat.PathAt < cat.Path.Count)
        {
            var to = cat.Path[cat.PathAt].Center;
            var d = to - cat.Pos;
            float len = d.Length();
            if (len <= speed) { cat.Pos = to; speed -= len; cat.PathAt++; continue; }
            cat.Facing = d / len;
            cat.Pos += cat.Facing * speed;
            speed = 0f;
        }
        if (cat.PathAt >= cat.Path.Count) cat.Path = null;
    }
}
