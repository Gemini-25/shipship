using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// 의료 1차 — 수술 (수술실 · 수술 종류 · 집도 · 마취 · 결과 · 주컴퓨터 감시).
//   수술실: 의무실에 수술대 · 무영등 · 마취기 · 멸균기 (배가 처음부터 싣고 나온다 — 자리가 없으면 있는 만큼)
//   수술 종류: 봉합 · 지혈 / 골절 고정 / 배 속 출혈 · 장기 손상 개복 / 머리 / 화상 피부 이식 / 팔다리 살리기 아니면 절단
//   후보: 중상 · 위중한 사람 가운데 칼을 대야 낫는 것 (배 속 출혈은 누르거나 침대로는 멎지 않는다)
//   성공률: 집도의 솜씨 · 보조 · 피로 · 마음(죄책감 · 자신감) · 조명(무영등 · 비상 배터리 · 캄캄함) · 멸균 · 무중력 · 흔들림 · 마취
//   마취: 마취제가 없으면 미룬다 — 위중하면 마취 없이 무릅쓴다
//   결과: 흉터 · 후유증(손이 굳음 · 다리를 전다) · 실패면 악화 · 숨짐 · 집도의의 기억(살렸다 / 내 손에서)
// 주컴퓨터: 생체 신호를 본다("혈압이 떨어진다") · 출혈량을 예측한다 · 수술 순서를 권한다 · 정전이면 수술실 회로에 전기를 몰아준다 ·
//   수술 중에 항로 점화가 잡혀 있으면 함장에게 미루자고 제안한다 · 회피 기동 직전이면 손을 떼라고 알린다.
public enum SurgeryKind : byte { Suture, Fracture, Laparotomy, Head, Graft, Salvage, Amputate }
public enum CaseState : byte { Waiting, Deferred, Prep, Operating, Done }

public sealed class SurgeryCase
{
    public int Id { get; init; }
    public int Patient { get; init; }
    public SurgeryKind Kind { get; set; }
    public BodyPart Part { get; init; }
    public CaseState State { get; set; }
    public long Opened { get; init; }
    public string Why { get; init; } = "";
    public int Surgeon { get; set; } = -1;
    public int Assistant { get; set; } = -1;
    public int Table { get; set; } = -1;          // 수술대 (없으면 치료 침대)
    public int Phase { get; set; }                // 0 손 씻기 · 1 마취 · 2 수술 · 3 닫기
    public int PhaseTicks { get; set; }
    public long Started { get; set; } = -1;
    public float Progress { get; set; }
    public float NeedTicks { get; set; }
    public bool Anesthesia { get; set; }
    public bool NoAnesthesia { get; set; }
    public float Sterile { get; set; } = 0.45f;
    public int OpTicks, DarkTicks, BatteryTicks, ShakeTicks, HoldTicks, ZeroGTicks, Waited;
    public bool Warned { get; set; }              // 회피 기동 전에 컴퓨터가 알렸다
    public bool PowerLost { get; set; }
    public float LastWarnHealth { get; set; } = 2f;
    public int Bloods { get; set; }
    public long LastBlood { get; set; } = -1;
    public string DeferWhy { get; set; } = "";
    public string Decision { get; set; } = "";
    public float Chance { get; set; }
    public bool Success { get; set; }
    public string Outcome { get; set; } = "";
    public float Urgency { get; set; }
    public List<string> Notes { get; } = new();
}

public sealed class SurgerySystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6151 + 3271));
    private int _next = 1;
    private bool _fitted;
    public List<SurgeryCase> Cases { get; } = new();
    public List<SurgeryCase> Done { get; } = new();
    private readonly SortedSet<int> _internal = new();            // 배 속 출혈 (외상 번호)
    private readonly SortedSet<string> _operated = new();         // 이미 수술한 상처 (사람:부위:종류)
    public SortedDictionary<int, float> Guilt { get; } = new();
    public SortedDictionary<int, float> Confidence { get; } = new();
    private readonly SortedDictionary<int, float> _lampCharge = new(); // 무영등 비상 배터리 (분)
    public long HoldBurnUntil { get; private set; } = -1;
    private long _lastOrderCast = -1;
    private string _lastOrder = "";
    public string LastOrder => _lastOrder;
    public int Operations, Successes, Failures, Deaths, Deferrals, NoAnesthesiaOps, PowerRouted, BurnProposals, BurnHolds, Amputations, Salvages,
        VitalWarnings, BleedForecasts, StepBacks, Fitted, InternalBleeds, WarnedShakes;

    public SurgerySystem(World w) => _w = w;

    public static string KindName(SurgeryKind k) => k switch
    {
        SurgeryKind.Suture => "봉합 · 지혈", SurgeryKind.Fracture => "골절 고정", SurgeryKind.Laparotomy => "개복 (배 속 출혈)",
        SurgeryKind.Head => "머리 수술", SurgeryKind.Graft => "피부 이식", SurgeryKind.Salvage => "팔다리 살리기", _ => "절단",
    };
    private static float Hours(SurgeryKind k) => k switch
    {
        SurgeryKind.Suture => 0.5f, SurgeryKind.Fracture => 1.5f, SurgeryKind.Laparotomy => 2.2f, SurgeryKind.Head => 2.6f,
        SurgeryKind.Graft => 1.8f, SurgeryKind.Salvage => 2.8f, _ => 1.2f,
    };
    private static float Ease(SurgeryKind k) => k switch
    {
        SurgeryKind.Suture => 0.25f, SurgeryKind.Fracture => 0.1f, SurgeryKind.Laparotomy => -0.1f, SurgeryKind.Head => -0.18f,
        SurgeryKind.Graft => 0f, SurgeryKind.Salvage => -0.15f, _ => 0.15f,
    };
    /// <summary>수술 중 시간당 잃는 피 (체력).</summary>
    private static float Loss(SurgeryKind k) => k switch
    {
        SurgeryKind.Suture => 0.02f, SurgeryKind.Fracture => 0.05f, SurgeryKind.Laparotomy => 0.13f, SurgeryKind.Head => 0.07f,
        SurgeryKind.Graft => 0.06f, SurgeryKind.Salvage => 0.1f, _ => 0.08f,
    };

    private CrewMember? CrewOf(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }
    public SurgeryCase? CaseOf(CrewMember c) { foreach (var k in Cases) if (k.Patient == c.Id) return k; return null; }
    public bool Busy(CrewMember c) { foreach (var k in Cases) if (k.State is CaseState.Prep or CaseState.Operating && (k.Surgeon == c.Id || k.Assistant == c.Id || k.Patient == c.Id)) return true; return false; }
    public int WaitingFor(ItemKind k) { int n = 0; foreach (var x in Cases) if (x.State == CaseState.Deferred && x.DeferWhy.StartsWith("마취제")) n++; return k == ItemKind.Anesthetic ? n : 0; }
    public Furniture? TableOf(SurgeryCase k) => k.Table >= 0 && k.Table < _w.Ship.Furniture.Count ? _w.Ship.Furniture[k.Table] : null;
    public bool BurnHeld => _w.Tick < HoldBurnUntil;

    /// <summary>수술대(또는 그 침대) 위에서 수술을 받는 중 — 구조 · 치료 일감을 내지 않는다.</summary>
    public bool OnTable(CrewMember c)
    {
        foreach (var k in Cases)
        {
            if (k.Patient != c.Id || k.State is not (CaseState.Prep or CaseState.Operating)) continue;
            var t = TableOf(k);
            return t != null && t.Cells.Contains(c.Cell) && c.CarriedBy == null;
        }
        return false;
    }

    /// <summary>배 속 출혈 — 누르거나 치료 침대로는 멎지 않는다 (개복해야).</summary>
    public bool Internal(Trauma t) => _internal.Contains(t.Id);
    public bool Internal(CrewMember c) { var t = _w.Casualty.Of(c); return t != null && _internal.Contains(t.Id); }

    /// <summary>Casualty가 부른다: 배 속 출혈의 하루 (치료 침대 · 수액은 늦출 뿐).</summary>
    public void TendInternal(CrewMember c, Trauma t, float dt)
    {
        float slow = c.CareBed?.Machine is Machine bed && bed.Efficiency > 0f ? 0.45f : c.Vitals.TreatedTick >= t.Since ? 0.7f : 1f;
        if (OnTable(c)) slow = 0.2f; // 수술대 위: 손으로 누르고 · 집고 있다 (수술이 피를 따로 센다)
        c.Vitals.Health -= t.Rate * slow * (c.Down ? 1.3f : 1f) * dt;
    }

    /// <summary>정전 몰아주기: 수술 중인 방의 설비 (Power · 컴퓨터 우선순위가 읽는다).</summary>
    public bool Holds(Machine m)
    {
        if (Cases.Count == 0) return false;
        foreach (var k in Cases)
            if (k.State is CaseState.Prep or CaseState.Operating && TableOf(k) is Furniture t && t.Room == m.Body.Room
                && m.Body.Type is FurnitureType.OperatingTable or FurnitureType.SurgicalLamp or FurnitureType.AnesthesiaMachine or FurnitureType.Autoclave or FurnitureType.MedBed or FurnitureType.BloodFridge)
                return true;
        return false;
    }

    // ───────────── 수술실 차리기 (배가 처음부터 싣고 나온다) ─────────────

    private void Fit()
    {
        _fitted = true;
        var w = _w;
        var room = w.Ship.Rooms.Where(r => !r.Detached && r.Type == RoomType.Medbay).OrderByDescending(r => r.Cells.Count).ThenBy(r => r.Id).FirstOrDefault();
        if (room == null) return;
        Cell? tableCell = room.Furniture.FirstOrDefault(f => f.Type == FurnitureType.OperatingTable)?.Cells[0];
        var order = new[] { FurnitureType.OperatingTable, FurnitureType.SurgicalLamp, FurnitureType.AnesthesiaMachine, FurnitureType.Autoclave, FurnitureType.BloodFridge, FurnitureType.MedCabinet };
        foreach (var t in order)
        {
            if (room.Furniture.Any(f => f.Type == t)) continue;
            var cells = Adaptation.CotCells(w, room).Where(c => Adaptation.SafeToBlock(w, room, c)).ToList();
            if (cells.Count == 0) break;
            Cell at;
            if (t == FurnitureType.OperatingTable)
                at = cells.OrderByDescending(c => Cell.Dirs4.Count(d => w.Ship.IsOpenFloor(c + d) && w.Ship.RoomAt(c + d) == room)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
            else if (tableCell is Cell tc && t is FurnitureType.SurgicalLamp or FurnitureType.AnesthesiaMachine)
                at = cells.Where(c => c != tc).OrderBy(c => Math.Max(Math.Abs(c.X - tc.X), Math.Abs(c.Y - tc.Y))).ThenBy(c => c.Y).ThenBy(c => c.X).First();
            else at = cells.OrderBy(c => c.Y).ThenBy(c => c.X).First();
            var f = w.Ship.AddFurniture(t, at);
            if (f.Machine != null) { f.Machine.Wear = 0.03f; f.Machine.Condition = 0.97f; if (t != FurnitureType.Autoclave && t != FurnitureType.BloodFridge) f.Machine.Active = false; }
            if (t == FurnitureType.OperatingTable) tableCell = at;
            if (t == FurnitureType.MedCabinet)
            {
                f.Storage = new Inventory(40, ItemKind.Painkiller, ItemKind.Antibiotic, ItemKind.Anesthetic, ItemKind.BloodSubstitute, ItemKind.MedHerb, ItemKind.MedKit, ItemKind.Bandage, ItemKind.Disinfectant);
                f.Storage.Add(ItemKind.Painkiller, 6); f.Storage.Add(ItemKind.Antibiotic, 4); f.Storage.Add(ItemKind.Anesthetic, 3); f.Storage.Add(ItemKind.BloodSubstitute, 2);
            }
            Fitted++;
        }
        if (!w.Ship.FurnitureOf(FurnitureType.MedCabinet).Any())
        {
            // 약장 자리가 없으면 선반에 (의무실 선반부터)
            var sh = w.Ship.Furniture.Where(f => f.Storage != null && f.Type == FurnitureType.Shelf && !f.Room.Detached).OrderBy(f => f.Room.Type == RoomType.Medbay ? 0 : 1).ThenBy(f => f.Id).FirstOrDefault();
            if (sh != null) { sh.Storage!.Add(ItemKind.Painkiller, 6); sh.Storage.Add(ItemKind.Antibiotic, 4); sh.Storage.Add(ItemKind.Anesthetic, 3); sh.Storage.Add(ItemKind.BloodSubstitute, 2); }
        }
        w.Blood.Load(2);
        w.Paths.Invalidate();
    }

    // ───────────── 어떤 수술이 필요한가 ─────────────

    /// <summary>칼을 대야 낫는 것 (가장 급한 것 하나).</summary>
    public (SurgeryKind kind, BodyPart part, string why, float urg)? Needs(CrewMember c)
    {
        var w = _w;
        if (c.Dead || c.Away || c.Outside) return null;
        var g = w.Grades.Now(c);
        if (g < InjuryGrade.Serious) return null;
        var v = c.Vitals;
        var t = w.Casualty.Of(c);
        float crit = g == InjuryGrade.Critical ? 1f : 0f;
        if (t != null && _internal.Contains(t.Id) && !Did(c, BodyPart.Chest, SurgeryKind.Laparotomy, 2))
            return (SurgeryKind.Laparotomy, BodyPart.Chest, $"배 속 출혈 ({t.Cause})", 2.5f + crit);
        Wound? best = null; float bs = 0f;
        foreach (var x in v.Wounds)
        {
            if (x.Lost) continue;
            float s = Wounds.Severity(v, x);
            if (s > bs) { bs = s; best = x; }
        }
        if (t is { Kind: TraumaKind.Bleed } && t.Rate >= 0.06f && v.TreatedTick < t.Since && best != null && !Did(c, best.Part, SurgeryKind.Suture, 1))
            return (SurgeryKind.Suture, best.Part, $"{Wounds.PartName(best.Part)} 깊은 상처 — 피가 멎지 않는다", 2f + crit);
        if (best == null || bs < 0.15f) return null;
        var part = best.Part;
        if (part == BodyPart.Head && best.Kind is WoundKind.Cut or WoundKind.Crush or WoundKind.Fracture && !Did(c, part, SurgeryKind.Head, 1))
            return (SurgeryKind.Head, part, $"머리 {Wounds.KindName(best.Kind)}", 1.8f + crit);
        bool limb = Wounds.IsArm(part) || Wounds.IsLeg(part);
        if (limb && best.Kind is WoundKind.Crush or WoundKind.Fracture && bs >= 0.45f && !Did(c, part, SurgeryKind.Salvage, 1) && !Did(c, part, SurgeryKind.Amputate, 1))
            return (SurgeryKind.Salvage, part, $"{Wounds.PartName(part)} {Wounds.KindName(best.Kind)} — 살릴 수 있나", 1.6f + crit);
        if (best.Kind is WoundKind.Fracture or WoundKind.Crush && !Did(c, part, SurgeryKind.Fracture, 1))
            return (SurgeryKind.Fracture, part, $"{Wounds.PartName(part)} 골절", 1f + crit);
        if (best.Kind == WoundKind.Burn && bs >= 0.25f && !Did(c, part, SurgeryKind.Graft, 1))
            return (SurgeryKind.Graft, part, $"{Wounds.PartName(part)} 깊은 화상", 0.9f + crit);
        return null;
    }

    private bool Did(CrewMember c, BodyPart p, SurgeryKind k, int times)
    {
        int n = 0;
        for (int i = 0; i < times + 1; i++) if (_operated.Contains($"{c.Id}:{(int)p}:{(int)k}:{i}")) n++;
        return n >= times;
    }
    private void MarkDone(CrewMember c, BodyPart p, SurgeryKind k)
    {
        for (int i = 0; ; i++) if (_operated.Add($"{c.Id}:{(int)p}:{(int)k}:{i}")) return;
    }

    // ───────────── 매 틱 묶음 ─────────────

    public void Update(float dt)
    {
        var w = _w;
        if (!_fitted) Fit();
        MarkInternal();
        foreach (var k in Cases) if (k.State is CaseState.Prep or CaseState.Operating) Monitor(k);
        Machines();
        if (w.Tick % SimTime.Minutes(10) >= World.SystemInterval) return;
        Scan();
        Assign();
        CastOrder();
        Moods(dt);
    }

    /// <summary>새로 난 큰 출혈 가운데 배 · 가슴 속에서 나는 것 (몸통을 크게 받혔거나 짓눌렸다 · 폐가 터졌다).</summary>
    private void MarkInternal()
    {
        var w = _w;
        foreach (var t in w.Casualty.Open)
        {
            if (t.Kind != TraumaKind.Bleed || t.Id < _seenTrauma) continue;
            _seenTrauma = Math.Max(_seenTrauma, t.Id + 1);
            var c = CrewOf(t.CrewId);
            if (c == null) continue;
            bool blunt = t.Cause.Contains("폭발") || t.Cause.Contains("충돌") || t.Cause.Contains("무너") || t.Cause.Contains("짓눌") || t.Cause.Contains("구조물") || t.Cause.Contains("끼임") || t.Cause.Contains("감압") || t.Cause.Contains("배 속");
            bool chest = c.Vitals.Wounds.Any(x => !x.Lost && x.Part is BodyPart.Chest or BodyPart.Lungs);
            if (blunt && (chest || t.Rate >= 0.2f && R.Chance(0.35f)) || t.Cause.Contains("배 속"))
            {
                _internal.Add(t.Id);
                InternalBleeds++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{c.Name} — 겉으로 보이는 것보다 많이 흘린다 (배 속 출혈)", c.Id);
            }
        }
        if (_internal.Count > 0) _internal.RemoveWhere(id => !w.Casualty.Open.Any(t => t.Id == id));
    }
    private int _seenTrauma;

    private void Scan()
    {
        var w = _w;
        for (int i = Cases.Count - 1; i >= 0; i--)
        {
            var k = Cases[i];
            var pt = CrewOf(k.Patient);
            if (pt == null || pt.Dead) { Close(k, pt == null ? "사라짐" : "수술 전에 숨졌다", false); continue; }
            if (k.State is CaseState.Waiting or CaseState.Deferred && Needs(pt) is not { } n) { Close(k, "칼을 대지 않아도 되게 됐다", true); continue; }
            if (k.State == CaseState.Deferred && w.Pharmacy.Stock(ItemKind.Anesthetic) > 0 && k.DeferWhy.StartsWith("마취제"))
            {
                k.State = CaseState.Waiting; k.DeferWhy = "";
                w.Log.Add(w.Tick, LogKind.Work, $"마취제가 생겼다 — {pt.Name} {KindName(k.Kind)}을 다시 잡는다", pt.Id);
            }
            if (k.State == CaseState.Deferred && k.DeferWhy.StartsWith("마취제") && w.Grades.Now(pt) == InjuryGrade.Critical) { k.State = CaseState.Waiting; k.DeferWhy = ""; }
        }
        foreach (var c in w.Crew)
        {
            if (CaseOf(c) != null || Needs(c) is not { } n) continue;
            var k = new SurgeryCase { Id = _next++, Patient = c.Id, Kind = n.kind, Part = n.part, Why = n.why, Opened = w.Tick, Urgency = n.urg };
            Cases.Add(k);
            w.Log.Add(w.Tick, LogKind.Warning, $"{c.Name} — 수술이 필요하다: {KindName(k.Kind)} ({n.why})", c.Id);
        }
        foreach (var k in Cases) if (CrewOf(k.Patient) is CrewMember p && Needs(p) is { } nn) k.Urgency = nn.urg;
        Cases.Sort((a, b) => b.Urgency != a.Urgency ? b.Urgency.CompareTo(a.Urgency) : a.Id.CompareTo(b.Id));
    }

    /// <summary>집도의 · 보조를 정한다 (의무관 → 의료 솜씨 · 쉬었는가 · 마음).</summary>
    private void Assign()
    {
        var w = _w;
        foreach (var k in Cases)
        {
            if (k.State is CaseState.Deferred or CaseState.Done) continue;
            var pt = CrewOf(k.Patient)!;
            if (k.Surgeon >= 0 && CrewOf(k.Surgeon) is CrewMember s0 && s0.CanAct && !s0.Outside) continue;
            k.Surgeon = -1;
            bool cont = k.State != CaseState.Waiting; // 손을 놓은 수술을 이어받는다
            bool crit = w.Grades.Now(pt) == InjuryGrade.Critical;
            var cands = w.Crew.Where(c => c != pt && c.CanAct && !c.Outside && !c.IsChild && !Busy(c) && !Cases.Any(x => x != k && (x.Surgeon == c.Id || x.Assistant == c.Id))
                                          && w.Grades.Now(c) < InjuryGrade.Serious && (c.Role == CrewRole.Medic || c.SkillLevel(Skill.Medicine) >= (crit ? 0.15f : 0.3f)))
                .Select(c => (c, s: c.SkillLevel(Skill.Medicine) + (c.Role == CrewRole.Medic ? 0.25f : 0f) - (c.Needs.Rest < 0.3f ? 0.2f : 0f) - 0.35f * Guilt.GetValueOrDefault(c.Id) + 0.08f * Confidence.GetValueOrDefault(c.Id)))
                .OrderByDescending(x => x.s).ThenBy(x => x.c.Id).ToList();
            if (cands.Count == 0) continue;
            var pick = cands[0].c;
            // 손을 놓친 기억 — 다른 사람이 있으면 물러선다
            var first = cands.OrderByDescending(x => x.c.SkillLevel(Skill.Medicine) + (x.c.Role == CrewRole.Medic ? 0.25f : 0f)).ThenBy(x => x.c.Id).First().c;
            if (first != pick && Guilt.GetValueOrDefault(first.Id) > 0.5f)
            {
                StepBacks++;
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(first.Name)} 수술대 앞에 서지 못했다 — {pick.Name}에게 맡겼다", first.Id);
                MarkLog.Add(first.Memory.Marks, w.Tick, $"{pt.Name} 수술을 {pick.Name}에게 넘겼다 — 손이 떨렸다");
            }
            k.Surgeon = pick.Id;
            var helper = w.Crew.Where(c => c != pt && c != pick && c.CanAct && !c.Outside && !c.IsChild && !Busy(c) && !Cases.Any(x => x.Surgeon == c.Id || x.Assistant == c.Id) && w.Grades.Now(c) < InjuryGrade.Serious)
                .OrderByDescending(c => c.SkillLevel(Skill.Medicine) + (c.Role == CrewRole.Medic ? 0.2f : 0f) + 0.1f * pick.AffinityTo(c)).ThenBy(c => c.Id).FirstOrDefault();
            if (!(k.Assistant >= 0 && CrewOf(k.Assistant) is CrewMember a0 && a0.CanAct && a0 != pick)) k.Assistant = helper?.Id ?? -1;
            if (!cont) k.Table = PickTable(pt)?.Id ?? -1;
            if (cont) w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(pick.Name)} {pt.Name} 수술을 이어받는다", pick.Id);
            pick.NextThinkTick = Math.Min(pick.NextThinkTick, w.Tick + 1);
            if (helper != null) helper.NextThinkTick = Math.Min(helper.NextThinkTick, w.Tick + 1);
            pt.NextThinkTick = Math.Min(pt.NextThinkTick, w.Tick + 1);
        }
    }

    /// <summary>수술할 자리: 빈 수술대 → 환자가 누운 치료 침대 → 빈 치료 침대.</summary>
    private Furniture? PickTable(CrewMember pt)
    {
        var w = _w;
        bool Free(Furniture f) => f.ReservedBy == null || f.ReservedBy == pt;
        bool Taken(Furniture f) => Cases.Any(x => x.Patient != pt.Id && x.Table == f.Id && x.State != CaseState.Done);
        var t = w.Ship.FurnitureOf(FurnitureType.OperatingTable).Where(f => !f.Room.Detached && Free(f) && !Taken(f) && !w.Crew.Any(c => c != pt && !c.Dead && f.Cells.Contains(c.Cell) && c.Pose != Pose.Walking)).OrderBy(f => f.Id).FirstOrDefault();
        if (t != null) return t;
        if (pt.CareBed is Furniture cb && !Taken(cb)) return cb;
        return w.Ship.FurnitureOf(FurnitureType.MedBed).Where(f => !f.Room.Detached && Free(f) && !Taken(f)).OrderBy(f => f.Id).FirstOrDefault();
    }

    /// <summary>주컴퓨터: 기다리는 수술이 둘 넘으면 순서를 권한다 (위중 · 배 속 출혈부터).</summary>
    private void CastOrder()
    {
        var w = _w;
        var a = w.Automation;
        var wait = Cases.Where(k => k.State is CaseState.Waiting or CaseState.Prep or CaseState.Deferred).ToList();
        if (wait.Count < 2 || !a.Present || !a.MainOnline) return;
        string text = string.Join(" → ", wait.Select((k, i) => $"{i + 1}. {CrewOf(k.Patient)?.Name}({KindName(k.Kind)}" + (w.Grades.Now(CrewOf(k.Patient)!) == InjuryGrade.Critical ? " · 위중" : "") + ")"));
        if (text == _lastOrder && w.Tick - _lastOrderCast < SimTime.Hours(2)) return;
        _lastOrder = text; _lastOrderCast = w.Tick;
        a.Speak.Announce(a.Voice.Style($"수술 순서 — {text}"), CrewOf(wait[0].Patient)?.Room, 2);
        a.Book.Add(ActKind.Plan, CrewOf(wait[0].Patient)?.Room, $"수술을 기다리는 사람 {wait.Count}", "급한 순서: 배 속 출혈 · 위중 → 머리 → 팔다리 → 뼈 · 화상",
            $"순서를 알렸다: {text}", "", "surgorder", SimTime.Hours(2));
    }

    // ───────────── 수술실 설비 (켜고 끄기 · 비상 배터리) ─────────────

    private void Machines()
    {
        var w = _w;
        foreach (var f in w.Ship.Furniture)
        {
            if (f.Type is not (FurnitureType.OperatingTable or FurnitureType.SurgicalLamp or FurnitureType.AnesthesiaMachine) || f.Machine is not Machine m) continue;
            bool on = false;
            foreach (var k in Cases) if (k.State is CaseState.Prep or CaseState.Operating && TableOf(k)?.Room == f.Room) on = true;
            m.Active = on;
            if (f.Type == FurnitureType.SurgicalLamp)
            {
                float ch = _lampCharge.TryGetValue(f.Id, out var x) ? x : 40f;
                float min = World.SystemInterval / (float)SimTime.TicksPerHour * 60f;
                if (m.Powered) ch = MathF.Min(40f, ch + min * 0.5f);
                else if (on) ch = MathF.Max(0f, ch - min);
                _lampCharge[f.Id] = ch;
            }
        }
    }

    public float LampCharge(Furniture f) => _lampCharge.TryGetValue(f.Id, out var x) ? x : 40f;

    /// <summary>수술대 위 빛: 2 무영등 · 1 비상 배터리 · 0.5 방 불빛만 · 0 캄캄함.</summary>
    public float Light(SurgeryCase k)
    {
        var t = TableOf(k);
        if (t == null) return 0.5f;
        Furniture? lamp = null;
        foreach (var f in t.Room.Furniture) if (f.Type == FurnitureType.SurgicalLamp) { lamp = f; break; }
        if (lamp?.Machine is Machine m && m.Faults.Count == 0 || lamp != null && lamp.Machine == null)
        {
            if (lamp!.Machine == null || lamp.Machine.Powered && lamp.Machine.Efficiency > 0.2f) return 2f;
            if (LampCharge(lamp) > 0f) return 1f;
        }
        return t.Room.Dark ? 0f : 0.5f;
    }

    // ───────────── 수술 (집도의의 손 — SurgeryToil이 한 틱씩) ─────────────

    /// <summary>수술 한 틱. Running · Succeeded(끝) · Failed(손을 놓았다 — 다시 잡는다).</summary>
    internal ToilStatus Step(SurgeryCase k, CrewMember s)
    {
        var w = _w;
        if (k.State == CaseState.Done || k.State == CaseState.Deferred) return ToilStatus.Succeeded;
        var pt = CrewOf(k.Patient);
        if (pt == null || pt.Dead) { Close(k, "수술대 위에서 숨졌다", false); return ToilStatus.Succeeded; }
        var table = TableOf(k);
        if (table == null) return ToilStatus.Failed;
        if ((s.Position - table.Center).Length() > 2.7f) return ToilStatus.Failed;
        bool lying = table.Cells.Contains(pt.Cell) && pt.CarriedBy == null;
        if (!lying)
        {
            if (++k.Waited > SimTime.Minutes(45)) { Release(k, "환자가 오지 않았다"); return ToilStatus.Failed; }
            return ToilStatus.Running;
        }
        if (k.State == CaseState.Waiting)
        {
            k.State = CaseState.Prep; k.Phase = 0; k.PhaseTicks = 0; k.Started = w.Tick;
            table.ReservedBy = pt;
            if (pt.CareBed != null && pt.CareBed != table) { if (pt.CareBed.ReservedBy == pt) pt.CareBed.ReservedBy = null; pt.CareBed = null; }
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(s.Name)} {pt.Name} {KindName(k.Kind)}을 시작한다" + (CrewOf(k.Assistant) is CrewMember asx ? $" (보조 {asx.Name})" : " (혼자)"), s.Id);
        }
        k.PhaseTicks++;
        switch (k.Phase)
        {
            case 0: // 손 씻기 · 기구 멸균
                if (k.PhaseTicks < SimTime.Minutes(10)) return ToilStatus.Running;
                Sterilize(k, table);
                k.Phase = 1; k.PhaseTicks = 0;
                return ToilStatus.Running;
            case 1: // 마취
                if (k.PhaseTicks < SimTime.Minutes(5)) return ToilStatus.Running;
                if (!Anesthetize(k, pt, s)) return ToilStatus.Succeeded;
                k.Phase = 2; k.PhaseTicks = 0; k.State = CaseState.Operating;
                k.NeedTicks = SimTime.Hours(Hours(k.Kind)) * (1.5f - 0.7f * s.SkillLevel(Skill.Medicine));
                if (k.Kind == SurgeryKind.Salvage) Decide(k, pt, s);
                Forecast(k, pt);
                return ToilStatus.Running;
            case 2:
                Operate(k, pt, s, table);
                return ToilStatus.Running;
            default: // 닫기
                if (k.PhaseTicks < SimTime.Minutes(6)) return ToilStatus.Running;
                Finish(k, pt, s);
                return ToilStatus.Succeeded;
        }
    }

    private void Sterilize(SurgeryCase k, Furniture table)
    {
        var w = _w;
        var ac = table.Room.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Autoclave);
        if (ac?.Machine is Machine m && m.Powered && m.Efficiency > 0.3f) { k.Sterile = 1f; k.Notes.Add("멸균기"); }
        else if (ItemsV15.Use(w, ItemKind.Disinfectant)) { k.Sterile = 0.75f; k.Notes.Add(ac != null ? "멸균기가 꺼져 소독약으로" : "소독약"); }
        else { k.Sterile = 0.45f; k.Notes.Add("끓인 물로만"); }
    }

    private bool Anesthetize(SurgeryCase k, CrewMember pt, CrewMember s)
    {
        var w = _w;
        if (w.Pharmacy.Take(ItemKind.Anesthetic)) { k.Anesthesia = true; return true; }
        bool crit = w.Grades.Now(pt) == InjuryGrade.Critical || Internal(pt);
        if (!crit)
        {
            Defer(k, "마취제가 없다");
            return false;
        }
        // 위중 — 마취 없이 무릅쓴다
        k.NoAnesthesia = true; NoAnesthesiaOps++;
        pt.Needs.Stress = MathF.Min(1f, pt.Needs.Stress + 0.5f);
        Memory.Frighten(w, pt, pt.Room, 0.2f, "마취 없이 수술을 받았다");
        MarkLog.Add(s.Memory.Marks, w.Tick, $"{pt.Name}을 마취 없이 열었다 — 비명이 귀에 남는다");
        w.Log.Add(w.Tick, LogKind.Warning, $"마취제가 없다 — {Ko.IGa(s.Name)} {pt.Name}을 마취 없이 연다 (기다리면 숨진다)", s.Id);
        if (w.Automation.Present && w.Automation.MainOnline)
            w.Automation.Book.Add(ActKind.Advice, pt.Room, "마취제 0 · 위중", "기다리면 숨진다", "마취 없이 하는 것을 막지 않았다 — 꽉 붙잡으라고 했다", "", $"noanes:{k.Id}", SimTime.Hours(1));
        return true;
    }

    private void Defer(SurgeryCase k, string why)
    {
        var w = _w;
        k.State = CaseState.Deferred; k.DeferWhy = why; k.Phase = 0; k.PhaseTicks = 0; k.Waited = 0;
        Deferrals++;
        var pt = CrewOf(k.Patient);
        if (TableOf(k) is Furniture t && t.ReservedBy == pt) t.ReservedBy = null;
        k.Surgeon = -1; k.Assistant = -1;
        w.Log.Add(w.Tick, LogKind.Warning, $"{pt?.Name} {KindName(k.Kind)}을 미뤘다 — {why}", pt?.Id ?? -1);
        if (w.Automation.Present && w.Automation.MainOnline)
            w.Automation.Book.Add(ActKind.Advice, pt?.Room, $"{pt?.Name} {KindName(k.Kind)} — {why}", "급하지 않다 — 마취 없이 하면 몸이 버티지 못한다",
                "미루고 마취제를 만들게 했다", "", $"defer:{k.Id}", SimTime.Hours(6));
    }

    /// <summary>팔다리 살리기냐 절단이냐: 다친 지 오래 · 몸이 약함 · 솜씨 · 컴퓨터 의견.</summary>
    private void Decide(SurgeryCase k, CrewMember pt, CrewMember s)
    {
        var w = _w;
        float hours = (w.Tick - (w.Casualty.Done.Concat(w.Casualty.Open).Where(t => t.CrewId == pt.Id).Select(t => t.Since).DefaultIfEmpty(k.Opened).Max())) / (float)SimTime.TicksPerHour;
        float sev = pt.Vitals.Wounds.Where(x => x.Part == k.Part && !x.Lost).Select(x => Wounds.Severity(pt.Vitals, x)).DefaultIfEmpty(0f).Max();
        float keep = s.SkillLevel(Skill.Medicine) * 0.6f + (pt.Vitals.Health > 0.4f ? 0.25f : -0.1f) - (hours > 12f ? 0.3f : 0f) - (sev > 0.7f ? 0.25f : 0f) + (Light(k) >= 2f ? 0.05f : -0.1f);
        string comp = "";
        if (w.Automation.Present && w.Automation.MainOnline)
        {
            float odds = Math.Clamp(0.5f + keep * 0.6f, 0.05f, 0.95f);
            comp = $"컴퓨터: 살릴 가망 {odds * 100:0}%";
            w.Automation.Book.Add(ActKind.Advice, pt.Room, $"{pt.Name} {Wounds.PartName(k.Part)} 상처 {sev * 100:0}% · 다친 지 {hours:0.#}시간", "살리려다 실패하면 더 크게 잃는다",
                comp, "", $"salvage:{k.Id}", SimTime.Hours(2));
        }
        if (keep >= 0.25f) { k.Decision = "살린다"; Salvages++; }
        else { k.Kind = SurgeryKind.Amputate; k.Decision = "자른다"; k.NeedTicks = SimTime.Hours(Hours(k.Kind)) * (1.5f - 0.7f * s.SkillLevel(Skill.Medicine)); }
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(s.Name)} {pt.Name}의 {Wounds.PartName(k.Part)} — {k.Decision}" + (comp != "" ? $" ({comp})" : ""), s.Id);
    }

    /// <summary>주컴퓨터: 이 수술에 피가 얼마나 들지 (시작할 때).</summary>
    private void Forecast(SurgeryCase k, CrewMember pt)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline || pt.Room?.DataLinked != true) return;
        float hours = k.NeedTicks / SimTime.TicksPerHour;
        float loss = Loss(k.Kind) * hours + (Internal(pt) ? (w.Casualty.Of(pt)?.Rate ?? 0f) * 0.2f * hours : 0f);
        int packs = (int)MathF.Ceiling(MathF.Max(0f, loss - (pt.Vitals.Health - 0.35f)) / 0.28f);
        int have = w.Blood.CompatibleFor(pt);
        BleedForecasts++;
        string text = packs <= 0 ? $"{pt.Name} {KindName(k.Kind)} — 약 {hours * 60:0}분 · 피는 버틸 만하다" : $"{pt.Name} {KindName(k.Kind)} — 약 {hours * 60:0}분 · 피 {packs}팩이 들 것 (맞는 팩 {have})";
        a.Book.Add(ActKind.Forecast, pt.Room, text, $"{w.Blood.TypeOf(pt).Name} · 체력 {pt.Vitals.Health * 100:0}%", packs > have ? "헌혈자를 미리 불렀다" : "냉장고 팩을 꺼내 두라고 했다", "", $"bleedfc:{k.Id}", SimTime.Hours(3));
        if (packs > have) w.Blood.Call(pt, $"수술 중 피 {packs}팩이 들 것");
    }

    private void Operate(SurgeryCase k, CrewMember pt, CrewMember s, Furniture table)
    {
        var w = _w;
        k.OpTicks++;
        float light = Light(k);
        if (light <= 0f) k.DarkTicks++;
        else if (light < 2f) k.BatteryTicks++;
        bool shake = w.Propulsion.Burning;
        bool hold = shake && k.Warned;
        if (shake && !k.Warned) k.ShakeTicks++;
        if (hold) k.HoldTicks++;
        if (w.ZeroG.Weightless && w.ZeroG.RingRoom != table.Room) k.ZeroGTicks++;
        // 피: 수술 중에도 빠진다 (빛이 없고 · 서툴면 더)
        float loss = Loss(k.Kind) * (light <= 0f ? 1.6f : 1f) * (1.3f - 0.5f * s.SkillLevel(Skill.Medicine)) / SimTime.TicksPerHour;
        if (!k.Anesthesia && (CrewOf(k.Assistant) is not CrewMember)) loss *= 1.2f; // 붙잡아 줄 사람 없이 마취 없이
        if (FurnitureAt(table.Room, FurnitureType.AnesthesiaMachine) is Furniture am && am.Machine is Machine mm && !mm.Powered && k.Anesthesia && CrewOf(k.Assistant) == null)
            loss += 0.02f / SimTime.TicksPerHour; // 마취기가 멎었는데 손으로 짜 줄 사람이 없다
        pt.Vitals.Health = MathF.Max(0f, pt.Vitals.Health - loss);
        if (hold) return;
        float speed = Wounds.HandFactor(s.Vitals) * (s.Needs.Rest < 0.2f ? 0.8f : 1f) * (light <= 0f ? 0.6f : 1f) * (CrewOf(k.Assistant) is CrewMember ax && (ax.Position - table.Center).Length() < 2.8f ? 1.15f : 1f);
        k.Progress = MathF.Min(1f, k.Progress + speed / MathF.Max(1f, k.NeedTicks));
        if (k.Progress >= 1f) { k.Phase = 3; k.PhaseTicks = 0; }
    }

    private static Furniture? FurnitureAt(Room r, FurnitureType t) { foreach (var f in r.Furniture) if (f.Type == t) return f; return null; }

    /// <summary>수술 중 (시스템 틱마다): 생체 신호 · 수혈 · 정전 · 흔들림 · 점화.</summary>
    private void Monitor(SurgeryCase k)
    {
        var w = _w;
        var pt = CrewOf(k.Patient);
        var table = TableOf(k);
        if (pt == null || pt.Dead || table == null) return;
        var a = w.Automation;
        bool comp = a.Present && a.MainOnline && table.Room.DataLinked;
        // 피가 모자라면: 보조(없으면 집도의)가 냉장고 피를 넣는다 · 없으면 부른다
        if (k.State == CaseState.Operating && pt.Vitals.Health < 0.35f && w.Tick - k.LastBlood > SimTime.Minutes(20))
        {
            k.LastBlood = w.Tick;
            var by = CrewOf(k.Assistant) ?? CrewOf(k.Surgeon);
            string did = w.Blood.Transfuse(pt, by, desperate: pt.Vitals.Health < 0.1f && !w.Blood.Donors.Values.Any(d => d.forId == pt.Id));
            if (did != "") { k.Bloods++; k.Notes.Add(did); }
            else w.Blood.Call(pt, "수술 중 피가 모자라다");
        }
        // 생체 신호
        if (comp && k.State == CaseState.Operating && (pt.Vitals.Health < k.LastWarnHealth - 0.08f || pt.Vitals.Health < 0.25f && k.LastWarnHealth >= 0.25f))
        {
            k.LastWarnHealth = pt.Vitals.Health;
            if (pt.Vitals.Health < 0.45f)
            {
                VitalWarnings++;
                a.Speak.Announce(a.Voice.Style($"{pt.Name} 혈압이 떨어집니다 — 체력 {pt.Vitals.Health * 100:0}%" + (w.Blood.CompatibleFor(pt) > 0 ? " · 피를 넣으십시오" : " · 맞는 피가 없습니다")), table.Room, 3);
                a.Book.Add(ActKind.Alarm, table.Room, $"{pt.Name} 생체 신호 — 체력 {pt.Vitals.Health * 100:0}% · 맥이 빠르다", "피가 빠지고 있다",
                    "혈압이 떨어진다고 알렸다", "", $"vital:{k.Id}:{(int)(pt.Vitals.Health * 10)}", SimTime.Minutes(10));
            }
        }
        // 정전: 수술실 설비에 전기가 안 들어온다 → 몰아준다 (Holds가 우선순위를 올린다) · 무영등은 비상 배터리로
        var lamp = FurnitureAt(table.Room, FurnitureType.SurgicalLamp);
        bool dark = lamp?.Machine is Machine lm && !lm.Powered;
        if (dark && !k.PowerLost)
        {
            k.PowerLost = true;
            k.Notes.Add("정전");
            w.Log.Add(w.Tick, LogKind.Warning, $"수술 중 정전 — 무영등이 비상 배터리로 넘어갔다 ({LampCharge(lamp!):0}분)", k.Surgeon);
            if (comp || a.Present && a.MainOnline)
            {
                a.Speak.Announce(a.Voice.Style($"{table.Room.Name} 수술 중 — 급한 데부터 전기를 돌립니다. 수술실 회로를 먼저 살립니다"), table.Room, 3);
                a.Book.Add(ActKind.Shed, table.Room, "수술 중인 방에 전기가 끊겼다", "무영등 배터리는 몇십 분 · 마취기 · 냉장고",
                    "수술실 회로를 맨 앞으로 올렸다 (다른 데를 끈다)", "", $"orpower:{k.Id}", SimTime.Minutes(30));
            }
        }
        else if (!dark && k.PowerLost && lamp != null)
        {
            k.PowerLost = false;
            PowerRouted++;
            k.Notes.Add("전기가 돌아왔다");
            w.Log.Add(w.Tick, LogKind.Work, $"수술실에 전기가 다시 들어왔다 — 무영등이 켜졌다", k.Surgeon);
        }
        // 회피 기동 직전: 손을 떼라고
        if (w.Propulsion.Current is Burn b && !k.Warned && w.Tick < b.Ignite && comp)
        {
            k.Warned = true;
            WarnedShakes++;
            a.Speak.Announce(a.Voice.Style($"{(b.Ignite - w.Tick) / (float)SimTime.TicksPerHour * 60f:0}분 뒤 엔진 점화 — {table.Room.Name}, 칼을 떼고 기다리십시오"), table.Room, 3);
        }
        if (w.Propulsion.Current == null && k.Warned && !w.Propulsion.Burning && k.HoldTicks > 0) k.Warned = false;
        // 항로 점화가 잡혀 있다 → 함장에게 미루자고
        if (comp && !BurnHeld && w.Board.OpenUnsorted.Any(o => o.Kind == WorkKind.ChangeCourse) && a.Asks.Pending("surgburn") == null
            && (a.Asks.Latest("surgburn") is not Proposal last || w.Tick - last.Tick > SimTime.Hours(1)))
        {
            float left = k.State == CaseState.Operating ? (1f - k.Progress) * k.NeedTicks + SimTime.Minutes(15) : SimTime.Hours(Hours(k.Kind) + 0.4f);
            BurnProposals++;
            a.Asks.Propose("surgburn", "surgery", table.Room, $"수술이 끝날 때까지 항로 점화를 미룬다 (약 {left / SimTime.TicksPerHour * 60f:0}분)",
                $"{pt.Name} {KindName(k.Kind)} 중 — 흔들리면 칼끝이 빗나간다", "항로는 늦게 바뀌지만 수술은 흔들리지 않는다", 8f, null,
                (world, p) => { world.Surgery.HoldBurn((long)left); });
        }
    }

    public void HoldBurn(long ticks)
    {
        var w = _w;
        HoldBurnUntil = Math.Max(HoldBurnUntil, w.Tick + ticks);
        BurnHolds++;
        w.Log.Add(w.Tick, LogKind.Ship, $"수술이 끝날 때까지 항로 점화를 미뤘다 ({ticks / (float)SimTime.TicksPerHour * 60f:0}분)");
    }

    // ───────────── 결과 ─────────────

    /// <summary>성공할 가망 (지금 사정으로).</summary>
    public float Odds(SurgeryCase k, CrewMember s)
    {
        var w = _w;
        var pt = CrewOf(k.Patient);
        float op = MathF.Max(1f, k.OpTicks);
        float q = 0.5f + 0.4f * s.SkillLevel(Skill.Medicine) + Ease(k.Kind);
        if (CrewOf(k.Assistant) is CrewMember ax) q += 0.06f + (ax.SkillLevel(Skill.Medicine) >= 0.4f || ax.Role == CrewRole.Medic ? 0.04f : 0f);
        q -= 0.3f * (k.DarkTicks / op) + 0.06f * (k.BatteryTicks / op);
        q += (k.Sterile - 0.7f) * 0.2f;
        if (s.Needs.Rest < 0.3f) q -= 0.1f;
        if (s.Needs.Stress > 0.7f) q -= 0.06f;
        q -= 0.18f * Guilt.GetValueOrDefault(s.Id);
        q += 0.06f * Confidence.GetValueOrDefault(s.Id);
        q -= (TableOf(k)?.Type == FurnitureType.OperatingTable ? 0.07f : 0.15f) * (k.ZeroGTicks / op); // 수술대에는 묶는 띠가 있다
        q -= MathF.Min(0.25f, 0.04f * k.ShakeTicks / SimTime.Minutes(1));
        if (k.NoAnesthesia) q -= 0.15f;
        if (TableOf(k)?.Type != FurnitureType.OperatingTable) q -= 0.08f;
        if (pt != null && pt.Vitals.Health < 0.25f) q -= 0.08f;
        return Math.Clamp(q, 0.05f, 0.97f);
    }

    private void Finish(SurgeryCase k, CrewMember pt, CrewMember s)
    {
        var w = _w;
        Operations++;
        float p = Odds(k, s);
        k.Chance = p;
        float roll = R.Float();
        bool ok = roll < p;
        bool clean = roll < p - 0.3f;
        bool fatal = !ok && roll > p + 0.35f && (w.Grades.Now(pt) == InjuryGrade.Critical || k.Kind is SurgeryKind.Laparotomy or SurgeryKind.Head);
        var v = pt.Vitals;
        string part = Wounds.PartName(k.Part);
        MarkDone(pt, k.Part, k.Kind);
        string scarText = "";
        void Scar(float amt, string why) { v.Scar = MathF.Min(0.3f, v.Scar + amt); v.ScarFloor = MathF.Max(v.ScarFloor, v.Scar * 0.5f); v.ScarCause ??= why; scarText = why; }
        void Ease(float inj) { v.Injury = MathF.Max(0f, v.Injury - inj); }
        void Shrink(Func<Wound, bool> which, float mul) { foreach (var x in v.Wounds) if (!x.Lost && which(x)) x.Weight *= mul; }
        var trauma = w.Casualty.Of(pt);
        if (ok)
        {
            Successes++;
            switch (k.Kind)
            {
                case SurgeryKind.Suture: v.TreatedTick = w.Tick; Ease(0.05f); Scar(0.01f, $"{part} 꿰맨 자국"); break;
                case SurgeryKind.Fracture: Ease(0.08f); w.Recovery.Fixed(pt, k.Part); Scar(0.015f, $"{part} 고정한 자국"); break;
                case SurgeryKind.Laparotomy:
                    if (trauma != null) _internal.Remove(trauma.Id);
                    v.TreatedTick = w.Tick; v.Health = MathF.Min(v.MaxHealth, v.Health + 0.05f); Ease(0.06f); Scar(0.03f, "배를 가른 자국"); break;
                case SurgeryKind.Head: Shrink(x => x.Part == BodyPart.Head, 0.5f); Ease(0.06f); Scar(0.02f, "머리 꿰맨 자국"); break;
                case SurgeryKind.Graft:
                    if (trauma is { Kind: TraumaKind.BurnShock }) v.TreatedTick = w.Tick;
                    Shrink(x => x.Kind == WoundKind.Burn && x.Part == k.Part, 0.6f); Ease(0.08f); Scar(0.04f, $"{part} 피부를 옮겨 붙인 자국"); break;
                case SurgeryKind.Salvage:
                    v.TreatedTick = w.Tick; Ease(0.1f); w.Recovery.Fixed(pt, k.Part); Scar(0.04f, $"{part}을 살린 자국"); break;
                case SurgeryKind.Amputate:
                    Amputate(pt, k.Part); v.TreatedTick = w.Tick; Ease(0.12f); break;
            }
            // 후유증: 팔다리를 크게 손댔으면 (깨끗하지 않을수록)
            bool limb = Wounds.IsArm(k.Part) || Wounds.IsLeg(k.Part);
            if (limb && k.Kind is SurgeryKind.Fracture or SurgeryKind.Salvage or SurgeryKind.Graft && R.Chance((clean ? 0.15f : 0.55f) + (k.Kind == SurgeryKind.Salvage ? 0.25f : 0f)))
                w.Recovery.Sequela(pt, k.Part, $"{KindName(k.Kind)} 뒤");
            k.Outcome = clean ? "깨끗이 끝났다" : "되었다 — 흉이 남는다";
            Remember(k, pt, s, true, false);
        }
        else
        {
            Failures++;
            if (fatal)
            {
                Deaths++;
                v.InjuryCause = $"{KindName(k.Kind)} 중 출혈";
                v.Health = w.CrewCanDie ? 0f : 0.02f;
                k.Outcome = "수술대 위에서 숨졌다";
            }
            else
            {
                v.Health = MathF.Max(0.02f, v.Health - 0.15f);
                v.Injury = MathF.Min(1f, v.Injury + 0.04f);
                if (k.Kind == SurgeryKind.Salvage) { Amputate(pt, k.Part); k.Outcome = "살리지 못해 결국 잘랐다"; }
                else if (k.Kind == SurgeryKind.Head) { Scar(0.06f, "머리 수술 뒤"); k.Outcome = "더 나빠졌다 — 머리가 흐리다"; }
                else k.Outcome = "더 나빠졌다";
                if (Wounds.IsArm(k.Part) || Wounds.IsLeg(k.Part)) w.Recovery.Sequela(pt, k.Part, $"{KindName(k.Kind)}이 잘 안 됐다");
            }
            Remember(k, pt, s, false, fatal);
        }
        // 곪을까: 멸균이 모자랄수록 · 예방 항생제(아낄 때는 큰 수술만)
        if (!pt.Dead && v.Health > 0f)
        {
            float inf = (1f - k.Sterile) * 0.5f + (k.Kind is SurgeryKind.Laparotomy or SurgeryKind.Salvage or SurgeryKind.Graft ? 0.08f : 0f);
            bool big = k.Kind is not (SurgeryKind.Suture or SurgeryKind.Fracture);
            if ((!w.Pharmacy.Careful || big) && w.Pharmacy.Take(ItemKind.Antibiotic))
            {
                inf *= 0.3f + 0.7f * w.Pharmacy.Resistance;
                k.Notes.Add("예방 항생제");
            }
            if (R.Chance(inf)) { w.Ailments.Catch(pt, "woundinf", null, "수술 자리가 곪았다"); k.Notes.Add("곪음"); }
            w.Pharmacy.Painkill(pt, s, "수술 뒤");
            w.Recovery.PostOp(pt, k);
        }
        k.Success = ok;
        if (TableOf(k) is Furniture t && t.ReservedBy == pt) t.ReservedBy = null;
        string line = $"{pt.Name} {KindName(k.Kind)} — {k.Outcome} (집도 {s.Name} · 가망 {p * 100:0}%" + (k.Notes.Count > 0 ? $" · {string.Join(" · ", k.Notes.Distinct())}" : "") + ")";
        w.Log.Add(w.Tick, ok ? LogKind.Work : LogKind.Warning, line, s.Id);
        if (k.Kind is not SurgeryKind.Suture || !ok)
            w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(s.Name)} {pt.Name}의 {KindName(k.Kind)}을 했다 — {k.Outcome}" + (scarText != "" && ok ? $" ({scarText})" : ""), pt.Room, new[] { s, pt });
        Close(k, k.Outcome, ok);
    }

    private void Amputate(CrewMember pt, BodyPart part)
    {
        var w = _w;
        var x = pt.Vitals.Wounds.FirstOrDefault(y => y.Part == part && !y.Lost);
        if (x == null) return;
        x.Lost = true;
        Amputations++;
        MarkLog.Add(pt.Memory.Marks, w.Tick, $"{Wounds.PartName(part)}을 잃었다");
        pt.Needs.Stress = MathF.Min(1f, pt.Needs.Stress + 0.3f);
        w.Recovery.Lost(pt, part);
    }

    /// <summary>집도의 · 환자 · 보조의 기억.</summary>
    private void Remember(SurgeryCase k, CrewMember pt, CrewMember s, bool ok, bool died)
    {
        var w = _w;
        var asx = CrewOf(k.Assistant);
        s.Practice(Skill.Medicine, ok ? 0.06f : 0.03f);
        if (ok)
        {
            Confidence[s.Id] = MathF.Min(1f, Confidence.GetValueOrDefault(s.Id) + 0.15f);
            Guilt[s.Id] = MathF.Max(0f, Guilt.GetValueOrDefault(s.Id) - 0.15f);
            s.Needs.Stress = MathF.Max(0f, s.Needs.Stress - 0.05f);
            MarkLog.Add(s.Memory.Marks, w.Tick, $"{pt.Name} {KindName(k.Kind)} — 살렸다");
            MarkLog.Add(pt.Memory.Marks, w.Tick, $"{Ko.IGa(s.Name)} 수술로 살렸다 ({KindName(k.Kind)})");
            pt.ChangeAffinity(s, 0.12f); s.ChangeAffinity(pt, 0.05f);
            w.Relations.Remember(pt, s, RelationReason.SavedMe, $"{KindName(k.Kind)}을 해 줬다");
            if (asx != null) { s.ChangeAffinity(asx, 0.04f); asx.ChangeAffinity(s, 0.04f); w.Relations.Remember(s, asx, RelationReason.GoodPartner, "수술대 건너에서 손을 맞췄다"); }
        }
        else
        {
            float g = died ? 0.7f : 0.35f;
            Guilt[s.Id] = MathF.Min(1f, Guilt.GetValueOrDefault(s.Id) + g);
            Confidence[s.Id] = MathF.Max(0f, Confidence.GetValueOrDefault(s.Id) - 0.2f);
            s.Needs.Stress = MathF.Min(1f, s.Needs.Stress + (died ? 0.4f : 0.2f));
            MarkLog.Add(s.Memory.Marks, w.Tick, died ? $"{pt.Name} — 내 손에서 숨졌다" : $"{pt.Name} {KindName(k.Kind)} — 잘 안 됐다");
            if (died)
            {
                s.Memory.Trauma = MathF.Max(s.Memory.Trauma, 0.25f);
                s.Memory.TraumaCause ??= $"{pt.Name}의 수술";
                if (TableOf(k)?.Room is Room room) Memory.Frighten(w, s, room, 0.2f, $"{Ko.IGa(pt.Name)} 수술대 위에서 숨졌다");
                if (asx != null) MarkLog.Add(asx.Memory.Marks, w.Tick, $"{pt.Name} 수술 — 곁에서 봤다");
            }
            else if (pt.Room != null) MarkLog.Add(pt.Memory.Marks, w.Tick, $"{KindName(k.Kind)} 뒤 더 아프다");
        }
    }

    private void Release(SurgeryCase k, string why)
    {
        if (k.State == CaseState.Done) return;
        var pt = CrewOf(k.Patient);
        if (TableOf(k) is Furniture t && t.ReservedBy == pt && !(pt != null && t.Cells.Contains(pt.Cell))) t.ReservedBy = null;
        k.State = CaseState.Waiting; k.Surgeon = -1; k.Assistant = -1; k.Phase = 0; k.PhaseTicks = 0; k.Waited = 0;
        k.Notes.Add(why);
    }

    /// <summary>집도의가 손을 놓았다 (불려 감 · 다침) — 다음 사람이 이어받는다.</summary>
    internal void Abandoned(SurgeryCase k, CrewMember s)
    {
        if (k.State is CaseState.Done or CaseState.Deferred || k.Surgeon != s.Id) return;
        var w = _w;
        if (k.State == CaseState.Operating) w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(s.Name)} 수술 중에 손을 놓았다 — 다른 사람이 이어받아야 한다", s.Id);
        k.Surgeon = -1;
        if (k.State == CaseState.Prep) { k.State = CaseState.Waiting; k.Phase = 0; k.PhaseTicks = 0; }
    }

    private void Close(SurgeryCase k, string outcome, bool ok)
    {
        k.State = CaseState.Done;
        k.Outcome = outcome;
        Cases.Remove(k);
        Done.Add(k);
        if (Done.Count > 40) Done.RemoveAt(0);
        var pt = CrewOf(k.Patient);
        if (TableOf(k) is Furniture t && pt != null && t.ReservedBy == pt && !t.Cells.Contains(pt.Cell)) t.ReservedBy = null;
    }

    /// <summary>죄책감 · 자신감은 날이 가며 옅어진다.</summary>
    private void Moods(float dt)
    {
        float days = SimTime.Minutes(10) / (float)SimTime.TicksPerDay;
        foreach (var id in Guilt.Keys.ToList()) { Guilt[id] = MathF.Max(0f, Guilt[id] - 0.04f * days); if (Guilt[id] <= 0f) Guilt.Remove(id); }
        foreach (var id in Confidence.Keys.ToList()) { Confidence[id] = MathF.Max(0f, Confidence[id] - 0.01f * days); if (Confidence[id] <= 0f) Confidence.Remove(id); }
    }

    /// <summary>시험 · 장면: 그 사람에게 배 속 출혈을 연다.</summary>
    public void MarkInternalFor(Trauma t) { _internal.Add(t.Id); InternalBleeds++; }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var k in Cases) { I(k.Id); I(k.Patient); I((int)k.Kind); I((int)k.State); I(k.Surgeon); I(k.Assistant); I(k.Phase); F(k.Progress); }
        foreach (var id in _internal) I(id);
        foreach (var kv in Guilt) { I(kv.Key); F(kv.Value); }
        foreach (var kv in Confidence) { I(kv.Key); F(kv.Value); }
        I(Operations); I(Successes); I(Failures); I(Deaths); I(Deferrals); I(HoldBurnUntil); I(Fitted);
    }
}

/// <summary>수술 한 단계: SurgerySystem.Step이 한 틱씩 진행한다 (손 씻기 → 마취 → 수술 → 닫기).</summary>
internal sealed class SurgeryToil : Toil
{
    private readonly SurgeryCase _k;
    private readonly Vector2 _face;
    public SurgeryToil(SurgeryCase k, Vector2 face) { _k = k; _face = face; }
    public override float? Progress => _k.Phase < 2 ? 0f : _k.Phase == 2 ? _k.Progress : 1f;
    public override void Begin(CrewMember c, World w) { c.Pose = Pose.Working; Locomotion.Face(c, _face); }
    public override ToilStatus Tick(CrewMember c, World w)
    {
        c.Pose = Pose.Working;
        return w.Surgery.Step(_k, c);
    }
    public override void End(CrewMember c, World w)
    {
        if (_k.State is CaseState.Prep or CaseState.Operating && _k.Surgeon == c.Id) w.Surgery.Abandoned(_k, c);
    }
}

/// <summary>집도: 맡은 수술 — 쓰러진 환자는 업어 수술대에 눕히고 · 손 씻고 · 마취하고 · 수술하고 · 끝나면 침대로 옮긴다.</summary>
public sealed class SurgeryActivity : Activity
{
    public override string Id => "surgery";
    public override string Label => "수술";

    private static SurgeryCase? Mine(CrewMember c, World w)
    {
        foreach (var k in w.Surgery.Cases) if (k.Surgeon == c.Id && k.State is CaseState.Waiting or CaseState.Prep or CaseState.Operating) return k;
        return null;
    }

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside) return (0f, "—");
        var k = Mine(c, w);
        if (k == null || w.Surgery.TableOf(k) is not Furniture t || t.UseSpots.Count == 0) return (0f, "—");
        var pt = w.Crew.FirstOrDefault(x => x.Id == k.Patient);
        if (pt == null || pt.Dead) return (0f, "—");
        bool crit = w.Grades.Now(pt) == InjuryGrade.Critical || w.Surgery.Internal(pt);
        if (c.Pose == Pose.Sleeping && !crit && k.State == CaseState.Waiting) return (0f, "아침에");
        if (k.State != CaseState.Waiting) return (1.25f, $"{pt.Name} {SurgerySystem.KindName(k.Kind)} 중");
        return (crit ? 1.12f : 0.78f + (OnShift(c, w) ? 0.06f : 0f), $"{pt.Name} — {SurgerySystem.KindName(k.Kind)} ({k.Why})");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var k = Mine(c, w);
        if (k == null || w.Surgery.TableOf(k) is not Furniture table) return null;
        var pt = w.Crew.FirstOrDefault(x => x.Id == k.Patient);
        if (pt == null || pt.Dead) return null;
        var tcell = table.Cells[0];
        Cell? side = Cell.Dirs8.Select(d => tcell + d).Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && w.Ship.RoomAt(x) == table.Room)
            .OrderBy(x => Math.Abs(x.X - tcell.X) + Math.Abs(x.Y - tcell.Y)).ThenBy(x => dist.Get(x)).ThenBy(x => x.X).ThenBy(x => x.Y).Cast<Cell?>().FirstOrDefault();
        if (side is not Cell at) return null;
        var toils = Plans.DropOff(c, w, dist);
        bool carry = pt.Down && !table.Cells.Contains(pt.Cell);
        if (carry)
        {
            if (RadCareActivity.Near(w, dist, pt) is not Cell near) return null;
            toils.Add(new GotoToil(near));
            toils.Add(new DoToil((cm, world) =>
            {
                if (pt.Dead || pt.CarriedBy != null && pt.CarriedBy != cm) return false;
                if (!pt.Down) return true; // 깨어났다 — 걸어온다
                if ((pt.Position - cm.Position).Length() > 2.2f) return false;
                if (pt.CareBed is Furniture cb) { if (cb.ReservedBy == pt) cb.ReservedBy = null; pt.CareBed = null; }
                pt.CarriedBy = cm; cm.CarryingPerson = pt;
                world.Log.Add(world.Tick, LogKind.Work, $"{Ko.EulReul(pt.Name)} 업고 수술대로 간다", cm.Id);
                return true;
            }));
            toils.Add(new GotoToil(tcell));
            toils.Add(new DoToil((cm, world) =>
            {
                if (cm.CarryingPerson != pt) return pt.Down ? false : true;
                pt.CarriedBy = null; cm.CarryingPerson = null;
                pt.Position = tcell.Center; pt.PreviousPosition = tcell.Center; pt.Room = world.Ship.RoomAt(tcell);
                table.ReservedBy = pt;
                return true;
            }));
        }
        toils.Add(new GotoToil(at));
        toils.Add(new SurgeryToil(k, table.Center));
        // 끝나고: 아직 깨지 못했으면 치료 침대(없으면 간이침대)로 옮긴다
        toils.Add(new DoToil((cm, world) =>
        {
            if (pt.Dead || !pt.Down || !table.Cells.Contains(pt.Cell) || k.State != CaseState.Done) return true;
            if ((pt.Position - cm.Position).Length() > 2.4f) return true;
            pt.CarriedBy = cm; cm.CarryingPerson = pt;
            return true;
        }));
        Furniture? bed = null;
        toils.Add(new GotoToilLate(cm =>
        {
            if (cm.CarryingPerson != pt) return null;
            bed = w.Recovery.BedFor(pt);
            return bed?.UseSpots.FirstOrDefault() ?? (Cell?)null;
        }));
        toils.Add(new DoToil((cm, world) =>
        {
            if (cm.CarryingPerson != pt) return true;
            pt.CarriedBy = null; cm.CarryingPerson = null;
            var dest = bed?.UseSpots.FirstOrDefault() ?? cm.Cell;
            pt.Position = dest.Center; pt.PreviousPosition = dest.Center; pt.Room = world.Ship.RoomAt(dest);
            if (bed != null) { bed.ReservedBy = pt; if (bed.Type == FurnitureType.MedBed) pt.CareBed = bed; else world.Recovery.Ward(pt, bed); }
            else pt.LaidSafe = true;
            world.Log.Add(world.Tick, LogKind.Work, $"{Ko.EulReul(pt.Name)} {(bed?.Name ?? "바닥")}에 눕혔다 — 깨어날 때까지", cm.Id);
            return true;
        }));
        return new Job(this, $"{pt.Name} {SurgerySystem.KindName(k.Kind)}", toils)
        {
            LogText = carry ? $"쓰러진 {Ko.EulReul(pt.Name)} 수술대로 옮긴다" : $"{pt.Name} {SurgerySystem.KindName(k.Kind)} — 수술실로",
            TargetRoom = table.Room, Target = table, Urgent = w.Grades.Now(pt) == InjuryGrade.Critical, InterruptMargin = 0.6f,
        };
    }
}

/// <summary>수술 보조: 수술대 건너편에 서서 거든다 (기구 · 흡인 · 등불 · 마취기를 손으로).</summary>
public sealed class AssistSurgeryActivity : Activity
{
    public override string Id => "assistsurgery";
    public override string Label => "수술 보조";

    private static SurgeryCase? Mine(CrewMember c, World w)
    {
        foreach (var k in w.Surgery.Cases) if (k.Assistant == c.Id && k.State is CaseState.Waiting or CaseState.Prep or CaseState.Operating) return k;
        return null;
    }

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside) return (0f, "—");
        var k = Mine(c, w);
        if (k == null || k.Surgeon < 0) return (0f, "—");
        var pt = w.Crew.FirstOrDefault(x => x.Id == k.Patient);
        if (pt == null || pt.Dead) return (0f, "—");
        if (k.State == CaseState.Waiting && c.Pose == Pose.Sleeping && w.Grades.Now(pt) != InjuryGrade.Critical) return (0f, "—");
        return (k.State == CaseState.Waiting ? 0.76f : 1.2f, $"{pt.Name} 수술 보조");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var k = Mine(c, w);
        if (k == null || w.Surgery.TableOf(k) is not Furniture table) return null;
        var tcell = table.Cells[0];
        var surgeon = w.Crew.FirstOrDefault(x => x.Id == k.Surgeon);
        Cell? side = Cell.Dirs8.Select(d => tcell + d).Where(x => w.Ship.IsOpenFloor(x) && dist.Reachable(x) && w.Ship.RoomAt(x) == table.Room && !w.IsSpotTaken(x, c) && (surgeon == null || surgeon.Destination != x))
            .OrderByDescending(x => surgeon == null ? 0 : Math.Abs(x.X - surgeon.Cell.X) + Math.Abs(x.Y - surgeon.Cell.Y)).ThenBy(x => x.X).ThenBy(x => x.Y).Cast<Cell?>().FirstOrDefault();
        if (side is not Cell at) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WaitToil(SimTime.Hours(5), Pose.Working, table.Center)
        {
            DoneWhen = (cm, world) => k.State is CaseState.Done or CaseState.Deferred || k.Assistant != cm.Id || k.State == CaseState.Waiting && k.Surgeon < 0,
        });
        return new Job(this, "수술 보조", toils) { LogText = "수술대 건너편에 선다", TargetRoom = table.Room, Target = table, InterruptMargin = 0.5f };
    }
}

/// <summary>수술을 받는다: 걸을 수 있으면 스스로 수술대에 눕는다 (마취가 들면 잠든다).</summary>
public sealed class SurgeryPatientActivity : Activity
{
    public override string Id => "surgerypatient";
    public override string Label => "수술 받기";

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside) return (0f, "—");
        var k = w.Surgery.CaseOf(c);
        if (k == null || k.Surgeon < 0 || k.State is CaseState.Deferred or CaseState.Done || w.Surgery.TableOf(k) == null) return (0f, "—");
        return (1.5f, $"{SurgerySystem.KindName(k.Kind)} — 수술대로");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var k = w.Surgery.CaseOf(c);
        if (k == null || w.Surgery.TableOf(k) is not Furniture table) return null;
        var cell = table.Cells[0];
        if (!dist.Reachable(cell) && c.Cell != cell) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(cell));
        toils.Add(new WaitToil(SimTime.Hours(6), Pose.Sleeping)
        {
            DoneWhen = (cm, world) => k.State is CaseState.Done or CaseState.Deferred || k.Surgeon < 0 && k.State == CaseState.Waiting,
        });
        var job = new Job(this, SurgerySystem.KindName(k.Kind), toils) { LogText = "수술대에 눕는다", TargetRoom = table.Room, InterruptMargin = 0.8f };
        return job.Reserve(table, c);
    }
}
