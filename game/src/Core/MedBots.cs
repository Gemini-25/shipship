using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// 의료 3차 — 의료 로봇: 사람과 같은 일을 나눠 맡는다 (로봇이 멎으면 사람이 떠맡는다).
//   들것 로봇: 쓰러진 사람을 들것에 실어 치료 침대(팔이 집도할 수술이면 수술대)로 · 싣는 길에 상처를 누른다.
//   간호 로봇: 상처 누르기(서랍의 지혈 거즈) · 혈액 냉장고의 피 · 구급 키트 나르기 · 인공 폐 손 펌프 · 격리실 소독(소독약 · 없으면 자외선 등).
//   잡역 · 배식 · 운반 로봇도 피와 구급 키트는 나른다 (잡역 로봇은 손 펌프도).
//   순서: 위중한 사람부터 (부상 등급) — 주컴퓨터(V 지휘)가 맡기고, 작업 목록에 그 까닭을 남긴다.
//   사람과 나눔: 사람이 이미 맡은 구조는 건드리지 않는다 · 로봇이 맡은 구조 · 피 · 손 펌프는 사람이 비켜 간다 —
//     로봇이 고장 · 방전으로 서면 들것의 사람을 그 자리에 내려놓고, 일은 작업 목록으로 돌아가 사람이 한다.
public enum MedTask : byte { None, Carry, Press, Blood, Kit, Crank, Disinfect }

public sealed class MedBotSystem
{
    private readonly World _w;
    private readonly SortedDictionary<int, (MedTask task, int target)> _task = new(); // 로봇 → 하는 일 · 대상(사람 · 가구 · 방)
    private readonly SortedDictionary<int, int> _carry = new();          // 들것 로봇 → 실은 사람
    private readonly SortedDictionary<int, int> _pressed = new();        // 외상 번호 → 누른 로봇
    private readonly SortedDictionary<int, (int bot, long at)> _claim = new(); // 피를 가지러 간 사람 → 로봇
    private readonly SortedSet<int> _holding = new();                    // 피 · 키트를 실은 로봇
    private readonly SortedDictionary<int, long> _crank = new();         // 로봇이 손 펌프를 돌린 때 (가구)
    private readonly SortedDictionary<int, long> _cleaned = new();       // 소독한 때 (방)
    private readonly SortedDictionary<int, long> _downSince = new();     // 쓰러진 때 (늦은 구조를 센다)
    public int Carried, Pressed, Stopped, Delivered, Transfused, Kits, Cranks, Disinfected, Handoffs, Dropped, LateRescues, Orders;

    // 시스템 틱마다 다시 모으는 일감 (로봇은 아무 틱에나 고른다)
    private readonly List<WorkOrder> _rescue = new();
    private readonly List<CrewMember> _toTable = new();
    private readonly List<(CrewMember pt, Trauma t)> _bleed = new();
    private readonly List<CrewMember> _blood = new();
    private readonly List<(CrewMember pt, Trauma t)> _kit = new();
    private readonly List<Furniture> _ecmo = new();
    private readonly List<Room> _dirty = new();

    public MedBotSystem(World w) => _w = w;

    private CrewMember? P(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }
    private Robot? Bot(int id) { foreach (var r in _w.Robots.Robots) if (r.Id == id) return r; return null; }

    public static bool Can(RobotKind k, MedTask t) => k switch
    {
        RobotKind.Stretcher => t is MedTask.Carry or MedTask.Press,
        RobotKind.Nurse => t is MedTask.Press or MedTask.Blood or MedTask.Kit or MedTask.Crank or MedTask.Disinfect,
        RobotKind.Utility => t is MedTask.Blood or MedTask.Kit or MedTask.Crank,
        RobotKind.Courier or RobotKind.Hauler => t is MedTask.Blood or MedTask.Kit,
        _ => false,
    };

    public static string TaskName(MedTask t) => t switch
    {
        MedTask.Carry => "들것", MedTask.Press => "지혈", MedTask.Blood => "피 나르기", MedTask.Kit => "구급 키트", MedTask.Crank => "손 펌프", MedTask.Disinfect => "소독", _ => "",
    };

    public MedTask TaskOf(Robot r) => _task.TryGetValue(r.Id, out var x) && r.Steps != null ? x.task : MedTask.None;
    public CrewMember? Carrying(Robot r) => _carry.TryGetValue(r.Id, out var id) ? P(id) : null;
    public bool CarriedByBot(CrewMember c) => _carry.ContainsValue(c.Id);
    public bool Holding(Robot r) => _holding.Contains(r.Id);
    public bool Cranking(Furniture f) => _crank.TryGetValue(f.Id, out var t) && _w.Tick - t < SimTime.Minutes(3);
    public bool CanCarry() => _w.Robots.Robots.Any(r => r.Kind == RobotKind.Stretcher && r.Operational && r.Battery > 0.3f);
    public bool Claimed(CrewMember pt) => _claim.TryGetValue(pt.Id, out var x) && _w.Tick - x.at < SimTime.Minutes(40) && Bot(x.bot) is Robot r && r.Operational && TaskOf(r) == MedTask.Blood;
    private bool Taken(MedTask t, int target) { foreach (var kv in _task) if (kv.Value.task == t && kv.Value.target == target && Bot(kv.Key) is Robot r && r.Steps != null && r.Operational) return true; return false; }

    /// <summary>이 로봇이 할 의료 일이 있나 (Robots.Decide가 묻는다 — 싸게).</summary>
    public bool Wants(Robot r)
    {
        var k = r.Kind;
        if (!(Can(k, MedTask.Carry) || Can(k, MedTask.Press) || Can(k, MedTask.Blood) || Can(k, MedTask.Kit) || Can(k, MedTask.Crank) || Can(k, MedTask.Disinfect))) return false;
        return Can(k, MedTask.Carry) && (_rescue.Count > 0 || _toTable.Count > 0) || Can(k, MedTask.Press) && _bleed.Count > 0 || Can(k, MedTask.Blood) && _blood.Count > 0
            || Can(k, MedTask.Kit) && _kit.Count > 0 || Can(k, MedTask.Crank) && _ecmo.Count > 0 || Can(k, MedTask.Disinfect) && _dirty.Count > 0;
    }

    private static float GradeW(World w, CrewMember c) => w.Grades.Now(c) switch { InjuryGrade.Critical => 1f, InjuryGrade.Serious => 0.5f, InjuryGrade.Minor => 0.15f, _ => 0f };

    /// <summary>위중한 사람부터 — 가장 급한 일 하나를 골라 맡긴다.</summary>
    public bool Plan(Robot r, DistanceField dist)
    {
        var w = _w;
        var k = r.Kind;
        float best = float.MinValue;
        Func<List<RobotStep>?>? build = null;
        MedTask pick = MedTask.None; int target = -1; string what = ""; Room? where = null; string why = "";
        void Offer(MedTask t, int tg, float score, Func<List<RobotStep>?> b, string text, Room? room, string reason)
        {
            if (!Can(k, t) || score <= best || Taken(t, tg)) return;
            best = score; build = b; pick = t; target = tg; what = text; where = room; why = reason;
        }
        Cell? Near(CrewMember pt)
        {
            Cell? at = null; int bd = int.MaxValue;
            foreach (var d in Cell.Dirs8)
            {
                var c = pt.Cell + d;
                int g = dist.Get(c);
                if (g < 0 || g >= bd || !w.Ship.IsWalkable(c)) continue;
                bd = g; at = c;
            }
            if (at == null && dist.Reachable(pt.Cell)) at = pt.Cell;
            return at;
        }
        float D(Cell c) => dist.Get(c) / 9000f;

        if (Can(k, MedTask.Carry))
        {
            foreach (var o in _rescue)
                if (o.Target.Crew is CrewMember pt && Near(pt) is Cell at && !o.Closed && o.Robot == null && o.Assignee == null)
                    Offer(MedTask.Carry, pt.Id, 3f + GradeW(w, pt) - D(at), () => Carry(r, pt, at, dist, o), $"들것으로 {Ko.EulReul(pt.Name)} 옮긴다", pt.Room, $"{pt.Name} 쓰러졌다 · {InjuryGradeSystem.Name(w.Grades.Now(pt))}");
            foreach (var pt in _toTable)
                if (Near(pt) is Cell at)
                    Offer(MedTask.Carry, pt.Id, 3.2f + GradeW(w, pt) - D(at), () => Carry(r, pt, at, dist, null), $"들것으로 {Ko.EulReul(pt.Name)} 수술대로", pt.Room, "수술 팔이 기다린다");
        }
        if (Can(k, MedTask.Press))
            foreach (var (pt, t) in _bleed)
                if (Near(pt) is Cell at)
                    Offer(MedTask.Press, pt.Id, 2.5f + GradeW(w, pt) + t.Rate - D(at), () => new List<RobotStep> { new RGoto(at), new RPress(pt, this) }, $"{pt.Name} 상처를 누른다", pt.Room, $"피가 난다 (시간당 {t.Rate * 100:0}%)");
        if (Can(k, MedTask.Kit))
            foreach (var (pt, t) in _kit)
                if (Near(pt) is Cell at && Box(dist, ItemKind.MedKit) is (Furniture box, Cell bs))
                    Offer(MedTask.Kit, pt.Id, 2.4f + GradeW(w, pt) - D(at), () => Kit(r, pt, box, bs, at), $"구급 키트를 {pt.Name}에게", pt.Room, "누르는 손만으로는 멎지 않는다");
        if (Can(k, MedTask.Crank))
            foreach (var f in _ecmo)
                if (f.UseSpots.Where(s => dist.Reachable(s)).OrderBy(s => dist.Get(s)).Cast<Cell?>().FirstOrDefault() is Cell at)
                    Offer(MedTask.Crank, f.Id, 2.8f + (w.Organs.PatientOn(f) is CrewMember hp ? GradeW(w, hp) : 0f) - D(at), () => new List<RobotStep> { new RGoto(at), new RCrank(f, this) }, $"{f.Name} 손 펌프를 돌린다", f.Room, "전기가 끊겼다 · 내장 전지가 바닥난다");
        if (Can(k, MedTask.Blood))
            foreach (var pt in _blood)
                if (Near(pt) is Cell at && Fridge(dist) is (Furniture fr, Cell fs))
                    Offer(MedTask.Blood, pt.Id, 2.2f + GradeW(w, pt) - D(at), () => Blood(r, pt, fr, fs, at), $"혈액 냉장고 → {pt.Name}", pt.Room, $"피가 모자라다 (체력 {pt.Vitals.Health * 100:0}%)");
        if (Can(k, MedTask.Disinfect))
            foreach (var room in _dirty)
                if (room.Cells.Where(c => w.Ship.IsWalkable(c) && dist.Reachable(c)).OrderBy(c => (c.Center - room.Center).LengthSquared()).ThenBy(c => c.X).ThenBy(c => c.Y).Cast<Cell?>().FirstOrDefault() is Cell at)
                    Offer(MedTask.Disinfect, room.Id, 0.8f - D(at), () => new List<RobotStep> { new RGoto(at), new RWork(0.4f, null, room.Center), new RDo((rb, world) => { world.MedBots.Clean(rb, room); return true; }) }, $"{room.Name} 소독", room, "균이 쌓였다");
        if (build == null) return false;
        var steps = build();
        if (steps == null || steps.Count == 0) return false;
        _task[r.Id] = (pick, target);
        w.Robots.Begin(r, steps, what);
        Orders++;
        var a = w.Automation;
        if (a.Present && a.CoreOnline && a.Level >= 5)
            a.Book.Add(ActKind.Plan, where, why, $"{r.Name}이(가) 가장 가깝다 · 위중한 사람부터", $"{r.Name}: {what}", "", $"medbot:{r.Id}:{(int)pick}:{target}", SimTime.Minutes(30));
        return true;
    }

    // ───────────── 들것 ─────────────

    private List<RobotStep>? Carry(Robot r, CrewMember pt, Cell at, DistanceField dist, WorkOrder? o)
    {
        var w = _w;
        var arm = w.Surgery.CaseOf(pt) is SurgeryCase k && w.SurgArm.Leads(k) && w.Surgery.TableOf(k) is Furniture tb ? tb : null;
        Furniture? bed = null;
        Cell dest;
        if (arm != null) dest = arm.Cells[0];
        else
        {
            bed = w.Ship.FurnitureOf(FurnitureType.MedBed).Where(b => (b.ReservedBy == null || b.ReservedBy == pt) && b.UseSpots.Count > 0 && dist.Reachable(b.UseSpots[0]) && !b.Room.Detached)
                .OrderBy(b => dist.Get(b.UseSpots[0])).ThenBy(b => b.Id).FirstOrDefault();
            if (bed != null) dest = bed.UseSpots[0];
            else if (w.Ship.RoomsOf(RoomType.Medbay).Where(m => !m.Detached && !m.Leaking).SelectMany(m => m.Cells).Where(c => w.Ship.IsOpenFloor(c) && dist.Reachable(c)).OrderBy(c => dist.Get(c)).ThenBy(c => c.X).ThenBy(c => c.Y).Cast<Cell?>().FirstOrDefault() is Cell safe) dest = safe;
            else return null;
        }
        if (o != null) { o.Robot = r; r.Order = o; }
        var steps = new List<RobotStep>
        {
            new RGoto(at),
            new RDo((rb, world) =>
            {
                if (!pt.Down || pt.Dead || pt.CarriedBy != null || CarriedByBot(pt) || (pt.Position - rb.Position).Length() > 2.4f) return false;
                if (pt.CareBed is Furniture cb) { if (cb.ReservedBy == pt) cb.ReservedBy = null; pt.CareBed = null; }
                pt.LaidSafe = false;
                _carry[rb.Id] = pt.Id;
                world.Log.Add(world.Tick, LogKind.Work, $"{Ko.IGa(rb.Name)} 쓰러진 {Ko.EulReul(pt.Name)} 들것에 실었다 — 띠를 채운다", pt.Id);
                if (world.Casualty.Of(pt) is { Kind: TraumaKind.Bleed, Closed: false } t && t.Rate >= 0.05f) Press(rb, pt, t, false); // 실으면서 상처 위에 누름 띠
                return true;
            }),
            new RCarry(pt, dest),
            new RDo((rb, world) =>
            {
                if (!_carry.Remove(rb.Id)) return false;
                pt.Position = dest.Center; pt.PreviousPosition = dest.Center; pt.Room = world.Ship.RoomAt(dest);
                if (arm != null) arm.ReservedBy = pt;
                else if (bed != null && (bed.ReservedBy == null || bed.ReservedBy == pt)) { bed.ReservedBy = pt; pt.CareBed = bed; }
                else pt.LaidSafe = true;
                Carried++;
                if (o != null && !o.Closed) world.Board.Close(o);
                world.Board.RequestScan();
                string to = arm != null ? "수술대" : bed != null ? "치료 침대" : pt.Room?.Name ?? "의무실";
                world.Log.Add(world.Tick, LogKind.Work, $"{Ko.IGa(rb.Name)} {Ko.EulReul(pt.Name)} {Ko.EuRo(to)} 옮겼다", pt.Id);
                MarkLog.Add(pt.Memory.Marks, world.Tick, $"{Ko.IGa(rb.Name)} 나를 {Ko.EuRo(to)} 옮겨 줬다");
                MarkLog.Add(rb.Marks, world.Tick, $"{pt.Name}을 {Ko.EuRo(to)}");
                world.Automation.Trusts.Change(pt, 0.03f, "쓰러졌을 때 로봇이 옮겨 줬다", quiet: true);
                world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(rb.Name)} 쓰러진 {Ko.EulReul(pt.Name)} {Ko.EuRo(to)} 옮겼다", pt.Room, new[] { pt });
                return true;
            }),
        };
        return steps;
    }

    // ───────────── 지혈 ─────────────

    /// <summary>로봇이 상처를 누른다: 간호 로봇은 서랍의 지혈 거즈로 웬만한 피는 멎게 하고, 들것 로봇은 누름 띠로 늦춘다.</summary>
    internal void Press(Robot r, CrewMember pt, Trauma t, bool log = true)
    {
        var w = _w;
        if (_pressed.ContainsKey(t.Id) || t.Closed) return;
        _pressed[t.Id] = r.Id;
        Pressed++;
        bool gauze = r.Kind == RobotKind.Nurse && t.Rate < 0.22f && (ItemsV15.Use(w, ItemKind.Bandage) || ItemsV15.Use(w, ItemKind.MedKit));
        if (gauze)
        {
            Stopped++;
            w.Casualty.StopBy(pt, t, $"{Ko.IGa(r.Name)} 지혈 거즈로 눌러 피를 멎게 했다");
        }
        else
        {
            t.Rate *= 0.4f;
            if (log) w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(r.Name)} {pt.Name}의 상처를 눌러 피를 늦췄다 — 다 멎지는 않는다", pt.Id);
        }
        MarkLog.Add(pt.Memory.Marks, w.Tick, $"{Ko.IGa(r.Name)} 상처를 눌러 줬다");
    }

    // ───────────── 피 · 구급 키트 ─────────────

    private (Furniture, Cell)? Fridge(DistanceField dist)
    {
        Furniture? best = null; Cell spot = default; int bd = int.MaxValue;
        foreach (var f in _w.Blood.Fridges)
            foreach (var s in f.UseSpots) { int d = dist.Get(s); if (d >= 0 && d < bd) { bd = d; best = f; spot = s; } }
        return best != null ? (best, spot) : null;
    }

    private (Furniture, Cell)? Box(DistanceField dist, ItemKind kind)
    {
        Furniture? best = null; Cell spot = default; int bd = int.MaxValue;
        foreach (var f in _w.Ship.Containers)
        {
            if (f.Storage!.Count(kind) <= 0) continue;
            foreach (var s in f.UseSpots) { int d = dist.Get(s); if (d >= 0 && d < bd) { bd = d; best = f; spot = s; } }
        }
        return best != null ? (best, spot) : null;
    }

    /// <summary>피를 넣을 손: 곁의 의료를 아는 사람 → 데이터선이 닿는 침대의 투여 펌프(주컴퓨터).</summary>
    private (CrewMember? hand, bool pump) Infuser(CrewMember pt)
    {
        var w = _w;
        CrewMember? hand = null; float bd = 3f * 3f;
        foreach (var c in w.Crew)
        {
            if (c == pt || !c.CanAct || c.Outside || c.IsChild || c.Pose == Pose.Sleeping || c.Role != CrewRole.Medic && c.SkillLevel(Skill.Medicine) < 0.2f) continue;
            float d = (c.Position - pt.Position).LengthSquared();
            if (d < bd) { bd = d; hand = c; }
        }
        return (hand, hand == null && w.Telemed.PumpAt(pt));
    }

    private List<RobotStep> Blood(Robot r, CrewMember pt, Furniture fridge, Cell fs, Cell at)
    {
        _claim[pt.Id] = (r.Id, _w.Tick);
        return new List<RobotStep>
        {
            new RGoto(fs),
            new RDo((rb, world) =>
            {
                if (!world.Blood.NeedsBlood(pt) || pt.Dead) return false;
                bool have = world.Blood.CompatibleFor(pt) > 0 || world.Pharmacy.Stock(ItemKind.BloodSubstitute) > 0;
                if (!have) { world.Blood.Call(pt, $"{rb.Name}이(가) 냉장고를 열었는데 맞는 피가 없다"); return false; }
                _holding.Add(rb.Id);
                _claim[pt.Id] = (rb.Id, world.Tick);
                return true;
            }),
            new RGoto(at),
            new RWaitFor((rb, world) => pt.Dead || !world.Blood.NeedsBlood(pt) || Infuser(pt) is var x && (x.hand != null || x.pump), SimTime.Minutes(20)),
            new RDo((rb, world) =>
            {
                _holding.Remove(rb.Id);
                _claim.Remove(pt.Id);
                if (pt.Dead || !world.Blood.NeedsBlood(pt)) return true;
                Delivered++;
                var (hand, pump) = Infuser(pt);
                if (hand == null && !pump) { world.Log.Add(world.Tick, LogKind.Warning, $"{Ko.IGa(rb.Name)} 피를 들고 왔지만 넣어 줄 손이 없다 — 냉장고로 되돌린다", pt.Id); return false; }
                string did = world.Blood.Transfuse(pt, hand, false);
                if (did == "") return false;
                Transfused++;
                world.Log.Add(world.Tick, LogKind.Work, hand != null ? $"{Ko.IGa(hand.Name)} {rb.Name}이(가) 가져온 피를 {pt.Name}에게 넣었다 ({did})"
                    : $"주컴퓨터가 침대 투여 펌프로 {rb.Name}이(가) 가져온 피를 {pt.Name}에게 넣었다 ({did})", pt.Id);
                MarkLog.Add(rb.Marks, world.Tick, $"{pt.Name}에게 피를 날랐다");
                return true;
            }),
        };
    }

    private List<RobotStep> Kit(Robot r, CrewMember pt, Furniture box, Cell bs, Cell at) => new()
    {
        new RGoto(bs),
        new RTake(box, ItemKind.MedKit, 1),
        new RDo((rb, world) => { _holding.Add(rb.Id); return true; }),
        new RGoto(at),
        new RDo((rb, world) =>
        {
            _holding.Remove(rb.Id);
            if (rb.Cargo is not ItemStack { Kind: ItemKind.MedKit }) return false;
            rb.Cargo = rb.Cargo.Value.Count > 1 ? new ItemStack(ItemKind.MedKit, rb.Cargo.Value.Count - 1) : null;
            Kits++;
            if (world.Casualty.Of(pt) is not { Kind: TraumaKind.Bleed, Closed: false } t) return true;
            var (hand, _) = Infuser(pt);
            if (hand != null)
            {
                world.Casualty.StopBy(pt, t, $"{Ko.IGa(hand.Name)} {rb.Name}이(가) 가져온 구급 키트로 피를 멎게 했다");
                pt.ChangeAffinity(hand, 0.06f);
                hand.Practice(Skill.Medicine, 0.02f);
                MarkLog.Add(hand.Memory.Marks, world.Tick, $"{rb.Name}이(가) 가져온 키트로 {pt.Name}의 피를 멎게 했다");
            }
            else if (t.Rate < 0.22f) { Stopped++; world.Casualty.StopBy(pt, t, $"{Ko.IGa(rb.Name)} 구급 키트의 지혈대로 피를 멎게 했다"); }
            else t.Rate *= 0.5f;
            return true;
        }),
    };

    // ───────────── 손 펌프 · 소독 ─────────────

    internal void Cranked(Robot r, Furniture f)
    {
        if (!Cranking(f)) Cranks++;
        _crank[f.Id] = _w.Tick;
        _w.Organs.CrankBot(r, f);
    }

    internal void Clean(Robot r, Room room)
    {
        var w = _w;
        var soil = w.Soil.RoomSoil(room);
        bool dis = ItemsV15.Use(w, ItemKind.Disinfectant);
        soil[(int)SoilKind.Bio] *= dis ? 0.15f : 0.55f;
        _cleaned[room.Id] = w.Tick;
        Disinfected++;
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(r.Name)} {Ko.EulReul(room.Name)} 소독했다 ({(dis ? "소독약을 뿌리고 닦았다" : "소독약이 없어 자외선 등만")})");
        MarkLog.Add(room.Marks, w.Tick, $"{r.Name} 소독");
    }

    // ───────────── 시스템 틱 ─────────────

    public void Update(float dt)
    {
        var w = _w;
        Watch();
        if (w.Robots.Robots.Count == 0) { Collect(false); return; }
        Collect(true);
    }

    /// <summary>들것 로봇이 멎었다 — 실은 사람을 그 자리에 내려놓는다 (일은 작업 목록으로).</summary>
    private void Watch()
    {
        var w = _w;
        foreach (var id in _carry.Keys.ToList())
        {
            var r = Bot(id);
            var pt = P(_carry[id]);
            if (r != null && r.State == RobotState.Active && r.Steps != null && r.Fault == null && pt != null && !pt.Dead && TaskOf(r) == MedTask.Carry) continue;
            _carry.Remove(id);
            if (pt == null || pt.Dead) continue;
            Dropped++;
            pt.LaidSafe = false;
            w.Log.Add(w.Tick, LogKind.Warning, $"{r?.Name ?? "들것 로봇"}이(가) 멈췄다 — {Ko.EulReul(pt.Name)} {pt.Room?.Name ?? "그 자리"}에 내려놓았다 · 사람이 옮겨야 한다", pt.Id);
            if (w.Automation.Present && w.Automation.CoreOnline)
                w.Automation.Speak.Announce(w.Automation.Voice.Style($"{pt.Room?.Name} — 들것 로봇이 섰습니다. {pt.Name}을 사람이 옮겨 주십시오"), pt.Room, 3);
            w.Board.RequestScan();
        }
        foreach (var id in _task.Keys.ToList()) if (Bot(id) is not Robot r || r.Steps == null) { _task.Remove(id); _holding.Remove(id); }
        foreach (var id in _claim.Keys.ToList()) if (w.Tick - _claim[id].at > SimTime.Minutes(40)) _claim.Remove(id);
        foreach (var id in _pressed.Keys.ToList()) if (!w.Casualty.Open.Any(t => t.Id == id)) _pressed.Remove(id);
        // 늦은 구조 (들것 로봇을 부르는 까닭)
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away) { _downSince.Remove(c.Id); continue; }
            bool lying = c.Down && c.CarriedBy == null && c.CareBed == null && !c.LaidSafe && !CarriedByBot(c);
            if (lying) { if (!_downSince.ContainsKey(c.Id)) _downSince[c.Id] = w.Tick; continue; }
            if (_downSince.TryGetValue(c.Id, out var since)) { if (w.Tick - since > SimTime.Minutes(20)) LateRescues++; _downSince.Remove(c.Id); }
        }
    }

    /// <summary>일감을 모은다 (위중한 사람부터 고르는 건 Plan이).</summary>
    private void Collect(bool any)
    {
        var w = _w;
        _rescue.Clear(); _toTable.Clear(); _bleed.Clear(); _blood.Clear(); _kit.Clear(); _ecmo.Clear(); _dirty.Clear();
        if (!any) return;
        foreach (var o in w.Board.OpenUnsorted)
            if (o.Kind == WorkKind.Rescue && o.Robot == null && o.Assignee == null && o.Target.Crew is CrewMember pt && pt.Down && !pt.Dead && !pt.Outside && pt.CarriedBy == null && !CarriedByBot(pt))
                _rescue.Add(o);
        _rescue.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var k in w.Surgery.Cases)
            if (w.SurgArm.Leads(k) && P(k.Patient) is CrewMember pt && pt.Down && !pt.Dead && pt.CarriedBy == null && !CarriedByBot(pt) && w.Surgery.TableOf(k) is Furniture tb && !tb.Cells.Contains(pt.Cell))
                _toTable.Add(pt);
        foreach (var t in w.Casualty.Open)
        {
            if (t.Closed || P(t.CrewId) is not CrewMember pt || pt.Dead || pt.Outside || pt.Away || w.Surgery.OnTable(pt)) continue;
            if (t.Kind == TraumaKind.Bleed && t.Rate >= 0.05f && !_pressed.ContainsKey(t.Id) && t.Helper < 0) _bleed.Add((pt, t));
            if (t.Kind == TraumaKind.Bleed && t.Rate >= 0.06f && t.Helped >= 0) _kit.Add((pt, t)); // 사람이 누르고 있다 — 키트가 있어야 멎는다
        }
        foreach (var pt in w.Crew)
            if (!pt.Dead && !pt.Away && !pt.Outside && w.Blood.NeedsBlood(pt) && !Claimed(pt) && (pt.Down || pt.CareBed != null || w.Surgery.OnTable(pt)))
                _blood.Add(pt);
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.Ecmo))
            if (!f.Room.Detached && f.Machine is Machine m && !m.Powered && w.Organs.PatientOn(f) != null && w.Organs.Cell(f) < 0.25f && (!w.Organs.Cranked(f) || Cranking(f)))
                _ecmo.Add(f);
        if (w.Tick % SimTime.Minutes(30) < World.SystemInterval || _dirtyCache == null)
        {
            _dirtyCache = new List<Room>();
            var rooms = w.Ship.RoomsOf(RoomType.Medbay).ToList();
            if (w.Infection.Ward() is Room ward && !rooms.Contains(ward)) rooms.Add(ward);
            foreach (var room in rooms.OrderBy(x => x.Id))
                if (!room.Detached && !room.Leaking && w.Soil.RoomSoil(room)[(int)SoilKind.Bio] > 0.3f && (!_cleaned.TryGetValue(room.Id, out var t) || w.Tick - t > SimTime.Hours(6)))
                    _dirtyCache.Add(room);
        }
        foreach (var room in _dirtyCache) if (!_cleaned.TryGetValue(room.Id, out var t) || w.Tick - t > SimTime.Hours(6)) _dirty.Add(room);
    }
    private List<Room>? _dirtyCache;

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var kv in _task) { I(kv.Key); I((int)kv.Value.task); I(kv.Value.target); }
        foreach (var kv in _carry) { I(kv.Key); I(kv.Value); }
        foreach (var kv in _pressed) { I(kv.Key); I(kv.Value); }
        foreach (var kv in _crank) { I(kv.Key); I(kv.Value); }
        I(Carried); I(Pressed); I(Stopped); I(Delivered); I(Transfused); I(Kits); I(Cranks); I(Disinfected); I(Dropped); I(LateRescues);
    }
}

// ─────────────────────────────── 의료 로봇의 작은 단계 ───────────────────────────────

/// <summary>들것에 실은 사람과 함께 간다 (사람은 들것 위에 누워 따라온다).</summary>
internal sealed class RCarry : RobotStep
{
    private readonly CrewMember _pt;
    private readonly Cell _to;
    private bool _ok;
    public RCarry(CrewMember pt, Cell to) { _pt = pt; _to = to; }
    public override bool Moving => true;
    public override void Begin(Robot r, World w) => _ok = RobotSystem.SetDestination(r, w, _to);
    public override ToilStatus Tick(Robot r, World w)
    {
        if (!_ok || _pt.Dead || !w.MedBots.CarriedByBot(_pt)) return ToilStatus.Failed;
        bool there = RobotSystem.Move(r, w);
        _pt.Position = r.Position - r.Facing * 0.2f;
        _pt.Room = w.Ship.RoomAt(_pt.Cell) ?? _pt.Room;
        return there ? ToilStatus.Succeeded : r.Path == null ? ToilStatus.Failed : ToilStatus.Running;
    }
}

/// <summary>상처 곁에서 누른다 (2분 뒤 듣는다) — 의료를 아는 사람이 오면 손을 넘긴다.</summary>
internal sealed class RPress : RobotStep
{
    private readonly CrewMember _pt;
    private readonly MedBotSystem _m;
    private int _ticks;
    public RPress(CrewMember pt, MedBotSystem m) { _pt = pt; _m = m; }
    public override float? Progress => MathF.Min(1f, _ticks / (float)SimTime.Minutes(2));
    public override ToilStatus Tick(Robot r, World w)
    {
        var t = w.Casualty.Of(_pt);
        if (t == null || t.Closed || t.Kind != TraumaKind.Bleed || _pt.Dead) return ToilStatus.Succeeded;
        if ((_pt.Position - r.Position).LengthSquared() > 2.4f * 2.4f) return ToilStatus.Succeeded;
        if ((_pt.Position - r.Position).LengthSquared() > 0.0001f) r.Facing = Vector2.Normalize(_pt.Position - r.Position);
        _ticks++;
        if (_ticks == SimTime.Minutes(2)) _m.Press(r, _pt, t);
        if (_ticks > SimTime.Minutes(2))
            foreach (var c in w.Crew)
                if (c != _pt && c.CanAct && (c.Role == CrewRole.Medic || c.SkillLevel(Skill.Medicine) >= 0.3f) && (c.Position - _pt.Position).LengthSquared() < 2.3f * 2.3f)
                {
                    _m.Handoffs++;
                    w.Log.Add(w.Tick, LogKind.Work, $"{r.Name}: {_pt.Name}의 상처를 {c.Name}에게 넘겼다", c.Id);
                    return ToilStatus.Succeeded;
                }
        return _ticks > SimTime.Minutes(45) ? ToilStatus.Succeeded : ToilStatus.Running;
    }
}

/// <summary>인공 폐 곁에서 손 펌프를 돌린다 (전기가 돌아오거나 · 사람이 떨어지거나 · 배터리가 바닥날 때까지).</summary>
internal sealed class RCrank : RobotStep
{
    private readonly Furniture _f;
    private readonly MedBotSystem _m;
    public RCrank(Furniture f, MedBotSystem m) { _f = f; _m = m; }
    public override ToilStatus Tick(Robot r, World w)
    {
        if (_f.Machine!.Powered || w.Organs.PatientOn(_f) == null) return ToilStatus.Succeeded;
        if ((_f.Center - r.Position).LengthSquared() > 2.6f * 2.6f) return ToilStatus.Failed;
        if (r.Battery < 0.12f) { w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name}: 배터리가 바닥나 {_f.Name} 손 펌프를 놓는다 — 사람이 이어 돌려야 한다"); return ToilStatus.Succeeded; }
        if ((_f.Center - r.Position).LengthSquared() > 0.0001f) r.Facing = Vector2.Normalize(_f.Center - r.Position);
        r.Battery = MathF.Max(0f, r.Battery - 0.25f / SimTime.TicksPerHour); // 손잡이를 돌리는 힘
        _m.Cranked(r, _f);
        return ToilStatus.Running;
    }
}

/// <summary>될 때까지 (또는 정한 시간까지) 그 자리에서 기다린다.</summary>
internal sealed class RWaitFor : RobotStep
{
    private readonly Func<Robot, World, bool> _done;
    private readonly int _max;
    private int _t;
    public RWaitFor(Func<Robot, World, bool> done, int maxTicks) { _done = done; _max = maxTicks; }
    public override ToilStatus Tick(Robot r, World w) => _done(r, w) || ++_t >= _max ? ToilStatus.Succeeded : ToilStatus.Running;
}
