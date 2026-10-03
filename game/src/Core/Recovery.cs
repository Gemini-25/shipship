using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 의료 1차 — 회복 · 재활.
//   병상: 치료 침대가 모자라면 휴게실 · 복도에 간이침대를 펴고 눕힌다 (침대보다 더디 낫는다 · 휴게실이 좁아진다)
//   간병 순번: 네 시간씩 돌아가며 누운 사람을 들여다본다 (말벗 · 물 · 진통제 · 뒤척여 주기) — 아무도 안 오면 마음이 가라앉는다
//   뼈: 수술로 고정한 뼈는 빨리 붙고 · 그냥 둔 큰 골절은 비뚤게 붙어 후유증이 남는다
//   후유증: 손이 굳음(일이 굼뜨다) · 다리를 전다(걸음이 느리다) — 재활 운동(체육관 · 의무실 · 휴게실)으로 조금씩 푼다
//   자존감: 후유증 · 잃은 팔다리는 자존감을 깎는다 (말수가 줄고 · 긴장이 안 풀린다) · 재활 · 다시 일하기 · 곁에서 거들어 주는 사람이 세운다
// 주컴퓨터: 침대 수와 누워야 할 사람을 견줘 간이침대를 권하고 · 간병 순번을 알리고 · 재활을 권한다.
public sealed class RecoverySystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 5651 + 1907));
    private readonly SortedDictionary<int, long> _fixed = new();
    private readonly SortedDictionary<int, int> _fracture = new();
    public SortedDictionary<int, long> PostOps { get; } = new();
    public SortedDictionary<int, int> WardBed { get; } = new();
    public SortedDictionary<int, float> Esteem { get; } = new();
    private readonly SortedDictionary<int, int> _rehabSeen = new();
    public SortedDictionary<int, long> LastRound { get; } = new();
    private readonly SortedSet<int> _lowMarked = new(), _neglectMarked = new();
    public int Carer { get; private set; } = -1;
    private long _shift = -1, _lastBedTalk = -1;
    public int CotsSpread, Overflow, Rounds, Malunions, RehabGains, Helps, LowEsteem, RotaCasts, Sequelae, Neglected;

    public RecoverySystem(World w) => _w = w;

    private CrewMember? CrewOf(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }
    public float EsteemOf(CrewMember c) => Esteem.TryGetValue(c.Id, out var e) ? e : 0.7f;

    public void Fixed(CrewMember c, BodyPart p) { _fixed[c.Id] = _w.Tick; _fracture.Remove(c.Id); }
    public void PostOp(CrewMember c, SurgeryCase k) => PostOps[c.Id] = _w.Tick;
    public void Ward(CrewMember c, Furniture cot) => WardBed[c.Id] = cot.Id;

    /// <summary>후유증 하나 (팔이면 손이 굳고 · 다리면 전다).</summary>
    public void Sequela(CrewMember c, BodyPart p, string why)
    {
        var w = _w;
        string id = Wounds.IsLeg(p) ? "limp" : "stiffhand";
        if (w.Ailments.Has(c, id)) return;
        if (w.Ailments.Catch(c, id, null, why) is Ailment a) a.Diagnosed = true;
        Sequelae++;
        Esteem[c.Id] = MathF.Max(0.15f, EsteemOf(c) - 0.1f);
        MarkLog.Add(c.Memory.Marks, w.Tick, Wounds.IsLeg(p) ? $"{Wounds.PartName(p)}을 전다 — {why}" : $"{Wounds.PartName(p)} 손가락이 굳었다 — {why}");
        w.Log.Add(w.Tick, LogKind.Life, $"{c.Name} — {(Wounds.IsLeg(p) ? "다리를 전다" : "손이 굳었다")} ({why})", c.Id);
    }

    public void Lost(CrewMember c, BodyPart p)
    {
        Esteem[c.Id] = MathF.Max(0.1f, EsteemOf(c) - 0.25f);
        _fracture.Remove(c.Id);
    }

    public bool NeedsRehab(CrewMember c)
    {
        foreach (var a in c.Ailments) if (a.Id is "stiffhand" or "limp") return true;
        return false;
    }

    /// <summary>Needs가 부른다: 부상이 낫는 빠르기 배율.</summary>
    public float HealMul(CrewMember c)
    {
        float m = 1f;
        if (_fixed.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.TicksPerDay * 10) m *= 1.5f;
        else if (_fracture.ContainsKey(c.Id)) m *= 0.8f; // 그냥 둔 큰 골절은 더디 붙는다
        if (c.Job?.Activity is WardRestActivity && c.Pose == Pose.Sleeping) m *= 2.4f; // 간이침대: 침대만은 못해도 누워 쉰다
        return m;
    }

    /// <summary>누워야 할 사람 (위중 · 중상 · 수술 뒤 하루).</summary>
    public bool NeedsBed(CrewMember c)
    {
        if (c.Dead || c.Away || c.Outside) return false;
        return _w.Grades.Now(c) >= InjuryGrade.Serious || PostOps.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.TicksPerDay || c.Down;
    }

    private static bool BedOk(Furniture f) => !f.Room.Detached && !f.Stowed && f.Machine is Machine m && m.Efficiency > 0f;

    /// <summary>눕힐 자리: 빈 치료 침대 → 이 사람 간이침대 → 비어 있는 병상 간이침대 → 새로 편다.</summary>
    public Furniture? BedFor(CrewMember pt)
    {
        var w = _w;
        Furniture? best = null; int bd = int.MaxValue;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.MedBed))
        {
            if (!BedOk(f) || f.ReservedBy != null && f.ReservedBy != pt || f.UseSpots.Count == 0) continue;
            int d = Math.Abs(f.Cells[0].X - pt.Cell.X) + Math.Abs(f.Cells[0].Y - pt.Cell.Y);
            if (d < bd) { bd = d; best = f; }
        }
        if (best != null) return best;
        if (WardBed.TryGetValue(pt.Id, out var id) && id < w.Ship.Furniture.Count && w.Ship.Furniture[id] is { Type: FurnitureType.Cot } own && (own.ReservedBy == null || own.ReservedBy == pt)) return own;
        foreach (var f in WardCots())
            if (f.ReservedBy == null && !WardBed.Values.Contains(f.Id)) return f;
        return Spread();
    }

    private IEnumerable<Furniture> WardCots() => _cots.Select(id => _w.Ship.Furniture[id]).Where(f => !f.Room.Detached && !f.Stowed);
    private readonly SortedSet<int> _cots = new();

    /// <summary>휴게실(없으면 복도)에 간이침대를 하나 편다.</summary>
    private Furniture? Spread()
    {
        var w = _w;
        var rooms = w.Ship.Rooms.Where(r => !r.Detached && !r.Leaking && !r.OffLimits && r.Type == RoomType.Lounge).OrderBy(r => r.Id)
            .Concat(w.Ship.Rooms.Where(r => !r.Detached && !r.Leaking && !r.OffLimits && r.Type == RoomType.Corridor)
                .OrderBy(r => w.Ship.RoomsOf(RoomType.Medbay).Select(m => (m.Center - r.Center).Length()).DefaultIfEmpty(0f).Min()).ThenBy(r => r.Id));
        foreach (var r in rooms)
        {
            foreach (var cell in Adaptation.CotCells(w, r))
            {
                if (!Adaptation.SafeToBlock(w, r, cell)) continue;
                var cot = w.Ship.AddFurniture(FurnitureType.Cot, cell);
                _cots.Add(cot.Id);
                CotsSpread++;
                w.Paths.Invalidate();
                w.Structure.Touch();
                w.Log.Add(w.Tick, LogKind.Work, $"치료 침대가 모자라 {r.Name}에 간이침대를 폈다");
                MarkLog.Add(r.Marks, w.Tick, "다친 사람을 눕힐 간이침대");
                return cot;
            }
        }
        return null;
    }

    public bool IsWardCot(Furniture f) => _cots.Contains(f.Id);

    // ───────────── 30분마다 ─────────────

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick % SimTime.Minutes(30) >= World.SystemInterval) return;
        Bones();
        Beds();
        Rehab();
        Mood();
        Rota();
        if (w.Tick % SimTime.Hours(2) < World.SystemInterval) Help();
    }

    /// <summary>큰 골절을 그냥 두면 비뚤게 붙는다.</summary>
    private void Bones()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead) { _fracture.Remove(c.Id); continue; }
            var v = c.Vitals;
            if (!_fixed.ContainsKey(c.Id) || w.Tick - _fixed[c.Id] > SimTime.TicksPerDay * 10)
                foreach (var x in v.Wounds)
                    if (!x.Lost && x.Kind == WoundKind.Fracture && Wounds.Severity(v, x) >= 0.2f && (Wounds.IsArm(x.Part) || Wounds.IsLeg(x.Part))) { _fracture[c.Id] = (int)x.Part; break; }
            if (_fracture.TryGetValue(c.Id, out var p) && v.Injury < 0.04f)
            {
                _fracture.Remove(c.Id);
                Malunions++;
                Sequela(c, (BodyPart)p, "뼈가 비뚤게 붙었다");
            }
        }
    }

    private void Beds()
    {
        var w = _w;
        int beds = 0;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.MedBed)) if (BedOk(f)) beds++;
        int need = 0;
        foreach (var c in w.Crew) if (NeedsBed(c)) need++;
        // 다 나은 사람은 간이침대를 비운다
        foreach (var id in WardBed.Keys.ToList())
        {
            var c = CrewOf(id);
            if (c == null || c.Dead || !NeedsBed(c) && c.Vitals.Injury < 0.25f && c.Vitals.Health > 0.7f && c.Job?.Activity is not WardRestActivity)
            {
                if (w.Ship.Furniture[WardBed[id]].ReservedBy == c) w.Ship.Furniture[WardBed[id]].ReservedBy = null;
                WardBed.Remove(id);
            }
        }
        if (need <= beds) return;
        Overflow = Math.Max(Overflow, need - beds);
        if (WardCots().Count() < need - beds) Spread();
        if (w.Automation.Present && w.Automation.MainOnline && w.Tick - _lastBedTalk > SimTime.Hours(12))
        {
            _lastBedTalk = w.Tick;
            var lounge = WardCots().Select(f => f.Room.Name).Distinct().FirstOrDefault() ?? "휴게실";
            w.Automation.Book.Add(ActKind.Advice, null, $"치료 침대 {beds} · 누워야 할 사람 {need}", "침대에 못 누우면 더디 낫는다",
                $"{lounge}에 간이침대를 펴라고 했다 (조용히 해 달라고)", "", "wardbeds", SimTime.Hours(12));
            w.Automation.Speak.Announce(w.Automation.Voice.Style($"치료 침대가 모자랍니다 — {lounge}에 간이침대를 폈습니다. 그 방에서는 조용히 해 주십시오"), null, 1);
        }
    }

    /// <summary>재활 운동을 한 번 할 때마다 (Growth의 재활 일감) 후유증이 풀린다 — 체육관 · 러닝머신이면 더.</summary>
    private void Rehab()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            int seen = _rehabSeen.TryGetValue(c.Id, out var s) ? s : c.Stats.RehabSessions;
            int now = c.Stats.RehabSessions;
            _rehabSeen[c.Id] = now;
            if (now <= seen) continue;
            bool gym = c.Room != null && (c.Room.Kind == RoomType.Gym || c.Room.Furniture.Any(f => f.Type == FurnitureType.Treadmill));
            foreach (var a in c.Ailments)
            {
                if (a.Id is not ("stiffhand" or "limp")) continue;
                a.Healed += (now - seen) * (gym ? 2.5f : 1.5f);
                RehabGains++;
            }
            if (NeedsRehab(c) || Esteem.ContainsKey(c.Id)) Esteem[c.Id] = MathF.Min(0.9f, EsteemOf(c) + 0.06f * (now - seen));
        }
    }

    private void Mood()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away) continue;
            bool hurt = NeedsRehab(c) || Wounds.Missing(c.Vitals).Any();
            if (!hurt && !Esteem.ContainsKey(c.Id)) continue;
            float e = EsteemOf(c);
            if (hurt) e = MathF.Max(0.12f, e - (c.Stats.TicksWorking > 0 && c.Job?.Order != null ? 0.002f : 0.005f)); // 일을 하면 덜 꺾인다
            else e = MathF.Min(0.7f, e + 0.01f);
            Esteem[c.Id] = e;
            if (!hurt && e >= 0.69f) { Esteem.Remove(c.Id); _lowMarked.Remove(c.Id); continue; }
            if (e < 0.35f)
            {
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.015f);
                c.Needs.Social = MathF.Max(0f, c.Needs.Social - 0.01f); // 말수가 준다
                if (_lowMarked.Add(c.Id))
                {
                    LowEsteem++;
                    MarkLog.Add(c.Memory.Marks, w.Tick, NeedsRehab(c) ? "예전 같지 않다 — 손발이 말을 안 듣는다" : "잃은 팔다리가 자꾸 생각난다");
                    w.Log.Add(w.Tick, LogKind.Life, $"{c.Name} — 요즘 말수가 줄었다", c.Id);
                }
            }
        }
        // 누운 사람을 아무도 안 들여다본다
        foreach (var c in w.Crew)
        {
            if (!NeedsBed(c) || c.Down) continue;
            long last = LastRound.TryGetValue(c.Id, out var t) ? t : PostOps.GetValueOrDefault(c.Id, w.Tick);
            if (w.Tick - last < SimTime.Hours(10)) continue;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
            if (_neglectMarked.Add(c.Id)) { Neglected++; MarkLog.Add(c.Memory.Marks, w.Tick, "누워 있는데 아무도 들여다보지 않았다"); }
        }
    }

    /// <summary>후유증이 있는 사람 곁의 가까운 사람이 거든다 — 자존감이 선다 · 사이가 가까워진다.</summary>
    private void Help()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.CanAct || !NeedsRehab(c) && !Wounds.Missing(c.Vitals).Any() || c.Room == null) continue;
            CrewMember? by = null;
            foreach (var o in w.Crew)
                if (o != c && o.CanAct && o.Room == c.Room && o.AffinityTo(c) > 0.3f && o.Pose != Pose.Sleeping && (by == null || o.AffinityTo(c) > by.AffinityTo(c))) by = o;
            if (by == null) continue;
            Helps++;
            c.ChangeAffinity(by, 0.02f); by.ChangeAffinity(c, 0.02f);
            Esteem[c.Id] = MathF.Min(0.9f, EsteemOf(c) + 0.03f);
            if (Helps % 4 == 1) w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(by.Name)} {c.Name}의 굳은 손 대신 거들었다", by.Id);
        }
    }

    /// <summary>간병 순번: 네 시간마다 돌아간다 (누운 사람이 있을 때만 알린다).</summary>
    private void Rota()
    {
        var w = _w;
        var roster = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside && !NeedsBed(c)).OrderBy(c => c.Id).ToList();
        if (roster.Count == 0) { Carer = -1; return; }
        long shift = w.Tick / SimTime.Hours(4);
        var who = roster[(int)(shift % roster.Count)];
        bool patients = w.Crew.Any(NeedsBed);
        if (shift != _shift || Carer != who.Id)
        {
            _shift = shift;
            Carer = who.Id;
            if (patients)
            {
                RotaCasts++;
                if (w.Automation.Present && w.Automation.MainOnline)
                    w.Automation.Book.Add(ActKind.Plan, null, $"누운 사람 {w.Crew.Count(NeedsBed)}", "네 시간씩 돌아가며 들여다본다", $"간병 순번 — {who.Name}", "", "carerota", SimTime.Hours(4));
                else w.Log.Add(w.Tick, LogKind.Life, $"간병 순번 — {who.Name} (칠판에 적었다)", who.Id);
            }
        }
    }

    /// <summary>간병하는 사람이 들여다봤다.</summary>
    public void Visit(CrewMember pt, CrewMember by)
    {
        var w = _w;
        LastRound[pt.Id] = w.Tick;
        _neglectMarked.Remove(pt.Id);
        Rounds++;
        pt.Needs.Stress = MathF.Max(0f, pt.Needs.Stress - 0.08f);
        pt.Needs.Social = MathF.Min(1f, pt.Needs.Social + 0.15f);
        if (PostOps.TryGetValue(pt.Id, out var t) && w.Tick - t < SimTime.TicksPerDay * 2) w.Pharmacy.Painkill(pt, by, "회진");
        pt.ChangeAffinity(by, 0.03f); by.ChangeAffinity(pt, 0.02f);
        by.Needs.Rest = MathF.Max(0f, by.Needs.Rest - 0.02f);
        if (pt.AffinityTo(by) < 0.3f) w.Relations.Remember(pt, by, RelationReason.NursedMe, "누워 있을 때 들여다봐 줬다");
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var kv in _fixed) { I(kv.Key); I(kv.Value); }
        foreach (var kv in _fracture) { I(kv.Key); I(kv.Value); }
        foreach (var kv in WardBed) { I(kv.Key); I(kv.Value); }
        foreach (var kv in Esteem) { I(kv.Key); F(kv.Value); }
        foreach (var kv in LastRound) { I(kv.Key); I(kv.Value); }
        I(Carer); I(CotsSpread); I(Rounds); I(Malunions); I(RehabGains); I(Sequelae);
    }
}

/// <summary>치료 침대가 모자랄 때: 휴게실 · 복도의 간이침대에 눕는다.</summary>
public sealed class WardRestActivity : Activity
{
    public override string Id => "wardrest";
    public override string Label => "간이침대에서 회복";

    private static bool MedBedFree(CrewMember c, World w)
    {
        foreach (var b in w.Ship.FurnitureOf(FurnitureType.MedBed))
            if ((b.ReservedBy == null || b.ReservedBy == c) && !b.Room.Detached && b.Machine is Machine m && m.Efficiency > 0f) return true;
        return false;
    }

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || w.Surgery.CaseOf(c) is { Surgeon: >= 0 }) return (0f, "—");
        float h = c.Vitals.Health, inj = c.Vitals.Injury;
        bool post = w.Recovery.PostOps.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.TicksPerDay;
        float need = MathF.Max(h < 0.75f ? (0.75f - h) * 2.2f : 0f, inj > 0.25f ? (inj - 0.25f) * 1.2f : 0f);
        if (post) need = MathF.Max(need, 0.7f);
        if (need <= 0f) return (0f, "—");
        if (c.Job?.Activity is WardRestActivity) return (need + 0.05f, "간이침대에서 쉰다");
        if (MedBedFree(c, w)) return (0f, "치료 침대가 비어 있다");
        return (need + 0.01f, $"치료 침대가 모자라다 — 간이침대에 눕는다 (부상 {inj * 100:0}%)");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var cot = w.Recovery.BedFor(c);
        if (cot == null || cot.Type == FurnitureType.MedBed || cot.UseSpots.Count == 0 || !dist.Reachable(cot.UseSpots[0])) return null;
        w.Recovery.Ward(c, cot);
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(cot.UseSpots[0]));
        toils.Add(new WaitToil(SimTime.Hours(8), Pose.Sleeping, minTicks: SimTime.Hours(1))
        {
            DoneWhen = (cm, world) => cm.Vitals.Health >= MathF.Min(0.9f, cm.Vitals.MaxHealth - 0.02f) && cm.Vitals.Injury < 0.25f
                                      && !(world.Recovery.PostOps.TryGetValue(cm.Id, out var tt) && world.Tick - tt < SimTime.TicksPerDay),
        });
        var job = new Job(this, "간이침대에서 회복", toils) { LogText = $"{cot.Room.Name} 간이침대에 눕는다", TargetRoom = cot.Room, InterruptMargin = 0.35f };
        return job.Reserve(cot, c);
    }
}

/// <summary>간병 순번: 맡은 네 시간 동안 누운 사람들을 차례로 들여다본다.</summary>
public sealed class CareRoundActivity : Activity
{
    public override string Id => "careround";
    public override string Label => "간병";

    private static List<CrewMember> Due(CrewMember c, World w)
    {
        var list = new List<CrewMember>();
        foreach (var p in w.Crew)
        {
            if (p == c || !w.Recovery.NeedsBed(p) || p.Room == null || w.Surgery.OnTable(p) || p.CarriedBy != null) continue;
            long last = w.Recovery.LastRound.TryGetValue(p.Id, out var t) ? t : -1_000_000;
            if (w.Tick - last < SimTime.Hours(3)) continue;
            list.Add(p);
        }
        list.Sort((a, b) => w.Recovery.LastRound.GetValueOrDefault(a.Id, -1_000_000).CompareTo(w.Recovery.LastRound.GetValueOrDefault(b.Id, -1_000_000)) is int d && d != 0 ? d : a.Id.CompareTo(b.Id));
        return list;
    }

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.Pose == Pose.Sleeping || w.Recovery.Carer != c.Id || w.Surgery.Busy(c)) return (0f, "—");
        var due = Due(c, w);
        if (due.Count == 0) return (0f, "들여다볼 사람이 없다");
        return (0.62f, $"간병 순번 — {due[0].Name} 들여다보기");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var due = Due(c, w);
        if (due.Count == 0) return null;
        var toils = Plans.DropOff(c, w, dist);
        int n = 0;
        foreach (var pt in due)
        {
            if (n >= 3) break;
            if (RadCareActivity.Near(w, dist, pt) is not Cell at) continue;
            n++;
            var p = pt;
            toils.Add(new GotoToil(at));
            toils.Add(new WaitToil(SimTime.Minutes(8), Pose.Working, p.Position));
            toils.Add(new DoToil((cm, world) =>
            {
                if (!p.Dead && (p.Position - cm.Position).Length() < 2.6f) world.Recovery.Visit(p, cm);
                return true;
            }));
        }
        if (n == 0) return null;
        return new Job(this, "간병 순번", toils) { LogText = $"{due[0].Name}부터 들여다본다", TargetRoom = due[0].Room };
    }
}
