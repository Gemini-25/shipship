using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.4 승무원이 재질 규칙을 알고 움직인다 (보이는 대로):
// 젖은 바닥의 분전함 앞엔 고무 매트를 깔고 작업한다(고무 = 절연) · 히터 · 불 곁의 천 · 종이를 치운다(마르면 그을린다) ·
// 그을리는 물건을 찾아 치우고 적신다 · 물을 다 머금은 러그를 걷어 널어 말린다(그 아래 숨은 젖은 접속부를 찾는다) ·
// 찾은 접속부를 말리고 감는다 · 통로를 막은 짐을 치운다 · 깨끗해야 하는 방의 문 손잡이를 닦는다.
// 유리 조각은 길찾기가 돌아가고(칸 상태 "유리") 배 손보기가 쓸어 낸다.

public sealed partial class MatterSystem
{
    private readonly Dictionary<int, Job?> _carryJob = new();
    private readonly Dictionary<int, long> _claimUntil = new();

    internal void Claim(Article t, CrewMember c) { t.ClaimedBy = c.Id; _claimUntil[t.Id] = _w.Tick + SimTime.Hours(2); }

    private readonly Dictionary<int, Cell> _carryFrom = new();

    internal void Carry(Article t, CrewMember c)
    {
        _carryFrom[t.Id] = t.At;
        PickUp(t, c);
        _carryJob[t.Id] = c.Job;
    }

    /// <summary>들고 가던 사람이 일을 놓쳤으면 그 자리에 내려놓는다 · 오래된 맡음은 푼다.</summary>
    private void Release()
    {
        var w = _w;
        foreach (var t in Things)
        {
            if (t.ClaimedBy >= 0 && _claimUntil.TryGetValue(t.Id, out var until) && w.Tick > until) t.ClaimedBy = -1;
            if (t.CarriedBy < 0) continue;
            var c = w.Crew.FirstOrDefault(x => x.Id == t.CarriedBy);
            bool keep = c != null && !c.Dead && !c.Down && c.Job != null && _carryJob.TryGetValue(t.Id, out var j) && j == c.Job;
            if (keep) continue;
            var at = c?.Cell ?? t.At;
            if (!w.Ship.IsOpenFloor(at) || w.Ship.DoorAt(at) != null) foreach (var d in Cell.Dirs8) if (w.Ship.IsOpenFloor(at + d) && w.Ship.DoorAt(at + d) == null) { at += d; break; }
            t.ClaimedBy = -1;
            _carryJob.Remove(t.Id);
            Place(t, at);
        }
    }

    /// <summary>그 칸에서 전기 일을 하려면 고무 매트가 필요한가 (젖은 · 물 고인 · 전기 흐르는 바닥 — 고무 바닥이면 필요 없다).</summary>
    public bool NeedsMat(Cell at)
    {
        var b = _w.Body;
        if (Matter.Base(b.FloorAt(at)) == Material.Rubber || MatAt(at)) return false;
        var room = _w.Ship.RoomAt(at);
        return b.Mark(at, CellMark.Wet) >= 0.2f || LitersAt(at) > 0.1f || LiveAt(at) > 0f || room != null && MoistureSystem.Depth(room) > 0.02f;
    }

    /// <summary>v16.4 젖은 바닥에서 전기 일: 가까운 고무 매트를 가져와 그 칸에 깐다 (Moisture 분전함 · 접속부 고치기가 부른다).</summary>
    public void Footing(CrewMember c, DistanceField dist, List<Toil> toils, Cell at)
    {
        if (!NeedsMat(at)) return;
        Article? best = null;
        int bd = int.MaxValue;
        foreach (var t in Things)
        {
            if (t.Kind != ArticleKind.RubberMat || t.CarriedBy >= 0 || t.ClaimedBy >= 0 && t.ClaimedBy != c.Id) continue;
            if (!t.Stowed && _w.Crew.Any(x => !x.Dead && x.Id != c.Id && x.Pose == Pose.Working && x.Cell == t.At)) continue; // 누가 그 위에서 일한다
            int d = dist.Get(t.At);
            if (d < 0 || d >= bd) continue;
            bd = d;
            best = t;
        }
        if (best == null) return;
        var mat = best;
        Claim(mat, c);
        toils.Add(new GotoToil(mat.At));
        toils.Add(new DoToil((cm, world) =>
        {
            if (mat.CarriedBy >= 0 && mat.CarriedBy != cm.Id || !world.Matter.Things.Contains(mat)) return true; // 누가 가져갔다 — 매트 없이
            world.Matter.Carry(mat, cm);
            return true;
        }));
        toils.Add(new GotoToil(at));
        toils.Add(new DoToil((cm, world) => { if (mat.CarriedBy == cm.Id) world.Matter.LayMat(mat, cm, at); return true; }));
    }

    internal void LayMat(Article mat, CrewMember c, Cell at)
    {
        var w = _w;
        Place(mat, at);
        mat.Stowed = false;
        mat.ClaimedBy = -1;
        mat.Off = Vector2.Zero;
        mat.Angle = 0f;
        _carryJob.Remove(mat.Id);
        Stats.MatsLaid++;
        var room = w.Ship.RoomAt(at);
        w.Log.Add(w.Tick, LogKind.Work, $"{room?.Name} 젖은 바닥 — 고무 매트를 깔고 그 위에서 작업한다 (고무는 젖어도 전기를 막는다)", c.Id);
        if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: 젖은 바닥에 고무 매트");
    }

    /// <summary>열 · 불에서 떨어진 안전한 칸 (같은 방 · 빈 칸 · 젖지 않은).</summary>
    internal Cell? SafeSpot(Cell from, int minAway, bool wantDry, bool warm = false)
    {
        var w = _w;
        var room = w.Ship.RoomAt(from);
        if (room == null) return null;
        Cell? best = null;
        float bs = float.MaxValue;
        foreach (var c in room.Cells)
        {
            if (!w.Ship.IsOpenFloor(c) || w.Ship.DoorAt(c) != null || Any(c)) continue;
            if (HeatAt(c) > (warm ? 35f : 8f) || w.Fire.Count > 0 && w.Fire.AnyWithin(c, minAway)) continue;
            if (wantDry && (w.Body.Mark(c, CellMark.Wet) > 0.15f || LitersAt(c) > 0.05f)) continue;
            int away = Math.Max(Math.Abs(c.X - from.X), Math.Abs(c.Y - from.Y));
            if (away < minAway) continue;
            float s = away + (warm ? -HeatAt(c) * 0.1f : 0f);
            if (s < bs) { bs = s; best = c; }
        }
        return best;
    }

    internal bool Hot(Article t)
    {
        foreach (var d in _w.Portable.Devices)
            if (d.Kind == PortableKind.Heater && d.Running && d.Placed && Math.Max(Math.Abs(t.At.X - d.At.X), Math.Abs(t.At.Y - d.At.Y)) <= 1) return true;
        return _w.Fire.Count > 0 && _w.Fire.At(t.At) <= 0f && _w.Fire.AnyWithin(t.At, 2.2f);
    }

    internal bool Alarmed(Room r) => _alarmed.Contains(r.Id);
}

public sealed class MatterActivity : Activity
{
    public override string Id => "matter";
    public override string Label => "물건 · 재질 손보기";

    private enum Task { Smolder, Heat, Rug, Junction, ReturnRug, Aisle, Handle }

    private readonly record struct Pick_(Task Task, Article? Article, Junction? Junction, Door? Door, Cell Spot, float Value);

    private static Pick_? Pick(CrewMember c, World w, DistanceField dist)
    {
        var m = w.Matter;
        Pick_? best = null;
        float bs = 0f;
        void Consider(Task task, Article? t, Junction? j, Door? d, Cell spot, float value)
        {
            int dd = dist.Get(spot);
            if (dd < 0 && task == Task.Smolder && w.Matter.SmokeSealed(w.Ship.RoomAt(spot)!)) dd = 600; // 연기로 닫힌 방: 비상 개방하고 들어간다
            if (dd < 0) return;
            float s = value - dd / 3000f;
            if (s > bs) { bs = s; best = new Pick_(task, t, j, d, spot, value); }
        }
        bool quiet = w.Tick < m.QuietUntil;
        foreach (var t in m.Things)
        {
            if (t.CarriedBy >= 0 || t.ClaimedBy >= 0 && t.ClaimedBy != c.Id && !t.Smolder) continue; // 연기 나는 것엔 여럿이 달려간다 (먼저 닿은 사람이 치운다)
            var room = w.Ship.RoomAt(t.At);
            if (room == null || room.Detached) continue;
            // 그을리는 것: 경보 · 눈 · 탄 냄새로 안다
            // (불 · 구멍 · 나쁜 공기라고 믿는 방은 들어가지 않는다 — 그 방은 소화 · 대피의 일: 두뇌 2.0 믿음)
            if (t.Smolder && w.Fire.At(t.At) <= 0f && (room == c.Room || w.Brain2.Beliefs.SafeEnough(c, room)) && (m.Alarmed(room) || c.Room == room || t.Known && t.Char > 0.1f || w.Smells.Smelled(c, SmellKind.Burnt, SimTime.Minutes(40)) is { } sn && sn.Room == room.Id))
            { Consider(Task.Smolder, t, null, null, t.At, 0.95f); continue; }
            // 히터 · 불 곁의 천 · 종이 · 플라스틱 (보이는 것 · 컴퓨터가 짚은 것)
            if (!quiet && (t.Known || t.Flagged) && Matter.React(t.Mat, Element.Heat) is Reaction.Char or Reaction.Melt && t.Char < 1f && t.Stage != BreakStage.Shards && m.Hot(t))
                Consider(Task.Heat, t, null, null, t.At, 0.45f + (t.Flagged ? 0.3f : 0f) + (t.Char > 0f ? 0.2f : 0f));
            // 물을 다 머금은 러그 · 아래가 의심스러운 러그: 걷어 널어 말린다
            if (t.Kind == ArticleKind.Rug && !t.Hung && !t.Stowed && (t.WetFrac >= 0.97f && t.Known || m.JunctionAt(t.At) is { Suspected: true }))
                Consider(Task.Rug, t, null, null, t.At, 0.45f + (m.JunctionAt(t.At) is { Suspected: true } ? 0.2f : 0f));
            // 다 마른 러그를 제자리로 (아래 접속부를 고쳤으면)
            if (t.Kind == ArticleKind.Rug && t.Hung && t.WetFrac < 0.1f && t.Home != t.At && w.Body.Mark(t.Home, CellMark.Wet) < 0.1f && m.LitersAt(t.Home) < 0.05f
                && (m.JunctionAt(t.Home) is not { } jh || jh.Taped || jh.Wet < 0.05f))
                Consider(Task.ReturnRug, t, null, null, t.At, 0.15f);
            // 통로를 막은 짐
            if (t.Spec.Bulk >= 6 && t.Known && m.InAisle(t.At)) Consider(Task.Aisle, t, null, null, t.At, 0.18f);
        }
        foreach (var j in m.Junctions)
            if (j.Known && !j.Fixed && (j.ClaimedBy < 0 || j.ClaimedBy == c.Id))
                Consider(Task.Junction, null, j, null, j.At, 0.6f + 0.2f * c.SkillLevel(Skill.Electrical));
        foreach (var d in w.Ship.Doors)
        {
            if (d.Removed || d.IsExternal || m.HandleSoil(d, SoilKind.Bio) < 0.3f) continue; // 균 묻은 손잡이
            if (!(d.RoomA is Room a && SoilSystem.CleanRoom(a) || d.RoomB is Room b && SoilSystem.CleanRoom(b))) continue;
            foreach (var dd in Cell.Dirs4) if (w.Ship.IsOpenFloor(d.Cell + dd)) { Consider(Task.Handle, null, null, d, d.Cell + dd, 0.2f); break; }
        }
        return best;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.IsChild || c.Needs.Rest < 0.15f) return (0f, "—");
        var p = Pick(c, w, dist);
        if (p is not Pick_ pk) return (0f, "손볼 것 없음");
        if (pk.Task != Task.Smolder && Crisis.Acting(w)) return (0f, "위기");
        float s = pk.Task == Task.Smolder ? pk.Value : pk.Value * (OnShift(c, w) ? 1f : 0.6f) * (0.7f + 0.5f * c.Traits.Diligence);
        return (s, Name(pk.Task));
    }

    private static string Name(Task t) => t switch
    {
        Task.Smolder => "그을리는 것 치우기", Task.Heat => "불 · 히터 곁의 천 치우기", Task.Rug => "젖은 러그 걷어 말리기", Task.Junction => "젖은 접속부 말리고 감기",
        Task.ReturnRug => "마른 러그 제자리에", Task.Aisle => "통로 짐 치우기", _ => "문 손잡이 닦기",
    };

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var p = Pick(c, w, dist);
        if (p is not Pick_ pk) return null;
        var m = w.Matter;
        var toils = Plans.DropOff(c, w, dist);
        var t = pk.Article;
        string label = Name(pk.Task);
        switch (pk.Task)
        {
            case Task.Smolder or Task.Heat or Task.Aisle or Task.ReturnRug:
            {
                if (t == null) return null;
                Cell? dest = pk.Task switch
                {
                    Task.Aisle => StoreSpot(w, dist, t),
                    Task.ReturnRug => t.Home,
                    _ => m.SafeSpot(t.At, 3, wantDry: false),
                };
                if (dest is not Cell to) return null;
                m.Claim(t, c);
                var task = pk.Task;
                toils.Add(new GotoToil(t.At));
                toils.Add(new DoToil((cm, world) => { if (t.CarriedBy >= 0 || !world.Matter.Things.Contains(t)) return false; world.Matter.Carry(t, cm); return true; }));
                toils.Add(new GotoToil(to));
                toils.Add(new DoToil((cm, world) => world.Matter.Finish(cm, task.ToString(), t, to)));
                break;
            }
            case Task.Rug:
            {
                if (t == null) return null;
                var dry = m.SafeSpot(t.At, 2, wantDry: true, warm: true);
                if (dry is not Cell to) return null;
                m.Claim(t, c);
                toils.Add(new GotoToil(t.At));
                toils.Add(new WorkToil(0.08f, Skill.Mechanics, t.At.Center));
                toils.Add(new DoToil((cm, world) => world.Matter.LiftRug(cm, t)));
                toils.Add(new GotoToil(to));
                toils.Add(new DoToil((cm, world) => world.Matter.Finish(cm, "Hang", t, to)));
                break;
            }
            case Task.Junction:
            {
                var j = pk.Junction!;
                j.ClaimedBy = c.Id;
                ItemKind? tape = w.Ship.CountStored(ItemKind.Tape) > 0 ? ItemKind.Tape : w.Ship.CountStored(ItemKind.Insulator) > 0 ? ItemKind.Insulator : null;
                if (tape is ItemKind ik)
                {
                    var (box, bs) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Count(ik) > 0);
                    if (box != null) { toils.Add(new GotoToil(bs)); toils.Add(new TakeToil(box, ik, 1)); } else tape = null;
                }
                m.Footing(c, dist, toils, j.At); // 젖은 바닥이면 고무 매트부터
                toils.Add(new GotoToil(j.At));
                toils.Add(new WorkToil(0.4f, Skill.Electrical, j.At.Center));
                toils.Add(new DoToil((cm, world) => world.Matter.FixJunction(cm, j, tape)));
                return new Job(this, label, toils)
                {
                    LogText = label,
                    OnFinished = (cm, world, st) => { if (j.ClaimedBy == cm.Id) j.ClaimedBy = -1; },
                };
            }
            case Task.Handle:
            {
                var d = pk.Door!;
                toils.Add(new GotoToil(pk.Spot));
                toils.Add(new WorkToil(0.05f, Skill.Medicine, d.Cell.Center));
                toils.Add(new DoToil((cm, world) => { world.Matter.WipeHandle(d); world.Log.Add(world.Tick, LogKind.Work, "문 손잡이를 닦았다 — 손에서 손으로 옮는다", cm.Id); return true; }));
                break;
            }
        }
        return new Job(this, label, toils)
        {
            LogText = label,
            Urgent = pk.Task == Task.Smolder, // 그을음: 닫힌 문도 비상 개방한다
            OnFinished = (cm, world, st) => { if (t != null && t.ClaimedBy == cm.Id) t.ClaimedBy = -1; },
        };
    }

    private static Cell? StoreSpot(World w, DistanceField dist, Article t)
    {
        Cell? best = null;
        int bd = int.MaxValue;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Kind is not (RoomType.Storage or RoomType.Cargo)) continue;
            foreach (var c in r.Cells)
            {
                if (!w.Ship.IsOpenFloor(c) || w.Ship.DoorAt(c) != null || w.Matter.Any(c)) continue;
                int d = dist.Get(c);
                if (d >= 0 && d < bd) { bd = d; best = c; }
            }
        }
        return best;
    }
}

public sealed partial class MatterSystem
{
    /// <summary>러그를 걷는다 — 그 아래가 드러난다.</summary>
    internal bool LiftRug(CrewMember c, Article rug)
    {
        if (rug.CarriedBy >= 0 || !Things.Contains(rug)) return false;
        var at = rug.At;
        Carry(rug, c);
        Stats.Lifted++;
        if (JunctionAt(at) is { } j) Expose(rug, j, c);
        _w.Log.Add(_w.Tick, LogKind.Work, $"물을 머금은 {Ko.EulReul(rug.Name)} 걷었다 ({rug.Water:0.0}L)", c.Id);
        return true;
    }

    /// <summary>들고 온 물건을 내려놓는다 (치움 · 적심 · 널기 · 되돌림).</summary>
    internal bool Finish(CrewMember c, string task, Article t, Cell to)
    {
        var w = _w;
        if (t.CarriedBy != c.Id) return false;
        _carryJob.Remove(t.Id);
        var room = w.Ship.RoomAt(to);
        Place(t, to);
        t.ClaimedBy = -1;
        t.Off = new Vector2(((t.Id * 37) % 7 - 3) * 0.05f, ((t.Id * 53) % 7 - 3) * 0.05f);
        bool heeded = t.Flagged;
        switch (task)
        {
            case "Smolder":
                t.Water = MathF.Max(t.Water, t.Spec.Capacity > 0f ? t.Spec.Capacity : 0.3f); // 적신다
                t.Smolder = false;
                t.Temp = room?.Air.Temperature ?? 20f;
                Stats.Doused++;
                string off = "";
                foreach (var d in w.Portable.Devices) // 열원(히터)도 끈다
                    if (d.Kind == PortableKind.Heater && d.On && d.Placed && _carryFrom.TryGetValue(t.Id, out var from) && Math.Max(Math.Abs(d.At.X - from.X), Math.Abs(d.At.Y - from.Y)) <= 2 && w.Ship.RoomAt(d.At) == room) { d.On = false; d.Running = false; off = " · 곁의 히터도 껐다"; }
                w.Log.Add(w.Tick, LogKind.Work, $"그을던 {Ko.EulReul(t.Name)} 열에서 떼어 물에 적셨다 — 연기가 멎는다{off}", c.Id);
                if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: 그을던 {Ko.EulReul(t.Name)} 치우고 적심");
                MarkLog.Add(c.Memory.Marks, w.Tick, $"그을던 {Ko.EulReul(t.Name)} 찾아 껐다");
                break;
            case "Heat":
                Stats.MovedFromHeat++;
                w.Log.Add(w.Tick, LogKind.Work, $"{(t.WetFrac > 0.2f ? "젖은 " : "")}{Ko.EulReul(t.Name)} 열에서 떨어뜨려 놓았다 — {Ko.EunNeun(Materials.Name(t.Mat))} 달궈지면 {Matter.Name(Matter.React(t.Mat, Element.Heat))}", c.Id);
                break;
            case "Hang":
                t.Hung = true;
                Stats.Hung++;
                w.Log.Add(w.Tick, LogKind.Work, $"젖은 {Ko.EulReul(t.Name)} 널어 말린다", c.Id);
                break;
            case "ReturnRug":
                t.Hung = false;
                Stats.Returned++;
                w.Log.Add(w.Tick, LogKind.Work, $"마른 {Ko.EulReul(t.Name)} 제자리에 깔았다", c.Id);
                break;
            case "Aisle":
                Stats.Aisles++;
                w.Log.Add(w.Tick, LogKind.Work, $"통로를 막은 {Ko.EulReul(t.Name)} 창고로 치웠다", c.Id);
                break;
        }
        if (heeded) { Stats.HeededWarns++; t.Flagged = false; }
        return true;
    }

    internal bool FixJunction(CrewMember c, Junction j, ItemKind? tape)
    {
        var w = _w;
        bool taped = false;
        if (tape is ItemKind ik && c.Carrying is ItemStack held && held.Kind == ik)
        {
            c.Carrying = held.Count > 1 ? new ItemStack(ik, held.Count - 1) : null;
            taped = true;
        }
        j.Wet = 0f;
        j.Taped = taped;
        j.Fixed = true;
        j.Live = false;
        j.Suspected = false;
        j.ClaimedBy = -1;
        Stats.JunctionsFixed++;
        var room = w.Ship.RoomAt(j.At);
        w.Log.Add(w.Tick, LogKind.Work, $"젖은 배선 접속부를 말리고 {(taped ? "절연 테이프로 감았다" : "닦아 말렸다 (테이프가 없다)")}", c.Id);
        if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: 바닥 아래 접속부 수리");
        w.Board.RequestScan();
        return true;
    }
}
